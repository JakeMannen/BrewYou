using BrewYou.ApiService.Services;
using FluentAssertions;

namespace BrewYou.ApiService.Tests;

public class YeastPitchCalculatorTests
{
    [Fact]
    public void Calculate_ComputesStandardAleAndLagerPitchTargets()
    {
        // 20 L of 1.050 (~12.4°P) Ale at 0.75M/mL/°P -> ~186B cells
        var alePlan = YeastPitchCalculator.Calculate(new YeastPitchCalculationInput(
            BatchVolumeLiters: 20m,
            OriginalGravity: 1.050m,
            StyleCategory: "Ale",
            YeastForm: "Liquid",
            PackageAgeDays: 0m,
            BaseViabilityPercent: 100m,
            StarterMethod: "StirPlate"));

        alePlan.Plato.Should().BeApproximately(12.39m, 0.1m);
        alePlan.TargetPitchRateMillionPerMlPlato.Should().Be(0.75m);
        alePlan.RequiredCellsBillions.Should().BeApproximately(185.9m, 2m);
        alePlan.RecommendedStarterVolumeLiters.Should().BeGreaterThan(0.5m);
        alePlan.StarterDmeGrams.Should().Be(alePlan.RecommendedStarterVolumeLiters * 100m);

        // Same wort as Lager -> 1.50M/mL/°P -> double cells (~372B)
        var lagerPlan = YeastPitchCalculator.Calculate(new YeastPitchCalculationInput(
            BatchVolumeLiters: 20m,
            OriginalGravity: 1.050m,
            StyleCategory: "Lager",
            YeastForm: "Dry",
            PackageAgeDays: 0m));

        lagerPlan.TargetPitchRateMillionPerMlPlato.Should().Be(1.50m);
        lagerPlan.RequiredCellsBillions.Should().BeApproximately(alePlan.RequiredCellsBillions * 2m, 2m);
        lagerPlan.DryPacksRecommended.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void ViabilityDecay_LiquidLosesAbout20PercentPerMonth()
    {
        var day0 = YeastPitchCalculator.CalculateViabilityPercent("Liquid", 0m, 100m);
        var day30 = YeastPitchCalculator.CalculateViabilityPercent("Liquid", 30m, 100m);
        var day90 = YeastPitchCalculator.CalculateViabilityPercent("Liquid", 90m, 100m);

        day0.Should().Be(100m);
        day30.Should().Be(80m);
        day90.Should().Be(40m);

        var dryDay60 = YeastPitchCalculator.CalculateViabilityPercent("Dry", 60m, 100m);
        dryDay60.Should().Be(96m);
    }

    [Fact]
    public void StirPlateProducesHigherStarterYieldThanSimpleStarter()
    {
        var stir = YeastPitchCalculator.Calculate(new YeastPitchCalculationInput(
            20m, 1.060m, "Ale", null, "Liquid", 30m, 100m, null, 1, null, null, "StirPlate", 1.5m));
        var simple = YeastPitchCalculator.Calculate(new YeastPitchCalculationInput(
            20m, 1.060m, "Ale", null, "Liquid", 30m, 100m, null, 1, null, null, "Simple", 1.5m));

        stir.ProjectedStarterCellsBillions.Should().BeGreaterThan(simple.ProjectedStarterCellsBillions);
    }
}