using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Services;

public enum RecipeAccessResult
{
    Success,
    NotFound,
    Forbidden,
    DuplicateName
}

public interface IRecipeService
{
    Task<(List<RecipeSummaryDto> Items, PaginationMeta Pagination)> GetRecipesAsync(string? userId, int page, int limit, string? scope = null);
    Task<(RecipeAccessResult Result, RecipeDetailDto? Recipe)> GetRecipeByIdAsync(Guid id, string? userId);
    Task<(RecipeAccessResult Result, RecipeDetailDto? Recipe, string? Error)> CreateRecipeAsync(CreateRecipeRequest request, string userId);
    Task<CalculateRecipeResponse> CalculateRecipeAsync(CalculateRecipeRequest request);
    Task<(RecipeAccessResult Result, RecipeDetailDto? Recipe, string? Error)> UpdateRecipeAsync(Guid id, UpdateRecipeRequest request, string userId);
    Task<RecipeAccessResult> DeleteRecipeAsync(Guid id, string userId);
    Task<bool> RecipeNameExistsAsync(string name, string userId);
}

public class RecipeService(BrewYouDbContext db) : IRecipeService
{
    public async Task<(List<RecipeSummaryDto> Items, PaginationMeta Pagination)> GetRecipesAsync(
        string? userId,
        int page,
        int limit,
        string? scope = null)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = db.Recipes.AsNoTracking();

        var normalizedScope = scope?.Trim().ToLowerInvariant();
        switch (normalizedScope)
        {
            case "mine":
                query = string.IsNullOrEmpty(userId)
                    ? query.Where(_ => false)
                    : query.Where(r => r.UserId == userId);
                break;
            case "shared":
                query = string.IsNullOrEmpty(userId)
                    ? query.Where(r => r.IsPublic)
                    : query.Where(r => r.IsPublic && r.UserId != userId);
                break;
            case "all":
            default:
                query = string.IsNullOrEmpty(userId)
                    ? query.Where(r => r.IsPublic)
                    : query.Where(r => r.IsPublic || r.UserId == userId);
                break;
        }

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)limit);

        var items = await query
            .OrderByDescending(r => r.UpdatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
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
                r.UpdatedAt,
                r.User != null && !string.IsNullOrEmpty(r.User.DisplayName)
                    ? r.User.DisplayName
                    : "Community Brewer",
                userId != null && r.UserId == userId
            ))
            .ToListAsync();

        var meta = new PaginationMeta(page, limit, total, totalPages);
        return (items, meta);
    }

    public async Task<(RecipeAccessResult Result, RecipeDetailDto? Recipe)> GetRecipeByIdAsync(Guid id, string? userId)
    {
        var recipe = await db.Recipes
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
            .Include(r => r.MashSteps)
            .Include(r => r.FermentationSteps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return (RecipeAccessResult.NotFound, null);
        }

        if (!recipe.IsPublic && recipe.UserId != userId)
        {
            return (RecipeAccessResult.NotFound, null);
        }

        var dto = MapToDetailDto(recipe, userId);
        return (RecipeAccessResult.Success, dto);
    }

    public async Task<(RecipeAccessResult Result, RecipeDetailDto? Recipe, string? Error)> CreateRecipeAsync(CreateRecipeRequest request, string userId)
    {
        var trimmedName = request.Name.Trim();
        var nameExists = await db.Recipes.AnyAsync(r =>
            r.UserId == userId && r.Name.ToLower() == trimmedName.ToLower());

        if (nameExists)
        {
            return (RecipeAccessResult.DuplicateName, null, $"A recipe named '{trimmedName}' already exists.");
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
            Notes = input.Notes,
            Form = input.Form
        }).ToList();

        var mashSteps = request.MashSteps != null && request.MashSteps.Count > 0
            ? request.MashSteps
                .OrderBy(s => s.StepOrder)
                .Select(s => new RecipeMashStep
                {
                    StepOrder = s.StepOrder,
                    Name = s.Name.Trim(),
                    Type = s.Type,
                    TemperatureC = s.TemperatureC,
                    DurationMinutes = s.DurationMinutes,
                    RampTimeMinutes = s.RampTimeMinutes,
                    InfuseAmountLiters = s.InfuseAmountLiters,
                    Notes = s.Notes?.Trim()
                }).ToList()
            : new List<RecipeMashStep>
            {
                new()
                {
                    StepOrder = 1,
                    Name = "Saccharification Rest",
                    Type = MashStepType.Infusion,
                    TemperatureC = 65.0m,
                    DurationMinutes = 60
                }
            };

        var fermentationSteps = request.FermentationSteps != null && request.FermentationSteps.Count > 0
            ? request.FermentationSteps
                .OrderBy(s => s.StepOrder)
                .Select(s => new RecipeFermentationStep
                {
                    StepOrder = s.StepOrder,
                    Name = s.Name.Trim(),
                    Type = s.Type,
                    TargetTemperatureC = s.TargetTemperatureC,
                    DurationDays = s.DurationDays,
                    RampTimeHours = s.RampTimeHours,
                    TriggerGravity = s.TriggerGravity,
                    Notes = s.Notes?.Trim()
                }).ToList()
            : new List<RecipeFermentationStep>
            {
                new()
                {
                    StepOrder = 1,
                    Name = "Primary Fermentation",
                    Type = FermentationStepType.Primary,
                    TargetTemperatureC = 19.0m,
                    DurationDays = 14
                }
            };

        var calc = BrewingCalculator.Calculate(
            request.BatchSizeLiters,
            request.EfficiencyPercent,
            request.BoilTimeMinutes,
            recipeIngredients
        );

        var recipe = new Recipe
        {
            UserId = userId,
            Name = trimmedName,
            Description = request.Description,
            BeerStyle = string.IsNullOrWhiteSpace(request.BeerStyle) ? "Custom" : request.BeerStyle.Trim(),
            BatchSizeLiters = request.BatchSizeLiters,
            BoilTimeMinutes = request.BoilTimeMinutes,
            EfficiencyPercent = request.EfficiencyPercent,
            OriginalGravity = calc.OriginalGravity,
            FinalGravity = calc.FinalGravity,
            AlcoholByVolume = calc.AlcoholByVolume,
            BitternessIbu = calc.BitternessIbu,
            ColorSrm = calc.ColorSrm,
            IsPublic = request.IsPublic,
            Ingredients = recipeIngredients,
            MashSteps = mashSteps,
            FermentationSteps = fermentationSteps
        };

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        return (RecipeAccessResult.Success, MapToDetailDto(recipe, userId), null);
    }

    public async Task<(RecipeAccessResult Result, RecipeDetailDto? Recipe, string? Error)> UpdateRecipeAsync(
        Guid id, UpdateRecipeRequest request, string userId)
    {
        var recipe = await db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.MashSteps)
            .Include(r => r.FermentationSteps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return (RecipeAccessResult.NotFound, null, "Recipe not found.");
        }

        if (recipe.UserId != userId)
        {
            return (RecipeAccessResult.Forbidden, null, "You do not have permission to update this recipe.");
        }

        var trimmedName = request.Name.Trim();
        var nameExists = await db.Recipes.AnyAsync(r =>
            r.UserId == userId && r.Id != id && r.Name.ToLower() == trimmedName.ToLower());

        if (nameExists)
        {
            return (RecipeAccessResult.DuplicateName, null, $"A recipe named '{trimmedName}' already exists.");
        }

        var ingredientIds = request.Ingredients.Select(i => i.IngredientId).Distinct().ToList();
        var dbIngredients = await db.Ingredients
            .Where(i => ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);

        // Remove existing ingredients, mash steps, and fermentation steps
        db.RecipeIngredients.RemoveRange(recipe.Ingredients);
        db.RecipeMashSteps.RemoveRange(recipe.MashSteps);
        db.RecipeFermentationSteps.RemoveRange(recipe.FermentationSteps);

        var newIngredients = request.Ingredients.Select(input => new RecipeIngredient
        {
            RecipeId = recipe.Id,
            IngredientId = input.IngredientId,
            Ingredient = dbIngredients.GetValueOrDefault(input.IngredientId),
            Amount = input.Amount,
            Unit = input.Unit,
            DurationMinutes = input.DurationMinutes,
            Usage = input.Usage,
            Notes = input.Notes,
            Form = input.Form
        }).ToList();

        foreach (var newIng in newIngredients)
        {
            db.RecipeIngredients.Add(newIng);
        }

        var newMashSteps = request.MashSteps?
            .OrderBy(s => s.StepOrder)
            .Select(s => new RecipeMashStep
            {
                RecipeId = recipe.Id,
                StepOrder = s.StepOrder,
                Name = s.Name.Trim(),
                Type = s.Type,
                TemperatureC = s.TemperatureC,
                DurationMinutes = s.DurationMinutes,
                RampTimeMinutes = s.RampTimeMinutes,
                InfuseAmountLiters = s.InfuseAmountLiters,
                Notes = s.Notes?.Trim()
            }).ToList() ?? new List<RecipeMashStep>();

        foreach (var ms in newMashSteps)
        {
            db.RecipeMashSteps.Add(ms);
        }

        var newFermentationSteps = request.FermentationSteps?
            .OrderBy(s => s.StepOrder)
            .Select(s => new RecipeFermentationStep
            {
                RecipeId = recipe.Id,
                StepOrder = s.StepOrder,
                Name = s.Name.Trim(),
                Type = s.Type,
                TargetTemperatureC = s.TargetTemperatureC,
                DurationDays = s.DurationDays,
                RampTimeHours = s.RampTimeHours,
                TriggerGravity = s.TriggerGravity,
                Notes = s.Notes?.Trim()
            }).ToList() ?? new List<RecipeFermentationStep>();

        foreach (var fs in newFermentationSteps)
        {
            db.RecipeFermentationSteps.Add(fs);
        }

        var calc = BrewingCalculator.Calculate(
            request.BatchSizeLiters,
            request.EfficiencyPercent,
            request.BoilTimeMinutes,
            newIngredients
        );

        recipe.Name = trimmedName;
        recipe.Description = request.Description;
        recipe.BeerStyle = string.IsNullOrWhiteSpace(request.BeerStyle) ? "Custom" : request.BeerStyle.Trim();
        recipe.BatchSizeLiters = request.BatchSizeLiters;
        recipe.BoilTimeMinutes = request.BoilTimeMinutes;
        recipe.EfficiencyPercent = request.EfficiencyPercent;
        recipe.OriginalGravity = calc.OriginalGravity;
        recipe.FinalGravity = calc.FinalGravity;
        recipe.AlcoholByVolume = calc.AlcoholByVolume;
        recipe.BitternessIbu = calc.BitternessIbu;
        recipe.ColorSrm = calc.ColorSrm;
        recipe.IsPublic = request.IsPublic;
        recipe.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        recipe.Ingredients = newIngredients;
        recipe.MashSteps = newMashSteps;
        recipe.FermentationSteps = newFermentationSteps;
        return (RecipeAccessResult.Success, MapToDetailDto(recipe, userId), null);
    }

    public async Task<CalculateRecipeResponse> CalculateRecipeAsync(CalculateRecipeRequest request)
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
            Notes = input.Notes,
            Form = input.Form
        }).ToList();

        var calc = BrewingCalculator.Calculate(
            request.BatchSizeLiters,
            request.EfficiencyPercent,
            request.BoilTimeMinutes,
            recipeIngredients
        );

        return new CalculateRecipeResponse(
            calc.OriginalGravity,
            calc.FinalGravity,
            calc.AlcoholByVolume,
            calc.BitternessIbu,
            calc.ColorSrm
        );
    }

    public async Task<RecipeAccessResult> DeleteRecipeAsync(Guid id, string userId)
    {
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id);
        if (recipe == null)
        {
            return RecipeAccessResult.NotFound;
        }

        if (recipe.UserId != userId)
        {
            return RecipeAccessResult.Forbidden;
        }

        // Decouple linked batches explicitly so In-Memory database & cached entities stay synchronized
        var linkedBatches = await db.Batches.Where(b => b.RecipeId == id).ToListAsync();
        foreach (var batch in linkedBatches)
        {
            batch.RecipeId = null;
        }

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();

        return RecipeAccessResult.Success;
    }

    private static RecipeDetailDto MapToDetailDto(Recipe recipe, string? userId = null)
    {
        return new RecipeDetailDto(
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
            BatchService.OrderIngredientsChronologically(recipe.Ingredients).Select(ri => new RecipeIngredientOutputDto(
                ri.Id,
                ri.IngredientId,
                ri.Ingredient?.Name ?? "Unknown",
                ri.Ingredient?.Type ?? IngredientType.Other,
                ri.Amount,
                ri.Unit,
                ri.DurationMinutes,
                ri.Usage,
                ri.Notes,
                ri.Ingredient?.PotentialGravity,
                ri.Ingredient?.ColorSrm,
                ri.Ingredient?.AlphaAcidPercent,
                ri.Ingredient?.AttenuationPercent,
                ri.Form ?? ri.Ingredient?.Form
            )).ToList(),
            recipe.MashSteps.OrderBy(ms => ms.StepOrder).Select(ms => new RecipeMashStepOutputDto(
                ms.Id,
                ms.RecipeId,
                ms.StepOrder,
                ms.Name,
                ms.Type,
                ms.TemperatureC,
                ms.DurationMinutes,
                ms.RampTimeMinutes,
                ms.InfuseAmountLiters,
                ms.Notes
            )).ToList(),
            recipe.FermentationSteps.OrderBy(fs => fs.StepOrder).Select(fs => new RecipeFermentationStepOutputDto(
                fs.Id,
                fs.RecipeId,
                fs.StepOrder,
                fs.Name,
                fs.Type,
                fs.TargetTemperatureC,
                fs.DurationDays,
                fs.RampTimeHours,
                fs.TriggerGravity,
                fs.Notes
            )).ToList(),
            recipe.User != null && !string.IsNullOrEmpty(recipe.User.DisplayName)
                ? recipe.User.DisplayName
                : "Community Brewer",
            userId != null && recipe.UserId == userId
        );
    }

    public async Task<bool> RecipeNameExistsAsync(string name, string userId)
    {
        var trimmedName = name.Trim();
        return await db.Recipes.AnyAsync(r =>
            r.UserId == userId && r.Name.ToLower() == trimmedName.ToLower());
    }
}