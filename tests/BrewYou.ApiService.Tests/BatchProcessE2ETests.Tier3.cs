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
/// Tier 3: Cross-Feature Combinations E2E Tests (>= 5 pairwise interaction tests).
/// Tests complex interactions between:
/// - R1 (Lifecycle Progression) × R2 (Post-Finish Batch Record Editing)
/// - R2 (Post-Finish Batch Record Editing) × R4 (Target Deviations Computation)
/// - R3 (Consolidated Manual Telemetry) × R4 (Hydrometer Calibration & High-Gravity ASBC ABV)
/// - R1 (Lifecycle Progression) × R3 (Equipment Handling & Offline Hardware)
/// - R1 (Lifecycle Volume Profiles) × R4 (Thermal Wort Shrinkage & Deviations)
/// - R2 × R3 × R4 (Multi-feature interaction on completed batch)
/// </summary>
public class BatchProcessE2ETests_Tier3_CrossFeatureCombinations : BatchProcessE2ETestBase
{
    public BatchProcessE2ETests_Tier3_CrossFeatureCombinations(WebApplicationFactory<Program> factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task TC_T3_Pairwise_R1_R2_LifecycleProgressionToCompleted_ThenPostFinishNotesAndMetricsUpdated()
    {
        // Pairwise R1 x R2: Progress through full lifecycle to Completed, then perform post-finish update
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var recipe = await CreateStandardRecipeAsync(client, "West Coast IPA");

        // R1: Complete lifecycle progression
        var batch = await CreateRecipeBatchAsync(client, recipe.Id, batchSize: 20m, boilerId: boilerId, fermenterId: fermenterId);
        var completed = await FastForwardToCompletedAsync(client, batch.Id, kegId, measuredOg: 1.062m, measuredFg: 1.012m, measuredPackagedVolume: 19.1m);

        completed.CurrentStage.Should().Be(BrewStage.Package);
        completed.Status.Should().Be(BatchStatus.Completed);
        completed.CompletedAt.Should().NotBeNull();

        // R2: Post-finish editing on Completed batch
        var postFinishReq = new UpdateBatchRequest(
            Name: "West Coast IPA - Blue Ribbon Batch",
            BeerStyle: "American IPA",
            Notes: "Judged 44/50 at National Homebrew Competition. Crisp bitterness, bright citrus.",
            MeasuredPackagedVolumeLiters: 19.3m
        );
        var updated = await UpdateBatchAsync(client, batch.Id, postFinishReq);

        // Assert: Changes persisted, batch remains Completed, CompletedAt timestamp preserved
        updated.Status.Should().Be(BatchStatus.Completed);
        updated.Name.Should().Be("West Coast IPA - Blue Ribbon Batch");
        updated.Notes.Should().Contain("National Homebrew Competition");
        updated.VolumeProfile!.MeasuredPackagedVolumeLiters.Should().Be(19.3m);
        updated.CompletedAt.Should().Be(completed.CompletedAt);
    }

    [Fact]
    public async Task TC_T3_Pairwise_R2_R4_PostFinishEditing_DynamicallyRecalculatesTargetDeviations()
    {
        // Pairwise R2 x R4: Post-finish updates to measured values dynamically recompute BatchDeviationsDto
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);

        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-PAIR-R2R4-{Guid.NewGuid():N}"[..18],
            Name: "Dynamic Deviation Batch",
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

        await FastForwardToCompletedAsync(client, batch.Id, kegId, measuredOg: 1.050m, measuredFg: 1.010m, measuredPackagedVolume: 20.0m);

        // Initial state: 0 points deviation
        var initialDetail = await GetBatchDetailAsync(client, batch.Id);
        initialDetail.Deviations.Should().NotBeNull();
        initialDetail.Deviations!.OgPointsDeviation.Should().Be(0);

        // Act: Update post-finish measured values: OG +6 points, FG +2 points, Volume -1.0L
        var updated = await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            MeasuredOg: 1.056m,
            MeasuredFg: 1.012m,
            MeasuredBatchSizeLiters: 19.0m
        ));

        // Assert: Deviations recalculated immediately in response payload
        updated.Deviations.Should().NotBeNull();
        updated.Deviations!.OgDeviation.Should().Be(0.006m);
        updated.Deviations.OgPointsDeviation.Should().Be(6);
        updated.Deviations.FgDeviation.Should().Be(0.002m);
        updated.Deviations.FgPointsDeviation.Should().Be(2);
        updated.Deviations.BatchSizeLitersDeviation.Should().Be(-1.0m);
        updated.AlcoholByVolume.Should().BeApproximately(5.77m, 0.15m);
    }

    [Fact]
    public async Task TC_T3_Pairwise_R3_R4_ConsolidatedReading_WithWarmHydrometerTemperatureCorrection()
    {
        // Pairwise R3 x R4: Consolidated manual telemetry ingestion combined with hydrometer calibration
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);

        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-PAIR-R3R4-{Guid.NewGuid():N}"[..18],
            Name: "Calibrated Telemetry Batch",
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

        // A hydrometer sample taken warm at 45.0°C gives raw reading 1.048m
        var rawSg = 1.048m;
        var sampleTempC = 45.0m;
        var correctedSg = BrewingCalculator.CorrectHydrometerReading(rawSg, sampleTempC, 20.0m);

        correctedSg.Should().BeGreaterThan(rawSg);
        correctedSg.Should().BeInRange(1.055m, 1.058m);

        // Act: Ingest the temperature-corrected reading into batch telemetry
        var reading = await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: correctedSg,
            TemperatureC: sampleTempC,
            Notes: "Hydrometer sample taken at 45C, corrected to 20C baseline"
        ));

        // Assert: Batch reflects the calibrated gravity reading
        reading.SpecificGravity.Should().Be(correctedSg);
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.CurrentGravity.Should().Be(correctedSg);
    }

    [Fact]
    public async Task TC_T3_Pairwise_R1_R3_OfflineEquipment_ProgressesGracefullyWithManualReadings()
    {
        // Pairwise R1 x R3: Complete lifecycle progression with offline / unassigned equipment
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-PAIR-R1R3-{Guid.NewGuid():N}"[..18],
            Name: "Offline Hardware Batch",
            BeerStyle: "Saison",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 15m,
            TargetOg: 1.052m,
            TargetFg: 1.008m,
            TargetAbv: 5.8m,
            TargetIbu: 30m,
            TargetColorSrm: 4.5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: null,
            FermenterId: null
        ));

        // Act 1: Mash stage manual reading
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow, 1.040m, 65m, "Mash reading"));

        // Act 2: Advance to Boil
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Boil, MeasuredPreBoilVolumeLiters: 18m));

        // Act 3: Advance to Ferment
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Ferment, MeasuredOg: 1.052m, MeasuredBatchSizeLiters: 15m));

        // Act 4: Ferment stage manual reading
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow, 1.015m, 20m, "Mid-ferment reading"));

        // Act 5: Advance to Condition & Package
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Condition));
        var completed = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Package, MeasuredFg: 1.008m, MeasuredPackagedVolumeLiters: 14.5m));

        // Assert: Successful completion with all manual readings preserved and zero hardware errors
        completed.Status.Should().Be(BatchStatus.Completed);
        completed.Readings.Should().HaveCount(2);
        completed.CurrentGravity.Should().Be(1.008m);
    }

    [Fact]
    public async Task TC_T3_Pairwise_R1_R4_VolumeProfileProgression_WithShrinkageAndDeviations()
    {
        // Pairwise R1 x R4: Volume profile lifecycle tracking thermal shrinkage and computing volume deviations
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, _, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);

        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-PAIR-R1R4-{Guid.NewGuid():N}"[..18],
            Name: "Volume Shrinkage Batch",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
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

        // Hot post-boil volume: 22.0L. Uniform 4% cooling contraction yields 22.0 * 0.96 = 21.12L cold
        var hotPostBoilVolume = 22.0m;
        var expectedColdVolume = Math.Round(hotPostBoilVolume * 0.96m, 2);

        // Advance through Boil and Ferment
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 25.5m
        ));

        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.055m,
            MeasuredPostBoilVolumeLiters: hotPostBoilVolume,
            MeasuredBatchSizeLiters: expectedColdVolume
        ));

        // Assert: Deviations track measured vs target volumes
        var detail = await GetBatchDetailAsync(client, batch.Id);
        detail.VolumeProfile.Should().NotBeNull();
        detail.VolumeProfile!.MeasuredPreBoilVolumeLiters.Should().Be(25.5m);
        detail.VolumeProfile.MeasuredPostBoilVolumeLiters.Should().Be(hotPostBoilVolume);
        detail.MeasuredBatchSizeLiters.Should().Be(expectedColdVolume);
        detail.Deviations.Should().NotBeNull();
        detail.Deviations!.BatchSizeLitersDeviation.Should().Be(Math.Round(expectedColdVolume - 20.0m, 2));
    }

    [Fact]
    public async Task TC_T3_Multi_R2_R3_R4_PostFinish_TelemetryLogging_WithDeviationRecalculation()
    {
        // Multi-feature R2 x R3 x R4: Completed batch receives terminal reading and post-finish adjustment
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync();
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);

        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-MULTI-R234-{Guid.NewGuid():N}"[..18],
            Name: "Multi-Feature Completed Batch",
            BeerStyle: "Imperial Stout",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.080m,
            TargetFg: 1.018m,
            TargetAbv: 8.5m,
            TargetIbu: 60m,
            TargetColorSrm: 30m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 72m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        await FastForwardToCompletedAsync(client, batch.Id, kegId, measuredOg: 1.080m, measuredFg: 1.020m, measuredPackagedVolume: 19.5m);

        // Add retrospective reading to completed batch
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: 1.018m,
            TemperatureC: 20.0m,
            Notes: "Final gravity confirmation sample"
        ));

        // Perform post-finish adjustment: update MeasuredFg and tasting notes
        var updated = await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            MeasuredFg: 1.018m,
            Notes: "Tasting profile: rich dark chocolate, roasted barley, warm alcohol finish."
        ));

        // Assert: High gravity ASBC calculation applied, deviations zeroed out on target match
        updated.Status.Should().Be(BatchStatus.Completed);
        updated.MeasuredFg.Should().Be(1.018m);
        updated.AlcoholByVolume.Should().BeApproximately(8.70m, 0.15m);
        updated.Deviations.Should().NotBeNull();
        updated.Deviations!.OgPointsDeviation.Should().Be(0);
        updated.Deviations.FgPointsDeviation.Should().Be(0);
    }
}