using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BrewYou.ApiService.Tests;

/// <summary>
/// Shared test fixture base for Batch Process E2E integration tests (Tiers 1–4).
/// Provides authenticated HTTP client creation, equipment fixtures, recipe setup,
/// lifecycle progression, and consolidated reading ingestion.
/// </summary>
public abstract class BatchProcessE2ETestBase : IClassFixture<WebApplicationFactory<Program>>
{
    protected readonly WebApplicationFactory<Program> Factory;

    protected BatchProcessE2ETestBase(WebApplicationFactory<Program> factory)
    {
        Factory = factory;
    }

    #region Authentication & User Setup

    protected async Task<(HttpClient Client, string UserId, string Email, Guid DefaultSetupId)> CreateAuthenticatedUserAsync(string? displayName = null)
    {
        var client = Factory.CreateClient();
        var email = $"e2e_batch_brewer_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, displayName ?? "E2E Batch Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var env = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        env.Should().NotBeNull();
        env!.Success.Should().BeTrue();

        var token = env.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        setupsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        return (client, env.Data.User.Id, email, defaultSetupId);
    }

    #endregion

    #region Equipment & Recipe Setup

    protected async Task<(Guid BoilerId, Guid FermenterId, Guid KegId, Guid HltId, Guid MashTunId)> CreateStandardBreweryEquipmentAsync(
        HttpClient client, Guid defaultSetupId)
    {
        // 1. Boiler (50L)
        var boilerReq = new CreateEquipmentRequest(
            defaultSetupId,
            $"Grainfather G40 - {Guid.NewGuid():N}"[..25],
            EquipmentType.Boiler,
            45m
        );
        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", boilerReq);
        boilerRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var boilerId = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!.Id;

        // 2. Fermenter (30L)
        var fermenterReq = new CreateEquipmentRequest(
            defaultSetupId,
            $"SS Conical 30L - {Guid.NewGuid():N}"[..25],
            EquipmentType.Fermenter,
            30m
        );
        var fermenterRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", fermenterReq);
        fermenterRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var fermenterId = (await fermenterRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!.Id;

        // 3. Keg (19L)
        var kegReq = new CreateEquipmentRequest(
            defaultSetupId,
            $"Corny Keg - {Guid.NewGuid():N}"[..25],
            EquipmentType.Keg,
            19m
        );
        var kegRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", kegReq);
        kegRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var kegId = (await kegRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!.Id;

        // 4. HLT (40L) - Other with HLT Subtype or Other
        var hltReq = new CreateEquipmentRequest(
            defaultSetupId,
            $"HLT 40L - {Guid.NewGuid():N}"[..25],
            EquipmentType.Other,
            40m
        );
        var hltRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", hltReq);
        hltRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var hltId = (await hltRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!.Id;

        // 5. Mash Tun (40L) - Other with MashTun Subtype or Other
        var mashTunReq = new CreateEquipmentRequest(
            defaultSetupId,
            $"Mash Tun 40L - {Guid.NewGuid():N}"[..25],
            EquipmentType.Other,
            40m
        );
        var mashTunRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", mashTunReq);
        mashTunRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var mashTunId = (await mashTunRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!.Id;

        return (boilerId, fermenterId, kegId, hltId, mashTunId);
    }

    protected async Task<RecipeDetailDto> CreateStandardRecipeAsync(
        HttpClient client,
        string name = "Standard IPA",
        decimal targetOg = 1.050m)
    {
        var ingredientsRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients");
        var ingredients = ingredientsRes!.Data!;
        var paleMalt = ingredients.First(i => i.Name.Contains("Pale Malt") || i.Type == IngredientType.Fermentable);
        var hop = ingredients.First(i => i.Type == IngredientType.Hop);
        var yeast = ingredients.First(i => i.Type == IngredientType.Yeast);

        var req = new CreateRecipeRequest(
            Name: name,
            Description: "Standard Recipe for Batch Process E2E Testing",
            BeerStyle: "American IPA",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            IsPublic: true,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(paleMalt.Id, 5.0m, "kg", null, IngredientUsage.Mash, null),
                new(hop.Id, 30.0m, "g", 60, IngredientUsage.Boil, null),
                new(yeast.Id, 11.5m, "g", null, IngredientUsage.Primary, null)
            },
            MashSteps: new List<RecipeMashStepInputDto>
            {
                new(1, "Saccharification Rest", MashStepType.Infusion, 65m, 60, null, 15m)
            },
            FermentationSteps: new List<RecipeFermentationStepInputDto>
            {
                new(1, "Primary Fermentation", FermentationStepType.Primary, 19m, 14, null)
            }
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        return env!.Data!;
    }

    protected async Task<RecipeDetailDto> CreateHighGravityRecipeAsync(
        HttpClient client,
        string name = "Imperial Stout",
        decimal targetOg = 1.095m)
    {
        var ingredientsRes = await client.GetFromJsonAsync<ApiResponse<List<IngredientDto>>>("/api/v1/ingredients");
        var ingredients = ingredientsRes!.Data!;
        var paleMalt = ingredients.First(i => i.Name.Contains("Pale Malt") || i.Type == IngredientType.Fermentable);
        var hop = ingredients.First(i => i.Type == IngredientType.Hop);
        var yeast = ingredients.First(i => i.Type == IngredientType.Yeast);

        var req = new CreateRecipeRequest(
            Name: name,
            Description: "High-gravity Imperial Stout for ASBC Non-linear Testing",
            BeerStyle: "Imperial Stout",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 90,
            EfficiencyPercent: 72m,
            IsPublic: true,
            Ingredients: new List<RecipeIngredientInputDto>
            {
                new(paleMalt.Id, 9.5m, "kg", null, IngredientUsage.Mash, null),
                new(hop.Id, 60.0m, "g", 60, IngredientUsage.Boil, null),
                new(yeast.Id, 23.0m, "g", null, IngredientUsage.Primary, null)
            },
            MashSteps: new List<RecipeMashStepInputDto>
            {
                new(1, "Single Infusion", MashStepType.Infusion, 67m, 75, null, 25m)
            },
            FermentationSteps: new List<RecipeFermentationStepInputDto>
            {
                new(1, "Primary Fermentation", FermentationStepType.Primary, 18m, 21, null)
            }
        );

        var res = await client.PostAsJsonAsync("/api/v1/recipes", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>();
        return env!.Data!;
    }

    #endregion

    #region Batch Lifecycle Operations

    protected async Task<BatchDetailDto> CreateAdHocBatchAsync(HttpClient client, CreateBatchRequest request)
    {
        var res = await client.PostAsJsonAsync("/api/v1/batches", request);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        return env!.Data!;
    }

    protected async Task<BatchDetailDto> CreateRecipeBatchAsync(
        HttpClient client,
        Guid recipeId,
        decimal batchSize = 20m,
        Guid? boilerId = null,
        Guid? fermenterId = null)
    {
        var codeRes = await client.GetAsync("/api/v1/batches/next-code");
        var codeEnv = await codeRes.Content.ReadFromJsonAsync<ApiResponse<NextBatchCodeResponse>>();
        var code = codeEnv?.Data?.BatchCode ?? $"B-{DateTime.UtcNow:yyyy-MM-dd}-{Guid.NewGuid():N}"[..18];

        var req = new CreateBatchRequest(
            RecipeId: recipeId,
            BatchCode: code,
            Name: $"Batch for Recipe {recipeId:N}"[..25],
            BeerStyle: "American IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: batchSize,
            TargetOg: 1.050m,
            TargetFg: 1.010m,
            TargetAbv: 5.25m,
            TargetIbu: 40m,
            TargetColorSrm: 6.0m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boilerId,
            FermenterId: fermenterId
        );

        return await CreateAdHocBatchAsync(client, req);
    }

    protected async Task<BatchDetailDto> AdvanceStageAsync(
        HttpClient client,
        Guid batchId,
        AdvanceBatchStageRequest request)
    {
        var res = await client.PatchAsJsonAsync($"/api/v1/batches/{batchId}/stage", request);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        return env!.Data!;
    }

    protected async Task<HttpResponseMessage> AdvanceStageRawAsync(
        HttpClient client,
        Guid batchId,
        object request)
    {
        return await client.PatchAsJsonAsync($"/api/v1/batches/{batchId}/stage", request);
    }

    protected async Task<BatchDetailDto> FastForwardToCompletedAsync(
        HttpClient client,
        Guid batchId,
        Guid kegId,
        decimal measuredOg = 1.050m,
        decimal measuredFg = 1.010m,
        decimal measuredPackagedVolume = 19.0m)
    {
        // 1. Mash -> Boil
        await AdvanceStageAsync(client, batchId, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Boil,
            MeasuredPreBoilVolumeLiters: 24.5m,
            MeasuredPreBoilGravity: 1.042m
        ));

        // 2. Boil -> Ferment
        await AdvanceStageAsync(client, batchId, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Ferment,
            MeasuredOg: measuredOg,
            MeasuredPostBoilVolumeLiters: 21.0m,
            MeasuredBatchSizeLiters: 20.0m,
            PitchTemperatureC: 19.5m
        ));

        // 3. Ferment -> Condition
        await AdvanceStageAsync(client, batchId, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Condition,
            MeasuredFg: measuredFg
        ));

        // 4. Condition -> Package (sets Status = Completed)
        var completedBatch = await AdvanceStageAsync(client, batchId, new AdvanceBatchStageRequest(
            TargetStage: BrewStage.Package,
            MeasuredFg: measuredFg,
            PackagingVesselId: kegId,
            MeasuredPackagedVolumeLiters: measuredPackagedVolume
        ));

        completedBatch.Status.Should().Be(BatchStatus.Completed);
        return completedBatch;
    }

    protected async Task<BatchReadingDto> AddConsolidatedReadingAsync(
        HttpClient client,
        Guid batchId,
        AddBatchReadingRequest request)
    {
        var res = await client.PostAsJsonAsync($"/api/v1/batches/{batchId}/readings", request);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BatchReadingDto>>();
        return env!.Data!;
    }

    protected async Task<HttpResponseMessage> AddReadingRawAsync(
        HttpClient client,
        Guid batchId,
        object request)
    {
        return await client.PostAsJsonAsync($"/api/v1/batches/{batchId}/readings", request);
    }

    protected async Task<BatchDetailDto> UpdateBatchAsync(
        HttpClient client,
        Guid batchId,
        UpdateBatchRequest request)
    {
        var res = await client.PutAsJsonAsync($"/api/v1/batches/{batchId}", request);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        return env!.Data!;
    }

    protected async Task<HttpResponseMessage> UpdateBatchRawAsync(
        HttpClient client,
        Guid batchId,
        object request)
    {
        return await client.PutAsJsonAsync($"/api/v1/batches/{batchId}", request);
    }

    protected async Task<BatchDetailDto> GetBatchDetailAsync(HttpClient client, Guid batchId)
    {
        var res = await client.GetAsync($"/api/v1/batches/{batchId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>();
        return env!.Data!;
    }

    protected async Task SetBatchStatusDirectlyAsync(Guid batchId, BatchStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYou.ApiService.Data.BrewYouDbContext>();
        var batch = await db.Batches.FindAsync(batchId);
        if (batch != null)
        {
            batch.Status = status;
            await db.SaveChangesAsync();
        }
    }

    #endregion
}