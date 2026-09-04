using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BrewYou.ApiService.Tests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetIngredients_ReturnsSeededCatalog()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/ingredients");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ingredients = await response.Content.ReadFromJsonAsync<List<IngredientDto>>();
        ingredients.Should().NotBeNull();
        ingredients.Should().NotBeEmpty();
        ingredients!.Should().Contain(i => i.Name.Contains("Pale Malt"));
        ingredients!.Should().Contain(i => i.Name == "Citra");
        ingredients!.Should().Contain(i => i.Name.Contains("SafAle US-05"));
    }

    [Fact]
    public async Task AuthAndRecipeFlow_EndToEnd_Succeeds()
    {
        // 1. Register
        var email = $"testbrewer_{Guid.NewGuid():N}@example.com";
        var password = "SecurePassword123!";
        var registerRequest = new RegisterRequest(email, password, "Head Brewer");

        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        regResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var authData = await regResponse.Content.ReadFromJsonAsync<AuthResponse>();
        authData.Should().NotBeNull();
        authData!.AccessToken.Should().NotBeNullOrWhiteSpace();
        authData.User.Email.Should().Be(email);
        authData.User.DisplayName.Should().Be("Head Brewer");

        // 2. Duplicate registration returns 409 Conflict
        var dupResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        dupResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Access /auth/me with Bearer token
        using var authedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        authedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);
        var meResponse = await _client.SendAsync(authedRequest);
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Get seeded ingredients to form a recipe
        var ingredientsResponse = await _client.GetFromJsonAsync<List<IngredientDto>>("/api/v1/ingredients");
        ingredientsResponse.Should().NotBeNull();

        var paleMalt = ingredientsResponse!.First(i => i.Type == IngredientType.Fermentable);
        var citra = ingredientsResponse!.First(i => i.Type == IngredientType.Hop);
        var yeast = ingredientsResponse!.First(i => i.Type == IngredientType.Yeast);

        // 6. Test Calculation Endpoint
        var calcRequest = new CalculateRecipeRequest(
            BatchSizeLiters: 20.0m,
            EfficiencyPercent: 72.0m,
            BoilTimeMinutes: 60,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(paleMalt.Id, 5.0m, "kg", null, IngredientUsage.Mash, null),
                new(citra.Id, 40.0m, "g", 60, IngredientUsage.Boil, null),
                new(yeast.Id, 11.5m, "g", null, IngredientUsage.Primary, null)
            }
        );

        var calcResponse = await _client.PostAsJsonAsync("/api/v1/recipes/calculate", calcRequest);
        calcResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var calcResult = await calcResponse.Content.ReadFromJsonAsync<CalculateRecipeResponse>();
        calcResult.Should().NotBeNull();
        calcResult!.OriginalGravity.Should().BeGreaterThan(1.040m);
        calcResult.AlcoholByVolume.Should().BeGreaterThan(4.0m);
        calcResult.BitternessIbu.Should().BeGreaterThan(20.0m);

        // 7. Create Recipe
        var createRecipeRequest = new CreateRecipeRequest(
            Name: "Citra West Coast Pale Ale",
            Description: "Crisp and citrusy pale ale brewed with 100% Citra hops.",
            BeerStyle: "American Pale Ale",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72.0m,
            IsPublic: true,
            Ingredients: calcRequest.Ingredients
        );

        using var createRecipeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/recipes")
        {
            Content = JsonContent.Create(createRecipeRequest)
        };
        createRecipeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);

        var createRecipeResponse = await _client.SendAsync(createRecipeReq);
        createRecipeResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdRecipe = await createRecipeResponse.Content.ReadFromJsonAsync<RecipeDetailDto>();
        createdRecipe.Should().NotBeNull();
        createdRecipe!.Name.Should().Be("Citra West Coast Pale Ale");
        createdRecipe.Ingredients.Should().HaveCount(3);
        createdRecipe.OriginalGravity.Should().Be(calcResult.OriginalGravity);
    }
}
