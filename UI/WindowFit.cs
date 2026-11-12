using System.Runtime.InteropServices;
using System.Windows;

namespace Missiva.UI;

/// <summary>
/// La misura di partenza della finestra principale (12 novembre 2026, l'autore, dopo averla installata su un altro PC: "la
/// finestra non era centrata nello schermo al primo avvio"). La finestra nasce a 1320 x 820 DIP al
/// centro dello schermo (CenterScreen nel XAML), e WPF non la riduce: su uno schermo con un'area utile più piccola (un
/// 1920 x 1080 al 150% ha 1280 x 672 DIP, con la barra delle applicazioni) la centra lo stesso, con la barra del titolo
/// sopra il bordo dello schermo e il fondo sotto la barra delle applicazioni. Qui, prima che la finestra esista, la misura
/// scende quanto serve per stare nell'area utile dello schermo dove WPF la metterà (quello del mouse, come fa CenterScreen),
/// con un margine; dove ci sta non cambia nulla. La posizione resta di WPF.
/// </summary>
static class WindowFit
{
    /// <summary>Lo spazio lasciato fra la finestra e i bordi dell'area utile, in DIP, quando la misura scende.</summary>
    internal const double Margin = 24;

    /// <summary>La misura voluta, ridotta all'area utile meno il margine, mai sotto la minima della finestra.</summary>
    internal static Size Fit(Size wanted, Size work, Size min) =>
        new(Math.Max(min.Width, Math.Min(wanted.Width, work.Width - 2 * Margin)),
            Math.Max(min.Height, Math.Min(wanted.Height, work.Height - 2 * Margin)));

    /// <summary>
    /// Prima che la finestra esista: Width e Height ridotte all'area utile dello schermo sotto il mouse, se serve. Lo stesso
    /// per le altre finestre grandi (scrittura, Impostazioni, regole, messaggio, scheda della persona, firma digitale):
    /// centrate sulla principale, su uno schermo piccolo uscivano anche loro in alto. Nel registro una riga per tipo di
    /// finestra, mai il titolo (ci sono gli oggetti dei messaggi).
    /// </summary>
    internal static void FitStart(Window window)
    {
        if (WorkAtCursor() is not { } at) return;
        var size = Fit(new Size(window.Width, window.Height), at.Work.Size, new Size(window.MinWidth, window.MinHeight));
        if (size.Width == window.Width && size.Height == window.Height) return;
        lock (logged)
            if (logged.Add(window.GetType().Name))
                Log.Write($"finestra {window.GetType().Name}: lo schermo ha un'area utile di {at.Work.Width:0} x {at.Work.Height:0} DIP, parte a {size.Width:0} x {size.Height:0} invece di {window.Width:0} x {window.Height:0}");
        window.Width = size.Width;
        window.Height = size.Height;
    }

    static readonly HashSet<string> logged = new();

    /// <summary>
    /// L'area utile (lo schermo senza la barra delle applicazioni) in DIP dello schermo sotto il mouse, con la scala usata,
    /// e se è lo schermo principale; null se Windows non risponde. Con schermi a scale diverse WPF crea la finestra alla
    /// scala del sistema (quella dello schermo principale) e la porta a quella dello schermo solo quando la mostra: la scala
    /// è la più grande delle due, così la finestra sta dentro in tutti e due i momenti. Visto il 12 novembre sul secondo
    /// schermo dell'autore, a 1,75 con il sistema a 2: contata a 1,75, la finestra appena creata usciva ancora in alto.
    /// </summary>
    internal static (Rect Work, double Scale, double MonitorScale, double SystemScale, bool Primary)? WorkAtCursor()
    {
        if (!GetCursorPos(out var point)) return null;
        var monitor = MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpi, out _) != 0 || dpi == 0) return null;
        uint system = GetDpiForSystem();
        double scale = Math.Max(dpi, system) / 96.0;
        var work = new Rect(info.Work.Left / scale, info.Work.Top / scale, (info.Work.Right - info.Work.Left) / scale, (info.Work.Bottom - info.Work.Top) / scale);
        return (work, scale, dpi / 96.0, system / 96.0, (info.Flags & 1 /* MONITORINFOF_PRIMARY */) != 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point32 { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect32 { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo { public int Size; public Rect32 Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out Point32 point);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Point32 point, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();
}
