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
        const string output = "Attribute value:\n  Value: 0x7b 0x22 0x6f 0x6b 0x22 0x7d\n";

        var bytes = BlueZBleTransport.ParseReadValue(output);

        Assert.Equal(new byte[] { 0x7b, 0x22, 0x6f, 0x6b, 0x22, 0x7d }, bytes);
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
    private static BlueZBleTransport ShellTransport(string script, string windowsScript) =>
        OperatingSystem.IsWindows()
            ? new BlueZBleTransport(NullLogger<BlueZBleTransport>.Instance, "cmd.exe", $"/c \"{windowsScript}\"")
            : new BlueZBleTransport(NullLogger<BlueZBleTransport>.Instance, "/bin/sh", $"-c \"{script}\"");
}
