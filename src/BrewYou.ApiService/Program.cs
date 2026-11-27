using BrewYou.ApiService.Auth;
using BrewYou.ApiService.Backup.Versioning;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Middleware;
using BrewYou.ApiService.Services;
using BrewYou.ApiService.Services.Labels;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults (telemetry, health checks, discovery)
builder.AddServiceDefaults();

// Global exception handling & ProblemDetails
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Configure EF Core & Database (PostgreSQL via Aspire or fallback in-memory for testing/local fallback)
var hasDatabaseConnectionString = builder.Configuration.GetConnectionString("brewyou-db") != null;
if (hasDatabaseConnectionString)
{
    builder.AddNpgsqlDbContext<BrewYouDbContext>("brewyou-db");
}
else
{
    builder.Services.AddDbContext<BrewYouDbContext>(options =>
        options.UseInMemoryDatabase("BrewYouFallbackDb"));
}

// Data Protection (Persist keys in DB so tokens and credentials survive container restarts)
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("BrewYou")
    .PersistKeysToDbContext<BrewYouDbContext>();

var dataProtectionCertPath = builder.Configuration["DataProtection:CertificatePath"];
if (!string.IsNullOrWhiteSpace(dataProtectionCertPath))
{
    var dataProtectionCertPassword = builder.Configuration["DataProtection:CertificatePassword"];
    var cert = string.IsNullOrEmpty(dataProtectionCertPassword)
        ? X509CertificateLoader.LoadPkcs12FromFile(dataProtectionCertPath, password: null)
        : X509CertificateLoader.LoadPkcs12FromFile(dataProtectionCertPath, dataProtectionCertPassword);
    if (!cert.HasPrivateKey)
    {
        cert.Dispose();
        throw new InvalidOperationException("Fatal security error: The configured DataProtection certificate must include a private key.");
    }

    dataProtectionBuilder.ProtectKeysWithCertificate(cert);
}

// Forwarded headers for reverse-proxy TLS termination (populates Request.IsHttps safely)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// In-Memory Cache for image renditions and metadata
builder.Services.AddMemoryCache();

// Identity Core
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<BrewYouDbContext>();

// JWT Authentication & Token Service
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var configuredJwtSecret = builder.Configuration["Jwt:SecretKey"];
if (!string.IsNullOrWhiteSpace(configuredJwtSecret))
{
    JwtSigningKeyProvider.ValidateConfiguredSecret(configuredJwtSecret, builder.Environment.IsDevelopment());
}

builder.Services.AddSingleton<IJwtSigningKeyProvider, JwtSigningKeyProvider>();

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "BrewYou";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "BrewYouApp";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IJwtSigningKeyProvider>((options, keyProvider) =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = keyProvider.GetSigningKey(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.PostConfigure<GoogleAuthOptions>(options =>
{
    options.ClientId = (string.IsNullOrWhiteSpace(options.ClientId)
        ? builder.Configuration["GOOGLE_CLIENT_ID"]
        : options.ClientId)?.Trim() ?? string.Empty;
});
builder.Services.AddScoped<IGoogleAuthValidator, GoogleAuthValidator>();

// Domain Services
builder.Services.AddSingleton<ILabelImageProcessor, SkiaLabelImageProcessor>();
builder.Services.AddSingleton<ILabelUrlSigner, LabelUrlSigner>();
builder.Services.AddScoped<IRecipeLabelService, RecipeLabelService>();
builder.Services.AddScoped<IRecipeService, RecipeService>();
builder.Services.AddScoped<IIngredientService, IngredientService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEquipmentService, EquipmentService>();
builder.Services.AddScoped<IBrewerySetupService, BrewerySetupService>();
builder.Services.AddScoped<IBreweryCollaborationService, BreweryCollaborationService>();
builder.Services.AddScoped<IBatchService, BatchService>();
builder.Services.AddSingleton<IBackupSchemaMigrator, V0ToV1BackupMigrator>();
builder.Services.AddSingleton<IBackupSchemaMigrator, V1ToV2BackupMigrator>();
builder.Services.AddSingleton<IBackupSchemaMigrator, V2ToV3BackupMigrator>();
builder.Services.AddSingleton<BackupMigrationPipeline>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("TelemetryPoller", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.MaxResponseContentBufferSize = 64 * 1024; // 64 KB limit to prevent response amplification DoS
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false, // Critical SSRF defense: Prevent following redirects to internal targets
    ConnectTimeout = TimeSpan.FromSeconds(3),
    ConnectCallback = NetworkSecurityValidator.CreateSsrfSafeConnectCallback(allowPrivateNetworks: false, allowLoopback: false)
});
builder.Services.AddHttpClient("BrewLinkPoller", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.MaxResponseContentBufferSize = 64 * 1024;
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    ConnectTimeout = TimeSpan.FromSeconds(3),
    ConnectCallback = NetworkSecurityValidator.CreateSsrfSafeConnectCallback(allowPrivateNetworks: true, allowLoopback: false)
});
builder.Services.AddSingleton<ITelemetryBroadcastService, TelemetryBroadcastService>();
builder.Services.AddSingleton<ITelemetryThrottlingService, TelemetryThrottlingService>();
builder.Services.AddSingleton<IBrewLinkWebSocketManager, BrewLinkWebSocketManager>();
builder.Services.AddSingleton<IBrewLinkDiscoveryService, BrewLinkDiscoveryService>();
builder.Services.Configure<NetworkSecurityOptions>(builder.Configuration.GetSection(NetworkSecurityOptions.SectionName));
builder.Services.AddSingleton<IMqttConnectivityChecker, MqttConnectivityChecker>();
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddHttpClient<IBrewLinkService, BrewLinkService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHostedService<EquipmentPollingWorker>();
builder.Services.AddHostedService<MqttWorker>();
builder.Services.AddHostedService<BrewLinkDiscoveryWorker>();
builder.Services.AddHostedService<BrewLinkWebSocketWorker>();

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Rate Limiting (thwart brute-force credential stuffing and unbounded partition DoS)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsDevelopment() ? 5000 : 300,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("auth-rate-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsDevelopment() ? 1000 : 60,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("ingredient-creation-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("telemetry-rate-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsDevelopment() ? 600 : 120,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("backup-rate-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsDevelopment() ? 120 : 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("mqtt-status-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsDevelopment() ? 120 : 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("test-poll-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsDevelopment() ? 200 : 20,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("batch-write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsDevelopment() ? 600 : 120,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("label-upload-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("tap-display-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsDevelopment() ? 600 : 120,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// CORS: Strictly allow configured frontend origins or localhost/127.0.0.1/private LAN when permitted
var rawOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var configuredOrigins = rawOrigins
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .Select(o =>
    {
        var trimmed = o.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"http://{trimmed}";
    })
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

var allowPrivateNetworks = builder.Configuration.GetValue<bool>("NetworkSecurity:AllowPrivateNetworks") || builder.Environment.IsDevelopment();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (configuredOrigins.Length > 0)
        {
            policy.WithOrigins(configuredOrigins);
        }

        policy.SetIsOriginAllowed(origin =>
        {
            if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host;
            if (host is "localhost" or "127.0.0.1" or "::1")
            {
                return true;
            }

            if (allowPrivateNetworks)
            {
                if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (IPAddress.TryParse(host, out var ip))
                {
                    if (ip.IsIPv4MappedToIPv6)
                    {
                        ip = ip.MapToIPv4();
                    }

                    if (IPAddress.IsLoopback(ip))
                    {
                        return true;
                    }

                    var b = ip.GetAddressBytes();
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
                        if (b[0] == 10 ||
                            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                            (b[0] == 192 && b[1] == 168))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        });

        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Localization (en, sv)
builder.Services.AddLocalization();
var supportedCultures = new[] { "en-US", "en", "sv-SE", "sv" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("en-US")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

// OpenAPI & Documentation
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.License = new()
        {
            Name = "MIT",
            Url = new Uri("https://opensource.org/licenses/MIT")
        };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment() && !hasDatabaseConnectionString)
{
    app.Logger.LogCritical("ConnectionStrings:brewyou-db is not configured in a non-development environment. Falling back to an ephemeral in-memory database; all data and auto-generated signing keys will be lost on restart.");
}

if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(dataProtectionCertPath))
{
    app.Logger.LogWarning("DataProtection:CertificatePath is not configured. Data Protection keys and auto-persisted JWT signing keys in PostgreSQL are not encrypted at rest with an external certificate.");
}

app.UseForwardedHeaders();
app.UseSecurityHeaders();
app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.UseRequestLocalization(localizationOptions);
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "BrewYou API Documentation";
        options.Theme = ScalarTheme.Purple;
    });
}

// Map minimal api route groups
app.MapAuthEndpoints().RequireRateLimiting("auth-rate-limit");
app.MapIngredientEndpoints();
app.MapRecipeEndpoints();
app.MapRecipeLabelEndpoints();
app.MapLabelImageEndpoints();
app.MapTapDisplayEndpoints();
app.MapEquipmentEndpoints();
app.MapBrewerySetupEndpoints();
app.MapBreweryCollaborationEndpoints();
app.MapBatchEndpoints();
app.MapBackupEndpoints().RequireRateLimiting("backup-rate-limit");
app.MapTelemetryEndpoints();
app.MapBrewLinkEndpoints();

// Seed initial database catalog
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DbSeeder.SeedAsync(db);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not run initial database migration/seeding on startup.");
}

// Initialize JWT signing key (configured env var first, otherwise auto-generated & persisted in DB)
await app.Services.GetRequiredService<IJwtSigningKeyProvider>().InitializeAsync();

app.Run();

// Make Program accessible to WebApplicationFactory test runner
public partial class Program { }