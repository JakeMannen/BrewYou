using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrewYou.ApiService.Endpoints;

public static class BatchEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void MapBatchEndpoints(this IEndpointRouteBuilder routes)
    {
        MapBatchRoutes(routes.MapGroup("/api/v1/batches"), "V1");
        MapBatchRoutes(routes.MapGroup("/api/batches"), "Legacy");
    }

    private static void MapBatchRoutes(RouteGroupBuilder group, string prefixTag)
    {
        group.WithTags("Batches").RequireAuthorization();

        // 1. List batches
        group.MapGet("/", async (
            [FromQuery] BatchStatus? status,
            [FromQuery] string? search,
            [FromQuery] int? page,
            [FromQuery] int? limit,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (items, pagination) = await batchService.GetBatchesAsync(userId, status, search, page ?? 1, limit ?? 20);
            return Results.Ok(ApiResponse<List<BatchSummaryDto>>.Ok(items, pagination));
        })
        .WithName($"GetBatches_{prefixTag}")
        .Produces<ApiResponse<List<BatchSummaryDto>>>(StatusCodes.Status200OK);

        // 2. Get next batch code preview
        group.MapGet("/next-code", async (
            [FromQuery] DateOnly? date,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var code = await batchService.GetNextBatchCodeAsync(userId, date);
            return Results.Ok(ApiResponse<NextBatchCodeResponse>.Ok(new NextBatchCodeResponse(code)));
        })
        .WithName($"GetNextBatchCode_{prefixTag}")
        .Produces<ApiResponse<NextBatchCodeResponse>>(StatusCodes.Status200OK);

        // 2.5 Check ingredient stock for recipe batch
        group.MapPost("/check-stock", async (
            [FromBody] CheckBatchStockRequest request,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, checkResult, errorMsg) = await batchService.CheckRecipeStockAsync(
                request.RecipeId,
                request.TargetBatchSizeLiters,
                userId,
                request.TargetVolumeBasis,
                request.FermenterLossLiters,
                request.PackagingLossLiters);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<BatchStockCheckResult>.Ok(checkResult!)),
                BatchAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Recipe not found.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMsg ?? "Failed to check stock."))
            };
        })
        .WithName($"CheckBatchStock_{prefixTag}")
        .Produces<ApiResponse<BatchStockCheckResult>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 3. Get batch by ID
        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, batch) = await batchService.GetBatchByIdAsync(id, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<BatchDetailDto>.Ok(batch!)),
                _ => Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Batch not found."))
            };
        })
        .WithName($"GetBatchById_{prefixTag}")
        .Produces<ApiResponse<BatchDetailDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 4. Create / Start Batch
        group.MapPost("/", async (
            [FromBody] CreateBatchRequest request,
            IValidator<CreateBatchRequest> validator,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
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
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Validation failed for batch creation.", details));
            }

            var (result, batch, errorMsg) = await batchService.CreateBatchAsync(request, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Created($"/api/v1/batches/{batch!.Id}", ApiResponse<BatchDetailDto>.Ok(batch!)),
                BatchAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Resource not found.")),
                BatchAccessResult.QuotaExceeded => Results.UnprocessableEntity(ApiResponse.Fail("QUOTA_EXCEEDED", errorMsg ?? "Batch quota exceeded.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMsg ?? "Failed to create batch."))
            };
        })
        .WithName($"CreateBatch_{prefixTag}")
        .Produces<ApiResponse<BatchDetailDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status422UnprocessableEntity);

        // 5. Advance stage
        group.MapPatch("/{id:guid}/stage", async (
            Guid id,
            [FromBody] AdvanceBatchStageRequest request,
            IValidator<AdvanceBatchStageRequest> validator,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
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
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Validation failed for stage advance.", details));
            }

            var (result, batch, errorMsg) = await batchService.AdvanceStageAsync(id, request, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<BatchDetailDto>.Ok(batch!)),
                BatchAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Batch not found.")),
                BatchAccessResult.Conflict => Results.Conflict(ApiResponse.Fail("CONFLICT", errorMsg ?? "Cannot advance stage.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMsg ?? "Failed to advance stage."))
            };
        })
        .WithName($"AdvanceBatchStage_{prefixTag}")
        .Produces<ApiResponse<BatchDetailDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        // 6. Add reading
        group.MapPost("/{id:guid}/readings", async (
            Guid id,
            [FromBody] AddBatchReadingRequest request,
            IValidator<AddBatchReadingRequest> validator,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
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
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Validation failed for batch reading.", details));
            }

            var (result, reading, errorMsg) = await batchService.AddReadingAsync(id, request, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Created($"/api/v1/batches/{id}/readings/{reading!.Id}", ApiResponse<BatchReadingDto>.Ok(reading!)),
                BatchAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Batch not found.")),
                BatchAccessResult.QuotaExceeded => Results.UnprocessableEntity(ApiResponse.Fail("QUOTA_EXCEEDED", errorMsg ?? "Reading limit reached.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMsg ?? "Failed to add reading."))
            };
        })
        .WithName($"AddBatchReading_{prefixTag}")
        .Produces<ApiResponse<BatchReadingDto>>(StatusCodes.Status201Created)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 7. Toggle Ingredient
        group.MapPatch("/{id:guid}/ingredients/{ingredientId:guid}", async (
            Guid id,
            Guid ingredientId,
            [FromQuery] bool isChecked,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, ingredient, errorMsg) = await batchService.ToggleIngredientAsync(id, ingredientId, isChecked, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<BatchIngredientDto>.Ok(ingredient!)),
                _ => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Ingredient not found."))
            };
        })
        .WithName($"ToggleBatchIngredient_{prefixTag}")
        .Produces<ApiResponse<BatchIngredientDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 7.5 Toggle Mash Step
        group.MapPatch("/{id:guid}/mash-steps/{stepId:guid}", async (
            Guid id,
            Guid stepId,
            [FromBody] ToggleBatchMashStepRequest request,
            IValidator<ToggleBatchMashStepRequest> validator,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
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
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Invalid mash step toggle request.", details));
            }

            var (result, step, errorMsg) = await batchService.ToggleMashStepAsync(id, stepId, request, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<BatchMashStepDto>.Ok(step!)),
                BatchAccessResult.Conflict => Results.Conflict(ApiResponse.Fail("INVALID_STATE", errorMsg ?? "Batch is in an invalid state.")),
                _ => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Mash step not found."))
            };
        })
        .WithName($"ToggleBatchMashStep_{prefixTag}")
        .Produces<ApiResponse<BatchMashStepDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        // 7.6 Toggle Fermentation Step
        group.MapPatch("/{id:guid}/fermentation-steps/{stepId:guid}", async (
            Guid id,
            Guid stepId,
            [FromBody] ToggleBatchFermentationStepRequest request,
            IValidator<ToggleBatchFermentationStepRequest> validator,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
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
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Invalid fermentation step toggle request.", details));
            }

            var (result, step, errorMsg) = await batchService.ToggleFermentationStepAsync(id, stepId, request, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<BatchFermentationStepDto>.Ok(step!)),
                BatchAccessResult.Conflict => Results.Conflict(ApiResponse.Fail("INVALID_STATE", errorMsg ?? "Batch is in an invalid state.")),
                _ => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Fermentation step not found."))
            };
        })
        .WithName($"ToggleBatchFermentationStep_{prefixTag}")
        .Produces<ApiResponse<BatchFermentationStepDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        // 8. Update Batch
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateBatchRequest request,
            IValidator<UpdateBatchRequest> validator,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
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
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Validation failed for batch update.", details));
            }

            var (result, batch, errorMsg) = await batchService.UpdateBatchAsync(id, request, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<BatchDetailDto>.Ok(batch!)),
                _ => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Batch not found."))
            };
        })
        .WithName($"UpdateBatch_{prefixTag}")
        .Produces<ApiResponse<BatchDetailDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 9. Delete Batch
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await batchService.DeleteBatchAsync(id, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse.Ok()),
                _ => Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Batch not found."))
            };
        })
        .WithName($"DeleteBatch_{prefixTag}")
        .Produces<ApiResponse>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 10. Get Batch Equipment Readings (Filterable by stage and step)
        group.MapGet("/{id:guid}/equipment-readings", async (
            Guid id,
            [FromQuery] BrewStage? stage,
            [FromQuery] Guid? stepId,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, readings, errorMsg) = await batchService.GetBatchEquipmentReadingsAsync(id, userId, stage, stepId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<List<BatchEquipmentReadingDto>>.Ok(readings!)),
                _ => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Batch not found."))
            };
        })
        .WithName($"GetBatchEquipmentReadings_{prefixTag}")
        .Produces<ApiResponse<List<BatchEquipmentReadingDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound);

        // 11. Log Temperature Reading for Batch Step
        group.MapPost("/{id:guid}/equipment-readings", async (
            Guid id,
            [FromBody] LogBatchTemperatureRequest request,
            IValidator<LogBatchTemperatureRequest> validator,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
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
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Validation failed for temperature reading.", details));
            }

            var (result, reading, errorMsg) = await batchService.LogTemperatureReadingAsync(id, request, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<BatchEquipmentReadingDto>.Ok(reading!)),
                BatchAccessResult.Conflict => Results.Conflict(ApiResponse.Fail("CONFLICT", errorMsg ?? "Cannot log temperature reading.")),
                _ => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Batch not found."))
            };
        })
        .WithName($"LogBatchEquipmentReading_{prefixTag}")
        .Produces<ApiResponse<BatchEquipmentReadingDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        // 12. Stream Live Batch Equipment Readings (SSE)
        group.MapGet("/{id:guid}/telemetry-stream", async (
            Guid id,
            [FromQuery] BrewStage? stage,
            HttpContext httpContext,
            ClaimsPrincipal principal,
            IBatchService batchService,
            ITelemetryBroadcastService broadcastService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            // BOLA / IDOR Verification: Ensure user owns batch before starting stream
            var (batchResult, _) = await batchService.GetBatchByIdAsync(id, userId);
            if (batchResult != BatchAccessResult.Success)
            {
                return Results.NotFound(ApiResponse.Fail("NOT_FOUND", "Batch not found."));
            }

            using var subscription = broadcastService.Subscribe(id);
            if (subscription is null)
            {
                return Results.Json(
                    ApiResponse.Fail("TOO_MANY_SUBSCRIBERS", "Maximum concurrent telemetry streams reached for this batch."),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var reader = subscription.Reader;
            var response = httpContext.Response;
            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache, no-transform";
            response.Headers.Append("X-Accel-Buffering", "no");

            var cancellationToken = httpContext.RequestAborted;

            // Maximum connection lifetime (15 minutes) as advised by Cybersecurity Expert
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

                        while (reader.TryRead(out var reading))
                        {
                            if (!stage.HasValue || reading.Stage == stage.Value)
                            {
                                var json = JsonSerializer.Serialize(reading, SseJsonOptions);
                                await response.WriteAsync($"event: reading\ndata: {json}\n\n", streamToken);
                                await response.Body.FlushAsync(streamToken);
                            }
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
        .WithName($"StreamBatchTelemetry_{prefixTag}")
        .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status429TooManyRequests);

        // 13. Update batch sensor assignments
        group.MapPut("/{id:guid}/sensors", async (
            Guid id,
            [FromBody] List<BatchSensorAssignmentInput> sensorInputs,
            ClaimsPrincipal principal,
            IBatchService batchService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (result, sensors, errorMsg) = await batchService.UpdateBatchSensorsAsync(id, sensorInputs, userId);
            return result switch
            {
                BatchAccessResult.Success => Results.Ok(ApiResponse<List<BatchSensorAssignmentDto>>.Ok(sensors!)),
                BatchAccessResult.NotFound => Results.NotFound(ApiResponse.Fail("NOT_FOUND", errorMsg ?? "Batch not found.")),
                BatchAccessResult.Conflict => Results.Conflict(ApiResponse.Fail("CONFLICT", errorMsg ?? "Batch cannot be modified.")),
                _ => Results.BadRequest(ApiResponse.Fail("BAD_REQUEST", errorMsg ?? "Failed to update sensor assignments."))
            };
        })
        .WithName($"UpdateBatchSensors_{prefixTag}")
        .Produces<ApiResponse<List<BatchSensorAssignmentDto>>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status404NotFound)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);
    }
}