using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services;

public record BreweryMemberDto(
    Guid Id,
    string UserId,
    string? Email,
    string? DisplayName,
    BreweryRole Role,
    DateTime JoinedAt,
    bool IsPrimaryOwner = false
);

public record BreweryInviteDto(
    Guid Id,
    Guid BrewerySetupId,
    string SetupName,
    string InvitedEmail,
    string InviteCode,
    BreweryRole Role,
    string? InvitedByName,
    DateTime CreatedAt,
    DateTime ExpiresAt
);

public record InviteMemberRequest(
    string Email,
    BreweryRole Role = BreweryRole.Brewer
);

public record UpdateMemberRoleRequest(
    BreweryRole Role
);

public record AcceptInviteRequest(
    string Code
);

public enum BreweryCollaborationAccessResult
{
    Success,
    NotFound,
    Forbidden,
    AlreadyMember,
    AlreadyInvited,
    InviteExpired,
    CannotModifyLastOwner,
    InvalidCode,
    EmailMismatch
}