# Changelog — Phone Mirror

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
- Detecta los móviles cada tres segundos hasta que hay sesión; español e inglés; tema claro y
  oscuro siguiendo a Windows.
