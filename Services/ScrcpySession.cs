using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PhoneMirror.Media;

namespace PhoneMirror.Services;

/// <summary>Un cuadro listo para pintar: BGRA de 32 bits, ancho × alto × 4 bytes.</summary>
public sealed record VideoFrame(int Width, int Height, byte[] Pixels);

/// <summary>
/// Una sesion de espejo: el servidor de scrcpy corriendo en el movil, el video llegando por un
/// socket y el canal de control por otro.
/// </summary>
/// <remarks>
/// <para><b>Como arranca.</b> Se empuja <c>scrcpy-server</c> a <c>/data/local/tmp</c>, se redirige
/// un puerto local al socket abstracto <c>scrcpy_&lt;scid&gt;</c> y se lanza el servidor por
/// <c>app_process</c> en modo <c>tunnel_forward</c>: el servidor escucha y nosotros conectamos dos
/// veces, primero el video y despues el control (el audio va desactivado). El primer byte del
/// video es un byte de relleno que solo sirve para detectar que la conexion es de verdad.</para>
///
/// <para><b>Que llega por el video.</b> 64 bytes con el nombre del movil, 4 bytes con el codec y a
/// partir de ahi paquetes con cabecera de 12 bytes: o «sesion» (nuevo tamaño de video, al girar) o
/// «medio» (PTS y flags + tamaño + datos H.264). Los paquetes de configuracion (SPS/PPS) se pegan
/// delante del siguiente cuadro, igual que hace el cliente oficial.</para>
///
/// <para>Formato de la version 4.1 del servidor; la constante <see cref="ServerVersion"/> viaja
/// como primer argumento y el servidor se niega a arrancar si no coincide.</para>
/// </remarks>
public sealed class ScrcpySession : IAsyncDisposable
{
    public const string ServerVersion = "4.1";
    private const string RemoteServerPath = "/data/local/tmp/scrcpy-server.jar";
    private const int DeviceNameLength = 64;
    private const int HeaderSize = 12;

    private const ulong PacketFlagConfig = 1UL << 62;
    private const ulong PacketFlagKeyFrame = 1UL << 61;
    private const ulong PacketPtsMask = PacketFlagKeyFrame - 1;

    private readonly AdbService _adb;
    private readonly string _serial;
    private readonly int _localPort;
    private readonly int _scid;
    private readonly CancellationTokenSource _cancellation = new();

    private Process? _server;
    private TcpClient? _videoSocket;
    private TcpClient? _controlSocket;
    private Task? _videoTask;
    private Task? _controlTask;

    public ScrcpySession(AdbService adb, string serial)
    {
        _adb = adb;
        _serial = serial;
        _localPort = 27183 + Random.Shared.Next(0, 100);
        _scid = Random.Shared.Next(1, int.MaxValue);
    }

    /// <summary>Nombre que dice el movil (64 bytes en el arranque del video).</summary>
    public string DeviceName { get; private set; } = string.Empty;

    /// <summary>Tamaño actual del video. Es el que hay que mandar con cada toque.</summary>
    public int VideoWidth { get; private set; }

    public int VideoHeight { get; private set; }

    public ControlChannel? Control { get; private set; }

    /// <summary>Cada cuadro decodificado, desde el hilo de video.</summary>
    public event Action<VideoFrame>? FrameReady;

    /// <summary>El video ha cambiado de tamaño (giro del movil).</summary>
    public event Action<int, int>? VideoSizeChanged;

    /// <summary>La sesion ha terminado, por error o porque el movil se fue. Con el motivo, si lo hay.</summary>
    public event Action<string?>? Ended;

    /// <summary>Mensajes del servidor de scrcpy, por si hay que ver que ha pasado.</summary>
    public event Action<string>? ServerOutput;

    /// <summary>Opciones del servidor: tamaño maximo del lado largo, bitrate y fps.</summary>
    public int MaxSize { get; init; } = 1280;

    public int BitRate { get; init; } = 8_000_000;

    public int MaxFps { get; init; } = 60;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var serverPath = Path.Combine(AppContext.BaseDirectory, "Assets", "scrcpy-server");
        if (!File.Exists(serverPath))
            throw new FileNotFoundException("Falta el servidor de scrcpy junto al ejecutable.", serverPath);

        await _adb.PushAsync(_serial, serverPath, RemoteServerPath, cancellationToken).ConfigureAwait(false);
        await _adb.ForwardAsync(_serial, _localPort, $"localabstract:scrcpy_{_scid:x8}", cancellationToken).ConfigureAwait(false);

        var command = string.Join(' ',
            $"CLASSPATH={RemoteServerPath}", "app_process", "/", "com.genymobile.scrcpy.Server", ServerVersion,
            $"scid={_scid:x8}", "log_level=info", "audio=false", "tunnel_forward=true", "control=true",
            $"max_size={MaxSize}", $"video_bit_rate={BitRate}", $"max_fps={MaxFps}",
            "clipboard_autosync=true", "stay_awake=true", "cleanup=true");

        _server = _adb.StartShell(_serial, command);
        _server.OutputDataReceived += (_, e) => { if (e.Data is not null) ServerOutput?.Invoke(e.Data); };
        _server.ErrorDataReceived += (_, e) => { if (e.Data is not null) ServerOutput?.Invoke(e.Data); };

        // El servidor tarda un momento en escuchar: se insiste hasta que acepte y mande el byte
        // de relleno (sin el, adb acepta la conexion aunque nadie escuche detras).
        _videoSocket = await ConnectAsync(expectDummyByte: true, cancellationToken).ConfigureAwait(false);
        _controlSocket = await ConnectAsync(expectDummyByte: false, cancellationToken).ConfigureAwait(false);

        var video = _videoSocket.GetStream();

        var name = new byte[DeviceNameLength];
        await ReadExactlyAsync(video, name, cancellationToken).ConfigureAwait(false);
        DeviceName = Encoding.UTF8.GetString(name).TrimEnd('\0');

        var codec = new byte[4];
        await ReadExactlyAsync(video, codec, cancellationToken).ConfigureAwait(false);
        var codecId = BinaryPrimitives.ReadUInt32BigEndian(codec);
        if (codecId != 0x68323634) // "h264"
            throw new InvalidOperationException($"Codec de video no admitido: 0x{codecId:X8}");

        Control = new ControlChannel(_controlSocket.GetStream());

        _videoTask = Task.Run(() => VideoLoopAsync(video, _cancellation.Token));
        _controlTask = Control.ReadLoopAsync(_cancellation.Token);
    }

    private async Task<TcpClient> ConnectAsync(bool expectDummyByte, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_server is { HasExited: true })
                throw new InvalidOperationException("El servidor de scrcpy ha terminado antes de aceptar la conexion.");

            var client = new TcpClient { NoDelay = true };
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, _localPort, cancellationToken).ConfigureAwait(false);

                if (!expectDummyByte)
                    return client;

                var dummy = new byte[1];
                var stream = client.GetStream();
                stream.ReadTimeout = 1000;
                var read = await stream.ReadAsync(dummy, cancellationToken).ConfigureAwait(false);
                if (read == 1)
                    return client;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                last = ex;
            }

            client.Dispose();
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("El servidor de scrcpy no ha contestado.", last);
    }

    private async Task VideoLoopAsync(NetworkStream video, CancellationToken cancellationToken)
    {
        string? reason = null;
        H264Decoder? decoder = null;
        var header = new byte[HeaderSize];
        byte[]? pendingConfig = null;
        var packet = new byte[1 << 20];
        var pixels = new byte[0];

        try
        {
            decoder = new H264Decoder();

            while (!cancellationToken.IsCancellationRequested)
            {
                await ReadExactlyAsync(video, header, cancellationToken).ConfigureAwait(false);

                if ((header[0] & 0x80) != 0)
                {
                    // Paquete de sesion: solo trae el tamaño nuevo del video.
                    VideoWidth = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
                    VideoHeight = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8));
                    VideoSizeChanged?.Invoke(VideoWidth, VideoHeight);
                    continue;
                }

                var ptsAndFlags = BinaryPrimitives.ReadUInt64BigEndian(header);
                var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8));
                if (length <= 0)
                    throw new IOException("Paquete de video con longitud invalida.");

                if (packet.Length < length)
                    packet = new byte[length];

                await ReadExactlyAsync(video, packet.AsMemory(0, length), cancellationToken).ConfigureAwait(false);

                if ((ptsAndFlags & PacketFlagConfig) != 0)
                {
                    // SPS/PPS: se guardan para pegarlos delante del cuadro que viene.
                    pendingConfig = packet[..length];
                    continue;
                }

                var pts = (long)(ptsAndFlags & PacketPtsMask);
                ReadOnlySpan<byte> accessUnit;
                if (pendingConfig is not null)
                {
                    var merged = new byte[pendingConfig.Length + length];
                    pendingConfig.CopyTo(merged, 0);
                    packet.AsSpan(0, length).CopyTo(merged.AsSpan(pendingConfig.Length));
                    pendingConfig = null;
                    accessUnit = merged;
                }
                else
                {
                    accessUnit = packet.AsSpan(0, length);
                }

                decoder.Decode(accessUnit, pts, (in DecodedFrame frame) =>
                {
                    var needed = frame.Width * frame.Height * 4;
                    if (pixels.Length != needed)
                        pixels = new byte[needed];

                    Nv12Converter.ToBgra(in frame, pixels);
                    FrameReady?.Invoke(new VideoFrame(frame.Width, frame.Height, pixels));
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Cierre pedido.
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            reason = _cancellation.IsCancellationRequested ? null : "El movil ha cerrado la conexion.";
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            ServerOutput?.Invoke(ex.ToString());
        }
        finally
        {
            decoder?.Dispose();
            Ended?.Invoke(reason);
        }
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (n == 0)
                throw new IOException("Fin del flujo de video.");
            read += n;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();

        _videoSocket?.Dispose();
        _controlSocket?.Dispose();

        foreach (var task in new[] { _videoTask, _controlTask })
        {
            if (task is null)
                continue;
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ya se esta cerrando; lo que diga el hilo no cambia nada.
            }
        }

        Control?.Dispose();

        try
        {
            if (_server is { HasExited: false })
                _server.Kill(entireProcessTree: true);
            _server?.Dispose();
        }
        catch (Exception)
        {
            // El proceso de adb puede haberse ido ya.
        }

        try
        {
            await _adb.RemoveForwardAsync(_serial, _localPort).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sin el movil conectado, la redireccion ya no existe.
        }

        _cancellation.Dispose();
    }
}
