using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace BrewYou.ApiService.Services;

public class BreweryCollaborationService : IBreweryCollaborationService
{
    private readonly BrewYouDbContext _db;
    private readonly ILogger<BreweryCollaborationService> _logger;

    public BreweryCollaborationService(BrewYouDbContext db, ILogger<BreweryCollaborationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<BreweryMemberDto>> GetMembersAsync(Guid setupId, string currentUserId)
    {
        var setup = await _db.BrewerySetups
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(s => s.Id == setupId);

        if (setup == null)
        {
            return [];
        }

        // Verify current user has access to this setup
        var isOwner = setup.UserId == currentUserId;
        var isMember = setup.Members.Any(m => m.UserId == currentUserId);
        if (!isOwner && !isMember)
        {
            return [];
        }

        var result = new List<BreweryMemberDto>();

        // Include primary owner first
        if (setup.User != null)
        {
            result.Add(new BreweryMemberDto(
                setup.Id, // using setup.Id or empty guid as owner member ID
                setup.UserId,
                setup.User.Email,
                setup.User.DisplayName,
                BreweryRole.Owner,
                setup.CreatedAt,
                IsPrimaryOwner: true
            ));
        }

        // Include all co-brewers / members
        foreach (var m in setup.Members.OrderBy(m => m.JoinedAt))
        {
            result.Add(new BreweryMemberDto(
                m.Id,
                m.UserId,
                m.User?.Email,
                m.User?.DisplayName,
                m.Role,
                m.JoinedAt,
                IsPrimaryOwner: false
            ));
        }

        return result;
    }

    public async Task<List<BreweryInviteDto>> GetPendingInvitesAsync(Guid setupId, string currentUserId)
    {
        var isOwner = await IsSetupOwnerAsync(setupId, currentUserId);
        if (!isOwner)
        {
            return [];
        }

        var now = DateTime.UtcNow;
        var invites = await _db.BreweryInvites
            .AsNoTracking()
            .Include(i => i.BrewerySetup)
            .Include(i => i.InvitedByUser)
            .Where(i => i.BrewerySetupId == setupId && i.AcceptedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new BreweryInviteDto(
                i.Id,
                i.BrewerySetupId,
                i.BrewerySetup != null ? i.BrewerySetup.Name : string.Empty,
                i.InvitedEmail,
                i.InviteCode,
                i.Role,
                i.InvitedByUser != null ? i.InvitedByUser.DisplayName : null,
                i.CreatedAt,
                i.ExpiresAt
            ))
            .ToListAsync();

        return invites;
    }

    public async Task<(BreweryCollaborationAccessResult Result, BreweryInviteDto? Invite, string? ErrorMessage)> InviteMemberAsync(
        Guid setupId, InviteMemberRequest request, string currentUserId)
    {
        var setup = await _db.BrewerySetups
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == setupId);

        if (setup == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, null, "Brewery setup not found.");
        }

        var isOwner = await IsSetupOwnerAsync(setupId, currentUserId);
        if (!isOwner)
        {
            return (BreweryCollaborationAccessResult.Forbidden, null, "Only an owner can invite new collaborators.");
        }

        var emailNormalized = request.Email.Trim().ToLowerInvariant();

        // Check if user with this email is already the primary owner
        var ownerUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == setup.UserId);
        if (ownerUser != null && string.Equals(ownerUser.Email, emailNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return (BreweryCollaborationAccessResult.AlreadyMember, null, "This user is already the owner of this brewery setup.");
        }

        // Check if user is already an active member
        var existingMemberUser = await _db.Users
            .Where(u => u.Email != null && u.Email.ToLower() == emailNormalized)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        if (existingMemberUser != null && setup.Members.Any(m => m.UserId == existingMemberUser))
        {
            return (BreweryCollaborationAccessResult.AlreadyMember, null, "This user is already a member of this brewery setup.");
        }

        // Check if an unexpired invite already exists for this email on this setup
        var now = DateTime.UtcNow;
        var existingInvite = await _db.BreweryInvites
            .FirstOrDefaultAsync(i => i.BrewerySetupId == setupId &&
                                      i.InvitedEmail.ToLower() == emailNormalized &&
                                      i.AcceptedAt == null &&
                                      i.ExpiresAt > now);

        if (existingInvite != null)
        {
            return (BreweryCollaborationAccessResult.AlreadyInvited, null, "A pending invitation for this email address already exists.");
        }

        var inviter = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
        var inviteCode = GenerateInviteCode();

        var invite = new BreweryInvite
        {
            BrewerySetupId = setupId,
            InvitedEmail = emailNormalized,
            InviteCode = inviteCode,
            Role = request.Role,
            InvitedByUserId = currentUserId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(7)
        };

        _db.BreweryInvites.Add(invite);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Invitation created for {Email} to brewery setup {SetupId} with role {Role}",
            emailNormalized, setupId, request.Role);

        var dto = new BreweryInviteDto(
            invite.Id,
            invite.BrewerySetupId,
            setup.Name,
            invite.InvitedEmail,
            invite.InviteCode,
            invite.Role,
            inviter?.DisplayName,
            invite.CreatedAt,
            invite.ExpiresAt
        );

        return (BreweryCollaborationAccessResult.Success, dto, null);
    }

    public async Task<(BreweryCollaborationAccessResult Result, string? ErrorMessage)> RevokeInviteAsync(
        Guid setupId, Guid inviteId, string currentUserId)
    {
        var isOwner = await IsSetupOwnerAsync(setupId, currentUserId);
        if (!isOwner)
        {
            return (BreweryCollaborationAccessResult.Forbidden, "Only an owner can revoke invitations.");
        }

        var invite = await _db.BreweryInvites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.BrewerySetupId == setupId);

        if (invite == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, "Invitation not found.");
        }

        _db.BreweryInvites.Remove(invite);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Invitation {InviteId} for setup {SetupId} was revoked by {UserId}", inviteId, setupId, currentUserId);
        return (BreweryCollaborationAccessResult.Success, null);
    }

    public async Task<(BreweryCollaborationAccessResult Result, BreweryMemberDto? Member, string? ErrorMessage)> UpdateMemberRoleAsync(
        Guid setupId, string targetUserId, BreweryRole newRole, string currentUserId)
    {
        var setup = await _db.BrewerySetups.FirstOrDefaultAsync(s => s.Id == setupId);
        if (setup == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, null, "Brewery setup not found.");
        }

        var isOwner = await IsSetupOwnerAsync(setupId, currentUserId);
        if (!isOwner)
        {
            return (BreweryCollaborationAccessResult.Forbidden, null, "Only an owner can modify collaborator roles.");
        }

        if (targetUserId == setup.UserId)
        {
            return (BreweryCollaborationAccessResult.CannotModifyLastOwner, null, "Cannot modify the primary owner's role.");
        }

        if (newRole != BreweryRole.Owner)
        {
            var otherOwnersCount = (setup.UserId != targetUserId ? 1 : 0) +
                await _db.BreweryMembers.CountAsync(m => m.BrewerySetupId == setupId && m.Role == BreweryRole.Owner && m.UserId != targetUserId);
            if (otherOwnersCount <= 0)
            {
                return (BreweryCollaborationAccessResult.CannotModifyLastOwner, null, "Cannot demote the last owner from the brewery setup.");
            }
        }

        var member = await _db.BreweryMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.BrewerySetupId == setupId && m.UserId == targetUserId);

        if (member == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, null, "Member not found in this brewery setup.");
        }

        member.Role = newRole;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Member {TargetUserId} role updated to {Role} in setup {SetupId} by {UserId}",
            targetUserId, newRole, setupId, currentUserId);

        var dto = new BreweryMemberDto(
            member.Id,
            member.UserId,
            member.User?.Email,
            member.User?.DisplayName,
            member.Role,
            member.JoinedAt,
            IsPrimaryOwner: false
        );

        return (BreweryCollaborationAccessResult.Success, dto, null);
    }

    public async Task<(BreweryCollaborationAccessResult Result, string? ErrorMessage)> RemoveMemberAsync(
        Guid setupId, string targetUserId, string currentUserId)
    {
        var setup = await _db.BrewerySetups.FirstOrDefaultAsync(s => s.Id == setupId);
        if (setup == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, "Brewery setup not found.");
        }

        var isOwner = await IsSetupOwnerAsync(setupId, currentUserId);
        if (!isOwner)
        {
            return (BreweryCollaborationAccessResult.Forbidden, "Only an owner can remove collaborators.");
        }

        if (targetUserId == setup.UserId)
        {
            return (BreweryCollaborationAccessResult.CannotModifyLastOwner, "Cannot remove the primary owner from the brewery setup.");
        }

        var otherOwnersCount = (setup.UserId != targetUserId ? 1 : 0) +
            await _db.BreweryMembers.CountAsync(m => m.BrewerySetupId == setupId && m.Role == BreweryRole.Owner && m.UserId != targetUserId);
        if (otherOwnersCount <= 0)
        {
            return (BreweryCollaborationAccessResult.CannotModifyLastOwner, "Cannot remove the last owner from the brewery setup.");
        }

        var member = await _db.BreweryMembers
            .FirstOrDefaultAsync(m => m.BrewerySetupId == setupId && m.UserId == targetUserId);

        if (member == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, "Member not found in this brewery setup.");
        }

        _db.BreweryMembers.Remove(member);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Member {TargetUserId} removed from setup {SetupId} by {UserId}", targetUserId, setupId, currentUserId);
        return (BreweryCollaborationAccessResult.Success, null);
    }

    public async Task<(BreweryCollaborationAccessResult Result, string? ErrorMessage)> LeaveBreweryAsync(
        Guid setupId, string currentUserId)
    {
        var setup = await _db.BrewerySetups.FirstOrDefaultAsync(s => s.Id == setupId);
        if (setup == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, "Brewery setup not found.");
        }

        if (currentUserId == setup.UserId)
        {
            return (BreweryCollaborationAccessResult.CannotModifyLastOwner, "The primary owner cannot leave the brewery setup. Delete or reassign the setup instead.");
        }

        var member = await _db.BreweryMembers
            .FirstOrDefaultAsync(m => m.BrewerySetupId == setupId && m.UserId == currentUserId);

        if (member == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, "You are not a member of this brewery setup.");
        }

        _db.BreweryMembers.Remove(member);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} left brewery setup {SetupId}", currentUserId, setupId);
        return (BreweryCollaborationAccessResult.Success, null);
    }

    public async Task<(BreweryCollaborationAccessResult Result, BrewerySetupDto? Setup, string? ErrorMessage)> AcceptInviteAsync(
        string inviteCode, string currentUserId)
    {
        var normalizedCode = inviteCode.Trim();
        var now = DateTime.UtcNow;

        var invite = await _db.BreweryInvites
            .Include(i => i.BrewerySetup)
                .ThenInclude(s => s!.Equipment)
            .Include(i => i.BrewerySetup)
                .ThenInclude(s => s!.Members)
            .FirstOrDefaultAsync(i => i.InviteCode == normalizedCode && i.AcceptedAt == null);

        if (invite == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, null, "Invitation code is invalid or has already been used.");
        }

        if (invite.ExpiresAt <= now)
        {
            return (BreweryCollaborationAccessResult.InviteExpired, null, "This invitation has expired.");
        }

        var setup = invite.BrewerySetup;
        if (setup == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, null, "Associated brewery setup no longer exists.");
        }

        if (setup.UserId == currentUserId || setup.Members.Any(m => m.UserId == currentUserId))
        {
            return (BreweryCollaborationAccessResult.AlreadyMember, null, "You are already a member of this brewery setup.");
        }

        var newMember = new BreweryMember
        {
            BrewerySetupId = setup.Id,
            UserId = currentUserId,
            Role = invite.Role,
            JoinedAt = now
        };

        _db.BreweryMembers.Add(newMember);
        invite.AcceptedAt = now;
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} accepted invite {InviteId} and joined setup {SetupId} as {Role}",
            currentUserId, invite.Id, setup.Id, invite.Role);

        var memberCount = 1 + await _db.BreweryMembers.CountAsync(m => m.BrewerySetupId == setup.Id);
        var equipmentCount = setup.Equipment.Count;

        var setupDto = new BrewerySetupDto(
            setup.Id,
            setup.Name,
            setup.Description,
            setup.IsDefault,
            equipmentCount,
            setup.DefaultGrainAbsorptionRate,
            setup.DefaultBoilOffRatePerHour,
            setup.DefaultKettleTrubLossLiters,
            setup.DefaultFermenterLossLiters,
            setup.DefaultMashTunDeadSpaceLiters,
            setup.CoolingShrinkagePercent,
            setup.DefaultPackagingLossLiters,
            setup.CreatedAt,
            setup.UpdatedAt,
            CurrentUserRole: invite.Role,
            MemberCount: memberCount,
            IsOwner: false
        );

        return (BreweryCollaborationAccessResult.Success, setupDto, null);
    }

    public async Task<(BreweryCollaborationAccessResult Result, BreweryInviteDto? Invite, string? ErrorMessage)> ValidateInviteAsync(
        string inviteCode)
    {
        var normalizedCode = inviteCode.Trim();
        var now = DateTime.UtcNow;

        var invite = await _db.BreweryInvites
            .AsNoTracking()
            .Include(i => i.BrewerySetup)
            .Include(i => i.InvitedByUser)
            .FirstOrDefaultAsync(i => i.InviteCode == normalizedCode && i.AcceptedAt == null);

        if (invite == null)
        {
            return (BreweryCollaborationAccessResult.NotFound, null, "Invitation not found or already accepted.");
        }

        if (invite.ExpiresAt <= now)
        {
            return (BreweryCollaborationAccessResult.InviteExpired, null, "This invitation has expired.");
        }

        var dto = new BreweryInviteDto(
            invite.Id,
            invite.BrewerySetupId,
            invite.BrewerySetup?.Name ?? "Brewery",
            MaskEmail(invite.InvitedEmail),
            invite.InviteCode,
            invite.Role,
            invite.InvitedByUser?.DisplayName,
            invite.CreatedAt,
            invite.ExpiresAt
        );

        return (BreweryCollaborationAccessResult.Success, dto, null);
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return "***" + (atIndex >= 0 ? email[atIndex..] : "");
        return string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(atIndex - 1));
    }

    private async Task<bool> IsSetupOwnerAsync(Guid setupId, string userId)
    {
        var isPrimaryOwner = await _db.BrewerySetups
            .AnyAsync(s => s.Id == setupId && s.UserId == userId);

        if (isPrimaryOwner)
        {
            return true;
        }

        var isCoOwner = await _db.BreweryMembers
            .AnyAsync(m => m.BrewerySetupId == setupId && m.UserId == userId && m.Role == BreweryRole.Owner);

        return isCoOwner;
    }

    private static string GenerateInviteCode()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }
}