using System.Text;

Console.OutputEncoding = Encoding.UTF8;

if (Environment.GetEnvironmentVariable("FAKE_ADB_LOG") is { Length: > 0 } log)
{
    // Varios adb de mentira pueden escribir a la vez: se reintenta compartiendo el fichero.
    for (var attempt = 0; attempt < 50; attempt++)
    {
        try
        {
            using var file = new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var bytes = Encoding.UTF8.GetBytes(string.Join('|', args) + "\n");
            file.Write(bytes);
            break;
        }
        catch (IOException)
        {
            Thread.Sleep(10);
        }
    }
}

// Con FAKE_ADB_RULES, un fichero de reglas (una por linea, separadas por tabuladores):
//   trozo de los argumentos <TAB> salida (\n para saltos) <TAB> codigo de salida <TAB> ms que se queda vivo
// Gana la primera regla cuyo trozo aparezca en los argumentos unidos con '|'. Sin regla: nada y 0.
if (Environment.GetEnvironmentVariable("FAKE_ADB_RULES") is { Length: > 0 } rules && File.Exists(rules))
{
    var joined = string.Join('|', args);
    foreach (var line in File.ReadAllLines(rules))
    {
        var parts = line.Split('\t');
        if (parts.Length < 1 || parts[0].Length == 0 || !joined.Contains(parts[0], StringComparison.Ordinal))
            continue;
        Console.Out.Write(parts.Length > 1 ? parts[1].Replace("\\n", "\n") : string.Empty);
        Console.Out.Flush();
        if (parts.Length > 3 && int.TryParse(parts[3], out var sleep) && sleep > 0)
            Thread.Sleep(sleep);
        return parts.Length > 2 && int.TryParse(parts[2], out var exit) ? exit : 0;
    }
    return 0;
}

Console.Out.Write(Environment.GetEnvironmentVariable("FAKE_ADB_OUT") ?? string.Empty);
Console.Error.Write(Environment.GetEnvironmentVariable("FAKE_ADB_ERR") ?? string.Empty);
return int.TryParse(Environment.GetEnvironmentVariable("FAKE_ADB_EXIT"), out var code) ? code : 0;
