using MailKit;
using MailKit.Net.Imap;

namespace Missiva;

/// <summary>
/// Che cosa è successo nella Posta in arrivo, detto dal server senza aspettare il controllo di ogni due minuti. Connected: la
/// sessione dell'IDLE è collegata con la Posta in arrivo aperta (anche a ogni rinnovo), cioè il server si raggiunge: chi
/// riceve toglie "Non in linea" senza aspettare un controllo (audit del 7 ottobre, A-15).
/// </summary>
public enum InboxEvent { Arrived, Changed, Removed, Connected }

/// <summary>
/// Un avviso dell'IDLE: che cosa è successo e, per i segni cambiati, il messaggio (UID) con i suoi segni quando il server li
/// dice nella FETCH (Gmail e Dovecot sì): allora basta cambiare la riga, senza rileggere la Posta in arrivo.
/// </summary>
/// <param name="Reconnected">
/// Connected su una connessione nuova dopo un'altra sessione o dopo una caduta (non il primo collegamento e non un rinnovo):
/// quello che è arrivato mentre l'IDLE era giù non l'ha annunciato nessuno, e chi riceve controlla l'account (A-03).
/// </param>
/// <param name="New">
/// Per Arrived: quanti messaggi nuovi ha annunciato il server (EXISTS), perché chi riceve li cerchi tutti: Gmail rende
/// visibile il messaggio alle altre connessioni anche qualche secondo dopo l'avviso, e il controllo si ripete finché non li
/// ha trovati (App.OnInboxEvent, A-05).
/// </param>
public sealed record InboxNotice(InboxEvent Kind, string? Id = null, bool? Read = null, bool? Flagged = null, bool Reconnected = false, int New = 0);

/// <summary>
/// I cambiamenti della Posta in arrivo di Graph dall'ultima volta (query delta): i messaggi nuovi o cambiati (letti altrove)
/// con il loro stato di lettura, e quelli usciti (eliminati o spostati altrove).
/// </summary>
public sealed class InboxChanges
{
    /// <summary>
    /// Quando la query delta è partita (nasce con lei): per i messaggi segnati qui dopo, o con la scrittura ancora in viaggio,
    /// lo stato di lettura della delta non vale (ReadMarks.Apply; audit di affidabilità del 25 ottobre 2026, L-05).
    /// </summary>
    public DateTime Asked { get; } = ReadMarks.Asked();
    public List<(string Id, bool Read)> Seen { get; } = new();
    public List<string> Removed { get; } = new();
    public bool Any => Seen.Count + Removed.Count > 0;
    /// <summary>
    /// La query delta è ripartita da capo (un deltaLink che Graph non accetta più, e la lettura dopo che fa da punto di
    /// partenza): quello che è arrivato intanto non lo dice nessuna delle due, e chi riceve controlla l'account (A-08).
    /// </summary>
    public bool Restarted { get; init; }
}

/// <summary>
/// I non letti della Posta in arrivo contati dal server (App.CheckAccountAsync, App.RecountSoon, le cartelle rilette)
/// valgono solo se nel frattempo Missiva non li ha cambiati. Una lettura nel riquadro abbassa subito il numero e parte verso
/// il server: un conteggio chiesto un attimo prima, o con la scrittura ancora in viaggio, tornava con il numero di prima e
/// lo rimetteva (7 ottobre 2026, registro della versione installata: numero sull'icona 2, 1 e di nuovo 2 nello stesso
/// secondo, corretto solo dal controllo di 28 secondi dopo). Qui, per account, le modifiche fatte da Missiva (Edited) e le
/// sue scritture in corso (WriteAsync; per Gmail e IMAP tutte, da InboxIdle.OwnAsync): chi conta prende un biglietto prima
/// di chiedere (Ticket) e scrive il numero solo se vale ancora (Valid). Finita una scrittura, Written avvisa e il numero si
/// riconta poco dopo: così torna giusto anche dopo un messaggio da leggere eliminato o spostato senza aprirlo, che qui
/// nessuno toglie dal conto (prima restava alto fino al controllo di ogni due minuti).
/// </summary>
public static class UnreadSync
{
    /// <summary>
    /// Quanto dopo una scrittura il conteggio del server può essere ancora quello di prima (i non letti di una cartella di
    /// Graph si aggiornano un attimo dopo la risposta alla scrittura): prima non si conta.
    /// </summary>
    public static TimeSpan Settle = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Per account: le modifiche, le scritture in corso, la fine dell'ultima e, per chi ha chiesto di aspettare di più dopo la
    /// sua (WriteAsync con settle), fino a quando.
    /// </summary>
    /// <remarks>Asked: il primo biglietto dato per le modifiche di adesso (AskedAt); si azzera a ogni modifica.</remarks>
    static readonly Dictionary<string, (long Changes, int Writing, DateTime Ended, DateTime Quiet, DateTime Asked)> states = new();

    /// <summary>Una scrittura di Missiva sull'account è finita, riuscita o no (sul thread di chi l'ha fatta).</summary>
    public static event Action<string>? Written;

    static void Change(string accountId, int writing, bool ended = false, TimeSpan? settle = null)
    {
        lock (states)
        {
            var s = states.GetValueOrDefault(accountId);
            var now = DateTime.UtcNow;
            var quiet = ended && settle is { } longer && now + longer > s.Quiet ? now + longer : s.Quiet;
            states[accountId] = (s.Changes + 1, Math.Max(0, s.Writing + writing), ended ? now : s.Ended, quiet, default);
        }
    }

    /// <summary>I non letti della Posta in arrivo cambiati qui: un messaggio letto nel riquadro, segnato a mano, arrivato.</summary>
    public static void Edited(string accountId) => Change(accountId, 0);

    /// <summary>Una scrittura di Missiva sulla casella che può cambiare i non letti (letto, da leggere, elimina, sposta, Posponi).</summary>
    /// <param name="settle">
    /// Quanto aspettare dopo la fine, oltre a Settle, prima di credere al conteggio del server: Exchange aggiorna i non letti
    /// della cartella qualche secondo dopo aver risposto al PATCH del letto (GraphProvider.ReadCountSettle).
    /// </param>
    public static async Task<T> WriteAsync<T>(string accountId, Func<Task<T>> write, TimeSpan? settle = null)
    {
        Change(accountId, 1);
        try
        {
            return await write();
        }
        finally
        {
            Change(accountId, -1, ended: true, settle);
            Written?.Invoke(accountId);
        }
    }

    public static Task WriteAsync(string accountId, Func<Task> write, TimeSpan? settle = null) => WriteAsync(accountId, async () => { await write(); return true; }, settle);

    /// <summary>
    /// Da prendere prima di chiedere i non letti al server. null se adesso non è il momento: una scrittura di Missiva è in
    /// corso o appena finita, e il server risponderebbe con il numero di prima.
    /// </summary>
    public static long? Ticket(string accountId)
    {
        lock (states)
        {
            var s = states.GetValueOrDefault(accountId);
            if (s.Writing > 0 || DateTime.UtcNow - s.Ended < Settle || DateTime.UtcNow < s.Quiet) return null;
            if (s.Asked == default) states[accountId] = s with { Asked = DateTime.UtcNow };
            return s.Changes;
        }
    }

    /// <summary>
    /// Un numero chiesto al server con quel biglietto è stato chiesto non prima di allora: l'istante del primo biglietto dato
    /// per le stesse modifiche (chi conta lo prende subito prima di chiedere). null se il biglietto non vale più. Il numero
    /// del server comprende i messaggi arrivati prima di allora (FolderItem.CountedAt, MainWindow.ArrivedUnread).
    /// </summary>
    public static DateTime? AskedAt(string accountId, long? ticket)
    {
        lock (states)
        {
            var s = states.GetValueOrDefault(accountId);
            return ticket == s.Changes && s.Asked != default ? s.Asked : null;
        }
    }

    /// <summary>Il numero chiesto con quel biglietto vale ancora: da allora Missiva non ha cambiato né scritto nulla.</summary>
    public static bool Valid(string accountId, long? ticket)
    {
        lock (states)
        {
            var s = states.GetValueOrDefault(accountId);
            return ticket == s.Changes && s.Writing == 0;
        }
    }
}

/// <summary>
/// Nuovi messaggi subito, come Thunderbird ed eM Client: una connessione IMAP in IDLE sulla Posta in arrivo (in sola
/// lettura), rinnovata ogni 2 minuti (Renew) con un tempo limite sulla risposta al DONE (DoneLimit), e ripresa dopo una
/// caduta con attese crescenti fino a 5 minuti. Quando arriva EXISTS (un messaggio nuovo), FETCH con i segni (letto
/// altrove) o EXPUNGE (eliminato o spostato altrove), chi l'ha avviata controlla quell'account. Sulla connessione
/// dell'IDLE non passa altro: nessun comando interrompe l'IDLE senza DONE. I server senza IDLE restano con il controllo
/// di riserva.
/// </summary>
public static class InboxIdle
{
    /// <summary>
    /// Ogni quanto l'IDLE si rinnova (DONE e di nuovo IDLE). È l'unico momento in cui Missiva scrive sulla connessione
    /// dell'IDLE: una connessione morta in silenzio (il NAT del router che la dimentica, un server che chiude mentre il PC
    /// dorme, un programma in mezzo che la tiene aperta) si scopre solo lì. Fino all'audit di affidabilità del 25 ottobre
    /// 2026 (A-01) erano 9 minuti, come nell'esempio di MailKit per Gmail: intanto ogni arrivo aspettava il controllo di
    /// riserva. Due minuti costano un rinnovo in più ogni due minuti per account (poche centinaia di byte, qualche
    /// millisecondo di processore: misura nel gruppo affidabilita-arrivo) e tengono viva la connessione anche nei NAT che
    /// dimenticano dopo pochi minuti quelle ferme. Il keepalive di TCP non basta da solo: non vede una connessione che TCP
    /// tiene viva ma su cui il server non risponde più (il caso della sonda arrivo-done).
    /// </summary>
    public static TimeSpan Renew = TimeSpan.FromMinutes(2);
    /// <summary>
    /// Quanto si aspetta la risposta al DONE del rinnovo: oltre, la sessione si cancella (MailKit chiude la connessione) e
    /// se ne apre un'altra. Senza tempo limite MailKit la aspettava per sempre, anche con il suo Timeout (sonda arrivo-done:
    /// dopo 150 s aspettava ancora), e l'IDLE di quell'account restava fermo fino al riavvio di Missiva (A-01).
    /// </summary>
    public static TimeSpan DoneLimit = TimeSpan.FromSeconds(20);
    /// <summary>La prima attesa dopo una caduta, poi doppia a ogni tentativo, fino a MaxRetry.</summary>
    public static TimeSpan FirstRetry = TimeSpan.FromSeconds(5);
    public static TimeSpan MaxRetry = TimeSpan.FromMinutes(5);

    // ---------------------------------------------------------------- l'eco delle azioni di Missiva

    /// <summary>
    /// Per quanto, dopo una scrittura di Missiva, il suo eco all'IDLE si aspetta ancora. Era 4 s per tutto l'account: Gmail
    /// lo manda fino a 34 s dopo (registro dell'autore, 15-25 ottobre 2026: 33 avvisi su 39 dopo 1-34 s, 25 di questi dopo
    /// almeno 16; 3 dopo 43-85 s), e quei 4 s coprivano invece gli
    /// avvisi veri degli altri messaggi. Ora l'eco è per messaggio e per segno (Expect, OwnEcho con l'avviso): tenerlo più a
    /// lungo costa solo per lo stesso messaggio con lo stesso segno.
    /// </summary>
    public static TimeSpan OwnWindow = TimeSpan.FromSeconds(60);

    /// <summary>Per account, le scritture di Missiva in corso.</summary>
    static readonly Dictionary<string, int> own = new();

    /// <summary>Che cosa ci si aspetta di vedere tornare all'IDLE della Posta in arrivo: un segno (letto o contrassegno, con il valore scritto), un segno qualsiasi (categorie, \Deleted), un messaggio uscito.</summary>
    public enum EchoOf { Seen, Flagged, Any, Removed }

    /// <summary>Una scrittura di Missiva (OwnAsync): quando è finita, e se non è riuscita (allora il suo eco non si aspetta più).</summary>
    sealed class EchoScope(string accountId)
    {
        public string AccountId { get; } = accountId;
        public DateTime Ended = DateTime.MaxValue;
        public bool Failed;
        /// <summary>Gli echi già annotati da questa scrittura: una ripresa (RunAsync dopo una connessione caduta) non li ripete.</summary>
        public readonly HashSet<(string Id, EchoOf Kind, bool Value)> Noted = new();
    }

    /// <summary>Un eco atteso: il messaggio (UID della Posta in arrivo), che cosa e con quale valore; vale una volta sola.</summary>
    sealed class Expected
    {
        public required EchoScope Scope { get; init; }
        public required string Id { get; init; }
        public EchoOf Kind { get; init; }
        public bool Value { get; init; }
        public bool Consumed;
    }

    /// <summary>Sotto il suo lucchetto: gli echi attesi, dal più vecchio.</summary>
    static readonly List<Expected> expected = new();
    /// <summary>La scrittura in corso (OwnAsync) di chi chiama: Expect le annota i suoi echi, anche dai metodi statici dei provider.</summary>
    static readonly AsyncLocal<EchoScope?> writing = new();

    /// <summary>
    /// Una scrittura di Missiva sulla casella (Elimina, Archivia, Sposta, letto, contrassegno, Posponi...), fatta dal provider:
    /// il server manda l'EXPUNGE o la FETCH anche alla connessione dell'IDLE, e ogni Canc rileggeva la Posta in arrivo e rifaceva
    /// l'elenco. Dentro la scrittura il provider dice quali avvisi ne torneranno (Expect), e OwnEcho dice di ignorare quelli.
    /// </summary>
    /// <param name="affectsUnread">
    /// Falso per le scritture che non cambiano i non letti (contrassegno, categoria): l'eco all'IDLE c'è lo stesso, ma i
    /// conteggi del server chiesti intanto valgono e alla fine non si riconta. Prima ogni contrassegno e ogni categoria
    /// facevano partire un riconteggio un secondo dopo (NOOP e SEARCH sulla connessione in sottofondo; audit del 7 ottobre, B-17).
    /// </param>
    public static async Task<T> OwnAsync<T>(string accountId, Func<Task<T>> write, bool affectsUnread = true)
    {
        var scope = writing.Value = new EchoScope(accountId); // vale per write e per quello che chiama, non per chi ha chiamato qui
        lock (own) own[accountId] = own.GetValueOrDefault(accountId) + 1;
        try
        {
            if (!affectsUnread) return await write();
            return await UnreadSync.WriteAsync(accountId, write); // i non letti contati intanto dal server non valgono
        }
        catch
        {
            scope.Failed = true; // non riuscita: il suo eco non arriverà, e un avviso uguale che arriva è di qualcun altro
            throw;
        }
        finally
        {
            lock (expected) scope.Ended = DateTime.UtcNow;
            lock (own) own[accountId] = Math.Max(0, own.GetValueOrDefault(accountId) - 1);
        }
    }

    public static Task OwnAsync(string accountId, Func<Task> write, bool affectsUnread = true) => OwnAsync(accountId, async () => { await write(); return true; }, affectsUnread);

    /// <summary>
    /// I messaggi che Missiva sta per mettere nella Posta in arrivo (Sposta lì, Annulla di un Elimina o di un Archivia, il
    /// ritorno di Posponi), per chiave stabile (X-GM-MSGID, Message-Id), per OwnArrivalWindow. Nella Posta in arrivo hanno un
    /// UID nuovo, e da quando i nuovi si riconoscono per UID (audit di affidabilità del 25 ottobre 2026, A-06) sembrerebbero
    /// arrivati: il controllo non li annuncia (App.CheckAccountAsync). Prima non serviva: lo spostamento non cambia la data
    /// di arrivo, e quella era vecchia.
    /// </summary>
    public static void OwnArrivals(string accountId, IEnumerable<string> keys)
    {
        var until = Environment.TickCount64 + (long)OwnArrivalWindow.TotalMilliseconds;
        lock (ownArrivals)
        {
            var now = Environment.TickCount64;
            foreach (var old in ownArrivals.Where(o => o.Value < now).Select(o => o.Key).ToList()) ownArrivals.Remove(old);
            foreach (var key in keys.Where(k => k.Length > 0)) ownArrivals[(accountId, key)] = until;
        }
    }

    /// <summary>Il messaggio con quella chiave stabile l'ha messo Missiva nella Posta in arrivo dell'account da poco (OwnArrivals).</summary>
    public static bool IsOwnArrival(string accountId, string key)
    {
        if (key.Length == 0) return false;
        lock (ownArrivals) return ownArrivals.TryGetValue((accountId, key), out var until) && until >= Environment.TickCount64;
    }

    /// <summary>Quanto si ricorda un messaggio messo da Missiva nella Posta in arrivo: più di un giro del controllo di riserva.</summary>
    public static TimeSpan OwnArrivalWindow = TimeSpan.FromMinutes(10);

    static readonly Dictionary<(string Account, string Key), long> ownArrivals = new();

    /// Dentro una scrittura (OwnAsync), dal provider prima del comando: quello che ne tornerà all'IDLE per questi messaggi di
    /// folderId. Solo per la Posta in arrivo, l'unica che l'IDLE guarda; fuori da una scrittura non vale nulla.
    /// </summary>
    public static void Expect(string folderId, IEnumerable<string> ids, EchoOf kind, bool value = false)
    {
        if (writing.Value is not { } scope || !folderId.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) return;
        var now = DateTime.UtcNow;
        lock (expected)
        {
            // Quelli che non valgono più escono anche da qui: senza avvisi dell'IDLE (un server senza IDLE) nessuno li toglierebbe.
            expected.RemoveAll(e => !Alive(e, now));
            foreach (var id in ids)
                if (scope.Noted.Add((id, kind, value))) expected.Add(new Expected { Scope = scope, Id = id, Kind = kind, Value = value });
        }
    }

    public static void Expect(string folderId, IEnumerable<UniqueId> uids, EchoOf kind, bool value = false) => Expect(folderId, uids.Select(u => u.Id.ToString()), kind, value);

    /// <summary>Sotto il lucchetto: l'eco vale ancora (non arrivato, scrittura riuscita o in corso, finita da meno di OwnWindow).</summary>
    static bool Alive(Expected e, DateTime now) => !e.Consumed && !e.Scope.Failed && (e.Scope.Ended == DateTime.MaxValue || now - e.Scope.Ended < OwnWindow);

    /// <summary>Una scrittura di Missiva sull'account è in corso, o se ne aspetta ancora l'eco.</summary>
    public static bool OwnEcho(string accountId)
    {
        lock (own) if (own.GetValueOrDefault(accountId) > 0) return true;
        var now = DateTime.UtcNow;
        lock (expected) return expected.Any(e => e.Scope.AccountId == accountId && Alive(e, now));
    }

    /// <summary>
    /// L'avviso dell'IDLE è l'eco di una scrittura di Missiva: il segno che ha appena scritto su quel messaggio (UID e valore:
    /// \Seen o \Flagged), un segno qualsiasi dove ha cambiato categorie o \Deleted, un messaggio che ha tolto lei dalla Posta in
    /// arrivo. Ogni eco atteso vale per un avviso solo; una FETCH senza UID (server che non lo mettono) vale per un segno
    /// scritto qualsiasi. Tutto il resto passa: un cambio fatto sul telefono su un altro messaggio, un cambio diverso da quello
    /// atteso, un messaggio eliminato altrove. Prima, per 4 s dopo una scrittura qualsiasi, era eco ogni avviso dell'account
    /// (audit di affidabilità del 25 ottobre 2026, L-04 ed E-14). In rest l'altro segno dello stesso avviso (il letto dell'eco
    /// di un contrassegno, o il contrassegno dell'eco di un letto), da portare sulla riga perché può essere cambiato altrove;
    /// non se Missiva sta scrivendo anche quello.
    /// </summary>
    public static bool OwnEcho(string accountId, InboxNotice notice, out InboxNotice? rest)
    {
        rest = null;
        if (notice.Kind is InboxEvent.Arrived or InboxEvent.Connected) return false;
        var now = DateTime.UtcNow;
        lock (expected)
        {
            expected.RemoveAll(e => !Alive(e, now));
            var alive = expected.Where(e => e.Scope.AccountId == accountId).ToList();
            var match = notice.Kind == InboxEvent.Removed ? alive.FirstOrDefault(e => e.Kind == EchoOf.Removed)
                      : notice.Id is not { } id ? alive.FirstOrDefault(e => e.Kind is EchoOf.Seen or EchoOf.Flagged)
                      : alive.FirstOrDefault(e => e.Id == id && (e.Kind == EchoOf.Any || e.Kind == EchoOf.Seen && notice.Read == e.Value || e.Kind == EchoOf.Flagged && notice.Flagged == e.Value));
            if (match == null) return false;
            match.Consumed = true;
            if (notice.Id is { } uid && match.Kind is EchoOf.Seen or EchoOf.Flagged)
            {
                bool Writing(EchoOf kind) => alive.Any(e => e != match && e.Id == uid && (e.Kind == kind || e.Kind == EchoOf.Any));
                bool? read = match.Kind == EchoOf.Flagged && !Writing(EchoOf.Seen) ? notice.Read : null;
                bool? flagged = match.Kind == EchoOf.Seen && !Writing(EchoOf.Flagged) ? notice.Flagged : null;
                if (read != null || flagged != null) rest = notice with { Read = read, Flagged = flagged };
            }
            return true;
        }
    }

    /// <summary>
    /// Il ciclo dell'IDLE di un account: una sessione dopo l'altra (session, che si collega da sé), e dopo un errore l'attesa
    /// crescente. Finisce quando cancel scade o quando il server non ha IDLE.
    /// </summary>
    public static async Task RunAsync(string who, Func<CancellationToken, Task<bool>> session, CancellationToken cancel)
    {
        var wait = FirstRetry;
        bool announced = false;
        while (!cancel.IsCancellationRequested)
        {
            var signal = Volatile.Read(ref wake); // preso prima del tentativo: un RetryNow arrivato intanto non si perde
            try
            {
                if (!announced)
                {
                    Log.Write($"IDLE su INBOX di {who}: i nuovi messaggi arrivano subito");
                    announced = true;
                }
                lock (watching) watching.Add(who);
                if (!await session(cancel))
                {
                    lock (watching) watching.Remove(who);
                    Log.Write($"IDLE non disponibile sul server di {who}: i nuovi messaggi con il controllo di ogni due minuti");
                    return;
                }
                wait = FirstRetry; // rinnovo normale
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                lock (watching) watching.Remove(who);
                Down(who);
                // La rete è tornata mentre il tentativo falliva: il prossimo alla prima attesa, non a quella cresciuta.
                if (signal.Task.IsCompleted) wait = FirstRetry;
                Log.Write($"IDLE su INBOX di {who} interrotto ({ex.GetType().Name}: {ex.Message}); di nuovo fra {wait.TotalSeconds:0} s");
                try
                {
                    var waiting = Task.Delay(wait, cancel);
                    if (!signal.Task.IsCompleted && await Task.WhenAny(waiting, signal.Task) != waiting && !cancel.IsCancellationRequested)
                    {
                        Log.Write($"IDLE su INBOX di {who}: la rete è tornata, riprovo subito invece che fra {wait.TotalSeconds:0} s");
                        wait = FirstRetry;
                        continue;
                    }
                    await waiting;
                }
                catch (OperationCanceledException) { break; }
                wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, MaxRetry.Ticks));
            }
        }
        lock (watching) watching.Remove(who);
    }

    /// <summary>Il segnale che sveglia gli IDLE in attesa di riprovare (RetryNow): uno nuovo a ogni risveglio.</summary>
    static TaskCompletionSource wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// La rete è tornata (Windows dice che è cambiata, o un account che era non in linea ha risposto): gli IDLE caduti che
    /// aspettano di riprovare riprovano subito, e ripartono dalla prima attesa. Prima aspettavano la loro scadenza (5, 10,
    /// 20... fino a 5 minuti): con la rete tornata a 80 secondi dall'accesso a Windows il tentativo dopo era a 155, e fino
    /// ad allora i nuovi messaggi arrivavano solo con il controllo di ogni due minuti (audit del 7 ottobre, A-16).
    /// </summary>
    public static void RetryNow() =>
        Interlocked.Exchange(ref wake, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    /// <summary>Gli account (per indirizzo) con l'IDLE in sessione: i nuovi messaggi arrivano da soli.</summary>
    static readonly HashSet<string> watching = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// L'IDLE dell'account è in sessione (non caduto, non assente dal server): il controllo di ogni due minuti può
    /// diventare una rete di sicurezza (App.CheckNewAsync).
    /// </summary>
    public static bool Watching(string who)
    {
        lock (watching) return watching.Contains(who);
    }

    /// <summary>
    /// Una sessione: la Posta in arrivo in sola lettura (EXAMINE) e IDLE fino al rinnovo o alla fine; gli avvisi del server
    /// vanno a changed (sul thread della connessione: chi riceve li passa all'interfaccia). false se il server non ha IDLE.
    /// Se al rinnovo il server non risponde al DONE entro DoneLimit, la sessione si cancella (MailKit chiude la connessione)
    /// ed esce con TimeoutException: chi l'ha chiamata si ricollega (A-01).
    /// </summary>
    /// <param name="who">L'account (l'indirizzo), per il registro e per Break.</param>
    public static async Task<bool> SessionAsync(string who, ImapClient client, Action<InboxNotice> changed, CancellationToken cancel)
    {
        if (!client.Capabilities.HasFlag(ImapCapabilities.Idle)) return false;
        // Break (la ripresa del PC, un cambio di rete) chiude questa sessione: è la sua cancellazione.
        using var broken = new CancellationTokenSource();
        lock (sessions) sessions[who] = broken;
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancel, broken.Token);
        try
        {
            return await SessionAsync(who, client, changed, session.Token, cancel);
        }
        catch (OperationCanceledException) when (broken.IsCancellationRequested && !cancel.IsCancellationRequested)
        {
            // MailKit ha chiuso la connessione cancellando il comando: la sessione dopo si ricollega da capo.
            Log.Write($"IDLE su INBOX di {who}: connessione chiusa dopo la ripresa del PC o un cambio di rete, mi ricollego");
            Down(who);
            return true;
        }
        finally
        {
            lock (sessions)
                if (sessions.TryGetValue(who, out var current) && current == broken) sessions.Remove(who);
        }
    }

    /// <summary>Per account (indirizzo), la sessione dell'IDLE in corso, che Break chiude.</summary>
    static readonly Dictionary<string, CancellationTokenSource> sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Dopo la ripresa del PC o un cambio di rete (IMailProvider.DropConnections): la sessione dell'IDLE in corso
    /// dell'account si chiude subito e se ne apre un'altra su una connessione nuova. Prima chi la svegliava (RetryNow)
    /// svegliava solo le sessioni cadute in attesa di riprovare: una sessione ferma su una connessione morta restava lì fino
    /// al rinnovo (A-04). Chiudere il client di MailKit non basta: non ferma un IDLE in corso (misurato con il server finto),
    /// la cancellazione sì.
    /// </summary>
    public static void Break(string who)
    {
        CancellationTokenSource? current;
        lock (sessions) sessions.TryGetValue(who, out current);
        try { current?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Per account (indirizzo), quando l'IDLE è stato in sessione la prima volta, e da quando è giù (una caduta, una sessione
    /// chiusa da Break o dal cane da guardia): la sessione dopo, su una connessione nuova, è un ricollegamento.
    /// </summary>
    static readonly Dictionary<string, (bool Once, long DownSince)> history = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>L'IDLE dell'account è giù da adesso (InboxIdle.RunAsync dopo un errore, Break): il prossimo Connected è un ricollegamento.</summary>
    static void Down(string who)
    {
        lock (history)
        {
            var h = history.GetValueOrDefault(who);
            if (h.DownSince == 0) history[who] = (h.Once, Environment.TickCount64);
        }
    }

    /// <summary>
    /// Una sessione su una connessione nuova (la Posta in arrivo appena aperta): vero se è un ricollegamento (prima ce n'era
    /// stata un'altra, o era caduta), con una riga nel registro anche nella versione installata (A-10: prima un IDLE
    /// ricollegato da RunAsync non lasciava traccia, e non si distingueva da uno morto in silenzio).
    /// </summary>
    static bool Fresh(string who)
    {
        (bool Once, long DownSince) h;
        lock (history)
        {
            h = history.GetValueOrDefault(who);
            history[who] = (true, 0);
        }
        if (!h.Once && h.DownSince == 0) return false;
        Log.Write($"IDLE su INBOX di {who}: di nuovo in sessione su una connessione nuova{(h.DownSince > 0 ? $", giù da {(Environment.TickCount64 - h.DownSince) / 1000.0:0.0} s" : "")}: controllo l'account");
        return true;
    }

    static async Task<bool> SessionAsync(string who, ImapClient client, Action<InboxNotice> changed, CancellationToken session, CancellationToken cancel)
    {
        var inbox = client.Inbox;
        bool reconnected = false;
        if (!inbox.IsOpen)
        {
            await inbox.OpenAsync(FolderAccess.ReadOnly, session);
            reconnected = Fresh(who);
        }
        changed(new InboxNotice(InboxEvent.Connected, Reconnected: reconnected));
        int count = inbox.Count;
        void OnCount(object? sender, EventArgs e)
        {
            if (inbox.Count > count) changed(new InboxNotice(InboxEvent.Arrived, New: inbox.Count - count));
            count = inbox.Count;
        }
        // Con l'UID nella FETCH (Gmail, Dovecot) il messaggio e i suoi segni; senza, solo che qualcosa è cambiato.
        void OnFlags(object? sender, MessageFlagsChangedEventArgs e) => changed(e.UniqueId is { } uid
            ? new InboxNotice(InboxEvent.Changed, uid.Id.ToString(), e.Flags.HasFlag(MessageFlags.Seen), e.Flags.HasFlag(MessageFlags.Flagged))
            : new InboxNotice(InboxEvent.Changed));
        void OnExpunged(object? sender, MessageEventArgs e) => changed(new InboxNotice(InboxEvent.Removed));
        inbox.CountChanged += OnCount;
        inbox.MessageFlagsChanged += OnFlags;
        inbox.MessageExpunged += OnExpunged;
        try
        {
            using var done = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            // Il cane da guardia: dal DONE, DoneLimit per la risposta; poi la sessione si cancella (il secondo token di
            // IdleAsync), e MailKit chiude la connessione in pochi millisecondi (sonda arrivo-done: 8 ms).
            using var stuck = CancellationTokenSource.CreateLinkedTokenSource(session);
            var limit = DoneLimit;
            using var watch = done.Token.Register(() =>
            {
                try { stuck.CancelAfter(limit); } catch (ObjectDisposedException) { }
            });
            done.CancelAfter(Renew);
            // Il DONE lo manda MailKit quando done scade (rinnovo o fine): sulla connessione non passa altro prima.
            try
            {
                await client.IdleAsync(done.Token, stuck.Token);
            }
            catch (OperationCanceledException) when (stuck.IsCancellationRequested && !session.IsCancellationRequested)
            {
                Log.Write($"IDLE su INBOX di {who}: il server non ha risposto al DONE del rinnovo in {limit.TotalSeconds:0} s, chiudo la connessione e mi ricollego");
                throw new TimeoutException($"il server non ha risposto al DONE dell'IDLE in {limit.TotalSeconds:0} s");
            }
        }
        finally
        {
            inbox.CountChanged -= OnCount;
            inbox.MessageFlagsChanged -= OnFlags;
            inbox.MessageExpunged -= OnExpunged;
        }
        return true;
    }
}
