using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Data.Entities;

public record LabelCrop(decimal X, decimal Y, decimal Width, decimal Height)
{
    public static LabelCrop Default => new(0m, 0m, 1m, 1m);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DisplayOrientation
{
    Landscape,
    Portrait
}

public class RecipeLabel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }

    public byte[] ImageData { get; set; } = [];
    public string ContentType { get; set; } = "image/webp";
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public int SizeBytes { get; set; }
    public required string Sha256 { get; set; }
    public int Version { get; set; } = 1;
    public string? OriginalFileName { get; set; }

    public LabelCrop BottleCrop { get; set; } = LabelCrop.Default;
    public LabelCrop RoundCrop { get; set; } = LabelCrop.Default;
    public LabelCrop RectCrop { get; set; } = LabelCrop.Default;
    public DisplayOrientation RectOrientation { get; set; } = DisplayOrientation.Landscape;

    public decimal BottleWidthMm { get; set; } = 90.0m;
    public decimal BottleHeightMm { get; set; } = 120.0m;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}