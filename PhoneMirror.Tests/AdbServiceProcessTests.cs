using System.IO;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

/// <summary>
/// AdbService contra un adb de mentira (FakeAdb.exe, al lado de las pruebas): nunca se lanza el
/// adb de verdad ni se toca un movil. Las variables de entorno son del proceso, asi que todo lo
/// que las usa va en esta clase (xUnit ejecuta en serie las pruebas de una misma clase).
/// </summary>
public sealed class AdbServiceProcessTests : IDisposable
{
    private readonly string _log = Path.Combine(Path.GetTempPath(), $"fakeadb-{Guid.NewGuid():N}.log");
    private readonly AdbService _adb;

    public AdbServiceProcessTests()
    {
        var fake = Path.Combine(AppContext.BaseDirectory, "FakeAdb.exe");
        Assert.True(File.Exists(fake), $"Falta {fake}");
        _adb = new AdbService { ExecutablePath = fake };
        Environment.SetEnvironmentVariable("FAKE_ADB_LOG", _log);
        Fake(string.Empty);
    }

    public void Dispose()
    {
        foreach (var name in new[] { "FAKE_ADB_LOG", "FAKE_ADB_OUT", "FAKE_ADB_ERR", "FAKE_ADB_EXIT" })
            Environment.SetEnvironmentVariable(name, null);
        try { File.Delete(_log); } catch (IOException) { }
    }

    private static void Fake(string stdout, string stderr = "", int exit = 0)
    {
        Environment.SetEnvironmentVariable("FAKE_ADB_OUT", stdout.Length > 0 ? stdout : null);
        Environment.SetEnvironmentVariable("FAKE_ADB_ERR", stderr.Length > 0 ? stderr : null);
        Environment.SetEnvironmentVariable("FAKE_ADB_EXIT", exit.ToString());
    }

    private string[] Calls() => File.Exists(_log) ? File.ReadAllLines(_log) : [];

    [Fact]
    public async Task ListDevicesAsync_RunsDevicesLongAndParses()
    {
        Fake("List of devices attached\nABC\tdevice model:Pixel_7\n");

        var devices = await _adb.ListDevicesAsync();

        Assert.Equal(["devices|-l"], Calls());
        Assert.Equal(new AdbDevice("ABC", "device", "Pixel 7"), Assert.Single(devices));
    }

    [Fact]
    public async Task RunAsync_ReturnsStdout_KeepingUtf8()
    {
        Fake("Móvil de Àlex\n");
        Assert.Equal("Móvil de Àlex\n", await _adb.RunAsync(["version"], CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_ThrowsWithStderr()
    {
        Fake("salida", "error: device 'XYZ' not found\n", 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _adb.RunAsync(["-s", "XYZ", "shell", "ls"], CancellationToken.None));

        Assert.Equal("adb -s XYZ shell ls: error: device 'XYZ' not found", ex.Message);
    }

    [Fact]
    public async Task RunAsync_NonZeroExitWithoutStderr_ThrowsWithStdout()
    {
        Fake("  lo que dijo por la salida  ", exit: 255);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _adb.RunAsync(["x"], CancellationToken.None));

        Assert.Equal("adb x: lo que dijo por la salida", ex.Message);
    }

    [Fact]
    public async Task RunAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _adb.RunAsync(["devices"], cts.Token));
    }

    [Fact]
    public async Task WithoutExecutable_EverythingSaysAdbNotFound()
    {
        var adb = new AdbService { ExecutablePath = null };

        Assert.False(adb.IsAvailable);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adb.ListDevicesAsync());
        Assert.Equal("adb.exe no encontrado.", ex.Message);
        Assert.Throws<InvalidOperationException>(() => adb.StartShell("ABC", "ls"));
    }

    [Fact]
    public async Task Commands_PassTheRightArguments()
    {
        await _adb.PushAsync("ABC", @"C:\tmp\foto 1.jpg", "/sdcard/Download/foto 1.jpg");
        await _adb.ForwardAsync("ABC", 27183, "localabstract:scrcpy_0000002a");
        await _adb.RemoveForwardAsync("ABC", 27183);
        await _adb.InstallAsync("ABC", @"C:\tmp\app.apk");
        await _adb.DisconnectAsync(" 192.168.1.40 ");
        Fake("u0_a123\n");
        Assert.Equal("u0_a123\n", await _adb.ShellAsync("ABC", "whoami"));

        Assert.Equal(
        [
            @"-s|ABC|push|C:\tmp\foto 1.jpg|/sdcard/Download/foto 1.jpg",
            "-s|ABC|forward|tcp:27183|localabstract:scrcpy_0000002a",
            "-s|ABC|forward|--remove|tcp:27183",
            @"-s|ABC|install|-r|C:\tmp\app.apk",
            "disconnect|192.168.1.40:5555",
            "-s|ABC|shell|whoami",
        ], Calls());
    }

    [Theory]
    [InlineData("connected to 192.168.1.40:5555\n")]
    [InlineData("already connected to 192.168.1.40:5555\n")]
    public async Task ConnectAsync_Success_ReturnsTrimmedOutput(string output)
    {
        Fake(output);

        var result = await _adb.ConnectAsync("192.168.1.40");

        Assert.Equal(output.Trim(), result);
        Assert.Equal(["connect|192.168.1.40:5555"], Calls());
    }

    [Fact]
    public async Task ConnectAsync_FailureWithExitZero_Throws()
    {
        Fake("failed to connect to '192.168.1.40:5555': Connection refused\n");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _adb.ConnectAsync("192.168.1.40:5555"));

        Assert.Equal("failed to connect to '192.168.1.40:5555': Connection refused", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_NoOutput_ThrowsWithTheCommand()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _adb.ConnectAsync("10.0.0.2"));
        Assert.Equal("adb connect 10.0.0.2", ex.Message);
    }

    [Fact]
    public async Task PairAsync_TrimsArgumentsAndAcceptsSuccess()
    {
        Fake("Successfully paired to 192.168.1.40:37000 [guid=adb-X]\n");

        var result = await _adb.PairAsync(" 192.168.1.40:37000 ", " 123456 ");

        Assert.Equal("Successfully paired to 192.168.1.40:37000 [guid=adb-X]", result);
        Assert.Equal(["pair|192.168.1.40:37000|123456"], Calls());
    }

    [Fact]
    public async Task PairAsync_Failure_Throws()
    {
        Fake("Failed: Wrong password or connection was dropped.\n");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _adb.PairAsync("1.2.3.4:5", "000000"));
        Assert.Equal("Failed: Wrong password or connection was dropped.", ex.Message);

        Fake(string.Empty);
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _adb.PairAsync("1.2.3.4:5", "000000"));
        Assert.Equal("adb pair 1.2.3.4:5", ex.Message);
    }

    [Fact]
    public async Task StartShell_ReturnsTheRunningProcess()
    {
        using var process = _adb.StartShell("ABC", "CLASSPATH=/data/local/tmp/x app_process /");
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.Equal(["-s|ABC|shell|CLASSPATH=/data/local/tmp/x app_process /"], Calls());
    }

    [Fact]
    public void Relocate_FindsSomethingOrNothing_Consistently()
    {
        var adb = new AdbService();
        adb.Relocate();
        Assert.Equal(adb.ExecutablePath is not null, adb.IsAvailable);
        if (adb.ExecutablePath is { } path)
            Assert.True(File.Exists(path));
    }
}
