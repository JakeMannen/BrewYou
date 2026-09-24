using BrewYou.ApiService.Data.Entities;
using System.Security.Claims;

namespace BrewYou.ApiService.Auth;

public interface ITokenService
{
    string GenerateAccessToken(ApplicationUser user, IEnumerable<string>? roles = null);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}