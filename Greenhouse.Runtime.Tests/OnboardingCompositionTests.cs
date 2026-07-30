using Greenhouse.Api.Hubs;
using Greenhouse.Bluetooth;
using Greenhouse.Core.Configuration;
using Greenhouse.Core.EdgeUnits;
using Greenhouse.Core.Messaging;
using Greenhouse.Core.Onboarding;
using Greenhouse.Mqtt;
using Greenhouse.Network;
using Greenhouse.Runtime.HostedServices;
using Greenhouse.Storage;
using Greenhouse.Storage.Repositories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Greenhouse.Runtime.Tests;

/// <summary>
/// Verifies the Edge Unit onboarding half of the composition root. The registrations mirror
/// <c>Program.cs</c>; only host startup (which would open a broker connection and a BLE session)
/// is omitted.
/// </summary>
/// <remarks>
/// The important property under test is lifetime correctness: the onboarding workflow, the
/// configuration publisher, and the heartbeat handler are singletons that run outside any
/// request, so every dependency they take must resolve from the root provider. A scoped
/// repository would fail here rather than at runtime on the Pi.
/// </remarks>
public class OnboardingCompositionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public OnboardingCompositionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<GreenhouseDbContext>()
            .UseSqlite(_connection)
            .Options;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        // SignalR's connection manager takes the host lifetime, which only a running host
        // provides; the stub keeps this container to the registrations under test.
        services.AddSingleton<IHostApplicationLifetime>(new StubHostApplicationLifetime());

        services.AddSingleton(new GreenhouseDatabase(dbOptions));
        services.AddSingleton<IMainConfigRepository, MainConfigRepository>();
        services.AddSingleton<IWifiCredentialsRepository, WifiCredentialsRepository>();
        services.AddSingleton<IEdgeUnitRepository, EdgeUnitRepository>();
        services.AddSingleton<IOnboardingSessionRepository, OnboardingSessionRepository>();

        services.Configure<MqttOptions>(
            new ConfigurationBuilder().AddInMemoryCollection().Build().GetSection(MqttOptions.SectionName));
        services.AddGreenhouseMqtt();
        services.AddGreenhouseBluetooth();
        services.AddGreenhouseNetwork();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(OnboardingTimeouts.Default);
        services.AddSingleton<IOnboardingNotifier, SignalROnboardingNotifier>();
        services.AddSingleton<IOnboardingWorkflow, OnboardingWorkflow>();

        services.AddSingleton(ConfigurationPublishPolicy.Default);
        services.AddSingleton<EdgeUnitConfigurationPublisher>();
        services.AddSingleton<IEdgeUnitConfigurationPublisher>(
            provider => provider.GetRequiredService<EdgeUnitConfigurationPublisher>());
        services.AddSingleton<ProcessHeartbeat>();

        services.AddHostedService<HeartbeatSubscriptionService>();
        services.AddHostedService<EdgeUnitConfigurationService>();

        services.AddTransient<UpdateEdgeUnitMapping>();

        // Validate on build so a scoped dependency captured by a singleton fails the test rather
        // than surviving to runtime.
        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    [Fact]
    public void The_onboarding_graph_resolves_from_the_root_provider()
    {
        Assert.NotNull(_provider.GetService<IOnboardingWorkflow>());
        Assert.NotNull(_provider.GetService<IEdgeUnitRepository>());
        Assert.NotNull(_provider.GetService<IOnboardingSessionRepository>());
        Assert.NotNull(_provider.GetService<IEdgeUnitConfigurationPublisher>());
        Assert.NotNull(_provider.GetService<ProcessHeartbeat>());
        Assert.NotNull(_provider.GetService<UpdateEdgeUnitMapping>());
    }

    [Fact]
    public void The_onboarding_workflow_is_a_single_shared_session_owner()
    {
        Assert.Same(
            _provider.GetRequiredService<IOnboardingWorkflow>(),
            _provider.GetRequiredService<IOnboardingWorkflow>());
    }

    [Fact]
    public void The_configuration_publisher_port_and_pump_are_the_same_instance()
    {
        Assert.Same(
            _provider.GetRequiredService<IEdgeUnitConfigurationPublisher>(),
            _provider.GetRequiredService<EdgeUnitConfigurationPublisher>());
    }

    [Fact]
    public void The_onboarding_notifier_is_the_SignalR_hub_adapter()
    {
        Assert.IsType<SignalROnboardingNotifier>(_provider.GetRequiredService<IOnboardingNotifier>());
        Assert.NotNull(_provider.GetService<IHubContext<OnboardingHub>>());
    }

    [Fact]
    public void The_heartbeat_and_configuration_subscriptions_are_hosted_services()
    {
        var hosted = _provider.GetServices<IHostedService>().ToArray();

        Assert.Contains(hosted, h => h is HeartbeatSubscriptionService);
        Assert.Contains(hosted, h => h is EdgeUnitConfigurationService);
    }

    [Fact]
    public async Task Starting_the_heartbeat_service_subscribes_to_the_canonical_topic()
    {
        var messaging = new RecordingMessagingService();
        var service = new HeartbeatSubscriptionService(
            messaging,
            new ProcessHeartbeat(
                _provider.GetRequiredService<IEdgeUnitRepository>(),
                _provider.GetRequiredService<IOnboardingWorkflow>(),
                _provider.GetRequiredService<ILogger<ProcessHeartbeat>>()));

        await service.StartAsync(CancellationToken.None);

        Assert.Contains("gh/heartbeat", messaging.Subscribed);

        await service.StopAsync(CancellationToken.None);
        Assert.Contains("gh/heartbeat", messaging.Unsubscribed);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private sealed class StubHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private sealed class RecordingMessagingService : IMessagingService
    {
        public List<string> Subscribed { get; } = new();

        public List<string> Unsubscribed { get; } = new();

        public Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Subscribe(string topicPattern, Func<MessageEnvelope, Task> handler) =>
            Subscribed.Add(topicPattern);

        public void Unsubscribe(string topicPattern) => Unsubscribed.Add(topicPattern);
    }
}
