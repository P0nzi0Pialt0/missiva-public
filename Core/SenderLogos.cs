using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AngleSharp.Dom;

namespace Missiva;

/// <summary>
/// Il logo del marchio del mittente al posto delle iniziali (richiesta dell'autore, 28 settembre 2026), dal dominio
/// dell'indirizzo. Un logo dà fiducia, quindi non da chiunque (scelta dell'autore fra le tre proposte, stretta dall'audit di
/// sicurezza del 4 ottobre 2026, docs\audit\ESITO.md, blocco 2). Solo per i domini noti (posta ricevuta da almeno 14
/// giorni, in giorni diversi, con la data del server: IsKnown) e autenticati (un messaggio ricevuto da lì ha superato
/// DMARC secondo il server della propria casella: NoteBody, IsAuthenticated); per gli altri non parte nessuna richiesta
/// e non si mostra nulla, nemmeno un logo già salvato.
/// 1. BIMI (default._bimi.dominio nel DNS), solo con il certificato del marchio (VMC) che vale per quel dominio e risale
///    a un'autorità dei marchi fissata qui (DigiCert Verified Mark, SSL.com VMC): il logo è quello dentro il
///    certificato, non quello dell'indirizzo l=. Un dominio di phishing non ha un VMC a suo nome (serve un marchio registrato).
/// 2. Altrimenti l'icona del sito: apple-touch-icon, le icone dichiarate nella pagina, favicon.ico.
/// Mai per la posta indesiderata (lo decide chi chiama) né per i domini di posta personale (gmail.com, libero.it...),
/// dove il logo sarebbe del gestore. Solo https, solo indirizzi pubblici e mai dal proxy di sistema (un dominio che punta
/// a 192.168.x o a 127.0.0.1 non fa bussare Missiva alla rete di casa), dimensioni limitate; gli SVG passano da LogoSvg
/// (lista bianca sull'albero, riferimenti solo interni, tetti; a SharpVectors arriva il testo riscritto, con le risorse
/// esterne vietate), le icone dal decodificatore del loro tipo con le misure lette prima, la pagina del sito sotto i
/// tetti dell'analisi delle email, e il disegno che non finisce si ferma. Salvati come PNG 96x96 in Store.Dir\loghi e
/// riverificati dopo 30 giorni; ".none" per i domini senza logo, riprovati dopo 14 giorni.
/// </summary>
public static class SenderLogos
{
    public const int Pixels = 96;
    static readonly TimeSpan Retry = TimeSpan.FromDays(14);
    /// <summary>Un logo salvato si riverifica dopo 30 giorni (audit, S-17: prima restava per sempre, anche tolto o cambiato).</summary>
    static readonly TimeSpan Refresh = TimeSpan.FromDays(30);
    static readonly TimeSpan KnownAfter = TimeSpan.FromDays(14);

    static string Dir => Path.Combine(Store.Dir, "loghi");
    public static string PathOf(string domain) => Path.Combine(Dir, domain + ".png");
    static string MissingPath(string domain) => Path.Combine(Dir, domain + ".none");

    /// <summary>
    /// Il logo di un dominio è cambiato (salvato, tolto, o il dominio è appena risultato autenticato e quindi può
    /// mostrarlo), da un thread qualsiasi.
    /// </summary>
    public static event Action<string>? Arrived;

    // ---------------------------------------------------------------- il dominio

    /// <summary>Posta personale e certificata: il logo sarebbe del gestore, non di chi scrive.</summary>
    static readonly HashSet<string> Personal = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "outlook.it", "hotmail.com", "hotmail.it", "live.com", "live.it", "msn.com",
        "yahoo.com", "yahoo.it", "ymail.com", "icloud.com", "me.com", "mac.com", "aol.com", "libero.it", "virgilio.it", "tiscali.it",
        "alice.it", "tim.it", "tin.it", "fastwebnet.it", "email.it", "inwind.it", "iol.it", "blu.it", "gmx.com", "gmx.it", "gmx.de",
        "gmx.net", "web.de", "proton.me", "protonmail.com", "pm.me", "tutanota.com", "tuta.io", "yandex.com", "yandex.ru", "mail.ru",
        "zoho.com", "hey.com", "fastmail.com", "legalmail.it", "arubapec.it", "postacert.it", "pecveneto.it", "sicurezzapostale.it",
    };

    /// <summary>Suffissi a due livelli: di "news.amazon.co.uk" il dominio è amazon.co.uk, non co.uk.</summary>
    static readonly HashSet<string> TwoLevel = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "com.au", "net.au", "org.au", "co.jp", "ne.jp", "or.jp", "com.br", "com.mx", "com.ar",
        "co.nz", "co.za", "com.tr", "com.cn", "com.hk", "com.sg", "co.in", "co.kr", "com.tw", "co.il", "com.eg",
    };

    /// <summary>
    /// Il dominio del marchio: di auto-confirm@amazon.it amazon.it, di newsletter@insider.sky.it sky.it; null per la posta
    /// personale o certificata e per indirizzi strani.
    /// </summary>
    public static string? DomainOf(string email)
    {
        int at = email.LastIndexOf('@');
        if (at < 0) return null;
        var host = email[(at + 1)..].Trim().TrimEnd('.', '>').ToLowerInvariant();
        if (host.Length is < 4 or > 253 || !host.Contains('.') || host.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))) return null;
        var labels = host.Split('.');
        if (labels.Any(l => l.Length == 0) || labels.All(l => l.All(char.IsAsciiDigit))) return null;
        int keep = labels.Length >= 3 && TwoLevel.Contains(labels[^2] + "." + labels[^1]) ? 3 : 2;
        var domain = string.Join('.', labels[^Math.Min(keep, labels.Length)..]);
        if (Personal.Contains(domain) || labels.Contains("pec")) return null;
        return domain;
    }

    // ---------------------------------------------------------------- i domini noti

    sealed class Seen
    {
        public DateTime First { get; set; }
        public DateTime Last { get; set; }
        /// <summary>
        /// L'ultima volta che un messaggio ricevuto dal dominio è risultato autenticato (NoteBody); null mai. Senza, niente
        /// logo: né cercato né mostrato. I file scritti prima dell'audit non l'hanno (AuthenticateFromCopies).
        /// </summary>
        public DateTime? Verified { get; set; }
    }

    static Dictionary<string, Seen>? seen;
    static readonly object seenLock = new();
    static int saveVersion;
    static string SeenPath => Path.Combine(Dir, "domini.dat");

    /// <summary>
    /// Un messaggio ricevuto (non indesiderato): la prima e l'ultima ricezione del suo dominio, con la data del server
    /// (INTERNALDATE, receivedDateTime), che il mittente non può anticipare. Il file è cifrato come il resto (DPAPI).
    /// </summary>
    public static void Note(MessageSummary message)
    {
        if (message.Date == DateTime.MinValue || DomainOf(message.From.Email) is not { } domain) return;
        var when = message.Date.ToUniversalTime();
        lock (seenLock)
        {
            var all = Load();
            if (all.TryGetValue(domain, out var s))
            {
                if (when >= s.First && when <= s.Last) return;
                if (when < s.First) s.First = when;
                if (when > s.Last) s.Last = when;
            }
            else all[domain] = new Seen { First = when, Last = when };
        }
        _ = SaveSoonAsync();
    }

    /// <summary>Posta da almeno 14 giorni, in almeno due giorni diversi: il phishing arriva da domini appena nati.</summary>
    public static bool IsKnown(string domain)
    {
        lock (seenLock)
            return Load().TryGetValue(domain, out var s) && s.First <= DateTime.UtcNow - KnownAfter && s.Last - s.First >= TimeSpan.FromDays(1);
    }

    /// <summary>
    /// Un messaggio ricevuto dal dominio è arrivato davvero da lì (audit, S-04): il server della casella ha scritto DMARC
    /// superato per quel dominio (SenderCheck.Authenticated). Solo allora il logo si cerca e si mostra: prima bastava il
    /// From, che chiunque può scrivere.
    /// </summary>
    public static bool IsAuthenticated(string domain)
    {
        lock (seenLock) return Load().TryGetValue(domain, out var s) && s.Verified != null;
    }

    /// <summary>I messaggi di cui il corpo è arrivato senza la prova che vengano dal dominio del From: per loro niente logo.</summary>
    static readonly ConcurrentDictionary<string, bool> doubtful = new();
    static string MessageKey(MailAccount account, MessageSummary message) => account.Id + "|" + message.FolderId + "|" + message.Id;

    /// <summary>
    /// Il corpo di questo messaggio è arrivato e le intestazioni del server non dicono che viene dal dominio del From (o
    /// dicono di no: la fascia rossa): niente logo sulla sua riga e nel riquadro, anche se il dominio è autenticato da
    /// altri messaggi. Solo in memoria; finché il corpo non arriva vale il dominio.
    /// </summary>
    public static bool IsDoubtful(MailAccount? account, MessageSummary message) =>
        account != null && !doubtful.IsEmpty && doubtful.ContainsKey(MessageKey(account, message));

    /// <summary>
    /// Il corpo di un messaggio ricevuto (aperto, scaricato in anticipo o copiato per leggerlo senza rete; mai dalla Posta
    /// indesiderata): le Authentication-Results arrivano con lui, non con l'elenco. Se il server della casella dice che
    /// viene dal dominio del From, il dominio diventa autenticato (domini.dat) e chi mostra i loghi viene avvisato; se dice
    /// altro, quel messaggio resta senza logo. Senza intestazioni del proprio server non si sa, e non cambia nulla.
    /// </summary>
    public static void NoteBody(MailAccount account, MessageSummary message, MessageBody body)
    {
        if (DomainOf(message.From.Email) is not { } domain || SenderCheck.Own(account, body.AuthenticationResults) is not { } results) return;
        if (!SenderCheck.Authenticated(results, domain))
        {
            // Un'intestazione che non si sa chi ha scritto (Unproven: account IMAP, quella di Gmail sotto un'altra) e che
            // direbbe che va tutto bene non è una prova, ma nemmeno un dubbio: come se non ci fosse (audit dell'11 ottobre, T-03).
            if (results.Unproven && SenderCheck.Authenticated(results with { Unproven = false }, domain)) return;
            if (doubtful.TryAdd(MessageKey(account, message), true)) Arrived?.Invoke(domain);
            return;
        }
        Note(message);
        bool first;
        lock (seenLock)
        {
            if (!Load().TryGetValue(domain, out var s)) return; // un messaggio senza data
            first = s.Verified == null;
            if (!first && DateTime.UtcNow - s.Verified < TimeSpan.FromDays(1)) return;
            s.Verified = DateTime.UtcNow;
        }
        _ = SaveSoonAsync();
        if (first)
        {
            Log.Write($"logo di {domain}: dominio autenticato (DMARC superato in un messaggio ricevuto)");
            Arrived?.Invoke(domain);
        }
    }

    /// <summary>
    /// Una volta sola dopo l'aggiornamento (audit, S-04): i domini di prima non hanno il segno "autenticato" e i loro
    /// loghi sparirebbero tutti fino al prossimo messaggio. Le copie dei corpi già sul PC (BodyCache: i messaggi aperti e
    /// la Posta in arrivo degli ultimi 30 giorni) hanno le Authentication-Results: si rileggono, in sottofondo. Chi non ha
    /// un messaggio autenticato fra le copie riavrà il logo (che resta su disco) al prossimo messaggio che lo supera.
    /// </summary>
    public static void AuthenticateFromCopies(IReadOnlyList<MailAccount> accounts)
    {
        var done = MigrationMark;
        if (File.Exists(done)) return;
        try
        {
            var clock = Stopwatch.StartNew();
            int copies = 0, before;
            lock (seenLock) before = Load().Values.Count(s => s.Verified != null);
            foreach (var account in accounts)
                BodyCache.Each(account, saved =>
                {
                    Interlocked.Increment(ref copies);
                    if (saved.Summary is { } message) NoteBody(account, message, new MessageBody { AuthenticationResults = saved.AuthenticationResults });
                });
            int after, known;
            lock (seenLock) (after, known) = (Load().Values.Count(s => s.Verified != null), Load().Count);
            Directory.CreateDirectory(Dir);
            File.WriteAllText(done, DateTime.UtcNow.ToString("o"));
            Log.Write($"loghi: {copies} copie di messaggi rilette in {clock.ElapsedMilliseconds} ms, domini autenticati {after - before} su {known}");
        }
        catch (Exception ex)
        {
            Log.Error("loghi: domini autenticati dalle copie", ex);
        }
    }

    /// <summary>
    /// Una volta sola dopo l'aggiornamento del 12 ottobre 2026 (scelte, T-06): "autenticato" ora chiede DMARC superato
    /// (SenderCheck.Authenticated), ma un dominio segnato prima poteva esserlo per una firma DKIM o un SPF senza DMARC, e
    /// il segno non scade. Le copie dei corpi sul PC si rileggono, in sottofondo: un dominio che fra le copie ha messaggi
    /// che valevano con la regola di prima (SenderCheck.Aligned) e nessuno che supera DMARC perde il segno, e con lui il
    /// logo (il file resta: torna al primo messaggio che supera DMARC). Chi non ha copie, o ha solo messaggi che non
    /// valevano nemmeno prima (un From falsificato, l'intestazione di un account IMAP), lo tiene: non c'è nulla che dica
    /// come l'aveva avuto, e ogni suo messaggio nuovo si giudica per sé (IsDoubtful).
    /// </summary>
    public static void RecheckFromCopies(IReadOnlyList<MailAccount> accounts)
    {
        var done = RecheckMark;
        if (File.Exists(done)) return;
        try
        {
            var clock = Stopwatch.StartNew();
            int copies = 0;
            var passes = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase); // per dominio: almeno un messaggio che supera DMARC
            foreach (var account in accounts)
                BodyCache.Each(account, saved =>
                {
                    Interlocked.Increment(ref copies);
                    if (saved.Summary is not { } message || DomainOf(message.From.Email) is not { } domain
                        || SenderCheck.Own(account, saved.AuthenticationResults) is not { } results || !SenderCheck.Aligned(results, domain)) return;
                    bool now = SenderCheck.Authenticated(results, domain);
                    passes.AddOrUpdate(domain, now, (_, before) => before || now);
                });
            var dropped = new List<string>();
            lock (seenLock)
                foreach (var (domain, any) in passes)
                    if (!any && Load().TryGetValue(domain, out var s) && s.Verified != null)
                    {
                        s.Verified = null;
                        dropped.Add(domain);
                    }
            if (dropped.Count > 0)
            {
                Interlocked.Increment(ref saveVersion); // un salvataggio in attesa non riscrive dopo
                SaveNow();
            }
            Directory.CreateDirectory(Dir);
            File.WriteAllText(done, DateTime.UtcNow.ToString("o"));
            Log.Write($"loghi: {copies} copie di messaggi rilette in {clock.ElapsedMilliseconds} ms, {dropped.Count} domini autenticati senza DMARC superato non lo sono più");
            foreach (var domain in dropped) Arrived?.Invoke(domain);
        }
        catch (Exception ex)
        {
            Log.Error("loghi: domini autenticati riletti dalle copie", ex);
        }
    }

    static Dictionary<string, Seen> Load()
    {
        if (seen != null) return seen;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            seen = File.Exists(SeenPath)
                ? JsonSerializer.Deserialize<Dictionary<string, Seen>>(Store.Unprotect(File.ReadAllText(SeenPath)))
                : null;
            Log.Time($"loghi: domini noti letti ({seen?.Count ?? 0} domini, thread {Environment.CurrentManagedThreadId})", clock);
        }
        catch (Exception ex)
        {
            Log.Write($"loghi: domini noti non letti ({ex.Message}), si riparte");
        }
        return seen ??= new(StringComparer.OrdinalIgnoreCase);
    }

    static async Task SaveSoonAsync()
    {
        var version = Interlocked.Increment(ref saveVersion);
        Volatile.Write(ref savePending, 1);
        await Task.Delay(5000).ConfigureAwait(false);
        if (version != Volatile.Read(ref saveVersion)) return;
        SaveNow();
    }

    /// <summary>Un salvataggio di domini.dat è in attesa (SaveSoonAsync), non ancora scritto.</summary>
    static int savePending;

    /// <summary>
    /// All'uscita (App.OnExit, fine della sessione di Windows): i domini visti e i segni "autenticato" degli ultimi cinque
    /// secondi, che aspettavano SaveSoonAsync, si scrivono subito. Prima si perdevano, e per il logo contano i giorni in cui
    /// un dominio ha scritto (audit dell'11 ottobre, C-34). Senza niente in attesa non scrive.
    /// </summary>
    public static void Flush()
    {
        if (Volatile.Read(ref savePending) == 0) return;
        Interlocked.Increment(ref saveVersion); // quello in attesa non riscrive dopo
        SaveNow();
    }

    static void SaveNow()
    {
        try
        {
            string json;
            Volatile.Write(ref savePending, 0);
            lock (seenLock) json = JsonSerializer.Serialize(seen);
            Directory.CreateDirectory(Dir);
            // File temporaneo, svuotamento e rinomina: una chiusura a metà scrittura non lascia il file troncato (i domini
            // autenticati ripartirebbero da zero; audit del 7 ottobre, B-26).
            SafeFile.Write(SeenPath, System.Text.Encoding.UTF8.GetBytes(Store.Protect(json)));
        }
        catch (Exception ex) { Log.Error("loghi: domini noti", ex); }
    }

    /// <summary>Il segno che le copie dei corpi sono già state rilette (AuthenticateFromCopies).</summary>
    internal static string MigrationMark => Path.Combine(Dir, "autenticati");

    /// <summary>Il segno che i domini autenticati sono già stati riletti con la regola del 12 ottobre 2026 (RecheckFromCopies).</summary>
    internal static string RecheckMark => Path.Combine(Dir, "autenticati-dmarc");

#if DEBUG
    /// <summary>Prove: un dominio già visto prende il segno "autenticato" com'era prima del 12 ottobre 2026, senza un messaggio che superi DMARC.</summary>
    internal static void TestMark(string domain)
    {
        lock (seenLock)
            if (Load().TryGetValue(domain, out var s)) s.Verified = DateTime.UtcNow;
    }

    /// <summary>Prove: domini.dat scritto subito e riletto dal disco alla prossima domanda.</summary>
    internal static void TestReload()
    {
        Interlocked.Increment(ref saveVersion);
        lock (seenLock)
        {
            SaveNow();
            seen = null;
        }
    }

    /// <summary>Prove: il file scritto prima dell'audit (senza il segno "autenticato") si legge, e il dominio non è autenticato.</summary>
    internal static bool TestReadsOld() =>
        JsonSerializer.Deserialize<Dictionary<string, Seen>>("{\"esempio-prova.example\":{\"First\":\"2026-09-01T08:00:00Z\",\"Last\":\"2026-09-20T08:00:00Z\"}}")
            is { Count: 1 } old && old["esempio-prova.example"] is { Verified: null, First.Year: 2026 };

    /// <summary>Prove: i domini di prova escono da domini.dat e dalla cartella dei loghi.</summary>
    internal static void TestForget(IEnumerable<string> domains)
    {
        lock (seenLock)
            foreach (var domain in domains)
            {
                Load().Remove(domain);
                if (!Directory.Exists(Dir)) continue; // cartella dei dati nuova, nessun logo ancora: File.Delete solleverebbe
                File.Delete(PathOf(domain));
                File.Delete(MissingPath(domain));
            }
        doubtful.Clear();
        TestReload();
    }
#endif

    // ---------------------------------------------------------------- la ricerca

    static readonly SemaphoreSlim Gate = new(2);
    static readonly ConcurrentDictionary<string, bool> running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cerca il logo del dominio in sottofondo, se non c'è già e non si è cercato da poco: BIMI, poi l'icona del sito.
    /// Solo per i domini noti (IsKnown) e autenticati (IsAuthenticated), e falso per gli altri: prima la richiesta BIMI
    /// (DNS, poi l'indirizzo scelto dal mittente) partiva per qualunque dominio appena una sua riga compariva nell'elenco,
    /// ed era una conferma di recapito che il riquadro invece nega (audit, S-03). Quando il logo arriva, Arrived.
    /// </summary>
    public static bool Request(string domain)
    {
        if (!IsKnown(domain) || !IsAuthenticated(domain)) return false;
        if (!running.TryAdd(domain, true)) return true;
        _ = Task.Run(async () =>
        {
            try { await FetchAsync(domain); }
            finally { running.TryRemove(domain, out _); }
        });
        return true;
    }

    /// <summary>La ricerca del logo del dominio è in corso.</summary>
    internal static bool IsBusy(string domain) => running.ContainsKey(domain);

    /// <summary>Il logo salvato del dominio ha più di 30 giorni: chi lo mostra lo fa riverificare (Request), una volta per sessione.</summary>
    public static bool IsStale(string domain)
    {
        try { return File.Exists(PathOf(domain)) && DateTime.UtcNow - File.GetLastWriteTimeUtc(PathOf(domain)) >= Refresh; }
        catch (IOException) { return false; }
    }

    /// <summary>Una ricerca in corso: se una richiesta è caduta per la rete o per un errore del server, "non trovato" non è una risposta.</summary>
    sealed class FetchAttempt
    {
        public volatile bool Unreachable;
        /// <summary>Un'immagine non si è disegnata perché i disegni erano sospesi (DrawingSuspended): nemmeno questa è una risposta.</summary>
        public volatile bool Suspended;
    }
    static readonly AsyncLocal<FetchAttempt?> Attempt = new();

    static async Task FetchAsync(string domain)
    {
        await Gate.WaitAsync();
        try
        {
            // Con un disegno che non si ferma (DrawingSuspended) non si cerca nulla: un logo non disegnato per questo non
            // deve diventare "nessun logo" per 14 giorni. Si riprova quando una riga lo richiede di nuovo.
            if (DrawingSuspended) return;
            Directory.CreateDirectory(Dir);
            // Un logo già salvato vale 30 giorni, poi si riverifica (intanto resta quello).
            var saved = PathOf(domain);
            bool had = File.Exists(saved);
            var age = had ? DateTime.UtcNow - File.GetLastWriteTimeUtc(saved) : TimeSpan.Zero;
            if (had && age < Refresh) return;
            // Già cercato di recente. I ".none" con "bimi" sono di prima dell'audit (solo BIMI, il dominio non era ancora noto): si riprova.
            var missing = MissingPath(domain);
            if (!had && File.Exists(missing) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missing) < Retry && File.ReadAllText(missing).Trim() == "tutto") return;
            var clock = Stopwatch.StartNew();
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var attempt = Attempt.Value = new FetchAttempt();
            string how = "BIMI verificato";
            var png = await BimiAsync(domain, cancel.Token);
            if (png == null)
            {
                how = "icona del sito";
                png = await SiteIconAsync(domain, cancel.Token);
            }
            if (png == null && attempt.Suspended) return;
            if (png == null && had && attempt.Unreachable && age < Refresh + Refresh)
            {
                // La rete o il sito non rispondono: non si sa, il logo resta e si riprova alla prossima sessione (fino a 60 giorni).
                Log.Write($"logo di {domain}: non riverificato (rete o sito non raggiungibili), resta quello salvato, {clock.ElapsedMilliseconds} ms");
                return;
            }
            if (png == null)
            {
                File.WriteAllText(missing, "tutto");
                Log.Write($"logo di {domain}: nessuno (né BIMI né sito){(had ? ", quello salvato esce" : "")}, {clock.ElapsedMilliseconds} ms");
                if (had)
                {
                    File.Delete(saved);
                    Arrived?.Invoke(domain);
                }
                return;
            }
            SafeFile.WriteCopy(PathOf(domain), png); // file temporaneo e rinomina (C-17): un logo scritto a metà non resta sul disco
            File.Delete(missing);
            Log.Write($"logo di {domain}: {how}, {clock.ElapsedMilliseconds} ms");
            Arrived?.Invoke(domain);
        }
        catch (Exception ex)
        {
            Log.Write($"logo di {domain} non trovato: {ex.Message}");
        }
        finally
        {
            Gate.Release();
        }
    }

    // ---------------------------------------------------------------- BIMI

    const string MarkUsage = "1.3.6.1.5.5.7.3.31"; // id-kp-BrandIndicatorforMessageIdentification

    /// <summary>
    /// Le autorità dei marchi riconosciute: le radici viste il 28 settembre 2026 nei certificati di Amazon, PayPal, eBay,
    /// Zalando, LinkedIn, Apple, Booking, Revolut e altri (DigiCert) e di x.com (SSL.com). Solo queste, non quelle di Windows.
    /// </summary>
    static readonly X509Certificate2[] MarkRoots =
    [
        // CN=DigiCert Verified Mark Root CA, SHA-256 504386C9EE8932FECC95FADE427F69C3E2534B7310489E300FEE448E33C46B42, fino al 2049
        X509CertificateLoader.LoadCertificate(Convert.FromBase64String(
            "MIIF3jCCA8agAwIBAgIQBsFnz+v0jTXWJBAYXhHF6zANBgkqhkiG9w0BAQsFADCBiDELMAkGA1UEBhMCVVMxDTALBgNVBAgTBFV0YWgxDTALBgNVBAcTBExlaGkxFzAVBgNVBAoTDkRpZ2lDZXJ0LCBJbmMuMRkwFwYDVQQLExB3d3cuZGlnaWNlcnQuY29tMScwJQYDVQQDEx5EaWdpQ2VydCBWZXJpZmllZCBNYXJrIFJvb3QgQ0EwHhcNMTkwOTIzMTIxMjA2WhcNNDkwOTIzMTIxMjA2WjCBiDELMAkGA1UEBhMCVVMxDTALBgNVBAgTBFV0YWgxDTALBgNVBAcTBExlaGkxFzAVBgNVBAoTDkRpZ2lDZXJ0LCBJbmMuMRkwFwYDVQQLExB3d3cuZGlnaWNlcnQuY29tMScwJQYDVQQDEx5EaWdpQ2VydCBWZXJpZmllZCBNYXJrIFJvb3QgQ0EwggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoICAQDawvvIO7cL04ptZxgLw/YwqDuluiFsMvGsr+vZcfq5c3hKuX0uMrslza91OFB6SPmbkG2hLErOcaVH0nMnG0RE3AM6dpfhw7qU+n3c6XPS7HlO9ZC57GJeaOXyb0cmcK2G96WC/VRuB1ZgjqYoq6PP4yjn/DB/Pc+7kjwJ2EDH5BFEnywVq4rH1a+QAbVDpxJfCfQZV1VKW+JNtO/KKKX+NlPrtHroSgKiRZ019oWptImyfgpg7j6FNNATR8uPsvU5zYJyCDOxKv4MqllMJmUVwGUHF61WnbiZeJsxzb5H5wMpikX4mfdKaIm0ym2QsHVRazST1bIVvAZThcKPd2EnysQi6XpYpMcpiSRo58ENXZW47M/Ocu7mBCLPTJEPEC9YG2aCfHxFSz/n6xZR+1rvNPUxcLZ+FNOwZRnHqcqe5TDNQewoC8/AWR0OdKqu2WgBF40ncXmtm5QnYhlTmBcoPUWfR40bCLJsm4fV2B4hkC5ZCHV/91jpsv7jhsGkpQpY6n9XWBABW6ZGQWM4jXxybbNmb3u21xx8rEkaIh22is08i41xeV9iLYecPup6npZnZbiKSOEFQ3WAwzi3TtABmRknOMybFJKSlJQXMfHqENfwKpNvMMRVO8PlJ+Oh6AN8l75vZaFF27gqBhbmjJ2Y9ioqTI7g+Dg4qClUQqXPCQIDAQABo0IwQDAdBgNVHQ4EFgQU7G8ipLME4sFjh+Z3Y+pGaU7u/OswDgYDVR0PAQH/BAQDAgGGMA8GA1UdEwEB/wQFMAMBAf8wDQYJKoZIhvcNAQELBQADggIBAC832YLVevVWINnr3vWCXNvLPtmPOPLKO5cHupQpkcug+IOli2FAxnC8JDlbOT6hiMK7MYaurag9QvDI/As04cNOa+4sqKCxQR3aLEyyqeLA4WdA6UFIHdMSIzLHZylzjuwciI706x83Ib17DMKOcpO2QVB7Beqv240TWxKxH21pFZsl44OgI+HcAPDbfJe3PEzwEZKNcKRkMWa/FFu2ckQxpTcfZABrarnuRLcSINiodSW7VfxctzegXWM4WmQeutPBOicceV3J4ZVkhthBm784vES1DIuDTqT9/iqStBGN8eOGx9qKvjaXT8SdcrP58FpXrtm/xKgtILptxfVT042oogQfb2cNahKRSvs0xH3jyhO944t0zMH/bEpRdU36wR1/Fo56zXy2Zv4czMwg3Hg7mbAalJvcnBvH+NHPgucQI432XX11K29vz7HuNC7P9yKhxns+MbOQDMDPOhtSLUpBmzRNG4+2BZJZyKGqYd+STHisEGYeYCi3MVrwSe2UqcDi9f2UAWVbkDE/YB6/e7+C7o6UWkXSU7dzR7FwFsfBHi6EqgIb2e9pINAxdvlc/3E19Ld/GJEtlw7nSdzp71eMp5Z48iY54fV2lM/rXogS1R4r3p2oPe9efG0XaJMd0v1gom5Da/khJA7+wjRB0wberd/tg3N0dJsSSznZjwYB")),
        // CN=SSL.com VMC RSA Root CA 2024, SHA-256 8F9D1B7698886782A599B48510651C66A1AA0C5CA3192097BDC68534154BD30D, fino al 2048
        X509CertificateLoader.LoadCertificate(Convert.FromBase64String(
            "MIIFiTCCA3GgAwIBAgIQcJRvrr6zzODYb7h2d4BhyzANBgkqhkiG9w0BAQsFADBOMQswCQYDVQQGEwJVUzEYMBYGA1UECgwPU1NMIENvcnBvcmF0aW9uMSUwIwYDVQQDDBxTU0wuY29tIFZNQyBSU0EgUm9vdCBDQSAyMDI0MB4XDTI0MDIxOTE2MjU1MVoXDTQ4MDIxMzE2MjU1MFowTjELMAkGA1UEBhMCVVMxGDAWBgNVBAoMD1NTTCBDb3Jwb3JhdGlvbjElMCMGA1UEAwwcU1NMLmNvbSBWTUMgUlNBIFJvb3QgQ0EgMjAyNDCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBAIyEglUdOfMiwwS13+Q9dho5swa3HbdPZByWNPwgD1tDdPYyCdox2F0bJxfrBnUFKCf46SZV7DESkn/YTKj7tMSkVr4c9GzNbr7WWSMprVEsgDrwDw/RM9YcwsFNlx1PFt+gDQhitA/ezIRO0uextBp40/HP7HQ7ZeOKVB0W71WoBty6TBCN+l5LVQGGDSvRGmQndf9zsmITQpddp1xAbUMAuxFnkS22vYCHdEWzSVuEWLakVh53qcYF7krDZY82kIurOC1wEwGNkdsfQ0HLpf8YVhHMOm/JoCOXcA79nvjehykZb+7c1zgPgUjRd4d/UbwoDBy0Y4GrGw4Q+c0bscz/xlrxf+SYIozASUEFjNcfvOkPCLlTHj7V8yhQRm58Q36/GdnahwLgdRwhk4WP5p7WtkxJrHIvdsQJgIH9OVL089MHfOHQHuAcxV54+/YKr/AT6m3pgr+Zs067rQYMMc/kTCGial39KMnkHydy8AduW+17U1auIpqt7XOtroC46IMbL2zVQDY1lyvUfvwc+5KtfjYVpnsle7g+fkEupWvbQHCWZR4iE9jFNR6ETF2damAMx+yuFP+dfyc4CAZUoGwz0+ZhUIYuTeEMCPSuZAvHy0ugTPEuSc5LKnJ5JK+bOeqtbRkQVmFC74fkAqhkCfztvkckHbTCI5ukoeKueCRJAgMBAAGjYzBhMA8GA1UdEwEB/wQFMAMBAf8wHwYDVR0jBBgwFoAUwTWi9FnZB3aX/croa3fWJq56WvswHQYDVR0OBBYEFME1ovRZ2Qd2l/3K6Gt31iauelr7MA4GA1UdDwEB/wQEAwIBhjANBgkqhkiG9w0BAQsFAAOCAgEAIet3+K2+sef6SZBE+5cFsnKYtagi50I1iECJQziusyw1wK9Keman4TZshwio4Z/nvaq2ed3s3YYFL6BGksJTJPImrEaQI3aqHa16AEzw6F2k3gz78wbcpjygGJMekVrhyF5lE+ErRcY9JGo1IGscBYlZcXkHJThNO9gEyoH+os73KIsY4120CBe3jbfOhRpDZioFwUxepdzEzx8ZRXGsz58m+0NS0cBcvkqpIpnI2TFDT/lK0s2ys65h2YLhDsUnhtJ2Kd8boP+ImazgpfpjgVEeCCpoPzWmmlMzxr083f0Mya4uSPidk/FkVYo3l+NAUcfHWcfH6ca7Xrm5DXsOaP0cDWaYphbEKVVi164V03fNClW3p7xzLCDUfzdAaQs1JY1Gh0GgH4dNdp+BgGjjUPZ86IZhCuOq0rm3czfxb25l6WuAf7A0gq4oVgMNbbIepap8YRbbU3nGl4A+s6qjtIhy6QGVWYqwQVElP7ba1sbhqLu5Uu/QyI/B5uEBO3oTvNgcpld8jFKTqIO9yFgzvIO6XhfwW5VvLHozlzb7fM8hPKnrdd5HT3XnFTkKl/y4zLrwOnvNhh5QMQe+HEnpScufX/cFPh568bVicV061IvLoGr84rlgxO+RjWuILOiXCVi07JsQLmpgD+ebpTit7HrVpCJa1SZXeHlzkc/yI74=")),
    ];

#if DEBUG
    /// <summary>Prove: una radice in più fra le autorità dei marchi, per i certificati fatti al volo.</summary>
    internal static X509Certificate2? TestMarkRoot;
#endif

    static async Task<byte[]?> BimiAsync(string domain, CancellationToken cancel)
    {
        var record = (await MailDns.TxtAsync($"default._bimi.{domain}"))
            .FirstOrDefault(t => t.TrimStart().StartsWith("v=BIMI1", StringComparison.OrdinalIgnoreCase));
        if (record == null) return null;
        var tags = record.Split(';').Select(p => p.Split('=', 2)).Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0].Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First()[1].Trim());
        // Senza certificato (a=) il logo non è verificato: Gmail non lo mostra, e nemmeno Missiva.
        if (!tags.TryGetValue("a", out var evidence) || evidence.Length == 0) return null;
        var pem = await DownloadAsync(evidence, 64 * 1024, cancel);
        if (pem == null) return null;
        // Il logo è quello dentro il certificato, che l'autorità ha controllato: l'SVG dell'indirizzo l= non si scarica più
        // (audit, S-17: chi falsifica il DNS metteva accanto al certificato vero di un marchio un'immagine sua).
        if (MarkLogo(Encoding.ASCII.GetString(pem.Data), domain) is not { } svg)
        {
            Log.Write($"logo di {domain}: il certificato del marchio non vale per il dominio o non ha il logo, ignorato");
            return null;
        }
        return await RenderSvgAsync(svg);
    }

    const string LogotypeOid = "1.3.6.1.5.5.7.1.12"; // id-pe-logotype (RFC 3709)

    /// <summary>
    /// Il logo del certificato del marchio, se il certificato vale: uso BIMI, il dominio fra i suoi nomi, catena fino a una
    /// delle MarkRoots (e nessun'altra, senza scaricare i certificati che mancano dagli indirizzi scritti nel certificato),
    /// valido oggi. Senza controllo di revoca: al più si vede il logo di un marchio a cui il certificato è stato tolto.
    /// </summary>
    static byte[]? MarkLogo(string pem, string domain)
    {
        var certs = Regex.Matches(pem, "-----BEGIN CERTIFICATE-----(.+?)-----END CERTIFICATE-----", RegexOptions.Singleline)
            .Take(6).Select(m => X509CertificateLoader.LoadCertificate(Convert.FromBase64String(Regex.Replace(m.Groups[1].Value, @"\s", ""))))
            .ToList();
        try
        {
            if (certs.Count == 0) return null;
            var leaf = certs[0];
            if (!leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == MarkUsage)))
                return null;
            if (!leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().SelectMany(e => e.EnumerateDnsNames())
                    .Any(n => n.Equals(domain, StringComparison.OrdinalIgnoreCase)))
                return null;
            using var chain = new X509Chain();
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(MarkRoots);
#if DEBUG
            if (TestMarkRoot is { } testRoot) chain.ChainPolicy.CustomTrustStore.Add(testRoot);
#endif
            chain.ChainPolicy.ExtraStore.AddRange(certs.Skip(1).ToArray());
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(MarkUsage));
            return chain.Build(leaf) ? EmbeddedLogo(leaf) : null;
        }
        finally
        {
            foreach (var c in certs) c.Dispose();
        }
    }

    /// <summary>
    /// L'SVG dentro il certificato del marchio (estensione logotype, subjectLogo: un indirizzo data: con l'SVG in base64,
    /// di solito gzip), al più 256 KB; null se non c'è o è scritto in un altro modo. Poi passa da LogoSvg come gli altri.
    /// </summary>
    static byte[]? EmbeddedLogo(X509Certificate2 leaf)
    {
        const string prefix = "data:image/svg+xml;base64,";
        try
        {
            if (leaf.Extensions[LogotypeOid] is not { } extension) return null;
            var logos = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            var subject = new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true);
            while (logos.HasData && logos.PeekTag() != subject) logos.ReadEncodedValue();
            if (!logos.HasData) return null;
            // subjectLogo [2] > direct [0] LogotypeData > image > la prima LogotypeImage > LogotypeDetails: tipo, impronte, indirizzi
            var details = logos.ReadSequence(subject).ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence().ReadSequence().ReadSequence();
            var type = details.ReadCharacterString(UniversalTagNumber.IA5String);
            details.ReadSequence();
            var address = details.ReadSequence().ReadCharacterString(UniversalTagNumber.IA5String);
            if (!type.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase) || !address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var bytes = Convert.FromBase64String(address[prefix.Length..]);
            if (bytes is not [0x1F, 0x8B, ..]) return bytes;
            using var packed = new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress);
            var buffer = new byte[256 * 1024 + 1];
            int length = packed.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return length < buffer.Length ? buffer[..length] : null;
        }
        catch (Exception ex) when (ex is AsnContentException or FormatException or InvalidDataException or CryptographicException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- icona del sito

    static async Task<byte[]?> SiteIconAsync(string domain, CancellationToken cancel)
    {
        foreach (var url in new[] { $"https://{domain}/apple-touch-icon.png", $"https://www.{domain}/apple-touch-icon.png" })
            if (await ImageAsync(url, 64, cancel) is { } png) return png;
        // Le icone dichiarate dalla pagina principale, dalla più grande (al più tre tentativi)
        foreach (var home in new[] { $"https://www.{domain}/", $"https://{domain}/" })
        {
            var page = await DownloadAsync(home, 768 * 1024, cancel);
            if (page == null) continue;
            foreach (var url in DeclaredIcons(Encoding.UTF8.GetString(page.Data), page.Url).Take(3))
                if (await ImageAsync(url, 32, cancel) is { } png) return png;
            break;
        }
        return await ImageAsync($"https://{domain}/favicon.ico", 32, cancel);
    }

    /// <summary>Le icone di link rel="icon" e "apple-touch-icon" della pagina, dalla più grande, solo https.</summary>
    internal static IEnumerable<string> DeclaredIcons(string html, Uri page)
    {
        // La pagina è di un sito qualunque: l'analisi sotto i tetti delle email (MailHtml.ParseGuard). Oltre, nessuna icona
        // dichiarata (resta favicon.ico): prima 1.000 <font> mai chiusi in 25 KB costavano secondi e un gigabyte.
        var icons = new List<(string Url, int Size)>();
        if (MailHtml.ParseWithinLimits(html) is not { } dom) return [];
        foreach (var link in dom.QuerySelectorAll("link[rel][href]"))
        {
            var rel = (link.GetAttribute("rel") ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool touch = rel.Any(r => r.StartsWith("apple-touch-icon"));
            if (!touch && !rel.Contains("icon")) continue;
            if (!Uri.TryCreate(page, link.GetAttribute("href"), out var url) || url.Scheme != Uri.UriSchemeHttps) continue;
            var sizes = (link.GetAttribute("sizes") ?? "").ToLowerInvariant();
            bool svg = url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || (link.GetAttribute("type") ?? "").Contains("svg");
            int size = sizes.Contains("any") || svg ? 256
                : Regex.Matches(sizes, @"(\d+)x(\d+)").Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(touch ? 180 : 32).Max();
            icons.Add((url.AbsoluteUri, size));
        }
        return icons.Where(i => i.Size >= 32).OrderByDescending(i => i.Size).Select(i => i.Url).Distinct();
    }

    static async Task<byte[]?> ImageAsync(string url, int minimum, CancellationToken cancel)
    {
        var file = await DownloadAsync(url, 1024 * 1024, cancel);
        if (file == null) return null;
        bool svg = file.Type.Contains("svg") || file.Url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
        if (!svg && (file.Type.StartsWith("text/") || file.Type.Contains("json"))) return null; // la pagina "non trovato" con 200
        return svg ? await RenderSvgAsync(file.Data) : await RenderRasterAsync(file.Data, minimum);
    }

    // ---------------------------------------------------------------- rete

    sealed record Download(byte[] Data, string Type, Uri Url);

    static HttpClient Http = CreateHttp();

#if DEBUG
    /// <summary>Prove (--prova-bozze): le richieste dei loghi a un gestore finto, al posto della rete.</summary>
    internal static void UseTestHandler(HttpMessageHandler? handler) => Http = handler == null ? CreateHttp() : Browser(new HttpClient(handler));
#endif

    /// <summary>Un User-Agent qualunque di browser: quello di prima diceva "Missiva" a ogni sito (audit, S-03).</summary>
    internal static HttpClient Browser(HttpClient http)
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        return http;
    }

    static HttpClient CreateHttp() => Browser(new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromSeconds(10) });

#if DEBUG
    /// <summary>Prove: a chi il gestore dei loghi sta per collegarsi ("nome:porta"); con il gancio non si collega a nessuno.</summary>
    internal static Action<string>? TestConnect;
#endif

    internal static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = true, // da https a http no: SocketsHttpHandler non lo segue
            MaxAutomaticRedirections = 3,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            // Mai dal proxy di sistema: il controllo qui sotto guarderebbe l'indirizzo del proxy, e il sito lo raggiungerebbe lui (audit, S-16).
            UseProxy = false,
            // Solo indirizzi pubblici: un dominio che punta alla rete di casa o al PC stesso non riceve nulla da Missiva.
            ConnectCallback = async (context, cancel) =>
            {
#if DEBUG
                if (TestConnect is { } probe)
                {
                    probe($"{context.DnsEndPoint.Host}:{context.DnsEndPoint.Port}");
                    throw new HttpRequestException("prova: nessun collegamento");
                }
#endif
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancel);
                var ip = addresses.FirstOrDefault(IsPublic) ?? throw new HttpRequestException($"{context.DnsEndPoint.Host} non ha indirizzi pubblici");
                var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), cancel);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }

    /// <summary>
    /// Un indirizzo di Internet, non della rete di casa, del PC o di una rete speciale. IPv4: fuori le reti private, locali,
    /// del gestore (100.64/10), di prova e di misura (192.0.0/24, 192.0.2/24, 198.18/15, 198.51.100/24, 203.0.113/24), da 224
    /// in su. IPv6: solo gli indirizzi globali (2000::/3), e dove dentro c'è un IPv4 vale quello: NAT64 (64:ff9b::/96, dove
    /// la rete traduce verso l'IPv4, anche privato) e 6to4 (2002::/16); mai Teredo né gli indirizzi di esempio (audit, S-16:
    /// prima 64:ff9b::c0a8:101, cioè 192.168.1.1, passava per pubblico).
    /// </summary>
    internal static bool IsPublic(IPAddress ip)
    {
        static bool PublicV4(ReadOnlySpan<byte> b) =>
            !(b[0] is 0 or 10 or 127 || b[0] >= 224 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
              || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
              || (b[0] == 198 && b[1] is 18 or 19) || (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113));
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork) return PublicV4(b);
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return false;
        if (b.AsSpan(0, 12).SequenceEqual<byte>([0, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0])) return PublicV4(b.AsSpan(12));
        if ((b[0] & 0xE0) != 0x20) return false;
        if (b[0] == 0x20 && b[1] == 0x02) return PublicV4(b.AsSpan(2));
        return !(b[0] == 0x20 && b[1] == 0x01 && ((b[2] == 0 && b[3] == 0) || (b[2] == 0x0D && b[3] == 0xB8)));
    }

    /// <summary>Un file via https, al più max byte; null se non c'è, non è https (anche dopo i reindirizzamenti) o è troppo grande.</summary>
    static async Task<Download?> DownloadAsync(string address, int max, CancellationToken cancel)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
            var final = response.RequestMessage?.RequestUri;
            if ((int)response.StatusCode is 429 or >= 500 && Attempt.Value is { } busy) busy.Unreachable = true;
            if (!response.IsSuccessStatusCode || final?.Scheme != Uri.UriSchemeHttps || response.Content.Headers.ContentLength > max) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancel);
            var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancel)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > max) return null;
            }
            return new Download(buffer.ToArray(), response.Content.Headers.ContentType?.MediaType ?? "", final);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (Attempt.Value is { } failed) failed.Unreachable = true;
            return null;
        }
    }

    // ---------------------------------------------------------------- disegno

    /// <summary>L'SVG passa il filtro (LogoSvg: lista bianca sull'albero, riferimenti solo interni, tetti).</summary>
    internal static bool SafeSvg(string svg) => LogoSvg.Clean(Encoding.UTF8.GetBytes(svg)) != null;

    /// <summary>
    /// A SharpVectors arriva il testo ripulito e riscritto da LogoSvg, mai i byte del sito (che potevano essere gzip o
    /// UTF-16 e dire altro da quello che il filtro leggeva), e con le risorse esterne vietate anche nelle sue impostazioni.
    /// Le due cose insieme: da sole le impostazioni non bastano (un use verso \\server\... lo cerca lo stesso).
    /// </summary>
    internal static Task<byte[]?> RenderSvgAsync(byte[] data) => OnStaAsync(() =>
    {
        if (LogoSvg.Clean(data) is not { } svg) return null;
        var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings
        {
            IncludeRuntime = false, TextAsGeometry = true, CanUseBitmap = false,
            ExternalResourcesAccessMode = SharpVectors.Dom.ExternalResourcesAccessModes.Ignore,
        };
        var reader = new SharpVectors.Converters.FileSvgReader(settings);
        using var text = new StringReader(svg);
        var drawing = reader.Read(text);
        if (drawing == null || drawing.Bounds.IsEmpty || drawing.Bounds.Width < 1 || drawing.Bounds.Height < 1) return null;
        var bounds = drawing.Bounds;
        return Compose(bounds.Size, (dc, target) =>
        {
            dc.PushTransform(new MatrixTransform(target.Width / bounds.Width, 0, 0, target.Height / bounds.Height,
                                                 target.X - bounds.X * target.Width / bounds.Width, target.Y - bounds.Y * target.Height / bounds.Height));
            dc.DrawDrawing(drawing);
            dc.Pop();
        });
    });

    /// <summary>Il lato più lungo che un'icona può dichiarare: oltre non si decodifica.</summary>
    const int MaxSide = 1024;

    /// <summary>
    /// PNG, JPEG, GIF, BMP, ICO: almeno minimum pixel di lato; dell'ICO l'immagine più grande. Il tipo si decide dalla
    /// firma dei byte e si usa il decodificatore di quel tipo, mai la scelta automatica di WIC (audit, S-02: un TIFF, un
    /// WebP o un DDS passavano dal codec installato, nel processo con la posta); le misure si leggono dall'intestazione
    /// prima di decodificare (un PNG di 250 KB che dichiara 16.000 pixel di lato chiedeva 245 MB).
    /// </summary>
    internal static Task<byte[]?> RenderRasterAsync(byte[] data, int minimum) => OnStaAsync(() =>
    {
        if (DeclaredSize(data) is not var (type, width, height) || width < 1 || height < 1 || width > MaxSide || height > MaxSide) return null;
        const BitmapCreateOptions options = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
        var stream = new MemoryStream(data, writable: false);
        BitmapDecoder decoder = type switch
        {
            "image/png" => new PngBitmapDecoder(stream, options, BitmapCacheOption.OnLoad),
            "image/jpeg" => new JpegBitmapDecoder(stream, options, BitmapCacheOption.OnLoad),
            "image/gif" => new GifBitmapDecoder(stream, options, BitmapCacheOption.OnLoad),
            "image/bmp" => new BmpBitmapDecoder(stream, options, BitmapCacheOption.OnLoad),
            _ => new IconBitmapDecoder(stream, options, BitmapCacheOption.OnLoad),
        };
        // Di un'animazione il primo fotogramma, senza toccare gli altri; dell'ICO tutte le immagini (al più IcoImages, già misurate).
        var frame = decoder is IconBitmapDecoder ? decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).FirstOrDefault() : decoder.Frames[0];
        if (frame == null || frame.PixelWidth > MaxSide || frame.PixelHeight > MaxSide || Math.Max(frame.PixelWidth, frame.PixelHeight) < minimum) return null;
        return Compose(new Size(frame.PixelWidth, frame.PixelHeight), (dc, target) => dc.DrawImage(frame, target));
    });

    const int IcoImages = 32;

    /// <summary>
    /// Il tipo (dalla firma) e le misure che l'immagine dichiara nell'intestazione, senza decodificare nulla; per l'ICO
    /// le misure più grandi fra le sue immagini. Null se non è uno dei cinque tipi o se l'intestazione non torna.
    /// </summary>
    internal static (string Type, int Width, int Height)? DeclaredSize(ReadOnlySpan<byte> data)
    {
        static int Big16(ReadOnlySpan<byte> b) => b[0] << 8 | b[1];
        static int Little16(ReadOnlySpan<byte> b) => b[0] | b[1] << 8;
        static long Little32(ReadOnlySpan<byte> b) => (uint)(b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24);
        static (int, int)? Png(ReadOnlySpan<byte> png) =>
            png.Length >= 24 && png[8..16].SequenceEqual<byte>([0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R']) && (png[16] | png[20]) < 0x80
                ? (png[16] << 24 | Big16(png[17..]) << 8 | png[19], png[20] << 24 | Big16(png[21..]) << 8 | png[23]) : null;
        // BITMAPINFOHEADER e seguenti (larghezza e altezza a 32 bit, l'altezza negativa dall'alto in basso), o il vecchio di 12 byte.
        static (int, int)? Dib(ReadOnlySpan<byte> dib)
        {
            if (dib.Length < 12) return null;
            long size = Little32(dib);
            if (size == 12) return (Little16(dib[4..]), Little16(dib[6..]));
            if (size < 40 || dib.Length < 12) return null;
            long width = (int)Little32(dib[4..]), height = Math.Abs((long)(int)Little32(dib[8..]));
            return width is > 0 and <= 0x10000 && height <= 0x10000 ? ((int)width, (int)height) : null;
        }

        switch (EditorImages.TypeOf(data.Length >= 8 ? data[..8].ToArray() : []))
        {
            case "image/png":
                return Png(data) is var (w, h) ? ("image/png", w, h) : null;
            case "image/gif":
            {
                // Schermo logico, tavola dei colori, estensioni, poi la prima immagine: contano le misure più grandi fra i due.
                if (data.Length < 13) return null;
                int width = Little16(data[6..]), height = Little16(data[8..]), at = 13 + ((data[10] & 0x80) != 0 ? 3 << ((data[10] & 7) + 1) : 0);
                while (at + 1 < data.Length && data[at] == 0x21)
                {
                    at += 2;
                    while (at < data.Length && data[at] != 0) at += data[at] + 1;
                    at++;
                }
                if (at + 9 >= data.Length || data[at] != 0x2C) return null;
                return ("image/gif", Math.Max(width, Little16(data[(at + 5)..])), Math.Max(height, Little16(data[(at + 7)..])));
            }
            case "image/bmp":
                return data.Length >= 26 && Dib(data[14..]) is var (bw, bh) ? ("image/bmp", bw, bh) : null;
            case "image/jpeg":
            {
                // I segmenti fino al primo SOF (C0-CF tranne C4, C8, CC): altezza e larghezza.
                for (int at = 2; at + 9 < data.Length;)
                {
                    if (data[at] != 0xFF) return null;
                    byte marker = data[at + 1];
                    if (marker == 0xFF)
                    {
                        at++;
                        continue;
                    }
                    if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC)) return ("image/jpeg", Big16(data[(at + 7)..]), Big16(data[(at + 5)..]));
                    if (marker is 0xD9 or 0xDA) return null;
                    at += 2 + (marker is 0x01 or (>= 0xD0 and <= 0xD8) ? 0 : Big16(data[(at + 2)..]));
                }
                return null;
            }
        }
        if (data.Length < 22 || !data[..4].SequenceEqual<byte>([0, 0, 1, 0])) return null;
        // ICO: ogni immagine sta nel file ed è un PNG o un bitmap (alto il doppio, con la maschera), con le sue misure vere.
        int count = Little16(data[4..]), widest = 0, tallest = 0;
        if (count is < 1 or > IcoImages || data.Length < 6 + count * 16) return null;
        for (int i = 0; i < count; i++)
        {
            var entry = data.Slice(6 + i * 16, 16);
            long length = Little32(entry[8..]), offset = Little32(entry[12..]);
            if (length < 12 || offset < 6 + count * 16 || offset + length > data.Length) return null;
            var image = data.Slice((int)offset, (int)length);
            var size = image.Length >= 8 && image[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? Png(image)
                     : Dib(image) is var (dw, dh) ? (dw, dh / 2) : null;
            if (size is not var (iw, ih)) return null;
            (widest, tallest) = (Math.Max(widest, iw), Math.Max(tallest, ih));
        }
        return ("image/x-icon", widest, tallest);
    }

    /// <summary>
    /// Il logo in un quadrato di Pixels: intero se è pieno fino agli angoli (le icone con il loro sfondo, i loghi BIMI),
    /// con un margine se gli angoli sono trasparenti (un simbolo da solo, che nel cerchio si taglierebbe). Null se è tutto
    /// di un colore (le icone segnaposto).
    /// </summary>
    static byte[]? Compose(Size size, Action<DrawingContext, Rect> draw)
    {
        RenderTargetBitmap Render(double margin)
        {
            double side = Pixels - 2 * margin, scale = Math.Min(side / size.Width, side / size.Height);
            var target = new Rect((Pixels - size.Width * scale) / 2, (Pixels - size.Height * scale) / 2, size.Width * scale, size.Height * scale);
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen()) draw(dc, target);
            var bitmap = new RenderTargetBitmap(Pixels, Pixels, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }
        var image = Render(0);
        var pixels = new byte[Pixels * Pixels * 4];
        image.CopyPixels(pixels, Pixels * 4, 0);
        int At(int x, int y) => (y * Pixels + x) * 4;
        bool transparentCorners = new[] { At(2, 2), At(Pixels - 3, 2), At(2, Pixels - 3), At(Pixels - 3, Pixels - 3) }.All(i => pixels[i + 3] < 200);
        int same = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (Math.Abs(pixels[i] - pixels[0]) + Math.Abs(pixels[i + 1] - pixels[1]) + Math.Abs(pixels[i + 2] - pixels[2]) + Math.Abs(pixels[i + 3] - pixels[3]) < 24) same++;
        if (same > Pixels * Pixels * 0.97) return null;
        if (transparentCorners) image = Render(Pixels * 0.14);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>Disegni che non si sono fermati nemmeno dopo il doppio del tempo e girano ancora (OnStaAsync).</summary>
    static int unstopped;

    /// <summary>
    /// Un disegno non si è fermato e il suo thread gira ancora: finché c'è, nessun altro disegno parte e nessun logo si
    /// cerca (audit dell'11 ottobre, T-07). ControlledExecution non interrompe il codice nativo né un blocco finally: prima
    /// dopo il doppio del tempo si smetteva di aspettare e basta, senza una riga nel registro, e i disegni dopo partivano
    /// lo stesso, ognuno con il suo thread e la sua memoria.
    /// </summary>
    internal static bool DrawingSuspended => Volatile.Read(ref unstopped) > 0;

    /// <summary>
    /// Il disegno di WPF in un thread STA suo; null (e una riga nel registro) se non riesce. Dopo seconds secondi il
    /// thread si ferma davvero (ControlledExecution): prima restava a girare per sempre, e SharpVectors con certi stili
    /// non finisce mai e chiede 1 GB al secondo (docs\audit\ESITO.md, S-01). Il thread è solo del disegno, non tiene
    /// nulla di Missiva; se non si ferma (codice nativo), dopo il doppio del tempo non lo si aspetta più, lo si dice nel
    /// registro e i disegni restano sospesi finché gira (<see cref="DrawingSuspended"/>).
    /// </summary>
    internal static async Task<byte[]?> OnStaAsync(Func<byte[]?> work, int seconds = 5)
    {
        if (DrawingSuspended)
        {
            if (Attempt.Value is { } attempt) attempt.Suspended = true;
            return null;
        }
        var done = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new CancellationTokenSource();
        var thread = new Thread(() =>
        {
            byte[]? result = null;
            try
            {
#pragma warning disable SYSLIB0046 // sconsigliato in generale: qui il codice fermato è solo il disegno di un logo, in un thread suo
                System.Runtime.ControlledExecution.Run(() => result = work(), stop.Token);
#pragma warning restore SYSLIB0046
            }
            catch (OperationCanceledException)
            {
                Log.Write($"logo non disegnato: fermato dopo {seconds} secondi");
            }
            catch (Exception ex)
            {
                Log.Write($"logo non disegnato: {ex.GetType().Name}: {ex.Message}");
            }
            done.SetResult(result);
        }) { IsBackground = true, Name = "logo" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        stop.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { return await done.Task.WaitAsync(TimeSpan.FromSeconds(seconds * 2)); }
        catch (TimeoutException)
        {
            // Non si è fermato: si conta finché gira. Se un giorno finisce, i disegni riprendono.
            Interlocked.Increment(ref unstopped);
            Log.Write($"logo non disegnato: il disegno non si è fermato dopo {seconds * 2} secondi e il suo thread gira ancora; finché c'è, niente altri loghi");
            _ = done.Task.ContinueWith(_ =>
            {
                Interlocked.Decrement(ref unstopped);
                Log.Write("logo: il disegno che non si fermava è finito, i loghi riprendono");
            }, TaskScheduler.Default);
            return null;
        }
        finally { _ = done.Task.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default); }
    }
}
