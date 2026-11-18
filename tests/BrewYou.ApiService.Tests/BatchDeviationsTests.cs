using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrewYou.ApiService.Tests;

public class BatchDeviationsTests
{
    private readonly BrewYouDbContext _db;
    private readonly BatchService _batchService;

    public BatchDeviationsTests()
    {
        var options = new DbContextOptionsBuilder<BrewYouDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new BrewYouDbContext(options);
        var broadcast = new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance);
        _batchService = new BatchService(_db, broadcast, NullLogger<BatchService>.Instance);
    }

    [Fact]
    public async Task GetBatchByIdAsync_WithMeasuredAndTargetMetrics_ComputesExpectedDeviations()
    {
        // Arrange
        var userId = "test_brewer";
        var batchId = Guid.NewGuid();

        var batch = new Batch
        {
            Id = batchId,
            UserId = userId,
            BatchCode = "DEV-01",
            Name = "Deviations Test IPA",
            BeerStyle = "American IPA",
            Status = BatchStatus.Fermenting,
            CurrentStage = BrewStage.Ferment,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            TargetOg = 1.050m,
            TargetFg = 1.010m,
            TargetAbv = 5.25m,
            TargetBatchSizeLiters = 20.0m,
            EfficiencyPercent = 72.0m,
            MeasuredOg = 1.055m,
            MeasuredFg = 1.012m,
            AlcoholByVolume = 5.60m,
            MeasuredBatchSizeLiters = 19.5m,
            BrewhouseEfficiency = 76.5m,
            VolumeProfile = new BatchVolumeProfile
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                TargetPreBoilVolumeLiters = 27.0m,
                MeasuredPreBoilVolumeLiters = 28.2m,
                TargetPostBoilVolumeLiters = 23.5m,
                MeasuredPostBoilVolumeLiters = 23.0m,
                TargetPackagedVolumeLiters = 19.0m,
                MeasuredPackagedVolumeLiters = 18.5m,
                CoolingShrinkagePercent = 4.0m
            }
        };

        _db.Batches.Add(batch);
        await _db.SaveChangesAsync();

        // Act
        var (result, detail) = await _batchService.GetBatchByIdAsync(batchId, userId);

        // Assert
        result.Should().Be(BatchAccessResult.Success);
        detail.Should().NotBeNull();
        detail!.Deviations.Should().NotBeNull();

        var dev = detail.Deviations!;
        // OG: 1.055 - 1.050 = +0.0050, points = +5
        dev.OgDeviation.Should().Be(0.0050m);
        dev.OgPointsDeviation.Should().Be(5);

        // FG: 1.012 - 1.010 = +0.0020, points = +2
        dev.FgDeviation.Should().Be(0.0020m);
        dev.FgPointsDeviation.Should().Be(2);

        // ABV: 5.60 - 5.25 = +0.35
        dev.AbvDeviation.Should().Be(0.35m);

        // Batch size: 19.5 - 20.0 = -0.50
        dev.BatchSizeLitersDeviation.Should().Be(-0.50m);

        // Volume Profile deviations
        // Pre-boil: 28.2 - 27.0 = +1.20
        dev.PreBoilVolumeDeviation.Should().Be(1.20m);

        // Post-boil: 23.0 - 23.5 = -0.50
        dev.PostBoilVolumeDeviation.Should().Be(-0.50m);

        // Packaged: 18.5 - 19.0 = -0.50
        dev.PackagedVolumeDeviation.Should().Be(-0.50m);

        // Efficiency: 76.5 - 72.0 = +4.50
        dev.BrewhouseEfficiencyDeviation.Should().Be(4.50m);
    }

    [Fact]
    public async Task GetBatchByIdAsync_WithNoMeasuredMetrics_ReturnsNullDeviations()
    {
        // Arrange
        var userId = "test_brewer";
        var batchId = Guid.NewGuid();

        var batch = new Batch
        {
            Id = batchId,
            UserId = userId,
            BatchCode = "DEV-02",
            Name = "Unstarted Batch",
            BeerStyle = "Pale Ale",
            Status = BatchStatus.Planned,
            CurrentStage = BrewStage.Mash,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            TargetOg = 1.052m,
            TargetFg = 1.011m,
            TargetAbv = 5.38m,
            TargetBatchSizeLiters = 20.0m,
            EfficiencyPercent = 75.0m
        };

        _db.Batches.Add(batch);
        await _db.SaveChangesAsync();

        // Act
        var (result, detail) = await _batchService.GetBatchByIdAsync(batchId, userId);

        // Assert
        result.Should().Be(BatchAccessResult.Success);
        detail.Should().NotBeNull();
        detail!.Deviations.Should().NotBeNull();

        var dev = detail.Deviations!;
        dev.OgDeviation.Should().BeNull();
        dev.OgPointsDeviation.Should().BeNull();
        dev.FgDeviation.Should().BeNull();
        dev.FgPointsDeviation.Should().BeNull();
        dev.AbvDeviation.Should().BeNull();
        dev.BatchSizeLitersDeviation.Should().BeNull();
        dev.PreBoilVolumeDeviation.Should().BeNull();
        dev.PostBoilVolumeDeviation.Should().BeNull();
        dev.PackagedVolumeDeviation.Should().BeNull();
        dev.BrewhouseEfficiencyDeviation.Should().BeNull();
    }

    [Fact]
    public async Task CreateBatchAsync_WithAllInOneBoiler_DefaultsCoolingShrinkageToFourPercent()
    {
        // Arrange
        var userId = "aio_brewer";
        var setup = new BrewerySetup
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "AIO Setup"
        };
        _db.BrewerySetups.Add(setup);

        var aioBoiler = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BrewerySetup = setup,
            Name = "Grainfather G30",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            BoilOffRatePerHour = 3.0m,
            TrubLossLiters = 1.5m,
            MashTunDeadSpaceLiters = 2.5m
        };
        _db.Equipment.Add(aioBoiler);
        await _db.SaveChangesAsync();

        var request = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "AIO-01",
            Name: "All In One Brew",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20.0m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 35.0m,
            TargetColorSrm: 6.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75.0m,
            BoilerId: aioBoiler.Id,
            FermenterId: null
        );

        // Act
        var (result, created, errorMessage) = await _batchService.CreateBatchAsync(request, userId);

        // Assert
        result.Should().Be(BatchAccessResult.Success);
        created.Should().NotBeNull();
        created!.VolumeProfile.Should().NotBeNull();
        created.VolumeProfile!.CoolingShrinkagePercent.Should().Be(4.0m);
    }
}