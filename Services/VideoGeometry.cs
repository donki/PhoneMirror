using System.Windows;

namespace PhoneMirror.Services;

/// <summary>
/// De un punto del area del espejo a coordenadas del video. La imagen va con
/// <c>Stretch="Uniform"</c>: ocupa el mayor rectangulo proporcional, centrado.
/// </summary>
public static class VideoGeometry
{
    /// <summary>El punto en el video, o <c>null</c> si cae fuera de la imagen (o no hay video).</summary>
    public static (int X, int Y)? ToVideo(double areaWidth, double areaHeight, int videoWidth, int videoHeight, double x, double y)
    {
        if (videoWidth == 0 || videoHeight == 0)
            return null;

        var scale = Math.Min(areaWidth / videoWidth, areaHeight / videoHeight);
        var shownWidth = videoWidth * scale;
        var shownHeight = videoHeight * scale;
        var offsetX = (areaWidth - shownWidth) / 2;
        var offsetY = (areaHeight - shownHeight) / 2;

        var vx = (x - offsetX) / scale;
        var vy = (y - offsetY) / scale;

        if (vx < 0 || vy < 0 || vx >= videoWidth || vy >= videoHeight)
            return null;

        return ((int)vx, (int)vy);
    }

    /// <summary>
    /// Donde y de que tamaño poner la ventana para un video de <paramref name="videoWidth"/> ×
    /// <paramref name="videoHeight"/>: mas o menos la misma superficie de espejo que ahora (o
    /// 480×800 la primera vez) con el formato nuevo, sin pasar del area de trabajo ni del minimo.
    /// </summary>
    /// <param name="window">Tamaño actual de la ventana entera.</param>
    /// <param name="mirror">Tamaño actual del area del espejo (la diferencia es el marco y las barras).</param>
    public static Rect FitWindow(int videoWidth, int videoHeight, Size window, Size mirror, Size minimum, Point position, Rect work)
    {
        var chromeWidth = Math.Max(0, window.Width - mirror.Width);
        var chromeHeight = Math.Max(0, window.Height - mirror.Height);

        var maxMirrorWidth = work.Width - chromeWidth - 24;
        var maxMirrorHeight = work.Height - chromeHeight - 24;

        // Misma superficie que ahora (o un tamaño razonable la primera vez), con el formato nuevo.
        var area = Math.Max(mirror.Width * mirror.Height, 480.0 * 800.0);
        var ratio = (double)videoWidth / videoHeight;
        var mirrorWidth = Math.Sqrt(area * ratio);
        var mirrorHeight = mirrorWidth / ratio;

        var scale = Math.Min(1.0, Math.Min(maxMirrorWidth / mirrorWidth, maxMirrorHeight / mirrorHeight));
        mirrorWidth *= scale;
        mirrorHeight *= scale;

        var newWidth = Math.Max(minimum.Width, mirrorWidth + chromeWidth);
        var newHeight = Math.Max(minimum.Height, mirrorHeight + chromeHeight);

        // Que no se salga por la derecha o por abajo al crecer.
        var left = Math.Max(work.Left, Math.Min(position.X, work.Right - newWidth));
        var top = Math.Max(work.Top, Math.Min(position.Y, work.Bottom - newHeight));
        return new Rect(left, top, newWidth, newHeight);
    }

    /// <summary>Como <see cref="ToVideo"/>, pero llevando al borde lo que cae fuera (arrastrar sin cortar el gesto).</summary>
    public static (int X, int Y) Clamp(double areaWidth, double areaHeight, int videoWidth, int videoHeight, double x, double y)
    {
        var scale = Math.Min(areaWidth / videoWidth, areaHeight / videoHeight);
        var offsetX = (areaWidth - videoWidth * scale) / 2;
        var offsetY = (areaHeight - videoHeight * scale) / 2;
        var vx = Math.Clamp((x - offsetX) / scale, 0, videoWidth - 1);
        var vy = Math.Clamp((y - offsetY) / scale, 0, videoHeight - 1);
        return ((int)vx, (int)vy);
    }
}
