using System.Windows;
using System.Windows.Input;
using PhoneMirror.Localization;
using PhoneMirror.Services;

namespace PhoneMirror;

/// <summary>
/// Conectar con un movil por Wi-Fi.
/// </summary>
/// <remarks>
/// <para>Phone Mirror trabaja sobre adb, y adb sabe hablar por red desde siempre: aqui solo se le
/// pide <c>adb connect</c> (y <c>adb pair</c> la primera vez en Android 11+) para que el movil
/// aparezca en el desplegable como si estuviera enchufado. El espejo en si no cambia.</para>
///
/// <para>Las direcciones que han funcionado se recuerdan (<see cref="WifiAddresses"/>) y la
/// ventana principal las vuelve a conectar al arrancar.</para>
/// </remarks>
public partial class WifiWindow : Window
{
    private readonly AdbService _adb;

    public WifiWindow(AdbService adb)
    {
        InitializeComponent();
        _adb = adb;

        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        AddressBox.ItemsSource = WifiAddresses.Load();
        if (AddressBox.Items.Count > 0)
            AddressBox.SelectedIndex = 0;

        ForgetButton.ToolTip = Loc.Get("WifiForgetTooltip");
        ConnectButton.ToolTip = Loc.Get("WifiConnectTooltip");
        PairButton.ToolTip = Loc.Get("WifiPairTooltip");
        CloseButton.ToolTip = Loc.Get("Close");
    }

    /// <summary>La direccion que ha quedado conectada, o null si se cerro sin conectar.</summary>
    public string? ConnectedAddress { get; private set; }

    private string Address => (AddressBox.Text ?? string.Empty).Trim();

    private async void OnConnectClick(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ConnectAsync();
        }
    }

    private async Task ConnectAsync()
    {
        var address = Address;
        if (address.Length == 0)
            return;

        SetBusy(true, Loc.Format("WifiConnecting", address));
        try
        {
            await _adb.ConnectAsync(address);

            // Se guarda tal como la ha escrito el usuario (con o sin puerto): es lo que querra ver
            // la proxima vez en el desplegable.
            WifiAddresses.Remember(address);
            ConnectedAddress = address;
            StatusText.Text = Loc.Format("WifiConnected", address);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("WifiFailed", ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void OnPairClick(object sender, RoutedEventArgs e)
    {
        var address = PairAddressBox.Text.Trim();
        var code = PairCodeBox.Text.Trim();
        if (address.Length == 0 || code.Length == 0)
            return;

        SetBusy(true, Loc.Format("WifiPairing", address));
        try
        {
            await _adb.PairAsync(address, code);
            StatusText.Text = Loc.Get("WifiPaired");

            // Tras vincular, lo normal es conectar con la misma IP y otro puerto: se deja la IP
            // puesta arriba para que solo haya que completar el puerto.
            var host = address.Split(':')[0];
            if (Address.Length == 0)
                AddressBox.Text = host + ":";
            AddressBox.Focus();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("WifiFailed", ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void OnForgetClick(object sender, RoutedEventArgs e)
    {
        var address = Address;
        if (address.Length == 0)
            return;

        WifiAddresses.Forget(address);
        AddressBox.ItemsSource = WifiAddresses.Load();
        AddressBox.Text = string.Empty;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy, string? status)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.IsEnabled = !busy;
        PairButton.IsEnabled = !busy;
        ForgetButton.IsEnabled = !busy;
        if (status is not null)
            StatusText.Text = status;
    }
}
