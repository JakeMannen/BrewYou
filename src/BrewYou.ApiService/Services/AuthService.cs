using BrewYou.ApiService.Auth;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BrewYou.ApiService.Services;

public record AuthResult(
    bool Succeeded,
    AuthResponse? Response = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    IEnumerable<string>? Details = null
);

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, HttpContext httpContext);
    Task<AuthResult> LoginAsync(LoginRequest request, HttpContext httpContext);
    Task<AuthResult> GoogleLoginAsync(GoogleAuthRequest request, HttpContext httpContext);
    Task<AuthResult> RefreshTokenAsync(string? refreshToken, HttpContext httpContext);
    Task<UserDto?> GetCurrentUserAsync(string userId);
    Task<UserDto?> UpdateUserLanguageAsync(string userId, string language);
    Task<UserDto?> UpdateUserVolumeUnitAsync(string userId, VolumeUnit volumeUnit);
    Task<UserDto?> UpdateUserPreferencesAsync(string userId, UpdateUserPreferencesRequest request);
    Task<UserDto?> UpdateUserProfileAsync(string userId, UpdateProfileRequest request);
    Task<AuthResult> ChangePasswordAsync(string userId, ChangePasswordRequest request, HttpContext httpContext);
    Task<MqttStatusResult> GetMqttStatusAsync(string userId, string? overrideHost = null, int? overridePort = null);
    Task LogoutAsync(string? userId, HttpContext httpContext);
}

public class AuthService(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    IOptions<JwtOptions> jwtOptions,
    IGoogleAuthValidator googleAuthValidator,
    BrewYouDbContext db,
    IDataProtectionProvider dataProtectionProvider,
    IMqttConnectivityChecker mqttConnectivityChecker,
    IMqttService mqttService) : IAuthService
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("BrewYou.MqttCredentials");
    public async Task<AuthResult> RegisterAsync(RegisterRequest request, HttpContext httpContext)
    {
        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing != null)
        {
            return new AuthResult(false, ErrorCode: "EMAIL_CONFLICT", ErrorMessage: "User with this email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Email.Split('@')[0] : request.DisplayName,
            PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage) ? "en" : request.PreferredLanguage,
            PreferredVolumeUnit = request.PreferredVolumeUnit ?? VolumeUnit.Liters
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.Select(e => e.Description);
            return new AuthResult(false, ErrorCode: "REGISTRATION_FAILED", ErrorMessage: "Registration failed.", Details: errors);
        }

        var defaultSetup = new BrewerySetup
        {
            UserId = user.Id,
            Name = GetDefaultBreweryName(user.PreferredLanguage),
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.BrewerySetups.Add(defaultSetup);
        await db.SaveChangesAsync();

        var authResponse = await GenerateAuthResponseAsync(user, httpContext, isNewUser: true);
        return new AuthResult(true, Response: authResponse);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, HttpContext httpContext)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return new AuthResult(false, ErrorCode: "INVALID_CREDENTIALS", ErrorMessage: "Invalid email or password.");
        }

        await EnsureUserHasDefaultSetupAsync(user.Id);

        var authResponse = await GenerateAuthResponseAsync(user, httpContext);
        return new AuthResult(true, Response: authResponse);
    }

    public async Task<AuthResult> GoogleLoginAsync(GoogleAuthRequest request, HttpContext httpContext)
    {
        var googleUser = await googleAuthValidator.ValidateAsync(request.IdToken);
        if (googleUser == null)
        {
            return new AuthResult(
                false,
                ErrorCode: "INVALID_GOOGLE_TOKEN",
                ErrorMessage: "The supplied Google credentials could not be validated or the email is unverified.");
        }

        const string provider = "Google";
        var providerKey = googleUser.SubjectId;

        // Check if user already exists with this Google provider key
        var user = await userManager.FindByLoginAsync(provider, providerKey);

        bool isNewUser = false;
        if (user == null)
        {
            // Check if user exists with matching email
            user = await userManager.FindByEmailAsync(googleUser.Email);

            if (user != null)
            {
                // Existing user: Link Google identity to existing account.
                // Guard against pre-account-takeover: Google ID tokens guarantee the email is verified (googleUser.EmailVerified == true).
                // Confirm the email on the existing account to lock in verification.
                if (!user.EmailConfirmed && googleUser.EmailVerified)
                {
                    user.EmailConfirmed = true;
                    await userManager.UpdateAsync(user);
                }

                var linkResult = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, "Google"));
                if (!linkResult.Succeeded)
                {
                    var errors = linkResult.Errors.Select(e => e.Description);
                    return new AuthResult(
                        false,
                        ErrorCode: "LOGIN_LINK_FAILED",
                        ErrorMessage: "Failed to link Google account to existing user profile.",
                        Details: errors);
                }
            }
            else
            {
                // New user: Provision ApplicationUser
                isNewUser = true;
                var preferredLang = !string.IsNullOrWhiteSpace(request.PreferredLanguage)
                    ? request.PreferredLanguage
                    : (!string.IsNullOrWhiteSpace(googleUser.Locale) && googleUser.Locale.StartsWith("sv", StringComparison.OrdinalIgnoreCase) ? "sv" : "en");

                var displayName = !string.IsNullOrWhiteSpace(googleUser.Name)
                    ? googleUser.Name
                    : googleUser.Email.Split('@')[0];

                user = new ApplicationUser
                {
                    UserName = googleUser.Email,
                    Email = googleUser.Email,
                    EmailConfirmed = googleUser.EmailVerified,
                    DisplayName = displayName,
                    PreferredLanguage = preferredLang,
                    CreatedAt = DateTime.UtcNow
                };

                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    var errors = createResult.Errors.Select(e => e.Description);
                    return new AuthResult(
                        false,
                        ErrorCode: "USER_CREATION_FAILED",
                        ErrorMessage: "Could not create user account from Google profile.",
                        Details: errors);
                }

                var linkResult = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, "Google"));
                if (!linkResult.Succeeded)
                {
                    var errors = linkResult.Errors.Select(e => e.Description);
                    return new AuthResult(
                        false,
                        ErrorCode: "LOGIN_LINK_FAILED",
                        ErrorMessage: "Account created but linking Google credentials failed.",
                        Details: errors);
                }

                var defaultSetup = new BrewerySetup
                {
                    UserId = user.Id,
                    Name = GetDefaultBreweryName(user.PreferredLanguage),
                    IsDefault = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.BrewerySetups.Add(defaultSetup);
                await db.SaveChangesAsync();
            }
        }

        await EnsureUserHasDefaultSetupAsync(user.Id);

        var authResponse = await GenerateAuthResponseAsync(user, httpContext, isNewUser: isNewUser);
        return new AuthResult(true, Response: authResponse);
    }

    public async Task<AuthResult> RefreshTokenAsync(string? refreshToken, HttpContext httpContext)
    {
        var token = refreshToken ?? httpContext.Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(token))
        {
            return new AuthResult(false, ErrorCode: "INVALID_TOKEN", ErrorMessage: "Refresh token is missing.");
        }

        var tokenHash = HashRefreshToken(token);
        var user = await userManager.Users.FirstOrDefaultAsync(u => u.RefreshToken == tokenHash);
        if (user == null || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
        {
            return new AuthResult(false, ErrorCode: "TOKEN_EXPIRED", ErrorMessage: "Refresh token is expired or invalid.");
        }

        var authResponse = await GenerateAuthResponseAsync(user, httpContext);
        return new AuthResult(true, Response: authResponse);
    }

    public async Task<UserDto?> GetCurrentUserAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return null;

        return MapToUserDto(user);
    }

    public async Task<UserDto?> UpdateUserLanguageAsync(string userId, string language)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLower().Trim();
        user.PreferredLanguage = lang;
        await userManager.UpdateAsync(user);

        return MapToUserDto(user);
    }

    public async Task<UserDto?> UpdateUserVolumeUnitAsync(string userId, VolumeUnit volumeUnit)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return null;

        user.PreferredVolumeUnit = volumeUnit;
        await userManager.UpdateAsync(user);

        return MapToUserDto(user);
    }

    public async Task<UserDto?> UpdateUserPreferencesAsync(string userId, UpdateUserPreferencesRequest request)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return null;

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            user.PreferredLanguage = request.Language.ToLower().Trim();
        }

        if (request.VolumeUnit.HasValue)
        {
            user.PreferredVolumeUnit = request.VolumeUnit.Value;
        }

        if (request.WeightUnit.HasValue)
        {
            user.PreferredWeightUnit = request.WeightUnit.Value;
            if (string.IsNullOrWhiteSpace(request.GrainWeightUnit))
            {
                user.PreferredGrainWeightUnit = request.WeightUnit.Value == WeightUnit.Imperial ? "lb" : "kg";
            }
            if (string.IsNullOrWhiteSpace(request.HopWeightUnit))
            {
                user.PreferredHopWeightUnit = request.WeightUnit.Value == WeightUnit.Imperial ? "oz" : "g";
            }
        }

        if (!string.IsNullOrWhiteSpace(request.GrainWeightUnit))
        {
            user.PreferredGrainWeightUnit = request.GrainWeightUnit.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.HopWeightUnit))
        {
            user.PreferredHopWeightUnit = request.HopWeightUnit.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ColorUnit))
        {
            user.PreferredColorUnit = request.ColorUnit.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.CarbonationUnit))
        {
            user.PreferredCarbonationUnit = request.CarbonationUnit.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.BitternessFormula))
        {
            user.PreferredBitternessFormula = request.BitternessFormula.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ColorFormula))
        {
            user.PreferredColorFormula = request.ColorFormula.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.AbvFormula))
        {
            user.PreferredAbvFormula = request.AbvFormula.Trim();
        }

        if (request.TemperatureUnit.HasValue)
        {
            user.PreferredTemperatureUnit = request.TemperatureUnit.Value;
        }

        if (request.GravityUnit.HasValue)
        {
            user.PreferredGravityUnit = request.GravityUnit.Value;
        }

        if (request.Theme.HasValue)
        {
            user.PreferredTheme = request.Theme.Value;
        }

        if (request.DefaultBatchSizeLiters.HasValue)
        {
            user.DefaultBatchSizeLiters = request.DefaultBatchSizeLiters.Value;
        }

        if (request.DefaultEfficiencyPercent.HasValue)
        {
            user.DefaultEfficiencyPercent = request.DefaultEfficiencyPercent.Value;
        }

        if (request.DefaultBoilTimeMinutes.HasValue)
        {
            user.DefaultBoilTimeMinutes = request.DefaultBoilTimeMinutes.Value;
        }

        if (request.MqttHost != null)
        {
            user.MqttHost = string.IsNullOrWhiteSpace(request.MqttHost) ? null : request.MqttHost.Trim();
        }

        if (request.MqttPort.HasValue)
        {
            user.MqttPort = request.MqttPort.Value;
        }

        if (request.MqttUsername != null)
        {
            user.MqttUsername = string.IsNullOrWhiteSpace(request.MqttUsername) ? null : request.MqttUsername.Trim();
        }

        if (request.ClearMqttPassword == true)
        {
            user.MqttPassword = null;
        }
        else if (request.MqttPassword != null)
        {
            if (string.IsNullOrWhiteSpace(request.MqttPassword))
            {
                user.MqttPassword = null;
            }
            else
            {
                user.MqttPassword = _protector.Protect(request.MqttPassword);
            }
        }

        if (request.MqttCertificate != null)
        {
            user.MqttCertificate = string.IsNullOrWhiteSpace(request.MqttCertificate) ? null : request.MqttCertificate.Trim();
        }

        if (request.MqttTopicPrefix != null)
        {
            var trimmed = request.MqttTopicPrefix.Trim().Trim('/');
            user.MqttTopicPrefix = string.IsNullOrWhiteSpace(trimmed) ? "brewyou/equipment" : trimmed;
        }

        if (request.AppBaseUrl != null)
        {
            user.AppBaseUrl = string.IsNullOrWhiteSpace(request.AppBaseUrl) ? null : request.AppBaseUrl.Trim().TrimEnd('/');
        }

        if (request.WebhookBaseUrl != null)
        {
            user.WebhookBaseUrl = string.IsNullOrWhiteSpace(request.WebhookBaseUrl) ? null : request.WebhookBaseUrl.Trim().TrimEnd('/');
        }

        var mqttChanged = request.MqttHost != null ||
                          request.MqttPort.HasValue ||
                          request.MqttUsername != null ||
                          request.MqttPassword != null ||
                          request.ClearMqttPassword == true ||
                          request.MqttCertificate != null;

        await userManager.UpdateAsync(user);

        if (mqttChanged)
        {
            await mqttService.DisconnectUserAsync(userId);
        }

        return MapToUserDto(user);
    }

    public async Task<UserDto?> UpdateUserProfileAsync(string userId, UpdateProfileRequest request)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return null;

        user.DisplayName = request.DisplayName.Trim();
        await userManager.UpdateAsync(user);

        return MapToUserDto(user);
    }

    public async Task<AuthResult> ChangePasswordAsync(string userId, ChangePasswordRequest request, HttpContext httpContext)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return new AuthResult(false, ErrorCode: "USER_NOT_FOUND", ErrorMessage: "User profile not found.");
        }

        var hasPassword = await userManager.HasPasswordAsync(user);
        if (!hasPassword)
        {
            return new AuthResult(
                false,
                ErrorCode: "NO_PASSWORD_SET",
                ErrorMessage: "This account uses external sign-in and does not have a local password.");
        }

        var changeResult = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changeResult.Succeeded)
        {
            var errors = changeResult.Errors.Select(e => e.Description);
            return new AuthResult(
                false,
                ErrorCode: "PASSWORD_CHANGE_FAILED",
                ErrorMessage: "Failed to change password. Please verify your current password.",
                Details: errors);
        }

        // Rotate refresh token so any previously issued session tokens are immediately invalidated
        var newRefreshToken = tokenService.GenerateRefreshToken();
        var expiry = DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenExpirationDays);
        user.RefreshToken = HashRefreshToken(newRefreshToken);
        user.RefreshTokenExpiryTime = expiry;
        await userManager.UpdateAsync(user);

        SetRefreshTokenCookie(httpContext, newRefreshToken, expiry);

        return new AuthResult(true);
    }

    public async Task LogoutAsync(string? userId, HttpContext httpContext)
    {
        ApplicationUser? user = null;
        if (!string.IsNullOrEmpty(userId))
        {
            user = await userManager.FindByIdAsync(userId);
        }
        else
        {
            var token = httpContext.Request.Cookies["refreshToken"];
            if (!string.IsNullOrEmpty(token))
            {
                var tokenHash = HashRefreshToken(token);
                user = await userManager.Users.FirstOrDefaultAsync(u => u.RefreshToken == tokenHash);
            }
        }

        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            await userManager.UpdateAsync(user);
        }

        httpContext.Response.Cookies.Delete("refreshToken", new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth"
        });
        httpContext.Response.Cookies.Delete("refreshToken");
    }

    public static string HashRefreshToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<AuthResponse> GenerateAuthResponseAsync(ApplicationUser user, HttpContext httpContext, bool isNewUser = false)
    {
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();
        var refreshTokenDays = jwtOptions.Value.RefreshTokenExpirationDays;

        user.RefreshToken = HashRefreshToken(refreshToken);
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(refreshTokenDays);
        await userManager.UpdateAsync(user);

        SetRefreshTokenCookie(httpContext, refreshToken, user.RefreshTokenExpiryTime.Value);

        var expiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.AccessTokenExpirationMinutes);
        var userDto = MapToUserDto(user);

        // Security: Never expose the raw refresh token in JSON responses; clients rely on the HttpOnly cookie.
        return new AuthResponse(accessToken, string.Empty, expiresAt, userDto, isNewUser);
    }

    private static UserDto MapToUserDto(ApplicationUser user)
    {
        var preferences = new UserPreferencesDto(
            user.PreferredLanguage,
            user.PreferredVolumeUnit,
            user.PreferredWeightUnit,
            user.PreferredTemperatureUnit,
            user.PreferredGravityUnit,
            user.PreferredTheme,
            user.DefaultBatchSizeLiters,
            user.DefaultEfficiencyPercent,
            user.DefaultBoilTimeMinutes,
            user.MqttHost,
            user.MqttPort,
            user.MqttUsername,
            MqttPassword: null,
            user.MqttCertificate,
            HasMqttPassword: !string.IsNullOrEmpty(user.MqttPassword),
            MqttTopicPrefix: user.MqttTopicPrefix ?? "brewyou/equipment",
            GrainWeightUnit: user.PreferredGrainWeightUnit ?? (user.PreferredWeightUnit == WeightUnit.Imperial ? "lb" : "kg"),
            HopWeightUnit: user.PreferredHopWeightUnit ?? (user.PreferredWeightUnit == WeightUnit.Imperial ? "oz" : "g"),
            ColorUnit: user.PreferredColorUnit ?? "SRM",
            CarbonationUnit: user.PreferredCarbonationUnit ?? "Volumes",
            BitternessFormula: user.PreferredBitternessFormula ?? "Tinseth",
            ColorFormula: user.PreferredColorFormula ?? "Morey",
            AbvFormula: user.PreferredAbvFormula ?? "Linear",
            AppBaseUrl: user.AppBaseUrl,
            WebhookBaseUrl: user.WebhookBaseUrl
        );

        return new UserDto(
            user.Id,
            user.Email!,
            user.DisplayName ?? user.UserName!,
            user.PreferredLanguage,
            user.PreferredVolumeUnit,
            user.CreatedAt,
            preferences,
            HasPassword: !string.IsNullOrEmpty(user.PasswordHash)
        );
    }

    private static void SetRefreshTokenCookie(HttpContext context, string token, DateTime expires)
    {
        context.Response.Cookies.Append("refreshToken", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/api/v1/auth"
        });
    }

    private static string GetDefaultBreweryName(string? preferredLanguage)
    {
        return BrewerySetupService.GetDefaultBreweryName(preferredLanguage);
    }

    private async Task EnsureUserHasDefaultSetupAsync(string userId)
    {
        var hasSetup = await db.BrewerySetups.AnyAsync(s => s.UserId == userId);
        if (!hasSetup)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return;
            }

            var defaultSetup = new BrewerySetup
            {
                UserId = userId,
                Name = GetDefaultBreweryName(user.PreferredLanguage),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.BrewerySetups.Add(defaultSetup);
            await db.SaveChangesAsync();
        }
    }

    public async Task<MqttStatusResult> GetMqttStatusAsync(string userId, string? overrideHost = null, int? overridePort = null)
    {
        string? host = overrideHost;
        int? port = overridePort;

        if (string.IsNullOrWhiteSpace(host))
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null || string.IsNullOrWhiteSpace(user.MqttHost))
            {
                return new MqttStatusResult(false, false);
            }

            host = user.MqttHost;
            port = user.MqttPort ?? 1883;
        }
        else
        {
            port ??= 1883;
        }

        // Check if this user's persistent MQTT client is already actively connected to this host/port
        var status = mqttService.GetStatus(userId, host, port);
        if (status.Connected)
        {
            return new MqttStatusResult(true, true, host, port.Value);
        }

        var isConnected = await mqttConnectivityChecker.CanConnectAsync(host, port.Value, TimeSpan.FromSeconds(2));
        return new MqttStatusResult(true, isConnected, host, port.Value);
    }
}