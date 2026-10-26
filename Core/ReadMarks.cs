namespace Missiva;

/// <summary>
/// I segni di letto e da leggere fatti in Missiva (il riquadro che segna letto, Ctrl+Q, Ctrl+U, la busta della riga, la
/// barra della finestra del messaggio) e la loro strada verso il server. Audit di affidabilità del 25 ottobre 2026, letti e
/// non letti (docs/audit/affidabilita-letti.md):
/// - L-01: un segno che non arriva al server per un errore che passa (un tempo scaduto, la connessione morta dopo la
///   sospensione del PC o caduta due volte, Outlook che risponde 429 o 5xx, un accesso da rifare: ActionQueue.Passing) va
///   nella coda delle azioni come senza rete, e si ripete. Prima andava in coda solo con la rete che manca (nove errori di
///   Windows, Network.IsOffline): con tutti gli altri il messaggio restava letto in Missiva e da leggere sul server, e il
///   numero dei non letti risaliva con le righe tutte lette. Con un rifiuto del server (un NO di IMAP, un 400 o un 403 di
///   Graph) chi ha fatto il segno lo rimette com'era (Outcome.Refused, con i messaggi da rimettere in Result.Undo).
/// - L-03: un segno riuscito in linea toglie dalla coda i segni di prima dello stesso messaggio (ActionQueue.Supersede):
///   prima la coda li ripeteva al ritorno della rete, o al riavvio, e rimetteva lo stato vecchio. Vale il segno più nuovo:
///   ogni segno ha il suo numero (Begin), e un segno superato da uno fatto dopo non va in coda, non toglie dalla coda quello
///   dopo e non si rimette indietro (End).
/// - L-05: finché la scrittura non è arrivata al server (e per Settle dopo), le righe di quel messaggio non prendono lo stato
///   di lettura da una lettura del server chiesta prima: la prima pagina dell'elenco, la sua rilettura, la vista di tutti gli
///   account, la query delta di Graph, un avviso dell'IDLE (Apply, Holding), come UnreadSync per i numeri.
/// - L-07: le righe dello stesso messaggio si riconoscono per messaggio, non per solo identificativo (Same, Matches).
/// - L-08: i segni ancora in viaggio quando Missiva esce vanno nella coda, che è su disco (QueueUnfinished).
/// </summary>
public static class ReadMarks
{
    public enum Outcome { Done, Queued, Refused }

    /// <param name="Undo">Con Refused, i messaggi il cui segno va rimesso com'era (riga e numero).</param>
    public sealed record Result(Outcome Outcome, Exception? Error = null, IReadOnlyList<MessageSummary>? Undo = null);

    /// <summary>
    /// Un segno è andato in coda per un errore che passa, non per la rete che manca (l'account): la coda si ripete poco dopo,
    /// senza aspettare il controllo di ogni due minuti, perché intanto il conteggio del server, che conta ancora quel
    /// messaggio, fa risalire il numero dei non letti (MainWindow.ReplayLaterAsync).
    /// </summary>
    public static event Action<string>? QueuedAfterError;

    /// <summary>
    /// Il segno sul server, con la sua strada se non ci arriva: con l'account già non in linea subito in coda; con la rete
    /// che manca o un errore che passa in coda; con un altro errore (il server l'ha rifiutato) Refused. Un .eml non è nella
    /// casella: i suoi segni non vanno mai in coda. Le righe e i numeri li ha già cambiati chi chiama.
    /// </summary>
    public static async Task<Result> WriteAsync(IMailProvider p, IReadOnlyList<MessageSummary> messages, bool read)
    {
        var account = p.Account;
        bool queues = p is not EmlProvider;
        var kind = read ? QueuedKind.Read : QueuedKind.Unread;
        long stamp = Begin(account, messages, read, queues);
        if (queues && Network.KnownOffline(account))
        {
            Queue(account, kind, stamp);
            return new Result(Outcome.Queued);
        }
        try
        {
            await p.SetReadAsync(messages, read);
            // I segni di prima ancora in coda (fatti senza rete, prima di un riavvio) ripetuti dopo rimetterebbero lo stato
            // vecchio: escono. Non quelli fatti dopo questo, se intanto ce ne sono.
            var newest = End(stamp, keep: true);
            if (queues && newest.Count > 0) ActionQueue.Supersede(account, newest);
            return new Result(Outcome.Done);
        }
        catch (Exception ex) when (queues && Network.IsOffline(ex))
        {
            Network.Note(account, ex);
            Queue(account, kind, stamp);
            return new Result(Outcome.Queued, ex);
        }
        catch (Exception ex) when (queues && ActionQueue.Passing(ex))
        {
            Log.Write($"stato di lettura di {account.Email} non arrivato al server ({ex.GetType().Name}: {Log.WithoutAddresses(ex.Message)}): " +
                      "il segno va in coda e si ripete al prossimo controllo riuscito");
            Queue(account, kind, stamp);
            QueuedAfterError?.Invoke(account.Id);
            return new Result(Outcome.Queued, ex);
        }
        catch (Exception ex)
        {
            return new Result(Outcome.Refused, ex, End(stamp, keep: false));
        }
    }

    /// <summary>In coda i messaggi del segno che è ancora il più nuovo: per gli altri vale quello fatto dopo, in linea o in coda.</summary>
    static void Queue(MailAccount account, QueuedKind kind, long stamp)
    {
        var newest = End(stamp, keep: true);
        if (newest.Count > 0) ActionQueue.Add(account, newest, kind);
    }

    // ---------------------------------------------------------------- i segni fatti, per messaggio

    sealed class Mark
    {
        public required MailAccount Account { get; init; }
        public required MessageSummary Message { get; init; }
        public bool Read { get; init; }
        /// <summary>Il segno può andare in coda (non quello di un .eml, che non sta nella casella).</summary>
        public bool Queues { get; init; }
        public long Stamp { get; init; }
        /// <summary>Quando la scrittura è finita (riuscita o messa in coda); MaxValue finché è in viaggio.</summary>
        public DateTime Done { get; set; } = DateTime.MaxValue;
    }

    /// <summary>Sotto il suo lucchetto: i segni degli ultimi minuti, dal più vecchio.</summary>
    static readonly List<Mark> marks = new();
    static long stamps;
    /// <summary>Per quanto si tiene un segno finito: chi lo guarda dopo non ha più bisogno di lui.</summary>
    static readonly TimeSpan Keep = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Lo stesso messaggio: con Microsoft l'id (unico nella casella); con Gmail e IMAP cartella e UID, e con Gmail anche
    /// l'X-GM-MSGID, che è lo stesso in tutte le cartelle (un messaggio della conversazione viene da "Tutti i messaggi" con un
    /// UID di quella cartella). Con IMAP il Message-Id no: due copie dello stesso messaggio in due cartelle sono due messaggi.
    /// </summary>
    public static bool Same(AccountKind kind, MessageSummary a, MessageSummary b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (kind == AccountKind.Microsoft) return a.Id == b.Id;
        if (a.Id == b.Id && a.FolderId == b.FolderId) return true;
        return kind == AccountKind.Gmail && a.StableId.Length > 0 && a.StableId == b.StableId;
    }

    /// <summary>
    /// Le righe di questi messaggi (Same), con una ricerca sola anche per molti messaggi: per Microsoft gli id, per Gmail e IMAP
    /// cartella e UID, per Gmail anche gli X-GM-MSGID. Prima le righe si cercavano per solo identificativo, e con Gmail il
    /// messaggio della conversazione (da "Tutti i messaggi") rendeva letta la riga della Posta in arrivo con lo stesso numero,
    /// un altro messaggio (audit di affidabilità del 25 ottobre 2026, L-07).
    /// </summary>
    public static Func<MessageSummary, bool> Matches(AccountKind kind, IEnumerable<MessageSummary> messages)
    {
        if (kind == AccountKind.Microsoft)
        {
            var ids = messages.Select(m => m.Id).ToHashSet();
            return m => ids.Contains(m.Id);
        }
        var keys = messages.Select(m => (m.FolderId, m.Id)).ToHashSet();
        var stable = kind == AccountKind.Gmail ? messages.Where(m => m.StableId.Length > 0).Select(m => m.StableId).ToHashSet() : null;
        return m => keys.Contains((m.FolderId, m.Id)) || stable is { Count: > 0 } && m.StableId.Length > 0 && stable.Contains(m.StableId);
    }

    static long Begin(MailAccount account, IEnumerable<MessageSummary> messages, bool read, bool queues)
    {
        lock (marks)
        {
            marks.RemoveAll(m => m.Done != DateTime.MaxValue && DateTime.UtcNow - m.Done > Keep);
            long stamp = ++stamps;
            foreach (var m in messages) marks.Add(new Mark { Account = account, Message = m, Read = read, Stamp = stamp, Queues = queues });
            return stamp;
        }
    }

    /// <summary>
    /// La scrittura del segno è finita: riuscita o in coda (keep), o rifiutata (il segno esce). I messaggi per cui questo è
    /// ancora il segno più nuovo: per gli altri ne è arrivato uno dopo, che decide lui.
    /// </summary>
    static List<MessageSummary> End(long stamp, bool keep)
    {
        lock (marks)
        {
            var mine = marks.Where(m => m.Stamp == stamp).ToList();
            var newest = mine.Where(m => !marks.Any(o => o.Stamp > stamp && o.Account.Id == m.Account.Id && Same(m.Account.Kind, o.Message, m.Message)))
                             .Select(m => m.Message).ToList();
            if (keep) foreach (var m in mine) m.Done = DateTime.UtcNow;
            else marks.RemoveAll(m => m.Stamp == stamp);
            return newest;
        }
    }

    // ---------------------------------------------------------------- lo stato del server chiesto prima (L-05)

    /// <summary>
    /// Quanto dopo la fine della scrittura una lettura del server può dire ancora lo stato di prima (Graph risponde al PATCH
    /// un attimo prima di averlo dappertutto): fino ad allora vale il segno di qui. Come UnreadSync.Settle per i numeri.
    /// </summary>
    public static TimeSpan Settle = TimeSpan.FromSeconds(1);

    /// <summary>Il biglietto di una lettura del server (pagina dell'elenco, sua rilettura, query delta): l'istante in cui parte.</summary>
    public static DateTime Asked() => DateTime.UtcNow;

    /// <summary>
    /// Il segno di qui che vale per il messaggio contro una lettura del server partita ad asked: quello più nuovo, se la
    /// lettura è partita prima che la sua scrittura finisse (più Settle) o se la scrittura è ancora in viaggio; altrimenti
    /// null, e vale il server.
    /// </summary>
    public static bool? Holding(string accountId, MessageSummary m, DateTime asked)
    {
        lock (marks)
        {
            Mark? newest = null;
            foreach (var mark in marks)
                if (mark.Account.Id == accountId && (newest == null || mark.Stamp > newest.Stamp) && Same(mark.Account.Kind, mark.Message, m)) newest = mark;
            return newest != null && (newest.Done == DateTime.MaxValue || asked < newest.Done + Settle) ? newest.Read : null;
        }
    }

    /// <summary>
    /// I messaggi arrivati da una lettura del server partita ad asked (Asked): quelli segnati qui dopo, o con la scrittura
    /// ancora in viaggio, prendono il segno di qui. Prima le righe prendevano sempre lo stato del server, e il messaggio
    /// appena aperto mentre la prima pagina era in viaggio tornava in grassetto (con IMAP fino alla rilettura), un "da
    /// leggere" appena fatto tornava letto (audit di affidabilità del 25 ottobre 2026, L-05). Quanti ne ha cambiati.
    /// </summary>
    public static int Apply(string accountId, IEnumerable<MessageSummary> fromServer, DateTime asked)
    {
        lock (marks) if (marks.Count == 0) return 0;
        int changed = 0;
        foreach (var m in fromServer)
            if (Holding(accountId, m, asked) is { } read && m.IsRead != read)
            {
                m.IsRead = read;
                changed++;
            }
        return changed;
    }

    /// <summary>
    /// Missiva esce (App.OnExit, e la fine della sessione di Windows, che può chiudere il processo senza OnExit): i segni
    /// ancora in viaggio vanno nella coda delle azioni, che è su disco, e ripartono al prossimo avvio. Se la scrittura arriva
    /// lo stesso prima della fine del processo, il segno esce dalla coda (Supersede); se non arriva, la coda la rifà (un
    /// segno ripetuto non cambia nulla). Prima nessuno li aspettava e non erano in coda: un letto fatto un attimo prima di
    /// uscire, o partito chiudendo la finestra con "quando la selezione cambia", si perdeva con una connessione lenta o ferma
    /// (audit di affidabilità del 25 ottobre 2026, L-08). Quanti segni sono andati in coda.
    /// </summary>
    public static int QueueUnfinished()
    {
        List<(MailAccount Account, bool Read, List<MessageSummary> Messages)> open;
        lock (marks)
        {
            open = marks.Where(m => m.Done == DateTime.MaxValue && m.Queues
                                    && !marks.Any(o => o.Stamp > m.Stamp && o.Account.Id == m.Account.Id && Same(m.Account.Kind, o.Message, m.Message)))
                        .GroupBy(m => (m.Account, m.Read))
                        .Select(g => (g.Key.Account, g.Key.Read, g.Select(m => m.Message).ToList())).ToList();
        }
        foreach (var (account, read, messages) in open)
        {
            Log.Write($"uscita: {messages.Count} segni di lettura di {account.Email} ancora in viaggio, in coda per il prossimo avvio");
            ActionQueue.Add(account, messages, read ? QueuedKind.Read : QueuedKind.Unread);
        }
        return open.Sum(o => o.Messages.Count);
    }

#if DEBUG
    /// <summary>Prove: si riparte senza segni (come dopo un riavvio).</summary>
    internal static void Reset() { lock (marks) marks.Clear(); }
#endif
}
