using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public static class BrewerySetupEndpoints
{
    public static RouteGroupBuilder MapBrewerySetupEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/brewery-setups")
            .WithTags("BrewerySetups")
            .RequireAuthorization();

        group.MapGet("/", async (
            ClaimsPrincipal principal,
            IBrewerySetupService setupService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var items = await setupService.GetSetupsAsync(userId);
            return Results.Ok(ApiResponse<List<BrewerySetupDto>>.Ok(items));
        })
        .WithName("GetBrewerySetups")
        .Produces<ApiResponse<List<BrewerySetupDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IBrewerySetupService setupService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, setup) = await setupService.GetSetupByIdAsync(id, userId);
            if (result == BrewerySetupAccessResult.NotFound || setup == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Brewery setup not found."));
            }

            return Results.Ok(ApiResponse<BrewerySetupDto>.Ok(setup));
        })
        .WithName("GetBrewerySetupById")
        .Produces<ApiResponse<BrewerySetupDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            [FromBody] CreateBrewerySetupRequest request,
            IValidator<CreateBrewerySetupRequest> validator,
            ClaimsPrincipal principal,
            IBrewerySetupService setupService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Brewery setup validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, created, errorMessage) = await setupService.CreateSetupAsync(request, userId);
            if (result == BrewerySetupAccessResult.QuotaExceeded)
            {
                return Results.UnprocessableEntity(ApiResponse.Fail("QUOTA_EXCEEDED", errorMessage ?? "Setup limit reached."));
            }

            return Results.Created($"/api/v1/brewery-setups/{created!.Id}", ApiResponse<BrewerySetupDto>.Ok(created));
        })
        .WithName("CreateBrewerySetup")
        .Produces<ApiResponse<BrewerySetupDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateBrewerySetupRequest request,
            IValidator<UpdateBrewerySetupRequest> validator,
            ClaimsPrincipal principal,
            IBrewerySetupService setupService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Brewery setup validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, updated, errorMessage) = await setupService.UpdateSetupAsync(id, request, userId);
            if (result == BrewerySetupAccessResult.NotFound || updated == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Brewery setup not found."));
            }

            return Results.Ok(ApiResponse<BrewerySetupDto>.Ok(updated));
        })
        .WithName("UpdateBrewerySetup")
        .Produces<ApiResponse<BrewerySetupDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IBrewerySetupService setupService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, errorMessage) = await setupService.DeleteSetupAsync(id, userId);
            if (result == BrewerySetupAccessResult.NotFound)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Brewery setup not found."));
            }

            if (result == BrewerySetupAccessResult.CannotDeleteLastSetup)
            {
                return Results.BadRequest(ApiResponse.Fail("CANNOT_DELETE_LAST_SETUP", errorMessage ?? "Cannot delete the last remaining brewery setup."));
            }

            return Results.Ok(ApiResponse.Ok());
        })
        .WithName("DeleteBrewerySetup")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/set-default", async (
            Guid id,
            ClaimsPrincipal principal,
            IBrewerySetupService setupService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, setup, errorMessage) = await setupService.SetDefaultSetupAsync(id, userId);
            if (result == BrewerySetupAccessResult.NotFound || setup == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Brewery setup not found."));
            }

            return Results.Ok(ApiResponse<BrewerySetupDto>.Ok(setup));
        })
        .WithName("SetDefaultBrewerySetup")
        .Produces<ApiResponse<BrewerySetupDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        return group;
    }
}