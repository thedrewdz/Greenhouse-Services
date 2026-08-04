using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Greenhouse.Bluetooth;

/// <summary>
/// <see cref="IBleTransport"/> backed by the BlueZ <c>bluetoothctl</c> subprocess on Raspberry Pi
/// Debian Bookworm — the pattern proven in
/// <c>services-old/Greenhouse.Bluetooth/BlueZEdgeUnitDiscoveryService</c>. No BLE NuGet dependency
/// is required. GATT read/write use <c>bluetoothctl</c> interactive-mode commands, which live in its
/// <c>gatt</c> submenu — see <see cref="EnterGattMenuCommand"/>.
/// </summary>
/// <remarks>
/// The scan-output parser is exercised by unit tests; connect/read/write require BLE hardware and
/// are validated on-device (see issue #19 acceptance criteria). Every operation is best-effort and
/// surfaces failures as exceptions so the adapter above can clean up and map to a result.
///
/// A stand-in for <c>bluetoothctl</c> cannot tell whether a command sequence is one bluetoothctl
/// would actually accept — it answers whatever it is asked. That is how #72 stayed invisible through
/// a green suite, so the literal sequences are asserted directly and the accepted and refused
/// outputs are taken from the real binary rather than assumed.
/// </remarks>
internal sealed class BlueZBleTransport : IBleTransport
{
    private const string Executable = "bluetoothctl";

    /// <summary>Longest stderr excerpt carried in a <see cref="BleTransportException"/> message.</summary>
    private const int StderrExcerptLength = 500;

    /// <summary>How long a scan teardown may take before the subprocess is killed instead.</summary>
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// bluetoothctl keeps <c>select-attribute</c>, <c>read</c>, and <c>write</c> in its <c>gatt</c>
    /// submenu. Issued from the main menu every one of them is refused outright — "Invalid command
    /// in menu main: write" — so entering the submenu is the first step of every GATT session.
    /// Regression #72: without it, BLE provisioning could not succeed on any device.
    /// </summary>
    private const string EnterGattMenuCommand = "menu gatt";

    /// <summary>
    /// How long a GATT session waits after entering the submenu, and again after selecting the
    /// attribute. A session runs in a <em>new</em> bluetoothctl process attaching to a connection an
    /// earlier one made, so it must first receive the connected device's attribute tree from
    /// bluetoothd: until it has, <c>select-attribute</c> answers "No device connected".
    /// </summary>
    private static readonly TimeSpan DefaultGattSettleDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a GATT session waits for bluetoothd's asynchronous reply before quitting.
    /// <c>read</c> in particular dispatches immediately and prints the value later, so quitting
    /// sooner discards it.
    /// </summary>
    private static readonly TimeSpan DefaultGattReplyDelay = TimeSpan.FromSeconds(2);

    private readonly ILogger<BlueZBleTransport> _logger;
    private readonly string _executable;
    private readonly string _arguments;
    private readonly TimeSpan _gattSettleDelay;
    private readonly TimeSpan _gattReplyDelay;

    public BlueZBleTransport(ILogger<BlueZBleTransport> logger)
        : this(logger, Executable, string.Empty)
    {
    }

    /// <summary>
    /// Test seam: substitutes a stand-in for <c>bluetoothctl</c> so subprocess handling — pipe
    /// draining, exit-code reporting, and outcome detection — can be exercised on a host without
    /// BlueZ. <paramref name="gattStepDelay"/> collapses the GATT session's settle and reply waits
    /// so those tests do not pay for real device timing; production keeps the defaults.
    /// </summary>
    internal BlueZBleTransport(
        ILogger<BlueZBleTransport> logger,
        string executable,
        string arguments,
        TimeSpan? gattStepDelay = null)
    {
        _logger = logger;
        _executable = executable;
        _arguments = arguments;
        _gattSettleDelay = gattStepDelay ?? DefaultGattSettleDelay;
        _gattReplyDelay = gattStepDelay ?? DefaultGattReplyDelay;
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

        // stderr is redirected but nothing below consumes it, so drain it in the background: a
        // chatty bluetoothctl would otherwise fill the stderr pipe buffer, block on the write, and
        // stop producing the stdout lines this scan is reading. The read ends when teardown closes
        // the pipe; its content is of no use here, only the draining is.
        Forget(process.StandardError.ReadToEndAsync());

        // The window is closed by racing each read against a timer, never by cancelling the read
        // itself: a pending read on a child process pipe does not observe cancellation on Linux,
        // so a silent bluetoothctl would otherwise park here forever — past the scan duration,
        // deaf to an explicit cancel, and blocking daemon shutdown.
        var window = WindowAsync(duration, cancellationToken);
        var accumulator = new ScanAccumulator();

        try
        {
            await process.StandardInput.WriteLineAsync("scan on");
            await process.StandardInput.FlushAsync();

            while (true)
            {
                var read = process.StandardOutput.ReadLineAsync();

                if (await Task.WhenAny(read, window) != read)
                {
                    // Window elapsed or the caller cancelled. The abandoned read completes when
                    // the finally block below tears the process down and closes the pipe.
                    Forget(read);
                    break;
                }

                var line = await read;
                if (line is null)
                {
                    break;
                }

                // Same hazard class as #72 at the other end of the transport: a refused command
                // otherwise reads as a scan that simply found nothing, and the operator is told the
                // greenhouse has no Edge Units rather than that the scan never started.
                EnsureScanLineNotRefused(line);

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

    /// <summary>
    /// Completes when <paramref name="duration"/> elapses or <paramref name="cancellationToken"/>
    /// is cancelled — never faults, so it is safe to race against a read.
    /// </summary>
    private static async Task WindowAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation closes the window exactly as the timer would.
        }
    }

    /// <summary>Observes an abandoned task's outcome so it cannot surface as unobserved.</summary>
    private static void Forget(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    /// Best-effort graceful shutdown so the adapter is not left scanning. Bounded as a whole: a
    /// wedged subprocess can block the stdin write as easily as the exit wait, and the caller
    /// always follows this with a kill.
    /// </summary>
    private async Task StopScanAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        var graceful = GracefulStopAsync(process);
        if (await Task.WhenAny(graceful, Task.Delay(GracefulStopTimeout)) != graceful)
        {
            Forget(graceful);
            _logger.LogDebug("bluetoothctl did not shut down within {Timeout}; killing it.", GracefulStopTimeout);
        }
    }

    private async Task GracefulStopAsync(Process process)
    {
        try
        {
            await process.StandardInput.WriteLineAsync("scan off");
            await process.StandardInput.WriteLineAsync("quit");
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync();
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

        var output = await RunGattSessionAsync(characteristicUuid, $"write \"{hexBytes}\"", cancellationToken);

        EnsureNotRefused(output, $"write characteristic '{characteristicUuid}'");

        // A completed write is confirmed only by bluetoothctl's own dispatch acknowledgement;
        // success itself is silent. Regression #72: inferring success from the *absence* of "Failed
        // to write" reported a rejected command as a completed write, and no bytes left the Pi.
        EnsureAcknowledged(output, WriteAcknowledgement, $"write to characteristic '{characteristicUuid}'");
    }

    public async Task<byte[]> ReadCharacteristicAsync(
        string deviceId,
        Guid serviceUuid,
        Guid characteristicUuid,
        CancellationToken cancellationToken)
    {
        var output = await RunGattSessionAsync(characteristicUuid, "read", cancellationToken);

        EnsureNotRefused(output, $"read characteristic '{characteristicUuid}'");

        // Regression #72: a read that never ran parsed to zero bytes, which the adapter above
        // reported as an empty response *from the Edge Unit* — blaming the device for a Main Unit
        // fault. A read that was never dispatched is a transport failure and must say so.
        EnsureAcknowledged(output, ReadAcknowledgement, $"read of characteristic '{characteristicUuid}'");

        return ParseReadValue(output);
    }

    public async Task DisconnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        var output = await RunSessionAsync(
            async (stdin, ct) =>
            {
                await stdin.WriteLineAsync($"disconnect {deviceId}");
                await stdin.WriteLineAsync("quit");
                await stdin.FlushAsync(ct);
            },
            cancellationToken);

        // Teardown is best-effort — the caller only logs this — but a refusal must still be visible
        // rather than leaving the operator with a unit that looks disconnected and is not.
        EnsureNotRefused(output, $"disconnect '{deviceId}'");
    }

    /// <summary>
    /// Runs one bluetoothctl session through the GATT sequence and returns its stdout transcript.
    /// Read and write share it so neither can drift back out of the <c>gatt</c> submenu.
    /// </summary>
    private Task<string> RunGattSessionAsync(
        Guid characteristicUuid,
        string command,
        CancellationToken cancellationToken) =>
        RunSessionAsync(
            (stdin, ct) => DriveGattSessionAsync(
                stdin,
                characteristicUuid,
                command,
                _gattSettleDelay,
                _gattReplyDelay,
                delay => Task.Delay(delay, ct),
                ct),
            cancellationToken);

    /// <summary>
    /// Writes the literal command sequence for one GATT operation.
    /// </summary>
    /// <remarks>
    /// Extracted from the subprocess so a test can assert the exact lines. #72 was a sequence that
    /// was well-formed but issued in the wrong menu, and no bluetoothctl stand-in can catch that:
    /// a stand-in answers whatever it is asked. Only the literal sequence is evidence.
    ///
    /// No <c>back</c> follows the operation. The session quits immediately afterwards, and <c>back</c>
    /// would only dump the whole main-menu help listing into the transcript the value is parsed from.
    /// </remarks>
    internal static async Task DriveGattSessionAsync(
        TextWriter stdin,
        Guid characteristicUuid,
        string command,
        TimeSpan settleDelay,
        TimeSpan replyDelay,
        Func<TimeSpan, Task> delay,
        CancellationToken cancellationToken)
    {
        await stdin.WriteLineAsync(EnterGattMenuCommand);
        await stdin.FlushAsync(cancellationToken);
        await delay(settleDelay);

        await stdin.WriteLineAsync($"select-attribute {characteristicUuid:D}");
        await stdin.FlushAsync(cancellationToken);
        await delay(settleDelay);

        await stdin.WriteLineAsync(command);
        await stdin.FlushAsync(cancellationToken);
        await delay(replyDelay);

        await stdin.WriteLineAsync("quit");
        await stdin.FlushAsync(cancellationToken);
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

    /// <summary>Bytes per line in bluetoothctl's value hexdump (<c>bt_shell_hexdump</c> in BlueZ).</summary>
    private const int HexDumpBytesPerLine = 16;

    /// <summary>Colour and readline markers bluetoothctl emits even when its output is a pipe.</summary>
    private static readonly Regex TerminalMarkers = new("\u001b\\[[0-9;]*[a-zA-Z]|[\u0001\u0002]", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the value from a <c>bluetoothctl read</c> transcript.
    /// </summary>
    /// <remarks>
    /// bluetoothctl renders a value as a hexdump: up to sixteen space-separated hex pairs, two
    /// spaces, then the same bytes as printable ASCII. Only that leading hex column is the value, so
    /// each line is read from its start and abandoned at the first token that is not a hex pair.
    ///
    /// Scanning the whole transcript for anything hex-shaped invents bytes that were never read. The
    /// ASCII column repeats the payload verbatim, spaces included, so a value containing " ad " puts
    /// a bare hex pair there; and since #72 the transcript also carries the <c>gatt</c> submenu's
    /// help listing, which <see cref="EnterGattMenuCommand"/> prints on the way in.
    /// </remarks>
    internal static byte[] ParseReadValue(string output)
    {
        var bytes = new List<byte>();

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            AppendHexDumpLine(bytes, line);
        }

        return bytes.ToArray();
    }

    private static void AppendHexDumpLine(List<byte> bytes, string rawLine)
    {
        var line = StripPrompt(TerminalMarkers.Replace(rawLine, string.Empty).Trim());

        var tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var take = Math.Min(tokens.Length, HexDumpBytesPerLine);

        for (var i = 0; i < take; i++)
        {
            if (!TryParseHexPair(tokens[i], out var value))
            {
                // Either this is not a value line at all, or the ASCII column has begun.
                return;
            }

            bytes.Add(value);
        }
    }

    /// <summary>
    /// Removes a leading <c>[bluetooth]#</c>-style prompt. bluetoothctl interleaves its prompt with
    /// output, so a value line can arrive with one in front of it.
    /// </summary>
    private static string StripPrompt(string line)
    {
        if (!line.StartsWith('['))
        {
            return line;
        }

        var end = line.IndexOf("]#", StringComparison.Ordinal);

        return end < 0 ? line : line[(end + 2)..].Trim();
    }

    private static bool TryParseHexPair(string token, out byte value)
    {
        var hex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;

        value = 0;

        return hex.Length == 2
               && byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// bluetoothctl acknowledges dispatching a GATT operation to bluetoothd before its reply
    /// arrives; the reply itself is silent on success. These are the only positive signals it gives.
    /// </summary>
    private const string ReadAcknowledgement = "Attempting to read";

    private const string WriteAcknowledgement = "Attempting to write";

    /// <summary>
    /// Everything bluetoothctl 5.66 prints when it refuses a command instead of carrying it out,
    /// taken from the strings in the binary shipped on the target unit. A refusal must never be read
    /// as a quiet success or an empty result (#72).
    /// </summary>
    private static readonly string[] RefusalMarkers =
    [
        "Invalid command in menu",  // the command does not exist in the menu it was issued in
        "No device connected",      // select-attribute before the attribute tree resolved
        "No attribute selected",    // read or write with nothing selected
        "not available",            // "Attribute <uuid> not available", and its Device/Controller kin
        "Failed to read",
        "Failed to write",
        "Failed to disconnect",
    ];

    /// <summary>
    /// Refusals bluetoothctl can give a scan. Kept separate from <see cref="RefusalMarkers"/>
    /// because scan output carries advertised device names, which are untrusted: a neighbouring
    /// device could otherwise name itself into aborting the scan.
    /// </summary>
    private static readonly string[] ScanRefusalMarkers =
    [
        "Invalid command in menu",
        "Failed to start discovery",
    ];

    private static void EnsureNotRefused(string output, string operation)
    {
        var refusal = RefusalMarkers.FirstOrDefault(
            marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (refusal is not null)
        {
            throw new BleTransportException(
                $"bluetoothctl refused to {operation}: {Excerpt(LinesContaining(output, refusal))}");
        }
    }

    private static void EnsureAcknowledged(string output, string acknowledgement, string operation)
    {
        if (!output.Contains(acknowledgement, StringComparison.OrdinalIgnoreCase))
        {
            throw new BleTransportException(
                $"bluetoothctl never dispatched the {operation}; it did not report \"{acknowledgement}\".");
        }
    }

    /// <summary>Guards the streaming scan. See <see cref="ScanRefusalMarkers"/> for the narrow set.</summary>
    private static void EnsureScanLineNotRefused(string line)
    {
        // Advertised names arrive on "Device <address> <name>" lines; refusals never do.
        if (line.Contains("Device ", StringComparison.Ordinal))
        {
            return;
        }

        var refusal = ScanRefusalMarkers.FirstOrDefault(
            marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (refusal is not null)
        {
            throw new BleTransportException($"bluetoothctl refused to scan: {line.Trim()}");
        }
    }

    private static string LinesContaining(string output, string marker) =>
        string.Join(
            '\n',
            output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private Process StartProcess()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _executable,
                Arguments = _arguments,
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

    /// <summary>
    /// Runs one <c>bluetoothctl</c> session and returns its stdout transcript.
    /// </summary>
    /// <remarks>
    /// Both pipes are drained concurrently, and draining stderr is not optional: it is redirected,
    /// so a session that writes more than the OS pipe buffer holds would block on the write and
    /// never reach exit. Both reads start before <paramref name="drive"/> so neither stream can
    /// back up while the session is being driven.
    ///
    /// Every failure path tears the session down, not just cancellation. A session can fail without
    /// being cancelled — most readily when <c>bluetoothctl</c> exits mid-session and the next stdin
    /// write lands on a closed pipe — and without teardown that leaves a live-but-wedged subprocess
    /// on the unit and two abandoned reads. Failures other than cancellation are wrapped so callers
    /// only ever see <see cref="BleTransportException"/> from this transport.
    /// </remarks>
    private async Task<string> RunSessionAsync(
        Func<StreamWriter, CancellationToken, Task> drive,
        CancellationToken cancellationToken)
    {
        using var process = StartProcess();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var drained = false;

        try
        {
            await drive(process.StandardInput, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            // Awaited together so a fault in one still observes the other.
            await Task.WhenAll(outputTask, errorTask);
            drained = true;

            if (process.ExitCode != 0)
            {
                // stderr is the only diagnosis available for an on-device session that never ran.
                throw new BleTransportException(
                    $"bluetoothctl exited with code {process.ExitCode}: {Excerpt(errorTask.Result)}");
            }

            return outputTask.Result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not BleTransportException)
        {
            throw new BleTransportException("The bluetoothctl session failed.", ex);
        }
        finally
        {
            if (!drained)
            {
                // Killing the process closes both pipes, which completes the abandoned reads.
                StopProcess(process);
                Forget(outputTask);
                Forget(errorTask);
            }
        }
    }

    /// <summary>Condenses subprocess output into a single bounded line for an exception message.</summary>
    internal static string Excerpt(string error)
    {
        var text = string.Join(
            ' ',
            error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return text.Length switch
        {
            0 => "(no stderr output)",
            <= StderrExcerptLength => text,
            _ => text[..StderrExcerptLength] + "...",
        };
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
