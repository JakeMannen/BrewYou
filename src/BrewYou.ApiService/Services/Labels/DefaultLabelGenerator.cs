using SkiaSharp;
using System.Reflection;
using System.Security.Cryptography;

namespace BrewYou.ApiService.Services.Labels;

public static class DefaultLabelGenerator
{
    public const string DefaultAssetKey = "default-recipe-label";

    private static readonly Lazy<(byte[] ImageData, string ContentType, int Width, int Height, string Sha256)> CachedLabel =
        new(GenerateDefaultLabelInternal);

    public static string CurrentSha256 => CachedLabel.Value.Sha256;

    public static (byte[] ImageData, string ContentType, int Width, int Height, string Sha256) GenerateDefaultLabel() =>
        CachedLabel.Value;

    private static (byte[] ImageData, string ContentType, int Width, int Height, string Sha256) GenerateDefaultLabelInternal()
    {
        const int size = 1024;
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        // 1. Velvet craft brewery background: deep roasted-malt / obsidian radial gradient
        using var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(size / 2f, size / 2f),
                size * 0.72f,
                [new SKColor(36, 26, 20), new SKColor(20, 16, 14), new SKColor(11, 9, 8)],
                [0.0f, 0.55f, 1.0f],
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(0, 0, size, size, bgPaint);

        // 2. Warm amber ambient glow behind the centerpiece badge
        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(size / 2f, size / 2f),
                size * 0.44f,
                [new SKColor(217, 119, 6, 45), new SKColor(217, 119, 6, 12), SKColors.Transparent],
                [0.0f, 0.65f, 1.0f],
                SKShaderTileMode.Clamp)
        };
        canvas.DrawCircle(size / 2f, size / 2f, size * 0.44f, glowPaint);

        // 3. Subtle artisan framing rings (visible on circular tap screens and full bottle labels)
        using var outerRingPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(217, 119, 6, 75), // amber-600
            StrokeWidth = 2.5f
        };
        canvas.DrawCircle(size / 2f, size / 2f, size * 0.465f, outerRingPaint);

        using var innerRingPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(245, 158, 11, 35), // amber-500
            StrokeWidth = 1f
        };
        canvas.DrawCircle(size / 2f, size / 2f, size * 0.45f, innerRingPaint);

        // 4. Draw the authentic BrewYou Logo (loaded from embedded resource or local disk)
        using var logoBitmap = LoadLogoBitmap();
        if (logoBitmap == null)
        {
            throw new InvalidOperationException("Required default logo asset 'default-logo.png' could not be loaded from assembly resources or local disk.");
        }

        // Sized 500x500 centered: fits within 16:9 widescreen crops (height 576),
        // circular dial bezels (diameter 1024), and 4:5 bottle labels without clipping.
        const float logoSize = 500f;
        var destRect = new SKRect(
            (size - logoSize) / 2f,
            (size - logoSize) / 2f,
            (size + logoSize) / 2f,
            (size + logoSize) / 2f);

        using var logoPaint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };
        canvas.DrawBitmap(logoBitmap, destRect, logoPaint);

        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Webp, 95);
        var bytes = data.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        return (bytes, "image/webp", size, size, sha256);
    }

    private static SKBitmap? LoadLogoBitmap()
    {
        var assembly = typeof(DefaultLabelGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("default-logo.png", StringComparison.OrdinalIgnoreCase) ||
                                 n.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                return SKBitmap.Decode(stream);
            }
        }

        // Fallback paths for filesystem access in test runners or local development
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "default-logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "default-logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "BrewYou.ApiService", "Assets", "default-logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "src", "BrewYou.ApiService", "Assets", "default-logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "src", "BrewYou.ApiService", "Assets", "default-logo.png"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "BrewYou.ApiService", "Assets", "default-logo.png")
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                using var fs = File.OpenRead(path);
                return SKBitmap.Decode(fs);
            }
        }

        return null;
    }
}