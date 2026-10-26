namespace Missiva.UI;

/// <summary>
/// L'elenco aperto rispetto al server (audit di affidabilità del 25 ottobre 2026, elenco). Quando la prima pagina non arriva e a
/// schermo restano i messaggi di prima (la copia locale, o quelli già letti), la riga sopra l'elenco lo dice ("Copia sul PC
/// del ...: non aggiornata", con Riprova) e l'elenco si rilegge da solo appena l'account torna a rispondere: un controllo
/// riuscito (NoteAccountState), l'IDLE collegato o la query delta riuscita (AccountReached), la rete cambiata
/// (App.OnNetworkBack); e alla riapertura dall'area di notifica se è vecchio di qualche minuto. Prima la copia restava per ore,
/// senza avvisi né pagine dopo (E-01; il 25 ottobre alle 14:01, dopo 18 ore di sospensione, il numero diceva 4 nuovi e
/// l'elenco era quello della sera prima). Le decisioni sono in ListRefresh.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Le righe tolte e arrivate da quando una pagina è stata chiesta: le pagine arrivate dopo non rimettono le righe eliminate e
    /// non perdono gli arrivi (E-07).
    /// </summary>
    readonly ListJournal journal = new();

    /// <summary>L'ultima lettura della prima pagina dell'elenco aperto non è riuscita: a schermo c'è quello che c'era (E-01).</summary>
    bool listStale;
    /// <summary>L'ultima prima pagina dell'elenco aperto arrivata dal server, e l'ultima rilettura partita da sola (UTC).</summary>
    DateTime listReadAt, listTriedAt;
    /// <summary>Di quando sono le righe dell'elenco aperto venute dalla copia locale (ora di Windows), per la riga sopra l'elenco.</summary>
    DateTime? listCopyAt;

    ListRefresh.State FreshState => new(listStale, listReadAt, listTriedAt, ListWatched);

    /// <summary>
    /// L'elenco aperto lo aggiorna il server da sé: la Posta in arrivo con l'IDLE in sessione o la query delta (avvisi di arrivi,
    /// segni, usciti), e le viste Posta in arrivo e Non letti di tutti gli account. Le altre cartelle, le etichette di Gmail e le
    /// viste Contrassegnati, Bozze e Posta inviata non le guarda nessuno (E-06).
    /// </summary>
    bool ListWatched => folder is { } open
        ? open.Folder.Kind == FolderKind.Inbox && (open.Account.Provider.HasInboxDelta || InboxIdle.Watching(open.Account.Provider.Account.Email))
        : unified is { View: UnifiedView.Inbox or UnifiedView.Unread };

    /// <summary>Per account, quando si sono riletti dal server i numeri delle cartelle (AfterCheck, UTC).</summary>
    readonly Dictionary<string, DateTime> countsAt = new();

    /// <summary>
    /// Il controllo di riserva dell'account è riuscito (App.CheckAccountAsync, ogni minuto per Gmail e IMAP e ogni due per Graph):
    /// ogni ListRefresh.CountsEvery i numeri di tutte le sue cartelle dal server (RefreshCountsAsync), e la cartella aperta che
    /// nessuno guarda si rilegge se i suoi numeri sono cambiati, o se la sua prima pagina ha più di ListRefresh.OtherEvery. Prima
    /// le altre cartelle e i loro numeri restavano com'erano fino a un cambio di cartella o a F5, per giorni (audit di
    /// affidabilità del 25 ottobre 2026, elenco, E-06). Le finestre di una sola cartella rileggono la loro.
    /// </summary>
    internal async void AfterCheck(IMailProvider provider, bool periodic)
    {
        foreach (var window in FolderWindows) window.AfterCheck(provider, periodic);
        if (!periodic || accounts.FirstOrDefault(a => a.Provider.Account.Id == provider.Account.Id) is not { } item) return;
        var id = provider.Account.Id;
        HashSet<string> moved = new();
        try
        {
            if (only == null && item.Folders.Count > 0 && ListRefresh.CountsDue(countsAt.GetValueOrDefault(id), DateTime.UtcNow))
            {
                countsAt[id] = DateTime.UtcNow;
                moved = await RefreshCountsAsync(item);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"numeri delle cartelle di {item.LogName}", ex);
        }
        if (!HasList || searching || !ListShows(provider)) return;
        bool openMoved = folder != null ? folder.Account == item && moved.Contains(folder.Folder.Id)
                                        : unified is { } view && item.Folders.Any(f => f.Folder.Kind == view.Kind && moved.Contains(f.Folder.Id));
        var cause = openMoved ? ListRefresh.Cause.CountsChanged : ListRefresh.Cause.Periodic;
        if (!ListRefresh.Wanted(FreshState, cause, DateTime.UtcNow)) return;
        listTriedAt = DateTime.UtcNow;
        _ = RefreshListAsync(openMoved ? "numeri della cartella cambiati" : listStale ? "controllo riuscito" : "rilettura di ogni tanto");
    }

    /// <summary>
    /// I numeri delle cartelle dell'account dal server, senza il resto di LoadFoldersAsync (categorie, risposte automatiche, coda):
    /// una LIST-STATUS per Gmail e IMAP, mailFolders per Graph. Anche l'albero si aggiorna (cartelle create o eliminate altrove).
    /// I non letti delle Posta in arrivo cambiati qui nel frattempo restano i nostri e si ricontano (UnreadSync), come per
    /// LoadFoldersAsync. Restituisce le cartelle i cui numeri sono cambiati.
    /// </summary>
    async Task<HashSet<string>> RefreshCountsAsync(AccountItem account)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var before = account.Folders.ToDictionary(f => f.Folder.Id, f => new MailFolder { Id = f.Folder.Id, Name = f.Folder.Name, Unread = f.Folder.Unread, Total = f.Folder.Total });
        var ticket = UnreadSync.Ticket(account.Provider.Account.Id);
        var folders = await account.Provider.GetFoldersAsync();
        if (!account.SetServerFolders(folders, ticket)) App.Current.RecountSoon(account.Provider);
        MailCache.SaveFolders(account.Provider.Account, account.Snapshot());
        UpdateFavorites();
        var moved = account.Folders.Where(f => !before.TryGetValue(f.Folder.Id, out var old) || ListRefresh.CountsMoved(old, f.Folder)).Select(f => f.Folder.Id).ToHashSet();
        Log.Time($"numeri delle cartelle di {account.LogName} riletti ({folders.Count} cartelle, {moved.Count} cambiate)", clock);
        var others = moved.Where(m => account.Folders.FirstOrDefault(f => f.Folder.Id == m)?.Folder.Kind != FolderKind.Inbox).ToList();
        if (others.Count > 0) Log.Write($"numeri delle cartelle di {account.LogName} cambiati sul server: {others.Count} cartelle oltre alla Posta in arrivo");
        return moved;
    }

    /// <summary>Un elenco nuovo (un'altra cartella o vista, un filtro): non è ancora stato letto, e non è vecchio.</summary>
    void ResetFreshness(DateTime? copyAt)
    {
        listStale = false;
        listReadAt = listTriedAt = DateTime.MinValue;
        listCopyAt = copyAt;
        refreshAfterLoad = false; // l'elenco nuovo si legge da capo
    }

    /// <summary>
    /// Una rilettura chiesta mentre l'elenco stava caricando (una pagina dopo scorrendo, la prima pagina, un'altra rilettura), con il
    /// suo motivo: parte quando quel caricamento finisce (RefreshIfAsked). Prima si buttava, e l'avviso dell'IDLE di un messaggio
    /// uscito arrivato in quel momento non faceva più nulla (audit di affidabilità del 25 ottobre 2026, elenco, E-08).
    /// </summary>
    bool refreshAfterLoad, refreshing;
    string refreshAfterWhy = "";

    void DeferRefresh(string why)
    {
        refreshAfterLoad = true;
        refreshAfterWhy = why;
    }

    /// <summary>Il caricamento dell'elenco (quello con questa versione) è finito: la rilettura chiesta intanto parte adesso.</summary>
    void RefreshIfAsked(int version)
    {
        if (version != listVersion || !refreshAfterLoad || loading || refreshing) return;
        refreshAfterLoad = false;
        _ = RefreshListAsync(refreshAfterWhy + ", dopo il caricamento che era in corso");
    }

    /// <summary>La prima pagina è arrivata dal server: l'elenco è aggiornato.</summary>
    void MarkListRead()
    {
        listStale = false;
        listReadAt = DateTime.UtcNow;
    }

    /// <summary>
    /// La prima pagina non è arrivata: l'elenco resta com'è, e la riga sopra lo dice con l'ora di quello che si vede (l'ultima
    /// lettura dal server, o la copia sul PC), con Riprova. Senza righe resta la riga dell'errore di sempre. Un errore della rete
    /// segna l'account non in linea (Network.Note), come quando non riesce il controllo: prima l'elenco non lo diceva a nessuno.
    /// </summary>
    void MarkListStale(MailAccount account, Exception ex)
    {
        listStale = true;
        Network.Note(account, ex);
        if (rows.Rows.Count == 0) return; // la riga dell'errore la mette chi chiama, come prima
        var when = listReadAt > DateTime.MinValue ? listReadAt.ToLocalTime() : listCopyAt;
        SetListStatus(when is { } at ? ListRefresh.StaleText(at) : Network.Explain(ex) ?? T("Non riesco a caricare i messaggi: {0}", ex.Message),
                      () => _ = RefreshListAsync("Riprova"));
    }

    /// <summary>L'elenco aperto mostra messaggi dell'account: una sua cartella, o una vista di tutti gli account.</summary>
    bool ListShows(IMailProvider? provider) =>
        provider == null || folder?.Account.Provider.Account.Id == provider.Account.Id || folder == null && unified != null && accounts.Any(a => a.Provider.Account.Id == provider.Account.Id);

    /// <summary>
    /// L'account ha risposto: se l'elenco aperto è rimasto vecchio (la prima pagina non era arrivata), si rilegge adesso, al più
    /// una volta ogni ListRefresh.StaleRetry. Anche nelle finestre di una sola cartella.
    /// </summary>
    internal void RetryStaleList(IMailProvider? provider, string why)
    {
        foreach (var window in FolderWindows) window.RetryStaleList(provider, why);
        if (!HasList || searching || !ListShows(provider) || !ListRefresh.Wanted(FreshState, ListRefresh.Cause.AccountBack, DateTime.UtcNow)) return;
        listTriedAt = DateTime.UtcNow;
        _ = RefreshListAsync(why);
    }

    /// <summary>Per account, il numero del server per cui si è riletto l'ultima volta (RereadIfCountBelowRows), e quando.</summary>
    readonly Dictionary<string, (int Count, DateTime At)> countReread = new();

    /// <summary>
    /// I da leggere della Posta in arrivo contati dal server sono meno dei messaggi da leggere che l'elenco aperto mostra per lei:
    /// qualcosa è stato letto o è uscito altrove senza che le righe lo sapessero (un avviso dell'IDLE senza UID, la query delta,
    /// che non guarda i messaggi più vecchi di due giorni prima dell'avvio, la vista di tutti gli account). La Posta in arrivo
    /// aperta, o la vista di tutti gli account, si rilegge, una volta per numero o ogni cinque minuti al più: prima il numero diceva
    /// 0 e la riga restava in grassetto fino a un cambio di cartella (audit di affidabilità del 25 ottobre 2026, letti, L-10, la
    /// parte dell'elenco, ed elenco, E-11).
    /// </summary>
    void RereadIfCountBelowRows(FolderItem inbox, int serverUnread)
    {
        if (!HasList || searching || !ListShows(inbox.Account.Provider)) return;
        int shown = rows.Rows.Where(r => r.Account == inbox.Account).SelectMany(MessageRows.MessagesOf)
                             .Where(m => !m.IsRead && m.FolderId == inbox.Folder.Id).DistinctBy(m => m.Id).Count();
        if (!ListRefresh.CountBelowRows(serverUnread, shown)) return;
        var id = inbox.Account.Provider.Account.Id;
        if (countReread.TryGetValue(id, out var last) && last.Count == serverUnread && DateTime.UtcNow - last.At < TimeSpan.FromMinutes(5)) return;
        countReread[id] = (serverUnread, DateTime.UtcNow);
        RefreshInboxOf(inbox.Account.Provider, $"il server conta {serverUnread} da leggere, l'elenco ne mostra {shown}");
    }

    /// <summary>Riaperta dall'area di notifica: l'elenco rimasto vecchio, o letto più di qualche minuto fa, si rilegge.</summary>
    void RefreshAfterTray()
    {
        if (!HasList || searching || !ListRefresh.Wanted(FreshState, ListRefresh.Cause.TrayReopen, DateTime.UtcNow)) return;
        listTriedAt = DateTime.UtcNow;
        _ = RefreshListAsync("finestra riaperta dall'area di notifica");
    }
}
