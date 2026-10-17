using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Missiva.UI;

/// <summary>
/// Il gestore degli scaricamenti nella barra del titolo (ritocchi del 16 ottobre 2026, richiesta dell'autore: "non si vede se
/// il download è stato avviato, terminato, né l'avanzamento"), accanto all'aggiornamento, come in un browser. L'indicatore
/// c'è da quando c'è un elemento in questa sessione (DownloadList.Session); mentre qualcosa scarica un anello attorno
/// all'icona dice quanto manca (gira se di uno non si sa la dimensione) e, con più di uno, quanti sono; quando uno finisce,
/// per qualche secondo un segno di fatto; uno non riuscito resta segnato finché non si apre il pannello. Il pannello
/// (TitleFlyout) ha gli scaricamenti, i più recenti in cima, con l'icona del tipo (FileKinds), nome, stato e le azioni:
/// Annulla per quelli in corso, Apri e Mostra nella cartella per quelli finiti (Apri passa dalle stesse domande del
/// riquadro; per un file aperto con un programma, che sta fra i temporanei di Missiva e se ne va un'ora dopo, Mostra nella
/// cartella non c'è), e in fondo "Cancella l'elenco".
/// </summary>
public partial class MainWindow
{
    TitleFlyout? downloadsFlyout;
    /// <summary>Mentre qualcosa scarica, l'anello e le righe del pannello si rifanno al più ogni 150 ms, non a ogni blocco arrivato.</summary>
    DispatcherTimer? downloadsTicker;
    /// <summary>Fino a quando l'indicatore mostra il segno di fatto.</summary>
    DateTime downloadsDoneUntil;
    /// <summary>Le righe del pannello aperto per gli elementi in corso: testo e barra, rifatti dal timer senza rifare il pannello.</summary>
    readonly Dictionary<DownloadItem, (TextBlock Status, ProgressBar Bar)> downloadRows = new();
    Storyboard? downloadsSpin;

    void WatchDownloads()
    {
        DownloadList.Session.Changed += OnDownloadsChanged;
        DownloadList.Session.Finished += OnDownloadFinished;
    }

    void UnwatchDownloads()
    {
        DownloadList.Session.Changed -= OnDownloadsChanged;
        DownloadList.Session.Finished -= OnDownloadFinished;
    }

    void OnDownloadsChanged()
    {
        if (Dispatcher.CheckAccess()) RefreshDownloads(rebuild: true);
        else Dispatcher.BeginInvoke(() => RefreshDownloads(rebuild: true));
    }

    void OnDownloadFinished(DownloadItem item)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnDownloadFinished(item));
            return;
        }
        downloadsDoneUntil = DateTime.UtcNow.AddSeconds(3);
        RefreshDownloads(rebuild: false);
        // Passati i tre secondi l'indicatore torna com'era: un giro del timer anche se nulla scarica più.
        var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.1) };
        back.Tick += (_, _) => { back.Stop(); RefreshDownloads(rebuild: false); };
        back.Start();
    }

    /// <summary>L'indicatore (e il pannello aperto: rifatto se è cambiato un elemento, altrimenti solo testi e barre).</summary>
    void RefreshDownloads(bool rebuild)
    {
        var list = DownloadList.Session;
        var items = list.Items;
        DownloadsButton.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var (active, fraction) = list.Overall();
        var look = DownloadsLook(active, DateTime.UtcNow < downloadsDoneUntil, items.Any(i => i.State == DownloadState.Failed && !i.Seen));
        DownloadsTrack.Visibility = DownloadsArc.Visibility = look == DownloadsState.Running ? Visibility.Visible : Visibility.Collapsed;
        DownloadsGlyph.FontSize = look == DownloadsState.Running ? 11 : 15;
        DownloadsGlyph.Text = look switch { DownloadsState.Done => "", DownloadsState.Failed => "", _ => "" };
        if (look == DownloadsState.Done) DownloadsGlyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        else if (look == DownloadsState.Failed) DownloadsGlyph.SetResourceReference(TextBlock.ForegroundProperty, "CriticalBrush");
        else DownloadsGlyph.ClearValue(TextBlock.ForegroundProperty);
        DownloadsCount.Visibility = active > 1 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsCountText.Text = active.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SetDownloadsRing(look == DownloadsState.Running ? fraction : 0);
        var tip = DownloadsTip(items);
        DownloadsButton.ToolTip = tip;
        AutomationProperties.SetHelpText(DownloadsButton, tip);
        // Il timer gira solo mentre qualcosa scarica.
        if (active > 0)
        {
            downloadsTicker ??= NewDownloadsTicker();
            downloadsTicker.Start();
        }
        else downloadsTicker?.Stop();
        if (downloadsFlyout is { IsOpen: true } flyout)
        {
            if (items.Count == 0) flyout.Close("elenco vuoto");
            else if (rebuild) flyout.Update(DownloadsPanel());
            else RefreshDownloadRows();
        }
    }

    DispatcherTimer NewDownloadsTicker()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => RefreshDownloads(rebuild: false);
        return timer;
    }

    /// <summary>Che cosa mostra l'indicatore.</summary>
    internal enum DownloadsState { Idle, Running, Done, Failed }

    /// <summary>In ordine: qualcosa che scarica, uno appena finito (per qualche secondo), uno non riuscito non ancora visto nel pannello.</summary>
    internal static DownloadsState DownloadsLook(int active, bool justDone, bool failedUnseen) =>
        active > 0 ? DownloadsState.Running : justDone ? DownloadsState.Done : failedUnseen ? DownloadsState.Failed : DownloadsState.Idle;

    /// <summary>Il suggerimento dell'indicatore: quanti in corso e quanto manca, o l'ultimo com'è andato.</summary>
    internal static string DownloadsTip(IReadOnlyList<DownloadItem> items)
    {
        var active = items.Where(i => i.Active).ToList();
        if (active.Count == 1) return $"Scaricamenti: {active[0].Name}, {active[0].Status}";
        if (active.Count > 1) return $"Scaricamenti: {active.Count} in corso";
        return items.Count > 0 ? $"Scaricamenti: {items[0].Name}, {items[0].Status}" : "Scaricamenti";
    }

    /// <summary>L'anello attorno all'icona: la parte fatta, da mezzogiorno in senso orario; null, un quarto che gira.</summary>
    void SetDownloadsRing(double? fraction)
    {
        const double size = 26, radius = 12;
        double part = fraction ?? 0.25;
        part = Math.Clamp(part, 0.001, 0.999);
        double angle = part * 2 * Math.PI;
        var start = new Point(size / 2, size / 2 - radius);
        var end = new Point(size / 2 + radius * Math.Sin(angle), size / 2 - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start }; // aperta: è un arco (di base una figura non si chiude)
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, part > 0.5, SweepDirection.Clockwise, true));
        DownloadsArc.Data = new PathGeometry([figure]);
        bool spin = fraction == null && DownloadsArc.Visibility == Visibility.Visible;
        if (spin && downloadsSpin == null)
        {
            var turn = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTargetName(turn, nameof(DownloadsSpin));
            Storyboard.SetTargetProperty(turn, new PropertyPath(RotateTransform.AngleProperty));
            downloadsSpin = new Storyboard { Children = { turn } };
            downloadsSpin.Begin(this, true);
        }
        else if (!spin && downloadsSpin != null)
        {
            downloadsSpin.Stop(this);
            downloadsSpin = null;
            DownloadsSpin.Angle = 0;
        }
    }

    /// <summary>Il clic sull'indicatore: il pannello, che si apre e si chiude. Gli errori si considerano visti.</summary>
    void OnDownloadsButton(object sender, RoutedEventArgs e)
    {
        foreach (var item in DownloadList.Session.Items.Where(i => i.State == DownloadState.Failed)) item.Seen = true;
        downloadsFlyout ??= new TitleFlyout(this, DownloadsButton, "Scaricamenti", 420);
        downloadsFlyout.Toggle(DownloadsPanel);
        RefreshDownloads(rebuild: false);
    }

    /// <summary>Il pannello: il titolo, una riga per elemento (i più recenti in cima) e in fondo "Cancella l'elenco".</summary>
    UIElement DownloadsPanel()
    {
        downloadRows.Clear();
        var items = DownloadList.Session.Items;
        var panel = new DockPanel();
        var title = new TextBlock { Text = "Scaricamenti", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);
        var clear = new Button { Content = "Cancella l'elenco", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), MinWidth = 120,
                                 IsEnabled = items.Any(i => !i.Active), ToolTip = "Toglie dall'elenco gli scaricamenti finiti, non riusciti e annullati: i file restano dove sono" };
        clear.Click += (_, _) => DownloadList.Session.Clear();
        DockPanel.SetDock(clear, Dock.Bottom);
        panel.Children.Add(clear);
        var rows = new StackPanel();
        AutomationProperties.SetName(rows, "Elenco degli scaricamenti");
        foreach (var item in items) rows.Children.Add(DownloadRow(item));
        panel.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) });
        return panel;
    }

    /// <summary>Una riga: l'icona del tipo, il nome, lo stato (con la barra se è in corso) e le azioni.</summary>
    FrameworkElement DownloadRow(DownloadItem item)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var kind = FileKinds.Of(item.Name);
        var icon = new TextBlock
        {
            Text = FileKinds.Glyph(kind), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(FileKinds.GlyphColor(kind))),
        };
        grid.Children.Add(icon);
        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        text.Children.Add(new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Path ?? item.Name });
        var status = new TextBlock { Text = item.Status, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, item.State == DownloadState.Failed ? "CriticalBrush" : "SecondaryTextBrush");
        text.Children.Add(status);
        if (item.Active)
        {
            // In coda solo il testo; in corso la barra, che gira se non si sa quanto manca.
            var bar = new ProgressBar { Height = 4, Maximum = 100, Margin = new Thickness(0, 5, 0, 2), IsIndeterminate = item.Percent == null, Value = item.Percent ?? 0,
                                        Visibility = item.State == DownloadState.Running ? Visibility.Visible : Visibility.Collapsed };
            AutomationProperties.SetName(bar, "Avanzamento di " + item.Name);
            text.Children.Add(bar);
            downloadRows[item] = (status, bar);
        }
        var actions = new WrapPanel { Margin = new Thickness(-8, 2, 0, 0) };
        Button Action(string label, string help, Action click)
        {
            var button = new Button
            {
                Content = label, Template = (ControlTemplate)FindResource("SubtleButton"), Background = Brushes.Transparent, Padding = new Thickness(8, 3, 8, 3),
                FontSize = 12, FontWeight = FontWeights.SemiBold, ToolTip = help,
            };
            button.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            AutomationProperties.SetName(button, $"{label}: {item.Name}");
            button.Click += (_, _) => click();
            actions.Children.Add(button);
            return button;
        }
        if (item.Active) Action("Annulla", "Ferma lo scaricamento: nessun file resta a metà", item.Cancel);
        else if (item.State == DownloadState.Done && !item.Gone)
        {
            Action("Apri", "Apre il file, con le stesse domande del riquadro per i tipi che possono avviare programmi e per le pagine web", () => OpenDownloaded(item));
            if (!item.Temporary) Action("Mostra nella cartella", "Apre Esplora file sulla cartella del file, con il file scelto", () => ShowDownloaded(item));
        }
        if (actions.Children.Count > 0) text.Children.Add(actions);
        AutomationProperties.SetName(grid, $"{item.Name}, {item.Status}");
        return grid;
    }

    /// <summary>Durante lo scaricamento: lo stato e la barra delle righe in corso, senza rifare il pannello (e senza perdere il fuoco).</summary>
    void RefreshDownloadRows()
    {
        foreach (var (item, row) in downloadRows)
        {
            row.Status.Text = item.Status;
            if (item.Percent is int percent && item.State == DownloadState.Running)
            {
                row.Bar.IsIndeterminate = false;
                row.Bar.Value = percent;
            }
        }
    }

    /// <summary>
    /// Apri, dal pannello: il file scaricato, con le stesse domande di Apri nel riquadro (un tipo che può avviare programmi, una
    /// pagina web, un file temporaneo senza marchio web). Un .p7m e un .eml salvati si riaprono in Missiva, come dal riquadro.
    /// Se il file non c'è più, la riga lo dice.
    /// </summary>
    void OpenDownloaded(DownloadItem item)
    {
        downloadsFlyout?.Close("Apri");
        if (item.Path is not { } downloaded || !File.Exists(downloaded))
        {
            item.Gone = true;
            Log.Write($"scaricamenti: {Log.Private(item.Name)} non c'è più dov'era");
            RefreshDownloads(rebuild: true);
            return;
        }
        var name = Path.GetFileName(downloaded);
        try
        {
            if (P7m.IsP7m(name))
            {
                _ = P7mWindow.ShowAsync(this, File.ReadAllBytes(downloaded), name);
                return;
            }
            if (name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) && (Providers.FirstOrDefault(p => p.Account.Id == item.AccountId) ?? Providers.FirstOrDefault()) is { } provider)
            {
                OpenEml(provider, File.ReadAllBytes(downloaded), name, item.FromJunk);
                return;
            }
            if (Safety.IsDangerous(name) && !Dialog.ConfirmOpenDangerous(this, name)) return;
            if (!Dialog.ConfirmOpenWebPage(this, name)) return;
            if (item.Temporary && !item.Marked && !Dialog.ConfirmOpenUnmarked(this, name)) return;
            Process.Start(new ProcessStartInfo(downloaded) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("scaricamenti, Apri", ex);
            Dialog.Show(this, "File non aperto", ex.Message);
        }
    }

    /// <summary>Mostra nella cartella: Esplora file sulla cartella del file, con il file scelto (Safety.ShowInFolder, dal percorso intero di Esplora file).</summary>
    void ShowDownloaded(DownloadItem item)
    {
        downloadsFlyout?.Close("Mostra nella cartella");
        if (item.Path is not { } path || !File.Exists(path))
        {
            item.Gone = true;
            RefreshDownloads(rebuild: true);
            return;
        }
        Safety.ShowInFolder(path);
    }

#if DEBUG
    /// <summary>
    /// Prove e foto ("scaricamenti prova|pannello|chiudi" in MISSIVA_KEYS): elementi finti in ogni stato, senza rete e senza
    /// scrivere altro che due file di prova nella cartella dei dati; "pannello" apre il pannello.
    /// </summary>
    internal void TestDownloads(string what)
    {
        var list = DownloadList.Session;
        switch (what)
        {
            case "prova":
                // Dal più vecchio al più recente: in cima finiscono quelli in corso. I file stanno in una cartella della prova.
                var folder = Path.Combine(Store.Dir, "scaricamenti-di-prova");
                Directory.CreateDirectory(folder);
                list.Add("Preventivo cucina.xlsx", 48_000, temporary: false).Fail("Non riesco a scaricare l'allegato (il server ha risposto 503).");
                list.Add("Foto sopralluogo.zip", 24_000_000, temporary: false).Stop();
                _ = list.SaveAsync(Path.Combine(folder, "Capitolato.docx"), 412_000, (_, _) => Task.FromResult(new byte[412_000]));
                _ = TestTempAsync(list, folder);
                list.Add("Tavola esecutiva.pdf", 2_936_013, temporary: false).Report(1_258_291);
                list.Add("Computo metrico.pdf", 1_100_000, temporary: false, waiting: true);
                break;
            case "fatto": // uno appena finito: il segno di fatto per qualche secondo
                var done = Path.Combine(Store.Dir, "scaricamenti-di-prova");
                Directory.CreateDirectory(done);
                _ = list.SaveAsync(Path.Combine(done, "Verbale.pdf"), 64_000, (_, _) => Task.FromResult(new byte[64_000]));
                break;
            case "errore": // uno non riuscito, non ancora visto nel pannello
                list.Add("Preventivo cucina.xlsx", 48_000, temporary: false).Fail("la connessione si è interrotta.");
                break;
            case "pannello":
                OnDownloadsButton(this, new RoutedEventArgs());
                break;
            case "chiudi":
                downloadsFlyout?.Close("prova");
                break;
        }
        RefreshDownloads(rebuild: true);
        Log.Write($"prova: scaricamenti {what}");
    }

    /// <summary>Un allegato "aperto con un programma" finto: scritto con Safety.WriteTemp nella cartella della prova, rinominato, mai aperto.</summary>
    static async Task TestTempAsync(DownloadList list, string folder)
    {
        var (item, temp) = await list.FetchToTempAsync("Ricevuta.pdf", 96_000, (_, _) => Task.FromResult(new byte[96_000]), root: Path.Combine(folder, "temporanei"));
        if (temp == null) return;
        temp.Release();
        list.Opened(item, temp);
    }
#endif
}
