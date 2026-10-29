using BrewYou.ApiService.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace BrewYou.ApiService.Middleware;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badRequestEx)
        {
            logger.LogWarning(badRequestEx, "Bad HTTP request: {Message}", badRequestEx.Message);

            var statusCode = badRequestEx.StatusCode;
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json";

            var errorCode = statusCode switch
            {
                StatusCodes.Status400BadRequest => "BAD_REQUEST",
                StatusCodes.Status413PayloadTooLarge => "PAYLOAD_TOO_LARGE",
                StatusCodes.Status415UnsupportedMediaType => "UNSUPPORTED_MEDIA_TYPE",
                _ => "BAD_REQUEST"
            };

            var response = ApiResponse.Fail(errorCode, badRequestEx.Message);
            await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
            return true;
        }

        if (exception is System.Text.Json.JsonException jsonEx)
        {
            logger.LogWarning(jsonEx, "Malformed JSON in request: {Message}", jsonEx.Message);

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            httpContext.Response.ContentType = "application/json";

            var response = ApiResponse.Fail("INVALID_PAYLOAD", "Invalid JSON payload.");
            await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
            return true;
        }

        logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";

        var defaultResponse = ApiResponse.Fail("INTERNAL_SERVER_ERROR", "An unexpected error occurred. Please try again later.");
        await httpContext.Response.WriteAsJsonAsync(defaultResponse, cancellationToken);

        return true;
    }
}