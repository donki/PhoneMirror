using System.IO;

namespace PhoneMirror.Services;

/// <summary>
/// Registro de lo que pasa, en un fichero de texto: lo que dice la barra de estado y lo que
/// escribe el servidor de scrcpy. Es lo que se mira cuando «no conecta» (constitucion, seccion 10).
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Socratic", "PhoneMirror", "log.txt");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Un registro que no se puede escribir no puede tumbar la aplicacion.
        }
    }
}
