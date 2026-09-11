using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace PhoneMirror.Services;

/// <summary>
/// El canal de control de scrcpy: lo que se le manda al movil (toques, teclas, texto, pegar…) y
/// lo que el movil manda de vuelta (su portapapeles).
/// </summary>
/// <remarks>
/// El formato es el de <c>ControlMessageReader.java</c> / <c>DeviceMessageWriter.java</c> del
/// servidor, version 4.1: un byte de tipo y los campos en big-endian. Los mensajes se escriben en
/// serie por un unico hilo de envio, para que un toque no se meta dentro de un texto.
/// </remarks>
public sealed class ControlChannel : IDisposable
{
    // ControlMessage.TYPE_*
    private const byte TypeInjectKeycode = 0;
    private const byte TypeInjectText = 1;
    private const byte TypeInjectTouch = 2;
    private const byte TypeInjectScroll = 3;
    private const byte TypeBackOrScreenOn = 4;
    private const byte TypeExpandNotificationPanel = 5;
    private const byte TypeExpandSettingsPanel = 6;
    private const byte TypeCollapsePanels = 7;
    private const byte TypeGetClipboard = 8;
    private const byte TypeSetClipboard = 9;
    private const byte TypeSetDisplayPower = 10;
    private const byte TypeRotateDevice = 11;

    // DeviceMessage.TYPE_*
    private const byte DeviceClipboard = 0;
    private const byte DeviceAckClipboard = 1;
    private const byte DeviceUhidOutput = 2;

    /// <summary>Identificador del puntero «raton» en Android (POINTER_ID_MOUSE = -1).</summary>
    public const long MousePointerId = -1;

    public const int ActionDown = 0;
    public const int ActionUp = 1;
    public const int ActionMove = 2;

    public const int ButtonPrimary = 1 << 0;
    public const int ButtonSecondary = 1 << 1;
    public const int ButtonTertiary = 1 << 2;

    private const int InjectTextMaxLength = 300;

    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private long _clipboardSequence;

    public ControlChannel(NetworkStream stream)
    {
        _stream = stream;
    }

    /// <summary>Texto que el movil acaba de copiar (llega solo, con <c>clipboard_autosync</c>).</summary>
    public event Action<string>? ClipboardReceived;

    // ------------------------------------------------------------------ envio

    /// <param name="x">Coordenadas en el espacio del <b>video</b> (lo que se ve), no de la pantalla fisica.</param>
    /// <param name="videoWidth">Tamaño del video en ese momento: el servidor descarta el toque si no coincide.</param>
    public Task TouchAsync(int action, long pointerId, int x, int y, int videoWidth, int videoHeight, float pressure, int actionButton, int buttons)
    {
        var message = new byte[32];
        message[0] = TypeInjectTouch;
        message[1] = (byte)action;
        BinaryPrimitives.WriteInt64BigEndian(message.AsSpan(2), pointerId);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(10), x);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(14), y);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(18), (ushort)videoWidth);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(20), (ushort)videoHeight);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(22), ToU16FixedPoint(pressure));
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(24), actionButton);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(28), buttons);
        return SendAsync(message);
    }

    /// <param name="hScroll">Desplazamiento horizontal en «muescas», entre -1 y 1 por mensaje.</param>
    public Task ScrollAsync(int x, int y, int videoWidth, int videoHeight, float hScroll, float vScroll, int buttons)
    {
        var message = new byte[21];
        message[0] = TypeInjectScroll;
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(1), x);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(5), y);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(9), (ushort)videoWidth);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(11), (ushort)videoHeight);
        BinaryPrimitives.WriteInt16BigEndian(message.AsSpan(13), ToI16FixedPoint(hScroll));
        BinaryPrimitives.WriteInt16BigEndian(message.AsSpan(15), ToI16FixedPoint(vScroll));
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(17), buttons);
        return SendAsync(message);
    }

    /// <param name="action">0 = pulsar, 1 = soltar (AKEY_EVENT_ACTION_DOWN / UP).</param>
    public Task KeyAsync(int action, int keycode, int repeat, int metaState)
    {
        var message = new byte[14];
        message[0] = TypeInjectKeycode;
        message[1] = (byte)action;
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(2), keycode);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(6), repeat);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(10), metaState);
        return SendAsync(message);
    }

    /// <summary>Pulsa y suelta una tecla.</summary>
    public async Task PressKeyAsync(int keycode, int metaState = 0)
    {
        await KeyAsync(0, keycode, 0, metaState).ConfigureAwait(false);
        await KeyAsync(1, keycode, 0, metaState).ConfigureAwait(false);
    }

    /// <summary>Escribe texto tal cual (el servidor lo reparte en eventos de teclado).</summary>
    public Task TextAsync(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > InjectTextMaxLength)
            bytes = bytes[..InjectTextMaxLength];

        var message = new byte[5 + bytes.Length];
        message[0] = TypeInjectText;
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(1), bytes.Length);
        bytes.CopyTo(message, 5);
        return SendAsync(message);
    }

    /// <summary>Atras, o encender la pantalla si estaba apagada (es lo que hace el boton derecho en scrcpy).</summary>
    public async Task BackOrScreenOnAsync()
    {
        await SendAsync([TypeBackOrScreenOn, 0]).ConfigureAwait(false);
        await SendAsync([TypeBackOrScreenOn, 1]).ConfigureAwait(false);
    }

    public Task ExpandNotificationPanelAsync() => SendAsync([TypeExpandNotificationPanel]);

    public Task ExpandSettingsPanelAsync() => SendAsync([TypeExpandSettingsPanel]);

    public Task CollapsePanelsAsync() => SendAsync([TypeCollapsePanels]);

    public Task RotateDeviceAsync() => SendAsync([TypeRotateDevice]);

    /// <summary>Apaga o enciende la pantalla del movil; el espejo sigue funcionando apagada.</summary>
    public Task SetDisplayPowerAsync(bool on) => SendAsync([TypeSetDisplayPower, (byte)(on ? 1 : 0)]);

    /// <summary>
    /// Pide el portapapeles del movil; llega por <see cref="ClipboardReceived"/>.
    /// </summary>
    /// <param name="copyKey">0 = tal cual; 1 = antes pulsa «copiar» en lo que este seleccionado; 2 = «cortar».</param>
    public Task RequestClipboardAsync(int copyKey = 0) => SendAsync([TypeGetClipboard, (byte)copyKey]);

    /// <summary>Pone texto en el portapapeles del movil y, si se pide, lo pega donde este el foco.</summary>
    public Task SetClipboardAsync(string text, bool paste)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var message = new byte[14 + bytes.Length];
        message[0] = TypeSetClipboard;
        BinaryPrimitives.WriteInt64BigEndian(message.AsSpan(1), Interlocked.Increment(ref _clipboardSequence));
        message[9] = (byte)(paste ? 1 : 0);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(10), bytes.Length);
        bytes.CopyTo(message, 14);
        return SendAsync(message);
    }

    private async Task SendAsync(byte[] message)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(message).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // ---------------------------------------------------------------- recepcion

    /// <summary>Bucle de lectura de los mensajes del movil. Termina cuando se cierra el socket.</summary>
    public async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var header = new byte[9];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ReadExactlyAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                switch (header[0])
                {
                    case DeviceClipboard:
                        await ReadExactlyAsync(header.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                        var length = BinaryPrimitives.ReadInt32BigEndian(header);
                        var text = new byte[length];
                        await ReadExactlyAsync(text, cancellationToken).ConfigureAwait(false);
                        ClipboardReceived?.Invoke(Encoding.UTF8.GetString(text));
                        break;

                    case DeviceAckClipboard:
                        await ReadExactlyAsync(header.AsMemory(0, 8), cancellationToken).ConfigureAwait(false);
                        break;

                    case DeviceUhidOutput:
                        await ReadExactlyAsync(header.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                        var size = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                        await ReadExactlyAsync(new byte[size], cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        throw new IOException($"Mensaje del movil desconocido: {header[0]}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // El canal se ha cerrado: es la forma normal de terminar.
        }
    }

    private async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await _stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (n == 0)
                throw new IOException("El movil ha cerrado el canal de control.");
            read += n;
        }
    }

    private static ushort ToU16FixedPoint(float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);
        return clamped >= 1f ? (ushort)0xFFFF : (ushort)(clamped * 65536f);
    }

    private static short ToI16FixedPoint(float value)
    {
        var clamped = Math.Clamp(value, -1f, 1f);
        return clamped >= 1f ? (short)0x7FFF : (short)(clamped * 32768f);
    }

    public void Dispose()
    {
        _writeGate.Dispose();
    }
}
