using System.Net;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Missiva;

/// <summary>
/// Gmail tramite IMAP e SMTP, con l'accesso di Google fatto nel browser (OAuth, vedi <see cref="GoogleAuth"/>).
/// Più connessioni IMAP per account, perché MailKit non accetta operazioni contemporanee sulla stessa (vedi main,
/// background, threads e drafts): aprire un messaggio non aspetta mai le anteprime, la ricerca della conversazione né il
/// salvataggio di una bozza.
/// </summary>
public sealed partial class GmailProvider(MailAccount account) : IMailProvider, IDisposable
{
    const int PageSize = MailLimits.PageSize;
    /// <summary>Per l'elenco: senza anteprima, che Gmail non fornisce e MailKit ricava dal testo (50 messaggi: 8 s contro 0,5).</summary>
    const MessageSummaryItems ListItems = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
        MessageSummaryItems.InternalDate | MessageSummaryItems.BodyStructure | MessageSummaryItems.GMailThreadId |
        MessageSummaryItems.GMailLabels | MessageSummaryItems.GMailMessageId | MessageSummaryItems.Size;
    /// <summary>Per pochi messaggi (i nuovi arrivi): con l'anteprima.</summary>
    const MessageSummaryItems SummaryItems = ListItems | MessageSummaryItems.PreviewText;

    sealed class Connection(string name) : IDisposable
    {
        public readonly string Name = name;
        public ImapClient Client { get; private set; } = new() { Timeout = MailLimits.GmailSilenceMs };
        public readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>Al posto del client non ancora collegato, quello già collegato e autenticato all'avvio (Preconnect).</summary>
        public void Adopt(ImapClient ready)
        {
            Client.Dispose();
            Client = ready;
        }

        /// <summary>Quante volte la connessione è stata buttata (Drop): chi la stava usando lo vede e riprova su quella nuova.</summary>
        public int Generation => Volatile.Read(ref generation);
        int generation;

        /// <summary>Finisce quando la connessione di adesso viene buttata (Drop): RunAsync smette di aspettarla.</summary>
        public Task Dropped => Volatile.Read(ref dropped).Task;
        TaskCompletionSource dropped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Dopo la ripresa del PC o un cambio di rete (DropConnections), come in ImapProvider: al posto del client un altro,
        /// non collegato, e quello di prima si chiude subito, senza saluto e senza aspettare chi lo sta usando, che smette di
        /// aspettarla e riprova sul client nuovo (A-04).
        /// </summary>
        public void Drop()
        {
            var old = Client;
            Client = new() { Timeout = old.Timeout };
            Interlocked.Increment(ref generation);
            Interlocked.Exchange(ref dropped, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            ImapSockets.Abort(old);
            try { old.Dispose(); } catch { }
        }

        /// <summary>Il LOGOUT in sottofondo, con cinque secondi di tetto, come ImapProvider: chi chiude il provider non lo aspetta.</summary>
        public void Dispose()
        {
            var client = Client;
            if (!client.IsConnected)
            {
                client.Dispose();
                return;
            }
            _ = Task.Run(() =>
            {
                // Prima la richiesta in corso su questa connessione, al più cinque secondi: il LOGOUT mandato in mezzo a un
                // comando ne prendeva la risposta, e quel comando restava fermo fino al tempo scaduto (audit dell'11 ottobre, C-53).
                bool free = Gate.Wait(TimeSpan.FromSeconds(MailLimits.CloseGraceSeconds));
                try
                {
                    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(MailLimits.LogoutSeconds));
                    client.Disconnect(true, limit.Token);
                }
                catch { }
                finally
                {
                    client.Dispose();
                    if (free) Gate.Release();
                }
            });
        }
    }

    /// <summary>Le connessioni principali aperte all'avvio, per account, che il provider prende alla prima richiesta.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<ImapClient?>> early = new();

    /// <summary>
    /// All'avvio, prima della finestra (App.OnStartup): la connessione principale di ogni account Gmail si apre in
    /// sottofondo, con il token (salvato o rinnovato) e l'accesso XOAUTH2, a cui Gmail risponde in circa un secondo. Il
    /// provider la prende alla prima richiesta, invece di cominciare solo quando la finestra carica gli account. Se non
    /// riesce, il provider si collega da sé come sempre.
    /// </summary>
    public static void Preconnect(IEnumerable<MailAccount> accounts)
    {
        if (!GoogleAuth.IsConfigured) return;
        foreach (var account in accounts.Where(a => a.Kind == AccountKind.Gmail && a.ProtectedRefreshToken != null).ToList())
            early[account.Id] = Task.Run(() => ConnectEarlyAsync(account));
    }

    static async Task<ImapClient?> ConnectEarlyAsync(MailAccount account)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var client = new ImapClient { Timeout = MailLimits.GmailSilenceMs };
        try
        {
            Network.Check(); // --senza-rete (solo Debug)
            await ImapSockets.ConnectAsync(client, "imap.gmail.com", 993, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(new SaslMechanismOAuth2(account.Email, await GoogleAuth.TokenAsync(account)));
            Log.Time($"collegamento IMAP principale in anticipo ({account.Email})", clock);
            return client;
        }
        catch (Exception ex)
        {
            Log.Write($"collegamento IMAP in anticipo di {account.Email} non riuscito ({ex.GetType().Name}): si collega il provider");
            client.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Tre connessioni, ognuna ferma sulla sua cartella: aprire una cartella (SELECT) costa 0,3 s sulle caselle grandi.
    /// main per quello che chiede l'utente, background per le anteprime, threads per le conversazioni e i messaggi di
    /// "Tutti i messaggi", così aprire un messaggio non fa saltare main avanti e indietro tra le cartelle.
    /// </summary>
    readonly Connection main = new("principale"), background = new("in sottofondo"), threads = new("delle conversazioni");
    /// <summary>
    /// Bozze: salvarne una con allegati ne riscarica e ricarica anche decine di MB, e intanto sulla principale aprire
    /// messaggi e cartelle aspettava. Si collega la prima volta che serve e poi resta, ferma sulle Bozze.
    /// </summary>
    readonly Connection drafts = new("delle bozze");
    /// <summary>Corpi già letti: riaprire un messaggio non richiede nulla a Gmail.</summary>
    readonly Dictionary<string, MessageBody> bodyCache = new();
    /// <summary>Chiavi di bodyCache dalla meno recente: si tengono solo gli ultimi corpi aperti.</summary>
    readonly List<string> bodyOrder = new();
    /// <summary>Corpi tenuti per riaprirli subito: pochi, perché con le immagini incorporate pesano.</summary>
    const int BodyCacheSize = MailLimits.BodyCacheSize;

    public MailAccount Account { get; } = account;
    public bool SupportsFocused => false;
    public bool PrefetchesMany => true;

    async Task<SaslMechanismOAuth2> CredentialsAsync() =>
#if DEBUG
        TestPort != null ? new(Account.Email, "prova") :
#endif
        new(Account.Email, await GoogleAuth.TokenAsync(Account));

#if DEBUG
    /// <summary>Prove con un server IMAP finto sul PC (Missiva.exe --prova-bozze): la sua porta, senza TLS e con un token finto.</summary>
    internal static int? TestPort;
#endif

    /// <summary>
    /// Verifica che Gmail accetti l'accesso via IMAP al momento dell'aggiunta dell'account. Con Google Workspace
    /// l'amministratore può aver spento IMAP per tutto il dominio: Gmail lo dice nel rifiuto, e qui si spiega.
    /// </summary>
    public static async Task TestAsync(string email, string accessToken)
    {
        using var client = new ImapClient { Timeout = MailLimits.GmailSilenceMs };
        await client.ConnectAsync("imap.gmail.com", 993, SecureSocketOptions.SslOnConnect);
        try
        {
            await client.AuthenticateAsync(new SaslMechanismOAuth2(email, accessToken));
        }
        catch (AuthenticationException ex) when (ImapDisabled(ex.Message))
        {
            throw new MailException(ImapOff(email), ex);
        }
        await client.DisconnectAsync(true);
    }

    /// <summary>Il rifiuto di Gmail quando IMAP è spento: "IMAP access is disabled for your domain", "not enabled for IMAP use".</summary>
    internal static bool ImapDisabled(string text) =>
        text.Contains("IMAP", StringComparison.OrdinalIgnoreCase) &&
        (text.Contains("disabled", StringComparison.OrdinalIgnoreCase) || text.Contains("not enabled", StringComparison.OrdinalIgnoreCase));

    internal static string ImapOff(string email) => AccountTypes.IsGmailDomain(email)
        ? T("Gmail non accetta l'accesso IMAP per questo account: attivalo nelle impostazioni di Gmail sul web (Inoltro e POP/IMAP), poi riprova.")
        : T("L'amministratore di Google Workspace ha spento l'accesso IMAP per {0}: può riattivarlo nella Console di amministrazione (App, Google Workspace, Gmail, Accesso utente finale, Accesso POP e IMAP). Senza IMAP Missiva non legge la posta.",
            AccountTypes.DomainOf(email));

    /// <param name="cancel">
    /// Si guarda solo prima di cominciare: una richiesta superata mentre aspettava la connessione non parte. Ai comandi di
    /// MailKit non si passa, perché interrotto a metà MailKit chiude la connessione (e la riapertura costa 1-2 s); solo
    /// gli scaricamenti che si annullano a mano (un allegato, il messaggio intero: AttachmentAsync, GetRawAsync senza
    /// limit) lo passano anche ai loro comandi, dentro l'azione.
    /// </param>
    /// <summary>
    /// Come RunAsync, per le letture (elenco, cartelle, corpi, nuovi messaggi, non letti, IDLE): anche un tempo scaduto si
    /// riprova una volta su una connessione nuova, come in ImapProvider.ReadAsync (A-02). Mai per una scrittura.
    /// </summary>
    Task<T> ReadAsync<T>(Func<ImapClient, Task<T>> action, Connection? on = null, CancellationToken cancel = default) => RunAsync(action, on, cancel, read: true);

    async Task<T> RunAsync<T>(Func<ImapClient, Task<T>> action, Connection? on = null, CancellationToken cancel = default, bool read = false)
    {
        var connection = on ?? main;
        ThrowIfClosed();
        var waiting = System.Diagnostics.Stopwatch.StartNew();
        await connection.Gate.WaitAsync(cancel);
        if (waiting.ElapsedMilliseconds > 20) Log.Time($"attesa della connessione {connection.Name}", waiting);
        try
        {
            cancel.ThrowIfCancellationRequested();
            ThrowIfClosed(); // chiuso mentre aspettava il suo turno
            for (int attempt = 0; ; attempt++)
            {
                // Dopo l'attesa, e a ogni tentativo: la richiesta di prima può aver preso la connessione aperta all'avvio
                // (Adopt), e DropConnections può aver cambiato il client mentre si usava quello di prima.
                var imap = connection.Client;
                int generation = connection.Generation;
                var gone = connection.Dropped;
                try
                {
                    async Task<T> AttemptAsync()
                    {
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        if (connection == main && !imap.IsConnected && early.TryRemove(Account.Id, out var pending) && await pending is { IsAuthenticated: true } ready)
                        {
                            connection.Adopt(ready); // collegata all'avvio, mentre si preparava la finestra
                            imap = ready;
                            Log.Time("attesa della connessione principale aperta in anticipo", clock);
                        }
                        bool connecting = !imap.IsAuthenticated;
                        if (!imap.IsConnected) Network.Check(); // --senza-rete (solo Debug)
#if DEBUG
                        if (!imap.IsConnected && TestPort is { } port) await ImapSockets.ConnectAsync(imap, "127.0.0.1", port, SecureSocketOptions.None);
#endif
                        if (!imap.IsConnected) await ImapSockets.ConnectAsync(imap, "imap.gmail.com", 993, SecureSocketOptions.SslOnConnect);
                        if (connecting) Log.Time($"collegamento IMAP {connection.Name}: connesso", clock);
                        if (!imap.IsAuthenticated)
                        {
                            var credentials = await CredentialsAsync();
                            Log.Time($"collegamento IMAP {connection.Name}: token pronto", clock);
                            await imap.AuthenticateAsync(credentials);
                        }
                        if (connecting) Log.Time($"collegamento IMAP {connection.Name}", clock);
                        return await action(imap);
                    }
                    var work = AttemptAsync();
                    // La connessione buttata mentre si aspettava (la ripresa del PC): non si aspetta più quella, che resta
                    // indietro e finisce da sola con un errore (osservato qui), e si riprova sulla nuova (ImapProvider.RunAsync).
                    if (await Task.WhenAny(work, gone) != work)
                    {
                        _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                        throw new IOException($"la connessione {connection.Name} è stata rifatta mentre aspettava una risposta");
                    }
                    return await work;
                }
                catch (AuthenticationException ex) when (ImapDisabled(ex.Message))
                {
                    throw new MailException(ImapOff(Account.Email), ex); // un altro token non serve
                }
                catch (AuthenticationException) when (attempt == 0)
                {
                    GoogleAuth.Forget(Account); // token scaduto o revocato: se ne chiede uno nuovo e si riprova
                }
                catch (AuthenticationException ex)
                {
                    throw new MailException(Lingua.T("Gmail ha rifiutato l'accesso di {0}: rifallo con Accedi di nuovo (Impostazioni, Account, Gestisci).", Account.Email), ex) { Fix = AccountFix.SignIn };
                }
                catch (Exception ex) when (!closed &&
                                           (attempt == 0 && (ex is IOException or ServiceNotConnectedException or ImapProtocolException ||
                                                             !cancel.IsCancellationRequested && read && ex is TimeoutException) ||
                                            !cancel.IsCancellationRequested && connection.Generation != generation))
                {
                    // Dopo una connessione buttata si riprova sempre, anche al secondo tentativo (ImapProvider.RunAsync).
                    // Connessione caduta (sospensione, cambio di rete): si riprova una volta da capo. Per le letture anche dopo
                    // un tempo scaduto (ReadAsync), che con Gmail sono due minuti: MailKit ha già chiuso la connessione. E con
                    // qualunque errore se intanto la connessione è stata buttata (DropConnections, la ripresa del PC).
                    if (connection.Generation != generation) Log.Write($"connessione {connection.Name} di {Account.Email} rifatta dopo la ripresa o un cambio di rete: riprovo su quella nuova");
                    else if (ex is TimeoutException) Log.Write($"tempo scaduto sulla connessione {connection.Name} di {Account.Email}: riprovo su una connessione nuova");
                    try { await imap.DisconnectAsync(false); } catch { }
                }
            }
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    Task RunAsync(Func<ImapClient, Task> action, Connection? on = null) => RunAsync(async c => { await action(c); return true; }, on);

    static async Task<IMailFolder> OpenAsync(ImapClient client, string folderId)
    {
        var folder = folderId.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? client.Inbox : await client.GetFolderAsync(folderId);
        if (!folder.IsOpen || folder.Access != FolderAccess.ReadWrite)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await folder.OpenAsync(FolderAccess.ReadWrite);
            Log.Time($"apertura di {folderId} ({folder.Count} messaggi)", clock);
        }
        return folder;
    }

    // ---------------------------------------------------------------- cartelle

    public Task<List<MailFolder>> GetFoldersAsync() => ReadAsync(async client =>
    {
#if DEBUG
        var clock = System.Diagnostics.Stopwatch.StartNew();
#endif
        // Cartelle e non letti con una sola richiesta (LIST-STATUS): una STATUS per cartella costava 4-7 secondi.
        var all = await client.GetFoldersAsync(client.PersonalNamespaces[0], StatusItems.Unread | StatusItems.Count);
#if DEBUG
        Log.Write($"tempi: cartelle e non letti {clock.ElapsedMilliseconds} ms");
#endif
        var result = new List<MailFolder>();

        // [Gmail] è solo un contenitore: Gmail lo segnala come inesistente, non come non selezionabile.
        static bool Selectable(IMailFolder f) => !f.Attributes.HasFlag(FolderAttributes.NoSelect) && !f.Attributes.HasFlag(FolderAttributes.NonExistent);

        void Add(IMailFolder? f, FolderKind kind, string name)
        {
            if (f == null || !Selectable(f)) return;
            int cut = f.FullName.LastIndexOf(f.DirectorySeparator);
            result.Add(new MailFolder { Id = f.FullName, Name = name, Kind = kind, Unread = f.Unread, Total = f.Count, ParentId = cut > 0 ? f.FullName[..cut] : null });
        }

        IMailFolder? Special(FolderAttributes a) => all.FirstOrDefault(f => f.Attributes.HasFlag(a));

        // I nomi delle cartelle speciali li dà Missiva, nella sua lingua (MailFolder.SpecialName), non quelli del server.
        static string Named(FolderKind kind) => MailFolder.SpecialShown(AccountKind.Gmail, kind);
        Add(all.FirstOrDefault(f => f.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) ?? client.Inbox, FolderKind.Inbox, Named(FolderKind.Inbox));
        Add(Special(FolderAttributes.Drafts), FolderKind.Drafts, Named(FolderKind.Drafts));
        Add(Special(FolderAttributes.Sent), FolderKind.Sent, Named(FolderKind.Sent));
        Add(Special(FolderAttributes.Trash), FolderKind.Deleted, Named(FolderKind.Deleted));
        Add(Special(FolderAttributes.All), FolderKind.Archive, Named(FolderKind.Archive));
        Add(Special(FolderAttributes.Junk), FolderKind.Junk, Named(FolderKind.Junk));
        Add(Special(FolderAttributes.Flagged), FolderKind.Other, T("Speciali"));

        // Le altre (etichette e sottocartelle) in ordine alfabetico, con il solo nome: l'albero lo costruisce l'elenco.
        var used = result.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in all.Where(f => !used.Contains(f.FullName) && Selectable(f)
                                         && !f.Attributes.HasFlag(FolderAttributes.Important) && !f.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                              .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            Add(f, FolderKind.Other, f.Name);

        // Le etichette dell'utente (non quelle di sistema sotto [Gmail]) sono anche le sue categorie.
        userLabels = result.Where(r => r.Kind == FolderKind.Other && r.ParentId != "[Gmail]").Select(r => r.Id).ToList();
        return result;
    });

    List<string>? userLabels;

    // ---------------------------------------------------------------- categorie (etichette)

    public async Task<List<Category>> GetCategoriesAsync()
    {
        if (userLabels == null) await GetFoldersAsync();
        return userLabels!.Select(l => new Category(l, CategoryPalette.PresetOf(Account.Id, l))).ToList();
    }

    public Task<Category> CreateCategoryAsync(string name, int preset) => RunAsync(async client =>
    {
        var root = client.GetFolder(client.PersonalNamespaces[0]);
        // MailKit restituisce null se, creata l'etichetta, il server non la elenca: prima ne usciva un errore di .NET senza
        // spiegazione (audit dell'11 ottobre, C-37). Come ImapProvider.CreateFolderAsync.
        var created = await root.CreateAsync(name.Trim(), true) ?? throw new MailException(T("Gmail non ha creato l'etichetta {0}.", name.Trim()));
        Store.Settings.CategoryColors[Account.Id + "|" + created.FullName] = preset;
        Store.Save();
        userLabels ??= new();
        userLabels.Add(created.FullName);
        return new Category(created.FullName, preset);
    });

    public async Task SetCategoryAsync(IReadOnlyList<MessageSummary> messages, string category, bool on)
    {
        await EachFolderAsync(messages, (_, f, uids, folderId) =>
        {
            InboxIdle.Expect(folderId, uids, InboxIdle.EchoOf.Any);
            return f.StoreAsync(uids, new StoreLabelsRequest(on ? StoreAction.Add : StoreAction.Remove, new[] { category }) { Silent = true });
        }, affectsUnread: false);
        foreach (var message in messages)
        {
            message.Categories.RemoveAll(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
            if (on) message.Categories.Add(category);
        }
    }

    static bool IsSystem(MailFolder f) => f.Id.Equals("INBOX", StringComparison.OrdinalIgnoreCase) || f.Id.StartsWith("[Gmail]", StringComparison.Ordinal) && f.Kind != FolderKind.Other
                                           || f.ParentId == "[Gmail]";

    public Task<MailFolder> CreateFolderAsync(string name, MailFolder? parent) => RunAsync(async client =>
    {
        var container = parent == null || IsSystem(parent) ? client.GetFolder(client.PersonalNamespaces[0]) : await client.GetFolderAsync(parent.Id);
        var created = await container.CreateAsync(name.Trim(), true) ?? throw new MailException(T("Gmail non ha creato la cartella {0}.", name.Trim()));
        return new MailFolder { Id = created.FullName, Name = created.Name, Kind = FolderKind.Other, ParentId = container.FullName.Length > 0 ? container.FullName : null };
    });

    public Task<string> RenameFolderAsync(MailFolder folder, string name) => RunAsync(async client =>
    {
        var f = await client.GetFolderAsync(folder.Id);
        if (f.IsOpen) await f.CloseAsync();
        // La cartella che la contiene: quella che MailKit conosce, altrimenti chiesta al server con il suo percorso (il primo
        // livello per una cartella che non sta dentro nessuna). Mai il primo livello per una cartella annidata: la sposterebbe.
        int cut = f.FullName.LastIndexOf(f.DirectorySeparator);
        var parent = f.ParentFolder ?? (cut > 0 ? await client.GetFolderAsync(f.FullName[..cut]) : client.GetFolder(client.PersonalNamespaces[0]));
        await f.RenameAsync(parent, name.Trim());
        return f.FullName;
    });

    public Task<string> MoveFolderAsync(MailFolder folder, MailFolder? parent) => RunAsync(async client =>
    {
        var f = await client.GetFolderAsync(folder.Id);
        if (f.IsOpen) await f.CloseAsync();
        var target = parent == null ? client.GetFolder(client.PersonalNamespaces[0]) : await client.GetFolderAsync(parent.Id);
        await f.RenameAsync(target, f.Name);
        return f.FullName;
    });

    /// <summary>
    /// Mai Tutti i messaggi e Speciali, che contengono anche i messaggi delle altre cartelle (Tutti i messaggi l'intera
    /// casella): il menu non lo propone, e qui si rifiuta comunque.
    /// </summary>
    public Task EmptyFolderAsync(MailFolder folder, MailFolder? trash) => InboxIdle.OwnAsync(Account.Id, () => RunAsync(async client =>
    {
        var f = await OpenAsync(client, folder.Id);
        if (folder.Kind == FolderKind.Archive || f.Attributes.HasFlag(FolderAttributes.All) || f.Attributes.HasFlag(FolderAttributes.Flagged))
            throw new MailException(T("In Gmail \"{0}\" non si svuota: contiene anche i messaggi delle altre cartelle.", folder.Name));
        if (f.Count == 0) return;
        var all = await f.SearchAsync(SearchQuery.All);
        InboxIdle.Expect(folder.Id, all, InboxIdle.EchoOf.Removed);
        InboxIdle.Expect(folder.Id, all, InboxIdle.EchoOf.Any);
        if (folder.Kind is FolderKind.Deleted or FolderKind.Junk || trash == null)
        {
            await f.AddFlagsAsync(all, MessageFlags.Deleted, true);
            await f.ExpungeAsync();
        }
        else
        {
            await f.MoveToAsync(all, await client.GetFolderAsync(trash.Id));
        }
    }));

    public Task MarkAllReadAsync(MailFolder folder) => InboxIdle.OwnAsync(Account.Id, () => RunAsync(async client =>
    {
        var f = await OpenAsync(client, folder.Id);
        var unread = await f.SearchAsync(SearchQuery.NotSeen);
        InboxIdle.Expect(folder.Id, unread, InboxIdle.EchoOf.Seen, true);
        if (unread.Count > 0) await f.AddFlagsAsync(unread, MessageFlags.Seen, true);
    }));

    public Task DeleteFolderAsync(MailFolder folder) => RunAsync(async client =>
    {
        var f = await client.GetFolderAsync(folder.Id);
        if (f.IsOpen) await f.CloseAsync();
        await f.DeleteAsync();
    });

    // ---------------------------------------------------------------- messaggi

    public bool SortsOnServer(SortBy sort) => sort == SortBy.Date; // Gmail non ha il comando SORT di IMAP

    /// <summary>Risultati delle ricerche per filtro o data, per sfogliarne le pagine senza ripeterle.</summary>
    readonly Dictionary<string, List<UniqueId>> searches = new();

    /// <summary>
    /// Si tiene solo l'ultima ricerca di ogni tipo (filtro dell'elenco, ricerca): con una parola comune in "Tutti i
    /// messaggi" sono decine di migliaia di UID, e quelle vecchie non servono (tornandoci si rifanno).
    /// </summary>
    void Remember(string key, List<UniqueId> uids)
    {
        bool search = key.StartsWith("cerca|", StringComparison.Ordinal);
        foreach (var old in searches.Keys.Where(k => k.StartsWith("cerca|", StringComparison.Ordinal) == search).ToList()) searches.Remove(old);
        searches[key] = uids;
    }

    public Task<MessagePage> GetMessagesAsync(MailFolder folder, bool? focused, string? next, ListOptions? options = null) => ReadAsync(async client =>
    {
        options ??= ListOptions.Default;
        if (next == null) WarmThreads();
        var f = next == null ? await ImapProvider.OpenFreshAsync(client, folder.Id, OpenAsync) : await OpenAsync(client, folder.Id);
        if (options.Filter != MessageFilter.All || options.UpTo != null) return await FilteredPageAsync(client, f, folder, next, options);
        if (options.Ascending) return await OldestFirstPageAsync(f, folder, next);

        int end = next != null ? int.Parse(next) : f.Count - 1;
        if (end < 0) return new MessagePage();
        int start = Math.Max(0, end - PageSize + 1);
#if DEBUG
        var clock = System.Diagnostics.Stopwatch.StartNew();
#endif
        var items = await f.FetchAsync(start, end, MimeOptions.ListRequest(ListItems));
#if DEBUG
        Log.Write($"tempi: {items.Count} messaggi di {folder.Name} {clock.ElapsedMilliseconds} ms");
#endif
        return new MessagePage
        {
            // Nell'ordine della data mostrata (INTERNALDATE), come Outlook: un messaggio arrivato nella cartella dopo (spostato,
            // tornato da Posponi) non sta fra due più recenti. A parità, l'ordine di arrivo.
            Messages = items.OrderByDescending(i => i.Index).Select(i => Summary(i, folder.Id)).OrderByDescending(m => m.Instant).ToList(),
            Next = start > 0 ? (start - 1).ToString() : null,
        };
    });

    /// <summary>Dal più vecchio: le pagine partono dall'inizio della cartella.</summary>
    async Task<MessagePage> OldestFirstPageAsync(IMailFolder f, MailFolder folder, string? next)
    {
        int start = next != null ? int.Parse(next) : 0;
        if (start >= f.Count) return new MessagePage();
        int end = Math.Min(f.Count - 1, start + PageSize - 1);
        var items = await f.FetchAsync(start, end, MimeOptions.ListRequest(ListItems));
        return new MessagePage
        {
            Messages = items.OrderBy(i => i.Index).Select(i => Summary(i, folder.Id)).OrderBy(m => m.Instant).ToList(),
            Next = end < f.Count - 1 ? (end + 1).ToString() : null,
        };
    }

    /// <summary>
    /// Filtri e "Vai a" con la ricerca di IMAP su tutta la cartella; i risultati si sfogliano a pagine di 50, in ordine
    /// di arrivo (gli UID crescono con l'arrivo), dal più recente o dal più vecchio.
    /// </summary>
    async Task<MessagePage> FilteredPageAsync(ImapClient client, IMailFolder f, MailFolder folder, string? next, ListOptions options)
    {
        var key = folder.Id + "|" + options;
        if (next == null || !searches.TryGetValue(key, out var uids))
        {
            SearchQuery query = SearchQuery.All;
            SearchQuery And(SearchQuery q) => ReferenceEquals(query, SearchQuery.All) ? q : query.And(q);
            if (options.Filter == MessageFilter.Unread) query = And(SearchQuery.NotSeen);
            if (options.Filter == MessageFilter.Flagged) query = And(SearchQuery.Flagged);
            if (options.Filter == MessageFilter.Attachments)
                query = And(client.Capabilities.HasFlag(ImapCapabilities.GMailExt1) ? SearchQuery.GMailRawSearch("has:attachment") : SearchQuery.HeaderContains("Content-Type", "multipart/mixed"));
            if (options.UpTo is { } upTo) query = And(SearchQuery.DeliveredBefore(upTo.Date.AddDays(1)));
#if DEBUG
            var clock = System.Diagnostics.Stopwatch.StartNew();
#endif
            uids = (await f.SearchAsync(query)).OrderBy(u => u.Id).ToList();
#if DEBUG
            Log.Write($"tempi: ricerca {options} in {folder.Name}: {uids.Count} risultati in {clock.ElapsedMilliseconds} ms");
#endif
            if (!options.Ascending) uids.Reverse();
            Remember(key, uids);
        }
        int offset = next != null ? int.Parse(next) : 0;
        var slice = uids.Skip(offset).Take(PageSize).ToList();
        if (slice.Count == 0) return new MessagePage();
        var items = await f.FetchAsync(slice, MimeOptions.ListRequest(ListItems));
        var ordered = options.Ascending ? items.OrderBy(i => i.UniqueId.Id) : items.OrderByDescending(i => i.UniqueId.Id);
        return new MessagePage
        {
            Messages = ordered.Select(i => Summary(i, folder.Id)).ToList(),
            Next = offset + PageSize < uids.Count ? (offset + PageSize).ToString() : null,
        };
    }

    public Task<Dictionary<string, string>> GetPreviewsAsync(MailFolder folder, IReadOnlyList<MessageSummary> messages) => ReadAsync(async client =>
    {
        var result = new Dictionary<string, string>();
        if (messages.Count == 0) return result;
        var f = await ExamineAsync(client, folder.Id);
        var items = await f.FetchAsync(messages.Select(Uid).ToList(), MessageSummaryItems.UniqueId | MessageSummaryItems.PreviewText);
        foreach (var i in items) result[i.UniqueId.Id.ToString()] = Clean(i.PreviewText);
        return result;
    }, background);

    /// <summary>Sulla connessione in sottofondo, che legge soltanto: la cartella in sola lettura (EXAMINE), se non è già aperta.</summary>
    static async Task<IMailFolder> ExamineAsync(ImapClient client, string folderId)
    {
        var f = folderId.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? client.Inbox : await client.GetFolderAsync(folderId);
        if (!f.IsOpen) await f.OpenAsync(FolderAccess.ReadOnly);
        return f;
    }

    static string Clean(string? preview) => MailHtml.PlainPreview(preview);

    /// <summary>
    /// In tutte le cartelle si cerca in "Tutti i messaggi", che le contiene tutte (tranne Spam e Cestino, come nella
    /// ricerca di Gmail), sulla connessione che ci resta ferma; la cartella di ogni risultato viene dalle sue etichette.
    /// Senza anteprime, che costano 8 s ogni 50 messaggi: arrivano dopo, come per l'elenco.
    /// </summary>
    public Task<MessagePage> SearchAsync(MailFolder? folder, string query, string? next = null, CancellationToken cancel = default) => ReadAsync(async client =>
    {
        var folderId = folder?.Id ?? await AllMailAsync(client) ?? "INBOX";
        var f = await OpenAsync(client, folderId);
        var key = "cerca|" + folderId + "|" + query;
        if (next == null || !searches.TryGetValue(key, out var uids))
        {
            // I filtri della ricerca (SearchTerms) nella sintassi di Gmail; il resto del testo com'è.
            var terms = SearchTerms.Parse(query);
            var words = terms.Text.Trim();
            SearchQuery search = client.Capabilities.HasFlag(ImapCapabilities.GMailExt1)
                ? SearchQuery.GMailRawSearch(terms.ToGmail()) // stessa sintassi della ricerca di Gmail
                : ImapProvider.Criteria(words.Length > 0 ? SearchQuery.SubjectContains(words).Or(SearchQuery.FromContains(words)).Or(SearchQuery.BodyContains(words)) : SearchQuery.NotDeleted, terms);
            uids = (await f.SearchAsync(search)).OrderByDescending(u => u.Id).ToList();
            Remember(key, uids);
        }
        int offset = next != null ? int.Parse(next) : 0;
        var slice = uids.Skip(offset).Take(PageSize).ToList();
        if (slice.Count == 0) return new MessagePage();
        var items = await f.FetchAsync(slice, MimeOptions.ListRequest(ListItems));
        return new MessagePage
        {
            Messages = items.OrderByDescending(i => i.InternalDate).Select(i =>
            {
                var m = Summary(i, folderId);
                if (folder == null) (m.FolderName, m.LabelKind) = FolderOf(i.GMailLabels);
                return m;
            }).ToList(),
            Next = offset + PageSize < uids.Count ? (offset + PageSize).ToString() : null,
        };
    }, folder == null ? threads : main, cancel);

    /// <summary>
    /// La cartella di un messaggio di "Tutti i messaggi", dalle sue etichette, con i nomi delle cartelle di Missiva e il
    /// tipo (per la riga dell'elenco: in Posta inviata e Bozze si mostrano i destinatari).
    /// </summary>
    static (string Name, FolderKind? Kind) FolderOf(IList<string>? labels)
    {
        labels ??= [];
        foreach (var (label, kind) in new[] { (@"\Inbox", FolderKind.Inbox), (@"\Sent", FolderKind.Sent), (@"\Draft", FolderKind.Drafts), (@"\Trash", FolderKind.Deleted), (@"\Spam", FolderKind.Junk) })
            if (labels.Contains(label, StringComparer.OrdinalIgnoreCase)) return (MailFolder.SpecialShown(AccountKind.Gmail, kind), kind);
        var mine = labels.FirstOrDefault(l => !l.StartsWith('\\'));
        return mine != null ? (mine[(mine.LastIndexOf('/') + 1)..], FolderKind.Other) : (MailFolder.SpecialShown(AccountKind.Gmail, FolderKind.Archive), FolderKind.Archive);
    }

    /// <summary>
    /// Ogni due minuti, per le notifiche. La ricerca di IMAP va per giorni: dei non letti da ieri si chiede prima solo la
    /// data, che costa poco, e l'anteprima (lenta) solo di quelli davvero nuovi, di solito nessuno. Prima si ricalcolavano
    /// ogni volta le anteprime di tutti, 2-3 s sulla connessione principale. Ora gira su quella in sottofondo, in sola
    /// lettura: aprire un messaggio o una cartella non la aspetta.
    /// </summary>
    public Task<List<MessageSummary>> GetNewAsync(DateTime sinceUtc) => ReadAsync(async client =>
    {
        var f = await ImapProvider.FreshInboxAsync(client); // aggiornata con NOOP: vedi FreshInboxAsync
        var uids = await f.SearchAsync(SearchQuery.NotSeen.And(SearchQuery.DeliveredAfter(sinceUtc.ToLocalTime().Date.AddDays(-1))));
        if (uids.Count == 0) return new List<MessageSummary>();
        // Le date già chieste non si richiedono (ImapProvider.ArrivalDates): con i soli non letti già annunciati, nessuna FETCH.
        var fresh = await arrivalDates.AfterAsync(f, uids.Reverse().Take(20).ToList(), sinceUtc);
        if (fresh.Count == 0) return new List<MessageSummary>();
        var items = await f.FetchAsync(fresh, MimeOptions.ListRequest(SummaryItems));
        return items.Select(i => Summary(i, "INBOX")).ToList();
    }, background);

    readonly ImapProvider.ArrivalDates arrivalDates = new();

    /// <summary>
    /// Per le notifiche: i nuovi da leggere per UID dopo il mark (ImapProvider.NewSinceAsync, audit di affidabilità del 25
    /// ottobre 2026, A-06), sulla connessione in sottofondo in sola lettura, con l'anteprima solo di quelli.
    /// </summary>
    public Task<Arrivals> GetNewAsync(string? mark, DateTime sinceUtc) => ReadAsync(async client =>
    {
        var f = await ImapProvider.FreshInboxAsync(client); // aggiornata con NOOP: vedi FreshInboxAsync
        var (fresh, next) = await ImapProvider.NewSinceAsync(f, mark, sinceUtc, arrivalDates);
        if (fresh.Count == 0) return new Arrivals([], next);
        var items = await f.FetchAsync(fresh, MimeOptions.ListRequest(SummaryItems));
        return new Arrivals(ImapProvider.Recent(items, sinceUtc).Select(i => Summary(i, "INBOX")).ToList(), next);
    }, background);

    /// <summary>
    /// Le regole di Missiva: i messaggi arrivati dopo il mark, letti o no e senza il limite dei 20 delle notifiche
    /// (ImapProvider.ArrivedAsync), sulla connessione in sottofondo in sola lettura.
    /// </summary>
    public Task<Arrivals?> GetArrivedAsync(string? mark, DateTime sinceUtc) => ReadAsync(async client =>
    {
        var f = await ImapProvider.FreshInboxAsync(client);
        var (items, next) = await ImapProvider.ArrivedAsync(f, mark, sinceUtc, MimeOptions.ListRequest(ListItems));
        return (Arrivals?)new Arrivals(items.Select(i => Summary(i, "INBOX")).ToList(), next);
    }, background);

    /// <summary>
    /// Dopo GetNewAsync, sulla stessa connessione dove la Posta in arrivo è già aperta: una SEARCH che restituisce solo il
    /// numero (ESEARCH, che Gmail ha), non gli UID di tutti i non letti.
    /// </summary>
    /// <summary>
    /// Lavoro senza rete: i messaggi della Posta in arrivo degli ultimi giorni, dal più recente, con busta, struttura e
    /// dimensione (senza anteprime, che costano), sulla connessione in sottofondo con EXAMINE: poi PrefetchAsync ne scarica i
    /// corpi, uno alla volta.
    /// </summary>
    public Task<List<MessageSummary>> GetRecentAsync(MailFolder inbox, DateTime sinceUtc, int max) => ReadAsync(async client =>
    {
        var f = client.Inbox;
        if (!f.IsOpen) await f.OpenAsync(FolderAccess.ReadOnly);
        var uids = await f.SearchAsync(SearchQuery.DeliveredAfter(sinceUtc.ToLocalTime().Date.AddDays(-1)));
        if (uids.Count == 0) return new List<MessageSummary>();
        var items = await f.FetchAsync(uids.Reverse().Take(max).ToList(), ListItems);
        return items.Where(i => i.InternalDate?.UtcDateTime >= sinceUtc).OrderByDescending(i => i.InternalDate).Select(i => Summary(i, "INBOX")).ToList();
    }, background);

    /// <summary>
    /// Lo stesso, ma solo quello che serve a BodyCache.Has (UID, X-GM-MSGID, segni e data): la copia degli ultimi 30 giorni
    /// rileggeva ogni mezz'ora busta e struttura di 400 messaggi (0,7-5,3 s, 360-530 ms di CPU) per trovarli già tutti sul PC.
    /// </summary>
    public Task<List<MessageSummary>> GetRecentKeysAsync(MailFolder inbox, DateTime sinceUtc, int max) => ReadAsync(async client =>
    {
        var f = client.Inbox;
        if (!f.IsOpen) await f.OpenAsync(FolderAccess.ReadOnly);
        var uids = await f.SearchAsync(SearchQuery.DeliveredAfter(sinceUtc.ToLocalTime().Date.AddDays(-1)));
        if (uids.Count == 0) return new List<MessageSummary>();
        var items = await f.FetchAsync(uids.Reverse().Take(max).ToList(),
            MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate | MessageSummaryItems.Flags | MessageSummaryItems.GMailMessageId);
        return items.Where(i => i.InternalDate?.UtcDateTime >= sinceUtc).OrderByDescending(i => i.InternalDate).Select(i => Summary(i, "INBOX")).ToList();
    }, background);

    /// <summary>Busta e struttura dei soli messaggi dati (quelli che mancano alla copia su disco), per scaricarne i corpi.</summary>
    public Task<List<MessageSummary>> GetRecentAsync(MailFolder inbox, IReadOnlyList<MessageSummary> keys) => ReadAsync(async client =>
    {
        if (keys.Count == 0) return new List<MessageSummary>();
        var f = client.Inbox;
        if (!f.IsOpen) await f.OpenAsync(FolderAccess.ReadOnly);
        var items = await f.FetchAsync(keys.Select(Uid).ToList(), ListItems);
        return items.OrderByDescending(i => i.InternalDate).Select(i => Summary(i, "INBOX")).ToList();
    }, background);

    public Task<int> GetInboxUnreadAsync() => ReadAsync(async client =>
    {
        var f = await ImapProvider.FreshInboxAsync(client, counting: true);
        return client.Capabilities.HasFlag(ImapCapabilities.ESearch)
            ? (await f.SearchAsync(SearchOptions.Count, SearchQuery.NotSeen)).Count
            : (await f.SearchAsync(SearchQuery.NotSeen)).Count;
    }, background);

    /// <summary>
    /// Buste (ENVELOPE, con A e Cc) degli ultimi messaggi della Posta inviata, sulla connessione in sottofondo in sola
    /// lettura: nessuna richiesta dell'utente la aspetta.
    /// </summary>
    public Task<List<SentRecipients>> GetSentRecipientsAsync(int max) => ReadAsync(async client =>
    {
        var sent = (await client.GetFoldersAsync(client.PersonalNamespaces[0])).FirstOrDefault(f => f.Attributes.HasFlag(FolderAttributes.Sent));
        if (sent == null) return new List<SentRecipients>();
        if (!sent.IsOpen) await sent.OpenAsync(FolderAccess.ReadOnly);
        if (sent.Count == 0) return new List<SentRecipients>();
        var items = await sent.FetchAsync(Math.Max(0, sent.Count - max), -1, MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate);
        static IEnumerable<Address> People(InternetAddressList? list) =>
            list?.Mailboxes.Select(a => new Address { Name = a.Name ?? "", Email = a.Address }) ?? [];
        return items.Select(i => new SentRecipients((i.InternalDate ?? i.Envelope?.Date ?? DateTimeOffset.MinValue).LocalDateTime,
            People(i.Envelope?.To).Concat(People(i.Envelope?.Cc)).ToList())).ToList();
    }, background);

    /// <summary>Struttura, destinatari e dimensione dei messaggi elencati: aprirli poi costa una richiesta in meno.</summary>
    readonly SummaryCache known = new();

    MessageSummary Summary(IMessageSummary i, string folderId)
    {
        if (i.Body != null && i.Envelope != null) known[folderId + "|" + i.UniqueId.Id] = i;
        var from = i.Envelope?.From.Mailboxes.FirstOrDefault();
        return new MessageSummary
        {
            Id = i.UniqueId.Id.ToString(),
            FolderId = folderId,
            From = new Address { Name = from?.Name ?? "", Email = from?.Address ?? "" },
            Subject = i.Envelope?.Subject ?? "",
            Preview = Clean(i.PreviewText),
            Date = (i.InternalDate ?? i.Envelope?.Date ?? DateTimeOffset.MinValue).LocalDateTime,
            IsRead = i.Flags?.HasFlag(MessageFlags.Seen) ?? true,
            IsFlagged = i.Flags?.HasFlag(MessageFlags.Flagged) ?? false,
            IsDraft = i.Flags?.HasFlag(MessageFlags.Draft) ?? false,
            HasAttachments = i.Attachments?.Any() ?? false,
            ConversationId = i.GMailThreadId?.ToString() ?? "",
            StableId = i.GMailMessageId?.ToString() ?? "",
            AttachmentNames = i.Attachments?.Select(a => a.FileName ?? "allegato").ToList() ?? new(),
            // Le etichette di sistema (\Inbox, \Important, \Starred...) cominciano con la barra rovesciata.
            Categories = i.GMailLabels?.Where(l => !l.StartsWith('\\')).ToList() ?? new(),
            // Già nella busta (ENVELOPE) che l'elenco scarica: niente richieste in più.
            To = i.Envelope?.To.Mailboxes.Take(10).Select(a => new Address { Name = a.Name ?? "", Email = a.Address }).ToList() ?? new(),
            ReplyTo = Address.ReplyToOf(i.Envelope?.ReplyTo.Mailboxes.Select(a => new Address { Name = a.Name ?? "", Email = a.Address }) ?? [],
                                        new Address { Email = from?.Address ?? "" }),
            IsImportant = MimeOptions.IsImportant(i.Headers),
        };
    }

    string? allMailFolder;

    async Task<string?> AllMailAsync(ImapClient client) =>
        allMailFolder ??= (await client.GetFoldersAsync(client.PersonalNamespaces[0]))
            .FirstOrDefault(f => f.Attributes.HasFlag(FolderAttributes.All))?.FullName;

    bool threadsWarm;

    /// <summary>La connessione delle conversazioni si prepara mentre arriva l'elenco: il primo messaggio aperto non la aspetta.</summary>
    void WarmThreads()
    {
        if (threadsWarm) return;
        threadsWarm = true;
        _ = RunAsync(async client => { if (await AllMailAsync(client) is { } all) await OpenAsync(client, all); }, threads)
            .ContinueWith(t => Log.Error("connessione delle conversazioni", t.Exception!), TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>I messaggi di "Tutti i messaggi" (quelli delle conversazioni) passano dalla connessione che ci resta ferma.</summary>
    Connection ConnectionFor(MessageSummary m) => On(m.FolderId);

    Connection On(string folderId) => allMailFolder != null && folderId == allMailFolder ? threads : main;

    /// <summary>
    /// Senza anteprime, come l'elenco: calcolarle costa (8 s ogni 50 messaggi) e intanto la connessione delle conversazioni
    /// restava occupata, anche per aprire i messaggi di "Tutti i messaggi". Il riquadro le chiede dopo, in sottofondo.
    /// Mai sulla connessione del corpo, che altrimenti la aspetta o la fa aspettare: di solito su quella delle conversazioni
    /// (il corpo va sulla principale); per i messaggi di "Tutti i messaggi", cioè i risultati della ricerca in tutte le
    /// cartelle, il cui corpo va su quella delle conversazioni, sulla principale, che durante la ricerca non serve (la prima
    /// volta apre "Tutti i messaggi", 0,2-0,3 s, mentre arriva il corpo). Prima la conversazione di un risultato arrivava
    /// dopo il suo corpo, fino a 1,3 s dal clic.
    /// </summary>
    public Task<List<MessageSummary>> GetConversationAsync(MessageSummary message, CancellationToken cancel = default) => ReadAsync(async client =>
    {
        if (!ulong.TryParse(message.ConversationId, out var thread)) return new List<MessageSummary> { message };
        // In Gmail la conversazione intera (comprese le tue risposte) sta in "Tutti i messaggi".
        if (await AllMailAsync(client) is not { } all) return new List<MessageSummary> { message };
        var f = await OpenAsync(client, all);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var uids = await f.SearchAsync(SearchQuery.GMailThreadId(thread));
        if (uids.Count <= 1) return new List<MessageSummary> { message }; // c'è solo questo messaggio: niente da scaricare
        var items = await f.FetchAsync(uids.Reverse().Take(50).ToList(), MimeOptions.ListRequest(ListItems));
        Log.Time($"conversazione cercata e scaricata ({items.Count} messaggi)", clock);
        return items.Select(i => Summary(i, all)).OrderBy(m => m.Instant).ToList();
    }, ConnectionFor(message) == threads ? main : threads, cancel);

    static UniqueId Uid(MessageSummary m) => new(uint.Parse(m.Id));

    /// <summary>Sotto questa dimensione il messaggio si scarica intero: una richiesta sola, invece di una per parte.</summary>
    const uint SmallMessage = MailLimits.SmallMessage;

    /// <summary>
    /// Dai corpi già letti o scaricati in anticipo, se c'è; altrimenti dal server, sulla connessione ferma sulla cartella
    /// del messaggio.
    /// </summary>
    public async Task<MessageBody> GetBodyAsync(MessageSummary message, CancellationToken cancel = default)
    {
        var key = KeyOf(message);
        if (Cached(key) is { } cached) return cached;
        if (Downloading(key) is { } coming)
        {
            // Lo sta già scaricando l'anticipo (PrefetchAsync), su un'altra connessione: meglio aspettare quello.
            try { return await coming.WaitAsync(cancel); }
            catch (Exception) when (!cancel.IsCancellationRequested) { } // non riuscito: si chiede come sempre
        }
        return await ReadAsync(async client =>
        {
            if (Cached(key) is { } body) return body;
            try { return await DownloadAsync(OpenAsync(client, message.FolderId), message, key, early: false); }
            catch (MessageNotFoundException)
            {
                // Appena arrivato: la connessione, ferma sulla cartella, non lo vede finché Gmail non glielo annuncia, e lo
                // annuncia con NOOP (come per i nuovi messaggi, ImapProvider.FreshInboxAsync). Giro D1: un clic su un
                // messaggio arrivato un attimo prima dava errore.
                await client.NoOpAsync(cancel);
                return await DownloadAsync(OpenAsync(client, message.FolderId), message, key, early: false);
            }
        }, ConnectionFor(message), cancel);
    }

    static string KeyOf(MessageSummary m) => m.FolderId + "|" + m.Id;

    public MessageBody? KeptBody(MessageSummary message)
    {
        lock (bodyCache) return bodyCache.GetValueOrDefault(KeyOf(message));
    }

    /// <summary>
    /// Dalla copia su disco anche in linea: un messaggio di Gmail non cambia (UID e X-GM-MSGID nel nome del file). Solo con
    /// la struttura arrivata con l'elenco e senza inviti, che la copia non tiene. Ogni avvio riscaricava i primi dieci
    /// della Posta in arrivo (2,4-3,7 s) e un clic fuori dai 20 corpi in memoria costava 290-440 ms invece di 40-70.
    /// </summary>
    public MessageBody? SavedBody(MessageSummary message) => OnDisk(message) ? BodyCache.LoadOnline(Account, message) : null;

    /// <summary>Come in ImapProvider: le sole intestazioni di "Annulla iscrizione", in sottofondo con EXAMINE.</summary>
    public Task<ListHeaders?> GetListHeadersAsync(MessageSummary message, CancellationToken cancel = default) => ReadAsync(async client =>
    {
        var headers = await SenderCheck.FetchIfThereAsync(await ExamineAsync(client, message.FolderId), Uid(message));
        return headers == null ? null : new ListHeaders(headers["List-Unsubscribe"], headers["List-Unsubscribe-Post"]);
    }, background, cancel);

    bool OnDisk(MessageSummary message) =>
        message.StableId.Length > 0 && known.TryGetValue(KeyOf(message), out var s) && !Invitation.MayHave(s) && BodyCache.Has(Account, message);

    /// <summary>Il corpo già letto, che diventa il più recente fra quelli tenuti.</summary>
    MessageBody? Cached(string key)
    {
        lock (bodyCache)
        {
            if (!bodyCache.TryGetValue(key, out var body)) return null;
            bodyOrder.Remove(key);
            bodyOrder.Add(key);
            return body;
        }
    }

    Task<MessageBody>? Downloading(string key)
    {
        lock (bodyCache) return downloading.GetValueOrDefault(key);
    }

    /// <summary>
    /// Corpi che si stanno scaricando, su qualunque connessione: chi chiede lo stesso messaggio (il clic su uno scaricato in
    /// anticipo, l'anticipo su uno appena aperto) aspetta quello invece di chiederlo di nuovo.
    /// </summary>
    readonly Dictionary<string, Task<MessageBody>> downloading = new();

    /// <summary>
    /// Il corpo, fra quelli tenuti in memoria, dalla cartella che si sta aprendo; intanto è in downloading, già prima che la
    /// cartella sia aperta (EXAMINE o SELECT, fino a 0,3 s): un clic in quel momento aspetta questo invece di chiederlo di
    /// nuovo su un'altra connessione.
    /// </summary>
    async Task<MessageBody> DownloadAsync(Task<IMailFolder> folder, MessageSummary message, string key, bool early)
    {
        var download = ReadBodyAsync(folder, message, key, early);
        lock (bodyCache) downloading[key] = download;
        try
        {
            var body = await download;
            lock (bodyCache)
            {
                if (bodyCache.TryAdd(key, body)) bodyOrder.Add(key);
                if (bodyOrder.Count > BodyCacheSize)
                {
                    bodyCache.Remove(bodyOrder[0]);
                    bodyOrder.RemoveAt(0);
                }
            }
            return body;
        }
        finally
        {
            // Solo il proprio: un altro scaricamento dello stesso corpo, partito intanto, resta a chi lo aspetta.
            lock (bodyCache) if (downloading.GetValueOrDefault(key) == download) downloading.Remove(key);
        }
    }

    /// <summary>
    /// Come il nuovo Outlook, che precarica i messaggi: corpi scaricati a riposo (i primi dell'elenco, i vicini di quello
    /// aperto), che il riquadro poi trova fra quelli già letti. Sulla connessione in sottofondo, con la cartella in sola
    /// lettura (EXAMINE) e BODY.PEEK: i messaggi non diventano letti, e aprire un messaggio o una cartella non aspetta.
    /// Uno alla volta, così anteprime e controllo dei nuovi messaggi passano fra l'uno e l'altro; i piccoli interi, di quelli
    /// con allegati solo testo e immagini incorporate (ImapProvider.Prefetchable).
    /// </summary>
    public async Task PrefetchAsync(IReadOnlyList<MessageSummary> messages, CancellationToken cancel = default)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int fetched = 0;
        foreach (var message in messages)
        {
            if (cancel.IsCancellationRequested) break;
            var key = KeyOf(message);
            bool Wanted() { lock (bodyCache) return !bodyCache.ContainsKey(key) && !downloading.ContainsKey(key); }
            // Già nella copia su disco: il clic lo apre da lì (SavedBody).
            if (!Wanted() || !known.TryGetValue(key, out var s) || !ImapProvider.Prefetchable(s) || OnDisk(message)) continue;
            try
            {
                if (await ReadAsync(async client =>
                    {
                        if (!Wanted()) return false; // intanto l'ha aperto l'utente
                        await DownloadAsync(ExamineAsync(client, message.FolderId), message, key, early: true);
                        return true;
                    }, background, cancel))
                    fetched++;
            }
            catch (OperationCanceledException)
            {
                break; // cambiata cartella, o aperto un altro messaggio
            }
            catch (Exception ex)
            {
                Log.Write($"corpo in anticipo non scaricato ({message.Id}): {ex.Message}"); // al clic si riprova
            }
        }
        if (fetched > 0) Log.Time($"corpi in anticipo ({fetched} di {messages.Count})", clock);
    }

    /// <summary>
    /// Per leggere bastano il testo e le immagini che il testo mostra. I messaggi piccoli si scaricano interi (una sola
    /// richiesta); di quelli grandi, cioè con allegati, solo queste parti: gli allegati arrivano quando si aprono
    /// (prima arrivava sempre il messaggio intero, anche con 20 MB di allegati). MailKit chiede sempre BODY.PEEK: leggere
    /// non segna letto.
    /// </summary>
    async Task<MessageBody> ReadBodyAsync(Task<IMailFolder> folder, MessageSummary message, string key, bool early)
    {
        var f = await folder;
        var uid = Uid(message);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Struttura, destinatari e dimensione di solito sono già arrivati con l'elenco.
        if (!known.TryGetValue(key, out var s))
            s = (await f.FetchAsync(new[] { uid }, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope |
                                                   MessageSummaryItems.BodyStructure | MessageSummaryItems.Size)).FirstOrDefault()
                ?? throw new MailException(T("Il messaggio non c'è più: forse è stato spostato o eliminato."));

        // Busta PEC o messaggio firmato: i byte com'è sul server, così Pec verifica la firma senza scaricarlo di nuovo.
        byte[]? raw = null;
        MimeMessage? whole = null;
        if (s.Size is < SmallMessage && s.BodyParts.Any(p => Pec.IsSignaturePart(p.FileName, p.ContentType.MimeType)))
        {
            await using var stream = await f.GetStreamAsync(uid, "");
            raw = new byte[stream.Length];
            await stream.ReadExactlyAsync(raw);
            whole = MimeMessage.Load(new MemoryStream(raw));
        }
        else if (s.Size is < SmallMessage) whole = await f.GetMessageAsync(uid);
        async Task<string> TextOf(BodyPartText part) => await f.GetBodyPartAsync(uid, part) is TextPart t ? t.Text : "";
        var html = whole != null ? whole.HtmlBody ?? "<pre>" + WebUtility.HtmlEncode(whole.TextBody ?? "") + "</pre>"
                 : s.HtmlBody is { } h ? await TextOf(h)
                 : "<pre>" + WebUtility.HtmlEncode(s.TextBody is { } t ? await TextOf(t) : "") + "</pre>";

        async Task<byte[]> InlineAsync(BodyPartBasic part, string cid)
        {
            var mine = whole?.BodyParts.OfType<MimePart>().FirstOrDefault(p => string.Equals(p.ContentId, cid, StringComparison.OrdinalIgnoreCase));
            if (mine?.Content == null) return await ContentOf(f, uid, part);
            using var ms = new MemoryStream();
            await mine.Content.DecodeToAsync(ms);
            return ms.ToArray();
        }

        var attachments = new List<Attachment>();
        foreach (var part in s.BodyParts)
        {
            var cid = part.ContentId?.Trim('<', '>');
            // Incorporata solo se il testo la mostra davvero; altrimenti è un allegato come gli altri.
            bool inline = cid != null && !part.IsAttachment && html.Contains("cid:" + cid, StringComparison.OrdinalIgnoreCase);
            if (!part.IsAttachment && !inline && cid == null) continue;
            attachments.Add(new Attachment
            {
                Id = part.PartSpecifier,
                Name = part.FileName ?? (part is BodyPartMessage m ? (m.Envelope?.Subject ?? "messaggio") + ".eml" : "allegato"),
                ContentType = part.ContentType.MimeType,
                Size = "base64".Equals(part.ContentTransferEncoding, StringComparison.OrdinalIgnoreCase) ? part.Octets * 3 / 4 : part.Octets,
                IsInline = inline,
                ContentId = cid,
                Content = inline ? await InlineAsync(part, cid!) : null,
            });
        }
        // Chi ha verificato il mittente: nei messaggi interi le intestazioni ci sono già, negli altri si chiedono sole.
        var headers = whole?.Headers ?? await SenderCheck.FetchAsync(f, uid);
        var authentication = SenderCheck.FromHeaders(headers);
        Log.Time($"corpo{(early ? " in anticipo" : "")} ({(whole != null ? "intero" : "solo testo e immagini")}, {s.Size / 1024} KB, {attachments.Count(a => a.IsInline)} immagini incorporate, {attachments.Count(a => !a.IsInline)} allegati)", clock);

        return new MessageBody
        {
            AuthenticationResults = authentication,
            ListUnsubscribe = headers["List-Unsubscribe"],
            ListUnsubscribePost = headers["List-Unsubscribe-Post"],
            Invite = await Invitation.FindAsync(f, uid, s, whole),
            Html = html,
            To = s.Envelope?.To.Mailboxes.Select(a => new Address { Name = a.Name ?? "", Email = a.Address }).ToList() ?? new(),
            Cc = s.Envelope?.Cc.Mailboxes.Select(a => new Address { Name = a.Name ?? "", Email = a.Address }).ToList() ?? new(),
            Bcc = s.Envelope?.Bcc.Mailboxes.Select(a => new Address { Name = a.Name ?? "", Email = a.Address }).ToList() ?? new(),
            Attachments = attachments,
            Raw = raw,
        };
    }

    /// <param name="cancel">Solo per gli allegati scaricati (GetAttachmentAsync): interrotto, MailKit chiude la connessione.</param>
    /// <param name="progress">I byte arrivati nella misura di Attachment.Size (TransferProgress).</param>
    static async Task<byte[]> ContentOf(IMailFolder f, UniqueId uid, BodyPartBasic part, CancellationToken cancel = default, IProgress<long>? progress = null)
    {
        using var ms = new MemoryStream();
        switch (await f.GetBodyPartAsync(uid, part, cancel, TransferProgress.For(progress, part.ContentTransferEncoding)))
        {
            case MimePart p when p.Content != null: await p.Content.DecodeToAsync(ms); break;
            case MessagePart m when m.Message != null: await m.Message.WriteToAsync(ms); break;
        }
        return ms.ToArray();
    }

    public Task<byte[]> GetAttachmentAsync(MessageSummary message, Attachment attachment, IProgress<long>? progress = null, CancellationToken cancel = default) =>
        AttachmentAsync(message, attachment, ConnectionFor(message), progress, cancel);

    /// <param name="cancel">
    /// Annulla del gestore degli scaricamenti (ritocchi del 16 ottobre 2026): a differenza delle altre richieste arriva ai
    /// comandi di MailKit, che si interrompono e chiudono la connessione (si riapre alla richiesta dopo, 1-2 s): così il
    /// trasferimento si ferma davvero, invece di continuare fino in fondo per un allegato che nessuno aspetta più.
    /// </param>
    Task<byte[]> AttachmentAsync(MessageSummary message, Attachment attachment, Connection on, IProgress<long>? progress = null, CancellationToken cancel = default)
    {
        if (attachment.Content != null)
        {
            progress?.Report(attachment.Content.Length);
            return Task.FromResult(attachment.Content);
        }
        return RunAsync(async client =>
        {
            var f = await ExamineOrOpenAsync(client, message.FolderId, on);
            var uid = Uid(message);
            var s = (await f.FetchAsync(new[] { uid }, MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure, cancel)).FirstOrDefault();
            var part = s?.BodyParts.FirstOrDefault(p => p.PartSpecifier == attachment.Id) ?? throw new MailException(T("Allegato non trovato."));
            return await ContentOf(f, uid, part, cancel, progress);
        }, on, cancel);
    }

    /// <summary>
    /// Sulla connessione delle bozze, che di una cartella diversa dalle Bozze legge soltanto (gli allegati di un messaggio
    /// inoltrato), la cartella in sola lettura, come negli account IMAP (ImapProvider.ExamineOrOpenAsync); sulle altre due,
    /// dove passano anche le azioni, in lettura e scrittura. Fino alle scelte del 12 ottobre (C-08) qui era sempre SELECT:
    /// non per come funziona Gmail, ma perché la lettura degli allegati era nata sulla connessione principale. Se le Bozze
    /// sono già aperte per scrivere restano così (ExamineAsync non riapre una cartella aperta).
    /// </summary>
    Task<IMailFolder> ExamineOrOpenAsync(ImapClient client, string folderId, Connection on) =>
        on == drafts ? ExamineAsync(client, folderId) : OpenAsync(client, folderId);

    // ---------------------------------------------------------------- azioni

    /// <summary>
    /// Un solo comando per cartella, con tutti i suoi UID (50 messaggi erano 50 giri in fila sulla connessione), sulla
    /// connessione che resta ferma su quella cartella: "Tutti i messaggi" su quella delle conversazioni.
    /// </summary>
    /// <remarks>Sono scritture di Missiva: gli avvisi dell'IDLE che ne tornano (EXPUNGE, segni) sono il loro eco (InboxIdle.OwnAsync).</remarks>
    /// <param name="affectsUnread">Falso per contrassegni e categorie, che non cambiano i non letti: alla fine non si riconta.</param>
    Task EachFolderAsync(IReadOnlyList<MessageSummary> messages, Func<ImapClient, IMailFolder, IList<UniqueId>, string, Task> action, bool affectsUnread = true) => InboxIdle.OwnAsync(Account.Id, async () =>
    {
        foreach (var group in messages.GroupBy(m => m.FolderId))
        {
            var uids = group.Select(Uid).Distinct().ToList();
            await RunAsync(async client => await action(client, await OpenAsync(client, group.Key), uids, group.Key), ConnectionFor(group.First()));
        }
    }, affectsUnread);

    public Task SetReadAsync(IReadOnlyList<MessageSummary> messages, bool read) => EachFolderAsync(messages, async (_, f, uids, folderId) =>
    {
        // L'eco all'IDLE, per messaggio (L-04): dalla Posta in arrivo con il suo UID. Da "Tutti i messaggi" (conversazioni,
        // ricerca) l'UID della Posta in arrivo non si sa, e il suo avviso passa: dice lo stato giusto, costa un controllo.
        InboxIdle.Expect(folderId, uids, InboxIdle.EchoOf.Seen, read);
        if (read) await f.AddFlagsAsync(uids, MessageFlags.Seen, true);
        else await f.RemoveFlagsAsync(uids, MessageFlags.Seen, true);
    });

    public Task SetFlagAsync(IReadOnlyList<MessageSummary> messages, bool flagged) => EachFolderAsync(messages, async (_, f, uids, folderId) =>
    {
        InboxIdle.Expect(folderId, uids, InboxIdle.EchoOf.Flagged, flagged);
        if (flagged) await f.AddFlagsAsync(uids, MessageFlags.Flagged, true);
        else await f.RemoveFlagsAsync(uids, MessageFlags.Flagged, true);
    }, affectsUnread: false);

    /// <summary>
    /// Per Annulla, l'UID di ogni messaggio nella cartella di arrivo (Gmail lo dice con COPYUID, UIDPLUS) e, quando servono
    /// per rimettere le etichette com'erano, quelle che aveva prima (X-GM-LABELS, una FETCH leggera): se aveva già
    /// l'etichetta della cartella di arrivo, e per i messaggi di "Tutti i messaggi" se era in Posta in arrivo. Elimina e
    /// Archivia dalle cartelle normali non ne hanno bisogno.
    /// </summary>
    public async Task<List<MovedMessage>> MoveAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination)
    {
        // Nella Posta in arrivo con un UID nuovo, ma non sono posta nuova (A-06).
        if (ImapProvider.IsInbox(destination)) InboxIdle.OwnArrivals(Account.Id, messages.Select(m => m.StableId));
        var moved = new List<MovedMessage>();
        await EachFolderAsync(messages, async (client, f, uids, folderId) =>
        {
            var target = await client.GetFolderAsync(destination.Id);
            var here = messages.Where(m => m.FolderId == folderId).DistinctBy(m => m.Id).ToList();
            var labels = folderId == allMailFolder || folderId != destination.Id && LabelOf(target) != null ? await LabelsAsync(client, f, uids) : null;
            IReadOnlyList<string>? Before(MessageSummary m) => labels?.GetValueOrDefault(Uid(m));
            if (folderId == allMailFolder && destination.Kind is not (FolderKind.Deleted or FolderKind.Junk))
            {
                // Chi era in Posta in arrivo ne esce (\Inbox tolta): l'IDLE ne riceve l'EXPUNGE, uno per messaggio (L-04, E-14).
                if (destination.Kind != FolderKind.Inbox && labels != null)
                    InboxIdle.Expect("INBOX", uids.Where(u => labels.GetValueOrDefault(u)?.Contains(@"\Inbox", StringComparer.OrdinalIgnoreCase) == true).Select(u => "tutti:" + u.Id), InboxIdle.EchoOf.Removed);
                await LeaveInboxAsync(f, uids, destination, target);
                moved.AddRange(here.Select(m => new MovedMessage(m, m.Id, Before(m)))); // restano in "Tutti i messaggi", con lo stesso UID
            }
            else if (folderId != destination.Id)
            {
                InboxIdle.Expect(folderId, uids, InboxIdle.EchoOf.Removed);
                InboxIdle.Expect(folderId, uids, InboxIdle.EchoOf.Any);
                UniqueIdMap map;
                try { map = await f.MoveToAsync(uids, target); }
                catch (ImapCommandException ex)
                {
                    // Il motivo del rifiuto lo dà Gmail nel testo del NO: senza, l'avviso non dice nulla.
                    throw new MailException(uids.Count == 1
                            ? T("Gmail non ha spostato il messaggio da \"{0}\" a \"{1}\": {2}", folderId, destination.Id, ex.ResponseText)
                            : T("Gmail non ha spostato {0} messaggi da \"{1}\" a \"{2}\": {3}", uids.Count, folderId, destination.Id, ex.ResponseText), ex)
                        { Repeatable = client.Capabilities.HasFlag(ImapCapabilities.Move) }; // un UID MOVE rifiutato per intero (MailException.Repeatable)
                }
                foreach (var m in here)
                    if (map.TryGetValue(Uid(m), out var to)) moved.Add(new MovedMessage(m, to.Id.ToString(), Before(m)));
            }
        });
        return moved;
    }

    /// <summary>Le etichette di Gmail dei messaggi, per UID; null se il server non le dà (non è Gmail).</summary>
    static async Task<Dictionary<UniqueId, IReadOnlyList<string>>?> LabelsAsync(ImapClient client, IMailFolder f, IList<UniqueId> uids)
    {
        if (!client.Capabilities.HasFlag(ImapCapabilities.GMailExt1)) return null;
        var items = await f.FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.GMailLabels);
        return items.ToDictionary(i => i.UniqueId, i => (IReadOnlyList<string>)(i.GMailLabels?.ToList() ?? []));
    }

    /// <summary>
    /// Se il messaggio aveva l'etichetta prima dello spostamento: dalle etichette lette allora; senza, da quello che dice
    /// l'elenco (tipo della cartella nella ricerca, cartella, stella, categorie).
    /// </summary>
    static bool Had(MovedMessage x, string label) =>
        x.Labels is { } labels ? labels.Contains(label, StringComparer.OrdinalIgnoreCase)
        : label == @"\Inbox" ? x.Original.LabelKind == FolderKind.Inbox || x.Original.FolderId.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
        : label == @"\Starred" ? x.Original.IsFlagged
        : x.Original.Categories.Contains(label, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// I messaggi tornano da dove erano, con lo spostamento opposto sulla connessione ferma sulla cartella di arrivo, e con
    /// le etichette di prima: chi aveva già l'etichetta della cartella di arrivo torna con una copia (UID COPY), che in
    /// Gmail gliela lascia, invece che con uno spostamento, che gliela toglierebbe. Quelli di "Tutti i messaggi" (risultati
    /// della ricerca, conversazioni, la cartella stessa) non si erano spostati davvero: si rimette \Inbox a chi l'aveva e si
    /// toglie l'etichetta della cartella a chi non l'aveva (mai un EXPUNGE, che secondo le impostazioni IMAP di Gmail può
    /// eliminare); da Cestino e Spam tornano con uno spostamento vero, e chi era in Posta in arrivo ci torna.
    /// </summary>
    public Task<List<MovedMessage>> UndoMoveAsync(IReadOnlyList<MovedMessage> moved, MailFolder destination)
    {
        // Annulla li rimette dov'erano, anche nella Posta in arrivo (con l'etichetta, da "Tutti i messaggi"): non sono posta nuova (A-06).
        InboxIdle.OwnArrivals(Account.Id, moved.Select(x => x.Original.StableId));
        return InboxIdle.OwnAsync(Account.Id, () => UndoMoveNowAsync(moved, destination));
    }

    async Task<List<MovedMessage>> UndoMoveNowAsync(IReadOnlyList<MovedMessage> moved, MailFolder destination)
    {
        var back = new List<MovedMessage>();
        static StoreLabelsRequest Labels(StoreAction action, string label) => new(action, new[] { label }) { Silent = true };
        foreach (var group in moved.GroupBy(x => x.Original.FolderId))
        {
            var from = group.Key;
            var hadInbox = group.Where(x => Had(x, @"\Inbox")).ToList();
            if (from == allMailFolder && destination.Kind is not (FolderKind.Deleted or FolderKind.Junk))
            {
                await RunAsync(async client =>
                {
                    var all = await OpenAsync(client, from);
                    if (destination.Kind == FolderKind.Inbox)
                    {
                        var added = group.Except(hadInbox).Select(x => Uid(x.Original)).ToList();
                        InboxIdle.Expect("INBOX", added.Select(u => "tutti:" + u.Id), InboxIdle.EchoOf.Removed); // escono dalla Posta in arrivo
                        if (added.Count > 0) await all.StoreAsync(added, Labels(StoreAction.Remove, @"\Inbox"));
                        return;
                    }
                    if (hadInbox.Count > 0) await all.StoreAsync(hadInbox.Select(x => Uid(x.Original)).ToList(), Labels(StoreAction.Add, @"\Inbox"));
                    if (LabelOf(await client.GetFolderAsync(destination.Id)) is { } label)
                    {
                        var added = group.Where(x => !Had(x, label)).Select(x => Uid(x.Original)).ToList();
                        if (added.Count > 0) await all.StoreAsync(added, Labels(StoreAction.Remove, label));
                    }
                }, On(from));
                back.AddRange(group.Select(x => x with { NewId = x.Original.Id }));
                continue;
            }
            await RunAsync(async client =>
            {
                static UniqueId NewUid(MovedMessage x) => new(uint.Parse(x.NewId));
                var there = await OpenAsync(client, destination.Id);
                var origin = await client.GetFolderAsync(from);
                var label = LabelOf(there);
                var kept = label == null ? [] : group.Where(x => Had(x, label)).ToList();
                var inbox = new List<UniqueId>();
                async Task ReturnAsync(List<MovedMessage> part, bool copy)
                {
                    if (part.Count == 0) return;
                    var uids = part.Select(NewUid).ToList();
                    if (!copy) InboxIdle.Expect(destination.Id, uids, InboxIdle.EchoOf.Removed); // tornano via dalla Posta in arrivo
                    var map = copy ? await there.CopyToAsync(uids, origin) : await there.MoveToAsync(uids, origin);
                    foreach (var x in part)
                    {
                        if (!map.TryGetValue(NewUid(x), out var to)) continue;
                        back.Add(x with { NewId = to.Id.ToString() });
                        if (hadInbox.Contains(x)) inbox.Add(to);
                    }
                }
                await ReturnAsync(group.Except(kept).ToList(), copy: false);
                await ReturnAsync(kept, copy: true);
                if (from == allMailFolder && inbox.Count > 0) await (await OpenAsync(client, from)).StoreAsync(inbox, Labels(StoreAction.Add, @"\Inbox"));
            }, On(destination.Id));
        }
        return back;
    }

    /// <summary>
    /// Coda delle azioni senza rete: i messaggi ancora nella loro cartella, sulla connessione in sottofondo con EXAMINE, con lo
    /// stesso X-GM-MSGID (l'UID di un messaggio uscito dalla cartella non torna, ma la chiave si controlla lo stesso).
    /// </summary>
    public async Task<List<MessageSummary>?> StillThereAsync(IReadOnlyList<MessageSummary> messages)
    {
        var there = new List<MessageSummary>();
        foreach (var group in messages.GroupBy(m => m.FolderId))
        {
            var uids = group.Select(Uid).Distinct().ToList();
            var found = await ReadAsync(async client =>
                await (await ExamineAsync(client, group.Key)).FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.GMailMessageId), background);
            foreach (var m in group)
                if (found.FirstOrDefault(s => s.UniqueId == Uid(m)) is { } s && (m.StableId.Length == 0 || s.GMailMessageId?.ToString() == m.StableId))
                    there.Add(m);
        }
        return there;
    }

    /// <summary>
    /// L'etichetta di Gmail di una cartella: quelle dell'utente per percorso; di quelle di sistema Posta in arrivo, Speciali e
    /// Importanti (le altre, come Cestino e Tutti i messaggi, non sono etichette che un messaggio ha insieme ad altre).
    /// </summary>
    static string? LabelOf(IMailFolder folder) =>
        folder.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? @"\Inbox"
        : folder.Attributes.HasFlag(FolderAttributes.Flagged) ? @"\Starred"
        : folder.Attributes.HasFlag(FolderAttributes.Important) ? @"\Important"
        : folder.FullName.StartsWith("[Gmail]", StringComparison.Ordinal) ? null
        : folder.FullName;

    /// <summary>
    /// I messaggi di "Tutti i messaggi" (risultati della ricerca in tutte le cartelle, conversazioni) ci restano comunque:
    /// spostarli da lì non toglie nulla, nemmeno dalla Posta in arrivo. Si lavora sulle etichette, come fa Gmail:
    /// archiviare è togliere \Inbox, spostare in una cartella è aggiungere la sua etichetta (copiandoli lì) e togliere
    /// \Inbox. Solo Cestino e Spam restano uno spostamento vero.
    /// </summary>
    static async Task LeaveInboxAsync(IMailFolder allMail, IList<UniqueId> uids, MailFolder destination, IMailFolder target)
    {
        if (destination.Kind == FolderKind.Inbox)
        {
            await allMail.StoreAsync(uids, new StoreLabelsRequest(StoreAction.Add, new[] { @"\Inbox" }) { Silent = true });
            return;
        }
        if (destination.Id != allMail.FullName) await allMail.CopyToAsync(uids, target);
        await allMail.StoreAsync(uids, new StoreLabelsRequest(StoreAction.Remove, new[] { @"\Inbox" }) { Silent = true });
    }

    // ---------------------------------------------------------------- Posponi

    public Task<MailFolder> EnsureFolderAsync(string name) => RunAsync(async client =>
    {
        var root = client.GetFolder(client.PersonalNamespaces[0]);
        var existing = (await client.GetFoldersAsync(client.PersonalNamespaces[0]))
            .FirstOrDefault(f => f.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
        var folder = existing ?? await root.CreateAsync(name, true) ?? throw new MailException(T("Gmail non ha creato la cartella {0}.", name));
        return new MailFolder { Id = folder.FullName, Name = folder.Name, Kind = FolderKind.Other };
    });

    /// <summary>La chiave è l'X-GM-MSGID: in Gmail lo stesso messaggio lo conserva in tutte le cartelle.</summary>
    public Task<string> SnoozeAsync(MessageSummary message, MailFolder snoozeFolder) => InboxIdle.OwnAsync(Account.Id, () => RunAsync(async client =>
    {
        var f = await OpenAsync(client, message.FolderId);
        var key = message.StableId;
        if (key.Length == 0)
            key = (await f.FetchAsync(new[] { Uid(message) }, MessageSummaryItems.GMailMessageId)).FirstOrDefault()?.GMailMessageId?.ToString()
                  ?? throw new MailException(T("Gmail non ha dato l'identificativo del messaggio."));
        var target = await client.GetFolderAsync(snoozeFolder.Id);
        InboxIdle.Expect(message.FolderId, [Uid(message)], InboxIdle.EchoOf.Removed);
        if (message.FolderId == allMailFolder) await LeaveInboxAsync(f, new[] { Uid(message) }, snoozeFolder, target);
        else await f.MoveToAsync(Uid(message), target);
        return key;
    }, ConnectionFor(message)));

    public Task<string?> WakeAsync(string key, MailFolder snoozeFolder, MailFolder inbox) => InboxIdle.OwnAsync(Account.Id, () => RunAsync(async client =>
    {
        InboxIdle.OwnArrivals(Account.Id, [key]); // il ritorno di Posponi non è posta nuova (A-06): come prima, nessuna notifica
        if (!ulong.TryParse(key, out var id)) return null;
        var f = await OpenAsync(client, snoozeFolder.Id);
        var uids = await f.SearchAsync(SearchQuery.GMailMessageId(id));
        if (uids.Count == 0) return null;
        await f.RemoveFlagsAsync(uids, MessageFlags.Seen, true); // torna da leggere
        await f.MoveToAsync(uids, client.Inbox);
        return (string?)key;
    }));

    /// <summary>In Gmail copiare in una cartella significa aggiungere quell'etichetta.</summary>
    public Task CopyAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination) => EachFolderAsync(messages, async (client, f, uids, folderId) =>
    {
        InboxIdle.Expect(folderId, uids, InboxIdle.EchoOf.Any); // un'etichetta in più
        await f.CopyToAsync(uids, await client.GetFolderAsync(destination.Id));
    });

    /// <summary>
    /// Il messaggio com'è sul server (BODY.PEEK[], con limit solo l'inizio), allegati compresi, senza analizzarlo né
    /// riscriverlo: prima MimeKit lo analizzava e lo riscriveva, circa tre volte la sua dimensione in memoria (con 20 MB di
    /// allegati 60 MB, per mostrarne 2 nell'Origine). Serve una volta sola, quindi non resta in memoria. Mai sulla
    /// principale, sempre in sola lettura: l'Origine (al massimo limit) su quella in sottofondo, già collegata; il messaggio
    /// intero (Salva con nome), che con 20 MB di allegati sono secondi, su quella delle bozze, così intanto non aspettano
    /// né i clic né le anteprime.
    /// </summary>
    public Task<byte[]> GetRawAsync(MessageSummary message, int limit = int.MaxValue, CancellationToken cancel = default, IProgress<long>? progress = null) => RunAsync(async client =>
    {
        var f = await ExamineAsync(client, message.FolderId);
        // Il messaggio intero (Salva con nome .eml, dal gestore degli scaricamenti) si ferma davvero con Annulla: il token arriva
        // al comando, e interrotto MailKit chiude la connessione delle bozze. Con limit (l'Origine, la posta certificata) come
        // prima: si guarda solo prima di partire (ritocchi del 16 ottobre 2026).
        var transfer = TransferProgress.For(progress, null);
        await using var stream = limit < int.MaxValue ? await f.GetStreamAsync(Uid(message), "", 0, limit, CancellationToken.None, transfer)
                                                      : await f.GetStreamAsync(Uid(message), "", cancel, transfer);
        var raw = new byte[stream.Length];
        await stream.ReadExactlyAsync(raw);
        return raw;
    }, limit < int.MaxValue ? background : drafts, cancel);

    public Task DeletePermanentlyAsync(IReadOnlyList<MessageSummary> messages) => EachFolderAsync(messages, async (_, f, uids, folderId) =>
    {
        InboxIdle.Expect(folderId, uids, InboxIdle.EchoOf.Removed);
        InboxIdle.Expect(folderId, uids, InboxIdle.EchoOf.Any);
        await f.AddFlagsAsync(uids, MessageFlags.Deleted, true);
        await f.ExpungeAsync(uids);
    });

#if DEBUG
    /// <summary>Prove: il server SMTP finto sul PC (STARTTLS con il suo certificato), al posto di smtp.gmail.com.</summary>
    internal static int? TestSmtpPort;
    internal static string? TestSmtpCertificate;
#endif

    /// <summary>Via SMTP con STARTTLS e il token di Google; con la conferma di recapito, la DSN di SMTP.</summary>
    async Task SmtpSendAsync(MimeMessage message, bool deliveryReceipt)
    {
        ThrowIfClosed(); // l'SMTP ha un collegamento suo: senza questo il messaggio partirebbe, e la sua bozza resterebbe nelle Bozze
        using var smtp = new MimeOptions.DsnSmtpClient(deliveryReceipt) { Timeout = MailLimits.GmailSilenceMs };
        Network.Check(); // --senza-rete (solo Debug)
#if DEBUG
        if (TestSmtpPort is { } port)
        {
            smtp.ServerCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == TestSmtpCertificate;
            await smtp.ConnectAsync("127.0.0.1", port, SecureSocketOptions.StartTls);
        }
#endif
        if (!smtp.IsConnected) await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
        await smtp.AuthenticateAsync(await CredentialsAsync());
        await MimeOptions.SendAsync(smtp, message, "smtp.gmail.com"); // Gmail salva da sé il messaggio in Posta inviata
        // Partito: un errore nel saluto finale non deve farlo sembrare non partito (l'invio pianificato lo rifarebbe).
        try { await smtp.DisconnectAsync(true); } catch (Exception ex) { Log.Write("SMTP di Gmail chiuso male dopo l'invio: " + ex.Message); }
    }

    /// <summary>
    /// Invio pianificato fatto da Missiva: la bozza con quell'X-GM-MSGID parte com'è nelle Bozze (con la data di adesso), poi
    /// esce dalle Bozze. false se non c'è più (eliminata, o modificata e quindi con un'altra chiave). Partito il messaggio,
    /// restituisce true anche se la bozza non esce: un errore dopo l'SMTP lo farebbe ripartire al minuto dopo.
    /// </summary>
    public async Task<bool> SendDraftAsync(string draftKey)
    {
        if (!ulong.TryParse(draftKey, out var id)) return false;
        var message = await RunAsync(async client =>
        {
            if (await DraftsAsync(client) is not { } folder) return null;
            var f = await OpenAsync(client, folder);
            var uids = await f.SearchAsync(SearchQuery.GMailMessageId(id));
            return uids.Count == 0 ? null : await f.GetMessageAsync(uids[^1]);
        }, drafts);
        if (message == null) return false;
        message.Date = DateTimeOffset.Now;
        await SmtpSendAsync(message, deliveryReceipt: false);
        try
        {
            await RunAsync(async client =>
            {
                if (await DraftsAsync(client) is { } folder) await DeleteDraftAsync(await OpenAsync(client, folder), draftKey);
            }, drafts);
        }
        catch (Exception ex)
        {
            Log.Error("bozza dell'invio pianificato", ex); // il messaggio è partito: la bozza resta nelle Bozze, niente errore
        }
        return true;
    }

    public async Task SendAsync(OutgoingMessage mail)
    {
        var message = await BuildAsync(mail);
        await SmtpSendAsync(message, mail.DeliveryReceipt);

        // La bozza che il messaggio continuava sparisce dalle Bozze, come in Outlook: un invio via SMTP non la tocca.
        if (mail.Draft is { } draft)
        {
            try
            {
                await RunAsync(async client =>
                {
                    if (await DraftsAsync(client) is { } folder) await DeleteDraftAsync(await OpenAsync(client, folder), draft.Message.StableId);
                }, drafts);
            }
            catch (Exception ex)
            {
                Log.Error("bozza del messaggio inviato", ex); // il messaggio è partito: la bozza resta, niente errore
            }
        }
    }

    /// <summary>
    /// Nelle Bozze con \Draft e \Seen, come le bozze scritte nel sito di Gmail, sulla connessione delle bozze; poi la bozza
    /// di prima si elimina (si ritrova con l'X-GM-MSGID, che non dipende dalla cartella da cui la si è aperta). Gli
    /// allegati della bozza di prima si scaricano prima, sulla stessa connessione; quelli piccoli restano in memoria
    /// (Attachment.Content), così il salvataggio dopo non li riscarica.
    /// </summary>
    public async Task<SavedDraft> SaveDraftAsync(OutgoingMessage mail)
    {
        var message = await BuildAsync(mail);
        var messageId = message.MessageId ??= MimeKit.Utils.MimeUtils.GenerateMessageId();
        // Il contenuto degli allegati appena scritti, nell'ordine del messaggio (null per quelli grandi).
        var contents = new List<(string? Name, byte[]? Bytes)>();
        foreach (var entity in message.Attachments)
        {
            byte[]? bytes = null;
            if (entity is MimePart { Content: { Stream: { } stream } content } && stream.Length < KeepAttachment)
            {
                using var ms = new MemoryStream();
                await content.DecodeToAsync(ms);
                bytes = ms.ToArray();
            }
            contents.Add(((entity as MimePart)?.FileName, bytes));
        }
        bool retried = false;
        return await RunAsync(async client =>
        {
            var folder = await DraftsAsync(client) ?? throw new MailException(T("Non trovo la cartella Bozze di Gmail."));
            var f = await OpenAsync(client, folder);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // Ripresa dopo una connessione caduta (RunAsync riprova da capo): la bozza forse è già scritta, e due APPEND ne
            // lascerebbero due copie. Prima la si cerca con il suo Message-Id.
            UniqueId? uid = retried ? await FindAsync(f, messageId) : null;
            retried = true;
            uid ??= await f.AppendAsync(new AppendRequest(message, MessageFlags.Draft | MessageFlags.Seen))
                    ?? await FindAsync(f, messageId)
                    ?? throw new MailException(T("Gmail non ha restituito la bozza salvata."));
            var s = (await f.FetchAsync(new[] { uid.Value }, ListItems)).FirstOrDefault() ?? throw new MailException(T("Gmail non ha restituito la bozza salvata."));
            if (mail.Draft is { } old) await DeleteDraftAsync(f, old.Message.StableId, keep: uid);
            Log.Time($"bozza salvata ({s.Size / 1024} KB){(mail.Draft != null ? ", sostituita quella di prima" : "")}", clock);
            var parts = (s.Attachments ?? []).ToList();
            // Allegati del server e del messaggio scritto, uno per uno, nello stesso ordine.
            byte[]? Kept(BodyPartBasic p, int k) =>
                parts.Count == contents.Count && contents[k].Name == p.FileName ? contents[k].Bytes : null;
            return new SavedDraft
            {
                Message = Summary(s, folder),
                Attachments = parts.Select((p, k) => new Attachment
                {
                    Id = p.PartSpecifier,
                    Name = p.FileName ?? "allegato",
                    ContentType = p.ContentType.MimeType,
                    Size = "base64".Equals(p.ContentTransferEncoding, StringComparison.OrdinalIgnoreCase) ? p.Octets * 3 / 4 : p.Octets,
                    Content = Kept(p, k),
                }).ToList(),
            };
        }, drafts);
    }

    /// <summary>Allegati delle bozze tenuti in memoria fra un salvataggio e l'altro: quelli grandi si riscaricano.</summary>
    const long KeepAttachment = MailLimits.KeepAttachment;

    /// <summary>La bozza scartata esce dalle Bozze, sulla connessione delle bozze; se non c'è già più, niente.</summary>
    public Task DiscardDraftAsync(SavedDraft draft) => RunAsync(async client =>
    {
        var folder = await DraftsAsync(client) ?? throw new MailException(T("Non trovo la cartella Bozze di Gmail."));
        await DeleteDraftAsync(await OpenAsync(client, folder), draft.Message.StableId);
    }, drafts);

    /// <summary>
    /// La bozza con quel Message-Id, non segnata \Deleted, come negli account IMAP (ImapProvider.FindAsync). Fino alle scelte
    /// del 12 ottobre (C-11) qui il filtro mancava, non per come funziona Gmail: questa ricerca è nata prima di quella degli
    /// account IMAP. Alla ripresa di un salvataggio una bozza che un altro programma aveva intanto segnato come eliminata
    /// (Gmail la toglie subito solo con l'eliminazione automatica accesa) veniva restituita come quella salvata, senza
    /// riscriverla.
    /// </summary>
    static async Task<UniqueId?> FindAsync(IMailFolder f, string messageId) =>
        messageId.Length == 0 ? null
        : (await f.SearchAsync(SearchQuery.HeaderContains("Message-Id", messageId).And(SearchQuery.NotDeleted))).Cast<UniqueId?>().LastOrDefault();

    string? draftsFolder;

    async Task<string?> DraftsAsync(ImapClient client) =>
        draftsFolder ??= (await client.GetFoldersAsync(client.PersonalNamespaces[0]))
            .FirstOrDefault(f => f.Attributes.HasFlag(FolderAttributes.Drafts))?.FullName;

    /// <summary>
    /// La bozza con quell'X-GM-MSGID esce dalle Bozze (in Gmail eliminare dalle Bozze elimina la bozza). Si cerca solo lì:
    /// una chiave sbagliata non tocca mai i messaggi delle altre cartelle.
    /// </summary>
    static async Task DeleteDraftAsync(IMailFolder drafts, string key, UniqueId? keep = null)
    {
        if (!ulong.TryParse(key, out var id)) return;
        var uids = (await drafts.SearchAsync(SearchQuery.GMailMessageId(id))).Where(u => u != keep).ToList();
        if (uids.Count == 0) return;
        await drafts.AddFlagsAsync(uids, MessageFlags.Deleted, true);
        await drafts.ExpungeAsync(uids);
    }

    readonly ReplySources replySources = new();

    /// <summary>
    /// Il messaggio da inviare o da salvare come bozza (MimeOptions.BuildAsync, lo stesso degli account IMAP). Di Gmail: il
    /// corpo dell'originale dai corpi già letti, Message-Id e References sulla connessione del messaggio (la principale, o
    /// quella delle conversazioni per "Tutti i messaggi"), gli allegati sulla connessione delle bozze.
    /// </summary>
    Task<MimeMessage> BuildAsync(OutgoingMessage mail) => MimeOptions.BuildAsync(mail, Account, replySources,
        original => GetBodyAsync(original),
        original => RunAsync(async client => (await (await OpenAsync(client, original.FolderId)).FetchAsync(new[] { Uid(original) },
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.References)).FirstOrDefault(), ConnectionFor(original)),
        (message, attachment) => AttachmentAsync(message, attachment, drafts));


    /// <summary>La connessione dell'IDLE, ferma sulla Posta in arrivo in sola lettura (InboxIdle): non la usa nient'altro.</summary>
    readonly Connection idle = new("dell'IDLE");

    /// <summary>Nuovi messaggi subito: IDLE sulla Posta in arrivo, rinnovato e ripreso da InboxIdle, sulla sua connessione.</summary>
    public Task WatchInboxAsync(Action<InboxNotice> changed, CancellationToken cancel) =>
        InboxIdle.RunAsync(Account.Email, token => ReadAsync(client => InboxIdle.SessionAsync(Account.Email, client, changed, token), idle, token), cancel);

    /// <summary>
    /// Dopo la ripresa del PC o un cambio di rete (App.WakeAsync), come in ImapProvider: tutte le connessioni, anche quella
    /// dell'IDLE con la sua sessione in corso, si buttano subito (A-04). Prima Gmail non aveva nemmeno ResetConnectionsAsync:
    /// dopo la sospensione il primo clic e il primo controllo aspettavano il tempo scaduto di due minuti (V-37 di velocita-2).
    /// </summary>
    public void DropConnections()
    {
        if (closed) return;
        foreach (var connection in new[] { main, background, threads, drafts, idle }) connection.Drop();
        InboxIdle.Break(Account.Email);
    }

    /// <summary>Chiuso con Dispose: da lì ogni richiesta, anche l'invio, esce con MailException.Closed (audit dell'11 ottobre, X-1).</summary>
    volatile bool closed;

    public bool IsClosed => closed;

    void ThrowIfClosed()
    {
        if (closed) throw MailException.Closed(Account);
    }

    public void Dispose()
    {
        closed = true;
        main.Dispose();
        background.Dispose();
        threads.Dispose();
        drafts.Dispose();
        idle.Dispose();
    }
}
