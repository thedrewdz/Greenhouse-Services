using Greenhouse.Core.EdgeUnits;
using Greenhouse.Core.Messaging;

namespace Greenhouse.Runtime.HostedServices;

/// <summary>
/// Registers the <c>gh/heartbeat</c> subscription for the life of the daemon and routes every
/// message to <see cref="ProcessHeartbeat"/>.
/// </summary>
/// <remarks>
/// Subscribing at startup rather than from a request handler is what makes heartbeat handling a
/// cross-cutting message stream instead of an onboarding-specific service: registration, drift
/// detection, and liveness all run whether or not anyone is onboarding a unit. The subscription
/// survives broker reconnects, which the messaging service re-applies.
/// </remarks>
internal sealed class HeartbeatSubscriptionService : IHostedService
{
    private readonly IMessagingService _messaging;
    private readonly ProcessHeartbeat _processHeartbeat;

    private CancellationTokenSource? _lifetime;

    public HeartbeatSubscriptionService(IMessagingService messaging, ProcessHeartbeat processHeartbeat)
    {
        _messaging = messaging;
        _processHeartbeat = processHeartbeat;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime = new CancellationTokenSource();
        var token = _lifetime.Token;

        _messaging.Subscribe(EdgeUnitTopics.Heartbeat, envelope => _processHeartbeat.HandleAsync(envelope, token));
        return Task.CompletedTask;
    }

    /// <remarks>
    /// Unsubscribes before cancelling so no further handler starts, then cancels the ones already
    /// running. The token source is deliberately not disposed: handlers are dispatched
    /// fire-and-forget by the messaging service, so there is nothing to await, and disposing it out
    /// from under an in-flight handler would surface as an <see cref="ObjectDisposedException"/>
    /// during shutdown. It holds no timer, so leaving it to the GC costs nothing.
    /// </remarks>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _messaging.Unsubscribe(EdgeUnitTopics.Heartbeat);
        _lifetime?.Cancel();
        return Task.CompletedTask;
    }
}
