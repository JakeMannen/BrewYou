using BrewYou.ApiService.Auth;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Middleware;
using BrewYou.ApiService.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults (telemetry, health checks, discovery)
builder.AddServiceDefaults();

// Global exception handling & ProblemDetails
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Configure EF Core & Database (PostgreSQL via Aspire or fallback in-memory for testing/local fallback)
if (builder.Configuration.GetConnectionString("brewyou-db") != null)
{
    builder.AddNpgsqlDbContext<BrewYouDbContext>("brewyou-db");
}
else
{
    builder.Services.AddDbContext<BrewYouDbContext>(options =>
        options.UseInMemoryDatabase("BrewYouFallbackDb"));
}

// Identity Core
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<BrewYouDbContext>();

// JWT Authentication & Token Service
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtSecret = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrEmpty(jwtSecret))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Fatal security error: Jwt:SecretKey must be configured in non-development environments.");
    }
    jwtSecret = "SuperSecretBrewYouKey_AtLeast32BytesLong!";
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "BrewYou";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "BrewYouApp";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddAuthorization();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.AddScoped<IGoogleAuthValidator, GoogleAuthValidator>();

// Domain Services
builder.Services.AddScoped<IRecipeService, RecipeService>();
builder.Services.AddScoped<IIngredientService, IngredientService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEquipmentService, EquipmentService>();
builder.Services.AddScoped<IBrewerySetupService, BrewerySetupService>();
builder.Services.AddScoped<IBatchService, BatchService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ITelemetryBroadcastService, TelemetryBroadcastService>();
builder.Services.AddSingleton<IMqttConnectivityChecker, MqttConnectivityChecker>();
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddHostedService<EquipmentPollingWorker>();
builder.Services.AddHostedService<MqttWorker>();

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Rate Limiting (thwart brute-force credential stuffing)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-rate-limit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
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
            partitionKey: httpContext.Request.RouteValues["token"]?.ToString()
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// CORS: Strictly allow configured frontend origins or localhost/127.0.0.1 in development
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (configuredOrigins.Length > 0)
        {
            policy.WithOrigins(configuredOrigins);
        }
        else
        {
            policy.SetIsOriginAllowed(origin =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        return uri.Host is "localhost" or "127.0.0.1";
                    }
                }
                return false;
            });
        }

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

app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseRateLimiter();

app.MapDefaultEndpoints();

app.UseRequestLocalization(localizationOptions);
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

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
app.MapEquipmentEndpoints();
app.MapBrewerySetupEndpoints();
app.MapBatchEndpoints();
app.MapTelemetryEndpoints().RequireRateLimiting("telemetry-rate-limit");

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

app.Run();

// Make Program accessible to WebApplicationFactory test runner
public partial class Program { }