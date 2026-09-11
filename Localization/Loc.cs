using System.Globalization;
using System.Windows.Markup;

namespace PhoneMirror.Localization;

/// <summary>
/// Textos de la aplicacion en español e ingles (constitucion, seccion 7). Ningun texto va en el
/// XAML ni en el codigo: todo pasa por aqui.
/// </summary>
/// <remarks>
/// Arranca en el idioma de Windows y se puede cambiar desde la barra; el cambio vuelve a pintar
/// la ventana, que es mas sencillo que enlazar cada texto.
/// </remarks>
public static class Loc
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["AppTitle"] = "Phone Mirror",
        ["NoAdb"] = "adb.exe was not found. Install the Android platform-tools or set the ADB environment variable to its path.",
        ["NoDevices"] = "No phone connected. Plug it in by USB and enable USB debugging in the developer options.",
        ["Unauthorized"] = "The phone is asking whether to trust this computer. Accept on its screen.",
        ["Connecting"] = "Connecting to {0}…",
        ["Connected"] = "{0} · {1}×{2}",
        ["Disconnected"] = "Disconnected",
        ["SessionEnded"] = "The connection ended: {0}",
        ["RefreshTooltip"] = "Look for phones again",
        ["ConnectTooltip"] = "Connect",
        ["DisconnectTooltip"] = "Disconnect",
        ["HomeTooltip"] = "Home",
        ["BackTooltip"] = "Back",
        ["RecentsTooltip"] = "Recent apps",
        ["PowerTooltip"] = "Screen on / off",
        ["RotateTooltip"] = "Rotate",
        ["VolumeUpTooltip"] = "Volume up",
        ["VolumeDownTooltip"] = "Volume down",
        ["NotificationsTooltip"] = "Notifications",
        ["ScreenshotTooltip"] = "Save a screenshot",
        ["PasteTooltip"] = "Paste the PC clipboard on the phone",
        ["CopyTooltip"] = "Copy the phone clipboard to the PC",
        ["AlwaysOnTopTooltip"] = "Keep on top",
        ["LanguageTooltip"] = "Español / English",
        ["ScreenshotSaved"] = "Screenshot saved to {0}",
        ["ClipboardCopied"] = "Phone clipboard copied to the PC",
        ["DropHint"] = "Drop an APK to install it, or any file to copy it to the phone's Download folder",
        ["Installing"] = "Installing {0}…",
        ["Installed"] = "{0} installed",
        ["Copying"] = "Copying {0}…",
        ["Copied"] = "{0} copied to Download",
        ["DropFailed"] = "Could not send {0}: {1}",
        ["Hint"] = "Click and drag to touch · right button: back · middle button: home · wheel: scroll · type to send text · Ctrl+V: paste",
    };

    private static readonly Dictionary<string, string> Spanish = new()
    {
        ["AppTitle"] = "Phone Mirror",
        ["NoAdb"] = "No se encuentra adb.exe. Instala las platform-tools de Android o pon su ruta en la variable de entorno ADB.",
        ["NoDevices"] = "No hay ningún móvil conectado. Conéctalo por USB y activa la depuración USB en las opciones de desarrollador.",
        ["Unauthorized"] = "El móvil pregunta si confía en este PC. Acepta en su pantalla.",
        ["Connecting"] = "Conectando con {0}…",
        ["Connected"] = "{0} · {1}×{2}",
        ["Disconnected"] = "Desconectado",
        ["SessionEnded"] = "Se ha cortado la conexión: {0}",
        ["RefreshTooltip"] = "Volver a buscar móviles",
        ["ConnectTooltip"] = "Conectar",
        ["DisconnectTooltip"] = "Desconectar",
        ["HomeTooltip"] = "Inicio",
        ["BackTooltip"] = "Atrás",
        ["RecentsTooltip"] = "Aplicaciones recientes",
        ["PowerTooltip"] = "Encender / apagar la pantalla",
        ["RotateTooltip"] = "Girar",
        ["VolumeUpTooltip"] = "Subir volumen",
        ["VolumeDownTooltip"] = "Bajar volumen",
        ["NotificationsTooltip"] = "Notificaciones",
        ["ScreenshotTooltip"] = "Guardar una captura",
        ["PasteTooltip"] = "Pegar el portapapeles del PC en el móvil",
        ["CopyTooltip"] = "Copiar el portapapeles del móvil al PC",
        ["AlwaysOnTopTooltip"] = "Siempre encima",
        ["LanguageTooltip"] = "Español / English",
        ["ScreenshotSaved"] = "Captura guardada en {0}",
        ["ClipboardCopied"] = "Portapapeles del móvil copiado al PC",
        ["DropHint"] = "Suelta un APK para instalarlo, o cualquier fichero para copiarlo a la carpeta Download del móvil",
        ["Installing"] = "Instalando {0}…",
        ["Installed"] = "{0} instalado",
        ["Copying"] = "Copiando {0}…",
        ["Copied"] = "{0} copiado a Download",
        ["DropFailed"] = "No se pudo enviar {0}: {1}",
        ["Hint"] = "Clic y arrastrar para tocar · botón derecho: atrás · botón central: inicio · rueda: desplazar · teclea para escribir · Ctrl+V: pegar",
    };

    public static string Language { get; private set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";

    public static event Action? LanguageChanged;

    public static void Toggle()
    {
        Language = Language == "es" ? "en" : "es";
        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        var table = Language == "es" ? Spanish : English;
        return table.TryGetValue(key, out var value) ? value
            : English.TryGetValue(key, out var fallback) ? fallback
            : string.Empty;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary><c>{loc:T Clave}</c> en el XAML.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
