using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Missiva;

/// <summary>Che cosa fare su un messaggio quando torna la rete.</summary>
public enum QueuedKind { Move, Read, Unread, Flag, Unflag }

/// <summary>
/// Un'azione fatta senza rete (Elimina nel Cestino, Archivia, Sposta, segna come letto o da leggere, contrassegna), che
/// aspetta la rete: l'account, il messaggio (id, cartella, la chiave che non cambia) e, per gli spostamenti, la cartella di
/// arrivo. Mai oggetto, mittente o testo: nel file ci sono solo identificativi, e il file è cifrato con DPAPI.
/// </summary>
public sealed class QueuedAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "";
    public QueuedKind Kind { get; set; }
    public string MessageId { get; set; } = "";
    public string FolderId { get; set; } = "";
    public string StableId { get; set; } = "";
    public string TargetFolderId { get; set; } = "";
    public DateTime Queued { get; set; } = DateTime.Now;

    /// <summary>Il messaggio come lo vuole il provider: identificativo, cartella e chiave che non cambia.</summary>
    public MessageSummary Message() => new() { Id = MessageId, FolderId = FolderId, StableId = StableId };

    /// <summary>Due azioni consecutive che partono con una richiesta sola (lo stesso tipo, dalla stessa cartella alla stessa).</summary>
    public bool SameAs(QueuedAction other) => Kind == other.Kind && FolderId == other.FolderId && TargetFolderId == other.TargetFolderId;
}

[JsonSerializable(typeof(List<QueuedAction>))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
sealed partial class QueueJson : JsonSerializerContext;

/// <summary>
/// Lavoro senza rete, come l'Outlook classico e il nuovo Outlook: le azioni fatte senza rete vanno in coda (coda.dat nella
/// cartella dei dati, tutto il file cifrato con DPAPI), la riga cambia subito, sotto l'account "N azioni in attesa", e al
/// ritorno della rete ogni azione si ripete una volta sola: la ripetizione che la prende la segna come presa (running, in
/// memoria: se due ritorni della rete si accavallano, il secondo non la prende), e l'azione esce dalla coda e dal file solo
/// quando è riuscita, non è più possibile o il server l'ha rifiutata. Se Missiva esce o cade a metà, quelle non finite
/// sono ancora nel file e ripartono al prossimo avvio (audit del 7 ottobre, B-13: prima uscivano tutte dal file prima di
/// partire, e una chiusura a metà le perdeva). Un messaggio che nel frattempo non c'è più (eliminato o spostato dal
/// telefono, dal sito) si dice, e l'azione esce.
/// </summary>
public static class ActionQueue
{
    static string FilePath => Path.Combine(Store.Dir, "coda.dat");
    static readonly object gate = new();
    static List<QueuedAction>? items;
    /// <summary>
    /// Sotto gate. Le azioni (Id) che una ripetizione ha preso e non ha ancora finito: restano nella coda e nel file, ma
    /// un'altra ripetizione non le prende e Annulla non le toglie più. Solo in memoria: dopo un riavvio nessuna è presa.
    /// </summary>
    static readonly HashSet<string> running = new();

    /// <summary>La coda è cambiata (azioni entrate, uscite, ripartite): i conteggi sotto gli account. Da qualunque thread.</summary>
    public static event Action? Changed;

    /// <summary>Sotto gate. La coda, letta dal file la prima volta (anche quella lasciata da una Missiva chiusa senza rete).</summary>
    static List<QueuedAction> Items
    {
        get
        {
            lock (gate)
            {
                if (items != null) return items;
                try
                {
                    items = File.Exists(FilePath)
                        ? JsonSerializer.Deserialize(SafeFile.Unprotect(File.ReadAllBytes(FilePath)), QueueJson.Default.ListQueuedAction) ?? new()
                        : new();
                }
                catch (Exception ex)
                {
                    // Da parte con la data nel nome, non sovrascritta dalla prossima azione in coda (audit, F-44).
                    Log.Error($"coda delle azioni non letta, file messo da parte: {SafeFile.SetAside(FilePath) ?? "non riuscito"}", ex);
                    items = new();
                }
                return items;
            }
        }
    }

    /// <summary>Sotto gate. Senza azioni il file sparisce; altrimenti si riscrive intero, cifrato, passando da un file temporaneo.</summary>
    static void Save()
    {
        try
        {
            var list = Items;
            if (list.Count == 0)
            {
                File.Delete(FilePath);
                return;
            }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(list, QueueJson.Default.ListQueuedAction);
            SafeFile.Write(FilePath, SafeFile.Protect(bytes));
        }
        catch (Exception ex)
        {
            Log.Error("coda delle azioni non salvata", ex);
        }
    }

    /// <summary>Quante azioni aspettano per l'account.</summary>
    public static int Count(string accountId)
    {
        lock (gate) return Items.Count(a => a.AccountId == accountId);
    }

    /// <summary>Le azioni sui messaggi, in coda (una per messaggio), dopo quelle che c'erano già.</summary>
    public static void Add(MailAccount account, IReadOnlyList<MessageSummary> messages, QueuedKind kind, string targetFolderId = "")
    {
        if (messages.Count == 0) return;
        lock (gate)
        {
            foreach (var m in messages)
            {
                // Lo stesso segno chiesto due volte vale una volta; il contrario (letto dopo da leggere) prende il suo posto.
                if (kind is not QueuedKind.Move)
                    Items.RemoveAll(a => a.AccountId == account.Id && a.MessageId == m.Id && a.FolderId == m.FolderId && a.Kind is not QueuedKind.Move && Pair(a.Kind) == Pair(kind));
                Items.Add(new QueuedAction { AccountId = account.Id, Kind = kind, MessageId = m.Id, FolderId = m.FolderId, StableId = m.StableId, TargetFolderId = targetFolderId });
            }
            Save();
        }
        Log.Write($"in coda: {messages.Count} azioni ({Describe(kind)}) di {account.Email}, partiranno quando torna la rete");
        Changed?.Invoke();
    }

    static int Pair(QueuedKind kind) => kind is QueuedKind.Read or QueuedKind.Unread ? 1 : 2;

    static string Describe(QueuedKind kind) => kind switch
    {
        QueuedKind.Move => "spostamento",
        QueuedKind.Read => "segna come letto",
        QueuedKind.Unread => "segna come da leggere",
        QueuedKind.Flag => "contrassegna",
        _ => "togli il contrassegno",
    };

    /// <summary>
    /// Annulla dopo Elimina, Archivia o Sposta fatti senza rete: gli spostamenti di quei messaggi escono dalla coda. Quante
    /// ne sono uscite (meno di quelle chieste se intanto la rete è tornata e sono già partite).
    /// </summary>
    public static int Remove(MailAccount account, IEnumerable<MessageSummary> messages, string targetFolderId) => Take(account, messages, targetFolderId).Count;

    /// <summary>
    /// Come Remove, ma dice quali: i messaggi il cui spostamento è uscito dalla coda prima di partire. Gli altri sono già
    /// partiti (o stanno partendo) al ritorno della rete. Annulla rimette nell'elenco solo i primi: prima, se ne usciva solo
    /// una parte, li trattava tutti come già spostati, anche quelli rimasti nella loro cartella (audit dell'11 ottobre, C-55).
    /// </summary>
    public static List<MessageSummary> Take(MailAccount account, IEnumerable<MessageSummary> messages, string targetFolderId)
    {
        var asked = messages.GroupBy(m => (m.FolderId, m.Id)).ToDictionary(g => g.Key, g => g.First());
        lock (gate)
        {
            var taken = Items.Where(a => a.AccountId == account.Id && a.Kind == QueuedKind.Move && a.TargetFolderId == targetFolderId && asked.ContainsKey((a.FolderId, a.MessageId))
                                         && !running.Contains(a.Id)).ToList();
            if (taken.Count == 0) return new();
            Items.RemoveAll(taken.Contains);
            Save();
            Log.Write($"coda di {account.Email}: {taken.Count} spostamenti annullati prima di partire");
            Changed?.Invoke();
            return taken.Select(a => asked[(a.FolderId, a.MessageId)]).ToList();
        }
    }

    /// <summary>Account rimosso: le sue azioni escono.</summary>
    public static void Forget(string accountId)
    {
        lock (gate)
        {
            if (Items.RemoveAll(a => a.AccountId == accountId) == 0) return;
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Le azioni in coda valgono anche per gli elenchi riletti prima che la coda riparta (dalla copia locale, o dal server
    /// appena tornata la rete): i messaggi spostati o eliminati non ci sono, letto e contrassegno sono quelli scelti.
    /// </summary>
    public static void Apply(string accountId, List<MessageSummary> messages)
    {
        List<QueuedAction> mine;
        lock (gate)
        {
            if (Items.Count == 0) return;
            mine = Items.Where(a => a.AccountId == accountId).ToList();
        }
        if (mine.Count == 0 || messages.Count == 0) return;
        var moved = mine.Where(a => a.Kind == QueuedKind.Move).Select(a => (a.FolderId, a.MessageId)).ToHashSet();
        messages.RemoveAll(m => moved.Contains((m.FolderId, m.Id)));
        foreach (var a in mine.Where(a => a.Kind != QueuedKind.Move))
            foreach (var m in messages.Where(m => m.Id == a.MessageId && m.FolderId == a.FolderId))
            {
                if (a.Kind is QueuedKind.Read or QueuedKind.Unread) m.IsRead = a.Kind == QueuedKind.Read;
                else m.IsFlagged = a.Kind == QueuedKind.Flag;
            }
    }

    /// <summary>
    /// Quello che è successo ripetendo la coda di un account: fatte, non più possibili (i messaggi non ci sono più), ancora in
    /// attesa, rifiutate dal server; Reason è il motivo vero dell'attesa (null: la rete), Refused quello dell'ultimo rifiuto.
    /// </summary>
    public sealed record Replay(int Done, int Gone, int Waiting, int Failed = 0, string? Reason = null, string? Refused = null);

    /// <summary>
    /// Il messaggio non c'è più: Graph risponde 404, o la cartella in cui stava non esiste più. Solo questi (e i messaggi che
    /// StillThereAsync non trova) escono dalla coda come "eliminati o spostati altrove".
    /// </summary>
    static bool Gone(Exception ex) => GraphProvider.IsNotFound(ex) || ex is MailKit.FolderNotFoundException;

    /// <summary>
    /// Un errore che passa, come la rete che manca: l'accesso da rifare (anche annullato nella finestra di Microsoft, o
    /// invalid_grant di Google), la password rifiutata, il consenso, Graph che risponde 429 o 5xx, un server che non risponde
    /// in tempo o chiude la connessione. L'azione non è partita e aspetta il prossimo ritorno, con il motivo vero.
    /// </summary>
    static bool Passing(Exception ex) =>
        MailException.FixOf(ex) != AccountFix.None || ex is Microsoft.Identity.Client.MsalException ||
        ex is MailException { Status: 429 or >= 500 } || ex is TimeoutException or TaskCanceledException or IOException or System.Net.Sockets.SocketException ||
        ex is MailKit.ServiceNotConnectedException or MailKit.Net.Imap.ImapProtocolException || ex is MailException { InnerException: { } inner } && Passing(inner);

    /// <summary>
    /// La coda dell'account ripetuta al ritorno della rete, nell'ordine in cui le azioni erano state fatte. La ripetizione
    /// prende tutte quelle dell'account (running): una seconda ripetizione (un altro ritorno della rete, un controllo che
    /// arriva insieme) non le ripete. Restano nella coda e nel file finché non sono finite: ogni gruppo esce quando è
    /// riuscito, non è più possibile o è stato rifiutato, così una Missiva chiusa o caduta a metà ritrova al riavvio quelle
    /// non finite (quella in viaggio in quel momento si ripete: un segno due volte non cambia nulla, uno spostamento già
    /// fatto risulta "non più possibile"). Gmail e IMAP dicono prima quali messaggi ci sono ancora (StillThereAsync), e le
    /// azioni consecutive uguali partono insieme (un solo UID MOVE per cartella); Graph una per messaggio, perché un 404 dica
    /// quale non c'è più. Se la rete manca ancora, o l'errore passa (Passing: accesso da rifare, 429, 5xx), l'azione e le
    /// seguenti restano in coda, nello stesso ordine, e si riprova al prossimo ritorno. Solo i messaggi che non ci sono più
    /// escono come "non più possibili" (Gone); un rifiuto del server esce come tale, con il suo motivo (Failed, Refused).
    /// </summary>
    /// <param name="folders">Le cartelle dell'account, per ritrovare quella di arrivo degli spostamenti.</param>
    public static async Task<Replay> ReplayAsync(IMailProvider provider, Func<Task<List<MailFolder>>> folders)
    {
        List<QueuedAction> mine;
        lock (gate)
        {
            mine = Items.Where(a => a.AccountId == provider.Account.Id && !running.Contains(a.Id)).ToList();
            if (mine.Count == 0) return new Replay(0, 0, 0);
            foreach (var a in mine) running.Add(a.Id);
        }
        try
        {
            return await ReplayAsync(provider, folders, mine);
        }
        finally
        {
            // Finite o no (la rete manca ancora, un errore che nessuno aspettava): nessuna resta presa. Quelle non finite
            // sono ancora in coda, dov'erano, e ripartono al prossimo ritorno.
            lock (gate) foreach (var a in mine) running.Remove(a.Id);
        }
    }

    static async Task<Replay> ReplayAsync(IMailProvider provider, Func<Task<List<MailFolder>>> folders, List<QueuedAction> mine)
    {
        var account = provider.Account;
        Log.Write($"coda di {account.Email}: la rete è tornata, riparto con {mine.Count} azioni");
        Replay Wait(int from, string? reason = null)
        {
            // La rete manca ancora (o l'errore passa: accesso da rifare, 429, 503): questa e le seguenti restano in coda
            // (non ne sono uscite), davanti a quelle fatte intanto, nello stesso ordine.
            Log.Write($"coda di {account.Email}: {(reason == null ? "la rete manca ancora" : "non ora (" + reason + ")")}, {mine.Count - from} azioni restano in attesa");
            return new Replay(0, 0, mine.Count - from, Reason: reason);
        }
        // Un gruppo finito (riuscito, non più possibile, rifiutato) esce dalla coda e dal file. Su un thread del pool: la
        // ripetizione parte dal thread dell'interfaccia, e la scrittura del file (DPAPI, file nuovo, Flush) non deve pesarci.
        Task FinishedAsync(List<QueuedAction> group) => Task.Run(() =>
        {
            var ids = group.Select(a => a.Id).ToHashSet();
            lock (gate)
            {
                if (Items.RemoveAll(a => ids.Contains(a.Id)) > 0) Save();
            }
            Changed?.Invoke(); // il numero sotto l'account scende mentre la coda va
        });

        var messages = mine.ToDictionary(a => a, a => a.Message());
        HashSet<MessageSummary>? there = null;
        try
        {
            if (await provider.StillThereAsync(messages.Values.ToList()) is { } found) there = found.ToHashSet();
        }
        catch (Exception ex) when (Network.IsOffline(ex))
        {
            return Wait(0);
        }
        catch (Exception ex) when (Passing(ex))
        {
            // Senza sapere quali messaggi ci sono ancora, nessuna azione esce dalla coda come "eliminata altrove".
            return Wait(0, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Write($"coda di {account.Email}: non so quali messaggi ci sono ancora ({ex.Message}), provo azione per azione");
        }

        int done = 0, gone = 0, failed = 0;
        string? refused = null;
        List<MailFolder>? known = null;
        for (int i = 0; i < mine.Count;)
        {
            int end = i + 1;
            if (there != null) while (end < mine.Count && mine[end].SameAs(mine[i])) end++;
            var group = mine.GetRange(i, end - i);
            var first = group[0];
            var live = group.Select(a => messages[a]).Where(m => there == null || there.Contains(m)).ToList();
            gone += group.Count - live.Count;
            try
            {
                if (live.Count > 0)
                {
                    switch (first.Kind)
                    {
                        case QueuedKind.Move:
                            known ??= await folders();
                            if (known.FirstOrDefault(f => f.Id == first.TargetFolderId) is not { } target)
                            {
                                // La cartella di arrivo non c'è più (eliminata o rinominata altrove).
                                gone += live.Count;
                                Log.Write($"coda di {account.Email}: la cartella di arrivo di {live.Count} spostamenti non c'è più");
                                break;
                            }
                            var moved = await provider.MoveAsync(live, target);
                            // Come dopo uno spostamento fatto subito: la copia su disco non resta alla cartella di prima, le date dei
                            // contrassegni seguono il messaggio.
                            BodyCache.Forget(account, live);
                            bool followed = false;
                            foreach (var x in moved) followed |= Flags.Follow(account, x.Original.Id, x.Original.FolderId, x.NewId, target.Id);
                            if (followed) Store.Save();
                            done += live.Count;
                            break;
                        case QueuedKind.Read or QueuedKind.Unread:
                            await provider.SetReadAsync(live, first.Kind == QueuedKind.Read);
                            done += live.Count;
                            break;
                        case QueuedKind.Flag or QueuedKind.Unflag:
                            await provider.SetFlagAsync(live, first.Kind == QueuedKind.Flag);
                            done += live.Count;
                            break;
                    }
                }
            }
            catch (Exception ex) when (Network.IsOffline(ex))
            {
                var waiting = Wait(i);
                Changed?.Invoke();
                return waiting with { Done = done, Gone = gone, Failed = failed, Refused = refused };
            }
            catch (Exception ex) when (Passing(ex))
            {
                var waiting = Wait(i, ex.Message);
                Changed?.Invoke();
                return waiting with { Done = done, Gone = gone, Failed = failed, Refused = refused };
            }
            catch (Exception ex) when (Gone(ex))
            {
                // Il messaggio non c'è più (eliminato o spostato altrove: Graph risponde 404): l'azione esce, e si dice.
                gone += live.Count;
                Log.Write($"coda di {account.Email}: {live.Count} azioni ({Describe(first.Kind)}) non più possibili, il messaggio non c'è più: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Il server rifiuta l'azione (un NO di IMAP, un 400 o un 403 di Graph): esce, e si dice il motivo vero, non
                // "eliminato altrove".
                failed += live.Count;
                refused = ex.Message;
                Log.Write($"coda di {account.Email}: {live.Count} azioni ({Describe(first.Kind)}) rifiutate dal server: {ex.Message}");
            }
            await FinishedAsync(group);
            i = end;
        }
        Changed?.Invoke();
        Log.Write($"coda di {account.Email}: {done} azioni fatte al ritorno della rete{(gone > 0 ? $", {gone} non più possibili (messaggi eliminati o spostati altrove)" : "")}" +
                  (failed > 0 ? $", {failed} rifiutate" : ""));
        return new Replay(done, gone, 0, failed, Refused: refused);
    }

#if DEBUG
    /// <summary>Prove: si rilegge il file (o si parte da capo).</summary>
    internal static void Reset() { lock (gate) { items = null; running.Clear(); } }
#endif
}

/// <summary>
/// La Posta in uscita di Missiva, come quella dell'Outlook classico: i messaggi inviati senza rete aspettano qui (in memoria,
/// con la loro finestra di scrittura nascosta, che Annulla riapre) e partono al ritorno della rete, anche con Missiva
/// nell'area di notifica. Ognuno una volta sola: esce dalla Posta in uscita prima di partire, e ci torna solo se la rete
/// manca ancora. Non va su disco: uscendo da Missiva quelli non partiti si riaprono, con Salva bozza, Scarta e Annulla.
/// </summary>
public static class Outbox
{
    public sealed class Entry
    {
        public required string AccountId { get; init; }
        public required OutgoingMessage Message { get; init; }
        /// <summary>Partito (con il provider che l'ha mandato): la finestra di scrittura si chiude come dopo Invia.</summary>
        public Action<IMailProvider>? Sent { get; init; }
        /// <summary>Non partito per un errore che non è la rete: la finestra si riapre con il motivo.</summary>
        public Action<Exception>? Failed { get; init; }
    }

    static readonly List<Entry> entries = new();

    /// <summary>Cambiata (entrato, partito, annullato): la barra della finestra principale e i conteggi sotto gli account.</summary>
    public static event Action? Changed;

    public static void Add(Entry entry, string account)
    {
        lock (entries) entries.Add(entry);
        Log.Write($"Posta in uscita: {Log.Private(entry.Message.Subject)} di {account}, parte quando torna la rete");
        Changed?.Invoke();
    }

    public static int Count(string accountId)
    {
        lock (entries) return entries.Count(e => e.AccountId == accountId);
    }

    /// <summary>
    /// Account rimosso: i suoi messaggi escono dalla Posta in uscita (le loro finestre le riapre chi chiama, prima). Prima
    /// restavano: la barra continuava a dire "partirà quando torna la rete", e non sarebbero partiti mai (audit dell'11
    /// ottobre, C-54). Quanti ne sono usciti.
    /// </summary>
    public static int Forget(string accountId)
    {
        int removed;
        lock (entries) removed = entries.RemoveAll(e => e.AccountId == accountId);
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    /// <summary>Annulla, dalla barra: il messaggio esce dalla Posta in uscita (la finestra la riapre chi chiama). Falso se è già partito.</summary>
    public static bool Remove(Entry entry)
    {
        bool removed;
        lock (entries) removed = entries.Remove(entry);
        if (removed) Changed?.Invoke();
        return removed;
    }

    /// <summary>I messaggi dell'account partono, uno alla volta, con il provider di adesso; quanti sono partiti.</summary>
    public static async Task<int> SendAsync(IMailProvider provider)
    {
        List<Entry> mine;
        lock (entries)
        {
            mine = entries.Where(e => e.AccountId == provider.Account.Id).ToList();
            if (mine.Count == 0) return 0;
            entries.RemoveAll(mine.Contains);
        }
        int sent = 0;
        try
        {
            for (int i = 0; i < mine.Count; i++)
            {
                var entry = mine[i];
                try
                {
                    await provider.SendAsync(entry.Message);
                    sent++;
                    Log.Write($"Posta in uscita: partito {Log.Private(entry.Message.Subject)} di {provider.Account.Email}");
                    entry.Sent?.Invoke(provider);
                }
                catch (Exception ex) when (Network.IsOffline(ex))
                {
                    lock (entries) entries.InsertRange(0, mine.Skip(i));
                    Log.Write($"Posta in uscita di {provider.Account.Email}: la rete manca ancora, {mine.Count - i} messaggi aspettano");
                    return sent;
                }
                catch (Exception ex)
                {
                    Log.Error("Posta in uscita", ex);
                    entry.Failed?.Invoke(ex);
                }
            }
            return sent;
        }
        finally
        {
            Changed?.Invoke();
        }
    }
}
