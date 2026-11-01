using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace BrewYou.ApiService.Services.Labels;

public class RecipeLabelService : IRecipeLabelService
{
    private readonly BrewYouDbContext _db;
    private readonly ILabelImageProcessor _imageProcessor;
    private readonly ILabelUrlSigner _urlSigner;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RecipeLabelService> _logger;

    private static readonly HashSet<int> AllowedSizes = [160, 240, 320, 360, 466, 480, 640, 720, 800, 960, 1080, 1280, 1920];
    private static readonly HashSet<string> AllowedFormats = ["png", "jpeg", "jpg", "webp"];

    public RecipeLabelService(
        BrewYouDbContext db,
        ILabelImageProcessor imageProcessor,
        ILabelUrlSigner urlSigner,
        IMemoryCache cache,
        ILogger<RecipeLabelService> logger)
    {
        _db = db;
        _imageProcessor = imageProcessor;
        _urlSigner = urlSigner;
        _cache = cache;
        _logger = logger;
    }

    public async Task<(RecipeAccessResult Result, RecipeLabelDto? Label, string? ErrorMessage)> GetRecipeLabelAsync(
        Guid recipeId, string? userId, string apiBaseUrl)
    {
        var recipe = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Label)
            .FirstOrDefaultAsync(r => r.Id == recipeId);

        if (recipe == null)
        {
            return (RecipeAccessResult.NotFound, null, "Recipe not found.");
        }

        if (!recipe.IsPublic && recipe.UserId != userId)
        {
            return (RecipeAccessResult.NotFound, null, "Recipe not found.");
        }

        if (recipe.Label == null)
        {
            // Fallback to seeded default system asset
            var defaultAsset = await GetDefaultAssetAsync();
            var defaultUrls = BuildDefaultImageUrls(apiBaseUrl, defaultAsset.Sha256);

            var defaultDto = new RecipeLabelDto(
                RecipeId: recipe.Id,
                IsDefault: true,
                Version: 1,
                UpdatedAt: null,
                WidthPx: defaultAsset.WidthPx,
                HeightPx: defaultAsset.HeightPx,
                SizeBytes: defaultAsset.ImageData.Length,
                BottleCrop: LabelCropDto.Default,
                RoundCrop: LabelCropDto.Default,
                RectCrop: LabelCropDto.Default,
                RectOrientation: DisplayOrientation.Landscape,
                BottleWidthMm: 90m,
                BottleHeightMm: 120m,
                ImageUrls: defaultUrls,
                OriginalFileName: null
            );

            return (RecipeAccessResult.Success, defaultDto, null);
        }

        var label = recipe.Label;
        var imageUrls = BuildSignedImageUrls(apiBaseUrl, recipe.Id, label.Version);

        var dto = new RecipeLabelDto(
            RecipeId: recipe.Id,
            IsDefault: false,
            Version: label.Version,
            UpdatedAt: label.UpdatedAt,
            WidthPx: label.WidthPx,
            HeightPx: label.HeightPx,
            SizeBytes: label.SizeBytes,
            BottleCrop: LabelCropDto.FromEntity(label.BottleCrop),
            RoundCrop: LabelCropDto.FromEntity(label.RoundCrop),
            RectCrop: LabelCropDto.FromEntity(label.RectCrop),
            RectOrientation: label.RectOrientation,
            BottleWidthMm: label.BottleWidthMm,
            BottleHeightMm: label.BottleHeightMm,
            ImageUrls: imageUrls,
            OriginalFileName: label.OriginalFileName
        );

        return (RecipeAccessResult.Success, dto, null);
    }

    public async Task<(RecipeAccessResult Result, RecipeLabelDto? Label, string? ErrorMessage)> UploadRecipeLabelAsync(
        Guid recipeId, Stream fileStream, string? originalFileName, UpdateRecipeLabelSettingsRequest? settings, string userId, string apiBaseUrl)
    {
        var recipe = await _db.Recipes
            .Include(r => r.Label)
            .FirstOrDefaultAsync(r => r.Id == recipeId);

        if (recipe == null)
        {
            return (RecipeAccessResult.NotFound, null, "Recipe not found.");
        }

        if (recipe.UserId != userId)
        {
            return (RecipeAccessResult.Forbidden, null, "Only the recipe owner can upload or edit label artwork.");
        }

        var normalized = _imageProcessor.Normalize(fileStream, originalFileName);

        var now = DateTime.UtcNow;
        if (recipe.Label == null)
        {
            recipe.Label = new RecipeLabel
            {
                RecipeId = recipe.Id,
                ImageData = normalized.Bytes,
                ContentType = normalized.ContentType,
                WidthPx = normalized.Width,
                HeightPx = normalized.Height,
                SizeBytes = normalized.SizeBytes,
                Sha256 = normalized.Sha256,
                Version = 1,
                OriginalFileName = SanitizeFileName(originalFileName),
                BottleCrop = settings?.BottleCrop?.ToEntity() ?? LabelCrop.Default,
                RoundCrop = settings?.RoundCrop?.ToEntity() ?? LabelCrop.Default,
                RectCrop = settings?.RectCrop?.ToEntity() ?? LabelCrop.Default,
                RectOrientation = settings?.RectOrientation ?? DisplayOrientation.Landscape,
                BottleWidthMm = settings?.BottleWidthMm ?? 90.0m,
                BottleHeightMm = settings?.BottleHeightMm ?? 120.0m,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.RecipeLabels.Add(recipe.Label);
        }
        else
        {
            var label = recipe.Label;
            label.ImageData = normalized.Bytes;
            label.ContentType = normalized.ContentType;
            label.WidthPx = normalized.Width;
            label.HeightPx = normalized.Height;
            label.SizeBytes = normalized.SizeBytes;
            label.Sha256 = normalized.Sha256;
            label.Version++;
            label.OriginalFileName = SanitizeFileName(originalFileName);
            if (settings?.BottleCrop != null) label.BottleCrop = settings.BottleCrop.ToEntity();
            if (settings?.RoundCrop != null) label.RoundCrop = settings.RoundCrop.ToEntity();
            if (settings?.RectCrop != null) label.RectCrop = settings.RectCrop.ToEntity();
            if (settings?.RectOrientation.HasValue == true) label.RectOrientation = settings.RectOrientation.Value;
            if (settings?.BottleWidthMm.HasValue == true) label.BottleWidthMm = settings.BottleWidthMm.Value;
            if (settings?.BottleHeightMm.HasValue == true) label.BottleHeightMm = settings.BottleHeightMm.Value;
            label.UpdatedAt = now;
        }

        recipe.UpdatedAt = now;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Recipe label uploaded for recipe {RecipeId} by user {UserId} (Version {Version})", recipeId, userId, recipe.Label.Version);

        return await GetRecipeLabelAsync(recipeId, userId, apiBaseUrl);
    }

    public async Task<(RecipeAccessResult Result, RecipeLabelDto? Label, string? ErrorMessage)> UpdateRecipeLabelSettingsAsync(
        Guid recipeId, UpdateRecipeLabelSettingsRequest request, string userId, string apiBaseUrl)
    {
        var recipe = await _db.Recipes
            .Include(r => r.Label)
            .FirstOrDefaultAsync(r => r.Id == recipeId);

        if (recipe == null)
        {
            return (RecipeAccessResult.NotFound, null, "Recipe not found.");
        }

        if (recipe.UserId != userId)
        {
            return (RecipeAccessResult.Forbidden, null, "Only the recipe owner can update label settings.");
        }

        if (recipe.Label == null)
        {
            return (RecipeAccessResult.NotFound, null, "Recipe does not have a custom label artwork. Upload an image first.");
        }

        var label = recipe.Label;
        if (request.BottleCrop != null) label.BottleCrop = request.BottleCrop.ToEntity();
        if (request.RoundCrop != null) label.RoundCrop = request.RoundCrop.ToEntity();
        if (request.RectCrop != null) label.RectCrop = request.RectCrop.ToEntity();
        if (request.RectOrientation.HasValue) label.RectOrientation = request.RectOrientation.Value;
        if (request.BottleWidthMm.HasValue) label.BottleWidthMm = request.BottleWidthMm.Value;
        if (request.BottleHeightMm.HasValue) label.BottleHeightMm = request.BottleHeightMm.Value;

        label.Version++;
        label.UpdatedAt = DateTime.UtcNow;
        recipe.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Recipe label settings updated for recipe {RecipeId} by user {UserId}", recipeId, userId);

        return await GetRecipeLabelAsync(recipeId, userId, apiBaseUrl);
    }

    public async Task<RecipeAccessResult> DeleteRecipeLabelAsync(Guid recipeId, string userId)
    {
        var recipe = await _db.Recipes
            .Include(r => r.Label)
            .FirstOrDefaultAsync(r => r.Id == recipeId);

        if (recipe == null)
        {
            return RecipeAccessResult.NotFound;
        }

        if (recipe.UserId != userId)
        {
            return RecipeAccessResult.Forbidden;
        }

        if (recipe.Label != null)
        {
            _db.RecipeLabels.Remove(recipe.Label);
            recipe.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Recipe label deleted for recipe {RecipeId} by user {UserId}", recipeId, userId);
        }

        return RecipeAccessResult.Success;
    }

    public async Task<(byte[]? Bytes, string ContentType, string ETag)?> GetRenditionAsync(
        Guid recipeId, string variant, int? size, string? format, string? sig, long? exp, int? v)
    {
        var targetSize = size ?? 640;
        if (!AllowedSizes.Contains(targetSize))
        {
            targetSize = 640;
        }

        var targetFormat = NormalizeFormat(format);

        // Verify URL signature
        if (string.IsNullOrEmpty(sig) || !exp.HasValue || !v.HasValue)
        {
            return null;
        }

        if (!_urlSigner.VerifySignature(recipeId, variant, targetSize, v.Value, exp.Value, sig))
        {
            return null;
        }

        var label = await _db.RecipeLabels
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.RecipeId == recipeId);

        if (label == null || label.Version != v.Value)
        {
            return null;
        }

        var (crop, isRound, w, h) = ResolveCropAndDimensions(variant, label, targetSize);
        var cacheKey = FormattableString.Invariant($"lbl:{label.Id}:{label.Version}:{variant}:{targetSize}:{targetFormat}:{isRound}:{w}x{h}");

        if (_cache.TryGetValue(cacheKey, out byte[]? cached) && cached != null)
        {
            var cachedEtag = $"\"{label.Sha256}-{variant}-{targetSize}-{targetFormat}\"";
            return (cached, GetMimeType(targetFormat), cachedEtag);
        }

        var rendered = _imageProcessor.RenderRendition(label.ImageData, crop, w, h, targetFormat, isRound: isRound);
        _cache.Set(cacheKey, rendered, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(15),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
        });

        var etag = $"\"{label.Sha256}-{variant}-{targetSize}-{targetFormat}\"";
        return (rendered, GetMimeType(targetFormat), etag);
    }

    public async Task<(byte[]? Bytes, string ContentType, string ETag)?> GetDefaultRenditionAsync(
        string variant, int? size, string? format, string? v)
    {
        var targetSize = size ?? 640;
        if (!AllowedSizes.Contains(targetSize))
        {
            targetSize = 640;
        }

        var targetFormat = NormalizeFormat(format);
        var defaultAsset = await GetDefaultAssetAsync();

        var (crop, isRound, w, h) = ResolveDefaultCropAndDimensions(variant, targetSize);
        var cacheKey = FormattableString.Invariant($"lbl_def:{defaultAsset.Sha256}:{variant}:{targetSize}:{targetFormat}:{isRound}:{w}x{h}");

        if (_cache.TryGetValue(cacheKey, out byte[]? cached) && cached != null)
        {
            var cachedEtag = $"\"{defaultAsset.Sha256}-{variant}-{targetSize}-{targetFormat}\"";
            return (cached, GetMimeType(targetFormat), cachedEtag);
        }

        var rendered = _imageProcessor.RenderRendition(defaultAsset.ImageData, crop, w, h, targetFormat, isRound: isRound);
        _cache.Set(cacheKey, rendered, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
        });

        var etag = $"\"{defaultAsset.Sha256}-{variant}-{targetSize}-{targetFormat}\"";
        return (rendered, GetMimeType(targetFormat), etag);
    }

    private async Task<SystemAsset> GetDefaultAssetAsync()
    {
        var asset = await _db.SystemAssets.FirstOrDefaultAsync(a => a.Key == DefaultLabelGenerator.DefaultAssetKey);
        if (asset != null) return asset;

        // On-demand self-healing if table wasn't seeded yet
        var (imageData, contentType, width, height, sha256) = DefaultLabelGenerator.GenerateDefaultLabel();
        asset = new SystemAsset
        {
            Key = DefaultLabelGenerator.DefaultAssetKey,
            ImageData = imageData,
            ContentType = contentType,
            WidthPx = width,
            HeightPx = height,
            Sha256 = sha256,
            UpdatedAt = DateTime.UtcNow
        };
        _db.SystemAssets.Add(asset);
        await _db.SaveChangesAsync();
        return asset;
    }

    private Dictionary<string, string> BuildSignedImageUrls(string baseUrl, Guid recipeId, int version)
    {
        var card = _urlSigner.BuildSignedImageUrl(baseUrl, recipeId, "card", 640, version);
        var bottle = _urlSigner.BuildSignedImageUrl(baseUrl, recipeId, "bottle", 640, version);
        var round = _urlSigner.BuildSignedImageUrl(baseUrl, recipeId, "round", 480, version);
        var rect = _urlSigner.BuildSignedImageUrl(baseUrl, recipeId, "rect", 800, version);
        var original = _urlSigner.BuildSignedImageUrl(baseUrl, recipeId, "original", 1280, version);

        return new Dictionary<string, string>
        {
            ["card"] = card,
            ["bottle"] = bottle,
            ["round"] = round,
            ["rect"] = rect,
            ["original"] = original,
            ["Card"] = card,
            ["Bottle"] = bottle,
            ["Round"] = round,
            ["Rect"] = rect,
            ["Original"] = original
        };
    }

    private Dictionary<string, string> BuildDefaultImageUrls(string baseUrl, string defaultSha)
    {
        var card = _urlSigner.BuildDefaultImageUrl(baseUrl, "card", 640, defaultSha);
        var bottle = _urlSigner.BuildDefaultImageUrl(baseUrl, "bottle", 640, defaultSha);
        var round = _urlSigner.BuildDefaultImageUrl(baseUrl, "round", 480, defaultSha);
        var rect = _urlSigner.BuildDefaultImageUrl(baseUrl, "rect", 800, defaultSha);
        var original = _urlSigner.BuildDefaultImageUrl(baseUrl, "original", 1280, defaultSha);

        return new Dictionary<string, string>
        {
            ["card"] = card,
            ["bottle"] = bottle,
            ["round"] = round,
            ["rect"] = rect,
            ["original"] = original,
            ["Card"] = card,
            ["Bottle"] = bottle,
            ["Round"] = round,
            ["Rect"] = rect,
            ["Original"] = original
        };
    }

    private static (LabelCrop Crop, bool IsRound, int Width, int Height) ResolveCropAndDimensions(
        string variant, RecipeLabel label, int targetSize)
    {
        var normalized = variant.Trim().ToLowerInvariant();
        return normalized switch
        {
            "round" => (label.RoundCrop, true, targetSize, targetSize),
            "rect" => label.RectOrientation == DisplayOrientation.Landscape
                ? (label.RectCrop, false, targetSize, (int)(targetSize * 9f / 16f))
                : (label.RectCrop, false, (int)(targetSize * 9f / 16f), targetSize),
            "bottle" =>
                (label.BottleCrop, false, targetSize, (int)(targetSize * (float)(label.BottleHeightMm / Math.Max(1m, label.BottleWidthMm)))),
            "card" =>
                (label.RectCrop, false, targetSize, (int)(targetSize * 9f / 16f)),
            "original" or _ =>
                (LabelCrop.Default, false, targetSize, (int)(targetSize * (float)label.HeightPx / Math.Max(1, label.WidthPx)))
        };
    }

    private static (LabelCrop Crop, bool IsRound, int Width, int Height) ResolveDefaultCropAndDimensions(
        string variant, int targetSize)
    {
        var normalized = variant.Trim().ToLowerInvariant();
        return normalized switch
        {
            "round" => (LabelCrop.Default, true, targetSize, targetSize),
            "rect" or "card" => (LabelCrop.Default, false, targetSize, (int)(targetSize * 9f / 16f)),
            "bottle" => (LabelCrop.Default, false, targetSize, (int)(targetSize * (120f / 90f))),
            "original" or _ => (LabelCrop.Default, false, targetSize, targetSize)
        };
    }

    private static string NormalizeFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return "webp";
        var f = format.Trim().ToLowerInvariant();
        return AllowedFormats.Contains(f) ? f : "webp";
    }

    private static string GetMimeType(string format) => format switch
    {
        "png" => "image/png",
        "jpeg" or "jpg" => "image/jpeg",
        "webp" or _ => "image/webp"
    };

    private static string? SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var clean = Path.GetFileName(fileName);
        return clean.Length > 255 ? clean[..255] : clean;
    }
}