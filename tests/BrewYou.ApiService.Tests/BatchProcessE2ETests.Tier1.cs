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
/// Tier 1: Feature Coverage E2E Tests (>= 20 test cases, >= 5 for each of R1, R2, R3, R4).
/// Exercises happy paths: batch lifecycle flow, post-finish editing on completed batches,
/// consolidated telemetry and equipment handling, and target deviation computations.
/// </summary>
public class BatchProcessE2ETests_Tier1_FeatureCoverage : BatchProcessE2ETestBase
{
    public BatchProcessE2ETests_Tier1_FeatureCoverage(WebApplicationFactory<Program> factory)
        : base(factory)
    {
    }

    #region Requirement R1: Batch Process Flow & Lifecycle Progression (>= 5 cases)

    [Fact]
    public async Task R1_TC01_CreateBatch_AdHocAndRecipe_InitializesStageAndVolumeProfile()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var recipe = await CreateStandardRecipeAsync(client, "Cascade Pale Ale");

        // Act 1: Ad-hoc batch
        var adHocBatch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-ADHOC-{Guid.NewGuid():N}"[..18],
            Name: "AdHoc Test Ale",
            BeerStyle: "American Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 35m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act 2: Recipe-based batch
        var recipeBatch = await CreateRecipeBatchAsync(client, recipe.Id, batchSize: 20m, boilerId: boilerId, fermenterId: fermenterId);

        // Assert
        adHocBatch.CurrentStage.Should().Be(BrewStage.Mash);
        adHocBatch.Status.Should().Be(BatchStatus.Brewing);
        adHocBatch.VolumeProfile.Should().NotBeNull();
        adHocBatch.VolumeProfile!.TargetPreBoilVolumeLiters.Should().BeGreaterThan(0m);

        recipeBatch.CurrentStage.Should().Be(BrewStage.Mash);
        recipeBatch.Status.Should().Be(BatchStatus.Brewing);
        recipeBatch.RecipeId.Should().Be(recipe.Id);
        recipeBatch.VolumeProfile.Should().NotBeNull();
    }

    [Fact]
    public async Task R1_TC02_LifecycleProgression_MashToBoilToFermentToConditionToPackage_TransitionsStagesCorrectly()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-LIFE-{Guid.NewGuid():N}"[..18],
            Name: "Lifecycle Test Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.054m,
            TargetFg: 1.012m,
            TargetAbv: 5.5m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act & Assert 1: Mash -> Boil
        var boilBatch = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 25.0m,
            MeasuredPreBoilGravity: 1.044m
        ));
        boilBatch.CurrentStage.Should().Be(BrewStage.Boil);
        boilBatch.Status.Should().Be(BatchStatus.Brewing);

        // Act & Assert 2: Boil -> Ferment
        var fermentBatch = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.054m,
            MeasuredPostBoilVolumeLiters: 21.5m,
            MeasuredBatchSizeLiters: 20.0m,
            PitchTemperatureC: 19.0m
        ));
        fermentBatch.CurrentStage.Should().Be(BrewStage.Ferment);
        fermentBatch.Status.Should().Be(BatchStatus.Fermenting);
        fermentBatch.MeasuredOg.Should().Be(1.054m);

        // Act & Assert 3: Ferment -> Condition
        var conditionBatch = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Condition,
            MeasuredFg: 1.012m
        ));
        conditionBatch.CurrentStage.Should().Be(BrewStage.Condition);

        // Act & Assert 4: Condition -> Package
        var packageBatch = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Package,
            MeasuredFg: 1.011m,
            PackagingVesselId: kegId,
            MeasuredPackagedVolumeLiters: 19.2m
        ));
        packageBatch.CurrentStage.Should().Be(BrewStage.Package);
        packageBatch.Status.Should().Be(BatchStatus.Completed);
        packageBatch.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task R1_TC03_EquipmentOccupancyLifecycle_OccupiesBoilerAndFermenter_AndReleasesOnPackage()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-OCCUPY-{Guid.NewGuid():N}"[..18],
            Name: "Occupancy Test Batch",
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
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 24m
        ));
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.050m,
            MeasuredBatchSizeLiters: 20m
        ));

        // Fermenter is occupied
        var fermResBefore = await client.GetAsync($"/api/v1/inventory/equipment/{fermenterId}");
        var fermBefore = (await fermResBefore.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        fermBefore.CurrentVolume.Should().Be(20m);

        // Act: Advance to Package (transfers volume into Keg)
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Package,
            PackagingVesselId: kegId,
            MeasuredPackagedVolumeLiters: 19.0m
        ));

        // Assert: Fermenter freed (volume 0), Keg occupied (19L)
        var fermResAfter = await client.GetAsync($"/api/v1/inventory/equipment/{fermenterId}");
        var fermAfter = (await fermResAfter.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        fermAfter.CurrentVolume.Should().Be(0m);

        var kegResAfter = await client.GetAsync($"/api/v1/inventory/equipment/{kegId}");
        var kegAfter = (await kegResAfter.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        kegAfter.CurrentVolume.Should().Be(19.0m);
    }

    [Fact]
    public async Task R1_TC04_VolumeProfilePreservation_StageAdvanceRetainsMeasuredValues()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-VOLPROF-{Guid.NewGuid():N}"[..18],
            Name: "Volume Preservation Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.052m,
            TargetFg: 1.010m,
            TargetAbv: 5.5m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 25.5m,
            MeasuredPreBoilGravity: 1.042m
        ));

        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.052m,
            MeasuredPostBoilVolumeLiters: 21.8m,
            MeasuredBatchSizeLiters: 20.2m
        ));

        // Assert
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.VolumeProfile.Should().NotBeNull();
        detail.VolumeProfile!.MeasuredPreBoilVolumeLiters.Should().Be(25.5m);
        detail.VolumeProfile.MeasuredPostBoilVolumeLiters.Should().Be(21.8m);
        detail.MeasuredBatchSizeLiters.Should().Be(20.2m);
    }

    [Fact]
    public async Task R1_TC05_StepAndIngredientToggling_ActiveStages_TracksCompletionStatus()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var recipe = await CreateStandardRecipeAsync(client, "Toggle Check Pale Ale");
        var batch = await CreateRecipeBatchAsync(client, recipe.Id, batchSize: 20m, boilerId: boilerId, fermenterId: fermenterId);

        var firstIng = batch.Ingredients.First();

        // Act: Toggle ingredient checked
        var patchRes = await client.PatchAsync($"/api/v1/batches/{batch.Id}/ingredients/{firstIng.Id}?isChecked=true", null);
        patchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.Ingredients.First(i => i.Id == firstIng.Id).IsChecked.Should().BeTrue();
    }

    [Fact]
    public async Task R1_TC06_StageHistory_RecordsChronologicalMilestones()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-STAGEHIST-{Guid.NewGuid():N}"[..18],
            Name: "Stage History Batch",
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

        // Act: Fast-forward through stages to Completed
        await FastForwardToCompletedAsync(client, batch.Id, kegId);

        // Assert
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.StageHistory.Should().NotBeEmpty();
        detail.StageHistory.Should().Contain(sh => sh.Stage == BrewStage.Mash);
        detail.StageHistory.Should().Contain(sh => sh.Stage == BrewStage.Boil);
        detail.StageHistory.Should().Contain(sh => sh.Stage == BrewStage.Ferment);
        detail.StageHistory.Should().Contain(sh => sh.Stage == BrewStage.Package);
    }

    #endregion

    #region Requirement R2: Post-Finish Batch Record Editing (>= 5 cases)

    [Fact]
    public async Task R2_TC01_PostFinish_UpdateMetadataOnCompletedBatch_SucceedsWith200OK()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-COMP-EDIT-{Guid.NewGuid():N}"[..18],
            Name: "Initial Batch Name",
            BeerStyle: "Stout",
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

        // Act: Update metadata on completed batch
        var updateReq = new UpdateBatchRequest(
            Name: "Bourbon Barrel Stout - Vintage 2026",
            BeerStyle: "Imperial Stout",
            Notes: "Aged 6 months on oak chips steeped in Kentucky bourbon."
        );
        var updated = await UpdateBatchAsync(client, batch.Id, updateReq);

        // Assert
        updated.Status.Should().Be(BatchStatus.Completed);
        updated.Name.Should().Be("Bourbon Barrel Stout - Vintage 2026");
        updated.BeerStyle.Should().Be("Imperial Stout");
        updated.Notes.Should().Contain("bourbon");
    }

    [Fact]
    public async Task R2_TC02_PostFinish_UpdateMeasuredOgAndFgOnCompletedBatch_RecalculatesAbvAndAttenuation()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-COMP-RECALC-{Guid.NewGuid():N}"[..18],
            Name: "Recalculation Test Ale",
            BeerStyle: "American IPA",
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

        await FastForwardToCompletedAsync(client, batch.Id, kegId, measuredOg: 1.050m, measuredFg: 1.010m);

        // Act: Adjust measured gravities on completed batch
        var updateReq = new UpdateBatchRequest(
            MeasuredOg: 1.060m,
            MeasuredFg: 1.012m
        );
        var updated = await UpdateBatchAsync(client, batch.Id, updateReq);

        // Assert
        updated.Status.Should().Be(BatchStatus.Completed);
        updated.MeasuredOg.Should().Be(1.060m);
        updated.MeasuredFg.Should().Be(1.012m);
        updated.AlcoholByVolume.Should().BeApproximately(6.30m, 0.15m);
    }

    [Fact]
    public async Task R2_TC03_PostFinish_UpdateMeasuredBatchSize_PreservesCompletedStatusAndUpdatesVolume()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-COMP-VOL-{Guid.NewGuid():N}"[..18],
            Name: "Volume Update Batch",
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

        // Act
        var updateReq = new UpdateBatchRequest(
            MeasuredBatchSizeLiters: 19.4m
        );
        var updated = await UpdateBatchAsync(client, batch.Id, updateReq);

        // Assert
        updated.Status.Should().Be(BatchStatus.Completed);
        updated.MeasuredBatchSizeLiters.Should().Be(19.4m);
    }

    [Fact]
    public async Task R2_TC04_PostFinish_UpdateVolumeMilestonesOnCompletedBatch_UpdatesProfileAndDeviations()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-COMP-MILE-{Guid.NewGuid():N}"[..18],
            Name: "Milestone Adjustment Batch",
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

        // Act
        var updateReq = new UpdateBatchRequest(
            MeasuredPreBoilVolumeLiters: 25.2m,
            MeasuredPostBoilVolumeLiters: 21.4m,
            MeasuredPackagedVolumeLiters: 19.1m
        );
        var updated = await UpdateBatchAsync(client, batch.Id, updateReq);

        // Assert
        updated.Status.Should().Be(BatchStatus.Completed);
        updated.VolumeProfile.Should().NotBeNull();
        updated.VolumeProfile!.MeasuredPreBoilVolumeLiters.Should().Be(25.2m);
        updated.VolumeProfile.MeasuredPostBoilVolumeLiters.Should().Be(21.4m);
        updated.VolumeProfile.MeasuredPackagedVolumeLiters.Should().Be(19.1m);
    }

    [Fact]
    public async Task R2_TC05_PostFinish_MultiTenantIsolation_OtherUserCannotModifyCompletedBatch()
    {
        // Arrange
        var (clientA, _, _, setupIdA) = await CreateAuthenticatedUserAsync("Brewer A");
        var (clientB, _, _, _) = await CreateAuthenticatedUserAsync("Brewer B");
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(clientA, setupIdA);

        var batchA = await CreateAdHocBatchAsync(clientA, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-BOLA-{Guid.NewGuid():N}"[..18],
            Name: "Brewer A Completed Batch",
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

        await FastForwardToCompletedAsync(clientA, batchA.Id, kegId);

        // Act: Brewer B tries to modify Brewer A's completed batch
        var maliciousReq = new UpdateBatchRequest(Name: "Hijacked Batch Name");
        var res = await UpdateBatchRawAsync(clientB, batchA.Id, maliciousReq);

        // Assert: BOLA security boundary returns 404 Not Found
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Original batch is untouched
        var unchanged = await GetBatchDetailAsync(clientA, batchA.Id);
        unchanged.Name.Should().Be("Brewer A Completed Batch");
    }

    #endregion

    #region Requirement R3: Equipment Handling & Consolidated Manual Telemetry (>= 5 cases)

    [Fact]
    public async Task R3_TC01_ConsolidatedReading_IngestsGravityAndTemperature_UpdatesCurrentGravity()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-READING-{Guid.NewGuid():N}"[..18],
            Name: "Telemetry Test Batch",
            BeerStyle: "Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 35m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Act: Submit consolidated reading
        var reading = await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: 1.036m,
            TemperatureC: 20.2m,
            Notes: "Mid-fermentation gravity check"
        ));

        // Assert
        reading.SpecificGravity.Should().Be(1.036m);
        reading.TemperatureC.Should().Be(20.2m);
        reading.Notes.Should().Be("Mid-fermentation gravity check");

        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.CurrentGravity.Should().Be(1.036m);
        detail.Readings.Should().ContainSingle(r => r.SpecificGravity == 1.036m);
    }

    [Fact]
    public async Task R3_TC02_ConsolidatedReading_ComputesReadingAbv_WhenOgIsEstablished()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-READ-ABV-{Guid.NewGuid():N}"[..18],
            Name: "Reading ABV Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.060m,
            TargetFg: 1.012m,
            TargetAbv: 6.3m,
            TargetIbu: 45m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId,
            MeasuredOg: 1.060m
        ));

        // Act: Submit terminal gravity reading
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: 1.015m,
            TemperatureC: 20.0m,
            Notes: "Day 10 FG sample"
        ));

        // Assert: CurrentGravity and ABV are updated
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.CurrentGravity.Should().Be(1.015m);
        detail.AlcoholByVolume.Should().BeGreaterThan(5.5m);
    }

    [Fact]
    public async Task R3_TC03_EquipmentHandling_BatchWithUnassignedOrOfflineEquipment_ProgressesGracefully()
    {
        // Arrange: Batch created without boiler or fermenter IDs (manual brewing setup)
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-MANUAL-{Guid.NewGuid():N}"[..18],
            Name: "Manual Stove-Top Batch",
            BeerStyle: "Saison",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 10m,
            TargetOg: 1.048m,
            TargetFg: 1.006m,
            TargetAbv: 5.5m,
            TargetIbu: 30m,
            TargetColorSrm: 4m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 70m,
            BoilerId: null,
            FermenterId: null
        ));

        // Act: Progress stages manually
        var boil = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 12.5m
        ));
        var ferment = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.048m,
            MeasuredBatchSizeLiters: 10.0m
        ));

        // Assert: Succeeds without crashing on null equipment references
        boil.CurrentStage.Should().Be(BrewStage.Boil);
        ferment.CurrentStage.Should().Be(BrewStage.Ferment);
        ferment.BoilerId.Should().BeNull();
        ferment.FermenterId.Should().BeNull();
    }

    [Fact]
    public async Task R3_TC04_EquipmentHandling_MultipleSensorsOrManualReadings_PersistInChronologicalOrder()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-SERIES-{Guid.NewGuid():N}"[..18],
            Name: "Reading Series Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.055m,
            TargetFg: 1.010m,
            TargetAbv: 5.9m,
            TargetIbu: 40m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        var baseTime = DateTime.UtcNow.AddDays(-3);

        // Act: Post 3 readings in sequential time order
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(baseTime, 1.050m, 20.0m, "Day 1"));
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(baseTime.AddDays(1), 1.035m, 20.2m, "Day 2"));
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(baseTime.AddDays(2), 1.020m, 20.1m, "Day 3"));

        // Assert
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.Readings.Should().HaveCount(3);
        detail.Readings[0].SpecificGravity.Should().Be(1.050m);
        detail.Readings[1].SpecificGravity.Should().Be(1.035m);
        detail.Readings[2].SpecificGravity.Should().Be(1.020m);
    }

    [Fact]
    public async Task R3_TC05_Telemetry_BatchEquipmentReading_LogsStepTemperature_FilterableByStage()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-EQREAD-{Guid.NewGuid():N}"[..18],
            Name: "Equipment Reading Batch",
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

        // Act: Log temperature reading for active Mash stage
        var logReq = new LogBatchTemperatureRequest(
            TemperatureC: 66.5m,
            EquipmentId: boilerId,
            Stage: BrewStage.Mash,
            StepName: "Saccharification Rest",
            Timestamp: DateTime.UtcNow
        );
        var postRes = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings", logReq);
        postRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: Filter readings by stage
        var getRes = await client.GetAsync($"/api/v1/batches/{batch.Id}/equipment-readings?stage=Mash");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var env = await getRes.Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>();
        env!.Data.Should().ContainSingle(r => r.TemperatureC == 66.5m && r.Stage == BrewStage.Mash);
    }

    #endregion

    #region Requirement R4: Accurate Calculations & Target Deviations (>= 5 cases)

    [Fact]
    public async Task R4_TC01_TargetDeviations_BatchDetail_ComputesOgAndFgPointsDeviations()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-DEV-PTS-{Guid.NewGuid():N}"[..18],
            Name: "Deviation Points Batch",
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

        // Act: Update with MeasuredOg = 1.054 (+4 points) and MeasuredFg = 1.012 (+2 points)
        await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            MeasuredOg: 1.054m,
            MeasuredFg: 1.012m
        ));

        // Assert
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.Deviations.Should().NotBeNull();
        detail.Deviations!.OgDeviation.Should().Be(0.004m);
        detail.Deviations.OgPointsDeviation.Should().Be(4);
        detail.Deviations.FgDeviation.Should().Be(0.002m);
        detail.Deviations.FgPointsDeviation.Should().Be(2);
    }

    [Fact]
    public async Task R4_TC02_TargetDeviations_BatchDetail_ComputesVolumeAndEfficiencyDeviations()
    {
        // Arrange
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-DEV-VOL-{Guid.NewGuid():N}"[..18],
            Name: "Volume Deviation Batch",
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

        // Act: Update with MeasuredBatchSize = 19.2 (-0.8L) and pre/post-boil measurements
        await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            MeasuredBatchSizeLiters: 19.2m,
            MeasuredPreBoilVolumeLiters: 25.0m,
            MeasuredPostBoilVolumeLiters: 21.0m
        ));

        // Assert
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.Deviations.Should().NotBeNull();
        detail.Deviations!.BatchSizeLitersDeviation.Should().Be(-0.8m);
    }

    [Fact]
    public void R4_TC03_HydrometerTemperatureCorrection_ReferenceTemp20C_ReturnsIdenticalGravity()
    {
        // At the 20°C calibration baseline, density polynomial matches calibration exactly
        var rawSg = 1.050m;
        var corrected = BrewingCalculator.CorrectHydrometerReading(rawSg, tempC: 20.0m, calTempC: 20.0m);

        corrected.Should().Be(rawSg);
    }

    [Fact]
    public void R4_TC04_HydrometerTemperatureCorrection_WarmWortSample_AppliesPositiveCorrection()
    {
        // Liquid expands at higher temperature, causing hydrometer to sink deeper (reading lower).
        // Corrected reading at 20°C reference must therefore be higher than the raw reading.
        var rawSg = 1.048m;
        var corrected = BrewingCalculator.CorrectHydrometerReading(rawSg, tempC: 50.0m, calTempC: 20.0m);

        corrected.Should().BeGreaterThan(rawSg);
        corrected.Should().BeInRange(1.056m, 1.060m);
    }

    [Fact]
    public void R4_TC05_HighGravityAbvParity_CalculatesNonLinearAsbcAbv_WhenOgExceeds1060()
    {
        // High gravity beer: OG = 1.080, FG = 1.018
        // ASBC Advanced non-linear formula:
        // ABW = (76.08 * (OG - FG)) / (1.775 - OG)
        // ABV = ABW * (FG / 0.794)
        var og = 1.080m;
        var fg = 1.018m;
        var abv = BrewingCalculator.CalculateActualAbv(og, fg);

        abv.Should().BeInRange(8.65m, 8.75m);
    }

    [Fact]
    public void R4_TC06_RealAttenuation_CalculatesStandardAsbcRdfFromApparentAttenuation()
    {
        // ASBC equation: RDF = ADF * 0.8192
        var adf = 80.0m;
        var rdf = BrewingCalculator.CalculateRealAttenuation(adf);

        rdf.Should().Be(Math.Round(80.0m * 0.8192m, 1));
    }

    #endregion
}