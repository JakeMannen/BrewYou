using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class YeastBankAndCascadingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public YeastBankAndCascadingTests(WebApplicationFactory<Program> factory)
    {
        var dbName = $"YeastBankTestDb_{Guid.NewGuid():N}";
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<BrewYouDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddDbContext<BrewYouDbContext>(options => options.UseInMemoryDatabase(dbName));
            });
        });
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest($"yeast_{Guid.NewGuid():N}@brewyou.test", "StrongPassword123!", "Yeast Brewer", "en", VolumeUnit.Liters));
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    [Fact]
    public async Task HarvestRepitchAndCascadingDeletion_PreservesLineageSnapshots()
    {
        var client = await CreateAuthenticatedClientAsync();

        // 1. Create custom yeast ingredient
        var customYeastRes = await client.PostAsJsonAsync("/api/v1/ingredients", new CreateIngredientRequest(
            Name: "Custom House ESB Yeast",
            Type: IngredientType.Yeast,
            PotentialGravity: null,
            ColorSrm: null,
            AlphaAcidPercent: null,
            AttenuationPercent: 74m,
            Description: "House ESB strain",
            Form: "Liquid"));
        customYeastRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var yeastIng = (await customYeastRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;

        // 2. Create Batch 1
        var b1Res = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "B-GEN1",
            Name: "First Generation Bitter",
            BeerStyle: "Best Bitter",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.048m,
            TargetFg: 1.012m,
            TargetAbv: 4.7m,
            TargetIbu: 32m,
            TargetColorSrm: 10m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            BoilerId: null,
            FermenterId: null,
            CustomIngredients:
            [
                new BatchIngredientInputDto(yeastIng.Name, IngredientType.Yeast, 1m, "pkg", IngredientUsage.Primary, null, null)
            ]));
        b1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var b1 = (await b1Res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 3. Harvest Gen 1 slurry from Batch 1
        var harvest1Res = await client.PostAsJsonAsync($"/api/v1/yeast-bank/harvest/{b1.Id}", new HarvestYeastRequest(
            StrainName: "Custom House ESB Yeast",
            IngredientId: yeastIng.Id,
            VolumeMl: 600m,
            EstimatedDensityBillionPerMl: 1.2m,
            BaseViabilityPercent: 95m,
            StorageLocation: "Fridge Jar #1"));
        harvest1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var gen1Entry = (await harvest1Res.Content.ReadFromJsonAsync<ApiResponse<YeastBankEntryDto>>())!.Data!;
        gen1Entry.Generation.Should().Be(1);
        gen1Entry.SourceBatchId.Should().Be(b1.Id);
        gen1Entry.SourceBatchCode.Should().Be("B-GEN1");

        // 4. Calculate pitch plan using banked slurry
        var calcRes = await client.PostAsJsonAsync("/api/v1/yeast-bank/calculate-pitch", new CalculateYeastPitchRequest(
            BatchVolumeLiters: 20m,
            OriginalGravity: 1.054m,
            StyleCategory: "Ale",
            YeastBankEntryId: gen1Entry.Id));
        calcRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pitchPlan = (await calcRes.Content.ReadFromJsonAsync<ApiResponse<YeastPitchPlanDto>>())!.Data!;
        pitchPlan.RequiredCellsBillions.Should().BeGreaterThan(150m);
        pitchPlan.StrainName.Should().Be("Custom House ESB Yeast");

        // 5. Repitch 250mL from Gen 1 entry into Batch 2 & harvest Gen 2
        var b2Res = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "B-GEN2",
            Name: "Second Generation Porter",
            BeerStyle: "English Porter",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.055m,
            TargetFg: 1.014m,
            TargetAbv: 5.4m,
            TargetIbu: 30m,
            TargetColorSrm: 24m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            BoilerId: null,
            FermenterId: null,
            YeastBankEntryId: gen1Entry.Id,
            YeastSlurryVolumeUsedMl: 250m,
            YeastPitchRateMillionPerMlPlato: 0.75m));
        b2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var b2 = (await b2Res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        b2.YeastBankEntryId.Should().Be(gen1Entry.Id);
        b2.YeastGeneration.Should().Be(1);

        var gen1AfterPitch = (await client.GetFromJsonAsync<ApiResponse<YeastBankEntryDto>>($"/api/v1/yeast-bank/{gen1Entry.Id}"))!.Data!;
        gen1AfterPitch.VolumeMl.Should().Be(350m);

        var harvest2Res = await client.PostAsJsonAsync($"/api/v1/yeast-bank/harvest/{b2.Id}", new HarvestYeastRequest(VolumeMl: 500m));
        harvest2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var gen2Entry = (await harvest2Res.Content.ReadFromJsonAsync<ApiResponse<YeastBankEntryDto>>())!.Data!;
        gen2Entry.Generation.Should().Be(2);

        // 6. Cascading Integrity Checks (AGENTS.md §3.K):
        // Deleting source Batch 1 keeps Gen 1 YeastBankEntry intact with SourceBatchId = null + snapshotted code/name
        var delB1 = await client.DeleteAsync($"/api/v1/batches/{b1.Id}");
        delB1.StatusCode.Should().Be(HttpStatusCode.OK);
        var gen1AfterBatchDel = (await client.GetFromJsonAsync<ApiResponse<YeastBankEntryDto>>($"/api/v1/yeast-bank/{gen1Entry.Id}"))!.Data!;
        gen1AfterBatchDel.SourceBatchId.Should().BeNull();
        gen1AfterBatchDel.SourceBatchCode.Should().Be("B-GEN1");
        gen1AfterBatchDel.SourceBatchName.Should().Be("First Generation Bitter");

        // Deleting Yeast Ingredient decouples IngredientId = null while preserving StrainName
        var delIng = await client.DeleteAsync($"/api/v1/ingredients/{yeastIng.Id}?force=true");
        delIng.StatusCode.Should().Be(HttpStatusCode.OK);
        var gen1AfterIngDel = (await client.GetFromJsonAsync<ApiResponse<YeastBankEntryDto>>($"/api/v1/yeast-bank/{gen1Entry.Id}"))!.Data!;
        gen1AfterIngDel.IngredientId.Should().BeNull();
        gen1AfterIngDel.StrainName.Should().Be("Custom House ESB Yeast");

        // Deleting Gen 1 YeastBankEntry decouples Batch 2's YeastBankEntryId cleanly
        var delYb = await client.DeleteAsync($"/api/v1/yeast-bank/{gen1Entry.Id}");
        delYb.StatusCode.Should().Be(HttpStatusCode.OK);
        var b2AfterYbDel = (await client.GetFromJsonAsync<ApiResponse<BatchDetailDto>>($"/api/v1/batches/{b2.Id}"))!.Data!;
        b2AfterYbDel.YeastBankEntryId.Should().BeNull();
    }
}