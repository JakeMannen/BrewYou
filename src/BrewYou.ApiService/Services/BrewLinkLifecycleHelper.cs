using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BrewYou.ApiService.Services;

internal sealed record BrewLinkModelSpec(
    EquipmentType EquipmentType,
    EquipmentSubtype Subtype,
    decimal CapacityLiters,
    decimal? MashTunDeadSpaceLiters,
    decimal? BoilOffRatePerHour,
    decimal? TrubLossLiters
);

internal static partial class BrewLinkLifecycleHelper
{
    [GeneratedRegex(@"[^0-9A-Fa-f]")]
    private static partial Regex NonHexRegex();

    internal static string NormalizeMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return string.Empty;
        }

        return NonHexRegex().Replace(mac, string.Empty).ToUpperInvariant();
    }

    internal static BrewLinkModelSpec MapModelSpec(string? model, string? equipmentType)
    {
        var typeLower = equipmentType?.Trim().ToLowerInvariant() ?? string.Empty;
        var modelTrimmed = model?.Trim() ?? string.Empty;

        if (typeLower is "fermenter" or "cooler")
        {
            return new BrewLinkModelSpec(
                EquipmentType.Fermenter,
                EquipmentSubtype.ConicalFermenter,
                CapacityLiters: 30.0m,
                MashTunDeadSpaceLiters: null,
                BoilOffRatePerHour: null,
                TrubLossLiters: 1.5m);
        }

        if (modelTrimmed.Contains("Grainfather G30", StringComparison.OrdinalIgnoreCase))
        {
            return new BrewLinkModelSpec(
                EquipmentType.Boiler,
                EquipmentSubtype.AllInOne,
                CapacityLiters: 30.0m,
                MashTunDeadSpaceLiters: 3.5m,
                BoilOffRatePerHour: 3.0m,
                TrubLossLiters: 2.0m);
        }

        return new BrewLinkModelSpec(
            EquipmentType.Boiler,
            EquipmentSubtype.AllInOne,
            CapacityLiters: 30.0m,
            MashTunDeadSpaceLiters: 2.5m,
            BoilOffRatePerHour: 3.0m,
            TrubLossLiters: 2.0m);
    }

    internal static bool TryParseBrewLinkConfig(
        string? configJson,
        out Guid? gatewayId,
        out string? targetMacNormalized,
        out int? slot)
    {
        gatewayId = null;
        targetMacNormalized = null;
        slot = null;

        if (string.IsNullOrWhiteSpace(configJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = doc.RootElement;
            if (root.TryGetProperty("brewLinkGatewayId", out var gwProp) &&
                gwProp.ValueKind == JsonValueKind.String &&
                Guid.TryParse(gwProp.GetString(), out var parsedGw))
            {
                gatewayId = parsedGw;
            }

            if ((root.TryGetProperty("targetMac", out var macProp) ||
                 root.TryGetProperty("target_mac", out macProp)) &&
                macProp.ValueKind == JsonValueKind.String)
            {
                var norm = NormalizeMac(macProp.GetString());
                if (!string.IsNullOrEmpty(norm))
                {
                    targetMacNormalized = norm;
                }
            }

            if (root.TryGetProperty("slot", out var slotProp) && slotProp.TryGetInt32(out var parsedSlot))
            {
                slot = parsedSlot;
            }

            return gatewayId.HasValue || !string.IsNullOrEmpty(targetMacNormalized) || slot.HasValue;
        }
        catch
        {
            return false;
        }
    }

    internal static Equipment? FindMatchingLinkedEquipment(
        IEnumerable<Equipment> gatewayAttachedEquipment,
        int slot,
        string? mac)
    {
        var normalizedMac = NormalizeMac(mac);

        foreach (var eq in gatewayAttachedEquipment)
        {
            if (!TryParseBrewLinkConfig(eq.ConnectionConfigJson, out _, out var eqMac, out var eqSlot))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(normalizedMac) &&
                !string.IsNullOrEmpty(eqMac) &&
                string.Equals(normalizedMac, eqMac, StringComparison.OrdinalIgnoreCase))
            {
                return eq;
            }

            if (slot > 0 && eqSlot.HasValue && eqSlot.Value == slot &&
                (string.IsNullOrEmpty(normalizedMac) || string.IsNullOrEmpty(eqMac)))
            {
                return eq;
            }
        }

        return null;
    }

    internal static async Task<List<Equipment>> FindBrewLinkGatewayGroupAsync(
        BrewYouDbContext db,
        Guid gatewayId,
        Equipment? primaryEquipment = null,
        CancellationToken ct = default)
    {
        var candidates = await db.Equipment
            .Where(e => e.ConnectionConfigJson != null &&
                        (e.ConnectionType == EquipmentConnectionType.BrewLink || e.Subtype == EquipmentSubtype.BrewLink))
            .ToListAsync(ct);

        var group = new List<Equipment>();
        var seen = new HashSet<Guid>();

        if (primaryEquipment != null && seen.Add(primaryEquipment.Id))
        {
            group.Add(primaryEquipment);
        }

        foreach (var eq in candidates)
        {
            if (seen.Contains(eq.Id))
            {
                continue;
            }

            if (TryParseBrewLinkConfig(eq.ConnectionConfigJson, out var eqGwId, out _, out _) &&
                eqGwId == gatewayId)
            {
                seen.Add(eq.Id);
                group.Add(eq);
            }
        }

        return group;
    }

    internal static async Task<bool> HasActiveBatchesForEquipmentGroupAsync(
        BrewYouDbContext db,
        IReadOnlyCollection<Equipment> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
        {
            return false;
        }

        var allIds = items.Select(e => e.Id).ToHashSet();
        var assignedBatchIds = items
            .Where(e => e.AssignedBatchId.HasValue)
            .Select(e => e.AssignedBatchId!.Value)
            .ToHashSet();

        return await db.Batches.AsNoTracking().AnyAsync(b =>
            (b.BoilerId.HasValue && allIds.Contains(b.BoilerId.Value) ||
             b.FermenterId.HasValue && allIds.Contains(b.FermenterId.Value) ||
             b.PackagingVesselId.HasValue && allIds.Contains(b.PackagingVesselId.Value) ||
             b.HltId.HasValue && allIds.Contains(b.HltId.Value) ||
             b.MashTunId.HasValue && allIds.Contains(b.MashTunId.Value) ||
             assignedBatchIds.Contains(b.Id) ||
             b.SensorAssignments.Any(sa => allIds.Contains(sa.EquipmentId))) &&
            b.Status != BatchStatus.Completed &&
            b.Status != BatchStatus.Archived, ct);
    }

    internal static async Task CleanEquipmentGroupRelationsAndRemoveAsync(
        BrewYouDbContext db,
        IReadOnlyCollection<Equipment> items,
        BrewLinkGateway? gateway,
        CancellationToken ct = default)
    {
        if (items.Count > 0)
        {
            var allIds = items.Select(e => e.Id).ToHashSet();
            var now = DateTime.UtcNow;

            // 1. Decouple all 5 batch vessel references on historical/active batches
            var batchesWithEquipment = await db.Batches
                .Where(b => (b.BoilerId.HasValue && allIds.Contains(b.BoilerId.Value)) ||
                            (b.FermenterId.HasValue && allIds.Contains(b.FermenterId.Value)) ||
                            (b.PackagingVesselId.HasValue && allIds.Contains(b.PackagingVesselId.Value)) ||
                            (b.HltId.HasValue && allIds.Contains(b.HltId.Value)) ||
                            (b.MashTunId.HasValue && allIds.Contains(b.MashTunId.Value)))
                .ToListAsync(ct);

            foreach (var b in batchesWithEquipment)
            {
                if (b.BoilerId.HasValue && allIds.Contains(b.BoilerId.Value)) b.BoilerId = null;
                if (b.FermenterId.HasValue && allIds.Contains(b.FermenterId.Value)) b.FermenterId = null;
                if (b.PackagingVesselId.HasValue && allIds.Contains(b.PackagingVesselId.Value)) b.PackagingVesselId = null;
                if (b.HltId.HasValue && allIds.Contains(b.HltId.Value)) b.HltId = null;
                if (b.MashTunId.HasValue && allIds.Contains(b.MashTunId.Value)) b.MashTunId = null;
                b.UpdatedAt = now;
            }

            // 2. Remove BatchSensorAssignments referencing any equipment in the group
            var sensorAssignments = await db.BatchSensorAssignments
                .Where(sa => allIds.Contains(sa.EquipmentId))
                .ToListAsync(ct);
            if (sensorAssignments.Count > 0)
            {
                db.BatchSensorAssignments.RemoveRange(sensorAssignments);
            }

            // 3. Decouple any taps referencing equipment in this group as AssignedKeg
            var referencingTaps = await db.Equipment
                .Where(e => e.AssignedKegId.HasValue && allIds.Contains(e.AssignedKegId.Value))
                .ToListAsync(ct);
            foreach (var tap in referencingTaps)
            {
                tap.AssignedKegId = null;
                tap.AssignedKeg = null;
                tap.UpdatedAt = now;
            }

            // 4. Clear tap/keg assignments on removed items
            foreach (var item in items)
            {
                if (item.AssignedBatchId != null)
                {
                    item.AssignedBatchId = null;
                    item.AssignedBatch = null;
                    item.AssignedAt = null;
                    item.CurrentVolume = 0m;
                    item.CurrentVolumeLiters = 0m;
                }

                if (item.AssignedKegId != null)
                {
                    item.AssignedKegId = null;
                    item.AssignedKeg = null;
                }
            }

            // 5. Remove telemetry readings explicitly (important for InMemory and relation parity)
            var readings = await db.EquipmentReadings
                .Where(r => allIds.Contains(r.EquipmentId))
                .ToListAsync(ct);
            if (readings.Count > 0)
            {
                db.EquipmentReadings.RemoveRange(readings);
            }

            db.Equipment.RemoveRange(items);
        }

        if (gateway != null)
        {
            db.BrewLinkGateways.Remove(gateway);
        }
    }
}