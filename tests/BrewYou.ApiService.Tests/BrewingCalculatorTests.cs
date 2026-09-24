using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentAssertions;

namespace BrewYou.ApiService.Tests;

public class BrewingCalculatorTests
{
    [Fact]
    public void Calculate_WithStandardGrainBill_ComputesExpectedOriginalGravityAndColor()
    {
        // Arrange
        // 5kg Pale 2-Row (1.037, 2.0 SRM) in a 20 liter batch at 75% efficiency
        var ingredients = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 5.0m,
                Unit = "kg",
                Ingredient = new Ingredient
                {
                    Name = "Pale Malt",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.037m,
                    ColorSrm = 2.0m
                }
            }
        };

        // Act
        var result = BrewingCalculator.Calculate(
            batchSizeLiters: 20.0m,
            efficiencyPercent: 75.0m,
            boilTimeMinutes: 60,
            ingredients: ingredients
        );

        // Assert
        // Expected: ~1.055 - 1.058 OG
        result.OriginalGravity.Should().BeInRange(1.050m, 1.065m);
        result.ColorSrm.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Calculate_WithYeastAttenuation_CalculatesFinalGravityAndAbv()
    {
        // Arrange: Grain bill yielding ~1.055 OG with 80% attenuation yeast
        var ingredients = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 5.0m,
                Unit = "kg",
                Ingredient = new Ingredient
                {
                    Name = "Pale Malt",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.037m
                }
            },
            new()
            {
                Amount = 11.5m,
                Unit = "g",
                Ingredient = new Ingredient
                {
                    Name = "SafAle US-05",
                    Type = IngredientType.Yeast,
                    AttenuationPercent = 80.0m
                }
            }
        };

        // Act
        var result = BrewingCalculator.Calculate(
            batchSizeLiters: 20.0m,
            efficiencyPercent: 75.0m,
            boilTimeMinutes: 60,
            ingredients: ingredients
        );

        // Assert
        result.FinalGravity.Should().BeLessThan(result.OriginalGravity);
        result.FinalGravity.Should().BeInRange(1.008m, 1.015m);
        result.AlcoholByVolume.Should().BeInRange(5.0m, 6.5m);
    }

    [Fact]
    public void Calculate_WithBoilHops_CalculatesTinsethIbu()
    {
        // Arrange: 30g of 12% AA hops boiled for 60 min
        var ingredients = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 5.0m,
                Unit = "kg",
                Ingredient = new Ingredient
                {
                    Name = "Pale Malt",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.037m
                }
            },
            new()
            {
                Amount = 30.0m,
                Unit = "g",
                DurationMinutes = 60,
                Usage = IngredientUsage.Boil,
                Ingredient = new Ingredient
                {
                    Name = "Citra",
                    Type = IngredientType.Hop,
                    AlphaAcidPercent = 12.0m
                }
            }
        };

        // Act
        var result = BrewingCalculator.Calculate(
            batchSizeLiters: 20.0m,
            efficiencyPercent: 75.0m,
            boilTimeMinutes: 60,
            ingredients: ingredients
        );

        // Assert
        result.BitternessIbu.Should().BeInRange(30.0m, 60.0m);
    }

    [Fact]
    public void CalculateActualAbv_StandardGravity_UsesStandardLinearFormula()
    {
        // Arrange: OG 1.050, FG 1.010 -> (1.050 - 1.010) * 131.25 = 5.25%
        var og = 1.050m;
        var fg = 1.010m;

        // Act
        var abv = BrewingCalculator.CalculateActualAbv(og, fg);

        // Assert
        abv.Should().Be(5.25m);
    }

    [Fact]
    public void CalculateActualAbv_HighGravity_UsesAsbcNonlinearFormula()
    {
        // Arrange: Imperial Stout with OG 1.090 and FG 1.020 (OG > 1.060)
        var og = 1.090m;
        var fg = 1.020m;

        // Act
        var abv = BrewingCalculator.CalculateActualAbv(og, fg);

        // Assert: Nonlinear ASBC formula yields higher ABV than linear for big beers
        abv.Should().BeGreaterThan(9.0m);
        abv.Should().BeLessThan(10.5m);
    }

    [Fact]
    public void CalculateActualAbv_InvalidOrZeroDrop_ReturnsZero()
    {
        BrewingCalculator.CalculateActualAbv(1.050m, 1.050m).Should().Be(0.0m);
        BrewingCalculator.CalculateActualAbv(1.010m, 1.050m).Should().Be(0.0m);
        BrewingCalculator.CalculateActualAbv(0.999m, 0.990m).Should().Be(0.0m);
    }

    [Fact]
    public void CalculateApparentAttenuation_ComputesCorrectPercentage()
    {
        // Arrange: OG 1.050, FG 1.010 -> (50 - 10) / 50 = 80.0%
        var og = 1.050m;
        var fg = 1.010m;

        // Act
        var aa = BrewingCalculator.CalculateApparentAttenuation(og, fg);

        // Assert
        aa.Should().Be(80.0m);
    }

    [Fact]
    public void CalculateBrewhouseEfficiency_ComputesCorrectPercentage()
    {
        // Arrange: 20L batch (~5.28 gal) with 1.050 OG = 50 * 5.28 = 264 yielded points
        // If theoretical grain potential was 350 points
        var volumeLiters = 20.0m;
        var measuredOg = 1.050m;
        var totalPotentialPoints = 350.0m;

        // Act
        var efficiency = BrewingCalculator.CalculateBrewhouseEfficiency(volumeLiters, measuredOg, totalPotentialPoints);

        // Assert: 264.17 / 350 = ~75.5%
        efficiency.Should().BeInRange(74.0m, 77.0m);
    }

    [Fact]
    public void CalculateStrikeWaterTemperature_ComputesInfusionThermodynamics()
    {
        // Target mash 65°C, grain 20°C, liquor ratio 3.0, tun loss 1.0°C
        // Strike = 65 + ((0.41 / 3.0) * (65 - 20)) + 1.0 = 65 + 6.15 + 1.0 = 72.15°C -> 72.2°C
        var strikeTemp = BrewingCalculator.CalculateStrikeWaterTemperature(
            targetMashTempC: 65.0m,
            grainTempC: 20.0m,
            liquorToGristRatio: 3.0m,
            tunLossC: 1.0m
        );

        strikeTemp.Should().Be(72.2m);
    }
}