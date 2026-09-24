using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentAssertions;

namespace BrewYou.ApiService.Tests;

public class VolumeCalculatorTests
{
    // 1. CalculateWaterRequirements

    [Fact]
    public void CalculateWaterRequirements_StandardBatch_ComputesCorrectVolumes()
    {
        // Arrange
        var input = new VolumeCalculationInput(
            TargetBatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            TotalGrainWeightKg: 4.5m,
            BoilOffRatePerHour: 3.0m,
            GrainAbsorptionRate: 0.96m,
            KettleTrubLossLiters: 1.5m,
            FermenterLossLiters: 1.5m,
            MashTunDeadSpaceLiters: 0.0m,
            CoolingShrinkagePercent: 4.0m,
            PackagingLossLiters: 0.5m,
            MashThicknessLitersPerKg: 3.0m
        );

        // Act
        var result = VolumeCalculator.CalculateWaterRequirements(input);

        // Assert
        // Using explicit expected values to match math:
        // Packaged: 20 - 1.5 = 18.5
        // Post-boil cold: 20 + 1.5 = 21.5
        // Post-boil hot: 21.5 / 0.96 = ~22.40
        // Boil off: 3.0
        // Pre-boil hot: 25.40
        // Pre-boil cold: 25.40 * 0.96 = ~24.38
        // Grain absorption: 4.5 * 0.96 = 4.32
        // Total water: 24.38 + 4.32 = 28.70
        // Strike: 4.5 * 3.0 = 13.5
        // Sparge: 28.70 - 13.5 = 15.2

        result.TargetPreBoilVolumeLiters.Should().BeApproximately(24.38m, 0.05m);
        result.TotalWaterLiters.Should().BeApproximately(28.70m, 0.05m);
        result.StrikeWaterLiters.Should().Be(13.5m);
        result.SpargeWaterLiters.Should().BeApproximately(15.20m, 0.05m);
    }

    [Fact]
    public void CalculateWaterRequirements_PackagedBasis_WorksBackwardsToDetermineFermenterVolume()
    {
        // Arrange
        var input = new VolumeCalculationInput(
            TargetBatchSizeLiters: 19.0m, // 19L Cornelius keg
            BoilTimeMinutes: 60,
            TotalGrainWeightKg: 4.5m,
            TargetBasis: TargetVolumeBasis.Packaged,
            FermenterLossLiters: 1.5m,
            PackagingLossLiters: 0.5m,
            KettleTrubLossLiters: 1.5m
        );

        // Act
        var result = VolumeCalculator.CalculateWaterRequirements(input);

        // Assert
        result.TargetPackagedVolumeLiters.Should().Be(19.0m);
        result.TargetFermenterVolumeLiters.Should().Be(21.0m); // 19.0 + 1.5 + 0.5
        result.FermenterLossLiters.Should().Be(1.5m);
        result.PackagingLossLiters.Should().Be(0.5m);
        result.TargetPostBoilVolumeLiters.Should().Be(22.5m); // 21.0 + 1.5 kettle trub
    }

    [Fact]
    public void CalculateWaterRequirements_FermenterBasis_YieldsReducedPackagedVolume()
    {
        // Arrange
        var input = new VolumeCalculationInput(
            TargetBatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            TotalGrainWeightKg: 4.5m,
            TargetBasis: TargetVolumeBasis.Fermenter,
            FermenterLossLiters: 1.5m
        );

        // Act
        var result = VolumeCalculator.CalculateWaterRequirements(input);

        // Assert
        result.TargetFermenterVolumeLiters.Should().Be(20.0m);
        result.TargetPackagedVolumeLiters.Should().Be(18.5m); // 20.0 - 1.5
    }

    [Fact]
    public void CalculateWaterRequirements_BiabNoSparge_ReturnsZeroSparge()
    {
        // Arrange
        var input = new VolumeCalculationInput(
            TargetBatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            TotalGrainWeightKg: 4.5m,
            MashThicknessLitersPerKg: 7.0m // High thickness for BIAB
        );

        // Act
        var result = VolumeCalculator.CalculateWaterRequirements(input);

        // Assert
        result.SpargeWaterLiters.Should().Be(0.0m);
        result.StrikeWaterLiters.Should().Be(31.5m); // 4.5kg * 7.0 L/kg
    }

    [Fact]
    public void CalculateWaterRequirements_ZeroGrain_CalculatesCorrectVolumesWithoutAbsorption()
    {
        // Arrange
        var input = new VolumeCalculationInput(
            TargetBatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            TotalGrainWeightKg: 0.0m,
            BoilOffRatePerHour: 3.0m
        );

        // Act
        var result = VolumeCalculator.CalculateWaterRequirements(input);

        // Assert
        result.GrainAbsorptionLossLiters.Should().Be(0.0m);
        result.TotalWaterLiters.Should().Be(result.TargetPreBoilVolumeLiters + result.MashTunDeadSpaceLossLiters);
    }

    [Fact]
    public void CalculateWaterRequirements_NinetyMinuteBoil_RequiresMoreWaterThanSixtyMinute()
    {
        // Arrange
        var input60 = new VolumeCalculationInput(TargetBatchSizeLiters: 20.0m, BoilTimeMinutes: 60, TotalGrainWeightKg: 4.5m);
        var input90 = new VolumeCalculationInput(TargetBatchSizeLiters: 20.0m, BoilTimeMinutes: 90, TotalGrainWeightKg: 4.5m);

        // Act
        var result60 = VolumeCalculator.CalculateWaterRequirements(input60);
        var result90 = VolumeCalculator.CalculateWaterRequirements(input90);

        // Assert
        result90.BoilOffLossLiters.Should().BeGreaterThan(result60.BoilOffLossLiters);
        result90.TotalWaterLiters.Should().BeGreaterThan(result60.TotalWaterLiters);
    }

    [Fact]
    public void CalculateWaterRequirements_NoLosses_TotalWaterApproximatesBatchSizePlusBoilOff()
    {
        // Arrange
        var input = new VolumeCalculationInput(
            TargetBatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            TotalGrainWeightKg: 0.0m, // No grain absorption
            BoilOffRatePerHour: 3.0m,
            KettleTrubLossLiters: 0.0m,
            FermenterLossLiters: 0.0m,
            MashTunDeadSpaceLiters: 0.0m,
            CoolingShrinkagePercent: 0.0m, // No shrinkage
            PackagingLossLiters: 0.0m
        );

        // Act
        var result = VolumeCalculator.CalculateWaterRequirements(input);

        // Assert
        result.TotalWaterLiters.Should().Be(23.0m); // 20L batch + 3L boil off
    }

    [Fact]
    public void CalculateWaterRequirements_HighLosses_RequiresSignificantlyMoreWater()
    {
        // Arrange
        var standardInput = new VolumeCalculationInput(TargetBatchSizeLiters: 20.0m, BoilTimeMinutes: 60, TotalGrainWeightKg: 4.5m);
        var highLossInput = new VolumeCalculationInput(
            TargetBatchSizeLiters: 20.0m,
            BoilTimeMinutes: 60,
            TotalGrainWeightKg: 4.5m,
            BoilOffRatePerHour: 4.5m, // 1.5L more than standard 3.0L
            KettleTrubLossLiters: 5.0m, // 3.5L more than standard 1.5L
            FermenterLossLiters: 5.0m,
            MashTunDeadSpaceLiters: 1.5m // 1.5L more than standard 0.0L
        );

        // Act
        var standardResult = VolumeCalculator.CalculateWaterRequirements(standardInput);
        var highLossResult = VolumeCalculator.CalculateWaterRequirements(highLossInput);

        // Assert
        highLossResult.TotalWaterLiters.Should().BeGreaterThan(standardResult.TotalWaterLiters + 3.5m);
    }

    // 2. CalculateExpectedPostBoilVolume

    [Fact]
    public void CalculateExpectedPostBoilVolume_StandardCase_ComputesCorrectly()
    {
        // Act
        var result = VolumeCalculator.CalculateExpectedPostBoilVolume(
            measuredPreBoilVolumeLiters: 28.0m,
            boilOffRatePerHour: 3.5m,
            boilTimeMinutes: 60,
            coolingShrinkagePercent: 4.0m
        );

        // Assert
        // shrinkage = 0.96
        // preBoilHot = 28 / 0.96 = 29.166
        // boilOff = 3.5
        // postBoilHot = 29.166 - 3.5 = 25.666
        // postBoilCold = 25.666 * 0.96 = 24.64
        result.Should().BeApproximately(24.64m, 0.05m);
    }

    [Fact]
    public void CalculateExpectedPostBoilVolume_ZeroBoilTime_PostBoilEqualsPreBoil()
    {
        // Act
        var result = VolumeCalculator.CalculateExpectedPostBoilVolume(28.0m, 3.5m, 0, 4.0m);

        // Assert
        result.Should().Be(28.0m);
    }

    [Fact]
    public void CalculateExpectedPostBoilVolume_ZeroShrinkageFactor_ReturnsZero()
    {
        // Act
        var result = VolumeCalculator.CalculateExpectedPostBoilVolume(28.0m, 3.5m, 60, 100.0m);

        // Assert
        result.Should().Be(0.0m);
    }

    // 3. CalculateExpectedOg

    [Fact]
    public void CalculateExpectedOg_StandardCase_CalculatesHigherGravity()
    {
        // Act
        var result = VolumeCalculator.CalculateExpectedOg(28.0m, 1.040m, 21.5m);

        // Assert
        // OG = 1.0 + (28 * 0.040 / 21.5) = 1.05209...
        result.Should().BeApproximately(1.052m, 0.001m);
    }

    [Fact]
    public void CalculateExpectedOg_ZeroPostBoilVolume_ReturnsZero()
    {
        var result = VolumeCalculator.CalculateExpectedOg(28.0m, 1.040m, 0.0m);
        result.Should().Be(0.0m);
    }

    [Fact]
    public void CalculateExpectedOg_PreBoilGravityBelowOne_ReturnsZero()
    {
        var result = VolumeCalculator.CalculateExpectedOg(28.0m, 0.999m, 21.5m);
        result.Should().Be(0.0m);
    }

    // 4. CalculateBoilTimeAdjustment

    [Fact]
    public void CalculateBoilTimeAdjustment_VolumeOverTarget_CalculatesExtraMinutes()
    {
        // Arrange
        var actual = 31.0m;
        var target = 28.0m;
        var boilOffRate = 3.5m;

        // Act
        var result = VolumeCalculator.CalculateBoilTimeAdjustment(actual, target, boilOffRate);

        // Assert
        // excess = 3.0. hours = 3.0/3.5 = 0.857. mins = 51.4 -> 51
        result.Should().Be(51);
    }

    [Theory]
    [InlineData(28.0, 28.0, 3.5)] // At target
    [InlineData(27.0, 28.0, 3.5)] // Below target
    public void CalculateBoilTimeAdjustment_VolumeAtOrBelowTarget_ReturnsZero(double actual, double target, double rate)
    {
        var result = VolumeCalculator.CalculateBoilTimeAdjustment((decimal)actual, (decimal)target, (decimal)rate);
        result.Should().Be(0);
    }

    [Fact]
    public void CalculateBoilTimeAdjustment_ZeroBoilOffRate_ReturnsZero()
    {
        var result = VolumeCalculator.CalculateBoilTimeAdjustment(31.0m, 28.0m, 0.0m);
        result.Should().Be(0);
    }

    // 5. CalculateActualBoilOffRate

    [Fact]
    public void CalculateActualBoilOffRate_StandardCase_CalculatesRatePerHour()
    {
        // Act
        var result = VolumeCalculator.CalculateActualBoilOffRate(28.0m, 24.5m, 60);

        // Assert
        result.Should().Be(3.5m);
    }

    [Fact]
    public void CalculateActualBoilOffRate_ZeroBoilTime_ReturnsZero()
    {
        var result = VolumeCalculator.CalculateActualBoilOffRate(28.0m, 24.5m, 0);
        result.Should().Be(0.0m);
    }

    [Theory]
    [InlineData(28.0, 28.0, 60)] // Equal
    [InlineData(28.0, 29.0, 60)] // Post > Pre
    public void CalculateActualBoilOffRate_PostBoilGreaterOrEqual_ReturnsZero(double pre, double post, int mins)
    {
        var result = VolumeCalculator.CalculateActualBoilOffRate((decimal)pre, (decimal)post, mins);
        result.Should().Be(0.0m);
    }

    // 6. CalculateActualGrainAbsorption

    [Fact]
    public void CalculateActualGrainAbsorption_StandardCase_CalculatesRate()
    {
        // Act
        var result = VolumeCalculator.CalculateActualGrainAbsorption(
            totalWaterUsedLiters: 33.85m,
            preBoilVolumeLiters: 28.57m,
            mashTunDeadSpaceLiters: 1.0m,
            totalGrainWeightKg: 4.5m
        );

        // Assert
        // 33.85 - 28.57 - 1.0 = 4.28
        // 4.28 / 4.5 = 0.951...
        result.Should().BeApproximately(0.95m, 0.01m);
    }

    [Fact]
    public void CalculateActualGrainAbsorption_ZeroGrain_ReturnsZero()
    {
        var result = VolumeCalculator.CalculateActualGrainAbsorption(33.85m, 28.57m, 1.0m, 0.0m);
        result.Should().Be(0.0m);
    }

    // 7. CalculateMashEfficiency

    [Fact]
    public void CalculateMashEfficiency_StandardCase_ComputesPercentage()
    {
        // Arrange
        var preBoilVolume = 28.0m;
        var preBoilGravity = 1.040m;
        var potentialPoints = 350.0m;

        // Act
        var result = VolumeCalculator.CalculateMashEfficiency(preBoilVolume, preBoilGravity, potentialPoints);

        // Assert
        // volumeGallons = 28 * 0.264172052 = 7.3968
        // points = 40 * 7.3968 = 295.872
        // efficiency = 295.872 / 350 = 84.53%
        result.Should().BeApproximately(84.5m, 0.1m);
    }

    [Fact]
    public void CalculateMashEfficiency_ZeroPotentialPoints_ReturnsZero()
    {
        var result = VolumeCalculator.CalculateMashEfficiency(28.0m, 1.040m, 0.0m);
        result.Should().Be(0.0m);
    }

    [Fact]
    public void CalculateMashEfficiency_GravityAtOrBelowOne_ReturnsZero()
    {
        var result = VolumeCalculator.CalculateMashEfficiency(28.0m, 1.000m, 350.0m);
        result.Should().Be(0.0m);
    }

    // 8. RecalculatePostStepMilestones

    [Fact]
    public void RecalculatePostStepMilestones_WhenPreBoilVolumeEdited_UpdatesPostBoilFermenterAndPackagedTargets()
    {
        // Arrange: Initial profile before brew day
        var profile = new BatchVolumeProfile
        {
            TargetPreBoilVolumeLiters = 27.0m,
            TargetPostBoilVolumeLiters = 23.0m,
            TargetFermenterVolumeLiters = 21.0m,
            TargetPackagedVolumeLiters = 19.0m,
            BoilOffRatePerHour = 4.0m,
            CoolingShrinkagePercent = 4.0m,
            KettleTrubLossLiters = 2.0m,
            FermenterTrubLossLiters = 1.5m,
            PackagingLossLiters = 0.5m
        };

        // Act: User measures lower pre-boil volume (25.0L instead of 27.0L)
        profile.MeasuredPreBoilVolumeLiters = 25.0m;
        VolumeCalculator.RecalculatePostStepMilestones(profile, boilTimeMinutes: 60);

        // Assert:
        // Expected Post-Boil:
        // preBoilHot = 25.0 / 0.96 = 26.04166...
        // postBoilHot = 26.04166... - 4.0 = 22.04166...
        // postBoilCold = 22.04166... * 0.96 = 21.16L
        profile.TargetPostBoilVolumeLiters.Should().Be(21.16m);

        // Expected Fermenter: 21.16 - 2.0 (kettle trub) = 19.16L
        profile.TargetFermenterVolumeLiters.Should().Be(19.16m);

        // Expected Packaged: 19.16 - 2.0 (fermenter + packaging loss) = 17.16L
        profile.TargetPackagedVolumeLiters.Should().Be(17.16m);
    }

    [Fact]
    public void RecalculatePostStepMilestones_WhenPostBoilVolumeEdited_UpdatesFermenterAndPackagedTargets()
    {
        // Arrange
        var profile = new BatchVolumeProfile
        {
            TargetPreBoilVolumeLiters = 27.0m,
            MeasuredPreBoilVolumeLiters = 25.0m,
            TargetPostBoilVolumeLiters = 21.16m,
            TargetFermenterVolumeLiters = 19.16m,
            TargetPackagedVolumeLiters = 17.16m,
            BoilOffRatePerHour = 4.0m,
            CoolingShrinkagePercent = 4.0m,
            KettleTrubLossLiters = 2.0m,
            FermenterTrubLossLiters = 1.5m,
            PackagingLossLiters = 0.5m
        };

        // Act: Brewer measures 22.0L post boil (less boil-off than anticipated)
        profile.MeasuredPostBoilVolumeLiters = 22.0m;
        VolumeCalculator.RecalculatePostStepMilestones(profile, boilTimeMinutes: 60);

        // Assert:
        // Fermenter target derived from measured post-boil: 22.0 - 2.0 = 20.0L
        profile.TargetFermenterVolumeLiters.Should().Be(20.0m);

        // Packaged target derived from updated fermenter target: 20.0 - 2.0 = 18.0L
        profile.TargetPackagedVolumeLiters.Should().Be(18.0m);
    }

    [Fact]
    public void RecalculatePostStepMilestones_WhenFermenterVolumeEdited_UpdatesPackagedTarget()
    {
        // Arrange
        var profile = new BatchVolumeProfile
        {
            TargetPreBoilVolumeLiters = 27.0m,
            MeasuredPreBoilVolumeLiters = 25.0m,
            MeasuredPostBoilVolumeLiters = 22.0m,
            TargetPostBoilVolumeLiters = 21.16m,
            TargetFermenterVolumeLiters = 20.0m,
            TargetPackagedVolumeLiters = 18.0m,
            BoilOffRatePerHour = 4.0m,
            CoolingShrinkagePercent = 4.0m,
            KettleTrubLossLiters = 2.0m,
            FermenterTrubLossLiters = 1.5m,
            PackagingLossLiters = 0.5m
        };

        // Act: Actual volume into fermenter is measured as 19.5L
        profile.MeasuredFermenterVolumeLiters = 19.5m;
        VolumeCalculator.RecalculatePostStepMilestones(profile, boilTimeMinutes: 60);

        // Assert:
        // Packaged target derived from actual fermenter measurement: 19.5 - 2.0 = 17.5L
        profile.TargetPackagedVolumeLiters.Should().Be(17.5m);
    }
}