using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Services;

public enum BatchTastingAccessResult
{
    Success,
    NotFound
}

public interface IBatchTastingService
{
    Task<(BatchTastingAccessResult Result, List<BatchTastingDto>? Items)> GetTastingsAsync(Guid batchId, string userId);
    Task<(BatchTastingAccessResult Result, BatchTastingDto? Tasting)> AddTastingAsync(Guid batchId, CreateBatchTastingRequest request, string userId);
    Task<(BatchTastingAccessResult Result, BatchTastingDto? Tasting)> UpdateTastingAsync(Guid batchId, Guid tastingId, UpdateBatchTastingRequest request, string userId);
    Task<BatchTastingAccessResult> DeleteTastingAsync(Guid batchId, Guid tastingId, string userId);
}

public class BatchTastingService(BrewYouDbContext db) : IBatchTastingService
{
    public async Task<(BatchTastingAccessResult Result, List<BatchTastingDto>? Items)> GetTastingsAsync(Guid batchId, string userId)
    {
        var exists = await db.Batches.AsNoTracking().AnyAsync(b => b.Id == batchId && b.UserId == userId);
        if (!exists)
        {
            return (BatchTastingAccessResult.NotFound, null);
        }

        var items = await db.BatchTastings
            .AsNoTracking()
            .Where(t => t.BatchId == batchId && t.UserId == userId)
            .OrderByDescending(t => t.TastedAt)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        return (BatchTastingAccessResult.Success, items.Select(MapToDto).ToList());
    }

    public async Task<(BatchTastingAccessResult Result, BatchTastingDto? Tasting)> AddTastingAsync(
        Guid batchId,
        CreateBatchTastingRequest request,
        string userId)
    {
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId);
        if (batch is null)
        {
            return (BatchTastingAccessResult.NotFound, null);
        }

        var now = DateTime.UtcNow;
        var aroma = Math.Round(request.AromaScore, 1);
        var appearance = Math.Round(request.AppearanceScore, 1);
        var flavor = Math.Round(request.FlavorScore, 1);
        var mouthfeel = Math.Round(request.MouthfeelScore, 1);
        var overall = Math.Round(request.OverallScore, 1);

        var tasting = new BatchTasting
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            UserId = userId,
            TastedAt = (request.TastedAt ?? now).ToUniversalTime(),
            SampleSource = string.IsNullOrWhiteSpace(request.SampleSource) ? "Keg" : request.SampleSource.Trim(),
            AromaScore = aroma,
            AromaNotes = request.AromaNotes?.Trim(),
            AppearanceScore = appearance,
            AppearanceNotes = request.AppearanceNotes?.Trim(),
            FlavorScore = flavor,
            FlavorNotes = request.FlavorNotes?.Trim(),
            MouthfeelScore = mouthfeel,
            MouthfeelNotes = request.MouthfeelNotes?.Trim(),
            OverallScore = overall,
            OverallNotes = request.OverallNotes?.Trim(),
            TotalScore = Math.Round(aroma + appearance + flavor + mouthfeel + overall, 1),
            StarRating = request.StarRating,
            WouldRebrew = request.WouldRebrew,
            OffFlavors = OffFlavorCatalog.NormalizeKeys(request.OffFlavors),
            ServingTemperatureC = request.ServingTemperatureC,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.BatchTastings.Add(tasting);
        await db.SaveChangesAsync();

        return (BatchTastingAccessResult.Success, MapToDto(tasting));
    }

    public async Task<(BatchTastingAccessResult Result, BatchTastingDto? Tasting)> UpdateTastingAsync(
        Guid batchId,
        Guid tastingId,
        UpdateBatchTastingRequest request,
        string userId)
    {
        var tasting = await db.BatchTastings
            .FirstOrDefaultAsync(t => t.Id == tastingId && t.BatchId == batchId && t.UserId == userId);

        if (tasting is null)
        {
            return (BatchTastingAccessResult.NotFound, null);
        }

        if (request.TastedAt.HasValue) tasting.TastedAt = request.TastedAt.Value.ToUniversalTime();
        if (request.SampleSource is not null) tasting.SampleSource = string.IsNullOrWhiteSpace(request.SampleSource) ? null : request.SampleSource.Trim();
        if (request.AromaScore.HasValue) tasting.AromaScore = Math.Round(request.AromaScore.Value, 1);
        if (request.AromaNotes is not null) tasting.AromaNotes = string.IsNullOrWhiteSpace(request.AromaNotes) ? null : request.AromaNotes.Trim();
        if (request.AppearanceScore.HasValue) tasting.AppearanceScore = Math.Round(request.AppearanceScore.Value, 1);
        if (request.AppearanceNotes is not null) tasting.AppearanceNotes = string.IsNullOrWhiteSpace(request.AppearanceNotes) ? null : request.AppearanceNotes.Trim();
        if (request.FlavorScore.HasValue) tasting.FlavorScore = Math.Round(request.FlavorScore.Value, 1);
        if (request.FlavorNotes is not null) tasting.FlavorNotes = string.IsNullOrWhiteSpace(request.FlavorNotes) ? null : request.FlavorNotes.Trim();
        if (request.MouthfeelScore.HasValue) tasting.MouthfeelScore = Math.Round(request.MouthfeelScore.Value, 1);
        if (request.MouthfeelNotes is not null) tasting.MouthfeelNotes = string.IsNullOrWhiteSpace(request.MouthfeelNotes) ? null : request.MouthfeelNotes.Trim();
        if (request.OverallScore.HasValue) tasting.OverallScore = Math.Round(request.OverallScore.Value, 1);
        if (request.OverallNotes is not null) tasting.OverallNotes = string.IsNullOrWhiteSpace(request.OverallNotes) ? null : request.OverallNotes.Trim();
        if (request.StarRating.HasValue) tasting.StarRating = request.StarRating.Value;
        if (request.WouldRebrew.HasValue) tasting.WouldRebrew = request.WouldRebrew.Value;
        if (request.OffFlavors is not null) tasting.OffFlavors = OffFlavorCatalog.NormalizeKeys(request.OffFlavors);
        if (request.ServingTemperatureC.HasValue) tasting.ServingTemperatureC = request.ServingTemperatureC.Value;

        tasting.TotalScore = Math.Round(
            tasting.AromaScore + tasting.AppearanceScore + tasting.FlavorScore + tasting.MouthfeelScore + tasting.OverallScore,
            1);
        tasting.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return (BatchTastingAccessResult.Success, MapToDto(tasting));
    }

    public async Task<BatchTastingAccessResult> DeleteTastingAsync(Guid batchId, Guid tastingId, string userId)
    {
        var tasting = await db.BatchTastings
            .FirstOrDefaultAsync(t => t.Id == tastingId && t.BatchId == batchId && t.UserId == userId);

        if (tasting is null)
        {
            return BatchTastingAccessResult.NotFound;
        }

        db.BatchTastings.Remove(tasting);
        await db.SaveChangesAsync();
        return BatchTastingAccessResult.Success;
    }

    public static string ClassifyBjcpBand(decimal totalScore) => totalScore switch
    {
        >= 45m => "Outstanding",
        >= 38m => "Excellent",
        >= 30m => "VeryGood",
        >= 21m => "Good",
        >= 14m => "Fair",
        _ => "Problematic"
    };

    public static BatchTastingDto MapToDto(BatchTasting tasting) => new(
        tasting.Id,
        tasting.BatchId,
        tasting.TastedAt,
        tasting.SampleSource,
        tasting.AromaScore,
        tasting.AromaNotes,
        tasting.AppearanceScore,
        tasting.AppearanceNotes,
        tasting.FlavorScore,
        tasting.FlavorNotes,
        tasting.MouthfeelScore,
        tasting.MouthfeelNotes,
        tasting.OverallScore,
        tasting.OverallNotes,
        tasting.TotalScore,
        ClassifyBjcpBand(tasting.TotalScore),
        tasting.StarRating,
        tasting.WouldRebrew,
        tasting.OffFlavors.ToList(),
        tasting.ServingTemperatureC,
        tasting.CreatedAt,
        tasting.UpdatedAt);

    public static BatchTastingSummaryDto BuildSummary(IEnumerable<BatchTasting> tastings)
    {
        var list = tastings.OrderByDescending(t => t.TastedAt).ThenByDescending(t => t.CreatedAt).ToList();
        if (list.Count == 0)
        {
            return new BatchTastingSummaryDto(0, null, null, null, null, null, []);
        }

        var latest = list[0];
        var reportedOffFlavors = list
            .SelectMany(t => t.OffFlavors)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BatchTastingSummaryDto(
            list.Count,
            Math.Round((decimal)list.Average(t => t.StarRating), 1),
            Math.Round(list.Average(t => t.TotalScore), 1),
            latest.StarRating,
            latest.TotalScore,
            latest.WouldRebrew,
            reportedOffFlavors);
    }
}