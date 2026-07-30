using Greenhouse.Core.EdgeUnits;
using Greenhouse.Core.Messaging;

namespace Greenhouse.Runtime.HostedServices;

/// <summary>
/// Runs the runtime configuration publish pump and routes Edge Unit acknowledgements back into
/// it, for the life of the daemon.
/// </summary>
/// <remarks>
/// The subscription covers the whole <c>ghcfg/#</c> namespace because a broker may echo the Main
/// Unit's own writes back; the publisher discards everything that is not an acknowledgement it
/// is waiting on.
/// </remarks>
internal sealed class EdgeUnitConfigurationService : IHostedService
{
    private readonly IMessagingService _messaging;
    private readonly EdgeUnitConfigurationPublisher _publisher;

    private CancellationTokenSource? _lifetime;
    private Task? _pump;

    public EdgeUnitConfigurationService(IMessagingService messaging, EdgeUnitConfigurationPublisher publisher)
    {
        _messaging = messaging;
        _publisher = publisher;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime = new CancellationTokenSource();

        _messaging.Subscribe(EdgeUnitTopics.ConfigurationRoot, _publisher.HandleAcknowledgementAsync);
        _pump = _publisher.RunAsync(_lifetime.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _messaging.Unsubscribe(EdgeUnitTopics.ConfigurationRoot);
        _lifetime?.Cancel();

        if (_pump is not null)
        {
            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _lifetime?.Dispose();
    }
}
