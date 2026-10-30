using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services;

public record BrewingCalculationResult(
    decimal OriginalGravity,
    decimal FinalGravity,
    decimal AlcoholByVolume,
    decimal BitternessIbu,
    decimal ColorSrm
);

public record MashStepTempInput(
    decimal TemperatureC,
    int DurationMinutes,
    int StepOrder = 1
);

public record BrewingCalculationOptions(
    string? BitternessFormula = "Tinseth",
    string? ColorFormula = "Morey",
    string? AbvFormula = "Linear",
    IReadOnlyList<MashStepTempInput>? MashSteps = null
);

public static class BrewingCalculator
{
    private const double KgToLbs = 2.20462262;
    private const double LitersToGallons = 0.264172052;
    private const double LbToKg = 0.45359237;
    private const double OzToKg = 0.028349523125;
    private const double OzToGrams = 28.349523125;
    private const double LbToGrams = 453.59237;
    private const double ReferenceMashTempC = 67.0;
    private const double MashTempSlopePerDegreeC = 2.5;
    private const double MaxMashAttenuationDelta = 8.0;

    private static readonly string[] SolubleSugarKeywords =
    [
        "dme",
        "dry malt extract",
        "dried malt extract",
        "lme",
        "liquid malt extract",
        "malt extract",
        "extract",
        "sugar",
        "sucrose",
        "dextrose",
        "glucose",
        "fructose",
        "candi",
        "honey",
        "lactose",
        "maltodextrin",
        "molasses",
        "treacle",
        "maple syrup",
        "agave"
    ];

    private static readonly string[] UnfermentableSugarKeywords =
    [
        "lactose",
        "milk sugar",
        "maltodextrin"
    ];

    public static BrewingCalculationResult Calculate(
        decimal batchSizeLiters,
        decimal efficiencyPercent,
        int boilTimeMinutes,
        IEnumerable<RecipeIngredient> ingredients,
        BrewingCalculationOptions? options = null)
    {
        var ingredientList = ingredients.ToList();
        var safeBatchLiters = (double)batchSizeLiters > 0 ? Math.Max(0.1, (double)batchSizeLiters) : 18.9270589;
        var volumeGallons = (double)batchSizeLiters * LitersToGallons;
        if (volumeGallons <= 0) volumeGallons = 5.0;

        var eff = (double)efficiencyPercent / 100.0;
        if (eff <= 0) eff = 0.72;

        // 1. Calculate Original Gravity (OG) & Color (MCU)
        double totalGravityPoints = 0.0;
        double unfermentablePoints = 0.0;
        double totalMcu = 0.0;
        double? maxYeastAttenuation = null;

        foreach (var item in ingredientList)
        {
            if (item.Ingredient == null) continue;

            if (item.Ingredient.Type == IngredientType.Fermentable)
            {
                var weightKg = NormalizeFermentableToKg((double)item.Amount, item.Unit);
                var weightLbs = weightKg * KgToLbs;
                var ppg = item.Ingredient.PotentialGravity.HasValue
                    ? (double)(item.Ingredient.PotentialGravity.Value - 1.0m) * 1000.0
                    : 37.0; // Default 37 PPG

                var effectiveEff = IsSolubleSugarOrExtract(item.Ingredient.Name, item.Usage) ? 1.0 : eff;
                var points = (weightLbs * ppg * effectiveEff) / volumeGallons;
                totalGravityPoints += points;

                if (IsUnfermentableSugar(item.Ingredient.Name))
                {
                    unfermentablePoints += points;
                }

                var color = (double)(item.Ingredient.ColorSrm ?? 2.0m);
                totalMcu += (weightLbs * color) / volumeGallons;
            }
            else if (item.Ingredient.Type == IngredientType.Yeast)
            {
                if (item.Ingredient.AttenuationPercent.HasValue)
                {
                    var att = (double)item.Ingredient.AttenuationPercent.Value;
                    maxYeastAttenuation = maxYeastAttenuation.HasValue
                        ? Math.Max(maxYeastAttenuation.Value, att)
                        : att;
                }
            }
        }

        var ogPoints = Math.Max(0.0, totalGravityPoints);
        var og = 1.0 + (ogPoints / 1000.0);

        // 2. Calculate Final Gravity (FG) and ABV (adjusting for mash saccharification temperature and unfermentable sugars)
        var baseAttenuationPercent = maxYeastAttenuation ?? 75.0;
        var effectiveAttenuationPercent = CalculateMashAdjustedAttenuationPercent(baseAttenuationPercent, options?.MashSteps);
        var attenuation = effectiveAttenuationPercent / 100.0;
        var fermentablePoints = Math.Max(0.0, ogPoints - unfermentablePoints);
        var fgPoints = (fermentablePoints * (1.0 - attenuation)) + unfermentablePoints;
        var fg = 1.0 + (fgPoints / 1000.0);

        var abvFormula = options?.AbvFormula?.Trim() ?? "Linear";
        var abv = CalculateAbvWithFormula(og, fg, abvFormula);

        // 3. Calculate Bitterness (IBU)
        var bitternessFormula = options?.BitternessFormula?.Trim() ?? "Tinseth";
        double totalIbu = 0.0;

        foreach (var item in ingredientList)
        {
            if (item.Ingredient?.Type != IngredientType.Hop) continue;

            var alpha = (double)(item.Ingredient.AlphaAcidPercent ?? 5.0m);
            var weightGrams = NormalizeHopToGrams((double)item.Amount, item.Unit);
            var hopForm = !string.IsNullOrWhiteSpace(item.Form) ? item.Form : item.Ingredient.Form;

            totalIbu += CalculateSingleHopIbu(
                weightGrams,
                alpha,
                item.DurationMinutes,
                boilTimeMinutes,
                og,
                safeBatchLiters,
                bitternessFormula,
                item.Usage,
                hopForm
            );
        }

        // 4. Calculate SRM
        var colorFormula = options?.ColorFormula?.Trim() ?? "Morey";
        var srm = CalculateSrmWithFormula(totalMcu, colorFormula);

        return new BrewingCalculationResult(
            OriginalGravity: Math.Round((decimal)og, 3),
            FinalGravity: Math.Round((decimal)fg, 3),
            AlcoholByVolume: Math.Round((decimal)abv, 2),
            BitternessIbu: Math.Round((decimal)totalIbu, 1),
            ColorSrm: Math.Round((decimal)srm, 1)
        );
    }

    public static double NormalizeFermentableToKg(double amount, string? unit)
    {
        if (amount <= 0) return 0.0;
        if (string.IsNullOrWhiteSpace(unit)) return amount;

        return unit.Trim().ToLowerInvariant() switch
        {
            "kg" or "kilogram" or "kilograms" => amount,
            "g" or "gram" or "grams" => amount / 1000.0,
            "lb" or "lbs" or "pound" or "pounds" => amount * LbToKg,
            "oz" or "ounce" or "ounces" => amount * OzToKg,
            _ => amount
        };
    }

    public static double NormalizeHopToGrams(double amount, string? unit)
    {
        if (amount <= 0) return 0.0;
        if (string.IsNullOrWhiteSpace(unit)) return amount;

        return unit.Trim().ToLowerInvariant() switch
        {
            "g" or "gram" or "grams" => amount,
            "kg" or "kilogram" or "kilograms" => amount * 1000.0,
            "oz" or "ounce" or "ounces" => amount * OzToGrams,
            "lb" or "lbs" or "pound" or "pounds" => amount * LbToGrams,
            _ => amount
        };
    }

    public static bool IsSolubleSugarOrExtract(string? name, IngredientUsage usage)
    {
        if (usage is IngredientUsage.Whirlpool or IngredientUsage.Primary or IngredientUsage.Secondary or IngredientUsage.Bottling)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(name)) return false;

        var lower = name.Trim().ToLowerInvariant();
        foreach (var keyword in SolubleSugarKeywords)
        {
            if (lower.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUnfermentableSugar(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var lower = name.Trim().ToLowerInvariant();
        foreach (var keyword in UnfermentableSugarKeywords)
        {
            if (lower.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adjusts the yeast's laboratory base attenuation (rated in a standard 67.0 °C / 152.6 °F congress/single-infusion wort)
    /// for the recipe's active enzymatic saccharification rest temperature (60.0 °C – 72.5 °C).
    /// Beta-amylase rests below 67.0 °C (such as a 64.5 °C Hochkurz maltose rest) increase fermentability by +2.5% per °C,
    /// while alpha-amylase rests above 67.0 °C increase unfermentable limit dextrins by -2.5% per °C (clamped to ±8.0%).
    /// </summary>
    public static double CalculateMashAdjustedAttenuationPercent(
        double baseAttenuationPercent,
        IReadOnlyList<MashStepTempInput>? mashSteps)
    {
        if (mashSteps == null || mashSteps.Count == 0)
        {
            return Math.Clamp(baseAttenuationPercent, 30.0, 98.0);
        }

        var sacchSteps = mashSteps
            .Where(s => s.DurationMinutes > 0 && s.TemperatureC >= 60.0m && s.TemperatureC <= 72.5m)
            .OrderBy(s => s.StepOrder)
            .ToList();

        if (sacchSteps.Count == 0)
        {
            return Math.Clamp(baseAttenuationPercent, 30.0, 98.0);
        }

        var primaryRest = sacchSteps.FirstOrDefault(s => s.DurationMinutes >= 15);
        double primaryTempC;

        if (primaryRest != null)
        {
            primaryTempC = (double)primaryRest.TemperatureC;
        }
        else
        {
            var totalDuration = sacchSteps.Sum(s => s.DurationMinutes);
            primaryTempC = totalDuration > 0
                ? sacchSteps.Sum(s => (double)s.TemperatureC * s.DurationMinutes) / totalDuration
                : (double)sacchSteps[0].TemperatureC;
        }

        var delta = Math.Clamp((ReferenceMashTempC - primaryTempC) * MashTempSlopePerDegreeC, -MaxMashAttenuationDelta, MaxMashAttenuationDelta);
        return Math.Clamp(baseAttenuationPercent + delta, 30.0, 98.0);
    }

    private static double CalculateAbvWithFormula(double og, double fg, string formula)
    {
        if (og <= fg || og <= 1.0) return 0.0;

        if (string.Equals(formula, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            var abw = (76.08 * (og - fg)) / (1.775 - og);
            var abv = abw * (fg / 0.794);
            return Math.Max(0.0, abv);
        }

        return Math.Max(0.0, (og - fg) * 131.25);
    }

    private static double CalculateSrmWithFormula(double totalMcu, string formula)
    {
        if (totalMcu <= 0) return 0.0;

        if (string.Equals(formula, "Mosher", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(0.0, 0.3 * totalMcu + 4.7);
        }

        if (string.Equals(formula, "Daniels", StringComparison.OrdinalIgnoreCase))
        {
            return totalMcu > 11.0 ? 0.2 * totalMcu + 8.4 : totalMcu * 0.95;
        }

        return 1.4922 * Math.Pow(totalMcu, 0.6859);
    }

    private static double CalculateSingleHopIbu(
        double weightGrams,
        double alphaAcidPercent,
        int? durationMinutes,
        int defaultBoilMinutes,
        double og,
        double batchSizeLiters,
        string formula,
        IngredientUsage usage,
        string? form)
    {
        if (weightGrams <= 0 || alphaAcidPercent <= 0 || batchSizeLiters <= 0) return 0.0;

        double usageMultiplier;
        int effectiveMinutes;

        switch (usage)
        {
            case IngredientUsage.Boil:
                usageMultiplier = 1.0;
                effectiveMinutes = Math.Max(0, durationMinutes ?? defaultBoilMinutes);
                break;
            case IngredientUsage.FirstWort:
                usageMultiplier = 1.10;
                effectiveMinutes = Math.Max(0, durationMinutes is > 0 ? durationMinutes.Value : defaultBoilMinutes);
                break;
            case IngredientUsage.Mash:
                usageMultiplier = 0.30;
                effectiveMinutes = Math.Max(0, durationMinutes ?? defaultBoilMinutes);
                break;
            case IngredientUsage.Whirlpool:
                effectiveMinutes = Math.Max(0, durationMinutes ?? 0);
                usageMultiplier = effectiveMinutes > 0 ? 0.20 : 0.0;
                break;
            default:
                return 0.0;
        }

        if (usageMultiplier <= 0.0) return 0.0;

        var formMultiplier = GetHopFormMultiplier(form);

        double baseIbu;
        if (string.Equals(formula, "Rager", StringComparison.OrdinalIgnoreCase))
        {
            baseIbu = RagerIbu(weightGrams, alphaAcidPercent, effectiveMinutes, og, batchSizeLiters);
        }
        else if (string.Equals(formula, "Daniels", StringComparison.OrdinalIgnoreCase))
        {
            baseIbu = DanielsIbu(weightGrams, alphaAcidPercent, effectiveMinutes, og, batchSizeLiters);
        }
        else
        {
            baseIbu = TinsethIbu(weightGrams, alphaAcidPercent, effectiveMinutes, og, batchSizeLiters);
        }

        return baseIbu * usageMultiplier * formMultiplier;
    }

    public static double GetHopFormMultiplier(string? form)
    {
        if (string.IsNullOrWhiteSpace(form)) return 1.0;

        var lower = form.Trim().ToLowerInvariant();
        if (lower.Contains("leaf", StringComparison.Ordinal) || lower.Contains("whole", StringComparison.Ordinal))
        {
            return 0.90;
        }

        if (lower.Contains("plug", StringComparison.Ordinal))
        {
            return 0.92;
        }

        return 1.0;
    }

    private static double TinsethIbu(double weightGrams, double alphaAcidPercent, int boilTimeMinutes, double og, double batchSizeLiters)
    {
        var bivalence = 1.65 * Math.Pow(0.000125, Math.Max(1.0, og) - 1.0);
        var timeFactor = (1.0 - Math.Exp(-0.04 * Math.Max(0, boilTimeMinutes))) / 4.15;
        var utilization = bivalence * timeFactor;
        var alpha = alphaAcidPercent / 100.0;
        var alphaMgL = (weightGrams * alpha * 1000.0) / Math.Max(0.1, batchSizeLiters);
        return utilization * alphaMgL;
    }

    private static double RagerIbu(double weightGrams, double alphaAcidPercent, int boilTimeMinutes, double og, double batchSizeLiters)
    {
        var time = Math.Max(0, boilTimeMinutes);
        if (time <= 0) return 0.0;

        var gravityAdjustment = og > 1.05 ? (og - 1.05) / 0.2 : 0.0;
        var baseUtilization = (18.11 + 13.86 * Math.Tanh((time - 31.32) / 18.27)) / 100.0;
        var utilization = baseUtilization / (1.0 + gravityAdjustment);
        var alpha = alphaAcidPercent / 100.0;
        var alphaMgL = (weightGrams * alpha * 1000.0) / Math.Max(0.1, batchSizeLiters);
        return utilization * alphaMgL;
    }

    private static double DanielsIbu(double weightGrams, double alphaAcidPercent, int boilTimeMinutes, double og, double batchSizeLiters)
    {
        var time = Math.Max(0, boilTimeMinutes);
        var baseUtil = 0.05;
        if (time >= 60) baseUtil = 0.25;
        else if (time >= 45) baseUtil = 0.22;
        else if (time >= 30) baseUtil = 0.17;
        else if (time >= 15) baseUtil = 0.12;
        else if (time >= 5) baseUtil = 0.05;

        var gravityAdjustment = og > 1.05 ? (og - 1.05) / 0.2 : 0.0;
        var utilization = baseUtil / (1.0 + gravityAdjustment);
        var alpha = alphaAcidPercent / 100.0;
        var alphaMgL = (weightGrams * alpha * 1000.0) / Math.Max(0.1, batchSizeLiters);
        return utilization * alphaMgL;
    }

    /// <summary>
    /// Calculates Alcohol by Volume (ABV) using standard linear formula for OG <= 1.060,
    /// and the ASBC non-linear equation for high-gravity worts (OG > 1.060).
    /// </summary>
    public static decimal CalculateActualAbv(decimal measuredOg, decimal currentGravity)
    {
        if (measuredOg <= currentGravity || measuredOg <= 1.000m || currentGravity <= 0m)
        {
            return 0.0m;
        }

        if (measuredOg > 1.060m)
        {
            // ASBC Advanced Equation for High-Gravity Beers
            // ABW = (76.08 * (OG - FG)) / (1.775 - OG)
            // ABV = ABW * (FG / 0.794)
            var abw = (76.08m * (measuredOg - currentGravity)) / (1.775m - measuredOg);
            var abv = abw * (currentGravity / 0.794m);
            return Math.Max(0.0m, Math.Round(abv, 2));
        }

        // Standard Linear Equation
        var standardAbv = (measuredOg - currentGravity) * 131.25m;
        return Math.Max(0.0m, Math.Round(standardAbv, 2));
    }

    /// <summary>
    /// Calculates Apparent Attenuation (AA%) from Measured OG and Current Gravity.
    /// </summary>
    public static decimal CalculateApparentAttenuation(decimal measuredOg, decimal currentGravity)
    {
        if (measuredOg <= 1.000m || measuredOg <= currentGravity)
        {
            return 0.0m;
        }

        var attenuation = ((measuredOg - currentGravity) / (measuredOg - 1.000m)) * 100.0m;
        return Math.Max(0.0m, Math.Round(attenuation, 1));
    }

    /// <summary>
    /// Calculates Brewhouse Efficiency (%) based on wort volume, OG, and theoretical grain potential points.
    /// </summary>
    public static decimal CalculateBrewhouseEfficiency(decimal volumeLiters, decimal measuredOg, decimal totalPotentialPoints)
    {
        if (volumeLiters <= 0m || measuredOg <= 1.000m || totalPotentialPoints <= 0m)
        {
            return 0.0m;
        }

        var volumeGallons = volumeLiters * (decimal)LitersToGallons;
        var yieldedPoints = (measuredOg - 1.000m) * 1000.0m * volumeGallons;
        var efficiency = (yieldedPoints / totalPotentialPoints) * 100.0m;
        return Math.Max(0.0m, Math.Round(efficiency, 1));
    }

    /// <summary>
    /// Calculates Strike Water Temperature in Celsius based on thermodynamic heat capacity of malt.
    /// </summary>
    public static decimal CalculateStrikeWaterTemperature(
        decimal targetMashTempC,
        decimal grainTempC = 20.0m,
        decimal liquorToGristRatio = 3.0m,
        decimal tunLossC = 1.0m)
    {
        if (liquorToGristRatio <= 0m) liquorToGristRatio = 3.0m;

        var strikeTemp = targetMashTempC + ((0.41m / liquorToGristRatio) * (targetMashTempC - grainTempC)) + tunLossC;
        return Math.Round(strikeTemp, 1);
    }

    /// <summary>
    /// Calculates HLT water bath setpoint temperature in Celsius for HERMS coil recirculation.
    /// Typically 1.0°C - 2.5°C above desired mash exit temperature to compensate for plumbing heat loss.
    /// </summary>
    public static decimal CalculateHltTargetTemperature(
        decimal targetMashTempC,
        decimal hermsOffsetC = 1.5m)
    {
        return Math.Round(targetMashTempC + Math.Max(0.0m, hermsOffsetC), 1);
    }

    /// <summary>
    /// Evaluates whether sparge runoff has dropped below the safe gravity/pH threshold.
    /// Runoff below 1.010 SG (2.5° Plato) risks extracting harsh polyphenols/tannins from grain husks.
    /// </summary>
    public static bool IsRunoffAtRiskOfTanninExtraction(decimal runoffSpecificGravity, decimal? runoffPh = null)
    {
        if (runoffSpecificGravity <= 1.010m && runoffSpecificGravity > 0m)
        {
            return true;
        }

        if (runoffPh.HasValue && runoffPh.Value > 5.8m)
        {
            return true;
        }

        return false;
    }
}