using Greenhouse.Core.Onboarding;

namespace Greenhouse.Bluetooth.Tests;

/// <summary>
/// Enforces the BLE layering boundary (#18, #19, #21): the low-level transport seam and its
/// implementations are internal to <c>Greenhouse.Bluetooth</c>, so no other assembly can reference
/// them. Only the application port <see cref="IEdgeUnitProvisioningTransport"/> (owned by Core) is
/// public. These tests fail if a transport type is accidentally promoted to public.
/// </summary>
public class BleVisibilityTests
{
    [Theory]
    [InlineData(typeof(IBleTransport))]
    [InlineData(typeof(BleScanFilter))]
    [InlineData(typeof(BleDeviceInfo))]
    [InlineData(typeof(BlueZBleTransport))]
    [InlineData(typeof(BleEdgeUnitProvisioningAdapter))]
    public void Transport_types_are_not_public(Type type)
    {
        Assert.False(type.IsPublic, $"{type.Name} must remain internal to Greenhouse.Bluetooth.");
    }

    [Fact]
    public void Only_the_application_port_is_public_and_it_lives_in_core()
    {
        Assert.True(typeof(IEdgeUnitProvisioningTransport).IsPublic);
        Assert.Equal("Greenhouse.Core", typeof(IEdgeUnitProvisioningTransport).Assembly.GetName().Name);
    }
}
