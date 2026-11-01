using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services.Labels;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SkiaSharp;

namespace BrewYou.ApiService.Tests;

public class LabelImageProcessorTests
{
    private readonly SkiaLabelImageProcessor _processor = new();

    private static byte[] CreateTestBitmapBytes(int width, int height, SKColor color, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(color);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    [Fact]
    public void Normalize_WithValidPng_NormalizesToWebP()
    {
        var inputBytes = CreateTestBitmapBytes(400, 300, SKColors.Crimson, SKEncodedImageFormat.Png);
        using var ms = new MemoryStream(inputBytes);

        var result = _processor.Normalize(ms, "test.png");

        result.ContentType.Should().Be("image/webp");
        result.Width.Should().Be(400);
        result.Height.Should().Be(300);
        result.Sha256.Should().NotBeNullOrWhiteSpace();
        result.Bytes.Length.Should().BeGreaterThan(0);

        // Verify magic bytes are RIFF...WEBP
        result.Bytes[0..4].Should().Equal("RIFF"u8.ToArray());
        result.Bytes[8..12].Should().Equal("WEBP"u8.ToArray());
    }

    [Fact]
    public void Normalize_WithValidJpeg_NormalizesToWebP()
    {
        var inputBytes = CreateTestBitmapBytes(500, 500, SKColors.ForestGreen, SKEncodedImageFormat.Jpeg);
        using var ms = new MemoryStream(inputBytes);

        var result = _processor.Normalize(ms, "test.jpg");

        result.ContentType.Should().Be("image/webp");
        result.Width.Should().Be(500);
        result.Height.Should().Be(500);
        result.Bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Normalize_WithInvalidMagicBytes_ThrowsBadHttpRequestException()
    {
        var fakeBytes = "Not an image, just plain text payload for testing"u8.ToArray();
        using var ms = new MemoryStream(fakeBytes);

        var act = () => _processor.Normalize(ms, "fake.jpg");

        act.Should().Throw<BadHttpRequestException>()
            .WithMessage("*Unsupported image format*");
    }

    [Fact]
    public void Normalize_WithDimensionsAbove4096_ScalesDownTo4096()
    {
        // 5000 x 2500 -> scaled to 4096 x 2048
        var largeBytes = CreateTestBitmapBytes(5000, 2500, SKColors.Orange, SKEncodedImageFormat.Png);
        using var ms = new MemoryStream(largeBytes);

        var result = _processor.Normalize(ms, "large.png");

        result.Width.Should().Be(4096);
        result.Height.Should().Be(2048);
    }

    [Fact]
    public void RenderRendition_RoundWebP_ProducesCircularImageWithAlpha()
    {
        var sourceBytes = CreateTestBitmapBytes(600, 600, SKColors.Navy, SKEncodedImageFormat.Png);
        var normalized = _processor.Normalize(new MemoryStream(sourceBytes), "navy.png");

        var renderedBytes = _processor.RenderRendition(
            normalized.Bytes,
            crop: new LabelCrop(0.1m, 0.1m, 0.8m, 0.8m),
            targetWidth: 256,
            targetHeight: 256,
            format: "webp",
            isRound: true);

        renderedBytes.Length.Should().BeGreaterThan(0);

        // Verify corner pixel is transparent
        using var bmp = SKBitmap.Decode(renderedBytes);
        bmp.Should().NotBeNull();
        bmp.Width.Should().Be(256);
        bmp.Height.Should().Be(256);
        var corner = bmp.GetPixel(2, 2);
        corner.Alpha.Should().Be(0, "corners of round WebP must be transparent");
    }

    [Fact]
    public void RenderRendition_RoundJpeg_ProducesBlackBackground()
    {
        var sourceBytes = CreateTestBitmapBytes(400, 400, SKColors.Goldenrod, SKEncodedImageFormat.Png);
        var normalized = _processor.Normalize(new MemoryStream(sourceBytes), "gold.png");

        var renderedBytes = _processor.RenderRendition(
            normalized.Bytes,
            crop: LabelCrop.Default,
            targetWidth: 240,
            targetHeight: 240,
            format: "jpeg",
            isRound: true);

        renderedBytes.Length.Should().BeGreaterThan(0);

        using var bmp = SKBitmap.Decode(renderedBytes);
        bmp.Should().NotBeNull();
        bmp.Width.Should().Be(240);
        bmp.Height.Should().Be(240);
        var corner = bmp.GetPixel(2, 2);
        corner.Red.Should().BeLessThan(10);
        corner.Green.Should().BeLessThan(10);
        corner.Blue.Should().BeLessThan(10);
    }

    [Fact]
    public void RenderRendition_WithAbvOverlay_RendersWithoutErrors()
    {
        var sourceBytes = CreateTestBitmapBytes(600, 600, SKColors.CadetBlue, SKEncodedImageFormat.Png);
        var normalized = _processor.Normalize(new MemoryStream(sourceBytes), "blue.png");

        var renderedBytes = _processor.RenderRendition(
            normalized.Bytes,
            crop: LabelCrop.Default,
            targetWidth: 320,
            targetHeight: 240,
            format: "webp",
            isRound: false,
            abvOverlay: "6.8% ABV");

        renderedBytes.Length.Should().BeGreaterThan(0);

        using var bmp = SKBitmap.Decode(renderedBytes);
        bmp.Should().NotBeNull();
        bmp.Width.Should().Be(320);
        bmp.Height.Should().Be(240);
    }

    [Fact]
    public void DefaultLabelGenerator_ProducesValidHighResWebP()
    {
        var (defaultBytes, contentType, width, height, sha) = DefaultLabelGenerator.GenerateDefaultLabel();
        defaultBytes.Should().NotBeNull();
        defaultBytes.Length.Should().BeGreaterThan(1000);
        contentType.Should().Be("image/webp");
        sha.Should().NotBeNullOrWhiteSpace();

        using var bmp = SKBitmap.Decode(defaultBytes);
        bmp.Should().NotBeNull();
        bmp.Width.Should().Be(1024);
        bmp.Height.Should().Be(1024);

        // Center pixel should be within the golden BrewYou emblem
        var centerPixel = bmp.GetPixel(512, 512);
        centerPixel.Red.Should().BeGreaterThan(100);
        centerPixel.Green.Should().BeGreaterThan(60);
    }
}