using BrewYou.ApiService.Data.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BrewYou.ApiService.Auth;

public class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IJwtSigningKeyProvider? _signingKeyProvider;

    public TokenService(IOptions<JwtOptions> options, IJwtSigningKeyProvider? signingKeyProvider = null)
    {
        _options = options.Value;
        _signingKeyProvider = signingKeyProvider;
    }

    public string GenerateAccessToken(ApplicationUser user, IEnumerable<string>? roles = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? "Brewer"),
            new("displayName", user.DisplayName ?? user.UserName ?? "Brewer")
        };

        if (roles != null)
        {
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var key = GetSigningKey();
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenExpirationMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = GetSigningKey(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = false // Critical to allow reading claims from expired access token
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwtSecurityToken ||
                !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        if (_signingKeyProvider is not null)
        {
            return _signingKeyProvider.GetSigningKey();
        }

        if (string.IsNullOrWhiteSpace(_options.SecretKey) || Encoding.UTF8.GetByteCount(_options.SecretKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SecretKey must be configured and at least 256 bits (32 bytes) long.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
    }
}