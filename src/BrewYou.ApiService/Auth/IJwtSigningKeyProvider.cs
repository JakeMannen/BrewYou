using Microsoft.IdentityModel.Tokens;

namespace BrewYou.ApiService.Auth;

public interface IJwtSigningKeyProvider
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    string GetSecretKey();
    SymmetricSecurityKey GetSigningKey();
}