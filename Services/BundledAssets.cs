using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace PhoneMirror.Services;

/// <summary>
/// Todo lo que la aplicacion necesita aparte del ejecutable (adb con sus DLL, scrcpy-server) va
/// <b>dentro del exe</b> como recursos embebidos. Al arrancar se mira la carpeta <c>Assets</c>:
/// si no existe, o falta algo, o lo que hay es de otra version (distinto tamaño o SHA-256), se
/// crea y se copian o actualizan los ficheros. Asi el exe suelto (OneDrive, un pendrive) funciona
/// solo, sin copiar nada a mano.
/// </summary>
/// <remarks>
/// La carpeta es <c>Assets</c> junto al ejecutable si ahi se puede escribir. Si no (instalado
/// desde el MSIX, en WindowsApps, o en Archivos de programa), se usa
/// <c>%LOCALAPPDATA%\sOCPhoneMirror\Assets</c>. En los dos casos <see cref="Root"/> dice cual es.
/// Un fichero que no se pueda reemplazar (adb.exe en uso por un servidor adb de antes) se deja
/// como esta y se apunta en el log; a la siguiente vez se vuelve a intentar.
/// </remarks>
public static class BundledAssets
{
    private const string Prefix = "Assets/";

    /// <summary>Carpeta de assets en uso. Hasta llamar a <see cref="Ensure"/>, la de al lado del exe.</summary>
    public static string Root { get; private set; } = Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>Ficheros copiados o actualizados en el ultimo <see cref="Ensure"/> (para el log).</summary>
    public static IReadOnlyList<string> Updated { get; private set; } = [];

    /// <summary>Deja la carpeta de assets al dia con lo que lleva dentro el ejecutable.</summary>
    public static void Ensure()
    {
        var updated = new List<string>();
        try
        {
            Root = PickRoot();
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
            {
                var relative = name[Prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                var target = Path.Combine(Root, relative);
                try
                {
                    using var stream = assembly.GetManifestResourceStream(name);
                    if (stream is null)
                        continue;
                    if (SameFile(target, stream))
                        continue;
                    stream.Position = 0;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    // A un temporal y luego encima: si se corta a medias no queda un fichero a trozos.
                    var temp = target + ".nuevo";
                    using (var file = File.Create(temp))
                        stream.CopyTo(file);
                    File.Move(temp, target, overwrite: true);
                    updated.Add(relative);
                }
                catch (Exception ex)
                {
                    AppLog.Write($"assets: no se ha podido actualizar {relative}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"assets: {ex.Message}");
        }
        Updated = updated;
        if (updated.Count > 0)
            AppLog.Write($"assets: {updated.Count} fichero(s) puestos al dia en {Root}: {string.Join(", ", updated)}");
    }

    /// <summary>Junto al exe si se puede escribir ahi; si no, en el perfil del usuario.</summary>
    private static string PickRoot()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "Assets");
        try
        {
            Directory.CreateDirectory(beside);
            var probe = Path.Combine(beside, ".escribible");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return beside;
        }
        catch (Exception)
        {
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCPhoneMirror", "Assets");
            Directory.CreateDirectory(local);
            return local;
        }
    }

    /// <summary>El fichero de disco es el mismo que el embebido: mismo tamaño y mismo SHA-256.</summary>
    private static bool SameFile(string path, Stream embedded)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != embedded.Length)
            return false;
        using var file = info.OpenRead();
        return SHA256.HashData(file).AsSpan().SequenceEqual(SHA256.HashData(embedded));
    }
}
