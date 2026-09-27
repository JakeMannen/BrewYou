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

public class BreweryCollaborationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BreweryCollaborationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string UserId, string Email)> CreateAuthenticatedUserAsync(string? displayName = null)
    {
        var client = _factory.CreateClient();
        var email = $"brewer_{Guid.NewGuid():N}@brewyou.test";
        var password = "StrongPassword123!";
        var reg = new RegisterRequest(email, password, displayName ?? "Test Brewer", "en", VolumeUnit.Liters);

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
    public async Task CollaborationEndpoints_RequireAuthentication()
    {
        var client = _factory.CreateClient();
        var fakeId = Guid.NewGuid();

        var getMembers = await client.GetAsync($"/api/v1/brewery-setups/{fakeId}/members");
        getMembers.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var getInvites = await client.GetAsync($"/api/v1/brewery-setups/{fakeId}/invites");
        getInvites.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var postInvite = await client.PostAsJsonAsync($"/api/v1/brewery-setups/{fakeId}/invites",
            new InviteMemberRequest("test@brewyou.test"));
        postInvite.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var acceptInvite = await client.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept",
            new AcceptInviteRequest("test-code"));
        acceptInvite.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Owner_CanInviteMember_AndValidateCodeAnonymously()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Owner Brewer");

        // Get owner's default setup
        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setups = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var setupId = setups[0].Id;

        // Owner invites a friend
        var inviteeEmail = $"invited_{Guid.NewGuid():N}@brewyou.test";
        var inviteReq = new InviteMemberRequest(inviteeEmail, BreweryRole.Brewer);
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites", inviteReq);
        inviteRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var inviteEnvelope = await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>();
        inviteEnvelope.Should().NotBeNull();
        inviteEnvelope!.Success.Should().BeTrue();
        var inviteData = inviteEnvelope.Data!;
        inviteData.InvitedEmail.Should().Be(inviteeEmail.ToLowerInvariant());
        inviteData.Role.Should().Be(BreweryRole.Brewer);
        inviteData.InviteCode.Should().NotBeNullOrWhiteSpace();

        // Anonymous user can validate the invite code
        var anonymousClient = _factory.CreateClient();
        var validateRes = await anonymousClient.GetAsync($"/api/v1/brewery-setups/invites/validate?code={inviteData.InviteCode}");
        validateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var validateEnvelope = await validateRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>();
        validateEnvelope!.Success.Should().BeTrue();
        validateEnvelope.Data!.InviteCode.Should().Be(inviteData.InviteCode);
    }

    [Fact]
    public async Task Invitee_CanAcceptInvite_AndAccessSharedSetupAndEquipment()
    {
        var (ownerClient, ownerId, _) = await CreateAuthenticatedUserAsync("Primary Owner");
        var (friendClient, friendId, _) = await CreateAuthenticatedUserAsync("Co Brewer");

        // 1. Owner gets setup
        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setups = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var setupId = setups[0].Id;

        // 2. Owner creates an equipment item in the setup
        var createEquipRes = await ownerClient.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            setupId,
            "Shared 35L Grainfather",
            EquipmentType.Boiler,
            35.0m,
            Subtype: EquipmentSubtype.AllInOne
        ));
        createEquipRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdEquip = (await createEquipRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // 3. Owner invites friend as Brewer
        var inviteReq = new InviteMemberRequest("friend@brewyou.test", BreweryRole.Brewer);
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites", inviteReq);
        inviteRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var inviteData = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!;

        // 4. Friend accepts invite
        var acceptRes = await friendClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept",
            new AcceptInviteRequest(inviteData.InviteCode));
        acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var acceptEnvelope = await acceptRes.Content.ReadFromJsonAsync<ApiResponse<BrewerySetupDto>>();
        acceptEnvelope!.Success.Should().BeTrue();
        acceptEnvelope.Data!.Id.Should().Be(setupId);
        acceptEnvelope.Data.CurrentUserRole.Should().Be(BreweryRole.Brewer);
        acceptEnvelope.Data.IsOwner.Should().BeFalse();
        acceptEnvelope.Data.MemberCount.Should().Be(2);

        // 5. Friend lists setups -> includes the shared setup
        var friendSetupsRes = await friendClient.GetAsync("/api/v1/brewery-setups");
        var friendSetups = (await friendSetupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        friendSetups.Should().Contain(s => s.Id == setupId && s.CurrentUserRole == BreweryRole.Brewer && !s.IsOwner);

        // 6. Friend lists equipment -> can see the shared Grainfather
        var friendEquipRes = await friendClient.GetAsync($"/api/v1/inventory/equipment?setupId={setupId}");
        var friendEquip = (await friendEquipRes.Content.ReadFromJsonAsync<ApiResponse<List<EquipmentDto>>>())!.Data!;
        friendEquip.Should().Contain(e => e.Id == createdEquip.Id && e.Name == "Shared 35L Grainfather");

        // 7. Check members endpoint
        var membersRes = await friendClient.GetAsync($"/api/v1/brewery-setups/{setupId}/members");
        membersRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var members = (await membersRes.Content.ReadFromJsonAsync<ApiResponse<List<BreweryMemberDto>>>())!.Data!;
        members.Should().HaveCount(2);
        members.Should().Contain(m => m.UserId == ownerId && m.Role == BreweryRole.Owner);
        members.Should().Contain(m => m.UserId == friendId && m.Role == BreweryRole.Brewer);
    }

    [Fact]
    public async Task ViewerRole_CannotModifyEquipment_AndCannotInviteCollaborators()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Owner");
        var (viewerClient, viewerId, _) = await CreateAuthenticatedUserAsync("Observer");

        // Owner gets setup
        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setupId = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0].Id;

        // Owner creates equipment
        var createEquipRes = await ownerClient.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            setupId,
            "Owner Kettle",
            EquipmentType.Boiler,
            50.0m
        ));
        var equipId = (await createEquipRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!.Id;

        // Owner invites viewer with Viewer role
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest("viewer@brewyou.test", BreweryRole.Viewer));
        var inviteCode = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!.InviteCode;

        // Viewer accepts invite
        var acceptRes = await viewerClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept",
            new AcceptInviteRequest(inviteCode));
        acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Viewer tries to invite someone else -> 403 Forbidden
        var viewerInviteRes = await viewerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest("someone@brewyou.test", BreweryRole.Viewer));
        viewerInviteRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Viewer tries to create equipment in this setup -> 403 Forbidden
        var viewerCreateEquipRes = await viewerClient.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            setupId,
            "Viewer Fermenter",
            EquipmentType.Fermenter,
            30.0m
        ));
        viewerCreateEquipRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Viewer tries to edit owner equipment -> 403 Forbidden
        var viewerUpdateEquipRes = await viewerClient.PutAsJsonAsync($"/api/v1/inventory/equipment/{equipId}", new UpdateEquipmentRequest(
            "Hacked Kettle",
            EquipmentType.Boiler,
            60.0m
        ));
        viewerUpdateEquipRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Viewer tries to delete equipment -> 403 Forbidden
        var viewerDeleteEquipRes = await viewerClient.DeleteAsync($"/api/v1/inventory/equipment/{equipId}");
        viewerDeleteEquipRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NonOwner_CannotRegenerateConnectionToken()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Owner");
        var (brewerClient, _, _) = await CreateAuthenticatedUserAsync("Brewer");

        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setupId = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0].Id;

        // Create equipment with HttpPush connection
        var createEquipRes = await ownerClient.PostAsJsonAsync("/api/v1/inventory/equipment", new CreateEquipmentRequest(
            setupId,
            "Connected Tilt",
            EquipmentType.Sensor,
            0.0m,
            Subtype: EquipmentSubtype.Tilt,
            ConnectionType: EquipmentConnectionType.HttpPush
        ));
        var equipId = (await createEquipRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!.Id;

        // Owner invites Brewer
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest("brewer@brewyou.test", BreweryRole.Brewer));
        var inviteCode = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!.InviteCode;

        // Brewer accepts invite
        await brewerClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept", new AcceptInviteRequest(inviteCode));

        // Brewer attempts to regenerate sensor connection token -> 403 Forbidden
        var regenRes = await brewerClient.PostAsync($"/api/v1/inventory/equipment/{equipId}/regenerate-token", null);
        regenRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Owner CAN regenerate token
        var ownerRegenRes = await ownerClient.PostAsync($"/api/v1/inventory/equipment/{equipId}/regenerate-token", null);
        ownerRegenRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Owner_CanUpdateMemberRole_AndRemoveMember()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Owner");
        var (collabClient, collabId, _) = await CreateAuthenticatedUserAsync("Collaborator");

        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setupId = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0].Id;

        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest("collab@brewyou.test", BreweryRole.Viewer));
        var inviteCode = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!.InviteCode;
        await collabClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept", new AcceptInviteRequest(inviteCode));

        // Owner promotes collaborator to Brewer
        var updateRoleRes = await ownerClient.PatchAsJsonAsync($"/api/v1/brewery-setups/{setupId}/members/{collabId}",
            new UpdateMemberRoleRequest(BreweryRole.Brewer));
        updateRoleRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedMember = (await updateRoleRes.Content.ReadFromJsonAsync<ApiResponse<BreweryMemberDto>>())!.Data!;
        updatedMember.Role.Should().Be(BreweryRole.Brewer);

        // Owner removes collaborator
        var removeRes = await ownerClient.DeleteAsync($"/api/v1/brewery-setups/{setupId}/members/{collabId}");
        removeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Collaborator no longer sees the setup
        var collabSetupsRes = await collabClient.GetAsync("/api/v1/brewery-setups");
        var collabSetups = (await collabSetupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        collabSetups.Should().NotContain(s => s.Id == setupId);
    }

    [Fact]
    public async Task Member_CanLeaveBrewerySetup()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Owner");
        var (memberClient, _, _) = await CreateAuthenticatedUserAsync("Member");

        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setupId = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0].Id;

        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest("member@brewyou.test", BreweryRole.Brewer));
        var inviteCode = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!.InviteCode;
        await memberClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept", new AcceptInviteRequest(inviteCode));

        // Member decides to leave the brewery setup
        var leaveRes = await memberClient.PostAsync($"/api/v1/brewery-setups/{setupId}/leave", null);
        leaveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Member no longer has the setup
        var memberSetupsRes = await memberClient.GetAsync("/api/v1/brewery-setups");
        var memberSetups = (await memberSetupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        memberSetups.Should().NotContain(s => s.Id == setupId);
    }

    [Fact]
    public async Task ValidateInvite_MasksEmailAddress_ForPrivacy()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Owner");
        var anonClient = _factory.CreateClient();

        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setupId = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0].Id;

        var fullEmail = "private_brewer@brewyou.test";
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest(fullEmail, BreweryRole.Brewer));
        var inviteCode = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!.InviteCode;

        // Anonymous user validates the invite code
        var validateRes = await anonClient.GetAsync($"/api/v1/brewery-setups/invites/validate?code={inviteCode}");
        validateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var inviteDto = (await validateRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!;
        inviteDto.InvitedEmail.Should().Contain("***");
        inviteDto.InvitedEmail.Should().NotBe(fullEmail);
    }

    [Fact]
    public async Task Brewer_CanCreateBatch_UsingSharedSetupEquipment()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Owner");
        var (brewerClient, _, _) = await CreateAuthenticatedUserAsync("Brewer");

        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setupId = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0].Id;

        // Owner creates a boiler in their setup
        var createEqRes = await ownerClient.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Community 50L Kettle", EquipmentType.Boiler, 50.0m));
        createEqRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var kettle = (await createEqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Owner invites collaborator as Brewer
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest("brewer@brewyou.test", BreweryRole.Brewer));
        var inviteCode = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!.InviteCode;
        await brewerClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept", new AcceptInviteRequest(inviteCode));

        // Collaborator (Brewer) creates a batch using the shared kettle
        var batchRes = await brewerClient.PostAsJsonAsync("/api/v1/batches",
            new CreateBatchRequest(
                RecipeId: null,
                BatchCode: null,
                Name: "Shared IPA Batch",
                BeerStyle: "American IPA",
                BrewDate: DateOnly.FromDateTime(DateTime.UtcNow),
                TargetBatchSizeLiters: 40.0m,
                TargetOg: 1.060m,
                TargetFg: 1.012m,
                TargetAbv: 6.3m,
                TargetIbu: 50m,
                TargetColorSrm: 7m,
                BoilTimeMinutes: 60,
                EfficiencyPercent: 72m,
                BoilerId: kettle.Id,
                FermenterId: null
            ));

        batchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = (await batchRes.Content.ReadFromJsonAsync<ApiResponse<BatchDetailDto>>())!.Data!;
        batch.BoilerId.Should().Be(kettle.Id);
        batch.Name.Should().Be("Shared IPA Batch");
    }

    [Fact]
    public async Task GuestBrewer_CannotDeleteEquipmentCreatedByOwner()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Owner");
        var (brewerClient, _, _) = await CreateAuthenticatedUserAsync("Brewer");

        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setupId = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0].Id;

        // Owner creates equipment in their setup
        var createEqRes = await ownerClient.PostAsJsonAsync("/api/v1/inventory/equipment",
            new CreateEquipmentRequest(setupId, "Master System 100L", EquipmentType.Boiler, 100.0m));
        var kettle = (await createEqRes.Content.ReadFromJsonAsync<ApiResponse<EquipmentDto>>())!.Data!;

        // Owner invites collaborator as Brewer
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest("brewer@brewyou.test", BreweryRole.Brewer));
        var inviteCode = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!.InviteCode;
        await brewerClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept", new AcceptInviteRequest(inviteCode));

        // Guest Brewer attempts to delete equipment created by owner -> Must be Forbidden (403)
        var deleteRes = await brewerClient.DeleteAsync($"/api/v1/inventory/equipment/{kettle.Id}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_CannotDeletePrimaryOwnerOrLastOwner_AndMembersHaveIsPrimaryOwnerFlag()
    {
        var (ownerClient, ownerId, _) = await CreateAuthenticatedUserAsync("Original Owner");
        var (friendClient, friendId, _) = await CreateAuthenticatedUserAsync("Invited Friend");

        var setupsRes = await ownerClient.GetAsync("/api/v1/brewery-setups");
        var setupId = (await setupsRes.Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data![0].Id;

        // Owner invites friend as Brewer
        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest("friend@brewyou.test", BreweryRole.Brewer));
        var inviteCode = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!.InviteCode;
        await friendClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept", new AcceptInviteRequest(inviteCode));

        // Get members list
        var membersRes = await ownerClient.GetAsync($"/api/v1/brewery-setups/{setupId}/members");
        membersRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var members = (await membersRes.Content.ReadFromJsonAsync<ApiResponse<List<BreweryMemberDto>>>())!.Data!;

        var primaryOwnerMember = members.FirstOrDefault(m => m.UserId == ownerId);
        primaryOwnerMember.Should().NotBeNull();
        primaryOwnerMember!.IsPrimaryOwner.Should().BeTrue();

        var invitedMember = members.FirstOrDefault(m => m.UserId == friendId);
        invitedMember.Should().NotBeNull();
        invitedMember!.IsPrimaryOwner.Should().BeFalse();

        // Attempting to delete the primary owner must fail
        var deleteOwnerRes = await ownerClient.DeleteAsync($"/api/v1/brewery-setups/{setupId}/members/{ownerId}");
        deleteOwnerRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Attempting to demote the primary owner must fail
        var demoteOwnerRes = await ownerClient.PatchAsJsonAsync($"/api/v1/brewery-setups/{setupId}/members/{ownerId}",
            new UpdateMemberRoleRequest(BreweryRole.Brewer));
        demoteOwnerRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}