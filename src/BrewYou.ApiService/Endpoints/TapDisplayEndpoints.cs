using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BrewYou.ApiService.Endpoints;

public static class TapDisplayEndpoints
{
    public static RouteGroupBuilder MapTapDisplayEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/tap/{token}")
            .WithTags("Tap Displays")
            .RequireRateLimiting("tap-display-limit");

        // Public manifest endpoint for digital tap screens & kiosks
        group.MapGet("/", async (
            string token,
            HttpContext context,
            IEquipmentService equipmentService) =>
        {
            var apiBase = $"{context.Request.Scheme}://{context.Request.Host}";
            var result = await equipmentService.GetTapManifestByTokenAsync(token, apiBase);
            if (result == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Tap display not found or link has been revoked."));
            }

            var (manifest, etag) = result.Value;

            if (context.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) &&
                string.Equals(ifNoneMatch.ToString(), etag, StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "public, max-age=60";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            return Results.Ok(ApiResponse<TapDisplayManifestDto>.Ok(manifest!));
        })
        .WithName("GetTapDisplayManifest")
        .Produces<ApiResponse<TapDisplayManifestDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status304NotModified)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // Public image endpoint for digital tap screens & kiosks
        group.MapGet("/image", async (
            string token,
            [FromQuery] string? format,
            [FromQuery] bool? abv,
            HttpContext context,
            IEquipmentService equipmentService) =>
        {
            var result = await equipmentService.RenderTapImageByTokenAsync(token, format, abv ?? true);
            if (result == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Tap display image not found or link has been revoked."));
            }

            var (bytes, contentType, etag) = result.Value;

            if (context.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) &&
                string.Equals(ifNoneMatch.ToString(), etag, StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "public, max-age=60";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
            context.Response.Headers["Content-Disposition"] = $"inline; filename=\"tap-display.{format ?? "webp"}\"";

            return Results.File(bytes!, contentType);
        })
        .WithName("GetTapDisplayImage")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status304NotModified)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        return group;
    }
}