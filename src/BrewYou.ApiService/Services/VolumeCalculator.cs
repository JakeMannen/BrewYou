using BrewYou.ApiService.Data.Entities;
using System;
using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetVolumeBasis
{
    Fermenter,
    Packaged
}

public record VolumeCalculationInput(
    decimal TargetBatchSizeLiters,
    int BoilTimeMinutes,
    decimal TotalGrainWeightKg,
    TargetVolumeBasis TargetBasis = TargetVolumeBasis.Fermenter,
    bool SpargeEnabled = true,
    decimal BoilOffRatePerHour = 3.0m,
    decimal GrainAbsorptionRate = 0.96m,
    decimal KettleTrubLossLiters = 1.5m,
    decimal FermenterLossLiters = 1.5m,
    decimal MashTunDeadSpaceLiters = 0.0m,
    decimal CoolingShrinkagePercent = 4.0m,
    decimal PackagingLossLiters = 0.5m,
    decimal MashThicknessLitersPerKg = 3.0m
);

public record VolumeCalculationResult(
    decimal TotalWaterLiters,
    decimal StrikeWaterLiters,
    decimal SpargeWaterLiters,

    decimal TargetPreBoilVolumeLiters,
    decimal TargetPostBoilVolumeLiters,
    decimal TargetFermenterVolumeLiters,
    decimal TargetPackagedVolumeLiters,

    decimal GrainAbsorptionLossLiters,
    decimal BoilOffLossLiters,
    decimal KettleTrubLossLiters,
    decimal ShrinkageLossLiters,
    decimal FermenterLossLiters,
    decimal PackagingLossLiters,
    decimal MashTunDeadSpaceLossLiters
);

public static class VolumeCalculator
{
    private const decimal LitersToGallons = 0.264172052m;

    /// <summary>
    /// Calculates the full water schedule and volume milestones, working backwards from the target batch size.
    /// When TargetBasis is Fermenter (default), TargetBatchSizeLiters represents the volume into the fermenter.
    /// When TargetBasis is Packaged, TargetBatchSizeLiters represents the packaged yield, and all losses are added backwards.
    /// </summary>
    public static VolumeCalculationResult CalculateWaterRequirements(VolumeCalculationInput input)
    {
        decimal targetFermenterVolume;
        decimal targetPackagedVolume;

        if (input.TargetBasis == TargetVolumeBasis.Packaged)
        {
            // Brewer specifies desired packaged yield (keg / bottles); work backwards
            targetPackagedVolume = input.TargetBatchSizeLiters;
            targetFermenterVolume = targetPackagedVolume + input.FermenterLossLiters + input.PackagingLossLiters;
        }
        else
        {
            // Brewer specifies volume into fermenter
            targetFermenterVolume = input.TargetBatchSizeLiters;
            targetPackagedVolume = targetFermenterVolume - input.FermenterLossLiters;
        }

        // 2. Need this in the kettle after boil, cold (to yield targetFermenterVolume after kettle trub loss)
        var postBoilCold = targetFermenterVolume + input.KettleTrubLossLiters;

        // 3. Hot volume in kettle at flameout
        var shrinkageFactor = 1.0m - (input.CoolingShrinkagePercent / 100.0m);
        var postBoilHot = shrinkageFactor > 0m ? postBoilCold / shrinkageFactor : postBoilCold;

        // 4. Boil off
        var boilOff = input.BoilOffRatePerHour * (input.BoilTimeMinutes / 60.0m);

        // 5. Hot volume pre-boil
        var preBoilHot = postBoilHot + boilOff;

        // 6. Pre-boil cold equivalent
        var preBoilCold = preBoilHot * shrinkageFactor;

        // 7. Grain absorption
        var grainAbsorption = input.TotalGrainWeightKg * input.GrainAbsorptionRate;

        // 8. Total water
        var totalWater = preBoilCold + grainAbsorption + input.MashTunDeadSpaceLiters;

        // 9. Strike and sparge water split
        decimal strikeWater;
        decimal spargeWater;

        if (input.SpargeEnabled)
        {
            strikeWater = (input.TotalGrainWeightKg * input.MashThicknessLitersPerKg) + input.MashTunDeadSpaceLiters;
            spargeWater = Math.Max(0m, totalWater - strikeWater);
        }
        else
        {
            strikeWater = totalWater;
            spargeWater = 0m;
        }

        return new VolumeCalculationResult(
            TotalWaterLiters: Math.Round(totalWater, 2),
            StrikeWaterLiters: Math.Round(strikeWater, 2),
            SpargeWaterLiters: Math.Round(spargeWater, 2),

            TargetPreBoilVolumeLiters: Math.Round(preBoilCold, 2),
            TargetPostBoilVolumeLiters: Math.Round(postBoilCold, 2),
            TargetFermenterVolumeLiters: Math.Round(targetFermenterVolume, 2),
            TargetPackagedVolumeLiters: Math.Round(targetPackagedVolume, 2),

            GrainAbsorptionLossLiters: Math.Round(grainAbsorption, 2),
            BoilOffLossLiters: Math.Round(boilOff, 2),
            KettleTrubLossLiters: Math.Round(input.KettleTrubLossLiters, 2),
            ShrinkageLossLiters: Math.Round(postBoilHot - postBoilCold, 2),
            FermenterLossLiters: Math.Round(input.FermenterLossLiters, 2),
            PackagingLossLiters: Math.Round(input.PackagingLossLiters, 2),
            MashTunDeadSpaceLossLiters: Math.Round(input.MashTunDeadSpaceLiters, 2)
        );
    }

    /// <summary>
    /// Returns expected post-boil cold volume based on pre-boil volume (cold equivalent).
    /// </summary>
    public static decimal CalculateExpectedPostBoilVolume(
        decimal measuredPreBoilVolumeLiters,
        decimal boilOffRatePerHour,
        int boilTimeMinutes,
        decimal coolingShrinkagePercent = 4.0m)
    {
        var shrinkageFactor = 1.0m - (coolingShrinkagePercent / 100.0m);
        if (shrinkageFactor <= 0m) return 0.0m;

        var preBoilHot = measuredPreBoilVolumeLiters / shrinkageFactor;
        var boilOff = boilOffRatePerHour * (boilTimeMinutes / 60.0m);
        var postBoilHot = preBoilHot - boilOff;
        var postBoilCold = postBoilHot * shrinkageFactor;

        return Math.Max(0m, Math.Round(postBoilCold, 2));
    }

    /// <summary>
    /// Recalculates downstream milestone targets from measured checkpoints based on equipment settings.
    /// When a brewer manually records or edits a volume between steps (pre-boil, post-boil, or fermenter),
    /// subsequent target volumes are dynamically updated according to boil-off, cooling shrinkage, and vessel losses.
    /// </summary>
    public static void RecalculatePostStepMilestones(BatchVolumeProfile profile, int boilTimeMinutes)
    {
        if (profile == null) return;

        // 1. Post-Boil Milestone (updated if pre-boil is measured)
        if (profile.MeasuredPreBoilVolumeLiters.HasValue)
        {
            profile.TargetPostBoilVolumeLiters = CalculateExpectedPostBoilVolume(
                profile.MeasuredPreBoilVolumeLiters.Value,
                profile.BoilOffRatePerHour,
                boilTimeMinutes,
                profile.CoolingShrinkagePercent
            );
        }

        // 2. Fermenter Milestone (derived from measured or expected post-boil minus kettle trub loss)
        var effectivePostBoil = profile.MeasuredPostBoilVolumeLiters ?? profile.TargetPostBoilVolumeLiters;
        profile.TargetFermenterVolumeLiters = Math.Max(0m, Math.Round(effectivePostBoil - profile.KettleTrubLossLiters, 2));

        // 3. Packaging Milestone (derived from measured or expected fermenter volume minus equipment losses)
        var effectiveFermenter = profile.MeasuredFermenterVolumeLiters ?? profile.TargetFermenterVolumeLiters;
        var fermenterToPackagedLoss = profile.FermenterTrubLossLiters + profile.PackagingLossLiters;
        profile.TargetPackagedVolumeLiters = Math.Max(0m, Math.Round(effectiveFermenter - fermenterToPackagedLoss, 2));
    }

    /// <summary>
    /// Calculates Expected OG based on pre-boil volume and gravity point concentration.
    /// </summary>
    public static decimal CalculateExpectedOg(
        decimal measuredPreBoilVolumeLiters,
        decimal measuredPreBoilGravity,
        decimal expectedPostBoilVolumeLiters)
    {
        if (expectedPostBoilVolumeLiters <= 0m || measuredPreBoilVolumeLiters <= 0m || measuredPreBoilGravity < 1.000m)
        {
            return 0.0m;
        }

        var og = 1.0m + ((measuredPreBoilVolumeLiters * (measuredPreBoilGravity - 1.0m)) / expectedPostBoilVolumeLiters);
        return Math.Round(og, 3);
    }

    /// <summary>
    /// Returns extra minutes of boiling needed if volume is too high. Returns 0 if volume is at or below target.
    /// </summary>
    public static int CalculateBoilTimeAdjustment(
        decimal actualPreBoilVolumeLiters,
        decimal targetPreBoilVolumeLiters,
        decimal boilOffRatePerHour)
    {
        if (boilOffRatePerHour <= 0m || actualPreBoilVolumeLiters <= targetPreBoilVolumeLiters)
        {
            return 0;
        }

        var excessVolume = actualPreBoilVolumeLiters - targetPreBoilVolumeLiters;
        var extraHours = excessVolume / boilOffRatePerHour;
        return (int)Math.Round(extraHours * 60.0m, 0);
    }

    /// <summary>
    /// Returns actual L/hr evaporation for equipment profile calibration.
    /// </summary>
    public static decimal CalculateActualBoilOffRate(
        decimal preBoilVolumeLiters,
        decimal postBoilVolumeLiters,
        int boilTimeMinutes)
    {
        if (boilTimeMinutes <= 0 || preBoilVolumeLiters <= postBoilVolumeLiters)
        {
            return 0.0m;
        }

        var volumeLost = preBoilVolumeLiters - postBoilVolumeLiters;
        var boilOffRatePerHour = volumeLost / (boilTimeMinutes / 60.0m);
        return Math.Round(boilOffRatePerHour, 2);
    }

    /// <summary>
    /// Returns actual L/kg absorption for equipment profile calibration.
    /// </summary>
    public static decimal CalculateActualGrainAbsorption(
        decimal totalWaterUsedLiters,
        decimal preBoilVolumeLiters,
        decimal mashTunDeadSpaceLiters,
        decimal totalGrainWeightKg)
    {
        if (totalGrainWeightKg <= 0m)
        {
            return 0.0m;
        }

        var grainAbsorption = totalWaterUsedLiters - preBoilVolumeLiters - mashTunDeadSpaceLiters;
        var rate = grainAbsorption / totalGrainWeightKg;
        return Math.Max(0.0m, Math.Round(rate, 2));
    }

    /// <summary>
    /// Calculates mash (pre-boil) efficiency based on pre-boil volume and gravity vs total potential points.
    /// </summary>
    public static decimal CalculateMashEfficiency(
        decimal preBoilVolumeLiters,
        decimal preBoilGravity,
        decimal totalPotentialPoints)
    {
        if (preBoilVolumeLiters <= 0m || preBoilGravity <= 1.000m || totalPotentialPoints <= 0m)
        {
            return 0.0m;
        }

        var volumeGallons = preBoilVolumeLiters * LitersToGallons;
        var yieldedPoints = (preBoilGravity - 1.000m) * 1000.0m * volumeGallons;
        var efficiency = (yieldedPoints / totalPotentialPoints) * 100.0m;
        return Math.Max(0.0m, Math.Round(efficiency, 1));
    }
}