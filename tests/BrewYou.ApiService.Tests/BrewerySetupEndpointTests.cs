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

public class BrewerySetupEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BrewerySetupEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string UserId, string Email)> CreateAuthenticatedUserAsync()
    {
        var client = _factory.CreateClient();
        var email = $"brewer_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, "Test Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var token = envelope.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, envelope.Data.User.Id, email);
    }

    [Fact]
    public async Task BrewerySetupEndpoints_RequireAuthentication()
    {
        var unauthenticated = _factory.CreateClient();

        var getRes = await unauthenticated.GetAsync("/api/v1/brewery-setups");
        getRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var postRes = await unauthenticated.PostAsJsonAsync("/api/v1/brewery-setups",
            new CreateBrewerySetupRequest("New Rig"));
        postRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_AutoProvisionsDefaultBrewerySetupNamedMyBrewery()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync();

        var res = await client.GetAsync("/api/v1/brewery-setups");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var env = await res.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        env.Should().NotBeNull();
        env!.Success.Should().BeTrue();
        env.Data.Should().HaveCount(1);

        var setup = env.Data![0];
        setup.Name.Should().Be("My brewery");
        setup.IsDefault.Should().BeTrue();
        setup.EquipmentCount.Should().Be(0);
    }

    [Fact]
    public async Task Register_WithSwedishLanguage_AutoProvisionsDefaultBrewerySetupNamedMittBryggeri()
    {
        var client = _factory.CreateClient();
        var email = $"brewer_sv_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, "Svensk Bryggare", "sv", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Data!.IsNewUser.Should().BeTrue();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope.Data.AccessToken);

        var res = await client.GetAsync("/api/v1/brewery-setups");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var env = await res.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        env.Should().NotBeNull();
        env!.Data.Should().HaveCount(1);
        env.Data![0].Name.Should().Be("Mitt bryggeri");
        env.Data[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Login_ExistingUser_ReturnsIsNewUserFalse()
    {
        var client = _factory.CreateClient();
        var email = $"brewer_login_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, "Existing Brewer", "en", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginReq = new LoginRequest(email, password);
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await loginRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Data!.IsNewUser.Should().BeFalse();
    }

    [Fact]
    public async Task CreateBrewerySetup_ValidPayload_ReturnsCreated()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync();

        var request = new CreateBrewerySetupRequest("Garage 50L 3-Vessel", "RIMS brewing setup");
        var res = await client.PostAsJsonAsync("/api/v1/brewery-setups", request);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var env = await res.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>();
        env.Should().NotBeNull();
        env!.Success.Should().BeTrue();
        env.Data!.Name.Should().Be("Garage 50L 3-Vessel");
        env.Data.Description.Should().Be("RIMS brewing setup");
        env.Data.IsDefault.Should().BeFalse();

        // Verify list now has 2 setups
        var listRes = await client.GetAsync("/api/v1/brewery-setups");
        var listEnv = await listRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        listEnv!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateBrewerySetup_RenamesSetupSuccessfully()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync();

        var listRes = await client.GetAsync("/api/v1/brewery-setups");
        var initialList = (await listRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var setupId = initialList[0].Id;

        var updateReq = new UpdateBrewerySetupRequest("The Hop Cave", "Updated description");
        var updateRes = await client.PutAsJsonAsync($"/api/v1/brewery-setups/{setupId}", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateEnv = await updateRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>();
        updateEnv!.Data!.Name.Should().Be("The Hop Cave");
        updateEnv.Data.Description.Should().Be("Updated description");
    }

    [Fact]
    public async Task SetDefaultBrewerySetup_SwitchesDefaultSetup()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync();

        // Create second setup
        var createRes = await client.PostAsJsonAsync("/api/v1/brewery-setups",
            new CreateBrewerySetupRequest("Secondary Pilot Rig"));
        var secondSetup = (await createRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!;
        secondSetup.IsDefault.Should().BeFalse();

        // Set second setup as default
        var setDefaultRes = await client.PostAsync($"/api/v1/brewery-setups/{secondSetup.Id}/set-default", null);
        setDefaultRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify list has exactly one default and it is the second setup
        var listRes = await client.GetAsync("/api/v1/brewery-setups");
        var setups = (await listRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        setups.Should().ContainSingle(s => s.IsDefault && s.Id == secondSetup.Id);
    }

    [Fact]
    public async Task DeleteBrewerySetup_SoleSetupCannotBeDeleted_ReturnsBadRequest()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync();

        var listRes = await client.GetAsync("/api/v1/brewery-setups");
        var setups = (await listRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        setups.Should().HaveCount(1);
        var soleSetupId = setups[0].Id;

        var delRes = await client.DeleteAsync($"/api/v1/brewery-setups/{soleSetupId}");
        delRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errEnv = await delRes.Content.ReadFromJsonAsync<ApiResponse>();
        errEnv!.Success.Should().BeFalse();
        errEnv.Error!.Code.Should().Be("CANNOT_DELETE_LAST_SETUP");
    }

    [Fact]
    public async Task DeleteBrewerySetup_MultipleSetups_DeletesSuccessfullyAndPromotesDefault()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync();

        // Initial default setup
        var listRes = await client.GetAsync("/api/v1/brewery-setups");
        var initialSetup = (await listRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0];

        // Create second setup
        var createRes = await client.PostAsJsonAsync("/api/v1/brewery-setups",
            new CreateBrewerySetupRequest("Second Setup"));
        var secondSetup = (await createRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!;

        // Delete initial default setup
        var delRes = await client.DeleteAsync($"/api/v1/brewery-setups/{initialSetup.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify second setup was automatically promoted to default
        var remainingRes = await client.GetAsync("/api/v1/brewery-setups");
        var remaining = (await remainingRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        remaining.Should().HaveCount(1);
        remaining[0].Id.Should().Be(secondSetup.Id);
        remaining[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task CrossUserIsolation_CannotAccessOrMutateOtherUserBrewerySetup_ReturnsNotFound()
    {
        var (userAClient, _, _) = await CreateAuthenticatedUserAsync();
        var (userBClient, _, _) = await CreateAuthenticatedUserAsync();

        var userAList = (await (await userAClient.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var userASetupId = userAList[0].Id;

        // User B cannot read User A's setup -> 404
        var getRes = await userBClient.GetAsync($"/api/v1/brewery-setups/{userASetupId}");
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User B cannot update User A's setup -> 404
        var putRes = await userBClient.PutAsJsonAsync($"/api/v1/brewery-setups/{userASetupId}",
            new UpdateBrewerySetupRequest("Hacked Setup"));
        putRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User B cannot delete User A's setup -> 404
        var delRes = await userBClient.DeleteAsync($"/api/v1/brewery-setups/{userASetupId}");
        delRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User B cannot set User A's setup as default -> 404
        var defRes = await userBClient.PostAsync($"/api/v1/brewery-setups/{userASetupId}/set-default", null);
        defRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Register_SwedishLanguageUser_AutoProvisionsDefaultBrewerySetupNamedMittBryggeri()
    {
        var client = _factory.CreateClient();
        var email = $"sv_brewer_{Guid.NewGuid():N}@brewyou.test";
        var reg = new RegisterRequest(email, "StrongPassword123!", "Svensk Bryggare", "sv", VolumeUnit.Liters);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope.Data!.AccessToken);

        var res = await client.GetAsync("/api/v1/brewery-setups");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var env = await res.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        env.Should().NotBeNull();
        env!.Success.Should().BeTrue();
        env.Data.Should().HaveCount(1);

        var setup = env.Data![0];
        setup.Name.Should().Be("Mitt bryggeri");
        setup.IsDefault.Should().BeTrue();
    }
}