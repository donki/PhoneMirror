using System.Buffers.Binary;
using System.Text;

namespace PhoneMirror.Services;

/// <summary>
/// La parte pura del protocolo de video de scrcpy 4.1 (sin sockets ni procesos): la orden que
/// arranca el servidor, el nombre del movil, el codec y la cabecera de 12 bytes de cada paquete.
/// La usa <see cref="ScrcpySession"/>; aparte para poder probarla.
/// </summary>
public static class ScrcpyProtocol
{
    /// <summary>Viaja como primer argumento: el servidor se niega a arrancar si no coincide.</summary>
    public const string ServerVersion = "4.1";

    public const string RemoteServerPath = "/data/local/tmp/scrcpy-server.jar";

    public const int DeviceNameLength = 64;

    public const int HeaderSize = 12;

    /// <summary>"h264" en big-endian.</summary>
    public const uint CodecH264 = 0x68323634;

    public const ulong PacketFlagConfig = 1UL << 62;
    public const ulong PacketFlagKeyFrame = 1UL << 61;
    public const ulong PacketPtsMask = PacketFlagKeyFrame - 1;

    /// <summary>El socket abstracto al que se redirige el puerto local.</summary>
    public static string ForwardSocket(int scid) => $"localabstract:scrcpy_{scid:x8}";

    /// <summary>La orden de <c>adb shell</c> que lanza el servidor en modo <c>tunnel_forward</c>.</summary>
    public static string ServerCommand(int scid, int maxSize, int bitRate, int maxFps) => string.Join(' ',
        $"CLASSPATH={RemoteServerPath}", "app_process", "/", "com.genymobile.scrcpy.Server", ServerVersion,
        $"scid={scid:x8}", "log_level=info", "audio=false", "tunnel_forward=true", "control=true",
        $"max_size={maxSize}", $"video_bit_rate={bitRate}", $"max_fps={maxFps}",
        "clipboard_autosync=true", "stay_awake=true", "cleanup=true");

    /// <summary>Los 64 bytes del nombre, en UTF-8 y rellenos de ceros.</summary>
    public static string DeviceName(ReadOnlySpan<byte> name) => Encoding.UTF8.GetString(name).TrimEnd('\0');

    public static uint CodecId(ReadOnlySpan<byte> codec) => BinaryPrimitives.ReadUInt32BigEndian(codec);

    /// <summary>Lee la cabecera de 12 bytes de un paquete de video.</summary>
    public static VideoPacketHeader ReadHeader(ReadOnlySpan<byte> header)
    {
        if ((header[0] & 0x80) != 0)
        {
            // Paquete de sesion: solo trae el tamaño nuevo del video.
            return new VideoPacketHeader(true, BinaryPrimitives.ReadInt32BigEndian(header[4..]),
                BinaryPrimitives.ReadInt32BigEndian(header[8..]), false, 0, 0);
        }

        var ptsAndFlags = BinaryPrimitives.ReadUInt64BigEndian(header);
        return new VideoPacketHeader(false, 0, 0, (ptsAndFlags & PacketFlagConfig) != 0,
            (long)(ptsAndFlags & PacketPtsMask), BinaryPrimitives.ReadInt32BigEndian(header[8..]));
    }
}

/// <summary>
/// Cabecera de un paquete de video: de sesion (<see cref="Width"/> × <see cref="Height"/>) o de
/// medio (<see cref="IsConfig"/>, <see cref="Pts"/> y <see cref="Length"/> bytes de H.264 detras).
/// </summary>
public readonly record struct VideoPacketHeader(bool IsSession, int Width, int Height, bool IsConfig, long Pts, int Length);
