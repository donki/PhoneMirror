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
    private readonly AdbService _adb = new();
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

    /// <summary>Arrancar escondida en la bandeja y enseñarse al enchufar un movil (opcion --tray).</summary>
    public bool StartInTray { get; init; }

    private TrayIconHost? _tray;
    private bool _quitting;
    private bool _loadingToggles;

    public MainWindow()
    {
        InitializeComponent();

        ApplyTexts();
        Loc.LanguageChanged += ApplyTexts;

        // Cada pocos segundos se mira si ha aparecido un movil, para no tener que pulsar nada al
        // enchufarlo. Solo mientras no hay sesion: con el espejo en marcha no hace falta.
        _deviceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _deviceTimer.Tick += async (_, _) => { if (_session is null) await RefreshDevicesAsync(); };

        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        Loaded += async (_, _) =>
        {
            MirrorArea.Focus();
            await EnsureAdbAsync();
            await RefreshDevicesAsync();
            _deviceTimer.Start();

            // Los moviles que se conectaron por Wi-Fi otras veces: se les vuelve a llamar en
            // segundo plano y el que este encendido aparece solo en la lista. No se espera a los
            // que no contesten (adb tarda unos segundos en darlos por perdidos).
            _ = ReconnectRememberedAsync();
        };
        Closing += async (_, e) =>
        {
            // En modo bandeja, cerrar la ventana es esconderla: la aplicacion sigue esperando al
            // siguiente movil. Salir de verdad se hace desde el menu del icono.
            if (_tray is not null && !_quitting)
            {
                e.Cancel = true;
                await DisconnectAsync();
                Hide();
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
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        AppLog.Write(text);
    }

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
            await AdbInstaller.InstallAsync();
            _adb.Relocate();
            SetStatus(Loc.Format("AdbInstalled", AdbInstaller.Folder));
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

        _tray = new TrayIconHost(Loc.Get("TrayOpen"), Loc.Get("TrayQuit"));
        _tray.SetText(Loc.Get("TrayWaiting"));
        _tray.Activated += (_, _) => ShowFromTray();
        _tray.QuitRequested += (_, _) =>
        {
            _quitting = true;
            Close();
            Application.Current.Shutdown();
        };
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void OnOpenOnConnectChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingToggles)
            return;

        var enabled = OpenOnConnectButton.IsChecked == true;
        OpenOnConnect.Set(enabled);

        // Al activarlo, la ventana ya se queda vigilando desde ahora: cerrar la esconde en la
        // bandeja y el siguiente movil la abre. Al desactivarlo, el icono se va.
        if (enabled)
        {
            ShowTrayIcon();
            SetStatus(Loc.Get("OpenOnConnectOn"));
        }
        else
        {
            _tray?.Dispose();
            _tray = null;
            SetStatus(Loc.Get("OpenOnConnectOff"));
        }
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

        ConnectButton.IsEnabled = DeviceBox.SelectedItem is AdbDevice { IsReady: true };
        UpdatePlaceholder();

        if (AutoConnect && _session is null && ConnectButton.IsEnabled && !_autoConnected)
        {
            _autoConnected = true;
            await ConnectAsync();
        }

        // Como Vysor: con el icono en la bandeja, el movil que aparece abre la ventana y se espeja.
        if (_tray is not null && _session is null && ConnectButton.IsEnabled && !_installingAdb)
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

    private async void OnDisconnectClick(object sender, RoutedEventArgs e) => await DisconnectAsync();

    private async Task ConnectAsync()
    {
        if (_session is not null || DeviceBox.SelectedItem is not AdbDevice { IsReady: true } device)
            return;

        ConnectButton.IsEnabled = false;
        SetStatus(Loc.Format("Connecting", device.Caption));

        var session = new ScrcpySession(_adb, device.Serial);
        session.FrameReady += OnFrameReady;
        session.VideoSizeChanged += (w, h) => Dispatcher.BeginInvoke(() =>
        {
            SetStatus(Loc.Format("Connected", session.DeviceName, w, h));
            FitWindowToVideo(w, h);
        });
        session.Ended += reason => Dispatcher.BeginInvoke(async () =>
        {
            if (!ReferenceEquals(_session, session))
                return;
            await DisconnectAsync();
            if (reason is not null)
                SetStatus(Loc.Format("SessionEnded", reason));

            // Se ha desenchufado el movil: en modo bandeja la ventana vuelve a esconderse y se
            // queda esperando al siguiente.
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
                Clipboard.SetText(text);
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

        var chromeWidth = Math.Max(0, ActualWidth - MirrorArea.ActualWidth);
        var chromeHeight = Math.Max(0, ActualHeight - MirrorArea.ActualHeight);

        var work = SystemParameters.WorkArea;
        var maxMirrorWidth = work.Width - chromeWidth - 24;
        var maxMirrorHeight = work.Height - chromeHeight - 24;

        // Misma superficie que ahora (o un tamaño razonable la primera vez), con el formato nuevo.
        var area = Math.Max(MirrorArea.ActualWidth * MirrorArea.ActualHeight, 480.0 * 800.0);
        var ratio = (double)videoWidth / videoHeight;
        var mirrorWidth = Math.Sqrt(area * ratio);
        var mirrorHeight = mirrorWidth / ratio;

        var scale = Math.Min(1.0, Math.Min(maxMirrorWidth / mirrorWidth, maxMirrorHeight / mirrorHeight));
        mirrorWidth *= scale;
        mirrorHeight *= scale;

        var newWidth = Math.Max(MinWidth, mirrorWidth + chromeWidth);
        var newHeight = Math.Max(MinHeight, mirrorHeight + chromeHeight);

        // Que no se salga por la derecha o por abajo al crecer.
        Left = Math.Max(work.Left, Math.Min(Left, work.Right - newWidth));
        Top = Math.Max(work.Top, Math.Min(Top, work.Bottom - newHeight));
        Width = newWidth;
        Height = newHeight;
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

        var videoWidth = _session.VideoWidth;
        var videoHeight = _session.VideoHeight;
        if (videoWidth == 0 || videoHeight == 0)
            return null;

        // Stretch="Uniform": la imagen ocupa el mayor rectangulo proporcional centrado.
        var scale = Math.Min(MirrorArea.ActualWidth / videoWidth, MirrorArea.ActualHeight / videoHeight);
        var shownWidth = videoWidth * scale;
        var shownHeight = videoHeight * scale;
        var offsetX = (MirrorArea.ActualWidth - shownWidth) / 2;
        var offsetY = (MirrorArea.ActualHeight - shownHeight) / 2;

        var x = (point.X - offsetX) / scale;
        var y = (point.Y - offsetY) / scale;

        if (x < 0 || y < 0 || x >= videoWidth || y >= videoHeight)
            return null;

        return ((int)x, (int)y);
    }

    private async void OnMirrorMouseDown(object sender, MouseButtonEventArgs e)
    {
        MirrorArea.Focus();
        if (_session?.Control is not { } control)
            return;

        // Como en scrcpy: el boton derecho es «atras» y el central «inicio».
        if (e.ChangedButton == MouseButton.Right)
        {
            await control.BackOrScreenOnAsync();
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            await control.PressKeyAsync(AndroidKeys.Home);
            return;
        }

        if (e.ChangedButton != MouseButton.Left || ToVideo(e.GetPosition(MirrorArea)) is not { } p)
            return;

        _touching = true;
        MirrorArea.CaptureMouse();
        await control.TouchAsync(ControlChannel.ActionDown, ControlChannel.MousePointerId, p.X, p.Y,
            _session.VideoWidth, _session.VideoHeight, 1f, ControlChannel.ButtonPrimary, ControlChannel.ButtonPrimary);
    }

    private async void OnMirrorMouseMove(object sender, MouseEventArgs e)
    {
        if (!_touching || _session?.Control is not { } control)
            return;

        var point = e.GetPosition(MirrorArea);
        if (ToVideo(point) is not { } p)
        {
            // Fuera de la imagen se sigue arrastrando en el borde, no se corta el gesto.
            p = Clamp(point);
        }

        await control.TouchAsync(ControlChannel.ActionMove, ControlChannel.MousePointerId, p.X, p.Y,
            _session.VideoWidth, _session.VideoHeight, 1f, 0, ControlChannel.ButtonPrimary);
    }

    private async void OnMirrorMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_touching || _session?.Control is not { } control)
            return;

        _touching = false;
        MirrorArea.ReleaseMouseCapture();

        var point = e.GetPosition(MirrorArea);
        var p = ToVideo(point) ?? Clamp(point);
        await control.TouchAsync(ControlChannel.ActionUp, ControlChannel.MousePointerId, p.X, p.Y,
            _session.VideoWidth, _session.VideoHeight, 0f, ControlChannel.ButtonPrimary, 0);
    }

    private void OnMirrorMouseLeave(object sender, MouseEventArgs e)
    {
        // Con el raton capturado los eventos siguen llegando aunque salga del area.
    }

    private async void OnMirrorMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_session?.Control is not { } control || ToVideo(e.GetPosition(MirrorArea)) is not { } p)
            return;

        // Una muesca de rueda = un paso de desplazamiento. Con Shift, horizontal.
        var notches = Math.Clamp(e.Delta / 120f, -1f, 1f);
        var horizontal = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        await control.ScrollAsync(p.X, p.Y, _session.VideoWidth, _session.VideoHeight,
            horizontal ? notches : 0f, horizontal ? 0f : notches, 0);
    }

    private (int X, int Y) Clamp(Point point)
    {
        var videoWidth = _session?.VideoWidth ?? 1;
        var videoHeight = _session?.VideoHeight ?? 1;
        var scale = Math.Min(MirrorArea.ActualWidth / videoWidth, MirrorArea.ActualHeight / videoHeight);
        var offsetX = (MirrorArea.ActualWidth - videoWidth * scale) / 2;
        var offsetY = (MirrorArea.ActualHeight - videoHeight * scale) / 2;
        var x = Math.Clamp((point.X - offsetX) / scale, 0, videoWidth - 1);
        var y = Math.Clamp((point.Y - offsetY) / scale, 0, videoHeight - 1);
        return ((int)x, (int)y);
    }

    // =====================================================================
    //  Teclado
    // =====================================================================

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session?.Control is not { } control || !MirrorArea.IsKeyboardFocused)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ctrl+V pega el portapapeles del PC; Ctrl+C trae el del movil.
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (key == Key.V)
            {
                e.Handled = true;
                await PasteAsync();
                return;
            }

            if (key == Key.C)
            {
                e.Handled = true;
                await control.RequestClipboardAsync(copyKey: 1);
                return;
            }
        }

        if (AndroidKeys.FromKey(key) is { } keycode)
        {
            e.Handled = true;
            await control.KeyAsync(0, keycode, e.IsRepeat ? 1 : 0, AndroidKeys.MetaState(Keyboard.Modifiers));
        }
    }

    private async void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_session?.Control is not { } control || !MirrorArea.IsKeyboardFocused)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (AndroidKeys.FromKey(key) is { } keycode)
        {
            e.Handled = true;
            await control.KeyAsync(1, keycode, 0, AndroidKeys.MetaState(Keyboard.Modifiers));
        }
    }

    private async void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_session?.Control is not { } control || !MirrorArea.IsKeyboardFocused)
            return;

        // Lo que no es texto de verdad (controles, tabulador) ya se ha mandado como tecla.
        if (string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl))
            return;

        e.Handled = true;
        await control.TextAsync(e.Text);
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

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Phone Mirror");
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
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
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
        e.Effects = _session is not null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (_session is null || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
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
