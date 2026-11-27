using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace BrewYou.ApiService.Auth;

public sealed class JwtSigningKeyProvider(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<JwtSigningKeyProvider> logger) : IJwtSigningKeyProvider
{
    public const string JwtSecretSettingKey = "Jwt:SecretKey";
    public const string KnownDevelopmentSecret = "SuperSecretBrewYouKey_AtLeast32BytesLong!";

    private const string DataProtectionPurpose = "BrewYou.JwtSigningKey";
    private const long PostgresAdvisoryLockId = 7283910421L;
    private const int MinimumSecretBytes = 32;
    private const int MinimumDistinctCharacters = 8;

    private sealed record ResolvedKey(string Secret, SymmetricSecurityKey SigningKey);

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile ResolvedKey? _resolved;

    public static void ValidateConfiguredSecret(string secret, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Fatal security error: Jwt:SecretKey must not be empty or whitespace.");
        }

        if (secret.Any(char.IsControl))
        {
            throw new InvalidOperationException("Fatal security error: Jwt:SecretKey must not contain control characters.");
        }

        if (!isDevelopment && string.Equals(secret.Trim(), KnownDevelopmentSecret, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Fatal security error: The default development JWT secret must not be used in non-development environments.");
        }

        if (Encoding.UTF8.GetByteCount(secret) < MinimumSecretBytes)
        {
            throw new InvalidOperationException("Fatal security error: Jwt:SecretKey must be at least 256 bits (32 bytes) long.");
        }

        if (secret.Distinct().Count() < MinimumDistinctCharacters)
        {
            throw new InvalidOperationException(
                $"Fatal security error: Jwt:SecretKey has insufficient entropy (must contain at least {MinimumDistinctCharacters} distinct characters).");
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved is not null)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_resolved is not null)
            {
                return;
            }

            string? configuredSecret = configuration[JwtSecretSettingKey];
            if (!string.IsNullOrWhiteSpace(configuredSecret))
            {
                ValidateConfiguredSecret(configuredSecret, environment.IsDevelopment());
                CacheSecret(configuredSecret);
                logger.LogInformation("Using JWT signing key from configuration.");
                return;
            }

            using IServiceScope scope = scopeFactory.CreateScope();
            BrewYouDbContext db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            string persistedSecret = await LoadOrCreatePersistedSecretAsync(db, cancellationToken);
            CacheSecret(persistedSecret);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public string GetSecretKey()
    {
        ResolvedKey? resolved = _resolved;
        return resolved is not null
            ? resolved.Secret
            : throw new InvalidOperationException("JWT signing key has not been initialized. Ensure IJwtSigningKeyProvider.InitializeAsync() is called during application startup.");
    }

    public SymmetricSecurityKey GetSigningKey()
    {
        ResolvedKey? resolved = _resolved;
        return resolved is not null
            ? resolved.SigningKey
            : throw new InvalidOperationException("JWT signing key has not been initialized. Ensure IJwtSigningKeyProvider.InitializeAsync() is called during application startup.");
    }

    private void CacheSecret(string secret)
    {
        SymmetricSecurityKey signingKey = new(Encoding.UTF8.GetBytes(secret));
        _resolved = new ResolvedKey(secret, signingKey);
    }

    private async Task<string> LoadOrCreatePersistedSecretAsync(BrewYouDbContext db, CancellationToken cancellationToken)
    {
        IDataProtector protector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        IExecutionStrategy executionStrategy = db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            if (db.Database.IsNpgsql())
            {
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({PostgresAdvisoryLockId})",
                    cancellationToken);

                try
                {
                    string secret = await LoadOrCreateCoreAsync(db, protector, allowDuplicateKeyRecovery: false, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return secret;
                }
                catch (DbUpdateException)
                {
                    // PostgreSQL aborts the active transaction on constraint failure (25P02);
                    // roll back before re-reading the concurrently committed row.
                    await transaction.RollbackAsync(cancellationToken);
                    db.ChangeTracker.Clear();

                    return await ReadConcurrentlyPersistedSecretOrRethrowAsync(db, protector, cancellationToken);
                }
            }

            return await LoadOrCreateCoreAsync(db, protector, allowDuplicateKeyRecovery: true, cancellationToken);
        });
    }

    private async Task<string> LoadOrCreateCoreAsync(
        BrewYouDbContext db,
        IDataProtector protector,
        bool allowDuplicateKeyRecovery,
        CancellationToken cancellationToken)
    {
        SystemSetting? existing = await db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == JwtSecretSettingKey, cancellationToken);

        if (existing is not null)
        {
            string loadedSecret = UnprotectAndValidate(protector, existing.Value);
            logger.LogInformation("Loaded persisted JWT signing key from database.");
            return loadedSecret;
        }

        string generatedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(MinimumSecretBytes));
        string protectedValue = protector.Protect(generatedSecret);
        DateTime now = DateTime.UtcNow;

        db.SystemSettings.Add(new SystemSetting
        {
            Key = JwtSecretSettingKey,
            Value = protectedValue,
            CreatedAt = now,
            UpdatedAt = now
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Generated and persisted a new JWT signing key in the database.");
            return generatedSecret;
        }
        catch (Exception ex) when (allowDuplicateKeyRecovery && (ex is DbUpdateException or ArgumentException))
        {
            db.ChangeTracker.Clear();
            return await ReadConcurrentlyPersistedSecretOrRethrowAsync(db, protector, cancellationToken);
        }
    }

    private async Task<string> ReadConcurrentlyPersistedSecretOrRethrowAsync(
        BrewYouDbContext db,
        IDataProtector protector,
        CancellationToken cancellationToken)
    {
        SystemSetting? concurrent = await db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == JwtSecretSettingKey, cancellationToken);

        if (concurrent is null)
        {
            throw new InvalidOperationException("Failed to persist or retrieve the JWT signing key from the database.");
        }

        string loadedSecret = UnprotectAndValidate(protector, concurrent.Value);
        logger.LogInformation("Loaded concurrently persisted JWT signing key from database.");
        return loadedSecret;
    }

    private string UnprotectAndValidate(IDataProtector protector, string protectedValue)
    {
        string secret;
        try
        {
            secret = protector.Unprotect(protectedValue);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Fatal security error: Could not decrypt the persisted JWT signing key from the database. Restore the matching Data Protection key/certificate or configure an explicit Jwt:SecretKey.",
                ex);
        }

        ValidateConfiguredSecret(secret, environment.IsDevelopment());
        return secret;
    }
}