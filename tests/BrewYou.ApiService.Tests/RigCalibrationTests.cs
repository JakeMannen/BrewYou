using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class RigCalibrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RigCalibrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest($"cal_{Guid.NewGuid():N}@brewyou.test", "StrongPassword123!", "Cal Brewer", "en", VolumeUnit.Liters));
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    [Fact]
    public async Task CalibrationReport_ComputesVarianceAndAppliesToSetupAndEquipment()
    {
        var client = await CreateAuthenticatedClientAsync();

        // 1. Get default brewery setup and add a boiler + fermenter
        var setupsRes = await client.GetFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>("/api/v1/brewery-setups");
        var setup = setupsRes!.Data!.First();

        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Calibration Kettle 40L",
            Type: EquipmentType.Boiler,
            Capacity: 40m,
            Subtype: EquipmentSubtype.AllInOne,
            BoilOffRatePerHour: 3.0m,
            TrubLossLiters: 1.5m));
        boilerRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var fermRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Calibration Fermenter 30L",
            Type: EquipmentType.Fermenter,
            Capacity: 30m,
            TrubLossLiters: 1.0m));
        fermRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var fermenter = (await fermRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 2. Log two completed past batches with observed pre-boil 27L -> post-boil 23L (4.0 L/hr boil-off for 60 min)
        // and fermenter 20.0L -> packaged 18.5L (1.5L fermenter loss)
        for (var i = 1; i <= 2; i++)
        {
            var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
                RecipeId: null,
                BatchCode: $"B-CAL-{i}",
                Name: $"Calibration Brew #{i}",
                BeerStyle: "American Pale Ale",
                BrewDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10 - i)),
                TargetBatchSizeLiters: 20m,
                TargetOg: 1.052m,
                TargetFg: 1.012m,
                TargetAbv: 5.25m,
                TargetIbu: 35m,
                TargetColorSrm: 6m,
                BoilTimeMinutes: 60,
                EfficiencyPercent: 72m,
                BoilerId: boiler.Id,
                FermenterId: fermenter.Id,
                MeasuredOg: 1.055m));
            batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
            var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

            var updateRes = await client.PutAsJsonAsync($"/api/v1/batches/{batch.Id}", new UpdateBatchRequest(
                MeasuredPreBoilVolumeLiters: 27.0m,
                MeasuredPreBoilGravity: 1.045m,
                MeasuredPostBoilVolumeLiters: 23.0m,
                MeasuredBatchSizeLiters: 20.0m,
                MeasuredFg: 1.011m,
                MeasuredPackagedVolumeLiters: 18.5m,
                Status: BatchStatus.Completed));
            updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 3. Fetch calibration report
        var reportRes = await client.GetFromJsonAsync<ApiResponse<RigCalibrationReportDto>>(
            $"/api/v1/brewery-setups/{setup.Id}/calibration?sampleSize=5");
        reportRes!.Success.Should().BeTrue();
        var report = reportRes.Data!;

        report.AnalyzedBatchCount.Should().Be(2);
        report.HasSuggestions.Should().BeTrue();
        report.SuggestedBoilOffRatePerHour.Should().Be(4.00m);
        report.SuggestedFermenterLossLiters.Should().Be(1.50m);
        report.AverageOgVariancePoints.Should().Be(3.0m);
        report.Batches.Should().HaveCount(2);

        // 4. Apply suggested calibration to setup & equipment
        var applyRes = await client.PostAsJsonAsync(
            $"/api/v1/brewery-setups/{setup.Id}/calibration/apply",
            new ApplyRigCalibrationRequest(SampleSize: 5, ApplyToPrimaryEquipment: true));
        applyRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshedSetup = (await client.GetFromJsonAsync<ApiResponse<BrewerySetupDto>>($"/api/v1/brewery-setups/{setup.Id}"))!.Data!;
        refreshedSetup.DefaultBoilOffRatePerHour.Should().Be(4.00m);
        refreshedSetup.DefaultFermenterLossLiters.Should().Be(1.50m);

        var refreshedBoiler = (await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{boiler.Id}"))!.Data!;
        refreshedBoiler.BoilOffRatePerHour.Should().Be(4.00m);

        // 5. Outsider cannot view or apply calibration
        var outsider = await CreateAuthenticatedClientAsync();
        var outsiderGet = await outsider.GetAsync($"/api/v1/brewery-setups/{setup.Id}/calibration");
        outsiderGet.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CalibrationReport_FiltersAndAppliesPerBoilerAndFermenterInMultiVesselSetup()
    {
        var client = await CreateAuthenticatedClientAsync();

        var setupsRes = await client.GetFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>("/api/v1/brewery-setups");
        var setup = setupsRes!.Data!.First();

        // Create two boilers and two fermenters inside the same brewery setup
        var kettleARes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Kettle A (All-in-One 30L)",
            Type: EquipmentType.Boiler,
            Capacity: 30m,
            Subtype: EquipmentSubtype.AllInOne,
            BoilOffRatePerHour: 3.0m,
            TrubLossLiters: 1.5m,
            DefaultEfficiencyPercent: 72m));
        kettleARes.StatusCode.Should().Be(HttpStatusCode.Created);
        var kettleA = (await kettleARes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var kettleBRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Kettle B (Propane 70L)",
            Type: EquipmentType.Boiler,
            Capacity: 70m,
            Subtype: EquipmentSubtype.Pan,
            BoilOffRatePerHour: 5.5m,
            TrubLossLiters: 3.0m,
            DefaultEfficiencyPercent: 75m));
        kettleBRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var kettleB = (await kettleBRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var conical1Res = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Conical 1",
            Type: EquipmentType.Fermenter,
            Capacity: 30m,
            Subtype: EquipmentSubtype.ConicalFermenter,
            TrubLossLiters: 1.0m));
        conical1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var conical1 = (await conical1Res.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var conical2Res = await client.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            BrewerySetupId: setup.Id,
            Name: "Conical 2",
            Type: EquipmentType.Fermenter,
            Capacity: 60m,
            Subtype: EquipmentSubtype.ConicalFermenter,
            TrubLossLiters: 2.0m));
        conical2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var conical2 = (await conical2Res.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Batch on Kettle A + Conical 1: observed 3.6 L/hr boil-off, 1.4 L fermenter loss
        var batchARes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "MV-A1",
            Name: "Kettle A Batch",
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-12)),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.060m,
            TargetFg: 1.012m,
            TargetAbv: 6.3m,
            TargetIbu: 55m,
            TargetColorSrm: 7m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            BoilerId: kettleA.Id,
            FermenterId: conical1.Id,
            MeasuredOg: 1.064m,
            CustomIngredients:
            [
                new BatchIngredientInputDto(
                    "Pale Ale Malt",
                    IngredientType.Fermentable,
                    5.0m,
                    "kg",
                    IngredientUsage.Mash,
                    null,
                    null,
                    FermentableCategory: FermentableCategory.Grain)
            ]));
        batchARes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batchA = (await batchARes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        var updARes = await client.PutAsJsonAsync($"/api/v1/batches/{batchA.Id}", new UpdateBatchRequest(
            MeasuredPreBoilVolumeLiters: 26.6m,
            MeasuredPostBoilVolumeLiters: 23.0m,
            MeasuredBatchSizeLiters: 20.0m,
            MeasuredFg: 1.012m,
            MeasuredPackagedVolumeLiters: 18.6m,
            Status: BatchStatus.Completed));
        updARes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Batch on Kettle B + Conical 2: observed 6.2 L/hr boil-off, 2.8 L fermenter loss
        var batchBRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "MV-B2",
            Name: "Kettle B Batch",
            BeerStyle: "German Pils",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-8)),
            TargetBatchSizeLiters: 45m,
            TargetOg: 1.048m,
            TargetFg: 1.008m,
            TargetAbv: 5.25m,
            TargetIbu: 32m,
            TargetColorSrm: 3m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: kettleB.Id,
            FermenterId: conical2.Id,
            MeasuredOg: 1.046m,
            CustomIngredients:
            [
                new BatchIngredientInputDto(
                    "Pilsner Malt",
                    IngredientType.Fermentable,
                    11.0m,
                    "kg",
                    IngredientUsage.Mash,
                    null,
                    null,
                    FermentableCategory: FermentableCategory.Grain)
            ]));
        batchBRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batchB = (await batchBRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        var updBRes = await client.PutAsJsonAsync($"/api/v1/batches/{batchB.Id}", new UpdateBatchRequest(
            MeasuredPreBoilVolumeLiters: 54.2m,
            MeasuredPostBoilVolumeLiters: 48.0m,
            MeasuredBatchSizeLiters: 45.0m,
            MeasuredFg: 1.008m,
            MeasuredPackagedVolumeLiters: 42.2m,
            Status: BatchStatus.Completed));
        updBRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Filter calibration report to Kettle A only
        var reportARes = await client.GetFromJsonAsync<ApiResponse<RigCalibrationReportDto>>(
            $"/api/v1/brewery-setups/{setup.Id}/calibration?sampleSize=5&boilerId={kettleA.Id}");
        reportARes!.Success.Should().BeTrue();
        var reportA = reportARes.Data!;

        reportA.BoilerId.Should().Be(kettleA.Id);
        reportA.BoilerName.Should().Be("Kettle A (All-in-One 30L)");
        reportA.CurrentDefaultEfficiencyPercent.Should().Be(72m);
        reportA.AnalyzedBatchCount.Should().Be(1);
        reportA.SuggestedBoilOffRatePerHour.Should().Be(3.60m);
        reportA.SuggestedBrewhouseEfficiencyPercent.Should().NotBeNull();
        reportA.Batches.Should().ContainSingle(b => b.BatchCode == "MV-A1" && b.BoilerName == "Kettle A (All-in-One 30L)" && b.FermenterName == "Conical 1");

        // Apply calibration ONLY to Kettle A and Conical 1 without overwriting setup defaults or Kettle B / Conical 2
        var applyRes = await client.PostAsJsonAsync(
            $"/api/v1/brewery-setups/{setup.Id}/calibration/apply",
            new ApplyRigCalibrationRequest(
                SampleSize: 5,
                ApplyBoilOffRate: true,
                ApplyKettleTrubLoss: true,
                ApplyFermenterLoss: true,
                ApplyGrainAbsorption: false,
                ApplyToPrimaryEquipment: false,
                TargetBoilerId: kettleA.Id,
                TargetFermenterId: conical1.Id,
                ApplyEfficiency: true,
                ApplyToSetup: false));
        applyRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshedSetup = (await client.GetFromJsonAsync<ApiResponse<BrewerySetupDto>>($"/api/v1/brewery-setups/{setup.Id}"))!.Data!;
        refreshedSetup.DefaultBoilOffRatePerHour.Should().Be(setup.DefaultBoilOffRatePerHour);
        refreshedSetup.DefaultEfficiencyPercent.Should().Be(setup.DefaultEfficiencyPercent);

        var refreshedKettleA = (await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{kettleA.Id}"))!.Data!;
        refreshedKettleA.BoilOffRatePerHour.Should().Be(3.60m);
        refreshedKettleA.DefaultEfficiencyPercent.Should().Be(reportA.SuggestedBrewhouseEfficiencyPercent);

        var refreshedKettleB = (await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{kettleB.Id}"))!.Data!;
        refreshedKettleB.BoilOffRatePerHour.Should().Be(5.5m);
        refreshedKettleB.DefaultEfficiencyPercent.Should().Be(75m);

        var refreshedConical1 = (await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{conical1.Id}"))!.Data!;
        refreshedConical1.TrubLossLiters.Should().Be(1.40m);

        var refreshedConical2 = (await client.GetFromJsonAsync<ApiResponse<EquipmentDto>>($"/api/v1/inventory/equipment/{conical2.Id}"))!.Data!;
        refreshedConical2.TrubLossLiters.Should().Be(2.0m);
    }
}