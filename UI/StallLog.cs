using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;

namespace Missiva;

/// <summary>
/// Registro permanente dei momenti in cui l'interfaccia non risponde (Loop 4, agente C, richiesta di A), anche nella versione
/// installata: per avere i dati veri del PC dell'autore invece di quelli della prova. Scrive nel registro solo due cose, con il
/// solo nome dell'operazione e i millisecondi (mai oggetti, mittenti o testi):
/// - "blocco": un'operazione del Dispatcher che dura più di 50 ms (priorità e metodo, per esempio "Input MainWindow.OnKeyDown");
/// - "fotogramma": durante un'animazione o uno scorrimento (almeno tre disegni di fila a meno di 33 ms uno dall'altro) due
///   disegni a più di 33 ms e a meno di 150 ms l'uno dall'altro, con il lavoro dell'interfaccia in mezzo e l'operazione più lunga.
/// Al massimo 10 righe al minuto: le altre si contano e si dicono nella riga dopo. Le operazioni che aprono un ciclo annidato
/// (finestre di dialogo, ShowDialog, MessageBox) non si contano, o durerebbero quanto la finestra aperta.
/// Costo: due letture dell'orologio e una scrittura in un vettore per operazione; a riposo il Dispatcher non ha operazioni
/// (nessun gestore di Rendering, che porterebbe WPF a disegnare a 60 Hz anche da fermo: per questo i fotogrammi si leggono
/// dalle operazioni di priorità Render).
/// </summary>
static class StallLog
{
    const double BlockMs = 50, FrameMs = 33, FrameLongestMs = 150;
    const int PerMinute = 10;

    static readonly double Tick = 1000.0 / Stopwatch.Frequency;
    static readonly FieldInfo? MethodField = typeof(DispatcherOperation).GetField("_method", BindingFlags.NonPublic | BindingFlags.Instance);

    struct Entry
    {
        public DispatcherOperation? Op;
        public Delegate? Method;
        public long Start;
        public bool Pumped;
    }

    static readonly Entry[] Stack = new Entry[32];
    static int depth;

    static long lastRender;
    static int fastRun;
    static double busy, longestMs;
    static string longestName = "";

    static long windowStart;
    static int written, suppressed;

    public static void Start(Dispatcher dispatcher)
    {
#if DEBUG || PROVA
        if (Environment.GetEnvironmentVariable("MISSIVA_SENZA_STALL") == "1") return; // per confrontare il consumo a riposo, solo nelle versioni di prova (T-29)
#endif
        var hooks = dispatcher.Hooks;
        hooks.OperationStarted += OnStarted;
        hooks.OperationCompleted += OnCompleted;
    }

    static void OnStarted(object? sender, DispatcherHookEventArgs e)
    {
        if (depth > 0) Stack[depth - 1].Pumped = true; // un'operazione dentro un'altra: l'esterna ha un ciclo annidato
        if (depth >= Stack.Length) return;
        Stack[depth] = new Entry { Op = e.Operation, Method = MethodOf(e.Operation), Start = Stopwatch.GetTimestamp() };
        depth++;
    }

    static Delegate? MethodOf(DispatcherOperation op)
    {
        try
        {
            return MethodField?.GetValue(op) as Delegate;
        }
        catch
        {
            return null;
        }
    }

    static void OnCompleted(object? sender, DispatcherHookEventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        int i = depth - 1;
        while (i >= 0 && !ReferenceEquals(Stack[i].Op, e.Operation)) i--;
        if (i < 0) return;
        var entry = Stack[i];
        for (int j = i; j < depth; j++) Stack[j] = default; // anche quelle finite senza avviso, se ce ne sono
        depth = i;

        double ms = (now - entry.Start) * Tick;
        var priority = e.Operation.Priority;
        if (!entry.Pumped)
        {
            busy += ms;
            if (ms > BlockMs)
            {
                string name = Describe(priority, entry.Method);
                if (ms > longestMs) { longestMs = ms; longestName = name; }
                if (Allow()) Log.Write($"interfaccia lenta: blocco di {ms:0} ms, {name}");
            }
            else if (ms > longestMs)
            {
                longestMs = ms;
                longestName = ms >= 8 ? Describe(priority, entry.Method) : ""; // il nome si compone solo per quelle che contano
            }
        }
        if (priority == DispatcherPriority.Render) OnFrame(now);
    }

    static void OnFrame(long now)
    {
        if (lastRender != 0)
        {
            double gap = (now - lastRender) * Tick;
            if (gap <= FrameMs) fastRun++;
            else
            {
                if (gap <= FrameLongestMs && fastRun >= 3 && Allow())
                {
                    Log.Write($"interfaccia lenta: fotogramma di {gap:0} ms, lavoro dell'interfaccia {busy:0} ms, " +
                        $"operazione più lunga {(longestName.Length == 0 ? "sotto 8 ms" : $"{longestName} {longestMs:0} ms")}");
                }
                fastRun = 0;
            }
        }
        lastRender = now;
        busy = 0;
        longestMs = 0;
        longestName = "";
    }

    static string Describe(DispatcherPriority priority, Delegate? method)
    {
        string name = "?";
        if (method != null)
        {
            var type = method.Method.DeclaringType;
            name = $"{type?.DeclaringType?.Name ?? type?.Name}.{method.Method.Name}";
        }
        return $"{priority} {name}";
    }

    static bool Allow()
    {
        long now = Stopwatch.GetTimestamp();
        if (windowStart == 0 || (now - windowStart) * Tick > 60_000)
        {
            if (suppressed > 0)
            {
                Log.Write($"interfaccia lenta: altre {suppressed} righe non scritte nel minuto prima (al massimo {PerMinute} al minuto)");
                suppressed = 0;
            }
            windowStart = now;
            written = 0;
        }
        if (written >= PerMinute)
        {
            suppressed++;
            return false;
        }
        written++;
        return true;
    }
}
