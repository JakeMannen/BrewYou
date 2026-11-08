using BrewYou.ApiService.Data;
using BrewYou.ApiService.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BrewYou.ApiService.Services;

public class EquipmentPollingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EquipmentPollingWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(10);

    public EquipmentPollingWorker(IServiceProvider serviceProvider, ILogger<EquipmentPollingWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EquipmentPollingWorker started with check interval {Interval}", _checkInterval);
        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PollEligibleEquipmentAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in EquipmentPollingWorker iteration");
            }
        }

        _logger.LogInformation("EquipmentPollingWorker stopped");
    }

    private async Task PollEligibleEquipmentAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();
        var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
        var wsManager = _serviceProvider.GetService<IBrewLinkWebSocketManager>();

        var pollingEquipment = await db.Equipment
            .AsNoTracking()
            .Where(e => (e.ConnectionType == EquipmentConnectionType.HttpPoll ||
                         e.ConnectionType == EquipmentConnectionType.BrewLink) &&
                        !string.IsNullOrEmpty(e.ConnectionConfigJson))
            .ToListAsync(cancellationToken);

        if (pollingEquipment.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var eq in pollingEquipment)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var intervalSeconds = ExtractIntervalSeconds(eq.ConnectionConfigJson);
            var shouldPoll = eq.TemperatureUpdatedAt == null ||
                             (now - eq.TemperatureUpdatedAt.Value) >= TimeSpan.FromSeconds(intervalSeconds);

            if (eq.ConnectionType == EquipmentConnectionType.BrewLink)
            {
                var gwGuid = ExtractBrewLinkGatewayId(eq.ConnectionConfigJson);
                if (gwGuid.HasValue && wsManager?.IsGatewayConnected(gwGuid.Value) == true)
                {
                    // WebSocket stream is active; avoid redundant polling
                    continue;
                }

                if (shouldPoll && gwGuid.HasValue)
                {
                    try
                    {
                        var gateway = await db.BrewLinkGateways.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gwGuid.Value, cancellationToken);
                        if (gateway != null)
                        {
                            var tokenProtector = _serviceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("BrewYou.BrewLinkCredentials");
                            var token = tokenProtector.Unprotect(gateway.ApiToken);
                            var (isValidHost, _, normalizedHost) = await BrewLinkService.ValidateAndNormalizeHostAsync(gateway.Host, cancellationToken);
                            if (isValidHost && normalizedHost != null)
                            {
                                var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
                                var client = httpClientFactory.CreateClient("TelemetryPoller");
                                using var req = new HttpRequestMessage(HttpMethod.Get, $"{normalizedHost.TrimEnd('/')}/api/equipment");
                                req.Headers.Add("Authorization", $"Bearer {token}");
                                req.Headers.Add("X-BrewLink-Token", token);
                                using var res = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken);
                                if (res.IsSuccessStatusCode)
                                {
                                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                                    using var doc = JsonDocument.Parse(content);
                                    await telemetryService.IngestBrewLinkTelemetryAsync(eq.Id, doc.RootElement, source: "BrewLinkPoll");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to poll BrewLink fallback equipment {EquipmentId}", eq.Id);
                    }
                }
                continue;
            }

            if (shouldPoll)
            {
                _logger.LogDebug("Polling equipment {EquipmentId} ({Name}) - interval {Interval}s",
                    eq.Id, eq.Name, intervalSeconds);

                try
                {
                    await telemetryService.PollEquipmentAsync(eq.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to poll equipment {EquipmentId}", eq.Id);
                }
            }
        }
    }

    public static Guid? ExtractBrewLinkGatewayId(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("brewLinkGatewayId", out var prop) &&
                Guid.TryParse(prop.GetString(), out var id))
            {
                return id;
            }
        }
        catch
        {
            // ignore malformed
        }
        return null;
    }

    public static int ExtractIntervalSeconds(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return 60;
        }

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if ((doc.RootElement.TryGetProperty("intervalSeconds", out var prop) ||
                 doc.RootElement.TryGetProperty("IntervalSeconds", out prop) ||
                 doc.RootElement.TryGetProperty("interval", out prop)) &&
                prop.TryGetInt32(out var seconds))
            {
                return Math.Clamp(seconds, 10, 3600);
            }
        }
        catch (JsonException)
        {
            // Fallback to default
        }

        return 60;
    }
}