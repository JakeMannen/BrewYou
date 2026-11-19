using BrewYou.ApiService.Services;
using FluentAssertions;

namespace BrewYou.ApiService.Tests;

public class WaterChemistryCalculatorTests
{
    [Fact]
    public void GypsumAndCaCl2Additions_IncreaseExpectedMineralsPerLiter()
    {
        var ro = new WaterIonProfile(0m, 0m, 0m, 0m, 0m, 0m);
        // In 10 L: 1 g gypsum -> +23.28 ppm Ca, +55.77 ppm SO4; 1 g CaCl2 -> +27.26 ppm Ca, +48.23 ppm Cl
        var additions = new SaltAdditionsInput(GypsumGrams: 1m, CalciumChlorideGrams: 1m);

        var result = WaterChemistryCalculator.CalculateWaterIonsAfterAdditions(ro, additions, 10m);

        result.CalciumPpm.Should().BeApproximately(50.5m, 0.2m);
        result.SulfatePpm.Should().BeApproximately(55.8m, 0.2m);
        result.ChloridePpm.Should().BeApproximately(48.2m, 0.2m);
    }

    [Fact]
    public void KolbachResidualAlkalinity_CalculatesAccurately()
    {
        // Pilsen-like soft water: Ca 7, Mg 2, HCO3 15 => Alk ~12.3, RA ~ 12.3 - (4.99 + 1.18) ~ +6.1 ppm as CaCO3
        var pilsen = new WaterIonProfile(7m, 2m, 2m, 5m, 5m, 15m);
        var ra = WaterChemistryCalculator.CalculateResidualAlkalinity(pilsen);
        ra.Should().BeApproximately(6.1m, 0.5m);

        // Burton-like hard water with elevated Ca/Mg vs moderate alkalinity lowers RA
        var highCa = new WaterIonProfile(200m, 20m, 15m, 400m, 35m, 60m);
        var highCaRa = WaterChemistryCalculator.CalculateResidualAlkalinity(highCa);
        highCaRa.Should().BeLessThan(-80m);
    }

    [Fact]
    public void SulfateToChlorideRatio_ClassifiesBalanceProfiles()
    {
        WaterChemistryCalculator.CalculateSulfateToChlorideRatio(250m, 50m).Should().Be(5.0m);
        WaterChemistryCalculator.ClassifySulfateChlorideBalance(5.0m, 250m, 50m).Should().Be("VeryBitter");
        WaterChemistryCalculator.ClassifySulfateChlorideBalance(1.1m, 80m, 72m).Should().Be("Balanced");
        WaterChemistryCalculator.ClassifySulfateChlorideBalance(0.45m, 75m, 165m).Should().Be("Malty");
    }

    [Fact]
    public void PredictMashPh_DropsWithAcidAndDarkMalt_AndSuggestsSalts()
    {
        var alkalineSource = new WaterIonProfile(40m, 10m, 20m, 20m, 25m, 180m);
        var paleGrist = new List<GristGrainInput>
        {
            new(4.5m, 2.0m, "Pilsner Malt"),
            new(0.3m, 40m, "Caramunich II")
        };

        var untreatedPh = WaterChemistryCalculator.PredictMashPh(alkalineSource, new SaltAdditionsInput(), 16m, paleGrist);
        var acidifiedPh = WaterChemistryCalculator.PredictMashPh(alkalineSource, new SaltAdditionsInput(AcidType: "Lactic80", AcidAmountMlOrG: 5m), 16m, paleGrist);

        untreatedPh.Should().BeGreaterThan(5.60m);
        acidifiedPh.Should().BeLessThan(untreatedPh);

        var targetIpa = new WaterIonProfile(110m, 10m, 15m, 250m, 55m, 35m);
        var suggestion = WaterChemistryCalculator.SuggestSaltAdditions(
            alkalineSource,
            targetIpa,
            mashVolumeLiters: 16m,
            spargeVolumeLiters: 14m,
            paleGrist,
            targetMashPh: 5.40m,
            acidType: "Lactic80");

        suggestion.MashAdditions.GypsumGrams.Should().BeGreaterThan(0m);
        suggestion.MashAdditions.AcidAmountMlOrG.Should().BeGreaterThan(0m);
        suggestion.Preview.PredictedMashPh.Should().BeApproximately(5.40m, 0.15m);
        suggestion.Preview.TotalResultingIons.SulfatePpm.Should().BeApproximately(250m, 15m);
    }
}