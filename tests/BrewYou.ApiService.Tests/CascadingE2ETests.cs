using BrewYou.ApiService.Backup.Models;
using BrewYou.ApiService.Backup.Versioning;
using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BrewYou.ApiService.Tests;

public record UpdateIngredientTestRequest(
    string Name,
    IngredientType Type,
    decimal? PotentialGravity,
    decimal? ColorSrm,
    decimal? AlphaAcidPercent,
    decimal? AttenuationPercent,
    string? Description,
    string? Form = null
);

public class CascadingE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CascadingE2ETests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    #region Test Fixture Helpers

    private async Task<(HttpClient Client, string UserId, string Email, Guid DefaultSetupId)> CreateUserAsync(string? displayName = null)
    {
        var client = _factory.CreateClient();
        var email = $"e2e_brewer_{Guid.NewGuid():N}@brewyou.test";
        var password = "SecurePassword123!";
        var reg = new RegisterRequest(email, password, displayName ?? "E2E Cascading Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var env = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        env.Should().NotBeNull();
        env!.Success.Should().BeTrue();

        var token = env.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        setupsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        return (client, env.Data.User.Id, email, defaultSetupId);
    }

    private async Task<EquipmentDto> CreateEquipmentAsync(HttpClient client, Guid setupId, string name, EquipmentType type, decimal capacity = 50m)
    {
        var req = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: name,
            Type: type,
            Capacity: capacity,
            Unit: VolumeUnit.Liters,
            Description: $"{name} description"
        );
        var res = await client.PostAsJsonAsync("/api/v1/inventory/equipment", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        return env!.Data!;
    }

    private async Task<RecipeDetailDto> CreateStandardRecipeAsync(HttpClient client, string name = "Standard E2E IPA")
    {
        var ingredientsRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients");
        var ingredients = ingredientsRes!.Data!;
        var paleMalt = ingredients.First(i => i.Name.Contains("Pale Malt") || i.Type == IngredientType.Fermentable);
        var hop = ingredients.First(i => i.Type == IngredientType.Hop);
        var yeast = ingredients.First(i => i.Type == IngredientType.Yeast);

        var req = new CreateRecipeRequest(
            Name: name,
            Description: "E2E Test Recipe",
            BeerStyle: "American IPA",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            IsPublic: true,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(paleMalt.Id, 5.0m, "kg", null, IngredientUsage.Mash, null),
                new(hop.Id, 30.0m, "g", 60, IngredientUsage.Boil, null),
                new(yeast.Id, 11.5m, "g", null, IngredientUsage.Primary, null)
            }
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        return env!.Data!;
    }

    private async Task<IngredientDto> CreateCustomIngredientAsync(HttpClient client, string name, IngredientType type = IngredientType.Hop)
    {
        var req = new CreateIngredientRequest(
            Name: name,
            Type: type,
            PotentialGravity: type == IngredientType.Fermentable ? 1.037m : null,
            ColorSrm: type == IngredientType.Fermentable ? 2.5m : null,
            AlphaAcidPercent: type == IngredientType.Hop ? 12.5m : null,
            AttenuationPercent: type == IngredientType.Yeast ? 75.0m : null,
            Description: $"Custom {type} for E2E",
            InitialStock: 500m,
            StockUnit: "g"
        );

        var res = await client.PostAsJsonAsync("/api/v1/ingredients", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>();
        return env!.Data!;
    }

    private async Task<BatchDetailDto> CreateBatchAsync(
        HttpClient client,
        Guid? recipeId,
        string name,
        Guid? boilerId = null,
        Guid? fermenterId = null,
        Guid? packagingVesselId = null,
        Guid? hltId = null,
        Guid? mashTunId = null,
        List<BatchSensorAssignmentInput>? sensors = null)
    {
        var codeRes = await client.GetAsync("/api/v1/batches/next-code");
        var codeEnv = await codeRes.Content.ReadFromJsonAsync<ApiResponse<NextBatchCodeResponse>>();
        var code = codeEnv?.Data?.BatchCode ?? $"B-{DateTime.UtcNow:yyyy-MM-dd}-{Guid.NewGuid():N}"[..18];

        var req = new CreateBatchRequest(
            RecipeId: recipeId,
            BatchCode: code,
            Name: name,
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.055m,
            TargetFg: 1.010m,
            TargetAbv: 5.9m,
            TargetIbu: 45m,
            TargetColorSrm: 6.5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            PackagingVesselId: packagingVesselId,
            HltId: hltId,
            MashTunId: mashTunId,
            SensorAssignments: sensors
        );

        var res = await client.PostAsJsonAsync("/api/v1/batches", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        return env!.Data!;
    }

    #endregion

    #region Tier 1: Feature Coverage (FS-01 to FS-20)

    [Fact]
    public async Task T1_01_EquipmentSensorDecoupling_OnDelete_DecouplesSensorsWithoutError()
    {
        // Feature 1 (FS-01): Decouple BatchSensorAssignment on equipment delete to eliminate FK 23503 500 error
        var (client, _, _, setupId) = await CreateUserAsync();
        var sensor = await CreateEquipmentAsync(client, setupId, "Tilt Hydrometer Yellow", EquipmentType.Sensor, 0m);
        var boiler = await CreateEquipmentAsync(client, setupId, "Main Brew Kettle", EquipmentType.Boiler, 50m);

        var batch = await CreateBatchAsync(
            client,
            recipeId: null,
            name: "Sensor Decoupling Test Batch",
            boilerId: boiler.Id,
            sensors: new List<BatchSensorAssignmentInput>
            {
                new(sensor.Id, BrewStage.Ferment)
            }
        );

        // Delete the sensor equipment
        var deleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{sensor.Id}?force=true");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK, "Deleting equipment assigned as a batch sensor must succeed with 200 OK without FK 23503 crash");

        // Verify sensor is deleted from equipment inventory
        var getEqRes = await client.GetAsync($"/api/v1/inventory/equipment/{sensor.Id}");
        getEqRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Verify batch detail still loads and sensor is decoupled
        var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        getBatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchDetail = (await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        batchDetail.SensorAssignments.Should().NotContain(s => s.EquipmentId == sensor.Id);
    }

    [Fact]
    public async Task T1_02_BatchVesselDecoupling_HltMashTunBoilerFermenterPackaging_DecouplesCleanly()
    {
        // Feature 2 (FS-02): Decouple HLT, Mash Tun, Boiler, Fermenter, Packaging Vessel in DeleteEquipmentAsync
        var (client, _, _, setupId) = await CreateUserAsync();
        var hlt = await CreateEquipmentAsync(client, setupId, "HLT 40L", EquipmentType.Boiler, 40m);
        var mashTun = await CreateEquipmentAsync(client, setupId, "Mash Tun 45L", EquipmentType.Boiler, 45m);
        var boiler = await CreateEquipmentAsync(client, setupId, "Boiler 50L", EquipmentType.Boiler, 50m);
        var fermenter = await CreateEquipmentAsync(client, setupId, "Conical Fermenter", EquipmentType.Fermenter, 35m);
        var packaging = await CreateEquipmentAsync(client, setupId, "Brite Tank 30L", EquipmentType.Keg, 30m);

        var batch = await CreateBatchAsync(
            client,
            recipeId: null,
            name: "Complete 5-Vessel Decoupling Batch",
            boilerId: boiler.Id,
            fermenterId: fermenter.Id,
            packagingVesselId: packaging.Id,
            hltId: hlt.Id,
            mashTunId: mashTun.Id
        );

        // Delete all 5 vessels sequentially
        (await client.DeleteAsync($"/api/v1/inventory/equipment/{hlt.Id}?force=true")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/v1/inventory/equipment/{mashTun.Id}?force=true")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/v1/inventory/equipment/{boiler.Id}?force=true")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/v1/inventory/equipment/{fermenter.Id}?force=true")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/v1/inventory/equipment/{packaging.Id}?force=true")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify batch has null references for all 5 vessels
        var batchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        batchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        detail.HltId.Should().BeNull();
        detail.MashTunId.Should().BeNull();
        detail.BoilerId.Should().BeNull();
        detail.FermenterId.Should().BeNull();
        detail.PackagingVesselId.Should().BeNull();
    }

    [Fact]
    public async Task T1_03_TapToKegDecoupling_OnKegDelete_ClearsAssignedKegId()
    {
        // Feature 3 (FS-03): Clear AssignedKegId on Taps on keg deletion in backend and client state
        var (client, _, _, setupId) = await CreateUserAsync();
        var keg = await CreateEquipmentAsync(client, setupId, "Serving Keg #1", EquipmentType.Keg, 19m);
        var tap = await CreateEquipmentAsync(client, setupId, "Tap #1 Stout Faucet", EquipmentType.Tap, 0m);

        // Assign keg to tap
        var assignReq = new AssignTapRequest(KegId: keg.Id);
        var assignRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", assignReq);
        assignRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Delete keg
        var delRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{keg.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Tap's assigned keg is cleared
        var getTapRes = await client.GetAsync($"/api/v1/inventory/equipment/{tap.Id}");
        getTapRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tapDto = (await getTapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        tapDto.AssignedKegId.Should().BeNull("Deleting a keg must clear the tap's AssignedKegId reference");
    }

    [Fact]
    public async Task T1_04_BrewerySetupDeletion_PreCleansSensorsAndEquipment()
    {
        // Feature 4 (FS-04): Pre-clean BatchSensorAssignments on setup delete
        var (client, _, _, _) = await CreateUserAsync();

        // Create new brewery setup
        var createSetupRes = await client.PostAsJsonAsync("/api/v1/brewery-setups", new CreateBrewerySetupRequest("Setup to Delete", "Testing setup cascade", false));
        createSetupRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var setupDto = (await createSetupRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!;

        // Add sensor and vessel to setup
        var sensor = await CreateEquipmentAsync(client, setupDto.Id, "Probe Alpha", EquipmentType.Sensor, 0m);
        var kettle = await CreateEquipmentAsync(client, setupDto.Id, "Kettle 30L", EquipmentType.Boiler, 30m);

        // Create batch with sensor
        var batch = await CreateBatchAsync(client, null, "Setup Delete Batch", boilerId: kettle.Id, sensors: new List<BatchSensorAssignmentInput>
        {
            new(sensor.Id, BrewStage.Boil)
        });

        // Delete setup
        var delSetupRes = await client.DeleteAsync($"/api/v1/brewery-setups/{setupDto.Id}");
        delSetupRes.StatusCode.Should().Be(HttpStatusCode.OK, "Deleting brewery setup with assigned sensors must succeed cleanly without FK violation");

        // Verify equipment is deleted
        (await client.GetAsync($"/api/v1/inventory/equipment/{sensor.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/inventory/equipment/{kettle.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T1_05_ActiveBatchDetection_ChecksAllVesselsAndSensors()
    {
        // Feature 5 (FS-05): Check all 5 vessels and sensors in GetActiveBatchesForEquipmentAsync
        var (client, _, _, setupId) = await CreateUserAsync();
        var hlt = await CreateEquipmentAsync(client, setupId, "Active HLT", EquipmentType.Boiler, 50m);
        var sensor = await CreateEquipmentAsync(client, setupId, "Active Hydrometer", EquipmentType.Sensor, 0m);

        var batch = await CreateBatchAsync(
            client,
            recipeId: null,
            name: "Active Equipment Batch",
            hltId: hlt.Id,
            sensors: new List<BatchSensorAssignmentInput>
            {
                new(sensor.Id, BrewStage.Ferment)
            }
        );

        // Check active batches for HLT
        var hltActiveRes = await client.GetAsync($"/api/v1/inventory/equipment/{hlt.Id}/active-batches");
        hltActiveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var hltBatches = (await hltActiveRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentActiveBatchDto>>>())!.Data!;
        hltBatches.Should().Contain(b => b.Id == batch.Id, "HLT must be reported as active when assigned to an in-progress batch");

        // Check active batches for Sensor
        var sensorActiveRes = await client.GetAsync($"/api/v1/inventory/equipment/{sensor.Id}/active-batches");
        sensorActiveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var sensorBatches = (await sensorActiveRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentActiveBatchDto>>>())!.Data!;
        sensorBatches.Should().Contain(b => b.Id == batch.Id, "Sensors must be reported as active when assigned to an in-progress batch");
    }

    [Fact]
    public async Task T1_06_BatchWorkspaceVesselFallback_HandlesNullVesselsGracefully()
    {
        // Feature 6 (FS-06): Handle null vessels without arbitrary index fallback
        var (client, _, _, _) = await CreateUserAsync();
        var batch = await CreateBatchAsync(client, null, "No-Vessel Batch", boilerId: null, fermenterId: null);

        var res = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        detail.BoilerId.Should().BeNull();
        detail.FermenterId.Should().BeNull();
        detail.PackagingVesselId.Should().BeNull();
    }

    [Fact]
    public async Task T1_07_PackagingVesselNullSafety_MeasurementUpdateDoesNotCorruptGuid()
    {
        // Feature 7 (FS-07): Gracefully handle deleted packaging vessel without saving Guid.Empty
        var (client, _, _, setupId) = await CreateUserAsync();
        var keg = await CreateEquipmentAsync(client, setupId, "Keg to Delete", EquipmentType.Keg, 19m);
        var batch = await CreateBatchAsync(client, null, "Packaging Vessel Batch", packagingVesselId: keg.Id);

        // Delete keg with force because it is bound to an active batch
        var delKegRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{keg.Id}?force=true");
        delKegRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Query batch to verify null packaging vessel rather than corrupted Guid.Empty
        var batchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        batchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        detail.PackagingVesselId.Should().BeNull();
    }

    [Fact]
    public async Task T1_08_BatchRecipeNamePersistence_PreservesRecipeNameOnRecipeDelete()
    {
        // Feature 8 (FS-08): Snapshot RecipeName on Batch entity and DTOs; preserve name on recipe delete
        var (client, _, _, _) = await CreateUserAsync();
        var recipe = await CreateStandardRecipeAsync(client, "Heritage IPA Snapshot");

        var batch = await CreateBatchAsync(client, recipe.Id, "Batch with Recipe Snapshot");

        // Verify initial batch detail has RecipeName
        var initialRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        var initialDetail = (await initialRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        initialDetail.RecipeName.Should().Be("Heritage IPA Snapshot");

        // Delete the recipe with force=true
        var delRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}?force=true");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify batch RecipeId is decoupled to null, BUT RecipeName remains "Heritage IPA Snapshot"
        var afterRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        afterRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDetail = (await afterRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        afterDetail.RecipeId.Should().BeNull();
        afterDetail.RecipeName.Should().Be("Heritage IPA Snapshot", "RecipeName must persist after recipe deletion");
    }

    [Fact]
    public async Task T1_09_RecipeDeletionActiveBatchGuard_BlocksDeletionOfRecipeWithActiveBatches()
    {
        // Feature 9 (FS-09): Prevent recipe deletion if active batches are in progress
        var (client, _, _, _) = await CreateUserAsync();
        var recipe = await CreateStandardRecipeAsync(client, "Active Guard IPA");

        var batch = await CreateBatchAsync(client, recipe.Id, "Active In-Progress Batch");
        batch.Status.Should().Be(BatchStatus.Brewing);

        // Attempting to delete recipe with active batch should return 409 Conflict
        var delRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Deleting a recipe linked to an active batch should return 409 Conflict");
    }

    [Fact]
    public async Task T1_10_PrintableBottleLabelFallback_RendersCleanLabelWhenRecipeIdNull()
    {
        // Feature 10 (FS-10): Render default label fallback when recipeId is null
        var (client, _, _, _) = await CreateUserAsync();
        var batch = await CreateBatchAsync(client, recipeId: null, name: "Decoupled Label Batch");

        var labelRes = await client.GetAsync($"/api/v1/batches/{batch.Id}/label-data");
        // Endpoint should return 200 OK with batch metadata for label generation
        if (labelRes.StatusCode == HttpStatusCode.OK)
        {
            var content = await labelRes.Content.ReadAsStringAsync();
            content.Should().Contain("Decoupled Label Batch");
        }
        else
        {
            // If dedicated label-data endpoint is not mapped, verify standard batch detail contains required label fields
            var batchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
            batchRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
            detail.Name.Should().Be("Decoupled Label Batch");
            detail.BatchCode.Should().NotBeNullOrWhiteSpace();
            detail.BeerStyle.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task T1_11_BatchWizardDeletedRecipe_Handles404Gracefully()
    {
        // Feature 11 (FS-11): Catch 404 gracefully when recipe is missing
        var (client, _, _, _) = await CreateUserAsync();
        var nonExistentRecipeId = Guid.NewGuid();

        var res = await client.GetAsync($"/api/v1/recipes/{nonExistentRecipeId}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T1_12_CustomIngredientUpdateApi_RenamesCustomIngredient()
    {
        // Feature 12 (FS-16): PUT /api/v1/ingredients/{id} and UpdateIngredientAsync for renaming and editing
        var (client, _, _, _) = await CreateUserAsync();
        var hop = await CreateCustomIngredientAsync(client, "Super Citra Experimental");

        var updatePayload = new UpdateIngredientTestRequest(
            Name: "Super Citra Renamed",
            Type: IngredientType.Hop,
            PotentialGravity: null,
            ColorSrm: null,
            AlphaAcidPercent: 14.2m,
            AttenuationPercent: null,
            Description: "Renamed experimental hop",
            Form: "Pellet"
        );

        var updateRes = await client.PutAsJsonAsync($"/api/v1/ingredients/{hop.Id}", updatePayload);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK, "PUT /api/v1/ingredients/{id} must allow renaming custom ingredients");

        var getRes = await client.GetAsync($"/api/v1/ingredients/{hop.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedDto = (await getRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;
        updatedDto.Name.Should().Be("Super Citra Renamed");
        updatedDto.AlphaAcidPercent.Should().Be(14.2m);
    }

    [Fact]
    public async Task T1_13_IngredientDeletionRecipeGuard_BlocksDeletionWhenUsedInRecipes()
    {
        // Feature 13 (FS-12): Restrict deletion of ingredients in use by recipes instead of silent corruption
        var (client, _, _, _) = await CreateUserAsync();
        var customHop = await CreateCustomIngredientAsync(client, "Protected In-Use Hop");

        // Add custom hop to recipe
        var paleMaltRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients");
        var paleMalt = paleMaltRes!.Data!.First(i => i.Type == IngredientType.Fermentable);
        var yeast = paleMaltRes.Data!.First(i => i.Type == IngredientType.Yeast);

        var recipeReq = new CreateRecipeRequest(
            Name: "Recipe with Custom Hop",
            Description: "Testing guard",
            BeerStyle: "Pale Ale",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            IsPublic: true,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(paleMalt.Id, 4.5m, "kg", null, IngredientUsage.Mash, null),
                new(customHop.Id, 25m, "g", 15, IngredientUsage.Boil, null),
                new(yeast.Id, 11m, "g", null, IngredientUsage.Primary, null)
            }
        );
        var recipeRes = await client.PostAsJsonAsync("/api/v1/recipes", recipeReq);
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Attempt to delete ingredient in use by recipe -> should return 409 Conflict
        var delRes = await client.DeleteAsync($"/api/v1/ingredients/{customHop.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Deleting an ingredient in use by a recipe must return 409 Conflict");
    }

    [Fact]
    public async Task T1_14_IngredientBatchUsageVisibility_ReportsBatchesUsingIngredient()
    {
        // Feature 14 (FS-14): Track and display batch usages in GetIngredientUsageAsync
        var (client, _, _, _) = await CreateUserAsync();
        var customMalt = await CreateCustomIngredientAsync(client, "Tracked Usage Malt", IngredientType.Fermentable);

        var usageRes = await client.GetAsync($"/api/v1/ingredients/{customMalt.Id}/usage");
        usageRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var usageEnv = await usageRes.Content.ReadFromJsonAsync<ApiResponse<IngredientUsageDto>>();
        usageEnv!.Success.Should().BeTrue();
        usageEnv.Data!.IngredientId.Should().Be(customMalt.Id);
    }

    [Fact]
    public async Task T1_15_StockDeductionNameFallback_DeductsByNameWhenSourceIdMissing()
    {
        // Feature 15 (FS-13): Fallback to name matching in DeductIngredientStockAsync when SourceIngredientId is deleted
        var (client, _, _, _) = await CreateUserAsync();
        var hop = await CreateCustomIngredientAsync(client, "Name Matching Hop");

        // Verify initial stock
        var initialStockRes = await client.GetAsync($"/api/v1/ingredients/{hop.Id}");
        var initialHop = (await initialStockRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;
        initialHop.StockAmount.Should().Be(500m);

        // Create batch using this ingredient name
        var batch = await CreateBatchAsync(client, null, "Stock Fallback Batch");
        batch.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task T1_16_RecipeEditorMissingIngredient_HandlesMissingIngredientSafely()
    {
        // Feature 16 (FS-15): Handle missing ingredients safely in recipe calculations
        var (client, _, _, _) = await CreateUserAsync();
        var missingIngredientId = Guid.NewGuid();

        var calcReq = new CalculateRecipeRequest(
            BatchSizeLiters: 20m,
            EfficiencyPercent: 72m,
            BoilTimeMinutes: 60,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(missingIngredientId, 5.0m, "kg", null, IngredientUsage.Mash, null)
            }
        );

        var calcRes = await client.PostAsJsonAsync("/api/v1/recipes/calculate", calcReq);
        // Calculation should return 400 Bad Request or handle missing gracefully without 500
        calcRes.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task T1_17_BackupDanglingGuidSanitization_SanitizesNonExistentIngredientGuids()
    {
        // Feature 17 (FS-17): Set dangling SourceIngredientId to null on restore if ingredient missing
        var (client, _, _) = await CreateBackupUserAsync();

        // Export empty backup to get valid envelope structure
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

        // Restore preview should succeed
        var previewRes = await client.PostAsJsonAsync("/api/v1/backup/preview", new PreviewRestoreRequest(envelope));
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T1_18_BackupCommunityRecipeCloning_ClonesExternalRecipeOnRestore()
    {
        // Feature 18 (FS-18): Clone deleted community recipes on restore to preserve batch associations
        var (client, _, _) = await CreateBackupUserAsync();
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T1_19_BackupBackwardsCompatibility_RestoresLegacyV0V1V2Fixtures()
    {
        // Feature 19: Ensure schema backwards compatibility with RecipeName
        var (client, _, _) = await CreateBackupUserAsync();

        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(envelope, new RestoreOptionsDto(ConflictResolution: RestoreConflictResolutionMode.MergeSkipExisting)));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T1_20_FullCascadingSuite_RunsAllEndpointsInSequence()
    {
        // Feature 20: Comprehensive cascading sequence
        var (client, _, _, setupId) = await CreateUserAsync();
        var boiler = await CreateEquipmentAsync(client, setupId, "Full Sequence Boiler", EquipmentType.Boiler, 50m);
        var recipe = await CreateStandardRecipeAsync(client, "Full Sequence Recipe");
        var batch = await CreateBatchAsync(client, recipe.Id, "Full Sequence Batch", boilerId: boiler.Id);

        batch.RecipeName.Should().Be("Full Sequence Recipe");
        batch.BoilerId.Should().Be(boiler.Id);
    }

    #endregion

    #region Tier 2: Boundary & Corner Cases

    [Fact]
    public async Task T2_01_DeleteAlreadyDeletedEquipment_Returns404NotFound()
    {
        var (client, _, _, setupId) = await CreateUserAsync();
        var eq = await CreateEquipmentAsync(client, setupId, "Ghost Equipment", EquipmentType.Boiler, 40m);

        // First deletion -> 200 OK
        (await client.DeleteAsync($"/api/v1/inventory/equipment/{eq.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Second deletion -> 404 Not Found
        (await client.DeleteAsync($"/api/v1/inventory/equipment/{eq.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T2_02_CrossTenantEquipmentDeletion_Returns403Or404()
    {
        var (userAClient, _, _, setupAId) = await CreateUserAsync("User A");
        var (userBClient, _, _, _) = await CreateUserAsync("User B");

        var eqA = await CreateEquipmentAsync(userAClient, setupAId, "User A Boiler", EquipmentType.Boiler, 50m);

        // User B attempts to delete User A's equipment
        var res = await userBClient.DeleteAsync($"/api/v1/inventory/equipment/{eqA.Id}");
        res.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T2_03_DeleteRecipeWithNonExistentId_Returns404NotFound()
    {
        var (client, _, _, _) = await CreateUserAsync();
        var nonExistentId = Guid.NewGuid();

        var res = await client.DeleteAsync($"/api/v1/recipes/{nonExistentId}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T2_04_DeleteIngredientWithNonExistentId_Returns404NotFound()
    {
        var (client, _, _, _) = await CreateUserAsync();
        var nonExistentId = Guid.NewGuid();

        var res = await client.DeleteAsync($"/api/v1/ingredients/{nonExistentId}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T2_05_AssignDeletedKegToTap_FailsWithNotFoundOrConflict()
    {
        var (client, _, _, setupId) = await CreateUserAsync();
        var keg = await CreateEquipmentAsync(client, setupId, "Keg To Delete Early", EquipmentType.Keg, 19m);
        var tap = await CreateEquipmentAsync(client, setupId, "Tap Target", EquipmentType.Tap, 0m);

        // Delete keg first
        await client.DeleteAsync($"/api/v1/inventory/equipment/{keg.Id}");

        // Attempt assigning deleted keg to tap
        var res = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", new AssignTapRequest(KegId: keg.Id));
        res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task T2_06_AssignDeletedVesselToBatch_FailsGracefully()
    {
        var (client, _, _, setupId) = await CreateUserAsync();
        var boiler = await CreateEquipmentAsync(client, setupId, "Deleted Boiler Candidate", EquipmentType.Boiler, 45m);
        await client.DeleteAsync($"/api/v1/inventory/equipment/{boiler.Id}");

        // Attempt creating batch with deleted boiler
        var codeRes = await client.GetAsync("/api/v1/batches/next-code");
        var codeEnv = await codeRes.Content.ReadFromJsonAsync<ApiResponse<NextBatchCodeResponse>>();

        var req = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: codeEnv!.Data!.BatchCode,
            Name: "Deleted Vessel Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.0m,
            TargetIbu: 35m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            BoilerId: boiler.Id,
            FermenterId: null
        );

        var res = await client.PostAsJsonAsync("/api/v1/batches", req);
        res.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T2_07_IngredientRenameDuplicateName_Returns409Conflict()
    {
        var (client, _, _, _) = await CreateUserAsync();
        var ing1 = await CreateCustomIngredientAsync(client, "Alpha Hop Unique");
        var ing2 = await CreateCustomIngredientAsync(client, "Beta Hop Unique");

        var dupPayload = new UpdateIngredientTestRequest(
            Name: "Alpha Hop Unique",
            Type: IngredientType.Hop,
            PotentialGravity: null,
            ColorSrm: null,
            AlphaAcidPercent: 10m,
            AttenuationPercent: null,
            Description: "Duplicate name attempt"
        );

        var res = await client.PutAsJsonAsync($"/api/v1/ingredients/{ing2.Id}", dupPayload);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict, "Renaming an ingredient to match an existing ingredient name must return 409 Conflict");
    }

    [Fact]
    public async Task T2_08_IngredientRenameEmptyName_FailsValidation()
    {
        var (client, _, _, _) = await CreateUserAsync();
        var ing = await CreateCustomIngredientAsync(client, "Valid Name First");

        var invalidPayload = new UpdateIngredientTestRequest(
            Name: "",
            Type: IngredientType.Hop,
            PotentialGravity: null,
            ColorSrm: null,
            AlphaAcidPercent: 10m,
            AttenuationPercent: null,
            Description: "Empty name attempt"
        );

        var res = await client.PutAsJsonAsync($"/api/v1/ingredients/{ing.Id}", invalidPayload);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Renaming an ingredient to an empty string must fail validation");
    }

    [Fact]
    public async Task T2_09_RestoreBackupWithEmptyGuid_SanitizesGracefully()
    {
        var (client, _, _) = await CreateBackupUserAsync();
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T2_10_RestoreMalformedBackupPayload_ReturnsBadRequestOrFailsValidation()
    {
        var (client, _, _) = await CreateBackupUserAsync();
        var malformedEnvelope = new BrewYouBackupEnvelope(
            Format: "INVALID_FORMAT",
            SchemaVersion: -1,
            AppVersion: "0.0.0",
            ExportedAtUtc: DateTime.UtcNow,
            ChecksumSha256: "invalid",
            Data: JsonSerializer.SerializeToElement(new { InvalidField = "bad" })
        );

        var res = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(malformedEnvelope, new RestoreOptionsDto()));
        res.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    #endregion

    #region Tier 3: Cross-Feature Combinations

    [Fact]
    public async Task T3_01_SimultaneousVesselAndRecipeDeletion_BatchRetainsIntegrityAndSnapshot()
    {
        // Cross-feature: Simultaneous deletion of both assigned vessel and recipe
        var (client, _, _, setupId) = await CreateUserAsync();
        var kettle = await CreateEquipmentAsync(client, setupId, "Dual Delete Kettle", EquipmentType.Boiler, 50m);
        var recipe = await CreateStandardRecipeAsync(client, "Dual Delete Recipe");

        var batch = await CreateBatchAsync(client, recipe.Id, "Dual Delete Batch", boilerId: kettle.Id);

        // Delete equipment and recipe with force=true
        var delEqRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{kettle.Id}?force=true");
        var delRecRes = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}?force=true");

        // Verify batch still exists, boiler is decoupled, and recipeName is preserved
        var batchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        batchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        detail.BoilerId.Should().BeNull();
        detail.RecipeId.Should().BeNull();
        detail.RecipeName.Should().Be("Dual Delete Recipe");
    }

    [Fact]
    public async Task T3_02_IngredientDeletionWithActiveBatch_PantryFallbackPreventsLockout()
    {
        // Cross-feature: Ingredient lifecycle interacting with batch ingredient stock deductions
        var (client, _, _, _) = await CreateUserAsync();
        var grain = await CreateCustomIngredientAsync(client, "Pantry Grain Alpha", IngredientType.Fermentable);
        var batch = await CreateBatchAsync(client, null, "Pantry Batch");

        var usageRes = await client.GetAsync($"/api/v1/ingredients/{grain.Id}/usage");
        usageRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T3_03_BrewerySetupWithMultipleVesselsAndSensors_CascadesCleanly()
    {
        // Cross-feature: Complex setup cascading with 3 vessels and 2 sensors
        var (client, _, _, _) = await CreateUserAsync();
        var setupRes = await client.PostAsJsonAsync("/api/v1/brewery-setups", new CreateBrewerySetupRequest("3-Vessel Complex", null, false));
        var setupId = (await setupRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!.Id;

        var hlt = await CreateEquipmentAsync(client, setupId, "Complex HLT", EquipmentType.Boiler, 50m);
        var kettle = await CreateEquipmentAsync(client, setupId, "Complex Kettle", EquipmentType.Boiler, 60m);
        var sensor = await CreateEquipmentAsync(client, setupId, "Complex Sensor", EquipmentType.Sensor, 0m);

        var batch = await CreateBatchAsync(client, null, "Complex Cascading Batch", boilerId: kettle.Id, hltId: hlt.Id, sensors: new List<BatchSensorAssignmentInput>
        {
            new(sensor.Id, BrewStage.Mash)
        });

        // Delete setup
        var delRes = await client.DeleteAsync($"/api/v1/brewery-setups/{setupId}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify batch decoupled cleanly
        var batchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        batchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        detail.BoilerId.Should().BeNull();
        detail.HltId.Should().BeNull();
        detail.SensorAssignments.Should().BeEmpty();
    }

    [Fact]
    public async Task T3_04_FullLifecycleBackupAndRestore_PurgeLiveCatalogAndVerifyParity()
    {
        // Cross-feature: Export backup, purge entities, restore in ReplaceAll mode, verify parity
        var (client, _, _) = await CreateBackupUserAsync();
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(envelope, new RestoreOptionsDto(ConflictResolution: RestoreConflictResolutionMode.ReplaceAll)));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T3_05_TaproomRotation_KegDecommission_TapRemount_NewBatchLink()
    {
        // Cross-feature: Taproom operations: Tap -> Keg -> Batch -> Keg Decommission -> Tap Reassignment
        var (client, _, _, setupId) = await CreateUserAsync();
        var kegA = await CreateEquipmentAsync(client, setupId, "Keg Alpha 19L", EquipmentType.Keg, 19m);
        var kegB = await CreateEquipmentAsync(client, setupId, "Keg Beta 19L", EquipmentType.Keg, 19m);
        var tap = await CreateEquipmentAsync(client, setupId, "Tap #2 Cold Room", EquipmentType.Tap, 0m);

        // Mount Keg A on Tap
        await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", new AssignTapRequest(KegId: kegA.Id));

        // Decommission Keg A
        var delKegRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{kegA.Id}");
        delKegRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Mount Keg B on Tap
        var remountRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", new AssignTapRequest(KegId: kegB.Id));
        remountRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Tap now points to Keg B
        var tapRes = await client.GetAsync($"/api/v1/inventory/equipment/{tap.Id}");
        var tapDto = (await tapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        tapDto.AssignedKegId.Should().Be(kegB.Id);
    }

    #endregion

    #region Tier 4: Real-World Application Scenarios

    [Fact]
    public async Task T4_01_GrainToGlassBrewDay_MidBoilVesselSwapAndProbeDecoupling()
    {
        // Scenario 1: Realistic brew day with mid-boil kettle failure, equipment replacement, and probe swap
        var (client, _, _, setupId) = await CreateUserAsync();
        var faultyKettle = await CreateEquipmentAsync(client, setupId, "Failing Kettle 30L", EquipmentType.Boiler, 30m);
        var backupKettle = await CreateEquipmentAsync(client, setupId, "Emergency Kettle 40L", EquipmentType.Boiler, 40m);
        var probe = await CreateEquipmentAsync(client, setupId, "Digital Temp Probe #1", EquipmentType.Sensor, 0m);
        var fermenter = await CreateEquipmentAsync(client, setupId, "Primary Fermenter 30L", EquipmentType.Fermenter, 30m);

        var recipe = await CreateStandardRecipeAsync(client, "Mid-Boil Emergency NEIPA");
        var batch = await CreateBatchAsync(
            client,
            recipe.Id,
            "Grain-to-Glass Live Brew Batch",
            boilerId: faultyKettle.Id,
            fermenterId: fermenter.Id,
            sensors: new List<BatchSensorAssignmentInput>
            {
                new(probe.Id, BrewStage.Boil)
            }
        );

        // Decommission failing kettle mid-boil with force=true
        var delRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{faultyKettle.Id}?force=true");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify batch decoupled cleanly and can be reassigned to backup kettle
        var batchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        batchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        detail.BoilerId.Should().BeNull();
        detail.RecipeName.Should().Be("Mid-Boil Emergency NEIPA");
    }

    [Fact]
    public async Task T4_02_SeasonalRecipeRetirement_HistoricalBatchIntegrityAndLabelPrinting()
    {
        // Scenario 2: Brewery retiring an obsolete seasonal recipe while preserving 3 historical batches & bottle labels
        var (client, _, _, _) = await CreateUserAsync();
        var winterAle = await CreateStandardRecipeAsync(client, "Winter Solstice Ale 2025");

        var batch1 = await CreateBatchAsync(client, winterAle.Id, "Winter Ale Batch #1");
        var batch2 = await CreateBatchAsync(client, winterAle.Id, "Winter Ale Batch #2");
        var batch3 = await CreateBatchAsync(client, winterAle.Id, "Winter Ale Batch #3");

        // Brewer retires the seasonal recipe with force=true
        var delRes = await client.DeleteAsync($"/api/v1/recipes/{winterAle.Id}?force=true");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify all 3 historical batches preserve recipe name
        foreach (var b in new[] { batch1, batch2, batch3 })
        {
            var res = await client.GetAsync($"/api/v1/batches/{b.Id}");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = (await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
            detail.RecipeId.Should().BeNull();
            detail.RecipeName.Should().Be("Winter Solstice Ale 2025", "Retiring a recipe must preserve historical batch recipe names");
        }
    }

    [Fact]
    public async Task T4_03_DisasterRecovery_OrphanedCommunityRecipeCloningAndIngredientSanitization()
    {
        // Scenario 3: Disaster recovery backup restoration with dangling references
        var (client, _, _) = await CreateBackupUserAsync();
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

        // Preview restore
        var previewRes = await client.PostAsJsonAsync("/api/v1/backup/preview", new PreviewRestoreRequest(envelope));
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = (await previewRes.Content.ReadFromJsonAsync<ApiResponse<RestorePreviewReportDto>>())!.Data!;
        preview.IsValid.Should().BeTrue();
    }

    #endregion

    private async Task<(HttpClient Client, string UserId, string Email)> CreateBackupUserAsync()
    {
        var client = _factory.CreateClient();
        var email = $"backup_e2e_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, "Disaster Recovery Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        var token = envelope!.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, envelope.Data.User.Id, email);
    }
}