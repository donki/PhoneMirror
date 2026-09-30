using PhoneMirror.Services;

namespace PhoneMirror.Tests;

public class LaunchArgumentsTests
{
    [Fact]
    public void NoArguments_OpensNormallyAndAsksAboutOtherWindows() =>
        Assert.Equal(new LaunchArguments(false, null, false, true), LaunchArguments.Parse([]));

    [Fact]
    public void Connect_AutoConnectsWithoutAsking() =>
        Assert.Equal(new LaunchArguments(true, null, false, false), LaunchArguments.Parse(["--connect"]));

    [Fact]
    public void Serial_AutoConnectsToThatPhone() =>
        Assert.Equal(new LaunchArguments(true, "R58M123ABC", false, false), LaunchArguments.Parse(["--serial", "R58M123ABC"]));

    [Fact]
    public void Serial_WithoutValue_StillAutoConnects() =>
        Assert.Equal(new LaunchArguments(true, null, false, false), LaunchArguments.Parse(["--serial"]));

    [Fact]
    public void Serial_TakesTheNextArgumentAsIs() =>
        Assert.Equal("--tray", LaunchArguments.Parse(["--serial", "--tray"]).PreferredSerial);

    [Fact]
    public void Tray_StartsHiddenWithoutAsking() =>
        Assert.Equal(new LaunchArguments(false, null, true, false), LaunchArguments.Parse(["--tray"]));

    [Fact]
    public void New_OpensAnotherWindowWithoutAsking() =>
        Assert.Equal(new LaunchArguments(false, null, false, false), LaunchArguments.Parse(["--new"]));

    [Fact]
    public void Combined_AndUnknownArgumentsIgnored() =>
        Assert.Equal(new LaunchArguments(true, "ABC", true, false),
            LaunchArguments.Parse(["--otra", "--tray", "--serial", "ABC", "--connect"]));

    [Fact]
    public void OptionsAreCaseSensitive() =>
        Assert.Equal(new LaunchArguments(false, null, false, true), LaunchArguments.Parse(["--TRAY", "--Connect"]));

    [Fact]
    public void TrayArgumentConstant() => Assert.Equal("--tray", LaunchArguments.TrayArgument);
}
