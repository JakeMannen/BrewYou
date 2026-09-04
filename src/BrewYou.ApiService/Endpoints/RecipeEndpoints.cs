using System.Security.Claims;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Endpoints;

public record RecipeIngredientInputDto(
    Guid IngredientId,
    decimal Amount,
    string Unit,
    int? DurationMinutes,
    IngredientUsage Usage,
    string? Notes
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
    string? Notes
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

public record CreateRecipeRequest(
    string Name,
    string? Description,
    string BeerStyle,
    decimal BatchSizeLiters,
    int BoilTimeMinutes,
    decimal EfficiencyPercent,
    bool IsPublic,
    List<RecipeIngredientInputDto> Ingredients
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
    DateTime UpdatedAt
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
    List<RecipeIngredientOutputDto> Ingredients
);

public static class RecipeEndpoints
{
    public static RouteGroupBuilder MapRecipeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/recipes")
            .WithTags("Recipes");

        // Public recipe preview/calculation endpoint
        group.MapPost("/calculate", async (
            [FromBody] CalculateRecipeRequest request,
            BrewYouDbContext db) =>
        {
            var ingredientIds = request.Ingredients.Select(i => i.IngredientId).Distinct().ToList();
            var dbIngredients = await db.Ingredients
                .Where(i => ingredientIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id);

            var recipeIngredients = request.Ingredients.Select(input => new RecipeIngredient
            {
                IngredientId = input.IngredientId,
                Ingredient = dbIngredients.GetValueOrDefault(input.IngredientId),
                Amount = input.Amount,
                Unit = input.Unit,
                DurationMinutes = input.DurationMinutes,
                Usage = input.Usage,
                Notes = input.Notes
            }).ToList();

            var calc = BrewingCalculator.Calculate(
                request.BatchSizeLiters,
                request.EfficiencyPercent,
                request.BoilTimeMinutes,
                recipeIngredients
            );

            return Results.Ok(new CalculateRecipeResponse(
                calc.OriginalGravity,
                calc.FinalGravity,
                calc.AlcoholByVolume,
                calc.BitternessIbu,
                calc.ColorSrm
            ));
        })
        .WithName("CalculateRecipe")
        .Produces<CalculateRecipeResponse>(StatusCodes.Status200OK);

        group.MapGet("/", async (
            ClaimsPrincipal principal,
            BrewYouDbContext db) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

            var recipes = await db.Recipes
                .AsNoTracking()
                .Where(r => r.IsPublic || (userId != null && r.UserId == userId))
                .OrderByDescending(r => r.UpdatedAt)
                .Select(r => new RecipeSummaryDto(
                    r.Id,
                    r.Name,
                    r.Description,
                    r.BeerStyle,
                    r.BatchSizeLiters,
                    r.OriginalGravity,
                    r.FinalGravity,
                    r.AlcoholByVolume,
                    r.BitternessIbu,
                    r.ColorSrm,
                    r.IsPublic,
                    r.CreatedAt,
                    r.UpdatedAt
                ))
                .ToListAsync();

            return Results.Ok(recipes);
        })
        .WithName("GetRecipes")
        .Produces<List<RecipeSummaryDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            BrewYouDbContext db) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

            var recipe = await db.Recipes
                .AsNoTracking()
                .Include(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe == null)
            {
                return Results.NotFound();
            }

            if (!recipe.IsPublic && recipe.UserId != userId)
            {
                return Results.Forbid();
            }

            var dto = new RecipeDetailDto(
                recipe.Id,
                recipe.Name,
                recipe.Description,
                recipe.BeerStyle,
                recipe.BatchSizeLiters,
                recipe.BoilTimeMinutes,
                recipe.EfficiencyPercent,
                recipe.OriginalGravity,
                recipe.FinalGravity,
                recipe.AlcoholByVolume,
                recipe.BitternessIbu,
                recipe.ColorSrm,
                recipe.IsPublic,
                recipe.CreatedAt,
                recipe.UpdatedAt,
                recipe.Ingredients.Select(ri => new RecipeIngredientOutputDto(
                    ri.Id,
                    ri.IngredientId,
                    ri.Ingredient?.Name ?? "Unknown",
                    ri.Ingredient?.Type ?? IngredientType.Other,
                    ri.Amount,
                    ri.Unit,
                    ri.DurationMinutes,
                    ri.Usage,
                    ri.Notes
                )).ToList()
            );

            return Results.Ok(dto);
        })
        .WithName("GetRecipeById")
        .Produces<RecipeDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            [FromBody] CreateRecipeRequest request,
            ClaimsPrincipal principal,
            BrewYouDbContext db) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Recipe name is required." });
            }

            var ingredientIds = request.Ingredients.Select(i => i.IngredientId).Distinct().ToList();
            var dbIngredients = await db.Ingredients
                .Where(i => ingredientIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id);

            var recipeIngredients = request.Ingredients.Select(input => new RecipeIngredient
            {
                IngredientId = input.IngredientId,
                Ingredient = dbIngredients.GetValueOrDefault(input.IngredientId),
                Amount = input.Amount,
                Unit = input.Unit,
                DurationMinutes = input.DurationMinutes,
                Usage = input.Usage,
                Notes = input.Notes
            }).ToList();

            var calc = BrewingCalculator.Calculate(
                request.BatchSizeLiters,
                request.EfficiencyPercent,
                request.BoilTimeMinutes,
                recipeIngredients
            );

            var recipe = new Recipe
            {
                UserId = userId,
                Name = request.Name,
                Description = request.Description,
                BeerStyle = request.BeerStyle,
                BatchSizeLiters = request.BatchSizeLiters,
                BoilTimeMinutes = request.BoilTimeMinutes,
                EfficiencyPercent = request.EfficiencyPercent,
                OriginalGravity = calc.OriginalGravity,
                FinalGravity = calc.FinalGravity,
                AlcoholByVolume = calc.AlcoholByVolume,
                BitternessIbu = calc.BitternessIbu,
                ColorSrm = calc.ColorSrm,
                IsPublic = request.IsPublic,
                Ingredients = recipeIngredients
            };

            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();

            var dto = new RecipeDetailDto(
                recipe.Id,
                recipe.Name,
                recipe.Description,
                recipe.BeerStyle,
                recipe.BatchSizeLiters,
                recipe.BoilTimeMinutes,
                recipe.EfficiencyPercent,
                recipe.OriginalGravity,
                recipe.FinalGravity,
                recipe.AlcoholByVolume,
                recipe.BitternessIbu,
                recipe.ColorSrm,
                recipe.IsPublic,
                recipe.CreatedAt,
                recipe.UpdatedAt,
                recipe.Ingredients.Select(ri => new RecipeIngredientOutputDto(
                    ri.Id,
                    ri.IngredientId,
                    ri.Ingredient?.Name ?? "Unknown",
                    ri.Ingredient?.Type ?? IngredientType.Other,
                    ri.Amount,
                    ri.Unit,
                    ri.DurationMinutes,
                    ri.Usage,
                    ri.Notes
                )).ToList()
            );

            return Results.Created($"/api/v1/recipes/{recipe.Id}", dto);
        })
        .RequireAuthorization()
        .WithName("CreateRecipe")
        .Produces<RecipeDetailDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            BrewYouDbContext db) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id);
            if (recipe == null)
            {
                return Results.NotFound();
            }

            if (recipe.UserId != userId)
            {
                return Results.Forbid();
            }

            db.Recipes.Remove(recipe);
            await db.SaveChangesAsync();

            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("DeleteRecipe")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        return group;
    }
}
