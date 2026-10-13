#if DEBUG
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Missiva;

/// <summary>Le prove del giro P5 (--prova-bozze): formato del testo, carattere predefinito, più firme.</summary>
static partial class ProvaBozze
{
    /// <summary>
    /// Formato del testo (giro P5), sul thread STA di Main: andata e ritorno FlowToHtml e HtmlToFlow con carattere,
    /// dimensione, colore, evidenziatore, centrato, rientro, tabella 3x2 e immagine da un file; il carattere predefinito delle
    /// impostazioni nel contenitore; le firme con un nome, la lunga per i nuovi e la breve per le risposte.
    /// </summary>
    static void FormatRoundTrip()
    {
        Report.Add("--- Formato del testo: FlowToHtml e HtmlToFlow andata e ritorno, carattere predefinito, più firme");
        var settings = Store.Settings;
        var (font, size, color) = (settings.ComposeFont, settings.ComposeFontSize, settings.ComposeColor);
        var file = Path.Combine(Path.GetTempPath(), $"missiva-prova-{Guid.NewGuid():N}.png");
        try
        {
            settings.ComposeFont = "Calibri";
            settings.ComposeFontSize = 12;
            settings.ComposeColor = "#003966";
            FlowDocument Document() => new() { FontFamily = new FontFamily("Calibri"), FontSize = 12 * 96 / 72.0, Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x39, 0x66)) };
            SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

            var doc = Document();
            var first = new Paragraph();
            first.Inlines.Add(new Run("Avviso ") { FontFamily = new FontFamily("Georgia"), FontSize = 16 * 96 / 72.0 });
            first.Inlines.Add(new Run("in rosso") { Foreground = Brush("#E81123") });
            first.Inlines.Add(new Run(" e un importo evidenziato") { Background = Brush("#FFF100") });
            doc.Blocks.Add(first);
            doc.Blocks.Add(new Paragraph(new Run("Testo al centro")) { TextAlignment = TextAlignment.Center });
            doc.Blocks.Add(new Paragraph(new Run("Testo rientrato")) { Margin = new Thickness(40, 0, 0, 0) });
            var table = UI.ComposeWindow.NewTable(3, 2);
            int n = 0;
            foreach (var cell in table.RowGroups[0].Rows.SelectMany(r => r.Cells)) ((Paragraph)cell.Blocks.FirstBlock).Inlines.Add(new Run($"cella {++n}"));
            doc.Blocks.Add(table);
            var bitmap = BitmapSource.Create(24, 12, 96, 96, PixelFormats.Bgra32, null, new byte[24 * 12 * 4].Select((_, i) => (byte)(i % 4 == 3 ? 255 : i % 7 * 30)).ToArray(), 24 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file)) encoder.Save(stream);
            var picture = EditorImages.FromFile(file)!;
            doc.Blocks.Add(new Paragraph(new InlineUIContainer(EditorImages.Create(picture, 24)!)));

            var images = new List<InlineImage>();
            var html = FlowToHtml.Convert(doc, images);
            Report.Add("      " + html[..Math.Min(html.Length, 420)]);
            Check(html.StartsWith("<div style=\"font-family:'Calibri',Arial,sans-serif;font-size:12pt;color:#003966\">")
                  && html.Contains("<span style=\"font-family:'Georgia';font-size:16pt\">Avviso </span>") && html.Contains("<span style=\"color:#E81123\">in rosso</span>")
                  && html.Contains("<span style=\"background-color:#FFF100\"> e un importo evidenziato</span>")
                  && html.Contains("<p style=\"margin:0;text-align:center\">Testo al centro</p>") && html.Contains("<p style=\"margin:0 0 0 40px\">Testo rientrato</p>")
                  && CountOf(html, "<tr>") == 2 && CountOf(html, "<td") == 6 && html.Contains("border-color:#A0A0A0") && images.Count == 1 && html.Contains($"cid:{picture.ContentId}"),
                  "FlowToHtml: carattere predefinito delle impostazioni nel contenitore, carattere e dimensione, colore, evidenziatore, centrato, rientro, tabella 3x2, immagine da un file");

            var blocks = HtmlToFlow.Blocks(html, cid => images.Where(i => i.ContentId == cid).Select(i => new Attachment { Id = "1", Name = i.Name, ContentType = i.ContentType, Content = i.Content }).FirstOrDefault());
            var back = Document();
            back.Blocks.AddRange(blocks);
            var again = FlowToHtml.Convert(back, new List<InlineImage>());
            var runs = ((Paragraph)back.Blocks.FirstBlock).Inlines.OfType<Run>().ToList();
            Check(again == html && runs[0].ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily { Source: "Georgia" } && back.Blocks.OfType<Table>().Single().RowGroups[0].Rows.Count == 2,
                  again == html ? "HtmlToFlow e di nuovo FlowToHtml: lo stesso HTML (bozze riprese e modelli tengono il formato, la tabella e l'immagine)"
                                : "HtmlToFlow e di nuovo FlowToHtml diversi: " + again[..Math.Min(again.Length, 600)]);
            // Aumenta rientro della barra (ComposeWindow.ShiftParagraphs): due volte e una indietro, per due paragrafi.
            var indentDoc = Document();
            var one = new Paragraph(new Run("uno"));
            var two = new Paragraph(new Run("due"));
            indentDoc.Blocks.Add(one);
            indentDoc.Blocks.Add(two);
            UI.ComposeWindow.ShiftParagraphs(one, two, UI.ComposeWindow.IndentStep);
            UI.ComposeWindow.ShiftParagraphs(one, two, UI.ComposeWindow.IndentStep);
            UI.ComposeWindow.ShiftParagraphs(two, two, -UI.ComposeWindow.IndentStep);
            var indentHtml = FlowToHtml.Convert(indentDoc);
            Check(indentHtml.Contains("<p style=\"margin:0 0 0 80px\">uno</p><p style=\"margin:0 0 0 40px\">due</p>"),
                  "Aumenta e riduci rientro della barra: margin-left dei paragrafi scelti, a passi di 40 pixel");
            // Il testo senza formato proprio segue il carattere predefinito: il contenitore di un messaggio non diventa formato del testo.
            var plain = HtmlToFlow.Blocks($"<div style=\"{FlowToHtml.BaseStyle()}\"><p style=\"margin:0\">Solo testo</p></div>");
            Check(((Paragraph)plain[0]).Inlines.FirstInline.ReadLocalValue(TextElement.FontFamilyProperty) == DependencyProperty.UnsetValue
                  && ((Paragraph)plain[0]).Inlines.FirstInline.ReadLocalValue(TextElement.ForegroundProperty) == DependencyProperty.UnsetValue,
                  "il testo scritto con il carattere e il colore predefiniti non riceve un formato proprio (resta com'è se le impostazioni cambiano)");

            // Più firme: la lunga per i nuovi messaggi, la breve per le risposte.
            var account = new MailAccount { Kind = AccountKind.Gmail, Email = "firme@studio-prova.example" };
            var shortOne = new NamedSignature { Name = "Breve", Rtf = @"{\rtf1 Mario}" };
            var signatures = new Settings { Signatures = { [account.Id] = new Signature { Rtf = @"{\rtf1 Mario Rossi, Studio Prova, via Roma 1}", More = [shortOne], ReplyId = shortOne.Id } } };
            var none = new Settings { Signatures = { [account.Id] = new Signature { Rtf = @"{\rtf1 Mario Rossi}", Replies = false } } };
            Check(signatures.SignatureFor(account, reply: false)!.RtfFor(false).Contains("via Roma") && signatures.SignatureFor(account, reply: true)!.RtfFor(true) == @"{\rtf1 Mario}"
                  && signatures.SignaturesOf(account, null)!.All().Count() == 2 && none.SignatureFor(account, reply: true) == null && none.SignatureFor(account, reply: false) != null,
                  "più firme per indirizzo: la lunga nei nuovi messaggi, la breve nelle risposte; \"Nessuna firma\" nelle risposte");
            var json = System.Text.Json.JsonSerializer.Serialize(signatures, SettingsJson.Default.Settings);
            var reread = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.Settings)!;
            Check(reread.Signatures[account.Id].More.Single().Name == "Breve" && reread.Signatures[account.Id].ReplyId == shortOne.Id,
                  "le firme con un nome e le predefinite rilette uguali dalle impostazioni");
        }
        finally
        {
            (settings.ComposeFont, settings.ComposeFontSize, settings.ComposeColor) = (font, size, color);
            try { File.Delete(file); } catch { }
        }
    }

    static int CountOf(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
#endif
