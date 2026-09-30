using System.IO;
using System.IO.Compression;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

/// <summary>
/// BundledAssets, BundledAdb, AppLog y las rutas de AdbInstaller. Todo en la carpeta de salida
/// de las pruebas (Assets junto al ejecutable) y el registro en un temporal: nada del equipo.
/// Van juntas porque comparten estado estatico (<see cref="BundledAssets.Root"/>, <see cref="AppLog.Path"/>).
/// </summary>
public sealed class BundledTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-bundled-{Guid.NewGuid():N}");
    private readonly string _originalLog = AppLog.Path;

    public BundledTests()
    {
        Directory.CreateDirectory(_dir);
        AppLog.Path = Path.Combine(_dir, "log.txt");
    }

    public void Dispose()
    {
        AppLog.Path = _originalLog;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string AssetsBeside => Path.Combine(AppContext.BaseDirectory, "Assets");

    [Fact]
    public void AppLog_AppendsTimestampedLines()
    {
        AppLog.Write("primera");
        AppLog.Write("segunda línea");

        var lines = File.ReadAllLines(AppLog.Path);
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} primera$", lines[0]);
        Assert.EndsWith(" segunda línea", lines[1]);
    }

    [Fact]
    public void AppLog_Unwritable_DoesNotThrow()
    {
        var blocker = Path.Combine(_dir, "bloqueo");
        File.WriteAllText(blocker, "x");
        AppLog.Path = Path.Combine(blocker, "log.txt");

        AppLog.Write("no cabe");

        Assert.False(File.Exists(AppLog.Path));
    }

    [Fact]
    public void AppLog_DefaultPathIsInLocalAppData() =>
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Socratic", "PhoneMirror", "log.txt"),
            _originalLog);

    [Fact]
    public void Ensure_CopiesEmbeddedAssets_SkipsUnchanged_ReplacesChanged_AndLogsFailures()
    {
        var hello = Path.Combine(AssetsBeside, "test", "hello.txt");
        var blocked = Path.Combine(AssetsBeside, "test", "blocked.txt");
        var testDir = Path.Combine(AssetsBeside, "test");
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, recursive: true);
        // Un directorio donde deberia ir el fichero: no se puede reemplazar.
        Directory.CreateDirectory(blocked);

        BundledAssets.Ensure();

        Assert.Equal(AssetsBeside, BundledAssets.Root);
        Assert.Equal([Path.Combine("test", "hello.txt")], BundledAssets.Updated);
        Assert.Equal("hola", File.ReadAllText(hello).Trim());
        var log = File.ReadAllText(AppLog.Path);
        Assert.Contains("no se ha podido actualizar", log);
        Assert.Contains("1 fichero(s) puestos al dia", log);

        // Igual (tamaño y SHA-256): no se toca.
        BundledAssets.Ensure();
        Assert.Empty(BundledAssets.Updated);

        // Mismo tamaño y contenido distinto: se reemplaza.
        var original = File.ReadAllBytes(hello);
        var changed = (byte[])original.Clone();
        changed[0] ^= 0x20;
        File.WriteAllBytes(hello, changed);
        BundledAssets.Ensure();
        Assert.Equal([Path.Combine("test", "hello.txt")], BundledAssets.Updated);
        Assert.Equal(original, File.ReadAllBytes(hello));

        // Otro tamaño: tambien.
        File.WriteAllText(hello, "otro contenido mas largo");
        BundledAssets.Ensure();
        Assert.Single(BundledAssets.Updated);
        Assert.Equal(original, File.ReadAllBytes(hello));

        Directory.Delete(blocked);
        BundledAssets.Ensure();
        Assert.Equal([Path.Combine("test", "blocked.txt")], BundledAssets.Updated);
        Assert.False(File.Exists(blocked + ".nuevo"));
    }

    [Fact]
    public void BundledAdb_PathsHangFromTheAssetsRoot()
    {
        Assert.Equal(Path.Combine(BundledAssets.Root, "platform-tools"), BundledAdb.Folder);
        Assert.Equal(Path.Combine(BundledAssets.Root, "platform-tools", "adb.exe"), BundledAdb.ExecutablePath);
    }

    [Fact]
    public void BundledAdb_NotPackaged_NoAdbHere_DoesNotTouchThePath()
    {
        Assert.False(BundledAdb.IsPackaged);
        Assert.False(BundledAdb.IsPresent);
        // Sin adb empaquetado sale antes de abrir el registro.
        Assert.False(BundledAdb.EnsureOnUserPath());
    }

    [Fact]
    public void BundledAdb_Revision_FromSourceProperties()
    {
        var file = Path.Combine(BundledAdb.Folder, "source.properties");
        Directory.CreateDirectory(BundledAdb.Folder);
        try
        {
            File.WriteAllText(file, "Pkg.UserSrc=false\nPkg.Revision= 36.0.0 \nPkg.Path=platform-tools\n");
            Assert.Equal("36.0.0", BundledAdb.Revision);

            File.WriteAllText(file, "Pkg.Path=platform-tools\n");
            Assert.Equal(string.Empty, BundledAdb.Revision);

            File.Delete(file);
            Assert.Equal(string.Empty, BundledAdb.Revision);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AdbInstaller_DownloadsFromGoogleToLocalAppData()
    {
        Assert.Equal("https://dl.google.com/android/repository/platform-tools-latest-windows.zip", AdbInstaller.DownloadUrl);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCPhoneMirror", "platform-tools", "adb.exe"),
            AdbInstaller.ExecutablePath);
    }

    private static byte[] Zip(params string[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write($"contenido de {name}");
            }
        }
        return memory.ToArray();
    }

    [Fact]
    public void AdbInstaller_Extract_TakesOnlyTheThreeAdbFiles()
    {
        var folder = Path.Combine(_dir, "pt");
        var zip = Zip("platform-tools/adb.exe", "platform-tools/AdbWinApi.dll", "platform-tools/adbwinusbapi.dll",
            "platform-tools/fastboot.exe", "platform-tools/NOTICE.txt", "platform-tools/lib64/");

        AdbInstaller.Extract(zip, folder);

        Assert.Equal(["AdbWinApi.dll", "adb.exe", "adbwinusbapi.dll"],
            Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("contenido de platform-tools/adb.exe", File.ReadAllText(Path.Combine(folder, "adb.exe")));

        // Una segunda vez sobrescribe sin quejarse.
        AdbInstaller.Extract(zip, folder);
    }

    [Fact]
    public void AdbInstaller_Extract_IncompleteZip_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AdbInstaller.Extract(Zip("platform-tools/adb.exe", "platform-tools/fastboot.exe"), Path.Combine(_dir, "pt")));
        Assert.Contains("(1/3)", ex.Message);
    }

    [Fact]
    public void AdbInstaller_Extract_NotAZip_Throws() =>
        Assert.ThrowsAny<InvalidDataException>(() => AdbInstaller.Extract([1, 2, 3, 4], Path.Combine(_dir, "pt")));
}
