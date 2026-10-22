using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Missiva;

/// <summary>Com'è andata la revoca presso Google alla rimozione di un account (GoogleAuth.SignOutAsync).</summary>
public enum GoogleRevocation
{
    /// <summary>Google ha risposto che l'autorizzazione è revocata.</summary>
    Revoked,
    /// <summary>Niente da revocare e niente da dire: nessun accesso salvato, la versione di prova (che non chiede), o Google dice che l'autorizzazione non valeva già più.</summary>
    NotNeeded,
    /// <summary>Chiesta e non riuscita (rete assente, un'altra risposta): presso Google può restare valida, e si toglie a mano.</summary>
    Failed,
}

/// <summary>
/// Accesso agli account Google come nel nuovo Outlook: si apre il browser, scegli l'account e autorizzi Missiva.
/// È il flusso OAuth per app desktop, con ritorno su 127.0.0.1 e PKCE. Serve un client OAuth di tipo "App desktop"
/// creato su Google Cloud, inserito una volta sola in Missiva. Il token di aggiornamento resta cifrato con DPAPI.
/// </summary>
public static class GoogleAuth
{
    const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    const string TokenUrl = "https://oauth2.googleapis.com/token";
    const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    /// <summary>Posta completa via IMAP e SMTP; openid, email e profile servono a sapere quale account è.</summary>
    const string MailScope = "https://mail.google.com/";
    const string Scopes = MailScope + " openid email profile";
    /// <summary>
    /// Il calendario (leggere e scrivere eventi, rispondere agli inviti): si chiede solo dal pulsante "Consenti l'accesso al
    /// calendario" (AllowCalendarAsync di GmailProvider), mai all'aggiunta dell'account, come i permessi in più di Microsoft.
    /// L'ambito va aggiunto anche nella schermata di consenso del progetto Google Cloud.
    /// </summary>
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar";
    /// <summary>
    /// I contatti di Google, solo in lettura (People API): come il calendario, solo dal pulsante "Consenti l'accesso ai contatti".
    /// Scrivere servirebbe l'ambito .../auth/contacts, che sul progetto Google Cloud non c'è.
    /// </summary>
    public const string ContactsScope = "https://www.googleapis.com/auth/contacts.readonly";
    /// <summary>
    /// I contatti di Google in lettura e scrittura (People API: createContact, updateContact, deleteContact): chiesto solo dal pulsante
    /// "Consenti la modifica dei contatti" (AllowContactsWriteAsync), mai da solo; sostituisce contacts.readonly e tiene gli altri
    /// ambiti dell'account. Va aggiunto anche nella schermata di consenso del progetto Google Cloud.
    /// </summary>
    public const string ContactsWriteScope = "https://www.googleapis.com/auth/contacts";

    /// <summary>Se Google ha dato a Missiva questo permesso per l'account (l'ultimo accesso).</summary>
    public static bool Has(MailAccount account, string scope) =>
        account.GoogleScopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope) == true;

    /// <summary>Se Google ha dato a Missiva il permesso del calendario di questo account (l'ultimo accesso).</summary>
    public static bool HasCalendar(MailAccount account) => Has(account, CalendarScope);

    /// <summary>Se Google ha dato a Missiva il permesso di leggere i contatti di questo account (anche chi può scriverli li legge).</summary>
    public static bool HasContacts(MailAccount account) => Has(account, ContactsScope) || Has(account, ContactsWriteScope);

    /// <summary>Se Google ha dato a Missiva il permesso di creare, cambiare ed eliminare i contatti di questo account.</summary>
    public static bool HasContactsWrite(MailAccount account) => Has(account, ContactsWriteScope);

    /// <summary>
    /// I permessi in più che l'account ha già (calendario, contatti): un nuovo accesso li chiede di nuovo insieme a quelli
    /// nuovi, perché il token nuovo sostituisce il vecchio e altrimenti li perderebbe.
    /// </summary>
    public static List<string> ExtraScopesOf(MailAccount account)
    {
        var scopes = new List<string>();
        if (Has(account, CalendarScope)) scopes.Add(CalendarScope);
        if (Has(account, ContactsWriteScope)) scopes.Add(ContactsWriteScope); // scrivere comprende leggere: contacts.readonly non serve più
        else if (Has(account, ContactsScope)) scopes.Add(ContactsScope);
        return scopes;
    }

    /// <summary>Un nuovo accesso ha tolto all'account un permesso che aveva (calendario, contatti): l'account e l'avviso da mostrare. Da qualunque thread.</summary>
    public static event Action<MailAccount, string>? PermissionsLost;

    /// <summary>I permessi in più che c'erano prima e che il nuovo accesso non ha dato, in parole; vuoto se ci sono tutti.</summary>
    internal static List<string> Lost(string? before, string after)
    {
        static HashSet<string> Set(string? scopes) => (scopes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        HashSet<string> was = Set(before), now = Set(after);
        var lost = new List<string>();
        if (was.Contains(CalendarScope) && !now.Contains(CalendarScope)) lost.Add(T("il calendario"));
        bool read = now.Contains(ContactsScope) || now.Contains(ContactsWriteScope);
        if ((was.Contains(ContactsScope) || was.Contains(ContactsWriteScope)) && !read) lost.Add(T("i contatti"));
        else if (was.Contains(ContactsWriteScope) && !now.Contains(ContactsWriteScope)) lost.Add(T("la modifica dei contatti (restano in sola lettura)"));
        return lost;
    }

    /// <summary>
    /// Dopo un nuovo accesso (un permesso in più, Accedi di nuovo, l'account aggiunto un'altra volta) i permessi dell'account
    /// sono quelli che Google ha dato in quel momento, perché il token nuovo sostituisce il vecchio. Prima, se nella pagina di
    /// Google si toglieva una casella, calendario o contatti sparivano senza una parola (audit, P-03 e P-06). Ora il permesso
    /// che manca rispetto a prima si dice (PermissionsLost), con come riaverlo. Restituisce l'avviso, o null.
    /// </summary>
    public static string? SetScopes(MailAccount account, string granted)
    {
        var lost = Lost(account.GoogleScopes, granted);
        account.GoogleScopes = granted;
        if (lost.Count == 0) return null;
        // Al più due permessi (il calendario, e i contatti o la loro modifica): "il calendario e i contatti".
        var what = lost.Count == 1 ? lost[0] : T("{0} e {1}", lost[0], lost[1]);
        var notice = T("Con il nuovo accesso Google non ha più dato a Missiva il permesso per {0} di {1}: nella pagina di Google la casella non era spuntata, oppure l'account è stato aggiunto di nuovo senza chiederlo. La posta funziona come prima.\n\nPer riaverlo premi di nuovo \"Consenti l'accesso\" nel calendario o in Persone, e nella pagina di Google lascia spuntate tutte le caselle.",
                       what, account.Email);
        Log.Write($"nuovo accesso di {account.Email}: Google non ha più dato {string.Join(" e ", lost)}");
        try { PermissionsLost?.Invoke(account, notice); } catch (Exception ex) { Log.Error("avviso dei permessi di Google tolti", ex); }
        return notice;
    }

    /// <summary>
    /// Un nuovo accesso riuscito, messo nell'account: il token di aggiornamento cifrato per questo PC, i permessi che
    /// Google ha dato adesso (SetScopes, che dice se ne manca uno di prima) e il token di accesso di prima dimenticato.
    /// Erano tre righe ripetute nei tre punti da cui si rifà l'accesso (un permesso in più, Gestisci account, l'account
    /// aggiunto di nuovo): un quarto che dimentica SetScopes non direbbe più un permesso tolto (audit dell'11 ottobre,
    /// C-14). Chi chiama salva le impostazioni.
    /// </summary>
    public static void Apply(MailAccount account, SignInResult result)
    {
        account.ProtectedRefreshToken = Store.Protect(result.RefreshToken);
        SetScopes(account, result.Scope);
        Forget(account);
    }

#if DEBUG
    /// <summary>Prove (--prova-bozze): un token di accesso finto, al posto del rinnovo da Google.</summary>
    internal static string? TestToken;
#endif

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(MailLimits.GoogleHttpSeconds) };
#if DEBUG
    /// <summary>Prove: le richieste del token a un gestore finto (una rete che non risponde, contando le richieste).</summary>
    internal static HttpClient? TestHttp;
    /// <summary>Prove: l'errore di rete ricordato non vale per le prove dopo.</summary>
    internal static void TestForgetFailure() => networkFailure = null;
#endif
    /// <summary>
    /// L'ultima richiesta del token fallita per la rete (nessuna risposta, tempo scaduto), con quando: per FailureMemory chi
    /// era in fila dietro di lei riceve lo stesso errore subito. Ogni account Gmail ha quattro o cinque connessioni e ognuna
    /// chiede il token passando da Gate: con una rete che non cade ma non risponde (portale, VPN che sta salendo) i tempi
    /// scaduti da 30 secondi si mettevano in fila uno dopo l'altro (audit del 7 ottobre, A-19). Si legge e si scrive con Gate preso.
    /// </summary>
    static (DateTime At, Exception Error)? networkFailure;
    internal static TimeSpan FailureMemory = TimeSpan.FromSeconds(10);
    /// <summary>Token di accesso per account, con la scadenza e l'impronta del token di aggiornamento da cui vengono (Grant).</summary>
    static readonly ConcurrentDictionary<string, (string Token, DateTime Expires, string Grant)> Tokens = new();
    static readonly SemaphoreSlim Gate = new(1, 1);

    // ---------------------------------------------------------------- token di accesso fra un avvio e l'altro

    /// <summary>
    /// I token di accesso (validi un'ora) restano fra un avvio e l'altro, cifrati con DPAPI come quello di aggiornamento
    /// (tutto il file, solo per questo utente di Windows): al secondo avvio entro l'ora Gmail si collega senza chiedere
    /// nulla a Google. Mai in impostazioni.json.
    /// </summary>
    static readonly string TokenFile = Path.Combine(Store.Dir, "token-google.dat");
    static bool loaded;
    /// <summary>Account di cui il registro ha già detto se il token è riusato o nuovo.</summary>
    static readonly HashSet<string> reused = new();

    /// <summary>
    /// L'impronta del token di aggiornamento cifrato: cambia a ogni nuovo accesso (Gestisci account, Accedi di nuovo), e con
    /// lei il token di accesso salvato non vale più.
    /// </summary>
    static string GrantOf(MailAccount account) =>
        account.ProtectedRefreshToken == null ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.ProtectedRefreshToken)))[..16];

    /// <summary>Una volta per avvio, sotto Gate: i token ancora validi salvati dall'avvio di prima.</summary>
    static void LoadSaved()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            if (!File.Exists(TokenFile)) return;
            var text = Encoding.UTF8.GetString(SafeFile.Unprotect(File.ReadAllBytes(TokenFile)));
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Split('\t') is not [var id, var ticks, var grant, var token] || !long.TryParse(ticks, out var t)) continue;
                var expires = new DateTime(t, DateTimeKind.Utc);
                if (expires <= DateTime.UtcNow) continue;
                var account = Store.Settings.Accounts.FirstOrDefault(a => a.Id == id);
                if (account == null || GrantOf(account) != grant) continue; // account tolto o nuovo accesso
                Tokens.TryAdd(id, (token, expires, grant));
            }
        }
        catch (Exception ex)
        {
            Log.Error("token di Google salvati", ex); // un file rovinato o di un altro utente: si rinnovano
        }
    }

    static readonly object FileGate = new();

    /// <summary>I token ancora validi, cifrati (anche da Forget, che non passa da Gate: la chiama il provider fuori dal token).</summary>
    static void SaveTokens()
    {
        try
        {
            lock (FileGate)
            {
                var lines = Tokens.Where(t => t.Value.Expires > DateTime.UtcNow).Select(t => $"{t.Key}\t{t.Value.Expires.Ticks}\t{t.Value.Grant}\t{t.Value.Token}\n");
                var bytes = SafeFile.Protect(Encoding.UTF8.GetBytes(string.Concat(lines)));
                Directory.CreateDirectory(Store.Dir);
                SafeFile.WriteCopy(TokenFile, bytes);
            }
        }
        catch (Exception ex)
        {
            Log.Error("salvataggio dei token di Google", ex);
        }
    }

    /// <param name="Scope">I permessi che Google ha dato davvero (nella pagina di consenso se ne possono togliere).</param>
    public sealed record SignInResult(string Email, string Name, string RefreshToken, string AccessToken, string Scope = "");

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Store.Settings.GoogleClientId) && !string.IsNullOrWhiteSpace(Store.Settings.ProtectedGoogleClientSecret);

    static (string Id, string Secret) Client() => !IsConfigured
        ? throw new MailException(T("Manca l'app Google di Missiva: inseriscila aggiungendo un account Gmail."))
        : Store.TryUnprotect(Store.Settings.ProtectedGoogleClientSecret, out var secret) ? (Store.Settings.GoogleClientId!, secret)
        : throw new MailException(T("Il segreto dell'app Google di Missiva salvato su questo PC non si legge più (profilo di Windows cambiato, o impostazioni copiate da un altro PC): inseriscilo di nuovo da Aggiungi account, Gmail, App Google."));

    /// <summary>
    /// Nuovo accesso: apre il browser e aspetta che l'utente autorizzi Missiva. Per Google Workspace (loginHint, l'indirizzo
    /// scritto nella finestra di aggiunta) Google propone subito quell'account; Gmail resta com'era. Con extraScopes chiede
    /// anche i permessi in più (calendario, contatti), quelli nuovi e quelli che l'account ha già (ExtraScopesOf): il nuovo
    /// token sostituisce il vecchio.
    /// </summary>
    public static async Task<SignInResult> SignInAsync(CancellationToken cancel, string? loginHint = null, IEnumerable<string>? extraScopes = null)
    {
        var (clientId, secret) = Client();
        var listener = new TcpListener(IPAddress.Loopback, 0); // porta libera scelta da Windows
        listener.Start();
        try
        {
            var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
            var query = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirect,
                ["response_type"] = "code",
                ["scope"] = string.Join(" ", new[] { Scopes }.Concat(extraScopes ?? []).Distinct()),
                ["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["access_type"] = "offline",
                ["prompt"] = "select_account consent", // sempre la scelta dell'account, e ogni volta un token di aggiornamento
            };
            if (loginHint != null) query["login_hint"] = loginHint;
            var url = AuthUrl + "?" + string.Join("&", query.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            var reply = await WaitForReplyAsync(listener, state, cancel);
            if (reply.TryGetValue("error", out var error)) throw new MailException(Refused(error, clientId));

            var json = await PostTokenAsync(new()
            {
                ["code"] = reply.GetValueOrDefault("code", ""),
                ["client_id"] = clientId,
                ["client_secret"] = secret,
                ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = verifier,
            }, cancel);

            // Nella pagina di consenso si possono togliere i singoli permessi: senza quello della posta non si va avanti.
            if (!(json.GetProperty("scope").GetString() ?? "").Split(' ').Contains(MailScope))
                throw new MailException(T("Non hai dato a Missiva il permesso di usare Gmail. Riprova e lascia spuntata la casella della posta."));
            var refresh = json.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            if (string.IsNullOrEmpty(refresh)) throw new MailException(T("Google non ha restituito il token di aggiornamento. Riprova."));

            // L'id_token arriva direttamente da Google su HTTPS: per leggerne indirizzo e nome non serve verificarne la firma.
            var payload = json.GetProperty("id_token").GetString()!.Split('.')[1];
            var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(payload)).RootElement;
            var email = claims.GetProperty("email").GetString()!;
            var name = claims.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } s ? s : email;
            return new SignInResult(email, name, refresh, json.GetProperty("access_token").GetString()!, json.GetProperty("scope").GetString() ?? "");
        }
        catch (TokenError ex)
        {
            throw new MailException(ex.Code == "invalid_client"
                ? T("Google non riconosce ID client e client secret dell'app: ricontrollali.")
                : T("Accesso non riuscito: {0}", ex.Message), ex);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Token di accesso per IMAP e SMTP. Dura un'ora: si rinnova da sé con il token di aggiornamento.</summary>
    public static async Task<string> TokenAsync(MailAccount account)
    {
#if DEBUG
        if (TestToken != null) return TestToken;
#endif
        await Gate.WaitAsync();
        try
        {
            LoadSaved();
            if (Tokens.TryGetValue(account.Id, out var cached) && cached.Expires > DateTime.UtcNow && cached.Grant == GrantOf(account))
            {
                if (reused.Add(account.Id)) Log.Write($"token di Gmail riusato per {account.Email} (salvato cifrato, vale fino alle {cached.Expires.ToLocalTime():HH:mm})");
                return cached.Token;
            }
            // Senza accesso salvato: un account arrivato con una copia di sicurezza, che non porta gli accessi (Backup).
            if (account.ProtectedRefreshToken == null)
                throw new MailException(T("Su questo PC manca l'accesso a Google di {0}: Accedi di nuovo (Impostazioni, Account, Gestisci).", account.Email)) { Fix = AccountFix.SignIn };
            var (clientId, secret) = Client();
            // L'accesso salvato che non si decifra più: Accedi di nuovo, non l'errore di Windows (audit, F-44 punto 4).
            if (!Store.TryUnprotect(account.ProtectedRefreshToken, out var refresh))
                throw new MailException(T("L'accesso a Google di {0} salvato su questo PC non si legge più (profilo di Windows cambiato, o impostazioni copiate da un altro PC): Accedi di nuovo (Impostazioni, Account, Gestisci).",
                                          account.Email)) { Fix = AccountFix.SignIn };
            // La rete non rispondeva un attimo fa a chi era davanti in fila: lo stesso errore, senza un altro tempo scaduto.
            if (networkFailure is { } failed && DateTime.UtcNow - failed.At < FailureMemory)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failed.Error);
            var clock = Stopwatch.StartNew();
            JsonElement json;
            try
            {
                json = await PostTokenAsync(new()
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = secret,
                    ["refresh_token"] = refresh,
                    ["grant_type"] = "refresh_token",
                }, CancellationToken.None);
                networkFailure = null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            {
                networkFailure = (DateTime.UtcNow, ex);
                throw;
            }
            var token = json.GetProperty("access_token").GetString()!;
            Tokens[account.Id] = (token, DateTime.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32() - 120), GrantOf(account));
            reused.Add(account.Id);
            Log.Write($"token di Gmail rinnovato per {account.Email} da oauth2.googleapis.com in {clock.ElapsedMilliseconds} ms");
            SaveTokens();
            return token;
        }
        catch (TokenError ex) when (ex.Code == "invalid_grant")
        {
            // Accesso revocato dall'account Google, password cambiata, o app ancora "In test" (scade dopo 7 giorni).
            throw new MailException(SignInAgain(account), ex) { Fix = AccountFix.SignIn };
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Il rifiuto di Google tornato al browser, nella lingua di Missiva. Con Google Workspace l'amministratore può bloccare le app non
    /// rese attendibili (admin_policy_enforced) o lasciare solo quelle della propria organizzazione (org_internal).
    /// </summary>
    public static string Refused(string error, string clientId) => error switch
    {
        "access_denied" => T("Accesso annullato nel browser."),
        "admin_policy_enforced" => AdminBlocked(clientId),
        "org_internal" => T("L'app Google di Missiva è riservata agli utenti dell'organizzazione che l'ha creata su Google Cloud: con un altro account serve un'app di tipo Esterno."),
        _ => T("Google ha rifiutato l'accesso: {0}", error),
    };

    /// <summary>L'amministratore di Google Workspace non ha reso attendibile Missiva: cosa deve fare.</summary>
    public static string AdminBlocked(string clientId) =>
        T("L'amministratore di Google Workspace della tua organizzazione non ha reso attendibile Missiva. Nella Console di amministrazione (Sicurezza, Accesso e controllo dei dati, Controlli API) può consentire l'app con l'ID client {0}; poi riprova.", clientId);

    static string SignInAgain(MailAccount account) =>
        T("Google chiede di rifare l'accesso a {0}: Accedi di nuovo (Impostazioni, Account, Gestisci).", account.Email);

#if DEBUG
    /// <summary>
    /// Prove (--prova-bozze): un token finto salvato come quelli veri, poi riletto come al prossimo avvio. Restituisce se il
    /// file è cifrato (il token in chiaro non c'è), se al "prossimo avvio" torna, e se non torna più dopo un nuovo accesso.
    /// </summary>
    internal static (bool Encrypted, bool Reused, bool DroppedAfterSignIn) TestSavedToken(MailAccount account, string token)
    {
        Store.Settings.Accounts.Add(account);
        try
        {
            Tokens[account.Id] = (token, DateTime.UtcNow.AddMinutes(50), GrantOf(account));
            SaveTokens();
            bool encrypted = !Encoding.UTF8.GetString(File.ReadAllBytes(TokenFile)).Contains(token) &&
                             !Encoding.Latin1.GetString(File.ReadAllBytes(TokenFile)).Contains("ya29.");
            Tokens.Clear();
            loaded = false;
            LoadSaved();
            bool reused = Tokens.TryGetValue(account.Id, out var again) && again.Token == token;
            account.ProtectedRefreshToken = Store.Protect("nuovo accesso");
            Tokens.Clear();
            loaded = false;
            LoadSaved();
            bool dropped = !Tokens.ContainsKey(account.Id);
            return (encrypted, reused, dropped);
        }
        finally
        {
            Store.Settings.Accounts.Remove(account);
            Tokens.Clear();
            SaveTokens();
        }
    }

    /// <summary>Prove: un token di accesso finto in memoria per l'account, come dopo una richiesta riuscita.</summary>
    internal static void TestRemember(MailAccount account, string token) => Tokens[account.Id] = (token, DateTime.UtcNow.AddMinutes(50), GrantOf(account));

    /// <summary>Prove: se il token di accesso dell'account è ancora in memoria.</summary>
    internal static bool TestRemembers(MailAccount account) => Tokens.ContainsKey(account.Id);
#endif

    /// <summary>Dimentica il token di accesso, in memoria e salvato (per esempio se Gmail lo ha rifiutato).</summary>
    public static void Forget(MailAccount account)
    {
        if (Tokens.TryRemove(account.Id, out _)) SaveTokens();
    }

    /// <summary>
    /// Alla rimozione dell'account: Google revoca l'autorizzazione data a Missiva. La risposta si guarda: se la revoca non
    /// riesce (rete assente, risposta diversa da 200) l'autorizzazione resta valida presso Google, e prima nessuno lo
    /// sapeva (audit dell'11 ottobre, C-32). Una riga nel registro, mai con il token, e dalle scelte del 12 ottobre 2026
    /// l'esito torna a chi rimuove l'account, che se la revoca non è riuscita lo dice a schermo (RevocationNotice).
    /// </summary>
    public static async Task<GoogleRevocation> SignOutAsync(MailAccount account)
    {
        Forget(account);
        if (account.ProtectedRefreshToken == null) return GoogleRevocation.NotNeeded;
#if DEBUG || PROVA
        // La versione di prova lavora su una copia dei dati veri: una revoca toglierebbe l'accesso anche alla Missiva
        // installata, che usa la stessa autorizzazione. Solo il registro, e nessun avviso.
        if (Environment.GetEnvironmentVariable("MISSIVA_NO_READ") == "1")
        {
            Log.Write($"prova: chiederei a Google di revocare l'accesso di {account.Email}");
            return GoogleRevocation.NotNeeded;
        }
#endif
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = Store.Unprotect(account.ProtectedRefreshToken) });
#if DEBUG
            using var response = await (TestHttp ?? Http).PostAsync(RevokeUrl, form);
#else
            using var response = await Http.PostAsync(RevokeUrl, form);
#endif
            if (response.IsSuccessStatusCode)
            {
                Log.Write($"accesso a Google di {account.Email} revocato");
                return GoogleRevocation.Revoked;
            }
            // Google risponde 400 con "invalid_token" per un'autorizzazione già scaduta o già revocata: allora non c'è altro
            // da fare, né da dire. Ogni altra risposta è una revoca non riuscita.
            if (response.StatusCode == HttpStatusCode.BadRequest && await AlreadyInvalidAsync(response))
            {
                Log.Write($"accesso a Google di {account.Email} non revocato: Google ha risposto 400, l'autorizzazione non era più valida (già scaduta o già revocata)");
                return GoogleRevocation.NotNeeded;
            }
            Log.Write($"accesso a Google di {account.Email} non revocato: Google ha risposto {(int)response.StatusCode}. Se era ancora valido, lo resta finché non si toglie dall'account Google");
        }
        catch (Exception ex)
        {
            Log.Write($"accesso a Google di {account.Email} non revocato ({ex.GetType().Name}: {ex.Message}): presso Google resta valido finché non si toglie dall'account Google");
        }
        return GoogleRevocation.Failed;
    }

    /// <summary>La risposta 400 di Google alla revoca dice "invalid_token": il token non valeva già più. Una risposta che non si legge non lo dice.</summary>
    static async Task<bool> AlreadyInvalidAsync(HttpResponseMessage response)
    {
        try
        {
            using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return parsed.RootElement.ValueKind == JsonValueKind.Object && parsed.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.String && error.GetString() == "invalid_token";
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            return false;
        }
    }

    /// <summary>Dove si tolgono a mano i permessi dati a un'app: la pagina dell'account Google con le app e i servizi di terze parti collegati.</summary>
    internal const string PermissionsPage = "myaccount.google.com/permissions";

    /// <summary>
    /// Che cosa dire a schermo dopo aver rimosso un account Google, o null se non c'è nulla da dire (scelte del 12 ottobre
    /// 2026, C-32): solo quando la revoca è stata chiesta e non è riuscita. Prima finiva solo nel registro, e
    /// l'autorizzazione restava valida presso Google senza che chi aveva rimosso l'account lo sapesse. Nella versione di
    /// prova la revoca non parte (SignOutAsync), quindi niente avviso.
    /// </summary>
    public static (string Heading, string Body)? RevocationNotice(MailAccount account, GoogleRevocation outcome) =>
        outcome != GoogleRevocation.Failed ? null
        : (T("Account rimosso, ma l'accesso di Google può restare"),
           T("{0} è uscito da Missiva, ma Google non ha confermato la revoca dell'accesso dato a Missiva: presso Google può restare valido. Si toglie a mano dalla pagina dell'account Google, fra le app e i servizi di terze parti collegati: {1}",
             account.Email, PermissionsPage));

    sealed class TokenError(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    static async Task<JsonElement> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancel)
    {
#if DEBUG
        using var response = await (TestHttp ?? Http).PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancel);
#else
        using var response = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancel);
#endif
        var text = await response.Content.ReadAsStringAsync(cancel);
        JsonElement json;
        try { json = JsonDocument.Parse(text).RootElement; }
        catch (JsonException) { throw new TokenError("", T("risposta inattesa da Google ({0})", (int)response.StatusCode)); }
        if (response.IsSuccessStatusCode) return json;
        var code = json.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
        var description = json.TryGetProperty("error_description", out var d) ? d.GetString() : null;
        throw new TokenError(code, description ?? code);
    }

    // ---------------------------------------------------------------- ritorno dal browser

    /// <summary>
    /// Aspetta che il browser torni su 127.0.0.1 con il risultato. Le connessioni si servono in parallelo, perché i
    /// browser ne aprono anche di vuote in anticipo; quelle senza il nostro "state" ricevono un 404.
    /// </summary>
    static async Task<Dictionary<string, string>> WaitForReplyAsync(TcpListener listener, string state, CancellationToken cancel)
    {
        var done = new TaskCompletionSource<Dictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync(stop.Token);
                    _ = AnswerAsync(client);
                }
            }
            catch (OperationCanceledException) { done.TrySetCanceled(cancel); }
            catch (Exception ex) { done.TrySetException(ex); }
        });
        try
        {
            return await done.Task;
        }
        finally
        {
            stop.Cancel();
        }

        async Task AnswerAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(2));
                    var stream = client.GetStream();
                    var query = ParseQuery(await ReadRequestLineAsync(stream, timeout.Token));
                    bool ours = query.GetValueOrDefault("state") == state;
                    await RespondAsync(stream, !ours ? null : query.ContainsKey("code") ? DonePage : CanceledPage, timeout.Token);
                    if (ours) done.TrySetResult(query);
                }
                catch { }
            }
        }
    }

    /// <summary>Legge la richiesta fino alla fine delle intestazioni (chiudere prima fa perdere la risposta) e ne restituisce la prima riga.</summary>
    static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancel)
    {
        var buffer = new byte[16384];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), cancel);
            if (read == 0) break;
            length += read;
            var text = Encoding.ASCII.GetString(buffer, 0, length);
            if (text.Contains("\r\n\r\n")) return text[..text.IndexOf("\r\n", StringComparison.Ordinal)];
        }
        return "";
    }

    /// <summary>Da "GET /?code=...&amp;state=... HTTP/1.1" ai parametri.</summary>
    static Dictionary<string, string> ParseQuery(string requestLine)
    {
        var result = new Dictionary<string, string>();
        var target = requestLine.Split(' ').ElementAtOrDefault(1) ?? "";
        int q = target.IndexOf('?');
        if (q < 0) return result;
        foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0].Replace('+', ' '))] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return result;
    }

    static async Task RespondAsync(NetworkStream stream, string? page, CancellationToken cancel)
    {
        var body = Encoding.UTF8.GetBytes(page ?? "");
        var head = Encoding.ASCII.GetBytes((page == null ? "HTTP/1.1 404 Not Found" : "HTTP/1.1 200 OK") +
            $"\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancel);
        await stream.WriteAsync(body, cancel);
    }

    static string Page(string title, string text) => $$"""
        <!doctype html><html lang="{{Lingua.Tag}}"><head><meta charset="utf-8"><title>Missiva</title>
        <style>
          body { font-family: "Segoe UI Variable", "Segoe UI", sans-serif; background: #f3f3f3; color: #1b1b1b;
                 display: grid; place-items: center; height: 100vh; margin: 0; }
          @media (prefers-color-scheme: dark) { body { background: #202020; color: #f3f3f3; } }
          div { text-align: center; max-width: 420px; padding: 16px; }
          h1 { font-size: 22px; font-weight: 600; margin: 0 0 8px; }
          p { font-size: 15px; opacity: .8; margin: 0; }
        </style></head>
        <body><div><h1>{{title}}</h1><p>{{text}}</p></div></body></html>
        """;

    // Le pagine che il browser mostra dopo l'accesso, nella lingua di Missiva: calcolate quando servono, non all'avvio.
    static string DonePage => Page(T("Accesso completato"), T("Puoi chiudere questa scheda e tornare a Missiva."));
    static string CanceledPage => Page(T("Accesso annullato"), T("Missiva non ha ricevuto l'autorizzazione. Puoi chiudere questa scheda."));
}
