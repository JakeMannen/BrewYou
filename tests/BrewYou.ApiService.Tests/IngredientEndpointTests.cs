using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class IngredientEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public IngredientEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FilterIngredients_ByTypeAndSearch_WorksCorrectly()
    {
        // 1. Filter by Hop
        var hopResponse = await _client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Hop");
        hopResponse.Should().NotBeNull();
        hopResponse!.Success.Should().BeTrue();
        hopResponse.Data.Should().OnlyContain(i => i.Type == IngredientType.Hop);

        // 2. Filter by Search keyword
        var searchResponse = await _client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?search=citra");
        searchResponse.Should().NotBeNull();
        searchResponse!.Success.Should().BeTrue();
        searchResponse.Data.Should().Contain(i => i.Name == "Citra");

        // 3. Pagination limits items
        var pagedResponse = await _client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?page=1&limit=2");
        pagedResponse.Should().NotBeNull();
        pagedResponse!.Success.Should().BeTrue();
        pagedResponse.Data!.Count.Should().BeLessThanOrEqualTo(2);
        pagedResponse.Pagination!.Limit.Should().Be(2);
        pagedResponse.Pagination.Total.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task GetIngredientById_ReturnsNotFound_ForMissingId()
    {
        var missingId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/ingredients/{missingId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task CreateIngredient_ValidationFailure_ReturnsBadRequestEnvelope()
    {
        // Missing name should fail validation
        var invalid = new CreateIngredientRequest("", IngredientType.Hop, null, null, null, null, null);

        var regEmail = $"hophead_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(regEmail, "Pass123!", "Hophead"));
        var auth = await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingredients")
        {
            Content = JsonContent.Create(invalid)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse>();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("VALIDATION_ERROR");
        envelope.Error.Details.Should().Contain(d => d.Field == "Name");

        // XSS characters in Name or Description should fail
        var xssRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingredients")
        {
            Content = JsonContent.Create(new CreateIngredientRequest(
                "<script>alert(1)</script>",
                IngredientType.Fermentable,
                1.037m,
                2.0m,
                null,
                null,
                "<img src=x onerror=alert(1)>"
            ))
        };
        xssRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Data!.AccessToken);

        var xssResponse = await _client.SendAsync(xssRequest);
        xssResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var xssEnvelope = await xssResponse.Content.ReadFromJsonAsync<ApiResponse>();
        xssEnvelope!.Success.Should().BeFalse();
        xssEnvelope.Error!.Details.Should().Contain(d => d.Field == "Name");
        xssEnvelope.Error.Details.Should().Contain(d => d.Field == "Description");
    }

    [Fact]
    public async Task GetCatalogIngredients_ContainsExpandedCommonMarketIngredients_WithAccurateMetrics()
    {
        // 1. Fetch full catalog list
        var response = await _client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?limit=250");
        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        var allIngredients = response.Data!;

        // 2. Assert total catalog size and type distribution
        allIngredients.Count.Should().BeGreaterThanOrEqualTo(54);
        var fermentables = allIngredients.Where(i => i.Type == IngredientType.Fermentable).ToList();
        var hops = allIngredients.Where(i => i.Type == IngredientType.Hop).ToList();
        var yeasts = allIngredients.Where(i => i.Type == IngredientType.Yeast).ToList();

        fermentables.Count.Should().BeGreaterThanOrEqualTo(22);
        hops.Count.Should().BeGreaterThanOrEqualTo(20);
        yeasts.Count.Should().BeGreaterThanOrEqualTo(12);

        // 3. Verify specific domain metrics certified by the Master Brewer
        var marisOtter = fermentables.FirstOrDefault(f => f.Name == "Maris Otter");
        marisOtter.Should().NotBeNull();
        marisOtter!.PotentialGravity.Should().Be(1.038m);
        marisOtter.ColorSrm.Should().Be(3.0m);

        var carafa = fermentables.FirstOrDefault(f => f.Name == "Carafa Special III");
        carafa.Should().NotBeNull();
        carafa!.PotentialGravity.Should().Be(1.032m);
        carafa.ColorSrm.Should().Be(520.0m);

        var galaxy = hops.FirstOrDefault(h => h.Name == "Galaxy");
        galaxy.Should().NotBeNull();
        galaxy!.AlphaAcidPercent.Should().Be(14.5m);

        var hallertau = hops.FirstOrDefault(h => h.Name == "Hallertau Mittelfrüh");
        hallertau.Should().NotBeNull();
        hallertau!.AlphaAcidPercent.Should().Be(4.0m);

        var w3470 = yeasts.FirstOrDefault(y => y.Name == "Fermentis SafLager W-34/70");
        w3470.Should().NotBeNull();
        w3470!.AttenuationPercent.Should().Be(83.0m);

        var belleSaison = yeasts.FirstOrDefault(y => y.Name == "Lallemand LalBrew Belle Saison");
        belleSaison.Should().NotBeNull();
        belleSaison!.AttenuationPercent.Should().Be(90.0m);

        var kveik = yeasts.FirstOrDefault(y => y.Name == "Lallemand LalBrew Voss Kveik");
        kveik.Should().NotBeNull();
        kveik!.AttenuationPercent.Should().Be(79.0m);
    }

    [Fact]
    public async Task GetAll_IncludesSeededMiscellaneousAndWaterAgents()
    {
        var response = await _client.GetAsync("/api/v1/ingredients?type=Other");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();

        var miscs = body.Data!;
        miscs.Should().HaveCountGreaterThanOrEqualTo(10);
        miscs.Should().Contain(i => i.Name == "Irish Moss");
        miscs.Should().Contain(i => i.Name == "Whirlfloc");
        miscs.Should().Contain(i => i.Name == "Gypsum (Calcium Sulfate)");
        miscs.Should().Contain(i => i.Name == "Calcium Chloride");
        miscs.Should().Contain(i => i.Name == "Yeast Nutrient");
    }

    [Fact]
    public async Task DbSeeder_SeedAsync_IsIdempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

        var countBefore = await db.Ingredients.CountAsync();
        countBefore.Should().BeGreaterThanOrEqualTo(54);

        // Run seed again
        await DbSeeder.SeedAsync(db);

        var countAfter = await db.Ingredients.CountAsync();
        countAfter.Should().Be(countBefore);
    }

    [Fact]
    public async Task CreateIngredient_Unauthenticated_ReturnsUnauthorized()
    {
        var request = new CreateIngredientRequest("Unauthorized Hop", IngredientType.Hop, null, null, 10.5m, null, "Test");
        var response = await _client.PostAsJsonAsync("/api/v1/ingredients", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateIngredient_AllFourTypes_SucceedsAndPersistsMetrics()
    {
        // Register a test user
        var email = $"brewer_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Pass123!", "BrewMaster"));
        var auth = await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        var token = auth!.Data!.AccessToken;

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Fermentable
        var fermentableReq = new CreateIngredientRequest(
            $"Custom Munich {Guid.NewGuid():N}"[..25],
            IngredientType.Fermentable,
            1.036m,
            15.0m,
            null,
            null,
            "Toasty Munich malt"
        );
        var fermRes = await client.PostAsJsonAsync("/api/v1/ingredients", fermentableReq);
        fermRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var fermDto = (await fermRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;
        fermDto.Name.Should().Be(fermentableReq.Name);
        fermDto.Type.Should().Be(IngredientType.Fermentable);
        fermDto.PotentialGravity.Should().Be(1.036m);
        fermDto.ColorSrm.Should().Be(15.0m);
        fermDto.IsCatalogItem.Should().BeFalse();

        // 2. Hop
        var hopReq = new CreateIngredientRequest(
            $"Custom Hop {Guid.NewGuid():N}"[..20],
            IngredientType.Hop,
            null,
            null,
            14.2m,
            null,
            "High alpha aroma hop"
        );
        var hopRes = await client.PostAsJsonAsync("/api/v1/ingredients", hopReq);
        hopRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var hopDto = (await hopRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;
        hopDto.Name.Should().Be(hopReq.Name);
        hopDto.Type.Should().Be(IngredientType.Hop);
        hopDto.AlphaAcidPercent.Should().Be(14.2m);
        hopDto.IsCatalogItem.Should().BeFalse();

        // 3. Yeast
        var yeastReq = new CreateIngredientRequest(
            $"Custom Yeast {Guid.NewGuid():N}"[..22],
            IngredientType.Yeast,
            null,
            null,
            null,
            82.0m,
            "Clean house ale yeast"
        );
        var yeastRes = await client.PostAsJsonAsync("/api/v1/ingredients", yeastReq);
        yeastRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var yeastDto = (await yeastRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;
        yeastDto.Name.Should().Be(yeastReq.Name);
        yeastDto.Type.Should().Be(IngredientType.Yeast);
        yeastDto.AttenuationPercent.Should().Be(82.0m);
        yeastDto.IsCatalogItem.Should().BeFalse();

        // 4. Other
        var otherReq = new CreateIngredientRequest(
            $"Custom Adjunct {Guid.NewGuid():N}"[..24],
            IngredientType.Other,
            null,
            null,
            null,
            null,
            "Special clarifying fining"
        );
        var otherRes = await client.PostAsJsonAsync("/api/v1/ingredients", otherReq);
        otherRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var otherDto = (await otherRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;
        otherDto.Name.Should().Be(otherReq.Name);
        otherDto.Type.Should().Be(IngredientType.Other);
        otherDto.Description.Should().Be("Special clarifying fining");
        otherDto.IsCatalogItem.Should().BeFalse();
    }

    [Fact]
    public async Task CreateIngredient_DuplicateName_ReturnsConflict()
    {
        var email = $"dup_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Pass123!", "DupTester"));
        var auth = await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);

        // Attempt to create an ingredient with identical name to a catalog item (e.g., "Cascade")
        var duplicateCatalogReq = new CreateIngredientRequest("Cascade", IngredientType.Hop, null, null, 7.0m, null, "Duplicate");
        var dupCatRes = await client.PostAsJsonAsync("/api/v1/ingredients", duplicateCatalogReq);
        dupCatRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var dupCatEnvelope = await dupCatRes.Content.ReadFromJsonAsync<ApiResponse>();
        dupCatEnvelope!.Success.Should().BeFalse();
        dupCatEnvelope.Error!.Code.Should().Be("DUPLICATE_NAME");

        // Create a unique custom ingredient
        var customName = $"UniqueCustom_{Guid.NewGuid():N}"[..20];
        var customReq = new CreateIngredientRequest(customName, IngredientType.Fermentable, 1.035m, 4.0m, null, null, null);
        var firstRes = await client.PostAsJsonAsync("/api/v1/ingredients", customReq);
        firstRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Attempt to create the same custom ingredient name again
        var secondRes = await client.PostAsJsonAsync("/api/v1/ingredients", customReq);
        secondRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var secondEnvelope = await secondRes.Content.ReadFromJsonAsync<ApiResponse>();
        secondEnvelope!.Success.Should().BeFalse();
        secondEnvelope.Error!.Code.Should().Be("DUPLICATE_NAME");
    }

    [Fact]
    public async Task GetIngredients_MultiTenantIsolation_ScopesCustomIngredientsToOwner()
    {
        // User A
        var emailA = $"usera_{Guid.NewGuid():N}@example.com";
        var regA = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(emailA, "Pass123!", "UserA"));
        var authA = await regA.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();

        using var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA!.Data!.AccessToken);

        var userAIngredientName = $"SecretMalt_{Guid.NewGuid():N}"[..20];
        var reqA = new CreateIngredientRequest(userAIngredientName, IngredientType.Fermentable, 1.037m, 5.0m, null, null, "User A only");
        var createRes = await clientA.PostAsJsonAsync("/api/v1/ingredients", reqA);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // User B
        var emailB = $"userb_{Guid.NewGuid():N}@example.com";
        var regB = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(emailB, "Pass123!", "UserB"));
        var authB = await regB.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();

        using var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB!.Data!.AccessToken);

        // User B queries ingredients
        var resB = await clientB.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>($"/api/v1/ingredients?search={userAIngredientName}");
        resB.Should().NotBeNull();
        resB!.Data.Should().BeEmpty("User B should not see User A's custom ingredients");

        // Anonymous queries ingredients
        var anonRes = await _client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>($"/api/v1/ingredients?search={userAIngredientName}");
        anonRes.Should().NotBeNull();
        anonRes!.Data.Should().BeEmpty("Anonymous visitor should not see User A's custom ingredients");

        // User A queries ingredients
        var resA = await clientA.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>($"/api/v1/ingredients?search={userAIngredientName}");
        resA.Should().NotBeNull();
        resA!.Data.Should().ContainSingle(i => i.Name == userAIngredientName, "User A should see their own custom ingredient");
    }

    [Fact]
    public async Task UpdateIngredientStock_AndFilterInStock_WorksCorrectly()
    {
        // Register user
        var email = $"stockuser_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Pass123!", "StockUser"));
        var auth = await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);

        // Fetch catalog ingredients
        var listRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Hop");
        listRes.Should().NotBeNull();
        var hop = listRes!.Data!.First();

        // Check initial stock is 0
        hop.StockAmount.Should().Be(0);
        hop.IsInStock.Should().BeFalse();

        // Update stock for this user
        var updateStockReq = new UpdateIngredientStockRequest(250.0m, "g");
        var updateRes = await client.PutAsJsonAsync($"/api/v1/ingredients/{hop.Id}/stock", updateStockReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateEnv = await updateRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>();
        updateEnv.Should().NotBeNull();
        updateEnv!.Data!.StockAmount.Should().Be(250.0m);
        updateEnv.Data.StockUnit.Should().Be("g");
        updateEnv.Data.IsInStock.Should().BeTrue();

        // Query with inStock=true
        var inStockRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?inStock=true");
        inStockRes.Should().NotBeNull();
        inStockRes!.Data.Should().Contain(i => i.Id == hop.Id);
        inStockRes.Data.Should().OnlyContain(i => i.IsInStock);

        // Verify another user does NOT see User A's stock (multi-tenant isolation)
        var emailOther = $"other_{Guid.NewGuid():N}@example.com";
        var regOther = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(emailOther, "Pass123!", "OtherUser"));
        var authOther = await regOther.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();

        using var clientOther = _factory.CreateClient();
        clientOther.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authOther!.Data!.AccessToken);

        var otherInStockRes = await clientOther.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?inStock=true");
        otherInStockRes.Should().NotBeNull();
        otherInStockRes!.Data.Should().NotContain(i => i.Id == hop.Id);
    }

    [Fact]
    public void DefaultCatalog_ContainsDomainEssentialGermanAndCraftIngredients()
    {
        var catalog = DbSeeder.GetDefaultCatalogIngredients();
        catalog.Should().HaveCount(87);

        var fermentables = catalog.Where(i => i.Type == IngredientType.Fermentable).ToList();
        var hops = catalog.Where(i => i.Type == IngredientType.Hop).ToList();
        var yeasts = catalog.Where(i => i.Type == IngredientType.Yeast).ToList();
        var other = catalog.Where(i => i.Type == IngredientType.Other).ToList();

        fermentables.Should().HaveCount(31);
        hops.Should().HaveCount(30);
        yeasts.Should().HaveCount(16);
        other.Should().HaveCount(10);

        // Verify Hallertau cultivars and German hops
        hops.Should().Contain(h => h.Name == "Hallertau Mittelfrüh" && h.AlphaAcidPercent == 4.0m && h.Form == "Pellet");
        hops.Should().Contain(h => h.Name == "Hallertauer Tradition" && h.AlphaAcidPercent == 6.0m && h.Form == "Pellet");
        hops.Should().Contain(h => h.Name == "Hersbrucker" && h.AlphaAcidPercent == 3.5m && h.Form == "Pellet");
        hops.Should().Contain(h => h.Name == "Perle" && h.AlphaAcidPercent == 8.0m && h.Form == "Pellet");
        hops.Should().Contain(h => h.Name == "Hallertau Blanc" && h.AlphaAcidPercent == 10.5m && h.Form == "Pellet");
        hops.Should().Contain(h => h.Name == "Mandarina Bavaria" && h.AlphaAcidPercent == 8.5m && h.Form == "Pellet");
        hops.Should().Contain(h => h.Name == "Spalter Select" && h.AlphaAcidPercent == 4.5m && h.Form == "Pellet");
        hops.Should().OnlyContain(h => h.Form == "Pellet");

        // Verify Wheat, Reinheitsgebot, and craft malts
        fermentables.Should().Contain(f => f.Name == "Malted Wheat" && f.ColorSrm == 2.5m);
        fermentables.Should().Contain(f => f.Name == "Dark Wheat Malt" && f.ColorSrm == 7.0m);
        fermentables.Should().Contain(f => f.Name == "Acidulated Malt" && f.PotentialGravity == 1.034m);
        fermentables.Should().Contain(f => f.Name == "Melanoidin Malt" && f.ColorSrm == 28.0m);
        fermentables.Should().Contain(f => f.Name == "Smoked Malt (Rauchmalz)" && f.PotentialGravity == 1.037m);
        fermentables.Should().Contain(f => f.Name == "Golden Promise" && f.PotentialGravity == 1.038m);

        // Verify Bavarian, German, and craft yeasts with physical form
        yeasts.Should().Contain(y => y.Name == "Wyeast 3068 / WLP300 Weihenstephan Weizen" && y.AttenuationPercent == 75.0m && y.Form == "Liquid");
        yeasts.Should().Contain(y => y.Name == "Fermentis SafAle K-97" && y.AttenuationPercent == 82.0m && y.Form == "Dry");
        yeasts.Should().Contain(y => y.Name == "Fermentis SafLager S-23" && y.AttenuationPercent == 82.0m && y.Form == "Dry");
        yeasts.Should().Contain(y => y.Name == "Lallemand LalBrew Verdant IPA" && y.AttenuationPercent == 79.0m && y.Form == "Dry");
    }

    [Fact]
    public async Task CreateIngredient_WithForm_PersistsAndReturnsForm()
    {
        var email = $"form_user_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Pass123!", "FormUser"));
        var auth = await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);

        var createReq = new CreateIngredientRequest(
            Name: $"Homegrown Cascade Leaf {Guid.NewGuid():N}",
            Type: IngredientType.Hop,
            PotentialGravity: null,
            ColorSrm: null,
            AlphaAcidPercent: 5.8m,
            AttenuationPercent: null,
            Description: "Whole cone homegrown hops",
            InitialStock: 100m,
            StockUnit: "g",
            Form: "Leaf"
        );

        var postRes = await client.PostAsJsonAsync("/api/v1/ingredients", createReq);
        postRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>();
        created.Should().NotBeNull();
        created!.Data!.Form.Should().Be("Leaf");

        var getRes = await client.GetAsync($"/api/v1/ingredients/{created.Data.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>();
        fetched!.Data!.Form.Should().Be("Leaf");
    }

    [Fact]
    public async Task GetIngredientUsage_WhenInRecipes_ReturnsCorrectRecipeList()
    {
        var email = $"usage_user_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Pass123!", "UsageUser"));
        var auth = await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);

        // 1. Create custom ingredient
        var customReq = new CreateIngredientRequest($"Usage Hop {Guid.NewGuid():N}"[..20], IngredientType.Hop, null, null, 12.0m, null, "Aroma");
        var postRes = await client.PostAsJsonAsync("/api/v1/ingredients", customReq);
        postRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var customIng = (await postRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;

        // Check usage before recipe: 0 recipes
        var initUsageRes = await client.GetAsync($"/api/v1/ingredients/{customIng.Id}/usage");
        initUsageRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var initUsage = (await initUsageRes.Content.ReadFromJsonAsync<ApiResponse<IngredientUsageDto>>())!.Data!;
        initUsage.RecipeCount.Should().Be(0);
        initUsage.Recipes.Should().BeEmpty();

        // 2. Fetch a catalog grain and yeast to formulate a valid recipe
        var catalogRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?limit=100");
        var grain = catalogRes!.Data!.First(i => i.Type == IngredientType.Fermentable);
        var yeast = catalogRes.Data!.First(i => i.Type == IngredientType.Yeast);

        var recipeName = $"Test Usage Recipe {Guid.NewGuid():N}"[..25];
        var recipeReq = new CreateRecipeRequest(
            Name: recipeName,
            Description: "Recipe using custom ingredient",
            BeerStyle: "IPA",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(grain.Id, 5.0m, "kg", 60, IngredientUsage.Mash, "Base"),
                new(customIng.Id, 40.0m, "g", 15, IngredientUsage.Boil, "Late addition"),
                new(yeast.Id, 11.5m, "g", 0, IngredientUsage.Primary, "Pitch")
            }
        );
        var createRecipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        createRecipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdRecipe = (await createRecipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 3. Check usage after recipe: 1 recipe
        var usageRes = await client.GetAsync($"/api/v1/ingredients/{customIng.Id}/usage");
        usageRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var usage = (await usageRes.Content.ReadFromJsonAsync<ApiResponse<IngredientUsageDto>>())!.Data!;
        usage.RecipeCount.Should().Be(1);
        usage.Recipes.Should().ContainSingle(r => r.Id == createdRecipe.Id && r.Name == recipeName);
    }

    [Fact]
    public async Task DeleteIngredient_WhenUnused_DeletesSuccessfully()
    {
        var email = $"delete_unused_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Pass123!", "DeleteUser"));
        var auth = await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);

        // Create custom ingredient with stock
        var customReq = new CreateIngredientRequest($"Unused Ingredient {Guid.NewGuid():N}"[..22], IngredientType.Fermentable, 1.035m, 10m, null, null, "To delete", 50m, "kg");
        var postRes = await client.PostAsJsonAsync("/api/v1/ingredients", customReq);
        postRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ing = (await postRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;

        // Delete ingredient
        var delRes = await client.DeleteAsync($"/api/v1/ingredients/{ing.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify ingredient is no longer found
        var getRes = await client.GetAsync($"/api/v1/ingredients/{ing.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteIngredient_WhenInUseByRecipe_RemovesFromRecipeAndRecalculatesRecipeAbv()
    {
        var email = $"delete_cascade_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Pass123!", "CascadeUser"));
        var auth = await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);

        // 1. Create a custom fermentable with high potential gravity
        var customMaltReq = new CreateIngredientRequest(
            Name: $"Super Heavy Malt {Guid.NewGuid():N}"[..22],
            Type: IngredientType.Fermentable,
            PotentialGravity: 1.040m,
            ColorSrm: 25.0m,
            AlphaAcidPercent: null,
            AttenuationPercent: null,
            Description: "Dense malt"
        );
        var postRes = await client.PostAsJsonAsync("/api/v1/ingredients", customMaltReq);
        postRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var customMalt = (await postRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;

        // 2. Fetch catalog ingredients
        var catalogRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?limit=100");
        var baseGrain = catalogRes!.Data!.First(i => i.Type == IngredientType.Fermentable && i.Name == "Pilsner Malt");
        var hop = catalogRes.Data!.First(i => i.Type == IngredientType.Hop);
        var yeast = catalogRes.Data!.First(i => i.Type == IngredientType.Yeast);

        // 3. Create recipe with base malt + custom malt
        var recipeReq = new CreateRecipeRequest(
            Name: $"Cascade Recalc Recipe {Guid.NewGuid():N}"[..25],
            Description: "Recipe to test ingredient deletion recalculation",
            BeerStyle: "Imperial Ale",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(baseGrain.Id, 4.0m, "kg", 60, IngredientUsage.Mash, "Base Pilsner"),
                new(customMalt.Id, 5.0m, "kg", 60, IngredientUsage.Mash, "Heavy Addition"),
                new(hop.Id, 30.0m, "g", 60, IngredientUsage.Boil, "Bittering"),
                new(yeast.Id, 11.5m, "g", 0, IngredientUsage.Primary, "Pitch")
            }
        );
        var createRecipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        createRecipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var initialRecipe = (await createRecipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        var initialOg = initialRecipe.OriginalGravity;
        var initialAbv = initialRecipe.AlcoholByVolume;
        var initialSrm = initialRecipe.ColorSrm;
        initialOg.Should().BeGreaterThan(1.080m);
        initialAbv.Should().BeGreaterThan(8.0m);

        // 4. Delete the custom malt
        var deleteRes = await client.DeleteAsync($"/api/v1/ingredients/{customMalt.Id}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Fetch recipe and verify:
        // - custom malt removed
        // - base grain, hop, yeast remain intact
        // - OG, ABV, SRM have been recalculated down
        var fetchRecipeRes = await client.GetAsync($"/api/v1/recipes/{initialRecipe.Id}");
        fetchRecipeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedRecipe = (await fetchRecipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        updatedRecipe.Ingredients.Should().NotContain(ri => ri.IngredientId == customMalt.Id);
        updatedRecipe.Ingredients.Should().Contain(ri => ri.IngredientId == baseGrain.Id);
        updatedRecipe.Ingredients.Should().Contain(ri => ri.IngredientId == hop.Id);
        updatedRecipe.Ingredients.Should().Contain(ri => ri.IngredientId == yeast.Id);
        updatedRecipe.Ingredients.Count.Should().Be(3);

        updatedRecipe.OriginalGravity.Should().BeLessThan(initialOg);
        updatedRecipe.AlcoholByVolume.Should().BeLessThan(initialAbv);
        updatedRecipe.ColorSrm.Should().BeLessThan(initialSrm);
    }

    [Fact]
    public async Task DeleteIngredient_WhenCatalogItem_ReturnsBadRequest()
    {
        var email = $"catalog_del_{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Pass123!", "CatalogDelUser"));
        var auth = await reg.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);

        // Fetch a catalog ingredient
        var catalogRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?limit=10");
        var catalogItem = catalogRes!.Data!.First(i => i.IsCatalogItem);

        // Attempt deletion
        var delRes = await client.DeleteAsync($"/api/v1/ingredients/{catalogItem.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var env = await delRes.Content.ReadFromJsonAsync<ApiResponse>();
        env!.Success.Should().BeFalse();
        env.Error!.Code.Should().Be("CATALOG_ITEM_CANNOT_BE_DELETED");
    }

    [Fact]
    public async Task DeleteIngredient_WhenOwnedByOtherUser_ReturnsForbidden()
    {
        // User A
        var emailA = $"user_a_{Guid.NewGuid():N}@example.com";
        var regA = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(emailA, "Pass123!", "UserA"));
        var authA = await regA.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        using var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA!.Data!.AccessToken);

        var customReq = new CreateIngredientRequest($"Private Malt {Guid.NewGuid():N}"[..20], IngredientType.Fermentable, 1.035m, 5m, null, null, "Private");
        var postRes = await clientA.PostAsJsonAsync("/api/v1/ingredients", customReq);
        var ingA = (await postRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;

        // User B
        var emailB = $"user_b_{Guid.NewGuid():N}@example.com";
        var regB = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(emailB, "Pass123!", "UserB"));
        var authB = await regB.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        using var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB!.Data!.AccessToken);

        // User B attempts to check usage and delete User A's ingredient
        var usageRes = await clientB.GetAsync($"/api/v1/ingredients/{ingA.Id}/usage");
        usageRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var delRes = await clientB.DeleteAsync($"/api/v1/ingredients/{ingA.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}