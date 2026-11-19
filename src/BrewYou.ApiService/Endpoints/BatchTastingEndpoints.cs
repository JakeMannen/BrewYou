using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public static class BatchTastingEndpoints
{
    public static RouteGroupBuilder MapBatchTastingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1")
            .WithTags("Tastings")
            .RequireAuthorization();

        group.MapGet("/tastings/off-flavors", () =>
        {
            var items = OffFlavorCatalog.All
                .Select(d => new OffFlavorDescriptorDto(d.Key, d.DisplayName, d.SensoryCue, d.LikelyCauses, d.RemediationFixes))
                .ToList();
            return Results.Ok(ApiResponse<List<OffFlavorDescriptorDto>>.Ok(items));
        })
        .WithName("GetOffFlavorCatalog")
        .Produces<ApiResponse<List<OffFlavorDescriptorDto>>>(StatusCodes.Status200OK);

        group.MapGet("/batches/{batchId:guid}/tastings", async (
            Guid batchId,
            ClaimsPrincipal principal,
            IBatchTastingService tastingService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, items) = await tastingService.GetTastingsAsync(batchId, userId);
            if (result == BatchTastingAccessResult.NotFound || items is null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Batch not found."));
            }

            return Results.Ok(ApiResponse<List<BatchTastingDto>>.Ok(items));
        })
        .WithName("GetBatchTastings")
        .Produces<ApiResponse<List<BatchTastingDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/batches/{batchId:guid}/tastings", async (
            Guid batchId,
            [FromBody] CreateBatchTastingRequest request,
            IValidator<CreateBatchTastingRequest> validator,
            ClaimsPrincipal principal,
            IBatchTastingService tastingService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Tasting note validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, tasting) = await tastingService.AddTastingAsync(batchId, request, userId);
            if (result == BatchTastingAccessResult.NotFound || tasting is null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Batch not found."));
            }

            return Results.Created($"/api/v1/batches/{batchId}/tastings/{tasting.Id}", ApiResponse<BatchTastingDto>.Ok(tasting));
        })
        .WithName("CreateBatchTasting")
        .RequireRateLimiting("batch-write")
        .Produces<ApiResponse<BatchTastingDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPut("/batches/{batchId:guid}/tastings/{tastingId:guid}", async (
            Guid batchId,
            Guid tastingId,
            [FromBody] UpdateBatchTastingRequest request,
            IValidator<UpdateBatchTastingRequest> validator,
            ClaimsPrincipal principal,
            IBatchTastingService tastingService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Tasting note validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, tasting) = await tastingService.UpdateTastingAsync(batchId, tastingId, request, userId);
            if (result == BatchTastingAccessResult.NotFound || tasting is null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Batch or tasting entry not found."));
            }

            return Results.Ok(ApiResponse<BatchTastingDto>.Ok(tasting));
        })
        .WithName("UpdateBatchTasting")
        .RequireRateLimiting("batch-write")
        .Produces<ApiResponse<BatchTastingDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/batches/{batchId:guid}/tastings/{tastingId:guid}", async (
            Guid batchId,
            Guid tastingId,
            ClaimsPrincipal principal,
            IBatchTastingService tastingService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await tastingService.DeleteTastingAsync(batchId, tastingId, userId);
            if (result == BatchTastingAccessResult.NotFound)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Batch or tasting entry not found."));
            }

            return Results.Ok(ApiResponse.Ok());
        })
        .WithName("DeleteBatchTasting")
        .RequireRateLimiting("batch-write")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        return group;
    }
}