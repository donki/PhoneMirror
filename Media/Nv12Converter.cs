namespace PhoneMirror.Media;

/// <summary>
/// NV12 → BGRA de 32 bits, que es lo que pinta un <c>WriteableBitmap</c>.
/// </summary>
/// <remarks>
/// BT.601 de rango limitado, que es lo que produce el codificador del movil para un espejo de
/// pantalla, con la aritmetica en enteros y las tablas de conversion precalculadas. Un cuadro de
/// 1280×720 se convierte en pocos milisegundos; el trabajo se reparte por bloques de filas entre
/// los nucleos disponibles.
/// </remarks>
internal static class Nv12Converter
{
    private static readonly int[] YTable = new int[256];
    private static readonly int[] RVTable = new int[256];
    private static readonly int[] GUTable = new int[256];
    private static readonly int[] GVTable = new int[256];
    private static readonly int[] BUTable = new int[256];

    static Nv12Converter()
    {
        for (var i = 0; i < 256; i++)
        {
            YTable[i] = (int)(1.164 * (i - 16) * 65536);
            RVTable[i] = (int)(1.596 * (i - 128) * 65536);
            GUTable[i] = (int)(0.391 * (i - 128) * 65536);
            GVTable[i] = (int)(0.813 * (i - 128) * 65536);
            BUTable[i] = (int)(2.018 * (i - 128) * 65536);
        }
    }

    /// <summary>Convierte el cuadro en <paramref name="destination"/> (ancho × alto × 4 bytes).</summary>
    public static unsafe void ToBgra(in DecodedFrame frame, byte[] destination)
    {
        var width = frame.Width;
        var height = frame.Height;
        if (destination.Length < width * height * 4)
            throw new ArgumentException("El destino es mas pequeño que el cuadro.", nameof(destination));

        var yBase = (byte*)frame.YPlane;
        var uvBase = (byte*)frame.UvPlane;
        var pitch = frame.Pitch;

        fixed (byte* dstBase = destination)
        {
            var dst = dstBase;

            // Filas de dos en dos: comparten la fila de croma.
            var pairs = (height + 1) / 2;
            var parallel = pairs >= 64 && Environment.ProcessorCount > 1;

            if (parallel)
            {
                // Los punteros no se pueden capturar en una lambda: viajan como enteros.
                nint yPtr = (nint)yBase, uvPtr = (nint)uvBase, dstPtr = (nint)dst;
                Parallel.For(0, pairs, pair =>
                    ConvertRowPair(pair, width, height, (byte*)yPtr, (byte*)uvPtr, pitch, (byte*)dstPtr));
            }
            else
            {
                for (var pair = 0; pair < pairs; pair++)
                    ConvertRowPair(pair, width, height, yBase, uvBase, pitch, dst);
            }
        }
    }

    private static unsafe void ConvertRowPair(int pair, int width, int height, byte* yBase, byte* uvBase, int pitch, byte* dst)
    {
        var uvRow = uvBase + (nint)pair * pitch;

        for (var sub = 0; sub < 2; sub++)
        {
            var y = pair * 2 + sub;
            if (y >= height)
                return;

            var yRow = yBase + (nint)y * pitch;
            var outRow = dst + (nint)y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var uvIndex = x & ~1;
                var u = uvRow[uvIndex];
                var v = uvRow[uvIndex + 1];
                var luma = YTable[yRow[x]];

                var r = (luma + RVTable[v]) >> 16;
                var g = (luma - GUTable[u] - GVTable[v]) >> 16;
                var b = (luma + BUTable[u]) >> 16;

                var p = outRow + x * 4;
                p[0] = Clamp(b);
                p[1] = Clamp(g);
                p[2] = Clamp(r);
                p[3] = 255;
            }
        }
    }

    private static byte Clamp(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
}
