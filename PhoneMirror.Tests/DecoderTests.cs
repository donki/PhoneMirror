using System.Runtime.InteropServices;
using PhoneMirror.Media;

namespace PhoneMirror.Tests;

/// <summary>
/// El descodificador H.264 de Windows (Media Foundation) con un video hecho a mano
/// (<see cref="H264Sample"/>) y la conversion NV12 → BGRA, con colores que se saben de antemano.
/// </summary>
public sealed class DecoderTests
{
    private static List<(int W, int H, byte[] Bgra)> Decode(params byte[][] accessUnits)
    {
        var frames = new List<(int, int, byte[])>();
        using var decoder = new H264Decoder();
        long pts = 0;
        foreach (var unit in accessUnits)
        {
            decoder.Decode(unit, pts, (in DecodedFrame f) =>
            {
                var pixels = new byte[f.Width * f.Height * 4];
                Nv12Converter.ToBgra(in f, pixels);
                frames.Add((f.Width, f.Height, pixels));
            });
            pts += 16_666;
        }
        return frames;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(byte[] bgra, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]);
    }

    [Fact]
    public void Descodifica_CuadrosDeUnColor_YLosConvierteABgra()
    {
        var config = H264Sample.Config(64, 64);
        var red = H264Sample.Frame(64, 64, 81, 90, 240, idrPicId: 0);
        var blue = H264Sample.Frame(64, 64, 41, 240, 110, idrPicId: 1);
        var trace = new List<string>();
        H264Decoder.Trace = trace.Add;
        try
        {
            var frames = Decode([.. config, .. red], blue, red);
            Assert.True(frames.Count >= 2, $"cuadros: {frames.Count}");
            var (w, h, first) = frames[0];
            Assert.Equal((64, 64), (w, h));
            var p = Pixel(first, w, 10, 10);
            Assert.True(p.R > 240 && p.G < 15 && p.B < 15, $"rojo: {p}");
            Assert.Equal(255, p.A);
            var q = Pixel(frames[1].Bgra, 64, 63, 63);
            Assert.True(q.B > 230 && q.R < 15 && q.G < 15, $"azul: {q}");
            Assert.Contains(trace, t => t.StartsWith("ProcessInput", StringComparison.Ordinal));
        }
        finally
        {
            H264Decoder.Trace = null;
        }
    }

    [Fact]
    public void Descodifica_ConRecorte_LaZonaVisible()
    {
        var frames = Decode([.. H264Sample.Config(48, 48, cropBottom: 8), .. H264Sample.Frame(48, 48, 235, 128, 128, 0)],
            H264Sample.Frame(48, 48, 16, 128, 128, 1));
        var (w, h, white) = frames[0];
        Assert.Equal((48, 40), (w, h));
        Assert.True(Pixel(white, w, 0, h - 1).R > 245);
        Assert.True(Pixel(frames[^1].Bgra, w, 5, 5).R < 10);
    }

    [Fact]
    public void Basura_NoSacaCuadros_YCerradoNoSeUsa()
    {
        var decoder = new H264Decoder();
        var frames = 0;
        try
        {
            decoder.Decode([0, 0, 0, 1, 0x09, 0xF0], 0, (in DecodedFrame _) => frames++);   // un delimitador suelto
        }
        catch (COMException)
        {
            // Algunas versiones del descodificador lo rechazan: tambien vale, no saca nada.
        }
        Assert.Equal(0, frames);
        Assert.True(decoder.Width > 0 && decoder.Height > 0);   // el tamaño de partida que ofrece el descodificador
        decoder.Dispose();
        decoder.Dispose();   // dos veces: nada
        Assert.Throws<ObjectDisposedException>(() => decoder.Decode([1], 0, (in DecodedFrame _) => { }));
    }

    [Fact]
    public unsafe void Nv12_Colores_Paso_FilasImpares_YDestinoPequeño()
    {
        // 3×3 con un paso de 8: Y en filas, UV entrelazado por pares de columnas.
        const int pitch = 8;
        var y = new byte[pitch * 3];
        var uv = new byte[pitch * 2];
        Array.Fill(y, (byte)235);
        y[pitch * 2 + 2] = 16;   // la ultima, negra
        for (var i = 0; i < uv.Length; i += 2)
        {
            uv[i] = 128;
            uv[i + 1] = 128;
        }
        var bgra = new byte[3 * 3 * 4];
        fixed (byte* py = y, puv = uv)
        {
            var frame = new DecodedFrame(3, 3, (IntPtr)py, (IntPtr)puv, pitch);
            Nv12Converter.ToBgra(in frame, bgra);
            IntPtr yp = (IntPtr)py, uvp = (IntPtr)puv;
            Assert.Throws<ArgumentException>(() =>
            {
                var small = new DecodedFrame(3, 3, yp, uvp, pitch);
                Nv12Converter.ToBgra(in small, new byte[10]);
            });
        }
        Assert.Equal(new byte[] { 254, 254, 254, 255 }, bgra[..4]);   // Y=235 es el blanco de video (1,164·219 = 254,9)
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, bgra[^4..]);
        Assert.Equal(3 * 3 * 4, bgra.Length);
    }

    [Fact]
    public unsafe void Nv12_Grande_VaEnParalelo_YDaLoMismo()
    {
        const int width = 32, height = 200;
        var y = new byte[width * height];
        var uv = new byte[width * height / 2];
        for (var i = 0; i < y.Length; i++)
            y[i] = (byte)(16 + i % 200);
        Array.Fill(uv, (byte)128);
        var bgra = new byte[width * height * 4];
        fixed (byte* py = y, puv = uv)
        {
            var frame = new DecodedFrame(width, height, (IntPtr)py, (IntPtr)puv, width);
            Nv12Converter.ToBgra(in frame, bgra);
        }
        for (var i = 0; i < y.Length; i += 37)
        {
            var expected = Math.Clamp((int)(1.164 * (y[i] - 16)), 0, 255);
            Assert.InRange(bgra[i * 4 + 2], expected - 1, expected + 1);
            Assert.Equal(bgra[i * 4], bgra[i * 4 + 2]);   // gris: R = B
        }
    }
}
