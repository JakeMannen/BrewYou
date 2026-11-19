using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public static class WaterProfileEndpoints
{
    public static RouteGroupBuilder MapWaterProfileEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/water-profiles")
            .WithTags("WaterProfiles")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] string? category,
            ClaimsPrincipal principal,
            IWaterProfileService waterProfileService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var items = await waterProfileService.GetProfilesAsync(userId, category);
            return Results.Ok(ApiResponse<List<WaterProfileDto>>.Ok(items));
        })
        .WithName("GetWaterProfiles")
        .Produces<ApiResponse<List<WaterProfileDto>>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IWaterProfileService waterProfileService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var item = await waterProfileService.GetProfileByIdAsync(id, userId);
            if (item is null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Water profile not found."));
            }

            return Results.Ok(ApiResponse<WaterProfileDto>.Ok(item));
        })
        .WithName("GetWaterProfileById")
        .Produces<ApiResponse<WaterProfileDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            [FromBody] CreateWaterProfileRequest request,
            IValidator<CreateWaterProfileRequest> validator,
            ClaimsPrincipal principal,
            IWaterProfileService waterProfileService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Water profile validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var created = await waterProfileService.CreateProfileAsync(request, userId);
            return Results.Created($"/api/v1/water-profiles/{created.Id}", ApiResponse<WaterProfileDto>.Ok(created));
        })
        .WithName("CreateWaterProfile")
        .Produces<ApiResponse<WaterProfileDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateWaterProfileRequest request,
            IValidator<UpdateWaterProfileRequest> validator,
            ClaimsPrincipal principal,
            IWaterProfileService waterProfileService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Water profile validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, updated, err) = await waterProfileService.UpdateProfileAsync(id, request, userId);
            return result switch
            {
                WaterProfileAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", err ?? "Water profile not found.")),
                WaterProfileAccessResult.PresetImmutable => Results.Json(ApiResponse.Fail("PRESET_IMMUTABLE", err ?? "Presets cannot be edited."), statusCode: StatusCodes.Status403Forbidden),
                WaterProfileAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", err ?? "Forbidden."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.Ok(ApiResponse<WaterProfileDto>.Ok(updated!))
            };
        })
        .WithName("UpdateWaterProfile")
        .Produces<ApiResponse<WaterProfileDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IWaterProfileService waterProfileService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, err) = await waterProfileService.DeleteProfileAsync(id, userId);
            return result switch
            {
                WaterProfileAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", err ?? "Water profile not found.")),
                WaterProfileAccessResult.PresetImmutable => Results.Json(ApiResponse.Fail("PRESET_IMMUTABLE", err ?? "Presets cannot be deleted."), statusCode: StatusCodes.Status403Forbidden),
                WaterProfileAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", err ?? "Forbidden."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.Ok(ApiResponse.Ok())
            };
        })
        .WithName("DeleteWaterProfile")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/calculate", (
            [FromBody] CalculateWaterChemistryRequest request,
            IWaterProfileService waterProfileService) =>
        {
            var res = waterProfileService.CalculateChemistry(request);
            return Results.Ok(ApiResponse<WaterChemistryResultDto>.Ok(res));
        })
        .WithName("CalculateWaterChemistry")
        .Produces<ApiResponse<WaterChemistryResultDto>>(StatusCodes.Status200OK);

        group.MapPost("/suggest-salts", (
            [FromBody] SuggestWaterSaltsRequest request,
            IWaterProfileService waterProfileService) =>
        {
            var res = waterProfileService.SuggestSalts(request);
            return Results.Ok(ApiResponse<SuggestWaterSaltsResponse>.Ok(res));
        })
        .WithName("SuggestWaterSalts")
        .Produces<ApiResponse<SuggestWaterSaltsResponse>>(StatusCodes.Status200OK);

        return group;
    }
}