using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Missiva.UI;

/// <summary>
/// Il mouse fermo sopra l'elenco dei messaggi non partecipa allo scorrimento veloce (Loop 4, agente cursore-I, 30 settembre 2026,
/// misure dell'autore). Con il cursore fermo sulle righe, mentre scorrono, a ogni layout WPF rivaluta chi sta sotto il mouse
/// (MouseDevice.Synchronize: MouseMove sintetico e hit test, HitTestInvalidatedAsyncCallback) e ogni riga che passa esegue
/// MouseEnter e MouseLeave (azioni rapide e casella come elementi veri, trigger del colore, suggerimento) e innesca disegni a
/// parte: scorri 120 da 9,3 ms a fotogramma con il cursore fuori a 10,1 con il cursore sopra, e con la lista fuori dal hit test
/// (MISSIVA_LISTA_INERTE) o con tutti gli effetti spenti torna a 9,3. Un interruttore solo non basta: ognuno lascia gli altri.
/// Qui, finché la lista si muove di almeno <see cref="FastPixels"/> pixel a fotogramma per un gesto (WheelScrolling.Step,
/// o "scorri N" della prova) il suo ItemsPresenter (un elemento solo, le righe nuove non si toccano) ha IsHitTestVisible falso:
/// nessun MouseEnter, MouseLeave, hover, Sync, suggerimento. Finito il movimento (<see cref="RestMs"/> senza un fotogramma veloce)
/// torna tutto e WPF rivaluta una volta chi sta sotto il mouse.
/// La rotella e il touchpad arrivano comunque a WheelScrolling: senza il presenter il hit test cade sulla cornice della lista
/// (o dietro), fuori dal ScrollViewer, e il suo gestore non sarebbe nel percorso dell'evento; per questo, mentre è sospeso,
/// un gestore sulla finestra gira l'evento a <see cref="WheelScrolling.Forward"/> se il mouse è dentro la lista.
/// Si torna subito con un movimento vero del mouse (posizione diversa da quella di quando è cominciata la sospensione), con un
/// pulsante del mouse o con un tasto. Un clic che arriva mentre la lista corre ancora (un fotogramma a più di 3 px negli ultimi
/// <see cref="SlowMs"/> ms) va alla cornice della lista e non alla riga: si perde, il successivo funziona. Un clic che arriva
/// dopo, nell'attesa prima della ripresa, va alla riga sotto il cursore (audit del 7 ottobre 2026, B-02: prima si perdeva
/// anche quello, e l'attesa, affidata a un timer a priorità Input che resta indietro finché WPF disegna a ogni fotogramma,
/// durava 61-153 ms invece di 60-85: misurato con tasti e clic veri, un clic 100 ms dopo l'ultima tacca andava perso).
/// Spenta con MISSIVA_SENZA_SOSPENSIONE=1 (confronti; nella prova anche "sonda senza-sospensione on").
/// </summary>
sealed class ScrollHitGuard
{
    const double FastPixels = 3;
    const double RestMs = 60;
    /// <summary>Senza un fotogramma veloce da tanti ms la lista non corre più: un pulsante del mouse va alla riga.</summary>
    const double SlowMs = 25;

    static readonly ConditionalWeakTable<ScrollViewer, ScrollHitGuard> Guards = new();

    /// <summary>Spenta (confronti e prove): la variabile d'ambiente vale solo nelle versioni di prova (audit dell'11 ottobre, T-29).</summary>
    internal static bool Disabled { get; set; }
#if DEBUG || PROVA
        = Environment.GetEnvironmentVariable("MISSIVA_SENZA_SOSPENSIONE") == "1";
#endif

    readonly ListBox list;
    readonly ScrollViewer viewer;
    DispatcherTimer? timer;
    Window? window;
    UIElement? presenter;
    Point suspendedAt;
    long lastFast;
    bool suspended;

    readonly MouseWheelEventHandler wheelHandler;
    readonly MouseButtonEventHandler buttonHandler;
    readonly MouseEventHandler moveHandler;
    readonly KeyEventHandler keyHandler;
    readonly ThreadMessageEventHandler messageHandler;

    ScrollHitGuard(ListBox list, ScrollViewer viewer)
    {
        this.list = list;
        this.viewer = viewer;
        wheelHandler = OnWindowWheel;
        buttonHandler = (_, _) => Resume();
        moveHandler = OnWindowMove;
        keyHandler = (_, _) => Resume();
        messageHandler = OnThreadMessage;
    }

    /// <summary>Da MessageList.Loaded: una guardia per elenco (più finestre principali: una ciascuna).</summary>
    public static void Attach(ListBox list, ScrollViewer? viewer)
    {
        if (viewer == null || Guards.TryGetValue(viewer, out _)) return;
        Guards.Add(viewer, new ScrollHitGuard(list, viewer));
    }

    /// <summary>L'elenco si è mosso di step pixel in questo fotogramma per un gesto (WheelScrolling.Step, "scorri N" della prova).</summary>
    public static void Moving(ScrollViewer viewer, double step)
    {
        if (Math.Abs(step) < FastPixels || !Guards.TryGetValue(viewer, out var guard)) return;
        guard.Fast();
    }

    void Fast()
    {
        if (Disabled)
        {
            if (suspended) Resume();
            return;
        }
        lastFast = Stopwatch.GetTimestamp();
        InputProbe.Mark(InputProbe.M.Fast);
        if (suspended) return;
        if (viewer.Content is not UIElement content || Window.GetWindow(viewer) is not { } owner) return;
        suspended = true;
        presenter = content;
        window = owner;
        suspendedAt = Mouse.GetPosition(owner);
        content.IsHitTestVisible = false;
        owner.AddHandler(UIElement.PreviewMouseWheelEvent, wheelHandler, false);
        owner.AddHandler(UIElement.PreviewMouseDownEvent, buttonHandler, true);
        owner.AddHandler(UIElement.PreviewMouseMoveEvent, moveHandler, true);
        owner.AddHandler(UIElement.PreviewKeyDownEvent, keyHandler, true);
        ComponentDispatcher.ThreadFilterMessage += messageHandler;
        if (timer == null)
        {
            // A priorità Normal, sopra il disegno: a Input, finché WPF disegna a ogni fotogramma (lo scorrimento che frena, il
            // cursore della barra), lo scatto del timer restava in coda fino a mezzo secondo.
            timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) =>
            {
                if (Stopwatch.GetElapsedTime(lastFast).TotalMilliseconds >= RestMs) Resume();
            };
        }
        timer.Start();
        CursorProbe.Hit(CursorProbe.C.Suspend);
        InputProbe.Mark(InputProbe.M.Suspend);
    }

    void Resume()
    {
        if (!suspended) return;
        suspended = false;
        timer?.Stop();
        if (window is { } owner)
        {
            owner.RemoveHandler(UIElement.PreviewMouseWheelEvent, wheelHandler);
            owner.RemoveHandler(UIElement.PreviewMouseDownEvent, buttonHandler);
            owner.RemoveHandler(UIElement.PreviewMouseMoveEvent, moveHandler);
            owner.RemoveHandler(UIElement.PreviewKeyDownEvent, keyHandler);
            window = null;
        }
        ComponentDispatcher.ThreadFilterMessage -= messageHandler;
        if (presenter != null)
        {
            presenter.IsHitTestVisible = true;
            presenter = null;
        }
        CursorProbe.Hit(CursorProbe.C.Resume);
        InputProbe.Mark(InputProbe.M.Resume);
    }

    void OnWindowWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || !suspended) return;
        var at = e.GetPosition(list);
        if (at.X < 0 || at.Y < 0 || at.X > list.ActualWidth || at.Y > list.ActualHeight) return;
        WheelScrolling.Forward(viewer, e);
    }

    /// <summary>
    /// Un pulsante del mouse premuto quando la lista non corre più ma la sospensione dura ancora: si riprende qui, prima che
    /// WPF tratti il messaggio (WM_LBUTTONDOWN, doppio clic, WM_RBUTTONDOWN, WM_MBUTTONDOWN), e si fa rivalutare a WPF chi sta
    /// sotto il cursore: il clic arriva alla riga. Nel gestore di PreviewMouseDown sarebbe tardi: il bersaglio è già scelto.
    /// </summary>
    void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message is not (0x201 or 0x203 or 0x204 or 0x207) || !suspended) return;
        if (Stopwatch.GetElapsedTime(lastFast).TotalMilliseconds < SlowMs) return;
        Resume();
        Mouse.Synchronize();
    }

    void OnWindowMove(object sender, MouseEventArgs e)
    {
        if (window == null) return;
        var at = e.GetPosition(window);
        if (Math.Abs(at.X - suspendedAt.X) > 0.5 || Math.Abs(at.Y - suspendedAt.Y) > 0.5) Resume();
    }

#if DEBUG
    /// <summary>Prove ("scorrimento stato" in MISSIVA_KEYS): questo elenco ha la sua guardia?</summary>
    internal static bool Attached(ScrollViewer viewer) => Guards.TryGetValue(viewer, out _);

    /// <summary>
    /// "sospensione prova" (MISSIVA_KEYS): senza mouse vero, dove cade il hit test della lista con il presenter fuori dal hit test
    /// (nella cornice della lista? nel ScrollViewer?) e se una rotella a quel punto arriva a WheelScrolling.
    /// </summary>
    internal static string Test(ListBox list)
    {
        ScrollViewer? viewer = null;
        ScrollViewer? Find(DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d) && viewer == null; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is ScrollViewer sv) viewer = sv;
                else Find(child);
            }
            return viewer;
        }
        if (Find(list) is not { } found || !Guards.TryGetValue(found, out var guard)) return "nessuna guardia sulla lista";
        var point = new Point(Math.Min(120, list.ActualWidth / 2), list.ActualHeight / 2);
        string Chain(IInputElement? hit)
        {
            if (hit is not DependencyObject d) return "nulla";
            var names = new List<string>();
            for (DependencyObject? p = d; p != null && names.Count < 8; p = p is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            {
                names.Add(p.GetType().Name);
                if (ReferenceEquals(p, list)) break;
            }
            return string.Join(">", names);
        }
        bool Inside(DependencyObject? ancestor, IInputElement? hit)
        {
            for (DependencyObject? p = hit as DependencyObject; p != null; p = p is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
                if (ReferenceEquals(p, ancestor)) return true;
            return false;
        }
        var before = list.InputHitTest(point);
        bool wasDisabled = Disabled;
        Disabled = false;
        guard.Fast();
        bool on = guard.suspended;
        var during = list.InputHitTest(point);
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        (during as UIElement ?? list).RaiseEvent(args);
        bool handled = args.Handled;
        guard.Resume();
        Disabled = wasDisabled;
        var after = list.InputHitTest(point);
        return $"presenter={guard.viewer.Content?.GetType().Name}; sospesa={on}; hit test prima: {Chain(before)}; durante: {Chain(during)} (nella lista {Inside(list, during)}, nel ScrollViewer {Inside(guard.viewer, during)}); " +
               $"rotella a quel punto arriva a WheelScrolling dal suo percorso: {handled}; dopo: {Chain(after)}; di nuovo attiva {guard.viewer.Content is UIElement { IsHitTestVisible: true }}";
    }
#endif
}
