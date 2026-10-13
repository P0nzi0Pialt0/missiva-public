using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// I menu del tasto destro personalizzabili: quello dei messaggi e quello delle cartelle. Per ciascuno, le voci
/// possibili, l'ordine di base (quello del nuovo Outlook) e le scelte dell'utente, fatte da "Personalizza il menu…".
/// </summary>
public static class ContextMenus
{
    public const string Separator = "-";
    public const string ReplyAllGlyph = "reply-all", ForwardGlyph = "forward";

    public enum Tone { Plain, Purple, Blue, Green, Red }

    public sealed record Entry(string Key, string Name, string Glyph, Tone Tone);

    /// <summary>Un menu: le sue voci, l'ordine di base e dove stanno le scelte dell'utente nelle impostazioni.</summary>
    public sealed class Definition
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required Entry[] All { get; init; }
        public required string[] DefaultOrder { get; init; }
        public string[] DefaultHidden { get; init; } = [];
        public required Func<Settings, List<string>?> GetOrder { get; init; }
        public required Action<Settings, List<string>?> SetOrder { get; init; }
        public required Func<Settings, List<string>?> GetHidden { get; init; }
        public required Action<Settings, List<string>?> SetHidden { get; init; }

        public Entry? Find(string key) => All.FirstOrDefault(e => e.Key == key);

        /// <summary>
        /// L'ordine salvato. Le voci che una versione precedente di Missiva non aveva entrano dopo la voce che le
        /// precede nell'ordine di base, o in fondo.
        /// </summary>
        public List<string> Order()
        {
            var saved = GetOrder(Store.Settings);
            if (saved == null) return DefaultOrder.ToList();
            var order = saved.Where(k => k == Separator || Find(k) != null).ToList();
            foreach (var e in All.Where(e => !order.Contains(e.Key)))
            {
                int at = Array.IndexOf(DefaultOrder, e.Key);
                string? before = DefaultOrder.Take(Math.Max(at, 0)).LastOrDefault(k => k != Separator && order.Contains(k));
                order.Insert(before == null ? order.Count : order.IndexOf(before) + 1, e.Key);
            }
            return order;
        }

        public HashSet<string> Hidden() => (GetHidden(Store.Settings) ?? DefaultHidden.ToList()).ToHashSet();

        public void Save(IEnumerable<string> order, IEnumerable<string> hidden)
        {
            SetOrder(Store.Settings, order.ToList());
            SetHidden(Store.Settings, hidden.ToList());
            Store.Save();
        }

        public void Reset()
        {
            SetOrder(Store.Settings, null);
            SetHidden(Store.Settings, null);
            Store.Save();
        }
    }

    public static readonly Definition Messages = new()
    {
        Title = "Menu del tasto destro sui messaggi",
        Description = "Spunta le voci da mostrare quando premi il tasto destro su un messaggio. Trascinale, o usa Su e Giù, per cambiarne l'ordine. I separatori dividono il menu in gruppi.",
        All = new Entry[]
        {
            new("reply", "Rispondi", "", Tone.Purple),
            new("replyAll", "Rispondi a tutti", ReplyAllGlyph, Tone.Purple),
            new("forward", "Inoltra", ForwardGlyph, Tone.Blue),
            new("meeting", "Rispondi con riunione", "", Tone.Purple),
            new("replyTemplate", "Rispondi con modello", "", Tone.Purple),
            new("delete", "Elimina", "", Tone.Plain),
            new("archive", "Archivia", "", Tone.Green),
            new("move", "Sposta", "", Tone.Blue),
            new("copy", "Copia", "", Tone.Blue),
            new("read", "Segna come letto o da leggere", "", Tone.Plain),
            new("pin", "Fissa in alto", "", Tone.Blue),
            new("categorize", "Categorizza", "", Tone.Plain),
            new("flag", "Contrassegna", "", Tone.Red),
            new("snooze", "Posponi", "", Tone.Plain),
            new("rule", "Crea regola", "", Tone.Plain),
            new("report", "Segnala", "", Tone.Red),
            new("save", "Salva con nome", "", Tone.Plain),
            new("print", "Stampa", "", Tone.Plain),
            new("related", "Trova elementi correlati", "", Tone.Plain),
            new("view", "Visualizza", "", Tone.Plain),
            new("window", "Apri in una nuova finestra", "", Tone.Plain),
            new("source", "Origine del messaggio", "", Tone.Plain),
        },
        DefaultOrder = new[]
        {
            "reply", "replyAll", "forward", "meeting", "replyTemplate", Separator, "delete", "archive", "move", "copy", Separator, "read", "pin", "categorize",
            "flag", "snooze", Separator, "rule", "report", Separator, "save", "print", "related", "view", "window", "source",
        },
        // Voci in più rispetto a Outlook: ci sono, ma di base spente.
        DefaultHidden = new[] { "window", "source" },
        GetOrder = s => s.MessageMenuOrder, SetOrder = (s, v) => s.MessageMenuOrder = v,
        GetHidden = s => s.MessageMenuHidden, SetHidden = (s, v) => s.MessageMenuHidden = v,
    };

    public static readonly Definition Folders = new()
    {
        Title = "Menu del tasto destro sulle cartelle",
        Description = "Spunta le voci da mostrare quando premi il tasto destro su una cartella. Trascinale, o usa Su e Giù, per cambiarne l'ordine. I separatori dividono il menu in gruppi.",
        All = new Entry[]
        {
            new("count", "Numero di elementi e non letti", "", Tone.Plain),
            new("newFolder", "Crea nuova sottocartella", "", Tone.Plain),
            new("rename", "Rinomina", "", Tone.Plain),
            new("color", "Cambia colore", "", Tone.Plain),
            new("move", "Sposta", "", Tone.Plain),
            new("delete", "Elimina", "", Tone.Plain),
            new("empty", "Svuota", "", Tone.Plain),
            new("markRead", "Contrassegna tutti come letti", "", Tone.Plain),
            new("favorite", "Aggiungi a Preferiti o rimuovi", "", Tone.Plain),
            new("up", "Sposta su", "", Tone.Plain),
            new("down", "Sposta giù", "", Tone.Plain),
            new("window", "Apri in un'altra finestra", "", Tone.Plain),
            new("hide", "Nascondi dall'elenco", "", Tone.Plain),
            new("resetOrder", "Ripristina l'ordine predefinito", "", Tone.Plain),
        },
        DefaultOrder = new[]
        {
            "count", Separator, "newFolder", Separator, "rename", "color", "move", "delete", "empty", Separator, "markRead", Separator,
            "favorite", "up", "down", Separator, "window", Separator, "hide", "resetOrder",
        },
        GetOrder = s => s.FolderMenuOrder, SetOrder = (s, v) => s.FolderMenuOrder = v,
        GetHidden = s => s.FolderMenuHidden, SetHidden = (s, v) => s.FolderMenuHidden = v,
    };

    // ---------------------------------------------------------------- icone

    /// <summary>I colori delle icone di Outlook, più chiari nel tema scuro.</summary>
    public static Brush? Brush(Tone tone)
    {
        bool dark = ReadingView.Dark;
        int slot = (int)tone * 2 + (dark ? 1 : 0);
        if (slot < toneBrushes.Length && toneBrushes[slot] is { } cached) return cached; // ogni riga dell'elenco chiede "[Bozza]"
        string? hex = tone switch
        {
            Tone.Purple => dark ? "#C39EF7" : "#8764B8",
            Tone.Blue => dark ? "#62ABF5" : "#0F6CBD",
            Tone.Green => dark ? "#6CCB5F" : "#107C10",
            Tone.Red => dark ? "#FF8A8A" : "#C42B1C",
            _ => null,
        };
        if (hex == null) return null;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        if (slot < toneBrushes.Length) toneBrushes[slot] = brush;
        return brush;
    }

    /// <summary>I pennelli delle icone già fatti, uno per tono e tema (congelati: si condividono).</summary>
    static readonly Brush?[] toneBrushes = new Brush?[10];

    /// <summary>
    /// Icona Fluent. Rispondi a tutti e Inoltra come nel nuovo Outlook: due frecce di risposta sovrapposte, e la
    /// freccia di risposta specchiata (Segoe Fluent Icons non ha questi due disegni).
    /// </summary>
    public static FrameworkElement Icon(string glyph, Brush? color)
    {
        TextBlock Glyph(string text, double left = 0)
        {
            var t = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, Margin = new Thickness(left, 0, 0, 0) };
            if (color != null) t.Foreground = color;
            return t;
        }
        switch (glyph)
        {
            case ReplyAllGlyph:
                var both = new Grid { Width = 20 };
                var back = Glyph("", -3);
                back.Clip = new RectangleGeometry(new Rect(0, 0, 7, 20));
                both.Children.Add(back);
                both.Children.Add(Glyph("", 3));
                return both;
            case ForwardGlyph:
                var forward = Glyph("");
                forward.RenderTransformOrigin = new Point(0.5, 0.5);
                forward.RenderTransform = new ScaleTransform(-1, 1);
                return forward;
            default:
                return Glyph(glyph);
        }
    }
}
