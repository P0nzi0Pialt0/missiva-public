#if DEBUG
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Missiva;

/// <summary>
/// Audit di affidabilità del 25 ottobre 2026, letti e non letti: i controlli delle correzioni (docs/audit/affidabilita-letti.md,
/// punti L-.., e due punti del registro dell'autore). Ognuno passa quando il difetto NON c'è più; il commit di ogni punto dice
/// che con il codice di prima cadeva (le sonde del ramo affidabilita-letti mostravano il difetto). Il riquadro di lettura è
/// quello vero (ReadingView.ShowAsync: il corpo dal provider, il letto secondo Gestione messaggi), sul thread dell'interfaccia
/// con il suo Dispatcher (OnUiThread) e senza WebView2 (ReadingView.TestWithoutWeb): nessuna finestra. Server IMAP finto e
/// gestore finto di Graph. Da solo: Missiva.exe --prova-audit risultato.txt affidabilita-letti (con MISSIVA_DATA).
/// </summary>
static partial class ProvaBozze
{
    static void ReadReliabilityTests()
    {
        Report.Add("--- Audit di affidabilità del 25 ottobre 2026, letti e non letti: le correzioni (L-..)");
        var mode = Store.Settings.MarkRead;
        Store.Settings.MarkRead = MarkReadMode.Immediately; // quella di base
        UI.ReadingView.TestWithoutWeb = true;
        try
        {
            OnUiThread(RlReaderImapAsync);
            OnUiThread(RlReaderGraphAsync);
            RlManualMarksSource();
            OnUiThread(RlTrayReopenAsync);
            Wait(RlQueueOverrideAsync);
            Wait(RlEchoAsync);
            Wait(RlServerReadAsync);
            Wait(RlGraphOrderAsync);
            RlSameMessage();
            Wait(RlExitAsync);
            RlMarkAllSource();
            Wait(RlExchangeCountAsync);
            Wait(RlArrivedTwiceAsync);
        }
        finally
        {
            Store.Settings.MarkRead = mode;
            UI.ReadingView.TestWithoutWeb = false;
            ImapProvider.TestPlain = false;
            // Le connessioni chiuse apposta dal server finto (FailOn, Hold) non sono difetti della suite.
            lock (ServerErrors) ServerErrors.Clear();
        }
    }

    // ---------------------------------------------------------------- L-01

    /// <summary>
    /// L-01 con un account IMAP (il codice di Gmail è lo stesso: RunAsync, EachFolderAsync), dal riquadro vero: il letto
    /// automatico di un messaggio aperto quando la scrittura non riesce. Con un NO del server il messaggio torna da leggere in
    /// Missiva (com'è sul server) e ReadUndone lo dice a chi tiene righe e numeri; con la connessione caduta due volte o muta
    /// fino al tempo scaduto (come dopo la sospensione del PC) il segno va in coda e la ripetizione della coda lo porta sul
    /// server. Prima, in tutti e tre i casi, il messaggio restava letto in Missiva e da leggere sul server, e nulla in coda.
    /// </summary>
    static async Task RlReaderImapAsync()
    {
        var views = Enumerable.Range(0, 3).Select(_ => new UI.ReadingView()).ToList();
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var a = fake.Add("INBOX", SpeedMail("Fattura di ottobre", "La fattura"));
        var b = fake.Add("INBOX", SpeedMail("Verbale della riunione", "Il verbale"));
        var c = fake.Add("INBOX", SpeedMail("Preventivo cucina", "Il preventivo"));
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        using var provider = new ImapProvider(account);
        ActionQueue.Forget(account.Id);
        var undone = new List<string>();
        foreach (var view in views) view.ReadUndone += (_, m) => undone.Add(m.Id);
        try
        {
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var page = await provider.GetMessagesAsync(inbox, null, null);
            MessageSummary Row(string subject) => page.Messages.First(m => m.Subject == subject);
            int Stores() => fake.Commands.Count(x => x.Contains("STORE", StringComparison.OrdinalIgnoreCase));
            QueuedAction? Queued(MessageSummary m) => RlQueue(account.Id).FirstOrDefault(q => q.MessageId == m.Id && q.FolderId == m.FolderId);

            // 1. Il server risponde NO al segno: rifiutato, il messaggio torna da leggere (com'è sul server), nulla in coda.
            var sa = Row("Fattura di ottobre");
            fake.RefuseOnce = "UID STORE";
            int before = Stores();
            await views[0].ShowAsync(provider, sa, wholeConversation: false);
            await Until(() => Stores() > before && undone.Contains(sa.Id), TimeSpan.FromSeconds(5));
            await Task.Delay(500);
            Check(Stores() - before == 1 && !sa.IsRead && !a.Has("\\Seen") && undone.SequenceEqual([sa.Id]) && Queued(sa) == null,
                  $"L-01, IMAP, NO del server al letto del riquadro: il messaggio torna da leggere in Missiva (letto {sa.IsRead}, sul server \\Seen {a.Has("\\Seen")}), " +
                  $"il riquadro lo dice (ReadUndone: {string.Join(",", undone)}) perché righe e numero tornino com'erano, e non va in coda ({RlQueue(account.Id).Count} azioni)");

            // 2. La connessione cade al segno, e di nuovo alla ripresa: il segno va in coda, e la coda lo porta sul server.
            var sb = Row("Verbale della riunione");
            fake.FailOn = "UID STORE";
            before = Stores();
            await views[1].ShowAsync(provider, sb, wholeConversation: false);
            await Until(() => Stores() >= before + 2 && Queued(sb) != null, TimeSpan.FromSeconds(10));
            fake.FailOn = null;
            var queuedDrop = Queued(sb);
            var replayDrop = await ActionQueue.ReplayAsync(provider, () => provider.GetFoldersAsync());
            Check(queuedDrop is { Kind: QueuedKind.Read } && sb.IsRead && b.Has("\\Seen") && replayDrop.Done == 1 && Queued(sb) == null && !undone.Contains(sb.Id),
                  $"L-01, IMAP, connessione caduta due volte al letto del riquadro: il segno va in coda ({queuedDrop?.Kind.ToString() ?? "niente"}), il messaggio resta letto in Missiva " +
                  $"e la ripetizione della coda lo segna sul server ({replayDrop.Done} fatte, \\Seen {b.Has("\\Seen")})");

            // 3. La connessione muta: il server non risponde al segno (una connessione morta dopo la sospensione) finché MailKit
            // non smette di aspettare (60 s per IMAP, 120 per Gmail; qui 1,5 s). Che il segno vada in coda o che lo riprenda
            // subito RunAsync con una connessione nuova, alla fine deve stare sul server.
            var main = typeof(ImapProvider).GetField("main", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(provider)!;
            var client = (MailKit.Net.Imap.ImapClient)main.GetType().GetProperty("Client")!.GetValue(main)!;
            client.Timeout = 1500;
            int mute = 0;
            fake.Hold = async command =>
            {
                if (command == "UID STORE" && Interlocked.Exchange(ref mute, 1) == 0)
                {
                    await Task.Delay(3000);
                    throw new IOException("connessione morta (prova): il comando non è mai arrivato");
                }
            };
            var sc = Row("Preventivo cucina");
            bool queuedMute;
            try
            {
                before = Stores();
                await views[2].ShowAsync(provider, sc, wholeConversation: false);
                await Until(() => Queued(sc) != null || c.Has("\\Seen"), TimeSpan.FromSeconds(10));
                queuedMute = Queued(sc) != null;
            }
            finally
            {
                fake.Hold = null;
                client.Timeout = MailLimits.ImapSilenceMs;
            }
            var replayMute = await ActionQueue.ReplayAsync(provider, () => provider.GetFoldersAsync());
            Check(sc.IsRead && c.Has("\\Seen") && Queued(sc) == null && !undone.Contains(sc.Id),
                  $"L-01, IMAP, connessione muta fino al tempo scaduto al letto del riquadro: il segno non si perde ({(queuedMute ? "in coda, poi la coda" : "ripreso subito")}; " +
                  $"{replayMute.Done} fatte dalla coda), il messaggio è letto in Missiva e sul server (\\Seen {c.Has("\\Seen")})");
        }
        finally
        {
            fake.RefuseOnce = null;
            fake.FailOn = null;
            ActionQueue.Forget(account.Id);
        }
    }

    /// <summary>Le azioni in coda dell'account, lette dalla coda (sotto il suo lucchetto).</summary>
    static List<QueuedAction> RlQueue(string accountId)
    {
        var items = (List<QueuedAction>)typeof(ActionQueue).GetProperty("Items", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var gate = typeof(ActionQueue).GetField("gate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        lock (gate) return items.Where(q => q.AccountId == accountId).ToList();
    }

    /// <summary>Un gestore HTTP finto che risponde in modo asincrono (per le attese e i 429 con Retry-After).</summary>
    sealed class RlHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => answer(request);
    }

    static HttpResponseMessage RlGraphError(HttpStatusCode status, string code, int? retryAfter = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent($"{{\"error\":{{\"code\":\"{code}\",\"message\":\"prova\"}}}}", Encoding.UTF8, "application/json") };
        if (retryAfter is { } seconds) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    static HttpResponseMessage RlJson(JsonNode json) => new(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };

    /// <summary>
    /// L-01 con un account Microsoft, dal riquadro vero: il PATCH del letto che riceve 503 (Graph ripete i 503 solo per le
    /// letture), tre 429 di fila o un 500 va in coda, e la coda lo porta sul server quando Outlook torna a rispondere; un 400
    /// (un rifiuto vero) rimette il messaggio da leggere. Prima tutti e quattro restavano letti in Missiva e da leggere sul server.
    /// </summary>
    static async Task RlReaderGraphAsync()
    {
        var views = Enumerable.Range(0, 4).Select(_ => new UI.ReadingView()).ToList();
        var answers = new Dictionary<string, HttpStatusCode> { ["m503"] = HttpStatusCode.ServiceUnavailable, ["m429"] = HttpStatusCode.TooManyRequests, ["m500"] = HttpStatusCode.InternalServerError, ["m400"] = HttpStatusCode.BadRequest };
        var read = new Dictionary<string, bool>();
        var patches = new Dictionary<string, int>();
        bool healthy = false;
        GraphProvider.UseTestHandler(new RlHttp(async request =>
        {
            await Task.Yield();
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            var id = path[(path.LastIndexOf('/') + 1)..];
            if (!answers.ContainsKey(id)) return RlGraphError(HttpStatusCode.NotFound, "ErrorItemNotFound");
            if (request.Method == HttpMethod.Get)
                return RlJson(new JsonObject { ["id"] = id, ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = "<p>prova</p>" }, ["hasAttachments"] = false });
            if (request.Method != HttpMethod.Patch) return RlGraphError(HttpStatusCode.BadRequest, "ErrorInvalidRequest");
            lock (patches) patches[id] = patches.GetValueOrDefault(id) + 1;
            if (!healthy || id == "m400")
                return answers[id] == HttpStatusCode.TooManyRequests ? RlGraphError(answers[id], "ApplicationThrottled", retryAfter: 0)
                     : RlGraphError(answers[id], answers[id] == HttpStatusCode.BadRequest ? "ErrorInvalidRequest" : "ServiceUnavailable");
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            lock (read) read[id] = (bool)body["isRead"]!;
            return RlJson(new JsonObject { ["id"] = id });
        }));
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@outlook.example" };
        var graph = new GraphProvider(account);
        ActionQueue.Forget(account.Id);
        var undone = new List<string>();
        foreach (var view in views) view.ReadUndone += (_, m) => undone.Add(m.Id);
        MessageSummary Mail(string id) => new() { Id = id, FolderId = "inbox-vero", Subject = "Prova " + id, From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" } };
        var shown = answers.Keys.Select(Mail).ToList();
        try
        {
            for (int k = 0; k < shown.Count; k++) await views[k].ShowAsync(graph, shown[k], wholeConversation: false);
            int Patches(string id) { lock (patches) return patches.GetValueOrDefault(id); }
            await Until(() => Patches("m503") >= 1 && Patches("m429") >= 3 && Patches("m500") >= 1 && Patches("m400") >= 1 && undone.Contains("m400")
                              && RlQueue(account.Id).Count >= 3, TimeSpan.FromSeconds(10));
            var queued = RlQueue(account.Id).Select(q => q.MessageId).Order().ToList();
            healthy = true;
            var replay = await ActionQueue.ReplayAsync(graph, () => graph.GetFoldersAsync());
            bool Read(string id) { lock (read) return read.GetValueOrDefault(id); }
            Check(queued.SequenceEqual(["m429", "m500", "m503"]) && replay.Done == 3 && Read("m503") && Read("m429") && Read("m500") && RlQueue(account.Id).Count == 0
                  && shown.Where(m => m.Id != "m400").All(m => m.IsRead) && !shown.First(m => m.Id == "m400").IsRead && undone.SequenceEqual(["m400"]),
                  $"L-01, Microsoft, letto del riquadro: con 503, tre 429 di fila e 500 il segno va in coda ({string.Join(", ", queued)}) e la coda lo porta sul server quando Outlook risponde " +
                  $"({replay.Done} fatte); con 400 il messaggio torna da leggere (ReadUndone: {string.Join(",", undone)})");
        }
        finally
        {
            ActionQueue.Forget(account.Id);
        }
    }

    /// <summary>
    /// L-01 per i segni a mano (Ctrl+Q, Ctrl+U, la busta, la barra della finestra del messaggio): MainWindow.SetReadAsync,
    /// che nessuna prova può chiamare senza la finestra, passa dalla stessa strada del riquadro (ReadMarks.WriteAsync, provata
    /// qui sopra) e, con un rifiuto del server, rimette le righe e il numero com'erano, dopo l'avviso di sempre. Lettura del
    /// sorgente.
    /// </summary>
    static void RlManualMarksSource()
    {
        if (SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: i segni a mano non sono stati letti)");
            return;
        }
        var main = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.xaml.cs"));
        var offline = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.Offline.cs"));
        var reader = File.ReadAllText(Path.Combine(root, "UI", "ReadingView.xaml.cs"));
        string Part(string text, string from, string to) => text.IndexOf(from, StringComparison.Ordinal) is >= 0 and var start && text.IndexOf(to, start, StringComparison.Ordinal) is > 0 and var end ? text[start..end] : "";
        var setRead = Part(main, "async Task SetReadAsync(List<MessageItem> items, bool read)", "async void OnToggleFlag(");
        var mark = Part(offline, "async Task<IReadOnlyList<MessageSummary>> MarkOnServerAsync(", "/// <summary>Le azioni in coda per l'account");
        var pane = Part(reader, "async void SetRead(IMailProvider p, Entry entry, Stopwatch shown)", "// ---------------------------------------------------------------- disegno");
        Check(setRead.Contains("await MarkOnServerAsync(group.Key, mine, read)") && !setRead.Contains("RunOrQueueAsync(") && mark.Contains("ReadMarks.WriteAsync(account.Provider, messages, read)")
              && mark.Contains("Dialog.Show(this, T(\"Operazione non riuscita\")") && pane.Contains("ReadMarks.WriteAsync(p, [entry.Summary], true)") && pane.Contains("ReadUndone?.Invoke(p, entry.Summary)")
              && main.Contains("Reader.ReadUndone += OnReaderReadUndone;") && main.Contains("reader.ReadUndone += OnReaderReadUndone;"),
              "L-01, segni a mano e letto del riquadro: MainWindow.SetReadAsync passa dalla strada del riquadro (ReadMarks.WriteAsync: in coda anche con gli errori che passano), " +
              "e con un rifiuto del server rimette righe e numero dopo l'avviso di sempre; la finestra principale e quella del messaggio ascoltano ReadUndone del loro riquadro");
    }

    static string RlPart(string text, string from, string to) =>
        text.IndexOf(from, StringComparison.Ordinal) is >= 0 and var start && text.IndexOf(to, start, StringComparison.Ordinal) is > 0 and var end ? text[start..end] : "";

    // ---------------------------------------------------------------- L-03

    /// <summary>
    /// L-03: un segno in coda (fatto senza rete) non vince su quello fatto dopo in linea. Prima la coda non sapeva del segno
    /// nuovo e al ritorno della rete (o al riavvio, con coda.dat) rimetteva lo stato vecchio sul server. Ora il segno riuscito
    /// in linea toglie dalla coda quelli di prima dello stesso messaggio (ActionQueue.Supersede), anche dal file, e una
    /// ripetizione già partita non ripete quelli tolti intanto. Al contrario, un segno in coda fatto dopo una scrittura ancora
    /// in viaggio resta in coda: vale il più nuovo.
    /// </summary>
    static async Task RlQueueOverrideAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var a = fake.Add("INBOX", SpeedMail("Contratto da firmare", "Il contratto"), "\\Seen");
        var b = fake.Add("INBOX", SpeedMail("Promemoria scadenza", "La scadenza"), "\\Seen");
        var c = fake.Add("INBOX", SpeedMail("Offerta del fornitore", "L'offerta"), "\\Seen");
        var d = fake.Add("INBOX", SpeedMail("Richiesta del cliente", "La richiesta"));
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        using var provider = new ImapProvider(account);
        ActionQueue.Forget(account.Id);
        try
        {
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var page = await provider.GetMessagesAsync(inbox, null, null);
            MessageSummary Row(string subject) => page.Messages.First(m => m.Subject == subject);

            // 1. Senza rete "Segna come da leggere" sul contratto (in coda); poi, in linea, lo si apre e diventa letto.
            var sa = Row("Contratto da firmare");
            ActionQueue.Add(account, [sa], QueuedKind.Unread);
            var online = await ReadMarks.WriteAsync(provider, [sa], true);
            int left = ActionQueue.Count(account.Id);
            var replay = await ActionQueue.ReplayAsync(provider, () => provider.GetFoldersAsync());
            Check(online.Outcome == ReadMarks.Outcome.Done && left == 0 && replay.Done == 0 && a.Has("\\Seen"),
                  $"L-03: il \"da leggere\" fatto senza rete e ancora in coda esce quando lo stesso messaggio è segnato letto in linea (in coda dopo il segno: {left}); " +
                  $"la coda ripetuta non lo rimette da leggere sul server (\\Seen {a.Has("\\Seen")}, {replay.Done} fatte)");

            // 2. Lo stesso dopo un riavvio: la coda riletta da coda.dat, il segno in linea la toglie anche dal file.
            var sb = Row("Promemoria scadenza");
            ActionQueue.Add(account, [sb], QueuedKind.Unread);
            ActionQueue.Reset();
            ReadMarks.Reset();
            bool reloaded = ActionQueue.Count(account.Id) == 1;
            await ReadMarks.WriteAsync(provider, [sb], true);
            ActionQueue.Reset();
            Check(reloaded && ActionQueue.Count(account.Id) == 0 && b.Has("\\Seen"),
                  $"L-03, dopo un riavvio: il segno di prima riletto dal file (coda.dat: {reloaded}) esce, anche dal file, quando lo stesso messaggio è segnato in linea (in coda riletta: {ActionQueue.Count(account.Id)})");

            // 3. La coda sta ripartendo (presi i suoi segni, aspetta di sapere quali messaggi ci sono ancora) quando lo stesso
            // messaggio si segna in linea: la ripetizione non ripete il segno tolto intanto.
            var sc = Row("Offerta del fornitore");
            ActionQueue.Add(account, [sc], QueuedKind.Unread);
            var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fake.Hold = async command =>
            {
                if (command is not ("EXAMINE" or "UID FETCH")) return;
                asked.TrySetResult();
                await release.Task;
            };
            var replaying = ActionQueue.ReplayAsync(provider, () => provider.GetFoldersAsync());
            await Task.WhenAny(asked.Task, Task.Delay(5000));
            await ReadMarks.WriteAsync(provider, [sc], true);
            fake.Hold = null;
            release.TrySetResult();
            var during = await replaying;
            Check(asked.Task.IsCompleted && during.Done == 0 && c.Has("\\Seen") && ActionQueue.Count(account.Id) == 0,
                  $"L-03, coda che riparte mentre si segna in linea: il segno di prima tolto intanto non parte (\\Seen {c.Has("\\Seen")}, {during.Done} fatte, in coda {ActionQueue.Count(account.Id)})");

            // 4. Al contrario: un letto in viaggio (il server lo trattiene), intanto la rete cade e si segna da leggere (in coda).
            // Quando il letto finisce, il "da leggere" fatto dopo resta in coda: è il più nuovo.
            var sd = Row("Richiesta del cliente");
            var stored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fake.Hold = async command =>
            {
                if (command != "UID STORE") return;
                stored.TrySetResult();
                await go.Task;
            };
            var first = ReadMarks.WriteAsync(provider, [sd], true);
            await Task.WhenAny(stored.Task, Task.Delay(5000));
            Network.Note(account, new System.Net.Sockets.SocketException(11001)); // la rete cade: l'account è non in linea
            var later = await ReadMarks.WriteAsync(provider, [sd], false);
            fake.Hold = null;
            go.TrySetResult();
            var done = await first;
            var queued = RlQueue(account.Id);
            Network.Note(account, null);
            Check(later.Outcome == ReadMarks.Outcome.Queued && done.Outcome == ReadMarks.Outcome.Done && queued is [{ Kind: QueuedKind.Unread }],
                  $"L-03, il segno più nuovo vince anche al contrario: il \"da leggere\" messo in coda mentre il letto di prima era in viaggio resta in coda quando quello finisce " +
                  $"({string.Join(", ", queued.Select(q => q.Kind))})");
        }
        finally
        {
            fake.Hold = null;
            Network.Note(account, null);
            ActionQueue.Forget(account.Id);
        }
    }

    // ---------------------------------------------------------------- L-04 ed E-14

    /// <summary>
    /// L-04 ed E-14: l'eco delle scritture di Missiva all'IDLE si riconosce per messaggio e per il segno atteso (InboxIdle.OwnEcho
    /// con l'avviso), per il tempo che serve all'eco vero (Gmail lo manda fino a 30-35 s dopo: registro dell'autore, 15-25
    /// ottobre). Prima ogni avviso di segni o di messaggi usciti dell'account, per 4 s dopo una qualunque scrittura, era eco:
    /// un letto fatto sul telefono su un altro messaggio si perdeva per l'elenco, e l'eco vero di Gmail, che arriva dopo,
    /// passava e faceva un controllo in più. Server IMAP finto con l'IDLE vero; gli avvisi li manda la prova (Push), uno per
    /// volta, e la prova li passa a OwnEcho come App.OnInboxEvent, nell'ordine in cui arrivano.
    /// </summary>
    static async Task RlEchoAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var a = fake.Add("INBOX", SpeedMail("Offerta del fornitore", "L'offerta"));
        var b = fake.Add("INBOX", SpeedMail("Richiesta del cliente", "La richiesta"));
        var c = fake.Add("INBOX", SpeedMail("Sollecito di pagamento", "Il sollecito"));
        var d = fake.Add("INBOX", SpeedMail("Notizie della settimana", "Le notizie"));
        var e = fake.Add("INBOX", SpeedMail("Verbale della riunione", "Il verbale"));
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        using var provider = new ImapProvider(account);
        var seen = new List<(InboxNotice Notice, bool Echo, InboxNotice? Extra)>();
        using var stop = new CancellationTokenSource();
        var watching = Task.Run(() => provider.WatchInboxAsync(n =>
        {
            if (n.Kind == InboxEvent.Connected) return;
            bool echo = InboxIdle.OwnEcho(account.Id, n, out var rest);
            lock (seen) seen.Add((n, echo, rest));
        }, stop.Token));
        async Task<(InboxNotice Notice, bool Echo, InboxNotice? Extra)> Push(string line)
        {
            int before;
            lock (seen) before = seen.Count;
            fake.Push(line);
            await Until(() => { lock (seen) return seen.Count > before; }, TimeSpan.FromSeconds(3));
            lock (seen) return seen.Count > before ? seen[before] : (new InboxNotice(InboxEvent.Connected), false, null);
        }
        try
        {
            await Until(() => fake.Idling > 0, TimeSpan.FromSeconds(10));
            var folders = await provider.GetFoldersAsync();
            var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
            var page = await provider.GetMessagesAsync(inbox, null, null);
            MessageSummary Row(FakeImap.Message m) => page.Messages.First(s => s.Id == m.Uid.ToString());

            // 1. Missiva segna letta l'offerta; un secondo dopo il telefono legge la richiesta del cliente; poi arriva l'eco.
            await provider.SetReadAsync([Row(a)], true);
            var phone = await Push($"* 2 FETCH (UID {b.Uid} FLAGS (\\Seen))");
            var echoA = await Push($"* 1 FETCH (UID {a.Uid} FLAGS (\\Seen))");
            // 2. Un cambio diverso da quello atteso sullo stesso messaggio (Missiva l'ha segnato letto, il server dice da leggere).
            await provider.SetReadAsync([Row(c)], true);
            var other = await Push($"* 3 FETCH (UID {c.Uid} FLAGS ())");
            var echoC = await Push($"* 3 FETCH (UID {c.Uid} FLAGS (\\Seen))");
            // 3. L'eco che arriva tardi, come quello di Gmail: oltre i 4 s di prima.
            await provider.SetReadAsync([Row(e)], true);
            await Task.Delay(4500);
            var late = await Push($"* 5 FETCH (UID {e.Uid} FLAGS (\\Seen))");
            Check(phone is { Echo: false, Notice: { Kind: InboxEvent.Changed, Read: true } } && phone.Notice.Id == b.Uid.ToString() && echoA.Echo && other is { Echo: false } && echoC.Echo && late.Echo
                  && InboxIdle.OwnWindow >= TimeSpan.FromSeconds(35),
                  $"L-04: dopo il letto scritto da Missiva, il letto fatto sul telefono su un altro messaggio passa (eco {phone.Echo}), e anche un cambio diverso da quello atteso sullo stesso " +
                  $"messaggio (eco {other.Echo}); l'eco vero si riconosce (eco {echoA.Echo}, {echoC.Echo}), anche 4,5 s dopo (eco {late.Echo}: l'eco si aspetta per {InboxIdle.OwnWindow.TotalSeconds:0} s)");

            // 4. Nello stesso avviso dell'eco il telefono ha anche contrassegnato il messaggio: l'eco non lo nasconde.
            await provider.SetReadAsync([Row(d)], true);
            var both = await Push($"* 4 FETCH (UID {d.Uid} FLAGS (\\Seen \\Flagged))");
            Check(both is { Echo: true, Extra: { Flagged: true, Read: null } } && both.Extra?.Id == d.Uid.ToString(),
                  $"L-04: l'eco del letto scritto da Missiva che porta anche un contrassegno messo altrove resta eco per il letto, e il contrassegno arriva alla riga " +
                  $"(resto dell'avviso: letto {both.Extra?.Read?.ToString() ?? "-"}, contrassegno {both.Extra?.Flagged?.ToString() ?? "-"})");

            // 5. E-14: Missiva sposta un messaggio fuori dalla Posta in arrivo; poi dal telefono se ne elimina un altro.
            await provider.MoveAsync([Row(d)], folders.First(f => f.Id == fake.UserFolder));
            var moved = await Push("* 4 EXPUNGE");
            var deleted = await Push("* 1 EXPUNGE");
            // 6. Un server che nella FETCH non mette l'UID: si riconosce dal conto, uno per segno scritto.
            await provider.SetFlagAsync([Row(e)], true);
            var noUid = await Push("* 3 FETCH (FLAGS (\\Seen \\Flagged))");
            var noUidPhone = await Push("* 1 FETCH (FLAGS (\\Seen))");
            Check(moved is { Echo: true, Notice.Kind: InboxEvent.Removed } && deleted is { Echo: false, Notice.Kind: InboxEvent.Removed } && noUid.Echo && !noUidPhone.Echo,
                  $"E-14: il messaggio che Missiva ha tolto dalla Posta in arrivo è eco (eco {moved.Echo}), quello eliminato dal telefono subito dopo no (eco {deleted.Echo}); senza UID nella FETCH " +
                  $"l'eco vale per un avviso per segno scritto (eco {noUid.Echo}, poi {noUidPhone.Echo})");
        }
        finally
        {
            stop.Cancel();
            try { await watching; } catch { }
        }
    }

    // ---------------------------------------------------------------- L-05

    /// <summary>
    /// L-05: lo stato di lettura di una lettura del server chiesta prima che il segno di Missiva ci arrivasse (la prima pagina
    /// in viaggio mentre si clicca il messaggio, la sua rilettura dopo un avviso, la query delta di Graph partita un attimo
    /// prima del PATCH) non rimette sulla riga lo stato di prima: per quei messaggi vale il segno di qui (ReadMarks.Apply e
    /// Holding), come per i numeri (UnreadSync). Una lettura chiesta dopo che la scrittura è finita (e ReadMarks.Settle) dice
    /// il vero, anche se è diverso: vince il server. Prima le righe prendevano sempre lo stato del server. Le pagine, la
    /// rilettura, la vista di tutti gli account, la query delta e gli avvisi dell'IDLE ci passano: lettura del sorgente.
    /// </summary>
    static async Task RlServerReadAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var stored = fake.Add("INBOX", SpeedMail("Preventivo arrivato ora", "Il preventivo"));
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        using var provider = new ImapProvider(account);
        ActionQueue.Forget(account.Id);
        try
        {
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var row = (await provider.GetMessagesAsync(inbox, null, null)).Messages.Single();
            MessageSummary FromServer(bool read) { var m = row.WithId(row.Id); m.IsRead = read; return m; }
            // La prima pagina parte, poi il clic segna letto il messaggio (la scrittura trattenuta dal server).
            var before = ReadMarks.Asked();
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fake.Hold = async command =>
            {
                if (command != "UID STORE") return;
                reached.TrySetResult();
                await go.Task;
            };
            row.IsRead = true;
            var writing = ReadMarks.WriteAsync(provider, [row], true);
            await Task.WhenAny(reached.Task, Task.Delay(5000));
            var during = ReadMarks.Asked();
            var pageBefore = new List<MessageSummary> { FromServer(false) };
            var pageDuring = new List<MessageSummary> { FromServer(false) };
            ReadMarks.Apply(account.Id, pageBefore, before);
            ReadMarks.Apply(account.Id, pageDuring, during);
            fake.Hold = null;
            go.TrySetResult();
            await writing;
            // Finita la scrittura, una pagina chiesta prima che il server la avesse non vale ancora; una chiesta dopo Settle sì,
            // anche se dice da leggere (il telefono, intanto).
            var stillOld = new List<MessageSummary> { FromServer(false) };
            ReadMarks.Apply(account.Id, stillOld, during);
            await Task.Delay(ReadMarks.Settle + TimeSpan.FromMilliseconds(200));
            var fresh = new List<MessageSummary> { FromServer(false) };
            ReadMarks.Apply(account.Id, fresh, ReadMarks.Asked());
            bool? delta = ReadMarks.Holding(account.Id, new MessageSummary { Id = row.Id, FolderId = row.FolderId }, during);
            Check(pageBefore[0].IsRead && pageDuring[0].IsRead && stillOld[0].IsRead && !fresh[0].IsRead && delta == true && stored.Has("\\Seen"),
                  $"L-05: una pagina dell'elenco chiesta prima del clic, o mentre il letto era in viaggio, non rimette la riga da leggere (letto {pageBefore[0].IsRead}, {pageDuring[0].IsRead}, " +
                  $"anche a scrittura finita {stillOld[0].IsRead}); una chiesta dopo che il server l'ha avuto dice il vero, anche da leggere (letto {fresh[0].IsRead}); lo stesso per un solo messaggio " +
                  $"(query delta, avviso dell'IDLE: {delta?.ToString() ?? "nessun segno"})");
        }
        finally
        {
            fake.Hold = null;
            ActionQueue.Forget(account.Id);
        }
        if (SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: chi applica lo stato del server non è stato letto)");
            return;
        }
        var main = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.xaml.cs"));
        var notes = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.Notifications.cs"));
        var unified = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.Unified.cs"));
        var more = RlPart(main, "async Task LoadMoreAsync(bool first = false)", "void ShowFirstPage(");
        var refresh = RlPart(main, "async Task RefreshListAsync()", "// ---------------------------------------------------------------- categorie");
        var changes = RlPart(notes, "public void ApplyInboxChanges(", "public void RefreshInboxOf(");
        var flags = RlPart(notes, "public void ApplyIdleFlags(", "public void RemoveRuledRows(");
        bool Ordered(string text, params string[] parts)
        {
            int at = 0;
            foreach (var part in parts) if ((at = text.IndexOf(part, at, StringComparison.Ordinal)) < 0) return false;
            return true;
        }
        Check(Ordered(more, "var asked = ReadMarks.Asked();", "GetMessagesAsync(", "ReadMarks.Apply(account.Id, page.Messages, asked)", "ShowFirstPage(")
              && Ordered(refresh, "var asked = ReadMarks.Asked();", "GetMessagesAsync(", "ReadMarks.Apply(current.Account.Provider.Account.Id, page.Messages, asked)", "MergeFirstPage(")
              && unified.Split("ActionQueue.Apply(").Length - 1 == unified.Split("ReadMarks.Apply(").Length - 1 && unified.Contains("ReadMarks.Asked()")
              && changes.Contains("changes.Asked") && changes.Contains("ActionQueue.Apply(") && changes.Contains("ReadMarks.Apply(") && flags.Contains("ReadMarks.Holding("),
              "L-05: la prima pagina e la sua rilettura (prima di ShowFirstPage e MergeFirstPage), le pagine della vista di tutti gli account, la query delta di Graph (con la coda) e gli " +
              "avvisi dell'IDLE passano dal segno di qui, con l'istante in cui la lettura del server è partita");
    }

    // ---------------------------------------------------------------- L-06

    /// <summary>
    /// L-06: con Microsoft due scritture di lettura sullo stesso messaggio hanno l'ordine in cui sono state chieste. Il letto
    /// del riquadro riceve un 429 (Outlook oltre le quattro richieste insieme) e si ripete dopo l'attesa che Outlook chiede;
    /// intanto Ctrl+U chiede "da leggere". Prima il "da leggere" partiva subito, e il letto ripetuto arrivava dopo e vinceva:
    /// sul server letto, in Missiva da leggere. Ora il secondo aspetta il primo (GraphProvider.SetReadAsync, in fila per
    /// messaggio), e due messaggi diversi non si aspettano.
    /// </summary>
    static async Task RlGraphOrderAsync()
    {
        var read = new Dictionary<string, bool>();
        int attempts = 0;
        var order = new List<string>();
        GraphProvider.UseTestHandler(new RlHttp(async request =>
        {
            await Task.Yield();
            if (request.Method != HttpMethod.Patch) return RlGraphError(HttpStatusCode.NotFound, "ErrorItemNotFound");
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            var id = path[(path.LastIndexOf('/') + 1)..];
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            bool isRead = (bool)body["isRead"]!;
            if (id == "ordine" && isRead && Interlocked.Increment(ref attempts) == 1)
            {
                lock (order) order.Add("letto: 429");
                return RlGraphError(HttpStatusCode.TooManyRequests, "ApplicationThrottled", retryAfter: 1);
            }
            lock (order) order.Add($"{id} {(isRead ? "letto" : "da leggere")}: applicato");
            lock (read) read[id] = isRead;
            return RlJson(new JsonObject { ["id"] = id });
        }));
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@outlook.example" });
        var m = new MessageSummary { Id = "ordine", FolderId = "inbox-vero" };
        var other = new MessageSummary { Id = "altro", FolderId = "inbox-vero" };
        var first = graph.SetReadAsync([m], true);    // il riquadro, appena il corpo si vede
        await Task.Delay(200);
        var second = graph.SetReadAsync([m], false);  // Ctrl+U subito dopo
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await graph.SetReadAsync([other], true);      // un altro messaggio intanto: non aspetta
        long otherMs = clock.ElapsedMilliseconds;
        await Task.WhenAll(first, second);
        string done;
        lock (order) done = string.Join(", ", order);
        bool final;
        lock (read) final = read.GetValueOrDefault("ordine", true);
        Check(!final && done.EndsWith("ordine da leggere: applicato") && otherMs < 800,
              $"L-06, Microsoft: il \"da leggere\" chiesto dopo il letto del riquadro arriva dopo il letto ripetuto per il 429 e vince (sul server: {done}; letto alla fine {final}); " +
              $"un altro messaggio intanto non aspetta ({otherMs} ms)");
    }

    // ---------------------------------------------------------------- L-07

    /// <summary>
    /// L-07: le righe di un messaggio letto o segnato si cercano per messaggio, non per solo identificativo (ReadMarks.Same e
    /// Matches). Con Gmail un messaggio della conversazione aperto nel riquadro viene da "Tutti i messaggi", con un UID di
    /// quella cartella: prima diventava letta la riga della Posta in arrivo con lo stesso numero (un altro messaggio), la riga
    /// vera no, e scendeva il numero di "Tutti i messaggi"; ora la riga vera (lo stesso X-GM-MSGID) e il numero della sua
    /// cartella. Nella ricerca IMAP in tutte le cartelle due risultati con lo stesso UID in due cartelle cambiavano insieme.
    /// La regola qui; che il riquadro e i segni a mano la usino, nel sorgente.
    /// </summary>
    static void RlSameMessage()
    {
        MessageSummary M(string id, string folder, string stable = "") => new() { Id = id, FolderId = folder, StableId = stable };
        const string all = "[Gmail]/Tutti i messaggi";
        var thread = M("7", all, "1001");                       // dalla conversazione: UID di "Tutti i messaggi"
        var otherRow = M("7", "INBOX", "2002");                 // la riga della Posta in arrivo con lo stesso numero: un altro messaggio
        var realRow = M("3", "INBOX", "1001");                  // la riga vera: lo stesso X-GM-MSGID
        var imapA = M("12", "INBOX", "<a@prova.example>");
        var imapB = M("12", "INBOX.Clienti", "<a@prova.example>"); // un'altra cartella, lo stesso UID (e lo stesso Message-Id: una copia)
        var graphA = M("AAMk1", "inbox-vero");
        var graphB = M("AAMk1", "");                            // dalla query delta: senza cartella
        var gmailRows = ReadMarks.Matches(AccountKind.Gmail, [thread]);
        var imapRows = ReadMarks.Matches(AccountKind.Imap, [imapA]);
        bool rule = !ReadMarks.Same(AccountKind.Gmail, thread, otherRow) && ReadMarks.Same(AccountKind.Gmail, thread, realRow)
                    && !ReadMarks.Same(AccountKind.Imap, imapA, imapB) && ReadMarks.Same(AccountKind.Imap, imapA, M("12", "INBOX"))
                    && ReadMarks.Same(AccountKind.Microsoft, graphA, graphB)
                    && !gmailRows(otherRow) && gmailRows(realRow) && imapRows(M("12", "INBOX")) && !imapRows(imapB);
        bool source = true;
        if (SourceRoot() is { } root)
        {
            var main = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.xaml.cs"));
            var read = RlPart(main, "void OnReaderRead(IMailProvider provider, MessageSummary summary)", "/// <summary>Un segno di letto in coda per un errore che passa");
            var manual = RlPart(main, "async Task SetReadAsync(List<MessageItem> items, bool read)", "async void OnToggleFlag(");
            source = read.Contains("ReadMarks.Matches(") && !read.Contains("r.Summary.Id == summary.Id") && read.Split("ReadReaderRows(").Length >= 3
                     && manual.Contains("ReadMarks.Matches(") && !manual.Contains("ids.Contains(r.Summary.Id)") && !manual.Contains("back.Contains(r.Summary.Id)");
        }
        else Report.Add("      (sorgenti non trovati accanto all'eseguibile: le righe del riquadro e dei segni a mano non sono state lette)");
        Check(rule && source,
              "L-07: il messaggio della conversazione di Gmail (da \"Tutti i messaggi\") trova la sua riga della Posta in arrivo per X-GM-MSGID, non quella con lo stesso UID; due risultati IMAP con lo " +
              "stesso UID in due cartelle sono due messaggi; con Microsoft vale l'id. Il letto del riquadro (anche quando il server lo rifiuta) e i segni a mano cercano così le righe, e il numero " +
              "scende nella cartella della riga trovata");
    }

    // ---------------------------------------------------------------- L-08

    /// <summary>
    /// L-08: i segni di lettura ancora in viaggio quando Missiva esce (Esci, "esci" di installa.ps1, l'aggiornamento, la fine
    /// della sessione di Windows; con "quando la selezione cambia" il letto parte proprio chiudendo la finestra) vanno nella
    /// coda delle azioni, che è su disco (ReadMarks.QueueUnfinished, da App.OnExit e SessionEnding), e ripartono al prossimo
    /// avvio. Se la scrittura arriva lo stesso prima che il processo finisca, il segno esce dalla coda (Supersede). Prima
    /// nessuno li aspettava e non erano in coda: con la connessione ferma il segno si perdeva. Il server finto trattiene il
    /// segno; "il riavvio" è una coda riletta dal file e un provider nuovo.
    /// </summary>
    static async Task RlExitAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var a = fake.Add("INBOX", SpeedMail("Ultimo messaggio letto", "Il testo"));
        var b = fake.Add("INBOX", SpeedMail("Letto prima del riavvio", "Il testo"));
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        using var provider = new ImapProvider(account);
        ActionQueue.Forget(account.Id);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var page = (await provider.GetMessagesAsync(inbox, null, null)).Messages;
            var sa = page.First(m => m.Id == a.Uid.ToString());
            var sb = page.First(m => m.Id == b.Uid.ToString());
            fake.Hold = async command =>
            {
                if (command != "UID STORE") return;
                reached.TrySetResult();
                await go.Task;
            };
            // 1. Il letto in viaggio quando Missiva esce: in coda, nel file; poi la scrittura arriva lo stesso, ed esce dalla coda.
            var writing = ReadMarks.WriteAsync(provider, [sa], true);
            await Task.WhenAny(reached.Task, Task.Delay(5000));
            int queuedAtExit = ReadMarks.QueueUnfinished();
            ActionQueue.Reset();
            bool inFile = RlQueue(account.Id) is [{ Kind: QueuedKind.Read }];
            fake.Hold = null;
            go.TrySetResult();
            await writing;
            bool cleared = ActionQueue.Count(account.Id) == 0 && a.Has("\\Seen");

            // 2. Il letto in viaggio, e il processo finisce prima della risposta: al riavvio la coda lo porta sul server.
            var stuck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fake.Hold = async command =>
            {
                if (command != "UID STORE") return;
                stuck.TrySetResult();
                await never.Task;
            };
            var lost = ReadMarks.WriteAsync(provider, [sb], true);
            await Task.WhenAny(stuck.Task, Task.Delay(5000));
            ReadMarks.QueueUnfinished();
            fake.Hold = null;
            ActionQueue.Reset(); // il riavvio
            ReadMarks.Reset();
            using var again = new ImapProvider(account);
            var replay = await ActionQueue.ReplayAsync(again, () => again.GetFoldersAsync());
            bool afterRestart = b.Has("\\Seen"); // prima che la scrittura trattenuta, di "prima del riavvio", arrivi
            never.TrySetResult();
            await Task.WhenAny(lost, Task.Delay(3000)); // la scrittura di prima del "riavvio" finisce: niente resta in sottofondo
            Check(queuedAtExit == 1 && inFile && cleared && replay.Done == 1 && afterRestart && ActionQueue.Count(account.Id) == 0,
                  $"L-08: il letto in viaggio all'uscita va nella coda su disco ({queuedAtExit} messo in coda, nel file {inFile}); se arriva lo stesso esce dalla coda ({cleared}); se il processo " +
                  $"finisce prima, al riavvio la coda lo porta sul server ({replay.Done} fatte, \\Seen {afterRestart})");
        }
        finally
        {
            fake.Hold = null;
            go.TrySetResult();
            ActionQueue.Forget(account.Id);
        }
        if (SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: l'uscita non è stata letta)");
            return;
        }
        var app = File.ReadAllText(Path.Combine(root, "UI", "App.cs"));
        var exit = RlPart(app, "protected override void OnExit(ExitEventArgs e)", "base.OnExit(e);");
        var session = RlPart(app, "SessionEnding += (_, ev) =>", "};");
        Check(exit.Contains("ReadMarks.QueueUnfinished()") && session.Contains("ReadMarks.QueueUnfinished()"),
              "L-08: all'uscita (App.OnExit) e alla fine della sessione di Windows (SessionEnding, dove Windows può chiudere il processo senza OnExit) i segni in viaggio vanno nella coda");
    }

    // ---------------------------------------------------------------- L-09

    /// <summary>
    /// L-09: "Contrassegna tutti come letti" segna lette le righe solo se il server l'ha fatto. Prima FolderActionAsync
    /// mostrava l'errore e lo inghiottiva, e la riga dopo segnava lette tutte le righe comunque: senza rete, o con un errore,
    /// l'elenco diceva letti e il server no, fino alla rilettura. Il comando al provider con i suoi tre server finti lo prova
    /// già scelte3 (scelta 48); qui il sorgente della finestra, che nessuna prova costruisce.
    /// </summary>
    static void RlMarkAllSource()
    {
        if (SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: Contrassegna tutti come letti non è stato letto)");
            return;
        }
        var main = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.xaml.cs"));
        var action = RlPart(main, "async Task MarkAllReadAsync(FolderItem f)", "async Task FolderActionAsync(");
        var folder = RlPart(main, "async Task<bool> TryFolderActionAsync(", "internal static void MigrateFolderKeys(");
        int guard = action.IndexOf("if (!await TryFolderActionAsync(f.Account, \"segna tutti come letti\"", StringComparison.Ordinal);
        int rowsAt = action.IndexOf("row.IsRead = true", StringComparison.Ordinal);
        Check(guard >= 0 && rowsAt > guard && folder.Contains("return done;") && folder.IndexOf("done = true;", StringComparison.Ordinal) > folder.IndexOf("await action();", StringComparison.Ordinal),
              "L-09: \"Contrassegna tutti come letti\" segna lette le righe dell'elenco aperto solo se il server l'ha fatto (FolderActionAsync dice se l'azione è riuscita); con un errore restano com'erano, dopo l'avviso");
    }

    // ---------------------------------------------------------------- il numero di Outlook.com dopo una lettura

    /// <summary>
    /// Registro dell'autore, 22 e 24 ottobre: con Outlook.com, letto un messaggio nel riquadro, il numero dei non letti
    /// risaliva per qualche secondo (17:00:51 "1 -> 0 letto nel riquadro", 17:00:52 "0 -> 1 ricontati dal server", 17:01:05
    /// di nuovo 0 dalla query delta). Il riconteggio dopo la scrittura (App.RecountSoon) aspettava UnreadSync.Settle, un
    /// secondo, e Exchange aggiorna unreadItemCount qualche secondo dopo la risposta al PATCH. Ora dopo il letto di Graph il
    /// conteggio del server non vale per GraphProvider.ReadCountSettle (5 s); per Gmail e IMAP resta il secondo di sempre.
    /// </summary>
    static async Task RlExchangeCountAsync()
    {
        GraphProvider.UseTestHandler(new RlHttp(async request =>
        {
            await Task.Yield();
            return RlJson(new JsonObject { ["id"] = "conteggio" });
        }));
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@outlook.example" });
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        fake.Add("INBOX", SpeedMail("Da leggere", "Il testo"));
        using var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
        var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
        var row = (await imap.GetMessagesAsync(inbox, null, null)).Messages.Single();
        await graph.SetReadAsync([new MessageSummary { Id = "conteggio", FolderId = "inbox-vero" }], true);
        await imap.SetReadAsync([row], true);
        await Task.Delay(2000);
        bool graphWaits = UnreadSync.Ticket(graph.Account.Id) == null, imapReady = UnreadSync.Ticket(imap.Account.Id) != null;
        Check(graphWaits && imapReady && GraphProvider.ReadCountSettle >= TimeSpan.FromSeconds(3),
              $"registro dell'autore: 2 s dopo il letto di Outlook.com il conteggio del server non vale ancora (Exchange aggiorna unreadItemCount dopo la risposta al PATCH: si aspetta " +
              $"{GraphProvider.ReadCountSettle.TotalSeconds:0} s), per IMAP sì come prima (Graph aspetta {graphWaits}, IMAP pronto {imapReady})");
    }

    // ---------------------------------------------------------------- un arrivo contato due volte

    /// <summary>
    /// Registro dell'autore, quattro volte in dieci giorni: un arrivo contato due volte per un istante ("5 -> 11 cartelle
    /// rilette", poi "11 -> 17 (6 arrivati)" e subito "17 -> 11" dal server). Le cartelle rilette (o il riconteggio) avevano
    /// già contato i messaggi arrivati, per esempio durante la sospensione del PC, e il controllo dopo li sommava di nuovo nel
    /// numero stimato che mostra con la notifica (MainWindow.AddIncoming), prima che arrivasse quello del server. Ora la
    /// stima non somma i messaggi arrivati più di mezzo minuto prima che fosse chiesto il numero del server che la Posta in
    /// arrivo ha adesso (FolderItem.CountedAt, da UnreadSync.AskedAt; mezzo minuto perché Gmail mostra un arrivo alle
    /// altre connessioni anche qualche secondo dopo, e gli orologi del PC e del server non sono uguali).
    /// </summary>
    static async Task RlArrivedTwiceAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        for (int k = 0; k < 11; k++) fake.Add("INBOX", SpeedMail("Arrivato di notte " + k, "Il testo"));
        using var provider = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
        var item = new UI.AccountItem(provider);
        // Le cartelle rilette alla ripresa del PC: 11 da leggere, compresi i 6 arrivati durante la notte.
        var ticket = UnreadSync.Ticket(provider.Account.Id);
        bool taken = item.SetServerFolders(await provider.GetFoldersAsync(), ticket);
        var inbox = item.Folders.First(f => f.Folder.Kind == FolderKind.Inbox);
        MessageSummary Arrived(string id, TimeSpan ago) => new() { Id = id, FolderId = "INBOX", Date = DateTime.Now - ago };
        // Il controllo dopo trova i 6 arrivati di notte (uno già letto altrove), e uno arrivato adesso.
        var night = Enumerable.Range(1, 6).Select(k => Arrived("n" + k, TimeSpan.FromHours(7))).ToList();
        night[0].IsRead = true;
        var now = Arrived("ora", TimeSpan.Zero);
        int fromNight = UI.MainWindow.ArrivedUnread(inbox, night), fromNow = UI.MainWindow.ArrivedUnread(inbox, [now]);
        // Senza un numero del server (la copia locale all'avvio) si sommano tutti, come prima.
        var fresh = new UI.AccountItem(provider);
        fresh.SetFolders([new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox, Unread = 2 }]);
        int fromCopy = UI.MainWindow.ArrivedUnread(fresh.Folders.First(), night);
        Check(taken && inbox.Unread == 11 && inbox.CountedAt != null && fromNight == 0 && fromNow == 1 && fromCopy == 5,
              $"registro dell'autore: i messaggi già contati dalle cartelle rilette non si sommano di nuovo nel numero stimato con la notifica (arrivati di notte: +{fromNight}, " +
              $"arrivato adesso: +{fromNow}); senza un numero del server si sommano i non letti, come prima (+{fromCopy})");
    }

    // ---------------------------------------------------------------- L-02

    /// <summary>
    /// L-02: il "da leggere" a mano sul messaggio aperto (Ctrl+U, la busta) e la riapertura dall'area di notifica. Chiusa la
    /// finestra il riquadro si svuota (HideToTray, ReleaseWeb, Clear); riaperta, ShowFromTray rimette nel riquadro la riga
    /// ancora scelta. Prima la rimetteva come un'apertura nuova, e con "Subito" il messaggio tornava letto (anche sul server):
    /// il "da leggere" stava sull'apertura di prima (Entry.ReadByHand). Ora la riapertura è un ritorno (restore): il messaggio
    /// si mostra com'è, letto o no, in ogni modo di Gestione messaggi. Il riquadro vero con le sue due aperture e, in mezzo,
    /// il segno a mano come lo fa MainWindow.SetReadAsync; il passaggio da ShowFromTray letto nel sorgente.
    /// </summary>
    static async Task RlTrayReopenAsync()
    {
        var view = new UI.ReadingView();
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var stored = fake.Add("INBOX", SpeedMail("Da rileggere con calma", "Il documento"));
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        using var provider = new ImapProvider(account);
        // Il riquadro fra quelli aperti, come dopo Loaded (ForgetPendingRead li guarda tutti).
        var open = (System.Collections.IList)typeof(UI.ReadingView).GetField("open", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        open.Add(view);
        var (mode, seconds) = (Store.Settings.MarkRead, Store.Settings.MarkReadSeconds);
        try
        {
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var row = (await provider.GetMessagesAsync(inbox, null, null)).Messages.Single();
            int Stores() => fake.Commands.Count(x => x.Contains("STORE", StringComparison.OrdinalIgnoreCase));
            // 1. Il clic sulla riga: il messaggio si apre e diventa letto.
            await view.ShowAsync(provider, row, wholeConversation: false);
            await Until(() => stored.Has("\\Seen"), TimeSpan.FromSeconds(5));
            bool readFirst = stored.Has("\\Seen") && row.IsRead;
            // 2. Ctrl+U sulla riga aperta, come MainWindow.SetReadAsync: le letture rimandate dimenticate, poi il segno.
            UI.ReadingView.ForgetPendingRead([row]);
            row.IsRead = false;
            await ReadMarks.WriteAsync(provider, [row], false);
            bool byHand = !stored.Has("\\Seen");
            // 3. Finestra chiusa nell'area di notifica (il riquadro si svuota) e riaperta: la riga è ancora scelta e torna nel
            // riquadro, in ognuno dei modi di Gestione messaggi; con "quando la selezione cambia" anche dopo un altro cambio.
            var kept = new List<string>();
            foreach (var how in new[] { MarkReadMode.Immediately, MarkReadMode.AfterDelay, MarkReadMode.OnSelectionChange })
            {
                Store.Settings.MarkRead = how;
                Store.Settings.MarkReadSeconds = 1;
                int before = Stores();
                view.Clear();
                await view.ShowAsync(provider, row, wholeConversation: false, restore: true);
                await Task.Delay(how == MarkReadMode.AfterDelay ? 1600 : 400);
                if (how == MarkReadMode.OnSelectionChange) view.Clear(); // la selezione cambia
                await Task.Delay(300);
                if (Stores() == before && !row.IsRead && !stored.Has("\\Seen")) kept.Add(how.ToString());
            }
            Report.Add($"      L-02: letto al clic {readFirst}; da leggere a mano {byHand}; riaperto il riquadro con la stessa riga, resta da leggere in: {string.Join(", ", kept)}");
            Check(readFirst && byHand && kept.Count == 3,
                  "L-02: il messaggio segnato da leggere a mano resta da leggere quando il riquadro lo rimette com'era (riapertura dall'area di notifica), con \"Subito\", \"dopo N secondi\" e " +
                  "\"quando la selezione cambia\": nessun segno parte, in Missiva e sul server");
        }
        finally
        {
            (Store.Settings.MarkRead, Store.Settings.MarkReadSeconds) = (mode, seconds);
            open.Remove(view);
        }
        if (SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: ShowFromTray non è stato letto)");
            return;
        }
        var main = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.xaml.cs"));
        var tray = RlPart(main, "public void ShowFromTray()", "#if DEBUG");
        var show = RlPart(main, "async Task ShowInReaderAsync(MessageItem item", "async Task PrefetchNeighborsAsync(");
        Check(tray.Contains("ShowInReaderAsync(item, restore: true)") && show.Contains("restore: restore") && !main.Replace("ShowInReaderAsync(item, restore: true)", "").Contains("restore: true"),
              "L-02: ShowFromTray rimette il messaggio di prima nel riquadro come un ritorno (ShowInReaderAsync con restore, passato a ReadingView.ShowAsync), e nessun'altra apertura della finestra principale lo fa");
    }
}
#endif
