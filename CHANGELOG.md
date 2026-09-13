# Changelog — sOC Phone Mirror

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
