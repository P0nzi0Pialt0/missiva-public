#if DEBUG
using System.Windows;
using System.Windows.Media;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Il vetro (20 novembre 2026, UI/Glass.cs, UI/MainWindow.Tabs.cs, UI/ReadingView.Glass.cs): la tavolozza per ogni tema e
/// colore, le macchie di luce che la pagina del riquadro ridisegna con gli stessi numeri, la pagina con e senza il vetro,
/// le larghezze delle schede degli account, e nei sorgenti gli agganci delle altre finestre. Senza finestre e senza rete.
/// Con "Missiva.exe --prova-audit risultato.txt vetro" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void GlassTests()
    {
        Report.Add("--- Il vetro (20 novembre 2026)");
        VtPalette();
        VtBlobs();
        VtPage();
        VtTabs();
        VtSources();
    }

    /// <summary>
    /// La tavolozza: per i due temi e per ogni scelta (il colore di Windows, bianco, nero, gli otto predefiniti) ci sono tutti
    /// i colori del vetro e la carta è piena; con un colore scelto i controlli di Fluent prendono quello (anche la riga sotto
    /// la casella di testo attiva, con la chiave che il modello chiede davvero), con il colore di Windows quelle voci non ci
    /// sono, nemmeno tornando indietro da un colore; bianco e nero portano con sé il tema.
    /// </summary>
    static void VtPalette()
    {
        string[] glass = ["GlassWashColor", "GlassBlobLightColor", "GlassBlobHueColor", "GlassBlobHue2Color", "GlassPanelBrush", "GlassBorderBrush", "GlassShadowColor",
                          "GlassPillBrush", "GlassHoverBrush", "GlassPressedBrush", "GlassPillBorderBrush", "GlassDividerBrush", "GlassShadeBrush", "GlassEdgeBrush",
                          "GlassSeamBrush", "GlassPaperBrush"];
        string[] accent = ["AccentFillColorDefaultBrush", "AccentButtonBackground", "CheckBoxCheckBackgroundFillChecked", "TextControlBorderBrushFocused"];
        var choices = new[] { "", Glass.White, Glass.Black }.Concat(Glass.Presets.Select(p => p.Key)).ToList();
        var wrong = new List<string>();
        foreach (bool dark in new[] { false, true })
            foreach (var choice in choices)
            {
                var r = new ResourceDictionary();
                Glass.Apply(r, dark, choice);
                string name = (dark ? "scuro " : "chiaro ") + (choice.Length > 0 ? choice : "Windows");
                if (glass.Any(k => !r.Contains(k))) wrong.Add(name + ": manca " + string.Join(", ", glass.Where(k => !r.Contains(k))));
                if (r["GlassPaperBrush"] is not SolidColorBrush { Color.A: 255 }) wrong.Add(name + ": la carta non è piena");
                if (accent.Any(k => r.Contains(k) != (choice.Length > 0))) wrong.Add(name + ": i colori dei controlli");
                Glass.Apply(r, dark, "");
                if (accent.Any(r.Contains)) wrong.Add(name + ": tornando al colore di Windows restano i colori dei controlli");
            }
        bool themes = Glass.ForcesDark(Glass.White) == false && Glass.ForcesDark(Glass.Black) == true && Glass.ForcesDark("blue") == null && Glass.ForcesDark("") == null
                      && Glass.Hue(Glass.White) == null && Glass.Hue(Glass.Black) == null && Glass.Presets.All(p => Glass.Hue(p.Key) == p.Color);
        Check(wrong.Count == 0 && themes && Glass.Presets.Length == 8,
              $"vetro, la tavolozza: nei due temi, per il colore di Windows, bianco, nero e gli {Glass.Presets.Length} predefiniti, tutti i {glass.Length} colori del vetro, la carta piena, " +
              $"e i controlli del colore scelto solo con un colore scelto ({(wrong.Count == 0 ? "sì" : string.Join("; ", wrong.Take(4)))}); bianco e nero portano il tema, gli altri no ({themes})");
    }

    /// <summary>
    /// Una macchia di luce: piena al centro, al 45% a quasi metà raggio, a zero dal bordo in poi, come il pennello con cui
    /// GlassBackdrop la disegna (il riquadro di lettura ne calcola il colore con At e la riscrive in CSS con gli stessi
    /// numeri); un fondo senza misura non ha macchie.
    /// </summary>
    static void VtBlobs()
    {
        var blob = new GlassBackdrop.Blob(Color.FromArgb(128, 10, 20, 30), new Point(100, 100), 50, 20);
        double full = 128 / 255.0;
        bool Near(double a, double b) => Math.Abs(a - b) < 0.002;
        bool at = Near(blob.At(new Point(100, 100)), full) && Near(blob.At(new Point(100 + 50 * 0.45, 100)), full * 0.45) && Near(blob.At(new Point(100, 100 + 20 * 0.45)), full * 0.45)
                  && blob.At(new Point(150, 100)) == 0 && blob.At(new Point(100, 121)) == 0 && Near(blob.At(new Point(100, 100 + 20 * 0.725)), full * 0.225);
        bool brush = blob.Brush() is RadialGradientBrush { IsFrozen: true, RadiusX: 50, RadiusY: 20, GradientStops.Count: 3 } b
                     && b.GradientStops[1].Offset == GlassBackdrop.Blob.MidStop && b.GradientStops[1].Color.A == (byte)(128 * GlassBackdrop.Blob.MidAlpha) && b.GradientStops[2].Color.A == 0;
        bool none = new GlassBackdrop { BlobLight = Colors.White, BlobHue = Colors.Blue, BlobHue2 = Colors.Red }.Blobs().Count == 0;
        Check(at && brush && none,
              $"vetro, le macchie di luce: il colore in un punto è quello del pennello (pieno al centro, 45% a quasi metà raggio, zero dal bordo: {at}), il pennello ha gli stessi tre punti ({brush}), " +
              $"un fondo senza misura non ne ha ({none})");
    }

    /// <summary>
    /// La pagina del riquadro: con il vetro lo porta nella proprietà --vetro, dipinto da un elemento fisso dietro a tutto (non
    /// un fondo "fixed"), con il colore pieno fuori dalla pagina e la fascia dell'oggetto del colore dato; senza (le prove, le
    /// pagine di --page) ha il fondo pieno di prima. Il riquadro dell'email ha gli angoli tondi e torna dritto se è troppo largo.
    /// </summary>
    static void VtPage()
    {
        var summary = new MessageSummary { Id = "1", FolderId = "f", Subject = "Prova", From = new Address { Name = "Mario Rossi", Email = "mario@esempio.example" } };
        UI.PageEntry Entry() => new(summary, new MessageBody { Html = "<p>ciao</p>" }, true, false, null, false);
        string with = UI.ReadingPage.Build([Entry()], true, header: new UI.PageHeader("Prova", false), back: new UI.PageBack("VETRO-CSS", "VETRO-PIENO", "VETRO-FASCIA")).Html;
        string without = UI.ReadingPage.Build([Entry()], true, header: new UI.PageHeader("Prova", false)).Html;
        string dark = UI.ReadingPage.Build([Entry()], false, header: new UI.PageHeader("Prova", false)).Html;
        bool glass = with.Contains("--vetro: VETRO-CSS;") && with.Contains("html { margin: 0; background: VETRO-PIENO;") && with.Contains("background: VETRO-FASCIA; backdrop-filter:")
                     && with.Contains("html::before { content: \"\"; position: fixed; inset: 0; z-index: -1; background: var(--vetro);") && !with.Contains("background-attachment");
        bool plain = without.Contains("--vetro: #FFFFFF;") && without.Contains("html { margin: 0; background: #FFFFFF;") && without.Contains("background: rgba(255,255,255,.70); backdrop-filter:")
                     && dark.Contains("--vetro: #292929;") && !without.Contains("VETRO");
        bool corners = with.Contains(".content { margin: 10px 0 4px 48px; display: flow-root; overflow: clip; border-radius: 12px;")
                       && with.Contains(".content.wide { overflow-x: visible; overflow-y: clip; border-radius: 0; }")
                       && (Ch1Code("UI", "ReadingView.xaml.cs") is not { } view || view.Contains("box.classList.toggle('wide', need > have + 2 && have / need < 0.5);"));
        Check(glass && plain && corners,
              $"vetro, la pagina del riquadro: con il vetro lo porta in --vetro, su un elemento fisso dietro a tutto, con il colore pieno attorno e la fascia dell'oggetto del suo colore ({glass}); " +
              $"senza ha il fondo pieno di prima, bianco o scuro ({plain}); il riquadro dell'email ha gli angoli tondi e quello troppo largo torna dritto e scorre di lato ({corners})");
    }

    /// <summary>
    /// Le schede degli account: larghe quanto serve fino a 230; se non ci stanno si tagliano le più larghe a una stessa
    /// misura, lasciando intere le strette, e mai sotto 56.
    /// </summary>
    static void VtTabs()
    {
        static bool Same(double[] a, params double[] b) => a.Length == b.Length && a.Zip(b).All(p => Math.Abs(p.First - p.Second) < 0.01);
        bool room = Same(TabStripPanel.Widths([100, 300, 80], 1000), 100, 230, 80) && Same(TabStripPanel.Widths([100, 300], double.PositiveInfinity), 100, 230);
        bool tight = Same(TabStripPanel.Widths([200, 200, 200], 320), 104, 104, 104) && Same(TabStripPanel.Widths([60, 200, 200], 320), 60, 126, 126);
        bool least = Same(TabStripPanel.Widths([200, 200, 200], 100), 56, 56, 56) && TabStripPanel.Widths([], 100).Length == 0;
        Check(room && tight && least,
              $"vetro, le schede degli account: con lo spazio ognuna larga quanto serve, al più {TabStripPanel.MaxTab} ({room}); senza, le più larghe tagliate alla stessa misura e le strette intere ({tight}); " +
              $"mai sotto {TabStripPanel.MinTab} ({least})");
    }

    /// <summary>
    /// Nei sorgenti: ogni finestra che non è la principale prende il fondo di vetro quando si carica (non i pannellini senza
    /// cornice), e nelle foto fuori schermo non l'acrilico; scrittura, Impostazioni e domande usano i pezzi del vetro; il
    /// riquadro di lettura non rende trasparente WebView2 (mostrerebbe il fondo grezzo della finestra) e ridipinge il foglio
    /// quando cambia misura.
    /// </summary>
    static void VtSources()
    {
        if (Ch1Code("UI", "App.cs") is not { } app || Ch1Code("UI", "ComposeWindow.xaml") is not { } compose || Ch1Code("UI", "SettingsWindow.cs") is not { } settings
            || Ch1Code("UI", "Dialog.cs") is not { } dialog || Ch1Code("UI", "ReadingView.xaml.cs") is not { } view || Ch1Code("UI", "ReadingView.Glass.cs") is not { } glass)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: gli agganci del vetro non sono stati letti)");
            return;
        }
        bool windows = app.Contains("EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));")
                       && app.Contains("if (sender is not Window window || window is UI.MainWindow || window.AllowsTransparency) return;") && app.Contains("Glass.Dress(window, AcrylicAllowed);")
                       && app.Contains("!DevTools.Photos;");
        bool pieces = compose.Contains("Style=\"{StaticResource GlassPanel}\"") && compose.Contains("Background=\"{DynamicResource GlassShadeBrush}\"") && compose.Contains("Background=\"{DynamicResource GlassPaperBrush}\"")
                      && settings.Contains("\"GlassShadeBrush\"") && settings.Contains("\"GlassPaperBrush\"") && dialog.Contains("\"GlassShadeBrush\"");
        bool reader = !view.Contains("Color.Transparent") && view.Contains("Paper.SizeChanged += (_, _) => OnPaperSize();") && view.Split("PageGlass(light)").Length == 3
                      && glass.Contains("style.setProperty('--vetro',");
        Check(windows && pieces && reader,
              $"vetro, nei sorgenti: ogni finestra che non è la principale prende il fondo di vetro quando si carica, senza acrilico nelle foto ({windows}); scrittura, Impostazioni e domande " +
              $"usano la fascia scura e la carta ({pieces}); il riquadro di lettura non rende trasparente WebView2, passa il vetro alle due pagine e lo rifà al cambio di misura ({reader})");
    }
}
#endif
