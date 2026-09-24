using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace BrewYou.ApiService.Services;

public class TelemetryService : ITelemetryService
{
    public const decimal MinTemperatureC = -20.0m;
    public const decimal MaxTemperatureC = 120.0m;
    private const int MaxPayloadLogLength = 1000;

    private readonly BrewYouDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly ITelemetryBroadcastService _broadcastService;
    private readonly ILogger<TelemetryService> _logger;

    private readonly List<ITelemetryParser> _parsers =
    [
        new UniversalBrewYouTelemetryParser(),
        new ISpindelTelemetryParser(),
        new TiltTelemetryParser(),
        new GenericJsonTelemetryParser()
    ];

    public TelemetryService(
        BrewYouDbContext db,
        HttpClient httpClient,
        ITelemetryBroadcastService broadcastService,
        ILogger<TelemetryService> logger)
    {
        _db = db;
        _httpClient = httpClient;
        _broadcastService = broadcastService;
        _logger = logger;
    }

    public NormalizedTelemetryData? NormalizeJsonPayload(JsonElement root, Equipment equipment, string? preferredPath = null)
    {
        foreach (var parser in _parsers.OrderBy(p => p.Priority))
        {
            if (parser.CanParse(root, equipment, preferredPath))
            {
                var result = parser.Parse(root, equipment, preferredPath);
                if (result != null) return result;
            }
        }
        return null;
    }

    public async Task<TelemetryIngestResult> IngestTelemetryAsync(string token, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new TelemetryIngestResult(TelemetryIngestStatus.NotFound, null, "Invalid or missing token.");
        }

        var normalizedToken = token.Trim();
        var equipment = await _db.Equipment
            .FirstOrDefaultAsync(e => e.ConnectionToken == normalizedToken);

        if (equipment == null)
        {
            _logger.LogWarning("Telemetry rejected: No equipment matching token {TokenPrefix}...",
                normalizedToken.Length > 8 ? normalizedToken[..8] : normalizedToken);
            return new TelemetryIngestResult(TelemetryIngestStatus.NotFound, null, "Equipment not found for provided token.");
        }

        NormalizedTelemetryData? normalized = null;
        if (payload.ValueKind == JsonValueKind.Object)
        {
            normalized = NormalizeJsonPayload(payload, equipment);
        }

        decimal? tempC = normalized?.TemperatureC;
        var detectedFormat = normalized?.DetectedFormat;

        if (!tempC.HasValue)
        {
            var parseResult = ParseTemperature(payload);
            if (parseResult.Success && parseResult.TemperatureC.HasValue)
            {
                tempC = parseResult.TemperatureC.Value;
                detectedFormat = parseResult.DetectedFormat ?? "HttpPush";
            }
            else
            {
                return new TelemetryIngestResult(TelemetryIngestStatus.InvalidPayload, null,
                    parseResult.ErrorMessage ?? "Could not extract valid temperature from payload.");
            }
        }

        var finalTempC = tempC.Value;
        if (finalTempC < MinTemperatureC || finalTempC > MaxTemperatureC)
        {
            _logger.LogWarning("Telemetry temperature {TempC} °C out of plausible range [{Min}, {Max}] for equipment {EquipmentId}",
                finalTempC, MinTemperatureC, MaxTemperatureC, equipment.Id);
            return new TelemetryIngestResult(TelemetryIngestStatus.OutOfRange, finalTempC,
                $"Temperature {finalTempC} °C is outside plausible brewing range ({MinTemperatureC} °C to {MaxTemperatureC} °C).");
        }

        var now = DateTime.UtcNow;
        var gravity = normalized?.SpecificGravity ?? ParseGravity(payload);
        var pressure = normalized?.PressureBar;
        var batteryPct = normalized?.BatteryPercent;
        var batteryVolt = normalized?.BatteryVoltage;
        var tilt = normalized?.TiltDegrees;
        var rssi = normalized?.Rssi;
        var metricsJson = normalized?.MetricsJson;

        equipment.CurrentTemperatureC = finalTempC;
        equipment.TemperatureUpdatedAt = now;
        equipment.LastTelemetryAt = now;
        if (gravity.HasValue) equipment.CurrentSpecificGravity = gravity;
        if (pressure.HasValue) equipment.CurrentPressureBar = pressure;
        if (batteryPct.HasValue) equipment.CurrentBatteryPercent = batteryPct;
        if (batteryVolt.HasValue) equipment.CurrentBatteryVoltage = batteryVolt;
        if (!string.IsNullOrEmpty(metricsJson)) equipment.LatestMetricsJson = metricsJson;

        var rawString = payload.ValueKind != JsonValueKind.Undefined
            ? payload.ToString()
            : null;
        if (rawString != null && rawString.Length > MaxPayloadLogLength)
        {
            rawString = rawString[..MaxPayloadLogLength];
        }

        var (reading, targetTempC, recordedGravity) = await CreateAndAssociateReadingAsync(
            equipment,
            finalTempC,
            gravity,
            detectedFormat ?? "HttpPush",
            rawString,
            now,
            pressure,
            batteryPct,
            batteryVolt,
            tilt,
            rssi,
            metricsJson);

        await _db.SaveChangesAsync();
        BroadcastIfAssociated(reading, equipment, targetTempC, recordedGravity);

        _logger.LogInformation("Updated temperature to {TempC} °C (gravity: {Gravity}, pressure: {Pressure}) for equipment {EquipmentId} ({EquipmentName}) via {Source}",
            finalTempC, recordedGravity, pressure, equipment.Id, equipment.Name, reading.Source);

        return new TelemetryIngestResult(TelemetryIngestStatus.Success, finalTempC, null, now);
    }

    public async Task<TelemetryIngestResult> IngestMqttTelemetryAsync(Guid equipmentId, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new TelemetryIngestResult(TelemetryIngestStatus.InvalidPayload, null, "Payload cannot be empty.");
        }

        if (payload.Length > 16384)
        {
            return new TelemetryIngestResult(TelemetryIngestStatus.InvalidPayload, null, "Payload exceeds maximum length of 16KB.");
        }

        var equipment = await _db.Equipment
            .FirstOrDefaultAsync(e => e.Id == equipmentId);

        if (equipment == null)
        {
            _logger.LogWarning("MQTT telemetry rejected: Equipment {EquipmentId} not found", equipmentId);
            return new TelemetryIngestResult(TelemetryIngestStatus.NotFound, null, "Equipment not found.");
        }

        decimal? tempC = null;
        decimal? gravity = null;
        decimal? pressure = null;
        decimal? batteryPct = null;
        decimal? batteryVolt = null;
        decimal? tilt = null;
        short? rssi = null;
        string? metricsJson = null;
        var format = "Mqtt";

        var trimmed = payload.Trim();

        // 1. Try parsing as JSON first
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var normalized = NormalizeJsonPayload(doc.RootElement, equipment);
            if (normalized != null && normalized.TemperatureC != 0)
            {
                tempC = normalized.TemperatureC;
                gravity = normalized.SpecificGravity;
                pressure = normalized.PressureBar;
                batteryPct = normalized.BatteryPercent;
                batteryVolt = normalized.BatteryVoltage;
                tilt = normalized.TiltDegrees;
                rssi = normalized.Rssi;
                metricsJson = normalized.MetricsJson;
                format = normalized.DetectedFormat ?? "MqttJson";
            }
            else
            {
                var parseResult = ParseTemperature(doc.RootElement);
                if (parseResult.Success && parseResult.TemperatureC.HasValue)
                {
                    tempC = parseResult.TemperatureC.Value;
                    format = parseResult.DetectedFormat ?? "MqttJson";
                    gravity = ParseGravity(doc.RootElement);
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON, try scalar numeric parsing below
        }

        // 2. Fallback: Parse plain scalar number (e.g. "68.5", "20.1 C", "68.2 F")
        if (!tempC.HasValue)
        {
            var scalarResult = ParseScalarTemperature(trimmed);
            if (scalarResult.Success && scalarResult.TemperatureC.HasValue)
            {
                tempC = scalarResult.TemperatureC.Value;
                format = scalarResult.DetectedFormat ?? "MqttScalar";
            }
        }

        if (!tempC.HasValue)
        {
            return new TelemetryIngestResult(TelemetryIngestStatus.InvalidPayload, null, "Could not parse temperature from MQTT payload.");
        }

        if (tempC.Value < MinTemperatureC || tempC.Value > MaxTemperatureC)
        {
            _logger.LogWarning("MQTT temperature {TempC} °C out of plausible range [{Min}, {Max}] for equipment {EquipmentId}",
                tempC.Value, MinTemperatureC, MaxTemperatureC, equipment.Id);
            return new TelemetryIngestResult(TelemetryIngestStatus.OutOfRange, tempC,
                $"Temperature {tempC.Value} °C is outside plausible brewing range ({MinTemperatureC} °C to {MaxTemperatureC} °C).");
        }

        var now = DateTime.UtcNow;
        equipment.CurrentTemperatureC = tempC.Value;
        equipment.TemperatureUpdatedAt = now;
        equipment.LastTelemetryAt = now;
        if (gravity.HasValue) equipment.CurrentSpecificGravity = gravity;
        if (pressure.HasValue) equipment.CurrentPressureBar = pressure;
        if (batteryPct.HasValue) equipment.CurrentBatteryPercent = batteryPct;
        if (batteryVolt.HasValue) equipment.CurrentBatteryVoltage = batteryVolt;
        if (!string.IsNullOrEmpty(metricsJson)) equipment.LatestMetricsJson = metricsJson;

        var snippet = trimmed.Length > MaxPayloadLogLength ? trimmed[..MaxPayloadLogLength] : trimmed;

        var (reading, targetTempC, recordedGravity) = await CreateAndAssociateReadingAsync(
            equipment,
            tempC.Value,
            gravity,
            format,
            snippet,
            now,
            pressure,
            batteryPct,
            batteryVolt,
            tilt,
            rssi,
            metricsJson);

        await _db.SaveChangesAsync();
        BroadcastIfAssociated(reading, equipment, targetTempC, recordedGravity);

        _logger.LogInformation("Updated temperature to {TempC} °C via MQTT for equipment {EquipmentId} ({EquipmentName})",
            tempC.Value, equipment.Id, equipment.Name);

        return new TelemetryIngestResult(TelemetryIngestStatus.Success, tempC.Value, null, now);
    }

    public async Task<(bool Found, List<EquipmentReadingDto>? Readings)> GetEquipmentReadingsAsync(
        Guid equipmentId, string userId, int limit = 50)
    {
        var equipmentExists = await _db.Equipment.AsNoTracking()
            .AnyAsync(e => e.Id == equipmentId && e.UserId == userId);

        if (!equipmentExists)
        {
            return (false, null);
        }

        limit = Math.Clamp(limit, 1, 200);

        var readings = await _db.EquipmentReadings.AsNoTracking()
            .Where(r => r.EquipmentId == equipmentId)
            .OrderByDescending(r => r.Timestamp)
            .Take(limit)
            .Select(r => new EquipmentReadingDto(
                r.Id,
                r.EquipmentId,
                r.Timestamp,
                r.TemperatureC,
                r.Source,
                r.CreatedAt,
                r.SpecificGravity,
                r.PressureBar,
                r.BatteryPercent,
                r.BatteryVoltage,
                r.TiltDegrees,
                r.Rssi,
                r.MetricsJson
            ))
            .ToListAsync();

        return (true, readings);
    }

    public async Task<TelemetryPollResult> TestPollAsync(Guid equipmentId, string userId)
    {
        var equipment = await _db.Equipment.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == equipmentId && e.UserId == userId);

        if (equipment == null)
        {
            return new TelemetryPollResult(false, null, "Equipment not found.");
        }

        return await ExecutePollAsync(equipment, isTestOnly: true);
    }

    public async Task<TelemetryPollResult> PollEquipmentAsync(Guid equipmentId)
    {
        var equipment = await _db.Equipment
            .FirstOrDefaultAsync(e => e.Id == equipmentId);

        if (equipment == null)
        {
            return new TelemetryPollResult(false, null, "Equipment not found.");
        }

        return await ExecutePollAsync(equipment, isTestOnly: false);
    }

    private async Task<TelemetryPollResult> ExecutePollAsync(Equipment equipment, bool isTestOnly)
    {
        if (string.IsNullOrWhiteSpace(equipment.ConnectionConfigJson))
        {
            return new TelemetryPollResult(false, null, "No polling configuration specified.");
        }

        string? targetUrl = null;
        string? jsonPath = null;

        try
        {
            using var doc = JsonDocument.Parse(equipment.ConnectionConfigJson);
            if (doc.RootElement.TryGetProperty("url", out var urlProp) ||
                doc.RootElement.TryGetProperty("Url", out urlProp))
            {
                targetUrl = urlProp.GetString();
            }

            if (doc.RootElement.TryGetProperty("jsonPath", out var pathProp) ||
                doc.RootElement.TryGetProperty("JsonPath", out pathProp))
            {
                jsonPath = pathProp.GetString();
            }
        }
        catch (JsonException)
        {
            return new TelemetryPollResult(false, null, "Invalid polling configuration JSON.");
        }

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return new TelemetryPollResult(false, null, "Poll URL is required.");
        }

        var ssrfValidation = ValidatePollUrl(targetUrl);
        if (!ssrfValidation.IsValid)
        {
            return new TelemetryPollResult(false, null, ssrfValidation.ErrorMessage);
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            request.Headers.Add("User-Agent", "BrewYou-Telemetry-Poller/1.0");

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new TelemetryPollResult(false, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            var content = await response.Content.ReadAsStringAsync(cts.Token);
            var snippet = content.Length > 200 ? content[..200] + "..." : content;

            using var payloadDoc = JsonDocument.Parse(content);
            var normalized = NormalizeJsonPayload(payloadDoc.RootElement, equipment, jsonPath);
            decimal? tempC = normalized?.TemperatureC;

            if (!tempC.HasValue)
            {
                var parseResult = ParseTemperature(payloadDoc.RootElement, jsonPath);
                if (parseResult.Success && parseResult.TemperatureC.HasValue)
                {
                    tempC = parseResult.TemperatureC.Value;
                }
                else
                {
                    return new TelemetryPollResult(false, null,
                        parseResult.ErrorMessage ?? "Could not find temperature in response JSON.", snippet);
                }
            }

            var finalTempC = tempC.Value;
            if (finalTempC < MinTemperatureC || finalTempC > MaxTemperatureC)
            {
                return new TelemetryPollResult(false, finalTempC,
                    $"Temperature {finalTempC} °C is outside plausible range [{MinTemperatureC}, {MaxTemperatureC}].", snippet);
            }

            if (!isTestOnly)
            {
                var now = DateTime.UtcNow;
                var gravity = normalized?.SpecificGravity ?? ParseGravity(payloadDoc.RootElement);
                var pressure = normalized?.PressureBar;
                var batteryPct = normalized?.BatteryPercent;
                var batteryVolt = normalized?.BatteryVoltage;
                var tilt = normalized?.TiltDegrees;
                var rssi = normalized?.Rssi;
                var metricsJson = normalized?.MetricsJson;

                equipment.CurrentTemperatureC = finalTempC;
                equipment.TemperatureUpdatedAt = now;
                equipment.LastTelemetryAt = now;
                if (gravity.HasValue) equipment.CurrentSpecificGravity = gravity;
                if (pressure.HasValue) equipment.CurrentPressureBar = pressure;
                if (batteryPct.HasValue) equipment.CurrentBatteryPercent = batteryPct;
                if (batteryVolt.HasValue) equipment.CurrentBatteryVoltage = batteryVolt;
                if (!string.IsNullOrEmpty(metricsJson)) equipment.LatestMetricsJson = metricsJson;

                var (reading, targetTempC, recordedGravity) = await CreateAndAssociateReadingAsync(
                    equipment,
                    finalTempC,
                    gravity,
                    "HttpPoll",
                    snippet,
                    now,
                    pressure,
                    batteryPct,
                    batteryVolt,
                    tilt,
                    rssi,
                    metricsJson);

                await _db.SaveChangesAsync();
                BroadcastIfAssociated(reading, equipment, targetTempC, recordedGravity);
            }

            return new TelemetryPollResult(true, finalTempC, null, snippet);
        }
        catch (TaskCanceledException)
        {
            return new TelemetryPollResult(false, null, "Request timed out (5s limit).");
        }
        catch (HttpRequestException ex)
        {
            return new TelemetryPollResult(false, null, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to poll equipment {EquipmentId} from {Url}", equipment.Id, targetUrl);
            return new TelemetryPollResult(false, null, $"Polling error: {ex.Message}");
        }
    }

    private async Task<(EquipmentReading Reading, decimal? TargetTempC, decimal? Gravity)> CreateAndAssociateReadingAsync(
        Equipment equipment,
        decimal tempC,
        decimal? gravity,
        string source,
        string? rawPayload,
        DateTime timestamp,
        decimal? pressureBar = null,
        decimal? batteryPercent = null,
        decimal? batteryVoltage = null,
        decimal? tiltDegrees = null,
        short? rssi = null,
        string? metricsJson = null)
    {
        var reading = new EquipmentReading
        {
            EquipmentId = equipment.Id,
            Timestamp = timestamp,
            TemperatureC = tempC,
            SpecificGravity = gravity,
            PressureBar = pressureBar,
            BatteryPercent = batteryPercent,
            BatteryVoltage = batteryVoltage,
            TiltDegrees = tiltDegrees,
            Rssi = rssi,
            MetricsJson = metricsJson,
            Source = source,
            RawPayload = rawPayload,
            CreatedAt = timestamp
        };

        decimal? targetTempC = null;
        decimal? recordedGravity = gravity;

        // Check if this equipment is currently in use for the active stage of an in-progress batch
        var activeBatch = await _db.Batches
            .Include(b => b.MashSteps)
            .Include(b => b.SensorAssignments)
            .FirstOrDefaultAsync(b =>
                b.UserId == equipment.UserId &&
                (b.Status == BatchStatus.Brewing || b.Status == BatchStatus.Fermenting || b.Status == BatchStatus.Conditioning) &&
                (
                    (equipment.Id == b.BoilerId && (b.CurrentStage == BrewStage.Mash || b.CurrentStage == BrewStage.Boil)) ||
                    (equipment.Id == b.FermenterId && (b.CurrentStage == BrewStage.Ferment || b.CurrentStage == BrewStage.Condition)) ||
                    (equipment.Id == b.PackagingVesselId && b.CurrentStage == BrewStage.Package) ||
                    ((equipment.Type == EquipmentType.Sensor || equipment.Type == EquipmentType.Other) && b.SensorAssignments.Any(sa =>
                        sa.EquipmentId == equipment.Id &&
                        (sa.Stage == null || sa.Stage == b.CurrentStage)))
                ));

        if (activeBatch != null)
        {
            if (equipment.Type == EquipmentType.Sensor || equipment.Type == EquipmentType.Other)
            {
                // Find matching sensor assignment for this batch
                var assignment = activeBatch.SensorAssignments
                    .FirstOrDefault(sa => sa.EquipmentId == equipment.Id && (sa.Stage == null || sa.Stage == activeBatch.CurrentStage));

                if (assignment != null)
                {
                    reading.BatchId = activeBatch.Id;
                    reading.Stage = activeBatch.CurrentStage;

                    if (activeBatch.CurrentStage == BrewStage.Mash)
                    {
                        BatchMashStep? targetStep = null;
                        if (assignment.BatchMashStepId.HasValue)
                        {
                            targetStep = activeBatch.MashSteps.FirstOrDefault(s => s.Id == assignment.BatchMashStepId.Value && !s.IsCompleted);
                        }
                        else
                        {
                            targetStep = activeBatch.MashSteps.OrderBy(s => s.StepOrder).FirstOrDefault(s => !s.IsCompleted);
                        }

                        if (targetStep != null)
                        {
                            reading.BatchMashStepId = targetStep.Id;
                            reading.StepName = targetStep.Name;
                            targetTempC = targetStep.TargetTemperatureC;
                            targetStep.ActualTemperatureC = tempC;
                        }
                        else
                        {
                            reading.StepName = "Mash";
                        }
                    }
                    else if (activeBatch.CurrentStage == BrewStage.Boil)
                    {
                        reading.StepName = "Boil";
                        targetTempC = 100.0m;
                    }
                    else if (activeBatch.CurrentStage == BrewStage.Ferment)
                    {
                        reading.StepName = "Ferment";
                        targetTempC = activeBatch.PitchTemperatureC ?? 20.0m;
                    }
                    else if (activeBatch.CurrentStage == BrewStage.Condition)
                    {
                        reading.StepName = "Condition";
                        targetTempC = 2.0m;
                    }
                    else if (activeBatch.CurrentStage == BrewStage.Package)
                    {
                        reading.StepName = "Package";
                    }
                }
            }
            else if (equipment.Id == activeBatch.BoilerId && (activeBatch.CurrentStage == BrewStage.Mash || activeBatch.CurrentStage == BrewStage.Boil))
            {
                if (activeBatch.CurrentStage == BrewStage.Mash)
                {
                    reading.BatchId = activeBatch.Id;
                    reading.Stage = BrewStage.Mash;

                    var activeStep = activeBatch.MashSteps
                        .OrderBy(s => s.StepOrder)
                        .FirstOrDefault(s => !s.IsCompleted);

                    if (activeStep != null)
                    {
                        reading.BatchMashStepId = activeStep.Id;
                        reading.StepName = activeStep.Name;
                        targetTempC = activeStep.TargetTemperatureC;
                        activeStep.ActualTemperatureC = tempC;
                    }
                    else
                    {
                        reading.StepName = "Mash";
                    }
                }
                else if (activeBatch.CurrentStage == BrewStage.Boil)
                {
                    reading.BatchId = activeBatch.Id;
                    reading.Stage = BrewStage.Boil;
                    reading.StepName = "Boil";
                    targetTempC = 100.0m;
                }
            }
            else if (equipment.Id == activeBatch.FermenterId && (activeBatch.CurrentStage == BrewStage.Ferment || activeBatch.CurrentStage == BrewStage.Condition))
            {
                if (activeBatch.CurrentStage == BrewStage.Ferment)
                {
                    reading.BatchId = activeBatch.Id;
                    reading.Stage = BrewStage.Ferment;
                    reading.StepName = "Ferment";
                    targetTempC = activeBatch.PitchTemperatureC ?? 20.0m;
                }
                else if (activeBatch.CurrentStage == BrewStage.Condition)
                {
                    reading.BatchId = activeBatch.Id;
                    reading.Stage = BrewStage.Condition;
                    reading.StepName = "Condition";
                    targetTempC = 2.0m;
                }
            }
            else if (equipment.Id == activeBatch.PackagingVesselId && activeBatch.CurrentStage == BrewStage.Package)
            {
                reading.BatchId = activeBatch.Id;
                reading.Stage = BrewStage.Package;
                reading.StepName = "Package";
            }

            // If reading is associated with a fermentation or conditioning stage batch and carries gravity, persist BatchReading
            if (reading.BatchId.HasValue &&
                (activeBatch.CurrentStage == BrewStage.Ferment || activeBatch.CurrentStage == BrewStage.Condition) &&
                gravity.HasValue)
            {
                recordedGravity = gravity.Value;
                var batchReading = new BatchReading
                {
                    Id = Guid.NewGuid(),
                    BatchId = activeBatch.Id,
                    Timestamp = timestamp,
                    SpecificGravity = gravity.Value,
                    TemperatureC = tempC,
                    Notes = $"Telemetry from {equipment.Name} ({source})"
                };
                _db.BatchReadings.Add(batchReading);

                activeBatch.CurrentGravity = gravity.Value;
                var effectiveOg = activeBatch.MeasuredOg ?? (activeBatch.TargetOg > 1.000m ? activeBatch.TargetOg : (decimal?)null);
                if (effectiveOg.HasValue && effectiveOg.Value > gravity.Value)
                {
                    activeBatch.AlcoholByVolume = BrewingCalculator.CalculateActualAbv(effectiveOg.Value, gravity.Value);
                }
            }
        }

        _db.EquipmentReadings.Add(reading);
        return (reading, targetTempC, recordedGravity);
    }

    private void BroadcastIfAssociated(EquipmentReading reading, Equipment equipment, decimal? targetTempC, decimal? gravity = null)
    {
        // 1. Broadcast to Equipment Telemetry stream for the equipment owner
        var fillPercentage = equipment.CapacityLiters > 0
            ? Math.Round(Math.Clamp((equipment.CurrentVolumeLiters / equipment.CapacityLiters) * 100m, 0m, 100m), 1)
            : 0m;

        var equipmentUpdate = new EquipmentTelemetryUpdateDto(
            equipment.Id,
            equipment.Name,
            reading.TemperatureC,
            reading.Timestamp,
            reading.Source,
            reading.BatchId,
            gravity ?? reading.SpecificGravity,
            equipment.CurrentVolumeLiters,
            fillPercentage,
            reading.PressureBar,
            reading.BatteryPercent,
            reading.BatteryVoltage,
            reading.TiltDegrees,
            reading.Rssi,
            reading.MetricsJson
        );
        _broadcastService.BroadcastEquipmentReading(equipment.UserId, equipmentUpdate);

        // 2. Broadcast to Batch Telemetry stream if associated with an active batch
        if (!reading.BatchId.HasValue)
        {
            return;
        }

        var dto = new BatchEquipmentReadingDto(
            reading.Id,
            equipment.Id,
            equipment.Name,
            reading.BatchId.Value,
            reading.Stage,
            reading.BatchMashStepId,
            reading.StepName,
            reading.TemperatureC,
            reading.Timestamp,
            reading.Source,
            targetTempC,
            reading.Notes,
            gravity ?? reading.SpecificGravity,
            reading.PressureBar,
            reading.BatteryPercent,
            reading.BatteryVoltage,
            reading.TiltDegrees,
            reading.Rssi,
            reading.MetricsJson
        );

        _broadcastService.BroadcastReading(dto);
    }

    public static (bool IsValid, string? ErrorMessage) ValidatePollUrl(string urlString)
    {
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
        {
            return (false, "Invalid URL format.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return (false, "Only HTTP and HTTPS URLs are allowed.");
        }

        // SSRF Defense: Block cloud metadata services
        var host = uri.Host.ToLowerInvariant();
        if (host == "169.254.169.254" || host == "metadata.google.internal" || host == "100.100.100.200")
        {
            return (false, "Access to cloud metadata endpoints is prohibited.");
        }

        return (true, null);
    }

    public static TemperatureParseResult ParseTemperature(JsonElement root, string? preferredPath = null)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return TemperatureParseResult.Fail("Payload must be a JSON object.");
        }

        // If a specific property path was configured, try it first
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var segments = preferredPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var current = root;
            var pathResolved = true;

            foreach (var segment in segments)
            {
                if (current.ValueKind == JsonValueKind.Object && TryGetPropertyInsensitive(current, segment, out var next))
                {
                    current = next;
                }
                else
                {
                    pathResolved = false;
                    break;
                }
            }

            if (pathResolved && TryExtractDecimal(current, out var explicitValue))
            {
                // Look for a companion unit property
                var unitStr = "C";
                if (TryGetPropertyInsensitive(root, "unit", out var u1) ||
                    TryGetPropertyInsensitive(root, "temp_units", out u1) ||
                    TryGetPropertyInsensitive(root, "temp_unit", out u1))
                {
                    unitStr = u1.GetString() ?? "C";
                }

                var tempInC = ConvertToCelsius(explicitValue, unitStr);
                return TemperatureParseResult.Ok(tempInC, "CustomPath");
            }
        }

        // 1. Check for Tilt Hydrometer format: {"color": "RED", "temp": 68.0, "gravity": 1.048}
        if (TryGetPropertyInsensitive(root, "color", out _) &&
            TryGetPropertyInsensitive(root, "temp", out var tiltTemp) &&
            TryExtractDecimal(tiltTemp, out var tiltF))
        {
            // Tilt always reports in Fahrenheit
            var tempC = ConvertToCelsius(tiltF, "F");
            return TemperatureParseResult.Ok(tempC, "TiltHydrometer");
        }

        // 2. Check for iSpindel format: {"name": "...", "temperature": 20.2, "temp_units": "C", ...}
        if (TryGetPropertyInsensitive(root, "temp_units", out var ispindelUnit) &&
            (TryGetPropertyInsensitive(root, "temperature", out var ispindelTemp) ||
             TryGetPropertyInsensitive(root, "temp", out ispindelTemp)) &&
            TryExtractDecimal(ispindelTemp, out var ispindelVal))
        {
            var tempC = ConvertToCelsius(ispindelVal, ispindelUnit.GetString() ?? "C");
            return TemperatureParseResult.Ok(tempC, "iSpindel");
        }

        // 3. Generic JSON format: {"temperature": 21.5, "unit": "C"} or {"temp": 70.0, "unit": "F"}
        JsonElement tempProp;
        if (TryGetPropertyInsensitive(root, "temperature", out tempProp) ||
            TryGetPropertyInsensitive(root, "temp", out tempProp) ||
            TryGetPropertyInsensitive(root, "current_temp", out tempProp) ||
            TryGetPropertyInsensitive(root, "currentTemperature", out tempProp))
        {
            if (TryExtractDecimal(tempProp, out var val))
            {
                var unit = "C";
                if (TryGetPropertyInsensitive(root, "unit", out var u) ||
                    TryGetPropertyInsensitive(root, "temp_unit", out u) ||
                    TryGetPropertyInsensitive(root, "units", out u))
                {
                    unit = u.GetString() ?? "C";
                }

                var tempC = ConvertToCelsius(val, unit);
                return TemperatureParseResult.Ok(tempC, "GenericJson");
            }
        }

        return TemperatureParseResult.Fail("No recognizeable temperature property found (expected 'temperature' or 'temp').");
    }

    private static bool TryGetPropertyInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryExtractDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (!string.IsNullOrWhiteSpace(str) &&
                decimal.TryParse(str, NumberStyles.Float | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    public static decimal ConvertToCelsius(decimal value, string unit)
    {
        var normalized = unit.Trim().ToUpperInvariant();
        if (normalized.StartsWith("F")) // F, FAHRENHEIT
        {
            var c = (value - 32m) * (5m / 9m);
            return Math.Round(c, 2);
        }

        return Math.Round(value, 2);
    }

    public static decimal? ParseGravity(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Check for common gravity property names: gravity, sg, specific_gravity, specificGravity
        if (TryGetPropertyInsensitive(root, "gravity", out var gProp) ||
            TryGetPropertyInsensitive(root, "sg", out gProp) ||
            TryGetPropertyInsensitive(root, "specific_gravity", out gProp) ||
            TryGetPropertyInsensitive(root, "specificGravity", out gProp))
        {
            if (TryExtractDecimal(gProp, out var val))
            {
                // Uncalibrated Tilt or raw gravity points (e.g. 1054 instead of 1.054)
                if (val >= 900m && val <= 1200m)
                {
                    val /= 1000m;
                }

                // Check plausible brewing gravity range (0.980 to 1.200)
                if (val >= 0.980m && val <= 1.200m)
                {
                    return Math.Round(val, 4);
                }
            }
        }

        return null;
    }

    public static TemperatureParseResult ParseScalarTemperature(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return TemperatureParseResult.Fail("Payload is empty.");
        }

        var trimmed = raw.Trim();
        var isFahrenheit = false;

        if (trimmed.EndsWith("F", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("°F", StringComparison.OrdinalIgnoreCase))
        {
            isFahrenheit = true;
            trimmed = trimmed.TrimEnd('F', 'f', '°', ' ');
        }
        else if (trimmed.EndsWith("C", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.EndsWith("°C", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.TrimEnd('C', 'c', '°', ' ');
        }

        if (decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedVal) ||
            decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out parsedVal))
        {
            // Plausible temperature bounds before conversion to prevent OverflowException on extreme exponents
            if (parsedVal < -100m || parsedVal > 300m)
            {
                return TemperatureParseResult.Fail("Temperature value out of plausible bounds.");
            }

            try
            {
                if (isFahrenheit)
                {
                    var converted = Math.Round((parsedVal - 32m) * 5m / 9m, 2);
                    return TemperatureParseResult.Ok(converted, "MqttScalarF");
                }
                return TemperatureParseResult.Ok(Math.Round(parsedVal, 2), "MqttScalarC");
            }
            catch (OverflowException)
            {
                return TemperatureParseResult.Fail("Numerical overflow calculating temperature.");
            }
        }

        return TemperatureParseResult.Fail("Could not parse scalar number.");
    }
}

public record TemperatureParseResult(
    bool Success,
    decimal? TemperatureC,
    string? DetectedFormat,
    string? ErrorMessage
)
{
    public static TemperatureParseResult Ok(decimal tempC, string format) =>
        new(true, tempC, format, null);

    public static TemperatureParseResult Fail(string error) =>
        new(false, null, null, error);
}