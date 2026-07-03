using Greenhouse.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Greenhouse.Mqtt;

/// <summary>
/// Registration for the MQTT messaging stack. Called from the host composition root; keeps the
/// concrete MQTT client adapter and the messaging service internal to this project.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddGreenhouseMqtt(this IServiceCollection services)
    {
        services.AddSingleton<IMqttClientAdapter, MqttNetClientAdapter>();

        // The same singleton instance is both the IMessagingService and the IHostedService, so the
        // service that connects at startup is exactly the one feature code publishes/subscribes through.
        services.AddSingleton<IMessagingService, MqttMessagingService>();
        services.AddHostedService(sp => (MqttMessagingService)sp.GetRequiredService<IMessagingService>());

        return services;
    }
}
