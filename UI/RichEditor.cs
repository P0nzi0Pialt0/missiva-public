using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Editor di testo formattato di Missiva (corpo dei messaggi, firme): un RichTextBox che per UI Automation ha anche il
/// "valore", cioè il testo semplice, leggibile e sostituibile. Il RichTextBox di WPF offre solo TextPattern, che non
/// scrive: con ValuePattern Assistente vocale legge il contenuto e le prove della versione di prova scrivono una firma.
/// </summary>
public class RichEditor : RichTextBox
{
    public RichEditor()
    {
        // Lo stile Fluent del RichTextBox: quelli impliciti valgono per il tipo esatto, non per le sottoclassi.
        SetResourceReference(StyleProperty, typeof(RichTextBox));
        // Paragrafi senza spazio sopra e sotto, come nel nuovo Outlook e come li spedisce FlowToHtml (margin:0).
        Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph)) { Setters = { new Setter(Block.MarginProperty, new Thickness(0)) } });
        // Incolla e trascina, in ogni editor (scrittura, firme, modelli, risposte automatiche): mai lo XAML messo negli
        // appunti da un altro programma (PasteFormat). Quello che si copia da qui porta il segno di questa Missiva.
        DataObject.AddPastingHandler(this, OnPasting);
        DataObject.AddCopyingHandler(this, OnCopying);
    }

    /// <summary>Tutto il testo, senza la formattazione.</summary>
    public string PlainText => new TextRange(Document.ContentStart, Document.ContentEnd).Text.TrimEnd('\r', '\n');

    /// <summary>Sostituisce il contenuto con testo semplice, un paragrafo per riga.</summary>
    public void SetPlainText(string text)
    {
        Document.Blocks.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n')) Document.Blocks.Add(new Paragraph(new Run(line)));
    }

    /// <summary>Il contenuto in RTF, come si salva una firma; vuoto se non c'è testo.</summary>
    public string Rtf
    {
        get
        {
            if (PlainText.Trim().Length == 0) return "";
            using var stream = new MemoryStream();
            new TextRange(Document.ContentStart, Document.ContentEnd).Save(stream, DataFormats.Rtf);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        set
        {
            Document.Blocks.Clear();
            Document.Blocks.AddRange(BlocksFromRtf(value));
            if (Document.Blocks.Count == 0) Document.Blocks.Add(new Paragraph());
        }
    }

    /// <summary>I paragrafi di un testo RTF (una firma), da mettere in un documento.</summary>
    public static List<Block> BlocksFromRtf(string rtf)
    {
        if (rtf.Length == 0) return new();
        var scratch = new FlowDocument();
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
            new TextRange(scratch.ContentStart, scratch.ContentEnd).Load(stream, DataFormats.Rtf);
        }
        catch (Exception ex)
        {
            Log.Error("lettura della firma", ex);
            return new();
        }
        // Una firma o un modello arrivati con una copia di sicurezza: nessun carattere che WPF cercherebbe in rete.
        if (ClearFontLocations(scratch) is > 0 and var cleared) Log.Write($"testo RTF: {cleared} caratteri con un indirizzo al posto del nome, tolti");
        var blocks = scratch.Blocks.ToList();
        scratch.Blocks.Clear();
        foreach (var b in blocks) Neutral(b);
        return blocks;
    }

    /// <summary>
    /// Toglie dal documento i caratteri il cui nome è un indirizzo (HtmlToFlow.IsFontLocation) e dice quanti erano: WPF li
    /// cercherebbe a quell'indirizzo, anche in rete, appena disegna il testo. Va chiamato prima che il documento sia
    /// disegnato: caricare un RTF in un documento che nessun editor mostra non cerca nulla (audit dell'11 ottobre, T-31 e T-32).
    /// </summary>
    internal static int ClearFontLocations(FlowDocument document)
    {
        int cleared = 0;
        if (document.ReadLocalValue(FlowDocument.FontFamilyProperty) is FontFamily { Source: var whole } && HtmlToFlow.IsFontLocation(whole))
        {
            document.ClearValue(FlowDocument.FontFamilyProperty);
            cleared++;
        }
        for (var p = document.ContentStart; p != null; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.ElementStart || p.GetAdjacentElement(LogicalDirection.Forward) is not TextElement e) continue;
            if (e.ReadLocalValue(TextElement.FontFamilyProperty) is not FontFamily { Source: var source } || !HtmlToFlow.IsFontLocation(source)) continue;
            e.ClearValue(TextElement.FontFamilyProperty);
            cleared++;
        }
        return cleared;
    }

    /// <summary>
    /// Il segno su quello che questa Missiva copia (o trascina) dai suoi editor: un formato in più negli appunti, con un
    /// nome che vale solo per questo processo. Serve a PasteFormat per riconoscere il proprio XAML.
    /// </summary>
    internal static readonly string OwnCopy = "Missiva.Editor." + Guid.NewGuid().ToString("N");

    void OnCopying(object sender, DataObjectCopyingEventArgs e) => e.DataObject.SetData(OwnCopy, new MemoryStream([1]));

    /// <summary>
    /// Che cosa incollare (o lasciar cadere) in un editor di Missiva, fra i formati che gli appunti hanno; null se nulla.
    /// WPF sceglie per primo il suo XAML ("XamlPackage", "Xaml"), che si esegue leggendolo: un'immagine con un indirizzo si
    /// scarica subito, un Frame apre una pagina (misurato verso 127.0.0.1: audit dell'11 ottobre, T-32). Si incolla solo
    /// quello copiato da un editor di questa Missiva (OwnCopy), che tiene il formato com'è; quello messo negli appunti da
    /// un altro programma mai: resta l'RTF (Word), o il testo. E l'RTF con un carattere che è un indirizzo (T-31) si
    /// incolla come testo semplice.
    /// </summary>
    internal static string? PasteFormat(IDataObject data, string suggested)
    {
        if (data.GetDataPresent(OwnCopy, false)) return suggested;
        string? Text() => data.GetDataPresent(DataFormats.UnicodeText) ? DataFormats.UnicodeText : data.GetDataPresent(DataFormats.Text) ? DataFormats.Text : null;
        var format = suggested;
        if (format == DataFormats.Xaml || format == DataFormats.XamlPackage) format = data.GetDataPresent(DataFormats.Rtf) ? DataFormats.Rtf : Text();
        if (format == DataFormats.Rtf && (data.GetData(DataFormats.Rtf) is not string rtf || HasFontLocation(rtf))) format = Text();
        return format;
    }

    /// <summary>L'RTF porta un carattere il cui nome è un indirizzo? Si legge in un documento a parte, che nessuno disegna.</summary>
    internal static bool HasFontLocation(string rtf)
    {
        // Senza "#" nessun nome di carattere è un indirizzo (anche scritto \'23 o \u35): la gran parte degli RTF finisce qui.
        if (!rtf.Contains('#') && !rtf.Contains(@"\'23") && !rtf.Contains(@"\u35")) return false;
        var scratch = new FlowDocument();
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
            new TextRange(scratch.ContentStart, scratch.ContentEnd).Load(stream, DataFormats.Rtf);
        }
        catch (Exception)
        {
            return true; // un RTF che non si legge non si incolla come RTF
        }
        return ClearFontLocations(scratch) > 0;
    }

    void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        var format = PasteFormat(e.DataObject, e.FormatToApply);
        if (format == e.FormatToApply) return;
        Log.Write($"incolla: gli appunti offrivano {e.FormatToApply}, si incolla {format ?? "nulla"}");
        if (format == null) e.CancelCommand();
        else e.FormatToApply = format;
    }

    /// <summary>
    /// Toglie i colori e l'allineamento che l'RTF si porta dietro dal tema (testo bianco o nero scritto dal tema, fondo del
    /// tema, giustificato dallo stile Fluent): il testo senza colore proprio deve seguire il tema. Dal giro P5 restano i
    /// colori scelti con la barra (rosso, blu, evidenziatore) e l'allineamento al centro o a destra.
    /// </summary>
    static void Neutral(TextElement e)
    {
        static bool ThemeColor(Color c) => c.A < 255 || c.R < 0x30 && c.G < 0x30 && c.B < 0x30 || c.R > 0xE8 && c.G > 0xE8 && c.B > 0xE8;
        if (e.ReadLocalValue(TextElement.ForegroundProperty) is not SolidColorBrush fore || ThemeColor(fore.Color)) e.ClearValue(TextElement.ForegroundProperty);
        if (e.ReadLocalValue(TextElement.BackgroundProperty) is not SolidColorBrush back || ThemeColor(back.Color) || IsDarkTheme(back.Color)) e.ClearValue(TextElement.BackgroundProperty);
        if (e is Block block && block.ReadLocalValue(Block.TextAlignmentProperty) is not (TextAlignment.Center or TextAlignment.Right)) block.ClearValue(Block.TextAlignmentProperty);
        var children = e switch
        {
            Paragraph p => p.Inlines.Cast<TextElement>(),
            Span s => s.Inlines.Cast<TextElement>(),
            Section s => s.Blocks.Cast<TextElement>(),
            List l => l.ListItems.Cast<TextElement>(),
            ListItem i => i.Blocks.Cast<TextElement>(),
            _ => [],
        };
        foreach (var child in children.ToList()) Neutral(child);
    }

    /// <summary>I fondi grigi scuri del tema scuro (Fluent), che l'RTF può scrivere come fondo del testo.</summary>
    static bool IsDarkTheme(Color c) => c.R == c.G && c.G == c.B && c.R < 0x60;

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed class Peer(RichEditor owner) : RichTextBoxAutomationPeer(owner), IValueProvider
    {
        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

        public bool IsReadOnly => owner.IsReadOnly;
        public string Value => owner.PlainText;

        public void SetValue(string value)
        {
            if (!owner.IsEnabled) throw new ElementNotEnabledException();
            if (owner.IsReadOnly) throw new InvalidOperationException("sola lettura");
            owner.SetPlainText(value);
        }
    }
}
