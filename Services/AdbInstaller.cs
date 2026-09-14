using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace PhoneMirror.Services;

/// <summary>
/// Descarga las platform-tools de Android <b>de Google</b> cuando en el equipo no hay adb.
/// </summary>
/// <remarks>
/// <para><b>Por que de Google y no de un zip nuestro en GitHub.</b> Las platform-tools van bajo la
/// licencia del SDK de Android, que no permite redistribuirlas; scrcpy las mete en sus releases,
/// pero nosotros no tenemos por que asumir eso. Google publica un zip con direccion fija
/// (<c>platform-tools-latest-windows.zip</c>, unos 7 MB) y cada usuario se lo baja de ahi al
/// primer uso, que es lo que haria a mano. No se redistribuye nada.</para>
///
/// <para>Solo se sacan los tres ficheros que adb necesita en Windows (<c>adb.exe</c>,
/// <c>AdbWinApi.dll</c>, <c>AdbWinUsbApi.dll</c>) a <c>%LOCALAPPDATA%\sOCPhoneMirror\platform-tools</c>,
/// que es donde <see cref="AdbService"/> mira despues de la variable <c>ADB</c> y de la carpeta del
/// ejecutable. Si el usuario instala el SDK mas tarde, el suyo manda.</para>
/// </remarks>
public static class AdbInstaller
{
    public const string DownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

    private static readonly string[] Needed = ["adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll"];

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCPhoneMirror", "platform-tools");

    public static string ExecutablePath => Path.Combine(Folder, "adb.exe");

    /// <summary>Baja el zip y deja adb en <see cref="Folder"/>. Devuelve la ruta de adb.exe.</summary>
    public static async Task<string> InstallAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        progress?.Report(DownloadUrl);

        var bytes = await http.GetByteArrayAsync(DownloadUrl, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(Folder);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var found = 0;
        foreach (var entry in zip.Entries)
        {
            var name = Path.GetFileName(entry.FullName);
            if (!Needed.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            // Un adb que estuviera corriendo tiene el .exe bloqueado: se le pide que pare antes.
            var target = Path.Combine(Folder, name);
            entry.ExtractToFile(target, overwrite: true);
            found++;
        }

        if (found < Needed.Length)
            throw new InvalidOperationException($"El zip de platform-tools no trae adb completo ({found}/{Needed.Length}).");

        return ExecutablePath;
    }
}
