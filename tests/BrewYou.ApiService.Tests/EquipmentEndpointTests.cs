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

public class EquipmentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public EquipmentEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    private async Task<(HttpClient Client, string UserId, string Email, Guid DefaultSetupId)> CreateAuthenticatedUserAsync(
        string? lang = "en", VolumeUnit unit = VolumeUnit.Liters)
    {
        var client = _factory.CreateClient();
        var email = $"brewer_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, "Test Brewer", lang, unit);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var token = envelope.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Fetch user's default brewery setup ID
        var setupsRes = await client.GetAsync("/api/v1/brewery-setups");
        setupsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var setupsEnv = await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>();
        var defaultSetupId = setupsEnv!.Data!.First(s => s.IsDefault).Id;

        return (client, envelope.Data.User.Id, email, defaultSetupId);
    }

    [Fact]
    public async Task EquipmentEndpoints_RequireAuthentication()
    {
        var unauthenticatedClient = _factory.CreateClient();

        var getRes = await unauthenticatedClient.GetAsync("/api/v1/inventory/equipment");
        getRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var postRes = await unauthenticatedClient.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(Guid.NewGuid(), "Test Boiler", EquipmentType.Boiler, 50));
        postRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateEquipment_WithLiters_StoresCanonicalLitersAndReturnsCreated()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var request = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Grainfather G40",
            Type: EquipmentType.Boiler,
            Capacity: 40.0m,
            Unit: VolumeUnit.Liters,
            Description: "All-in-one electric brewing system",
            Notes: "Max boil volume 40L"
        );

        var response = await client.PostAsJsonAsync("/api/v1/inventory/equipment", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var item = envelope.Data!;
        item.Id.Should().NotBeEmpty();
        item.BrewerySetupId.Should().Be(setupId);
        item.Name.Should().Be("Grainfather G40");
        item.Type.Should().Be(EquipmentType.Boiler);
        item.Capacity.Should().Be(40.0m);
        item.Unit.Should().Be(VolumeUnit.Liters);
        item.CapacityLiters.Should().Be(40.0m);
        item.Description.Should().Be("All-in-one electric brewing system");
        item.Notes.Should().Be("Max boil volume 40L");
        item.BoilOffRatePerHour.Should().Be(3.0m);
        item.TrubLossLiters.Should().Be(1.5m);
        item.MashTunDeadSpaceLiters.Should().Be(0.0m);
        item.PackagingLossLiters.Should().BeNull();
    }

    [Fact]
    public async Task CreateEquipment_WithGallons_ConvertsToCanonicalLiters()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var request = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Ss Brewtech 7 Gal Conical",
            Type: EquipmentType.Fermenter,
            Capacity: 7.0m,
            Unit: VolumeUnit.Gallons,
            Description: "Conical unitank"
        );

        var response = await client.PostAsJsonAsync("/api/v1/inventory/equipment", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();

        var item = envelope.Data!;
        item.BrewerySetupId.Should().Be(setupId);
        item.Capacity.Should().Be(7.0m);
        item.Unit.Should().Be(VolumeUnit.Gallons);
        // 7 gallons * 3.785411784 = 26.50 liters
        item.CapacityLiters.Should().Be(26.50m);
        item.TrubLossLiters.Should().Be(1.5m);
        item.BoilOffRatePerHour.Should().BeNull();
        item.MashTunDeadSpaceLiters.Should().BeNull();
        item.PackagingLossLiters.Should().BeNull();
    }

    [Fact]
    public async Task CrossUserIsolation_CannotAccessOrMutateOtherUserEquipment_ReturnsNotFound()
    {
        var (userAClient, _, _, userASetupId) = await CreateAuthenticatedUserAsync();
        var (userBClient, _, _, _) = await CreateAuthenticatedUserAsync();

        // User A creates equipment
        var createReq = new CreateEquipmentRequest(userASetupId, "Private Kettle", EquipmentType.Boiler, 30.0m, Unit: VolumeUnit.Liters);
        var createRes = await userAClient.PostAsJsonAsync("/api/v1/inventory/equipment", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // User B tries to read User A's equipment -> 404 (IDOR defense)
        var getRes = await userBClient.GetAsync($"/api/v1/inventory/equipment/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User B tries to update User A's equipment -> 404
        var updateReq = new UpdateEquipmentRequest("Hacked Kettle", EquipmentType.Boiler, 100.0m, Unit: VolumeUnit.Liters);
        var putRes = await userBClient.PutAsJsonAsync($"/api/v1/inventory/equipment/{created.Id}", updateReq);
        putRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User B tries to delete User A's equipment -> 404
        var deleteRes = await userBClient.DeleteAsync($"/api/v1/inventory/equipment/{created.Id}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User A can still retrieve the original equipment intact
        var verifyRes = await userAClient.GetAsync($"/api/v1/inventory/equipment/{created.Id}");
        verifyRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyItem = (await verifyRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        verifyItem.Name.Should().Be("Private Kettle");
    }

    [Fact]
    public async Task GetEquipmentList_FiltersByTypeAndPaginates()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Boiler A", EquipmentType.Boiler, 50));
        await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Fermenter A", EquipmentType.Fermenter, 30));
        await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Keg A", EquipmentType.Keg, 19));

        // Filter by Fermenter
        var filterRes = await client.GetAsync("/api/v1/inventory/equipment?type=Fermenter");
        filterRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var filterEnv = await filterRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>();
        filterEnv.Should().NotBeNull();
        filterEnv!.Data.Should().ContainSingle(e => e.Name == "Fermenter A");

        // List all
        var allRes = await client.GetAsync("/api/v1/inventory/equipment");
        allRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var allEnv = await allRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>();
        allEnv.Should().NotBeNull();
        allEnv!.Data!.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetEquipmentList_FiltersBySetupId_ReturnsOnlyScopedEquipment()
    {
        var (client, _, _, setup1Id) = await CreateAuthenticatedUserAsync();

        // Create Setup 2
        var setup2Res = await client.PostAsJsonAsync("/api/v1/brewery-setups",
            new CreateBrewerySetupRequest("Pilot Kitchen Setup"));
        var setup2 = (await setup2Res.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!;

        // Add equipment to Setup 1
        await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setup1Id, "Setup 1 Kettle", EquipmentType.Boiler, 50));

        // Add equipment to Setup 2
        await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setup2.Id, "Setup 2 Mini Kettle", EquipmentType.Boiler, 15));

        // Query Setup 1
        var setup1ListRes = await client.GetAsync($"/api/v1/inventory/equipment?setupId={setup1Id}");
        var setup1Items = (await setup1ListRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        setup1Items.Should().ContainSingle(e => e.Name == "Setup 1 Kettle");
        setup1Items.Should().NotContain(e => e.Name == "Setup 2 Mini Kettle");

        // Query Setup 2
        var setup2ListRes = await client.GetAsync($"/api/v1/inventory/equipment?setupId={setup2.Id}");
        var setup2Items = (await setup2ListRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        setup2Items.Should().ContainSingle(e => e.Name == "Setup 2 Mini Kettle");
        setup2Items.Should().NotContain(e => e.Name == "Setup 1 Kettle");
    }

    [Fact]
    public async Task CreateEquipment_WithInvalidOrOtherUserSetupId_ReturnsNotFound()
    {
        var (userAClient, _, _, userASetupId) = await CreateAuthenticatedUserAsync();
        var (userBClient, _, _, _) = await CreateAuthenticatedUserAsync();

        // User B tries to create equipment pointing to User A's setup -> 404
        var idorReq = new CreateEquipmentRequest(userASetupId, "Injected Kettle", EquipmentType.Boiler, 30);
        var idorRes = await userBClient.PostAsJsonAsync("/api/v1/inventory/equipment", idorReq);
        idorRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Non-existent setup Guid -> 404
        var nonExistentReq = new CreateEquipmentRequest(Guid.NewGuid(), "Ghost Kettle", EquipmentType.Boiler, 30);
        var nonExistentRes = await userAClient.PostAsJsonAsync("/api/v1/inventory/equipment", nonExistentReq);
        nonExistentRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingBrewerySetup_CascadeDeletesScopedEquipment()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync();

        // Create second setup
        var setup2Res = await client.PostAsJsonAsync("/api/v1/brewery-setups",
            new CreateBrewerySetupRequest("Temporary Setup"));
        var setup2 = (await setup2Res.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!;

        // Add equipment to second setup
        var eqRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setup2.Id, "Ephemeral Fermenter", EquipmentType.Fermenter, 30));
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Delete second setup
        var delRes = await client.DeleteAsync($"/api/v1/brewery-setups/{setup2.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Equipment should now be gone -> 404
        var checkRes = await client.GetAsync($"/api/v1/inventory/equipment/{eq.Id}");
        checkRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateEquipment_ValidPayload_UpdatesSuccessfully()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var createRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Old Keg", EquipmentType.Keg, 19, Unit: VolumeUnit.Liters));
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var updateReq = new UpdateEquipmentRequest("Cornelius Keg #1", EquipmentType.Keg, 5, Unit: VolumeUnit.Gallons, Notes: "Updated notes");
        var updateRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{created.Id}", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedEnv = await updateRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        updatedEnv!.Data!.Name.Should().Be("Cornelius Keg #1");
        updatedEnv.Data.Capacity.Should().Be(5);
        updatedEnv.Data.CapacityLiters.Should().Be(18.93m);
    }

    [Fact]
    public async Task DeleteEquipment_RemovesItemFromList()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var createRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "To Delete", EquipmentType.Other, 10));
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var delRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{created.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var getRes = await client.GetAsync($"/api/v1/inventory/equipment/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Validation_RejectsInvalidVolumeAndMissingName()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        // Zero capacity
        var zeroVolRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Valid Name", EquipmentType.Boiler, 0));
        zeroVolRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Negative capacity
        var negVolRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Valid Name", EquipmentType.Boiler, -10));
        negVolRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Empty name
        var emptyNameRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "", EquipmentType.Boiler, 20));
        emptyNameRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateVolumeUnit_UpdatesPreferenceAndPersistsInProfile()
    {
        var (client, _, _, _) = await CreateAuthenticatedUserAsync(unit: VolumeUnit.Liters);

        // Check initial /me
        var meRes = await client.GetAsync("/api/v1/auth/me");
        var meEnv = await meRes.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        meEnv!.Data!.PreferredVolumeUnit.Should().Be(VolumeUnit.Liters);

        // Update to Gallons
        var updateRes = await client.PutAsJsonAsync("/api/v1/auth/me/volume-unit",
            new UpdateVolumeUnitRequest(VolumeUnit.Gallons));
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateEnv = await updateRes.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        updateEnv!.Data!.PreferredVolumeUnit.Should().Be(VolumeUnit.Gallons);

        // Verify with /me again
        var verifyRes = await client.GetAsync("/api/v1/auth/me");
        var verifyEnv = await verifyRes.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        verifyEnv!.Data!.PreferredVolumeUnit.Should().Be(VolumeUnit.Gallons);
    }

    [Fact]
    public async Task CreateEquipment_WithSubtypeAndFillLevel_PersistsAndCalculatesFillPercentage()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        // Create All-in-One Boiler with fill level (25L of 35L = 71.4%)
        var boilerReq = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "BrewZilla Gen 4",
            Type: EquipmentType.Boiler,
            Capacity: 35.0m,
            Subtype: EquipmentSubtype.AllInOne,
            CurrentVolume: 25.0m,
            Unit: VolumeUnit.Liters,
            Description: "Electric all-in-one",
            Notes: "Mash vessel"
        );

        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", boilerReq);
        boilerRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var boilerEnv = await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        boilerEnv!.Success.Should().BeTrue();
        var boiler = boilerEnv.Data!;
        boiler.Subtype.Should().Be(EquipmentSubtype.AllInOne);
        boiler.CurrentVolume.Should().Be(25.0m);
        boiler.CurrentVolumeLiters.Should().Be(25.0m);
        boiler.FillPercentage.Should().Be(71.4m);

        // Create Conical Fermenter in Gallons (7 gal of 14 gal = 50.0%)
        var fermReq = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Spike Conical CF10",
            Type: EquipmentType.Fermenter,
            Capacity: 14.0m,
            Subtype: EquipmentSubtype.ConicalFermenter,
            CurrentVolume: 7.0m,
            Unit: VolumeUnit.Gallons
        );

        var fermRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", fermReq);
        fermRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var fermEnv = await fermRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        var ferm = fermEnv!.Data!;
        ferm.Subtype.Should().Be(EquipmentSubtype.ConicalFermenter);
        ferm.CurrentVolume.Should().Be(7.0m);
        // 7 gal * 3.785411784 = ~26.50 L, 14 gal = ~53.00 L
        ferm.CurrentVolumeLiters.Should().Be(26.50m);
        ferm.CapacityLiters.Should().Be(53.00m);
        ferm.FillPercentage.Should().Be(50.0m);

        // Verify retrieval round-trip
        var getRes = await client.GetAsync($"/api/v1/inventory/equipment/{ferm.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getEnv = await getRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        getEnv!.Data!.Subtype.Should().Be(EquipmentSubtype.ConicalFermenter);
        getEnv.Data.CurrentVolume.Should().Be(7.0m);
        getEnv.Data.FillPercentage.Should().Be(50.0m);
    }

    [Fact]
    public async Task CreateEquipment_WhenSubtypeOmitted_DefaultsSensibly()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        // Boiler defaults to AllInOne
        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Default Boiler", EquipmentType.Boiler, 30));
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        boiler.Subtype.Should().Be(EquipmentSubtype.AllInOne);
        boiler.CurrentVolume.Should().Be(0.0m);
        boiler.FillPercentage.Should().Be(0.0m);

        // Fermenter defaults to Bucket
        var fermRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Default Fermenter", EquipmentType.Fermenter, 30));
        var ferm = (await fermRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        ferm.Subtype.Should().Be(EquipmentSubtype.Bucket);

        // Keg defaults to Cornelius
        var kegRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Default Keg", EquipmentType.Keg, 19));
        var keg = (await kegRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        keg.Subtype.Should().Be(EquipmentSubtype.Cornelius);
    }

    [Theory]
    [InlineData(EquipmentType.Boiler, EquipmentSubtype.Cornelius)]
    [InlineData(EquipmentType.Boiler, EquipmentSubtype.Bucket)]
    [InlineData(EquipmentType.Fermenter, EquipmentSubtype.Pan)]
    [InlineData(EquipmentType.Fermenter, EquipmentSubtype.MiniBarrel)]
    [InlineData(EquipmentType.Keg, EquipmentSubtype.AllInOne)]
    [InlineData(EquipmentType.Keg, EquipmentSubtype.ConicalFermenter)]
    [InlineData(EquipmentType.Sensor, EquipmentSubtype.AllInOne)]
    [InlineData(EquipmentType.Other, EquipmentSubtype.ISpindel)]
    public async Task CreateEquipment_WithMismatchedSubtype_ReturnsBadRequestValidation(
        EquipmentType type, EquipmentSubtype subtype)
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var request = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Mismatched Vessel",
            Type: type,
            Capacity: 25.0m,
            Subtype: subtype,
            Unit: VolumeUnit.Liters
        );

        var response = await client.PostAsJsonAsync("/api/v1/inventory/equipment", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var env = await response.Content.ReadFromJsonAsync<ApiResponse>();
        env!.Success.Should().BeFalse();
        env.Error!.Code.Should().Be("VALIDATION_ERROR");
        env.Error.Details.Should().Contain(d => d.Field == "Subtype");
    }

    [Fact]
    public async Task Validation_RejectsNegativeOrOverfilledCurrentVolume()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        // Negative current volume
        var negRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Negative Fill", EquipmentType.Keg, 19.0m, EquipmentSubtype.Cornelius, -2.0m));
        negRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var negEnv = await negRes.Content.ReadFromJsonAsync<ApiResponse>();
        negEnv!.Error!.Details.Should().Contain(d => d.Field == "CurrentVolume");

        // Overfilled: 25L into 19L keg
        var overfillRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Overfilled Keg", EquipmentType.Keg, 19.0m, EquipmentSubtype.Cornelius, 25.0m));
        overfillRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var overfillEnv = await overfillRes.Content.ReadFromJsonAsync<ApiResponse>();
        overfillEnv!.Error!.Details.Should().Contain(d => d.Field == "CurrentVolume");
    }

    [Fact]
    public async Task CreateEquipment_WhenNameAlreadyExistsInSameSetup_Returns409Conflict()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();
        var uniqueName = $"Fermenter Alpha {Guid.NewGuid():N}";

        var firstRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, uniqueName, EquipmentType.Fermenter, 30m, EquipmentSubtype.Bucket));
        firstRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicateRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, uniqueName, EquipmentType.Fermenter, 30m, EquipmentSubtype.Bucket));
        duplicateRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var env = await duplicateRes.Content.ReadFromJsonAsync<ApiResponse>();
        env.Should().NotBeNull();
        env!.Success.Should().BeFalse();
        env.Error.Should().NotBeNull();
        env.Error!.Code.Should().Be("DUPLICATE_NAME");
        env.Error.Message.Should().Contain("already exists");
    }

    [Theory]
    [InlineData("grainfather g40")]
    [InlineData("GRAINFATHER G40")]
    [InlineData("  Grainfather G40  ")]
    [InlineData("Grainfather   G40")]
    public async Task CreateEquipment_CaseInsensitiveAndWhitespaceCollisions_Return409Conflict(string variant)
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();
        var baseName = "Grainfather G40";

        var firstRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, baseName, EquipmentType.Boiler, 40m, EquipmentSubtype.AllInOne));
        firstRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicateRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, variant, EquipmentType.Boiler, 40m, EquipmentSubtype.AllInOne));
        duplicateRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var env = await duplicateRes.Content.ReadFromJsonAsync<ApiResponse>();
        env!.Error!.Code.Should().Be("DUPLICATE_NAME");
    }

    [Fact]
    public async Task UpdateEquipment_WhenRenamingToExistingNameInSameSetup_Returns409Conflict()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();
        var name1 = $"Kettle 1 {Guid.NewGuid():N}";
        var name2 = $"Kettle 2 {Guid.NewGuid():N}";

        var res1 = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, name1, EquipmentType.Boiler, 50m, EquipmentSubtype.AllInOne));
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        var res2 = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, name2, EquipmentType.Boiler, 50m, EquipmentSubtype.AllInOne));
        res2.StatusCode.Should().Be(HttpStatusCode.Created);
        var item2 = (await res2.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Attempt to rename item2 to name1
        var updateRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{item2.Id}",
            new UpdateEquipmentRequest(name1, item2.Type, item2.Capacity));
        updateRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var env = await updateRes.Content.ReadFromJsonAsync<ApiResponse>();
        env!.Error!.Code.Should().Be("DUPLICATE_NAME");
    }

    [Fact]
    public async Task UpdateEquipment_KeepingSameNameWhenEditingOtherFields_Returns200OK()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();
        var name = $"Conical {Guid.NewGuid():N}";

        var createRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, name, EquipmentType.Fermenter, 30m, EquipmentSubtype.ConicalFermenter));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Update capacity and notes, preserving identical name
        var updateRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{created.Id}",
            new UpdateEquipmentRequest(name, created.Type, 35m, Notes: "Added cooling jacket"));
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var env = await updateRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>();
        env!.Success.Should().BeTrue();
        env.Data!.Capacity.Should().Be(35m);
        env.Data.Notes.Should().Be("Added cooling jacket");
    }

    [Fact]
    public async Task CreateEquipment_SameNameInDifferentBrewerySetup_Returns201Created()
    {
        var (client, _, _, setup1Id) = await CreateAuthenticatedUserAsync();
        var commonName = $"Standard Keg {Guid.NewGuid():N}";

        // Create second brewery setup
        var createSetupRes = await client.PostAsJsonAsync("/api/v1/brewery-setups",
            new CreateBrewerySetupRequest("Pilot Brewhouse", "Pilot setup"));
        createSetupRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var setup2 = (await createSetupRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>())!.Data!;

        // Create equipment in setup 1
        var res1 = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setup1Id, commonName, EquipmentType.Keg, 19m, EquipmentSubtype.Cornelius));
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Create equipment with same name in setup 2 -> Allowed!
        var res2 = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setup2.Id, commonName, EquipmentType.Keg, 19m, EquipmentSubtype.Cornelius));
        res2.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateEquipment_SameNameForDifferentUser_Returns201Created()
    {
        var (client1, _, _, setup1Id) = await CreateAuthenticatedUserAsync();
        var (client2, _, _, setup2Id) = await CreateAuthenticatedUserAsync();
        var commonName = $"Shared Model {Guid.NewGuid():N}";

        var res1 = await client1.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setup1Id, commonName, EquipmentType.Boiler, 30m, EquipmentSubtype.AllInOne));
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        var res2 = await client2.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setup2Id, commonName, EquipmentType.Boiler, 30m, EquipmentSubtype.AllInOne));
        res2.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetEquipmentActiveBatches_WhenUsedInActiveBatch_ReturnsBatches()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        // Create Boiler and Fermenter
        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Active Kettle", EquipmentType.Boiler, 50m));
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var fermenterRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Active Conical", EquipmentType.Fermenter, 60m));
        var fermenter = (await fermenterRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Create Batch using this boiler and fermenter
        var batchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-{Guid.NewGuid():N}"[..12],
            Name: "Hop Storm IPA",
            BeerStyle: "IPA",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 40m,
            TargetOg: 1.065m,
            TargetFg: 1.012m,
            TargetAbv: 6.9m,
            TargetIbu: 60m,
            TargetColorSrm: 6m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 75m,
            BoilerId: boiler.Id,
            FermenterId: fermenter.Id,
            MeasuredOg: 1.065m,
            PitchTemperatureC: 19m,
            Notes: null,
            CustomIngredients: null
        );
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Query active batches for boiler
        var activeRes = await client.GetAsync($"/api/v1/inventory/equipment/{boiler.Id}/active-batches");
        activeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeEnv = await activeRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentActiveBatchDto>>>();
        activeEnv.Should().NotBeNull();
        activeEnv!.Success.Should().BeTrue();
        activeEnv.Data.Should().ContainSingle(b => b.Name == "Hop Storm IPA");

        // Query active batches for fermenter
        var fermActiveRes = await client.GetAsync($"/api/v1/inventory/equipment/{fermenter.Id}/active-batches");
        fermActiveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var fermActiveEnv = await fermActiveRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentActiveBatchDto>>>();
        fermActiveEnv!.Data.Should().ContainSingle(b => b.Name == "Hop Storm IPA");
    }

    [Fact]
    public async Task GetEquipmentActiveBatches_ForOtherUserEquipment_Returns404NotFound()
    {
        var (userAClient, _, _, userASetupId) = await CreateAuthenticatedUserAsync();
        var (userBClient, _, _, _) = await CreateAuthenticatedUserAsync();

        var eqRes = await userAClient.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(userASetupId, "Private Fermenter", EquipmentType.Fermenter, 30m));
        var eq = (await eqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // User B tries to query active batches for User A's equipment -> 404
        var res = await userBClient.GetAsync($"/api/v1/inventory/equipment/{eq.Id}/active-batches");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteEquipment_WhenAssignedToActiveBatch_DecouplesForeignKeyViaSetNull()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var boilerRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Decoupled Kettle", EquipmentType.Boiler, 50m));
        var boiler = (await boilerRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        var batchReq = new CreateBatchRequest(
            RecipeId: null,
            BatchCode: $"B-{Guid.NewGuid():N}"[..12],
            Name: "Transient Batch",
            BeerStyle: "Stout",
            BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
            TargetBatchSizeLiters: 20m,
            TargetOg: 1.050m,
            TargetFg: 1.012m,
            TargetAbv: 5.0m,
            TargetIbu: 30m,
            TargetColorSrm: 30m,
            BoilTimeMinutes: 60,
            EfficiencyPercent: 70m,
            BoilerId: boiler.Id,
            FermenterId: null,
            MeasuredOg: 1.050m,
            PitchTemperatureC: 20m,
            Notes: null,
            CustomIngredients: null
        );
        var batchRes = await client.PostAsJsonAsync("/api/v1/batches", batchReq);
        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;

        // Delete the equipment
        var delRes = await client.DeleteAsync($"/api/v1/inventory/equipment/{boiler.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Batch should still exist with BoilerId set to null
        var checkBatchRes = await client.GetAsync($"/api/v1/batches/{batch.Id}");
        checkBatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedBatch = (await checkBatchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        updatedBatch.BoilerId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAndUpdateEquipment_WithLossParameters_PersistsAndReturnsLosses()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        // 1. Create with loss parameters
        var createReq = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Kettle With Calibrated Losses",
            Type: EquipmentType.Boiler,
            Capacity: 50.0m,
            Subtype: EquipmentSubtype.AllInOne,
            BoilOffRatePerHour: 3.8m,
            TrubLossLiters: 2.2m,
            MashTunDeadSpaceLiters: 0.8m,
            PackagingLossLiters: null
        );

        var createRes = await client.PostAsJsonAsync("/api/v1/inventory/equipment", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        created.BoilOffRatePerHour.Should().Be(3.8m);
        created.TrubLossLiters.Should().Be(2.2m);
        created.MashTunDeadSpaceLiters.Should().Be(0.8m);
        created.PackagingLossLiters.Should().BeNull();

        // 2. Update loss parameters
        var updateReq = new UpdateEquipmentRequest(
            Name: "Kettle With Calibrated Losses Updated",
            Type: EquipmentType.Boiler,
            Capacity: 50.0m,
            BoilOffRatePerHour: 4.0m,
            TrubLossLiters: 2.5m,
            MashTunDeadSpaceLiters: 1.0m,
            PackagingLossLiters: 0.2m
        );

        var updateRes = await client.PutAsJsonAsync($"/api/v1/inventory/equipment/{created.Id}", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = (await updateRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        updated.BoilOffRatePerHour.Should().Be(4.0m);
        updated.TrubLossLiters.Should().Be(2.5m);
        updated.MashTunDeadSpaceLiters.Should().Be(1.0m);
        updated.PackagingLossLiters.Should().Be(0.2m);
    }

    [Theory]
    [InlineData(EquipmentType.Boiler, EquipmentSubtype.Hlt)]
    [InlineData(EquipmentType.Boiler, EquipmentSubtype.HermsRims)]
    [InlineData(EquipmentType.Fermenter, EquipmentSubtype.Carboy)]
    [InlineData(EquipmentType.Fermenter, EquipmentSubtype.PressureFermenter)]
    [InlineData(EquipmentType.Fermenter, EquipmentSubtype.StainlessBucket)]
    [InlineData(EquipmentType.Keg, EquipmentSubtype.PetKeg)]
    public async Task CreateEquipment_WithNewSubtypes_Succeeds(EquipmentType type, EquipmentSubtype subtype)
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var request = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: $"New Subtype Vessel {subtype}",
            Type: type,
            Capacity: 20.0m,
            Subtype: subtype,
            Unit: VolumeUnit.Liters
        );

        var response = await client.PostAsJsonAsync("/api/v1/inventory/equipment", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        created.Subtype.Should().Be(subtype);
        created.Type.Should().Be(type);
    }

    [Fact]
    public async Task CreateEquipment_KegWithoutLossParameters_PopulatesSetupDefaultPackagingLoss()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var request = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Cornelius Keg 19L",
            Type: EquipmentType.Keg,
            Capacity: 19.0m,
            Subtype: EquipmentSubtype.Cornelius,
            Unit: VolumeUnit.Liters
        );

        var response = await client.PostAsJsonAsync("/api/v1/inventory/equipment", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        created.PackagingLossLiters.Should().Be(0.5m);
        created.BoilOffRatePerHour.Should().BeNull();
        created.TrubLossLiters.Should().BeNull();
        created.MashTunDeadSpaceLiters.Should().BeNull();
    }

    [Fact]
    public async Task CreateEquipment_Sensor_DefaultsToZeroCapacityAndNullLosses_Success()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var request = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Tilt Hydrometer Red",
            Type: EquipmentType.Sensor,
            Capacity: 0m,
            Subtype: EquipmentSubtype.Tilt,
            Unit: VolumeUnit.Liters
        );

        var response = await client.PostAsJsonAsync("/api/v1/inventory/equipment", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        created.Type.Should().Be(EquipmentType.Sensor);
        created.Subtype.Should().Be(EquipmentSubtype.Tilt);
        created.Capacity.Should().Be(0m);
        created.CapacityLiters.Should().Be(0m);
        created.CurrentVolume.Should().Be(0m);
        created.BoilOffRatePerHour.Should().BeNull();
        created.TrubLossLiters.Should().BeNull();
        created.MashTunDeadSpaceLiters.Should().BeNull();
        created.PackagingLossLiters.Should().BeNull();
    }

    [Theory]
    [InlineData("BoilOffRatePerHour")]
    [InlineData("TrubLossLiters")]
    [InlineData("MashTunDeadSpaceLiters")]
    [InlineData("PackagingLossLiters")]
    public async Task CreateEquipment_SensorWithLossParameters_ReturnsBadRequestValidation(string lossField)
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var request = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Sensor With Loss",
            Type: EquipmentType.Sensor,
            Capacity: 0m,
            Subtype: EquipmentSubtype.ISpindel,
            Unit: VolumeUnit.Liters,
            BoilOffRatePerHour: lossField == "BoilOffRatePerHour" ? 2.0m : null,
            TrubLossLiters: lossField == "TrubLossLiters" ? 1.0m : null,
            MashTunDeadSpaceLiters: lossField == "MashTunDeadSpaceLiters" ? 0.5m : null,
            PackagingLossLiters: lossField == "PackagingLossLiters" ? 0.5m : null
        );

        var response = await client.PostAsJsonAsync("/api/v1/inventory/equipment", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var env = await response.Content.ReadFromJsonAsync<ApiResponse>();
        env!.Success.Should().BeFalse();
        env.Error!.Code.Should().Be("VALIDATION_ERROR");
        env.Error.Details.Should().Contain(d => d.Field == lossField);
    }

    [Fact]
    public async Task CreateEquipment_SensorWithCustomMqttBroker_RedactsPasswordInResponse()
    {
        var (client, _, _, setupId) = await CreateAuthenticatedUserAsync();

        var configWithSecret = "{\"brokerHost\":\"mqtt.custom.org\",\"brokerPort\":1883,\"topic\":\"brew/temp\",\"username\":\"user1\",\"password\":\"SuperSecretBrokerPass123!\"}";
        var request = new CreateEquipmentRequest(
            BrewerySetupId: setupId,
            Name: "Secure Custom Sensor",
            Type: EquipmentType.Sensor,
            Capacity: 0m,
            Subtype: EquipmentSubtype.Tilt,
            Unit: VolumeUnit.Liters,
            ConnectionType: EquipmentConnectionType.Mqtt,
            ConnectionConfigJson: configWithSecret
        );

        var response = await client.PostAsJsonAsync("/api/v1/inventory/equipment", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;
        created.ConnectionConfigJson.Should().NotBeNull();
        created.ConnectionConfigJson.Should().NotContain("SuperSecretBrokerPass123!");
        created.ConnectionConfigJson.Should().Contain("\"hasPassword\":true");
    }
}