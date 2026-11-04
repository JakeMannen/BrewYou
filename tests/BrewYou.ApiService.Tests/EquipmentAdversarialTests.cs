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

/// <summary>
/// Empirical Challenger adversarial and stress test suite for Milestone 1:
/// Equipment Cascading Integrity & Sensor Decoupling.
/// Stress-tests concurrent deletions, multi-vessel assignments, multi-batch multi-stage sensor links,
/// active batch guard robustness, and full-topology cascading without orphan records.
/// </summary>
public class EquipmentAdversarialTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EquipmentAdversarialTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    #region Fixture Helpers

    private async Task<(HttpClient Client, string UserId, string Email, Guid DefaultSetupId)> CreateUserAsync(string prefix = "adv_brewer")
    {
        var client = _factory.CreateClient();
        var email = $"{prefix}_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, $"{prefix} Challenger", "en", VolumeUnit.Liters);

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

    private async Task<RecipeDetailDto> CreateSampleRecipeAsync(HttpClient client, string name = "Adversarial Recipe")
    {
        var ingRes = await client.GetAsync("/api/v1/ingredients");
        var ingredients = (await ingRes.Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        var grain = ingredients.First(i => i.Type == IngredientType.Fermentable);

        var req = new CreateRecipeRequest(
            Name: name,
            Description: "Recipe for stress testing.",
            BeerStyle: "Imperial Stout",
            BatchSizeLiters: 50.0m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 80.0m,
            IsPublic: false,
            Ingredients: [new RecipeIngredientInputDto(grain.Id, 12.0m, "kg", 90, IngredientUsage.Mash, "Base Malt")]
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!;
    }

    private async Task<BatchDetailDto> CreateSampleBatchAsync(
        HttpClient client,
        Guid recipeId,
        string? name = null,
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
            BatchCode: $"ADV-{Guid.NewGuid():N}"[..12],
            Name: name ?? $"Adversarial Batch {Guid.NewGuid():N}"[..22],
            BeerStyle: "Imperial Stout",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 50.0m,
            TargetOg: 1.090m,
            TargetFg: 1.020m,
            TargetAbv: 9.5m,
            TargetIbu: 65.0m,
            TargetColorSrm: 40.0m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 80.0m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            HltId: hltId,
            MashTunId: mashTunId,
            PackagingVesselId: packagingVesselId
        );

        var res = await client.PostAsJsonAsync("/api/v1/batches", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

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

    #region Adversarial Stress Tests

    [Fact]
    public async Task Adversarial_RapidSequentialAndConcurrentDeletions_AcrossMultipleBatches()
    {
        // 1. Arrange: User and setup
        var (client, userId, _, setupId) = await CreateUserAsync("stress_seq");

        // 2. Create 10 pieces of equipment
        var kettleA = await CreateEquipmentAsync(client, setupId, "Kettle A 100L", EquipmentType.Boiler, EquipmentSubtype.AllInOne, 100m);
        var kettleB = await CreateEquipmentAsync(client, setupId, "Kettle B 100L", EquipmentType.Boiler, EquipmentSubtype.AllInOne, 100m);
        var fv1 = await CreateEquipmentAsync(client, setupId, "Fermenter FV-1", EquipmentType.Fermenter, EquipmentSubtype.ConicalFermenter, 120m);
        var fv2 = await CreateEquipmentAsync(client, setupId, "Fermenter FV-2", EquipmentType.Fermenter, EquipmentSubtype.ConicalFermenter, 120m);
        var mashA = await CreateEquipmentAsync(client, setupId, "Mash Tun Alpha", EquipmentType.Boiler, EquipmentSubtype.MashTun, 90m);
        var mashB = await CreateEquipmentAsync(client, setupId, "Mash Tun Beta", EquipmentType.Boiler, EquipmentSubtype.MashTun, 90m);
        var hlt1 = await CreateEquipmentAsync(client, setupId, "HLT North", EquipmentType.Boiler, EquipmentSubtype.Hlt, 80m);
        var hlt2 = await CreateEquipmentAsync(client, setupId, "HLT South", EquipmentType.Boiler, EquipmentSubtype.Hlt, 80m);
        var tilt1 = await CreateEquipmentAsync(client, setupId, "Tilt Hydrometer Purple", EquipmentType.Sensor, EquipmentSubtype.Tilt, 0m);
        var tilt2 = await CreateEquipmentAsync(client, setupId, "Tilt Hydrometer Orange", EquipmentType.Sensor, EquipmentSubtype.Tilt, 0m);

        var allEquipment = new[] { kettleA, kettleB, fv1, fv2, mashA, mashB, hlt1, hlt2, tilt1, tilt2 };

        // 3. Create recipe and 6 interlocking active batches
        var recipe = await CreateSampleRecipeAsync(client, "Interlocking Cascade Recipe");

        var b1 = await CreateSampleBatchAsync(client, recipe.Id, "Batch-1", boilerId: kettleA.Id, fermenterId: fv1.Id, hltId: hlt1.Id, mashTunId: mashA.Id);
        var b2 = await CreateSampleBatchAsync(client, recipe.Id, "Batch-2", boilerId: kettleB.Id, fermenterId: fv2.Id, hltId: hlt2.Id, mashTunId: mashB.Id);
        var b3 = await CreateSampleBatchAsync(client, recipe.Id, "Batch-3", boilerId: kettleA.Id, fermenterId: fv1.Id, hltId: hlt2.Id, mashTunId: mashB.Id);
        var b4 = await CreateSampleBatchAsync(client, recipe.Id, "Batch-4", boilerId: kettleB.Id, fermenterId: fv2.Id, hltId: hlt1.Id, mashTunId: mashA.Id);
        var b5 = await CreateSampleBatchAsync(client, recipe.Id, "Batch-5", boilerId: kettleA.Id, fermenterId: fv2.Id);
        var b6 = await CreateSampleBatchAsync(client, recipe.Id, "Batch-6", boilerId: kettleB.Id, fermenterId: fv1.Id);

        var allBatches = new[] { b1, b2, b3, b4, b5, b6 };

        // Assign sensors across batches
        var assignRes1 = await client.PutAsJsonAsync($"/api/v1/batches/{b1.Id}/sensors",
            new List<BatchSensorAssignmentInput> { new(tilt1.Id, BrewStage.Mash) });
        assignRes1.StatusCode.Should().Be(HttpStatusCode.OK);

        var assignRes2 = await client.PutAsJsonAsync($"/api/v1/batches/{b2.Id}/sensors",
            new List<BatchSensorAssignmentInput> { new(tilt2.Id, BrewStage.Boil) });
        assignRes2.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Act Part A: Unforced deletions must all be rejected with 409 Conflict (EQUIPMENT_IN_USE)
        foreach (var eq in allEquipment)
        {
            var unforcedRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{eq.Id}");
            unforcedRes.StatusCode.Should().Be(HttpStatusCode.Conflict, $"Equipment {eq.Name} is used in active batches and must be protected without force");
            var errEnvelope = await unforcedRes.Content.ReadFromJsonAsync<ApiResponse>();
            errEnvelope!.Error!.Code.Should().Be("EQUIPMENT_IN_USE");
        }

        // Verify nothing was deleted
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var count = await db.Equipment.CountAsync(e => e.UserId == userId);
            count.Should().Be(10, "unforced delete attempts must not remove any equipment");
        }

        // 5. Act Part B: Rapid concurrent force deletion of all 10 equipment items
        var deleteTasks = allEquipment.Select(eq => client.DeleteAsync($"/api/v1/inventory/equipment/{eq.Id}?force=true")).ToList();
        var responses = await Task.WhenAll(deleteTasks);

        // 6. Assert all force deletes succeeded with 200 OK
        foreach (var res in responses)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK, "Force delete on equipment must succeed cleanly without FK or concurrency exception");
        }

        // 7. Assert DB State: Zero equipment remaining for user, zero sensor assignments
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var remainingEq = await db.Equipment.CountAsync(e => e.UserId == userId);
            remainingEq.Should().Be(0, "all equipment must be deleted from database");

            var remainingSensors = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == tilt1.Id || sa.EquipmentId == tilt2.Id);
            remainingSensors.Should().Be(0, "all sensor assignments must be removed");

            // Verify all 6 batches exist and have clean null references for all 5 vessels
            var batchesInDb = await db.Batches.Where(b => b.UserId == userId).ToListAsync();
            batchesInDb.Should().HaveCount(6);
            foreach (var batch in batchesInDb)
            {
                batch.BoilerId.Should().BeNull();
                batch.FermenterId.Should().BeNull();
                batch.MashTunId.Should().BeNull();
                batch.HltId.Should().BeNull();
                batch.PackagingVesselId.Should().BeNull();
            }
        }

        // 8. Assert API Batch View: Every batch loads cleanly via HTTP GET
        foreach (var b in allBatches)
        {
            var getRes = await client.GetAsync($"/api/v1/batches/{b.Id}");
            getRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = (await getRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
            detail.BoilerId.Should().BeNull();
            detail.FermenterId.Should().BeNull();
            detail.MashTunId.Should().BeNull();
            detail.HltId.Should().BeNull();
            detail.PackagingVesselId.Should().BeNull();
            detail.SensorAssignments.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Adversarial_DeleteEquipment_LinkedAsMultipleVesselsSimultaneously_DecouplesAllSlots()
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateUserAsync("multivessel_adv");

        // 2. Create multipurpose All-In-One brew kettle
        var rig = await CreateEquipmentAsync(
            client, setupId, "Grainfather G70 Multipurpose", EquipmentType.Boiler, EquipmentSubtype.AllInOne, 70m);

        var recipe = await CreateSampleRecipeAsync(client, "Multipurpose Rig Ale");

        // 3. Create Batch 1: Rig is used as Hlt, MashTun, AND Boiler simultaneously
        var b1 = await CreateSampleBatchAsync(client, recipe.Id, "Triple-Vessel Batch",
            boilerId: rig.Id, hltId: rig.Id, mashTunId: rig.Id,
            status: BatchStatus.Brewing, stage: BrewStage.Mash);

        // 4. Create Batch 2: Rig is used as MashTun AND Boiler
        var b2 = await CreateSampleBatchAsync(client, recipe.Id, "Dual-Vessel Batch",
            boilerId: rig.Id, mashTunId: rig.Id,
            status: BatchStatus.Brewing, stage: BrewStage.Boil);

        // 5. Create Batch 3: Rig is used as Boiler AND PackagingVessel (e.g. bottling bucket)
        var b3 = await CreateSampleBatchAsync(client, recipe.Id, "Boil-and-Package Batch",
            boilerId: rig.Id, packagingVesselId: rig.Id,
            status: BatchStatus.Fermenting, stage: BrewStage.Ferment);

        // 6. Inspect Active Batches API for multi-vessel assignment
        var activeRes = await client.GetAsync($"/api/v1/inventory/equipment/{rig.Id}/active-batches");
        activeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeList = (await activeRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentActiveBatchDto>>>())!.Data!;
        activeList.Should().HaveCount(3);
        activeList.Should().Contain(b => b.Id == b1.Id);
        activeList.Should().Contain(b => b.Id == b2.Id);
        activeList.Should().Contain(b => b.Id == b3.Id);

        // 7. Act: Delete equipment with ?force=true
        var deleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{rig.Id}?force=true");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK, "Deleting equipment linked to multiple vessel slots simultaneously must succeed without error");

        // 8. Assert DB State: All slots decoupled to null on every batch
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var rigExists = await db.Equipment.AnyAsync(e => e.Id == rig.Id);
            rigExists.Should().BeFalse("equipment must be purged");

            var db1 = await db.Batches.FindAsync(b1.Id);
            db1!.BoilerId.Should().BeNull("BoilerId must be nullified");
            db1.HltId.Should().BeNull("HltId must be nullified");
            db1.MashTunId.Should().BeNull("MashTunId must be nullified");

            var db2 = await db.Batches.FindAsync(b2.Id);
            db2!.BoilerId.Should().BeNull("BoilerId must be nullified");
            db2.MashTunId.Should().BeNull("MashTunId must be nullified");

            var db3 = await db.Batches.FindAsync(b3.Id);
            db3!.BoilerId.Should().BeNull("BoilerId must be nullified");
            db3.PackagingVesselId.Should().BeNull("PackagingVesselId must be nullified");
        }

        // 9. Assert API Batch Details: All slots return null via HTTP
        var b1Detail = (await (await client.GetAsync($"/api/v1/batches/{b1.Id}")).Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        b1Detail.BoilerId.Should().BeNull();
        b1Detail.HltId.Should().BeNull();
        b1Detail.MashTunId.Should().BeNull();

        var b2Detail = (await (await client.GetAsync($"/api/v1/batches/{b2.Id}")).Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        b2Detail.BoilerId.Should().BeNull();
        b2Detail.MashTunId.Should().BeNull();

        var b3Detail = (await (await client.GetAsync($"/api/v1/batches/{b3.Id}")).Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        b3Detail.BoilerId.Should().BeNull();
        b3Detail.PackagingVesselId.Should().BeNull();
    }

    [Fact]
    public async Task Adversarial_DeleteSensor_LinkedToMultipleBatchesAcrossMultipleStages_DecouplesCleanly()
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateUserAsync("sensor_multistage");

        // 2. Create single sensor equipment
        var sensor = await CreateEquipmentAsync(
            client, setupId, "Tilt Hydrometer Yellow Pro", EquipmentType.Sensor, EquipmentSubtype.Tilt, 0m);

        var recipe = await CreateSampleRecipeAsync(client, "Sensor Multistage Recipe");

        // 3. Create 3 batches in different stages
        var bMash = await CreateSampleBatchAsync(client, recipe.Id, "Mash Stage Batch", status: BatchStatus.Brewing, stage: BrewStage.Mash);
        var bBoil = await CreateSampleBatchAsync(client, recipe.Id, "Boil Stage Batch", status: BatchStatus.Brewing, stage: BrewStage.Boil);
        var bFerm = await CreateSampleBatchAsync(client, recipe.Id, "Ferment Stage Batch", status: BatchStatus.Fermenting, stage: BrewStage.Ferment);

        // Assign sensor to Batch 1 at Mash stage
        var assignRes1 = await client.PutAsJsonAsync($"/api/v1/batches/{bMash.Id}/sensors",
            new List<BatchSensorAssignmentInput> { new(sensor.Id, BrewStage.Mash) });
        assignRes1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assign same sensor to Batch 2 at Boil stage
        var assignRes2 = await client.PutAsJsonAsync($"/api/v1/batches/{bBoil.Id}/sensors",
            new List<BatchSensorAssignmentInput> { new(sensor.Id, BrewStage.Boil) });
        assignRes2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assign same sensor to Batch 3 at Ferment stage
        var assignRes3 = await client.PutAsJsonAsync($"/api/v1/batches/{bFerm.Id}/sensors",
            new List<BatchSensorAssignmentInput> { new(sensor.Id, BrewStage.Ferment) });
        assignRes3.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify 3 sensor assignment records exist in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var saCount = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == sensor.Id);
            saCount.Should().Be(3, "sensor must be assigned across 3 distinct batches");
        }

        // 4. Act: Delete the sensor with force=true
        var deleteRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{sensor.Id}?force=true");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK, "Deleting sensor linked across multiple batches and stages must not throw FK 23503");

        // 5. Assert DB State: Sensor purged and all 3 assignments removed
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var sensorExists = await db.Equipment.AnyAsync(e => e.Id == sensor.Id);
            sensorExists.Should().BeFalse("sensor entity must be deleted");

            var saCount = await db.BatchSensorAssignments.CountAsync(sa => sa.EquipmentId == sensor.Id);
            saCount.Should().Be(0, "all sensor assignments across all stages must be decoupled");
        }

        // 6. Assert API: All 3 batches still exist and have empty SensorAssignments
        foreach (var batchId in new[] { bMash.Id, bBoil.Id, bFerm.Id })
        {
            var getRes = await client.GetAsync($"/api/v1/batches/{batchId}");
            getRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = (await getRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
            detail.SensorAssignments.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Adversarial_FullTopology_BrewerySetupWithTapsKegsVesselsSensors_NoOrphansOrFKExceptions()
    {
        // 1. Arrange: User and custom setup
        var (client, userId, _, _) = await CreateUserAsync("full_topology");

        var createSetupRes = await client.PostAsJsonAsync("/api/v1/brewery-setups",
            new CreateBrewerySetupRequest("Production Complex Brewhouse"));
        createSetupRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var setup = (await createSetupRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!;

        // 2. Create full topology: 2 Taps, 2 Kegs, 1 Kettle, 1 Fermenter, 1 Sensor
        var keg1 = await CreateEquipmentAsync(client, setup.Id, "Serving Keg Alpha 19L", EquipmentType.Keg, EquipmentSubtype.Cornelius, 19m);
        var keg2 = await CreateEquipmentAsync(client, setup.Id, "Serving Keg Beta 19L", EquipmentType.Keg, EquipmentSubtype.Cornelius, 19m);
        var tap1 = await CreateEquipmentAsync(client, setup.Id, "Tap #1 Stout", EquipmentType.Tap, EquipmentSubtype.StandardTap, 0m);
        var tap2 = await CreateEquipmentAsync(client, setup.Id, "Tap #2 IPA", EquipmentType.Tap, EquipmentSubtype.StandardTap, 0m);
        var kettle = await CreateEquipmentAsync(client, setup.Id, "Braumeister 50L", EquipmentType.Boiler, EquipmentSubtype.AllInOne, 50m);
        var fv = await CreateEquipmentAsync(client, setup.Id, "Conical 60L", EquipmentType.Fermenter, EquipmentSubtype.ConicalFermenter, 60m);
        var sensor = await CreateEquipmentAsync(client, setup.Id, "Tilt Hydrometer Blue 2", EquipmentType.Sensor, EquipmentSubtype.Tilt, 0m);

        // Assign Kegs to Taps
        var tap1Assign = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap1.Id}/tap-assignment", new AssignTapRequest(KegId: keg1.Id));
        tap1Assign.StatusCode.Should().Be(HttpStatusCode.OK);
        var tap2Assign = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{tap2.Id}/tap-assignment", new AssignTapRequest(KegId: keg2.Id));
        tap2Assign.StatusCode.Should().Be(HttpStatusCode.OK);

        // Create recipe and batches
        var recipe = await CreateSampleRecipeAsync(client, "Full Topology Stout");
        var activeBatch = await CreateSampleBatchAsync(client, recipe.Id, "Active Brewing Batch",
            boilerId: kettle.Id, fermenterId: fv.Id, status: BatchStatus.Brewing, stage: BrewStage.Boil);

        var sensorAssign = await client.PutAsJsonAsync($"/api/v1/batches/{activeBatch.Id}/sensors",
            new List<BatchSensorAssignmentInput> { new(sensor.Id, BrewStage.Boil) });
        sensorAssign.StatusCode.Should().Be(HttpStatusCode.OK);

        var packagedBatch = await CreateSampleBatchAsync(client, recipe.Id, "Packaged Batch",
            packagingVesselId: keg1.Id, status: BatchStatus.Completed, stage: BrewStage.Package);

        // Step A: Delete Keg 1 (linked to Tap 1 AND PackagedBatch)
        var delKegRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{keg1.Id}");
        delKegRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Tap 1 has null AssignedKegId and PackagedBatch has null PackagingVesselId
        var tap1Dto = (await (await client.GetAsync($"/api/v1/inventory/equipment/{tap1.Id}")).Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        tap1Dto.AssignedKegId.Should().BeNull();

        var pkgBatchDto = (await (await client.GetAsync($"/api/v1/batches/{packagedBatch.Id}")).Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        pkgBatchDto.PackagingVesselId.Should().BeNull();

        // Step B: Delete Brewery Setup containing all remaining equipment
        var delSetupRes = await client.DeleteAsync($"/api/v1/brewery-setups/{setup.Id}");
        delSetupRes.StatusCode.Should().Be(HttpStatusCode.OK, "Deleting brewery setup must cascade cleanly without FK constraint failures");

        // Assert DB State: Zero orphaned records
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var setupExists = await db.BrewerySetups.AnyAsync(s => s.Id == setup.Id);
            setupExists.Should().BeFalse();

            var anySetupEqExists = await db.Equipment.AnyAsync(e =>
                e.Id == keg1.Id || e.Id == keg2.Id || e.Id == tap1.Id || e.Id == tap2.Id || e.Id == kettle.Id || e.Id == fv.Id || e.Id == sensor.Id);
            anySetupEqExists.Should().BeFalse("all setup equipment must be completely deleted");

            var anyAssignments = await db.BatchSensorAssignments.AnyAsync(sa => sa.EquipmentId == sensor.Id);
            anyAssignments.Should().BeFalse("all sensor assignments must be removed");

            var dbActiveBatch = await db.Batches.FindAsync(activeBatch.Id);
            dbActiveBatch!.BoilerId.Should().BeNull();
            dbActiveBatch.FermenterId.Should().BeNull();
            dbActiveBatch.BrewerySetupId.Should().BeNull();

            var dbPkgBatch = await db.Batches.FindAsync(packagedBatch.Id);
            dbPkgBatch!.PackagingVesselId.Should().BeNull();
            dbPkgBatch.BrewerySetupId.Should().NotBe(setup.Id, "batch must not reference the deleted setup");
        }
    }

    [Fact]
    public async Task Adversarial_ConcurrentDoubleDelete_RaceConditionResilience()
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateUserAsync("race_delete");

        var kettle = await CreateEquipmentAsync(
            client, setupId, "Race Condition Kettle", EquipmentType.Boiler, EquipmentSubtype.AllInOne, 30m);

        // 2. Act: Send two concurrent DELETE requests for the exact same equipment
        var task1 = client.DeleteAsync($"/api/v1/inventory/equipment/{kettle.Id}");
        var task2 = client.DeleteAsync($"/api/v1/inventory/equipment/{kettle.Id}");

        var results = await Task.WhenAll(task1, task2);

        // 3. Assert: Neither request must throw a 500 Internal Server Error
        results.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError,
            "Concurrent deletions must be handled gracefully without 500 internal server error");

        // Exactly one should succeed with 200 OK, the other should be 409 Conflict or 404 Not Found
        var statusCodes = results.Select(r => r.StatusCode).ToList();
        statusCodes.Should().Contain(HttpStatusCode.OK);
        statusCodes.Should().Match(codes => codes.Contains(HttpStatusCode.Conflict) || codes.Contains(HttpStatusCode.NotFound),
            "The secondary concurrent delete must fail cleanly with Conflict or NotFound, never 500");
    }

    [Fact]
    public async Task Adversarial_ActiveBatchGuard_IncludesEquipmentAssignedAsTapOrKegBatch()
    {
        // 1. Arrange: User and setup
        var (client, _, _, setupId) = await CreateUserAsync("tap_batch_guard");

        var tap = await CreateEquipmentAsync(
            client, setupId, "Bar Tap Dedicated", EquipmentType.Tap, EquipmentSubtype.StandardTap, 0m);

        var recipe = await CreateSampleRecipeAsync(client, "Tap Guard Ale");
        var activeBatch = await CreateSampleBatchAsync(client, recipe.Id, "Tap Active Batch",
            status: BatchStatus.Fermenting, stage: BrewStage.Ferment);

        // Assign active batch directly to the Tap entity
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var trackedTap = await db.Equipment.FindAsync(tap.Id);
            trackedTap!.AssignedBatchId = activeBatch.Id;
            await db.SaveChangesAsync();
        }

        // 2. Act Part A: Attempt unforced delete
        var unforcedRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{tap.Id}");

        // 3. Assert: 409 Conflict with EQUIPMENT_IN_USE
        unforcedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errEnv = await unforcedRes.Content.ReadFromJsonAsync<ApiResponse>();
        errEnv!.Error!.Code.Should().Be("EQUIPMENT_IN_USE");

        // 4. Act Part B: Force delete
        var forcedRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{tap.Id}?force=true");
        forcedRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Assert DB state: Tap is purged and active batch remains intact
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var tapExists = await db.Equipment.AnyAsync(e => e.Id == tap.Id);
            tapExists.Should().BeFalse();

            var b = await db.Batches.FindAsync(activeBatch.Id);
            b.Should().NotBeNull();
        }
    }

    #endregion
}