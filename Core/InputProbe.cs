using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Missiva;

/// <summary>
/// Quanto aspettano i tasti e i clic VERI, e le operazioni a priorità Input, mentre l'elenco scorre o subito dopo (audit del
/// 7 ottobre 2026, B-01..B-04). I comandi di MISSIVA_KEYS girano a priorità Input e misurano quella; qui si misura il percorso
/// vero: strumenti\ingresso.ps1 manda tasti e clic con SendInput alla finestra di prova e scrive l'istante dell'invio, e
/// questa classe scrive nel registro l'istante in cui arrivano (lo stesso orologio per i due processi:
/// Stopwatch.GetTimestamp, cioè QueryPerformanceCounter).
/// SOLO nella compilazione Debug e nella versione di prova con gli strumenti, e solo con MISSIVA_INGRESSO=1: nella versione
/// normale i segni non esistono (Conditional). Righe del registro:
/// - "ingresso: tasto ..." e "ingresso: clic ...": l'istante di PreviewKeyDown o PreviewMouseDown, quanto il messaggio è
///   rimasto nella coda di Windows (orologio dei messaggi, a scatti di qualche ms), che cosa ha preso il clic, se intanto
///   WPF disegnava a ogni fotogramma (scorrimento, cursore della barra) e se il hit test dell'elenco era sospeso;
/// - "ingresso: nel secondo dopo ...": i disegni, e le operazioni a priorità Input o inferiore con la loro attesa in coda;
/// - "coda del gesto: ...": dopo l'ultimo evento della rotella, quando finisce lo scorrimento, quando si ferma il cursore
///   della barra, quando riprende il hit test e per quanto WPF ha continuato a disegnare a ogni fotogramma;
/// - "disegni dopo NOME: ..." (Watch, dal comando "arrivo misurato" di MISSIVA_KEYS): quanti disegni in un intervallo e
///   quanto lavoro del thread dell'interfaccia dentro ognuno (il layout e il disegno di un'animazione).
/// Niente gestore di CompositionTarget.Rendering: farebbe disegnare WPF e cambierebbe ciò che si misura. I disegni si contano
/// dalle operazioni del Dispatcher (MediaContext.RenderMessageHandler).
/// </summary>
static class InputProbe
{
    public enum M { Wheel, GlideEnd, BarStart, BarEnd, Fast, Suspend, Resume }

    [Conditional("DEBUG")]
    public static void Mark(M what)
    {
#if DEBUG
        if (!on) return;
        long now = Stopwatch.GetTimestamp();
        switch (what)
        {
            case M.Wheel:
                if (!gesture)
                {
                    gesture = true;
                    wheelEvents = 0;
                    firstWheel = now;
                }
                wheelEvents++;
                lastWheel = now;
                glideEnd = barEnd = 0;
                if (!suspended) suspend = resume = 0;
                renders.Clear();
                lowered = 0;
                gestureWorst = 0;
                gliding = true;
                break;
            case M.GlideEnd: glideEnd = now; gliding = false; break;
            case M.BarStart:
                barMoving = true;
                barEnd = 0;
                barStart = now;
                barRenders = barLowered = 0;
                barWorst = 0;
                break;
            case M.BarEnd:
                barMoving = false;
                barEnd = now;
                // Il cursore che si muove senza un gesto della rotella: una pagina aggiunta in fondo, righe tolte, la barra trascinata.
                if (!gesture && Ms(barStart, now) > 100)
                    Log.Write($"cursore della barra: in movimento per {Ms(barStart, now):0} ms senza gesto, disegni {barRenders} (a priorità abbassata {barLowered}), " +
                              $"attesa massima di un'operazione a Input o sotto {barWorst:0} ms");
                break;
            case M.Fast: lastFast = now; break;
            case M.Suspend: suspended = true; suspend = now; resume = 0; break;
            case M.Resume: suspended = false; resume = now; resumeAfterFast = Ms(lastFast, now); break;
        }
#endif
    }

#if DEBUG
    static bool on, gesture, gliding, barMoving, suspended;
    static int wheelEvents, lowered;
    static long firstWheel, lastWheel, glideEnd, barEnd, lastFast, suspend, resume, lastInput, pulled;
    static int pulledTime;
    static double resumeAfterFast;
    static readonly List<long> renders = new();
    static readonly Dictionary<DispatcherOperation, long> posted = new();
    static readonly List<(string Name, double Wait, double At)> slowInput = new();
    static int inputOps, inputRenders, inputLowered, highOps, barRenders, barLowered;
    static long barStart;
    static double inputWorst, highWorst, barWorst, gestureWorst;
    static string inputWorstName = "", highWorstName = "";
    static readonly System.Reflection.FieldInfo? MethodField = typeof(DispatcherOperation).GetField("_method", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    static string watchName = "";
    static long watchStart, watchEnd, watchRenderStart;
    static int watchRenders, watchOver8;
    static double watchWork, watchWorst;
    static readonly List<double> watchFirst = new();

    /// <summary>Per ms millisecondi da adesso conta i disegni e il lavoro dentro ognuno; poi una riga "disegni dopo NOME".</summary>
    public static void Watch(string name, int ms)
    {
        if (!on) return;
        watchName = name;
        watchStart = Stopwatch.GetTimestamp();
        watchEnd = watchStart + ms * Stopwatch.Frequency / 1000;
        watchRenders = watchOver8 = 0;
        watchWork = watchWorst = 0;
        watchFirst.Clear();
    }

    public static void Start(Dispatcher dispatcher)
    {
        if (Environment.GetEnvironmentVariable("MISSIVA_INGRESSO") != "1") return;
        on = true;
        Log.Write($"ingresso: sonda accesa, orologio a {Stopwatch.Frequency} scatti al secondo");
        // I messaggi di tastiera e mouse appena usciti dalla coda di Windows, prima che WPF li trasformi in eventi.
        ComponentDispatcher.ThreadFilterMessage += (ref MSG msg, ref bool _) =>
        {
            if (msg.message is 0x100 or 0x104 or 0x201 or 0x204)
            {
                pulled = Stopwatch.GetTimestamp();
                pulledTime = msg.time;
            }
        };
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnKey), true);
        EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnButton), true);
        var hooks = dispatcher.Hooks;
        // OperationPosted arriva dal thread che mette in coda (anche uno del pool, dopo un await): il dizionario sotto lucchetto.
        hooks.OperationPosted += (_, e) => { lock (posted) posted[e.Operation] = Stopwatch.GetTimestamp(); };
        hooks.OperationAborted += (_, e) => { lock (posted) posted.Remove(e.Operation); };
        // Un DispatcherTimer mette in coda la sua operazione a priorità Inactive e la alza quando scade: l'attesa conta da lì.
        hooks.OperationPriorityChanged += (_, e) => { lock (posted) posted[e.Operation] = Stopwatch.GetTimestamp(); };
        hooks.OperationStarted += OnStarted;
        hooks.OperationCompleted += (_, e) =>
        {
            if (watchRenderStart == 0 || watchEnd == 0) return;
            if (MethodField?.GetValue(e.Operation) is not Delegate { Method: { DeclaringType.Name: "MediaContext" } m } || !m.Name.EndsWith("RenderMessageHandler", StringComparison.Ordinal)) return;
            double ms = Ms(watchRenderStart, Stopwatch.GetTimestamp());
            watchRenderStart = 0;
            watchRenders++;
            watchWork += ms;
            watchWorst = Math.Max(watchWorst, ms);
            if (ms > 8) watchOver8++;
            if (watchFirst.Count < 30) watchFirst.Add(ms);
        };
        // Un timer normale (non Rendering): scrive i riepiloghi quando il secondo dopo un ingresso o un gesto è passato.
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Flush();
        timer.Start();
    }

    static string State(long now) =>
        $"scorrimento in corso {(gliding ? "sì" : "no")}, cursore della barra in movimento {(barMoving ? "sì" : "no")}, hit test dell'elenco sospeso {(suspended ? "sì" : "no")}" +
        (lastWheel != 0 && Ms(lastWheel, now) < 5000 ? $", {Ms(lastWheel, now):0} ms dopo l'ultimo evento della rotella" : "");

    static void OnKey(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || e.Device is not KeyboardDevice || e.Timestamp == 0) return;
        long now = Stopwatch.GetTimestamp();
        if (Ms(pulled, now) > 50) return; // non viene da un messaggio di Windows: un tasto di MISSIVA_KEYS
        FlushInput(now);
        Log.Write($"ingresso: tasto {e.Key} qpc={now}, coda di Windows {unchecked(Environment.TickCount - pulledTime)} ms, dal messaggio a PreviewKeyDown {Ms(pulled, now):0.00} ms; {State(now)}");
        BeginInput(now);
    }

    static void OnButton(object sender, MouseButtonEventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        if (Ms(pulled, now) > 50) return; // un clic di MISSIVA_KEYS
        FlushInput(now);
        string hit = e.OriginalSource is DependencyObject d ? d.GetType().Name : "?";
        Log.Write($"ingresso: clic {e.ChangedButton} qpc={now}, coda di Windows {unchecked(Environment.TickCount - pulledTime)} ms, dal messaggio a PreviewMouseDown {Ms(pulled, now):0.00} ms, preso da {hit}; {State(now)}");
        BeginInput(now);
    }

    static void BeginInput(long now)
    {
        lastInput = now;
        slowInput.Clear();
        inputOps = inputRenders = inputLowered = highOps = 0;
        inputWorst = highWorst = 0;
        inputWorstName = highWorstName = "";
    }

    static void OnStarted(object? sender, DispatcherHookEventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        bool known;
        long at;
        lock (posted) known = posted.Remove(e.Operation, out at);
        var method = MethodField?.GetValue(e.Operation) as Delegate;
        // Con un gestore di Rendering o un'animazione il disegno è AnimatedRenderMessageHandler, altrimenti RenderMessageHandler.
        bool render = method?.Method is { DeclaringType.Name: "MediaContext" } m && m.Name.EndsWith("RenderMessageHandler", StringComparison.Ordinal);
        var priority = e.Operation.Priority;
        if (render)
        {
            if (watchEnd != 0 && now < watchEnd) watchRenderStart = now;
            if (gesture && renders.Count < 4000) renders.Add(now);
            if (barMoving) barRenders++;
            if (priority != DispatcherPriority.Render)
            {
                lowered++;
                if (barMoving) barLowered++;
                if (lastInput != 0) inputLowered++;
            }
            if (lastInput != 0) inputRenders++;
            return;
        }
        if (!known || priority == DispatcherPriority.Inactive) return;
        double wait = Ms(at, now);
        if (priority > DispatcherPriority.Input)
        {
            // Sopra il disegno (le riprese dopo un await di un tasto o di un clic vero sono a Send): non dovrebbero aspettare.
            if (lastInput == 0) return;
            highOps++;
            if (wait > highWorst) (highWorst, highWorstName) = (wait, $"{priority} {Name(method)}");
            return;
        }
        if (barMoving) barWorst = Math.Max(barWorst, wait);
        if (gesture) gestureWorst = Math.Max(gestureWorst, wait);
        if (lastInput == 0) return;
        inputOps++;
        if (wait > inputWorst || wait > 30)
        {
            string name = Name(method);
            if (wait > inputWorst) (inputWorst, inputWorstName) = (wait, $"{priority} {name}");
            if (wait > 30 && slowInput.Count < 12) slowInput.Add(($"{priority} {name}", wait, Ms(lastInput, now)));
        }
    }

    static string Name(Delegate? method) => method == null ? "?" : $"{method.Method.DeclaringType?.DeclaringType?.Name ?? method.Method.DeclaringType?.Name}.{method.Method.Name}";

    static void Flush()
    {
        long now = Stopwatch.GetTimestamp();
        FlushInput(now, 1000);
        if (watchEnd != 0 && now > watchEnd)
        {
            Log.Write($"disegni dopo {watchName}: {watchRenders} in {Ms(watchStart, watchEnd):0} ms, lavoro {watchWork:0} ms in tutto, massimo {watchWorst:0.0} ms, oltre 8 ms: {watchOver8}; " +
                      $"i primi: {string.Join(" ", watchFirst.Select(w => w.ToString("0.0")))}");
            watchEnd = 0;
        }
        if (gesture && Ms(lastWheel, now) > 2000 && !gliding)
        {
            gesture = false;
            // Per quanto WPF ha continuato a disegnare di fila dopo l'ultimo evento: fino al primo intervallo oltre 60 ms.
            int run = 0;
            long previous = lastWheel;
            foreach (long r in renders)
            {
                if (Ms(previous, r) > 60) break;
                previous = r;
                run++;
            }
            Log.Write($"coda del gesto: {wheelEvents} eventi in {Ms(firstWheel, lastWheel):0} ms; dopo l'ultimo evento: scorrimento finito a {At(glideEnd)}, " +
                      $"cursore della barra fermo a {(barMoving ? "ancora in movimento dopo 2 s" : At(barEnd))}, " +
                      $"hit test {(suspend == 0 ? "mai sospeso" : $"sospeso, ripreso a {At(resume)} ({resumeAfterFast:0} ms dopo l'ultimo fotogramma veloce)")}; " +
                      $"disegni di fila {run} fino a {(run == 0 ? "0" : Ms(lastWheel, previous).ToString("0"))} ms, in tutto {renders.Count} in 2 s, a priorità abbassata {lowered}; attesa massima di un'operazione a Input o sotto dall'ultimo evento {gestureWorst:0} ms");
        }
    }

    static string At(long t) => t == 0 ? "mai" : $"{Ms(lastWheel, t):0} ms";

    static void FlushInput(long now, double after = 0)
    {
        if (lastInput == 0 || Ms(lastInput, now) < after) return;
        Log.Write($"ingresso: nel{(after == 0 ? "l'intervallo" : " secondo")} dopo ({Ms(lastInput, now):0} ms): disegni {inputRenders} (a priorità abbassata {inputLowered}), " +
                  $"operazioni sopra il disegno {highOps}, attesa massima {highWorst:0.0} ms{(highWorstName.Length > 0 ? $" ({highWorstName})" : "")}; " +
                  $"operazioni a Input o sotto {inputOps}, attesa massima {inputWorst:0} ms{(inputWorstName.Length > 0 ? $" ({inputWorstName})" : "")}" +
                  (slowInput.Count > 0 ? "; oltre 30 ms: " + string.Join(", ", slowInput.Select(s => $"{s.Name} {s.Wait:0} ms a +{s.At:0}")) : ""));
        lastInput = 0;
    }
#endif
}
