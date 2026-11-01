using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services.Labels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BrewYou.ApiService.Endpoints;

public static class LabelImageEndpoints
{
    public static RouteGroupBuilder MapLabelImageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/labels")
            .WithTags("Label Images");

        // Public signed image delivery for custom recipe labels
        group.MapGet("/{recipeId:guid}/image/{variant}", async (
            Guid recipeId,
            string variant,
            [FromQuery] int? size,
            [FromQuery] string? format,
            [FromQuery] string? sig,
            [FromQuery] long? exp,
            [FromQuery] int? v,
            HttpContext context,
            IRecipeLabelService labelService) =>
        {
            var rendition = await labelService.GetRenditionAsync(recipeId, variant, size, format, sig, exp, v);
            if (rendition == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Label image not found or link has expired."));
            }

            var (bytes, contentType, etag) = rendition.Value;

            // Conditional request handling (ETag / 304 Not Modified)
            if (context.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) &&
                string.Equals(ifNoneMatch.ToString(), etag, StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "private, max-age=86400, immutable";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
            context.Response.Headers["Content-Disposition"] = $"inline; filename=\"label-{recipeId:N}-{variant}.{format ?? "webp"}\"";

            return Results.File(bytes!, contentType);
        })
        .WithName("GetSignedLabelImage")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status304NotModified)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // Public image delivery for default system label
        group.MapGet("/default/image/{variant}", async (
            string variant,
            [FromQuery] int? size,
            [FromQuery] string? format,
            [FromQuery] string? v,
            HttpContext context,
            IRecipeLabelService labelService) =>
        {
            var rendition = await labelService.GetDefaultRenditionAsync(variant, size, format, v);
            if (rendition == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Default label image not found."));
            }

            var (bytes, contentType, etag) = rendition.Value;

            if (context.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) &&
                string.Equals(ifNoneMatch.ToString(), etag, StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "public, max-age=604800, immutable";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
            context.Response.Headers["Content-Disposition"] = $"inline; filename=\"default-label-{variant}.{format ?? "webp"}\"";

            return Results.File(bytes!, contentType);
        })
        .WithName("GetDefaultLabelImage")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status304NotModified)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        return group;
    }
}