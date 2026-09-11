# Phone Mirror

Ver y manejar el móvil Android conectado por cable desde una ventana de Windows.

- **Qué hace.** Enseña la pantalla del móvil en el PC y deja tocarla con el ratón y escribir con el
  teclado. Botones de atrás, inicio, recientes, notificaciones, pantalla, giro y volumen; captura de
  pantalla; portapapeles en los dos sentidos; arrastrar un APK lo instala y cualquier otro fichero
  se copia a `Download`.
- **Cómo funciona.** Empuja `scrcpy-server` (Apache 2.0) al móvil por adb y lo lanza; el servidor
  codifica la pantalla en H.264 y lo manda por un socket, y recibe los toques por otro. El cliente
  descodifica con el MFT H.264 de **Media Foundation**, que trae Windows: no hay FFmpeg ni ninguna
  dependencia LGPL (constitución, sección 4). El protocolo es el de scrcpy 4.1 y está documentado en
  `Services/ScrcpySession.cs` y `Services/ControlChannel.cs`.

## Ejecutar

```
dotnet run --project PhoneMirror.csproj
```

Hace falta:

- **adb.exe** (Android platform-tools). Se busca en la variable `ADB`, junto al ejecutable, en
  `ANDROID_HOME` / `ANDROID_SDK_ROOT`, en `C:\Program Files (x86)\Android\android-sdk` y en el
  `PATH`.
- El móvil por USB con **depuración USB** activada y el PC aceptado en el móvil.
- `Assets\scrcpy-server` (va en el repositorio; `tools\get-scrcpy-server.ps1` lo vuelve a bajar y
  comprueba el SHA-256).

Opciones: `--connect` conecta solo con el primer móvil listo; `--serial <serie>` conecta con ese
móvil (una ventana por móvil: se puede abrir una para el teléfono y otra para la tablet).

## Manejo

| Gesto | Qué hace |
|---|---|
| Clic y arrastrar | Tocar y deslizar |
| Botón derecho | Atrás (o encender la pantalla si está apagada) |
| Botón central | Inicio |
| Rueda / Mayús+rueda | Desplazar vertical / horizontal |
| Teclear | Escribe en el móvil (letras como texto; flechas, Intro, Retroceso… como teclas) |
| Ctrl+V | Pega el portapapeles del PC en el móvil |
| Ctrl+C | Copia lo seleccionado en el móvil y lo trae al PC |
| Arrastrar un `.apk` | Lo instala |
| Arrastrar otro fichero | Lo copia a `/sdcard/Download` |

## A qué accede

- Al móvil, por adb: empuja `scrcpy-server` a `/data/local/tmp`, redirige un puerto local
  (27183–27282) a un socket abstracto y lanza el servidor con `app_process`. Al cerrar, mata el
  servidor y quita la redirección.
- Al portapapeles de Windows, solo cuando se pide (Ctrl+C / Ctrl+V / botones).
- A `Imágenes\Phone Mirror` para las capturas.

No sale nada a internet. No guarda nada del móvil salvo lo que el usuario capture o copie.

## Qué puede romper

Nada del móvil: el servidor no toca datos, solo captura la pantalla e inyecta eventos de entrada.
Si se cierra mal, puede quedar una redirección `adb forward` colgada; `adb forward --remove-all`
la quita.
