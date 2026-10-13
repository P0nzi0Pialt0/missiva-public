using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Missiva.UI;

/// <summary>
/// L'anteprima di un allegato nel riquadro di lettura, come nel nuovo Outlook: Torna al messaggio, frecce fra gli allegati,
/// Scarica e Apri. PDF con il motore di Windows (Windows.Data.Pdf) nel processo dei PDF (PdfWorker, in un AppContainer
/// senza rete né file dell'utente): le pagine disegnate come immagini, niente JavaScript, moduli o link, niente WebView2;
/// solo quelle vicine a dove si guarda, con lo zoom. Immagini con i decodificatori di Windows nello stesso processo (tipo
/// letto dai byte, in uscita un PNG); testi (.txt, .csv, .xml, .json, .log) in una casella di sola lettura.
/// Tutto in memoria: nessun file in %TEMP% e nessun programma avviato, se non con Apri. I documenti Office restano con
/// Apri: i loro gestori di anteprima eseguirebbero codice di terzi.
/// </summary>
public sealed class AttachmentPreview : UserControl
{
    public sealed record Item(Attachment Attachment, Func<Task<byte[]>> Load);

    enum Kind { Pdf, Image, Text }

    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    const double PageGap = 12;
    /// <summary>Pagine tenute disegnate intorno a quella che si guarda: le altre tornano segnaposto (memoria).</summary>
    const int KeepAround = 4;

    List<Item> items = new();
    int index;
    int version;
    double zoom = 1;
    /// <summary>Pagine del PDF mostrato (0: nessun PDF).</summary>
    int pdfPages;
    Size pageSize;
    readonly List<Border> pageSlots = new();
    readonly Dictionary<int, Image> rendered = new();
    /// <summary>Pagine chieste al processo dei PDF e non ancora arrivate, ognuna con il suo annullamento.</summary>
    readonly Dictionary<int, CancellationTokenSource> rendering = new();

    readonly TextBlock title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 0) };
    readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24), MaxWidth = 520, TextAlignment = TextAlignment.Center };
    readonly ScrollViewer scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true };
    readonly StackPanel pages = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
    readonly Button previous, next, zoomIn, zoomOut;

    /// <summary>Torna al messaggio.</summary>
    public event Action? BackRequested;
    /// <summary>Scarica e Apri: li fa il riquadro, come per gli allegati senza anteprima.</summary>
    public Func<Attachment, Task>? SaveRequested { get; set; }
    public Func<Attachment, Task>? OpenRequested { get; set; }

    public AttachmentPreview()
    {
        var root = new DockPanel { Margin = new Thickness(12, 6, 12, 12) };
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var back = Command("", "Torna al messaggio");
        back.Click += (_, _) => BackRequested?.Invoke();
        DockPanel.SetDock(back, Dock.Left);
        bar.Children.Add(back);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        previous = Command("", "Allegato precedente");
        previous.Click += async (_, _) => await MoveAsync(-1);
        next = Command("", "Allegato successivo");
        next.Click += async (_, _) => await MoveAsync(1);
        zoomOut = Command("", "Riduci");
        zoomOut.Click += (_, _) => Zoom(zoom / 1.25);
        zoomIn = Command("", "Ingrandisci");
        zoomIn.Click += (_, _) => Zoom(zoom * 1.25);
        var save = Command("", "Scarica");
        save.Click += async (_, _) => { if (Current is { } a && SaveRequested != null) await SaveRequested(a); };
        var open = Command("", "Apri con il programma di Windows");
        open.Click += async (_, _) => { if (Current is { } a && OpenRequested != null) await OpenRequested(a); };
        foreach (var b in new[] { zoomOut, zoomIn, previous, next, save, open }) right.Children.Add(b);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(title);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        var area = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true };
        area.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorTertiaryBrush");
        var layers = new Grid();
        scroller.Content = pages;
        scroller.ScrollChanged += (_, _) => RenderVisible();
        layers.Children.Add(scroller);
        status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        layers.Children.Add(status);
        area.Child = layers;
        root.Children.Add(area);
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        Content = root;
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; BackRequested?.Invoke(); }
            else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) { e.Handled = true; await MoveAsync(-1); }
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt) { e.Handled = true; await MoveAsync(1); }
        };
        SizeChanged += (_, e) => { if (e.WidthChanged && pdfPages > 0) Layout(); };
    }

    Button Command(string glyph, string name)
    {
        var button = new Button { Content = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 15 }, ToolTip = name, Padding = new Thickness(10, 6, 10, 6),
                                  Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>Per le righe "tempi:" del registro: memoria privata di Missiva e heap di .NET.</summary>
    static string Memory()
    {
        using var self = Process.GetCurrentProcess();
        return $"Missiva {self.PrivateMemorySize64 / (1024 * 1024)} MB privati, heap .NET {GC.GetGCMemoryInfo().TotalCommittedBytes / (1024 * 1024)} MB";
    }

    Attachment? Current => index >= 0 && index < items.Count ? items[index].Attachment : null;

    /// <summary>Si può vedere qui: PDF, immagini (PNG, JPEG, GIF, BMP) e testi; gli altri (Office, archivi, eseguibili) no.</summary>
    public static bool CanPreview(Attachment a) => KindOf(a) != null;

    static Kind? KindOf(Attachment a)
    {
        var name = a.Name.ToLowerInvariant();
        var type = a.ContentType.ToLowerInvariant();
        if (name.EndsWith(".pdf") || type == "application/pdf") return Kind.Pdf;
        if (name.EndsWith(".png") || name.EndsWith(".jpg") || name.EndsWith(".jpeg") || name.EndsWith(".gif") || name.EndsWith(".bmp")
            || type is "image/png" or "image/jpeg" or "image/gif" or "image/bmp") return Kind.Image;
        if (name.EndsWith(".txt") || name.EndsWith(".csv") || name.EndsWith(".xml") || name.EndsWith(".json") || name.EndsWith(".log")
            || type is "text/plain" or "text/csv") return Kind.Text;
        return null;
    }

    /// <summary>Gli allegati da scorrere con le frecce e quello da mostrare.</summary>
    public async Task ShowAsync(List<Item> list, int at)
    {
        items = list;
        index = Math.Clamp(at, 0, Math.Max(0, list.Count - 1));
        await LoadAsync();
        scroller.Focus();
    }

    async Task MoveAsync(int step)
    {
        if (items.Count < 2) return;
        index = (index + step + items.Count) % items.Count;
        await LoadAsync();
    }

    /// <summary>
    /// Lascia la memoria: il PDF, le pagine disegnate, i byte. Le pagine sono bitmap di Windows (una pagina A4 larga quanto
    /// il riquadro al 175% sono 12 MB) che il GC libera solo quando passa: tornando al messaggio si chiede subito, altrimenti
    /// dieci anteprime lasciavano 150 MB in più.
    /// </summary>
    public void Clear()
    {
        version++;
        bool heavy = rendered.Count > 0 || pages.Children.OfType<Image>().Any();
        foreach (var image in rendered.Values) image.Source = null;
        foreach (var image in pages.Children.OfType<Image>()) image.Source = null;
        ResetPdf();
        pages.Children.Clear();
        items = new();
        if (heavy) FreeLater();
    }

    /// <summary>Il riquadro sta aprendo un messaggio (ReadingView): la raccolta aspetta che sia mostrato.</summary>
    public Func<bool>? Busy;

    static readonly TimeSpan FreeDelay = TimeSpan.FromSeconds(3);
    System.Windows.Threading.DispatcherTimer? freeTimer;
    int freeWaits;

    /// <summary>
    /// Una raccolta sola, tre secondi dopo la chiusura e non mentre il riquadro apre un messaggio (allora al giro dopo, al
    /// più cinque volte). Prima due raccolte complete partivano appena l'interfaccia era libera, cioè dentro l'apertura
    /// del messaggio su cui si era fatto clic: 46-50 ms in più, misurati (audit, v1 V-08 e v2 V-30). Le bitmap le liberano
    /// i finalizzatori: la seconda raccolta toglieva solo i loro involucri.
    /// </summary>
    void FreeLater()
    {
        freeWaits = 0;
        if (freeTimer == null)
        {
            var timer = freeTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.ApplicationIdle) { Interval = FreeDelay };
            timer.Tick += (_, _) =>
            {
                if (Busy?.Invoke() == true && ++freeWaits <= 5) return;
                timer.Stop();
                if (items.Count > 0) return; // un'altra anteprima è aperta: libera tutto la sua chiusura
                var clock = Stopwatch.StartNew();
                GC.Collect();
                GC.WaitForPendingFinalizers(); // le bitmap di Windows si liberano nei finalizzatori
                Log.Time($"anteprima chiusa, memoria liberata ({Memory()})", clock);
            };
        }
        freeTimer.Stop();
        freeTimer.Start();
    }

    void ResetPdf()
    {
        if (pdfPages > 0) PdfWorker.Shared.Forget();
        pdfPages = 0;
        pageSlots.Clear();
        rendered.Clear();
        CancelRendering(_ => true);
    }

    /// <summary>Le richieste di pagine che non servono più escono dalla coda del processo dei PDF (e non si aspettano).</summary>
    void CancelRendering(Func<int, bool> which)
    {
        foreach (var (i, cancel) in rendering.Where(r => which(r.Key)).ToList())
        {
            cancel.Cancel();
            rendering.Remove(i);
        }
    }

    async Task LoadAsync()
    {
        var v = ++version;
        foreach (var image in rendered.Values.Concat(pages.Children.OfType<Image>())) image.Source = null;
        ResetPdf();
        pages.Children.Clear();
        pages.HorizontalAlignment = HorizontalAlignment.Center;
        if (Current is not { } attachment) return;
        var kind = KindOf(attachment);
        title.Text = attachment.Name;
        previous.IsEnabled = next.IsEnabled = items.Count > 1;
        zoomIn.IsEnabled = zoomOut.IsEnabled = kind != Kind.Text;
        status.Text = "Caricamento…";
        status.Visibility = Visibility.Visible;
        var clock = Stopwatch.StartNew();
        byte[] bytes;
        try
        {
            bytes = await items[index].Load();
        }
        catch (Exception ex)
        {
            if (v != version) return;
            Log.Error("anteprima dell'allegato", ex);
            status.Text = "L'allegato non è disponibile: " + ex.Message;
            return;
        }
        if (v != version) return;
        Log.Time($"anteprima: allegato arrivato ({bytes.Length / 1024} KB; {Memory()})", clock);
        zoom = 1;
        switch (kind)
        {
            case Kind.Pdf: await ShowPdfAsync(bytes, v, clock); break;
            case Kind.Image: await ShowImageAsync(bytes, v, clock); break;
            default: ShowText(bytes, clock); break;
        }
    }

    // ---------------------------------------------------------------- PDF

    /// <summary>
    /// Il PDF con il motore di Windows: solo le pagine, come immagini. Si prepara un segnaposto per pagina (della misura
    /// della prima) e si disegnano quelle in vista: la prima di un PDF da 500 pagine arriva senza disegnare le altre.
    /// </summary>
    async Task ShowPdfAsync(byte[] bytes, int v, Stopwatch clock)
    {
        var drawing = Stopwatch.StartNew();
        try
        {
            var (count, width, height) = await PdfWorker.Shared.LoadAsync(bytes);
            if (v != version) return;
            pdfPages = count;
            pageSize = new Size(Math.Max(1, width), Math.Max(1, height));
            for (int i = 0; i < count; i++)
            {
                var slot = new Border { Margin = new Thickness(0, 0, 0, PageGap), Background = Brushes.White };
                AutomationProperties.SetName(slot, $"Pagina {i + 1} di {count}");
                pageSlots.Add(slot);
                pages.Children.Add(slot);
            }
            status.Visibility = Visibility.Collapsed;
            Layout(render: false);
            await RenderAsync(0, v); // la prima pagina subito, prima di quelle in vista
            if (v == version) Log.Time($"anteprima: prima pagina del PDF ({count} pagine, {bytes.Length / 1024} KB, disegnata in {drawing.ElapsedMilliseconds} ms; {Memory()})", clock);
            RenderVisible();
        }
        catch (Exception ex) when (v == version)
        {
            Log.Write($"anteprima del PDF non riuscita: {ex.GetType().Name} {ex.Message}");
            ResetPdf();
            pages.Children.Clear();
            status.Visibility = Visibility.Visible;
            status.Text = PdfRender.Explain(ex);
        }
        catch (Exception)
        {
            // superata da un'altra anteprima
        }
    }

    /// <summary>Larghezza delle pagine: quella del riquadro (meno i margini), per lo zoom.</summary>
    double PageWidth => Math.Max(200, (ActualWidth > 0 ? ActualWidth - 64 : 600) * zoom);

    void Layout(bool render = true)
    {
        double width = PageWidth, height = width * pageSize.Height / pageSize.Width;
        foreach (var slot in pageSlots)
        {
            slot.Width = width;
            if (slot.Child is not Image) slot.Height = height;
        }
        foreach (var (i, image) in rendered.ToList())
        {
            // Con lo zoom le pagine si ridisegnano alla misura nuova.
            pageSlots[i].Child = null;
            pageSlots[i].Height = height;
        }
        rendered.Clear();
        CancelRendering(_ => true); // chieste alla larghezza di prima
        if (render) RenderVisible();
    }

    void Zoom(double value)
    {
        zoom = Math.Clamp(value, 0.4, 4);
        if (pdfPages > 0) Layout();
        else if (pages.Children.OfType<Image>().FirstOrDefault() is { } image) image.Width = image.Source.Width * zoom;
    }

    /// <summary>
    /// Le pagine in vista (e una prima e dopo) si disegnano; quelle lontane tornano segnaposto, e quelle chieste ma ormai
    /// lontane escono dalla coda: trascinando la barra dalla pagina 1 alla 250 di un PDF grande non si aspettano le cento
    /// pagine di passaggio.
    /// </summary>
    void RenderVisible()
    {
        if (pdfPages == 0 || pageSlots.Count == 0) return;
        double slot = PageWidth * pageSize.Height / pageSize.Width + PageGap;
        int first = Math.Max(0, (int)(scroller.VerticalOffset / slot) - 1);
        int last = Math.Min(pageSlots.Count - 1, (int)((scroller.VerticalOffset + scroller.ViewportHeight) / slot) + 1);
        bool Far(int k) => k < first - KeepAround || k > last + KeepAround;
        CancelRendering(Far);
        var v = version;
        for (int i = first; i <= last; i++) _ = RenderAsync(i, v);
        foreach (var far in rendered.Keys.Where(Far).ToList())
        {
            pageSlots[far].Child = null;
            rendered.Remove(far);
        }
    }

    async Task RenderAsync(int i, int v)
    {
        if (pdfPages == 0 || rendered.ContainsKey(i) || rendering.ContainsKey(i)) return;
        var cancel = new CancellationTokenSource();
        rendering[i] = cancel;
        try
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX, width = PageWidth;
            var png = await PdfWorker.Shared.RenderAsync(i, (int)Math.Min(4000, Math.Round(width * dpi)), cancel.Token);
            // PNG, con il suo decodificatore (non la scelta automatica di WIC), in sottofondo: sul thread dell'interfaccia
            // costava a ogni pagina, anche a quelle già uscite dalla vista.
            var frame = await Task.Run(() => DecodePng(png), cancel.Token);
            if (v != version || cancel.IsCancellationRequested || i >= pageSlots.Count) return;
            var image = new Image { Source = frame, Stretch = Stretch.Uniform, Width = width };
            pageSlots[i].Height = double.NaN;
            pageSlots[i].Child = image;
            rendered[i] = image;
        }
        catch (OperationCanceledException)
        {
            // uscita dalla vista, zoom cambiato o un altro allegato
        }
        catch (Exception ex)
        {
            Log.Write($"pagina {i + 1} del PDF non disegnata: {ex.Message}");
        }
        finally
        {
            if (rendering.TryGetValue(i, out var mine) && mine == cancel) rendering.Remove(i);
            cancel.Dispose();
        }
    }

    /// <summary>Un PNG del processo dei PDF (o delle immagini) con il decodificatore dei PNG, congelato per il thread dell'interfaccia.</summary>
    static BitmapSource DecodePng(byte[] png)
    {
        var decoder = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    // ---------------------------------------------------------------- immagini e testi

    /// <summary>
    /// L'immagine decodificata nel processo dell'anteprima (nel suo AppContainer, come i PDF), che manda un PNG larga al
    /// massimo quanto serve al riquadro (fra 1440 e 2560 pixel); qui si decodifica solo quel PNG, in sottofondo.
    /// </summary>
    async Task ShowImageAsync(byte[] bytes, int v, Stopwatch clock)
    {
        BitmapSource bitmap;
        try
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var png = await PdfWorker.Shared.ImageAsync(bytes, (int)Math.Clamp((ActualWidth > 0 ? ActualWidth - 64 : 1440) * dpi, 1440, 2560));
            if (v != version) return;
            bitmap = await Task.Run(() => DecodePng(png));
            if (v != version) return;
        }
        catch (Exception ex) when (v == version)
        {
            Log.Write($"anteprima dell'immagine non riuscita: {ex.GetType().Name} {ex.Message}");
            status.Text = ex is Sandbox.SandboxException
                ? "L'anteprima protetta non si è potuta avviare su questo PC, e senza protezione Missiva non apre l'immagine: aprila con il programma di Windows (Apri)."
                : "L'immagine non si legge: il formato non è PNG, JPEG, GIF o BMP, o il file è rovinato. Puoi aprirla con il programma di Windows.";
            return;
        }
        catch (Exception)
        {
            return; // superata da un'altra anteprima
        }
        status.Visibility = Visibility.Collapsed;
        double fit = Math.Min(1, (ActualWidth > 0 ? ActualWidth - 64 : bitmap.Width) / bitmap.Width);
        zoom = fit;
        pages.Children.Add(new Image { Source = bitmap, Width = bitmap.Width * fit, Stretch = Stretch.Uniform });
        Log.Time($"anteprima: immagine ({bytes.Length / 1024} KB)", clock);
    }

    void ShowText(byte[] bytes, Stopwatch clock)
    {
        status.Visibility = Visibility.Collapsed;
        const int max = 2_000_000;
        var text = new UTF8Encoding(false, false).GetString(bytes, 0, Math.Min(bytes.Length, max)).TrimStart('﻿');
        if (bytes.Length > max) text += "\n…";
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true, BorderThickness = new Thickness(0), Padding = new Thickness(12), MinWidth = Math.Max(300, ActualWidth - 64),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(box, "Testo dell'allegato");
        pages.HorizontalAlignment = HorizontalAlignment.Stretch;
        pages.Children.Add(box);
        Log.Time($"anteprima: testo ({bytes.Length / 1024} KB)", clock);
    }
}
