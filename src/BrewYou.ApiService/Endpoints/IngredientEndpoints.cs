using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public record CreateIngredientRequest(
    string Name,
    IngredientType Type,
    decimal? PotentialGravity,
    decimal? ColorSrm,
    decimal? AlphaAcidPercent,
    decimal? AttenuationPercent,
    string? Description,
    decimal? InitialStock = null,
    string? StockUnit = null,
    string? Form = null
);

public record UpdateIngredientStockRequest(
    decimal Amount,
    string? Unit = null
);

public record IngredientDto(
    Guid Id,
    string Name,
    IngredientType Type,
    decimal? PotentialGravity,
    decimal? ColorSrm,
    decimal? AlphaAcidPercent,
    decimal? AttenuationPercent,
    string? Description,
    bool IsCatalogItem,
    decimal StockAmount,
    string StockUnit,
    bool IsInStock,
    string? Form = null
);

public record IngredientUsageRecipeDto(
    Guid Id,
    string Name
);

public record IngredientUsageDto(
    Guid IngredientId,
    string IngredientName,
    int RecipeCount,
    List<IngredientUsageRecipeDto> Recipes
);

public static class IngredientEndpoints
{
    public static RouteGroupBuilder MapIngredientEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/ingredients")
            .WithTags("Ingredients");

        group.MapGet("/", async (
            [FromQuery] IngredientType? type,
            [FromQuery] string? search,
            [FromQuery] bool? inStock,
            [FromQuery] int? page,
            [FromQuery] int? limit,
            ClaimsPrincipal principal,
            IIngredientService ingredientService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var (items, pagination) = await ingredientService.GetIngredientsAsync(userId, type, search, page ?? 1, limit ?? 100, inStock);
            return Results.Ok(ApiResponse<List<IngredientDto>>.Ok(items, pagination));
        })
        .WithName("GetIngredients")
        .Produces<ApiResponse<List<IngredientDto>>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IIngredientService ingredientService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var ingredient = await ingredientService.GetIngredientByIdAsync(id, userId);
            if (ingredient == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Ingredient not found."));
            }

            return Results.Ok(ApiResponse<IngredientDto>.Ok(ingredient));
        })
        .WithName("GetIngredientById")
        .Produces<ApiResponse<IngredientDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            [FromBody] CreateIngredientRequest request,
            IValidator<CreateIngredientRequest> validator,
            ClaimsPrincipal principal,
            IIngredientService ingredientService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Validation failed.", details));
            }

            var (result, ingredient, errorMessage) = await ingredientService.CreateIngredientAsync(request, userId);
            if (result == IngredientOperationResult.DuplicateName)
            {
                return Results.Conflict(ApiResponse.Fail("DUPLICATE_NAME", errorMessage ?? "An ingredient with this name already exists."));
            }

            if (result == IngredientOperationResult.QuotaExceeded)
            {
                return Results.UnprocessableEntity(ApiResponse.Fail("QUOTA_EXCEEDED", errorMessage ?? "Custom ingredient limit reached."));
            }

            return Results.Created($"/api/v1/ingredients/{ingredient!.Id}", ApiResponse<IngredientDto>.Ok(ingredient));
        })
        .RequireAuthorization()
        .RequireRateLimiting("ingredient-creation-limit")
        .WithName("CreateIngredient")
        .Produces<ApiResponse<IngredientDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict)
        .Produces<ApiResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{id:guid}/stock", async (
            Guid id,
            [FromBody] UpdateIngredientStockRequest request,
            IValidator<UpdateIngredientStockRequest> validator,
            ClaimsPrincipal principal,
            IIngredientService ingredientService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Validation failed.", details));
            }

            var (result, ingredient, errorMessage) = await ingredientService.UpdateStockAsync(id, request, userId);
            if (result == IngredientOperationResult.NotFound)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Ingredient not found."));
            }

            return Results.Ok(ApiResponse<IngredientDto>.Ok(ingredient!));
        })
        .RequireAuthorization()
        .WithName("UpdateIngredientStock")
        .Produces<ApiResponse<IngredientDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/usage", async (
            Guid id,
            ClaimsPrincipal principal,
            IIngredientService ingredientService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, usage, errorMessage) = await ingredientService.GetIngredientUsageAsync(id, userId);
            return result switch
            {
                IngredientOperationResult.Success => Results.Ok(ApiResponse<IngredientUsageDto>.Ok(usage!)),
                IngredientOperationResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Ingredient not found.")),
                IngredientOperationResult.CatalogItemImmutable => Results.BadRequest(ApiResponse.Fail("CATALOG_ITEM_CANNOT_BE_DELETED", errorMessage ?? "Catalog ingredients cannot be deleted.")),
                IngredientOperationResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", errorMessage ?? "You do not have permission to access this ingredient."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("GetIngredientUsage")
        .Produces<ApiResponse<IngredientUsageDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IIngredientService ingredientService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, errorMessage) = await ingredientService.DeleteIngredientAsync(id, userId);
            return result switch
            {
                IngredientOperationResult.Success => Results.Ok(ApiResponse.Ok()),
                IngredientOperationResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Ingredient not found.")),
                IngredientOperationResult.CatalogItemImmutable => Results.BadRequest(ApiResponse.Fail("CATALOG_ITEM_CANNOT_BE_DELETED", errorMessage ?? "Catalog ingredients cannot be deleted.")),
                IngredientOperationResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", errorMessage ?? "You do not have permission to delete this ingredient."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("DeleteIngredient")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        return group;
    }
}