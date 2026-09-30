using System.Windows.Input;
using PhoneMirror.Services;

namespace PhoneMirror.Tests;

public class AndroidKeysTests
{
    [Theory]
    [InlineData(Key.Enter, 66)]
    [InlineData(Key.Back, 67)]
    [InlineData(Key.Delete, 112)]
    [InlineData(Key.Escape, 111)]
    [InlineData(Key.Tab, 61)]
    [InlineData(Key.Left, 21)]
    [InlineData(Key.Right, 22)]
    [InlineData(Key.Up, 19)]
    [InlineData(Key.Down, 20)]
    [InlineData(Key.Home, 122)]
    [InlineData(Key.End, 123)]
    [InlineData(Key.PageUp, 92)]
    [InlineData(Key.PageDown, 93)]
    [InlineData(Key.VolumeUp, 24)]
    [InlineData(Key.VolumeDown, 25)]
    [InlineData(Key.VolumeMute, 164)]
    [InlineData(Key.MediaPlayPause, 85)]
    [InlineData(Key.MediaNextTrack, 87)]
    [InlineData(Key.MediaPreviousTrack, 88)]
    public void FromKey_NonTextKeysGoAsKeycodes(Key key, int keycode) =>
        Assert.Equal(keycode, AndroidKeys.FromKey(key));

    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.D1)]
    [InlineData(Key.Space)]
    [InlineData(Key.OemComma)]
    [InlineData(Key.F5)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.None)]
    public void FromKey_TextAndOtherKeysAreNull(Key key) =>
        Assert.Null(AndroidKeys.FromKey(key));

    [Fact]
    public void FromKey_AllMappedKeycodesAreDistinct()
    {
        var mapped = Enum.GetValues<Key>().Distinct().Select(AndroidKeys.FromKey).OfType<int>().ToList();
        Assert.Equal(19, mapped.Count);
        Assert.Equal(mapped.Count, mapped.Distinct().Count());
    }

    [Theory]
    [InlineData(ModifierKeys.None, 0)]
    [InlineData(ModifierKeys.Shift, 0x1)]
    [InlineData(ModifierKeys.Alt, 0x2)]
    [InlineData(ModifierKeys.Control, 0x1000)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, 0x1001)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, 0x1003)]
    [InlineData(ModifierKeys.Windows, 0)]
    public void MetaState(ModifierKeys modifiers, int expected) =>
        Assert.Equal(expected, AndroidKeys.MetaState(modifiers));

    [Fact]
    public void ButtonKeycodes_AreAndroidOnes()
    {
        Assert.Equal(3, AndroidKeys.Home);
        Assert.Equal(4, AndroidKeys.Back);
        Assert.Equal(26, AndroidKeys.Power);
        Assert.Equal(187, AndroidKeys.AppSwitch);
        Assert.Equal(62, AndroidKeys.Space);
    }
}
