using BrewYou.ApiService.Backup.Models;
using BrewYou.ApiService.Common;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public static class BackupEndpoints
{
    public static RouteGroupBuilder MapBackupEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/backup")
            .WithTags("Backup")
            .RequireAuthorization();

        group.MapPost("/export", async (
            [FromBody] ExportBackupRequest? request,
            IValidator<ExportBackupRequest> validator,
            ClaimsPrincipal principal,
            IBackupService backupService) =>
        {
            var effectiveRequest = request ?? new ExportBackupRequest();
            var validation = await validator.ValidateAsync(effectiveRequest);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Backup export validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var envelope = await backupService.ExportUserBackupAsync(userId, effectiveRequest);
            return Results.Ok(ApiResponse<BrewYouBackupEnvelope>.Ok(envelope));
        })
        .WithName("ExportUserBackup")
        .Produces<ApiResponse<BrewYouBackupEnvelope>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/preview", async (
            [FromBody] PreviewRestoreRequest request,
            IValidator<PreviewRestoreRequest> validator,
            ClaimsPrincipal principal,
            IBackupService backupService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Restore preview validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var report = await backupService.PreviewRestoreAsync(userId, request);
            return Results.Ok(ApiResponse<RestorePreviewReportDto>.Ok(report));
        })
        .WithName("PreviewUserBackupRestore")
        .Produces<ApiResponse<RestorePreviewReportDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/restore", async (
            [FromBody] ExecuteRestoreRequest request,
            IValidator<ExecuteRestoreRequest> validator,
            ClaimsPrincipal principal,
            IBackupService backupService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Restore request validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var (succeeded, result, errorCode, errorMessage) = await backupService.ExecuteRestoreAsync(userId, request);
            if (!succeeded || result == null)
            {
                if (errorCode == "INVALID_PASSPHRASE")
                {
                    return Results.UnprocessableEntity(ApiResponse.Fail("INVALID_PASSPHRASE", errorMessage ?? "Invalid backup passphrase."));
                }

                return Results.BadRequest(ApiResponse.Fail(errorCode ?? "RESTORE_FAILED", errorMessage ?? "Backup restore failed."));
            }

            return Results.Ok(ApiResponse<RestoreResultDto>.Ok(result));
        })
        .WithName("ExecuteUserBackupRestore")
        .Produces<ApiResponse<RestoreResultDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ApiResponse>(StatusCodes.Status422UnprocessableEntity);

        return group;
    }
}