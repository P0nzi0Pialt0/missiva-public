using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Missiva.UI;

/// <summary>
/// Le ricerche sul server chieste dai suggerimenti della ricerca, per chiave (account, cartella, testo). Una ricerca uguale
/// a una già finita bene o ancora in corso non si richiede; le ricerche in corso si fermano quando il pannello è superato;
/// e Invio (o la scelta di una riga) riusa la ricerca con lo stesso testo appena finita o ancora in corso, invece di
/// rifarla: prima i suggerimenti cercavano sul server e Invio rifaceva la stessa ricerca, 1,0-1,8 s in più con Gmail
/// (audit, v1 V-05 e v2 V-27). Tutto sul thread dell'interfaccia.
/// </summary>
internal sealed class SuggestSearches
{
    sealed record Asked(Task<MessagePage> Page, CancellationTokenSource Cancel, long At);

    readonly Dictionary<string, Asked> asked = new();

    /// <summary>
    /// Per quanto tempo una ricerca finita vale ancora per Invio: dopo si rifà (intanto può essere arrivata posta). La stessa
    /// finestra della ricerca in tutte le cartelle di IMAP (MailLimits.SearchReuseSeconds): quella rifatta è nuova davvero.
    /// </summary>
    internal static readonly TimeSpan Fresh = TimeSpan.FromSeconds(MailLimits.SearchReuseSeconds);

    /// <summary>L'orologio in millisecondi (le prove lo spostano).</summary>
    internal Func<long> Clock = () => Environment.TickCount64;

    public static string Key(string accountId, string? folderId, string query) => $"{accountId}|{folderId}|{query}";

    /// <summary>La ricerca già chiesta con questa chiave, se è finita bene o è in corso e non fermata; altrimenti una nuova.</summary>
    public Task<MessagePage> Start(string key, Func<CancellationToken, Task<MessagePage>> search)
    {
        if (asked.TryGetValue(key, out var known) && (known.Page.IsCompletedSuccessfully || !known.Page.IsCompleted && !known.Cancel.IsCancellationRequested))
            return known.Page;
        if (asked.Count > 40) asked.Clear();
        var cancel = new CancellationTokenSource();
        var page = search(cancel.Token);
        asked[key] = new Asked(page, cancel, Clock());
        return page;
    }

    /// <summary>Le ricerche ancora in corso si fermano, tranne quelle che la ricerca che sta partendo riusa.</summary>
    public void CancelPending(ICollection<string>? keep = null)
    {
        foreach (var (key, search) in asked)
            if (!search.Page.IsCompleted && keep?.Contains(key) != true) search.Cancel.Cancel();
    }

    /// <summary>
    /// Per Invio: la ricerca con questa chiave finita bene da meno di Fresh, o ancora in corso e non fermata. Una volta sola
    /// (un secondo Invio cerca di nuovo); null se non c'è.
    /// </summary>
    public Task<MessagePage>? Take(string key)
    {
        if (!asked.Remove(key, out var known)) return null;
        if (known.Page.IsCompletedSuccessfully) return Clock() - known.At <= Fresh.TotalMilliseconds ? known.Page : null;
        return !known.Page.IsCompleted && !known.Cancel.IsCancellationRequested ? known.Page : null;
    }
}

/// <summary>
/// Il pannello sotto la ricerca, come nel nuovo Outlook: scrivendo propone la ricerca, le ricerche recenti, le persone
/// (dalla rubrica), i file allegati e i messaggi, con le schede Tutto, Mail, Persone e File. Ricerche recenti e persone
/// sono sul PC e arrivano subito; file e messaggi li cerca il server dell'account aperto, dopo una pausa nella scrittura.
/// Con la ricerca vuota (Ctrl+E, clic nella casella) mostra le ricerche recenti.
/// </summary>
public partial class MainWindow
{
    enum SuggestTab { All, Mail, People, Files }

    SuggestTab suggestTab = SuggestTab.All;
    int suggestVersion;
    bool suggestHooked, suggestPending, settingSearchText;
    /// <summary>
    /// La pausa nella scrittura prima di chiedere al server. Con Task.Delay e non con un DispatcherTimer: i timer di finestra
    /// Windows li può rimandare di secondi (nella versione di prova, in sottofondo, scattavano 2-5 s dopo).
    /// </summary>
    static readonly TimeSpan SuggestPause = TimeSpan.FromMilliseconds(350);
    /// <summary>
    /// Ricerche sul server già chieste (account, dove, testo), anche se non ancora finite: cancellando una lettera non si
    /// richiedono, due richieste uguali (IMAP: messaggi e file sono la stessa ricerca) diventano una, e Invio riusa quella
    /// con lo stesso testo invece di rifarla (SuggestSearches).
    /// </summary>
    readonly SuggestSearches suggestSearches = new();
    List<SearchSuggestion> suggestLocal = new(), suggestPeople = new(), suggestMails = new(), suggestFiles = new();

    AccountItem? SuggestAccount => searching ? searchAccount : folder?.Account;

    /// <summary>Gli account in cui cercano i suggerimenti: quello aperto (o della ricerca in corso), o tutti.</summary>
    List<AccountItem> SuggestAccounts => SearchesAccounts ? accounts.ToList() : SuggestAccount is { } one ? [one] : [];

    void HookSuggestions()
    {
        if (suggestHooked) return;
        suggestHooked = true;
        // Il pannello è una finestra a sé: non segue la finestra che si sposta, e resterebbe sopra le altre app.
        Deactivated += (_, _) => CloseSuggestions();
        LocationChanged += (_, _) => CloseSuggestions();
        StateChanged += (_, _) => CloseSuggestions();
    }

    /// <summary>Il testo della ricerca cambiato da Missiva (una riga scelta), non scritto: il pannello non si riapre.</summary>
    void SetSearchText(string text)
    {
        settingSearchText = true;
        SearchBox.Text = text;
        SearchBox.CaretIndex = text.Length;
        settingSearchText = false;
    }

    void OnSearchFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateSuggestions(fromFocus: true);

    void OnSearchBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!SuggestPopup.IsKeyboardFocusWithin) CloseSuggestions();
    }

    /// <param name="keep">Le ricerche dei suggerimenti che la ricerca che sta partendo riusa (SuggestSearches.Key): non si fermano.</param>
    void CloseSuggestions(ICollection<string>? keep = null)
    {
        NextSuggestVersion(keep);
        SuggestPopup.IsOpen = false;
    }

    /// <summary>A ogni nuova versione del pannello le ricerche dei suggerimenti superati si fermano.</summary>
    void NextSuggestVersion(ICollection<string>? keep = null)
    {
        suggestVersion++;
        suggestSearches.CancelPending(keep);
    }

    /// <param name="fromFocus">Entrando nella casella vuota si vedono le ricerche recenti; svuotandola il pannello si chiude.</param>
    void UpdateSuggestions(bool fromFocus = false)
    {
        if (settingSearchText) return;
        var typed = SearchBox.Text.Trim();
        if (!SearchBox.IsKeyboardFocused || SuggestAccounts is not [var account, ..] || typed.Length == 0 && !fromFocus)
        {
            CloseSuggestions();
            return;
        }
        bool across = SearchesAccounts;
        HookSuggestions();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        NextSuggestVersion();
        suggestMails = new();
        suggestFiles = new();
        suggestLocal = new();
        suggestPeople = new();
        if (typed.Length == 0)
        {
            suggestLocal.AddRange(Store.Settings.RecentSearches.Where(q => !ProtectedTextConverter.IsUnreadable(q)).Take(8).Select(q => Recent(q, "")));
        }
        else
        {
            suggestLocal.Add(new SearchSuggestion { Kind = SuggestionKind.Search, Title = typed, Subtitle = "Cerca in " + SearchScopeText.Text.ToLowerInvariant(),
                                                    Query = typed, Glyph = "", GlyphBrush = Secondary }.Highlight(typed));
            suggestLocal.AddRange(Store.Settings.RecentSearches
                .Where(q => q.Contains(typed, StringComparison.CurrentCultureIgnoreCase) && !q.Equals(typed, StringComparison.CurrentCultureIgnoreCase))
                .Take(3).Select(q => Recent(q, typed)));
            var provider = account.Provider;
            // Anche i contatti di Persone, con l'azienda; i gruppi no (la ricerca è per una persona).
            suggestPeople = AddressBook.Suggest(typed, 10).Where(p => p.Group == null).Take(8).Select(p =>
            {
                var person = new Address { Name = p.Name, Email = p.Email };
                // In tutti gli account la sintassi è quella di ogni account: la sceglie ChooseSuggestionAsync.
                return new SearchSuggestion { Kind = SuggestionKind.Person, Title = person.Display, Subtitle = p.Name.Length > 0 ? p.Detail : "",
                                              Query = across ? p.Email : PersonQuery(provider, p.Email), Person = person }.Highlight(typed);
            }).ToList();
        }
        suggestPending = typed.Length >= 2;
        if (suggestPending) _ = ServerSuggestionsAsync(suggestVersion);
        ShowSuggestions();
        Log.Time("suggerimenti: pannello aggiornato", clock);
    }

    static Brush Secondary => (Brush)(Application.Current.TryFindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray);

    static SearchSuggestion Recent(string query, string typed) =>
        new SearchSuggestion { Kind = SuggestionKind.Recent, Title = query, Query = query, Glyph = "", GlyphBrush = Secondary }.Highlight(typed);

    /// <summary>I messaggi da e per una persona, nella sintassi della ricerca dell'account.</summary>
    static string PersonQuery(IMailProvider provider, string email) => provider.Account.Kind switch
    {
        AccountKind.Gmail => $"from:{email} OR to:{email} OR cc:{email}",
        AccountKind.Microsoft => $"participants:{email}",
        _ => email, // IMAP: la ricerca guarda già mittente, destinatari e Cc
    };

    /// <summary>
    /// Gli allegati per nome: filename: in Gmail, attachment: in Outlook, una parola per volta. In IMAP le parole come sono:
    /// la ricerca nel testo comprende le intestazioni delle parti, dove sta il nome del file.
    /// </summary>
    static string FileQuery(IMailProvider provider, string typed) => string.Join(" ",
        typed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => provider.Account.Kind switch
        {
            AccountKind.Gmail => "filename:" + w,
            AccountKind.Microsoft => "attachment:" + w,
            _ => w,
        }));

    /// <summary>File e messaggi dal server, dopo la pausa nella scrittura, se nel frattempo non è cambiato nulla.</summary>
    async Task ServerSuggestionsAsync(int v)
    {
        await Task.Delay(SuggestPause);
        if (v != suggestVersion) return;
        var typed = SearchBox.Text.Trim();
        if (typed.Length < 2 || SuggestAccounts is not { Count: > 0 } list) return;
        var where = SearchesAccounts || searchAll ? null : folder?.Folder;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Per ogni account (uno solo, o tutti): i messaggi e, nella sua sintassi, i file.
        var searches = list.Select(a =>
        {
            var mailTask = SuggestSearchAsync(a.Provider, where, typed);
            var fileQuery = FileQuery(a.Provider, typed);
            return (Account: a, Mail: mailTask, FileQuery: fileQuery, Files: fileQuery == typed ? mailTask : SuggestSearchAsync(a.Provider, where, fileQuery));
        }).ToList();
        _ = Task.WhenAll(searches.SelectMany(s => new[] { s.Mail, s.Files }))
                .ContinueWith(t => { if (t.IsCompletedSuccessfully) Log.Time($"suggerimenti per \"{typed}\"{(list.Count > 1 ? $" in {list.Count} account" : "")}", clock); });
        try
        {
            var mails = await Task.WhenAll(searches.Select(s => Quietly(s.Account, s.Mail)));
            if (v != suggestVersion) return;
            suggestMails = searches.SelectMany((s, i) => mails[i].Select(m => (Message: m, s.Account)))
                                   .OrderByDescending(x => x.Message.Date).Take(8).Select(x => MailSuggestion(x.Message, typed, x.Account.Provider)).ToList();
            ShowSuggestions();
            var files = await Task.WhenAll(searches.Select(s => Quietly(s.Account, s.Files)));
            if (v != suggestVersion) return;
            suggestFiles = searches.SelectMany((s, i) => FileSuggestions(files[i], typed, s.FileQuery, s.Account.Provider))
                                   .OrderByDescending(f => f.Message!.Date).Take(8).ToList();
        }
        catch (OperationCanceledException)
        {
            // Suggerimenti superati da altre lettere, o pannello chiuso: niente da mostrare.
        }
        catch (Exception ex)
        {
            Log.Error("suggerimenti della ricerca", ex);
        }
        finally
        {
            foreach (var s in searches) _ = s.Files.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        if (v != suggestVersion) return;
        suggestPending = false;
        ShowSuggestions();
    }

    /// <summary>I risultati di un account; uno che non risponde non toglie i suggerimenti degli altri (annullata: si ferma tutto).</summary>
    static async Task<List<MessageSummary>> Quietly(AccountItem account, Task<List<MessageSummary>> search)
    {
        try { return await search; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error($"suggerimenti della ricerca di {account.Header}", ex);
            return new();
        }
    }

    /// <summary>
    /// La ricerca già chiesta con lo stesso testo, se è finita bene o è ancora in corso per un pannello non superato;
    /// altrimenti una nuova, che si ferma quando il pannello è superato (SuggestSearches).
    /// </summary>
    async Task<List<MessageSummary>> SuggestSearchAsync(IMailProvider provider, MailFolder? where, string query) =>
        (await suggestSearches.Start(SuggestSearches.Key(provider.Account.Id, where?.Id, query), cancel => provider.SearchAsync(where, query, cancel: cancel))).Messages;

    static SearchSuggestion MailSuggestion(MessageSummary m, string typed, IMailProvider provider) => new SearchSuggestion
    {
        Kind = SuggestionKind.Mail,
        Title = m.Subject.Length > 0 ? m.Subject : "(nessun oggetto)",
        Subtitle = $"{m.From.Display} · {MessageItem.Format(m.Date)}",
        Query = typed,
        Typed = typed,
        Message = m,
        Provider = provider,
        Glyph = "",
        GlyphBrush = Secondary,
    }.Highlight(typed);

    /// <summary>Gli allegati dei risultati il cui nome contiene tutte le parole scritte, uno per riga, con l'icona del tipo.</summary>
    static IEnumerable<SearchSuggestion> FileSuggestions(List<MessageSummary> messages, string typed, string query, IMailProvider provider)
    {
        var words = typed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var m in messages)
            foreach (var name in m.AttachmentNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!words.All(w => name.Contains(w, StringComparison.CurrentCultureIgnoreCase))) continue;
                var chip = new AttachmentChip(name);
                yield return new SearchSuggestion
                {
                    Kind = SuggestionKind.File,
                    Title = name,
                    Subtitle = $"{MessageItem.Format(m.Date)} · da {m.From.Display}",
                    Query = query,
                    Typed = typed,
                    Message = m,
                    Provider = provider,
                    Glyph = chip.Glyph,
                    GlyphBrush = chip.Brush,
                }.Highlight(words[0]);
            }
    }

    /// <summary>Le righe della scheda scelta; il pannello si apre se c'è qualcosa da mostrare.</summary>
    void ShowSuggestions()
    {
        var head = suggestLocal.Where(s => s.Kind == SuggestionKind.Search).ToList();
        var recent = suggestLocal.Where(s => s.Kind == SuggestionKind.Recent).ToList();
        var list = suggestTab switch
        {
            SuggestTab.Mail => head.Concat(suggestMails),
            SuggestTab.People => suggestPeople,
            SuggestTab.Files => suggestFiles,
            _ => head.Concat(recent).Concat(suggestFiles.Take(4)).Concat(suggestPeople.Take(3)).Concat(suggestMails.Take(4)),
        };
        var items = list.ToList();
        SuggestList.ItemsSource = items;
        bool waiting = suggestPending && suggestTab is not SuggestTab.People;
        SuggestStatus.Text = waiting ? "Cerco file e messaggi…" : items.Count == 0 ? "Nessun suggerimento" : "";
        SuggestStatus.Visibility = SuggestStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Senza nulla scritto il pannello serve solo per le ricerche recenti.
        bool empty = SearchBox.Text.Trim().Length == 0;
        SuggestTabs.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        SuggestPopup.IsOpen = items.Count > 0 || !empty;
    }

    void OnSuggestTab(object sender, RoutedEventArgs e)
    {
        if (SuggestList == null) return;
        suggestTab = sender == SuggestMailTab ? SuggestTab.Mail : sender == SuggestPeopleTab ? SuggestTab.People
                   : sender == SuggestFilesTab ? SuggestTab.Files : SuggestTab.All;
        ShowSuggestions();
    }

    /// <summary>Frecce per scorrere le righe, Invio per sceglierne una, Esc per chiudere il pannello.</summary>
    void OnSearchPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!SuggestPopup.IsOpen) return;
        int count = SuggestList.Items.Count;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                SuggestList.SelectedIndex = (SuggestList.SelectedIndex + 1) % count;
                SuggestList.ScrollIntoView(SuggestList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                SuggestList.SelectedIndex = SuggestList.SelectedIndex <= 0 ? count - 1 : SuggestList.SelectedIndex - 1;
                SuggestList.ScrollIntoView(SuggestList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter when SuggestList.SelectedItem is SearchSuggestion chosen:
                e.Handled = true;
                _ = ChooseSuggestionAsync(chosen);
                break;
            case Key.Escape:
                CloseSuggestions();
                e.Handled = true;
                break;
        }
    }

    void OnSuggestionClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: SearchSuggestion chosen })
        {
            e.Handled = true;
            _ = ChooseSuggestionAsync(chosen);
        }
    }

    /// <summary>
    /// Ricerca, ricerca recente e persona: si cerca. File e messaggio: si cerca anche, e fra i risultati si apre il loro
    /// messaggio, così la barra dei comandi e il riquadro lavorano come sempre sulla riga scelta.
    /// </summary>
    async Task ChooseSuggestionAsync(SearchSuggestion chosen)
    {
        // Il pannello si chiude senza fermare le ricerche in corso: quella con lo stesso testo la riusa la ricerca che parte
        // qui sotto (SearchAsync e SearchAccountsAsync fermano le altre).
        suggestVersion++;
        SuggestPopup.IsOpen = false;
        if (!HasList)
        {
            suggestSearches.CancelPending();
            return;
        }
        if (SearchesAccounts)
        {
            // In tutti gli account persone e file si cercano con la sintassi di ogni account.
            Func<IMailProvider, string> queryFor = chosen switch
            {
                { Kind: SuggestionKind.Person, Person: { } person } => p => PersonQuery(p, person.Email),
                { Kind: SuggestionKind.File } => p => FileQuery(p, chosen.Typed),
                _ => _ => chosen.Query,
            };
            var text = chosen.Kind == SuggestionKind.File ? chosen.Typed : chosen.Query;
            SetSearchText(text);
            await SearchAccountsAsync(text, queryFor);
        }
        else
        {
            SetSearchText(chosen.Query);
            await SearchAsync(chosen.Query, searchAll ? null : folder);
        }
        if (chosen.Message is { } m && rows.Rows.FirstOrDefault(r => (chosen.Provider == null || r.Provider == chosen.Provider)
                                                                     && r.Summary.Id == m.Id && r.Summary.FolderId == m.FolderId) is { } row)
        {
            MessageList.SelectedItem = row;
            MessageList.ScrollIntoView(row);
            // Un file: anche la sua anteprima, appena arriva il messaggio.
            if (chosen.Kind == SuggestionKind.File && S.ReadingPane != ReadingPanePosition.Off) Reader.PreviewWhenReady(chosen.Title);
        }
    }
}
