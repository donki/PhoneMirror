using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PhoneMirror.Services;

/// <summary>
/// El adb que va dentro del paquete (<c>Assets\platform-tools</c>, junto al ejecutable) y su
/// puesta en el PATH del usuario, para que <c>adb</c> valga tambien desde una consola.
/// </summary>
/// <remarks>
/// <para><b>Por que se puede redistribuir.</b> adb y sus dos DLL son componentes de codigo abierto
/// del SDK (Apache 2.0, en AOSP); la clausula 3.6 del Android SDK License Agreement remite esos
/// componentes a su propia licencia. Van sin modificar y con su NOTICE (ver THIRD-PARTY-NOTICES.md).</para>
///
/// <para><b>PATH.</b> En la version portable (exe suelto) se añade la carpeta al PATH del usuario
/// (HKCU\Environment, sin tocar el del sistema) y se avisa a Windows del cambio; las consolas
/// nuevas lo ven. En el MSIX no se toca el registro (el paquete lo tiene virtualizado y no
/// llegaria al PATH real): ahi el alias <c>adb</c> lo declara el manifiesto
/// (<c>AppExecutionAlias</c>), que es como la Store pone ejecutables en el PATH.</para>
/// </remarks>
public static class BundledAdb
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets", "platform-tools");

    public static string ExecutablePath => Path.Combine(Folder, "adb.exe");

    public static bool IsPresent => File.Exists(ExecutablePath);

    /// <summary>Revision de las platform-tools empaquetadas (source.properties), o vacio.</summary>
    public static string Revision
    {
        get
        {
            try
            {
                var line = File.ReadLines(Path.Combine(Folder, "source.properties")).FirstOrDefault(l => l.StartsWith("Pkg.Revision=", StringComparison.Ordinal));
                return line?["Pkg.Revision=".Length..].Trim() ?? string.Empty;
            }
            catch (Exception) { return string.Empty; }
        }
    }

    /// <summary>Corre dentro de un paquete MSIX (Store): sin acceso real a HKCU\Environment.</summary>
    public static bool IsPackaged
    {
        get
        {
            try
            {
                var length = 0;
                return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
            }
            catch (Exception) { return false; }
        }
    }

    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, System.Text.StringBuilder? packageFullName);

    /// <summary>
    /// Añade la carpeta del adb empaquetado al PATH del usuario si no hay ya ningun adb en el PATH.
    /// Devuelve true si lo ha añadido ahora.
    /// </summary>
    public static bool EnsureOnUserPath()
    {
        if (!IsPresent || IsPackaged)
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
            if (key is null)
                return false;

            var raw = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? string.Empty;
            var entries = raw.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToList();
            var folder = Folder.TrimEnd('\\');

            // Ya esta esta carpeta, o hay otro adb en el PATH (del SDK, de scrcpy…): no se pisa.
            if (entries.Any(e => string.Equals(e.TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (entries.Any(e => File.Exists(Path.Combine(Environment.ExpandEnvironmentVariables(e), "adb.exe"))))
                return false;

            entries.Add(folder);
            key.SetValue("Path", string.Join(';', entries), RegistryValueKind.ExpandString);

            // Que el Explorador y las consolas nuevas se enteren sin cerrar sesion.
            SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", 0, 5000, out _);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private const uint WmSettingChange = 0x001A;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
