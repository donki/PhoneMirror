using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using PhoneMirror.Localization;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

/// <summary>
/// Las ventanas de verdad en un hilo STA propio, con la App y sus recursos (App.xaml). Los
/// dialogos modales se contestan con <see cref="Expect{T}"/>; uno que nadie esperaba se cierra y
/// la prueba falla. Las ventanas no se activan y se abren fuera de la pantalla.
/// </summary>
internal static class Ui
{
    private static readonly Dispatcher Dispatcher;
    private static readonly Queue<(Type Type, Action<Window> Act)> Responders = new();
    private static readonly List<Exception> Errors = [];

    static Ui()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            App.HostedByTests = true;
            // pack://application:,,, tiene que ser la app, no testhost (WPF no deja cambiarlo dos veces).
            typeof(Application).GetField("_resourceAssembly", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(null, typeof(App).Assembly);
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // App.xaml dice OnMainWindowClose
            dispatcher.UnhandledException += (_, e) =>
            {
                Errors.Add(e.Exception);
                e.Handled = true;
            };
            foreach (var type in typeof(App).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Window)) && !t.IsAbstract))
                Window.ShowActivatedProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(false));
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Ui" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        Dispatcher = dispatcher!;
    }

    public static void Run(Action action) => Run(() => { action(); return 0; });

    public static T Run<T>(Func<T> action)
    {
        var done = new ManualResetEventSlim();
        var watchdog = new Thread(() =>
        {
            // Una prueba colgada (un dialogo que no se cierra) no cuelga el banco: se cierra todo y falla.
            if (done.Wait(40_000))
                return;
            Dispatcher.BeginInvoke(() =>
            {
                var open = string.Join(", ", Application.Current.Windows.Cast<Window>().Select(w => $"{w.GetType().Name} «{w.Title}»"));
                Errors.Add(new TimeoutException("La prueba no acabo en 40 s; ventanas abiertas: " + open));
                foreach (var w in Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).ToList())
                    w.Close();
            });
        }) { IsBackground = true };
        watchdog.Start();
        var result = Dispatcher.Invoke(() =>
        {
            Responders.Clear();
            Errors.Clear();
            try
            {
                return action();
            }
            finally
            {
                foreach (var w in Application.Current.Windows.Cast<Window>().ToList())
                {
                    if (w is MainWindow)
                        w.GetType().GetField("_quitting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(w, true);
                    w.Close();
                }
                WaitFor(() => Desktop.Current is not FakeDesktop fake || fake.Servers.All(s => s.Closed), 10_000, fail: false);
                DoEvents();
            }
        });
        done.Set();
        var pending = Dispatcher.Invoke(() => (Responders.Count, Errors.ToList()));
        if (pending.Item2.Count > 0)
            ExceptionDispatchInfo.Capture(pending.Item2[0]).Throw();
        Assert.True(pending.Count == 0, $"Quedaron {pending.Count} dialogos esperados sin abrir");
        return result;
    }

    public static int Pending => Responders.Count;

    public static void Expect<T>(Action<T> act) where T : Window => Responders.Enqueue((typeof(T), w => act((T)w)));

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var w = (Window)sender;
        w.Left = -20000;
        w.Top = -20000;
        if (w is MainWindow && (Responders.Count == 0 || Responders.Peek().Type != typeof(MainWindow)))
            return;
        if (Responders.Count == 0 || !Responders.Peek().Type.IsInstanceOfType(w))
        {
            Errors.Add(new InvalidOperationException($"Dialogo inesperado: {w.GetType().Name} «{w.Title}»"));
            w.Dispatcher.BeginInvoke(w.Close);
            return;
        }
        var (_, act) = Responders.Dequeue();
        w.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                act(w);
            }
            catch (Exception ex)
            {
                Errors.Add(ex);
                w.Close();
            }
        }, DispatcherPriority.Background);
    }

    private static readonly System.Reflection.MethodInfo OnClick =
        typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    public static bool IsClosed(Window w) => PresentationSource.FromVisual(w) is null && !w.IsVisible;

    /// <summary>Pulsa un boton como el raton (Click, IsCancel/IsDefault, los ToggleButton cambian).</summary>
    public static void Click(ButtonBase button)
    {
        Assert.True(button.IsEnabled, $"{button.Name} esta desactivado");
        OnClick.Invoke(button, null);
    }

    public static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        All<T>(root).FirstOrDefault(c => (c as FrameworkElement)?.Name == id || System.Windows.Automation.AutomationProperties.GetAutomationId(c) == id)
        ?? throw new InvalidOperationException($"No hay {typeof(T).Name} «{id}»");

    public static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T t)
            yield return t;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var c in All<T>(child))
                yield return c;
    }

    /// <summary>Deja correr la cola de la interfaz (y lo asincrono) hasta que se cumpla la condicion.</summary>
    public static void WaitFor(Func<bool> condition, int timeoutMs = 15_000, bool fail = true)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds >= timeoutMs)
            {
                Assert.False(fail, "La interfaz no llego al estado esperado");
                return;
            }
            DoEvents();
            Thread.Sleep(5);
        }
    }

    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    public static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;

    public static object? Call(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try
        {
            return method.Invoke(target, args);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}

/// <summary>El icono de la bandeja, de mentira: apunta lo que se le pide.</summary>
internal sealed class FakeTray : ITrayIcon
{
    public string Text { get; private set; } = string.Empty;
    public List<(string Title, string Text)> Balloons { get; } = [];
    public bool Disposed { get; private set; }

    public event EventHandler? Activated;

    public event EventHandler? QuitRequested;

    public void SetText(string text) => Text = text;

    public void Balloon(string title, string text) => Balloons.Add((title, text));

    public void Activate() => Activated?.Invoke(this, EventArgs.Empty);

    public void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}

/// <summary>
/// El servidor de scrcpy de mentira, en el puerto local de la sesion: acepta el video (con su
/// byte de relleno, nombre y codec) y el control, deja mandar paquetes de video y mensajes del
/// movil, y apunta lo que llega por el canal de control.
/// </summary>
internal sealed class FakeScrcpyServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<byte> _control = [];
    private TcpClient? _video;
    private TcpClient? _controlClient;

    public FakeScrcpyServer(int port, string deviceName = "Pixel de prueba", uint codec = 0x68323634, bool acceptVideo = true)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        if (!acceptVideo)
        {
            // Nadie escucha: la conexion se rechaza (como un servidor que no ha llegado a arrancar).
            Closed = true;
            Task = Task.CompletedTask;
            return;
        }
        _listener.Start();
        Task = Task.Run(async () =>
        {
            _video = await _listener.AcceptTcpClientAsync();
            var v = _video.GetStream();
            await v.WriteAsync(new byte[] { 0 });
            _controlClient = await _listener.AcceptTcpClientAsync();
            var name = new byte[64];
            Encoding.UTF8.GetBytes(deviceName).CopyTo(name, 0);
            await v.WriteAsync(name);
            var c = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(c, codec);
            await v.WriteAsync(c);
            Connected.Set();
            var buffer = new byte[4096];
            var stream = _controlClient.GetStream();
            try
            {
                while (true)
                {
                    var n = await stream.ReadAsync(buffer);
                    if (n == 0)
                        break;
                    lock (_control)
                        _control.AddRange(buffer.AsSpan(0, n).ToArray());
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
            Closed = true;
        });
    }

    public Task Task { get; }

    public ManualResetEventSlim Connected { get; } = new();

    public bool Closed { get; private set; }

    public byte[] ControlBytes()
    {
        lock (_control)
            return [.. _control];
    }

    public void ClearControl()
    {
        lock (_control)
            _control.Clear();
    }

    private void Video(byte[] header, byte[]? payload = null)
    {
        var v = _video!.GetStream();
        v.Write(header);
        if (payload is not null)
            v.Write(payload);
        v.Flush();
    }

    public void Session(int width, int height)
    {
        var h = new byte[12];
        h[0] = 0x80;
        BinaryPrimitives.WriteInt32BigEndian(h.AsSpan(4), width);
        BinaryPrimitives.WriteInt32BigEndian(h.AsSpan(8), height);
        Video(h);
    }

    public void Packet(byte[] data, bool config = false, long pts = 0)
    {
        var h = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(h, (ulong)pts | (config ? ScrcpyProtocol.PacketFlagConfig : 0));
        BinaryPrimitives.WriteInt32BigEndian(h.AsSpan(8), data.Length);
        Video(h, data);
    }

    /// <summary>Paquete de video con longitud cero: el cliente lo da por roto.</summary>
    public void BrokenPacket() => Video(new byte[12]);

    public void Clipboard(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var m = new byte[5 + bytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(m.AsSpan(1), bytes.Length);
        bytes.CopyTo(m, 5);
        var s = _controlClient!.GetStream();
        s.Write(m);
        s.Flush();
    }

    /// <summary>El movil se va (se desenchufa).</summary>
    public void Hangup()
    {
        _video?.Dispose();
        _controlClient?.Dispose();
    }

    public void Dispose()
    {
        Hangup();
        _listener.Stop();
    }
}

/// <summary>La plataforma de mentira: adb de mentira, bandeja de mentira, portapapeles propio.</summary>
internal sealed class FakeDesktop : IDesktop
{
    public static string FakeAdbPath => Path.Combine(AppContext.BaseDirectory, "FakeAdb.exe");

    public List<FakeScrcpyServer> Servers { get; } = [];
    public List<FakeTray> Trays { get; } = [];
    public List<ProcessStartInfo> Started { get; } = [];
    public List<(string Text, string Caption)> Messages { get; } = [];
    public Exception? StartError { get; set; }
    public Exception? InstallError { get; set; }
    public bool AdbOnPath { get; set; }
    public string? AdbToPick { get; set; }
    public string? Clipboard { get; set; }
    public bool ClipboardThrows { get; set; }
    public string ScreenshotFolder { get; set; } = string.Empty;
    public List<Instances.Instance> Others { get; } = [];
    public List<Instances.Instance> Shown { get; } = [];
    public int Shutdowns { get; private set; }
    public int Prepared { get; private set; }
    public int Installs { get; private set; }
    public bool AdbAvailable { get; set; } = true;
    public bool ServerAcceptsVideo { get; set; } = true;
    public uint Codec { get; set; } = ScrcpyProtocol.CodecH264;

    public AdbService CreateAdb() => new() { ExecutablePath = AdbAvailable ? FakeAdbPath : null };

    public ScrcpySession CreateSession(AdbService adb, string serial)
    {
        var session = new ScrcpySession(adb, serial);
        Servers.Add(new FakeScrcpyServer(session.LocalPort, codec: Codec, acceptVideo: ServerAcceptsVideo));
        return session;
    }

    public void PrepareAssets() => Prepared++;

    public async Task<string> InstallAdbAsync()
    {
        Installs++;
        await Task.Yield();
        if (InstallError is not null)
            throw InstallError;
        AdbService.RememberChosen(FakeAdbPath);
        return Path.GetDirectoryName(FakeAdbPath)!;
    }

    public bool EnsureAdbOnPath() => AdbOnPath;

    public ITrayIcon CreateTray(string openText, string quitText)
    {
        var tray = new FakeTray();
        Trays.Add(tray);
        return tray;
    }

    public string? PickAdb(Window owner, string title) => AdbToPick;

    public bool ClipboardHasText() => ClipboardThrows ? throw new InvalidOperationException("ocupado") : Clipboard is not null;

    public string ClipboardText() => Clipboard!;

    public void SetClipboardText(string text)
    {
        if (ClipboardThrows)
            throw new InvalidOperationException("ocupado");
        Clipboard = text;
    }

    public void Start(ProcessStartInfo info)
    {
        Started.Add(info);
        if (StartError is not null)
            throw StartError;
    }

    public void ShowMessage(Window owner, string text, string caption) => Messages.Add((text, caption));

    public IReadOnlyList<Instances.Instance> OtherInstances() => Others;

    public void ShowInstance(Instances.Instance instance) => Shown.Add(instance);

    public void Shutdown() => Shutdowns++;

    public FakeScrcpyServer Server => Servers[^1];
}

/// <summary>
/// Base de las pruebas de ventanas: plataforma falsa, adb de mentira con sus reglas, registro,
/// log, Wi-Fi y ruta de adb en temporales. Lo deja todo como estaba al acabar.
/// </summary>
public abstract class UiTest : IDisposable
{
    public const string RegistryBranch = @"Software\sOCPhoneMirror.Tests";
    private readonly IDesktop _desktop = Desktop.Current;
    private readonly string _log = AppLog.Path;
    private readonly string _wifi = WifiAddresses.FilePath;
    private readonly string _chosen = AdbService.ChosenPathFile;
    private readonly Func<RegistryKey> _root = OpenOnConnect.Root;
    private readonly Func<string?> _exe = OpenOnConnect.Executable;
    private readonly string _language = Loc.Language;

    internal FakeDesktop Fake { get; } = new();
    internal string Dir { get; } = Path.Combine(Path.GetTempPath(), "pm-ui-" + Guid.NewGuid().ToString("N"));
    internal string Rules => Path.Combine(Dir, "rules.txt");
    internal string AdbLog => Path.Combine(Dir, "adb.log");

    protected UiTest()
    {
        Directory.CreateDirectory(Dir);
        Desktop.Current = Fake;
        Fake.ScreenshotFolder = Path.Combine(Dir, "capturas");
        AppLog.Path = Path.Combine(Dir, "log.txt");
        WifiAddresses.FilePath = Path.Combine(Dir, "wifi.json");
        AdbService.ChosenPathFile = Path.Combine(Dir, "adb-path.txt");
        // Siempre el de mentira: si AdbService volviera a buscar adb sin esto, encontraria el de
        // verdad del PATH y hablaria con el movil enchufado.
        AdbService.RememberChosen(FakeDesktop.FakeAdbPath);
        Registry.CurrentUser.DeleteSubKeyTree(RegistryBranch, throwOnMissingSubKey: false);
        Registry.CurrentUser.CreateSubKey(RegistryBranch + @"\Software\Microsoft\Windows\CurrentVersion\Run").Dispose();
        OpenOnConnect.Root = () => Registry.CurrentUser.OpenSubKey(RegistryBranch, writable: true)!;
        OpenOnConnect.Executable = () => @"C:\Programas\sOCPhoneMirror.exe";
        Environment.SetEnvironmentVariable("FAKE_ADB_RULES", Rules);
        Environment.SetEnvironmentVariable("FAKE_ADB_LOG", AdbLog);
        Devices(("ABC123", "device", "Pixel_7"));
        Ui.Run(() =>
        {
            if (Loc.Language != "es")
                Loc.Toggle();
        });
    }

    /// <summary>Lo que dira «adb devices -l» (y las demas ordenes contestan bien).</summary>
    internal void Devices(params (string Serial, string State, string Model)[] devices)
    {
        var list = "List of devices attached\\n" + string.Concat(devices.Select(d => $"{d.Serial}   {d.State} model:{d.Model}\\n"));
        File.WriteAllLines(Rules,
        [
            "disconnect|\t\t0\t0",
            "devices\t" + list + "\t0\t0",
            "app_process\t\t0\t60000",
            "connect|\tconnected to 192.168.1.5:5555\t0\t0",
            "pair|\tSuccessfully paired to 192.168.1.5:37000\t0\t0",
        ]);
    }

    internal void Rule(string line) => File.AppendAllLines(Rules, [line]);

    internal string[] AdbCalls()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!File.Exists(AdbLog))
                    return [];
                using var file = new FileStream(AdbLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(file);
                return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            catch (IOException) when (attempt < 50)
            {
                Thread.Sleep(10);
            }
        }
    }

    public virtual void Dispose()
    {
        Ui.Run(() =>
        {
            if (Loc.Language != _language)
                Loc.Toggle();
        });
        foreach (var s in Fake.Servers)
            s.Dispose();
        Desktop.Current = _desktop;
        AppLog.Path = _log;
        WifiAddresses.FilePath = _wifi;
        AdbService.ChosenPathFile = _chosen;
        OpenOnConnect.Root = _root;
        OpenOnConnect.Executable = _exe;
        Environment.SetEnvironmentVariable("FAKE_ADB_RULES", null);
        Environment.SetEnvironmentVariable("FAKE_ADB_LOG", null);
        Registry.CurrentUser.DeleteSubKeyTree(RegistryBranch, throwOnMissingSubKey: false);
        try { Directory.Delete(Dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
