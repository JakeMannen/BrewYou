using BrewYou.ApiService.Common;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class ValidationAndAuthPolicyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ValidationAndAuthPolicyTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CalculateRecipe_NegativeBatchSize_ReturnsBadRequestEnvelope()
    {
        var invalid = new CalculateRecipeRequest(
            BatchSizeLiters: -10m,
            EfficiencyPercent: 72m,
            BoilTimeMinutes: 60,
            Ingredients: new List<RecipeIngredientInputDto>()
        );

        var response = await _client.PostAsJsonAsync("/api/v1/recipes/calculate", invalid);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse>();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("VALIDATION_ERROR");
        envelope.Error.Details.Should().Contain(d => d.Field == "BatchSizeLiters");
    }

    [Fact]
    public async Task DeleteRecipe_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.DeleteAsync($"/api/v1/recipes/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteRecipe_OtherUserRecipe_ReturnsForbiddenEnvelope()
    {
        // 1. User A registers and creates a private recipe
        var emailA = $"brewer_a_{Guid.NewGuid():N}@example.com";
        var regA = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(emailA, "Pass123!", "Brewer A"));
        var authA = (await regA.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;

        var createReq = new CreateRecipeRequest(
            Name: "Private Secret Stash",
            Description: null,
            BeerStyle: "Imperial Stout",
            BatchSizeLiters: 15m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 70m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>()
        );

        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/recipes")
        {
            Content = JsonContent.Create(createReq)
        };
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authA.AccessToken);
        var postRes = await _client.SendAsync(postReq);
        postRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await postRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 2. User B registers
        var emailB = $"brewer_b_{Guid.NewGuid():N}@example.com";
        var regB = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(emailB, "Pass123!", "Brewer B"));
        var authB = (await regB.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;

        // 3. User B tries to delete User A's private recipe -> 403 Forbidden
        using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/recipes/{recipe.Id}");
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authB.AccessToken);
        var delRes = await _client.SendAsync(delReq);

        delRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var envelope = await delRes.Content.ReadFromJsonAsync<ApiResponse>();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task RefreshToken_WithValidAndInvalidTokens_HandlesCorrectly()
    {
        // 1. Register a user
        var email = $"refresh_user_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "StrongPass123!", "Refresh User"));
        reg.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;

        // 2. Refresh with valid token
        var refreshReq = new RefreshTokenRequest(auth.RefreshToken);
        var refreshRes = await _client.PostAsJsonAsync("/api/v1/auth/refresh", refreshReq);
        refreshRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshEnv = await refreshRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        refreshEnv!.Success.Should().BeTrue();
        refreshEnv.Data!.AccessToken.Should().NotBeNullOrEmpty();
        refreshEnv.Data.RefreshToken.Should().NotBeNullOrEmpty();

        // 3. Refresh with invalid token returns unauthorized / bad request
        var invalidReq = new RefreshTokenRequest("invalid-token-value");
        var invalidRes = await _client.PostAsJsonAsync("/api/v1/auth/refresh", invalidReq);
        invalidRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 4. Refresh with empty token returns 401
        var emptyReq = new RefreshTokenRequest("");
        var emptyRes = await _client.PostAsJsonAsync("/api/v1/auth/refresh", emptyReq);
        emptyRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 5. Logout revokes token
        using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshEnv.Data.AccessToken);
        var logoutRes = await _client.SendAsync(logoutReq);
        logoutRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Refreshing with old or revoked token now fails
        var revokedReq = new RefreshTokenRequest(refreshEnv.Data.RefreshToken);
        var revokedRes = await _client.PostAsJsonAsync("/api/v1/auth/refresh", revokedReq);
        revokedRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}