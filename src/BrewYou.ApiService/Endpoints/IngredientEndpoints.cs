using System.Security.Claims;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Endpoints;

public record CreateIngredientRequest(
    string Name,
    IngredientType Type,
    decimal? PotentialGravity,
    decimal? ColorSrm,
    decimal? AlphaAcidPercent,
    decimal? AttenuationPercent,
    string? Description
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
    bool IsCatalogItem
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
            BrewYouDbContext db) =>
        {
            var query = db.Ingredients.AsNoTracking();

            if (type.HasValue)
            {
                query = query.Where(i => i.Type == type.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(i => i.Name.ToLower().Contains(term));
            }

            var items = await query
                .OrderBy(i => i.Name)
                .Select(i => new IngredientDto(
                    i.Id,
                    i.Name,
                    i.Type,
                    i.PotentialGravity,
                    i.ColorSrm,
                    i.AlphaAcidPercent,
                    i.AttenuationPercent,
                    i.Description,
                    i.IsCatalogItem
                ))
                .ToListAsync();

            return Results.Ok(items);
        })
        .WithName("GetIngredients")
        .Produces<List<IngredientDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (Guid id, BrewYouDbContext db) =>
        {
            var ingredient = await db.Ingredients.FindAsync(id);
            if (ingredient == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new IngredientDto(
                ingredient.Id,
                ingredient.Name,
                ingredient.Type,
                ingredient.PotentialGravity,
                ingredient.ColorSrm,
                ingredient.AlphaAcidPercent,
                ingredient.AttenuationPercent,
                ingredient.Description,
                ingredient.IsCatalogItem
            ));
        })
        .WithName("GetIngredientById")
        .Produces<IngredientDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            [FromBody] CreateIngredientRequest request,
            ClaimsPrincipal principal,
            BrewYouDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Ingredient name is required." });
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

            var ingredient = new Ingredient
            {
                Name = request.Name,
                Type = request.Type,
                PotentialGravity = request.PotentialGravity,
                ColorSrm = request.ColorSrm,
                AlphaAcidPercent = request.AlphaAcidPercent,
                AttenuationPercent = request.AttenuationPercent,
                Description = request.Description,
                IsCatalogItem = false,
                CreatedByUserId = userId
            };

            db.Ingredients.Add(ingredient);
            await db.SaveChangesAsync();

            var dto = new IngredientDto(
                ingredient.Id,
                ingredient.Name,
                ingredient.Type,
                ingredient.PotentialGravity,
                ingredient.ColorSrm,
                ingredient.AlphaAcidPercent,
                ingredient.AttenuationPercent,
                ingredient.Description,
                ingredient.IsCatalogItem
            );

            return Results.Created($"/api/v1/ingredients/{ingredient.Id}", dto);
        })
        .RequireAuthorization()
        .WithName("CreateIngredient")
        .Produces<IngredientDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        return group;
    }
}
