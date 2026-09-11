using System.Runtime.InteropServices;
using static PhoneMirror.Media.MediaFoundation;

namespace PhoneMirror.Media;

/// <summary>Un cuadro decodificado, en NV12, valido solo mientras dura la llamada que lo entrega.</summary>
internal readonly ref struct DecodedFrame
{
    public DecodedFrame(int width, int height, IntPtr yPlane, IntPtr uvPlane, int pitch)
    {
        Width = width;
        Height = height;
        YPlane = yPlane;
        UvPlane = uvPlane;
        Pitch = pitch;
    }

    /// <summary>Tamaño visible (ya sin el relleno de alineacion del decodificador).</summary>
    public int Width { get; }

    public int Height { get; }

    public IntPtr YPlane { get; }

    public IntPtr UvPlane { get; }

    /// <summary>Bytes por fila en las dos planas.</summary>
    public int Pitch { get; }
}

internal delegate void FrameHandler(in DecodedFrame frame);

/// <summary>
/// Descodificador H.264 sobre el MFT de Microsoft que trae Windows (CLSID_CMSH264DecoderMFT).
/// </summary>
/// <remarks>
/// <para>Se le dan unidades de acceso Annex B tal como llegan del servidor de scrcpy (con los SPS/PPS
/// delante del primer cuadro y de cada cambio de tamaño) y devuelve cuadros NV12. En modo de baja
/// latencia (<c>MF_LOW_LATENCY</c>) saca cada cuadro en cuanto lo tiene, sin esperar al siguiente:
/// sin eso, un espejo del movil va un cuadro por detras de la mano.</para>
///
/// <para>Cuando el movil gira, el flujo cambia de tamaño y el MFT lo avisa con
/// <c>MF_E_TRANSFORM_STREAM_CHANGE</c>: se vuelve a negociar la salida y se sigue.</para>
///
/// <para>No es seguro entre hilos: lo usa solo el hilo que lee el video.</para>
/// </remarks>
internal sealed class H264Decoder : IDisposable
{
    /// <summary>Traza de lo que devuelve el MFT, para cuando algo no decodifica.</summary>
    public static Action<string>? Trace { get; set; }

    private readonly IMFTransform _transform;
    private IMFSample? _outputSample;
    private IMFMediaBuffer? _outputBuffer;
    private IntPtr _outputSamplePtr;
    private bool _transformProvidesSamples;
    private uint _outputBufferSize;

    private int _codedWidth;
    private int _codedHeight;
    private int _visibleWidth;
    private int _visibleHeight;
    private int _visibleOffsetX;
    private int _visibleOffsetY;
    private bool _disposed;

    public H264Decoder()
    {
        Check(MFStartup(MF_VERSION, MFSTARTUP_FULL), "MFStartup");

        Check(CoCreateInstance(CLSID_CMSH264DecoderMFT, IntPtr.Zero, CLSCTX_INPROC_SERVER, IID_IMFTransform, out var instance),
            "CoCreateInstance(CMSH264DecoderMFT)");
        _transform = (IMFTransform)instance;

        // Baja latencia: que saque cada cuadro en cuanto lo tenga. Sin esto, el descodificador de
        // Microsoft retiene hasta dieciseis cuadros para reordenar cuando el flujo no dice cuantos
        // necesita (y el del movil no lo dice): un espejo con medio segundo de retraso. Se pide
        // por ICodecAPI con un VT_UI4, que es lo que acepta (con VT_BOOL devuelve E_INVALIDARG).
        var variant = Marshal.AllocHGlobal(24);
        try
        {
            for (var i = 0; i < 24; i++)
                Marshal.WriteByte(variant, i, 0);
            Marshal.WriteInt16(variant, 0, 19); // VT_UI4
            Marshal.WriteInt32(variant, 8, 1);
            var hr = ((ICodecAPI)instance).SetValue(CODECAPI_AVLowLatencyMode, variant);
            Trace?.Invoke($"AVLowLatencyMode → 0x{hr:X8}");
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
        }

        Check(MFCreateMediaType(out var input), "MFCreateMediaType");
        Check(input.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video), "SetGUID(major)");
        Check(input.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264), "SetGUID(subtype)");
        Check(_transform.SetInputType(0, input, 0), "SetInputType");

        NegotiateOutput();

        Check(_transform.ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, UIntPtr.Zero), "BEGIN_STREAMING");
        Check(_transform.ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, UIntPtr.Zero), "START_OF_STREAM");
    }

    public int Width => _visibleWidth;

    public int Height => _visibleHeight;

    /// <summary>
    /// Entrega una unidad de acceso al decodificador y llama a <paramref name="onFrame"/> por cada
    /// cuadro que salga (normalmente uno, a veces ninguno al principio, varios tras un cambio).
    /// </summary>
    public void Decode(ReadOnlySpan<byte> accessUnit, long presentationMicroseconds, FrameHandler onFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sample = CreateInputSample(accessUnit, presentationMicroseconds);
        try
        {
            var hr = _transform.ProcessInput(0, sample, 0);
            Trace?.Invoke($"ProcessInput({accessUnit.Length} bytes) → 0x{hr:X8}");
            if (hr == MF_E_NOTACCEPTING)
            {
                // Tiene salida pendiente: se vacia y se vuelve a intentar.
                DrainOutput(onFrame);
                hr = _transform.ProcessInput(0, sample, 0);
            }

            Check(hr, "ProcessInput");
            DrainOutput(onFrame);
        }
        finally
        {
            Marshal.ReleaseComObject(sample);
        }
    }

    private void DrainOutput(FrameHandler onFrame)
    {
        while (true)
        {
            // La muestra se reutiliza: hay que dejar el bufer a cero antes de cada salida, o el
            // descodificador lo toma por lleno y devuelve E_FAIL.
            _outputBuffer?.SetCurrentLength(0);

            var buffer = new MFT_OUTPUT_DATA_BUFFER
            {
                dwStreamID = 0,
                pSample = _transformProvidesSamples ? IntPtr.Zero : _outputSamplePtr,
            };

            var hr = _transform.ProcessOutput(0, 1, ref buffer, out _);
            Trace?.Invoke($"ProcessOutput → 0x{hr:X8} estado 0x{buffer.dwStatus:X8}");

            if (buffer.pEvents != IntPtr.Zero)
                Marshal.Release(buffer.pEvents);

            if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT)
                return;

            if (hr == MF_E_TRANSFORM_STREAM_CHANGE)
            {
                // El movil ha girado o ha cambiado de resolucion: nueva salida, mismo flujo.
                NegotiateOutput();
                continue;
            }

            Check(hr, $"ProcessOutput (estado 0x{buffer.dwStatus:X8}, salida {_codedWidth}×{_codedHeight}, " +
                      $"muestras del MFT: {_transformProvidesSamples}, tamaño de bufer {_outputBufferSize})");

            var samplePtr = buffer.pSample;
            if (samplePtr == IntPtr.Zero)
                continue;

            var sample = _transformProvidesSamples
                ? (IMFSample)Marshal.GetObjectForIUnknown(samplePtr)
                : _outputSample!;

            try
            {
                Deliver(sample, onFrame);
            }
            finally
            {
                if (_transformProvidesSamples)
                {
                    Marshal.ReleaseComObject(sample);
                    Marshal.Release(samplePtr);
                }
            }
        }
    }

    private void Deliver(IMFSample sample, FrameHandler onFrame)
    {
        Check(sample.ConvertToContiguousBuffer(out var buffer), "ConvertToContiguousBuffer");
        try
        {
            // Con IMF2DBuffer se sabe el paso real de las filas; sin el, se supone igual al ancho.
            if (buffer is IMF2DBuffer twoD)
            {
                Check(twoD.Lock2D(out var scanline0, out var pitch), "Lock2D");
                try
                {
                    Emit(scanline0, pitch, onFrame);
                }
                finally
                {
                    twoD.Unlock2D();
                }
            }
            else
            {
                Check(buffer.Lock(out var data, out _, out _), "Lock");
                try
                {
                    Emit(data, _codedWidth, onFrame);
                }
                finally
                {
                    buffer.Unlock();
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }
    }

    private void Emit(IntPtr scanline0, int pitch, FrameHandler onFrame)
    {
        // NV12: primero las filas de luma (alto codificado), despues las de croma entrelazado.
        var uv = scanline0 + (nint)pitch * _codedHeight;

        // El recorte visible se aplica desplazando el origen; el paso no cambia.
        var y = scanline0 + (nint)pitch * _visibleOffsetY + _visibleOffsetX;
        var uvVisible = uv + (nint)pitch * (_visibleOffsetY / 2) + (_visibleOffsetX & ~1);

        var frame = new DecodedFrame(_visibleWidth, _visibleHeight, y, uvVisible, pitch);
        onFrame(in frame);
    }

    /// <summary>
    /// Elige NV12 entre las salidas que ofrece el MFT y lee el tamaño y el recorte visible.
    /// </summary>
    private void NegotiateOutput()
    {
        IMFMediaType? chosen = null;
        for (uint i = 0; ; i++)
        {
            var hr = _transform.GetOutputAvailableType(0, i, out var candidate);
            if (hr < 0)
                break;

            if (candidate.GetGUID(MF_MT_SUBTYPE, out var subtype) >= 0 && subtype == MFVideoFormat_NV12)
            {
                chosen = candidate;
                break;
            }

            Marshal.ReleaseComObject(candidate);
        }

        if (chosen is null)
            throw new InvalidOperationException("El descodificador H.264 no ofrece salida NV12.");

        try
        {
            Check(_transform.SetOutputType(0, chosen, 0), "SetOutputType");
            Trace?.Invoke("SetOutputType NV12 ok");

            Check(chosen.GetUINT64(MF_MT_FRAME_SIZE, out var size), "GetUINT64(MF_MT_FRAME_SIZE)");
            _codedWidth = (int)(size >> 32);
            _codedHeight = (int)(size & 0xFFFFFFFF);

            _visibleWidth = _codedWidth;
            _visibleHeight = _codedHeight;
            _visibleOffsetX = 0;
            _visibleOffsetY = 0;

            // El decodificador alinea a 16 y declara la zona util aparte: 1080 se codifica como 1088.
            if (chosen.GetBlobSize(MF_MT_MINIMUM_DISPLAY_APERTURE, out var blobSize) >= 0 &&
                blobSize == Marshal.SizeOf<MFVideoArea>())
            {
                var raw = Marshal.AllocHGlobal((int)blobSize);
                try
                {
                    if (chosen.GetBlob(MF_MT_MINIMUM_DISPLAY_APERTURE, raw, blobSize, out _) >= 0)
                    {
                        var area = Marshal.PtrToStructure<MFVideoArea>(raw);
                        if (area.Width > 0 && area.Height > 0)
                        {
                            _visibleOffsetX = area.OffsetXValue;
                            _visibleOffsetY = area.OffsetYValue;
                            _visibleWidth = area.Width;
                            _visibleHeight = area.Height;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(raw);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(chosen);
        }

        PrepareOutputSample();
    }

    /// <summary>El MFT de software no reserva las muestras de salida: hay que darselas.</summary>
    private void PrepareOutputSample()
    {
        Check(_transform.GetOutputStreamInfo(0, out var info), "GetOutputStreamInfo");
        _transformProvidesSamples = (info.dwFlags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES | MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES)) != 0;

        ReleaseOutputSample();
        if (_transformProvidesSamples)
            return;

        Check(MFCreateSample(out var sample), "MFCreateSample");
        var alignment = info.cbAlignment > 0 ? info.cbAlignment - 1 : 15;
        _outputBufferSize = Math.Max(info.cbSize, (uint)(_codedWidth * _codedHeight * 3 / 2));
        Check(MFCreateAlignedMemoryBuffer(_outputBufferSize, alignment, out var buffer), "MFCreateAlignedMemoryBuffer");
        Check(sample.AddBuffer(buffer), "AddBuffer");

        _outputSample = sample;
        _outputBuffer = buffer;
        _outputSamplePtr = Marshal.GetComInterfaceForObject(sample, typeof(IMFSample));
    }

    private void ReleaseOutputSample()
    {
        if (_outputBuffer is not null)
        {
            Marshal.ReleaseComObject(_outputBuffer);
            _outputBuffer = null;
        }

        if (_outputSamplePtr != IntPtr.Zero)
        {
            Marshal.Release(_outputSamplePtr);
            _outputSamplePtr = IntPtr.Zero;
        }

        if (_outputSample is not null)
        {
            Marshal.ReleaseComObject(_outputSample);
            _outputSample = null;
        }
    }

    private static IMFSample CreateInputSample(ReadOnlySpan<byte> data, long presentationMicroseconds)
    {
        Check(MFCreateSample(out var sample), "MFCreateSample");
        Check(MFCreateMemoryBuffer((uint)data.Length, out var buffer), "MFCreateMemoryBuffer");
        try
        {
            Check(buffer.Lock(out var destination, out _, out _), "Lock");
            try
            {
                unsafe
                {
                    data.CopyTo(new Span<byte>((void*)destination, data.Length));
                }
            }
            finally
            {
                buffer.Unlock();
            }

            Check(buffer.SetCurrentLength((uint)data.Length), "SetCurrentLength");
            Check(sample.AddBuffer(buffer), "AddBuffer");

            // Media Foundation cuenta en unidades de 100 ns; scrcpy manda microsegundos.
            sample.SetSampleTime(presentationMicroseconds * 10);
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }

        return sample;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _transform.ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, UIntPtr.Zero);
            _transform.ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING, UIntPtr.Zero);
        }
        catch (Exception)
        {
            // Cerrando: da igual lo que diga.
        }

        ReleaseOutputSample();
        Marshal.ReleaseComObject(_transform);
        MFShutdown();
    }
}
