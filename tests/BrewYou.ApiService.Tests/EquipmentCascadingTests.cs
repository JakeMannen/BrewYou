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
using Xunit;

namespace BrewYou.ApiService.Tests;

public class EquipmentCascadingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EquipmentCascadingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    #region Helper Methods

    private async Task<(HttpClient Client, string UserId, string Email, Guid DefaultSetupId)> CreateAuthenticatedUserAsync(
        string prefix = "cascadetester")
    {
        var client = _factory.CreateClient();
        var email = $"{prefix}_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, $"{prefix} Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var token = envelope.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        setupsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        return (client, envelope.Data.User.Id, email, defaultSetupId);
    }

    private async Task<EquipmentDto> CreateEquipmentAsync(
        HttpClient client,
        Guid setupId,
        string name,
        EquipmentType type,
        EquipmentSubtype subtype = EquipmentSubtype.Other,
        decimal capacity = 0m)
    {
        var req = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: name,
            Type: type,
            Subtype: subtype,
            Capacity: capacity,
            Unit: VolumeUnit.Liters
        );

        var res = await client.PostAsJsonAsync("/api/v1/inventory/equipment", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        return env!.Data!;
    }

    private async Task<RecipeDetailDto> CreateSampleRecipeAsync(HttpClient client, string name = "Cascade Recipe")
    {
        var ingRes = await client.GetAsync("/api/v1/ingredients");
        var ingredients = (await ingRes.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);

        var req = new CreateRecipeRequest(
            Name: name,
            Description: "Recipe for cascading test.",
            BeerStyle: "IPA",
            BatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            IsPublic: false,
            Ingredients: [new RecipeIngredientInputDto(grain.Id, 5.0m, "kg", 60, IngredientUsage.Mash, "Base")]
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
    }

    private async Task<BatchDetailDto> CreateSampleBatchAsync(
        HttpClient client,
        Guid recipeId,
        Guid? boilerId = null,
        Guid? fermenterId = null,
        Guid? hltId = null,
        Guid? mashTunId = null,
        Guid? packagingVesselId = null,
        BatchStatus status = BatchStatus.Brewing,
        BrewStage stage = BrewStage.Mash)
    {
        var req = new CreateBatchRequest(
            RecipeId: recipeId,
            BatchCode: $"B-{Guid.NewGuid():N}"[..12],
            Name: "Cascading Test Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.2m,
            TargetIbu: 35.0m,
            TargetColorSrm: 6.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            HltId: hltId,
            MashTunId: mashTunId,
            PackagingVesselId: packagingVesselId
        );

        var res = await client.PostAsJsonAsync("/api/v1/batches", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // Adjust status/stage if non-default requested
        if (status != BatchStatus.Brewing || stage != BrewStage.Mash)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var trackedBatch = await db.Batches.FindAsync(batch.Id);
            trackedBatch!.Status = status;
            trackedBatch.CurrentStage = stage;
            await db.SaveChangesAsync();
        }

        return batch;
    }

    #endregion

    #region Automated Test Cases

    [Fact]
    public async Task DeleteSensorEquipment_WithBatchSensorAssignment_DecouplesCleanlyWithout500Error()
    {
        // 1. Arrange: Authenticated user and default setup
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync("sensor_cascade");

        // 2. Create sensor equipment
        var sensor = await CreateEquipmentAsync(
            client, setupId, "Tilt Hydrometer Yellow", EquipmentType.Sensor, EquipmentSubtype.Tilt, 0m);

        // 3. Create a recipe and completed batch
        var recipe = await CreateSampleRecipeAsync(client, "Sensor Decoupling Test Ale");
        var batch = await CreateSampleBatchAsync(
            client, recipe.Id, status: BatchStatus.Completed, stage: BrewStage.Package);

        // 4. Assign sensor to the batch
        var sensorInput = new List<BatchSensorAssignmentInput>
        {
            new(sensor.Id, BrewStage.Ferment)
        };
        var assignRes = await client.PutAsJsonAsync($"/api/v1/batches/{batch.Id}/sensors", sensorInput);
        assignRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify assignment exists in database prior to delete
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var count = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == sensor.Id);
            count.Should().Be(1, "sensor must be actively assigned in database");
        }

        // 5. Act: Delete the sensor equipment
        var deleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{sensor.Id}");

        // 6. Assert HTTP Status Code
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK, "deleting sensor must not trigger FK 23503 500 error");
        var envelope = await deleteRes.Content.ReadFromJsonAsync<ApiResponse>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        // 7. Assert Database State
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var eqExists = await db.Equipment.AnyAsync(e => e.Id == sensor.Id);
            eqExists.Should().BeFalse("equipment entity must be purged from database");

            var assignmentCount = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == sensor.Id);
            assignmentCount.Should().Be(0, "all referencing BatchSensorAssignments must be decoupled/deleted");
        }

        // 8. Assert API Batch View
        var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        getBatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchEnv = await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        batchEnv!.Data!.SensorAssignments.Should().NotContain(sa => sa.EquipmentId == sensor.Id);
    }

    [Fact]
    public async Task DeleteEquipment_UsedAsHltOrMashTun_DecouplesBothIds()
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync("vessel_cascade");

        // 2. Create multipurpose hot vessel
        var vessel = await CreateEquipmentAsync(
            client, setupId, "Multipurpose Kettle", EquipmentType.Boiler, EquipmentSubtype.AllInOne, 50m);

        // 3. Create recipe and two completed batches using HLT and MashTun respectively
        var recipe = await CreateSampleRecipeAsync(client, "HLT MashTun Recipe");
        var batch1 = await CreateSampleBatchAsync(
            client, recipe.Id, hltId: vessel.Id, status: BatchStatus.Completed, stage: BrewStage.Package);
        var batch2 = await CreateSampleBatchAsync(
            client, recipe.Id, mashTunId: vessel.Id, status: BatchStatus.Completed, stage: BrewStage.Package);

        // Verify initial DB state
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var b1 = await db.Batches.FindAsync(batch1.Id);
            var b2 = await db.Batches.FindAsync(batch2.Id);
            b1!.HltId.Should().Be(vessel.Id);
            b2!.MashTunId.Should().Be(vessel.Id);
        }

        // 4. Act: Delete the multipurpose vessel
        var deleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{vessel.Id}");

        // 5. Assert HTTP Status
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Assert DB State: HltId and MashTunId must both be null
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var eqExists = await db.Equipment.AnyAsync(e => e.Id == vessel.Id);
            eqExists.Should().BeFalse();

            var b1 = await db.Batches.FindAsync(batch1.Id);
            var b2 = await db.Batches.FindAsync(batch2.Id);
            b1!.HltId.Should().BeNull("HltId reference must be decoupled to null on equipment delete");
            b2!.MashTunId.Should().BeNull("MashTunId reference must be decoupled to null on equipment delete");
        }

        // 7. Assert API Batch Details
        var res1 = await client.GetAsync($"/api/v1/batches/{batch1.Id}");
        var env1 = await res1.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        env1!.Data!.HltId.Should().BeNull();

        var res2 = await client.GetAsync($"/api/v1/batches/{batch2.Id}");
        var env2 = await res2.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        env2!.Data!.MashTunId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteKeg_AssignedToTap_DecouplesTapAssignedKegId()
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync("tap_keg_cascade");

        // 2. Create Keg and Tap
        var keg = await CreateEquipmentAsync(
            client, setupId, "Cornelius Keg 19L", EquipmentType.Keg, EquipmentSubtype.Cornelius, 19m);
        var tap = await CreateEquipmentAsync(
            client, setupId, "Draft Tower Tap 1", EquipmentType.Tap, EquipmentSubtype.StandardTap, 0m);

        // 3. Assign Keg to Tap
        var assignReq = new AssignTapRequest(KegId: keg.Id);
        var assignRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap.Id}/tap-assignment", assignReq);
        assignRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tapDto = (await assignRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        tapDto.AssignedKegId.Should().Be(keg.Id);

        // Verify DB link
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var tapFromDb = await db.Equipment.FindAsync(tap.Id);
            tapFromDb!.AssignedKegId.Should().Be(keg.Id);
        }

        // 4. Act: Delete the assigned Keg
        var deleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{keg.Id}");

        // 5. Assert HTTP Status
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Assert DB State: Keg removed and Tap.AssignedKegId cleared to null
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var kegExists = await db.Equipment.AnyAsync(e => e.Id == keg.Id);
            kegExists.Should().BeFalse();

            var tapFromDb = await db.Equipment.FindAsync(tap.Id);
            tapFromDb!.AssignedKegId.Should().BeNull("Tap.AssignedKegId must be cleared to null when keg is deleted");
        }

        // 7. Assert API Tap View
        var getTapRes = await client.GetAsync($"/api/v1/inventory/equipment/{tap.Id}");
        getTapRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tapEnv = await getTapRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        tapEnv!.Data!.AssignedKegId.Should().BeNull();
        tapEnv.Data.AssignedKeg.Should().BeNull();
    }

    [Fact]
    public async Task DeleteEquipment_WithActiveBatch_ReturnsConflictUnlessForced()
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync("active_guard");

        // 2. Create Fermenter
        var fermenter = await CreateEquipmentAsync(
            client, setupId, "Conical 60L", EquipmentType.Fermenter, EquipmentSubtype.ConicalFermenter, 60m);

        // 3. Create recipe and ACTIVE batch in Fermenting status
        var recipe = await CreateSampleRecipeAsync(client, "Active Fermenting IPA");
        var batch = await CreateSampleBatchAsync(
            client, recipe.Id, fermenterId: fermenter.Id, status: BatchStatus.Fermenting, stage: BrewStage.Ferment);

        // 4. Act Part A: Attempt delete without force
        var unforcedDeleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{fermenter.Id}");

        // 5. Assert Conflict: HTTP 409 Conflict with EQUIPMENT_IN_USE code
        unforcedDeleteRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errEnv = await unforcedDeleteRes.Content.ReadFromJsonAsync<ApiResponse>();
        errEnv!.Success.Should().BeFalse();
        errEnv.Error!.Code.Should().Be("EQUIPMENT_IN_USE");

        // Verify equipment was NOT deleted from DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var eqExists = await db.Equipment.AnyAsync(e => e.Id == fermenter.Id);
            eqExists.Should().BeTrue("equipment must not be deleted when active batches exist without force");

            var b = await db.Batches.FindAsync(batch.Id);
            b!.FermenterId.Should().Be(fermenter.Id);
        }

        // 6. Act Part B: Force delete with ?force=true
        var forcedDeleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{fermenter.Id}?force=true");

        // 7. Assert Forced Delete Success: HTTP 200 OK
        forcedDeleteRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var successEnv = await forcedDeleteRes.Content.ReadFromJsonAsync<ApiResponse>();
        successEnv!.Success.Should().BeTrue();

        // 8. Assert DB State: Equipment deleted and active batch FermenterId decoupled to null
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var eqExists = await db.Equipment.AnyAsync(e => e.Id == fermenter.Id);
            eqExists.Should().BeFalse();

            var b = await db.Batches.FindAsync(batch.Id);
            b!.FermenterId.Should().BeNull("active batch must be decoupled cleanly upon forced equipment delete");
        }

        // 9. Assert API Batch View
        var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        var batchEnv = await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        batchEnv!.Data!.FermenterId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteBrewerySetup_WithAssignedSensors_CleansUpAndDeletesWithout500Error()
    {
        // 1. Arrange: User and initial default setup
        var (client, _, _, _) = await CreateAuthenticatedUserAsync("setup_sensor_cascade");

        // 2. Create second brewery setup
        var createSetupRes = await client.PostAsJsonAsync("/api/v1/brewery-setups",
            new CreateBrewerySetupRequest("Garage Nano Setup"));
        createSetupRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondSetup = (await createSetupRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!;

        // 3. Add sensor to the second setup
        var sensor = await CreateEquipmentAsync(
            client, secondSetup.Id, "Tilt Hydrometer Green", EquipmentType.Sensor, EquipmentSubtype.Tilt, 0m);

        // 4. Create recipe and batch, assign the sensor
        var recipe = await CreateSampleRecipeAsync(client, "Garage Setup Recipe");
        var batch = await CreateSampleBatchAsync(
            client, recipe.Id, status: BatchStatus.Completed, stage: BrewStage.Package);

        var sensorInput = new List<BatchSensorAssignmentInput>
        {
            new(sensor.Id, BrewStage.Ferment)
        };
        var assignRes = await client.PutAsJsonAsync($"/api/v1/batches/{batch.Id}/sensors", sensorInput);
        assignRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify assignment exists in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var saCount = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == sensor.Id);
            saCount.Should().Be(1);
        }

        // 5. Act: Delete the second brewery setup
        var delSetupRes = await client.DeleteAsync($"/api/v1/brewery-setups/{secondSetup.Id}");

        // 6. Assert HTTP Status: Must be 200 OK (not 500 DbUpdateException)
        delSetupRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. Assert DB State: Setup, equipment, and sensor assignments are all purged
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var setupExists = await db.BrewerySetups.AnyAsync(s => s.Id == secondSetup.Id);
            setupExists.Should().BeFalse();

            var sensorExists = await db.Equipment.AnyAsync(e => e.Id == sensor.Id);
            sensorExists.Should().BeFalse();

            var saCount = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == sensor.Id);
            saCount.Should().Be(0, "assignments must be deleted before setup equipment cascade to prevent FK restrict violation");
        }

        // 8. Assert Batch remains intact and sensor assignment is gone
        var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        getBatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchEnv = await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        batchEnv!.Data!.SensorAssignments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(BatchStatus.Planned)]
    [InlineData(BatchStatus.Brewing)]
    [InlineData(BatchStatus.Fermenting)]
    [InlineData(BatchStatus.Conditioning)]
    public async Task DeleteEquipment_ActiveBatchStatuses_AllReturnConflictWithoutForce(BatchStatus activeStatus)
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync($"active_status_{activeStatus}".ToLowerInvariant());

        // 2. Create Boiler equipment
        var kettle = await CreateEquipmentAsync(
            client, setupId, $"Kettle for {activeStatus}", EquipmentType.Boiler, EquipmentSubtype.AllInOne, 30m);

        // 3. Create recipe and batch with active status
        var recipe = await CreateSampleRecipeAsync(client, $"Recipe for {activeStatus}");
        var stage = activeStatus switch
        {
            BatchStatus.Planned => BrewStage.Mash,
            BatchStatus.Brewing => BrewStage.Boil,
            BatchStatus.Fermenting => BrewStage.Ferment,
            BatchStatus.Conditioning => BrewStage.Condition,
            _ => BrewStage.Mash
        };

        var batch = await CreateSampleBatchAsync(
            client, recipe.Id, boilerId: kettle.Id, status: activeStatus, stage: stage);

        // 4. Act: Attempt delete without force
        var deleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{kettle.Id}");

        // 5. Assert: 409 Conflict with EQUIPMENT_IN_USE
        deleteRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errEnv = await deleteRes.Content.ReadFromJsonAsync<ApiResponse>();
        errEnv!.Success.Should().BeFalse();
        errEnv.Error!.Code.Should().Be("EQUIPMENT_IN_USE");

        // Verify equipment was not deleted and batch link remains
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var exists = await db.Equipment.AnyAsync(e => e.Id == kettle.Id);
            exists.Should().BeTrue();

            var b = await db.Batches.FindAsync(batch.Id);
            b!.BoilerId.Should().Be(kettle.Id);
        }
    }

    [Theory]
    [InlineData(BatchStatus.Completed)]
    [InlineData(BatchStatus.Archived)]
    public async Task DeleteEquipment_InactiveBatchStatuses_DoNotBlockDeletion(BatchStatus inactiveStatus)
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync($"inactive_status_{inactiveStatus}".ToLowerInvariant());

        // 2. Create Fermenter equipment
        var fermenter = await CreateEquipmentAsync(
            client, setupId, $"Fermenter for {inactiveStatus}", EquipmentType.Fermenter, EquipmentSubtype.ConicalFermenter, 30m);

        // 3. Create recipe and batch with inactive status
        var recipe = await CreateSampleRecipeAsync(client, $"Recipe for {inactiveStatus}");
        var batch = await CreateSampleBatchAsync(
            client, recipe.Id, fermenterId: fermenter.Id, status: inactiveStatus, stage: BrewStage.Package);

        // 4. Act: Attempt delete without force
        var deleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{fermenter.Id}");

        // 5. Assert: HTTP 200 OK (inactive batch does NOT block equipment delete)
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await deleteRes.Content.ReadFromJsonAsync<ApiResponse>();
        envelope!.Success.Should().BeTrue();

        // 6. Assert DB State: Equipment purged and batch FermenterId decoupled to null
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var exists = await db.Equipment.AnyAsync(e => e.Id == fermenter.Id);
            exists.Should().BeFalse();

            var b = await db.Batches.FindAsync(batch.Id);
            b!.FermenterId.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(BatchStatus.Planned)]
    [InlineData(BatchStatus.Brewing)]
    [InlineData(BatchStatus.Fermenting)]
    [InlineData(BatchStatus.Conditioning)]
    public async Task DeleteEquipment_ForceTrue_SucceedsAcrossAllActiveBatchStatuses(BatchStatus activeStatus)
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync($"force_{activeStatus}".ToLowerInvariant());

        // 2. Create PackagingVessel equipment
        var keg = await CreateEquipmentAsync(
            client, setupId, $"Keg for {activeStatus}", EquipmentType.Keg, EquipmentSubtype.Cornelius, 19m);

        // 3. Create recipe and batch with active status
        var recipe = await CreateSampleRecipeAsync(client, $"Recipe for force {activeStatus}");
        var stage = activeStatus switch
        {
            BatchStatus.Planned => BrewStage.Mash,
            BatchStatus.Brewing => BrewStage.Boil,
            BatchStatus.Fermenting => BrewStage.Ferment,
            BatchStatus.Conditioning => BrewStage.Condition,
            _ => BrewStage.Mash
        };

        var batch = await CreateSampleBatchAsync(
            client, recipe.Id, packagingVesselId: keg.Id, status: activeStatus, stage: stage);

        // 4. Act: Force delete
        var deleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{keg.Id}?force=true");

        // 5. Assert: HTTP 200 OK
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await deleteRes.Content.ReadFromJsonAsync<ApiResponse>();
        envelope!.Success.Should().BeTrue();

        // 6. Assert DB State: Keg purged and batch PackagingVesselId decoupled to null
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var exists = await db.Equipment.AnyAsync(e => e.Id == keg.Id);
            exists.Should().BeFalse();

            var b = await db.Batches.FindAsync(batch.Id);
            b!.PackagingVesselId.Should().BeNull();
        }
    }

    [Fact]
    public async Task DeleteEquipment_WithActiveBatchSensorAssignment_ReturnsConflictWithoutForce_AndDecouplesWithForce()
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync("sensor_active_guard");

        // 2. Create Sensor
        var sensor = await CreateEquipmentAsync(
            client, setupId, "Active iSpindel Red", EquipmentType.Sensor, EquipmentSubtype.ISpindel, 0m);

        // 3. Create active batch (Fermenting)
        var recipe = await CreateSampleRecipeAsync(client, "Active Sensor Test Recipe");
        var batch = await CreateSampleBatchAsync(
            client, recipe.Id, status: BatchStatus.Fermenting, stage: BrewStage.Ferment);

        // Assign sensor
        var sensorInput = new List<BatchSensorAssignmentInput>
        {
            new(sensor.Id, BrewStage.Ferment)
        };
        var assignRes = await client.PutAsJsonAsync($"/api/v1/batches/{batch.Id}/sensors", sensorInput);
        assignRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Act Part A: Attempt delete without force
        var unforcedDeleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{sensor.Id}");

        // 5. Assert Conflict: HTTP 409 Conflict with EQUIPMENT_IN_USE code
        unforcedDeleteRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errEnv = await unforcedDeleteRes.Content.ReadFromJsonAsync<ApiResponse>();
        errEnv!.Success.Should().BeFalse();
        errEnv.Error!.Code.Should().Be("EQUIPMENT_IN_USE");

        // 6. Act Part B: Force delete
        var forcedDeleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{sensor.Id}?force=true");

        // 7. Assert Success: HTTP 200 OK
        forcedDeleteRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. Assert DB State: Sensor purged and BatchSensorAssignment removed
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var exists = await db.Equipment.AnyAsync(e => e.Id == sensor.Id);
            exists.Should().BeFalse();

            var saCount = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == sensor.Id);
            saCount.Should().Be(0);
        }

        // 9. Assert API view
        var getBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        var batchEnv = await getBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        batchEnv!.Data!.SensorAssignments.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteBrewerySetup_WithMultipleEquipment_ActiveAndHistoricalSensorAssignments_DeletesCleanly()
    {
        // 1. Arrange: User and initial default setup
        var (client, _, _, _) = await CreateAuthenticatedUserAsync("multi_eq_setup_cascade");

        // 2. Create second brewery setup
        var createSetupRes = await client.PostAsJsonAsync("/api/v1/brewery-setups",
            new CreateBrewerySetupRequest("Pilot Commercial Setup"));
        createSetupRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondSetup = (await createSetupRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!;

        // 3. Create multiple equipment in the second setup
        var sensor1 = await CreateEquipmentAsync(
            client, secondSetup.Id, "Tilt Hydrometer Red", EquipmentType.Sensor, EquipmentSubtype.Tilt, 0m);
        var sensor2 = await CreateEquipmentAsync(
            client, secondSetup.Id, "Tilt Hydrometer Blue", EquipmentType.Sensor, EquipmentSubtype.Tilt, 0m);
        var boiler = await CreateEquipmentAsync(
            client, secondSetup.Id, "Electric Kettle 100L", EquipmentType.Boiler, EquipmentSubtype.AllInOne, 100m);
        var fermenter = await CreateEquipmentAsync(
            client, secondSetup.Id, "Unitank 100L", EquipmentType.Fermenter, EquipmentSubtype.ConicalFermenter, 100m);

        var recipe = await CreateSampleRecipeAsync(client, "Pilot Setup Ale");

        // 4. Batch 1: Active (Fermenting) with Boiler, Fermenter, and Sensor 1
        var batch1 = await CreateSampleBatchAsync(
            client, recipe.Id, boilerId: boiler.Id, fermenterId: fermenter.Id,
            status: BatchStatus.Fermenting, stage: BrewStage.Ferment);
        var assignRes1 = await client.PutAsJsonAsync($"/api/v1/batches/{batch1.Id}/sensors",
            new List<BatchSensorAssignmentInput> { new(sensor1.Id, BrewStage.Ferment) });
        assignRes1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Batch 2: Completed (Historical) with Sensor 2
        var batch2 = await CreateSampleBatchAsync(
            client, recipe.Id, status: BatchStatus.Completed, stage: BrewStage.Package);
        var assignRes2 = await client.PutAsJsonAsync($"/api/v1/batches/{batch2.Id}/sensors",
            new List<BatchSensorAssignmentInput> { new(sensor2.Id, BrewStage.Ferment) });
        assignRes2.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Batch 3: Historical batch with both Sensor 1 and Sensor 2, subsequently Archived
        var batch3 = await CreateSampleBatchAsync(
            client, recipe.Id, status: BatchStatus.Brewing, stage: BrewStage.Mash);
        var assignRes3 = await client.PutAsJsonAsync($"/api/v1/batches/{batch3.Id}/sensors",
            new List<BatchSensorAssignmentInput>
            {
                new(sensor1.Id, BrewStage.Mash),
                new(sensor2.Id, BrewStage.Ferment)
            });
        var body3 = await assignRes3.Content.ReadAsStringAsync();
        assignRes3.StatusCode.Should().Be(HttpStatusCode.OK, because: $"Body: {body3}");

        // Archive Batch 3 to simulate historical archived batch with sensor assignments
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var trackedBatch3 = await db.Batches.FindAsync(batch3.Id);
            trackedBatch3!.Status = BatchStatus.Archived;
            trackedBatch3.CurrentStage = BrewStage.Package;
            await db.SaveChangesAsync();
        }

        // Verify total assignments in DB prior to deletion
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var s1Assignments = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == sensor1.Id);
            var s2Assignments = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == sensor2.Id);
            (s1Assignments + s2Assignments).Should().Be(4, "four total sensor assignments across active, completed, and archived batches");
        }

        // 7. Act: Delete the second brewery setup
        var delSetupRes = await client.DeleteAsync($"/api/v1/brewery-setups/{secondSetup.Id}");

        // 8. Assert HTTP Status: Must be 200 OK (no 500 error or FK constraint failure)
        delSetupRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 9. Assert DB State: Setup and all its equipment purged, all sensor assignments decoupled
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

            var setupExists = await db.BrewerySetups.AnyAsync(s => s.Id == secondSetup.Id);
            setupExists.Should().BeFalse();

            var anyEqExists = await db.Equipment.AnyAsync(e =>
                e.Id == sensor1.Id || e.Id == sensor2.Id || e.Id == boiler.Id || e.Id == fermenter.Id);
            anyEqExists.Should().BeFalse("all equipment in the deleted setup must be removed");

            var remainingAssignments = await db.BatchSensorAssignments.CountAsync(sa =>
                sa.EquipmentId == sensor1.Id || sa.EquipmentId == sensor2.Id);
            remainingAssignments.Should().Be(0, "all sensor assignments for deleted setup equipment must be removed");

            var b1 = await db.Batches.FindAsync(batch1.Id);
            b1!.BoilerId.Should().BeNull();
            b1.FermenterId.Should().BeNull();

            var b2 = await db.Batches.FindAsync(batch2.Id);
            b2.Should().NotBeNull();

            var b3 = await db.Batches.FindAsync(batch3.Id);
            b3.Should().NotBeNull();
        }

        // 10. Assert API Batch View: batches exist with empty sensor assignments
        var b1Res = await client.GetAsync($"/api/v1/batches/{batch1.Id}");
        var b1Dto = (await b1Res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        b1Dto.BoilerId.Should().BeNull();
        b1Dto.FermenterId.Should().BeNull();
        b1Dto.SensorAssignments.Should().BeEmpty();

        var b2Res = await client.GetAsync($"/api/v1/batches/{batch2.Id}");
        var b2Dto = (await b2Res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        b2Dto.SensorAssignments.Should().BeEmpty();

        var b3Res = await client.GetAsync($"/api/v1/batches/{batch3.Id}");
        var b3Dto = (await b3Res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        b3Dto.SensorAssignments.Should().BeEmpty();
    }

    #endregion
}