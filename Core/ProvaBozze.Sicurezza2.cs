#if DEBUG
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Missiva;

/// <summary>
/// Audit dell'11 ottobre, sicurezza, secondo gruppo (docs\audit\ESITO.md): immagini, mittenti, allegati (T-01..T-10).
/// Per le immagini (T-01) la pagina vera del riquadro (ReadingPage.Build) si carica nel motore vero, un WebView2 senza
/// finestra con lo stesso confino del riquadro (ReadingView.Confine) e la stessa regola per le richieste
/// (ReadingView.FateOf): quello che il motore chiede si legge lì, e parte davvero solo verso un ascoltatore su 127.0.0.1.
/// Solo dati finti. Con "Missiva.exe --prova-audit risultato.txt sicurezza2" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void Security2AuditTests()
    {
        Report.Add("--- Audit dell'11 ottobre, sicurezza, secondo gruppo: immagini, mittenti, allegati (T-01..T-10)");
        Sec2Cleaning();
        try
        {
            Sec2Images();
        }
        catch (Exception ex)
        {
            Check(false, "T-01, il motore vero del riquadro (WebView2 senza finestra) non ha finito la prova: " + ex.Message);
        }
        Sec2TrustedImages();
        Sec2SendersAsync().GetAwaiter().GetResult();
        Sec2StuckDrawing();
        Sec2JunkAsync().GetAwaiter().GetResult();
        Sec2Attachments();
    }

    /// <summary>
    /// T-09 e T-10: i nomi degli allegati che per Windows sono un dispositivo e non un file (COM0, LPT0, quelli con la cifra
    /// in apice, CONIN$ e CONOUT$, e un nome riservato seguito da spazi), e i tipi di file che chiedono conferma prima di
    /// aprirsi. Che cosa fa Windows di questi nomi si prova su questo PC, in una cartella temporanea.
    /// </summary>
    static void Sec2Attachments()
    {
        static string Ch(int code) => ((char)code).ToString();
        string[] devices =
        [
            "COM0", "LPT0", "COM" + Ch(0xB9), "COM" + Ch(0xB2), "COM" + Ch(0xB3), "LPT" + Ch(0xB9), "LPT" + Ch(0xB2), "LPT" + Ch(0xB3), "CONIN$", "CONOUT$", "conin$",
            "CON .txt", "NUL  .pdf", "COM1 .docx", "LPT" + Ch(0xB9) + ".txt", "COM0.zip", "CONOUT$.txt", "Aux .tar.gz",
        ];
        static bool Device(string name) => name.Split('.')[0].TrimEnd(' ').ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || System.Text.RegularExpressions.Regex.IsMatch(name.Split('.')[0].TrimEnd(' '), "^(?i:COM|LPT)[0-9¹²³]$");
        var left = devices.Where(d => Device(Safety.SafeFileName(d)) || Device(Safety.CleanAttachmentName(d))).ToList();
        string[] normal = ["Contratto.pdf", "COMUNE.pdf", "Console.txt", "LPT10.txt", "COM.txt", "conin.txt", "Nulla.docx", "CONTO .pdf", "Preventivo $.xlsx"];
        var changed = normal.Where(n => Safety.SafeFileName(n) != n || Safety.CleanAttachmentName(n) != n).ToList();
        Check(left.Count == 0 && changed.Count == 0 && Safety.SafeFileName("CON .txt") == "_CON .txt" && Safety.CleanAttachmentName("CONIN$") == "_CONIN$",
              $"T-09, {devices.Length} nomi di allegato che per Windows sono un dispositivo (COM0, LPT0, la cifra in apice, CONIN$, CONOUT$, un nome riservato seguito da spazi) escono con un \"_\" davanti; " +
              $"{normal.Length} nomi normali che li ricordano restano uguali" + (left.Count + changed.Count == 0 ? "" : ": restano " + string.Join(" | ", left) + "; cambiati " + string.Join(" | ", changed)));

        // Che cosa ne fa questo Windows: il nome com'era e il nome reso sicuro, scritti in una cartella temporanea (a tempo: un
        // dispositivo vero potrebbe non rispondere). Sola misura: cambia con la versione di Windows.
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-nomi-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string Write(string name)
            {
                var write = Task.Run(() =>
                {
                    try
                    {
                        // Solo aperto in scrittura e richiuso: se è un dispositivo non gli si manda nulla.
                        using (new FileStream(Path.Combine(dir, name), FileMode.CreateNew, FileAccess.Write)) { }
                        return Directory.GetFiles(dir).Any(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)) ? "file" : "nessun file";
                    }
                    catch (Exception ex) { return ex.GetType().Name; }
                });
                return write.Wait(3000) ? write.Result : "nessuna risposta";
            }
            string[] probes = ["CONIN$", "CONOUT$", "COM" + Ch(0xB9), "COM0", "LPT0", "CON .txt", "NUL.txt", "NUL", "CON"];
            var raw = probes.Select(p => $"{p}: {Write(p)}").ToList();
            var safe = probes.Select(p => Write(Safety.SafeFileName(p))).ToList();
            Check(safe.All(s => s == "file"),
                  $"T-09, su questo Windows ({Environment.OSVersion.Version}) i nomi resi sicuri si scrivono come file ({string.Join(", ", safe.Distinct())}); com'erano: {string.Join("; ", raw)}");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        // T-10: i tre tipi dell'elenco di Outlook che mancavano. Le pagine web allegate dalle scelte del 12 ottobre chiedono
        // una conferma loro, da una classe a parte (Safety.IsWebPage, ProvaBozze.Scelte1.cs): fra questi tipi non ci sono.
        string[] added = ["Assistenza.appcontent-ms", "archivio.udl", "diagnosi.diagpkg"], pages = ["Accesso.html", "Accesso.htm", "pagina.shtml", "pagina.xhtml", "logo.svg", "pagina.mht", "pagina.mhtml"];
        Check(added.All(Safety.IsDangerous) && added.All(Safety.NeedsWebMark) && !Safety.IsDangerous("fattura.pdf"),
              $"T-10, chiedono conferma prima di aprirsi anche {string.Join(", ", added.Select(a => Path.GetExtension(a)))}");
        Report.Add($"      (T-10, le pagine web allegate, fra i tipi che avviano programmi e fra quelli con la conferma delle pagine web: " +
                   $"{string.Join(", ", pages.Select(p => $"{Path.GetExtension(p)} {(Safety.IsDangerous(p) ? "sì" : "no")} e {(Safety.IsWebPage(p) ? "sì" : "no")}"))})");
    }

    /// <summary>
    /// T-08: dove la Posta indesiderata non si riconosce, quello che vale lì non vale. Nel riquadro quattro decisioni (copia
    /// su disco, dominio autenticato, rubrica, logo) guardavano solo la cartella e non l'etichetta di Gmail nei risultati di
    /// una ricerca. Su IMAP una seconda cartella di indesiderata è una cartella qualunque; con Graph lo è quella vera se la
    /// prima lettura delle cartelle speciali non riesce.
    /// </summary>
    static async Task Sec2JunkAsync()
    {
        // Nel riquadro si chiede sempre InJunk (cartella o etichetta), non la sola cartella: lettura del sorgente, perché
        // quelle decisioni stanno nei metodi della finestra.
        if (SourceRoot() is { } root)
        {
            var reading = File.ReadAllText(Path.Combine(root, "UI", "ReadingView.xaml.cs"));
            int byFolder = System.Text.RegularExpressions.Regex.Matches(reading, @"FolderId\s*[!=]=\s*junk\b").Count;
            Check(byFolder == 0 && System.Text.RegularExpressions.Regex.Matches(reading, @"InJunk\(").Count >= 9,
                  $"T-08, nel riquadro la Posta indesiderata è sempre la cartella o l'etichetta (InJunk): {byFolder} decisioni guardano ancora la sola cartella");
        }
        else Report.Add("      (sorgenti non trovati accanto all'eseguibile: le decisioni del riquadro sulla Posta indesiderata non sono state lette)");
        var labelled = new MessageSummary { Id = "1", FolderId = "Tutti i messaggi", LabelKind = FolderKind.Junk, From = new Address { Email = "x@dominio-ostile.example" } };
        Check(UI.ReadingView.InJunk(labelled, "Spam") && UI.ReadingView.InJunk(labelled, null) && !UI.ReadingView.InJunk(Sec2Mail("2", "Mario Rossi", "mario.rossi@example.com"), "Spam"),
              "T-08, un messaggio con l'etichetta della Posta indesiderata trovato in un'altra cartella (una ricerca di Gmail) è nella Posta indesiderata");

        // IMAP, lasciato: una seconda cartella di indesiderata (quella del filtro del server) accanto a quella segnata dal server.
        using (var fake = new FakeImap(new Flavor(Gmail: false)))
        {
            bool plainBefore = ImapProvider.TestPlain;
            ImapProvider.TestPlain = true;
            fake.Folders["Spam"] = new();
            try
            {
                using var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
                var folders = await imap.GetFoldersAsync();
                Report.Add($"      (T-08, IMAP, lasciato: con \"{fake.JunkFolder}\" segnata dal server e una seconda cartella \"Spam\", la prima è {folders.First(f => f.Id == fake.JunkFolder).Kind} " +
                           $"e la seconda {folders.First(f => f.Id == "Spam").Kind}: per i suoi messaggi vale quello che vale in una cartella qualunque)");
            }
            finally
            {
                ImapProvider.TestPlain = plainBefore;
            }
        }

        // Graph: la cartella si riconosce dalla copia locale anche se la richiesta non riesce; senza copia, solo fino alla lettura dopo.
        (string Id, string Name)[] top = [("in", "Inbox"), ("dr", "Drafts"), ("se", "Sent Items"), ("de", "Deleted Items"), ("ar", "Archive"), ("ju", "Junk Email"), ("cli", "Clienti")];
        var aliases = new Dictionary<string, string> { ["inbox"] = "in", ["drafts"] = "dr", ["sentitems"] = "se", ["deleteditems"] = "de", ["archive"] = "ar", ["junkemail"] = "ju" };
        bool broken = true;
        var graph = new SlowGraph
        {
            Delay = 1,
            Answer = path =>
            {
                if (path.Contains("mailFolders?"))
                    return new System.Text.Json.Nodes.JsonObject { ["value"] = new System.Text.Json.Nodes.JsonArray(top.Select(f => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                        { ["id"] = f.Id, ["displayName"] = f.Name, ["unreadItemCount"] = 0, ["totalItemCount"] = 1, ["childFolderCount"] = 0 }).ToArray()) };
                var alias = path.Split('/')[^1].Split('?')[0];
                if (broken && alias == "junkemail") return null; // la risposta non porta la cartella, come dopo un errore
                return path.Contains("mailFolders/") && aliases.TryGetValue(alias, out var id) ? new System.Text.Json.Nodes.JsonObject { ["id"] = id } : null;
            },
        };
        GraphProvider.UseTestHandler(graph);
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "maria.rossi@studio-t08-prova.example", Tenant = "organizations" };
        MailCache.Delete(account);
        try
        {
            var provider = new GraphProvider(account);
            FolderKind Junk(List<MailFolder> folders) => folders.First(f => f.Id == "ju").Kind;
            var first = Junk(await provider.GetFoldersAsync()); // nessuna copia, richiesta non riuscita
            broken = false;
            var good = await provider.GetFoldersAsync(); // alla lettura dopo torna
            MailCache.SaveFolders(account, good);
            broken = true;
            graph.Order.Clear();
            var cached = Junk(await provider.GetFoldersAsync()); // con la copia non si chiede nemmeno
            int asked = graph.Order.Count(o => o.Contains("mailFolders/junkemail"));
            Check(first == FolderKind.Other && Junk(good) == FolderKind.Junk && cached == FolderKind.Junk && asked == 0,
                  $"T-08, Graph: con la copia locale delle cartelle la Posta indesiderata si riconosce senza chiederla ({asked} richieste, anche con il server che non risponde); senza copia e con la richiesta " +
                  $"non riuscita è una cartella qualunque ({first}) solo fino alla lettura dopo ({Junk(good)})");
        }
        finally
        {
            MailCache.Delete(account);
        }
    }

    /// <summary>
    /// T-07: il disegno di un logo che ControlledExecution non riesce a fermare. Qui lo fa un blocco finally (come il codice
    /// nativo, non si interrompe): dopo il doppio del tempo OnStaAsync smette di aspettarlo, ma il thread resta a girare e
    /// prima nessuno lo sapeva, e i disegni dopo partivano lo stesso, uno sull'altro.
    /// </summary>
    static void Sec2StuckDrawing()
    {
        var running = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        byte[]? Stuck()
        {
            try { }
            finally
            {
                running.Set();
                release.Wait(TimeSpan.FromSeconds(20)); // in un finally l'interruzione non arriva: come una chiamata nativa
            }
            return [1];
        }
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var first = Task.Run(() => SenderLogos.OnStaAsync(Stuck, 1)).GetAwaiter().GetResult();
            long waited = clock.ElapsedMilliseconds;
            bool alive = running.IsSet && !release.IsSet;
            int drawn = 0;
            var second = Task.Run(() => SenderLogos.OnStaAsync(() => { Interlocked.Increment(ref drawn); return [2]; }, 1)).GetAwaiter().GetResult();
            bool suspended = SenderLogos.DrawingSuspended;
            // Il thread finisce: i disegni riprendono.
            release.Set();
            for (int k = 0; k < 100 && SenderLogos.DrawingSuspended; k++) Thread.Sleep(20);
            var third = Task.Run(() => SenderLogos.OnStaAsync(() => [3], 1)).GetAwaiter().GetResult();
            Check(first == null && waited is > 1800 and < 3500 && alive && second == null && drawn == 0 && suspended && !SenderLogos.DrawingSuspended && third is [3],
                  $"T-07, un disegno che non si ferma nemmeno dopo il doppio del tempo ({waited} ms con il limite di prova di 1 s): finché il suo thread gira non se ne avviano altri " +
                  $"(disegni partiti nel frattempo: {drawn}), e quando finisce i disegni riprendono");
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    /// T-03, T-04, T-05, T-06: di chi è la Authentication-Results che Missiva prende per quella del server della casella.
    /// Su un account IMAP una scritta da chi manda, con il nome del dominio del destinatario, valeva come prova: il dominio
    /// del From diventava autenticato per sempre (logo) e "Annulla iscrizione" partiva con un clic. Con Gmail valeva quella
    /// con il suo nome anche sotto quella di un altro server; con Microsoft un nome che solo finisce come il suo.
    /// </summary>
    static async Task Sec2SendersAsync()
    {
        const string bank = "banca-t03-prova.example", list = "https://liste.banca-t03-prova.example/via?id=1";
        // T-03, sul server IMAP finto, che come molti server piccoli non scrive la sua intestazione e non toglie quelle che arrivano.
        using var fake = new FakeImap(new Flavor(Gmail: false));
        bool plainBefore = ImapProvider.TestPlain;
        ImapProvider.TestPlain = true;
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        MimeKit.MimeMessage Forged(string subject, string results)
        {
            var mime = new MimeKit.MimeMessage { Subject = subject, Date = DateTimeOffset.Now, Body = new MimeKit.TextPart("html") { Text = "<p>Gentile cliente</p>" } };
            mime.From.Add(new MimeKit.MailboxAddress("Banca di prova", "avvisi@" + bank));
            mime.To.Add(new MimeKit.MailboxAddress("Mario Rossi", "mario@studio-prova.example"));
            mime.Headers.Add("Authentication-Results", results);
            mime.Headers.Add("List-Unsubscribe", $"<{list}>");
            mime.Headers.Add("List-Unsubscribe-Post", OneClick);
            return mime;
        }
        fake.Add("INBOX", Forged("Superato", $"mail.studio-prova.example; dkim=pass header.d={bank}; spf=pass smtp.mailfrom=avvisi@{bank}; dmarc=pass header.from={bank}"));
        fake.Add("INBOX", Forged("Fallito", $"mail.studio-prova.example; spf=softfail smtp.mailfrom=x@dominio-ostile.example; dmarc=fail header.from={bank}"));
        fake.Add("INBOX", Forged("Di un altro", $"mx.altro-prova.example; dkim=pass header.d={bank}; dmarc=pass header.from={bank}"));
        MailCache.Delete(account);
        BodyCache.Delete(account);
        SenderLogos.TestForget([bank]);
        try
        {
            using var imap = new ImapProvider(account);
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var rows = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            MessageSummary Row(string subject) => rows.First(m => m.Subject == subject);
            var passed = await imap.GetBodyAsync(Row("Superato"));
            var failed = await imap.GetBodyAsync(Row("Fallito"));
            var foreign = await imap.GetBodyAsync(Row("Di un altro"));
            SenderLogos.NoteBody(account, Row("Superato"), passed);
            bool authenticated = SenderLogos.IsAuthenticated(bank), doubtful = SenderLogos.IsDoubtful(account, Row("Superato"));
            var action = Unsubscribe.For(account, Row("Superato"), passed, false);
            var notes = SenderCheck.Of(account, Row("Fallito"), failed);
            SenderLogos.NoteBody(account, Row("Fallito"), failed);
            Check(passed.AuthenticationResults.Count == 1 && !authenticated && !doubtful && action is { Kind: UnsubscribeKind.Web }
                  && notes.Unverified == bank && SenderLogos.IsDoubtful(account, Row("Fallito")) && Unsubscribe.For(account, Row("Fallito"), failed, false) == null
                  && SenderCheck.Own(account, foreign.AuthenticationResults) == null,
                  $"T-03, account IMAP, una Authentication-Results con il nome del dominio della casella scritta da chi manda: il dominio del From non diventa autenticato ({authenticated}) e " +
                  $"\"Annulla iscrizione\" non parte con un clic ({action?.Kind}); se dice che DMARC è fallito la fascia rossa c'è lo stesso ({notes.Unverified ?? "nessuna"})");
        }
        finally
        {
            ImapProvider.TestPlain = plainBefore;
            MailCache.Delete(account);
            BodyCache.Delete(account);
            SenderLogos.TestForget([bank]);
        }

        // T-04, Gmail: la sua intestazione vale come prova solo se è la prima in alto.
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "ada.prova@gmail.com" };
        string googlePass = $"mx.google.com; dkim=pass header.i=@{bank}; spf=pass smtp.mailfrom=avvisi@{bank}; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from={bank}";
        string googleFail = $"mx.google.com; spf=softfail smtp.mailfrom=x@dominio-ostile.example; dmarc=fail (p=NONE sp=NONE dis=NONE) header.from={bank}";
        string other = $"mx.altro-prova.example; dkim=none; spf=none smtp.mailfrom=x@dominio-ostile.example";
        bool Authentic(MailAccount who, params string[] headers) => SenderCheck.Own(who, headers) is { } r && SenderCheck.Authenticated(r, bank);
        bool Red(MailAccount who, params string[] headers) => SenderCheck.Own(who, headers) is { } r && SenderCheck.Failed(r);
        Check(Authentic(gmail, googlePass) && Authentic(gmail, googlePass, other) && !Authentic(gmail, other, googlePass) && Red(gmail, other, googleFail) && Red(gmail, googleFail, googlePass)
              && SenderCheck.Own(gmail, [other]) == null,
              $"T-04, Gmail: la sua Authentication-Results autentica il mittente solo se è la prima in alto ({Authentic(gmail, googlePass)}); sotto quella di un altro server non prova nulla " +
              $"({Authentic(gmail, other, googlePass)}), ma se dice che DMARC è fallito la fascia rossa resta ({Red(gmail, other, googleFail)})");

        // T-05, Microsoft: il nome di chi l'ha scritta deve essere il suo o stare sotto il suo, non solo finire allo stesso modo.
        var microsoft = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@outlook.example" };
        string results = $"; dkim=pass header.d={bank}; dmarc=pass action=none header.from={bank}";
        string[] theirs = ["BN8NAM11FT045.mail.protection.outlook.com", "mx.microsoft.com", "outlook.com", "MICROSOFT.COM"], notTheirs = ["xoutlook.com", "posta-outlook.com", "finto-microsoft.com", "outlook.com.dominio-ostile.example"];
        var accepted = notTheirs.Where(id => SenderCheck.Own(microsoft, [id + results]) != null).ToList();
        var refused = theirs.Where(id => !Authentic(microsoft, id + results)).ToList();
        Check(accepted.Count == 0 && refused.Count == 0 && Authentic(microsoft, results[2..]),
              $"T-05, Microsoft: una Authentication-Results scritta da un nome che solo finisce come quello di Microsoft non è la sua ({notTheirs.Length} nomi" +
              (accepted.Count == 0 ? "" : ", accettati " + string.Join(", ", accepted)) + $"); le sue, con il suo nome o senza nome come le scrive Exchange, valgono come prima ({theirs.Length + 1} forme)");

        // T-06: senza DMARC valeva una firma DKIM dello stesso dominio a due etichette, anche di un altro sottodominio. Lasciato
        // l'11 ottobre; dalle scelte del 12 ottobre "autenticato" chiede DMARC superato (ProvaBozze.Scelte1.cs). Sola misura.
        const string platform = "piattaforma-prova.example", shop = "negozio-prova.example";
        bool sibling = SenderCheck.Authenticated(SenderCheck.Parse($"mx.google.com; dkim=pass header.d=cliente2.{platform}; spf=none smtp.mailfrom=x@cliente2.{platform}"), platform);
        bool child = SenderCheck.Authenticated(SenderCheck.Parse($"mx.google.com; dkim=pass header.d=em.{shop}; spf=none smtp.mailfrom=rimbalzi@em.{shop}"), shop);
        Report.Add($"      (T-06: senza DMARC un messaggio da cliente1.{platform} firmato da cliente2.{platform} vale come autenticato per {platform}: {sibling}; " +
                   $"la firma di em.{shop} per un From di {shop}: {child}. Fino all'11 ottobre valevano tutti e due)");
    }

    /// <summary>
    /// T-02: le immagini di un mittente consentito si caricavano da sole anche quando Missiva stessa dice di non fidarsi di
    /// chi scrive: con la fascia rossa (il server della casella non ha verificato il mittente), in una busta che sembra
    /// posta certificata ma non è firmata da un gestore, in un .eml allegato a un messaggio della Posta indesiderata. E
    /// "Mostra immagini" ricordava per sempre il mittente scritto in quel From.
    /// </summary>
    static void Sec2TrustedImages()
    {
        const string bank = "banca-prova.example", hostile = "dominio-ostile.example";
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "ada.prova@gmail.com" };
        var before = Store.Settings.ImageSenders;
        try
        {
            Store.Settings.ImageSenders = [];
            Store.Settings.AllowImagesFrom("avvisi@" + bank);
            Store.Settings.AllowImagesFrom("mario.rossi@pec-prova.it");
            var mail = Sec2Mail("1", "Banca di prova", "avvisi@" + bank);
            const string picture = "<p>Avviso</p><img src=\"https://immagini.banca-prova.example/a.png\" width=\"200\" height=\"100\" alt=\"Avviso\">";
            MessageBody Body(string results) => new() { Html = picture, AuthenticationResults = [results] };
            var forged = Body($"mx.google.com; spf=softfail smtp.mailfrom=x@{hostile}; dmarc=fail (p=NONE sp=NONE dis=NONE) header.from={bank}");
            var honest = Body($"mx.google.com; dkim=pass header.i=@{bank}; spf=pass smtp.mailfrom=rimbalzi@{bank}; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from={bank}");
            var silent = new MessageBody { Html = picture };
            bool Loads(MessageBody body, bool once = false) => UI.ReadingView.LoadsImages(mail, body, "Spam", once, gmail);

            // (a) La fascia rossa.
            bool band = SenderCheck.Of(gmail, mail, forged).Unverified == bank && SenderCheck.Of(gmail, mail, honest).Unverified == null;
            Check(band && !Loads(forged) && Loads(forged, once: true) && Loads(honest) && Loads(silent),
                  $"T-02, un mittente consentito con la fascia rossa (DMARC fallito secondo il server della casella): le immagini non si caricano da sole ({Loads(forged)}), solo dopo \"Mostra immagini\" " +
                  $"({Loads(forged, once: true)}); con DMARC superato, o senza intestazioni del server, come prima ({Loads(honest)}, {Loads(silent)})");

            // (b) Una busta che sembra posta certificata ma non ha la firma di un gestore, e una firmata con un certificato per i siti web.
            var keys = new PecKeys();
            (MessageSummary Summary, MessageBody Body) Envelope(byte[] raw)
            {
                var message = MimeKit.MimeMessage.Load(new MemoryStream(raw));
                return (new EmlProvider(message, null!).Summary, Pec.Open(raw, EmlProvider.BodyOf(message)));
            }
            bool unsignedLoads, webLoads, greenLoads;
            string unsignedTone, greenTone;
            try
            {
                Pec.TestRoots = [keys.Root];
                var unsigned = Envelope(PecEnvelope(null, "posta-certificata"));
                var web = Envelope(PecEnvelope(keys.Web, "posta-certificata"));
                var green = Envelope(PecEnvelope(keys.Provider, "posta-certificata"));
                unsignedLoads = UI.ReadingView.LoadsImages(unsigned.Summary, unsigned.Body, "Spam", account: gmail);
                webLoads = UI.ReadingView.LoadsImages(web.Summary, web.Body, "Spam", account: gmail);
                greenLoads = UI.ReadingView.LoadsImages(green.Summary, green.Body, "Spam", account: gmail);
                (unsignedTone, greenTone) = (Pec.Band(unsigned.Body.Pec!).Tone, Pec.Band(green.Body.Pec!).Tone);
                Check(unsigned.Body.ImagesFrom == "mario.rossi@pec-prova.it" && unsignedTone == "danger" && greenTone == "ok" && !unsignedLoads && !webLoads && greenLoads
                      && UI.ReadingView.LoadsImages(unsigned.Summary, unsigned.Body, "Spam", once: true, account: gmail),
                      $"T-02, una busta che sembra posta certificata con dentro il From di un mittente consentito: senza la firma di un gestore le immagini non si caricano da sole ({unsignedLoads}), " +
                      $"e nemmeno con un certificato per i siti web ({webLoads}); con la firma del gestore verificata sì ({greenLoads})");
            }
            finally
            {
                Pec.TestRoots = null;
            }

            // (c) Un .eml allegato a un messaggio della Posta indesiderata si apre come se ci stesse anche lui.
            var attached = new MimeKit.MimeMessage { Subject = "Avviso" };
            attached.From.Add(new MimeKit.MailboxAddress("Banca di prova", "avvisi@" + bank));
            attached.Body = new MimeKit.BodyBuilder { HtmlBody = picture }.ToMessageBody();
            var eml = new EmlProvider(attached, new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "studio@uno.example", Imap = new ImapServer() }));
            var emlBody = eml.GetBodyAsync(eml.Summary).GetAwaiter().GetResult();
            // Dalle scelte del 12 ottobre nemmeno l'allegato di un messaggio normale le carica da solo (ProvaBozze.Scelte1.cs): qui
            // resta che quello della Posta indesiderata si apre come se ci stesse anche lui, anche senza dire che è un .eml.
            bool fromJunk = UI.ReadingView.LoadsImages(eml.Summary, emlBody, UI.ReadingView.EmlJunk(eml, fromJunk: true), account: eml.Account);
            bool fromInbox = UI.ReadingView.LoadsImages(eml.Summary, emlBody, UI.ReadingView.EmlJunk(eml, fromJunk: false), account: eml.Account, eml: true);
            Check(!fromJunk && !fromInbox && UI.ReadingView.InJunk(eml.Summary, UI.ReadingView.EmlJunk(eml, true)) && Unsubscribe.For(eml.Account, eml.Summary, emlBody, junk: true) == null,
                  $"T-02, un .eml con il From di un mittente consentito, allegato a un messaggio della Posta indesiderata: le immagini non si caricano da sole ({fromJunk}); " +
                  $"dalle scelte del 12 ottobre nemmeno allegato a un messaggio normale ({fromInbox})");

            // (d) "Mostra immagini" in quei casi non ricorda nessuno.
            Store.Settings.ImageSenders = [];
            var unsignedAgain = Envelope(PecEnvelope(null, "posta-certificata"));
            bool rememberedForged = UI.ReadingView.RememberImages(gmail, mail, forged, "Spam");
            bool rememberedEnvelope = UI.ReadingView.RememberImages(gmail, unsignedAgain.Summary, unsignedAgain.Body, "Spam");
            bool rememberedEml = UI.ReadingView.RememberImages(eml.Account, eml.Summary, emlBody, UI.ReadingView.EmlJunk(eml, true));
            int afterDistrusted = Store.Settings.ImageSenders.Count;
            bool rememberedHonest = UI.ReadingView.RememberImages(gmail, mail, honest, "Spam");
            Check(!rememberedForged && !rememberedEnvelope && !rememberedEml && afterDistrusted == 0 && rememberedHonest && Store.Settings.ImageSenders.SequenceEqual([bank]),
                  $"T-02, \"Mostra immagini\" con la fascia rossa, nella busta senza firma e nell'allegato della Posta indesiderata vale per quella volta e non ricorda nessuno ({afterDistrusted} mittenti ricordati); " +
                  $"per il messaggio verificato ricorda {string.Join(", ", Store.Settings.ImageSenders)}");

            // La fascia delle immagini bloccate lo dice.
            Store.Settings.ImageSenders = [];
            UI.ReadingPage Page(MessageBody body) => UI.ReadingPage.Build(
                [new UI.PageEntry(mail, body, true, false, null, UI.ReadingView.LoadsImages(mail, body, "Spam", account: gmail), SenderCheck.Of(gmail, mail, body),
                                  ImagesOnce: UI.ReadingView.ImagesOnce(gmail, mail, body, "Spam"))], light: true, header: new UI.PageHeader("Avviso", false));
            string red = Page(forged).Html, plain = Page(honest).Html;
            Check(red.Contains("Mostra immagini") && red.Contains("vale solo per questa volta") && !red.Contains("in tutti gli account")
                  && plain.Contains($"per {bank}, in tutti gli account") && !plain.Contains("solo per questa volta"),
                  "T-02, nella pagina di un messaggio con la fascia rossa \"Mostra immagini\" dice che vale solo per quella volta; in quella di un messaggio verificato dice che ricorderà il mittente");
        }
        finally
        {
            Store.Settings.ImageSenders = before;
        }
    }

    /// <summary>
    /// T-01, senza il motore: che cosa resta nel frammento pulito e quante immagini si contano bloccate (senza il conto
    /// non compare nemmeno la fascia "Mostra immagini").
    /// </summary>
    static void Sec2Cleaning()
    {
        const string site = "esterno-prova.example";
        static MailHtml.Result Clean(string html, bool allow = false) => MailHtml.Prepare(html, false, allow, cid => cid == "logo" ? "https://missiva.example/cid/1/logo" : null);

        // Le forme del rapporto e le loro parenti: un'immagine vera (200 x 100) per ognuna.
        string[] written =
        [
            "http:\\\\{S}/a.png", "\\\\{S}/a.png", "/\\{S}/a.png", "\\/{S}/a.png", "http:/{S}/a.png", "http:{S}/a.png", "ht&#9;tp://{S}/a.png", "&#10;//{S}/a.png",
            "https:\\\\{S}/a.png", "HTTP:\\/{S}/a.png", "&#1;http://{S}/a.png", "http:/&#13;&#10;/{S}/a.png", "//{S}/a.png", "https://{S}/a.png",
        ];
        var kept = new List<string>();
        foreach (var form in written)
        {
            string address = form.Replace("{S}", site);
            var image = Clean($"<img src=\"{address}\" width=\"200\" height=\"100\" alt=\"Foto\">");
            var cell = Clean($"<table><tr><td background=\"{address}\">x</td></tr></table>");
            if (image.BlockedImages != 1 || image.Fragment.Contains(site) || !image.Fragment.Contains("data:image/svg+xml") || cell.BlockedImages != 1 || cell.Fragment.Contains(site)) kept.Add(address);
        }
        Check(kept.Count == 0,
              $"T-01, un'immagine e uno sfondo esterni scritti in {written.Length} modi: tolti dal messaggio di un mittente non consentito e contati fra le immagini bloccate" +
              (kept.Count == 0 ? "" : ": restano o non si contano " + string.Join(" | ", kept)));

        // Con il mittente consentito restano come sono scritte; quello che non è né un sito né un'immagine dell'email si toglie sempre.
        var allowed = Clean($"<img src=\"http:\\\\{site}/a.png\"><img src=\"/\\{site}/b.png\"><svg><image href=\"http:{site}/c.png\"/></svg>", allow: true);
        var other = Clean($"<img src=\"file://{site}/a.png\"><img src=\"javascript:alert(1)\"><img src=\"blob:https://{site}/1\"><img src=\"C:\\a.png\">", allow: true);
        Check(allowed.BlockedImages == 0 && allowed.Fragment.Contains($"http:\\\\{site}/a.png") && allowed.Fragment.Contains($"/\\{site}/b.png") && allowed.Fragment.Contains($"http:{site}/c.png")
              && !other.Fragment.Contains("src="),
              "T-01, con il mittente consentito le stesse forme restano come sono scritte (il motore le carica); file:, javascript:, blob: e un percorso di Windows si tolgono comunque");

        // Quello che non va toccato: indirizzi della pagina, immagini incorporate, dati, riferimenti interni dei disegni.
        const string normal =
            "<img src=\"foto.png\"><img src=\"/img/foto.png\"><img src=\"cid:logo\"><img src=\"data:image/gif;base64,R0lGODlhAQABAAAAACw=\">" +
            "<svg><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"/></linearGradient></defs>" +
            "<rect width=\"10\" height=\"10\" fill=\"url(#g)\" transform=\"translate(1,2) rotate(3)\" style=\"stroke:rgb(1,2,3)\"><animate attributeName=\"opacity\" values=\"0;1\" dur=\"1s\"/></rect>" +
            "<use href=\"#g\"/></svg><div style=\"background:url(data:image/gif;base64,R0lGODlhAQABAAAAACw=)\" title=\"//non è un indirizzo (x)\" data-nota=\"vedi (1)\">x</div>";
        var plain = Clean(normal);
        Check(plain.BlockedImages == 0 && plain.Fragment.Contains("src=\"foto.png\"") && plain.Fragment.Contains("src=\"/img/foto.png\"") && plain.Fragment.Contains("src=\"https://missiva.example/cid/1/logo\"")
              && plain.Fragment.Contains("src=\"data:image/gif;base64,R0lGODlhAQABAAAAACw=\"") && plain.Fragment.Contains("fill=\"url(#g)\"") && plain.Fragment.Contains("translate(1,2) rotate(3)")
              && plain.Fragment.Contains("values=\"0;1\"") && plain.Fragment.Contains("href=\"#g\"") && plain.Fragment.Contains("url(data:image/gif;base64,R0lGODlhAQABAAAAACw=)")
              && plain.Fragment.Contains("title=\"//non è un indirizzo (x)\"") && plain.Fragment.Contains("data-nota=\"vedi (1)\""),
              "T-01, quello che non porta fuori resta com'era: indirizzi relativi, immagini incorporate e data:, url(#id) e le trasformazioni dei disegni, un'animazione dell'opacità, i testi con parentesi");

        // Il CSS che la fine del testo chiude da sola, e le funzioni negli attributi dei disegni.
        (string What, string Html)[] css =
        [
            ("url( mai chiusa", $"<div style=\"background-image:url(http://{site}/a.png\">x</div>"),
            ("url(\" mai chiusa", $"<div style='background-image:url(\"http://{site}/a.png?a)b'>x</div>"),
            ("image-set( mai chiusa", $"<div style=\"background-image:image-set('http://{site}/a.png' 1x\">x</div>"),
            ("-webkit-image-set( mai chiusa", $"<div style=\"background-image:-webkit-image-set('http://{site}/a.png' 1x\">x</div>"),
            ("url( mai chiusa in fondo a un foglio", $"<style>.a{{background-image:url(http://{site}/a.png</style><div class=\"a\">x</div>"),
            ("URL( maiuscola mai chiusa", $"<div style=\"background-image:URL(  'http://{site}/a.png\">x</div>"),
        ];
        var open = css.Where(c => Clean(c.Html) is var r && (r.BlockedImages != 1 || System.Text.RegularExpressions.Regex.IsMatch(r.Fragment, @"(?i)(?<![-\w])(url|image-set)\("))).Select(c => c.What).ToList();
        (string What, string Html)[] drawn =
        [
            ("mask=url(http:\\\\\\\\sito)", $"<svg><rect mask=\"url(http:\\\\\\\\{site}/a.png)\"/></svg>"),
            ("cursor=url( 'http:/sito')", $"<svg><rect cursor=\"url( 'http:/{site}/a.png'), auto\"/></svg>"),
            ("fill=url(http:sito)", $"<svg><rect fill=\"url(http:{site}/a.svg#p)\"/></svg>"),
            ("mask=image-set('http://sito')", $"<svg><rect mask=\"image-set('http://{site}/a.png' 1x)\"/></svg>"),
            ("mask=u\\72l(http://sito)", $"<svg><rect mask=\"u\\72l(http://{site}/a.png)\"/></svg>"),
            ("set su href", $"<svg><image><set attributeName=\"href\" to=\"http://{site}/a.png\"/></image></svg>"),
            ("animate su href con values", $"<svg><image><animate attributeName=\"href\" values=\"#a;http://{site}/a.png\" dur=\"1s\"/></image></svg>"),
            ("animate su xlink:href", $"<svg><image><animate attributeName=\"xlink:href\" from=\"http:{site}/a.png\" to=\"http:{site}/b.png\" dur=\"1s\"/></image></svg>"),
            ("animate di fill con values", $"<svg><rect><animate attributeName=\"fill\" values=\"red;url(http:/{site}/a.svg#p)\" dur=\"1s\"/></rect></svg>"),
            ("values con un indirizzo", $"<svg><rect><animate attributeName=\"x\" values=\"1;/\\{site}/a.png\" dur=\"1s\"/></rect></svg>"),
        ];
        var left = drawn.Where(d => Clean(d.Html).Fragment.Contains(site)).Select(d => d.What).ToList();
        var loop = Clean("<svg><image width=\"10\" height=\"10\"><animate attributeName=\"href\" values=\"#a;#b\" dur=\"0.01s\" repeatCount=\"indefinite\"/></image></svg>", allow: true);
        Check(open.Count == 0 && left.Count == 0 && !loop.Fragment.Contains("animate"),
              $"T-01, url( e image-set( mai chiuse nel CSS ({css.Length} modi) diventano una funzione che non esiste e si contano; negli attributi e nelle animazioni dei disegni ({drawn.Length} modi) l'indirizzo esterno non resta; " +
              "un'animazione di href si toglie anche al mittente consentito" +
              (open.Count + left.Count == 0 ? "" : ": restano " + string.Join(" | ", open.Concat(left))));

        // Il costo delle regole nuove sui messaggi costruiti per farle lavorare: una passata, mai il quadrato.
        (string What, string Html)[] heavy =
        [
            ("250.000 url( mai chiuse in uno stile (1 MB)", "<div style=\"" + string.Concat(Enumerable.Repeat("url(", 250_000)) + "\">x</div>"),
            ("un attributo con 150.000 url(#a (1 MB)", "<svg><rect fill=\"" + string.Concat(Enumerable.Repeat("url(#a ", 150_000)) + "\"/></svg>"),
            ("values con 300.000 voci", "<svg><rect><animate attributeName=\"x\" values=\"" + string.Concat(Enumerable.Repeat("1;", 300_000)) + "\"/></rect></svg>"),
            ("src con 1 MB di tabulazioni davanti", "<img src=\"" + new string('\t', 1_000_000) + $"http:{site}/a.png\">"),
            ("un data: di 2 MB con un a capo ogni 76 caratteri", "<img src=\"data:image/png;base64," + string.Concat(Enumerable.Repeat(new string('A', 76) + "\n", 26_000)) + "\">"),
            ("10.000 immagini scritte http:\\\\sito", string.Concat(Enumerable.Repeat($"<img src=\"http:\\\\{site}/a.png\" width=\"20\" height=\"20\">", 10_000))),
        ];
        var times = new List<string>();
        long slowest = 0;
        foreach (var (what, html) in heavy)
        {
            Clean(html); // la prima volta paga anche le espressioni regolari
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = Clean(html);
            slowest = Math.Max(slowest, clock.ElapsedMilliseconds);
            times.Add($"{what} {clock.ElapsedMilliseconds} ms{(result.PlainOnly ? " (solo testo)" : "")}");
        }
        Check(slowest < 1500, "T-01, messaggi costruiti per far lavorare le regole nuove: " + string.Join("; ", times));

        // La pagina del riquadro si dà solo al documento: un'immagine dell'email con l'indirizzo "#a" è la pagina stessa.
        var page = UI.ReadingPage.Build([new UI.PageEntry(Sec2Mail("1", "Mario Rossi", "mario.rossi@example.com"), new MessageBody { Html = "<p>Prova</p>" }, true, false, null, false)], light: true);
        UI.ReadingView.RequestFate Fate(string address, CoreWebView2WebResourceContext context) => UI.ReadingView.FateOf(page, address, context, out _);
        Check(Fate("https://missiva.example/?v=1", CoreWebView2WebResourceContext.Document) == UI.ReadingView.RequestFate.Page
              && Fate("https://missiva.example/?v=1#a", CoreWebView2WebResourceContext.Image) == UI.ReadingView.RequestFate.Refused
              && Fate("https://missiva.example/", CoreWebView2WebResourceContext.Stylesheet) == UI.ReadingView.RequestFate.Refused
              && Fate($"https://{site}/a.png", CoreWebView2WebResourceContext.Image) == UI.ReadingView.RequestFate.Refused,
              "T-01, la pagina del riquadro si dà solo a chi la chiede come documento: chiesta come immagine o come foglio di stile è rifiutata");
    }

    static MessageSummary Sec2Mail(string id, string name, string email, string folder = "INBOX") =>
        new() { Id = id, FolderId = folder, Subject = "Prova", Date = DateTime.Now, From = new Address { Name = name, Email = email } };

    // ---------------------------------------------------------------- il motore vero, senza finestra

    /// <summary>Lavoro asincrono sul thread STA della suite con i messaggi di Windows che girano: WebView2 risponde lì.</summary>
    static T Sec2Pump<T>(Func<Task<T>> work, int seconds = 90)
    {
        var frame = new DispatcherFrame();
        T result = default!;
        Exception? error = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try { result = await work().WaitAsync(TimeSpan.FromSeconds(seconds)); }
            catch (Exception ex) { error = ex; }
            finally { frame.Continue = false; }
        }));
        Dispatcher.PushFrame(frame);
        if (error != null) throw new InvalidOperationException("motore del riquadro: " + error.Message, error);
        return result;
    }

    /// <summary>
    /// Il motore del riquadro (WebView2, lo stesso Runtime) senza finestra: figlio di HWND_MESSAGE, con il confino e la
    /// regola delle richieste del riquadro vero. Ogni richiesta che il motore fa passa da qui e si registra con quello che
    /// il riquadro ne farebbe; quelle che lascerebbe andare in rete partono solo verso l'ascoltatore di prova (ListenerPort),
    /// e nessun nome si risolve (--host-resolver-rules): fuori da 127.0.0.1 non si arriva.
    /// </summary>
    sealed class Sec2Engine
    {
        CoreWebView2Controller controller = null!;
        CoreWebView2 core = null!;
        UI.ReadingPage? page;
        string? raw;
        int number;
        TaskCompletionSource<bool> loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(string Url, string Context, UI.ReadingView.RequestFate Fate)> Requests { get; } = new();
        public int ListenerPort { get; set; }
        public string Version => core.Environment.BrowserVersionString;

        public static async Task<Sec2Engine> StartAsync()
        {
            var engine = new Sec2Engine();
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(EngineRoot, "WebView2-prove"), new CoreWebView2EnvironmentOptions
            {
                Language = "it-IT",
                AdditionalBrowserArguments = UI.ReadingView.BrowserArguments + " --host-resolver-rules=\"MAP * ~NOTFOUND, EXCLUDE 127.0.0.1\"",
            });
            engine.controller = await env.CreateCoreWebView2ControllerAsync(new IntPtr(-3)); // HWND_MESSAGE
            engine.controller.Bounds = new System.Drawing.Rectangle(0, 0, 900, 700);
            engine.core = engine.controller.CoreWebView2;
            UI.ReadingView.Confine(engine.core);
            engine.core.WebResourceRequested += engine.OnRequest;
            engine.core.NavigationCompleted += (_, _) => engine.loaded.TrySetResult(true);
            return engine;
        }

        void OnRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var env = core.Environment;
            if (raw != null)
            {
                bool own = Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var u) && u.Host == UI.ReadingPage.Host && u.AbsolutePath == "/";
                Requests.Add((e.Request.Uri, e.ResourceContext.ToString(), own ? UI.ReadingView.RequestFate.Page : UI.ReadingView.RequestFate.Refused));
                e.Response = own ? UI.ReadingView.Serve(env, Encoding.UTF8.GetBytes(raw), "text/html; charset=utf-8") : UI.ReadingView.Refuse(env);
                return;
            }
            var fate = UI.ReadingView.FateOf(page!, e.Request.Uri, e.ResourceContext, out var resource);
            Requests.Add((e.Request.Uri, e.ResourceContext.ToString(), fate));
            switch (fate)
            {
                case UI.ReadingView.RequestFate.Page:
                    e.Response = UI.ReadingView.Serve(env, Encoding.UTF8.GetBytes(page!.Html), "text/html; charset=utf-8");
                    break;
                case UI.ReadingView.RequestFate.Resource:
                    e.Response = UI.ReadingView.Serve(env, resource.Data, resource.Type);
                    break;
                case UI.ReadingView.RequestFate.Network:
                    // Il riquadro la lascerebbe andare: qui parte davvero solo verso l'ascoltatore di prova.
                    if (!(Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var target) && target.IsLoopback && target.Port == ListenerPort))
                        e.Response = UI.ReadingView.Refuse(env);
                    break;
                default:
                    e.Response = UI.ReadingView.Refuse(env);
                    break;
            }
        }

        /// <summary>Una pagina del riquadro, com'è: servita da https://missiva.example/ e mostrata come fa ReadingView.</summary>
        public Task ShowAsync(UI.ReadingPage shown, int settle = 500)
        {
            (page, raw) = (shown, null);
            return NavigateAsync(settle);
        }

        /// <summary>Una pagina scritta a mano dalla stessa origine: tutto il resto è rifiutato, e quello che chiede si registra.</summary>
        public Task ShowRawAsync(string html, int settle = 0)
        {
            (page, raw) = (null, html);
            return NavigateAsync(settle);
        }

        async Task NavigateAsync(int settle)
        {
            Requests.Clear();
            loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            core.Navigate($"{UI.ReadingPage.Origin}/?v={++number}");
            await loaded.Task;
            // Come in una finestra: la pagina si mostra, e stili e disposizione si calcolano (le immagini di sfondo partono da lì).
            await core.ExecuteScriptAsync(
                "(() => { document.documentElement.classList.remove('fitting'); const roots = [document]; " +
                "for (const m of document.querySelectorAll('.mail')) if (m.shadowRoot) roots.push(m.shadowRoot); " +
                "for (const r of roots) for (const e of r.querySelectorAll('*')) getComputedStyle(e).backgroundImage; return document.body.offsetHeight; })()");
            if (settle > 0) await Task.Delay(settle);
        }

        /// <summary>Uno script di Missiva nella pagina (gira anche con JavaScript spento per la pagina): il suo risultato, da JSON.</summary>
        public async Task<JsonElement> ScriptAsync(string code) => JsonDocument.Parse(await core.ExecuteScriptAsync(code)).RootElement.Clone();

        public void Close()
        {
            try { controller.Close(); } catch (Exception) { }
        }
    }

    /// <summary>Un ascoltatore su 127.0.0.1 che risponde con un'immagine di un pixel e ricorda che cosa gli è stato chiesto.</summary>
    sealed class Sec2Listener : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly List<string> asked = new();
        static readonly byte[] Gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

        public int Port { get; }
        /// <summary>I percorsi chiesti in HTTP; "(non HTTP)" per una connessione che comincia in un altro modo (TLS).</summary>
        public List<string> Asked { get { lock (asked) return asked.ToList(); } }

        public Sec2Listener()
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var client = await listener.AcceptTcpClientAsync();
                        _ = Task.Run(() => Serve(client));
                    }
                }
                catch (Exception) { } // chiuso
            });
        }

        void Serve(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 2000;
                    var stream = client.GetStream();
                    var buffer = new byte[4096];
                    int read = 0;
                    while (read < buffer.Length && !Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n"))
                    {
                        int n = stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                        if (read >= 3 && Encoding.ASCII.GetString(buffer, 0, 3) != "GET") break;
                    }
                    var text = Encoding.ASCII.GetString(buffer, 0, read);
                    var first = text.Split('\r', '\n')[0].Split(' ');
                    lock (asked) asked.Add(first.Length >= 2 && first[0] == "GET" ? first[1] : "(non HTTP)");
                    if (first.Length < 2 || first[0] != "GET") return;
                    stream.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: image/gif\r\nContent-Length: {Gif.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"));
                    stream.Write(Gif);
                }
            }
            catch (Exception) { }
        }

        public void Dispose() => listener.Stop();
    }

    // ---------------------------------------------------------------- T-01

    /// <summary>
    /// T-01: le immagini esterne scritte come le legge il motore e non come le scriveva la pulizia ("http:\\host", "/\host",
    /// "http:host", con una tabulazione in mezzo). In una conversazione con un messaggio espanso di un mittente consentito
    /// la pagina lascia passare le immagini, e quelle del mittente non consentito partivano.
    /// </summary>
    static void Sec2Images()
    {
        using var listener = new Sec2Listener();
        string host = $"127.0.0.1:{listener.Port}";
        // Il messaggio del mittente consentito: un'immagine per ogni strada, scritta nel modo di sempre. Se queste non
        // arrivano, il motore senza finestra non sta provando quella strada.
        string allowedHtml =
            $"<p>Notizie</p><img src=\"http://{host}/consentita-img.gif\" width=\"20\" height=\"20\">" +
            $"<table><tr><td background=\"http://{host}/consentita-sfondo.gif\">x</td></tr></table>" +
            $"<div style=\"background-image:url(http://{host}/consentita-stile.gif)\">y</div>" +
            $"<svg width=\"10\" height=\"10\"><image href=\"http://{host}/consentita-svg.gif\" width=\"10\" height=\"10\"/></svg>";
        (string What, string Html)[] forms =
        [
            ("img, http:\\\\host", "<img src=\"http:\\\\{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, \\\\host", "<img src=\"\\\\{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, /\\host", "<img src=\"/\\{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, \\/host", "<img src=\"\\/{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, http:/host", "<img src=\"http:/{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, http:host", "<img src=\"http:{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, una tabulazione nello schema", "<img src=\"ht&#9;tp://{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, un a capo fra le barre", "<img src=\"http:/&#10;/{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, un carattere di controllo davanti", "<img src=\"&#1;http://{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, HTTP:\\/host", "<img src=\"HTTP:\\/{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, https:\\\\host", "<img src=\"https:\\\\{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, un a capo davanti a //host (riconosciuta anche prima)", "<img src=\"&#10;//{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img, //host (riconosciuta anche prima)", "<img src=\"//{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("td background, http:\\\\host", "<table><tr><td background=\"http:\\\\{H}/{K}.gif\">x</td></tr></table>"),
            ("td background, /\\host", "<table><tr><td background=\"/\\{H}/{K}.gif\">x</td></tr></table>"),
            ("table background, http:host", "<table background=\"http:{H}/{K}.gif\"><tr><td>x</td></tr></table>"),
            ("stile, url(http:\\\\host)", "<div style=\"background-image:url(http:\\\\{H}/{K}.gif)\">x</div>"),
            ("stile, url('http:/host')", "<div style=\"background-image:url('http:/{H}/{K}.gif')\">x</div>"),
            ("svg image href, http:\\\\host", "<svg width=\"10\" height=\"10\"><image href=\"http:\\\\{H}/{K}.gif\" width=\"10\" height=\"10\"/></svg>"),
            ("svg image xlink:href, http:host", "<svg width=\"10\" height=\"10\"><image xlink:href=\"http:{H}/{K}.gif\" width=\"10\" height=\"10\"/></svg>"),
            ("svg set to, http://host", "<svg width=\"10\" height=\"10\"><image width=\"10\" height=\"10\"><set attributeName=\"href\" to=\"http://{H}/{K}.gif\"/></image></svg>"),
            ("svg set to, http:\\\\host", "<svg width=\"10\" height=\"10\"><image width=\"10\" height=\"10\"><set attributeName=\"href\" to=\"http:\\\\{H}/{K}.gif\"/></image></svg>"),
            ("svg animate values, #a;http://host", "<svg width=\"10\" height=\"10\"><image width=\"10\" height=\"10\"><animate attributeName=\"href\" values=\"#a;http://{H}/{K}.gif\" dur=\"0.2s\" repeatCount=\"indefinite\"/></image></svg>"),
            ("svg animate from/to, http:host", "<svg width=\"10\" height=\"10\"><image width=\"10\" height=\"10\"><animate attributeName=\"href\" from=\"http:{H}/{K}.gif\" to=\"http:{H}/{K}.gif\" dur=\"0.2s\" repeatCount=\"indefinite\"/></image></svg>"),
            ("svg mask=url(http://host)", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" mask=\"url(http://{H}/{K}.gif)\"/></svg>"),
            ("svg mask=url(http:\\\\\\\\host)", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" mask=\"url(http:\\\\\\\\{H}/{K}.gif)\"/></svg>"),
            ("svg cursor=url(http:\\\\\\\\host)", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" cursor=\"url(http:\\\\\\\\{H}/{K}.gif), auto\"/></svg>"),
            ("svg filter=u\\72l(//host)", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" filter=\"u\\72l(//{H}/{K}.svg#f)\"/></svg>"),
            ("svg fill=url(http:/host)", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" fill=\"url(http:/{H}/{K}.svg#p)\"/></svg>"),
            ("img srcset, http:\\\\host", "<img srcset=\"http:\\\\{H}/{K}.gif 1x\" width=\"20\" height=\"20\">"),
            ("img, file://host", "<img src=\"file://{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("body background, http:\\\\host", "<body background=\"http:\\\\{H}/{K}.gif\">"),
            ("stile, url( mai chiusa", "<div style=\"background-image:url(http://{H}/{K}.gif\">x</div>"),
            ("stile, url(\" mai chiusa con una parentesi dentro", "<div style='background-image:url(\"http://{H}/{K}.gif?a)b'>x</div>"),
            ("stile, url(' ') con una barra rovesciata e mai chiusa", "<div style=\"background-image:url('http://{H}/{K}.gif\\\">x</div>"),
            ("stile, image-set( mai chiusa", "<div style=\"background-image:image-set('http://{H}/{K}.gif' 1x\">x</div>"),
            ("stile, -webkit-image-set( mai chiusa", "<div style=\"background-image:-webkit-image-set('http://{H}/{K}.gif' 1x\">x</div>"),
            ("stile, src('http://host')", "<div style=\"background-image:src('http://{H}/{K}.gif')\">x</div>"),
            ("stile, image('http://host')", "<div style=\"background-image:image('http://{H}/{K}.gif')\">x</div>"),
            ("stile, cross-fade(url(http:/host))", "<div style=\"background-image:cross-fade(url(http:/{H}/{K}.gif), red)\">x</div>"),
            ("foglio di stile, url( mai chiusa in fondo", "<style>.sfondo-{K}{background-image:url(http://{H}/{K}.gif</style><div class=\"sfondo-{K}\">x</div>"),
            ("svg mask=image-set('http://host')", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" mask=\"image-set('http://{H}/{K}.gif' 1x)\"/></svg>"),
            ("svg cursor=url(http://host mai chiusa", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" cursor=\"url(http://{H}/{K}.gif\"/></svg>"),
            ("svg fill=url( 'http:\\\\\\\\host')", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" fill=\"url( 'http:\\\\\\\\{H}/{K}.svg#p')\"/></svg>"),
            ("svg mask=u\\72l(http://host)", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" mask=\"u\\72l(http://{H}/{K}.gif)\"/></svg>"),
            ("svg set xlink:href, http:host", "<svg width=\"10\" height=\"10\"><image width=\"10\" height=\"10\"><set attributeName=\"xlink:href\" to=\"http:{H}/{K}.gif\"/></image></svg>"),
            ("svg set fill, url(http:/host)", "<svg width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\"><set attributeName=\"fill\" to=\"url(http:/{H}/{K}.svg#p)\"/></rect></svg>"),
            ("svg animate values, #a;http:\\\\host", "<svg width=\"10\" height=\"10\"><image width=\"10\" height=\"10\"><animate attributeName=\"href\" values=\"#a;http:\\\\{H}/{K}.gif\" dur=\"0.2s\" repeatCount=\"indefinite\"/></image></svg>"),
            ("svg feImage href, http:\\\\host", "<svg width=\"10\" height=\"10\"><filter id=\"f-{K}\"><feImage href=\"http:\\\\{H}/{K}.gif\"/></filter><rect width=\"10\" height=\"10\" filter=\"url(#f-{K})\"/></svg>"),
            ("svg use href, http:\\\\host", "<svg width=\"10\" height=\"10\"><use href=\"http:\\\\{H}/{K}.svg#a\"/></svg>"),
            ("image al posto di img, http:\\\\host", "<image src=\"http:\\\\{H}/{K}.gif\" width=\"20\" height=\"20\">"),
            ("img dentro foreignObject, http:host", "<svg width=\"30\" height=\"30\"><foreignObject width=\"30\" height=\"30\"><img src=\"http:{H}/{K}.gif\" width=\"20\" height=\"20\"></foreignObject></svg>"),
            ("img, uno spazio unificatore davanti (per il motore è un percorso della pagina)", "<img src=\"&#160;http://{H}/{K}.gif\" width=\"20\" height=\"20\">"),
        ];
        var hostileHtml = new StringBuilder("<p>Avviso</p>");
        for (int k = 0; k < forms.Length; k++) hostileHtml.Append(forms[k].Html.Replace("{H}", host).Replace("{K}", $"bloccata-{k}")).Append('\n');

        UI.ReadingPage Page(bool withAllowed)
        {
            var entries = new List<UI.PageEntry>();
            if (withAllowed) entries.Add(new UI.PageEntry(Sec2Mail("1", "Negozio di prova", "notizie@negozio-prova.example"), new MessageBody { Html = allowedHtml }, true, false, null, true));
            entries.Add(new UI.PageEntry(Sec2Mail("2", "Mittente di prova", "x@dominio-ostile.example"), new MessageBody { Html = hostileHtml.ToString() }, true, false, null, false));
            return UI.ReadingPage.Build(entries, light: true, header: new UI.PageHeader("Prova", false));
        }

        // Le forme a migliaia, per il confronto con il motore: quello che il motore toglie o ignora davanti, uno schema, le
        // barre, il resto. Nessuna parte ha virgolette; NUL non c'è (l'HTML lo cambia prima che l'indirizzo si legga).
        // I caratteri che non si vedono sono scritti con il loro numero: tabulazione verticale, due di controllo, spazio
        // unificatore, spazio ideografico, segno dell'ordine dei byte, spazio a larghezza zero, riga successiva.
        static string Ch(int code) => ((char)code).ToString();
        string[] before = ["", " ", "\t", "\n", "\r", "\f", Ch(0x0B), Ch(0x01), Ch(0x1F), Ch(0xA0), Ch(0x3000), Ch(0xFEFF), Ch(0x200B), Ch(0x85)];
        string[] schemes =
        [
            "", "http:", "https:", "HTTP:", "hTtPs:", "ht\ttp:", "h\nttps:", "http\r:", "http :", "ftp:", "ws:", "wss:", "file:", "javascript:", "data:",
            "blob:", "about:", "x:", "c:", "http%3A", Ch(0xFF48) + "ttp:", "http" + Ch(0xFF1A), "cid:", "mailto:", ":", "1http:", "+http:", "ht.tp:", "view-source:",
        ];
        string[] bars = ["", "/", "\\", "//", "\\\\", "/\\", "\\/", "///", "\\\\\\", "/\t/", "\\\n\\", "/ /", "/./", ".//", "?//", "#//", "\\\t/"];
        string[] rests =
        [
            "esterno.test/x.gif", "missiva.example/x.gif", "missiva.example@esterno.test/x.gif", "@esterno.test/x.gif", "missiva" + Ch(0x3002) + "example/x.gif",
            "MISSIVA.EXAMPLE/x.gif", "missiva.example:443/x.gif", "esterno.test\\x.gif", "", "x.gif", "[2001:db8::1]/x.gif", "esterno.test:8080/x.gif",
        ];
        var shapes = (from b in before from s in schemes from l in bars from r in rests select b + s + l + r).Distinct().ToList();
        // Un campione passa anche dalla pulizia vera e torna al motore dentro la pagina, come elemento: una forma ogni 37.
        var sample = shapes.Where((_, k) => k % 37 == 0).ToList();
        static string Attribute(string value)
        {
            var text = new StringBuilder(value.Length + 8);
            foreach (char c in value)
                text.Append(c is '&' or '"' or '<' or '>' || c < ' ' || c > '~' ? $"&#{(int)c};" : c.ToString());
            return text.ToString();
        }
        var rawPage = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'\"></head><body>");
        for (int k = 0; k < sample.Count; k++)
        {
            rawPage.Append($"<img data-raw=\"{k}\" src=\"{Attribute(sample[k])}\">");
            var cleaned = MailHtml.Prepare($"<img src=\"{Attribute(sample[k])}\" width=\"20\" height=\"20\"><table><tr><td background=\"{Attribute(sample[k])}\">x</td></tr></table>", false, false, _ => null);
            rawPage.Append($"<div data-clean=\"{k}\">{cleaned.Fragment}</div>");
        }
        rawPage.Append("</body></html>");
        const string Reading = "(u => u.protocol + '|' + u.hostname)";
        string readShapes = "(() => { const read = " + Reading + "; return " + JsonSerializer.Serialize(shapes) +
                            ".map(f => { try { return read(new URL(f, document.baseURI)); } catch (e) { return 'non valido|'; } }); })()";
        const string ReadElements =
            "(() => { const read = " + Reading + "; const at = a => { try { return read(new URL(a, document.baseURI)); } catch (e) { return 'non valido|'; } }; " +
            "const raw = [...document.querySelectorAll('img[data-raw]')].map(i => i.dataset.raw + '|' + at(i.src)); " +
            "const clean = []; for (const d of document.querySelectorAll('div[data-clean]')) { " +
            "for (const i of d.querySelectorAll('img')) clean.push(d.dataset.clean + '|img|' + at(i.getAttribute('src') ?? '')); " +
            "for (const t of d.querySelectorAll('[background]')) clean.push(d.dataset.clean + '|sfondo|' + at(t.getAttribute('background'))); } return { raw, clean }; })()";

        var (version, mixed, alone, mixedAsked, engineShapes, engineElements) = Sec2Pump(async () =>
        {
            var engine = await Sec2Engine.StartAsync();
            try
            {
                engine.ListenerPort = listener.Port;
                await engine.ShowAsync(Page(withAllowed: true), settle: 1200);
                var first = engine.Requests.ToList();
                var asked = listener.Asked;
                await engine.ShowAsync(Page(withAllowed: false), settle: 600);
                var second = engine.Requests.ToList();
                await engine.ShowRawAsync(rawPage.ToString());
                var readings = (await engine.ScriptAsync(readShapes)).EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                var elements = await engine.ScriptAsync(ReadElements);
                return (engine.Version, first, second, asked, readings, elements);
            }
            finally
            {
                engine.Close();
            }
        });

        // Con MISSIVA_PROVA_MOTORE=file ogni richiesta del motore, con quello che il riquadro ne farebbe, finisce nel file.
        if (Environment.GetEnvironmentVariable("MISSIVA_PROVA_MOTORE") is { Length: > 0 } dump)
            File.WriteAllLines(dump, mixed.Select(r => $"conversazione\t{r.Fate}\t{r.Context}\t{r.Url}").Concat(alone.Select(r => $"da solo\t{r.Fate}\t{r.Context}\t{r.Url}")));
        static bool Out((string Url, string Context, UI.ReadingView.RequestFate Fate) r) => r.Fate == UI.ReadingView.RequestFate.Network;
        var controls = new[] { "img", "sfondo", "stile", "svg" }.Where(c => mixed.Any(r => Out(r) && r.Url.Contains($"consentita-{c}.")) && mixedAsked.Contains($"/consentita-{c}.gif")).ToList();
        var leaked = Enumerable.Range(0, forms.Length).Where(k => mixed.Any(r => Out(r) && r.Url.Contains($"bloccata-{k}."))).ToList();
        var seen = Enumerable.Range(0, forms.Length).Where(k => mixed.Any(r => r.Url.Contains($"bloccata-{k}."))).ToList();
        Report.Add($"      (motore: WebView2 {version}; nella conversazione il motore ha fatto {mixed.Count} richieste, {mixed.Count(Out)} lasciate andare; all'ascoltatore: {string.Join(" ", mixedAsked)})");
        foreach (var k in seen)
            Report.Add($"      ({forms[k].What}: {string.Join(" | ", mixed.Where(r => r.Url.Contains($"bloccata-{k}.")).Select(r => $"{r.Url} [{r.Context}, {r.Fate}]"))})");
        Check(controls.Count == 4,
              $"T-01, il motore vero senza finestra carica le immagini del mittente consentito, e arrivano all'ascoltatore su 127.0.0.1 (strade: {string.Join(", ", controls)}): la prova vede quello che partirebbe");
        Check(leaked.Count == 0,
              $"T-01, conversazione con un mittente consentito espanso: nessuna immagine del mittente non consentito parte, scritta in {forms.Length} modi" +
              (leaked.Count == 0 ? "" : $": partono {leaked.Count}, " + string.Join("; ", leaked.Select(k => forms[k].What))));
        // Il confronto con il motore: per ogni forma, dove la legge lui e che cosa ne dice la pulizia.
        static bool Outside(string reading) => reading.Split('|') is [("http:" or "https:"), var where, ..] && where != MailHtml.PageHost;
        static bool OldRule(string s) => s.TrimStart() is var t && (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("//"));
        static string Shown(string s) => string.Concat(s.Select(c => c < ' ' || c > '~' ? $"[{(int)c:X2}]" : c.ToString()));
        int outside = 0, missedBefore = 0, cautious = 0;
        var missed = new List<string>();
        var table = new SortedDictionary<string, int>();
        for (int k = 0; k < shapes.Count; k++)
        {
            var kind = MailHtml.KindOf(shapes[k]);
            bool away = Outside(engineShapes[k]);
            if (away) outside++;
            if (away && !OldRule(shapes[k])) missedBefore++;
            if (away && kind != MailHtml.UrlKind.Remote) missed.Add(Shown(shapes[k]) + " → " + engineShapes[k]);
            if (!away && kind == MailHtml.UrlKind.Remote) cautious++;
            var cell = $"{kind}: {(away ? "fuori" : engineShapes[k].Split('|')[0] is "https:" ? "pagina" : engineShapes[k].Split('|')[0])}";
            table[cell] = table.GetValueOrDefault(cell) + 1;
        }
        var position = shapes.Select((s, k) => (s, k)).ToDictionary(x => x.s, x => x.k);
        var rawRead = engineElements.GetProperty("raw").EnumerateArray().Select(x => x.GetString()!.Split('|', 2)).ToList();
        // Lo stesso schema e, per il web, lo stesso sito: di un data: il "sito" non conta (il motore lo scrive in due modi).
        static bool SameReading(string a, string b) => a == b || a.Split('|')[0] == b.Split('|')[0] && a.Split('|')[0] is not ("http:" or "https:");
        int sameAsElement = rawRead.Count(r => SameReading(engineShapes[position[sample[int.Parse(r[0])]]], r[1]));
        foreach (var r in rawRead.Where(r => !SameReading(engineShapes[position[sample[int.Parse(r[0])]]], r[1])).Take(5))
            Report.Add($"      (letta diversa come immagine: {Shown(sample[int.Parse(r[0])])} → {r[1]} come immagine, {engineShapes[position[sample[int.Parse(r[0])]]]} come indirizzo)");
        var cleanRead = engineElements.GetProperty("clean").EnumerateArray().Select(x => x.GetString()!.Split('|', 3)).ToList();
        var cleanOut = cleanRead.Where(r => Outside(r[2])).Select(r => $"{r[1]} {Shown(sample[int.Parse(r[0])])} → {r[2]}").ToList();
        string[] reported = ["http:\\\\esterno.test/x.gif", "\\\\esterno.test/x.gif", "/\\esterno.test/x.gif", "http:/esterno.test/x.gif", "http:esterno.test/x.gif", "ht\ttp://esterno.test/x.gif", "https:esterno.test/x.gif"];
        Report.Add($"      (come li legge il motore: {string.Join("; ", reported.Select(f => $"{Shown(f)} → {engineShapes[position[f]].TrimEnd('|')}"))})");
        Report.Add($"      (pulizia e motore su {shapes.Count} forme: {string.Join("; ", table.Select(c => $"{c.Key} {c.Value}"))})");
        Check(shapes.Count > 50_000 && outside > 5_000 && missed.Count == 0 && sameAsElement == rawRead.Count && rawRead.Count == sample.Count,
              $"T-01, {shapes.Count} forme di indirizzo lette dal motore vero: per {outside} esce dalla pagina, e la pulizia le dà tutte per esterne (con la regola di prima ne sfuggivano {missedBefore}; " +
              $"date per esterne e per il motore no, {cautious}); {sameAsElement} su {rawRead.Count} lette uguali anche come immagini della pagina" +
              (missed.Count == 0 ? "" : $": sfuggono {missed.Count}, " + string.Join("; ", missed.Take(12))));
        Check(cleanRead.Count >= sample.Count && cleanOut.Count == 0,
              $"T-01, {sample.Count} forme passate dalla pulizia e rilette dal motore dentro la pagina: nessuna immagine e nessuno sfondo porta fuori ({cleanRead.Count} indirizzi rimasti)" +
              (cleanOut.Count == 0 ? "" : $": fuori {cleanOut.Count}, " + string.Join("; ", cleanOut.Take(12))));

        var aloneOut = alone.Where(Out).ToList();
        int pages = alone.Count(r => r.Fate == UI.ReadingView.RequestFate.Page), elsewhere = alone.Count(r => !r.Url.StartsWith(UI.ReadingPage.Origin + "/"));
        Check(aloneOut.Count == 0 && pages == 1 && elsewhere == 0,
              $"T-01, lo stesso messaggio da solo: nessuna richiesta lasciata andare, e la pagina si dà una volta sola, al documento ({alone.Count} richieste del motore, {aloneOut.Count} verso la rete, {elsewhere} fuori dalla pagina, pagina servita {pages} volte)");
    }
}
#endif
