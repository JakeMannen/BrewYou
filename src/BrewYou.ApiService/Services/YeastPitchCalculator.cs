namespace BrewYou.ApiService.Services;

public sealed record YeastPitchCalculationInput(
    decimal BatchVolumeLiters,
    decimal OriginalGravity,
    string StyleCategory = "Ale", // Ale, Hybrid, Lager, Custom
    decimal? CustomPitchRateMillionPerMlPlato = null,
    string YeastForm = "Liquid", // Liquid, Dry, Slurry
    decimal? PackageAgeDays = 0m,
    decimal? BaseViabilityPercent = null,
    decimal? CellsPerUnitBillions = null, // Liquid pack default 100B, Dry pack default 115B (or per 11.5g)
    int PacksAvailable = 1,
    decimal? SlurryDensityBillionPerMl = 1.0m,
    decimal? SlurryVolumeAvailableMl = null,
    string StarterMethod = "StirPlate", // None, Simple, Shaken, StirPlate
    decimal? StarterVolumeLiters = null);

public sealed record YeastPitchCalculationOutput(
    decimal Plato,
    decimal TargetPitchRateMillionPerMlPlato,
    decimal RequiredCellsBillions,
    decimal EstimatedViabilityPercent,
    decimal ViableCellsPerUnitBillions,
    decimal InitialViableCellsBillions,
    decimal DeficitCellsBillions,
    decimal DryPacksExact,
    int DryPacksRecommended,
    decimal RequiredSlurryMl,
    decimal RecommendedStarterVolumeLiters,
    decimal StarterDmeGrams,
    decimal ProjectedStarterCellsBillions,
    bool NeedsMultiStepStarter,
    string RecommendationSummaryKey);

/// <summary>
/// Pure domain calculator for yeast pitch rates, age-based viability decay, dry pack counts,
/// harvested slurry dosing, and yeast starter sizing.
/// </summary>
public static class YeastPitchCalculator
{
    public const decimal DefaultAlePitchRate = 0.75m;
    public const decimal DefaultHybridPitchRate = 1.00m;
    public const decimal DefaultLagerPitchRate = 1.50m;

    public const decimal DefaultLiquidPackCellsBillions = 100m;
    public const decimal DefaultDryPackCellsBillions = 115m; // ~10B/g * 11.5g standard dry pack

    public static decimal SpecificGravityToPlato(decimal sg)
    {
        if (sg <= 1.000m)
        {
            return 0m;
        }
        var sgD = (double)sg;
        var plato = -616.868 + (1111.14 * sgD) - (630.272 * sgD * sgD) + (135.997 * sgD * sgD * sgD);
        return Math.Round((decimal)Math.Max(0, plato), 2);
    }

    public static decimal ResolveTargetPitchRate(string styleCategory, decimal? customRate, decimal originalGravity)
    {
        if (customRate.HasValue && customRate.Value > 0m)
        {
            return Math.Round(customRate.Value, 2);
        }

        var normalized = (styleCategory ?? "Ale").Trim().ToLowerInvariant();
        if (normalized == "lager")
        {
            return originalGravity >= 1.065m ? 2.00m : DefaultLagerPitchRate;
        }
        if (normalized == "hybrid")
        {
            return originalGravity >= 1.070m ? 1.25m : DefaultHybridPitchRate;
        }

        // Standard Ale: 0.75M/mL/°P, bumped to 1.0M for high-gravity ales (>= 1.065)
        return originalGravity >= 1.065m ? 1.00m : DefaultAlePitchRate;
    }

    /// <summary>
    /// Computes remaining viability (%) based on yeast form and age in days.
    /// Liquid yeast decays ~20% per month, harvested slurry ~25% per month, dry yeast ~2% per month.
    /// </summary>
    public static decimal CalculateViabilityPercent(string yeastForm, decimal ageDays, decimal? baseViabilityPercent = null)
    {
        var days = Math.Max(0m, ageDays);
        var months = days / 30m;
        var normalizedForm = (yeastForm ?? "Liquid").Trim().ToLowerInvariant();
        var startViability = Math.Clamp(baseViabilityPercent ?? 98m, 1m, 100m);

        var decayPerMonth = normalizedForm switch
        {
            "dry" => 2m,
            "slurry" => 25m,
            _ => 20m
        };

        var floor = normalizedForm == "dry" ? 10m : 5m;
        var remaining = startViability - (decayPerMonth * months);
        return Math.Round(Math.Clamp(remaining, floor, 100m), 1);
    }

    private static decimal GetStarterYieldBillionPerGramDme(string method) => (method ?? "StirPlate").Trim().ToLowerInvariant() switch
    {
        "stirplate" => 1.40m,
        "shaken" => 0.75m,
        "simple" => 0.45m,
        _ => 0m
    };

    public static YeastPitchCalculationOutput Calculate(YeastPitchCalculationInput input)
    {
        var batchVolL = Math.Max(0.1m, input.BatchVolumeLiters);
        var og = Math.Clamp(input.OriginalGravity, 1.000m, 1.200m);
        var plato = SpecificGravityToPlato(og);
        var pitchRate = ResolveTargetPitchRate(input.StyleCategory, input.CustomPitchRateMillionPerMlPlato, og);

        // 1L * 1000mL/L * pitchRate(1e6/mL/P) * P = pitchRate * L * P * 1e9 cells
        var requiredCells = Math.Round(pitchRate * batchVolL * plato, 1);

        var form = (input.YeastForm ?? "Liquid").Trim().ToLowerInvariant();
        var viability = CalculateViabilityPercent(form, input.PackageAgeDays ?? 0m, input.BaseViabilityPercent);

        var nominalCellsPerUnit = input.CellsPerUnitBillions ?? form switch
        {
            "dry" => DefaultDryPackCellsBillions,
            "slurry" => Math.Max(0.1m, input.SlurryDensityBillionPerMl ?? 1.0m), // per 1 mL
            _ => DefaultLiquidPackCellsBillions
        };

        var viableCellsPerUnit = Math.Round(nominalCellsPerUnit * (viability / 100m), 2);
        var unitsCount = form == "slurry"
            ? Math.Max(0m, input.SlurryVolumeAvailableMl ?? 0m)
            : Math.Max(1, input.PacksAvailable);

        var initialViableCells = Math.Round(viableCellsPerUnit * unitsCount, 1);
        var deficit = Math.Round(Math.Max(0m, requiredCells - initialViableCells), 1);

        var dryViablePerPack = Math.Max(1m, DefaultDryPackCellsBillions * (CalculateViabilityPercent("dry", input.PackageAgeDays ?? 0m, input.BaseViabilityPercent) / 100m));
        var dryPacksExact = requiredCells > 0m ? Math.Round(requiredCells / dryViablePerPack, 2) : 0m;
        var dryPacksRecommended = requiredCells > 0m ? (int)Math.Ceiling(dryPacksExact) : 1;

        var slurryDensity = Math.Max(0.1m, input.SlurryDensityBillionPerMl ?? 1.0m);
        var slurryViablePerMl = slurryDensity * (CalculateViabilityPercent("slurry", input.PackageAgeDays ?? 0m, input.BaseViabilityPercent) / 100m);
        var requiredSlurryMl = slurryViablePerMl > 0m ? Math.Round(requiredCells / slurryViablePerMl, 0) : 0m;

        // Starter sizing: 100g DME per 1 L gives ~1.037 SG (~9.2°P)
        var yieldPerGram = GetStarterYieldBillionPerGramDme(input.StarterMethod);
        decimal starterVolL = 0m;
        decimal starterDmeG = 0m;
        decimal projectedStarterCells = initialViableCells;

        if (yieldPerGram > 0m && (input.StarterVolumeLiters.HasValue || deficit > 0m))
        {
            if (input.StarterVolumeLiters.HasValue && input.StarterVolumeLiters.Value > 0m)
            {
                starterVolL = Math.Round(input.StarterVolumeLiters.Value, 2);
            }
            else if (deficit > 0m)
            {
                // Solve needed DME for deficit: deficit / (yieldPerGram) -> /100g per L
                var neededDme = deficit / yieldPerGram;
                starterVolL = Math.Round(Math.Clamp(neededDme / 100m, 0.5m, 5.0m), 2);
            }

            if (starterVolL > 0m)
            {
                starterDmeG = Math.Round(starterVolL * 100m, 0);
                // Cap growth factor cleanly at ~5.5x inoculation for realistic single-step starter ceiling
                var rawNewGrowth = starterDmeG * yieldPerGram;
                var maxPracticalTotal = initialViableCells * 5.5m;
                projectedStarterCells = Math.Round(Math.Min(initialViableCells + rawNewGrowth, maxPracticalTotal), 1);
            }
        }

        var needsStepUp = yieldPerGram > 0m && ((deficit > 0m && projectedStarterCells < requiredCells) || starterVolL > 3.0m);

        var summaryKey = deficit <= 0m
            ? "PitchSufficient"
            : yieldPerGram <= 0m
                ? "PitchMorePacks"
                : needsStepUp
                    ? "StepUpStarterRecommended"
                    : "StarterRecommended";

        return new YeastPitchCalculationOutput(
            plato,
            pitchRate,
            requiredCells,
            viability,
            viableCellsPerUnit,
            initialViableCells,
            deficit,
            dryPacksExact,
            Math.Max(1, dryPacksRecommended),
            requiredSlurryMl,
            starterVolL,
            starterDmeG,
            projectedStarterCells,
            needsStepUp,
            summaryKey);
    }
}