using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services;
using BrewYou.ApiService.Services.Labels;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace BrewYou.ApiService.Endpoints;

public static class RecipeLabelEndpoints
{
    public static RouteGroupBuilder MapRecipeLabelEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/recipes/{id:guid}/label")
            .WithTags("Recipe Labels");

        group.MapGet("/", async (
            Guid id,
            ClaimsPrincipal principal,
            HttpContext context,
            IRecipeLabelService labelService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var apiBase = $"{context.Request.Scheme}://{context.Request.Host}";
            var (result, label, error) = await labelService.GetRecipeLabelAsync(id, userId, apiBase);

            return result switch
            {
                RecipeAccessResult.Success => Results.Ok(ApiResponse<RecipeLabelDto>.Ok(label!)),
                RecipeAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Recipe or label not found.")),
                RecipeAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("GetRecipeLabel")
        .Produces<ApiResponse<RecipeLabelDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPut("/", async (
            Guid id,
            HttpRequest request,
            ClaimsPrincipal principal,
            HttpContext context,
            IRecipeLabelService labelService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(ApiResponse.Fail("INVALID_CONTENT_TYPE", "Request must be multipart/form-data."));
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(ApiResponse.Fail("MISSING_FILE", "A valid image file must be uploaded."));
            }

            const long maxFileSize = 8 * 1024 * 1024; // 8 MB limit
            if (file.Length > maxFileSize)
            {
                return Results.Json(ApiResponse.Fail("PAYLOAD_TOO_LARGE", "Image file exceeds maximum allowable size of 8 MB."), statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            UpdateRecipeLabelSettingsRequest? settings = null;
            var settingsJson = form["settings"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(settingsJson))
            {
                try
                {
                    settings = JsonSerializer.Deserialize<UpdateRecipeLabelSettingsRequest>(settingsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException)
                {
                    return Results.BadRequest(ApiResponse.Fail("INVALID_JSON", "Malformed settings JSON in form-data."));
                }
            }

            using var stream = file.OpenReadStream();
            var apiBase = $"{context.Request.Scheme}://{context.Request.Host}";

            try
            {
                var (result, label, error) = await labelService.UploadRecipeLabelAsync(
                    id, stream, file.FileName, settings, userId, apiBase);

                return result switch
                {
                    RecipeAccessResult.Success => Results.Ok(ApiResponse<RecipeLabelDto>.Ok(label!)),
                    RecipeAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Recipe not found.")),
                    RecipeAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Only the recipe owner can upload label artwork."), statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
                };
            }
            catch (BadHttpRequestException ex)
            {
                return Results.BadRequest(ApiResponse.Fail("INVALID_IMAGE", ex.Message));
            }
        })
        .RequireAuthorization()
        .DisableAntiforgery() // Safe because authentication relies exclusively on Authorization: Bearer tokens
        .RequireRateLimiting("label-upload-limit")
        .WithName("UploadRecipeLabel")
        .Produces<ApiResponse<RecipeLabelDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status413PayloadTooLarge);

        group.MapPatch("/", async (
            Guid id,
            [FromBody] UpdateRecipeLabelSettingsRequest request,
            IValidator<UpdateRecipeLabelSettingsRequest> validator,
            ClaimsPrincipal principal,
            HttpContext context,
            IRecipeLabelService labelService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Validation failed for label settings.", details));
            }

            var apiBase = $"{context.Request.Scheme}://{context.Request.Host}";
            var (result, label, error) = await labelService.UpdateRecipeLabelSettingsAsync(id, request, userId, apiBase);

            return result switch
            {
                RecipeAccessResult.Success => Results.Ok(ApiResponse<RecipeLabelDto>.Ok(label!)),
                RecipeAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Recipe not found.")),
                RecipeAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Only the recipe owner can update label settings."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("UpdateRecipeLabelSettings")
        .Produces<ApiResponse<RecipeLabelDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden);

        group.MapDelete("/", async (
            Guid id,
            ClaimsPrincipal principal,
            IRecipeLabelService labelService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await labelService.DeleteRecipeLabelAsync(id, userId);
            return result switch
            {
                RecipeAccessResult.Success => Results.Ok(ApiResponse.Ok()),
                RecipeAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Recipe not found.")),
                RecipeAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", "Only the recipe owner can delete the label."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .RequireAuthorization()
        .WithName("DeleteRecipeLabel")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden);

        return group;
    }
}