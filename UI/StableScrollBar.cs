using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Il cursore della barra di scorrimento dell'elenco dei messaggi senza scatti (richiesta dell'autore, 30 settembre 2026: "quando
/// faccio uno scorrimento veloce la barra di scorrimento ogni tanto ha degli scatti"). Il cursore sta a posizione/altezza
/// scorribile, e nell'elenco a pixel con righe e gruppi di altezze diverse WPF stima l'altezza delle parti non ancora realizzate
/// (misurato: da 1786 a 2717 px in 11 cambi durante il primo passaggio, cursore che salta fino a 114 px in un fotogramma;
/// ripassando su righe già misurate, nessuno scarto). La barra qui si guida da sola in una scala 0..1: il cursore si muove con lo
/// scorrimento vero (spostamento diviso l'altezza di adesso, senza ritardo), e quello che il cambio della stima aggiungerebbe di colpo
/// (la posizione vera meno quella mostrata) lo assorbe piano, con la costante Tau; la grandezza del cursore, che dipende solo
/// dall'altezza stimata, la segue con la stessa costante. Ai bordi (in cima e in fondo) il cursore ci arriva subito, e mentre lo si
/// trascina non si tocca. I comandi della barra (frecce, clic sulla pista, cursore) fanno quello che faceva ScrollViewer.
/// </summary>
sealed class StableScrollBar
{
    /// <summary>Costante di tempo (ms) con cui il cursore assorbe i cambi di stima.</summary>
    const double Tau = 200;

    readonly ScrollViewer viewer;
    readonly ScrollBar bar;
    double position, size = 0.5, lastOffset;
    double targetPosition, targetSize;
    TimeSpan last;
    bool animating, started, fresh;

    static readonly ConditionalWeakTable<ScrollViewer, StableScrollBar> Bars = new();

    /// <summary>Da chiamare a finestra caricata, quando il modello dello ScrollViewer è applicato.</summary>
    public static void Attach(ScrollViewer? viewer)
    {
        if (viewer == null) return;
        viewer.ApplyTemplate();
        if (viewer.Template?.FindName("PART_VerticalScrollBar", viewer) is not ScrollBar bar) return;
        if (Bars.TryGetValue(viewer, out _)) return;
        Bars.Add(viewer, new StableScrollBar(viewer, bar));
    }

    /// <summary>
    /// L'elenco è stato sostituito (un'altra cartella, un altro filtro) e riparte dall'alto: finché non lo si scorre, i cambi di
    /// altezza sono le righe che arrivano, non la stima che cambia, e il cursore va subito al suo posto. Prima a ogni cambio di
    /// cartella il cursore cambiava grandezza in 0,8-1,3 s, e per tutto quel tempo WPF disegnava a ogni fotogramma (90-158
    /// fotogrammi, misurati con MISSIVA_INGRESSO=1; audit del 7 ottobre 2026, B-01).
    /// </summary>
#if DEBUG
    /// <summary>Prove ("scorrimento stato" in MISSIVA_KEYS): questa barra è guidata da qui?</summary>
    internal static bool Attached(ScrollViewer viewer) => Bars.TryGetValue(viewer, out _);
#endif

    public static void Replaced(ScrollViewer? viewer)
    {
        if (viewer != null && Bars.TryGetValue(viewer, out var bar)) bar.fresh = true;
    }

    StableScrollBar(ScrollViewer viewer, ScrollBar bar)
    {
        this.viewer = viewer;
        this.bar = bar;
        bar.SetValue(RangeBase.MinimumProperty, 0.0); // un valore locale al posto dei TemplateBinding del modello
        bar.SetValue(RangeBase.MaximumProperty, 1.0);
        bar.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnScroll)); // prima di ScrollViewer: qui la scala è 0..1
        // Righe tolte o aggiunte senza scorrere (Elimina, Annulla, un messaggio arrivato), fino a una schermata: non è la stima
        // che cambia, e il cursore va subito al suo posto come in una barra qualsiasi.
        viewer.ScrollChanged += (_, e) => Update(still: e.VerticalChange == 0 && e.ExtentHeightChange != 0 && Math.Abs(e.ExtentHeightChange) <= viewer.ViewportHeight);
        Update();
    }

    bool Dragging => bar.Track?.Thumb is { IsDragging: true };

    /// <param name="still">
    /// L'elenco è cambiato di poche righe da fermo: niente assorbimento. Con Tau a 200 ms il cursore metteva 0,6 s a crescere
    /// di pochi pixel, e per tutto quel tempo WPF disegnava a ogni fotogramma (CompositionTarget.Rendering): finché lo fa,
    /// il lavoro a priorità Input (le riprese dopo un tasto) aspetta fino a mezzo secondo, e dopo Canc o Ctrl+Z il messaggio
    /// successivo compariva 0,35-0,8 s più tardi (audit, blocchi 4 e 9; visto con le operazioni del Dispatcher in fila).
    /// </param>
    void Update(bool still = false)
    {
        double sh = viewer.ScrollableHeight, view = viewer.ViewportHeight, extent = viewer.ExtentHeight, offset = viewer.VerticalOffset;
        targetPosition = sh <= 0 ? 0 : Math.Clamp(offset / sh, 0, 1);
        targetSize = extent <= 0 ? 0.98 : Math.Clamp(view / extent, 0.02, 0.98);
        if (fresh && offset > 0.5 && offset != lastOffset) fresh = false; // scorso: da qui l'altezza che cambia è di nuovo la stima
        if (!started || Dragging || fresh || (still && !animating))
        {
            started = true;
            (position, size) = (targetPosition, targetSize);
        }
        else
        {
            // Con lo scorrimento vero: il cursore si sposta di quanto si è spostato l'elenco sull'altezza di adesso.
            if (sh > 0) position = Math.Clamp(position + (offset - lastOffset) / sh, 0, 1);
            if (offset <= 0.5) position = 0;
            else if (offset >= sh - 0.5) position = 1;
        }
        lastOffset = offset;
        Show();
        if (!Converged() && !animating)
        {
            animating = true;
            last = default;
            CompositionTarget.Rendering += OnFrame;
            InputProbe.Mark(InputProbe.M.BarStart);
        }
    }

    bool Converged() => Math.Abs(targetPosition - position) < 0.0004 && Math.Abs(targetSize - size) < 0.0004;

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == last) return;
        double dt = last == default ? 8.3 : Math.Clamp((now - last).TotalMilliseconds, 1, 50);
        last = now;
        double a = 1 - Math.Exp(-dt / Tau);
        position += (targetPosition - position) * a;
        size += (targetSize - size) * a;
        if (Converged())
        {
            (position, size) = (targetPosition, targetSize);
            CompositionTarget.Rendering -= OnFrame;
            animating = false;
            InputProbe.Mark(InputProbe.M.BarEnd);
        }
        Show();
    }

    void Show()
    {
        if (Dragging) return;
        bar.ViewportSize = size / (1 - size); // la parte della pista che occupa il cursore: ViewportSize / (Maximum + ViewportSize)
        bar.Value = position;
    }

    /// <summary>I comandi della barra, com'erano in ScrollViewer (che qui non li vede: la scala è 0..1).</summary>
    void OnScroll(object sender, ScrollEventArgs e)
    {
        e.Handled = true;
        switch (e.ScrollEventType)
        {
            case ScrollEventType.SmallDecrement: viewer.LineUp(); break;
            case ScrollEventType.SmallIncrement: viewer.LineDown(); break;
            case ScrollEventType.LargeDecrement: viewer.PageUp(); break;
            case ScrollEventType.LargeIncrement: viewer.PageDown(); break;
            case ScrollEventType.First: viewer.ScrollToTop(); break;
            case ScrollEventType.Last: viewer.ScrollToBottom(); break;
            case ScrollEventType.ThumbPosition or ScrollEventType.ThumbTrack:
                viewer.ScrollToVerticalOffset(Math.Clamp(e.NewValue, 0, 1) * viewer.ScrollableHeight);
                break;
        }
        if (e.ScrollEventType is not (ScrollEventType.ThumbPosition or ScrollEventType.ThumbTrack)) Show();
    }
}
