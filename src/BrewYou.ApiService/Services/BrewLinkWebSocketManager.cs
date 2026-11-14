using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using BrewYou.ApiService.Services.Telemetry;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BrewYou.ApiService.Services;

public interface IBrewLinkWebSocketManager
{
    Task ReconcileGatewaysAsync(IEnumerable<BrewLinkGateway> gateways, CancellationToken ct = default);
    bool IsGatewayConnected(Guid gatewayId);
    Task DisconnectAllAsync(CancellationToken ct = default);
}

public sealed class BrewLinkWebSocketManager : IBrewLinkWebSocketManager, IDisposable
{
    private sealed class GatewaySession
    {
        public required BrewLinkGateway Gateway { get; set; }
        public required CancellationTokenSource Cts { get; init; }
        public ClientWebSocket? Socket { get; set; }
        public DateTime? LastFrameReceivedAt { get; set; }
        public int ConsecutiveFailures { get; set; }
        public Task? WorkerTask { get; set; }
    }

    private readonly IServiceProvider _serviceProvider;
    private readonly IDataProtector _protector;
    private readonly ILogger<BrewLinkWebSocketManager> _logger;
    private readonly ConcurrentDictionary<Guid, GatewaySession> _sessions = new();
    private bool _disposed;

    public BrewLinkWebSocketManager(
        IServiceProvider serviceProvider,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<BrewLinkWebSocketManager> logger)
    {
        _serviceProvider = serviceProvider;
        _protector = dataProtectionProvider.CreateProtector("BrewYou.BrewLinkCredentials");
        _logger = logger;
    }

    public bool IsGatewayConnected(Guid gatewayId)
    {
        return _sessions.TryGetValue(gatewayId, out var session) &&
               session.Socket?.State == WebSocketState.Open;
    }

    public Task ReconcileGatewaysAsync(IEnumerable<BrewLinkGateway> gateways, CancellationToken ct = default)
    {
        if (_disposed) return Task.CompletedTask;

        var gatewayMap = gateways.ToDictionary(g => g.Id);

        // 1. Remove obsolete or updated sessions
        foreach (var (id, session) in _sessions)
        {
            if (!gatewayMap.TryGetValue(id, out var current) ||
                !string.Equals(session.Gateway.Host, current.Host, StringComparison.OrdinalIgnoreCase))
            {
                if (_sessions.TryRemove(id, out var removed))
                {
                    _logger.LogInformation("Stopping WebSocket session for removed/updated gateway {GatewayId} ({Host})", id, removed.Gateway.Host);
                    removed.Cts.Cancel();
                    removed.Cts.Dispose();
                }
            }
        }

        // 2. Start new sessions
        foreach (var (id, gateway) in gatewayMap)
        {
            if (!_sessions.ContainsKey(id))
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var session = new GatewaySession
                {
                    Gateway = gateway,
                    Cts = cts
                };

                if (_sessions.TryAdd(id, session))
                {
                    _logger.LogInformation("Starting WebSocket session for gateway {GatewayId} ({Host})", id, gateway.Host);
                    session.WorkerTask = Task.Run(() => RunGatewayLoopAsync(session, cts.Token), cts.Token);
                }
                else
                {
                    cts.Dispose();
                }
            }
        }

        return Task.CompletedTask;
    }

    private async Task RunGatewayLoopAsync(GatewaySession session, CancellationToken stoppingToken)
    {
        var gateway = session.Gateway;

        while (!stoppingToken.IsCancellationRequested && !_disposed)
        {
            var (isValid, errorMessage, normalizedUrl) = await BrewLinkService.ValidateAndNormalizeHostAsync(gateway.Host, stoppingToken);
            if (!isValid || normalizedUrl == null || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var baseUri))
            {
                _logger.LogWarning("Invalid or unsafe host for BrewLink gateway {GatewayId}: {Host} ({Error})",
                    gateway.Id, gateway.Host, errorMessage);
                await DelayBackoffAsync(session, stoppingToken);
                continue;
            }

            string token;
            try
            {
                token = _protector.Unprotect(gateway.ApiToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unprotect API token for BrewLink gateway {GatewayId}", gateway.Id);
                await DelayBackoffAsync(session, stoppingToken);
                continue;
            }

            var wsScheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            var wsUri = new Uri($"{wsScheme}://{baseUri.Authority}/ws?token={Uri.EscapeDataString(token)}");

            ClientWebSocket? ws = null;
            try
            {
                ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                ws.Options.SetRequestHeader("Authorization", $"Bearer {token}");
                ws.Options.SetRequestHeader("X-BrewLink-Token", token);

                session.Socket = ws;

                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                    await ws.ConnectAsync(wsUri, connectCts.Token);
                }

                _logger.LogInformation("Connected WebSocket stream to BrewLink gateway {GatewayId} ({Host})", gateway.Id, gateway.Host);
                session.ConsecutiveFailures = 0;
                session.LastFrameReceivedAt = DateTime.UtcNow;

                await ReceiveFramesAsync(session, ws, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebSocket connection failed or dropped for BrewLink gateway {GatewayId} ({Host})", gateway.Id, gateway.Host);
            }
            finally
            {
                if (ws != null)
                {
                    try
                    {
                        if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                        {
                            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token);
                        }
                    }
                    catch
                    {
                        // Best effort close
                    }
                    ws.Dispose();
                    session.Socket = null;
                }
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await DelayBackoffAsync(session, stoppingToken);
            }
        }
    }

    private async Task ReceiveFramesAsync(GatewaySession session, ClientWebSocket ws, CancellationToken stoppingToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        using var ms = new MemoryStream();

        try
        {
            while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("BrewLink gateway {GatewayId} closed WebSocket connection gracefully", session.Gateway.Id);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                    if (ms.Length > 65536)
                    {
                        _logger.LogWarning("Dropping oversized WebSocket frame ({Length} bytes) from gateway {GatewayId}", ms.Length, session.Gateway.Id);
                        break;
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text || ms.Length == 0)
                {
                    continue;
                }

                session.LastFrameReceivedAt = DateTime.UtcNow;
                session.ConsecutiveFailures = 0;

                var text = Encoding.UTF8.GetString(ms.ToArray());
                await ProcessFrameAsync(session.Gateway, text);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ProcessFrameAsync(BrewLinkGateway gateway, string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (!TelemetryHelper.TryGetPropertyInsensitive(root, "event", out var eventElem))
            {
                return;
            }

            var eventName = eventElem.GetString();
            var isTelemetry = string.Equals(eventName, "telemetry", StringComparison.OrdinalIgnoreCase);
            var isConnectedEvent = string.Equals(eventName, "connected", StringComparison.OrdinalIgnoreCase);
            var isDisconnectedEvent = string.Equals(eventName, "disconnected", StringComparison.OrdinalIgnoreCase);
            var isEquipmentListEvent = string.Equals(eventName, "equipment_list", StringComparison.OrdinalIgnoreCase);

            if (!isTelemetry && !isConnectedEvent && !isDisconnectedEvent && !isEquipmentListEvent)
            {
                return;
            }

            string? frameMac = null;
            if (TelemetryHelper.TryGetPropertyInsensitive(root, "mac", out var macElem))
            {
                frameMac = BrewLinkTelemetryParser.CleanMac(macElem.GetString());
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
            var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

            var candidateEquipment = await db.Equipment
                .Where(e => e.ConnectionType == EquipmentConnectionType.BrewLink &&
                            !(e.Type == EquipmentType.Sensor && e.Subtype != EquipmentSubtype.BrewLink) &&
                            !string.IsNullOrEmpty(e.ConnectionConfigJson))
                .ToListAsync();

            if (isEquipmentListEvent)
            {
                foreach (var eq in candidateEquipment)
                {
                    if (!MatchesGateway(eq, gateway, matchMac: false, frameMac: null))
                    {
                        continue;
                    }

                    if (BrewLinkTelemetryParser.TryResolveDeviceConnectionState(root, eq, out var eqConnected))
                    {
                        await telemetryService.SetBrewLinkConnectionStateAsync(eq.Id, eqConnected, source: "BrewLinkWebSocket");
                    }
                }
                return;
            }

            var matchedEquipment = candidateEquipment.FirstOrDefault(eq => MatchesGateway(eq, gateway, matchMac: true, frameMac: frameMac));
            if (matchedEquipment == null)
            {
                return;
            }

            if (isTelemetry)
            {
                await telemetryService.IngestBrewLinkTelemetryAsync(matchedEquipment.Id, root, source: "BrewLinkWebSocket");
            }
            else
            {
                await telemetryService.SetBrewLinkConnectionStateAsync(
                    matchedEquipment.Id,
                    isConnected: isConnectedEvent,
                    source: "BrewLinkWebSocket");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON WebSocket frame from gateway {GatewayId}", gateway.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WebSocket frame from gateway {GatewayId}", gateway.Id);
        }
    }

    private static bool MatchesGateway(Equipment eq, BrewLinkGateway gateway, bool matchMac, string? frameMac)
    {
        if (eq.Subtype == EquipmentSubtype.BrewLink || string.IsNullOrWhiteSpace(eq.ConnectionConfigJson))
        {
            return false;
        }

        try
        {
            using var configDoc = JsonDocument.Parse(eq.ConnectionConfigJson);
            var configRoot = configDoc.RootElement;

            if (!configRoot.TryGetProperty("brewLinkGatewayId", out var gwElem))
            {
                return false;
            }

            var gwStr = gwElem.GetString();
            if (!string.Equals(gwStr, gateway.Id.ToString(), StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(gwStr, gateway.GatewayId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!matchMac)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(frameMac) &&
                (configRoot.TryGetProperty("targetMac", out var tmElem) ||
                 configRoot.TryGetProperty("target_mac", out tmElem)))
            {
                return string.Equals(BrewLinkTelemetryParser.CleanMac(tmElem.GetString()), frameMac, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Ignore malformed JSON in connection config
        }

        return false;
    }

    private static async Task DelayBackoffAsync(GatewaySession session, CancellationToken ct)
    {
        session.ConsecutiveFailures++;
        var delaySec = Math.Min(Math.Pow(2, Math.Min(session.ConsecutiveFailures, 6)), 60.0);
        var jitterMs = Random.Shared.Next(0, 1000);
        var totalDelay = TimeSpan.FromSeconds(delaySec) + TimeSpan.FromMilliseconds(jitterMs);

        try
        {
            await Task.Delay(totalDelay, ct);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
    }

    public async Task DisconnectAllAsync(CancellationToken ct = default)
    {
        foreach (var (id, session) in _sessions)
        {
            if (_sessions.TryRemove(id, out var removed))
            {
                removed.Cts.Cancel();
                removed.Cts.Dispose();
            }
        }

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (_, session) in _sessions)
        {
            try
            {
                session.Cts.Cancel();
                session.Cts.Dispose();
            }
            catch
            {
                // Best effort
            }
        }
        _sessions.Clear();
    }
}