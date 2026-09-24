using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace BrewYou.ApiService.Auth;

public class GoogleAuthValidator(
    IOptions<GoogleAuthOptions> options,
    ILogger<GoogleAuthValidator> logger) : IGoogleAuthValidator
{
    private readonly GoogleAuthOptions _options = options.Value;

    public async Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            logger.LogError("Google OAuth ClientId is not configured in Authentication:Google:ClientId.");
            return null;
        }

        var validationSettings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = [_options.ClientId]
        };

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings);
            if (payload == null)
            {
                logger.LogWarning("GoogleJsonWebSignature returned a null payload.");
                return null;
            }

            if (!payload.EmailVerified)
            {
                logger.LogWarning("Google ID token validated, but email {Email} is not verified by Google.", payload.Email);
                return null;
            }

            return new GoogleUserInfo(
                SubjectId: payload.Subject,
                Email: payload.Email,
                EmailVerified: payload.EmailVerified,
                Name: payload.Name,
                GivenName: payload.GivenName,
                FamilyName: payload.FamilyName,
                PictureUrl: payload.Picture,
                Locale: payload.Locale
            );
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning(ex, "Google ID token validation failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error validating Google ID token.");
            return null;
        }
    }
}