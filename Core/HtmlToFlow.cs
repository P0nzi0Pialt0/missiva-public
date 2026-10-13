using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WpfBlock = System.Windows.Documents.Block;
using WpfList = System.Windows.Documents.List;

namespace Missiva;

/// <summary>
/// Dall'HTML di una bozza al documento dell'editor nativo (Modifica, nelle Bozze): paragrafi, a capo, grassetto,
/// corsivo, sottolineato, link, elenchi, immagini incorporate, e dal giro P5 carattere, dimensione, colore, evidenziatore,
/// allineamento, rientro e tabelle, cioè quello che FlowToHtml sa spedire (bozze riprese e modelli tengono il formato).
/// Gli stili si leggono come testo (proprietà CSS scritte nell'elemento), senza fogli di stile né nulla da eseguire; il
/// resto (immagini da Internet, stili di pagina) diventa testo semplice: l'editor di Missiva non è una pagina web.
/// </summary>
public static partial class HtmlToFlow
{
    /// <param name="image">Le immagini incorporate della bozza per cid (senza &lt;&gt;), con il contenuto; null se non ci sono.</param>
    public static List<WpfBlock> Blocks(string html, Func<string, Attachment?>? image = null)
    {
        var blocks = new List<WpfBlock>();
        // Sotto i tetti dell'analisi delle email (MailHtml.ParseGuard): una bozza porta anche l'HTML del messaggio a cui
        // risponde, che arriva da fuori, e senza tetti elementi di formato mai chiusi costavano secondi e gigabyte sul thread
        // dell'interfaccia. Oltre i tetti l'editor riceve il solo testo, un paragrafo per riga (audit, blocco 1).
        if (MailHtml.ParseWithinLimits(html) is not { } dom)
        {
            foreach (var line in PlainText(html)) blocks.Add(new Paragraph(new Run(line)));
            if (blocks.Count == 0) blocks.Add(new Paragraph());
            return blocks;
        }
        if (dom.Body is { } body) new Writer(blocks.Add, image).Children(body);
        if (blocks.Count == 0) blocks.Add(new Paragraph());
        return blocks;
    }

    /// <summary>
    /// Il testo di un HTML senza costruirne l'albero: i tag tolti con una passata (un a capo al posto di quelli che vanno a
    /// capo), le entità decodificate, le righe vuote tolte. Al più 5000 righe.
    /// </summary>
    static IEnumerable<string> PlainText(string html)
    {
        var text = new System.Text.StringBuilder(html.Length);
        for (int i = 0; i < html.Length; i++)
        {
            if (html[i] != '<')
            {
                text.Append(html[i] is '\r' or '\n' or '\t' ? ' ' : html[i]);
                continue;
            }
            int close = html.IndexOf('>', i + 1);
            if (close < 0) break;
            int from = html[i + 1] == '/' ? i + 2 : i + 1, to = from;
            while (to < close && char.IsLetterOrDigit(html[to])) to++;
            var tag = html[from..to];
            if (from == i + 1 && (tag.Equals("style", StringComparison.OrdinalIgnoreCase) || tag.Equals("script", StringComparison.OrdinalIgnoreCase)))
            {
                // Stili e script non sono testo: si salta fino alla loro chiusura.
                int end = html.IndexOf("</" + tag, close, StringComparison.OrdinalIgnoreCase);
                close = end >= 0 && html.IndexOf('>', end) is >= 0 and var after ? after : html.Length - 1;
            }
            else if (BlockTags.Contains(tag) || tag.Equals("br", StringComparison.OrdinalIgnoreCase) || tag.Equals("li", StringComparison.OrdinalIgnoreCase)) text.Append('\n');
            i = close;
        }
        return System.Net.WebUtility.HtmlDecode(text.ToString()).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(5000);
    }

    /// <summary>Gli elementi che vanno a capo prima e dopo, come nel browser.</summary>
    static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre", "table", "tr", "section", "article", "header",
        "footer", "hr", "address", "center", "dl", "dt", "dd", "figure", "main", "nav", "form", "fieldset",
    };

    /// <summary>Spazi da ridurre a uno: non lo spazio indivisibile, con cui FlowToHtml spedisce gli spazi doppi.</summary>
    [GeneratedRegex(@"[ \t\r\n\f]+")]
    private static partial Regex Spaces();

    /// <summary>Le proprietà dell'attributo style, nome in minuscolo e valore.</summary>
    static Dictionary<string, string> Css(IElement e)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (e.GetAttribute("style") ?? "").Split(';'))
        {
            int colon = part.IndexOf(':');
            if (colon > 0) result[part[..colon].Trim()] = part[(colon + 1)..].Trim();
        }
        return result;
    }

    /// <summary>Una lunghezza CSS in DIP (px, pt); null se non si legge.</summary>
    static double? Length(string value)
    {
        var m = LengthPattern().Match(value.Trim());
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return null;
        return m.Groups[2].Value.ToLowerInvariant() switch { "pt" => n * 96 / 72, "px" or "" => n, "em" => n * 16, _ => null };
    }

    [GeneratedRegex(@"^(-?\d+(?:\.\d+)?)\s*(px|pt|em)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LengthPattern();

    /// <summary>Un colore CSS (#RGB, #RRGGBB, rgb(), i nomi semplici); null se non si legge o è trasparente.</summary>
    static Color? CssColor(string value)
    {
        value = value.Trim();
        var rgb = RgbPattern().Match(value);
        if (rgb.Success)
            return Color.FromRgb(byte.Parse(rgb.Groups[1].Value, CultureInfo.InvariantCulture), byte.Parse(rgb.Groups[2].Value, CultureInfo.InvariantCulture), byte.Parse(rgb.Groups[3].Value, CultureInfo.InvariantCulture));
        if (value.Length == 0 || value.Equals("transparent", StringComparison.OrdinalIgnoreCase) || value.Contains('(')) return null;
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch (FormatException) { return null; }
    }

    [GeneratedRegex(@"^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*[\d.]+\s*)?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbPattern();

    /// <summary>
    /// Il nome di un carattere ha la forma di un indirizzo? Per WPF "indirizzo#nome" è un carattere da cercare in quella
    /// cartella, anche di rete: con \\server\cartella\#nome o file://server/cartella/#nome il disegno del testo apre una
    /// connessione verso quel server (misurato verso 127.x.y.z: audit dell'11 ottobre, T-31) e può sollevare un'eccezione.
    /// Senza "#" WPF cerca solo fra i caratteri di Windows; barre e due punti non stanno in nessun nome vero.
    /// </summary>
    public static bool IsFontLocation(string? name) => name != null && name.AsSpan().IndexOfAny("#/\\:") >= 0;

    /// <summary>
    /// Il nome di un carattere scritto da altri (lo stile di una bozza ripresa o di una risposta automatica, le impostazioni
    /// di una copia di sicurezza): solo un nome, come lo spedisce FlowToHtml (lettere, cifre, spazi e trattini, al più 64
    /// caratteri), mai un indirizzo (IsFontLocation). Null se non lo è.
    /// </summary>
    public static string? FontName(string? name)
    {
        if (name == null || name.Length > 200 || IsFontLocation(name)) return null;
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Trim();
        return clean.Length is > 0 and <= 64 ? clean : null;
    }

    static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    sealed class Writer(Action<WpfBlock> add, Func<string, Attachment?>? image)
    {
        Paragraph? paragraph;
        Hyperlink? link;
        bool bold, italic, underline, pre;
        string? family;
        double? size;
        Color? color, background;
        TextAlignment? align;
        double indent;

        /// <summary>Il testo che segue comincia un paragrafo nuovo.</summary>
        void Break() => paragraph = null;

        Paragraph Current()
        {
            if (paragraph == null)
            {
                paragraph = new Paragraph();
                if (align is { } a) paragraph.TextAlignment = a;
                if (indent > 0) paragraph.Margin = new Thickness(indent, 0, 0, 0);
                add(paragraph);
            }
            return paragraph;
        }

        void Append(Inline inline)
        {
            if (link != null) link.Inlines.Add(inline);
            else Current().Inlines.Add(inline);
        }

        void Text(string text)
        {
            if (pre)
            {
                // Dentro <pre> ogni a capo è una riga: nell'editor un LineBreak, che FlowToHtml spedisce come <br>. Un Run con
                // gli a capo finiva nell'HTML così com'era, dove diventano spazi (la citazione di Gmail in <pre> su una riga).
                var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (int k = 0; k < lines.Length; k++)
                {
                    if (k > 0) Append(new LineBreak());
                    if (lines[k].Length > 0) Append(Styled(lines[k]));
                }
                return;
            }
            text = Spaces().Replace(text, " ");
            // FirstInline e non Count: Count di WPF conta gli elementi uno a uno, e chiesto a ogni pezzo di testo faceva
            // crescere il tempo col quadrato delle righe di un paragrafo (4.000 a capo: 2,7 s; audit del 7 ottobre, B-11).
            bool empty = paragraph == null || paragraph.Inlines.FirstInline == null;
#if DEBUG
            if (TestOld == 1) empty = paragraph == null || paragraph.Inlines.Count == 0;
#endif
            if (empty || paragraph!.Inlines.LastInline is LineBreak) text = text.TrimStart(' ');
            if (text.Length == 0) return;
            Append(Styled(text));
        }

        Run Styled(string text)
        {
            var run = new Run(text);
            if (bold) run.FontWeight = FontWeights.Bold;
            if (italic) run.FontStyle = FontStyles.Italic;
            if (underline) run.TextDecorations = TextDecorations.Underline;
            if (family != null) run.FontFamily = new FontFamily(family);
            if (size is { } s) run.FontSize = s;
            if (color is { } c) run.Foreground = Brush(c);
            if (background is { } b) run.Background = Brush(b);
            return run;
        }

        /// <summary>Un Writer per il contenuto di un elemento (cella, voce di elenco) con il formato di adesso.</summary>
        Writer Inner(Action<WpfBlock> into) => new(into, image)
        {
            bold = bold, italic = italic, underline = underline, family = family, size = size, color = color, background = background,
        };

        /// <remarks>
        /// Qui si trova, una volta per elemento, l'ultimo figlio che conta (un elemento, o testo che non è solo spazi): serve
        /// agli a capo in fondo a un blocco. Prima ogni &lt;br&gt; lo chiedeva con NextSibling, che in AngleSharp cerca il nodo fra i
        /// fratelli dal primo: migliaia di a capo nello stesso elemento (testo incollato, una citazione senza un div per riga)
        /// costavano secondi sul thread dell'interfaccia (audit del 7 ottobre, B-11).
        /// </remarks>
        public void Children(INode parent)
        {
            var nodes = parent.ChildNodes;
            int last = -1;
            for (int i = nodes.Length - 1; i >= 0 && last < 0; i--)
                if (nodes[i] is IElement || nodes[i] is IText t && t.Data.Trim().Length > 0) last = i;
            bool closes = parent is IElement p && (BlockTags.Contains(p.LocalName) || p.LocalName is "body" or "li");
            for (int i = 0; i < nodes.Length; i++) Node(nodes[i], closes && i == last);
        }

        /// <summary>
        /// Il formato scritto nell'elemento: carattere, dimensione, colori, allineamento e rientro. Quelli uguali al formato
        /// di partenza dei messaggi (FlowToHtml.BaseStyle, il contenitore di ogni messaggio di Missiva) non diventano un
        /// formato proprio del testo.
        /// </summary>
        void Style(IElement e, bool block)
        {
            var css = Css(e);
            var s = Store.Settings;
            // Solo un nome di carattere, mai un indirizzo (FontName): arriva dall'HTML della bozza, che può averlo scritto chiunque.
            if (css.TryGetValue("font-family", out var f) && FontName(f.Split(',')[0].Trim().Trim('\'', '"')) is { } first)
                family = first.Equals(s.ComposeFont, StringComparison.OrdinalIgnoreCase) ? null : first;
            if (css.TryGetValue("font-size", out var fs) && Length(fs) is { } dip && dip is > 4 and < 200)
                size = Math.Abs(dip * 72 / 96 - s.ComposeFontSize) < 0.1 ? null : dip;
            if (css.TryGetValue("color", out var fc) && CssColor(fc) is { } fore)
                color = FlowToHtml.ParseColor(s.ComposeColor) == fore ? null : fore;
            if (css.TryGetValue("background-color", out var bc)) background = CssColor(bc);
            if (!block) return;
            if (css.TryGetValue("text-align", out var ta))
                align = ta.ToLowerInvariant() switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, "justify" => TextAlignment.Justify, _ => null };
            double? left = css.TryGetValue("margin-left", out var ml) ? Length(ml)
                : css.TryGetValue("margin", out var m) && m.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: 4 } four ? Length(four[3]) : null;
            if (left is { } l && l >= 0 && l < 400) indent = l;
        }

        /// <param name="closesBlock">Il nodo è l'ultimo del suo blocco (dopo ci sono solo spazi o commenti).</param>
        void Node(INode node, bool closesBlock)
        {
            if (node is IText t)
            {
                Text(t.Data);
                return;
            }
            if (node is not IElement e) return;
            var tag = e.LocalName;
            switch (tag)
            {
                case "style" or "script" or "head" or "title" or "template":
                    return;
                case "img":
                    Image(e);
                    return;
                case "br":
                    // Come nel browser: un a capo in fondo a un blocco non aggiunge una riga, tranne che in un blocco vuoto.
#if DEBUG
                    if (TestOld != 0) closesBlock = IsLastOld(e);
#endif
                    if (closesBlock) Current();
                    else Append(new LineBreak());
                    return;
                case "ul" or "ol":
                    Break();
                    var list = new WpfList { MarkerStyle = tag == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc };
                    add(list);
                    foreach (var li in e.Children.Where(c => c.LocalName == "li"))
                    {
                        var item = new ListItem();
                        list.ListItems.Add(item);
                        Inner(item.Blocks.Add).Children(li);
                        if (item.Blocks.Count == 0) item.Blocks.Add(new Paragraph());
                    }
                    Break();
                    return;
                case "table":
                    Break();
                    Table(e);
                    Break();
                    return;
            }

            bool block = BlockTags.Contains(tag);
            if (block) Break();
            var saved = (bold, italic, underline, pre, link, family, size, color, background, align, indent);
            if (tag is "b" or "strong" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" || e.GetAttribute("style") is { } style && BoldStyle().IsMatch(style)) bold = true;
            if (tag is "i" or "em") italic = true;
            if (tag == "u") underline = true;
            if (tag == "pre") pre = true;
            Style(e, block);
            if (tag == "a" && link == null && Uri.TryCreate(e.GetAttribute("href"), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                var h = new Hyperlink { NavigateUri = uri, ToolTip = uri.OriginalString };
                // Nell'editor il documento non è "attivo": lo stile Fluent disegnerebbe grigi i link (come in ComposeWindow.InsertLink).
                h.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
                Append(h);
                link = h;
            }
            Children(e);
            (bold, italic, underline, pre, link, family, size, color, background, align, indent) = saved;
            if (block) Break();
        }

        /// <summary>
        /// Una tabella (quelle che FlowToHtml spedisce: incollate, inserite con Inserisci tabella, o delle firme): righe e
        /// celle con colspan, rowspan, margine, bordo e fondo; il contenuto di ogni cella con lo stesso Writer.
        /// </summary>
        void Table(IElement e)
        {
            var table = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 4) };
            var group = new TableRowGroup();
            table.RowGroups.Add(group);
            var rows = new List<IElement>();
            foreach (var c in e.Children)
            {
                if (c.LocalName is "tbody" or "thead" or "tfoot") rows.AddRange(c.Children.Where(r => r.LocalName == "tr"));
                else if (c.LocalName == "tr") rows.Add(c);
            }
            int columns = 0;
            foreach (var tr in rows)
            {
                var row = new TableRow();
                foreach (var td in tr.Children.Where(c => c.LocalName is "td" or "th"))
                {
                    var cell = new TableCell();
                    if (int.TryParse(td.GetAttribute("colspan"), out var span) && span is > 1 and < 50) cell.ColumnSpan = span;
                    if (int.TryParse(td.GetAttribute("rowspan"), out var down) && down is > 1 and < 200) cell.RowSpan = down;
                    var css = Css(td);
                    if (css.TryGetValue("padding", out var padding) && Box(padding) is { } pad) cell.Padding = pad;
                    if (css.TryGetValue("border-width", out var bw) && Box(bw) is { } width && css.TryGetValue("border-color", out var bcolor) && CssColor(bcolor) is { } line)
                    {
                        cell.BorderThickness = width;
                        cell.BorderBrush = Brush(line);
                    }
                    if (css.TryGetValue("background-color", out var fill) && CssColor(fill) is { } back) cell.Background = Brush(back);
                    Inner(cell.Blocks.Add).Children(td);
                    if (cell.Blocks.Count == 0) cell.Blocks.Add(new Paragraph());
                    row.Cells.Add(cell);
                }
                columns = Math.Max(columns, row.Cells.Sum(c => c.ColumnSpan));
                if (row.Cells.Count > 0) group.Rows.Add(row);
            }
            if (group.Rows.Count == 0) return;
            for (int c = 0; c < columns; c++) table.Columns.Add(new TableColumn());
            add(table);
        }

        /// <summary>Margini o bordi CSS (uno, due o quattro valori) come Thickness di WPF.</summary>
        static Thickness? Box(string value)
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Length).ToList();
            if (parts.Count == 0 || parts.Any(p => p is null or < 0 or > 100)) return null;
            return parts.Count switch
            {
                1 => new Thickness(parts[0]!.Value),
                2 or 3 => new Thickness(parts[1]!.Value, parts[0]!.Value, parts[1]!.Value, parts[0]!.Value),
                _ => new Thickness(parts[3]!.Value, parts[0]!.Value, parts[1]!.Value, parts[2]!.Value),
            };
        }

        /// <summary>
        /// Un'immagine incorporata (cid:) di cui la bozza ha il contenuto torna un'immagine del testo, con il suo cid e la
        /// sua larghezza, così salvando di nuovo non si ricarica. Solo quelle messe da Missiva (cid che finisce con
        /// @missiva, EditorImages.NewContentId): quelle dell'email a cui si risponde, che createReply di Graph e la citazione
        /// di Gmail mettono nella bozza, le decodificherebbe Missiva stessa invece di WebView2 confinato. Quelle da Internet
        /// no: l'editor non scarica nulla.
        /// </summary>
        void Image(IElement e)
        {
            var src = e.GetAttribute("src") ?? "";
            if (image == null || !src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) return;
            var cid = src[4..].Trim('<', '>');
            // Il suffisso lo può scrivere anche il mittente (audit, blocco 10, S-15): non basta a fidarsi dell'immagine, e un cid
            // con un codice di controllo farebbe perdere le immagini delle bozze già salvate. La difesa è nel decodificatore:
            // solo i quattro tipi di Missiva, dalla firma dei byte, con un tetto su larghezza, altezza e pixel (EditorImages.Decode).
            if (!cid.EndsWith(EditorImages.ContentIdSuffix, StringComparison.OrdinalIgnoreCase)) return;
            if (image(cid) is not { Content: { } bytes } a || EditorImages.TypeOf(bytes) is not { } type) return;
            double? width = double.TryParse(e.GetAttribute("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0 ? w : null;
            if (EditorImages.Create(new InlineImage(cid, a.Name.Length > 0 ? a.Name : "immagine", type, bytes), width) is { } picture)
                Append(new InlineUIContainer(picture));
        }

#if DEBUG
        /// <summary>Com'era fino all'audit del 7 ottobre (B-11), per le prove che confrontano l'albero di prima e quello di ora.</summary>
        static bool IsLastOld(IElement e)
        {
            for (var next = e.NextSibling; next != null; next = next.NextSibling)
                if (next is IElement || next is IText t && t.Data.Trim().Length > 0) return false;
            return e.ParentElement is { } p && (BlockTags.Contains(p.LocalName) || p.LocalName is "body" or "li");
        }
#endif
    }

#if DEBUG
    /// <summary>
    /// Prove: 1 rifà i conti come prima dell'audit del 7 ottobre (B-11: l'ultimo del blocco con NextSibling e il paragrafo
    /// vuoto con Inlines.Count), 2 solo il primo dei due, per misurarli separati.
    /// </summary>
    internal static int TestOld;
#endif

    [GeneratedRegex(@"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase)]
    private static partial Regex BoldStyle();
}
