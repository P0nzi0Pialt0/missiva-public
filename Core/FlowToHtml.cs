using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfBlock = System.Windows.Documents.Block;
using WpfImage = System.Windows.Controls.Image;
using WpfList = System.Windows.Documents.List;

namespace Missiva;

/// <summary>Dal testo scritto nell'editor nativo (RichTextBox) all'HTML da spedire.</summary>
public static class FlowToHtml
{
    /// <summary>Il documento che si sta convertendo: i caratteri e i colori uguali ai suoi non si ripetono in ogni pezzo.</summary>
    [ThreadStatic] static FlowDocument? current;

    /// <summary>
    /// Il carattere di partenza dei messaggi (Impostazioni, Posta, Scrittura e risposta) come CSS: il contenitore di tutto il
    /// testo, e quello con cui HtmlToFlow riconosce il testo senza formato proprio.
    /// </summary>
    public static string BaseStyle()
    {
        var s = Store.Settings;
        return $"font-family:{CssFamily(s.ComposeFont)},Arial,sans-serif;font-size:{Number(s.ComposeFontSize)}pt" +
               (ParseColor(s.ComposeColor) is { } color ? ";color:" + CssColor(color) : "");
    }

    /// <param name="images">Se c'è, riceve le immagini del testo, che l'HTML mostra con cid: e che partono incorporate.</param>
    public static string Convert(FlowDocument document, List<InlineImage>? images = null)
    {
        var sb = new StringBuilder("<div style=\"").Append(BaseStyle()).Append("\">");
        var before = current;
        current = document;
        try
        {
            foreach (var block in document.Blocks) Block(block, sb, images);
        }
        finally
        {
            current = before;
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    static string Number(double value) => Math.Round(value, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Un colore scritto come #RRGGBB; null se vuoto o non valido.</summary>
    public static Color? ParseColor(string hex)
    {
        if (hex.Length == 0) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return null; }
    }

    /// <summary>Un nome di carattere in CSS, fra apici; solo lettere, cifre, spazi e trattini (un nome venuto da un RTF incollato non esce dalla proprietà).</summary>
    static string CssFamily(string family)
    {
        var names = family.Split(',').Select(n => new string(n.Trim().Trim('\'', '"').Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Trim()).Where(n => n.Length > 0);
        return string.Join(',', names.Select(n => $"'{n}'"));
    }

    /// <summary>
    /// Il formato proprio di un pezzo di testo (messo con la barra, incollato, di una firma): carattere, dimensione in punti,
    /// colore ed evidenziatore, solo dove è diverso da quello del documento.
    /// </summary>
    static string InlineCss(TextElement e)
    {
        var css = new List<string>();
        var doc = current;
        if (e.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily family && !family.Source.Equals(doc?.FontFamily.Source, StringComparison.OrdinalIgnoreCase)
            && CssFamily(family.Source) is { Length: > 0 } name)
            css.Add("font-family:" + name);
        if (e.ReadLocalValue(TextElement.FontSizeProperty) is double size && (doc == null || Math.Abs(size - doc.FontSize) > 0.1))
            css.Add("font-size:" + Number(Math.Round(size * 72 / 96 * 2) / 2) + "pt");
        if (e.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush fore && !(doc?.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush same && same.Color == fore.Color))
            css.Add("color:" + CssColor(fore.Color));
        if (e.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush { Color.A: > 0 } back) css.Add("background-color:" + CssColor(back.Color));
        return string.Join(';', css);
    }

    /// <summary>Allineamento (al centro, a destra, giustificato) e rientro di un paragrafo, come CSS.</summary>
    static string BlockCss(WpfBlock block)
    {
        var css = new StringBuilder("margin:0");
        if (block.ReadLocalValue(WpfBlock.MarginProperty) is Thickness m && m.Left > 0) css.Append(" 0 0 ").Append(Number(m.Left)).Append("px");
        if (block.ReadLocalValue(WpfBlock.TextAlignmentProperty) is TextAlignment align && align != TextAlignment.Left)
            css.Append(";text-align:").Append(align switch { TextAlignment.Center => "center", TextAlignment.Right => "right", _ => "justify" });
        return css.ToString();
    }

    static void Block(WpfBlock block, StringBuilder sb, List<InlineImage>? images)
    {
        switch (block)
        {
            case Paragraph p:
                sb.Append("<p style=\"").Append(BlockCss(p)).Append('"');
                var own = InlineCss(p);
                if (own.Length > 0) sb.Append("><span style=\"").Append(own).Append("\">");
                else sb.Append('>');
                foreach (var inline in p.Inlines) Inline(inline, sb, images);
                if (p.Inlines.Count == 0) sb.Append("<br>");
                if (own.Length > 0) sb.Append("</span>");
                sb.Append("</p>");
                break;
            case WpfList list:
                var tag = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin ? "ol" : "ul";
                sb.Append('<').Append(tag).Append('>');
                foreach (var item in list.ListItems)
                {
                    sb.Append("<li>");
                    foreach (var b in item.Blocks) Block(b, sb, images);
                    sb.Append("</li>");
                }
                sb.Append("</").Append(tag).Append('>');
                break;
            case Section section:
                foreach (var b in section.Blocks) Block(b, sb, images);
                break;
            case BlockUIContainer { Child: WpfImage image }:
                // Un'immagine incollata dal RichTextBox da solo (Incolla di WPF, testo RTF con immagini) sta in un blocco suo.
                sb.Append("<p style=\"margin:0\">");
                Image(image, sb, images);
                sb.Append("</p>");
                break;
            case Table table:
                // Una tabella incollata da Word, Excel o Outlook, o una firma fatta con una tabella (l'RTF diventa un Table):
                // righe e celle come nell'editor, con bordi, margini e fondo delle celle. Prima non partiva nulla, nemmeno
                // il testo e le immagini delle celle.
                sb.Append("<table style=\"border-collapse:collapse\">");
                foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                    {
                        sb.Append("<tr>");
                        foreach (var cell in row.Cells) Cell(cell, sb, images);
                        sb.Append("</tr>");
                    }
                sb.Append("</table>");
                break;
        }
    }

    static void Cell(TableCell cell, StringBuilder sb, List<InlineImage>? images)
    {
        sb.Append("<td");
        if (cell.ColumnSpan > 1) sb.Append(" colspan=\"").Append(cell.ColumnSpan).Append('"');
        if (cell.RowSpan > 1) sb.Append(" rowspan=\"").Append(cell.RowSpan).Append('"');
        sb.Append(" style=\"vertical-align:top");
        if (cell.Padding != default) sb.Append(";padding:").Append(Pixels(cell.Padding));
        if (cell.BorderThickness != default && cell.BorderBrush is SolidColorBrush border)
            sb.Append(";border-style:solid;border-width:").Append(Pixels(cell.BorderThickness)).Append(";border-color:").Append(CssColor(border.Color));
        if (cell.Background is SolidColorBrush { Color.A: > 0 } fill) sb.Append(";background-color:").Append(CssColor(fill.Color));
        sb.Append("\">");
        foreach (var b in cell.Blocks) Block(b, sb, images);
        if (cell.Blocks.Count == 0) sb.Append("<br>");
        sb.Append("</td>");
    }

    /// <summary>Un margine o un bordo di WPF (DIP) come in CSS: sopra, destra, sotto, sinistra.</summary>
    static string Pixels(Thickness t) =>
        string.Join(' ', new[] { t.Top, t.Right, t.Bottom, t.Left }.Select(v => Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture) + "px"));

    static string CssColor(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    static void Inline(Inline inline, StringBuilder sb, List<InlineImage>? images)
    {
        switch (inline)
        {
            case LineBreak:
                sb.Append("<br>");
                return;
            case InlineUIContainer { Child: WpfImage image }:
                Image(image, sb, images);
                return;
            case Hyperlink link:
                // Solo i link che Missiva lascia scrivere (LinkWindow, HtmlToFlow) e tel:. Da un RTF incollato, o di una firma
                // o di un modello, WPF fa un link con qualunque indirizzo (javascript:, file://server/...): di quelli resta
                // il testo (revisione del 13 ottobre 2026, K-05). Prima partivano com'erano.
                bool sent = link.NavigateUri is { IsAbsoluteUri: true, Scheme: "http" or "https" or "mailto" or "tel" };
                if (sent) sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(link.NavigateUri!.ToString())).Append("\">");
                foreach (var i in link.Inlines) Inline(i, sb, images);
                if (sent) sb.Append("</a>");
                return;
            case Span span:
                var closeSpan = Open(span, sb);
                foreach (var i in span.Inlines) Inline(i, sb, images);
                sb.Append(closeSpan);
                return;
            case Run run:
                var close = Open(run, sb);
                // Un a capo dentro un Run (testo incollato) resta una riga: nell'HTML sarebbe uno spazio.
                sb.Append(WebUtility.HtmlEncode(run.Text).Replace("  ", " &nbsp;").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "<br>"));
                sb.Append(close);
                return;
        }
    }

    /// <summary>L'immagine come la mostra l'editor: larga uguale, ma mai più del messaggio di chi la riceve.</summary>
    static void Image(WpfImage image, StringBuilder sb, List<InlineImage>? images)
    {
        if (EditorImages.Of(image) is not { } data) return;
        if (images != null && !images.Any(i => i.ContentId == data.ContentId)) images.Add(data);
        sb.Append("<img src=\"cid:").Append(WebUtility.HtmlEncode(data.ContentId)).Append('"');
        if (EditorImages.WidthOf(image) is double width) sb.Append(" width=\"").Append(Math.Round(width)).Append('"');
        sb.Append(" alt=\"").Append(WebUtility.HtmlEncode(data.Name)).Append("\" style=\"max-width:100%\">");
    }

    static string Open(TextElement e, StringBuilder sb)
    {
        var close = new StringBuilder();
        var css = InlineCss(e);
        if (css.Length > 0)
        {
            sb.Append("<span style=\"").Append(css).Append("\">");
            close.Insert(0, "</span>");
        }
        if (e.FontWeight >= FontWeights.SemiBold) { sb.Append("<b>"); close.Insert(0, "</b>"); }
        if (e.FontStyle == FontStyles.Italic) { sb.Append("<i>"); close.Insert(0, "</i>"); }
        if (e is Inline i && i.TextDecorations != null && i.TextDecorations.Any(d => d.Location == TextDecorationLocation.Underline))
        {
            sb.Append("<u>");
            close.Insert(0, "</u>");
        }
        return close.ToString();
    }
}

/// <summary>
/// Le immagini nel testo dell'editor: un Image di WPF con i suoi byte e il suo cid (InlineImage) nel Tag, così lo stesso
/// Image dà sempre lo stesso HTML (ComposeWindow.State) e i byte non si ricodificano a ogni conversione.
/// </summary>
public static class EditorImages
{
    /// <summary>Più larghe di così nel testo si rimpiccioliscono (circa la finestra di scrittura, e il messaggio di chi la riceve).</summary>
    public const double MaxWidth = 720;

    /// <summary>Tipi di file che nel corpo diventano immagini (trascinati sul testo); gli altri sono allegati.</summary>
    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".bmp"] = "image/bmp",
    };

    public static bool IsImageFile(string path) => Types.ContainsKey(Path.GetExtension(path));

    /// <summary>La fine dei cid delle immagini messe da Missiva: riprendendo una bozza tornano immagini del testo solo queste (HtmlToFlow).</summary>
    public const string ContentIdSuffix = "@missiva";

    public static string NewContentId() => $"{Guid.NewGuid():N}{ContentIdSuffix}";

    /// <summary>Un'immagine da un file (trascinata sul testo); null se il file non si legge come immagine.</summary>
    public static InlineImage? FromFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return Decode(bytes) != null ? new InlineImage(NewContentId(), Path.GetFileName(path), TypeOf(bytes) ?? Types[Path.GetExtension(path)], bytes) : null;
        }
        catch (Exception ex)
        {
            Log.Error($"immagine da {path}", ex);
            return null;
        }
    }

    /// <summary>Un'immagine dagli appunti (uno screenshot): PNG, come la incolla il nuovo Outlook.</summary>
    /// <param name="name">Il nome del file che parte con il messaggio; di base "immagine.png" nella lingua di Missiva.</param>
    public static InlineImage FromBitmap(BitmapSource source, string? name = null)
    {
        name ??= T("immagine.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new InlineImage(NewContentId(), name, "image/png", stream.ToArray());
    }

    /// <summary>
    /// L'Image da mettere nel testo, larga width (DIP, uguale nell'HTML), altrimenti come l'immagine fino a MaxWidth;
    /// null se i byte non sono un'immagine.
    /// </summary>
    public static WpfImage? Create(InlineImage data, double? width = null)
    {
        if (Decode(data.Content) is not { } bitmap) return null;
        return new WpfImage
        {
            Source = bitmap,
            Tag = data,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Width = Math.Min(width ?? bitmap.PixelWidth, MaxWidth),
            ToolTip = data.Name,
        };
    }

    /// <summary>I dati dell'immagine; per quelle messe da WPF (Incolla, RTF) i byte si fanno ora, in PNG, una volta sola.</summary>
    public static InlineImage? Of(WpfImage image)
    {
        if (image.Tag is InlineImage data) return data;
        if (image.Source is not BitmapSource source) return null;
        try
        {
            var made = FromBitmap(source);
            image.Tag = made;
            return made;
        }
        catch (Exception ex)
        {
            Log.Error("immagine del testo", ex);
            return null;
        }
    }

    public static double? WidthOf(WpfImage image) =>
        !double.IsNaN(image.Width) ? image.Width : image.ActualWidth > 0 ? image.ActualWidth : image.Source?.Width;

    /// <summary>Tutte le immagini del documento, dentro paragrafi, elenchi, sezioni e tabelle.</summary>
    public static IEnumerable<WpfImage> All(FlowDocument document) => All(document.Blocks);

    /// <summary>Tutte le immagini di quei blocchi (per esempio della firma), dentro paragrafi, elenchi, sezioni e tabelle.</summary>
    public static IEnumerable<WpfImage> All(IEnumerable<WpfBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case BlockUIContainer { Child: WpfImage image }: yield return image; break;
                case Paragraph p: foreach (var i in Inlines(p.Inlines)) yield return i; break;
                case Section s: foreach (var i in All(s.Blocks)) yield return i; break;
                case WpfList l: foreach (var i in All(l.ListItems.SelectMany(item => item.Blocks))) yield return i; break;
                case Table t: foreach (var i in All(t.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Blocks))) yield return i; break;
            }
        }

        static IEnumerable<WpfImage> Inlines(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                if (inline is InlineUIContainer { Child: WpfImage image }) yield return image;
                else if (inline is Span span) foreach (var i in Inlines(span.Inlines)) yield return i;
            }
        }
    }

    /// <summary>Il tipo dei byte, dalla loro firma: solo i tipi che Missiva mette nel testo (PNG, JPEG, GIF, BMP); null per gli altri.</summary>
    public static string? TypeOf(byte[] bytes) => bytes switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x42, 0x4D, ..] => "image/bmp",
        _ => null,
    };

    /// <summary>
    /// Il decodificatore di Windows del tipo dei byte (TypeOf), mai la scelta automatica di WIC, che proverebbe ogni codec
    /// installato (HEIF, RAW, WebP...) su byte che possono venire da chiunque; null se il tipo non è fra quelli di Missiva.
    /// </summary>
    static BitmapDecoder? Decoder(byte[] bytes)
    {
        var stream = new MemoryStream(bytes, writable: false);
        return TypeOf(bytes) switch
        {
            "image/png" => new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.None),
            "image/jpeg" => new JpegBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.None),
            "image/gif" => new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.None),
            "image/bmp" => new BmpBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.None),
            _ => null,
        };
    }

    /// <summary>La larghezza in pixel dell'immagine, senza decodificarla tutta; null se non è un'immagine di Missiva.</summary>
    public static int? PixelWidth(byte[] bytes)
    {
        try
        {
            return Decoder(bytes)?.Frames[0].PixelWidth;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>L'altezza massima, in pixel, di un'immagine del testo decodificata: una più alta si rimpicciolisce (audit, blocco 10, S-15).</summary>
    internal const int MaxDecodedHeight = 8192;

    /// <summary>I pixel massimi di un'immagine come arriva: oltre non si decodifica nemmeno rimpicciolita (una bomba di decompressione).</summary>
    internal const long MaxSourcePixels = 100_000_000;

    /// <summary>
    /// I byte come immagine per lo schermo: le foto grandi si decodificano già rimpicciolite (una da 4000 pixel sarebbero
    /// 48 MB di memoria), mentre partono i byte originali. Dall'audit, blocco 10 (S-15), non solo per larghezza: al massimo
    /// 2 x MaxWidth di larghezza e MaxDecodedHeight di altezza (un'immagine 1440 x 65535 arrivava intera, 377 MB), e
    /// null oltre MaxSourcePixels.
    /// </summary>
    internal static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            if (Decoder(bytes) is not { } decoder) return null;
            BitmapSource frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > MaxSourcePixels) return null;
            var scale = Math.Min(Math.Min(1.0, 2 * MaxWidth / frame.PixelWidth), (double)MaxDecodedHeight / frame.PixelHeight);
            if (scale < 1) frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            var bitmap = new CachedBitmap(frame, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
