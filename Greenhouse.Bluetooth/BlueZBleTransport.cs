using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Greenhouse.Bluetooth;

/// <summary>
/// <see cref="IBleTransport"/> backed by the BlueZ <c>bluetoothctl</c> subprocess on Raspberry Pi
/// Debian Bookworm — the pattern proven in
/// <c>services-old/Greenhouse.Bluetooth/BlueZEdgeUnitDiscoveryService</c>. No BLE NuGet dependency
/// is required. GATT read/write use <c>bluetoothctl</c> interactive-mode commands.
/// </summary>
/// <remarks>
/// The scan-output parser is exercised by unit tests; connect/read/write require BLE hardware and
/// are validated on-device (see issue #19 acceptance criteria). Every operation is best-effort and
/// surfaces failures as exceptions so the adapter above can clean up and map to a result.
/// </remarks>
internal sealed class BlueZBleTransport : IBleTransport
{
    private const string Executable = "bluetoothctl";

    private readonly ILogger<BlueZBleTransport> _logger;

    public BlueZBleTransport(ILogger<BlueZBleTransport> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<BleDeviceInfo>> ScanAsync(
        BleScanFilter filter,
        CancellationToken cancellationToken)
    {
        // Scan for the lifetime of the token, or a bounded default if none is supplied.
        var duration = TimeSpan.FromSeconds(10);
        var output = await RunSessionAsync(
            async (stdin, ct) =>
            {
                await stdin.WriteLineAsync("scan on");
                await stdin.FlushAsync(ct);
                await Task.Delay(duration, ct);
                await stdin.WriteLineAsync("scan off");
                await stdin.WriteLineAsync("quit");
                await stdin.FlushAsync(ct);
            },
            cancellationToken);

        return ParseScanOutput(output, filter);
    }

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        var output = await RunSessionAsync(
            async (stdin, ct) =>
            {
                await stdin.WriteLineAsync($"connect {deviceId}");
                await stdin.FlushAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                await stdin.WriteLineAsync("quit");
                await stdin.FlushAsync(ct);
            },
            cancellationToken);

        if (!output.Contains("Connection successful", StringComparison.OrdinalIgnoreCase))
        {
            throw new BleTransportException($"Failed to establish a GATT connection to '{deviceId}'.");
        }
    }

    public async Task WriteCharacteristicAsync(
        string deviceId,
        Guid serviceUuid,
        Guid characteristicUuid,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var hexBytes = string.Join(' ', data.Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture)));

        var output = await RunSessionAsync(
            async (stdin, ct) =>
            {
                await stdin.WriteLineAsync($"select-attribute {characteristicUuid:D}");
                await stdin.WriteLineAsync($"write \"{hexBytes}\"");
                await stdin.FlushAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                await stdin.WriteLineAsync("quit");
                await stdin.FlushAsync(ct);
            },
            cancellationToken);

        if (output.Contains("Failed to write", StringComparison.OrdinalIgnoreCase))
        {
            throw new BleTransportException($"Failed to write characteristic '{characteristicUuid}'.");
        }
    }

    public async Task<byte[]> ReadCharacteristicAsync(
        string deviceId,
        Guid serviceUuid,
        Guid characteristicUuid,
        CancellationToken cancellationToken)
    {
        var output = await RunSessionAsync(
            async (stdin, ct) =>
            {
                await stdin.WriteLineAsync($"select-attribute {characteristicUuid:D}");
                await stdin.WriteLineAsync("read");
                await stdin.FlushAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                await stdin.WriteLineAsync("quit");
                await stdin.FlushAsync(ct);
            },
            cancellationToken);

        return ParseReadValue(output);
    }

    public async Task DisconnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        await RunSessionAsync(
            async (stdin, ct) =>
            {
                await stdin.WriteLineAsync($"disconnect {deviceId}");
                await stdin.WriteLineAsync("quit");
                await stdin.FlushAsync(ct);
            },
            cancellationToken);
    }

    /// <summary>
    /// Parses <c>bluetoothctl</c> scan output into devices, applying <paramref name="filter"/>.
    /// Ordered by descending RSSI so the nearest unit sorts first.
    /// </summary>
    internal static IReadOnlyList<BleDeviceInfo> ParseScanOutput(string output, BleScanFilter filter)
    {
        var devices = new Dictionary<string, MutableDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var deviceIndex = line.IndexOf("Device ", StringComparison.Ordinal);
            if (deviceIndex < 0)
            {
                continue;
            }

            var deviceText = line[(deviceIndex + "Device ".Length)..];
            var spaceIndex = deviceText.IndexOf(' ');
            if (spaceIndex <= 0)
            {
                continue;
            }

            var address = deviceText[..spaceIndex];
            var remainder = deviceText[(spaceIndex + 1)..].Trim();

            if (!devices.TryGetValue(address, out var device))
            {
                device = new MutableDevice(address);
                devices[address] = device;
            }

            if (remainder.StartsWith("Name:", StringComparison.OrdinalIgnoreCase)
                || remainder.StartsWith("Alias:", StringComparison.OrdinalIgnoreCase))
            {
                device.Name = remainder[(remainder.IndexOf(':') + 1)..].Trim();
            }
            else if (remainder.StartsWith("RSSI:", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(remainder[(remainder.IndexOf(':') + 1)..].Trim(), out var rssi))
            {
                device.Rssi = rssi;
            }
            else if (!remainder.Contains(':'))
            {
                // Inline advertised name form: "Device <address> <name>". Attribute updates
                // (RSSI:, Connected:, ServicesResolved:, ...) all contain a colon and are ignored here.
                device.Name = remainder;
            }
        }

        return devices.Values
            .Where(device => MatchesFilter(device.Name, filter))
            .OrderByDescending(device => device.Rssi ?? int.MinValue)
            .Select(device => new BleDeviceInfo(device.Address, device.Name, device.Rssi))
            .ToArray();
    }

    private static bool MatchesFilter(string name, BleScanFilter filter) =>
        filter.NamePrefix is null
        || name.StartsWith(filter.NamePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Extracts the hex byte tokens from a <c>bluetoothctl read</c> value dump.</summary>
    internal static byte[] ParseReadValue(string output)
    {
        var bytes = new List<byte>();

        foreach (var token in output.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var hex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
            if (hex.Length == 2 && byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                bytes.Add(value);
            }
        }

        return bytes.ToArray();
    }

    private async Task<string> RunSessionAsync(
        Func<StreamWriter, CancellationToken, Task> drive,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new BleTransportException("bluetoothctl is not available on this host.", ex);
        }

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await drive(process.StandardInput, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return await outputTask;
        }
        catch (OperationCanceledException)
        {
            StopProcess(process);
            throw;
        }
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort teardown during cancellation.
        }
    }

    private sealed class MutableDevice(string address)
    {
        public string Address { get; } = address;

        public string Name { get; set; } = string.Empty;

        public int? Rssi { get; set; }
    }
}

/// <summary>Raised when a BlueZ transport operation cannot be completed.</summary>
internal sealed class BleTransportException : Exception
{
    public BleTransportException(string message)
        : base(message)
    {
    }

    public BleTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
