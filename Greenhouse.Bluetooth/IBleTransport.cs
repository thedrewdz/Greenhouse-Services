namespace Greenhouse.Bluetooth;

/// <summary>
/// Filter applied to a BLE scan. All fields are optional; when both are set a device must match
/// both to be returned.
/// </summary>
/// <param name="NamePrefix">Advertised-name prefix to match (e.g. <c>GH-Edge-</c>).</param>
/// <param name="ServiceUuid">Advertised service UUID to match.</param>
internal sealed record BleScanFilter(string? NamePrefix = null, Guid? ServiceUuid = null);

/// <summary>A device discovered during a BLE scan.</summary>
/// <param name="DeviceId">Transport device identifier (BlueZ address).</param>
/// <param name="Name">Advertised device name.</param>
/// <param name="Rssi">Received signal strength indicator, when reported.</param>
internal sealed record BleDeviceInfo(string DeviceId, string Name, int? Rssi);

/// <summary>
/// Low-level, infrastructure-internal BLE transport seam. This is <b>not</b> an application port:
/// it must never be referenced outside <c>Greenhouse.Bluetooth</c>. GATT UUIDs never appear here —
/// they are private constants in the adapter that composes this transport.
/// </summary>
internal interface IBleTransport
{
    /// <summary>
    /// Streams advertising devices matching <paramref name="filter"/> as they are observed, for
    /// up to <paramref name="duration"/>. A device is yielded again whenever its advertised
    /// details change — the name and the RSSI usually arrive in separate advertisements.
    /// </summary>
    IAsyncEnumerable<BleDeviceInfo> ScanAsync(
        BleScanFilter filter,
        TimeSpan duration,
        CancellationToken cancellationToken);

    /// <summary>Establishes a GATT connection to <paramref name="deviceId"/>. Throws on timeout.</summary>
    Task ConnectAsync(string deviceId, CancellationToken cancellationToken);

    /// <summary>Writes <paramref name="data"/> to a characteristic on the active connection.</summary>
    Task WriteCharacteristicAsync(
        string deviceId,
        Guid serviceUuid,
        Guid characteristicUuid,
        byte[] data,
        CancellationToken cancellationToken);

    /// <summary>Reads a characteristic from the active connection.</summary>
    Task<byte[]> ReadCharacteristicAsync(
        string deviceId,
        Guid serviceUuid,
        Guid characteristicUuid,
        CancellationToken cancellationToken);

    /// <summary>Tears down the GATT connection and releases resources.</summary>
    Task DisconnectAsync(string deviceId, CancellationToken cancellationToken);
}
