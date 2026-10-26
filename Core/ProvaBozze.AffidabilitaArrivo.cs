#if DEBUG
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using MailKit.Net.Imap;

namespace Missiva;

/// <summary>
/// Le correzioni dell'audit di affidabilità, parte "arrivo" (25 ottobre 2026, docs/audit/affidabilita-arrivo.md, A-01..A-10,
/// e A-11 del 26 ottobre per Graph): le mail che arrivano tardi. Server IMAP finti con le connessioni mute (leggono e non
/// rispondono, senza chiudersi: una connessione morta dopo la ripresa del PC o un cambio di rete), i provider veri di Gmail
/// e degli account IMAP, un gestore HTTP finto che non risponde per Graph e Google, i pezzi del risveglio
/// (Core/Arrival.cs) e, per quello che sta nella finestra, i sorgenti. Nessuna rete. I tempi di MailKit e
/// dell'IDLE sono accorciati dove serve (secondi invece di minuti). Con "Missiva.exe --prova-audit risultato.txt
/// affidabilita-arrivo" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void ArrivalFixTests() => Wait(async () =>
    {
        Report.Add("--- Audit di affidabilità, arrivo dei messaggi nuovi: le correzioni (A-01..A-11)");
        ImapProvider.TestPlain = true;
        var (renew, retry, done) = (InboxIdle.Renew, InboxIdle.FirstRetry, InboxIdle.DoneLimit);
        try
        {
            ArfIdleTimes();
            foreach (var gmail in new[] { false, true }) await ArfIdleWatchdogAsync(gmail);
            foreach (var gmail in new[] { false, true }) await ArfIdleWatchingAsync(gmail);
            await ArfRenewCostAsync();
            foreach (var gmail in new[] { false, true }) await ArfTimeoutReadAsync(gmail);
            foreach (var gmail in new[] { false, true }) await ArfTimeoutWriteAsync(gmail);
            await ArfAllTogetherAsync();
            ArfAllTogetherSources();
            foreach (var gmail in new[] { false, true }) await ArfDropAsync(gmail);
            await ArfWakeRetryAsync();
            ArfSleepClock();
            ArfNetworkPrint();
            ArfWakeSources();
            foreach (var gmail in new[] { false, true }) await ArfReconnectedAsync(gmail);
            ArfReconnectedSources();
            ArfPollTimes();
            foreach (var gmail in new[] { false, true }) await ArfPollCostAsync(gmail);
            foreach (var gmail in new[] { false, true }) await ArfUidNewAsync(gmail);
            foreach (var gmail in new[] { false, true }) await ArfOwnArrivalsAsync(gmail);
            ArfUidSources();
            foreach (var gmail in new[] { false, true }) await ArfAnnouncedAsync(gmail);
            ArfAnnouncedSources();
            await ArfDeltaRestartAsync();
            await ArfHttpResetAsync();
            ArfLogSources();
            await ArfRulesBackgroundAsync();
        }
        finally
        {
            (InboxIdle.Renew, InboxIdle.FirstRetry, InboxIdle.DoneLimit) = (renew, retry, done);
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
        }
    });

    /// <summary>Il client IMAP (ImapClient) di una connessione di un provider (main, background, idle...), per accorciarne il Timeout.</summary>
    static ImapClient ArfClient(object provider, string field)
    {
        var connection = provider.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(provider)!;
        var type = connection.GetType();
        return (ImapClient)(type.GetField("Client")?.GetValue(connection) ?? type.GetProperty("Client")!.GetValue(connection)!);
    }

    static string Arf(bool gmail) => gmail ? "Gmail" : "IMAP";

    /// <summary>
    /// La chiusura del provider di una prova, dopo aver buttato le connessioni (DropConnections): la chiusura normale manda il
    /// LOGOUT a ogni connessione e lo aspetta fino a cinque secondi su un thread del pool, e con le connessioni mute delle
    /// prove i thread bloccati facevano aspettare per decine di secondi i lavori delle prove dopo.
    /// </summary>
    sealed class ArfClosing(IMailProvider provider) : IDisposable
    {
        public void Dispose()
        {
            provider.DropConnections();
            (provider as IDisposable)?.Dispose();
        }
    }

    // ---------------------------------------------------------------- A-01: l'IDLE che muore senza che nessuno se ne accorga

    /// <summary>
    /// A-01: il rinnovo dell'IDLE e il cane da guardia sul DONE. Il rinnovo è l'unico momento in cui il client scrive sulla
    /// connessione dell'IDLE (né MailKit né Missiva mandano altro, e il socket non ha il keepalive di TCP): una connessione
    /// morta in silenzio si scopre lì, e solo se la risposta al DONE ha un tempo limite. Prima il rinnovo era ogni 9 minuti
    /// e il DONE senza risposta aspettava per sempre.
    /// </summary>
    static void ArfIdleTimes()
    {
        Check(InboxIdle.Renew <= TimeSpan.FromMinutes(2) && InboxIdle.DoneLimit >= TimeSpan.FromSeconds(10) && InboxIdle.DoneLimit <= TimeSpan.FromSeconds(30),
              $"A-01, i tempi dell'IDLE: rinnovo ogni {InboxIdle.Renew.TotalSeconds:0} s (al più 2 minuti), risposta al DONE attesa al più {InboxIdle.DoneLimit.TotalSeconds:0} s " +
              $"(fra 10 e 30): una connessione dell'IDLE morta senza che Windows lo dica si scopre entro {(InboxIdle.Renew + InboxIdle.DoneLimit).TotalSeconds:0} s");
    }

    /// <summary>
    /// A-01: la connessione dell'IDLE muore in silenzio (muta: legge e non risponde, senza chiudersi). Al rinnovo (qui 2 s)
    /// il DONE non ha risposta: entro DoneLimit (qui 1,5 s) la sessione si chiude e se ne apre un'altra, e la sessione nuova
    /// annuncia un arrivo. Prima l'IDLE restava fermo sul DONE per sempre (sonda arrivo-done: dopo 150 s aspettava ancora).
    /// </summary>
    static async Task ArfIdleWatchdogAsync(bool gmail)
    {
        InboxIdle.Renew = TimeSpan.FromSeconds(2);
        InboxIdle.FirstRetry = TimeSpan.FromMilliseconds(200);
        InboxIdle.DoneLimit = TimeSpan.FromSeconds(1.5);
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        fake.Add("INBOX", SpeedMail("Vecchio", "già qui", daysAgo: 2), "\\Seen");
        var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
        using var closing = new ArfClosing(provider);
        var events = new ConcurrentQueue<InboxNotice>();
        using var stop = new CancellationTokenSource();
        var watching = provider.WatchInboxAsync(events.Enqueue, stop.Token);
        bool up = await Until(() => fake.Idling == 1, TimeSpan.FromSeconds(5));
        var first = fake.IdleConnections();
        fake.MuteIdlers();
        var clock = Stopwatch.StartNew();
        bool back = await Until(() => fake.IdleConnections() is [var c] && !first.Contains(c), TimeSpan.FromSeconds(10));
        long backAt = clock.ElapsedMilliseconds;
        bool doneUnanswered;
        lock (fake.IdleLog) doneUnanswered = fake.IdleLog.Any(l => l.StartsWith("DONE (connessione muta"));
        fake.Add("INBOX", SpeedMail("Preventivo urgente", "arrivato con la sessione nuova"));
        fake.Push($"* {fake.Folders["INBOX"].Count} EXISTS");
        bool announced = await Until(() => events.Any(e => e.Kind == InboxEvent.Arrived), TimeSpan.FromSeconds(3));
        stop.Cancel();
        bool ended = await Task.WhenAny(watching, Task.Delay(TimeSpan.FromSeconds(5))) == watching;
        Check(up && back && doneUnanswered && announced && ended,
              $"A-01 ({Arf(gmail)}): connessione dell'IDLE morta in silenzio, DONE del rinnovo senza risposta ({doneUnanswered}): " +
              $"{(back ? $"sessione nuova su un'altra connessione dopo {backAt} ms" : "ancora ferma sul DONE dopo 10 s")} (rinnovo 2 s, limite del DONE 1,5 s), " +
              $"e la sessione nuova annuncia un arrivo ({announced}); la fine del ciclo lo ferma ({ended})");
    }

    /// <summary>
    /// A-01: Watching dice il vero. Il server non risponde più a nessuno (anche le connessioni nuove sono mute): dopo il
    /// rinnovo e il limite del DONE l'IDLE non è più "in sessione", e il controllo di riserva non lo crede vivo; quando il
    /// server torna a rispondere l'IDLE è di nuovo in sessione su una connessione nuova. Prima restava "in sessione" per
    /// sempre, fermo sul DONE.
    /// </summary>
    static async Task ArfIdleWatchingAsync(bool gmail)
    {
        InboxIdle.Renew = TimeSpan.FromSeconds(2);
        InboxIdle.FirstRetry = TimeSpan.FromMilliseconds(200);
        InboxIdle.DoneLimit = TimeSpan.FromSeconds(1.5);
        const string who = "mario@studio-prova.example";
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        var provider = FakeProvider(fake, gmail, who);
        using var closing = new ArfClosing(provider);
        ArfClient(provider, "idle").Timeout = 2000;
        using var stop = new CancellationTokenSource();
        var watching = provider.WatchInboxAsync(_ => { }, stop.Token);
        bool up = await Until(() => fake.Idling == 1 && InboxIdle.Watching(who), TimeSpan.FromSeconds(5));
        var first = fake.IdleConnections();
        fake.MuteOpen();
        fake.MuteNew = true;
        var clock = Stopwatch.StartNew();
        bool down = await Until(() => !InboxIdle.Watching(who), TimeSpan.FromSeconds(12));
        long downAt = clock.ElapsedMilliseconds;
        fake.MuteNew = false;
        bool again = await Until(() => fake.IdleConnections() is [var c] && !first.Contains(c) && InboxIdle.Watching(who), TimeSpan.FromSeconds(12));
        stop.Cancel();
        await Task.WhenAny(watching, Task.Delay(TimeSpan.FromSeconds(5)));
        Check(up && down && again,
              $"A-01 ({Arf(gmail)}): server muto per tutti, anche per le connessioni nuove: Watching {(down ? $"falso dopo {downAt} ms" : "ancora vero dopo 12 s")} " +
              $"(rinnovo 2 s, limite del DONE 1,5 s, tempo scaduto 2 s); con il server di nuovo vivo l'IDLE è di nuovo in sessione su una connessione nuova ({again})");
    }

    /// <summary>
    /// A-01, misura: quanto costa un rinnovo dell'IDLE (DONE, la risposta, IDLE, "+ idling") in tempo di processore, sul
    /// server finto nello stesso processo (quindi per eccesso: c'è anche il server). Con il rinnovo ogni 2 minuti invece che
    /// ogni 9 i rinnovi passano da 7 a 30 all'ora per account.
    /// </summary>
    static async Task ArfRenewCostAsync()
    {
        var process = Process.GetCurrentProcess();
        double Cpu() { process.Refresh(); return process.TotalProcessorTime.TotalMilliseconds; }
        // Prima il processo da fermo (con l'IDLE in sessione e nessun rinnovo), poi lo stesso tempo con un rinnovo ogni 100 ms.
        async Task<(int Renewals, double Cpu)> RunAsync(TimeSpan renew)
        {
            InboxIdle.Renew = renew;
            using var fake = new FakeImap(new Flavor(Gmail: false));
            var provider = FakeProvider(fake, false, "mario@studio-prova.example");
            using var closing = new ArfClosing(provider);
            using var stop = new CancellationTokenSource();
            var watching = provider.WatchInboxAsync(_ => { }, stop.Token);
            await Until(() => fake.Idling == 1, TimeSpan.FromSeconds(5));
            await Task.Delay(500);
            int Renewals() { lock (fake.IdleLog) return fake.IdleLog.Count(l => l == "IDLE"); }
            int before = Renewals();
            double start = Cpu();
            await Task.Delay(4000);
            var result = (Renewals() - before, Cpu() - start);
            stop.Cancel();
            await Task.WhenAny(watching, Task.Delay(TimeSpan.FromSeconds(5)));
            return result;
        }
        var quiet = await RunAsync(TimeSpan.FromMinutes(5));
        var busy = await RunAsync(TimeSpan.FromMilliseconds(100));
        double perRenewal = busy.Renewals == 0 ? 0 : Math.Max(0, busy.Cpu - quiet.Cpu) / busy.Renewals;
        Report.Add($"      misura A-01: {busy.Renewals} rinnovi dell'IDLE in 4 s, {busy.Cpu:0} ms di processore contro {quiet.Cpu:0} senza rinnovi: {perRenewal:0.00} ms l'uno " +
                   $"(client e server finto insieme): ogni 2 minuti sono {perRenewal * 30:0.0} ms all'ora per account, ogni 9 erano {perRenewal * 60 / 9:0.0}");
        Check(busy.Renewals > 5 && quiet.Renewals == 0, $"A-01, misura del costo del rinnovo: il rinnovo gira ({busy.Renewals} in 4 s, {quiet.Renewals} con il rinnovo lungo)");
    }

    // ---------------------------------------------------------------- A-02: le connessioni morte e gli account in fila

    /// <summary>
    /// A-02: il controllo dei nuovi messaggi (GetNewAsync, sulla connessione in sottofondo) con quella connessione morta in
    /// silenzio, come dopo la ripresa del PC: il tempo scaduto di MailKit (qui 2 s, nell'uso 60 s con IMAP e 120 con Gmail)
    /// si riprova una volta su una connessione nuova, e il controllo trova il messaggio. Prima usciva con TimeoutException e
    /// quel controllo era perso fino al giro dopo (sonda A-02: un solo tentativo, non riprovato).
    /// </summary>
    static async Task ArfTimeoutReadAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail)) { AnnounceOnNoop = true };
        fake.Add("INBOX", SpeedMail("Vecchio", "già qui", daysAgo: 2), "\\Seen");
        var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
        using var closing = new ArfClosing(provider);
        ArfClient(provider, "background").Timeout = 2000;
        var first = await provider.GetNewAsync(DateTime.UtcNow.AddMinutes(-1));
        int connections = fake.ConnectionCount;
        fake.MuteOpen();
        fake.Add("INBOX", SpeedMail("Preventivo urgente", "arrivato mentre la connessione era morta"));
        var clock = Stopwatch.StartNew();
        List<MessageSummary>? found = null;
        Exception? error = null;
        try { found = await provider.GetNewAsync(DateTime.UtcNow.AddMinutes(-2)); }
        catch (Exception ex) { error = ex; }
        long took = clock.ElapsedMilliseconds;
        Check(first.Count == 0 && error == null && found is [{ Subject: "Preventivo urgente" }] && took < 4000 && fake.ConnectionCount == connections + 1,
              $"A-02 ({Arf(gmail)}): controllo dei nuovi messaggi con la connessione in sottofondo morta in silenzio (tempo scaduto 2 s): " +
              (error == null ? $"{found?.Count} messaggi trovati in {took} ms su una connessione nuova ({fake.ConnectionCount - connections})" : $"{error.GetType().Name} dopo {took} ms, non riprovato"));
    }

    /// <summary>
    /// A-02, quello che resta com'era: una scrittura (qui Già letto, UID STORE) su una connessione morta in silenzio non si
    /// riprova dopo il tempo scaduto, perché il server può averla ricevuta e fatta prima che il tempo scadesse.
    /// </summary>
    static async Task ArfTimeoutWriteAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        fake.Add("INBOX", SpeedMail("Da leggere", "ancora da leggere"));
        var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
        using var closing = new ArfClosing(provider);
        ArfClient(provider, "main").Timeout = 2000;
        var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
        var page = await provider.GetMessagesAsync(inbox, null, null);
        fake.MuteOpen();
        int Stores() { lock (fake.Commands) return fake.Commands.Count(c => c.StartsWith("UID STORE", StringComparison.Ordinal)); }
        int before = Stores();
        Exception? error = null;
        try { await provider.SetReadAsync([page.Messages[0]], true); }
        catch (Exception ex) { error = ex; }
        int stores = Stores() - before;
        bool timedOut = error is TimeoutException || error?.InnerException is TimeoutException;
        Check(timedOut && stores == 1,
              $"A-02 ({Arf(gmail)}): una scrittura (Già letto) su una connessione morta in silenzio non si ripete dopo il tempo scaduto: {error?.GetType().Name ?? "nessun errore"}, UID STORE arrivati al server {stores}");
    }

    /// <summary>
    /// A-02: gli account si controllano insieme (Arrival.RunAllAsync, quello che App.CheckNewAsync usa): uno veloce finisce
    /// subito anche se un altro è lento, il giro dura quanto il più lento, e uno che non finisce entro il limite si lascia
    /// andare e si dice. Prima si controllavano in fila: il veloce aspettava il lento.
    /// </summary>
    static async Task ArfAllTogetherAsync()
    {
        var clock = Stopwatch.StartNew();
        var finished = new ConcurrentDictionary<string, long>();
        var stuck = new ConcurrentBag<string>();
        int total = await Arrival.RunAllAsync(["lento", "veloce", "fermo", "già in corso"], async name =>
        {
            if (name == "lento") await Task.Delay(1500);
            if (name == "fermo") await Task.Delay(8000);
            if (name == "veloce") await Task.Yield();
            finished[name] = clock.ElapsedMilliseconds;
            return name switch { "veloce" => 2, "lento" => 1, "già in corso" => -1, _ => 5 };
        }, TimeSpan.FromSeconds(3), stuck.Add);
        long took = clock.ElapsedMilliseconds;
        Check(finished.GetValueOrDefault("veloce", 99999) < 500 && finished.GetValueOrDefault("lento", 0) is >= 1400 and < 2500 && total == 3 && stuck.SequenceEqual(["fermo"]) && took is >= 2900 and < 4500,
              $"A-02: gli account si controllano insieme: il veloce finisce a {finished.GetValueOrDefault("veloce", -1)} ms, il lento a {finished.GetValueOrDefault("lento", -1)} ms, " +
              $"quello fermo si lascia andare al limite di 3 s ({string.Join(", ", stuck)}), il giro dura {took} ms, {total} nuovi (quello già in corso non conta)");
    }

    /// <summary>
    /// A-02 nei sorgenti: App.CheckNewAsync controlla tutti gli account con Arrival.RunAllAsync, non uno dopo l'altro, e il
    /// giro del timer non esce più perché quello di prima non è finito (dopo la ripresa del PC un controllo partito prima
    /// della sospensione teneva fermo il giro).
    /// </summary>
    static void ArfAllTogetherSources()
    {
        if (Ch1Code("UI", "App.cs") is not { } app)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: App.CheckNewAsync non è stato letto)");
            return;
        }
        string check = Ch3Part(app, "async Task CheckNewAsync(", "\n    }");
        bool together = check.Contains("await Arrival.RunAllAsync(due,") && !check.Contains("foreach (var p in due)");
        bool noSkip = check.Length > 0 && !check.Contains("if (checking) return;");
        Check(together && noSkip,
              $"A-02 nei sorgenti: App.CheckNewAsync controlla gli account insieme con Arrival.RunAllAsync ({together}) e non salta il giro se quello di prima non è finito ({noSkip})");
    }

    // ---------------------------------------------------------------- A-03: l'IDLE ricollegato

    /// <summary>
    /// A-03: quando l'IDLE si ricollega dopo una caduta (qui il server chiude la connessione mentre un messaggio arriva e non
    /// glielo può dire), l'avviso Connected lo dice (Reconnected), e chi lo riceve controlla l'account: la sessione nuova conta
    /// i messaggi da lì e quello arrivato intanto non lo annuncia nessuno. Il primo collegamento e i rinnovi non lo dicono
    /// (niente controlli in più all'avvio e ogni due minuti). Prima il Connected era sempre lo stesso.
    /// </summary>
    static async Task ArfReconnectedAsync(bool gmail)
    {
        InboxIdle.Renew = TimeSpan.FromMinutes(5);
        InboxIdle.FirstRetry = TimeSpan.FromMilliseconds(200);
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        // Un indirizzo che nessun'altra prova usa: "già stato in sessione" vale per indirizzo, per tutto il processo.
        var provider = FakeProvider(fake, gmail, $"mario.a03.{(gmail ? "gmail" : "imap")}@studio-prova.example");
        using var closing = new ArfClosing(provider);
        var events = new ConcurrentQueue<InboxNotice>();
        using var stop = new CancellationTokenSource();
        var watching = provider.WatchInboxAsync(events.Enqueue, stop.Token);
        bool up = await Until(() => fake.Idling == 1 && events.Any(e => e.Kind == InboxEvent.Connected), TimeSpan.FromSeconds(5));
        bool firstQuiet = events.Where(e => e.Kind == InboxEvent.Connected).All(e => !e.Reconnected);
        fake.MuteIdlers();
        fake.Add("INBOX", SpeedMail("Preventivo urgente", "arrivato con l'IDLE giù"));
        fake.Push($"* {fake.Folders["INBOX"].Count} EXISTS");
        fake.DropIdlers();
        bool told = await Until(() => events.Any(e => e is { Kind: InboxEvent.Connected, Reconnected: true }), TimeSpan.FromSeconds(5));
        bool noArrival = !events.Any(e => e.Kind == InboxEvent.Arrived);
        // Un rinnovo (qui ogni secondo) dice Connected, ma non Reconnected.
        int reconnections = events.Count(e => e is { Kind: InboxEvent.Connected, Reconnected: true });
        InboxIdle.Renew = TimeSpan.FromSeconds(1);
        stop.Cancel();
        await ArfWithin(watching, TimeSpan.FromSeconds(5));
        using var stop2 = new CancellationTokenSource();
        var renewing = provider.WatchInboxAsync(events.Enqueue, stop2.Token);
        int connectedBefore = events.Count(e => e.Kind == InboxEvent.Connected);
        bool renewed = await Until(() => events.Count(e => e.Kind == InboxEvent.Connected) >= connectedBefore + 3, TimeSpan.FromSeconds(6));
        int reconnectionsAfter = events.Count(e => e is { Kind: InboxEvent.Connected, Reconnected: true });
        stop2.Cancel();
        await ArfWithin(renewing, TimeSpan.FromSeconds(5));
        Check(up && firstQuiet && told && noArrival && renewed && reconnectionsAfter <= reconnections + 1,
              $"A-03 ({Arf(gmail)}): il primo collegamento dell'IDLE non dice \"ricollegato\" ({firstQuiet}); il server chiude la connessione mentre arriva un messaggio: " +
              $"l'IDLE si ricollega e lo dice ({told}), il messaggio arrivato intanto non ha avvisi suoi ({noArrival}) e lo trova il controllo che parte da lì; " +
              $"i rinnovi non dicono \"ricollegato\" ({renewed}, {reconnectionsAfter - reconnections} in più, al più quello del ciclo nuovo)");
    }

    /// <summary>A-03 nei sorgenti: App.OnInboxEvent, a un Connected con Reconnected, controlla l'account ("IDLE ricollegato"), tranne subito dopo un risveglio, che li controlla già tutti.</summary>
    static void ArfReconnectedSources()
    {
        if (Ch1Code("UI", "App.cs") is not { } app)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: App.OnInboxEvent non è stato letto)");
            return;
        }
        string connected = Ch3Part(app, "if (e.Kind == InboxEvent.Connected)", "return;\n        }");
        bool checks = Ch3InOrder(connected, "AccountReached(provider, \"IDLE collegato\")", "if (e.Reconnected", "lastWake", "CheckAccountAsync(provider, \"IDLE ricollegato\")");
        Check(checks, $"A-03 nei sorgenti: a un IDLE ricollegato App.OnInboxEvent controlla l'account, tranne subito dopo un risveglio ({checks})");
    }

    // ---------------------------------------------------------------- il controllo di riserva ogni minuto (punto 5 della richiesta)

    /// <summary>
    /// Il controllo di riserva per Gmail e IMAP ogni minuto invece che ogni due (scelta dell'autore del 25 ottobre 2026): nel
    /// suo registro 12 messaggi in 10 giorni avvisati da Gmail stesso fino a 52 s dopo che erano già nella ricerca, con
    /// l'IDLE vivo, e li trovava il controllo di riserva. Graph resta ogni due minuti (ha la query delta ogni 30 s). Prima il
    /// timer era di due minuti per tutti.
    /// </summary>
    static void ArfPollTimes()
    {
        var minute = TimeSpan.FromMinutes(1);
        bool imap = UI.App.PollDue(graph: false, pushed: true, minute) && UI.App.PollDue(graph: false, pushed: false, minute);
        bool graph = !UI.App.PollDue(graph: true, pushed: true, minute) && !UI.App.PollDue(graph: true, pushed: false, minute)
                     && UI.App.PollDue(graph: true, pushed: true, 2 * minute) && UI.App.PollDue(graph: true, pushed: false, 2 * minute);
        bool soon = !UI.App.PollDue(graph: false, pushed: true, TimeSpan.FromSeconds(30));
        Check(UI.App.PollEvery == minute && imap && graph && soon,
              $"punto 5, il controllo di riserva: il timer ogni {UI.App.PollEvery.TotalSeconds:0} s; Gmail e IMAP a ogni giro, anche con l'IDLE in sessione ({imap}); Graph ogni due giri, " +
              $"con o senza la query delta ({graph}); un account controllato 30 s prima non si ricontrolla ({soon})");
    }

    /// <summary>
    /// Punto 5, misura: quanto costa un controllo di riserva senza novità (GetNewAsync e il conteggio dei non letti, come in
    /// App.CheckAccountAsync senza regole) in tempo di processore (client e server finto nello stesso processo, quindi per
    /// eccesso) e in comandi IMAP.
    /// </summary>
    static async Task ArfPollCostAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail)) { AnnounceOnNoop = true };
        // Una Posta in arrivo con qualche messaggio da leggere degli ultimi giorni, nessuno arrivato dopo il controllo di prima.
        for (int i = 0; i < 30; i++) fake.Add("INBOX", SpeedMail($"Messaggio {i}", "già qui", daysAgo: 1 + i % 3), i % 4 == 0 ? "" : "\\Seen");
        var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
        using var closing = new ArfClosing(provider);
        var since = DateTime.UtcNow.AddMinutes(-1);
        // Come App.CheckAccountAsync: i nuovi per UID dopo il mark del controllo di prima (A-06), poi il conteggio.
        string? mark = null;
        async Task CheckAsync()
        {
            mark = (await provider.GetNewAsync(mark, since)).Mark;
            await provider.GetInboxUnreadAsync();
        }
        for (int i = 0; i < 5; i++) await CheckAsync();
        // Come in un controllo vero, il conteggio subito dopo la ricerca: il suo NOOP non si ripete (FreshInboxAsync, counting).
        int commands;
        lock (fake.Commands) commands = fake.Commands.Count;
        for (int i = 0; i < 5; i++) await CheckAsync();
        List<string> sent;
        lock (fake.Commands) sent = fake.Commands.Skip(commands).ToList();
        int perCheck = sent.Count / 5;
        var kinds = string.Join(", ", sent.Take(perCheck).Select(c => c.Split(' ') is [var a, var b, ..] && a == "UID" ? a + " " + b : c.Split(' ')[0]));
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        const int checks = 100;
        for (int i = 0; i < checks; i++) await CheckAsync();
        process.Refresh();
        double perCheckCpu = (process.TotalProcessorTime - cpu).TotalMilliseconds / checks;
        Report.Add($"      misura punto 5 ({Arf(gmail)}): un controllo di riserva senza novità, {perCheck} comandi ({kinds}) e {perCheckCpu:0.00} ms di processore " +
                   $"(client e server finto insieme); ogni minuto invece che ogni due sono 30 controlli in più all'ora per account: {30 * perCheck} comandi e {30 * perCheckCpu:0} ms all'ora");
        Check(perCheck >= 1, $"punto 5, misura del costo del controllo di riserva ({Arf(gmail)}): {perCheck} comandi per controllo");
    }

    // ---------------------------------------------------------------- A-06: i nuovi per UID

    /// <summary>
    /// A-06: i messaggi nuovi si riconoscono per UID (dopo quello visto dal controllo di prima), non per data di arrivo e
    /// orologio del PC. Due da leggere arrivati dopo il controllo di prima, uno con la data di arrivo di 150 s fa (più vecchia
    /// della finestra di un minuto: un messaggio reso visibile tardi, tolto dalla posta indesiderata, l'orologio del PC
    /// avanti): tutti e due. Non quello arrivato già letto, nemmeno se poi torna da leggere; non uno di tre giorni fa messo
    /// nella Posta in arrivo adesso (le vecchie spostate lì non sono "nuove"); nessuno due volte. Prima GetNewAsync ne dava
    /// uno (sonda A-06, prova a finestra F3: nessuna notifica).
    /// </summary>
    static async Task ArfUidNewAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail)) { AnnounceOnNoop = true };
        fake.Add("INBOX", SpeedMail("Vecchio", "già qui", daysAgo: 2), "\\Seen");
        var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
        using var closing = new ArfClosing(provider);
        // Il controllo di prima è cominciato un minuto fa: lastCheck è quell'inizio meno un minuto (App.CheckAccountAsync).
        var since = DateTime.UtcNow.AddMinutes(-2);
        var first = await provider.GetNewAsync(null, since);
        var late = SpeedMail("Fattura di ottobre", "arrivata con la data di arrivo di 150 secondi fa");
        late.Date = DateTimeOffset.Now.AddSeconds(-150);
        fake.Add("INBOX", late);
        fake.Add("INBOX", SpeedMail("Preventivo", "arrivato adesso"));
        var seen = fake.Add("INBOX", SpeedMail("Letto sul telefono", "arrivato e letto altrove prima del controllo"), "\\Seen");
        fake.Add("INBOX", SpeedMail("Vecchio spostato", "di tre giorni fa, messo ora nella Posta in arrivo", daysAgo: 3));
        var second = await provider.GetNewAsync(first.Mark, since);
        var third = await provider.GetNewAsync(second.Mark, since);
        seen.Set("\\Seen", false);
        var fourth = await provider.GetNewAsync(third.Mark, since);
        var subjects = second.Messages.Select(m => m.Subject).Order().ToList();
        Check(first.Messages.Count == 0 && first.Mark != null && subjects.SequenceEqual(["Fattura di ottobre", "Preventivo"]) && second.Mark != first.Mark
              && third.Messages.Count == 0 && fourth.Messages.Count == 0,
              $"A-06 ({Arf(gmail)}): dopo il controllo di prima (mark {first.Mark}) due da leggere arrivati, uno con la data di arrivo di 150 s fa: nuovi {string.Join(", ", subjects)} " +
              $"(mark {second.Mark}); al controllo dopo {third.Messages.Count}; quello arrivato già letto e tornato da leggere {fourth.Messages.Count}; il messaggio di tre giorni fa messo ora nella " +
              "Posta in arrivo non è fra i nuovi");
    }

    /// <summary>
    /// A-06: i messaggi che Missiva stessa mette nella Posta in arrivo (Sposta lì, Annulla di un Elimina o di un Archivia,
    /// il ritorno di Posponi) hanno un UID nuovo, ma non sono posta nuova: Missiva li ricorda (InboxIdle.OwnArrivals) e il
    /// controllo non li annuncia. Prima non serviva: la data di arrivo, che lo spostamento non cambia, era vecchia.
    /// </summary>
    static async Task ArfOwnArrivalsAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail)) { AnnounceOnNoop = true };
        fake.Add(fake.UserFolder, SpeedMail("Da rimettere", "spostato nella Posta in arrivo da Missiva"));
        fake.Add("INBOX", SpeedMail("Da eliminare", "eliminato e poi Annulla"));
        var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
        using var closing = new ArfClosing(provider);
        var folders = await provider.GetFoldersAsync();
        var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
        var other = folders.First(f => f.Id == fake.UserFolder);
        var trash = folders.First(f => f.Kind == FolderKind.Deleted);
        var toInbox = (await provider.GetMessagesAsync(other, null, null)).Messages.First();
        var toTrash = (await provider.GetMessagesAsync(inbox, null, null)).Messages.First();
        var id = provider.Account.Id;
        bool before = InboxIdle.IsOwnArrival(id, toInbox.StableId) || InboxIdle.IsOwnArrival(id, toTrash.StableId);
        await provider.MoveAsync([toInbox], inbox);
        bool moved = InboxIdle.IsOwnArrival(id, toInbox.StableId);
        var gone = await provider.MoveAsync([toTrash], trash);
        bool deleted = InboxIdle.IsOwnArrival(id, toTrash.StableId);
        await provider.UndoMoveAsync(gone, trash);
        bool undone = InboxIdle.IsOwnArrival(id, toTrash.StableId);
        bool woken = !InboxIdle.IsOwnArrival(id, "<posposto@studio-prova.example>");
        InboxIdle.OwnArrivals(id, ["<posposto@studio-prova.example>"]);
        woken &= InboxIdle.IsOwnArrival(id, "<posposto@studio-prova.example>") && !InboxIdle.IsOwnArrival("un altro account", "<posposto@studio-prova.example>") && !InboxIdle.IsOwnArrival(id, "");
        Check(!before && moved && !deleted && undone && woken && toInbox.StableId.Length > 0,
              $"A-06 ({Arf(gmail)}): i messaggi che Missiva mette nella Posta in arrivo non sono posta nuova: Sposta nella Posta in arrivo ({moved}), Annulla di un Elimina ({undone}), " +
              $"non lo spostamento nel Cestino ({!deleted}), per account e solo con una chiave ({woken})");
    }

    /// <summary>
    /// A-06 nei sorgenti: App.CheckAccountAsync chiede i nuovi con il mark dell'account, non annuncia quelli che Missiva stessa
    /// ha messo nella Posta in arrivo, e il mark nuovo vale solo dopo l'annuncio (un controllo interrotto a metà non li perde).
    /// I ritorni di Posponi li ricorda il provider (WakeAsync).
    /// </summary>
    static void ArfUidSources()
    {
        if (Ch1Code("UI", "App.cs") is not { } app || Ch1Code("Core", "ImapProvider.cs") is not { } imap || Ch1Code("Core", "GmailProvider.cs") is not { } gmail)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: App.CheckAccountAsync non è stato letto)");
            return;
        }
        string check = Ch3Part(app, "async Task<int> CheckAccountAsync(", "\n    }");
        bool marks = Ch3InOrder(check, "p.GetNewAsync(newMarks.GetValueOrDefault(id), since)", "InboxIdle.IsOwnArrival(id, m.StableId)", "Notify(p, announce);", "newMarks[id] = Arrival.LaterMark(");
        bool woken = Ch3Part(imap, "public Task<string?> WakeAsync(", "\n    }").Contains("InboxIdle.OwnArrivals(Account.Id, [key])")
                     && Ch3Part(gmail, "public Task<string?> WakeAsync(", "\n    }").Contains("InboxIdle.OwnArrivals(Account.Id, [key])");
        // Il primo mark lo fissa un controllo poco dopo l'avvio, non il primo giro del timer (prova a finestra F3 nel primo minuto).
        bool first = Ch3Part(app, "public void SetProviders(", "\n    }").Contains("_ = FirstCheckLaterAsync();")
                     && Ch3Part(app, "async Task FirstCheckLaterAsync()", "\n    }").Contains("CheckNewAsync(reason: \"primo controllo dopo l'avvio\")");
        Check(marks && woken && first, $"A-06 nei sorgenti: i nuovi per mark, senza quelli messi da Missiva, mark nuovo dopo l'annuncio ({marks}); i ritorni di Posponi ricordati ({woken}); " +
              $"il primo controllo 10 s dopo l'avvio ({first})");
    }

    // ---------------------------------------------------------------- A-05: quanti ne ha annunciati l'IDLE

    /// <summary>
    /// A-05: l'avviso dell'IDLE dice quanti messaggi nuovi il server ha annunciato (qui due con un EXISTS solo, poi uno), e il
    /// controllo si ripete finché non li ha trovati tutti (Arrival.KeepLooking): trovato il primo, il secondo, visibile qualche
    /// secondo dopo, si cerca ancora. Prima l'avviso non diceva quanti, e si ripeteva solo se il controllo ne trovava zero.
    /// </summary>
    static async Task ArfAnnouncedAsync(bool gmail)
    {
        InboxIdle.Renew = TimeSpan.FromMinutes(5);
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        fake.Add("INBOX", SpeedMail("Vecchio", "già qui", daysAgo: 2), "\\Seen");
        var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
        using var closing = new ArfClosing(provider);
        var events = new ConcurrentQueue<InboxNotice>();
        using var stop = new CancellationTokenSource();
        var watching = provider.WatchInboxAsync(events.Enqueue, stop.Token);
        await Until(() => fake.Idling == 1, TimeSpan.FromSeconds(5));
        fake.Add("INBOX", SpeedMail("Primo", "insieme al secondo"));
        fake.Add("INBOX", SpeedMail("Secondo", "insieme al primo"));
        fake.Push($"* {fake.Folders["INBOX"].Count} EXISTS");
        bool two = await Until(() => events.Any(e => e is { Kind: InboxEvent.Arrived, New: 2 }), TimeSpan.FromSeconds(3));
        fake.Add("INBOX", SpeedMail("Terzo", "da solo"));
        fake.Push($"* {fake.Folders["INBOX"].Count} EXISTS");
        bool one = await Until(() => events.Any(e => e is { Kind: InboxEvent.Arrived, New: 1 }), TimeSpan.FromSeconds(3));
        stop.Cancel();
        await ArfWithin(watching, TimeSpan.FromSeconds(5));
        bool rule = Arrival.KeepLooking(1, 2) && !Arrival.KeepLooking(2, 2) && Arrival.KeepLooking(0, 0) && !Arrival.KeepLooking(1, 0) && Arrival.KeepLooking(-1, 1)
                    && Arrival.ArrivalRetries.Length >= 3 && Arrival.ArrivalRetries[^1] >= TimeSpan.FromSeconds(30);
        Check(two && one && rule,
              $"A-05 ({Arf(gmail)}): l'avviso dell'IDLE dice quanti messaggi sono arrivati (due con un EXISTS solo: {two}; poi uno: {one}); il controllo cerca ancora finché non li ha " +
              $"trovati tutti, dopo {string.Join(", ", Arrival.ArrivalRetries.Select(w => w.TotalSeconds))} s ({rule})");
    }

    /// <summary>A-05 nei sorgenti: App.OnInboxEvent somma i messaggi annunciati e ripete il controllo finché non li ha trovati (Arrival.KeepLooking), non solo quando ne trova zero.</summary>
    static void ArfAnnouncedSources()
    {
        if (Ch1Code("UI", "App.cs") is not { } app)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: App.OnInboxEvent non è stato letto)");
            return;
        }
        string idle = Ch3Part(app, "async void OnInboxEvent(", "\n    }");
        bool counts = Ch3InOrder(idle, "pendingArrived[id] = pendingArrived.GetValueOrDefault(id) + Math.Max(1, e.New)", "foreach (var wait in Arrival.ArrivalRetries)",
                                 "if (!Arrival.KeepLooking(found, expected)) break;") && !idle.Contains("if (found != 0) break;");
        Check(counts, $"A-05 nei sorgenti: l'IDLE dice quanti, e il controllo si ripete finché non li ha trovati ({counts})");
    }

    // ---------------------------------------------------------------- A-08: la query delta rifatta

    /// <summary>
    /// A-08: un deltaLink che Graph non accetta più (qui 410) fa ripartire la query delta da capo, e la lettura dopo è di
    /// nuovo il punto di partenza: per due giri quello che arriva non lo dice nessuno. Ora le due letture lo dicono
    /// (InboxChanges.Restarted) e App controlla l'account; poi la query delta va come prima. Prima tutte e due davano null, e il
    /// messaggio arrivato intanto aspettava il controllo di riserva (fino a due minuti).
    /// </summary>
    static async Task ArfDeltaRestartAsync()
    {
        int call = 0;
        static System.Text.Json.Nodes.JsonObject M(string id, bool read) => new() { ["id"] = id, ["isRead"] = read, ["receivedDateTime"] = "2026-10-25T08:00:00Z" };
        static System.Text.Json.Nodes.JsonObject Page(string token, params System.Text.Json.Nodes.JsonObject[] items) =>
            new() { ["value"] = new System.Text.Json.Nodes.JsonArray(items), ["@odata.deltaLink"] = $"https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken={token}" };
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var json = (call++) switch
            {
                0 => Page("A", M("m1", true)),
                1 => null, // il deltaLink A non vale più
                2 => Page("B", M("m1", true), M("m2", false)),
                _ => Page("C", M("m3", false)),
            };
            return json == null
                ? new HttpResponseMessage(System.Net.HttpStatusCode.Gone) { Content = new StringContent("{\"error\":{\"code\":\"SyncStateNotFound\",\"message\":\"scaduto\"}}", System.Text.Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        }));
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@outlook.example" });
        var start = await graph.GetInboxChangesAsync();
        var gone = await graph.GetInboxChangesAsync();
        var again = await graph.GetInboxChangesAsync();
        var normal = await graph.GetInboxChangesAsync();
        // E App la controlla (nei sorgenti, se ci sono).
        bool checks = Ch1Code("UI", "App.cs") is not { } app
                      || Ch3Part(app, "async Task DeltasOnceAsync(", "\n    }").Contains("if (changes is { Restarted: true }) await CheckAccountAsync(p, \"query delta rifatta\");");
        Check(start == null && gone is { Restarted: true } && again is { Restarted: true, Any: false } && normal is { Restarted: false } n && n.Seen.Contains(("m3", false)) && checks,
              $"A-08: la query delta che riparte da capo (410) lo dice alla lettura che la rifà ({gone?.Restarted}) e a quella dopo, il punto di partenza nuovo ({again?.Restarted}), " +
              $"e App controlla l'account ({checks}); poi come prima ({normal?.Restarted}, m3 nuovo); il primo punto di partenza all'avvio no ({start == null})");
    }

    // ---------------------------------------------------------------- A-11: le connessioni HTTP dopo la ripresa

    /// <summary>Un gestore HTTP finto che riceve anche l'annullamento: la richiesta che aspetta per sempre è la connessione morta.</summary>
    sealed class ArfHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => answer(request, cancel);
    }

    /// <summary>
    /// A-11 (dopo l'installazione del 26 ottobre 2026, registro dell'autore): alla ripresa del PC le connessioni IMAP si
    /// rifacevano, quelle HTTP di Graph no. La connessione in HTTP/2 tenuta per riusarla era morta, e per un minuto ogni
    /// richiesta all'account Microsoft restava ferma fino al tempo scaduto (60 s), poi 10054: "Non riesco ad aprire il
    /// messaggio". Qui un gestore finto che non risponde mai, come la connessione morta: GraphProvider.ResetConnections
    /// ferma la richiesta in attesa e la lettura riparte sul client nuovo (anche uno scaricamento); una scrittura no, perché
    /// non si sa se è arrivata, e cade come una connessione caduta, che la coda riprova; una lettura caduta con la
    /// connessione (10054) si ripete una volta, senza rete no. Lo stesso per le API di Google. Poi il gestore vero: i ping
    /// che dicono morta una connessione HTTP/2 in 10 s, il gestore nuovo dopo ResetConnections, e App.WakeAsync che lo fa.
    /// </summary>
    static async Task ArfHttpResetAsync()
    {
        static HttpResponseMessage Json(string text) => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(text, System.Text.Encoding.UTF8, "application/json") };
        static HttpRequestException Lost(int code) => new("connessione caduta", new IOException("connessione caduta", new System.Net.Sockets.SocketException(code)));
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "prova.a11@outlook.example" };
        try
        {
            // Una lettura ferma sulla connessione morta.
            int calls = 0;
            bool firstStopped = false;
            GraphProvider.UseTestHandler(new ArfHttp(async (request, cancel) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    try { await Task.Delay(Timeout.Infinite, cancel); }
                    catch (OperationCanceledException) { firstStopped = true; throw; }
                }
                return Json("{\"unreadItemCount\":7}");
            }));
            var clock = Stopwatch.StartNew();
            var read = new GraphProvider(account).GetInboxUnreadAsync();
            await Task.Delay(500);
            bool waiting = !read.IsCompleted;
            GraphProvider.ResetConnections();
            int unread = await read.WaitAsync(TimeSpan.FromSeconds(10));
            long readMs = clock.ElapsedMilliseconds;
            Check(waiting && firstStopped && unread == 7 && calls == 2 && readMs < 3000,
                  $"A-11: una lettura di Graph ferma su una connessione morta (dopo 500 ms ancora in attesa: {waiting}) si ferma con le connessioni rifatte " +
                  $"({firstStopped}) e riparte su quella nuova ({calls} richieste, risposta {unread}, in {readMs} ms; prima fino al tempo scaduto, 60 s)");

            // Uno scaricamento fermo prima delle intestazioni: lo stesso.
            calls = 0;
            GraphProvider.UseTestHandler(new ArfHttp(async (request, cancel) =>
            {
                if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.Infinite, cancel);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            }));
            var download = new GraphProvider(account).GetAttachmentAsync(new MessageSummary { Id = "AQMkA11", FolderId = "inbox" }, new Attachment { Id = "AAMkA11" });
            await Task.Delay(300);
            GraphProvider.ResetConnections();
            var bytes = await download.WaitAsync(TimeSpan.FromSeconds(10));
            Check(bytes.Length == 3 && calls == 2, $"A-11: uno scaricamento fermo riparte sulla connessione nuova ({calls} richieste, {bytes.Length} byte)");

            // Una scrittura ferma: non si ripete, e cade come una connessione caduta (la coda la riprova).
            calls = 0;
            GraphProvider.UseTestHandler(new ArfHttp(async (request, cancel) =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(Timeout.Infinite, cancel);
                return Json("{}");
            }));
            var write = new GraphProvider(account).SetFlagAsync([new MessageSummary { Id = "AQMkA11", FolderId = "inbox" }], true);
            await Task.Delay(300);
            GraphProvider.ResetConnections();
            Exception? writeError = null;
            try { await write.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception ex) { writeError = ex; }
            Check(writeError is IOException && ActionQueue.Passing(writeError) && calls == 1,
                  $"A-11: una scrittura ferma non si ripete ({calls} richieste) e cade come una connessione caduta, che la coda riprova ({writeError?.GetType().Name}, {writeError is { } w && ActionQueue.Passing(w)})");

            // Una lettura caduta con la connessione (10054): una volta di nuovo; due volte, l'errore; senza rete (10051) subito.
            calls = 0;
            GraphProvider.UseTestHandler(new ArfHttp((request, cancel) => Interlocked.Increment(ref calls) == 1
                ? Task.FromException<HttpResponseMessage>(Lost(10054))
                : Task.FromResult(Json("{\"unreadItemCount\":3}"))));
            int afterReset = await new GraphProvider(account).GetInboxUnreadAsync();
            int resetCalls = calls;
            calls = 0;
            GraphProvider.UseTestHandler(new ArfHttp((request, cancel) => { Interlocked.Increment(ref calls); return Task.FromException<HttpResponseMessage>(Lost(10054)); }));
            Exception? twice = null;
            try { await new GraphProvider(account).GetInboxUnreadAsync(); }
            catch (Exception ex) { twice = ex; }
            int twiceCalls = calls;
            calls = 0;
            GraphProvider.UseTestHandler(new ArfHttp((request, cancel) => { Interlocked.Increment(ref calls); return Task.FromException<HttpResponseMessage>(Lost(10051)); }));
            Exception? offline = null;
            try { await new GraphProvider(account).GetInboxUnreadAsync(); }
            catch (Exception ex) { offline = ex; }
            Check(afterReset == 3 && resetCalls == 2 && twice is HttpRequestException && twiceCalls == 2 && Network.IsOffline(offline) && calls == 1,
                  $"A-11: una lettura caduta con la connessione (10054) si ripete una volta ({resetCalls} richieste, risposta {afterReset}); caduta due volte l'errore arriva " +
                  $"({twice?.GetType().Name}, {twiceCalls} richieste); senza rete (10051) nessuna ripetizione ({calls} richiesta)");

            // Le API di Google (calendario, contatti): lo stesso.
            GoogleAuth.TestToken = "prova-google";
            calls = 0;
            GoogleApi.UseTestHandler(new ArfHttp(async (request, cancel) =>
            {
                if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.Infinite, cancel);
                return Json("{\"ok\":1}");
            }));
            var google = GoogleApi.SendAsync(new GoogleService("Prova", "prova", "", ""), new MailAccount { Kind = AccountKind.Gmail, Email = "prova.a11@gmail.example" },
                                             HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");
            await Task.Delay(300);
            GoogleApi.ResetConnections();
            var node = await google.WaitAsync(TimeSpan.FromSeconds(10));
            Check(node?["ok"]?.GetValue<int>() == 1 && calls == 2, $"A-11: una lettura delle API di Google ferma riparte sulla connessione nuova ({calls} richieste)");

            // Il gestore vero: i ping, e ResetConnections che lo cambia (senza mandare nulla).
            using (var api = Network.ApiHandler())
            {
                bool pings = api.KeepAlivePingDelay == TimeSpan.FromSeconds(30) && api.KeepAlivePingTimeout == TimeSpan.FromSeconds(10) &&
                             api.KeepAlivePingPolicy == HttpKeepAlivePingPolicy.WithActiveRequests;
                Check(pings, $"A-11: il gestore di Graph e Google manda un ping dopo 30 s senza risposta con una richiesta in corso, e senza risposta al ping in 10 s chiude la connessione " +
                             $"({api.KeepAlivePingDelay.TotalSeconds} s, {api.KeepAlivePingTimeout.TotalSeconds} s, {api.KeepAlivePingPolicy})");
            }
            GraphProvider.UseTestHandler(null);
            var before = GraphProvider.TestNet;
            GraphProvider.ResetConnections();
            var after = GraphProvider.TestNet;
            bool closed;
            try
            {
                using var old = new HttpClient(before, disposeHandler: false);
                using var _ = await old.GetAsync("http://127.0.0.1:9/");
                closed = false;
            }
            catch (ObjectDisposedException) { closed = true; }
            catch (Exception) { closed = false; }
            Check(after != before && closed && after.KeepAlivePingPolicy == HttpKeepAlivePingPolicy.WithActiveRequests,
                  $"A-11: con le connessioni rifatte Graph ha un gestore nuovo ({after != before}), con i ping, e quello di prima è chiuso ({closed})");

            // App.WakeAsync le rifà, prima di tutto il resto (nei sorgenti, se ci sono).
            bool wakes = Ch1Code("UI", "App.cs") is not { } app ||
                         Ch3InOrder(Ch3Part(app, "internal async Task WakeAsync(", "\n    }"), "GraphProvider.ResetConnections();", "GoogleApi.ResetConnections();",
                                    "p.DropConnections();", "_ = CheckDeltasAsync(again: true);");
            Check(wakes, $"A-11 nei sorgenti: al risveglio (App.WakeAsync) le connessioni HTTP di Graph e Google si rifanno prima dei controlli ({wakes})");
        }
        finally
        {
            GraphProvider.UseTestHandler(new NoHandler("Graph"));
            GoogleApi.UseTestHandler(new NoHandler("Google"));
            GoogleAuth.TestToken = null;
        }
    }

    // ---------------------------------------------------------------- A-07: le regole sul testo e la connessione principale

    /// <summary>
    /// A-07: con una regola di Missiva che guarda il testo, il corpo dei messaggi nuovi si legge sulla connessione in
    /// sottofondo (Rules.ApplyToNewAsync con PrefetchAsync: EXAMINE e BODY.PEEK), non sulla principale, dove passano elenco,
    /// messaggi aperti e allegati: con 1,2 MB in arrivo a pezzi sulla principale le regole finiscono subito, e con loro la
    /// notifica, che viene dopo. Prima finivano insieme allo scaricamento (sonda A-07: 6,45 s).
    /// </summary>
    static async Task ArfRulesBackgroundAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false)) { AnnounceOnNoop = true };
        var big = SpeedMail("Disegni del progetto", "in allegato i disegni", daysAgo: 1);
        big.Body = new MimeKit.Multipart("mixed")
        {
            big.Body!,
            new MimeKit.MimePart("application", "octet-stream") { FileName = "disegni.bin", Content = new MimeKit.MimeContent(new MemoryStream(new byte[1_200_000])), ContentTransferEncoding = MimeKit.ContentEncoding.Base64 },
        };
        fake.Add("INBOX", big, "\\Seen");
        var provider = new ImapProvider(SpeedAccount(fake, "mario.a07@studio-prova.example"));
        using var closing = new ArfClosing(provider);
        var folders = await provider.GetFoldersAsync();
        var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
        var page = await provider.GetMessagesAsync(inbox, null, null);
        var rule = new MailRule { AccountId = provider.Account.Id, Name = "Fatture", Enabled = true, BodyContains = ["fattura"], MarkRead = true };
        Store.Settings.Rules.Add(rule);
        Rules.ForgetMarks();
        try
        {
            var since = DateTime.UtcNow.AddMinutes(-1);
            await Rules.ApplyToNewAsync(provider, since, () => Task.FromResult(folders)); // il mark delle regole
            var drawings = page.Messages.First(m => m.Subject == "Disegni del progetto");
            var attachment = (await provider.GetBodyAsync(drawings)).Attachments.First();
            fake.Trickle = (32 * 1024, TimeSpan.FromMilliseconds(120));
            var clock = Stopwatch.StartNew();
            var download = provider.GetAttachmentAsync(drawings, attachment);
            await Task.Delay(300);
            // Un messaggio che la regola deve leggere per sapere che non la riguarda: è quello che poi si annuncia. (Uno che la
            // riguarda non si annuncia: le regole agiscono, e le azioni restano sulla principale, come dice il rapporto.)
            fake.Add("INBOX", SpeedMail("Preventivo per la cucina", "in allegato il preventivo"));
            var outcome = await Rules.ApplyToNewAsync(provider, since, () => Task.FromResult(folders));
            long ruled = clock.ElapsedMilliseconds;
            bool downloading = !download.IsCompleted;
            await download;
            long downloaded = clock.ElapsedMilliseconds;
            Check(outcome is { Matched: 0 } && downloading && ruled < 2000 && downloaded - ruled > 1500,
                  $"A-07: regola \"il testo contiene fattura\" e un messaggio nuovo che non la riguarda, con 1,2 MB in arrivo a pezzi sulla connessione principale: regole finite a {ruled} ms, " +
                  $"scaricamento finito a {downloaded} ms: la notifica, che viene dopo le regole, non aspetta più lo scaricamento");
        }
        finally
        {
            fake.Trickle = null;
            Store.Settings.Rules.Remove(rule);
            Rules.ForgetMarks();
        }
    }

    // ---------------------------------------------------------------- A-10: il registro

    /// <summary>
    /// A-10 nei sorgenti: il registro della versione installata (Log.Write, non Log.Time) dice la sospensione e la ripresa,
    /// l'IDLE fermo sul DONE e quello ricollegato, e, quando il controllo di riserva trova posta di un account con l'IDLE,
    /// com'era l'IDLE. Prima nessuna di queste righe c'era, e un IDLE morto non si distingueva da una sospensione o da Gmail lento.
    /// </summary>
    static void ArfLogSources()
    {
        if (Ch1Code("UI", "App.cs") is not { } app || Ch1Code("Core", "Idle.cs") is not { } idle)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: le righe del registro non sono state lette)");
            return;
        }
        bool power = Ch3Part(app, "void OnPowerModeChanged(", "\n    });").Contains("Log.Write(\"sospensione del PC\")")
                     && Ch3Part(app, "internal async Task WakeAsync(", "\n    }").Contains("Log.Write($\"{why}: rifaccio le connessioni");
        bool done = idle.Contains("Log.Write($\"IDLE su INBOX di {who}: il server non ha risposto al DONE del rinnovo");
        bool back = idle.Contains("Log.Write($\"IDLE su INBOX di {who}: di nuovo in sessione su una connessione nuova");
        bool why = Ch3Part(app, "async Task<int> CheckAccountAsync(", "\n    }").Contains("+ WhyNotPushed(p, reason, incoming.Count));")
                   && Ch3Part(app, "string WhyNotPushed(", "\n    }").Contains("InboxIdle.Watching(p.Account.Email)");
        Check(power && done && back && why,
              $"A-10 nei sorgenti, le righe del registro anche nella versione installata: sospensione e ripresa ({power}), IDLE fermo sul DONE ({done}), IDLE ricollegato ({back}), " +
              $"com'era l'IDLE quando il controllo di riserva trova posta ({why})");
    }

    // ---------------------------------------------------------------- A-04: la ripresa dalla sospensione e i cambi di rete

    static async Task<bool> ArfWithin(Task task, TimeSpan limit) => await Task.WhenAny(task, Task.Delay(limit)) == task;

    /// <summary>
    /// A-04: dopo la ripresa del PC tutte le connessioni aperte sono morte senza che TCP lo sappia (qui mute: leggono e non
    /// rispondono, senza chiudersi), e un controllo dei nuovi messaggi è fermo su quella in sottofondo. DropConnections (il
    /// gestore della ripresa) le butta tutte: il controllo fermo riprova su una connessione nuova e trova il messaggio
    /// arrivato col PC sospeso, la sessione dell'IDLE in corso si chiude e se ne apre un'altra (avviso Connected), e anche la
    /// principale è nuova. Prima nessuno lo faceva: il controllo aspettava il tempo scaduto di MailKit (60 s con IMAP, 120
    /// con Gmail) e l'IDLE il rinnovo.
    /// </summary>
    static async Task ArfDropAsync(bool gmail)
    {
        InboxIdle.Renew = TimeSpan.FromMinutes(5); // nessun rinnovo durante la prova: solo la ripresa
        InboxIdle.FirstRetry = TimeSpan.FromMilliseconds(200);
        using var fake = new FakeImap(new Flavor(Gmail: gmail)) { AnnounceOnNoop = true };
        fake.Add("INBOX", SpeedMail("Vecchio", "già qui", daysAgo: 2), "\\Seen");
        var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
        using var closing = new ArfClosing(provider);
        var clock = Stopwatch.StartNew();
        var events = new ConcurrentQueue<(long At, InboxNotice Notice)>();
        using var stop = new CancellationTokenSource();
        var watching = provider.WatchInboxAsync(e => events.Enqueue((clock.ElapsedMilliseconds, e)), stop.Token);
        bool up = await Until(() => fake.Idling == 1, TimeSpan.FromSeconds(5));
        var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
        await provider.GetMessagesAsync(inbox, null, null);
        await provider.GetNewAsync(DateTime.UtcNow.AddMinutes(-1));
        var idleBefore = fake.IdleConnections();
        fake.MuteOpen();
        fake.Add("INBOX", SpeedMail("Preventivo urgente", "arrivato col PC sospeso"));
        var pending = provider.GetNewAsync(DateTime.UtcNow.AddMinutes(-2));
        await Task.Delay(300);
        long dropAt = clock.ElapsedMilliseconds;
        provider.DropConnections();
        bool answered = await ArfWithin(pending, TimeSpan.FromSeconds(8));
        long answeredIn = clock.ElapsedMilliseconds - dropAt;
        var found = pending.IsCompletedSuccessfully ? pending.Result : null;
        bool idleBack = await Until(() => fake.IdleConnections() is [var c] && !idleBefore.Contains(c), TimeSpan.FromSeconds(5));
        bool connected = await Until(() => events.Any(e => e.At > dropAt && e.Notice.Kind == InboxEvent.Connected), TimeSpan.FromSeconds(3));
        var listing = provider.GetMessagesAsync(inbox, null, null);
        bool listed = await ArfWithin(listing, TimeSpan.FromSeconds(5)) && listing.IsCompletedSuccessfully && listing.Result.Messages.Any(m => m.Subject == "Preventivo urgente");
        // Un secondo risveglio subito dopo (la rete che cambia dopo la ripresa): la sessione dell'IDLE ripresa si rifà di nuovo.
        var idleSecond = fake.IdleConnections();
        provider.DropConnections();
        bool againBack = await Until(() => fake.IdleConnections() is [var c] && !idleSecond.Contains(c), TimeSpan.FromSeconds(5));
        stop.Cancel();
        await ArfWithin(watching, TimeSpan.FromSeconds(5));
        Check(up && answered && found is [{ Subject: "Preventivo urgente" }] && idleBack && connected && listed && againBack,
              $"A-04 ({Arf(gmail)}): connessioni morte in silenzio come dopo la ripresa, un controllo fermo su quella in sottofondo: DropConnections, e il controllo " +
              $"{(answered ? $"risponde in {answeredIn} ms con {found?.Count} messaggi" : "è ancora fermo dopo 8 s")}; la sessione dell'IDLE si rifà su una connessione nuova ({idleBack}, " +
              $"avviso Connected {connected}), anche a un secondo risveglio subito dopo ({againBack}); l'elenco dalla principale nuova ha il messaggio ({listed})");
    }

    /// <summary>
    /// A-04: il controllo fatto al risveglio si ripete con attese brevi finché la rete risponde (Arrival.RetryAsync): qui
    /// due volte "Host sconosciuto" (11001, il DNS non ancora pronto dopo la ripresa) e poi riesce; un errore che non è della
    /// rete non si ripete; finite le attese, l'ultimo tentativo lo sa (chi chiama dice allora l'errore come sempre).
    /// </summary>
    static async Task ArfWakeRetryAsync()
    {
        var waits = new[] { TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(150) };
        var lasts = new List<bool>();
        int calls = 0;
        bool ok = await Arrival.RetryAsync(last =>
        {
            lasts.Add(last);
            return Task.FromResult<Exception?>(++calls <= 2 ? new SocketExceptionFor11001() : null);
        }, waits, Arrival.Transient);
        int refused = 0;
        bool notNetwork = await Arrival.RetryAsync(_ => { refused++; return Task.FromResult<Exception?>(new MailException("password rifiutata")); }, waits, Arrival.Transient);
        var exhausted = new List<bool>();
        bool never = await Arrival.RetryAsync(last => { exhausted.Add(last); return Task.FromResult<Exception?>(new TimeoutException()); }, waits, Arrival.Transient);
        bool dns = Arrival.Transient(new HttpRequestException("Host sconosciuto. (oauth2.googleapis.com:443)", new SocketExceptionFor11001()));
        Check(ok && calls == 3 && lasts.SequenceEqual([false, false, false]) && !notNetwork && refused == 1 && !never && exhausted.SequenceEqual([false, false, false, true]) && dns
              && Arrival.WakeRetries.Sum(w => w.TotalSeconds) is >= 30 and <= 90 && Arrival.WakeRetries[0] <= TimeSpan.FromSeconds(3),
              $"A-04: al risveglio il controllo si ripete se la rete non è pronta: due \"Host sconosciuto\" poi riuscito al terzo tentativo ({ok}, {calls}); " +
              $"una password rifiutata non si ripete ({refused}); finite le attese l'ultimo tentativo lo sa ({string.Join(" ", exhausted)}); il DNS di Google dentro HttpRequestException è un errore di rete ({dns}); " +
              $"le attese vere {string.Join(", ", Arrival.WakeRetries.Select(w => w.TotalSeconds))} s");
    }

    sealed class SocketExceptionFor11001() : System.Net.Sockets.SocketException(11001);

    /// <summary>A-04: Arrival.SleepClock si accorge che il PC ha dormito (la differenza fra i due orologi di Windows cresce), sotto 5 s no.</summary>
    static void ArfSleepClock()
    {
        long slept = 1000;
        var clock = new Arrival.SleepClock(() => slept);
        long a = clock.Check();
        slept += 3000;
        long b = clock.Check();
        slept += 45_000;
        long c = clock.Check();
        long d = clock.Check();
        long real = Arrival.SleptMs();
        Check(a == 0 && b == 0 && c == 45_000 && d == 0 && real >= 0,
              $"A-04: l'orologio della sospensione: niente da fermo ({a}), niente per 3 s di rumore ({b}), 45 s di sospensione ({c}), poi di nuovo niente ({d}); su questo PC {real / 1000} s sospeso dall'avvio di Windows");
    }

    /// <summary>A-04: la rete in una riga (Arrival.NetworkPrint) è la stessa se la rete non cambia: un "indirizzi cambiati" di Windows senza un cambio vero non rifà le connessioni.</summary>
    static void ArfNetworkPrint()
    {
        var a = Arrival.NetworkPrint();
        var b = Arrival.NetworkPrint();
        Check(a == b, $"A-04: la rete in una riga è la stessa due volte di fila ({(a.Length == 0 ? "nessuna scheda accesa" : $"{a.Split(';').Length} schede")})");
    }

    /// <summary>
    /// A-04 nei sorgenti: App si accorge della ripresa (SystemEvents.PowerModeChanged e l'orologio della sospensione) e dei
    /// cambi di rete veri (la rete in una riga cambiata), e in tutti e tre i casi WakeAsync scrive una riga nel registro,
    /// butta le connessioni, sveglia gli IDLE, rifà la query delta di Graph, controlla tutti gli account insieme con i
    /// tentativi brevi e poi rilegge cartelle ed elenco; MISSIVA_KEYS "ripresa" (solo Debug) chiama lo stesso gestore.
    /// </summary>
    static void ArfWakeSources()
    {
        if (Ch1Code("UI", "App.cs") is not { } app || Ch1Code("Core", "DevTools.cs") is not { } tools || Ch1Code("UI", "MainWindow.xaml.cs") is not { } main)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: il gestore della ripresa non è stato letto)");
            return;
        }
        bool power = app.Contains("SystemEvents.PowerModeChanged += OnPowerModeChanged;") && Ch3Part(app, "void OnPowerModeChanged(", "\n    }").Contains("PowerModes.Resume")
                     && app.Contains("SystemEvents.PowerModeChanged -= OnPowerModeChanged;");
        bool clock = app.Contains("new Arrival.SleepClock()") && Ch3Part(app, "void OnSleepClock(", "\n    }").Contains("WakeAsync(");
        string changed = Ch3Part(app, "void OnNetworkChanged(", "\n    });");
        bool network = Ch3InOrder(changed, "Arrival.NetworkPrint()", "WakeAsync(", "OnNetworkBack();");
        string wake = Ch3Part(app, "internal async Task WakeAsync(", "\n    }");
        bool steps = Ch3InOrder(wake, "Log.Write(", ".DropConnections();", "InboxIdle.RetryNow();", "CheckDeltasAsync(again: true)", "Arrival.RunAllAsync(", "AfterWakeAll();", "Log.Write(");
        bool retries = Ch3InOrder(Ch3Part(app, "Task<int> WakeCheckAsync(", "\n    }"), "Arrival.RetryAsync(", "Arrival.WakeRetries, Arrival.Transient", "AfterWake(p)");
        bool key = Ch3Part(tools, "case \"ripresa\":", "return;").Contains("App.Current.WakeAsync(");
        bool reload = Ch3Part(main, "internal void AfterWake(", "\n    }").Contains("LoadFoldersAsync(");
        Check(power && clock && network && steps && retries && key && reload,
              $"A-04 nei sorgenti: la ripresa da Windows ({power}) e dall'orologio della sospensione ({clock}), i cambi di rete veri ({network}); WakeAsync scrive nel registro, butta " +
              $"le connessioni, sveglia gli IDLE, rifà la query delta, controlla tutti insieme ({steps}) con i tentativi brevi ({retries}) e rilegge cartelle ed elenco ({reload}); " +
              $"\"ripresa\" in MISSIVA_KEYS ({key})");
    }
}
#endif
