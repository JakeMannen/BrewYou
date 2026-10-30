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

    [Fact]
    public void CalculateHltTargetTemperature_AppliesHermsOffset()
    {
        // Target mash 66.0°C with 1.5°C offset -> 67.5°C
        var hltTemp = BrewingCalculator.CalculateHltTargetTemperature(targetMashTempC: 66.0m, hermsOffsetC: 1.5m);
        hltTemp.Should().Be(67.5m);
    }

    [Fact]
    public void IsRunoffAtRiskOfTanninExtraction_CorrectlyFlagsLowGravityOrHighPh()
    {
        // Safe runoff: 1.020 SG, pH 5.4
        BrewingCalculator.IsRunoffAtRiskOfTanninExtraction(1.020m, 5.4m).Should().BeFalse();

        // At-risk: runoff dropped to 1.008 SG
        BrewingCalculator.IsRunoffAtRiskOfTanninExtraction(1.008m, 5.4m).Should().BeTrue();

        // At-risk: runoff pH rose to 6.0
        BrewingCalculator.IsRunoffAtRiskOfTanninExtraction(1.015m, 6.0m).Should().BeTrue();
    }

    [Fact]
    public void Calculate_WithBitternessFormulas_DistinguishesTinsethRagerAndDaniels()
    {
        var ingredients = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 5.0m,
                Unit = "kg",
                Usage = IngredientUsage.Mash,
                Ingredient = new Ingredient
                {
                    Name = "Pale Malt",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.037m,
                    ColorSrm = 2.0m
                }
            },
            new()
            {
                Amount = 35.0m,
                Unit = "g",
                DurationMinutes = 60,
                Usage = IngredientUsage.Boil,
                Form = "Pellet",
                Ingredient = new Ingredient
                {
                    Name = "Centennial",
                    Type = IngredientType.Hop,
                    AlphaAcidPercent = 10.0m
                }
            }
        };

        var tinseth = BrewingCalculator.Calculate(20.0m, 75.0m, 60, ingredients, new BrewingCalculationOptions("Tinseth", "Morey", "Linear"));
        var rager = BrewingCalculator.Calculate(20.0m, 75.0m, 60, ingredients, new BrewingCalculationOptions("Rager", "Morey", "Linear"));
        var daniels = BrewingCalculator.Calculate(20.0m, 75.0m, 60, ingredients, new BrewingCalculationOptions("Daniels", "Morey", "Linear"));

        tinseth.BitternessIbu.Should().BeGreaterThan(30.0m);
        rager.BitternessIbu.Should().BeGreaterThan(tinseth.BitternessIbu);
        daniels.BitternessIbu.Should().NotBe(tinseth.BitternessIbu);
    }

    [Fact]
    public void Calculate_WithColorAndAbvFormulas_HonorsSelectedFormulas()
    {
        var ingredients = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 8.0m,
                Unit = "kg",
                Usage = IngredientUsage.Mash,
                Ingredient = new Ingredient
                {
                    Name = "Munich Malt",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.037m,
                    ColorSrm = 10.0m
                }
            },
            new()
            {
                Amount = 11.5m,
                Unit = "g",
                Usage = IngredientUsage.Primary,
                Ingredient = new Ingredient
                {
                    Name = "SafAle US-05",
                    Type = IngredientType.Yeast,
                    AttenuationPercent = 80.0m
                }
            }
        };

        var moreyLinear = BrewingCalculator.Calculate(20.0m, 75.0m, 60, ingredients, new BrewingCalculationOptions("Tinseth", "Morey", "Linear"));
        var mosherAdvanced = BrewingCalculator.Calculate(20.0m, 75.0m, 60, ingredients, new BrewingCalculationOptions("Tinseth", "Mosher", "Advanced"));
        var danielsColor = BrewingCalculator.Calculate(20.0m, 75.0m, 60, ingredients, new BrewingCalculationOptions("Tinseth", "Daniels", "Linear"));

        moreyLinear.ColorSrm.Should().NotBe(mosherAdvanced.ColorSrm);
        danielsColor.ColorSrm.Should().NotBe(moreyLinear.ColorSrm);
        mosherAdvanced.AlcoholByVolume.Should().BeGreaterThan(moreyLinear.AlcoholByVolume);
    }

    [Fact]
    public void Calculate_WithHopUsagesAndForms_AppliesFirstWortWhirlpoolMashAndLeafMultipliers()
    {
        List<RecipeIngredient> BuildWithHop(IngredientUsage usage, string form, int mins = 60) =>
        [
            new()
            {
                Amount = 4.5m,
                Unit = "kg",
                Usage = IngredientUsage.Mash,
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
                DurationMinutes = mins,
                Usage = usage,
                Form = form,
                Ingredient = new Ingredient
                {
                    Name = "Cascade",
                    Type = IngredientType.Hop,
                    AlphaAcidPercent = 6.5m
                }
            }
        ];

        var boilPellet = BrewingCalculator.Calculate(20.0m, 75.0m, 60, BuildWithHop(IngredientUsage.Boil, "Pellet", 60));
        var fwhPellet = BrewingCalculator.Calculate(20.0m, 75.0m, 60, BuildWithHop(IngredientUsage.FirstWort, "Pellet", 60));
        var boilLeaf = BrewingCalculator.Calculate(20.0m, 75.0m, 60, BuildWithHop(IngredientUsage.Boil, "Leaf", 60));
        var whirlpool = BrewingCalculator.Calculate(20.0m, 75.0m, 60, BuildWithHop(IngredientUsage.Whirlpool, "Pellet", 20));
        var mashHop = BrewingCalculator.Calculate(20.0m, 75.0m, 60, BuildWithHop(IngredientUsage.Mash, "Pellet", 60));
        var dryHop = BrewingCalculator.Calculate(20.0m, 75.0m, 60, BuildWithHop(IngredientUsage.DryHop, "Pellet", 4320));

        fwhPellet.BitternessIbu.Should().BeApproximately(boilPellet.BitternessIbu * 1.10m, 0.2m);
        boilLeaf.BitternessIbu.Should().BeApproximately(boilPellet.BitternessIbu * 0.90m, 0.2m);
        whirlpool.BitternessIbu.Should().BeGreaterThan(0m).And.BeLessThan(boilPellet.BitternessIbu);
        mashHop.BitternessIbu.Should().BeApproximately(boilPellet.BitternessIbu * 0.30m, 0.2m);
        dryHop.BitternessIbu.Should().Be(0m);
    }

    [Fact]
    public void Calculate_WithSolubleSugarAndLactose_BypassesEfficiencyAndRetainsUnfermentableFg()
    {
        var grainOnly = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 4.0m,
                Unit = "kg",
                Usage = IngredientUsage.Mash,
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
                Usage = IngredientUsage.Primary,
                Ingredient = new Ingredient
                {
                    Name = "SafAle US-05",
                    Type = IngredientType.Yeast,
                    AttenuationPercent = 80.0m
                }
            }
        };

        var withDextrose = new List<RecipeIngredient>(grainOnly)
        {
            new()
            {
                Amount = 0.5m,
                Unit = "kg",
                Usage = IngredientUsage.Boil,
                Ingredient = new Ingredient
                {
                    Name = "Corn Sugar (Dextrose)",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.046m
                }
            }
        };

        var withLactose = new List<RecipeIngredient>(grainOnly)
        {
            new()
            {
                Amount = 0.5m,
                Unit = "kg",
                Usage = IngredientUsage.Boil,
                Ingredient = new Ingredient
                {
                    Name = "Milk Sugar (Lactose)",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.043m
                }
            }
        };

        // Dextrose OG contribution must be identical at 60% vs 85% mash efficiency (100% soluble)
        var base60 = BrewingCalculator.Calculate(20.0m, 60.0m, 60, grainOnly);
        var dex60 = BrewingCalculator.Calculate(20.0m, 60.0m, 60, withDextrose);
        var base85 = BrewingCalculator.Calculate(20.0m, 85.0m, 60, grainOnly);
        var dex85 = BrewingCalculator.Calculate(20.0m, 85.0m, 60, withDextrose);

        (dex60.OriginalGravity - base60.OriginalGravity).Should()
            .BeApproximately(dex85.OriginalGravity - base85.OriginalGravity, 0.001m);

        // Lactose must retain 100% of its added gravity points in Final Gravity
        var base75 = BrewingCalculator.Calculate(20.0m, 75.0m, 60, grainOnly);
        var lac75 = BrewingCalculator.Calculate(20.0m, 75.0m, 60, withLactose);
        var addedLactosePoints = lac75.OriginalGravity - base75.OriginalGravity;

        (lac75.FinalGravity - base75.FinalGravity).Should()
            .BeApproximately(addedLactosePoints, 0.001m);
    }

    [Fact]
    public void Calculate_WithMultipleYeastsAndImperialUnits_UsesMaxAttenuationAndNormalizesUnits()
    {
        var imperialCoPitched = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 11.0231m, // ~5.0 kg
                Unit = "lb",
                Usage = IngredientUsage.Mash,
                Ingredient = new Ingredient
                {
                    Name = "Pale Malt",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.037m,
                    ColorSrm = 2.0m
                }
            },
            new()
            {
                Amount = 1.0582m, // ~30.0 g
                Unit = "oz",
                DurationMinutes = 60,
                Usage = IngredientUsage.Boil,
                Ingredient = new Ingredient
                {
                    Name = "Citra",
                    Type = IngredientType.Hop,
                    AlphaAcidPercent = 12.0m
                }
            },
            new()
            {
                Amount = 1m,
                Unit = "pkg",
                Usage = IngredientUsage.Primary,
                Ingredient = new Ingredient
                {
                    Name = "English Ale Yeast",
                    Type = IngredientType.Yeast,
                    AttenuationPercent = 70.0m
                }
            },
            new()
            {
                Amount = 1m,
                Unit = "pkg",
                Usage = IngredientUsage.Secondary,
                Ingredient = new Ingredient
                {
                    Name = "Belle Saison",
                    Type = IngredientType.Yeast,
                    AttenuationPercent = 88.0m
                }
            }
        };

        var metricSingleHighAtten = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 5.0m,
                Unit = "kg",
                Usage = IngredientUsage.Mash,
                Ingredient = new Ingredient
                {
                    Name = "Pale Malt",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.037m,
                    ColorSrm = 2.0m
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
            },
            new()
            {
                Amount = 1m,
                Unit = "pkg",
                Usage = IngredientUsage.Primary,
                Ingredient = new Ingredient
                {
                    Name = "Belle Saison",
                    Type = IngredientType.Yeast,
                    AttenuationPercent = 88.0m
                }
            }
        };

        var imperialResult = BrewingCalculator.Calculate(20.0m, 75.0m, 60, imperialCoPitched);
        var metricResult = BrewingCalculator.Calculate(20.0m, 75.0m, 60, metricSingleHighAtten);

        imperialResult.OriginalGravity.Should().Be(metricResult.OriginalGravity);
        imperialResult.FinalGravity.Should().Be(metricResult.FinalGravity);
        imperialResult.BitternessIbu.Should().BeApproximately(metricResult.BitternessIbu, 0.1m);
    }

    [Fact]
    public void Calculate_WithMashSteps_AdjustsAttenuationForMaltoseAndAlphaRests()
    {
        // WLP860 Munich Helles Yeast at its 70% datasheet midpoint in a 20L Munich Helles grist
        var hellesIngredients = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 4.40m,
                Unit = "kg",
                Usage = IngredientUsage.Mash,
                Ingredient = new Ingredient
                {
                    Name = "Barke Pilsner Malt",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.037m,
                    ColorSrm = 1.8m
                }
            },
            new()
            {
                Amount = 0.20m,
                Unit = "kg",
                Usage = IngredientUsage.Mash,
                Ingredient = new Ingredient
                {
                    Name = "Munich Malt (Light)",
                    Type = IngredientType.Fermentable,
                    PotentialGravity = 1.035m,
                    ColorSrm = 9.0m
                }
            },
            new()
            {
                Amount = 70.0m,
                Unit = "ml",
                Usage = IngredientUsage.Primary,
                Ingredient = new Ingredient
                {
                    Name = "Munich Helles Yeast WLP860",
                    Type = IngredientType.Yeast,
                    AttenuationPercent = 70.0m
                }
            }
        };

        var standard67C = BrewingCalculator.Calculate(
            20.0m,
            72.0m,
            75,
            hellesIngredients,
            new BrewingCalculationOptions(MashSteps:
            [
                new MashStepTempInput(67.0m, 60, 1),
                new MashStepTempInput(76.0m, 10, 2)
            ]));

        var hochkurz645C = BrewingCalculator.Calculate(
            20.0m,
            72.0m,
            75,
            hellesIngredients,
            new BrewingCalculationOptions(MashSteps:
            [
                new MashStepTempInput(64.5m, 45, 1),
                new MashStepTempInput(71.0m, 25, 2),
                new MashStepTempInput(76.0m, 10, 3)
            ]));

        var dextrinous70C = BrewingCalculator.Calculate(
            20.0m,
            72.0m,
            75,
            hellesIngredients,
            new BrewingCalculationOptions(MashSteps:
            [
                new MashStepTempInput(70.0m, 60, 1)
            ]));

        // 64.5°C maltose rest adds +6.25% attenuation (70% -> 76.25%), yielding FG 1.012 and ~5.09% ABV
        hochkurz645C.FinalGravity.Should().Be(1.012m);
        hochkurz645C.AlcoholByVolume.Should().BeInRange(5.05m, 5.20m);
        hochkurz645C.FinalGravity.Should().BeLessThan(standard67C.FinalGravity);
        dextrinous70C.FinalGravity.Should().BeGreaterThan(standard67C.FinalGravity);
    }
}