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

        // Ya hay otra Phone Mirror abierta (a la vista o en la bandeja): ¿enseñar una de ellas o
        // abrir otra? Solo cuando arranca el usuario a mano; con --tray/--connect/--serial (accesos
        // directos y arranque con Windows) no se pregunta.
        if (!tray && serialIndex < 0 && !e.Args.Contains("--connect") && !e.Args.Contains("--new"))
        {
            var others = Instances.Others();
            if (others.Count > 0)
            {
                // Mientras el dialogo es la unica ventana, cerrarlo no debe apagar la aplicacion
                // (con el ShutdownMode por defecto, «Ventana nueva» se cerraba sin abrir nada).
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var chooser = new InstancesWindow(others);
                if (chooser.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
                if (chooser.Chosen is { } chosen)
                {
                    Instances.Show(chosen);
                    Shutdown();
                    return;
                }
            }
        }
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
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
        }
    }
}
