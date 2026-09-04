using System.Text;
using BrewYou.ApiService.Auth;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults (telemetry, health checks, discovery)
builder.AddServiceDefaults();

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
var jwtSecret = builder.Configuration["Jwt:SecretKey"] ?? "SuperSecretBrewYouKey_AtLeast32BytesLong!";
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

// CORS for frontend interaction
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// OpenAPI & Documentation
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

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
app.MapAuthEndpoints();
app.MapIngredientEndpoints();
app.MapRecipeEndpoints();

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
