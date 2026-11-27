using BrewYou.ApiService.Auth;
using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services.Labels;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BrewYou.ApiService.Tests;

public class JwtSigningKeyProviderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public JwtSigningKeyProviderTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InitializeAsync_WhenEnvVarConfigured_UsesConfiguredSecretWithoutPersistingToDb()
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        string configuredSecret = "ExplicitProductionSecretKey_AtLeast32BytesLong!";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();

        JwtSigningKeyProvider provider = CreateProvider(
            serviceProvider,
            dataProtection,
            configuredSecret,
            Environments.Production);

        await provider.InitializeAsync();

        provider.GetSecretKey().Should().Be(configuredSecret);
        provider.GetSigningKey().Key.Should().Equal(Encoding.UTF8.GetBytes(configuredSecret));

        using IServiceScope scope = serviceProvider.CreateScope();
        BrewYouDbContext db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        (await db.SystemSettings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InitializeAsync_WhenNoSecretConfigured_GeneratesPersistsEncryptedAndReusesAcrossRestarts()
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        IDataProtectionProvider sharedDataProtection = new EphemeralDataProtectionProvider();

        JwtSigningKeyProvider firstBootProvider = CreateProvider(
            serviceProvider,
            sharedDataProtection,
            configuredSecret: null,
            Environments.Production);

        await firstBootProvider.InitializeAsync();
        string firstSecret = firstBootProvider.GetSecretKey();
        Encoding.UTF8.GetByteCount(firstSecret).Should().BeGreaterThanOrEqualTo(32);

        using (IServiceScope scope = serviceProvider.CreateScope())
        {
            BrewYouDbContext db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            SystemSetting? setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == JwtSigningKeyProvider.JwtSecretSettingKey);
            setting.Should().NotBeNull();
            setting!.Value.Should().NotBe(firstSecret);
            setting.Value.Should().NotContain(firstSecret);
        }

        JwtSigningKeyProvider secondBootProvider = CreateProvider(
            serviceProvider,
            sharedDataProtection,
            configuredSecret: "",
            Environments.Production);

        await secondBootProvider.InitializeAsync();
        secondBootProvider.GetSecretKey().Should().Be(firstSecret);
    }

    [Fact]
    public async Task InitializeAsync_WhenWhitespaceOnlySecretConfigured_FallsBackToPersistedDbKey()
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();

        JwtSigningKeyProvider provider = CreateProvider(
            serviceProvider,
            dataProtection,
            configuredSecret: "     \t  ",
            Environments.Production);

        await provider.InitializeAsync();

        string resolved = provider.GetSecretKey();
        Encoding.UTF8.GetByteCount(resolved).Should().BeGreaterThanOrEqualTo(32);

        using IServiceScope scope = serviceProvider.CreateScope();
        BrewYouDbContext db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        (await db.SystemSettings.CountAsync(s => s.Key == JwtSigningKeyProvider.JwtSecretSettingKey)).Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_WhenEnvVarProvidedAfterPersistedKeyExists_PrefersEnvVarWithoutOverwritingDb()
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        IDataProtectionProvider sharedDataProtection = new EphemeralDataProtectionProvider();

        JwtSigningKeyProvider firstBootProvider = CreateProvider(
            serviceProvider,
            sharedDataProtection,
            configuredSecret: null,
            Environments.Production);
        await firstBootProvider.InitializeAsync();
        string persistedSecret = firstBootProvider.GetSecretKey();

        string overrideSecret = "OperatorOverrideSecretKey_AtLeast32BytesLong!!";
        JwtSigningKeyProvider overrideProvider = CreateProvider(
            serviceProvider,
            sharedDataProtection,
            overrideSecret,
            Environments.Production);
        await overrideProvider.InitializeAsync();

        overrideProvider.GetSecretKey().Should().Be(overrideSecret);

        JwtSigningKeyProvider revertedProvider = CreateProvider(
            serviceProvider,
            sharedDataProtection,
            configuredSecret: null,
            Environments.Production);
        await revertedProvider.InitializeAsync();
        revertedProvider.GetSecretKey().Should().Be(persistedSecret);
    }

    [Fact]
    public async Task InitializeAsync_WhenKnownDevelopmentSecretConfiguredInProduction_ThrowsInvalidOperationException()
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();

        JwtSigningKeyProvider provider = CreateProvider(
            serviceProvider,
            dataProtection,
            JwtSigningKeyProvider.KnownDevelopmentSecret,
            Environments.Production);

        Func<Task> act = () => provider.InitializeAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*default development JWT secret*");
    }

    [Theory]
    [InlineData(" SuperSecretBrewYouKey_AtLeast32BytesLong! ", false, "*default development JWT secret*")]
    [InlineData("supersecretbrewyoukey_atleast32byteslong!", false, "*default development JWT secret*")]
    [InlineData("ExplicitProductionSecretKey_AtLeast32BytesLong!\n", false, "*control characters*")]
    [InlineData("ExplicitProductionSecretKey_AtLeast32BytesLong!\r\n", true, "*control characters*")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false, "*insufficient entropy*")]
    [InlineData("🍺🍺🍺🍺🍺🍺🍺🍺", false, "*insufficient entropy*")]
    [InlineData("                                        ", false, "*empty or whitespace*")]
    [InlineData("too-short-secret", true, "*at least 256 bits*")]
    public void ValidateConfiguredSecret_RejectsBypassAttemptsAndLowEntropySecrets(
        string candidateSecret,
        bool isDevelopment,
        string expectedMessagePattern)
    {
        Action act = () => JwtSigningKeyProvider.ValidateConfiguredSecret(candidateSecret, isDevelopment);
        act.Should().Throw<InvalidOperationException>().WithMessage(expectedMessagePattern);
    }

    [Fact]
    public async Task InitializeAsync_WhenSecretTooShort_ThrowsInvalidOperationException()
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();

        JwtSigningKeyProvider provider = CreateProvider(
            serviceProvider,
            dataProtection,
            "too-short-secret",
            Environments.Development);

        Func<Task> act = () => provider.InitializeAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*at least 256 bits*");
    }

    [Fact]
    public async Task InitializeAsync_WhenPersistedKeyCannotBeDecrypted_ThrowsInvalidOperationException()
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);

        JwtSigningKeyProvider writerProvider = CreateProvider(
            serviceProvider,
            new EphemeralDataProtectionProvider(),
            configuredSecret: null,
            Environments.Production);
        await writerProvider.InitializeAsync();

        JwtSigningKeyProvider mismatchedKeyRingProvider = CreateProvider(
            serviceProvider,
            new EphemeralDataProtectionProvider(),
            configuredSecret: null,
            Environments.Production);

        Func<Task> act = () => mismatchedKeyRingProvider.InitializeAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Could not decrypt the persisted JWT signing key*");
    }

    [Theory]
    [InlineData("short-persisted-secret", "*at least 256 bits*")]
    [InlineData(JwtSigningKeyProvider.KnownDevelopmentSecret, "*default development JWT secret*")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", "*insufficient entropy*")]
    public async Task InitializeAsync_WhenPersistedDbKeyFailsPostDecryptionValidation_ThrowsInvalidOperationException(
        string invalidPlaintextSecret,
        string expectedMessagePattern)
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();

        using (IServiceScope scope = serviceProvider.CreateScope())
        {
            BrewYouDbContext db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            string protectedValue = dataProtection.CreateProtector("BrewYou.JwtSigningKey").Protect(invalidPlaintextSecret);
            db.SystemSettings.Add(new SystemSetting
            {
                Key = JwtSigningKeyProvider.JwtSecretSettingKey,
                Value = protectedValue
            });
            await db.SaveChangesAsync();
        }

        JwtSigningKeyProvider provider = CreateProvider(
            serviceProvider,
            dataProtection,
            configuredSecret: null,
            Environments.Production);

        Func<Task> act = () => provider.InitializeAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(expectedMessagePattern);
    }

    [Fact]
    public void GetSecretKey_BeforeInitialization_ThrowsInvalidOperationException()
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        JwtSigningKeyProvider provider = CreateProvider(
            serviceProvider,
            new EphemeralDataProtectionProvider(),
            configuredSecret: null,
            Environments.Production);

        Action actSecret = () => provider.GetSecretKey();
        Action actSigningKey = () => provider.GetSigningKey();

        actSecret.Should().Throw<InvalidOperationException>();
        actSigningKey.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentFirstBoot_ConvergesOnSinglePersistedKey()
    {
        string dbName = $"JwtKeyTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        IDataProtectionProvider sharedDataProtection = new EphemeralDataProtectionProvider();

        JwtSigningKeyProvider[] providers = Enumerable.Range(0, 6)
            .Select(_ => CreateProvider(serviceProvider, sharedDataProtection, configuredSecret: null, Environments.Production))
            .ToArray();

        await Task.WhenAll(providers.Select(p => p.InitializeAsync()));

        string[] resolvedSecrets = providers.Select(p => p.GetSecretKey()).Distinct().ToArray();
        resolvedSecrets.Should().HaveCount(1);
        Encoding.UTF8.GetByteCount(resolvedSecrets[0]).Should().BeGreaterThanOrEqualTo(32);

        using IServiceScope scope = serviceProvider.CreateScope();
        BrewYouDbContext db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        (await db.SystemSettings.CountAsync(s => s.Key == JwtSigningKeyProvider.JwtSecretSettingKey)).Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_ExecutesThroughDatabaseExecutionStrategy()
    {
        string dbName = $"JwtKeyStrategyTest_{Guid.NewGuid():N}";
        TrackingExecutionStrategyFactory strategyFactory = new();

        ServiceCollection customServices = new();
        customServices.AddDbContext<BrewYouDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                .UseInternalServiceProvider(
                    new ServiceCollection()
                        .AddEntityFrameworkInMemoryDatabase()
                        .AddSingleton<IExecutionStrategyFactory>(strategyFactory)
                        .BuildServiceProvider()));
        using ServiceProvider serviceProvider = customServices.BuildServiceProvider();

        JwtSigningKeyProvider provider = CreateProvider(
            serviceProvider,
            new EphemeralDataProtectionProvider(),
            configuredSecret: null,
            Environments.Production);

        await provider.InitializeAsync();

        strategyFactory.ExecutionCount.Should().BeGreaterThanOrEqualTo(1);
        Encoding.UTF8.GetByteCount(provider.GetSecretKey()).Should().BeGreaterThanOrEqualTo(32);
    }

    [Fact]
    public async Task PersistedJwtSecret_IsIsolatedFromOtherDataProtectionPurposes()
    {
        string dbName = $"JwtKeyPurposeTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();

        JwtSigningKeyProvider provider = CreateProvider(
            serviceProvider,
            dataProtection,
            configuredSecret: null,
            Environments.Production);
        await provider.InitializeAsync();

        using IServiceScope scope = serviceProvider.CreateScope();
        BrewYouDbContext db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        SystemSetting setting = await db.SystemSettings.SingleAsync(s => s.Key == JwtSigningKeyProvider.JwtSecretSettingKey);

        IDataProtector mqttProtector = dataProtection.CreateProtector("BrewYou.MqttCredentials");
        Action crossPurposeDecrypt = () => mqttProtector.Unprotect(setting.Value);
        crossPurposeDecrypt.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task LabelUrlSigner_AccessedBeforeInitialization_DoesNotPoisonSingletonAfterInit()
    {
        string dbName = $"JwtKeyLabelTest_{Guid.NewGuid():N}";
        using ServiceProvider serviceProvider = BuildServiceProvider(dbName);
        JwtSigningKeyProvider provider = CreateProvider(
            serviceProvider,
            new EphemeralDataProtectionProvider(),
            configuredSecret: null,
            Environments.Production);

        LabelUrlSigner signer = new(Options.Create(new JwtOptions()), provider);
        Guid recipeId = Guid.NewGuid();
        long expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();

        Action preInitCall = () => signer.GenerateSignature(recipeId, "bottle", 512, 1, expires);
        preInitCall.Should().Throw<InvalidOperationException>();

        await provider.InitializeAsync();

        string signature = signer.GenerateSignature(recipeId, "bottle", 512, 1, expires);
        signer.VerifySignature(recipeId, "bottle", 512, 1, expires, signature).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    public void TokenServiceAndLabelUrlSigner_WithoutProvider_RejectEmptyOrShortSecret(string invalidSecret)
    {
        IOptions<JwtOptions> options = Options.Create(new JwtOptions
        {
            Issuer = "BrewYou",
            Audience = "BrewYouApp",
            SecretKey = invalidSecret
        });

        TokenService tokenService = new(options);
        ApplicationUser user = new() { Id = "u1", Email = "brewer@example.com", UserName = "brewer@example.com" };
        Action tokenAct = () => tokenService.GenerateAccessToken(user);
        tokenAct.Should().Throw<InvalidOperationException>();

        LabelUrlSigner labelSigner = new(options);
        Action labelAct = () => labelSigner.GenerateSignature(Guid.NewGuid(), "bottle", 512, 1, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        labelAct.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(SecurityAlgorithms.HmacSha384)]
    [InlineData(SecurityAlgorithms.HmacSha512)]
    public void TokenService_GetPrincipalFromExpiredToken_RejectsNonHs256AlgorithmTokens(string nonHs256Algorithm)
    {
        string secret = "StrongSigningSecretKeyForAlgorithmPinning_64BytesLong_1234567890!";
        JwtOptions options = new()
        {
            Issuer = "BrewYouTestIssuer",
            Audience = "BrewYouTestAudience",
            SecretKey = secret
        };

        TokenService tokenService = new(Options.Create(options));
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(secret));
        SigningCredentials creds = new(key, nonHs256Algorithm);
        JwtSecurityToken forgedAlgToken = new(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: [new Claim(ClaimTypes.NameIdentifier, "user-1")],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        string tokenString = new JwtSecurityTokenHandler().WriteToken(forgedAlgToken);
        tokenService.GetPrincipalFromExpiredToken(tokenString).Should().BeNull();
    }

    [Fact]
    public async Task ProductionHost_WithoutJwtSecretEnvVar_AutoGeneratesPersistsAndAuthenticatesEndToEnd()
    {
        using WebApplicationFactory<Program> prodFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("Jwt:SecretKey", "");
        });

        using HttpClient client = prodFactory.CreateClient();

        string email = $"autoprod_{Guid.NewGuid():N}@brewyou.test";
        HttpResponseMessage registerResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(email, "Password123!", "Auto Prod Brewer"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        ApiResponse<AuthResponse>? registerPayload = await registerResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        registerPayload.Should().NotBeNull();
        registerPayload!.Success.Should().BeTrue();
        string accessToken = registerPayload.Data!.AccessToken;
        accessToken.Should().NotBeNullOrWhiteSpace();

        using HttpRequestMessage meRequest = new(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        HttpResponseMessage meResponse = await client.SendAsync(meRequest);
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify user backup export never leaks SystemSettings or the JWT secret
        IJwtSigningKeyProvider keyProvider = prodFactory.Services.GetRequiredService<IJwtSigningKeyProvider>();
        using HttpRequestMessage exportRequest = new(HttpMethod.Post, "/api/v1/backup/export")
        {
            Content = JsonContent.Create(new { includeSecrets = true, passphrase = "BackupTestPassphrase123!" })
        };
        exportRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        HttpResponseMessage exportResponse = await client.SendAsync(exportRequest);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        string exportBody = await exportResponse.Content.ReadAsStringAsync();
        exportBody.Should().NotContain(keyProvider.GetSecretKey());
        exportBody.Should().NotContain("SystemSettings");

        // Verify LabelUrlSigner also uses the resolved persisted signing key cleanly
        ILabelUrlSigner labelUrlSigner = prodFactory.Services.GetRequiredService<ILabelUrlSigner>();
        Guid recipeId = Guid.NewGuid();
        long expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        string signature = labelUrlSigner.GenerateSignature(recipeId, "bottle", 512, 1, expires);
        labelUrlSigner.VerifySignature(recipeId, "bottle", 512, 1, expires, signature).Should().BeTrue();
    }

    [Fact]
    public async Task ProductionHost_RejectsHs384BearerTokenSignedWithSameKey()
    {
        const string explicit64ByteSecret = "Explicit64ByteSecretKeyForAlgorithmPinningCheck_1234567890ABCDEF";
        using WebApplicationFactory<Program> prodFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("Jwt:SecretKey", explicit64ByteSecret);
        });

        using HttpClient client = prodFactory.CreateClient();

        string email = $"algpin_{Guid.NewGuid():N}@brewyou.test";
        HttpResponseMessage registerResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(email, "Password123!", "Alg Pin Brewer"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        ApiResponse<AuthResponse>? registerPayload = await registerResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        string userId = registerPayload!.Data!.User.Id;

        IJwtSigningKeyProvider keyProvider = prodFactory.Services.GetRequiredService<IJwtSigningKeyProvider>();
        string hs384Token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "BrewYou",
            audience: "BrewYouApp",
            claims: [new Claim(ClaimTypes.NameIdentifier, userId)],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(keyProvider.GetSigningKey(), SecurityAlgorithms.HmacSha384)));

        using HttpRequestMessage hs384Request = new(HttpMethod.Get, "/api/v1/auth/me");
        hs384Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hs384Token);
        HttpResponseMessage hs384Response = await client.SendAsync(hs384Request);
        hs384Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void ProductionHost_WithKnownDevelopmentSecret_FailsFastOnStartup()
    {
        using WebApplicationFactory<Program> prodFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("Jwt:SecretKey", JwtSigningKeyProvider.KnownDevelopmentSecret);
        });

        Action act = () => prodFactory.CreateClient();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*default development JWT secret*");
    }

    [Fact]
    public void ProductionHost_WithTooShortSecret_FailsFastOnStartup()
    {
        using WebApplicationFactory<Program> prodFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("Jwt:SecretKey", "short-secret");
        });

        Action act = () => prodFactory.CreateClient();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 256 bits*");
    }

    private static ServiceProvider BuildServiceProvider(string databaseName)
    {
        ServiceCollection services = new();
        services.AddDbContext<BrewYouDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static JwtSigningKeyProvider CreateProvider(
        IServiceProvider serviceProvider,
        IDataProtectionProvider dataProtectionProvider,
        string? configuredSecret,
        string environmentName)
    {
        Dictionary<string, string?> configValues = [];
        if (configuredSecret is not null)
        {
            configValues[JwtSigningKeyProvider.JwtSecretSettingKey] = configuredSecret;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return new JwtSigningKeyProvider(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            new StubHostEnvironment(environmentName),
            dataProtectionProvider,
            NullLogger<JwtSigningKeyProvider>.Instance);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BrewYou.ApiService";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TrackingExecutionStrategyFactory : IExecutionStrategyFactory
    {
        private int _executionCount;
        public int ExecutionCount => Volatile.Read(ref _executionCount);

        public IExecutionStrategy Create() => new TrackingExecutionStrategy(this);

        private void RecordExecution() => Interlocked.Increment(ref _executionCount);

        private sealed class TrackingExecutionStrategy(TrackingExecutionStrategyFactory factory) : IExecutionStrategy
        {
            public bool RetriesOnFailure => true;

            public TResult Execute<TState, TResult>(
                TState state,
                Func<DbContext, TState, TResult> operation,
                Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
            {
                factory.RecordExecution();
                return operation(null!, state);
            }

            public Task<TResult> ExecuteAsync<TState, TResult>(
                TState state,
                Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
                Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
                CancellationToken cancellationToken = default)
            {
                factory.RecordExecution();
                return operation(null!, state, cancellationToken);
            }
        }
    }
}