using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Greenhouse.Bluetooth.Tests;

public class BlueZBleTransportTests
{
    private const string SampleScanOutput =
        "[NEW] Device AA:BB:CC:DD:EE:01 GH-Edge-1ADD5912AF61\n" +
        "[CHG] Device AA:BB:CC:DD:EE:01 RSSI: -40\n" +
        "[NEW] Device AA:BB:CC:DD:EE:02 GH-Edge-2BEEF0000001\n" +
        "[CHG] Device AA:BB:CC:DD:EE:02 RSSI: -70\n" +
        "[NEW] Device AA:BB:CC:DD:EE:03 SomeLaptop\n" +
        "[CHG] Device AA:BB:CC:DD:EE:03 RSSI: -50\n";

    [Fact]
    public void ParseScanOutput_returns_only_prefix_matches_ordered_by_rssi()
    {
        var filter = new BleScanFilter(NamePrefix: "GH-Edge-");

        var devices = BlueZBleTransport.ParseScanOutput(SampleScanOutput, filter);

        Assert.Equal(2, devices.Count);
        Assert.Equal("AA:BB:CC:DD:EE:01", devices[0].DeviceId);   // -40 sorts before -70
        Assert.Equal("GH-Edge-1ADD5912AF61", devices[0].Name);
        Assert.Equal(-40, devices[0].Rssi);
        Assert.Equal("AA:BB:CC:DD:EE:02", devices[1].DeviceId);
        Assert.DoesNotContain(devices, d => d.Name == "SomeLaptop");
    }

    [Fact]
    public void ParseScanOutput_without_filter_returns_all_named_devices()
    {
        var devices = BlueZBleTransport.ParseScanOutput(SampleScanOutput, new BleScanFilter());

        Assert.Equal(3, devices.Count);
    }

    [Fact]
    public void ParseScanOutput_ignores_devices_that_never_advertised_a_name()
    {
        const string output = "[CHG] Device AA:BB:CC:DD:EE:04 RSSI: -30\n";

        var devices = BlueZBleTransport.ParseScanOutput(output, new BleScanFilter());

        Assert.Empty(devices);
    }

    [Fact]
    public void ParseReadValue_extracts_hex_bytes()
    {
        const string output = "Attribute value:\n  0x7b 0x22 0x6f 0x6b 0x22 0x7d\n";

        var bytes = BlueZBleTransport.ParseReadValue(output);

        Assert.Equal(new byte[] { 0x7b, 0x22, 0x6f, 0x6b, 0x22, 0x7d }, bytes);
    }

    /// <summary>
    /// Regression for #72: the fix puts <c>menu gatt</c> in the session, and bluetoothctl answers it
    /// by printing the whole submenu help listing into the transcript the value is parsed from. None
    /// of it is the value.
    /// </summary>
    [Fact]
    public void ParseReadValue_ignores_the_gatt_submenu_help_listing()
    {
        var bytes = BlueZBleTransport.ParseReadValue(GattMenuHelp + ReadTranscript);

        Assert.Equal(StatusPayloadBytes, bytes);
    }

    /// <summary>
    /// Regression for #72: bluetoothctl prints the value twice on every line — hex, then the same
    /// bytes as printable ASCII. A payload with a space in it therefore puts a bare two-character
    /// hex word in the ASCII column ("ad" below), and a parser that scans the whole line reads it as
    /// a seventeenth byte the Edge Unit never sent.
    /// </summary>
    [Fact]
    public void ParseReadValue_ignores_the_ascii_column()
    {
        var bytes = BlueZBleTransport.ParseReadValue(ReadTranscript);

        Assert.Equal(StatusPayloadBytes, bytes);
        Assert.Equal(StatusPayload, Encoding.UTF8.GetString(bytes));
    }

    /// <summary>A value line can arrive with bluetoothctl's interleaved prompt in front of it.</summary>
    [Fact]
    public void ParseReadValue_reads_a_value_line_behind_a_prompt()
    {
        const string output = "[GH-Edge-704BCA69CC00]#   7b 22 6f 6b 22 7d      {\"ok\"}\n";

        var bytes = BlueZBleTransport.ParseReadValue(output);

        Assert.Equal(new byte[] { 0x7b, 0x22, 0x6f, 0x6b, 0x22, 0x7d }, bytes);
    }

    /// <summary>
    /// Regression for #72, and the assertion that could not be delegated to a stand-in: the fault
    /// was a sequence that was perfectly well-formed but issued in bluetoothctl's main menu, and a
    /// stand-in answers whatever it is asked. Only the literal command sequence is evidence.
    /// </summary>
    [Fact]
    public async Task Gatt_read_enters_the_gatt_menu_before_selecting_the_attribute()
    {
        var sequence = await CaptureGattSequenceAsync("read");

        Assert.Equal(ExpectedGattSequence("read"), sequence);
    }

    /// <summary>Regression for #72 — the write half of the same fault.</summary>
    [Fact]
    public async Task Gatt_write_enters_the_gatt_menu_before_selecting_the_attribute()
    {
        var sequence = await CaptureGattSequenceAsync("write \"0x7b 0x7d\"");

        Assert.Equal(ExpectedGattSequence("write \"0x7b 0x7d\""), sequence);
    }

    /// <summary>
    /// Regression for #72, the silent half: the real rejection is "Invalid command in menu main:
    /// write", which does not contain "Failed to write". Inferring success from the absence of that
    /// one phrase reported a completed write when no bytes had left the Pi.
    /// </summary>
    [Fact]
    public async Task Write_fails_when_bluetoothctl_refuses_the_command()
    {
        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => WriteAsync(GattTranscript("Invalid command in menu main: write")));

        Assert.Contains("refused", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A write bluetoothctl never acknowledged dispatching did not happen.</summary>
    [Fact]
    public async Task Write_fails_when_bluetoothctl_never_acknowledges_the_write()
    {
        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => WriteAsync(GattTranscript("[bluetooth]# ")));

        Assert.Contains("Attempting to write", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_succeeds_when_bluetoothctl_acknowledges_the_dispatch()
    {
        await WriteAsync(GattTranscript(WriteAcknowledgement));
    }

    /// <summary>
    /// Regression for #72: a refused read parsed to zero bytes, and the adapter above reported that
    /// as an empty response *from the Edge Unit* — error 2099 blaming a device that was powered,
    /// advertising, and answering correctly. It must surface as the transport fault it is.
    /// </summary>
    [Fact]
    public async Task Read_fails_when_bluetoothctl_refuses_the_command()
    {
        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => ReadAsync(GattTranscript("No attribute selected")));

        Assert.Contains("refused", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No attribute selected", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A read that produced no value at all is not an empty value.</summary>
    [Fact]
    public async Task Read_fails_when_bluetoothctl_never_acknowledges_the_read()
    {
        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => ReadAsync(GattTranscript("[bluetooth]# ")));

        Assert.Contains("Attempting to read", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The whole read path, from a transcript shaped like the real unit's.</summary>
    [Fact]
    public async Task Read_returns_the_value_from_a_full_session_transcript()
    {
        var bytes = await ReadAsync(GattTranscript(GattMenuHelp + ReadTranscript));

        Assert.Equal(StatusPayload, Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// Same hazard class as #72, at the other end of the transport: a refused <c>scan on</c> reads
    /// as a greenhouse with no Edge Units in it rather than as a scan that never started.
    /// </summary>
    [Fact]
    public async Task Scan_fails_when_bluetoothctl_refuses_to_start_discovery()
    {
        var transport = ScanTransport("Failed to start discovery: org.bluez.Error.NotReady");
        using var timeout = new CancellationTokenSource(SessionTimeout);

        await Assert.ThrowsAsync<BleTransportException>(async () =>
        {
            await foreach (var _ in transport.ScanAsync(new BleScanFilter(), ScanWindow, timeout.Token))
            {
            }
        });
    }

    /// <summary>An advertised name is untrusted input and must not be able to abort a scan.</summary>
    [Fact]
    public async Task Scan_is_not_aborted_by_a_device_that_advertises_a_refusal_as_its_name()
    {
        var transport = ScanTransport("[NEW] Device AA:BB:CC:DD:EE:05 Invalid command in menu main: scan");
        using var timeout = new CancellationTokenSource(SessionTimeout);

        await foreach (var _ in transport.ScanAsync(new BleScanFilter(), ScanWindow, timeout.Token))
        {
        }
    }

    [Fact]
    public void Excerpt_reports_empty_stderr_explicitly()
    {
        Assert.Equal("(no stderr output)", BlueZBleTransport.Excerpt("\n  \r\n"));
    }

    [Fact]
    public void Excerpt_flattens_and_bounds_chatty_stderr()
    {
        var excerpt = BlueZBleTransport.Excerpt(string.Join('\n', Enumerable.Repeat(new string('e', 80), 50)));

        Assert.DoesNotContain('\n', excerpt);
        Assert.EndsWith("...", excerpt);
        Assert.True(excerpt.Length < 600, $"stderr excerpt was not bounded: {excerpt.Length} chars.");
    }

    /// <summary>
    /// Regression for #41: a session whose stderr exceeds the OS pipe buffer must still complete.
    /// Before both pipes were drained, the child blocked on its stderr write and the session hung
    /// until cancellation. ~260 KB of stderr is far past the buffer on either host platform
    /// (64 KB on Linux, 4 KB on Windows), so the pre-fix code deadlocks here every time.
    /// </summary>
    [Fact]
    public async Task RunSession_completes_when_the_subprocess_floods_stderr()
    {
        var transport = ShellTransport(FloodStderrScript, WindowsFloodStderrScript);
        using var timeout = new CancellationTokenSource(SessionTimeout);

        await transport.DisconnectAsync(SampleAddress, timeout.Token);
    }

    /// <summary>A non-zero exit surfaces the subprocess's stderr, the only on-device diagnosis.</summary>
    [Fact]
    public async Task RunSession_surfaces_exit_code_and_stderr_on_failure()
    {
        var transport = ShellTransport(FailingScript, WindowsFailingScript);
        using var timeout = new CancellationTokenSource(SessionTimeout);

        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => transport.DisconnectAsync(SampleAddress, timeout.Token));

        Assert.Contains("code 3", ex.Message, StringComparison.Ordinal);
        Assert.Contains(ControllerError, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression for #65: a session can fail without being cancelled. The child here exits before
    /// <see cref="BlueZBleTransport.ConnectAsync"/> drives the second half of its stdin, so the
    /// write lands on a closed pipe. That must not escape as a raw <see cref="IOException"/> — the
    /// onboarding workflow surfaces the message straight to the operator.
    /// </summary>
    [Fact]
    public async Task RunSession_wraps_a_failure_when_the_child_exits_before_stdin_is_driven()
    {
        // No stdin read and no delay: the child is long gone by the time ConnectAsync flushes
        // "quit", five seconds in. This is the race without the FailingScript workaround.
        var transport = ShellTransport("exit 0", "exit /b 0");
        using var timeout = new CancellationTokenSource(SessionTimeout);

        var ex = await Assert.ThrowsAsync<BleTransportException>(
            () => transport.ConnectAsync(SampleAddress, timeout.Token));

        Assert.IsAssignableFrom<IOException>(ex.InnerException);
    }

    /// <summary>
    /// Regression for #65: the same failure must also tear the subprocess down. <c>Process.Dispose</c>
    /// does not kill, so without teardown a live-but-wedged <c>bluetoothctl</c> is leaked on the Pi.
    /// The child here closes its own stdin — making the parent's write fail — but stays alive
    /// ticking a file, so the test can see whether it was actually killed.
    /// </summary>
    [Fact]
    public async Task RunSession_kills_a_child_that_survives_the_failure()
    {
        if (!OperatingSystem.IsLinux())
        {
            // Needs a shell that can close an inherited descriptor; the target unit is Linux.
            return;
        }

        var heartbeat = Path.Combine(Path.GetTempPath(), $"gh-ble-65-{Guid.NewGuid():N}");
        var transport = ShellTransport(
            $"exec 0<&-; while true; do echo tick >> {heartbeat}; sleep 0.2; done",
            windowsScript: string.Empty);
        using var timeout = new CancellationTokenSource(SessionTimeout);

        try
        {
            await Assert.ThrowsAsync<BleTransportException>(
                () => transport.ConnectAsync(SampleAddress, timeout.Token));

            var atFailure = new FileInfo(heartbeat).Length;
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Equal(atFailure, new FileInfo(heartbeat).Length);
        }
        finally
        {
            File.Delete(heartbeat);
        }
    }

    private const string SampleAddress = "AA:BB:CC:DD:EE:01";

    private const string ControllerError = "No default controller available";

    /// <summary>Bounds a regression: a deadlocked session fails the test instead of hanging it.</summary>
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(30);

    private const string StderrLine = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    private const string FloodStderrScript =
        "i=0; while [ $i -lt 4000 ]; do echo '" + StderrLine + "' >&2; i=$((i+1)); done";

    private const string WindowsFloodStderrScript =
        "for /L %i in (1,1,4000) do @echo " + StderrLine + " 1>&2";

    // The child consumes a line of stdin first so it cannot exit before the session drives stdin,
    // which would race the write against a closed pipe instead of exercising the exit-code path.
    private const string FailingScript = "read line; echo '" + ControllerError + "' >&2; exit 3";

    private const string WindowsFailingScript =
        "set /p line= & echo " + ControllerError + " 1>&2 & exit /b 3";

    /// <summary>
    /// A <c>bluetoothctl</c> stand-in: the transport's subprocess handling is platform-neutral, so
    /// the same assertions run against the host's shell on the Pi's Linux and on a developer's
    /// Windows box.
    /// </summary>
    private static BlueZBleTransport ShellTransport(
        string script,
        string windowsScript,
        TimeSpan? gattStepDelay = null) =>
        OperatingSystem.IsWindows()
            ? new BlueZBleTransport(
                NullLogger<BlueZBleTransport>.Instance,
                "cmd.exe",
                $"/c \"{windowsScript}\"",
                gattStepDelay)
            : new BlueZBleTransport(
                NullLogger<BlueZBleTransport>.Instance,
                "/bin/sh",
                $"-c \"{script}\"",
                gattStepDelay);

    // ---- #72 fixtures ---------------------------------------------------------------------------

    /// <summary>The canonical provisioning-status characteristic; the UUID #72 was reported against.</summary>
    private static readonly Guid StatusCharacteristicUuid = new("00034452-414f-424e-4f2d-454744454847");

    private const string ReadAcknowledgement =
        "Attempting to read /org/bluez/hci0/dev_70_4B_CA_69_CC_02/service0009/char000a";

    private const string WriteAcknowledgement =
        "Attempting to write /org/bluez/hci0/dev_70_4B_CA_69_CC_02/service0009/char000c";

    /// <summary>
    /// A payload whose ASCII rendering contains a bare two-character hex word — "ad" — so the
    /// hexdump's second column is a live trap for a parser that reads the whole line.
    /// </summary>
    private const string StatusPayload = "{\"m\":\"no ad here\"}";

    private static readonly byte[] StatusPayloadBytes = Encoding.UTF8.GetBytes(StatusPayload);

    /// <summary>
    /// <c>bluetoothctl</c>'s value hexdump, in its exact shape: sixteen hex pairs, two spaces, then
    /// the same bytes as printable ASCII.
    /// </summary>
    private const string ReadTranscript =
        ReadAcknowledgement + "\n" +
        "  7b 22 6d 22 3a 22 6e 6f 20 61 64 20 68 65 72 65   {\"m\":\"no ad here\n" +
        "  22 7d                                             \"}\n";

    /// <summary>
    /// What <c>menu gatt</c> itself prints — the noise the #72 fix necessarily adds to every read
    /// transcript. Trimmed to the shape, not the full listing.
    /// </summary>
    private const string GattMenuHelp =
        "Menu gatt:\n" +
        "Available commands:\n" +
        "-------------------\n" +
        "list-attributes [dev/local]                       List attributes\n" +
        "select-attribute <attribute/UUID>                 Select attribute\n" +
        "read [offset]                                     Read attribute value\n" +
        "write <data=xx xx ...> [offset] [type]            Write attribute value\n" +
        "back                                              Return to main menu\n" +
        "[bluetooth]#                         [CHG] Controller E4:5F:01:8E:47:93 Pairable: yes\n";

    /// <summary>Long enough that the stand-in finishes inside it, short enough not to pad the suite.</summary>
    private static readonly TimeSpan ScanWindow = TimeSpan.FromSeconds(10);

    private static string ExpectedGattSequence(string command) =>
        string.Join(
            '\n',
            "menu gatt",
            $"select-attribute {StatusCharacteristicUuid:D}",
            command,
            "quit") + "\n";

    /// <summary>
    /// Captures the literal lines the transport writes for a GATT operation, with no subprocess in
    /// the way. See <see cref="Gatt_read_enters_the_gatt_menu_before_selecting_the_attribute"/> for
    /// why the sequence itself has to be the assertion.
    /// </summary>
    private static async Task<string> CaptureGattSequenceAsync(string command)
    {
        var stdin = new StringWriter { NewLine = "\n" };

        await BlueZBleTransport.DriveGattSessionAsync(
            stdin,
            StatusCharacteristicUuid,
            command,
            TimeSpan.Zero,
            TimeSpan.Zero,
            _ => Task.CompletedTask,
            CancellationToken.None);

        return stdin.ToString();
    }

    private static Task WriteAsync(BlueZBleTransport transport) =>
        transport.WriteCharacteristicAsync(
            SampleAddress,
            Guid.Empty,
            StatusCharacteristicUuid,
            [0x7b, 0x7d],
            CancellationToken.None);

    private static Task<byte[]> ReadAsync(BlueZBleTransport transport) =>
        transport.ReadCharacteristicAsync(
            SampleAddress,
            Guid.Empty,
            StatusCharacteristicUuid,
            CancellationToken.None);

    /// <summary>
    /// A stand-in that answers a GATT session with <paramref name="transcript"/>. The transcript goes
    /// through a file so no shell has to quote it, and the stand-in outlives the session being driven
    /// so the writes cannot land on a closed pipe (the #65 race).
    /// </summary>
    private static BlueZBleTransport GattTranscript(string transcript)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gh-ble-72-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, transcript);

        return ShellTransport(
            $"sleep 1; cat '{path}'",
            $"ping -n 2 127.0.0.1 >nul & type \"{path}\"",
            TimeSpan.Zero);
    }

    /// <summary>
    /// A scan stand-in that emits one line. It is alive while the session writes <c>scan on</c> —
    /// otherwise that write lands on a closed pipe instead of exercising the scan — and exits soon
    /// after, which closes the scan loop well inside <see cref="ScanWindow"/>.
    /// </summary>
    private static BlueZBleTransport ScanTransport(string line) =>
        ShellTransport(
            $"sleep 1; echo '{line}'; sleep 2",
            $"ping -n 2 127.0.0.1 >nul & echo {line} & ping -n 3 127.0.0.1 >nul");
}
