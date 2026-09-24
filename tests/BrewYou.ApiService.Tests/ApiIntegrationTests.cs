using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

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
    public async Task ScalarApiReference_ReturnsSuccess_InDevelopment()
    {
        var responseScalar = await _client.GetAsync("/scalar");
        responseScalar.StatusCode.Should().Be(HttpStatusCode.OK);
        var contentScalar = await responseScalar.Content.ReadAsStringAsync();
        contentScalar.Should().Contain("BrewYou API Documentation");

        var responseScalarV1 = await _client.GetAsync("/scalar/v1");
        responseScalarV1.StatusCode.Should().Be(HttpStatusCode.OK);
        var contentScalarV1 = await responseScalarV1.Content.ReadAsStringAsync();
        contentScalarV1.Should().Contain("BrewYou API Documentation");
    }

    [Fact]
    public async Task OpenApiDocument_ContainsMitLicenseMetadata()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonContent = await response.Content.ReadAsStringAsync();
        jsonContent.Should().Contain("\"license\"");
        jsonContent.Should().Contain("\"name\": \"MIT\"");
        jsonContent.Should().Contain("https://opensource.org/licenses/MIT");
    }

    [Fact]
    public async Task GetIngredients_ReturnsSeededCatalog_WithUniformEnvelopeAndPagination()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/ingredients");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Pagination.Should().NotBeNull();
        envelope.Pagination!.Total.Should().BeGreaterThan(0);

        var ingredients = envelope.Data;
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
        var registerRequest = new RegisterRequest(email, password, "Head Brewer", "sv");

        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        regResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var regEnvelope = await regResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        regEnvelope.Should().NotBeNull();
        regEnvelope!.Success.Should().BeTrue();
        var authData = regEnvelope.Data!;
        authData.AccessToken.Should().NotBeNullOrWhiteSpace();
        authData.User.Email.Should().Be(email);
        authData.User.DisplayName.Should().Be("Head Brewer");
        authData.User.PreferredLanguage.Should().Be("sv");

        // 2. Duplicate registration returns 409 Conflict with error envelope
        var dupResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        dupResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var dupEnvelope = await dupResponse.Content.ReadFromJsonAsync<ApiResponse>();
        dupEnvelope!.Success.Should().BeFalse();
        dupEnvelope.Error!.Code.Should().Be("EMAIL_CONFLICT");

        // 3. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginEnvelope = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        loginEnvelope!.Success.Should().BeTrue();

        // 4. Access /auth/me with Bearer token
        using var authedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        authedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);
        var meResponse = await _client.SendAsync(authedRequest);
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var meEnvelope = await meResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        meEnvelope!.Success.Should().BeTrue();
        meEnvelope.Data!.PreferredLanguage.Should().Be("sv");

        // 4b. Update language preference to "en"
        using var updateLangReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/auth/me/language")
        {
            Content = JsonContent.Create(new UpdateLanguageRequest("en"))
        };
        updateLangReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);
        var updateLangResponse = await _client.SendAsync(updateLangReq);
        updateLangResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateLangEnvelope = await updateLangResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        updateLangEnvelope!.Success.Should().BeTrue();
        updateLangEnvelope.Data!.PreferredLanguage.Should().Be("en");

        // 5. Get seeded ingredients to form a recipe
        var ingredientsResponse = await _client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients");
        ingredientsResponse.Should().NotBeNull();
        ingredientsResponse!.Success.Should().BeTrue();

        var ingredients = ingredientsResponse.Data!;
        var paleMalt = ingredients.First(i => i.Name.Contains("Pale Malt"));
        var citra = ingredients.First(i => i.Type == IngredientType.Hop);
        var yeast = ingredients.First(i => i.Type == IngredientType.Yeast);

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
        var calcEnvelope = await calcResponse.Content.ReadFromJsonAsync<ApiResponse<CalculateRecipeResponse>>();
        calcEnvelope.Should().NotBeNull();
        calcEnvelope!.Success.Should().BeTrue();
        var calcResult = calcEnvelope.Data!;
        calcResult.OriginalGravity.Should().BeGreaterThan(1.040m);
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

        var createEnvelope = await createRecipeResponse.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        createEnvelope.Should().NotBeNull();
        createEnvelope!.Success.Should().BeTrue();
        var createdRecipe = createEnvelope.Data!;
        createdRecipe.Name.Should().Be("Citra West Coast Pale Ale");
        createdRecipe.Ingredients.Should().HaveCount(3);
        createdRecipe.OriginalGravity.Should().Be(calcResult.OriginalGravity);
    }
}