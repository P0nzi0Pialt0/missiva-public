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
public sealed record InboxNotice(InboxEvent Kind, string? Id = null, bool? Read = null, bool? Flagged = null);

/// <summary>
/// I cambiamenti della Posta in arrivo di Graph dall'ultima volta (query delta): i messaggi nuovi o cambiati (letti altrove)
/// con il loro stato di lettura, e quelli usciti (eliminati o spostati altrove).
/// </summary>
public sealed class InboxChanges
{
    public List<(string Id, bool Read)> Seen { get; } = new();
    public List<string> Removed { get; } = new();
    public bool Any => Seen.Count + Removed.Count > 0;
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

    static readonly Dictionary<string, (long Changes, int Writing, DateTime Ended)> states = new();

    /// <summary>Una scrittura di Missiva sull'account è finita, riuscita o no (sul thread di chi l'ha fatta).</summary>
    public static event Action<string>? Written;

    static void Change(string accountId, int writing, bool ended = false)
    {
        lock (states)
        {
            var s = states.GetValueOrDefault(accountId);
            states[accountId] = (s.Changes + 1, Math.Max(0, s.Writing + writing), ended ? DateTime.UtcNow : s.Ended);
        }
    }

    /// <summary>I non letti della Posta in arrivo cambiati qui: un messaggio letto nel riquadro, segnato a mano, arrivato.</summary>
    public static void Edited(string accountId) => Change(accountId, 0);

    /// <summary>Una scrittura di Missiva sulla casella che può cambiare i non letti (letto, da leggere, elimina, sposta, Posponi).</summary>
    public static async Task<T> WriteAsync<T>(string accountId, Func<Task<T>> write)
    {
        Change(accountId, 1);
        try
        {
            return await write();
        }
        finally
        {
            Change(accountId, -1, ended: true);
            Written?.Invoke(accountId);
        }
    }

    public static Task WriteAsync(string accountId, Func<Task> write) => WriteAsync(accountId, async () => { await write(); return true; });

    /// <summary>
    /// Da prendere prima di chiedere i non letti al server. null se adesso non è il momento: una scrittura di Missiva è in
    /// corso o appena finita, e il server risponderebbe con il numero di prima.
    /// </summary>
    public static long? Ticket(string accountId)
    {
        lock (states)
        {
            var s = states.GetValueOrDefault(accountId);
            return s.Writing > 0 || DateTime.UtcNow - s.Ended < Settle ? null : s.Changes;
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
/// lettura), rinnovata ogni 9 minuti come nell'esempio di MailKit per Gmail (i server chiudono un IDLE dopo circa 30), e
/// ripresa dopo una caduta con attese crescenti fino a 5 minuti. Quando arriva EXISTS (un messaggio nuovo), FETCH con i
/// segni (letto altrove) o EXPUNGE (eliminato o spostato altrove), chi l'ha avviata controlla quell'account. Sulla
/// connessione dell'IDLE non passa altro: nessun comando interrompe l'IDLE senza DONE. I server senza IDLE restano con il
/// controllo di ogni due minuti.
/// </summary>
public static class InboxIdle
{
    /// <summary>Ogni quanto l'IDLE si rinnova (DONE e di nuovo IDLE).</summary>
    public static TimeSpan Renew = TimeSpan.FromMinutes(9);
    /// <summary>La prima attesa dopo una caduta, poi doppia a ogni tentativo, fino a MaxRetry.</summary>
    public static TimeSpan FirstRetry = TimeSpan.FromSeconds(5);
    public static TimeSpan MaxRetry = TimeSpan.FromMinutes(5);

    // ---------------------------------------------------------------- l'eco delle azioni di Missiva

    /// <summary>Quanto dopo una scrittura di Missiva gli EXPUNGE e le FETCH dei segni che arrivano all'IDLE sono il suo eco.</summary>
    public static TimeSpan OwnWindow = TimeSpan.FromSeconds(4);

    /// <summary>Per account, le scritture di Missiva in corso e quando è finita l'ultima.</summary>
    static readonly Dictionary<string, (int Running, DateTime Ended)> own = new();

    /// <summary>
    /// Una scrittura di Missiva sulla casella (Elimina, Archivia, Sposta, letto, contrassegno, Posponi...), fatta dal provider:
    /// il server manda l'EXPUNGE o la FETCH anche alla connessione dell'IDLE, e ogni Canc rileggeva la Posta in arrivo e rifaceva
    /// l'elenco. Si annota qui, e OwnEcho dice di ignorare quegli avvisi.
    /// </summary>
    /// <param name="affectsUnread">
    /// Falso per le scritture che non cambiano i non letti (contrassegno, categoria): l'eco all'IDLE c'è lo stesso, ma i
    /// conteggi del server chiesti intanto valgono e alla fine non si riconta. Prima ogni contrassegno e ogni categoria
    /// facevano partire un riconteggio un secondo dopo (NOOP e SEARCH sulla connessione in sottofondo; audit del 7 ottobre, B-17).
    /// </param>
    public static async Task<T> OwnAsync<T>(string accountId, Func<Task<T>> write, bool affectsUnread = true)
    {
        lock (own)
        {
            var s = own.GetValueOrDefault(accountId);
            own[accountId] = (s.Running + 1, s.Ended);
        }
        try
        {
            if (!affectsUnread) return await write();
            return await UnreadSync.WriteAsync(accountId, write); // i non letti contati intanto dal server non valgono
        }
        finally
        {
            lock (own) own[accountId] = (Math.Max(0, own.GetValueOrDefault(accountId).Running - 1), DateTime.UtcNow);
        }
    }

    public static Task OwnAsync(string accountId, Func<Task> write, bool affectsUnread = true) => OwnAsync(accountId, async () => { await write(); return true; }, affectsUnread);

    /// <summary>Una scrittura di Missiva sull'account è in corso o è finita da meno di OwnWindow: i segni e gli usciti sono il suo eco.</summary>
    public static bool OwnEcho(string accountId)
    {
        lock (own) return own.TryGetValue(accountId, out var s) && (s.Running > 0 || DateTime.UtcNow - s.Ended < OwnWindow);
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
    /// </summary>
    public static async Task<bool> SessionAsync(ImapClient client, Action<InboxNotice> changed, CancellationToken cancel)
    {
        if (!client.Capabilities.HasFlag(ImapCapabilities.Idle)) return false;
        var inbox = client.Inbox;
        if (!inbox.IsOpen) await inbox.OpenAsync(FolderAccess.ReadOnly, cancel);
        changed(new InboxNotice(InboxEvent.Connected));
        int count = inbox.Count;
        void OnCount(object? sender, EventArgs e)
        {
            if (inbox.Count > count) changed(new InboxNotice(InboxEvent.Arrived));
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
            done.CancelAfter(Renew);
            // Il DONE lo manda MailKit quando done scade (rinnovo o fine): sulla connessione non passa altro prima.
            await client.IdleAsync(done.Token, cancel);
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
