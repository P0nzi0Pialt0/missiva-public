using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Missiva;

/// <summary>
/// Account Microsoft (Outlook.com, Hotmail, Microsoft 365) tramite l'interfaccia ufficiale Microsoft Graph. Anche le
/// caselle condivise di Microsoft 365 (MailAccount.SharedOf): stessa posta, da users/{indirizzo}/ invece che da me/, con il
/// token dell'account di lavoro che le apre.
/// </summary>
public sealed partial class GraphProvider(MailAccount account) : IMailProvider
{
    const string Base = "https://graph.microsoft.com/v1.0/";
    /// <summary>
    /// Dove sta la posta: me/ per la casella dell'account, users/{indirizzo}/ per una casella condivisa (Graph non ha un
    /// altro modo: la casella si nomina con l'indirizzo).
    /// </summary>
    readonly string me = account.SharedOf != null ? "users/" + Uri.EscapeDataString(account.Email.Trim()) + "/" : "me/";

    /// <summary>
    /// Un identificativo come pezzo di un percorso di Graph: l'unico punto da cui un identificativo entra in un percorso
    /// (revisione del 13 ottobre 2026, K-03). Arrivano da Graph, ma anche da posti che un altro può aver scritto: i
    /// posposti e i contrassegni di una copia di sicurezza importata, la coda delle azioni, gli argomenti di una notifica.
    /// Prima si mettevano nel percorso com'erano: con una barra, un punto di domanda, un cancelletto o ".." la richiesta,
    /// con il token dell'account, andava a un altro percorso di Graph ("../mailFolders/archive" al posto di un messaggio
    /// da eliminare eliminava una cartella). Quelli veri di Outlook sono lettere, cifre, "-", "_" e "=" in fondo, e passano
    /// com'erano, byte per byte. Uno vuoto, fatto di soli punti, con caratteri non ASCII o di controllo, o (dove non si
    /// codifica) con "/", "\", "?", "#" o "%" non è di Graph: nessuna richiesta parte, e vale come un elemento che non c'è
    /// (NotFoundException: Posponi lo dà per eliminato, la coda toglie l'azione). Codificarli tutti con
    /// Uri.EscapeDataString non basta e non è prudente: ".." non ha nulla da codificare, e "=" diventerebbe "%3D" in
    /// ogni richiesta, cosa mai provata su Graph vero per messaggi e cartelle.
    /// </summary>
    /// <param name="escape">
    /// Per contatti, eventi, calendari e regole, che già si codificavano (un identificativo di regola può avere "+" e "/"):
    /// restano codificati allo stesso modo.
    /// </param>
    internal static string Segment(string? id, bool escape = false)
    {
        bool valid = !string.IsNullOrEmpty(id) && id.AsSpan().Trim('.').Length > 0;
        for (int i = 0; valid && i < id!.Length; i++)
            valid = id[i] is > ' ' and < (char)0x7F && (escape || id[i] is not ('/' or '\\' or '?' or '#' or '%'));
        if (!valid)
        {
            // Solo la lunghezza: se un giorno Graph ne desse uno vero fatto così, dal registro si vede che è stato fermato qui.
            Log.Write($"Graph: un identificativo di {id?.Length ?? 0} caratteri non è di Graph (vuoto, di soli punti o con caratteri che cambierebbero il percorso): la richiesta non parte");
            throw new NotFoundException(T("L'identificativo non è di Microsoft Graph: la richiesta non è stata inviata."));
        }
        return escape ? Uri.EscapeDataString(id!) : id!;
    }

    /// <summary>Il percorso di un messaggio (me/messages/id) e quello di una cartella (me/mailFolders/id, anche con un alias come inbox).</summary>
    string MessageUrl(string id) => me + "messages/" + Segment(id);
    string FolderUrl(string id) => me + "mailFolders/" + Segment(id);
    /// <summary>
    /// Con i destinatari (A): in Posta inviata e Bozze la riga dell'elenco mostra loro, non il mittente. Con la scheda della
    /// Posta in arrivo (inferenceClassification, Evidenziata o Altra), anche per i messaggi nuovi del controllo: prima un
    /// messaggio di Altra entrava in cima a Evidenziata e spariva alla rilettura (audit di affidabilità del 25 ottobre 2026,
    /// elenco, E-09). Non per le caselle condivise, che Evidenziata non l'hanno (SupportsFocused): lì non si chiede nulla della
    /// scheda, come prima.
    /// </summary>
    const string BaseFields = "id,subject,from,toRecipients,replyTo,receivedDateTime,isRead,isDraft,flag,hasAttachments,bodyPreview,conversationId,parentFolderId,categories,importance,internetMessageId";
    string SummaryFields => SupportsFocused ? BaseFields + ",inferenceClassification" : BaseFields;
    const string AttachmentNamesExpand = "&$expand=attachments($select=name,isInline)";
    /// <summary>
    /// Si spegne da sola se Graph non accetta l'espansione, cioè risponde 400. Con gli altri errori (429 scorrendo veloce,
    /// 503) si riprova senza per quella volta: prima una sola 429 toglieva per tutta la sessione i nomi degli allegati
    /// dall'elenco e una richiesta in più a ogni corpo con allegati.
    /// </summary>
    static bool expandAttachments = true;
    const int PageSize = MailLimits.PageSize;
    /// <summary>Dopo un 429 o un 503 si aspetta quanto Graph chiede (Retry-After), ma mai più di così: per le richieste e per i pezzi dei caricamenti.</summary>
    static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Un gestore solo per i due client (Network.ApiHandler: risposte compresse, HTTP/2 se Graph lo parlerà, connessioni
    /// tenute cinque minuti): uno scaricamento trova la connessione già aperta dalle richieste, invece di un gruppo suo
    /// quasi sempre freddo.
    /// </summary>
    static SocketsHttpHandler net = Network.ApiHandler();
    static HttpClient Http = ApiClient(net);
    /// <summary>Per gli scaricamenti (DownloadAsync), che controllano da sé l'attesa invece della durata.</summary>
    static HttpClient Downloads = DownloadClient(net);
    /// <summary>
    /// Per i pezzi degli allegati grandi (UploadLargeAsync), che vanno a un altro server, all'indirizzo che Graph dà: senza
    /// seguire reindirizzamenti (un 307 o un 308 di quel server rimanderebbe il pezzo, con il contenuto del file, a un
    /// altro indirizzo) e senza cookie (audit dell'11 ottobre, T-23).
    /// </summary>
    internal static SocketsHttpHandler UploadHandler() => new() { AllowAutoRedirect = false, UseCookies = false, PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5) };
    static HttpClient Uploads = new(UploadHandler()) { Timeout = TimeSpan.FromSeconds(60) };

    static HttpClient ApiClient(HttpMessageHandler handler) => new(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
    static HttpClient DownloadClient(HttpMessageHandler handler) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Quante volte le connessioni sono state rifatte (ResetConnections): una richiesta partita prima e caduta si riconosce.</summary>
    static int generation;

    /// <summary>
    /// Dopo la ripresa del PC o un cambio di rete (App.WakeAsync): le connessioni con Graph rifatte da capo. Il gestore
    /// tiene le connessioni aperte per riusarle, e dopo la sospensione quella in HTTP/2 era morta senza che qui arrivasse
    /// nulla: ogni richiesta ci restava ferma fino al tempo scaduto di 60 s (registro dell'autore del 26 ottobre 2026, un
    /// minuto di "Non riesco ad aprire il messaggio" dopo la ripresa, poi 10054). Ora un gestore nuovo per le richieste
    /// dopo, e i client di prima chiusi, che fermano subito le richieste che aspettavano ancora: le letture si ripetono
    /// sulle connessioni nuove (SendAsync, DownloadAsync, i pezzi degli allegati), le altre falliscono come una
    /// connessione caduta (Interrupted), perché non si sa se sono arrivate.
    /// </summary>
    internal static void ResetConnections()
    {
        HttpClient http = Http, downloads = Downloads, uploads = Uploads;
        HttpMessageHandler? handler = net;
#if DEBUG
        if (testHandler is { } fake)
        {
            (Http, Downloads, Uploads, handler) = (new HttpClient(fake, false), DownloadClient(fake), new HttpClient(fake, false), null);
        }
        else
#endif
        {
            net = Network.ApiHandler();
            (Http, Downloads, Uploads) = (ApiClient(net), DownloadClient(net), new HttpClient(UploadHandler()) { Timeout = TimeSpan.FromSeconds(60) });
        }
        Interlocked.Increment(ref generation);
        http.Dispose();
        downloads.Dispose();
        uploads.Dispose();
        handler?.Dispose();
    }

    /// <summary>
    /// La richiesta è caduta perché le connessioni sono state rifatte mentre partiva o aspettava (ResetConnections: il client
    /// di prima, chiuso, annulla le sue richieste, o era già chiuso). Non è colpa del server né di chi l'aveva chiesta.
    /// </summary>
    static bool CutByReset(Exception ex, int sentOn, CancellationToken cancel) =>
        !cancel.IsCancellationRequested && Volatile.Read(ref generation) != sentOn &&
        ex is OperationCanceledException or ObjectDisposedException or HttpRequestException or IOException;

    /// <summary>Una scrittura caduta con le connessioni rifatte: come una connessione caduta (ActionQueue.Passing la rimette in coda).</summary>
    static IOException Interrupted(Exception ex) =>
        new(T("La connessione con Microsoft si è interrotta (il PC è uscito dalla sospensione o la rete è cambiata): riprova."), ex);

    /// <summary>
    /// L'indirizzo a cui va una richiesta con il token dell'account: uno relativo sta sotto Base; uno intero (il nextLink o
    /// il deltaLink di una risposta) vale solo se è di Graph: https, lo stesso nome di Base, la sua porta, senza nome
    /// utente. A un altro indirizzo arriverebbe il token (audit dell'11 ottobre, T-23).
    /// </summary>
    internal static string Target(string url)
    {
        if (!url.StartsWith("https://")) return Base + url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
            && uri.IdnHost.Equals(new Uri(Base).IdnHost, StringComparison.OrdinalIgnoreCase))
            return url;
        throw new MailException(T("Microsoft Graph ha risposto con un indirizzo che non è il suo: la richiesta non è stata inviata."));
    }

    /// <summary>
    /// Outlook accetta quattro richieste insieme per casella e alle altre risponde 429, da ripetere dopo l'attesa che chiede
    /// (qui sempre i 5 s del massimo). Il limite valeva solo dentro ogni chiamata (EachAsync): all'avvio cartelle speciali,
    /// query delta, elenco e categorie insieme lo superavano (audit, v2 V-24: 2 avvii su 79 nei registri della versione di
    /// prova). Ora vale per casella, anche fra più GraphProvider della stessa: al più quattro richieste insieme, e di queste
    /// al più tre che non siano l'apertura di un messaggio (corpo, immagini del testo, invito, conversazione), che così trova
    /// sempre un posto. Chi aspetta un posto aspetta la fine di una richiesta (decimi di secondo), non un Retry-After.
    /// Scaricamenti e caricamenti a pezzi, che durano, al più due insieme: alle richieste brevi resta sempre un posto.
    /// </summary>
    internal sealed class MailboxGate
    {
        internal const int Together = 4, NotOpening = 3, Lengthy = 2;
        readonly SemaphoreSlim all = new(Together), others = new(NotOpening), transfers = new(Lengthy);

        /// <param name="opening">L'apertura di un messaggio: usa anche il posto che le altre richieste non prendono.</param>
        /// <param name="lengthy">Uno scaricamento o un pezzo di un caricamento.</param>
        public async Task<Slot> EnterAsync(bool opening, CancellationToken cancel, bool lengthy = false)
        {
            var held = new List<SemaphoreSlim>(3);
            try
            {
                if (lengthy) await Take(transfers);
                if (!opening || lengthy) await Take(others);
                await Take(all);
            }
            catch
            {
                foreach (var one in held) one.Release();
                throw;
            }
            return new Slot(held);

            async Task Take(SemaphoreSlim one)
            {
                await one.WaitAsync(cancel);
                held.Add(one);
            }
        }

        public readonly struct Slot(List<SemaphoreSlim> held) : IDisposable
        {
            public void Dispose()
            {
                foreach (var one in held) one.Release();
            }
        }
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MailboxGate> gates = new();
    readonly MailboxGate gate = gates.GetOrAdd(account.Id, _ => new MailboxGate());
    /// <summary>Vero dentro GetBodyAsync e GetConversationAsync (e nelle richieste che fanno): l'apertura di un messaggio, per MailboxGate.</summary>
    static readonly AsyncLocal<bool> opening = new();

#if DEBUG
    /// <summary>
    /// Prove con un gestore HTTP finto (Missiva.exe --prova-bozze): le richieste vanno lì, con un token finto. Con null di
    /// nuovo i client veri, per le prove di ResetConnections, che non mandano nulla.
    /// </summary>
    internal static void UseTestHandler(HttpMessageHandler? handler)
    {
        testHandler = handler;
        if (handler == null) (Http, Downloads, Uploads) = (ApiClient(net), DownloadClient(net), new HttpClient(UploadHandler()));
        else (Http, Downloads, Uploads) = (new HttpClient(handler, false), DownloadClient(handler), new HttpClient(handler, false));
        testing = true;
    }

    static bool testing;
    static HttpMessageHandler? testHandler;
    internal static SocketsHttpHandler TestNet => net;
#endif

    /// <summary>
    /// Il token delle richieste: per una casella condivisa quello dell'account di lavoro con i permessi delle caselle
    /// condivise, senza finestre; se il consenso manca nessuna richiesta parte e sotto la casella la barra lo chiede.
    /// </summary>
    async Task<string> TokenAsync()
    {
        if (Account.SharedOf == null)
        {
#if DEBUG
            if (testing) return "prova";
#endif
            return await MicrosoftAuth.TokenAsync(Account);
        }
        return await SharedTokenAsync() ?? throw new MailException(
            T("Per aprire la casella condivisa {0} Missiva deve poter usare le caselle condivise: premi \"Consenti l'accesso alle caselle condivise\".", Account.Email))
            { Fix = AccountFix.Consent };
    }

    /// <summary>Una casella condivisa aperta con un account di lavoro (users/{indirizzo}/ invece di me/).</summary>
    public bool IsShared => Account.SharedOf != null;

    /// <summary>
    /// L'account con cui si accede: per una casella condivisa l'account di lavoro che la apre (i token sono suoi), per gli
    /// altri l'account stesso.
    /// </summary>
    public MailAccount SignInAccount => Account.SharedOf is { } owner
        ? Store.Settings.Accounts.FirstOrDefault(a => a.Id == owner) ?? throw new MailException(T("L'account di lavoro che apriva {0} non c'è più: rimuovi la casella condivisa e aggiungila di nuovo.", Account.Email))
        : Account;

#if DEBUG
    /// <summary>Prove con il gestore finto: se il consenso alle caselle condivise c'è già (nessuna finestra si apre mai).</summary>
    internal static bool TestSharedConsent;
#endif

    /// <summary>
    /// I permessi in più, chiesti con il consenso incrementale solo dal loro pulsante "Consenti ...". Erano cinque copie
    /// dello stesso schema, una per permesso (audit dell'11 ottobre, C-12): ora un permesso nuovo è una voce qui, una riga
    /// in ExtraOf e, per le prove, una in TestConsent.
    /// </summary>
    enum Extra { Shared, Calendar, Settings, Contacts, People }

    /// <summary>Di un permesso in più: gli ambiti da chiedere, il token finto delle prove e la frase quando il consenso non arriva.</summary>
    static (string[] Scopes, string Sample, string Missing) ExtraOf(Extra extra) => extra switch
    {
        Extra.Shared => (MicrosoftAuth.SharedScopes, "prova-condivise", N("Il consenso alle caselle condivise non è arrivato.")),
        Extra.Calendar => (MicrosoftAuth.CalendarScopes, "prova-calendario", N("Il consenso al calendario non è arrivato.")),
        Extra.Settings => (MicrosoftAuth.MailboxSettingsScopes, "prova-impostazioni", N("Il consenso alle impostazioni della casella non è arrivato.")),
        Extra.Contacts => (MicrosoftAuth.ContactsScopes, "prova-contatti", N("Il consenso ai contatti non è arrivato.")),
        _ => (MicrosoftAuth.PeopleScopes, "prova-persone", N("Il consenso alle persone non è arrivato.")),
    };

#if DEBUG
    /// <summary>Prove con il gestore finto: se il consenso a quel permesso c'è già (nessuna finestra si apre mai).</summary>
    static bool TestConsent(Extra extra) => extra switch
    {
        Extra.Shared => TestSharedConsent,
        Extra.Calendar => TestCalendarConsent,
        Extra.Settings => TestSettingsConsent,
        Extra.Contacts => TestContactsConsent,
        _ => TestPeopleConsent,
    };
#endif

    /// <summary>
    /// Il token con un permesso in più; null se manca il consenso (e chi lo chiede non fa nessuna richiesta a Graph).
    /// Interattivo solo dal pulsante. Per le caselle condivise il token è dell'account di lavoro che le apre.
    /// </summary>
    Task<string?> ExtraTokenAsync(Extra extra, bool interactive = false) =>
#if DEBUG
        testing ? Task.FromResult<string?>(TestConsent(extra) || interactive ? ExtraOf(extra).Sample : null) :
#endif
        MicrosoftAuth.ExtraTokenAsync(extra == Extra.Shared ? SignInAccount : Account, ExtraOf(extra).Scopes, interactive);

    /// <summary>La finestra di Microsoft per un permesso in più, dal suo pulsante: se il consenso non arriva lo dice.</summary>
    async Task AllowAsync(Extra extra)
    {
        if (await ExtraTokenAsync(extra, interactive: true) == null) throw new MailException(T(ExtraOf(extra).Missing));
    }

    /// <summary>
    /// Il token con Mail.ReadWrite.Shared e Mail.Send.Shared dell'account di lavoro; null se manca il consenso. Interattivo
    /// solo dal pulsante "Consenti l'accesso alle caselle condivise".
    /// </summary>
    Task<string?> SharedTokenAsync(bool interactive = false) => ExtraTokenAsync(Extra.Shared, interactive);

    public Task AllowSharedAsync() => IsShared ? AllowAsync(Extra.Shared) : Task.CompletedTask;

    /// <summary>Le cartelle speciali di Outlook, per alias; il nome lo dà Missiva (MailFolder.SpecialName: Posta in arrivo, Bozze...).</summary>
    static readonly (string Alias, FolderKind Kind)[] WellKnown =
    {
        ("inbox", FolderKind.Inbox),
        ("drafts", FolderKind.Drafts),
        ("sentitems", FolderKind.Sent),
        ("deleteditems", FolderKind.Deleted),
        ("archive", FolderKind.Archive),
        ("junkemail", FolderKind.Junk),
    };

    public MailAccount Account { get; } = account;
    /// <summary>Evidenziata e Altra ci sono solo nella casella dell'account, non in quelle condivise.</summary>
    public bool SupportsFocused => !IsShared;
    public bool SchedulesOnServer => true;

    // ---------------------------------------------------------------- HTTP

    /// <param name="token">Un token con permessi in più (il calendario, per gli inviti); null è quello di sempre.</param>
    /// <param name="timeZone">Il fuso in cui Graph deve dare gli orari degli eventi (Prefer outlook.timezone), quello di Windows.</param>
    async Task<JsonNode?> SendAsync(HttpMethod method, string url, JsonNode? body = null, bool htmlBody = false, CancellationToken cancel = default,
                                    string? token = null, string? timeZone = null)
    {
        Network.Check(); // --senza-rete (solo Debug)
        bool again = false;
        for (int attempt = 0; ; attempt++)
        {
            int sentOn = Volatile.Read(ref generation);
            using var request = Network.ApiRequest(method, Target(url));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? await TokenAsync());
            var tokenMs = clock.ElapsedMilliseconds;
            if (htmlBody) request.Headers.Add("Prefer", "outlook.body-content-type=\"html\"");
            if (timeZone != null) request.Headers.Add("Prefer", $"outlook.timezone=\"{timeZone}\"");
            if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            // Annullata (si è aperto un altro messaggio), la richiesta si interrompe: Outlook non la conta più fra le quattro
            // contemporanee che accetta per casella. Il posto (MailboxGate) si tiene fino alla risposta intera, non durante
            // l'attesa di un 429.
            HttpResponseMessage answer;
            long queued;
            using (await gate.EnterAsync(opening.Value, cancel))
            {
                queued = clock.ElapsedMilliseconds - tokenMs;
                try
                {
                    answer = await Http.SendAsync(request, cancel);
                }
                // Le connessioni rifatte mentre aspettava (ResetConnections), o la connessione caduta (10054, un ping senza
                // risposta): una lettura una volta di nuovo, su una connessione nuova; le altre non si sa se sono arrivate.
                catch (Exception ex) when (CutByReset(ex, sentOn, cancel) || Network.ConnectionLost(ex, cancel))
                {
                    bool reset = CutByReset(ex, sentOn, cancel);
                    if (method != HttpMethod.Get || again)
                    {
                        if (reset) throw Interrupted(ex);
                        throw;
                    }
                    again = true;
                    Log.Write($"Graph: lettura ripetuta su una connessione nuova ({(reset ? "connessioni rifatte" : "connessione caduta: " + ex.GetBaseException().GetType().Name)})");
                    continue;
                }
            }
            using var response = answer;
            var text = await response.Content.ReadAsStringAsync(cancel);
            Log.Time($"Graph {method} {url[..Math.Min(url.Length, 90)]} ({text.Length / 1024} KB, HTTP/{response.Version}, token {tokenMs} ms{(queued >= 5 ? $", in fila {queued} ms" : "")}{(response.IsSuccessStatusCode ? "" : $", risposta {(int)response.StatusCode}")})", clock);
            // Oltre le quattro richieste contemporanee per casella Outlook risponde 429 senza eseguire la richiesta: si ripete,
            // anche un POST, dopo l'attesa che chiede. All'avvio le sei cartelle speciali chieste insieme ne perdevano ogni
            // tanto una, che diventava una cartella qualsiasi (Posta inviata senza i destinatari nell'elenco). Un 503 si
            // ripete solo per le letture.
            if (attempt < 2 && (response.StatusCode == HttpStatusCode.TooManyRequests ||
                                response.StatusCode == HttpStatusCode.ServiceUnavailable && method == HttpMethod.Get))
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(400 * (attempt + 1));
                await Task.Delay(wait < MaxRetryAfter ? wait : MaxRetryAfter, cancel);
                continue;
            }
            return Result(response, text, IsShared ? Account.Email : null);
        }
    }

    /// <summary>Il corpo della risposta, o l'errore di Graph con il suo messaggio.</summary>
    /// <param name="shared">L'indirizzo della casella condivisa della richiesta: i suoi errori di accesso si spiegano con i permessi da chiedere.</param>
    static JsonNode? Result(HttpResponseMessage response, string text, string? shared = null)
    {
        if (!response.IsSuccessStatusCode)
        {
            string message = response.ReasonPhrase ?? response.StatusCode.ToString(), code = "";
            try
            {
                var error = JsonNode.Parse(text)?["error"];
                message = error?["message"]?.GetValue<string>() ?? message;
                code = error?["code"]?.GetValue<string>() ?? "";
            }
            catch { }
            if (shared != null && SharedError(response.StatusCode, code, shared) is { } explained)
                throw new MailException(explained, status: (int)response.StatusCode) { Code = code };
            if (response.StatusCode == HttpStatusCode.NotFound) throw new NotFoundException(T("Errore di Microsoft Graph: {0}", message));
            // Da un indirizzo che l'account non può usare (Gestisci account, altri indirizzi).
            if (code == "ErrorSendAsDenied")
                throw new MailException(T("Microsoft non ti lascia inviare da questo indirizzo. Con Outlook.com deve essere un alias del tuo account Microsoft (account.microsoft.com, Le tue informazioni); con Microsoft 365 un amministratore deve darti il permesso Invia come su quella casella."),
                                        status: (int)response.StatusCode) { Code = code };
            throw new MailException(response.StatusCode == HttpStatusCode.Forbidden
                ? T("Accesso negato da Microsoft: {0}", message)
                : T("Errore di Microsoft Graph: {0}", message), status: (int)response.StatusCode) { Code = code };
        }
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }

    Task<JsonNode?> GetAsync(string url, bool htmlBody = false, CancellationToken cancel = default) => SendAsync(HttpMethod.Get, url, null, htmlBody, cancel);

    /// <summary>
    /// Un istante UTC come lo vuole Graph (filtri, proprietà), sempre con i due punti: nei formati personalizzati ":" è il
    /// separatore dell'ora di Windows, e con l'ora scritta "HH.mm.ss" Graph rispondeva 400 (niente delta né nuovi messaggi).
    /// </summary>
    internal static string UtcText(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Gli errori di una casella condivisa, detti con quello che si può fare: i permessi sulla casella li dà un
    /// amministratore di Microsoft 365 (Accesso completo per leggerla, Invia come o Invia per conto di per scrivere da lì).
    /// Null per gli altri errori.
    /// </summary>
    internal static string? SharedError(HttpStatusCode status, string code, string shared) => code switch
    {
        "ErrorSendAsDenied" => T("Microsoft non ti lascia inviare come {0}: chiedi all'amministratore di Microsoft 365 il permesso Invia come (o Invia per conto di) su quella casella condivisa.", shared),
        "ErrorInvalidUser" or "MailboxNotEnabledForRESTAPI" or "MailboxNotHostedInExchangeOnline" or "ErrorNonExistentMailbox" =>
            T("La casella {0} non esiste o non è una casella di Exchange Online della tua organizzazione: controlla l'indirizzo (Rimuovi casella condivisa, poi aggiungila di nuovo).", shared),
        _ when status == HttpStatusCode.Forbidden || code == "ErrorAccessDenied" =>
            T("Microsoft non ti lascia aprire la casella condivisa {0}: chiedi all'amministratore di Microsoft 365 il permesso Accesso completo su quella casella. Se l'ha appena dato, possono servire fino a due ore.", shared),
        _ => null,
    };

    /// <summary>L'elemento non esiste (più): per Posponi significa che il messaggio è stato eliminato o spostato.</summary>
    sealed class NotFoundException(string message) : Exception(message);

    /// <summary>Graph ha risposto 404: il messaggio (o la cartella) non c'è più, eliminato o spostato altrove. Per la coda delle azioni.</summary>
    internal static bool IsNotFound(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is NotFoundException) return true;
            if (e is AggregateException { InnerExceptions: [var only] }) return IsNotFound(only);
        }
        return false;
    }

    // ---------------------------------------------------------------- cartelle

    const string FolderFields = "id,displayName,unreadItemCount,totalItemCount,childFolderCount";

    public async Task<List<MailFolder>> GetFoldersAsync()
    {
        var all = await GetAsync($"{me}mailFolders?$top=250&$select={FolderFields}");
        var folders = (all?["value"]?.AsArray() ?? new JsonArray())
            .Select(f => (Id: Str(f, "id"), Name: Str(f, "displayName"), Unread: f?["unreadItemCount"]?.GetValue<int>() ?? 0,
                          Total: f?["totalItemCount"]?.GetValue<int>() ?? 0, Children: f?["childFolderCount"]?.GetValue<int>() ?? 0))
            .ToList();

        // Sottocartelle, a ogni livello (anche dentro Posta in arrivo): l'albero lo costruisce l'elenco. Le figlie di cartelle
        // diverse si chiedono insieme (dentro MailboxGate), non una dopo l'altra (audit, v2 V-38), e si mettono nell'ordine
        // di prima: ogni cartella per nome, seguita dalle sue.
        async Task<List<MailFolder>> Children(string parentId, int depth)
        {
            var page = await GetAsync($"{FolderUrl(parentId)}/childFolders?$top=250&$select={FolderFields}");
            var children = (page?["value"]?.AsArray() ?? new JsonArray()).OrderBy(c => Str(c, "displayName"), StringComparer.CurrentCultureIgnoreCase).ToList();
            var below = await Task.WhenAll(children.Select(c =>
                (c?["childFolderCount"]?.GetValue<int>() ?? 0) > 0 && depth < 6 ? Children(Str(c, "id"), depth + 1) : Task.FromResult(new List<MailFolder>())));
            var list = new List<MailFolder>();
            for (int i = 0; i < children.Count; i++)
            {
                var c = children[i];
                list.Add(new MailFolder
                {
                    Id = Str(c, "id"), Name = Str(c, "displayName"), Kind = FolderKind.Other, ParentId = parentId,
                    Unread = c?["unreadItemCount"]?.GetValue<int>() ?? 0, Total = c?["totalItemCount"]?.GetValue<int>() ?? 0,
                });
                list.AddRange(below[i]);
            }
            return list;
        }
        var trees = Task.WhenAll(folders.Where(f => f.Children > 0).Select(f => Children(f.Id, 1)));

        // Gli identificativi delle cartelle speciali si chiedono per nome: i nomi visualizzati cambiano con la lingua. Quelli
        // di Posta in arrivo, Bozze, Posta inviata, Posta eliminata e Posta indesiderata non cambiano: se la copia locale delle
        // cartelle li ha, e sono ancora nell'elenco appena letto, non si richiedono a ogni avvio. L'Archivio si chiede sempre:
        // da Outlook classico si può scegliere un'altra cartella.
        var cached = await Task.Run(() => MailCache.LoadFolders(Account)) ?? [];
        var ids = await Task.WhenAll(WellKnown.Select(async w =>
        {
            if (w.Kind != FolderKind.Archive && cached.FirstOrDefault(f => f.Kind == w.Kind && f.ParentId == null)?.Id is { } known && folders.Any(f => f.Id == known))
                return known;
            try { return Str(await GetAsync($"{FolderUrl(w.Alias)}?$select=id"), "id"); }
            catch (Exception ex)
            {
                // Un account può non avere l'Archivio; per le altre si vede nel registro perché la cartella è diventata qualsiasi.
                Log.Write($"cartella speciale {w.Alias} non letta: {ex.Message}");
                return "";
            }
        }));

        var result = new List<MailFolder>();
        for (int i = 0; i < WellKnown.Length; i++)
        {
            var match = folders.FirstOrDefault(f => f.Id == ids[i]);
            if (match.Id == null) continue;
            result.Add(new MailFolder { Id = match.Id, Name = MailFolder.SpecialShown(AccountKind.Microsoft, WellKnown[i].Kind), Kind = WellKnown[i].Kind, Unread = match.Unread, Total = match.Total });
        }
        foreach (var f in folders.Where(f => !ids.Contains(f.Id)).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            result.Add(new MailFolder { Id = f.Id, Name = f.Name, Kind = FolderKind.Other, Unread = f.Unread, Total = f.Total });

        foreach (var tree in await trees) result.AddRange(tree);
        return result;
    }

    public Task DeleteFolderAsync(MailFolder folder) => SendAsync(HttpMethod.Delete, $"{FolderUrl(folder.Id)}");

    public async Task<MailFolder> CreateFolderAsync(string name, MailFolder? parent)
    {
        var url = parent == null ? me + "mailFolders" : $"{FolderUrl(parent.Id)}/childFolders";
        var created = await SendAsync(HttpMethod.Post, url, new JsonObject { ["displayName"] = name.Trim() });
        return new MailFolder { Id = Str(created, "id"), Name = name.Trim(), Kind = FolderKind.Other, ParentId = parent?.Id };
    }

    public async Task<string> RenameFolderAsync(MailFolder folder, string name)
    {
        await SendAsync(HttpMethod.Patch, $"{FolderUrl(folder.Id)}", new JsonObject { ["displayName"] = name.Trim() });
        return folder.Id;
    }

    public async Task<string> MoveFolderAsync(MailFolder folder, MailFolder? parent)
    {
        var moved = await SendAsync(HttpMethod.Post, $"{FolderUrl(folder.Id)}/move", new JsonObject { ["destinationId"] = parent?.Id ?? "msgfolderroot" });
        return Str(moved, "id") is { Length: > 0 } id ? id : folder.Id;
    }

    /// <summary>Graph non ha un comando per svuotare: si procede a pagine di 50 messaggi.</summary>
    public async Task EmptyFolderAsync(MailFolder folder, MailFolder? trash)
    {
        bool permanent = folder.Kind is FolderKind.Deleted or FolderKind.Junk || trash == null;
        for (int round = 0; round < 400; round++)
        {
            var page = await GetAsync($"{FolderUrl(folder.Id)}/messages?$select=id&$top=50");
            var ids = (page?["value"]?.AsArray() ?? new JsonArray()).Select(m => Str(m, "id")).ToList();
            if (ids.Count == 0) return;
            await EachAsync(ids, id => permanent
                ? SendAsync(HttpMethod.Delete, $"{MessageUrl(id)}")
                : SendAsync(HttpMethod.Post, $"{MessageUrl(id)}/move", new JsonObject { ["destinationId"] = trash!.Id }));
        }
    }

    public Task MarkAllReadAsync(MailFolder folder) => UnreadSync.WriteAsync(Account.Id, async () =>
    {
        for (int round = 0; round < 400; round++)
        {
            var page = await GetAsync($"{FolderUrl(folder.Id)}/messages?$filter=isRead eq false&$select=id&$top=50");
            var ids = (page?["value"]?.AsArray() ?? new JsonArray()).Select(m => Str(m, "id")).ToList();
            if (ids.Count == 0) return;
            await EachAsync(ids, id => SendAsync(HttpMethod.Patch, $"{MessageUrl(id)}", new JsonObject { ["isRead"] = true }));
        }
    });

    // ---------------------------------------------------------------- messaggi

    public bool SortsOnServer(SortBy sort) => true;

    public async Task<MessagePage> GetMessagesAsync(MailFolder folder, bool? focused, string? next, ListOptions? options = null)
    {
        options ??= ListOptions.Default;
        var url = next ?? MessagesUrl(folder, focused, options);
        JsonNode? page;
        if (next == null && expandAttachments)
        {
            try { page = await GetAsync(url + AttachmentNamesExpand); }
            catch (MailException ex)
            {
                if (ex.Status == (int)HttpStatusCode.BadRequest)
                {
                    Log.Write("Graph non accetta i nomi degli allegati nell'elenco, continuo senza: " + ex.Message);
                    expandAttachments = false;
                }
                page = await GetAsync(url);
            }
        }
        else
        {
            page = await GetAsync(url);
        }
        return new MessagePage
        {
            Messages = Summaries(page, folder.Id),
            Next = page?["@odata.nextLink"]?.GetValue<string>(),
        };
    }

    /// <summary>
    /// Filtro e ordinamento per Graph. Con tutti e due, Graph vuole la proprietà dell'ordinamento anche all'inizio del
    /// filtro: per la data basta "dal 1900", per mittente e oggetto "non vuoto".
    /// </summary>
    string MessagesUrl(MailFolder folder, bool? focused, ListOptions options)
    {
        var sort = options.Sort switch { SortBy.From => "from/emailAddress/name", SortBy.Subject => "subject", _ => "receivedDateTime" };
        var conditions = new List<string>();
        if (focused != null) conditions.Add($"inferenceClassification eq '{(focused.Value ? "focused" : "other")}'");
        if (options.Filter == MessageFilter.Unread) conditions.Add("isRead eq false");
        if (options.Filter == MessageFilter.Flagged) conditions.Add("flag/flagStatus eq 'flagged'");
        if (options.Filter == MessageFilter.Attachments) conditions.Add("hasAttachments eq true");
        string? upTo = options.UpTo is { } d ? $"receivedDateTime lt {UtcText(d.Date.AddDays(1).ToUniversalTime())}" : null;
        if (options.Sort == SortBy.Date)
        {
            if (conditions.Count > 0 || upTo != null) conditions.Insert(0, upTo ?? "receivedDateTime ge 1900-01-01T00:00:00Z");
        }
        else
        {
            if (upTo != null) conditions.Add(upTo);
            if (conditions.Count > 0) conditions.Insert(0, $"{sort} ne null");
        }
        var filter = conditions.Count > 0 ? "&$filter=" + Uri.EscapeDataString(string.Join(" and ", conditions)) : "";
        return $"{FolderUrl(folder.Id)}/messages?$top={PageSize}&$select={SummaryFields}{filter}&$orderby={sort} {(options.Ascending ? "asc" : "desc")}";
    }

    /// <summary>I nomi degli allegati nei risultati, per i suggerimenti dei file; si spegne se Graph non li dà insieme a $search.</summary>
    static bool expandInSearch = true;

    /// <summary>
    /// In tutte le cartelle con me/messages: ogni risultato sa la sua cartella (parentFolderId). Con i nomi degli allegati,
    /// come l'elenco: la ricerca dei file (attachment:...) li mostra nei suggerimenti.
    /// </summary>
    public async Task<MessagePage> SearchAsync(MailFolder? folder, string query, string? next = null, CancellationToken cancel = default)
    {
        // Filtri della ricerca (SearchTerms): da, a, oggetto e allegati in KQL; il giorno esatto, i non letti e i contrassegnati
        // sulla pagina (Matches), perché $search non si combina con $filter. Senza parole da cercare, $filter con le date esatte.
        var terms = SearchTerms.Parse(query);
        var kql = terms.ToKql();
        var where = folder != null ? $"{FolderUrl(folder.Id)}/messages" : me + "messages";
        var url = next ?? (kql.Length > 0
            ? $"{where}?$search={Uri.EscapeDataString("\"" + kql + "\"")}&$top={PageSize}&$select={SummaryFields}"
            : $"{where}?$filter={Uri.EscapeDataString(terms.Filter())}&$orderby=receivedDateTime desc&$top={PageSize}&$select={SummaryFields}");
        JsonNode? page;
        if (next == null && expandInSearch)
        {
            try { page = await GetAsync(url + AttachmentNamesExpand, cancel: cancel); }
            catch (MailException ex) when (ex.Status == (int)HttpStatusCode.BadRequest)
            {
                // Il 400 può venire anche dalla ricerca stessa: l'espansione si spegne solo se senza la richiesta riesce.
                page = await GetAsync(url, cancel: cancel);
                Log.Write("Graph non accetta i nomi degli allegati nella ricerca, continuo senza: " + ex.Message);
                expandInSearch = false;
            }
        }
        else page = await GetAsync(url, cancel: cancel);
        return new MessagePage
        {
            Messages = Summaries(page, folder?.Id).Where(terms.Matches).OrderByDescending(m => m.Instant).ToList(),
            Next = page?["@odata.nextLink"]?.GetValue<string>(),
        };
    }

    public async Task<List<MessageSummary>> GetNewAsync(DateTime sinceUtc)
    {
        var since = UtcText(sinceUtc);
        var page = await GetAsync($"{me}mailFolders/inbox/messages?$top=20&$select={SummaryFields}" +
                                  $"&$filter=receivedDateTime gt {since} and isRead eq false&$orderby=receivedDateTime desc");
        // La cartella dall'id vero (parentFolderId), come negli elenchi: con l'alias "inbox" aprirli non abbassava i non letti.
        return Summaries(page, null);
    }

    public async Task<int> GetInboxUnreadAsync() =>
        (await GetAsync(me + "mailFolders/inbox?$select=unreadItemCount"))?["unreadItemCount"]?.GetValue<int>() ?? 0;

    /// <summary>
    /// Per la copia degli ultimi 30 giorni: solo identificativi, bozze e date, in una richiesta (fino a 1000), invece delle
    /// pagine dell'elenco da 50 righe intere (53 messaggi in 0,8-1,1 s ogni mezz'ora, per trovarli già tutti sul PC).
    /// </summary>
    public async Task<List<MessageSummary>> GetRecentKeysAsync(MailFolder inbox, DateTime sinceUtc, int max)
    {
        var recent = new List<MessageSummary>();
        string? url = $"{FolderUrl(inbox.Id)}/messages?$select=id,isDraft,receivedDateTime&$filter=receivedDateTime ge {UtcText(sinceUtc)}" +
                      $"&$orderby=receivedDateTime desc&$top={Math.Min(max, 1000)}";
        while (url != null && recent.Count < max)
        {
            var page = await GetAsync(url);
            recent.AddRange((page?["value"]?.AsArray() ?? new JsonArray()).Select(m => new MessageSummary
            {
                Id = Str(m, "id"),
                StableId = Str(m, "id"),
                FolderId = inbox.Id, // come le righe dell'elenco della cartella (Summaries): è nel nome del file della copia
                IsDraft = m?["isDraft"]?.GetValue<bool>() ?? false,
                Date = DateTime.TryParse(Str(m, "receivedDateTime"), out var d) ? d.ToLocalTime() : DateTime.MinValue,
            }));
            url = Str(page, "@odata.nextLink") is { Length: > 0 } next ? next : null;
        }
        return recent.Take(max).ToList();
    }

    /// <summary>Le righe intere dei messaggi che mancano alla copia (di solito i più recenti): le pagine dell'elenco fino al più vecchio.</summary>
    public async Task<List<MessageSummary>> GetRecentAsync(MailFolder inbox, IReadOnlyList<MessageSummary> keys)
    {
        if (keys.Count == 0) return new();
        var wanted = keys.Select(k => k.Id).ToHashSet();
        var rows = await ((IMailProvider)this).GetRecentAsync(inbox, keys.Min(k => k.Date).ToUniversalTime(), 500);
        return rows.Where(m => wanted.Contains(m.Id)).ToList();
    }

    /// <summary>Il deltaLink dell'ultima lettura dei cambiamenti della Posta in arrivo; null prima della prima.</summary>
    string? inboxDelta;

    public bool HasInboxDelta => true;

    /// <summary>
    /// I cambiamenti della Posta in arrivo, come il nuovo Outlook e Thunderbird con Exchange: la query delta
    /// (mailFolders/inbox/messages/delta, $select stretto, solo i messaggi degli ultimi due giorni) dà i messaggi nuovi o
    /// cambiati (letti altrove) e quelli usciti (@removed: eliminati o spostati altrove). La prima lettura fa da punto di
    /// partenza e restituisce null; le altre partono dal deltaLink. Un deltaLink che Graph non accetta più riparte da capo.
    /// </summary>
    public async Task<InboxChanges?> GetInboxChangesAsync(CancellationToken cancel = default)
    {
        bool first = inboxDelta == null;
        string? url = inboxDelta ?? $"{me}mailFolders/inbox/messages/delta?$select=id,isRead,receivedDateTime" +
                                    $"&$filter=receivedDateTime ge {UtcText(DateTime.UtcNow.AddDays(-2))}";
        var changes = new InboxChanges();
        try
        {
            for (int page = 0; url != null && page < 40; page++)
            {
                var answer = await SendAsync(HttpMethod.Get, url, cancel: cancel);
                foreach (var m in answer?["value"]?.AsArray() ?? new JsonArray())
                {
                    if (m?["@removed"] != null) changes.Removed.Add(Str(m, "id"));
                    else changes.Seen.Add((Str(m, "id"), m?["isRead"]?.GetValue<bool>() ?? true));
                }
                if (Str(answer, "@odata.deltaLink") is { Length: > 0 } delta)
                {
                    inboxDelta = delta;
                    url = null;
                }
                else url = Str(answer, "@odata.nextLink") is { Length: > 0 } next ? next : null;
            }
        }
        catch (Exception ex) when (!first && (ex is NotFoundException || ex is MailException { Status: 400 or 410 }))
        {
            Log.Write($"delta della Posta in arrivo di {Account.Email} da rifare: {ex.Message}");
            inboxDelta = null;
            deltaRestarted = true;
            return new InboxChanges { Restarted = true };
        }
        if (!first) return changes;
        // Il punto di partenza dopo un deltaLink rifatto: quello che è arrivato fra le due letture non lo dice nessuna delle due
        // (audit di affidabilità del 25 ottobre 2026, A-08), quindi lo dice Restarted, e App controlla l'account. Il primo
        // punto di partenza, all'avvio, no: lo fa il primo controllo dopo l'avvio.
        if (!deltaRestarted) return null;
        deltaRestarted = false;
        return new InboxChanges { Restarted = true };
    }

    /// <summary>Il deltaLink è stato rifatto: la lettura dopo, il punto di partenza nuovo, lo dice (InboxChanges.Restarted).</summary>
    bool deltaRestarted;

    /// <summary>Una richiesta sola, con solo destinatari e data: 500 messaggi in poche centinaia di KB.</summary>
    public async Task<List<SentRecipients>> GetSentRecipientsAsync(int max)
    {
        var page = await GetAsync($"{me}mailFolders/sentitems/messages?$select=toRecipients,ccRecipients,sentDateTime&$orderby=sentDateTime desc&$top={max}");
        return (page?["value"]?.AsArray() ?? new JsonArray()).Select(m => new SentRecipients(
            DateTime.TryParse(Str(m, "sentDateTime"), out var d) ? d.ToLocalTime() : DateTime.MinValue,
            Addresses(m?["toRecipients"]).Concat(Addresses(m?["ccRecipients"])).ToList())).ToList();
    }

    /// <param name="folderId">La cartella elencata; null nella ricerca in tutte, dove vale quella di ogni messaggio.</param>
    static List<MessageSummary> Summaries(JsonNode? page, string? folderId) =>
        (page?["value"]?.AsArray() ?? new JsonArray()).Select(m => new MessageSummary
        {
            Id = Str(m, "id"),
            StableId = Str(m, "id"),
            FolderId = folderId ?? Str(m, "parentFolderId"),
            From = ParseAddress(m?["from"]),
            Subject = Str(m, "subject"),
            Preview = MailHtml.PlainPreview(Str(m, "bodyPreview")),
            Date = DateTime.TryParse(Str(m, "receivedDateTime"), out var d) ? d.ToLocalTime() : DateTime.MinValue,
            IsRead = m?["isRead"]?.GetValue<bool>() ?? true,
            IsFlagged = Str(m?["flag"], "flagStatus") == "flagged",
            FlagDue = Str(m?["flag"], "flagStatus") == "flagged" && m?["flag"]?["dueDateTime"] is JsonObject due ? Calendars.LocalTime(due).Date : null,
            IsDraft = m?["isDraft"]?.GetValue<bool>() ?? false,
            HasAttachments = m?["hasAttachments"]?.GetValue<bool>() ?? false,
            ConversationId = Str(m, "conversationId"),
            AttachmentNames = (m?["attachments"]?.AsArray() ?? new JsonArray())
                .Where(a => !(a?["isInline"]?.GetValue<bool>() ?? false))
                .Select(a => Str(a, "name")).Where(n => n.Length > 0).ToList(),
            Categories = (m?["categories"]?.AsArray() ?? new JsonArray()).Select(c => c?.GetValue<string>() ?? "").Where(c => c.Length > 0).ToList(),
            To = Addresses(m?["toRecipients"]).Take(10).ToList(),
            ReplyTo = Address.ReplyToOf(Addresses(m?["replyTo"]), ParseAddress(m?["from"])),
            IsImportant = Str(m, "importance") == "high",
            InternetMessageId = Str(m, "internetMessageId"),
            Focused = Str(m, "inferenceClassification") switch { "focused" => true, "other" => false, _ => null },
        }).ToList();

    // ---------------------------------------------------------------- categorie

    /// <summary>
    /// Le categorie di Outlook, con i loro colori. Leggerle richiede il permesso MailboxSettings, che l'accesso di base non
    /// chiede: senza il consenso (token non interattivo nullo) nessuna richiesta, e valgono le categorie create in Missiva
    /// (i messaggi le ricevono lo stesso, Outlook le mostra senza colore). Prima la richiesta partiva lo stesso a ogni
    /// avvio e tornava 403.
    /// </summary>
    public async Task<List<Category>> GetCategoriesAsync()
    {
        try
        {
            // Le categorie di Outlook di una casella condivisa vorrebbero i suoi permessi delle impostazioni: lì quelle di Missiva.
            if (!IsShared && await SettingsTokenAsync() is { } token)
            {
                var list = await SendAsync(HttpMethod.Get, "me/outlook/masterCategories", token: token);
                return (list?["value"]?.AsArray() ?? new JsonArray())
                    .Select(c => new Category(Str(c, "displayName"), CategoryPalette.FromGraph(Str(c, "color")))).ToList();
            }
        }
        catch (MailException)
        {
        }
        return Store.Settings.LocalCategories.GetValueOrDefault(Account.Id, new())
            .Select(n => new Category(n, CategoryPalette.PresetOf(Account.Id, n))).ToList();
    }

    public async Task<Category> CreateCategoryAsync(string name, int preset)
    {
        name = name.Trim();
        try
        {
            var token = (IsShared ? null : await SettingsTokenAsync()) ?? throw new MailException(T("Nessun consenso alle impostazioni della casella."));
            await SendAsync(HttpMethod.Post, "me/outlook/masterCategories",
                new JsonObject { ["displayName"] = name, ["color"] = CategoryPalette.GraphName(preset) }, token: token);
        }
        catch (MailException)
        {
            if (!Store.Settings.LocalCategories.TryGetValue(Account.Id, out var local)) Store.Settings.LocalCategories[Account.Id] = local = new();
            if (!local.Contains(name)) local.Add(name);
        }
        Store.Settings.CategoryColors[Account.Id + "|" + name] = preset;
        Store.Save();
        return new Category(name, preset);
    }

    public Task SetCategoryAsync(IReadOnlyList<MessageSummary> messages, string category, bool on) => EachAsync(messages, async message =>
    {
        var updated = message.Categories.Where(c => !c.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        if (on) updated.Add(category);
        await SendAsync(HttpMethod.Patch, $"{MessageUrl(message.Id)}",
            new JsonObject { ["categories"] = new JsonArray(updated.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()) });
        message.Categories = updated;
    });

    public async Task<List<MessageSummary>> GetConversationAsync(MessageSummary message, CancellationToken cancel = default)
    {
        if (message.ConversationId.Length == 0) return new List<MessageSummary> { message };
        opening.Value = true; // come in GetBodyAsync
        var id = Uri.EscapeDataString(message.ConversationId.Replace("'", "''"));
        var page = await GetAsync($"{me}messages?$filter=conversationId eq '{id}'&$top=50&$select={SummaryFields}", cancel: cancel);
        // La cartella vera di ogni messaggio (Posta in arrivo, Posta inviata...) serve per le azioni successive.
        var list = (page?["value"]?.AsArray() ?? new JsonArray())
            .Select(m => (Node: m, Folder: Str(m, "parentFolderId")))
            .SelectMany(x => Summaries(new JsonObject { ["value"] = new JsonArray(x.Node!.DeepClone()) }, x.Folder))
            .OrderBy(m => m.Instant).ToList();
        return list.Count > 0 ? list : new List<MessageSummary> { message };
    }

    /// <summary>Allegati nella stessa richiesta del corpo, solo nomi e dimensioni: senza $select arriverebbe anche il contenuto, anche di 20 MB.</summary>
    const string AttachmentsExpand = "&$expand=attachments($select=id,name,contentType,size,isInline)";

    /// <summary>Corpi chiesti in anticipo (il messaggio dopo quello aperto): il clic prende la richiesta già partita o arrivata.</summary>
    readonly Dictionary<string, Task<MessageBody>> early = new();

    /// <summary>
    /// Il messaggio dopo quello aperto, come fa il nuovo Outlook: una GET come all'apertura, che poi GetBodyAsync prende una
    /// volta sola (una bozza modificata dopo non torna vecchia). Non si interrompe cambiando messaggio: è proprio il clic
    /// su questo che la supera. Al massimo quattro tenuti.
    /// </summary>
    public Task PrefetchAsync(IReadOnlyList<MessageSummary> messages, CancellationToken cancel = default)
    {
        foreach (var message in messages)
        {
            if (cancel.IsCancellationRequested) break;
            lock (early)
            {
                if (early.ContainsKey(message.Id)) continue;
                if (early.Count >= 4) early.Remove(early.Keys.First());
                var body = ReadBodyAsync(message, CancellationToken.None);
                // Se nessuno la prende, un errore non resta un'eccezione mai osservata.
                _ = body.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                early[message.Id] = body;
            }
        }
        return Task.CompletedTask;
    }

    public async Task<MessageBody> GetBodyAsync(MessageSummary message, CancellationToken cancel = default)
    {
        opening.Value = true; // solo per le richieste di questa chiamata (MailboxGate): chi chiama non lo vede
        Task<MessageBody>? coming;
        lock (early) early.Remove(message.Id, out coming);
        if (coming != null)
        {
            try { return await coming.WaitAsync(cancel); }
            catch (Exception) when (!cancel.IsCancellationRequested) { } // non riuscita: si chiede di nuovo
        }
        return await ReadBodyAsync(message, cancel);
    }

    async Task<MessageBody> ReadBodyAsync(MessageSummary message, CancellationToken cancel)
    {
        var url = $"{MessageUrl(message.Id)}?$select=body,toRecipients,ccRecipients,bccRecipients,hasAttachments,internetMessageHeaders";
        JsonNode? m = null;
        if (expandAttachments)
        {
            try { m = await GetAsync(url + AttachmentsExpand, htmlBody: true, cancel); }
            catch (MailException ex) when (ex.Status == (int)HttpStatusCode.BadRequest)
            {
                Log.Write("Graph non accetta gli allegati insieme al corpo, continuo senza: " + ex.Message);
                expandAttachments = false;
            }
            catch (MailException ex)
            {
                Log.Write("Corpo con gli allegati non riuscito, riprovo senza: " + ex.Message);
            }
        }
        m ??= await GetAsync(url, htmlBody: true, cancel);
        var html = Str(m?["body"], "content");
        if (Str(m?["body"], "contentType").Equals("text", StringComparison.OrdinalIgnoreCase))
            html = "<pre>" + WebUtility.HtmlEncode(html) + "</pre>";

        var cids = CidReference().Matches(html).Select(x => x.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = m?["attachments"]?.AsArray();
        if (list == null && ((m?["hasAttachments"]?.GetValue<bool>() ?? false) || cids.Count > 0))
            list = (await GetAsync($"{MessageUrl(message.Id)}/attachments?$select=id,name,contentType,size,isInline", cancel: cancel))?["value"]?.AsArray();
        var nodes = (list ?? new JsonArray()).OfType<JsonNode>().ToList();

        // Le immagini che il testo mostra (cid:) si scaricano subito, con il loro contentId, quattro alla volta: una dopo
        // l'altra, 6-8 loghi (le email di Microsoft) costavano 0,6-0,9 s. Prima quelle incorporate. Outlook a volte segna
        // come non incorporate anche le immagini del testo: di quelle si scaricano solo le necessarie per i cid: rimasti
        // senza immagine, prima quelle con il nome del cid (image001.png@01D...), poi le più piccole, mai le foto
        // allegate (8 foto da 1,5 MB erano 8 richieste da 2 MB solo per mostrare il logo della firma).
        var full = new System.Collections.Concurrent.ConcurrentDictionary<string, JsonNode?>();
        Task FetchAsync(IEnumerable<JsonNode> which) =>
            EachAsync(which.ToList(), async a => full[Str(a, "id")] = await GetAsync($"{MessageUrl(message.Id)}/attachments/{Segment(Str(a, "id"))}", cancel: cancel));
        if (cids.Count > 0)
        {
            await FetchAsync(nodes.Where(a => a["isInline"]?.GetValue<bool>() ?? false));
            static long Size(JsonNode a) => a["size"]?.GetValue<long>() ?? 0;
            bool Named(JsonNode a, IEnumerable<string> missing) => missing.Any(c => c.Split('@')[0].Equals(Str(a, "name"), StringComparison.OrdinalIgnoreCase));
            while (true)
            {
                var missing = cids.Except(full.Values.Select(n => Str(n, "contentId").Trim('<', '>')), StringComparer.OrdinalIgnoreCase).ToList();
                var next = nodes
                    .Where(a => !full.ContainsKey(Str(a, "id")) && Str(a, "contentType").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    .Where(a => Size(a) < 300_000 || Named(a, missing) && Size(a) < 2_000_000)
                    .OrderByDescending(a => Named(a, missing)).ThenBy(Size)
                    .Take(missing.Count).ToList();
                if (next.Count == 0) break;
                await FetchAsync(next);
            }
        }

        var attachments = new List<Attachment>();
        foreach (var a in nodes)
        {
            var id = Str(a, "id");
            bool inline = a["isInline"]?.GetValue<bool>() ?? false;
            string? contentId = null;
            byte[]? content = null;
            if (full.TryGetValue(id, out var image))
            {
                contentId = Str(image, "contentId").Trim('<', '>');
                var bytes = Str(image, "contentBytes");
                if (bytes.Length > 0) content = Convert.FromBase64String(bytes);
                inline = contentId.Length > 0 && html.Contains("cid:" + contentId, StringComparison.OrdinalIgnoreCase);
            }
            attachments.Add(new Attachment
            {
                Id = id,
                Name = Str(a, "name"),
                ContentType = Str(a, "contentType"),
                Size = a["size"]?.GetValue<long>() ?? 0,
                IsInline = inline,
                ContentId = contentId,
                Content = content,
            });
        }

        // Inviti: quelli di Exchange sono eventMessage (lo dice @odata.type); quelli arrivati da fuori, a volte un .ics allegato.
        Invitation? invite = null;
        try
        {
            // Gli inviti di Exchange di una casella condivisa si rispondono sul suo calendario, che Missiva non apre: niente scheda.
            if (Str(m, "@odata.type").Contains("eventMessage", StringComparison.OrdinalIgnoreCase)) invite = IsShared ? null : await EventInviteAsync(message, html, cancel);
            else if (attachments.FirstOrDefault(a => (a.ContentType.StartsWith("text/calendar", StringComparison.OrdinalIgnoreCase) || a.Name.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
                                                     && a.Size < Invitation.MaxLength) is { } ics)
            {
                var file = await GetAsync($"{MessageUrl(message.Id)}/attachments/{Segment(ics.Id)}", cancel: cancel);
                if (Str(file, "contentBytes") is { Length: > 0 } bytes) invite = await Invitation.ParseAsync(Encoding.UTF8.GetString(Convert.FromBase64String(bytes)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Write($"invito non letto ({message.Id}): {ex.Message}"); // il messaggio si mostra lo stesso, senza la scheda
        }

        var headers = m?["internetMessageHeaders"]?.AsArray() ?? new JsonArray();
        string? Header(string name) => headers.FirstOrDefault(x => Str(x, "name").Equals(name, StringComparison.OrdinalIgnoreCase)) is { } found ? Str(found, "value") : null;
#if DEBUG
        // Per le prove di "Annulla iscrizione": se la riga non c'è, si vede se è perché il messaggio non ha l'intestazione.
        Log.Write($"corpo di Graph: {headers.Count} intestazioni, List-Unsubscribe {(Header("List-Unsubscribe") != null ? "c'è" : "non c'è")}");
#endif
        return new MessageBody
        {
            Invite = invite,
            // Le intestazioni vengono con il corpo (internetMessageHeaders): quelle di chi ha verificato il mittente e le due di
            // "Annulla iscrizione" (Unsubscribe).
            AuthenticationResults = headers
                .Where(x => Str(x, "name").Equals("Authentication-Results", StringComparison.OrdinalIgnoreCase)).Select(x => Str(x, "value")).ToList(),
            ListUnsubscribe = Header("List-Unsubscribe"),
            ListUnsubscribePost = Header("List-Unsubscribe-Post"),
            Html = html,
            To = Addresses(m?["toRecipients"]),
            Cc = Addresses(m?["ccRecipients"]),
            Bcc = Addresses(m?["bccRecipients"]),
            Attachments = attachments,
        };
    }

    public async Task<byte[]> GetAttachmentAsync(MessageSummary message, Attachment attachment, IProgress<long>? progress = null, CancellationToken cancel = default)
    {
        if (attachment.Content is { } kept)
        {
            progress?.Report(kept.Length);
            return kept;
        }
        return await DownloadAsync($"{MessageUrl(message.Id)}/attachments/{Segment(attachment.Id)}/$value", whole: false, cancel: cancel, progress: progress);
    }

    /// <summary>Senza dati per tanto tempo, uno scaricamento si interrompe (le prove di --prova-bozze lo accorciano).</summary>
    internal static TimeSpan DownloadIdle = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Allegati e messaggi interi. Non con Http, il cui limite di 60 s vale per tutta la risposta: un allegato da 20 MB
    /// sotto circa 3 Mbit/s (l'hotspot del telefono) non arrivava mai. Qui il limite vale per l'attesa: si interrompe solo
    /// se per 60 s non arriva nulla. Con limit si ferma dopo tanti byte (l'Origine), e il resto non arriva. Con cancel
    /// superato (un altro messaggio aperto, Annulla del gestore degli scaricamenti) smette fra un blocco e l'altro. Con
    /// progress i byte arrivati dopo ogni blocco, e prima la lunghezza dichiarata dalla risposta, se c'è (IExpectedSize).
    /// </summary>
    /// <param name="whole">Il messaggio intero (l'Origine, Salva con nome .eml), non un allegato: cambia solo la frase dell'errore.</param>
    async Task<byte[]> DownloadAsync(string url, bool whole, int limit = int.MaxValue, CancellationToken cancel = default, IProgress<long>? progress = null)
    {
        // Come le altre letture (SendAsync): caduto con le connessioni rifatte o con la connessione, una volta da capo.
        for (bool again = false; ; again = true)
        {
            int sentOn = Volatile.Read(ref generation);
            try
            {
                return await DownloadOnceAsync(url, whole, limit, cancel, progress);
            }
            catch (Exception ex) when (!again && (CutByReset(ex, sentOn, cancel) || Network.ConnectionLost(ex, cancel)))
            {
                Log.Write($"Graph: scaricamento ripetuto su una connessione nuova ({(CutByReset(ex, sentOn, cancel) ? "connessioni rifatte" : "connessione caduta: " + ex.GetBaseException().GetType().Name)})");
            }
        }
    }

    async Task<byte[]> DownloadOnceAsync(string url, bool whole, int limit, CancellationToken cancel, IProgress<long>? progress)
    {
        using var request = Network.ApiRequest(HttpMethod.Get, Base + url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        cancel.ThrowIfCancellationRequested();
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        try
        {
            // Uno dei posti della casella (MailboxGate) per tutto lo scaricamento; l'attesa del posto non conta come silenzio.
            using var slot = await gate.EnterAsync(opening: false, cancel, lengthy: true);
            idle.CancelAfter(DownloadIdle);
            using var response = await Downloads.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            if (!response.IsSuccessStatusCode)
                throw new MailException(whole ? T("Non riesco a scaricare il messaggio.") : T("Non riesco a scaricare l'allegato."), status: (int)response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(idle.Token);
            using var result = new MemoryStream(response.Content.Headers.ContentLength is long length and < int.MaxValue ? (int)Math.Min(length, limit) : 0);
            if (progress is IExpectedSize expected && response.Content.Headers.ContentLength is long declared and > 0) expected.Expect(Math.Min(declared, limit));
            var buffer = new byte[81920];
            while (result.Length < limit)
            {
                idle.CancelAfter(DownloadIdle);
                int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - result.Length)), idle.Token);
                if (read == 0) break;
                result.Write(buffer, 0, read);
                progress?.Report(result.Length);
            }
            return result.ToArray();
        }
        catch (OperationCanceledException) when (idle.IsCancellationRequested && !cancel.IsCancellationRequested)
        {
            throw new MailException(whole ? T("Non riesco a scaricare il messaggio: da {0:0} secondi la connessione non manda nulla.", DownloadIdle.TotalSeconds)
                                          : T("Non riesco a scaricare l'allegato: da {0:0} secondi la connessione non manda nulla.", DownloadIdle.TotalSeconds));
        }
    }

    // ---------------------------------------------------------------- azioni

    /// <summary>
    /// Graph vuole una richiesta per messaggio: si mandano quattro alla volta invece che una dopo l'altra (Outlook ne
    /// accetta al massimo quattro insieme per casella). 50 messaggi: da 50 giri di 60-95 ms a circa 13.
    /// </summary>
    static async Task EachAsync<T>(IEnumerable<T> items, Func<T, Task> action)
    {
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(items.Select(async item =>
        {
            await gate.WaitAsync();
            try { await action(item); }
            finally { gate.Release(); }
        }));
    }

    /// <remarks>
    /// Letto, spostato, eliminato, posposto: le scritture che cambiano i non letti passano da UnreadSync.WriteAsync, come
    /// quelle di Gmail e IMAP da InboxIdle.OwnAsync. Un conteggio del server chiesto mentre sono in viaggio non vale, e
    /// quando finiscono il numero si riconta.
    /// </remarks>
    public Task SetReadAsync(IReadOnlyList<MessageSummary> messages, bool read) => UnreadSync.WriteAsync(Account.Id, () => EachAsync(messages, m =>
        InReadOrder(m.Id, () => SendAsync(HttpMethod.Patch, $"{MessageUrl(m.Id)}", new JsonObject { ["isRead"] = read }))), ReadCountSettle);

    /// <summary>
    /// Quanto dopo il letto (o il da leggere) il numero dei non letti che Graph dà (unreadItemCount) è ancora quello di prima:
    /// Exchange lo aggiorna qualche secondo dopo aver risposto al PATCH. Nel registro dell'autore (22 e 24 ottobre 2026) il
    /// riconteggio un secondo dopo la lettura (UnreadSync.Settle) rimetteva il numero di prima, per 1-13 secondi, finché la
    /// query delta non lo correggeva. Fino ad allora il conteggio del server non vale (UnreadSync.Ticket), e il riconteggio
    /// (App.RecountSoon) aspetta. Cinque secondi: il 24 ottobre il numero vero c'era già 2-3 secondi dopo; il 22 era ancora
    /// quello di prima dopo uno, e la query delta l'ha corretto dopo 13 (in mezzo il registro non dice).
    /// </summary>
    internal static TimeSpan ReadCountSettle = TimeSpan.FromSeconds(5);

    /// <summary>Per messaggio, l'ultima scrittura di lettura chiesta (la fine della sua attesa): quella dopo parte quando è finita.</summary>
    readonly Dictionary<string, Task> readOrder = new();

    /// <summary>
    /// Le scritture di lettura dello stesso messaggio, una dopo l'altra nell'ordine in cui sono state chieste. Ogni PATCH è
    /// una richiesta a sé, fino a quattro insieme (MailboxGate), e un 429 si ripete dopo l'attesa che Outlook chiede: il letto
    /// del riquadro respinto e ripetuto arrivava dopo il "da leggere" di Ctrl+U chiesto un attimo dopo, e vinceva (sul server
    /// letto, in Missiva da leggere; audit di affidabilità del 25 ottobre 2026, L-06). Gmail e IMAP non ne hanno bisogno:
    /// una connessione, un comando alla volta, nell'ordine. Messaggi diversi non si aspettano; quella prima, riuscita o no,
    /// fa partire quella dopo.
    /// </summary>
    Task InReadOrder(string id, Func<Task> write)
    {
        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task before;
        lock (readOrder)
        {
            before = readOrder.GetValueOrDefault(id) ?? Task.CompletedTask;
            readOrder[id] = mine.Task;
        }
        return AfterAsync();

        async Task AfterAsync()
        {
            try
            {
                await before; // non fallisce mai: è la fine dell'attesa di quella prima
                await write();
            }
            finally
            {
                mine.SetResult();
                lock (readOrder) if (readOrder.GetValueOrDefault(id) == mine.Task) readOrder.Remove(id);
            }
        }
    }

    public Task SetFlagAsync(IReadOnlyList<MessageSummary> messages, bool flagged) => EachAsync(messages, m =>
        SendAsync(HttpMethod.Patch, $"{MessageUrl(m.Id)}",
            new JsonObject { ["flag"] = new JsonObject { ["flagStatus"] = flagged ? "flagged" : "notFlagged" } }));

    /// <summary>
    /// Contrassegna con scadenza, come Outlook: flag con flagStatus, startDateTime e dueDateTime (senza startDateTime Graph
    /// risponde 400) nel fuso di Windows, a mezzanotte; senza data le date si tolgono. Basta Mail.ReadWrite.
    /// </summary>
    public Task SetFlagDueAsync(IReadOnlyList<MessageSummary> messages, DateTime? start, DateTime? due)
    {
        var zone = TimeZoneInfo.Local.Id;
        JsonNode? Day(DateTime? d) => d is { } day
            ? new JsonObject { ["dateTime"] = day.Date.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), ["timeZone"] = zone }
            : null;
        if (due != null) start ??= due.Value.Date < DateTime.Today ? due : DateTime.Today;
        return EachAsync(messages, m => SendAsync(HttpMethod.Patch, $"{MessageUrl(m.Id)}", new JsonObject
        {
            ["flag"] = new JsonObject { ["flagStatus"] = "flagged", ["startDateTime"] = Day(start), ["dueDateTime"] = Day(due) },
        }));
    }

    /// <summary>Segna come completato: flagStatus complete con completedDateTime, come Outlook (resta fra i completati).</summary>
    public Task CompleteFlagAsync(IReadOnlyList<MessageSummary> messages)
    {
        var now = new JsonObject
        {
            ["dateTime"] = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["timeZone"] = TimeZoneInfo.Local.Id,
        };
        return EachAsync(messages, m => SendAsync(HttpMethod.Patch, $"{MessageUrl(m.Id)}", new JsonObject
        {
            ["flag"] = new JsonObject { ["flagStatus"] = "complete", ["completedDateTime"] = now.DeepClone() },
        }));
    }

    /// <summary>Lo spostamento dà l'id del messaggio nella cartella di arrivo: serve ad Annulla.</summary>
    public Task<List<MovedMessage>> MoveAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination) => UnreadSync.WriteAsync(Account.Id, async () =>
    {
        var ids = new System.Collections.Concurrent.ConcurrentDictionary<MessageSummary, string>();
        await EachAsync(messages.Where(m => m.FolderId != destination.Id), async m =>
            ids[m] = Str(await SendAsync(HttpMethod.Post, $"{MessageUrl(m.Id)}/move", new JsonObject { ["destinationId"] = destination.Id }), "id"));
        return messages.Where(ids.ContainsKey).Select(m => new MovedMessage(m, ids[m])).ToList();
    });

    /// <summary>Lo spostamento opposto, sull'id avuto dal primo; ne arriva un altro, quello del messaggio tornato.</summary>
    public Task<List<MovedMessage>> UndoMoveAsync(IReadOnlyList<MovedMessage> moved, MailFolder destination) => UnreadSync.WriteAsync(Account.Id, async () =>
    {
        var ids = new System.Collections.Concurrent.ConcurrentDictionary<MovedMessage, string>();
        await EachAsync(moved, async x =>
            ids[x] = Str(await SendAsync(HttpMethod.Post, $"{MessageUrl(x.NewId)}/move", new JsonObject { ["destinationId"] = x.Original.FolderId }), "id"));
        return moved.Where(ids.ContainsKey).Select(x => x with { NewId = ids[x] }).ToList();
    });

    // ---------------------------------------------------------------- Posponi

    public async Task<MailFolder> EnsureFolderAsync(string name)
    {
        var filter = Uri.EscapeDataString($"displayName eq '{name.Replace("'", "''")}'");
        var found = (await GetAsync($"{me}mailFolders?$filter={filter}&$select=id,displayName"))?["value"]?.AsArray().FirstOrDefault();
        found ??= await SendAsync(HttpMethod.Post, me + "mailFolders", new JsonObject { ["displayName"] = name });
        return new MailFolder { Id = Str(found, "id"), Name = name, Kind = FolderKind.Other };
    }

    /// <summary>Spostare un messaggio ne cambia l'identificativo: la chiave è quello nuovo, dato dallo spostamento.</summary>
    public Task<string> SnoozeAsync(MessageSummary message, MailFolder snoozeFolder) => UnreadSync.WriteAsync(Account.Id, async () =>
    {
        var moved = await SendAsync(HttpMethod.Post, $"{MessageUrl(message.Id)}/move", new JsonObject { ["destinationId"] = snoozeFolder.Id });
        return Str(moved, "id");
    });

    public Task<string?> WakeAsync(string key, MailFolder snoozeFolder, MailFolder inbox) => UnreadSync.WriteAsync(Account.Id, async () =>
    {
        try
        {
            await SendAsync(HttpMethod.Patch, $"{MessageUrl(key)}", new JsonObject { ["isRead"] = false });
            var moved = await SendAsync(HttpMethod.Post, $"{MessageUrl(key)}/move", new JsonObject { ["destinationId"] = inbox.Id });
            return (string?)Str(moved, "id");
        }
        catch (NotFoundException)
        {
            return null;
        }
    });

    public Task CopyAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination) => EachAsync(messages, m =>
        SendAsync(HttpMethod.Post, $"{MessageUrl(m.Id)}/copy", new JsonObject { ["destinationId"] = destination.Id }));

    public Task<byte[]> GetRawAsync(MessageSummary message, int limit = int.MaxValue, CancellationToken cancel = default, IProgress<long>? progress = null) =>
        DownloadAsync($"{MessageUrl(message.Id)}/$value", whole: true, limit, cancel, progress);

    public Task DeletePermanentlyAsync(IReadOnlyList<MessageSummary> messages) => UnreadSync.WriteAsync(Account.Id, () => EachAsync(messages, m =>
        SendAsync(HttpMethod.Delete, $"{MessageUrl(m.Id)}")));

    public async Task SendAsync(OutgoingMessage mail)
    {
        if (mail.Calendar != null)
        {
            await SendMimeAsync(mail);
            return;
        }
        var (id, _, created, _, _) = await WriteDraftAsync(mail);
        try
        {
            mail.Cancel.ThrowIfCancellationRequested(); // la scrittura chiusa mentre gli allegati si caricavano: non parte
            try
            {
                await SendAsync(HttpMethod.Post, $"{MessageUrl(id)}/send");
            }
            catch (MailException ex) when (IsShared && ex.Code == "ErrorSendAsDenied")
            {
                // Senza Invia come, con Invia per conto di: come Outlook il messaggio parte "da ... per conto di ...", con
                // sender l'account di lavoro. Se l'amministratore non ha dato nemmeno quello, resta la spiegazione.
                Log.Write($"invio da {Account.Email}: Invia come negato, provo per conto di {SignInAccount.Email}");
                await SendAsync(HttpMethod.Patch, $"{MessageUrl(id)}", new JsonObject
                {
                    ["from"] = new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = Account.Email } },
                    ["sender"] = new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = SignInAccount.Email } },
                });
                await SendAsync(HttpMethod.Post, $"{MessageUrl(id)}/send");
            }
        }
        catch
        {
            if (created) await DeleteDraftAsync(id);
            throw;
        }
    }

    /// <summary>
    /// Le risposte iMIP (inviti arrivati come .ics): Graph non sa mettere una parte text/calendar con il suo metodo, quindi
    /// il messaggio parte in formato MIME (me/sendMail con il MIME in base64, solo il permesso Mail.Send).
    /// </summary>
    async Task SendMimeAsync(OutgoingMessage mail)
    {
        var mime = new MimeKit.MimeMessage { Subject = mail.Subject, Body = Invitation.MimeBody(mail) };
        mime.From.Add(new MimeKit.MailboxAddress(Account.DisplayName, Account.Email));
        foreach (var a in mail.To) mime.To.Add(new MimeKit.MailboxAddress(a.Name, a.Email));
        foreach (var a in mail.Cc) mime.Cc.Add(new MimeKit.MailboxAddress(a.Name, a.Email));
        using var buffer = new MemoryStream();
        await mime.WriteToAsync(buffer);
        using var request = Network.ApiRequest(HttpMethod.Post, Base + me + "sendMail");
        request.Content = new StringContent(Convert.ToBase64String(buffer.ToArray()), Encoding.ASCII, "text/plain");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        using var slot = await gate.EnterAsync(opening: false, default);
        int sentOn = Volatile.Read(ref generation);
        HttpResponseMessage answer;
        try
        {
            answer = await Http.SendAsync(request);
        }
        catch (Exception ex) when (CutByReset(ex, sentOn, default))
        {
            throw Interrupted(ex);
        }
        using var response = answer;
        Result(response, await response.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- inviti

#if DEBUG
    /// <summary>Prove con il gestore finto: se il consenso al calendario c'è già (nessuna finestra si apre mai).</summary>
    internal static bool TestCalendarConsent;
#endif

    /// <summary>Il token con il permesso del calendario; null se manca il consenso. Interattivo solo dal pulsante della scheda.</summary>
    Task<string?> CalendarTokenAsync(bool interactive = false) => ExtraTokenAsync(Extra.Calendar, interactive);

    public Task AllowCalendarAsync() => AllowAsync(Extra.Calendar);

    // ---------------------------------------------------------------- impostazioni della casella

#if DEBUG
    /// <summary>Prove con il gestore finto: se il consenso alle impostazioni della casella c'è già.</summary>
    internal static bool TestSettingsConsent;
#endif

    /// <summary>
    /// Il token con MailboxSettings.ReadWrite (risposte automatiche, categorie di Outlook); null se manca il consenso.
    /// Interattivo solo dal pulsante "Consenti l'accesso alle impostazioni della casella".
    /// </summary>
    Task<string?> SettingsTokenAsync(bool interactive = false) => ExtraTokenAsync(Extra.Settings, interactive);

    public Task AllowMailboxSettingsAsync() => AllowAsync(Extra.Settings);

    /// <summary>Le risposte automatiche della casella dell'account; quelle di una casella condivisa le cambia l'amministratore.</summary>
    public bool HasAutoReplies => !IsShared;

    /// <summary>Le risposte automatiche (me/mailboxSettings/automaticRepliesSetting); null senza consenso, e nessuna richiesta.</summary>
    public async Task<AutoReplies?> GetAutoRepliesAsync()
    {
        var token = await SettingsTokenAsync();
        if (token == null) return null;
        var setting = await SendAsync(HttpMethod.Get, "me/mailboxSettings/automaticRepliesSetting", token: token);
        return AutoReplies.FromGraph(setting);
    }

    public async Task SetAutoRepliesAsync(AutoReplies replies)
    {
        var token = await SettingsTokenAsync() ?? throw new MailException(
            T("Per cambiare le risposte automatiche serve l'accesso alle impostazioni della casella: premi \"Consenti l'accesso alle impostazioni della casella\"."));
        await SendAsync(HttpMethod.Patch, "me/mailboxSettings", replies.ToGraph(), token: token);
    }

    // ---------------------------------------------------------------- regole

    const string RulesUrl = "me/mailFolders/inbox/messageRules";

    /// <summary>Le regole di Outlook della casella dell'account; quelle di una casella condivisa le gestisce l'amministratore.</summary>
    public bool HasServerRules => !IsShared;

    /// <summary>
    /// Le regole della Posta in arrivo (me/mailFolders/inbox/messageRules), le stesse di Outlook sul web, con il token di
    /// MailboxSettings.ReadWrite delle risposte automatiche; null senza consenso, e nessuna richiesta.
    /// </summary>
    public async Task<List<MailRule>?> GetRulesAsync()
    {
        if (IsShared || await SettingsTokenAsync() is not { } token) return null;
        var list = await SendAsync(HttpMethod.Get, RulesUrl, token: token);
        return (list?["value"]?.AsArray() ?? new JsonArray()).Select(r => Rules.FromGraph(r, Account.Id)).OrderBy(r => r.Sequence).ToList();
    }

    /// <summary>Calcolato a ogni uso (non un const): T, nella lingua di Missiva, che si sceglie all'avvio.</summary>
    static string RulesConsentNeeded => T("Per cambiare le regole di Outlook serve l'accesso alle impostazioni della casella: premi \"Consenti l'accesso alle impostazioni della casella\".");

    /// <summary>POST della regola nuova; per una cambiata PATCH dei soli campi cambiati (Rules.Changes), nessuna richiesta senza modifiche.</summary>
    public async Task<string> SaveRuleAsync(MailRule? before, MailRule after)
    {
        var token = (IsShared ? null : await SettingsTokenAsync()) ?? throw new MailException(RulesConsentNeeded);
        if (before == null) return Str(await SendAsync(HttpMethod.Post, RulesUrl, Rules.ToGraph(after), token: token), "id");
        var changes = Rules.Changes(before, after);
        if (changes.Count > 0) await SendAsync(HttpMethod.Patch, $"{RulesUrl}/{Segment(before.ServerId, escape: true)}", changes, token: token);
        return before.ServerId;
    }

    public async Task DeleteRuleAsync(MailRule rule)
    {
        var token = (IsShared ? null : await SettingsTokenAsync()) ?? throw new MailException(RulesConsentNeeded);
        await SendAsync(HttpMethod.Delete, $"{RulesUrl}/{Segment(rule.ServerId, escape: true)}", token: token);
    }

    // ---------------------------------------------------------------- contatti

#if DEBUG
    /// <summary>Prove con il gestore finto: se il consenso ai contatti c'è già (nessuna finestra si apre mai).</summary>
    internal static bool TestContactsConsent;
#endif

    /// <summary>Il token con Contacts.ReadWrite; null se manca il consenso. Interattivo solo dal pulsante della vista Persone.</summary>
    Task<string?> ContactsTokenAsync(bool interactive = false) => ExtraTokenAsync(Extra.Contacts, interactive);

    /// <summary>I contatti di Outlook dell'account; quelli di una casella condivisa vorrebbero Contacts.ReadWrite.Shared.</summary>
    public bool HasContacts => !IsShared;

    public Task AllowContactsAsync() => AllowAsync(Extra.Contacts);

    static string ContactsConsentNeeded => T("Per scrivere nei contatti di Outlook Missiva deve poterli usare: premi \"Consenti l'accesso ai contatti\" in Persone.");

    /// <summary>
    /// I contatti di Outlook, come Persone del nuovo Outlook: me/contacts a pagine di 100 (@odata.nextLink, al massimo 50),
    /// solo i campi della scheda. Senza consenso null e nessuna richiesta.
    /// </summary>
    public async Task<List<Contact>?> GetContactsAsync(CancellationToken cancel = default)
    {
        var token = await ContactsTokenAsync();
        if (token == null) return null;
        var found = new List<Contact>();
        string? url = $"me/contacts?$top=100&$select={ContactFiles.GraphFields}&$orderby=displayName";
        for (int page = 0; url != null && page < 50; page++)
        {
            var answer = await SendAsync(HttpMethod.Get, url, cancel: cancel, token: token);
            found.AddRange((answer?["value"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>().Select(c => ContactFiles.FromGraph(c, Account.Id)));
            url = Str(answer, "@odata.nextLink") is { Length: > 0 } nextLink ? nextLink : null;
        }
        return found;
    }

    /// <summary>Nuovo: POST me/contacts; cambiato: PATCH me/contacts/{id} dei soli campi cambiati, nessuna richiesta se nulla è cambiato.</summary>
    public async Task<string> SaveContactAsync(Contact? before, Contact after)
    {
        var token = await ContactsTokenAsync() ?? throw new MailException(ContactsConsentNeeded);
        var body = ContactFiles.ToGraph(before, after);
        if (before?.ServerId is { Length: > 0 } id)
        {
            if (body.Count > 0) await SendAsync(HttpMethod.Patch, $"me/contacts/{Segment(id, escape: true)}", body, token: token);
            return id;
        }
        return Str(await SendAsync(HttpMethod.Post, "me/contacts", body, token: token), "id");
    }

    public async Task DeleteContactAsync(Contact contact)
    {
        var token = await ContactsTokenAsync() ?? throw new MailException(ContactsConsentNeeded);
        if (contact.ServerId is { Length: > 0 } id) await SendAsync(HttpMethod.Delete, $"me/contacts/{Segment(id, escape: true)}", token: token);
    }

    // ---------------------------------------------------------------- persone (colleghi)

#if DEBUG
    /// <summary>Prove con il gestore finto: se il consenso alle persone (People.Read) c'è già.</summary>
    internal static bool TestPeopleConsent;
#endif

    /// <summary>Il token con People.Read; null se manca il consenso. Interattivo solo da "Cerca anche fra i colleghi".</summary>
    Task<string?> PeopleTokenAsync(bool interactive = false) => ExtraTokenAsync(Extra.People, interactive);

    /// <summary>I colleghi della directory ci sono solo per gli account di lavoro o della scuola (non Outlook.com né le caselle condivise).</summary>
    public bool HasPeople => !IsShared && Account.Tenant != null;

    public Task AllowPeopleAsync() => HasPeople ? AllowAsync(Extra.People) : Task.CompletedTask;

    /// <summary>
    /// Le persone rilevanti di Microsoft 365 che corrispondono al testo (me/people?$search, anche i colleghi a cui non si è
    /// mai scritto), con nome, indirizzi e azienda; null senza il consenso (nessuna richiesta).
    /// </summary>
    public async Task<List<Contact>?> SearchPeopleAsync(string text, CancellationToken cancel = default)
    {
        if (!HasPeople || await PeopleTokenAsync() is not { } token) return null;
        var q = Uri.EscapeDataString("\"" + text.Replace("\"", "").Trim() + "\"");
        var answer = await SendAsync(HttpMethod.Get, $"me/people?$search={q}&$top=10&$select=displayName,scoredEmailAddresses,companyName,jobTitle", cancel: cancel, token: token);
        return (answer?["value"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>().Select(p => new Contact
        {
            Source = Account.Id, DisplayName = Str(p, "displayName"), Company = Str(p, "companyName"), JobTitle = Str(p, "jobTitle"),
            Emails = (p["scoredEmailAddresses"]?.AsArray() ?? new JsonArray()).Select(e => Str(e, "address")).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        }).Where(c => c.Emails.Count > 0).ToList();
    }

    // ---------------------------------------------------------------- calendario

    /// <summary>Il calendario dell'account; quello di una casella condivisa vorrebbe Calendars.ReadWrite.Shared (non ancora).</summary>
    public bool HasCalendar => !IsShared;

    /// <summary>
    /// Il calendario, come nel nuovo Outlook: i calendari dell'account (me/calendars, con i colori) e gli eventi
    /// dell'intervallo di ognuno (calendarView, che espande le ricorrenze), con gli orari nel fuso di Windows (Prefer
    /// outlook.timezone) e solo i campi che servono. Serve il consenso al calendario: senza, null e nessuna richiesta
    /// (il pulsante "Consenti l'accesso al calendario" lo chiede, mai una finestra da sola).
    /// </summary>
    public async Task<CalendarData?> GetCalendarAsync(DateTime from, DateTime to, CancellationToken cancel = default)
    {
        var token = await CalendarTokenAsync();
        if (token == null) return null;
        var zone = TimeZoneInfo.Local.Id;
        var list = await SendAsync(HttpMethod.Get, "me/calendars?$select=id,name,hexColor,color,isDefaultCalendar,canEdit&$top=50", cancel: cancel, token: token);
        var calendars = (list?["value"]?.AsArray() ?? new JsonArray()).Select((c, i) =>
            new CalendarInfo(Str(c, "id"), Str(c, "name"), Calendars.Color(c, i), c?["isDefaultCalendar"]?.GetValue<bool>() ?? false, c?["canEdit"]?.GetValue<bool>() ?? true))
            .Where(c => c.Id.Length > 0).ToList();
        var range = $"startDateTime={Uri.EscapeDataString(Calendars.Iso(from))}&endDateTime={Uri.EscapeDataString(Calendars.Iso(to))}";
        // Al massimo tre calendari alla volta: Outlook accetta quattro richieste insieme per casella, e la posta ne usa.
        using var gate = new SemaphoreSlim(3);
        var events = await Task.WhenAll(calendars.Select(async c =>
        {
            await gate.WaitAsync(cancel);
            try
            {
                var found = new List<CalendarEvent>();
                string? url = $"me/calendars/{Segment(c.Id)}/calendarView?{range}&$select={Calendars.EventFields}&$orderby=start/dateTime&$top=250";
                for (int page = 0; url != null && page < 10; page++)
                {
                    var answer = await SendAsync(HttpMethod.Get, url, cancel: cancel, token: token, timeZone: zone);
                    found.AddRange((answer?["value"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>().Select(e => Calendars.Event(e, c.Id)));
                    url = Str(answer, "@odata.nextLink") is { Length: > 0 } nextLink ? nextLink : null;
                }
                return found;
            }
            finally
            {
                gate.Release();
            }
        }));
        return new CalendarData { Calendars = calendars, Events = events.SelectMany(e => e).ToList(), From = from, To = to };
    }

    static string CalendarConsentNeeded => T("Per scrivere nel calendario Missiva deve poterlo usare: premi \"Consenti l'accesso al calendario\" nel calendario.");

    /// <summary>
    /// Nuovo evento, come il nuovo Outlook: POST me/calendars/{id}/events con il fuso di Windows (Calendars.ToGraph); con
    /// partecipanti Exchange manda gli inviti. La riunione di Teams solo per gli account di lavoro o della scuola.
    /// </summary>
    public async Task<string> CreateEventAsync(NewEvent e)
    {
        var token = await CalendarTokenAsync() ?? throw new MailException(CalendarConsentNeeded);
        if (e.OnlineMeeting && Account.Tenant == null) throw new MailException(T("Le riunioni di Teams si creano solo con gli account di lavoro o della scuola."));
        var path = e.CalendarId.Length > 0 ? $"me/calendars/{Segment(e.CalendarId, escape: true)}/events" : "me/events";
        var created = await SendAsync(HttpMethod.Post, path, Calendars.ToGraph(e, TimeZoneInfo.Local.Id), token: token);
        return Str(created, "id");
    }

    /// <summary>L'evento su cui agire: l'occorrenza scelta, o per tutta la serie la serie (seriesMasterId).</summary>
    static string EventId(CalendarEvent e, bool series) => Segment(series && e.SeriesMasterId.Length > 0 ? e.SeriesMasterId : e.Id, escape: true);

    /// <summary>
    /// Modifica, come il nuovo Outlook: PATCH me/events/{id} dei soli campi cambiati (Calendars.Changes), nel fuso di Windows.
    /// Per tutta la serie il PATCH va alla serie, con l'orario spostato quanto quello dell'occorrenza (Calendars.SeriesTimes,
    /// dall'inizio della serie letto prima). Nessuna richiesta se non è cambiato nulla.
    /// </summary>
    public async Task UpdateEventAsync(CalendarEvent original, NewEvent changed, bool series)
    {
        var token = await CalendarTokenAsync() ?? throw new MailException(CalendarConsentNeeded);
        var zone = TimeZoneInfo.Local.Id;
        var patch = Calendars.Changes(original, changed, zone);
        if (patch.Count == 0)
        {
            Log.Write($"evento {original.Id}: nessuna modifica, nessuna richiesta");
            return;
        }
        series &= original.SeriesMasterId.Length > 0;
        if (series && patch.ContainsKey("start"))
        {
            if (changed.Start.Date != original.Start.Date || changed.AllDay != original.AllDay)
                throw new MailException(T("Tutta la serie si sposta solo di orario, nello stesso giorno: per cambiare i giorni della ripetizione usa Outlook sul web."));
            var master = await SendAsync(HttpMethod.Get, $"me/events/{EventId(original, true)}?$select=start,end,isAllDay", token: token, timeZone: zone);
            var (start, end) = Calendars.SeriesTimes(original, changed, Calendars.LocalTime(master?["start"]));
            Calendars.SetTimes(patch, start, end, changed.AllDay, zone);
        }
        await SendAsync(HttpMethod.Patch, $"me/events/{EventId(original, series)}", patch, token: token);
    }

    /// <summary>
    /// La disponibilità, come l'Assistente Pianificazione di Outlook: POST me/calendar/getSchedule con le persone,
    /// l'intervallo nel fuso di Windows e availabilityViewInterval 30 (Prefer outlook.timezone). Solo per gli account di
    /// lavoro o della scuola: con quelli personali Graph non la dà, e lo si dice senza richieste.
    /// </summary>
    public async Task<List<Availability>?> GetScheduleAsync(IReadOnlyList<string> emails, DateTime from, DateTime to)
    {
        if (Account.Tenant == null)
            throw new MailException(T("La disponibilità dei partecipanti c'è solo con gli account di lavoro o della scuola: Outlook.com non la dà ai programmi."));
        var token = await CalendarTokenAsync();
        if (token == null || emails.Count == 0) return null;
        var zone = TimeZoneInfo.Local.Id;
        static JsonObject Time(DateTime t, string z) => new() { ["dateTime"] = t.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), ["timeZone"] = z };
        var answer = await SendAsync(HttpMethod.Post, "me/calendar/getSchedule", new JsonObject
        {
            ["schedules"] = new JsonArray(emails.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray()),
            ["startTime"] = Time(from, zone),
            ["endTime"] = Time(to, zone),
            ["availabilityViewInterval"] = 30,
        }, token: token, timeZone: zone);
        return (answer?["value"]?.AsArray() ?? new JsonArray()).Select(s => new Availability(
            Str(s, "scheduleId"), Str(s, "availabilityView"), s?["error"] is { } error ? Str(error, "message") is { Length: > 0 } m ? m : T("non disponibile") : null)).ToList();
    }

    /// <summary>Elimina: DELETE me/events/{id}, dell'occorrenza o della serie.</summary>
    public async Task DeleteEventAsync(CalendarEvent e, bool series)
    {
        var token = await CalendarTokenAsync() ?? throw new MailException(CalendarConsentNeeded);
        await SendAsync(HttpMethod.Delete, $"me/events/{EventId(e, series)}", token: token);
    }

    /// <summary>Annulla riunione, come Outlook: POST me/events/{id}/cancel con la nota (Comment) che arriva ai partecipanti.</summary>
    public async Task CancelEventAsync(CalendarEvent e, string comment, bool series)
    {
        var token = await CalendarTokenAsync() ?? throw new MailException(CalendarConsentNeeded);
        await SendAsync(HttpMethod.Post, $"me/events/{EventId(e, series)}/cancel", new JsonObject { ["Comment"] = comment.Trim() }, token: token);
    }

    /// <summary>Accetta, Provvisorio o Rifiuta dai dettagli dell'evento: POST me/events/{id}/accept (tentativelyAccept, decline).</summary>
    public async Task RespondToEventAsync(CalendarEvent e, InviteAnswer answer, string comment, bool send)
    {
        var token = await CalendarTokenAsync() ?? throw new MailException(CalendarConsentNeeded);
        var action = answer switch { InviteAnswer.Accept => "accept", InviteAnswer.Decline => "decline", _ => "tentativelyAccept" };
        await SendAsync(HttpMethod.Post, $"me/events/{Segment(e.Id, escape: true)}/{action}",
            new JsonObject { ["comment"] = comment.Trim(), ["sendResponse"] = send }, token: token);
    }

    /// <summary>
    /// Un invito di Exchange (eventMessage). I campi del tipo derivato (orari, luogo, ricorrenza, tipo di messaggio) non si
    /// chiedono con $select: arrivano con una richiesta senza, fatta solo per gli inviti. Solo richieste e annullamenti.
    /// </summary>
    async Task<Invitation?> EventInviteAsync(MessageSummary message, string html, CancellationToken cancel)
    {
        var e = await GetAsync($"{MessageUrl(message.Id)}", cancel: cancel);
        var type = Str(e, "meetingMessageType");
        if (type is not ("meetingRequest" or "meetingCancelled")) return null;
        static DateTime When(JsonNode? t)
        {
            if (!DateTime.TryParse(Str(t, "dateTime"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time)) return DateTime.MinValue;
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(Str(t, "timeZone") is { Length: > 0 } tz ? tz : "UTC"); }
            catch (Exception) { zone = TimeZoneInfo.Utc; }
            return Calendars.FromZone(time, zone);
        }
        var start = When(e?["startDateTime"]);
        var location = Str(e?["location"], "displayName");
        return new Invitation
        {
            Method = type == "meetingCancelled" ? "CANCEL" : "REQUEST",
            Summary = Str(e, "subject"),
            Location = location.Length > 0 ? location : null,
            Start = start,
            End = When(e?["endDateTime"]),
            AllDay = e?["isAllDay"]?.GetValue<bool>() ?? false,
            Organizer = ParseAddress(e?["from"]),
            Recurrence = e?["recurrence"] is JsonObject r && RuleOf(r) is { } rule ? Invitation.RecurrenceText(rule, start) : null,
            OnlineUrl = Invitation.FindMeetingLink(html),
            GraphMessageId = message.Id,
            NeedsConsent = await CalendarTokenAsync() == null,
        };
    }

    /// <summary>La ricorrenza di Graph (patternedRecurrence) scritta come una RRULE di iCalendar, per dirla in parole.</summary>
    static string? RuleOf(JsonObject recurrence)
    {
        var pattern = recurrence["pattern"];
        var range = recurrence["range"];
        var kind = Str(pattern, "type");
        var freq = kind switch
        {
            "daily" => "DAILY",
            "weekly" => "WEEKLY",
            "absoluteMonthly" or "relativeMonthly" => "MONTHLY",
            "absoluteYearly" or "relativeYearly" => "YEARLY",
            _ => null,
        };
        if (freq == null) return null;
        var parts = new List<string> { "FREQ=" + freq, "INTERVAL=" + (pattern?["interval"]?.GetValue<int>() ?? 1) };
        var index = Str(pattern, "index") switch { "first" => "1", "second" => "2", "third" => "3", "fourth" => "4", "last" => "-1", _ => "" };
        var days = (pattern?["daysOfWeek"]?.AsArray() ?? new JsonArray()).Select(d => d?.GetValue<string>() ?? "").Where(d => d.Length >= 2)
            .Select(d => (kind == "relativeMonthly" ? index : "") + d[..2].ToUpperInvariant()).ToList();
        if (days.Count > 0 && kind != "absoluteMonthly") parts.Add("BYDAY=" + string.Join(",", days));
        if (kind == "absoluteMonthly" && pattern?["dayOfMonth"]?.GetValue<int>() is { } day) parts.Add("BYMONTHDAY=" + day);
        if (Str(range, "type") == "endDate" && Str(range, "endDate") is { Length: >= 10 } end) parts.Add("UNTIL=" + end.Replace("-", "")[..8]);
        if (Str(range, "type") == "numbered" && range?["numberOfOccurrences"]?.GetValue<int>() is { } count) parts.Add("COUNT=" + count);
        return string.Join(";", parts);
    }

    /// <summary>
    /// Gli inviti di Exchange si rispondono sull'evento del calendario, come Outlook: accept, tentativelyAccept (con un
    /// nuovo orario proposto) o decline, con la nota e con o senza risposta all'organizzatore; il calendario di Outlook
    /// si aggiorna. Serve il consenso al calendario. Gli inviti arrivati come .ics: risposta iMIP.
    /// </summary>
    public async Task RespondAsync(MessageSummary message, Invitation invite, InviteResponse response)
    {
        if (invite.GraphMessageId == null)
        {
            if (response.Send && invite.Organizer != null) await SendAsync(invite.ReplyMessage(Account, response));
            return;
        }
        var token = await CalendarTokenAsync() ?? throw new MailException(
            T("Per rispondere agli inviti di Outlook Missiva deve poter usare il tuo calendario: premi \"Consenti l'accesso al calendario\" nella scheda dell'invito."));
        var withEvent = await SendAsync(HttpMethod.Get, $"me/messages/{Segment(invite.GraphMessageId)}?$select=id&$expand=microsoft.graph.eventMessage/event($select=id)", token: token);
        var eventId = Str(withEvent?["event"], "id");
        if (eventId.Length == 0) throw new MailException(T("Outlook non trova l'evento dell'invito nel calendario: forse è stato annullato o spostato."));
        var action = response.Answer switch { InviteAnswer.Accept => "accept", InviteAnswer.Decline => "decline", _ => "tentativelyAccept" };
        var body = new JsonObject { ["comment"] = response.Comment.Trim(), ["sendResponse"] = response.Send || response.Answer == InviteAnswer.Propose };
        if (response.Answer == InviteAnswer.Propose && response.NewStart is { } s && response.NewEnd is { } e)
        {
            static JsonObject Time(DateTime local) => new() { ["dateTime"] = local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), ["timeZone"] = "UTC" };
            body["proposedNewTime"] = new JsonObject { ["start"] = Time(s), ["end"] = Time(e) };
        }
        await SendAsync(HttpMethod.Post, $"me/events/{Segment(eventId)}/{action}", body, token: token);
    }

    /// <summary>
    /// La bozza scritta come nell'invio, senza /send; poi, per la finestra di scrittura, com'è ora sul server. Se quella
    /// lettura non riesce la bozza è comunque scritta: la finestra riceve lo stesso il suo id (con gli allegati che restano
    /// e quelli appena aggiunti), altrimenti il salvataggio dopo ne creerebbe un'altra.
    /// </summary>
    public async Task<SavedDraft> SaveDraftAsync(OutgoingMessage mail)
    {
        var (id, quote, _, added, images) = await WriteDraftAsync(mail);
        JsonNode? saved = null;
        try
        {
            saved = await GetAsync($"{MessageUrl(id)}?$select={SummaryFields}{(expandAttachments ? AttachmentsExpand : "")}");
            if (!expandAttachments && saved is JsonObject withoutExpand)
                withoutExpand["attachments"] = (await GetAsync($"{MessageUrl(id)}/attachments?$select=id,name,contentType,size,isInline"))?["value"]?.DeepClone();
        }
        catch (Exception ex)
        {
            Log.Error("bozza salvata, lettura dal server", ex);
        }
        if (saved == null)
            return new SavedDraft
            {
                // Con l'indirizzo da cui è salvata: tornando a quello principale, il salvataggio dopo rimette from (WriteDraftAsync).
                Message = new MessageSummary
                {
                    Id = id, StableId = id, FolderId = mail.Draft?.Message.FolderId ?? "", Subject = mail.Subject, Date = DateTime.Now, IsRead = true, IsDraft = true,
                    From = mail.From is { } identity ? new Address { Name = identity.Name, Email = identity.Email } : new Address { Name = Account.DisplayName, Email = Account.Email },
                },
                Attachments = mail.KeptAttachments.Concat(added).ToList(),
                Images = images,
                QuoteHtml = quote,
            };
        return new SavedDraft
        {
            Message = Summaries(new JsonObject { ["value"] = new JsonArray(saved.DeepClone()) }, null)[0],
            Attachments = (saved["attachments"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>()
                .Where(a => !(a["isInline"]?.GetValue<bool>() ?? false) && !images.Any(i => i.Id == Str(a, "id"))) // le immagini del testo non sono allegati
                .Select(a => new Attachment { Id = Str(a, "id"), Name = Str(a, "name"), ContentType = Str(a, "contentType"), Size = a["size"]?.GetValue<long>() ?? 0 })
                .ToList(),
            Images = images,
            QuoteHtml = quote,
        };
    }

    /// <summary>
    /// Si passa sempre da una bozza, per inviare e per salvare. Nuova: per le risposte con createReply, che ci mette già
    /// l'originale citato (il testo nuovo va in cima al suo corpo), altrimenti POST me/messages. Se il messaggio continua
    /// una bozza, la stessa. Poi PATCH di oggetto, corpo e destinatari, gli allegati tolti si eliminano e i file nuovi si
    /// aggiungono, come le immagini nuove del testo. Restituisce l'identificativo, il corpo con la citazione, se la bozza
    /// l'ha creata questa chiamata, gli allegati aggiunti e le immagini del testo che la bozza ha ora.
    /// </summary>
    async Task<(string Id, string? Quote, bool Created, List<Attachment> Added, List<Attachment> Images)> WriteDraftAsync(OutgoingMessage mail)
    {
        var added = new List<Attachment>();
        var cancel = mail.Cancel;
        // I file piccoli si leggono subito (uno che non c'è più ferma tutto prima di toccare la bozza) e partono come sempre in
        // una richiesta; da LargeAttachment in su si caricano a pezzi dal disco (UploadLargeAsync), senza tenerli in memoria.
        var files = new List<(string Path, string Name, long Size, byte[]? Bytes)>();
        foreach (var path in mail.AttachmentPaths)
        {
            // Misura e contenuto dallo stesso file aperto: fra FileInfo.Length e la lettura un file piccolo poteva crescere
            // ed entrare intero in memoria, fuori dal conto dei 150 MB (audit dell'11 ottobre, T-24).
            await using var opened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            long size = opened.Length;
            byte[]? bytes = null;
            if (size < LargeAttachment)
            {
                bytes = new byte[size];
                await opened.ReadExactlyAsync(bytes, cancel);
            }
            files.Add((path, Path.GetFileName(path), size, bytes));
        }
        // Il limite di Outlook per un messaggio, detto prima di cominciare: allegati nuovi, quelli che restano e immagini del testo.
        long total = files.Sum(f => f.Size) + mail.KeptAttachments.Sum(a => a.Size) + mail.Images.Sum(i => (long)i.Content.Length);
        if (total > MaxAttachments)
            throw new MailException(T("Gli allegati pesano in tutto {0:0} MB: Outlook ne accetta al massimo {1} per messaggio. Togline qualcuno, oppure condividi i file più grandi con un link.",
                                      total / 1048576.0, MaxAttachments / 1048576));
        // Per riconoscere, dopo, l'allegato appena caricato a pezzi (ListedAttachmentsAsync): quelli che la bozza ha già.
        HashSet<string>? known = null;

        string id;
        string? quote = null;
        bool created = mail.Draft == null;
        if (mail.Draft is { } draft)
        {
            id = draft.Message.Id;
            quote = draft.QuoteHtml;
        }
        else
        {
            var action = mail.Mode switch
            {
                ComposeMode.Reply => "createReply",
                ComposeMode.ReplyAll => "createReplyAll",
                ComposeMode.Forward => "createForward",
                _ => null,
            };
            JsonNode? node;
            if (action != null && mail.Original != null)
            {
                node = await SendAsync(HttpMethod.Post, $"{MessageUrl(mail.Original.Id)}/{action}", new JsonObject(), htmlBody: true);
                quote = Str(node?["body"], "content");
            }
            else
            {
                node = await SendAsync(HttpMethod.Post, me + "messages", new JsonObject { ["subject"] = mail.Subject });
            }
            id = Str(node, "id");
        }

        var images = new List<Attachment>();
        try
        {
            var content = quote != null ? InsertIntoBody(quote, mail.Html) : mail.Html;
            var patch = new JsonObject
            {
                ["subject"] = mail.Subject,
                ["body"] = new JsonObject { ["contentType"] = "HTML", ["content"] = content },
                ["toRecipients"] = Recipients(mail.To),
                ["ccRecipients"] = Recipients(mail.Cc),
                ["bccRecipients"] = Recipients(mail.Bcc),
                ["importance"] = mail.Importance switch { Importance.High => "high", Importance.Low => "low", _ => "normal" },
                ["isReadReceiptRequested"] = mail.ReadReceipt,
                ["isDeliveryReceiptRequested"] = mail.DeliveryReceipt,
            };
            // Invio pianificato, come Outlook: PidTagDeferredSendTime. Exchange tiene il messaggio nella Posta in uscita e lo
            // manda all'ora, anche con Missiva chiusa; nessun permesso in più.
            // Da un altro indirizzo (alias di Outlook.com, casella con il permesso Invia come): from, e il suo Rispondi a.
            if (mail.From is { } identity)
            {
                patch["from"] = new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = identity.Email, ["name"] = identity.Name.Trim().Length > 0 ? identity.Name.Trim() : Account.DisplayName } };
                if (identity.ReplyTo.Trim().Length > 0) patch["replyTo"] = Recipients([new Address { Email = identity.ReplyTo.Trim() }]);
                else if (mail.Draft != null) patch["replyTo"] = new JsonArray();
            }
            else if (mail.Draft is { } saved)
            {
                // Una bozza già sul server salvata da un altro indirizzo dell'account (il salvataggio automatico dopo 10 s, o
                // una bozza ripresa): tornati all'indirizzo principale, from torna quello dell'account e Rispondi a si toglie,
                // altrimenti partirebbe dall'altro indirizzo. Solo in quel caso: l'indirizzo con cui si accede a Microsoft 365
                // può non essere quello della casella, e from resta quello che Exchange mette.
                if (Account.Identities.Any(i => i.Email.Equals(saved.Message.From.Email, StringComparison.OrdinalIgnoreCase)))
                {
                    var main = new JsonObject { ["address"] = Account.Email };
                    if (Account.DisplayName.Trim().Length > 0) main["name"] = Account.DisplayName.Trim();
                    patch["from"] = new JsonObject { ["emailAddress"] = main };
                }
                patch["replyTo"] = new JsonArray();
            }
            if (mail.SendAt is { } at)
                patch["singleValueExtendedProperties"] = new JsonArray(new JsonObject
                {
                    ["id"] = "SystemTime 0x3FEF",
                    ["value"] = UtcText(at.ToUniversalTime()),
                });
            await SendAsync(HttpMethod.Patch, $"{MessageUrl(id)}", patch);
            foreach (var a in mail.Draft?.Attachments ?? new())
                if (!mail.KeptAttachments.Any(k => k.Id == a.Id))
                    await RemoveAttachmentAsync(id, a.Id);
            foreach (var (path, name, size, bytes) in files)
            {
                cancel.ThrowIfCancellationRequested();
                if (bytes == null)
                {
                    known ??= (await ListedAttachmentsAsync(id, cancel)).Select(a => a.Id).ToHashSet();
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                    // La misura è di prima (serve al limite dei 150 MB e alla sessione di caricamento): se nel frattempo il file è
                    // cresciuto partirebbe troncato, con un Content-Range coerente, senza che nessuno se ne accorga (T-24). Da
                    // qui in poi è aperto senza lasciar scrivere agli altri programmi.
                    if (stream.Length != size) throw new MailException(T("{0} è cambiato mentre il messaggio veniva preparato: l'allegato non è stato aggiunto. Riprova quando il file è completo.", name));
                    added.Add(await UploadLargeAsync(id, name, null, size, stream, null, known, mail.Progress, cancel));
                    continue;
                }
                var file = await SendAsync(HttpMethod.Post, $"{MessageUrl(id)}/attachments", new JsonObject
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = name,
                    ["contentBytes"] = Convert.ToBase64String(bytes),
                }); // senza annullamento: una POST interrotta potrebbe lasciare sulla bozza un allegato di cui non si sa l'id
                added.Add(new Attachment { Id = Str(file, "id"), Name = name, Size = bytes.Length });
            }
            // Le immagini del testo, come nel nuovo Outlook: allegati incorporati (isInline) con il contentId che l'HTML
            // mostra da cid:. Quelle già sulla bozza restano, se il testo le mostra ancora; le altre si eliminano. Solo le
            // immagini che la finestra conosce (Draft.Images): quelle della citazione messe da createReply restano sempre.
            foreach (var old in mail.Draft?.Images ?? new())
            {
                if (content.Contains("cid:" + old.ContentId, StringComparison.OrdinalIgnoreCase)) images.Add(old);
                else await RemoveAttachmentAsync(id, old.Id);
            }
            foreach (var image in mail.Images.Where(i => !images.Any(a => a.ContentId == i.ContentId)))
            {
                cancel.ThrowIfCancellationRequested();
                if (image.Content.Length >= LargeAttachment)
                {
                    // Una foto grande nel testo: a pezzi anche lei, incorporata con il suo contentId.
                    known ??= (await ListedAttachmentsAsync(id, cancel)).Select(a => a.Id).ToHashSet();
                    images.Add(await UploadLargeAsync(id, image.Name, image.ContentType, image.Content.Length, new MemoryStream(image.Content), image.ContentId, known, mail.Progress, cancel));
                    continue;
                }
                var node = await SendAsync(HttpMethod.Post, $"{MessageUrl(id)}/attachments", new JsonObject
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = image.Name,
                    ["contentType"] = image.ContentType,
                    ["contentBytes"] = Convert.ToBase64String(image.Content),
                    ["isInline"] = true,
                    ["contentId"] = image.ContentId,
                });
                images.Add(new Attachment
                {
                    Id = Str(node, "id"), Name = image.Name, ContentType = image.ContentType, Size = image.Content.Length,
                    IsInline = true, ContentId = image.ContentId,
                });
            }
        }
        catch
        {
            if (created) await DeleteDraftAsync(id);
            else
            {
                // La bozza continuata resta, ma la finestra non saprà di quello che questa chiamata ha già caricato: il
                // salvataggio dopo lo caricherebbe di nuovo, e chi riceve troverebbe due volte lo stesso file o la stessa
                // immagine (stesso contentId). Si toglie.
                foreach (var a in added.Concat(images.Where(i => !mail.Draft!.Images.Any(d => d.Id == i.Id))))
                {
                    try { await RemoveAttachmentAsync(id, a.Id); }
                    catch (Exception ex) { Log.Error("allegato di un salvataggio non riuscito", ex); }
                }
                // Un allegato caricato a pezzi che Outlook ha già messo sulla bozza senza che qui se ne sia saputo l'id (la
                // lettura dopo l'ultimo pezzo non riuscita, o l'annullamento arrivato lì): fuori anche lui.
                if (known != null)
                {
                    try
                    {
                        foreach (var orphan in (await ListedAttachmentsAsync(id, default)).Where(a => !known.Contains(a.Id) && !images.Any(i => i.Id == a.Id) && !added.Any(i => i.Id == a.Id)))
                            await RemoveAttachmentAsync(id, orphan.Id);
                    }
                    catch (Exception ex) { Log.Error("allegati di un caricamento non riuscito", ex); }
                }
            }
            throw;
        }
        return (id, quote, created, added, images);
    }

    /// <summary>Da questa misura in su un allegato si carica a pezzi: in una richiesta sola Graph ne accetta meno di 3 MB.</summary>
    internal const int LargeAttachment = 3 * 1024 * 1024;
    /// <summary>
    /// Un pezzo: 4 volte 320 KiB (1,25 MB), multiplo di 320 KiB e sotto i 4 MB che Graph accetta. Piccolo perché anche su una
    /// linea lenta (ADSL in salita) stia nei 60 secondi di una richiesta.
    /// </summary>
    internal const int UploadChunk = 4 * 320 * 1024;
    /// <summary>Il limite di Outlook per gli allegati di un messaggio.</summary>
    public const long MaxAttachments = 150L * 1024 * 1024;

    /// <summary>Gli allegati che la bozza ha ora sul server (senza il contenuto), anche su più pagine.</summary>
    async Task<List<(string Id, string Name)>> ListedAttachmentsAsync(string message, CancellationToken cancel)
    {
        var result = new List<(string, string)>();
        string? url = $"{MessageUrl(message)}/attachments?$select=id,name,size,isInline";
        for (int page = 0; url != null && page < 20; page++)
        {
            var node = await GetAsync(url, cancel: cancel);
            foreach (var a in node?["value"]?.AsArray() ?? new JsonArray()) result.Add((Str(a, "id"), Str(a, "name")));
            url = node?["@odata.nextLink"]?.GetValue<string>();
        }
        return result;
    }

    /// <summary>
    /// Un allegato da 3 MB in su (audit, F-15), come lo vuole Graph: POST .../attachments/createUploadSession con nome e
    /// misura, poi i pezzi con PUT all'indirizzo restituito (Content-Range, senza il token: l'indirizzo ha il suo), uno alla
    /// volta dal file, senza tenerlo in memoria; all'ultimo Outlook risponde 201 e l'allegato è sulla bozza. Il suo id si
    /// legge dall'elenco degli allegati (quello che non c'era: known), non dall'indirizzo della risposta. Un errore o
    /// l'annullamento a metà lasciano la bozza senza l'allegato: la sessione si chiude (DELETE) e il resto lo toglie chi chiama.
    /// </summary>
    /// <param name="contentId">Per un'immagine del testo: incorporata, con questo cid.</param>
    async Task<Attachment> UploadLargeAsync(string message, string name, string? contentType, long size, Stream content, string? contentId, HashSet<string> known,
                                            IProgress<string>? progress, CancellationToken cancel)
    {
        static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.#", Lingua.Culture);
        string Stopped(long at) => T("Caricamento di {0} interrotto a {1} di {2} MB: l'allegato non è stato aggiunto.", name, Mb(at), Mb(size));
        var item = new JsonObject { ["attachmentType"] = "file", ["name"] = name, ["size"] = size };
        if (contentType != null) item["contentType"] = contentType;
        if (contentId != null)
        {
            item["isInline"] = true;
            item["contentId"] = contentId;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var session = await SendAsync(HttpMethod.Post, $"{MessageUrl(message)}/attachments/createUploadSession", new JsonObject { ["AttachmentItem"] = item }, cancel: cancel);
        if (!Uri.TryCreate(Str(session, "uploadUrl"), UriKind.Absolute, out var target) || target.Scheme != Uri.UriSchemeHttps)
            throw new MailException(T("Outlook non ha dato l'indirizzo a cui caricare {0}: l'allegato non è stato aggiunto.", name));
        bool complete = false;
        long sent = 0;
        int pieces = 0;
        try
        {
            var buffer = new byte[(int)Math.Min(UploadChunk, size)];
            while (sent < size)
            {
                int want = (int)Math.Min(buffer.Length, size - sent);
                int read = await content.ReadAtLeastAsync(buffer.AsMemory(0, want), want, throwOnEndOfStream: false, cancel);
                if (read < want) throw new MailException(T("{0} è cambiato mentre veniva caricato: l'allegato non è stato aggiunto.", name));
                // MainWindow.UploadShort riconosce questo testo e lo riscrive per la barra degli invii e la riga di stato della
                // scrittura: in italiano "Caricamento di nome: 3,5 di 12 MB…", in inglese esattamente "Uploading nome: 3.5 of 12 MB…".
                progress?.Report(T("Caricamento di {0}: {1} di {2} MB…", name, Mb(sent), Mb(size)));
                bool resent = false;
                for (int attempt = 0; ; attempt++)
                {
                    Network.Check(); // --senza-rete (solo Debug)
                    // I pezzi restano in HTTP/1.1, com'erano (non Network.ApiRequest): l'indirizzo è di un altro server, e in
                    // HTTP/2 un carico grande dipende dalla finestra che il server concede.
                    using var request = new HttpRequestMessage(HttpMethod.Put, target) { Content = new ByteArrayContent(buffer, 0, read) };
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    request.Content.Headers.ContentRange = new ContentRangeHeaderValue(sent, sent + read - 1, size);
                    HttpResponseMessage response;
                    int sentOn = Volatile.Read(ref generation);
                    try
                    {
                        using var slot = await gate.EnterAsync(opening: false, cancel, lengthy: true);
                        response = await Uploads.SendAsync(request, cancel);
                    }
                    // Le connessioni rifatte mentre il pezzo partiva (ResetConnections): lo stesso pezzo di nuovo, una volta.
                    catch (Exception ex) when (CutByReset(ex, sentOn, cancel) && !resent)
                    {
                        resent = true;
                        Log.Write("Graph: pezzo dell'allegato ripetuto su una connessione nuova (connessioni rifatte)");
                        continue;
                    }
                    catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                    {
                        throw new MailException(Stopped(sent) + " " + T("La connessione è troppo lenta o si è interrotta: un pezzo non è arrivato in 60 secondi."));
                    }
                    using (response)
                    {
                        // Come per le altre richieste: 429 e 503 si ripetono dopo l'attesa chiesta, con lo stesso pezzo.
                        if (attempt < 2 && response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                        {
                            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(400 * (attempt + 1));
                            await Task.Delay(wait < MaxRetryAfter ? wait : MaxRetryAfter, cancel);
                            continue;
                        }
                        if (!response.IsSuccessStatusCode)
                        {
                            var text = await response.Content.ReadAsStringAsync(cancel);
                            try { Result(response, text); }
                            catch (Exception ex) when (ex is MailException or NotFoundException)
                            {
                                throw new MailException(Stopped(sent) + " " + ex.Message, status: (int)response.StatusCode) { Code = (ex as MailException)?.Code ?? "" };
                            }
                        }
                        complete = response.StatusCode == HttpStatusCode.Created;
                    }
                    break;
                }
                sent += read;
                pieces++;
            }
            if (!complete) throw new MailException(T("Outlook non ha confermato {0} dopo l'ultimo pezzo: l'allegato non è stato aggiunto.", name));
        }
        catch
        {
            // La sessione lasciata a metà si chiude, senza aspettare troppo: scadrebbe comunque da sola.
            if (!complete)
            {
                try
                {
                    using var abandon = new HttpRequestMessage(HttpMethod.Delete, target);
                    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var closed = await Uploads.SendAsync(abandon, limit.Token);
                }
                catch (Exception ex) { Log.Write($"caricamento a pezzi: sessione non chiusa ({ex.GetType().Name})"); }
            }
            throw;
        }
        Log.Time($"Graph, allegato a pezzi ({size / 1024} KB in {pieces} pezzi)", clock);
        // Il nome del server a cui Outlook fa caricare i pezzi, per poterlo un giorno limitare a quelli noti (T-23).
        Log.Write($"allegato a pezzi caricato su {target.IdnHost}");
        var listed = await ListedAttachmentsAsync(message, cancel);
        var created = listed.Where(a => !known.Contains(a.Id)).ToList();
        var found = created.LastOrDefault(a => a.Name == name).Id ?? created.LastOrDefault().Id
                    ?? throw new MailException(T("Outlook non mostra {0} fra gli allegati della bozza dopo il caricamento: riprova.", name));
        known.Add(found);
        return new Attachment { Id = found, Name = name, ContentType = contentType ?? "", Size = size, IsInline = contentId != null, ContentId = contentId };
    }

    /// <summary>
    /// Un allegato (o un'immagine del testo) esce dalla bozza. Se non c'è già più va bene lo stesso: l'ha tolto un
    /// salvataggio non riuscito a metà, dopo il quale la finestra lo crede ancora sulla bozza.
    /// </summary>
    async Task RemoveAttachmentAsync(string message, string attachment)
    {
        try { await SendAsync(HttpMethod.Delete, $"{MessageUrl(message)}/attachments/{Segment(attachment)}"); }
        catch (NotFoundException) { }
    }

    /// <summary>
    /// Solo per la bozza creata da un invio o un salvataggio non riusciti: senza, ogni nuovo tentativo ne lasciava
    /// un'altra in Bozze. Se invece /send era arrivato, il messaggio inviato ha un altro id e qui si riceve solo un 404.
    /// Quella che il messaggio continuava resta nelle Bozze.
    /// </summary>
    async Task DeleteDraftAsync(string id)
    {
        try { await SendAsync(HttpMethod.Delete, $"{MessageUrl(id)}"); }
        catch (NotFoundException) { }
        catch (Exception ex) { Log.Error("bozza del messaggio non inviato", ex); }
    }

    /// <summary>La bozza scartata si elimina; se non c'è già più (eliminata altrove), niente.</summary>
    public async Task DiscardDraftAsync(SavedDraft draft)
    {
        try { await SendAsync(HttpMethod.Delete, $"{MessageUrl(draft.Message.Id)}"); }
        catch (NotFoundException) { }
    }

    /// <summary>Il testo nuovo va in cima al corpo della bozza, subito dopo il tag body.</summary>
    static string InsertIntoBody(string draftHtml, string newHtml)
    {
        var body = draftHtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var end = body < 0 ? -1 : draftHtml.IndexOf('>', body);
        return end < 0 ? newHtml + draftHtml : draftHtml[..(end + 1)] + newHtml + draftHtml[(end + 1)..];
    }

    // ---------------------------------------------------------------- utilità

    /// <summary>Le immagini incorporate che il testo mostra: src="cid:..." e url(cid:...).</summary>
    [GeneratedRegex(@"cid:<?([^""'\s()<>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CidReference();

    static string Str(JsonNode? node, string property)
    {
        try { return node?[property]?.GetValue<string>() ?? ""; }
        catch { return ""; }
    }

    static Address ParseAddress(JsonNode? recipient) => new()
    {
        Name = Str(recipient?["emailAddress"], "name"),
        Email = Str(recipient?["emailAddress"], "address"),
    };

    static List<Address> Addresses(JsonNode? list) =>
        (list?.AsArray() ?? new JsonArray()).Select(ParseAddress).ToList();

    static JsonArray Recipients(List<Address> list) =>
        new(list.Select(a => (JsonNode)new JsonObject
        {
            ["emailAddress"] = new JsonObject { ["address"] = a.Email, ["name"] = a.Name },
        }).ToArray());
}
