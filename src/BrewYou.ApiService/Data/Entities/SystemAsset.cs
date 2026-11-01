namespace BrewYou.ApiService.Data.Entities;

public class SystemAsset
{
    public required string Key { get; set; }
    public byte[] ImageData { get; set; } = [];
    public string ContentType { get; set; } = "image/png";
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public required string Sha256 { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}