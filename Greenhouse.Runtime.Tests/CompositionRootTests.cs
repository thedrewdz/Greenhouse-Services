using Greenhouse.Bluetooth;
using Greenhouse.Core.Messaging;
using Greenhouse.Core.Networking;
using Greenhouse.Core.Onboarding;
using Greenhouse.Mqtt;
using Greenhouse.Network;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Greenhouse.Runtime.Tests;

/// <summary>
/// Verifies the infrastructure registrations the runtime composition root depends on (#22, #24):
/// the DI graph resolves, MQTT is a single instance shared between <see cref="IMessagingService"/>
/// and the hosted service, and MQTT options bind from configuration including env-var overrides.
/// The registrations mirror <c>Program.cs</c>; only host startup (which would open a broker
/// connection) is omitted.
/// </summary>
public class CompositionRootTests
{
    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<MqttOptions>(configuration.GetSection(MqttOptions.SectionName));
        services.AddGreenhouseMqtt();
        services.AddGreenhouseBluetooth();
        services.AddGreenhouseNetwork();
        return services.BuildServiceProvider();
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    [Fact]
    public void Messaging_service_and_hosted_service_are_the_same_singleton()
    {
        using var provider = BuildProvider(EmptyConfig());

        var messaging = provider.GetRequiredService<IMessagingService>();
        var hosted = provider.GetServices<IHostedService>();

        Assert.Contains(hosted, h => ReferenceEquals(h, messaging));
    }

    [Fact]
    public void Messaging_service_is_a_singleton()
    {
        using var provider = BuildProvider(EmptyConfig());

        var first = provider.GetRequiredService<IMessagingService>();
        var second = provider.GetRequiredService<IMessagingService>();

        Assert.Same(first, second);
    }

    [Fact]
    public void Application_ports_resolve_from_the_container()
    {
        using var provider = BuildProvider(EmptyConfig());

        Assert.NotNull(provider.GetService<IEdgeUnitProvisioningTransport>());
        Assert.NotNull(provider.GetService<INetworkConnector>());
    }

    [Fact]
    public void MqttOptions_bind_from_the_Mqtt_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Host"] = "broker.local",
                ["Mqtt:Port"] = "8883",
                ["Mqtt:ClientId"] = "unit-under-test",
            })
            .Build();

        using var provider = BuildProvider(configuration);
        var options = provider.GetRequiredService<IOptions<MqttOptions>>().Value;

        Assert.Equal("broker.local", options.Host);
        Assert.Equal(8883, options.Port);
        Assert.Equal("unit-under-test", options.ClientId);
    }

    [Fact]
    public void Environment_variable_overrides_the_Mqtt_host()
    {
        const string variable = "MQTT__HOST";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "env-broker");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Mqtt:Host"] = "appsettings-broker" })
                .AddEnvironmentVariables()
                .Build();

            using var provider = BuildProvider(configuration);
            var options = provider.GetRequiredService<IOptions<MqttOptions>>().Value;

            Assert.Equal("env-broker", options.Host);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
