using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.Http;
using SkiaSharp;
using System.Security.Cryptography;

namespace BrewYou.ApiService.Services.Labels;

public class SkiaLabelImageProcessor : ILabelImageProcessor
{
    private const int MaxDimensionPx = 10_000;
    private const long MaxPixels = 40_000_000; // 40 MP decompression limit
    private const int MaxMasterDimensionPx = 4096;

    public NormalizedImage Normalize(Stream inputStream, string? fileName)
    {
        // 1. Read input stream into memory
        using var memoryStream = new MemoryStream();
        inputStream.CopyTo(memoryStream);
        var bytes = memoryStream.ToArray();

        if (bytes.Length < 16)
        {
            throw new BadHttpRequestException("Image file is too small or corrupt.", StatusCodes.Status400BadRequest);
        }

        // 2. Validate magic bytes (strictly PNG, JPEG, WebP)
        var detectedType = DetectContentType(bytes);

        // 3. Inspect codec & headers without allocating full pixel buffer
        using var codecStream = new MemoryStream(bytes);
        using var codec = SKCodec.Create(codecStream);
        if (codec == null)
        {
            throw new BadHttpRequestException("Failed to decode image. Corrupted or invalid image data.", StatusCodes.Status400BadRequest);
        }

        var width = codec.Info.Width;
        var height = codec.Info.Height;

        if (width <= 0 || height <= 0)
        {
            throw new BadHttpRequestException("Invalid image dimensions.", StatusCodes.Status400BadRequest);
        }

        if (width > MaxDimensionPx || height > MaxDimensionPx || ((long)width * height) > MaxPixels)
        {
            throw new BadHttpRequestException(
                $"Image dimensions ({width}x{height}) exceed maximum allowed dimensions ({MaxDimensionPx}x{MaxDimensionPx} or 40 megapixels).",
                StatusCodes.Status400BadRequest);
        }

        // 4. Decode pixel data
        using var decodedBitmap = SKBitmap.Decode(codec);
        if (decodedBitmap == null)
        {
            throw new BadHttpRequestException("Failed to decode image raster.", StatusCodes.Status400BadRequest);
        }

        // 5. Apply EXIF orientation
        using var orientedBitmap = ApplyExifOrientation(decodedBitmap, codec.EncodedOrigin);

        // 6. Scale down if larger than max master dimension
        SKBitmap normalizedBitmap;
        bool needsDispose = false;

        int currentW = orientedBitmap.Width;
        int currentH = orientedBitmap.Height;

        if (currentW > MaxMasterDimensionPx || currentH > MaxMasterDimensionPx)
        {
            float scale = (float)MaxMasterDimensionPx / Math.Max(currentW, currentH);
            int newW = Math.Max(1, (int)(currentW * scale));
            int newH = Math.Max(1, (int)(currentH * scale));

            var scaled = new SKBitmap(newW, newH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(scaled);
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            canvas.DrawBitmap(orientedBitmap, new SKRect(0, 0, newW, newH), paint);

            normalizedBitmap = scaled;
            needsDispose = true;
        }
        else
        {
            normalizedBitmap = orientedBitmap;
        }

        try
        {
            // 7. Re-encode as WebP (stripping all EXIF, metadata, and polyglots)
            using var image = SKImage.FromBitmap(normalizedBitmap);
            using var encodedData = image.Encode(SKEncodedImageFormat.Webp, 92);
            var normalizedBytes = encodedData.ToArray();
            var sha256 = Convert.ToHexString(SHA256.HashData(normalizedBytes)).ToLowerInvariant();

            return new NormalizedImage(
                Bytes: normalizedBytes,
                ContentType: "image/webp",
                Width: normalizedBitmap.Width,
                Height: normalizedBitmap.Height,
                SizeBytes: normalizedBytes.Length,
                Sha256: sha256
            );
        }
        finally
        {
            if (needsDispose)
            {
                normalizedBitmap.Dispose();
            }
        }
    }

    public byte[] RenderRendition(
        byte[] masterBytes,
        LabelCrop crop,
        int targetWidth,
        int targetHeight,
        string format,
        bool isRound = false,
        string? abvOverlay = null)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentException("Target dimensions must be greater than zero.");
        }

        using var masterBitmap = SKBitmap.Decode(masterBytes);
        if (masterBitmap == null)
        {
            throw new InvalidOperationException("Failed to decode master image.");
        }

        // Clamp crop boundaries
        float mw = masterBitmap.Width;
        float mh = masterBitmap.Height;

        float cx = Math.Clamp((float)crop.X, 0f, 0.99f) * mw;
        float cy = Math.Clamp((float)crop.Y, 0f, 0.99f) * mh;
        float cw = Math.Clamp((float)crop.Width, 0.01f, 1f) * mw;
        float ch = Math.Clamp((float)crop.Height, 0.01f, 1f) * mh;

        if (cx + cw > mw) cw = mw - cx;
        if (cy + ch > mh) ch = mh - cy;

        var sourceRect = new SKRect(cx, cy, cx + cw, cy + ch);
        var destRect = new SKRect(0, 0, targetWidth, targetHeight);

        var imageInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        var normalizedFormat = format.Trim().ToLowerInvariant();
        bool isJpeg = normalizedFormat is "jpeg" or "jpg";

        if (isRound)
        {
            if (isJpeg)
            {
                // Fill black background for JPEG round screens
                canvas.Clear(SKColors.Black);
            }
            else
            {
                canvas.Clear(SKColors.Transparent);
            }

            // Circular clip path with antialiasing
            using var circlePath = new SKPath();
            float radius = Math.Min(targetWidth, targetHeight) / 2f;
            circlePath.AddCircle(targetWidth / 2f, targetHeight / 2f, radius);
            canvas.ClipPath(circlePath, antialias: true);
        }
        else
        {
            canvas.Clear(isJpeg ? SKColors.White : SKColors.Transparent);
        }

        using var drawPaint = new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = true
        };
        canvas.DrawBitmap(masterBitmap, sourceRect, destRect, drawPaint);

        // Optional ABV typography overlay (from actual batch measured ABV)
        if (!string.IsNullOrWhiteSpace(abvOverlay))
        {
            DrawAbvOverlay(canvas, targetWidth, targetHeight, abvOverlay.Trim(), isRound);
        }

        canvas.Flush();
        using var snapshot = surface.Snapshot();

        var skFormat = normalizedFormat switch
        {
            "png" => SKEncodedImageFormat.Png,
            "jpeg" or "jpg" => SKEncodedImageFormat.Jpeg,
            "webp" or _ => SKEncodedImageFormat.Webp
        };

        using var outputData = snapshot.Encode(skFormat, 90);
        return outputData.ToArray();
    }

    private static void DrawAbvOverlay(SKCanvas canvas, int width, int height, string text, bool isRound)
    {
        // Scale font proportionally to image height
        float fontSize = Math.Clamp(height * 0.075f, 16f, 72f);
        using var fontPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            TextSize = fontSize,
            FakeBoldText = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        var textBounds = new SKRect();
        fontPaint.MeasureText(text, ref textBounds);

        float paddingX = fontSize * 0.6f;
        float paddingY = fontSize * 0.35f;
        float pillW = textBounds.Width + (paddingX * 2);
        float pillH = textBounds.Height + (paddingY * 2);

        float pillX = (width - pillW) / 2f;
        float pillY = isRound
            ? (height * 0.76f) - (pillH / 2f)
            : height - pillH - (height * 0.06f);

        // Rounded pill background
        using var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(15, 23, 42, 220) // Slate-900 with high opacity
        };
        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(217, 119, 6, 200), // Amber-600
            StrokeWidth = Math.Clamp(fontSize * 0.08f, 1.5f, 4f)
        };

        var pillRect = new SKRoundRect(new SKRect(pillX, pillY, pillX + pillW, pillY + pillH), pillH / 2f, pillH / 2f);
        canvas.DrawRoundRect(pillRect, bgPaint);
        canvas.DrawRoundRect(pillRect, borderPaint);

        // Centered text
        float textX = pillX + paddingX - textBounds.Left;
        float textY = pillY + paddingY - textBounds.Top;
        canvas.DrawText(text, textX, textY, fontPaint);
    }

    private static string DetectContentType(byte[] header)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return "image/png";
        }

        // JPEG: FF D8 FF
        if (header.Length >= 3 &&
            header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // WebP: RIFF ... WEBP
        if (header.Length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return "image/webp";
        }

        throw new BadHttpRequestException("Unsupported image format. Allowed formats are PNG, JPEG, and WebP.", StatusCodes.Status415UnsupportedMediaType);
    }

    private static SKBitmap ApplyExifOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return bitmap;
        }

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                return Flip(bitmap, horizontal: true, vertical: false);
            case SKEncodedOrigin.BottomRight:
                return Rotate(bitmap, 180);
            case SKEncodedOrigin.BottomLeft:
                return Flip(bitmap, horizontal: false, vertical: true);
            case SKEncodedOrigin.LeftTop:
                using (var rot = Rotate(bitmap, 90))
                {
                    return Flip(rot, horizontal: true, vertical: false);
                }
            case SKEncodedOrigin.RightTop:
                return Rotate(bitmap, 90);
            case SKEncodedOrigin.RightBottom:
                using (var rot = Rotate(bitmap, 270))
                {
                    return Flip(rot, horizontal: true, vertical: false);
                }
            case SKEncodedOrigin.LeftBottom:
                return Rotate(bitmap, 270);
            default:
                return bitmap;
        }
    }

    private static SKBitmap Rotate(SKBitmap bitmap, float degrees)
    {
        bool swap = Math.Abs(degrees - 90) < 0.1f || Math.Abs(degrees - 270) < 0.1f;
        int w = swap ? bitmap.Height : bitmap.Width;
        int h = swap ? bitmap.Width : bitmap.Height;

        var rotated = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(w / 2f, h / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, 0, 0);
        return rotated;
    }

    private static SKBitmap Flip(SKBitmap bitmap, bool horizontal, bool vertical)
    {
        var flipped = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(flipped);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(horizontal ? -1 : 1, vertical ? -1 : 1, bitmap.Width / 2f, bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, 0, 0);
        return flipped;
    }
}