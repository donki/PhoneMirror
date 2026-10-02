using System.Windows;
using PhoneMirror.Services;

namespace PhoneMirror;

public partial class App : Application
{
    /// <summary>
    /// Las pruebas crean la App solo por sus recursos: WPF llama a OnStartup en cuanto corre el
    /// Dispatcher, y ellas arrancan con <see cref="Launch"/> cuando y como quieren.
    /// </summary>
    internal static bool HostedByTests { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (HostedByTests)
            return;

        // Un error que no se esperaba no puede cerrar la aplicacion: se apunta en el log y se avisa
        // en la barra de estado. La Store la rechazo el 2026-09-25 por cerrarse al arrancar.
        DispatcherUnhandledException += (_, ex) =>
        {
            OnUnexpected(ex.Exception);
            ex.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLog.Write($"error no controlado en tarea: {ex.Exception}");
            ex.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            AppLog.Write($"error fatal: {ex.ExceptionObject}");

        Launch(e.Args);
    }

    /// <summary>Un error que nadie esperaba: al registro y a la barra de estado; la aplicacion sigue.</summary>
    internal void OnUnexpected(Exception ex)
    {
        AppLog.Write($"error no controlado: {ex}");
        (MainWindow as MainWindow)?.ShowError(ex.Message);
    }

    /// <summary>Tema, Assets, otras ventanas abiertas y la ventana principal (o la bandeja).</summary>
    internal void Launch(IReadOnlyList<string> args)
    {
        ThemeManager.Apply();

        // adb y scrcpy-server van dentro del exe: la carpeta Assets se crea o se pone al dia aqui,
        // antes de que nadie los busque.
        Desktop.Current.PrepareAssets();

        // «--connect»: conecta solo con el primer movil que haya, sin pulsar nada.
        // «--serial XXXX»: con ese movil en concreto (una ventana por movil).
        // «--tray»: arranca escondida en la bandeja y se enseña al enchufar un movil (OpenOnConnect).
        var arguments = LaunchArguments.Parse(args);
        var tray = arguments.StartInTray;

        // Ya hay otra Phone Mirror abierta (a la vista o en la bandeja): ¿enseñar una de ellas o
        // abrir otra? Solo cuando arranca el usuario a mano; con --tray/--connect/--serial (accesos
        // directos y arranque con Windows) no se pregunta.
        if (arguments.AskForInstances)
        {
            var others = Desktop.Current.OtherInstances();
            if (others.Count > 0)
            {
                // Mientras el dialogo es la unica ventana, cerrarlo no debe apagar la aplicacion
                // (con el ShutdownMode por defecto, «Ventana nueva» se cerraba sin abrir nada).
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var chooser = new InstancesWindow(others);
                if (chooser.ShowDialog() != true)
                {
                    Desktop.Current.Shutdown();
                    return;
                }
                if (chooser.Chosen is { } chosen)
                {
                    Desktop.Current.ShowInstance(chosen);
                    Desktop.Current.Shutdown();
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
