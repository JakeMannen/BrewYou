using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services;

public sealed record WaterIonProfile(
    decimal CalciumPpm,
    decimal MagnesiumPpm,
    decimal SodiumPpm,
    decimal SulfatePpm,
    decimal ChloridePpm,
    decimal BicarbonatePpm);

public sealed record SaltAdditionsInput(
    decimal GypsumGrams = 0m,
    decimal CalciumChlorideGrams = 0m,
    decimal EpsomSaltGrams = 0m,
    decimal TableSaltGrams = 0m,
    decimal BakingSodaGrams = 0m,
    decimal ChalkGrams = 0m,
    string? AcidType = null,
    decimal AcidAmountMlOrG = 0m);

public sealed record GristGrainInput(
    decimal WeightKg,
    decimal ColorSrm,
    string? Name = null,
    FermentableCategory? Category = null);

public sealed record WaterCalculationSummary(
    WaterIonProfile MashResultingIons,
    WaterIonProfile TotalResultingIons,
    decimal? SulfateToChlorideRatio,
    string BalanceProfile,
    decimal AlkalinityPpm,
    decimal ResidualAlkalinityPpm,
    decimal PredictedMashPh,
    bool HasChalkWarning,
    decimal ResultingSodiumWarningThresholdExceeded);

public sealed record SuggestedSaltAdditionsResult(
    SaltAdditionsInput MashAdditions,
    SaltAdditionsInput SpargeAdditions,
    WaterCalculationSummary Preview);

/// <summary>
/// Pure domain calculator for brewing water salts, ion profiles, residual alkalinity, and mash pH prediction.
/// Uses metric units (grams, milliliters, liters, kilograms) and standard ppm (mg/L) ion weights.
/// </summary>
public static class WaterChemistryCalculator
{
    // Ion contribution in ppm (mg/L) per 1 gram dissolved in 1 liter (= 1000 mg/L * mass fraction)
    private const decimal GypsumCaPpmPerGPerL = 232.8m;
    private const decimal GypsumSo4PpmPerGPerL = 557.7m;

    private const decimal CaCl2CaPpmPerGPerL = 272.6m;
    private const decimal CaCl2ClPpmPerGPerL = 482.3m;

    private const decimal EpsomMgPpmPerGPerL = 98.6m;
    private const decimal EpsomSo4PpmPerGPerL = 389.7m;

    private const decimal NaClNaPpmPerGPerL = 393.4m;
    private const decimal NaClClPpmPerGPerL = 606.6m;

    private const decimal BakingSodaNaPpmPerGPerL = 273.7m;
    private const decimal BakingSodaHco3PpmPerGPerL = 726.3m;

    // Chalk only partially dissolves in mash (~50% without acid sparging), we track full theoretical Stoichiometric ppm + flag warning
    private const decimal ChalkCaPpmPerGPerL = 400.4m;
    private const decimal ChalkHco3PpmPerGPerL = 599.6m;

    private const decimal Hco3MolarWeight = 61.0168m;
    private const decimal CaCo3EquivalentWeight = 50.0435m;

    public static decimal GetAcidMeqPerUnit(string? acidType) => (acidType ?? "None").Trim().ToLowerInvariant() switch
    {
        "lactic80" => 10.57m,      // mEq per mL
        "lactic88" => 11.82m,      // mEq per mL
        "phosphoric10" => 1.07m,   // mEq per mL
        "phosphoric75" => 12.10m,  // mEq per mL
        "phosphoric85" => 14.70m,  // mEq per mL
        "citric" => 15.61m,        // mEq per gram (anhydrous)
        _ => 0m
    };

    public static WaterIonProfile CalculateWaterIonsAfterAdditions(
        WaterIonProfile source,
        SaltAdditionsInput additions,
        decimal volumeLiters)
    {
        if (volumeLiters <= 0m)
        {
            return source;
        }

        var invVol = 1m / volumeLiters;
        var ca = source.CalciumPpm
            + (Math.Max(0m, additions.GypsumGrams) * GypsumCaPpmPerGPerL * invVol)
            + (Math.Max(0m, additions.CalciumChlorideGrams) * CaCl2CaPpmPerGPerL * invVol)
            + (Math.Max(0m, additions.ChalkGrams) * ChalkCaPpmPerGPerL * invVol);

        var mg = source.MagnesiumPpm
            + (Math.Max(0m, additions.EpsomSaltGrams) * EpsomMgPpmPerGPerL * invVol);

        var na = source.SodiumPpm
            + (Math.Max(0m, additions.TableSaltGrams) * NaClNaPpmPerGPerL * invVol)
            + (Math.Max(0m, additions.BakingSodaGrams) * BakingSodaNaPpmPerGPerL * invVol);

        var so4 = source.SulfatePpm
            + (Math.Max(0m, additions.GypsumGrams) * GypsumSo4PpmPerGPerL * invVol)
            + (Math.Max(0m, additions.EpsomSaltGrams) * EpsomSo4PpmPerGPerL * invVol);

        var cl = source.ChloridePpm
            + (Math.Max(0m, additions.CalciumChlorideGrams) * CaCl2ClPpmPerGPerL * invVol)
            + (Math.Max(0m, additions.TableSaltGrams) * NaClClPpmPerGPerL * invVol);

        var rawHco3 = source.BicarbonatePpm
            + (Math.Max(0m, additions.BakingSodaGrams) * BakingSodaHco3PpmPerGPerL * invVol)
            + (Math.Max(0m, additions.ChalkGrams) * ChalkHco3PpmPerGPerL * invVol);

        var acidMeq = Math.Max(0m, additions.AcidAmountMlOrG) * GetAcidMeqPerUnit(additions.AcidType);
        var hco3NeutralizedPpm = acidMeq * Hco3MolarWeight * invVol;
        var netHco3 = Math.Max(0m, rawHco3 - hco3NeutralizedPpm);

        return new WaterIonProfile(
            Math.Round(ca, 1),
            Math.Round(mg, 1),
            Math.Round(na, 1),
            Math.Round(so4, 1),
            Math.Round(cl, 1),
            Math.Round(netHco3, 1));
    }

    public static decimal CalculateAlkalinityAsCaCo3(decimal bicarbonatePpm)
        => Math.Round(bicarbonatePpm * (CaCo3EquivalentWeight / Hco3MolarWeight), 1);

    /// <summary>
    /// Kolbach Residual Alkalinity in ppm as CaCO3:
    /// RA = Alkalinity - (Ca / 1.403 + Mg / 1.701)
    /// </summary>
    public static decimal CalculateResidualAlkalinity(WaterIonProfile ions)
    {
        var alk = ions.BicarbonatePpm * (CaCo3EquivalentWeight / Hco3MolarWeight);
        var hardnessOffset = (ions.CalciumPpm / 1.403m) + (ions.MagnesiumPpm / 1.701m);
        return Math.Round(alk - hardnessOffset, 1);
    }

    public static decimal? CalculateSulfateToChlorideRatio(decimal sulfatePpm, decimal chloridePpm)
    {
        if (chloridePpm <= 0.1m)
        {
            return sulfatePpm > 0m ? null : 1.0m;
        }
        return Math.Round(sulfatePpm / chloridePpm, 2);
    }

    public static string ClassifySulfateChlorideBalance(decimal? ratio, decimal sulfatePpm, decimal chloridePpm)
    {
        if (sulfatePpm < 25m && chloridePpm < 25m)
        {
            return "SoftNeutral";
        }
        if (ratio is null || ratio >= 3.0m)
        {
            return "VeryBitter";
        }
        if (ratio >= 1.5m)
        {
            return "Bitter";
        }
        if (ratio >= 0.75m)
        {
            return "Balanced";
        }
        if (ratio >= 0.4m)
        {
            return "Malty";
        }
        return "VeryMalty";
    }

    public static decimal EstimateGrainDistilledWaterPh(GristGrainInput grain)
    {
        var nameLower = (grain.Name ?? string.Empty).ToLowerInvariant();
        if (nameLower.Contains("acidulated") || nameLower.Contains("sauermalz"))
        {
            return 3.45m;
        }

        if (grain.ColorSrm >= 150m || nameLower.Contains("roast") || nameLower.Contains("black") || nameLower.Contains("chocolate"))
        {
            return Math.Max(4.35m, 4.72m - (0.0005m * Math.Min(grain.ColorSrm, 600m)));
        }

        if (grain.ColorSrm >= 25m || nameLower.Contains("crystal") || nameLower.Contains("caramel") || nameLower.Contains("cara"))
        {
            return Math.Max(4.70m, 5.36m - (0.005m * grain.ColorSrm));
        }

        if (grain.ColorSrm >= 6m || nameLower.Contains("munich") || nameLower.Contains("vienna") || nameLower.Contains("biscuit") || nameLower.Contains("amber"))
        {
            return Math.Max(5.35m, 5.65m - (0.01m * grain.ColorSrm));
        }

        return Math.Max(5.55m, 5.74m - (0.012m * grain.ColorSrm));
    }

    public static decimal PredictMashPh(
        WaterIonProfile source,
        SaltAdditionsInput mashAdditions,
        decimal mashVolumeLiters,
        IEnumerable<GristGrainInput> grist)
    {
        var grains = grist.Where(g => g.WeightKg > 0m && g.Category is null or FermentableCategory.Grain or FermentableCategory.Adjunct).ToList();
        var totalGrainKg = grains.Sum(g => g.WeightKg);

        decimal weightedDiPh;
        if (totalGrainKg > 0m)
        {
            weightedDiPh = grains.Sum(g => EstimateGrainDistilledWaterPh(g) * g.WeightKg) / totalGrainKg;
        }
        else
        {
            weightedDiPh = 5.70m;
        }

        if (mashVolumeLiters <= 0m)
        {
            return Math.Round(weightedDiPh, 2);
        }

        var invVol = 1m / mashVolumeLiters;
        // Raw ions prior to neutralization subtraction clipping (so excess acid continues pulling pH down accurately)
        var ca = source.CalciumPpm
            + (Math.Max(0m, mashAdditions.GypsumGrams) * GypsumCaPpmPerGPerL * invVol)
            + (Math.Max(0m, mashAdditions.CalciumChlorideGrams) * CaCl2CaPpmPerGPerL * invVol)
            + (Math.Max(0m, mashAdditions.ChalkGrams) * ChalkCaPpmPerGPerL * invVol);
        var mg = source.MagnesiumPpm + (Math.Max(0m, mashAdditions.EpsomSaltGrams) * EpsomMgPpmPerGPerL * invVol);
        var rawHco3 = source.BicarbonatePpm
            + (Math.Max(0m, mashAdditions.BakingSodaGrams) * BakingSodaHco3PpmPerGPerL * invVol)
            + (Math.Max(0m, mashAdditions.ChalkGrams) * ChalkHco3PpmPerGPerL * invVol);

        var acidMeqTotal = Math.Max(0m, mashAdditions.AcidAmountMlOrG) * GetAcidMeqPerUnit(mashAdditions.AcidType);
        var effectiveHco3MeqPerL = (rawHco3 / Hco3MolarWeight) - (acidMeqTotal * invVol);
        var hardnessOffsetMeqPerL = ((ca / 1.403m) + (mg / 1.701m)) / CaCo3EquivalentWeight;
        var netRaMeqTotal = (effectiveHco3MeqPerL - hardnessOffsetMeqPerL) * mashVolumeLiters;

        // Mash grain buffering capacity is ~45 mEq per kg per pH unit
        var bufferingMeqPerPh = totalGrainKg > 0m ? 45m * totalGrainKg : 180m;
        var predicted = weightedDiPh + (netRaMeqTotal / bufferingMeqPerPh);
        return Math.Round(Math.Clamp(predicted, 3.80m, 7.20m), 2);
    }

    public static WaterCalculationSummary EvaluatePlan(
        WaterIonProfile source,
        SaltAdditionsInput mashAdditions,
        decimal mashVolumeLiters,
        SaltAdditionsInput spargeAdditions,
        decimal spargeVolumeLiters,
        IEnumerable<GristGrainInput> grist)
    {
        var mashWater = Math.Max(1m, mashVolumeLiters);
        var spargeWater = Math.Max(0m, spargeVolumeLiters);
        var totalWater = mashWater + spargeWater;

        var combinedAdditions = new SaltAdditionsInput(
            mashAdditions.GypsumGrams + spargeAdditions.GypsumGrams,
            mashAdditions.CalciumChlorideGrams + spargeAdditions.CalciumChlorideGrams,
            mashAdditions.EpsomSaltGrams + spargeAdditions.EpsomSaltGrams,
            mashAdditions.TableSaltGrams + spargeAdditions.TableSaltGrams,
            mashAdditions.BakingSodaGrams + spargeAdditions.BakingSodaGrams,
            mashAdditions.ChalkGrams + spargeAdditions.ChalkGrams,
            mashAdditions.AcidType,
            mashAdditions.AcidAmountMlOrG + spargeAdditions.AcidAmountMlOrG);

        var mashIons = CalculateWaterIonsAfterAdditions(source, mashAdditions, mashWater);
        var totalIons = CalculateWaterIonsAfterAdditions(source, combinedAdditions, totalWater);
        var ratio = CalculateSulfateToChlorideRatio(totalIons.SulfatePpm, totalIons.ChloridePpm);
        var balance = ClassifySulfateChlorideBalance(ratio, totalIons.SulfatePpm, totalIons.ChloridePpm);
        var alk = CalculateAlkalinityAsCaCo3(totalIons.BicarbonatePpm);
        var ra = CalculateResidualAlkalinity(mashIons);
        var predictedPh = PredictMashPh(source, mashAdditions, mashWater, grist);

        return new WaterCalculationSummary(
            mashIons,
            totalIons,
            ratio,
            balance,
            alk,
            ra,
            predictedPh,
            mashAdditions.ChalkGrams > 0m || spargeAdditions.ChalkGrams > 0m,
            totalIons.SodiumPpm);
    }

    /// <summary>
    /// Computes practical non-negative mash and sparge salt & lactic acid additions
    /// to approximate target mineral concentrations and hit the target mash pH.
    /// </summary>
    public static SuggestedSaltAdditionsResult SuggestSaltAdditions(
        WaterIonProfile source,
        WaterIonProfile target,
        decimal mashVolumeLiters,
        decimal spargeVolumeLiters,
        IEnumerable<GristGrainInput> grist,
        decimal targetMashPh = 5.40m,
        string acidType = "Lactic80")
    {
        var mashL = Math.Max(1m, mashVolumeLiters);
        var spargeL = Math.Max(0m, spargeVolumeLiters);
        var totalL = mashL + spargeL;
        var gristList = grist.ToList();

        var deltaSo4 = Math.Max(0m, target.SulfatePpm - source.SulfatePpm);
        var deltaCl = Math.Max(0m, target.ChloridePpm - source.ChloridePpm);
        var deltaMg = Math.Max(0m, target.MagnesiumPpm - source.MagnesiumPpm);

        // 1. Meet Mg delta with Epsom (contributes SO4 too)
        var totalEpsomG = deltaMg > 0m ? (deltaMg * totalL) / EpsomMgPpmPerGPerL : 0m;
        var so4FromEpsom = (totalEpsomG * EpsomSo4PpmPerGPerL) / totalL;
        var remSo4 = Math.Max(0m, deltaSo4 - so4FromEpsom);

        // 2. Meet remaining SO4 with Gypsum
        var totalGypsumG = remSo4 > 0m ? (remSo4 * totalL) / GypsumSo4PpmPerGPerL : 0m;

        // 3. Meet Cl delta with CaCl2 (or cap if Na budget wants NaCl? Prefer CaCl2 for healthy mash Ca >= 50ppm)
        var totalCaCl2G = deltaCl > 0m ? (deltaCl * totalL) / CaCl2ClPpmPerGPerL : 0m;

        // Split minerals proportionally between mash and sparge water
        var mashShare = mashL / totalL;
        var spargeShare = spargeL / totalL;

        var mashGypsum = Math.Round(totalGypsumG * mashShare, 1);
        var spargeGypsum = Math.Round(totalGypsumG * spargeShare, 1);
        var mashCaCl2 = Math.Round(totalCaCl2G * mashShare, 1);
        var spargeCaCl2 = Math.Round(totalCaCl2G * spargeShare, 1);
        var mashEpsom = Math.Round(totalEpsomG * mashShare, 1);
        var spargeEpsom = Math.Round(totalEpsomG * spargeShare, 1);

        // 4. Evaluate mash pH before alkalinity/acid adjustment
        var baseMashAdditions = new SaltAdditionsInput(mashGypsum, mashCaCl2, mashEpsom);
        var basePh = PredictMashPh(source, baseMashAdditions, mashL, gristList);

        decimal mashBakingSoda = 0m;
        decimal mashAcid = 0m;
        var totalGrainKg = gristList.Where(g => g.WeightKg > 0m).Sum(g => g.WeightKg);
        var buffering = totalGrainKg > 0m ? 45m * totalGrainKg : 180m;

        if (basePh > targetMashPh + 0.03m)
        {
            // Need acid (Lactic 80% default) to drop mash pH
            var phDrop = basePh - targetMashPh;
            var neededMeq = phDrop * buffering;
            var meqPerUnit = GetAcidMeqPerUnit(acidType);
            if (meqPerUnit > 0m)
            {
                mashAcid = Math.Round(neededMeq / meqPerUnit, 1);
            }
        }
        else if (basePh < targetMashPh - 0.05m)
        {
            // Dark beer mash too acidic -> Baking Soda (safer/more soluble than chalk)
            var phRise = targetMashPh - basePh;
            var neededMeq = phRise * buffering;
            // 1 g NaHCO3 gives ~11.90 mEq HCO3
            var gramsNeeded = neededMeq / 11.90m;
            mashBakingSoda = Math.Round(gramsNeeded, 1);
        }

        // Also neutralize sparge alkalinity down to ~pH 5.6 if source HCO3 > 50 ppm
        decimal spargeAcid = 0m;
        if (spargeL > 0m && source.BicarbonatePpm > 50m)
        {
            var excessHco3Ppm = source.BicarbonatePpm - 25m;
            var spargeMeq = (excessHco3Ppm / Hco3MolarWeight) * spargeL;
            var meqPerUnit = GetAcidMeqPerUnit(acidType);
            if (meqPerUnit > 0m)
            {
                spargeAcid = Math.Round(spargeMeq / meqPerUnit, 1);
            }
        }

        var finalMash = new SaltAdditionsInput(
            mashGypsum,
            mashCaCl2,
            mashEpsom,
            0m,
            mashBakingSoda,
            0m,
            acidType,
            mashAcid);

        var finalSparge = new SaltAdditionsInput(
            spargeGypsum,
            spargeCaCl2,
            spargeEpsom,
            0m,
            0m,
            0m,
            acidType,
            spargeAcid);

        var summary = EvaluatePlan(source, finalMash, mashL, finalSparge, spargeL, gristList);
        return new SuggestedSaltAdditionsResult(finalMash, finalSparge, summary);
    }
}