using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services.Telemetry;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace BrewYou.ApiService.Services;

public class MqttService : IMqttService, IDisposable
{
    private const int DefaultTlsMqttPort = 8883;

    private sealed class UserMqttConnection : IDisposable
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public IMqttClient? Client { get; set; }
        public HashSet<string> SubscribedTopics { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? ConfigFingerprint { get; set; }
        public DateTime? LastConnectedAt { get; set; }
        public string? LastError { get; set; }
        public bool IsConnected => Client?.IsConnected ?? false;

        public void Dispose()
        {
            Client?.Dispose();
            Lock.Dispose();
        }
    }

    private readonly IServiceProvider _serviceProvider;
    private readonly IMqttConnectivityChecker _connectivityChecker;
    private readonly IDataProtector _protector;
    private readonly IDataProtector _brewLinkProtector;
    private readonly IBrewLinkDiscoveryService? _brewLinkDiscoveryService;
    private readonly ILogger<MqttService> _logger;
    private readonly ConcurrentDictionary<string, UserMqttConnection> _connections = new(StringComparer.Ordinal);

    public bool IsConnected => _connections.Values.Any(c => c.IsConnected);
    public string? ConnectedHost => _connections.Values.FirstOrDefault(c => c.IsConnected)?.Host;
    public int? ConnectedPort => _connections.Values.FirstOrDefault(c => c.IsConnected)?.Port;
    public DateTime? LastConnectedAt => _connections.Values
        .Where(c => c.LastConnectedAt.HasValue)
        .Select(c => c.LastConnectedAt)
        .Max();

    public MqttService(
        IServiceProvider serviceProvider,
        IMqttConnectivityChecker connectivityChecker,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<MqttService> logger,
        IBrewLinkDiscoveryService? brewLinkDiscoveryService = null)
    {
        _serviceProvider = serviceProvider;
        _connectivityChecker = connectivityChecker;
        _protector = dataProtectionProvider.CreateProtector("BrewYou.MqttCredentials");
        _brewLinkProtector = dataProtectionProvider.CreateProtector("BrewYou.BrewLinkCredentials");
        _brewLinkDiscoveryService = brewLinkDiscoveryService;
        _logger = logger;
    }

    public MqttStatusInfo GetStatus(string userId, string? host = null, int? port = null)
    {
        if (string.IsNullOrWhiteSpace(userId) || !_connections.TryGetValue(userId, out var conn))
        {
            return new MqttStatusInfo(
                Configured: !string.IsNullOrWhiteSpace(host),
                Connected: false,
                Host: host,
                Port: port
            );
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return new MqttStatusInfo(
                Configured: !string.IsNullOrWhiteSpace(conn.Host),
                Connected: conn.IsConnected,
                Host: conn.Host,
                Port: conn.Port,
                Error: conn.LastError,
                LastConnectedAt: conn.LastConnectedAt
            );
        }

        var isSameHost = string.Equals(host.Trim(), conn.Host, StringComparison.OrdinalIgnoreCase);
        var isSamePort = port == null || port == conn.Port;

        if (isSameHost && isSamePort && conn.IsConnected)
        {
            return new MqttStatusInfo(
                Configured: true,
                Connected: true,
                Host: conn.Host,
                Port: conn.Port,
                LastConnectedAt: conn.LastConnectedAt
            );
        }

        return new MqttStatusInfo(
            Configured: true,
            Connected: false,
            Host: host,
            Port: port,
            Error: isSameHost && isSamePort ? conn.LastError : null
        );
    }

    public MqttStatusInfo GetStatus(string? host = null, int? port = null)
    {
        // Aggregate summary only (never returns cross-user status for arbitrary host/port queries)
        if (!string.IsNullOrWhiteSpace(host))
        {
            return new MqttStatusInfo(Configured: true, Connected: false, Host: host, Port: port);
        }

        var connected = _connections.Values.FirstOrDefault(c => c.IsConnected);
        if (connected != null)
        {
            return new MqttStatusInfo(
                Configured: true,
                Connected: true,
                Host: connected.Host,
                Port: connected.Port,
                LastConnectedAt: connected.LastConnectedAt
            );
        }

        return new MqttStatusInfo(
            Configured: !_connections.IsEmpty,
            Connected: false
        );
    }

    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

        var configuredUsers = await db.Users
            .AsNoTracking()
            .Where(u => !string.IsNullOrEmpty(u.MqttHost))
            .ToListAsync(cancellationToken);

        var activeUserIds = configuredUsers.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);

        // Disconnect any stale connections for users who removed their MQTT host
        foreach (var staleUserId in _connections.Keys.Where(id => !activeUserIds.Contains(id)).ToList())
        {
            await DisconnectUserAsync(staleUserId, cancellationToken);
        }

        if (configuredUsers.Count == 0)
        {
            return false;
        }

        var anyConnected = false;
        foreach (var user in configuredUsers)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await EnsureUserConnectedAsync(user, cancellationToken))
            {
                anyConnected = true;
            }
        }

        return anyConnected;
    }

    private async Task<bool> EnsureUserConnectedAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.MqttHost))
        {
            return false;
        }

        var host = user.MqttHost.Trim();
        var port = user.MqttPort ?? 1883;
        var fingerprint = ComputeConfigFingerprint(host, port, user.MqttUsername, user.MqttPassword, user.MqttCertificate);
        var conn = _connections.GetOrAdd(user.Id, _ => new UserMqttConnection());

        await conn.Lock.WaitAsync(cancellationToken);
        try
        {
            if (conn.IsConnected && string.Equals(conn.ConfigFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return true;
            }

            // Reconfigure if user changed host/port/credentials/certificate
            if (conn.Client != null && conn.Client.IsConnected)
            {
                try
                {
                    await conn.Client.DisconnectAsync(cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disconnecting previous MQTT session for user {UserId}", user.Id);
                }
                conn.SubscribedTopics.Clear();
            }

            // SSRF & DNS-rebinding defense: resolve and validate once, then pin the validated IP
            var validatedAddresses = await _connectivityChecker.ResolveValidatedAddressesAsync(host, cancellationToken);
            if (validatedAddresses.Length == 0)
            {
                conn.Host = host;
                conn.Port = port;
                conn.LastError = $"Broker {host}:{port} is unreachable or forbidden.";
                _logger.LogWarning("MQTT host validation failed for user {UserId} ({Host}:{Port})", user.Id, host, port);
                return false;
            }

            string? plaintextPassword = null;
            if (!string.IsNullOrWhiteSpace(user.MqttPassword))
            {
                try
                {
                    plaintextPassword = _protector.Unprotect(user.MqttPassword);
                }
                catch (Exception ex)
                {
                    conn.Host = host;
                    conn.Port = port;
                    conn.LastError = "Stored MQTT credentials could not be decrypted. Please re-save your MQTT password.";
                    _logger.LogWarning(ex, "Failed to decrypt stored MQTT password for user {UserId}", user.Id);
                    return false;
                }
            }

            if (conn.Client == null)
            {
                var factory = new MqttClientFactory();
                var client = factory.CreateMqttClient();
                var ownerUserId = user.Id;
                client.ApplicationMessageReceivedAsync += e => HandleApplicationMessageReceivedAsync(ownerUserId, e);
                client.DisconnectedAsync += e => OnClientDisconnectedAsync(ownerUserId, conn, e);
                conn.Client = client;
            }

            Exception? lastException = null;
            foreach (var resolvedIp in validatedAddresses)
            {
                MqttClientOptions clientOptions;
                try
                {
                    clientOptions = BuildClientOptions(
                        originalHost: host,
                        pinnedAddress: resolvedIp,
                        port: port,
                        username: user.MqttUsername,
                        plaintextPassword: plaintextPassword,
                        certificatePem: user.MqttCertificate);
                }
                catch (Exception ex)
                {
                    conn.Host = host;
                    conn.Port = port;
                    conn.LastError = $"Invalid MQTT TLS configuration: {ex.Message}";
                    _logger.LogWarning(ex, "Invalid MQTT TLS certificate configuration for user {UserId}", user.Id);
                    return false;
                }

                try
                {
                    _logger.LogInformation("Connecting persistent MQTT client for user {UserId} to {Host}:{Port}...", user.Id, host, port);
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(5));

                    var result = await conn.Client.ConnectAsync(clientOptions, connectCts.Token);
                    if (conn.Client.IsConnected)
                    {
                        conn.Host = host;
                        conn.Port = port;
                        conn.ConfigFingerprint = fingerprint;
                        conn.LastConnectedAt = DateTime.UtcNow;
                        conn.LastError = null;
                        _logger.LogInformation("Persistent MQTT client connected for user {UserId} to {Host}:{Port}", user.Id, host, port);

                        await RefreshUserSubscriptionsAsync(user.Id, conn, cancellationToken);
                        return true;
                    }

                    conn.LastError = $"Connection failed with result code: {result.ResultCode}";
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            conn.Host = host;
            conn.Port = port;
            if (lastException != null)
            {
                conn.LastError = lastException.Message;
                _logger.LogWarning(lastException, "Failed to connect persistent MQTT client for user {UserId}", user.Id);
            }
            return false;
        }
        finally
        {
            conn.Lock.Release();
        }
    }

    public static MqttClientOptions BuildClientOptions(
        string originalHost,
        IPAddress pinnedAddress,
        int port,
        string? username,
        string? plaintextPassword,
        string? certificatePem)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(pinnedAddress.ToString(), port)
            .WithClientId($"BrewYou-{Guid.NewGuid():N}"[..23])
            .WithCleanSession(true)
            .WithTimeout(TimeSpan.FromSeconds(5))
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrWhiteSpace(username))
        {
            builder.WithCredentials(username, plaintextPassword);
        }

        var hasCustomCert = !string.IsNullOrWhiteSpace(certificatePem);
        var useTls = hasCustomCert || port == DefaultTlsMqttPort;

        if (useTls)
        {
            X509Certificate2Collection? customRoots = null;
            if (hasCustomCert)
            {
                customRoots = new X509Certificate2Collection();
                customRoots.ImportFromPem(certificatePem!);
                if (customRoots.Count == 0)
                {
                    throw new CryptographicException("No valid X.509 certificate found in PEM data.");
                }
            }

            builder.WithTlsOptions(tls =>
            {
                tls.UseTls(true);
                tls.WithTargetHost(originalHost);
                tls.WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13);
                tls.WithAllowUntrustedCertificates(false);
                tls.WithIgnoreCertificateChainErrors(false);
                tls.WithIgnoreCertificateRevocationErrors(false);

                if (customRoots != null)
                {
                    tls.WithCertificateValidationHandler(ctx =>
                        ValidateServerCertificateWithCustomRoots(ctx.Certificate, ctx.Chain, ctx.SslPolicyErrors, originalHost, customRoots));
                }
            });
        }

        return builder.Build();
    }

    public static bool ValidateServerCertificateWithCustomRoots(
        X509Certificate? certificate,
        X509Chain? _,
        SslPolicyErrors __,
        string expectedHost,
        X509Certificate2Collection customRoots)
    {
        if (certificate == null || customRoots.Count == 0)
        {
            return false;
        }

        using var serverCert = certificate is X509Certificate2 cert2
            ? new X509Certificate2(cert2)
            : new X509Certificate2(certificate);

        if (!serverCert.MatchesHostname(expectedHost, allowWildcards: true, allowCommonName: true))
        {
            return false;
        }

        using var customChain = new X509Chain();
        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        foreach (var root in customRoots)
        {
            customChain.ChainPolicy.CustomTrustStore.Add(root);
        }

        return customChain.Build(serverCert);
    }

    public static string RedactTopicForLogging(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return string.Empty;
        }

        var segments = topic.Split('/');
        // Redact secret topicKey in brewlink/{topicKey}/{gatewayId}/...
        if (segments.Length >= 3 &&
            string.Equals(segments[0], "brewlink", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(segments[1], "discovery", StringComparison.OrdinalIgnoreCase))
        {
            segments[1] = "***";
            return string.Join('/', segments);
        }

        return topic;
    }

    private static string ComputeConfigFingerprint(string host, int port, string? username, string? encryptedPassword, string? certificate)
    {
        var input = $"{host.ToLowerInvariant()}|{port}|{username ?? ""}|{encryptedPassword ?? ""}|{certificate ?? ""}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private Task OnClientDisconnectedAsync(string userId, UserMqttConnection conn, MqttClientDisconnectedEventArgs e)
    {
        _logger.LogWarning("MQTT client disconnected for user {UserId} from {Host}:{Port}. Reason: {Reason}",
            userId, conn.Host, conn.Port, e.Reason);
        conn.SubscribedTopics.Clear();
        return Task.CompletedTask;
    }

    public async Task RefreshSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (userId, conn) in _connections)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!conn.IsConnected)
            {
                continue;
            }

            await conn.Lock.WaitAsync(cancellationToken);
            try
            {
                if (conn.IsConnected)
                {
                    await RefreshUserSubscriptionsAsync(userId, conn, cancellationToken);
                }
            }
            finally
            {
                conn.Lock.Release();
            }
        }
    }

    private async Task RefreshUserSubscriptionsAsync(string userId, UserMqttConnection conn, CancellationToken cancellationToken)
    {
        if (!conn.IsConnected || conn.Client == null)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

        var mqttEquipment = await db.Equipment
            .AsNoTracking()
            .Where(e => e.UserId == userId &&
                        e.ConnectionType == EquipmentConnectionType.Mqtt &&
                        !string.IsNullOrEmpty(e.ConnectionConfigJson))
            .ToListAsync(cancellationToken);

        var targetTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "brewlink/discovery/+"
        };

        foreach (var eq in mqttEquipment)
        {
            var topic = ExtractTopic(eq.ConnectionConfigJson);
            if (!string.IsNullOrWhiteSpace(topic))
            {
                targetTopics.Add(topic);
            }
        }

        var brewLinkGateways = await db.BrewLinkGateways
            .AsNoTracking()
            .Where(g => g.UserId == userId && !string.IsNullOrEmpty(g.MqttTopicKey))
            .ToListAsync(cancellationToken);

        foreach (var gw in brewLinkGateways)
        {
            try
            {
                var topicKey = _brewLinkProtector.Unprotect(gw.MqttTopicKey!);
                if (!string.IsNullOrWhiteSpace(topicKey))
                {
                    targetTopics.Add($"brewlink/{topicKey}/{gw.GatewayId}/equipment/+/telemetry");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unprotect MQTT topic key for BrewLink gateway {GatewayId}", gw.GatewayId);
            }
        }

        foreach (var topic in targetTopics)
        {
            if (!conn.SubscribedTopics.Contains(topic))
            {
                try
                {
                    await conn.Client.SubscribeAsync(topic, cancellationToken: cancellationToken);
                    conn.SubscribedTopics.Add(topic);
                    _logger.LogInformation("Subscribed to MQTT equipment topic for user {UserId}: {Topic}",
                        userId, RedactTopicForLogging(topic));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to subscribe to MQTT topic {Topic} for user {UserId}",
                        RedactTopicForLogging(topic), userId);
                }
            }
        }
    }

    public async Task DisconnectUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || !_connections.TryRemove(userId, out var conn))
        {
            return;
        }

        await conn.Lock.WaitAsync(cancellationToken);
        try
        {
            if (conn.Client != null && conn.Client.IsConnected)
            {
                await conn.Client.DisconnectAsync(cancellationToken: cancellationToken);
                _logger.LogInformation("MQTT client disconnected cleanly for user {UserId}.", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting MQTT client for user {UserId}.", userId);
        }
        finally
        {
            conn.Lock.Release();
            conn.Dispose();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        foreach (var userId in _connections.Keys.ToList())
        {
            await DisconnectUserAsync(userId, cancellationToken);
        }
    }

    private async Task HandleApplicationMessageReceivedAsync(string ownerUserId, MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payloadBytes = e.ApplicationMessage.Payload;
        if (payloadBytes.IsEmpty || payloadBytes.Length == 0 || payloadBytes.Length > 16384)
        {
            return;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes.ToArray());
        var safeTopic = RedactTopicForLogging(topic);
        _logger.LogDebug("Received MQTT message for user {UserId} on topic {Topic}: {Payload}", ownerUserId, safeTopic, payload);

        if (topic.StartsWith("brewlink/discovery/", StringComparison.OrdinalIgnoreCase))
        {
            _brewLinkDiscoveryService?.IngestBeaconJson(payload);
            return;
        }

        var segments = topic.Split('/');
        // brewlink / {topicKey} / {gatewayId} / equipment / {mac} / telemetry
        if (segments.Length == 6 &&
            string.Equals(segments[0], "brewlink", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[3], "equipment", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[5], "telemetry", StringComparison.OrdinalIgnoreCase))
        {
            var topicKey = segments[1];
            var gatewayHardwareId = segments[2];
            var mac = segments[4];

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
                var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

                var matchingGateways = await db.BrewLinkGateways.AsNoTracking()
                    .Where(g => g.GatewayId == gatewayHardwareId && g.UserId == ownerUserId)
                    .ToListAsync();

                BrewLinkGateway? gateway = null;
                var incomingKeyBytes = Encoding.UTF8.GetBytes(topicKey);
                foreach (var candidateGw in matchingGateways)
                {
                    if (string.IsNullOrEmpty(candidateGw.MqttTopicKey))
                    {
                        continue;
                    }

                    try
                    {
                        var unprotectedKey = _brewLinkProtector.Unprotect(candidateGw.MqttTopicKey);
                        var expectedKeyBytes = Encoding.UTF8.GetBytes(unprotectedKey);
                        if (CryptographicOperations.FixedTimeEquals(expectedKeyBytes, incomingKeyBytes))
                        {
                            gateway = candidateGw;
                            break;
                        }
                    }
                    catch
                    {
                        // Ignore unprotect failures for non-matching gateways
                    }
                }

                if (gateway != null)
                {
                    var cleanMac = BrewLinkTelemetryParser.CleanMac(mac);
                    var candidates = await db.Equipment.AsNoTracking()
                        .Include(eq => eq.BrewerySetup)
                        .Where(eq => ((eq.BrewerySetup != null && eq.BrewerySetup.UserId == gateway.UserId) ||
                                      (eq.BrewerySetup == null && eq.UserId == gateway.UserId)) &&
                                     eq.ConnectionType == EquipmentConnectionType.BrewLink &&
                                     !(eq.Type == EquipmentType.Sensor && eq.Subtype != EquipmentSubtype.BrewLink) &&
                                     !string.IsNullOrEmpty(eq.ConnectionConfigJson))
                        .ToListAsync();

                    var matchedEquipment = candidates.FirstOrDefault(eq =>
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(eq.ConnectionConfigJson!);
                            if (doc.RootElement.TryGetProperty("brewLinkGatewayId", out var gwElem))
                            {
                                var gwStr = gwElem.GetString();
                                if (!string.Equals(gwStr, gateway.Id.ToString(), StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(gwStr, gateway.GatewayId, StringComparison.OrdinalIgnoreCase))
                                {
                                    return false;
                                }

                                if (doc.RootElement.TryGetProperty("targetMac", out var tmElem) ||
                                    doc.RootElement.TryGetProperty("target_mac", out tmElem))
                                {
                                    return string.Equals(BrewLinkTelemetryParser.CleanMac(tmElem.GetString()), cleanMac, StringComparison.OrdinalIgnoreCase);
                                }
                            }
                        }
                        catch
                        {
                            // ignore json error
                        }
                        return false;
                    });

                    if (matchedEquipment != null)
                    {
                        await telemetryService.IngestMqttTelemetryAsync(matchedEquipment.Id, payload);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing incoming BrewLink MQTT telemetry on {Topic} for user {UserId}", safeTopic, ownerUserId);
            }
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

            var matchingEquipment = await db.Equipment
                .Where(eq => eq.UserId == ownerUserId &&
                             eq.ConnectionType == EquipmentConnectionType.Mqtt &&
                             !string.IsNullOrEmpty(eq.ConnectionConfigJson))
                .ToListAsync();

            foreach (var eq in matchingEquipment)
            {
                var eqTopic = ExtractTopic(eq.ConnectionConfigJson);
                if (string.Equals(eqTopic, topic, StringComparison.OrdinalIgnoreCase))
                {
                    await telemetryService.IngestMqttTelemetryAsync(eq.Id, payload);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing incoming MQTT message on {Topic} for user {UserId}", safeTopic, ownerUserId);
        }
    }

    public static string? ExtractTopic(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("topic", out var prop) ||
                doc.RootElement.TryGetProperty("Topic", out prop))
            {
                return prop.GetString();
            }
        }
        catch
        {
            // Ignore malformed json
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var conn in _connections.Values)
        {
            conn.Dispose();
        }
        _connections.Clear();
    }
}