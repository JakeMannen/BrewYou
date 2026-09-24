using BrewYou.ApiService.Auth;
using BrewYou.ApiService.Common;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class TestGoogleAuthValidator : IGoogleAuthValidator
{
    public Func<string, Task<GoogleUserInfo?>>? ValidatorFunc { get; set; }

    public Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (ValidatorFunc != null)
        {
            return ValidatorFunc(idToken);
        }

        if (idToken == "valid-new-google-token")
        {
            return Task.FromResult<GoogleUserInfo?>(new GoogleUserInfo(
                SubjectId: "google-sub-new-12345",
                Email: $"brewer_google_{Guid.NewGuid():N}@example.com",
                EmailVerified: true,
                Name: "Google Master Brewer",
                GivenName: "Google",
                FamilyName: "Brewer",
                PictureUrl: "https://example.com/photo.jpg",
                Locale: "en"
            ));
        }

        return Task.FromResult<GoogleUserInfo?>(null);
    }
}

public class GoogleAuthTestFactory : WebApplicationFactory<Program>
{
    public TestGoogleAuthValidator Validator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IGoogleAuthValidator>(Validator);
        });
    }
}

public class GoogleAuthEndpointTests : IClassFixture<GoogleAuthTestFactory>
{
    private readonly GoogleAuthTestFactory _factory;
    private readonly HttpClient _client;

    public GoogleAuthEndpointTests(GoogleAuthTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GoogleAuth_NewUser_CreatesApplicationUserAndReturnsTokensAndCookie()
    {
        // Arrange
        var testEmail = $"new_google_brewer_{Guid.NewGuid():N}@example.com";
        _factory.Validator.ValidatorFunc = token => Task.FromResult<GoogleUserInfo?>(new GoogleUserInfo(
            SubjectId: "sub-" + Guid.NewGuid().ToString("N"),
            Email: testEmail,
            EmailVerified: true,
            Name: "Fresh Google Brewer",
            GivenName: "Fresh",
            FamilyName: "Brewer",
            PictureUrl: "https://lh3.googleusercontent.com/a/photo",
            Locale: "sv"
        ));

        var request = new GoogleAuthRequest("valid-token", "sv");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeNull();

        var authData = envelope.Data!;
        authData.AccessToken.Should().NotBeNullOrWhiteSpace();
        authData.User.Email.Should().Be(testEmail);
        authData.User.DisplayName.Should().Be("Fresh Google Brewer");
        authData.User.PreferredLanguage.Should().Be("sv");

        // Verify Set-Cookie header contains refreshToken
        response.Headers.Contains("Set-Cookie").Should().BeTrue();
        var setCookie = string.Join(";", response.Headers.GetValues("Set-Cookie"));
        setCookie.Should().Contain("refreshToken=");
        setCookie.Should().Contain("httponly");
    }

    [Fact]
    public async Task GoogleAuth_ExistingPasswordUser_LinksAccountAndIssuesTokens()
    {
        // Arrange: 1. Register user via password
        var existingEmail = $"standard_brewer_{Guid.NewGuid():N}@example.com";
        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(existingEmail, "StrongPassword123!", "Standard Brewer", "en"));
        regResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. User logs in via Google with the same email
        var googleSubId = "google-sub-" + Guid.NewGuid().ToString("N");
        _factory.Validator.ValidatorFunc = token => Task.FromResult<GoogleUserInfo?>(new GoogleUserInfo(
            SubjectId: googleSubId,
            Email: existingEmail,
            EmailVerified: true,
            Name: "Standard Brewer via Google",
            GivenName: "Standard",
            FamilyName: "Brewer",
            PictureUrl: null,
            Locale: "en"
        ));

        // Act
        var googleResponse = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleAuthRequest("google-token", "en"));

        // Assert
        googleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await googleResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.User.Email.Should().Be(existingEmail);

        // 3. Subsequent login with the same Google provider key succeeds
        var subsequentResponse = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleAuthRequest("google-token-again", "en"));
        subsequentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GoogleAuth_InvalidToken_ReturnsUnauthorizedWithInvalidGoogleTokenCode()
    {
        // Arrange
        _factory.Validator.ValidatorFunc = _ => Task.FromResult<GoogleUserInfo?>(null);

        var request = new GoogleAuthRequest("invalid-or-forged-jwt");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("INVALID_GOOGLE_TOKEN");
    }

    [Fact]
    public async Task GoogleAuth_EmptyToken_ReturnsBadRequestWithValidationError()
    {
        // Arrange
        var request = new GoogleAuthRequest("");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("VALIDATION_ERROR");
        envelope.Error.Details.Should().Contain(d => d.Field == "IdToken");
    }

    [Fact]
    public async Task GoogleAuth_AuthenticatedUser_CanAccessProtectedEndpoints()
    {
        // Arrange
        var testEmail = $"authenticated_google_{Guid.NewGuid():N}@example.com";
        _factory.Validator.ValidatorFunc = _ => Task.FromResult<GoogleUserInfo?>(new GoogleUserInfo(
            SubjectId: "sub-" + Guid.NewGuid().ToString("N"),
            Email: testEmail,
            EmailVerified: true,
            Name: "Authed Google Brewer",
            GivenName: "Authed",
            FamilyName: "Brewer",
            PictureUrl: null,
            Locale: "en"
        ));

        var authResponse = await _client.PostAsJsonAsync("/api/v1/auth/google", new GoogleAuthRequest("valid-token"));
        var authEnvelope = await authResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        var token = authEnvelope!.Data!.AccessToken;

        // Act: Call /api/v1/auth/me with Bearer token
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meResponse = await _client.SendAsync(meRequest);

        // Assert
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var meEnvelope = await meResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        meEnvelope!.Success.Should().BeTrue();
        meEnvelope.Data!.Email.Should().Be(testEmail);
        meEnvelope.Data.DisplayName.Should().Be("Authed Google Brewer");
    }
}