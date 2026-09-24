using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BrewYou.ApiService.Services;

public class EquipmentService : IEquipmentService
{
    private static readonly Regex MultipleWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private const decimal GallonsToLitersFactor = 3.785411784m;
    private const int MaxEquipmentPerUser = 100;

    private readonly BrewYouDbContext _db;
    private readonly ILogger<EquipmentService> _logger;
    private readonly IDataProtector? _protector;

    public EquipmentService(BrewYouDbContext db, ILogger<EquipmentService> logger, IDataProtectionProvider? dataProtectionProvider = null)
    {
        _db = db;
        _logger = logger;
        _protector = dataProtectionProvider?.CreateProtector("BrewYou.MqttCredentials");
    }

    public async Task<(List<EquipmentDto> Items, PaginationMeta Pagination)> GetEquipmentListAsync(
        string userId, Guid? setupId, EquipmentType? type, string? search, int page, int limit)
    {
        var userUnit = await GetUserPreferredUnitAsync(userId);

        var query = _db.Equipment.AsNoTracking().Where(e => e.UserId == userId);

        if (setupId.HasValue)
        {
            query = query.Where(e => e.BrewerySetupId == setupId.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(e => e.Type == type.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e =>
                EF.Functions.ILike(e.Name, $"%{term}%") ||
                (e.Description != null && EF.Functions.ILike(e.Description, $"%{term}%")));
        }

        var total = await query.CountAsync();
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);
        var totalPages = (int)Math.Ceiling(total / (double)limit);

        var entities = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var items = entities.Select(ToDto).ToList();

        var pagination = new PaginationMeta(page, limit, total, totalPages);
        return (items, pagination);
    }

    public async Task<(EquipmentAccessResult Result, EquipmentDto? Equipment)> GetEquipmentByIdAsync(Guid id, string userId)
    {
        var entity = await _db.Equipment.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

        if (entity == null)
        {
            return (EquipmentAccessResult.NotFound, null);
        }

        return (EquipmentAccessResult.Success, ToDto(entity));
    }

    public async Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> CreateEquipmentAsync(
        CreateEquipmentRequest request, string userId)
    {
        var count = await _db.Equipment.CountAsync(e => e.UserId == userId);
        if (count >= MaxEquipmentPerUser)
        {
            return (EquipmentAccessResult.QuotaExceeded, null, $"Equipment inventory limit reached (max {MaxEquipmentPerUser} items).");
        }

        BrewerySetup? targetSetup;
        var targetSetupId = request.BrewerySetupId;
        if (targetSetupId == Guid.Empty)
        {
            targetSetup = await _db.BrewerySetups
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.IsDefault)
                .ThenBy(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (targetSetup == null)
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
                var defaultName = user?.PreferredLanguage?.StartsWith("sv", StringComparison.OrdinalIgnoreCase) == true
                    ? "Mitt bryggeri"
                    : "My brewery";

                targetSetup = new BrewerySetup
                {
                    UserId = userId,
                    Name = defaultName,
                    IsDefault = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.BrewerySetups.Add(targetSetup);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Auto-provisioned default {DefaultName} setup for user {UserId} during equipment creation", defaultName, userId);
            }

            targetSetupId = targetSetup.Id;
        }
        else
        {
            targetSetup = await _db.BrewerySetups.FirstOrDefaultAsync(s => s.Id == targetSetupId && s.UserId == userId);
            if (targetSetup == null)
            {
                return (EquipmentAccessResult.NotFound, null, "Brewery setup not found.");
            }
        }

        var normalizedName = NormalizeEquipmentName(request.Name);
        var nameLower = normalizedName.ToLower();
        var isDuplicate = await _db.Equipment.AnyAsync(e =>
            e.UserId == userId &&
            e.BrewerySetupId == targetSetupId &&
            e.Name.ToLower() == nameLower);

        if (isDuplicate)
        {
            return (EquipmentAccessResult.DuplicateName, null, "An equipment item with this name already exists in this brewery setup.");
        }

        var userUnit = await GetUserPreferredUnitAsync(userId);
        var inputUnit = request.Unit ?? userUnit;
        var capacity = request.Type == EquipmentType.Sensor ? 0.0m : request.Capacity;
        var capacityLiters = request.Type == EquipmentType.Sensor ? 0.0m : ToLiters(capacity, inputUnit);
        var subtype = ResolveDefaultSubtype(request.Type, request.Subtype);
        var currentVolume = request.Type == EquipmentType.Sensor ? 0.0m : (request.CurrentVolume ?? 0.0m);
        var currentVolumeLiters = request.Type == EquipmentType.Sensor ? 0.0m : ToLiters(currentVolume, inputUnit);

        decimal? boilOffRate = request.Type == EquipmentType.Sensor ? null : request.BoilOffRatePerHour;
        decimal? trubLoss = request.Type == EquipmentType.Sensor ? null : request.TrubLossLiters;
        decimal? mashDeadSpace = request.Type == EquipmentType.Sensor ? null : request.MashTunDeadSpaceLiters;
        decimal? packagingLoss = request.Type == EquipmentType.Sensor ? null : request.PackagingLossLiters;

        if (request.Type == EquipmentType.Boiler)
        {
            boilOffRate ??= targetSetup.DefaultBoilOffRatePerHour;
            trubLoss ??= targetSetup.DefaultKettleTrubLossLiters;
            mashDeadSpace ??= targetSetup.DefaultMashTunDeadSpaceLiters;
        }
        else if (request.Type == EquipmentType.Fermenter)
        {
            trubLoss ??= targetSetup.DefaultFermenterLossLiters;
        }
        else if (request.Type == EquipmentType.Keg || request.Type == EquipmentType.Other)
        {
            packagingLoss ??= targetSetup.DefaultPackagingLossLiters;
        }

        var connectionType = request.ConnectionType ?? EquipmentConnectionType.None;
        string? connectionToken = null;
        if (connectionType == EquipmentConnectionType.HttpPush)
        {
            connectionToken = GenerateConnectionToken();
        }

        var equipment = new Equipment
        {
            UserId = userId,
            BrewerySetupId = targetSetupId,
            Name = normalizedName,
            Type = request.Type,
            Subtype = subtype,
            Capacity = Math.Round(capacity, 2),
            Unit = inputUnit,
            CapacityLiters = Math.Round(capacityLiters, 2),
            CurrentVolume = Math.Round(currentVolume, 2),
            CurrentVolumeLiters = Math.Round(currentVolumeLiters, 2),
            BoilOffRatePerHour = boilOffRate,
            TrubLossLiters = trubLoss,
            MashTunDeadSpaceLiters = mashDeadSpace,
            PackagingLossLiters = packagingLoss,
            CurrentTemperatureC = request.CurrentTemperatureC,
            TemperatureUpdatedAt = request.CurrentTemperatureC.HasValue ? DateTime.UtcNow : null,
            ConnectionType = connectionType,
            ConnectionToken = connectionToken,
            ConnectionConfigJson = ProtectConfigJson(request.ConnectionConfigJson),
            Description = request.Description?.Trim(),
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Equipment.Add(equipment);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Duplicate equipment name constraint violated for user {UserId} in setup {SetupId}", userId, targetSetupId);
            return (EquipmentAccessResult.DuplicateName, null, "An equipment item with this name already exists in this brewery setup.");
        }

        _logger.LogInformation("Equipment {EquipmentId} created for user {UserId} in setup {SetupId}", equipment.Id, userId, targetSetupId);
        return (EquipmentAccessResult.Success, ToDto(equipment), null);
    }

    public async Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> UpdateEquipmentAsync(
        Guid id, UpdateEquipmentRequest request, string userId)
    {
        var equipment = await _db.Equipment
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null, "Equipment not found.");
        }

        var targetSetupId = request.BrewerySetupId ?? equipment.BrewerySetupId;
        if (request.BrewerySetupId.HasValue && request.BrewerySetupId.Value != equipment.BrewerySetupId)
        {
            var setupExists = await _db.BrewerySetups.AnyAsync(s => s.Id == request.BrewerySetupId.Value && s.UserId == userId);
            if (!setupExists)
            {
                return (EquipmentAccessResult.NotFound, null, "Brewery setup not found.");
            }
            equipment.BrewerySetupId = request.BrewerySetupId.Value;
        }

        var normalizedName = NormalizeEquipmentName(request.Name);
        var nameLower = normalizedName.ToLower();
        var isDuplicate = await _db.Equipment.AnyAsync(e =>
            e.UserId == userId &&
            e.BrewerySetupId == targetSetupId &&
            e.Id != id &&
            e.Name.ToLower() == nameLower);

        if (isDuplicate)
        {
            return (EquipmentAccessResult.DuplicateName, null, "An equipment item with this name already exists in this brewery setup.");
        }

        var userUnit = await GetUserPreferredUnitAsync(userId);
        var inputUnit = request.Unit ?? userUnit;
        var capacity = request.Type == EquipmentType.Sensor ? 0.0m : request.Capacity;
        var capacityLiters = request.Type == EquipmentType.Sensor ? 0.0m : ToLiters(capacity, inputUnit);
        var subtype = ResolveDefaultSubtype(request.Type, request.Subtype);
        var currentVolume = request.Type == EquipmentType.Sensor ? 0.0m : (request.CurrentVolume ?? equipment.CurrentVolume);
        var currentVolumeLiters = request.Type == EquipmentType.Sensor ? 0.0m : ToLiters(currentVolume, inputUnit);

        equipment.Name = normalizedName;
        equipment.Type = request.Type;
        equipment.Subtype = subtype;
        equipment.Capacity = Math.Round(capacity, 2);
        equipment.Unit = inputUnit;
        equipment.CapacityLiters = Math.Round(capacityLiters, 2);
        equipment.CurrentVolume = Math.Round(currentVolume, 2);
        equipment.CurrentVolumeLiters = Math.Round(currentVolumeLiters, 2);
        equipment.BoilOffRatePerHour = request.Type == EquipmentType.Sensor ? null : request.BoilOffRatePerHour;
        equipment.TrubLossLiters = request.Type == EquipmentType.Sensor ? null : request.TrubLossLiters;
        equipment.MashTunDeadSpaceLiters = request.Type == EquipmentType.Sensor ? null : request.MashTunDeadSpaceLiters;
        equipment.PackagingLossLiters = request.Type == EquipmentType.Sensor ? null : request.PackagingLossLiters;

        if (request.ConnectionType.HasValue)
        {
            equipment.ConnectionType = request.ConnectionType.Value;
            if (equipment.ConnectionType == EquipmentConnectionType.HttpPush && string.IsNullOrEmpty(equipment.ConnectionToken))
            {
                equipment.ConnectionToken = GenerateConnectionToken();
            }
        }

        if (request.ConnectionConfigJson != null)
        {
            equipment.ConnectionConfigJson = ProtectConfigJson(request.ConnectionConfigJson, equipment.ConnectionConfigJson);
        }

        if (request.CurrentTemperatureC.HasValue)
        {
            equipment.CurrentTemperatureC = request.CurrentTemperatureC.Value;
            equipment.TemperatureUpdatedAt = DateTime.UtcNow;
        }

        equipment.Description = request.Description?.Trim();
        equipment.Notes = request.Notes?.Trim();
        equipment.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Duplicate equipment name constraint violated when updating equipment {EquipmentId}", equipment.Id);
            return (EquipmentAccessResult.DuplicateName, null, "An equipment item with this name already exists in this brewery setup.");
        }

        _logger.LogInformation("Equipment {EquipmentId} updated for user {UserId}", equipment.Id, userId);
        return (EquipmentAccessResult.Success, ToDto(equipment), null);
    }

    public async Task<EquipmentAccessResult> DeleteEquipmentAsync(Guid id, string userId)
    {
        var equipment = await _db.Equipment
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

        if (equipment == null)
        {
            return EquipmentAccessResult.NotFound;
        }

        // Decouple from any batches using this equipment
        var batchesWithEquipment = await _db.Batches
            .Where(b => b.BoilerId == id || b.FermenterId == id || b.PackagingVesselId == id)
            .ToListAsync();

        foreach (var b in batchesWithEquipment)
        {
            if (b.BoilerId == id) b.BoilerId = null;
            if (b.FermenterId == id) b.FermenterId = null;
            if (b.PackagingVesselId == id) b.PackagingVesselId = null;
            b.UpdatedAt = DateTime.UtcNow;
        }

        _db.Equipment.Remove(equipment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Equipment {EquipmentId} deleted for user {UserId}", id, userId);
        return EquipmentAccessResult.Success;
    }

    public async Task<(EquipmentAccessResult Result, List<EquipmentActiveBatchDto>? Batches)> GetActiveBatchesForEquipmentAsync(
        Guid equipmentId, string userId)
    {
        var equipmentExists = await _db.Equipment.AsNoTracking()
            .AnyAsync(e => e.Id == equipmentId && e.UserId == userId);

        if (!equipmentExists)
        {
            return (EquipmentAccessResult.NotFound, null);
        }

        var activeBatches = await _db.Batches.AsNoTracking()
            .Where(b => b.UserId == userId &&
                        (b.BoilerId == equipmentId || b.FermenterId == equipmentId || b.PackagingVesselId == equipmentId) &&
                        b.Status != BatchStatus.Completed &&
                        b.Status != BatchStatus.Archived)
            .OrderByDescending(b => b.BrewDate)
            .ThenByDescending(b => b.CreatedAt)
            .Select(b => new EquipmentActiveBatchDto(
                b.Id,
                b.BatchCode,
                b.Name,
                b.Status,
                b.CurrentStage
            ))
            .ToListAsync();

        return (EquipmentAccessResult.Success, activeBatches);
    }

    public static EquipmentSubtype ResolveDefaultSubtype(EquipmentType type, EquipmentSubtype? requested)
    {
        if (requested.HasValue)
        {
            return requested.Value;
        }

        return type switch
        {
            EquipmentType.Boiler => EquipmentSubtype.AllInOne,
            EquipmentType.Fermenter => EquipmentSubtype.Bucket,
            EquipmentType.Keg => EquipmentSubtype.Cornelius,
            EquipmentType.Sensor => EquipmentSubtype.GenericSensor,
            _ => EquipmentSubtype.Other
        };
    }

    private async Task<VolumeUnit> GetUserPreferredUnitAsync(string userId)
    {
        var unit = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => (VolumeUnit?)u.PreferredVolumeUnit)
            .FirstOrDefaultAsync();

        return unit ?? VolumeUnit.Liters;
    }

    private static decimal ToLiters(decimal capacity, VolumeUnit unit) => unit switch
    {
        VolumeUnit.Gallons => capacity * GallonsToLitersFactor,
        _ => capacity
    };

    private static EquipmentDto ToDto(Equipment e)
    {
        var fillPercentage = e.CapacityLiters > 0
            ? Math.Round(Math.Clamp((e.CurrentVolumeLiters / e.CapacityLiters) * 100m, 0m, 100m), 1)
            : 0m;

        return new EquipmentDto(
            e.Id,
            e.BrewerySetupId,
            e.Name,
            e.Type,
            e.Subtype,
            e.Capacity,
            e.Unit,
            e.CapacityLiters,
            e.CurrentVolume,
            e.CurrentVolumeLiters,
            e.BoilOffRatePerHour,
            e.TrubLossLiters,
            e.MashTunDeadSpaceLiters,
            e.PackagingLossLiters,
            fillPercentage,
            e.CurrentTemperatureC,
            e.TemperatureUpdatedAt,
            e.ConnectionType,
            e.ConnectionToken,
            SanitizeConfigJson(e.ConnectionConfigJson),
            e.Description,
            e.Notes,
            e.CreatedAt,
            e.UpdatedAt,
            e.CurrentSpecificGravity,
            e.CurrentPressureBar,
            e.CurrentBatteryPercent,
            e.CurrentBatteryVoltage,
            e.LastTelemetryAt,
            e.LatestMetricsJson
        );
    }

    private static string? SanitizeConfigJson(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return configJson;

        try
        {
            var node = JsonNode.Parse(configJson);
            if (node is JsonObject obj && obj.ContainsKey("password"))
            {
                if (obj["password"] != null)
                {
                    obj["hasPassword"] = true;
                }
                obj.Remove("password");
                return obj.ToJsonString();
            }
            return configJson;
        }
        catch
        {
            return configJson;
        }
    }

    private string? ProtectConfigJson(string? configJson, string? existingConfigJson = null)
    {
        if (string.IsNullOrWhiteSpace(configJson) || _protector == null) return configJson;

        try
        {
            var node = JsonNode.Parse(configJson);
            if (node is JsonObject obj && obj.ContainsKey("password"))
            {
                var rawPassword = obj["password"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(rawPassword))
                {
                    obj["password"] = _protector.Protect(rawPassword);
                }
                else if (existingConfigJson != null)
                {
                    try
                    {
                        var existingNode = JsonNode.Parse(existingConfigJson);
                        if (existingNode is JsonObject existingObj && existingObj.ContainsKey("password") && existingObj["password"] != null)
                        {
                            obj["password"] = existingObj["password"]!.DeepClone();
                        }
                    }
                    catch
                    {
                        // ignore parse error
                    }
                }
                return obj.ToJsonString();
            }
            return configJson;
        }
        catch
        {
            return configJson;
        }
    }

    public async Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? NewToken)> RegenerateConnectionTokenAsync(
        Guid id, string userId)
    {
        var equipment = await _db.Equipment
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null, null);
        }

        var newToken = GenerateConnectionToken();
        equipment.ConnectionToken = newToken;
        equipment.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Connection token regenerated for equipment {EquipmentId} by user {UserId}", id, userId);

        return (EquipmentAccessResult.Success, ToDto(equipment), newToken);
    }

    public static string GenerateConnectionToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    public static string NormalizeEquipmentName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return MultipleWhitespaceRegex.Replace(name.Trim(), " ");
    }
}