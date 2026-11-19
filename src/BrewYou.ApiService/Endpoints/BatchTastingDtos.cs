using FluentValidation;

namespace BrewYou.ApiService.Endpoints;

public record OffFlavorDescriptorDto(
    string Key,
    string DisplayName,
    string SensoryCue,
    IReadOnlyList<string> LikelyCauses,
    IReadOnlyList<string> RemediationFixes);

public record BatchTastingDto(
    Guid Id,
    Guid BatchId,
    DateTime TastedAt,
    string? SampleSource,
    decimal AromaScore,
    string? AromaNotes,
    decimal AppearanceScore,
    string? AppearanceNotes,
    decimal FlavorScore,
    string? FlavorNotes,
    decimal MouthfeelScore,
    string? MouthfeelNotes,
    decimal OverallScore,
    string? OverallNotes,
    decimal TotalScore,
    string BjcpScoreBand,
    int StarRating,
    bool WouldRebrew,
    List<string> OffFlavors,
    decimal? ServingTemperatureC,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record BatchTastingSummaryDto(
    int Count,
    decimal? AverageStarRating,
    decimal? AverageBjcpScore,
    int? LatestStarRating,
    decimal? LatestBjcpScore,
    bool? LatestWouldRebrew,
    List<string> ReportedOffFlavors);

public record CreateBatchTastingRequest(
    DateTime? TastedAt = null,
    string? SampleSource = "Keg",
    decimal AromaScore = 8m,
    string? AromaNotes = null,
    decimal AppearanceScore = 2.5m,
    string? AppearanceNotes = null,
    decimal FlavorScore = 14m,
    string? FlavorNotes = null,
    decimal MouthfeelScore = 3.5m,
    string? MouthfeelNotes = null,
    decimal OverallScore = 7m,
    string? OverallNotes = null,
    int StarRating = 4,
    bool WouldRebrew = true,
    List<string>? OffFlavors = null,
    decimal? ServingTemperatureC = null);

public record UpdateBatchTastingRequest(
    DateTime? TastedAt = null,
    string? SampleSource = null,
    decimal? AromaScore = null,
    string? AromaNotes = null,
    decimal? AppearanceScore = null,
    string? AppearanceNotes = null,
    decimal? FlavorScore = null,
    string? FlavorNotes = null,
    decimal? MouthfeelScore = null,
    string? MouthfeelNotes = null,
    decimal? OverallScore = null,
    string? OverallNotes = null,
    int? StarRating = null,
    bool? WouldRebrew = null,
    List<string>? OffFlavors = null,
    decimal? ServingTemperatureC = null);

public class CreateBatchTastingRequestValidator : AbstractValidator<CreateBatchTastingRequest>
{
    public CreateBatchTastingRequestValidator()
    {
        RuleFor(x => x.AromaScore).InclusiveBetween(0m, 12m);
        RuleFor(x => x.AppearanceScore).InclusiveBetween(0m, 3m);
        RuleFor(x => x.FlavorScore).InclusiveBetween(0m, 20m);
        RuleFor(x => x.MouthfeelScore).InclusiveBetween(0m, 5m);
        RuleFor(x => x.OverallScore).InclusiveBetween(0m, 10m);
        RuleFor(x => x.StarRating).InclusiveBetween(1, 5);
        RuleFor(x => x.SampleSource).MaximumLength(40);
        RuleFor(x => x.AromaNotes).MaximumLength(1000);
        RuleFor(x => x.AppearanceNotes).MaximumLength(1000);
        RuleFor(x => x.FlavorNotes).MaximumLength(1000);
        RuleFor(x => x.MouthfeelNotes).MaximumLength(1000);
        RuleFor(x => x.OverallNotes).MaximumLength(1000);
        When(x => x.ServingTemperatureC.HasValue, () =>
            RuleFor(x => x.ServingTemperatureC!.Value).InclusiveBetween(-5m, 40m));
    }
}

public class UpdateBatchTastingRequestValidator : AbstractValidator<UpdateBatchTastingRequest>
{
    public UpdateBatchTastingRequestValidator()
    {
        When(x => x.AromaScore.HasValue, () => RuleFor(x => x.AromaScore!.Value).InclusiveBetween(0m, 12m));
        When(x => x.AppearanceScore.HasValue, () => RuleFor(x => x.AppearanceScore!.Value).InclusiveBetween(0m, 3m));
        When(x => x.FlavorScore.HasValue, () => RuleFor(x => x.FlavorScore!.Value).InclusiveBetween(0m, 20m));
        When(x => x.MouthfeelScore.HasValue, () => RuleFor(x => x.MouthfeelScore!.Value).InclusiveBetween(0m, 5m));
        When(x => x.OverallScore.HasValue, () => RuleFor(x => x.OverallScore!.Value).InclusiveBetween(0m, 10m));
        When(x => x.StarRating.HasValue, () => RuleFor(x => x.StarRating!.Value).InclusiveBetween(1, 5));
        When(x => x.ServingTemperatureC.HasValue, () => RuleFor(x => x.ServingTemperatureC!.Value).InclusiveBetween(-5m, 40m));
    }
}