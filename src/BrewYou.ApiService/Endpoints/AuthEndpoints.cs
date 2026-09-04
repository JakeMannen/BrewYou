using System.Security.Claims;
using BrewYou.ApiService.Auth;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BrewYou.ApiService.Endpoints;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record RefreshTokenRequest(string? RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User);
public record UserDto(string Id, string Email, string DisplayName);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        group.MapPost("/register", async (
            [FromBody] RegisterRequest request,
            UserManager<ApplicationUser> userManager,
            ITokenService tokenService,
            IOptions<JwtOptions> jwtOptions,
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { message = "Email and password are required." });
            }

            var existing = await userManager.FindByEmailAsync(request.Email);
            if (existing != null)
            {
                return Results.Conflict(new { message = "User with this email already exists." });
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Email.Split('@')[0] : request.DisplayName
            };

            var createResult = await userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                var errors = createResult.Errors.Select(e => e.Description);
                return Results.BadRequest(new { message = "Registration failed.", errors });
            }

            var accessToken = tokenService.GenerateAccessToken(user);
            var refreshToken = tokenService.GenerateRefreshToken();
            var refreshTokenDays = jwtOptions.Value.RefreshTokenExpirationDays;

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(refreshTokenDays);
            await userManager.UpdateAsync(user);

            SetRefreshTokenCookie(httpContext, refreshToken, user.RefreshTokenExpiryTime.Value);

            var expiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.AccessTokenExpirationMinutes);
            var userDto = new UserDto(user.Id, user.Email!, user.DisplayName ?? user.UserName!);

            return Results.Ok(new AuthResponse(accessToken, refreshToken, expiresAt, userDto));
        })
        .WithName("Register")
        .Produces<AuthResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            UserManager<ApplicationUser> userManager,
            ITokenService tokenService,
            IOptions<JwtOptions> jwtOptions,
            HttpContext httpContext) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
            {
                return Results.Unauthorized();
            }

            var accessToken = tokenService.GenerateAccessToken(user);
            var refreshToken = tokenService.GenerateRefreshToken();
            var refreshTokenDays = jwtOptions.Value.RefreshTokenExpirationDays;

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(refreshTokenDays);
            await userManager.UpdateAsync(user);

            SetRefreshTokenCookie(httpContext, refreshToken, user.RefreshTokenExpiryTime.Value);

            var expiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.AccessTokenExpirationMinutes);
            var userDto = new UserDto(user.Id, user.Email!, user.DisplayName ?? user.UserName!);

            return Results.Ok(new AuthResponse(accessToken, refreshToken, expiresAt, userDto));
        })
        .WithName("Login")
        .Produces<AuthResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", async (
            [FromBody] RefreshTokenRequest? request,
            UserManager<ApplicationUser> userManager,
            ITokenService tokenService,
            IOptions<JwtOptions> jwtOptions,
            HttpContext httpContext) =>
        {
            var refreshToken = request?.RefreshToken ?? httpContext.Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.Users.FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);
            if (user == null || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            {
                return Results.Unauthorized();
            }

            var newAccessToken = tokenService.GenerateAccessToken(user);
            var newRefreshToken = tokenService.GenerateRefreshToken();
            var refreshTokenDays = jwtOptions.Value.RefreshTokenExpirationDays;

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(refreshTokenDays);
            await userManager.UpdateAsync(user);

            SetRefreshTokenCookie(httpContext, newRefreshToken, user.RefreshTokenExpiryTime.Value);

            var expiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.AccessTokenExpirationMinutes);
            var userDto = new UserDto(user.Id, user.Email!, user.DisplayName ?? user.UserName!);

            return Results.Ok(new AuthResponse(newAccessToken, newRefreshToken, expiresAt, userDto));
        })
        .WithName("RefreshToken")
        .Produces<AuthResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new UserDto(user.Id, user.Email!, user.DisplayName ?? user.UserName!));
        })
        .RequireAuthorization()
        .WithName("GetCurrentUser")
        .Produces<UserDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    user.RefreshToken = null;
                    user.RefreshTokenExpiryTime = null;
                    await userManager.UpdateAsync(user);
                }
            }

            httpContext.Response.Cookies.Delete("refreshToken");
            return Results.Ok(new { message = "Logged out successfully." });
        })
        .WithName("Logout")
        .Produces(StatusCodes.Status200OK);

        return group;
    }

    private static void SetRefreshTokenCookie(HttpContext context, string token, DateTime expires)
    {
        context.Response.Cookies.Append("refreshToken", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = false, // Set to true in production with HTTPS
            SameSite = SameSiteMode.Lax,
            Expires = expires
        });
    }
}
