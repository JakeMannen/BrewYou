using BrewYou.ApiService.Endpoints;
using System.Threading.Channels;

namespace BrewYou.ApiService.Services;

public sealed class TelemetrySubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private int _disposed;

    public Guid SubscriptionId { get; }
    public ChannelReader<BatchEquipmentReadingDto> Reader { get; }

    internal TelemetrySubscription(Guid subscriptionId, ChannelReader<BatchEquipmentReadingDto> reader, Action unsubscribe)
    {
        SubscriptionId = subscriptionId;
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _unsubscribe();
        }
    }
}

public sealed class EquipmentTelemetrySubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private int _disposed;

    public Guid SubscriptionId { get; }
    public ChannelReader<EquipmentTelemetryUpdateDto> Reader { get; }

    internal EquipmentTelemetrySubscription(Guid subscriptionId, ChannelReader<EquipmentTelemetryUpdateDto> reader, Action unsubscribe)
    {
        SubscriptionId = subscriptionId;
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _unsubscribe();
        }
    }
}

public record EquipmentTelemetryUpdateDto(
    Guid EquipmentId,
    string EquipmentName,
    decimal TemperatureC,
    DateTime Timestamp,
    string? Source = null,
    Guid? BatchId = null,
    decimal? SpecificGravity = null,
    decimal? CurrentVolumeLiters = null,
    decimal? FillPercentage = null,
    decimal? PressureBar = null,
    decimal? BatteryPercent = null,
    decimal? BatteryVoltage = null,
    decimal? TiltDegrees = null,
    short? Rssi = null,
    string? MetricsJson = null
);

public interface ITelemetryBroadcastService
{
    void BroadcastReading(BatchEquipmentReadingDto reading);
    TelemetrySubscription? Subscribe(Guid batchId);
    ChannelReader<BatchEquipmentReadingDto>? Subscribe(Guid batchId, out Guid subscriptionId);
    bool Unsubscribe(Guid batchId, Guid subscriptionId);
    int GetActiveSubscriberCount(Guid batchId);

    void BroadcastEquipmentReading(string userId, EquipmentTelemetryUpdateDto update);
    EquipmentTelemetrySubscription? SubscribeEquipment(string userId);
    ChannelReader<EquipmentTelemetryUpdateDto>? SubscribeEquipment(string userId, out Guid subscriptionId);
    bool UnsubscribeEquipment(string userId, Guid subscriptionId);
    int GetActiveEquipmentSubscriberCount(string userId);
}