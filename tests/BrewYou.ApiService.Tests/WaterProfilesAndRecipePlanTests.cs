using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class WaterProfilesAndRecipePlanTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WaterProfilesAndRecipePlanTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest($"water_{Guid.NewGuid():N}@brewyou.test", "StrongPassword123!", "Water Brewer", "en", VolumeUnit.Liters));
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    [Fact]
    public async Task PresetsAreProtected_AndRecipeWaterPlanPersistsAndSurvivesProfileDeletion()
    {
        var client = await CreateAuthenticatedClientAsync();

        // 1. Built-in presets exist and cannot be modified or deleted
        var listRes = await client.GetFromJsonAsync<ApiResponse<List<WaterProfileDto>>>("/api/v1/water-profiles");
        var profiles = listRes!.Data!;
        var pilsen = profiles.First(p => p.IsPreset && p.Name.Contains("Pilsen"));
        profiles.Should().Contain(p => p.IsPreset && p.Name.Contains("Burton"));

        var delPreset = await client.DeleteAsync($"/api/v1/water-profiles/{pilsen.Id}");
        delPreset.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Create custom source profile
        var sourceRes = await client.PostAsJsonAsync("/api/v1/water-profiles", new CreateWaterProfileRequest(
            Name: "Tap City Soft",
            Category: "Source",
            CalciumPpm: 18m,
            MagnesiumPpm: 4m,
            SodiumPpm: 10m,
            SulfatePpm: 14m,
            ChloridePpm: 18m,
            BicarbonatePpm: 45m,
            Ph: 7.4m));
        sourceRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var sourceProfile = (await sourceRes.Content.ReadFromJsonAsync<ApiResponse<WaterProfileDto>>())!.Data!;

        // 3. Get catalog pale malt and create recipe with water plan
        var catalogRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Fermentable&search=Pale%20Malt&limit=20");
        var paleMalt = catalogRes!.Data!.First(i => (i.ColorSrm ?? 10m) < 5m);

        var createRecipeRes = await client.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Crisp Czech Pils",
            Description: "With water plan",
            BeerStyle: "Czech Premium Pale Lager",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 74m,
            IsPublic: false,
            Ingredients:
            [
                new RecipeIngredientInputDto(paleMalt.Id, 4.5m, "kg", null, IngredientUsage.Mash, null)
            ],
            WaterPlan: new RecipeWaterPlanInputDto(
                SourceProfileId: sourceProfile.Id,
                SourceProfileName: sourceProfile.Name,
                SourceCalciumPpm: sourceProfile.CalciumPpm,
                SourceMagnesiumPpm: sourceProfile.MagnesiumPpm,
                SourceSodiumPpm: sourceProfile.SodiumPpm,
                SourceSulfatePpm: sourceProfile.SulfatePpm,
                SourceChloridePpm: sourceProfile.ChloridePpm,
                SourceBicarbonatePpm: sourceProfile.BicarbonatePpm,
                SourcePh: 7.4m,
                TargetProfileId: pilsen.Id,
                TargetProfileName: pilsen.Name,
                TargetMashPh: 5.35m,
                MashCalciumChlorideGrams: 1.5m,
                MashAcidType: "Lactic80",
                MashAcidAmountMlOrG: 2.0m,
                SpargeAcidType: "Lactic80",
                SpargeAcidAmountMlOrG: 1.0m,
                Notes: "Keep Ca around 40ppm")));
        createRecipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await createRecipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        recipe.WaterPlan.Should().NotBeNull();
        recipe.WaterPlan!.SourceProfileId.Should().Be(sourceProfile.Id);
        recipe.WaterPlan.TargetProfileId.Should().Be(pilsen.Id);
        recipe.WaterPlan.ResultingCalciumPpm.Should().BeGreaterThan(sourceProfile.CalciumPpm);
        recipe.WaterPlan.PredictedMashPh.Should().BeInRange(4.8m, 5.8m);
        recipe.WaterPlan.SulfateToChlorideRatio.Should().NotBeNull();

        // 4. Deleting custom source profile decouples SourceProfileId cleanly while keeping snapshot name & ions
        var delSource = await client.DeleteAsync($"/api/v1/water-profiles/{sourceProfile.Id}");
        delSource.StatusCode.Should().Be(HttpStatusCode.OK);

        var reloadedRecipe = (await client.GetFromJsonAsync<ApiResponse<RecipeDetailDto>>($"/api/v1/recipes/{recipe.Id}"))!.Data!;
        reloadedRecipe.WaterPlan.Should().NotBeNull();
        reloadedRecipe.WaterPlan!.SourceProfileId.Should().BeNull();
        reloadedRecipe.WaterPlan.SourceProfileName.Should().Be("Tap City Soft");
        reloadedRecipe.WaterPlan.ResultingCalciumPpm.Should().BeGreaterThan(sourceProfile.CalciumPpm);
    }
}