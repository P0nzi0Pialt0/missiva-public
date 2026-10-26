#if DEBUG
namespace Missiva;

/// <summary>
/// Audit di affidabilità del 25 ottobre 2026, elenco e conteggi: i controlli delle correzioni (docs/audit/affidabilita-elenco.md,
/// punti E-01..E-15, e la parte dell'elenco di L-10). Ognuno passa quando il difetto NON c'è più; il commit di ogni punto dice
/// che con il codice di prima cadeva (le sonde del ramo affidabilita-elenco mostravano il difetto). La suite non costruisce
/// la finestra principale: le decisioni dell'elenco stanno in funzioni senza finestra (ListRefresh, ListJournal, MessageRows),
/// provate qui, e dove servono i posti che le chiamano si leggono i sorgenti; le prove a finestra sono in
/// strumenti\arrivo\elenco.ps1. Server IMAP finto e gestore finto di Graph. Da solo:
/// Missiva.exe --prova-audit risultato.txt affidabilita-elenco (con MISSIVA_DATA).
/// </summary>
static partial class ProvaBozze
{
    static void ListFixTests()
    {
        Report.Add("--- Audit di affidabilità del 25 ottobre 2026, elenco e conteggi: le correzioni (E-..)");
        try
        {
            ElStaleRules();
            ElStaleSources();
            Wait(() => ElUnexplainedAsync(gmail: false));
            Wait(() => ElUnexplainedAsync(gmail: true));
            Wait(ElIdleGapAsync);
            Wait(ElDeltaMissingAsync);
            ElArrivalSources();
            ElCountsRules();
            Wait(() => ElCountsAsync(gmail: false));
            Wait(() => ElCountsAsync(gmail: true));
            Wait(ElCountsGraphAsync);
            ElCountsSources();
            ElConversationRows();
            Wait(ElFocusedAsync);
            ElDeferredSources();
            Wait(() => ElPageBeforeMoveAsync(gmail: false));
            Wait(() => ElPageBeforeMoveAsync(gmail: true));
            Wait(() => ElPageBeforeArrivalAsync(gmail: false));
            Wait(() => ElPageBeforeArrivalAsync(gmail: true));
            ElJournalRules();
            ElJournalSources();
            ElUnifiedSources();
            ElWindowsSources();
            ElOrderRules();
            ElOrderSources();
            ElEntersRules();
            ElCountRules();
            ElMinorSources();
            Wait(ElDaylightAsync);
        }
        finally
        {
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
            // Le connessioni chiuse apposta dal server finto (FailOn, Hold) non sono difetti della suite.
            lock (ServerErrors) ServerErrors.Clear();
        }
    }

    /// <summary>Un controllo che non passa dice che cosa manca, nella riga prima del NO; sempre falso.</summary>
    static bool Fail(string why)
    {
        Report.Add($"      (non passa: {why})");
        return false;
    }

    /// <summary>Il codice di un sorgente senza commenti (Ch1Code), o null se i sorgenti non stanno accanto all'eseguibile (lo dice il risultato).</summary>
    static string? ElCode(string what, params string[] parts)
    {
        if (Ch1Code(parts) is { } code) return code;
        Report.Add($"      (sorgenti non trovati accanto all'eseguibile: {what} non è stato letto)");
        return null;
    }

    // ---------------------------------------------------------------- E-01

    /// <summary>
    /// E-01, le regole (ListRefresh): un elenco rimasto vecchio (la prima pagina non è arrivata) si rilegge quando l'account
    /// torna a rispondere, al più una volta ogni 30 s; uno aggiornato no. Riaperta la finestra dall'area di notifica, si rilegge
    /// quello rimasto vecchio o letto più di due minuti prima. La riga sopra l'elenco dice di quando è la copia, in italiano e in
    /// inglese, e la data viene dalla copia sul PC (MailCache.SavedAt). Prima non c'era nulla di tutto questo: la copia restava a
    /// schermo, in silenzio, fino a un cambio di cartella.
    /// </summary>
    static void ElStaleRules()
    {
        var now = DateTime.UtcNow;
        UI.ListRefresh.State Fresh(TimeSpan readAgo) => new(false, now - readAgo, DateTime.MinValue);
        UI.ListRefresh.State Stale(TimeSpan triedAgo) => new(true, DateTime.MinValue, now - triedAgo);
        bool Back(UI.ListRefresh.State s) => UI.ListRefresh.Wanted(s, UI.ListRefresh.Cause.AccountBack, now);
        bool Tray(UI.ListRefresh.State s) => UI.ListRefresh.Wanted(s, UI.ListRefresh.Cause.TrayReopen, now);
        bool back = Back(Stale(TimeSpan.FromHours(1))) && Back(new(true, DateTime.MinValue, DateTime.MinValue)) && !Back(Stale(TimeSpan.FromSeconds(5)))
                    && Back(Stale(TimeSpan.FromSeconds(31))) && !Back(Fresh(TimeSpan.FromHours(5)));
        bool tray = Tray(Stale(TimeSpan.FromSeconds(1))) && Tray(Fresh(TimeSpan.FromMinutes(3))) && !Tray(Fresh(TimeSpan.FromSeconds(50)))
                    && Tray(new(false, DateTime.MinValue, DateTime.MinValue));
        var at = new DateTime(2026, 10, 25, 22, 14, 0);
        string italian = UI.ListRefresh.StaleText(at), english;
        Lingua.Force("en");
        try { english = UI.ListRefresh.StaleText(at); }
        finally { Lingua.Force("it"); }
        var account = new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example" };
        var before = DateTime.Now.AddSeconds(-2);
        MailCache.SaveMessages(account, "INBOX", null, [new MessageSummary { Id = "1", FolderId = "INBOX", Subject = "Pratica" }]);
        var saved = MailCache.SavedAt(account, "INBOX", null);
        bool savedAt = saved is { } s && s >= before && s <= DateTime.Now.AddSeconds(2) && MailCache.SavedAt(account, "Clienti", null) == null;
        MailCache.Delete(account);
        Report.Add($"      riga sopra l'elenco: \"{italian}\" / \"{english}\"; copia scritta alle {saved:HH:mm:ss}");
        Check(back && tray && italian == "Copia sul PC del 25/10/2026 22:14: non aggiornata." && english.StartsWith("Copy on this PC from ", StringComparison.Ordinal) && english.Contains("22:14") && savedAt,
              "E-01, le regole: l'elenco rimasto vecchio si rilegge quando l'account torna a rispondere (al più ogni 30 s), quello aggiornato no; alla riapertura dall'area di notifica " +
              "si rilegge se è vecchio o letto più di due minuti prima; la riga sopra l'elenco dice di quando è la copia sul PC (la data del file della copia), anche in inglese");
    }

    /// <summary>
    /// E-01, i posti (MainWindow, che la suite non costruisce: lettura dei sorgenti). La prima pagina che non arriva segna l'elenco
    /// vecchio (MarkListStale: la riga con l'ora e Riprova, l'account non in linea con un errore della rete) prima della riga
    /// d'errore di sempre, che resta senza copia; la rilettura riuscita lo rimette a posto e quella non riuscita lo segna. Lo
    /// rileggono un controllo riuscito (NoteAccountState), l'IDLE collegato o la query delta (AccountReached, prima dell'uscita per
    /// gli account non segnati), la rete cambiata (App.OnNetworkBack, prima dell'uscita) e la riapertura dall'area di notifica
    /// (ShowFromTray), con le regole di ListRefresh; la vista di tutti gli account si rilegge senza svuotare elenco e riquadro.
    /// </summary>
    static void ElStaleSources()
    {
        if (ElCode("l'elenco rimasto vecchio", "UI", "MainWindow.xaml.cs") is not { } main || ElCode("l'elenco rimasto vecchio", "UI", "MainWindow.Freshness.cs") is not { } fresh
            || ElCode("l'elenco rimasto vecchio", "UI", "App.cs") is not { } app || ElCode("l'elenco rimasto vecchio", "UI", "MainWindow.Unified.cs") is not { } unified) return;
        var more = Ch3Part(main, "async Task LoadMoreAsync(bool first = false)", "void ShowFirstPage(");
        var refresh = Ch3Part(main, "async Task RefreshListAsync(string why)", "MenuItem CategoryMenu(");
        var reached = Ch3Part(main, "public void AccountReached(", "public bool AnyFoldersFailed");
        var note = Ch3Part(main, "public void NoteAccountState(", "void OnAccountFix(");
        var tray = Ch3Part(main, "public void ShowFromTray()", "#if DEBUG");
        var back = Ch3Part(app, "internal async void OnNetworkBack()", "readonly Dictionary<string, DateTime> contactsTried");
        var stale = Ch3Part(fresh, "void MarkListStale(", "bool ListShows(");
        var retry = Ch3Part(fresh, "internal void RetryStaleList(", "void RefreshAfterTray()");
        var trayRule = Ch3Part(fresh, "void RefreshAfterTray()", "\n}");
        bool load = Ch3InOrder(more, "MailCache.SavedAt(", "GetMessagesAsync(", "MarkListRead();", "ShowFirstPage(", "catch (Exception ex)", "MarkListStale(account, ex)", "if (version == listVersion && !fromCache) SetListStatus(");
        bool marks = Ch3InOrder(stale, "listStale = true;", "Network.Note(account, ex);", "ListRefresh.StaleText(", "RefreshListAsync(") && Ch3InOrder(refresh, "GetMessagesAsync(", "MarkListRead();", "catch (Exception ex)", "MarkListStale(account, ex)");
        bool callers = Ch3InOrder(reached, "RetryStaleList(provider, how);", "if (!item.FoldersFailed && !Network.KnownOffline(provider.Account)) return;")
                       && Ch3InOrder(note, "if (error == null) RetryStaleList(provider,") && Ch3InOrder(back, "RetryStaleList(null,", "return;") && tray.Contains("RefreshAfterTray();")
                       && retry.Contains("ListRefresh.Cause.AccountBack") && trayRule.Contains("ListRefresh.Cause.TrayReopen") && retry.Contains("foreach (var window in FolderWindows) window.RetryStaleList(");
        bool views = Ch3InOrder(refresh, "LoadUnifiedAsync(first: true, again: why)") && !refresh.Contains("ReloadAsync(") && Ch3Part(unified, "async Task LoadUnifiedAsync(", "async Task<List<").Contains("listStale = true;");
        Check(load && marks && callers && views || Fail($"caricamento {load}, segni {marks}, chiamate {callers}, viste {views}"),
              "E-01, i posti: la prima pagina non arrivata con la copia a schermo segna l'elenco vecchio con la riga e Riprova (senza copia resta la riga d'errore di sempre), anche dopo una rilettura " +
              "non riuscita; lo rileggono il controllo riuscito, l'IDLE collegato e la query delta (anche per un account non segnato non in linea), la rete cambiata e la riapertura dall'area di notifica; " +
              "la vista di tutti gli account si rilegge senza svuotare elenco e riquadro");
    }

    // ---------------------------------------------------------------- E-02 ed E-03

    /// <summary>
    /// E-02 (la sonda ProbeNotNewAsync rovesciata), con l'IDLE vero sul server finto: un arrivo già letto altrove e uno rimesso in
    /// Posta in arrivo con la data di un mese fa danno l'avviso Arrived, ma il controllo (GetNewAsync con il mark) non li trova da
    /// annunciare. È l'arrivo che il controllo non spiega (Arrival.Unexplained), per cui App rilegge la Posta in arrivo aperta: la
    /// prima pagina del server li ha. Un arrivo da leggere normale invece è spiegato, e non fa rileggere nulla. Prima entravano
    /// nell'elenco solo i nuovi da leggere, e quei due mai.
    /// </summary>
    static async Task ElUnexplainedAsync(bool gmail)
    {
        var retry = InboxIdle.FirstRetry;
        InboxIdle.FirstRetry = TimeSpan.FromMilliseconds(200);
        using var fake = new FakeImap(new Flavor(Gmail: gmail)) { AnnounceOnNoop = true };
        ImapProvider.TestPlain = true;
        try
        {
            for (int k = 1; k <= 5; k++) fake.Add("INBOX", SpeedMail("Pratica " + k, "Testo della pratica " + k, daysAgo: 10 - k), "\\Seen");
            var provider = FakeProvider(fake, gmail, $"mario.e02.{Arf(gmail).ToLowerInvariant()}@studio-prova.example");
            using var closing = new ArfClosing(provider);
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            await provider.GetMessagesAsync(inbox, null, null);
            var since = DateTime.UtcNow.AddMinutes(-3);
            string? mark = (await provider.GetNewAsync(null, since)).Mark;
            var events = new System.Collections.Concurrent.ConcurrentQueue<InboxNotice>();
            using var stop = new CancellationTokenSource();
            var watching = provider.WatchInboxAsync(events.Enqueue, stop.Token);
            bool up = await Until(() => fake.Idling == 1, TimeSpan.FromSeconds(5));
            // Come App.OnInboxEvent: l'avviso dice quanti, il controllo li cerca, e se ne trova di meno l'arrivo non è spiegato.
            async Task<(int Announced, int Found, bool Reread)> ArriveAsync(MimeKit.MimeMessage mail, string flags)
            {
                events.Clear();
                fake.Add("INBOX", mail, flags);
                fake.Push($"* {fake.Folders["INBOX"].Count} EXISTS");
                await Until(() => events.Any(e => e.Kind == InboxEvent.Arrived), TimeSpan.FromSeconds(5));
                int announced = events.Where(e => e.Kind == InboxEvent.Arrived).Sum(e => Math.Max(1, e.New));
                var found = await provider.GetNewAsync(mark, since);
                mark = Arrival.LaterMark(mark, found.Mark);
                return (announced, found.Messages.Count, Arrival.Unexplained(found.Messages.Count, announced));
            }
            var read = await ArriveAsync(SpeedMail("Letto sul telefono", "Arrivato e già aperto altrove"), "\\Seen");
            var back = await ArriveAsync(SpeedMail("Tornato in Posta in arrivo", "Spostato qui dal telefono", daysAgo: 30), "");
            var normal = await ArriveAsync(SpeedMail("Preventivo appena arrivato", "Un arrivo da leggere"), "");
            var page = (await provider.GetMessagesAsync(inbox, null, null)).Messages;
            bool inPage = page.Any(m => m.Subject == "Letto sul telefono") && page.Any(m => m.Subject == "Tornato in Posta in arrivo");
            stop.Cancel();
            await ArfWithin(watching, TimeSpan.FromSeconds(3));
            Report.Add($"      {Arf(gmail)}: già letto annunciati {read.Announced} trovati {read.Found}, rimesso {back.Announced}/{back.Found}, da leggere {normal.Announced}/{normal.Found}; " +
                       $"nella prima pagina del server {page.Count} righe");
            Check(up && read is { Announced: 1, Found: 0, Reread: true } && back is { Announced: 1, Found: 0, Reread: true } && normal is { Announced: 1, Found: 1, Reread: false } && inPage
                  && Arrival.Unexplained(20, 25) && !Arrival.Unexplained(0, 0),
                  $"E-02 ({Arf(gmail)}): un arrivo già letto altrove e uno rimesso in Posta in arrivo con la data di un mese fa non sono spiegati dal controllo (annunciati 1, trovati 0) e fanno " +
                  "rileggere la Posta in arrivo, che li ha; anche i più di venti insieme; un arrivo da leggere normale è spiegato e non fa rileggere");
        }
        finally
        {
            InboxIdle.FirstRetry = retry;
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
        }
    }

    /// <summary>
    /// E-03 (la sonda ProbeIdleGapAsync rovesciata): la sessione IDLE cade e, prima che riparta, sul server un messaggio esce e uno
    /// diventa letto (il telefono, con il portatile sospeso). La sessione nuova non avvisa di nessuno dei due, ma il suo Connected
    /// dice Reconnected, per cui App controlla l'account e poi rilegge la Posta in arrivo aperta (ElArrivalSources): la pagina
    /// riletta non ha più il messaggio uscito e ha quello letto. Prima il Connected faceva solo AccountReached, e la riga eliminata
    /// restava fino a un cambio di cartella.
    /// </summary>
    static async Task ElIdleGapAsync()
    {
        var retry = InboxIdle.FirstRetry;
        InboxIdle.FirstRetry = TimeSpan.FromMilliseconds(200);
        ImapProvider.TestPlain = true;
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            for (int k = 1; k <= 4; k++) fake.Add("INBOX", SpeedMail("Pratica " + k, "Testo della pratica " + k, daysAgo: 5 - k));
            var imap = new ImapProvider(SpeedAccount(fake, "mario.e03@studio-prova.example"));
            using var closing = new ArfClosing(imap);
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var shown = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            var events = new System.Collections.Concurrent.ConcurrentQueue<InboxNotice>();
            using var stop = new CancellationTokenSource();
            var watching = imap.WatchInboxAsync(events.Enqueue, stop.Token);
            bool idling = await Until(() => fake.Idling == 1 && events.Any(e => e.Kind == InboxEvent.Connected), TimeSpan.FromSeconds(5));
            var gate = new TaskCompletionSource();
            fake.Hold = async command => { if (command == "EXAMINE") await gate.Task; };
            events.Clear();
            fake.DropIdlers();
            string goneId, readId;
            lock (fake.Folders)
            {
                var list = fake.Folders["INBOX"];
                goneId = list[^1].Uid.ToString();
                list.Remove(list[^1]);
                readId = list[0].Uid.ToString();
                list[0].Set("\\Seen", true);
            }
            await Task.Delay(300);
            fake.Hold = null;
            gate.SetResult();
            bool told = await Until(() => events.Any(e => e is { Kind: InboxEvent.Connected, Reconnected: true }), TimeSpan.FromSeconds(5));
            // App, a Reconnected: il controllo, poi la Posta in arrivo aperta riletta (RefreshInboxOf), come qui.
            var fresh = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            stop.Cancel();
            await ArfWithin(watching, TimeSpan.FromSeconds(3));
            bool gone = shown.Any(m => m.Id == goneId) && !fresh.Any(m => m.Id == goneId);
            bool readNow = fresh.FirstOrDefault(m => m.Id == readId) is { IsRead: true };
            Report.Add($"      IMAP: avvisi dopo la caduta: {string.Join(", ", events.Select(e => e.Kind + (e.Reconnected ? " (ricollegato)" : "")))}; righe prima {shown.Count}, nella pagina riletta {fresh.Count}");
            Check(idling && told && gone && readNow,
                  "E-03: l'IDLE caduto e ripreso dice Reconnected, che fa controllare l'account e rileggere la Posta in arrivo aperta; la pagina riletta non ha più il messaggio uscito intanto e " +
                  "ha letto quello letto altrove (prima nessuno rileggeva: la riga eliminata restava)");
        }
        finally
        {
            InboxIdle.FirstRetry = retry;
            ImapProvider.TestPlain = false;
        }
    }

    /// <summary>
    /// E-02 per Graph: la query delta (gestore finto) dice un messaggio nuovo già letto, uno che l'elenco ha e uno uscito.
    /// ListRefresh.Missing trova solo il primo, che l'elenco non ha nemmeno dopo il controllo (che mette in cima solo i nuovi da
    /// leggere): allora la Posta in arrivo aperta si rilegge (MainWindow.RefreshIfMissing). Prima la delta cambiava solo le righe
    /// che c'erano.
    /// </summary>
    static async Task ElDeltaMissingAsync()
    {
        int call = 0;
        static System.Text.Json.Nodes.JsonObject M(string id, bool read) => new() { ["id"] = id, ["isRead"] = read, ["receivedDateTime"] = "2026-10-25T08:00:00Z" };
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var items = (call++) == 0 ? new System.Text.Json.Nodes.JsonArray(M("vecchio", true))
                : new System.Text.Json.Nodes.JsonArray(M("letto-sul-telefono", true), M("in-elenco", true), new System.Text.Json.Nodes.JsonObject { ["id"] = "uscito", ["@removed"] = new System.Text.Json.Nodes.JsonObject { ["reason"] = "deleted" } });
            var json = new System.Text.Json.Nodes.JsonObject { ["value"] = items, ["@odata.deltaLink"] = $"https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=t{call}" };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        }));
        try
        {
            var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "maria.e02@outlook-prova.example" });
            var start = await graph.GetInboxChangesAsync();
            var changes = await graph.GetInboxChangesAsync();
            var missing = changes == null ? [] : UI.ListRefresh.Missing(changes, ["in-elenco", "uscito", "altro"]);
            var none = changes == null ? ["?"] : UI.ListRefresh.Missing(changes, ["in-elenco", "letto-sul-telefono"]);
            Check(start == null && missing.SequenceEqual(["letto-sul-telefono"]) && none.Count == 0,
                  $"E-02 per Graph: della query delta resta da far entrare solo il messaggio che l'elenco non ha ({string.Join(", ", missing)}), non quello che ha né quello uscito; " +
                  "con tutti nell'elenco non si rilegge nulla");
        }
        finally
        {
            GraphProvider.UseTestHandler(new NoHandler("Graph"));
        }
    }

    /// <summary>
    /// E-02 ed E-03, i posti in App (lettura dei sorgenti): dopo il primo controllo di un avviso Arrived, un arrivo non spiegato
    /// rilegge la Posta in arrivo, e di nuovo dopo i tentativi se resta non spiegato; un IDLE ricollegato rilegge dopo il suo
    /// controllo; la query delta, dopo il controllo, rilegge se ha messaggi che l'elenco non ha.
    /// </summary>
    static void ElArrivalSources()
    {
        if (ElCode("App.OnInboxEvent", "UI", "App.cs") is not { } app || ElCode("MainWindow.RefreshIfMissing", "UI", "MainWindow.Notifications.cs") is not { } notes) return;
        var idle = Ch3Part(app, "async void OnInboxEvent(", "Task CheckDeltasAsync(");
        var connected = Ch3Part(app, "if (e.Kind == InboxEvent.Connected)", "return;\n        }");
        var delta = Ch3Part(app, "async Task DeltasOnceAsync(", "readonly Dictionary<string, AccountFix> accountFixLogged");
        bool arrived = Ch3InOrder(idle, "CheckAccountAsync(provider, \"avviso dell'IDLE\")", "Arrival.Unexplained(found, expected)", "RefreshInboxOf(provider, \"arrivo che il controllo non spiega\")",
                                  "foreach (var wait in Arrival.ArrivalRetries)", "Arrival.Unexplained(found, expected)", "RefreshInboxOf(provider, \"arrivo che il controllo non spiega, dopo i tentativi\")");
        bool again = Ch3InOrder(connected, "CheckAccountAsync(provider, \"IDLE ricollegato\")", "RefreshInboxOf(provider, \"IDLE ricollegato\")");
        bool missing = Ch3InOrder(delta, "ApplyInboxChanges(p, changes)", "await CheckAccountAsync(p, changes.Seen", "RefreshIfMissing(p, changes)")
                       && Ch3Part(notes, "internal void RefreshIfMissing(", "public void ApplyIdleFlags(").Contains("ListRefresh.Missing(changes, known)");
        Check(arrived && again && missing || Fail($"arrivo {arrived}, ricollegato {again}, delta {missing}"),
              "E-02 ed E-03 nei sorgenti: un arrivo che il controllo non spiega rilegge la Posta in arrivo aperta, subito e dopo i tentativi; un IDLE ricollegato la rilegge dopo il suo controllo; " +
              "la query delta la rilegge, dopo il controllo, se ha messaggi che l'elenco non ha");
    }

    // ---------------------------------------------------------------- E-06

    /// <summary>
    /// E-06, le regole: i numeri delle cartelle si rileggono al controllo di riserva al più ogni cinque minuti per account; la
    /// cartella aperta che nessuno guarda (non la Posta in arrivo con l'IDLE o la query delta) si rilegge se i suoi numeri sono
    /// cambiati, o se la sua prima pagina ha più di cinque minuti; la Posta in arrivo guardata no (la aggiornano IDLE e delta);
    /// mai più di una volta ogni 30 s. Prima nessuna di queste: solo il cambio di cartella e F5.
    /// </summary>
    static void ElCountsRules()
    {
        var now = DateTime.UtcNow;
        var long_ = now - TimeSpan.FromHours(1);
        UI.ListRefresh.State Other(TimeSpan readAgo, TimeSpan triedAgo) => new(false, now - readAgo, now - triedAgo, Watched: false);
        UI.ListRefresh.State Inbox(TimeSpan readAgo) => new(false, now - readAgo, long_, Watched: true);
        bool Periodic(UI.ListRefresh.State s) => UI.ListRefresh.Wanted(s, UI.ListRefresh.Cause.Periodic, now);
        bool Moved(UI.ListRefresh.State s) => UI.ListRefresh.Wanted(s, UI.ListRefresh.Cause.CountsChanged, now);
        bool due = UI.ListRefresh.CountsDue(DateTime.MinValue, now) && UI.ListRefresh.CountsDue(now - TimeSpan.FromMinutes(5), now) && !UI.ListRefresh.CountsDue(now - TimeSpan.FromMinutes(4), now);
        var folder = new MailFolder { Id = "Clienti", Name = "Clienti", Unread = 0, Total = 3 };
        bool moved = UI.ListRefresh.CountsMoved(folder, new MailFolder { Id = "Clienti", Name = "Clienti", Unread = 1, Total = 4 })
                     && UI.ListRefresh.CountsMoved(folder, new MailFolder { Id = "Clienti", Name = "Clienti", Unread = 0, Total = 2 })
                     && !UI.ListRefresh.CountsMoved(folder, new MailFolder { Id = "Clienti", Name = "Clienti", Unread = 0, Total = 3 });
        bool periodic = Periodic(Other(TimeSpan.FromMinutes(6), TimeSpan.FromHours(1))) && !Periodic(Other(TimeSpan.FromMinutes(2), TimeSpan.FromHours(1)))
                        && !Periodic(Inbox(TimeSpan.FromHours(3))) && Periodic(new(true, DateTime.MinValue, now - TimeSpan.FromSeconds(40), Watched: true))
                        && !Periodic(Other(TimeSpan.FromMinutes(6), TimeSpan.FromSeconds(10)));
        bool changed = Moved(Other(TimeSpan.FromSeconds(20), TimeSpan.FromHours(1))) && !Moved(Inbox(TimeSpan.FromSeconds(20))) && !Moved(Other(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10)));
        Check(due && moved && periodic && changed || Fail($"ogni tanto {due}, numeri cambiati {moved}, periodica {periodic}, per i numeri {changed}"),
              "E-06, le regole: i numeri delle cartelle si rileggono al controllo di riserva al più ogni 5 minuti per account; la cartella aperta che nessuno guarda si rilegge se i suoi numeri " +
              "cambiano o se la sua prima pagina ha più di 5 minuti, la Posta in arrivo guardata dall'IDLE o dalla query delta no, e mai più di una volta ogni 30 s");
    }

    /// <summary>
    /// E-06 con Gmail e IMAP sul server finto: un messaggio arriva in una cartella dell'utente (un filtro del server, un'etichetta
    /// di Gmail), che nessuno guarda. Il giro dei numeri (GetFoldersAsync e AccountItem.SetServerFolders, come
    /// MainWindow.RefreshCountsAsync) dà alla cartella il suo numero nuovo e la dice cambiata, la Posta in arrivo no. Con il costo:
    /// i comandi IMAP e il tempo di un giro (il server finto non ha LIST-STATUS: MailKit chiede una STATUS per cartella, Gmail e
    /// i server con LIST-STATUS una richiesta sola).
    /// </summary>
    static async Task ElCountsAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        ImapProvider.TestPlain = true;
        try
        {
            for (int k = 1; k <= 3; k++) fake.Add("INBOX", SpeedMail("Pratica " + k, "Testo della pratica " + k, daysAgo: 4 - k));
            var provider = FakeProvider(fake, gmail, $"mario.e06.{Arf(gmail).ToLowerInvariant()}@studio-prova.example");
            using var closing = new ArfClosing(provider);
            var item = new UI.AccountItem(provider);
            item.SetServerFolders(await provider.GetFoldersAsync(), UnreadSync.Ticket(provider.Account.Id));
            var user = item.Folders.First(f => f.Folder.Id == fake.UserFolder);
            var before = item.Folders.ToDictionary(f => f.Folder.Id, f => new MailFolder { Id = f.Folder.Id, Name = f.Folder.Name, Unread = f.Folder.Unread, Total = f.Folder.Total });
            fake.Add(fake.UserFolder, SpeedMail("Arrivato con un filtro del server", "Nella cartella dell'utente"));
            int commands = fake.Commands.Count;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var folders = await provider.GetFoldersAsync();
            double ms = clock.Elapsed.TotalMilliseconds;
            item.SetServerFolders(folders, UnreadSync.Ticket(provider.Account.Id));
            int cost = fake.Commands.Count - commands;
            var moved = item.Folders.Where(f => UI.ListRefresh.CountsMoved(before[f.Folder.Id], f.Folder)).Select(f => f.Folder.Id).ToList();
            Report.Add($"      {Arf(gmail)}: un giro dei numeri, {folders.Count} cartelle: {cost} comandi IMAP ({string.Join(", ", fake.Commands.Skip(commands).Select(c => c.Split(' ')[0]).GroupBy(c => c).Select(g => $"{g.Count()} {g.Key}"))}), {ms:0} ms; cambiate: {string.Join(", ", moved)}");
            Check(user.Folder is { Unread: 1, Total: 1 } && moved.SequenceEqual([fake.UserFolder]),
                  $"E-06 ({Arf(gmail)}): un messaggio arrivato in una cartella dell'utente che nessuno guarda: il giro dei numeri del controllo di riserva le dà il numero nuovo ({user.Folder.Unread} da leggere su {user.Folder.Total}) " +
                  "e la dice cambiata, così la cartella aperta si rilegge; la Posta in arrivo e le altre no");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
        }
    }

    /// <summary>
    /// E-06 con Graph (gestore finto): il costo di un giro dei numeri quando la copia locale delle cartelle c'è già (come dopo
    /// l'avvio): mailFolders, le sottocartelle delle cartelle che ne hanno, e l'Archivio, che si chiede sempre. Il numero nuovo di
    /// una cartella arriva.
    /// </summary>
    static async Task ElCountsGraphAsync()
    {
        var urls = new List<string>();
        int clients = 0;
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            lock (urls) urls.Add(path);
            System.Text.Json.Nodes.JsonNode json;
            static System.Text.Json.Nodes.JsonObject F(string id, string name, int unread, int children = 0) =>
                new() { ["id"] = id, ["displayName"] = name, ["unreadItemCount"] = unread, ["totalItemCount"] = unread + 3, ["childFolderCount"] = children };
            if (path.Contains("/childFolders")) json = new System.Text.Json.Nodes.JsonObject { ["value"] = new System.Text.Json.Nodes.JsonArray(F("sotto", "Fornitori", 0)) };
            else if (path.Contains("mailFolders?")) json = new System.Text.Json.Nodes.JsonObject { ["value"] = new System.Text.Json.Nodes.JsonArray(F("in", "Posta in arrivo", 2), F("bozze", "Bozze", 0), F("inviata", "Posta inviata", 0),
                F("cestino", "Posta eliminata", 0), F("spam", "Posta indesiderata", 0), F("archivio", "Archivio", 0), F("clienti", "Clienti", clients, children: 1)) };
            else
            {
                var alias = path.Split('/', '?')[path.Split('/', '?').ToList().IndexOf("mailFolders") + 1];
                json = new System.Text.Json.Nodes.JsonObject { ["id"] = alias switch { "inbox" => "in", "drafts" => "bozze", "sentitems" => "inviata", "deleteditems" => "cestino", "junkemail" => "spam", _ => "archivio" } };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        }));
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "maria.e06@outlook-prova.example" };
        try
        {
            var graph = new GraphProvider(account);
            var first = await graph.GetFoldersAsync(); // all'avvio: anche le cartelle speciali per nome
            MailCache.SaveFolders(account, first);
            int startup;
            lock (urls) { startup = urls.Count; urls.Clear(); }
            clients = 1; // un messaggio arrivato in Clienti
            var again = await graph.GetFoldersAsync();
            List<string> round;
            lock (urls) round = urls.ToList();
            Report.Add($"      Graph: un giro dei numeri, {again.Count} cartelle: {round.Count} richieste ({string.Join(", ", round.Select(u => u.Split('?')[0][(u.IndexOf("mailFolders", StringComparison.Ordinal) is var at and >= 0 ? at : 0)..]))}); all'avvio {startup}");
            Check(again.FirstOrDefault(f => f.Id == "clienti") is { Unread: 1 } && round.Count == 3,
                  "E-06 (Graph): un giro dei numeri dopo l'avvio sono 3 richieste (mailFolders, le sottocartelle di Clienti, l'Archivio) e porta il numero nuovo della cartella");
        }
        finally
        {
            GraphProvider.UseTestHandler(new NoHandler("Graph"));
            MailCache.Delete(account);
        }
    }

    /// <summary>
    /// E-06, i posti (lettura dei sorgenti): il controllo di riserva riuscito (App.CheckAccountAsync) chiama MainWindow.AfterCheck,
    /// che rilegge i numeri quando è ora (ListRefresh.CountsDue, RefreshCountsAsync con SetServerFolders e il biglietto dei non
    /// letti) e poi rilegge la cartella aperta con le regole di ListRefresh (numeri cambiati, o ogni tanto); la Posta in arrivo con
    /// l'IDLE o la query delta è "guardata".
    /// </summary>
    static void ElCountsSources()
    {
        if (ElCode("App.CheckAccountAsync", "UI", "App.cs") is not { } app || ElCode("MainWindow.AfterCheck", "UI", "MainWindow.Freshness.cs") is not { } fresh) return;
        var check = Ch3Part(app, "async Task<int> CheckAccountAsync(", "sealed class CheckFailure");
        var after = Ch3Part(fresh, "internal async void AfterCheck(", "async Task<HashSet<string>> RefreshCountsAsync(");
        var counts = Ch3Part(fresh, "async Task<HashSet<string>> RefreshCountsAsync(", "\n}");
        var watched = Ch3Part(fresh, "bool ListWatched =>", ";");
        bool calls = Ch3InOrder(check, "NoteAccountState(p, null);", "AfterCheck(p, reason == PeriodicReason);", "catch (Exception ex)");
        bool round = Ch3InOrder(after, "ListRefresh.CountsDue(", "RefreshCountsAsync(item)", "ListRefresh.Cause.CountsChanged", "ListRefresh.Wanted(FreshState, cause", "RefreshListAsync(")
                     && Ch3InOrder(counts, "UnreadSync.Ticket(", "GetFoldersAsync()", "SetServerFolders(folders, ticket)", "MailCache.SaveFolders(", "ListRefresh.CountsMoved(");
        bool inbox = watched.Contains("FolderKind.Inbox") && watched.Contains("HasInboxDelta") && watched.Contains("InboxIdle.Watching(");
        Check(calls && round && inbox || Fail($"controllo {calls}, giro {round}, guardata {inbox}"),
              "E-06 nei sorgenti: il controllo di riserva riuscito rilegge i numeri delle cartelle quando è ora (con il biglietto dei non letti) e poi la cartella aperta che nessuno guarda, " +
              "se i suoi numeri sono cambiati o ogni tanto; la Posta in arrivo con l'IDLE o la query delta no");
    }

    // ---------------------------------------------------------------- E-04

    /// <summary>
    /// E-04 (la sonda ProbeConversationRows rovesciata): con le conversazioni (di base) una conversazione di due messaggi e un
    /// messaggio da solo. Tutti e tre usciti altrove (la query delta di Graph, Elimina dalla notifica, le regole: le strade di
    /// ApplyInboxChanges e RemoveNotifiedRow, che ora passano da MessageRows.RemoveMessage): nessuna riga resta. Uscito solo il
    /// più recente della conversazione: la testa diventa l'altro, con il suo numero, al suo posto per data. Uscito un messaggio
    /// di una conversazione aperta: la sua riga figlia esce. Prima restava la riga "r2(2)" con l'oggetto del messaggio uscito.
    /// </summary>
    static void ElConversationRows()
    {
        bool grouped = Store.Settings.GroupByConversation;
        Store.Settings.GroupByConversation = true;
        try
        {
            var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "mario.e04@studio-prova.example", Imap = new ImapServer() }));
            static MessageSummary M(string id, string thread, double hoursAgo) => new()
            {
                Id = id, FolderId = "INBOX", ConversationId = thread, Subject = thread.Length > 0 ? "Preventivo per il bagno" : "Fattura di ottobre",
                Date = DateTime.Now.AddHours(-hoursAgo), From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" },
            };
            static string Shown(UI.MessageRows rows) => string.Join(" ", rows.Rows.Select(r => $"{r.Summary.Id}({r.Count}){(r.IsChild ? "c" : "")}"));
            var all = new UI.MessageRows();
            all.Append([M("r2", "t1", 1), M("x", "", 3), M("r1", "t1", 5)], account);
            string before = Shown(all);
            bool removed = all.RemoveMessage(account, null, "r2", byDate: true) & all.RemoveMessage(account, null, "r1", byDate: true) & all.RemoveMessage(account, "INBOX", "x", byDate: true);
            string after = Shown(all);
            var one = new UI.MessageRows();
            one.Append([M("r2", "t1", 1), M("x", "", 3), M("r1", "t1", 5)], account);
            one.RemoveMessage(account, "INBOX", "r2", byDate: true);
            string headGone = Shown(one);
            var open = new UI.MessageRows();
            open.Append([M("r2", "t1", 1), M("x", "", 3), M("r1", "t1", 5)], account);
            open.Toggle(open.Rows[0]);
            string expanded = Shown(open);
            open.RemoveMessage(account, "INBOX", "r1", byDate: true);
            string childGone = Shown(open);
            bool missing = !open.RemoveMessage(account, "INBOX", "assente", byDate: true) && !open.RemoveMessage(account, "Altra", "r2", byDate: true);
            Report.Add($"      righe {before}; tutti e tre usciti: \"{after}\"; uscita la testa: {headGone}; conversazione aperta {expanded}, uscito il figlio: {childGone}");
            Check(before == "r2(2) x(1)" && removed && after == "" && headGone == "x(1) r1(1)" && expanded == "r2(2) r1(2)c x(1)" && childGone == "r2(1) x(1)" && missing,
                  "E-04: un messaggio uscito esce anche dalla sua conversazione: con tutti usciti non resta nessuna riga; uscita la testa, la testa diventa il messaggio che resta, al suo posto per data; " +
                  "uscito il figlio di una conversazione aperta, esce la sua riga; un messaggio che non c'è, o di un'altra cartella, non tocca nulla");
        }
        finally
        {
            Store.Settings.GroupByConversation = grouped;
        }
        if (ElCode("ApplyInboxChanges e RemoveNotifiedRow", "UI", "MainWindow.Notifications.cs") is not { } notes) return;
        var changes = Ch3Part(notes, "public void ApplyInboxChanges(", "public void RefreshInboxOf(");
        var notified = Ch3Part(notes, "void RemoveNotifiedRow(", "\n    }");
        Check(changes.Contains("rows.RemoveMessage(account, null, id,") && notified.Contains("rows.RemoveMessage(account, message.FolderId, message.Id,") && !notes.Contains("Count == 1"),
              "E-04 nei sorgenti: la query delta (ApplyInboxChanges) e Elimina e Posponi dalla notifica e le regole (RemoveNotifiedRow) tolgono i messaggi anche dalle conversazioni, senza più la regola Count == 1");
    }

    // ---------------------------------------------------------------- E-09

    /// <summary>
    /// E-09 (la sonda ProbeGraphFocusedAsync rovesciata), con il gestore finto di Graph: il controllo dei nuovi messaggi chiede la
    /// scheda (inferenceClassification nel $select) e il messaggio la dice (MessageSummary.Focused, anche nella copia locale e
    /// in WithId); un messaggio di Altra non entra in cima a Evidenziata (ListRefresh.InTab, dentro ListRefresh.Enters di E-10, in
    /// AddIncoming e nelle finestre di una sola cartella), uno senza scheda (Gmail, IMAP) entra come prima. Prima la richiesta non chiedeva la scheda e il
    /// messaggio di Altra entrava in cima a Evidenziata, per sparire alla rilettura.
    /// </summary>
    static async Task ElFocusedAsync()
    {
        var urls = new List<string>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            lock (urls) urls.Add(Uri.UnescapeDataString(request.RequestUri!.PathAndQuery));
            System.Text.Json.Nodes.JsonObject Mail(string id, string tab) => new()
            {
                ["id"] = id, ["subject"] = "Offerte della settimana", ["receivedDateTime"] = DateTime.UtcNow.ToString("o"), ["isRead"] = false,
                ["inferenceClassification"] = tab, ["conversationId"] = "c-" + id, ["parentFolderId"] = "in",
                ["from"] = new System.Text.Json.Nodes.JsonObject { ["emailAddress"] = new System.Text.Json.Nodes.JsonObject { ["name"] = "Negozio di prova", ["address"] = "offerte@negozio-prova.example" } },
            };
            var json = new System.Text.Json.Nodes.JsonObject { ["value"] = new System.Text.Json.Nodes.JsonArray(Mail("n1", "other"), Mail("n2", "focused")) };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        }));
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "maria.e09@outlook-prova.example" };
        try
        {
            var graph = new GraphProvider(account);
            var incoming = await graph.GetNewAsync(DateTime.UtcNow.AddMinutes(-3));
            string newUrl;
            lock (urls) newUrl = urls.FirstOrDefault(u => u.Contains("isRead eq false")) ?? "";
            var other = incoming.FirstOrDefault(m => m.Id == "n1");
            var focusedOne = incoming.FirstOrDefault(m => m.Id == "n2");
            MailCache.SaveMessages(account, "in", true, incoming);
            var cached = MailCache.LoadMessages(account, "in", true) ?? [];
            bool kept = cached.FirstOrDefault(m => m.Id == "n1")?.Focused == false && other?.WithId("n1b").Focused == false;
            var plain = new MessageSummary { Id = "7", FolderId = "INBOX" };
            bool tabs = other != null && focusedOne != null && !UI.ListRefresh.InTab(other, true) && UI.ListRefresh.InTab(focusedOne, true) && UI.ListRefresh.InTab(other, false)
                        && UI.ListRefresh.InTab(other, null) && UI.ListRefresh.InTab(plain, true);
            bool sources = ElCode("AddIncoming", "UI", "MainWindow.xaml.cs") is not { } main
                           || Ch3InOrder(Ch3Part(main, "public void AddIncoming(", "void ShowIncoming("), "ListRefresh.Enters(m, focused, listOptions)", "PrependRows(entering,")
                              && Ch3Part(main, "void ShowIncoming(", "void KeepInSavedPage(").Contains("ListRefresh.Enters(m, focused, listOptions)");
            Report.Add($"      Graph: nuovi chiesti con il $select {(newUrl.Contains("inferenceClassification") ? "con" : "senza")} la scheda; n1 {other?.Focused}, n2 {focusedOne?.Focused}");
            Check(newUrl.Contains("inferenceClassification", StringComparison.Ordinal) && other?.Focused == false && focusedOne?.Focused == true && kept && tabs && sources,
                  "E-09: il controllo dei nuovi messaggi di Graph chiede la scheda e il messaggio la dice (anche nella copia locale); con Evidenziata aperta un messaggio di Altra non entra in cima, " +
                  "uno di Evidenziata sì, e uno senza scheda (Gmail, IMAP) come prima");
        }
        finally
        {
            GraphProvider.UseTestHandler(new NoHandler("Graph"));
            MailCache.Delete(account);
        }
    }

    // ---------------------------------------------------------------- E-08

    /// <summary>
    /// E-08, i posti (lettura dei sorgenti): una rilettura chiesta mentre l'elenco carica (una pagina dopo, la prima pagina, la
    /// vista di tutti gli account) o mentre un'altra rilettura è in corso non si butta: resta da fare (DeferRefresh) e parte alla
    /// fine di quel caricamento (RefreshIfAsked, nei finally di LoadMoreAsync, LoadUnifiedAsync e RefreshListAsync), se
    /// l'elenco è ancora quello. Chi chiede (gli avvisi dell'IDLE, il risveglio) non guarda più se sta caricando. Prima con
    /// loading vero RefreshInboxOf, AfterWake e RefreshListAsync non facevano nulla, e l'avviso di un messaggio uscito si perdeva.
    /// </summary>
    static void ElDeferredSources()
    {
        if (ElCode("la rilettura rimandata", "UI", "MainWindow.xaml.cs") is not { } main || ElCode("la rilettura rimandata", "UI", "MainWindow.Freshness.cs") is not { } fresh
            || ElCode("la rilettura rimandata", "UI", "MainWindow.Unified.cs") is not { } unified || ElCode("la rilettura rimandata", "UI", "MainWindow.Notifications.cs") is not { } notes) return;
        var refresh = Ch3Part(main, "async Task RefreshListAsync(string why)", "MenuItem CategoryMenu(");
        var more = Ch3Part(main, "async Task LoadMoreAsync(bool first = false)", "void ShowFirstPage(");
        var load = Ch3Part(unified, "async Task LoadUnifiedAsync(", "MergedSource UnifiedSource(");
        var asked = Ch3Part(fresh, "void RefreshIfAsked(int version)", "\n    }");
        bool defer = Ch3InOrder(refresh, "if (loading) DeferRefresh(why);", "if (loading || refreshing)", "DeferRefresh(why);", "refreshing = true;", "finally", "refreshing = false;", "RefreshIfAsked(version);");
        bool after = Ch3InOrder(more, "finally", "loading = false;", "RefreshIfAsked(version);") && Ch3InOrder(load, "finally", "loading = false;", "RefreshIfAsked(version);")
                     && Ch3InOrder(asked, "version != listVersion", "refreshAfterLoad = false;", "RefreshListAsync(");
        bool callers = !Ch3Part(notes, "public void RefreshInboxOf(", "internal void RefreshIfMissing(").Contains("loading")
                       && !Ch3Part(main, "internal void AfterWake(", "public void NoteAccountState(").Contains("loading");
        Check(defer && after && callers || Fail($"rimandata {defer}, alla fine {after}, chi chiede {callers}"),
              "E-08 nei sorgenti: una rilettura chiesta mentre l'elenco carica o si rilegge resta da fare e parte alla fine del caricamento, se l'elenco è ancora quello; gli avvisi dell'IDLE e il " +
              "risveglio non la scartano più");
    }

    // ---------------------------------------------------------------- E-07

    /// <summary>
    /// E-07, prima metà (la sonda ProbePageBeforeMoveAsync rovesciata): Elimina con la prima pagina in viaggio. Gmail e IMAP mettono
    /// lo spostamento dietro la pagina sulla stessa connessione, e la pagina arriva con il messaggio eliminato. Con le chiamate
    /// della finestra (RemoveRows segna la riga tolta nel ListJournal; la pagina, chiesta con il segno di prima, passa da
    /// ListJournal.Apply prima di ShowFirstPage) la riga non torna. Prima tornava: la versione dell'elenco non cambia con Elimina,
    /// e l'EXPUNGE che arriva all'IDLE è l'eco di Missiva.
    /// </summary>
    static async Task ElPageBeforeMoveAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        ImapProvider.TestPlain = true;
        try
        {
            for (int k = 1; k <= 6; k++) fake.Add("INBOX", SpeedMail("Pratica " + k, "Testo della pratica " + k, daysAgo: 7 - k));
            var provider = FakeProvider(fake, gmail, $"mario.e07a.{Arf(gmail).ToLowerInvariant()}@studio-prova.example");
            using var closing = new ArfClosing(provider);
            var folders = await provider.GetFoldersAsync();
            var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
            var trash = folders.First(f => f.Kind == FolderKind.Deleted);
            var cached = (await provider.GetMessagesAsync(inbox, null, null)).Messages;
            var top = cached[0];
            var journal = new UI.ListJournal();
            var held = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            fake.Hold = async command =>
            {
                if (command != "FETCH" || held.Task.IsCompleted) return;
                held.TrySetResult();
                await release.Task;
            };
            var since = journal.Asked();
            var request = provider.GetMessagesAsync(inbox, null, null);
            bool reached = await Task.WhenAny(held.Task, Task.Delay(TimeSpan.FromSeconds(5))) == held.Task;
            journal.Removed(provider.Account.Id, [top]); // RemoveRows, prima di MoveAsync
            var move = provider.MoveAsync([top], trash);
            await Task.Delay(300);
            fake.Hold = null;
            release.SetResult();
            var fresh = (await request).Messages;
            await move;
            bool stale = fresh.Any(m => m.Id == top.Id);
            var (removed, _) = journal.Apply(since, provider.Account.Id, inbox.Id, fresh, arrivals: true, byDate: true);
            var account = new UI.AccountItem(provider);
            var rows = new UI.MessageRows();
            rows.Append(fresh, account); // ShowFirstPage rifà l'elenco con la pagina corretta
            bool ghost = rows.Rows.Any(r => r.Summary.Id == top.Id);
            Report.Add($"      {Arf(gmail)}: la pagina chiesta prima di Elimina aveva il messaggio eliminato: {stale}; tolto dalla pagina {removed}; riga di nuovo nell'elenco: {ghost}");
            Check(reached && stale && removed == 1 && !ghost && fresh.Count == 5,
                  $"E-07 ({Arf(gmail)}): Elimina con la prima pagina in viaggio: la pagina arriva ancora con il messaggio, ma la riga tolta da allora le viene tolta e non torna nell'elenco");
        }
        finally
        {
            fake.Hold = null;
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
        }
    }

    /// <summary>
    /// E-07, seconda metà (la sonda ProbePageBeforeArrivalAsync rovesciata; ESITO.md, "un arrivo proprio mentre la prima pagina è
    /// in viaggio"): il messaggio nuovo trovato dal controllo entra in cima (AddIncoming lo segna nel ListJournal), poi arriva la
    /// pagina chiesta prima del suo arrivo, senza di lui. ListJournal.Apply glielo rimette in cima: resta nell'elenco e nella
    /// prima pagina salvata. Prima spariva da tutte e due, e il controllo dopo non lo riproponeva (era fra i notificati).
    /// </summary>
    static async Task ElPageBeforeArrivalAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail)) { AnnounceOnNoop = true };
        ImapProvider.TestPlain = true;
        try
        {
            for (int k = 1; k <= 6; k++) fake.Add("INBOX", SpeedMail("Pratica " + k, "Testo della pratica " + k, daysAgo: 7 - k), "\\Seen");
            var provider = FakeProvider(fake, gmail, $"mario.e07b.{Arf(gmail).ToLowerInvariant()}@studio-prova.example");
            using var closing = new ArfClosing(provider);
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var since3 = DateTime.UtcNow.AddMinutes(-3);
            await provider.GetMessagesAsync(inbox, null, null);
            var journal = new UI.ListJournal();
            var held = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            fake.Hold = async command =>
            {
                if (command != "FETCH" || held.Task.IsCompleted) return;
                held.TrySetResult();
                await release.Task;
            };
            var since = journal.Asked();
            var request = provider.GetMessagesAsync(inbox, null, null);
            bool reached = await Task.WhenAny(held.Task, Task.Delay(TimeSpan.FromSeconds(5))) == held.Task;
            fake.Add("INBOX", SpeedMail("Appena arrivato", "Arriva mentre la pagina è in viaggio"));
            var incoming = await provider.GetNewAsync(since3);
            journal.Arrived(provider.Account.Id, incoming); // AddIncoming: in cima, e nel ListJournal
            fake.Hold = null;
            release.SetResult();
            var fresh = (await request).Messages;
            bool missing = !fresh.Any(m => m.Subject == "Appena arrivato");
            var (_, added) = journal.Apply(since, provider.Account.Id, inbox.Id, fresh, arrivals: true, byDate: true);
            // Una pagina chiesta dopo l'arrivo non ne ha due: c'è già.
            var later = journal.Asked();
            var after = (await provider.GetMessagesAsync(inbox, null, null)).Messages;
            var (_, twice) = journal.Apply(since, provider.Account.Id, inbox.Id, after, arrivals: true, byDate: true);
            var (_, none) = journal.Apply(later, provider.Account.Id, inbox.Id, after, arrivals: true, byDate: true);
            Report.Add($"      {Arf(gmail)}: arrivato durante la pagina: trovato dal controllo {incoming.Count}, mancava nella pagina {missing}, rimesso {added} (in cima: {fresh.FirstOrDefault()?.Subject})");
            Check(reached && incoming.Count == 1 && missing && added == 1 && fresh[0].Subject == "Appena arrivato" && twice == 0 && none == 0 && after.Count(m => m.Subject == "Appena arrivato") == 1,
                  $"E-07 ({Arf(gmail)}): un arrivo mentre la prima pagina è in viaggio resta in cima quando la pagina chiesta prima arriva senza di lui (e così nella prima pagina salvata); " +
                  "una pagina che lo ha già non lo ha due volte");
        }
        finally
        {
            fake.Hold = null;
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
        }
    }

    /// <summary>
    /// E-07, ListJournal da solo: vale l'ultima cosa successa a ogni messaggio (tolto e poi rimesso con Annulla, o rifiutato dal
    /// server: c'è; arrivato e poi eliminato: non c'è), solo per l'account e, per gli arrivi, la cartella della pagina; le pagine
    /// dopo (senza arrivi) perdono solo i tolti; una pagina chiesta dopo non cambia.
    /// </summary>
    static void ElJournalRules()
    {
        static MessageSummary M(string id, string folder = "INBOX", int hoursAgo = 1) => new() { Id = id, FolderId = folder, Subject = "Messaggio " + id, Date = DateTime.Now.AddHours(-hoursAgo) };
        var journal = new UI.ListJournal();
        var since = journal.Asked();
        journal.Removed("a", [M("1")]);
        journal.Arrived("a", [M("1")]);            // Annulla: è tornato
        journal.Arrived("a", [M("9", hoursAgo: 0)]); // arrivato
        journal.Removed("a", [M("9")]);            // e poi eliminato dalla notifica
        journal.Removed("a", [M("2")]);            // eliminato
        journal.Arrived("a", [M("7", "Clienti", 0)]); // un'altra cartella
        journal.Removed("b", [M("3")]);            // un altro account
        journal.Arrived("a", [M("8", hoursAgo: 0)]);
        var page = new List<MessageSummary> { M("1", hoursAgo: 2), M("2", hoursAgo: 3), M("3", hoursAgo: 4) };
        var applied = journal.Apply(since, "a", "INBOX", page, arrivals: true, byDate: true);
        string first = string.Join(" ", page.Select(m => m.Id));
        var next = new List<MessageSummary> { M("2", hoursAgo: 30), M("4", hoursAgo: 31) };
        var nextApplied = journal.Apply(since, "a", "INBOX", next, arrivals: false, byDate: true);
        var later = new List<MessageSummary> { M("2", hoursAgo: 3) };
        var laterApplied = journal.Apply(journal.Asked(), "a", "INBOX", later, arrivals: true, byDate: true);
        Check(first == "8 1 3" && applied == (1, 1) && string.Join(" ", next.Select(m => m.Id)) == "4" && nextApplied == (1, 0) && laterApplied == (0, 0) && later.Count == 1,
              $"E-07, le regole del ListJournal: vale l'ultima cosa successa a ogni messaggio (rimesso con Annulla c'è, arrivato ed eliminato no), solo per l'account e la cartella della pagina " +
              $"(prima pagina: {first}); le pagine dopo perdono solo i tolti; una pagina chiesta dopo resta com'è");
    }

    /// <summary>
    /// E-07, i posti (lettura dei sorgenti): le pagine (prima e dopo, la rilettura, le fonti di tutti gli account) prendono il segno
    /// prima di chiedere e passano da ListJournal.Apply prima di diventare righe; segnano i tolti RemoveRows, la query delta e
    /// RemoveNotifiedRow, gli arrivati AddIncoming, la vista di tutti gli account, le finestre di una sola cartella, Annulla e gli
    /// spostamenti rifiutati.
    /// </summary>
    static void ElJournalSources()
    {
        if (ElCode("le pagine e il ListJournal", "UI", "MainWindow.xaml.cs") is not { } main || ElCode("le pagine e il ListJournal", "UI", "MainWindow.Unified.cs") is not { } unified
            || ElCode("le pagine e il ListJournal", "UI", "MainWindow.Notifications.cs") is not { } notes) return;
        var more = Ch3Part(main, "async Task LoadMoreAsync(bool first = false)", "void ShowFirstPage(");
        var refresh = Ch3Part(main, "async Task RefreshListAsync(string why)", "MenuItem CategoryMenu(");
        var source = Ch3Part(unified, "MergedSource UnifiedSource(", "\n    });");
        bool pages = Ch3InOrder(more, "journal.Asked()", "GetMessagesAsync(", "journal.Apply(since, account.Id, current.Folder.Id, page.Messages, arrivals: first,", "ShowFirstPage(")
                     && Ch3InOrder(refresh, "journal.Asked()", "GetMessagesAsync(", "journal.Apply(since,", "MergeFirstPage(")
                     && Ch3InOrder(source, "journal.Asked()", "GetMessagesAsync(", "journal.Apply(since,");
        bool removals = Ch3InOrder(Ch3Part(main, "List<(int Index, List<MessageSummary> Messages)> RemoveRows(", "internal static bool Testing"), "journal.Removed(", "rows.Remove(i)")
                        && Ch3Part(notes, "public void ApplyInboxChanges(", "bool ByDateNewest").Contains("journal.Removed(") && Ch3Part(notes, "void RemoveNotifiedRow(", "\n    }").Contains("journal.Removed(");
        bool arrivals = Ch3InOrder(Ch3Part(main, "public void AddIncoming(", "void ShowIncoming("), "PrependRows(entering,", "journal.Arrived(")
                        && Ch3Part(main, "void ShowIncoming(", "void KeepInSavedPage(").Contains("journal.Arrived(") && Ch3Part(unified, "bool AddIncomingUnified(", "\n    }").Contains("journal.Arrived(")
                        && Ch3InOrder(Ch3Part(main, "async Task UndoAsync()", "internal static string UndoStartedText("), "rows.Restore(", "journal.Arrived(")
                        && Ch3InOrder(Ch3Part(main, "void PutBackFailed(", "static void FollowFlags("), "rows.RestoreFailed(", "journal.Arrived(");
        Check(pages && removals && arrivals || Fail($"pagine {pages}, tolti {removals}, arrivati {arrivals}"),
              "E-07 nei sorgenti: ogni pagina (prima, dopo, riletta, di tutti gli account) prende il segno prima di chiedere e passa dal ListJournal prima di diventare righe; i tolti (Elimina, " +
              "Sposta, la query delta, la notifica, le regole) e gli arrivati (in cima, tutti gli account, finestre di una cartella, Annulla, spostamenti rifiutati) ci finiscono");
    }

    // ---------------------------------------------------------------- E-05

    /// <summary>
    /// E-05, i posti (lettura dei sorgenti; la prova con un account che non risponde è a finestra, strumenti\arrivo\elenco.ps1 -Prova
    /// E05). (a) Alla prima pagina della vista di tutti gli account l'account più lento non fa aspettare gli altri: dopo SlowSource
    /// è in ritardo (Late), non conta per Cutoff né si richiede, l'elenco si mostra con gli altri (con le sue righe della copia
    /// locale) e la riga sopra lo dice; quando arriva le righe si rifanno fin dove ora sono arrivati tutti, e quelle più vecchie
    /// tornano da mostrare scorrendo (LateSourceAsync). (b) Le pagine passano dal ListJournal (E-07). (c) Un avviso dell'IDLE che
    /// fa rileggere la Posta in arrivo rilegge anche le viste Posta in arrivo e Non letti di tutti gli account, senza svuotarle.
    /// (d) Contrassegnati, Bozze e Posta inviata di tutti gli account non sono "guardate": si rileggono con il controllo di
    /// riserva (E-06). Prima Task.WhenAll aspettava tutti (fino a due minuti), e RefreshInboxOf guardava solo una cartella aperta.
    /// </summary>
    static void ElUnifiedSources()
    {
        if (ElCode("la vista di tutti gli account", "UI", "MainWindow.Unified.cs") is not { } unified || ElCode("la vista di tutti gli account", "UI", "MainWindow.Notifications.cs") is not { } notes
            || ElCode("la vista di tutti gli account", "UI", "MainWindow.Freshness.cs") is not { } fresh) return;
        var next = Ch3Part(unified, "async Task<List<(MessageSummary Message, AccountItem Account)>?> NextMergedAsync(", "async Task LateSourceAsync(");
        var late = Ch3Part(unified, "async Task LateSourceAsync(", "static async Task FetchAsync(");
        var load = Ch3Part(unified, "async Task LoadUnifiedAsync(", "MergedSource UnifiedSource(");
        var status = Ch3Part(unified, "void ShowMergedStatus(", "\n    }");
        bool slow = Ch3InOrder(next, "!s.Late &&", "Task.WhenAny(all, Task.Delay(SlowSource))", "s.Late = true;", "LateSourceAsync(s, work, version)", "else await all;")
                    && Ch3Part(unified, "DateTime Cutoff()", ";").Contains("!s.Late");
        bool after = Ch3InOrder(late, "await fetch;", "version != listVersion", "late.Late = false;", "Cutoff();", "Pending.Add(m)", "Release();", "rows.Clear();", "rows.Append(all);", "ShowMergedStatus(")
                     && Ch3InOrder(load, "s.Late", "rows.Clear();", "shown.Concat(keep)") && status.Contains("non ha ancora risposto") && status.Contains("non hanno ancora risposto");
        bool reread = Ch3InOrder(Ch3Part(notes, "public void RefreshInboxOf(", "internal void RefreshIfMissing("), "unified is { View: UnifiedView.Inbox or UnifiedView.Unread }", "RefreshListAsync(why)");
        bool watched = Ch3Part(fresh, "bool ListWatched =>", ";").Contains("unified is { View: UnifiedView.Inbox or UnifiedView.Unread }");
        Check(slow && after && reread && watched || Fail($"lento {slow}, dopo {after}, riletta {reread}, guardate {watched}"),
              "E-05 nei sorgenti: alla prima pagina di tutti gli account il più lento non fa aspettare gli altri (dopo 3 s arriva dopo, senza rompere l'ordine, e la riga lo dice); un avviso che fa " +
              "rileggere la Posta in arrivo rilegge anche le viste Posta in arrivo e Non letti; Contrassegnati, Bozze e Posta inviata si rileggono con il controllo di riserva");
    }

    // ---------------------------------------------------------------- E-13

    /// <summary>
    /// E-13, i posti (lettura dei sorgenti; le finestre non si costruiscono nella suite). Partito un messaggio (ComposeWindow
    /// AfterSent dice Sent, che la finestra principale ascolta come DraftsChanged), in ogni finestra la Posta inviata aperta
    /// dell'account, o quella di tutti gli account, si rilegge poco dopo. Spostati ed eliminati da una finestra (riusciti o in coda)
    /// escono anche dalle altre (ShareRemoved, RemoveShared con MessageRows.RemoveMessage e il ListJournal); letti e da leggere,
    /// a mano e dal riquadro, cambiano le righe anche nelle altre (ShareRead, ReadShared per messaggio). Prima dopo Invia si
    /// avvisavano solo le Bozze, e ogni finestra cambiava solo le sue righe.
    /// </summary>
    static void ElWindowsSources()
    {
        if (ElCode("le finestre", "UI", "MainWindow.xaml.cs") is not { } main || ElCode("le finestre", "UI", "MainWindow.Windows.cs") is not { } windows
            || ElCode("le finestre", "UI", "ComposeWindow.xaml.cs") is not { } compose) return;
        bool sent = Ch3InOrder(Ch3Part(compose, "void AfterSent(", "\n    }"), "Sent?.Invoke(provider);", "Close();") && Ch3Part(main, "void ShowCompose(", "\n    }").Contains("window.Sent += OnSent;")
                    && Ch3InOrder(Ch3Part(windows, "async void RefreshSentLater(", "\n    }"), "Task.Delay(SentSettle)", "FolderKind.Sent", "RefreshListAsync(\"messaggio inviato\")", "UnifiedView.Sent", "RefreshListAsync(\"messaggio inviato\")")
                    && Ch3Part(windows, "void OnSent(", "\n    }").Contains("window.RefreshSentLater(provider)");
        var move = Ch3Part(main, "async Task<bool> MoveAsync(", "void PutBackFailed(");
        var delete = Ch3Part(main, "async Task<bool> DeleteAsync(", "sealed record MovePart(");
        bool removed = move.Contains("ShareRemoved(account, moved.Select(x => x.Original).ToList())") && delete.Contains("ShareRemoved(account, moved.Select(x => x.Original).ToList())")
                       && delete.Contains("ShareRemoved(account, forever)") && Ch3InOrder(Ch3Part(windows, "internal void RemoveShared(", "\n    }"), "journal.Removed(", "rows.RemoveMessage(");
        bool read = Ch3Part(main, "async Task SetReadAsync(", "async void OnToggleFlag(").Split("ShareRead(group.Key,").Length == 3 && Ch3Part(main, "void ReadReaderRows(", "\n    }").Contains("ShareRead(account, [summary], read)")
                    && Ch3Part(windows, "internal void ReadShared(", "\n    }").Contains("ReadMarks.Matches(kind, messages)");
        Check(sent && removed && read || Fail($"inviato {sent}, tolti {removed}, letti {read}"),
              "E-13 nei sorgenti: dopo un invio la Posta inviata aperta (dell'account o di tutti gli account) si rilegge, in ogni finestra; spostati, eliminati e segni di lettura fatti in una " +
              "finestra arrivano anche alle altre sulla stessa cartella");
    }

    // ---------------------------------------------------------------- E-12

    /// <summary>
    /// E-12, le regole e l'ordine dei gruppi. La prima pagina riletta di Gmail e IMAP (per numero d'ordine) ha in fondo un messaggio
    /// rimesso con una data di un mese fa: per data (la regola di prima) sotto la pagina non resta nessuno dei messaggi caricati
    /// scorrendo, che sparivano; per UID (ListRefresh.Below) restano quelli con l'UID più basso della pagina, e quello eliminato
    /// dentro la pagina no. Poi la pagina dopo, più recente di lui: aggiunta in fondo (la regola di prima) il gruppo "Questo mese"
    /// nasce dopo quello più vecchio e WPF lo mette in fondo; riconosciuta fuori ordine (ListRefresh.OutOfOrder) e rifatto l'elenco
    /// in ordine di data (MainWindow.RebuildInOrder), i gruppi sono in ordine.
    /// </summary>
    static void ElOrderRules()
    {
        static MessageSummary M(uint uid, double daysAgo) => new()
        {
            Id = uid.ToString(), FolderId = "INBOX", Subject = "Pratica " + uid, Date = DateTime.Today.AddHours(12).AddDays(-daysAgo),
            From = new Address { Name = "Luca Bianchi", Email = "luca.bianchi@cliente-prova.example" },
        };
        // Caricati: la prima pagina (UID 71-120, ogni 2 ore fino a 4 giorni fa) e la seconda (21-70, fino a 8 giorni fa).
        var loaded = Enumerable.Range(21, 100).Select(k => M((uint)k, (120 - k) / 12.0)).ToList();
        // Riletta: arrivato il 125 con la data di un mese fa; il 100 è stato eliminato sul telefono; la pagina parte dal 72.
        var fresh = Enumerable.Range(72, 49).Where(k => k != 100).Select(k => M((uint)k, (120 - k) / 12.0)).Append(M(125, 34)).OrderByDescending(m => m.Date).ToList();
        var byUid = UI.ListRefresh.Below(loaded, fresh, byUid: true);
        var byDate = UI.ListRefresh.Below(loaded, fresh, byUid: false);
        bool below = byUid.Count == 51 && byUid.All(m => uint.Parse(m.Id) < 72) && !byUid.Any(m => m.Id == "100") && byDate.Count == 0
                     && UI.ListRefresh.Below(loaded, fresh, byUid: true, more: false).Count == 0 && UI.ListRefresh.Below(loaded, fresh, byUid: false, more: false).Count == 0;
        bool order = UI.ListRefresh.OutOfOrder(fresh.Min(m => m.Instant), [M(60, 6)]) && !UI.ListRefresh.OutOfOrder(DateTime.Today.AddDays(-6).ToUniversalTime(), [M(60, 7)]);
        // I gruppi: la prima pagina con in fondo il messaggio rimesso, di due mesi fa (il gruppo con il nome del mese), poi la pagina
        // dopo, del mese scorso (il gruppo "Mese scorso", che la prima pagina non ha), qualunque sia il giorno di oggi.
        var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "mario.e12@studio-prova.example", Imap = new ImapServer() }));
        static MessageSummary On(uint uid, DateTime date) { var m = M(uid, 0); m.Date = date; return m; }
        var lastMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        var first = new[] { M(130, 0), M(129, 1), On(125, lastMonth.AddMonths(-1).AddHours(10)) };
        var next = new[] { On(60, lastMonth.AddDays(14).AddHours(9)), On(59, lastMonth.AddDays(13).AddHours(9)), On(58, lastMonth.AddDays(12).AddHours(9)) };
        static (UI.MessageRows Rows, System.Windows.Data.ListCollectionView View) Grouped()
        {
            var rows = new UI.MessageRows();
            var view = (System.Windows.Data.ListCollectionView)System.Windows.Data.CollectionViewSource.GetDefaultView(rows.Rows);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(UI.MessageItem.Group)));
            return (rows, view);
        }
        static List<DateTime> Heads(System.Windows.Data.ListCollectionView view) =>
            view.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(g => g.Items.Cast<UI.MessageItem>().Max(i => i.Summary.Date)).ToList();
        static bool Descending(List<DateTime> heads) => heads.Zip(heads.Skip(1)).All(p => p.First > p.Second);
        var (before, beforeView) = Grouped();
        before.Append(first, account);
        before.Append(next, account); // com'era: in fondo
        var (after, afterView) = Grouped();
        after.Append(first, account);
        var shown = after.Rows.SelectMany(UI.MessageRows.MessagesOf).ToList();
        if (UI.ListRefresh.OutOfOrder(shown.Min(m => m.Instant), next))
        {
            var all = shown.Concat(next).OrderByDescending(m => m.Instant).ToList(); // come RebuildInOrder
            after.Clear();
            after.Append(all, account);
        }
        else after.Append(next, account);
        beforeView.Refresh();
        afterView.Refresh();
        string Names(System.Windows.Data.ListCollectionView view) => string.Join(", ", view.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(g => g.Name));
        Report.Add($"      gruppi con la pagina in fondo: {Names(beforeView)}; rifatto in ordine: {Names(afterView)}");
        Check(below && order && !Descending(Heads(beforeView)) && Descending(Heads(afterView)) || Fail($"sotto la pagina {below} ({byUid.Count}, per data {byDate.Count}), fuori ordine {order}"),
              "E-12: con un messaggio dalla data vecchia in fondo alla prima pagina riletta, i messaggi caricati scorrendo restano (per UID: prima per data sparivano tutti) e quello eliminato no; " +
              "la pagina dopo più recente di lui rifà l'elenco in ordine, e i gruppi di date sono in ordine (aggiunta in fondo, un gruppo più recente finiva dopo uno più vecchio)");
    }

    /// <summary>E-12, i posti (lettura dei sorgenti): MergeFirstPage tiene i caricati con ListRefresh.Below (per UID con Gmail e IMAP) e rifà tutto in ordine di data; la pagina dopo fuori ordine rifà l'elenco (RebuildInOrder).</summary>
    static void ElOrderSources()
    {
        if (ElCode("l'ordine dell'elenco", "UI", "MainWindow.xaml.cs") is not { } main) return;
        var merge = Ch3Part(main, "bool MergeFirstPage(", "void RebuildInOrder(");
        var more = Ch3Part(main, "async Task LoadMoreAsync(bool first = false)", "void ShowFirstPage(");
        bool merged = Ch3InOrder(merge, "ListRefresh.Below(loaded, fresh, byUid: account.Provider.Account.Kind != AccountKind.Microsoft, more)", "rows.Append(fresh.Concat(older).OrderByDescending(")
                      && Ch3Part(main, "async Task RefreshListAsync(string why)", "MenuItem CategoryMenu(").Contains("MergeFirstPage(page.Messages, current.Account, more: page.Next != null)")
                      && !merge.Contains("m.Date <= oldest");
        bool rebuilt = Ch3InOrder(more, "ListRefresh.OutOfOrder(", "RebuildInOrder(page.Messages, current.Account)", "rows.Append(page.Messages, current.Account)")
                       && Ch3Part(main, "void RebuildInOrder(", "\n    }").Contains("OrderByDescending(");
        Check(merged && rebuilt || Fail($"unione {merged}, pagina dopo {rebuilt}"),
              "E-12 nei sorgenti: la prima pagina riletta tiene i caricati per UID con Gmail e IMAP e rifà tutto in ordine di data; una pagina dopo fuori ordine rifà l'elenco in ordine");
    }

    // ---------------------------------------------------------------- E-10

    /// <summary>
    /// E-10, la regola (ListRefresh.Enters, in AddIncoming e nelle finestre di una cartella al posto della sola scheda): un messaggio
    /// nuovo entra in cima solo se l'elenco con il suo filtro, il suo ordine e "Vai a" lo mostrerebbe lì: non con il filtro dei
    /// contrassegnati se non lo è, non con "da leggere" se è già letto, non con quello degli allegati se non ne ha, non con l'ordine
    /// dal più vecchio o per mittente o oggetto, non con "Vai a" a un giorno prima del suo; con la vista normale sì. Prima entrava
    /// sempre in cima.
    /// </summary>
    static void ElEntersRules()
    {
        var plain = new MessageSummary { Id = "1", FolderId = "INBOX", Date = DateTime.Now, IsRead = false };
        var flagged = new MessageSummary { Id = "2", FolderId = "INBOX", Date = DateTime.Now, IsFlagged = true, IsRead = true };
        var withFile = new MessageSummary { Id = "3", FolderId = "INBOX", Date = DateTime.Now, HasAttachments = true, IsRead = true };
        bool In(MessageSummary m, ListOptions o) => UI.ListRefresh.Enters(m, null, o);
        var d = ListOptions.Default;
        bool rule = In(plain, d) && !In(plain, d with { Filter = MessageFilter.Flagged }) && In(flagged, d with { Filter = MessageFilter.Flagged })
                    && In(plain, d with { Filter = MessageFilter.Unread }) && !In(flagged, d with { Filter = MessageFilter.Unread })
                    && !In(plain, d with { Filter = MessageFilter.Attachments }) && In(withFile, d with { Filter = MessageFilter.Attachments })
                    && !In(plain, d with { Ascending = true }) && !In(plain, d with { Sort = SortBy.From, Ascending = true }) && !In(plain, d with { Sort = SortBy.Subject })
                    && !In(plain, d with { UpTo = DateTime.Today.AddDays(-3) }) && In(plain, d with { UpTo = DateTime.Today })
                    && !UI.ListRefresh.Enters(new MessageSummary { Id = "4", FolderId = "INBOX", Date = DateTime.Now, Focused = false }, true, d);
        bool sources = ElCode("AddIncoming", "UI", "MainWindow.xaml.cs") is not { } main
                       || Ch3Part(main, "public void AddIncoming(", "void ShowIncoming(").Contains("ListRefresh.Enters(m, focused, listOptions)")
                          && Ch3Part(main, "void ShowIncoming(", "void KeepInSavedPage(").Contains("ListRefresh.Enters(m, focused, listOptions)");
        Check(rule && sources || Fail($"regola {rule}, sorgenti {sources}"),
              "E-10: un messaggio nuovo entra in cima solo se l'elenco con il suo filtro, il suo ordine e \"Vai a\" lo mostrerebbe lì (e nella sua scheda); con la vista normale come prima");
    }

    // ---------------------------------------------------------------- L-10

    /// <summary>
    /// L-10, la parte dell'elenco: se il server conta meno da leggere di quanti l'elenco aperto ne mostra per quella Posta in
    /// arrivo, l'elenco si rilegge (RereadIfCountBelowRows, da AddIncoming quando il numero del server vale), una volta per numero o
    /// ogni cinque minuti, anche con la vista di tutti gli account (RefreshInboxOf, E-05). Prima il numero diceva 0 e la riga
    /// restava in grassetto.
    /// </summary>
    static void ElCountRules()
    {
        bool rule = UI.ListRefresh.CountBelowRows(0, 1) && !UI.ListRefresh.CountBelowRows(1, 1) && !UI.ListRefresh.CountBelowRows(5, 2);
        bool sources = ElCode("AddIncoming", "UI", "MainWindow.xaml.cs") is not { } main || ElCode("RereadIfCountBelowRows", "UI", "MainWindow.Freshness.cs") is not { } fresh
                       || Ch3InOrder(Ch3Part(main, "public void AddIncoming(", "void ShowIncoming("), "inbox.ApplyServerUnread(unread.Value, ticket, who)", "else RereadIfCountBelowRows(inbox, unread.Value);")
                          && Ch3InOrder(Ch3Part(fresh, "void RereadIfCountBelowRows(", "\n    }"), "!m.IsRead && m.FolderId == inbox.Folder.Id", "ListRefresh.CountBelowRows(serverUnread, shown)",
                                        "TimeSpan.FromMinutes(5)", "RefreshInboxOf(inbox.Account.Provider,");
        Check(rule && sources || Fail($"regola {rule}, sorgenti {sources}"),
              "L-10, la parte dell'elenco: con il server che conta meno da leggere di quanti l'elenco ne mostra per la Posta in arrivo, l'elenco (anche di tutti gli account) si rilegge, " +
              "una volta per numero o ogni cinque minuti");
    }

    // ---------------------------------------------------------------- E-15

    /// <summary>
    /// E-15, i posti (lettura dei sorgenti): la prima pagina arrivata con gli stessi messaggi della copia cambia anche le categorie e
    /// la scadenza del contrassegno delle righe, non solo letto e contrassegno; Svuota con una ricerca aperta intanto non rifà
    /// l'elenco della cartella (che lasciava "Ricerca in corso…" per sempre).
    /// </summary>
    static void ElMinorSources()
    {
        if (ElCode("E-15", "UI", "MainWindow.xaml.cs") is not { } main) return;
        var same = Ch3Part(main, "void ShowFirstPage(", "foreach (var m in fresh)\n            if (m.Preview.Length == 0");
        var empty = Ch3Part(main, "async Task EmptyFolderAsync(", "async Task MarkAllReadAsync(");
        bool first = Ch3InOrder(same, "old.Categories.SequenceEqual(m.Categories)", "old.FlagDue != m.FlagDue", "old.Categories = m.Categories", "old.FlagDue = m.FlagDue", "row.RefreshCategories()", "row.RefreshFlag()");
        bool search = empty.Contains("if (open && !searching) await ReloadAsync();");
        Check(first && search || Fail($"prima pagina {first}, svuota {search}"),
              "E-15 nei sorgenti: la prima pagina uguale alla copia aggiorna anche categorie e scadenze delle righe; Svuota con una ricerca aperta non rifà l'elenco sotto la ricerca");
    }

    // ---------------------------------------------------------------- l'ora legale

    /// <summary>
    /// L'ora legale (nota SOSPETTO del rapporto sull'arrivo, "Guardato e trovato a posto"): le righe hanno la data nell'ora di
    /// Windows, e nell'ora ripetuta della notte del ritorno all'ora solare un messaggio arrivato dopo il cambio (alle 2:10 della
    /// seconda volta) ha un'ora locale più bassa di uno arrivato prima (alle 2:50 della prima). Qui, con il fuso di Windows di
    /// questo PC, due messaggi a dieci minuti dal cambio sul server IMAP finto (INTERNALDATE): la prima pagina, le righe nuove in
    /// cima (MessageRows.Prepend) e la copia locale li mettono per istante (MessageSummary.Instant), il più recente sopra, con l'ora
    /// locale di sempre. Prima, per ora locale, quello di prima del cambio stava sopra.
    /// </summary>
    static async Task ElDaylightAsync()
    {
        // Il ritorno all'ora solare del fuso di Windows più vicino a oggi: dove l'offset scende.
        var zone = TimeZoneInfo.Local;
        DateTime? change = null;
        var start = new DateTime(DateTime.UtcNow.Year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var t = start; t < start.AddYears(3) && change == null; t = t.AddMinutes(30))
            if (zone.GetUtcOffset(t.AddMinutes(30)) < zone.GetUtcOffset(t)) change = t.AddMinutes(30);
        if (change is not { } at)
        {
            Report.Add($"      (il fuso di Windows di questo PC, {zone.Id}, non ha l'ora legale: l'ordine nell'ora ripetuta non si misura)");
            return;
        }
        var before = new DateTimeOffset(at.AddMinutes(-10));
        var after = new DateTimeOffset(at.AddMinutes(10));
        bool repeated = after.LocalDateTime < before.LocalDateTime;
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var account = SpeedAccount(fake, "mario.ora@studio-prova.example");
        try
        {
            MimeKit.MimeMessage Mail(string subject, DateTimeOffset date)
            {
                var mail = SpeedMail(subject, subject);
                mail.Date = date;
                return mail;
            }
            fake.Add("INBOX", Mail("Prima del cambio dell'ora", before));
            fake.Add("INBOX", Mail("Dopo il cambio dell'ora", after));
            using var imap = new ImapProvider(account);
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var page = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            string Order(IEnumerable<MessageSummary> list) => string.Join(", ", list.Select(m => m.Subject.StartsWith("Dopo") ? "dopo" : "prima"));
            var item = new UI.AccountItem(imap);
            var rows = new UI.MessageRows();
            rows.Prepend(page, item);
            MailCache.SaveMessages(account, "INBOX", null, page);
            var cached = (MailCache.LoadMessages(account, "INBOX", null) ?? []).OrderByDescending(m => m.Instant).ToList();
            string shown = string.Join(" ", page.Select(m => m.Date.ToString("HH:mm")));
            Report.Add($"      ora legale ({zone.Id}): cambio alle {at:HH:mm} UTC del {at:d}; ore locali {before.LocalDateTime:HH:mm} e {after.LocalDateTime:HH:mm}; prima pagina: {Order(page)} ({shown}); " +
                       $"righe nuove in cima: {Order(rows.Rows.Select(r => r.Summary))}; copia locale: {Order(cached)}");
            Check(repeated && Order(page) == "dopo, prima" && Order(rows.Rows.Select(r => r.Summary)) == "dopo, prima" && Order(cached) == "dopo, prima"
                  && shown == $"{after.LocalDateTime:HH:mm} {before.LocalDateTime:HH:mm}",
                  "l'ora legale: nell'ora ripetuta della notte del ritorno all'ora solare il messaggio arrivato dopo il cambio sta sopra quello arrivato prima (per istante), nella prima pagina, " +
                  "nelle righe nuove in cima e nella copia locale, con l'ora locale di sempre");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            MailCache.Delete(account);
        }
    }
}
#endif
