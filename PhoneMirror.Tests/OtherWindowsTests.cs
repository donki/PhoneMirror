using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using PhoneMirror.Localization;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

/// <summary>Wi-Fi, «Acerca de», elegir instancia, arranque (App), tema, bandeja y la plataforma de verdad.</summary>
public sealed class OtherWindowsTests : UiTest
{
    private static KeyEventArgs Press(System.Windows.Media.Visual target, Key key) =>
        new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };

    // ------------------------------------------------------------ Wi-Fi

    private WifiWindow Wifi(Action<WifiWindow> act)
    {
        var w = new WifiWindow(Fake.CreateAdb());
        Ui.Expect<WifiWindow>(d =>
        {
            act(d);
            if (d.IsVisible)
                d.Close();
        });
        w.ShowDialog();
        return w;
    }

    [Fact]
    public void Wifi_Vincular_Conectar_ConIntro_YOlvidar()
    {
        WifiAddresses.Remember("10.0.0.7");
        Ui.Run(() =>
        {
            var w = Wifi(d =>
            {
                var address = Ui.Find<ComboBox>(d, "AddressBox");
                Assert.Equal("10.0.0.7", address.SelectedItem);
                Ui.Click(Ui.Find<ButtonBase>(d, "ForgetButton"));
                Assert.Empty(WifiAddresses.Load());
                Ui.Click(Ui.Find<ButtonBase>(d, "ForgetButton"));   // ya vacia: nada
                Ui.Click(Ui.Find<ButtonBase>(d, "PairButton"));     // sin datos: nada
                Ui.Find<TextBox>(d, "PairAddressBox").Text = "192.168.1.5:37000";
                Ui.Find<TextBox>(d, "PairCodeBox").Text = "123456";
                Ui.Click(Ui.Find<ButtonBase>(d, "PairButton"));
                Ui.WaitFor(() => Ui.Find<TextBlock>(d, "StatusText").Text == Loc.Get("WifiPaired"));
                Assert.Equal("192.168.1.5:", address.Text);
                Assert.True(Ui.Find<ButtonBase>(d, "ConnectButton").IsEnabled);
                address.Text = "192.168.1.5:5555";
                Ui.Call(d, "OnAddressKeyDown", address, Press(address, Key.A));   // otra tecla: nada
                Ui.Call(d, "OnAddressKeyDown", address, Press(address, Key.Enter));
                Ui.WaitFor(() => !d.IsVisible);
            });
            Assert.Equal("192.168.1.5:5555", w.ConnectedAddress);
            Assert.Equal(["192.168.1.5:5555"], WifiAddresses.Load());
        });
        Assert.Contains(AdbCalls(), c => c == "pair|192.168.1.5:37000|123456");
    }

    [Fact]
    public void Wifi_SiNoConectaOVincula_LoDiceYSeQueda()
    {
        File.WriteAllLines(Rules, ["connect|\tcannot connect to 10.0.0.1:5555\t0\t0", "pair|\tFailed: wrong code\t1\t0"]);
        Ui.Run(() =>
        {
            var w = Wifi(d =>
            {
                Ui.Click(Ui.Find<ButtonBase>(d, "ConnectButton"));   // sin direccion: nada
                Ui.Find<ComboBox>(d, "AddressBox").Text = "10.0.0.1";
                Ui.Click(Ui.Find<ButtonBase>(d, "ConnectButton"));
                Ui.WaitFor(() => Ui.Find<TextBlock>(d, "StatusText").Text.StartsWith(Loc.Format("WifiFailed", "")[..5], StringComparison.Ordinal)
                    && Ui.Find<ButtonBase>(d, "ConnectButton").IsEnabled);
                Assert.Contains("cannot connect", Ui.Find<TextBlock>(d, "StatusText").Text);
                Ui.Find<TextBox>(d, "PairAddressBox").Text = "10.0.0.1:4000";
                Ui.Find<TextBox>(d, "PairCodeBox").Text = "1";
                Ui.Click(Ui.Find<ButtonBase>(d, "PairButton"));
                Ui.WaitFor(() => Ui.Find<TextBlock>(d, "StatusText").Text.Contains("wrong code", StringComparison.Ordinal));
                Ui.WaitFor(() => Ui.Find<ButtonBase>(d, "PairButton").IsEnabled);
                Assert.Equal("10.0.0.1", Ui.Find<ComboBox>(d, "AddressBox").Text);   // con direccion puesta no se toca
                Ui.Click(Ui.Find<ButtonBase>(d, "CloseButton"));
            });
            Assert.Null(w.ConnectedAddress);
            Assert.Empty(WifiAddresses.Load());
        });
    }

    // ------------------------------------------------------------ acerca de

    [Fact]
    public void AcercaDe_Version_Contacto_YCambiarDeIdiomaLaVuelveAAbrir()
    {
        Ui.Run(() =>
        {
            var owner = new Window();
            owner.Show();
            Ui.Expect<AboutWindow>(a =>
            {
                Assert.StartsWith("v", Ui.Find<TextBlock>(a, "VersionLabel").Text);
                Assert.NotNull(Ui.Find<System.Windows.Controls.Image>(a, "LogoImage").Source);
                Ui.Click(Ui.Find<ButtonBase>(a, "ContactButton"));
                Assert.Equal("mailto:jsoladelarosa@gmail.com", Fake.Started[^1].FileName);
                Fake.StartError = new InvalidOperationException("sin correo");
                Ui.Click(Ui.Find<ButtonBase>(a, "ContactButton"));
                Assert.Equal(("sin correo", Loc.Get("Contact")), Fake.Messages.Single());
                Ui.Click(Ui.Find<ButtonBase>(a, "SpanishButton"));   // ya en castellano: nada
                Ui.Expect<AboutWindow>(b =>
                {
                    Assert.Equal("en", Loc.Language);
                    Assert.Same(owner, b.Owner);
                    Ui.Click(Ui.Find<ButtonBase>(b, "SpanishButton"));
                });
                Ui.Expect<AboutWindow>(c =>
                {
                    Assert.Equal("es", Loc.Language);
                    Ui.Click(Ui.Find<ButtonBase>(c, "CloseButton"));
                });
                Ui.Click(Ui.Find<ButtonBase>(a, "EnglishButton"));
            });
            new AboutWindow { Owner = owner }.ShowDialog();
            Ui.WaitFor(() => Ui.Pending == 0);
        });
    }

    // ------------------------------------------------------------ instancias y arranque

    private static Instances.Instance Other(string title) => new(4242, IntPtr.Zero, title);

    [Fact]
    public void Instancias_Abrir_Nueva_Cancelar_YEscape()
    {
        Ui.Run(() =>
        {
            var list = new[] { Other("sOC Phone Mirror · Pixel"), Other("sOC Phone Mirror · Galaxy") };
            Ui.Expect<InstancesWindow>(w =>
            {
                var box = Ui.All<ListBox>(w).Single();
                Assert.Equal(2, box.Items.Count);
                box.SelectedIndex = 1;
                Ui.Click(Ui.All<Button>(w).Single(b => b.IsDefault));
            });
            var open = new InstancesWindow(list);
            Assert.True(open.ShowDialog());
            Assert.Equal("sOC Phone Mirror · Galaxy", open.Chosen!.Title);

            Ui.Expect<InstancesWindow>(w => Ui.Click(Ui.All<Button>(w).Single(b => b.Style == w.FindResource("OutlineButton"))));
            var fresh = new InstancesWindow(list);
            Assert.True(fresh.ShowDialog());
            Assert.Null(fresh.Chosen);

            Ui.Expect<InstancesWindow>(w => w.RaiseEvent(Press(w, Key.Escape)));
            Assert.NotEqual(true, new InstancesWindow(list).ShowDialog());

            Ui.Expect<InstancesWindow>(w =>
            {
                var box = Ui.All<ListBox>(w).Single();
                box.SelectedItem = null;
                Ui.Call(w, "Pick");   // nada elegido: sigue abierta
                Assert.True(w.IsVisible);
                box.SelectedIndex = 0;
                box.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            });
            Assert.True(new InstancesWindow(list).ShowDialog());
        });
    }

    private static App App => (App)Application.Current;

    /// <summary>Launch cambia el ShutdownMode: si se quedara en OnMainWindowClose, cerrar la ventana apagaria el arnes.</summary>
    private static void Launch(params string[] args)
    {
        try
        {
            App.Launch(args);
        }
        finally
        {
            ShutdownModeSeen = App.ShutdownMode;
            App.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }

    private static ShutdownMode ShutdownModeSeen;

    [Fact]
    public void Arranque_Normal_AbreLaVentana_ConSusOpciones()
    {
        Ui.Run(() =>
        {
            Launch("--serial", "ABC123");
            var w = (MainWindow)App.MainWindow;
            Assert.True(w.IsVisible);
            Assert.True(w.AutoConnect);
            Assert.Equal("ABC123", w.PreferredSerial);
            Assert.Equal(1, Fake.Prepared);
            Assert.Equal(ShutdownMode.OnMainWindowClose, ShutdownModeSeen);
            Ui.WaitFor(() => Ui.Field<object?>(w, "_session") is not null);
            App.OnUnexpected(new InvalidOperationException("prueba"));
            Assert.Equal(Loc.Format("UnexpectedError", "prueba"), Ui.Find<TextBlock>(w, "StatusText").Text);
        });
        Assert.Contains("error no controlado: System.InvalidOperationException: prueba", File.ReadAllText(AppLog.Path));
    }

    [Fact]
    public void Arranque_EnLaBandeja_NoSeVe()
    {
        Devices();
        Ui.Run(() =>
        {
            Launch("--tray");
            var w = (MainWindow)App.MainWindow;
            Assert.False(w.IsVisible);
            Assert.True(w.StartInTray);
            Assert.Single(Fake.Trays);
            Assert.Equal(ShutdownMode.OnExplicitShutdown, ShutdownModeSeen);
        });
    }

    [Fact]
    public void Arranque_ConOtrasAbiertas_PreguntaCual()
    {
        Fake.Others.Add(Other("sOC Phone Mirror · Pixel"));
        Devices();
        Ui.Run(() =>
        {
            Ui.Expect<InstancesWindow>(w => w.RaiseEvent(Press(w, Key.Escape)));
            Launch();
            Assert.Equal(1, Fake.Shutdowns);

            Ui.Expect<InstancesWindow>(w => Ui.Click(Ui.All<Button>(w).Single(b => b.IsDefault)));
            Launch();
            Assert.Equal(2, Fake.Shutdowns);
            Assert.Equal("sOC Phone Mirror · Pixel", Assert.Single(Fake.Shown).Title);

            Ui.Expect<InstancesWindow>(w => Ui.Click(Ui.All<Button>(w).Single(b => b.Style == w.FindResource("OutlineButton"))));
            Launch();
            Assert.True(App.MainWindow.IsVisible);
        });
    }

    // ------------------------------------------------------------ tema, bandeja, instancias de verdad

    [Fact]
    public void Tema_PintaLasSuperficies_YLaBarraDeTitulo()
    {
        Ui.Run(() =>
        {
            ThemeManager.Apply();
            var page = ((System.Windows.Media.SolidColorBrush)Application.Current.Resources["PageBackground"]).Color;
            Assert.Equal(ThemeManager.IsDark ? System.Windows.Media.Color.FromRgb(0x14, 0x13, 0x18) : System.Windows.Media.Color.FromRgb(0xF8, 0xF9, 0xFA), page);
            var w = new Window();
            ThemeManager.ApplyToWindow(w);   // sin ventana de Windows aun: nada
            new WindowInteropHelper(w).EnsureHandle();
            ThemeManager.ApplyToWindow(w);
            // y el otro tema, forzando la preferencia en una copia de los recursos
            typeof(ThemeManager).GetProperty("IsDark")!.SetValue(null, !ThemeManager.IsDark);
            ThemeManager.ApplyToWindow(w);
            ThemeManager.Apply();
        });
    }

    [Fact]
    public void Bandeja_DeVerdad_MenuAbrirYSalir_YTextoCorto()
    {
        Ui.Run(() =>
        {
            using var tray = (TrayIconHost)new RealDesktop().CreateTray("Abrir", "Salir");
            var opened = 0;
            var quit = 0;
            tray.Activated += (_, _) => opened++;
            tray.QuitRequested += (_, _) => quit++;
            tray.SetText(new string('x', 100));
            var icon = (System.Windows.Forms.NotifyIcon)typeof(TrayIconHost).GetField("_icon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tray)!;
            Assert.Equal(63, icon.Text.Length);
            icon.ContextMenuStrip!.Items[0].PerformClick();
            icon.ContextMenuStrip.Items[2].PerformClick();
            Assert.Equal((1, 1), (opened, quit));
            typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(icon, [new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 0, 0, 0)]);
            typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(icon, [new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Right, 1, 0, 0, 0)]);
            Assert.Equal(2, opened);
        });
    }

    [Fact]
    public void Instancias_DeVerdad_LasDemasYEnseñarUna()
    {
        Devices();
        Ui.Run(() =>
        {
            var real = new RealDesktop();
            Assert.DoesNotContain(real.OtherInstances(), i => i.ProcessId == Environment.ProcessId);
            var w = new MainWindow();
            w.Show();
            w.Hide();
            Instances.Show(new Instances.Instance(Environment.ProcessId, new WindowInteropHelper(w).Handle, w.Title));
            Ui.WaitFor(() => w.IsVisible);
        });
    }

    [Fact]
    public void PlataformaDeVerdad_LoQueNoTocaNada()
    {
        var real = new RealDesktop();
        Assert.NotNull(real.CreateAdb());
        var adb = new AdbService { ExecutablePath = FakeDesktop.FakeAdbPath };
        Assert.InRange(real.CreateSession(adb, "X").LocalPort, 27183, 27283);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Phone Mirror"), real.ScreenshotFolder);
        Assert.False(real.EnsureAdbOnPath());   // aqui no hay adb empaquetado
    }

    private static byte[] Zip(params string[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in entries)
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open()))
                    writer.Write(name);
        return memory.ToArray();
    }

    [Fact]
    public async Task InstalarAdb_BajaElZip_YLoDejaEnSuCarpeta()
    {
        var folder = AdbInstaller.Folder;
        var download = AdbInstaller.Download;
        try
        {
            AdbInstaller.Folder = Path.Combine(Dir, "pt");
            string? asked = null;
            AdbInstaller.Download = (url, _) =>
            {
                asked = url;
                return Task.FromResult(Zip("platform-tools/adb.exe", "platform-tools/AdbWinApi.dll", "platform-tools/AdbWinUsbApi.dll"));
            };
            var progress = new List<string>();
            var path = await AdbInstaller.InstallAsync(new SyncProgress(progress.Add));
            Assert.Equal(AdbInstaller.DownloadUrl, asked);
            Assert.Equal([AdbInstaller.DownloadUrl], progress);
            Assert.Equal(Path.Combine(Dir, "pt", "adb.exe"), path);
            Assert.True(File.Exists(path));
            Assert.Equal(Path.Combine(Dir, "pt"), await new RealDesktop().InstallAdbAsync());
        }
        finally
        {
            AdbInstaller.Folder = folder;
            AdbInstaller.Download = download;
        }
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    [Fact]
    public void AdbEnElPath_SeAñadeUnaVez_YNoPisaOtroAdb()
    {
        var root = BundledAdb.Root;
        var broadcast = BundledAdb.Broadcast;
        var broadcasts = 0;
        var folder = BundledAdb.Folder;
        Directory.CreateDirectory(folder);
        File.WriteAllText(BundledAdb.ExecutablePath, "adb de mentira");
        try
        {
            BundledAdb.Root = () => Registry.CurrentUser.OpenSubKey(RegistryBranch, writable: true)!;
            BundledAdb.Broadcast = () => broadcasts++;
            Assert.False(BundledAdb.EnsureOnUserPath());   // sin clave Environment
            using (var env = Registry.CurrentUser.CreateSubKey(RegistryBranch + @"\Environment"))
                env.SetValue("Path", @"C:\Windows;%SystemRoot%\system32", RegistryValueKind.ExpandString);
            Assert.True(BundledAdb.EnsureOnUserPath());
            Assert.Equal(1, broadcasts);
            using (var env = Registry.CurrentUser.OpenSubKey(RegistryBranch + @"\Environment")!)
                Assert.EndsWith(";" + folder.TrimEnd('\\'), (string)env.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)!);
            Assert.False(BundledAdb.EnsureOnUserPath());   // ya esta
            using (var env = Registry.CurrentUser.CreateSubKey(RegistryBranch + @"\Environment"))
                env.SetValue("Path", Path.GetDirectoryName(FakeDesktop.FakeAdbPath) + "x;" + folder.Replace("platform-tools", "otra"), RegistryValueKind.ExpandString);
            var other = folder.Replace("platform-tools", "otra");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "adb.exe"), "otro adb");
            Assert.False(BundledAdb.EnsureOnUserPath());   // hay otro adb en el PATH: no se pisa
            Directory.Delete(other, recursive: true);
            BundledAdb.Root = () => throw new UnauthorizedAccessException();
            Assert.False(BundledAdb.EnsureOnUserPath());
            Assert.Equal(1, broadcasts);
        }
        finally
        {
            BundledAdb.Root = root;
            BundledAdb.Broadcast = broadcast;
            Directory.Delete(folder, recursive: true);
        }
    }
}
