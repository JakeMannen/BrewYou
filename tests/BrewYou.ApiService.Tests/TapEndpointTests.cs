using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class TapEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TapEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string UserId, string Token)> CreateAuthenticatedUserAsync(string prefix = "tapbrewer")
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

    private async Task<BrewerySetupDto> GetDefaultSetupAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/v1/brewery-setups");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        return envelope!.Data!.First(s => s.IsDefault);
    }

    private async Task<RecipeDetailDto> CreateSampleRecipeAsync(HttpClient client, string name = "Tap IPA")
    {
        var grainRes = await client.GetAsync("/api/v1/ingredients");
        var ingredients = (await grainRes.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);

        var request = new CreateRecipeRequest(
            Name: name,
            Description: "Recipe for tap tests.",
            BeerStyle: "American IPA",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: true,
            Ingredients: [new RecipeIngredientInputDto(grain.Id, 5.0m, "kg", 60, IngredientUsage.Mash, "Base")]
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", request);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        return envelope!.Data!;
    }

    private async Task<BatchDetailDto> CreateSampleBatchAsync(HttpClient client, Guid recipeId, BrewStage stage = BrewStage.Condition)
    {
        var request = new CreateBatchRequest(
            RecipeId: recipeId,
            BatchCode: $"B-{Guid.NewGuid():N}"[..12],
            Name: "Test Tap Batch",
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.2m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: null,
            FermenterId: null
        );

        var res = await client.PostAsJsonAsync("/api/v1/batches", request);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        var batch = envelope!.Data!;

        if (stage != BrewStage.Mash)
        {
            var advanceRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage", new AdvanceBatchStageRequest(
                TargetStage: stage,
                MeasuredOg: 1.050m,
                MeasuredFg: 1.010m
            ));
            advanceRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var advanceEnvelope = await advanceRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
            return advanceEnvelope!.Data!;
        }

        return batch;
    }

    [Fact]
    public async Task CreateTapEquipment_WithDisplaySettings_PersistsSuccessfully()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("tapcreator");
        var setup = await GetDefaultSetupAsync(client);

        var createReq = new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Tap 1 (Round Screen)",
            Type: EquipmentType.Tap,
            Subtype: EquipmentSubtype.RoundDisplayTap,
            Capacity: 0m,
            Unit: VolumeUnit.Liters,
            DisplayWidthPx: 240,
            DisplayHeightPx: 240,
            DisplayOrientation: DisplayOrientation.Landscape
        );

        var res = await client.PostAsJsonAsync("/api/v1/inventory/equipment", createReq);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        envelope.Should().NotBeNull();
        var eq = envelope!.Data!;

        eq.Type.Should().Be(EquipmentType.Tap);
        eq.Subtype.Should().Be(EquipmentSubtype.RoundDisplayTap);
        eq.DisplayWidthPx.Should().Be(240);
        eq.DisplayHeightPx.Should().Be(240);
        eq.DisplayLinkEnabled.Should().BeFalse();
        eq.AssignedBatch.Should().BeNull();
    }

    [Fact]
    public async Task AssignTapBatch_WhenBatchInServableStage_Succeeds()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("tapassign");
        var setup = await GetDefaultSetupAsync(client);
        var recipe = await CreateSampleRecipeAsync(client, "Servable IPA");
        var batch = await CreateSampleBatchAsync(client, recipe.Id, BrewStage.Condition);

        // Create tap
        var createTapRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Kegerator Tap A",
            Type: EquipmentType.Tap,
            Subtype: EquipmentSubtype.StandardTap,
            Capacity: 0m,
            Unit: VolumeUnit.Liters
        ));
        var tap = (await createTapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Assign batch to tap
        var assignRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", new AssignTapRequest(batch.Id));
        assignRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var assignEnvelope = await assignRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        var updatedTap = assignEnvelope!.Data!;
        updatedTap.AssignedBatch.Should().NotBeNull();
        updatedTap.AssignedBatch!.Id.Should().Be(batch.Id);
        updatedTap.AssignedBatch.Name.Should().Be("Test Tap Batch");
        updatedTap.AssignedBatch.ActualAbv.Should().NotBeNull();

        // Unassign batch
        var unassignRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment");
        unassignRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var unassignEnvelope = await unassignRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        unassignEnvelope!.Data!.AssignedBatch.Should().BeNull();
    }

    [Fact]
    public async Task AssignTapBatch_WhenBatchInNonServableStage_FailsWithBadRequest()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("tapreject");
        var setup = await GetDefaultSetupAsync(client);
        var recipe = await CreateSampleRecipeAsync(client, "Planning Ale");
        var batch = await CreateSampleBatchAsync(client, recipe.Id, BrewStage.Mash);

        var createTapRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Kegerator Tap B",
            Type: EquipmentType.Tap,
            Subtype: EquipmentSubtype.StandardTap,
            Capacity: 0m,
            Unit: VolumeUnit.Liters
        ));
        var tap = (await createTapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var assignRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", new AssignTapRequest(batch.Id));
        assignRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TapDisplayLink_LifecycleAndPublicEndpoints_WorkSeamlessly()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("tapdisplay");
        var setup = await GetDefaultSetupAsync(client);
        var recipe = await CreateSampleRecipeAsync(client, "Display Amber");
        var batch = await CreateSampleBatchAsync(client, recipe.Id, BrewStage.Package);

        var createTapRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Bar Tap 1",
            Type: EquipmentType.Tap,
            Subtype: EquipmentSubtype.RectDisplayTap,
            Capacity: 0m,
            Unit: VolumeUnit.Liters,
            DisplayWidthPx: 320,
            DisplayHeightPx: 240,
            DisplayOrientation: DisplayOrientation.Landscape
        ));
        var tap = (await createTapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Assign batch
        await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", new AssignTapRequest(batch.Id));

        // Enable display link
        var enableRes = await client.PostAsync($"/api/v1/inventory/equipment/{tap.Id}/display-link", null);
        enableRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var linkEnvelope = await enableRes.Content.ReadFromJsonAsync<ApiResponse<TapDisplayLinkDto>>();
        var linkDto = linkEnvelope!.Data!;

        linkDto.ManifestUrl.Should().NotBeNullOrWhiteSpace();
        linkDto.ImageUrl.Should().NotBeNullOrWhiteSpace();

        var token = linkDto.ManifestUrl.TrimEnd('/').Split('/').Last();

        // Public manifest access without auth header
        var unauthClient = _factory.CreateClient();
        var manifestRes = await unauthClient.GetAsync(linkDto.ManifestUrl);
        manifestRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var manifestEnvelope = await manifestRes.Content.ReadFromJsonAsync<ApiResponse<TapDisplayManifestDto>>();
        var manifest = manifestEnvelope!.Data!;
        manifest.BeerName.Should().Be("Display Amber");
        manifest.ActualAbv.Should().NotBeNull();
        manifest.WidthPx.Should().Be(320);
        manifest.HeightPx.Should().Be(240);

        // Public image render access
        var imageRes = await unauthClient.GetAsync(linkDto.ImageUrl);
        imageRes.StatusCode.Should().Be(HttpStatusCode.OK);
        imageRes.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");

        // Revoke display link
        var revokeRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{tap.Id}/display-link");
        revokeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Manifest is now 404
        var deadManifestRes = await unauthClient.GetAsync(linkDto.ManifestUrl);
        deadManifestRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}