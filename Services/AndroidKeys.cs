using System.Windows.Input;

namespace PhoneMirror.Services;

/// <summary>
/// Codigos de tecla de Android (<c>android.view.KeyEvent.KEYCODE_*</c>) y la correspondencia con
/// las teclas de Windows que no son texto: flechas, borrar, intro, escape…
/// </summary>
/// <remarks>
/// Las letras y los signos no van por aqui: se mandan como texto (<c>TextInput</c> de WPF), que
/// es lo unico que respeta la distribucion del teclado del PC sin tener que saber cual es.
/// </remarks>
public static class AndroidKeys
{
    public const int Home = 3;
    public const int Back = 4;
    public const int VolumeUp = 24;
    public const int VolumeDown = 25;
    public const int Power = 26;
    public const int Tab = 61;
    public const int Space = 62;
    public const int Enter = 66;
    public const int Del = 67;
    public const int Escape = 111;
    public const int ForwardDel = 112;
    public const int MoveHome = 122;
    public const int MoveEnd = 123;
    public const int PageUp = 92;
    public const int PageDown = 93;
    public const int DpadUp = 19;
    public const int DpadDown = 20;
    public const int DpadLeft = 21;
    public const int DpadRight = 22;
    public const int AppSwitch = 187;
    public const int MediaPlayPause = 85;
    public const int MediaNext = 87;
    public const int MediaPrevious = 88;
    public const int VolumeMute = 164;

    public const int MetaShiftOn = 0x1;
    public const int MetaAltOn = 0x2;
    public const int MetaCtrlOn = 0x1000;

    /// <summary>Teclas que viajan como codigo. Devuelve <c>null</c> para las que van como texto.</summary>
    public static int? FromKey(Key key) => key switch
    {
        Key.Enter => Enter,
        Key.Back => Del,
        Key.Delete => ForwardDel,
        Key.Escape => Escape,
        Key.Tab => Tab,
        Key.Left => DpadLeft,
        Key.Right => DpadRight,
        Key.Up => DpadUp,
        Key.Down => DpadDown,
        Key.Home => MoveHome,
        Key.End => MoveEnd,
        Key.PageUp => PageUp,
        Key.PageDown => PageDown,
        Key.VolumeUp => VolumeUp,
        Key.VolumeDown => VolumeDown,
        Key.VolumeMute => VolumeMute,
        Key.MediaPlayPause => MediaPlayPause,
        Key.MediaNextTrack => MediaNext,
        Key.MediaPreviousTrack => MediaPrevious,
        _ => null,
    };

    public static int MetaState(ModifierKeys modifiers)
    {
        var meta = 0;
        if (modifiers.HasFlag(ModifierKeys.Shift))
            meta |= MetaShiftOn;
        if (modifiers.HasFlag(ModifierKeys.Alt))
            meta |= MetaAltOn;
        if (modifiers.HasFlag(ModifierKeys.Control))
            meta |= MetaCtrlOn;
        return meta;
    }
}
