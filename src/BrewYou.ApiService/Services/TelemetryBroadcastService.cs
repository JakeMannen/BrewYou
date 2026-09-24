using BrewYou.ApiService.Endpoints;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace BrewYou.ApiService.Services;

public class TelemetryBroadcastService : ITelemetryBroadcastService
{
    private const int ChannelCapacity = 100;
    private const int MaxSubscribersPerBatch = 20;

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<BatchEquipmentReadingDto>>> _subscriptions = new();
    private readonly ILogger<TelemetryBroadcastService> _logger;

    public TelemetryBroadcastService(ILogger<TelemetryBroadcastService> logger)
    {
        _logger = logger;
    }

    public void BroadcastReading(BatchEquipmentReadingDto reading)
    {
        if (!_subscriptions.TryGetValue(reading.BatchId, out var batchSubs) || batchSubs.IsEmpty)
        {
            return;
        }

        foreach (var (subId, channel) in batchSubs)
        {
            if (!channel.Writer.TryWrite(reading))
            {
                _logger.LogWarning("Telemetry channel for subscription {SubId} on batch {BatchId} dropped reading due to full buffer.", subId, reading.BatchId);
            }
        }
    }

    public TelemetrySubscription? Subscribe(Guid batchId)
    {
        var reader = Subscribe(batchId, out var subscriptionId);
        if (reader == null)
        {
            return null;
        }

        return new TelemetrySubscription(subscriptionId, reader, () => Unsubscribe(batchId, subscriptionId));
    }

    public ChannelReader<BatchEquipmentReadingDto>? Subscribe(Guid batchId, out Guid subscriptionId)
    {
        subscriptionId = Guid.Empty;
        var batchSubs = _subscriptions.GetOrAdd(batchId, _ => new ConcurrentDictionary<Guid, Channel<BatchEquipmentReadingDto>>());

        if (batchSubs.Count >= MaxSubscribersPerBatch)
        {
            _logger.LogWarning("Batch {BatchId} reached max active telemetry subscribers ({Max}).", batchId, MaxSubscribersPerBatch);
            return null;
        }

        var subId = Guid.NewGuid();
        var channel = Channel.CreateBounded<BatchEquipmentReadingDto>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        if (!batchSubs.TryAdd(subId, channel))
        {
            return null;
        }

        subscriptionId = subId;
        _logger.LogDebug("Client subscribed to telemetry stream for batch {BatchId} (SubId: {SubId}).", batchId, subId);
        return channel.Reader;
    }

    public bool Unsubscribe(Guid batchId, Guid subscriptionId)
    {
        if (!_subscriptions.TryGetValue(batchId, out var batchSubs))
        {
            return false;
        }

        if (batchSubs.TryRemove(subscriptionId, out var channel))
        {
            channel.Writer.TryComplete();

            if (batchSubs.IsEmpty)
            {
                ((ICollection<KeyValuePair<Guid, ConcurrentDictionary<Guid, Channel<BatchEquipmentReadingDto>>>>)_subscriptions)
                    .Remove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, Channel<BatchEquipmentReadingDto>>>(batchId, batchSubs));

                // Guard against race condition if a new subscription was added concurrently
                if (!batchSubs.IsEmpty)
                {
                    _subscriptions.TryAdd(batchId, batchSubs);
                }
            }

            _logger.LogDebug("Client unsubscribed from telemetry stream for batch {BatchId} (SubId: {SubId}).", batchId, subscriptionId);
            return true;
        }

        return false;
    }

    public int GetActiveSubscriberCount(Guid batchId)
    {
        return _subscriptions.TryGetValue(batchId, out var subs) ? subs.Count : 0;
    }

    private const int MaxSubscribersPerUser = 20;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<EquipmentTelemetryUpdateDto>>> _userEquipmentSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    public void BroadcastEquipmentReading(string userId, EquipmentTelemetryUpdateDto update)
    {
        if (string.IsNullOrEmpty(userId) || !_userEquipmentSubscriptions.TryGetValue(userId, out var userSubs) || userSubs.IsEmpty)
        {
            return;
        }

        foreach (var (subId, channel) in userSubs)
        {
            if (!channel.Writer.TryWrite(update))
            {
                _logger.LogWarning("Equipment telemetry channel for subscription {SubId} on user {UserId} dropped update due to full buffer.", subId, userId);
            }
        }
    }

    public EquipmentTelemetrySubscription? SubscribeEquipment(string userId)
    {
        var reader = SubscribeEquipment(userId, out var subscriptionId);
        if (reader == null)
        {
            return null;
        }

        return new EquipmentTelemetrySubscription(subscriptionId, reader, () => UnsubscribeEquipment(userId, subscriptionId));
    }

    public ChannelReader<EquipmentTelemetryUpdateDto>? SubscribeEquipment(string userId, out Guid subscriptionId)
    {
        subscriptionId = Guid.Empty;
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var userSubs = _userEquipmentSubscriptions.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<EquipmentTelemetryUpdateDto>>());

        if (userSubs.Count >= MaxSubscribersPerUser)
        {
            _logger.LogWarning("User {UserId} reached max active equipment telemetry subscribers ({Max}).", userId, MaxSubscribersPerUser);
            return null;
        }

        var subId = Guid.NewGuid();
        var channel = Channel.CreateBounded<EquipmentTelemetryUpdateDto>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        if (!userSubs.TryAdd(subId, channel))
        {
            return null;
        }

        subscriptionId = subId;
        _logger.LogDebug("Client subscribed to equipment telemetry stream for user {UserId} (SubId: {SubId}).", userId, subId);
        return channel.Reader;
    }

    public bool UnsubscribeEquipment(string userId, Guid subscriptionId)
    {
        if (string.IsNullOrEmpty(userId) || !_userEquipmentSubscriptions.TryGetValue(userId, out var userSubs))
        {
            return false;
        }

        if (userSubs.TryRemove(subscriptionId, out var channel))
        {
            channel.Writer.TryComplete();

            if (userSubs.IsEmpty)
            {
                ((ICollection<KeyValuePair<string, ConcurrentDictionary<Guid, Channel<EquipmentTelemetryUpdateDto>>>>)_userEquipmentSubscriptions)
                    .Remove(new KeyValuePair<string, ConcurrentDictionary<Guid, Channel<EquipmentTelemetryUpdateDto>>>(userId, userSubs));

                if (!userSubs.IsEmpty)
                {
                    _userEquipmentSubscriptions.TryAdd(userId, userSubs);
                }
            }

            _logger.LogDebug("Client unsubscribed from equipment telemetry stream for user {UserId} (SubId: {SubId}).", userId, subscriptionId);
            return true;
        }

        return false;
    }

    public int GetActiveEquipmentSubscriberCount(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return 0;
        }

        return _userEquipmentSubscriptions.TryGetValue(userId, out var subs) ? subs.Count : 0;
    }
}