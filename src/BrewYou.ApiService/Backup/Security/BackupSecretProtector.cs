using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace BrewYou.ApiService.Backup.Security;

public static class BackupSecretProtector
{
    public const int DefaultIterations = 600_000;
    public const int MinAllowedIterations = 10_000;
    public const int MaxAllowedIterations = 600_000;
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    public static string GenerateSaltBase64()
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        return Convert.ToBase64String(salt);
    }

    public static byte[] DeriveKey(string passphrase, string saltBase64, int iterations = DefaultIterations)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var passwordBytes = Encoding.UTF8.GetBytes(passphrase);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                Math.Clamp(iterations, MinAllowedIterations, MaxAllowedIterations),
                HashAlgorithmName.SHA256,
                KeySizeBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public static string EncryptSecret(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        try
        {
            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

            var combined = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(cipherBytes, 0, combined, NonceSizeBytes + TagSizeBytes, cipherBytes.Length);

            return Convert.ToBase64String(combined);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public static bool TryDecryptSecret(string encryptedBase64, byte[] key, out string? plaintext)
    {
        plaintext = null;
        if (string.IsNullOrWhiteSpace(encryptedBase64))
        {
            return false;
        }

        byte[]? plainBytes = null;
        try
        {
            var combined = Convert.FromBase64String(encryptedBase64);
            if (combined.Length < NonceSizeBytes + TagSizeBytes)
            {
                return false;
            }

            var nonce = combined.AsSpan(0, NonceSizeBytes);
            var tag = combined.AsSpan(NonceSizeBytes, TagSizeBytes);
            var cipherBytes = combined.AsSpan(NonceSizeBytes + TagSizeBytes);
            plainBytes = new byte[cipherBytes.Length];

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

            plaintext = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (plainBytes != null)
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
    }

    public static string? UnprotectServerSecret(string? protectedValue, IDataProtector? protector)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        if (protector == null)
        {
            return protectedValue;
        }

        try
        {
            return protector.Unprotect(protectedValue);
        }
        catch
        {
            // Fail closed: never fall back to returning raw server-protected ciphertext as plaintext
            return null;
        }
    }

    public static string? ProtectServerSecret(string? plainValue, IDataProtector? protector)
    {
        if (string.IsNullOrWhiteSpace(plainValue))
        {
            return null;
        }

        return protector != null ? protector.Protect(plainValue) : plainValue;
    }

    public static string? PrepareConfigJsonForExport(
        string? rawConfigJson,
        bool includeSecrets,
        byte[]? encryptionKey,
        IDataProtector? serverProtector)
    {
        if (string.IsNullOrWhiteSpace(rawConfigJson))
        {
            return rawConfigJson;
        }

        try
        {
            var node = JsonNode.Parse(rawConfigJson);
            if (node is not JsonObject obj)
            {
                return rawConfigJson;
            }

            if (obj.ContainsKey("password"))
            {
                var rawPassword = obj["password"]?.GetValue<string>();
                obj.Remove("password");

                if (!string.IsNullOrWhiteSpace(rawPassword))
                {
                    obj["hasPassword"] = true;
                    if (includeSecrets && encryptionKey != null)
                    {
                        var unprotected = UnprotectServerSecret(rawPassword, serverProtector);
                        if (!string.IsNullOrWhiteSpace(unprotected))
                        {
                            obj["passwordEncrypted"] = EncryptSecret(unprotected, encryptionKey);
                        }
                    }
                }
            }

            return obj.ToJsonString();
        }
        catch
        {
            return null;
        }
    }

    public static (string? ConfigJson, bool DecryptionFailed) RestoreConfigJsonFromBackup(
        string? backupConfigJson,
        byte[]? encryptionKey,
        IDataProtector? serverProtector)
    {
        if (string.IsNullOrWhiteSpace(backupConfigJson))
        {
            return (backupConfigJson, false);
        }

        if (backupConfigJson.Length > 4000)
        {
            return (null, false);
        }

        try
        {
            var node = JsonNode.Parse(backupConfigJson);
            if (node is not JsonObject obj)
            {
                return (null, false);
            }

            // Defense-in-depth: Strip any raw plaintext password fields from untrusted backup JSON
            var plaintextPasswordKeys = obj
                .Where(kvp => string.Equals(kvp.Key, "password", StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in plaintextPasswordKeys)
            {
                obj.Remove(key);
            }

            bool failed = false;
            if (obj.ContainsKey("passwordEncrypted"))
            {
                var enc = obj["passwordEncrypted"]?.GetValue<string>();
                obj.Remove("passwordEncrypted");

                if (!string.IsNullOrWhiteSpace(enc))
                {
                    if (encryptionKey != null && TryDecryptSecret(enc, encryptionKey, out var plain) && !string.IsNullOrWhiteSpace(plain))
                    {
                        obj["password"] = ProtectServerSecret(plain, serverProtector);
                    }
                    else
                    {
                        failed = true;
                    }
                }
            }

            return (obj.ToJsonString(), failed);
        }
        catch
        {
            return (null, false);
        }
    }
}