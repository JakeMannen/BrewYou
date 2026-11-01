using BrewYou.ApiService.Data.Entities;
using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Endpoints;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LabelVariant
{
    Bottle,
    Round,
    Rect,
    Card,
    Original
}

public record LabelCropDto(decimal X, decimal Y, decimal Width, decimal Height)
{
    public static LabelCropDto Default => new(0m, 0m, 1m, 1m);

    public LabelCrop ToEntity() => new(X, Y, Width, Height);

    public static LabelCropDto FromEntity(LabelCrop crop) => new(crop.X, crop.Y, crop.Width, crop.Height);
}

public record RecipeLabelDto(
    Guid RecipeId,
    bool IsDefault,
    int Version,
    DateTime? UpdatedAt,
    int WidthPx,
    int HeightPx,
    int? SizeBytes,
    LabelCropDto BottleCrop,
    LabelCropDto RoundCrop,
    LabelCropDto RectCrop,
    DisplayOrientation RectOrientation,
    decimal BottleWidthMm,
    decimal BottleHeightMm,
    Dictionary<string, string> ImageUrls,
    string? OriginalFileName = null
);

public record UpdateRecipeLabelSettingsRequest(
    LabelCropDto? BottleCrop = null,
    LabelCropDto? RoundCrop = null,
    LabelCropDto? RectCrop = null,
    DisplayOrientation? RectOrientation = null,
    decimal? BottleWidthMm = null,
    decimal? BottleHeightMm = null
);

public record AssignTapRequest(Guid? BatchId = null, Guid? KegId = null);

public record AssignKegBatchRequest(Guid? BatchId = null, decimal? VolumeLiters = null);

public record TapDisplayLinkDto(
    string ManifestUrl,
    string ImageUrl,
    string KioskUrl
);

public record TapDisplayManifestDto(
    string Status,
    string Shape,
    int WidthPx,
    int HeightPx,
    string? BeerName,
    decimal? ActualAbv,
    string LabelVersion,
    string ImageUrl,
    string? KegName = null,
    string? KegFillLevel = null,
    decimal? KegCapacityLiters = null,
    decimal? KegRemainingLiters = null
);