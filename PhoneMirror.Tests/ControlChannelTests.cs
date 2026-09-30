using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

/// <summary>
/// El canal de control contra un socket de loopback (127.0.0.1): lo que se escribe tiene que ser,
/// byte a byte, lo que espera el servidor de scrcpy 4.1, y lo que llega del «movil» se lee bien.
/// </summary>
public sealed class ControlChannelTests : IAsyncLifetime
{
    private TcpListener _listener = null!;
    private TcpClient _client = null!;
    private TcpClient _phone = null!;
    private NetworkStream _phoneStream = null!;
    private ControlChannel _channel = null!;

    public async Task InitializeAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _client = new TcpClient();
        var accept = _listener.AcceptTcpClientAsync();
        await _client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port);
        _phone = await accept;
        _phoneStream = _phone.GetStream();
        _channel = new ControlChannel(_client.GetStream());
    }

    public Task DisposeAsync()
    {
        _channel.Dispose();
        _client.Dispose();
        _phone.Dispose();
        _listener.Stop();
        return Task.CompletedTask;
    }

    private async Task<byte[]> ReadAsync(int count)
    {
        var buffer = new byte[count];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _phoneStream.ReadExactlyAsync(buffer, timeout.Token);
        return buffer;
    }

    // ------------------------------------------------------------------ envio

    [Fact]
    public async Task Touch_32BytesBigEndian()
    {
        await _channel.TouchAsync(ControlChannel.ActionDown, ControlChannel.MousePointerId, 540, 1200, 1080, 2400, 1f,
            ControlChannel.ButtonPrimary, ControlChannel.ButtonPrimary);

        var m = await ReadAsync(32);
        Assert.Equal(2, m[0]);
        Assert.Equal(0, m[1]);
        Assert.Equal(-1L, BinaryPrimitives.ReadInt64BigEndian(m.AsSpan(2)));
        Assert.Equal(540, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(10)));
        Assert.Equal(1200, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(14)));
        Assert.Equal(1080, BinaryPrimitives.ReadUInt16BigEndian(m.AsSpan(18)));
        Assert.Equal(2400, BinaryPrimitives.ReadUInt16BigEndian(m.AsSpan(20)));
        Assert.Equal(0xFFFF, BinaryPrimitives.ReadUInt16BigEndian(m.AsSpan(22)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(24)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(28)));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 0x8000)]
    [InlineData(0.25f, 0x4000)]
    [InlineData(1f, 0xFFFF)]
    [InlineData(2f, 0xFFFF)]
    [InlineData(-1f, 0)]
    public async Task Touch_PressureIsUnsignedFixedPoint(float pressure, int expected)
    {
        await _channel.TouchAsync(ControlChannel.ActionUp, 7, 0, 0, 10, 10, pressure, 0, 0);

        var m = await ReadAsync(32);
        Assert.Equal(1, m[1]);
        Assert.Equal(7L, BinaryPrimitives.ReadInt64BigEndian(m.AsSpan(2)));
        Assert.Equal(expected, BinaryPrimitives.ReadUInt16BigEndian(m.AsSpan(22)));
    }

    [Theory]
    [InlineData(0f, 1f, 0, 0x7FFF)]
    [InlineData(0f, -1f, 0, -32768)]
    [InlineData(0.5f, 0f, 16384, 0)]
    [InlineData(3f, -3f, 0x7FFF, -32768)]
    public async Task Scroll_21BytesWithSignedFixedPoint(float h, float v, int expectedH, int expectedV)
    {
        await _channel.ScrollAsync(100, 200, 1080, 2400, h, v, ControlChannel.ButtonSecondary);

        var m = await ReadAsync(21);
        Assert.Equal(3, m[0]);
        Assert.Equal(100, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(1)));
        Assert.Equal(200, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(5)));
        Assert.Equal(1080, BinaryPrimitives.ReadUInt16BigEndian(m.AsSpan(9)));
        Assert.Equal(2400, BinaryPrimitives.ReadUInt16BigEndian(m.AsSpan(11)));
        Assert.Equal(expectedH, BinaryPrimitives.ReadInt16BigEndian(m.AsSpan(13)));
        Assert.Equal(expectedV, BinaryPrimitives.ReadInt16BigEndian(m.AsSpan(15)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(17)));
    }

    [Fact]
    public async Task Key_14Bytes()
    {
        await _channel.KeyAsync(1, AndroidKeys.Enter, 3, AndroidKeys.MetaCtrlOn);

        var m = await ReadAsync(14);
        Assert.Equal(0, m[0]);
        Assert.Equal(1, m[1]);
        Assert.Equal(66, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(2)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(6)));
        Assert.Equal(0x1000, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(10)));
    }

    [Fact]
    public async Task PressKey_DownThenUp()
    {
        await _channel.PressKeyAsync(AndroidKeys.Home, AndroidKeys.MetaShiftOn);

        var down = await ReadAsync(14);
        var up = await ReadAsync(14);
        Assert.Equal(0, down[1]);
        Assert.Equal(1, up[1]);
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(down.AsSpan(2)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(up.AsSpan(2)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(up.AsSpan(10)));
    }

    [Fact]
    public async Task Text_Utf8WithLength()
    {
        await _channel.TextAsync("Hola, Àlex ñ");

        var bytes = Encoding.UTF8.GetBytes("Hola, Àlex ñ");
        var m = await ReadAsync(5 + bytes.Length);
        Assert.Equal(1, m[0]);
        Assert.Equal(bytes.Length, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(1)));
        Assert.Equal(bytes, m[5..]);
    }

    [Fact]
    public async Task Text_TruncatedTo300Bytes()
    {
        await _channel.TextAsync(new string('a', 400));
        await _channel.RotateDeviceAsync();

        var m = await ReadAsync(5 + 300);
        Assert.Equal(300, BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(1)));
        // Lo siguiente es el mensaje de girar, no el resto del texto.
        Assert.Equal([11], await ReadAsync(1));
    }

    [Fact]
    public async Task Text_Empty()
    {
        await _channel.TextAsync(string.Empty);
        Assert.Equal([1, 0, 0, 0, 0], await ReadAsync(5));
    }

    [Fact]
    public async Task SingleByteCommands()
    {
        await _channel.BackOrScreenOnAsync();
        await _channel.ExpandNotificationPanelAsync();
        await _channel.ExpandSettingsPanelAsync();
        await _channel.CollapsePanelsAsync();
        await _channel.RotateDeviceAsync();
        await _channel.SetDisplayPowerAsync(false);
        await _channel.SetDisplayPowerAsync(true);
        await _channel.RequestClipboardAsync();
        await _channel.RequestClipboardAsync(2);

        Assert.Equal([4, 0, 4, 1, 5, 6, 7, 11, 10, 0, 10, 1, 8, 0, 8, 2], await ReadAsync(16));
    }

    [Fact]
    public async Task SetClipboard_SequenceGrowsAndPasteFlag()
    {
        await _channel.SetClipboardAsync("uno", paste: true);
        await _channel.SetClipboardAsync("", paste: false);

        var first = await ReadAsync(14 + 3);
        Assert.Equal(9, first[0]);
        Assert.Equal(1L, BinaryPrimitives.ReadInt64BigEndian(first.AsSpan(1)));
        Assert.Equal(1, first[9]);
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(first.AsSpan(10)));
        Assert.Equal("uno"u8.ToArray(), first[14..]);

        var second = await ReadAsync(14);
        Assert.Equal(2L, BinaryPrimitives.ReadInt64BigEndian(second.AsSpan(1)));
        Assert.Equal(0, second[9]);
        Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(second.AsSpan(10)));
    }

    [Fact]
    public async Task ConcurrentSends_DoNotInterleave()
    {
        var text = new string('x', 200);
        var sends = Enumerable.Range(0, 20).Select(i => i % 2 == 0
            ? _channel.TextAsync(text)
            : _channel.KeyAsync(0, AndroidKeys.Tab, 0, 0)).ToArray();
        await Task.WhenAll(sends);

        for (var i = 0; i < 20; i++)
        {
            var type = (await ReadAsync(1))[0];
            if (type == 1)
            {
                var length = BinaryPrimitives.ReadInt32BigEndian(await ReadAsync(4));
                Assert.Equal(200, length);
                Assert.All(await ReadAsync(length), b => Assert.Equal((byte)'x', b));
            }
            else
            {
                Assert.Equal(0, type);
                Assert.Equal(61, BinaryPrimitives.ReadInt32BigEndian((await ReadAsync(13)).AsSpan(1)));
            }
        }
    }

    // ---------------------------------------------------------------- recepcion

    private static byte[] ClipboardMessage(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var m = new byte[5 + bytes.Length];
        m[0] = 0;
        BinaryPrimitives.WriteInt32BigEndian(m.AsSpan(1), bytes.Length);
        bytes.CopyTo(m, 5);
        return m;
    }

    [Fact]
    public async Task ReadLoop_ClipboardAckAndUhid_ThenEndsWhenThePhoneCloses()
    {
        var received = new List<string>();
        _channel.ClipboardReceived += received.Add;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var loop = _channel.ReadLoopAsync(cts.Token);

        await _phoneStream.WriteAsync(ClipboardMessage("copiado en el móvil"));
        await _phoneStream.WriteAsync(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 5 });           // ack del portapapeles
        await _phoneStream.WriteAsync(new byte[] { 2, 0, 1, 0, 3, 0xAA, 0xBB, 0xCC });     // uhid: id 1, 3 bytes
        await _phoneStream.WriteAsync(ClipboardMessage(""));
        // Partido en dos trozos: se tiene que esperar al resto.
        var split = ClipboardMessage("segundo");
        await _phoneStream.WriteAsync(split.AsMemory(0, 3));
        await _phoneStream.FlushAsync();
        await Task.Delay(50);
        await _phoneStream.WriteAsync(split.AsMemory(3));
        await Task.Delay(100);
        _phone.Client.Shutdown(SocketShutdown.Send);

        await loop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["copiado en el móvil", "", "segundo"], received);
    }

    [Fact]
    public async Task ReadLoop_UnknownMessage_EndsQuietly()
    {
        var received = 0;
        _channel.ClipboardReceived += _ => received++;
        var loop = _channel.ReadLoopAsync(CancellationToken.None);

        await _phoneStream.WriteAsync(new byte[] { 42 });

        await loop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, received);
    }

    [Fact]
    public async Task ReadLoop_ClosedInTheMiddleOfAMessage_EndsQuietly()
    {
        var loop = _channel.ReadLoopAsync(CancellationToken.None);

        await _phoneStream.WriteAsync(new byte[] { 0, 0, 0, 0, 10, (byte)'a' });
        _phone.Client.Shutdown(SocketShutdown.Send);

        await loop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReadLoop_Cancelled_EndsQuietly()
    {
        using var cts = new CancellationTokenSource();
        var loop = _channel.ReadLoopAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        await loop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReadLoop_AlreadyCancelled_ReturnsAtOnce()
    {
        await _channel.ReadLoopAsync(new CancellationToken(canceled: true)).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ReadLoop_WithoutSubscribers_DoesNotFail()
    {
        var loop = _channel.ReadLoopAsync(CancellationToken.None);
        await _phoneStream.WriteAsync(ClipboardMessage("nadie escucha"));
        await Task.Delay(50);
        _phone.Client.Shutdown(SocketShutdown.Send);
        await loop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Send_AfterTheSocketIsClosed_Throws()
    {
        _client.Close();
        await Assert.ThrowsAnyAsync<Exception>(() => _channel.RotateDeviceAsync());
    }

    [Fact]
    public void Constants_MatchScrcpy()
    {
        Assert.Equal(-1, ControlChannel.MousePointerId);
        Assert.Equal((0, 1, 2), (ControlChannel.ActionDown, ControlChannel.ActionUp, ControlChannel.ActionMove));
        Assert.Equal((1, 2, 4), (ControlChannel.ButtonPrimary, ControlChannel.ButtonSecondary, ControlChannel.ButtonTertiary));
    }
}
