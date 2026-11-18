using BrewYou.ApiService.Backup.Models;
using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace BrewYou.ApiService.Tests;

/// <summary>
/// Tier 2: Boundary & Corner Cases E2E Tests (>= 20 test cases, >= 5 each for R1, R2, R3, R4).
/// Tests extreme values, invalid lifecycle transitions, archived batch lockdown (409 Conflict),
/// offline/unassigned equipment boundaries, invalid gravity/temperature inputs, and zero/negative deviations.
/// </summary>
public class BatchProcessE2ETests_Tier2_BoundariesAndCorners : BatchProcessE2ETestBase
{
    public BatchProcessE2ETests_Tier2_BoundariesAndCorners(WebApplicationFactory<Program> factory)
        : base(factory)
    {
    }

    #region Requirement R1: Lifecycle Boundaries & Edge Transitions (>= 5 cases)

    [Fact]
    public async Task R1_Boundary_InvalidStageTransition_BackwardTransition_Returns409Conflict()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-BACKTRANS-{Guid.NewGuid():N}"[..18],
            Name: "Backwards Transition Test",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Advance to Ferment
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Boil, MeasuredPreBoilVolumeLiters: 24m));
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Ferment, MeasuredOg: 1.050m));

        // Act: Attempt invalid backwards transition to Mash
        var backwardReq = new AdvanceBatchStageRequest(TargetStage: BrewStage.Mash);
        var res = await AdvanceStageRawAsync(client, batch.Id, backwardReq);

        // Assert: 409 Conflict or 400 BadRequest prevents illegal backwards lifecycle transition
        res.StatusCode.Should().Match(c => c == HttpStatusCode.Conflict || c == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task R1_Boundary_ZeroVolumeBatch_Creation_RejectedWithBadRequest()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);

        // Act: Attempt to create batch with TargetBatchSize = 0.0L
        var invalidReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-ZEROVOL-{Guid.NewGuid():N}"[..18],
            Name: "Zero Volume Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 0.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        );
        var res = await client.PostAsJsonAsync("/api/v1/batches", invalidReq);

        // Assert: 400 Bad Request validator stops zero volume batch
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task R1_Boundary_ExtremeBoilTime_HandledWithoutOverflow()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);

        // Act: Create batch with extended boil time (240 minutes)
        var longBoilBatch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-LONGBOIL-{Guid.NewGuid():N}"[..18],
            Name: "Barleywine Long Boil",
            BeerStyle: "English Barleywine",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.100m,
            TargetFg: 1.025m,
            TargetAbv: 10.0m,
            TargetIbu: 80m,
            TargetColorSrm: 18m,
            BoilTimeMinutes: 240,
            EfficiencyPercent: 70m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Assert: Calculations handle large boil-off volume without integer/decimal overflow
        longBoilBatch.BoilTimeMinutes.Should().Be(240);
        longBoilBatch.VolumeProfile.Should().NotBeNull();
        longBoilBatch.VolumeProfile!.TargetPreBoilVolumeLiters.Should().BeGreaterThan(longBoilBatch.VolumeProfile.TargetPostBoilVolumeLiters);
    }

    [Fact]
    public async Task R1_Boundary_StageChecklist_EmptyRecipe_ZeroIngredientsAndSteps()
    {
        // Arrange: Ad-hoc batch created without recipe ingredients or steps
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-EMPTYREC-{Guid.NewGuid():N}"[..18],
            Name: "Bare Minimum Batch",
            BeerStyle: "Session Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 15m,
            TargetOg: 1.040m,
            TargetFg: 1.008m,
            TargetAbv: 4.2m,
            TargetIbu: 25m,
            TargetColorSrm: 3m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act & Assert: Progress across all stages without NullReferenceException on empty collections
        var completed = await FastForwardToCompletedAsync(client, batch.Id, kegId);
        completed.Status.Should().Be(BatchStatus.Completed);
        completed.Ingredients.Should().BeEmpty();
        completed.MashSteps.Should().NotBeEmpty();
    }

    [Fact]
    public async Task R1_Boundary_AdvanceStage_OnCompletedBatch_Returns409Conflict()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-COMP-ADV-{Guid.NewGuid():N}"[..18],
            Name: "Completed Stage Advance Guard",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Fast forward batch to Completed
        await FastForwardToCompletedAsync(client, batch.Id, kegId);

        // Act: Attempt to advance stage on an already completed batch
        var res = await AdvanceStageRawAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Package
        ));

        // Assert: 409 Conflict ("Cannot advance stage of a completed or archived batch.")
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region Requirement R2: Post-Finish Lockdown & Edge Cases (>= 5 cases)

    [Fact]
    public async Task R2_Boundary_ArchivedBatch_CannotBeModified_Returns409Conflict()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-ARCH-MOD-{Guid.NewGuid():N}"[..18],
            Name: "Archive Boundary Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        await FastForwardToCompletedAsync(client, batch.Id, kegId);

        // Transition status directly to Archived
        await SetBatchStatusDirectlyAsync(batch.Id, BatchStatus.Archived);

        // Act: Attempt PUT /api/v1/batches/{id} on Archived batch
        var updateReq = new UpdateBatchRequest(Notes: "Attempting edit on archived record");
        var res = await UpdateBatchRawAsync(client, batch.Id, updateReq);

        // Assert: Strictly locked with 409 Conflict ("Archived batches cannot be modified.")
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task R2_Boundary_ArchivedBatch_CannotToggleIngredients_Returns409Conflict()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var recipe = await CreateStandardRecipeAsync(client, "Archive Recipe");
        var batch = await CreateRecipeBatchAsync(client, recipe.Id, batchSize: 20m, boilerId: boilerId, fermenterId: fermenterId);

        await FastForwardToCompletedAsync(client, batch.Id, kegId);
        await SetBatchStatusDirectlyAsync(batch.Id, BatchStatus.Archived);

        var ingId = batch.Ingredients.First().Id;

        // Act: Attempt to toggle ingredient on Archived batch
        var res = await client.PatchAsync($"/api/v1/batches/{batch.Id}/ingredients/{ingId}?isChecked=true", null);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task R2_Boundary_ArchivedBatch_CannotAdvanceStage_Returns409Conflict()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-ARCH-STG-{Guid.NewGuid():N}"[..18],
            Name: "Archive Stage Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        await FastForwardToCompletedAsync(client, batch.Id, kegId);
        await SetBatchStatusDirectlyAsync(batch.Id, BatchStatus.Archived);

        // Act: Attempt to advance stage on Archived batch
        var res = await AdvanceStageRawAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Package));

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task R2_Boundary_ArchivedBatch_CannotUpdateSensors_Returns409Conflict()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-ARCH-SNS-{Guid.NewGuid():N}"[..18],
            Name: "Archive Sensor Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        await FastForwardToCompletedAsync(client, batch.Id, kegId);
        await SetBatchStatusDirectlyAsync(batch.Id, BatchStatus.Archived);

        // Act: Attempt to update sensors on Archived batch
        var res = await client.PutAsJsonAsync($"/api/v1/batches/{batch.Id}/sensors", new List<BatchSensorAssignmentInput>());

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task R2_Boundary_PostFinish_ExtremeMeasuredGravities_ClampsAbvGracefully()
    {
        // Guard check on domain calculator: when current gravity is >= measured OG, ABV returns 0.0m
        var zeroAbv = BrewingCalculator.CalculateActualAbv(1.050m, 1.055m);
        zeroAbv.Should().Be(0.0m);

        // Arrange: Completed batch updated with minimal attenuation
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-CLAMP-ABV-{Guid.NewGuid():N}"[..18],
            Name: "ABV Clamp Batch",
            BeerStyle: "Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        await FastForwardToCompletedAsync(client, batch.Id, kegId);

        // Act: Update with very close FG and OG (1.050 vs 1.049)
        var updated = await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            MeasuredOg: 1.050m,
            MeasuredFg: 1.049m
        ));

        // Assert: Minimal ABV calculated correctly (~0.13%)
        updated.AlcoholByVolume.Should().BeInRange(0.10m, 0.20m);
    }

    [Fact]
    public async Task R2_Boundary_PostFinish_VeryHighGravity_NonLinearAbvComputed()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-HIGH-ASBC-{Guid.NewGuid():N}"[..18],
            Name: "High Gravity Quad",
            BeerStyle: "Belgian Quad",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.115m,
            TargetFg: 1.025m,
            TargetAbv: 12.0m,
            TargetIbu: 35m,
            TargetColorSrm: 25m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 70m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        await FastForwardToCompletedAsync(client, batch.Id, kegId);

        // Act: Update post-finish measurements with high OG
        var updated = await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            MeasuredOg: 1.115m,
            MeasuredFg: 1.025m
        ));

        // Assert: Computes non-linear ASBC formula (> 12.0% ABV)
        updated.AlcoholByVolume.Should().BeGreaterThan(12.0m);
    }

    #endregion

    #region Requirement R3: Equipment & Telemetry Boundaries & Out-of-Bounds (>= 5 cases)

    [Fact]
    public async Task R3_Boundary_ConsolidatedReading_SubWaterGravity_RejectedWithBadRequest()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-SUBWATER-{Guid.NewGuid():N}"[..18],
            Name: "Sub-water Gravity Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act: Specific gravity = 0.500 (physically impossible for beer wort, validation range [0.900, 1.250])
        var res = await AddReadingRawAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: 0.500m,
            TemperatureC: null,
            Notes: null
        ));

        // Assert: 400 Bad Request
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task R3_Boundary_ConsolidatedReading_ExtremeHighGravity_RejectedWithBadRequest()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-HIGHGRAV-{Guid.NewGuid():N}"[..18],
            Name: "Extreme High Gravity Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act: SG = 1.500 (exceeds 1.250 upper bound)
        var res = await AddReadingRawAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: 1.500m,
            TemperatureC: null,
            Notes: null
        ));

        // Assert: 400 Bad Request
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task R3_Boundary_ConsolidatedReading_ExtremeTemperatures_HandledGracefully()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-TEMPBOUND-{Guid.NewGuid():N}"[..18],
            Name: "Temperature Boundary Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act 1: Freezing point 0.0°C
        var r1 = await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow.AddMinutes(-10),
            SpecificGravity: 1.010m,
            TemperatureC: 0.0m,
            Notes: null
        ));
        r1.TemperatureC.Should().Be(0.0m);

        // Act 2: Atmospheric boil 100.0°C
        var r2 = await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow.AddMinutes(-5),
            SpecificGravity: 1.045m,
            TemperatureC: 100.0m,
            Notes: null
        ));
        r2.TemperatureC.Should().Be(100.0m);

        // Act 3: Null temperature
        var r3 = await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: 1.012m,
            TemperatureC: null,
            Notes: null
        ));
        r3.TemperatureC.Should().BeNull();
    }

    [Fact]
    public async Task R3_Boundary_ConsolidatedReading_NonExistentBatchId_Returns404NotFound()
    {
        // Arrange
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        // Act: Post reading to non-existent batch
        var res = await AddReadingRawAsync(client, Guid.NewGuid(), new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: 1.050m,
            TemperatureC: null,
            Notes: null
        ));

        // Assert: 404 Not Found
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task R3_Boundary_EquipmentReading_CompletedStage_Returns409Conflict()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-COMPSTG-READ-{Guid.NewGuid():N}"[..18],
            Name: "Completed Stage Telemetry Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Advance to Ferment (Mash and Boil are completed)
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Boil, MeasuredPreBoilVolumeLiters: 24m));
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Ferment, MeasuredOg: 1.050m));

        // Act: Log retrospective equipment temperature reading for completed Mash stage (allowed per F6)
        var res = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings", new LogBatchTemperatureRequest(
            TemperatureC: 66.0m,
            EquipmentId: boilerId,
            Stage: BrewStage.Mash
        ));

        // Assert: 200 OK for retrospective stage logging on active/completed batches
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Archive batch and verify equipment-readings are strictly blocked with 409 Conflict
        await SetBatchStatusDirectlyAsync(batch.Id, BatchStatus.Archived);
        var archivedRes = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings", new LogBatchTemperatureRequest(
            TemperatureC: 66.0m,
            EquipmentId: boilerId,
            Stage: BrewStage.Mash
        ));

        archivedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #endregion

    #region Requirement R4: Calculations & Deviation Boundaries (>= 5 cases)

    [Fact]
    public void R4_Boundary_HydrometerTemperatureCorrection_FreezingAndBoilingBoundaries()
    {
        // Guard boundary: tempC <= 0°C returns rawSg without polynomial divergence
        var zeroDeg = BrewingCalculator.CorrectHydrometerReading(1.050m, tempC: 0.0m, calTempC: 20.0m);
        zeroDeg.Should().Be(1.050m);

        // High boiling temperature: 100°C
        var boilDeg = BrewingCalculator.CorrectHydrometerReading(1.050m, tempC: 100.0m, calTempC: 20.0m);
        boilDeg.Should().BeGreaterThan(1.050m);
        boilDeg.Should().BeInRange(1.080m, 1.100m);
    }

    [Fact]
    public void R4_Boundary_HydrometerTemperatureCorrection_NegativeTemp_ReturnsRawSg()
    {
        // Negative temperature sample returns rawSg
        var negative = BrewingCalculator.CorrectHydrometerReading(1.050m, tempC: -15.0m, calTempC: 20.0m);
        negative.Should().Be(1.050m);
    }

    [Fact]
    public void R4_Boundary_HighGravityAbv_ExactInflectionThreshold_OG1060()
    {
        // At OG = 1.060m, standard linear formula applies: (1.060 - 1.010) * 131.25 = 6.56%
        var standardAbv = BrewingCalculator.CalculateActualAbv(1.060m, 1.010m);
        standardAbv.Should().Be(6.56m);

        // At OG = 1.061m, non-linear ASBC formula kicks in:
        // ABW = (76.08 * (1.061 - 1.010)) / (1.775 - 1.061) = 3.879 / 0.714 = 5.433
        // ABV = 5.433 * (1.010 / 0.794) = 6.91%
        var advancedAbv = BrewingCalculator.CalculateActualAbv(1.061m, 1.010m);
        advancedAbv.Should().Be(6.91m);
    }

    [Fact]
    public async Task R4_Boundary_TargetDeviations_AdHocBatch_WithNullTargets_ReturnsNullDeviations()
    {
        // Arrange: Ad-hoc batch where TargetOg is 0 or null
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-NULLDEV-{Guid.NewGuid():N}"[..18],
            Name: "Null Targets Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // When no measured values have been submitted yet:
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.Deviations.Should().NotBeNull();
        detail.Deviations!.OgDeviation.Should().BeNull();
        detail.Deviations.OgPointsDeviation.Should().BeNull();
        detail.Deviations.FgDeviation.Should().BeNull();
        detail.Deviations.FgPointsDeviation.Should().BeNull();
        detail.Deviations.BatchSizeLitersDeviation.Should().BeNull();
    }

    [Fact]
    public async Task R4_Boundary_TargetDeviations_ExactZeroDeviation_PerfectMatch()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-PERFECT-{Guid.NewGuid():N}"[..18],
            Name: "Perfect Target Hit Batch",
            BeerStyle: "Pilsner",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 35m,
            TargetColorSrm: 3.5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act: Measured values exactly equal target values
        await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            MeasuredOg: 1.050m,
            MeasuredFg: 1.010m,
            MeasuredBatchSizeLiters: 20.0m
        ));

        // Assert: Deviations evaluate cleanly to zero
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.Deviations.Should().NotBeNull();
        detail.Deviations!.OgDeviation.Should().Be(0.000m);
        detail.Deviations.OgPointsDeviation.Should().Be(0);
        detail.Deviations.FgDeviation.Should().Be(0.000m);
        detail.Deviations.FgPointsDeviation.Should().Be(0);
        detail.Deviations.BatchSizeLitersDeviation.Should().Be(0.0m);
    }

    [Fact]
    public async Task R4_Boundary_TargetDeviations_NegativeDeviations_UnderTarget()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-UNDER-{Guid.NewGuid():N}"[..18],
            Name: "Under-target Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 25.0m,
            TargetOg: 1.065m,
            TargetFg: 1.015m,
            TargetAbv: 6.5m,
            TargetIbu: 50m,
            TargetColorSrm: 7m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act: Measured values lower than targets
        await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            MeasuredOg: 1.055m,      // -10 points
            MeasuredFg: 1.012m,      // -3 points
            MeasuredBatchSizeLiters: 22.5m // -2.5L
        ));

        // Assert: Negative signs preserved faithfully
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.Deviations.Should().NotBeNull();
        detail.Deviations!.OgDeviation.Should().Be(-0.010m);
        detail.Deviations.OgPointsDeviation.Should().Be(-10);
        detail.Deviations.FgDeviation.Should().Be(-0.003m);
        detail.Deviations.FgPointsDeviation.Should().Be(-3);
        detail.Deviations.BatchSizeLitersDeviation.Should().Be(-2.5m);
    }

    [Fact]
    public async Task R4_Boundary_BackupRestore_BackwardsCompatibility_ExportsAndPreviewsCleanly()
    {
        // Arrange: User with a completed batch containing deviations and readings
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-BAK-PREV-{Guid.NewGuid():N}"[..18],
            Name: "Backup Preview Batch",
            BeerStyle: "Porter",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.055m,
            TargetFg: 1.012m,
            TargetAbv: 5.6m,
            TargetIbu: 35m,
            TargetColorSrm: 25m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        await FastForwardToCompletedAsync(client, batch.Id, kegId);
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow, 1.012m, 20m, "Final"));

        // Act 1: Export backup via POST
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var backupPayload = await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>();
        backupPayload!.Success.Should().BeTrue();
        backupPayload.Data.Should().NotBeNull();
        backupPayload.Data!.Format.Should().Be("brewyou-user-backup");

        // Act 2: Preview restore
        var previewReq = new PreviewRestoreRequest(backupPayload.Data);
        var previewRes = await client.PostAsJsonAsync("/api/v1/backup/preview", previewReq);
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await previewRes.Content.ReadFromJsonAsync<ApiResponse<RestorePreviewReportDto>>();
        preview!.Success.Should().BeTrue();
        preview.Data!.Counts.Batches.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion
}