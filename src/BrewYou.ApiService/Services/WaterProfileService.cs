using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Services;

public enum WaterProfileAccessResult
{
    Success,
    NotFound,
    PresetImmutable,
    Forbidden
}

public interface IWaterProfileService
{
    Task<List<WaterProfileDto>> GetProfilesAsync(string userId, string? category = null);
    Task<WaterProfileDto?> GetProfileByIdAsync(Guid id, string userId);
    Task<WaterProfileDto> CreateProfileAsync(CreateWaterProfileRequest request, string userId);
    Task<(WaterProfileAccessResult Result, WaterProfileDto? Profile, string? ErrorMessage)> UpdateProfileAsync(Guid id, UpdateWaterProfileRequest request, string userId);
    Task<(WaterProfileAccessResult Result, string? ErrorMessage)> DeleteProfileAsync(Guid id, string userId);
    WaterChemistryResultDto CalculateChemistry(CalculateWaterChemistryRequest request);
    SuggestWaterSaltsResponse SuggestSalts(SuggestWaterSaltsRequest request);
}

public class WaterProfileService(BrewYouDbContext db) : IWaterProfileService
{
    public async Task<List<WaterProfileDto>> GetProfilesAsync(string userId, string? category = null)
    {
        var query = db.WaterProfiles
            .AsNoTracking()
            .Where(w => w.IsPreset || w.UserId == userId);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalized = NormalizeCategory(category);
            query = query.Where(w => w.Category == normalized);
        }

        var profiles = await query
            .OrderBy(w => w.IsPreset ? 0 : 1)
            .ThenBy(w => w.Category)
            .ThenBy(w => w.Name)
            .ToListAsync();

        return profiles.Select(MapToDto).ToList();
    }

    public async Task<WaterProfileDto?> GetProfileByIdAsync(Guid id, string userId)
    {
        var profile = await db.WaterProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id && (w.IsPreset || w.UserId == userId));

        return profile is null ? null : MapToDto(profile);
    }

    public async Task<WaterProfileDto> CreateProfileAsync(CreateWaterProfileRequest request, string userId)
    {
        var now = DateTime.UtcNow;
        var profile = new WaterProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Category = NormalizeCategory(request.Category),
            IsPreset = false,
            CalciumPpm = Math.Round(request.CalciumPpm, 1),
            MagnesiumPpm = Math.Round(request.MagnesiumPpm, 1),
            SodiumPpm = Math.Round(request.SodiumPpm, 1),
            SulfatePpm = Math.Round(request.SulfatePpm, 1),
            ChloridePpm = Math.Round(request.ChloridePpm, 1),
            BicarbonatePpm = Math.Round(request.BicarbonatePpm, 1),
            Ph = request.Ph.HasValue ? Math.Round(request.Ph.Value, 2) : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.WaterProfiles.Add(profile);
        await db.SaveChangesAsync();
        return MapToDto(profile);
    }

    public async Task<(WaterProfileAccessResult Result, WaterProfileDto? Profile, string? ErrorMessage)> UpdateProfileAsync(
        Guid id,
        UpdateWaterProfileRequest request,
        string userId)
    {
        var profile = await db.WaterProfiles.FirstOrDefaultAsync(w => w.Id == id);
        if (profile is null)
        {
            return (WaterProfileAccessResult.NotFound, null, "Water profile not found.");
        }

        if (profile.IsPreset)
        {
            return (WaterProfileAccessResult.PresetImmutable, null, "Built-in water profile presets cannot be modified.");
        }

        if (profile.UserId != userId)
        {
            return (WaterProfileAccessResult.Forbidden, null, "You do not have permission to update this water profile.");
        }

        if (request.Name is not null) profile.Name = request.Name.Trim();
        if (request.Category is not null) profile.Category = NormalizeCategory(request.Category);
        if (request.Description is not null) profile.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (request.CalciumPpm.HasValue) profile.CalciumPpm = Math.Round(request.CalciumPpm.Value, 1);
        if (request.MagnesiumPpm.HasValue) profile.MagnesiumPpm = Math.Round(request.MagnesiumPpm.Value, 1);
        if (request.SodiumPpm.HasValue) profile.SodiumPpm = Math.Round(request.SodiumPpm.Value, 1);
        if (request.SulfatePpm.HasValue) profile.SulfatePpm = Math.Round(request.SulfatePpm.Value, 1);
        if (request.ChloridePpm.HasValue) profile.ChloridePpm = Math.Round(request.ChloridePpm.Value, 1);
        if (request.BicarbonatePpm.HasValue) profile.BicarbonatePpm = Math.Round(request.BicarbonatePpm.Value, 1);
        if (request.Ph.HasValue) profile.Ph = Math.Round(request.Ph.Value, 2);

        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (WaterProfileAccessResult.Success, MapToDto(profile), null);
    }

    public async Task<(WaterProfileAccessResult Result, string? ErrorMessage)> DeleteProfileAsync(Guid id, string userId)
    {
        var profile = await db.WaterProfiles.FirstOrDefaultAsync(w => w.Id == id);
        if (profile is null)
        {
            return (WaterProfileAccessResult.NotFound, "Water profile not found.");
        }

        if (profile.IsPreset)
        {
            return (WaterProfileAccessResult.PresetImmutable, "Built-in water profile presets cannot be deleted.");
        }

        if (profile.UserId != userId)
        {
            return (WaterProfileAccessResult.Forbidden, "You do not have permission to delete this water profile.");
        }

        // Explicitly decouple referencing recipe water plans while preserving snapshotted names & ions
        var affectedPlans = await db.RecipeWaterPlans
            .Where(p => p.SourceProfileId == id || p.TargetProfileId == id)
            .ToListAsync();

        foreach (var plan in affectedPlans)
        {
            if (plan.SourceProfileId == id)
            {
                plan.SourceProfileName ??= profile.Name;
                plan.SourceProfileId = null;
            }
            if (plan.TargetProfileId == id)
            {
                plan.TargetProfileName ??= profile.Name;
                plan.TargetProfileId = null;
            }
        }

        db.WaterProfiles.Remove(profile);
        await db.SaveChangesAsync();
        return (WaterProfileAccessResult.Success, null);
    }

    public WaterChemistryResultDto CalculateChemistry(CalculateWaterChemistryRequest request)
    {
        var source = new WaterIonProfile(
            request.SourceCalciumPpm,
            request.SourceMagnesiumPpm,
            request.SourceSodiumPpm,
            request.SourceSulfatePpm,
            request.SourceChloridePpm,
            request.SourceBicarbonatePpm);

        var mashAdd = ToSaltInput(request.MashAdditions);
        var spargeAdd = request.SpargeAdditions is not null
            ? ToSaltInput(request.SpargeAdditions)
            : new SaltAdditionsInput();

        var grist = (request.Grist ?? [])
            .Select(g => new GristGrainInput(g.WeightKg, g.ColorSrm, g.Name))
            .ToList();

        var summary = WaterChemistryCalculator.EvaluatePlan(
            source,
            mashAdd,
            request.MashVolumeLiters,
            spargeAdd,
            request.SpargeVolumeLiters,
            grist);

        return MapSummaryToDto(summary);
    }

    public SuggestWaterSaltsResponse SuggestSalts(SuggestWaterSaltsRequest request)
    {
        var source = new WaterIonProfile(
            request.SourceCalciumPpm,
            request.SourceMagnesiumPpm,
            request.SourceSodiumPpm,
            request.SourceSulfatePpm,
            request.SourceChloridePpm,
            request.SourceBicarbonatePpm);

        var target = new WaterIonProfile(
            request.TargetCalciumPpm,
            request.TargetMagnesiumPpm,
            request.TargetSodiumPpm,
            request.TargetSulfatePpm,
            request.TargetChloridePpm,
            request.TargetBicarbonatePpm);

        var grist = (request.Grist ?? [])
            .Select(g => new GristGrainInput(g.WeightKg, g.ColorSrm, g.Name))
            .ToList();

        var suggestion = WaterChemistryCalculator.SuggestSaltAdditions(
            source,
            target,
            request.MashVolumeLiters,
            request.SpargeVolumeLiters,
            grist,
            request.TargetMashPh,
            request.AcidType);

        return new SuggestWaterSaltsResponse(
            ToSaltDto(suggestion.MashAdditions),
            ToSaltDto(suggestion.SpargeAdditions),
            MapSummaryToDto(suggestion.Preview));
    }

    public static WaterProfileDto MapToDto(WaterProfile profile)
    {
        var ions = new WaterIonProfile(
            profile.CalciumPpm,
            profile.MagnesiumPpm,
            profile.SodiumPpm,
            profile.SulfatePpm,
            profile.ChloridePpm,
            profile.BicarbonatePpm);

        return new WaterProfileDto(
            profile.Id,
            profile.Name,
            profile.Description,
            profile.Category,
            profile.IsPreset,
            profile.CalciumPpm,
            profile.MagnesiumPpm,
            profile.SodiumPpm,
            profile.SulfatePpm,
            profile.ChloridePpm,
            profile.BicarbonatePpm,
            profile.Ph,
            WaterChemistryCalculator.CalculateResidualAlkalinity(ions),
            WaterChemistryCalculator.CalculateSulfateToChlorideRatio(profile.SulfatePpm, profile.ChloridePpm),
            profile.CreatedAt,
            profile.UpdatedAt);
    }

    public static RecipeWaterPlanDto MapPlanToDto(RecipeWaterPlan plan)
    {
        var balance = WaterChemistryCalculator.ClassifySulfateChlorideBalance(
            plan.SulfateToChlorideRatio,
            plan.ResultingSulfatePpm,
            plan.ResultingChloridePpm);

        return new RecipeWaterPlanDto(
            plan.Id,
            plan.RecipeId,
            plan.SourceProfileId,
            plan.SourceProfileName,
            plan.SourceCalciumPpm,
            plan.SourceMagnesiumPpm,
            plan.SourceSodiumPpm,
            plan.SourceSulfatePpm,
            plan.SourceChloridePpm,
            plan.SourceBicarbonatePpm,
            plan.SourcePh,
            plan.TargetProfileId,
            plan.TargetProfileName,
            plan.TargetCalciumPpm,
            plan.TargetMagnesiumPpm,
            plan.TargetSodiumPpm,
            plan.TargetSulfatePpm,
            plan.TargetChloridePpm,
            plan.TargetBicarbonatePpm,
            plan.TargetMashPh,
            plan.MashGypsumGrams,
            plan.MashCalciumChlorideGrams,
            plan.MashEpsomSaltGrams,
            plan.MashTableSaltGrams,
            plan.MashBakingSodaGrams,
            plan.MashChalkGrams,
            plan.MashAcidType,
            plan.MashAcidAmountMlOrG,
            plan.SpargeGypsumGrams,
            plan.SpargeCalciumChlorideGrams,
            plan.SpargeEpsomSaltGrams,
            plan.SpargeTableSaltGrams,
            plan.SpargeBakingSodaGrams,
            plan.SpargeChalkGrams,
            plan.SpargeAcidType,
            plan.SpargeAcidAmountMlOrG,
            plan.ResultingCalciumPpm,
            plan.ResultingMagnesiumPpm,
            plan.ResultingSodiumPpm,
            plan.ResultingSulfatePpm,
            plan.ResultingChloridePpm,
            plan.ResultingBicarbonatePpm,
            plan.SulfateToChlorideRatio,
            balance,
            plan.ResidualAlkalinityPpm,
            plan.PredictedMashPh,
            plan.Notes,
            plan.UpdatedAt);
    }

    private static SaltAdditionsInput ToSaltInput(WaterSaltAdditionSetDto dto) => new(
        dto.GypsumGrams,
        dto.CalciumChlorideGrams,
        dto.EpsomSaltGrams,
        dto.TableSaltGrams,
        dto.BakingSodaGrams,
        dto.ChalkGrams,
        dto.AcidType,
        dto.AcidAmountMlOrG);

    private static WaterSaltAdditionSetDto ToSaltDto(SaltAdditionsInput input) => new(
        input.GypsumGrams,
        input.CalciumChlorideGrams,
        input.EpsomSaltGrams,
        input.TableSaltGrams,
        input.BakingSodaGrams,
        input.ChalkGrams,
        input.AcidType,
        input.AcidAmountMlOrG);

    private static WaterChemistryResultDto MapSummaryToDto(WaterCalculationSummary summary) => new(
        summary.MashResultingIons.CalciumPpm,
        summary.MashResultingIons.MagnesiumPpm,
        summary.MashResultingIons.SodiumPpm,
        summary.MashResultingIons.SulfatePpm,
        summary.MashResultingIons.ChloridePpm,
        summary.MashResultingIons.BicarbonatePpm,
        summary.TotalResultingIons.CalciumPpm,
        summary.TotalResultingIons.MagnesiumPpm,
        summary.TotalResultingIons.SodiumPpm,
        summary.TotalResultingIons.SulfatePpm,
        summary.TotalResultingIons.ChloridePpm,
        summary.TotalResultingIons.BicarbonatePpm,
        summary.SulfateToChlorideRatio,
        summary.BalanceProfile,
        summary.AlkalinityPpm,
        summary.ResidualAlkalinityPpm,
        summary.PredictedMashPh,
        summary.HasChalkWarning);

    private static string NormalizeCategory(string? category) => (category ?? "Target").Trim().ToLowerInvariant() switch
    {
        "source" => "Source",
        _ => "Target"
    };
}