using BrewYou.ApiService.Data;
using Microsoft.EntityFrameworkCore;

namespace BrewYou.ApiService.Services;

public sealed class BrewLinkWebSocketWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IBrewLinkWebSocketManager _manager;
    private readonly ILogger<BrewLinkWebSocketWorker> _logger;
    private readonly TimeSpan _reconciliationInterval = TimeSpan.FromSeconds(30);

    public BrewLinkWebSocketWorker(
        IServiceProvider serviceProvider,
        IBrewLinkWebSocketManager manager,
        ILogger<BrewLinkWebSocketWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _manager = manager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BrewLinkWebSocketWorker started with reconciliation interval {Interval}", _reconciliationInterval);

        // Initial reconciliation immediately on startup
        await ReconcileAsync(stoppingToken);

        using var timer = new PeriodicTimer(_reconciliationInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReconcileAsync(stoppingToken);
        }

        _logger.LogInformation("BrewLinkWebSocketWorker stopping; disconnecting all WebSocket streams");
        await _manager.DisconnectAllAsync(CancellationToken.None);
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BrewYouDbContext>();

            var gateways = await db.BrewLinkGateways
                .AsNoTracking()
                .ToListAsync(ct);

            await _manager.ReconcileGatewaysAsync(gateways, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconciling BrewLink WebSocket streams");
        }
    }
}