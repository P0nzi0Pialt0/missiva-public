namespace Missiva.UI;

/// <summary>
/// Quando l'elenco aperto si rilegge dal server da solo (audit di affidabilità del 25 ottobre 2026, elenco). Prima si
/// rileggeva solo cambiando cartella, con Sincronizza (F5) o per pochi avvisi dell'IDLE: una prima pagina che non arrivava
/// (rete non ancora pronta dopo la sospensione o all'accesso a Windows, una connessione morta) lasciava a schermo la copia
/// locale, in silenzio e senza le pagine dopo, finché non si cambiava cartella (E-01). Qui le decisioni, senza la finestra,
/// per provarle nella suite (gruppo affidabilita-elenco); MainWindow le chiama dai posti in cui l'account torna a rispondere.
/// </summary>
internal static class ListRefresh
{
    /// <summary>Che cosa chiede di guardare se l'elenco aperto va riletto.</summary>
    public enum Cause
    {
        /// <summary>L'account dell'elenco ha risposto (un controllo riuscito, l'IDLE collegato, la query delta, la rete cambiata).</summary>
        AccountBack,
        /// <summary>La finestra riaperta dall'area di notifica.</summary>
        TrayReopen,
        /// <summary>Il controllo di riserva dell'account è riuscito (ogni minuto per Gmail e IMAP, ogni due per Graph).</summary>
        Periodic,
        /// <summary>I numeri della cartella aperta, riletti dal server, sono cambiati.</summary>
        CountsChanged,
    }

    /// <summary>
    /// Com'è l'elenco aperto rispetto al server: Stale se l'ultima lettura della prima pagina non è riuscita (a schermo resta la
    /// copia locale, o quello che c'era); ReadAt l'ultima prima pagina arrivata dal server (UTC); TriedAt l'ultima rilettura
    /// partita da sola; Watched se il server dice da sé che cosa cambia (la Posta in arrivo con l'IDLE in sessione o la query
    /// delta, e le viste Posta in arrivo e Non letti di tutti gli account): le altre cartelle non le guarda nessuno.
    /// </summary>
    public readonly record struct State(bool Stale, DateTime ReadAt, DateTime TriedAt, bool Watched = true);

    /// <summary>
    /// La cartella aperta che nessuno guarda (non la Posta in arrivo: le altre cartelle, le etichette di Gmail, Bozze, Posta
    /// inviata e Contrassegnati di tutti gli account) si rilegge al controllo di riserva se la sua prima pagina è più vecchia di
    /// così, e subito se i suoi numeri cambiano (CountsChanged). Scelta qui: il costo è quello di aprirla (per Gmail e IMAP un
    /// NOOP e una FETCH di 50 righe, per Graph una richiesta), 12 volte all'ora al più, e solo per la cartella aperta (E-06).
    /// </summary>
    public static readonly TimeSpan OtherEvery = TimeSpan.FromMinutes(5);

    /// <summary>
    /// I numeri di tutte le cartelle (anche le etichette di Gmail) si rileggono al controllo di riserva al più ogni tanto per
    /// account: per Gmail e IMAP una LIST-STATUS (una STATUS per cartella dove il server non la ha), per Graph le richieste di
    /// mailFolders (una, più una per ogni cartella con sottocartelle, più l'Archivio). Prima solo all'avvio, con F5 e dopo le
    /// azioni sulle cartelle: con Missiva aperta per giorni i numeri erano di giorni prima (E-06).
    /// </summary>
    public static readonly TimeSpan CountsEvery = TimeSpan.FromMinutes(5);

    /// <summary>I numeri delle cartelle dell'account vanno riletti adesso (l'ultima volta last, UTC).</summary>
    public static bool CountsDue(DateTime last, DateTime nowUtc) => nowUtc - last >= CountsEvery;

    /// <summary>La cartella ha altri numeri (messaggi o da leggere) da quando si è letta l'ultima volta: qualcosa è entrato, uscito o cambiato.</summary>
    public static bool CountsMoved(MailFolder before, MailFolder after) => before.Total != after.Total || before.Unread != after.Unread;

    /// <summary>
    /// Fra due riletture partite da sole di un elenco rimasto vecchio: con l'account che risponde a ogni controllo (ogni minuto)
    /// e una cartella che non si legge per un altro motivo (eliminata altrove, un errore del server), una per controllo al più.
    /// </summary>
    public static readonly TimeSpan StaleRetry = TimeSpan.FromSeconds(30);

    /// <summary>Riaperta dall'area di notifica, la prima pagina letta più di questo tempo fa si rilegge.</summary>
    public static readonly TimeSpan TrayAge = TimeSpan.FromMinutes(2);

    /// <summary>Se l'elenco aperto va riletto adesso, per quella causa.</summary>
    public static bool Wanted(State state, Cause cause, DateTime nowUtc) => cause switch
    {
        Cause.AccountBack => state.Stale && nowUtc - state.TriedAt >= StaleRetry,
        Cause.TrayReopen => state.Stale || nowUtc - state.ReadAt >= TrayAge,
        Cause.Periodic => nowUtc - state.TriedAt >= StaleRetry && (state.Stale || !state.Watched && nowUtc - state.ReadAt >= OtherEvery),
        Cause.CountsChanged => nowUtc - state.TriedAt >= StaleRetry && (state.Stale || !state.Watched),
        _ => false,
    };

    /// <summary>
    /// I messaggi già caricati che stanno sotto la prima pagina riletta, e restano quando la pagina si unisce all'elenco
    /// (MainWindow.MergeFirstPage): gli altri non sono più sul server. Con Gmail e IMAP (byUid) le pagine vanno per numero d'ordine,
    /// cioè di arrivo, e l'UID cresce con quello: sotto la pagina c'è chi ha l'UID più basso del suo. Prima si guardava la data, e un
    /// messaggio rimesso in Posta in arrivo con una data vecchia, in fondo alla pagina, faceva credere usciti tutti quelli caricati
    /// scorrendo, che sparivano (audit di affidabilità del 25 ottobre 2026, elenco, E-12). Con Graph, che dà le pagine per data, la
    /// data.
    /// </summary>
    /// <param name="more">
    /// Il server ha altri messaggi sotto la pagina (la pagina ha un seguito, MessagePage.Next). Senza, la pagina è tutta la
    /// cartella e sotto non c'è nessuno: i caricati che non ha sono usciti (prova a finestra E03: con la regola per UID senza
    /// questo un messaggio eliminato altrove con l'UID più basso della cartella restava).
    /// </param>
    public static List<MessageSummary> Below(IEnumerable<MessageSummary> loaded, IReadOnlyList<MessageSummary> fresh, bool byUid, bool more = true)
    {
        if (fresh.Count == 0 || !more) return [];
        var freshIds = fresh.Select(m => (m.FolderId, m.Id)).ToHashSet();
        var others = loaded.Where(m => !freshIds.Contains((m.FolderId, m.Id))).ToList();
        if (byUid && fresh.All(m => uint.TryParse(m.Id, out _)))
        {
            uint lowest = fresh.Min(m => uint.Parse(m.Id));
            return others.Where(m => uint.TryParse(m.Id, out var uid) && uid < lowest).ToList();
        }
        var oldest = fresh.Min(m => m.Instant);
        return others.Where(m => m.Instant <= oldest).ToList();
    }

    /// <summary>
    /// Una pagina dopo (scorrendo) con un messaggio più recente del più vecchio già in elenco: aggiunta in fondo starebbe sotto di
    /// lui, e i suoi gruppi di date (Questo mese) dopo quelli più vecchi (Mese scorso). Allora l'elenco si rifà in ordine
    /// (MainWindow.LoadMoreAsync). Succede con Gmail e IMAP quando in fondo alla prima pagina c'è un messaggio rimesso con una
    /// data vecchia (E-12).
    /// </summary>
    public static bool OutOfOrder(DateTime oldestShown, IEnumerable<MessageSummary> page) => page.Any(m => m.Instant > oldestShown);

    /// <summary>
    /// Il messaggio nuovo entra in cima all'elenco aperto con queste opzioni: nella sua scheda (InTab), con l'elenco per data dal
    /// più recente, se passa il filtro (da leggere, contrassegnati, con allegati) e se è arrivato entro il giorno di "Vai a". Prima
    /// entrava sempre: con il filtro dei contrassegnati in cima c'era un messaggio che non lo era, con "dal più vecchio" o "Vai
    /// a" un messaggio di oggi (E-10). Gli altri arrivano alla rilettura.
    /// </summary>
    public static bool Enters(MessageSummary message, bool? tab, ListOptions options) =>
        InTab(message, tab) && options.Sort == SortBy.Date && !options.Ascending
        && options.Filter switch
        {
            MessageFilter.Unread => !message.IsRead,
            MessageFilter.Flagged => message.IsFlagged,
            MessageFilter.Attachments => message.HasAttachments,
            _ => true,
        }
        && (options.UpTo is not { } upTo || message.Date.Date <= upTo.Date);

    /// <summary>
    /// Il numero dei da leggere della Posta in arrivo detto dal server è più basso dei messaggi da leggere che l'elenco aperto
    /// mostra per lei: qualcosa è stato letto o è uscito altrove senza che le righe lo sapessero (un avviso senza UID, la query
    /// delta che non guarda i messaggi di più di due giorni prima dell'avvio). Allora l'elenco si rilegge (MainWindow.AddIncoming;
    /// audit di affidabilità del 25 ottobre 2026, letti, L-10, la parte dell'elenco).
    /// </summary>
    public static bool CountBelowRows(int serverUnread, int shownUnread) => serverUnread < shownUnread;

    /// <summary>
    /// Il messaggio nuovo sta nella scheda aperta della Posta in arrivo di Outlook (tab: vero Evidenziata, falso Altra, null
    /// senza schede): Graph dice la scheda di ogni messaggio (MessageSummary.Focused). Senza la scheda, come prima, entra. Prima
    /// con Evidenziata aperta entravano in cima anche i messaggi di Altra (E-09).
    /// </summary>
    public static bool InTab(MessageSummary message, bool? tab) => tab == null || message.Focused == null || message.Focused == tab;

    /// <summary>
    /// I messaggi della query delta (nuovi o cambiati nella Posta in arrivo) che l'elenco non ha fra quelli dati: se ce n'è
    /// uno, la Posta in arrivo aperta si rilegge (MainWindow.RefreshIfMissing; E-02). Quelli usciti no: per loro basta togliere
    /// la riga.
    /// </summary>
    public static List<string> Missing(InboxChanges changes, IEnumerable<string> known)
    {
        var have = known.ToHashSet();
        var gone = changes.Removed.ToHashSet();
        return changes.Seen.Select(s => s.Id).Where(id => !have.Contains(id) && !gone.Contains(id)).Distinct().ToList();
    }

    /// <summary>
    /// La riga sopra l'elenco quando la prima pagina non è arrivata e a schermo restano i messaggi di prima: di quando sono
    /// (l'ultima lettura dal server, o la copia sul PC), con Riprova accanto (MainWindow.SetListStatus).
    /// </summary>
    public static string StaleText(DateTime localTime) => T("Copia sul PC del {0}: non aggiornata.", localTime.ToString("g", Lingua.Culture));
}
