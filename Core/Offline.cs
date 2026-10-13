using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Missiva;

/// <summary>
/// Senza rete, come il nuovo Outlook: un solo stato "Non in linea", detto in italiano, invece del testo di Windows ("Tentativo
/// di accesso al socket con modalità non consentite..."), e per ogni account se l'ultima richiesta è fallita per la rete
/// (allora i messaggi si aprono subito dalla copia su disco, BodyCache, senza aspettare il collegamento).
/// </summary>
public static class Network
{
    /// <summary>Errori di Windows che vogliono dire "non in linea": firewall, rete assente, nome del server non risolto, nessuna risposta.</summary>
    static readonly HashSet<int> OfflineCodes = [10013, 10050, 10051, 10060, 10064, 10065, 11001, 11002, 11004];

    static SocketException? SocketError(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            // Un invio forse già partito non è "non in linea": non va nella Posta in uscita, che lo ripeterebbe da sola.
            if (e is MailException { MaybeSent: true }) return null;
            if (e is SocketException s) return s;
            if (e is AggregateException { InnerExceptions: [var only] }) return SocketError(only);
        }
        return null;
    }

    /// <summary>La richiesta non è arrivata al server per colpa della rete (o di un firewall), non per un errore del server.</summary>
    public static bool IsOffline(Exception? ex) => SocketError(ex) is { } s && OfflineCodes.Contains(s.ErrorCode);

    /// <summary>"Non in linea" con il perché, o null se l'errore non è della rete. 10013: un firewall come Portmaster blocca Missiva.</summary>
    public static string? Explain(Exception? ex) => SocketError(ex) switch
    {
        { ErrorCode: 10013 } => "Non in linea: un firewall (per esempio Portmaster) blocca Missiva. Consenti Missiva.exe nel firewall.",
        { } s when OfflineCodes.Contains(s.ErrorCode) => "Non in linea: controlla la connessione a Internet.",
        _ => null,
    };

    /// <summary>Gli account la cui ultima richiesta è fallita per la rete, da quando.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> offline = new();

    /// <summary>Annota l'esito di una richiesta dell'account: un errore di rete lo segna non in linea, un successo lo toglie.</summary>
    public static void Note(MailAccount account, Exception? error)
    {
        if (error == null) offline.TryRemove(account.Id, out _);
        else if (IsOffline(error)) offline[account.Id] = DateTime.UtcNow;
    }

    /// <summary>L'account era non in linea da meno di un minuto: i messaggi si aprono dalla copia su disco, senza provare il server.</summary>
    public static bool RecentlyOffline(MailAccount account) => offline.TryGetValue(account.Id, out var since) && DateTime.UtcNow - since < TimeSpan.FromMinutes(1);

    /// <summary>
    /// L'ultima richiesta dell'account è fallita per la rete e nessuna è riuscita dopo (il controllo di ogni due minuti, o
    /// subito quando Windows dice che la rete è cambiata, lo rimette in linea): le azioni vanno in coda (ActionQueue) e i
    /// messaggi nella Posta in uscita (Outbox) senza aspettare un collegamento che non arriva. Con --senza-rete sempre.
    /// </summary>
    public static bool KnownOffline(MailAccount account)
    {
#if DEBUG || PROVA
        if (TestOffline && OfflineCodes.Contains(TestOfflineCode)) return true;
#endif
        return offline.ContainsKey(account.Id);
    }

#if DEBUG || PROVA
    /// <summary>Prove (--senza-rete, MISSIVA_SENZA_RETE=1): i provider falliscono come con un firewall che blocca Missiva, prima di collegarsi.</summary>
    public static bool TestOffline;
    /// <summary>
    /// Prove (--errore-rete 10054): l'errore di Windows con cui falliscono i collegamenti. 10013 è il firewall; 10054
    /// (connessione chiusa dall'altra parte) e 10061 (rifiutata) non sono "non in linea": l'account dice "Non raggiungibile".
    /// </summary>
    public static int TestOfflineCode = 10013;
#endif

    /// <summary>Con --senza-rete o MISSIVA_SENZA_RETE=1 (solo nelle versioni di prova) un errore 10013 prima di ogni collegamento; altrimenti niente.</summary>
    [System.Diagnostics.Conditional("DEBUG"), System.Diagnostics.Conditional("PROVA")]
    public static void Check()
    {
#if DEBUG || PROVA
        if (TestOffline) throw new SocketException(TestOfflineCode);
#endif
    }

    /// <summary>
    /// Il gestore HTTP di un servizio (Graph, le API di Google; audit, v2 V-23), uno solo per tutti i suoi client: risposte
    /// compresse (di serie HttpClient non le chiede), connessioni ferme tenute cinque minuti invece di uno e, con le
    /// richieste di ApiRequest, HTTP/2: quelle fatte insieme passano dalla stessa connessione invece di aprirne una ciascuna.
    /// Misurato il 4 ottobre 2026: Graph comprime con gzip (la prima pagina di un elenco da 64 a 8 KB, un corpo da 20 a 9)
    /// ma risponde ancora in HTTP/1.1; le API di Google rispondono in HTTP/2.
    /// </summary>
    internal static SocketsHttpHandler ApiHandler() => new()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        EnableMultipleHttp2Connections = true,
    };

    /// <summary>Una richiesta in HTTP/2, con ripiego su HTTP/1.1 se il server non lo parla (di serie è sempre 1.1).</summary>
    internal static HttpRequestMessage ApiRequest(HttpMethod method, string url) =>
        new(method, url) { Version = System.Net.HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
}

/// <summary>Un corpo salvato su disco (BodyCache): quello che serve a mostrarlo senza rete.</summary>
public sealed class SavedBody
{
    public string Html { get; set; } = "";
    public List<Address> To { get; set; } = new();
    public List<Address> Cc { get; set; } = new();
    public List<Address> Bcc { get; set; } = new();
    public List<Attachment> Attachments { get; set; } = new();
    public List<string> AuthenticationResults { get; set; } = new();
    /// <summary>Per "Annulla iscrizione" (Unsubscribe); le copie fatte prima dell'audit, blocco 6, non le hanno.</summary>
    public string? ListUnsubscribe { get; set; }
    public string? ListUnsubscribePost { get; set; }
    /// <summary>
    /// Le due intestazioni qui sopra sono state lette dal messaggio: null allora vuol dire che non le ha. Le copie fatte prima
    /// (fino all'audit, blocco 9) non hanno questo campo: se non hanno nemmeno List-Unsubscribe, non si sa, e il riquadro le
    /// chiede al server alla prima apertura (BodyCache.SetListHeaders le scrive qui).
    /// </summary>
    public bool ListHeadersRead { get; set; }
    public string? ImagesFrom { get; set; }
    public DateTime SavedAt { get; set; }
    /// <summary>
    /// La riga dell'elenco del messaggio (mittente, oggetto, data, anteprima), per la ricerca senza rete (BodyCache.Search).
    /// Dal giro P5: le copie di prima non l'hanno, e la ricerca le salta.
    /// </summary>
    public MessageSummary? Summary { get; set; }
}

[JsonSerializable(typeof(SavedBody))]
sealed partial class SavedBodyJson : JsonSerializerContext;

/// <summary>
/// La copia su disco dei corpi dei messaggi aperti e dei primi della Posta in arrivo scaricati in anticipo, come la posta
/// offline del nuovo Outlook e di Thunderbird: senza rete si leggono lo stesso, con la riga "Non in linea: copia del ...". In
/// cache\account\corpi, una cartella per cartella della casella e un file per messaggio, cifrato con DPAPI come i token (solo
/// per questo utente di Windows): nessun oggetto o testo in chiaro. Al massimo 200 MB per account (i più vecchi escono) e 30
/// giorni; le immagini incorporate fino a 1 MB, niente allegati; mai dalla Posta indesiderata né dalle Bozze; tolta con i
/// messaggi eliminati, spostati o svuotati (Forget, ForgetFolder) e con l'account (MailCache.Delete).
/// </summary>
public static class BodyCache
{
    /// <summary>
    /// Quanto può occupare la copia di un account e per quanti giorni un corpo resta. OfflineCopy, che la riempie, si
    /// regola su questi due (OfflineCopy.Full e OfflineCopy.Days): prima erano numeri scritti a mano in due posti.
    /// </summary>
    internal const long DefaultLimit = 200L * 1024 * 1024;
    internal const int MaxDays = 30;
    static long AccountLimit = DefaultLimit;
    const int InlineMax = 1_000_000;
    static readonly TimeSpan MaxAge = TimeSpan.FromDays(MaxDays);

#if DEBUG
    /// <summary>Prove (--prova-bozze): un limite piccolo per vedere uscire i corpi più vecchi, e i salvataggi aspettati.</summary>
    internal static long TestLimit { get => AccountLimit; set => AccountLimit = value; }
    internal static readonly List<Task> TestWrites = new();
    /// <summary>Prove: la copia in una cartella temporanea invece che in quella dei dati.</summary>
    internal static string? TestRoot;
    static string Root => TestRoot ?? Store.DataRoot;
#else
    static string Root => Store.DataRoot;
#endif

    static string Folder(MailAccount account) => Path.Combine(Root, "cache", Store.CacheName(account.Id), "corpi");

    static string Hash(string text, int length) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..length].ToLowerInvariant();

    /// <summary>Le copie di una cartella della casella stanno insieme: Svuota le toglie tutte.</summary>
    static string FolderOf(MailAccount account, string folderId) => Path.Combine(Folder(account), Hash(folderId, 16));

    /// <summary>
    /// Il file di un messaggio: cartella, identificativo e identificativo stabile (per IMAP il Message-Id, per Gmail
    /// l'X-GM-MSGID). In Gmail e IMAP l'identificativo è l'UID: se la casella o la cartella rinasce gli UID ripartono da capo
    /// (UIDVALIDITY), e il messaggio 57 nuovo non deve aprirsi con il corpo del 57 vecchio.
    /// </summary>
    static string FileOf(MailAccount account, MessageSummary message) =>
        Path.Combine(FolderOf(account, message.FolderId), Hash(message.Id + "|" + message.StableId, 24) + ".bin");

    /// <summary>
    /// Scritture e cancellazioni di cartelle una alla volta; Delete (account rimosso) fa crescere la generazione dell'account,
    /// e una scrittura partita prima non ricrea la cartella. Forget (un file alla volta, dal thread dell'interfaccia) non lo
    /// prende, e la pulizia (Trim) ha il suo: prima Forget aspettava qui le scritture in sottofondo, ognuna con una pulizia
    /// intera (audit, v2 V-20: con 625 file e dieci corpi in scrittura, 26-39 ms di attesa in mediana e fino a 50).
    /// </summary>
    static readonly object Gate = new();
    static readonly Dictionary<string, int> generations = new();

    /// <summary>Una pulizia alla volta, fuori da Gate.</summary>
    static readonly object Trimming = new();

    /// <summary>
    /// I byte della copia di ogni account contati dall'ultima pulizia, e quando: Save li aggiorna e rifà la pulizia (che
    /// elenca tutti i file) solo la prima volta, oltre il limite o dopo TrimEvery, non a ogni corpo salvato.
    /// </summary>
    static readonly Dictionary<string, (long Bytes, DateTime Checked)> sizes = new();
    static readonly TimeSpan TrimEvery = TimeSpan.FromHours(1);

    /// <summary>Dopo una scrittura o una cancellazione: i byte in più o in meno. Vero se è ora di una pulizia.</summary>
    static bool Count(MailAccount account, long change)
    {
        lock (sizes)
        {
            if (!sizes.TryGetValue(account.Id, out var size))
            {
                // Mai contata in questa sessione: la conta la pulizia, una sola anche con più scritture insieme.
                if (change < 0) return false;
                sizes[account.Id] = (change, DateTime.UtcNow);
                return true;
            }
            sizes[account.Id] = size = (Math.Max(0, size.Bytes + change), size.Checked);
            return size.Bytes > AccountLimit || DateTime.UtcNow - size.Checked > TrimEvery;
        }
    }

#if DEBUG
    /// <summary>Prove: quante pulizie intere sono state fatte, e i conti dimenticati come dopo un riavvio (la prossima scrittura pulisce).</summary>
    internal static int TestTrims;
    internal static void TestForgetSizes() { lock (sizes) sizes.Clear(); }
    /// <summary>Prove: quanti file ha letto Search.</summary>
    internal static int TestSearchReads;
#endif

    /// <summary>
    /// Salva in sottofondo (cifrare e scrivere costa qualche millisecondo): chi apre il messaggio non aspetta. Un corpo non
    /// cambia: se c'è già non si riscrive (ai primi dieci della Posta in arrivo capita a ogni avvio). Le bozze no: cambiano
    /// con lo stesso identificativo (Graph), e Modifica riprenderebbe dalla copia una versione vecchia, che salvando
    /// sostituirebbe sul server quella nuova.
    /// </summary>
    public static bool Save(MailAccount account, MessageSummary message, MessageBody body)
    {
        if (message.IsDraft) return false;
        var path = FileOf(account, message);
        if (File.Exists(path)) return false;
        int generation;
        lock (Gate) generation = generations.GetValueOrDefault(account.Id);
        var saved = new SavedBody
        {
            Html = body.Html, To = body.To, Cc = body.Cc, Bcc = body.Bcc, AuthenticationResults = body.AuthenticationResults, ImagesFrom = body.ImagesFrom,
            ListUnsubscribe = body.ListUnsubscribe, ListUnsubscribePost = body.ListUnsubscribePost, ListHeadersRead = !body.ListHeadersUnread,
            SavedAt = DateTime.Now,
            Summary = new MessageSummary
            {
                Id = message.Id, FolderId = message.FolderId, StableId = message.StableId, From = message.From, To = message.To, Subject = message.Subject,
                Preview = message.Preview, Date = message.Date, IsRead = message.IsRead, IsFlagged = message.IsFlagged, HasAttachments = message.HasAttachments,
                AttachmentNames = message.AttachmentNames, ConversationId = message.ConversationId, FolderName = message.FolderName,
            },
            // Degli allegati solo i nomi (si vedono le schede) e le immagini incorporate piccole, che il testo mostra.
            Attachments = body.Attachments.Select(a => new Attachment
            {
                Id = a.Id, Name = a.Name, ContentType = a.ContentType, Size = a.Size, IsInline = a.IsInline, ContentId = a.ContentId,
                Content = a.IsInline && a.Content?.Length is > 0 and <= InlineMax ? a.Content : null,
            }).ToList(),
        };
        var writing = Task.Run(() =>
        {
            try
            {
                var bytes = SafeFile.Protect(JsonSerializer.SerializeToUtf8Bytes(saved, SavedBodyJson.Default.SavedBody));
                lock (Gate)
                {
                    if (generations.GetValueOrDefault(account.Id) != generation) return; // intanto l'account è stato rimosso
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    SafeFile.WriteCopy(path, bytes);
                }
                if (Count(account, bytes.Length)) Trim(account);
            }
            catch (Exception ex)
            {
                Log.Error("copia del messaggio su disco", ex);
            }
        });
#if DEBUG
        lock (TestWrites)
        {
            TestWrites.RemoveAll(t => t.IsCompleted);
            TestWrites.Add(writing);
        }
#endif
        return true;
    }

    /// <summary>
    /// Scrive nella copia le due intestazioni di "Annulla iscrizione" lette dopo, per le copie fatte prima che Missiva le
    /// leggesse (SavedBody.ListHeadersRead): alla prossima apertura il comando c'è subito e non si chiedono più. In
    /// sottofondo, come Save; una copia tolta nel frattempo (Forget) non si ricrea.
    /// </summary>
    public static Task SetListHeaders(MailAccount account, MessageSummary message, ListHeaders headers)
    {
        if (message.IsDraft) return Task.CompletedTask;
        var path = FileOf(account, message);
        int generation;
        lock (Gate) generation = generations.GetValueOrDefault(account.Id);
        var writing = Task.Run(() =>
        {
            try
            {
                if (!File.Exists(path)) return;
                var saved = JsonSerializer.Deserialize(SafeFile.Unprotect(File.ReadAllBytes(path)), SavedBodyJson.Default.SavedBody);
                if (saved == null) return;
                saved.ListUnsubscribe = headers.Unsubscribe;
                saved.ListUnsubscribePost = headers.Post;
                saved.ListHeadersRead = true;
                var bytes = SafeFile.Protect(JsonSerializer.SerializeToUtf8Bytes(saved, SavedBodyJson.Default.SavedBody));
                lock (Gate)
                {
                    if (generations.GetValueOrDefault(account.Id) != generation || !File.Exists(path)) return;
                    SafeFile.WriteCopy(path, bytes);
                }
            }
            catch (Exception ex)
            {
                Log.Error("copia del messaggio su disco", ex);
            }
        });
#if DEBUG
        lock (TestWrites)
        {
            TestWrites.RemoveAll(t => t.IsCompleted);
            TestWrites.Add(writing);
        }
#endif
        return writing;
    }

    /// <summary>Il corpo salvato, con OfflineCopy (quando è stato salvato), o null se non c'è (e per le bozze, mai salvate).</summary>
    public static MessageBody? Load(MailAccount account, MessageSummary message) => Load(account, message, online: false);

    /// <summary>
    /// Il corpo salvato come quello del server, anche in linea (Gmail e IMAP, IMailProvider.SavedBody): senza OfflineCopy,
    /// quindi senza la fascia "Non in linea". Null se la copia non basta: un'immagine incorporata senza contenuto (sopra 1
    /// MB) o un invito (text/calendar o .ics, che la copia non tiene). Gli allegati si scaricano quando si aprono, come per i
    /// corpi di cui arriva solo la parte da leggere.
    /// </summary>
    public static MessageBody? LoadOnline(MailAccount account, MessageSummary message) => Load(account, message, online: true);

    static MessageBody? Load(MailAccount account, MessageSummary message, bool online)
    {
        if (message.IsDraft) return null;
        try
        {
            var path = FileOf(account, message);
            if (!File.Exists(path)) return null;
            var saved = JsonSerializer.Deserialize(SafeFile.Unprotect(File.ReadAllBytes(path)), SavedBodyJson.Default.SavedBody);
            if (saved == null) return null;
            foreach (var a in saved.Attachments.Where(a => a.Content is { Length: 0 })) a.Content = null; // senza contenuto: si scarica quando serve
            if (online && saved.Attachments.Any(a => (a.IsInline && a.Content == null) || Invitation.IsCalendar(a.ContentType, a.Name))) return null;
            return new MessageBody
            {
                Html = saved.Html, To = saved.To, Cc = saved.Cc, Bcc = saved.Bcc, Attachments = saved.Attachments, AuthenticationResults = saved.AuthenticationResults,
                ListUnsubscribe = saved.ListUnsubscribe, ListUnsubscribePost = saved.ListUnsubscribePost,
                ListHeadersUnread = !saved.ListHeadersRead && saved.ListUnsubscribe == null,
                ImagesFrom = saved.ImagesFrom, OfflineCopy = online ? null : saved.SavedAt,
            };
        }
        catch (Exception ex)
        {
            Log.Error("copia del messaggio su disco", ex);
            return null;
        }
    }

    /// <summary>
    /// Tutte le copie dell'account, una alla volta su più thread (decifrate in memoria, come la ricerca): per rileggere le
    /// Authentication-Results dei messaggi già sul PC (SenderLogos.AuthenticateFromCopies). Da chiamare fuori dal thread
    /// dell'interfaccia; le copie rovinate si saltano.
    /// </summary>
    public static void Each(MailAccount account, Action<SavedBody> visit)
    {
        var dir = Folder(account);
        if (!Directory.Exists(dir)) return;
        Parallel.ForEach(Directory.EnumerateFiles(dir, "*.bin", SearchOption.AllDirectories), path =>
        {
            SavedBody? saved;
            try { saved = JsonSerializer.Deserialize(SafeFile.Unprotect(File.ReadAllBytes(path)), SavedBodyJson.Default.SavedBody); }
            catch (Exception) { return; }
            if (saved != null) visit(saved);
        });
    }

    /// <summary>C'è già la copia del messaggio (OfflineSync non lo riscarica).</summary>
    public static bool Has(MailAccount account, MessageSummary message) => !message.IsDraft && File.Exists(FileOf(account, message));

    /// <summary>
    /// Senza rete, come l'Outlook classico: la ricerca nella copia su disco dell'account (i messaggi aperti e la Posta in
    /// arrivo degli ultimi 30 giorni, OfflineSync), con le parole e i filtri della ricerca (SearchTerms: da, a, oggetto, date,
    /// allegati; letti e contrassegni come erano quando la copia è stata fatta), dal più recente, al massimo max. Si decifra
    /// un file alla volta, in memoria; il testo è l'HTML senza tag, stili e script. Da chiamare fuori dal thread
    /// dell'interfaccia. Le copie senza la riga dell'elenco (fatte prima del giro P5) si saltano.
    /// </summary>
    /// <param name="folderId">La cartella aperta ("Cartella corrente"); null per tutte.</param>
    /// <param name="cancel">
    /// Chi cerca non aspetta più (un'altra ricerca, la ricerca chiusa): ci si ferma fra un file e l'altro, con
    /// OperationCanceledException. Prima la scansione arrivava in fondo comunque, su tutti i processori, insieme a quella dopo
    /// (audit, v2 V-21).
    /// </param>
    public static List<MessageSummary> Search(MailAccount account, string query, string? folderId, int max, CancellationToken cancel = default)
    {
        var terms = SearchTerms.Parse(query);
        // Le parole libere; quelle con la sintassi dei provider (from:, participants:, OR) valgono per il loro valore.
        var words = terms.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("OR" or "AND"))
            .Select(w => (w.Contains(':') ? w[(w.IndexOf(':') + 1)..] : w).Trim('(', ')', '"'))
            .Where(w => w.Length > 0).ToList();
        var dir = folderId == null ? Folder(account) : FolderOf(account, folderId);
        if (!Directory.Exists(dir)) return new();
        static bool Inside(string text, string word) => text.Contains(word, StringComparison.CurrentCultureIgnoreCase);
        static string People(IEnumerable<Address> list) => string.Join(" ", list.Select(a => a.Name + " " + a.Email));
        // Un file alla volta per thread, su più thread: decifrare e leggere 400 corpi costava 2,6 s su un thread solo.
        var found = new System.Collections.Concurrent.ConcurrentBag<MessageSummary>();
        Parallel.ForEach(Directory.EnumerateFiles(dir, "*.bin", SearchOption.AllDirectories), new ParallelOptions { CancellationToken = cancel }, path =>
        {
#if DEBUG
            Interlocked.Increment(ref TestSearchReads);
#endif
            SavedBody? saved;
            try { saved = JsonSerializer.Deserialize(SafeFile.Unprotect(File.ReadAllBytes(path)), SavedBodyJson.Default.SavedBody); }
            catch (Exception) { return; } // una copia rovinata o di un altro utente di Windows: si salta
            if (saved?.Summary is not { } m) return;
            var from = m.From.Name + " " + m.From.Email;
            var to = People(saved.To.Concat(saved.Cc).Concat(m.To));
            if (terms.From.Length > 0 && !Inside(from, terms.From)) return;
            if (terms.To.Length > 0 && !Inside(to, terms.To)) return;
            if (terms.Subject.Length > 0 && !Inside(m.Subject, terms.Subject)) return;
            if (terms.HasAttachments && !m.HasAttachments && !saved.Attachments.Any(a => !a.IsInline)) return;
            if (!terms.Matches(m)) return; // date, non letti, contrassegnati
            // Prima la riga dell'elenco; il testo dell'email (più caro) solo se le parole non ci sono già tutte.
            var head = m.Subject + " " + from + " " + to + " " + string.Join(" ", m.AttachmentNames);
            if (!words.All(w => Inside(head, w)))
            {
                var text = head + " " + PlainText(saved.Html);
                if (!words.All(w => Inside(text, w))) return;
            }
            found.Add(m);
        });
        return found.OrderByDescending(m => m.Date).Take(max).ToList();
    }

    /// <summary>
    /// Il testo di un corpo salvato per la ricerca: senza stili, script e tag, con le entità decodificate. Senza backtracking
    /// (NonBacktracking, tempo lineare): è l'HTML dell'email com'è arrivato.
    /// </summary>
    static string PlainText(string html) =>
        System.Net.WebUtility.HtmlDecode(Tags.Replace(Blocks.Replace(html, " "), " "));

    static readonly System.Text.RegularExpressions.Regex Blocks = new(@"<(style|script)\b[^>]*>.*?</(style|script)\s*>",
        System.Text.RegularExpressions.RegexOptions.NonBacktracking | System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex Tags = new("<[^>]*>", System.Text.RegularExpressions.RegexOptions.NonBacktracking);

    /// <summary>
    /// La pulizia della copia di un account, una alla volta e fuori da Gate. Escono i file più vecchi di 30 giorni, quelli
    /// della versione di prima (una cartella sola, senza la cartella della casella nel nome) e, oltre il limite dell'account,
    /// quelli scritti meno di recente. Conta i byte rimasti (sizes). Se non arriva in fondo (una cartella tolta intanto da
    /// Svuota o con l'account) il conto si dimentica e la prossima scrittura rifà la pulizia.
    /// </summary>
    internal static void Trim(MailAccount account)
    {
        lock (Trimming)
        {
#if DEBUG
            Interlocked.Increment(ref TestTrims);
#endif
            long kept = 0;
            try
            {
                var root = new DirectoryInfo(Folder(account));
                if (root.Exists)
                {
                    foreach (var old in root.GetFiles("*.bin")) old.Delete();
                    var files = root.GetFiles("*.bin", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                    long total = 0;
                    foreach (var file in files)
                    {
                        total += file.Length;
                        if (total > AccountLimit || DateTime.UtcNow - file.LastWriteTimeUtc > MaxAge) file.Delete();
                        else kept += file.Length;
                    }
                    // Le cartelle rimaste vuote sotto Gate: una scrittura che ha appena creato la sua non la perde prima di scriverci.
                    lock (Gate)
                        foreach (var dir in root.GetDirectories().Where(d => !d.EnumerateFileSystemInfos().Any())) dir.Delete();
                }
            }
            catch (Exception ex)
            {
                lock (sizes) sizes.Remove(account.Id);
                if (ex is not DirectoryNotFoundException) Log.Error("pulizia della copia su disco", ex);
                return;
            }
            lock (sizes) sizes[account.Id] = (kept, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Le copie scadute di tutti gli account, una volta, qualche secondo dopo l'avvio: anche degli account in cui non si
    /// scrive nulla di nuovo.
    /// </summary>
    public static void TrimAll(IEnumerable<MailAccount> accounts)
    {
        // L'elenco degli account si copia qui, sul thread di chi chiama: letto 30 secondi dopo da un altro thread, un account
        // aggiunto o tolto in quel momento faceva saltare la pulizia senza dirlo (audit del 7 ottobre, A-20).
        var list = accounts.ToList();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            foreach (var account in list)
            {
                try
                {
                    Trim(account);
                }
                catch (Exception ex)
                {
                    Log.Error("pulizia della copia su disco", ex);
                }
            }
        });
    }

    /// <summary>
    /// I messaggi sono usciti dalla loro cartella (Elimina, Elimina definitivamente, Sposta, Archivia, Segnala come posta
    /// indesiderata, Posponi): la loro copia su disco si toglie. Anche spostati non tornerebbe utile: in Graph e Gmail
    /// l'identificativo cambia con la cartella.
    /// </summary>
    public static void Forget(MailAccount account, IEnumerable<MessageSummary> messages)
    {
        int removed = 0;
        long bytes = 0;
        try
        {
            // Senza Gate: un file alla volta, e chi chiama è il thread dell'interfaccia (non aspetta le scritture in sottofondo).
            foreach (var m in messages)
            {
                var file = new FileInfo(FileOf(account, m));
                if (!file.Exists) continue;
                long length = file.Length;
                file.Delete();
                bytes += length;
                removed++;
            }
        }
        catch (DirectoryNotFoundException)
        {
            // la cartella è uscita intanto (Svuota, account rimosso): non c'è più nulla da togliere
        }
        catch (Exception ex)
        {
            Log.Error("copia del messaggio su disco", ex);
        }
        if (removed > 0) Count(account, -bytes);
        if (removed > 0) Log.Write($"copia su disco di {removed} messaggi tolta (usciti dalla cartella)");
    }

    /// <summary>Svuota: le copie di tutti i messaggi della cartella.</summary>
    public static void ForgetFolder(MailAccount account, string folderId)
    {
        try
        {
            lock (Gate)
            {
                var dir = FolderOf(account, folderId);
                if (Directory.Exists(dir) && Store.IsInside(dir, Path.Combine(Root, "cache"))) Directory.Delete(dir, true);
            }
            lock (sizes) sizes.Remove(account.Id); // da ricontare: lo fa la prossima scrittura, con la pulizia
        }
        catch (Exception ex)
        {
            Log.Error("copia della cartella su disco", ex);
        }
    }

    /// <summary>
    /// L'account esce da Missiva (MailCache.Delete): la copia si toglie, e una scrittura in sottofondo partita prima non la
    /// ricrea dopo.
    /// </summary>
    public static void Delete(MailAccount account)
    {
        lock (Gate)
        {
            generations[account.Id] = generations.GetValueOrDefault(account.Id) + 1;
            // Una cartella intera: solo dentro la copia locale (come MailCache.Delete; audit dell'11 ottobre, T-14).
            var dir = Folder(account);
            if (!Store.IsInside(dir, Path.Combine(Root, "cache"))) Log.Write("copia su disco di un account rimosso non cancellata: il suo percorso non sta nella cartella della copia");
            else
                try { Directory.Delete(dir, true); }
                catch (DirectoryNotFoundException) { }
                catch (Exception ex) { Log.Error("copia su disco dell'account rimosso", ex); }
        }
        lock (sizes) sizes.Remove(account.Id);
    }

    /// <summary>Quanti messaggi e quanti byte ha la copia dell'account (per il registro della prova).</summary>
    public static (int Count, long Bytes) Size(MailAccount account)
    {
        var dir = new DirectoryInfo(Folder(account));
        if (!dir.Exists) return (0, 0);
        var files = dir.GetFiles("*.bin", SearchOption.AllDirectories);
        return (files.Length, files.Sum(f => f.Length));
    }
}

/// <summary>
/// La Posta in arrivo degli ultimi 30 giorni sul PC (lavoro senza rete, parte 6b), come l'Outlook classico che tiene sul
/// PC la posta degli ultimi giorni: i corpi che mancano alla copia su disco (BodyCache) si scaricano in sottofondo. Gmail e
/// IMAP sulla connessione in sottofondo, con EXAMINE e BODY.PEEK (nulla diventa letto), dei messaggi con allegati solo la
/// parte da leggere (PrefetchAsync, ImapProvider.Prefetchable), uno alla volta; Graph con le GET dei corpi, uno alla volta. Fino a 180 MB per account, sotto i 200 di
/// BodyCache: oltre, la copia toglierebbe i più vecchi per rimetterli al giro dopo. Chi chiama decide quando (MainWindow:
/// a finestra ferma) e lo ferma con il token, fra un messaggio e l'altro.
/// </summary>
public static class OfflineCopy
{
    /// <summary>La stessa finestra di BodyCache: un corpo più vecchio la copia lo toglierebbe appena scaricato.</summary>
    public const int Days = BodyCache.MaxDays;
    /// <summary>Piena a nove decimi del limite di BodyCache (180 MB sotto i 200): legata a quello, deve restargli sotto.</summary>
    static long Full = BodyCache.DefaultLimit * 9 / 10;

#if DEBUG
    /// <summary>Prove: una copia piena più piccola.</summary>
    internal static long TestFull { get => Full; set => Full = value; }
#endif

    /// <summary>Un giro per l'account: quanti corpi ha scaricato.</summary>
    public static async Task<int> RunAsync(IMailProvider provider, MailFolder inbox, CancellationToken cancel)
    {
        var mail = provider.Account;
        // L'elenco dei file e i 500 "c'è già?" (15-18 ms su questo PC) su un thread del pool: chi chiama è il thread dell'interfaccia.
        if (await Task.Run(() => BodyCache.Size(mail).Bytes) >= Full) return 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Prima solo quello che serve a sapere se il corpo c'è già (di solito ci sono tutti), poi le righe intere dei mancanti.
        var recent = await provider.GetRecentKeysAsync(inbox, DateTime.UtcNow.AddDays(-Days), 500);
        List<MessageSummary> Missing(IEnumerable<MessageSummary> list) => list.Where(m => !m.IsDraft && !BodyCache.Has(mail, m)).ToList();
        var missing = await Task.Run(() => Missing(recent));
        if (missing.Count > 0)
        {
            var full = await provider.GetRecentAsync(inbox, missing);
            missing = await Task.Run(() => Missing(full));
        }
        int saved = 0;
        try
        {
            // Gmail e IMAP a gruppi di cinque (PrefetchAsync li scarica uno alla volta), Graph uno alla volta.
            int step = provider.PrefetchesMany ? 5 : 1;
            for (int i = 0; i < missing.Count; i += step)
            {
                cancel.ThrowIfCancellationRequested();
                var batch = missing.Skip(i).Take(step).ToList();
                if (provider.PrefetchesMany)
                {
                    await provider.PrefetchAsync(batch, cancel);
                    foreach (var m in batch)
                        if (provider.KeptBody(m) is { } body)
                        {
                            SenderLogos.NoteBody(mail, m, body);
                            if (BodyCache.Save(mail, m, body)) saved++;
                        }
                }
                else
                {
                    var body = await provider.GetBodyAsync(batch[0], cancel);
                    SenderLogos.NoteBody(mail, batch[0], body);
                    if (BodyCache.Save(mail, batch[0], body)) saved++;
                }
                if (saved > 0 && saved % 25 == 0 && BodyCache.Size(mail).Bytes >= Full) break;
            }
        }
        finally
        {
            Log.Time($"copia degli ultimi {Days} giorni di {mail.Email}: {saved} corpi scaricati, {missing.Count - saved} da scaricare o troppo grandi, " +
                     $"{recent.Count - missing.Count} già sul PC", clock);
        }
        return saved;
    }
}
