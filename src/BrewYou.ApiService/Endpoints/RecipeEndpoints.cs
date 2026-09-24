using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public record RecipeIngredientInputDto(
    Guid IngredientId,
    decimal Amount,
    string Unit,
    int? DurationMinutes,
    IngredientUsage Usage,
    string? Notes,
    string? Form = null
);

public record RecipeIngredientOutputDto(
    Guid Id,
    Guid IngredientId,
    string IngredientName,
    IngredientType IngredientType,
    decimal Amount,
    string Unit,
    int? DurationMinutes,
    IngredientUsage Usage,
    string? Notes,
    decimal? PotentialGravity = null,
    decimal? ColorSrm = null,
    decimal? AlphaAcidPercent = null,
    decimal? AttenuationPercent = null,
    string? Form = null
);


public record CalculateRecipeRequest(
    decimal BatchSizeLiters,
    decimal EfficiencyPercent,
    int BoilTimeMinutes,
    List<RecipeIngredientInputDto> Ingredients
);

public record CalculateRecipeResponse(
    decimal OriginalGravity,
    decimal FinalGravity,
    decimal AlcoholByVolume,
    decimal BitternessIbu,
    decimal ColorSrm
);

public record RecipeMashStepInputDto(
    int StepOrder,
    string Name,
    MashStepType Type,
    decimal TemperatureC,
    int DurationMinutes,
    int? RampTimeMinutes = null,
    decimal? InfuseAmountLiters = null,
    string? Notes = null
);

public record RecipeMashStepOutputDto(
    Guid Id,
    Guid RecipeId,
    int StepOrder,
    string Name,
    MashStepType Type,
    decimal TemperatureC,
    int DurationMinutes,
    int? RampTimeMinutes,
    decimal? InfuseAmountLiters,
    string? Notes
);

public record RecipeFermentationStepInputDto(
    int StepOrder,
    string Name,
    FermentationStepType Type,
    decimal TargetTemperatureC,
    int DurationDays,
    int? RampTimeHours = null,
    decimal? TriggerGravity = null,
    string? Notes = null
);

public record RecipeFermentationStepOutputDto(
    Guid Id,
    Guid RecipeId,
    int StepOrder,
    string Name,
    FermentationStepType Type,
    decimal TargetTemperatureC,
    int DurationDays,
    int? RampTimeHours,
    decimal? TriggerGravity,
    string? Notes
);

public record CreateRecipeRequest(
    string Name,
    string? Description,
    string? BeerStyle,
    decimal BatchSizeLiters,
    int BoilTimeMinutes,
    decimal EfficiencyPercent,
    bool IsPublic,
    List<RecipeIngredientInputDto> Ingredients,
    List<RecipeMashStepInputDto>? MashSteps = null,
    List<RecipeFermentationStepInputDto>? FermentationSteps = null
);

public record UpdateRecipeRequest(
    string Name,
    string? Description,
    string? BeerStyle,
    decimal BatchSizeLiters,
    int BoilTimeMinutes,
    decimal EfficiencyPercent,
    bool IsPublic,
    List<RecipeIngredientInputDto> Ingredients,
    List<RecipeMashStepInputDto>? MashSteps = null,
    List<RecipeFermentationStepInputDto>? FermentationSteps = null
);

public record RecipeSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string BeerStyle,
    decimal BatchSizeLiters,
    decimal OriginalGravity,
    decimal FinalGravity,
    decimal AlcoholByVolume,
    decimal BitternessIbu,
    decimal ColorSrm,
    bool IsPublic,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? AuthorName = null,
    bool IsOwner = true
);

public record RecipeDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string BeerStyle,
    decimal BatchSizeLiters,
    int BoilTimeMinutes,
    decimal EfficiencyPercent,
    decimal OriginalGravity,
    decimal FinalGravity,
    decimal AlcoholByVolume,
    decimal BitternessIbu,
    decimal ColorSrm,
    bool IsPublic,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<RecipeIngredientOutputDto> Ingredients,
    List<RecipeMashStepOutputDto> MashSteps,
    List<RecipeFermentationStepOutputDto> FermentationSteps,
    string? AuthorName = null,
    bool IsOwner = true
);

public record RecipeNameCheckResponse(bool Exists);

public static class RecipeEndpoints
{
    public static RouteGroupBuilder MapRecipeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/recipes")
            .WithTags("Recipes");

        // Public recipe preview/calculation endpoint
        group.MapPost("/calculate", async (
            [FromBody] CalculateRecipeRequest request,
            IValidator<CalculateRecipeRequest> validator,
            IRecipeService recipeService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Invalid recipe calculation request.", details));
            }

            var result = await recipeService.CalculateRecipeAsync(request);
            return Results.Ok(ApiResponse<CalculateRecipeResponse>.Ok(result));
        })
        .WithName("CalculateRecipe")
        .Produces<ApiResponse<CalculateRecipeResponse>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        // Water volume calculation endpoint
        group.MapPost("/calculate-water", async (
            [FromBody] CalculateWaterVolumeRequest request,
            IValidator<CalculateWaterVolumeRequest> validator) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Invalid water volume calculation request.", details));
            }

            var input = new VolumeCalculationInput(
                TargetBatchSizeLiters: request.BatchSizeLiters,
                BoilTimeMinutes: request.BoilTimeMinutes,
                TotalGrainWeightKg: request.TotalGrainWeightKg,
                BoilOffRatePerHour: request.BoilOffRatePerHourLiters ?? 3.0m,
                GrainAbsorptionRate: request.GrainAbsorptionRateLPerKg ?? 0.96m,
                KettleTrubLossLiters: request.KettleTrubLossLiters ?? 1.5m,
                FermenterLossLiters: request.FermenterLossLiters ?? 1.5m,
                MashTunDeadSpaceLiters: request.MashTunDeadSpaceLiters ?? 0.0m,
                CoolingShrinkagePercent: request.CoolingShrinkagePercent ?? 4.0m,
                PackagingLossLiters: request.PackagingLossLiters ?? 0.5m,
                MashThicknessLitersPerKg: request.MashThicknessLitersPerKg ?? 3.0m
            );

            var result = VolumeCalculator.CalculateWaterRequirements(input);

            var response = new CalculateWaterVolumeResponse(
                StrikeWaterLiters: result.StrikeWaterLiters,
                SpargeWaterLiters: result.SpargeWaterLiters,
                TotalWaterLiters: result.TotalWaterLiters,
                EstimatedPreBoilVolumeLiters: result.TargetPreBoilVolumeLiters,
                EstimatedPostBoilVolumeLiters: result.TargetPostBoilVolumeLiters,
                EstimatedIntoFermenterVolumeLiters: result.TargetFermenterVolumeLiters,
                EstimatedPackagedVolumeLiters: result.TargetPackagedVolumeLiters,
                GrainAbsorptionLossLiters: result.GrainAbsorptionLossLiters,
                BoilOffLossLiters: result.BoilOffLossLiters,
                KettleTrubLossLiters: result.KettleTrubLossLiters,
                ShrinkageLossLiters: result.ShrinkageLossLiters,
                FermenterLossLiters: result.FermenterLossLiters,
                PackagingLossLiters: result.PackagingLossLiters
            );

            return Results.Ok(ApiResponse<CalculateWaterVolumeResponse>.Ok(response));
        })
        .WithName("CalculateWaterVolume")
        .Produces<ApiResponse<CalculateWaterVolumeResponse>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/", async (
            [FromQuery] string? scope,
            [FromQuery] int? page,
            [FromQuery] int? limit,
            ClaimsPrincipal principal,
            IRecipeService recipeService) =>
        {
            if (!string.IsNullOrWhiteSpace(scope))
            {
                var normalized = scope.Trim().ToLowerInvariant();
                if (normalized is not ("all" or "mine" or "shared"))
                {
                    return Results.BadRequest(ApiResponse.Fail(
                        "VALIDATION_ERROR",
                        "Invalid recipe scope. Permitted values are 'all', 'mine', or 'shared'.",
                        new List<ApiErrorDetail>
                        {
                            new("scope", "Must be one of: 'all', 'mine', 'shared'")
                        }));
                }
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var (items, pagination) = await recipeService.GetRecipesAsync(userId, page ?? 1, limit ?? 20, scope);

            return Results.Ok(ApiResponse<List<RecipeSummaryDto>>.Ok(items, pagination));
        })
        .RequireAuthorization()
        .WithName("GetRecipes")
        .Produces<ApiResponse<List<RecipeSummaryDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/check-name", async (
            [FromQuery] string? name,
            ClaimsPrincipal principal,
            IRecipeService recipeService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.Ok(ApiResponse<RecipeNameCheckResponse>.Ok(new RecipeNameCheckResponse(false)));
            }

            var exists = await recipeService.RecipeNameExistsAsync(name, userId);
            return Results.Ok(ApiResponse<RecipeNameCheckResponse>.Ok(new RecipeNameCheckResponse(exists)));
        })
        .RequireAuthorization()
        .WithName("CheckRecipeName")
        .Produces<ApiResponse<RecipeNameCheckResponse>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IRecipeService recipeService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var (result, recipe) = await recipeService.GetRecipeByIdAsync(id, userId);

            return result switch
            {
                RecipeAccessResult.Success => Results.Ok(ApiResponse<RecipeDetailDto>.Ok(recipe!)),
                RecipeAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Recipe not found.")),
                RecipeAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", "You do not have access to this recipe."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("GetRecipeById")
        .Produces<ApiResponse<RecipeDetailDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            [FromBody] CreateRecipeRequest request,
            IValidator<CreateRecipeRequest> validator,
            ClaimsPrincipal principal,
            IRecipeService recipeService) =>
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

            var (result, recipe, error) = await recipeService.CreateRecipeAsync(request, userId);
            return result switch
            {
                RecipeAccessResult.Success => Results.Created($"/api/v1/recipes/{recipe!.Id}", ApiResponse<RecipeDetailDto>.Ok(recipe)),
                RecipeAccessResult.DuplicateName => Results.Conflict(ApiResponse.Fail("DUPLICATE_NAME", error ?? "A recipe with this name already exists.")),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("CreateRecipe")
        .Produces<ApiResponse<RecipeDetailDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateRecipeRequest request,
            IValidator<UpdateRecipeRequest> validator,
            ClaimsPrincipal principal,
            IRecipeService recipeService) =>
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
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Validation failed for recipe update.", details));
            }

            var (result, recipe, error) = await recipeService.UpdateRecipeAsync(id, request, userId);
            return result switch
            {
                RecipeAccessResult.Success => Results.Ok(ApiResponse<RecipeDetailDto>.Ok(recipe!)),
                RecipeAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Recipe not found.")),
                RecipeAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "You do not have permission to update this recipe."), statusCode: StatusCodes.Status403Forbidden),
                RecipeAccessResult.DuplicateName => Results.Conflict(ApiResponse.Fail("DUPLICATE_NAME", error ?? "A recipe with this name already exists.")),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("UpdateRecipe")
        .Produces<ApiResponse<RecipeDetailDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IRecipeService recipeService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await recipeService.DeleteRecipeAsync(id, userId);
            return result switch
            {
                RecipeAccessResult.Success => Results.Ok(ApiResponse.Ok()),
                RecipeAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Recipe not found.")),
                RecipeAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", "You do not have permission to delete this recipe."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("DeleteRecipe")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        return group;
    }
}