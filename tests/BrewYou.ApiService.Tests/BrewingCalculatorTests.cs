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
}
