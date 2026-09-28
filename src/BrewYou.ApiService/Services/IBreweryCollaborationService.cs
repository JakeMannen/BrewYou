using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services;

public interface IBreweryCollaborationService
{
    Task<List<BreweryMemberDto>> GetMembersAsync(Guid setupId, string currentUserId);

    Task<List<BreweryInviteDto>> GetPendingInvitesAsync(Guid setupId, string currentUserId);

    Task<(BreweryCollaborationAccessResult Result, BreweryInviteDto? Invite, string? ErrorMessage)> InviteMemberAsync(
        Guid setupId, InviteMemberRequest request, string currentUserId);

    Task<(BreweryCollaborationAccessResult Result, string? ErrorMessage)> RevokeInviteAsync(
        Guid setupId, Guid inviteId, string currentUserId);

    Task<(BreweryCollaborationAccessResult Result, BreweryMemberDto? Member, string? ErrorMessage)> UpdateMemberRoleAsync(
        Guid setupId, string targetUserId, BreweryRole newRole, string currentUserId);

    Task<(BreweryCollaborationAccessResult Result, string? ErrorMessage)> RemoveMemberAsync(
        Guid setupId, string targetUserId, string currentUserId);

    Task<(BreweryCollaborationAccessResult Result, string? ErrorMessage)> LeaveBreweryAsync(
        Guid setupId, string currentUserId);

    Task<(BreweryCollaborationAccessResult Result, BrewerySetupDto? Setup, string? ErrorMessage)> AcceptInviteAsync(
        string inviteCode, string currentUserId);

    Task<(BreweryCollaborationAccessResult Result, BreweryInviteDto? Invite, string? ErrorMessage)> ValidateInviteAsync(
        string inviteCode);
}