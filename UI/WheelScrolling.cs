using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Missiva.UI;

/// <summary>
/// Scorrimento con rotellina e touchpad alla velocità del resto di Windows.
/// WPF di suo sposta di tre righe a ogni evento, qualunque sia la sua ampiezza (touchpad e rotelline ad alta risoluzione
/// ne mandano tanti piccoli), e negli elenchi conta le righe dell'elenco invece dei pixel.
/// Qui lo spostamento è proporzionale al movimento e segue l'impostazione di Windows "Righe da scorrere alla volta".
/// Ed è animato, come in Edge e nel nuovo Outlook: WPF saltava di colpo a ogni evento (60 pixel a tacca), che a 120 Hz
/// sembrava a scatti anche se WPF disegna a 115 fotogrammi al secondo (misurato con "scorri 8" in MISSIVA_KEYS, 28
/// settembre 2026). Ogni fotogramma (CompositionTarget.Rendering) mette l'elenco dove deve essere in quel momento.
/// Una tacca da ferma parte veloce e frena (FirstBrake); le tacche di fila tengono la velocità, come in Edge (prima ogni
/// tacca ripartiva da capo: fra una e l'altra l'elenco scendeva da 12 a 1 pixel a fotogramma e ripartiva, un dente di
/// sega a 10 Hz); i movimenti fini del touchpad muovono una molla smorzata criticamente (SpringTau): il touchpad vero manda
/// eventi piccolissimi (2-5 su 120) a raffiche, con buchi fino a 50 ms, e i passi per fotogramma che si distribuivano fra un
/// evento e l'altro (prima) facevano fermare l'elenco in un fotogramma su cinque e saltare nei flick (registrati a mano il 29
/// settembre 2026, WheelRecorder). Con gli effetti di animazione di Windows spenti, salta come prima.
/// </summary>
static class WheelScrolling
{
    /// <summary>Altezza di una "riga" di scorrimento, in unità indipendenti dal DPI.</summary>
    const double LineHeight = 20;

    /// <summary>
    /// Una tacca da ferma: veloce all'inizio e poi frenata fino a fermarsi in 150 ms (curva cubica, primo passo di circa 10
    /// pixel su 60 a 120 Hz, come prima con la costante di tempo di 40 ms).
    /// </summary>
    const double FirstBrake = 150;
    /// <summary>
    /// Tacche di fila (a meno di TrainGap dalla precedente): la velocità delle tacche (quanto una tacca ogni quanto) fino a
    /// quando dovrebbe arrivare la prossima, poi la frenata di TrainBrake: arrivando, la prossima trova l'elenco ancora in
    /// corsa e tiene la velocità.
    /// </summary>
    const double TrainGap = 140, TrainBrake = 60;
    /// <summary>
    /// Movimenti fini (touchpad, rotelline ad alta risoluzione: non multipli di 120): l'elenco insegue la meta con una molla
    /// smorzata criticamente, di costante di tempo SpringTau (ms); a velocità costante resta indietro di due costanti di
    /// tempo per la velocità (a 1 px/ms, 48 px). Misurata rifacendo nell'app i gesti veri: sbalzo di velocità fra fotogrammi
    /// 6-19% invece di 44-103%, fotogrammi fermi da 6-11 a 1-5 in un gesto di un secondo, passo massimo di un flick da
    /// 584-1271 a 313 px.
    /// </summary>
    internal static double SpringTau
    {
        get => Touch.TauSmall;
        set => Touch = new TouchModel($"molla {value:0} senza previsione", value, value, value, 0, 0);
    }

    /// <summary>
    /// Il modello dei movimenti fini (Giro F2 del Loop 4, "il touchpad non segue il dito"): la molla non insegue solo la meta
    /// (somma degli eventi) ma la meta più la velocità dell'ingresso (media degli ultimi Window ms) proseguita al massimo Hold
    /// ms dopo l'ultimo evento, così l'elenco non si ferma nei buchi fra le raffiche del touchpad (eventi ogni 7 ms con buchi
    /// fino a 50); la costante di tempo cresce con l'evento più grande degli ultimi ms: TauSmall per i gesti lenti (eventi sotto
    /// 60 px), TauBig da 200 px (lineare fra i due), TauHuge da 1600 px (lineare da 400: i flick mandano eventi da 3000 px e con
    /// una molla stretta il passo massimo arriva a 600-1000 px in un fotogramma). Window 0: senza previsione (la molla di prima).
    /// </summary>
    internal readonly record struct TouchModel(string Name, double TauSmall, double TauBig, double TauHuge, double Window, double Hold);

    /// <summary>
    /// I modelli provati alla cieca (WheelRecorder, MISSIVA_VARIANTI=1, Ctrl+Maiusc+1..4, in ordine casuale scritto nel registro);
    /// per fissare la scelta dell'autore basta mettere in Touch il modello scelto. Simulatore (gesti registrati, fotogrammi ideali;
    /// gesti lenti: fotogrammi fermi, ritardo dal dito; flick: passo massimo): 0 molla 14 senza previsione 11,6 fermi, 24 ms,
    /// 663 px; 1 previsione 40 ms e molla 14-24-50: 2,0 fermi, 19 ms, 285 px; 2 previsione 40 ms e molla 10-20-45: 3,4 fermi,
    /// 11 ms, 323 px; 3 previsione 40 ms e molla 18-28-60: 2,4 fermi, 27 ms, circa 250 px.
    /// </summary>
    internal static readonly TouchModel[] TouchModels =
    [
        new("molla 14 senza previsione", 14, 14, 14, 0, 0),
        new("previsione 40 ms, molla 14-24-50", 14, 24, 50, 40, 40),
        new("previsione 40 ms, molla 10-20-45", 10, 20, 45, 40, 40),
        new("previsione 40 ms, molla 18-28-60", 18, 28, 60, 40, 40),
    ];

    /// <summary>
    /// Il modello in uso: il 3 (previsione 40 ms, molla 18-28-60), scelto dall'autore con il test alla cieca del 30 settembre 2026:
    /// migliori A (molla 14 senza previsione, il modello 0) e C (il modello 3), peggiori B (il 1) e D (il 2, che era il valore
    /// fisso fino a quel giorno). Fra i due migliori il 3: nell'app (gesti veri rifatti con "replay", PresentMon) fermi 2,0 contro
    /// 6,0 per gesti lenti, sbalzo 26% contro 41%, flick passo massimo 175 contro 258 px, ritardo 26 contro 22 ms. Le costanti di
    /// tempo piccole (10 e 14 con la previsione) sono quelle che all'autore non piacciono.
    /// </summary>
    internal static TouchModel Touch = TouchModels[3];

    /// <summary>Orologio dei movimenti fini, in ms: gli eventi e i fotogrammi si confrontano con lo stesso.</summary>
    static double Now() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>Un fotogramma a 120 Hz: il primo passo di uno scorrimento, prima di sapere ogni quanto disegna lo schermo.</summary>
    const double FrameMs = 1000.0 / 120;

    sealed class Glide
    {
        /// <summary>Dove arriva l'elenco, e dove l'ha messo l'ultimo fotogramma.</summary>
        public double Target, LastSet;
        public TimeSpan Last;
        /// <summary>Ogni quanto arrivano i fotogrammi (ms), per partire nel fotogramma della tacca.</summary>
        public double Frame = FrameMs;
        // Il tratto delle tacche: da From, dal fotogramma Start, a Speed (pixel/ms, con il segno) per Cruise ms, poi la
        // frenata di Brake ms fino a Target. Pending: tacche arrivate, il tratto nuovo comincia al prossimo fotogramma.
        public double From, Speed, Cruise, Brake, Pending, NotchGap;
        public TimeSpan Start;
        public bool Moving;
        // I movimenti fini: la molla verso Target, con la sua velocità (pixel/ms, con il segno).
        public bool Fine;
        public double Velocity;
        /// <summary>Gli eventi fini recenti (ms dell'orologio di Now, spostamento della meta in pixel con il segno) e quando è arrivato l'ultimo.</summary>
        public readonly List<(double At, double Px)> Recent = new();
        public double LastEvent;
    }

    /// <summary>Gli scorrimenti in corso, uno per ScrollViewer; con nessuno, niente CompositionTarget.Rendering.</summary>
    static readonly Dictionary<ScrollViewer, Glide> gliding = new();
    /// <summary>
    /// Quando è arrivata l'ultima tacca, anche di uno scorrimento già finito: la frenata di una tacca sola finisce in circa
    /// 120 ms, e la seconda di una fila (ogni 100-130 ms) arrivava a elenco fermo e ripartiva da capo.
    /// </summary>
    static long lastNotch;

    public static void Enable()
    {
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
        // Elenchi in pixel invece che per righe: altrimenti ogni tacca salta di tre elementi interi.
        EventManager.RegisterClassHandler(typeof(ListBox), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => VirtualizingPanel.SetScrollUnit((ListBox)sender, ScrollUnit.Pixel)));
    }

    static double MsSince(long timestamp) => timestamp == 0 ? double.MaxValue : Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer viewer) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return; // Ctrl+rotellina e simili restano come sono
        // L'evento attraversa tutti gli scorrimenti annidati: lo gestisce il più interno che può ancora muoversi.
        if (Target(e.OriginalSource as DependencyObject, e.Delta) != viewer) return;
        Accept(viewer, e);
    }

    /// <summary>
    /// Dalla ScrollHitGuard: mentre l'elenco scorre veloce il suo presenter non partecipa al hit test e la rotella cade fuori dal
    /// ScrollViewer, il cui gestore non è nel percorso dell'evento; la guardia la gira qui, per l'elenco sotto il mouse.
    /// </summary>
    internal static void Forward(ScrollViewer viewer, MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        Accept(viewer, e);
    }

    static void Accept(ScrollViewer viewer, MouseWheelEventArgs e)
    {
#if DEBUG
        WheelSeen?.Invoke(viewer, e.Delta, e.Timestamp);
#endif
        InputProbe.Mark(InputProbe.M.Wheel);

        int lines = SystemParameters.WheelScrollLines; // -1 = "una schermata alla volta"
        double notches = e.Delta / 120.0;
        double amount = lines < 0 ? viewer.ViewportHeight * notches : notches * lines * LineHeight;
        e.Handled = true;
        if (!SystemParameters.ClientAreaAnimation)
        {
            viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset - amount, 0, viewer.ScrollableHeight));
            return;
        }
        // Dalla meta di prima, se sta ancora scorrendo: tacche di fila si sommano invece di ripartire da dove si è.
        bool starting = !gliding.TryGetValue(viewer, out var glide);
        if (glide == null)
        {
            glide = new Glide { Target = viewer.VerticalOffset, LastSet = viewer.VerticalOffset };
            if (gliding.Count == 0) CompositionTarget.Rendering += OnFrame;
            gliding[viewer] = glide;
        }
        double before = glide.Target;
        glide.Target = Math.Clamp(glide.Target - amount, 0, viewer.ScrollableHeight);
#if DEBUG
        StepLog?.Add($"{StepClock?.Elapsed.TotalMilliseconds:0.0} EVENTO {e.Delta} px {-amount:0} meta {before:0}->{glide.Target:0} posizione {viewer.VerticalOffset:0} altezza {viewer.ScrollableHeight:0}");
#endif
        if (e.Delta % 120 == 0)
        {
            glide.NotchGap = MsSince(lastNotch);
            lastNotch = Stopwatch.GetTimestamp();
            glide.Pending += glide.Target - before; // il tratto nuovo al prossimo fotogramma, da dove sarà l'elenco
            glide.Fine = false;
        }
        else
        {
            if (!glide.Fine)
            {
                // Dalle tacche ai movimenti fini (o da fermo): la molla parte ferma. Il resto lo fa Step a ogni fotogramma.
                glide.Fine = true;
                glide.Velocity = 0;
                glide.Moving = false;
                glide.Pending = 0;
                glide.Recent.Clear();
            }
            glide.LastEvent = Now();
            glide.Recent.Add((glide.LastEvent, glide.Target - before));
        }
#if DEBUG
        // Prove (DevTools.MeasureWheel): l'evento arriva dentro un fotogramma, e il primo passo è in quel fotogramma. Dal
        // mouse vero arriva fra due fotogrammi, e il primo passo è nel prossimo.
        if (starting && FrameTime is { } now) Step(viewer, glide, now);
#endif
    }

#if DEBUG
    /// <summary>Prove ("rotella 6" in MISSIVA_KEYS, DevTools.MeasureWheel): quando comincia e quanto dura il lavoro di ogni fotogramma.</summary>
    internal static Action? FrameStarting;
    internal static Action<double>? FrameWork;
    /// <summary>Prove (WheelRecorder, MISSIVA_RECORD=1): ogni evento della rotellina che arriva a un elenco.</summary>
    internal static Action<ScrollViewer, int, int>? WheelSeen;
    /// <summary>Prove (WheelRecorder): lo stato di ogni fotogramma di Step e di ogni evento, con l'orologio del registratore.</summary>
    internal static List<string>? StepLog;
    internal static System.Diagnostics.Stopwatch? StepClock;
    /// <summary>Prove: il fotogramma in cui DevTools manda gli eventi della rotellina.</summary>
    internal static TimeSpan? FrameTime;
#endif

    static void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
#if DEBUG
        FrameStarting?.Invoke();
        var busy = Stopwatch.StartNew();
#endif
        foreach (var (viewer, glide) in gliding.ToList())
        {
            if (glide.Last == now) continue; // lo stesso fotogramma annunciato due volte
            if (!viewer.IsVisible)
            {
                gliding.Remove(viewer);
                continue;
            }
            Step(viewer, glide, now);
        }
#if DEBUG
        FrameWork?.Invoke(busy.Elapsed.TotalMilliseconds);
#endif
        if (gliding.Count == 0)
        {
            CompositionTarget.Rendering -= OnFrame;
            InputProbe.Mark(InputProbe.M.GlideEnd);
        }
    }

    /// <summary>Un fotogramma: l'elenco dove deve essere adesso.</summary>
    static void Step(ScrollViewer viewer, Glide glide, TimeSpan now)
    {
        double current = viewer.VerticalOffset;
        // Spostato da altro fra un fotogramma e l'altro (tastiera, barra, elenco che si allunga): la meta lo segue.
        if (Math.Abs(current - glide.LastSet) > 1)
        {
            glide.Target += current - glide.LastSet;
            glide.From += current - glide.LastSet;
        }
        glide.Target = Math.Clamp(glide.Target, 0, viewer.ScrollableHeight);
        if (glide.Last != default) glide.Frame = Math.Clamp((now - glide.Last).TotalMilliseconds, 4, 17);
        // Al più un fotogramma a 60 Hz: il primo tempo dopo una pausa è vecchio, e un intoppo non diventa un salto.
        double dt = glide.Last == default ? glide.Frame : Math.Clamp((now - glide.Last).TotalMilliseconds, 1, 17);
        if (glide.Pending != 0) Retarget(glide, current, now);
        double next;
        bool done;
        if (glide.Fine)
        {
            // La molla smorzata criticamente, esatta per qualunque dt: errore e = posizione - meta, e'' = -2 w e' - w² e, cioè
            // e(t) = (e0 + (v0 + w e0) t) exp(-w t). Gli eventi cambiano la meta (e0) fra un fotogramma e l'altro.
            // Con il modello di Touch la molla insegue la meta più la velocità dell'ingresso proseguita fino a Hold ms.
            var m = Touch;
            double t = Now(), goal = glide.Target, tau = m.TauSmall;
            if (m.Window > 0)
            {
                glide.Recent.RemoveAll(r => r.At < t - Math.Max(m.Window, 100));
                double sum = 0, big = 0;
                foreach (var (at, px) in glide.Recent)
                {
                    if (at >= t - m.Window) sum += px;
                    big = Math.Max(big, Math.Abs(px));
                }
                tau += (m.TauBig - m.TauSmall) * Math.Clamp((big - 60) / 140, 0, 1);
                if (m.TauHuge > 0) tau += (m.TauHuge - tau) * Math.Clamp((big - 400) / 1200, 0, 1);
                goal = Math.Clamp(glide.Target + sum / m.Window * Math.Min(Math.Max(0, t - glide.LastEvent), m.Hold), 0, viewer.ScrollableHeight);
            }
            double w = 1 / tau, e0 = current - goal, v0 = glide.Velocity;
            double b = v0 + w * e0, decay = Math.Exp(-w * dt);
            double e1 = (e0 + b * dt) * decay;
            glide.Velocity = (v0 - w * b * dt) * decay;
            next = goal + e1;
            done = t - glide.LastEvent > m.Hold + 20 && Math.Abs(next - glide.Target) < 0.5 && Math.Abs(glide.Velocity) < 0.02;
        }
        else
        {
            double e = (now - glide.Start).TotalMilliseconds;
            done = !glide.Moving || e >= glide.Cruise + glide.Brake;
            next = done ? glide.Target : Position(glide, e);
        }
        if (done) next = glide.Target;
        glide.Last = now;
        viewer.ScrollToVerticalOffset(next);
        viewer.UpdateLayout(); // la disposizione di questo fotogramma, subito: VerticalOffset è quello vero
        if (Math.Abs(viewer.VerticalOffset - current) < 0.01 && !done && Math.Abs(next - current) >= 0.01)
        {
            // Fermo (un passo più piccolo di quanto l'elenco si sposta): l'ultimo tratto in una volta.
            viewer.ScrollToVerticalOffset(glide.Target);
            viewer.UpdateLayout();
            done = true;
        }
        ScrollHitGuard.Moving(viewer, viewer.VerticalOffset - current); // la lista si muove: il mouse fermo non partecipa finché scorre (ScrollHitGuard)
        glide.LastSet = viewer.VerticalOffset;
#if DEBUG
        StepLog?.Add($"{StepClock?.Elapsed.TotalMilliseconds:0.0} PASSO prima {current:0.0} voluto {next:0.0} ottenuto {viewer.VerticalOffset:0.0} meta {glide.Target:0} altezza {viewer.ScrollableHeight:0} fine {glide.Fine} velocità {glide.Velocity:0.000} dt {dt:0.0} finito {done}");
#endif
        if (done || !glide.Fine && Math.Abs(glide.Target - glide.LastSet) < 0.5) gliding.Remove(viewer);
    }

    /// <summary>Dove è il tratto delle tacche dopo e ms: velocità costante per Cruise, poi frenata cubica fino alla meta.</summary>
    static double Position(Glide g, double e)
    {
        if (e <= 0) return g.From;
        if (e < g.Cruise) return g.From + g.Speed * e;
        double u = Math.Min(1, (e - g.Cruise) / g.Brake);
        return g.From + g.Speed * g.Cruise + g.Speed * g.Brake / 3 * (1 - Math.Pow(1 - u, 3));
    }

    /// <summary>La velocità del tratto delle tacche dopo e ms (pixel/ms, con il segno).</summary>
    static double Velocity(Glide g, double e)
    {
        if (!g.Moving || e >= g.Cruise + g.Brake) return 0;
        if (e < g.Cruise) return g.Speed;
        double u = Math.Min(1, (e - g.Cruise) / g.Brake);
        return g.Speed * (1 - u) * (1 - u);
    }

    /// <summary>
    /// Tacche arrivate: un tratto nuovo da dove è l'elenco, che comincia un fotogramma fa (così si muove già in questo) con
    /// la velocità che aveva. Da ferma (o dopo una pausa) veloce e poi frenata; di fila, la velocità delle tacche fino alla
    /// prossima attesa, e se la corsa era già più veloce quella.
    /// </summary>
    static void Retarget(Glide g, double current, TimeSpan now)
    {
        double distance = g.Target - current, size = Math.Abs(distance), dir = Math.Sign(distance);
        var start = now - TimeSpan.FromMilliseconds(g.Frame);
        double running = g.Fine ? g.Velocity : Velocity(g, (start - g.Start).TotalMilliseconds);
        double speed = Math.Sign(running) == dir ? Math.Abs(running) : 0; // cambiando verso si riparte da fermi
        double cruise = 0, brake;
        if (size < 0.5)
        {
            g.Pending = 0;
            g.Moving = false;
            return;
        }
        if (g.NotchGap > TrainGap)
        {
            speed = Math.Max(speed, 3 * size / FirstBrake);
            brake = 3 * size / speed;
        }
        else
        {
            double notch = Math.Abs(g.Pending);
            speed = Math.Max(speed, Math.Min(notch / g.NotchGap, size / (g.NotchGap + TrainBrake / 3)));
            brake = TrainBrake;
            if (speed * brake / 3 > size) brake = 3 * size / speed;
            cruise = (size - speed * brake / 3) / speed;
        }
        g.From = current;
        g.Start = start;
        g.Speed = dir * speed;
        g.Cruise = cruise;
        g.Brake = brake;
        g.Moving = true;
        g.Pending = 0;
    }

    static ScrollViewer? Target(DependencyObject? d, int delta)
    {
        while (d != null)
        {
            if (d is ScrollViewer s && s.ScrollableHeight > 0 &&
                (delta > 0 ? s.VerticalOffset > 0 : s.VerticalOffset < s.ScrollableHeight - 0.5))
                return s;
            d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
