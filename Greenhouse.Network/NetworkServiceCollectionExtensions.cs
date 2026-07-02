using Greenhouse.Core.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Greenhouse.Network;

/// <summary>
/// Registration extension for the OS network connector. Called from the host composition
/// root; keeps the concrete adapter and its process runner internal to this project.
/// </summary>
public static class NetworkServiceCollectionExtensions
{
    public static IServiceCollection AddGreenhouseNetwork(this IServiceCollection services)
    {
        services.AddSingleton<INmcliCommandRunner, ProcessNmcliCommandRunner>();
        services.AddSingleton<INetworkConnector, NmcliNetworkAdapter>();
        return services;
    }
}
