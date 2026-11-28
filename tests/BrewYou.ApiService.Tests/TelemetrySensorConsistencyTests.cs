using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using BrewYou.ApiService.Services.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace BrewYou.ApiService.Tests;

public class TelemetrySensorConsistencyTests
{
    private static BrewYouDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<BrewYouDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BrewYouDbContext(options);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    #region 1. BatchTelemetryResolver unit tests (BE-M2 & BE-L3)

    [Fact]
    public void ResolveMetricSource_MultipleSensors_PrefersExactStageMatchOverGlobalAndOrdersDeterministically()
    {
        const string userId = "resolver_user";
        var globalSensor = new Equipment { Id = Guid.NewGuid(), UserId = userId, Name = "Global Tilt", Type = EquipmentType.Sensor, Subtype = EquipmentSubtype.Tilt };
        var conditionSensor = new Equipment { Id = Guid.NewGuid(), UserId = userId, Name = "Condition iSpindel", Type = EquipmentType.Sensor, Subtype = EquipmentSubtype.ISpindel };
        var fermentOnlySensor = new Equipment { Id = Guid.NewGuid(), UserId = userId, Name = "Ferment iSpindel", Type = EquipmentType.Sensor, Subtype = EquipmentSubtype.ISpindel };
        var fermenter = new Equipment { Id = Guid.NewGuid(), UserId = userId, Name = "Conical 1", Type = EquipmentType.Fermenter };

        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-RES-01",
            Name = "Resolver Batch",
            CurrentStage = BrewStage.Condition,
            Status = BatchStatus.Conditioning,
            FermenterId = fermenter.Id,
            Fermenter = fermenter,
            SensorAssignments =
            [
                new BatchSensorAssignment { Id = Guid.NewGuid(), EquipmentId = globalSensor.Id, Equipment = globalSensor, Stage = null, CreatedAt = DateTime.UtcNow.AddHours(-3) },
                new BatchSensorAssignment { Id = Guid.NewGuid(), EquipmentId = fermentOnlySensor.Id, Equipment = fermentOnlySensor, Stage = BrewStage.Ferment, CreatedAt = DateTime.UtcNow.AddHours(-2) },
                new BatchSensorAssignment { Id = Guid.NewGuid(), EquipmentId = conditionSensor.Id, Equipment = conditionSensor, Stage = BrewStage.Condition, CreatedAt = DateTime.UtcNow.AddHours(-1) }
            ]
        };

        var condSource = BatchTelemetryResolver.ResolveMetricSource(batch, BrewStage.Condition);
        condSource.SourceKind.Should().Be(BatchMetricSourceKind.Sensor);
        condSource.PrimarySensor.Should().BeSameAs(conditionSensor);
        condSource.Sensors.Select(s => s.Id).Should().Equal(conditionSensor.Id, globalSensor.Id);

        BatchTelemetryResolver.IsEquipmentSupersededBySensor(batch, BrewStage.Condition, fermenter.Id).Should().BeTrue();
        BatchTelemetryResolver.IsEquipmentSupersededBySensor(batch, BrewStage.Condition, fermentOnlySensor.Id).Should().BeTrue();
        BatchTelemetryResolver.IsEquipmentSupersededBySensor(batch, BrewStage.Condition, conditionSensor.Id).Should().BeFalse();
        BatchTelemetryResolver.IsEquipmentSupersededBySensor(batch, BrewStage.Condition, globalSensor.Id).Should().BeFalse();

        var fermSource = BatchTelemetryResolver.ResolveMetricSource(batch, BrewStage.Ferment);
        fermSource.SourceKind.Should().Be(BatchMetricSourceKind.Sensor);
        fermSource.PrimarySensor.Should().BeSameAs(fermentOnlySensor);
        fermSource.Sensors.Select(s => s.Id).Should().Equal(fermentOnlySensor.Id, globalSensor.Id);
    }

    [Fact]
    public void ResolveMetricSource_ConditionWithOnlyFermentScopedSensor_FallsBackToFermenter()
    {
        const string userId = "resolver_user_2";
        var fermentOnlySensor = new Equipment { Id = Guid.NewGuid(), UserId = userId, Name = "Ferment Only", Type = EquipmentType.Sensor, Subtype = EquipmentSubtype.ISpindel };
        var fermenter = new Equipment { Id = Guid.NewGuid(), UserId = userId, Name = "Conical 1", Type = EquipmentType.Fermenter };

        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BatchCode = "B-RES-02",
            Name = "Resolver Batch 2",
            CurrentStage = BrewStage.Condition,
            Status = BatchStatus.Conditioning,
            FermenterId = fermenter.Id,
            Fermenter = fermenter,
            SensorAssignments =
            [
                new BatchSensorAssignment { Id = Guid.NewGuid(), EquipmentId = fermentOnlySensor.Id, Equipment = fermentOnlySensor, Stage = BrewStage.Ferment }
            ]
        };

        var source = BatchTelemetryResolver.ResolveMetricSource(batch, BrewStage.Condition);
        source.SourceKind.Should().Be(BatchMetricSourceKind.Fermenter);
        source.PrimaryVessel.Should().BeSameAs(fermenter);
        BatchTelemetryResolver.IsEquipmentSupersededBySensor(batch, BrewStage.Condition, fermenter.Id).Should().BeFalse();
        BatchTelemetryResolver.ResolvePrimaryCellarSensor(batch).Should().BeNull();
    }

    #endregion

    #region 2. BatchService DTO projection (BE-M2 & BE-L3)

    [Fact]
    public async Task GetBatchesAsync_ConditionStage_UsesSensorOverFermenterAndReportsMetricSupplyingSensor()
    {
        using var db = CreateDb();
        var broadcast = new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance);
        var batchService = new BatchService(db, broadcast, NullLogger<BatchService>.Instance);
        var userId = "brewer_cond_1";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Main", IsDefault = true };
        var now = DateTime.UtcNow;

        var fermenter = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "Glycol Conical",
            Type = EquipmentType.Fermenter,
            CurrentTemperatureC = 1.2m,
            CurrentSpecificGravity = 1.020m,
            TemperatureUpdatedAt = now,
            LastTelemetryAt = now
        };
        var olderSensor = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "Older Tilt",
            Type = EquipmentType.Sensor,
            Subtype = EquipmentSubtype.Tilt,
            CurrentTemperatureC = 4.5m,
            CurrentSpecificGravity = 1.015m,
            TemperatureUpdatedAt = now.AddMinutes(-30),
            LastTelemetryAt = now.AddMinutes(-30)
        };
        var fresherSensor = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "Fresh iSpindel",
            Type = EquipmentType.Sensor,
            Subtype = EquipmentSubtype.ISpindel,
            CurrentTemperatureC = 2.8m,
            CurrentSpecificGravity = 1.010m,
            TemperatureUpdatedAt = now.AddMinutes(-2),
            LastTelemetryAt = now.AddMinutes(-2)
        };

        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BatchCode = "B-2026-11-28-01",
            Name = "Cold Lager",
            BeerStyle = "Czech Pilsner",
            Status = BatchStatus.Conditioning,
            CurrentStage = BrewStage.Condition,
            BrewDate = DateOnly.FromDateTime(now),
            TargetOg = 1.050m,
            MeasuredOg = 1.050m,
            TargetFg = 1.010m,
            FermenterId = fermenter.Id
        };

        db.BrewerySetups.Add(setup);
        db.Equipment.AddRange(fermenter, olderSensor, fresherSensor);
        db.Batches.Add(batch);
        db.BatchSensorAssignments.AddRange(
            new BatchSensorAssignment { BatchId = batch.Id, EquipmentId = olderSensor.Id, Stage = null, CreatedAt = now.AddHours(-2) },
            new BatchSensorAssignment { BatchId = batch.Id, EquipmentId = fresherSensor.Id, Stage = null, CreatedAt = now.AddHours(-1) });
        await db.SaveChangesAsync();

        var (items, _) = await batchService.GetBatchesAsync(userId, null, null, 1, 20);
        items.Should().ContainSingle();
        var summary = items[0];

        summary.VesselTempC.Should().Be(2.8m);
        summary.CurrentGravity.Should().Be(1.010m);
        summary.FermentationSensorId.Should().Be(fresherSensor.Id);
        summary.FermentationSensorName.Should().Be("Fresh iSpindel");
    }

    #endregion

    #region 3. TelemetryService Condition sensor precedence (BE-M2)

    [Fact]
    public async Task IngestTelemetry_ConditionStageWithAssignedSensor_FermenterDoesNotOverwriteStepTempOrGravityOrMeasuredFg()
    {
        using var db = CreateDb();
        var broadcast = new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance);
        var throttler = new TelemetryThrottlingService();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var telemetryService = new TelemetryService(db, new StubHttpClientFactory(httpClient), broadcast, throttler, NullLogger<TelemetryService>.Instance);

        var userId = "brewer_cond_tel";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Main", IsDefault = true };
        var fermToken = "ferm-raw-token-123";
        var sensorToken = "sensor-raw-token-456";

        var fermenter = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "Conical",
            Type = EquipmentType.Fermenter,
            ConnectionType = EquipmentConnectionType.HttpPush,
            ConnectionToken = EquipmentService.HashConnectionToken(fermToken)
        };
        var sensor = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "iSpindel Cond",
            Type = EquipmentType.Sensor,
            Subtype = EquipmentSubtype.ISpindel,
            ConnectionType = EquipmentConnectionType.HttpPush,
            ConnectionToken = EquipmentService.HashConnectionToken(sensorToken)
        };

        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BatchCode = "B-2026-11-28-02",
            Name = "Conditioned Ale",
            BeerStyle = "Bitter",
            Status = BatchStatus.Conditioning,
            CurrentStage = BrewStage.Condition,
            BrewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            TargetOg = 1.050m,
            MeasuredOg = 1.050m,
            CurrentGravity = 1.012m,
            MeasuredFg = 1.012m
        };
        var fermStep = new BatchFermentationStep
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            StepOrder = 1,
            Name = "Cold Crash",
            Type = FermentationStepType.ColdCrash,
            TargetTemperatureC = 2.0m,
            ActualTemperatureC = 3.0m,
            DurationDays = 3
        };
        batch.FermenterId = fermenter.Id;
        batch.FermentationSteps.Add(fermStep);

        db.BrewerySetups.Add(setup);
        db.Equipment.AddRange(fermenter, sensor);
        db.Batches.Add(batch);
        db.BatchSensorAssignments.Add(new BatchSensorAssignment
        {
            BatchId = batch.Id,
            EquipmentId = sensor.Id,
            Stage = null
        });
        await db.SaveChangesAsync();

        // Fermenter pushes conflicting temp & gravity -> must NOT overwrite batch step temp, CurrentGravity, or MeasuredFg
        using (var fermDoc = JsonDocument.Parse("{\"temperature\": 14.0, \"unit\": \"C\", \"gravity\": 1.025}"))
        {
            var fermRes = await telemetryService.IngestTelemetryAsync(fermToken, fermDoc.RootElement);
            fermRes.Status.Should().Be(TelemetryIngestStatus.Success);
        }

        var batchAfterFerm = await db.Batches.Include(b => b.FermentationSteps).FirstAsync(b => b.Id == batch.Id);
        batchAfterFerm.CurrentGravity.Should().Be(1.012m);
        batchAfterFerm.MeasuredFg.Should().Be(1.012m);
        batchAfterFerm.FermentationSteps.Single().ActualTemperatureC.Should().Be(3.0m);

        // Assigned sensor pushes temp & gravity -> MUST update step temp, CurrentGravity, and MeasuredFg
        using (var sensorDoc = JsonDocument.Parse("{\"name\":\"iSpindel\",\"temperature\": 2.1, \"temp_units\": \"C\", \"gravity\": 1.010}"))
        {
            var sensorRes = await telemetryService.IngestTelemetryAsync(sensorToken, sensorDoc.RootElement);
            sensorRes.Status.Should().Be(TelemetryIngestStatus.Success);
        }

        var batchAfterSensor = await db.Batches.Include(b => b.FermentationSteps).FirstAsync(b => b.Id == batch.Id);
        batchAfterSensor.CurrentGravity.Should().Be(1.010m);
        batchAfterSensor.MeasuredFg.Should().Be(1.010m);
        batchAfterSensor.FermentationSteps.Single().ActualTemperatureC.Should().Be(2.1m);
    }

    #endregion

    #region 4. TelemetryThrottlingService & BrewLink Sync dedup (BE-M3)

    [Fact]
    public void Throttler_BatchIdAndStageTransitionToNull_PersistsImmediatelyThenThrottlesSubsequentIdleReadings()
    {
        var throttler = new TelemetryThrottlingService(TimeSpan.FromSeconds(30));
        var equipmentId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var t0 = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

        throttler.ShouldPersist(equipmentId, t0, 20.0m, stage: BrewStage.Ferment, batchId: batchId).Should().BeTrue();
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(5), 20.0m, stage: BrewStage.Ferment, batchId: batchId).Should().BeFalse();

        // Detaching from batch (batchId -> null, stage -> null) within throttle window MUST persist
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(10), 20.0m, stage: null, batchId: null).Should().BeTrue();

        // Subsequent idle reading with identical null batchId/stage within throttle window MUST throttle
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(15), 20.0m, stage: null, batchId: null).Should().BeFalse();

        // Re-attaching to a batch with stage == null also persists, then detaching batchId -> null persists
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(18), 20.0m, stage: null, batchId: batchId).Should().BeTrue();
        throttler.ShouldPersist(equipmentId, t0.AddSeconds(20), 20.0m, stage: null, batchId: null).Should().BeTrue();
    }

    [Fact]
    public async Task SyncEquipmentStateAsync_WithTelemetryServiceRegistered_BroadcastsOnce()
    {
        using var db = CreateDb();
        var broadcast = new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance);
        var throttler = new TelemetryThrottlingService();
        var dataProtection = DataProtectionProvider.Create("BrewYou.Test");
        var protector = dataProtection.CreateProtector("BrewYou.BrewLinkCredentials");

        var userId = "brewer_sync_dedup";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Main", IsDefault = true };
        var gateway = new BrewLinkGateway
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "BrewLink GW",
            Host = "192.168.1.50",
            GatewayId = "GW-SYNC-01",
            ApiToken = protector.Protect("secret-token"),
            MqttTopicKey = protector.Protect("mqtt-key")
        };
        var boiler = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            BrewerySetup = setup,
            Name = "G30 Connect",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            ConnectionType = EquipmentConnectionType.BrewLink,
            ConnectionConfigJson = JsonSerializer.Serialize(new { brewLinkGatewayId = gateway.Id, targetMac = "C44F331A8B22" }),
            Capacity = 30m,
            CapacityLiters = 30m
        };

        db.BrewerySetups.Add(setup);
        db.BrewLinkGateways.Add(gateway);
        db.Equipment.Add(boiler);
        await db.SaveChangesAsync();

        var gatewayStateJson = JsonSerializer.Serialize(new
        {
            gateway_id = "GW-SYNC-01",
            count = 1,
            equipment = new[]
            {
                new
                {
                    slot = 1,
                    mac = "C4:4F:33:1A:8B:22",
                    name = "G30 Connect",
                    connected = true,
                    state = new
                    {
                        current_temp_c = 66.5,
                        target_temp_c = 67.0,
                        heat_active = true,
                        heat_power_pct = 80,
                        pump_active = true
                    }
                }
            }
        });

        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(gatewayStateJson, Encoding.UTF8, "application/json")
        }));

        var telemetryService = new TelemetryService(db, new StubHttpClientFactory(httpClient), broadcast, throttler, NullLogger<TelemetryService>.Instance);
        var services = new ServiceCollection();
        services.AddSingleton<ITelemetryService>(telemetryService);
        using var sp = services.BuildServiceProvider();

        var brewLinkService = new BrewLinkService(
            db,
            httpClient,
            dataProtection,
            NullLogger<BrewLinkService>.Instance,
            broadcastService: broadcast,
            serviceProvider: sp);

        using var sub = broadcast.SubscribeEquipment(userId);
        sub.Should().NotBeNull();

        var (result, syncDto, err) = await brewLinkService.SyncEquipmentStateAsync(userId, boiler.Id);

        result.Should().Be(BrewLinkSendResult.Success, err);
        syncDto.Should().NotBeNull();
        syncDto!.TemperatureC.Should().Be(66.5m);

        var received = new List<EquipmentTelemetryUpdateDto>();
        while (sub!.Reader.TryRead(out var update))
        {
            received.Add(update);
        }

        received.Should().ContainSingle("SyncEquipmentStateAsync must not duplicate SSE broadcasts when TelemetryService already ingested the reading");
        received[0].EquipmentId.Should().Be(boiler.Id);
        received[0].TemperatureC.Should().Be(66.5m);
    }

    #endregion

    #region 5. BatchService SSE equipment volume broadcasts (BE-M4)

    [Fact]
    public async Task BatchVesselLifecycle_EmitsEquipmentVolumeSseUpdatesAfterCreateAdvanceSwapAndDelete()
    {
        using var db = CreateDb();
        var broadcast = new TelemetryBroadcastService(NullLogger<TelemetryBroadcastService>.Instance);
        var batchService = new BatchService(db, broadcast, NullLogger<BatchService>.Instance);
        var userId = "brewer_sse_vol";
        var setup = new BrewerySetup { Id = Guid.NewGuid(), UserId = userId, Name = "Main", IsDefault = true };

        var boiler = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "Kettle",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            ConnectionType = EquipmentConnectionType.None,
            Capacity = 40m,
            CapacityLiters = 40m,
            CurrentTemperatureC = 65.0m
        };
        var ferm1 = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "Fermenter 1",
            Type = EquipmentType.Fermenter,
            ConnectionType = EquipmentConnectionType.None,
            Capacity = 30m,
            CapacityLiters = 30m,
            CurrentTemperatureC = 19.5m,
            CurrentSpecificGravity = 1.020m
        };
        var ferm2 = new Equipment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BrewerySetupId = setup.Id,
            Name = "Fermenter 2",
            Type = EquipmentType.Fermenter,
            ConnectionType = EquipmentConnectionType.None,
            Capacity = 30m,
            CapacityLiters = 30m
        };

        db.BrewerySetups.Add(setup);
        db.Equipment.AddRange(boiler, ferm1, ferm2);
        await db.SaveChangesAsync();

        using var sub = broadcast.SubscribeEquipment(userId);
        sub.Should().NotBeNull();

        List<EquipmentTelemetryUpdateDto> DrainUpdates()
        {
            var list = new List<EquipmentTelemetryUpdateDto>();
            while (sub!.Reader.TryRead(out var u))
            {
                list.Add(u);
            }
            return list;
        }

        // 1. CreateBatch -> Boiler occupied SSE
        var (createRes, created, createErr) = await batchService.CreateBatchAsync(new CreateBatchRequest(
            RecipeId: null,
            BatchCode: null,
            Name: "SSE Pale Ale",
            BeerStyle: "Pale Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.2m,
            TargetIbu: 35m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            BoilerId: boiler.Id,
            FermenterId: ferm1.Id), userId);
        createRes.Should().Be(BatchAccessResult.Success, createErr);

        var createUpdates = DrainUpdates();
        createUpdates.Should().ContainSingle(u => u.EquipmentId == boiler.Id && u.Source == "BatchVesselUpdate");
        createUpdates.Single(u => u.EquipmentId == boiler.Id).CurrentVolumeLiters.Should().Be(20m);
        createUpdates.Single(u => u.EquipmentId == boiler.Id).BatchId.Should().Be(created!.Id);

        // 2. AdvanceStage Mash -> Ferment -> Boiler emptied (manual temp cleared) & Ferm1 filled
        var (advRes, _, advErr) = await batchService.AdvanceStageAsync(created!.Id, new AdvanceBatchStageRequest(BrewStage.Ferment), userId);
        advRes.Should().Be(BatchAccessResult.Success, advErr);

        var advUpdates = DrainUpdates();
        var boilerAfterAdv = advUpdates.Should().ContainSingle(u => u.EquipmentId == boiler.Id && u.Source == "BatchVesselUpdate").Subject;
        boilerAfterAdv.CurrentVolumeLiters.Should().Be(0m);
        boilerAfterAdv.FillPercentage.Should().Be(0m);
        boilerAfterAdv.TemperatureC.Should().BeNull("emptied non-IoT boiler clears stale manual temp");
        boilerAfterAdv.BatchId.Should().BeNull();

        var ferm1AfterAdv = advUpdates.Should().ContainSingle(u => u.EquipmentId == ferm1.Id && u.Source == "BatchVesselUpdate").Subject;
        ferm1AfterAdv.CurrentVolumeLiters.Should().Be(20m);
        ferm1AfterAdv.BatchId.Should().Be(created.Id);

        // 3. UpdateBatch swap Fermenter 1 -> Fermenter 2 -> Ferm1 emptied (stale manual SG/temp cleared) & Ferm2 filled
        var (updRes, _, updErr) = await batchService.UpdateBatchAsync(created.Id, new UpdateBatchRequest(FermenterId: ferm2.Id), userId);
        updRes.Should().Be(BatchAccessResult.Success, updErr);

        var swapUpdates = DrainUpdates();
        var ferm1AfterSwap = swapUpdates.Should().ContainSingle(u => u.EquipmentId == ferm1.Id && u.Source == "BatchVesselUpdate").Subject;
        ferm1AfterSwap.CurrentVolumeLiters.Should().Be(0m);
        ferm1AfterSwap.TemperatureC.Should().BeNull();
        ferm1AfterSwap.SpecificGravity.Should().BeNull();
        ferm1AfterSwap.BatchId.Should().BeNull();

        var ferm2AfterSwap = swapUpdates.Should().ContainSingle(u => u.EquipmentId == ferm2.Id && u.Source == "BatchVesselUpdate").Subject;
        ferm2AfterSwap.CurrentVolumeLiters.Should().Be(20m);
        ferm2AfterSwap.BatchId.Should().Be(created.Id);

        // 4. DeleteBatch -> Ferm2 emptied via SSE
        var delRes = await batchService.DeleteBatchAsync(created.Id, userId);
        delRes.Should().Be(BatchAccessResult.Success);

        var delUpdates = DrainUpdates();
        var ferm2AfterDel = delUpdates.Should().ContainSingle(u => u.EquipmentId == ferm2.Id && u.Source == "BatchVesselUpdate").Subject;
        ferm2AfterDel.CurrentVolumeLiters.Should().Be(0m);
        ferm2AfterDel.BatchId.Should().BeNull();
    }

    #endregion
}