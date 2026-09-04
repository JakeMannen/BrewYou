using System.Security.Claims;
using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Auth;

public interface ITokenService
{
    string GenerateAccessToken(ApplicationUser user, IEnumerable<string>? roles = null);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
