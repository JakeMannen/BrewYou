using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Json;

namespace BrewYou.ApiService.Services;

public class MqttService : IMqttService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMqttConnectivityChecker _connectivityChecker;
    private readonly IDataProtector _protector;
    private readonly ILogger<MqttService> _logger;
    private IMqttClient? _mqttClient;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly HashSet<string> _subscribedTopics = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConnected => _mqttClient?.IsConnected ?? false;
    public string? ConnectedHost { get; private set; }
    public int? ConnectedPort { get; private set; }
    public string? ConnectedUserId { get; private set; }
    public DateTime? LastConnectedAt { get; private set; }
    public string? LastError { get; private set; }

    public MqttService(
        IServiceProvider serviceProvider,
        IMqttConnectivityChecker connectivityChecker,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<MqttService> logger)
    {
        _serviceProvider = serviceProvider;
        _connectivityChecker = connectivityChecker;
        _protector = dataProtectionProvider.CreateProtector("BrewYou.MqttCredentials");
        _logger = logger;
    }

    public MqttStatusInfo GetStatus(string? host = null, int? port = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return new MqttStatusInfo(
                Configured: !string.IsNullOrWhiteSpace(ConnectedHost),
                Connected: IsConnected,
                Host: ConnectedHost,
                Port: ConnectedPort,
                Error: LastError,
                LastConnectedAt: LastConnectedAt
            );
        }

        var isSameHost = string.Equals(host.Trim(), ConnectedHost, StringComparison.OrdinalIgnoreCase);
        var isSamePort = port == null || port == ConnectedPort;

        if (isSameHost && isSamePort && IsConnected)
        {
            return new MqttStatusInfo(
                Configured: true,
                Connected: true,
                Host: ConnectedHost,
                Port: ConnectedPort,
                LastConnectedAt: LastConnectedAt
            );
        }

        return new MqttStatusInfo(
            Configured: true,
            Connected: false,
            Host: host,
            Port: port,
            Error: isSameHost && isSamePort ? LastError : null
        );
    }

    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                return true;
            }

            // Look up active MQTT configurations from ApplicationUser or Equipment
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

            var userWithMqtt = await db.Users
                .AsNoTracking()
                .Where(u => !string.IsNullOrEmpty(u.MqttHost))
                .FirstOrDefaultAsync(cancellationToken);

            if (userWithMqtt == null || string.IsNullOrWhiteSpace(userWithMqtt.MqttHost))
            {
                return false;
            }

            var host = userWithMqtt.MqttHost.Trim();
            var port = userWithMqtt.MqttPort ?? 1883;

            // SSRF and Reachability pre-check
            var reachable = await _connectivityChecker.CanConnectAsync(host, port, TimeSpan.FromSeconds(3), cancellationToken);
            if (!reachable)
            {
                LastError = $"Broker {host}:{port} is unreachable or forbidden.";
                _logger.LogWarning("MQTT reachability check failed for {Host}:{Port}", host, port);
                return false;
            }

            if (_mqttClient == null)
            {
                var factory = new MqttClientFactory();
                _mqttClient = factory.CreateMqttClient();
                _mqttClient.ApplicationMessageReceivedAsync += HandleApplicationMessageReceivedAsync;
                _mqttClient.DisconnectedAsync += OnClientDisconnectedAsync;
            }

            var clientOptionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(host, port)
                .WithClientId($"BrewYou-{Guid.NewGuid():N}"[..23])
                .WithCleanSession(true)
                .WithTimeout(TimeSpan.FromSeconds(5))
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

            if (!string.IsNullOrWhiteSpace(userWithMqtt.MqttUsername))
            {
                string? plaintextPassword = null;
                if (!string.IsNullOrWhiteSpace(userWithMqtt.MqttPassword))
                {
                    try
                    {
                        plaintextPassword = _protector.Unprotect(userWithMqtt.MqttPassword);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decrypt stored MQTT password for user {UserId}", userWithMqtt.Id);
                    }
                }

                clientOptionsBuilder.WithCredentials(userWithMqtt.MqttUsername, plaintextPassword);
            }

            var clientOptions = clientOptionsBuilder.Build();

            _logger.LogInformation("Connecting persistent MQTT client to {Host}:{Port}...", host, port);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(5));

            var result = await _mqttClient.ConnectAsync(clientOptions, connectCts.Token);
            if (_mqttClient.IsConnected)
            {
                ConnectedHost = host;
                ConnectedPort = port;
                ConnectedUserId = userWithMqtt.Id;
                LastConnectedAt = DateTime.UtcNow;
                LastError = null;
                _logger.LogInformation("Persistent MQTT client successfully connected to {Host}:{Port}", host, port);

                await RefreshSubscriptionsAsync(cancellationToken);
                return true;
            }

            LastError = $"Connection failed with result code: {result.ResultCode}";
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "Failed to connect persistent MQTT client");
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private Task OnClientDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger.LogWarning("MQTT client disconnected from {Host}:{Port}. Reason: {Reason}",
            ConnectedHost, ConnectedPort, e.Reason);
        _subscribedTopics.Clear();
        ConnectedUserId = null;
        return Task.CompletedTask;
    }

    public async Task RefreshSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _mqttClient == null)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

        var mqttEquipment = await db.Equipment
            .AsNoTracking()
            .Where(e => (ConnectedUserId == null || e.UserId == ConnectedUserId) &&
                        e.ConnectionType == EquipmentConnectionType.Mqtt &&
                        !string.IsNullOrEmpty(e.ConnectionConfigJson))
            .ToListAsync(cancellationToken);

        var targetTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var eq in mqttEquipment)
        {
            var topic = ExtractTopic(eq.ConnectionConfigJson);
            if (!string.IsNullOrWhiteSpace(topic))
            {
                targetTopics.Add(topic);
            }
        }

        foreach (var topic in targetTopics)
        {
            if (!_subscribedTopics.Contains(topic))
            {
                try
                {
                    await _mqttClient.SubscribeAsync(topic, cancellationToken: cancellationToken);
                    _subscribedTopics.Add(topic);
                    _logger.LogInformation("Subscribed to MQTT equipment topic: {Topic}", topic);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to subscribe to MQTT topic {Topic}", topic);
                }
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync(cancellationToken: cancellationToken);
                _subscribedTopics.Clear();
                ConnectedUserId = null;
                _logger.LogInformation("MQTT client disconnected cleanly.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting MQTT client.");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payloadBytes = e.ApplicationMessage.Payload;
        if (payloadBytes.IsEmpty || payloadBytes.Length == 0 || payloadBytes.Length > 16384)
        {
            return;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes.ToArray());
        _logger.LogDebug("Received MQTT message on topic {Topic}: {Payload}", topic, payload);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

            var matchingEquipment = await db.Equipment
                .Where(eq => (ConnectedUserId == null || eq.UserId == ConnectedUserId) &&
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
            _logger.LogError(ex, "Error processing incoming MQTT message on {Topic}", topic);
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
        _mqttClient?.Dispose();
        _connectionLock.Dispose();
    }
}