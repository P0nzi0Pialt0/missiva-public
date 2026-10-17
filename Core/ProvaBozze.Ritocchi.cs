#if DEBUG
using System.Diagnostics;
using System.Text;

namespace Missiva;

/// <summary>
/// Ritocchi del 16 ottobre 2026, chiesti dall'autore: la rotellina che apre subito le Impostazioni, il numero sull'icona di
/// Missiva a scelta, l'aggiornamento nella barra del titolo e il gestore degli scaricamenti degli allegati. Le finestre non
/// si aprono: le parti di WPF si leggono dai sorgenti senza i commenti, il resto con funzioni pure, server IMAP finti e
/// gestori HTTP finti. Nessuna rete. Con "Missiva.exe --prova-audit risultato.txt ritocchi" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static async Task TouchUpsAsync()
    {
        Report.Add("--- Ritocchi del 16 ottobre: rotellina, numero sull'icona, aggiornamento nella barra del titolo, scaricamenti");
        RitSettingsButton();
        RitIconNumber();
        RitIconNumberSources();
        await RitGraphProgressAsync();
        await RitImapProgressAsync();
        await RitGmailProgressAsync();
        RitUpdateTexts();
        RitUpdateSources();
        RitDownloadTexts();
        RitDownloadListLimit();
        await RitDownloadsGraphAsync();
        await RitDownloadsImapAsync();
        RitDownloadsIndicator();
        RitDownloadsSources();
    }

    // ---------------------------------------------------------------- la rotellina

    /// <summary>
    /// La rotellina in fondo alla colonna delle app apre subito le Impostazioni, come in Outlook: prima un menu con
    /// Impostazioni, Aggiungi account e Apri la cartella dei registri. Aggiungi account resta in fondo alle cartelle e in
    /// Impostazioni, Account; la cartella dei registri è un pulsante di Impostazioni, Generali, Informazioni.
    /// </summary>
    static void RitSettingsButton()
    {
        if (BehSource("UI", "MainWindow.xaml") is not { } xaml || Ch1Code("UI", "MainWindow.xaml.cs") is not { } main || Ch1Code("UI", "SettingsWindow.cs") is not { } settings)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: la rotellina non è stata letta)");
            return;
        }
        // Il pulsante: suggerimento e nome per UI Automation "Impostazioni", e il clic che apre le Impostazioni senza menu.
        var button = System.Text.RegularExpressions.Regex.Match(xaml, "<Button x:Name=\"SettingsButton\"[^>]*>").Value;
        bool named = button.Contains("ToolTip=\"Impostazioni\"") && button.Contains("AutomationProperties.Name=\"Impostazioni\"") && button.Contains("Click=\"OnSettings\"");
        string handler = Ch3Part(main, "void OnSettings(object sender, RoutedEventArgs e)", "\n}");
        bool direct = handler.Contains("=> OpenSettings();") && !main.Contains("OnSettingsMenu") && !main.Contains("\"Aggiungi account…\"")
                      && !main.Contains("\"Apri la cartella dei registri\"");
        // Aggiungi account resta in fondo alle cartelle (con il suo nome) e in Impostazioni, Account.
        bool addKept = System.Text.RegularExpressions.Regex.IsMatch(xaml, "<Button DockPanel.Dock=\"Bottom\"[^>]*Click=\"OnAddAccount\" AutomationProperties.Name=\"Aggiungi account\">")
                       && Ch3Part(settings, "StackPanel AccountsPage()", "\n    }").Contains("addAccount()");
        // La cartella dei registri in Informazioni, come pulsante, con lo stesso avvio di prima (la cartella dei dati, dalla shell).
        string about = Ch3Part(settings, "StackPanel AboutPage()", "\n    }");
        bool logs = Ch3InOrder(about, "Section(\"Registri\")", "Content = \"Apri la cartella dei registri\"",
                               "logs.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Store.Dir) { UseShellExecute = true });", "p.Children.Add(logs);");
        Check(named && direct && addKept && logs,
              "la rotellina apre subito le Impostazioni, senza menu (suggerimento e nome per UI Automation \"Impostazioni\"); Aggiungi account resta in fondo alle cartelle e in Impostazioni, Account; " +
              $"\"Apri la cartella dei registri\" è un pulsante di Impostazioni, Generali, Informazioni [{named} {direct} {addKept} {logs}]");
    }

    // ---------------------------------------------------------------- il numero sull'icona

    /// <summary>
    /// Il numero sull'icona (IconCount, FreshMail): le tre scelte, gli account esclusi che valgono per tutte e due le scelte
    /// con il numero, i messaggi nuovi che si azzerano quando la finestra principale diventa attiva e non contano mentre la
    /// si guarda, e il suggerimento dell'area di notifica con la stessa regola. Funzioni pure, nessuna finestra.
    /// </summary>
    static void RitIconNumber()
    {
        static MessageSummary Mail(string id, string folder = "INBOX") => new() { Id = id, FolderId = folder, Subject = id };
        (string, int)[] inboxes = [("studio", 3), ("casa", 5), ("pec", 2), ("studio", 1)]; // studio ha due Posta in arrivo (Evidenziata e Altra)
        var fresh = new FreshMail();
        fresh.Add("studio", [Mail("1"), Mail("2")], looking: false);
        fresh.Add("casa", [Mail("7")], looking: false);
        fresh.Add("studio", [Mail("2")], looking: false); // annunciato di nuovo: conta una volta
        fresh.Add("studio", [Mail("2", "Lavoro")], looking: false); // lo stesso identificativo in un'altra cartella è un altro messaggio
        fresh.Add("pec", [Mail("9")], looking: true); // arrivato mentre si guardava la finestra principale
        string[] none = [], noHome = ["casa"];
        int unreadAll = IconCount.Count(IconNumberKind.Unread, inboxes, fresh, none), unreadSome = IconCount.Count(IconNumberKind.Unread, inboxes, fresh, noHome);
        int newAll = IconCount.Count(IconNumberKind.New, inboxes, fresh, none), newSome = IconCount.Count(IconNumberKind.New, inboxes, fresh, noHome);
        int off = IconCount.Count(IconNumberKind.None, inboxes, fresh, none);
        Check(unreadAll == 11 && unreadSome == 6 && newAll == 4 && newSome == 3 && off == 0,
              $"numero sull'icona, le tre scelte: da leggere nelle Posta in arrivo {unreadAll} (senza l'account escluso {unreadSome}), nuovi da quando si è guardato Missiva {newAll} " +
              $"(uno annunciato due volte conta una, quello arrivato mentre si guardava la finestra no; senza l'account escluso {newSome}), nessun numero {off}");

        bool cleared = fresh.Clear();
        int afterLook = IconCount.Count(IconNumberKind.New, inboxes, fresh, none);
        bool clearedAgain = fresh.Clear();
        fresh.Add("casa", [Mail("8")], looking: false);
        Check(cleared && afterLook == 0 && !clearedAgain && IconCount.Count(IconNumberKind.New, inboxes, fresh, none) == 1 && IconCount.Count(IconNumberKind.Unread, inboxes, fresh, none) == 11,
              "numero sull'icona, i messaggi nuovi si azzerano quando la finestra principale diventa attiva (FreshMail.Clear, che dice se c'era qualcosa: senza, il numero non si rifà) e ripartono con il prossimo arrivo; " +
              "i da leggere non cambiano");

        // La stessa regola per il cerchio, la descrizione dell'icona e il suggerimento dell'area di notifica.
        string?[] badges = [IconCount.Badge(0), IconCount.Badge(7), IconCount.Badge(99), IconCount.Badge(100)];
        string[] tips = [IconCount.TrayTip("Missiva", IconNumberKind.Unread, 3), IconCount.TrayTip("Missiva", IconNumberKind.New, 1), IconCount.TrayTip("Missiva", IconNumberKind.New, 4),
                         IconCount.TrayTip("Missiva", IconNumberKind.None, 4), IconCount.TrayTip("Missiva", IconNumberKind.Unread, 0)];
        Check(badges is [null, "7", "99", "99+"] && tips is ["Missiva: 3 da leggere", "Missiva: 1 nuovo", "Missiva: 4 nuovi", "Missiva", "Missiva"]
              && IconCount.Describe(IconNumberKind.Unread, 3) == "3 da leggere" && IconCount.Describe(IconNumberKind.None, 3) == "",
              $"numero sull'icona: il cerchio ({string.Join(", ", badges.Select(b => b ?? "niente"))}) e il suggerimento dell'area di notifica ({string.Join(" | ", tips)}) con la stessa regola");

        // Le impostazioni: di base com'era (da leggere, tutti gli account), salvate nel JSON delle impostazioni, e un account
        // rimosso esce anche da qui.
        var fresh2 = new Settings();
        var saved = new Settings { IconNumber = IconNumberKind.New, IconNumberOff = ["via", "resta"] };
        var read = System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(saved, SettingsJson.Default.Settings), SettingsJson.Default.Settings)!;
        var old = System.Text.Json.JsonSerializer.Deserialize("{\"Theme\":\"Dark\"}", SettingsJson.Default.Settings)!;
        read.ForgetAccount("via");
        string json = System.Text.Json.JsonSerializer.Serialize(saved, SettingsJson.Default.Settings);
        Check(fresh2.IconNumber == IconNumberKind.Unread && fresh2.IconNumberOff.Count == 0 && old.IconNumber == IconNumberKind.Unread && old.IconNumberOff.Count == 0
              && read.IconNumber == IconNumberKind.New && read.IconNumberOff.SequenceEqual(["resta"]) && json.Contains("\"IconNumber\": \"New\""),
              "numero sull'icona, le impostazioni: di base i messaggi da leggere per tutti gli account (anche dalle impostazioni di prima, senza le voci nuove); la scelta e gli account esclusi " +
              "si salvano con le altre (\"IconNumber\": \"New\"), e un account rimosso esce dagli esclusi (Settings.ForgetAccount)");
    }

    /// <summary>
    /// Il numero sull'icona nei sorgenti: la finestra principale lo calcola con IconCount e manda lo stesso numero, con la
    /// stessa scelta, al suggerimento dell'area di notifica; i messaggi nuovi arrivano da dove Missiva li annuncia, prima
    /// della notifica (che le notifiche spente fermano) e si azzerano con Activated; la pagina delle notifiche ha le tre
    /// scelte e gli account, e un cambio rifà il numero.
    /// </summary>
    static void RitIconNumberSources()
    {
        if (Ch1Code("UI", "MainWindow.xaml.cs") is not { } main || Ch1Code("UI", "App.cs") is not { } app || Ch1Code("UI", "SettingsWindow.cs") is not { } settings)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: il numero sull'icona non è stato letto)");
            return;
        }
        string now = Ch3Part(main, "void UpdateBadgeNow()", "static readonly Dictionary<(string Text, Color Color), ImageSource> badgeImages");
        bool badge = Ch3InOrder(now, "var kind = S.IconNumber;", "IconCount.Count(kind,", "fresh, S.IconNumberOff);", "if (count == badge && kind == badgeKind) return;",
                                "App.Current.UpdateTrayTip(kind, count);", "TaskbarItemInfo.Overlay = IconCount.Badge(count) is { } text ? BadgeImage(text, accent) : null;",
                                "TaskbarItemInfo.Description = IconCount.Describe(kind, count);");
        string tip = Ch3Part(app, "public void UpdateTrayTip(IconNumberKind kind, int count)", "\n    }");
        bool tray = tip.Contains("tray.Tip = IconCount.TrayTip(name, kind, count);") && Ch3Part(app, "public void UpdateTray()", "\n    }").Contains("UpdateTrayTip(trayKind, trayCount);");
        string check = Ch3Part(app, "async Task<int> CheckAccountAsync(", "(MainWindow as MainWindow)?.NoteAccountState(p, null);");
        bool arrivals = Ch3InOrder(check, "if (announce.Count > 0) (MainWindow as MainWindow)?.CountArrived(p, announce);", "if (announce.Count > 0) Notify(p, announce);");
        bool looked = main.Contains("Activated += (_, _) => { if (only == null && fresh.Clear()) UpdateBadge(); };")
                      && Ch3Part(main, "internal void CountArrived(", "\n    }").Contains("looking: IsActive && IsVisible && WindowState != WindowState.Minimized");
        string page = Ch3Part(settings, "IEnumerable<UIElement> IconNumberPart()", "\n    }");
        bool choices = Ch3InOrder(page, "Section(\"Numero sull'icona di Missiva\")", "(IconNumberKind.Unread, \"Messaggi da leggere nella Posta in arrivo\")",
                                  "(IconNumberKind.New, \"Messaggi nuovi arrivati da quando hai guardato Missiva\")", "(IconNumberKind.None, \"Nessun numero\")",
                                  "Text = \"Conta questi account\"", "S.IconNumberOff.Remove(account.Id);", "if (!on) S.IconNumberOff.Add(account.Id);", "Refresh();")
                       && page.Contains("main.RefreshBadge();") && Ch3Part(settings, "StackPanel NotificationsPage()", "\n    }").Contains("IconNumberPart()");
        Check(badge && tray && arrivals && looked && choices,
              "numero sull'icona nei sorgenti: la finestra principale lo calcola con IconCount e manda lo stesso numero con la stessa scelta all'area di notifica; i messaggi nuovi sono quelli " +
              "annunciati, contati prima della notifica (che le notifiche spente fermano), non mentre si guarda la finestra, e azzerati quando diventa attiva; Impostazioni, Generali, Notifiche ha le tre " +
              $"scelte e \"Conta questi account\", e ogni cambio rifà subito il numero [{badge} {tray} {arrivals} {looked} {choices}]");
    }

    // ---------------------------------------------------------------- l'aggiornamento nella barra del titolo

    /// <summary>
    /// I testi del pulsante dell'aggiornamento nella barra del titolo e del suo suggerimento, in ogni stato: disponibile,
    /// durante lo scaricamento con la percentuale (100 solo alla fine), un lavoro in corso, non riuscito con il motivo.
    /// </summary>
    static void RitUpdateTexts()
    {
        var manifest = new Updates.Manifest(new Version(0, 3, 600), "0.3.600", new DateOnly(2026, 10, 20), "abcdef0", "Missiva-0.3.600.zip", 100 * 1048576L, new string('0', 64), ["Una novità"]);
        string[] buttons = [UI.MainWindow.UpdateButtonText(UI.MainWindow.UpdateLook.Offer, 0), UI.MainWindow.UpdateButtonText(UI.MainWindow.UpdateLook.Downloading, 42),
                            UI.MainWindow.UpdateButtonText(UI.MainWindow.UpdateLook.Busy, 0), UI.MainWindow.UpdateButtonText(UI.MainWindow.UpdateLook.Failed, 0)];
        int[] percents = [UI.MainWindow.UpdatePercent(0, manifest.Size), UI.MainWindow.UpdatePercent(42 * 1048576L, manifest.Size), UI.MainWindow.UpdatePercent(manifest.Size - 1, manifest.Size),
                          UI.MainWindow.UpdatePercent(manifest.Size, manifest.Size), UI.MainWindow.UpdatePercent(5, 0)];
        string[] tips = [UI.MainWindow.UpdateButtonTip(UI.MainWindow.UpdateLook.Offer, manifest, 0, null), UI.MainWindow.UpdateButtonTip(UI.MainWindow.UpdateLook.Downloading, manifest, 42 * 1048576L, null),
                         UI.MainWindow.UpdateButtonTip(UI.MainWindow.UpdateLook.Failed, null, 0, "la firma non vale")];
        Check(buttons is ["Aggiornamento", "Aggiornamento 42%", "Aggiornamento…", "Aggiornamento non riuscito"] && percents is [0, 42, 99, 100, 0]
              && tips is ["È disponibile Missiva 0.3.600 del 20/10/2026: le novità e Aggiorna", "Scarico Missiva 0.3.600: 42,0 di 100,0 MB", "Aggiornamento non riuscito: la firma non vale"],
              $"aggiornamento nella barra del titolo, il pulsante: {string.Join(" | ", buttons)}; la percentuale {string.Join(", ", percents)}; il suggerimento {string.Join(" | ", tips)}");
    }

    /// <summary>
    /// L'aggiornamento nella barra del titolo, nei sorgenti: la barra in fondo all'elenco non c'è più; il pulsante sta nella
    /// colonna destra della barra del titolo, dentro la WindowChrome come la ricerca; il pannellino ha Aggiorna e Più tardi
    /// (che rimanda la versione al giorno dopo come la X di prima), Annulla durante lo scaricamento, il motivo e Riprova dopo
    /// un intoppo, e solo Chiudi dopo un aggiornamento non riuscito al riavvio; la ricerca lascia posto ai pulsanti e nella
    /// finestra stretta del pulsante resta l'icona.
    /// </summary>
    static void RitUpdateSources()
    {
        if (BehSource("UI", "MainWindow.xaml") is not { } xaml || Ch1Code("UI", "MainWindow.Updates.cs") is not { } window || Ch1Code("UI", "MainWindow.xaml.cs") is not { } main
            || Ch1Code("UI", "TitleFlyout.cs") is not { } flyout || Ch1Code("UI", "SettingsWindow.cs") is not { } settings)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: l'aggiornamento nella barra del titolo non è stato letto)");
            return;
        }
        string bar = Ch3Part(xaml, "<Grid x:Name=\"TitleBar\"", "<!-- Schede e barra dei comandi");
        bool placed = !xaml.Contains("x:Name=\"UpdateBar\"") && !xaml.Contains("x:Name=\"UpdateText\"")
                      && Ch3InOrder(bar, "<StackPanel x:Name=\"TitleTools\" Grid.Column=\"2\"", "WindowChrome.IsHitTestVisibleInChrome=\"True\"", "<Button x:Name=\"UpdateButton\" Visibility=\"Collapsed\" Click=\"OnUpdateButton\"",
                                    "x:Name=\"UpdateLabel\" Text=\"Aggiornamento\"", "<Grid x:Name=\"UpdateProgress\"", "<ScaleTransform x:Name=\"UpdateProgressScale\"");
        string panel = Ch3Part(window, "UIElement UpdatePanel()", "void OnUpdateLater(");
        bool states = Ch3InOrder(panel, "case UpdateLook.Offer when manifest != null:", "UpdateNews(manifest)", "Add(\"Più tardi\", OnUpdateLater", "Add(\"Aggiorna\", OnUpdateNow, accent: true",
                                 "case UpdateLook.Downloading when manifest != null:", "UpdateProgressText(manifest.Number, updateDone, manifest.Size)", "Add(\"Annulla\", OnUpdateCancel",
                                 "case UpdateLook.Failed:", "updateLine ?? ", "Add(\"Riprova\", OnUpdateNow, accent: true", "else Add(\"Chiudi\", OnUpdateLater);")
                      && panel.Contains("manifest?.Title");
        string later = Ch3Part(window, "void OnUpdateLater(", "void OnUpdateNow(");
        bool dismissed = Ch3InOrder(later, "SetUpdateLook(UpdateLook.Hidden);", "if (updateOffer is not { } offer) return;", "S.UpdateDismissed = offer.Manifest.Number.ToString();", "S.UpdateDismissedOn = DateTime.Today;", "Store.Save();");
        string now = Ch3Part(window, "async Task UpdateNowAsync(", "void OnUpdateCancel(");
        bool flow = Ch3InOrder(now, "SetUpdateLook(UpdateLook.Downloading);", "catch (OperationCanceledException) when (cancel.IsCancellationRequested)", "ShowUpdateOffer();",
                               "SetUpdateLook(UpdateLook.Failed, ex is Updates.Refused", "SetUpdateLook(UpdateLook.Busy, $\"Provo Missiva {number}…\");",
                               "SetUpdateLook(UpdateLook.Failed, $\"Missiva {number} è stata scaricata ma su questo PC non parte", "Dialog.Ask(this, $\"Aggiornare a Missiva {number}?\"", "App.Current.UpdateOnExit(prepared);")
                    && Ch3Part(window, "internal void ShowUpdateFailure(string notice)", "\n    }").Contains("SetUpdateLook(UpdateLook.Failed, notice);");
        string fit = Ch3Part(window, "void FitTitleBar()", "double TitleSearchWidth(");
        bool narrow = Ch3InOrder(fit, "TitleTools.Margin = margin;", "TitleToolsWidth(label: true)) < 280;", "UpdateLabel.Visibility = label;", "SearchArea.Width = Math.Clamp(TitleSearchWidth(left, caption, TitleToolsWidth(label: !narrow)), 200, 520);")
                      && Ch3Part(main, "void OnWindowSize(object sender, SizeChangedEventArgs e)", "FitColumns();").Contains("FitTitleBar();");
        bool panelWindow = window.Contains("updateFlyout ??= new TitleFlyout(this, UpdateButton, \"Aggiornamento di Missiva\", 380);")
                           && Ch3InOrder(flyout, "WindowStyle = WindowStyle.None", "if (DevTools.Photos) window.ShowActivated = false;", "opened.Deactivated +=", "> TimeSpan.FromMilliseconds(600)) Close(\"finestra non più attiva\");");
        bool about = settings.Contains("Aggiornamento\\\" nella barra del titolo, e si aggiorna solo con Aggiorna") && !settings.Contains("una barra in fondo all'elenco");
        Check(placed && states && dismissed && flow && narrow && panelWindow && about,
              "aggiornamento nella barra del titolo, nei sorgenti: niente più barra in fondo all'elenco; il pulsante a destra nella barra del titolo, dentro la WindowChrome; il pannellino (una finestra senza bordi " +
              "come i filtri della ricerca) con la versione, le novità, Aggiorna e Più tardi (rimandata al giorno dopo), Annulla durante lo scaricamento, il motivo con Riprova dopo un intoppo, Chiudi dopo " +
              $"un aggiornamento non riuscito al riavvio; nella finestra stretta del pulsante resta l'icona; Informazioni lo dice [{placed} {states} {dismissed} {flow} {narrow} {panelWindow} {about}]");
    }

    // ---------------------------------------------------------------- il gestore degli scaricamenti

    /// <summary>Gli stati di un elemento del gestore in una riga, come li dice il pannello; la percentuale al più 99 finché non è finito.</summary>
    static void RitDownloadTexts()
    {
        var list = new DownloadList();
        var waiting = list.Add("preventivo.pdf", 3 * 1048576, temporary: false, waiting: true);
        var running = list.Add("progetto.pdf", 2_936_013, temporary: false);
        running.Report(1_258_291);
        var unknown = list.Add("messaggio.eml", null, temporary: false);
        unknown.Report(400_000);
        var almost = list.Add("quasi.pdf", 1000, temporary: false);
        almost.Report(1000);
        var small = list.Add("nota.txt", 840_000, temporary: false);
        small.Report(300_000);
        string[] said = [waiting.Status, running.Status, unknown.Status, small.Status, DownloadItem.SizeText(512), DownloadItem.SizeText(2_936_013)];
        Check(said is ["In coda", "1,2 di 2,8 MB, 42%", "391 KB", "293 di 820 KB, 35%", "512 B", "2,8 MB"] && almost.Percent == 99 && unknown.Percent == null,
              $"scaricamenti, gli stati in una riga: {string.Join(" | ", said)}; finiti tutti i byte attesi ma non scritto il file, la percentuale resta {almost.Percent} (non 100); senza dimensione nessuna percentuale");

        // L'avanzamento non torna indietro (MailKit conta a parte le intestazioni della parte), e la misura annunciata dal server vale.
        running.Report(1000);
        unknown.Expect(800_000);
        Check(running.Received == 1_258_291 && unknown.Expected == 800_000 && unknown.Percent == 50,
              $"scaricamenti: l'avanzamento non torna indietro ({running.Received}) e la misura annunciata dal server (IExpectedSize) dà la percentuale a chi non l'aveva ({unknown.Percent}%)");
    }

    /// <summary>
    /// L'elenco tiene al più DownloadList.Max elementi: oltre esce il più vecchio fra quelli finiti, mai uno in corso (perderebbe
    /// Annulla); "Cancella l'elenco" toglie quelli finiti, non riusciti e annullati, e lascia quelli in corso.
    /// </summary>
    static void RitDownloadListLimit()
    {
        var list = new DownloadList();
        var oldest = list.Add("in-corso.pdf", 1000, temporary: false); // il più vecchio, ancora in corso
        var finished = new List<DownloadItem>();
        for (int i = 0; i < 24; i++)
        {
            var item = list.Add($"file-{i}.pdf", 1000, temporary: false);
            item.Finish(Path.Combine(Path.GetTempPath(), $"file-{i}.pdf"), true, 1000);
            finished.Add(item);
        }
        var items = list.Items;
        bool newestFirst = items[0] == finished[^1] && items.Contains(oldest);
        bool trimmed = items.Count == DownloadList.Max && !items.Contains(finished[0]) && !items.Contains(finished[4]) && items.Contains(finished[5]);
        list.Clear();
        var left = list.Items;
        Check(newestFirst && trimmed && left is [var only] && only == oldest,
              $"scaricamenti, l'elenco: i più recenti in cima, al più {DownloadList.Max} ({items.Count} dopo 25), oltre esce il più vecchio fra quelli finiti e mai quello in corso; " +
              $"\"Cancella l'elenco\" lascia solo quelli in corso ({left.Count})");
    }

    /// <summary>
    /// Il gestore con Graph finto: Salva con nome di un allegato grande (l'avanzamento passa dall'elemento, il file nasce solo
    /// alla fine, con il marchio web); Annulla a metà ferma la richiesta e non lascia il file, né nella cartella scelta né fra
    /// i temporanei (Apri con un programma); un errore del server dà "non riuscito" con il motivo; il messaggio intero (.eml)
    /// prende la misura annunciata dalla risposta.
    /// </summary>
    static async Task RitDownloadsGraphAsync()
    {
        var fake = new RitGraph();
        GraphProvider.UseTestHandler(fake);
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@studio-prova.example", Id = "rit-graph-" + Guid.NewGuid().ToString("N")[..8] };
        IMailProvider graph = new GraphProvider(account);
        var message = new MessageSummary { Id = "m1", FolderId = "inbox", Subject = "Progetto" };
        var folder = Path.Combine(Store.Dir, "ritocchi-graph-" + Guid.NewGuid().ToString("N")[..8]);
        var temp = Path.Combine(folder, "temporanei");
        Directory.CreateDirectory(temp);
        var list = new DownloadList();

        // Salva con nome: durante lo scaricamento il file non c'è, e l'elemento cresce.
        var path = Path.Combine(folder, "progetto.pdf");
        var seen = new List<long>();
        bool fileDuring = false;
        var big = new Attachment { Id = "grande", Name = "progetto.pdf", Size = 600_000 };
        var saving = list.SaveAsync(path, big.Size, async (progress, cancel) =>
        {
            var reporting = new RitProgress { Then = done => { progress.Report(done); lock (seen) seen.Add(done); fileDuring |= File.Exists(path); } };
            return await graph.GetAttachmentAsync(message, big, reporting, cancel);
        });
        var saved = await saving;
        var written = File.Exists(path) ? File.ReadAllBytes(path) : [];
        bool marked = File.Exists(path + ":Zone.Identifier");
        Check(saved.State == DownloadState.Done && saved.Path == path && saved.Received == fake.Big.Length && seen.Count >= 5 && !fileDuring && written.AsSpan().SequenceEqual(fake.Big) && marked && saved.Marked,
              $"scaricamenti con Graph, Salva con nome di un allegato grande: {seen.Count} passi di avanzamento sull'elemento, il file scritto solo alla fine (durante: {fileDuring}), intero e con il marchio web " +
              $"({saved.Status})");

        // Annulla a metà: Salva con nome e Apri con un programma.
        async Task<(DownloadItem Item, bool Stopped)> CancelHalfway(Func<Func<IProgress<long>, CancellationToken, Task<byte[]>>, Task<DownloadItem>> start)
        {
            fake.StallCancelled = false;
            var run = start(async (progress, cancel) =>
            {
                var item = (DownloadItem)progress;
                var watch = new RitProgress { Then = done => { item.Report(done); if (done >= 131_072) item.Cancel(); } };
                return await graph.GetAttachmentAsync(message, new Attachment { Id = "fermo", Name = "fermo.pdf", Size = 600_000 }, watch, cancel);
            });
            var finished = await run;
            return (finished, await Until(() => fake.StallCancelled, TimeSpan.FromSeconds(3)));
        }
        var stopPath = Path.Combine(folder, "fermo.pdf");
        var (stopSaved, stopped1) = await CancelHalfway(fetch => list.SaveAsync(stopPath, 600_000, fetch));
        var (stopOpened, stopped2) = await CancelHalfway(async fetch =>
        {
            var (item, tempFile) = await list.FetchToTempAsync("fermo.pdf", 600_000, fetch, root: temp);
            if (tempFile != null) list.Opened(item, tempFile);
            return item;
        });
        var leftInTemp = Directory.EnumerateFileSystemEntries(temp, "*", SearchOption.AllDirectories).ToList();
        Check(stopSaved.State == DownloadState.Canceled && stopOpened.State == DownloadState.Canceled && stopped1 && stopped2 && !File.Exists(stopPath) && leftInTemp.Count == 0
              && stopSaved.Status == "Annullato",
              $"scaricamenti con Graph, Annulla a metà: la richiesta si ferma (il server finto lo vede: {stopped1} e {stopped2}), l'elemento è annullato, e nessun file resta: né quello scelto con Salva con nome " +
              $"({File.Exists(stopPath)}), né fra i temporanei di Apri ({leftInTemp.Count} file o cartelle)");

        // Un errore del server.
        var brokenPath = Path.Combine(folder, "rotto.pdf");
        var broken = await list.SaveAsync(brokenPath, null, (progress, cancel) => graph.GetAttachmentAsync(message, new Attachment { Id = "rotto", Name = "rotto.pdf" }, progress, cancel));
        Check(broken.State == DownloadState.Failed && broken.Status.StartsWith("Non riuscito: Non riesco a scaricare l'allegato") && broken.Status.Contains("500") && !File.Exists(brokenPath),
              $"scaricamenti con Graph, un errore del server (500): \"{broken.Status}\", nessun file");

        // Il messaggio intero (Salva con nome .eml): senza dimensione all'inizio, prende quella annunciata dalla risposta.
        var emlPath = Path.Combine(folder, "Intero.eml");
        long? expectedDuring = null;
        var eml = await list.SaveAsync(emlPath, null, async (progress, cancel) =>
        {
            var raw = await graph.GetRawAsync(new MessageSummary { Id = "intero", FolderId = "inbox", Subject = "Intero" }, int.MaxValue, cancel, progress);
            expectedDuring = ((DownloadItem)progress).Expected;
            return raw;
        }, accountId: account.Id);
        Check(eml.State == DownloadState.Done && expectedDuring == fake.Raw.Length && File.ReadAllBytes(emlPath).Length == fake.Raw.Length && eml.AccountId == account.Id,
              $"scaricamenti con Graph, il messaggio salvato come .eml: la misura annunciata dalla risposta ({expectedDuring}) dà la percentuale, il file è intero, e l'elemento ricorda l'account (per riaprirlo in Missiva)");
        DeleteQuietly(folder);
    }

    /// <summary>
    /// Il gestore con IMAP finto: Salva tutti con tre allegati (tre elementi, i successivi in coda finché tocca a loro, un nome
    /// già preso diventa " (2)", tutti con il marchio web); Apri con un programma scrive nella cartella temporanea con il nome
    /// neutro finché non si apre, e una risposta no alla domanda lo butta; Annulla di uno in coda lo salta senza fermare gli
    /// altri; un rifiuto del server dà "non riuscito".
    /// </summary>
    static async Task RitDownloadsImapAsync()
    {
        var files = new (string Name, byte[] Bytes)[] { ("pianta.pdf", RitBytes(300_000, 21)), ("computo.xlsx", RitBytes(120_000, 22)), ("relazione.docx", RitBytes(80_000, 23)) };
        var mime = SpeedMail("Tre allegati", "Ti mando i tre file");
        var mixed = new MimeKit.Multipart("mixed") { new MimeKit.TextPart("html") { Text = "<p>Ti mando i tre file</p>" } };
        foreach (var (name, bytes) in files)
            mixed.Add(new MimeKit.MimePart("application", "octet-stream")
            {
                Content = new MimeKit.MimeContent(new MemoryStream(bytes)), ContentDisposition = new MimeKit.ContentDisposition(MimeKit.ContentDisposition.Attachment),
                ContentTransferEncoding = MimeKit.ContentEncoding.Base64, FileName = name,
            });
        mime.Body = mixed;
        using var fake = new FakeImap(new Flavor(Gmail: false));
        fake.Add("INBOX", mime);
        var folder = Path.Combine(Store.Dir, "ritocchi-imap-" + Guid.NewGuid().ToString("N")[..8]);
        var temp = Path.Combine(folder, "temporanei");
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(folder, "pianta.pdf"), "un file che c'era già");
        bool plain = ImapProvider.TestPlain;
        ImapProvider.TestPlain = true;
        try
        {
            using var provider = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            IMailProvider imap = provider;
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var message = (await imap.GetMessagesAsync(inbox, null, null)).Messages.Single();
            var attachments = (await imap.GetBodyAsync(message)).Attachments.Where(a => !a.IsInline).ToList();
            var list = new DownloadList();

            fake.Trickle = (16384, TimeSpan.FromMilliseconds(15));
            var states = new List<string>();
            var all = list.SaveAllAsync(folder, attachments.Select(a => new DownloadList.SaveAllFile(Safety.SafeFileName(a.Name), a.Size, async (progress, cancel) =>
            {
                lock (states) states.Add(string.Join(",", list.Items.Select(i => i.State)));
                return await imap.GetAttachmentAsync(message, a, progress, cancel);
            })).ToList());
            var items = await all;
            fake.Trickle = null;
            var names = items.Select(i => Path.GetFileName(i.Path ?? "")).ToList();
            bool same = items.Zip(files).All(p => p.First.Path != null && File.ReadAllBytes(p.First.Path).AsSpan().SequenceEqual(p.Second.Bytes));
            bool marks = items.All(i => i.Marked && File.Exists(i.Path + ":Zone.Identifier"));
            Check(items.Count == 3 && items.All(i => i.State == DownloadState.Done) && list.Items.SequenceEqual(items) && names is ["pianta (2).pdf", "computo.xlsx", "relazione.docx"]
                  && same && marks && File.ReadAllText(Path.Combine(folder, "pianta.pdf")) == "un file che c'era già" && states.FirstOrDefault() == "Running,Waiting,Waiting" && states.ElementAtOrDefault(1) == "Done,Running,Waiting",
                  $"scaricamenti con IMAP, Salva tutti con tre allegati: tre elementi in ordine, il primo in cima ({string.Join(", ", names)}), gli altri in coda finché tocca a loro ({string.Join(" poi ", states)}), " +
                  "il file che c'era resta e il nuovo prende \" (2)\", tutti interi e con il marchio web");

            // Annulla di uno in coda: si salta, gli altri vanno avanti.
            var skipFolder = Path.Combine(folder, "salta");
            Directory.CreateDirectory(skipFolder);
            DownloadList.SaveAllFile[] three = attachments.Select(a => new DownloadList.SaveAllFile(Safety.SafeFileName(a.Name), a.Size, (progress, cancel) => imap.GetAttachmentAsync(message, a, progress, cancel))).ToArray();
            var skipping = list.SaveAllAsync(skipFolder, three);
            var second = list.Items[1];
            second.Cancel();
            var skipped = await skipping;
            Check(skipped[1].State == DownloadState.Canceled && skipped[0].State == DownloadState.Done && skipped[2].State == DownloadState.Done
                  && Directory.GetFiles(skipFolder).Select(Path.GetFileName).Order().SequenceEqual(["pianta.pdf", "relazione.docx"]),
                  $"scaricamenti con IMAP, Annulla di un allegato in coda di Salva tutti: si salta ({skipped[1].Status}) e gli altri due si salvano");

            // Apri con un programma: il file temporaneo con il nome neutro, poi il suo nome; un no alla domanda lo butta.
            var (openItem, openTemp) = await list.FetchToTempAsync("computo.xlsx", attachments[1].Size, (progress, cancel) => imap.GetAttachmentAsync(message, attachments[1], progress, cancel), root: temp);
            bool neutral = openTemp != null && File.Exists(openTemp.Pending) && !File.Exists(openTemp.Final) && openTemp.Pending.EndsWith(".parte");
            if (openTemp != null)
            {
                openTemp.Release(); // quello che fa Apri prima di passarlo a Windows (qui nessun programma si avvia)
                list.Opened(openItem, openTemp);
            }
            var (noItem, noTemp) = await list.FetchToTempAsync("pianta.pdf", attachments[0].Size, (progress, cancel) => imap.GetAttachmentAsync(message, attachments[0], progress, cancel), root: temp);
            if (noTemp != null) list.NotOpened(noItem, noTemp);
            Check(neutral && openItem.State == DownloadState.Done && openItem.Temporary && openItem.Path == openTemp!.Final && File.Exists(openItem.Path) && openItem.Status.StartsWith("Aperto, ")
                  && noItem.State == DownloadState.Canceled && noTemp != null && !Directory.Exists(noTemp.Folder),
                  $"scaricamenti con IMAP, Apri con un programma: il file nella cartella temporanea con il nome neutro finché non si apre (Safety.WriteTemp: {neutral}), poi con il suo ({openItem.Status}); " +
                  "un no alla domanda lo butta con la sua cartella, e l'elemento è annullato");

            // Un rifiuto del server.
            fake.RefuseOnce = "UID FETCH";
            var refusedPath = Path.Combine(folder, "rifiutato.pdf");
            var refused = await list.SaveAsync(refusedPath, attachments[0].Size, (progress, cancel) => imap.GetAttachmentAsync(message, attachments[0], progress, cancel));
            Check(refused.State == DownloadState.Failed && refused.Status.StartsWith("Non riuscito: il server ha rifiutato la richiesta") && !File.Exists(refusedPath),
                  $"scaricamenti con IMAP, un rifiuto del server: \"{refused.Status}\", nessun file");
        }
        finally
        {
            ImapProvider.TestPlain = plain;
            DeleteQuietly(folder);
        }
    }

    /// <summary>
    /// L'indicatore degli scaricamenti nella barra del titolo: che cosa mostra (in corso prima di tutto, poi per qualche
    /// secondo il segno di fatto, poi un errore non ancora visto), il suo suggerimento, e la parte fatta di tutti quelli in
    /// corso (nessuna, e l'anello gira, se di uno non si sa la dimensione).
    /// </summary>
    static void RitDownloadsIndicator()
    {
        UI.MainWindow.DownloadsState[] looks = [UI.MainWindow.DownloadsLook(2, true, true), UI.MainWindow.DownloadsLook(0, true, true), UI.MainWindow.DownloadsLook(0, false, true), UI.MainWindow.DownloadsLook(0, false, false)];
        var list = new DownloadList();
        var first = list.Add("pianta.pdf", 1_000_000, temporary: false);
        first.Report(500_000);
        var second = list.Add("computo.xlsx", 3_000_000, temporary: false);
        second.Report(500_000);
        var known = list.Overall();
        string tipTwo = UI.MainWindow.DownloadsTip(list.Items);
        list.Add("messaggio.eml", null, temporary: false);
        var unknown = list.Overall();
        var single = new DownloadList();
        single.Add("pianta.pdf", 1_000_000, temporary: false).Report(250_000);
        string tipOne = UI.MainWindow.DownloadsTip(single.Items);
        Check(looks is [UI.MainWindow.DownloadsState.Running, UI.MainWindow.DownloadsState.Done, UI.MainWindow.DownloadsState.Failed, UI.MainWindow.DownloadsState.Idle]
              && known is (2, 0.25) && unknown is (3, null) && tipTwo == "Scaricamenti: 2 in corso" && tipOne == "Scaricamenti: pianta.pdf, 244 di 977 KB, 25%",
              $"scaricamenti, l'indicatore: in corso, poi il segno di fatto, poi un errore non visto ({string.Join(", ", looks)}); la parte fatta di tutti quelli in corso ({known.Fraction}), nessuna se di uno " +
              $"non si sa la dimensione ({unknown.Fraction?.ToString() ?? "l'anello gira"}); il suggerimento \"{tipTwo}\", \"{tipOne}\"");
    }

    /// <summary>
    /// Il gestore degli scaricamenti nei sorgenti: Apri con un programma, Salva con nome e Salva tutti del riquadro, Salva con
    /// nome .eml e il "Salva il documento" dei file firmati passano da DownloadList, con le domande di prima prima di scaricare
    /// e Safety per scrivere; l'anteprima no (si vede già caricare), né un .p7m o un .eml allegati che si aprono in Missiva;
    /// i byte del provider arrivano con l'avanzamento e l'annullamento; l'indicatore sta nella barra del titolo; Apri dal pannello
    /// fa le stesse domande e Mostra nella cartella usa Safety.ShowInFolder, solo per i file salvati.
    /// </summary>
    static void RitDownloadsSources()
    {
        if (Ch1Code("UI", "ReadingView.xaml.cs") is not { } reading || Ch1Code("UI", "MainWindow.xaml.cs") is not { } main || Ch1Code("UI", "P7mWindow.cs") is not { } signed
            || Ch1Code("UI", "MainWindow.Downloads.cs") is not { } panel || BehSource("UI", "MainWindow.xaml") is not { } xaml)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: il gestore degli scaricamenti nei sorgenti non è stato letto)");
            return;
        }
        string open = Ch3Part(reading, "async Task OpenAttachmentAsync(", "bool savingAll;");
        bool pane = Ch3InOrder(open, "await PreviewAsync(entry, attachment);", "if (!save && Safety.IsDangerous(name) && !Dialog.ConfirmOpenDangerous(owner, name)) return;",
                               "if (!save && !Dialog.ConfirmOpenWebPage(owner, name)) return;", "P7mWindow.ShowAsync(owner, await AttachmentBytesAsync(p, entry, attachment), name);",
                               "EmlRequested(p, await AttachmentBytesAsync(p, entry, attachment), name, InJunk(entry.Summary, junk));",
                               "Task<byte[]> Fetch(IProgress<long> progress, CancellationToken cancel) => AttachmentBytesAsync(p, entry, attachment, progress, cancel);",
                               "if (dialog.ShowDialog(owner) != true) return;", "await DownloadList.Session.SaveAsync(dialog.FileName, ExpectedSize(attachment), Fetch,",
                               "var (item, temp) = await DownloadList.Session.FetchToTempAsync(name, ExpectedSize(attachment), Fetch);",
                               "if (!temp.Marked && !Dialog.ConfirmOpenUnmarked(owner, name))", "DownloadList.Session.NotOpened(item, temp);",
                               "Process.Start(new ProcessStartInfo(temp.Release()) { UseShellExecute = true });", "DownloadList.Session.Opened(item, temp);", "DownloadList.Session.Failed(item, ex);");
        string all = Ch3Part(reading, "async Task SaveAllAsync(Entry entry, List<Attachment> attachments)", "void OpenLink(Uri uri)");
        bool saveAll = Ch3InOrder(all, "Safety.IsDangerous(Safety.SafeFileName(a.Name))", "if (dialog.ShowDialog(owner) != true) return;",
                                  "(progress, cancel) => AttachmentBytesAsync(p, entry, a, progress, cancel)", "DownloadList.Session.SaveAllAsync(dialog.FolderName, files,");
        string bytes = Ch3Part(reading, "Task<byte[]> AttachmentBytesAsync(", "static long? ExpectedSize(");
        bool provider = bytes.Contains("progress?.Report(kept.Length);") && bytes.Contains("return p.GetAttachmentAsync(entry.Summary, attachment, progress, cancel);")
                        && !Ch3Part(reading, "async Task<byte[]> LoadForPreviewAsync(", "Task<byte[]> AttachmentBytesAsync(").Contains("DownloadList");
        bool others = Ch3Part(main, "async Task SaveEmlAsync(MessageItem item)", "async Task SavePdfAsync(").Contains("await DownloadList.Session.SaveAsync(dialog.FileName, null, (progress, cancel) => provider.GetRawAsync(item.Summary, int.MaxValue, cancel, progress),")
                      && Ch3Part(signed, "void Save()", "void Open()").Contains("DownloadList.Session.SaveAsync(dialog.FileName, content.Length,");
        string reopen = Ch3Part(panel, "void OpenDownloaded(DownloadItem item)", "void ShowDownloaded(");
        bool apri = Ch3InOrder(reopen, "if (item.Path is not { } downloaded || !File.Exists(downloaded))", "item.Gone = true;", "if (P7m.IsP7m(name))", "P7mWindow.ShowAsync(", "OpenEml(provider,",
                               "if (Safety.IsDangerous(name) && !Dialog.ConfirmOpenDangerous(this, name)) return;", "if (!Dialog.ConfirmOpenWebPage(this, name)) return;",
                               "if (item.Temporary && !item.Marked && !Dialog.ConfirmOpenUnmarked(this, name)) return;", "Process.Start(new ProcessStartInfo(downloaded) { UseShellExecute = true });")
                    && Ch3Part(panel, "void ShowDownloaded(DownloadItem item)", "\n    }").Contains("Safety.ShowInFolder(path);")
                    && panel.Contains("if (!item.Temporary) Action(\"Mostra nella cartella\"") && panel.Contains("if (item.Active) Action(\"Annulla\"")
                    && panel.Contains("clear.Click += (_, _) => DownloadList.Session.Clear();");
        bool bar = Ch3InOrder(Ch3Part(xaml.Replace("\r\n", "\n"), "<StackPanel x:Name=\"TitleTools\"", "</StackPanel>\n        </Grid>"),
                              "<Button x:Name=\"UpdateButton\"", "<Button x:Name=\"DownloadsButton\" Visibility=\"Collapsed\" Click=\"OnDownloadsButton\"");
        Check(pane && saveAll && provider && others && apri && bar,
              "scaricamenti nei sorgenti: Apri con un programma, Salva con nome e Salva tutti del riquadro passano dal gestore dopo le domande di prima (l'anteprima, un .p7m e un .eml allegati no); i byte " +
              "arrivano dal provider con l'avanzamento e l'annullamento; Salva con nome .eml e \"Salva il documento\" dei file firmati anche; Apri dal pannello fa le stesse domande e Mostra nella " +
              $"cartella usa Safety.ShowInFolder, solo per i file salvati; l'indicatore accanto all'aggiornamento [{pane} {saveAll} {provider} {others} {apri} {bar}]");
    }

    // ---------------------------------------------------------------- l'avanzamento vero, dai provider

    /// <summary>Un avanzamento che scrive tutto quello che riceve: i byte arrivati, e quanti il server ne ha annunciati.</summary>
    sealed class RitProgress : IProgress<long>, IExpectedSize
    {
        public readonly List<long> Seen = new();
        public long? Expected;
        /// <summary>Chiamata a ogni passo (sul thread del provider): per annullare a metà.</summary>
        public Action<long>? Then;

        public void Report(long value)
        {
            lock (Seen) Seen.Add(value);
            Then?.Invoke(value);
        }

        public void Expect(long bytes) => Expected = bytes;

        public List<long> Steps() { lock (Seen) return Seen.ToList(); }
    }

    /// <summary>
    /// Il corpo di una risposta finta di Graph che arriva a pezzi: chunk byte alla volta con una pausa; con stallAfter, dopo
    /// tanti pezzi non arriva più nulla finché chi legge non annulla (e allora cancelled).
    /// </summary>
    sealed class RitChunks(byte[] data, int chunk, TimeSpan pause, int? stallAfter = null, Action? cancelled = null) : Stream
    {
        int at, served;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            try
            {
                if (stallAfter is { } n && served >= n) await Task.Delay(Timeout.Infinite, cancel);
                if (at >= data.Length) return 0;
                await Task.Delay(pause, cancel);
            }
            catch (OperationCanceledException)
            {
                cancelled?.Invoke();
                throw;
            }
            int length = Math.Min(Math.Min(chunk, buffer.Length), data.Length - at);
            data.AsMemory(at, length).CopyTo(buffer);
            at += length;
            served++;
            return length;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => at; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Graph finto per gli scaricamenti: "grande" arriva a pezzi da 64 KB con la lunghezza dichiarata, "fermo" si ferma dopo
    /// due pezzi finché non lo si annulla, "rotto" risponde 500, il messaggio "intero" ($value) arriva a pezzi.
    /// </summary>
    sealed class RitGraph : HttpMessageHandler
    {
        public readonly byte[] Big = RitBytes(600_000, 7);
        public readonly byte[] Raw = Encoding.ASCII.GetBytes("Subject: Prova\r\nFrom: maria.rossi@fornitori-prova.example\r\n\r\n" + new string('x', 300_000));
        public volatile bool StallCancelled;
        public readonly List<string> Requests = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests) Requests.Add(path);
            HttpResponseMessage Body(byte[] data, int? stallAfter = null)
            {
                var content = new StreamContent(new RitChunks(data, 65536, TimeSpan.FromMilliseconds(5), stallAfter, () => StallCancelled = true));
                content.Headers.ContentLength = data.Length;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content };
            }
            return Task.FromResult(path switch
            {
                _ when path.EndsWith("/attachments/grande/$value") => Body(Big),
                _ when path.EndsWith("/attachments/fermo/$value") => Body(Big, stallAfter: 2),
                _ when path.EndsWith("/attachments/rotto/$value") => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":{\"code\":\"ErrorInternalServerError\"}}") },
                _ when path.EndsWith("/messages/intero/$value") => Body(Raw),
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { Content = new StringContent("{}") },
            });
        }
    }

    /// <summary>Byte che non si comprimono e non si ripetono, sempre gli stessi per lo stesso seme.</summary>
    static byte[] RitBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// Graph: l'avanzamento di un allegato grande arriva in più passi, con la lunghezza dichiarata dalla risposta prima del
    /// primo; Annulla a metà ferma la lettura della risposta (il corpo finto vede l'annullamento) e solleva
    /// OperationCanceledException, non un errore; un errore del server è un MailException con il motivo; il messaggio intero
    /// (Salva con nome .eml) ha il suo avanzamento.
    /// </summary>
    static async Task RitGraphProgressAsync()
    {
        var fake = new RitGraph();
        GraphProvider.UseTestHandler(fake);
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@studio-prova.example", Id = "rit-graph-" + Guid.NewGuid().ToString("N")[..8] };
        IMailProvider graph = new GraphProvider(account);
        var message = new MessageSummary { Id = "m1", FolderId = "inbox", Subject = "Progetto" };

        var big = new RitProgress();
        var bytes = await graph.GetAttachmentAsync(message, new Attachment { Id = "grande", Name = "progetto.pdf", Size = 600_400 }, big);
        var steps = big.Steps();
        Check(bytes.AsSpan().SequenceEqual(fake.Big) && steps.Count >= 5 && steps.Zip(steps.Skip(1)).All(p => p.Second > p.First) && steps[^1] == fake.Big.Length && big.Expected == fake.Big.Length,
              $"Graph, l'avanzamento di un allegato grande (600 KB a pezzi da 64 KB): {steps.Count} passi crescenti fino a {steps.LastOrDefault()} byte, con la lunghezza dichiarata " +
              $"dalla risposta prima ({big.Expected?.ToString() ?? "nessuna"}), e l'allegato intero");

        using var cancel = new CancellationTokenSource();
        var stalled = new RitProgress { Then = done => { if (done >= 131_072) cancel.Cancel(); } }; // dopo due pezzi: da lì la risposta non manda più nulla
        string outcome;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var got = await graph.GetAttachmentAsync(message, new Attachment { Id = "fermo", Name = "fermo.pdf", Size = 600_000 }, stalled, cancel.Token);
            outcome = $"arrivato ({got.Length} byte)";
        }
        catch (OperationCanceledException) { outcome = "annullato"; }
        catch (Exception ex) { outcome = $"{ex.GetType().Name}: {ex.Message}"; }
        long ms = clock.ElapsedMilliseconds;
        bool stopped = await Until(() => fake.StallCancelled, TimeSpan.FromSeconds(3));
        Check(outcome == "annullato" && stopped && ms < 3000,
              $"Graph, Annulla a metà di un allegato: la lettura della risposta si ferma (il server finto vede l'annullamento: {stopped}) e lo scaricamento finisce come annullato, " +
              $"non come errore ({outcome}, in {ms} ms)");

        string? failure = null;
        try { await graph.GetAttachmentAsync(message, new Attachment { Id = "rotto", Name = "rotto.pdf" }, new RitProgress()); }
        catch (MailException ex) { failure = ex.Message; }
        catch (Exception ex) { failure = "altro errore: " + ex.GetType().Name; }
        Check(failure?.StartsWith("Non riesco a scaricare l'allegato") == true, $"Graph, un errore del server (500) è un errore con il motivo: \"{failure}\"");

        var whole = new RitProgress();
        var raw = await graph.GetRawAsync(new MessageSummary { Id = "intero", FolderId = "inbox", Subject = "Intero" }, progress: whole);
        Check(raw.Length == fake.Raw.Length && whole.Steps().Count >= 3 && whole.Steps()[^1] == raw.Length,
              $"Graph, il messaggio intero (Salva con nome .eml) con il suo avanzamento: {whole.Steps().Count} passi fino a {raw.Length} byte");
    }

    /// <summary>Un messaggio con un allegato di byte casuali, codificato in base64 come i programmi di posta.</summary>
    static MimeKit.MimeMessage RitMailWith(string name, byte[] content)
    {
        var mime = SpeedMail("Il progetto", "Ti mando il progetto");
        mime.Body = new MimeKit.Multipart("mixed")
        {
            new MimeKit.TextPart("html") { Text = "<p>Ti mando il progetto</p>" },
            new MimeKit.MimePart("application", "pdf")
            {
                Content = new MimeKit.MimeContent(new MemoryStream(content)), ContentDisposition = new MimeKit.ContentDisposition(MimeKit.ContentDisposition.Attachment),
                ContentTransferEncoding = MimeKit.ContentEncoding.Base64, FileName = name,
            },
        };
        return mime;
    }

    /// <summary>
    /// IMAP (MailKit, ITransferProgress): l'avanzamento di un allegato grande in più passi, nella misura di Attachment.Size;
    /// Annulla a metà di una risposta che arriva lenta interrompe il comando (il server finto vede la connessione chiusa e
    /// smette di mandare) e la richiesta dopo riapre la connessione e funziona; un rifiuto del server è un errore con il motivo.
    /// </summary>
    static async Task RitImapProgressAsync()
    {
        var content = RitBytes(1_200_000, 11);
        using var fake = new FakeImap(new Flavor(Gmail: false));
        fake.Add("INBOX", RitMailWith("progetto.pdf", content));
        bool plain = ImapProvider.TestPlain;
        ImapProvider.TestPlain = true;
        try
        {
            using var provider = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            IMailProvider imap = provider;
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var message = (await imap.GetMessagesAsync(inbox, null, null)).Messages.Single();
            var attachment = (await imap.GetBodyAsync(message)).Attachments.Single(a => !a.IsInline);

            fake.Trickle = (65536, TimeSpan.FromMilliseconds(5));
            var progress = new RitProgress();
            var bytes = await imap.GetAttachmentAsync(message, attachment, progress);
            var steps = progress.Steps();
            double last = steps.Count > 0 ? steps[^1] / (double)attachment.Size : 0;
            Check(bytes.AsSpan().SequenceEqual(content) && steps.Count >= 5 && last is > 0.97 and < 1.03 && progress.Expected is { } said && Math.Abs(said - attachment.Size) < attachment.Size / 50,
                  $"IMAP, l'avanzamento di un allegato grande (1,2 MB in base64, la risposta a pezzi da 64 KB): {steps.Count} passi, l'ultimo a {last:P1} della dimensione dell'allegato " +
                  $"({attachment.Size} byte, i tre quarti dei byte in base64), annunciati {progress.Expected?.ToString() ?? "nessuno"}; l'allegato arriva intero");

            fake.Trickle = (32768, TimeSpan.FromMilliseconds(60));
            long sentBefore = Interlocked.Read(ref fake.TrickleSent);
            using var cancel = new CancellationTokenSource();
            var slow = new RitProgress { Then = done => { if (done > 100_000) cancel.Cancel(); } };
            string outcome;
            var clock = Stopwatch.StartNew();
            try
            {
                var got = await imap.GetAttachmentAsync(message, attachment, slow, cancel.Token);
                outcome = $"arrivato ({got.Length} byte)";
            }
            catch (OperationCanceledException) { outcome = "annullato"; }
            catch (Exception ex) { outcome = $"{ex.GetType().Name}: {ex.Message}"; }
            long ms = clock.ElapsedMilliseconds;
            bool cut = await Until(() => Volatile.Read(ref fake.TrickleCut) > 0, TimeSpan.FromSeconds(3));
            long sent = Interlocked.Read(ref fake.TrickleSent) - sentBefore;
            fake.Trickle = null;
            var again = await imap.GetAttachmentAsync(message, attachment);
            Check(outcome == "annullato" && cut && sent < content.Length && again.AsSpan().SequenceEqual(content),
                  $"IMAP, Annulla a metà di un allegato che arriva lento: lo scaricamento finisce come annullato ({outcome}, in {ms} ms), il comando si interrompe e il server finto trova la " +
                  $"connessione chiusa ({cut}) dopo aver mandato {sent / 1024} KB su circa {content.Length * 4 / 3 / 1024}; la richiesta dopo riapre la connessione e l'allegato arriva");

            fake.RefuseOnce = "UID FETCH";
            string? refused = null;
            try { await imap.GetAttachmentAsync(message, attachment, new RitProgress()); }
            catch (Exception ex) { refused = ex.Message; }
            Check(refused is { Length: > 0 }, $"IMAP, un rifiuto del server è un errore con il motivo: \"{refused}\"");
        }
        finally
        {
            ImapProvider.TestPlain = plain;
        }
    }

    /// <summary>Gmail (la stessa strada di IMAP, sul server finto nella variante Gmail): l'avanzamento di un allegato grande in più passi.</summary>
    static async Task RitGmailProgressAsync()
    {
        var content = RitBytes(500_000, 13);
        using var fake = new FakeImap(new Flavor());
        var stored = fake.Add("INBOX", RitMailWith("progetto.pdf", content));
        stored.Labels.Add("\\Inbox");
        GmailProvider.TestPort = fake.Port;
        try
        {
            using var gmail = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "rit-gmail-" + Guid.NewGuid().ToString("N")[..8] });
            IMailProvider provider = gmail;
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var message = (await provider.GetMessagesAsync(inbox, null, null)).Messages.Single();
            var attachment = (await provider.GetBodyAsync(message)).Attachments.Single(a => !a.IsInline);
            fake.Trickle = (32768, TimeSpan.FromMilliseconds(2));
            var progress = new RitProgress();
            var bytes = await provider.GetAttachmentAsync(message, attachment, progress);
            var steps = progress.Steps();
            Check(bytes.AsSpan().SequenceEqual(content) && steps.Count >= 5 && steps[^1] / (double)attachment.Size is > 0.97 and < 1.03,
                  $"Gmail, l'avanzamento di un allegato grande: {steps.Count} passi, l'ultimo a {steps.LastOrDefault()} byte su {attachment.Size}; l'allegato arriva intero");
        }
        finally
        {
            GmailProvider.TestPort = null;
        }
    }
}
#endif
