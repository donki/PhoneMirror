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
        ["AppTitle"] = "sOC Phone Mirror",
        ["AboutTitle"] = "About",
        ["AboutTooltip"] = "About",
        ["AboutDescription"] = "See and control the phone plugged in by USB from Windows: touch, type, copy, paste and drag files.",
        ["Publisher"] = "Socratic",
        ["Contact"] = "Contact",
        ["WriteAuthor"] = "Write to the author",
        ["ContactHint"] = "Suggestions, bugs and ideas: all of it gets read.",
        ["LanguageTitle"] = "Language",
        ["LanguageHint"] = "The language applies right away.",
        ["Privacy"] = "Privacy",
        ["PrivacyText"] = "Everything happens between this PC and the phone over the USB cable: the screen is decoded here and the touches go straight to the phone. Nothing goes to the internet, nothing is stored except the screenshots you save and the text you copy. No ads, no trackers, no analytics.",
        ["License"] = "License",
        ["LicenseText"] = "Free software under the MIT licence. The scrcpy server (Apache 2.0) and the rest of third-party components are listed in THIRD-PARTY-NOTICES.md.",
        ["LicenseLine"] = "MIT License \u00b7 Copyright \u00a9 2026 Socratic",
        ["LegalTitle"] = "Legal notice",
        ["LegalText1"] = "This software is provided \"as is\", without warranty of any kind, express or implied.",
        ["LegalText2"] = "In no event shall the authors be liable for any claim, damages or other liability arising from the use of this software.",
        ["WarningText"] = "⚠️ Use at your own risk",
        ["Close"] = "Close",
        ["NoAdb"] = "adb.exe (Android platform-tools, by Google) was not found on this PC. Download it here, or point to a copy you already have.",
        ["DownloadAdbTooltip"] = "Download the Android platform-tools from Google (about 7 MB)",
        ["AdbOnPath"] = "adb added to your PATH ({0}): new consoles can use it.",
        ["LocateAdbTooltip"] = "Find adb.exe on this PC…",
        ["DebuggingHint"] = "On the phone: Settings › Developer options › USB debugging. On Xiaomi, Redmi and POCO also turn on «USB debugging (Security settings)»: without it the screen shows but the mouse and keyboard cannot control it.",
        ["AdbDownloading"] = "adb was not found: downloading the Android platform-tools from Google…",
        ["AdbInstalled"] = "adb ready ({0}).",
        ["AdbDownloadFailed"] = "Could not download adb: {0}",
        ["OpenOnConnectTooltip"] = "Open when a phone is plugged in (starts with Windows, in the tray)",
        ["OpenOnConnectOn"] = "The app will start with Windows and open when a phone is plugged in.",
        ["OpenOnConnectOff"] = "The app will no longer start with Windows.",
        ["TrayWaiting"] = "sOC Phone Mirror · waiting for a phone",
        ["TrayOpen"] = "Open",
        ["TrayHidden"] = "Still running here, in the tray (^): click to open, or Quit from the menu.",
        ["InstancesIntro"] = "sOC Phone Mirror is already open. Show one of these windows, or open a new one (one window per phone).",
        ["InstancesOpen"] = "Show the selected window",
        ["InstancesNew"] = "New window",
        ["TrayQuit"] = "Quit",
        ["NoDevices"] = "No phone connected. Plug it in by USB and enable USB debugging in the developer options.",
        ["Unauthorized"] = "The phone is asking whether to trust this computer. Accept on its screen.",
        ["Connecting"] = "Connecting to {0}…",
        ["Connected"] = "{0} · {1}×{2}",
        ["Disconnected"] = "Disconnected",
        ["SessionEnded"] = "The connection ended: {0}",
        ["RefreshTooltip"] = "Look for phones again",
        ["WifiTooltip"] = "Connect over Wi-Fi",
        ["WifiTitle"] = "Connect over Wi-Fi",
        ["WifiIntro"] = "The phone must have network debugging enabled, and be on the same network as this PC.",
        ["WifiAddressLabel"] = "Phone address (IP or IP:port)",
        ["WifiConnectTooltip"] = "Connect",
        ["WifiForgetTooltip"] = "Forget this address",
        ["WifiPairTitle"] = "First time on Android 11 or later",
        ["WifiPairIntro"] = "In Developer options › Wireless debugging › Pair device with pairing code, the phone shows an address with its port and a six-digit code. Enter them here, pair, and then connect above with the address shown under \"Wireless debugging\" (a different port).",
        ["WifiPairAddressLabel"] = "Pairing address (IP:port)",
        ["WifiPairCodeLabel"] = "Pairing code",
        ["WifiPairTooltip"] = "Pair",
        ["WifiConnecting"] = "Connecting to {0}…",
        ["WifiConnected"] = "Connected to {0}.",
        ["WifiPairing"] = "Pairing with {0}…",
        ["WifiPaired"] = "Paired. Now connect with the address shown under \"Wireless debugging\".",
        ["WifiFailed"] = "Could not connect: {0}",
        ["WifiHint"] = "Alternatively, with the phone plugged in once, run \"adb tcpip 5555\" and it will accept connections to its IP until it restarts.",
        ["ConnectTooltip"] = "Connect",
        ["DisconnectTooltip"] = "Disconnect",
        ["HomeTooltip"] = "Home",
        ["BackTooltip"] = "Back",
        ["RecentsTooltip"] = "Recent apps",
        ["PowerTooltip"] = "Screen on / off",
        ["RotateTooltip"] = "Rotate",
        ["VolumeUpTooltip"] = "Volume up",
        ["VolumeDownTooltip"] = "Volume down",
        ["MuteTooltip"] = "Mute / unmute the phone",
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
        ["AppTitle"] = "sOC Phone Mirror",
        ["AboutTitle"] = "Acerca de",
        ["AboutTooltip"] = "Acerca de",
        ["AboutDescription"] = "Ver y manejar desde Windows el móvil conectado por USB: tocar, escribir, copiar, pegar y arrastrar ficheros.",
        ["Publisher"] = "Socratic",
        ["Contact"] = "Contacto",
        ["WriteAuthor"] = "Escribir al autor",
        ["ContactHint"] = "Sugerencias, fallos o ideas: todo se lee.",
        ["LanguageTitle"] = "Idioma",
        ["LanguageHint"] = "El idioma se aplica al momento.",
        ["Privacy"] = "Privacidad",
        ["PrivacyText"] = "Todo pasa entre este PC y el móvil por el cable USB: la pantalla se descodifica aquí y los toques van directos al móvil. Nada sale a internet y no se guarda nada salvo las capturas que guardes y el texto que copies. Sin anuncios, sin rastreadores y sin analítica.",
        ["License"] = "Licencia",
        ["LicenseText"] = "Software libre bajo licencia MIT. El servidor de scrcpy (Apache 2.0) y el resto de componentes de terceros están en THIRD-PARTY-NOTICES.md.",
        ["LicenseLine"] = "MIT License \u00b7 Copyright \u00a9 2026 Socratic",
        ["LegalTitle"] = "Aviso legal",
        ["LegalText1"] = "Este software se entrega «tal cual», sin garantías de ningún tipo, expresas o implícitas.",
        ["LegalText2"] = "En ningún caso los autores serán responsables de reclamaciones, daños u otras responsabilidades derivadas del uso de este software.",
        ["WarningText"] = "⚠️ Uso bajo su propio riesgo",
        ["Close"] = "Cerrar",
        ["NoAdb"] = "No se encuentra adb.exe (platform-tools de Android, de Google) en este PC. Descárgalo aquí, o señala una copia que ya tengas.",
        ["DownloadAdbTooltip"] = "Descargar las platform-tools de Android de Google (unos 7 MB)",
        ["AdbOnPath"] = "adb añadido a tu PATH ({0}): las consolas nuevas ya pueden usarlo.",
        ["LocateAdbTooltip"] = "Buscar adb.exe en este PC…",
        ["DebuggingHint"] = "En el móvil: Ajustes › Opciones de desarrollador › Depuración USB. En Xiaomi, Redmi y POCO activa además «Depuración USB (ajustes de seguridad)»: sin eso se ve la pantalla pero el ratón y el teclado no la controlan.",
        ["AdbDownloading"] = "No hay adb: descargando las platform-tools de Android de Google…",
        ["AdbInstalled"] = "adb listo ({0}).",
        ["AdbDownloadFailed"] = "No se ha podido descargar adb: {0}",
        ["OpenOnConnectTooltip"] = "Abrir al conectar un móvil (arranca con Windows, en la bandeja)",
        ["OpenOnConnectOn"] = "La aplicación arrancará con Windows y se abrirá al enchufar un móvil.",
        ["OpenOnConnectOff"] = "La aplicación ya no arrancará con Windows.",
        ["TrayWaiting"] = "sOC Phone Mirror · esperando un móvil",
        ["TrayOpen"] = "Abrir",
        ["TrayHidden"] = "Sigue aquí, en el área de iconos (^): clic para abrir, o Salir desde el menú.",
        ["InstancesIntro"] = "sOC Phone Mirror ya está abierto. Enseña una de estas ventanas, o abre otra nueva (una ventana por móvil).",
        ["InstancesOpen"] = "Enseñar la ventana elegida",
        ["InstancesNew"] = "Ventana nueva",
        ["TrayQuit"] = "Salir",
        ["NoDevices"] = "No hay ningún móvil conectado. Conéctalo por USB y activa la depuración USB en las opciones de desarrollador.",
        ["Unauthorized"] = "El móvil pregunta si confía en este PC. Acepta en su pantalla.",
        ["Connecting"] = "Conectando con {0}…",
        ["Connected"] = "{0} · {1}×{2}",
        ["Disconnected"] = "Desconectado",
        ["SessionEnded"] = "Se ha cortado la conexión: {0}",
        ["RefreshTooltip"] = "Volver a buscar móviles",
        ["WifiTooltip"] = "Conectar por Wi-Fi",
        ["WifiTitle"] = "Conectar por Wi-Fi",
        ["WifiIntro"] = "El móvil tiene que tener activada la depuración por red y estar en la misma red que este PC.",
        ["WifiAddressLabel"] = "Dirección del móvil (IP o IP:puerto)",
        ["WifiConnectTooltip"] = "Conectar",
        ["WifiForgetTooltip"] = "Olvidar esta dirección",
        ["WifiPairTitle"] = "La primera vez en Android 11 o posterior",
        ["WifiPairIntro"] = "En Opciones de desarrollador › Depuración inalámbrica › Vincular dispositivo con un código, el móvil enseña una dirección con su puerto y un código de seis cifras. Ponlos aquí, vincula, y después conecta arriba con la dirección que sale en «Depuración inalámbrica» (es otro puerto).",
        ["WifiPairAddressLabel"] = "Dirección de vinculación (IP:puerto)",
        ["WifiPairCodeLabel"] = "Código de vinculación",
        ["WifiPairTooltip"] = "Vincular",
        ["WifiConnecting"] = "Conectando con {0}…",
        ["WifiConnected"] = "Conectado con {0}.",
        ["WifiPairing"] = "Vinculando con {0}…",
        ["WifiPaired"] = "Vinculado. Ahora conecta con la dirección que sale en «Depuración inalámbrica».",
        ["WifiFailed"] = "No se ha podido conectar: {0}",
        ["WifiHint"] = "Otra forma: con el móvil enchufado una vez, ejecuta «adb tcpip 5555» y aceptará conexiones a su IP hasta que se reinicie.",
        ["ConnectTooltip"] = "Conectar",
        ["DisconnectTooltip"] = "Desconectar",
        ["HomeTooltip"] = "Inicio",
        ["BackTooltip"] = "Atrás",
        ["RecentsTooltip"] = "Aplicaciones recientes",
        ["PowerTooltip"] = "Encender / apagar la pantalla",
        ["RotateTooltip"] = "Girar",
        ["VolumeUpTooltip"] = "Subir volumen",
        ["VolumeDownTooltip"] = "Bajar volumen",
        ["MuteTooltip"] = "Silenciar / activar el sonido del móvil",
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
