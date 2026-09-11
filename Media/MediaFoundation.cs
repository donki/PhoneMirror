using System.Runtime.InteropServices;

namespace PhoneMirror.Media;

/// <summary>
/// Lo justo de Media Foundation para decodificar H.264 con el descodificador que trae Windows.
/// </summary>
/// <remarks>
/// <para><b>Por que interop a mano y no una biblioteca.</b> Las envolturas administradas de Media
/// Foundation que existen son LGPL o estan abandonadas, y la constitucion (seccion 4) solo admite
/// dependencias permisivas o APIs del sistema. Media Foundation es del sistema; estas
/// declaraciones son las de <c>mfobjects.h</c> / <c>mftransform.h</c>, en el orden exacto de sus
/// tablas virtuales, que es lo unico que importa para que el interop funcione.</para>
///
/// <para>Solo se declaran los metodos que se usan por nombre; los demas llevan su sitio en la
/// interfaz (con un nombre y firma cualquiera valida) para que la numeracion de la tabla virtual
/// no se desplace.</para>
/// </remarks>
internal static class MediaFoundation
{
    public const uint MF_VERSION = 0x00020070;
    public const uint MFSTARTUP_FULL = 0;

    public const uint MFT_MESSAGE_COMMAND_FLUSH = 0x00000000;
    public const uint MFT_MESSAGE_COMMAND_DRAIN = 0x00000001;
    public const uint MFT_MESSAGE_NOTIFY_BEGIN_STREAMING = 0x10000000;
    public const uint MFT_MESSAGE_NOTIFY_END_STREAMING = 0x10000001;
    public const uint MFT_MESSAGE_NOTIFY_END_OF_STREAM = 0x10000002;
    public const uint MFT_MESSAGE_NOTIFY_START_OF_STREAM = 0x10000003;

    public const int MF_E_TRANSFORM_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
    public const int MF_E_TRANSFORM_STREAM_CHANGE = unchecked((int)0xC00D6D61);
    public const int MF_E_NOTACCEPTING = unchecked((int)0xC00D36B5);

    public const uint MFT_OUTPUT_STREAM_PROVIDES_SAMPLES = 0x00000100;
    public const uint MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES = 0x00000200;

    public static readonly Guid CLSID_CMSH264DecoderMFT = new("62CE7E72-4C71-4D20-B15D-452831A87D9D");

    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MF_MT_MINIMUM_DISPLAY_APERTURE = new("d7388766-18fe-48c6-a177-ee894867c8c4");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    /// <summary>CODECAPI_AVLowLatencyMode (mismo GUID que MF_LOW_LATENCY).</summary>
    public static readonly Guid CODECAPI_AVLowLatencyMode = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");

    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_NV12 = new("3231564E-0000-0010-8000-00AA00389B71");

    public static readonly Guid IID_IMFTransform = new("bf94c121-5b05-4e6f-8000-ba598961414d");
    public static readonly Guid IID_IMF2DBuffer = new("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType type);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateAlignedMemoryBuffer(uint maxLength, uint alignment, out IMFMediaBuffer buffer);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern int CoCreateInstance(
        in Guid clsid, IntPtr outer, uint context, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object instance);

    public const uint CLSCTX_INPROC_SERVER = 1;

    public static void Check(int hr, string what)
    {
        if (hr < 0)
            throw new InvalidOperationException($"{what}: 0x{hr:X8}");
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MFT_INPUT_STREAM_INFO
{
    public long hnsMaxLatency;
    public uint dwFlags;
    public uint cbSize;
    public uint cbMaxLookahead;
    public uint cbAlignment;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MFT_OUTPUT_STREAM_INFO
{
    public uint dwFlags;
    public uint cbSize;
    public uint cbAlignment;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MFT_OUTPUT_DATA_BUFFER
{
    public uint dwStreamID;
    public IntPtr pSample;
    public uint dwStatus;
    public IntPtr pEvents;
}

/// <summary>MFVideoArea: recorte visible dentro del cuadro decodificado (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MFVideoArea
{
    public ushort OffsetXFract;
    public short OffsetXValue;
    public ushort OffsetYFract;
    public short OffsetYValue;
    public int Width;
    public int Height;
}

// ---------------------------------------------------------------------------------------------
//  Interfaces COM. El orden de los metodos es el de la tabla virtual y NO se puede cambiar.
// ---------------------------------------------------------------------------------------------

[ComImport, Guid("2cd0bd52-bcd5-4b89-b62c-eadc0c031e7b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    [PreserveSig] int GetItem(in Guid key, IntPtr value);
    [PreserveSig] int GetItemType(in Guid key, out int type);
    [PreserveSig] int CompareItem(in Guid key, IntPtr value, out bool result);
    [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);
    [PreserveSig] int GetUINT32(in Guid key, out uint value);
    [PreserveSig] int GetUINT64(in Guid key, out ulong value);
    [PreserveSig] int GetDouble(in Guid key, out double value);
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    [PreserveSig] int GetStringLength(in Guid key, out uint length);
    [PreserveSig] int GetString(in Guid key, IntPtr value, uint size, out uint length);
    [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize(in Guid key, out uint size);
    [PreserveSig] int GetBlob(in Guid key, IntPtr buffer, uint size, out uint written);
    [PreserveSig] int GetAllocatedBlob(in Guid key, out IntPtr buffer, out uint size);
    [PreserveSig] int GetUnknown(in Guid key, in Guid iid, out IntPtr value);
    [PreserveSig] int SetItem(in Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(in Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, uint value);
    [PreserveSig] int SetUINT64(in Guid key, ulong value);
    [PreserveSig] int SetDouble(in Guid key, double value);
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(in Guid key, IntPtr buffer, uint size);
    [PreserveSig] int SetUnknown(in Guid key, IntPtr value);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IMFAttributes destination);
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType
{
    // IMFAttributes
    [PreserveSig] int GetItem(in Guid key, IntPtr value);
    [PreserveSig] int GetItemType(in Guid key, out int type);
    [PreserveSig] int CompareItem(in Guid key, IntPtr value, out bool result);
    [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);
    [PreserveSig] int GetUINT32(in Guid key, out uint value);
    [PreserveSig] int GetUINT64(in Guid key, out ulong value);
    [PreserveSig] int GetDouble(in Guid key, out double value);
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    [PreserveSig] int GetStringLength(in Guid key, out uint length);
    [PreserveSig] int GetString(in Guid key, IntPtr value, uint size, out uint length);
    [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize(in Guid key, out uint size);
    [PreserveSig] int GetBlob(in Guid key, IntPtr buffer, uint size, out uint written);
    [PreserveSig] int GetAllocatedBlob(in Guid key, out IntPtr buffer, out uint size);
    [PreserveSig] int GetUnknown(in Guid key, in Guid iid, out IntPtr value);
    [PreserveSig] int SetItem(in Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(in Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, uint value);
    [PreserveSig] int SetUINT64(in Guid key, ulong value);
    [PreserveSig] int SetDouble(in Guid key, double value);
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(in Guid key, IntPtr buffer, uint size);
    [PreserveSig] int SetUnknown(in Guid key, IntPtr value);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IMFAttributes destination);
    // IMFMediaType
    [PreserveSig] int GetMajorType(out Guid majorType);
    [PreserveSig] int IsCompressedFormat(out bool compressed);
    [PreserveSig] int IsEqual(IMFMediaType other, out uint flags);
    [PreserveSig] int GetRepresentation(Guid representation, out IntPtr value);
    [PreserveSig] int FreeRepresentation(Guid representation, IntPtr value);
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    // IMFAttributes
    [PreserveSig] int GetItem(in Guid key, IntPtr value);
    [PreserveSig] int GetItemType(in Guid key, out int type);
    [PreserveSig] int CompareItem(in Guid key, IntPtr value, out bool result);
    [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);
    [PreserveSig] int GetUINT32(in Guid key, out uint value);
    [PreserveSig] int GetUINT64(in Guid key, out ulong value);
    [PreserveSig] int GetDouble(in Guid key, out double value);
    [PreserveSig] int GetGUID(in Guid key, out Guid value);
    [PreserveSig] int GetStringLength(in Guid key, out uint length);
    [PreserveSig] int GetString(in Guid key, IntPtr value, uint size, out uint length);
    [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out uint length);
    [PreserveSig] int GetBlobSize(in Guid key, out uint size);
    [PreserveSig] int GetBlob(in Guid key, IntPtr buffer, uint size, out uint written);
    [PreserveSig] int GetAllocatedBlob(in Guid key, out IntPtr buffer, out uint size);
    [PreserveSig] int GetUnknown(in Guid key, in Guid iid, out IntPtr value);
    [PreserveSig] int SetItem(in Guid key, IntPtr value);
    [PreserveSig] int DeleteItem(in Guid key);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32(in Guid key, uint value);
    [PreserveSig] int SetUINT64(in Guid key, ulong value);
    [PreserveSig] int SetDouble(in Guid key, double value);
    [PreserveSig] int SetGUID(in Guid key, in Guid value);
    [PreserveSig] int SetString(in Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int SetBlob(in Guid key, IntPtr buffer, uint size);
    [PreserveSig] int SetUnknown(in Guid key, IntPtr value);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
    [PreserveSig] int CopyAllItems(IMFAttributes destination);
    // IMFSample
    [PreserveSig] int GetSampleFlags(out uint flags);
    [PreserveSig] int SetSampleFlags(uint flags);
    [PreserveSig] int GetSampleTime(out long time);
    [PreserveSig] int SetSampleTime(long time);
    [PreserveSig] int GetSampleDuration(out long duration);
    [PreserveSig] int SetSampleDuration(long duration);
    [PreserveSig] int GetBufferCount(out uint count);
    [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer buffer);
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
    [PreserveSig] int RemoveBufferByIndex(uint index);
    [PreserveSig] int RemoveAllBuffers();
    [PreserveSig] int GetTotalLength(out uint length);
    [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
}

[ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
    [PreserveSig] int Unlock();
    [PreserveSig] int GetCurrentLength(out uint length);
    [PreserveSig] int SetCurrentLength(uint length);
    [PreserveSig] int GetMaxLength(out uint length);
}

[ComImport, Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMF2DBuffer
{
    [PreserveSig] int Lock2D(out IntPtr scanline0, out int pitch);
    [PreserveSig] int Unlock2D();
    [PreserveSig] int GetScanline0AndPitch(out IntPtr scanline0, out int pitch);
    [PreserveSig] int IsContiguousFormat(out bool contiguous);
    [PreserveSig] int GetContiguousLength(out uint length);
    [PreserveSig] int ContiguousCopyTo(IntPtr destination, uint size);
    [PreserveSig] int ContiguousCopyFrom(IntPtr source, uint size);
}

/// <summary>Solo lo que hace falta para pedir baja latencia; los demas metodos ocupan su sitio.</summary>
[ComImport, Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICodecAPI
{
    [PreserveSig] int IsSupported(in Guid api);
    [PreserveSig] int IsModifiable(in Guid api);
    [PreserveSig] int GetParameterRange(in Guid api, IntPtr min, IntPtr max, IntPtr delta);
    [PreserveSig] int GetParameterValues(in Guid api, out IntPtr values, out uint count);
    [PreserveSig] int GetDefaultValue(in Guid api, IntPtr value);
    [PreserveSig] int GetValue(in Guid api, IntPtr value);
    [PreserveSig] int SetValue(in Guid api, IntPtr value);
}

[ComImport, Guid("bf94c121-5b05-4e6f-8000-ba598961414d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFTransform
{
    [PreserveSig] int GetStreamLimits(out uint inputMin, out uint inputMax, out uint outputMin, out uint outputMax);
    [PreserveSig] int GetStreamCount(out uint inputs, out uint outputs);
    [PreserveSig] int GetStreamIDs(uint inputSize, IntPtr inputIds, uint outputSize, IntPtr outputIds);
    [PreserveSig] int GetInputStreamInfo(uint id, out MFT_INPUT_STREAM_INFO info);
    [PreserveSig] int GetOutputStreamInfo(uint id, out MFT_OUTPUT_STREAM_INFO info);
    [PreserveSig] int GetAttributes(out IMFAttributes attributes);
    [PreserveSig] int GetInputStreamAttributes(uint id, out IMFAttributes attributes);
    [PreserveSig] int GetOutputStreamAttributes(uint id, out IMFAttributes attributes);
    [PreserveSig] int DeleteInputStream(uint id);
    [PreserveSig] int AddInputStreams(uint count, IntPtr ids);
    [PreserveSig] int GetInputAvailableType(uint id, uint index, out IMFMediaType type);
    [PreserveSig] int GetOutputAvailableType(uint id, uint index, out IMFMediaType type);
    [PreserveSig] int SetInputType(uint id, IMFMediaType? type, uint flags);
    [PreserveSig] int SetOutputType(uint id, IMFMediaType? type, uint flags);
    [PreserveSig] int GetInputCurrentType(uint id, out IMFMediaType type);
    [PreserveSig] int GetOutputCurrentType(uint id, out IMFMediaType type);
    [PreserveSig] int GetInputStatus(uint id, out uint flags);
    [PreserveSig] int GetOutputStatus(out uint flags);
    [PreserveSig] int SetOutputBounds(long lower, long upper);
    [PreserveSig] int ProcessEvent(uint id, IntPtr mediaEvent);
    [PreserveSig] int ProcessMessage(uint message, UIntPtr param);
    [PreserveSig] int ProcessInput(uint id, IMFSample sample, uint flags);
    // Un solo flujo de salida: se pasa la estructura por referencia. Un array aqui lo convertiria
    // el marshaler en SAFEARRAY (y pediria una biblioteca de tipos), que no es lo que espera el MFT.
    [PreserveSig] int ProcessOutput(uint flags, uint count, ref MFT_OUTPUT_DATA_BUFFER buffer, out uint status);
}
