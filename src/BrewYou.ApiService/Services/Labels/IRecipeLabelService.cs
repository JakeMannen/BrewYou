using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;

namespace BrewYou.ApiService.Services.Labels;

public interface IRecipeLabelService
{
    Task<(RecipeAccessResult Result, RecipeLabelDto? Label, string? ErrorMessage)> GetRecipeLabelAsync(
        Guid recipeId, string? userId, string apiBaseUrl);

    Task<(RecipeAccessResult Result, RecipeLabelDto? Label, string? ErrorMessage)> UploadRecipeLabelAsync(
        Guid recipeId, Stream fileStream, string? originalFileName, UpdateRecipeLabelSettingsRequest? settings, string userId, string apiBaseUrl);

    Task<(RecipeAccessResult Result, RecipeLabelDto? Label, string? ErrorMessage)> UpdateRecipeLabelSettingsAsync(
        Guid recipeId, UpdateRecipeLabelSettingsRequest request, string userId, string apiBaseUrl);

    Task<RecipeAccessResult> DeleteRecipeLabelAsync(Guid recipeId, string userId);

    Task<(byte[]? Bytes, string ContentType, string ETag)?> GetRenditionAsync(
        Guid recipeId, string variant, int? size, string? format, string? sig, long? exp, int? v);

    Task<(byte[]? Bytes, string ContentType, string ETag)?> GetDefaultRenditionAsync(
        string variant, int? size, string? format, string? v);
}