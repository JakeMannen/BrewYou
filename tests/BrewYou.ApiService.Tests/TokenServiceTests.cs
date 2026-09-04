using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BrewYou.ApiService.Auth;
using BrewYou.ApiService.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BrewYou.ApiService.Tests;

public class TokenServiceTests
{
    private readonly TokenService _tokenService;
    private readonly JwtOptions _options;

    public TokenServiceTests()
    {
        _options = new JwtOptions
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SecretKey = "SuperSecretTestKeyThatIsVeryLongAndSecure123!",
            AccessTokenExpirationMinutes = 30,
            RefreshTokenExpirationDays = 7
        };

        _tokenService = new TokenService(Options.Create(_options));
    }

    [Fact]
    public void GenerateAccessToken_ShouldProduceValidJwtWithExpectedClaims()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = "brewer@example.com",
            UserName = "brewer@example.com",
            DisplayName = "Master Brewer"
        };

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Issuer.Should().Be(_options.Issuer);
        jwt.Audiences.Should().Contain(_options.Audience);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);
        jwt.Claims.Should().Contain(c => c.Type == "displayName" && c.Value == "Master Brewer");
    }

    [Fact]
    public void GenerateRefreshToken_ShouldProduceCryptographicallyRandomString()
    {
        // Act
        var token1 = _tokenService.GenerateRefreshToken();
        var token2 = _tokenService.GenerateRefreshToken();

        // Assert
        token1.Should().NotBeNullOrWhiteSpace();
        token2.Should().NotBeNullOrWhiteSpace();
        token1.Should().NotBe(token2);
    }
}
