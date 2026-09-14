# Avisos de terceros — sOC Phone Mirror

| Componente | Uso | Licencia | Titular |
|---|---|---|---|
| `scrcpy-server` v4.1 (`Assets/scrcpy-server`) | Servidor que corre en el móvil: captura la pantalla, la codifica en H.264 y recibe los toques. Se distribuye tal cual, sin modificar, y se empuja al móvil por adb. SHA-256 `deacb991ed2509715160ffdc7907e47b4160eb30d1566217e9047fd5b8850cae`. | Apache 2.0 | Genymobile — <https://github.com/Genymobile/scrcpy> |
| `adb.exe` (Android platform-tools) | Listar dispositivos, empujar ficheros, redirigir puertos, lanzar el servidor. **No se distribuye**: se usa el del SDK instalado en el equipo y, si no hay, la aplicación lo descarga **de Google** (`dl.google.com/android/repository/platform-tools-latest-windows.zip`) a `%LOCALAPPDATA%\sOCPhoneMirror\platform-tools`. La licencia del SDK de Android no permite redistribuirlo, y por eso no va ni en el paquete ni en un zip propio. | Licencia del SDK de Android (adb es Apache 2.0 en AOSP) | Google / AOSP |
| Media Foundation (`mfplat.dll`, MFT H.264) | Descodificar el vídeo. API del sistema operativo. | Windows | Microsoft |

Todo el código del cliente (`*.cs`, `*.xaml`) es propio y va bajo MIT (`LICENSE`).
