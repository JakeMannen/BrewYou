using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public static class YeastBankEndpoints
{
    public static RouteGroupBuilder MapYeastBankEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/yeast-bank")
            .WithTags("YeastBank")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] string? status,
            ClaimsPrincipal principal,
            IYeastBankService yeastBankService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var items = await yeastBankService.GetEntriesAsync(userId, status);
            return Results.Ok(ApiResponse<List<YeastBankEntryDto>>.Ok(items));
        })
        .WithName("GetYeastBankEntries")
        .Produces<ApiResponse<List<YeastBankEntryDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IYeastBankService yeastBankService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var item = await yeastBankService.GetEntryByIdAsync(id, userId);
            if (item is null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Yeast bank entry not found."));
            }

            return Results.Ok(ApiResponse<YeastBankEntryDto>.Ok(item));
        })
        .WithName("GetYeastBankEntryById")
        .Produces<ApiResponse<YeastBankEntryDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            [FromBody] CreateYeastBankEntryRequest request,
            IValidator<CreateYeastBankEntryRequest> validator,
            ClaimsPrincipal principal,
            IYeastBankService yeastBankService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Yeast bank validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, entry, err) = await yeastBankService.CreateEntryAsync(request, userId);
            if (result == YeastBankAccessResult.NotFound || entry is null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", err ?? "Referenced entity not found."));
            }

            return Results.Created($"/api/v1/yeast-bank/{entry.Id}", ApiResponse<YeastBankEntryDto>.Ok(entry));
        })
        .WithName("CreateYeastBankEntry")
        .Produces<ApiResponse<YeastBankEntryDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateYeastBankEntryRequest request,
            IValidator<UpdateYeastBankEntryRequest> validator,
            ClaimsPrincipal principal,
            IYeastBankService yeastBankService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Yeast bank validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, updated, err) = await yeastBankService.UpdateEntryAsync(id, request, userId);
            if (result == YeastBankAccessResult.NotFound || updated is null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", err ?? "Yeast bank entry not found."));
            }

            return Results.Ok(ApiResponse<YeastBankEntryDto>.Ok(updated));
        })
        .WithName("UpdateYeastBankEntry")
        .Produces<ApiResponse<YeastBankEntryDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IYeastBankService yeastBankService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await yeastBankService.DeleteEntryAsync(id, userId);
            if (result == YeastBankAccessResult.NotFound)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Yeast bank entry not found."));
            }

            return Results.Ok(ApiResponse.Ok());
        })
        .WithName("DeleteYeastBankEntry")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/harvest/{batchId:guid}", async (
            Guid batchId,
            [FromBody] HarvestYeastRequest request,
            IValidator<HarvestYeastRequest> validator,
            ClaimsPrincipal principal,
            IYeastBankService yeastBankService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Yeast harvest validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, entry, err) = await yeastBankService.HarvestFromBatchAsync(batchId, request, userId);
            if (result == YeastBankAccessResult.NotFound || entry is null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", err ?? "Batch not found."));
            }

            return Results.Created($"/api/v1/yeast-bank/{entry.Id}", ApiResponse<YeastBankEntryDto>.Ok(entry));
        })
        .WithName("HarvestYeastFromBatch")
        .Produces<ApiResponse<YeastBankEntryDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/calculate-pitch", async (
            [FromBody] CalculateYeastPitchRequest request,
            IValidator<CalculateYeastPitchRequest> validator,
            ClaimsPrincipal principal,
            IYeastBankService yeastBankService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Pitch calculation validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var plan = await yeastBankService.CalculatePitchPlanAsync(request, userId);
            return Results.Ok(ApiResponse<YeastPitchPlanDto>.Ok(plan));
        })
        .WithName("CalculateYeastPitchPlan")
        .Produces<ApiResponse<YeastPitchPlanDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        return group;
    }
}