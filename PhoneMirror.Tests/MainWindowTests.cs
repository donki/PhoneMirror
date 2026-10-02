using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using PhoneMirror.Localization;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

/// <summary>
/// La ventana principal de verdad con un adb de mentira y un servidor de scrcpy de mentira en
/// loopback: conectar, ver video, mandar toques y teclas, botones, bandeja, Wi-Fi, arrastrar.
/// </summary>
public sealed class MainWindowTests : UiTest
{
    private static string Status(MainWindow w) => Ui.Find<TextBlock>(w, "StatusText").Text;

    private static ButtonBase Button(MainWindow w, string name) => Ui.Find<ButtonBase>(w, name);

    private static bool Connected(MainWindow w) => Ui.Field<object?>(w, "_session") is not null;

    /// <summary>Abre la ventana y espera a que se conecte sola con el movil de las reglas.</summary>
    private MainWindow Open(Action<MainWindow>? before = null)
    {
        var w = new MainWindow();
        before?.Invoke(w);
        w.Show();
        Ui.WaitFor(() => Connected(w));
        return w;
    }

    private byte[] WaitControl(int count)
    {
        Ui.WaitFor(() => Fake.Server.ControlBytes().Length >= count);
        var bytes = Fake.Server.ControlBytes();
        Fake.Server.ClearControl();
        return bytes;
    }

    private void Video(MainWindow w, int frames = 2)
    {
        Fake.Server.Session(64, 64);
        Fake.Server.Packet(H264Sample.Config(64, 64), config: true);
        for (var i = 0; i < frames; i++)
            Fake.Server.Packet(H264Sample.Frame(64, 64, 81, 90, 240, i % 2), pts: i * 16_000);
        Ui.WaitFor(() => Ui.Find<System.Windows.Controls.Image>(w, "Mirror").Source is not null);
    }

    [Fact]
    public void AlAbrir_ConUnMovil_SeConectaSolo_YLosBotonesSeEncienden()
    {
        Ui.Run(() =>
        {
            var w = Open();
            Ui.WaitFor(() => Status(w).StartsWith("Pixel de prueba", StringComparison.Ordinal));
            Assert.Equal(Visibility.Collapsed, Ui.Find<FrameworkElement>(w, "Placeholder").Visibility);
            Assert.Equal(Visibility.Visible, Button(w, "DisconnectButton").Visibility);
            Assert.True(Button(w, "HomeButton").IsEnabled);
            Assert.False(Button(w, "RefreshButton").IsEnabled);
            Assert.Contains("Pixel 7 · ABC123", w.Title);
            Assert.Equal(1, Fake.Trays.Count);
            Assert.Contains(AdbCalls(), c => c.Contains("push|", StringComparison.Ordinal));
            Assert.Contains(AdbCalls(), c => c.Contains("forward|", StringComparison.Ordinal));
            Assert.Contains("adb: ", File.ReadAllText(AppLog.Path));
        });
    }

    [Fact]
    public void Video_LlegaYSePinta_YElTituloLlevaElMovil()
    {
        Ui.Run(() =>
        {
            var w = Open();
            Video(w, frames: 3);
            var bitmap = (System.Windows.Media.Imaging.WriteableBitmap)Ui.Find<System.Windows.Controls.Image>(w, "Mirror").Source;
            Assert.Equal(64, bitmap.PixelWidth);
            Ui.WaitFor(() => w.Title.Contains("Pixel de prueba · ABC123", StringComparison.Ordinal));
            Assert.Equal(Loc.Format("Connected", "Pixel de prueba", 64, 64), Status(w));
            // contador de cuadros por segundo: pasado un segundo, se enseña
            typeof(MainWindow).GetField("_fpsSince", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(w, DateTime.UtcNow.AddSeconds(-2));
            Fake.Server.Packet(H264Sample.Frame(64, 64, 41, 240, 110, 1), pts: 99_000);
            Ui.WaitFor(() => Ui.Find<TextBlock>(w, "FpsText").Text.EndsWith("fps", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Botones_MandanSusMensajesAlMovil()
    {
        Ui.Run(() =>
        {
            var w = Open();
            Ui.WaitFor(() => Button(w, "BackButton").IsEnabled);
            Ui.Click(Button(w, "BackButton"));
            Assert.Equal(new byte[] { 4, 0, 4, 1 }, WaitControl(4));
            Ui.Click(Button(w, "HomeButton"));
            Assert.Equal(0, WaitControl(28)[0]);   // tecla abajo y arriba
            Ui.Click(Button(w, "RecentsButton"));
            WaitControl(28);
            Ui.Click(Button(w, "NotificationsButton"));
            Assert.Equal(new byte[] { 5 }, WaitControl(1));
            Ui.Click(Button(w, "PowerButton"));
            Assert.Equal(new byte[] { 10, 0 }, WaitControl(2));   // apaga la pantalla
            Ui.Click(Button(w, "PowerButton"));
            Assert.Equal(new byte[] { 10, 1 }, WaitControl(2));   // y la enciende
            Ui.Click(Button(w, "RotateButton"));
            Assert.Equal(new byte[] { 11 }, WaitControl(1));
            Ui.Click(Button(w, "VolumeUpButton"));
            WaitControl(28);
            Ui.Click(Button(w, "VolumeDownButton"));
            WaitControl(28);
            Ui.Click(Button(w, "MuteButton"));
            WaitControl(28);
            Ui.Click(Button(w, "CopyButton"));
            Assert.Equal(new byte[] { 8, 0 }, WaitControl(2));
            Fake.Clipboard = "texto del PC";
            Ui.Click(Button(w, "PasteButton"));
            var paste = WaitControl(14 + "texto del PC".Length);
            Assert.Equal(9, paste[0]);
            Assert.Contains("texto del PC", System.Text.Encoding.UTF8.GetString(paste));
            Fake.Clipboard = null;                      // portapapeles vacio: nada
            Ui.Click(Button(w, "PasteButton"));
            Fake.ClipboardThrows = true;                // ocupado por otro programa: nada
            Ui.Click(Button(w, "PasteButton"));
            Ui.DoEvents();
            Assert.Empty(Fake.Server.ControlBytes());
        });
    }

    [Fact]
    public void Raton_Toques_Arrastre_Rueda_YBotonesDeAtrasEInicio()
    {
        Ui.Run(() =>
        {
            var w = Open();
            Video(w);
            var area = Ui.Find<FrameworkElement>(w, "MirrorArea");
            var center = new Point(area.ActualWidth / 2, area.ActualHeight / 2);
            void Wait(Task t) => Ui.WaitFor(() => t.IsCompleted);

            Wait(w.MouseDownAsync(MouseButton.Left, center));
            var down = WaitControl(32);
            Assert.Equal(2, down[0]);   // toque
            Assert.Equal(0, down[1]);   // abajo, antes que ningun movimiento
            Ui.DoEvents();
            Thread.Sleep(50);
            Ui.DoEvents();
            Fake.Server.ClearControl();   // capturar el raton puede mandar un movimiento: fuera
            Wait(w.MouseMoveAsync(new Point(center.X + 5, center.Y)));
            Assert.Equal(2, WaitControl(32)[1]);   // movimiento
            Wait(w.MouseMoveAsync(new Point(-500, -500)));   // fuera: se pega al borde
            WaitControl(32);
            Wait(w.MouseUpAsync(MouseButton.Left, new Point(-500, -500)));
            Assert.Equal(1, WaitControl(32)[1]);   // arriba
            Wait(w.MouseUpAsync(MouseButton.Left, center));   // ya no esta tocando: nada
            Wait(w.MouseMoveAsync(center));
            Wait(w.MouseDownAsync(MouseButton.Left, new Point(-500, -500)));   // fuera de la imagen: nada
            Wait(w.MouseDownAsync(MouseButton.XButton1, center));
            Ui.DoEvents();
            Assert.Empty(Fake.Server.ControlBytes());

            Wait(w.MouseDownAsync(MouseButton.Right, center));
            Assert.Equal(new byte[] { 4, 0, 4, 1 }, WaitControl(4));
            Wait(w.MouseDownAsync(MouseButton.Middle, center));
            WaitControl(28);

            Wait(w.WheelAsync(120, center, ModifierKeys.None));
            var wheel = WaitControl(21);
            Assert.Equal(3, wheel[0]);
            Wait(w.WheelAsync(-240, center, ModifierKeys.Shift));
            WaitControl(21);
            Wait(w.WheelAsync(120, new Point(-500, -500), ModifierKeys.None));
            Ui.DoEvents();
            Assert.Empty(Fake.Server.ControlBytes());
        });
    }

    [Fact]
    public void Teclado_TeclasTextoYPortapapeles()
    {
        Assert.True(MainWindow.WillHandleKey(Key.V, ModifierKeys.Control));
        Assert.True(MainWindow.WillHandleKey(Key.Enter, ModifierKeys.None));
        Assert.False(MainWindow.WillHandleKey(Key.LeftShift, ModifierKeys.None));
        Assert.True(MainWindow.IsRealText("hola"));
        Assert.False(MainWindow.IsRealText("\t"));
        Assert.False(MainWindow.IsRealText(null));
        Ui.Run(() =>
        {
            var w = Open();
            void Wait(Task t) => Ui.WaitFor(() => t.IsCompleted);
            Wait(w.KeyDownAsync(Key.Enter, repeat: true, ModifierKeys.Shift));
            var key = WaitControl(14);
            Assert.Equal(0, key[0]);
            Assert.Equal(0, key[1]);    // abajo
            Assert.Equal(1, key[9]);    // repetida
            Wait(w.KeyUpAsync(Key.Enter, ModifierKeys.None));
            Assert.Equal(1, WaitControl(14)[1]);   // tecla arriba
            Wait(w.KeyUpAsync(Key.LeftShift, ModifierKeys.None));   // sin tecla de Android: nada
            Wait(w.KeyDownAsync(Key.C, false, ModifierKeys.Control));
            Assert.Equal(new byte[] { 8, 1 }, WaitControl(2));
            Fake.Clipboard = "x";
            Wait(w.KeyDownAsync(Key.V, false, ModifierKeys.Control));
            Assert.Equal(9, WaitControl(15)[0]);
            Wait(w.KeyDownAsync(Key.Tab, false, ModifierKeys.Control));   // Ctrl+Tab: tecla con meta
            Assert.Contains(WaitControl(14)[10..14], b => b != 0);   // el meta de Ctrl
            Wait(w.TextAsync("ñ"));
            var text = WaitControl(7);
            Assert.Equal(1, text[0]);
            Wait(w.TextAsync("\r"));
            Ui.DoEvents();
            Assert.Empty(Fake.Server.ControlBytes());
        });
    }

    [Fact]
    public void ElMovil_MandaSuPortapapeles_YSeCopiaAlPC()
    {
        Ui.Run(() =>
        {
            var w = Open();
            Fake.Server.Clipboard("copiado en el móvil");
            Ui.WaitFor(() => Fake.Clipboard == "copiado en el móvil");
            Ui.WaitFor(() => Status(w) == Loc.Get("ClipboardCopied"));
            Fake.ClipboardThrows = true;
            Fake.Server.Clipboard("otro");
            Ui.DoEvents();
            Thread.Sleep(100);
            Ui.DoEvents();
        });
    }

    [Fact]
    public void Captura_GuardaUnPng_YSinVideoNoHaceNada()
    {
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Click(Button(w, "ScreenshotButton"));   // aun sin cuadros
            Assert.False(Directory.Exists(Fake.ScreenshotFolder));
            Video(w);
            Ui.Click(Button(w, "ScreenshotButton"));
            var png = Assert.Single(Directory.GetFiles(Fake.ScreenshotFolder, "*.png"));
            Assert.Equal(Loc.Format("ScreenshotSaved", png), Status(w));
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, File.ReadAllBytes(png)[..4]);
        });
    }

    [Fact]
    public void Desconectar_AManoNoVuelveAConectarSolo_HastaQueCambiaElMovil()
    {
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Click(Button(w, "DisconnectButton"));
            Ui.WaitFor(() => !Connected(w) && Status(w) == Loc.Get("Disconnected"));
            Ui.WaitFor(() => Fake.Server.Closed);
            Assert.Equal(Visibility.Visible, Button(w, "ConnectButton").Visibility);
            Assert.Equal(Loc.Get("DropHint"), Ui.Find<TextBlock>(w, "PlaceholderText").Text);
            Ui.Click(Button(w, "RefreshButton"));
            Ui.DoEvents();
            Assert.False(Connected(w));
            Assert.Single(Fake.Servers);
            Devices(("ABC123", "device", "Pixel_7"), ("XYZ", "device", "Galaxy"));
            Ui.Click(Button(w, "RefreshButton"));   // aparece otro: vuelve a conectar
            Ui.WaitFor(() => Connected(w));
            Assert.Equal(2, Fake.Servers.Count);
        });
    }

    [Fact]
    public void ElegirOtroMovil_ConSesion_CambiaDeSesion()
    {
        Devices(("ABC123", "device", "Pixel_7"), ("XYZ", "device", "Galaxy"));
        Ui.Run(() =>
        {
            var w = Open();
            var box = Ui.Find<ComboBox>(w, "DeviceBox");
            box.SelectedIndex = 1;
            Ui.WaitFor(() => Fake.Servers.Count == 2 && Connected(w) && Fake.Servers[0].Closed);
            Assert.Contains(AdbCalls(), c => c.StartsWith("-s|XYZ|push", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ElMovilSeVa_LaSesionTermina_YLoDice()
    {
        Ui.Run(() =>
        {
            var w = Open();
            Fake.Server.Hangup();
            Ui.WaitFor(() => !Connected(w));
            Ui.WaitFor(() => Status(w).StartsWith(Loc.Format("SessionEnded", "")[..6], StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ElMovilSeVa_EnModoBandeja_LaVentanaSeEsconde()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow { StartInTray = true };
            w.Show();
            Ui.WaitFor(() => Connected(w));
            Fake.Server.BrokenPacket();   // paquete roto: la sesion termina
            Ui.WaitFor(() => !Connected(w) && !w.IsVisible);
        });
    }

    [Fact]
    public void Conectar_ConUnCodecQueNoEsH264_FallaYLoDice()
    {
        Fake.Codec = 0x68323635;   // h265
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            Ui.WaitFor(() => Status(w).Contains("0x68323635", StringComparison.Ordinal));
            Assert.False(Connected(w));
            Assert.True(Button(w, "ConnectButton").IsEnabled);
        });
    }

    [Fact]
    public void Conectar_SiElServidorSeMuereAntes_LoDice()
    {
        Fake.ServerAcceptsVideo = false;
        Devices(("ABC123", "device", "Pixel_7"));
        File.WriteAllLines(Rules, File.ReadAllLines(Rules).Where(l => !l.StartsWith("app_process", StringComparison.Ordinal)));
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            Ui.WaitFor(() => Status(w).Contains("ha terminado", StringComparison.Ordinal) || Status(w).Contains("finished", StringComparison.Ordinal));
            Assert.False(Connected(w));
        });
    }

    [Fact]
    public void SinMovil_YSinAutorizar_LoExplica()
    {
        Devices();
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            Ui.WaitFor(() => w.Title.Contains(Loc.Get("TitleNoPhone"), StringComparison.Ordinal));
            Assert.Equal(Loc.Get("NoDevices"), Ui.Find<TextBlock>(w, "PlaceholderText").Text);
            Assert.Equal(Visibility.Visible, Ui.Find<TextBlock>(w, "PlaceholderHint").Visibility);
            Devices(("ABC123", "unauthorized", ""));
            Ui.Click(Button(w, "RefreshButton"));
            Ui.WaitFor(() => Ui.Find<TextBlock>(w, "PlaceholderText").Text == Loc.Get("Unauthorized"));
            Assert.False(Button(w, "ConnectButton").IsEnabled);
            Ui.Click(Button(w, "ConnectButton") is { IsEnabled: true } b ? b : Button(w, "RefreshButton"));
        });
    }

    [Fact]
    public void SiAdbFalla_ElErrorSaleEnLaBarra()
    {
        File.WriteAllLines(Rules, ["devices\t\t1\t0"]);
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            Ui.WaitFor(() => Status(w).StartsWith("adb devices", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void SinAdb_LoBaja_YLuegoSigue()
    {
        Fake.AdbAvailable = false;
        Fake.AdbOnPath = true;
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            Ui.WaitFor(() => Connected(w));
            Assert.Equal(1, Fake.Installs);
            Assert.Contains(Loc.Format("AdbInstalled", Path.GetDirectoryName(FakeDesktop.FakeAdbPath)!), File.ReadAllText(AppLog.Path));
            Assert.Contains(Loc.Format("AdbOnPath", BundledAdb.Folder), File.ReadAllText(AppLog.Path));
        });
    }

    [Fact]
    public void SinAdb_SiNoSePuedeBajar_LoDice_YSePuedeSeñalarUnoAMano()
    {
        Fake.AdbAvailable = false;
        Fake.InstallError = new System.Net.Http.HttpRequestException("sin red");
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            Ui.WaitFor(() => File.Exists(AppLog.Path) && File.ReadAllText(AppLog.Path).Contains(Loc.Format("AdbDownloadFailed", "sin red"), StringComparison.Ordinal)
                && !Ui.Field<bool>(w, "_installingAdb"));
            Assert.Equal(Loc.Get("NoAdb"), Ui.Find<TextBlock>(w, "PlaceholderText").Text);
            Assert.Equal(Visibility.Visible, Ui.Find<FrameworkElement>(w, "AdbActions").Visibility);
            Ui.Click(Button(w, "LocateAdbButton"));   // cancelado: nada
            Fake.AdbToPick = FakeDesktop.FakeAdbPath;
            Ui.Click(Button(w, "LocateAdbButton"));
            Ui.WaitFor(() => Connected(w));
            Assert.Equal(FakeDesktop.FakeAdbPath, File.ReadAllText(AdbService.ChosenPathFile));
        });
    }

    [Fact]
    public void SinAdb_ElBotonDeDescargar_LoVuelveAIntentar()
    {
        Fake.AdbAvailable = false;
        Fake.InstallError = new System.Net.Http.HttpRequestException("sin red");
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            Ui.WaitFor(() => Fake.Installs == 1 && !Ui.Field<bool>(w, "_installingAdb"));
            Fake.InstallError = null;
            Ui.Click(Button(w, "DownloadAdbButton"));
            Ui.WaitFor(() => Connected(w));
        });
    }

    [Fact]
    public void ConexionAutomatica_ConNumeroDeSerie_EligeEseMovil()
    {
        Devices(("ABC123", "device", "Pixel_7"), ("XYZ", "device", "Galaxy"));
        Ui.Run(() =>
        {
            var w = new MainWindow { AutoConnect = true, PreferredSerial = "XYZ" };
            w.RunInTray();   // escondida: solo el --connect la conecta
            Ui.WaitFor(() => Connected(w));
            Assert.Contains(AdbCalls(), c => c.StartsWith("-s|XYZ|push", StringComparison.Ordinal));
            Assert.False(w.IsVisible);
        });
    }

    [Fact]
    public void AbrirAlConectar_SeGuardaEnElArranque_YEnseñaLaVentanaConUnMovil()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow();
            Assert.False(Ui.Find<ToggleButton>(w, "OpenOnConnectButton").IsChecked);
            Ui.Click(Ui.Find<ToggleButton>(w, "OpenOnConnectButton"));
            Assert.True(OpenOnConnect.IsEnabled);
            Assert.Equal(Loc.Get("OpenOnConnectOn"), Ui.Find<TextBlock>(w, "StatusText").Text);
            w.RunInTray();
            Ui.WaitFor(() => Connected(w) && w.IsVisible);   // el movil la abre
            Ui.Click(Ui.Find<ToggleButton>(w, "OpenOnConnectButton"));
            Assert.False(OpenOnConnect.IsEnabled);
            Assert.Equal(Loc.Get("OpenOnConnectOff"), Ui.Find<TextBlock>(w, "StatusText").Text);
        });
    }

    [Fact]
    public void Bandeja_Minimizar_Cerrar_Abrir_YSalir()
    {
        Ui.Run(() =>
        {
            var w = Open();
            var tray = Fake.Trays[0];
            w.WindowState = WindowState.Minimized;
            Ui.WaitFor(() => !w.IsVisible);
            Assert.Single(tray.Balloons);
            tray.Activate();
            Ui.WaitFor(() => w.IsVisible);
            Assert.Equal(WindowState.Normal, w.WindowState);
            w.Close();   // cerrar es esconder (y desconectar)
            Ui.WaitFor(() => !w.IsVisible && !Connected(w));
            Assert.False(Ui.IsClosed(w));
            Assert.Single(tray.Balloons);   // el globo, solo la primera vez
            tray.Quit();
            Ui.WaitFor(() => Ui.IsClosed(w));
            Assert.Equal(1, Fake.Shutdowns);
            Assert.True(tray.Disposed);
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [Fact]
    public void OtraInstancia_PideQueSeEnseñe()
    {
        Devices();
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            w.Hide();
            var hwnd = new WindowInteropHelper(w).Handle;
            SendMessage(hwnd, Instances.ShowMessage, 0, 0);
            Ui.WaitFor(() => w.IsVisible);
            SendMessage(hwnd, 0x0400, 0, 0);   // otro mensaje: nada
        });
    }

    [Fact]
    public void Arrastrar_ApkSeInstala_LoDemasVaADownload_YLosFallosSeDicen()
    {
        Rule("install|\tFailure [INSTALL_FAILED]\t1\t0");
        Ui.Run(() =>
        {
            var w = new MainWindow();
            Assert.Equal(DragDropEffects.None, w.DropEffect(new DataObject(DataFormats.FileDrop, new[] { "a.txt" })));
            w.Show();
            Ui.WaitFor(() => Connected(w));
            Assert.Equal(DragDropEffects.Copy, w.DropEffect(new DataObject(DataFormats.FileDrop, new[] { "a.txt" })));
            Assert.Equal(DragDropEffects.None, w.DropEffect(new DataObject(DataFormats.Text, "hola")));
            var drop = w.DropFilesAsync([Path.Combine(Dir, "foto.jpg"), Path.Combine(Dir, "app.apk")]);
            Ui.WaitFor(() => drop.IsCompleted);
            Assert.Contains(AdbCalls(), c => c.EndsWith("|/sdcard/Download/foto.jpg", StringComparison.Ordinal));
            Assert.StartsWith(Loc.Format("DropFailed", "app.apk", "")[..10], Status(w));
            var log = File.ReadAllText(AppLog.Path);
            Assert.Contains(Loc.Format("Copied", "foto.jpg"), log);
        });
    }

    [Fact]
    public void Arrastrar_SinSesion_NoHaceNada_YConApkBienSeInstala()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow();
            var nothing = w.DropFilesAsync(["x.apk"]);
            Assert.True(nothing.IsCompleted);
            w.Show();
            Ui.WaitFor(() => Connected(w));
            var drop = w.DropFilesAsync([Path.Combine(Dir, "app.apk")]);
            Ui.WaitFor(() => drop.IsCompleted);
            Assert.Equal(Loc.Format("Installed", "app.apk"), Status(w));
        });
    }

    [Fact]
    public void WiFi_ConectaYEligeEseMovil()
    {
        Ui.Run(() =>
        {
            var w = Open();
            Ui.Click(Button(w, "DisconnectButton"));
            Ui.WaitFor(() => Button(w, "WifiButton").IsEnabled);
            Devices(("ABC123", "device", "Pixel_7"), ("192.168.1.5:5555", "device", "Tablet"));
            Ui.Expect<WifiWindow>(d =>
            {
                Ui.Find<ComboBox>(d, "AddressBox").Text = "192.168.1.5";
                Ui.Click(Ui.Find<ButtonBase>(d, "ConnectButton"));
            });
            Ui.Click(Button(w, "WifiButton"));
            Ui.WaitFor(() => File.ReadAllText(AppLog.Path).Contains(Loc.Format("WifiConnected", "192.168.1.5"), StringComparison.Ordinal));
            Assert.Contains(Ui.Find<ComboBox>(w, "DeviceBox").Items.Cast<AdbDevice>(), d => d.Serial == "192.168.1.5:5555");
            Assert.Equal(["192.168.1.5"], WifiAddresses.Load());
        });
    }

    [Fact]
    public void WiFi_LasDireccionesRecordadas_SeVuelvenALlamarAlAbrir()
    {
        WifiAddresses.Remember("10.0.0.9");
        WifiAddresses.Remember("192.168.1.5");
        Rule("connect|10.0.0.9\tcannot connect\t0\t0");
        File.WriteAllLines(Rules, File.ReadAllLines(Rules).Reverse());   // las mas concretas primero
        Ui.Run(() =>
        {
            var w = Open();
            Ui.WaitFor(() => AdbCalls().Count(c => c.StartsWith("connect|", StringComparison.Ordinal)) >= 2);
        });
    }

    [Fact]
    public void Idioma_SiempreEncima_AcercaDe_YErrores()
    {
        Devices();
        Ui.Run(() =>
        {
            var w = new MainWindow();
            w.Show();
            Ui.Click(Button(w, "LanguageButton"));
            Assert.Equal("en", Loc.Language);
            Assert.Equal(Loc.Get("HomeTooltip"), Button(w, "HomeButton").ToolTip);
            Ui.Click(Button(w, "LanguageButton"));
            Ui.Click(Ui.Find<ToggleButton>(w, "TopmostButton"));
            Assert.True(w.Topmost);
            Ui.Click(Ui.Find<ToggleButton>(w, "TopmostButton"));
            Assert.False(w.Topmost);
            Ui.Expect<AboutWindow>(a => a.Close());
            Ui.Click(Button(w, "AboutButton"));
            w.ShowError("roto");
            Assert.Equal(Loc.Format("UnexpectedError", "roto"), Status(w));
        });
    }
}
