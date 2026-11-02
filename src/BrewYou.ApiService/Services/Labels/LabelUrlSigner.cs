using BrewYou.ApiService.Auth;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace BrewYou.ApiService.Services.Labels;

public interface ILabelUrlSigner
{
    string GenerateSignature(Guid recipeId, string variant, int size, int version, long expiresAtEpoch);
    bool VerifySignature(Guid recipeId, string variant, int size, int version, long expiresAtEpoch, string signature);
    string BuildSignedImageUrl(string baseUrl, Guid recipeId, string variant, int size, int version, string format = "webp");
    string BuildDefaultImageUrl(string baseUrl, string variant, int size, string defaultSha, string format = "webp");
}

public class LabelUrlSigner : ILabelUrlSigner
{
    private readonly byte[] _signingKey;

    public LabelUrlSigner(IOptions<JwtOptions> jwtOptions)
    {
        var secret = jwtOptions.Value.SecretKey;
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException("Jwt:SecretKey must be configured to sign label URLs.");
        }

        // Derive dedicated signing key using HKDF-SHA256
        _signingKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: Encoding.UTF8.GetBytes(secret),
            outputLength: 32,
            salt: "BrewYou.LabelUrlSigner.Salt"u8.ToArray(),
            info: "BrewYou.LabelUrlSigner.HmacKey"u8.ToArray());
    }

    public string GenerateSignature(Guid recipeId, string variant, int size, int version, long expiresAtEpoch)
    {
        var payload = FormattableString.Invariant($"{recipeId:D}:{variant.ToLowerInvariant()}:{size}:{version}:{expiresAtEpoch}");
        using var hmac = new HMACSHA256(_signingKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool VerifySignature(Guid recipeId, string variant, int size, int version, long expiresAtEpoch, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var currentEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (currentEpoch > expiresAtEpoch)
        {
            return false; // Expired
        }

        var expectedSig = GenerateSignature(recipeId, variant, size, version, expiresAtEpoch);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSig),
            Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    public string BuildSignedImageUrl(string baseUrl, Guid recipeId, string variant, int size, int version, string format = "webp")
    {
        // 24-hour expiry aligned to midnight UTC to optimize CDN & browser caching
        var expiresAt = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2), TimeSpan.Zero).ToUnixTimeSeconds();
        var sig = GenerateSignature(recipeId, variant, size, version, expiresAt);
        var cleanBase = baseUrl.TrimEnd('/');
        return $"{cleanBase}/api/v1/labels/{recipeId}/image/{variant.ToLowerInvariant()}?size={size}&format={format}&v={version}&exp={expiresAt}&sig={sig}";
    }

    public string BuildDefaultImageUrl(string baseUrl, string variant, int size, string defaultSha, string format = "webp")
    {
        var shortSha = defaultSha.Length >= 8 ? defaultSha[..8] : defaultSha;
        var cleanBase = baseUrl.TrimEnd('/');
        return $"{cleanBase}/api/v1/labels/default/image/{variant.ToLowerInvariant()}?size={size}&format={format}&v={shortSha}";
    }

    public static string ComputeSha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string ComputeSha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}