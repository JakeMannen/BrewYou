using BrewYou.ApiService.Backup.Security;
using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services.Labels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IDataProtector? _displayProtector;
    private readonly ILabelImageProcessor? _imageProcessor;
    private readonly IMemoryCache? _cache;

    public EquipmentService(
        BrewYouDbContext db,
        ILogger<EquipmentService> logger,
        IDataProtectionProvider? dataProtectionProvider = null,
        ILabelImageProcessor? imageProcessor = null,
        IMemoryCache? cache = null)
    {
        _db = db;
        _logger = logger;
        _protector = dataProtectionProvider?.CreateProtector("BrewYou.MqttCredentials");
        _displayProtector = dataProtectionProvider?.CreateProtector("BrewYou.TapDisplay");
        _imageProcessor = imageProcessor;
        _cache = cache;
    }

    public async Task<(List<EquipmentDto> Items, PaginationMeta Pagination)> GetEquipmentListAsync(
        string userId, Guid? setupId, EquipmentType? type, string? search, int page, int limit)
    {
        var userUnit = await GetUserPreferredUnitAsync(userId);

        var query = _db.Equipment.AsNoTracking()
            .Include(e => e.AssignedBatch)
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .Where(e => e.UserId == userId || (e.BrewerySetup != null && (e.BrewerySetup.UserId == userId || e.BrewerySetup.Members.Any(m => m.UserId == userId))));

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

        var items = entities.Select(e =>
        {
            var isOwner = e.UserId == userId || (e.BrewerySetup != null && (e.BrewerySetup.UserId == userId || e.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
            return ToDto(e, isOwner);
        }).ToList();

        var pagination = new PaginationMeta(page, limit, total, totalPages);
        return (items, pagination);
    }

    public async Task<(EquipmentAccessResult Result, EquipmentDto? Equipment)> GetEquipmentByIdAsync(Guid id, string userId)
    {
        var entity = await _db.Equipment.AsNoTracking()
            .Include(e => e.AssignedBatch)
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == id && (e.UserId == userId || (e.BrewerySetup != null && (e.BrewerySetup.UserId == userId || e.BrewerySetup.Members.Any(m => m.UserId == userId)))));

        if (entity == null)
        {
            return (EquipmentAccessResult.NotFound, null);
        }

        var isOwner = entity.UserId == userId || (entity.BrewerySetup != null && (entity.BrewerySetup.UserId == userId || entity.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        return (EquipmentAccessResult.Success, ToDto(entity, isOwner));
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
                var defaultName = BrewerySetupService.GetDefaultBreweryName(user?.PreferredLanguage);

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
            targetSetup = await _db.BrewerySetups
                .Include(s => s.Members)
                .FirstOrDefaultAsync(s => s.Id == targetSetupId && (s.UserId == userId || s.Members.Any(m => m.UserId == userId)));

            if (targetSetup == null)
            {
                return (EquipmentAccessResult.NotFound, null, "Brewery setup not found.");
            }

            var isOwner = targetSetup.UserId == userId || targetSetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner);
            var isBrewer = targetSetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);
            if (!isOwner && !isBrewer)
            {
                return (EquipmentAccessResult.Forbidden, null, "Viewers cannot create equipment.");
            }
        }

        var normalizedName = NormalizeEquipmentName(request.Name);
        var nameLower = normalizedName.ToLower();
        var isDuplicate = await _db.Equipment.AnyAsync(e =>
            e.BrewerySetupId == targetSetupId &&
            e.Name.ToLower() == nameLower);

        if (isDuplicate)
        {
            return (EquipmentAccessResult.DuplicateName, null, "An equipment item with this name already exists in this brewery setup.");
        }

        var userUnit = await GetUserPreferredUnitAsync(userId);
        var inputUnit = request.Unit ?? userUnit;
        var isSensorOrTap = request.Type is EquipmentType.Sensor or EquipmentType.Tap;
        var capacity = isSensorOrTap ? 0.0m : request.Capacity;
        var capacityLiters = isSensorOrTap ? 0.0m : ToLiters(capacity, inputUnit);
        var subtype = ResolveDefaultSubtype(request.Type, request.Subtype);
        var currentVolume = isSensorOrTap ? 0.0m : (request.CurrentVolume ?? 0.0m);
        var currentVolumeLiters = isSensorOrTap ? 0.0m : ToLiters(currentVolume, inputUnit);

        decimal? boilOffRate = isSensorOrTap ? null : request.BoilOffRatePerHour;
        decimal? trubLoss = isSensorOrTap ? null : request.TrubLossLiters;
        decimal? mashDeadSpace = isSensorOrTap ? null : request.MashTunDeadSpaceLiters;
        decimal? packagingLoss = isSensorOrTap ? null : request.PackagingLossLiters;

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
            DisplayWidthPx = request.DisplayWidthPx,
            DisplayHeightPx = request.DisplayHeightPx,
            DisplayOrientation = request.DisplayOrientation ?? DisplayOrientation.Landscape,
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
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null, "Equipment not found.");
        }

        var isMember = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId)));
        if (!isMember)
        {
            return (EquipmentAccessResult.NotFound, null, "Equipment not found.");
        }

        var isOwner = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        var isBrewer = equipment.BrewerySetup != null && equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);
        if (!isOwner && !isBrewer)
        {
            return (EquipmentAccessResult.Forbidden, null, "Viewers cannot edit equipment in this brewery setup.");
        }

        var targetSetupId = request.BrewerySetupId ?? equipment.BrewerySetupId;
        if (request.BrewerySetupId.HasValue && request.BrewerySetupId.Value != equipment.BrewerySetupId)
        {
            var setupExists = await _db.BrewerySetups.AnyAsync(s => s.Id == request.BrewerySetupId.Value && (s.UserId == userId || s.Members.Any(m => m.UserId == userId && m.Role != BreweryRole.Viewer)));
            if (!setupExists)
            {
                return (EquipmentAccessResult.NotFound, null, "Brewery setup not found.");
            }
            equipment.BrewerySetupId = request.BrewerySetupId.Value;
        }

        var normalizedName = NormalizeEquipmentName(request.Name);
        var nameLower = normalizedName.ToLower();
        var isDuplicate = await _db.Equipment.AnyAsync(e =>
            e.BrewerySetupId == targetSetupId &&
            e.Id != id &&
            e.Name.ToLower() == nameLower);

        if (isDuplicate)
        {
            return (EquipmentAccessResult.DuplicateName, null, "An equipment item with this name already exists in this brewery setup.");
        }

        var userUnit = await GetUserPreferredUnitAsync(userId);
        var inputUnit = request.Unit ?? userUnit;
        var isSensorOrTap = request.Type is EquipmentType.Sensor or EquipmentType.Tap;
        var capacity = isSensorOrTap ? 0.0m : request.Capacity;
        var capacityLiters = isSensorOrTap ? 0.0m : ToLiters(capacity, inputUnit);
        var subtype = ResolveDefaultSubtype(request.Type, request.Subtype);
        var currentVolume = isSensorOrTap ? 0.0m : (request.CurrentVolume ?? equipment.CurrentVolume);
        var currentVolumeLiters = isSensorOrTap ? 0.0m : ToLiters(currentVolume, inputUnit);

        equipment.Name = normalizedName;
        equipment.Type = request.Type;
        equipment.Subtype = subtype;
        equipment.Capacity = Math.Round(capacity, 2);
        equipment.Unit = inputUnit;
        equipment.CapacityLiters = Math.Round(capacityLiters, 2);
        equipment.CurrentVolume = Math.Round(currentVolume, 2);
        equipment.CurrentVolumeLiters = Math.Round(currentVolumeLiters, 2);
        equipment.BoilOffRatePerHour = isSensorOrTap ? null : request.BoilOffRatePerHour;
        equipment.TrubLossLiters = isSensorOrTap ? null : request.TrubLossLiters;
        equipment.MashTunDeadSpaceLiters = isSensorOrTap ? null : request.MashTunDeadSpaceLiters;
        equipment.PackagingLossLiters = isSensorOrTap ? null : request.PackagingLossLiters;

        equipment.DisplayWidthPx = request.DisplayWidthPx;
        equipment.DisplayHeightPx = request.DisplayHeightPx;
        if (request.DisplayOrientation.HasValue)
        {
            equipment.DisplayOrientation = request.DisplayOrientation.Value;
        }

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

        _logger.LogInformation("Equipment {EquipmentId} updated by user {UserId}", equipment.Id, userId);
        return (EquipmentAccessResult.Success, ToDto(equipment, isOwner), null);
    }

    public async Task<EquipmentAccessResult> DeleteEquipmentAsync(Guid id, string userId)
    {
        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (equipment == null)
        {
            return EquipmentAccessResult.NotFound;
        }

        var isMember = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId)));
        if (!isMember)
        {
            return EquipmentAccessResult.NotFound;
        }

        var isOwnerOrCreator = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        if (!isOwnerOrCreator)
        {
            return EquipmentAccessResult.Forbidden;
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

        _logger.LogInformation("Equipment {EquipmentId} deleted by user {UserId}", id, userId);
        return EquipmentAccessResult.Success;
    }

    public async Task<(EquipmentAccessResult Result, List<EquipmentActiveBatchDto>? Batches)> GetActiveBatchesForEquipmentAsync(
        Guid equipmentId, string userId)
    {
        var equipment = await _db.Equipment.AsNoTracking()
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == equipmentId && (e.UserId == userId || (e.BrewerySetup != null && (e.BrewerySetup.UserId == userId || e.BrewerySetup.Members.Any(m => m.UserId == userId)))));

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null);
        }

        var activeBatches = await _db.Batches.AsNoTracking()
            .Where(b => (b.UserId == userId || (b.BrewerySetupId != null && b.BrewerySetupId == equipment.BrewerySetupId)) &&
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
            EquipmentType.Tap => EquipmentSubtype.StandardTap,
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

    private static EquipmentDto ToDto(Equipment e, bool isOwner = true)
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
            isOwner ? e.ConnectionToken : null,
            isOwner ? SanitizeConfigJson(e.ConnectionConfigJson) : null,
            e.Description,
            e.Notes,
            e.CreatedAt,
            e.UpdatedAt,
            e.CurrentSpecificGravity,
            e.CurrentPressureBar,
            e.CurrentBatteryPercent,
            e.CurrentBatteryVoltage,
            e.LastTelemetryAt,
            e.LatestMetricsJson,
            e.DisplayWidthPx,
            e.DisplayHeightPx,
            e.DisplayOrientation,
            e.DisplayLinkEnabled,
            e.AssignedBatch != null
                ? new TapAssignedBatchDto(
                    e.AssignedBatch.Id,
                    e.AssignedBatch.BatchCode,
                    e.AssignedBatch.Name,
                    e.AssignedBatch.BeerStyle,
                    e.AssignedBatch.AlcoholByVolume,
                    e.AssignedAt)
                : null
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
            .Include(e => e.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null, null);
        }

        var isMember = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId)));
        if (!isMember)
        {
            return (EquipmentAccessResult.NotFound, null, null);
        }

        var isOwner = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        if (!isOwner)
        {
            return (EquipmentAccessResult.Forbidden, null, null);
        }

        var newToken = GenerateConnectionToken();
        equipment.ConnectionToken = newToken;
        equipment.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Connection token regenerated for equipment {EquipmentId} by user {UserId}", id, userId);

        return (EquipmentAccessResult.Success, ToDto(equipment, isOwner: true), newToken);
    }

    public async Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> AssignTapBatchAsync(
        Guid tapId, Guid batchId, string userId)
    {
        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup).ThenInclude(s => s!.Members)
            .Include(e => e.AssignedBatch)
            .FirstOrDefaultAsync(e => e.Id == tapId);

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null, "Equipment not found.");
        }

        if (equipment.Type != EquipmentType.Tap)
        {
            return (EquipmentAccessResult.InvalidTapAssignment, null, "Only tap equipment can be assigned a batch.");
        }

        var isOwner = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        var isBrewer = equipment.BrewerySetup != null && equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);

        if (!isOwner && !isBrewer)
        {
            return (EquipmentAccessResult.Forbidden, null, "You do not have permission to assign batches to this tap.");
        }

        var batch = await _db.Batches
            .Include(b => b.Recipe).ThenInclude(r => r!.Label)
            .FirstOrDefaultAsync(b => b.Id == batchId);

        if (batch == null)
        {
            return (EquipmentAccessResult.NotFound, null, "Batch not found.");
        }

        // Must belong to the same setup or user
        if (batch.BrewerySetupId != equipment.BrewerySetupId && batch.UserId != equipment.UserId)
        {
            return (EquipmentAccessResult.Forbidden, null, "Batch does not belong to the same brewery setup as the tap.");
        }

        if (batch.Status == BatchStatus.Archived)
        {
            return (EquipmentAccessResult.InvalidTapAssignment, null, "Archived batches cannot be assigned to a tap.");
        }

        // Servable gate: must be in Condition or Package stage, or status Conditioning / Completed
        var isServable = batch.CurrentStage is BrewStage.Condition or BrewStage.Package ||
                         batch.Status is BatchStatus.Conditioning or BatchStatus.Completed;

        if (!isServable)
        {
            return (EquipmentAccessResult.InvalidTapAssignment, null, "Batch is not servable yet. Taps can only be assigned batches in conditioning or packaging stages.");
        }

        equipment.AssignedBatchId = batch.Id;
        equipment.AssignedBatch = batch;
        equipment.AssignedAt = DateTime.UtcNow;
        equipment.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Assigned batch {BatchId} ({BatchCode}) to tap {TapId} by user {UserId}", batch.Id, batch.BatchCode, tapId, userId);

        return (EquipmentAccessResult.Success, ToDto(equipment, isOwner), null);
    }

    public async Task<(EquipmentAccessResult Result, EquipmentDto? Equipment, string? ErrorMessage)> UnassignTapBatchAsync(
        Guid tapId, string userId)
    {
        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup).ThenInclude(s => s!.Members)
            .Include(e => e.AssignedBatch)
            .FirstOrDefaultAsync(e => e.Id == tapId);

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null, "Equipment not found.");
        }

        if (equipment.Type != EquipmentType.Tap)
        {
            return (EquipmentAccessResult.InvalidTapAssignment, null, "Equipment is not a tap.");
        }

        var isOwner = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        var isBrewer = equipment.BrewerySetup != null && equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);

        if (!isOwner && !isBrewer)
        {
            return (EquipmentAccessResult.Forbidden, null, "You do not have permission to manage this tap.");
        }

        equipment.AssignedBatchId = null;
        equipment.AssignedBatch = null;
        equipment.AssignedAt = null;
        equipment.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Unassigned batch from tap {TapId} by user {UserId}", tapId, userId);

        return (EquipmentAccessResult.Success, ToDto(equipment, isOwner), null);
    }

    public async Task<(EquipmentAccessResult Result, TapDisplayLinkDto? Link, string? ErrorMessage)> GetTapDisplayLinkAsync(
        Guid tapId, string userId, string appBaseUrl, string apiBaseUrl)
    {
        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup).ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == tapId);

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null, "Equipment not found.");
        }

        if (equipment.Type != EquipmentType.Tap)
        {
            return (EquipmentAccessResult.InvalidTapAssignment, null, "Equipment is not a tap.");
        }

        var isOwner = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        var isBrewer = equipment.BrewerySetup != null && equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);
        if (!isOwner && !isBrewer)
        {
            return (EquipmentAccessResult.Forbidden, null, "Access denied.");
        }

        if (!equipment.DisplayLinkEnabled || string.IsNullOrEmpty(equipment.DisplayTokenProtected))
        {
            return (EquipmentAccessResult.NotFound, null, "Tap display link is not enabled.");
        }

        var plainToken = BackupSecretProtector.UnprotectServerSecret(equipment.DisplayTokenProtected, _displayProtector);
        if (string.IsNullOrEmpty(plainToken))
        {
            return (EquipmentAccessResult.NotFound, null, "Failed to decrypt tap display token.");
        }

        var cleanApi = apiBaseUrl.TrimEnd('/');
        var cleanApp = appBaseUrl.TrimEnd('/');
        var link = new TapDisplayLinkDto(
            ManifestUrl: $"{cleanApi}/api/v1/tap/{plainToken}",
            ImageUrl: $"{cleanApi}/api/v1/tap/{plainToken}/image",
            KioskUrl: $"{cleanApp}/tap/{plainToken}"
        );

        return (EquipmentAccessResult.Success, link, null);
    }

    public async Task<(EquipmentAccessResult Result, TapDisplayLinkDto? Link, string? ErrorMessage)> EnableOrRegenerateTapDisplayLinkAsync(
        Guid tapId, string userId, string appBaseUrl, string apiBaseUrl)
    {
        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup).ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == tapId);

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, null, "Equipment not found.");
        }

        if (equipment.Type != EquipmentType.Tap)
        {
            return (EquipmentAccessResult.InvalidTapAssignment, null, "Equipment is not a tap.");
        }

        var isOwner = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        var isBrewer = equipment.BrewerySetup != null && equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);
        if (!isOwner && !isBrewer)
        {
            return (EquipmentAccessResult.Forbidden, null, "Access denied.");
        }

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var plainToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken))).ToLowerInvariant();
        var protectedToken = BackupSecretProtector.ProtectServerSecret(plainToken, _displayProtector);

        equipment.DisplayLinkEnabled = true;
        equipment.DisplayTokenHash = tokenHash;
        equipment.DisplayTokenProtected = protectedToken;
        equipment.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var cleanApi = apiBaseUrl.TrimEnd('/');
        var cleanApp = appBaseUrl.TrimEnd('/');
        var link = new TapDisplayLinkDto(
            ManifestUrl: $"{cleanApi}/api/v1/tap/{plainToken}",
            ImageUrl: $"{cleanApi}/api/v1/tap/{plainToken}/image",
            KioskUrl: $"{cleanApp}/tap/{plainToken}"
        );

        return (EquipmentAccessResult.Success, link, null);
    }

    public async Task<(EquipmentAccessResult Result, string? ErrorMessage)> RevokeTapDisplayLinkAsync(
        Guid tapId, string userId)
    {
        var equipment = await _db.Equipment
            .Include(e => e.BrewerySetup).ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(e => e.Id == tapId);

        if (equipment == null)
        {
            return (EquipmentAccessResult.NotFound, "Equipment not found.");
        }

        if (equipment.Type != EquipmentType.Tap)
        {
            return (EquipmentAccessResult.InvalidTapAssignment, "Equipment is not a tap.");
        }

        var isOwner = equipment.UserId == userId || (equipment.BrewerySetup != null && (equipment.BrewerySetup.UserId == userId || equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner)));
        var isBrewer = equipment.BrewerySetup != null && equipment.BrewerySetup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Brewer);
        if (!isOwner && !isBrewer)
        {
            return (EquipmentAccessResult.Forbidden, "Access denied.");
        }

        equipment.DisplayLinkEnabled = false;
        equipment.DisplayTokenHash = null;
        equipment.DisplayTokenProtected = null;
        equipment.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return (EquipmentAccessResult.Success, null);
    }

    public async Task<(TapDisplayManifestDto? Manifest, string ETag)?> GetTapManifestByTokenAsync(
        string token, string apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim().ToLowerInvariant()))).ToLowerInvariant();

        var equipment = await _db.Equipment
            .AsNoTracking()
            .Include(e => e.AssignedBatch)
                .ThenInclude(b => b!.Recipe)
                    .ThenInclude(r => r!.Label)
            .FirstOrDefaultAsync(e => e.DisplayTokenHash == hash && e.DisplayLinkEnabled && e.Type == EquipmentType.Tap);

        if (equipment == null) return null;

        var status = equipment.AssignedBatch != null ? "Serving" : "Empty";
        var shape = equipment.Subtype == EquipmentSubtype.RoundDisplayTap ? "Round" : "Rectangular";
        var w = equipment.DisplayWidthPx ?? (equipment.Subtype == EquipmentSubtype.RoundDisplayTap ? 480 : 800);
        var h = equipment.DisplayHeightPx ?? (equipment.Subtype == EquipmentSubtype.RoundDisplayTap ? 480 : 480);
        var beerName = equipment.AssignedBatch != null
            ? (equipment.AssignedBatch.Recipe?.Name ?? equipment.AssignedBatch.Name)
            : null;

        // Master Brewer rule: actual measured ABV only
        var actualAbv = equipment.AssignedBatch?.AlcoholByVolume;

        var labelVersion = equipment.AssignedBatch != null
            ? $"{equipment.AssignedBatch.Id}:{equipment.AssignedBatch.Recipe?.Label?.Version ?? 0}:{actualAbv}:{equipment.UpdatedAt.Ticks}"
            : $"empty:{equipment.UpdatedAt.Ticks}";

        var etag = $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(labelVersion))).ToLowerInvariant()}\"";
        var imageUrl = $"{apiBaseUrl.TrimEnd('/')}/api/v1/tap/{token.Trim().ToLowerInvariant()}/image";

        var manifest = new TapDisplayManifestDto(
            Status: status,
            Shape: shape,
            WidthPx: w,
            HeightPx: h,
            BeerName: beerName,
            ActualAbv: actualAbv,
            LabelVersion: labelVersion,
            ImageUrl: imageUrl
        );

        return (manifest, etag);
    }

    public async Task<(byte[]? Bytes, string ContentType, string ETag)?> RenderTapImageByTokenAsync(
        string token, string? format, bool abv = true)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim().ToLowerInvariant()))).ToLowerInvariant();

        var equipment = await _db.Equipment
            .AsNoTracking()
            .Include(e => e.AssignedBatch)
                .ThenInclude(b => b!.Recipe)
                    .ThenInclude(r => r!.Label)
            .FirstOrDefaultAsync(e => e.DisplayTokenHash == hash && e.DisplayLinkEnabled && e.Type == EquipmentType.Tap);

        if (equipment == null) return null;

        var isRound = equipment.Subtype == EquipmentSubtype.RoundDisplayTap;
        var targetW = equipment.DisplayWidthPx ?? (isRound ? 480 : 800);
        var targetH = equipment.DisplayHeightPx ?? (isRound ? 480 : 480);
        var targetFormat = format?.Trim().ToLowerInvariant() is "png" or "jpeg" or "jpg" ? format.Trim().ToLowerInvariant() : "webp";

        byte[] masterBytes;
        LabelCrop crop;
        string assetSha;

        if (equipment.AssignedBatch?.Recipe?.Label != null)
        {
            var lbl = equipment.AssignedBatch.Recipe.Label;
            masterBytes = lbl.ImageData;
            crop = isRound ? lbl.RoundCrop : lbl.RectCrop;
            assetSha = lbl.Sha256;
        }
        else
        {
            var (genBytes, genType, genW, genH, genSha) = DefaultLabelGenerator.GenerateDefaultLabel();
            var defaultAsset = await _db.SystemAssets.FirstOrDefaultAsync(a => a.Key == DefaultLabelGenerator.DefaultAssetKey);
            if (defaultAsset == null)
            {
                defaultAsset = new SystemAsset
                {
                    Key = DefaultLabelGenerator.DefaultAssetKey,
                    ImageData = genBytes,
                    ContentType = genType,
                    WidthPx = genW,
                    HeightPx = genH,
                    Sha256 = genSha,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.SystemAssets.Add(defaultAsset);
                await _db.SaveChangesAsync();
            }
            else if (defaultAsset.Sha256 != genSha)
            {
                defaultAsset.ImageData = genBytes;
                defaultAsset.ContentType = genType;
                defaultAsset.WidthPx = genW;
                defaultAsset.HeightPx = genH;
                defaultAsset.Sha256 = genSha;
                defaultAsset.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            masterBytes = defaultAsset.ImageData;
            crop = isRound ? LabelCrop.Default : new LabelCrop(0m, 0.21875m, 1m, 0.5625m);
            assetSha = defaultAsset.Sha256;
        }

        // Master Brewer rule: only actual measured ABV from batch
        string? abvText = null;
        if (abv && equipment.AssignedBatch?.AlcoholByVolume.HasValue == true)
        {
            abvText = FormattableString.Invariant($"{equipment.AssignedBatch.AlcoholByVolume.Value:0.1}% ABV");
        }

        var cacheKey = $"tap:{equipment.Id}:{assetSha}:{targetW}x{targetH}:{targetFormat}:{isRound}:{abvText}";
        var etag = $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLowerInvariant()}\"";

        if (_cache != null && _cache.TryGetValue(cacheKey, out byte[]? cachedBytes) && cachedBytes != null)
        {
            return (cachedBytes, GetMimeType(targetFormat), etag);
        }

        if (_imageProcessor == null)
        {
            return null;
        }

        var rendered = _imageProcessor.RenderRendition(masterBytes, crop, targetW, targetH, targetFormat, isRound: isRound, abvOverlay: abvText);
        _cache?.Set(cacheKey, rendered, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        });

        return (rendered, GetMimeType(targetFormat), etag);
    }

    private static string GetMimeType(string format) => format switch
    {
        "png" => "image/png",
        "jpeg" or "jpg" => "image/jpeg",
        "webp" or _ => "image/webp"
    };

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