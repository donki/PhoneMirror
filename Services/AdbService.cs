using System.Diagnostics;
using System.IO;

namespace PhoneMirror.Services;

/// <summary>Un dispositivo tal como lo lista <c>adb devices -l</c>.</summary>
public sealed record AdbDevice(string Serial, string State, string Model)
{
    public bool IsReady => State == "device";

    public string Caption => Model.Length > 0 ? $"{Model} · {Serial}" : Serial;
}

/// <summary>
/// Envoltura minima de <c>adb.exe</c>: listar dispositivos, empujar ficheros, redirigir puertos y
/// lanzar ordenes en el movil.
/// </summary>
/// <remarks>
/// <para>No se reimplementa el protocolo de adb: el ejecutable de las platform-tools (Apache 2.0)
/// ya esta en cualquier equipo que desarrolle para Android, y hablar con el servidor de adb por su
/// puerto solo compraria problemas cuando Android Studio lo reinicie.</para>
///
/// <para>Se busca en este orden: la variable <c>ADB</c>, junto al ejecutable, <c>ANDROID_HOME</c> /
/// <c>ANDROID_SDK_ROOT</c>, la ruta clasica del SDK y el <c>PATH</c>.</para>
/// </remarks>
public sealed class AdbService
{
    // Donde suele acabar el SDK o las platform-tools sueltas cuando alguien las descarga a mano (la
    // Store probo con las platform-tools «instaladas» y no se encontraban: no estaban en el PATH).
    private static readonly string[] SdkCandidates =
    [
        @"C:\Program Files (x86)\Android\android-sdk",
        @"C:\Program Files\Android\android-sdk",
        @"C:\Android",
        @"C:\Android\Sdk",
        @"C:\Android\android-sdk",
        @"D:\Android",
        @"D:\Android\Sdk",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "android-sdk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Android", "Sdk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Android", "Sdk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Android"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "android-sdk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Android"),
    ];

    // Carpetas de platform-tools sueltas (el zip de Google descomprimido): la raiz de las unidades,
    // el perfil, Descargas, Escritorio y Documentos, con o sin el nombre de la carpeta del zip.
    private static IEnumerable<string> LooseCandidates()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>
        {
            profile,
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            roots.Add(drive.RootDirectory.FullName);
        foreach (var root in roots.Where(r => r.Length > 0))
        {
            yield return Path.Combine(root, "platform-tools", "adb.exe");
            yield return Path.Combine(root, "platform-tools-latest-windows", "platform-tools", "adb.exe");
            yield return Path.Combine(root, "adb", "adb.exe");
            yield return Path.Combine(root, "scrcpy", "adb.exe");
        }
    }

    /// <summary>Ruta elegida a mano con «Buscar adb.exe…», recordada entre sesiones.</summary>
    private static readonly string ChosenPathFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCPhoneMirror", "adb-path.txt");

    public static void RememberChosen(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ChosenPathFile)!);
        File.WriteAllText(ChosenPathFile, path);
    }

    public AdbService()
    {
        ExecutablePath = Locate();
    }

    /// <summary>Ruta de <c>adb.exe</c>, o <c>null</c> si no se encuentra.</summary>
    public string? ExecutablePath { get; private set; }

    /// <summary>Vuelve a buscar adb (despues de que <see cref="AdbInstaller"/> lo haya bajado).</summary>
    public void Relocate() => ExecutablePath = Locate();

    public bool IsAvailable => ExecutablePath is not null;

    private static string? Locate()
    {
        var candidates = new List<string>();

        // Primero lo que eligio el usuario a mano, luego la variable ADB.
        try
        {
            if (File.Exists(ChosenPathFile) && File.ReadAllText(ChosenPathFile).Trim() is { Length: > 0 } chosen)
                candidates.Add(chosen);
        }
        catch (Exception) { }

        if (Environment.GetEnvironmentVariable("ADB") is { Length: > 0 } fromEnv)
            candidates.Add(fromEnv);

        // El que va dentro del paquete (Assets\platform-tools): es el que se prueba y el que se
        // conoce, asi que va antes que cualquier otro del equipo.
        candidates.Add(BundledAdb.ExecutablePath);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "adb.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe"));

        // El que se baja de Google cuando no hay ninguno (AdbInstaller).
        candidates.Add(AdbInstaller.ExecutablePath);

        foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } sdk)
                candidates.Add(Path.Combine(sdk, "platform-tools", "adb.exe"));
        }

        foreach (var sdk in SdkCandidates)
            candidates.Add(Path.Combine(sdk, "platform-tools", "adb.exe"));

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir.Trim(), "adb.exe"));

        candidates.AddRange(LooseCandidates());

        return candidates.FirstOrDefault(p => { try { return File.Exists(p); } catch (Exception) { return false; } });
    }

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunAsync(["devices", "-l"], cancellationToken).ConfigureAwait(false);
        var devices = new List<AdbDevice>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("List of", StringComparison.Ordinal) || trimmed.StartsWith('*'))
                continue;

            var parts = trimmed.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var model = parts.Skip(2)
                .Select(p => p.Split(':', 2))
                .Where(kv => kv.Length == 2 && kv[0] == "model")
                .Select(kv => kv[1].Replace('_', ' '))
                .FirstOrDefault() ?? string.Empty;

            devices.Add(new AdbDevice(parts[0], parts[1], model));
        }

        return devices;
    }

    public Task PushAsync(string serial, string localPath, string remotePath, CancellationToken cancellationToken = default) =>
        RunAsync(["-s", serial, "push", localPath, remotePath], cancellationToken);

    public Task ForwardAsync(string serial, int localPort, string remoteSocket, CancellationToken cancellationToken = default) =>
        RunAsync(["-s", serial, "forward", $"tcp:{localPort}", remoteSocket], cancellationToken);

    public Task RemoveForwardAsync(string serial, int localPort, CancellationToken cancellationToken = default) =>
        RunAsync(["-s", serial, "forward", "--remove", $"tcp:{localPort}"], cancellationToken);

    public Task<string> ShellAsync(string serial, string command, CancellationToken cancellationToken = default) =>
        RunAsync(["-s", serial, "shell", command], cancellationToken);

    public Task InstallAsync(string serial, string apkPath, CancellationToken cancellationToken = default) =>
        RunAsync(["-s", serial, "install", "-r", apkPath], cancellationToken);

    // -----------------------------------------------------------------------
    //  Por Wi-Fi
    // -----------------------------------------------------------------------

    /// <summary>
    /// Conecta con un movil por red (<c>adb connect ip:puerto</c>). Devuelve lo que dice adb.
    /// </summary>
    /// <remarks>
    /// <c>adb connect</c> sale con 0 aunque no consiga conectar —«cannot connect», «failed to
    /// authenticate»—, asi que el resultado se lee del texto: solo «connected to» o «already
    /// connected» son exito. Sin puerto se pone el 5555, el de la depuracion por red de toda la vida.
    /// </remarks>
    public async Task<string> ConnectAsync(string address, CancellationToken cancellationToken = default)
    {
        var output = (await RunAsync(["connect", WithPort(address)], cancellationToken).ConfigureAwait(false)).Trim();
        if (!IsConnected(output))
            throw new InvalidOperationException(output.Length > 0 ? output : $"adb connect {address}");

        return output;
    }

    /// <summary>
    /// Empareja con la «Depuracion inalambrica» de Android 11+ (<c>adb pair ip:puerto codigo</c>).
    /// El puerto y el codigo son los que enseña el movil en «Vincular dispositivo con un codigo»,
    /// y son distintos del puerto de conexion.
    /// </summary>
    public async Task<string> PairAsync(string address, string code, CancellationToken cancellationToken = default)
    {
        var output = (await RunAsync(["pair", address.Trim(), code.Trim()], cancellationToken).ConfigureAwait(false)).Trim();
        if (!output.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(output.Length > 0 ? output : $"adb pair {address}");

        return output;
    }

    public Task DisconnectAsync(string address, CancellationToken cancellationToken = default) =>
        RunAsync(["disconnect", WithPort(address)], cancellationToken);

    /// <summary>Un numero de serie de adb es una direccion de red si lleva «ip:puerto».</summary>
    public static bool IsNetworkSerial(string serial) =>
        serial.Contains(':') && serial.Split(':') is [var host, var port] && host.Contains('.') && int.TryParse(port, out _);

    private static string WithPort(string address)
    {
        var trimmed = address.Trim();
        return trimmed.Contains(':') ? trimmed : $"{trimmed}:5555";
    }

    private static bool IsConnected(string output) =>
        output.Contains("connected to", StringComparison.OrdinalIgnoreCase) &&
        !output.Contains("cannot connect", StringComparison.OrdinalIgnoreCase) &&
        !output.Contains("failed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lanza un <c>adb shell</c> que se queda corriendo (el servidor de scrcpy) y devuelve el
    /// proceso para poder matarlo al cerrar.
    /// </summary>
    public Process StartShell(string serial, string command)
    {
        var process = new Process
        {
            StartInfo = Start(["-s", serial, "shell", command]),
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = Start(arguments) };
        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"adb {string.Join(' ', arguments)}: {(error.Length > 0 ? error : output).Trim()}");

        return output;
    }

    private ProcessStartInfo Start(IReadOnlyList<string> arguments)
    {
        if (ExecutablePath is null)
            throw new InvalidOperationException("adb.exe no encontrado.");

        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        return info;
    }
}
