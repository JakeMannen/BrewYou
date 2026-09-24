using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Services;

public enum IngredientOperationResult
{
    Success,
    DuplicateName,
    QuotaExceeded,
    NotFound,
    Forbidden,
    CatalogItemImmutable
}

public interface IIngredientService
{
    Task<(List<IngredientDto> Items, PaginationMeta Pagination)> GetIngredientsAsync(
        string? userId,
        IngredientType? type,
        string? search,
        int page,
        int limit,
        bool? inStock = null);

    Task<IngredientDto?> GetIngredientByIdAsync(Guid id, string? userId);

    Task<(IngredientOperationResult Result, IngredientDto? Ingredient, string? ErrorMessage)> CreateIngredientAsync(
        CreateIngredientRequest request,
        string userId);

    Task<(IngredientOperationResult Result, IngredientDto? Ingredient, string? ErrorMessage)> UpdateStockAsync(
        Guid id,
        UpdateIngredientStockRequest request,
        string userId);

    Task<(IngredientOperationResult Result, IngredientUsageDto? Usage, string? ErrorMessage)> GetIngredientUsageAsync(
        Guid id,
        string userId);

    Task<(IngredientOperationResult Result, string? ErrorMessage)> DeleteIngredientAsync(
        Guid id,
        string userId);
}

public class IngredientService(BrewYouDbContext db) : IIngredientService
{
    public const int MaxCustomIngredientsPerUser = 150;

    public async Task<(List<IngredientDto> Items, PaginationMeta Pagination)> GetIngredientsAsync(
        string? userId,
        IngredientType? type,
        string? search,
        int page,
        int limit,
        bool? inStock = null)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 250);

        var query = from i in db.Ingredients.AsNoTracking()
                    where i.IsCatalogItem || (userId != null && i.CreatedByUserId == userId)
                    join s in db.IngredientStocks.AsNoTracking().Where(st => st.UserId == userId)
                        on i.Id equals s.IngredientId into stockJoin
                    from st in stockJoin.DefaultIfEmpty()
                    select new
                    {
                        Ingredient = i,
                        Stock = st
                    };

        if (type.HasValue)
        {
            query = query.Where(x => x.Ingredient.Type == type.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.Ingredient.Name.ToLower().Contains(term));
        }

        if (inStock == true)
        {
            query = query.Where(x => x.Stock != null && x.Stock.Amount > 0);
        }

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)limit);

        var rawItems = await query
            .OrderBy(x => x.Ingredient.Name)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var items = rawItems.Select(x =>
        {
            var i = x.Ingredient;
            var defaultUnit = i.Type == IngredientType.Fermentable ? "kg" : "g";
            var stockAmount = x.Stock?.Amount ?? 0.0m;
            var stockUnit = x.Stock != null && !string.IsNullOrWhiteSpace(x.Stock.Unit) ? x.Stock.Unit : defaultUnit;
            return new IngredientDto(
                i.Id,
                i.Name,
                i.Type,
                i.PotentialGravity,
                i.ColorSrm,
                i.AlphaAcidPercent,
                i.AttenuationPercent,
                i.Description,
                i.IsCatalogItem,
                stockAmount,
                stockUnit,
                stockAmount > 0,
                i.Form
            );
        }).ToList();

        var meta = new PaginationMeta(page, limit, total, totalPages);
        return (items, meta);
    }

    public async Task<IngredientDto?> GetIngredientByIdAsync(Guid id, string? userId)
    {
        var ingredient = await db.Ingredients.FindAsync(id);
        if (ingredient == null)
        {
            return null;
        }

        if (!ingredient.IsCatalogItem && (userId == null || ingredient.CreatedByUserId != userId))
        {
            return null;
        }

        var stock = userId != null
            ? await db.IngredientStocks.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId && s.IngredientId == id)
            : null;

        var defaultUnit = ingredient.Type == IngredientType.Fermentable ? "kg" : "g";
        var stockAmount = stock?.Amount ?? 0.0m;
        var stockUnit = stock != null && !string.IsNullOrWhiteSpace(stock.Unit) ? stock.Unit : defaultUnit;

        return new IngredientDto(
            ingredient.Id,
            ingredient.Name,
            ingredient.Type,
            ingredient.PotentialGravity,
            ingredient.ColorSrm,
            ingredient.AlphaAcidPercent,
            ingredient.AttenuationPercent,
            ingredient.Description,
            ingredient.IsCatalogItem,
            stockAmount,
            stockUnit,
            stockAmount > 0,
            ingredient.Form
        );
    }

    public async Task<(IngredientOperationResult Result, IngredientDto? Ingredient, string? ErrorMessage)> CreateIngredientAsync(
        CreateIngredientRequest request,
        string userId)
    {
        var count = await db.Ingredients.CountAsync(i => i.CreatedByUserId == userId);
        if (count >= MaxCustomIngredientsPerUser)
        {
            return (IngredientOperationResult.QuotaExceeded, null, $"Custom ingredient limit reached (max {MaxCustomIngredientsPerUser} items).");
        }

        var trimmedName = request.Name.Trim();
        var exists = await db.Ingredients.AnyAsync(i =>
            (i.IsCatalogItem || i.CreatedByUserId == userId) &&
            i.Name.ToLower() == trimmedName.ToLower());
        if (exists)
        {
            return (IngredientOperationResult.DuplicateName, null, "An ingredient with this name already exists in your catalog.");
        }

        decimal? potentialGravity = null;
        decimal? colorSrm = null;
        decimal? alphaAcidPercent = null;
        decimal? attenuationPercent = null;

        switch (request.Type)
        {
            case IngredientType.Fermentable:
                potentialGravity = request.PotentialGravity;
                colorSrm = request.ColorSrm;
                break;
            case IngredientType.Hop:
                alphaAcidPercent = request.AlphaAcidPercent;
                break;
            case IngredientType.Yeast:
                attenuationPercent = request.AttenuationPercent;
                break;
            case IngredientType.Other:
                break;
        }

        var ingredient = new Ingredient
        {
            Name = trimmedName,
            Type = request.Type,
            PotentialGravity = potentialGravity,
            ColorSrm = colorSrm,
            AlphaAcidPercent = alphaAcidPercent,
            AttenuationPercent = attenuationPercent,
            Form = string.IsNullOrWhiteSpace(request.Form) ? null : request.Form.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsCatalogItem = false,
            CreatedByUserId = userId
        };

        db.Ingredients.Add(ingredient);

        var defaultUnit = request.Type == IngredientType.Fermentable ? "kg" : "g";
        var stockAmount = request.InitialStock.HasValue && request.InitialStock.Value > 0 ? request.InitialStock.Value : 0.0m;
        var stockUnit = !string.IsNullOrWhiteSpace(request.StockUnit) ? request.StockUnit.Trim() : defaultUnit;

        if (stockAmount > 0)
        {
            var stock = new IngredientStock
            {
                UserId = userId,
                IngredientId = ingredient.Id,
                Amount = stockAmount,
                Unit = stockUnit,
                UpdatedAt = DateTime.UtcNow
            };
            db.IngredientStocks.Add(stock);
        }

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
            ingredient.IsCatalogItem,
            stockAmount,
            stockUnit,
            stockAmount > 0,
            ingredient.Form
        );

        return (IngredientOperationResult.Success, dto, null);
    }

    public async Task<(IngredientOperationResult Result, IngredientDto? Ingredient, string? ErrorMessage)> UpdateStockAsync(
        Guid id,
        UpdateIngredientStockRequest request,
        string userId)
    {
        var ingredient = await db.Ingredients.FindAsync(id);
        if (ingredient == null || (!ingredient.IsCatalogItem && ingredient.CreatedByUserId != userId))
        {
            return (IngredientOperationResult.NotFound, null, "Ingredient not found.");
        }

        var stock = await db.IngredientStocks.FirstOrDefaultAsync(s => s.UserId == userId && s.IngredientId == id);
        var defaultUnit = ingredient.Type == IngredientType.Fermentable ? "kg" : "g";
        var unit = !string.IsNullOrWhiteSpace(request.Unit)
            ? request.Unit.Trim()
            : (stock?.Unit ?? defaultUnit);

        var amount = Math.Max(0.0m, request.Amount);

        if (stock == null)
        {
            stock = new IngredientStock
            {
                UserId = userId,
                IngredientId = id,
                Amount = amount,
                Unit = unit,
                UpdatedAt = DateTime.UtcNow
            };
            db.IngredientStocks.Add(stock);
        }
        else
        {
            stock.Amount = amount;
            stock.Unit = unit;
            stock.UpdatedAt = DateTime.UtcNow;
        }

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
            ingredient.IsCatalogItem,
            stock.Amount,
            stock.Unit,
            stock.Amount > 0,
            ingredient.Form
        );

        return (IngredientOperationResult.Success, dto, null);
    }

    public async Task<(IngredientOperationResult Result, IngredientUsageDto? Usage, string? ErrorMessage)> GetIngredientUsageAsync(
        Guid id,
        string userId)
    {
        var ingredient = await db.Ingredients.FindAsync(id);
        if (ingredient == null)
        {
            return (IngredientOperationResult.NotFound, null, "Ingredient not found.");
        }

        if (ingredient.IsCatalogItem)
        {
            return (IngredientOperationResult.CatalogItemImmutable, null, "Catalog ingredients cannot be deleted.");
        }

        if (ingredient.CreatedByUserId != userId)
        {
            return (IngredientOperationResult.Forbidden, null, "You do not have permission to access this ingredient.");
        }

        var recipes = await db.RecipeIngredients
            .AsNoTracking()
            .Where(ri => ri.IngredientId == id)
            .Select(ri => new IngredientUsageRecipeDto(ri.RecipeId, ri.Recipe != null ? ri.Recipe.Name : "Recipe"))
            .Distinct()
            .ToListAsync();

        var usage = new IngredientUsageDto(
            ingredient.Id,
            ingredient.Name,
            recipes.Count,
            recipes
        );

        return (IngredientOperationResult.Success, usage, null);
    }

    public async Task<(IngredientOperationResult Result, string? ErrorMessage)> DeleteIngredientAsync(
        Guid id,
        string userId)
    {
        var ingredient = await db.Ingredients.FindAsync(id);
        if (ingredient == null)
        {
            return (IngredientOperationResult.NotFound, "Ingredient not found.");
        }

        if (ingredient.IsCatalogItem)
        {
            return (IngredientOperationResult.CatalogItemImmutable, "Catalog ingredients cannot be deleted.");
        }

        if (ingredient.CreatedByUserId != userId)
        {
            return (IngredientOperationResult.Forbidden, "You do not have permission to delete this ingredient.");
        }

        // 1. Find all RecipeIngredients referencing this ingredient
        var affectedRecipeIngredients = await db.RecipeIngredients
            .Where(ri => ri.IngredientId == id)
            .ToListAsync();

        var affectedRecipeIds = affectedRecipeIngredients
            .Select(ri => ri.RecipeId)
            .Distinct()
            .ToList();

        // 2. Remove the RecipeIngredient entries
        if (affectedRecipeIngredients.Count > 0)
        {
            db.RecipeIngredients.RemoveRange(affectedRecipeIngredients);
        }

        // 3. For each affected recipe, recalculate metrics using remaining ingredients
        if (affectedRecipeIds.Count > 0)
        {
            var affectedRecipes = await db.Recipes
                .Include(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
                .Where(r => affectedRecipeIds.Contains(r.Id))
                .ToListAsync();

            foreach (var recipe in affectedRecipes)
            {
                // Remaining ingredients excluding the one being deleted
                var remainingIngredients = recipe.Ingredients
                    .Where(ri => ri.IngredientId != id)
                    .ToList();

                var calc = BrewingCalculator.Calculate(
                    recipe.BatchSizeLiters,
                    recipe.EfficiencyPercent,
                    recipe.BoilTimeMinutes,
                    remainingIngredients
                );

                recipe.OriginalGravity = calc.OriginalGravity;
                recipe.FinalGravity = calc.FinalGravity;
                recipe.AlcoholByVolume = calc.AlcoholByVolume;
                recipe.BitternessIbu = calc.BitternessIbu;
                recipe.ColorSrm = calc.ColorSrm;
                recipe.UpdatedAt = DateTime.UtcNow;
            }
        }

        // 4. Remove any IngredientStock for this ingredient
        var stocks = await db.IngredientStocks
            .Where(s => s.IngredientId == id)
            .ToListAsync();
        if (stocks.Count > 0)
        {
            db.IngredientStocks.RemoveRange(stocks);
        }

        // 5. Remove the ingredient itself
        db.Ingredients.Remove(ingredient);

        await db.SaveChangesAsync();

        return (IngredientOperationResult.Success, null);
    }
}