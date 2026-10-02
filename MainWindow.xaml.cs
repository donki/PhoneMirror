using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PhoneMirror.Localization;
using PhoneMirror.Services;

namespace PhoneMirror;

/// <summary>
/// La ventana: la barra de botones, el espejo y una linea de estado. Todo lo que hace es
/// traducir el raton y el teclado a mensajes del canal de control y pintar los cuadros que llegan.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AdbService _adb = Desktop.Current.CreateAdb();
    private readonly DispatcherTimer _deviceTimer;

    private ScrcpySession? _session;
    private WriteableBitmap? _bitmap;
    private bool _touching;
    private int _fpsCount;
    private DateTime _fpsSince = DateTime.UtcNow;
    private bool _screenOn = true;

    /// <summary>Conectar con el primer movil listo en cuanto aparezca (opcion --connect).</summary>
    public bool AutoConnect { get; init; }

    /// <summary>Numero de serie del movil que se quiere (opcion --serial); sin el, el primero listo.</summary>
    public string? PreferredSerial { get; init; }

    private bool _autoConnected;

    /// <summary>
    /// El usuario ha pulsado Desconectar: no se vuelve a conectar solo con ese movil hasta que se
    /// desenchufe y vuelva, o hasta que aparezca otro. Sin esto, desconectar a mano no serviria de nada.
    /// </summary>
    private bool _userDisconnected;
    private string _lastReadySerials = string.Empty;

    /// <summary>Arrancar escondida en la bandeja y enseñarse al enchufar un movil (opcion --tray).</summary>
    public bool StartInTray { get; init; }

    private ITrayIcon? _tray;
    private bool _quitting;
    private bool _loadingToggles;

    public MainWindow()
    {
        InitializeComponent();

        ApplyTexts();
        Loc.LanguageChanged += ApplyTexts;
        Closed += (_, _) => Loc.LanguageChanged -= ApplyTexts;

        // Cada pocos segundos se mira si ha aparecido un movil, para no tener que pulsar nada al
        // enchufarlo. Solo mientras no hay sesion: con el espejo en marcha no hace falta.
        _deviceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _deviceTimer.Tick += async (_, _) => { if (_session is null) await RefreshDevicesAsync(); };

        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        Closed += (_, _) =>
        {
            _closed = true;
            _deviceTimer.Stop();
        };
        Loaded += async (_, _) =>
        {
            MirrorArea.Focus();
            await EnsureAdbAsync();
            AppLog.Write($"adb: {_adb.ExecutablePath ?? "(no)"}{(BundledAdb.IsPackaged ? " · MSIX" : "")} · assets en {BundledAssets.Root}");
            if (BundledAssets.Updated.Count > 0)
                SetStatus(Loc.Format("AssetsUpdated", BundledAssets.Updated.Count, BundledAssets.Root));
            // adb va dentro del paquete: que valga tambien desde una consola (PATH del usuario).
            if (Desktop.Current.EnsureAdbOnPath())
                SetStatus(Loc.Format("AdbOnPath", BundledAdb.Folder));
            await RefreshDevicesAsync();
            _deviceTimer.Start();

            // Los moviles que se conectaron por Wi-Fi otras veces: se les vuelve a llamar en
            // segundo plano y el que este encendido aparece solo en la lista. No se espera a los
            // que no contesten (adb tarda unos segundos en darlos por perdidos).
            _ = ReconnectRememberedAsync();
        };
        // La aplicacion vive en la bandeja: cerrar o minimizar la ventana la esconde y el icono se
        // queda esperando; salir de verdad es «Salir» en el menu del icono.
        ShowTrayIcon();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && _tray is not null && !_quitting)
                HideToTray();
        };

        // Otra instancia recien arrancada pide que esta se enseñe (Instances.Show).
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
                source.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
                {
                    if (msg == (int)Instances.ShowMessage)
                    {
                        Dispatcher.BeginInvoke(ShowFromTray);
                        handled = true;
                    }
                    return IntPtr.Zero;
                });
        };
        Closing += async (_, e) =>
        {
            // Cerrar la ventana es esconderla: la aplicacion sigue esperando al siguiente movil.
            // Salir de verdad se hace desde el menu del icono.
            if (_tray is not null && !_quitting)
            {
                e.Cancel = true;
                await DisconnectAsync();
                HideToTray();
                return;
            }

            await DisconnectAsync();
            _tray?.Dispose();
        };

        _loadingToggles = true;
        OpenOnConnectButton.IsChecked = OpenOnConnect.IsEnabled;
        _loadingToggles = false;


        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        TextInput += OnTextInput;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    // =====================================================================
    //  Textos y estado
    // =====================================================================

    private void ApplyTexts()
    {
        Title = Loc.Get("AppTitle");
        RefreshButton.ToolTip = Loc.Get("RefreshTooltip");
        WifiButton.ToolTip = Loc.Get("WifiTooltip");
        ConnectButton.ToolTip = Loc.Get("ConnectTooltip");
        DisconnectButton.ToolTip = Loc.Get("DisconnectTooltip");
        BackButton.ToolTip = Loc.Get("BackTooltip");
        HomeButton.ToolTip = Loc.Get("HomeTooltip");
        RecentsButton.ToolTip = Loc.Get("RecentsTooltip");
        NotificationsButton.ToolTip = Loc.Get("NotificationsTooltip");
        PowerButton.ToolTip = Loc.Get("PowerTooltip");
        RotateButton.ToolTip = Loc.Get("RotateTooltip");
        VolumeDownButton.ToolTip = Loc.Get("VolumeDownTooltip");
        VolumeUpButton.ToolTip = Loc.Get("VolumeUpTooltip");
        MuteButton.ToolTip = Loc.Get("MuteTooltip");
        ScreenshotButton.ToolTip = Loc.Get("ScreenshotTooltip");
        CopyButton.ToolTip = Loc.Get("CopyTooltip");
        PasteButton.ToolTip = Loc.Get("PasteTooltip");
        TopmostButton.ToolTip = Loc.Get("AlwaysOnTopTooltip");
        OpenOnConnectButton.ToolTip = Loc.Get("OpenOnConnectTooltip");
        _tray?.SetText(Loc.Get("TrayWaiting"));
        LanguageButton.ToolTip = Loc.Get("LanguageTooltip");
        AboutButton.ToolTip = Loc.Get("AboutTooltip");
        UpdatePlaceholder();
    }

    private void UpdatePlaceholder()
    {
        if (_session is not null)
        {
            Placeholder.Visibility = Visibility.Collapsed;
            return;
        }

        Placeholder.Visibility = Visibility.Visible;
        PlaceholderText.Text = !_adb.IsAvailable ? Loc.Get("NoAdb")
            : DeviceBox.Items.Count == 0 ? Loc.Get("NoDevices")
            : DeviceBox.SelectedItem is AdbDevice { State: "unauthorized" } ? Loc.Get("Unauthorized")
            : Loc.Get("DropHint");
        AdbActions.Visibility = !_adb.IsAvailable && !_installingAdb ? Visibility.Visible : Visibility.Collapsed;

        // Sin movil o sin autorizar: recordar que hace falta la depuracion USB y, en Xiaomi/Redmi/
        // POCO (HyperOS/MIUI), ademas «Depuracion USB (ajustes de seguridad)», que es lo que deja
        // que el raton y el teclado del PC manejen el movil. Sin eso se ve la pantalla pero no responde.
        var needsHint = _adb.IsAvailable && (DeviceBox.Items.Count == 0 || DeviceBox.SelectedItem is AdbDevice { State: "unauthorized" });
        PlaceholderHint.Text = Loc.Get("DebuggingHint");
        PlaceholderHint.Visibility = needsHint ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnDownloadAdbClick(object sender, RoutedEventArgs e)
    {
        _adb.Relocate();
        await EnsureAdbAsync();
        await RefreshDevicesAsync();
    }

    /// <summary>Señalar un adb.exe que ya este en el PC (platform-tools descargadas a mano, Android Studio…).</summary>
    private async void OnLocateAdbClick(object sender, RoutedEventArgs e)
    {
        if (Desktop.Current.PickAdb(this, Loc.Get("LocateAdbTooltip")) is not { } chosen)
            return;
        AdbService.RememberChosen(chosen);
        _adb.Relocate();
        if (_adb.IsAvailable)
        {
            SetStatus(Loc.Format("AdbInstalled", System.IO.Path.GetDirectoryName(chosen) ?? chosen));
            _deviceTimer.Start();
            await RefreshDevicesAsync();
        }
        else
        {
            SetStatus(Loc.Get("NoAdb"));
        }
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        AppLog.Write(text);
    }

    /// <summary>Un error que no se esperaba (App lo recoge): se dice en la barra de estado y se sigue.</summary>
    public void ShowError(string message) => SetStatus(Loc.Format("UnexpectedError", message));

    private void SetSessionButtons(bool connected)
    {
        ConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        // El desplegable sigue activo con sesion: elegir otro movil cambia de sesion.
        RefreshButton.IsEnabled = !connected;
        WifiButton.IsEnabled = !connected;

        foreach (var button in new[] { BackButton, HomeButton, RecentsButton, NotificationsButton, PowerButton,
                     RotateButton, VolumeDownButton, VolumeUpButton, MuteButton, ScreenshotButton, CopyButton, PasteButton })
        {
            button.IsEnabled = connected;
        }
    }

    // =====================================================================
    //  Dispositivos
    // =====================================================================

    // =====================================================================
    //  adb: si no hay, se baja de Google
    // =====================================================================

    private bool _installingAdb;

    /// <summary>Sin adb no hay nada que hacer: se baja de Google al primer arranque y ya esta.</summary>
    private async Task EnsureAdbAsync()
    {
        if (_adb.IsAvailable || _installingAdb)
            return;

        _installingAdb = true;
        SetStatus(Loc.Get("AdbDownloading"));
        PlaceholderText.Text = Loc.Get("AdbDownloading");
        try
        {
            var folder = await Desktop.Current.InstallAdbAsync();
            _adb.Relocate();
            SetStatus(Loc.Format("AdbInstalled", folder));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.Format("AdbDownloadFailed", ex.Message));
        }
        finally
        {
            _installingAdb = false;
            UpdatePlaceholder();
        }
    }

    // =====================================================================
    //  Abrir al conectar un movil (bandeja)
    // =====================================================================

    /// <summary>
    /// Arranque en bandeja (opcion --tray): sin Show(). La ventana existe pero no se ve, y como
    /// Loaded no llega hasta que se enseña, el temporizador de moviles se arranca aqui a mano.
    /// Lo llama App despues de construir la ventana (las propiedades init aun no estan puestas
    /// dentro del constructor).
    /// </summary>
    public async void RunInTray()
    {
        ShowTrayIcon();
        await EnsureAdbAsync();
        _deviceTimer.Start();
    }

    private void ShowTrayIcon()
    {
        if (_tray is not null)
            return;

        _tray = Desktop.Current.CreateTray(Loc.Get("TrayOpen"), Loc.Get("TrayQuit"));
        _tray.SetText(Loc.Get("TrayWaiting"));
        _tray.Activated += (_, _) => ShowFromTray();
        _tray.QuitRequested += (_, _) =>
        {
            _quitting = true;
            Close();
            Desktop.Current.Shutdown();
        };
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    /// <summary>
    /// Titulo = aplicacion · modelo · numero de serie del movil elegido (o conectado), o «sin movil».
    /// Lo lee la lista de instancias de las otras Phone Mirror y la barra de tareas.
    /// </summary>
    private void UpdateTitle(string? deviceName = null)
    {
        var device = DeviceBox.SelectedItem as AdbDevice;
        var who = device is null ? Loc.Get("TitleNoPhone")
            : deviceName is { Length: > 0 } ? $"{deviceName} · {device.Serial}"
            : device.Caption;
        Title = $"{Loc.Get("AppTitle")} · {who}";
        if (_session is not null || device is not null)
            _tray?.SetText(Title);
    }

    private bool _hintShown;

    /// <summary>
    /// Esconde la ventana en la bandeja. La primera vez, un globo desde el icono: Windows 11 mete
    /// los iconos nuevos en el desbordamiento (^) y si no, parece que la aplicacion se ha cerrado.
    /// </summary>
    private void HideToTray()
    {
        Hide();
        if (!_hintShown)
        {
            _hintShown = true;
            _tray?.Balloon(Loc.Get("AppTitle"), Loc.Get("TrayHidden"));
        }
    }

    private void OnOpenOnConnectChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingToggles)
            return;

        var enabled = OpenOnConnectButton.IsChecked == true;
        OpenOnConnect.Set(enabled);

        // El icono de la bandeja esta siempre; esto solo decide si el movil que se enchufa abre
        // la ventana escondida (y si la aplicacion arranca con Windows).
        SetStatus(Loc.Get(enabled ? "OpenOnConnectOn" : "OpenOnConnectOff"));
    }

    private async Task RefreshDevicesAsync()
    {
        if (!_adb.IsAvailable)
        {
            SetSessionButtons(false);
            UpdatePlaceholder();
            return;
        }

        try
        {
            var devices = await _adb.ListDevicesAsync();
            var selected = (DeviceBox.SelectedItem as AdbDevice)?.Serial;

            // Solo se toca la lista si ha cambiado: repintar el desplegable cada tres segundos
            // molesta cuando esta abierto.
            var current = DeviceBox.Items.Cast<AdbDevice>().ToList();
            if (!current.SequenceEqual(devices))
            {
                DeviceBox.ItemsSource = devices;
                DeviceBox.SelectedItem = devices.FirstOrDefault(d => d.Serial == (selected ?? PreferredSerial))
                    ?? devices.FirstOrDefault(d => d.IsReady)
                    ?? devices.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }

        // Mientras se preguntaba a adb la ventana puede haberse cerrado de verdad: nada mas que hacer.
        if (_closed)
            return;

        ConnectButton.IsEnabled = DeviceBox.SelectedItem is AdbDevice { IsReady: true };
        UpdatePlaceholder();
        UpdateTitle();

        if (AutoConnect && _session is null && ConnectButton.IsEnabled && !_autoConnected)
        {
            _autoConnected = true;
            await ConnectAsync();
        }

        // Al abrir la aplicacion con un movil enchufado se conecta sola, y lo mismo cuando se
        // enchufa uno mientras la ventana esta abierta sin sesion: para eso se abre. Solo se
        // respeta al usuario que ha pulsado Desconectar, mientras siga el mismo movil.
        var readySerials = string.Join("|", DeviceBox.Items.Cast<AdbDevice>().Where(d => d.IsReady).Select(d => d.Serial).OrderBy(s => s));
        if (readySerials != _lastReadySerials)
        {
            _lastReadySerials = readySerials;
            _userDisconnected = false;
        }

        if (_session is null && ConnectButton.IsEnabled && !_userDisconnected && !_installingAdb && IsVisible)
        {
            await ConnectAsync();
            return;
        }

        // Como Vysor: con «abrir al conectar», el movil que aparece abre la ventana escondida y se espeja.
        if (OpenOnConnect.IsEnabled && _session is null && ConnectButton.IsEnabled && !_installingAdb)
        {
            if (!IsVisible)
                ShowFromTray();
            await ConnectAsync();
        }
    }

    private string? _connectedSerial;

    private async void OnDeviceSelected(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = DeviceBox.SelectedItem is AdbDevice { IsReady: true };
        UpdatePlaceholder();

        // Con una sesion en marcha, elegir otro movil es querer ver ese: se cambia.
        if (_session is not null && DeviceBox.SelectedItem is AdbDevice { IsReady: true } device
            && device.Serial != _connectedSerial)
        {
            await DisconnectAsync();
            await ConnectAsync();
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async void OnWifiClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WifiWindow(_adb) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ConnectedAddress is null)
            return;

        // El recien conectado pasa a ser el elegido del desplegable: es el que se queria ver.
        await RefreshDevicesAsync();
        var wanted = dialog.ConnectedAddress.Contains(':') ? dialog.ConnectedAddress : dialog.ConnectedAddress + ":5555";
        var device = DeviceBox.Items.Cast<AdbDevice>().FirstOrDefault(d => string.Equals(d.Serial, wanted, StringComparison.OrdinalIgnoreCase));
        if (device is not null)
        {
            DeviceBox.SelectedItem = device;
            ConnectButton.IsEnabled = device.IsReady;
        }

        SetStatus(Loc.Format("WifiConnected", dialog.ConnectedAddress));
    }

    private async Task ReconnectRememberedAsync()
    {
        if (!_adb.IsAvailable)
            return;

        foreach (var address in WifiAddresses.Load())
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await _adb.ConnectAsync(address, timeout.Token);
            }
            catch (Exception)
            {
                // Apagado o en otra red: nada que hacer, seguira en la lista para la proxima.
            }
        }
    }

    // =====================================================================
    //  Sesion
    // =====================================================================

    private async void OnConnectClick(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        _userDisconnected = true;
        await DisconnectAsync();
    }

    private bool _connecting;
    private bool _closed;

    private async Task ConnectAsync()
    {
        // Una sola conexion a la vez: el temporizador de moviles y un clic (o el cambio de movil)
        // podian llegar aqui a la vez mientras la primera aun arrancaba, y salian dos sesiones.
        if (_session is not null || _connecting || DeviceBox.SelectedItem is not AdbDevice { IsReady: true } device)
            return;
        _connecting = true;
        try
        {
            await ConnectToAsync(device);
        }
        finally
        {
            _connecting = false;
        }
    }

    private async Task ConnectToAsync(AdbDevice device)
    {
        ConnectButton.IsEnabled = false;
        SetStatus(Loc.Format("Connecting", device.Caption));

        var session = Desktop.Current.CreateSession(_adb, device.Serial);
        session.FrameReady += OnFrameReady;
        session.VideoSizeChanged += (w, h) => Dispatcher.BeginInvoke(() =>
        {
            SetStatus(Loc.Format("Connected", session.DeviceName, w, h));
            // El movil en el titulo: es lo que ve la lista de instancias y la barra de tareas.
            UpdateTitle(session.DeviceName);
            FitWindowToVideo(w, h);
        });
        session.Ended += reason => Dispatcher.BeginInvoke(async () =>
        {
            if (!ReferenceEquals(_session, session))
                return;
            await DisconnectAsync();
            if (reason is not null)
                SetStatus(Loc.Format("SessionEnded", reason));

            // Se ha desenchufado el movil: si la aplicacion arranco escondida (con Windows), la
            // ventana vuelve a esconderse y se queda esperando al siguiente.
            if (_tray is not null && StartInTray)
                Hide();
        });
        session.ServerOutput += line => AppLog.Write($"[scrcpy] {line}");

        try
        {
            await session.StartAsync();
        }
        catch (Exception ex)
        {
            await session.DisposeAsync();
            SetStatus(ex.Message);
            ConnectButton.IsEnabled = true;
            return;
        }

        _session = session;
        _connectedSerial = device.Serial;
        session.Control!.ClipboardReceived += text => Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Desktop.Current.SetClipboardText(text);
                SetStatus(Loc.Get("ClipboardCopied"));
            }
            catch (Exception)
            {
                // Otro programa tiene el portapapeles bloqueado: se intentara la proxima vez.
            }
        });

        _screenOn = true;
        SetSessionButtons(true);
        UpdatePlaceholder();
        SetStatus(Loc.Format("Connected", session.DeviceName, session.VideoWidth, session.VideoHeight));
        MirrorArea.Focus();
    }

    private async Task DisconnectAsync()
    {
        var session = _session;
        _session = null;
        if (session is not null)
        {
            session.FrameReady -= OnFrameReady;
            await session.DisposeAsync();
        }

        _connectedSerial = null;
        Mirror.Source = null;
        _bitmap = null;
        FpsText.Text = string.Empty;
        SetSessionButtons(false);
        ConnectButton.IsEnabled = DeviceBox.SelectedItem is AdbDevice { IsReady: true };
        SetStatus(Loc.Get("Disconnected"));
        UpdateTitle();
        UpdatePlaceholder();
    }

    // =====================================================================
    //  Video
    // =====================================================================

    /// <summary>
    /// Adapta la ventana al formato del video: al girar el movil (un juego apaisado, por ejemplo)
    /// la ventana pasa a apaisada, y al volver, a vertical. Se conserva mas o menos el area del
    /// espejo, sin salirse de la pantalla y sin tocar una ventana maximizada.
    /// </summary>
    private void FitWindowToVideo(int videoWidth, int videoHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || WindowState != WindowState.Normal)
            return;

        var bounds = VideoGeometry.FitWindow(videoWidth, videoHeight,
            new Size(ActualWidth, ActualHeight), new Size(MirrorArea.ActualWidth, MirrorArea.ActualHeight),
            new Size(MinWidth, MinHeight), new Point(Left, Top), SystemParameters.WorkArea);

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    /// <summary>
    /// Llega desde el hilo de video. Se copia al mapa de bits de forma sincrona: el bufer que
    /// trae se reutiliza para el cuadro siguiente, asi que hay que vaciarlo antes de volver.
    /// </summary>
    private void OnFrameReady(VideoFrame frame)
    {
        try
        {
            Dispatcher.Invoke(() =>
            {
                if (_session is null)
                    return;

                if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
                {
                    _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
                    Mirror.Source = _bitmap;
                }

                _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);

                _fpsCount++;
                var elapsed = DateTime.UtcNow - _fpsSince;
                if (elapsed.TotalSeconds >= 1)
                {
                    FpsText.Text = $"{_fpsCount / elapsed.TotalSeconds:0} fps";
                    _fpsCount = 0;
                    _fpsSince = DateTime.UtcNow;
                }
            }, DispatcherPriority.Render);
        }
        catch (TaskCanceledException)
        {
            // La ventana se esta cerrando.
        }
    }

    // =====================================================================
    //  Raton → toques
    // =====================================================================

    /// <summary>Punto del raton en coordenadas del video, o <c>null</c> si cae fuera de la imagen.</summary>
    private (int X, int Y)? ToVideo(Point point)
    {
        if (_session is null || _bitmap is null)
            return null;

        // Stretch="Uniform": la imagen ocupa el mayor rectangulo proporcional centrado.
        return VideoGeometry.ToVideo(MirrorArea.ActualWidth, MirrorArea.ActualHeight,
            _session.VideoWidth, _session.VideoHeight, point.X, point.Y);
    }

    private async void OnMirrorMouseDown(object sender, MouseButtonEventArgs e) =>
        await MouseDownAsync(e.ChangedButton, e.GetPosition(MirrorArea));

    /// <summary>Un boton del raton sobre el espejo, en coordenadas del area (las pruebas lo llaman directamente).</summary>
    internal async Task MouseDownAsync(MouseButton button, Point point)
    {
        MirrorArea.Focus();
        if (_session?.Control is not { } control)
            return;

        // Como en scrcpy: el boton derecho es «atras» y el central «inicio».
        if (button == MouseButton.Right)
        {
            await control.BackOrScreenOnAsync();
            return;
        }

        if (button == MouseButton.Middle)
        {
            await control.PressKeyAsync(AndroidKeys.Home);
            return;
        }

        if (button != MouseButton.Left || ToVideo(point) is not { } p)
            return;

        // Primero el «abajo» y despues capturar: capturar el raton dispara un movimiento, y el
        // movil no debe recibir un movimiento de un dedo que aun no ha tocado la pantalla.
        await control.TouchAsync(ControlChannel.ActionDown, ControlChannel.MousePointerId, p.X, p.Y,
            _session.VideoWidth, _session.VideoHeight, 1f, ControlChannel.ButtonPrimary, ControlChannel.ButtonPrimary);
        _touching = true;
        MirrorArea.CaptureMouse();
    }

    private async void OnMirrorMouseMove(object sender, MouseEventArgs e)
    {
        if (_touching)
            await MouseMoveAsync(e.GetPosition(MirrorArea));
    }

    internal async Task MouseMoveAsync(Point point)
    {
        if (!_touching || _session?.Control is not { } control)
            return;

        if (ToVideo(point) is not { } p)
        {
            // Fuera de la imagen se sigue arrastrando en el borde, no se corta el gesto.
            p = Clamp(point);
        }

        await control.TouchAsync(ControlChannel.ActionMove, ControlChannel.MousePointerId, p.X, p.Y,
            _session.VideoWidth, _session.VideoHeight, 1f, 0, ControlChannel.ButtonPrimary);
    }

    private async void OnMirrorMouseUp(object sender, MouseButtonEventArgs e) =>
        await MouseUpAsync(e.ChangedButton, e.GetPosition(MirrorArea));

    internal async Task MouseUpAsync(MouseButton button, Point point)
    {
        if (button != MouseButton.Left || !_touching || _session?.Control is not { } control)
            return;

        _touching = false;
        MirrorArea.ReleaseMouseCapture();

        var p = ToVideo(point) ?? Clamp(point);
        await control.TouchAsync(ControlChannel.ActionUp, ControlChannel.MousePointerId, p.X, p.Y,
            _session.VideoWidth, _session.VideoHeight, 0f, ControlChannel.ButtonPrimary, 0);
    }

    private void OnMirrorMouseLeave(object sender, MouseEventArgs e)
    {
        // Con el raton capturado los eventos siguen llegando aunque salga del area.
    }

    private async void OnMirrorMouseWheel(object sender, MouseWheelEventArgs e) =>
        await WheelAsync(e.Delta, e.GetPosition(MirrorArea), Keyboard.Modifiers);

    internal async Task WheelAsync(int delta, Point point, ModifierKeys modifiers)
    {
        if (_session?.Control is not { } control || ToVideo(point) is not { } p)
            return;

        // Una muesca de rueda = un paso de desplazamiento. Con Shift, horizontal.
        var notches = Math.Clamp(delta / 120f, -1f, 1f);
        var horizontal = modifiers.HasFlag(ModifierKeys.Shift);
        await control.ScrollAsync(p.X, p.Y, _session.VideoWidth, _session.VideoHeight,
            horizontal ? notches : 0f, horizontal ? 0f : notches, 0);
    }

    private (int X, int Y) Clamp(Point point)
    {
        return VideoGeometry.Clamp(MirrorArea.ActualWidth, MirrorArea.ActualHeight,
            _session?.VideoWidth ?? 1, _session?.VideoHeight ?? 1, point.X, point.Y);
    }

    // =====================================================================
    //  Teclado
    // =====================================================================

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session?.Control is null || !MirrorArea.IsKeyboardFocused)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (WillHandleKey(key, Keyboard.Modifiers))
        {
            e.Handled = true;
            await KeyDownAsync(key, e.IsRepeat, Keyboard.Modifiers);
        }
    }

    /// <summary>¿Va la tecla al movil? Ctrl+V y Ctrl+C (portapapeles) y las que tienen tecla de Android.</summary>
    internal static bool WillHandleKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control && key is Key.V or Key.C || AndroidKeys.FromKey(key) is not null;

    /// <summary>Una tecla pulsada con el espejo enfocado (las pruebas lo llaman directamente).</summary>
    internal async Task KeyDownAsync(Key key, bool repeat, ModifierKeys modifiers)
    {
        if (_session?.Control is not { } control)
            return;

        // Ctrl+V pega el portapapeles del PC; Ctrl+C trae el del movil.
        if (modifiers == ModifierKeys.Control)
        {
            if (key == Key.V)
            {
                await PasteAsync();
                return;
            }

            if (key == Key.C)
            {
                await control.RequestClipboardAsync(copyKey: 1);
                return;
            }
        }

        if (AndroidKeys.FromKey(key) is { } keycode)
            await control.KeyAsync(0, keycode, repeat ? 1 : 0, AndroidKeys.MetaState(modifiers));
    }

    private async void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_session?.Control is null || !MirrorArea.IsKeyboardFocused)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (AndroidKeys.FromKey(key) is not null)
        {
            e.Handled = true;
            await KeyUpAsync(key, Keyboard.Modifiers);
        }
    }

    internal async Task KeyUpAsync(Key key, ModifierKeys modifiers)
    {
        if (_session?.Control is { } control && AndroidKeys.FromKey(key) is { } keycode)
            await control.KeyAsync(1, keycode, 0, AndroidKeys.MetaState(modifiers));
    }

    private async void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_session?.Control is null || !MirrorArea.IsKeyboardFocused || !IsRealText(e.Text))
            return;

        e.Handled = true;
        await TextAsync(e.Text);
    }

    /// <summary>Lo que no es texto de verdad (controles, tabulador) ya se ha mandado como tecla.</summary>
    internal static bool IsRealText(string? text) => !string.IsNullOrEmpty(text) && !text.Any(char.IsControl);

    internal async Task TextAsync(string text)
    {
        if (_session?.Control is { } control && IsRealText(text))
            await control.TextAsync(text);
    }

    // =====================================================================
    //  Botones
    // =====================================================================

    private async void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is { } control)
            await control.BackOrScreenOnAsync();
    }

    private async void OnHomeClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is { } control)
            await control.PressKeyAsync(AndroidKeys.Home);
    }

    private async void OnRecentsClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is { } control)
            await control.PressKeyAsync(AndroidKeys.AppSwitch);
    }

    private async void OnNotificationsClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is { } control)
            await control.ExpandNotificationPanelAsync();
    }

    private async void OnPowerClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is not { } control)
            return;

        // Apaga solo la pantalla del movil; el espejo sigue vivo y se maneja igual.
        _screenOn = !_screenOn;
        await control.SetDisplayPowerAsync(_screenOn);
    }

    private async void OnRotateClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is { } control)
            await control.RotateDeviceAsync();
    }

    private async void OnVolumeUpClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is { } control)
            await control.PressKeyAsync(AndroidKeys.VolumeUp);
    }

    private async void OnVolumeDownClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is { } control)
            await control.PressKeyAsync(AndroidKeys.VolumeDown);
    }

    // La tecla de silencio del movil: alterna, como en un mando. Es el volumen del telefono lo
    // que se silencia; el audio no se reenvia a esta ventana (ScrcpySession lo desactiva).
    private async void OnMuteClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is { } control)
            await control.PressKeyAsync(AndroidKeys.VolumeMute);
    }

    private void OnScreenshotClick(object sender, RoutedEventArgs e)
    {
        if (_bitmap is null)
            return;

        var folder = Desktop.Current.ScreenshotFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_bitmap.Clone()));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        SetStatus(Loc.Format("ScreenshotSaved", path));
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_session?.Control is { } control)
            await control.RequestClipboardAsync();
    }

    private async void OnPasteClick(object sender, RoutedEventArgs e) => await PasteAsync();

    private async Task PasteAsync()
    {
        if (_session?.Control is not { } control)
            return;

        string text;
        try
        {
            text = Desktop.Current.ClipboardHasText() ? Desktop.Current.ClipboardText() : string.Empty;
        }
        catch (Exception)
        {
            return;
        }

        if (text.Length > 0)
            await control.SetClipboardAsync(text, paste: true);
    }

    private void OnTopmostChanged(object sender, RoutedEventArgs e) => Topmost = TopmostButton.IsChecked == true;

    private void OnLanguageClick(object sender, RoutedEventArgs e) => Loc.Toggle();

    private void OnAboutClick(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    // =====================================================================
    //  Arrastrar ficheros: APK se instala, lo demas va a Download
    // =====================================================================

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DropEffect(e.Data);
        e.Handled = true;
    }

    internal DragDropEffects DropEffect(IDataObject data) =>
        _session is not null && data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            await DropFilesAsync(files);
    }

    /// <summary>Ficheros soltados en la ventana: los APK se instalan, lo demas va a Download.</summary>
    internal async Task DropFilesAsync(string[] files)
    {
        if (_session is null)
            return;

        var serial = (DeviceBox.SelectedItem as AdbDevice)?.Serial;
        if (serial is null)
            return;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            try
            {
                if (file.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus(Loc.Format("Installing", name));
                    await _adb.InstallAsync(serial, file);
                    SetStatus(Loc.Format("Installed", name));
                }
                else
                {
                    SetStatus(Loc.Format("Copying", name));
                    await _adb.PushAsync(serial, file, $"/sdcard/Download/{name}");
                    SetStatus(Loc.Format("Copied", name));
                }
            }
            catch (Exception ex)
            {
                SetStatus(Loc.Format("DropFailed", name, ex.Message));
            }
        }
    }
}
