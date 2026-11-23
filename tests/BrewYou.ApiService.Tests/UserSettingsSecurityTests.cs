using BrewYou.ApiService.Backup.Models;
using BrewYou.ApiService.Backup.Security;
using BrewYou.ApiService.Backup.Versioning;
using BrewYou.ApiService.Common;
using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Endpoints;
using BrewYou.ApiService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace BrewYou.ApiService.Tests;

public class UserSettingsSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UserSettingsSecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, UserDto User, string Email)> CreateAuthenticatedUserAsync(string displayName)
    {
        var client = _factory.CreateClient();
        var email = $"sec_{Guid.NewGuid():N}@brewyou.test";
        var res = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, "Password123!", displayName));
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth.User, email);
    }

    private static BrewYouBackupEnvelope BuildEnvelope(BackupPayload payload)
    {
        var serializerOptions = BackupMigrationPipeline.CreateCanonicalSerializerOptions();
        var dataElement = JsonSerializer.SerializeToElement(payload, serializerOptions);
        var checksum = BackupMigrationPipeline.ComputeChecksumSha256(dataElement);

        return new BrewYouBackupEnvelope(
            Format: BackupSchemaConstants.FormatIdentifier,
            SchemaVersion: BackupSchemaConstants.CurrentSchemaVersion,
            AppVersion: BackupSchemaConstants.CurrentAppVersion,
            ExportedAtUtc: DateTime.UtcNow,
            ChecksumSha256: checksum,
            Data: dataElement);
    }

    [Fact]
    public async Task BackupRestore_CollaboratorRestore_DoesNotAutoEnrollUnconsentedUsersOrEnumerateAccounts()
    {
        var (attackerClient, attackerUser, _) = await CreateAuthenticatedUserAsync("Attacker Brewer");
        var (_, victimUser, victimEmail) = await CreateAuthenticatedUserAsync("Victim Brewer");
        var nonexistentEmail = $"nobody_{Guid.NewGuid():N}@brewyou.test";

        var setups = (await (await attackerClient.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var attackerSetupId = setups[0].Id;

        var craftedPayload = new BackupPayload
        {
            OwnedBrewerySetups =
            [
                new BrewerySetupBackupDto(
                    Id: attackerSetupId,
                    Name: "Attacker Brewery",
                    Description: "Crafted backup",
                    IsDefault: true,
                    Members:
                    [
                        new BreweryMemberBackupDto(victimUser.Id, victimEmail, "Victim", BreweryRole.Viewer, DateTime.UtcNow),
                        new BreweryMemberBackupDto(Guid.NewGuid().ToString(), nonexistentEmail, "Nobody", BreweryRole.Viewer, DateTime.UtcNow)
                    ])
            ]
        };

        var envelope = BuildEnvelope(craftedPayload);

        // Even when explicitly requesting RestoreMembershipsIfAvailable, unconsented users must NOT be auto-enrolled
        var restoreRes = await attackerClient.PostAsJsonAsync("/api/v1/backup/restore",
            new ExecuteRestoreRequest(
                envelope,
                new RestoreOptionsDto(
                    ConflictResolution: RestoreConflictResolutionMode.MergeOverwriteExisting,
                    CollaboratorRestore: CollaboratorRestoreMode.RestoreMembershipsIfAvailable)));

        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var restoreResult = (await restoreRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;

        restoreResult.Succeeded.Should().BeTrue();
        restoreResult.Summary.CollaboratorsRestored.Should().Be(0);

        // Both real and non-existent emails must be treated identically (pending invite only -> no user enumeration oracle)
        restoreResult.GeneratedInvites.Should().Contain(i =>
            string.Equals(i.InvitedEmail, victimEmail, StringComparison.OrdinalIgnoreCase));
        restoreResult.GeneratedInvites.Should().Contain(i =>
            string.Equals(i.InvitedEmail, nonexistentEmail, StringComparison.OrdinalIgnoreCase));

        // Verify victim was NOT silently added to the attacker's setup members
        var membersRes = await attackerClient.GetAsync($"/api/v1/brewery-setups/{attackerSetupId}/members");
        membersRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var members = (await membersRes.Content.ReadFromJsonAsync<ApiResponse<List<BreweryMemberDto>>>())!.Data!;
        members.Should().NotContain(m => m.UserId == victimUser.Id);
    }

    [Fact]
    public async Task BackupRestore_CollaboratorRestore_PreservesAlreadyExistingMembersWithoutDuplicateInvites()
    {
        var (ownerClient, _, _) = await CreateAuthenticatedUserAsync("Club Owner");
        var (memberClient, memberUser, memberEmail) = await CreateAuthenticatedUserAsync("Consented Member");

        var setups = (await (await ownerClient.GetAsync("/api/v1/brewery-setups"))
            .Content.ReadFromJsonAsync<ApiResponse<List<BrewerySetupDto>>>())!.Data!;
        var setupId = setups[0].Id;

        var inviteRes = await ownerClient.PostAsJsonAsync($"/api/v1/brewery-setups/{setupId}/invites",
            new InviteMemberRequest(memberEmail, BreweryRole.Brewer));
        inviteRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var invite = (await inviteRes.Content.ReadFromJsonAsync<ApiResponse<BreweryInviteDto>>())!.Data!;

        var acceptRes = await memberClient.PostAsJsonAsync("/api/v1/brewery-setups/invites/accept",
            new AcceptInviteRequest(invite.InviteCode));
        acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var exportRes = await ownerClient.PostAsJsonAsync("/api/v1/backup/export", new ExportBackupRequest());
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = (await exportRes.Content.ReadFromJsonAsync<ApiResponse<BrewYouBackupEnvelope>>())!.Data!;

        var restoreRes = await ownerClient.PostAsJsonAsync("/api/v1/backup/restore",
            new ExecuteRestoreRequest(
                envelope,
                new RestoreOptionsDto(
                    ConflictResolution: RestoreConflictResolutionMode.MergeOverwriteExisting,
                    CollaboratorRestore: CollaboratorRestoreMode.RestoreMembershipsIfAvailable)));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await restoreRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;
        result.Summary.CollaboratorsRestored.Should().Be(1);
        result.GeneratedInvites.Should().NotContain(i =>
            string.Equals(i.InvitedEmail, memberEmail, StringComparison.OrdinalIgnoreCase));

        var membersRes = await ownerClient.GetAsync($"/api/v1/brewery-setups/{setupId}/members");
        var members = (await membersRes.Content.ReadFromJsonAsync<ApiResponse<List<BreweryMemberDto>>>())!.Data!;
        members.Should().Contain(m => m.UserId == memberUser.Id);
    }

    [Fact]
    public void RestoreOptionsDto_DefaultCollaboratorRestoreMode_IsCreateInvitesOnly()
    {
        var defaults = new RestoreOptionsDto();
        defaults.CollaboratorRestore.Should().Be(CollaboratorRestoreMode.CreateInvitesOnly);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("localhost")]
    [InlineData("mqtt.localhost")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.10")]
    [InlineData("192.168.1.50")]
    [InlineData("fc00::1")]
    [InlineData("169.254.169.254")]
    [InlineData("metadata.google.internal")]
    public async Task MqttConnectivityChecker_DefaultConfig_BlocksLoopbackPrivateAndMetadataHosts(string host)
    {
        var checker = new MqttConnectivityChecker();
        var addresses = await checker.ResolveValidatedAddressesAsync(host);
        addresses.Should().BeEmpty();

        var canConnect = await checker.CanConnectAsync(host, 1883, TimeSpan.FromMilliseconds(200));
        canConnect.Should().BeFalse();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.50")]
    [InlineData("169.254.169.254")]
    public async Task MqttConnectivityChecker_DefaultConfig_BlocksDnsRebindingToForbiddenIp(string rebindingIp)
    {
        var checker = new MqttConnectivityChecker(
            options: Options.Create(new NetworkSecurityOptions { AllowPrivateNetworks = false, AllowLoopback = false }),
            dnsResolver: (_, _) => Task.FromResult(new[] { IPAddress.Parse(rebindingIp) }));

        var addresses = await checker.ResolveValidatedAddressesAsync("rebind.attacker.example");
        addresses.Should().BeEmpty();
    }

    [Fact]
    public async Task MqttConnectivityChecker_AllowPrivateNetworksEnabled_AllowsPrivateButStillBlocksLoopbackAndMetadata()
    {
        var checker = new MqttConnectivityChecker(
            options: Options.Create(new NetworkSecurityOptions { AllowPrivateNetworks = true, AllowLoopback = false }));

        (await checker.ResolveValidatedAddressesAsync("192.168.1.50")).Should().ContainSingle(ip => ip.Equals(IPAddress.Parse("192.168.1.50")));
        (await checker.ResolveValidatedAddressesAsync("127.0.0.1")).Should().BeEmpty();
        (await checker.ResolveValidatedAddressesAsync("::1")).Should().BeEmpty();
        (await checker.ResolveValidatedAddressesAsync("localhost")).Should().BeEmpty();
        (await checker.ResolveValidatedAddressesAsync("169.254.169.254")).Should().BeEmpty();
    }

    [Fact]
    public async Task MqttConnectivityChecker_AllowLoopbackEnabled_AllowsLoopbackButStillBlocksMetadata()
    {
        var checker = new MqttConnectivityChecker(
            options: Options.Create(new NetworkSecurityOptions { AllowPrivateNetworks = false, AllowLoopback = true }));

        (await checker.ResolveValidatedAddressesAsync("127.0.0.1")).Should().ContainSingle(ip => ip.Equals(IPAddress.Loopback));
        (await checker.ResolveValidatedAddressesAsync("169.254.169.254")).Should().BeEmpty();
    }

    [Fact]
    public void MqttService_BuildClientOptions_EnablesTlsForPort8883AndPinsIpWithOriginalTargetHost()
    {
        var options = MqttService.BuildClientOptions(
            originalHost: "broker.brewyou.example",
            pinnedAddress: IPAddress.Parse("93.184.216.34"),
            port: 8883,
            username: "brewer",
            plaintextPassword: "secret",
            certificatePem: null);

        var tcpOptions = options.ChannelOptions as MQTTnet.MqttClientTcpOptions;
        tcpOptions.Should().NotBeNull();
        tcpOptions!.RemoteEndpoint.Should().BeOfType<DnsEndPoint>();
        ((DnsEndPoint)tcpOptions.RemoteEndpoint!).Host.Should().Be("93.184.216.34");
        ((DnsEndPoint)tcpOptions.RemoteEndpoint!).Port.Should().Be(8883);

        tcpOptions.TlsOptions.UseTls.Should().BeTrue();
        tcpOptions.TlsOptions.TargetHost.Should().Be("broker.brewyou.example");
        tcpOptions.TlsOptions.AllowUntrustedCertificates.Should().BeFalse();
        tcpOptions.TlsOptions.IgnoreCertificateChainErrors.Should().BeFalse();
    }

    [Fact]
    public void MqttService_BuildClientOptions_InvalidPemCertificate_FailsClosed()
    {
        var act = () => MqttService.BuildClientOptions(
            originalHost: "broker.brewyou.example",
            pinnedAddress: IPAddress.Parse("93.184.216.34"),
            port: 1883,
            username: "brewer",
            plaintextPassword: "secret",
            certificatePem: "-----BEGIN CERTIFICATE-----\nINVALID\n-----END CERTIFICATE-----");

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void MqttService_ValidateServerCertificateWithCustomRoots_VerifiesChainAndHostnameStrictly()
    {
        using var trustedCaKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=BrewYou Test Root CA", trustedCaKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var trustedCa = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=mqtt.brewyou.example", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("mqtt.brewyou.example");
        leafRequest.CertificateExtensions.Add(sanBuilder.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        using var trustedLeaf = leafRequest.Create(trustedCa, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(7), RandomNumberGenerator.GetBytes(16));

        using var untrustedCaKey = RSA.Create(2048);
        var untrustedCaReq = new CertificateRequest("CN=Rogue CA", untrustedCaKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        untrustedCaReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var untrustedCa = untrustedCaReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        using var rogueLeaf = leafRequest.Create(untrustedCa, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(7), RandomNumberGenerator.GetBytes(16));

        var customRoots = new X509Certificate2Collection();
        customRoots.ImportFromPem(trustedCa.ExportCertificatePem());

        // Matching CA + matching hostname -> valid
        MqttService.ValidateServerCertificateWithCustomRoots(trustedLeaf, null, System.Net.Security.SslPolicyErrors.None, "mqtt.brewyou.example", customRoots)
            .Should().BeTrue();

        // Matching CA + wrong hostname -> rejected
        MqttService.ValidateServerCertificateWithCustomRoots(trustedLeaf, null, System.Net.Security.SslPolicyErrors.None, "wrong-host.example", customRoots)
            .Should().BeFalse();

        // Untrusted CA + matching hostname -> rejected
        MqttService.ValidateServerCertificateWithCustomRoots(rogueLeaf, null, System.Net.Security.SslPolicyErrors.None, "mqtt.brewyou.example", customRoots)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("brewlink/super-secret-topic-key/gw-01/equipment/aabbccddeeff/telemetry", "brewlink/***/gw-01/equipment/aabbccddeeff/telemetry")]
    [InlineData("brewlink/discovery/gw-01", "brewlink/discovery/gw-01")]
    [InlineData("brewyou/equipment/kettle/temp", "brewyou/equipment/kettle/temp")]
    public void MqttService_RedactTopicForLogging_MasksBrewLinkSecretTopicKey(string rawTopic, string expectedSafeTopic)
    {
        MqttService.RedactTopicForLogging(rawTopic).Should().Be(expectedSafeTopic);
    }

    [Fact]
    public async Task BackupRestore_SanitizesMaliciousUserSettings_BlocksJavascriptUrlsAndInvalidFields()
    {
        var (client, originalUser, _) = await CreateAuthenticatedUserAsync("Safe Brewer");

        var maliciousPayload = new BackupPayload
        {
            UserSettings = new UserSettingsBackupDto(
                OriginalUserId: originalUser.Id,
                Email: originalUser.Email,
                DisplayName: "<script>alert(1)</script>",
                PreferredLanguage: "en",
                MqttHost: "bad host\r\nwith-crlf",
                MqttPort: 1883,
                MqttUsername: "user\0injected",
                MqttCertificate: "NOT_A_VALID_PEM",
                MqttTopicPrefix: "brewyou/#",
                AppBaseUrl: "javascript:alert(document.domain)//",
                WebhookBaseUrl: "data:text/html,<script>alert(1)</script>")
        };

        var envelope = BuildEnvelope(maliciousPayload);
        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore",
            new ExecuteRestoreRequest(envelope, new RestoreOptionsDto(RestoreUserSettings: true)));

        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var restoreResult = (await restoreRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;
        restoreResult.Succeeded.Should().BeTrue();
        restoreResult.Warnings.Should().NotBeEmpty();

        var meRes = await client.GetAsync("/api/v1/auth/me");
        var me = (await meRes.Content.ReadFromJsonAsync<ApiResponse<UserDto>>())!.Data!;

        me.DisplayName.Should().Be("Safe Brewer");
        me.Preferences.AppBaseUrl.Should().BeNull();
        me.Preferences.WebhookBaseUrl.Should().BeNull();
        me.Preferences.MqttHost.Should().BeNull();
        me.Preferences.MqttUsername.Should().BeNull();
        me.Preferences.MqttCertificate.Should().BeNull();
        me.Preferences.MqttTopicPrefix.Should().Be("brewyou/equipment");
    }

    [Fact]
    public async Task BackupRestore_RejectsMaliciousRecipeLabelPayload_DisguisedAsSvgOrHtml()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Label Security Brewer");
        var maliciousSvgBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>"));

        var maliciousPayload = new BackupPayload
        {
            Recipes =
            [
                new RecipeBackupDto(
                    Id: Guid.NewGuid(),
                    Name: "SVG Attack IPA",
                    Description: "Test recipe",
                    Label: new RecipeLabelBackupDto(
                        ContentType: "image/svg+xml",
                        ImageBase64: maliciousSvgBase64,
                        WidthPx: 512,
                        HeightPx: 512,
                        Sha256: "fakehash",
                        OriginalFileName: "../../evil<script>.svg"))
            ]
        };

        var envelope = BuildEnvelope(maliciousPayload);
        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore",
            new ExecuteRestoreRequest(envelope, new RestoreOptionsDto()));

        restoreRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await restoreRes.Content.ReadFromJsonAsync<ApiResponse<RestoreResultDto>>())!.Data!;
        result.Succeeded.Should().BeTrue();
        result.Summary.RecipesCreated.Should().Be(1);
        result.Warnings.Should().Contain(w => w.Contains("Skipped invalid or unsupported recipe label image", StringComparison.OrdinalIgnoreCase));

        var recipesRes = await client.GetAsync("/api/v1/recipes?scope=mine");
        recipesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var recipes = (await recipesRes.Content.ReadFromJsonAsync<ApiResponse<List<RecipeSummaryDto>>>())!.Data!;
        var restoredRecipe = recipes.Should().ContainSingle(r => r.Name == "SVG Attack IPA").Subject;
        restoredRecipe.HasCustomLabel.Should().BeFalse();
    }

    [Fact]
    public void BackupSecretProtector_RestoreConfigJsonFromBackup_StripsRawPlaintextPasswordAndRejectsOversizedConfig()
    {
        var rawWithPlaintextPassword = "{\"host\":\"broker.example.com\",\"password\":\"plaintext-leak\",\"topic\":\"brew/temp\"}";
        var (restored, failed) = BackupSecretProtector.RestoreConfigJsonFromBackup(rawWithPlaintextPassword, null, null);

        failed.Should().BeFalse();
        restored.Should().NotBeNull();
        restored.Should().NotContain("plaintext-leak");
        restored.Should().NotContain("\"password\"");

        var oversized = new string('a', 4001);
        var (oversizedResult, _) = BackupSecretProtector.RestoreConfigJsonFromBackup(oversized, null, null);
        oversizedResult.Should().BeNull();
    }

    [Fact]
    public async Task BackupPreviewAndRestore_ExcessiveKdfIterations_ReturnsBadRequest()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Kdf Limit Brewer");
        var payload = new BackupPayload();
        var baseEnvelope = BuildEnvelope(payload);
        var dosEnvelope = baseEnvelope with
        {
            Encryption = new BackupEncryptionMetadataDto(
                SecretsEncrypted: true,
                Iterations: 2_000_000,
                SaltBase64: BackupSecretProtector.GenerateSaltBase64())
        };

        var previewRes = await client.PostAsJsonAsync("/api/v1/backup/preview", new PreviewRestoreRequest(dosEnvelope));
        previewRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(dosEnvelope));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BackupRestore_ExcessiveCollectionCounts_ReturnsInvalidPayload()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Limit Brewer");
        var tooManySetups = Enumerable.Range(0, 105)
            .Select(i => new BrewerySetupBackupDto(Guid.NewGuid(), $"Setup {i}", null, i == 0))
            .ToList();

        var envelope = BuildEnvelope(new BackupPayload { OwnedBrewerySetups = tooManySetups });

        var restoreRes = await client.PostAsJsonAsync("/api/v1/backup/restore", new ExecuteRestoreRequest(envelope));
        restoreRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await restoreRes.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("INVALID_BACKUP_PAYLOAD");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("zz")]
    [InlineData("en-US-too-long-locale")]
    public async Task UpdateUserLanguage_InvalidLocaleCode_ReturnsBadRequest(string invalidLang)
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Locale Guard Brewer");

        var res = await client.PutAsJsonAsync("/api/v1/auth/me/language", new UpdateLanguageRequest(invalidLang));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await res.Content.ReadFromJsonAsync<ApiResponse>();
        body!.Error!.Code.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Register_PasswordShorterThan8Chars_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var email = $"shortpwd_{Guid.NewGuid():N}@brewyou.test";

        var res = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "123456", "Short Pwd"));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RefreshToken_StoredAsSha256HashInDatabaseAndOmittedFromJson()
    {
        var client = _factory.CreateClient();
        var email = $"hashedrt_{Guid.NewGuid():N}@brewyou.test";

        var res = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "StrongPassword123!", "Hash Brewer"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await res.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        auth.RefreshToken.Should().BeEmpty();

        var rawCookie = res.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("refreshToken=", StringComparison.Ordinal))
            .Split(';')[0]["refreshToken=".Length..];
        var rawToken = Uri.UnescapeDataString(rawCookie);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var dbUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == auth.User.Id);

        dbUser.RefreshToken.Should().NotBeNullOrEmpty();
        dbUser.RefreshToken.Should().NotBe(rawToken);
        dbUser.RefreshToken.Should().Be(AuthService.HashRefreshToken(rawToken));
    }

    [Fact]
    public async Task ChangePassword_ValidCurrentPassword_UpdatesPasswordAndRevokesPreviousRefreshCookie()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"chgpwd_{Guid.NewGuid():N}@brewyou.test";

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "OldPassword123!", "Pwd Brewer"));
        regRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await regRes.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
        auth.User.HasPassword.Should().BeTrue();
        var oldCookiePair = regRes.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("refreshToken=", StringComparison.Ordinal))
            .Split(';')[0];

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        // Wrong current password must fail
        var badRes = await client.PutAsJsonAsync("/api/v1/auth/me/password", new ChangePasswordRequest("WrongPassword!", "NewPassword123!"));
        badRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Correct current password succeeds and rotates refresh cookie
        var okRes = await client.PutAsJsonAsync("/api/v1/auth/me/password", new ChangePasswordRequest("OldPassword123!", "NewPassword456!"));
        okRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Old refresh cookie must be rejected
        using var staleRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new RefreshTokenRequest(null))
        };
        staleRefresh.Headers.Add("Cookie", oldCookiePair);
        var staleRes = await client.SendAsync(staleRefresh);
        staleRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Old password login fails; new password login succeeds
        var oldLoginRes = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "OldPassword123!"));
        oldLoginRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newLoginRes = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "NewPassword456!"));
        newLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateUserPreferences_ClearMqttPassword_RemovesStoredPassword()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Mqtt Clear Brewer");

        var setRes = await client.PutAsJsonAsync("/api/v1/auth/me/preferences", new UpdateUserPreferencesRequest(
            MqttHost: "broker.brewyou.example",
            MqttPort: 8883,
            MqttUsername: "brewer",
            MqttPassword: "SuperSecretMqttPassword"));
        setRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterSet = (await setRes.Content.ReadFromJsonAsync<ApiResponse<UserDto>>())!.Data!;
        afterSet.Preferences.HasMqttPassword.Should().BeTrue();

        var clearRes = await client.PutAsJsonAsync("/api/v1/auth/me/preferences", new UpdateUserPreferencesRequest(
            ClearMqttPassword: true));
        clearRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterClear = (await clearRes.Content.ReadFromJsonAsync<ApiResponse<UserDto>>())!.Data!;
        afterClear.Preferences.HasMqttPassword.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateUserPreferences_ClearMqttSettings_ClearsBrokerConfigurationAndPreservesOtherPreferences()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Mqtt Disconnect Brewer");

        var setRes = await client.PutAsJsonAsync("/api/v1/auth/me/preferences", new UpdateUserPreferencesRequest(
            VolumeUnit: VolumeUnit.Gallons,
            MqttHost: "broker.brewyou.example",
            MqttPort: 8883,
            MqttUsername: "brewer",
            MqttPassword: "SuperSecretMqttPassword",
            MqttCertificate: "-----BEGIN CERTIFICATE-----\nMIIBtest\n-----END CERTIFICATE-----",
            MqttTopicPrefix: "cellar/sensors",
            AppBaseUrl: "https://brew.example.com"));
        setRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var clearRes = await client.PutAsJsonAsync("/api/v1/auth/me/preferences", new UpdateUserPreferencesRequest(
            ClearMqttSettings: true));
        clearRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterClear = (await clearRes.Content.ReadFromJsonAsync<ApiResponse<UserDto>>())!.Data!;

        afterClear.Preferences.MqttHost.Should().BeNull();
        afterClear.Preferences.MqttPort.Should().BeNull();
        afterClear.Preferences.MqttUsername.Should().BeNull();
        afterClear.Preferences.HasMqttPassword.Should().BeFalse();
        afterClear.Preferences.MqttCertificate.Should().BeNull();
        afterClear.Preferences.MqttTopicPrefix.Should().Be("brewyou/equipment");
        afterClear.Preferences.VolumeUnit.Should().Be(VolumeUnit.Gallons);
        afterClear.Preferences.AppBaseUrl.Should().Be("https://brew.example.com");

        var statusRes = await client.GetAsync("/api/v1/auth/me/mqtt-status");
        statusRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = (await statusRes.Content.ReadFromJsonAsync<ApiResponse<MqttStatusResult>>())!.Data!;
        status.Configured.Should().BeFalse();
        status.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task ApiResponse_IncludesStrictContentSecurityPolicyHeader()
    {
        var (client, _, _) = await CreateAuthenticatedUserAsync("Csp Brewer");
        var res = await client.GetAsync("/api/v1/auth/me");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Headers.Should().ContainKey("Content-Security-Policy");
        res.Headers.GetValues("Content-Security-Policy").First()
            .Should().Contain("default-src 'none'")
            .And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public void BackupSecretProtector_UnprotectServerSecret_FailsClosedOnCorruptedCiphertext()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = provider.CreateProtector("BrewYou.Test");

        var result = BackupSecretProtector.UnprotectServerSecret("corrupted-non-dp-ciphertext", protector);
        result.Should().BeNull();
    }
}