using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Services;

public interface IRigCalibrationService
{
    Task<(BrewerySetupAccessResult Result, RigCalibrationReportDto? Report, string? ErrorMessage)> GetCalibrationReportAsync(
        Guid setupId,
        string userId,
        int sampleSize = 5,
        Guid? boilerId = null,
        Guid? fermenterId = null);

    Task<(BrewerySetupAccessResult Result, RigCalibrationReportDto? Report, string? ErrorMessage)> ApplyCalibrationAsync(
        Guid setupId,
        ApplyRigCalibrationRequest request,
        string userId);
}

public class RigCalibrationService(BrewYouDbContext db) : IRigCalibrationService
{
    public async Task<(BrewerySetupAccessResult Result, RigCalibrationReportDto? Report, string? ErrorMessage)> GetCalibrationReportAsync(
        Guid setupId,
        string userId,
        int sampleSize = 5,
        Guid? boilerId = null,
        Guid? fermenterId = null)
    {
        var clampedSample = Math.Clamp(sampleSize, 1, 25);

        var setup = await db.BrewerySetups
            .AsNoTracking()
            .Include(s => s.Members)
            .Include(s => s.Equipment)
            .FirstOrDefaultAsync(s => s.Id == setupId);

        if (setup is null)
        {
            return (BrewerySetupAccessResult.NotFound, null, "Brewery setup not found.");
        }

        var hasAccess = setup.UserId == userId || setup.Members.Any(m => m.UserId == userId);
        if (!hasAccess)
        {
            return (BrewerySetupAccessResult.NotFound, null, "Brewery setup not found.");
        }

        Equipment? selectedBoiler = null;
        if (boilerId.HasValue)
        {
            selectedBoiler = setup.Equipment.FirstOrDefault(e => e.Id == boilerId.Value && e.Type == EquipmentType.Boiler);
            if (selectedBoiler is null)
            {
                return (BrewerySetupAccessResult.NotFound, null, "Selected boiler was not found in this brewery setup.");
            }
        }

        Equipment? selectedFermenter = null;
        if (fermenterId.HasValue)
        {
            selectedFermenter = setup.Equipment.FirstOrDefault(e => e.Id == fermenterId.Value && e.Type == EquipmentType.Fermenter);
            if (selectedFermenter is null)
            {
                return (BrewerySetupAccessResult.NotFound, null, "Selected fermenter was not found in this brewery setup.");
            }
        }

        var report = await BuildReportAsync(setup, clampedSample, selectedBoiler, selectedFermenter);
        return (BrewerySetupAccessResult.Success, report, null);
    }

    public async Task<(BrewerySetupAccessResult Result, RigCalibrationReportDto? Report, string? ErrorMessage)> ApplyCalibrationAsync(
        Guid setupId,
        ApplyRigCalibrationRequest request,
        string userId)
    {
        var setup = await db.BrewerySetups
            .Include(s => s.Members)
            .Include(s => s.Equipment)
            .FirstOrDefaultAsync(s => s.Id == setupId);

        if (setup is null)
        {
            return (BrewerySetupAccessResult.NotFound, null, "Brewery setup not found.");
        }

        var isOwner = setup.UserId == userId || setup.Members.Any(m => m.UserId == userId && m.Role == BreweryRole.Owner);
        if (!isOwner)
        {
            var isMember = setup.Members.Any(m => m.UserId == userId);
            return isMember
                ? (BrewerySetupAccessResult.Forbidden, null, "Only brewery owners can apply rig calibration changes.")
                : (BrewerySetupAccessResult.NotFound, null, "Brewery setup not found.");
        }

        Equipment? targetBoiler = null;
        if (request.TargetBoilerId.HasValue)
        {
            targetBoiler = setup.Equipment.FirstOrDefault(e => e.Id == request.TargetBoilerId.Value && e.Type == EquipmentType.Boiler);
            if (targetBoiler is null)
            {
                return (BrewerySetupAccessResult.NotFound, null, "Selected boiler was not found in this brewery setup.");
            }
        }

        Equipment? targetFermenter = null;
        if (request.TargetFermenterId.HasValue)
        {
            targetFermenter = setup.Equipment.FirstOrDefault(e => e.Id == request.TargetFermenterId.Value && e.Type == EquipmentType.Fermenter);
            if (targetFermenter is null)
            {
                return (BrewerySetupAccessResult.NotFound, null, "Selected fermenter was not found in this brewery setup.");
            }
        }

        var clampedSample = Math.Clamp(request.SampleSize, 1, 25);
        var report = await BuildReportAsync(setup, clampedSample, targetBoiler, targetFermenter);

        if (request.ApplyToSetup)
        {
            if (request.ApplyBoilOffRate && report.SuggestedBoilOffRatePerHour.HasValue)
            {
                setup.DefaultBoilOffRatePerHour = report.SuggestedBoilOffRatePerHour.Value;
            }
            if (request.ApplyKettleTrubLoss && report.SuggestedKettleTrubLossLiters.HasValue)
            {
                setup.DefaultKettleTrubLossLiters = report.SuggestedKettleTrubLossLiters.Value;
            }
            if (request.ApplyFermenterLoss && report.SuggestedFermenterLossLiters.HasValue)
            {
                setup.DefaultFermenterLossLiters = report.SuggestedFermenterLossLiters.Value;
            }
            if (request.ApplyGrainAbsorption && report.SuggestedGrainAbsorptionRate.HasValue)
            {
                setup.DefaultGrainAbsorptionRate = report.SuggestedGrainAbsorptionRate.Value;
            }
            if (request.ApplyEfficiency && report.SuggestedBrewhouseEfficiencyPercent.HasValue)
            {
                setup.DefaultEfficiencyPercent = report.SuggestedBrewhouseEfficiencyPercent.Value;
            }

            setup.UpdatedAt = DateTime.UtcNow;
        }

        var boilersToUpdate = targetBoiler is not null
            ? [targetBoiler]
            : request.ApplyToPrimaryEquipment
                ? setup.Equipment.Where(e => e.Type == EquipmentType.Boiler).ToList()
                : new List<Equipment>();

        foreach (var boiler in boilersToUpdate)
        {
            if (request.ApplyBoilOffRate && report.SuggestedBoilOffRatePerHour.HasValue)
            {
                boiler.BoilOffRatePerHour = report.SuggestedBoilOffRatePerHour.Value;
            }
            if (request.ApplyKettleTrubLoss && report.SuggestedKettleTrubLossLiters.HasValue)
            {
                boiler.TrubLossLiters = report.SuggestedKettleTrubLossLiters.Value;
            }
            if (request.ApplyEfficiency && report.SuggestedBrewhouseEfficiencyPercent.HasValue)
            {
                boiler.DefaultEfficiencyPercent = report.SuggestedBrewhouseEfficiencyPercent.Value;
            }
            boiler.UpdatedAt = DateTime.UtcNow;
        }

        var fermentersToUpdate = targetFermenter is not null
            ? [targetFermenter]
            : request.ApplyToPrimaryEquipment
                ? setup.Equipment.Where(e => e.Type == EquipmentType.Fermenter).ToList()
                : new List<Equipment>();

        foreach (var fermenter in fermentersToUpdate)
        {
            if (request.ApplyFermenterLoss && report.SuggestedFermenterLossLiters.HasValue)
            {
                fermenter.TrubLossLiters = report.SuggestedFermenterLossLiters.Value;
            }
            fermenter.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        var refreshed = await BuildReportAsync(setup, clampedSample, targetBoiler, targetFermenter);
        return (BrewerySetupAccessResult.Success, refreshed, null);
    }

    private async Task<RigCalibrationReportDto> BuildReportAsync(
        BrewerySetup setup,
        int sampleSize,
        Equipment? selectedBoiler = null,
        Equipment? selectedFermenter = null)
    {
        var query = db.Batches
            .AsNoTracking()
            .Include(b => b.VolumeProfile)
            .Include(b => b.Ingredients)
            .Include(b => b.Boiler)
            .Include(b => b.Fermenter)
            .Where(b => b.BrewerySetupId == setup.Id && b.Status != BatchStatus.Planned);

        if (selectedBoiler is not null)
        {
            query = query.Where(b => b.BoilerId == selectedBoiler.Id || b.MashTunId == selectedBoiler.Id);
        }

        if (selectedFermenter is not null)
        {
            query = query.Where(b => b.FermenterId == selectedFermenter.Id);
        }

        var batches = await query
            .OrderByDescending(b => b.BrewDate)
            .ThenByDescending(b => b.CreatedAt)
            .Take(sampleSize)
            .ToListAsync();

        var items = new List<BatchVarianceItemDto>(batches.Count);
        var boilOffSamples = new List<decimal>();
        var kettleTrubSamples = new List<decimal>();
        var fermenterLossSamples = new List<decimal>();
        var grainAbsorptionSamples = new List<decimal>();
        var brewhouseEffSamples = new List<decimal>();
        var mashEffSamples = new List<decimal>();
        var ogVariances = new List<decimal>();
        var volVariances = new List<decimal>();

        foreach (var batch in batches)
        {
            var vp = batch.VolumeProfile;
            var hasFermentables = batch.Ingredients.Any(i => i.Type == IngredientType.Fermentable);
            var totalPotential = hasFermentables
                ? await BatchService.CalculateTotalGrainPotentialPointsAsync(db, batch.Ingredients, batch.UserId)
                : 0m;
            var grainKg = BrewingCalculator.CalculateMashGrainWeightKg(batch.Ingredients);

            var actualEfficiency = batch.BrewhouseEfficiency;
            if (!actualEfficiency.HasValue && batch.MeasuredOg.HasValue && totalPotential > 1.0m)
            {
                var effVol = batch.MeasuredBatchSizeLiters ?? vp?.MeasuredFermenterVolumeLiters ?? batch.TargetBatchSizeLiters;
                if (effVol > 0m)
                {
                    actualEfficiency = BrewingCalculator.CalculateBrewhouseEfficiency(effVol, batch.MeasuredOg.Value, totalPotential);
                }
            }

            decimal? mashEfficiency = null;
            if (vp?.MeasuredPreBoilVolumeLiters is > 0m && vp.MeasuredPreBoilGravity is > 1.000m && totalPotential > 1.0m)
            {
                mashEfficiency = VolumeCalculator.CalculateMashEfficiency(
                    vp.MeasuredPreBoilVolumeLiters.Value,
                    vp.MeasuredPreBoilGravity.Value,
                    totalPotential);
            }

            decimal? observedBoilOff = null;
            if (vp?.MeasuredPreBoilVolumeLiters is > 0m && vp.MeasuredPostBoilVolumeLiters is > 0m && batch.BoilTimeMinutes > 0)
            {
                observedBoilOff = VolumeCalculator.CalculateActualBoilOffRate(
                    vp.MeasuredPreBoilVolumeLiters.Value,
                    vp.MeasuredPostBoilVolumeLiters.Value,
                    batch.BoilTimeMinutes);
            }

            decimal? observedKettleTrub = null;
            if (vp?.MeasuredPostBoilVolumeLiters is > 0m && vp.MeasuredFermenterVolumeLiters is > 0m)
            {
                var cooledPostBoil = vp.MeasuredPostBoilVolumeLiters.Value * (1m - (vp.CoolingShrinkagePercent / 100m));
                observedKettleTrub = Math.Round(Math.Max(0m, cooledPostBoil - vp.MeasuredFermenterVolumeLiters.Value), 2);
            }

            decimal? observedFermenterLoss = null;
            if (vp?.MeasuredFermenterVolumeLiters is > 0m && vp.MeasuredPackagedVolumeLiters is > 0m)
            {
                observedFermenterLoss = Math.Round(Math.Max(0m, vp.MeasuredFermenterVolumeLiters.Value - vp.MeasuredPackagedVolumeLiters.Value), 2);
            }

            decimal? observedGrainAbs = null;
            if (vp is { TotalWaterLiters: > 0m, MeasuredPreBoilVolumeLiters: > 0m } && grainKg > 0m)
            {
                observedGrainAbs = VolumeCalculator.CalculateActualGrainAbsorption(
                    vp.TotalWaterLiters,
                    vp.MeasuredPreBoilVolumeLiters.Value,
                    vp.MashTunDeadSpaceLiters,
                    grainKg);
            }

            var actualAbv = batch.AlcoholByVolume;
            if (!actualAbv.HasValue && batch.MeasuredOg.HasValue && batch.MeasuredFg.HasValue)
            {
                actualAbv = BrewingCalculator.CalculateActualAbv(batch.MeasuredOg.Value, batch.MeasuredFg.Value);
            }

            var ogVariancePoints = batch.MeasuredOg.HasValue
                ? Math.Round((batch.MeasuredOg.Value - batch.TargetOg) * 1000m, 1)
                : (decimal?)null;

            var fgVariancePoints = batch.MeasuredFg.HasValue
                ? Math.Round((batch.MeasuredFg.Value - batch.TargetFg) * 1000m, 1)
                : (decimal?)null;

            var abvVariance = actualAbv.HasValue
                ? Math.Round(actualAbv.Value - batch.TargetAbv, 2)
                : (decimal?)null;

            var effVariance = actualEfficiency.HasValue
                ? Math.Round(actualEfficiency.Value - batch.EfficiencyPercent, 1)
                : (decimal?)null;

            var measuredFermenterVol = vp?.MeasuredFermenterVolumeLiters ?? batch.MeasuredBatchSizeLiters;
            var targetFermenterVol = vp?.TargetFermenterVolumeLiters ?? batch.TargetBatchSizeLiters;
            var volVariance = measuredFermenterVol.HasValue
                ? Math.Round(measuredFermenterVol.Value - targetFermenterVol, 2)
                : (decimal?)null;

            if (observedBoilOff.HasValue) boilOffSamples.Add(observedBoilOff.Value);
            if (observedKettleTrub.HasValue) kettleTrubSamples.Add(observedKettleTrub.Value);
            if (observedFermenterLoss.HasValue) fermenterLossSamples.Add(observedFermenterLoss.Value);
            if (observedGrainAbs.HasValue) grainAbsorptionSamples.Add(observedGrainAbs.Value);
            if (actualEfficiency.HasValue) brewhouseEffSamples.Add(actualEfficiency.Value);
            if (mashEfficiency.HasValue) mashEffSamples.Add(mashEfficiency.Value);
            if (ogVariancePoints.HasValue) ogVariances.Add(ogVariancePoints.Value);
            if (volVariance.HasValue) volVariances.Add(volVariance.Value);

            items.Add(new BatchVarianceItemDto(
                batch.Id,
                batch.BatchCode,
                batch.Name,
                batch.BeerStyle,
                batch.BrewDate,
                batch.Status.ToString(),
                batch.RecipeName,
                batch.TargetOg,
                batch.MeasuredOg,
                ogVariancePoints,
                batch.TargetFg,
                batch.MeasuredFg,
                fgVariancePoints,
                batch.TargetAbv,
                actualAbv,
                abvVariance,
                batch.EfficiencyPercent,
                actualEfficiency,
                effVariance,
                mashEfficiency,
                vp?.TargetPreBoilVolumeLiters,
                vp?.MeasuredPreBoilVolumeLiters,
                vp?.TargetPostBoilVolumeLiters,
                vp?.MeasuredPostBoilVolumeLiters,
                targetFermenterVol,
                measuredFermenterVol,
                volVariance,
                vp?.TargetPackagedVolumeLiters,
                vp?.MeasuredPackagedVolumeLiters,
                vp?.BoilOffRatePerHour,
                observedBoilOff,
                observedKettleTrub,
                observedFermenterLoss,
                observedGrainAbs,
                batch.BoilerId,
                batch.Boiler?.Name,
                batch.FermenterId,
                batch.Fermenter?.Name));
        }

        decimal? AvgOrNull(List<decimal> values, int decimals) =>
            values.Count > 0 ? Math.Round(values.Average(), decimals) : null;

        var suggestedBoilOff = AvgOrNull(boilOffSamples, 2);
        var suggestedKettleTrub = AvgOrNull(kettleTrubSamples, 2);
        var suggestedFermenterLoss = AvgOrNull(fermenterLossSamples, 2);
        var suggestedGrainAbs = AvgOrNull(grainAbsorptionSamples, 2);
        var suggestedBrewhouseEff = AvgOrNull(brewhouseEffSamples, 1);
        var suggestedMashEff = AvgOrNull(mashEffSamples, 1);

        var currentBoilOff = selectedBoiler?.BoilOffRatePerHour ?? setup.DefaultBoilOffRatePerHour;
        var currentKettleTrub = selectedBoiler?.TrubLossLiters ?? setup.DefaultKettleTrubLossLiters;
        var currentFermenterLoss = selectedFermenter?.TrubLossLiters ?? setup.DefaultFermenterLossLiters;
        var currentEfficiency = selectedBoiler?.DefaultEfficiencyPercent ?? setup.DefaultEfficiencyPercent;

        return new RigCalibrationReportDto(
            setup.Id,
            setup.Name,
            sampleSize,
            batches.Count,
            currentBoilOff,
            suggestedBoilOff,
            boilOffSamples.Count,
            currentKettleTrub,
            suggestedKettleTrub,
            kettleTrubSamples.Count,
            currentFermenterLoss,
            suggestedFermenterLoss,
            fermenterLossSamples.Count,
            setup.DefaultGrainAbsorptionRate,
            suggestedGrainAbs,
            grainAbsorptionSamples.Count,
            suggestedBrewhouseEff,
            brewhouseEffSamples.Count,
            suggestedMashEff,
            AvgOrNull(ogVariances, 1),
            AvgOrNull(volVariances, 2),
            suggestedBoilOff.HasValue || suggestedKettleTrub.HasValue || suggestedFermenterLoss.HasValue || suggestedGrainAbs.HasValue || suggestedBrewhouseEff.HasValue,
            items,
            selectedBoiler?.Id,
            selectedBoiler?.Name,
            selectedFermenter?.Id,
            selectedFermenter?.Name,
            currentEfficiency);
    }
}