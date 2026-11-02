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
        manifest.ActualAbv.Should().Be(5.25m);
        manifest.WidthPx.Should().Be(320);
        manifest.HeightPx.Should().Be(240);

        // Public image render access with ABV overlay
        var imageRes = await unauthClient.GetAsync(linkDto.ImageUrl);
        imageRes.StatusCode.Should().Be(HttpStatusCode.OK);
        imageRes.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        var imageWithAbvBytes = await imageRes.Content.ReadAsByteArrayAsync();
        imageWithAbvBytes.Length.Should().BeGreaterThan(500);

        // Public image render access without ABV overlay
        var imageNoAbvRes = await unauthClient.GetAsync($"{linkDto.ImageUrl}?abv=false");
        imageNoAbvRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var imageNoAbvBytes = await imageNoAbvRes.Content.ReadAsByteArrayAsync();

        // The image with the ABV overlay badge has different pixels than without
        imageWithAbvBytes.Should().NotEqual(imageNoAbvBytes);

        // Revoke display link
        var revokeRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{tap.Id}/display-link");
        revokeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Manifest is now 404
        var deadManifestRes = await unauthClient.GetAsync(linkDto.ManifestUrl);
        deadManifestRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TapFlow_WithKegAndDirectBatch_HandlesFillLevelCorrectly()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("kegtapflow");
        var setup = await GetDefaultSetupAsync(client);
        var recipe = await CreateSampleRecipeAsync(client, "Keg Flow Stout");
        var batch = await CreateSampleBatchAsync(client, recipe.Id, BrewStage.Condition);

        // 1. Create a 20L Keg
        var createKegRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Corny Keg #1",
            Type: EquipmentType.Keg,
            Subtype: EquipmentSubtype.Cornelius,
            Capacity: 20m,
            Unit: VolumeUnit.Liters
        ));
        createKegRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var keg = (await createKegRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 2. Fill Keg with batch (15 liters)
        var fillKegRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{keg.Id}/keg-assignment", new AssignKegBatchRequest(batch.Id, 15.0m));
        fillKegRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var filledKeg = (await fillKegRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        filledKeg.AssignedBatch.Should().NotBeNull();
        filledKeg.CurrentVolumeLiters.Should().Be(15.0m);
        filledKeg.FillPercentage.Should().Be(75.0m);

        // 3. Create Tap
        var createTapRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Tap 1",
            Type: EquipmentType.Tap,
            Subtype: EquipmentSubtype.StandardTap,
            Capacity: 0m,
            Unit: VolumeUnit.Liters
        ));
        var tap = (await createTapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 4. Assign Keg to Tap
        var assignKegTapRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", new AssignTapRequest(KegId: keg.Id));
        assignKegTapRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedTapWithKeg = (await assignKegTapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        updatedTapWithKeg.AssignedKegId.Should().Be(keg.Id);
        updatedTapWithKeg.AssignedKeg.Should().NotBeNull();
        updatedTapWithKeg.AssignedKeg!.Name.Should().Be("Corny Keg #1");
        updatedTapWithKeg.AssignedBatch.Should().NotBeNull();
        updatedTapWithKeg.FillLevelPercent.Should().Be(75.0m);

        // 5. Check manifest shows 75%
        var enableRes = await client.PostAsync($"/api/v1/inventory/equipment/{tap.Id}/display-link", null);
        var linkDto = (await enableRes.Content.ReadFromJsonAsync<ApiResponse<TapDisplayLinkDto>>())!.Data!;
        var unauthClient = _factory.CreateClient();
        var manifestRes = await unauthClient.GetAsync(linkDto.ManifestUrl);
        var manifest = (await manifestRes.Content.ReadFromJsonAsync<ApiResponse<TapDisplayManifestDto>>())!.Data!;
        manifest.KegFillLevel.Should().Be("75%");
        manifest.KegName.Should().Be("Corny Keg #1");
        manifest.KegCapacityLiters.Should().Be(20.0m);
        manifest.KegRemainingLiters.Should().Be(15.0m);

        // 6. Direct Batch assignment to Tap (fallback mode)
        var assignBatchTapRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", new AssignTapRequest(BatchId: batch.Id));
        assignBatchTapRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedTapWithBatch = (await assignBatchTapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        updatedTapWithBatch.AssignedKegId.Should().BeNull();
        updatedTapWithBatch.AssignedKeg.Should().BeNull();
        updatedTapWithBatch.FillLevelPercent.Should().BeNull();

        // Manifest now shows "N/A"
        var manifestResDirect = await unauthClient.GetAsync(linkDto.ManifestUrl);
        var manifestDirect = (await manifestResDirect.Content.ReadFromJsonAsync<ApiResponse<TapDisplayManifestDto>>())!.Data!;
        manifestDirect.KegFillLevel.Should().Be("N/A");
        manifestDirect.KegName.Should().BeNull();

        // 7. Kick the keg
        var kickRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{keg.Id}/keg-assignment");
        kickRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var kickedKeg = (await kickRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        kickedKeg.AssignedBatch.Should().BeNull();
        kickedKeg.CurrentVolumeLiters.Should().Be(0m);
    }

    [Fact]
    public async Task TapDisplayLink_WhenUserConfiguredNetworkSettings_UsesConfiguredHostnames()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("tapnetwork");
        var setup = await GetDefaultSetupAsync(client);

        // Configure user network hostnames
        var prefRes = await client.PutAsJsonAsync("/api/v1/auth/me/preferences", new UpdateUserPreferencesRequest(
            AppBaseUrl: "http://192.168.1.188:3000",
            WebhookBaseUrl: "http://192.168.1.188:5000"
        ));
        prefRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Create tap
        var createTapRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Network Tap 1",
            Type: EquipmentType.Tap,
            Subtype: EquipmentSubtype.RectDisplayTap,
            Capacity: 0m,
            Unit: VolumeUnit.Liters
        ));
        var tap = (await createTapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Enable display link
        var enableRes = await client.PostAsync($"/api/v1/inventory/equipment/{tap.Id}/display-link", null);
        enableRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var linkDto = (await enableRes.Content.ReadFromJsonAsync<ApiResponse<TapDisplayLinkDto>>())!.Data!;

        linkDto.KioskUrl.Should().StartWith("http://192.168.1.188:3000/tap/");
        linkDto.ImageUrl.Should().StartWith("http://192.168.1.188:5000/api/v1/tap/");
        linkDto.ManifestUrl.Should().StartWith("http://192.168.1.188:5000/api/v1/tap/");

        // Query display link via GET
        var getRes = await client.GetAsync($"/api/v1/inventory/equipment/{tap.Id}/display-link");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getLinkDto = (await getRes.Content.ReadFromJsonAsync<ApiResponse<TapDisplayLinkDto>>())!.Data!;
        getLinkDto.KioskUrl.Should().StartWith("http://192.168.1.188:3000/tap/");
        getLinkDto.ImageUrl.Should().StartWith("http://192.168.1.188:5000/api/v1/tap/");
        getLinkDto.ManifestUrl.Should().StartWith("http://192.168.1.188:5000/api/v1/tap/");

        // Query public manifest - image URL must also use configured WebhookBaseUrl
        var token = linkDto.ManifestUrl.TrimEnd('/').Split('/').Last();
        var unauthClient = _factory.CreateClient();
        var manifestRes = await unauthClient.GetAsync($"/api/v1/tap/{token}");
        manifestRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifest = (await manifestRes.Content.ReadFromJsonAsync<ApiResponse<TapDisplayManifestDto>>())!.Data!;
        manifest.ImageUrl.Should().StartWith("http://192.168.1.188:5000/api/v1/tap/");
    }
}