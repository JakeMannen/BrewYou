using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

namespace BrewYou.ApiService.Tests;

public class BatchSecurityHardeningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BatchSecurityHardeningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string UserId, string Email, Guid DefaultSetupId)> CreateUserAsync(string prefix = "sec")
    {
        var client = _factory.CreateClient();
        var email = $"{prefix}_{Guid.NewGuid():N}@brewyou.test";
        var reg = new RegisterRequest(email, "StrongPassword123!", $"User {prefix}", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var env = (await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", env.Data!.AccessToken);

        var setups = (await (await client.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var setupId = setups.First(s => s.IsDefault).Id;

        return (client, env.Data.User.Id, email, setupId);
    }

    private static async Task<EquipmentDto> CreateEquipmentAsync(
        HttpClient client, Guid setupId, string name, EquipmentType type, decimal capacityLiters,
        EquipmentSubtype subtype = EquipmentSubtype.Other)
    {
        var res = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, name, type, capacityLiters, Subtype: subtype));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
    }

    private static async Task AddCollaboratorAsync(
        HttpClient ownerClient, Guid setupId, HttpClient collaboratorClient, string collaboratorEmail, BreweryRole role = BreweryRole.Brewer)
    {
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest(collaboratorEmail, role));
        inviteRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var code = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!.InviteCode;

        var acceptRes = await collaboratorClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept",
            new AcceptInviteRequest(code));
        acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Finding1_PublicRecipeOwner_CannotSeeOtherUsersBatches_AndForeignBatchesDoNotBlockDelete()
    {
        var (ownerClient, _, _, _) = await CreateUserAsync("pub_owner");
        var (brewerClient, _, _, _) = await CreateUserAsync("pub_brewer");

        var recipeRes = await ownerClient.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Community Czech Pilsner",
            Description: "Public test recipe",
            BeerStyle: "Czech Pilsner",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            IsPublic: true,
            Ingredients: []));
        recipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipeId = (await recipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!.Id;

        // Brewer creates an active batch from Owner's public recipe
        var foreignBatchRes = await brewerClient.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            recipeId, null, "Secret Brewer Batch", "Czech Pilsner", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 35m, 4m, 60, 75m, null, null));
        foreignBatchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var foreignBatchId = (await foreignBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!.Id;

        // Owner's active-batches view MUST NOT disclose Brewer's batch
        var activeRes = await ownerClient.GetAsync($"/api/v1/recipes/{recipeId}/active-batches");
        activeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeBatches = (await activeRes.Content.ReadFromJsonAsync<ApiResponse<List<RecipeActiveBatchDto>>>())!.Data!;
        activeBatches.Should().BeEmpty();

        // Owner deletes their public recipe WITHOUT force=true -> must succeed despite Brewer's active batch
        var delRes = await ownerClient.DeleteAsync($"/api/v1/recipes/{recipeId}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Brewer's batch retains snapshotted RecipeName and decoupled RecipeId
        var brewerBatch = (await (await brewerClient.GetAsync($"/api/v1/batches/{foreignBatchId}"))
            .Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        brewerBatch.RecipeId.Should().BeNull();
        brewerBatch.RecipeName.Should().Be("Community Czech Pilsner");
    }

    [Fact]
    public async Task Finding2_CollaboratorCannotTakeOverOrResetOccupiedSharedEquipmentOrSpoofTelemetry()
    {
        var (ownerClient, _, _, setupId) = await CreateUserAsync("eq_owner");
        var (collabClient, _, collabEmail, _) = await CreateUserAsync("eq_collab");
        await AddCollaboratorAsync(ownerClient, setupId, collabClient, collabEmail, BreweryRole.Brewer);

        var boiler = await CreateEquipmentAsync(ownerClient, setupId, "Shared Boiler", EquipmentType.Boiler, 50m);
        var fermenter = await CreateEquipmentAsync(ownerClient, setupId, "Shared Conical", EquipmentType.Fermenter, 30m);
        var keg = await CreateEquipmentAsync(ownerClient, setupId, "Shared Keg", EquipmentType.Keg, 19m);

        // Owner creates a live batch occupying Shared Boiler in Mash stage
        var ownerBatchRes = await ownerClient.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, null, "Owner Live Batch", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.060m, 1.012m, 6.3m, 50m, 6m, 60, 75m, boiler.Id, fermenter.Id));
        ownerBatchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ownerBatchId = (await ownerBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!.Id;

        // Collaborator tries to start a live batch on Owner's occupied Boiler -> 409 Conflict
        var collabTakeoverRes = await collabClient.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, null, "Collab Hijack Batch", "Pale Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            10m, 1.045m, 1.010m, 4.6m, 25m, 4m, 60, 75m, boiler.Id, null));
        collabTakeoverRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Owner verifies Boiler live volume is untouched
        var boilerSnapshot1 = (await (await ownerClient.GetAsync($"/api/v1/inventory/equipment/{boiler.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        boilerSnapshot1.CurrentVolumeLiters.Should().BeGreaterThan(0m);

        // Owner advances to Ferment
        (await ownerClient.PatchAsJsonAsync($"/api/v1/batches/{ownerBatchId}/stage",
            new AdvanceBatchStageRequest(BrewStage.Ferment, MeasuredBatchSizeLiters: 20m))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Collaborator creates their own batch with no boiler/fermenter initially
        var collabBatchRes = await collabClient.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, null, "Collab Isolated Batch", "Lager", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.048m, 1.010m, 5.0m, 22m, 3m, 60, 75m, null, null));
        collabBatchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var collabBatchId = (await collabBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!.Id;

        // Collaborator tries to log reading / temperature targeting Owner's occupied fermenter -> 409 Conflict
        var spoofReadingRes = await collabClient.PostAsJsonAsync($"/api/v1/batches/{collabBatchId}/readings",
            new AddBatchReadingRequest(DateTime.UtcNow, 1.001m, 40.0m, Notes: null, EquipmentId: fermenter.Id));
        spoofReadingRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var spoofTempRes = await collabClient.PostAsJsonAsync($"/api/v1/batches/{collabBatchId}/equipment-readings",
            new LogBatchTemperatureRequest(99.0m, EquipmentId: fermenter.Id));
        spoofTempRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Owner completes onto Shared Keg (AssignedBatchId = ownerBatchId)
        (await ownerClient.PatchAsJsonAsync($"/api/v1/batches/{ownerBatchId}/stage",
            new AdvanceBatchStageRequest(BrewStage.Package, PackagingVesselId: keg.Id, MeasuredPackagedVolumeLiters: 18m))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Collaborator cannot hijack Owner's assigned Shared Keg -> 409 Conflict
        var hijackKegRes = await collabClient.PatchAsJsonAsync($"/api/v1/batches/{collabBatchId}/stage",
            new AdvanceBatchStageRequest(BrewStage.Package, PackagingVesselId: keg.Id, MeasuredPackagedVolumeLiters: 15m));
        hijackKegRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var kegCheck = (await (await ownerClient.GetAsync($"/api/v1/inventory/equipment/{keg.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        kegCheck.AssignedBatchId.Should().Be(ownerBatchId);
        kegCheck.CurrentVolumeLiters.Should().Be(18m);
    }

    [Fact]
    public async Task Finding3_EquipmentReadingsPagination_PerBatchCap_CustomIngredientsCap()
    {
        var (client, _, _, _) = await CreateUserAsync("quota_page");

        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, null, "Paginated Telemetry Batch", "Pilsner", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 35m, 4m, 60, 75m, null, null));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batchId = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!.Id;

        for (var i = 0; i < 5; i++)
        {
            var r = await client.PostAsJsonAsync($"/api/v1/batches/{batchId}/equipment-readings",
                new LogBatchTemperatureRequest(64m + i, Notes: $"Step {i}"));
            r.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Verify pagination slice & PaginationMeta
        var page1Res = await client.GetAsync($"/api/v1/batches/{batchId}/equipment-readings?page=1&limit=2");
        page1Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var page1Env = (await page1Res.Content.ReadFromJsonAsync<ApiResponse<List<BatchEquipmentReadingDto>>>())!;
        page1Env.Data!.Should().HaveCount(2);
        page1Env.Pagination.Should().NotBeNull();
        page1Env.Pagination!.Page.Should().Be(1);
        page1Env.Pagination.Limit.Should().Be(2);
        page1Env.Pagination.Total.Should().Be(5);
        page1Env.Pagination.TotalPages.Should().Be(3);

        // CustomIngredients > 100 -> 400 BadRequest
        var excessiveIngredients = Enumerable.Range(1, 101)
            .Select(i => new BatchIngredientInputDto($"Malt {i}", IngredientType.Fermentable, 0.1m, "kg", IngredientUsage.Mash, 60, null))
            .ToList();
        var oversizedRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, null, "Oversized Batch", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, null,
            CustomIngredients: excessiveIngredients));
        oversizedRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Seed up to 10,000 equipment readings to verify per-batch quota enforcement (422 UnprocessableEntity)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var toSeed = Enumerable.Range(0, 9995).Select(i => new EquipmentReading
            {
                Id = Guid.NewGuid(),
                EquipmentId = null,
                BatchId = batchId,
                Stage = BrewStage.Mash,
                TemperatureC = 65m,
                Timestamp = DateTime.UtcNow.AddSeconds(-i),
                Source = "Manual",
                CreatedAt = DateTime.UtcNow
            });
            await db.EquipmentReadings.AddRangeAsync(toSeed);
            await db.SaveChangesAsync();
        }

        var capRes = await client.PostAsJsonAsync($"/api/v1/batches/{batchId}/equipment-readings",
            new LogBatchTemperatureRequest(67m));
        capRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Finding3And11_TelemetryBroadcastService_EnforcesPerUserSseCapAtomically_AndCappedExpLifetime()
    {
        using var scope = _factory.Services.CreateScope();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();
        var userA = $"sse-user-{Guid.NewGuid():N}";
        var userB = $"sse-user-{Guid.NewGuid():N}";

        var subsA = new List<TelemetrySubscription>();
        try
        {
            // User A subscribes concurrently across 25 distinct batches -> capped atomically at 20 per user
            var tasks = Enumerable.Range(0, 25).Select(_ => Task.Run(() => broadcast.Subscribe(Guid.NewGuid(), userA))).ToArray();
            var results = await Task.WhenAll(tasks);
            foreach (var sub in results)
            {
                if (sub != null) subsA.Add(sub);
            }

            subsA.Should().HaveCount(20);
            broadcast.GetActiveBatchSubscriberCountForUser(userA).Should().Be(20);

            // User B is unaffected by User A's cap
            using var subB = broadcast.Subscribe(Guid.NewGuid(), userB);
            subB.Should().NotBeNull();
            broadcast.GetActiveBatchSubscriberCountForUser(userB).Should().Be(1);

            // Releasing one subscription allows another for User A
            subsA[0].Dispose();
            subsA.RemoveAt(0);
            using var extraA = broadcast.Subscribe(Guid.NewGuid(), userA);
            extraA.Should().NotBeNull();
        }
        finally
        {
            foreach (var sub in subsA) sub.Dispose();
        }

        // Verify token exp lifetime calculation
        var shortExp = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString();
        var longExp = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString();
        var expiredExp = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds().ToString();

        var shortPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("exp", shortExp)]));
        var longPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("exp", longExp)]));
        var expiredPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("exp", expiredExp)]));

        var shortLife = SseStreamHelper.ComputeMaxStreamLifetime(shortPrincipal);
        shortLife.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(46));
        SseStreamHelper.ComputeMaxStreamLifetime(longPrincipal).Should().Be(SseStreamHelper.DefaultMaxStreamLifetime);
        SseStreamHelper.ComputeMaxStreamLifetime(expiredPrincipal).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Finding5_ReopeningCompletedBatch_RespectsActiveBatchQuota()
    {
        var (client, userId, _, _) = await CreateUserAsync("reopen_quota");

        var targetRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, "B-COMP-1", "Completed To Reopen", "Stout", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.012m, 5.0m, 35m, 30m, 60, 75m, null, null));
        targetRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var targetBatchId = (await targetRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!.Id;

        (await client.PutAsJsonAsync($"/api/v1/batches/{targetBatchId}", new UpdateBatchRequest(Status: BatchStatus.Completed)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Fill remaining quota to 50 active batches
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var fillerBatches = Enumerable.Range(1, 50).Select(i => new Batch
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BatchCode = $"B-QUOTA-{i:D2}",
                Name = $"Active Batch {i}",
                BeerStyle = "Ale",
                Status = BatchStatus.Brewing,
                CurrentStage = BrewStage.Mash,
                BrewDate = DateOnly.FromDateTime(DateTime.UtcNow),
                TargetBatchSizeLiters = 20m
            });
            await db.Batches.AddRangeAsync(fillerBatches);
            await db.SaveChangesAsync();
        }

        // Reopening the completed batch back to Brewing must fail with 422 QUOTA_EXCEEDED
        var reopenRes = await client.PutAsJsonAsync($"/api/v1/batches/{targetBatchId}",
            new UpdateBatchRequest(Status: BatchStatus.Brewing));
        reopenRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Finding7And10_PreventsInventoryInflation_AndScopesIngredientLookupsToUser()
    {
        var (userAClient, userAId, _, _) = await CreateUserAsync("ing_a");
        var (userBClient, _, _, _) = await CreateUserAsync("ing_b");

        // User B creates a private fermentable with skewed PotentialGravity (1.090) and uses it in a public recipe
        var privateIngRes = await userBClient.PostAsJsonAsync("/api/v1/ingredients", new CreateIngredientRequest(
            $"Shared Name Malt {Guid.NewGuid():N}", IngredientType.Fermentable, 1.090m, 5m, null, null, "User B private malt"));
        privateIngRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var userBPrivateIng = (await privateIngRes.Content.ReadFromJsonAsync<ApiResponse<IngredientDto>>())!.Data!;

        var publicRecipeRes = await userBClient.PostAsJsonAsync("/api/v1/recipes", new CreateRecipeRequest(
            Name: "Public Recipe With Private Malt",
            Description: null,
            BeerStyle: "Pale Ale",
            BatchSizeLiters: 20m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            IsPublic: true,
            Ingredients: [new RecipeIngredientInputDto(userBPrivateIng.Id, 4.0m, "kg", 60, IngredientUsage.Mash, null)]));
        publicRecipeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var publicRecipeId = (await publicRecipeRes.Content.ReadFromJsonAsync<ApiResponse<RecipeDetailDto>>())!.Data!.Id;

        // User A creates a batch from User B's public recipe -> BatchIngredient copies SourceIngredientId = userBPrivateIng.Id
        var recipeBatchRes = await userAClient.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            publicRecipeId, null, "User A Public Recipe Batch", "Pale Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 30m, 5m, 60, 75m, null, null));
        recipeBatchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipeBatch = (await recipeBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // Toggling User B's private ingredient on User A's batch MUST NOT create an IngredientStock pointing to User B's private ingredient
        (await userAClient.PatchAsJsonAsync($"/api/v1/batches/{recipeBatch.Id}/ingredients/{recipeBatch.Ingredients.Single().Id}?isChecked=true", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var crossTenantStockExists = await db.IngredientStocks.AnyAsync(s => s.UserId == userAId && s.IngredientId == userBPrivateIng.Id);
            crossTenantStockExists.Should().BeFalse();
        }

        // Verify inventory inflation protection on User A's own low-stock catalog ingredient (1.0 kg stock, batch asks 5.0 kg)
        var ingredientsA = (await (await userAClient.GetAsync("/api/v1/ingredients"))
            .Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        var catalogMalt = ingredientsA.First(i => i.IsCatalogItem && i.Type == IngredientType.Fermentable);

        (await userAClient.PutAsJsonAsync($"/api/v1/ingredients/{catalogMalt.Id}/stock",
            new UpdateIngredientStockRequest(1.0m, "kg"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var batchRes = await userAClient.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, null, "Inflation Guard Batch", "Pale Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 30m, 5m, 60, 75m, null, null,
            CustomIngredients:
            [
                new BatchIngredientInputDto(catalogMalt.Name, IngredientType.Fermentable, 5.0m, "kg", IngredientUsage.Mash, 60, null)
            ]));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        var catBatchIngId = batch.Ingredients.Single().Id;

        for (var cycle = 0; cycle < 3; cycle++)
        {
            (await userAClient.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/ingredients/{catBatchIngId}?isChecked=true", new { }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await userAClient.PatchAsJsonAsync($"/api/v1/batches/{batch.Id}/ingredients/{catBatchIngId}?isChecked=false", new { }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var refreshedIngredientsA = (await (await userAClient.GetAsync("/api/v1/ingredients"))
            .Content.ReadFromJsonAsync<ApiResponse<List<IngredientDto>>>())!.Data!;
        refreshedIngredientsA.First(i => i.Id == catalogMalt.Id).StockAmount.Should().Be(1.0m);
    }

    [Fact]
    public async Task Finding8And12_ValidationBounds_TimestampClamping_LikeEscaping_AndNoSideEffectBrewKettleCreation()
    {
        var (client, _, _, _) = await CreateUserAsync("val_hard");

        // 1. CheckBatchStockRequestValidator blocks negative/huge volumes
        var badStockReq = await client.PostAsJsonAsync("/api/v1/batches/check-stock",
            new CheckBatchStockRequest(Guid.NewGuid(), 1_000_000m));
        badStockReq.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. CreateBatchRequestValidator blocks oversized precision, <> in style/notes, far future brew date
        var badCreateRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, null, "Bad Batch", "<script>IPA</script>", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(500)),
            50_000m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, null));
        badCreateRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Valid batch with % in name for ILike escape check & zero equipment for manual temp test
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, "B-ESC-1", "100% Brett_Ale", "Wild Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, null));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batchId = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!.Id;

        await client.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, "B-ESC-2", "1000 Regular Ale", "Pale Ale", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.050m, 1.010m, 5.2m, 35m, 6m, 60, 75m, null, null));

        // Escaped search "100%" must match ONLY "100% Brett_Ale", not "1000 Regular Ale"
        var searchRes = await client.GetAsync("/api/v1/batches?search=100%25");
        searchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchItems = (await searchRes.Content.ReadFromJsonAsync<ApiResponse<List<BatchSummaryDto>>>())!.Data!;
        searchItems.Should().ContainSingle(b => b.Id == batchId);

        // 4. Future reading timestamps rejected (> UtcNow + 5min)
        var futureReadingRes = await client.PostAsJsonAsync($"/api/v1/batches/{batchId}/readings",
            new AddBatchReadingRequest(DateTime.UtcNow.AddDays(10), 1.020m, null, null));
        futureReadingRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var futureTempRes = await client.PostAsJsonAsync($"/api/v1/batches/{batchId}/equipment-readings",
            new LogBatchTemperatureRequest(67m, Timestamp: DateTime.UtcNow.AddDays(10)));
        futureTempRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 5. LogTemperatureReading without any equipment logs manual reading WITHOUT creating phantom "Brew Kettle"
        var equipCountBefore = (await (await client.GetAsync("/api/v1/inventory/equipment"))
            .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!.Count;

        var tempRes = await client.PostAsJsonAsync($"/api/v1/batches/{batchId}/equipment-readings",
            new LogBatchTemperatureRequest(66.2m, Notes: "Thermapen check"));
        tempRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var readingDto = (await tempRes.Content.ReadFromJsonAsync<ApiResponse<BatchEquipmentReadingDto>>())!.Data!;
        readingDto.EquipmentId.Should().Be(Guid.Empty);
        readingDto.EquipmentName.Should().Be("Manual");

        var equipCountAfter = (await (await client.GetAsync("/api/v1/inventory/equipment"))
            .Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!.Count;
        equipCountAfter.Should().Be(equipCountBefore);

        // 6. UpdateBatchRequest rejects volume > 9999.99 (DB precision 6,2)
        var oversizedUpdateRes = await client.PutAsJsonAsync($"/api/v1/batches/{batchId}",
            new UpdateBatchRequest(MeasuredPreBoilVolumeLiters: 50_000m));
        oversizedUpdateRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Finding13_CrossTenantBola_ReturnsNotFoundAcrossAllBatchSubResources()
    {
        var (ownerClient, _, _, setupId) = await CreateUserAsync("bola_owner");
        var (attackerClient, _, _, _) = await CreateUserAsync("bola_attacker");

        var sensor = await CreateEquipmentAsync(ownerClient, setupId, "Owner Sensor", EquipmentType.Sensor, 0m, EquipmentSubtype.Tilt);

        var batchRes = await ownerClient.PostAsJsonAsync("/api/v1/batches", new CreateBatchRequest(
            null, null, "Victim Batch", "Bitter", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.045m, 1.010m, 4.6m, 30m, 8m, 60, 75m, null, null,
            CustomIngredients: [new("Maris Otter", IngredientType.Fermentable, 4m, "kg", IngredientUsage.Mash, 60, null)],
            CustomMashSteps: [new(1, "Mash Rest", MashStepType.Temperature, 66m, 60)],
            CustomFermentationSteps: [new(1, "Primary", FermentationStepType.Primary, 19m, 14)]));
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var victimBatch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        var endpoints = new List<Func<Task<HttpResponseMessage>>>
        {
            () => attackerClient.GetAsync($"/api/v1/batches/{victimBatch.Id}"),
            () => attackerClient.PutAsJsonAsync($"/api/v1/batches/{victimBatch.Id}", new UpdateBatchRequest(Notes: "Pwned")),
            () => attackerClient.DeleteAsync($"/api/v1/batches/{victimBatch.Id}"),
            () => attackerClient.PatchAsJsonAsync($"/api/v1/batches/{victimBatch.Id}/stage", new AdvanceBatchStageRequest(BrewStage.Boil)),
            () => attackerClient.PostAsJsonAsync($"/api/v1/batches/{victimBatch.Id}/readings", new AddBatchReadingRequest(DateTime.UtcNow, 1.020m, null, null)),
            () => attackerClient.GetAsync($"/api/v1/batches/{victimBatch.Id}/equipment-readings"),
            () => attackerClient.PostAsJsonAsync($"/api/v1/batches/{victimBatch.Id}/equipment-readings", new LogBatchTemperatureRequest(65m)),
            () => attackerClient.PatchAsJsonAsync($"/api/v1/batches/{victimBatch.Id}/ingredients/{victimBatch.Ingredients[0].Id}?isChecked=true", new { }),
            () => attackerClient.PatchAsJsonAsync($"/api/v1/batches/{victimBatch.Id}/mash-steps/{victimBatch.MashSteps[0].Id}", new ToggleBatchMashStepRequest(true)),
            () => attackerClient.PatchAsJsonAsync($"/api/v1/batches/{victimBatch.Id}/fermentation-steps/{victimBatch.FermentationSteps[0].Id}", new ToggleBatchFermentationStepRequest(true)),
            () => attackerClient.PutAsJsonAsync($"/api/v1/batches/{victimBatch.Id}/sensors", new List<BatchSensorAssignmentInput> { new(sensor.Id, BrewStage.Mash) }),
            () => attackerClient.GetAsync($"/api/v1/batches/{victimBatch.Id}/telemetry-stream")
        };

        foreach (var call in endpoints)
        {
            var res = await call();
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Finding3And4_BatchWriteEndpointsUseBatchWritePolicy_AndRateLimiterPartitionsPerAuthenticatedUser()
    {
        var (userAClient, _, _, _) = await CreateUserAsync("rl_a");
        var (userBClient, _, _, _) = await CreateUserAsync("rl_b");

        // Verify all batch write endpoints declare the "batch-write" rate limit policy
        using (var scope = _factory.Services.CreateScope())
        {
            var dataSource = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();
            var writeBatchEndpoints = dataSource.Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText != null &&
                            e.RoutePattern.RawText.StartsWith("/api/v1/batches", StringComparison.OrdinalIgnoreCase) &&
                            !e.RoutePattern.RawText.EndsWith("/check-stock", StringComparison.OrdinalIgnoreCase) &&
                            e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Any(m => m is "POST" or "PUT" or "PATCH" or "DELETE") == true)
                .ToList();

            writeBatchEndpoints.Should().NotBeEmpty();
            foreach (var ep in writeBatchEndpoints)
            {
                var rlAttr = ep.Metadata.GetMetadata<EnableRateLimitingAttribute>();
                rlAttr.Should().NotBeNull($"Endpoint '{ep.RoutePattern.RawText}' must enforce rate limiting");
                rlAttr!.PolicyName.Should().Be("batch-write");
            }
        }

        // Exhaust User A's per-user ingredient-creation-limit (30/min) -> 31st gets 429
        for (var i = 0; i < 30; i++)
        {
            var okRes = await userAClient.PostAsJsonAsync("/api/v1/ingredients", new CreateIngredientRequest(
                $"RL Malt A {i}", IngredientType.Fermentable, 1.037m, 3m, null, null, null));
            okRes.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var blockedRes = await userAClient.PostAsJsonAsync("/api/v1/ingredients", new CreateIngredientRequest(
            "RL Malt A Blocked", IngredientType.Fermentable, 1.037m, 3m, null, null, null));
        blockedRes.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // User B on same TestServer (same empty RemoteIpAddress) MUST have an independent per-user bucket
        var userBRes = await userBClient.PostAsJsonAsync("/api/v1/ingredients", new CreateIngredientRequest(
            "RL Malt B Independent", IngredientType.Fermentable, 1.037m, 3m, null, null, null));
        userBRes.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Finding9_CreateBatchAsync_SanitizesExceptionMessageInBoilerSyncWarning()
    {
        var (_, userId, _, setupId) = await CreateUserAsync("warn_sanitize");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var broadcast = scope.ServiceProvider.GetRequiredService<ITelemetryBroadcastService>();

        var boiler = new Equipment
        {
            UserId = userId,
            BrewerySetupId = setupId,
            Name = "BrewLink Boiler",
            Type = EquipmentType.Boiler,
            Subtype = EquipmentSubtype.AllInOne,
            Capacity = 30m,
            Unit = VolumeUnit.Liters,
            CapacityLiters = 30m,
            ConnectionType = EquipmentConnectionType.BrewLink
        };
        db.Equipment.Add(boiler);
        await db.SaveChangesAsync();

        var sut = new BatchService(
            db,
            broadcast,
            NullLogger<BatchService>.Instance,
            new ThrowingBrewLinkService("Internal socket 10.99.88.77:9090 refused"));

        var (result, dto, _) = await sut.CreateBatchAsync(new CreateBatchRequest(
            null, "B-SAN-01", "Sanitized Warning Batch", "IPA", DateOnly.FromDateTime(DateTime.UtcNow),
            20m, 1.055m, 1.012m, 5.6m, 45m, 6m, 60, 75m, boiler.Id, null, SendRecipeToBoiler: true), userId);

        result.Should().Be(BatchAccessResult.Success);
        dto.Should().NotBeNull();
        dto!.BoilerSyncStatus.Should().Be("OfflineFallback");
        dto.BoilerSyncWarning.Should().Be("Boiler communication failed. Batch created in manual/offline mode.");
        dto.BoilerSyncWarning.Should().NotContain("10.99.88.77");
        dto.BoilerSyncWarning.Should().NotContain("9090");
    }

    private sealed class ThrowingBrewLinkService(string secretExceptionMessage) : IBrewLinkService
    {
        public Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendRecipeToEquipmentAsync(
            string userId, Guid equipmentId, SendBrewLinkRecipeRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException(secretExceptionMessage);

        public Task<List<BrewLinkGatewayDto>> ListGatewaysAsync(string userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<BrewLinkDiscoveredGatewayDto>> DiscoverGatewaysAsync(string userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<(bool Succeeded, string? ErrorMessage)> StartPairingWindowAsync(string userId, StartBrewLinkPairingRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<(bool Succeeded, BrewLinkGatewayDto? Gateway, string? ErrorMessage)> PairGatewayAsync(string userId, PairBrewLinkRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<(bool Succeeded, BrewLinkGatewayDto? Gateway, string? ErrorMessage)> UpdateGatewayAsync(string userId, Guid id, UpdateBrewLinkGatewayRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<(bool Succeeded, bool Conflict, string? ErrorMessage)> DeleteGatewayAsync(string userId, Guid id, bool force = false, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<(bool Succeeded, List<BrewLinkEquipmentDeviceDto>? Equipment, string? ErrorMessage)> GetGatewayEquipmentAsync(string userId, Guid gatewayId, Guid? setupId = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<(BrewLinkSendResult Result, ProvisionBrewLinkEquipmentResultDto? Data, string? ErrorMessage)> ProvisionGatewayEquipmentAsync(string userId, Guid gatewayId, ProvisionBrewLinkEquipmentRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task RevokeGatewayTokenBestEffortAsync(BrewLinkGateway gateway, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<(BrewLinkSendResult Result, BrewLinkCommandResultDto? CommandResult, string? ErrorMessage)> SendControlCommandAsync(string userId, Guid equipmentId, BrewLinkControlRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<(BrewLinkSendResult Result, BrewLinkSyncResultDto? SyncResult, string? ErrorMessage)> SyncEquipmentStateAsync(string userId, Guid equipmentId, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}