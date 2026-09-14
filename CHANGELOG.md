# Changelog — sOC Phone Mirror

## 2026.9.14.0 — adb se instala solo y la ventana se abre al enchufar el móvil

- **Sin adb, se descarga**: si en el equipo no hay platform-tools, la aplicación las baja de Google
  (`dl.google.com`, unos 8 MB) al primer arranque y deja `adb.exe` en
  `%LOCALAPPDATA%\sOCPhoneMirror\platform-tools`. No se redistribuye nada: la licencia del SDK de
  Android no lo permite, y por eso no va en el paquete ni en un zip propio.
- **Abrir al conectar un móvil** (botón 📱 en la barra, como Vysor): la aplicación arranca con
  Windows escondida en la bandeja y, en cuanto adb ve un móvil (por USB o por Wi-Fi), enseña la
  ventana y lo espeja. Al desenchufarlo vuelve a la bandeja; cerrar la ventana la esconde, y
  «Salir» está en el menú del icono. El ajuste es la propia entrada de arranque de Windows.

## 2026.9.13.2 — Conectar por Wi-Fi

- Botón **Wi-Fi** junto al de buscar móviles: pide la dirección del móvil (IP o IP:puerto) y lo
  conecta por red con adb, sin abrir una consola. Para la «Depuración inalámbrica» de Android 11+,
  la misma ventana hace la vinculación con el código de seis cifras.
- Las direcciones que han funcionado se recuerdan y se vuelven a conectar al arrancar: el móvil que
  esté encendido y en la misma red aparece en la lista solo.

## 2026.9.13.1 — Botón de silencio

- Botón de **silenciar** junto a los de volumen: manda la tecla de silencio del móvil (alterna
  silencio / sonido). Es el sonido del teléfono lo que se silencia; esta ventana no reproduce audio.

## 2026.9.11.1 — Primera versión

- Espejo del móvil conectado por USB en una ventana de Windows: `scrcpy-server` en el móvil,
  H.264 descodificado con Media Foundation (sin FFmpeg ni ninguna dependencia LGPL) y pintado en un
  `WriteableBitmap`.
- Ratón: clic y arrastrar tocan; botón derecho, atrás; central, inicio; rueda, desplazar
  (con Mayús, horizontal).
- Teclado: las letras y signos van como texto; flechas, Intro, Retroceso, Supr, Esc, Tab, Inicio,
  Fin, AvPág, RePág y las teclas de volumen y multimedia, como teclas. Ctrl+V pega el portapapeles
  del PC en el móvil; Ctrl+C trae el del móvil.
- Barra de botones: atrás, inicio, recientes, notificaciones, apagar/encender pantalla, girar,
  volumen, captura (a `Imágenes\Phone Mirror`), copiar/pegar portapapeles, siempre encima, idioma.
- Arrastrar un APK lo instala; cualquier otro fichero se copia a `Download` del móvil.
- La ventana se adapta al formato del móvil: al girar (un juego apaisado) pasa a apaisada y al
  volver, a vertical, conservando el tamaño y sin salirse de la pantalla.
- «Acerca de» con la estructura canónica del catálogo (contacto, idioma, privacidad, licencia,
  aviso legal).
- `--connect` y `--serial <serie>` para arrancar ya conectado; una ventana por móvil.
- Detecta los móviles cada tres segundos hasta que hay sesión; español e inglés; tema claro y
  oscuro siguiendo a Windows.
