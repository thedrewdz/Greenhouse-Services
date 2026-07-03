using System.Collections.Concurrent;
using Greenhouse.Core.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Greenhouse.Mqtt;

/// <summary>
/// MQTT-backed <see cref="IMessagingService"/>, run as a singleton <see cref="IHostedService"/>
/// for the life of the process. It owns subscription bookkeeping, bounded-backoff reconnection,
/// and thread-pool dispatch of inbound messages to matching handlers. All MQTT library types are
/// confined to the injected <see cref="IMqttClientAdapter"/>.
/// </summary>
internal sealed class MqttMessagingService : IMessagingService, IHostedService
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly IMqttClientAdapter _client;
    private readonly ILogger<MqttMessagingService> _logger;
    private readonly ConcurrentDictionary<string, Func<MessageEnvelope, Task>> _subscriptions = new();
    private readonly SemaphoreSlim _reconnectRequested = new(0);

    private CancellationTokenSource? _lifetimeCts;
    private Task? _connectionLoop;

    public MqttMessagingService(IMqttClientAdapter client, ILogger<MqttMessagingService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default) =>
        _client.PublishAsync(topic, payload, cancellationToken);

    public void Subscribe(string topicPattern, Func<MessageEnvelope, Task> handler)
    {
        _subscriptions[topicPattern] = handler;

        // Apply immediately when already connected; otherwise it is applied on (re)connect.
        if (_client.IsConnected)
        {
            _ = ApplySubscriptionAsync(topicPattern);
        }
    }

    public void Unsubscribe(string topicPattern)
    {
        if (_subscriptions.TryRemove(topicPattern, out _) && _client.IsConnected)
        {
            _ = SafeAsync(
                () => _client.UnsubscribeAsync(topicPattern, _lifetimeCts?.Token ?? CancellationToken.None),
                $"unsubscribe from '{topicPattern}'");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetimeCts = new CancellationTokenSource();
        _client.MessageReceived += OnMessageReceivedAsync;
        _client.Disconnected += OnDisconnectedAsync;

        // Connect in the background so host startup never blocks on (or crashes over) broker
        // availability. The loop retries with bounded backoff for the life of the process.
        _connectionLoop = Task.Run(() => RunConnectionLoopAsync(_lifetimeCts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _client.MessageReceived -= OnMessageReceivedAsync;
        _client.Disconnected -= OnDisconnectedAsync;

        if (_lifetimeCts is not null)
        {
            _lifetimeCts.Cancel();
        }

        if (_connectionLoop is not null)
        {
            try
            {
                await _connectionLoop;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        await SafeAsync(() => _client.DisconnectAsync(cancellationToken), "disconnect");
        _lifetimeCts?.Dispose();
    }

    private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await EnsureConnectedAsync(cancellationToken);

            try
            {
                // Sleep until a disconnect is signalled (or we are shutting down).
                await _reconnectRequested.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        var delay = InitialRetryDelay;

        while (!cancellationToken.IsCancellationRequested && !_client.IsConnected)
        {
            try
            {
                await _client.ConnectAsync(cancellationToken);
                await ApplyAllSubscriptionsAsync(cancellationToken);
                _logger.LogInformation("MQTT connected; {Count} subscription(s) applied.", _subscriptions.Count);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Concise message only — a full stack trace on every retry would flood the Pi's
                // limited storage while the broker is unreachable.
                _logger.LogWarning("MQTT connect failed ({Reason}); retrying in {Seconds}s.", ex.Message, delay.TotalSeconds);
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // Bounded exponential backoff.
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    private async Task ApplyAllSubscriptionsAsync(CancellationToken cancellationToken)
    {
        foreach (var topicPattern in _subscriptions.Keys)
        {
            await _client.SubscribeAsync(topicPattern, cancellationToken);
        }
    }

    private Task ApplySubscriptionAsync(string topicPattern) =>
        SafeAsync(
            () => _client.SubscribeAsync(topicPattern, _lifetimeCts?.Token ?? CancellationToken.None),
            $"subscribe to '{topicPattern}'");

    private Task OnMessageReceivedAsync(MqttInboundMessage message)
    {
        var envelope = new MessageEnvelope(message.Topic, message.Payload, DateTime.UtcNow);

        foreach (var subscription in _subscriptions)
        {
            if (!MqttTopicMatcher.Matches(subscription.Key, message.Topic))
            {
                continue;
            }

            var handler = subscription.Value;
            // Dispatch on the thread pool so one slow/faulting handler cannot stall the receive path.
            _ = Task.Run(() => InvokeHandlerAsync(handler, envelope));
        }

        return Task.CompletedTask;
    }

    private async Task InvokeHandlerAsync(Func<MessageEnvelope, Task> handler, MessageEnvelope envelope)
    {
        try
        {
            await handler(envelope);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT subscriber handler threw for topic '{Topic}'.", envelope.Topic);
        }
    }

    private Task OnDisconnectedAsync()
    {
        // Debug, not warning: this also fires for each failed connect attempt while the broker is
        // down. The actionable retry line is logged by EnsureConnectedAsync.
        _logger.LogDebug("MQTT connection dropped; scheduling reconnect.");
        // Wake the connection loop to reconnect and re-apply subscriptions.
        if (_reconnectRequested.CurrentCount == 0)
        {
            _reconnectRequested.Release();
        }

        return Task.CompletedTask;
    }

    private async Task SafeAsync(Func<Task> action, string description)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT operation failed: {Description}.", description);
        }
    }
}
