using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// L'aspetto "vetro" (20 novembre 2026, richiesta dell'autore, con una foto: pannelli di vetro traslucido con gli angoli
/// grandi sopra un fondo sfumato, la selezione come una pastiglia chiara; il colore lo sceglie chi usa Missiva: quello di
/// Windows, bianco, nero o uno dei predefiniti). Tre pezzi: la tavolozza (Apply, chiamata da App.SetTextColors a ogni
/// cambio di tema o di colore), il fondo della finestra (GlassBackdrop: una velatura del colore scelto sopra lo sfondo
/// acrilico di Windows, che sfoca quello che sta dietro la finestra) e il pannello (GlassBorder: un Border con l'ombra
/// morbida disegnata ad anelli, senza effetti di WPF, che nello scorrimento costano).
/// </summary>
public static class Glass
{
    /// <summary>I colori predefiniti: la chiave salvata in Settings.Color, il nome mostrato (da tradurre con T) e il colore.</summary>
    public static readonly (string Key, string Name, Color Color)[] Presets =
    [
        ("blue", N("Azzurro"), Color.FromRgb(0x2F, 0x8F, 0xEA)),
        ("teal", N("Verde acqua"), Color.FromRgb(0x12, 0xA3, 0x9A)),
        ("green", N("Verde"), Color.FromRgb(0x3D, 0xA3, 0x52)),
        ("amber", N("Ambra"), Color.FromRgb(0xD9, 0x9A, 0x0B)),
        ("orange", N("Arancio"), Color.FromRgb(0xEE, 0x7B, 0x2A)),
        ("red", N("Rosso"), Color.FromRgb(0xDD, 0x45, 0x4A)),
        ("pink", N("Rosa"), Color.FromRgb(0xDE, 0x57, 0x98)),
        ("purple", N("Viola"), Color.FromRgb(0x8A, 0x5C, 0xF0)),
    ];

    public const string White = "white", Black = "black";

    /// <summary>Bianco e Nero sono senza colore, e decidono anche il tema: chiaro il primo, scuro il secondo.</summary>
    public static bool? ForcesDark(string? choice) => choice switch { White => false, Black => true, _ => null };

    /// <summary>Il colore scelto: null per Bianco e Nero (senza colore), quello di Windows per "" o una chiave sconosciuta.</summary>
    public static Color? Hue(string? choice)
    {
        if (choice is White or Black) return null;
        foreach (var preset in Presets)
            if (preset.Key == choice) return preset.Color;
        return SystemColors.AccentColor;
    }

    static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B);

    static SolidColorBrush Brush(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    /// <summary>Luminosità percepita, da 0 a 1 (sRGB, senza la curva: basta per scegliere fra testo chiaro e scuro).</summary>
    static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    /// <summary>
    /// Le risorse del vetro per il tema e il colore: la velatura e le macchie di luce del fondo, il vetro dei pannelli, il
    /// suo bordo e la sua ombra, la pastiglia della selezione e il passaggio del mouse. Con un colore scelto (non quello di
    /// Windows) anche l'accento dei controlli di Fluent, che altrimenti resterebbe del colore di Windows.
    /// </summary>
    public static void Apply(ResourceDictionary r, bool dark, string? choice)
    {
        var hue = Hue(choice);
        var white = Colors.White;
        var ink = Color.FromRgb(0x0D, 0x0E, 0x11);
        if (dark)
        {
            var c = hue ?? Color.FromRgb(0x80, 0x84, 0x8C);
            double tint = hue == null ? 0.04 : 0.20;
            r["GlassWashColor"] = Alpha(Mix(ink, c, tint), 0.80);
            r["GlassBlobLightColor"] = Alpha(Mix(white, c, 0.5), hue == null ? 0.05 : 0.10);
            r["GlassBlobHueColor"] = Alpha(c, hue == null ? 0.06 : 0.30);
            r["GlassBlobHue2Color"] = Alpha(Shift(c, 40), hue == null ? 0.04 : 0.18);
            r["GlassPanelBrush"] = Brush(Alpha(white, 0.065));
            r["GlassPanelStrongBrush"] = Brush(Alpha(white, 0.10));
            r["GlassBorderBrush"] = Border(Alpha(white, 0.20), Alpha(white, 0.06));
            r["GlassShadowColor"] = Alpha(Colors.Black, 0.30);
            r["GlassPillBrush"] = Brush(Alpha(white, 0.15));
            r["GlassHoverBrush"] = Brush(Alpha(white, 0.07));
            r["GlassPressedBrush"] = Brush(Alpha(white, 0.11));
            r["GlassPillBorderBrush"] = Brush(Alpha(white, 0.10));
            r["GlassDividerBrush"] = Brush(Alpha(white, 0.10));
            // La fascia della cornice (barra dei comandi e striscia delle icone), un po' più scura delle cartelle.
            r["GlassShadeBrush"] = Brush(Alpha(Colors.Black, 0.24));
            r["GlassEdgeBrush"] = Brush(Alpha(white, 0.13));
            // Il bordino fra l'elenco e il riquadro di lettura: più marcato, perché fra il vetro e la pagina piena si perde.
            r["GlassSeamBrush"] = Brush(Alpha(white, 0.22));
            // La carta delle schede dove si legge e si scrive (testo della scrittura, pagine delle Impostazioni): piena, come
            // la pagina scura del riquadro di lettura (MailHtml.DarkPaper).
            r["GlassPaperBrush"] = Brush(Color.FromRgb(0x29, 0x29, 0x29));
        }
        else
        {
            var c = hue ?? Color.FromRgb(0x8A, 0x90, 0x9C);
            double tint = hue == null ? 0.10 : 0.26;
            r["GlassWashColor"] = Alpha(Mix(white, c, tint), 0.74);
            r["GlassBlobLightColor"] = Alpha(white, 0.70);
            r["GlassBlobHueColor"] = Alpha(c, hue == null ? 0.10 : 0.26);
            r["GlassBlobHue2Color"] = Alpha(Shift(c, 40), hue == null ? 0.06 : 0.16);
            r["GlassPanelBrush"] = Brush(Alpha(white, 0.56));
            r["GlassPanelStrongBrush"] = Brush(Alpha(white, 0.72));
            r["GlassBorderBrush"] = Border(Alpha(white, 0.95), Alpha(white, 0.55));
            r["GlassShadowColor"] = Alpha(Mix(Colors.Black, c, 0.35), 0.13);
            r["GlassPillBrush"] = Brush(Alpha(white, 0.92));
            r["GlassHoverBrush"] = Brush(Alpha(white, 0.50));
            r["GlassPressedBrush"] = Brush(Alpha(white, 0.70));
            r["GlassPillBorderBrush"] = Brush(Alpha(Mix(Colors.Black, c, 0.4), 0.07));
            r["GlassDividerBrush"] = Brush(Alpha(Mix(Colors.Black, c, 0.4), 0.10));
            r["GlassShadeBrush"] = Brush(Alpha(Mix(Colors.Black, c, 0.45), 0.085));
            r["GlassEdgeBrush"] = Brush(Alpha(Mix(Colors.Black, c, 0.45), 0.16));
            r["GlassSeamBrush"] = Brush(Alpha(Mix(Colors.Black, c, 0.45), 0.26));
            r["GlassPaperBrush"] = Brush(white);
        }
        // I controlli di Fluent: angoli più grandi, come i pannelli.
        r["ControlCornerRadius"] = new CornerRadius(8);
        r["OverlayCornerRadius"] = new CornerRadius(14);
        Accent(r, dark, choice, hue);
    }

    /// <summary>
    /// L'accento dei controlli (pulsante pieno, numeri dei non letti, barrette, selezione del testo): con "come Windows"
    /// resta quello di Fluent, cioè di Windows; con un colore scelto è quel colore, con Bianco e Nero un neutro (quasi nero
    /// nel chiaro, quasi bianco nello scuro), come nella foto di riferimento.
    /// </summary>
    static void Accent(ResourceDictionary r, bool dark, string? choice, Color? hue)
    {
        string[] keys = ["AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "AccentTextFillColorPrimaryBrush",
                         "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush", "AccentFillColorSelectedTextBackgroundBrush",
                         "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush", "AccentButtonBackground", "AccentButtonBackgroundPointerOver",
                         "AccentButtonBackgroundPressed", "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed"];
        // I controlli di Fluent che prendono l'accento da risorse loro (caselle di spunta, pulsanti di scelta, interruttori,
        // cursori, barre di avanzamento, la riga sotto la casella di testo attiva): senza, con un colore scelto restavano del
        // colore di Windows (le caselle del Calendario, viste nelle foto del 20 novembre). Fermo, sotto il mouse, premuto.
        string[] fills = ["CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked", "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke",
                          "ToggleButtonBackgroundChecked", "ToggleSwitchFillOn", "ToggleSwitchStrokeOn", "SliderTrackValueFill", "SliderThumbBackground", "ProgressBarForeground",
                          "ProgressBarIndicatorForeground", "CalendarViewSelectedBackground", "CalendarViewSelectedBorderBrush", "ComboBoxItemPillFillBrush", "ListViewItemSelectionIndicatorBrush", "TabViewSelectedItemBorderBrush"];
        string[] hovers = ["CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "RadioButtonCheckOuterEllipseCheckedFillPointerOver",
                           "RadioButtonOuterEllipseCheckedStrokePointerOver", "ToggleButtonBackgroundCheckedPointerOver", "ToggleSwitchFillOnPointerOver", "ToggleSwitchStrokeOnPointerOver",
                           "SliderTrackValueFillPointerOver", "SliderThumbBackgroundPointerOver"];
        string[] presses = ["CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundStrokeCheckedPressed", "RadioButtonCheckOuterEllipseCheckedFillPressed",
                            "RadioButtonCheckOuterEllipseCheckedStrokePressed", "ToggleButtonBackgroundCheckedPressed", "ToggleSwitchFillOnPressed", "ToggleSwitchStrokeOnPressed",
                            "SliderTrackValueFillPressed", "SliderThumbBackgroundPressed"];
        string[] onFills = ["ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver",
                            "CheckBoxCheckGlyphForegroundCheckedPressed", "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed"];
        // La casella di testo attiva (e il selettore della data): il bordo di sempre con la riga del colore sotto. Le chiavi
        // sono quelle che i modelli di Fluent chiedono davvero (lette dai modelli, 20 novembre 2026).
        string[] focused = ["TextControlBorderBrushFocused", "TextControlElevationBorderFocusedBrush"];
        bool system = string.IsNullOrEmpty(choice) || (choice is not (White or Black) && !Presets.Any(p => p.Key == choice));
        if (system)
        {
            foreach (var key in keys.Concat(fills).Concat(hovers).Concat(presses).Concat(onFills).Concat(focused)) r.Remove(key);
            return;
        }
        Color fill, text = AccentText(choice, dark) ?? default;
        if (hue is { } c)
        {
            // Nel chiaro il colore un po' scurito regge il testo bianco e si legge come testo; nello scuro schiarito.
            fill = dark ? Mix(c, Colors.White, 0.38) : Mix(c, Colors.Black, 0.12);
        }
        else fill = dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1F, 0x1F, 0x1F);
        var on = Luma(fill) > 0.6 ? Color.FromRgb(0x10, 0x10, 0x10) : Colors.White;
        r["AccentFillColorDefaultBrush"] = r["AccentButtonBackground"] = Brush(fill);
        r["AccentFillColorSecondaryBrush"] = r["AccentButtonBackgroundPointerOver"] = Brush(Alpha(fill, 0.90));
        r["AccentFillColorTertiaryBrush"] = r["AccentButtonBackgroundPressed"] = Brush(Alpha(fill, 0.80));
        r["AccentFillColorSelectedTextBackgroundBrush"] = Brush(fill);
        r["AccentTextFillColorPrimaryBrush"] = Brush(text);
        r["AccentTextFillColorSecondaryBrush"] = Brush(text);
        r["AccentTextFillColorTertiaryBrush"] = Brush(Alpha(text, 0.85));
        r["TextOnAccentFillColorPrimaryBrush"] = r["AccentButtonForeground"] = r["AccentButtonForegroundPointerOver"] = Brush(on);
        r["TextOnAccentFillColorSecondaryBrush"] = r["AccentButtonForegroundPressed"] = Brush(Alpha(on, 0.75));
        foreach (var key in fills) r[key] = r["AccentFillColorDefaultBrush"];
        foreach (var key in hovers) r[key] = r["AccentFillColorSecondaryBrush"];
        foreach (var key in presses) r[key] = r["AccentFillColorTertiaryBrush"];
        foreach (var key in onFills) r[key] = r["TextOnAccentFillColorPrimaryBrush"];
        // Come il pennello di Fluent: i due pixel in basso del colore, il resto il bordo tenue dei controlli.
        var line = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, 2),
            RelativeTransform = new ScaleTransform(1, -1, 0, 0.5),
            GradientStops = { new GradientStop(fill, 1), new GradientStop(dark ? Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0, 0, 0), 1) },
        };
        line.Freeze();
        foreach (var key in focused) r[key] = line;
    }

    /// <summary>
    /// Il colore scelto come testo (link e comandi scritti), su un fondo scuro o chiaro: null con il colore di Windows, che
    /// ha i suoi. Lo usa anche la pagina del riquadro di lettura (ReadingPage.Document), per il fondo della pagina e non
    /// per il tema dell'app: con "Riquadro di lettura chiaro" nel tema scuro la pagina è chiara.
    /// </summary>
    public static Color? AccentText(string? choice, bool dark)
    {
        if (string.IsNullOrEmpty(choice) || (choice is not (White or Black) && !Presets.Any(p => p.Key == choice))) return null;
        if (Hue(choice) is { } c) return dark ? Mix(c, Colors.White, 0.55) : Mix(c, Colors.Black, 0.32);
        return dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1F, 0x1F, 0x1F);
    }

    /// <summary>Il bordo del vetro: più chiaro in alto, dove prende la luce.</summary>
    static Brush Border(Color top, Color bottom)
    {
        var brush = new LinearGradientBrush(top, bottom, 90);
        brush.Freeze();
        return brush;
    }

    /// <summary>Lo stesso colore con la tinta girata di qualche grado: la seconda macchia del fondo, perché non sia di un colore solo.</summary>
    static Color Shift(Color c, double degrees)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-6) return c;
        double s = d / (1 - Math.Abs(2 * l - 1));
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h = ((h * 60 + degrees) % 360 + 360) % 360;
        double k = (1 - Math.Abs(2 * l - 1)) * s, x = k * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - k / 2;
        var (r1, g1, b1) = h < 60 ? (k, x, 0.0) : h < 120 ? (x, k, 0.0) : h < 180 ? (0.0, k, x) : h < 240 ? (0.0, x, k) : h < 300 ? (x, 0.0, k) : (k, 0.0, x);
        return Color.FromRgb((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    /// <summary>
    /// Lo sfondo acrilico di Windows 11 dietro la finestra (DWMSBT_TRANSIENTWINDOW): sfoca il desktop e le finestre sotto
    /// Missiva, il vetro vero. Fluent di WPF mette Mica, che è solo una tinta dello sfondo del desktop; va rimesso dopo ogni
    /// cambio di tema, perché WPF rimette il suo. Dove Windows non lo ha (prima di Windows 11 22H2, o con gli effetti di
    /// trasparenza spenti) resta un fondo pieno, e sopra la velatura di GlassBackdrop.
    /// </summary>
    public static void Acrylic(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int acrylic = 3;
        DwmSetWindowAttribute(handle, 38 /* DWMWA_SYSTEMBACKDROP_TYPE */, ref acrylic, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Il fondo di vetro per una finestra qualunque di Missiva (scrittura, Impostazioni, domande, evento, contatto, regola,
    /// messaggio): la velatura del colore scelto dietro al suo contenuto, e l'acrilico di Windows dietro la finestra. Le
    /// chiama App per ogni finestra che si carica (App.OnWindowLoaded), tranne la principale, che ha il suo fondo con il foro
    /// per i pulsanti di sistema, e i pannellini senza cornice. Queste finestre hanno la barra del titolo di Windows: i
    /// pulsanti di sistema stanno fuori dal contenuto, e il fondo non ha bisogno del foro.
    /// </summary>
    public static void Dress(Window window, bool acrylic)
    {
        if (window.Content is not UIElement content || content is Grid { Tag: DressedTag }) return;
        window.Content = null;
        var back = new GlassBackdrop();
        back.SetResourceReference(GlassBackdrop.WashProperty, "GlassWashColor");
        back.SetResourceReference(GlassBackdrop.BlobLightProperty, "GlassBlobLightColor");
        back.SetResourceReference(GlassBackdrop.BlobHueProperty, "GlassBlobHueColor");
        back.SetResourceReference(GlassBackdrop.BlobHue2Property, "GlassBlobHue2Color");
        var grid = new Grid { Tag = DressedTag };
        grid.Children.Add(back);
        grid.Children.Add(content);
        window.Content = grid;
        if (acrylic) Acrylic(window);
    }

    const string DressedTag = "vetro";
}

/// <summary>
/// Il fondo della finestra: una velatura del colore scelto, con due o tre macchie di luce larghe e sfumate, sopra lo sfondo
/// di Windows. Attorno ai pulsanti di sistema (riduci, ingrandisci, chiudi), che Windows disegna sotto il contenuto della
/// finestra, la velatura sfuma fino a sparire: sopra di loro li sbiadirebbe. Niente maschere di opacità né effetti: pezzi
/// rettangolari con pennelli sfumati, disegnati una volta.
/// </summary>
public sealed class GlassBackdrop : FrameworkElement
{
    public static readonly DependencyProperty WashProperty = Register(nameof(Wash));
    public static readonly DependencyProperty BlobLightProperty = Register(nameof(BlobLight));
    public static readonly DependencyProperty BlobHueProperty = Register(nameof(BlobHue));
    public static readonly DependencyProperty BlobHue2Property = Register(nameof(BlobHue2));
    /// <summary>Il rettangolo dei pulsanti di sistema, nelle coordinate di questo elemento; vuoto se non ci sono.</summary>
    public static readonly DependencyProperty HoleProperty = DependencyProperty.Register(nameof(Hole), typeof(Rect), typeof(GlassBackdrop),
        new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    static DependencyProperty Register(string name) => DependencyProperty.Register(name, typeof(Color), typeof(GlassBackdrop),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color Wash { get => (Color)GetValue(WashProperty); set => SetValue(WashProperty, value); }
    public Color BlobLight { get => (Color)GetValue(BlobLightProperty); set => SetValue(BlobLightProperty, value); }
    public Color BlobHue { get => (Color)GetValue(BlobHueProperty); set => SetValue(BlobHueProperty, value); }
    public Color BlobHue2 { get => (Color)GetValue(BlobHue2Property); set => SetValue(BlobHue2Property, value); }
    public Rect Hole { get => (Rect)GetValue(HoleProperty); set => SetValue(HoleProperty, value); }

    /// <summary>Quanto è larga la sfumatura attorno ai pulsanti di sistema, in DIP.</summary>
    internal const double Feather = 56;

    public GlassBackdrop()
    {
        IsHitTestVisible = false;
        // I pezzi della velatura si toccano: con i bordi sfumati fra l'uno e l'altro si vedrebbe una riga.
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        foreach (var (rect, brush) in Pieces(new Size(w, h), Hole, Wash)) dc.DrawRectangle(brush, null, rect);
        // Le macchie di luce stanno lontane dall'angolo dei pulsanti di sistema: in alto a sinistra la chiara, in basso a
        // destra e in basso a sinistra quelle del colore.
        foreach (var blob in Blobs()) dc.DrawRectangle(blob.Brush(), null, new Rect(0, 0, w, h));
    }

    /// <summary>
    /// Una macchia di luce: un ovale del colore, pieno al centro, al 45% a quasi metà raggio (MidStop) e a zero sul bordo.
    /// Il riquadro di lettura le ridisegna uguali nella sua pagina (ReadingView.GlassCss), con gli stessi numeri.
    /// </summary>
    internal readonly record struct Blob(Color Color, Point Center, double Rx, double Ry)
    {
        internal const double MidStop = 0.45, MidAlpha = 0.45;

        internal Brush Brush()
        {
            var brush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = Center, GradientOrigin = Center, RadiusX = Rx, RadiusY = Ry };
            brush.GradientStops.Add(new GradientStop(Color, 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(Color.A * MidAlpha), Color.R, Color.G, Color.B), MidStop));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, Color.R, Color.G, Color.B), 1));
            brush.Freeze();
            return brush;
        }

        /// <summary>Quanto del colore c'è in un punto, da 0 a 1 (come lo disegna il pennello).</summary>
        internal double At(Point p)
        {
            double dx = (p.X - Center.X) / Rx, dy = (p.Y - Center.Y) / Ry, t = Math.Sqrt(dx * dx + dy * dy);
            double part = t >= 1 ? 0 : t <= MidStop ? 1 - (1 - MidAlpha) * t / MidStop : MidAlpha * (1 - t) / (1 - MidStop);
            return part * Color.A / 255.0;
        }
    }

    /// <summary>Le macchie di questo fondo, nelle sue coordinate, dalla più bassa alla più alta; quelle senza colore non ci sono.</summary>
    internal List<Blob> Blobs()
    {
        double w = ActualWidth, h = ActualHeight;
        var all = new List<Blob>
        {
            new(BlobLight, new Point(w * 0.12, -h * 0.10), w * 0.62, h * 0.80),
            new(BlobHue, new Point(w * 0.92, h * 1.05), w * 0.60, h * 0.85),
            new(BlobHue2, new Point(w * 0.05, h * 1.00), w * 0.45, h * 0.60),
        };
        all.RemoveAll(b => b.Color.A == 0 || b.Rx <= 0 || b.Ry <= 0);
        return all;
    }

    /// <summary>
    /// I pezzi della velatura: tutta l'area se non c'è il foro; altrimenti l'area piena a sinistra e sotto il foro, e attorno
    /// al foro tre pezzi sfumati (a sinistra, sotto, e nell'angolo fra i due) che portano la velatura a zero sul suo bordo.
    /// Il foro si intende appoggiato in alto a destra, com'è per i pulsanti di sistema.
    /// </summary>
    internal static List<(Rect Rect, Brush Brush)> Pieces(Size size, Rect hole, Color wash)
    {
        var pieces = new List<(Rect, Brush)>();
        if (wash.A == 0) return pieces;
        var solid = new SolidColorBrush(wash);
        solid.Freeze();
        double w = size.Width, h = size.Height;
        if (hole.IsEmpty || hole.Width <= 0 || hole.Height <= 0 || hole.Left >= w || hole.Bottom <= 0)
        {
            pieces.Add((new Rect(0, 0, w, h), solid));
            return pieces;
        }
        var clear = Color.FromArgb(0, wash.R, wash.G, wash.B);
        double x = Math.Max(0, hole.Left), y = Math.Min(h, hole.Bottom), f = Math.Min(Feather, x), fy = Math.Min(Feather, h - y);
        if (x - f > 0) pieces.Add((new Rect(0, 0, x - f, h), solid));
        if (h - y - fy > 0) pieces.Add((new Rect(x - f, y + fy, w - (x - f), h - y - fy), solid));
        if (f > 0 && y > 0)
        {
            var across = new LinearGradientBrush(wash, clear, new Point(x - f, 0), new Point(x, 0)) { MappingMode = BrushMappingMode.Absolute };
            across.Freeze();
            pieces.Add((new Rect(x - f, 0, f, y), across));
        }
        if (fy > 0 && w - x > 0)
        {
            var down = new LinearGradientBrush(clear, wash, new Point(0, y), new Point(0, y + fy)) { MappingMode = BrushMappingMode.Absolute };
            down.Freeze();
            pieces.Add((new Rect(x, y, w - x, fy), down));
        }
        if (f > 0 && fy > 0)
        {
            var corner = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = new Point(x, y), GradientOrigin = new Point(x, y), RadiusX = f, RadiusY = fy };
            corner.GradientStops.Add(new GradientStop(clear, 0));
            corner.GradientStops.Add(new GradientStop(wash, 1));
            corner.Freeze();
            pieces.Add((new Rect(x - f, y, f, fy), corner));
        }
        return pieces;
    }
}

/// <summary>
/// La cornice della finestra principale in un pezzo solo di vetro (20 novembre 2026, l'autore: "voglio che la colonna di
/// posta in arrivo e il lato di scelta tra mail, calendario e persone siano integrati in un tutt'uno. E devono essere
/// integrati anche con la riga del nuovo messaggio"): la barra dei comandi in alto (Bar) e, sotto di lei a sinistra, la
/// colonna con Posta, Calendario e Persone e le cartelle (Side), unite in una L con gli angoli arrotondati, anche quello
/// interno. Elenco e riquadro di lettura stanno nell'angolo della L, staccati. I due rettangoli li dà la finestra
/// (MainWindow.FitChrome) nelle coordinate di questo elemento; senza la barra (Calendario, Persone) resta la colonna, senza
/// la colonna la barra. Disegnata una volta: riempimento, bordo e un'ombra fatta di contorni sempre più larghi e tenui,
/// fuori dalla forma.
/// </summary>
public sealed class GlassShape : FrameworkElement
{
    public static readonly DependencyProperty BarProperty = Rects(nameof(Bar));
    public static readonly DependencyProperty SideProperty = Rects(nameof(Side));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(GlassShape),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(GlassShape),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShadowColorProperty = DependencyProperty.Register(nameof(ShadowColor), typeof(Color), typeof(GlassShape),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    static DependencyProperty Rects(string name) => DependencyProperty.Register(name, typeof(Rect), typeof(GlassShape),
        new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Dove finisce, a destra, la striscia di Posta, Calendario e Persone dentro la colonna (la x, in queste coordinate); NaN
    /// se la colonna è tutta sua (cartelle chiuse, Calendario, Persone).
    /// </summary>
    public static readonly DependencyProperty RailProperty = DependencyProperty.Register(nameof(Rail), typeof(double), typeof(GlassShape),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShadeProperty = DependencyProperty.Register(nameof(Shade), typeof(Brush), typeof(GlassShape),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DividerProperty = DependencyProperty.Register(nameof(Divider), typeof(Brush), typeof(GlassShape),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Rect Bar { get => (Rect)GetValue(BarProperty); set => SetValue(BarProperty, value); }
    public Rect Side { get => (Rect)GetValue(SideProperty); set => SetValue(SideProperty, value); }
    public double Rail { get => (double)GetValue(RailProperty); set => SetValue(RailProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    /// <summary>La tinta della fascia più scura: la barra dei comandi e la striscia di Posta, Calendario e Persone.</summary>
    public Brush? Shade { get => (Brush?)GetValue(ShadeProperty); set => SetValue(ShadeProperty, value); }
    /// <summary>Il bordino fra la fascia più scura e le cartelle.</summary>
    public Brush? Divider { get => (Brush?)GetValue(DividerProperty); set => SetValue(DividerProperty, value); }
    public Color ShadowColor { get => (Color)GetValue(ShadowColorProperty); set => SetValue(ShadowColorProperty, value); }

    /// <summary>Gli angoli esterni, quello interno della L e quello delle cartelle dentro la fascia.</summary>
    internal const double Radius = 20, Inner = 16, Nested = 12;

    public GlassShape() => IsHitTestVisible = false;

    /// <summary>
    /// Le cartelle dentro la cornice (20 novembre 2026, l'autore: "va bene l'unione, ma deve rimanere un bordino a separare
    /// la riga di posta, calendario e contatti con quella della posta in arrivo, e la riga di posta, calendario e contatti
    /// deve essere un po' più scura. Lo stesso vale per la riga di nuovo messaggio"): la parte della colonna a destra della
    /// striscia delle icone e sotto la barra, con l'angolo in alto a sinistra arrotondato. Il resto della forma è la fascia
    /// più scura. Null se non ci sono (la colonna è solo la striscia, o manca la barra o la colonna).
    /// </summary>
    internal static (Geometry Area, Geometry Edge)? Folders(Rect bar, Rect side, double rail)
    {
        if (double.IsNaN(rail) || side.IsEmpty || rail <= side.Left + 1 || rail >= side.Right - 1) return null;
        if (bar.IsEmpty)
        {
            // Senza la barra dei comandi (Calendario, Persone): tutto quello che sta a destra della striscia, con un bordino dritto.
            var whole = new RectangleGeometry(new Rect(rail, side.Top - 2, side.Right - rail + Inner + 2, side.Height + 4));
            whole.Freeze();
            var straight = new LineGeometry(new Point(rail, side.Top - 2), new Point(rail, side.Bottom + 2));
            straight.Freeze();
            return (whole, straight);
        }
        if (side.Bottom <= bar.Bottom + 1) return null;
        double yb = bar.Bottom, y1 = side.Bottom, xs = side.Right, r = Math.Min(Nested, Math.Min(xs - rail, y1 - yb) / 2);
        // Fino all'angolo interno della L compreso (il suo raccordo sta con le cartelle), e oltre il bordo della forma in basso
        // e a destra: lì la taglia la forma.
        double far = xs + Inner + 2;
        // Le figure nascono dal costruttore (inizio, tratti, chiusa o no): una prova della suite cerca in tutta l'interfaccia la
        // parola con cui si chiede a un provider se è chiuso, che è anche il nome della proprietà delle figure.
        var area = new PathFigure(new Point(rail + r, yb),
        [
            new LineSegment(new Point(far, yb), true), new LineSegment(new Point(far, y1 + 2), true), new LineSegment(new Point(rail, y1 + 2), true),
            new LineSegment(new Point(rail, yb + r), true), new ArcSegment(new Point(rail + r, yb), new Size(r, r), 0, false, SweepDirection.Clockwise, true),
        ], true);
        var edge = new PathFigure(new Point(far, yb),
        [
            new LineSegment(new Point(rail + r, yb), true), new ArcSegment(new Point(rail, yb + r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true),
            new LineSegment(new Point(rail, y1 + 2), true),
        ], false) { IsFilled = false };
        var areaPath = new PathGeometry();
        areaPath.Figures.Add(area);
        areaPath.Freeze();
        var edgePath = new PathGeometry();
        edgePath.Figures.Add(edge);
        edgePath.Freeze();
        return (areaPath, edgePath);
    }

    /// <summary>
    /// Il contorno: la L di barra e colonna (stesso angolo in alto a sinistra, la barra più larga e la colonna più alta), o il
    /// rettangolo arrotondato di quella che c'è; null se non c'è nulla. Gli angoli non superano mai la metà del lato.
    /// </summary>
    internal static Geometry? Outline(Rect bar, Rect side)
    {
        bool hasBar = !bar.IsEmpty && bar.Width > 0 && bar.Height > 0, hasSide = !side.IsEmpty && side.Width > 0 && side.Height > 0;
        if (!hasBar && !hasSide) return null;
        Geometry Rounded(Rect r)
        {
            double radius = Math.Min(Radius, Math.Min(r.Width, r.Height) / 2);
            return new RectangleGeometry(r, radius, radius);
        }
        // Una L solo se la barra sporge a destra della colonna e la colonna sotto la barra; altrimenti il pezzo più grande.
        if (!hasBar) return Rounded(side);
        if (!hasSide) return Rounded(bar);
        if (side.Bottom <= bar.Bottom + 1 || bar.Right <= side.Right + 1) return Rounded(Rect.Union(bar, side));
        double x0 = Math.Min(bar.Left, side.Left), y0 = Math.Min(bar.Top, side.Top), x1 = bar.Right, yb = bar.Bottom, xs = side.Right, y1 = side.Bottom;
        double r = Math.Min(Radius, Math.Min(yb - y0, xs - x0) / 2);           // in alto a sinistra e in basso nella colonna
        double end = Math.Min(Radius, (yb - y0) / 2);                             // l'estremità destra della barra
        double inner = Math.Min(Inner, Math.Min(x1 - xs - end, y1 - yb - r));    // l'angolo interno, concavo
        inner = Math.Max(0, inner);
        var segments = new List<PathSegment>();
        void Line(double x, double y) => segments.Add(new LineSegment(new Point(x, y), true));
        void Arc(double x, double y, double radius, SweepDirection sweep) =>
            segments.Add(new ArcSegment(new Point(x, y), new Size(radius, radius), 0, false, sweep, true));
        Line(x1 - end, y0);
        Arc(x1, y0 + end, end, SweepDirection.Clockwise);
        Line(x1, yb - end);
        Arc(x1 - end, yb, end, SweepDirection.Clockwise);
        Line(xs + inner, yb);
        if (inner > 0) Arc(xs, yb + inner, inner, SweepDirection.Counterclockwise);
        Line(xs, y1 - r);
        Arc(xs - r, y1, r, SweepDirection.Clockwise);
        Line(x0 + r, y1);
        Arc(x0, y1 - r, r, SweepDirection.Clockwise);
        Line(x0, y0 + r);
        Arc(x0 + r, y0, r, SweepDirection.Clockwise);
        var path = new PathGeometry();
        path.Figures.Add(new PathFigure(new Point(x0 + r, y0), segments, true));
        path.Freeze();
        return path;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Outline(Bar, Side) is not { } outline) return;
        var color = ShadowColor;
        if (color.A > 0)
        {
            // L'ombra: il contorno ripassato con penne sempre più larghe e tenui, solo fuori dalla forma, un poco più in basso.
            const int rings = 18;
            var bounds = outline.Bounds;
            bounds.Inflate(rings + 6, rings + 6);
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), outline);
            outside.Freeze();
            dc.PushClip(outside);
            dc.PushTransform(new TranslateTransform(0, 3));
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Max(1, Math.Round(color.A * 0.05)), color.R, color.G, color.B));
            brush.Freeze();
            for (int i = 1; i <= rings; i++)
            {
                var pen = new Pen(brush, 2 * i) { LineJoin = PenLineJoin.Round };
                pen.Freeze();
                dc.DrawGeometry(null, pen, outline);
            }
            dc.Pop();
            dc.Pop();
        }
        dc.DrawGeometry(Fill, null, outline);
        // La fascia più scura (barra dei comandi e striscia delle icone): tutta la forma meno le cartelle; e il bordino lungo
        // il confine con le cartelle. Tutto dentro la forma.
        var folders = Folders(Bar, Side, Rail);
        dc.PushClip(outline);
        if (Shade is { } shade)
        {
            Geometry dark = outline;
            if (folders is { } f)
            {
                dark = new CombinedGeometry(GeometryCombineMode.Exclude, outline, f.Area);
                dark.Freeze();
            }
            dc.DrawGeometry(shade, null, dark);
        }
        if (folders is { } inside && Divider is { } divider)
        {
            var line = new Pen(divider, 1);
            if (divider.IsFrozen) line.Freeze();
            dc.DrawGeometry(null, line, inside.Edge);
        }
        dc.Pop();
        if (Stroke is { } stroke)
        {
            var border = new Pen(stroke, 1);
            if (stroke.IsFrozen) border.Freeze();
            dc.DrawGeometry(null, border, outline);
        }
    }
}

/// <summary>
/// Un pannello di vetro: un Border (fondo traslucido, bordo chiaro, angoli grandi, dallo stile GlassPanel) con sotto
/// un'ombra morbida, disegnata con anelli di un DIP sempre più tenui fuori dal pannello. Niente DropShadowEffect: su un
/// pannello con dentro l'elenco dei messaggi WPF rifarebbe l'effetto su tutto il pannello a ogni fotogramma dello
/// scorrimento. L'ombra sta solo fuori dal pannello (ritagliata): sotto un vetro traslucido lo scurirebbe.
/// </summary>
public class GlassBorder : Border
{
    public static readonly DependencyProperty ShadowColorProperty = DependencyProperty.Register(nameof(ShadowColor), typeof(Color), typeof(GlassBorder),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShadowSizeProperty = DependencyProperty.Register(nameof(ShadowSize), typeof(double), typeof(GlassBorder),
        new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color ShadowColor { get => (Color)GetValue(ShadowColorProperty); set => SetValue(ShadowColorProperty, value); }
    /// <summary>Quanto si allarga l'ombra, in DIP.</summary>
    public double ShadowSize { get => (double)GetValue(ShadowSizeProperty); set => SetValue(ShadowSizeProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var color = ShadowColor;
        int rings = (int)Math.Round(ShadowSize);
        double w = ActualWidth, h = ActualHeight, radius = CornerRadius.TopLeft;
        if (color.A > 0 && rings > 0 && w > 0 && h > 0)
        {
            // Fuori dal pannello soltanto, e un poco più in basso che in alto, come un'ombra che cade.
            const double drop = 4;
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(-rings - 2, -rings - 2, w + 2 * rings + 4, h + 2 * rings + drop + 4)),
                new RectangleGeometry(new Rect(0, 0, w, h), radius, radius));
            outside.Freeze();
            dc.PushClip(outside);
            for (int i = 1; i <= rings; i++)
            {
                double t = 1 - (i - 0.5) / rings;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(color.A * t * t * 0.55), color.R, color.G, color.B)), 1.2);
                pen.Freeze();
                double grow = i - 0.5;
                dc.DrawRoundedRectangle(null, pen, new Rect(-grow, -grow + drop * (1 - t), w + 2 * grow, h + 2 * grow), radius + grow, radius + grow);
            }
            dc.Pop();
        }
        base.OnRender(dc);
    }
}
