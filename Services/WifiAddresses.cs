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

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Pone (o sube) la direccion la primera.</summary>
    public static void Remember(string address)
    {
        var list = Load().Where(a => !string.Equals(a, address, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, address);
        Save(list.Take(Max).ToList());
    }

    public static void Forget(string address) =>
        Save(Load().Where(a => !string.Equals(a, address, StringComparison.OrdinalIgnoreCase)).ToList());

    private static void Save(List<string> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
        }
        catch (Exception)
        {
            // Sin sitio donde guardar, la lista dura lo que dure la ventana. No es para tanto.
        }
    }
}
