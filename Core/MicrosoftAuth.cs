using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Missiva;

/// <summary>
/// Accesso agli account Microsoft tramite la finestra account di Windows (WAM). Gli account personali (Outlook.com,
/// Hotmail, Live) usano l'app di Missiva con l'autorità dei soli account personali, come sempre. Gli account di lavoro o
/// della scuola (Microsoft 365) hanno la loro app e la loro autorità: qualsiasi organizzazione ("organizations") o la
/// directory dell'organizzazione, con l'app di Missiva o con quella registrata dall'organizzazione. Un'app MSAL per ogni
/// coppia di ID e autorità, tutte sulla stessa cache cifrata: dopo il primo accesso non serve rifarlo.
/// </summary>
public static class MicrosoftAuth
{
    public static readonly string[] Scopes = { "User.Read", "Mail.ReadWrite", "Mail.Send" };

    /// <summary>
    /// L'app "Missiva" registrata nella directory dell'autore, per gli account personali e per quelli di lavoro o della scuola.
    /// Per gli account personali si può sostituire con la propria.
    /// </summary>
    public const string DefaultClientId = "039bad62-0aa2-4072-9d7c-0b2c261fa04c";

    /// <summary>
    /// Se la registrazione di "Missiva" accetta anche gli account di lavoro o della scuola. Dal 26 settembre 2026 sì
    /// (signInAudience AzureADandPersonalMicrosoftAccount, dieci permessi delegati): l'organizzazione può però chiedere
    /// l'approvazione di un amministratore, spiegata da Explain con il link di AdminConsentUrl. Se la registrazione tornasse
    /// solo per account personali, a false un account di lavoro con l'app di Missiva riceverebbe subito una spiegazione,
    /// senza aprire la finestra di accesso.
    /// </summary>
    public const bool DefaultAppAcceptsWork = true;

    /// <summary>L'autorità di qualsiasi organizzazione (account di lavoro o della scuola, non personali).</summary>
    public const string Organizations = "organizations";

    /// <summary>
    /// La finestra sopra cui si apre la finestra account di Windows quando nessun'altra l'ha chiesta per sé (OpenOver): la
    /// principale, che la mette una volta sola alla nascita.
    /// </summary>
    public static Func<IntPtr> Owner { get; set; } = () => IntPtr.Zero;

    /// <summary>Le finestre che hanno chiesto la finestra account di Windows sopra di sé (OwnedBy), dalla prima all'ultima.</summary>
    static readonly List<Func<IntPtr>> askedOwners = new();

    /// <summary>
    /// Da adesso, e finché non si rilascia, la finestra account di Windows si apre sopra questa finestra; rilasciata, torna
    /// quella che c'era prima. Fino all'audit dell'11 ottobre (C-13) ogni finestra scriveva Owner, e solo dopo Aggiungi
    /// account, Gestisci account e Risposte automatiche la principale lo rimetteva: dopo una regola, le Impostazioni o
    /// "Cerca anche fra i colleghi" della scrittura Owner restava la finestra ormai chiusa, tenuta in memoria da qui con
    /// tutto il suo contenuto, e la richiesta dopo riceveva il suo riferimento, che non vale più.
    /// </summary>
    public static IDisposable OwnedBy(Func<IntPtr> window)
    {
        lock (askedOwners) askedOwners.Add(window);
        return new Asked(window);
    }

    /// <summary>
    /// Lo stesso per una finestra di WPF, fino alla sua chiusura: chi chiede il consenso da una finestra non deve rimettere
    /// nulla. Chiesto più volte dalla stessa finestra (un clic dopo l'altro) vale una volta.
    /// </summary>
    public static void OpenOver(Window window)
    {
        if (overWindows.TryGetValue(window, out _)) return;
        var asked = OwnedBy(() => new WindowInteropHelper(window).Handle);
        overWindows.Add(window, asked);
        window.Closed += (_, _) => asked.Dispose();
    }

    /// <summary>Le finestre di OpenOver ancora aperte; non le tiene in vita.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, IDisposable> overWindows = new();

    sealed class Asked(Func<IntPtr> window) : IDisposable
    {
        public void Dispose()
        {
            lock (askedOwners) askedOwners.Remove(window);
        }
    }

    /// <summary>
    /// Il genitore per MSAL, deciso al momento della richiesta: l'ultima finestra che l'ha chiesto e che si vede ancora
    /// (esiste per Windows e non è nascosta, come una scrittura nella Posta in uscita), altrimenti Owner. Una finestra
    /// chiusa senza rilasciare non fa da genitore a nessuno.
    /// </summary>
    internal static IntPtr CurrentOwner()
    {
        Func<IntPtr>[] asked;
        lock (askedOwners) asked = askedOwners.ToArray();
        for (int i = asked.Length - 1; i >= 0; i--)
            if (asked[i]() is var handle && handle != IntPtr.Zero && IsWindowVisible(handle)) return handle;
        return Owner();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr handle);

#if DEBUG
    /// <summary>Prove: quante finestre hanno chiesto la finestra account di Windows sopra di sé e non l'hanno ancora rilasciata.</summary>
    internal static int TestAskedOwners { get { lock (askedOwners) return askedOwners.Count; } }
#endif

    /// <summary>
    /// Vero con Missiva nascosta nell'area di notifica: un token scaduto durante il controllo dei nuovi messaggi non apre la
    /// finestra account di Windows sopra quello che l'utente sta facendo; l'account mostra "Accedi di nuovo" alla riapertura.
    /// </summary>
    public static Func<bool> Quiet { get; set; } = () => false;

    /// <summary>
    /// Vero dentro le richieste in sottofondo (InBackgroundAsync): controllo dei nuovi messaggi, query delta, copia degli
    /// ultimi 30 giorni, coda delle azioni, ritorni di Posponi. AsyncLocal: vale per quello che parte da lì dentro e finisce
    /// con loro, non per i clic dell'utente fatti intanto.
    /// </summary>
    static readonly AsyncLocal<bool> background = new();

    /// <summary>
    /// Gli account per cui si è premuto Annulla nella finestra account di Windows: da lì nessuna finestra si apre più da sola,
    /// finché non si preme "Accedi di nuovo" (SignInAgainAsync). Prima tornava alla richiesta dopo.
    /// </summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> declined = new();

    /// <summary>
    /// Una richiesta in sottofondo: se il token vuole un nuovo accesso, la finestra account di Windows non si apre (nemmeno
    /// con la finestra di Missiva aperta, sopra quello che si sta scrivendo): l'errore è una MailException con "Accedi di
    /// nuovo" (Fix SignIn), che accende la barra sotto l'account. La delta ogni 30 secondi la riapriva dopo ogni Annulla.
    /// </summary>
    public static async Task<T> InBackgroundAsync<T>(Func<Task<T>> work)
    {
        background.Value = true; // solo per questa chiamata: il metodo asincrono rimette il valore di prima a chi lo chiama
        return await work();
    }

    public static async Task InBackgroundAsync(Func<Task> work)
    {
        background.Value = true;
        await work();
    }

    /// <summary>Se una richiesta adesso può aprire la finestra account di Windows da sola (per le prove).</summary>
    internal static bool MayAsk(MailAccount account) => !Quiet() && !background.Value && !declined.ContainsKey(account.Id);

    /// <summary>
    /// Una cosa alla volta, come in GoogleAuth: due richieste a Graph partite insieme (elenco e cartelle, all'avvio)
    /// creavano due app MSAL sullo stesso file di cache e, con il token scaduto, due finestre account di Windows.
    /// </summary>
    static readonly SemaphoreSlim AppGate = new(1, 1), TokenGate = new(1, 1);

    /// <summary>Le app MSAL, per "ID|autorità": l'account personale e quello di lavoro non si scambiano mai l'app.</summary>
    static readonly Dictionary<string, IPublicClientApplication> apps = new();
    static MsalCacheHelper? cache;

    /// <summary>L'app di un account: quella di oggi per gli account personali, la sua (o quella di Missiva) per quelli di lavoro.</summary>
    public static (string ClientId, string? Tenant) AppOf(MailAccount account) =>
        account.Tenant == null ? (PersonalClientId(), null) : (account.ClientId ?? DefaultClientId, account.Tenant);

    static string PersonalClientId() =>
        Store.Settings.MicrosoftClientId is { Length: > 0 } id ? id
        : throw new MailException("Manca l'ID dell'app Microsoft: inseriscilo aggiungendo un account Microsoft.");

    /// <summary>L'indirizzo dell'autorità, per il registro e per il link del consenso dell'amministratore.</summary>
    public static string AuthorityOf(string? tenant) => "https://login.microsoftonline.com/" + (tenant ?? "consumers");

    /// <summary>
    /// Un account di lavoro con l'app di Missiva, quando la registrazione accetta solo account personali
    /// (DefaultAppAcceptsWork falso): la finestra di accesso non si apre nemmeno. Oggi mai.
    /// </summary>
    public static bool Blocked(string clientId, string? tenant) => tenant != null && clientId == DefaultClientId && !DefaultAppAcceptsWork;

#if DEBUG
    /// <summary>Prove (--prova-bozze): l'app MSAL di un account, creata come per l'accesso (e scritta nel registro), senza chiedere token.</summary>
    internal static async Task<string> TestAppAsync(MailAccount account)
    {
        var (clientId, tenant) = AppOf(account);
        await AppAsync(clientId, tenant);
        return AuthorityOf(tenant);
    }
#endif

    static async Task<IPublicClientApplication> AppAsync(string clientId, string? tenant)
    {
        await AppGate.WaitAsync();
        try
        {
            var key = clientId + "|" + (tenant ?? "consumers");
            if (apps.TryGetValue(key, out var known)) return known;

            var builder = PublicClientApplicationBuilder.Create(clientId);
            builder = tenant == null ? builder.WithAuthority(AadAuthorityAudience.PersonalMicrosoftAccount)
                    : tenant == Organizations ? builder.WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
                    : builder.WithAuthority(AzureCloudInstance.AzurePublic, tenant);
            var built = builder
                .WithDefaultRedirectUri()
                .WithParentActivityOrWindow(() => CurrentOwner())
                .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "Missiva" })
                .Build();

            cache ??= await MsalCacheHelper.CreateAsync(new StorageCreationPropertiesBuilder("msal.cache", Store.Dir).Build());
            cache.RegisterCache(built.UserTokenCache);
            apps[key] = built;
            Log.Write($"app MSAL: client {clientId}, autorità {AuthorityOf(tenant)}");
            return built;
        }
        finally
        {
            AppGate.Release();
        }
    }

    /// <summary>
    /// Nuovo accesso: mostra la finestra di scelta dell'account. Per gli account personali (tenant null) come sempre;
    /// per quelli di lavoro con l'app e la directory date, e l'indirizzo scritto nella finestra di aggiunta.
    /// </summary>
    public static async Task<AuthenticationResult> SignInAsync(string? tenant = null, string? clientId = null, string? loginHint = null)
    {
        var id = clientId ?? (tenant == null ? PersonalClientId() : DefaultClientId);
        var pca = await AppAsync(id, tenant);
        var builder = pca.AcquireTokenInteractive(Scopes).WithPrompt(Prompt.SelectAccount);
        if (tenant != null && loginHint != null) builder = builder.WithLoginHint(loginHint);
        return await builder.ExecuteAsync();
    }

    /// <summary>Token validi per account: chiederli a Windows costa 40-60 ms, e ogni richiesta a Graph ne vuole uno.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AuthenticationResult> tokens = new();

    public static async Task<string> TokenAsync(MailAccount account)
    {
        if (Valid(account) is { } token) return token;
        await TokenGate.WaitAsync();
        try
        {
            // Intanto un'altra richiesta può averlo già preso.
            if (Valid(account) is { } fresh) return fresh;
            var result = await FreshTokenAsync(account);
            tokens[account.Id] = result;
            return result.AccessToken;
        }
        finally
        {
            TokenGate.Release();
        }
    }

    static string? Valid(MailAccount account) =>
        tokens.TryGetValue(account.Id, out var known) && known.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5) ? known.AccessToken : null;

    /// <summary>
    /// All'avvio, prima della finestra: app MSAL, cache cifrata e token degli account Microsoft chiesti in sottofondo, senza
    /// finestre (la prima richiesta a Windows costa 0,5 s), così le cartelle di Graph non li aspettano. Se serve un nuovo
    /// accesso non succede nulla qui: lo chiederà la prima richiesta a Graph, con la finestra di Missiva come genitore.
    /// </summary>
    public static void WarmUp(IEnumerable<MailAccount> accounts)
    {
        foreach (var account in accounts.Where(a => a.Kind == AccountKind.Microsoft && a.HomeAccountId != null).ToList())
            _ = Task.Run(() => SilentAsync(account));
    }

    static async Task SilentAsync(MailAccount account)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await TokenGate.WaitAsync();
        try
        {
            if (Valid(account) != null) return;
            var (clientId, tenant) = AppOf(account);
            var pca = await AppAsync(clientId, tenant);
            if (await pca.GetAccountAsync(account.HomeAccountId) is not { } msalAccount) return;
            tokens[account.Id] = await pca.AcquireTokenSilent(Scopes, msalAccount).ExecuteAsync();
            Log.Time($"token MSAL di {account.Email} preso in anticipo", clock);
        }
        catch (Exception ex)
        {
            Log.Write($"token MSAL di {account.Email} non preso in anticipo ({ex.GetType().Name}): lo chiederà la prima richiesta");
        }
        finally
        {
            TokenGate.Release();
        }
    }

    static async Task<AuthenticationResult> FreshTokenAsync(MailAccount account)
    {
        // Un account arrivato con una copia di sicurezza (Backup: l'accesso a Microsoft non si esporta) e mai entrato su
        // questo PC: nessuna finestra di Microsoft da sola, una per account, all'avvio; la barra "Accedi di nuovo".
        if (account.HomeAccountId == null)
            throw new MailException($"Su questo PC manca l'accesso a Microsoft di {account.Email}: premi Accedi di nuovo, sotto l'account.") { Fix = AccountFix.SignIn };
        var (clientId, tenant) = AppOf(account);
        var pca = await AppAsync(clientId, tenant);
        var msalAccount = account.HomeAccountId == null ? null : await pca.GetAccountAsync(account.HomeAccountId);
        try
        {
            if (msalAccount == null) throw new MsalUiRequiredException("no_account", "Account non trovato nella cache.");
            return await pca.AcquireTokenSilent(Scopes, msalAccount).ExecuteAsync();
        }
        catch (MsalUiRequiredException)
        {
#if DEBUG || PROVA
            // Nella versione di prova nessuno può rifare l'accesso: la finestra account di Windows non si apre da sola.
            if (Environment.GetEnvironmentVariable("MISSIVA_NO_READ") == "1")
                throw new MailException($"{account.Email} chiede un nuovo accesso: nella versione di prova la finestra di Microsoft non si apre da sola.") { Fix = AccountFix.SignIn };
#endif
            // Nell'area di notifica, in sottofondo o dopo un Annulla: nessuna finestra da sola, la barra sotto l'account.
            if (!MayAsk(account))
                throw new MailException($"{account.Email} chiede un nuovo accesso: premi Accedi di nuovo, sotto l'account.") { Fix = AccountFix.SignIn };
            // Token scaduto o permessi cambiati: si richiede l'accesso per questo account.
            var builder = pca.AcquireTokenInteractive(Scopes);
            builder = msalAccount != null ? builder.WithAccount(msalAccount) : builder.WithLoginHint(account.Email);
            AuthenticationResult result;
            try
            {
                result = await builder.ExecuteAsync();
            }
            catch (Exception ex) when (Canceled(ex))
            {
                declined[account.Id] = true;
                Log.Write($"accesso di {account.Email} annullato nella finestra di Microsoft: nessuna finestra da sola fino ad Accedi di nuovo");
                throw new MailException($"Accesso di {account.Email} annullato: premi Accedi di nuovo, sotto l'account, quando vuoi.", ex) { Fix = AccountFix.SignIn };
            }
            CheckChosen(account, result);
            account.HomeAccountId = result.Account.HomeAccountId.Identifier;
            Store.Save();
            return result;
        }
    }

    /// <summary>
    /// Nella finestra di Windows si può scegliere un altro account (con il solo login_hint, quando la cache MSAL non ha più
    /// questo: msal.cache perso o copiato su un altro PC). Allora niente: né HomeAccountId né il token, altrimenti la voce di
    /// questo account mostrerebbe e invierebbe con la casella dell'altro, e i permessi in più finirebbero sotto questo.
    /// </summary>
    internal static void CheckChosen(MailAccount account, AuthenticationResult result)
    {
        if (!result.Account.Username.Equals(account.Email, StringComparison.OrdinalIgnoreCase))
            throw new MailException($"Nella finestra di Microsoft hai scelto {result.Account.Username}, non {account.Email}: accedi con {account.Email} " +
                                    "(per un altro account usa Aggiungi account).") { Fix = AccountFix.SignIn };
    }

    /// <summary>Il permesso del calendario, chiesto solo quando serve (rispondere agli inviti di Exchange): mai all'accesso di base.</summary>
    public static readonly string[] CalendarScopes = { "Calendars.ReadWrite" };

    /// <summary>Il permesso delle impostazioni della casella (risposte automatiche, categorie di Outlook), solo dal suo pulsante.</summary>
    public static readonly string[] MailboxSettingsScopes = { "MailboxSettings.ReadWrite" };

    /// <summary>
    /// I permessi delle caselle condivise di Microsoft 365 (leggere e scrivere da info@, segreteria@), chiesti insieme solo
    /// dal pulsante "Consenti l'accesso alle caselle condivise", con l'account di lavoro che le apre.
    /// </summary>
    public static readonly string[] SharedScopes = { "Mail.ReadWrite.Shared", "Mail.Send.Shared" };

    /// <summary>Il permesso dei contatti di Outlook (vista Persone), chiesto solo dal pulsante "Consenti l'accesso ai contatti".</summary>
    public static readonly string[] ContactsScopes = { "Contacts.ReadWrite" };

    /// <summary>
    /// Il permesso delle persone rilevanti (me/people: i colleghi della directory di Microsoft 365 a cui non si è mai scritto),
    /// chiesto solo da "Cerca anche fra i colleghi" in fondo ai suggerimenti dei destinatari.
    /// </summary>
    public static readonly string[] PeopleScopes = { "People.Read" };

    /// <summary>
    /// Un token con permessi in più (consenso incrementale). Senza interactive nessuna finestra: null se il consenso manca.
    /// Con interactive, solo dal pulsante "Consenti l'accesso a ...", la finestra account di Windows lo chiede.
    /// </summary>
    public static async Task<string?> ExtraTokenAsync(MailAccount account, string[] extra, bool interactive)
    {
        var key = account.Id + "|" + string.Join(' ', extra);
        if (tokens.TryGetValue(key, out var known) && known.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5)) return known.AccessToken;
        await TokenGate.WaitAsync();
        try
        {
            var (clientId, tenant) = AppOf(account);
            var pca = await AppAsync(clientId, tenant);
            var msalAccount = account.HomeAccountId == null ? null : await pca.GetAccountAsync(account.HomeAccountId);
            var scopes = Scopes.Concat(extra).ToArray();
            AuthenticationResult result;
            try
            {
                if (msalAccount == null) throw new MsalUiRequiredException("no_account", "Account non trovato nella cache.");
                result = await pca.AcquireTokenSilent(scopes, msalAccount).ExecuteAsync();
            }
            catch (MsalUiRequiredException) when (!interactive)
            {
                return null;
            }
            catch (MsalUiRequiredException)
            {
                var builder = pca.AcquireTokenInteractive(scopes);
                builder = msalAccount != null ? builder.WithAccount(msalAccount) : builder.WithLoginHint(account.Email);
                result = await builder.ExecuteAsync();
                CheckChosen(account, result);
                Log.Write($"consenso dato da {account.Email}: {string.Join(", ", extra)}");
            }
            tokens[key] = result;
            return result.AccessToken;
        }
        finally
        {
            TokenGate.Release();
        }
    }

    /// <summary>
    /// "Accedi di nuovo" (Impostazioni, Account, Gestisci), solo al clic: la finestra account di Windows per questo account,
    /// con la sua app e la sua autorità, come quando il token scade. I token tenuti in memoria (anche quelli dei permessi in
    /// più) si dimenticano. Un altro account scelto nella finestra non sostituisce questo.
    /// </summary>
    public static async Task SignInAgainAsync(MailAccount account)
    {
        await TokenGate.WaitAsync();
        try
        {
            Forget(account);
            var (clientId, tenant) = AppOf(account);
            var pca = await AppAsync(clientId, tenant);
            var msalAccount = account.HomeAccountId == null ? null : await pca.GetAccountAsync(account.HomeAccountId);
            var builder = pca.AcquireTokenInteractive(Scopes);
            builder = msalAccount != null ? builder.WithAccount(msalAccount) : builder.WithLoginHint(account.Email);
            var result = await builder.ExecuteAsync();
            CheckChosen(account, result);
            account.HomeAccountId = result.Account.HomeAccountId.Identifier;
            tokens[account.Id] = result;
            declined.TryRemove(account.Id, out _);
            Store.Save();
            Log.Write($"nuovo accesso di {account.Email}");
        }
        finally
        {
            TokenGate.Release();
        }
    }

    /// <summary>I token dell'account tenuti in memoria, anche quelli dei permessi in più.</summary>
    static void Forget(MailAccount account)
    {
        foreach (var key in tokens.Keys.Where(k => k == account.Id || k.StartsWith(account.Id + "|", StringComparison.Ordinal)).ToList())
            tokens.TryRemove(key, out _);
    }

    public static async Task SignOutAsync(MailAccount account)
    {
        Forget(account);
        try
        {
            var (clientId, tenant) = AppOf(account);
            var pca = await AppAsync(clientId, tenant);
            if (account.HomeAccountId != null && await pca.GetAccountAsync(account.HomeAccountId) is { } a)
                await pca.RemoveAsync(a);
        }
        catch (Exception ex)
        {
            // L'account esce comunque da Missiva; se la cache degli accessi di Microsoft lo tiene ancora, il registro lo dice.
            Log.Error($"accesso a Microsoft di {account.Email} non tolto dalla cache degli accessi", ex);
        }
    }

    // ---------------------------------------------------------------- errori spiegati

    /// <summary>
    /// Un accesso non riuscito, detto in italiano. AdminConsent: serve l'approvazione di un amministratore
    /// dell'organizzazione, e la finestra di aggiunta propone il link da mandargli.
    /// </summary>
    public sealed record SignInError(string Text, bool AdminConsent, string? Code);

    /// <summary>Annullato dall'utente nella finestra di Windows: nessun errore da mostrare.</summary>
    public static bool Canceled(Exception ex) =>
        ex is MsalException m && (m.ErrorCode == "authentication_canceled" || m.ErrorCode == "access_denied" && m.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Il codice AADSTS che Microsoft mette nel messaggio (anche quando l'errore passa dalla finestra di Windows) e la sua
    /// spiegazione, con quello che si può fare.
    /// </summary>
    /// <param name="purpose">Per che cosa serviva il consenso: all'accesso la posta, poi i permessi in più (il calendario).</param>
    public static SignInError Explain(Exception ex, string email, string clientId, string purpose = "leggere e inviare la posta")
    {
        var code = Regex.Match(ex.Message, @"AADSTS(\d+)") is { Success: true } m ? m.Groups[1].Value : null;
        var who = email.Length > 0 ? email : "questo account";
        return code switch
        {
            "65001" => new($"La tua organizzazione non ha ancora approvato Missiva: nessuno ha dato il consenso all'app per {purpose}. " +
                           "Serve l'approvazione di un amministratore di Microsoft 365: mandagli il link qui sotto.", true, code),
            "90094" => new($"La tua organizzazione lascia approvare le app solo agli amministratori: Missiva chiede di {purpose}, " +
                           "e un amministratore di Microsoft 365 deve approvarla. Mandagli il link qui sotto.", true, code),
            "90095" => new("Serve l'approvazione di un amministratore. Nella finestra di Microsoft puoi chiederla (Richiedi approvazione, con il motivo): " +
                           "quando la concede, riprova. Oppure manda all'amministratore il link qui sotto.", true, code),
            "50020" => new($"{who} non fa parte della directory scelta. Controlla l'indirizzo e la directory (tenant); se è un account personale, " +
                           "aggiungilo come Outlook.com, Hotmail e Live.", false, code),
            "700016" => new($"L'app {clientId} non esiste nella directory della tua organizzazione: l'ID dell'app o la directory sono sbagliati, " +
                            "oppure l'app è registrata per una sola organizzazione. Controllali con chi l'ha registrata.", false, code),
            "50194" => new($"L'app {clientId} è registrata per una sola organizzazione: scrivi l'ID o il dominio della sua directory (tenant) " +
                           "in \"Usa l'app della mia organizzazione\".", false, code),
            "500200" => new($"{who} è un account Microsoft personale: aggiungilo come Outlook.com, Hotmail e Live.", false, code),
            "53003" => new("L'accesso è bloccato dai criteri della tua organizzazione (accesso condizionale): per esempio solo da dispositivi gestiti, " +
                           "da certi luoghi o con certe app. Chiedi all'amministratore di Microsoft 365.", false, code),
            "50105" => new($"{who} non è fra gli utenti a cui l'amministratore ha assegnato l'app {clientId}: chiedigli di assegnartela.", false, code),
            "65004" => new("Hai rifiutato il consenso nella finestra di Microsoft: senza, Missiva non può leggere la posta. Riprova e premi Accetta.", false, code),
            "50011" or "500113" => new($"L'app {clientId} non ha gli indirizzi di ritorno di Missiva. Chi l'ha registrata deve aggiungere, nella piattaforma " +
                                       $"App per dispositivi mobili e desktop, ms-appx-web://microsoft.aad.brokerplugin/{clientId} e http://localhost.", false, code),
            "7000218" => new($"L'app {clientId} non accetta le app senza segreto: chi l'ha registrata deve attivare \"Consenti flussi client pubblici\".", false, code),
            _ => new("Accesso non riuscito: " + ex.Message, false, code),
        };
    }

    /// <summary>
    /// Il link del consenso dell'amministratore: aprendolo, un amministratore di Microsoft 365 approva l'app per tutta
    /// l'organizzazione (la directory dell'account se è nota, altrimenti quella dell'amministratore).
    /// </summary>
    public static string AdminConsentUrl(string clientId, string? tenant) =>
        $"https://login.microsoftonline.com/{(tenant is null or Organizations ? Organizations : tenant)}/v2.0/adminconsent" +
        $"?client_id={clientId}&scope=https://graph.microsoft.com/.default&redirect_uri=http://localhost";

    /// <summary>
    /// Cosa approva l'amministratore con il link: con scope .default tutti i permessi della registrazione in un solo
    /// consenso. Per l'app di Missiva anche calendario, impostazioni della casella e caselle condivise, che Missiva chiede
    /// solo quando si aprono; per l'app dell'organizzazione i permessi che le ha dato chi l'ha registrata.
    /// </summary>
    public static string AdminConsentScope(string clientId) => clientId == DefaultClientId
        ? "Con un solo consenso approva tutti i permessi di Missiva: posta, e anche calendario, impostazioni della casella (risposte automatiche) " +
          "e caselle condivise, che Missiva usa solo quando apri quelle funzioni."
        : "Con un solo consenso approva tutti i permessi che la tua organizzazione ha dato all'app.";
}
