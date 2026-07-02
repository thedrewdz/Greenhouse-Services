namespace Greenhouse.Core.Onboarding;

/// <summary>
/// Application-layer port for Edge Unit onboarding over the provisioning transport. This is the
/// only onboarding-transport contract application use cases may depend on. It expresses
/// operations in Greenhouse domain terms and exposes no BLE-specific types or GATT UUIDs; the
/// concrete adapter (<c>Greenhouse.Bluetooth</c>) owns all transport mechanics.
/// </summary>
public interface IEdgeUnitProvisioningTransport
{
    /// <summary>
    /// Scans for Edge Units currently advertising in Provisioning Mode, for up to
    /// <paramref name="timeout"/>. Scanning starts only when this is called — never at process startup.
    /// </summary>
    Task<IReadOnlyList<ProvisionableUnit>> ScanForProvisionableUnitsAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers <paramref name="payload"/> to the unit identified by <paramref name="deviceId"/>
    /// and returns the unit's provisioning result.
    /// </summary>
    Task<ProvisioningResult> ProvisionUnitAsync(
        string deviceId,
        ProvisioningPayload payload,
        CancellationToken cancellationToken = default);
}
