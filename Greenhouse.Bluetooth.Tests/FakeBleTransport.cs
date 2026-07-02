using System.Text;

namespace Greenhouse.Bluetooth.Tests;

/// <summary>
/// In-memory <see cref="IBleTransport"/> for testing <see cref="BleEdgeUnitProvisioningAdapter"/>
/// without hardware. Captures written bytes and call counts, and lets a test script scan results,
/// the status response, and a write fault.
/// </summary>
internal sealed class FakeBleTransport : IBleTransport
{
    public IReadOnlyList<BleDeviceInfo> ScanResult { get; set; } = Array.Empty<BleDeviceInfo>();

    public byte[] StatusResponse { get; set; } = Encoding.UTF8.GetBytes("{\"result\":\"success\",\"error_code\":0,\"error_message\":\"\"}");

    public bool ThrowOnWrite { get; set; }

    public byte[]? WrittenPayload { get; private set; }

    public int ConnectCount { get; private set; }

    public int DisconnectCount { get; private set; }

    public BleScanFilter? LastScanFilter { get; private set; }

    public Task<IReadOnlyList<BleDeviceInfo>> ScanAsync(BleScanFilter filter, CancellationToken cancellationToken)
    {
        LastScanFilter = filter;
        return Task.FromResult(ScanResult);
    }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        ConnectCount++;
        return Task.CompletedTask;
    }

    public Task WriteCharacteristicAsync(
        string deviceId,
        Guid serviceUuid,
        Guid characteristicUuid,
        byte[] data,
        CancellationToken cancellationToken)
    {
        if (ThrowOnWrite)
        {
            throw new InvalidOperationException("Simulated write failure.");
        }

        WrittenPayload = data;
        return Task.CompletedTask;
    }

    public Task<byte[]> ReadCharacteristicAsync(
        string deviceId,
        Guid serviceUuid,
        Guid characteristicUuid,
        CancellationToken cancellationToken) => Task.FromResult(StatusResponse);

    public Task DisconnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        DisconnectCount++;
        return Task.CompletedTask;
    }
}
