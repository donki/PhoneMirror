using System.IO;
using System.Text.Json;

namespace PhoneMirror.Services;

/// <summary>
/// Las direcciones a las que se ha conectado por Wi-Fi, para no tener que escribirlas otra vez.
/// </summary>
/// <remarks>
/// <para>Un JSON en <c>%LOCALAPPDATA%\sOCPhoneMirror\wifi.json</c>, la ultima usada la primera y
/// como mucho ocho. Es lo unico que la aplicacion guarda en el equipo.</para>
///
/// <para>Al arrancar se intenta <c>adb connect</c> con todas en segundo plano: un movil que ya
/// tuviera la depuracion por red activa aparece en la lista sin tocar nada, y el que no este
/// encendido sencillamente no aparece.</para>
/// </remarks>
public static class WifiAddresses
{
    private const int Max = 8;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCPhoneMirror", "wifi.json");

    public static IReadOnlyList<string> Load() => Load(FilePath);

    internal static IReadOnlyList<string> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return [];

            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(filePath)) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Pone (o sube) la direccion la primera.</summary>
    public static void Remember(string address) => Remember(FilePath, address);

    internal static void Remember(string filePath, string address) => Save(filePath, Promote(Load(filePath), address));

    public static void Forget(string address) => Forget(FilePath, address);

    internal static void Forget(string filePath, string address) => Save(filePath, Without(Load(filePath), address));

    /// <summary>La direccion la primera, sin repetirla (sin distinguir mayusculas) y como mucho <see cref="Max"/>.</summary>
    internal static List<string> Promote(IEnumerable<string> list, string address)
    {
        var result = Without(list, address);
        result.Insert(0, address);
        return result.Take(Max).ToList();
    }

    internal static List<string> Without(IEnumerable<string> list, string address) =>
        list.Where(a => !string.Equals(a, address, StringComparison.OrdinalIgnoreCase)).ToList();

    private static void Save(string filePath, List<string> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(list));
        }
        catch (Exception)
        {
            // Sin sitio donde guardar, la lista dura lo que dure la ventana. No es para tanto.
        }
    }
}
