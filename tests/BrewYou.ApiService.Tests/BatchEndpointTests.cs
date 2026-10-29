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

public class BatchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BatchEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string UserId, Guid BoilerId, Guid FermenterId, Guid KegId)> CreateAuthenticatedUserWithEquipmentAsync()
    {
        var client = _factory.CreateClient();
        var email = $"brewer_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, "Batch Master Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var token = envelope.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Fetch user default setup
        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        // Create Boiler (50L)
        var boilerReq = new CreateEquipmentRequest(defaultSetupId, "Grainfather G40", EquipmentType.Boiler, 45m);
        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", boilerReq);
        var boilerEnv = await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        var boilerId = boilerEnv!.Data!.Id;

        // Create Fermenter (30L)
        var fermenterReq = new CreateEquipmentRequest(defaultSetupId, "SS Conical 30L", EquipmentType.Fermenter, 30m);
        var fermenterRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", fermenterReq);
        var fermenterEnv = await fermenterRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        var fermenterId = fermenterEnv!.Data!.Id;

        // Create Keg (19L)
        var kegReq = new CreateEquipmentRequest(defaultSetupId, "Corny Keg #1", EquipmentType.Keg, 19m);
        var kegRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", kegReq);
        var kegEnv = await kegRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        var kegId = kegEnv!.Data!.Id;

        return (client, envelope.Data.User.Id, boilerId, fermenterId, kegId);
    }

    [Fact]
    public async Task BatchEndpoints_RequireAuthentication()
    {
        var client = _factory.CreateClient();

        var getRes = await client.GetAsync("/api/v1/batches");
        getRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var postRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, "B-2026-09-07-01", "Test Batch", "IPA", null, 20m, 1.050m, 1.010m, 5.25m, 40m, 6m, 60, 72m,
            null, null, null, null, null, null));
        postRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NextBatchCode_GeneratesFormattedDateCode()
    {
        var (client, _, _, _, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        var res = await client.GetAsync("/api/v1/batches/next-code");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<NextBatchCodeResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.BatchCode.Should().StartWith("B-");
    }

    [Fact]
    public async Task CreateBatch_AdHoc_OccupiesBoilerAndPersistsDetails()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        var req = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-{DateTime.UtcNow:yyyy-MM-dd}-99",
            Name: "Nordic Amber Ale",
            BeerStyle: "Amber Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.052m,
            TargetFg: 1.012m,
            TargetAbv: 5.25m,
            TargetIbu: 32m,
            TargetColorSrm: 12m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 74m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            MeasuredOg: 1.052m,
            PitchTemperatureC: 20.0m,
            Notes: "Brew day underway",
            CustomIngredients: new List<BatchIngredientInputDto>
            {
                new("Pale Ale Malt", IngredientType.Fermentable, 4.5m, "kg", IngredientUsage.Mash, null, null),
                new("Cascade", IngredientType.Hop, 25m, "g", IngredientUsage.Boil, 60, null)
            }
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/batches", req);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var createEnv = await createRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        createEnv.Should().NotBeNull();
        createEnv!.Success.Should().BeTrue();

        var batch = createEnv.Data!;
        batch.Name.Should().Be("Nordic Amber Ale");
        batch.CurrentStage.Should().Be(BrewStage.Mash);
        batch.Status.Should().Be(BatchStatus.Brewing);
        batch.TargetOg.Should().Be(1.052m);
        batch.TargetFg.Should().Be(1.012m);
        batch.TargetAbv.Should().Be(5.25m);
        batch.TargetIbu.Should().Be(32m);
        batch.TargetColorSrm.Should().Be(12m);
        batch.TargetBatchSizeLiters.Should().Be(20.0m);
        batch.BoilTimeMinutes.Should().Be(60);
        batch.EfficiencyPercent.Should().Be(74m);
        batch.Ingredients.Should().HaveCount(2);

        // Verify Boiler is occupied with batch volume
        var boilerRes = await client.GetAsync($"/api/v1/inventory/equipment/{boilerId}");
        var boilerEnv = await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        boilerEnv!.Data!.CurrentVolumeLiters.Should().Be(20.0m);
    }

    [Fact]
    public async Task AdvanceBatchStage_FromBoilToFerment_TransfersVolumeAndFreesBoiler()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Create Batch in Mash stage
        var createReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-{DateTime.UtcNow:yyyy-MM-dd}-88",
            Name: "Cascade Pale Ale",
            BeerStyle: "American Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 35m,
            TargetColorSrm: 7m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            MeasuredOg: null,
            PitchTemperatureC: null,
            Notes: null,
            CustomIngredients: null
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/batches", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batchId = (await createRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!.Id;

        // 2. Advance to Boil
        var advanceToBoilReq = new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredOg: null,
            MeasuredBatchSizeLiters: 21.0m,
            PitchTemperatureC: null,
            PackagingVesselId: null,
            MeasuredFg: null,
            Notes: "Pre-boil volume recorded"
        );
        var boilRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batchId}/stage", advanceToBoilReq);
        boilRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Advance to Ferment (chilled wort transferred from Boiler into Fermenter)
        var advanceToFermentReq = new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.052m,
            MeasuredBatchSizeLiters: 19.5m,
            PitchTemperatureC: 19.5m,
            PackagingVesselId: null,
            MeasuredFg: null,
            Notes: "Transferred to fermenter, pitched SafAle US-05"
        );
        var fermentRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batchId}/stage", advanceToFermentReq);
        fermentRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedBatchEnv = await fermentRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        updatedBatchEnv!.Data!.CurrentStage.Should().Be(BrewStage.Ferment);
        updatedBatchEnv.Data.Status.Should().Be(BatchStatus.Fermenting);
        updatedBatchEnv.Data.MeasuredOg.Should().Be(1.052m);

        // 4. Verify Equipment Occupancy Mutation
        // Boiler should now be freed (0L)
        var boilerRes = await client.GetAsync($"/api/v1/inventory/equipment/{boilerId}");
        var boilerEnv = await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        boilerEnv!.Data!.CurrentVolumeLiters.Should().Be(0m);

        // Fermenter should now be occupied with measured volume (19.5L)
        var fermenterRes = await client.GetAsync($"/api/v1/inventory/equipment/{fermenterId}");
        var fermenterEnv = await fermenterRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        fermenterEnv!.Data!.CurrentVolumeLiters.Should().Be(19.5m);
    }

    [Fact]
    public async Task AddBatchReading_RecalculatesCurrentGravityAndAbv()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        // Create Batch with Measured OG = 1.056
        var createReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-{DateTime.UtcNow:yyyy-MM-dd}-77",
            Name: "Session IPA",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.056m,
            TargetFg: 1.012m,
            TargetAbv: 5.7m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            MeasuredOg: 1.056m,
            PitchTemperatureC: 20.0m,
            Notes: null,
            CustomIngredients: null
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/batches", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batchId = (await createRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!.Id;

        // Log Fermentation Reading SG = 1.020
        var readingReq = new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: 1.020m,
            TemperatureC: 19.5m,
            Notes: "Day 3 vigorous krausen"
        );

        var readRes = await client.PostAsJsonAsync($"/api/v1/batches/{batchId}/readings", readingReq);
        readRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Fetch batch detail
        var detailRes = await client.GetAsync($"/api/v1/batches/{batchId}");
        var detailEnv = await detailRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        var batch = detailEnv!.Data!;

        batch.CurrentGravity.Should().Be(1.020m);
        batch.Readings.Should().HaveCount(1);
        batch.AlcoholByVolume.Should().Be(4.72m);
    }

    [Fact]
    public async Task CreateBatch_InitializesVolumeProfile_AndTracksStageVolumesThroughAdvancement()
    {
        var (client, _, boilerId, fermenterId, kegId) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Create batch with grain ingredients to test water schedule calculation
        var createReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-VOL-{Guid.NewGuid():N}"[..12],
            Name: "Volume Test Ale",
            BeerStyle: "Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 35m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            MeasuredOg: null,
            PitchTemperatureC: null,
            Notes: null,
            CustomIngredients: new List<BatchIngredientInputDto>
            {
                new("Maris Otter", IngredientType.Fermentable, 5.0m, "kg", IngredientUsage.Mash, null, null)
            }
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/batches", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var createEnv = await createRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        var batch = createEnv!.Data!;

        // Verify VolumeProfile is created and populated with water requirements
        batch.VolumeProfile.Should().NotBeNull();
        var vp = batch.VolumeProfile!;
        vp.TotalWaterLiters.Should().BeGreaterThan(25m);
        vp.StrikeWaterLiters.Should().Be(15.0m); // 5.0kg * 3.0 L/kg
        vp.TargetFermenterVolumeLiters.Should().Be(20.0m);
        vp.TargetPreBoilVolumeLiters.Should().BeGreaterThan(20.0m);
        vp.TargetPostBoilVolumeLiters.Should().BeGreaterThan(20.0m);

        // 2. Advance to Boil with pre-boil measurements
        var advanceToBoil = new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 28.5m,
            MeasuredPreBoilGravity: 1.044m
        );
        var boilRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage", advanceToBoil);
        boilRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var boilBatch = (await boilRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        boilBatch.VolumeProfile.Should().NotBeNull();
        boilBatch.VolumeProfile!.MeasuredPreBoilVolumeLiters.Should().Be(28.5m);
        boilBatch.VolumeProfile.MeasuredPreBoilGravity.Should().Be(1.044m);

        // 3. Advance to Ferment with flameout / into-fermenter measurements
        var advanceToFerment = new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.050m,
            MeasuredBatchSizeLiters: 20.2m,
            MeasuredPostBoilVolumeLiters: 22.0m,
            PitchTemperatureC: 19.0m
        );
        var fermentRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage", advanceToFerment);
        fermentRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var fermentBatch = (await fermentRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        fermentBatch.VolumeProfile!.MeasuredPostBoilVolumeLiters.Should().Be(22.0m);
        fermentBatch.VolumeProfile.MeasuredFermenterVolumeLiters.Should().Be(20.2m);

        // 4. Advance to Package with packaged volume
        var advanceToPackage = new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Package,
            PackagingVesselId: kegId,
            MeasuredPackagedVolumeLiters: 18.8m
        );
        var packageRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage", advanceToPackage);
        packageRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var packageBatch = (await packageRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        packageBatch.VolumeProfile!.MeasuredPackagedVolumeLiters.Should().Be(18.8m);
    }

    [Fact]
    public async Task CreateBatch_WithSpargeDisabled_CalculatesVolumeProfileCorrectly()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        var createReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-{DateTime.UtcNow:yyyy-MM-dd}-{Guid.NewGuid():N}"[..18],
            Name: "BIAB Pale Ale",
            BeerStyle: "American Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.2m,
            TargetIbu: 35m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            SpargeEnabled: false,
            CustomIngredients: new List<BatchIngredientInputDto>
            {
                new("Pilsner Malt", IngredientType.Fermentable, 5.0m, "kg", IngredientUsage.Mash, null, null)
            }
        );

        var res = await client.PostAsJsonAsync("/api/v1/batches", createReq);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        env.Should().NotBeNull();
        var batch = env!.Data!;
        batch.VolumeProfile.Should().NotBeNull();
        batch.VolumeProfile!.SpargeWaterLiters.Should().Be(0m);
        batch.VolumeProfile.StrikeWaterLiters.Should().Be(batch.VolumeProfile.TotalWaterLiters);
    }

    [Fact]
    public async Task UpdateBatch_UpdatesManualInputs_WithoutChangingCurrentStageOrStatus()
    {
        var (client, _, boilerId, fermenterId, kegId) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Create batch (starts in Mash)
        var createReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-{DateTime.UtcNow:yyyy-MM-dd}-88",
            Name: "Manual Input Test IPA",
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.055m,
            TargetFg: 1.012m,
            TargetAbv: 5.6m,
            TargetIbu: 45m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            MeasuredOg: null,
            PitchTemperatureC: null,
            Notes: "Initial notes",
            CustomIngredients: new List<BatchIngredientInputDto>
            {
                new("Pale 2-Row", IngredientType.Fermentable, 5.0m, "kg", IngredientUsage.Mash, null, null)
            }
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/batches", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await createRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // Advance to Boil
        var advanceToBoil = new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 28.0m,
            MeasuredPreBoilGravity: 1.045m
        );
        var boilRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage", advanceToBoil);
        boilRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var inBoilBatch = (await boilRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        inBoilBatch.CurrentStage.Should().Be(BrewStage.Boil);
        inBoilBatch.Status.Should().Be(BatchStatus.Brewing);

        // 2. User inspects Mash / Boil and updates manual measurements via PUT
        var updateReq = new UpdateBatchRequest(
            Notes: "Corrected pre-boil volume after cooling sample",
            MeasuredPreBoilVolumeLiters: 28.6m,
            MeasuredPreBoilGravity: 1.048m,
            MeasuredPostBoilVolumeLiters: 22.5m,
            MeasuredOg: 1.058m,
            MeasuredFg: 1.011m,
            MeasuredBatchSizeLiters: 20.5m,
            PitchTemperatureC: 18.5m,
            PackagingVesselId: kegId
        );

        var updateRes = await client.PutAsJsonAsync($"/api/v1/batches/{batch.Id}", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedEnv = await updateRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        var updated = updatedEnv!.Data!;

        // 3. Verify manual inputs updated
        updated.Notes.Should().Be("Corrected pre-boil volume after cooling sample");
        updated.VolumeProfile!.MeasuredPreBoilVolumeLiters.Should().Be(28.6m);
        updated.VolumeProfile.MeasuredPreBoilGravity.Should().Be(1.048m);
        updated.VolumeProfile.MeasuredPostBoilVolumeLiters.Should().Be(22.5m);
        updated.VolumeProfile.MeasuredFermenterVolumeLiters.Should().Be(20.5m);
        updated.MeasuredOg.Should().Be(1.058m);
        updated.MeasuredFg.Should().Be(1.011m);
        updated.CurrentGravity.Should().Be(1.011m);
        updated.PitchTemperatureC.Should().Be(18.5m);
        updated.PackagingVesselId.Should().Be(kegId);
        updated.AlcoholByVolume.Should().NotBeNull();
        updated.AlcoholByVolume!.Value.Should().BeGreaterThan(5.0m);
        updated.BrewhouseEfficiency.Should().NotBeNull();

        // 4. CRITICAL: CurrentStage and Status MUST REMAIN UNCHANGED
        updated.CurrentStage.Should().Be(BrewStage.Boil);
        updated.Status.Should().Be(BatchStatus.Brewing);
    }

    [Fact]
    public async Task CreateBatch_FromRecipeWithMultiStepMash_SnapshotsStepsAndAllowsToggling()
    {
        var (client, userId, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Create a recipe with multi-step mash
        var ingredients = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients"))!.Data!;
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);

        var recipeReq = new CreateRecipeRequest(
            Name: "Bavarian Weizen",
            Description: "Clove and banana wheat beer",
            BeerStyle: "Weissbier",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: true,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(grain.Id, 4.0m, "kg", 60, IngredientUsage.Mash, "Wheat Malt")
            },
            MashSteps: new List<RecipeMashStepInputDto>
            {
                new(1, "Ferulic Acid Rest", MashStepType.Temperature, 44.0m, 15, 5, null, "Precursor for 4VG clove"),
                new(2, "Saccharification", MashStepType.Temperature, 67.0m, 45, 10, null, "Medium body rest"),
                new(3, "Mash Out", MashStepType.Temperature, 76.0m, 10, 5, null, "Lautering viscosity")
            }
        );

        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 2. Create batch from recipe
        var batchReq = new CreateBatchRequest(
            RecipeId: recipe.Id,
            BatchCode: "B-2026-09-11-01",
            Name: "Bavarian Weizen Batch #1",
            BeerStyle: "Weissbier",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.012m,
            TargetAbv: 5.0m,
            TargetIbu: 15.0m,
            TargetColorSrm: 4.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            MeasuredOg: null,
            PitchTemperatureC: null,
            Notes: "Brewing today",
            CustomIngredients: null
        );

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 3. Verify mash steps were snapshotted
        batch.MashSteps.Should().HaveCount(3);
        batch.MashSteps[0].StepOrder.Should().Be(1);
        batch.MashSteps[0].Name.Should().Be("Ferulic Acid Rest");
        batch.MashSteps[0].TargetTemperatureC.Should().Be(44.0m);
        batch.MashSteps[0].DurationMinutes.Should().Be(15);
        batch.MashSteps[0].IsCompleted.Should().BeFalse();

        batch.MashSteps[1].StepOrder.Should().Be(2);
        batch.MashSteps[1].TargetTemperatureC.Should().Be(67.0m);

        batch.MashSteps[2].StepOrder.Should().Be(3);
        batch.MashSteps[2].TargetTemperatureC.Should().Be(76.0m);

        // 4. Toggle Step 1 to completed via PATCH
        var step1Id = batch.MashSteps[0].Id;
        var toggleReq = new ToggleBatchMashStepRequest(true, 44.5m, 16, "Hit temp spot on");
        var toggleRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/mash-steps/{step1Id}", toggleReq);
        toggleRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var toggleEnv = await toggleRes.Content.ReadFromJsonAsync<ApiResponse<BatchMashStepDto>>();
        toggleEnv.Should().NotBeNull();
        toggleEnv!.Data!.IsCompleted.Should().BeTrue();
        toggleEnv.Data.ActualTemperatureC.Should().Be(44.5m);
        toggleEnv.Data.ActualDurationMinutes.Should().Be(16);
        toggleEnv.Data.CompletedAt.Should().NotBeNull();

        // 5. Verify batch detail endpoint reflects updated step
        var getRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        var getBatch = (await getRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        var updatedStep1 = getBatch.MashSteps.First(s => s.Id == step1Id);
        updatedStep1.IsCompleted.Should().BeTrue();
        updatedStep1.ActualTemperatureC.Should().Be(44.5m);
    }

    [Fact]
    public async Task CreateBatch_WithSensorAsPackagingVessel_ReturnsBadRequest()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        // Fetch user default setup
        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        // Create an iSpindel sensor
        var sensorReq = new CreateEquipmentRequest(
            defaultSetupId,
            "Fermentation iSpindel",
            EquipmentType.Sensor,
            0m,
            Subtype: EquipmentSubtype.ISpindel
        );
        var sensorRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", sensorReq);
        sensorRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var sensor = (await sensorRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Attempt to start a batch with the iSpindel as the packaging vessel
        var batchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-ERR-{Guid.NewGuid():N}"[..16],
            Name: "Invalid Packaging Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.0m,
            TargetIbu: 30m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            PackagingVesselId: sensor.Id
        );

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errContent = await batchRes.Content.ReadAsStringAsync();
        errContent.Should().Contain("cannot be used as packaging vessels");
    }

    [Fact]
    public async Task CreateBatch_WithFermentationSensorAssignment_SucceedsAndPersistsSensor()
    {
        var (client, _, boilerId, fermenterId, kegId) = await CreateAuthenticatedUserWithEquipmentAsync();

        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        // Create a Tilt hydrometer sensor
        var tiltReq = new CreateEquipmentRequest(
            defaultSetupId,
            "Red Tilt Hydrometer",
            EquipmentType.Sensor,
            0m,
            Subtype: EquipmentSubtype.Tilt
        );
        var tiltRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", tiltReq);
        tiltRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var tilt = (await tiltRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var batchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-TILT-{Guid.NewGuid():N}"[..16],
            Name: "Tilt Fermentation Batch",
            BeerStyle: "Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.0m,
            TargetIbu: 30m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            PackagingVesselId: kegId,
            SensorAssignments: new List<BatchSensorAssignmentInput>
            {
                new(tilt.Id, BrewStage.Ferment, null)
            }
        );

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        batch.SensorAssignments.Should().NotBeNull();
        batch.SensorAssignments.Should().ContainSingle(s => s.EquipmentId == tilt.Id && s.Stage == BrewStage.Ferment);
    }

    [Fact]
    public async Task CheckStock_DetectsIngredientDeficits_AndAdvanceStage_DeductsStock()
    {
        var (client, _, boilerId, fermenterId, kegId) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Fetch catalog ingredients
        var hops = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Hop"))!.Data!;
        var malts = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Fermentable"))!.Data!;
        var hop = hops.First();
        var malt = malts.First();

        // 2. Set stock: 2.0 kg malt in stock, 0 g hop in stock
        await client.PutAsJsonAsync($"/api/v1/ingredients/{malt.Id}/stock", new UpdateIngredientStockRequest(2.0m, "kg"));
        await client.PutAsJsonAsync($"/api/v1/ingredients/{hop.Id}/stock", new UpdateIngredientStockRequest(0m, "g"));

        // 3. Create a recipe requiring 5.0 kg malt and 50 g hop for 20L
        var recipeReq = new CreateRecipeRequest(
            Name: "Stock Test IPA",
            Description: "Testing stock check",
            BeerStyle: "American IPA",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(malt.Id, 5.0m, "kg", null, IngredientUsage.Mash, null),
                new(hop.Id, 50m, "g", 60, IngredientUsage.Boil, null)
            }
        );
        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 4. Call POST /api/v1/batches/check-stock
        var checkReq = new CheckBatchStockRequest(recipe.Id, 20.0m);
        var checkRes = await client.PostAsJsonAsync("/api/v1/batches/check-stock", checkReq);
        checkRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var checkResult = (await checkRes.Content.ReadFromJsonAsync<ApiResponse<BatchStockCheckResult>>())!.Data!;
        checkResult.HasShortage.Should().BeTrue();
        checkResult.Shortages.Should().HaveCount(2);

        var maltShortage = checkResult.Shortages.First(s => s.IngredientId == malt.Id);
        maltShortage.RequiredAmount.Should().Be(5.0m);
        maltShortage.StockAmount.Should().Be(2.0m);
        maltShortage.Deficit.Should().Be(3.0m);

        var hopShortage = checkResult.Shortages.First(s => s.IngredientId == hop.Id);
        hopShortage.RequiredAmount.Should().Be(50m);
        hopShortage.StockAmount.Should().Be(0m);
        hopShortage.Deficit.Should().Be(50m);

        // 5. Start batch anyway
        var batchReq = new CreateBatchRequest(
            RecipeId: recipe.Id,
            BatchCode: $"B-STOCK-{Guid.NewGuid():N}"[..16],
            Name: "Stock Deduct Batch",
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.0m,
            TargetIbu: 30m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            PackagingVesselId: kegId
        );

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        batch.CurrentStage.Should().Be(BrewStage.Mash);

        // Verify mash ingredients are not deducted yet
        batch.Ingredients.Should().Contain(i => i.AdditionStage == IngredientUsage.Mash && !i.IsDeducted);

        // 6. Advance stage from Mash to Boil -> Should deduct mash ingredients automatically
        var advanceReq = new AdvanceBatchStageRequest(BrewStage.Boil);
        var advanceRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage", advanceReq);
        advanceRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var advancedBatch = (await advanceRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        advancedBatch.CurrentStage.Should().Be(BrewStage.Boil);
        advancedBatch.Ingredients.First(i => i.AdditionStage == IngredientUsage.Mash).IsDeducted.Should().BeTrue();

        // 7. Verify stock for malt was deducted and clamped at 0
        var maltDetailRes = await client.GetFromJsonAsync<ApiResponse<IngredientDto>>($"/api/v1/ingredients/{malt.Id}");
        maltDetailRes!.Data!.StockAmount.Should().Be(0m);
    }

    [Fact]
    public async Task AdvanceStage_ToFerment_WithMultipleAdditionsOfSameIngredient_SucceedsWithoutDuplicateKeyError()
    {
        var (client, _, boilerId, fermenterId, kegId) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Fetch a catalog hop
        var hops = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Hop"))!.Data!;
        var hop = hops.First();

        // 2. Create recipe with TWO additions of the same hop in Boil stage
        var recipeReq = new CreateRecipeRequest(
            Name: "Double Citra Boil Ale",
            Description: "Testing boil stage advance",
            BeerStyle: "American IPA",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(hop.Id, 25m, "g", 60, IngredientUsage.Boil, "First boil addition"),
                new(hop.Id, 50m, "g", 15, IngredientUsage.Boil, "Second boil addition")
            }
        );

        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 3. Create batch
        var batchReq = new CreateBatchRequest(
            RecipeId: recipe.Id,
            BatchCode: $"B-CITRA-{Guid.NewGuid():N}"[..16],
            Name: "Double Citra Batch",
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.0m,
            TargetIbu: 30m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            PackagingVesselId: kegId
        );

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 4. Advance from Mash to Boil
        var advanceToBoilRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage", new AdvanceBatchStageRequest(BrewStage.Boil));
        advanceToBoilRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Advance from Boil to Ferment with the payload similar to user report
        var advanceToFermentReq = new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredBatchSizeLiters: 25m,
            MeasuredOg: 1.05m,
            PitchTemperatureC: 20m,
            MeasuredPostBoilVolumeLiters: 26.5m
        );

        var advanceToFermentRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/stage", advanceToFermentReq);
        advanceToFermentRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var advancedBatch = (await advanceToFermentRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        advancedBatch.CurrentStage.Should().Be(BrewStage.Ferment);
        advancedBatch.Status.Should().Be(BatchStatus.Fermenting);
        advancedBatch.Ingredients.Where(i => i.AdditionStage == IngredientUsage.Boil).All(i => i.IsDeducted).Should().BeTrue();
    }

    [Fact]
    public async Task CreateBatch_FromRecipe_WithFermenterBasis_ScalesIngredientsByFermenterVolume_AndPreservesTargetSpecs()
    {
        var (client, userId, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Fetch catalog ingredients and create a recipe with standard 20L into fermenter
        var hops = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Hop"))!.Data!;
        var malts = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Fermentable"))!.Data!;
        var hop = hops.First();
        var malt = malts.First();

        var recipeReq = new CreateRecipeRequest(
            Name: "Bohemian Pilsner",
            Description: "Crisp lagering test",
            BeerStyle: "Czech Premium Pale Lager",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(malt.Id, 5.0m, "kg", null, IngredientUsage.Mash, null),
                new(hop.Id, 60m, "g", 60, IngredientUsage.Boil, null)
            }
        );
        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 2. Create batch scaled to 25L with TargetVolumeBasis = Fermenter
        var batchReq = new CreateBatchRequest(
            RecipeId: recipe.Id,
            BatchCode: $"B-FERM-{Guid.NewGuid():N}"[..16],
            Name: "Scaled Pilsner Batch",
            BeerStyle: recipe.BeerStyle,
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 25.0m,
            TargetOg: recipe.OriginalGravity,
            TargetFg: recipe.FinalGravity,
            TargetAbv: recipe.AlcoholByVolume,
            TargetIbu: recipe.BitternessIbu,
            TargetColorSrm: recipe.ColorSrm,
            BoilTimeMinutes: recipe.BoilTimeMinutes,
            EfficiencyPercent: recipe.EfficiencyPercent,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            TargetVolumeBasis: TargetVolumeBasis.Fermenter
        );

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 3. Verify target specs remain invariant (from recipe)
        batch.TargetBatchSizeLiters.Should().Be(25.0m);
        batch.TargetOg.Should().Be(recipe.OriginalGravity);
        batch.TargetFg.Should().Be(recipe.FinalGravity);
        batch.TargetAbv.Should().Be(recipe.AlcoholByVolume);
        batch.TargetIbu.Should().Be(recipe.BitternessIbu);
        batch.TargetColorSrm.Should().Be(recipe.ColorSrm);

        // 4. Verify ingredients scaled by 25 / 20 = 1.25
        var scaledMalt = batch.Ingredients.First(i => i.SourceIngredientId == malt.Id);
        scaledMalt.Amount.Should().Be(6.25m); // 5.0 * 1.25

        var scaledHop = batch.Ingredients.First(i => i.SourceIngredientId == hop.Id);
        scaledHop.Amount.Should().Be(75.0m); // 60 * 1.25

        // 5. Verify VolumeProfile
        batch.VolumeProfile.Should().NotBeNull();
        batch.VolumeProfile!.TargetFermenterVolumeLiters.Should().Be(25.0m);
    }

    [Fact]
    public async Task CreateBatch_FromRecipe_WithPackagedBasis_ScalesIngredientsByRequiredFermenterVolume_AndPreservesTargetSpecs()
    {
        var (client, userId, boilerId, fermenterId, kegId) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Fetch catalog ingredients and create a recipe with standard 20L into fermenter
        var hops = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Hop"))!.Data!;
        var malts = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Fermentable"))!.Data!;
        var hop = hops.First();
        var malt = malts.First();

        var recipeReq = new CreateRecipeRequest(
            Name: "Hazy Pale",
            Description: "Juicy recipe test",
            BeerStyle: "American Pale Ale",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72.0m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(malt.Id, 4.0m, "kg", null, IngredientUsage.Mash, null),
                new(hop.Id, 40m, "g", 15, IngredientUsage.Boil, null)
            }
        );
        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 2. Create batch with TargetBatchSize = 19L (keg size), TargetVolumeBasis = Packaged
        // Fermenter loss = 1.5L, Packaging loss = 0.5L -> Required fermenter volume = 21.0L
        var batchReq = new CreateBatchRequest(
            RecipeId: recipe.Id,
            BatchCode: $"B-PKG-{Guid.NewGuid():N}"[..16],
            Name: "Kegged Hazy Batch",
            BeerStyle: recipe.BeerStyle,
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 19.0m,
            TargetOg: recipe.OriginalGravity,
            TargetFg: recipe.FinalGravity,
            TargetAbv: recipe.AlcoholByVolume,
            TargetIbu: recipe.BitternessIbu,
            TargetColorSrm: recipe.ColorSrm,
            BoilTimeMinutes: recipe.BoilTimeMinutes,
            EfficiencyPercent: recipe.EfficiencyPercent,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            PackagingVesselId: kegId,
            TargetVolumeBasis: TargetVolumeBasis.Packaged,
            PackagingLossLiters: 0.5m,
            FermenterLossLiters: 1.5m
        );

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 3. Verify target specifications remain invariant from recipe
        batch.TargetBatchSizeLiters.Should().Be(19.0m);
        batch.TargetOg.Should().Be(recipe.OriginalGravity);
        batch.TargetFg.Should().Be(recipe.FinalGravity);
        batch.TargetAbv.Should().Be(recipe.AlcoholByVolume);
        batch.TargetIbu.Should().Be(recipe.BitternessIbu);

        // 4. Verify ingredients scaled by required fermenter volume: 21.0 / 20.0 = 1.05
        var scaledMalt = batch.Ingredients.First(i => i.SourceIngredientId == malt.Id);
        scaledMalt.Amount.Should().Be(4.2m); // 4.0 * 1.05

        var scaledHop = batch.Ingredients.First(i => i.SourceIngredientId == hop.Id);
        scaledHop.Amount.Should().Be(42.0m); // 40 * 1.05

        // 5. Verify VolumeProfile has both targets properly mapped
        batch.VolumeProfile.Should().NotBeNull();
        batch.VolumeProfile!.TargetPackagedVolumeLiters.Should().Be(19.0m);
        batch.VolumeProfile!.TargetFermenterVolumeLiters.Should().Be(21.0m);
    }

    [Fact]
    public async Task CheckRecipeStock_WithPackagedBasis_ScalesStockRequirementAgainstFermenterVolume()
    {
        var (client, userId, _, _, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Fetch catalog malt and set 4.1 kg in stock
        var malts = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Fermentable"))!.Data!;
        var malt = malts.First();
        var stockReq = new UpdateIngredientStockRequest(4.1m, "kg");
        var stockRes = await client.PutAsJsonAsync($"/api/v1/ingredients/{malt.Id}/stock", stockReq);
        stockRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Create recipe requiring 4.0 kg for 20L into fermenter
        var recipeReq = new CreateRecipeRequest(
            Name: "Munich Dunkel",
            Description: "Malty test",
            BeerStyle: "Munich Dunkel",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(malt.Id, 4.0m, "kg", null, IngredientUsage.Mash, null)
            }
        );
        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 3. Check stock with TargetVolumeBasis = Packaged (19L target + 1.5L fermenter loss + 0.5L pkg loss = 21L fermenter)
        // Scaled requirement = 4.0 * (21 / 20) = 4.2 kg. Since stock is 4.1 kg, there should be a 0.1 kg shortage!
        var checkReq = new CheckBatchStockRequest(
            RecipeId: recipe.Id,
            TargetBatchSizeLiters: 19.0m,
            TargetVolumeBasis: TargetVolumeBasis.Packaged,
            FermenterLossLiters: 1.5m,
            PackagingLossLiters: 0.5m
        );
        var checkRes = await client.PostAsJsonAsync("/api/v1/batches/check-stock", checkReq);
        checkRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var checkResult = (await checkRes.Content.ReadFromJsonAsync<ApiResponse<BatchStockCheckResult>>())!.Data!;
        checkResult.HasShortage.Should().BeTrue();
        checkResult.Shortages.Should().HaveCount(1);
        checkResult.Shortages[0].RequiredAmount.Should().Be(4.2m);
        checkResult.Shortages[0].StockAmount.Should().Be(4.1m);
        checkResult.Shortages[0].Deficit.Should().Be(0.1m);
    }

    [Fact]
    public async Task CreateBatch_WithMultipleHops_OrdersIngredientsChronologically_FirstHopAddedFirst()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        var hops = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Hop"))!.Data!;
        var malts = (await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients?type=Fermentable"))!.Data!;

        var malt = malts.First();
        var hop = hops.First();

        // 1. Create a recipe where ingredients are provided out of chronological order:
        // Flameout (0 min), Late boil (15 min), Mash, Bittering (60 min)
        var recipeReq = new CreateRecipeRequest(
            Name: "Chronological Hop Test",
            Description: "Recipe for hop addition order",
            BeerStyle: "IPA",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(hop.Id, 20m, "g", 0, IngredientUsage.Boil, "Flameout hop"),
                new(hop.Id, 25m, "g", 15, IngredientUsage.Boil, "15m hop"),
                new(malt.Id, 5.0m, "kg", null, IngredientUsage.Mash, "Base malt"),
                new(hop.Id, 30m, "g", 60, IngredientUsage.Boil, "First hop addition (60m)")
            }
        );

        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // Verify recipe ingredients are ordered chronologically:
        // Mash -> 60m boil -> 15m boil -> 0m boil
        recipe.Ingredients.Should().HaveCount(4);
        recipe.Ingredients[0].Usage.Should().Be(IngredientUsage.Mash);
        recipe.Ingredients[1].DurationMinutes.Should().Be(60);
        recipe.Ingredients[2].DurationMinutes.Should().Be(15);
        recipe.Ingredients[3].DurationMinutes.Should().Be(0);

        // 2. Create batch from recipe
        var batchReq = new CreateBatchRequest(
            RecipeId: recipe.Id,
            BatchCode: $"B-ORD-{Guid.NewGuid():N}"[..16],
            Name: "Batch Chronological Test",
            BeerStyle: recipe.BeerStyle,
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: recipe.OriginalGravity,
            TargetFg: recipe.FinalGravity,
            TargetAbv: recipe.AlcoholByVolume,
            TargetIbu: recipe.BitternessIbu,
            TargetColorSrm: recipe.ColorSrm,
            BoilTimeMinutes: recipe.BoilTimeMinutes,
            EfficiencyPercent: recipe.EfficiencyPercent,
            BoilerId: boilerId,
            FermenterId: fermenterId
        );

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // Verify batch ingredients preserve chronological order:
        // Mash -> 60m boil (first hop added) -> 15m boil -> 0m boil
        batch.Ingredients.Should().HaveCount(4);
        batch.Ingredients[0].AdditionStage.Should().Be(IngredientUsage.Mash);
        batch.Ingredients[1].AdditionTimeMinutes.Should().Be(60);
        batch.Ingredients[2].AdditionTimeMinutes.Should().Be(15);
        batch.Ingredients[3].AdditionTimeMinutes.Should().Be(0);
    }

    [Fact]
    public async Task CreateBatch_WithCustomIngredients_OrdersIngredientsChronologically()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        var customIngredients = new List<BatchIngredientInputDto>
        {
            new("Dry Hop Centennial", IngredientType.Hop, 50m, "g", IngredientUsage.DryHop, 3, "Day 3 dry hop"),
            new("Aroma Hop 10m", IngredientType.Hop, 30m, "g", IngredientUsage.Boil, 10, "10 min addition"),
            new("Pilsner Malt", IngredientType.Fermentable, 5.0m, "kg", IngredientUsage.Mash, 60, "Mash in"),
            new("Bittering Hop 60m", IngredientType.Hop, 25m, "g", IngredientUsage.Boil, 60, "60 min addition"),
            new("US-05 Yeast", IngredientType.Yeast, 11.5m, "g", IngredientUsage.Primary, 0, "Pitch at fermentation start"),
            new("Whirlpool Hop 0m", IngredientType.Hop, 40m, "g", IngredientUsage.Boil, 0, "Flameout addition")
        };

        var batchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-ADHOC-{Guid.NewGuid():N}"[..16],
            Name: "AdHoc Chronological Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.055m,
            TargetFg: 1.012m,
            TargetAbv: 5.6m,
            TargetIbu: 45m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            CustomIngredients: customIngredients
        );

        var res = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // Chronological order:
        // 1. Mash (Pilsner Malt)
        // 2. Boil 60m (Bittering Hop 60m) - first hop addition!
        // 3. Boil 10m (Aroma Hop 10m)
        // 4. Boil 0m (Whirlpool Hop 0m)
        // 5. Primary (US-05 Yeast)
        // 6. DryHop (Dry Hop Centennial)
        batch.Ingredients.Should().HaveCount(6);
        batch.Ingredients[0].Name.Should().Be("Pilsner Malt");
        batch.Ingredients[0].AdditionStage.Should().Be(IngredientUsage.Mash);

        batch.Ingredients[1].Name.Should().Be("Bittering Hop 60m");
        batch.Ingredients[1].AdditionStage.Should().Be(IngredientUsage.Boil);
        batch.Ingredients[1].AdditionTimeMinutes.Should().Be(60);

        batch.Ingredients[2].Name.Should().Be("Aroma Hop 10m");
        batch.Ingredients[2].AdditionStage.Should().Be(IngredientUsage.Boil);
        batch.Ingredients[2].AdditionTimeMinutes.Should().Be(10);

        batch.Ingredients[3].Name.Should().Be("Whirlpool Hop 0m");
        batch.Ingredients[3].AdditionStage.Should().Be(IngredientUsage.Boil);
        batch.Ingredients[3].AdditionTimeMinutes.Should().Be(0);

        batch.Ingredients[4].Name.Should().Be("US-05 Yeast");
        batch.Ingredients[4].AdditionStage.Should().Be(IngredientUsage.Primary);

        batch.Ingredients[5].Name.Should().Be("Dry Hop Centennial");
        batch.Ingredients[5].AdditionStage.Should().Be(IngredientUsage.DryHop);
    }

    [Fact]
    public async Task CreateBatch_WithNegativeAdditionTime_ReturnsBadRequest()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        var customIngredients = new List<BatchIngredientInputDto>
        {
            new("Invalid Hop", IngredientType.Hop, 20m, "g", IngredientUsage.Boil, -5, "Negative time")
        };

        var batchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-NEG-{Guid.NewGuid():N}"[..16],
            Name: "Invalid Addition Time Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.2m,
            TargetIbu: 30m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 70m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            CustomIngredients: customIngredients
        );

        var res = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateBatch_WhenPreBoilVolumeEdited_RecalculatesDownstreamMilestones()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        var batchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-VOL-{Guid.NewGuid():N}"[..16],
            Name: "Volume Milestone Test Batch",
            BeerStyle: "Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.2m,
            TargetIbu: 30m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        created.VolumeProfile.Should().NotBeNull();
        var initialPostBoilTarget = created.VolumeProfile!.TargetPostBoilVolumeLiters;

        // Act: Update MeasuredPreBoilVolumeLiters
        var updateReq = new UpdateBatchRequest(
            MeasuredPreBoilVolumeLiters: 28.0m
        );
        var updateRes = await client.PutAsJsonAsync($"/api/v1/batches/{created.Id}", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await updateRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // Assert: Post-boil, fermenter, and packaged targets should be recalculated based on 28.0L pre-boil
        updated.VolumeProfile.Should().NotBeNull();
        updated.VolumeProfile!.MeasuredPreBoilVolumeLiters.Should().Be(28.0m);
        updated.VolumeProfile.TargetPostBoilVolumeLiters.Should().NotBe(initialPostBoilTarget);
        updated.VolumeProfile.TargetFermenterVolumeLiters.Should().Be(
            Math.Max(0m, Math.Round(updated.VolumeProfile.TargetPostBoilVolumeLiters - updated.VolumeProfile.KettleTrubLossLiters, 2))
        );
    }

    [Fact]
    public async Task CreateBatch_FromRecipeWithFermentationSteps_InheritsAndAllowsStepToggle()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Fetch catalog ingredients
        var ingRes = await client.GetAsync("/api/v1/ingredients");
        var ings = (await ingRes.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        var grain = ings.First(i => i.Type == IngredientType.Fermentable);
        var yeast = ings.First(i => i.Type == IngredientType.Yeast);

        // 2. Create recipe with 2 fermentation steps
        var recipeReq = new CreateRecipeRequest(
            Name: "IPA with Staged Ferm",
            BeerStyle: "American IPA",
            Description: "Recipe with primary and dry hop temp rest",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: true,
            Ingredients:
            [
                new(grain.Id, 5.0m, "kg", null, IngredientUsage.Mash, null),
                new(yeast.Id, 11.5m, "g", null, IngredientUsage.Primary, null)
            ],
            MashSteps: null,
            FermentationSteps:
            [
                new(1, "Primary", FermentationStepType.Primary, 19.0m, 7, null, null, null),
                new(2, "Dry Hop Rest", FermentationStepType.Secondary, 16.0m, 4, null, null, null)
            ]
        );

        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;

        // 3. Create batch from this recipe
        var batchCode = $"B-FERM-{Guid.NewGuid():N}"[..14];
        var batchReq = new CreateBatchRequest(
            RecipeId: recipe.Id,
            BatchCode: batchCode,
            Name: "Live Batch with Ferm Profile",
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.055m,
            TargetFg: 1.012m,
            TargetAbv: 5.8m,
            TargetIbu: 45m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        );

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        batch.FermentationSteps.Should().HaveCount(2);
        var step1 = batch.FermentationSteps[0];
        step1.Name.Should().Be("Primary");
        step1.TargetTemperatureC.Should().Be(19.0m);
        step1.DurationDays.Should().Be(7);
        step1.IsCompleted.Should().BeFalse();
        step1.CompletedAt.Should().BeNull();

        var step2 = batch.FermentationSteps[1];
        step2.Name.Should().Be("Dry Hop Rest");
        step2.TargetTemperatureC.Should().Be(16.0m);

        // 4. Toggle step1 to completed
        var toggleReq = new ToggleBatchFermentationStepRequest(true);
        var toggleRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/fermentation-steps/{step1.Id}", toggleReq);
        toggleRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var toggledStep = (await toggleRes.Content.ReadFromJsonAsync<ApiResponse<BatchFermentationStepDto>>())!.Data!;

        toggledStep.IsCompleted.Should().BeTrue();
        toggledStep.CompletedAt.Should().NotBeNull();

        // 5. Verify batch detail reflects step1 completion
        var getRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        var fetched = (await getRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        fetched.FermentationSteps.First(s => s.Id == step1.Id).IsCompleted.Should().BeTrue();

        // 6. Toggle step1 back to false
        var uncheckReq = new ToggleBatchFermentationStepRequest(false);
        var uncheckRes = await client.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/fermentation-steps/{step1.Id}", uncheckReq);
        uncheckRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var uncheckedStep = (await uncheckRes.Content.ReadFromJsonAsync<ApiResponse<BatchFermentationStepDto>>())!.Data!;
        uncheckedStep.IsCompleted.Should().BeFalse();
        uncheckedStep.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateBatch_AllInOneWithSpargeHeater_PersistsBothVesselsAndCalculatesSplit()
    {
        var (client, userId, _, _, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        // 1. Fetch default brewery setup
        var setups = (await (await client.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var setupId = setups.First(s => s.IsDefault).Id;

        // 2. Create All-In-One (Grainfather) and HLT (Sparge water heater)
        var aioRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Grainfather G30",
            Type: EquipmentType.Boiler,
            Capacity: 30.0m,
            Subtype: EquipmentSubtype.AllInOne,
            MashTunDeadSpaceLiters: 2.0m
        ));
        aioRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var aio = (await aioRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var hltRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Grainfather Sparge Water Heater 18L",
            Type: EquipmentType.Boiler,
            Capacity: 18.0m,
            Subtype: EquipmentSubtype.Hlt
        ));
        hltRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var hlt = (await hltRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 3. Create batch assigning AIO for boiling and mash tun, and HLT for sparge water
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "B-AIO-01",
            Name: "Saison with AIO + Sparge Heater",
            BeerStyle: "Saison",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 23.0m,
            TargetOg: 1.052m,
            TargetFg: 1.006m,
            TargetAbv: 6.0m,
            TargetIbu: 30.0m,
            TargetColorSrm: 4.5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            BoilerId: aio.Id,
            FermenterId: null,
            PackagingVesselId: null,
            HltId: hlt.Id,
            MashTunId: aio.Id,
            HltDeadSpaceLiters: 0.5m,
            SpargeMethod: "Batch"
        ));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        batch.BoilerId.Should().Be(aio.Id);
        batch.MashTunId.Should().Be(aio.Id);
        batch.HltId.Should().Be(hlt.Id);
        batch.HltName.Should().Be("Grainfather Sparge Water Heater 18L");
        batch.VolumeProfile.Should().NotBeNull();
        batch.VolumeProfile!.HltDeadSpaceLiters.Should().Be(0.5m);
        batch.VolumeProfile!.StrikeWaterLiters.Should().BeGreaterThan(0m);
        batch.VolumeProfile!.SpargeWaterLiters.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task CreateBatch_ThreeVesselTraditional_PersistsHltMashTunAndBoiler()
    {
        var (client, userId, _, _, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        var setups = (await (await client.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var setupId = setups.First(s => s.IsDefault).Id;

        // 1. Create 3 distinct vessels
        var hlt = (await (await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId, Name: "3V Hot Liquor Tank", Type: EquipmentType.Boiler, Capacity: 50m, Subtype: EquipmentSubtype.Hlt
        ))).Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var mlt = (await (await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId, Name: "3V Mash Lauter Tun", Type: EquipmentType.Boiler, Capacity: 50m, Subtype: EquipmentSubtype.MashTun, MashTunDeadSpaceLiters: 1.5m
        ))).Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var bk = (await (await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setupId, Name: "3V Boil Kettle", Type: EquipmentType.Boiler, Capacity: 50m, Subtype: EquipmentSubtype.Pan
        ))).Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 2. Create batch
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "B-3V-01",
            Name: "Imperial Stout 3V",
            BeerStyle: "Imperial Stout",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 35.0m,
            TargetOg: 1.090m,
            TargetFg: 1.020m,
            TargetAbv: 9.2m,
            TargetIbu: 75.0m,
            TargetColorSrm: 40.0m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 78.0m,
            BoilerId: bk.Id,
            FermenterId: null,
            PackagingVesselId: null,
            HltId: hlt.Id,
            MashTunId: mlt.Id,
            PlumbingLossLiters: 0.8m,
            SpargeMethod: "Fly"
        ));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        batch.HltId.Should().Be(hlt.Id);
        batch.MashTunId.Should().Be(mlt.Id);
        batch.BoilerId.Should().Be(bk.Id);
        batch.VolumeProfile!.PlumbingLossLiters.Should().Be(0.8m);
        batch.VolumeProfile!.SpargeMethod.Should().Be("Fly");
    }

    [Fact]
    public async Task LogBatchEquipmentReading_NoBody_ReturnsBadRequest()
    {
        var (client, _, boilerId, fermenterId, _) = await CreateAuthenticatedUserWithEquipmentAsync();

        var createReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-{Guid.NewGuid():N}"[..18],
            Name: "Test Ale",
            BeerStyle: "American Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.2m,
            TargetIbu: 35.0m,
            TargetColorSrm: 7.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        );
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", createReq);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/batches/{batch.Id}/equipment-readings");
        // No body
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var env = await response.Content.ReadFromJsonAsync<ApiResponse>();
        env!.Success.Should().BeFalse();
        env.Error.Should().NotBeNull();
        env.Error!.Code.Should().Be("BAD_REQUEST");
    }

    [Fact]
    public async Task BatchEndpoints_EnforceForwardOnlyStage_CompletedIngredientGuard_AndSensorStepOwnership()
    {
        var (client, _, boilerId, fermenterId, kegId) = await CreateAuthenticatedUserWithEquipmentAsync();

        var setups = (await (await client.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var setupId = setups.First(s => s.IsDefault).Id;

        var sensor = (await (await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            setupId, "Mash Probe #1", EquipmentType.Sensor, 0m, Subtype: EquipmentSubtype.GenericSensor
        ))).Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var batchA = (await (await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, "B-P1-A", "Batch A", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, boilerId, fermenterId,
            CustomIngredients: [new("Cascade", IngredientType.Hop, 25m, "g", IngredientUsage.Boil, 60, null)]
        ))).Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        var batchB = (await (await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, "B-P1-B", "Batch B", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, boilerId, fermenterId
        ))).Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 1. Cross-batch BatchMashStepId on PUT /sensors and PUT /batches/{id} -> 400 BadRequest
        var badSensorsRes = await client.PutAsJsonAsync(
            $"/api/v1/batches/{batchA.Id}/sensors",
            new List<BatchSensorAssignmentInput> { new(sensor.Id, BrewStage.Mash, batchB.MashSteps[0].Id) });
        badSensorsRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badUpdateRes = await client.PutAsJsonAsync(
            $"/api/v1/batches/{batchA.Id}",
            new UpdateBatchRequest(SensorAssignments: [new(sensor.Id, BrewStage.Mash, batchB.MashSteps[0].Id)]));
        badUpdateRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Advance Mash -> Boil, then attempt backward Boil -> Mash -> 409 Conflict
        var toBoilRes = await client.PatchAsJsonAsync(
            $"/api/v1/batches/{batchA.Id}/stage", new AdvanceBatchStageRequest(BrewStage.Boil));
        toBoilRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var backwardRes = await client.PatchAsJsonAsync(
            $"/api/v1/batches/{batchA.Id}/stage", new AdvanceBatchStageRequest(BrewStage.Mash));
        backwardRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Advance Boil -> Ferment -> Package (skipping Condition)
        var toFermRes = await client.PatchAsJsonAsync(
            $"/api/v1/batches/{batchA.Id}/stage", new AdvanceBatchStageRequest(BrewStage.Ferment, MeasuredOg: 1.051m));
        toFermRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var toPkgRes = await client.PatchAsJsonAsync(
            $"/api/v1/batches/{batchA.Id}/stage",
            new AdvanceBatchStageRequest(BrewStage.Package, PackagingVesselId: kegId, MeasuredFg: 1.010m));
        toPkgRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var completedBatch = (await toPkgRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        completedBatch.StageHistory.First(h => h.Stage == BrewStage.Condition).Status.Should().Be(StageStatus.Skipped);
        completedBatch.StageHistory.First(h => h.Stage == BrewStage.Package).Status.Should().Be(StageStatus.Completed);

        // 4. Attempting to toggle ingredient on completed batch -> 409 Conflict
        var ingId = completedBatch.Ingredients[0].Id;
        var toggleCompletedRes = await client.PatchAsync(
            $"/api/v1/batches/{batchA.Id}/ingredients/{ingId}?isChecked=false", null);
        toggleCompletedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}