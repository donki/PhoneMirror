# Avisos de terceros — sOC Phone Mirror

| Componente | Uso | Licencia | Titular |
|---|---|---|---|
| `scrcpy-server` v4.1 (`Assets/scrcpy-server`) | Servidor que corre en el móvil: captura la pantalla, la codifica en H.264 y recibe los toques. Se distribuye tal cual, sin modificar, y se empuja al móvil por adb. SHA-256 `deacb991ed2509715160ffdc7907e47b4160eb30d1566217e9047fd5b8850cae`. | Apache 2.0 | Genymobile — <https://github.com/Genymobile/scrcpy> |
| `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll` (Android platform-tools **r37.0.1**, `Assets/platform-tools`) | Listar dispositivos, empujar ficheros, redirigir puertos, lanzar el servidor. Se distribuyen **tal cual, sin modificar**, con el `NOTICE.txt` del zip de Google. Zip `platform-tools_r37.0.1-win.zip`, SHA-256 `45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99` (`tools/get-platform-tools.ps1`). Por qué se pueden redistribuir: son componentes de código abierto del SDK (Apache 2.0, en AOSP: `packages/modules/adb` y `development/host/windows/usb`), y la cláusula 3.6 del Android SDK License Agreement remite esos componentes a su propia licencia, no al acuerdo; es lo mismo que hace scrcpy en su paquete de Windows. Si faltaran (copia sin `Assets`), la aplicación usa el adb del equipo o lo descarga de Google a `%LOCALAPPDATA%\sOCPhoneMirror\platform-tools`. | Apache 2.0 | Google / AOSP |
| Media Foundation (`mfplat.dll`, MFT H.264) | Descodificar el vídeo. API del sistema operativo. | Windows | Microsoft |

Todo el código del cliente (`*.cs`, `*.xaml`) es propio y va bajo MIT (`LICENSE`).
