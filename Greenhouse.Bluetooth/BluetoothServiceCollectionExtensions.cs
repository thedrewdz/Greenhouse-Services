using Greenhouse.Core.Onboarding;
using Microsoft.Extensions.DependencyInjection;

namespace Greenhouse.Bluetooth;

/// <summary>
/// Registration for the BLE onboarding stack. Called from the host composition root; keeps
/// <see cref="IBleTransport"/> and its BlueZ implementation internal to this project. Only the
/// application port <see cref="IEdgeUnitProvisioningTransport"/> is resolvable from outside.
/// </summary>
public static class BluetoothServiceCollectionExtensions
{
    public static IServiceCollection AddGreenhouseBluetooth(this IServiceCollection services)
    {
        // Low-level transport stays internal; neither it nor its supporting types are visible above.
        services.AddSingleton<IBleTransport, BlueZBleTransport>();

        // The only BLE contract application code may resolve. No BLE scan starts at registration.
        services.AddSingleton<IEdgeUnitProvisioningTransport, BleEdgeUnitProvisioningAdapter>();

        return services;
    }
}
