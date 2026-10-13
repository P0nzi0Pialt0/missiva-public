using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Il costo del cursore fermo sopra l'elenco dei messaggi (Loop 4, agente cursore-I). Le misure di prima giravano senza il
/// cursore sopra la finestra; con il cursore fermo sulla lista, mentre le righe scorrono, WPF rivaluta chi sta sotto il mouse
/// (MouseDevice.Synchronize dopo il layout: MouseMove sintetico, hit test, MouseEnter e MouseLeave) e ogni riga che passa sotto
/// il cursore esegue OnMouseEnter e OnMouseLeave di MessageRowElement (azioni rapide e casella come elementi veri, invalidazioni).
/// Qui i contatori di quello che succede (dentro "scorri N" e "rotella N", riga "cursore:").
/// SOLO nella compilazione Debug e nella versione di prova con gli strumenti (DEBUG): nella versione normale i conteggi non
/// esistono (Conditional), quindi non cambia nulla.
/// Un interruttore solo, da MISSIVA_KEYS mentre gira ("sonda senza-sospensione on" e "off") o dalla variabile d'ambiente
/// all'avvio (MISSIVA_SENZA_SOSPENSIONE=1): spegne il rimedio (ScrollHitGuard), per confrontare con e senza nella stessa
/// corsa. Gli altri interruttori dell'esperimento (uno per causa: azioni rapide, colore al passaggio, suggerimenti,
/// MouseMove, lista inerte, e il rimedio provato e non tenuto) sono stati tolti con l'audit dell'11 ottobre (C-02): la
/// misura per cui servivano si è chiusa il 1 ottobre con ScrollHitGuard.
/// "cursore stato" scrive dove sta il cursore (riga, lista, finestra, altro); "sospensione prova" prova il rimedio.
/// </summary>
static partial class CursorProbe
{
    public enum C { RowEnter, RowLeave, ItemEnter, ItemLeave, SyncCalls, SyncChanges, AddChild, RemoveChild, InvalidateMeasure, InvalidateVisual, RowMeasure, QueryCursor, ToolTipOpening, HitTest, WindowMove, ListMove, RealMove, Suspend, Resume, Count }

    public enum T { EnterLeave, Sync, Count }

#if DEBUG
    static readonly long[] counts = new long[(int)C.Count];
    static readonly long[] ticks = new long[(int)T.Count];
    static System.Windows.Point lastPoint;
#endif

    [Conditional("DEBUG")]
    public static void Hit(C counter)
    {
#if DEBUG
        counts[(int)counter]++;
#endif
    }

    public readonly struct Scope : IDisposable
    {
#if DEBUG
        readonly T timer;
        readonly long start;

        public Scope(T timer)
        {
            this.timer = timer;
            start = Stopwatch.GetTimestamp();
        }

        public void Dispose() => ticks[(int)timer] += Stopwatch.GetTimestamp() - start;
#else
        public void Dispose() { }
#endif
    }

    /// <summary>using var t = CursorProbe.Time(...): il tempo passato nel blocco, sommato per tipo (solo DEBUG).</summary>
    public static Scope Time(T timer)
    {
#if DEBUG
        return new Scope(timer);
#else
        return default;
#endif
    }

    [Conditional("DEBUG")]
    public static void AddWork(double ms)
    {
#if DEBUG
        workSum += ms;
        workCount++;
#endif
    }

    /// <summary>Dal costruttore di MainWindow: gestori di conteggio e l'interruttore iniziale dalla variabile d'ambiente.</summary>
    [Conditional("DEBUG")]
    public static void Setup(Window window, ListBox list)
    {
#if DEBUG
        if (messageList != null) return; // la finestra principale è la prima; le altre (una sola cartella) non contano
        messageList = list;
        // MouseMove dalla finestra: quelli sintetici di WPF (rivalutazione dopo il layout) hanno la stessa posizione del precedente,
        // quelli veri (il mouse si è mosso) una posizione diversa.
        window.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler((_, e) =>
        {
            Hit(C.WindowMove);
            var at = e.GetPosition(window);
            if (at != lastPoint)
            {
                lastPoint = at;
                Hit(C.RealMove);
            }
        }), true);
        list.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler((_, _) => Hit(C.ListMove)), true);
        EventManager.RegisterClassHandler(typeof(ListBoxItem), UIElement.MouseEnterEvent, new MouseEventHandler((_, _) => Hit(C.ItemEnter)));
        EventManager.RegisterClassHandler(typeof(ListBoxItem), UIElement.MouseLeaveEvent, new MouseEventHandler((_, _) => Hit(C.ItemLeave)));
        if (Environment.GetEnvironmentVariable("MISSIVA_SENZA_SOSPENSIONE") == "1") window.Loaded += (_, _) => Apply(NoGuard, true);
#endif
    }
}

#if DEBUG
static partial class CursorProbe
{
    /// <summary>L'unico interruttore rimasto: il rimedio (ScrollHitGuard) spento.</summary>
    const string NoGuard = "senza-sospensione";

    static ListBox? messageList;
    static readonly HashSet<string> active = new();
    static double workSum;
    static int workCount;
    static OpTotals? ops;
    static string startKind = "";

    static string Active() => active.Count == 0 ? "nessuno" : string.Join("+", active.Order());

    /// <summary>Accende o spegne un interruttore; falso se il nome non esiste.</summary>
    public static bool Apply(string name, bool on)
    {
        if (name != NoGuard) return false;
        ScrollHitGuard.Disabled = on;
        if (on) active.Add(name);
        else active.Remove(name);
        return true;
    }

    /// <summary>Comandi di MISSIVA_KEYS: "sonda senza-sospensione on|off", "sospensione prova" e "cursore stato".</summary>
    public static bool Command(Window main, string combo)
    {
        if (combo.StartsWith("sonda ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = combo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && parts[2] is "on" or "off")
                Log.Write(Apply(parts[1], parts[2] == "on") ? $"sonda: {parts[1]} {parts[2]}; interruttori attivi: {Active()}" : $"sonda: interruttore sconosciuto {parts[1]}");
            else Log.Write("sonda: uso \"sonda NOME on|off\"");
            return true;
        }
        if (combo.Equals("sospensione prova", StringComparison.OrdinalIgnoreCase))
        {
            Log.Write(messageList == null ? "sospensione prova: lista non trovata" : $"sospensione prova: {ScrollHitGuard.Test(messageList)}");
            return true;
        }
        if (combo.Equals("cursore stato", StringComparison.OrdinalIgnoreCase))
        {
            Log.Write($"cursore stato: {Status()}");
            return true;
        }
        return false;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Point32 { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetCursorPos(out Point32 point);

    /// <summary>Dove sta il cursore vero (GetCursorPos: una lettura, niente input): riga, lista (barra o spazio vuoto), finestra o altro.</summary>
    static string Kind(out string detail)
    {
        detail = "";
        if (messageList == null || !GetCursorPos(out var p)) return "sconosciuto";
        System.Windows.Point at;
        try { at = messageList.PointFromScreen(new System.Windows.Point(p.X, p.Y)); }
        catch (InvalidOperationException) { return "sconosciuto"; }
        var window = Window.GetWindow(messageList);
        detail = $"posizione={at.X:0},{at.Y:0} lista={messageList.ActualWidth:0}x{messageList.ActualHeight:0} finestra_attiva={window?.IsActive}";
        if (window is not { IsMouseOver: true }) return "altro";
        if (at.X < 0 || at.Y < 0 || at.X > messageList.ActualWidth || at.Y > messageList.ActualHeight) return "finestra";
        return Mouse.DirectlyOver is DependencyObject d && ItemsControl.ContainerFromElement(messageList, d) is ListBoxItem ? "riga" : "lista";
    }

    static string Status()
    {
        var kind = Kind(out var detail);
        return $"sopra={kind} {detail} interruttori={Active()}";
    }

    /// <summary>Comincia una misura (scorri N, rotella N): azzera i contatori e ascolta le operazioni del Dispatcher.</summary>
    public static void Begin(Dispatcher dispatcher)
    {
        Array.Clear(counts);
        Array.Clear(ticks);
        workSum = 0;
        workCount = 0;
        ops?.Stop();
        ops = new OpTotals(dispatcher);
        startKind = Kind(out _);
    }

    /// <summary>La riga "cursore:" della misura appena finita (chiave=valore, poi le operazioni del Dispatcher).</summary>
    public static string End(string label)
    {
        var top = ops?.Top() ?? "";
        ops?.Stop();
        ops = null;
        long n(C c) => counts[(int)c];
        double ms(T t) => ticks[(int)t] * 1000.0 / Stopwatch.Frequency;
        return FormattableString.Invariant($"cursore: prova={label.Replace(' ', '_')} sopra_inizio={startKind} sopra_fine={Kind(out _)} interruttori={Active()} riga_entra={n(C.RowEnter)} riga_esce={n(C.RowLeave)} ") +
               FormattableString.Invariant($"contenitore_entra={n(C.ItemEnter)} contenitore_esce={n(C.ItemLeave)} sync={n(C.SyncCalls)} sync_con_cambi={n(C.SyncChanges)} ") +
               FormattableString.Invariant($"aggiunti={n(C.AddChild)} tolti={n(C.RemoveChild)} inval_misura={n(C.InvalidateMeasure)} inval_disegno={n(C.InvalidateVisual)} misure_riga={n(C.RowMeasure)} ") +
               FormattableString.Invariant($"querycursor={n(C.QueryCursor)} tooltip={n(C.ToolTipOpening)} hittest_righe={n(C.HitTest)} mousemove_finestra={n(C.WindowMove)} mousemove_lista={n(C.ListMove)} ") +
               FormattableString.Invariant($"spostamenti_veri={n(C.RealMove)} sospensioni={n(C.Suspend)} riprese={n(C.Resume)} ms_entra_esce={ms(T.EnterLeave):0.0} ms_sync={ms(T.Sync):0.0} lavoro_frame_medio={(workCount == 0 ? 0 : workSum / workCount):0.00} lavoro_frame_n={workCount} | operazioni: {top}");
    }

    /// <summary>Per ogni operazione del Dispatcher (priorità, metodo): quante volte, tempo totale e massimo.</summary>
    sealed class OpTotals
    {
        static readonly System.Reflection.FieldInfo? MethodField = typeof(DispatcherOperation).GetField("_method", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        readonly DispatcherHooks hooks;
        readonly Dictionary<DispatcherOperation, long> started = new();
        readonly Dictionary<(DispatcherPriority, System.Reflection.MethodInfo?), string> names = new();
        readonly Dictionary<string, (int N, double Ms, double Max)> totals = new();

        public OpTotals(Dispatcher dispatcher)
        {
            hooks = dispatcher.Hooks;
            hooks.OperationStarted += OnStarted;
            hooks.OperationCompleted += OnCompleted;
        }

        void OnStarted(object? sender, DispatcherHookEventArgs e) => started[e.Operation] = Stopwatch.GetTimestamp();

        void OnCompleted(object? sender, DispatcherHookEventArgs e)
        {
            if (!started.Remove(e.Operation, out var at)) return;
            double ms = (Stopwatch.GetTimestamp() - at) * 1000.0 / Stopwatch.Frequency;
            var method = (MethodField?.GetValue(e.Operation) as Delegate)?.Method;
            var key = (e.Operation.Priority, method);
            if (!names.TryGetValue(key, out var name))
                names[key] = name = $"{e.Operation.Priority} {(method?.DeclaringType?.DeclaringType ?? method?.DeclaringType)?.Name}.{method?.Name}";
            var old = totals.GetValueOrDefault(name);
            totals[name] = (old.N + 1, old.Ms + ms, Math.Max(old.Max, ms));
        }

        public void Stop()
        {
            hooks.OperationStarted -= OnStarted;
            hooks.OperationCompleted -= OnCompleted;
        }

        /// <summary>Le 6 operazioni che hanno preso più tempo e, dopo, quelle di input, del mouse, dei suggerimenti e del hit test (al massimo 8).</summary>
        public string Top()
        {
            var ordered = totals.OrderByDescending(t => t.Value.Ms).ToList();
            var shown = ordered.Take(6).Concat(ordered.Skip(6).Where(t => t.Key.StartsWith("Input ") || t.Key.StartsWith("Loaded ") || t.Key.Contains("Mouse") || t.Key.Contains("Stylus") || t.Key.Contains("ToolTip") || t.Key.Contains("HitTest") || t.Key.Contains("Reevaluate") || t.Key.Contains("Synchronize")).Take(8));
            return string.Join("; ", shown.Select(t => FormattableString.Invariant($"{t.Key} {t.Value.N}x {t.Value.Ms:0.0} ms (max {t.Value.Max:0.0})")));
        }
    }
}
#endif
