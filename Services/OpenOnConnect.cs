using Microsoft.Win32;

namespace PhoneMirror.Services;

/// <summary>
/// «Abrir al conectar un movil»: como Vysor. La aplicacion arranca con Windows escondida en la
/// bandeja y, en cuanto adb ve un movil, enseña la ventana y lo espeja.
/// </summary>
/// <remarks>
/// <para>El ajuste ES la entrada de arranque: si la aplicacion esta en
/// <c>HKCU\...\Run</c> con <c>--tray</c>, esta activado; si no, no. Asi no hay dos sitios que
/// puedan contradecirse, y quitarla del arranque desde la configuracion de Windows lo desactiva
/// tambien.</para>
///
/// <para>No hace falta un servicio ni escuchar los eventos USB: la ventana ya mira cada tres
/// segundos si hay moviles nuevos, y en modo bandeja hace lo mismo sin que se vea.</para>
/// </remarks>
public static class OpenOnConnect
{
    public const string TrayArgument = "--tray";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "sOCPhoneMirror";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains(TrayArgument, StringComparison.Ordinal);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null)
            return;

        if (enabled)
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(executable))
                key.SetValue(ValueName, $"\"{executable}\" {TrayArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
