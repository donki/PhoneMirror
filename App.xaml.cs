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
        MainWindow = new MainWindow { AutoConnect = e.Args.Contains("--connect") };
        MainWindow.Show();
    }
}
