namespace Missiva.UI;

/// <summary>
/// Le righe tolte e quelle arrivate nell'elenco aperto, in ordine, per correggere le pagine chieste prima e arrivate dopo
/// (audit di affidabilità del 25 ottobre 2026, elenco, E-07). Una pagina in viaggio non sa di quello che è successo intanto:
/// con Gmail e IMAP Elimina aspetta la pagina sulla stessa connessione, e la pagina arriva con il messaggio eliminato, che
/// tornava nell'elenco (l'EXPUNGE all'IDLE è l'eco di Missiva e non lo toglie); un arrivo messo in cima mentre la pagina era in
/// viaggio spariva con lei, anche dalla prima pagina salvata, e nessuno lo rimetteva. Chi chiede una pagina prende il segno
/// (Asked) prima di chiederla; quando arriva, Apply le toglie i messaggi usciti da allora e le aggiunge quelli entrati da
/// allora, come ActionQueue.Apply fa per la coda e ReadMarks.Apply per i segni di lettura. Vale per ogni messaggio l'ultima cosa
/// successa: tolto e poi rimesso (Annulla, uno spostamento rifiutato dal server) è entrato. Sul thread dell'interfaccia.
/// </summary>
internal sealed class ListJournal
{
    sealed record Entry(long At, DateTime When, string Account, string Folder, string Id, MessageSummary? Arrived);

    readonly List<Entry> entries = new();
    long clock;

    /// <summary>Quante voci al più, e per quanto: le pagine in viaggio più vecchie di così non arrivano più (tempo scaduto).</summary>
    const int MaxEntries = 2000;
    static readonly TimeSpan Keep = TimeSpan.FromMinutes(10);

    /// <summary>Il segno da prendere prima di chiedere una pagina.</summary>
    public long Asked() => clock;

    /// <summary>Righe tolte dall'elenco (Elimina, Archivia, Sposta, la notifica, le regole, un messaggio uscito altrove).</summary>
    public void Removed(string accountId, IEnumerable<MessageSummary> messages)
    {
        foreach (var m in messages) Add(new Entry(++clock, DateTime.UtcNow, accountId, m.FolderId, m.Id, null));
    }

    /// <summary>Righe entrate nell'elenco senza una pagina (un arrivo messo in cima, Annulla, uno spostamento rifiutato rimesso).</summary>
    public void Arrived(string accountId, IEnumerable<MessageSummary> messages)
    {
        foreach (var m in messages) Add(new Entry(++clock, DateTime.UtcNow, accountId, m.FolderId, m.Id, m));
    }

    void Add(Entry entry)
    {
        entries.Add(entry);
        // In ordine di tempo: le più vecchie stanno in testa.
        if (entries.Count > MaxEntries) entries.RemoveRange(0, entries.Count - MaxEntries);
        int fresh = entries.FindIndex(e => e.When >= entry.When - Keep);
        if (fresh > 0) entries.RemoveRange(0, fresh);
    }

    /// <summary>
    /// Una pagina della cartella (folderId) dell'account chiesta con il segno asked: le toglie i messaggi usciti da allora e, se
    /// arrivals, le aggiunge in cima quelli entrati da allora nella stessa cartella (la prima pagina; non le pagine dopo, che
    /// sono più in basso), nell'ordine per data dal più recente se byDate. Restituisce quanti tolti e quanti aggiunti.
    /// </summary>
    public (int Removed, int Added) Apply(long asked, string accountId, string folderId, List<MessageSummary> page, bool arrivals, bool byDate)
    {
        if (asked >= clock) return (0, 0);
        var last = new Dictionary<(string Folder, string Id), Entry>();
        foreach (var e in entries)
            if (e.At > asked && e.Account == accountId) last[(e.Folder, e.Id)] = e;
        if (last.Count == 0) return (0, 0);
        int removed = page.RemoveAll(m => last.TryGetValue((m.FolderId, m.Id), out var e) && e.Arrived == null);
        int added = 0;
        if (arrivals)
        {
            var have = page.Select(m => (m.FolderId, m.Id)).ToHashSet();
            var back = last.Values.Where(e => e.Arrived != null && e.Folder == folderId && !have.Contains((e.Folder, e.Id))).OrderBy(e => e.At).Select(e => e.Arrived!).ToList();
            added = back.Count;
            if (added > 0)
            {
                page.InsertRange(0, back.AsEnumerable().Reverse());
                if (byDate)
                {
                    var sorted = page.OrderByDescending(m => m.Instant).ToList();
                    page.Clear();
                    page.AddRange(sorted);
                }
            }
        }
        return (removed, added);
    }
}
