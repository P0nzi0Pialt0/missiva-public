using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Missiva;

/// <summary>
/// Gli aggiornamenti da GitHub (15 ottobre 2026, richiesta dell'autore): una Missiva pubblicata come rilascio chiede a
/// GitHub l'ultimo rilascio del suo repository, e se è una versione più nuova lo offre in una barra; con Aggiorna lo
/// scarica, lo controlla e, chiusa Missiva, il nuovo eseguibile si mette al posto di quello che girava.
/// Chi si aggiorna: solo i rilasci, cioè gli eseguibili compilati da privato\rilascio.ps1, che scrive nell'eseguibile il
/// repository dei rilasci (MissivaAggiornamenti, un AssemblyMetadata); una Missiva compilata dai sorgenti non lo ha, e
/// non chiede nulla a nessuno. Che cosa si accetta: solo un manifesto (aggiornamento.json) firmato con la chiave di
/// rilascio dell'autore (UpdateKey), per Missiva, con una versione più alta di questa; uno zip con la dimensione e
/// l'impronta del manifesto, con dentro solo i cinque nomi attesi; un eseguibile che dice, nella sua versione di file,
/// quella del manifesto. Chi entrasse nell'account GitHub senza la chiave non può far accettare nulla. La rete: solo
/// https, solo verso gli host di GitHub (i reindirizzamenti compresi), con un tetto per ogni risposta. Tutto il formato
/// di GitHub (la risposta di releases/latest, dove portano i link dei file) è scritto dalla sua documentazione: le prove
/// lo simulano con un gestore HTTP finto, dal vero non è mai stato visto.
/// </summary>
public static partial class Updates
{
    // ---------------------------------------------------------------- chi si aggiorna

    /// <summary>Il nome della proprietà di MSBuild e dell'AssemblyMetadata con il repository dei rilasci ("proprietario/nome").</summary>
    internal const string MetadataKey = "MissivaAggiornamenti";

    /// <summary>Il repository dei rilasci scritto nell'eseguibile da rilascio.ps1, o null (compilata dai sorgenti).</summary>
    internal static string? Repository =>
        RepositoryFrom(typeof(Updates).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == MetadataKey)?.Value);

    /// <summary>Un repository di GitHub scritto come "proprietario/nome", o null se è fatto in un altro modo.</summary>
    internal static string? RepositoryFrom(string? value) => value is { } text && RepositoryName().IsMatch(text) && !text.Contains("..") ? text : null;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepositoryName();

    /// <summary>Che cosa sa fare questa Missiva: niente (dai sorgenti), niente per la chiave che manca, o controllare.</summary>
    internal enum Mode { FromSources, NoKey, On }

    internal static Mode StateOf(string? repository, string publicKey)
    {
        if (RepositoryFrom(repository) == null) return Mode.FromSources;
        using var key = KeyFrom(publicKey);
        return key == null ? Mode.NoKey : Mode.On;
    }

    /// <summary>Questa Missiva: dal repository nell'eseguibile e dalla chiave nel codice.</summary>
    internal static Mode Current => StateOf(Repository, UpdateKey.PublicKey);

    /// <summary>Il numero di questa Missiva come versione ("0.3.525"), 0.0.0 se non si legge.</summary>
    internal static Version CurrentNumber => Version.TryParse(AppVersion.Number, out var number) ? number : new Version(0, 0, 0);

    /// <summary>
    /// Quello che serve a privato\rilascio.ps1 per controllare l'eseguibile appena compilato senza aprirlo
    /// (Missiva.exe --dati-aggiornamenti file): il numero, il repository e la chiave pubblica che porta.
    /// </summary>
    internal static string Describe() =>
        $"versione={AppVersion.Number}\nrepository={Repository ?? ""}\nchiave={UpdateKey.PublicKey}\n";

    // ---------------------------------------------------------------- firma

    /// <summary>
    /// La chiave pubblica (base64 di SubjectPublicKeyInfo) se è una chiave ECDSA sulla curva P-256 e nient'altro, o null.
    /// Una chiave vuota, rovinata o di un altro tipo spegne gli aggiornamenti.
    /// </summary>
    internal static ECDsa? KeyFrom(string publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey)) return null;
        ECDsa? key = null;
        try
        {
            var bytes = Convert.FromBase64String(publicKey.Trim());
            key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out int read);
            var curve = key.ExportParameters(false).Curve;
            bool p256 = key.KeySize == 256 && curve.IsNamed && (curve.Oid.Value == "1.2.840.10045.3.1.7" || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256");
            if (read == bytes.Length && p256) return key;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
        }
        key?.Dispose();
        return null;
    }

    /// <summary>
    /// La firma del manifesto: il testo di aggiornamento.json.firma è il base64 dei 64 byte di una firma ECDSA P-256 su
    /// SHA-256 dei byte del manifesto, nel formato IEEE P1363 (r e s di 32 byte, uno dopo l'altro), quello di
    /// ECDsa.SignData di .NET che usa rilascio.ps1. Vera solo se è stata fatta con la chiave privata di questa chiave pubblica.
    /// </summary>
    internal static bool SignatureValid(byte[] manifest, string signature, string publicKey)
    {
        using var key = KeyFrom(publicKey);
        if (key == null) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(signature.Trim()); }
        catch (FormatException) { return false; }
        return bytes.Length == 64 && key.VerifyData(manifest, bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    // ---------------------------------------------------------------- manifesto

    /// <summary>I nomi dei file di un rilascio su GitHub, accanto allo zip.</summary>
    internal const string ManifestName = "aggiornamento.json", SignatureName = "aggiornamento.json.firma";

    /// <summary>I soli nomi che lo zip di un rilascio contiene, ognuno una volta, senza cartelle.</summary>
    internal static readonly string[] PackageFiles = ["Missiva.exe", "installa.ps1", "disinstalla.ps1", "LICENSE", "TERZE-PARTI.md"];

    /// <summary>I file che il nuovo eseguibile copia accanto a quello installato, oltre a sé stesso.</summary>
    internal static readonly string[] BesideFiles = ["disinstalla.ps1", "LICENSE", "TERZE-PARTI.md"];

    /// <summary>Che cosa dice un manifesto firmato.</summary>
    /// <param name="Number">Il numero, come in App installate: 0.3.530.</param>
    /// <param name="Text">Il testo della versione, come in Informazioni: "0.3.530 del 15/10/2026 (1a2b3c4)".</param>
    /// <param name="FileName">Il nome dello zip fra i file del rilascio: sempre "Missiva-numero.zip".</param>
    /// <param name="Sha256">L'impronta SHA-256 dello zip, in esadecimale minuscolo.</param>
    /// <param name="NewsItalian">Le novità in italiano, una riga per punto (il campo "novita" del manifesto).</param>
    /// <param name="NewsEnglish">
    /// Le novità in inglese (il campo "news", facoltativo, dal 21 ottobre 2026: rilascio.ps1 -News), o null. Le Missiva di
    /// prima leggono il manifesto con JsonDocument e TryGetProperty dei soli campi che conoscono: il campo in più non lo
    /// vedono, e il manifesto vale per loro come senza (provato nel gruppo lingua-servizio).
    /// </param>
    public sealed record Manifest(Version Number, string Text, DateOnly Date, string Commit, string FileName, long Size, string Sha256, IReadOnlyList<string> NewsItalian,
                                  IReadOnlyList<string>? NewsEnglish = null)
    {
        /// <summary>"Missiva 0.3.530 del 15/10/2026", per la barra; in inglese "Missiva 0.3.530 (15/10/2026)" con la data della cultura.</summary>
        public string Title => T("Missiva {0} del {1}", Number, Lingua.English ? Date.ToString("d", Lingua.Culture) : Date.ToString("dd'/'MM'/'yyyy", Lingua.Culture));

        /// <summary>
        /// Le novità da mostrare, nella lingua di Missiva (le legge il pannellino dell'aggiornamento, MainWindow.UpdateNews):
        /// in inglese quelle inglesi se il rilascio le ha, altrimenti quelle italiane; in italiano sempre quelle italiane.
        /// </summary>
        public IReadOnlyList<string> News => Lingua.English && NewsEnglish is { Count: > 0 } english ? english : NewsItalian;
    }

    /// <summary>Un aggiornamento rifiutato, con il motivo: va nel registro, e nella barra se l'ha chiesto chi usa Missiva.</summary>
    public sealed class Refused(string reason) : Exception(reason);

    /// <summary>Il tetto delle risposte di GitHub: quella di releases/latest, il manifesto, la firma, lo zip e i file al suo interno.</summary>
    internal const int MaxReleaseBytes = 1024 * 1024, MaxManifestBytes = 64 * 1024, MaxSignatureBytes = 1024;
    internal const long MaxZipBytes = 512L * 1024 * 1024, MaxExeBytes = 768L * 1024 * 1024, MaxTextBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Il manifesto già firmato, letto e controllato: prodotto Missiva, numero più alto di current, data, commit, nome
    /// dello zip, dimensione, impronta, novità. Solleva Refused con il motivo.
    /// </summary>
    internal static Manifest ReadManifest(byte[] bytes, Version current)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException) { throw new Refused(T("il manifesto non è JSON")); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new Refused(T("il manifesto non è un oggetto JSON"));
            string Text(string name, int max)
            {
                if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw new Refused(Lingua.T("nel manifesto manca \"{0}\"", name));
                var found = value.GetString()!;
                if (found.Length == 0 || found.Length > max || found.Any(char.IsControl)) throw new Refused(Lingua.T("nel manifesto \"{0}\" non è come deve essere", name));
                return found;
            }
            var product = Text("prodotto", 40);
            if (product != "Missiva") throw new Refused(T("il manifesto è di un altro prodotto ({0})", product));
            var numberText = Text("numero", 40);
            if (!NumberPattern().IsMatch(numberText) || !Version.TryParse(numberText, out var number)) throw new Refused(T("nel manifesto il numero non si legge ({0})", numberText));
            if (number <= current) throw new Refused(T("la versione {0} non è più nuova di questa ({1})", number, current));
            var text = Text("versione", 200);
            if (text != numberText && !text.StartsWith(numberText + " ", StringComparison.Ordinal)) throw new Refused(T("nel manifesto il testo della versione non comincia con il numero"));
            var dateText = Text("data", 10);
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
                throw new Refused(T("nel manifesto la data non si legge ({0})", dateText));
            var commit = Text("commit", 40);
            if (!CommitPattern().IsMatch(commit)) throw new Refused(T("nel manifesto il commit non è come deve essere"));
            var file = Text("file", 80);
            if (file != $"Missiva-{number}.zip") throw new Refused(T("nel manifesto il nome dello zip non è quello atteso ({0})", file));
            if (!root.TryGetProperty("dimensione", out var sizeValue) || sizeValue.ValueKind != JsonValueKind.Number || !sizeValue.TryGetInt64(out var size) || size <= 0 || size > MaxZipBytes)
                throw new Refused(T("nel manifesto la dimensione dello zip manca o è fuori misura"));
            var sha256 = Text("sha256", 64);
            if (!Sha256Pattern().IsMatch(sha256)) throw new Refused(T("nel manifesto l'impronta SHA-256 non è come deve essere"));
            // Le novità, in italiano ("novita") e, dal 21 ottobre 2026, in inglese ("news", facoltative): con le stesse regole.
            List<string>? Lines(string name)
            {
                if (!root.TryGetProperty(name, out var newsValue)) return null;
                if (newsValue.ValueKind != JsonValueKind.Array || newsValue.GetArrayLength() > 40) throw new Refused(Lingua.T("nel manifesto le novità non sono un elenco"));
                var lines = new List<string>();
                foreach (var line in newsValue.EnumerateArray())
                {
                    if (line.ValueKind != JsonValueKind.String || line.GetString() is not { Length: > 0 and <= 600 } item || item.Any(char.IsControl))
                        throw new Refused(Lingua.T("nel manifesto una riga delle novità non è come deve essere"));
                    lines.Add(item);
                }
                return lines;
            }
            return new Manifest(number, text, date, commit, file, size, sha256, Lines("novita") ?? [], Lines("news"));
        }
    }

    [GeneratedRegex(@"^\d{1,9}\.\d{1,9}\.\d{1,9}$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^[0-9a-f]{7,40}$")]
    private static partial Regex CommitPattern();

    [GeneratedRegex(@"^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    // ---------------------------------------------------------------- rete

    /// <summary>
    /// Gli host di GitHub a cui Missiva chiede qualcosa, anche dopo un reindirizzamento: l'API dei rilasci, la pagina dei
    /// file di un rilascio (github.com/.../releases/download/...) e i due host dove quella rimanda ai file veri, che sono
    /// quelli che si conoscono dalla documentazione e dall'uso di GitHub fino al 2025 (objects.githubusercontent.com, poi
    /// release-assets.githubusercontent.com). Mai visti dal vero da Missiva: se GitHub ne usasse un altro, lo scaricamento
    /// si fermerebbe con "indirizzo non ammesso" nel registro, e andrebbe aggiunto qui. I file sono comunque firmati e
    /// confrontati con l'impronta: questo elenco è una difesa in più.
    /// </summary>
    internal static readonly string[] Hosts = ["api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    const int MaxRedirects = 5;
    /// <summary>Le richieste piccole (rilascio, manifesto, firma) finiscono entro questo tempo; lo zip si ferma se resta muto per questo tempo.</summary>
    static readonly TimeSpan SmallTimeout = TimeSpan.FromSeconds(30), SilenceTimeout = TimeSpan.FromSeconds(60);

    static HttpClient? http;

    /// <summary>
    /// Il client degli aggiornamenti: senza reindirizzamenti automatici (li segue Missiva, guardando dove portano), senza
    /// cookie, con il proxy di sistema (GitHub è uno solo e noto, e dietro un proxy di un'azienda deve poter passare).
    /// </summary>
    internal static HttpClient Http => http ??= Client(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
    });

    /// <summary>Un client sopra un gestore (quello vero, o quello finto delle prove). GitHub vuole un User-Agent: dice solo "Missiva".</summary>
    internal static HttpClient Client(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Missiva");
        return client;
    }

    /// <summary>Un indirizzo a cui Missiva può chiedere: https, porta normale, senza nome e password, verso uno degli Hosts.</summary>
    internal static bool Allowed(Uri url) =>
        url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0 && Hosts.Contains(url.IdnHost.ToLowerInvariant());

    /// <summary>
    /// Una GET verso GitHub, con i reindirizzamenti seguiti a mano: ognuno deve portare a un indirizzo ammesso (Allowed),
    /// al più cinque. Prima di ogni richiesta, non dopo: verso un indirizzo non ammesso non parte niente.
    /// </summary>
    static async Task<HttpResponseMessage> GetAsync(HttpClient client, Uri url, string accept, CancellationToken cancel)
    {
        for (int hop = 0; ; hop++)
        {
            if (!Allowed(url)) throw new Refused(T("indirizzo non ammesso: {0} (solo https verso GitHub)", Shown(url)));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd(accept);
            if (url.IdnHost == "api.github.com") request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location == null) throw new HttpRequestException(T("{0} rimanda altrove senza dire dove", Shown(url)));
            if (hop >= MaxRedirects) throw new Refused(T("troppi reindirizzamenti da {0}", Shown(url)));
            url = location.IsAbsoluteUri ? location : new Uri(url, location);
        }
    }

    /// <summary>Un indirizzo per il registro: schema, host e percorso, senza la query (che per i file di GitHub porta una firma temporanea).</summary>
    internal static string Shown(Uri url) => url.IsAbsoluteUri ? $"{url.Scheme}://{url.Host}{url.AbsolutePath}" : url.OriginalString;

    /// <summary>Una risposta piccola, letta tutta, fino a limit byte: oltre si ferma lì e la rifiuta.</summary>
    /// <param name="what">Che cosa si legge, per i motivi ("rilascio", "manifesto", "firma"): segnato con N da chi chiama, tradotto qui.</param>
    static async Task<byte[]> ReadSmallAsync(HttpClient client, Uri url, string accept, int limit, string what, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(SmallTimeout);
        what = T(what);
        try
        {
            using var response = await GetAsync(client, url, accept, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException(T("{0}: risposta {1} da {2}", what, (int)response.StatusCode, Shown(url)));
            if (response.Content.Headers.ContentLength > limit) throw new Refused(T("{0}: {1} byte, più del tetto di {2}", what, response.Content.Headers.ContentLength, limit));
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > limit) throw new Refused(T("{0}: più del tetto di {1} byte, lettura fermata lì", what, limit));
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new HttpRequestException(T("{0}: nessuna risposta da {1} entro {2:0} secondi", what, Shown(url), SmallTimeout.TotalSeconds));
        }
    }

    // ---------------------------------------------------------------- controllo

    /// <summary>Un aggiornamento da offrire: il manifesto firmato e l'indirizzo del suo zip.</summary>
    public sealed record Offer(Manifest Manifest, Uri ZipUrl);

    public enum CheckKind { Off, UpToDate, Offer, Refused, Failed }

    /// <summary>L'esito di un controllo: che cosa è successo, l'aggiornamento se ce n'è uno, e la riga per Informazioni.</summary>
    public sealed record CheckOutcome(CheckKind Kind, Offer? Offer, string Line);

    /// <summary>
    /// Il controllo automatico (due minuti dopo l'avvio, poi una volta al giorno): con la casella "Controlla da solo gli
    /// aggiornamenti" spenta non parte nessuna richiesta.
    /// </summary>
    internal static Task<CheckOutcome> AutomaticAsync(bool enabled, string? repository, string publicKey, Version current, HttpClient client, CancellationToken cancel = default) =>
        enabled ? CheckAsync(repository, publicKey, current, client, cancel)
                : Task.FromResult(new CheckOutcome(CheckKind.Off, null, T("Il controllo automatico è spento.")));

    /// <summary>
    /// Il controllo: l'ultimo rilascio del repository (api.github.com, releases/latest), poi, se la sua etichetta non dice
    /// già una versione uguale o più vecchia di questa, il manifesto e la firma. Senza repository o senza chiave nessuna
    /// richiesta. Un rilascio offerto ha il manifesto firmato con la chiave, per Missiva, con un numero più alto di current.
    /// Ogni esito che non è "aggiornata" va nel registro, con il motivo; a schermo niente (lo decide chi chiama).
    /// </summary>
    internal static async Task<CheckOutcome> CheckAsync(string? repository, string publicKey, Version current, HttpClient client, CancellationToken cancel = default)
    {
        switch (StateOf(repository, publicKey))
        {
            case Mode.FromSources: return new(CheckKind.Off, null, T("Compilata dai sorgenti: non controlla gli aggiornamenti."));
            case Mode.NoKey: return new(CheckKind.Off, null, T("Senza la chiave degli aggiornamenti: non li controlla."));
        }
        string? tag = null;
        try
        {
            var release = await ReadSmallAsync(client, new Uri($"https://api.github.com/repos/{repository}/releases/latest"), "application/vnd.github+json", MaxReleaseBytes, N("rilascio"), cancel);
            var (releaseTag, files) = ReadRelease(release);
            tag = releaseTag;
            // L'etichetta non è firmata: serve solo a non scaricare manifesto e firma quando dice già che non c'è nulla di nuovo.
            if (tag is ['v', .. var tagText] && NumberPattern().IsMatch(tagText) && Version.Parse(tagText) <= current)
                return new(CheckKind.UpToDate, null, T("Missiva è aggiornata: l'ultimo rilascio è {0}.", tagText));
            Uri Asset(string name) => files.TryGetValue(name, out var url) ? url : throw new Refused(T("nel rilascio {0} manca {1}", tag, name));
            var manifest = await ReadSmallAsync(client, Asset(ManifestName), "application/octet-stream", MaxManifestBytes, N("manifesto"), cancel);
            var signature = await ReadSmallAsync(client, Asset(SignatureName), "application/octet-stream", MaxSignatureBytes, N("firma"), cancel);
            if (!SignatureValid(manifest, Encoding.ASCII.GetString(signature), publicKey)) throw new Refused(T("la firma del manifesto non è valida per la chiave di Missiva"));
            var read = ReadManifest(manifest, current);
            var offer = new Offer(read, Asset(read.FileName));
            Log.Write($"aggiornamenti: disponibile {read.Text} (rilascio {tag}, {read.Size} byte)");
            return new(CheckKind.Offer, offer, T("È disponibile {0}.", read.Title));
        }
        catch (Refused ex)
        {
            Log.Write($"aggiornamenti: rilascio {tag ?? "(nessuno)"} rifiutato: {ex.Message}");
            return new(CheckKind.Refused, null, T("L'ultimo rilascio non è stato accettato: {0}.", ex.Message));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !cancel.IsCancellationRequested)
        {
            Log.Write($"aggiornamenti: controllo non riuscito ({ex.GetType().Name}: {ex.Message})");
            return new(CheckKind.Failed, null, T("Non è stato possibile controllare: {0}", ex.Message));
        }
    }

    /// <summary>
    /// La risposta di releases/latest (dalla documentazione dell'API di GitHub, mai vista dal vero): l'etichetta
    /// ("tag_name") e i file ("assets": "name" e "browser_download_url"). Solo i file con un nome e un indirizzo ammesso.
    /// </summary>
    internal static (string Tag, Dictionary<string, Uri> Files) ReadRelease(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            var files = new Dictionary<string, Uri>(StringComparer.Ordinal);
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var asset in assets.EnumerateArray())
                    if (asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } name
                        && asset.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String
                        && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var url) && Allowed(url))
                        files.TryAdd(name, url);
            return (tag.Length <= 60 ? tag : tag[..60], files);
        }
        catch (JsonException)
        {
            throw new Refused(T("la risposta di GitHub sul rilascio non è JSON"));
        }
    }

    /// <summary>La barra di un aggiornamento chiusa con la X non torna per quella versione fino al giorno dopo.</summary>
    internal static bool Shows(Manifest manifest, string? dismissed, DateTime? dismissedOn, DateTime today) =>
        !(dismissed == manifest.Number.ToString() && dismissedOn?.Date == today.Date);

    // ---------------------------------------------------------------- scaricamento

    /// <summary>La cartella degli aggiornamenti scaricati, nella cartella dei dati.</summary>
    internal const string FolderName = "aggiornamento";
    /// <summary>Scritto da Missiva prima di passare la mano al nuovo eseguibile; lo legge l'avvio dopo (AfterStart).</summary>
    internal const string PendingName = "in-corso.json";
    /// <summary>Scritto dal nuovo eseguibile accanto a sé: com'è andata la sostituzione.</summary>
    internal const string OutcomeName = "esito.txt";

    /// <summary>Un aggiornamento scaricato e controllato, pronto in una sua cartella: il nuovo eseguibile e gli altri file dello zip.</summary>
    public sealed record Prepared(Manifest Manifest, string Folder, string Exe);

    /// <summary>
    /// Scarica lo zip di un aggiornamento in root (una sottocartella con il suo numero) e lo controlla: dimensione e
    /// impronta del manifesto, solo i nomi attesi, l'eseguibile con la versione del manifesto. Lo zip si estrae e si
    /// cancella. Qualunque cosa vada storta, la cartella si toglie (nessun file lasciato) e Refused o l'errore di rete
    /// salgono a chi chiama, dopo una riga nel registro.
    /// </summary>
    /// <param name="progress">I byte arrivati, man mano.</param>
    internal static async Task<Prepared> DownloadAsync(Offer offer, Version current, string root, HttpClient client, IProgress<long>? progress = null, CancellationToken cancel = default)
    {
        var manifest = offer.Manifest;
        var folder = Path.Combine(root, manifest.Number.ToString());
        try
        {
            if (manifest.Number <= current) throw new Refused(T("la versione {0} non è più nuova di questa ({1})", manifest.Number, current));
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var zip = Path.Combine(folder, manifest.FileName);
            await SaveZipAsync(offer, zip, client, progress, cancel);
            Extract(zip, folder);
            File.Delete(zip);
            var exe = Path.Combine(folder, "Missiva.exe");
            var info = FileVersionInfo.GetVersionInfo(exe);
            var inFile = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            if (inFile != manifest.Number || info.ProductName != "Missiva")
                throw new Refused(T("l'eseguibile nello zip dice {0} {1}, il manifesto Missiva {2}", info.ProductName, inFile, manifest.Number));
            Log.Write($"aggiornamenti: scaricato e controllato {manifest.Text} ({manifest.Size} byte, impronta e firma giuste)");
            return new Prepared(manifest, folder, exe);
        }
        catch (Exception ex)
        {
            Remove(folder);
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Remove(root);
            Log.Write(ex switch
            {
                Refused => $"aggiornamenti: {manifest.Number} rifiutato: {ex.Message}",
                OperationCanceledException when cancel.IsCancellationRequested => $"aggiornamenti: scaricamento di {manifest.Number} annullato",
                _ => $"aggiornamenti: scaricamento di {manifest.Number} non riuscito ({ex.GetType().Name}: {ex.Message})",
            });
            throw;
        }
    }

    /// <summary>Lo zip su disco, con l'impronta calcolata mentre arriva: mai più byte di quelli del manifesto.</summary>
    static async Task SaveZipAsync(Offer offer, string path, HttpClient client, IProgress<long>? progress, CancellationToken cancel)
    {
        var manifest = offer.Manifest;
        using var response = await GetAsync(client, offer.ZipUrl, "application/octet-stream", cancel);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(T("zip: risposta {0} da {1}", (int)response.StatusCode, Shown(offer.ZipUrl)));
        if (response.Content.Headers.ContentLength is { } announced && announced != manifest.Size)
            throw new Refused(T("lo zip è di {0} byte, il manifesto ne dice {1}", announced, manifest.Size));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
        await using (var input = await response.Content.ReadAsStreamAsync(cancel))
        {
            var chunk = new byte[1 << 16];
            while (true)
            {
                using var silence = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                silence.CancelAfter(SilenceTimeout);
                int read;
                try { read = await input.ReadAsync(chunk, silence.Token); }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    throw new HttpRequestException(T("zip: nessun dato da {0:0} secondi", SilenceTimeout.TotalSeconds));
                }
                if (read == 0) break;
                total += read;
                if (total > manifest.Size) throw new Refused(T("lo zip è più grande dei {0} byte del manifesto, scaricamento fermato", manifest.Size));
                hash.AppendData(chunk, 0, read);
                await output.WriteAsync(chunk.AsMemory(0, read), cancel);
                progress?.Report(total);
            }
        }
        if (total != manifest.Size) throw new Refused(T("lo zip è di {0} byte, il manifesto ne dice {1}", total, manifest.Size));
        var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (sha256 != manifest.Sha256) throw new Refused(T("l'impronta SHA-256 dello zip non è quella del manifesto"));
    }

    /// <summary>
    /// I file dello zip nella cartella: solo i nomi di PackageFiles, ognuno una volta e senza cartelle (niente percorsi,
    /// niente ".."), e ognuno al più della sua misura, contata mentre si estrae.
    /// </summary>
    internal static void Extract(string zip, string folder)
    {
        ZipArchive archive;
        try { archive = ZipFile.OpenRead(zip); }
        catch (InvalidDataException) { throw new Refused(T("lo zip non si legge")); }
        using var opened = archive;
        var names = archive.Entries.Select(e => e.FullName).ToList();
        var odd = names.Where(n => !PackageFiles.Contains(n, StringComparer.Ordinal)).ToList();
        var missing = PackageFiles.Where(n => names.Count(x => x == n) != 1).ToList();
        if (odd.Count > 0 || missing.Count > 0)
            throw new Refused(string.Join(" ", new[]
            {
                T("nello zip non ci sono solo i file attesi"),
                odd.Count > 0 ? T("(in più: {0})", string.Join(", ", odd.Take(5).Select(n => n.Length > 80 ? n[..80] : n))) : "",
                missing.Count > 0 ? T("(mancano o sono doppi: {0})", string.Join(", ", missing)) : "",
            }.Where(t => t.Length > 0)));
        foreach (var entry in archive.Entries)
        {
            long limit = entry.FullName == "Missiva.exe" ? MaxExeBytes : MaxTextBytes;
            if (entry.Length > limit) throw new Refused(T("nello zip {0} è di {1} byte, oltre il tetto", entry.FullName, entry.Length));
            using var input = entry.Open();
            using var output = new FileStream(Path.Combine(folder, entry.FullName), FileMode.CreateNew, FileAccess.Write);
            var chunk = new byte[1 << 16];
            long total = 0;
            int read;
            while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > limit) throw new Refused(T("nello zip {0} è più grande del tetto", entry.FullName));
                output.Write(chunk, 0, read);
            }
        }
    }

    /// <summary>Toglie una cartella e quello che contiene, senza sollevare: un file ancora aperto resta, e si riprova a un avvio dopo.</summary>
    internal static void Remove(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write($"aggiornamenti: la cartella {Path.GetFileName(folder)} non si toglie ora ({ex.Message})"); }
    }

    // ---------------------------------------------------------------- passaggio al nuovo eseguibile

    /// <summary>Quello che Missiva lascia scritto prima di passare la mano: da quale versione a quale, dove, al posto di che cosa.</summary>
    internal sealed record Pending(string From, string To, string Folder, string Installed);

    /// <summary>
    /// All'uscita, con l'aggiornamento confermato: scrive in-corso.json e avvia il nuovo eseguibile con l'argomento suo
    /// (Program.Main, --installa-aggiornamento), che aspetta l'uscita di questo processo e si mette al posto di installed,
    /// cioè dell'eseguibile che gira. Se non parte, lo scrive nel suo esito: lo dice l'apertura dopo.
    /// </summary>
    internal static void HandOver(Prepared prepared, string from, string installed, int processId, string root)
    {
        var pending = new Pending(from, prepared.Manifest.Number.ToString(), prepared.Folder, installed);
        File.WriteAllText(Path.Combine(root, PendingName), JsonSerializer.Serialize(pending));
        Log.Write($"aggiornamenti: passo la mano a {prepared.Manifest.Number}, che sostituirà {installed}");
        try
        {
            var start = new ProcessStartInfo(prepared.Exe) { UseShellExecute = false, WorkingDirectory = prepared.Folder };
            start.ArgumentList.Add("--installa-aggiornamento");
            start.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(installed);
            using var _ = Process.Start(start);
        }
        catch (Exception ex)
        {
            Log.Error("avvio del nuovo eseguibile", ex);
            // In italiano nel file, come quelli del nuovo eseguibile (Replace): lo legge l'avvio dopo, che lo mostra tradotto (Outcome).
            try { File.WriteAllText(Path.Combine(prepared.Folder, OutcomeName), string.Format(System.Globalization.CultureInfo.InvariantCulture, N("il nuovo eseguibile non è partito ({0})"), ex.Message)); }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Il nuovo eseguibile prova sé stesso con --versione prima che Missiva si chiuda (come installa.ps1): se non parte,
    /// per esempio perché Windows lo blocca, Missiva resta aperta e lo dice. Null se è partito ed è uscito con 0.
    /// </summary>
    internal static async Task<string?> TryRunAsync(Prepared prepared)
    {
        try
        {
            var start = new ProcessStartInfo(prepared.Exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = prepared.Folder };
            start.ArgumentList.Add("--versione");
            using var probe = Process.Start(start);
            if (probe == null) return T("Windows non l'ha avviato");
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await probe.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException)
            {
                try { probe.Kill(); } catch (Exception) { }
                return T("non risponde");
            }
            return probe.ExitCode == 0 ? null : T("si chiude con il codice 0x{0:X8}", probe.ExitCode);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return ex.Message;
        }
    }

    /// <summary>L'esito della sostituzione, dal nuovo eseguibile.</summary>
    internal sealed record Replacement(bool Done, int Attempts, string Message);

    /// <summary>
    /// Il nuovo eseguibile al posto di quello installato: prima una copia accanto (la parte lenta, fuori dai tentativi),
    /// poi lo scambio, con un nuovo tentativo ogni pause finché quello installato è ancora bloccato (Windows lo tiene per
    /// un attimo dopo l'uscita del processo, e un antivirus può aprirlo), al più attempts volte; poi disinstalla.ps1, LICENSE
    /// e TERZE-PARTI.md accanto. Se lo scambio non riesce, quello di prima resta com'era e la copia si toglie.
    /// </summary>
    internal static Replacement Replace(string source, string installed, int attempts, TimeSpan pause)
    {
        var folder = Path.GetDirectoryName(installed)!;
        var copy = installed + ".nuovo";
        File.Copy(source, copy, true);
        int attempt = 0;
        for (; ; )
        {
            attempt++;
            try
            {
                File.Move(copy, installed, true);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts)
                {
                    try { File.Delete(copy); } catch (Exception) { }
                    // In italiano (il nuovo eseguibile non sceglie la lingua): l'avvio dopo lo mostra tradotto (Outcome).
                    return new(false, attempt, string.Format(System.Globalization.CultureInfo.InvariantCulture, N("il file installato era ancora in uso dopo {0} tentativi ({1})"), attempt, ex.Message));
                }
                Thread.Sleep(pause);
            }
        }
        var missing = new List<string>();
        foreach (var name in BesideFiles)
        {
            try { File.Copy(Path.Combine(Path.GetDirectoryName(source)!, name), Path.Combine(folder, name), true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { missing.Add(name); }
        }
        return new(true, attempt, missing.Count == 0 ? "sostituita" : $"sostituita, ma non si sono copiati {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Program.Main, "--installa-aggiornamento processo installato": il nuovo eseguibile, avviato da HandOver dalla sua
    /// cartella fra i dati, aspetta che la Missiva di prima sia uscita, si mette al suo posto (Replace), scrive com'è
    /// andata accanto a sé, avvia quella installata (la nuova, o la vecchia se lo scambio non è riuscito) ed esce. Solo
    /// su un file che si chiama Missiva.exe, che c'è e che non è sé stesso. Nella versione di prova non fa nulla.
    /// </summary>
    internal static int InstallFromHere(string processId, string installed)
    {
#if DEBUG || PROVA
        _ = processId;
        _ = installed;
        return 2;
#else
        var source = Environment.ProcessPath;
        if (source == null || !int.TryParse(processId, out var id) || !Path.IsPathFullyQualified(installed) ||
            !string.Equals(Path.GetFileName(installed), "Missiva.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(installed) ||
            string.Equals(Path.GetFullPath(installed), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            return 2;
        string outcome;
        bool done = false;
        try
        {
            try
            {
                using var old = Process.GetProcessById(id);
                old.WaitForExit(60_000);
            }
            catch (ArgumentException)
            {
                // già uscita
            }
            var result = Replace(source, installed, 40, TimeSpan.FromMilliseconds(500));
            done = result.Done;
            outcome = result.Message;
        }
        catch (Exception ex)
        {
            outcome = $"{ex.GetType().Name}: {ex.Message}";
        }
        try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(source)!, OutcomeName), outcome); }
        catch (Exception) { }
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(installed) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installed)! });
        }
        catch (Exception)
        {
            // Missiva si riapre dal menu Start; l'esito è scritto, e quell'apertura lo dice
        }
        return done ? 0 : 1;
#endif
    }

    // ---------------------------------------------------------------- dopo l'aggiornamento

    /// <summary>
    /// All'avvio: se c'è un aggiornamento passato al nuovo eseguibile (in-corso.json), com'è andato. Riuscito (questa
    /// Missiva ha il numero atteso): una riga "aggiornata da ... a ..." nel registro e la voce di App installate
    /// (writeVersion). Non riuscito: la riga con il motivo, e il testo da mostrare. In tutti e due i casi, e se c'erano
    /// solo file di uno scaricamento lasciato a metà, la cartella degli aggiornamenti si toglie. Null se non c'è nulla da dire.
    /// </summary>
    internal static string? AfterStart(string root, string current, Func<string, bool> writeVersion)
    {
        if (!Directory.Exists(root)) return null;
        var pendingPath = Path.Combine(root, PendingName);
        string? notice = null;
        if (File.Exists(pendingPath))
        {
            Pending? pending = null;
            try { pending = JsonSerializer.Deserialize<Pending>(File.ReadAllText(pendingPath)); }
            catch (Exception ex) when (ex is JsonException or IOException) { Log.Write($"aggiornamenti: {PendingName} non si legge ({ex.Message})"); }
            if (pending != null)
            {
                string? outcome = null;
                var outcomePath = Path.Combine(root, Path.GetFileName(pending.Folder), OutcomeName);
                try { if (File.Exists(outcomePath)) outcome = File.ReadAllText(outcomePath).Trim(); }
                catch (IOException) { }
                if (pending.To == current)
                {
                    Log.Write($"aggiornata da {pending.From} a {pending.To}" + (outcome is { Length: > 0 } && outcome != "sostituita" ? $" ({outcome})" : ""));
                    bool written = writeVersion(pending.To);
                    Log.Write(written ? $"aggiornamenti: App installate dice {pending.To}" : "aggiornamenti: la voce di App installate non è di questa cartella, non toccata");
                }
                else
                {
                    var why = outcome is { Length: > 0 } ? outcome : N("il nuovo eseguibile non è partito");
                    Log.Write($"aggiornamenti: l'aggiornamento da {pending.From} a {pending.To} non è riuscito ({why}); questa è {current}");
                    notice = T("Missiva non si è aggiornata a {0}: {1}. È rimasta la {2}.", pending.To, Outcome(why), current);
                }
            }
        }
        else
        {
            Log.Write("aggiornamenti: tolti i file di uno scaricamento non finito");
        }
        Remove(root);
        return notice;
    }

    /// <summary>
    /// L'esito scritto in esito.txt, nella lingua di Missiva. Il nuovo eseguibile (--installa-aggiornamento) e HandOver lo
    /// scrivono in italiano, con i testi segnati con N in Replace e HandOver: qui si riconoscono e si traducono; un altro
    /// testo (il nome e il messaggio di un'eccezione) resta com'è.
    /// </summary>
    internal static string Outcome(string written)
    {
        if (written == "il nuovo eseguibile non è partito") return T("il nuovo eseguibile non è partito");
        if (OutcomeNotStarted().Match(written) is { Success: true } started) return T("il nuovo eseguibile non è partito ({0})", started.Groups[1].Value);
        if (OutcomeInUse().Match(written) is { Success: true } inUse) return T("il file installato era ancora in uso dopo {0} tentativi ({1})", inUse.Groups[1].Value, inUse.Groups[2].Value);
        return written;
    }

    [GeneratedRegex(@"^il nuovo eseguibile non è partito \((.*)\)$", RegexOptions.Singleline)]
    private static partial Regex OutcomeNotStarted();

    [GeneratedRegex(@"^il file installato era ancora in uso dopo (\d+) tentativi \((.*)\)$", RegexOptions.Singleline)]
    private static partial Regex OutcomeInUse();

    /// <summary>La voce di App installate scritta da installa.ps1.</summary>
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Missiva";

    /// <summary>La cartella dove installa.ps1 mette Missiva.</summary>
    internal static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Missiva");

    /// <summary>
    /// Dopo un aggiornamento: DisplayVersion della voce di App installate (HKCU), solo se Missiva gira dalla cartella dove
    /// la mette installa.ps1 e la voce dice quella cartella (InstallLocation). La cartella e la chiave sono parametri per
    /// le prove, che lavorano su una chiave loro. Vero se l'ha scritta.
    /// </summary>
    internal static bool WriteDisplayVersion(string version, string exe, string installFolder, string key = UninstallKey)
    {
        static string Folder(string path) => Path.GetFullPath(path).TrimEnd('\\');
        if (!string.Equals(Folder(Path.GetDirectoryName(exe)!), Folder(installFolder), StringComparison.OrdinalIgnoreCase)) return false;
        using var entry = Registry.CurrentUser.OpenSubKey(key, writable: true);
        if (entry?.GetValue("InstallLocation") is not string location || !string.Equals(Folder(location), Folder(installFolder), StringComparison.OrdinalIgnoreCase)) return false;
        entry.SetValue("DisplayVersion", version);
        return true;
    }
}
