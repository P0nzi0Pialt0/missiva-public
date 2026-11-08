using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Missiva.UI;

/// <summary>
/// La barra delle applicazioni di Windows nascosta automaticamente, con Missiva ingrandita (richiesta dell'autore dell'8
/// novembre 2026: "quando l'app è a tutto schermo, la barra di windows, che io ho a scomparsa, non compare più"). Con la
/// barra del titolo disegnata da Missiva (WindowChrome) l'area client è tutta la finestra, e ingrandita copre tutto lo
/// schermo: Windows la prende per una finestra a schermo intero e non fa più uscire la barra quando il mouse va sul bordo.
/// Qui, ingrandita, l'area client lascia liberi due pixel su ogni bordo dello schermo dove c'è una barra nascosta
/// automaticamente (WM_NCCALCSIZE, prima di quello della WindowChrome, che fa lo stesso conto senza quei pixel), come
/// fanno le altre app con la barra del titolo propria. Senza barre nascoste nulla cambia.
/// </summary>
public static class AutoHideTaskbar
{
    const int WM_NCCALCSIZE = 0x0083;
    const uint ABM_GETSTATE = 0x4, ABM_GETTASKBARPOS = 0x5, ABM_GETAUTOHIDEBAREX = 0xB, ABS_AUTOHIDE = 0x1;
    /// <summary>
    /// Quanto resta libero sul bordo della barra: 2 pixel, come Chromium (kAutoHideTaskbarThicknessPx), che con la barra del
    /// titolo propria ha lo stesso problema.
    /// </summary>
    const int Gap = 2;
    /// <summary>Come risponde la WindowChrome di WPF (in .NET 10) a WM_NCCALCSIZE: WVR_REDRAW, misurato con la prova.</summary>
    const int Redraw = 0x0300;

    /// <summary>I bordi dello schermo, come gli ABE_ di Windows: un bit per bordo.</summary>
    [Flags]
    public enum Edges { None = 0, Left = 1 << 0, Top = 1 << 1, Right = 1 << 2, Bottom = 1 << 3 }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect32 { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct AppBarData { public int Size; public IntPtr Window; public uint CallbackMessage; public uint Edge; public Rect32 Rect; public IntPtr Param; }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo { public int Size; public Rect32 Monitor, Work; public uint Flags; }

    [DllImport("shell32.dll")]
    static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromRect(ref Rect32 rect, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    static extern bool IsZoomed(IntPtr hwnd);

#if DEBUG
    /// <summary>Prove: le barre nascoste al posto di quelle vere, e la finestra come se fosse ingrandita.</summary>
    internal static Func<Rect32, Edges>? TestEdges;
    internal static bool TestZoomed;
#endif

    /// <summary>Dopo la creazione della finestra (OnSourceInitialized, dopo la WindowChrome): il gancio passa prima del suo.</summary>
    public static void Attach(Window window) =>
        HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(Hook);

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCCALCSIZE || wParam == IntPtr.Zero || !Zoomed(hwnd)) return IntPtr.Zero;
        // Il primo campo di NCCALCSIZE_PARAMS è la finestra proposta, che con la WindowChrome diventa tutta area client.
        var proposed = Marshal.PtrToStructure<Rect32>(lParam);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref proposed, 2 /* MONITOR_DEFAULTTONEAREST */), ref info)) return IntPtr.Zero;
        var edges = HiddenBars(info.Monitor);
        // Una riga nel registro, anche nella versione installata, quando cambia: per sapere se la correzione è scattata.
        var line = edges == Edges.None ? "nessuna barra nascosta" : $"barra nascosta ({edges}): area client ridotta di {Gap} pixel su quel bordo";
        if (line != lastLine)
        {
            lastLine = line;
            Log.Write($"finestra ingrandita: {line}");
        }
        if (edges == Edges.None) return IntPtr.Zero;
        Marshal.StructureToPtr(Client(proposed, info.Monitor, edges), lParam, false);
        handled = true;
        return new IntPtr(Redraw);
    }

    static string? lastLine;

    static bool Zoomed(IntPtr hwnd)
    {
#if DEBUG
        if (TestZoomed) return true;
#endif
        return IsZoomed(hwnd);
    }

    /// <summary>L'area client della finestra ingrandita: Gap pixel dentro lo schermo sui bordi con una barra nascosta.</summary>
    public static Rect32 Client(Rect32 proposed, Rect32 monitor, Edges edges)
    {
        var client = proposed;
        if (edges.HasFlag(Edges.Left)) client.Left = Math.Max(client.Left, monitor.Left + Gap);
        if (edges.HasFlag(Edges.Top)) client.Top = Math.Max(client.Top, monitor.Top + Gap);
        if (edges.HasFlag(Edges.Right)) client.Right = Math.Min(client.Right, monitor.Right - Gap);
        if (edges.HasFlag(Edges.Bottom)) client.Bottom = Math.Min(client.Bottom, monitor.Bottom - Gap);
        return client;
    }

    /// <summary>
    /// I bordi dello schermo con una barra nascosta automaticamente: per bordo e schermo (ABM_GETAUTOHIDEBAREX, anche le
    /// barre degli altri schermi), e se nessuno risponde il bordo della barra principale (ABM_GETTASKBARPOS). Sul PC
    /// dell'autore (Windows 11, 10.0.26300, 8 novembre 2026) ABM_GETSTATE dice "nascosta" e ABM_GETTASKBARPOS "in basso",
    /// ma ABM_GETAUTOHIDEBAREX e ABM_GETAUTOHIDEBAR rispondono vuoto su tutti i bordi: con la sola domanda per bordo la
    /// correzione non scattava mai. Windows 11 mette la barra sullo stesso bordo in tutti gli schermi.
    /// </summary>
    internal static Edges HiddenBars(Rect32 monitor)
    {
#if DEBUG
        if (TestEdges is { } test) return test(monitor);
#endif
        var state = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        if (((long)SHAppBarMessage(ABM_GETSTATE, ref state) & ABS_AUTOHIDE) == 0) return Edges.None;
        var edges = Edges.None;
        for (uint edge = 0; edge < 4; edge++) // ABE_LEFT, ABE_TOP, ABE_RIGHT, ABE_BOTTOM
        {
            var bar = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Edge = edge, Rect = monitor };
            if (SHAppBarMessage(ABM_GETAUTOHIDEBAREX, ref bar) != IntPtr.Zero) edges |= (Edges)(1 << (int)edge);
        }
        if (edges != Edges.None) return edges;
        var main = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        return SHAppBarMessage(ABM_GETTASKBARPOS, ref main) != IntPtr.Zero && main.Edge < 4 ? (Edges)(1 << (int)main.Edge) : Edges.None;
    }
}
