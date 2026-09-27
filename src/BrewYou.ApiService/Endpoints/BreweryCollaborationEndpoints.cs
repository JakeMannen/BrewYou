using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public static class BreweryCollaborationEndpoints
{
    public static RouteGroupBuilder MapBreweryCollaborationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/brewery-setups")
            .WithTags("BreweryCollaboration")
            .RequireAuthorization();

        // 1. GET /api/v1/brewery-setups/{id}/members
        group.MapGet("/{id:guid}/members", async (
            Guid id,
            ClaimsPrincipal principal,
            IBreweryCollaborationService collabService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var members = await collabService.GetMembersAsync(id, userId);
            return Results.Ok(ApiResponse<List<BreweryMemberDto>>.Ok(members));
        })
        .WithName("GetBreweryMembers")
        .Produces<ApiResponse<List<BreweryMemberDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        // 2. GET /api/v1/brewery-setups/{id}/invites
        group.MapGet("/{id:guid}/invites", async (
            Guid id,
            ClaimsPrincipal principal,
            IBreweryCollaborationService collabService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var invites = await collabService.GetPendingInvitesAsync(id, userId);
            return Results.Ok(ApiResponse<List<BreweryInviteDto>>.Ok(invites));
        })
        .WithName("GetBreweryInvites")
        .Produces<ApiResponse<List<BreweryInviteDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        // 3. POST /api/v1/brewery-setups/{id}/invites
        group.MapPost("/{id:guid}/invites", async (
            Guid id,
            [FromBody] InviteMemberRequest request,
            IValidator<InviteMemberRequest> validator,
            ClaimsPrincipal principal,
            IBreweryCollaborationService collabService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Invite validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, invite, errorMessage) = await collabService.InviteMemberAsync(id, request, userId);
            return result switch
            {
                BreweryCollaborationAccessResult.Success => Results.Created($"/api/v1/brewery-setups/{id}/invites/{invite!.Id}", ApiResponse<BreweryInviteDto>.Ok(invite)),
                BreweryCollaborationAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Brewery setup not found.")),
                BreweryCollaborationAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", errorMessage ?? "Only owners can invite collaborators."), statusCode: StatusCodes.Status403Forbidden),
                BreweryCollaborationAccessResult.AlreadyMember => Results.Conflict(ApiResponse.Fail("ALREADY_MEMBER", errorMessage ?? "User is already a member of this setup.")),
                BreweryCollaborationAccessResult.AlreadyInvited => Results.Conflict(ApiResponse.Fail("ALREADY_INVITED", errorMessage ?? "A pending invitation already exists for this email.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMessage ?? "Could not send invitation."))
            };
        })
        .WithName("InviteBreweryMember")
        .Produces<ApiResponse<BreweryInviteDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        // 4. DELETE /api/v1/brewery-setups/{id}/invites/{inviteId}
        group.MapDelete("/{id:guid}/invites/{inviteId:guid}", async (
            Guid id,
            Guid inviteId,
            ClaimsPrincipal principal,
            IBreweryCollaborationService collabService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, errorMessage) = await collabService.RevokeInviteAsync(id, inviteId, userId);
            return result switch
            {
                BreweryCollaborationAccessResult.Success => Results.Ok(ApiResponse.Ok()),
                BreweryCollaborationAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Invitation not found.")),
                BreweryCollaborationAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", errorMessage ?? "Only owners can revoke invitations."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMessage ?? "Could not revoke invitation."))
            };
        })
        .WithName("RevokeBreweryInvite")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 5. PATCH /api/v1/brewery-setups/{id}/members/{targetUserId}
        group.MapPatch("/{id:guid}/members/{targetUserId}", async (
            Guid id,
            string targetUserId,
            [FromBody] UpdateMemberRoleRequest request,
            IValidator<UpdateMemberRoleRequest> validator,
            ClaimsPrincipal principal,
            IBreweryCollaborationService collabService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Role update validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, member, errorMessage) = await collabService.UpdateMemberRoleAsync(id, targetUserId, request.Role, userId);
            return result switch
            {
                BreweryCollaborationAccessResult.Success => Results.Ok(ApiResponse<BreweryMemberDto>.Ok(member!)),
                BreweryCollaborationAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Member not found.")),
                BreweryCollaborationAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", errorMessage ?? "Only owners can modify roles."), statusCode: StatusCodes.Status403Forbidden),
                BreweryCollaborationAccessResult.CannotModifyLastOwner => Results.BadRequest(ApiResponse.Fail("CANNOT_MODIFY_OWNER", errorMessage ?? "Cannot modify the primary owner's role.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMessage ?? "Could not update member role."))
            };
        })
        .WithName("UpdateBreweryMemberRole")
        .Produces<ApiResponse<BreweryMemberDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 6. DELETE /api/v1/brewery-setups/{id}/members/{targetUserId}
        group.MapDelete("/{id:guid}/members/{targetUserId}", async (
            Guid id,
            string targetUserId,
            ClaimsPrincipal principal,
            IBreweryCollaborationService collabService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, errorMessage) = await collabService.RemoveMemberAsync(id, targetUserId, userId);
            return result switch
            {
                BreweryCollaborationAccessResult.Success => Results.Ok(ApiResponse.Ok()),
                BreweryCollaborationAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Member not found.")),
                BreweryCollaborationAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", errorMessage ?? "Only owners can remove members."), statusCode: StatusCodes.Status403Forbidden),
                BreweryCollaborationAccessResult.CannotModifyLastOwner => Results.BadRequest(ApiResponse.Fail("CANNOT_REMOVE_OWNER", errorMessage ?? "Cannot remove the primary owner.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMessage ?? "Could not remove member."))
            };
        })
        .WithName("RemoveBreweryMember")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 7. POST /api/v1/brewery-setups/{id}/leave
        group.MapPost("/{id:guid}/leave", async (
            Guid id,
            ClaimsPrincipal principal,
            IBreweryCollaborationService collabService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, errorMessage) = await collabService.LeaveBreweryAsync(id, userId);
            return result switch
            {
                BreweryCollaborationAccessResult.Success => Results.Ok(ApiResponse.Ok()),
                BreweryCollaborationAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Brewery setup not found.")),
                BreweryCollaborationAccessResult.CannotModifyLastOwner => Results.BadRequest(ApiResponse.Fail("OWNER_CANNOT_LEAVE", errorMessage ?? "Primary owner cannot leave the brewery setup.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMessage ?? "Could not leave brewery setup."))
            };
        })
        .WithName("LeaveBrewerySetup")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 8. POST /api/v1/brewery-setups/invites/accept
        group.MapPost("/invites/accept", async (
            [FromBody] AcceptInviteRequest request,
            IValidator<AcceptInviteRequest> validator,
            ClaimsPrincipal principal,
            IBreweryCollaborationService collabService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Invalid accept invite request.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, setup, errorMessage) = await collabService.AcceptInviteAsync(request.Code, userId);
            return result switch
            {
                BreweryCollaborationAccessResult.Success => Results.Ok(ApiResponse<BrewerySetupDto>.Ok(setup!)),
                BreweryCollaborationAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Invitation not found.")),
                BreweryCollaborationAccessResult.InviteExpired => Results.UnprocessableEntity(ApiResponse.Fail("INVITE_EXPIRED", errorMessage ?? "Invitation has expired.")),
                BreweryCollaborationAccessResult.AlreadyMember => Results.Conflict(ApiResponse.Fail("ALREADY_MEMBER", errorMessage ?? "You are already a member of this setup.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMessage ?? "Could not accept invitation."))
            };
        })
        .WithName("AcceptBreweryInvite")
        .Produces<ApiResponse<BrewerySetupDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict)
        .Produces<ApiResponse>(StatusCodes.Status422UnprocessableEntity);

        // 9. GET /api/v1/brewery-setups/invites/validate?code={code}
        group.MapGet("/invites/validate", async (
            [FromQuery] string code,
            IBreweryCollaborationService collabService) =>
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Invitation code is required."));
            }

            var (result, invite, errorMessage) = await collabService.ValidateInviteAsync(code);
            return result switch
            {
                BreweryCollaborationAccessResult.Success => Results.Ok(ApiResponse<BreweryInviteDto>.Ok(invite!)),
                BreweryCollaborationAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Invitation not found.")),
                BreweryCollaborationAccessResult.InviteExpired => Results.UnprocessableEntity(ApiResponse.Fail("INVITE_EXPIRED", errorMessage ?? "Invitation has expired.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMessage ?? "Invalid invitation code."))
            };
        })
        .WithName("ValidateBreweryInvite")
        .AllowAnonymous()
        .Produces<ApiResponse<BreweryInviteDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status422UnprocessableEntity);

        return group;
    }
}