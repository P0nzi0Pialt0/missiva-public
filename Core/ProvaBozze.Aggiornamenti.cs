#if DEBUG
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Missiva;

/// <summary>
/// Aggiornamenti da GitHub (15 ottobre 2026, richiesta dell'autore; Updates). Tutto con un GitHub finto (UpdGitHub, un
/// gestore HTTP), chiavi di prova fatte qui in memoria, uno zip fatto qui con l'eseguibile della suite (un Missiva.exe vero,
/// che nella sua versione di file dice quella di questa compilazione) e cartelle temporanee: nessuna rete, nessuna chiave
/// vera, nessun programma avviato. Il formato vero delle risposte di GitHub da qui non si vede: il gestore finto lo scrive
/// come dice la documentazione. Il manifesto e la firma fatti dagli script dell'autore (rilascio.ps1) sono in UpdScriptVector.
/// Con "Missiva.exe --prova-audit risultato.txt aggiornamenti" solo queste.
/// </summary>
static partial class ProvaBozze
{
    /// <summary>Il repository dei rilasci delle prove: mai uno vero.</summary>
    const string UpdRepo = "prova/missiva-prova";

    static void UpdatesTests()
    {
        Report.Add("--- Aggiornamenti da GitHub: chi si aggiorna, firma, manifesto, rete, zip, sostituzione, dopo l'aggiornamento");
        var work = Path.Combine(Path.GetTempPath(), "missiva-prova-aggiornamenti-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var kit = new UpdKit(key, other, work);
            UpdWho(kit);
            UpdSignature(kit);
            UpdManifestRules(kit);
            Wait(() => UpdCheckAsync(kit));
            Wait(() => UpdNetworkAsync(kit));
            Wait(() => UpdDownloadAsync(kit));
            UpdReplace(kit);
            UpdAfterStart(kit);
            UpdDisplayVersion(kit);
            UpdScriptVector();
            UpdSources();
            UpdSourcesWindow();
            UpdSourcesInstall();
        }
        finally
        {
            DeleteQuietly(work);
        }
    }

    // ---------------------------------------------------------------- aiuti

    /// <summary>Quello che serve alle prove: le due chiavi, l'eseguibile della suite con la sua versione, la cartella di lavoro.</summary>
    sealed class UpdKit(ECDsa key, ECDsa other, string work)
    {
        public ECDsa Key { get; } = key;
        public ECDsa Other { get; } = other;
        public string Work => work;
        public string Public { get; } = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        public string OtherPublic { get; } = Convert.ToBase64String(other.ExportSubjectPublicKeyInfo());
        /// <summary>L'eseguibile della suite: un Missiva.exe vero, con la versione di file di questa compilazione.</summary>
        public byte[] Exe { get; } = File.ReadAllBytes(Environment.ProcessPath!);
        public Version ExeVersion { get; } = FileVersion(Environment.ProcessPath!);
        static Version FileVersion(string path)
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        /// <summary>La Missiva "installata" delle prove: più vecchia di qualunque compilazione.</summary>
        public static Version Current { get; } = new(0, 1, 5);
        /// <summary>La cartella degli aggiornamenti scaricati delle prove, al posto di quella fra i dati.</summary>
        public string Root => Path.Combine(work, "dati", Updates.FolderName);
        /// <summary>Non è rimasto nulla: la cartella degli aggiornamenti non c'è, o è vuota.</summary>
        public bool Clean => !Directory.Exists(Root) || !Directory.EnumerateFileSystemEntries(Root).Any();
    }

    /// <summary>I cinque file di un rilascio, con l'eseguibile dato e testi di prova.</summary>
    static List<(string Name, byte[] Data)> UpdEntries(byte[] exe) =>
    [
        ("Missiva.exe", exe), ("installa.ps1", "# installa di prova"u8.ToArray()), ("disinstalla.ps1", "# disinstalla di prova"u8.ToArray()),
        ("LICENSE", "Licenza di prova"u8.ToArray()), ("TERZE-PARTI.md", "# Terze parti di prova"u8.ToArray()),
    ];

    static byte[] UpdZip(IEnumerable<(string Name, byte[] Data)> entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in entries)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
                stream.Write(data);
            }
        return buffer.ToArray();
    }

    /// <summary>Un manifesto come lo scrive rilascio.ps1, con i campi che una prova vuole sbagliare.</summary>
    static byte[] UpdManifest(string number, byte[] zip, string product = "Missiva", long? size = null, string? sha256 = null, string? file = null)
    {
        var fields = new Dictionary<string, object>
        {
            ["prodotto"] = product, ["numero"] = number, ["versione"] = $"{number} del 15/10/2026 (1a2b3c4)", ["data"] = "2026-10-15", ["commit"] = "1a2b3c4",
            ["file"] = file ?? $"Missiva-{number}.zip", ["dimensione"] = size ?? zip.LongLength,
            ["sha256"] = sha256 ?? Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(),
            ["novita"] = new[] { "Prima novità di prova", "Seconda novità di prova" },
        };
        return JsonSerializer.SerializeToUtf8Bytes(fields);
    }

    /// <summary>La firma come la fa rilascio.ps1: ECDsa.SignData con SHA-256 (IEEE P1363, 64 byte), in base64.</summary>
    static string UpdSign(ECDsa key, byte[] data) => Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256));

    /// <summary>
    /// Un GitHub finto, come lo descrive la documentazione: api.github.com/repos/.../releases/latest con tag_name e assets
    /// (name, browser_download_url verso github.com/.../releases/download/...), e i file che da lì rimandano (302) a
    /// release-assets.githubusercontent.com. Scrive ogni richiesta, con le sue intestazioni; Instead risponde al posto suo.
    /// </summary>
    sealed class UpdGitHub : HttpMessageHandler
    {
        public string Tag = "v0.0.0";
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        public readonly List<string> Requests = new();
        public readonly List<string> Agents = new();
        public readonly List<string> ApiVersions = new();
        public readonly List<string> Accepts = new();
        public Func<Uri, HttpResponseMessage?>? Instead;
        const string FileHost = "release-assets.githubusercontent.com", FilePath = "/github-production-release-asset/1/";

        public string DownloadUrl(string name) => $"https://github.com/{UpdRepo}/releases/download/{Tag}/{name}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var url = request.RequestUri!;
            lock (Requests)
            {
                Requests.Add(url.AbsoluteUri);
                Agents.Add(request.Headers.UserAgent.ToString());
                Accepts.Add(request.Headers.Accept.ToString());
                if (request.Headers.TryGetValues("X-GitHub-Api-Version", out var api)) ApiVersions.Add(string.Join(",", api));
            }
            if (Instead?.Invoke(url) is { } other) return Task.FromResult(other);
            if (url.Host == "api.github.com" && url.AbsolutePath == $"/repos/{UpdRepo}/releases/latest")
            {
                var assets = Files.Keys.Select((name, i) => new
                {
                    url = $"https://api.github.com/repos/{UpdRepo}/releases/assets/{100 + i}", id = 100 + i, name, label = "", content_type = "application/octet-stream",
                    state = "uploaded", size = Files[name].Length, download_count = 0, browser_download_url = DownloadUrl(name),
                });
                return Ok(JsonSerializer.SerializeToUtf8Bytes(new { url = $"https://api.github.com/repos/{UpdRepo}/releases/1", id = 1, tag_name = Tag, name = "Missiva " + Tag, draft = false, prerelease = false, assets, body = "" }));
            }
            var prefix = $"/{UpdRepo}/releases/download/{Tag}/";
            if (url.Host == "github.com" && url.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                var found = new HttpResponseMessage(HttpStatusCode.Found);
                found.Headers.Location = new Uri($"https://{FileHost}{FilePath}{url.AbsolutePath[prefix.Length..]}?sp=r&sig=prova");
                return Task.FromResult(found);
            }
            if (url.Host == FileHost && url.AbsolutePath.StartsWith(FilePath, StringComparison.Ordinal) && Files.TryGetValue(Uri.UnescapeDataString(url.AbsolutePath[FilePath.Length..]), out var data))
                return Ok(data);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        static Task<HttpResponseMessage> Ok(byte[] data) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
    }

    /// <summary>Un rilascio sul GitHub finto: lo zip, il manifesto e la firma (null: il file della firma manca).</summary>
    static UpdGitHub UpdRelease(string tag, string number, byte[] zip, byte[] manifest, string? signature)
    {
        var github = new UpdGitHub { Tag = tag };
        github.Files[$"Missiva-{number}.zip"] = zip;
        github.Files[Updates.ManifestName] = manifest;
        if (signature != null) github.Files[Updates.SignatureName] = Encoding.ASCII.GetBytes(signature);
        return github;
    }

    /// <summary>Un flusso che conta quanto è stato letto: lungo length, che dice o non dice (seekable) prima di leggerlo.</summary>
    sealed class UpdCounted(long length, bool seekable) : Stream
    {
        public long Taken;
        public override bool CanRead => true;
        public override bool CanSeek => seekable;
        public override bool CanWrite => false;
        public override long Length => seekable ? length : throw new NotSupportedException();
        public override long Position { get => Taken; set { if (value != Taken) throw new NotSupportedException(); } }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, length - Taken);
            Array.Fill(buffer, (byte)'a', offset, n);
            Taken += n;
            return n;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    static HttpResponseMessage UpdStream(Stream stream) => new(HttpStatusCode.OK) { Content = new StreamContent(stream) };

    static HttpResponseMessage UpdRedirect(string to)
    {
        var found = new HttpResponseMessage(HttpStatusCode.Found);
        found.Headers.Location = new Uri(to);
        return found;
    }

    /// <summary>Dove arriva il registro adesso: le righe scritte dopo si leggono con UpdLogSince.</summary>
    static long UpdLogMark() => new FileInfo(Path.Combine(Store.Dir, "missiva.log")) is { Exists: true } f ? f.Length : 0;

    static string UpdLogSince(long mark)
    {
        var path = Path.Combine(Store.Dir, "missiva.log");
        if (!File.Exists(path)) return "";
        var bytes = File.ReadAllBytes(path);
        return Encoding.UTF8.GetString(bytes, mark <= bytes.Length ? (int)mark : 0, mark <= bytes.Length ? bytes.Length - (int)mark : bytes.Length);
    }

    // ---------------------------------------------------------------- chi si aggiorna

    static void UpdWho(UpdKit kit)
    {
        using var rsa = RSA.Create(2048);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        string rsaKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), p384Key = Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo());
        var modes = new[]
        {
            Updates.StateOf(null, kit.Public), Updates.StateOf("", kit.Public), Updates.StateOf("prova", kit.Public), Updates.StateOf("../missiva", kit.Public),
            Updates.StateOf("prova/../missiva", kit.Public), Updates.StateOf("prova/missiva/altro", kit.Public), Updates.StateOf("https://github.com/prova/missiva", kit.Public),
        };
        bool sources = modes.All(m => m == Updates.Mode.FromSources);
        bool noKey = new[] { "", "   ", "non è base64", Convert.ToBase64String([1, 2, 3]), rsaKey, p384Key, kit.Public[..^8] }.All(k => Updates.StateOf(UpdRepo, k) == Updates.Mode.NoKey);
        Check(sources && noKey && Updates.StateOf(UpdRepo, kit.Public) == Updates.Mode.On,
              "aggiornamenti, chi si aggiorna: senza il repository dei rilasci (o con uno scritto male: un nome solo, \"..\", tre parti, un indirizzo) è una Missiva dai sorgenti; " +
              "con il repository ma senza una chiave P-256 (vuota, non base64, rovinata, RSA, P-384, tagliata) non controlla; con tutti e due sì");
        // La Missiva della suite è compilata dai sorgenti: niente repository nell'eseguibile, quindi nessuna richiesta a GitHub.
        var described = Updates.Describe().Split('\n');
        Check(Updates.Repository == null && Updates.Current == Updates.Mode.FromSources && described.Contains("versione=" + AppVersion.Number) && described.Contains("repository=")
              && described.Contains("chiave=" + UpdateKey.PublicKey),
              $"aggiornamenti, questa Missiva (Debug, senza -p:MissivaAggiornamenti) non ha il repository dei rilasci: è \"compilata dai sorgenti\" e non chiede nulla; --dati-aggiornamenti la descrive così ({string.Join(" | ", described.Where(l => l.Length > 0).Select(l => l.Length > 40 ? l[..40] + "…" : l))})");
        // La chiave nel codice: vuota fino al commit della chiave dell'autore, poi una P-256 valida (mai una rovinata).
        using var inCode = Updates.KeyFrom(UpdateKey.PublicKey);
        Check(UpdateKey.PublicKey.Length == 0 || inCode != null,
              UpdateKey.PublicKey.Length == 0 ? "aggiornamenti, la chiave pubblica in Core\\UpdateKey.cs è ancora vuota: i rilasci compilati ora non controllano gli aggiornamenti"
                                              : "aggiornamenti, la chiave pubblica in Core\\UpdateKey.cs è una chiave ECDSA P-256 che si legge");
        // La casella "Controlla da solo gli aggiornamenti": accesa di base, anche nelle impostazioni scritte prima che ci fosse.
        var before = JsonSerializer.Deserialize("{\"Theme\":\"Dark\"}", SettingsJson.Default.Settings)!;
        var off = JsonSerializer.Deserialize(JsonSerializer.Serialize(new Settings { UpdatesAuto = false }, SettingsJson.Default.Settings), SettingsJson.Default.Settings)!;
        Check(new Settings().UpdatesAuto && before.UpdatesAuto && !off.UpdatesAuto,
              "aggiornamenti, \"Controlla da solo gli aggiornamenti\" è accesa di base, anche con le impostazioni di una versione di prima; spenta resta spenta");
        // La barra chiusa con la X: per quella versione non torna fino al giorno dopo.
        var manifest = new Updates.Manifest(new Version(0, 3, 9), "0.3.9 del 15/10/2026 (1a2b3c4)", new DateOnly(2026, 10, 15), "1a2b3c4", "Missiva-0.3.9.zip", 10, new string('0', 64), []);
        var today = new DateTime(2026, 10, 15, 18, 0, 0);
        Check(!Updates.Shows(manifest, "0.3.9", today.Date.AddHours(9), today) && Updates.Shows(manifest, "0.3.9", today.AddDays(-1), today) && Updates.Shows(manifest, "0.3.8", today, today)
              && Updates.Shows(manifest, null, null, today) && manifest.Title == "Missiva 0.3.9 del 15/10/2026",
              $"aggiornamenti, la barra \"È disponibile {manifest.Title}\" chiusa con la X non torna lo stesso giorno per quella versione; il giorno dopo, o per un'altra versione, sì");
    }

    // ---------------------------------------------------------------- firma

    static void UpdSignature(UpdKit kit)
    {
        var data = UpdManifest("0.3.9", [1, 2, 3]);
        var good = UpdSign(kit.Key, data);
        var changed = data.ToArray();
        changed[^3] ^= 1;
        var der = Convert.ToBase64String(kit.Key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var other = UpdSign(kit.Other, data);
        var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        bool valid = Updates.SignatureValid(data, good, kit.Public) && Updates.SignatureValid(data, " " + good + "\r\n", kit.Public);
        bool refused = !Updates.SignatureValid(data, other, kit.Public) && !Updates.SignatureValid(changed, good, kit.Public) && !Updates.SignatureValid(data, random, kit.Public)
                       && !Updates.SignatureValid(data, der, kit.Public) && !Updates.SignatureValid(data, "", kit.Public) && !Updates.SignatureValid(data, "non è base64", kit.Public)
                       && !Updates.SignatureValid(data, good, "") && !Updates.SignatureValid(data, good, kit.OtherPublic);
        Check(valid && refused,
              "aggiornamenti, la firma del manifesto (ECDSA P-256 su SHA-256, 64 byte IEEE P1363 in base64, come ECDsa.SignData): valida con la chiave giusta, anche con spazi e a capo attorno; " +
              "rifiutata se fatta con un'altra chiave, sul manifesto cambiato di un bit, casuale, nel formato DER, vuota, non base64, o se manca la chiave");
    }

    // ---------------------------------------------------------------- manifesto

    static void UpdManifestRules(UpdKit kit)
    {
        var zip = new byte[] { 1, 2, 3 };
        string Refusal(byte[] bytes, Version? current = null)
        {
            try
            {
                Updates.ReadManifest(bytes, current ?? UpdKit.Current);
                return "";
            }
            catch (Updates.Refused ex)
            {
                return ex.Message;
            }
        }
        var read = Updates.ReadManifest(UpdManifest("0.3.9", zip), UpdKit.Current);
        Check(read.Number == new Version(0, 3, 9) && read.Text == "0.3.9 del 15/10/2026 (1a2b3c4)" && read.Date == new DateOnly(2026, 10, 15) && read.Commit == "1a2b3c4"
              && read.FileName == "Missiva-0.3.9.zip" && read.Size == 3 && read.Sha256 == Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant() && read.News.Count == 2,
              "aggiornamenti, il manifesto di rilascio.ps1 si legge: prodotto, numero, testo della versione, data, commit, nome dello zip, dimensione, impronta e novità");
        (string What, string Reason, string Expected)[] cases =
        [
            ("un altro prodotto", Refusal(UpdManifest("0.3.9", zip, product: "Missiva Pro")), "altro prodotto"),
            ("la stessa versione", Refusal(UpdManifest("0.1.5", zip)), "non è più nuova"),
            ("una versione più vecchia", Refusal(UpdManifest("0.1.4", zip)), "non è più nuova"),
            ("un numero di due parti", Refusal(UpdManifest("0.4", zip)), "il numero non si legge"),
            ("lo zip con un altro nome", Refusal(UpdManifest("0.3.9", zip, file: "..\\Missiva-0.3.9.zip")), "nome dello zip"),
            ("la dimensione zero", Refusal(UpdManifest("0.3.9", zip, size: 0)), "dimensione"),
            ("la dimensione oltre il tetto", Refusal(UpdManifest("0.3.9", zip, size: Updates.MaxZipBytes + 1)), "dimensione"),
            ("l'impronta in maiuscolo", Refusal(UpdManifest("0.3.9", zip, sha256: Convert.ToHexString(SHA256.HashData(zip)))), "impronta"),
            ("un testo che non è JSON", Refusal("non è JSON"u8.ToArray()), "non è JSON"),
        ];
        var wrong = cases.Where(c => !c.Reason.Contains(c.Expected, StringComparison.Ordinal)).Select(c => $"{c.What}: \"{c.Reason}\"").ToList();
        Check(wrong.Count == 0,
              $"aggiornamenti, un manifesto firmato ma sbagliato si rifiuta, ognuno con il suo motivo: {string.Join("; ", cases.Select(c => $"{c.What} (\"{c.Reason}\")"))}" +
              (wrong.Count == 0 ? "" : " [da guardare: " + string.Join(" | ", wrong) + "]"));
    }

    // ---------------------------------------------------------------- controllo

    static async Task UpdCheckAsync(UpdKit kit)
    {
        string number = kit.ExeVersion.ToString(3), tag = "v" + number;
        var zip = UpdZip(UpdEntries(kit.Exe));
        var manifest = UpdManifest(number, zip);
        var signature = UpdSign(kit.Key, manifest);

        // Un rilascio buono: offerto, con cinque richieste (l'API, poi manifesto e firma, ognuno con il suo reindirizzamento).
        var good = UpdRelease(tag, number, zip, manifest, signature);
        long mark = UpdLogMark();
        var offered = await Updates.CheckAsync(UpdRepo, kit.Public, UpdKit.Current, Updates.Client(good));
        string zipName = $"Missiva-{number}.zip";
        bool requests = good.Requests.Count == 5 && good.Requests[0] == $"https://api.github.com/repos/{UpdRepo}/releases/latest"
                        && good.Requests.All(r => r.StartsWith("https://", StringComparison.Ordinal)) && !good.Requests.Any(r => r.Contains(zipName))
                        && good.Agents.All(a => a == "Missiva") && good.ApiVersions is ["2022-11-28"] && good.Accepts[0] == "application/vnd.github+json";
        Check(offered is { Kind: Updates.CheckKind.Offer, Offer: { } offer } && offer.Manifest.Number == kit.ExeVersion && offer.ZipUrl.AbsoluteUri == good.DownloadUrl(zipName)
              && offered.Line == $"È disponibile Missiva {number} del 15/10/2026." && requests && UpdLogSince(mark).Contains($"aggiornamenti: disponibile {number} del 15/10/2026 (1a2b3c4)"),
              $"aggiornamenti, un rilascio buono sul GitHub finto si offre (\"{offered.Line}\"): {good.Requests.Count} richieste, tutte https e con User-Agent \"Missiva\", l'API con la sua " +
              "versione e il suo Accept, nessuna verso lo zip (si scarica solo con Aggiorna); una riga nel registro");

        // L'etichetta dice già una versione uguale o più vecchia: una richiesta sola, niente manifesto.
        var same = UpdRelease("v" + UpdKit.Current.ToString(3), number, zip, manifest, signature);
        var older = UpdRelease("v0.0.9", number, zip, manifest, signature);
        var sameOutcome = await Updates.CheckAsync(UpdRepo, kit.Public, UpdKit.Current, Updates.Client(same));
        var olderOutcome = await Updates.CheckAsync(UpdRepo, kit.Public, UpdKit.Current, Updates.Client(older));
        Check(sameOutcome.Kind == Updates.CheckKind.UpToDate && olderOutcome.Kind == Updates.CheckKind.UpToDate && same.Requests.Count == 1 && older.Requests.Count == 1,
              $"aggiornamenti, un rilascio con l'etichetta della versione installata o di una più vecchia: \"{sameOutcome.Line}\", con la sola richiesta all'API");

        // Rifiutati, ognuno con il suo motivo nel registro, senza scaricare lo zip. L'etichetta non dice un numero: il manifesto si legge sempre.
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("1a2b3c4", "1a2b3c5"));
        var otherProduct = UpdManifest(number, zip, product: "Missiva Pro");
        var sameVersion = UpdManifest(UpdKit.Current.ToString(3), zip);
        var lower = UpdManifest("0.1.4", zip);
        (string What, UpdGitHub GitHub, string Expected)[] refusals =
        [
            ("firma sbagliata", UpdRelease("rilascio", number, zip, manifest, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))), "la firma del manifesto non è valida"),
            ("firmato con un'altra chiave", UpdRelease("rilascio", number, zip, manifest, UpdSign(kit.Other, manifest)), "la firma del manifesto non è valida"),
            ("manifesto cambiato dopo la firma", UpdRelease("rilascio", number, zip, changed, signature), "la firma del manifesto non è valida"),
            ("firma nel formato DER", UpdRelease("rilascio", number, zip, manifest, Convert.ToBase64String(kit.Key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))), "la firma del manifesto non è valida"),
            ("un altro prodotto", UpdRelease("rilascio", number, zip, otherProduct, UpdSign(kit.Key, otherProduct)), "altro prodotto"),
            ("la stessa versione", UpdRelease("rilascio", UpdKit.Current.ToString(3), zip, sameVersion, UpdSign(kit.Key, sameVersion)), "non è più nuova"),
            ("una versione più vecchia", UpdRelease("rilascio", "0.1.4", zip, lower, UpdSign(kit.Key, lower)), "non è più nuova"),
            ("senza la firma", UpdRelease("rilascio", number, zip, manifest, null), "manca aggiornamento.json.firma"),
        ];
        var wrong = new List<string>();
        foreach (var (what, github, expected) in refusals)
        {
            mark = UpdLogMark();
            var outcome = await Updates.CheckAsync(UpdRepo, kit.Public, UpdKit.Current, Updates.Client(github));
            var logged = UpdLogSince(mark);
            if (outcome.Kind != Updates.CheckKind.Refused || outcome.Offer != null || !outcome.Line.Contains(expected) || !logged.Contains(" rifiutato: ") || !logged.Contains(expected)
                || github.Requests.Any(r => r.Contains(".zip")))
                wrong.Add($"{what}: {outcome.Kind} \"{outcome.Line}\"");
        }
        Check(wrong.Count == 0 && !changed.AsSpan().SequenceEqual(manifest),
              $"aggiornamenti, rifiutati dal controllo, ognuno con il suo motivo nel registro e senza scaricare lo zip: {string.Join(", ", refusals.Select(r => r.What))}" +
              (wrong.Count == 0 ? "" : " [da guardare: " + string.Join(" | ", wrong) + "]"));

        // Spento: senza repository, senza chiave, con la casella spenta. Nessuna richiesta.
        var quiet = UpdRelease(tag, number, zip, manifest, signature);
        var client = Updates.Client(quiet);
        var noRepository = await Updates.CheckAsync(null, kit.Public, UpdKit.Current, client);
        var noKey = await Updates.CheckAsync(UpdRepo, "", UpdKit.Current, client);
        var automaticOff = await Updates.AutomaticAsync(false, UpdRepo, kit.Public, UpdKit.Current, client);
        int silent = quiet.Requests.Count;
        var automaticOn = await Updates.AutomaticAsync(true, UpdRepo, kit.Public, UpdKit.Current, client);
        Check(silent == 0 && noRepository.Kind == Updates.CheckKind.Off && noKey.Kind == Updates.CheckKind.Off && automaticOff.Kind == Updates.CheckKind.Off && automaticOn.Kind == Updates.CheckKind.Offer,
              $"aggiornamenti, nessuna richiesta da una Missiva senza il repository dei rilasci (\"{noRepository.Line}\"), senza chiave (\"{noKey.Line}\") o con il controllo automatico spento " +
              $"(\"{automaticOff.Line}\"); acceso, lo stesso GitHub finto offre l'aggiornamento");

        // Un errore di rete: Failed, una riga nel registro, nessuna eccezione.
        var down = new UpdGitHub { Instead = _ => throw new HttpRequestException("rete di prova assente") };
        mark = UpdLogMark();
        var failed = await Updates.CheckAsync(UpdRepo, kit.Public, UpdKit.Current, Updates.Client(down));
        Check(failed.Kind == Updates.CheckKind.Failed && UpdLogSince(mark).Contains("aggiornamenti: controllo non riuscito (HttpRequestException: rete di prova assente)"),
              "aggiornamenti, un errore di rete durante il controllo: nessuna eccezione, solo una riga nel registro (a schermo il controllo automatico non dice nulla)");
    }

    // ---------------------------------------------------------------- rete

    static async Task UpdNetworkAsync(UpdKit kit)
    {
        string[] allowed = ["https://api.github.com/repos/a/b/releases/latest", "https://github.com/a/b/releases/download/v1/x", "https://objects.githubusercontent.com/x",
                            "https://release-assets.githubusercontent.com/x?sig=1", "https://GITHUB.com/a"];
        string[] refused = ["http://github.com/a", "https://github.com:8443/a", "https://utente:segreto@github.com/a", "https://github.com.esempio-non-ammesso.example/a",
                            "https://esempio-non-ammesso.example/github.com", "https://gist.githubusercontent.com/a", "ftp://github.com/a", "file:///C:/Missiva.exe"];
        Check(allowed.All(u => Updates.Allowed(new Uri(u))) && refused.All(u => !Updates.Allowed(new Uri(u))),
              $"aggiornamenti, gli indirizzi ammessi: solo https, porta normale, senza nome e password, verso {string.Join(", ", Updates.Hosts)}; non {string.Join(", ", refused)}");

        string number = kit.ExeVersion.ToString(3), tag = "v" + number;
        var zip = UpdZip(UpdEntries(kit.Exe));
        var manifest = UpdManifest(number, zip);
        var signature = UpdSign(kit.Key, manifest);
        var manifestUrl = $"https://github.com/{UpdRepo}/releases/download/{tag}/{Updates.ManifestName}";
        async Task<(Updates.CheckOutcome Outcome, UpdGitHub GitHub)> With(Func<Uri, HttpResponseMessage?> instead)
        {
            var github = UpdRelease(tag, number, zip, manifest, signature);
            github.Instead = instead;
            try { return (await Updates.CheckAsync(UpdRepo, kit.Public, UpdKit.Current, Updates.Client(github)), github); }
            catch (Exception ex) { return (new Updates.CheckOutcome(Updates.CheckKind.Failed, null, "eccezione: " + ex.Message), github); }
        }

        // Reindirizzamenti verso un host non ammesso, in http, su un'altra porta: rifiutati prima di chiedere.
        var (elsewhere, g1) = await With(u => u.AbsoluteUri == manifestUrl ? UpdRedirect("https://esempio-non-ammesso.example/aggiornamento.json") : null);
        var (plain, g2) = await With(u => u.AbsoluteUri == manifestUrl ? UpdRedirect("http://release-assets.githubusercontent.com/aggiornamento.json") : null);
        var (port, g3) = await With(u => u.AbsoluteUri == manifestUrl ? UpdRedirect("https://release-assets.githubusercontent.com:8443/aggiornamento.json") : null);
        bool notAsked = !g1.Requests.Any(r => r.Contains("esempio-non-ammesso")) && !g2.Requests.Any(r => r.StartsWith("http:")) && !g3.Requests.Any(r => r.Contains(":8443"));
        Check(new[] { elsewhere, plain, port }.All(o => o.Kind == Updates.CheckKind.Refused && o.Line.Contains("indirizzo non ammesso")) && notAsked,
              $"aggiornamenti, un reindirizzamento verso un host non ammesso, in http o su un'altra porta è rifiutato prima di fare la richiesta (\"{elsewhere.Line}\")");

        // Un reindirizzamento che non finisce: fermato dopo cinque.
        var (loop, g4) = await With(u => u.Host == "github.com" ? UpdRedirect(u.AbsoluteUri) : null);
        Check(loop.Kind == Updates.CheckKind.Refused && loop.Line.Contains("troppi reindirizzamenti") && g4.Requests.Count == 7,
              $"aggiornamenti, un reindirizzamento senza fine si ferma dopo cinque ({g4.Requests.Count} richieste in tutto, l'API compresa)");

        // Risposte troppo grandi: fermate al tetto, sia quando la misura è detta prima sia quando non lo è.
        var announced = new UpdCounted(10L * 1024 * 1024, seekable: true);
        var endless = new UpdCounted(5L * 1024 * 1024, seekable: false);
        var bigManifest = new UpdCounted(2L * 1024 * 1024, seekable: false);
        var (o1, _) = await With(u => u.Host == "api.github.com" ? UpdStream(announced) : null);
        var (o2, _) = await With(u => u.Host == "api.github.com" ? UpdStream(endless) : null);
        var (o3, _) = await With(u => u.Host == "release-assets.githubusercontent.com" && u.AbsolutePath.EndsWith(Updates.ManifestName) ? UpdStream(bigManifest) : null);
        Check(o1.Kind == Updates.CheckKind.Refused && announced.Taken == 0 && o2.Kind == Updates.CheckKind.Refused && endless.Taken <= Updates.MaxReleaseBytes + 64 * 1024
              && o3.Kind == Updates.CheckKind.Refused && o3.Line.Contains("manifesto") && bigManifest.Taken <= Updates.MaxManifestBytes + 64 * 1024,
              $"aggiornamenti, una risposta troppo grande si ferma al tetto: quella dell'API che dice 10 MB non si legge ({announced.Taken} byte letti); senza la misura si legge fino al tetto di " +
              $"{Updates.MaxReleaseBytes / 1024} KB ({endless.Taken / 1024} KB letti su 5120); un manifesto di 2 MB si ferma a {bigManifest.Taken / 1024} KB (tetto {Updates.MaxManifestBytes / 1024} KB)");
    }

    // ---------------------------------------------------------------- scaricamento

    static async Task UpdDownloadAsync(UpdKit kit)
    {
        string number = kit.ExeVersion.ToString(3);
        var zip = UpdZip(UpdEntries(kit.Exe));

        // Lo scaricamento di un'offerta fatta come quella del controllo: il manifesto letto da Updates.ReadManifest.
        async Task<(Updates.Prepared? Prepared, string Reason, UpdGitHub GitHub, string Logged)> Download(byte[] served, byte[] manifestBytes, Version? current = null,
                                                                                                           Func<Uri, HttpResponseMessage?>? instead = null)
        {
            var read = Updates.ReadManifest(manifestBytes, UpdKit.Current);
            var github = new UpdGitHub { Tag = "v" + read.Number, Instead = instead };
            github.Files[read.FileName] = served;
            var offer = new Updates.Offer(read, new Uri(github.DownloadUrl(read.FileName)));
            long mark = UpdLogMark();
            try
            {
                var prepared = await Updates.DownloadAsync(offer, current ?? UpdKit.Current, kit.Root, Updates.Client(github));
                return (prepared, "", github, UpdLogSince(mark));
            }
            catch (Exception ex)
            {
                return (null, ex is Updates.Refused ? ex.Message : $"eccezione {ex.GetType().Name}: {ex.Message}", github, UpdLogSince(mark));
            }
        }

        // Buono: nella cartella i cinque file, lo zip tolto, l'eseguibile uguale a quello messo nello zip.
        var (prepared, _, goodHub, goodLog) = await Download(zip, UpdManifest(number, zip));
        var files = prepared == null ? [] : Directory.GetFiles(prepared.Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        bool same = prepared != null && File.ReadAllBytes(prepared.Exe).AsSpan().SequenceEqual(kit.Exe);
        Check(prepared != null && files.SequenceEqual(Updates.PackageFiles.Order(StringComparer.Ordinal)) && same && prepared.Folder == Path.Combine(kit.Root, number)
              && goodHub.Requests.Count == 2 && goodLog.Contains($"aggiornamenti: scaricato e controllato {number} del 15/10/2026"),
              $"aggiornamenti, Aggiorna su un rilascio buono: lo zip scaricato (con il suo reindirizzamento) ha dimensione e impronta del manifesto, si estrae nella cartella {number} con i soli " +
              $"cinque file ({string.Join(", ", files)}) e si toglie; l'eseguibile è quello del rilascio e dice la versione del manifesto");
        if (prepared != null) Updates.Remove(prepared.Folder);
        Updates.Remove(kit.Root);

        // Rifiutati: ognuno con il suo motivo nel registro, e senza lasciare file.
        var other = zip.ToArray();
        other[^1] ^= 1;
        var bigger = UpdManifest(number, zip, size: zip.Length + 1L);
        var smaller = UpdManifest(number, zip, size: zip.Length - 1L);
        var extra = UpdZip(UpdEntries(kit.Exe).Append(("leggimi.txt", "in più"u8.ToArray())));
        var inFolder = UpdZip(UpdEntries(kit.Exe).Select(e => e.Name == "Missiva.exe" ? ("cartella/Missiva.exe", e.Data) : e));
        var climbing = UpdZip(UpdEntries(kit.Exe).Append(("../Missiva.exe", "fuori"u8.ToArray())));
        var twice = UpdZip(UpdEntries(kit.Exe).Append(("Missiva.exe", kit.Exe)));
        var missing = UpdZip(UpdEntries(kit.Exe).Where(e => e.Name != "LICENSE"));
        var next = new Version(kit.ExeVersion.Major, kit.ExeVersion.Minor, kit.ExeVersion.Build + 1).ToString(3);
        // Lo zip servito senza dire la sua misura (un flusso che non sa la sua lunghezza): si scarica fino in fondo, e si contano i byte.
        var shorter = new UpdCounted(zip.Length, seekable: false);
        var tooLong = new UpdCounted(zip.Length + 5L * 1024 * 1024, seekable: false);
        static Func<Uri, HttpResponseMessage?> Serve(Stream stream) => u => u.Host == "release-assets.githubusercontent.com" ? UpdStream(stream) : null;
        (string What, Func<Task<(Updates.Prepared? Prepared, string Reason, UpdGitHub GitHub, string Logged)>> Run, string Expected)[] refusals =
        [
            ("impronta sbagliata", () => Download(other, UpdManifest(number, zip)), "l'impronta SHA-256 dello zip non è quella del manifesto"),
            ("dimensione sbagliata, detta dal server", () => Download(zip, bigger), $"lo zip è di {zip.Length} byte, il manifesto ne dice {zip.Length + 1}"),
            ("dimensione sbagliata, vista alla fine", () => Download(zip, bigger, instead: Serve(shorter)), $"lo zip è di {zip.Length} byte, il manifesto ne dice {zip.Length + 1}"),
            ("zip più lungo del manifesto", () => Download(zip, smaller, instead: Serve(tooLong)), "più grande"),
            ("zip con un nome in più", () => Download(extra, UpdManifest(number, extra)), "in più: leggimi.txt"),
            ("zip con l'eseguibile in una cartella", () => Download(inFolder, UpdManifest(number, inFolder)), "in più: cartella/Missiva.exe"),
            ("zip con un percorso che risale", () => Download(climbing, UpdManifest(number, climbing)), "in più: ../Missiva.exe"),
            ("zip con l'eseguibile due volte", () => Download(twice, UpdManifest(number, twice)), "mancano o sono doppi: Missiva.exe"),
            ("zip senza LICENSE", () => Download(missing, UpdManifest(number, missing)), "mancano o sono doppi: LICENSE"),
            ("eseguibile con un'altra versione", () => Download(zip, UpdManifest(next, zip)), $"il manifesto Missiva {next}"),
            ("versione non più nuova di quella installata", () => Download(zip, UpdManifest(number, zip), current: kit.ExeVersion), "non è più nuova"),
        ];
        var wrong = new List<string>();
        foreach (var (what, run, expected) in refusals)
        {
            var (done, reason, _, logged) = await run();
            if (done != null || !reason.Contains(expected, StringComparison.Ordinal) || !logged.Contains(" rifiutato: ") || !logged.Contains(expected) || !kit.Clean)
                wrong.Add($"{what}: \"{reason}\"");
            Updates.Remove(kit.Root);
        }
        bool outside = !File.Exists(Path.Combine(kit.Work, "dati", "Missiva.exe")) && !File.Exists(Path.Combine(kit.Root, "Missiva.exe"));
        Check(wrong.Count == 0 && outside && shorter.Taken == zip.Length && tooLong.Taken <= zip.Length - 1L + 64 * 1024,
              $"aggiornamenti, rifiutati allo scaricamento, ognuno con il suo motivo nel registro e senza lasciare file: {string.Join(", ", refusals.Select(r => r.What))}; nessun file " +
              $"fuori dalla cartella; lo zip più lungo si ferma alla misura del manifesto ({tooLong.Taken} byte letti su {zip.Length + 5L * 1024 * 1024})" +
              (wrong.Count == 0 ? "" : " [da guardare: " + string.Join(" | ", wrong) + "]"));
    }

    // ---------------------------------------------------------------- sostituzione

    static void UpdReplace(UpdKit kit)
    {
        var installedFolder = Path.Combine(kit.Work, "Programs", "Missiva");
        var newFolder = Path.Combine(kit.Work, "dati", Updates.FolderName, "0.3.9");
        Directory.CreateDirectory(installedFolder);
        Directory.CreateDirectory(newFolder);
        var installed = Path.Combine(installedFolder, "Missiva.exe");
        var source = Path.Combine(newFolder, "Missiva.exe");
        File.WriteAllText(installed, "eseguibile di prima");
        File.WriteAllText(Path.Combine(installedFolder, "altro.txt"), "resta");
        File.WriteAllText(Path.Combine(installedFolder, "LICENSE"), "licenza di prima");
        File.WriteAllText(source, "eseguibile nuovo");
        foreach (var name in Updates.BesideFiles) File.WriteAllText(Path.Combine(newFolder, name), name + " nuovo");

        // Il file installato ancora aperto (come da Windows per un attimo dopo l'uscita): i nuovi tentativi, finché si libera.
        Updates.Replacement freed;
        var held = new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.Read);
        var clock = Stopwatch.StartNew();
        var release = Task.Run(async () =>
        {
            await Task.Delay(700);
            held.Dispose();
        });
        try { freed = Updates.Replace(source, installed, 40, TimeSpan.FromMilliseconds(100)); }
        finally { release.GetAwaiter().GetResult(); }
        bool besides = Updates.BesideFiles.All(n => Path.Combine(installedFolder, n) is var path && File.Exists(path) && File.ReadAllText(path) == n + " nuovo");
        Check(freed.Done && freed.Attempts > 1 && File.ReadAllText(installed) == "eseguibile nuovo" && besides && File.ReadAllText(Path.Combine(installedFolder, "altro.txt")) == "resta"
              && !File.Exists(installed + ".nuovo") && freed.Message == "sostituita",
              $"aggiornamenti, la sostituzione con il file installato ancora aperto: {freed.Attempts} tentativi in {clock.ElapsedMilliseconds} ms, poi il nuovo al suo posto, con disinstalla.ps1, " +
              "LICENSE e TERZE-PARTI.md accanto; gli altri file della cartella restano, la copia di passaggio si toglie");

        // Sempre aperto: dopo i tentativi si arrende, e quello di prima resta com'era.
        File.WriteAllText(installed, "eseguibile di prima");
        Updates.Replacement stuck;
        using (new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.Read))
            stuck = Updates.Replace(source, installed, 3, TimeSpan.FromMilliseconds(50));
        Check(!stuck.Done && stuck.Attempts == 3 && File.ReadAllText(installed) == "eseguibile di prima" && !File.Exists(installed + ".nuovo") && stuck.Message.Contains("ancora in uso dopo 3 tentativi"),
              $"aggiornamenti, con il file installato sempre in uso la sostituzione si ferma dopo i tentativi e quello di prima resta com'era (\"{stuck.Message}\")");

        // Nella versione di prova --installa-aggiornamento non fa nulla.
        int code = Updates.InstallFromHere(Environment.ProcessId.ToString(), installed);
        Check(code == 2 && File.ReadAllText(installed) == "eseguibile di prima",
              "aggiornamenti, nella versione di prova (Debug) Missiva.exe --installa-aggiornamento non sostituisce nulla ed esce con 2");
    }

    // ---------------------------------------------------------------- dopo l'aggiornamento

    static void UpdAfterStart(UpdKit kit)
    {
        var root = Path.Combine(kit.Work, "dopo", Updates.FolderName);
        void Prepare(string? outcome, bool pending = true)
        {
            Updates.Remove(root);
            var folder = Path.Combine(root, "0.3.9");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Missiva.exe"), "eseguibile nuovo");
            if (outcome != null) File.WriteAllText(Path.Combine(folder, Updates.OutcomeName), outcome);
            if (pending) File.WriteAllText(Path.Combine(root, Updates.PendingName), JsonSerializer.Serialize(new Updates.Pending("0.3.1", "0.3.9", folder, @"C:\prova\Missiva.exe")));
        }
        var written = new List<string>();
        bool Write(string version)
        {
            written.Add(version);
            return true;
        }

        Prepare("sostituita");
        long mark = UpdLogMark();
        var done = Updates.AfterStart(root, "0.3.9", Write);
        var doneLog = UpdLogSince(mark);
        Check(done == null && written is ["0.3.9"] && doneLog.Contains("aggiornata da 0.3.1 a 0.3.9") && doneLog.Contains("App installate dice 0.3.9") && !Directory.Exists(root),
              "aggiornamenti, all'avvio dopo un aggiornamento riuscito: \"aggiornata da 0.3.1 a 0.3.9\" nel registro, la voce di App installate, la cartella scaricata tolta; a schermo niente");

        written.Clear();
        Prepare("il file installato era ancora in uso dopo 40 tentativi");
        mark = UpdLogMark();
        var failed = Updates.AfterStart(root, "0.3.1", Write);
        Prepare(null);
        var silent = Updates.AfterStart(root, "0.3.1", Write);
        Check(failed == "Missiva non si è aggiornata a 0.3.9: il file installato era ancora in uso dopo 40 tentativi. È rimasta la 0.3.1."
              && silent == "Missiva non si è aggiornata a 0.3.9: il nuovo eseguibile non è partito. È rimasta la 0.3.1." && written.Count == 0 && !Directory.Exists(root)
              && UpdLogSince(mark).Contains("l'aggiornamento da 0.3.1 a 0.3.9 non è riuscito"),
              $"aggiornamenti, all'avvio dopo un aggiornamento non riuscito: la barra lo dice (\"{failed}\"), anche se il nuovo eseguibile non ha scritto nulla (\"{silent}\"); App installate non si tocca");

        Prepare("sostituita", pending: false);
        mark = UpdLogMark();
        var leftover = Updates.AfterStart(root, "0.3.1", Write);
        Check(leftover == null && written.Count == 0 && !Directory.Exists(root) && UpdLogSince(mark).Contains("tolti i file di uno scaricamento non finito")
              && Updates.AfterStart(root, "0.3.1", Write) == null,
              "aggiornamenti, all'avvio i file di uno scaricamento lasciato a metà (senza in-corso.json) si tolgono; senza la cartella non succede nulla");
    }

    /// <summary>DisplayVersion su chiavi di prova (la shell delle prove ha un registro suo: le chiavi vere non si toccano comunque).</summary>
    static void UpdDisplayVersion(UpdKit kit)
    {
        string ReadReal()
        {
            using var real = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Updates.UninstallKey);
            return real?.GetValue("DisplayVersion") as string ?? "(nessuna)";
        }
        var before = ReadReal();
        var root = @"Software\Missiva-prova-aggiornamenti-" + Guid.NewGuid().ToString("N")[..8];
        var key = root + @"\Uninstall\Missiva";
        var folder = Path.Combine(kit.Work, "Programs", "Missiva");
        var elsewhere = Path.Combine(kit.Work, "Altrove", "Missiva");
        string? Read()
        {
            using var entry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key);
            return entry?.GetValue("DisplayVersion") as string;
        }
        try
        {
            bool none = !Updates.WriteDisplayVersion("0.3.9", Path.Combine(folder, "Missiva.exe"), folder, key);
            using (var entry = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key))
            {
                entry.SetValue("DisplayVersion", "0.3.1");
                entry.SetValue("InstallLocation", folder + "\\");
            }
            bool fromElsewhere = !Updates.WriteDisplayVersion("0.3.9", Path.Combine(elsewhere, "Missiva.exe"), folder, key) && Read() == "0.3.1";
            bool written = Updates.WriteDisplayVersion("0.3.9", Path.Combine(folder, "Missiva.exe"), folder, key) && Read() == "0.3.9";
            using (var entry = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key)) entry.SetValue("InstallLocation", elsewhere);
            bool otherEntry = !Updates.WriteDisplayVersion("0.4.0", Path.Combine(folder, "Missiva.exe"), folder, key) && Read() == "0.3.9";
            Check(none && fromElsewhere && written && otherEntry,
                  "aggiornamenti, App installate su una chiave di prova: DisplayVersion si scrive solo se Missiva gira dalla cartella di installa.ps1 e la voce dice quella cartella " +
                  $"(InstallLocation); senza la voce, da un'altra cartella o con una voce di un'altra cartella no [{none} {fromElsewhere} {written} {otherEntry}]");
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(root, false);
        }
        Check(ReadReal() == before, $"aggiornamenti, la voce vera di App installate è com'era prima della prova (DisplayVersion {before})");
    }

    // ---------------------------------------------------------------- dagli script dell'autore

    /// <summary>
    /// La chiave pubblica, il manifesto e la firma fatti dagli script dell'autore su cartelle temporanee, con una chiave di
    /// prova (privato\chiave-rilascio.ps1, poi privato\rilascio.ps1 -SoloPacchetto su un clone dei sorgenti, il 15 ottobre
    /// 2026; la chiave privata e la sua password, casuale, non sono uscite da quella prova). È il punto dove è più facile
    /// sbagliare: lo stesso formato di chiave, di manifesto e di firma dai due lati. Il manifesto è in base64, byte per byte
    /// com'era nel file (UTF-8 senza BOM, gli a capo di Windows di ConvertTo-Json): la firma è su quei byte.
    /// </summary>
    const string UpdScriptKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEX2rp0lnWk4UsjkTylDrrlXTUNEPOw03ufCPkC07FNmu7RmkQgzXsNbikE50+D8MnGYHLTFS3yV5EjUrIHimRBA==";
    const string UpdScriptManifest =
        "ew0KICAicHJvZG90dG8iOiAiTWlzc2l2YSIsDQogICJudW1lcm8iOiAiMC4zLjUzMSIsDQogICJ2ZXJzaW9uZSI6ICIwLjMuNTMxIGRlbCAxNS8xMC8yMDI2IChjMTUwYzNmKSIsDQogICJkYXRhIjogIjIwMjYtMTAtMTUiLA0K" +
        "ICAiY29tbWl0IjogImMxNTBjM2YiLA0KICAiZmlsZSI6ICJNaXNzaXZhLTAuMy41MzEuemlwIiwNCiAgImRpbWVuc2lvbmUiOiAxMDY1OTcwOTUsDQogICJzaGEyNTYiOiAiYjA4NzhlOWNlNWM0MmI4MmFhZDM0NjUxM2NhN2Jm" +
        "MjhkNDJhNzViOThiMzU3MzM0NTkzOWUyYzdjMTkwMGI3OSIsDQogICJub3ZpdGEiOiBbDQogICAgIlByaW1hIG5vdml0w6AgZGkgcHJvdmEsIGNvbiBsJ2FjY2VudG8iLA0KICAgICJTZWNvbmRhIG5vdml0w6AgZGkgcHJvdmEiDQogIF0NCn0=";
    const string UpdScriptSignature = "kcNA7xZ7zKfhx27smEJJn1paNbGPHXUDa6yoo8xPM1RS3AZm1dLMX66tT50uQcCbeqQ7qPPRG4iLFLkrAZT9JA==";

    static void UpdScriptVector()
    {
        var bytes = Convert.FromBase64String(UpdScriptManifest);
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("0.3.531.zip", "0.3.532.zip"));
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        bool valid = Updates.SignatureValid(bytes, UpdScriptSignature, UpdScriptKey) && !Updates.SignatureValid(changed, UpdScriptSignature, UpdScriptKey)
                     && !Updates.SignatureValid(bytes, UpdScriptSignature, Convert.ToBase64String(stranger.ExportSubjectPublicKeyInfo()));
        string read;
        bool fields = false;
        try
        {
            var manifest = Updates.ReadManifest(bytes, UpdKit.Current);
            read = $"{manifest.Title}, {manifest.FileName}, {manifest.Size} byte, novità \"{string.Join("\" e \"", manifest.News)}\"";
            fields = manifest.Number == new Version(0, 3, 531) && manifest.Text == "0.3.531 del 15/10/2026 (c150c3f)" && manifest.Commit == "c150c3f" && manifest.Size == 106597095
                     && manifest.News is ["Prima novità di prova, con l'accento", "Seconda novità di prova"];
        }
        catch (Updates.Refused ex)
        {
            read = "rifiutato: " + ex.Message;
        }
        Check(valid && fields && !changed.AsSpan().SequenceEqual(bytes),
              $"aggiornamenti, il manifesto e la firma fatti da rilascio.ps1 con la chiave di chiave-rilascio.ps1 si verificano e si leggono in Missiva ({read}); cambiato, o con un'altra chiave, la firma non vale più");
    }

    // ---------------------------------------------------------------- sorgenti

    static void UpdSources()
    {
        if (Ch1Code("Program.cs") is not { } program || Ch1Code("Missiva.csproj") is not { } project)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: gli aggiornamenti nei sorgenti non sono stati letti)");
            return;
        }
        // Program.Main: i due argomenti degli aggiornamenti subito dopo --versione, prima di tutto il resto.
        bool main = Ch3InOrder(program, "if (args is [\"--versione\"]) return;", "if (args is [\"--dati-aggiornamenti\", var described])",
                               "if (args is [\"--installa-aggiornamento\", var processId, var installed])", "Environment.ExitCode = Updates.InstallFromHere(processId, installed);",
                               "if (args is [\"--pdf-worker\"])", "DevTools.TryRun(args)", "ClosesWithoutData(");
        // Il repository dei rilasci solo con la proprietà di rilascio.ps1.
        bool metadata = project.Contains("<ItemGroup Condition=\"'$(MissivaAggiornamenti)' != ''\">") && project.Contains("<AssemblyMetadata Include=\"MissivaAggiornamenti\" Value=\"$(MissivaAggiornamenti)\" />");
        Check(main && metadata,
              "aggiornamenti, nei sorgenti: --dati-aggiornamenti e --installa-aggiornamento in Program.Main subito dopo --versione, prima dei PDF, degli strumenti di prova e di MISSIVA_DATA; " +
              $"il repository dei rilasci nell'eseguibile solo con -p:MissivaAggiornamenti [{main} {metadata}]");
    }

    static void UpdSourcesWindow()
    {
        if (Ch1Code("UI", "MainWindow.Updates.cs") is not { } window || Ch1Code("UI", "App.cs") is not { } app)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: la barra e l'uscita per l'aggiornamento non sono state lette)");
            return;
        }
        // Aggiorna nella versione di prova scrive solo nel registro; all'uscita il passaggio al nuovo eseguibile, e un'uscita annullata lo toglie.
        bool trial = Ch3InOrder(Ch3Part(window, "void OnUpdateNow(", "async Task UpdateNowAsync("), "#if DEBUG || PROVA\n", "Log.Write($\"prova: aggiornerei Missiva", "#else\n", "UpdateNowAsync(offer)", "#endif\n");
        bool exit = Ch3InOrder(Ch3Part(app, "protected override void OnExit(", "bool restartAfterExit;"), "base.OnExit(e);", "if (updateOnExit is { } update)", "Updates.HandOver(update, AppVersion.Number, installed,", "if (!restartAfterExit) return;")
                    && Ch3InOrder(Ch3Part(app, "public void CancelExit()", "public static void ApplyStartWithWindows("), "if (updateOnExit is { } update)", "updateOnExit = null;", "UpdateCancelled(update)");
        Check(trial && exit,
              "aggiornamenti, nei sorgenti: Aggiorna nella versione di prova scrive solo nel registro; all'uscita (dopo quella normale) il passaggio al nuovo eseguibile, che un'uscita annullata " +
              $"toglie [{trial} {exit}]");
    }

    static void UpdSourcesInstall()
    {
        if (SourceRoot() is not { } root || !File.Exists(Path.Combine(root, "installa.ps1")))
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: installa.ps1 non è stato letto)");
            return;
        }
        // installa.ps1: da un rilascio installa l'eseguibile accanto (senza Missiva.csproj); UTF-8 con BOM per Windows PowerShell 5.1.
        var install = File.ReadAllBytes(Path.Combine(root, "installa.ps1"));
        var text = Encoding.UTF8.GetString(install);
        bool release = text.Contains("$release = (Test-Path (Join-Path $PSScriptRoot \"Missiva.exe\")) -and -not (Test-Path (Join-Path $PSScriptRoot \"Missiva.csproj\"))")
                       && Ch3InOrder(text, "if ($release) {", "Copy-Item (Join-Path $PSScriptRoot \"Missiva.exe\") $staging", "} else {", "dotnet publish", "New-Object System.Diagnostics.ProcessStartInfo((Join-Path $staging \"Missiva.exe\"), \"--versione\")");
        bool windowsPowerShell = install is [0xEF, 0xBB, 0xBF, ..] && text.Contains("if ([enum]::GetNames([System.IO.Pipes.PipeOptions]) -contains \"CurrentUserOnly\")")
                                 && !text.Contains("[System.IO.Pipes.PipeOptions]::CurrentUserOnly");
        bool notes = Ch3InOrder(text, "foreach ($name in \"disinstalla.ps1\", \"LICENSE\", \"TERZE-PARTI.md\")", "Copy-Item (Join-Path $PSScriptRoot $name) $staging -Force", "--versione");
        Check(release && windowsPowerShell && notes,
              "aggiornamenti, installa.ps1 da un rilascio (Missiva.exe accanto, senza Missiva.csproj) non compila e prova quell'eseguibile con --versione prima di installarlo; disinstalla.ps1, " +
              $"LICENSE e TERZE-PARTI.md accanto all'eseguibile; UTF-8 con BOM e senza CurrentUserOnly scritto fisso, per Windows PowerShell 5.1 [{release} {windowsPowerShell} {notes}]");
    }

    // ---------------------------------------------------------------- un pacchetto vero (strumento, non suite)

    /// <summary>
    /// Missiva.exe --verifica-pacchetto cartella chiave risultato.txt (solo in Debug): i file di un pacchetto di
    /// rilascio.ps1 (zip, manifesto, firma) serviti da un GitHub finto e passati per lo stesso codice di Missiva
    /// (Updates.CheckAsync e DownloadAsync), con la chiave pubblica data. Serve a provare gli script dell'autore.
    /// </summary>
    internal static void VerifyPackage(string folder, string publicKey, string output)
    {
        var lines = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), "missiva-verifica-pacchetto-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var manifestBytes = File.ReadAllBytes(Path.Combine(folder, Updates.ManifestName));
            lines.Add("firma valida per la chiave data: " + Updates.SignatureValid(manifestBytes, File.ReadAllText(Path.Combine(folder, Updates.SignatureName)), publicKey));
            using var document = JsonDocument.Parse(manifestBytes);
            var github = new UpdGitHub { Tag = "v" + document.RootElement.GetProperty("numero").GetString() };
            foreach (var file in Directory.GetFiles(folder)) github.Files[Path.GetFileName(file)] = File.ReadAllBytes(file);
            var current = new Version(0, 0, 1);
            var outcome = Updates.CheckAsync(UpdRepo, publicKey, current, Updates.Client(github)).GetAwaiter().GetResult();
            lines.Add($"controllo: {outcome.Kind}: {outcome.Line}");
            if (outcome.Offer is { } offer)
            {
                lines.Add("novità: " + string.Join(" | ", offer.Manifest.News));
                var prepared = Updates.DownloadAsync(offer, current, root, Updates.Client(github)).GetAwaiter().GetResult();
                var info = FileVersionInfo.GetVersionInfo(prepared.Exe);
                lines.Add($"scaricato e controllato: {string.Join(", ", Directory.GetFiles(prepared.Folder).Select(Path.GetFileName))}; eseguibile {info.ProductName} {info.FileVersion} ({info.ProductVersion})");
            }
        }
        catch (Exception ex)
        {
            lines.Add("ERRORE: " + ex.Message);
        }
        finally
        {
            DeleteQuietly(root);
        }
        File.WriteAllLines(output, lines);
    }
}
#endif
