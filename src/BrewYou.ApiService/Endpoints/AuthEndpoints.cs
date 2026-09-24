using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BrewYou.ApiService.Endpoints;

public record RegisterRequest(string Email, string Password, string DisplayName, string? PreferredLanguage = "en", VolumeUnit? PreferredVolumeUnit = VolumeUnit.Liters);
public record LoginRequest(string Email, string Password);
public record GoogleAuthRequest(string IdToken, string? PreferredLanguage = "en");
public record RefreshTokenRequest(string? RefreshToken);
public record UpdateLanguageRequest(string Language);
public record UpdateVolumeUnitRequest(VolumeUnit VolumeUnit);

public record UserPreferencesDto(
    string Language,
    VolumeUnit VolumeUnit,
    WeightUnit WeightUnit,
    TemperatureUnit TemperatureUnit,
    GravityUnit GravityUnit,
    ThemePreference Theme,
    decimal DefaultBatchSizeLiters,
    decimal DefaultEfficiencyPercent,
    int DefaultBoilTimeMinutes,
    string? MqttHost = null,
    int? MqttPort = null,
    string? MqttUsername = null,
    string? MqttPassword = null,
    string? MqttCertificate = null,
    bool HasMqttPassword = false,
    string? MqttTopicPrefix = "brewyou/equipment");

public record UpdateUserPreferencesRequest(
    string? Language = null,
    VolumeUnit? VolumeUnit = null,
    WeightUnit? WeightUnit = null,
    TemperatureUnit? TemperatureUnit = null,
    GravityUnit? GravityUnit = null,
    ThemePreference? Theme = null,
    decimal? DefaultBatchSizeLiters = null,
    decimal? DefaultEfficiencyPercent = null,
    int? DefaultBoilTimeMinutes = null,
    string? MqttHost = null,
    int? MqttPort = null,
    string? MqttUsername = null,
    string? MqttPassword = null,
    string? MqttCertificate = null,
    string? MqttTopicPrefix = null);

public record UpdateProfileRequest(string DisplayName);

public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User, bool IsNewUser = false);

public record UserDto(
    string Id,
    string Email,
    string DisplayName,
    string PreferredLanguage,
    VolumeUnit PreferredVolumeUnit,
    DateTime CreatedAt,
    UserPreferencesDto Preferences);

public record MqttStatusResult(bool Configured, bool Connected, string? Host = null, int? Port = null, string? Error = null);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        group.MapPost("/register", async (
            [FromBody] RegisterRequest request,
            IValidator<RegisterRequest> validator,
            IAuthService authService,
            HttpContext httpContext) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Registration validation failed.", details));
            }

            var result = await authService.RegisterAsync(request, httpContext);
            if (!result.Succeeded)
            {
                if (result.ErrorCode == "EMAIL_CONFLICT")
                {
                    return Results.Conflict(ApiResponse.Fail("EMAIL_CONFLICT", result.ErrorMessage!));
                }

                var details = result.Details?.Select(d => new ApiErrorDetail("General", d));
                return Results.BadRequest(ApiResponse.Fail("REGISTRATION_FAILED", result.ErrorMessage!, details));
            }

            return Results.Ok(ApiResponse<AuthResponse>.Ok(result.Response!));
        })
        .WithName("Register")
        .Produces<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            IValidator<LoginRequest> validator,
            IAuthService authService,
            HttpContext httpContext) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Login validation failed.", details));
            }

            var result = await authService.LoginAsync(request, httpContext);
            if (!result.Succeeded)
            {
                return Results.Json(ApiResponse.Fail("INVALID_CREDENTIALS", result.ErrorMessage!), statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(ApiResponse<AuthResponse>.Ok(result.Response!));
        })
        .WithName("Login")
        .Produces<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/google", async (
            [FromBody] GoogleAuthRequest request,
            IValidator<GoogleAuthRequest> validator,
            IAuthService authService,
            HttpContext httpContext) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Google auth request validation failed.", details));
            }

            var result = await authService.GoogleLoginAsync(request, httpContext);
            if (!result.Succeeded)
            {
                if (result.ErrorCode == "INVALID_GOOGLE_TOKEN")
                {
                    return Results.Json(
                        ApiResponse.Fail(result.ErrorCode, result.ErrorMessage!),
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var details = result.Details?.Select(d => new ApiErrorDetail("General", d));
                return Results.BadRequest(ApiResponse.Fail(result.ErrorCode ?? "AUTH_FAILED", result.ErrorMessage!, details));
            }

            return Results.Ok(ApiResponse<AuthResponse>.Ok(result.Response!));
        })
        .WithName("GoogleLogin")
        .Produces<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", async (
            [FromBody] RefreshTokenRequest? request,
            IAuthService authService,
            HttpContext httpContext) =>
        {
            var result = await authService.RefreshTokenAsync(request?.RefreshToken, httpContext);
            if (!result.Succeeded)
            {
                return Results.Json(ApiResponse.Fail("INVALID_TOKEN", result.ErrorMessage!), statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(ApiResponse<AuthResponse>.Ok(result.Response!));
        })
        .WithName("RefreshToken")
        .Produces<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", async (
            ClaimsPrincipal principal,
            IAuthService authService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await authService.GetCurrentUserAsync(userId);
            if (user == null)
            {
                return Results.NotFound(ApiResponse.Fail("USER_NOT_FOUND", "User profile not found."));
            }

            return Results.Ok(ApiResponse<UserDto>.Ok(user));
        })
        .RequireAuthorization()
        .WithName("GetCurrentUser")
        .Produces<ApiResponse<UserDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPut("/me/language", async (
            [FromBody] UpdateLanguageRequest request,
            ClaimsPrincipal principal,
            IAuthService authService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await authService.UpdateUserLanguageAsync(userId, request.Language);
            if (user == null)
            {
                return Results.NotFound(ApiResponse.Fail("USER_NOT_FOUND", "User profile not found."));
            }

            return Results.Ok(ApiResponse<UserDto>.Ok(user));
        })
        .RequireAuthorization()
        .WithName("UpdateUserLanguage")
        .Produces<ApiResponse<UserDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPut("/me/volume-unit", async (
            [FromBody] UpdateVolumeUnitRequest request,
            IValidator<UpdateVolumeUnitRequest> validator,
            ClaimsPrincipal principal,
            IAuthService authService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Volume unit validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await authService.UpdateUserVolumeUnitAsync(userId, request.VolumeUnit);
            if (user == null)
            {
                return Results.NotFound(ApiResponse.Fail("USER_NOT_FOUND", "User profile not found."));
            }

            return Results.Ok(ApiResponse<UserDto>.Ok(user));
        })
        .RequireAuthorization()
        .WithName("UpdateUserVolumeUnit")
        .Produces<ApiResponse<UserDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPut("/me/preferences", async (
            [FromBody] UpdateUserPreferencesRequest request,
            IValidator<UpdateUserPreferencesRequest> validator,
            ClaimsPrincipal principal,
            IAuthService authService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "User preferences validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await authService.UpdateUserPreferencesAsync(userId, request);
            if (user == null)
            {
                return Results.NotFound(ApiResponse.Fail("USER_NOT_FOUND", "User profile not found."));
            }

            return Results.Ok(ApiResponse<UserDto>.Ok(user));
        })
        .RequireAuthorization()
        .WithName("UpdateUserPreferences")
        .Produces<ApiResponse<UserDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPut("/me/profile", async (
            [FromBody] UpdateProfileRequest request,
            IValidator<UpdateProfileRequest> validator,
            ClaimsPrincipal principal,
            IAuthService authService) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                var details = validation.Errors.Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage));
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Profile update validation failed.", details));
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await authService.UpdateUserProfileAsync(userId, request);
            if (user == null)
            {
                return Results.NotFound(ApiResponse.Fail("USER_NOT_FOUND", "User profile not found."));
            }

            return Results.Ok(ApiResponse<UserDto>.Ok(user));
        })
        .RequireAuthorization()
        .WithName("UpdateUserProfile")
        .Produces<ApiResponse<UserDto>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapGet("/me/mqtt-status", async (
            ClaimsPrincipal principal,
            IAuthService authService,
            [FromQuery] string? host,
            [FromQuery] int? port) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Json(ApiResponse.Fail("UNAUTHORIZED", "Authentication required."), statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!string.IsNullOrEmpty(host) && host.Length > 255)
            {
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Host length exceeds 255 characters."));
            }

            if (port.HasValue && (port.Value < 1 || port.Value > 65535))
            {
                return Results.BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Port must be between 1 and 65535."));
            }

            var result = await authService.GetMqttStatusAsync(userId, host, port);
            return Results.Ok(ApiResponse<MqttStatusResult>.Ok(result));
        })
        .RequireAuthorization()
        .WithName("GetMqttStatus")
        .Produces<ApiResponse<MqttStatusResult>>(StatusCodes.Status200OK)
        .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (
            ClaimsPrincipal principal,
            IAuthService authService,
            HttpContext httpContext) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            await authService.LogoutAsync(userId, httpContext);

            return Results.Ok(ApiResponse.Ok());
        })
        .WithName("Logout")
        .Produces<ApiResponse>(StatusCodes.Status200OK);

        return group;
    }
}