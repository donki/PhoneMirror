using PhoneMirror.Services;

namespace PhoneMirror.Tests;

public class AdbParsingTests
{
    private const string DevicesOutput =
        "* daemon not running; starting now at tcp:5037\r\n" +
        "* daemon started successfully\r\n" +
        "List of devices attached\r\n" +
        "R58M123ABC             device usb:1-1 product:beyond1lteeea model:SM_G973F device:beyond1 transport_id:1\r\n" +
        "192.168.1.40:5555      device product:panther model:Pixel_7 device:panther transport_id:3\r\n" +
        "emulator-5554          offline transport_id:2\r\n" +
        "0123456789ABCDEF       unauthorized usb:1-2 transport_id:4\r\n" +
        "adb-R58M-abc._adb-tls-connect._tcp\tdevice product:a model:Redmi_Note_12 device:b transport_id:5\r\n" +
        "\r\n";

    [Fact]
    public void ParseDevices_ReadsSerialStateAndModel_AndSkipsHeaderAndDaemonLines()
    {
        var devices = AdbService.ParseDevices(DevicesOutput);

        Assert.Equal(5, devices.Count);
        Assert.Equal(new AdbDevice("R58M123ABC", "device", "SM G973F"), devices[0]);
        Assert.Equal(new AdbDevice("192.168.1.40:5555", "device", "Pixel 7"), devices[1]);
        Assert.Equal(new AdbDevice("emulator-5554", "offline", ""), devices[2]);
        Assert.Equal(new AdbDevice("0123456789ABCDEF", "unauthorized", ""), devices[3]);
        Assert.Equal(new AdbDevice("adb-R58M-abc._adb-tls-connect._tcp", "device", "Redmi Note 12"), devices[4]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("List of devices attached\n\n")]
    [InlineData("   \n\t\n")]
    [InlineData("solounapalabra\n")]
    public void ParseDevices_NothingUsable_GivesEmptyList(string output) =>
        Assert.Empty(AdbService.ParseDevices(output));

    [Fact]
    public void ParseDevices_ModelIsTakenOnlyFromTheModelKey()
    {
        var devices = AdbService.ParseDevices("ABC device product:model_x model:A:B device:model:C\nDEF device model:\nGHI device modelo:X model\n");

        Assert.Equal("A:B", devices[0].Model);
        Assert.Equal("", devices[1].Model);
        Assert.Equal("", devices[2].Model);
    }

    [Fact]
    public void AdbDevice_IsReadyOnlyInDeviceState()
    {
        Assert.True(new AdbDevice("s", "device", "").IsReady);
        Assert.False(new AdbDevice("s", "unauthorized", "").IsReady);
        Assert.False(new AdbDevice("s", "offline", "").IsReady);
        Assert.False(new AdbDevice("s", "Device", "").IsReady);
    }

    [Fact]
    public void AdbDevice_CaptionShowsModelWhenKnown()
    {
        Assert.Equal("Pixel 7 · ABC", new AdbDevice("ABC", "device", "Pixel 7").Caption);
        Assert.Equal("ABC", new AdbDevice("ABC", "device", "").Caption);
    }

    [Theory]
    [InlineData("192.168.1.40:5555", true)]
    [InlineData("10.0.0.2:37123", true)]
    [InlineData("emulator-5554", false)]
    [InlineData("R58M123ABC", false)]
    [InlineData("adb-R58M-abc._adb-tls-connect._tcp", false)]
    [InlineData("localhost:5555", false)]
    [InlineData("192.168.1.40:puerto", false)]
    [InlineData("192.168.1.40:", false)]
    [InlineData("[fe80::1]:5555", false)]
    [InlineData("", false)]
    public void IsNetworkSerial(string serial, bool expected) =>
        Assert.Equal(expected, AdbService.IsNetworkSerial(serial));

    [Theory]
    [InlineData("192.168.1.40", "192.168.1.40:5555")]
    [InlineData("  192.168.1.40  ", "192.168.1.40:5555")]
    [InlineData("192.168.1.40:41234", "192.168.1.40:41234")]
    [InlineData(" movil.local:5555 ", "movil.local:5555")]
    public void WithPort_AddsDefaultPortOnlyWhenMissing(string address, string expected) =>
        Assert.Equal(expected, AdbService.WithPort(address));

    [Theory]
    [InlineData("connected to 192.168.1.40:5555", true)]
    [InlineData("already connected to 192.168.1.40:5555", true)]
    [InlineData("CONNECTED TO 192.168.1.40:5555", true)]
    [InlineData("cannot connect to 192.168.1.40:5555: No connection could be made (10061)", false)]
    [InlineData("failed to connect to '192.168.1.40:5555': Connection refused", false)]
    [InlineData("failed to authenticate to 192.168.1.40:5555", false)]
    [InlineData("connected to 192.168.1.40:5555\nfailed to authenticate", false)]
    [InlineData("", false)]
    public void IsConnected_ReadsTheTextBecauseExitCodeIsAlwaysZero(string output, bool expected) =>
        Assert.Equal(expected, AdbService.IsConnected(output));

    [Theory]
    [InlineData("Successfully paired to 192.168.1.40:37000 [guid=adb-R58M-abc]", true)]
    [InlineData("successfully PAIRED to x", true)]
    [InlineData("Failed: Wrong password or connection was dropped.", false)]
    [InlineData("", false)]
    public void IsPaired(string output, bool expected) =>
        Assert.Equal(expected, AdbService.IsPaired(output));
}
