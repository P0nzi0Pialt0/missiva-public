using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Missiva;

/// <summary>
/// Account IMAP e SMTP con password, come in Thunderbird ed eM Client: caselle su dominio proprio (Aruba, Register.it,
/// OVH, Zoho, cPanel, Exchange con IMAP). Cartelle speciali da SPECIAL-USE (RFC 6154) o dai nomi, spostamenti con MOVE
/// o con COPY, \Deleted e UID EXPUNGE dei soli messaggi spostati (UIDPLUS), ricerca con SEARCH, invio con SMTP e copia in
/// Posta inviata (APPEND), bozze con APPEND e \Draft. Sempre con TLS (SSL/TLS o STARTTLS): un certificato non valido si
/// spiega e non si accetta mai. La password è cifrata con DPAPI e non va mai nel registro. Come in GmailProvider, più
/// connessioni: la principale per quello che chiede l'utente, una in sottofondo (anteprime, corpi in anticipo, nuovi
/// messaggi) e una per bozze, copie in Posta inviata e .eml.
/// </summary>
public sealed class ImapProvider(MailAccount account) : IMailProvider, IDisposable
{
    const int PageSize = MailLimits.PageSize;
    const MessageSummaryItems ListItems = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
        MessageSummaryItems.InternalDate | MessageSummaryItems.BodyStructure | MessageSummaryItems.Size;
    const MessageSummaryItems SummaryItems = ListItems | MessageSummaryItems.PreviewText;

    sealed class Connection(string name) : IDisposable
    {
        public readonly string Name = name;
        public readonly ImapClient Client = new() { Timeout = MailLimits.ImapSilenceMs };
        public readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>
        /// Il LOGOUT aspetta la risposta del server: con la rete caduta a metà erano fino a 60 s (il Timeout) sul thread di
        /// chi chiudeva il provider, anche quello dell'interfaccia (account rimosso, provider ricaricati). Ora in sottofondo,
        /// con cinque secondi di tetto: chi chiama non aspetta (audit, blocco 4).
        /// </summary>
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

    readonly Connection main = new("principale"), background = new("in sottofondo"), drafts = new("delle bozze");
    readonly Dictionary<string, MessageBody> bodyCache = new();
    readonly List<string> bodyOrder = new();
    readonly Dictionary<string, Task<MessageBody>> downloading = new();
    const int BodyCacheSize = MailLimits.BodyCacheSize;
    const uint SmallMessage = MailLimits.SmallMessage;

    public MailAccount Account { get; } = account;
    public bool SupportsFocused => false;
    public bool PrefetchesMany => true;

    ImapServer Server => Account.Imap ?? throw new MailException("Mancano le impostazioni del server: rimuovi l'account e aggiungilo di nuovo.");

    /// <summary>
    /// La password salvata che non si decifra più (profilo di Windows cambiato, impostazioni copiate da un altro PC) è un
    /// errore con il suo rimedio, la barra "Aggiorna password" sotto l'account: prima restava "Non raggiungibile: Dati non
    /// validi", l'errore di Windows (audit, F-44 punto 4). Il valore cifrato resta nelle impostazioni finché non la si riscrive.
    /// </summary>
    static NetworkCredential CredentialsOf(ImapServer server)
    {
        // Un account arrivato con una copia di sicurezza fatta senza le password (Backup): nessun tentativo con una password
        // vuota, che il server conterebbe fra gli accessi sbagliati; la barra "Aggiorna password" sotto l'account.
        if (server.ProtectedPassword == null)
            throw new MailException($"Su questo PC manca la password di {server.UserName}: scrivila con Aggiorna password.") { Fix = AccountFix.Password };
        if (Store.TryUnprotect(server.ProtectedPassword, out var password)) return new(server.UserName, password);
        throw new MailException($"La password di {server.UserName} salvata su questo PC non si legge più (profilo di Windows cambiato, o impostazioni copiate " +
                                "da un altro PC): scrivila di nuovo con Aggiorna password.") { Fix = AccountFix.Password };
    }

#if DEBUG
    /// <summary>
    /// Prove con il server IMAP finto sul PC (--prova-bozze, e con MISSIVA_IMAP_FINTO=1 la versione di prova): IMAP senza
    /// TLS, solo verso 127.0.0.1.
    /// </summary>
    internal static bool TestPlain = Environment.GetEnvironmentVariable("MISSIVA_IMAP_FINTO") == "1";
    /// <summary>L'impronta del certificato del server SMTP finto (STARTTLS), l'unico certificato non valido che si accetta.</summary>
    internal static string? TestCertificate;
#endif

    static SecureSocketOptions Options(MailSecurity security, string host, bool imap)
    {
#if DEBUG
        if (imap && TestPlain && host == "127.0.0.1") return SecureSocketOptions.None;
#endif
        // Mai None né StartTlsWhenAvailable: senza TLS la password viaggerebbe in chiaro.
        return security == MailSecurity.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
    }

    /// <summary>Il controllo del certificato di MailKit resta quello di Windows: qui solo il certificato del server finto delle prove.</summary>
    static void Validate(MailService client, string host)
    {
#if DEBUG
        if (TestCertificate is { } hash)
            client.ServerCertificateValidationCallback = (_, certificate, _, errors) => errors == SslPolicyErrors.None || certificate?.GetCertHashString() == hash;
        // La versione di prova con il server finto di --imap-finto (MISSIVA_IMAP_FINTO=1): il suo SMTP ha un certificato
        // fatto al volo, accettato solo sul PC stesso.
        else if (TestPlain && host == "127.0.0.1")
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
#endif
    }

    /// <summary>Gli errori di collegamento detti in italiano: certificato, credenziali, server che non risponde, niente TLS.</summary>
    internal static MailException? Explain(Exception ex, string host, int port, string what) => ex switch
    {
        MailException => null,
        SslHandshakeException => new MailException(
            $"Il certificato di {host} non è valido, quindi Missiva non si collega: con un certificato sbagliato qualcuno potrebbe leggere la password. " +
            "Controlla il nome del server (spesso è quello del provider, come imaps.aruba.it, invece di mail.tuodominio) o chiedi a chi gestisce la posta. " +
            "Dettagli: " + FirstSentence(ex.Message), ex),
        AuthenticationException => new MailException(
            $"{host} ha rifiutato nome utente o password ({what}). Controllali: spesso il nome utente è l'indirizzo intero, e con la verifica in due " +
            "passaggi serve una password per le app.", ex) { Fix = AccountFix.Password },
        NotSupportedException when ex.Message.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase) => new MailException(
            $"{host} non offre la cifratura STARTTLS sulla porta {port}: Missiva non gli manda la password in chiaro. Prova SSL/TLS (di solito porta {(what == "IMAP" ? 993 : 465)}).", ex),
        SocketException or TimeoutException or OperationCanceledException => new MailException(
            $"{host} non risponde sulla porta {port} ({what}): controlla nome del server e porta, o la connessione a Internet.", ex),
        ImapProtocolException or SmtpProtocolException => new MailException(
            $"{host} ha risposto in modo inatteso sulla porta {port} ({what}): forse la sicurezza scelta (SSL/TLS o STARTTLS) non è quella della porta.", ex),
        _ => null,
    };

    static string FirstSentence(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    /// <summary>Collega e autentica, con gli errori spiegati.</summary>
    static async Task ConnectAsync(ImapClient imap, ImapServer server, CancellationToken cancel = default)
    {
        try
        {
            // Prima di collegarsi: senza una password da mandare (mancante o non più leggibile) la connessione non si apre nemmeno.
            var credentials = imap.IsAuthenticated ? null : CredentialsOf(server);
            if (!imap.IsConnected)
            {
                Network.Check(); // --senza-rete (solo Debug)
                Validate(imap, server.ImapHost);
                await imap.ConnectAsync(server.ImapHost, server.ImapPort, Options(server.ImapSecurity, server.ImapHost, imap: true), cancel);
            }
            if (credentials != null && !imap.IsAuthenticated) await imap.AuthenticateAsync(credentials, cancel);
        }
        catch (Exception ex) when (Explain(ex, server.ImapHost, server.ImapPort, "IMAP") is { } explained)
        {
            try { await imap.DisconnectAsync(false); } catch { }
            throw explained;
        }
    }

    /// <param name="deliveryReceipt">Conferma di recapito: la DSN di SMTP (MimeOptions.DsnSmtpClient).</param>
    static async Task<SmtpClient> SmtpAsync(ImapServer server, bool deliveryReceipt = false, CancellationToken cancel = default)
    {
        var smtp = new MimeOptions.DsnSmtpClient(deliveryReceipt) { Timeout = MailLimits.ImapSilenceMs };
        try
        {
            Network.Check(); // --senza-rete (solo Debug)
            Validate(smtp, server.SmtpHost);
            await smtp.ConnectAsync(server.SmtpHost, server.SmtpPort, Options(server.SmtpSecurity, server.SmtpHost, imap: false), cancel);
            if (smtp.Capabilities.HasFlag(SmtpCapabilities.Authentication)) await smtp.AuthenticateAsync(CredentialsOf(server), cancel);
            return smtp;
        }
        catch (Exception ex) when (Explain(ex, server.SmtpHost, server.SmtpPort, "SMTP") is { } explained)
        {
            smtp.Dispose();
            throw explained;
        }
        catch
        {
            smtp.Dispose();
            throw;
        }
    }

    /// <summary>
    /// All'aggiunta dell'account: IMAP e SMTP accettano server, sicurezza e credenziali (niente viene inviato). Restituisce
    /// le estensioni del server IMAP, per il registro.
    /// </summary>
    public static async Task<string> TestAsync(ImapServer server, CancellationToken cancel)
    {
        using var imap = new ImapClient { Timeout = 30_000 };
        await ConnectAsync(imap, server, cancel);
        var capabilities = imap.Capabilities;
        await imap.DisconnectAsync(true, cancel);
        using var smtp = await SmtpAsync(server, cancel: cancel);
        await smtp.DisconnectAsync(true, cancel);
        return string.Join(" ", new[] { ImapCapabilities.Move, ImapCapabilities.UidPlus, ImapCapabilities.SpecialUse, ImapCapabilities.Idle,
                                         ImapCapabilities.CondStore, ImapCapabilities.ListStatus }.Where(c => capabilities.HasFlag(c)));
    }

    /// <summary>
    /// Gestisci account: nuovi server o nuova password (password null: resta quella di prima), provati con TestAsync prima
    /// di salvarli. Se il server rifiuta, l'errore spiegato esce da qui e l'account resta com'era; se accetta, l'account
    /// (stesso Id: firme, categorie e ordine delle cartelle restano) prende i server nuovi con la password cifrata con DPAPI.
    /// La password non va mai nel registro. Il chiamante salva le impostazioni.
    /// </summary>
    public static async Task<string> ChangeServerAsync(MailAccount account, ImapServer candidate, string? password, CancellationToken cancel)
    {
        candidate.ProtectedPassword = password != null ? Store.Protect(password) : account.Imap?.ProtectedPassword;
        var extensions = await TestAsync(candidate, cancel);
        cancel.ThrowIfCancellationRequested(); // Annulla durante la prova: anche se il server ha accettato, nulla cambia
        account.Imap = candidate;
        Log.Write($"account IMAP {account.Email} cambiato{(password != null ? ", con una password nuova" : "")}: " +
                  $"{candidate.ImapHost}:{candidate.ImapPort} {candidate.ImapSecurity}, {candidate.SmtpHost}:{candidate.SmtpPort} {candidate.SmtpSecurity} ({extensions})");
        return extensions;
    }

    /// <summary>
    /// Dopo un cambio di server o di password: le connessioni aperte si chiudono (ognuna quando finisce quello che sta
    /// facendo), così la prossima richiesta si collega con le impostazioni nuove.
    /// </summary>
    public async Task ResetConnectionsAsync()
    {
        // La connessione dell'IDLE resta presa fino al rinnovo (9 minuti): non la si aspetta, la sua sessione si interrompe da sé.
        Interlocked.Exchange(ref idleReset, new CancellationTokenSource()).Cancel();
        foreach (var connection in new[] { main, background, drafts })
        {
            await connection.Gate.WaitAsync();
            try
            {
                if (connection == main) special.Clear(); // su un altro server le cartelle speciali possono essere altre
                if (connection.Client.IsConnected) await connection.Client.DisconnectAsync(true);
            }
            catch { }
            finally
            {
                connection.Gate.Release();
            }
        }
    }

    /// <summary>
    /// Vero dentro la seconda prova di RunAsync, dopo una connessione caduta: le scritture che non si ripetono (APPEND in
    /// Posta inviata, COPY) guardano prima se la prima volta il server le aveva già fatte. AsyncLocal: vale per l'azione
    /// ripresa e per quello che chiama, non per le altre connessioni.
    /// </summary>
    static readonly AsyncLocal<bool> retrying = new();

    async Task<T> RunAsync<T>(Func<ImapClient, Task<T>> action, Connection? on = null, CancellationToken cancel = default)
    {
        var connection = on ?? main;
        var imap = connection.Client;
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
                try
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    bool connecting = !imap.IsAuthenticated;
                    await ConnectAsync(imap, Server);
                    if (connecting) Log.Time($"collegamento IMAP {connection.Name} a {Server.ImapHost}", clock);
                    retrying.Value = attempt > 0;
                    return await action(imap);
                }
                catch (Exception ex) when (attempt == 0 && ex is IOException or ServiceNotConnectedException or ImapProtocolException)
                {
                    // Connessione caduta (sospensione, cambio di rete, server che chiude dopo un po'): si riprova una volta.
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

    /// <summary>
    /// La cartella aperta e aggiornata, per la prima pagina dell'elenco (anche di Gmail). La connessione principale resta
    /// ferma sulla cartella aperta, e una connessione che tiene aperta una cartella la vede com'era all'ultimo annuncio del
    /// server: la pagina si conta sui messaggi che la connessione conosce (Count), e quelli arrivati dopo restavano fuori.
    /// L'elenco riletto (cartella scelta di nuovo, passaggio da un account all'altro, rilettura dopo un avviso dell'IDLE)
    /// usciva senza i messaggi appena notificati, e toglieva anche le righe che il controllo aveva messo in cima: la notifica
    /// puntuale, il numero dei non letti giusto, e nell'elenco tutti letti (segnalazioni dell'autore, 7 ottobre 2026). Come per i
    /// nuovi messaggi (FreshInboxAsync): se era già aperta, un NOOP, con cui il server annuncia gli arrivi; se si apre ora
    /// (SELECT) è già aggiornata e non serve.
    /// </summary>
    internal static async Task<IMailFolder> OpenFreshAsync(ImapClient client, string folderId, Func<ImapClient, string, Task<IMailFolder>> open)
    {
        var folder = folderId.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? client.Inbox : await client.GetFolderAsync(folderId);
        bool wasOpen = folder.IsOpen && folder.Access == FolderAccess.ReadWrite;
        var f = await open(client, folderId);
        if (wasOpen) await client.NoOpAsync();
        return f;
    }

    /// <summary>In sola lettura (EXAMINE), per chi legge soltanto: anteprime, corpi in anticipo, nuovi messaggi.</summary>
    static async Task<IMailFolder> ExamineAsync(ImapClient client, string folderId)
    {
        var f = folderId.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? client.Inbox : await client.GetFolderAsync(folderId);
        if (!f.IsOpen) await f.OpenAsync(FolderAccess.ReadOnly);
        return f;
    }

    // ---------------------------------------------------------------- cartelle

    /// <summary>
    /// Le cartelle speciali senza SPECIAL-USE, dai nomi più comuni (anche dentro INBOX, come INBOX.Sent sui server
    /// Courier e cPanel), in inglese, italiano, francese e tedesco.
    /// </summary>
    static readonly Dictionary<FolderKind, string[]> SpecialNames = new()
    {
        [FolderKind.Drafts] = ["Drafts", "Draft", "Bozze", "Brouillons", "Entwürfe"],
        [FolderKind.Sent] = ["Sent", "Sent Items", "Sent Messages", "Sent Mail", "Posta inviata", "Inviati", "Inviata", "Elementi inviati",
                             "Messaggi inviati", "Envoyés", "Éléments envoyés", "Gesendet", "Gesendete Objekte"],
        [FolderKind.Deleted] = ["Trash", "Deleted", "Deleted Items", "Deleted Messages", "Cestino", "Posta eliminata", "Elementi eliminati",
                                "Eliminati", "Corbeille", "Papierkorb", "Gelöschte Elemente"],
        [FolderKind.Junk] = ["Junk", "Spam", "Junk E-mail", "Junk Email", "Bulk Mail", "Posta indesiderata", "Indesiderata", "Indésirables", "Junk-E-Mail"],
        [FolderKind.Archive] = ["Archive", "Archives", "Archivio", "Archiv"],
    };

    static readonly (FolderKind Kind, FolderAttributes Attribute, string Name)[] Specials =
    {
        (FolderKind.Drafts, FolderAttributes.Drafts, "Bozze"),
        (FolderKind.Sent, FolderAttributes.Sent, "Posta inviata"),
        (FolderKind.Deleted, FolderAttributes.Trash, "Cestino"),
        (FolderKind.Archive, FolderAttributes.Archive, "Archivio"),
        (FolderKind.Junk, FolderAttributes.Junk, "Posta indesiderata"),
    };

    /// <summary>Le cartelle speciali trovate (tipo → percorso), per bozze, Posta inviata e cartelle del messaggio.</summary>
    readonly Dictionary<FolderKind, string> special = new();

    static bool Selectable(IMailFolder f) => !f.Attributes.HasFlag(FolderAttributes.NoSelect) && !f.Attributes.HasFlag(FolderAttributes.NonExistent);

    public Task<List<MailFolder>> GetFoldersAsync() => RunAsync(async client =>
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        IList<IMailFolder> all;
        try
        {
            // Con LIST-STATUS una sola richiesta; senza, MailKit chiede STATUS cartella per cartella.
            all = await client.GetFoldersAsync(client.PersonalNamespaces[0], StatusItems.Unread | StatusItems.Count);
        }
        catch (ImapCommandException ex)
        {
            Log.Write($"cartelle di {Account.Email} senza conteggi: {ex.Message}");
            all = await client.GetFoldersAsync(client.PersonalNamespaces[0]);
        }
        Log.Time($"cartelle e non letti di {Account.Email} ({all.Count})", clock);
        FindSpecial(client, all);

        var result = new List<MailFolder>();
        void Add(IMailFolder f, FolderKind kind, string name, bool top)
        {
            int cut = f.FullName.LastIndexOf(f.DirectorySeparator);
            result.Add(new MailFolder
            {
                Id = f.FullName, Name = name, Kind = kind, Unread = f.Unread, Total = f.Count,
                ParentId = top || cut <= 0 ? null : f.FullName[..cut],
            });
        }
        Add(all.FirstOrDefault(f => f.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) ?? client.Inbox, FolderKind.Inbox, "Posta in arrivo", true);
        foreach (var (kind, _, name) in Specials)
            if (special.TryGetValue(kind, out var id) && all.FirstOrDefault(f => f.FullName == id) is { } f) Add(f, kind, name, true);
        var used = result.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in all.Where(f => !used.Contains(f.FullName) && Selectable(f)).OrderBy(f => f.FullName, StringComparer.CurrentCultureIgnoreCase))
            Add(f, FolderKind.Other, f.Name, false);
        return result;
    });

    /// <summary>
    /// Quanto è in fondo una cartella: 0 alla radice o subito sotto INBOX (INBOX.Sent dei server Courier e cPanel). Le
    /// cartelle speciali trovate dal nome stanno lì: INBOX.Clienti.Rossi.Archivio è una cartella del cliente, non l'Archivio.
    /// </summary>
    static int Depth(IMailFolder f)
    {
        if (f.DirectorySeparator == '\0') return 0;
        var parts = f.FullName.Split(f.DirectorySeparator);
        return parts.Length - (parts.Length > 1 && parts[0].Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
    }

    /// <summary>
    /// Prima gli attributi SPECIAL-USE (o XLIST), poi i nomi, solo in cima (Depth 0): la prima cartella che corrisponde, per
    /// ogni tipo. Prima valevano i nomi a qualsiasi profondità, e una sottocartella dell'utente diventava Archivio o Cestino.
    /// </summary>
    void FindSpecial(ImapClient client, IList<IMailFolder> all)
    {
        special.Clear();
        foreach (var (kind, attribute, _) in Specials)
        {
            var found = all.FirstOrDefault(f => Selectable(f) && f.Attributes.HasFlag(attribute))
                        ?? all.FirstOrDefault(f => Selectable(f) && Depth(f) == 0 && SpecialNames[kind].Contains(f.Name, StringComparer.OrdinalIgnoreCase)
                                                   && !special.ContainsValue(f.FullName));
            if (found != null) special[kind] = found.FullName;
        }
        Log.Write($"cartelle speciali di {Account.Email} ({(client.Capabilities.HasFlag(ImapCapabilities.SpecialUse) ? "SPECIAL-USE" : "dai nomi")}): " +
                  string.Join(", ", special.Select(s => $"{s.Key} {s.Value}")));
    }

    async Task<string?> SpecialAsync(ImapClient client, FolderKind kind)
    {
        if (special.Count == 0) FindSpecial(client, await client.GetFoldersAsync(client.PersonalNamespaces[0]));
        return special.GetValueOrDefault(kind);
    }

    public Task<MailFolder> CreateFolderAsync(string name, MailFolder? parent) => RunAsync(async client =>
    {
        var container = parent == null ? client.GetFolder(client.PersonalNamespaces[0]) : await client.GetFolderAsync(parent.Id);
        var created = await container.CreateAsync(name.Trim(), true) ?? throw new MailException($"Il server non ha creato la cartella {name.Trim()}.");
        return new MailFolder { Id = created.FullName, Name = created.Name, Kind = FolderKind.Other, ParentId = parent?.Id };
    });

    public Task<string> RenameFolderAsync(MailFolder folder, string name) => RunAsync(async client =>
    {
        var f = await client.GetFolderAsync(folder.Id);
        if (f.IsOpen) await f.CloseAsync();
        await f.RenameAsync(f.ParentFolder ?? client.GetFolder(client.PersonalNamespaces[0]), name.Trim());
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

    public Task DeleteFolderAsync(MailFolder folder) => RunAsync(async client =>
    {
        var f = await client.GetFolderAsync(folder.Id);
        if (f.IsOpen) await f.CloseAsync();
        await f.DeleteAsync();
    });

    /// <summary>
    /// Cestino e Posta indesiderata si svuotano per sempre; le altre cartelle nel Cestino. Senza Cestino una cartella normale
    /// non si svuota: prima diventava un'eliminazione definitiva, dopo la domanda "Dal Cestino si possono ancora recuperare".
    /// </summary>
    public Task EmptyFolderAsync(MailFolder folder, MailFolder? trash) => InboxIdle.OwnAsync(Account.Id, () => RunAsync(async client =>
    {
        bool permanent = folder.Kind is FolderKind.Deleted or FolderKind.Junk;
        if (!permanent && trash == null) throw new MailException($"L'account non ha un Cestino: {folder.Name} non si svuota, perché i messaggi andrebbero persi per sempre.");
        var f = await OpenAsync(client, folder.Id);
        if (f.Count == 0) return;
        var all = await f.SearchAsync(SearchQuery.All);
        if (permanent) await DeleteNowAsync(client, f, all);
        else await MoveUidsAsync(client, f, all, await client.GetFolderAsync(trash!.Id));
    }));

    /// <summary>
    /// Il Cestino, creato al primo bisogno come fa Thunderbird: su una casella nuova molti server (Dovecot, cPanel) lo
    /// creano solo quando un programma lo chiede, e con un nome che Missiva non conosce non si trova. "Trash" nella radice
    /// dello spazio dei nomi personale (dentro INBOX sui server Courier e cPanel). Prima si rilegge l'elenco: forse l'ha
    /// creato intanto un altro programma.
    /// </summary>
    public Task<MailFolder?> EnsureTrashAsync() => RunAsync<MailFolder?>(async client =>
    {
        FindSpecial(client, await client.GetFoldersAsync(client.PersonalNamespaces[0]));
        if (special.GetValueOrDefault(FolderKind.Deleted) is not { } id)
        {
            var created = await client.GetFolder(client.PersonalNamespaces[0]).CreateAsync("Trash", true)
                          ?? throw new MailException("Il server non ha creato il Cestino.");
            id = created.FullName;
            special[FolderKind.Deleted] = id;
            Log.Write($"Cestino creato in {Account.Email}: {id}");
        }
        return new MailFolder { Id = id, Name = "Cestino", Kind = FolderKind.Deleted };
    });

    public Task MarkAllReadAsync(MailFolder folder) => InboxIdle.OwnAsync(Account.Id, () => RunAsync(async client =>
    {
        var f = await OpenAsync(client, folder.Id);
        var unread = await f.SearchAsync(SearchQuery.NotSeen);
        if (unread.Count > 0) await f.AddFlagsAsync(unread, MessageFlags.Seen, true);
    }));

    public Task<MailFolder> EnsureFolderAsync(string name) => RunAsync(async client =>
    {
        // Solo in cima, dove la si crea: una sottocartella dell'utente con lo stesso nome non è quella di Missiva.
        var existing = (await client.GetFoldersAsync(client.PersonalNamespaces[0]))
            .FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && Selectable(f) && Depth(f) == 0);
        var folder = existing ?? await client.GetFolder(client.PersonalNamespaces[0]).CreateAsync(name, true)
                     ?? throw new MailException($"Il server non ha creato la cartella {name}.");
        return new MailFolder { Id = folder.FullName, Name = folder.Name, Kind = FolderKind.Other };
    });

    // ---------------------------------------------------------------- categorie (parole chiave di IMAP)

    /// <summary>
    /// Le categorie sono parole chiave di IMAP (come i tag di Thunderbird), che il server tiene sul messaggio. Una parola
    /// chiave non ha spazi né parentesi: il nome mostrato resta in Missiva (Settings.LocalCategories).
    /// </summary>
    static string Keyword(string name) =>
        new(name.Trim().Select(c => c <= ' ' || c >= 127 || "(){%*\"\\]".Contains(c) ? '_' : c).ToArray());

    public Task<List<Category>> GetCategoriesAsync() =>
        Task.FromResult(Store.Settings.LocalCategories.GetValueOrDefault(Account.Id, new()).Select(n => new Category(n, CategoryPalette.PresetOf(Account.Id, n))).ToList());

    public Task<Category> CreateCategoryAsync(string name, int preset)
    {
        name = name.Trim();
        if (!Store.Settings.LocalCategories.TryGetValue(Account.Id, out var local)) Store.Settings.LocalCategories[Account.Id] = local = new();
        if (!local.Contains(name)) local.Add(name);
        Store.Settings.CategoryColors[Account.Id + "|" + name] = preset;
        Store.Save();
        return Task.FromResult(new Category(name, preset));
    }

    public async Task SetCategoryAsync(IReadOnlyList<MessageSummary> messages, string category, bool on)
    {
        await EachFolderAsync(messages, (_, f, uids, _) =>
        {
            var request = new StoreFlagsRequest(on ? StoreAction.Add : StoreAction.Remove, MessageFlags.None) { Silent = true };
            request.Keywords.Add(Keyword(category));
            return f.StoreAsync(uids, request);
        }, affectsUnread: false);
        foreach (var message in messages)
        {
            message.Categories.RemoveAll(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
            if (on) message.Categories.Add(category);
        }
    }

    /// <summary>Le parole chiave del messaggio come categorie: quelle di sistema ($Forwarded, $MDNSent, Junk...) no.</summary>
    List<string> CategoriesOf(IMessageSummary i)
    {
        var local = Store.Settings.LocalCategories.GetValueOrDefault(Account.Id, new());
        return (i.Keywords ?? (IEnumerable<string>)[])
            .Where(k => !k.StartsWith('$') && !k.Equals("Junk", StringComparison.OrdinalIgnoreCase) && !k.Equals("NonJunk", StringComparison.OrdinalIgnoreCase)
                        && !k.Equals("NotJunk", StringComparison.OrdinalIgnoreCase))
            .Select(k => local.FirstOrDefault(n => Keyword(n).Equals(k, StringComparison.OrdinalIgnoreCase)) ?? k).ToList();
    }

    // ---------------------------------------------------------------- messaggi

    public bool SortsOnServer(SortBy sort) => sort == SortBy.Date;

    readonly Dictionary<string, List<UniqueId>> searches = new();

    /// <summary>Un risultato della ricerca in tutte le cartelle: dove sta e quando è arrivato (per unire le cartelle per data).</summary>
    sealed record Hit(string FolderId, string FolderName, UniqueId Uid, DateTimeOffset Date);

    /// <summary>
    /// La ricerca in tutte le cartelle in corso o appena finita. Le stesse parole la riusano (i suggerimenti dopo la pausa,
    /// poi Invio, poi le pagine dopo) invece di rifarla da capo; Waiting conta chi la aspetta, e quando nessuno la aspetta
    /// più (suggerimenti superati da altre lettere) si ferma alla cartella dopo.
    /// </summary>
    sealed class Everywhere(string query)
    {
        public readonly string Query = query;
        public DateTime Started = DateTime.UtcNow;
        public Task<List<Hit>> Hits = Task.FromResult(new List<Hit>());
        public int Waiting;
    }

    Everywhere? everywhere;
#if DEBUG
    /// <summary>Prove: il lavoro dell'ultima ricerca in tutte le cartelle, per aspettarne la fine vera anche quando nessuno la aspetta più.</summary>
    internal Task? TestEverywhere => everywhere?.Hits;
    /// <summary>Prove: l'ultima ricerca in tutte le cartelle come se fosse cominciata tanto tempo prima.</summary>
    internal void TestAgeSearch(TimeSpan by)
    {
        if (everywhere is { } search) search.Started -= by;
    }
#endif
    /// <summary>
    /// Per quanto le stesse parole riusano la ricerca in tutte le cartelle già fatta (le pagine dopo sempre): la stessa
    /// finestra di Invio sui suggerimenti (MailLimits.SearchReuseSeconds), così quando Invio la rifà è nuova. Prima 60 secondi.
    /// </summary>
    internal static readonly TimeSpan ReuseSearch = TimeSpan.FromSeconds(MailLimits.SearchReuseSeconds);

    void Remember(string key, List<UniqueId> uids)
    {
        bool search = key.StartsWith("cerca|", StringComparison.Ordinal);
        foreach (var old in searches.Keys.Where(k => k.StartsWith("cerca|", StringComparison.Ordinal) == search).ToList()) searches.Remove(old);
        searches[key] = uids;
    }

    public Task<MessagePage> GetMessagesAsync(MailFolder folder, bool? focused, string? next, ListOptions? options = null) => RunAsync(async client =>
    {
        options ??= ListOptions.Default;
        var f = next == null ? await OpenFreshAsync(client, folder.Id, OpenAsync) : await OpenAsync(client, folder.Id);
        if (options.Filter != MessageFilter.All || options.UpTo != null || options.Ascending) return await FilteredPageAsync(f, folder, next, options);
        int end = next != null ? int.Parse(next) : f.Count - 1;
        if (end < 0) return new MessagePage();
        int start = Math.Max(0, end - PageSize + 1);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var items = await f.FetchAsync(start, end, MimeOptions.ListRequest(ListItems));
        Log.Time($"{items.Count} messaggi di {folder.Name} ({Account.Email})", clock);
        return new MessagePage
        {
            // Quelli segnati \Deleted e non ancora eliminati (spostati da un server senza UIDPLUS) non si mostrano.
            // Come Gmail: nell'ordine della data mostrata, a parità l'ordine di arrivo.
            Messages = items.Where(i => i.Flags?.HasFlag(MessageFlags.Deleted) != true).OrderByDescending(i => i.Index).Select(i => Summary(i, folder.Id))
                .OrderByDescending(m => m.Date).ToList(),
            Next = start > 0 ? (start - 1).ToString() : null,
        };
    });

    /// <summary>Filtri, "Vai a" e ordine dal più vecchio con la ricerca di IMAP su tutta la cartella, a pagine di 50.</summary>
    async Task<MessagePage> FilteredPageAsync(IMailFolder f, MailFolder folder, string? next, ListOptions options)
    {
        var key = folder.Id + "|" + options;
        if (next == null || !searches.TryGetValue(key, out var uids))
        {
            SearchQuery query = SearchQuery.NotDeleted;
            if (options.Filter == MessageFilter.Unread) query = query.And(SearchQuery.NotSeen);
            if (options.Filter == MessageFilter.Flagged) query = query.And(SearchQuery.Flagged);
            if (options.Filter == MessageFilter.Attachments) query = query.And(SearchQuery.HeaderContains("Content-Type", "multipart/mixed"));
            if (options.UpTo is { } upTo) query = query.And(SearchQuery.DeliveredBefore(upTo.Date.AddDays(1)));
            uids = (await f.SearchAsync(query)).OrderBy(u => u.Id).ToList();
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

    public Task<Dictionary<string, string>> GetPreviewsAsync(MailFolder folder, IReadOnlyList<MessageSummary> messages) => RunAsync(async client =>
    {
        var result = new Dictionary<string, string>();
        if (messages.Count == 0) return result;
        // I risultati della ricerca in tutte le cartelle vengono da cartelle diverse.
        foreach (var group in messages.GroupBy(m => m.FolderId))
        {
            var f = await ExamineAsync(client, group.Key);
            var items = await f.FetchAsync(group.Select(Uid).ToList(), MessageSummaryItems.UniqueId | MessageSummaryItems.PreviewText);
            foreach (var i in items) result[i.UniqueId.Id.ToString()] = MailHtml.PlainPreview(i.PreviewText);
        }
        return result;
    }, background);

    /// <summary>La ricerca di IMAP (SEARCH): oggetto, mittente, destinatari e testo.</summary>
    static SearchQuery TextQuery(string words) =>
        SearchQuery.NotDeleted.And(SearchQuery.SubjectContains(words).Or(SearchQuery.FromContains(words)).Or(SearchQuery.ToContains(words))
            .Or(SearchQuery.CcContains(words)).Or(SearchQuery.BodyContains(words)));

    /// <summary>La ricerca con i filtri (SearchTerms): le parole libere come TextQuery, poi i criteri dei filtri.</summary>
    static SearchQuery QueryOf(string query)
    {
        var terms = SearchTerms.Parse(query);
        return Criteria(terms.Text.Trim().Length > 0 ? TextQuery(terms.Text.Trim()) : SearchQuery.NotDeleted, terms);
    }

    /// <summary>
    /// I filtri della ricerca come criteri di SEARCH (RFC 3501): FROM, TO, SUBJECT, SINCE e BEFORE (giorni interi, il giorno
    /// dopo "Al" escluso), UNSEEN, FLAGGED; gli allegati come l'elenco, dal multipart/mixed dell'intestazione.
    /// </summary>
    internal static SearchQuery Criteria(SearchQuery query, SearchTerms terms)
    {
        if (terms.From.Length > 0) query = query.And(SearchQuery.FromContains(terms.From));
        if (terms.To.Length > 0) query = query.And(SearchQuery.ToContains(terms.To));
        if (terms.Subject.Length > 0) query = query.And(SearchQuery.SubjectContains(terms.Subject));
        if (terms.Since is { } since) query = query.And(SearchQuery.DeliveredAfter(since.Date));
        if (terms.Until is { } until) query = query.And(SearchQuery.DeliveredBefore(until.Date.AddDays(1)));
        if (terms.Unread) query = query.And(SearchQuery.NotSeen);
        if (terms.Flagged) query = query.And(SearchQuery.Flagged);
        if (terms.HasAttachments) query = query.And(SearchQuery.HeaderContains("Content-Type", "multipart/mixed"));
        return query;
    }

    /// <summary>
    /// In una cartella a pagine di 50, sulla connessione principale (dove la cartella aperta è già scelta). In tutte (le
    /// cartelle selezionabili tranne Cestino e Posta indesiderata, come nel nuovo Outlook) sulla connessione in sottofondo,
    /// così aprire un messaggio non la aspetta: prima solo SEARCH e la data dei 100 più recenti di ogni cartella, uniti per
    /// data, poi busta e struttura dei soli 50 della pagina. Prima ogni pausa nella casella Cerca faceva due ricerche uguali
    /// sulla connessione principale, con busta e struttura di tutti i risultati, e Invio la rifaceva.
    /// </summary>
    public async Task<MessagePage> SearchAsync(MailFolder? folder, string query, string? next = null, CancellationToken cancel = default)
    {
        var words = query.Trim();
        if (folder != null) return await RunAsync(client => SearchFolderAsync(client, folder, words, next), main, cancel);
        var search = everywhere;
        if (search == null || search.Query != words || search.Hits.IsFaulted || search.Hits.IsCanceled
            || next == null && search.Hits.IsCompleted && DateTime.UtcNow - search.Started > ReuseSearch)
        {
            everywhere = search = new Everywhere(words) { Waiting = 1 };
            search.Hits = FindEverywhereAsync(search);
        }
        else Interlocked.Increment(ref search.Waiting);
        List<Hit> hits;
        try
        {
            hits = await search.Hits.WaitAsync(cancel);
        }
        finally
        {
            Interlocked.Decrement(ref search.Waiting);
        }
        int start = next != null ? int.Parse(next) : 0;
        var slice = hits.Skip(start).Take(PageSize).ToList();
        var messages = slice.Count == 0 ? new List<MessageSummary>() : await RunAsync(client => FetchHitsAsync(client, slice), background, cancel);
        return new MessagePage { Messages = messages, Next = start + PageSize < hits.Count ? (start + PageSize).ToString() : null };
    }

    async Task<MessagePage> SearchFolderAsync(ImapClient client, MailFolder folder, string words, string? next)
    {
        var f = await OpenAsync(client, folder.Id);
        var key = "cerca|" + folder.Id + "|" + words;
        if (next == null || !searches.TryGetValue(key, out var uids))
        {
            uids = (await f.SearchAsync(QueryOf(words))).OrderByDescending(u => u.Id).ToList();
            Remember(key, uids);
        }
        int offset = next != null ? int.Parse(next) : 0;
        var slice = uids.Skip(offset).Take(PageSize).ToList();
        if (slice.Count == 0) return new MessagePage();
        var items = await f.FetchAsync(slice, MimeOptions.ListRequest(ListItems));
        return new MessagePage
        {
            Messages = items.OrderByDescending(i => i.InternalDate).Select(i => Summary(i, folder.Id)).ToList(),
            Next = offset + PageSize < uids.Count ? (offset + PageSize).ToString() : null,
        };
    }

    /// <summary>
    /// SEARCH cartella per cartella e, dei risultati, solo UID e data di arrivo (i 100 più recenti di ognuna). Fra una
    /// cartella e l'altra, se nessuno aspetta più questa ricerca, ci si ferma.
    /// </summary>
    Task<List<Hit>> FindEverywhereAsync(Everywhere search) => RunAsync(async client =>
    {
        void StopIfUnwanted()
        {
            if (Volatile.Read(ref search.Waiting) == 0) throw new OperationCanceledException($"ricerca di \"{search.Query}\" non più richiesta");
        }
        StopIfUnwanted();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var query = QueryOf(search.Query);
        var hits = new List<Hit>();
        var all = await client.GetFoldersAsync(client.PersonalNamespaces[0]);
        if (special.Count == 0) FindSpecial(client, all);
        var skip = new[] { special.GetValueOrDefault(FolderKind.Deleted), special.GetValueOrDefault(FolderKind.Junk) };
        int searched = 0;
        foreach (var f in all.Where(f => Selectable(f) && !skip.Contains(f.FullName)))
        {
            StopIfUnwanted();
            try
            {
                var open = await ExamineAsync(client, f.FullName);
                searched++;
                var uids = (await open.SearchAsync(query)).OrderByDescending(u => u.Id).Take(100).ToList();
                if (uids.Count == 0) continue;
                var name = f.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? "Posta in arrivo"
                         : Specials.FirstOrDefault(s => special.GetValueOrDefault(s.Kind) == f.FullName).Name ?? f.Name;
                foreach (var i in await open.FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate))
                    hits.Add(new Hit(f.FullName, name, i.UniqueId, i.InternalDate ?? DateTimeOffset.MinValue));
            }
            catch (ImapCommandException ex)
            {
                Log.Write($"ricerca in {Log.Private(f.FullName)} non riuscita: {ex.Message}"); // una cartella che il server non apre: si va avanti
            }
        }
        Log.Time($"ricerca in tutte le cartelle di {Account.Email}: {hits.Count} risultati in {searched} cartelle", clock);
        return hits.OrderByDescending(h => h.Date).ToList();
    }, background);

    /// <summary>Busta, struttura e intestazioni dei soli risultati della pagina, cartella per cartella.</summary>
    async Task<List<MessageSummary>> FetchHitsAsync(ImapClient client, List<Hit> page)
    {
        var found = new List<MessageSummary>();
        foreach (var group in page.GroupBy(h => h.FolderId))
        {
            try
            {
                var open = await ExamineAsync(client, group.Key);
                foreach (var i in await open.FetchAsync(group.Select(h => h.Uid).ToList(), MimeOptions.ListRequest(ListItems)))
                {
                    if (i.Flags?.HasFlag(MessageFlags.Deleted) == true) continue; // spostato intanto da un server senza UIDPLUS
                    var m = Summary(i, group.Key);
                    m.FolderName = group.First().FolderName;
                    found.Add(m);
                }
            }
            catch (ImapCommandException ex)
            {
                Log.Write($"risultati in {group.Key} non letti: {ex.Message}");
            }
        }
        return found.OrderByDescending(m => m.Date).ToList();
    }

    /// <summary>
    /// La Posta in arrivo aperta e aggiornata, per i controlli dei nuovi messaggi e dei non letti. Una connessione che la tiene
    /// aperta da tempo la vede com'era quando l'ha aperta: Gmail non mette nella SEARCH i messaggi arrivati dopo finché non li
    /// annuncia, e li annuncia con NOOP. Il 27 settembre 2026, con l'IDLE sulla sua connessione, la connessione in sottofondo
    /// restava ferma sulla Posta in arrivo: l'IDLE vedeva il messaggio nuovo e il controllo ne trovava zero, tre volte, e
    /// nessuna notifica partiva (prima la connessione cambiava spesso cartella e la vista si rinfrescava da sola).
    /// </summary>
    /// <param name="counting">
    /// Solo per il conteggio dei non letti, che App.CheckAccountAsync chiede subito dopo GetNewAsync sulla stessa connessione:
    /// se la Posta in arrivo è stata aggiornata qui da meno di RecentRefresh il NOOP non si ripete (audit, v2 V-36). La ricerca
    /// dei messaggi nuovi lo fa sempre.
    /// </param>
    internal static async Task<IMailFolder> FreshInboxAsync(ImapClient client, bool counting = false)
    {
        var f = client.Inbox;
        var last = refreshed.GetOrCreateValue(client);
        if (!f.IsOpen) await f.OpenAsync(FolderAccess.ReadOnly);
        else if (counting && Environment.TickCount64 - last.Value < RecentRefresh) return f;
        else await client.NoOpAsync();
        last.Value = Environment.TickCount64;
        return f;
    }

    const long RecentRefresh = 2000;
    /// <summary>Quando FreshInboxAsync ha aggiornato la Posta in arrivo di ogni connessione (Environment.TickCount64).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImapClient, System.Runtime.CompilerServices.StrongBox<long>> refreshed = new();

    /// <summary>
    /// Le date di arrivo dei non letti recenti, per UID. Non cambiano: si chiedono al server una volta, e a ogni controllo
    /// dei nuovi messaggi solo quelle degli UID mai visti, di solito nessuna (audit, v2 V-36: prima una FETCH delle date di
    /// tutti i non letti da ieri a ogni controllo). Con una UIDVALIDITY nuova si riparte da zero.
    /// </summary>
    internal sealed class ArrivalDates
    {
        uint validity;
        readonly Dictionary<uint, DateTime> known = new();

        /// <summary>Fra questi UID, quelli arrivati dopo sinceUtc: gli stessi che dava la FETCH delle date di tutti.</summary>
        public async Task<List<UniqueId>> AfterAsync(IMailFolder inbox, IList<UniqueId> uids, DateTime sinceUtc)
        {
            List<UniqueId> unknown;
            lock (known)
            {
                if (inbox.UidValidity != validity)
                {
                    known.Clear();
                    validity = inbox.UidValidity;
                }
                unknown = uids.Where(u => !known.ContainsKey(u.Id)).ToList();
            }
            IList<IMessageSummary> fetched = unknown.Count == 0 ? [] : await inbox.FetchAsync(unknown, MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate);
            lock (known)
            {
                foreach (var i in fetched)
                    if (i.InternalDate is { } date) known[i.UniqueId.Id] = date.UtcDateTime;
                // Restano solo i non letti di adesso: un messaggio letto esce, e se torna da leggere la data si richiede.
                var now = uids.Select(u => u.Id).ToHashSet();
                foreach (var gone in known.Keys.Where(k => !now.Contains(k)).ToList()) known.Remove(gone);
                return uids.Where(u => known.TryGetValue(u.Id, out var date) && date > sinceUtc).ToList();
            }
        }
    }

    readonly ArrivalDates arrivalDates = new();

    public Task<List<MessageSummary>> GetNewAsync(DateTime sinceUtc) => RunAsync(async client =>
    {
        var f = await FreshInboxAsync(client);
        var uids = await f.SearchAsync(SearchQuery.NotSeen.And(SearchQuery.NotDeleted).And(SearchQuery.DeliveredAfter(sinceUtc.ToLocalTime().Date.AddDays(-1))));
        if (uids.Count == 0) return new List<MessageSummary>();
        var fresh = await arrivalDates.AfterAsync(f, uids.Reverse().Take(20).ToList(), sinceUtc);
        if (fresh.Count == 0) return new List<MessageSummary>();
        var items = await f.FetchAsync(fresh, MimeOptions.ListRequest(SummaryItems));
        return items.Select(i => Summary(i, "INBOX")).ToList();
    }, background);

    /// <summary>Le regole di Missiva: i messaggi arrivati dopo il mark (ArrivedAsync), sulla connessione in sottofondo in sola lettura.</summary>
    public Task<Arrivals?> GetArrivedAsync(string? mark, DateTime sinceUtc) => RunAsync(async client =>
    {
        var f = await FreshInboxAsync(client);
        var (items, next) = await ArrivedAsync(f, mark, sinceUtc, MimeOptions.ListRequest(ListItems), SearchQuery.NotDeleted);
        return (Arrivals?)new Arrivals(items.Select(i => Summary(i, "INBOX")).ToList(), next);
    }, background);

    /// <summary>Quanti messaggi le regole guardano al massimo per volta: i seguenti al controllo dopo, dal mark.</summary>
    const int ArrivedMax = 500;

    /// <summary>
    /// Per le regole di Missiva (anche Gmail), sulla Posta in arrivo già aperta in sola lettura: gli UID dopo quello del mark
    /// con la stessa UIDVALIDITY, letti o no, dal più vecchio, al massimo ArrivedMax; senza mark (la prima volta, o con una
    /// UIDVALIDITY nuova) quelli arrivati dopo sinceUtc. Prima le regole vedevano solo i 20 non letti più recenti di ogni
    /// controllo: dopo una notte con il portatile chiuso gli altri restavano in Posta in arrivo. Il mark nuovo è
    /// "UIDVALIDITY:UID" dell'ultimo guardato.
    /// </summary>
    internal static async Task<(IList<IMessageSummary> Items, string? Mark)> ArrivedAsync(IMailFolder inbox, string? mark, DateTime sinceUtc, IFetchRequest request, SearchQuery? also = null)
    {
        uint validity = inbox.UidValidity, last = 0;
        bool known = mark?.Split(':') is [var v, var u] && uint.TryParse(v, out var was) && was == validity && uint.TryParse(u, out last);
        SearchQuery query = known
            ? SearchQuery.Uids(new UniqueIdRange(new UniqueId(validity, last + 1), UniqueId.MaxValue))
            : SearchQuery.DeliveredAfter(sinceUtc.ToLocalTime().Date.AddDays(-1));
        if (also != null) query = query.And(also);
        // "UID n:*" dà sempre almeno il più recente, anche se è già stato guardato: contano solo quelli dopo last.
        var uids = (await inbox.SearchAsync(query)).Where(x => x.Id > last).OrderBy(x => x.Id).Take(ArrivedMax).ToList();
        IList<IMessageSummary> items = uids.Count == 0 ? [] : await inbox.FetchAsync(uids, request);
        if (!known) items = items.Where(i => i.InternalDate?.UtcDateTime > sinceUtc).ToList();
        uint top = uids.Count > 0 ? uids[^1].Id : known ? last : inbox.UidNext is { Id: > 1 } upcoming ? upcoming.Id - 1 : 0;
        return (items, top > 0 ? $"{validity}:{top}" : null);
    }

    /// <summary>
    /// Lavoro senza rete: i messaggi della Posta in arrivo degli ultimi giorni, dal più recente, con busta, struttura e
    /// dimensione (senza anteprime), sulla connessione in sottofondo con EXAMINE: poi PrefetchAsync ne scarica i corpi.
    /// </summary>
    public Task<List<MessageSummary>> GetRecentAsync(MailFolder inbox, DateTime sinceUtc, int max) => RunAsync(async client =>
    {
        var f = client.Inbox;
        if (!f.IsOpen) await f.OpenAsync(FolderAccess.ReadOnly);
        var uids = await f.SearchAsync(SearchQuery.NotDeleted.And(SearchQuery.DeliveredAfter(sinceUtc.ToLocalTime().Date.AddDays(-1))));
        if (uids.Count == 0) return new List<MessageSummary>();
        var items = await f.FetchAsync(uids.Reverse().Take(max).ToList(), ListItems);
        return items.Where(i => i.InternalDate?.UtcDateTime >= sinceUtc).OrderByDescending(i => i.InternalDate).Select(i => Summary(i, "INBOX")).ToList();
    }, background);

    /// <summary>
    /// Lo stesso, ma solo quello che serve a BodyCache.Has (UID, Message-Id con BODY.PEEK[HEADER.FIELDS (MESSAGE-ID)], segni e
    /// data): come in Gmail, la copia degli ultimi 30 giorni non rilegge ogni mezz'ora busta e struttura di tutti.
    /// </summary>
    public Task<List<MessageSummary>> GetRecentKeysAsync(MailFolder inbox, DateTime sinceUtc, int max) => RunAsync(async client =>
    {
        var f = client.Inbox;
        if (!f.IsOpen) await f.OpenAsync(FolderAccess.ReadOnly);
        var uids = await f.SearchAsync(SearchQuery.NotDeleted.And(SearchQuery.DeliveredAfter(sinceUtc.ToLocalTime().Date.AddDays(-1))));
        if (uids.Count == 0) return new List<MessageSummary>();
        var items = await f.FetchAsync(uids.Reverse().Take(max).ToList(),
            new FetchRequest(MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate | MessageSummaryItems.Flags, ["Message-Id"]));
        return items.Where(i => i.InternalDate?.UtcDateTime >= sinceUtc).OrderByDescending(i => i.InternalDate).Select(i => new MessageSummary
        {
            Id = i.UniqueId.Id.ToString(),
            FolderId = "INBOX",
            Date = (i.InternalDate ?? DateTimeOffset.MinValue).LocalDateTime,
            IsDraft = i.Flags?.HasFlag(MessageFlags.Draft) ?? false,
            // Senza < >, come Envelope.MessageId della riga intera: è quello che dà il nome al file della copia su disco.
            StableId = i.Headers?[HeaderId.MessageId] is { } id ? MimeKit.Utils.MimeUtils.ParseMessageId(id) ?? "" : "",
        }).ToList();
    }, background);

    /// <summary>Busta e struttura dei soli messaggi dati (quelli che mancano alla copia su disco), per scaricarne i corpi.</summary>
    public Task<List<MessageSummary>> GetRecentAsync(MailFolder inbox, IReadOnlyList<MessageSummary> keys) => RunAsync(async client =>
    {
        if (keys.Count == 0) return new List<MessageSummary>();
        var f = client.Inbox;
        if (!f.IsOpen) await f.OpenAsync(FolderAccess.ReadOnly);
        var items = await f.FetchAsync(keys.Select(Uid).ToList(), ListItems);
        return items.OrderByDescending(i => i.InternalDate).Select(i => Summary(i, "INBOX")).ToList();
    }, background);

    public Task<int> GetInboxUnreadAsync() => RunAsync(async client =>
    {
        var f = await FreshInboxAsync(client, counting: true);
        var query = SearchQuery.NotSeen.And(SearchQuery.NotDeleted);
        return client.Capabilities.HasFlag(ImapCapabilities.ESearch) ? (await f.SearchAsync(SearchOptions.Count, query)).Count : (await f.SearchAsync(query)).Count;
    }, background);

    public Task<List<SentRecipients>> GetSentRecipientsAsync(int max) => RunAsync(async client =>
    {
        if (await SpecialAsync(client, FolderKind.Sent) is not { } id) return new List<SentRecipients>();
        var sent = await ExamineAsync(client, id);
        if (sent.Count == 0) return new List<SentRecipients>();
        var items = await sent.FetchAsync(Math.Max(0, sent.Count - max), -1, MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate);
        return items.Select(i => new SentRecipients((i.InternalDate ?? i.Envelope?.Date ?? DateTimeOffset.MinValue).LocalDateTime,
            People(i.Envelope?.To).Concat(People(i.Envelope?.Cc)).ToList())).ToList();
    }, background);

    static List<Address> People(InternetAddressList? list) =>
        list?.Mailboxes.Select(a => new Address { Name = a.Name ?? "", Email = a.Address }).ToList() ?? new();

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
            Preview = MailHtml.PlainPreview(i.PreviewText),
            Date = (i.InternalDate ?? i.Envelope?.Date ?? DateTimeOffset.MinValue).LocalDateTime,
            IsRead = i.Flags?.HasFlag(MessageFlags.Seen) ?? true,
            IsFlagged = i.Flags?.HasFlag(MessageFlags.Flagged) ?? false,
            IsDraft = i.Flags?.HasFlag(MessageFlags.Draft) ?? false,
            HasAttachments = i.Attachments?.Any() ?? false,
            // Senza un identificativo di conversazione del server: la conversazione è il messaggio (Rispondi usa References).
            ConversationId = "",
            // Il Message-Id non cambia spostando il messaggio: ritrova bozze e posposti.
            StableId = i.Envelope?.MessageId ?? "",
            AttachmentNames = i.Attachments?.Select(a => a.FileName ?? "allegato").ToList() ?? new(),
            Categories = CategoriesOf(i),
            To = i.Envelope?.To.Mailboxes.Take(10).Select(a => new Address { Name = a.Name ?? "", Email = a.Address }).ToList() ?? new(),
            ReplyTo = Address.ReplyToOf(People(i.Envelope?.ReplyTo), new Address { Email = from?.Address ?? "" }),
            IsImportant = MimeOptions.IsImportant(i.Headers),
        };
    }

    public Task<List<MessageSummary>> GetConversationAsync(MessageSummary message, CancellationToken cancel = default) =>
        Task.FromResult(new List<MessageSummary> { message });

    static UniqueId Uid(MessageSummary m) => new(uint.Parse(m.Id));

    static string KeyOf(MessageSummary m) => m.FolderId + "|" + m.Id;

    public async Task<MessageBody> GetBodyAsync(MessageSummary message, CancellationToken cancel = default)
    {
        var key = KeyOf(message);
        if (Cached(key) is { } cached) return cached;
        if (Downloading(key) is { } coming)
        {
            try { return await coming.WaitAsync(cancel); }
            catch (Exception) when (!cancel.IsCancellationRequested) { }
        }
        return await RunAsync(async client =>
        {
            if (Cached(key) is { } body) return body;
            try { return await DownloadAsync(OpenAsync(client, message.FolderId), message, key, early: false); }
            catch (MessageNotFoundException)
            {
                // Appena arrivato e non ancora annunciato alla connessione ferma sulla cartella: NOOP e di nuovo (come Gmail).
                await client.NoOpAsync(cancel);
                return await DownloadAsync(OpenAsync(client, message.FolderId), message, key, early: false);
            }
        }, main, cancel);
    }

    public MessageBody? KeptBody(MessageSummary message)
    {
        lock (bodyCache) return bodyCache.GetValueOrDefault(KeyOf(message));
    }

    /// <summary>
    /// Dalla copia su disco anche in linea, come Gmail: un messaggio IMAP non cambia (UID e Message-Id nel nome del file).
    /// Solo con la struttura arrivata con l'elenco e senza inviti, che la copia non tiene.
    /// </summary>
    public MessageBody? SavedBody(MessageSummary message) => OnDisk(message) ? BodyCache.LoadOnline(Account, message) : null;

    /// <summary>
    /// Per una copia su disco che non ha mai letto List-Unsubscribe: la stessa richiesta leggera delle Authentication-Results
    /// (SenderCheck.FetchIfThereAsync, tre intestazioni e niente corpo), in sottofondo con EXAMINE.
    /// </summary>
    public Task<ListHeaders?> GetListHeadersAsync(MessageSummary message, CancellationToken cancel = default) => RunAsync(async client =>
    {
        var headers = await SenderCheck.FetchIfThereAsync(await ExamineAsync(client, message.FolderId), Uid(message));
        return headers == null ? null : new ListHeaders(headers["List-Unsubscribe"], headers["List-Unsubscribe-Post"]);
    }, background, cancel);

    bool OnDisk(MessageSummary message) =>
        message.StableId.Length > 0 && known.TryGetValue(KeyOf(message), out var s) && !Invitation.MayHave(s) && BodyCache.Has(Account, message);

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
            lock (bodyCache) if (downloading.GetValueOrDefault(key) == download) downloading.Remove(key);
        }
    }

    /// <summary>
    /// Da scaricare in anticipo (anche GmailProvider): i messaggi piccoli interi, quelli grandi (con allegati) solo nella
    /// parte da leggere, testo e immagini incorporate, se insieme stanno sotto ReadableLimit; gli allegati mai. Prima gli
    /// anticipi e la copia dei 30 giorni saltavano tutti i messaggi grandi, e con Gmail le mail con allegati si aprivano in
    /// 0,45-0,55 s invece di 0,06-0,13 (l'autore: "il ritardo più grande è con gli allegati", misurato il 28 settembre 2026).
    /// </summary>
    internal static bool Prefetchable(IMessageSummary s) =>
        s.Size is < SmallMessage || (s.Body != null && s.BodyParts.Where(p => !p.IsAttachment && (p.ContentType.IsMimeType("text", "*") || p.ContentId != null))
                                                                   .Sum(p => (long)p.Octets) < ReadableLimit);
    const long ReadableLimit = 2_000_000;

    /// <summary>Come in Gmail: i primi dell'elenco e i vicini di quello aperto, in sottofondo, con EXAMINE e BODY.PEEK.</summary>
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
            if (!Wanted() || !known.TryGetValue(key, out var s) || !Prefetchable(s) || OnDisk(message)) continue;
            try
            {
                if (await RunAsync(async client =>
                    {
                        if (!Wanted()) return false;
                        await DownloadAsync(ExamineAsync(client, message.FolderId), message, key, early: true);
                        return true;
                    }, background, cancel))
                    fetched++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Write($"corpo in anticipo non scaricato ({message.Id}): {ex.Message}");
            }
        }
        if (fetched > 0) Log.Time($"corpi in anticipo ({fetched} di {messages.Count})", clock);
    }

    /// <summary>
    /// Il testo e le immagini che il testo mostra: i messaggi piccoli interi, quelli grandi parte per parte (gli allegati
    /// quando si aprono). Sempre BODY.PEEK: leggere non segna letto.
    /// </summary>
    async Task<MessageBody> ReadBodyAsync(Task<IMailFolder> folder, MessageSummary message, string key, bool early)
    {
        var f = await folder;
        var uid = Uid(message);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (!known.TryGetValue(key, out var s))
            s = (await f.FetchAsync(new[] { uid }, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope |
                                                   MessageSummaryItems.BodyStructure | MessageSummaryItems.Size)).FirstOrDefault()
                ?? throw new MailException("Il messaggio non c'è più: forse è stato spostato o eliminato.");

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
        var headers = whole?.Headers ?? await SenderCheck.FetchAsync(f, uid);
        var authentication = SenderCheck.FromHeaders(headers);
        Log.Time($"corpo{(early ? " in anticipo" : "")} IMAP ({(whole != null ? "intero" : "solo testo e immagini")}, {s.Size / 1024} KB, {attachments.Count(a => !a.IsInline)} allegati)", clock);
        return new MessageBody
        {
            AuthenticationResults = authentication,
            ListUnsubscribe = headers["List-Unsubscribe"],
            ListUnsubscribePost = headers["List-Unsubscribe-Post"],
            Invite = await Invitation.FindAsync(f, uid, s, whole),
            Html = html,
            To = People(s.Envelope?.To),
            Cc = People(s.Envelope?.Cc),
            Bcc = People(s.Envelope?.Bcc),
            Attachments = attachments,
            Raw = raw,
        };
    }

    static async Task<byte[]> ContentOf(IMailFolder f, UniqueId uid, BodyPartBasic part)
    {
        using var ms = new MemoryStream();
        switch (await f.GetBodyPartAsync(uid, part))
        {
            case MimePart p when p.Content != null: await p.Content.DecodeToAsync(ms); break;
            case MessagePart m when m.Message != null: await m.Message.WriteToAsync(ms); break;
        }
        return ms.ToArray();
    }

    public Task<byte[]> GetAttachmentAsync(MessageSummary message, Attachment attachment) => AttachmentAsync(message, attachment, main);

    Task<byte[]> AttachmentAsync(MessageSummary message, Attachment attachment, Connection on)
    {
        if (attachment.Content != null) return Task.FromResult(attachment.Content);
        return RunAsync(async client =>
        {
            var f = await ExamineOrOpenAsync(client, message.FolderId, on);
            var uid = Uid(message);
            var s = (await f.FetchAsync(new[] { uid }, MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure)).FirstOrDefault();
            var part = s?.BodyParts.FirstOrDefault(p => p.PartSpecifier == attachment.Id) ?? throw new MailException("Allegato non trovato.");
            return await ContentOf(f, uid, part);
        }, on);
    }

    /// <summary>Sulla principale la cartella aperta in lettura e scrittura (come per le azioni), sulle altre in sola lettura.</summary>
    Task<IMailFolder> ExamineOrOpenAsync(ImapClient client, string folderId, Connection on) =>
        on == main ? OpenAsync(client, folderId) : ExamineAsync(client, folderId);

    public Task<byte[]> GetRawAsync(MessageSummary message, int limit = int.MaxValue, CancellationToken cancel = default) => RunAsync(async client =>
    {
        var f = await ExamineAsync(client, message.FolderId);
        await using var stream = limit < int.MaxValue ? await f.GetStreamAsync(Uid(message), "", 0, limit) : await f.GetStreamAsync(Uid(message), "");
        var raw = new byte[stream.Length];
        await stream.ReadExactlyAsync(raw);
        return raw;
    }, limit < int.MaxValue ? background : drafts, cancel);

    // ---------------------------------------------------------------- azioni

    /// <remarks>Sono scritture di Missiva: gli avvisi dell'IDLE che ne tornano (EXPUNGE, segni) sono il loro eco (InboxIdle.OwnAsync).</remarks>
    /// <param name="affectsUnread">Falso per contrassegni e categorie, che non cambiano i non letti: alla fine non si riconta.</param>
    Task EachFolderAsync(IReadOnlyList<MessageSummary> messages, Func<ImapClient, IMailFolder, IList<UniqueId>, string, Task> action, bool affectsUnread = true) => InboxIdle.OwnAsync(Account.Id, async () =>
    {
        foreach (var group in messages.GroupBy(m => m.FolderId))
        {
            var uids = group.Select(Uid).Distinct().ToList();
            await RunAsync(async client => await action(client, await OpenAsync(client, group.Key), uids, group.Key));
        }
    }, affectsUnread);

    public Task SetReadAsync(IReadOnlyList<MessageSummary> messages, bool read) => EachFolderAsync(messages, async (_, f, uids, _) =>
    {
        if (read) await f.AddFlagsAsync(uids, MessageFlags.Seen, true);
        else await f.RemoveFlagsAsync(uids, MessageFlags.Seen, true);
    });

    public Task SetFlagAsync(IReadOnlyList<MessageSummary> messages, bool flagged) => EachFolderAsync(messages, async (_, f, uids, _) =>
    {
        if (flagged) await f.AddFlagsAsync(uids, MessageFlags.Flagged, true);
        else await f.RemoveFlagsAsync(uids, MessageFlags.Flagged, true);
    }, affectsUnread: false);

    /// <summary>
    /// Con MOVE (RFC 6851) un comando solo; senza, COPY, poi \Deleted e UID EXPUNGE dei soli messaggi copiati (UIDPLUS).
    /// Senza UIDPLUS restano segnati \Deleted, e l'elenco non li mostra: un EXPUNGE semplice eliminerebbe anche i messaggi
    /// che altri programmi hanno segnato così senza eliminarli.
    /// </summary>
    static async Task<UniqueIdMap> MoveUidsAsync(ImapClient client, IMailFolder from, IList<UniqueId> uids, IMailFolder to)
    {
        if (client.Capabilities.HasFlag(ImapCapabilities.Move)) return await from.MoveToAsync(uids, to);
        // Ripresa dopo una connessione caduta: la COPY forse era già fatta, e rifarla lascerebbe doppioni nella cartella di
        // arrivo. Quelli già copiati non hanno il nuovo UID: Annulla li ritrova con il Message-Id, come senza UIDPLUS.
        var copy = retrying.Value ? await NotCopiedAsync(from, uids, to) : uids;
        var map = copy.Count > 0 ? await from.CopyToAsync(copy, to) : UniqueIdMap.Empty;
        await from.AddFlagsAsync(uids, MessageFlags.Deleted, true);
        if (client.Capabilities.HasFlag(ImapCapabilities.UidPlus)) await from.ExpungeAsync(uids);
        return map;
    }

    /// <summary>
    /// Alla ripresa di una COPY, i messaggi ancora da copiare: uno già segnato \Deleted è stato copiato (il segno viene dopo
    /// la COPY); degli altri si cerca il Message-Id nella cartella di arrivo. Poi la cartella di partenza si riapre com'era.
    /// </summary>
    static async Task<IList<UniqueId>> NotCopiedAsync(IMailFolder from, IList<UniqueId> uids, IMailFolder to)
    {
        var access = from.Access;
        var items = await from.FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.Flags | MessageSummaryItems.Envelope);
        var unsure = items.Where(i => i.Flags?.HasFlag(MessageFlags.Deleted) != true).ToList();
        if (unsure.Count == 0) return [];
        var pending = new List<UniqueId>();
        await to.OpenAsync(FolderAccess.ReadOnly);
        foreach (var i in unsure)
            if (i.Envelope?.MessageId is not { Length: > 0 } id || await FindAsync(to, id) == null) pending.Add(i.UniqueId);
        await from.OpenAsync(access == FolderAccess.None ? FolderAccess.ReadWrite : access);
        Log.Write($"ripresa della copia in {Log.Private(to.FullName)}: {uids.Count - pending.Count} messaggi già copiati, {pending.Count} da copiare");
        return pending;
    }

    /// <summary>Eliminazione definitiva: \Deleted e UID EXPUNGE; senza UIDPLUS un EXPUNGE (Cestino e Svuota, dove tutto va via).</summary>
    static async Task DeleteNowAsync(ImapClient client, IMailFolder f, IList<UniqueId> uids)
    {
        await f.AddFlagsAsync(uids, MessageFlags.Deleted, true);
        if (client.Capabilities.HasFlag(ImapCapabilities.UidPlus)) await f.ExpungeAsync(uids);
        else await f.ExpungeAsync();
    }

    public async Task<List<MovedMessage>> MoveAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination)
    {
        var moved = new List<MovedMessage>();
        await EachFolderAsync(messages, async (client, f, uids, folderId) =>
        {
            if (folderId == destination.Id) return;
            UniqueIdMap map;
            try { map = await MoveUidsAsync(client, f, uids, await client.GetFolderAsync(destination.Id)); }
            catch (ImapCommandException ex)
            {
                // Con MOVE il rifiuto vale per il comando intero; senza, può essere arrivato dopo la copia (MailException.Repeatable).
                throw new MailException($"Il server non ha spostato {(uids.Count == 1 ? "il messaggio" : $"{uids.Count} messaggi")} in \"{destination.Name}\": {ex.ResponseText}", ex)
                    { Repeatable = client.Capabilities.HasFlag(ImapCapabilities.Move) };
            }
            // Senza UIDPLUS il server non dice i nuovi UID: Annulla ritrova i messaggi con il Message-Id.
            foreach (var m in messages.Where(m => m.FolderId == folderId).DistinctBy(m => m.Id))
                moved.Add(new MovedMessage(m, map.TryGetValue(Uid(m), out var to) ? to.Id.ToString() : ""));
        });
        return moved;
    }

    public Task<List<MovedMessage>> UndoMoveAsync(IReadOnlyList<MovedMessage> moved, MailFolder destination) =>
        InboxIdle.OwnAsync(Account.Id, () => UndoMoveNowAsync(moved, destination));

    async Task<List<MovedMessage>> UndoMoveNowAsync(IReadOnlyList<MovedMessage> moved, MailFolder destination)
    {
        var back = new List<MovedMessage>();
        foreach (var group in moved.GroupBy(x => x.Original.FolderId))
        {
            await RunAsync(async client =>
            {
                var there = await OpenAsync(client, destination.Id);
                var origin = await client.GetFolderAsync(group.Key);
                var uids = new List<(MovedMessage Moved, UniqueId Uid)>();
                foreach (var x in group)
                {
                    if (uint.TryParse(x.NewId, out var id)) uids.Add((x, new UniqueId(id)));
                    else if (await FindAsync(there, x.Original.StableId) is { } found) uids.Add((x, found));
                }
                if (uids.Count == 0) return;
                var map = await MoveUidsAsync(client, there, uids.Select(u => u.Uid).ToList(), origin);
                // Senza COPYUID il nuovo UID si ritrova nella cartella di prima, con il Message-Id.
                IMailFolder? reopened = null;
                foreach (var (x, uid) in uids)
                {
                    if (map.TryGetValue(uid, out var to)) back.Add(x with { NewId = to.Id.ToString() });
                    else if (await FindAsync(reopened ??= await OpenAsync(client, group.Key), x.Original.StableId) is { } found) back.Add(x with { NewId = found.Id.ToString() });
                }
            });
        }
        return back;
    }

    public Task CopyAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination) => EachFolderAsync(messages, async (client, f, uids, _) =>
    {
        var to = await client.GetFolderAsync(destination.Id);
        var copy = retrying.Value ? await NotCopiedAsync(f, uids, to) : uids; // ripresa: niente doppioni
        if (copy.Count > 0) await f.CopyToAsync(copy, to);
    });

    /// <summary>
    /// Coda delle azioni senza rete: i messaggi ancora nella loro cartella, sulla connessione in sottofondo con EXAMINE. Con lo
    /// stesso Message-Id (una cartella rifatta riparte dagli stessi UID) e senza \Deleted (senza UIDPLUS lo spostamento lascia
    /// così l'originale, e lo spostamento già fatto non si rifà).
    /// </summary>
    public async Task<List<MessageSummary>?> StillThereAsync(IReadOnlyList<MessageSummary> messages)
    {
        var there = new List<MessageSummary>();
        foreach (var group in messages.GroupBy(m => m.FolderId))
        {
            var uids = group.Select(Uid).Distinct().ToList();
            var found = await RunAsync(async client =>
                await (await ExamineAsync(client, group.Key)).FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags), background);
            foreach (var m in group)
                if (found.FirstOrDefault(s => s.UniqueId == Uid(m)) is { } s && s.Flags?.HasFlag(MessageFlags.Deleted) != true
                    && (m.StableId.Length == 0 || s.Envelope?.MessageId == m.StableId))
                    there.Add(m);
        }
        return there;
    }

    public Task DeletePermanentlyAsync(IReadOnlyList<MessageSummary> messages) => EachFolderAsync(messages, (client, f, uids, _) => DeleteNowAsync(client, f, uids));

    /// <summary>Posponi: la chiave è il Message-Id, che resta lo stesso nella cartella dei posposti.</summary>
    public Task<string> SnoozeAsync(MessageSummary message, MailFolder snoozeFolder) => InboxIdle.OwnAsync(Account.Id, () => RunAsync(async client =>
    {
        var f = await OpenAsync(client, message.FolderId);
        if (message.StableId.Length == 0) throw new MailException("Il messaggio non ha un Message-Id: non si può posporre.");
        await MoveUidsAsync(client, f, [Uid(message)], await client.GetFolderAsync(snoozeFolder.Id));
        return message.StableId;
    }));

    public Task<string?> WakeAsync(string key, MailFolder snoozeFolder, MailFolder inbox) => InboxIdle.OwnAsync(Account.Id, () => RunAsync(async client =>
    {
        var f = await OpenAsync(client, snoozeFolder.Id);
        if (await FindAsync(f, key) is not { } uid) return null;
        await f.RemoveFlagsAsync(uid, MessageFlags.Seen, true);
        await MoveUidsAsync(client, f, [uid], client.Inbox);
        return (string?)key;
    }));

    static async Task<UniqueId?> FindAsync(IMailFolder f, string messageId) =>
        messageId.Length == 0 ? null
        : (await f.SearchAsync(SearchQuery.HeaderContains("Message-Id", messageId).And(SearchQuery.NotDeleted))).Cast<UniqueId?>().LastOrDefault();

    // ---------------------------------------------------------------- invio e bozze

    /// <summary>
    /// SMTP con TLS e le credenziali dell'account; poi, come Thunderbird, una copia in Posta inviata (APPEND con \Seen), a
    /// meno che il server non la salvi da sé (ServerSavesSent). La bozza che il messaggio continuava esce dalle Bozze.
    /// </summary>
    public async Task SendAsync(OutgoingMessage mail)
    {
        ThrowIfClosed();
        await DeliverAsync(await BuildAsync(mail), mail.DeliveryReceipt);
        if (mail.Draft is { } draft)
        {
            try
            {
                await RunAsync(async client =>
                {
                    if (await SpecialAsync(client, FolderKind.Drafts) is { } folder) await DeleteDraftAsync(client, await OpenAsync(client, folder), draft.Message.StableId);
                }, drafts);
            }
            catch (Exception ex)
            {
                Log.Error("bozza del messaggio inviato", ex);
            }
        }
    }

    /// <summary>
    /// Invio pianificato fatto da Missiva: la bozza con quel Message-Id parte com'è nelle Bozze (con la data di adesso), con la
    /// copia in Posta inviata, poi esce dalle Bozze. false se non c'è più. Partito il messaggio, restituisce true anche se la
    /// bozza non esce: un errore dopo l'SMTP lo farebbe ripartire al minuto dopo.
    /// </summary>
    public async Task<bool> SendDraftAsync(string draftKey)
    {
        var message = await RunAsync(async client =>
        {
            if (await SpecialAsync(client, FolderKind.Drafts) is not { } folder) return null;
            var f = await OpenAsync(client, folder);
            return await FindAsync(f, draftKey) is { } uid ? await f.GetMessageAsync(uid) : null;
        }, drafts);
        if (message == null) return false;
        message.Date = DateTimeOffset.Now;
        await DeliverAsync(message, deliveryReceipt: false);
        try
        {
            await RunAsync(async client =>
            {
                if (await SpecialAsync(client, FolderKind.Drafts) is { } folder) await DeleteDraftAsync(client, await OpenAsync(client, folder), draftKey);
            }, drafts);
        }
        catch (Exception ex)
        {
            Log.Error("bozza dell'invio pianificato", ex); // il messaggio è partito: la bozza resta nelle Bozze, niente errore
        }
        return true;
    }

    /// <summary>Via SMTP, poi la copia in Posta inviata se il server non la fa da sé.</summary>
    async Task DeliverAsync(MimeMessage message, bool deliveryReceipt)
    {
        ThrowIfClosed(); // l'SMTP ha un collegamento suo: senza questo il messaggio partirebbe, senza copia in Posta inviata
        var server = Server;
        message.MessageId ??= MimeKit.Utils.MimeUtils.GenerateMessageId(); // ritrova la copia in Posta inviata alla ripresa
        using (var smtp = await SmtpAsync(server, deliveryReceipt))
        {
            try
            {
                await MimeOptions.SendAsync(smtp, message, server.SmtpHost);
            }
            catch (Exception ex) when (Explain(ex, server.SmtpHost, server.SmtpPort, "SMTP") is { } explained)
            {
                throw explained;
            }
            // Partito: un errore nel saluto finale non deve farlo sembrare non partito (l'invio pianificato lo rifarebbe).
            try { await smtp.DisconnectAsync(true); } catch (Exception ex) { Log.Write($"SMTP di {server.SmtpHost} chiuso male dopo l'invio: {ex.Message}"); }
        }
        if (!server.ServerSavesSent)
        {
            try
            {
                await RunAsync(async client =>
                {
                    if (await SpecialAsync(client, FolderKind.Sent) is not { } sent)
                    {
                        Log.Write($"nessuna cartella Posta inviata in {Account.Email}: il messaggio partito non ha una copia");
                        return;
                    }
                    // Ripresa dopo una connessione caduta: forse la copia c'è già, e due APPEND ne lascerebbero due.
                    if (retrying.Value && await FindAsync(await ExamineAsync(client, sent), message.MessageId) != null) return;
                    await (await client.GetFolderAsync(sent)).AppendAsync(new AppendRequest(message, MessageFlags.Seen));
                }, drafts);
            }
            catch (Exception ex)
            {
                Log.Error("copia in Posta inviata", ex); // il messaggio è partito: niente errore
            }
        }
    }

    /// <summary>
    /// Come in GmailProvider: gli allegati piccoli della bozza appena scritta restano in memoria (Attachment.Content), così
    /// il salvataggio dopo non li riscarica dalla bozza di prima. Prima ogni salvataggio automatico li riscaricava tutti
    /// (struttura e parte, uno per uno) e li ricaricava (audit, v2 V-06).
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
            var folder = await SpecialAsync(client, FolderKind.Drafts) ?? throw new MailException("Non trovo la cartella Bozze sul server.");
            var f = await OpenAsync(client, folder);
            // Ripresa dopo una connessione caduta: la bozza forse è già scritta, e due APPEND ne lascerebbero due.
            UniqueId? uid = retried ? await FindAsync(f, messageId) : null;
            retried = true;
            uid ??= await f.AppendAsync(new AppendRequest(message, MessageFlags.Draft | MessageFlags.Seen))
                    ?? await FindAsync(f, messageId)
                    ?? throw new MailException("Il server non ha restituito la bozza salvata.");
            var s = (await f.FetchAsync(new[] { uid.Value }, ListItems)).FirstOrDefault() ?? throw new MailException("Il server non ha restituito la bozza salvata.");
            if (mail.Draft is { } old) await DeleteDraftAsync(client, f, old.Message.StableId, keep: uid);
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

    /// <summary>Allegati delle bozze tenuti in memoria fra un salvataggio e l'altro: quelli grandi si riscaricano (come in Gmail).</summary>
    const long KeepAttachment = MailLimits.KeepAttachment;

    public Task DiscardDraftAsync(SavedDraft draft) => RunAsync(async client =>
    {
        var folder = await SpecialAsync(client, FolderKind.Drafts) ?? throw new MailException("Non trovo la cartella Bozze sul server.");
        await DeleteDraftAsync(client, await OpenAsync(client, folder), draft.Message.StableId);
    }, drafts);

    /// <summary>La bozza con quel Message-Id esce dalle Bozze (si cerca solo lì); con UIDPLUS solo lei, altrimenti resta segnata \Deleted.</summary>
    static async Task DeleteDraftAsync(ImapClient client, IMailFolder drafts, string messageId, UniqueId? keep = null)
    {
        if (messageId.Length == 0) return;
        var uids = (await drafts.SearchAsync(SearchQuery.HeaderContains("Message-Id", messageId))).Where(u => u != keep).ToList();
        if (uids.Count == 0) return;
        await drafts.AddFlagsAsync(uids, MessageFlags.Deleted, true);
        if (client.Capabilities.HasFlag(ImapCapabilities.UidPlus)) await drafts.ExpungeAsync(uids);
    }

    readonly ReplySources replySources = new();

    /// <summary>
    /// Il messaggio da inviare o da salvare come bozza (MimeOptions.BuildAsync, lo stesso di Gmail). Di questo provider:
    /// il corpo dell'originale dai corpi già letti, Message-Id e References sulla connessione principale, gli allegati
    /// sulla connessione delle bozze.
    /// </summary>
    Task<MimeMessage> BuildAsync(OutgoingMessage mail) => MimeOptions.BuildAsync(mail, Account, replySources,
        original => GetBodyAsync(original),
        original => RunAsync(async client => (await (await OpenAsync(client, original.FolderId)).FetchAsync(new[] { Uid(original) },
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.References)).FirstOrDefault()),
        (message, attachment) => AttachmentAsync(message, attachment, drafts));


    /// <summary>La connessione dell'IDLE, ferma sulla Posta in arrivo in sola lettura (InboxIdle): non la usa nient'altro.</summary>
    readonly Connection idle = new("dell'IDLE");

    /// <summary>
    /// Annullato da ResetConnectionsAsync (server o password cambiati in Gestisci account): la sessione dell'IDLE in corso,
    /// che tiene la sua connessione fino a 9 minuti, si chiude subito e la prossima si collega al server nuovo. Prima l'IDLE
    /// restava sul server di prima e annunciava la casella vecchia.
    /// </summary>
    CancellationTokenSource idleReset = new();

    /// <summary>Nuovi messaggi subito: IDLE sulla Posta in arrivo, rinnovato e ripreso da InboxIdle, sulla sua connessione.</summary>
    public Task WatchInboxAsync(Action<InboxNotice> changed, CancellationToken cancel) =>
        InboxIdle.RunAsync(Account.Email, async token =>
        {
            var reset = idleReset;
            using var session = CancellationTokenSource.CreateLinkedTokenSource(token, reset.Token);
            return await RunAsync(async client =>
            {
                try
                {
                    return await InboxIdle.SessionAsync(client, changed, session.Token);
                }
                catch (OperationCanceledException) when (reset.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    // La connessione si chiude qui (quella interrotta non si riusa): la sessione dopo si collega con i dati nuovi.
                    try { await client.DisconnectAsync(false); } catch { }
                    Log.Write($"IDLE su INBOX di {Account.Email}: server o password cambiati, mi ricollego");
                    return true;
                }
            }, idle, session.Token);
        }, cancel);

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
        drafts.Dispose();
        idle.Dispose();
    }
}

/// <summary>
/// Struttura, destinatari e dimensione dei messaggi elencati (anche GmailProvider), al più Limit: oltre esce la voce
/// elencata o letta da più tempo. Prima alla voce 1001 si svuotava tutto, anche le voci dell'elenco aperto: da lì i
/// messaggi già copiati sul PC si aprivano dal server, con una richiesta in più, e i vicini non si scaricavano in anticipo
/// (audit, v1 V-01 e v2 V-18). Elenchi, anticipi e copia dei 30 giorni scrivono da thread diversi: un lucchetto.
/// </summary>
sealed class SummaryCache(int limit = SummaryCache.Limit)
{
    public const int Limit = 2000;
    readonly Dictionary<string, LinkedListNode<(string Key, IMessageSummary Value)>> byKey = new();
    readonly LinkedList<(string Key, IMessageSummary Value)> order = new();

    public int Count { get { lock (order) return order.Count; } }

    public IMessageSummary this[string key]
    {
        set
        {
            lock (order)
            {
                if (byKey.Remove(key, out var old)) order.Remove(old);
                byKey[key] = order.AddLast((key, value));
                while (order.Count > limit)
                {
                    byKey.Remove(order.First!.Value.Key);
                    order.RemoveFirst();
                }
            }
        }
    }

    public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out IMessageSummary value)
    {
        lock (order)
        {
            if (!byKey.TryGetValue(key, out var node))
            {
                value = null;
                return false;
            }
            order.Remove(node);
            order.AddLast(node);
            value = node.Value.Value;
            return true;
        }
    }
}
