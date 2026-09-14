using System.Windows;
using PhoneMirror.Services;

namespace PhoneMirror;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ThemeManager.Apply();

        // «--connect»: conecta solo con el primer movil que haya, sin pulsar nada.
        // «--serial XXXX»: con ese movil en concreto (una ventana por movil).
        // «--tray»: arranca escondida en la bandeja y se enseña al enchufar un movil (OpenOnConnect).
        var serialIndex = Array.IndexOf(e.Args, "--serial");
        var tray = e.Args.Contains(OpenOnConnect.TrayArgument);
        MainWindow = new MainWindow
        {
            AutoConnect = e.Args.Contains("--connect") || serialIndex >= 0,
            PreferredSerial = serialIndex >= 0 && serialIndex + 1 < e.Args.Length ? e.Args[serialIndex + 1] : null,
            StartInTray = tray,
        };

        // Sin ShutdownMode explicito, esconder la unica ventana no cierra la aplicacion, pero
        // cerrarla si: en modo bandeja la ventana se esconde en vez de cerrarse (ver MainWindow).
        if (tray)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ((MainWindow)MainWindow).RunInTray();
        }
        else
        {
            MainWindow.Show();
        }
    }
}
