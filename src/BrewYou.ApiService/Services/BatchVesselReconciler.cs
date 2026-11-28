using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Services;

internal enum BatchVesselSlot
{
    Hlt,
    MashTun,
    Boiler,
    Fermenter,
    PackagingVessel
}

/// <summary>
/// Unified vessel volume, assignment, and stage-history reconciler shared by
/// BatchService.AdvanceStageAsync and BatchService.UpdateBatchAsync.
/// </summary>
internal static class BatchVesselReconciler
{
    private const decimal GallonsToLitersFactor = 3.785411784m;

    internal static bool IsEquipmentStillReferencedOnBatch(Batch batch, Guid equipmentId) =>
        batch.HltId == equipmentId ||
        batch.MashTunId == equipmentId ||
        batch.BoilerId == equipmentId ||
        batch.FermenterId == equipmentId ||
        batch.PackagingVesselId == equipmentId;

    internal static void SetEquipmentVolume(
        Equipment? equipment,
        decimal volumeLiters,
        DateTime now,
        HashSet<Guid>? affectedIds = null)
    {
        if (equipment == null)
        {
            return;
        }

        var clampedLiters = equipment.CapacityLiters > 0m
            ? Math.Clamp(volumeLiters, 0m, equipment.CapacityLiters)
            : Math.Max(0m, volumeLiters);

        equipment.CurrentVolumeLiters = clampedLiters;
        equipment.CurrentVolume = equipment.Unit == VolumeUnit.Gallons
            ? Math.Round(clampedLiters / GallonsToLitersFactor, 2)
            : clampedLiters;

        // Clear stale manual readings on non-IoT vessels when emptied (BE-M4)
        if (clampedLiters <= 0m && equipment.ConnectionType == EquipmentConnectionType.None)
        {
            equipment.CurrentSpecificGravity = null;
            equipment.CurrentTemperatureC = null;
            equipment.TemperatureUpdatedAt = null;
        }

        equipment.UpdatedAt = now;
        affectedIds?.Add(equipment.Id);
    }

    internal static void OccupyInitialMashVessels(
        Equipment? hlt,
        Equipment? mashTun,
        Equipment? boiler,
        decimal plannedVolume,
        decimal strikeWaterLiters,
        decimal spargeWaterLiters,
        DateTime now,
        HashSet<Guid>? affectedIds = null)
    {
        var mashVessel = mashTun ?? boiler;
        if (mashTun != null && mashTun.Id != boiler?.Id)
        {
            var mashVol = strikeWaterLiters > 0m ? strikeWaterLiters : plannedVolume;
            SetEquipmentVolume(mashTun, mashVol, now, affectedIds);
        }
        else if (boiler != null)
        {
            SetEquipmentVolume(boiler, plannedVolume, now, affectedIds);
        }

        if (hlt != null && hlt.Id != mashVessel?.Id)
        {
            SetEquipmentVolume(hlt, spargeWaterLiters, now, affectedIds);
        }
    }

    internal static async Task<bool> IsEquipmentOccupiedByOtherBatchAsync(
        BrewYouDbContext db,
        Equipment equipment,
        Guid? excludeBatchId)
    {
        if (equipment.AssignedBatchId.HasValue && equipment.AssignedBatchId.Value != excludeBatchId)
        {
            return true;
        }

        var eqId = equipment.Id;
        return await db.Batches
            .AsNoTracking()
            .Where(BatchLifecycle.UsesEquipmentInActiveStage(eqId))
            .AnyAsync(b => !excludeBatchId.HasValue || b.Id != excludeBatchId.Value);
    }

    internal static async Task ResetEquipmentVolumeIfOwnedAsync(
        BrewYouDbContext db,
        Equipment? equipment,
        Guid batchId,
        DateTime now,
        HashSet<Guid>? affectedIds = null)
    {
        if (equipment == null)
        {
            return;
        }

        if (equipment.AssignedBatchId.HasValue && equipment.AssignedBatchId.Value != batchId)
        {
            return;
        }

        if (await IsEquipmentOccupiedByOtherBatchAsync(db, equipment, batchId))
        {
            return;
        }

        SetEquipmentVolume(equipment, 0m, now, affectedIds);
    }

    internal static async Task DecoupleAssignedEquipmentAsync(
        BrewYouDbContext db,
        Guid batchId,
        DateTime now,
        HashSet<Guid>? affectedIds = null)
    {
        var assignedEquipment = await db.Equipment
            .Where(e => e.AssignedBatchId == batchId)
            .ToListAsync();

        foreach (var eq in assignedEquipment)
        {
            eq.AssignedBatchId = null;
            eq.AssignedBatch = null;
            eq.AssignedAt = null;
            if (eq.Type == EquipmentType.Keg)
            {
                SetEquipmentVolume(eq, 0m, now, affectedIds);
            }
            else
            {
                eq.UpdatedAt = now;
                affectedIds?.Add(eq.Id);
            }
        }
    }

    internal static void ReconcileStageHistory(
        Batch batch,
        BrewStage oldStage,
        BrewStage newStage,
        BatchStatus targetStatus,
        DateTime now)
    {
        if (targetStatus == BatchStatus.Archived)
        {
            return;
        }

        if (targetStatus == BatchStatus.Completed)
        {
            // Complete old stage and any skipped stages up to newStage; mark target stage completed
            var currentHistory = batch.StageHistory.FirstOrDefault(sh => sh.Stage == oldStage);
            if (currentHistory != null && currentHistory.Status != StageStatus.Completed)
            {
                currentHistory.StartedAt ??= now;
                currentHistory.Status = StageStatus.Completed;
                currentHistory.CompletedAt = now;
                currentHistory.DurationMinutes = (int)Math.Max(0, (now - currentHistory.StartedAt.Value).TotalMinutes);
            }

            for (var stageVal = (int)oldStage + 1; stageVal < (int)newStage; stageVal++)
            {
                if (!Enum.IsDefined(typeof(BrewStage), stageVal)) continue;
                var skippedStage = (BrewStage)stageVal;
                UpsertStageHistory(batch, skippedStage, StageStatus.Skipped, null, now, 0);
            }

            if (newStage != oldStage)
            {
                UpsertStageHistory(batch, newStage, StageStatus.Completed, now, now, 0);
            }

            return;
        }

        if (newStage > oldStage)
        {
            // Forward transition (AdvanceStageAsync or UpdateBatchAsync moving forward)
            var currentHistory = batch.StageHistory.FirstOrDefault(sh => sh.Stage == oldStage);
            if (currentHistory != null)
            {
                currentHistory.StartedAt ??= now;
                currentHistory.Status = StageStatus.Completed;
                currentHistory.CompletedAt = now;
                currentHistory.DurationMinutes = (int)Math.Max(0, (now - currentHistory.StartedAt.Value).TotalMinutes);
            }

            for (var stageVal = (int)oldStage + 1; stageVal < (int)newStage; stageVal++)
            {
                if (!Enum.IsDefined(typeof(BrewStage), stageVal)) continue;
                var skippedStage = (BrewStage)stageVal;
                UpsertStageHistory(batch, skippedStage, StageStatus.Skipped, null, now, 0);
            }

            UpsertStageHistory(batch, newStage, StageStatus.InProgress, now, null, null);
        }
        else
        {
            // Backward transition, same-stage status change, or reopen from Completed
            var targetHistory = batch.StageHistory.FirstOrDefault(sh => sh.Stage == newStage);
            if (targetHistory != null)
            {
                targetHistory.Status = StageStatus.InProgress;
                targetHistory.StartedAt ??= now;
                targetHistory.CompletedAt = null;
                targetHistory.DurationMinutes = null;
            }
            else
            {
                batch.StageHistory.Add(new BatchStageHistory
                {
                    BatchId = batch.Id,
                    Stage = newStage,
                    Status = StageStatus.InProgress,
                    StartedAt = now
                });
            }

            foreach (var sh in batch.StageHistory.Where(s => s.Stage > newStage))
            {
                sh.Status = StageStatus.Pending;
                sh.StartedAt = null;
                sh.CompletedAt = null;
                sh.DurationMinutes = null;
            }
        }
    }

    internal static async Task<HashSet<Guid>> ApplyTransitionAsync(
        BrewYouDbContext db,
        Batch batch,
        BatchStatus fromStatus,
        BrewStage fromStage,
        BatchStatus toStatus,
        BrewStage toStage,
        bool wasHistorical,
        DateTime now,
        decimal? measuredPreBoilVolumeLiters = null,
        decimal? measuredPackagedVolumeLiters = null)
    {
        var affectedIds = new HashSet<Guid>();

        ReconcileStageHistory(batch, fromStage, toStage, toStatus, now);

        batch.Status = toStatus;
        batch.CurrentStage = toStage;
        batch.CompletedAt = toStatus is BatchStatus.Completed or BatchStatus.Archived
            ? (batch.CompletedAt ?? now)
            : null;

        if (toStatus == BatchStatus.Planned)
        {
            // Planned batches never occupy live vessels; release everything when moving Brewing/Fermenting/etc. -> Planned
            if (!wasHistorical)
            {
                await ResetEquipmentVolumeIfOwnedAsync(db, batch.Hlt, batch.Id, now, affectedIds);
                await ResetEquipmentVolumeIfOwnedAsync(db, batch.MashTun, batch.Id, now, affectedIds);
                await ResetEquipmentVolumeIfOwnedAsync(db, batch.Boiler, batch.Id, now, affectedIds);
                await ResetEquipmentVolumeIfOwnedAsync(db, batch.Fermenter, batch.Id, now, affectedIds);
                if (batch.PackagingVessel != null && batch.PackagingVessel.Type != EquipmentType.Keg)
                {
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.PackagingVessel, batch.Id, now, affectedIds);
                }
            }

            await DecoupleAssignedEquipmentAsync(db, batch.Id, now, affectedIds);
            return affectedIds;
        }

        if (toStatus == BatchStatus.Archived)
        {
            if (!wasHistorical)
            {
                await ResetEquipmentVolumeIfOwnedAsync(db, batch.Hlt, batch.Id, now, affectedIds);
                await ResetEquipmentVolumeIfOwnedAsync(db, batch.MashTun, batch.Id, now, affectedIds);
                await ResetEquipmentVolumeIfOwnedAsync(db, batch.Boiler, batch.Id, now, affectedIds);
                await ResetEquipmentVolumeIfOwnedAsync(db, batch.Fermenter, batch.Id, now, affectedIds);
                if (batch.PackagingVessel != null && batch.PackagingVessel.Type != EquipmentType.Keg)
                {
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.PackagingVessel, batch.Id, now, affectedIds);
                }
            }

            await DecoupleAssignedEquipmentAsync(db, batch.Id, now, affectedIds);
            return affectedIds;
        }

        if (toStatus == BatchStatus.Completed)
        {
            if (!wasHistorical)
            {
                if (batch.Hlt != null && batch.HltId != batch.PackagingVesselId)
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.Hlt, batch.Id, now, affectedIds);
                if (batch.MashTun != null && batch.MashTunId != batch.PackagingVesselId)
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.MashTun, batch.Id, now, affectedIds);
                if (batch.Boiler != null && batch.BoilerId != batch.PackagingVesselId)
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.Boiler, batch.Id, now, affectedIds);
                if (batch.Fermenter != null && batch.FermenterId != batch.PackagingVesselId)
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.Fermenter, batch.Id, now, affectedIds);
            }

            if (!batch.IsHistorical && batch.PackagingVessel != null)
            {
                var vol = ResolvePackagedVolume(batch, measuredPackagedVolumeLiters);
                SetEquipmentVolume(batch.PackagingVessel, vol, now, affectedIds);

                if (batch.PackagingVessel.Type == EquipmentType.Keg)
                {
                    batch.PackagingVessel.AssignedBatchId = batch.Id;
                    batch.PackagingVessel.AssignedBatch = batch;
                    batch.PackagingVessel.AssignedAt ??= now;
                }
            }

            return affectedIds;
        }

        // Reopening or moving back from Completed/Package always decouples kegs/taps
        if (fromStatus == BatchStatus.Completed || fromStage == BrewStage.Package ||
            toStatus is BatchStatus.Brewing or BatchStatus.Fermenting)
        {
            await DecoupleAssignedEquipmentAsync(db, batch.Id, now, affectedIds);
        }

        if (toStatus == BatchStatus.Brewing)
        {
            if (!wasHistorical)
            {
                if (batch.Fermenter != null &&
                    batch.FermenterId != batch.BoilerId &&
                    batch.FermenterId != batch.MashTunId &&
                    batch.FermenterId != batch.HltId)
                {
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.Fermenter, batch.Id, now, affectedIds);
                }

                if (batch.PackagingVessel != null &&
                    batch.PackagingVessel.Type != EquipmentType.Keg &&
                    batch.PackagingVesselId != batch.BoilerId &&
                    batch.PackagingVesselId != batch.MashTunId &&
                    batch.PackagingVesselId != batch.HltId)
                {
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.PackagingVessel, batch.Id, now, affectedIds);
                }
            }

            if (!batch.IsHistorical)
            {
                if (toStage == BrewStage.Mash)
                {
                    OccupyInitialMashVessels(
                        batch.Hlt,
                        batch.MashTun,
                        batch.Boiler,
                        batch.TargetBatchSizeLiters,
                        batch.VolumeProfile?.StrikeWaterLiters ?? 0m,
                        batch.VolumeProfile?.SpargeWaterLiters ?? 0m,
                        now,
                        affectedIds);
                }
                else if (toStage == BrewStage.Boil)
                {
                    if (batch.MashTun != null && batch.MashTun.Id != batch.BoilerId)
                    {
                        await ResetEquipmentVolumeIfOwnedAsync(db, batch.MashTun, batch.Id, now, affectedIds);
                    }

                    if (batch.Hlt != null && batch.Hlt.Id != batch.BoilerId)
                    {
                        await ResetEquipmentVolumeIfOwnedAsync(db, batch.Hlt, batch.Id, now, affectedIds);
                    }

                    if (batch.Boiler != null)
                    {
                        var boilVol = measuredPreBoilVolumeLiters
                            ?? batch.VolumeProfile?.MeasuredPreBoilVolumeLiters
                            ?? (batch.MashTunId != null && batch.MashTunId != batch.BoilerId
                                ? batch.VolumeProfile?.TargetPreBoilVolumeLiters
                                : null)
                            ?? (batch.Boiler.CurrentVolumeLiters > 0m ? batch.Boiler.CurrentVolumeLiters : batch.TargetBatchSizeLiters);
                        SetEquipmentVolume(batch.Boiler, boilVol, now, affectedIds);
                    }
                }
            }

            return affectedIds;
        }

        if (toStatus is BatchStatus.Fermenting or BatchStatus.Conditioning)
        {
            if (!wasHistorical)
            {
                // Always release hot-side vessels when moving into Fermenting or Conditioning
                if (batch.MashTun != null && batch.MashTunId != batch.FermenterId)
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.MashTun, batch.Id, now, affectedIds);
                if (batch.Hlt != null && batch.HltId != batch.FermenterId)
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.Hlt, batch.Id, now, affectedIds);
                if (batch.Boiler != null && batch.BoilerId != batch.FermenterId)
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.Boiler, batch.Id, now, affectedIds);

                if ((fromStatus == BatchStatus.Completed || fromStage == BrewStage.Package) &&
                    batch.PackagingVessel != null &&
                    batch.PackagingVessel.Type != EquipmentType.Keg &&
                    batch.PackagingVesselId != batch.FermenterId)
                {
                    await ResetEquipmentVolumeIfOwnedAsync(db, batch.PackagingVessel, batch.Id, now, affectedIds);
                }
            }

            if (!batch.IsHistorical && batch.Fermenter != null)
            {
                var enteringFromNonCellar = fromStatus is BatchStatus.Planned or BatchStatus.Brewing or BatchStatus.Completed ||
                                            fromStage is BrewStage.Mash or BrewStage.Boil or BrewStage.Package;
                if (enteringFromNonCellar || batch.Fermenter.CurrentVolumeLiters <= 0m)
                {
                    var fermVol = batch.MeasuredBatchSizeLiters ?? batch.TargetBatchSizeLiters;
                    SetEquipmentVolume(batch.Fermenter, fermVol, now, affectedIds);
                }
            }

            var firstPendingFermStep = batch.FermentationSteps
                .OrderBy(fs => fs.StepOrder)
                .FirstOrDefault(fs => !fs.IsCompleted);
            if (firstPendingFermStep != null)
            {
                firstPendingFermStep.StartedAt ??= now;
            }
        }

        return affectedIds;
    }

    internal static async Task<HashSet<Guid>> ApplyVesselSwapAsync(
        BrewYouDbContext db,
        Batch batch,
        BatchVesselSlot slot,
        Equipment? oldEq,
        Equipment? newEq,
        DateTime now,
        decimal? measuredPackagedVolumeLiters = null)
    {
        var affectedIds = new HashSet<Guid>();

        if (slot == BatchVesselSlot.PackagingVessel)
        {
            if (oldEq != null && oldEq.Id != newEq?.Id)
            {
                if (oldEq.Type == EquipmentType.Keg && oldEq.AssignedBatchId == batch.Id)
                {
                    oldEq.AssignedBatchId = null;
                    oldEq.AssignedBatch = null;
                    oldEq.AssignedAt = null;
                    SetEquipmentVolume(oldEq, 0m, now, affectedIds);
                }
                else if (!batch.IsHistorical && !IsEquipmentStillReferencedOnBatch(batch, oldEq.Id))
                {
                    // Reset non-Keg oldPackagingVessel volumes on swap/clear (BE-H2)
                    await ResetEquipmentVolumeIfOwnedAsync(db, oldEq, batch.Id, now, affectedIds);
                }
            }

            if (!batch.IsHistorical &&
                batch.Status != BatchStatus.Archived &&
                (batch.CurrentStage == BrewStage.Package || batch.Status == BatchStatus.Completed) &&
                newEq != null)
            {
                var vol = ResolvePackagedVolume(batch, measuredPackagedVolumeLiters);
                SetEquipmentVolume(newEq, vol, now, affectedIds);

                if (newEq.Type == EquipmentType.Keg)
                {
                    newEq.AssignedBatchId = batch.Id;
                    newEq.AssignedBatch = batch;
                    newEq.AssignedAt ??= now;
                }
            }

            return affectedIds;
        }

        if (!BatchLifecycle.OccupiesLiveEquipment(batch))
        {
            return affectedIds;
        }

        if (oldEq != null && oldEq.Id != newEq?.Id && !IsEquipmentStillReferencedOnBatch(batch, oldEq.Id))
        {
            await ResetEquipmentVolumeIfOwnedAsync(db, oldEq, batch.Id, now, affectedIds);
        }

        if (batch.CurrentStage == BrewStage.Mash &&
            slot is BatchVesselSlot.Hlt or BatchVesselSlot.MashTun or BatchVesselSlot.Boiler)
        {
            // Reuse OccupyInitialMashVessels for consistent AIO / 2V / 3V mash volumes (BE-L1)
            OccupyInitialMashVessels(
                batch.Hlt,
                batch.MashTun,
                batch.Boiler,
                batch.TargetBatchSizeLiters,
                batch.VolumeProfile?.StrikeWaterLiters ?? 0m,
                batch.VolumeProfile?.SpargeWaterLiters ?? 0m,
                now,
                affectedIds);
        }
        else if (batch.CurrentStage == BrewStage.Boil && slot == BatchVesselSlot.Boiler && newEq != null)
        {
            var boilVol = batch.VolumeProfile?.MeasuredPreBoilVolumeLiters
                ?? batch.VolumeProfile?.TargetPreBoilVolumeLiters
                ?? batch.TargetBatchSizeLiters;
            SetEquipmentVolume(newEq, boilVol, now, affectedIds);
        }
        else if (batch.CurrentStage is BrewStage.Ferment or BrewStage.Condition &&
                 slot == BatchVesselSlot.Fermenter && newEq != null)
        {
            var fermVol = batch.MeasuredBatchSizeLiters ?? batch.TargetBatchSizeLiters;
            SetEquipmentVolume(newEq, fermVol, now, affectedIds);
        }

        return affectedIds;
    }

    internal static decimal ResolvePackagedVolume(Batch batch, decimal? overridePackagedLiters = null) =>
        overridePackagedLiters
        ?? batch.VolumeProfile?.MeasuredPackagedVolumeLiters
        ?? batch.VolumeProfile?.TargetPackagedVolumeLiters
        ?? batch.MeasuredBatchSizeLiters
        ?? batch.TargetBatchSizeLiters;

    private static void UpsertStageHistory(
        Batch batch,
        BrewStage stage,
        StageStatus status,
        DateTime? startedAt,
        DateTime? completedAt,
        int? durationMinutes)
    {
        var existing = batch.StageHistory.FirstOrDefault(sh => sh.Stage == stage);
        if (existing != null)
        {
            existing.Status = status;
            if (startedAt.HasValue) existing.StartedAt = startedAt;
            existing.CompletedAt = completedAt;
            existing.DurationMinutes = durationMinutes;
        }
        else
        {
            batch.StageHistory.Add(new BatchStageHistory
            {
                BatchId = batch.Id,
                Stage = stage,
                Status = status,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                DurationMinutes = durationMinutes
            });
        }
    }
}