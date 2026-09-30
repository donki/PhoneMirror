namespace PhoneMirror.Services;

/// <summary>
/// Los argumentos de la linea de ordenes:
/// <c>--connect</c> conecta solo con el primer movil que haya;
/// <c>--serial XXXX</c> con ese movil en concreto (una ventana por movil);
/// <c>--tray</c> arranca escondida en la bandeja (<see cref="OpenOnConnect"/>);
/// <c>--new</c> abre otra ventana sin preguntar.
/// </summary>
public sealed record LaunchArguments(bool AutoConnect, string? PreferredSerial, bool StartInTray, bool AskForInstances)
{
    public const string TrayArgument = "--tray";

    public static LaunchArguments Parse(IReadOnlyList<string> args)
    {
        var list = args.ToList();
        var serialIndex = list.IndexOf("--serial");
        var tray = list.Contains(TrayArgument);
        var connect = list.Contains("--connect");

        // Solo se pregunta por las otras ventanas cuando arranca el usuario a mano; con
        // --tray/--connect/--serial (accesos directos y arranque con Windows) no.
        var ask = !tray && serialIndex < 0 && !connect && !list.Contains("--new");

        return new LaunchArguments(
            connect || serialIndex >= 0,
            serialIndex >= 0 && serialIndex + 1 < list.Count ? list[serialIndex + 1] : null,
            tray,
            ask);
    }
}
