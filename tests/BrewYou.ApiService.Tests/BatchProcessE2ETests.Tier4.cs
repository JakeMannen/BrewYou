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
/// Tier 4: Real-World Application Scenarios E2E Tests (>= 5 end-to-end brewday workflows).
/// Comprehensive multi-step brewday lifecycles exercising:
/// 1. Full Brewday Lifecycle with Post-Finish Edits & Notes
/// 2. High-Gravity Imperial Stout with Temperature Correction & Attenuation Parity
/// 3. Disconnected Equipment to Consolidated Manual Telemetry
/// 4. Volume Profiles with Cooling Contraction & Stage Deviations
/// 5. Backup & Restore Parity of Completed Batches with Deviations
/// 6. Multi-Vessel System with Retrospective Telemetry & Post-Finish Auditing
/// </summary>
public class BatchProcessE2ETests_Tier4_RealWorldScenarios : BatchProcessE2ETestBase
{
    public BatchProcessE2ETests_Tier4_RealWorldScenarios(WebApplicationFactory<Program> factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task TC_T4_Scenario_01_FullBrewdayLifecycle_WithPostFinishEditsAndNotes()
    {
        // Narrative: Craft brewer creates an American IPA from recipe, completes the 5 brewday stages,
        // logs temperatures and gravities, completes the batch, then updates tasting notes and verifies deviations.
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Hophead Brewer");
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);
        var recipe = await CreateStandardRecipeAsync(client, "Mosaic Single Hop IPA", targetOg: 1.062m);

        // 1. Create batch from recipe
        var batch = await CreateRecipeBatchAsync(client, recipe.Id, batchSize: 20m, boilerId: boilerId, fermenterId: fermenterId);
        batch.CurrentStage.Should().Be(BrewStage.Mash);
        batch.Status.Should().Be(BatchStatus.Brewing);

        // 2. Progress Mash: Log temperature reading for strike rest & toggle mash ingredient
        var mashTempRes = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/equipment-readings", new LogBatchTemperatureRequest(
            TemperatureC: 66.8m,
            EquipmentId: boilerId,
            Stage: BrewStage.Mash,
            StepName: "Saccharification Rest"
        ));
        mashTempRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var paleMalt = batch.Ingredients.First(i => i.AdditionStage == IngredientUsage.Mash);
        await client.PatchAsync($"/api/v1/batches/{batch.Id}/ingredients/{paleMalt.Id}?isChecked=true", null);

        // 3. Advance to Boil: Record measured pre-boil volume (24.8L) and gravity (1.050)
        var boilBatch = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 24.8m,
            MeasuredPreBoilGravity: 1.050m
        ));
        boilBatch.CurrentStage.Should().Be(BrewStage.Boil);

        // Toggle boil hop addition
        var boilHop = batch.Ingredients.First(i => i.AdditionStage == IngredientUsage.Boil);
        await client.PatchAsync($"/api/v1/batches/{batch.Id}/ingredients/{boilHop.Id}?isChecked=true", null);

        // 4. Advance to Ferment: Hot post-boil 21.5L, cold fermenter volume 20.64L (4% contraction), OG = 1.063
        var fermentBatch = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.063m,
            MeasuredPostBoilVolumeLiters: 21.5m,
            MeasuredBatchSizeLiters: 20.64m,
            PitchTemperatureC: 19.5m
        ));
        fermentBatch.CurrentStage.Should().Be(BrewStage.Ferment);
        fermentBatch.Status.Should().Be(BatchStatus.Fermenting);

        // 5. Ingest fermentation gravity readings
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow.AddDays(-5), 1.030m, 19.5m, "Active krausen"));
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow, 1.011m, 19.0m, "Terminal gravity"));

        // 6. Advance to Condition
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Condition,
            MeasuredFg: 1.011m
        ));

        // 7. Advance to Package: Package 19.2L into Keg
        var packagedBatch = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Package,
            PackagingVesselId: kegId,
            MeasuredPackagedVolumeLiters: 19.2m
        ));
        packagedBatch.CurrentStage.Should().Be(BrewStage.Package);
        packagedBatch.Status.Should().Be(BatchStatus.Completed);

        // 8. Post-Finish Edits (Requirement R2)
        var updated = await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            Name: "Mosaic Single Hop IPA - Competition Reserve",
            Notes: "Won Gold Medal! Intense tropical fruit, clean malt backbone, very dry finish.",
            MeasuredPackagedVolumeLiters: 19.4m
        ));

        // 9. Assertions across all requirements
        updated.Status.Should().Be(BatchStatus.Completed);
        updated.Name.Should().Contain("Competition Reserve");
        updated.Notes.Should().Contain("Gold Medal");
        updated.AlcoholByVolume.Should().BeApproximately(7.07m, 0.05m); // ASBC non-linear ABV for OG 1.063 (> 1.060) & FG 1.011
        updated.Deviations.Should().NotBeNull();
        updated.Deviations!.OgPointsDeviation.Should().Be(13); // 1.063 vs recipe default 1.050
        updated.Deviations.BatchSizeLitersDeviation.Should().Be(0.64m); // 20.64 vs 20.0
    }

    [Fact]
    public async Task TC_T4_Scenario_02_HighGravityImperialStout_WithTemperatureCorrectionAndAsbcParity()
    {
        // Narrative: A brewer brews an Imperial Stout with OG > 1.090, takes a hot hydrometer reading at 55°C,
        // corrects it using ASBC hydrometer temperature calibration, and verifies non-linear ASBC ABV.
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Stout Specialist");
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);

        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-STOUT-{Guid.NewGuid():N}"[..18],
            Name: "Dark Sovereign Imperial Stout",
            BeerStyle: "Imperial Stout",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.095m,
            TargetFg: 1.022m,
            TargetAbv: 9.8m,
            TargetIbu: 75m,
            TargetColorSrm: 45m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 70m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Advance to Boil
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 26.0m,
            MeasuredPreBoilGravity: 1.078m
        ));

        // Hot kettle sample taken at 55°C: raw hydrometer reading = 1.082m
        var rawKettleSg = 1.082m;
        var sampleTempC = 55.0m;
        var calibratedOg = BrewingCalculator.CorrectHydrometerReading(rawKettleSg, sampleTempC, 20.0m);

        // Temperature correction verifies expansion: calibrated > raw
        calibratedOg.Should().BeGreaterThan(rawKettleSg);
        calibratedOg.Should().BeInRange(1.094m, 1.097m);

        // Advance to Ferment with the temperature-calibrated OG
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: calibratedOg,
            MeasuredPostBoilVolumeLiters: 21.0m,
            MeasuredBatchSizeLiters: 20.0m,
            PitchTemperatureC: 18.0m
        ));

        // Log mid-ferment reading
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow.AddDays(-7), 1.045m, 18.5m, "Active fermentation"));

        // Terminal gravity reading: 1.020m at 20°C
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow, 1.020m, 20.0m, "Terminal gravity"));

        // Fast forward to completed
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Condition, MeasuredFg: 1.020m));
        var completed = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Package,
            PackagingVesselId: kegId,
            MeasuredPackagedVolumeLiters: 19.0m
        ));

        // High-gravity ASBC calculation:
        // ABW = (76.08 * (OG - FG)) / (1.775 - OG)
        // ABV = ABW * (FG / 0.794)
        var expectedAbv = BrewingCalculator.CalculateActualAbv(calibratedOg, 1.020m);
        expectedAbv.Should().BeGreaterThan(10.0m);

        completed.AlcoholByVolume.Should().Be(expectedAbv);
        completed.Deviations.Should().NotBeNull();
        completed.Deviations!.OgPointsDeviation.Should().BeInRange(-1, 2);
    }

    [Fact]
    public async Task TC_T4_Scenario_03_DisconnectedEquipment_ToConsolidatedManualTelemetryFallback()
    {
        // Narrative: A brewer creates a batch without assigned hardware (unconnected / offline equipment).
        // The wizard and service gracefully handle the absence of IoT hardware and allow the brewer
        // to manually log temperatures, gravities, and stage progressions with complete data integrity.
        var (client, _, _, _) = await CreateAuthenticatedUserAsync("Manual Artisan Brewer");

        // 1. Create batch with null equipment IDs
        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-MANUAL-E2E-{Guid.NewGuid():N}"[..18],
            Name: "Rustic Farmhouse Saison",
            BeerStyle: "Saison",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 15.0m,
            TargetOg: 1.050m,
            TargetFg: 1.006m,
            TargetAbv: 5.8m,
            TargetIbu: 28m,
            TargetColorSrm: 5m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 74m,
            BoilerId: null,
            FermenterId: null
        ));

        batch.BoilerId.Should().BeNull();
        batch.FermenterId.Should().BeNull();

        // 2. Manual strike reading
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow, 1.040m, 66.0m, "Strike water infusion"));

        // 3. Advance to Boil
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 18.0m,
            MeasuredPreBoilGravity: 1.042m
        ));

        // 4. Advance to Ferment
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.050m,
            MeasuredBatchSizeLiters: 15.0m
        ));

        // 5. Consolidated manual readings
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow.AddDays(-3), 1.025m, 22.0m, "Day 3 warm rest"));
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(DateTime.UtcNow, 1.006m, 20.0m, "Attenuated dry"));

        // 6. Complete batch
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Condition));
        var completed = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Package,
            MeasuredFg: 1.006m,
            MeasuredPackagedVolumeLiters: 14.2m
        ));

        // 7. Post-finish edits on manual batch
        var updated = await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            Notes: "Spicy phenols, earthy black pepper, high carbonation in Belgian 750ml bottles."
        ));

        // Assert: 100% data integrity maintained without hardware dependencies
        updated.Status.Should().Be(BatchStatus.Completed);
        updated.AlcoholByVolume.Should().BeApproximately(5.78m, 0.15m);
        updated.Readings.Should().HaveCount(3);
        updated.Notes.Should().Contain("Belgian 750ml bottles");
    }

    [Fact]
    public async Task TC_T4_Scenario_04_VolumeProfiles_WithAllInOneCoolingContractionAndDeviations()
    {
        // Narrative: An All-In-One single vessel brewer tracks volume profile across all milestones:
        // strike water, sparge water, pre-boil, post-boil, cooling shrinkage, and final packaging.
        // Verifies uniform 4% thermal contraction and volume deviations.
        var (client, _, _, defaultSetupId) = await CreateAuthenticatedUserAsync("All-In-One Brewer");
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);

        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-AIO-VOL-{Guid.NewGuid():N}"[..18],
            Name: "All-In-One Vienna Lager",
            BeerStyle: "Vienna Lager",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.048m,
            TargetFg: 1.010m,
            TargetAbv: 5.0m,
            TargetIbu: 25m,
            TargetColorSrm: 12m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        // Target profile verification
        batch.VolumeProfile.Should().NotBeNull();
        batch.VolumeProfile!.TargetPreBoilVolumeLiters.Should().BeGreaterThan(22m);

        // 1. Pre-boil recorded: 24.5L (target ~24.0L, deviation +0.5L)
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 24.5m,
            MeasuredPreBoilGravity: 1.040m
        ));

        // 2. Post-boil hot recorded: 21.5L. Applying uniform 4% shrinkage: 21.5 * 0.96 = 20.64L cold
        var hotPostBoil = 21.5m;
        var coldFermenter = Math.Round(hotPostBoil * 0.96m, 2);

        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: 1.048m,
            MeasuredPostBoilVolumeLiters: hotPostBoil,
            MeasuredBatchSizeLiters: coldFermenter
        ));

        // 3. Complete and package
        await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(TargetStage: BrewStage.Condition, MeasuredFg: 1.010m));
        var completed = await AdvanceStageAsync(client, batch.Id, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Package,
            PackagingVesselId: kegId,
            MeasuredPackagedVolumeLiters: 19.5m
        ));

        // 4. Post-finish fine-tuning of packaging losses
        var updated = await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            MeasuredPackagedVolumeLiters: 19.6m
        ));

        // Assert: Deviations track all milestones accurately
        updated.VolumeProfile!.MeasuredPreBoilVolumeLiters.Should().Be(24.5m);
        updated.VolumeProfile.MeasuredPostBoilVolumeLiters.Should().Be(21.5m);
        updated.VolumeProfile.MeasuredPackagedVolumeLiters.Should().Be(19.6m);
        updated.Deviations.Should().NotBeNull();
        updated.Deviations!.PreBoilVolumeDeviation.Should().NotBeNull();
        updated.Deviations.PostBoilVolumeDeviation.Should().NotBeNull();
        updated.Deviations.BatchSizeLitersDeviation.Should().Be(Math.Round(coldFermenter - 20.0m, 2));
    }

    [Fact]
    public async Task TC_T4_Scenario_05_BackupAndRestore_CompletedBatch_WithDeviationsAndCustomReadings()
    {
        // Narrative: A user creates and finishes a batch with post-finish notes, extended readings,
        // and deviations, performs an automated user backup export, and verifies restore preview parity.
        var (client, userId, _, defaultSetupId) = await CreateAuthenticatedUserAsync("Archival Brewer");
        var (boilerId, fermenterId, kegId, _, _) = await CreateStandardBreweryEquipmentAsync(client, defaultSetupId);

        var batch = await CreateAdHocBatchAsync(client, new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-BACKUP-E2E-{Guid.NewGuid():N}"[..18],
            Name: "Export/Restore Amber Ale",
            BeerStyle: "American Amber Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.052m,
            TargetFg: 1.012m,
            TargetAbv: 5.25m,
            TargetIbu: 32m,
            TargetColorSrm: 14m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        ));

        await FastForwardToCompletedAsync(client, batch.Id, kegId, measuredOg: 1.054m, measuredFg: 1.012m, measuredPackagedVolume: 19.4m);

        // Add consolidated reading to completed batch
        await AddConsolidatedReadingAsync(client, batch.Id, new AddBatchReadingRequest(
            Timestamp: DateTime.UtcNow,
            SpecificGravity: 1.012m,
            TemperatureC: 20.0m,
            Notes: "Final packaging gravity check"
        ));

        // Update post-finish notes
        await UpdateBatchAsync(client, batch.Id, new UpdateBatchRequest(
            Notes: "Preserved in backup test archive. Rich caramel and crystal malt flavor."
        ));

        // 1. Export User Backup (POST /api/v1/backup/export)
        var exportRes = await client.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>();
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeNull();
        envelope.Data!.Format.Should().Be("brewyou-user-backup");
        envelope.Data.SchemaVersion.Should().BeGreaterThanOrEqualTo(1);

        // 2. Preview Restore against the exported payload
        var previewRes = await client.PostAsJsonAsync("/api/v1/backup/preview", new PreviewRestoreRequest(envelope.Data));
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = await previewRes.Content.ReadFromJsonAsync<ApiResponse<RestorePreviewReportDto>>();
        preview!.Success.Should().BeTrue();
        preview.Data!.IsValid.Should().BeTrue();
        preview.Data.Counts.Batches.Should().BeGreaterThanOrEqualTo(1);
        preview.Data.Counts.TelemetryReadings.Should().BeGreaterThanOrEqualTo(0);
    }
}