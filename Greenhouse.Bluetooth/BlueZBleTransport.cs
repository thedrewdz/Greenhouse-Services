using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
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

    public async IAsyncEnumerable<BleDeviceInfo> ScanAsync(
        BleScanFilter filter,
        TimeSpan duration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Read bluetoothctl's output line by line rather than waiting for the process to exit,
        // so a candidate reaches the operator the moment it advertises instead of after the
        // whole scan window closes.
        using var process = StartProcess();
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(duration);

        var accumulator = new ScanAccumulator();

        try
        {
            await process.StandardInput.WriteLineAsync("scan on");
            await process.StandardInput.FlushAsync();

            while (!window.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await process.StandardOutput.ReadLineAsync(window.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (line is null)
                {
                    break;
                }

                var updated = accumulator.Apply(line, filter);
                if (updated is not null)
                {
                    yield return updated;
                }
            }
        }
        finally
        {
            await StopScanAsync(process);
            StopProcess(process);
        }
    }

    /// <summary>Best-effort graceful shutdown so the adapter is not left scanning.</summary>
    private async Task StopScanAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            await process.StandardInput.WriteLineAsync("scan off");
            await process.StandardInput.WriteLineAsync("quit");
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "bluetoothctl did not shut down cleanly after scanning.");
        }
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
    /// Parses a complete <c>bluetoothctl</c> scan transcript into devices, applying
    /// <paramref name="filter"/>. Ordered by descending RSSI so the nearest unit sorts first.
    /// Shares its line handling with the streaming scan.
    /// </summary>
    internal static IReadOnlyList<BleDeviceInfo> ParseScanOutput(string output, BleScanFilter filter)
    {
        var accumulator = new ScanAccumulator();

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            accumulator.Apply(line, filter);
        }

        return accumulator.Matching(filter);
    }

    /// <summary>
    /// Incremental parser for <c>bluetoothctl</c> scan output. It holds the devices seen so far
    /// so that attribute updates arriving on later lines (RSSI in particular) are merged onto the
    /// device the name line introduced.
    /// </summary>
    private sealed class ScanAccumulator
    {
        private readonly Dictionary<string, MutableDevice> _devices = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Applies one output line. Returns the device when the line changed a matching,
        /// named device — that is, when there is something new to report — otherwise null.
        /// </summary>
        public BleDeviceInfo? Apply(string rawLine, BleScanFilter filter)
        {
            var line = rawLine.Trim();
            var deviceIndex = line.IndexOf("Device ", StringComparison.Ordinal);
            if (deviceIndex < 0)
            {
                return null;
            }

            var deviceText = line[(deviceIndex + "Device ".Length)..];
            var spaceIndex = deviceText.IndexOf(' ');
            if (spaceIndex <= 0)
            {
                return null;
            }

            var address = deviceText[..spaceIndex];
            var remainder = deviceText[(spaceIndex + 1)..].Trim();

            if (!_devices.TryGetValue(address, out var device))
            {
                device = new MutableDevice(address);
                _devices[address] = device;
            }

            var changed = false;

            if (remainder.StartsWith("Name:", StringComparison.OrdinalIgnoreCase)
                || remainder.StartsWith("Alias:", StringComparison.OrdinalIgnoreCase))
            {
                changed = device.SetName(remainder[(remainder.IndexOf(':') + 1)..].Trim());
            }
            else if (remainder.StartsWith("RSSI:", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(remainder[(remainder.IndexOf(':') + 1)..].Trim(), out var rssi))
            {
                changed = device.SetRssi(rssi);
            }
            else if (!remainder.Contains(':'))
            {
                // Inline advertised name form: "Device <address> <name>". Attribute updates
                // (RSSI:, Connected:, ServicesResolved:, ...) all contain a colon and are ignored here.
                changed = device.SetName(remainder);
            }

            return changed && MatchesFilter(device.Name, filter) ? device.ToDeviceInfo() : null;
        }

        public IReadOnlyList<BleDeviceInfo> Matching(BleScanFilter filter) =>
            _devices.Values
                .Where(device => MatchesFilter(device.Name, filter))
                .OrderByDescending(device => device.Rssi ?? int.MinValue)
                .Select(device => device.ToDeviceInfo())
                .ToArray();

        private static bool MatchesFilter(string name, BleScanFilter filter) =>
            name.Length > 0
            && (filter.NamePrefix is null
                || name.StartsWith(filter.NamePrefix, StringComparison.OrdinalIgnoreCase));
    }

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

    private static Process StartProcess()
    {
        var process = new Process
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
            process.Dispose();
            throw new BleTransportException("bluetoothctl is not available on this host.", ex);
        }

        return process;
    }

    private async Task<string> RunSessionAsync(
        Func<StreamWriter, CancellationToken, Task> drive,
        CancellationToken cancellationToken)
    {
        using var process = StartProcess();

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

        public string Name { get; private set; } = string.Empty;

        public int? Rssi { get; private set; }

        /// <summary>Returns true when the value actually changed.</summary>
        public bool SetName(string name)
        {
            if (name.Length == 0 || string.Equals(Name, name, StringComparison.Ordinal))
            {
                return false;
            }

            Name = name;
            return true;
        }

        /// <summary>Returns true when the value actually changed.</summary>
        public bool SetRssi(int rssi)
        {
            if (Rssi == rssi)
            {
                return false;
            }

            Rssi = rssi;
            return true;
        }

        public BleDeviceInfo ToDeviceInfo() => new(Address, Name, Rssi);
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
