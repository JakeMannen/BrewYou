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

    [Fact]
    public void Calculate_WithSugarAndDryExtractCategories_Uses100PercentFermentabilityForSugarAndYeastAttenuationForExtract()
    {
        var baseMalt = new Ingredient
        {
            Name = "Pilsner Malt",
            Type = IngredientType.Fermentable,
            FermentableCategory = FermentableCategory.Grain,
            PotentialGravity = 1.037m,
            ColorSrm = 1.8m
        };
        var yeast = new Ingredient
        {
            Name = "Abbey Ale Yeast",
            Type = IngredientType.Yeast,
            AttenuationPercent = 78.0m
        };
        var tripelSugar = new Ingredient
        {
            Name = "Candi Sugar (Clear)",
            Type = IngredientType.Fermentable,
            FermentableCategory = FermentableCategory.Sugar,
            PotentialGravity = 1.038m,
            ColorSrm = 0.5m
        };
        var customNameExtract = new Ingredient
        {
            Name = "Custom Golden Powder",
            Type = IngredientType.Fermentable,
            FermentableCategory = FermentableCategory.DryExtract,
            PotentialGravity = 1.044m,
            ColorSrm = 3.0m
        };

        var maltPlusSugar = new List<RecipeIngredient>
        {
            new() { Amount = 4.0m, Unit = "kg", Usage = IngredientUsage.Mash, Ingredient = baseMalt },
            new() { Amount = 1.0m, Unit = "kg", Usage = IngredientUsage.Boil, Ingredient = tripelSugar },
            new() { Amount = 11.5m, Unit = "g", Usage = IngredientUsage.Primary, Ingredient = yeast }
        };

        var maltPlusExtract = new List<RecipeIngredient>
        {
            new() { Amount = 4.0m, Unit = "kg", Usage = IngredientUsage.Mash, Ingredient = baseMalt },
            new() { Amount = 1.0m, Unit = "kg", Usage = IngredientUsage.Boil, Ingredient = customNameExtract },
            new() { Amount = 11.5m, Unit = "g", Usage = IngredientUsage.Primary, Ingredient = yeast }
        };

        // Explicit Sugar / DryExtract categories ignore mash efficiency even with a generic name
        var res60 = BrewingCalculator.Calculate(20.0m, 60.0m, 60, maltPlusExtract);
        var res85 = BrewingCalculator.Calculate(20.0m, 85.0m, 60, maltPlusExtract);
        var maltOnly60 = BrewingCalculator.Calculate(20.0m, 60.0m, 60, [maltPlusExtract[0], maltPlusExtract[2]]);
        var maltOnly85 = BrewingCalculator.Calculate(20.0m, 85.0m, 60, [maltPlusExtract[0], maltPlusExtract[2]]);

        (res60.OriginalGravity - maltOnly60.OriginalGravity).Should()
            .BeApproximately(res85.OriginalGravity - maltOnly85.OriginalGravity, 0.001m);

        // 100% fermentable Sugar addition leaves fewer unfermentable residual points than an Extract addition at 78% yeast attenuation
        var sugarRes = BrewingCalculator.Calculate(20.0m, 75.0m, 60, maltPlusSugar);
        var maltOnly75 = BrewingCalculator.Calculate(20.0m, 75.0m, 60, [maltPlusSugar[0], maltPlusSugar[2]]);
        sugarRes.OriginalGravity.Should().BeGreaterThan(maltOnly75.OriginalGravity);
        sugarRes.FinalGravity.Should().Be(maltOnly75.FinalGravity);
        sugarRes.AlcoholByVolume.Should().BeGreaterThan(maltOnly75.AlcoholByVolume);

        // Extract addition DOES contribute unfermentable residual extract to FG
        var extractRes = BrewingCalculator.Calculate(20.0m, 75.0m, 60, maltPlusExtract);
        extractRes.FinalGravity.Should().BeGreaterThan(maltOnly75.FinalGravity);
    }

    [Fact]
    public void Calculate_WithBottlingPrimingSugar_ExcludesFromOgFgAbvAndColor()
    {
        var baseList = new List<RecipeIngredient>
        {
            new()
            {
                Amount = 4.5m,
                Unit = "kg",
                Usage = IngredientUsage.Mash,
                Ingredient = new Ingredient { Name = "Pale Malt", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Grain, PotentialGravity = 1.037m, ColorSrm = 2.5m }
            },
            new()
            {
                Amount = 11.5m,
                Unit = "g",
                Usage = IngredientUsage.Primary,
                Ingredient = new Ingredient { Name = "US-05", Type = IngredientType.Yeast, AttenuationPercent = 78.0m }
            }
        };

        var withPrimingSugar = new List<RecipeIngredient>(baseList)
        {
            new()
            {
                Amount = 0.14m,
                Unit = "kg",
                Usage = IngredientUsage.Bottling,
                Ingredient = new Ingredient { Name = "Priming Dextrose", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Sugar, PotentialGravity = 1.046m, ColorSrm = 5.0m }
            }
        };

        var baseline = BrewingCalculator.Calculate(20.0m, 75.0m, 60, baseList);
        var withBottling = BrewingCalculator.Calculate(20.0m, 75.0m, 60, withPrimingSugar);

        withBottling.Should().Be(baseline);
    }

    [Fact]
    public void Calculate_WithExplicitFermentabilityPercentOverride_AppliesPartialFermentability()
    {
        var malt = new Ingredient
        {
            Name = "Pilsner Malt",
            Type = IngredientType.Fermentable,
            FermentableCategory = FermentableCategory.Grain,
            PotentialGravity = 1.037m,
            ColorSrm = 2.0m
        };
        var honeyMust = new Ingredient
        {
            Name = "Wildflower Honey",
            Type = IngredientType.Fermentable,
            FermentableCategory = FermentableCategory.Sugar,
            PotentialGravity = 1.035m,
            ColorSrm = 1.5m,
            FermentabilityPercent = 50.0m // 50% fermentable override
        };
        var yeast = new Ingredient
        {
            Name = "Saison Yeast",
            Type = IngredientType.Yeast,
            AttenuationPercent = 80.0m
        };

        var baseline = BrewingCalculator.Calculate(20.0m, 75.0m, 60,
        [
            new() { Amount = 4.0m, Unit = "kg", Usage = IngredientUsage.Mash, Ingredient = malt },
            new() { Amount = 11.5m, Unit = "g", Usage = IngredientUsage.Primary, Ingredient = yeast }
        ]);

        var withHalfFermentableHoney = BrewingCalculator.Calculate(20.0m, 75.0m, 60,
        [
            new() { Amount = 4.0m, Unit = "kg", Usage = IngredientUsage.Mash, Ingredient = malt },
            new() { Amount = 1.0m, Unit = "kg", Usage = IngredientUsage.Boil, Ingredient = honeyMust },
            new() { Amount = 11.5m, Unit = "g", Usage = IngredientUsage.Primary, Ingredient = yeast }
        ]);

        var addedPoints = withHalfFermentableHoney.OriginalGravity - baseline.OriginalGravity;
        var addedFgPoints = withHalfFermentableHoney.FinalGravity - baseline.FinalGravity;

        addedPoints.Should().BeGreaterThan(0.012m);
        // Roughly 50% of added points remain in FG
        addedFgPoints.Should().BeApproximately(addedPoints * 0.50m, 0.002m);
    }

    [Fact]
    public void CalculateMashGrainWeightKg_ExcludesSugarsExtractsAndNonMashAdditions()
    {
        var recipeIngredients = new List<RecipeIngredient>
        {
            new() { Amount = 4.0m, Unit = "kg", Usage = IngredientUsage.Mash, Ingredient = new Ingredient { Name = "Pilsner", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Grain } },
            new() { Amount = 500m, Unit = "g", Usage = IngredientUsage.Mash, Ingredient = new Ingredient { Name = "Flaked Oats", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Adjunct } },
            new() { Amount = 1.0m, Unit = "kg", Usage = IngredientUsage.Boil, Ingredient = new Ingredient { Name = "Candi Sugar", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Sugar } },
            new() { Amount = 1.5m, Unit = "kg", Usage = IngredientUsage.Mash, Ingredient = new Ingredient { Name = "Liquid Malt Extract", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Extract } },
            new() { Amount = 0.5m, Unit = "kg", Usage = IngredientUsage.Boil, Ingredient = new Ingredient { Name = "Caramel 60L", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Grain } },
            new() { Amount = 150m, Unit = "g", Usage = IngredientUsage.Bottling, Ingredient = new Ingredient { Name = "Dextrose", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Sugar } },
            new() { Amount = 30m, Unit = "g", Usage = IngredientUsage.Boil, Ingredient = new Ingredient { Name = "Hallertau", Type = IngredientType.Hop } }
        };

        BrewingCalculator.CalculateMashGrainWeightKg(recipeIngredients).Should().BeApproximately(4.5m, 0.001m);

        var batchIngredients = new List<BatchIngredient>
        {
            new() { Name = "Pilsner", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Grain, AdditionStage = IngredientUsage.Mash, Amount = 4.0m, Unit = "kg" },
            new() { Name = "Flaked Wheat", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Adjunct, AdditionStage = IngredientUsage.Mash, Amount = 0.5m, Unit = "kg" },
            new() { Name = "Maple Syrup", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.Sugar, AdditionStage = IngredientUsage.Boil, Amount = 1.0m, Unit = "kg" },
            new() { Name = "Dry Malt Extract", Type = IngredientType.Fermentable, FermentableCategory = FermentableCategory.DryExtract, AdditionStage = IngredientUsage.Boil, Amount = 0.5m, Unit = "kg" }
        };

        BrewingCalculator.CalculateMashGrainWeightKg(batchIngredients).Should().BeApproximately(4.5m, 0.001m);
    }

    [Fact]
    public void CorrectHydrometerReading_WarmSample_CorrectsToHigherGravity()
    {
        // 1.050 SG measured at 50°C with hydrometer calibrated at 20°C
        var corrected = BrewingCalculator.CorrectHydrometerReading(1.050m, 50.0m, 20.0m);

        corrected.Should().BeGreaterThan(1.058m);
        corrected.Should().BeApproximately(1.0607m, 0.001m);
    }

    [Fact]
    public void CorrectHydrometerReading_ColdSample_CorrectsToLowerGravity()
    {
        // 1.050 SG measured at 10°C with hydrometer calibrated at 20°C
        var corrected = BrewingCalculator.CorrectHydrometerReading(1.050m, 10.0m, 20.0m);

        corrected.Should().BeLessThan(1.050m);
        corrected.Should().BeApproximately(1.0483m, 0.001m);
    }

    [Fact]
    public void CorrectHydrometerReading_AtCalibrationTemp_ReturnsUnchanged()
    {
        var corrected = BrewingCalculator.CorrectHydrometerReading(1.050m, 20.0m, 20.0m);

        corrected.Should().Be(1.0500m);
    }

    [Fact]
    public void CorrectHydrometerReading_WithDifferentCalibrationTemp_CalculatesAccurately()
    {
        // Hydrometer calibrated at 15.55°C (60°F) measuring at 25°C
        var corrected = BrewingCalculator.CorrectHydrometerReading(1.050m, 25.0m, 15.55m);

        corrected.Should().BeGreaterThan(1.050m);
        corrected.Should().BeApproximately(1.0522m, 0.001m);
    }

    [Theory]
    [InlineData(1.050, 0.0, 20.0, 1.050)] // tempC <= 0
    [InlineData(1.050, -5.0, 20.0, 1.050)] // negative tempC
    [InlineData(1.050, 105.0, 20.0, 1.050)] // tempC > 100
    [InlineData(0.0, 25.0, 20.0, 0.0)] // rawSg <= 0
    [InlineData(-1.0, 25.0, 20.0, -1.0)] // rawSg negative
    [InlineData(1.050, 25.0, 0.0, 1.050)] // calTempC <= 0
    [InlineData(1.050, 25.0, 105.0, 1.050)] // calTempC > 100
    public void CorrectHydrometerReading_WithEdgeCases_HandlesSafely(
        double rawSg, double tempC, double calTempC, double expected)
    {
        var result = BrewingCalculator.CorrectHydrometerReading((decimal)rawSg, (decimal)tempC, (decimal)calTempC);
        result.Should().Be((decimal)expected);
    }

    [Fact]
    public void CalculateApparentAttenuation_WithValidInputs_CalculatesCorrectPercentage()
    {
        // OG 1.050, FG 1.010 -> (50 - 10) / 50 = 80.0%
        var attenuation = BrewingCalculator.CalculateApparentAttenuation(1.050m, 1.010m);

        attenuation.Should().Be(80.0m);
    }

    [Theory]
    [InlineData(1.000, 1.010)] // OG <= 1.000
    [InlineData(0.998, 0.990)] // OG <= 1.000
    [InlineData(1.050, 1.050)] // OG == FG
    [InlineData(1.040, 1.050)] // OG < FG
    public void CalculateApparentAttenuation_WithEdgeCases_ReturnsZero(double og, double fg)
    {
        var attenuation = BrewingCalculator.CalculateApparentAttenuation((decimal)og, (decimal)fg);
        attenuation.Should().Be(0.0m);
    }

    [Fact]
    public void CalculateRealAttenuation_FromApparent_AppliesAsbcFactor()
    {
        // 80.0% apparent * 0.8192 = 65.536% -> 65.5%
        var realAttenuation = BrewingCalculator.CalculateRealAttenuation(80.0m);

        realAttenuation.Should().Be(65.5m);
    }

    [Fact]
    public void CalculateRealAttenuation_FromOgAndFg_CalculatesCorrectly()
    {
        // OG 1.050, FG 1.010 -> apparent 80.0% -> real 65.5%
        var realAttenuation = BrewingCalculator.CalculateRealAttenuation(1.050m, 1.010m);

        realAttenuation.Should().Be(65.5m);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    public void CalculateRealAttenuation_WithZeroOrNegative_ReturnsZero(double apparent)
    {
        var realAttenuation = BrewingCalculator.CalculateRealAttenuation((decimal)apparent);
        realAttenuation.Should().Be(0.0m);
    }

    [Fact]
    public void CalculateActualAbv_StandardGravity_UsesLinearFormula()
    {
        // OG <= 1.060 uses linear (OG - FG) * 131.25
        // (1.050 - 1.010) * 131.25 = 5.25%
        var abv = BrewingCalculator.CalculateActualAbv(1.050m, 1.010m);

        abv.Should().Be(5.25m);
    }

    [Fact]
    public void CalculateActualAbv_HighGravity_UsesAsbcFormula()
    {
        // OG > 1.060 uses non-linear ASBC formula
        // OG = 1.070, FG = 1.015:
        // abw = (76.08 * 0.055) / (1.775 - 1.070) = 4.1844 / 0.705 = 5.935319
        // abv = 5.935319 * (1.015 / 0.794) = 7.587% -> 7.59%
        var abv = BrewingCalculator.CalculateActualAbv(1.070m, 1.015m);

        abv.Should().Be(7.59m);
    }

    [Theory]
    [InlineData(1.050, 1.050)] // OG == FG
    [InlineData(1.040, 1.050)] // OG < FG
    [InlineData(0.999, 0.990)] // OG <= 1.000
    [InlineData(1.050, 0.0)]   // FG <= 0
    [InlineData(1.800, 1.050)] // OG >= 1.775 (safely clamped)
    public void CalculateActualAbv_EdgeCases_ReturnsZero(double og, double fg)
    {
        var abv = BrewingCalculator.CalculateActualAbv((decimal)og, (decimal)fg);
        abv.Should().Be(0.0m);
    }
}