using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

public class BatchTastingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BatchTastingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest($"taster_{Guid.NewGuid():N}@brewyou.test", "StrongPassword123!", "Sensory Judge", "en", VolumeUnit.Liters));
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    [Fact]
    public async Task TastingTimeline_ComputesBjcpScoresSummaryAndOffFlavors()
    {
        var client = await CreateAuthenticatedClientAsync();

        // 1. Check off-flavor diagnostic catalog
        var catalogRes = await client.GetFromJsonAsync<ApiResponse<List<OffFlavorDescriptorDto>>>("/api/v1/tastings/off-flavors");
        catalogRes!.Data!.Select(o => o.Key).Should().Contain(["Diacetyl", "Dms", "Acetaldehyde", "Oxidation", "Phenolic", "Autolysis", "Lightstruck"]);

        // 2. Create batch
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            RecipeId: null,
            BatchCode: "B-TASTE-1",
            Name: "Hoppy Amber",
            BeerStyle: "American Amber Ale",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-21)),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.055m,
            TargetFg: 1.013m,
            TargetAbv: 5.5m,
            TargetIbu: 38m,
            TargetColorSrm: 14m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 72m,
            BoilerId: null,
            FermenterId: null));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // 3. Log early green tasting (with Diacetyl flag) and mature tasting (42/50, 5 stars)
        var t1Res = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/tastings", new CreateBatchTastingRequest(
            TastedAt: DateTime.UtcNow.AddDays(-5),
            SampleSource: "HydrometerSample",
            AromaScore: 7m,
            AromaNotes: "Slight butterscotch note early.",
            AppearanceScore: 2.5m,
            FlavorScore: 13m,
            MouthfeelScore: 3.5m,
            OverallScore: 6m,
            StarRating: 3,
            WouldRebrew: true,
            OffFlavors: ["diacetyl"]));
        t1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var t1 = (await t1Res.Content.ReadFromJsonAsync<ApiResponse<BatchTastingDto>>())!.Data!;
        t1.TotalScore.Should().Be(32m);
        t1.BjcpScoreBand.Should().Be("VeryGood");
        t1.OffFlavors.Should().Contain("Diacetyl");

        var t2Res = await client.PostAsJsonAsync($"/api/v1/batches/{batch.Id}/tastings", new CreateBatchTastingRequest(
            TastedAt: DateTime.UtcNow,
            SampleSource: "Keg",
            AromaScore: 10.5m,
            AppearanceScore: 3m,
            FlavorScore: 17m,
            MouthfeelScore: 4m,
            OverallScore: 8.5m,
            StarRating: 5,
            WouldRebrew: true,
            OffFlavors: []));
        t2Res.StatusCode.Should().Be(HttpStatusCode.Created);

        // 4. Reload batch detail -> check TastingSummary & chronological timeline
        var detail = (await client.GetFromJsonAsync<ApiResponse<BatchDetailDto>>($"/api/v1/batches/{batch.Id}"))!.Data!;
        detail.Tastings.Should().HaveCount(2);
        detail.TastingSummary.Should().NotBeNull();
        detail.TastingSummary!.Count.Should().Be(2);
        detail.TastingSummary.AverageStarRating.Should().Be(4.0m);
        detail.TastingSummary.LatestBjcpScore.Should().Be(43.0m);
        detail.TastingSummary.ReportedOffFlavors.Should().Contain("Diacetyl");

        // 5. Update and delete first tasting
        var updRes = await client.PutAsJsonAsync($"/api/v1/batches/{batch.Id}/tastings/{t1.Id}", new UpdateBatchTastingRequest(
            OffFlavors: [],
            StarRating: 4));
        updRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await updRes.Content.ReadFromJsonAsync<ApiResponse<BatchTastingDto>>())!.Data!;
        updated.OffFlavors.Should().BeEmpty();

        var delRes = await client.DeleteAsync($"/api/v1/batches/{batch.Id}/tastings/{t1.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var remaining = (await client.GetFromJsonAsync<ApiResponse<List<BatchTastingDto>>>($"/api/v1/batches/{batch.Id}/tastings"))!.Data!;
        remaining.Should().HaveCount(1);
    }
}