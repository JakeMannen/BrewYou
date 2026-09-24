namespace BrewYou.ApiService.Services;

public class MqttWorker : BackgroundService
{
    private readonly IMqttService _mqttService;
    private readonly ILogger<MqttWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public MqttWorker(IMqttService mqttService, ILogger<MqttWorker> logger)
    {
        _mqttService = mqttService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MqttWorker started. Maintaining persistent MQTT connectivity and telemetry subscriptions.");

        // Initial delay to allow DB and network services to settle on startup
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            await _mqttService.EnsureConnectedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial MQTT connection attempt failed. Will retry on periodic interval.");
        }

        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (!_mqttService.IsConnected)
                {
                    _logger.LogDebug("MqttWorker: MQTT client not connected. Attempting reconnection...");
                    await _mqttService.EnsureConnectedAsync(stoppingToken);
                }
                else
                {
                    await _mqttService.RefreshSubscriptionsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MqttWorker periodic check");
            }
        }

        _logger.LogInformation("MqttWorker stopping. Disconnecting MQTT client...");
        try
        {
            using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _mqttService.DisconnectAsync(shutdownCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting MQTT service on worker shutdown");
        }
    }
}