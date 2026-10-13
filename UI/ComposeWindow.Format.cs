using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Formato del testo nella scrittura, come Formato testo del nuovo Outlook: carattere e dimensione, colore del testo ed
/// evidenziatore (colori fissi, che restano anche nel tema scuro), allineamento, rientro, tabella, immagine da un file e
/// Cancella formattazione; il carattere, la dimensione e il colore di partenza vengono da Impostazioni, Posta, Scrittura e
/// risposta. Tutto parte con FlowToHtml (font-family, font-size, color, background-color, text-align, margin-left). E il
/// pulsante Firma, per scegliere fra le firme dell'indirizzo del Da.
/// </summary>
public partial class ComposeWindow
{
    /// <summary>I caratteri proposti, quelli di Office e di Windows che tutti i programmi di posta hanno.</summary>
    internal static readonly string[] Fonts =
        ["Segoe UI", "Aptos", "Calibri", "Arial", "Verdana", "Tahoma", "Trebuchet MS", "Cambria", "Georgia", "Garamond", "Times New Roman", "Courier New"];

    internal static readonly double[] Sizes = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 24, 28, 36];

    /// <summary>I colori del testo, fissi come nella tavolozza di Outlook; il primo è automatico (quello del tema).</summary>
    internal static readonly (string Name, string Hex)[] TextColors =
    [
        ("Automatico", ""), ("Nero", "#000000"), ("Grigio scuro", "#595959"), ("Rosso scuro", "#A4262C"), ("Rosso", "#E81123"),
        ("Arancione", "#CA5010"), ("Oro", "#986F0B"), ("Verde", "#107C10"), ("Verde acqua", "#038387"), ("Blu", "#0F6CBD"),
        ("Blu scuro", "#003966"), ("Viola", "#8764B8"),
    ];

    internal static readonly (string Name, string Hex)[] HighlightColors =
    [
        ("Nessuno", ""), ("Giallo", "#FFF100"), ("Verde chiaro", "#A9F5A9"), ("Azzurro", "#9FD8FF"), ("Rosa", "#FFB3D9"),
        ("Arancione chiaro", "#FFD08A"), ("Grigio chiaro", "#D9D9D9"),
    ];

    bool formatSyncing;

    static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>Le caselle del carattere e il formato di partenza del documento, dalle impostazioni.</summary>
    void InitFormat()
    {
        var s = Store.Settings;
        FontBox.ItemsSource = Fonts.Contains(s.ComposeFont) ? Fonts : Fonts.Prepend(s.ComposeFont).ToArray();
        SizeBox.ItemsSource = Sizes.Select(v => v.ToString("0.#", CultureInfo.CurrentCulture)).ToList();
        Editor.Document.FontFamily = new FontFamily(s.ComposeFont);
        Editor.Document.FontSize = s.ComposeFontSize * 96 / 72;
        if (s.ComposeColor.Length > 0) Editor.Document.Foreground = Brush(s.ComposeColor);
        SyncFormat();
    }

    /// <summary>Carattere, dimensione e allineamento del testo scelto (o del cursore) nelle caselle e nei pulsanti.</summary>
    void SyncFormat()
    {
        formatSyncing = true;
        try
        {
            var selection = Editor.Selection;
            FontBox.SelectedItem = selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily family && FontBox.Items.Contains(family.Source) ? family.Source : null;
            if (FontBox.SelectedItem == null && selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily other) FontBox.Text = other.Source;
            SizeBox.SelectedItem = selection.GetPropertyValue(TextElement.FontSizeProperty) is double size
                ? (Math.Round(size * 72 / 96 * 2) / 2).ToString("0.#", CultureInfo.CurrentCulture) : null;
            var align = selection.GetPropertyValue(Block.TextAlignmentProperty) as TextAlignment?;
            AlignLeftButton.IsChecked = align is TextAlignment.Left or TextAlignment.Justify;
            AlignCenterButton.IsChecked = align == TextAlignment.Center;
            AlignRightButton.IsChecked = align == TextAlignment.Right;
        }
        finally
        {
            formatSyncing = false;
        }
    }

    /// <summary>Un formato applicato al testo scelto (o al testo che si scriverà al cursore), con un solo passo da annullare.</summary>
    void Apply(DependencyProperty property, object? value)
    {
        Editor.Selection.ApplyPropertyValue(property, value);
        Editor.Focus();
        SyncFormat();
    }

    void OnFontPick(object sender, SelectionChangedEventArgs e)
    {
        if (formatSyncing || FontBox.SelectedItem is not string font) return;
        Apply(TextElement.FontFamilyProperty, new FontFamily(font));
    }

    void OnSizePick(object sender, SelectionChangedEventArgs e)
    {
        if (formatSyncing || SizeBox.SelectedItem is not string text || !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var points)) return;
        Apply(TextElement.FontSizeProperty, points * 96 / 72);
    }

    /// <summary>Un menu di colori sotto il pulsante: un quadratino del colore e il nome, per UI Automation.</summary>
    void ColorMenu(Button anchor, (string Name, string Hex)[] colors, Action<(string Name, string Hex)> pick)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var color in colors)
        {
            var swatch = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
            swatch.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            if (color.Hex.Length > 0) swatch.Background = Brush(color.Hex);
            var item = new MenuItem { Header = color.Name, Icon = swatch };
            AutomationProperties.SetName(item, color.Name);
            item.Click += (_, _) => pick(color);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>Colore del testo: fisso, anche nel tema scuro; Automatico torna al colore del messaggio (quello del tema o delle impostazioni).</summary>
    void OnTextColor(object sender, RoutedEventArgs e) => ColorMenu(ColorButton, TextColors, color =>
    {
        if (color.Hex.Length == 0)
        {
            Apply(TextElement.ForegroundProperty, Store.Settings.ComposeColor.Length > 0 ? Brush(Store.Settings.ComposeColor) : null);
            return;
        }
        ColorMark.Background = Brush(color.Hex);
        Apply(TextElement.ForegroundProperty, Brush(color.Hex));
    });

    void OnHighlight(object sender, RoutedEventArgs e) => ColorMenu(HighlightButton, HighlightColors, color =>
    {
        if (color.Hex.Length > 0) HighlightMark.Background = Brush(color.Hex);
        Apply(TextElement.BackgroundProperty, color.Hex.Length > 0 ? Brush(color.Hex) : null);
    });

    void OnAlignLeft(object sender, RoutedEventArgs e) => Toggle(EditingCommands.AlignLeft);
    void OnAlignCenter(object sender, RoutedEventArgs e) => Toggle(EditingCommands.AlignCenter);
    void OnAlignRight(object sender, RoutedEventArgs e) => Toggle(EditingCommands.AlignRight);
    void OnIndent(object sender, RoutedEventArgs e) => Indent(+IndentStep);
    void OnOutdent(object sender, RoutedEventArgs e) => Indent(-IndentStep);

    /// <summary>Il passo del rientro, come in Outlook (circa un centimetro).</summary>
    internal const double IndentStep = 40;

    /// <summary>
    /// Aumenta e riduci rientro: il margine sinistro dei paragrafi scelti (FlowToHtml lo spedisce come margin-left). Negli
    /// elenchi il comando di WPF, che sposta la voce di livello.
    /// </summary>
    void Indent(double delta)
    {
        if (Editor.Selection.Start.Paragraph is not { } first) return;
        if (first.Parent is ListItem)
        {
            Toggle(delta > 0 ? EditingCommands.IncreaseIndentation : EditingCommands.DecreaseIndentation);
            return;
        }
        Editor.BeginChange();
        try
        {
            ShiftParagraphs(first, Editor.Selection.End.Paragraph ?? first, delta);
        }
        finally
        {
            Editor.EndChange();
        }
        Editor.Focus();
    }

    /// <summary>Il margine sinistro dei paragrafi da first a last (fratelli nello stesso contenitore), mai sotto zero.</summary>
    internal static void ShiftParagraphs(Paragraph first, Paragraph last, double delta)
    {
        for (Block? block = first; block != null; block = block.NextBlock)
        {
            if (block is Paragraph p)
            {
                var left = Math.Max(0, (double.IsNaN(p.Margin.Left) ? 0 : p.Margin.Left) + delta); // fuori dall'editor il margine è "automatico"
                p.Margin = new Thickness(left, p.Margin.Top, p.Margin.Right, p.Margin.Bottom);
            }
            if (block == last) break;
        }
    }

    /// <summary>Cancella formattazione, come nel nuovo Outlook: il testo scelto torna com'è il messaggio (carattere, colore, allineamento).</summary>
    void OnClearFormat(object sender, RoutedEventArgs e)
    {
        Editor.Selection.ClearAllProperties();
        Editor.Focus();
        OnEditorSelection(this, e);
    }

    /// <summary>Inserisci tabella: righe e colonne fra quelle più usate, come la griglia del nuovo Outlook.</summary>
    void OnTable(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = TableButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (columns, rows) in new[] { (2, 2), (3, 2), (3, 3), (4, 3), (2, 5), (4, 5) })
        {
            var item = new MenuItem { Header = $"{columns} colonne e {rows} righe" };
            AutomationProperties.SetName(item, $"Tabella di {columns} colonne e {rows} righe");
            item.Click += (_, _) => InsertTable(columns, rows);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>Una tabella vuota dopo il paragrafo del cursore, con i bordi grigi sottili che FlowToHtml spedisce.</summary>
    internal void InsertTable(int columns, int rows)
    {
        var table = NewTable(columns, rows);
        Editor.BeginChange();
        try
        {
            var paragraph = Editor.CaretPosition.Paragraph;
            var blocks = paragraph?.Parent is TableCell or ListItem ? null : paragraph?.SiblingBlocks;
            if (paragraph != null && blocks != null)
            {
                blocks.InsertAfter(paragraph, table);
                var after = new Paragraph();
                blocks.InsertAfter(table, after); // per continuare a scrivere sotto la tabella
            }
            else Editor.Document.Blocks.Add(table);
            Editor.CaretPosition = ((Paragraph)table.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock).ContentStart;
        }
        finally
        {
            Editor.EndChange();
        }
        Editor.Focus();
        Log.Write($"scrittura: tabella di {columns} colonne e {rows} righe");
    }

    /// <summary>La tabella nuova: celle con un paragrafo vuoto, bordo di un pixel grigio e un po' di margine.</summary>
    internal static Table NewTable(int columns, int rows)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 4) };
        for (int c = 0; c < columns; c++) table.Columns.Add(new TableColumn { Width = new GridLength(120) });
        var group = new TableRowGroup();
        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow();
            for (int c = 0; c < columns; c++)
                row.Cells.Add(new TableCell(new Paragraph()) { BorderBrush = Brush("#A0A0A0"), BorderThickness = new Thickness(1), Padding = new Thickness(4) });
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    /// <summary>Inserisci immagine da un file: nel testo, come un'immagine trascinata (EditorImages.Decode com'è).</summary>
    void OnPicture(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Immagini (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp", Title = "Inserisci immagine" };
        if (dialog.ShowDialog(this) != true) return;
        if (EditorImages.FromFile(dialog.FileName) is not { } image)
        {
            Dialog.Show(this, "Immagine non inserita", $"{Path.GetFileName(dialog.FileName)} non si riesce a leggere come immagine (PNG, JPEG, GIF o BMP).");
            return;
        }
        InsertImage(image, null, screen: true);
    }

#if DEBUG
    /// <summary>
    /// Prove (--formato-finto): il testo scritto e formattato come con la barra (carattere e dimensione, rosso, evidenziato,
    /// centrato, rientrato, una tabella 3x2 e un'immagine), prima della firma; con MISSIVA_DUMP la chiusura (anche con
    /// Scarta) scrive l'HTML che partirebbe.
    /// </summary>
    internal void TestFormat()
    {
        var doc = Editor.Document;
        var at = signatureLine ?? (Block?)signature;
        void Add(Block block)
        {
            if (at != null) doc.Blocks.InsertBefore(at, block);
            else doc.Blocks.Add(block);
        }
        var first = new Paragraph(new Run("Avviso importante: la fattura scade il 30 ottobre, "));
        var red = new Run("importo da saldare");
        var money = new Run(" 1.250,00 euro");
        first.Inlines.Add(red);
        first.Inlines.Add(money);
        Add(first);
        new TextRange(first.ContentStart, first.ContentStart.GetPositionAtOffset(18)!).ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily("Georgia"));
        new TextRange(first.ContentStart, first.ContentStart.GetPositionAtOffset(18)!).ApplyPropertyValue(TextElement.FontSizeProperty, 14 * 96 / 72.0);
        new TextRange(red.ContentStart, red.ContentEnd).ApplyPropertyValue(TextElement.ForegroundProperty, Brush("#E81123"));
        new TextRange(money.ContentStart, money.ContentEnd).ApplyPropertyValue(TextElement.BackgroundProperty, Brush("#FFF100"));
        var centered = new Paragraph(new Run("Riepilogo al centro")) { TextAlignment = TextAlignment.Center };
        Add(centered);
        var indented = new Paragraph(new Run("Nota rientrata"));
        Add(indented);
        Editor.CaretPosition = indented.ContentStart;
        Indent(+IndentStep); // come Aumenta rientro della barra
        var table = NewTable(3, 2);
        int n = 0;
        foreach (var cell in table.RowGroups[0].Rows.SelectMany(r => r.Cells)) ((Paragraph)cell.Blocks.FirstBlock).Inlines.Add(new Run(n++ < 3 ? $"Voce {n}" : $"{n * 100},00"));
        Add(table);
        var image = EditorImages.FromBitmap(System.Windows.Media.Imaging.BitmapSource.Create(48, 24, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Range(0, 48 * 24 * 4).Select(i => (byte)(i % 4 == 3 ? 255 : i % 4 == 2 ? 200 : 60)).ToArray(), 48 * 4), "logo.png");
        var withImage = new Paragraph();
        Add(withImage);
        withImage.Inlines.Add(new InlineUIContainer(EditorImages.Create(image, 48)!));
        SubjectBox.Text = "Prova del formato";
        Log.Write("prova: testo formattato inserito (--formato-finto)");
    }
#endif

    // ---------------------------------------------------------------- firma

    /// <summary>Firma, come nel nuovo Outlook: le firme dell'indirizzo del Da e "Nessuna firma", al posto di quella messa da Missiva.</summary>
    void OnSignatureMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SignatureButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var sender0 = FromBox.SelectedItem as Sender;
        var all = sender0 == null ? null : Store.Settings.SignaturesOf(sender0.Provider.Account, sender0.Identity);
        foreach (var signature in all?.All().Where(s => s.Rtf.Length > 0) ?? [])
        {
            var item = new MenuItem { Header = signature.Name };
            AutomationProperties.SetName(item, "Firma " + signature.Name);
            var rtf = signature.Rtf;
            item.Click += (_, _) => ReplaceSignature(rtf);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Nessuna firma per questo indirizzo (Impostazioni, Account, Firme)", IsEnabled = false });
        menu.Items.Add(new Separator());
        var none = new MenuItem { Header = "Nessuna firma" };
        AutomationProperties.SetName(none, "Nessuna firma");
        none.Click += (_, _) => ReplaceSignature("");
        menu.Items.Add(none);
        menu.IsOpen = true;
    }

    /// <summary>
    /// La firma scelta con il pulsante al posto di quella messa da Missiva (anche se toccata: la scelta è dell'utente).
    /// "Nessuna firma" resta scelta anche cambiando il Da (noSignature).
    /// </summary>
    void ReplaceSignature(string rtf)
    {
        if (signature != null && Editor.Document.Blocks.Contains(signature)) Editor.Document.Blocks.Remove(signature);
        if (signatureLine != null && Editor.Document.Blocks.Contains(signatureLine) && new TextRange(signatureLine.ContentStart, signatureLine.ContentEnd).Text.Length == 0)
            Editor.Document.Blocks.Remove(signatureLine);
        signature = null;
        signatureLine = null;
        var caret = Editor.CaretPosition;
        PutSignature(rtf);
        noSignature = signature == null;
        if (caret.IsInSameDocument(Editor.Document.ContentStart)) Editor.CaretPosition = caret;
        Log.Write($"scrittura: firma scelta ({(rtf.Length == 0 ? "nessuna" : "una delle firme dell'indirizzo")})");
    }
}
