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
        logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";

        var response = ApiResponse.Fail("INTERNAL_SERVER_ERROR", "An unexpected error occurred. Please try again later.");
        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }
}