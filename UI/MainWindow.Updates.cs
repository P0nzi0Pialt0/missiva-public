using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Missiva.UI;

/// <summary>
/// Gli aggiornamenti da GitHub nella finestra (Updates; 15 ottobre 2026, richiesta dell'autore), solo nei rilasci: il
/// controllo due minuti dopo l'avvio e poi una volta al giorno (con la casella delle Impostazioni accesa), o con "Controlla
/// ora". Nessuna finestra da sola. Dai ritocchi del 16 ottobre 2026 (l'autore: "l'avanzamento del download dovrebbe andare
/// nel riquadro blu", la barra del titolo) tutto sta nella barra del titolo, a destra prima dei pulsanti della finestra, e
/// non più in una barra in fondo all'elenco: una versione nuova accende il pulsante "Aggiornamento", nel colore d'accento,
/// che apre un pannellino (TitleFlyout) con la versione, le novità, Aggiorna e "Più tardi" (per quella versione non torna
/// fino al giorno dopo, come la X di prima). Durante lo scaricamento il pulsante ha una barra sottile e la percentuale, e
/// il pannellino Annulla; un intoppo dopo Aggiorna, o l'aggiornamento non riuscito al riavvio, lo dice il pulsante
/// ("Aggiornamento non riuscito") e il pannellino ne dice il motivo. Aggiorna scarica e controlla, prova il nuovo
/// eseguibile, chiede conferma e poi esce come Esci (App.UpdateOnExit), con le domande delle scritture aperte. Un errore del
/// controllo automatico va solo nel registro.
/// </summary>
public partial class MainWindow
{
    DispatcherTimer? updateTimer;
    /// <summary>La versione nuova offerta nella barra del titolo (manifesto firmato e zip), o null.</summary>
    Updates.Offer? updateOffer;
    /// <summary>Lo scaricamento in corso, da fermare con Annulla; null fuori dallo scaricamento.</summary>
    CancellationTokenSource? updateDownload;
    /// <summary>Aggiorna è in corso, dallo scaricamento alla domanda: un controllo intanto non cambia il pulsante.</summary>
    bool updating;
    bool updateChecking;

    /// <summary>Che cosa dice il pulsante dell'aggiornamento nella barra del titolo.</summary>
    internal enum UpdateLook { Hidden, Offer, Downloading, Busy, Failed }

    UpdateLook updateLook;
    /// <summary>Il testo del pannellino oltre la versione: il motivo di un intoppo, che cosa sta facendo, un aggiornamento annullato.</summary>
    string? updateLine;
    /// <summary>I byte scaricati, durante lo scaricamento.</summary>
    long updateDone;
    TitleFlyout? updateFlyout;

    /// <summary>Com'è andato l'ultimo controllo, con l'ora: lo dice Impostazioni, Generali, Informazioni.</summary>
    internal static string? LastUpdateLine { get; private set; }

    /// <summary>Il controllo automatico: due minuti dopo l'avvio, poi ogni giorno, finché Missiva resta aperta. Solo nei rilasci.</summary>
    internal void StartUpdateChecks()
    {
        if (Updates.Current != Updates.Mode.On) return;
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = TimeSpan.FromDays(1);
            await CheckUpdatesAsync(automatic: true);
        };
        updateTimer.Start();
    }

    /// <summary>
    /// Un controllo: automatico (con la casella spenta nessuna richiesta, e un aggiornamento rimandato oggi non torna) o
    /// chiesto con "Controlla ora" (il pulsante torna anche se era stato rimandato). Il testo dell'esito, per le Impostazioni.
    /// </summary>
    internal async Task<string> CheckUpdatesAsync(bool automatic)
    {
        if (updateChecking) return "Un controllo è già in corso.";
        if (updating) return "Un aggiornamento è già in corso.";
        updateChecking = true;
        try
        {
            var outcome = automatic
                ? await Updates.AutomaticAsync(S.UpdatesAuto, Updates.Repository, UpdateKey.PublicKey, Updates.CurrentNumber, Updates.Http)
                : await Updates.CheckAsync(Updates.Repository, UpdateKey.PublicKey, Updates.CurrentNumber, Updates.Http);
            if (outcome.Kind != Updates.CheckKind.Off || !automatic)
                LastUpdateLine = $"{outcome.Line} (alle {DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)} del {DateTime.Now.ToString("dd'/'MM", CultureInfo.InvariantCulture)})";
            if (outcome.Offer is { } offer && !updating)
            {
                updateOffer = offer;
                if (!automatic || Updates.Shows(offer.Manifest, S.UpdateDismissed, S.UpdateDismissedOn, DateTime.Now)) ShowUpdateOffer();
            }
            return LastUpdateLine ?? outcome.Line;
        }
        catch (Exception ex)
        {
            Log.Error("controllo degli aggiornamenti", ex);
            return "Il controllo non è riuscito: " + ex.Message;
        }
        finally
        {
            updateChecking = false;
        }
    }

    /// <summary>Il pulsante con la versione offerta; note è una riga in più nel pannellino (un aggiornamento appena annullato).</summary>
    void ShowUpdateOffer(string? note = null)
    {
        if (updateOffer is not { } offer) return;
        SetUpdateLook(UpdateLook.Offer, note);
        Log.Write($"aggiornamenti: {offer.Manifest.Title} nella barra del titolo");
    }

    /// <summary>All'avvio dopo un aggiornamento non riuscito (Updates.AfterStart): il pulsante lo dice, il pannellino il motivo.</summary>
    internal void ShowUpdateFailure(string notice)
    {
        if (updating) return;
        updateOffer = null;
        SetUpdateLook(UpdateLook.Failed, notice);
    }

    /// <summary>
    /// Che cosa dice il pulsante: la versione offerta (pieno, nel colore d'accento), lo scaricamento (con la barra sottile e
    /// la percentuale), un lavoro in corso, un intoppo (nel rosso degli avvisi), o niente. Il pannellino aperto si rifà.
    /// </summary>
    void SetUpdateLook(UpdateLook look, string? line = null)
    {
        updateLook = look;
        updateLine = line;
        UpdateButton.Visibility = look == UpdateLook.Hidden ? Visibility.Collapsed : Visibility.Visible;
        UpdateProgress.Visibility = look == UpdateLook.Downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdateGlyph.Text = look switch { UpdateLook.Failed => "", UpdateLook.Downloading or UpdateLook.Busy => "", _ => "" };
        if (look == UpdateLook.Offer)
        {
            UpdateButton.SetResourceReference(BackgroundProperty, "AccentFillColorDefaultBrush");
            UpdateButton.SetResourceReference(ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        }
        else
        {
            UpdateButton.Background = Brushes.Transparent;
            UpdateButton.SetResourceReference(ForegroundProperty, look == UpdateLook.Failed ? "CriticalBrush" : "TextFillColorPrimaryBrush");
        }
        // Durante lo scaricamento il testo è largo come "Aggiornamento 100%": la ricerca non si sposta a ogni punto.
        UpdateLabel.MinWidth = look == UpdateLook.Downloading ? TextWidth(UpdateLabel, UpdateButtonText(UpdateLook.Downloading, 100)) : 0;
        RefreshUpdateProgress();
        updateFlyout?.Update(UpdatePanel());
    }

    /// <summary>La larghezza di un testo con il carattere di quel TextBlock, senza disegnarlo.</summary>
    double TextWidth(TextBlock block, string text) =>
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
                          block.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;

    /// <summary>Durante lo scaricamento, il testo e la barra del pannellino aperto (rifatti dal timer senza rifare il pannellino, che perderebbe il fuoco).</summary>
    TextBlock? updateFlyText;
    ProgressBar? updateFlyBar;

    /// <summary>Il testo del pulsante e la sua barra, durante lo scaricamento anche dal timer dell'avanzamento.</summary>
    void RefreshUpdateProgress()
    {
        int percent = UpdatePercent(updateDone, updateOffer?.Manifest.Size ?? 0);
        UpdateLabel.Text = UpdateButtonText(updateLook, percent);
        UpdateProgressScale.ScaleX = percent / 100.0;
        var tip = UpdateButtonTip(updateLook, updateOffer?.Manifest, updateDone, updateLine);
        UpdateButton.ToolTip = tip;
        AutomationProperties.SetHelpText(UpdateButton, tip);
        if (updateLook == UpdateLook.Downloading && updateOffer is { } offer)
        {
            if (updateFlyText != null) updateFlyText.Text = UpdateProgressText(offer.Manifest.Number, updateDone, offer.Manifest.Size);
            if (updateFlyBar != null) updateFlyBar.Value = percent;
        }
    }

    /// <summary>La percentuale scaricata, da 0 a 100 (100 solo a scaricamento finito).</summary>
    internal static int UpdatePercent(long done, long total) => total <= 0 ? 0 : (int)Math.Clamp(done * 100 / total, 0, 100);

    /// <summary>Il testo del pulsante: "Aggiornamento", "Aggiornamento 42%", "Aggiornamento non riuscito".</summary>
    internal static string UpdateButtonText(UpdateLook look, int percent) => look switch
    {
        UpdateLook.Downloading => $"Aggiornamento {percent}%",
        UpdateLook.Busy => "Aggiornamento…",
        UpdateLook.Failed => "Aggiornamento non riuscito",
        _ => "Aggiornamento",
    };

    /// <summary>Il suggerimento del pulsante, che dice anche con la finestra stretta (solo l'icona) che cosa c'è.</summary>
    internal static string UpdateButtonTip(UpdateLook look, Updates.Manifest? manifest, long done, string? line) => look switch
    {
        UpdateLook.Offer when manifest != null => $"È disponibile {manifest.Title}: le novità e Aggiorna",
        UpdateLook.Downloading when manifest != null => UpdateProgressText(manifest.Number, done, manifest.Size),
        UpdateLook.Failed => "Aggiornamento non riuscito" + (line is { Length: > 0 } ? ": " + line : ""),
        _ => line ?? "Aggiornamento di Missiva",
    };

    /// <summary>Le novità di un manifesto, una riga per punto.</summary>
    internal static string UpdateNews(Updates.Manifest manifest) =>
        manifest.News.Count == 0 ? "Il rilascio non dice che cosa cambia." : string.Join("\n", manifest.News.Select(line => "• " + line));

    /// <summary>"Scarico Missiva 0.3.530: 12,4 di 98,0 MB", nel pannellino e nel suggerimento durante lo scaricamento.</summary>
    internal static string UpdateProgressText(Version number, long done, long total) =>
        $"Scarico Missiva {number}: {done / 1048576.0:0.0} di {total / 1048576.0:0.0} MB";

    /// <summary>Il clic sul pulsante: il pannellino, che si apre e si chiude.</summary>
    void OnUpdateButton(object sender, RoutedEventArgs e)
    {
        updateFlyout ??= new TitleFlyout(this, UpdateButton, "Aggiornamento di Missiva", 380);
        updateFlyout.Toggle(UpdatePanel);
    }

    /// <summary>
    /// Il pannellino: la versione ("Missiva 0.3.N del gg/mm/aaaa"), poi secondo lo stato le novità con Aggiorna e Più tardi,
    /// lo scaricamento con Annulla, che cosa sta facendo, o il motivo dell'intoppo (con Riprova se la versione c'è ancora).
    /// </summary>
    UIElement UpdatePanel()
    {
        updateFlyText = null;
        updateFlyBar = null;
        var panel = new StackPanel();
        var manifest = updateOffer?.Manifest;
        panel.Children.Add(new TextBlock
        {
            Text = updateLook == UpdateLook.Failed && manifest == null ? "Aggiornamento non riuscito" : manifest?.Title ?? "Aggiornamento di Missiva",
            FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });
        TextBlock Text(string text, bool secondary = false)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            if (secondary) block.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            return block;
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        void Add(string label, RoutedEventHandler click, bool accent = false, string? help = null)
        {
            var button = new Button { Content = label, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
            if (accent) button.SetResourceReference(StyleProperty, "AccentButtonStyle");
            if (help != null) button.ToolTip = help;
            button.Click += click;
            buttons.Children.Add(button);
        }
        switch (updateLook)
        {
            case UpdateLook.Offer when manifest != null:
                panel.Children.Add(Text("Novità", secondary: true));
                panel.Children.Add(Text(UpdateNews(manifest)));
                if (updateLine is { Length: > 0 } note) panel.Children.Add(Text(note, secondary: true));
                Add("Più tardi", OnUpdateLater, help: "Per questa versione il pulsante torna domani");
                Add("Aggiorna", OnUpdateNow, accent: true, help: "Scarica la versione nuova e la controlla; poi, dopo una domanda, Missiva si chiude e si riapre aggiornata");
                break;
            case UpdateLook.Downloading when manifest != null:
                panel.Children.Add(updateFlyText = Text(UpdateProgressText(manifest.Number, updateDone, manifest.Size)));
                panel.Children.Add(updateFlyBar = new ProgressBar { Height = 4, Maximum = 100, Value = UpdatePercent(updateDone, manifest.Size), Margin = new Thickness(0, 0, 0, 8) });
                AutomationProperties.SetName(updateFlyBar, "Avanzamento dello scaricamento dell'aggiornamento");
                Add("Annulla", OnUpdateCancel, help: "Ferma lo scaricamento: Missiva resta com'è");
                break;
            case UpdateLook.Busy:
                panel.Children.Add(Text(updateLine ?? "Un momento…"));
                break;
            case UpdateLook.Failed:
                var why = Text(updateLine ?? "Il motivo non si conosce.");
                why.SetResourceReference(TextBlock.ForegroundProperty, "CriticalBrush");
                panel.Children.Add(why);
                if (manifest != null)
                {
                    Add("Più tardi", OnUpdateLater, help: "Per questa versione il pulsante torna domani");
                    Add("Riprova", OnUpdateNow, accent: true, help: "Scarica di nuovo la versione nuova e la controlla");
                }
                else Add("Chiudi", OnUpdateLater);
                break;
        }
        if (buttons.Children.Count > 0) panel.Children.Add(buttons);
        return panel;
    }

    /// <summary>
    /// Più tardi (prima la X della barra): il pulsante se ne va, e per quella versione non torna fino al giorno dopo. Dopo
    /// un aggiornamento non riuscito al riavvio, senza versione offerta, Chiudi lo toglie e basta.
    /// </summary>
    void OnUpdateLater(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        updateFlyout?.Close("Più tardi");
        SetUpdateLook(UpdateLook.Hidden);
        if (updateOffer is not { } offer) return;
        S.UpdateDismissed = offer.Manifest.Number.ToString();
        S.UpdateDismissedOn = DateTime.Today;
        Store.Save();
        Log.Write($"aggiornamenti: {offer.Manifest.Number} rimandato, torna domani");
    }

    /// <summary>
    /// Aggiorna. Nella versione di prova scrive solo che cosa farebbe. Altrimenti scarica e controlla (Updates.DownloadAsync),
    /// prova il nuovo eseguibile, chiede conferma ed esce come Esci; a ogni intoppo il pulsante lo dice e Missiva resta com'è.
    /// </summary>
    void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        if (updateOffer is not { } offer || updating) return;
#if DEBUG || PROVA
        Log.Write($"prova: aggiornerei Missiva da {AppVersion.Number} a {offer.Manifest.Number} (scaricherei {offer.Manifest.FileName}, {offer.Manifest.Size} byte, da {offer.ZipUrl.Host})");
#else
        _ = Log.GuardAsync("aggiornamento di Missiva", () => UpdateNowAsync(offer));
#endif
    }

    async Task UpdateNowAsync(Updates.Offer offer)
    {
        var number = offer.Manifest.Number;
        updating = true;
        try
        {
            using var cancel = updateDownload = new CancellationTokenSource();
            updateDone = 0;
            SetUpdateLook(UpdateLook.Downloading);
            // Il testo e la barra si rifanno al più ogni 150 ms, non a ogni blocco di 80 KB.
            var ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            ticker.Tick += (_, _) => { if (updateLook == UpdateLook.Downloading) RefreshUpdateProgress(); };
            ticker.Start();
            var progress = new Progress<long>(done => updateDone = done);
            Updates.Prepared prepared;
            try
            {
                prepared = await Updates.DownloadAsync(offer, Updates.CurrentNumber, Path.Combine(Store.Dir, Updates.FolderName), Updates.Http, progress, cancel.Token);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                ShowUpdateOffer();
                return;
            }
            catch (Exception ex)
            {
                SetUpdateLook(UpdateLook.Failed, ex is Updates.Refused
                    ? $"Missiva {number} non è stata accettata: {ex.Message}"
                    : $"Lo scaricamento di Missiva {number} non è riuscito: {ex.Message}");
                return;
            }
            finally
            {
                ticker.Stop();
                updateDownload = null;
            }
            SetUpdateLook(UpdateLook.Busy, $"Provo Missiva {number}…");
            if (await Updates.TryRunAsync(prepared) is { } problem)
            {
                Log.Write($"aggiornamenti: il nuovo eseguibile {number} non parte ({problem}): resta {AppVersion.Number}");
                Updates.Remove(prepared.Folder);
                SetUpdateLook(UpdateLook.Failed, $"Missiva {number} è stata scaricata ma su questo PC non parte ({problem}): resta questa.");
                return;
            }
            updateFlyout?.Close("domanda di conferma");
            if (Dialog.Ask(this, $"Aggiornare a Missiva {number}?",
                           "Missiva si chiude e si riapre aggiornata. Se stai scrivendo un messaggio, prima ti chiede se salvare la bozza.", "Aggiorna") != Choice.Primary)
            {
                Updates.Remove(prepared.Folder);
                ShowUpdateOffer();
                return;
            }
            SetUpdateLook(UpdateLook.Busy, $"Aggiorno a Missiva {number}…");
            App.Current.UpdateOnExit(prepared);
        }
        finally
        {
            updating = false;
        }
    }

    void OnUpdateCancel(object sender, RoutedEventArgs e) => updateDownload?.Cancel();

    /// <summary>L'uscita per l'aggiornamento è stata annullata (Annulla in una domanda di una scrittura): Missiva resta com'è.</summary>
    internal void UpdateCancelled(Updates.Prepared prepared)
    {
        Updates.Remove(prepared.Folder);
        ShowUpdateOffer($"Aggiornamento a Missiva {prepared.Manifest.Number} annullato: resta questa.");
    }

    // ---------------------------------------------------------------- la barra del titolo

    void OnTitleToolsSize(object sender, SizeChangedEventArgs e) => FitTitleBar();

    /// <summary>
    /// La barra del titolo nella larghezza della finestra: i pulsanti a destra subito prima di quelli di Windows, la ricerca
    /// al centro senza coprire il nome né i pulsanti (OnWindowSize). Se con il nome dell'aggiornamento la ricerca resterebbe
    /// più stretta di 280 DIP (la finestra stretta, fino a 720), del pulsante resta l'icona, e il testo va nel suggerimento.
    /// </summary>
    void FitTitleBar()
    {
        double caption = CaptionButtonsWidth();
        var margin = new Thickness(0, 11, caption + 6, 0);
        if (TitleTools.Margin != margin) TitleTools.Margin = margin;
        double left = Brand.ActualWidth + 28;
        bool narrow = UpdateButton.Visibility == Visibility.Visible && TitleSearchWidth(left, caption, TitleToolsWidth(label: true)) < 280;
        var label = narrow ? Visibility.Collapsed : Visibility.Visible;
        if (UpdateLabel.Visibility != label) UpdateLabel.Visibility = label;
        SearchArea.Width = Math.Clamp(TitleSearchWidth(left, caption, TitleToolsWidth(label: !narrow)), 200, 520);
    }

    /// <summary>La ricerca resta al centro: tanto spazio quanto il lato più largo, il nome a sinistra o pulsanti e strumenti a destra.</summary>
    double TitleSearchWidth(double left, double caption, double tools) => ActualWidth - 2 * Math.Max(left, caption + 12 + (tools > 0 ? tools + 6 : 0));

    /// <summary>
    /// La larghezza dei pulsanti della barra del titolo come li ha misurati l'ultimo giro di disposizione, con o senza il
    /// nome dell'aggiornamento: quello del nome si calcola dal testo (TextWidth), senza cambiare nulla di quello che si vede.
    /// </summary>
    double TitleToolsWidth(bool label)
    {
        double width = 0;
        foreach (var child in TitleTools.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible))
        {
            width += child.DesiredSize.Width;
            if (child != UpdateButton) continue;
            if (UpdateLabel.Visibility == Visibility.Visible) width -= UpdateLabel.DesiredSize.Width;
            if (label) width += Math.Max(UpdateLabel.MinWidth, TextWidth(UpdateLabel, UpdateLabel.Text)) + UpdateLabel.Margin.Left + UpdateLabel.Margin.Right;
        }
        return width;
    }

#if DEBUG
    /// <summary>
    /// Prove e foto ("aggiornamento offerta|scarica N|prova|errore|pannello|via" in MISSIVA_KEYS): il pulsante in ogni stato
    /// con una versione finta (0.3.999 di oggi, con tre novità), senza rete e senza scaricare nulla.
    /// </summary>
    internal void TestUpdate(string what)
    {
        updateOffer ??= new Updates.Offer(new Updates.Manifest(new Version(0, 3, 999), "0.3.999 di prova", DateOnly.FromDateTime(DateTime.Today), "0000000", "Missiva-0.3.999.zip",
            102_760_448, new string('0', 64), ["Il numero sull'icona a scelta", "L'aggiornamento nella barra del titolo", "Gli scaricamenti degli allegati"]),
            new Uri("https://example.com/Missiva-0.3.999.zip"));
        var parts = what.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0])
        {
            case "offerta":
                ShowUpdateOffer();
                break;
            case "scarica":
                updateDone = updateOffer.Manifest.Size * (parts.Length > 1 && int.TryParse(parts[1], out var percent) ? percent : 42) / 100;
                SetUpdateLook(UpdateLook.Downloading);
                break;
            case "prova":
                SetUpdateLook(UpdateLook.Busy, $"Provo Missiva {updateOffer.Manifest.Number}…");
                break;
            case "errore":
                SetUpdateLook(UpdateLook.Failed, $"Lo scaricamento di Missiva {updateOffer.Manifest.Number} non è riuscito: la connessione si è interrotta.");
                break;
            case "pannello":
                OnUpdateButton(this, new RoutedEventArgs());
                break;
            case "via":
                updateFlyout?.Close("prova");
                SetUpdateLook(UpdateLook.Hidden);
                break;
        }
        Log.Write($"prova: aggiornamento {what}");
    }
#endif
}
