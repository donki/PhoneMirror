using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;
using FontFamily = System.Windows.Media.FontFamily;

namespace PhoneMirror.Services;

/// <summary>
/// El icono de la bandeja cuando la aplicacion espera a que se enchufe un movil.
/// </summary>
/// <remarks>
/// NotifyIcon vive en WinForms; es la unica forma sin dependencias externas de tener icono de
/// bandeja desde WPF (lo mismo que hace Task Manager). El icono es el de la propia aplicacion.
/// El menu, en cambio, es un ContextMenu de WPF con el aspecto comun de las apps sOC (constitucion
/// General 6.14, el de sOC ScreenShare), no el menu gris de WinForms.
/// </remarks>
public sealed class TrayIconHost : ITrayIcon
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly string _openText;
    private readonly string _quitText;

    public TrayIconHost(string openText, string quitText)
    {
        _openText = openText;
        _quitText = quitText;
        _icon = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "sOC Phone Mirror",
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                Activated?.Invoke(this, EventArgs.Empty);
            else if (e.Button == WinForms.MouseButtons.Right)
                ShowMenu();
        };
    }

    /// <summary>Clic izquierdo o «Abrir»: enseñar la ventana.</summary>
    public event EventHandler? Activated;

    /// <summary>«Salir» del menu: cerrar de verdad.</summary>
    public event EventHandler? QuitRequested;

    public void SetText(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    /// <summary>Un globo desde el icono (Windows lo enseña aunque el icono este en el desbordamiento).</summary>
    public void Balloon(string title, string text) => _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.None);

    /// <summary>El menu del clic derecho: «Abrir», separador y «Salir».</summary>
    internal ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("TrayMenu") };
        menu.Items.Add(Item(_openText, "", () => Activated?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(Separator());
        menu.Items.Add(Item(_quitText, "", () => QuitRequested?.Invoke(this, EventArgs.Empty)));
        return menu;
    }

    /// <summary>
    /// Junto al puntero; al abrirse pasa a primer plano para que se cierre al pulsar fuera (si no,
    /// el menu de un icono de bandeja se queda abierto).
    /// </summary>
    private void ShowMenu()
    {
        var menu = BuildMenu();
        menu.Placement = PlacementMode.MousePoint;
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource popup)
                SetForegroundWindow(popup.Handle);
        };
        menu.IsOpen = true;
    }

    /// <summary>Un elemento del menu con su icono plano (glifo de Segoe Fluent Icons).</summary>
    public static MenuItem Item(string text, string glyph, Action action, bool enabled = true, bool danger = false)
    {
        var item = new MenuItem
        {
            Header = text,
            Style = (Style)Application.Current.FindResource("TrayItem"),
            IsEnabled = enabled,
            Icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = 14,
            },
        };
        // Recurso dinamico: el icono cambia de color con el tema igual que el resto del menu.
        ((TextBlock)item.Icon).SetResourceReference(TextBlock.ForegroundProperty, danger ? "Danger" : "PrimaryText");
        item.Click += (_, _) => action();
        return item;
    }

    public static Separator Separator() => new() { Style = (Style)Application.Current.FindResource("TraySeparator") };

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}
