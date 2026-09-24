using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace BrewYou.ApiService.Endpoints;

public record ManualTemperatureRequest(decimal Temperature, string? Unit = "C");

public static class TelemetryEndpoints
{
    public static RouteGroupBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder routes)
    {
        var publicGroup = routes.MapGroup("/api/v1/telemetry")
            .WithTags("Telemetry");

        // Public IoT device ingestion endpoint (authenticated by cryptographically unguessable token in route)
        publicGroup.MapPost("/equipment/{token}", async (
            string token,
            [FromBody] JsonElement payload,
            ITelemetryService telemetryService) =>
        {
            var result = await telemetryService.IngestTelemetryAsync(token, payload);

            return result.Status switch
            {
                TelemetryIngestStatus.NotFound =>
                    Results.NotFound(ApiResponse.Fail("NOT_FOUND", result.ErrorMessage ?? "Equipment not found for token.")),
                TelemetryIngestStatus.InvalidPayload =>
                    Results.BadRequest(ApiResponse.Fail("INVALID_PAYLOAD", result.ErrorMessage ?? "Invalid telemetry payload.")),
                TelemetryIngestStatus.OutOfRange =>
                    Results.BadRequest(ApiResponse.Fail("OUT_OF_RANGE", result.ErrorMessage ?? "Temperature value is out of physical range.")),
                TelemetryIngestStatus.Success =>
                    Results.Ok(ApiResponse<object>.Ok(new
                    {
                        success = true,
                        temperatureC = result.TemperatureC,
                        timestamp = result.Timestamp
                    })),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("IngestEquipmentTelemetry")
        .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // Public IoT device ingestion endpoint (authenticated by X-BrewYou-Device-Token or Bearer header)
        publicGroup.MapPost("/equipment", async (
            HttpContext context,
            [FromBody] JsonElement payload,
            ITelemetryService telemetryService) =>
        {
            var token = context.Request.Headers["X-BrewYou-Device-Token"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
            {
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = authHeader["Bearer ".Length..].Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.BadRequest(ApiResponse.Fail("MISSING_TOKEN", "Device token must be supplied in X-BrewYou-Device-Token header or Bearer authorization header."));
            }

            var result = await telemetryService.IngestTelemetryAsync(token, payload);
            return result.Status switch
            {
                TelemetryIngestStatus.NotFound =>
                    Results.NotFound(ApiResponse.Fail("NOT_FOUND", result.ErrorMessage ?? "Equipment not found for token.")),
                TelemetryIngestStatus.InvalidPayload =>
                    Results.BadRequest(ApiResponse.Fail("INVALID_PAYLOAD", result.ErrorMessage ?? "Invalid telemetry payload.")),
                TelemetryIngestStatus.OutOfRange =>
                    Results.BadRequest(ApiResponse.Fail("OUT_OF_RANGE", result.ErrorMessage ?? "Temperature value is out of physical range.")),
                TelemetryIngestStatus.Success =>
                    Results.Ok(ApiResponse<object>.Ok(new
                    {
                        success = true,
                        temperatureC = result.TemperatureC,
                        timestamp = result.Timestamp
                    })),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("IngestEquipmentTelemetryWithHeader")
        .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // Authenticated inventory equipment telemetry management endpoints
        var authGroup = routes.MapGroup("/api/v1/inventory/equipment")
            .WithTags("Equipment Telemetry")
            .RequireAuthorization();

        authGroup.MapGet("/{id:guid}/readings", async (
            Guid id,
            [FromQuery] int? limit,
            ClaimsPrincipal principal,
            ITelemetryService telemetryService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (found, readings) = await telemetryService.GetEquipmentReadingsAsync(id, userId, limit ?? 50);
            if (!found || readings == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Equipment not found."));
            }

            return Results.Ok(ApiResponse<List<EquipmentReadingDto>>.Ok(readings));
        })
        .WithName("GetEquipmentReadings")
        .Produces<ApiResponse<List<EquipmentReadingDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        authGroup.MapPost("/{id:guid}/regenerate-token", async (
            Guid id,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, updated, _) = await equipmentService.RegenerateConnectionTokenAsync(id, userId);
            if (result == EquipmentAccessResult.NotFound || updated == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Equipment not found."));
            }

            return Results.Ok(ApiResponse<EquipmentDto>.Ok(updated));
        })
        .WithName("RegenerateEquipmentConnectionToken")
        .Produces<ApiResponse<EquipmentDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        authGroup.MapPost("/{id:guid}/test-poll", async (
            Guid id,
            ClaimsPrincipal principal,
            ITelemetryService telemetryService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var pollResult = await telemetryService.TestPollAsync(id, userId);
            if (!pollResult.Success && pollResult.ErrorMessage == "Equipment not found.")
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Equipment not found."));
            }

            return Results.Ok(ApiResponse<TelemetryPollResult>.Ok(pollResult));
        })
        .WithName("TestEquipmentPoll")
        .Produces<ApiResponse<TelemetryPollResult>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        return publicGroup;
    }
}