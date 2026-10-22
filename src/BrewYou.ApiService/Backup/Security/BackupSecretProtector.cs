using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace BrewYou.ApiService.Backup.Security;

public static class BackupSecretProtector
{
    public const int DefaultIterations = 600_000;
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
        return Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes,
            salt,
            Math.Clamp(iterations, 10_000, 2_000_000),
            HashAlgorithmName.SHA256,
            KeySizeBytes);
    }

    public static string EncryptSecret(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var combined = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(cipherBytes, 0, combined, NonceSizeBytes + TagSizeBytes, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    public static bool TryDecryptSecret(string encryptedBase64, byte[] key, out string? plaintext)
    {
        plaintext = null;
        if (string.IsNullOrWhiteSpace(encryptedBase64))
        {
            return false;
        }

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
            var plainBytes = new byte[cipherBytes.Length];

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

            plaintext = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch
        {
            return false;
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
            return protectedValue;
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

        try
        {
            var node = JsonNode.Parse(backupConfigJson);
            if (node is not JsonObject obj)
            {
                return (backupConfigJson, false);
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