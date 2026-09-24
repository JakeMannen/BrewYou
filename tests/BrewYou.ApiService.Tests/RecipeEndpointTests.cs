using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class RecipeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RecipeEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string UserId, string Token)> CreateAuthenticatedUserAsync(string prefix = "brewer")
    {
        var client = _factory.CreateClient();
        var email = $"{prefix}_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, $"{prefix} Display", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var token = envelope.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, envelope.Data.User.Id, token);
    }

    private async Task<List<IngredientDto>> GetIngredientsAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/v1/ingredients");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>();
        return envelope!.Data!;
    }

    [Fact]
    public async Task CreateRecipe_WithValidIngredients_CalculatesMetricsAndPersists()
    {
        var (client, userId, _) = await CreateAuthenticatedUserAsync("recipemaker");
        var ingredients = await GetIngredientsAsync(client);

        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);
        var hop = ingredients.First(i => i.Type == IngredientType.Hop);
        var yeast = ingredients.First(i => i.Type == IngredientType.Yeast);

        var request = new CreateRecipeRequest(
            Name: "Cascade Pale Ale",
            Description: "A classic American pale ale with Cascade hops.",
            BeerStyle: "American Pale Ale",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72.0m,
            IsPublic: true,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(grain.Id, 4.5m, "kg", 60, IngredientUsage.Mash, "Base malt"),
                new(hop.Id, 30.0m, "g", 60, IngredientUsage.Boil, "Bittering"),
                new(yeast.Id, 11.5m, "g", 0, IngredientUsage.Primary, "Pitch at 18C")
            }
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", request);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var recipe = envelope.Data!;
        recipe.Name.Should().Be("Cascade Pale Ale");
        recipe.BeerStyle.Should().Be("American Pale Ale");
        recipe.OriginalGravity.Should().BeGreaterThan(1.000m);
        recipe.FinalGravity.Should().BeGreaterThan(0.990m);
        recipe.AlcoholByVolume.Should().BeGreaterThan(0m);
        recipe.BitternessIbu.Should().BeGreaterThan(0m);
        recipe.ColorSrm.Should().BeGreaterThan(0m);
        recipe.Ingredients.Should().HaveCount(3);
        recipe.MashSteps.Should().HaveCount(1);
        recipe.MashSteps[0].Name.Should().Be("Saccharification Rest");
        recipe.MashSteps[0].TemperatureC.Should().Be(65.0m);
    }

    [Fact]
    public async Task CreateRecipe_WithMultiStepMash_PersistsAndOrdersMashSteps()
    {
        var (client, userId, _) = await CreateAuthenticatedUserAsync("mashmaker");
        var ingredients = await GetIngredientsAsync(client);
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);

        var request = new CreateRecipeRequest(
            Name: "German Helles",
            Description: "Traditional step-mashed lager",
            BeerStyle: "Munich Helles",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 75.0m,
            IsPublic: true,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(grain.Id, 4.2m, "kg", 90, IngredientUsage.Mash, "Pilsner Malt")
            },
            MashSteps: new List<RecipeMashStepInputDto>
            {
                new(1, "Protein Rest", MashStepType.Infusion, 52.0m, 20, 5, 14.0m, "Protein degradation"),
                new(2, "Saccharification", MashStepType.Temperature, 64.0m, 45, 10, null, "Maltose production"),
                new(3, "Mash Out", MashStepType.Temperature, 76.0m, 10, 5, null, "Enzyme deactivation")
            }
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", request);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        envelope.Should().NotBeNull();
        var recipe = envelope!.Data!;
        recipe.MashSteps.Should().HaveCount(3);
        recipe.MashSteps[0].StepOrder.Should().Be(1);
        recipe.MashSteps[0].Name.Should().Be("Protein Rest");
        recipe.MashSteps[0].TemperatureC.Should().Be(52.0m);
        recipe.MashSteps[0].DurationMinutes.Should().Be(20);
        recipe.MashSteps[0].RampTimeMinutes.Should().Be(5);
        recipe.MashSteps[0].InfuseAmountLiters.Should().Be(14.0m);

        recipe.MashSteps[1].StepOrder.Should().Be(2);
        recipe.MashSteps[1].TemperatureC.Should().Be(64.0m);

        recipe.MashSteps[2].StepOrder.Should().Be(3);
        recipe.MashSteps[2].TemperatureC.Should().Be(76.0m);

        // Fetch via GET /api/v1/recipes/{id} to ensure EF Core include succeeds
        var getRes = await client.GetAsync($"/api/v1/recipes/{recipe.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getEnv = await getRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        getEnv!.Data!.MashSteps.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateRecipe_WithOnlyNameProvided_SucceedsWithDefaults()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("onlynametester");

        var request = new CreateRecipeRequest(
            Name: "Clean Minimal Recipe",
            Description: null,
            BeerStyle: null,
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            IsPublic: false,
            Ingredients: new()
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", request);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var recipe = envelope.Data!;
        recipe.Name.Should().Be("Clean Minimal Recipe");
        recipe.BeerStyle.Should().Be("Custom");
        recipe.Ingredients.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecipe_DuplicateNameForSameUser_Returns409Conflict()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("dupetester");

        var req1 = new CreateRecipeRequest(
            Name: "Unique Session IPA",
            Description: "First version",
            BeerStyle: "Session IPA",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            IsPublic: true,
            Ingredients: new()
        );

        var res1 = await client.PostAsJsonAsync("/api/v1/recipes", req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Attempt second recipe with same name (different casing and trailing spaces)
        var req2 = new CreateRecipeRequest(
            Name: "  unique session ipa  ",
            Description: "Duplicate version",
            BeerStyle: "Session IPA",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            IsPublic: true,
            Ingredients: new()
        );

        var res2 = await client.PostAsJsonAsync("/api/v1/recipes", req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var envelope = await res2.Content.ReadFromJsonAsync<ApiResponse>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("DUPLICATE_NAME");
    }

    [Fact]
    public async Task CreateRecipe_SameNameDifferentUser_Succeeds()
    {
        var (client1, _, _) = await CreateAuthenticatedUserAsync("user1");
        var (client2, _, _) = await CreateAuthenticatedUserAsync("user2");

        var req = new CreateRecipeRequest(
            Name: "Community Recipe Standard",
            Description: null,
            BeerStyle: "Blonde Ale",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            IsPublic: true,
            Ingredients: new()
        );

        var res1 = await client1.PostAsJsonAsync("/api/v1/recipes", req);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        var res2 = await client2.PostAsJsonAsync("/api/v1/recipes", req);
        res2.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UpdateRecipe_ValidChanges_RecalculatesMetricsAndPersists()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("updater");
        var ingredients = await GetIngredientsAsync(client);
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);
        var hop = ingredients.First(i => i.Type == IngredientType.Hop);

        var createReq = new CreateRecipeRequest(
            Name: "Initial IPA",
            Description: "Initial description",
            BeerStyle: "American IPA",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 70m,
            IsPublic: true,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(grain.Id, 4.0m, "kg", 60, IngredientUsage.Mash, null)
            }
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/recipes", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var initialRecipe = (await createRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // Update: increase grain to 6kg and add 40g hops
        var updateReq = new UpdateRecipeRequest(
            Name: "Updated Imperial IPA",
            Description: "Updated description",
            BeerStyle: "Imperial IPA",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 75m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(grain.Id, 6.5m, "kg", 90, IngredientUsage.Mash, "Heavy malt base"),
                new(hop.Id, 50.0m, "g", 60, IngredientUsage.Boil, "Bittering additions")
            }
        );

        var updateRes = await client.PutAsJsonAsync($"/api/v1/recipes/{initialRecipe.Id}", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateEnvelope = await updateRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        updateEnvelope!.Success.Should().BeTrue();

        var updatedRecipe = updateEnvelope.Data!;
        updatedRecipe.Name.Should().Be("Updated Imperial IPA");
        updatedRecipe.BeerStyle.Should().Be("Imperial IPA");
        updatedRecipe.BoilTimeMinutes.Should().Be(90);
        updatedRecipe.EfficiencyPercent.Should().Be(75m);
        updatedRecipe.IsPublic.Should().BeFalse();
        updatedRecipe.OriginalGravity.Should().BeGreaterThan(initialRecipe.OriginalGravity);
        updatedRecipe.Ingredients.Should().HaveCount(2);

        // Verify with subsequent GET
        var getRes = await client.GetAsync($"/api/v1/recipes/{initialRecipe.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getRecipe = (await getRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        getRecipe.Name.Should().Be("Updated Imperial IPA");
    }

    [Fact]
    public async Task UpdateRecipe_KeepingOwnName_Succeeds()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("selfupdater");

        var createReq = new CreateRecipeRequest(
            Name: "Stable Name Porter",
            Description: "Draft 1",
            BeerStyle: "Baltic Porter",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 70m,
            IsPublic: true,
            Ingredients: new()
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/recipes", createReq);
        var recipe = (await createRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        var updateReq = new UpdateRecipeRequest(
            Name: "Stable Name Porter", // Same name
            Description: "Draft 2 with new description",
            BeerStyle: "Baltic Porter",
            BatchSizeLiters: 25m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 70m,
            IsPublic: true,
            Ingredients: new()
        );

        var updateRes = await client.PutAsJsonAsync($"/api/v1/recipes/{recipe.Id}", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateRecipe_DuplicateNameToOtherExistingRecipe_Returns409Conflict()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("clashupdater");

        var req1 = new CreateRecipeRequest("Recipe Alpha", null, "Saison", 20m, 60, 70m, true, new());
        var res1 = await client.PostAsJsonAsync("/api/v1/recipes", req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        var req2 = new CreateRecipeRequest("Recipe Beta", null, "Saison", 20m, 60, 70m, true, new());
        var res2 = await client.PostAsJsonAsync("/api/v1/recipes", req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipeBeta = (await res2.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // Attempt to rename Recipe Beta to "Recipe Alpha"
        var updateReq = new UpdateRecipeRequest("Recipe Alpha", null, "Saison", 20m, 60, 70m, true, new());
        var updateRes = await client.PutAsJsonAsync($"/api/v1/recipes/{recipeBeta.Id}", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var envelope = await updateRes.Content.ReadFromJsonAsync<ApiResponse>();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("DUPLICATE_NAME");
    }

    [Fact]
    public async Task UpdateRecipe_OtherUserRecipe_Returns403Forbidden()
    {
        var (clientA, _, _) = await CreateAuthenticatedUserAsync("user_a");
        var (clientB, _, _) = await CreateAuthenticatedUserAsync("user_b");

        var createReq = new CreateRecipeRequest("Secret Stout", null, "Stout", 20m, 60, 70m, true, new());
        var createRes = await clientA.PostAsJsonAsync("/api/v1/recipes", createReq);
        var recipe = (await createRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        var updateReq = new UpdateRecipeRequest("Hacked Stout", null, "Stout", 20m, 60, 70m, true, new());
        var updateRes = await clientB.PutAsJsonAsync($"/api/v1/recipes/{recipe.Id}", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteRecipe_OwnerCanDelete_LinkedBatchRetainsSnapshotAndDecouples()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("batchbrewer");

        // 1. Create recipe
        var recipeReq = new CreateRecipeRequest("Batch Source IPA", null, "American IPA", 20m, 60, 72m, true, new());
        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 2. Create batch linked to recipe
        var batchReq = new CreateBatchRequest(
            RecipeId: recipe.Id,
            BatchCode: "BATCH-001",
            Name: "Live Batch #1",
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: null,
            TargetFg: null,
            TargetAbv: null,
            TargetIbu: null,
            TargetColorSrm: null,
            BoilTimeMinutes: null,
            EfficiencyPercent: null,
            BoilerId: null,
            FermenterId: null,
            MeasuredOg: null,
            PitchTemperatureC: null,
            Notes: null,
            CustomIngredients: null
        );
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        batch.RecipeId.Should().Be(recipe.Id);

        // 3. Delete recipe
        var delRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Verify recipe is gone
        var getRecipeRes = await client.GetAsync($"/api/v1/recipes/{recipe.Id}");
        getRecipeRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 5. Verify batch is still intact and RecipeId is null
        var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        getBatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedBatch = (await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        updatedBatch.RecipeId.Should().BeNull();
        updatedBatch.Name.Should().Be("Live Batch #1");
    }

    [Fact]
    public async Task CalculateWater_WithValidInput_ReturnsExpectedWaterSchedule()
    {
        var client = _factory.CreateClient();
        var request = new CalculateWaterVolumeRequest(
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            TotalGrainWeightKg: 4.5m,
            BoilOffRatePerHourLiters: 3.0m,
            KettleTrubLossLiters: 1.5m,
            GrainAbsorptionRateLPerKg: 0.96m,
            FermenterLossLiters: 1.5m,
            MashTunDeadSpaceLiters: 0.0m,
            CoolingShrinkagePercent: 4.0m,
            PackagingLossLiters: 0.5m,
            MashThicknessLitersPerKg: 3.0m
        );

        var response = await client.PostAsJsonAsync("/api/v1/recipes/calculate-water", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<CalculateWaterVolumeResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var data = envelope.Data!;
        data.TotalWaterLiters.Should().BeGreaterThan(25.0m);
        data.StrikeWaterLiters.Should().Be(13.5m); // 4.5 * 3.0
        data.SpargeWaterLiters.Should().Be(data.TotalWaterLiters - data.StrikeWaterLiters);
        data.EstimatedIntoFermenterVolumeLiters.Should().Be(20.0m);
        data.GrainAbsorptionLossLiters.Should().Be(4.32m); // 4.5 * 0.96
        data.BoilOffLossLiters.Should().Be(3.0m);
    }

    [Fact]
    public async Task CalculateWater_WithInvalidInput_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var request = new CalculateWaterVolumeRequest(
            BatchSizeLiters: -5.0m, // Invalid negative batch size
            BoilTimeMinutes: -10,
            TotalGrainWeightKg: -1.0m
        );

        var response = await client.PostAsJsonAsync("/api/v1/recipes/calculate-water", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetRecipes_ScopesAndOwnership_FilterAndMapCorrectly()
    {
        // Arrange: Create two separate users
        var (clientA, userAId, _) = await CreateAuthenticatedUserAsync("brewer_scope_a");
        var (clientB, userBId, _) = await CreateAuthenticatedUserAsync("brewer_scope_b");

        // Brewer A creates: 1 public recipe, 1 private recipe
        var reqA1 = new CreateRecipeRequest("Scope A Public", null, "Saison", 20m, 60, 70m, true, new());
        var reqA2 = new CreateRecipeRequest("Scope A Private", null, "Porter", 20m, 60, 70m, false, new());
        var resA1 = await clientA.PostAsJsonAsync("/api/v1/recipes", reqA1);
        var resA2 = await clientA.PostAsJsonAsync("/api/v1/recipes", reqA2);
        resA1.StatusCode.Should().Be(HttpStatusCode.Created);
        resA2.StatusCode.Should().Be(HttpStatusCode.Created);

        // Brewer B creates: 1 public recipe, 1 private recipe
        var reqB1 = new CreateRecipeRequest("Scope B Public", null, "IPA", 20m, 60, 70m, true, new());
        var reqB2 = new CreateRecipeRequest("Scope B Private", null, "Stout", 20m, 60, 70m, false, new());
        var resB1 = await clientB.PostAsJsonAsync("/api/v1/recipes", reqB1);
        var resB2 = await clientB.PostAsJsonAsync("/api/v1/recipes", reqB2);
        resB1.StatusCode.Should().Be(HttpStatusCode.Created);
        resB2.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipeB1 = (await resB1.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        var recipeB2 = (await resB2.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 1. Test "mine" scope for Brewer A
        var mineRes = await clientA.GetAsync("/api/v1/recipes?scope=mine");
        mineRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var mineData = (await mineRes.Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        mineData.Should().Contain(r => r.Name == "Scope A Public" && r.IsOwner);
        mineData.Should().Contain(r => r.Name == "Scope A Private" && r.IsOwner);
        mineData.Should().NotContain(r => r.Name.StartsWith("Scope B"));

        // 2. Test "shared" scope for Brewer A
        var sharedRes = await clientA.GetAsync("/api/v1/recipes?scope=shared");
        sharedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var sharedData = (await sharedRes.Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        sharedData.Should().Contain(r => r.Name == "Scope B Public" && !r.IsOwner && r.AuthorName == "brewer_scope_b Display");
        sharedData.Should().NotContain(r => r.Name == "Scope B Private"); // Private recipe not leaked
        sharedData.Should().NotContain(r => r.Name.StartsWith("Scope A")); // Own recipes excluded from shared tab

        // 3. Test "all" scope for Brewer A
        var allRes = await clientA.GetAsync("/api/v1/recipes?scope=all");
        allRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var allData = (await allRes.Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        allData.Should().Contain(r => r.Name == "Scope A Public" && r.IsOwner);
        allData.Should().Contain(r => r.Name == "Scope A Private" && r.IsOwner);
        allData.Should().Contain(r => r.Name == "Scope B Public" && !r.IsOwner);
        allData.Should().NotContain(r => r.Name == "Scope B Private"); // Other's private recipe excluded

        // 4. Test single recipe privacy: Brewer A accessing Brewer B's private recipe returns 404 (IDOR defense)
        var foreignPrivateRes = await clientA.GetAsync($"/api/v1/recipes/{recipeB2.Id}");
        foreignPrivateRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 5. Test single recipe public access: Brewer A accessing Brewer B's public recipe returns 200 with IsOwner = false
        var foreignPublicRes = await clientA.GetAsync($"/api/v1/recipes/{recipeB1.Id}");
        foreignPublicRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var foreignPublicDto = (await foreignPublicRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        foreignPublicDto.IsOwner.Should().BeFalse();
        foreignPublicDto.AuthorName.Should().Be("brewer_scope_b Display");
    }

    [Fact]
    public async Task GetRecipes_WithInvalidScope_ReturnsBadRequest()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("scope_tester");
        var res = await client.GetAsync("/api/v1/recipes?scope=unrecognized_scope");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be("VALIDATION_ERROR");
        envelope.Error.Details.Should().Contain(d => d.Field == "scope");
    }

    [Fact]
    public async Task CreateRecipe_WithFermentationSteps_PersistsAndReturnsProfile()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("ferm_tester");
        var ingredients = await GetIngredientsAsync(client);
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);
        var yeast = ingredients.First(i => i.Type == IngredientType.Yeast);

        var fermSteps = new List<RecipeFermentationStepInputDto>
        {
            new(1, "Primary", FermentationStepType.Primary, 11.0m, 10, null, null, "Lager pitch"),
            new(2, "Diacetyl Rest", FermentationStepType.Ramp, 18.0m, 3, 24, 1.018m, "Raise temp"),
            new(3, "Lagering", FermentationStepType.ColdCrash, 2.0m, 14, null, null, "Cold conditioning")
        };

        var request = new CreateRecipeRequest(
            Name: "German Pilsner with Staged Ferm",
            BeerStyle: "German Pils",
            Description: "Crisp lager with explicit fermentation profile",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 75.0m,
            IsPublic: true,
            Ingredients:
            [
                new(grain.Id, 4.5m, "kg", null, IngredientUsage.Mash, null),
                new(yeast.Id, 11.5m, "g", null, IngredientUsage.Primary, null)
            ],
            MashSteps: null,
            FermentationSteps: fermSteps
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/recipes", request);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        created.FermentationSteps.Should().HaveCount(3);
        created.FermentationSteps[0].Name.Should().Be("Primary");
        created.FermentationSteps[0].TargetTemperatureC.Should().Be(11.0m);
        created.FermentationSteps[0].DurationDays.Should().Be(10);

        created.FermentationSteps[1].Name.Should().Be("Diacetyl Rest");
        created.FermentationSteps[1].TargetTemperatureC.Should().Be(18.0m);
        created.FermentationSteps[1].TriggerGravity.Should().Be(1.018m);

        created.FermentationSteps[2].Name.Should().Be("Lagering");
        created.FermentationSteps[2].TargetTemperatureC.Should().Be(2.0m);

        // Fetch back and verify persistence
        var getRes = await client.GetAsync($"/api/v1/recipes/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = (await getRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        fetched.FermentationSteps.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateRecipe_WithIngredientForms_PersistsAndReturnsForm()
    {
        var (client, userId, _) = await CreateAuthenticatedUserAsync("formbrewer");
        var ingredients = await GetIngredientsAsync(client);

        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);
        var hop = ingredients.First(i => i.Type == IngredientType.Hop);
        var yeast = ingredients.First(i => i.Type == IngredientType.Yeast);

        var request = new CreateRecipeRequest(
            Name: "Hop & Yeast Form Test Ale",
            BeerStyle: "American IPA",
            Description: "Testing explicit forms",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: true,
            Ingredients:
            [
                new(grain.Id, 5.0m, "kg", 60, IngredientUsage.Mash, null, null),
                new(hop.Id, 50m, "g", 15, IngredientUsage.Boil, "Pellet hops", "Pellet"),
                new(yeast.Id, 23m, "g", null, IngredientUsage.Primary, "Double pitch", "Dry")
            ]
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/recipes", request);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        var hopIng = created.Ingredients.First(i => i.IngredientId == hop.Id);
        hopIng.Form.Should().Be("Pellet");
        var yeastIng = created.Ingredients.First(i => i.IngredientId == yeast.Id);
        yeastIng.Form.Should().Be("Dry");
        yeastIng.Amount.Should().Be(23m);

        var getRes = await client.GetAsync($"/api/v1/recipes/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = (await getRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
        fetched.Ingredients.First(i => i.IngredientId == hop.Id).Form.Should().Be("Pellet");
        fetched.Ingredients.First(i => i.IngredientId == yeast.Id).Form.Should().Be("Dry");
    }

    [Fact]
    public async Task CheckRecipeName_WhenNameExists_ReturnsTrue()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("namechecker1");
        var ingredients = await GetIngredientsAsync(client);
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);

        var recipeName = $"Unique Ale {Guid.NewGuid():N}";
        var request = new CreateRecipeRequest(
            Name: recipeName,
            Description: "Recipe for name check test",
            BeerStyle: "Pale Ale",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72.0m,
            IsPublic: true,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(grain.Id, 4.0m, "kg", 60, IngredientUsage.Mash, null)
            }
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/recipes", request);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Check exact match
        var checkRes = await client.GetAsync($"/api/v1/recipes/check-name?name={Uri.EscapeDataString(recipeName)}");
        checkRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkData = (await checkRes.Content.ReadFromJsonAsync<ApiResponse<RecipeNameCheckResponse>>())!.Data!;
        checkData.Exists.Should().BeTrue();

        // Check case-insensitive and trimmed match
        var checkCaseRes = await client.GetAsync($"/api/v1/recipes/check-name?name={Uri.EscapeDataString($"  {recipeName.ToUpperInvariant()}  ")}");
        checkCaseRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkCaseData = (await checkCaseRes.Content.ReadFromJsonAsync<ApiResponse<RecipeNameCheckResponse>>())!.Data!;
        checkCaseData.Exists.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRecipeName_WhenNameDoesNotExist_ReturnsFalse()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("namechecker2");
        var checkRes = await client.GetAsync($"/api/v1/recipes/check-name?name={Uri.EscapeDataString("Non Existent Recipe Name")}");
        checkRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkData = (await checkRes.Content.ReadFromJsonAsync<ApiResponse<RecipeNameCheckResponse>>())!.Data!;
        checkData.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRecipeName_WhenNameBelongsToDifferentUser_ReturnsFalse()
    {
        var (clientUser1, _, _) = await CreateAuthenticatedUserAsync("user1name");
        var (clientUser2, _, _) = await CreateAuthenticatedUserAsync("user2name");
        var ingredients = await GetIngredientsAsync(clientUser1);
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);

        var recipeName = $"User1 Private Ale {Guid.NewGuid():N}";
        var request = new CreateRecipeRequest(
            Name: recipeName,
            Description: "Recipe for User 1",
            BeerStyle: "Pale Ale",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72.0m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(grain.Id, 4.0m, "kg", 60, IngredientUsage.Mash, null)
            }
        );

        var createRes = await clientUser1.PostAsJsonAsync("/api/v1/recipes", request);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // User 2 checking the same name should receive Exists = false
        var checkRes = await clientUser2.GetAsync($"/api/v1/recipes/check-name?name={Uri.EscapeDataString(recipeName)}");
        checkRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkData = (await checkRes.Content.ReadFromJsonAsync<ApiResponse<RecipeNameCheckResponse>>())!.Data!;
        checkData.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRecipeName_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var anonymousClient = _factory.CreateClient();
        var checkRes = await anonymousClient.GetAsync("/api/v1/recipes/check-name?name=SomeRecipe");
        checkRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}