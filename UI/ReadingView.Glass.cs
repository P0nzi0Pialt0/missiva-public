using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Missiva.UI;

/// <summary>
/// Il riquadro di lettura di vetro (20 novembre 2026, l'autore: "vorrei che anche il riquadro di lettura fosse glass, anche
/// se più scuro/chiaro per migliorare il contrasto e la leggibilità"). WebView2 è una finestra a parte dentro quella di
/// Missiva e non lascia vedere WPF sotto di sé: resa trasparente mostra il fondo grezzo della finestra, non il vetro
/// (provato quel giorno: dentro la pagina #F3F3F3, attorno il vetro azzurro). Quindi il vetro lo ridisegna la pagina: lo
/// stesso fondo della finestra (la velatura e le macchie di luce di GlassBackdrop, con gli stessi numeri, nel punto in cui
/// sta la pagina), il foglio di vetro della finestra e sopra un velo, più chiaro nel tema chiaro e più scuro nello scuro,
/// che è quello che dà contrasto al testo. Il foglio di WPF attorno alla pagina (Paper) è dipinto con gli stessi pezzi,
/// così fra i due non c'è stacco. Sotto la velatura, al posto dello sfondo acrilico di Windows, che dentro la pagina non
/// può arrivare, c'è il grigio di base delle finestre: il riquadro non lascia intravedere la scrivania, il resto sì.
/// </summary>
public partial class ReadingView
{
    /// <summary>Quello che sta dietro il testo, dal basso in alto: fondo pieno, macchie di luce, veli. Nelle coordinate del fondo della finestra.</summary>
    sealed record GlassLook(GlassBackdrop Backdrop, Color Ground, List<GlassBackdrop.Blob> Blobs, List<Color> Veils)
    {
        /// <summary>Il colore che risulta in un punto.</summary>
        public Color At(Point p)
        {
            var c = Ground;
            foreach (var blob in Blobs) c = Over(blob.Color, blob.At(p), c);
            foreach (var veil in Veils) c = Over(veil, veil.A / 255.0, c);
            return c;
        }
    }

    static Color Over(Color top, double alpha, Color under)
    {
        byte Mix(byte t, byte u) => (byte)Math.Round(Math.Clamp(alpha * t + (1 - alpha) * u, 0, 255));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    /// <summary>
    /// Il velo del riquadro sopra il vetro della finestra: bianco nel tema chiaro, nero nello scuro. Con "Riquadro di
    /// lettura chiaro" nel tema scuro quasi pieno: una pagina chiara su un vetro scuro verrebbe grigia.
    /// </summary>
    static Color Veil(bool light) => !light ? Color.FromArgb(0x59, 0, 0, 0) : Dark ? Color.FromArgb(0xF0, 255, 255, 255) : Color.FromArgb(0x8C, 255, 255, 255);

    /// <summary>Il foglio senza il vetro (prima della disposizione, nelle prove senza finestra): il colore pieno di prima.</summary>
    static Color PlainPaper(bool light) => light ? Colors.White : MailHtml.DarkPaper;

    GlassLook? Look(bool light)
    {
        if (Window.GetWindow(this) is not { Content: Panel root } || root.Children.OfType<GlassBackdrop>().FirstOrDefault() is not { ActualWidth: > 0, ActualHeight: > 0 } back) return null;
        // Il grigio di base delle finestre di Windows, al posto dello sfondo acrilico; sopra, la velatura del colore scelto.
        var ground = Over(back.Wash, back.Wash.A / 255.0, Dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
        var veils = new List<Color>();
        if (Application.Current.TryFindResource("GlassPanelBrush") is SolidColorBrush panel) veils.Add(panel.Color);
        veils.Add(Veil(light));
        return new GlassLook(back, ground, back.Blobs(), veils);
    }

    /// <summary>Un elemento del riquadro nelle coordinate del fondo della finestra; null se non è ancora disposto.</summary>
    static Rect? Place(FrameworkElement element, GlassLook look)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
        try { return element.TransformToVisual(look.Backdrop).TransformBounds(new Rect(element.RenderSize)); }
        catch (InvalidOperationException) { return null; } // non sono (ancora) nella stessa finestra
    }

    bool? paperLight;

    /// <summary>Il fondo del foglio (Paper): il vetro con il velo, pieno; il colore di prima se il vetro non c'è.</summary>
    Brush PaperBrush(bool light)
    {
        paperLight = light;
        Brush brush;
        if (Look(light) is { } look && Place(Paper, look) is { } rect)
        {
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                dc.DrawRectangle(Solid(look.Ground), null, rect);
                foreach (var blob in look.Blobs) dc.DrawRectangle(blob.Brush(), null, rect);
                foreach (var veil in look.Veils) dc.DrawRectangle(Solid(veil), null, rect);
            }
            group.Freeze();
            brush = new DrawingBrush(group) { Viewbox = rect, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        }
        else brush = new SolidColorBrush(PlainPaper(light));
        brush.Freeze();
        return brush;

        static Brush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    /// <summary>Il colore del foglio al centro della pagina: il fondo di WebView2 prima della pagina, e quello su cui si posa il segnaposto.</summary>
    Color PaperColor(bool light) =>
        Look(light) is { } look && Place(WebSlot, look) is { } rect ? look.At(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)) : PlainPaper(light);

    /// <summary>Il vetro per la pagina (ReadingPage.Document): null se non c'è, e la pagina ha il fondo pieno di prima.</summary>
    PageBack? PageGlass(bool light)
    {
        if (Look(light) is not { } look || Place(WebSlot, look) is not { } rect) return null;
        double zoom = web is { ZoomFactor: > 0 } w ? w.ZoomFactor : Math.Clamp(Store.Settings.Zoom, 50, 200) / 100.0;
        var flat = look.At(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
        return new PageBack(GlassCss(look, rect.TopLeft, zoom), Css(flat), Css(flat, 0.55));
    }

    /// <summary>
    /// I pezzi del vetro come fondo CSS, dal più alto al più basso: i veli, le macchie (le stesse di GlassBackdrop.Blob:
    /// ovali con il colore pieno al centro, al 45% a quasi metà raggio e a zero sul bordo), il fondo pieno. Le misure in
    /// pixel della pagina: quelle di WPF dall'angolo della pagina, divise per lo zoom del riquadro. Solo numeri e colori.
    /// </summary>
    static string GlassCss(GlassLook look, Point origin, double zoom)
    {
        string Px(double v) => (v / zoom).ToString("0.#", CultureInfo.InvariantCulture) + "px";
        var layers = new List<string>();
        for (int i = look.Veils.Count - 1; i >= 0; i--) layers.Add($"linear-gradient({Css(look.Veils[i])},{Css(look.Veils[i])})");
        for (int i = look.Blobs.Count - 1; i >= 0; i--)
        {
            var b = look.Blobs[i];
            layers.Add($"radial-gradient({Px(b.Rx)} {Px(b.Ry)} at {Px(b.Center.X - origin.X)} {Px(b.Center.Y - origin.Y)}," +
                       $"{Css(b.Color)} 0%,{Css(b.Color, GlassBackdrop.Blob.MidAlpha)} {(GlassBackdrop.Blob.MidStop * 100).ToString("0.#", CultureInfo.InvariantCulture)}%,{Css(b.Color, 0)} 100%)");
        }
        layers.Add(Css(look.Ground));
        return string.Join(",", layers);
    }

    static string Css(Color c, double part = 1) =>
        $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0 * part).ToString("0.###", CultureInfo.InvariantCulture)})";

    DispatcherTimer? glassTimer;

    /// <summary>
    /// Il riquadro ha cambiato misura (finestra, divisore, cartelle): il foglio di WPF si ridipinge subito, la pagina aperta
    /// riceve il suo vetro nuovo un attimo dopo l'ultimo cambio (la proprietà --vetro, con uno script di Missiva: nella
    /// pagina gli script delle email restano spenti). Senza, fino al messaggio dopo le macchie resterebbero dov'erano.
    /// </summary>
    void OnPaperSize()
    {
        if (paperLight is not { } light) return;
        Paper.Background = PaperBrush(light);
        glassTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
        {
            glassTimer!.Stop();
            if (paperLight is { } l && web?.CoreWebView2 is { } core && PageGlass(l) is { } glass)
                _ = core.ExecuteScriptAsync($"document.documentElement.style.setProperty('--vetro','{glass.Css}')");
        }, Dispatcher);
        glassTimer.Stop();
        glassTimer.Start();
    }
}
