using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services;

public record BrewingCalculationResult(
    decimal OriginalGravity,
    decimal FinalGravity,
    decimal AlcoholByVolume,
    decimal BitternessIbu,
    decimal ColorSrm
);

public static class BrewingCalculator
{
    private const double KgToLbs = 2.20462262;
    private const double LitersToGallons = 0.264172052;

    public static BrewingCalculationResult Calculate(
        decimal batchSizeLiters,
        decimal efficiencyPercent,
        int boilTimeMinutes,
        IEnumerable<RecipeIngredient> ingredients)
    {
        var ingredientList = ingredients.ToList();
        var volumeGallons = (double)batchSizeLiters * LitersToGallons;
        if (volumeGallons <= 0) volumeGallons = 5.0;

        var eff = (double)efficiencyPercent / 100.0;
        if (eff <= 0) eff = 0.72;

        // 1. Calculate Original Gravity (OG) & Color (MCU / Morey SRM)
        double totalGravityPoints = 0.0;
        double totalMcu = 0.0;
        double? yeastAttenuation = null;

        foreach (var item in ingredientList)
        {
            if (item.Ingredient == null) continue;

            if (item.Ingredient.Type == IngredientType.Fermentable)
            {
                var weightKg = (double)item.Amount;
                var weightLbs = weightKg * KgToLbs;
                var ppg = item.Ingredient.PotentialGravity.HasValue
                    ? (double)(item.Ingredient.PotentialGravity.Value - 1.0m) * 1000.0
                    : 37.0; // Default 37 PPG

                totalGravityPoints += (weightLbs * ppg * eff) / volumeGallons;

                var color = (double)(item.Ingredient.ColorSrm ?? 2.0m);
                totalMcu += (weightLbs * color) / volumeGallons;
            }
            else if (item.Ingredient.Type == IngredientType.Yeast)
            {
                if (item.Ingredient.AttenuationPercent.HasValue)
                {
                    yeastAttenuation = (double)item.Ingredient.AttenuationPercent.Value;
                }
            }
        }

        var ogPoints = Math.Max(0.0, totalGravityPoints);
        var og = 1.0 + (ogPoints / 1000.0);

        // 2. Calculate Final Gravity (FG) and ABV
        var attenuation = (yeastAttenuation ?? 75.0) / 100.0;
        var fg = 1.0 + ((og - 1.0) * (1.0 - attenuation));
        var abv = Math.Max(0.0, (og - fg) * 131.25);

        // 3. Calculate Bitterness (IBU) - Tinseth Method
        double totalIbu = 0.0;
        var bivalence = 1.65 * Math.Pow(0.000125, og - 1.0);

        foreach (var item in ingredientList)
        {
            if (item.Ingredient?.Type == IngredientType.Hop &&
                item.Usage is IngredientUsage.Boil or IngredientUsage.Mash)
            {
                var boilMins = item.DurationMinutes ?? boilTimeMinutes;
                if (boilMins < 0) boilMins = 0;

                var timeFactor = (1.0 - Math.Exp(-0.04 * boilMins)) / 4.15;
                var utilization = bivalence * timeFactor;

                var alpha = (double)(item.Ingredient.AlphaAcidPercent ?? 5.0m) / 100.0;
                var weightGrams = (double)item.Amount;
                var alphaMgL = (weightGrams * alpha * 1000.0) / (double)batchSizeLiters;

                totalIbu += utilization * alphaMgL;
            }
        }

        // 4. Calculate SRM (Morey Formula)
        var srm = totalMcu > 0 ? 1.4922 * Math.Pow(totalMcu, 0.6859) : 0.0;

        return new BrewingCalculationResult(
            OriginalGravity: Math.Round((decimal)og, 3),
            FinalGravity: Math.Round((decimal)fg, 3),
            AlcoholByVolume: Math.Round((decimal)abv, 2),
            BitternessIbu: Math.Round((decimal)totalIbu, 1),
            ColorSrm: Math.Round((decimal)srm, 1)
        );
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
}