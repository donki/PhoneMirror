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
        var serialIndex = Array.IndexOf(e.Args, "--serial");
        MainWindow = new MainWindow
        {
            AutoConnect = e.Args.Contains("--connect") || serialIndex >= 0,
            PreferredSerial = serialIndex >= 0 && serialIndex + 1 < e.Args.Length ? e.Args[serialIndex + 1] : null,
        };
        MainWindow.Show();
    }
}
