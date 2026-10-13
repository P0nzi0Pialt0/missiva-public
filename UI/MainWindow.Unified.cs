using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Missiva.UI;

/// <summary>
/// Tutti gli account, come Thunderbird, Apple Mail ed eM Client: in cima alle cartelle (con due o più account) le Posta
/// in arrivo di tutti insieme, i loro non letti e contrassegnati, Bozze e Posta inviata; e la ricerca in tutti gli
/// account. Le prime pagine si chiedono in parallelo e si uniscono per data; ogni riga tiene il suo account (azioni,
/// Rispondi, Da, corpi in anticipo per account). Le pagine dopo arrivano per account, e dell'elenco si mostra solo fin
/// dove tutti gli account sono arrivati: un messaggio più vecchio non passa mai davanti a uno che deve ancora arrivare.
/// </summary>
public partial class MainWindow
{
    readonly ObservableCollection<UnifiedItem> unifiedItems = new(Enum.GetValues<UnifiedView>().Select(v => new UnifiedItem(v)));
    /// <summary>La vista di tutti gli account aperta; allora folder è null.</summary>
    UnifiedItem? unified;

    /// <summary>
    /// Una fonte di un elenco di più account: la cartella (o la ricerca) di un account, la sua prossima pagina e i messaggi
    /// arrivati ma non ancora mostrati, perché più vecchi di dove sono arrivati gli altri account.
    /// </summary>
    sealed class MergedSource(AccountItem account, FolderItem? folder, Func<string?, Task<MessagePage>> fetch)
    {
        public AccountItem Account { get; } = account;
        /// <summary>La cartella della vista (per le anteprime); null nella ricerca.</summary>
        public FolderItem? Folder { get; } = folder;
        public Func<string?, Task<MessagePage>> Fetch { get; } = fetch;
        public bool Started;
        public string? Next;
        public bool Done => Started && Next == null;
        public string? Error;
        public readonly List<MessageSummary> Pending = new();
        /// <summary>La data del messaggio più vecchio arrivato: più in basso l'account può averne altri ancora da chiedere.</summary>
        public DateTime Oldest = DateTime.MaxValue;
    }

    /// <summary>Le fonti dell'elenco di più account aperto (vista di tutti gli account o ricerca in tutti gli account).</summary>
    List<MergedSource> merged = new();

    /// <summary>C'è un elenco aperto: una cartella o una vista di tutti gli account.</summary>
    bool HasList => folder != null || unified != null;

    void ShowUnifiedGroup()
    {
        UnifiedList.ItemsSource ??= unifiedItems;
        UnifiedList.Visibility = S.UnifiedCollapsed ? Visibility.Collapsed : Visibility.Visible;
        UpdateGroupMarks();
        UnifiedChevron.Text = S.UnifiedCollapsed ? "" : "";
    }

    /// <summary>
    /// Il gruppo "Tutti gli account" c'è con due o più account (non nella finestra di una sola cartella), se non è spento
    /// nelle Impostazioni (Settings.ShowUnified); Posta in arrivo e Non letti hanno la somma dei non letti delle Posta in
    /// arrivo, come il numero sull'icona.
    /// </summary>
    void UpdateUnified()
    {
        UnifiedPanel.Visibility = only == null && accounts.Count >= 2 && S.ShowUnified ? Visibility.Visible : Visibility.Collapsed;
        int unread = accounts.SelectMany(a => a.Folders).Where(f => f.Folder.Kind == FolderKind.Inbox).Sum(f => f.Unread);
        foreach (var u in unifiedItems) u.Unread = u.View is UnifiedView.Inbox or UnifiedView.Unread ? unread : 0;
        if (unified != null && UnifiedPanel.Visibility != Visibility.Visible) SelectInbox(); // rimasto un account solo
    }

    void OnUnifiedToggle(object sender, RoutedEventArgs e)
    {
        S.UnifiedCollapsed = !S.UnifiedCollapsed;
        Store.SaveSoon();
        ShowUnifiedGroup();
    }

    void OnUnifiedSelected(object sender, SelectionChangedEventArgs e)
    {
        if (selectingFolder || UnifiedList.SelectedItem is not UnifiedItem item) return;
        CloseFoldersOver();
        SelectUnified(item);
    }

    /// <summary>Una vista di tutti gli account: come una cartella, ma con le righe di tutti gli account.</summary>
    void SelectUnified(UnifiedItem item)
    {
        // La vista scelta si vede evidenziata anche con il gruppo di Tutti gli account chiuso.
        if (S.UnifiedCollapsed)
        {
            S.UnifiedCollapsed = false;
            Store.SaveSoon();
            ShowUnifiedGroup();
        }
        MarkSelectedFolder(null);
        MarkUnified(item);
        folder = null;
        unified = item;
        searching = false;
        SearchBox.Text = "";
        FocusedTabs.Visibility = Visibility.Collapsed;
        focused = null;
        listOptions = ListOptions.Default;
        rows.Sort = SortBy.Date;
        UpdateListHeader();
        ShowSearchScope();
        reloading = ReloadAsync(fromTop: true);
    }

    void MarkUnified(UnifiedItem? item)
    {
        selectingFolder = true;
        UnifiedList.SelectedItem = item;
        selectingFolder = false;
        markedUnified = item;
        UpdateGroupMarks();
    }

    /// <summary>La cartella della vista in ogni account (Posta in arrivo, Bozze o Posta inviata), dove c'è.</summary>
    List<(AccountItem Account, FolderItem Folder)> UnifiedFolders(UnifiedItem view) =>
        accounts.Select(a => (Account: a, Folder: a.Folders.FirstOrDefault(f => f.Folder.Kind == view.Kind)))
                .Where(x => x.Folder != null).Select(x => (x.Account, x.Folder!)).ToList();

    /// <summary>
    /// La vista di tutti gli account: la prima pagina di ogni account in parallelo (subito le copie locali delle Posta in
    /// arrivo, se ci sono), poi le pagine dopo scorrendo.
    /// </summary>
    async Task LoadUnifiedAsync(bool first)
    {
        if (unified is not { } view || searching || !first && (loading || merged.All(s => s.Done))) return;
        loading = true;
        var version = listVersion;
        Dictionary<(AccountItem, string), MessageSummary> cached = new();
        try
        {
            if (first)
            {
                var folders = UnifiedFolders(view);
                merged = folders.Select(x => new MergedSource(x.Account, x.Folder, async next =>
                {
                    var page = await x.Account.Provider.GetMessagesAsync(x.Folder.Folder, null, next, view.Options);
                    ActionQueue.Apply(x.Account.Provider.Account.Id, page.Messages); // la coda delle azioni senza rete non è ancora ripartita
                    return page;
                })).ToList();
                if (view.View == UnifiedView.Inbox)
                {
                    // Le copie locali delle Posta in arrivo (di tutta la Posta in arrivo, o di Evidenziata se c'è solo quella).
                    // I tornati da Posponi con l'ora del ritorno prima di ordinare, come nelle pagine (ApplyWoken).
                    var saved = folders.SelectMany(x =>
                    {
                        var page = MailCache.LoadMessages(x.Account.Provider.Account, x.Folder.Folder.Id, null)
                                   ?? MailCache.LoadMessages(x.Account.Provider.Account, x.Folder.Folder.Id, true) ?? new();
                        ApplyWoken(page, x.Account);
                        ActionQueue.Apply(x.Account.Provider.Account.Id, page); // senza rete: spostati e segni in coda come fatti
                        return page.Select(m => (Message: m, x.Account));
                    }).OrderByDescending(x => x.Message.Date).Take(50).ToList();
                    foreach (var (m, a) in saved) cached[(a, m.Id)] = m;
                    rows.Append(saved);
                }
                SetListStatus(rows.Rows.Count > 0 ? null : "Caricamento…");
            }
            var clock = Stopwatch.StartNew();
            var shown = await NextMergedAsync(first, version);
            if (shown == null) return; // nel frattempo è cambiata cartella
            Log.Time($"elenco di tutti gli account: {view.Name} ({merged.Count} account, {shown.Count} messaggi, " +
                     $"{(first ? "prima pagina" : "pagina successiva")}{(cached.Count > 0 ? ", copia locale già visibile" : "")})", clock);
            if (view.View == UnifiedView.Inbox) AddressBook.Received(shown.Select(x => x.Message).ToList());
            if (first && cached.Count > 0)
            {
                // Arrivati dal server: le righe della copia locale lasciano il posto, con le anteprime già note e la selezione.
                foreach (var (m, a) in shown)
                    if (m.Preview.Length == 0 && cached.TryGetValue((a, m.Id), out var old)) m.Preview = old.Preview;
                var selected = Selected.Select(i => (i.Account, i.Summary.Id)).ToHashSet();
                regrouping = true;
                rows.Clear();
                rows.Append(shown);
                foreach (var item in rows.Rows.Where(r => selected.Contains((r.Account, r.Summary.Id)))) MessageList.SelectedItems.Add(item);
                regrouping = false;
                UpdateCommands();
            }
            else AppendKeepingPosition(() => rows.Append(shown));
            ShowMergedStatus("Non ci sono messaggi qui.");
            foreach (var g in shown.GroupBy(x => x.Account))
                if (merged.FirstOrDefault(s => s.Account == g.Key)?.Folder is { } where)
                    _ = FillPreviewsAsync(where, version, g.Select(x => x.Message).ToList(), prefetch: first);
        }
        catch (Exception ex)
        {
            Log.Error("elenco di tutti gli account", ex);
            if (version == listVersion && cached.Count == 0) SetListStatus(Network.Explain(ex) ?? "Non riesco a caricare i messaggi: " + ex.Message, () => _ = LoadUnifiedAsync(first));
        }
        finally
        {
            if (version == listVersion) loading = false;
        }
    }

    /// <summary>
    /// I messaggi da aggiungere in fondo all'elenco di più account: le fonti non ancora partite e quelle che fermano
    /// l'elenco (arrivate meno in basso delle altre) chiedono la loro pagina, poi si mostra fin dove sono arrivate tutte.
    /// Alla prima pagina, finché l'elenco non ne ha almeno 30 e qualcuno ha ancora messaggi, un altro giro. null se nel
    /// frattempo l'elenco è cambiato.
    /// </summary>
    async Task<List<(MessageSummary Message, AccountItem Account)>?> NextMergedAsync(bool first, int version)
    {
        var shown = new List<(MessageSummary Message, AccountItem Account)>();
        for (int round = 0; round < 4; round++)
        {
            var cutoff = Cutoff();
            var need = merged.Where(s => !s.Started || !s.Done && s.Oldest >= cutoff).ToList();
            if (need.Count == 0) break;
            await Task.WhenAll(need.Select(FetchAsync));
            if (version != listVersion) return null;
            shown.AddRange(Release());
            if (!first || shown.Count >= 30 || merged.All(s => s.Done)) break;
        }
        return shown;
    }

    /// <summary>La pagina dopo di una fonte. Un account che non risponde non ferma gli altri: resta fuori, e l'elenco lo dice.</summary>
    static async Task FetchAsync(MergedSource s)
    {
        try
        {
            var page = await s.Fetch(s.Started ? s.Next : null);
            s.Started = true;
            s.Next = page.Next;
            ApplyWoken(page.Messages, s.Account);
            s.Pending.AddRange(page.Messages);
            if (page.Messages.Count > 0) s.Oldest = new[] { s.Oldest, page.Messages.Min(m => m.Date) }.Min();
        }
        catch (Exception ex)
        {
            Log.Error($"elenco di {s.Account.Header} in tutti gli account", ex);
            s.Started = true;
            s.Next = null;
            s.Error = ex.Message;
        }
    }

    /// <summary>Fin dove tutte le fonti che hanno ancora messaggi sono arrivate: più in basso ne possono mancare.</summary>
    DateTime Cutoff() => merged.Where(s => !s.Done).Select(s => s.Oldest).DefaultIfEmpty(DateTime.MinValue).Max();

    /// <summary>I messaggi arrivati fino al limite (Cutoff), di tutti gli account, dal più recente.</summary>
    List<(MessageSummary Message, AccountItem Account)> Release()
    {
        var cutoff = Cutoff();
        var released = merged.SelectMany(s => s.Pending.Where(m => m.Date >= cutoff).Select(m => (Message: m, s.Account)))
                             .OrderByDescending(x => x.Message.Date).ToList();
        foreach (var s in merged) s.Pending.RemoveAll(m => m.Date >= cutoff);
        return released;
    }

    /// <summary>Elenco vuoto, o account che non hanno risposto: l'elenco lo dice sopra le righe.</summary>
    void ShowMergedStatus(string empty)
    {
        var failed = merged.Where(s => s.Error != null).ToList();
        if (failed.Count > 0)
            SetListStatus($"{string.Join(", ", failed.Select(s => s.Account.Header))} non {(failed.Count == 1 ? "risponde" : "rispondono")}: " +
                          (failed.Count == 1 ? failed[0].Error : "i loro messaggi mancano."));
        else SetListStatus(rows.Rows.Count == 0 ? empty : null);
    }

    /// <summary>Nuovi messaggi di un account con la Posta in arrivo di tutti gli account aperta: salgono in cima con il loro account.</summary>
    bool AddIncomingUnified(AccountItem account, List<MessageSummary> incoming)
    {
        if (unified is not { View: UnifiedView.Inbox or UnifiedView.Unread } || searching || incoming.Count == 0) return false;
        MarkArrived(PrependRows(incoming, account));
        return true;
    }

    // ---------------------------------------------------------------- ricerca in tutti gli account

    /// <summary>Si cerca in tutti gli account: scelto in "Dove cercare", o sempre dalla vista di tutti gli account.</summary>
    bool searchAccounts;
    bool SearchesAccounts => searchAccounts || folder == null && unified != null;
    /// <summary>La ricerca in tutti gli account in corso: il testo per ogni account, nella sua sintassi (persone, file).</summary>
    Func<IMailProvider, string>? searchAcross;

    /// <summary>
    /// Ricerca in tutte le cartelle di tutti gli account: le prime pagine in parallelo, unite per data, con l'account e la
    /// cartella di ogni risultato; scorrendo arrivano le successive di ogni account.
    /// </summary>
    async Task SearchAccountsAsync(string text, Func<IMailProvider, string> queryFor)
    {
        if (accounts.Count == 0) return;
        // I filtri (pastiglie e parole chiave scritte a mano) valgono per ogni account, con il testo della sua sintassi.
        text = TakeFilters(text);
        var filters = searchFilters;
        var inner = queryFor;
        queryFor = p =>
        {
            var parsed = SearchTerms.Parse(inner(p));
            return Merge(filters, parsed).WithText(parsed.Text).ToString();
        };
        CloseSuggestions(keep: accounts.Select(a => SuggestSearches.Key(a.Provider.Account.Id, null, queryFor(a.Provider))).ToList()); // le ricerche dei suggerimenti con lo stesso testo si riusano
        S.RememberSearch(text);
        Store.SaveSoon();
        searching = true;
        MarkSelectedFolder(null);
        MarkUnified(null);
        listVersion++;
        CancelListPrefetch();
        CancelOfflineSearch(); // la ricerca di prima nella copia sul PC, se ancora in corso, si ferma
        searchQuery = text;
        searchWhere = null;
        searchAccount = null;
        searchNext = null;
        searchAcross = queryFor;
        rows.Clear();
        rows.ShowAccounts = Reader.ShowAccount = true;
        rows.ByDue = false;
        Reader.Clear();
        ListTitle.Text = "Risultati in Tutti gli account";
        ListTitle.ToolTip = ListTitle.Text;
        StarButton.Visibility = FocusedTabs.Visibility = Visibility.Collapsed;
        // Senza rete, per l'account non in linea i risultati della copia sul PC (una pagina sola).
        merged = accounts.Select(a => new MergedSource(a, null, async next =>
        {
            var page = await SearchOrOfflineAsync(a, null, queryFor(a.Provider), next);
            return page.Next == OfflineResults ? new MessagePage { Messages = page.Messages } : page;
        })).ToList();
        SetListStatus("Ricerca in corso…");
        await SearchAccountsPageAsync(first: true);
    }

    async Task SearchAccountsPageAsync(bool first)
    {
        if (!searching || searchAcross == null || !first && (loading || merged.All(s => s.Done))) return;
        loading = true;
        var version = listVersion;
        try
        {
            var clock = Stopwatch.StartNew();
            var shown = await NextMergedAsync(first, version);
            if (shown == null) return;
            Log.Time($"ricerca in tutti gli account ({shown.Count} risultati da {shown.Select(x => x.Account).Distinct().Count()} account" +
                     $"{(first ? "" : ", pagina successiva")})", clock);
            // Graph dice solo l'identificativo della cartella: il nome lo sa l'elenco delle cartelle del suo account.
            foreach (var (m, a) in shown.Where(x => x.Message.FolderName.Length == 0))
                m.FolderName = a.Folders.FirstOrDefault(f => f.Folder.Id == m.FolderId)?.Name ?? "";
            AppendKeepingPosition(() => rows.Append(shown));
            ShowMergedStatus($"Nessun risultato per \"{searchQuery}\".");
            foreach (var g in shown.GroupBy(x => x.Account))
                if ((g.Key.Folders.FirstOrDefault(f => f.Folder.Kind == FolderKind.Inbox) ?? g.Key.Folders.FirstOrDefault()) is { } where)
                    _ = FillPreviewsAsync(where, version, g.Select(x => x.Message).ToList(), prefetch: first);
        }
        catch (Exception ex)
        {
            Log.Error("ricerca in tutti gli account", ex);
            if (version == listVersion) SetListStatus("Ricerca non riuscita: " + ex.Message, () => _ = SearchAccountsPageAsync(first));
        }
        finally
        {
            if (version == listVersion) loading = false;
        }
    }
}
