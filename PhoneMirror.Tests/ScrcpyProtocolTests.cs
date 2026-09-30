using System.Buffers.Binary;
using System.Text;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

public class ScrcpyProtocolTests
{
    [Fact]
    public void ServerCommand_HasVersionScidAndOptions()
    {
        var command = ScrcpyProtocol.ServerCommand(0x2a, 1280, 8_000_000, 60);

        Assert.Equal(
            "CLASSPATH=/data/local/tmp/scrcpy-server.jar app_process / com.genymobile.scrcpy.Server 4.1 " +
            "scid=0000002a log_level=info audio=false tunnel_forward=true control=true " +
            "max_size=1280 video_bit_rate=8000000 max_fps=60 clipboard_autosync=true stay_awake=true cleanup=true",
            command);
    }

    [Fact]
    public void ServerCommand_ScidIsEightLowercaseHexDigits()
    {
        Assert.Contains("scid=7fffffff ", ScrcpyProtocol.ServerCommand(int.MaxValue, 1, 2, 3));
        Assert.Contains("max_size=1 video_bit_rate=2 max_fps=3 ", ScrcpyProtocol.ServerCommand(1, 1, 2, 3));
    }

    [Theory]
    [InlineData(1, "localabstract:scrcpy_00000001")]
    [InlineData(0xABCDEF, "localabstract:scrcpy_00abcdef")]
    public void ForwardSocket(int scid, string expected) =>
        Assert.Equal(expected, ScrcpyProtocol.ForwardSocket(scid));

    [Fact]
    public void ServerVersion_IsTheBundledServerOne() =>
        Assert.Equal("4.1", ScrcpyProtocol.ServerVersion);

    [Fact]
    public void DeviceName_Utf8PaddedWithZeros()
    {
        var name = new byte[ScrcpyProtocol.DeviceNameLength];
        Encoding.UTF8.GetBytes("Redmi Note 12 · Àlex").CopyTo(name, 0);

        Assert.Equal("Redmi Note 12 · Àlex", ScrcpyProtocol.DeviceName(name));
        Assert.Equal("", ScrcpyProtocol.DeviceName(new byte[ScrcpyProtocol.DeviceNameLength]));
    }

    [Fact]
    public void CodecId_H264()
    {
        Assert.Equal(ScrcpyProtocol.CodecH264, ScrcpyProtocol.CodecId("h264"u8));
        Assert.NotEqual(ScrcpyProtocol.CodecH264, ScrcpyProtocol.CodecId("h265"u8));
        Assert.NotEqual(ScrcpyProtocol.CodecH264, ScrcpyProtocol.CodecId("av01"u8));
    }

    [Fact]
    public void ReadHeader_SessionPacket_GivesVideoSize()
    {
        var header = new byte[12];
        header[0] = 0x80;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1080);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), 2400);

        Assert.Equal(new VideoPacketHeader(true, 1080, 2400, false, 0, 0), ScrcpyProtocol.ReadHeader(header));
    }

    [Fact]
    public void ReadHeader_ConfigPacket()
    {
        var header = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(header, ScrcpyProtocol.PacketFlagConfig);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), 27);

        var parsed = ScrcpyProtocol.ReadHeader(header);

        Assert.False(parsed.IsSession);
        Assert.True(parsed.IsConfig);
        Assert.Equal(0, parsed.Pts);
        Assert.Equal(27, parsed.Length);
    }

    [Fact]
    public void ReadHeader_KeyFrame_PtsWithoutFlags()
    {
        const long pts = 123_456_789;
        var header = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(header, ScrcpyProtocol.PacketFlagKeyFrame | pts);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), 50_000);

        Assert.Equal(new VideoPacketHeader(false, 0, 0, false, pts, 50_000), ScrcpyProtocol.ReadHeader(header));
    }

    [Fact]
    public void ReadHeader_LargestPts()
    {
        var header = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(header, ScrcpyProtocol.PacketPtsMask);

        Assert.Equal((long)ScrcpyProtocol.PacketPtsMask, ScrcpyProtocol.ReadHeader(header).Pts);
    }

    [Fact]
    public void ReadHeader_NegativeLengthIsPassedThrough_TheSessionRejectsIt()
    {
        var header = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), -1);

        Assert.Equal(-1, ScrcpyProtocol.ReadHeader(header).Length);
    }
}
