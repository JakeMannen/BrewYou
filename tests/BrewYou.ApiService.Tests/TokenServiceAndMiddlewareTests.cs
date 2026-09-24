using BrewYou.ApiService.Auth;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Middleware;
using BrewYou.ApiService.Services;
using BrewYou.ApiService.Validation;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace BrewYou.ApiService.Tests;

public class TokenServiceAndMiddlewareTests
{
    private readonly IOptions<JwtOptions> _jwtOptions = Options.Create(new JwtOptions
    {
        Issuer = "BrewYouTestIssuer",
        Audience = "BrewYouTestAudience",
        SecretKey = "SuperSecretBrewYouKey_AtLeast32BytesLong!",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7
    });

    [Fact]
    public void TokenService_GenerateAccessToken_IncludesUserAndRoles()
    {
        var tokenService = new TokenService(_jwtOptions);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = "token_tester@brewyou.test",
            UserName = "TokenTester",
            DisplayName = "Token Display"
        };

        var token = tokenService.GenerateAccessToken(user, new[] { "Admin", "Brewer" });
        token.Should().NotBeNullOrWhiteSpace();

        var principal = tokenService.GetPrincipalFromExpiredToken(token);
        principal.Should().NotBeNull();
        principal!.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(user.Id);
        principal.FindFirstValue(ClaimTypes.Email).Should().Be(user.Email);
        principal.FindFirstValue(ClaimTypes.Name).Should().Be("TokenTester");
        principal.FindFirstValue("displayName").Should().Be("Token Display");

        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roles.Should().Contain(new[] { "Admin", "Brewer" });
    }

    [Fact]
    public void TokenService_GenerateRefreshToken_ReturnsRandom64ByteBase64String()
    {
        var tokenService = new TokenService(_jwtOptions);
        var rt1 = tokenService.GenerateRefreshToken();
        var rt2 = tokenService.GenerateRefreshToken();

        rt1.Should().NotBeNullOrWhiteSpace();
        rt2.Should().NotBeNullOrWhiteSpace();
        rt1.Should().NotBe(rt2);

        var bytes = Convert.FromBase64String(rt1);
        bytes.Length.Should().Be(64);
    }

    [Fact]
    public void TokenService_GetPrincipalFromExpiredToken_InvalidOrTamperedToken_ReturnsNull()
    {
        var tokenService = new TokenService(_jwtOptions);

        tokenService.GetPrincipalFromExpiredToken("invalid.jwt.token").Should().BeNull();
        tokenService.GetPrincipalFromExpiredToken("").Should().BeNull();

        // Token signed with different key
        var differentOptions = Options.Create(new JwtOptions
        {
            Issuer = "BrewYouTestIssuer",
            Audience = "BrewYouTestAudience",
            SecretKey = "CompletelyDifferentSecretKeyThatIsAtLeast32BytesLong!",
            AccessTokenExpirationMinutes = 15
        });
        var otherTokenService = new TokenService(differentOptions);
        var user = new ApplicationUser { Id = "test-user", Email = "test@brewyou.test" };
        var foreignToken = otherTokenService.GenerateAccessToken(user);

        tokenService.GetPrincipalFromExpiredToken(foreignToken).Should().BeNull();
    }

    [Fact]
    public async Task GlobalExceptionHandler_TryHandleAsync_Sets500AndWritesApiResponse()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        var exception = new InvalidOperationException("Something unexpected went wrong");
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().StartWith("application/json");

        bodyStream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(bodyStream);
        var bodyText = await reader.ReadToEndAsync();
        bodyText.Should().Contain("INTERNAL_SERVER_ERROR");
        bodyText.Should().Contain("An unexpected error occurred");
    }

    [Fact]
    public void UpdateEquipmentRequestValidator_ValidatesRules()
    {
        var validator = new UpdateEquipmentRequestValidator();

        // Valid update
        var valid = new UpdateEquipmentRequest(
            Name: "Updated Boiler",
            Type: EquipmentType.Boiler,
            Capacity: 50m,
            BrewerySetupId: Guid.NewGuid(),
            Subtype: EquipmentSubtype.AllInOne,
            CurrentVolume: 30m,
            Unit: VolumeUnit.Liters
        );
        validator.Validate(valid).IsValid.Should().BeTrue();

        // Empty setup ID
        var emptySetup = valid with { BrewerySetupId = Guid.Empty };
        var resSetup = validator.Validate(emptySetup);
        resSetup.IsValid.Should().BeFalse();
        resSetup.Errors.Should().Contain(e => e.PropertyName == "BrewerySetupId");

        // CurrentVolume > Capacity
        var overfill = valid with { CurrentVolume = 60m };
        var resOverfill = validator.Validate(overfill);
        resOverfill.IsValid.Should().BeFalse();
        resOverfill.Errors.Should().Contain(e => e.PropertyName == "CurrentVolume");

        // Negative volume
        var negVol = valid with { CurrentVolume = -5m };
        var resNegVol = validator.Validate(negVol);
        resNegVol.IsValid.Should().BeFalse();
        resNegVol.Errors.Should().Contain(e => e.PropertyName == "CurrentVolume");

        // Invalid subtype for type
        var badSubtype = valid with { Type = EquipmentType.Boiler, Subtype = EquipmentSubtype.ConicalFermenter };
        var resSubtype = validator.Validate(badSubtype);
        resSubtype.IsValid.Should().BeFalse();
        resSubtype.Errors.Should().Contain(e => e.PropertyName == "Subtype");
    }
}