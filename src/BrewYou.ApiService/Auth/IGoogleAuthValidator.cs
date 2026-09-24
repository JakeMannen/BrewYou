namespace BrewYou.ApiService.Auth;

public record GoogleUserInfo(
    string SubjectId,
    string Email,
    bool EmailVerified,
    string? Name,
    string? GivenName,
    string? FamilyName,
    string? PictureUrl,
    string? Locale
);

public interface IGoogleAuthValidator
{
    Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}