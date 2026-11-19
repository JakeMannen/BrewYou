using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Services;

public enum YeastBankAccessResult
{
    Success,
    NotFound,
    ValidationFailed
}

public interface IYeastBankService
{
    Task<List<YeastBankEntryDto>> GetEntriesAsync(string userId, string? status = null);
    Task<YeastBankEntryDto?> GetEntryByIdAsync(Guid id, string userId);
    Task<(YeastBankAccessResult Result, YeastBankEntryDto? Entry, string? ErrorMessage)> CreateEntryAsync(CreateYeastBankEntryRequest request, string userId);
    Task<(YeastBankAccessResult Result, YeastBankEntryDto? Entry, string? ErrorMessage)> UpdateEntryAsync(Guid id, UpdateYeastBankEntryRequest request, string userId);
    Task<YeastBankAccessResult> DeleteEntryAsync(Guid id, string userId);
    Task<(YeastBankAccessResult Result, YeastBankEntryDto? Entry, string? ErrorMessage)> HarvestFromBatchAsync(Guid batchId, HarvestYeastRequest request, string userId);
    Task<YeastPitchPlanDto> CalculatePitchPlanAsync(CalculateYeastPitchRequest request, string userId);
}

public class YeastBankService(BrewYouDbContext db) : IYeastBankService
{
    public async Task<List<YeastBankEntryDto>> GetEntriesAsync(string userId, string? status = null)
    {
        var query = db.YeastBankEntries
            .AsNoTracking()
            .Where(y => y.UserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim();
            query = query.Where(y => y.Status == normalized);
        }

        var entries = await query
            .OrderBy(y => y.Status == "Available" ? 0 : 1)
            .ThenByDescending(y => y.HarvestDate)
            .ThenByDescending(y => y.CreatedAt)
            .ToListAsync();

        return entries.Select(MapToDto).ToList();
    }

    public async Task<YeastBankEntryDto?> GetEntryByIdAsync(Guid id, string userId)
    {
        var entry = await db.YeastBankEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == id && y.UserId == userId);

        return entry is null ? null : MapToDto(entry);
    }

    public async Task<(YeastBankAccessResult Result, YeastBankEntryDto? Entry, string? ErrorMessage)> CreateEntryAsync(
        CreateYeastBankEntryRequest request,
        string userId)
    {
        Batch? sourceBatch = null;
        if (request.SourceBatchId.HasValue && request.SourceBatchId.Value != Guid.Empty)
        {
            sourceBatch = await db.Batches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == request.SourceBatchId.Value && b.UserId == userId);

            if (sourceBatch is null)
            {
                return (YeastBankAccessResult.NotFound, null, "Source batch not found.");
            }
        }

        Guid? ingredientId = null;
        if (request.IngredientId.HasValue && request.IngredientId.Value != Guid.Empty)
        {
            var exists = await db.Ingredients.AnyAsync(i =>
                i.Id == request.IngredientId.Value &&
                (i.IsCatalogItem || i.CreatedByUserId == userId));
            if (!exists)
            {
                return (YeastBankAccessResult.NotFound, null, "Yeast ingredient not found.");
            }
            ingredientId = request.IngredientId.Value;
        }

        var now = DateTime.UtcNow;
        var entry = new YeastBankEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IngredientId = ingredientId,
            StrainName = request.StrainName.Trim(),
            LabOrBrand = request.LabOrBrand?.Trim(),
            ProductCode = request.ProductCode?.Trim(),
            Form = NormalizeForm(request.Form),
            Generation = request.Generation,
            HarvestDate = (request.HarvestDate ?? now).ToUniversalTime(),
            SourceBatchId = sourceBatch?.Id,
            SourceBatchName = sourceBatch?.Name,
            SourceBatchCode = sourceBatch?.BatchCode,
            VolumeMl = request.VolumeMl,
            EstimatedDensityBillionPerMl = request.EstimatedDensityBillionPerMl,
            BaseViabilityPercent = request.BaseViabilityPercent,
            StorageLocation = request.StorageLocation?.Trim(),
            Status = request.VolumeMl <= 0m ? "Depleted" : "Available",
            Notes = request.Notes?.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.YeastBankEntries.Add(entry);
        await db.SaveChangesAsync();

        return (YeastBankAccessResult.Success, MapToDto(entry), null);
    }

    public async Task<(YeastBankAccessResult Result, YeastBankEntryDto? Entry, string? ErrorMessage)> UpdateEntryAsync(
        Guid id,
        UpdateYeastBankEntryRequest request,
        string userId)
    {
        var entry = await db.YeastBankEntries
            .FirstOrDefaultAsync(y => y.Id == id && y.UserId == userId);

        if (entry is null)
        {
            return (YeastBankAccessResult.NotFound, null, "Yeast bank entry not found.");
        }

        if (request.StrainName is not null) entry.StrainName = request.StrainName.Trim();
        if (request.LabOrBrand is not null) entry.LabOrBrand = string.IsNullOrWhiteSpace(request.LabOrBrand) ? null : request.LabOrBrand.Trim();
        if (request.ProductCode is not null) entry.ProductCode = string.IsNullOrWhiteSpace(request.ProductCode) ? null : request.ProductCode.Trim();
        if (request.Form is not null) entry.Form = NormalizeForm(request.Form);
        if (request.Generation.HasValue) entry.Generation = request.Generation.Value;
        if (request.HarvestDate.HasValue) entry.HarvestDate = request.HarvestDate.Value.ToUniversalTime();
        if (request.VolumeMl.HasValue) entry.VolumeMl = request.VolumeMl.Value;
        if (request.EstimatedDensityBillionPerMl.HasValue) entry.EstimatedDensityBillionPerMl = request.EstimatedDensityBillionPerMl.Value;
        if (request.BaseViabilityPercent.HasValue) entry.BaseViabilityPercent = request.BaseViabilityPercent.Value;
        if (request.StorageLocation is not null) entry.StorageLocation = string.IsNullOrWhiteSpace(request.StorageLocation) ? null : request.StorageLocation.Trim();
        if (request.Status is not null) entry.Status = NormalizeStatus(request.Status);
        if (request.Notes is not null) entry.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        if (entry.VolumeMl <= 0m && entry.Status == "Available")
        {
            entry.Status = "Depleted";
        }

        entry.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return (YeastBankAccessResult.Success, MapToDto(entry), null);
    }

    public async Task<YeastBankAccessResult> DeleteEntryAsync(Guid id, string userId)
    {
        var entry = await db.YeastBankEntries
            .FirstOrDefaultAsync(y => y.Id == id && y.UserId == userId);

        if (entry is null)
        {
            return YeastBankAccessResult.NotFound;
        }

        // Explicitly decouple any batches referencing this yeast bank entry so EF InMemory and PostgreSQL match
        var linkedBatches = await db.Batches
            .Where(b => b.YeastBankEntryId == id)
            .ToListAsync();
        foreach (var batch in linkedBatches)
        {
            batch.YeastBankEntryId = null;
        }

        db.YeastBankEntries.Remove(entry);
        await db.SaveChangesAsync();
        return YeastBankAccessResult.Success;
    }

    public async Task<(YeastBankAccessResult Result, YeastBankEntryDto? Entry, string? ErrorMessage)> HarvestFromBatchAsync(
        Guid batchId,
        HarvestYeastRequest request,
        string userId)
    {
        var batch = await db.Batches
            .Include(b => b.Ingredients)
            .Include(b => b.YeastBankEntry)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId);

        if (batch is null)
        {
            return (YeastBankAccessResult.NotFound, null, "Batch not found.");
        }

        var yeastIngredient = batch.Ingredients.FirstOrDefault(i => i.Type == IngredientType.Yeast);
        var resolvedStrainName = !string.IsNullOrWhiteSpace(request.StrainName)
            ? request.StrainName.Trim()
            : batch.YeastBankEntry?.StrainName ?? yeastIngredient?.Name ?? $"{batch.Name} House Yeast";

        var nextGeneration = batch.YeastBankEntry is not null
            ? batch.YeastBankEntry.Generation + 1
            : 1;

        var resolvedIngredientId = request.IngredientId ?? batch.YeastBankEntry?.IngredientId ?? yeastIngredient?.SourceIngredientId;
        if (resolvedIngredientId.HasValue)
        {
            var ingExists = await db.Ingredients.AnyAsync(i => i.Id == resolvedIngredientId.Value);
            if (!ingExists)
            {
                resolvedIngredientId = null;
            }
        }

        var now = DateTime.UtcNow;
        var entry = new YeastBankEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IngredientId = resolvedIngredientId,
            StrainName = resolvedStrainName,
            LabOrBrand = request.LabOrBrand?.Trim() ?? batch.YeastBankEntry?.LabOrBrand,
            ProductCode = request.ProductCode?.Trim() ?? batch.YeastBankEntry?.ProductCode,
            Form = "Slurry",
            Generation = nextGeneration,
            HarvestDate = (request.HarvestDate ?? now).ToUniversalTime(),
            SourceBatchId = batch.Id,
            SourceBatchName = batch.Name,
            SourceBatchCode = batch.BatchCode,
            VolumeMl = request.VolumeMl,
            EstimatedDensityBillionPerMl = request.EstimatedDensityBillionPerMl,
            BaseViabilityPercent = request.BaseViabilityPercent,
            StorageLocation = request.StorageLocation?.Trim(),
            Status = "Available",
            Notes = string.IsNullOrWhiteSpace(request.Notes)
                ? $"Harvested from {batch.BatchCode} ({batch.Name})"
                : request.Notes.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.YeastBankEntries.Add(entry);
        await db.SaveChangesAsync();

        return (YeastBankAccessResult.Success, MapToDto(entry), null);
    }

    public async Task<YeastPitchPlanDto> CalculatePitchPlanAsync(CalculateYeastPitchRequest request, string userId)
    {
        var form = request.YeastForm;
        var ageDays = request.PackageAgeDays ?? 0m;
        var baseViability = request.BaseViabilityPercent;
        var slurryDensity = request.SlurryDensityBillionPerMl ?? 1.0m;
        var slurryAvail = request.SlurryVolumeAvailableMl;
        string? strainName = null;

        if (request.YeastBankEntryId.HasValue && request.YeastBankEntryId.Value != Guid.Empty)
        {
            var bankEntry = await db.YeastBankEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(y => y.Id == request.YeastBankEntryId.Value && y.UserId == userId);

            if (bankEntry is not null)
            {
                form = bankEntry.Form;
                ageDays = Math.Max(0m, (decimal)(DateTime.UtcNow - bankEntry.HarvestDate).TotalDays);
                baseViability = bankEntry.BaseViabilityPercent;
                slurryDensity = bankEntry.EstimatedDensityBillionPerMl;
                slurryAvail = bankEntry.VolumeMl;
                strainName = bankEntry.StrainName;
            }
        }

        var output = YeastPitchCalculator.Calculate(new YeastPitchCalculationInput(
            request.BatchVolumeLiters,
            request.OriginalGravity,
            request.StyleCategory,
            request.CustomPitchRateMillionPerMlPlato,
            form,
            ageDays,
            baseViability,
            request.CellsPerUnitBillions,
            request.PacksAvailable,
            slurryDensity,
            slurryAvail,
            request.StarterMethod,
            request.StarterVolumeLiters));

        return new YeastPitchPlanDto(
            output.Plato,
            output.TargetPitchRateMillionPerMlPlato,
            output.RequiredCellsBillions,
            output.EstimatedViabilityPercent,
            output.ViableCellsPerUnitBillions,
            output.InitialViableCellsBillions,
            output.DeficitCellsBillions,
            output.DryPacksExact,
            output.DryPacksRecommended,
            output.RequiredSlurryMl,
            output.RecommendedStarterVolumeLiters,
            output.StarterDmeGrams,
            output.ProjectedStarterCellsBillions,
            output.NeedsMultiStepStarter,
            output.RecommendationSummaryKey,
            request.YeastBankEntryId,
            strainName);
    }

    public static YeastBankEntryDto MapToDto(YeastBankEntry entry)
    {
        var ageDays = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - entry.HarvestDate).TotalDays));
        var currentViability = YeastPitchCalculator.CalculateViabilityPercent(entry.Form, ageDays, entry.BaseViabilityPercent);
        var viablePerMl = entry.EstimatedDensityBillionPerMl * (currentViability / 100m);
        var totalViableCells = entry.Form.Equals("Slurry", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(entry.VolumeMl * viablePerMl, 1)
            : Math.Round((entry.Form.Equals("Dry", StringComparison.OrdinalIgnoreCase)
                ? YeastPitchCalculator.DefaultDryPackCellsBillions
                : YeastPitchCalculator.DefaultLiquidPackCellsBillions) * (currentViability / 100m), 1);

        return new YeastBankEntryDto(
            entry.Id,
            entry.IngredientId,
            entry.StrainName,
            entry.LabOrBrand,
            entry.ProductCode,
            entry.Form,
            entry.Generation,
            entry.HarvestDate,
            entry.SourceBatchId,
            entry.SourceBatchName,
            entry.SourceBatchCode,
            entry.VolumeMl,
            entry.EstimatedDensityBillionPerMl,
            entry.BaseViabilityPercent,
            currentViability,
            totalViableCells,
            ageDays,
            entry.StorageLocation,
            entry.Status,
            entry.Notes,
            entry.CreatedAt,
            entry.UpdatedAt);
    }

    private static string NormalizeForm(string? form) => (form ?? "Slurry").Trim().ToLowerInvariant() switch
    {
        "liquid" => "Liquid",
        "dry" => "Dry",
        _ => "Slurry"
    };

    private static string NormalizeStatus(string? status) => (status ?? "Available").Trim().ToLowerInvariant() switch
    {
        "depleted" => "Depleted",
        "discarded" => "Discarded",
        _ => "Available"
    };
}