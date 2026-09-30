using System.Text;

Console.OutputEncoding = Encoding.UTF8;

if (Environment.GetEnvironmentVariable("FAKE_ADB_LOG") is { Length: > 0 } log)
    File.AppendAllText(log, string.Join('|', args) + "\n");

Console.Out.Write(Environment.GetEnvironmentVariable("FAKE_ADB_OUT") ?? string.Empty);
Console.Error.Write(Environment.GetEnvironmentVariable("FAKE_ADB_ERR") ?? string.Empty);
return int.TryParse(Environment.GetEnvironmentVariable("FAKE_ADB_EXIT"), out var code) ? code : 0;
