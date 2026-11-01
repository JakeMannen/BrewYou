using SkiaSharp;
using System.Security.Cryptography;

namespace BrewYou.ApiService.Services.Labels;

public static class DefaultLabelGenerator
{
    public const string DefaultAssetKey = "default-recipe-label";

    public static (byte[] ImageData, string ContentType, int Width, int Height, string Sha256) GenerateDefaultLabel()
    {
        const int size = 1024;
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        // Background: radial gradient from dark amber/slate center to deep charcoal edge
        using var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(size / 2f, size / 2f),
                size * 0.7f,
                [new SKColor(48, 30, 18), new SKColor(22, 18, 15), new SKColor(14, 12, 10)],
                [0.0f, 0.6f, 1.0f],
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(0, 0, size, size, bgPaint);

        // Subtle decorative outer ring
        using var ringPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(217, 119, 6, 90), // amber-600 with opacity
            StrokeWidth = 4f
        };
        canvas.DrawCircle(size / 2f, size / 2f, size * 0.44f, ringPaint);

        // Inner golden ring
        using var innerRingPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(245, 158, 11, 140), // amber-500
            StrokeWidth = 2f
        };
        canvas.DrawCircle(size / 2f, size / 2f, size * 0.42f, innerRingPaint);

        // Center medallion circle
        using var medallionPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(32, 22, 14, 230)
        };
        canvas.DrawCircle(size / 2f, size / 2f, size * 0.38f, medallionPaint);

        // Stylized Hop Cone in Center
        DrawStylizedHop(canvas, size / 2f, size * 0.44f, size * 0.22f);

        // "BREWYOU" Brand text banner
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(254, 243, 199), // amber-100
            TextSize = 58f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            FakeBoldText = true
        };
        canvas.DrawText("BREWYOU", size / 2f, size * 0.68f, textPaint);

        // Subtitle "CRAFT BREWING"
        using var subTextPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(217, 119, 6), // amber-600
            TextSize = 26f,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal)
        };
        canvas.DrawText("ARTISAN CRAFT BEER", size / 2f, size * 0.74f, subTextPaint);

        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Webp, 95);
        var bytes = data.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        return (bytes, "image/webp", size, size, sha256);
    }

    private static void DrawStylizedHop(SKCanvas canvas, float cx, float cy, float radius)
    {
        // Stylized hop cone made of overlapping petals
        using var petalFill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(245, 158, 11, 210) // amber-500
        };
        using var petalStroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(251, 191, 36), // amber-400
            StrokeWidth = 3f
        };

        // Draw hop cone leaves from top to bottom
        (float dx, float dy, float scale)[] petals =
        [
            (0f, -radius * 0.55f, 0.45f),
            (-radius * 0.35f, -radius * 0.25f, 0.5f),
            (radius * 0.35f, -radius * 0.25f, 0.5f),
            (-radius * 0.45f, radius * 0.05f, 0.55f),
            (radius * 0.45f, radius * 0.05f, 0.55f),
            (0f, -radius * 0.05f, 0.58f),
            (-radius * 0.25f, radius * 0.32f, 0.5f),
            (radius * 0.25f, radius * 0.32f, 0.5f),
            (0f, radius * 0.45f, 0.42f),
            (0f, radius * 0.65f, 0.3f)
        ];

        foreach (var (dx, dy, s) in petals)
        {
            using var path = new SKPath();
            var px = cx + dx;
            var py = cy + dy;
            var r = radius * s;
            path.MoveTo(px, py - r);
            path.QuadTo(px + r * 1.2f, py, px, py + r * 1.3f);
            path.QuadTo(px - r * 1.2f, py, px, py - r);
            path.Close();

            canvas.DrawPath(path, petalFill);
            canvas.DrawPath(path, petalStroke);
        }
    }
}