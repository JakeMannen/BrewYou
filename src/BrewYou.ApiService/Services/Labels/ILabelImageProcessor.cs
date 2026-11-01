using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services.Labels;

public record NormalizedImage(
    byte[] Bytes,
    string ContentType,
    int Width,
    int Height,
    int SizeBytes,
    string Sha256
);

public interface ILabelImageProcessor
{
    NormalizedImage Normalize(Stream inputStream, string? fileName);

    byte[] RenderRendition(
        byte[] masterBytes,
        LabelCrop crop,
        int targetWidth,
        int targetHeight,
        string format,
        bool isRound = false,
        string? abvOverlay = null
    );
}