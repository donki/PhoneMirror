using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PhoneMirror.Services;

/// <summary>
/// Las otras ventanas de Phone Mirror que ya estan abiertas en este PC (una por movil), esten a la
/// vista o escondidas en la bandeja, y la forma de pedirles que se enseñen.
/// </summary>
/// <remarks>
/// Cada instancia es un proceso. Para encontrarlas se recorren las ventanas de primer nivel de los
/// procesos con el mismo nombre de ejecutable, visibles o no (una ventana escondida en la bandeja
/// sigue teniendo su HWND). Para «abrirla» se le manda un mensaje de ventana registrado con nombre
/// (<see cref="ShowMessage"/>), que la instancia atiende en <c>MainWindow</c> enseñandose.
/// </remarks>
public static class Instances
{
    public const string ShowMessageName = "sOCPhoneMirror.Show";

    public static readonly uint ShowMessage = RegisterWindowMessage(ShowMessageName);

    public sealed record Instance(int ProcessId, IntPtr Handle, string Title);

    public static IReadOnlyList<Instance> Others()
    {
        var me = Environment.ProcessId;
        var name = Process.GetCurrentProcess().ProcessName;
        var ids = Process.GetProcessesByName(name).Select(p => p.Id).Where(id => id != me).ToHashSet();
        var found = new List<Instance>();
        if (ids.Count == 0)
            return found;

        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (!ids.Contains((int)pid))
                return true;
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            // La ventana principal lleva el nombre de la aplicacion; las demas (tooltips, menus) no.
            if (title.StartsWith("sOC Phone Mirror", StringComparison.OrdinalIgnoreCase) && found.All(f => f.ProcessId != pid))
                found.Add(new Instance((int)pid, h, title));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Le pide a esa instancia que se enseñe (sale de la bandeja si estaba escondida).</summary>
    public static void Show(Instance instance)
    {
        PostMessage(instance.Handle, ShowMessage, IntPtr.Zero, IntPtr.Zero);
        SetForegroundWindow(instance.Handle);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}
