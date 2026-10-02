using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PhoneMirror.Services;

/// <summary>El icono de la bandeja, visto desde la ventana.</summary>
public interface ITrayIcon : IDisposable
{
    event EventHandler? Activated;

    event EventHandler? QuitRequested;

    void SetText(string text);

    void Balloon(string title, string text);
}

/// <summary>
/// Lo que las ventanas piden al sistema: adb, la bandeja, el portapapeles, dialogos de Windows,
/// abrir enlaces, las otras instancias y la sesion de espejo. En la aplicacion es
/// <see cref="RealDesktop"/>; las pruebas ponen un doble en <see cref="Desktop.Current"/> y asi
/// manejan las ventanas sin moviles, sin red, sin bandeja y sin tocar el portapapeles de nadie.
/// </summary>
public interface IDesktop
{
    AdbService CreateAdb();

    ScrcpySession CreateSession(AdbService adb, string serial);

    /// <summary>Saca adb y scrcpy-server del ejecutable a la carpeta Assets (BundledAssets.Ensure).</summary>
    void PrepareAssets();

    /// <summary>Baja adb de Google. Devuelve la carpeta donde ha quedado.</summary>
    Task<string> InstallAdbAsync();

    /// <summary>Pone el adb empaquetado en el PATH del usuario si no hay otro. Verdadero si lo ha puesto ahora.</summary>
    bool EnsureAdbOnPath();

    ITrayIcon CreateTray(string openText, string quitText);

    /// <summary>Dialogo para señalar un adb.exe. Null si se cancela.</summary>
    string? PickAdb(Window owner, string title);

    bool ClipboardHasText();

    string ClipboardText();

    void SetClipboardText(string text);

    /// <summary>Carpeta de las capturas (Imagenes\Phone Mirror en la aplicacion).</summary>
    string ScreenshotFolder { get; }

    /// <summary>Abre un enlace o un programa (como Process.Start con UseShellExecute).</summary>
    void Start(ProcessStartInfo info);

    void ShowMessage(Window owner, string text, string caption);

    IReadOnlyList<Instances.Instance> OtherInstances();

    void ShowInstance(Instances.Instance instance);

    /// <summary>Cerrar la aplicacion de verdad («Salir» en la bandeja).</summary>
    void Shutdown();
}

/// <summary>Donde esta el de esta ejecucion.</summary>
public static class Desktop
{
    public static IDesktop Current { get; set; } = new RealDesktop();
}

/// <summary>El de verdad.</summary>
public sealed class RealDesktop : IDesktop
{
    public AdbService CreateAdb() => new();

    public ScrcpySession CreateSession(AdbService adb, string serial) => new(adb, serial);

    public void PrepareAssets() => BundledAssets.Ensure();

    public async Task<string> InstallAdbAsync()
    {
        await AdbInstaller.InstallAsync().ConfigureAwait(true);
        return AdbInstaller.Folder;
    }

    public bool EnsureAdbOnPath() => BundledAdb.EnsureOnUserPath();

    public ITrayIcon CreateTray(string openText, string quitText) => new TrayIconHost(openText, quitText);

    public string? PickAdb(Window owner, string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "adb.exe|adb.exe",
            FileName = "adb.exe",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public bool ClipboardHasText() => Clipboard.ContainsText();

    public string ClipboardText() => Clipboard.GetText();

    public void SetClipboardText(string text) => Clipboard.SetText(text);

    public string ScreenshotFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Phone Mirror");

    public void Start(ProcessStartInfo info) => Process.Start(info)?.Dispose();

    public void ShowMessage(Window owner, string text, string caption) => MessageBox.Show(owner, text, caption);

    public IReadOnlyList<Instances.Instance> OtherInstances() => Instances.Others();

    public void ShowInstance(Instances.Instance instance) => Instances.Show(instance);

    public void Shutdown() => Application.Current.Shutdown();
}
