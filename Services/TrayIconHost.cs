using System.Drawing;
using WinForms = System.Windows.Forms;

namespace PhoneMirror.Services;

/// <summary>
/// El icono de la bandeja cuando la aplicacion espera a que se enchufe un movil.
/// </summary>
/// <remarks>
/// NotifyIcon vive en WinForms; es la unica forma sin dependencias externas de tener icono de
/// bandeja desde WPF (lo mismo que hace Task Manager). El icono es el de la propia aplicacion.
/// </remarks>
public sealed class TrayIconHost : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public TrayIconHost(string openText, string quitText)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(openText, null, (_, _) => Activated?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(quitText, null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "sOC Phone Mirror",
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                Activated?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Clic izquierdo o «Abrir»: enseñar la ventana.</summary>
    public event EventHandler? Activated;

    /// <summary>«Salir» del menu: cerrar de verdad.</summary>
    public event EventHandler? QuitRequested;

    public void SetText(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    /// <summary>Un globo desde el icono (Windows lo enseña aunque el icono este en el desbordamiento).</summary>
    public void Balloon(string title, string text) => _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
