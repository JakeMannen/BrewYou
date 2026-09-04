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
}
