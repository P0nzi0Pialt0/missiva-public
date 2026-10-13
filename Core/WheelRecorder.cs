#if DEBUG
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva;

/// <summary>
/// Prove con MISSIVA_RECORD=1: registra uno scorrimento vero (touchpad, rotellina) fatto a mano nella versione di prova e
/// scrive nel registro "rotella vera:" con gli eventi arrivati (quanti, di che ampiezza, ogni quanto), i fotogrammi
/// disegnati nel frattempo (intervalli, fermi a metà, passi) e la traccia dei fotogrammi oltre 12 ms (DevTools.FrameTrace).
/// Il registratore parte al primo evento della rotellina e finisce dopo 0,6 s senza eventi e 0,4 s senza movimento.
/// </summary>
static class WheelRecorder
{
    static readonly Stopwatch clock = new();
    static readonly List<(double At, int Delta, int Stamp)> events = new();
    static readonly List<(double At, double Offset, double Gap)> frames = new();
    static ScrollViewer? viewer;
    static DevTools.FrameTrace? trace;
    static bool recording;
    static TimeSpan lastRender;
    static double lastEvent, lastMotion;
    static int collections;
    /// <summary>I modelli dei movimenti fini provati alla cieca (WheelScrolling.TouchModels; Ctrl+Maiusc+1..4 scelgono A..D, in un ordine a caso scritto nel registro).</summary>
    static readonly UI.WheelScrolling.TouchModel[] Models = UI.WheelScrolling.TouchModels;
    static int[] mapping = { 0, 1, 2, 3 };
    static string variant = "-";

    public static void EnableVariants()
    {
        var random = new Random();
        mapping = Enumerable.Range(0, Models.Length).OrderBy(_ => random.Next()).ToArray();
        Log.Write("rotella vera, varianti: " + string.Join(" | ", Enumerable.Range(0, Models.Length).Select(i => $"{(char)('A' + i)} = {Models[mapping[i]].Name}")));
        EventManager.RegisterClassHandler(typeof(Window), System.Windows.Input.Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((_, e) =>
        {
            if (System.Windows.Input.Keyboard.Modifiers != (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)) return;
            int i = e.Key switch { System.Windows.Input.Key.D1 => 0, System.Windows.Input.Key.D2 => 1, System.Windows.Input.Key.D3 => 2, System.Windows.Input.Key.D4 => 3, _ => -1 };
            if (i < 0) return;
            e.Handled = true;
            Select(i);
        }));
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Select(0);
            if (Application.Current.MainWindow is { } main)
                System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.TitleProperty, typeof(Window)).AddValueChanged(main, (_, _) => ShowVariant());
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    static void Select(int index)
    {
        UI.WheelScrolling.Touch = Models[mapping[index]];
        variant = ((char)('A' + index)).ToString();
        Log.Write($"rotella vera, variante {variant} ({UI.WheelScrolling.Touch.Name})");
        ShowVariant();
    }

    static void ShowVariant()
    {
        if (Application.Current.MainWindow is not { } main) return;
        string title = main.Title, marker = $"   [{variant}]";
        int at = title.IndexOf("   [", StringComparison.Ordinal);
        if (at >= 0) title = title[..at];
        if (main.Title != title + marker) main.Title = title + marker;
    }

    public static void Seen(ScrollViewer target, int delta, int stamp)
    {
        if (!recording) Start(target);
        lastEvent = clock.Elapsed.TotalMilliseconds;
        events.Add((lastEvent, delta, stamp));
    }

    static void Start(ScrollViewer target)
    {
        recording = true;
        viewer = target;
        events.Clear();
        frames.Clear();
        clock.Restart();
        lastEvent = lastMotion = 0;
        lastRender = default;
        collections = GC.CollectionCount(0);
        UI.WheelScrolling.StepLog = new List<string>();
        UI.WheelScrolling.StepClock = clock;
        trace = new DevTools.FrameTrace(target.Dispatcher);
        CompositionTarget.Rendering += OnFrame;
    }

    static void OnFrame(object? sender, EventArgs e)
    {
        var at = ((RenderingEventArgs)e).RenderingTime;
        if (at == lastRender || viewer == null) return;
        double gap = lastRender == default ? 0 : (at - lastRender).TotalMilliseconds;
        lastRender = at;
        trace?.TickStart();
        double now = clock.Elapsed.TotalMilliseconds, offset = viewer.VerticalOffset;
        if (frames.Count > 0 && frames[^1].Offset != offset) lastMotion = now;
        frames.Add((now, offset, gap));
        trace?.TickEnd();
        if (now - lastEvent > 600 && now - lastMotion > 400) Finish();
    }

    static double Percentile(List<double> sorted, double p) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];

    static void Finish()
    {
        CompositionTarget.Rendering -= OnFrame;
        trace?.Stop();
        recording = false;
        var abs = events.Select(v => (double)Math.Abs(v.Delta)).OrderBy(v => v).ToList();
        var gaps = events.Zip(events.Skip(1), (a, b) => b.At - a.At).OrderBy(v => v).ToList();
        // Dal primo evento all'ultimo movimento: i passi di ogni fotogramma.
        int first = frames.FindIndex(f => f.Offset != frames[0].Offset);
        int last = frames.FindLastIndex(f => f.Offset != frames[^1].Offset);
        var steps = new List<double>();
        for (int i = Math.Max(1, first); i <= last + 1 && i < frames.Count; i++) steps.Add(frames[i].Offset - frames[i - 1].Offset);
        var motion = steps.Select(Math.Abs).ToList();
        double mean = motion.Count == 0 ? 0 : motion.Average();
        double jerk = motion.Count < 2 ? 0 : motion.Zip(motion.Skip(1), (a, b) => Math.Abs(b - a)).Average() / Math.Max(mean, 0.01);
        var intervals = frames.Skip(1).Select(f => f.Gap).ToList();
        Log.Write(
            $"rotella vera: {events.Count} eventi in {(events.Count == 0 ? 0 : events[^1].At - events[0].At):0} ms, ampiezze {(abs.Count == 0 ? 0 : abs[0]):0}/{Percentile(abs, 0.5):0}/{(abs.Count == 0 ? 0 : abs[^1]):0} (min/mediana/max), " +
            $"multipli di 120: {events.Count(v => v.Delta % 120 == 0)}, intervalli fra eventi {Percentile(gaps, 0.1):0.0}/{Percentile(gaps, 0.5):0.0}/{Percentile(gaps, 0.9):0.0}/{(gaps.Count == 0 ? 0 : gaps[^1]):0.0} ms (10%/mediana/90%/massimo); " +
            $"{frames.Count} fotogrammi, ogni {(intervals.Count == 0 ? 0 : intervals.Average()):0.0} ms (peggiore {(intervals.Count == 0 ? 0 : intervals.Max()):0.0}), oltre 16,7 ms: {intervals.Count(g => g > 16.7)}, oltre 25 ms: {intervals.Count(g => g > 25)}, raccolte di .NET: {GC.CollectionCount(0) - collections}; " +
            $"passi in movimento: {steps.Count}, medio {mean:0.0} px, da {(motion.Count == 0 ? 0 : motion.Min()):0.0} a {(motion.Count == 0 ? 0 : motion.Max()):0.0}, fermi a metà {motion.Skip(1).SkipLast(1).Count(s => s < 0.01)}, sbalzo medio fra passi {jerk * 100:0}%");
        Log.Write("rotella vera, eventi (ms:ampiezza): " + string.Join(" ", events.Take(80).Select(v => $"{v.At:0}:{v.Delta}")));
        Log.Write("rotella vera, passi (px per fotogramma): " + string.Join(" ", steps.Take(120).Select(s => s.ToString("0.#"))));
        Log.Write("rotella vera, intervalli dei fotogrammi (ms): " + string.Join(" ", intervals.Skip(Math.Max(0, first)).Take(120).Select(g => g.ToString("0.#"))));
        try
        {
            // Tutto il gesto in un file, per rifarlo offline: E = evento (ms dall'inizio, ampiezza, timestamp del messaggio), F = fotogramma (ms, posizione).
            var lines = new List<string> { "tipo;ms;valore;timestamp", $"T;0;{UI.WheelScrolling.Touch.TauSmall:0};{variant};{UI.WheelScrolling.Touch.Name}" };
            lines.AddRange(events.Select(v => $"E;{v.At:0.00};{v.Delta};{v.Stamp}"));
            lines.AddRange(frames.Select(f => $"F;{f.At:0.00};{f.Offset:0.000};{f.Gap:0.00}"));
            lines.AddRange(UI.WheelScrolling.StepLog ?? new List<string>());
            var path = Path.Combine(Store.Dir, $"rotella-vera-{DateTime.Now:HHmmss}.csv");
            File.WriteAllLines(path, lines);
            Log.Write("rotella vera: tutto in " + path);
        }
        catch (Exception ex) { Log.Write("rotella vera: file non scritto: " + ex.Message); }
        var text = trace?.ToString() ?? "";
        Log.Write("rotella vera, traccia: " + (text.Length > 4000 ? text[..4000] + "…" : text));
        UI.WheelScrolling.StepLog = null;
        UI.WheelScrolling.StepClock = null;
        trace = null;
        viewer = null;
    }
}
#endif
