using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace BrewYou.ApiService.Services;

public class BatchService : IBatchService
{
    private const decimal GallonsToLitersFactor = 3.785411784m;
    private const int MaxActiveBatchesPerUser = 50;
    private const int MaxReadingsPerBatch = 1000;
    private const int MaxEquipmentReadingsPerBatch = 10_000;

    private readonly BrewYouDbContext _db;
    private readonly ITelemetryBroadcastService _broadcastService;
    private readonly ILogger<BatchService> _logger;
    private readonly IBrewLinkService? _brewLinkService;
    private readonly TimeProvider _timeProvider;

    public BatchService(
        BrewYouDbContext db,
        ITelemetryBroadcastService broadcastService,
        ILogger<BatchService> logger,
        IBrewLinkService? brewLinkService = null,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _broadcastService = broadcastService;
        _logger = logger;
        _brewLinkService = brewLinkService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
    private DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);

    private IQueryable<Equipment> GetAccessibleEquipment(string userId)
    {
        return _db.Equipment
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .Where(e => e.UserId == userId ||
                        (e.BrewerySetup != null && (e.BrewerySetup.UserId == userId || e.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role != BreweryRole.Viewer))));
    }

    public async Task<(List<BatchSummaryDto> Items, PaginationMeta Pagination)> GetBatchesAsync(
        string userId, BatchStatus? status, string? search, int page, int limit)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = _db.Batches
            .AsNoTracking()
            .Include(b => b.Recipe)
            .Include(b => b.Hlt)
            .Include(b => b.MashTun)
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Include(b => b.PackagingVessel)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.Equipment)
            .Where(b => b.UserId == userId);

        if (status.HasValue)
        {
            query = query.Where(b => b.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var rawTerm = search.Trim();
            if (_db.Database.IsInMemory())
            {
                query = query.Where(b =>
                    b.Name.Contains(rawTerm, StringComparison.OrdinalIgnoreCase) ||
                    b.BatchCode.Contains(rawTerm, StringComparison.OrdinalIgnoreCase) ||
                    b.BeerStyle.Contains(rawTerm, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var term = EscapeLikePattern(rawTerm);
                query = query.Where(b =>
                    EF.Functions.ILike(b.Name, $"%{term}%", @"\") ||
                    EF.Functions.ILike(b.BatchCode, $"%{term}%", @"\") ||
                    EF.Functions.ILike(b.BeerStyle, $"%{term}%", @"\"));
            }
        }

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)limit);

        var batches = await query
            .OrderByDescending(b => b.BrewDate)
            .ThenByDescending(b => b.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var batchIds = batches.Select(b => b.Id).ToList();
        var batchReadings = batchIds.Count > 0
            ? await _db.BatchReadings
                .AsNoTracking()
                .Where(r => batchIds.Contains(r.BatchId))
                .Select(r => new BatchReadingSummary(r.BatchId, r.Timestamp, r.SpecificGravity, r.TemperatureC))
                .ToListAsync()
            : new List<BatchReadingSummary>();

        var latestBatchReadingByBatch = batchReadings
            .GroupBy(r => r.BatchId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.Timestamp).First());

        var latestBatchReadingTempByBatch = batchReadings
            .Where(r => r.TemperatureC.HasValue)
            .GroupBy(r => r.BatchId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.Timestamp).First());

        var equipmentReadingsByBatch = batchIds.Count > 0
            ? (await _db.EquipmentReadings
                .AsNoTracking()
                .Where(r => r.BatchId.HasValue && batchIds.Contains(r.BatchId.Value))
                .Select(r => new EquipmentReadingSummary(r.BatchId!.Value, r.EquipmentId, r.Stage, r.Timestamp, r.TemperatureC, r.SpecificGravity))
                .ToListAsync())
                .GroupBy(r => r.BatchId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EquipmentReadingSummary>)g.ToList())
            : new Dictionary<Guid, IReadOnlyList<EquipmentReadingSummary>>();

        var today = TodayUtc;
        var items = batches.Select(b =>
        {
            equipmentReadingsByBatch.TryGetValue(b.Id, out var eqReadings);
            latestBatchReadingByBatch.TryGetValue(b.Id, out var latestBatchReading);
            latestBatchReadingTempByBatch.TryGetValue(b.Id, out var latestBatchTempReading);
            var (vesselTempC, tempSourceSensor, tempTimestamp) = ResolveVesselTempC(b, eqReadings, latestBatchTempReading);
            var (currentGravity, gravitySourceSensor, gravityTimestamp) = ResolveCurrentGravity(b, eqReadings, latestBatchReading);
            var alcoholByVolume = b.AlcoholByVolume;
            if (currentGravity.HasValue && (alcoholByVolume == null || currentGravity != b.CurrentGravity))
            {
                var effectiveOg = b.MeasuredOg ?? (b.TargetOg > 1.000m ? b.TargetOg : (decimal?)null);
                if (effectiveOg.HasValue && effectiveOg.Value > currentGravity.Value)
                {
                    alcoholByVolume = BrewingCalculator.CalculateActualAbv(effectiveOg.Value, currentGravity.Value);
                }
            }

            Equipment? fermentationSensor;
            if (tempSourceSensor != null && gravitySourceSensor != null)
            {
                fermentationSensor = gravityTimestamp > tempTimestamp ? gravitySourceSensor : tempSourceSensor;
            }
            else
            {
                fermentationSensor = tempSourceSensor
                    ?? gravitySourceSensor
                    ?? BatchTelemetryResolver.ResolvePrimaryCellarSensor(b);
            }

            return new BatchSummaryDto(
                b.Id,
                b.BatchCode,
                b.Name,
                b.RecipeId,
                b.BeerStyle,
                b.Status,
                b.CurrentStage,
                b.BrewDate,
                Math.Max(0, today.DayNumber - b.BrewDate.DayNumber),
                b.TargetOg,
                b.MeasuredOg,
                currentGravity,
                b.TargetFg,
                b.MeasuredFg,
                alcoholByVolume,
                b.TargetColorSrm,
                vesselTempC,
                b.FermenterId,
                b.Fermenter?.Name,
                b.TargetBatchSizeLiters,
                b.MeasuredBatchSizeLiters,
                b.CreatedAt,
                b.UpdatedAt,
                b.HltId,
                b.Hlt?.Name,
                b.MashTunId,
                b.MashTun?.Name,
                b.BoilerId,
                b.Boiler?.Name,
                b.Recipe?.Name ?? b.RecipeName,
                fermentationSensor?.Id,
                fermentationSensor?.Name,
                b.IsHistorical
            );
        }).ToList();

        var meta = new PaginationMeta(page, limit, total, totalPages);
        return (items, meta);
    }

    private sealed record BatchReadingSummary(Guid BatchId, DateTime Timestamp, decimal SpecificGravity, decimal? TemperatureC);
    private sealed record EquipmentReadingSummary(Guid BatchId, Guid? EquipmentId, BrewStage? Stage, DateTime Timestamp, decimal TemperatureC, decimal? SpecificGravity);

    private static (decimal? Value, Equipment? SourceSensor, DateTime Timestamp) ResolveCurrentGravity(
        Batch batch,
        IReadOnlyList<EquipmentReadingSummary>? equipmentReadings,
        BatchReadingSummary? latestBatchReading)
    {
        if (!BatchLifecycle.OccupiesLiveEquipment(batch) || !BatchTelemetryResolver.IsCellarStage(batch.CurrentStage))
        {
            var fallbackGravity = batch.CurrentGravity ?? latestBatchReading?.SpecificGravity;
            return (fallbackGravity, null, latestBatchReading?.Timestamp ?? DateTime.MinValue);
        }

        decimal? bestGravity = null;
        var bestTimestamp = DateTime.MinValue;
        Equipment? bestSensor = null;

        void Consider(decimal? gravity, DateTime? timestamp, Equipment? sensor = null)
        {
            if (!gravity.HasValue)
            {
                return;
            }

            var candidateTs = timestamp ?? DateTime.MinValue;
            if (!bestGravity.HasValue || candidateTs >= bestTimestamp)
            {
                bestGravity = gravity.Value;
                bestTimestamp = candidateTs;
                bestSensor = sensor;
            }
        }

        // In fermentation and conditioning stages, current gravity comes from a stage-scoped sensor if one is assigned
        // (ignoring the fermenter equipment); otherwise it comes from the fermenter equipment / batch readings.
        var metricSource = BatchTelemetryResolver.ResolveMetricSource(batch, batch.CurrentStage);
        if (metricSource.SourceKind == BatchMetricSourceKind.Sensor)
        {
            var sensorsById = metricSource.Sensors.ToDictionary(s => s.Id);

            foreach (var sensor in metricSource.Sensors)
            {
                if (sensor.CurrentSpecificGravity is { } sensorSg)
                {
                    Consider(sensorSg, sensor.LastTelemetryAt ?? sensor.TemperatureUpdatedAt, sensor);
                }
            }

            var latestSensorReading = equipmentReadings?
                .Where(r =>
                    r.SpecificGravity.HasValue &&
                    r.EquipmentId.HasValue &&
                    sensorsById.ContainsKey(r.EquipmentId.Value) &&
                    (r.Stage == batch.CurrentStage || r.Stage == null))
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefault();

            if (latestSensorReading != null && latestSensorReading.EquipmentId.HasValue)
            {
                sensorsById.TryGetValue(latestSensorReading.EquipmentId.Value, out var readingSensor);
                Consider(latestSensorReading.SpecificGravity, latestSensorReading.Timestamp, readingSensor);
            }

            return (bestGravity ?? batch.CurrentGravity ?? latestBatchReading?.SpecificGravity, bestSensor, bestTimestamp);
        }

        if (batch.Fermenter?.CurrentSpecificGravity is { } fermenterSg &&
            (!batch.Fermenter.AssignedBatchId.HasValue || batch.Fermenter.AssignedBatchId.Value == batch.Id))
        {
            Consider(fermenterSg, batch.Fermenter.LastTelemetryAt ?? batch.Fermenter.TemperatureUpdatedAt);
        }

        var latestFermenterReading = equipmentReadings?
            .Where(r =>
                r.SpecificGravity.HasValue &&
                (r.Stage == batch.CurrentStage || r.Stage == null) &&
                (!r.EquipmentId.HasValue || (batch.FermenterId.HasValue && r.EquipmentId.Value == batch.FermenterId.Value)))
            .OrderByDescending(r => r.Timestamp)
            .FirstOrDefault();

        if (latestFermenterReading != null)
        {
            Consider(latestFermenterReading.SpecificGravity, latestFermenterReading.Timestamp);
        }

        if (latestBatchReading != null)
        {
            Consider(latestBatchReading.SpecificGravity, latestBatchReading.Timestamp);
        }

        return (bestGravity ?? batch.CurrentGravity, null, bestTimestamp);
    }

    private static (decimal? Value, Equipment? SourceSensor, DateTime Timestamp) ResolveVesselTempC(
        Batch batch,
        IReadOnlyList<EquipmentReadingSummary>? equipmentReadings,
        BatchReadingSummary? latestBatchReadingWithTemp)
    {
        if (!BatchLifecycle.OccupiesLiveEquipment(batch))
        {
            if (batch.Status == BatchStatus.Planned)
            {
                return (null, null, DateTime.MinValue);
            }

            // For completed/archived/historical batches, never read live vessel temperature because the equipment may be reused.
            var latestRecordedEq = equipmentReadings?
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefault();

            if (latestRecordedEq != null && latestBatchReadingWithTemp?.TemperatureC is { } completedBatchTemp)
            {
                return latestRecordedEq.Timestamp >= latestBatchReadingWithTemp.Timestamp
                    ? (latestRecordedEq.TemperatureC, null, latestRecordedEq.Timestamp)
                    : (completedBatchTemp, null, latestBatchReadingWithTemp.Timestamp);
            }

            if (latestRecordedEq != null)
            {
                return (latestRecordedEq.TemperatureC, null, latestRecordedEq.Timestamp);
            }

            return (latestBatchReadingWithTemp?.TemperatureC, null, latestBatchReadingWithTemp?.Timestamp ?? DateTime.MinValue);
        }

        decimal? bestTemp = null;
        var bestTimestamp = DateTime.MinValue;
        Equipment? bestSensor = null;

        void Consider(decimal? tempC, DateTime? timestamp, Equipment? sensor = null)
        {
            if (!tempC.HasValue)
            {
                return;
            }

            var candidateTs = timestamp ?? DateTime.MinValue;
            if (!bestTemp.HasValue || candidateTs >= bestTimestamp)
            {
                bestTemp = tempC.Value;
                bestTimestamp = candidateTs;
                bestSensor = sensor;
            }
        }

        var metricSource = BatchTelemetryResolver.ResolveMetricSource(batch, batch.CurrentStage);

        // In fermentation and conditioning stages, vessel temperature comes exclusively from a stage-scoped sensor
        // if one is assigned to the batch; otherwise it comes from the fermenter equipment itself.
        if (BatchTelemetryResolver.IsCellarStage(batch.CurrentStage))
        {
            if (metricSource.SourceKind == BatchMetricSourceKind.Sensor)
            {
                var sensorsById = metricSource.Sensors.ToDictionary(s => s.Id);

                foreach (var sensor in metricSource.Sensors)
                {
                    if (sensor.CurrentTemperatureC is { } sensorTemp)
                    {
                        Consider(sensorTemp, sensor.TemperatureUpdatedAt ?? sensor.LastTelemetryAt, sensor);
                    }
                }

                var latestSensorReading = equipmentReadings?
                    .Where(r =>
                        r.EquipmentId.HasValue &&
                        sensorsById.ContainsKey(r.EquipmentId.Value) &&
                        (r.Stage == batch.CurrentStage || r.Stage == null))
                    .OrderByDescending(r => r.Timestamp)
                    .FirstOrDefault();

                if (latestSensorReading != null && latestSensorReading.EquipmentId.HasValue)
                {
                    sensorsById.TryGetValue(latestSensorReading.EquipmentId.Value, out var readingSensor);
                    Consider(latestSensorReading.TemperatureC, latestSensorReading.Timestamp, readingSensor);
                }

                return (bestTemp, bestSensor, bestTimestamp);
            }

            if (batch.Fermenter?.CurrentTemperatureC is { } fermenterTemp &&
                (!batch.Fermenter.AssignedBatchId.HasValue || batch.Fermenter.AssignedBatchId.Value == batch.Id))
            {
                Consider(fermenterTemp, batch.Fermenter.TemperatureUpdatedAt ?? batch.Fermenter.LastTelemetryAt);
            }

            var latestFermenterReading = equipmentReadings?
                .Where(r =>
                    (r.Stage == batch.CurrentStage || r.Stage == null) &&
                    (!r.EquipmentId.HasValue || (batch.FermenterId.HasValue && r.EquipmentId.Value == batch.FermenterId.Value)))
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefault();

            if (latestFermenterReading != null)
            {
                Consider(latestFermenterReading.TemperatureC, latestFermenterReading.Timestamp);
            }

            if (latestBatchReadingWithTemp?.TemperatureC is { } fermBatchTemp)
            {
                Consider(fermBatchTemp, latestBatchReadingWithTemp.Timestamp);
            }

            return (bestTemp, null, bestTimestamp);
        }

        // 1. Live temperature from the active stage's primary vessel
        var activeVessel = metricSource.PrimaryVessel;

        if (activeVessel?.CurrentTemperatureC is { } vesselTemp &&
            (!activeVessel.AssignedBatchId.HasValue || activeVessel.AssignedBatchId.Value == batch.Id))
        {
            Consider(vesselTemp, activeVessel.TemperatureUpdatedAt ?? activeVessel.LastTelemetryAt);
        }

        // 2. Live temperature from assigned auxiliary sensors scoped to this stage (or all stages)
        foreach (var sensor in metricSource.Sensors)
        {
            if (sensor.CurrentTemperatureC is { } sensorTemp)
            {
                Consider(sensorTemp, sensor.TemperatureUpdatedAt ?? sensor.LastTelemetryAt);
            }
        }

        // 3. Latest EquipmentReading recorded for this batch in the current stage (or unstaged) from currently assigned equipment
        var stageAllowedEquipmentIds = new HashSet<Guid>();
        if (batch.CurrentStage == BrewStage.Mash)
        {
            if (batch.MashTunId.HasValue) stageAllowedEquipmentIds.Add(batch.MashTunId.Value);
            if (batch.BoilerId.HasValue) stageAllowedEquipmentIds.Add(batch.BoilerId.Value);
        }
        else if (activeVessel != null)
        {
            stageAllowedEquipmentIds.Add(activeVessel.Id);
        }

        foreach (var assignment in BatchTelemetryResolver.GetStageScopedSensorAssignments(batch, batch.CurrentStage))
        {
            stageAllowedEquipmentIds.Add(assignment.EquipmentId);
        }

        var latestStageEquipmentReading = equipmentReadings?
            .Where(r =>
                (r.Stage == batch.CurrentStage || r.Stage == null) &&
                (!r.EquipmentId.HasValue || stageAllowedEquipmentIds.Contains(r.EquipmentId.Value)))
            .OrderByDescending(r => r.Timestamp)
            .FirstOrDefault();

        if (latestStageEquipmentReading != null)
        {
            Consider(latestStageEquipmentReading.TemperatureC, latestStageEquipmentReading.Timestamp);
        }

        // 4. Latest manual/hydrometer BatchReading carrying a temperature
        if (latestBatchReadingWithTemp?.TemperatureC is { } readingTemp)
        {
            Consider(readingTemp, latestBatchReadingWithTemp.Timestamp);
        }

        return (bestTemp, null, bestTimestamp);
    }

    private IQueryable<Batch> QueryBatchWithDetails()
    {
        return _db.Batches
            .Include(b => b.Recipe)
            .Include(b => b.BrewerySetup)
            .Include(b => b.Hlt)
            .Include(b => b.MashTun)
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Include(b => b.PackagingVessel)
            .Include(b => b.Readings)
            .Include(b => b.Ingredients)
            .Include(b => b.MashSteps)
            .Include(b => b.FermentationSteps)
            .Include(b => b.StageHistory)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.Equipment)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.BatchMashStep)
            .Include(b => b.VolumeProfile);
    }

    public async Task<(BatchAccessResult Result, BatchDetailDto? Batch)> GetBatchByIdAsync(Guid id, string userId)
    {
        var batch = await QueryBatchWithDetails()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null);
        }

        var eqReadings = await _db.EquipmentReadings
            .AsNoTracking()
            .Where(r => r.BatchId == batch.Id)
            .Select(r => new EquipmentReadingSummary(r.BatchId!.Value, r.EquipmentId, r.Stage, r.Timestamp, r.TemperatureC, r.SpecificGravity))
            .ToListAsync();

        var latestBatchReading = batch.Readings
            .OrderByDescending(r => r.Timestamp)
            .Select(r => new BatchReadingSummary(r.BatchId, r.Timestamp, r.SpecificGravity, r.TemperatureC))
            .FirstOrDefault();

        var (resolvedGravity, _, _) = ResolveCurrentGravity(batch, eqReadings, latestBatchReading);
        return (BatchAccessResult.Success, MapToDetailDto(batch, resolvedCurrentGravity: resolvedGravity));
    }

    public async Task<string> GetNextBatchCodeAsync(string userId, DateOnly? date = null)
    {
        var targetDate = date ?? TodayUtc;
        var prefix = $"B-{targetDate:yyyy-MM-dd}-";

        var existingCodes = await _db.Batches
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.BatchCode.StartsWith(prefix))
            .Select(b => b.BatchCode)
            .ToListAsync();

        var maxSeq = 0;
        foreach (var code in existingCodes)
        {
            var suffix = code[prefix.Length..];
            if (int.TryParse(suffix, out var seq) && seq > maxSeq)
            {
                maxSeq = seq;
            }
        }

        return $"{prefix}{(maxSeq + 1):D2}";
    }

    public async Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> CreateBatchAsync(
        CreateBatchRequest request, string userId)
    {
        // 1. Quota check
        var activeCount = await _db.Batches
            .CountAsync(b => b.UserId == userId && b.Status != BatchStatus.Completed && b.Status != BatchStatus.Archived);

        if (activeCount >= MaxActiveBatchesPerUser)
        {
            return (BatchAccessResult.QuotaExceeded, null, $"Active batch limit reached ({MaxActiveBatchesPerUser}). Complete or archive older batches.");
        }

        // 2. Resolve Recipe if provided
        Recipe? recipe = null;
        if (request.RecipeId.HasValue)
        {
            recipe = await _db.Recipes
                .Include(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
                .Include(r => r.MashSteps)
                .Include(r => r.FermentationSteps)
                .FirstOrDefaultAsync(r => r.Id == request.RecipeId.Value && (r.UserId == userId || r.IsPublic));

            if (recipe == null)
            {
                return (BatchAccessResult.NotFound, null, "Recipe not found.");
            }
        }

        // 3. Resolve Equipment & Verify Ownership
        Equipment? boiler = null;
        if (request.BoilerId.HasValue)
        {
            boiler = await GetAccessibleEquipment(userId)
                .FirstOrDefaultAsync(e => e.Id == request.BoilerId.Value);

            if (boiler == null)
            {
                return (BatchAccessResult.NotFound, null, "Boiler not found.");
            }

            if (boiler.Type is EquipmentType.Sensor or EquipmentType.Tap || boiler.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor or EquipmentSubtype.BrewLink)
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as boilers.");
            }
        }

        Equipment? fermenter = null;
        if (request.FermenterId.HasValue)
        {
            fermenter = await GetAccessibleEquipment(userId)
                .FirstOrDefaultAsync(e => e.Id == request.FermenterId.Value);

            if (fermenter == null)
            {
                return (BatchAccessResult.NotFound, null, "Fermenter not found.");
            }

            if (fermenter.Type is EquipmentType.Sensor or EquipmentType.Tap || fermenter.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor or EquipmentSubtype.BrewLink)
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as fermenters.");
            }
        }

        Equipment? packagingVessel = null;
        if (request.PackagingVesselId.HasValue)
        {
            packagingVessel = await GetAccessibleEquipment(userId)
                .FirstOrDefaultAsync(e => e.Id == request.PackagingVesselId.Value);

            if (packagingVessel == null)
            {
                return (BatchAccessResult.NotFound, null, "Packaging vessel not found.");
            }

            if (packagingVessel.Type is EquipmentType.Sensor or EquipmentType.Tap || packagingVessel.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor or EquipmentSubtype.BrewLink)
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as packaging vessels.");
            }
        }

        Equipment? hlt = null;
        if (request.HltId.HasValue)
        {
            hlt = await GetAccessibleEquipment(userId)
                .FirstOrDefaultAsync(e => e.Id == request.HltId.Value);

            if (hlt == null)
            {
                return (BatchAccessResult.NotFound, null, "HLT equipment not found.");
            }

            if (hlt.Type is EquipmentType.Sensor or EquipmentType.Tap || hlt.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor or EquipmentSubtype.BrewLink)
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as hot liquor tanks.");
            }
        }

        Equipment? mashTun = null;
        if (request.MashTunId.HasValue)
        {
            mashTun = await GetAccessibleEquipment(userId)
                .FirstOrDefaultAsync(e => e.Id == request.MashTunId.Value);

            if (mashTun == null)
            {
                return (BatchAccessResult.NotFound, null, "Mash tun equipment not found.");
            }

            if (mashTun.Type is EquipmentType.Sensor or EquipmentType.Tap || mashTun.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor or EquipmentSubtype.BrewLink)
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as mash tuns.");
            }
        }

        var plannedVolume = request.TargetBatchSizeLiters ?? recipe?.BatchSizeLiters ?? 20.0m;
        var brewDate = request.BrewDate ?? TodayUtc;
        var isHistorical = request.IsHistorical ?? false;

        if (!isHistorical)
        {
            foreach (var candidate in new[] { hlt, mashTun, boiler }.Where(e => e != null).DistinctBy(e => e!.Id))
            {
                if (await IsEquipmentOccupiedByOtherBatchAsync(candidate!, excludeBatchId: null))
                {
                    return (BatchAccessResult.Conflict, null, $"Equipment '{candidate!.Name}' is currently occupied by another active batch.");
                }
            }

            if (packagingVessel != null && packagingVessel.AssignedBatchId.HasValue)
            {
                return (BatchAccessResult.Conflict, null, $"Equipment '{packagingVessel.Name}' is currently assigned to another active batch.");
            }
        }

        // Resolve loss parameters & setup early: Request override -> Equipment override -> BrewerySetup default -> System constant
        var setup = boiler?.BrewerySetup ?? mashTun?.BrewerySetup ?? hlt?.BrewerySetup ?? (request.BoilerId.HasValue ? null :
            await _db.BrewerySetups.FirstOrDefaultAsync(s => s.UserId == userId && s.IsDefault));

        var isAllInOne = boiler?.Subtype == EquipmentSubtype.AllInOne ||
                         mashTun?.Subtype == EquipmentSubtype.AllInOne ||
                         (boiler != null && (mashTun == null || boiler.Id == mashTun.Id));

        var boilOffRate = request.BoilOffRatePerHour ?? boiler?.BoilOffRatePerHour ?? setup?.DefaultBoilOffRatePerHour ?? 3.0m;
        var kettleTrub = request.KettleTrubLossLiters ?? boiler?.TrubLossLiters ?? setup?.DefaultKettleTrubLossLiters ?? 1.5m;
        var mashDeadSpace = request.MashTunDeadSpaceLiters ?? mashTun?.MashTunDeadSpaceLiters ?? boiler?.MashTunDeadSpaceLiters ?? setup?.DefaultMashTunDeadSpaceLiters ?? 0.0m;
        var hltDeadSpace = request.HltDeadSpaceLiters ?? hlt?.TrubLossLiters ?? 0.0m;
        var plumbingLoss = request.PlumbingLossLiters ?? 0.0m;
        var spargeMethod = request.SpargeMethod ?? "Batch";
        var fermenterLoss = request.FermenterLossLiters ?? fermenter?.TrubLossLiters ?? setup?.DefaultFermenterLossLiters ?? 1.5m;
        var grainAbsorption = isAllInOne ? 0.80m : (setup?.DefaultGrainAbsorptionRate ?? 0.96m);
        var shrinkage = setup?.CoolingShrinkagePercent ?? 4.0m;
        var mashThickness = isAllInOne ? 2.7m : 3.0m;
        var packagingLoss = request.PackagingLossLiters ?? packagingVessel?.PackagingLossLiters ?? setup?.DefaultPackagingLossLiters ?? 0.5m;
        var targetBasis = request.TargetVolumeBasis ?? TargetVolumeBasis.Fermenter;

        // Calculate required volume into fermenter.
        // BeerXML/BeerJSON recipes define ingredients for volume into fermenter.
        // When TargetBasis is Packaged, plannedVolume represents the packaged beer target,
        // so we work backwards to determine the required fermenter volume to scale ingredients
        // accurately and preserve recipe target gravity, bitterness, and ABV.
        var targetFermenterVolume = targetBasis == TargetVolumeBasis.Packaged
            ? plannedVolume + fermenterLoss + packagingLoss
            : plannedVolume;

        // 4. Concurrency-safe Batch Code Generation
        var batchCode = string.IsNullOrWhiteSpace(request.BatchCode)
            ? await GetNextBatchCodeAsync(userId, brewDate)
            : request.BatchCode.Trim();

        var codeExists = await _db.Batches.AnyAsync(b => b.UserId == userId && b.BatchCode == batchCode);
        if (codeExists)
        {
            batchCode = await GetNextBatchCodeAsync(userId, brewDate);
        }

        // 5. Build Batch Entity
        var batch = new Batch
        {
            UserId = userId,
            RecipeId = recipe?.Id,
            RecipeName = recipe?.Name,
            BatchCode = batchCode,
            Name = request.Name.Trim(),
            BeerStyle = request.BeerStyle?.Trim() ?? recipe?.BeerStyle ?? "American IPA",
            Status = BatchStatus.Brewing,
            CurrentStage = BrewStage.Mash,
            BrewDate = brewDate,
            IsHistorical = isHistorical,
            TargetBatchSizeLiters = plannedVolume,
            BoilTimeMinutes = request.BoilTimeMinutes ?? recipe?.BoilTimeMinutes ?? 60,
            EfficiencyPercent = request.EfficiencyPercent ?? recipe?.EfficiencyPercent ?? 72.0m,
            TargetOg = request.TargetOg ?? recipe?.OriginalGravity ?? 1.050m,
            TargetFg = request.TargetFg ?? recipe?.FinalGravity ?? 1.010m,
            TargetAbv = request.TargetAbv ?? recipe?.AlcoholByVolume ?? 5.25m,
            TargetIbu = request.TargetIbu ?? recipe?.BitternessIbu ?? 35.0m,
            TargetColorSrm = request.TargetColorSrm ?? recipe?.ColorSrm ?? 6.0m,
            MeasuredOg = request.MeasuredOg,
            PitchTemperatureC = request.PitchTemperatureC,
            Notes = request.Notes?.Trim(),
            HltId = hlt?.Id,
            Hlt = hlt,
            MashTunId = mashTun?.Id ?? boiler?.Id,
            MashTun = mashTun ?? boiler,
            BoilerId = boiler?.Id,
            Boiler = boiler,
            FermenterId = fermenter?.Id,
            Fermenter = fermenter,
            PackagingVesselId = packagingVessel?.Id,
            PackagingVessel = packagingVessel,
            BrewerySetupId = setup?.Id
        };

        // 6. Snapshot ingredients scaled against target fermenter volume and ordered chronologically
        if (recipe != null && recipe.Ingredients.Count > 0)
        {
            var scale = recipe.BatchSizeLiters > 0 ? targetFermenterVolume / recipe.BatchSizeLiters : 1.0m;
            foreach (var ri in OrderIngredientsChronologically(recipe.Ingredients))
            {
                batch.Ingredients.Add(new BatchIngredient
                {
                    BatchId = batch.Id,
                    SourceIngredientId = ri.IngredientId,
                    Name = ri.Ingredient?.Name ?? "Ingredient",
                    Type = ri.Ingredient?.Type ?? IngredientType.Other,
                    FermentableCategory = ri.Ingredient?.FermentableCategory,
                    FermentabilityPercent = ri.Ingredient?.FermentabilityPercent,
                    Amount = Math.Round(ri.Amount * scale, 3),
                    Unit = ri.Unit,
                    AdditionStage = ri.Usage,
                    AdditionTimeMinutes = ri.DurationMinutes,
                    Notes = ri.Notes,
                    Form = ri.Form ?? ri.Ingredient?.Form
                });
            }
        }
        else if (request.CustomIngredients != null)
        {
            var customIngredients = request.CustomIngredients.Select(ci => new BatchIngredient
            {
                BatchId = batch.Id,
                Name = ci.Name.Trim(),
                Type = ci.Type,
                FermentableCategory = ci.FermentableCategory,
                FermentabilityPercent = ci.FermentabilityPercent,
                Amount = ci.Amount,
                Unit = ci.Unit.Trim(),
                AdditionStage = ci.AdditionStage,
                AdditionTimeMinutes = ci.AdditionTimeMinutes,
                Notes = ci.Notes?.Trim(),
                Form = ci.Form
            });

            foreach (var ci in OrderIngredientsChronologically(customIngredients))
            {
                batch.Ingredients.Add(ci);
            }
        }

        // 6.2 Snapshot mash steps
        if (recipe != null && recipe.MashSteps.Count > 0)
        {
            var scale = recipe.BatchSizeLiters > 0 ? targetFermenterVolume / recipe.BatchSizeLiters : 1.0m;
            foreach (var rms in recipe.MashSteps.OrderBy(s => s.StepOrder))
            {
                batch.MashSteps.Add(new BatchMashStep
                {
                    BatchId = batch.Id,
                    RecipeMashStepId = rms.Id,
                    StepOrder = rms.StepOrder,
                    Name = rms.Name,
                    Type = rms.Type,
                    TargetTemperatureC = rms.TemperatureC,
                    DurationMinutes = rms.DurationMinutes,
                    RampTimeMinutes = rms.RampTimeMinutes,
                    InfuseAmountLiters = rms.InfuseAmountLiters.HasValue ? Math.Round(rms.InfuseAmountLiters.Value * scale, 2) : null,
                    IsCompleted = false
                });
            }
        }
        else if (request.CustomMashSteps != null && request.CustomMashSteps.Count > 0)
        {
            foreach (var cms in request.CustomMashSteps.OrderBy(s => s.StepOrder))
            {
                batch.MashSteps.Add(new BatchMashStep
                {
                    BatchId = batch.Id,
                    StepOrder = cms.StepOrder,
                    Name = cms.Name.Trim(),
                    Type = cms.Type,
                    TargetTemperatureC = cms.TargetTemperatureC,
                    DurationMinutes = cms.DurationMinutes,
                    RampTimeMinutes = cms.RampTimeMinutes,
                    InfuseAmountLiters = cms.InfuseAmountLiters,
                    Notes = cms.Notes?.Trim(),
                    IsCompleted = false
                });
            }
        }
        else
        {
            // Default single infusion rest so every batch has a valid mash step
            batch.MashSteps.Add(new BatchMashStep
            {
                BatchId = batch.Id,
                StepOrder = 1,
                Name = "Saccharification Rest",
                Type = MashStepType.Infusion,
                TargetTemperatureC = 65.0m,
                DurationMinutes = 60,
                IsCompleted = false
            });
        }

        // 6.3 Snapshot fermentation steps
        if (recipe != null && recipe.FermentationSteps.Count > 0)
        {
            foreach (var rfs in recipe.FermentationSteps.OrderBy(s => s.StepOrder))
            {
                batch.FermentationSteps.Add(new BatchFermentationStep
                {
                    BatchId = batch.Id,
                    RecipeFermentationStepId = rfs.Id,
                    StepOrder = rfs.StepOrder,
                    Name = rfs.Name,
                    Type = rfs.Type,
                    TargetTemperatureC = rfs.TargetTemperatureC,
                    DurationDays = rfs.DurationDays,
                    RampTimeHours = rfs.RampTimeHours,
                    TriggerGravity = rfs.TriggerGravity,
                    Notes = rfs.Notes,
                    IsCompleted = false
                });
            }
        }
        else if (request.CustomFermentationSteps != null && request.CustomFermentationSteps.Count > 0)
        {
            foreach (var cfs in request.CustomFermentationSteps.OrderBy(s => s.StepOrder))
            {
                batch.FermentationSteps.Add(new BatchFermentationStep
                {
                    BatchId = batch.Id,
                    StepOrder = cfs.StepOrder,
                    Name = cfs.Name.Trim(),
                    Type = cfs.Type,
                    TargetTemperatureC = cfs.TargetTemperatureC,
                    DurationDays = cfs.DurationDays,
                    RampTimeHours = cfs.RampTimeHours,
                    TriggerGravity = cfs.TriggerGravity,
                    Notes = cfs.Notes?.Trim(),
                    IsCompleted = false
                });
            }
        }
        else
        {
            // Default primary fermentation step so every batch has a valid fermentation profile
            batch.FermentationSteps.Add(new BatchFermentationStep
            {
                BatchId = batch.Id,
                StepOrder = 1,
                Name = "Primary Fermentation",
                Type = FermentationStepType.Primary,
                TargetTemperatureC = batch.PitchTemperatureC ?? 19.0m,
                DurationDays = 14,
                IsCompleted = false
            });
        }

        // 6.5 Calculate and attach Volume Profile (excluding non-mash sugars, syrups, and extracts)
        var totalGrainKg = BrewingCalculator.CalculateMashGrainWeightKg(batch.Ingredients);
        if (totalGrainKg <= 0m && batch.RecipeId == null && batch.Ingredients.Count == 0)
        {
            totalGrainKg = Math.Round(4.5m * (targetFermenterVolume / 20.0m), 3);
        }

        var volumeInput = new VolumeCalculationInput(
            TargetBatchSizeLiters: plannedVolume,
            BoilTimeMinutes: batch.BoilTimeMinutes,
            TotalGrainWeightKg: totalGrainKg,
            TargetBasis: targetBasis,
            SpargeEnabled: request.SpargeEnabled ?? true,
            BoilOffRatePerHour: boilOffRate,
            GrainAbsorptionRate: grainAbsorption,
            KettleTrubLossLiters: kettleTrub,
            FermenterLossLiters: fermenterLoss,
            MashTunDeadSpaceLiters: mashDeadSpace,
            CoolingShrinkagePercent: shrinkage,
            PackagingLossLiters: packagingLoss,
            MashThicknessLitersPerKg: mashThickness,
            HltDeadSpaceLiters: hltDeadSpace,
            PlumbingLossLiters: plumbingLoss,
            SpargeMethod: spargeMethod,
            RecoverMashTunDeadSpace: isAllInOne
        );

        var volumeResult = VolumeCalculator.CalculateWaterRequirements(volumeInput);

        batch.VolumeProfile = new BatchVolumeProfile
        {
            BatchId = batch.Id,
            TotalWaterLiters = volumeResult.TotalWaterLiters,
            StrikeWaterLiters = volumeResult.StrikeWaterLiters,
            SpargeWaterLiters = volumeResult.SpargeWaterLiters,
            TargetPreBoilVolumeLiters = volumeResult.TargetPreBoilVolumeLiters,
            TargetPostBoilVolumeLiters = volumeResult.TargetPostBoilVolumeLiters,
            TargetFermenterVolumeLiters = volumeResult.TargetFermenterVolumeLiters,
            TargetPackagedVolumeLiters = volumeResult.TargetPackagedVolumeLiters,
            BoilOffRatePerHour = boilOffRate,
            GrainAbsorptionRateLPerKg = grainAbsorption,
            KettleTrubLossLiters = kettleTrub,
            FermenterTrubLossLiters = fermenterLoss,
            MashTunDeadSpaceLiters = mashDeadSpace,
            HltDeadSpaceLiters = hltDeadSpace,
            PlumbingLossLiters = plumbingLoss,
            SpargeMethod = spargeMethod,
            CoolingShrinkagePercent = shrinkage,
            PackagingLossLiters = packagingLoss
        };

        // 7. Seed Initial Stages
        var stages = new[]
        {
            (BrewStage.Mash, StageStatus.InProgress, (DateTime?)DateTime.UtcNow),
            (BrewStage.Boil, StageStatus.Pending, (DateTime?)null),
            (BrewStage.Ferment, StageStatus.Pending, (DateTime?)null),
            (BrewStage.Condition, StageStatus.Pending, (DateTime?)null),
            (BrewStage.Package, StageStatus.Pending, (DateTime?)null)
        };

        foreach (var (stage, status, startedAt) in stages)
        {
            batch.StageHistory.Add(new BatchStageHistory
            {
                BatchId = batch.Id,
                Stage = stage,
                Status = status,
                StartedAt = startedAt
            });
        }

        // 7.5 Attach Auxiliary Sensor Assignments
        if (request.SensorAssignments != null)
        {
            var (sensorResult, validatedAssignments, sensorErr) = await ValidateAndBuildSensorAssignmentsAsync(
                batch, request.SensorAssignments, userId, allowRecipeMashStepFallback: true);

            if (sensorResult != BatchAccessResult.Success)
            {
                return (sensorResult, null, sensorErr);
            }

            foreach (var assignment in validatedAssignments)
            {
                batch.SensorAssignments.Add(assignment);
            }
        }

        // 7.5 Baseline telemetry: log the primary vessel's live temperature against mash step 1 for live batches only
        var primaryVessel = mashTun ?? boiler;
        if (!isHistorical && primaryVessel?.CurrentTemperatureC != null && batch.MashSteps.Count > 0)
        {
            var firstStep = batch.MashSteps.OrderBy(s => s.StepOrder).First();
            var nowUtc = UtcNow;
            firstStep.ActualTemperatureC = primaryVessel.CurrentTemperatureC.Value;

            batch.EquipmentReadings.Add(new EquipmentReading
            {
                EquipmentId = primaryVessel.Id,
                BatchId = batch.Id,
                Stage = BrewStage.Mash,
                BatchMashStepId = firstStep.Id,
                StepName = firstStep.Name,
                TemperatureC = primaryVessel.CurrentTemperatureC.Value,
                Source = primaryVessel.ConnectionType == EquipmentConnectionType.BrewLink ? "BrewLinkInitial" : "InitialTelemetry",
                Timestamp = nowUtc,
                CreatedAt = nowUtc
            });
        }

        // 8. Transactional Equipment Occupancy
        var affectedEquipmentIds = new HashSet<Guid>();
        var occupancyTimestamp = UtcNow;
        if (_db.Database.IsInMemory())
        {
            if (!isHistorical)
            {
                BatchVesselReconciler.OccupyInitialMashVessels(
                    hlt, mashTun, boiler, plannedVolume,
                    volumeResult.StrikeWaterLiters, volumeResult.SpargeWaterLiters, occupancyTimestamp, affectedEquipmentIds);
            }

            _db.Batches.Add(batch);
            await _db.SaveChangesAsync();
        }
        else
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                affectedEquipmentIds.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                try
                {
                    if (!isHistorical)
                    {
                        BatchVesselReconciler.OccupyInitialMashVessels(
                            hlt, mashTun, boiler, plannedVolume,
                            volumeResult.StrikeWaterLiters, volumeResult.SpargeWaterLiters, occupancyTimestamp, affectedEquipmentIds);
                    }

                    _db.Batches.Add(batch);
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Failed to start batch for user {UserId}", userId);
                    throw;
                }
            });
        }

        await BroadcastEquipmentVolumeUpdatesAsync(affectedEquipmentIds, occupancyTimestamp);

        string? boilerSyncStatus = null;
        string? boilerSyncWarning = null;

        if (!isHistorical && request.SendRecipeToBoiler && batch.BoilerId.HasValue && _brewLinkService != null)
        {
            if (boiler?.ConnectionType == EquipmentConnectionType.BrewLink)
            {
                try
                {
                    var (sendResult, _, sendErr) = await _brewLinkService.SendRecipeToEquipmentAsync(
                        userId,
                        batch.BoilerId.Value,
                        new SendBrewLinkRecipeRequest(null, batch.Id));

                    if (sendResult == BrewLinkSendResult.Success)
                    {
                        boilerSyncStatus = "Sent";
                    }
                    else
                    {
                        boilerSyncStatus = "OfflineFallback";
                        boilerSyncWarning = sendErr ?? $"Could not send recipe to boiler ({sendResult}). Batch created in manual/offline mode.";
                        _logger.LogWarning(
                            "BrewLink recipe push for new batch {BatchId} on boiler {BoilerId} returned {SendResult}: {Error}",
                            batch.Id, batch.BoilerId.Value, sendResult, boilerSyncWarning);
                    }
                }
                catch (Exception ex)
                {
                    boilerSyncStatus = "OfflineFallback";
                    boilerSyncWarning = "Boiler communication failed. Batch created in manual/offline mode.";
                    _logger.LogWarning(
                        ex,
                        "BrewLink recipe push failed for new batch {BatchId} on boiler {BoilerId}; continuing in offline mode",
                        batch.Id, batch.BoilerId.Value);
                }
            }
            else
            {
                boilerSyncStatus = "Skipped";
                boilerSyncWarning = "Selected boiler does not use BrewLink; batch created in manual mode.";
            }
        }

        return (BatchAccessResult.Success, MapToDetailDto(batch, boilerSyncStatus, boilerSyncWarning), null);
    }

    public async Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> AdvanceStageAsync(
        Guid id, AdvanceBatchStageRequest request, string userId)
    {
        var affectedEquipmentIds = new HashSet<Guid>();
        var broadcastTimestamp = UtcNow;

        async Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> ExecuteAdvanceAsync()
        {
            affectedEquipmentIds.Clear();
            var batch = await QueryBatchWithDetails()
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

            if (batch == null)
            {
                return (BatchAccessResult.NotFound, null, "Batch not found.");
            }

            if (batch.Status == BatchStatus.Completed || batch.Status == BatchStatus.Archived)
            {
                return (BatchAccessResult.Conflict, null, "Cannot advance stage of a completed or archived batch.");
            }

            var oldStage = batch.CurrentStage;
            var newStage = request.TargetStage;

            if (newStage < oldStage || (newStage == oldStage && oldStage != BrewStage.Package))
            {
                return (BatchAccessResult.Conflict, null, "Cannot move batch backward to a previous stage.");
            }

            var isHistorical = batch.IsHistorical;

            if (oldStage == BrewStage.Mash && newStage > BrewStage.Mash)
            {
                var mashTun = batch.MashTun;
                var boiler = batch.Boiler;
                var isMashControlledByBrewLink = (mashTun != null && mashTun.ConnectionType == EquipmentConnectionType.BrewLink) ||
                                                 (boiler != null && boiler.ConnectionType == EquipmentConnectionType.BrewLink);

                if (!isHistorical && !request.Force && isMashControlledByBrewLink && batch.MashSteps.Count > 0 && batch.MashSteps.Any(s => !s.IsCompleted))
                {
                    return (BatchAccessResult.Conflict, null,
                        "Cannot advance stage: BrewLink mash process is still in progress with incomplete mash steps.");
                }
            }
            else if (oldStage == BrewStage.Boil && newStage > BrewStage.Boil)
            {
                var boiler = batch.Boiler;
                var isBoilControlledByBrewLink = boiler != null && boiler.ConnectionType == EquipmentConnectionType.BrewLink;

                if (!isHistorical && isBoilControlledByBrewLink && !request.Force && !string.IsNullOrWhiteSpace(boiler?.LatestMetricsJson))
                {
                    var canonical = BrewLinkTelemetryParser.FromMetricsJson(boiler.LatestMetricsJson);
                    if (canonical?.Timer != null && (canonical.Timer.Active || (canonical.Timer.RemainingSeconds.HasValue && canonical.Timer.RemainingSeconds.Value > 0)))
                    {
                        return (BatchAccessResult.Conflict, null,
                            "Cannot advance stage: BrewLink boil process is still in progress. Confirm to force advance.");
                    }
                }
            }

            Equipment? selectedFermenter = null;
            var fermenterSpecified = request.FermenterId.HasValue && newStage is BrewStage.Ferment or BrewStage.Condition;
            if (fermenterSpecified && request.FermenterId!.Value != Guid.Empty)
            {
                selectedFermenter = await GetAccessibleEquipment(userId)
                    .FirstOrDefaultAsync(e => e.Id == request.FermenterId.Value);

                if (selectedFermenter == null)
                {
                    return (BatchAccessResult.NotFound, null, "Fermenter not found.");
                }

                if (IsIneligibleVesselEquipment(selectedFermenter))
                {
                    return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as fermenters.");
                }
            }

            Equipment? selectedFermentationSensor = null;
            var fermentationSensorSpecified = request.FermentationSensorId.HasValue && newStage is BrewStage.Ferment or BrewStage.Condition;
            if (fermentationSensorSpecified && request.FermentationSensorId!.Value != Guid.Empty)
            {
                selectedFermentationSensor = await GetAccessibleEquipment(userId)
                    .FirstOrDefaultAsync(e => e.Id == request.FermentationSensorId.Value);

                if (selectedFermentationSensor == null)
                {
                    return (BatchAccessResult.NotFound, null, "Specified sensor equipment was not found or is not accessible.");
                }

                if (!IsEligibleBatchTelemetrySensor(selectedFermentationSensor))
                {
                    var message = selectedFermentationSensor.Subtype == EquipmentSubtype.BrewLink
                        ? "BrewLink gateway equipment cannot be assigned as a batch telemetry sensor."
                        : $"Equipment '{selectedFermentationSensor.Name}' is not a valid telemetry sensor. Only iSpindel, Tilt, and Generic Sensor equipment can be assigned.";
                    return (BatchAccessResult.ValidationFailed, null, message);
                }

                if (!isHistorical)
                {
                    var sensorOccupied = await _db.BatchSensorAssignments
                        .AsNoTracking()
                        .AnyAsync(existing =>
                            existing.EquipmentId == selectedFermentationSensor.Id &&
                            existing.BatchId != batch.Id &&
                            !existing.Batch.IsHistorical &&
                            (existing.Batch.Status == BatchStatus.Brewing ||
                             existing.Batch.Status == BatchStatus.Fermenting ||
                             existing.Batch.Status == BatchStatus.Conditioning) &&
                            (existing.Stage == null || existing.Stage == BrewStage.Ferment || existing.Stage == newStage));

                    if (sensorOccupied)
                    {
                        return (BatchAccessResult.Conflict, null, $"Sensor '{selectedFermentationSensor.Name}' is currently assigned to another active batch.");
                    }
                }
            }

            Equipment? selectedPackagingVessel = null;
            var packagingVesselSpecified = request.PackagingVesselId.HasValue;
            if (newStage == BrewStage.Package && packagingVesselSpecified)
            {
                if (request.PackagingVesselId!.Value != Guid.Empty)
                {
                    selectedPackagingVessel = await GetAccessibleEquipment(userId)
                        .FirstOrDefaultAsync(e => e.Id == request.PackagingVesselId.Value);

                    if (selectedPackagingVessel == null)
                    {
                        return (BatchAccessResult.NotFound, null, "Packaging vessel not found.");
                    }

                    if (selectedPackagingVessel.Type is EquipmentType.Sensor or EquipmentType.Tap ||
                         selectedPackagingVessel.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor or EquipmentSubtype.BrewLink)
                    {
                        return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as packaging vessels.");
                    }
                }
            }

            var targetFermenter = fermenterSpecified ? selectedFermenter : batch.Fermenter;
            if (!isHistorical)
            {
                if (newStage == BrewStage.Boil && batch.Boiler != null &&
                    await IsEquipmentOccupiedByOtherBatchAsync(batch.Boiler, batch.Id))
                {
                    return (BatchAccessResult.Conflict, null, $"Equipment '{batch.Boiler.Name}' is currently occupied by another active batch.");
                }

                if ((oldStage is BrewStage.Mash or BrewStage.Boil || (fermenterSpecified && targetFermenter?.Id != batch.FermenterId)) &&
                    newStage is BrewStage.Ferment or BrewStage.Condition &&
                    targetFermenter != null &&
                    await IsEquipmentOccupiedByOtherBatchAsync(targetFermenter, batch.Id))
                {
                    return (BatchAccessResult.Conflict, null, $"Equipment '{targetFermenter.Name}' is currently occupied by another active batch.");
                }

                if (newStage == BrewStage.Package)
                {
                    var targetPackagingVessel = packagingVesselSpecified ? selectedPackagingVessel : batch.PackagingVessel;
                    if (targetPackagingVessel != null &&
                        await IsEquipmentOccupiedByOtherBatchAsync(targetPackagingVessel, batch.Id))
                    {
                        return (BatchAccessResult.Conflict, null, $"Equipment '{targetPackagingVessel.Name}' is currently assigned to or occupied by another batch.");
                    }
                }
            }

            var now = UtcNow;
            var oldStatus = batch.Status;
            var oldFermenter = batch.Fermenter;
            var oldPackagingVessel = batch.PackagingVessel;

            // Apply optional measurements before transition so volume transfers use updated batch size
            if (request.MeasuredOg.HasValue) batch.MeasuredOg = request.MeasuredOg.Value;
            if (request.MeasuredBatchSizeLiters.HasValue) batch.MeasuredBatchSizeLiters = request.MeasuredBatchSizeLiters.Value;
            if (request.PitchTemperatureC.HasValue) batch.PitchTemperatureC = request.PitchTemperatureC.Value;
            if (request.MeasuredFg.HasValue)
            {
                batch.MeasuredFg = request.MeasuredFg.Value;
                batch.CurrentGravity = request.MeasuredFg.Value;

                var effectiveOg = batch.MeasuredOg ?? (batch.TargetOg > 1.000m ? batch.TargetOg : (decimal?)null);
                if (effectiveOg.HasValue && effectiveOg.Value > batch.CurrentGravity.Value)
                {
                    batch.AlcoholByVolume = BrewingCalculator.CalculateActualAbv(effectiveOg.Value, batch.CurrentGravity.Value);
                }
            }
            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                batch.Notes = string.IsNullOrEmpty(batch.Notes)
                    ? request.Notes.Trim()
                    : $"{batch.Notes}\n[{newStage} - {now:g}]: {request.Notes.Trim()}";
            }

            if (fermenterSpecified)
            {
                batch.FermenterId = selectedFermenter?.Id;
                batch.Fermenter = selectedFermenter;
                if (selectedFermenter != null && selectedFermenter.BrewerySetupId != Guid.Empty)
                {
                    batch.BrewerySetupId = selectedFermenter.BrewerySetupId;
                    batch.BrewerySetup = selectedFermenter.BrewerySetup;
                }
            }

            if (fermentationSensorSpecified)
            {
                var targetSensorStage = newStage == BrewStage.Condition ? BrewStage.Condition : BrewStage.Ferment;
                var existingFermSensors = batch.SensorAssignments
                    .Where(sa => sa.Stage == targetSensorStage || sa.Stage == null)
                    .ToList();
                if (existingFermSensors.Count > 0)
                {
                    _db.BatchSensorAssignments.RemoveRange(existingFermSensors);
                    foreach (var removed in existingFermSensors)
                    {
                        batch.SensorAssignments.Remove(removed);
                    }
                }

                if (selectedFermentationSensor != null)
                {
                    var newAssignment = new BatchSensorAssignment
                    {
                        BatchId = batch.Id,
                        EquipmentId = selectedFermentationSensor.Id,
                        Equipment = selectedFermentationSensor,
                        Stage = targetSensorStage
                    };
                    batch.SensorAssignments.Add(newAssignment);
                    _db.BatchSensorAssignments.Add(newAssignment);
                }
            }

            if (newStage == BrewStage.Package && packagingVesselSpecified)
            {
                batch.PackagingVesselId = selectedPackagingVessel?.Id;
                batch.PackagingVessel = selectedPackagingVessel;
                if (selectedPackagingVessel != null && selectedPackagingVessel.BrewerySetupId != Guid.Empty)
                {
                    batch.BrewerySetupId = selectedPackagingVessel.BrewerySetupId;
                    batch.BrewerySetup = selectedPackagingVessel.BrewerySetup;
                }
            }

            // Update Volume Profile with stage-specific measurements (and swapped vessel losses)
            if (batch.VolumeProfile != null)
            {
                if (fermenterSpecified && oldFermenter?.Id != batch.FermenterId)
                {
                    var setup = batch.BrewerySetup ?? (batch.BrewerySetupId.HasValue
                        ? await _db.BrewerySetups.AsNoTracking().FirstOrDefaultAsync(s => s.Id == batch.BrewerySetupId.Value)
                        : null);
                    batch.VolumeProfile.FermenterTrubLossLiters = batch.Fermenter?.TrubLossLiters ?? setup?.DefaultFermenterLossLiters ?? 1.5m;
                }
                if (newStage == BrewStage.Package && packagingVesselSpecified && oldPackagingVessel?.Id != batch.PackagingVesselId)
                {
                    var setup = batch.BrewerySetup ?? (batch.BrewerySetupId.HasValue
                        ? await _db.BrewerySetups.AsNoTracking().FirstOrDefaultAsync(s => s.Id == batch.BrewerySetupId.Value)
                        : null);
                    batch.VolumeProfile.PackagingLossLiters = batch.PackagingVessel?.PackagingLossLiters ?? setup?.DefaultPackagingLossLiters ?? 0.5m;
                }

                if (request.MeasuredPreBoilVolumeLiters.HasValue)
                    batch.VolumeProfile.MeasuredPreBoilVolumeLiters = request.MeasuredPreBoilVolumeLiters.Value;
                if (request.MeasuredPreBoilGravity.HasValue)
                    batch.VolumeProfile.MeasuredPreBoilGravity = request.MeasuredPreBoilGravity.Value;
                if (request.MeasuredPostBoilVolumeLiters.HasValue)
                    batch.VolumeProfile.MeasuredPostBoilVolumeLiters = request.MeasuredPostBoilVolumeLiters.Value;
                if (request.MeasuredBatchSizeLiters.HasValue)
                    batch.VolumeProfile.MeasuredFermenterVolumeLiters = request.MeasuredBatchSizeLiters.Value;
                if (request.MeasuredPackagedVolumeLiters.HasValue)
                    batch.VolumeProfile.MeasuredPackagedVolumeLiters = request.MeasuredPackagedVolumeLiters.Value;
                VolumeCalculator.RecalculatePostStepMilestones(batch.VolumeProfile, batch.BoilTimeMinutes);
                batch.VolumeProfile.UpdatedAt = now;
            }

            var targetStatus = newStage switch
            {
                BrewStage.Mash or BrewStage.Boil => BatchStatus.Brewing,
                BrewStage.Ferment => BatchStatus.Fermenting,
                BrewStage.Condition => BatchStatus.Conditioning,
                BrewStage.Package => BatchStatus.Completed,
                _ => batch.Status
            };

            if (newStage == BrewStage.Package && packagingVesselSpecified && oldPackagingVessel?.Id != batch.PackagingVesselId)
            {
                affectedEquipmentIds.UnionWith(await BatchVesselReconciler.ApplyVesselSwapAsync(
                    _db, batch, BatchVesselSlot.PackagingVessel, oldPackagingVessel, batch.PackagingVessel, now, request.MeasuredPackagedVolumeLiters));
            }

            if (fermenterSpecified && oldFermenter?.Id != batch.FermenterId && oldStage is BrewStage.Ferment or BrewStage.Condition)
            {
                affectedEquipmentIds.UnionWith(await BatchVesselReconciler.ApplyVesselSwapAsync(
                    _db, batch, BatchVesselSlot.Fermenter, oldFermenter, batch.Fermenter, now));
            }

            affectedEquipmentIds.UnionWith(await BatchVesselReconciler.ApplyTransitionAsync(
                _db,
                batch,
                oldStatus,
                oldStage,
                targetStatus,
                newStage,
                wasHistorical: isHistorical,
                now: now,
                measuredPreBoilVolumeLiters: request.MeasuredPreBoilVolumeLiters,
                measuredPackagedVolumeLiters: request.MeasuredPackagedVolumeLiters));
            broadcastTimestamp = now;

            if (oldStage is BrewStage.Mash or BrewStage.Boil && newStage is BrewStage.Ferment or BrewStage.Condition)
            {
                if (batch.MeasuredOg.HasValue)
                {
                    var vol = batch.MeasuredBatchSizeLiters ?? batch.TargetBatchSizeLiters;
                    var totalPotentialPoints = await CalculateTotalGrainPotentialPointsAsync(batch.Ingredients, userId);
                    batch.BrewhouseEfficiency = BrewingCalculator.CalculateBrewhouseEfficiency(vol, batch.MeasuredOg.Value, totalPotentialPoints);
                }
            }
            else if (newStage == BrewStage.Package)
            {
                var effectiveOg = batch.MeasuredOg ?? (batch.TargetOg > 1.000m ? batch.TargetOg : (decimal?)null);
                if (effectiveOg.HasValue && batch.CurrentGravity.HasValue && effectiveOg.Value > batch.CurrentGravity.Value)
                {
                    batch.AlcoholByVolume = BrewingCalculator.CalculateActualAbv(effectiveOg.Value, batch.CurrentGravity.Value);
                }
            }

            // Auto-deduct ingredients for oldStage and any skipped stages that haven't been deducted yet
            List<BatchIngredient> stageIngredients;
            if (newStage == BrewStage.Package)
            {
                stageIngredients = batch.Ingredients.Where(bi => !bi.IsDeducted).ToList();
            }
            else
            {
                stageIngredients = [];
                for (var stageVal = (int)oldStage; stageVal < (int)newStage; stageVal++)
                {
                    if (!Enum.IsDefined(typeof(BrewStage), stageVal)) continue;
                    stageIngredients.AddRange(GetIngredientsForStage(batch.Ingredients, (BrewStage)stageVal));
                }
                stageIngredients = stageIngredients.DistinctBy(bi => bi.Id).ToList();
            }

            foreach (var si in stageIngredients)
            {
                if (!si.IsDeducted)
                {
                    await DeductIngredientStockAsync(userId, si);
                    si.IsDeducted = true;
                    si.DeductedAt = now;
                    si.IsChecked = true;
                }
            }

            batch.CurrentStage = newStage;
            batch.UpdatedAt = now;

            await _db.SaveChangesAsync();
            return (BatchAccessResult.Success, MapToDetailDto(batch), null);
        }

        (BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage) advanceOutcome;
        if (_db.Database.IsInMemory())
        {
            advanceOutcome = await ExecuteAdvanceAsync();
        }
        else
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            advanceOutcome = await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                try
                {
                    var result = await ExecuteAdvanceAsync();
                    if (result.Result == BatchAccessResult.Success)
                    {
                        await tx.CommitAsync();
                    }
                    else
                    {
                        await tx.RollbackAsync();
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Failed to advance stage for batch {BatchId}", id);
                    throw;
                }
            });
        }

        if (advanceOutcome.Result == BatchAccessResult.Success)
        {
            await BroadcastEquipmentVolumeUpdatesAsync(affectedEquipmentIds, broadcastTimestamp);
        }

        return advanceOutcome;
    }

    public async Task<(BatchAccessResult Result, BatchReadingDto? Reading, string? ErrorMessage)> AddReadingAsync(
        Guid id, AddBatchReadingRequest request, string userId)
    {
        var batch = await _db.Batches
            .Include(b => b.Readings)
            .Include(b => b.Hlt)
            .Include(b => b.MashTun)
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Include(b => b.PackagingVessel)
            .Include(b => b.MashSteps)
            .Include(b => b.SensorAssignments)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch not found.");
        }

        if (batch.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Cannot add readings to an archived batch.");
        }

        if (request.SpecificGravity < 0.900m || request.SpecificGravity > 1.250m)
        {
            return (BatchAccessResult.ValidationFailed, null, "Specific gravity must be between 0.900 and 1.250.");
        }

        if (request.TemperatureC.HasValue && (request.TemperatureC.Value < -20.0m || request.TemperatureC.Value > 120.0m))
        {
            return (BatchAccessResult.ValidationFailed, null, "Temperature must be between -20°C and 120°C.");
        }

        if (request.Stage.HasValue && !Enum.IsDefined(typeof(BrewStage), request.Stage.Value))
        {
            return (BatchAccessResult.ValidationFailed, null, "Invalid brew stage.");
        }

        if (request.EquipmentId.HasValue && request.EquipmentId.Value == Guid.Empty)
        {
            return (BatchAccessResult.ValidationFailed, null, "Equipment ID cannot be empty.");
        }

        if (request.BatchMashStepId.HasValue && request.BatchMashStepId.Value == Guid.Empty)
        {
            return (BatchAccessResult.ValidationFailed, null, "Mash step ID cannot be empty.");
        }

        if (request.Notes != null && request.Notes.Length > 500)
        {
            return (BatchAccessResult.ValidationFailed, null, "Reading notes cannot exceed 500 characters.");
        }

        if (batch.Readings.Count >= MaxReadingsPerBatch)
        {
            return (BatchAccessResult.QuotaExceeded, null, $"Max readings limit ({MaxReadingsPerBatch}) reached for this batch.");
        }

        Equipment? targetEquipment = null;
        if (request.EquipmentId.HasValue)
        {
            targetEquipment = await GetAccessibleEquipment(userId)
                .FirstOrDefaultAsync(e => e.Id == request.EquipmentId.Value);

            if (targetEquipment == null)
            {
                return (BatchAccessResult.NotFound, null, "Specified equipment not found or not accessible to user.");
            }

            if (targetEquipment.Type == EquipmentType.Tap || targetEquipment.Subtype == EquipmentSubtype.BrewLink)
            {
                return (BatchAccessResult.ValidationFailed, null, "Taps and BrewLink gateways cannot be used for batch telemetry readings.");
            }

            if (BatchLifecycle.OccupiesLiveEquipment(batch) &&
                await IsEquipmentOccupiedByOtherBatchAsync(targetEquipment, batch.Id))
            {
                return (BatchAccessResult.Conflict, null, "Specified equipment is currently occupied by another active batch.");
            }
        }

        BatchMashStep? resolvedStep = null;
        if (request.BatchMashStepId.HasValue)
        {
            resolvedStep = batch.MashSteps.FirstOrDefault(s => s.Id == request.BatchMashStepId.Value);
            if (resolvedStep == null)
            {
                return (BatchAccessResult.NotFound, null, "Specified mash step does not belong to this batch.");
            }
        }

        var resolvedStage = request.Stage ?? (resolvedStep != null ? BrewStage.Mash : batch.CurrentStage);

        if (targetEquipment == null && (request.TemperatureC.HasValue || request.Stage.HasValue || request.BatchMashStepId.HasValue))
        {
            targetEquipment = resolvedStage switch
            {
                BrewStage.Mash => batch.MashTun ?? batch.Boiler,
                BrewStage.Boil => batch.Boiler,
                BrewStage.Ferment or BrewStage.Condition => batch.Fermenter ?? batch.Boiler,
                BrewStage.Package => batch.PackagingVessel ?? batch.Fermenter,
                _ => null
            };
        }

        var effectiveSg = request.CalibrateHydrometerTemp && request.TemperatureC.HasValue
            ? BrewingCalculator.CorrectHydrometerReading(request.SpecificGravity, request.TemperatureC.Value)
            : request.SpecificGravity;

        var readingTimestamp = ClampReadingTimestamp(request.Timestamp, batch.BrewDate);
        var isLatestReading = batch.Readings.Count == 0 ||
            readingTimestamp >= batch.Readings.Max(r => r.Timestamp);

        var reading = new BatchReading
        {
            BatchId = batch.Id,
            Timestamp = readingTimestamp,
            SpecificGravity = effectiveSg,
            TemperatureC = request.TemperatureC,
            Notes = request.Notes?.Trim()
        };

        batch.Readings.Add(reading);

        var effectiveOg = batch.MeasuredOg ?? (batch.TargetOg > 1.000m ? batch.TargetOg : (decimal?)null);
        decimal? readingAbv = effectiveOg.HasValue
            ? BrewingCalculator.CalculateActualAbv(effectiveOg.Value, effectiveSg)
            : null;

        if (isLatestReading)
        {
            batch.CurrentGravity = effectiveSg;
            if (readingAbv.HasValue)
            {
                batch.AlcoholByVolume = readingAbv.Value;
            }

            // If conditioning or packaging, also update MeasuredFg
            if (batch.CurrentStage is BrewStage.Condition or BrewStage.Package)
            {
                batch.MeasuredFg = effectiveSg;
            }
        }

        var nowUtc = UtcNow;
        if (targetEquipment != null &&
            BatchLifecycle.OccupiesLiveEquipment(batch) &&
            (IsEquipmentLinkedToBatch(batch, targetEquipment.Id) || targetEquipment.AssignedBatchId == batch.Id) &&
            !await IsEquipmentOccupiedByOtherBatchAsync(targetEquipment, batch.Id))
        {
            targetEquipment.CurrentSpecificGravity = effectiveSg;
            if (request.TemperatureC.HasValue && resolvedStage == batch.CurrentStage)
            {
                targetEquipment.CurrentTemperatureC = request.TemperatureC.Value;
                targetEquipment.TemperatureUpdatedAt = readingTimestamp;
            }
            targetEquipment.UpdatedAt = nowUtc;
        }

        EquipmentReading? equipmentReading = null;
        if (targetEquipment != null && request.TemperatureC.HasValue)
        {
            equipmentReading = new EquipmentReading
            {
                EquipmentId = targetEquipment.Id,
                BatchId = batch.Id,
                Stage = resolvedStage,
                BatchMashStepId = resolvedStep?.Id,
                StepName = resolvedStep?.Name,
                TemperatureC = request.TemperatureC.Value,
                SpecificGravity = effectiveSg,
                Timestamp = readingTimestamp,
                Source = "Manual",
                Notes = request.Notes?.Trim(),
                CreatedAt = nowUtc
            };
            _db.EquipmentReadings.Add(equipmentReading);
        }

        batch.UpdatedAt = nowUtc;

        _db.BatchReadings.Add(reading);
        await _db.SaveChangesAsync();

        if (request.TemperatureC.HasValue)
        {
            var sseDto = new BatchEquipmentReadingDto(
                Id: equipmentReading?.Id ?? reading.Id,
                EquipmentId: targetEquipment?.Id ?? Guid.Empty,
                EquipmentName: targetEquipment?.Name ?? "Manual",
                BatchId: batch.Id,
                Stage: resolvedStage,
                BatchMashStepId: resolvedStep?.Id,
                StepName: resolvedStep?.Name,
                TemperatureC: request.TemperatureC.Value,
                Timestamp: reading.Timestamp,
                Source: "Manual",
                TargetTemperatureC: resolvedStep?.TargetTemperatureC,
                Notes: reading.Notes,
                SpecificGravity: effectiveSg
            );
            _broadcastService.BroadcastReading(sseDto);

            if (targetEquipment != null && equipmentReading != null)
            {
                _broadcastService.BroadcastEquipmentReading(userId, new EquipmentTelemetryUpdateDto(
                    EquipmentId: targetEquipment.Id,
                    EquipmentName: targetEquipment.Name,
                    TemperatureC: request.TemperatureC.Value,
                    Timestamp: reading.Timestamp,
                    Source: "Manual",
                    BatchId: batch.Id,
                    SpecificGravity: effectiveSg
                ));
            }
        }

        var dto = new BatchReadingDto(
            reading.Id,
            reading.BatchId,
            reading.Timestamp,
            reading.SpecificGravity,
            reading.TemperatureC,
            reading.Notes,
            reading.CreatedAt,
            readingAbv
        );

        return (BatchAccessResult.Success, dto, null);
    }

    public async Task<(BatchAccessResult Result, BatchIngredientDto? Ingredient, string? ErrorMessage)> ToggleIngredientAsync(
        Guid batchId, Guid ingredientId, bool isChecked, string userId)
    {
        var ingredient = await _db.BatchIngredients
            .Include(bi => bi.Batch)
            .FirstOrDefaultAsync(bi => bi.Id == ingredientId && bi.BatchId == batchId && bi.Batch!.UserId == userId);

        if (ingredient == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch ingredient not found.");
        }

        if (ingredient.Batch!.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Cannot modify ingredients on archived batches.");
        }

        ingredient.IsChecked = isChecked;
        if (isChecked)
        {
            if (!ingredient.IsDeducted)
            {
                await DeductIngredientStockAsync(userId, ingredient);
                ingredient.IsDeducted = true;
                ingredient.DeductedAt = DateTime.UtcNow;
            }
        }
        else
        {
            if (ingredient.IsDeducted)
            {
                await RefundIngredientStockAsync(userId, ingredient);
                ingredient.IsDeducted = false;
                ingredient.DeductedAt = null;
            }
        }

        ingredient.Batch!.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = new BatchIngredientDto(
            ingredient.Id,
            ingredient.BatchId,
            ingredient.SourceIngredientId,
            ingredient.Name,
            ingredient.Type,
            ingredient.Amount,
            ingredient.Unit,
            ingredient.AdditionStage,
            ingredient.AdditionTimeMinutes,
            ingredient.IsChecked,
            ingredient.IsDeducted,
            ingredient.Notes,
            ingredient.Form,
            ingredient.FermentableCategory,
            ingredient.FermentabilityPercent
        );

        return (BatchAccessResult.Success, dto, null);
    }

    public async Task<(BatchAccessResult Result, BatchMashStepDto? Step, string? ErrorMessage)> ToggleMashStepAsync(
        Guid batchId, Guid stepId, ToggleBatchMashStepRequest request, string userId)
    {
        var step = await _db.BatchMashSteps
            .Include(s => s.Batch)
                .ThenInclude(b => b!.Ingredients)
            .Include(s => s.Batch)
                .ThenInclude(b => b!.MashSteps)
            .Include(s => s.Batch)
                .ThenInclude(b => b!.MashTun)
            .Include(s => s.Batch)
                .ThenInclude(b => b!.Boiler)
            .FirstOrDefaultAsync(s => s.Id == stepId && s.BatchId == batchId && s.Batch!.UserId == userId);

        if (step == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch mash step not found.");
        }

        if (step.Batch!.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Cannot modify mash steps on archived batches.");
        }

        step.IsCompleted = request.IsCompleted;
        step.CompletedAt = request.IsCompleted ? DateTime.UtcNow : null;
        if (request.ActualTemperatureC.HasValue)
            step.ActualTemperatureC = request.ActualTemperatureC.Value;
        if (request.ActualDurationMinutes.HasValue)
            step.ActualDurationMinutes = request.ActualDurationMinutes.Value;
        if (!string.IsNullOrWhiteSpace(request.Notes))
            step.Notes = request.Notes.Trim();

        if (request.IsCompleted && step.Batch.Ingredients.Count > 0)
        {
            var mashIngredients = step.Batch.Ingredients
                .Where(bi => !bi.IsDeducted && bi.AdditionStage == IngredientUsage.Mash)
                .ToList();

            foreach (var mi in mashIngredients)
            {
                await DeductIngredientStockAsync(userId, mi);
                mi.IsDeducted = true;
                mi.DeductedAt = DateTime.UtcNow;
                mi.IsChecked = true;
            }
        }

        step.Batch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var isMashControlledByBrewLink =
            (step.Batch.MashTun != null && step.Batch.MashTun.ConnectionType == EquipmentConnectionType.BrewLink) ||
            (step.Batch.Boiler != null && step.Batch.Boiler.ConnectionType == EquipmentConnectionType.BrewLink);

        if (request.IsCompleted &&
            step.Batch.Status != BatchStatus.Completed &&
            step.Batch.Status != BatchStatus.Archived &&
            step.Batch.CurrentStage == BrewStage.Mash &&
            isMashControlledByBrewLink &&
            step.Batch.MashSteps.All(s => s.IsCompleted))
        {
            var primaryVessel = step.Batch.MashTun ?? step.Batch.Boiler;
            var interactionRequired = false;
            if (!step.Batch.IsHistorical && !string.IsNullOrWhiteSpace(primaryVessel?.LatestMetricsJson))
            {
                var canonical = BrewLinkTelemetryParser.FromMetricsJson(primaryVessel.LatestMetricsJson);
                interactionRequired = canonical?.Interaction?.Required == true;
            }

            if (!interactionRequired)
            {
                await AdvanceStageAsync(step.Batch.Id, new AdvanceBatchStageRequest(BrewStage.Boil), userId);
            }
        }

        var dto = new BatchMashStepDto(
            step.Id,
            step.BatchId,
            step.RecipeMashStepId,
            step.StepOrder,
            step.Name,
            step.Type,
            step.TargetTemperatureC,
            step.DurationMinutes,
            step.RampTimeMinutes,
            step.InfuseAmountLiters,
            step.ActualTemperatureC,
            step.ActualDurationMinutes,
            step.IsCompleted,
            step.CompletedAt,
            step.Notes
        );

        return (BatchAccessResult.Success, dto, null);
    }

    public async Task<(BatchAccessResult Result, BatchFermentationStepDto? Step, string? ErrorMessage)> ToggleFermentationStepAsync(
        Guid batchId, Guid stepId, ToggleBatchFermentationStepRequest request, string userId)
    {
        var step = await _db.BatchFermentationSteps
            .Include(s => s.Batch)
                .ThenInclude(b => b!.FermentationSteps)
            .FirstOrDefaultAsync(s => s.Id == stepId && s.BatchId == batchId && s.Batch!.UserId == userId);

        if (step == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch fermentation step not found.");
        }

        if (step.Batch!.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Cannot modify fermentation steps on archived batches.");
        }

        var now = DateTime.UtcNow;
        step.IsCompleted = request.IsCompleted;
        step.CompletedAt = request.IsCompleted ? now : null;
        if (request.IsCompleted)
        {
            step.StartedAt ??= now;
            var nextStep = step.Batch.FermentationSteps
                .OrderBy(fs => fs.StepOrder)
                .FirstOrDefault(fs => !fs.IsCompleted && fs.Id != step.Id);
            if (nextStep != null)
            {
                nextStep.StartedAt ??= now;
            }
        }
        if (request.ActualTemperatureC.HasValue)
            step.ActualTemperatureC = request.ActualTemperatureC.Value;
        if (!string.IsNullOrWhiteSpace(request.Notes))
            step.Notes = request.Notes.Trim();

        step.Batch.UpdatedAt = now;
        await _db.SaveChangesAsync();

        var dto = new BatchFermentationStepDto(
            step.Id,
            step.BatchId,
            step.RecipeFermentationStepId,
            step.StepOrder,
            step.Name,
            step.Type,
            step.TargetTemperatureC,
            step.ActualTemperatureC,
            step.DurationDays,
            step.RampTimeHours,
            step.TriggerGravity,
            step.IsCompleted,
            step.StartedAt,
            step.CompletedAt,
            step.Notes
        );

        return (BatchAccessResult.Success, dto, null);
    }

    private static bool IsIneligibleVesselEquipment(Equipment eq) =>
        eq.Type is EquipmentType.Sensor or EquipmentType.Tap ||
        eq.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor or EquipmentSubtype.BrewLink;

    public async Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> UpdateBatchAsync(
        Guid id, UpdateBatchRequest request, string userId)
    {
        var affectedEquipmentIds = new HashSet<Guid>();
        (BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage) updateOutcome;

        if (_db.Database.IsInMemory())
        {
            updateOutcome = await ExecuteUpdateBatchCoreAsync(id, request, userId, affectedEquipmentIds);
        }
        else
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            updateOutcome = await strategy.ExecuteAsync(async () =>
            {
                affectedEquipmentIds.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                try
                {
                    var result = await ExecuteUpdateBatchCoreAsync(id, request, userId, affectedEquipmentIds);
                    if (result.Result == BatchAccessResult.Success)
                    {
                        await tx.CommitAsync();
                    }
                    else
                    {
                        await tx.RollbackAsync();
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Failed to update batch {BatchId}", id);
                    throw;
                }
            });
        }

        if (updateOutcome.Result == BatchAccessResult.Success)
        {
            await BroadcastEquipmentVolumeUpdatesAsync(affectedEquipmentIds, UtcNow);
        }

        return updateOutcome;
    }

    private async Task<(BatchAccessResult Result, BatchDetailDto? Batch, string? ErrorMessage)> ExecuteUpdateBatchCoreAsync(
        Guid id, UpdateBatchRequest request, string userId, HashSet<Guid> affectedEquipmentIds)
    {
        var batch = await QueryBatchWithDetails()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch not found.");
        }

        if (batch.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Archived batches cannot be modified.");
        }

        var now = UtcNow;
        var oldStatus = batch.Status;
        var oldIsHistorical = batch.IsHistorical;
        var effectiveStatus = request.Status ?? oldStatus;
        var targetIsHistorical = request.IsHistorical ?? oldIsHistorical;

        if (!oldIsHistorical && targetIsHistorical &&
            BatchLifecycle.IsLiveStatus(oldStatus) &&
            BatchLifecycle.IsLiveStatus(effectiveStatus))
        {
            return (BatchAccessResult.Conflict, null, "Cannot mark an active in-progress batch as historical. Complete, archive, or move it to Planned first.");
        }

        var oldHlt = batch.Hlt;
        var oldMashTun = batch.MashTun;
        var oldBoiler = batch.Boiler;
        var oldFermenter = batch.Fermenter;
        var oldPackagingVessel = batch.PackagingVessel;

        // Pre-validate all requested vessel IDs before mutating entity state
        Equipment? newPackagingVessel = null;
        if (request.PackagingVesselId.HasValue && request.PackagingVesselId.Value != Guid.Empty)
        {
            newPackagingVessel = await GetAccessibleEquipment(userId).FirstOrDefaultAsync(e => e.Id == request.PackagingVesselId.Value);
            if (newPackagingVessel == null)
            {
                return (BatchAccessResult.NotFound, null, "Packaging vessel not found.");
            }
            if (IsIneligibleVesselEquipment(newPackagingVessel))
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as packaging vessels.");
            }
        }

        Equipment? newHlt = null;
        if (request.HltId.HasValue && request.HltId.Value != Guid.Empty)
        {
            newHlt = await GetAccessibleEquipment(userId).FirstOrDefaultAsync(e => e.Id == request.HltId.Value);
            if (newHlt == null)
            {
                return (BatchAccessResult.NotFound, null, "HLT equipment not found.");
            }
            if (IsIneligibleVesselEquipment(newHlt))
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as hot liquor tanks.");
            }
        }

        Equipment? newMashTun = null;
        if (request.MashTunId.HasValue && request.MashTunId.Value != Guid.Empty)
        {
            newMashTun = await GetAccessibleEquipment(userId).FirstOrDefaultAsync(e => e.Id == request.MashTunId.Value);
            if (newMashTun == null)
            {
                return (BatchAccessResult.NotFound, null, "Mash tun equipment not found.");
            }
            if (IsIneligibleVesselEquipment(newMashTun))
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as mash tuns.");
            }
        }

        Equipment? newBoiler = null;
        if (request.BoilerId.HasValue && request.BoilerId.Value != Guid.Empty)
        {
            newBoiler = await GetAccessibleEquipment(userId).FirstOrDefaultAsync(e => e.Id == request.BoilerId.Value);
            if (newBoiler == null)
            {
                return (BatchAccessResult.NotFound, null, "Boiler not found.");
            }
            if (IsIneligibleVesselEquipment(newBoiler))
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as boilers.");
            }
        }

        Equipment? newFermenter = null;
        if (request.FermenterId.HasValue && request.FermenterId.Value != Guid.Empty)
        {
            newFermenter = await GetAccessibleEquipment(userId).FirstOrDefaultAsync(e => e.Id == request.FermenterId.Value);
            if (newFermenter == null)
            {
                return (BatchAccessResult.NotFound, null, "Fermenter not found.");
            }
            if (IsIneligibleVesselEquipment(newFermenter))
            {
                return (BatchAccessResult.ValidationFailed, null, "Sensors and taps cannot be used as fermenters.");
            }
        }

        List<BatchSensorAssignment>? validatedSensorAssignments = null;
        if (request.SensorAssignments != null)
        {
            var (sensorResult, assignments, sensorErr) = await ValidateAndBuildSensorAssignmentsAsync(
                batch,
                request.SensorAssignments,
                userId,
                checkOccupancyOverride: !targetIsHistorical && BatchLifecycle.IsLiveStatus(effectiveStatus));

            if (sensorResult != BatchAccessResult.Success)
            {
                return (sensorResult, null, sensorErr);
            }
            validatedSensorAssignments = assignments;
        }
        else if (oldIsHistorical && !targetIsHistorical && BatchLifecycle.IsLiveStatus(effectiveStatus) && batch.SensorAssignments.Count > 0)
        {
            foreach (var existingSa in batch.SensorAssignments)
            {
                var occupiedByOtherBatch = await _db.BatchSensorAssignments
                    .AsNoTracking()
                    .AnyAsync(existing =>
                        existing.EquipmentId == existingSa.EquipmentId &&
                        existing.BatchId != batch.Id &&
                        !existing.Batch.IsHistorical &&
                        (existing.Batch.Status == BatchStatus.Brewing ||
                         existing.Batch.Status == BatchStatus.Fermenting ||
                         existing.Batch.Status == BatchStatus.Conditioning) &&
                        (existing.Stage == null || existingSa.Stage == null || existing.Stage == existingSa.Stage));

                if (occupiedByOtherBatch)
                {
                    var sensorName = existingSa.Equipment?.Name ?? "Sensor";
                    return (BatchAccessResult.Conflict, null, $"Sensor '{sensorName}' is currently assigned to another active batch.");
                }
            }
        }

        var isReopeningFromCompleted = oldStatus == BatchStatus.Completed &&
            request.Status.HasValue &&
            request.Status.Value != BatchStatus.Completed &&
            request.Status.Value != BatchStatus.Archived;

        if (isReopeningFromCompleted)
        {
            var activeCount = await _db.Batches
                .CountAsync(b => b.UserId == userId && b.Status != BatchStatus.Completed && b.Status != BatchStatus.Archived);

            if (activeCount >= MaxActiveBatchesPerUser)
            {
                return (BatchAccessResult.QuotaExceeded, null, $"Active batch limit reached ({MaxActiveBatchesPerUser}). Complete or archive older batches.");
            }
        }

        var effectiveStage = batch.CurrentStage;
        if (request.Status.HasValue && request.Status.Value != oldStatus)
        {
            var targetStatus = request.Status.Value;
            if (targetStatus is BatchStatus.Planned or BatchStatus.Brewing)
            {
                if (oldStatus == BatchStatus.Completed || batch.CurrentStage > BrewStage.Boil)
                {
                    effectiveStage = BrewStage.Mash;
                }
            }
            else if (targetStatus == BatchStatus.Fermenting)
            {
                effectiveStage = BrewStage.Ferment;
            }
            else if (targetStatus == BatchStatus.Conditioning)
            {
                effectiveStage = BrewStage.Condition;
            }
            else if (targetStatus == BatchStatus.Completed)
            {
                effectiveStage = BrewStage.Package;
            }
        }

        var isRevivingFromHistorical = oldIsHistorical && !targetIsHistorical;
        var syncAioMashTunWithBoiler = request.BoilerId.HasValue &&
            !request.MashTunId.HasValue &&
            oldBoiler != null &&
            oldMashTun != null &&
            oldMashTun.Id == oldBoiler.Id;

        if (!targetIsHistorical && effectiveStatus != BatchStatus.Archived && effectiveStatus != BatchStatus.Planned)
        {
            var effectiveHlt = request.HltId.HasValue ? (request.HltId.Value == Guid.Empty ? null : newHlt) : batch.Hlt;
            var effectiveBoiler = request.BoilerId.HasValue ? (request.BoilerId.Value == Guid.Empty ? null : newBoiler) : batch.Boiler;
            var effectiveMashTun = syncAioMashTunWithBoiler
                ? effectiveBoiler
                : (request.MashTunId.HasValue ? (request.MashTunId.Value == Guid.Empty ? null : newMashTun) : batch.MashTun);
            var effectiveFermenter = request.FermenterId.HasValue ? (request.FermenterId.Value == Guid.Empty ? null : newFermenter) : batch.Fermenter;
            var effectivePackagingVessel = request.PackagingVesselId.HasValue ? (request.PackagingVesselId.Value == Guid.Empty ? null : newPackagingVessel) : batch.PackagingVessel;

            if (effectiveStatus != BatchStatus.Completed)
            {
                var vesselsToValidate = new List<Equipment?>();
                if (effectiveStage == BrewStage.Mash)
                {
                    vesselsToValidate.Add(effectiveHlt);
                    vesselsToValidate.Add(effectiveMashTun);
                    vesselsToValidate.Add(effectiveBoiler);
                }
                else if (effectiveStage == BrewStage.Boil)
                {
                    vesselsToValidate.Add(effectiveBoiler);
                }
                else if (effectiveStage is BrewStage.Ferment or BrewStage.Condition)
                {
                    vesselsToValidate.Add(effectiveFermenter);
                }
                else if (effectiveStage == BrewStage.Package)
                {
                    vesselsToValidate.Add(effectivePackagingVessel);
                }

                if (newHlt != null && newHlt.Id != batch.HltId) vesselsToValidate.Add(newHlt);
                if (newMashTun != null && newMashTun.Id != batch.MashTunId) vesselsToValidate.Add(newMashTun);
                if (newBoiler != null && newBoiler.Id != batch.BoilerId) vesselsToValidate.Add(newBoiler);
                if (newFermenter != null && newFermenter.Id != batch.FermenterId) vesselsToValidate.Add(newFermenter);

                foreach (var candidate in vesselsToValidate.Where(e => e != null).DistinctBy(e => e!.Id))
                {
                    if (await IsEquipmentOccupiedByOtherBatchAsync(candidate!, batch.Id))
                    {
                        return (BatchAccessResult.Conflict, null, $"Equipment '{candidate!.Name}' is currently occupied by another active batch.");
                    }
                }
            }

            if ((effectiveStage == BrewStage.Package || effectiveStatus == BatchStatus.Completed) &&
                effectivePackagingVessel != null &&
                (isRevivingFromHistorical || effectivePackagingVessel.Id != batch.PackagingVesselId || request.MeasuredPackagedVolumeLiters.HasValue || (oldStatus != BatchStatus.Completed && effectiveStatus == BatchStatus.Completed)) &&
                await IsEquipmentOccupiedByOtherBatchAsync(effectivePackagingVessel, batch.Id))
            {
                return (BatchAccessResult.Conflict, null, $"Equipment '{effectivePackagingVessel.Name}' is currently assigned to or occupied by another batch.");
            }
        }

        // Apply scalar updates
        if (!string.IsNullOrWhiteSpace(request.Name)) batch.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.BeerStyle)) batch.BeerStyle = request.BeerStyle.Trim();
        if (request.Notes != null) batch.Notes = request.Notes.Trim();
        if (request.BrewDate.HasValue) batch.BrewDate = request.BrewDate.Value;
        batch.IsHistorical = targetIsHistorical;
        if (request.MeasuredOg.HasValue) batch.MeasuredOg = request.MeasuredOg.Value;
        if (request.MeasuredFg.HasValue)
        {
            batch.MeasuredFg = request.MeasuredFg.Value;
            batch.CurrentGravity = request.MeasuredFg.Value;
        }
        if (request.MeasuredBatchSizeLiters.HasValue) batch.MeasuredBatchSizeLiters = request.MeasuredBatchSizeLiters.Value;
        if (request.PitchTemperatureC.HasValue) batch.PitchTemperatureC = request.PitchTemperatureC.Value;

        var packagingVesselChanged = false;
        if (request.PackagingVesselId.HasValue)
        {
            if (request.PackagingVesselId.Value == Guid.Empty)
            {
                if (batch.PackagingVesselId != null) packagingVesselChanged = true;
                batch.PackagingVesselId = null;
                batch.PackagingVessel = null;
            }
            else if (newPackagingVessel != null)
            {
                if (batch.PackagingVesselId != newPackagingVessel.Id) packagingVesselChanged = true;
                batch.PackagingVesselId = newPackagingVessel.Id;
                batch.PackagingVessel = newPackagingVessel;
            }
        }

        var hltChanged = false;
        if (request.HltId.HasValue)
        {
            if (request.HltId.Value == Guid.Empty)
            {
                if (batch.HltId != null) hltChanged = true;
                batch.HltId = null;
                batch.Hlt = null;
            }
            else if (newHlt != null)
            {
                if (batch.HltId != newHlt.Id) hltChanged = true;
                batch.HltId = newHlt.Id;
                batch.Hlt = newHlt;
            }
        }

        var mashTunChanged = false;
        if (request.MashTunId.HasValue)
        {
            if (request.MashTunId.Value == Guid.Empty)
            {
                if (batch.MashTunId != null) mashTunChanged = true;
                batch.MashTunId = null;
                batch.MashTun = null;
            }
            else if (newMashTun != null)
            {
                if (batch.MashTunId != newMashTun.Id) mashTunChanged = true;
                batch.MashTunId = newMashTun.Id;
                batch.MashTun = newMashTun;
            }
        }

        var boilerChanged = false;
        if (request.BoilerId.HasValue)
        {
            if (request.BoilerId.Value == Guid.Empty)
            {
                if (batch.BoilerId != null) boilerChanged = true;
                batch.BoilerId = null;
                batch.Boiler = null;
            }
            else if (newBoiler != null)
            {
                if (batch.BoilerId != newBoiler.Id) boilerChanged = true;
                batch.BoilerId = newBoiler.Id;
                batch.Boiler = newBoiler;
            }
        }

        var fermenterChanged = false;
        if (request.FermenterId.HasValue)
        {
            if (request.FermenterId.Value == Guid.Empty)
            {
                if (batch.FermenterId != null) fermenterChanged = true;
                batch.FermenterId = null;
                batch.Fermenter = null;
            }
            else if (newFermenter != null)
            {
                if (batch.FermenterId != newFermenter.Id) fermenterChanged = true;
                batch.FermenterId = newFermenter.Id;
                batch.Fermenter = newFermenter;
            }
        }

        if (syncAioMashTunWithBoiler && batch.MashTunId != batch.BoilerId)
        {
            batch.MashTunId = batch.BoilerId;
            batch.MashTun = batch.Boiler;
            mashTunChanged = true;
        }

        // Keep Batch.BrewerySetupId consistent on vessel swaps (BE-H4)
        var anyVesselChanged = hltChanged || mashTunChanged || boilerChanged || fermenterChanged || packagingVesselChanged;
        if (anyVesselChanged)
        {
            var setupSource =
                (boilerChanged ? batch.Boiler : null) ??
                (mashTunChanged ? batch.MashTun : null) ??
                (hltChanged ? batch.Hlt : null) ??
                (fermenterChanged ? batch.Fermenter : null) ??
                (packagingVesselChanged ? batch.PackagingVessel : null) ??
                batch.Boiler ?? batch.MashTun ?? batch.Hlt ?? batch.Fermenter ?? batch.PackagingVessel;

            if (setupSource != null && setupSource.BrewerySetupId != Guid.Empty && batch.BrewerySetupId != setupSource.BrewerySetupId)
            {
                batch.BrewerySetupId = setupSource.BrewerySetupId;
                batch.BrewerySetup = setupSource.BrewerySetup;
            }
        }

        // Recalculate ABV & BrewhouseEfficiency
        var effectiveOg = batch.MeasuredOg ?? (batch.TargetOg > 1.000m ? batch.TargetOg : (decimal?)null);
        var effectiveFg = batch.CurrentGravity ?? batch.MeasuredFg;
        if (effectiveOg.HasValue && effectiveFg.HasValue)
        {
            batch.AlcoholByVolume = effectiveOg.Value > effectiveFg.Value
                ? BrewingCalculator.CalculateActualAbv(effectiveOg.Value, effectiveFg.Value)
                : 0.0m;
        }

        if (batch.MeasuredOg.HasValue && (request.MeasuredOg.HasValue || request.MeasuredBatchSizeLiters.HasValue))
        {
            var vol = batch.MeasuredBatchSizeLiters ?? batch.TargetBatchSizeLiters;
            var totalPotentialPoints = await CalculateTotalGrainPotentialPointsAsync(batch.Ingredients, userId);
            batch.BrewhouseEfficiency = BrewingCalculator.CalculateBrewhouseEfficiency(vol, batch.MeasuredOg.Value, totalPotentialPoints);
        }

        if (batch.VolumeProfile != null)
        {
            var lossOrSpargeChanged = false;
            if (anyVesselChanged)
            {
                var setup = batch.BrewerySetup ?? (batch.BrewerySetupId.HasValue
                    ? await _db.BrewerySetups.AsNoTracking().FirstOrDefaultAsync(s => s.Id == batch.BrewerySetupId.Value)
                    : null);

                if (boilerChanged)
                {
                    batch.VolumeProfile.BoilOffRatePerHour = batch.Boiler?.BoilOffRatePerHour ?? setup?.DefaultBoilOffRatePerHour ?? 3.0m;
                    batch.VolumeProfile.KettleTrubLossLiters = batch.Boiler?.TrubLossLiters ?? setup?.DefaultKettleTrubLossLiters ?? 1.5m;
                    if ((batch.MashTun == null || batch.MashTunId == batch.BoilerId) && !request.MashTunDeadSpaceLiters.HasValue)
                    {
                        batch.VolumeProfile.MashTunDeadSpaceLiters = batch.Boiler?.MashTunDeadSpaceLiters ?? setup?.DefaultMashTunDeadSpaceLiters ?? 0.0m;
                    }
                    lossOrSpargeChanged = true;
                }
                if (mashTunChanged && !request.MashTunDeadSpaceLiters.HasValue)
                {
                    batch.VolumeProfile.MashTunDeadSpaceLiters = batch.MashTun?.MashTunDeadSpaceLiters ?? batch.Boiler?.MashTunDeadSpaceLiters ?? setup?.DefaultMashTunDeadSpaceLiters ?? 0.0m;
                    lossOrSpargeChanged = true;
                }
                if (hltChanged && !request.HltDeadSpaceLiters.HasValue)
                {
                    batch.VolumeProfile.HltDeadSpaceLiters = batch.Hlt?.TrubLossLiters ?? 0.0m;
                    lossOrSpargeChanged = true;
                }
                if (fermenterChanged)
                {
                    batch.VolumeProfile.FermenterTrubLossLiters = batch.Fermenter?.TrubLossLiters ?? setup?.DefaultFermenterLossLiters ?? 1.5m;
                    lossOrSpargeChanged = true;
                }
                if (packagingVesselChanged)
                {
                    batch.VolumeProfile.PackagingLossLiters = batch.PackagingVessel?.PackagingLossLiters ?? setup?.DefaultPackagingLossLiters ?? 0.5m;
                    lossOrSpargeChanged = true;
                }
            }

            if (request.MashTunDeadSpaceLiters.HasValue)
            {
                batch.VolumeProfile.MashTunDeadSpaceLiters = request.MashTunDeadSpaceLiters.Value;
                lossOrSpargeChanged = true;
            }
            if (request.HltDeadSpaceLiters.HasValue)
            {
                batch.VolumeProfile.HltDeadSpaceLiters = request.HltDeadSpaceLiters.Value;
                lossOrSpargeChanged = true;
            }
            if (request.PlumbingLossLiters.HasValue)
            {
                batch.VolumeProfile.PlumbingLossLiters = request.PlumbingLossLiters.Value;
                lossOrSpargeChanged = true;
            }
            if (!string.IsNullOrWhiteSpace(request.SpargeMethod))
            {
                batch.VolumeProfile.SpargeMethod = request.SpargeMethod.Trim();
                lossOrSpargeChanged = true;
            }

            if (lossOrSpargeChanged)
            {
                var isAllInOne = batch.Boiler?.Subtype == EquipmentSubtype.AllInOne ||
                                 batch.MashTun?.Subtype == EquipmentSubtype.AllInOne ||
                                 (batch.Boiler != null && (batch.MashTun == null || batch.Boiler.Id == batch.MashTun.Id));
                var mashThickness = isAllInOne ? 2.7m : 3.0m;

                var totalGrainKg = BrewingCalculator.CalculateMashGrainWeightKg(batch.Ingredients);
                if (totalGrainKg <= 0m && batch.RecipeId == null && batch.Ingredients.Count == 0)
                {
                    var refVol = batch.VolumeProfile.TargetFermenterVolumeLiters > 0m
                        ? batch.VolumeProfile.TargetFermenterVolumeLiters
                        : batch.TargetBatchSizeLiters;
                    totalGrainKg = Math.Round(4.5m * (refVol / 20.0m), 3);
                }

                var spargeEnabled = !string.Equals(batch.VolumeProfile.SpargeMethod, "NoSparge", StringComparison.OrdinalIgnoreCase);
                var recalcResult = VolumeCalculator.CalculateWaterRequirements(new VolumeCalculationInput(
                    TargetBatchSizeLiters: batch.VolumeProfile.TargetFermenterVolumeLiters > 0m
                        ? batch.VolumeProfile.TargetFermenterVolumeLiters
                        : batch.TargetBatchSizeLiters,
                    BoilTimeMinutes: batch.BoilTimeMinutes,
                    TotalGrainWeightKg: totalGrainKg,
                    TargetBasis: TargetVolumeBasis.Fermenter,
                    SpargeEnabled: spargeEnabled,
                    BoilOffRatePerHour: batch.VolumeProfile.BoilOffRatePerHour,
                    GrainAbsorptionRate: batch.VolumeProfile.GrainAbsorptionRateLPerKg,
                    KettleTrubLossLiters: batch.VolumeProfile.KettleTrubLossLiters,
                    FermenterLossLiters: batch.VolumeProfile.FermenterTrubLossLiters,
                    MashTunDeadSpaceLiters: batch.VolumeProfile.MashTunDeadSpaceLiters,
                    CoolingShrinkagePercent: batch.VolumeProfile.CoolingShrinkagePercent,
                    PackagingLossLiters: batch.VolumeProfile.PackagingLossLiters,
                    MashThicknessLitersPerKg: mashThickness,
                    HltDeadSpaceLiters: batch.VolumeProfile.HltDeadSpaceLiters,
                    PlumbingLossLiters: batch.VolumeProfile.PlumbingLossLiters,
                    SpargeMethod: batch.VolumeProfile.SpargeMethod,
                    RecoverMashTunDeadSpace: isAllInOne
                ));

                batch.VolumeProfile.TotalWaterLiters = recalcResult.TotalWaterLiters;
                batch.VolumeProfile.StrikeWaterLiters = recalcResult.StrikeWaterLiters;
                batch.VolumeProfile.SpargeWaterLiters = recalcResult.SpargeWaterLiters;
                batch.VolumeProfile.TargetPreBoilVolumeLiters = recalcResult.TargetPreBoilVolumeLiters;
                if (!batch.VolumeProfile.MeasuredPreBoilVolumeLiters.HasValue)
                {
                    batch.VolumeProfile.TargetPostBoilVolumeLiters = recalcResult.TargetPostBoilVolumeLiters;
                }
            }

            if (request.MeasuredPreBoilVolumeLiters.HasValue)
                batch.VolumeProfile.MeasuredPreBoilVolumeLiters = request.MeasuredPreBoilVolumeLiters.Value;
            if (request.MeasuredPreBoilGravity.HasValue)
                batch.VolumeProfile.MeasuredPreBoilGravity = request.MeasuredPreBoilGravity.Value;
            if (request.MeasuredPostBoilVolumeLiters.HasValue)
                batch.VolumeProfile.MeasuredPostBoilVolumeLiters = request.MeasuredPostBoilVolumeLiters.Value;
            if (request.MeasuredBatchSizeLiters.HasValue)
                batch.VolumeProfile.MeasuredFermenterVolumeLiters = request.MeasuredBatchSizeLiters.Value;
            if (request.MeasuredPackagedVolumeLiters.HasValue)
                batch.VolumeProfile.MeasuredPackagedVolumeLiters = request.MeasuredPackagedVolumeLiters.Value;
            VolumeCalculator.RecalculatePostStepMilestones(batch.VolumeProfile, batch.BoilTimeMinutes);
            batch.VolumeProfile.UpdatedAt = now;
        }

        // Unified Status/Stage transition & equipment reconciliation (BE-H3)
        var statusChanged = request.Status.HasValue && request.Status.Value != oldStatus;
        if (statusChanged)
        {
            var oldStage = batch.CurrentStage;
            affectedEquipmentIds.UnionWith(await BatchVesselReconciler.ApplyTransitionAsync(
                _db,
                batch,
                oldStatus,
                oldStage,
                effectiveStatus,
                effectiveStage,
                wasHistorical: oldIsHistorical,
                now: now,
                measuredPreBoilVolumeLiters: request.MeasuredPreBoilVolumeLiters,
                measuredPackagedVolumeLiters: request.MeasuredPackagedVolumeLiters));
        }

        // Vessel swap / clear / revival reconciliation
        if (packagingVesselChanged || isRevivingFromHistorical || request.MeasuredPackagedVolumeLiters.HasValue)
        {
            affectedEquipmentIds.UnionWith(await BatchVesselReconciler.ApplyVesselSwapAsync(
                _db,
                batch,
                BatchVesselSlot.PackagingVessel,
                packagingVesselChanged ? oldPackagingVessel : null,
                batch.PackagingVessel,
                now,
                request.MeasuredPackagedVolumeLiters));
        }

        if (hltChanged || isRevivingFromHistorical)
        {
            affectedEquipmentIds.UnionWith(await BatchVesselReconciler.ApplyVesselSwapAsync(
                _db, batch, BatchVesselSlot.Hlt, hltChanged ? oldHlt : null, batch.Hlt, now));
        }
        if (mashTunChanged || isRevivingFromHistorical)
        {
            affectedEquipmentIds.UnionWith(await BatchVesselReconciler.ApplyVesselSwapAsync(
                _db, batch, BatchVesselSlot.MashTun, mashTunChanged ? oldMashTun : null, batch.MashTun, now));
        }
        if (boilerChanged || isRevivingFromHistorical)
        {
            affectedEquipmentIds.UnionWith(await BatchVesselReconciler.ApplyVesselSwapAsync(
                _db, batch, BatchVesselSlot.Boiler, boilerChanged ? oldBoiler : null, batch.Boiler, now));
        }
        if (fermenterChanged || isRevivingFromHistorical)
        {
            affectedEquipmentIds.UnionWith(await BatchVesselReconciler.ApplyVesselSwapAsync(
                _db, batch, BatchVesselSlot.Fermenter, fermenterChanged ? oldFermenter : null, batch.Fermenter, now));
        }

        if (validatedSensorAssignments != null)
        {
            _db.BatchSensorAssignments.RemoveRange(batch.SensorAssignments);
            batch.SensorAssignments.Clear();

            foreach (var assignment in validatedSensorAssignments)
            {
                batch.SensorAssignments.Add(assignment);
                _db.BatchSensorAssignments.Add(assignment);
            }
        }

        batch.UpdatedAt = now;
        await _db.SaveChangesAsync();

        return (BatchAccessResult.Success, MapToDetailDto(batch), null);
    }

    public async Task<BatchAccessResult> DeleteBatchAsync(Guid id, string userId)
    {
        var batch = await _db.Batches
            .Include(b => b.Hlt)
            .Include(b => b.MashTun)
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Include(b => b.PackagingVessel)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (batch == null)
        {
            return BatchAccessResult.NotFound;
        }

        var now = UtcNow;
        var affectedEquipmentIds = new HashSet<Guid>();
        await BatchVesselReconciler.DecoupleAssignedEquipmentAsync(_db, batch.Id, now, affectedEquipmentIds);

        if (BatchLifecycle.OccupiesLiveEquipment(batch))
        {
            // Free associated active equipment only when not actively occupied by another batch
            await BatchVesselReconciler.ResetEquipmentVolumeIfOwnedAsync(_db, batch.Hlt, batch.Id, now, affectedEquipmentIds);
            await BatchVesselReconciler.ResetEquipmentVolumeIfOwnedAsync(_db, batch.MashTun, batch.Id, now, affectedEquipmentIds);
            await BatchVesselReconciler.ResetEquipmentVolumeIfOwnedAsync(_db, batch.Boiler, batch.Id, now, affectedEquipmentIds);
            await BatchVesselReconciler.ResetEquipmentVolumeIfOwnedAsync(_db, batch.Fermenter, batch.Id, now, affectedEquipmentIds);
            if (batch.PackagingVessel != null &&
                (batch.PackagingVessel.AssignedBatchId == null || batch.PackagingVessel.AssignedBatchId == batch.Id))
            {
                if (batch.PackagingVessel.Type == EquipmentType.Keg && batch.PackagingVessel.AssignedBatchId == batch.Id)
                {
                    batch.PackagingVessel.AssignedBatchId = null;
                    batch.PackagingVessel.AssignedBatch = null;
                    batch.PackagingVessel.AssignedAt = null;
                }
                await BatchVesselReconciler.ResetEquipmentVolumeIfOwnedAsync(_db, batch.PackagingVessel, batch.Id, now, affectedEquipmentIds);
            }
        }

        _db.Batches.Remove(batch);
        await _db.SaveChangesAsync();

        await BroadcastEquipmentVolumeUpdatesAsync(affectedEquipmentIds, now);

        return BatchAccessResult.Success;
    }

    private async Task BroadcastEquipmentVolumeUpdatesAsync(IReadOnlyCollection<Guid> equipmentIds, DateTime timestamp)
    {
        if (equipmentIds.Count == 0)
        {
            return;
        }

        var affectedEquipment = await _db.Equipment
            .AsNoTracking()
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .Where(e => equipmentIds.Contains(e.Id))
            .ToListAsync();

        var affectedKegIds = affectedEquipment
            .Where(e => e.Type == EquipmentType.Keg)
            .Select(e => e.Id)
            .ToList();

        var connectedTapsByKeg = affectedKegIds.Count > 0
            ? (await _db.Equipment
                .AsNoTracking()
                .Include(t => t.BrewerySetup)
                    .ThenInclude(s => s!.Members)
                .Where(t => t.Type == EquipmentType.Tap && t.AssignedKegId.HasValue && affectedKegIds.Contains(t.AssignedKegId.Value))
                .ToListAsync())
                .GroupBy(t => t.AssignedKegId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList())
            : new Dictionary<Guid, List<Equipment>>();

        foreach (var equipment in affectedEquipment)
        {
            var fillPercentage = equipment.CapacityLiters > 0m
                ? Math.Round(Math.Clamp((equipment.CurrentVolumeLiters / equipment.CapacityLiters) * 100m, 0m, 100m), 1)
                : 0m;

            var activeBatchId = await BatchLifecycle.ResolveActiveBatchIdForEquipmentAsync(_db, equipment);

            var update = new EquipmentTelemetryUpdateDto(
                EquipmentId: equipment.Id,
                EquipmentName: equipment.Name,
                TemperatureC: equipment.CurrentTemperatureC,
                Timestamp: timestamp,
                Source: "BatchVesselUpdate",
                BatchId: activeBatchId,
                SpecificGravity: equipment.CurrentSpecificGravity,
                CurrentVolumeLiters: equipment.CurrentVolumeLiters,
                FillPercentage: fillPercentage,
                PressureBar: equipment.CurrentPressureBar,
                BatteryPercent: equipment.CurrentBatteryPercent,
                BatteryVoltage: equipment.CurrentBatteryVoltage,
                TiltDegrees: null,
                Rssi: null,
                MetricsJson: equipment.LatestMetricsJson,
                Connected: null
            );

            foreach (var uid in EquipmentAccessHelper.GetAuthorizedSetupUserIds(equipment))
            {
                _broadcastService.BroadcastEquipmentReading(uid, update);
            }

            if (connectedTapsByKeg.TryGetValue(equipment.Id, out var connectedTaps))
            {
                foreach (var tap in connectedTaps)
                {
                    var tapUpdate = new EquipmentTelemetryUpdateDto(
                        EquipmentId: tap.Id,
                        EquipmentName: tap.Name,
                        TemperatureC: equipment.CurrentTemperatureC,
                        Timestamp: timestamp,
                        Source: "BatchVesselUpdate",
                        BatchId: tap.AssignedBatchId,
                        SpecificGravity: null,
                        CurrentVolumeLiters: 0m,
                        FillPercentage: fillPercentage,
                        PressureBar: null,
                        BatteryPercent: null,
                        BatteryVoltage: null,
                        TiltDegrees: null,
                        Rssi: null,
                        MetricsJson: null,
                        Connected: null
                    );

                    foreach (var uid in EquipmentAccessHelper.GetAuthorizedSetupUserIds(tap))
                    {
                        _broadcastService.BroadcastEquipmentReading(uid, tapUpdate);
                    }
                }
            }
        }
    }

    public static BatchFermentationStep? ResolveActiveFermentationStep(ICollection<BatchFermentationStep> steps)
    {
        if (steps == null || steps.Count == 0)
        {
            return null;
        }

        return steps.OrderBy(s => s.StepOrder).FirstOrDefault(s => !s.IsCompleted)
            ?? steps.OrderBy(s => s.StepOrder).LastOrDefault();
    }

    private async Task<decimal> CalculateTotalGrainPotentialPointsAsync(IEnumerable<BatchIngredient> ingredients, string userId)
    {
        var fermentables = ingredients
            .Where(i => i.Type == IngredientType.Fermentable &&
                        i.AdditionStage is not (IngredientUsage.Primary or IngredientUsage.Secondary or IngredientUsage.Bottling) &&
                        IsMassUnit(i.Unit))
            .ToList();

        if (fermentables.Count == 0)
        {
            return 1.0m;
        }

        var sourceIds = fermentables
            .Where(f => f.SourceIngredientId.HasValue)
            .Select(f => f.SourceIngredientId!.Value)
            .Distinct()
            .ToList();

        var potentialById = sourceIds.Count > 0
            ? await _db.Ingredients
                .AsNoTracking()
                .Where(i => sourceIds.Contains(i.Id) && (i.IsCatalogItem || i.CreatedByUserId == userId))
                .ToDictionaryAsync(i => i.Id, i => i.PotentialGravity)
            : new Dictionary<Guid, decimal?>();

        var namesWithoutId = fermentables
            .Where(f => !f.SourceIngredientId.HasValue && !string.IsNullOrWhiteSpace(f.Name))
            .Select(f => f.Name.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var potentialByName = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        if (namesWithoutId.Count > 0)
        {
            var matchedByName = await _db.Ingredients
                .AsNoTracking()
                .Where(i => i.Type == IngredientType.Fermentable &&
                            (i.IsCatalogItem || i.CreatedByUserId == userId) &&
                            namesWithoutId.Contains(i.Name.ToLower()))
                .ToListAsync();

            foreach (var match in matchedByName)
            {
                potentialByName.TryAdd(match.Name.Trim(), match.PotentialGravity);
            }
        }

        double totalPoints = 0.0;
        foreach (var ing in fermentables)
        {
            var weightLbs = (double)ConvertUnitAmount(ing.Amount, ing.Unit, "lb");
            decimal? potentialSg = null;
            if (ing.SourceIngredientId.HasValue)
            {
                potentialById.TryGetValue(ing.SourceIngredientId.Value, out potentialSg);
            }
            else if (!string.IsNullOrWhiteSpace(ing.Name))
            {
                potentialByName.TryGetValue(ing.Name.Trim(), out potentialSg);
            }

            var ppg = potentialSg is > 1.0m
                ? (double)((potentialSg.Value - 1.0m) * 1000m)
                : 37.0;

            totalPoints += weightLbs * ppg;
        }

        return (decimal)Math.Max(1.0, totalPoints);
    }

    internal static bool IsEligibleBatchTelemetrySensor(Equipment equipment)
    {
        return (equipment.Type is EquipmentType.Sensor or EquipmentType.Other) &&
               equipment.Subtype is EquipmentSubtype.ISpindel or EquipmentSubtype.Tilt or EquipmentSubtype.GenericSensor;
    }

    private async Task<(BatchAccessResult Result, List<BatchSensorAssignment> Assignments, string? ErrorMessage)> ValidateAndBuildSensorAssignmentsAsync(
        Batch batch,
        IEnumerable<BatchSensorAssignmentInput> inputs,
        string userId,
        bool allowRecipeMashStepFallback = false,
        bool? checkOccupancyOverride = null)
    {
        var assignments = new List<BatchSensorAssignment>();
        var seenKeys = new HashSet<(Guid EquipmentId, BrewStage? Stage, Guid? BatchMashStepId)>();
        var checkOccupancy = checkOccupancyOverride ?? BatchLifecycle.OccupiesLiveEquipment(batch);

        foreach (var sa in inputs)
        {
            if (sa.BatchMashStepId.HasValue && sa.Stage.HasValue && sa.Stage.Value != BrewStage.Mash)
            {
                return (BatchAccessResult.ValidationFailed, [], "Mash step sensor assignments must use the Mash stage.");
            }

            BatchMashStep? matchedStep = null;
            if (sa.BatchMashStepId.HasValue)
            {
                matchedStep = batch.MashSteps.FirstOrDefault(ms =>
                    ms.Id == sa.BatchMashStepId.Value ||
                    (allowRecipeMashStepFallback && ms.RecipeMashStepId == sa.BatchMashStepId.Value));

                if (matchedStep == null)
                {
                    return (BatchAccessResult.ValidationFailed, [], "Specified mash step does not belong to this batch.");
                }
            }

            var sensorEq = await GetAccessibleEquipment(userId).FirstOrDefaultAsync(e => e.Id == sa.EquipmentId);
            if (sensorEq == null)
            {
                return (BatchAccessResult.NotFound, [], "Specified sensor equipment was not found or is not accessible.");
            }

            if (!IsEligibleBatchTelemetrySensor(sensorEq))
            {
                var message = sensorEq.Subtype == EquipmentSubtype.BrewLink
                    ? "BrewLink gateway equipment cannot be assigned as a batch telemetry sensor."
                    : $"Equipment '{sensorEq.Name}' is not a valid telemetry sensor. Only iSpindel, Tilt, and Generic Sensor equipment can be assigned.";
                return (BatchAccessResult.ValidationFailed, [], message);
            }

            var key = (sensorEq.Id, sa.Stage, matchedStep?.Id);
            if (!seenKeys.Add(key))
            {
                return (BatchAccessResult.ValidationFailed, [], $"Duplicate sensor assignment specified for '{sensorEq.Name}'.");
            }

            if (checkOccupancy)
            {
                var targetStage = sa.Stage;
                var occupiedByOtherBatch = await _db.BatchSensorAssignments
                    .AsNoTracking()
                    .AnyAsync(existing =>
                        existing.EquipmentId == sensorEq.Id &&
                        existing.BatchId != batch.Id &&
                        !existing.Batch.IsHistorical &&
                        (existing.Batch.Status == BatchStatus.Brewing ||
                         existing.Batch.Status == BatchStatus.Fermenting ||
                         existing.Batch.Status == BatchStatus.Conditioning) &&
                        (existing.Stage == null || targetStage == null || existing.Stage == targetStage));

                if (occupiedByOtherBatch)
                {
                    return (BatchAccessResult.Conflict, [], $"Sensor '{sensorEq.Name}' is currently assigned to another active batch.");
                }
            }

            assignments.Add(new BatchSensorAssignment
            {
                BatchId = batch.Id,
                EquipmentId = sensorEq.Id,
                Equipment = sensorEq,
                Stage = sa.Stage,
                BatchMashStepId = matchedStep?.Id,
                BatchMashStep = matchedStep
            });
        }

        return (BatchAccessResult.Success, assignments, null);
    }

    private BatchDetailDto MapToDetailDto(Batch b, string? boilerSyncStatus = null, string? boilerSyncWarning = null, decimal? resolvedCurrentGravity = null)
    {
        var today = TodayUtc;
        var latestReading = b.Readings.OrderByDescending(r => r.Timestamp).FirstOrDefault();
        var effectiveReadingOg = b.MeasuredOg ?? (b.TargetOg > 1.000m ? b.TargetOg : (decimal?)null);

        decimal? ogDeviation = b.MeasuredOg.HasValue ? Math.Round(b.MeasuredOg.Value - b.TargetOg, 4) : null;
        int? ogPointsDeviation = ogDeviation.HasValue ? (int?)Math.Round(ogDeviation.Value * 1000m, MidpointRounding.AwayFromZero) : null;

        decimal? fgDeviation = b.MeasuredFg.HasValue ? Math.Round(b.MeasuredFg.Value - b.TargetFg, 4) : null;
        int? fgPointsDeviation = fgDeviation.HasValue ? (int?)Math.Round(fgDeviation.Value * 1000m, MidpointRounding.AwayFromZero) : null;

        decimal? abvDeviation = b.AlcoholByVolume.HasValue ? Math.Round(b.AlcoholByVolume.Value - b.TargetAbv, 2) : null;

        decimal? batchSizeDeviation = b.MeasuredBatchSizeLiters.HasValue ? Math.Round(b.MeasuredBatchSizeLiters.Value - b.TargetBatchSizeLiters, 2) : null;

        decimal? preBoilVolumeDeviation = (b.VolumeProfile?.MeasuredPreBoilVolumeLiters.HasValue == true)
            ? Math.Round(b.VolumeProfile.MeasuredPreBoilVolumeLiters.Value - b.VolumeProfile.TargetPreBoilVolumeLiters, 2)
            : null;

        decimal? postBoilVolumeDeviation = (b.VolumeProfile?.MeasuredPostBoilVolumeLiters.HasValue == true)
            ? Math.Round(b.VolumeProfile.MeasuredPostBoilVolumeLiters.Value - b.VolumeProfile.TargetPostBoilVolumeLiters, 2)
            : null;

        decimal? packagedVolumeDeviation = (b.VolumeProfile?.MeasuredPackagedVolumeLiters.HasValue == true)
            ? Math.Round(b.VolumeProfile.MeasuredPackagedVolumeLiters.Value - b.VolumeProfile.TargetPackagedVolumeLiters, 2)
            : null;

        decimal? efficiencyDeviation = b.BrewhouseEfficiency.HasValue
            ? Math.Round(b.BrewhouseEfficiency.Value - b.EfficiencyPercent, 2)
            : null;

        var deviations = new BatchDeviationsDto(
            OgDeviation: ogDeviation,
            OgPointsDeviation: ogPointsDeviation,
            FgDeviation: fgDeviation,
            FgPointsDeviation: fgPointsDeviation,
            AbvDeviation: abvDeviation,
            BatchSizeLitersDeviation: batchSizeDeviation,
            PreBoilVolumeDeviation: preBoilVolumeDeviation,
            PostBoilVolumeDeviation: postBoilVolumeDeviation,
            PackagedVolumeDeviation: packagedVolumeDeviation,
            BrewhouseEfficiencyDeviation: efficiencyDeviation
        );

        return new BatchDetailDto(
            Id: b.Id,
            UserId: b.UserId,
            RecipeId: b.RecipeId,
            RecipeName: b.Recipe?.Name ?? b.RecipeName,
            BatchCode: b.BatchCode,
            Name: b.Name,
            BeerStyle: b.BeerStyle,
            Status: b.Status,
            CurrentStage: b.CurrentStage,
            BrewDate: b.BrewDate,
            DaysActive: Math.Max(0, today.DayNumber - b.BrewDate.DayNumber),
            TargetOg: b.TargetOg,
            TargetFg: b.TargetFg,
            TargetAbv: b.TargetAbv,
            TargetIbu: b.TargetIbu,
            TargetColorSrm: b.TargetColorSrm,
            TargetBatchSizeLiters: b.TargetBatchSizeLiters,
            BoilTimeMinutes: b.BoilTimeMinutes,
            EfficiencyPercent: b.EfficiencyPercent,
            MeasuredOg: b.MeasuredOg,
            CurrentGravity: resolvedCurrentGravity ?? b.CurrentGravity ?? latestReading?.SpecificGravity,
            MeasuredFg: b.MeasuredFg,
            AlcoholByVolume: b.AlcoholByVolume,
            BrewhouseEfficiency: b.BrewhouseEfficiency,
            MeasuredBatchSizeLiters: b.MeasuredBatchSizeLiters,
            PitchTemperatureC: b.PitchTemperatureC,
            BoilerId: b.BoilerId,
            BoilerName: b.Boiler?.Name,
            FermenterId: b.FermenterId,
            FermenterName: b.Fermenter?.Name,
            PackagingVesselId: b.PackagingVesselId,
            PackagingVesselName: b.PackagingVessel?.Name,
            HltId: b.HltId,
            HltName: b.Hlt?.Name,
            MashTunId: b.MashTunId,
            MashTunName: b.MashTun?.Name,
            Notes: b.Notes,
            CreatedAt: b.CreatedAt,
            UpdatedAt: b.UpdatedAt,
            CompletedAt: b.CompletedAt,
            Readings: b.Readings.OrderBy(r => r.Timestamp).Select(r => new BatchReadingDto(
                r.Id,
                r.BatchId,
                r.Timestamp,
                r.SpecificGravity,
                r.TemperatureC,
                r.Notes,
                r.CreatedAt,
                effectiveReadingOg.HasValue
                    ? BrewingCalculator.CalculateActualAbv(effectiveReadingOg.Value, r.SpecificGravity)
                    : null
            )).ToList(),
            Ingredients: OrderIngredientsChronologically(b.Ingredients).Select(i => new BatchIngredientDto(
                i.Id,
                i.BatchId,
                i.SourceIngredientId,
                i.Name,
                i.Type,
                i.Amount,
                i.Unit,
                i.AdditionStage,
                i.AdditionTimeMinutes,
                i.IsChecked,
                i.IsDeducted,
                i.Notes,
                i.Form,
                i.FermentableCategory,
                i.FermentabilityPercent
            )).ToList(),
            MashSteps: b.MashSteps.OrderBy(s => s.StepOrder).Select(s => new BatchMashStepDto(
                s.Id,
                s.BatchId,
                s.RecipeMashStepId,
                s.StepOrder,
                s.Name,
                s.Type,
                s.TargetTemperatureC,
                s.DurationMinutes,
                s.RampTimeMinutes,
                s.InfuseAmountLiters,
                s.ActualTemperatureC,
                s.ActualDurationMinutes,
                s.IsCompleted,
                s.CompletedAt,
                s.Notes
            )).ToList(),
            FermentationSteps: b.FermentationSteps.OrderBy(s => s.StepOrder).Select(s => new BatchFermentationStepDto(
                s.Id,
                s.BatchId,
                s.RecipeFermentationStepId,
                s.StepOrder,
                s.Name,
                s.Type,
                s.TargetTemperatureC,
                s.ActualTemperatureC,
                s.DurationDays,
                s.RampTimeHours,
                s.TriggerGravity,
                s.IsCompleted,
                s.StartedAt,
                s.CompletedAt,
                s.Notes
            )).ToList(),
            StageHistory: b.StageHistory.OrderBy(sh => sh.Stage).Select(sh => new BatchStageHistoryDto(
                sh.Id,
                sh.BatchId,
                sh.Stage,
                sh.Status,
                sh.StartedAt,
                sh.CompletedAt,
                sh.DurationMinutes,
                sh.Notes
            )).ToList(),
            SensorAssignments: b.SensorAssignments.Select(sa => new BatchSensorAssignmentDto(
                sa.Id,
                sa.EquipmentId,
                sa.Equipment?.Name,
                sa.Equipment?.Subtype,
                sa.Stage,
                sa.BatchMashStepId,
                sa.BatchMashStep?.Name
            )).ToList(),
            VolumeProfile: b.VolumeProfile != null ? new BatchVolumeProfileDto(
                b.VolumeProfile.TotalWaterLiters,
                b.VolumeProfile.StrikeWaterLiters,
                b.VolumeProfile.SpargeWaterLiters,
                b.VolumeProfile.TargetPreBoilVolumeLiters,
                b.VolumeProfile.MeasuredPreBoilVolumeLiters,
                b.VolumeProfile.MeasuredPreBoilGravity,
                b.VolumeProfile.TargetPostBoilVolumeLiters,
                b.VolumeProfile.MeasuredPostBoilVolumeLiters,
                b.VolumeProfile.TargetFermenterVolumeLiters,
                b.VolumeProfile.MeasuredFermenterVolumeLiters,
                b.VolumeProfile.TargetPackagedVolumeLiters,
                b.VolumeProfile.MeasuredPackagedVolumeLiters,
                b.VolumeProfile.BoilOffRatePerHour,
                b.VolumeProfile.GrainAbsorptionRateLPerKg,
                b.VolumeProfile.KettleTrubLossLiters,
                b.VolumeProfile.FermenterTrubLossLiters,
                b.VolumeProfile.MashTunDeadSpaceLiters,
                b.VolumeProfile.CoolingShrinkagePercent,
                b.VolumeProfile.PackagingLossLiters,
                b.VolumeProfile.HltDeadSpaceLiters,
                b.VolumeProfile.PlumbingLossLiters,
                b.VolumeProfile.SpargeMethod
            ) : null,
            Deviations: deviations,
            BoilerSyncStatus: boilerSyncStatus,
            BoilerSyncWarning: boilerSyncWarning,
            IsHistorical: b.IsHistorical
        );
    }

    public async Task<(BatchAccessResult Result, BatchEquipmentReadingDto? Reading, string? ErrorMessage)> LogTemperatureReadingAsync(
        Guid batchId, LogBatchTemperatureRequest request, string userId)
    {
        var batch = await _db.Batches
            .Include(b => b.Hlt)
            .Include(b => b.MashTun)
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Include(b => b.PackagingVessel)
            .Include(b => b.MashSteps)
            .Include(b => b.FermentationSteps)
            .Include(b => b.StageHistory)
            .Include(b => b.SensorAssignments)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch not found.");
        }

        if (batch.Status == BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Cannot log readings for an archived batch.");
        }

        var existingEquipmentReadingCount = await _db.EquipmentReadings.CountAsync(r => r.BatchId == batch.Id);
        if (existingEquipmentReadingCount >= MaxEquipmentReadingsPerBatch)
        {
            return (BatchAccessResult.QuotaExceeded, null, $"Maximum readings per batch ({MaxEquipmentReadingsPerBatch}) reached.");
        }

        var stage = request.Stage ?? batch.CurrentStage;
        var stepName = request.StepName;
        Guid? stepId = request.BatchMashStepId;
        decimal? targetTempC = null;

        // If stage is Mash, resolve mash step
        if (stage == BrewStage.Mash)
        {
            BatchMashStep? mashStep = null;
            if (stepId.HasValue)
            {
                mashStep = batch.MashSteps.FirstOrDefault(s => s.Id == stepId.Value);
                if (mashStep == null)
                {
                    return (BatchAccessResult.NotFound, null, "Specified mash step not found.");
                }
            }
            else
            {
                mashStep = batch.MashSteps.OrderBy(s => s.StepOrder).FirstOrDefault(s => !s.IsCompleted);
            }

            if (mashStep != null)
            {
                stepId = mashStep.Id;
                stepName ??= mashStep.Name;
                targetTempC = mashStep.TargetTemperatureC;
                mashStep.ActualTemperatureC = request.TemperatureC;
            }
            else
            {
                stepId = null;
                stepName ??= "Mash";
            }
        }
        else if (stage == BrewStage.Boil)
        {
            stepName ??= "Boil";
            targetTempC = request.TargetTemperatureC ?? ((stepName != null && stepName.Contains("Whirlpool", StringComparison.OrdinalIgnoreCase)) ? 85.0m : 100.0m);
        }
        else if (stage == BrewStage.Ferment || stage == BrewStage.Condition)
        {
            var activeFermStep = ResolveActiveFermentationStep(batch.FermentationSteps);
            if (activeFermStep != null)
            {
                stepName ??= activeFermStep.Name;
                targetTempC = activeFermStep.TargetTemperatureC;
                if (!activeFermStep.IsCompleted)
                {
                    activeFermStep.ActualTemperatureC = request.TemperatureC;
                }
            }
            else
            {
                stepName ??= stage == BrewStage.Ferment ? "Ferment" : "Condition";
                targetTempC = stage == BrewStage.Ferment ? (batch.PitchTemperatureC ?? 20.0m) : 2.0m;
            }
        }
        else if (stage == BrewStage.Package)
        {
            stepName ??= "Package";
        }

        // Determine equipment (nullable when logging manual batch-only temperatures without assigned equipment)
        Guid? equipmentId = null;
        string equipmentName = "Manual";

        var timestamp = ClampReadingTimestamp(request.Timestamp, batch.BrewDate);
        var canUpdateLiveEquipment = BatchLifecycle.OccupiesLiveEquipment(batch) &&
                                     stage == batch.CurrentStage;

        if (request.EquipmentId.HasValue)
        {
            var eq = await GetAccessibleEquipment(userId)
                .FirstOrDefaultAsync(e => e.Id == request.EquipmentId.Value);
            if (eq == null)
            {
                return (BatchAccessResult.NotFound, null, "Specified equipment not found or not accessible to user.");
            }

            if (eq.Type == EquipmentType.Tap || eq.Subtype == EquipmentSubtype.BrewLink)
            {
                return (BatchAccessResult.ValidationFailed, null, "Taps and BrewLink gateways cannot be used for batch telemetry readings.");
            }

            if (BatchLifecycle.OccupiesLiveEquipment(batch) &&
                await IsEquipmentOccupiedByOtherBatchAsync(eq, batch.Id))
            {
                return (BatchAccessResult.Conflict, null, "Specified equipment is currently occupied by another active batch.");
            }

            equipmentId = eq.Id;
            equipmentName = eq.Name;
            if (canUpdateLiveEquipment &&
                (IsEquipmentLinkedToBatch(batch, eq.Id) || eq.AssignedBatchId == batch.Id) &&
                !await IsEquipmentOccupiedByOtherBatchAsync(eq, batch.Id))
            {
                eq.CurrentTemperatureC = request.TemperatureC;
                eq.TemperatureUpdatedAt = timestamp;
                eq.UpdatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            // Default equipment based on stage on this batch only (never fall back to arbitrary user/shared equipment)
            Equipment? defaultEq = stage switch
            {
                BrewStage.Mash => batch.MashTun ?? batch.Boiler,
                BrewStage.Boil => batch.Boiler,
                BrewStage.Ferment or BrewStage.Condition => batch.Fermenter,
                BrewStage.Package => batch.PackagingVessel,
                _ => null
            };

            if (defaultEq != null)
            {
                equipmentId = defaultEq.Id;
                equipmentName = defaultEq.Name;
                if (canUpdateLiveEquipment && !await IsEquipmentOccupiedByOtherBatchAsync(defaultEq, batch.Id))
                {
                    defaultEq.CurrentTemperatureC = request.TemperatureC;
                    defaultEq.TemperatureUpdatedAt = timestamp;
                    defaultEq.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        var reading = new EquipmentReading
        {
            EquipmentId = equipmentId,
            BatchId = batch.Id,
            Stage = stage,
            BatchMashStepId = stepId,
            StepName = stepName,
            TemperatureC = request.TemperatureC,
            Timestamp = timestamp,
            Source = "Manual",
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.EquipmentReadings.Add(reading);
        await _db.SaveChangesAsync();

        var dto = new BatchEquipmentReadingDto(
            reading.Id,
            equipmentId ?? Guid.Empty,
            equipmentName,
            batch.Id,
            stage,
            stepId,
            stepName,
            reading.TemperatureC,
            reading.Timestamp,
            reading.Source,
            targetTempC,
            reading.Notes
        );

        _broadcastService.BroadcastReading(dto);

        return (BatchAccessResult.Success, dto, null);
    }

    public async Task<(BatchAccessResult Result, List<BatchEquipmentReadingDto>? Readings, PaginationMeta? Pagination, string? ErrorMessage)> GetBatchEquipmentReadingsAsync(
        Guid batchId, string userId, BrewStage? stage = null, Guid? stepId = null, int page = 1, int limit = 500)
    {
        var batch = await _db.Batches.AsNoTracking()
            .Include(b => b.MashSteps)
            .Include(b => b.FermentationSteps)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, null, "Batch not found.");
        }

        var normalizedPage = Math.Max(1, page);
        var normalizedLimit = Math.Clamp(limit, 1, 1000);

        var activeFermTargetTempC = ResolveActiveFermentationStep(batch.FermentationSteps)?.TargetTemperatureC;
        var fermentFallbackTempC = activeFermTargetTempC ?? batch.PitchTemperatureC ?? 20.0m;
        var conditionFallbackTempC = activeFermTargetTempC ?? 2.0m;

        var query = _db.EquipmentReadings.AsNoTracking()
            .Include(r => r.Equipment)
            .Include(r => r.BatchMashStep)
            .Where(r => r.BatchId == batchId);

        if (stage.HasValue)
        {
            query = query.Where(r => r.Stage == stage.Value);
        }

        if (stepId.HasValue)
        {
            query = query.Where(r => r.BatchMashStepId == stepId.Value);
        }

        var totalCount = await query.CountAsync();
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedLimit);

        var readings = await query
            .OrderBy(r => r.Timestamp)
            .Skip((normalizedPage - 1) * normalizedLimit)
            .Take(normalizedLimit)
            .Select(r => new BatchEquipmentReadingDto(
                r.Id,
                r.EquipmentId ?? Guid.Empty,
                r.Equipment != null ? r.Equipment.Name : (r.EquipmentId == null ? "Manual" : "Equipment"),
                r.BatchId!.Value,
                r.Stage,
                r.BatchMashStepId,
                r.StepName,
                r.TemperatureC,
                r.Timestamp,
                r.Source,
                r.BatchMashStep != null
                    ? r.BatchMashStep.TargetTemperatureC
                    : (r.Stage == BrewStage.Boil
                        ? 100.0m
                        : (r.Stage == BrewStage.Ferment
                            ? fermentFallbackTempC
                            : (r.Stage == BrewStage.Condition ? conditionFallbackTempC : null))),
                r.Notes,
                r.SpecificGravity
            ))
            .ToListAsync();

        var pagination = new PaginationMeta(normalizedPage, normalizedLimit, totalCount, totalPages);
        return (BatchAccessResult.Success, readings, pagination, null);
    }

    public async Task<(BatchAccessResult Result, List<BatchSensorAssignmentDto>? Sensors, string? ErrorMessage)> UpdateBatchSensorsAsync(
        Guid batchId, List<BatchSensorAssignmentInput> sensorInputs, string userId)
    {
        var batch = await _db.Batches
            .Include(b => b.MashSteps)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.Equipment)
            .Include(b => b.SensorAssignments)
                .ThenInclude(sa => sa.BatchMashStep)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId);

        if (batch == null)
        {
            return (BatchAccessResult.NotFound, null, "Batch not found.");
        }

        if (batch.Status is BatchStatus.Completed or BatchStatus.Archived)
        {
            return (BatchAccessResult.Conflict, null, "Completed or archived batches cannot have their sensor assignments modified.");
        }

        var (sensorResult, validatedAssignments, sensorErr) = await ValidateAndBuildSensorAssignmentsAsync(
            batch, sensorInputs, userId);

        if (sensorResult != BatchAccessResult.Success)
        {
            return (sensorResult, null, sensorErr);
        }

        _db.BatchSensorAssignments.RemoveRange(batch.SensorAssignments);
        batch.SensorAssignments.Clear();

        foreach (var assignment in validatedAssignments)
        {
            batch.SensorAssignments.Add(assignment);
            _db.BatchSensorAssignments.Add(assignment);
        }

        batch.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dtoList = batch.SensorAssignments.Select(sa => new BatchSensorAssignmentDto(
            sa.Id,
            sa.EquipmentId,
            sa.Equipment?.Name,
            sa.Equipment?.Subtype,
            sa.Stage,
            sa.BatchMashStepId,
            sa.BatchMashStep?.Name
        )).ToList();

        return (BatchAccessResult.Success, dtoList, null);
    }

    public async Task<(BatchAccessResult Result, BatchStockCheckResult? CheckResult, string? ErrorMessage)> CheckRecipeStockAsync(
        Guid recipeId,
        decimal targetBatchSizeLiters,
        string userId,
        TargetVolumeBasis? targetBasis = null,
        decimal? fermenterLossLiters = null,
        decimal? packagingLossLiters = null)
    {
        var recipe = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
            .FirstOrDefaultAsync(r => r.Id == recipeId && (r.UserId == userId || r.IsPublic));

        if (recipe == null)
        {
            return (BatchAccessResult.NotFound, null, "Recipe not found.");
        }

        var effectiveFermenterVolume = targetBasis == TargetVolumeBasis.Packaged
            ? targetBatchSizeLiters + (fermenterLossLiters ?? 1.5m) + (packagingLossLiters ?? 0.5m)
            : targetBatchSizeLiters;

        var scale = recipe.BatchSizeLiters > 0 ? effectiveFermenterVolume / recipe.BatchSizeLiters : 1.0m;
        var shortages = new List<IngredientShortageDto>();

        var userStocks = await _db.IngredientStocks
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync();

        var groupedIngredients = recipe.Ingredients
            .GroupBy(ri => ri.IngredientId);

        foreach (var group in groupedIngredients)
        {
            var first = group.First();
            var baseUnit = first.Unit;
            decimal totalRequiredAmount = 0m;
            foreach (var ri in group)
            {
                var scaled = Math.Round(ri.Amount * scale, 3);
                totalRequiredAmount += ConvertUnitAmount(scaled, ri.Unit, baseUnit);
            }
            totalRequiredAmount = Math.Round(totalRequiredAmount, 3);

            var stock = userStocks.FirstOrDefault(s => s.IngredientId == group.Key);

            decimal availableStockInRecipeUnit = 0m;
            if (stock != null)
            {
                availableStockInRecipeUnit = ConvertUnitAmount(stock.Amount, stock.Unit, baseUnit);
            }

            if (availableStockInRecipeUnit < totalRequiredAmount)
            {
                var deficit = Math.Round(totalRequiredAmount - availableStockInRecipeUnit, 3);
                shortages.Add(new IngredientShortageDto(
                    group.Key,
                    first.Ingredient?.Name ?? "Unknown Ingredient",
                    first.Ingredient?.Type ?? IngredientType.Other,
                    totalRequiredAmount,
                    Math.Round(availableStockInRecipeUnit, 3),
                    baseUnit,
                    deficit
                ));
            }
        }

        var result = new BatchStockCheckResult(shortages.Count > 0, shortages);
        return (BatchAccessResult.Success, result, null);
    }

    private async Task DeductIngredientStockAsync(string userId, BatchIngredient item)
    {
        if (item.Amount <= 0)
        {
            item.DeductedAmount = 0m;
            item.DeductedUnit = item.Unit;
            return;
        }

        var stock = item.SourceIngredientId.HasValue
            ? (_db.IngredientStocks.Local.FirstOrDefault(s => s.UserId == userId && s.IngredientId == item.SourceIngredientId.Value)
               ?? await _db.IngredientStocks.FirstOrDefaultAsync(s => s.UserId == userId && s.IngredientId == item.SourceIngredientId.Value))
            : null;

        if (stock == null)
        {
            var ingredient = item.SourceIngredientId.HasValue
                ? await _db.Ingredients.FirstOrDefaultAsync(i =>
                    i.Id == item.SourceIngredientId.Value &&
                    (i.IsCatalogItem || i.CreatedByUserId == userId))
                : null;

            if (ingredient == null)
            {
                ingredient = await _db.Ingredients.FirstOrDefaultAsync(i =>
                    (i.IsCatalogItem || i.CreatedByUserId == userId) &&
                    i.Name.ToLower() == item.Name.ToLower());
            }

            if (ingredient != null)
            {
                stock = _db.IngredientStocks.Local.FirstOrDefault(s => s.UserId == userId && s.IngredientId == ingredient.Id)
                    ?? await _db.IngredientStocks.FirstOrDefaultAsync(s => s.UserId == userId && s.IngredientId == ingredient.Id);
                if (stock == null)
                {
                    stock = new IngredientStock
                    {
                        UserId = userId,
                        IngredientId = ingredient.Id,
                        Amount = 0.0m,
                        Unit = item.Unit,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.IngredientStocks.Add(stock);
                }
            }
        }

        if (stock != null)
        {
            var previousAmount = stock.Amount;
            var amountToDeduct = ConvertUnitAmount(item.Amount, item.Unit, stock.Unit);
            var newAmount = Math.Max(0.0m, previousAmount - amountToDeduct);
            var actualDeducted = Math.Round(previousAmount - newAmount, 3);
            stock.Amount = newAmount;
            stock.UpdatedAt = DateTime.UtcNow;
            item.DeductedAmount = actualDeducted;
            item.DeductedUnit = stock.Unit;
        }
        else
        {
            item.DeductedAmount = 0m;
            item.DeductedUnit = item.Unit;
        }
    }

    private async Task RefundIngredientStockAsync(string userId, BatchIngredient item)
    {
        if (item.Amount <= 0 || (item.DeductedAmount.HasValue && item.DeductedAmount.Value <= 0m))
        {
            item.DeductedAmount = null;
            item.DeductedUnit = null;
            return;
        }

        var stock = item.SourceIngredientId.HasValue
            ? (_db.IngredientStocks.Local.FirstOrDefault(s => s.UserId == userId && s.IngredientId == item.SourceIngredientId.Value)
               ?? await _db.IngredientStocks.FirstOrDefaultAsync(s => s.UserId == userId && s.IngredientId == item.SourceIngredientId.Value))
            : null;

        if (stock == null)
        {
            var ingredient = item.SourceIngredientId.HasValue
                ? await _db.Ingredients.FirstOrDefaultAsync(i =>
                    i.Id == item.SourceIngredientId.Value &&
                    (i.IsCatalogItem || i.CreatedByUserId == userId))
                : null;

            if (ingredient == null)
            {
                ingredient = await _db.Ingredients.FirstOrDefaultAsync(i =>
                    (i.IsCatalogItem || i.CreatedByUserId == userId) &&
                    i.Name.ToLower() == item.Name.ToLower());
            }

            if (ingredient != null)
            {
                stock = _db.IngredientStocks.Local.FirstOrDefault(s => s.UserId == userId && s.IngredientId == ingredient.Id)
                    ?? await _db.IngredientStocks.FirstOrDefaultAsync(s => s.UserId == userId && s.IngredientId == ingredient.Id);
            }
        }

        if (stock != null)
        {
            var refundSourceAmount = item.DeductedAmount ?? item.Amount;
            var refundSourceUnit = item.DeductedAmount.HasValue ? (item.DeductedUnit ?? stock.Unit) : item.Unit;
            var amountToRestore = ConvertUnitAmount(refundSourceAmount, refundSourceUnit, stock.Unit);
            stock.Amount += amountToRestore;
            stock.UpdatedAt = DateTime.UtcNow;
        }

        item.DeductedAmount = null;
        item.DeductedUnit = null;
    }

    private static bool TryGetGramsFactor(string unit, out decimal gramsPerUnit)
    {
        switch (unit.Trim().ToLowerInvariant())
        {
            case "g":
            case "gram":
            case "grams":
                gramsPerUnit = 1m;
                return true;
            case "kg":
            case "kilogram":
            case "kilograms":
                gramsPerUnit = 1000m;
                return true;
            case "oz":
            case "ounce":
            case "ounces":
                gramsPerUnit = 28.3495231m;
                return true;
            case "lb":
            case "lbs":
            case "pound":
            case "pounds":
                gramsPerUnit = 453.59237m;
                return true;
            default:
                gramsPerUnit = 0m;
                return false;
        }
    }

    private static bool TryGetLitersFactor(string unit, out decimal litersPerUnit)
    {
        switch (unit.Trim().ToLowerInvariant())
        {
            case "ml":
            case "milliliter":
            case "milliliters":
                litersPerUnit = 0.001m;
                return true;
            case "l":
            case "liter":
            case "liters":
            case "litre":
            case "litres":
                litersPerUnit = 1m;
                return true;
            case "gal":
            case "gallon":
            case "gallons":
                litersPerUnit = GallonsToLitersFactor;
                return true;
            default:
                litersPerUnit = 0m;
                return false;
        }
    }

    public static bool IsMassUnit(string unit) =>
        !string.IsNullOrWhiteSpace(unit) && TryGetGramsFactor(unit, out _);

    public static decimal ConvertUnitAmount(decimal amount, string sourceUnit, string targetUnit)
    {
        if (string.IsNullOrWhiteSpace(sourceUnit) || string.IsNullOrWhiteSpace(targetUnit))
            return amount;

        if (string.Equals(sourceUnit.Trim(), targetUnit.Trim(), StringComparison.OrdinalIgnoreCase))
            return amount;

        if (TryGetGramsFactor(sourceUnit, out var srcGrams) && TryGetGramsFactor(targetUnit, out var tgtGrams))
        {
            return (amount * srcGrams) / tgtGrams;
        }

        if (TryGetLitersFactor(sourceUnit, out var srcLiters) && TryGetLitersFactor(targetUnit, out var tgtLiters))
        {
            return (amount * srcLiters) / tgtLiters;
        }

        return amount;
    }

    public static int GetStageOrderPriority(IngredientUsage usage) => usage switch
    {
        IngredientUsage.Mash => 1,
        IngredientUsage.FirstWort => 2,
        IngredientUsage.Boil => 2,
        IngredientUsage.Whirlpool => 3,
        IngredientUsage.Primary => 4,
        IngredientUsage.Secondary => 5,
        IngredientUsage.DryHop => 6,
        IngredientUsage.Bottling => 7,
        _ => 8
    };

    public static IEnumerable<BatchIngredient> OrderIngredientsChronologically(IEnumerable<BatchIngredient> ingredients)
    {
        return ingredients
            .OrderBy(i => GetStageOrderPriority(i.AdditionStage))
            // First Wort Hops come right after Mash and before regular Boil additions
            .ThenBy(i => i.AdditionStage == IngredientUsage.FirstWort ? 0 : 1)
            // For FirstWort, Boil & Whirlpool: time is minutes remaining until flameout -> highest time is pitched first
            .ThenByDescending(i => (i.AdditionStage == IngredientUsage.FirstWort || i.AdditionStage == IngredientUsage.Boil || i.AdditionStage == IngredientUsage.Whirlpool) ? (i.AdditionTimeMinutes ?? -1) : 0)
            // For Mash: grains first, largest amount first
            .ThenBy(i => i.AdditionStage == IngredientUsage.Mash && i.Type == IngredientType.Fermentable ? 0 : 1)
            .ThenByDescending(i => i.AdditionStage == IngredientUsage.Mash ? i.Amount : 0)
            // For Primary: yeast first
            .ThenBy(i => i.AdditionStage == IngredientUsage.Primary && i.Type == IngredientType.Yeast ? 0 : 1)
            // DryHop / Secondary: days/minutes ascending
            .ThenBy(i => (i.AdditionStage == IngredientUsage.DryHop || i.AdditionStage == IngredientUsage.Secondary) ? (i.AdditionTimeMinutes ?? 0) : 0)
            .ThenByDescending(i => i.Amount)
            .ThenBy(i => i.Name);
    }

    public static IEnumerable<RecipeIngredient> OrderIngredientsChronologically(IEnumerable<RecipeIngredient> ingredients)
    {
        return ingredients
            .OrderBy(i => GetStageOrderPriority(i.Usage))
            // First Wort Hops come right after Mash and before regular Boil additions
            .ThenBy(i => i.Usage == IngredientUsage.FirstWort ? 0 : 1)
            // For FirstWort, Boil & Whirlpool: time is minutes remaining until flameout -> highest time is pitched first
            .ThenByDescending(i => (i.Usage == IngredientUsage.FirstWort || i.Usage == IngredientUsage.Boil || i.Usage == IngredientUsage.Whirlpool) ? (i.DurationMinutes ?? -1) : 0)
            // For Mash: grains first, largest amount first
            .ThenBy(i => i.Usage == IngredientUsage.Mash && i.Ingredient?.Type == IngredientType.Fermentable ? 0 : 1)
            .ThenByDescending(i => i.Usage == IngredientUsage.Mash ? i.Amount : 0)
            // For Primary: yeast first
            .ThenBy(i => i.Usage == IngredientUsage.Primary && i.Ingredient?.Type == IngredientType.Yeast ? 0 : 1)
            // DryHop / Secondary: days/minutes ascending
            .ThenBy(i => (i.Usage == IngredientUsage.DryHop || i.Usage == IngredientUsage.Secondary) ? (i.DurationMinutes ?? 0) : 0)
            .ThenByDescending(i => i.Amount)
            .ThenBy(i => i.Ingredient?.Name ?? string.Empty);
    }

    private static List<BatchIngredient> GetIngredientsForStage(IEnumerable<BatchIngredient> ingredients, BrewStage stage)
    {
        var filtered = stage switch
        {
            BrewStage.Mash => ingredients.Where(i => i.AdditionStage == IngredientUsage.Mash),
            BrewStage.Boil => ingredients.Where(i => i.AdditionStage == IngredientUsage.FirstWort ||
                                                    i.AdditionStage == IngredientUsage.Boil ||
                                                    i.AdditionStage == IngredientUsage.Whirlpool ||
                                                    (i.Type == IngredientType.Hop &&
                                                     i.AdditionStage != IngredientUsage.DryHop &&
                                                     i.AdditionStage != IngredientUsage.Mash &&
                                                     i.AdditionStage != IngredientUsage.Primary &&
                                                     i.AdditionStage != IngredientUsage.Secondary &&
                                                     i.AdditionStage != IngredientUsage.Bottling)),
            BrewStage.Ferment => ingredients.Where(i => i.AdditionStage == IngredientUsage.Primary ||
                                                       i.AdditionStage == IngredientUsage.DryHop ||
                                                       (i.Type == IngredientType.Yeast &&
                                                        i.AdditionStage != IngredientUsage.Secondary &&
                                                        i.AdditionStage != IngredientUsage.Bottling)),
            BrewStage.Condition => ingredients.Where(i => i.AdditionStage == IngredientUsage.Secondary),
            BrewStage.Package => ingredients.Where(i => i.AdditionStage == IngredientUsage.Bottling),
            _ => ingredients
        };

        return OrderIngredientsChronologically(filtered).ToList();
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    internal static DateTime NormalizeUtcTimestamp(DateTime timestamp) => timestamp.Kind switch
    {
        DateTimeKind.Utc => timestamp,
        DateTimeKind.Local => timestamp.ToUniversalTime(),
        _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
    };

    private DateTime ClampReadingTimestamp(DateTime? requestedTimestamp, DateOnly brewDate)
    {
        var now = UtcNow;
        if (!requestedTimestamp.HasValue)
        {
            return now;
        }

        var utc = NormalizeUtcTimestamp(requestedTimestamp.Value);
        var minAllowed = brewDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1);
        var maxAllowed = now.AddMinutes(5);

        if (utc < minAllowed) return minAllowed;
        if (utc > maxAllowed) return maxAllowed;
        return utc;
    }

    private static bool IsEquipmentLinkedToBatch(Batch batch, Guid equipmentId) =>
        batch.HltId == equipmentId ||
        batch.MashTunId == equipmentId ||
        batch.BoilerId == equipmentId ||
        batch.FermenterId == equipmentId ||
        batch.PackagingVesselId == equipmentId ||
        batch.SensorAssignments.Any(sa => sa.EquipmentId == equipmentId);

    private Task<bool> IsEquipmentOccupiedByOtherBatchAsync(
        Equipment equipment,
        Guid? excludeBatchId) =>
        BatchVesselReconciler.IsEquipmentOccupiedByOtherBatchAsync(_db, equipment, excludeBatchId);
}