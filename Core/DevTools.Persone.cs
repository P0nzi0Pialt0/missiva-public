#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Missiva.UI;

namespace Missiva;

/// <summary>I comandi di prova (MISSIVA_KEYS) per Persone: "persone stato" e "persone rotella N".</summary>
static partial class DevTools
{
    /// <summary>
    /// "persone stato": come scorre l'elenco (unità di scorrimento, righe create, altezza), nel registro.
    /// "persone rotella N": N tacche di rotellina verso il basso, una ogni 100 ms, sull'elenco delle persone, e il registro dice dove arriva
    /// a ogni fotogramma (pixel interi e non multipli dell'altezza di una riga: scorrimento a pixel, non per elementi).
    /// </summary>
    static bool PeopleCommand(MainWindow main, string combo)
    {
        if (!combo.StartsWith("persone ", StringComparison.OrdinalIgnoreCase)) return false;
        var view = FindChild<PeopleView>(main);
        if (view == null)
        {
            Log.Write("prova: Persone non è aperta");
            return true;
        }
        var list = view.TestList;
        var viewer = FindChild<ScrollViewer>(list);
        if (viewer == null) return true;
        var word = combo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "persone cerca mario r": il testo nella casella di ricerca di Persone, come scritto ("persone cerca" da solo la svuota);
        // "persone confronto vecchio" o "nuovo": il confronto di prima dell'audit, blocco 8 (ogni campo con la lingua di Windows), per i tempi.
        if (word[1].Equals("cerca", StringComparison.OrdinalIgnoreCase))
        {
            view.TestSearch(combo.Length > 14 ? combo[14..] : "");
            return true;
        }
        if (word[1].Equals("confronto", StringComparison.OrdinalIgnoreCase) && word.Length > 2)
        {
            Contact.TestOldMatch = word[2].Equals("vecchio", StringComparison.OrdinalIgnoreCase);
            Log.Write($"prova: Persone, confronto {(Contact.TestOldMatch ? "di prima (ogni campo con la lingua di Windows)" : "nuovo")}");
            return true;
        }
        if (word[1].Equals("stato", StringComparison.OrdinalIgnoreCase))
        {
            Log.Write(PeopleState(list, viewer, "persone"));
            return true;
        }
        if (word[1].Equals("rotella", StringComparison.OrdinalIgnoreCase) && word.Length > 2 && int.TryParse(word[2], out var notches))
        {
            var start = viewer.VerticalOffset;
            var offsets = new List<double>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int sent = 0;
            EventHandler? tick = null;
            tick = (_, _) =>
            {
                offsets.Add(viewer.VerticalOffset);
                while (sent < notches && clock.Elapsed.TotalMilliseconds >= sent * 100)
                {
                    FindChild<ListBoxItem>(list)?.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                    sent++;
                }
                if (clock.Elapsed < TimeSpan.FromMilliseconds(notches * 100 + 700)) return;
                System.Windows.Media.CompositionTarget.Rendering -= tick;
                var moved = viewer.VerticalOffset - start;
                var fractional = offsets.Count(o => Math.Abs(o - Math.Round(o)) > 0.01);
                Log.Write($"persone rotella: {notches} tacche, {moved:0.##} unità, {moved / Math.Max(notches, 1):0.##} a tacca, {offsets.Select(o => o - start).Distinct().Count()} posizioni diverse, " +
                          $"{fractional} non intere; {PeopleState(list, viewer, "dopo")}");
            };
            System.Windows.Media.CompositionTarget.Rendering += tick;
        }
        return true;
    }

    static string PeopleState(ListBox list, ScrollViewer viewer, string tag)
    {
        int made = 0;
        void Count(DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is ListBoxItem) made++;
                Count(child);
            }
        }
        Count(list);
        return $"{tag}: righe {list.Items.Count}, create {made}, unità {VirtualizingPanel.GetScrollUnit(list)}, virtualizza {VirtualizingPanel.GetIsVirtualizing(list)}, " +
               $"posizione {viewer.VerticalOffset:0.##} di {viewer.ScrollableHeight:0.##}, vista {viewer.ViewportHeight:0.##}, a contenuto {viewer.CanContentScroll}, intestazioni {list.Items.OfType<PeopleView.Header>().Count()}, scelta {list.SelectedIndex} ({list.SelectedItem})";
    }
}
#endif
