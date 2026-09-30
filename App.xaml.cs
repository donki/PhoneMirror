using System.Windows;
using PhoneMirror.Services;

namespace PhoneMirror;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Un error que no se esperaba no puede cerrar la aplicacion: se apunta en el log y se avisa
        // en la barra de estado. La Store la rechazo el 2026-09-25 por cerrarse al arrancar.
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Write($"error no controlado: {ex.Exception}");
            (MainWindow as MainWindow)?.ShowError(ex.Exception.Message);
            ex.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLog.Write($"error no controlado en tarea: {ex.Exception}");
            ex.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            AppLog.Write($"error fatal: {ex.ExceptionObject}");

        ThemeManager.Apply();

        // adb y scrcpy-server van dentro del exe: la carpeta Assets se crea o se pone al dia aqui,
        // antes de que nadie los busque.
        BundledAssets.Ensure();

        // «--connect»: conecta solo con el primer movil que haya, sin pulsar nada.
        // «--serial XXXX»: con ese movil en concreto (una ventana por movil).
        // «--tray»: arranca escondida en la bandeja y se enseña al enchufar un movil (OpenOnConnect).
        var arguments = LaunchArguments.Parse(e.Args);
        var tray = arguments.StartInTray;

        // Ya hay otra Phone Mirror abierta (a la vista o en la bandeja): ¿enseñar una de ellas o
        // abrir otra? Solo cuando arranca el usuario a mano; con --tray/--connect/--serial (accesos
        // directos y arranque con Windows) no se pregunta.
        if (arguments.AskForInstances)
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
            AutoConnect = arguments.AutoConnect,
            PreferredSerial = arguments.PreferredSerial,
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
