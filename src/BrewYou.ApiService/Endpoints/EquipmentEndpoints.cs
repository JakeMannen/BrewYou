using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Endpoints;

public static class EquipmentEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static RouteGroupBuilder MapEquipmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/inventory/equipment")
            .WithTags("Equipment")
            .RequireAuthorization();

        // Stream Live Equipment Telemetry (SSE)
        group.MapGet("/telemetry-stream", async (
            HttpContext httpContext,
            ClaimsPrincipal principal,
            ITelemetryBroadcastService broadcastService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            using var subscription = broadcastService.SubscribeEquipment(userId);
            if (subscription is null)
            {
                return Results.Json(
                    ApiResponse.Fail("TOO_MANY_SUBSCRIBERS", "Maximum concurrent equipment telemetry streams reached."),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var reader = subscription.Reader;
            var response = httpContext.Response;
            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache, no-transform";
            response.Headers.Append("X-Accel-Buffering", "no");

            var cancellationToken = httpContext.RequestAborted;

            // Maximum connection lifetime (15 minutes), matching batch telemetry stream
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var streamToken = linkedCts.Token;

            // Initial connection acknowledgment
            await response.WriteAsync(": connected\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);

            try
            {
                var waitTask = reader.WaitToReadAsync(streamToken).AsTask();

                while (!streamToken.IsCancellationRequested)
                {
                    var delayTask = Task.Delay(TimeSpan.FromSeconds(15), streamToken);
                    var completed = await Task.WhenAny(waitTask, delayTask);

                    if (completed == waitTask)
                    {
                        var hasData = await waitTask;
                        if (!hasData)
                        {
                            break;
                        }

                        while (reader.TryRead(out var update))
                        {
                            var json = JsonSerializer.Serialize(update, SseJsonOptions);
                            await response.WriteAsync($"event: reading\ndata: {json}\n\n", streamToken);
                            await response.Body.FlushAsync(streamToken);
                        }

                        waitTask = reader.WaitToReadAsync(streamToken).AsTask();
                    }
                    else
                    {
                        // Keep-alive heartbeat every 15s
                        await response.WriteAsync(": ping\n\n", streamToken);
                        await response.Body.FlushAsync(streamToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || timeoutCts.IsCancellationRequested)
            {
                // Clean disconnect or timeout
            }

            return Results.Empty;
        })
        .WithName("StreamEquipmentTelemetry")
        .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status429TooManyRequests);

        group.MapGet("/", async (
            [FromQuery] Guid? setupId,
            [FromQuery] EquipmentType? type,
            [FromQuery] string? search,
            [FromQuery] int? page,
            [FromQuery] int? limit,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (items, pagination) = await equipmentService.GetEquipmentListAsync(
                userId, setupId, type, search, page ?? 1, limit ?? 20);

            return Results.Ok(ApiResponse<List<EquipmentDto>>.Ok(items, pagination));
        })
        .WithName("GetEquipmentList")
        .Produces<ApiResponse<List<EquipmentDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, equipment) = await equipmentService.GetEquipmentByIdAsync(id, userId);
            if (result == EquipmentAccessResult.NotFound || equipment == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Equipment not found."));
            }

            return Results.Ok(ApiResponse<EquipmentDto>.Ok(equipment));
        })
        .WithName("GetEquipmentById")
        .Produces<ApiResponse<EquipmentDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            [FromBody] CreateEquipmentRequest request,
            IValidator<CreateEquipmentRequest> validator,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Equipment validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, created, errorMessage) = await equipmentService.CreateEquipmentAsync(request, userId);
            if (result == EquipmentAccessResult.Forbidden)
            {
                return Results.Json(ApiResponse.Fail("FORBIDDEN", errorMessage ?? "Viewers cannot create equipment."), statusCode: StatusCodes.Status403Forbidden);
            }

            if (result == EquipmentAccessResult.NotFound)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Brewery setup not found."));
            }

            if (result == EquipmentAccessResult.DuplicateName)
            {
                return Results.Conflict(ApiResponse.Fail("DUPLICATE_NAME", errorMessage ?? "An equipment item with this name already exists in this brewery setup."));
            }

            if (result == EquipmentAccessResult.QuotaExceeded)
            {
                return Results.UnprocessableEntity(ApiResponse.Fail("QUOTA_EXCEEDED", errorMessage ?? "Equipment limit reached."));
            }

            return Results.Created($"/api/v1/inventory/equipment/{created!.Id}", ApiResponse<EquipmentDto>.Ok(created));
        })
        .WithName("CreateEquipment")
        .Produces<ApiResponse<EquipmentDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict)
        .Produces<ApiResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateEquipmentRequest request,
            IValidator<UpdateEquipmentRequest> validator,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Equipment validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, updated, errorMessage) = await equipmentService.UpdateEquipmentAsync(id, request, userId);
            if (result == EquipmentAccessResult.Forbidden)
            {
                return Results.Json(ApiResponse.Fail("FORBIDDEN", errorMessage ?? "Viewers cannot edit equipment."), statusCode: StatusCodes.Status403Forbidden);
            }

            if (result == EquipmentAccessResult.NotFound)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Equipment not found."));
            }

            if (result == EquipmentAccessResult.DuplicateName)
            {
                return Results.Conflict(ApiResponse.Fail("DUPLICATE_NAME", errorMessage ?? "An equipment item with this name already exists in this brewery setup."));
            }

            if (updated == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMessage ?? "Equipment not found."));
            }

            return Results.Ok(ApiResponse<EquipmentDto>.Ok(updated));
        })
        .WithName("UpdateEquipment")
        .Produces<ApiResponse<EquipmentDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await equipmentService.DeleteEquipmentAsync(id, userId);
            if (result == EquipmentAccessResult.Forbidden)
            {
                return Results.Json(ApiResponse.Fail("FORBIDDEN", "Viewers cannot delete equipment."), statusCode: StatusCodes.Status403Forbidden);
            }

            if (result == EquipmentAccessResult.NotFound)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Equipment not found."));
            }

            return Results.Ok(ApiResponse.Ok());
        })
        .WithName("DeleteEquipment")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/active-batches", async (
            Guid id,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, batches) = await equipmentService.GetActiveBatchesForEquipmentAsync(id, userId);
            if (result == EquipmentAccessResult.NotFound || batches == null)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Equipment not found."));
            }

            return Results.Ok(ApiResponse<List<EquipmentActiveBatchDto>>.Ok(batches));
        })
        .WithName("GetEquipmentActiveBatches")
        .Produces<ApiResponse<List<EquipmentActiveBatchDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/tap-assignment", async (
            Guid id,
            [FromBody] AssignTapRequest request,
            IValidator<AssignTapRequest> validator,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Invalid tap assignment request.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, equipment, error) = await equipmentService.AssignTapBatchAsync(id, request.BatchId, userId);
            return result switch
            {
                EquipmentAccessResult.Success => Results.Ok(ApiResponse<EquipmentDto>.Ok(equipment!)),
                EquipmentAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Equipment or batch not found.")),
                EquipmentAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
                EquipmentAccessResult.InvalidTapAssignment => Results.Conflict(ApiResponse.Fail("INVALID_TAP_ASSIGNMENT", error ?? "Invalid tap assignment.")),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("AssignTapBatch")
        .Produces<ApiResponse<EquipmentDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/tap-assignment", async (
            Guid id,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, equipment, error) = await equipmentService.UnassignTapBatchAsync(id, userId);
            return result switch
            {
                EquipmentAccessResult.Success => Results.Ok(ApiResponse<EquipmentDto>.Ok(equipment!)),
                EquipmentAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Equipment not found.")),
                EquipmentAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
                EquipmentAccessResult.InvalidTapAssignment => Results.BadRequest(ApiResponse.Fail("INVALID_TAP", error ?? "Equipment is not a tap.")),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("UnassignTapBatch")
        .Produces<ApiResponse<EquipmentDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/display-link", async (
            Guid id,
            ClaimsPrincipal principal,
            HttpContext context,
            IConfiguration config,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var apiBase = $"{context.Request.Scheme}://{context.Request.Host}";
            var appBase = config["PUBLIC_APP_URL"] ?? "http://localhost:3000";

            var (result, link, error) = await equipmentService.GetTapDisplayLinkAsync(id, userId, appBase, apiBase);
            return result switch
            {
                EquipmentAccessResult.Success => Results.Ok(ApiResponse<TapDisplayLinkDto>.Ok(link!)),
                EquipmentAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Display link not enabled.")),
                EquipmentAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("GetTapDisplayLink")
        .Produces<ApiResponse<TapDisplayLinkDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/display-link", async (
            Guid id,
            ClaimsPrincipal principal,
            HttpContext context,
            IConfiguration config,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var apiBase = $"{context.Request.Scheme}://{context.Request.Host}";
            var appBase = config["PUBLIC_APP_URL"] ?? "http://localhost:3000";

            var (result, link, error) = await equipmentService.EnableOrRegenerateTapDisplayLinkAsync(id, userId, appBase, apiBase);
            return result switch
            {
                EquipmentAccessResult.Success => Results.Ok(ApiResponse<TapDisplayLinkDto>.Ok(link!)),
                EquipmentAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Equipment not found.")),
                EquipmentAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("EnableOrRegenerateTapDisplayLink")
        .Produces<ApiResponse<TapDisplayLinkDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}/display-link", async (
            Guid id,
            ClaimsPrincipal principal,
            IEquipmentService equipmentService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, error) = await equipmentService.RevokeTapDisplayLinkAsync(id, userId);
            return result switch
            {
                EquipmentAccessResult.Success => Results.Ok(ApiResponse.Ok()),
                EquipmentAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", error ?? "Equipment not found.")),
                EquipmentAccessResult.Forbidden => Results.Json(ApiResponse.Fail("FORBIDDEN", error ?? "Access denied."), statusCode: StatusCodes.Status403Forbidden),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("RevokeTapDisplayLink")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        return group;
    }
}