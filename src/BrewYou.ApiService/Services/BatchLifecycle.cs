using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace BrewYou.ApiService.Services;

/// <summary>
/// Single source of truth for batch liveness and stage-aware live equipment occupancy.
/// </summary>
public static class BatchLifecycle
{
    public static bool IsLiveStatus(BatchStatus status) =>
        status is BatchStatus.Brewing or BatchStatus.Fermenting or BatchStatus.Conditioning;

    public static bool OccupiesLiveEquipment(Batch batch) =>
        !batch.IsHistorical && IsLiveStatus(batch.Status);

    public static IEnumerable<Guid> ActiveStageVesselIds(Batch batch)
    {
        if (!OccupiesLiveEquipment(batch))
        {
            yield break;
        }

        switch (batch.CurrentStage)
        {
            case BrewStage.Mash:
                var seen = new HashSet<Guid>();
                if (batch.HltId.HasValue && seen.Add(batch.HltId.Value)) yield return batch.HltId.Value;
                if (batch.MashTunId.HasValue && seen.Add(batch.MashTunId.Value)) yield return batch.MashTunId.Value;
                if (batch.BoilerId.HasValue && seen.Add(batch.BoilerId.Value)) yield return batch.BoilerId.Value;
                break;

            case BrewStage.Boil:
                if (batch.BoilerId.HasValue) yield return batch.BoilerId.Value;
                break;

            case BrewStage.Ferment:
            case BrewStage.Condition:
                if (batch.FermenterId.HasValue) yield return batch.FermenterId.Value;
                break;

            case BrewStage.Package:
                if (batch.PackagingVesselId.HasValue) yield return batch.PackagingVesselId.Value;
                break;
        }
    }

    public static Expression<Func<Batch, bool>> UsesEquipmentInActiveStage(Guid equipmentId) =>
        b => !b.IsHistorical &&
             (b.Status == BatchStatus.Brewing ||
              b.Status == BatchStatus.Fermenting ||
              b.Status == BatchStatus.Conditioning) &&
             ((b.CurrentStage == BrewStage.Mash && (b.HltId == equipmentId || b.MashTunId == equipmentId || b.BoilerId == equipmentId)) ||
              (b.CurrentStage == BrewStage.Boil && b.BoilerId == equipmentId) ||
              ((b.CurrentStage == BrewStage.Ferment || b.CurrentStage == BrewStage.Condition) && b.FermenterId == equipmentId) ||
              (b.CurrentStage == BrewStage.Package && b.PackagingVesselId == equipmentId));

    public static Expression<Func<Batch, bool>> UsesEquipmentOrSensorInActiveStage(
        Guid equipmentId,
        bool isEligibleTelemetrySensor) =>
        b => !b.IsHistorical &&
             (b.Status == BatchStatus.Brewing ||
              b.Status == BatchStatus.Fermenting ||
              b.Status == BatchStatus.Conditioning) &&
             ((b.CurrentStage == BrewStage.Mash && (b.HltId == equipmentId || b.MashTunId == equipmentId || b.BoilerId == equipmentId)) ||
              (b.CurrentStage == BrewStage.Boil && b.BoilerId == equipmentId) ||
              ((b.CurrentStage == BrewStage.Ferment || b.CurrentStage == BrewStage.Condition) && b.FermenterId == equipmentId) ||
              (b.CurrentStage == BrewStage.Package && b.PackagingVesselId == equipmentId) ||
              (isEligibleTelemetrySensor && b.SensorAssignments.Any(sa =>
                  sa.EquipmentId == equipmentId &&
                  (sa.Stage == null || sa.Stage == b.CurrentStage))));

    public static Expression<Func<Batch, bool>> UsesAnyEquipmentInActiveStage(
        IReadOnlyCollection<Guid> targetIds,
        IReadOnlyCollection<Guid> assignedBatchIds) =>
        b => !b.IsHistorical &&
             (b.Status == BatchStatus.Brewing ||
              b.Status == BatchStatus.Fermenting ||
              b.Status == BatchStatus.Conditioning) &&
             ((b.CurrentStage == BrewStage.Mash &&
               ((b.HltId.HasValue && targetIds.Contains(b.HltId.Value)) ||
                (b.MashTunId.HasValue && targetIds.Contains(b.MashTunId.Value)) ||
                (b.BoilerId.HasValue && targetIds.Contains(b.BoilerId.Value)))) ||
              (b.CurrentStage == BrewStage.Boil &&
               b.BoilerId.HasValue && targetIds.Contains(b.BoilerId.Value)) ||
              ((b.CurrentStage == BrewStage.Ferment || b.CurrentStage == BrewStage.Condition) &&
               b.FermenterId.HasValue && targetIds.Contains(b.FermenterId.Value)) ||
              (b.CurrentStage == BrewStage.Package &&
               b.PackagingVesselId.HasValue && targetIds.Contains(b.PackagingVesselId.Value)) ||
              assignedBatchIds.Contains(b.Id) ||
              b.SensorAssignments.Any(sa =>
                  targetIds.Contains(sa.EquipmentId) &&
                  (sa.Stage == null || sa.Stage == b.CurrentStage)));

    /// <summary>
    /// Resolves the live batch ID currently using the given equipment in its active stage
    /// (or via Keg/Tap AssignedBatchId). Returns null when idle or only referenced by past stages.
    /// </summary>
    public static async Task<Guid?> ResolveActiveBatchIdForEquipmentAsync(
        BrewYouDbContext db,
        Equipment equipment,
        CancellationToken ct = default)
    {
        if (equipment.AssignedBatchId.HasValue)
        {
            return equipment.AssignedBatchId.Value;
        }

        var isEligibleTelemetrySensor = BatchService.IsEligibleBatchTelemetrySensor(equipment);

        if (equipment.BrewerySetup == null && equipment.BrewerySetupId != Guid.Empty)
        {
            equipment.BrewerySetup = await db.BrewerySetups
                .AsNoTracking()
                .Include(s => s.Members)
                .FirstOrDefaultAsync(s => s.Id == equipment.BrewerySetupId, ct);
        }

        var authorizedUserIds = EquipmentAccessHelper.GetAuthorizedSetupUserIds(equipment).ToList();

        return await db.Batches
            .AsNoTracking()
            .Where(UsesEquipmentOrSensorInActiveStage(equipment.Id, isEligibleTelemetrySensor))
            .Where(b =>
                b.UserId == equipment.UserId ||
                authorizedUserIds.Contains(b.UserId) ||
                (b.BrewerySetupId != null && b.BrewerySetupId == equipment.BrewerySetupId))
            .OrderByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.CreatedAt)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);
    }
}