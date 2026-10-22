#if DEBUG
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MailKit;
using MimeKit;

namespace Missiva;

/// <summary>
/// Revisione del 13 ottobre 2026: sei consigli di sicurezza sul modulo Core (docs\audit\sicurezza-core.md, K-01..K-06;
/// l'esito in docs\audit\ESITO.md). Il revisore ha letto solo Core, senza eseguire: ogni punto qui si riproduce su dati
/// finti prima di dirlo vero. Nessuna finestra, nessuna rete: Graph è il gestore HTTP finto. Con
/// "Missiva.exe --prova-audit risultato.txt core" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void CoreReviewTests()
    {
        Report.Add("--- Revisione del 13 ottobre: sei consigli sul modulo Core (K-01..K-06)");
        K1FileNames();
        K2Suffixes();
        K3GraphIdsAsync().GetAwaiter().GetResult();
        K4QuotedHtmlAsync().GetAwaiter().GetResult();
        K5Links();
        K6Signatures();
        Report.Add("--- Scelte del 14 ottobre: i suffissi di .it dalla lista pubblica (K-02), le voci larghe di \"Mostra immagini\" all'avvio (scelta 25)");
        K2PublicList();
        K2BroadAtStart();
    }

    // ---------------------------------------------------------------- K-01: il taglio a 150 caratteri e i nomi riservati

    /// <summary>Per Windows il nome è un dispositivo: la parte prima del primo punto, senza gli spazi in fondo (come Sec2Attachments).</summary>
    static bool K1Device(string name) => name.Split('.')[0].TrimEnd(' ').ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
        || System.Text.RegularExpressions.Regex.IsMatch(name.Split('.')[0].TrimEnd(' '), "^(?i:COM|LPT)[0-9¹²³]$");

    /// <summary>SafeFileName com'era fino al 13 ottobre 2026 (prima il nome riservato, poi il taglio), per il confronto.</summary>
    static string K1Before(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '\\', '/' }).ToArray();
        var clean = string.Concat(name.Split(invalid)).Trim().TrimEnd('.', ' ');
        if (clean.Length == 0) return "allegato";
        if (K1Device(clean)) clean = "_" + clean;
        return clean.Length > 150 ? clean[^150..] : clean;
    }

    /// <summary>
    /// K-01: SafeFileName guardava i nomi riservati prima di tagliare a 150 caratteri, e il taglio tiene la fine del nome:
    /// un nome lungo poteva uscire dal taglio come un nome di dispositivo (NUL.txt), o con uno spazio davanti. Un nome
    /// riservato lungo giusto 150 perdeva nel taglio il "_" appena messo. Il nome di un allegato si pulisce alla nascita
    /// (CleanAttachmentName) e di nuovo al salvataggio: i due passaggi devono dare la stessa stringa.
    /// </summary>
    static void K1FileNames()
    {
        static string Rep(char c, int n) => new(c, n);
        // L'inizio di un nome, per dirlo nel risultato; mezzo carattere diventa "?" (il file del risultato è UTF-8, e non lo accetta).
        static string Start(string name) => string.Concat((name.Length > 14 ? name[..14] + "..." : name).Select(c => char.IsSurrogate(c) ? '?' : c));
        (string Name, string What)[] cut =
        [
            (Rep('a', 10) + "NUL." + Rep('x', 142) + ".txt", "dopo il taglio comincia con NUL."),
            (Rep('a', 30) + "con." + Rep('x', 146), "dopo il taglio comincia con con."),
            (Rep('a', 5) + "COM1 ." + Rep('x', 140) + ".pdf", "dopo il taglio comincia con COM1 e uno spazio"),
            (Rep('a', 5) + "LPT" + (char)0xB9 + "." + Rep('x', 141) + ".docx", "dopo il taglio comincia con LPT e la cifra in apice"),
            ("NUL." + Rep('x', 146), "riservato e lungo giusto 150: il taglio toglieva il \"_\" appena messo"),
            ("CON ." + Rep('x', 141) + ".exe", "riservato con uno spazio, lungo giusto 150, con un'estensione che chiede conferma"),
            (Rep('a', 10) + " " + Rep('b', 145) + ".pdf", "dopo il taglio comincia con uno spazio"),
            (Rep('a', 10) + " . " + Rep('b', 143) + ".pdf", "dopo il taglio comincia con un punto e uno spazio"),
            (Rep('a', 10) + " NUL." + Rep('x', 141) + ".txt", "dopo il taglio comincia con uno spazio e poi NUL."),
            (Rep('a', 10) + char.ConvertFromUtf32(0x1F600) + Rep('b', 145) + ".pdf", "il taglio cade in mezzo a un carattere fatto di due metà"),
        ];
        var wrong = new List<string>();
        foreach (var (name, what) in cut)
        {
            string safe = Safety.SafeFileName(name), shown = Safety.CleanAttachmentName(name);
            bool ok = safe.Length is > 0 and <= 150 && !K1Device(safe) && safe[0] is not (' ' or '.') && !char.IsLowSurrogate(safe[0])
                      && Safety.SafeFileName(safe) == safe && shown == safe && Safety.CleanAttachmentName(shown) == shown
                      && Path.GetExtension(safe) == Path.GetExtension(name) && Safety.IsDangerous(safe) == Safety.IsDangerous(name);
            if (!ok) wrong.Add($"{what}: esce \"{Start(safe)}\" ({safe.Length} caratteri), al secondo passaggio \"{Start(Safety.SafeFileName(safe))}\"");
        }
        Check(wrong.Count == 0,
              $"K-01, {cut.Length} nomi di allegato più lunghi di 150 caratteri, o riservati e lunghi giusto 150: dopo il taglio non sono un nome di dispositivo, non cominciano con uno spazio, un punto o mezzo carattere, " +
              "tengono l'estensione, e il nome mostrato (CleanAttachmentName) è quello con cui si salva (SafeFileName, anche al secondo passaggio)" + (wrong.Count == 0 ? "" : ": " + string.Join(" | ", wrong)));

        // Nomi fatti a pezzi (parole riservate, punti, spazi, pezzi lunghi, estensioni, caratteri che non si vedono), sempre
        // gli stessi: nessuno esce come un dispositivo o più lungo di 150, il secondo passaggio non cambia nulla, e chi con la
        // regola di prima era già a posto resta com'era.
        string[] pieces = ["NUL", "con", "COM1", "LPT" + (char)0xB9, "CONIN$", "aux", "prn", ".", "..", " ", "  ", "_", "a", "Relazione", "fattura 2026", ".pdf", ".exe", ".tar.gz", ".txt",
                           Rep('x', 40), Rep('y', 149), Rep('z', 150), ((char)0x202E).ToString(), ((char)0x200B).ToString(), ((char)0xA0).ToString(), "\\", ":", "è"];
        var random = new Random(20261013);
        int made = 0, device = 0, tooLong = 0, unstable = 0, shownOther = 0, changed = 0, kept = 0;
        string? example = null;
        for (int i = 0; i < 4000; i++)
        {
            var name = string.Concat(Enumerable.Range(0, random.Next(1, 7)).Select(_ => pieces[random.Next(pieces.Length)]));
            string safe = Safety.SafeFileName(name), before = K1Before(new string(name.Where(c => c is not ((char)0x202E or (char)0x200B)).ToArray()));
            made++;
            bool bad = false;
            if (K1Device(safe)) { device++; bad = true; }
            if (safe.Length is 0 or > 150) { tooLong++; bad = true; }
            if (Safety.SafeFileName(safe) != safe) { unstable++; bad = true; }
            if (Safety.CleanAttachmentName(name) is var shown && name.Length > 0 && shown != safe) { shownOther++; bad = true; }
            // Com'era prima, dove prima andava bene: non un dispositivo, già stabile, senza spazi o punti davanti rimasti da un taglio.
            bool fine = !K1Device(before) && K1Before(before) == before && !(before.Length == 150 && before[0] is ' ' or '.');
            if (fine && safe == before) kept++;
            else if (fine) { changed++; bad = true; }
            if (bad) example ??= string.Concat((name.Length > 60 ? name[..60] + "..." : name).Select(c => c < ' ' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? '?' : c));
        }
        Check(device + tooLong + unstable + shownOther + changed == 0 && kept > made / 2,
              $"K-01, {made} nomi fatti a pezzi (parole riservate, punti, spazi, pezzi lunghi, estensioni, caratteri che non si vedono): nessuno esce come un dispositivo ({device}), vuoto o oltre 150 ({tooLong}), " +
              $"diverso al secondo passaggio ({unstable}) o diverso dal nome mostrato ({shownOther}); i {kept} che la regola di prima lasciava a posto escono uguali (cambiati: {changed})" +
              (example == null ? "" : $"; il primo che non va: \"{example}\""));
    }

    // ---------------------------------------------------------------- K-02: gov.it, edu.it e i nomi geografici di .it

    /// <summary>
    /// K-02: i suffissi di .it sotto cui enti diversi registrano ognuno il suo nome. Tre regole ricavano l'organizzazione da
    /// un nome, ognuna con il suo elenco: quella dei loghi e del segno "autenticato" (SenderLogos.DomainOf), quella di
    /// "Mostra immagini" (Settings.ImageKey) e quella degli avvisi sui link (Safety.LinkWarning). Il rapporto parla di
    /// gov.it e edu.it per tutte e tre: mancavano solo nella prima. I nomi geografici (pd.it, padova.it, veneto.it)
    /// mancavano in tutte. Gli enti qui sono finti; i suffissi sono quelli veri.
    /// </summary>
    static void K2Suffixes()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        string one = $"ente-uno-{tag}.gov.it", two = $"ente-due-{tag}.gov.it", school = $"scuola-uno-{tag}.edu.it", college = $"scuola-due-{tag}.edu.it";
        string town = $"paese-{tag}.pd.it", firm = $"ditta-{tag}.pd.it";
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "maria.prova@gmail.example" };
        var now = DateTime.Now;
        MessageSummary Mail(string id, string from, DateTime date) =>
            new() { Id = id + tag, FolderId = "INBOX", StableId = id + tag, From = new Address { Name = "Ente di prova", Email = from }, Subject = "Prova", Date = date };
        static MessageBody Body(string results) => new() { Html = "<p>Prova</p>", AuthenticationResults = [results] };
        static string Pass(string d) => $"mx.google.com; dkim=pass header.i=@{d} header.s=s1; spf=pass smtp.mailfrom=rimbalzi@posta.{d}; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from={d}";

        // ---------------------------------------------------------------- loghi e segno "autenticato" (SenderLogos.DomainOf)
        string[] seen = [one, two, school, college, town, firm, "gov.it", "edu.it", "pd.it"];
        try
        {
            (string Email, string Domain)[] marks =
            [
                ("protocollo@" + one, one), ("urp@posta." + one, one), ("info@" + two, two), ("segreteria@" + school, school), ("preside@" + college, college),
                ("protocollo@comune." + town, town), ("info@" + firm, firm), ($"urp@regione-{tag}.veneto.it", $"regione-{tag}.veneto.it"), ($"sportello@comune-{tag}.padova.it", $"comune-{tag}.padova.it"),
                ($"x@azienda-{tag}.fvg.it", $"azienda-{tag}.fvg.it"), ($"a@servizi.ente-{tag}.rm.it", $"ente-{tag}.rm.it"),
            ];
            var lumped = marks.Where(m => SenderLogos.DomainOf(m.Email) != m.Domain).Select(m => $"{m.Email}: {SenderLogos.DomainOf(m.Email) ?? "nessuno"}").ToList();
            // Un ente autenticato (posta da 15 giorni, un messaggio che supera DMARC) non autentica l'ente accanto.
            var first = Mail("k2a", "protocollo@" + one, now.AddDays(-15));
            SenderLogos.Note(first);
            SenderLogos.NoteBody(gmail, Mail("k2b", "protocollo@" + one, now), Body(Pass(one)));
            bool own = UI.Logos.DomainFor(first.From) == one;
            string? neighbour = UI.Logos.DomainFor(new Address { Email = "info@" + two }), firmLogo = null;
            SenderLogos.Note(Mail("k2c", "protocollo@comune." + town, now.AddDays(-15)));
            SenderLogos.NoteBody(gmail, Mail("k2d", "protocollo@comune." + town, now), Body(Pass("comune." + town)));
            firmLogo = UI.Logos.DomainFor(new Address { Email = "info@" + firm });
            // Annulla iscrizione con un clic: serve la firma DKIM del dominio del From, non quella di un'altra scuola.
            var results = SenderCheck.Parse($"mx.google.com; dkim=pass header.i=@{college} header.s=s1; spf=pass smtp.mailfrom=x@{school}; dmarc=pass (p=NONE) header.from={school}");
            bool signedByOther = SenderLogos.DomainOf("avvisi@" + school) is { } schoolDomain && SenderCheck.Signed(results, schoolDomain);
            Check(lumped.Count == 0 && own && neighbour == null && firmLogo == null && !signedByOther,
                  $"K-02, loghi e segno \"autenticato\": {marks.Length} indirizzi sotto gov.it, edu.it e i nomi geografici (pd.it, rm.it, padova.it, veneto.it, fvg.it) hanno ognuno il dominio del suo ente, non il suffisso; " +
                  $"un ente autenticato non dà il logo né il segno all'ente accanto (gov.it: {neighbour ?? "no"}; pd.it: {firmLogo ?? "no"}), e la firma DKIM di un'altra scuola non vale per l'annullamento con un clic ({signedByOther})" +
                  (lumped.Count == 0 ? "" : ". Insieme: " + string.Join("; ", lumped)));
        }
        finally
        {
            SenderLogos.TestForget(seen);
        }

        // ---------------------------------------------------------------- "Mostra immagini" (Settings.ImageKey)
        // gov.it e edu.it: la regola generale di ImageKey (un secondo livello come gov o edu sotto un dominio nazionale) li
        // divideva già dal primo giorno. Per questa parte il rapporto è falso: il controllo dice com'è, prima e dopo.
        var allowed = new Settings();
        allowed.AllowImagesFrom("protocollo@" + one);
        allowed.AllowImagesFrom("segreteria@posta." + school);
        string? govLink = Safety.LinkWarning(one, new Uri($"https://{two}/accesso")), eduLink = Safety.LinkWarning("www." + school, new Uri($"https://{college}/"));
        Check(allowed.ImageSenders is [var govKey, var eduKey] && govKey == one && eduKey == school && allowed.ShowsImagesFrom("urp@servizi." + one) && !allowed.ShowsImagesFrom("info@" + two) && !allowed.ShowsImagesFrom("preside@" + college)
              && govLink != null && eduLink != null && Safety.LinkWarning(one, new Uri($"https://servizi.{one}/")) == null,
              $"K-02, per \"Mostra immagini\" e per gli avvisi sui link gov.it e edu.it erano già suffissi (la regola generale dei secondi livelli nazionali): si ricorda l'ente ({string.Join(", ", allowed.ImageSenders)}), " +
              "l'ente accanto resta con le immagini bloccate, e un link che mostra un ente e porta a un altro ha l'avviso");

        // I nomi geografici: prima "Mostra immagini" su un comune ricordava tutta la provincia.
        var places = new Settings();
        places.AllowImagesFrom("protocollo@comune." + town);
        places.AllowImagesFrom($"urp@regione-{tag}.veneto.it");
        places.AllowImagesFrom($"sportello@comune-{tag}.padova.it");
        bool remembered = places.ImageSenders.SequenceEqual([town, $"regione-{tag}.veneto.it", $"comune-{tag}.padova.it"]);
        string[] others = ["info@" + firm, $"x@altro-{tag}.veneto.it", $"y@altro-{tag}.padova.it", "z@gov.it"];
        var shown = others.Where(places.ShowsImagesFrom).ToList();
        string? placeLink = Safety.LinkWarning("comune." + town, new Uri($"https://{firm}/accesso")), sameLink = Safety.LinkWarning("comune." + town, new Uri($"https://servizi.comune.{town}/"));
        string? nameAddress = SenderCheck.NameAddress(new Address { Name = "protocollo@comune." + town, Email = "x@" + firm });
        Check(remembered && shown.Count == 0 && places.ShowsImagesFrom("urp@comune." + town) && placeLink != null && sameLink == null && nameAddress != null,
              $"K-02, nomi geografici: \"Mostra immagini\" su un comune sotto pd.it, su un ente sotto veneto.it e su uno sotto padova.it ricorda quell'ente ({string.Join(", ", places.ImageSenders)}), non il suffisso; " +
              $"le immagini degli altri sotto lo stesso suffisso restano bloccate (caricate da sole per {shown.Count} su {others.Length}); un link che mostra il comune e porta a un altro nome della provincia ha l'avviso ({placeLink != null}), " +
              $"e un nome del mittente con l'indirizzo del comune da un altro nome della provincia è segnalato ({nameAddress ?? "no"})");

        // Un mittente che scrive proprio dal suffisso (x@gov.it, x@pd.it, x@co.uk): si ricorda l'indirizzo, non il suffisso.
        var bare = new Settings();
        foreach (var from in (string[])["x@gov.it", "y@pd.it", "z@co.uk"]) bare.AllowImagesFrom(from);
        bare.UpgradeImageSenders(); // come a ogni avvio: l'indirizzo non deve tornare suffisso
        Check(bare.ImageSenders is ["x@gov.it", "y@pd.it", "z@co.uk"] && bare.ShowsImagesFrom("x@gov.it") && !bare.ShowsImagesFrom("protocollo@" + one) && !bare.ShowsImagesFrom("info@" + firm) && !bare.ShowsImagesFrom("a@negozio-prova.co.uk"),
              $"K-02, \"Mostra immagini\" su un messaggio che arriva proprio da un suffisso (x@gov.it, y@pd.it, z@co.uk) ricorda quell'indirizzo ({string.Join(", ", bare.ImageSenders)}): prima ricordava il suffisso, e valeva per tutti quelli sotto");

        // ---------------------------------------------------------------- le impostazioni di chi usa già Missiva
        // Scritte e rilette da una cartella di prova, come a un avvio (Store.Read e poi UpgradeImageSenders, come Store.Load).
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-core-" + tag);
        try
        {
            var before = new Settings { ImageSenders = ["gov.it", "edu.it", "pd.it", "veneto.it", "padova.it", one, "co.uk", "fornitore-prova.example", "mario.rossi@gmail.example", "info@" + firm] };
            Store.TryWrite(dir, before, null, out _);
            var after = Store.Read(dir, out _);
            after.UpgradeImageSenders();
            // co.uk esce anche lui dalle scelte del 14 ottobre (scelta 25: K2BroadAtStart); fino ad allora restava.
            bool kept = after.ImageSenders.SequenceEqual([one, "fornitore-prova.example", "mario.rossi@gmail.example", firm]);
            string[] closed = ["info@" + two, "preside@" + college, "protocollo@comune." + town, $"urp@regione-{tag}.veneto.it", $"sportello@comune-{tag}.padova.it"];
            var open = closed.Where(after.ShowsImagesFrom).ToList();
            after.AllowImagesFrom("protocollo@comune." + town);
            Check(kept && open.Count == 0 && after.ShowsImagesFrom("protocollo@" + one) && after.ShowsImagesFrom("urp@comune." + town) && !after.ShowsImagesFrom($"x@altro-{tag}.pd.it"),
                  $"K-02, impostazioni di prima: le voci che sono un suffisso italiano intero (gov.it, edu.it, pd.it, veneto.it, padova.it) escono all'avvio e non valgono più per tutti gli enti lì sotto (ancora aperti: {open.Count} su {closed.Length}); " +
                  $"restano le altre ({string.Join(", ", after.ImageSenders.Take(5))}; co.uk, dalle scelte del 14 ottobre, esce anche lui), e \"Mostra immagini\" premuto di nuovo sul proprio comune vale per quel comune soltanto");

            // Lo stesso per una copia di sicurezza importata (DropBroadImageSenders, T-17).
            var copy = new Settings { ImageSenders = ["gov.it", "pd.it", "veneto.it", town, "fornitore-prova.example"] };
            int dropped = copy.DropBroadImageSenders();
            Check(dropped == 3 && copy.ImageSenders.SequenceEqual([town, "fornitore-prova.example"]),
                  $"K-02, da una copia di sicurezza non entrano le voci che sono un suffisso italiano intero (fuori {dropped} su 5; prima usciva solo gov.it, per la regola generale)");
        }
        finally
        {
            DeleteQuietly(dir);
        }

        // ---------------------------------------------------------------- i nomi normali restano com'erano
        (string Email, string? Logo, string Images)[] normal =
        [
            ("auto-confirm@amazon.it", "amazon.it", "amazon.it"), ("newsletter@insider.sky.it", "sky.it", "sky.it"), ("news@email.amazon.co.uk", "amazon.co.uk", "amazon.co.uk"),
            ("segreteria@studenti.ateneo-prova.it", "ateneo-prova.it", "ateneo-prova.it"), ("info@fornitore-prova.example", "fornitore-prova.example", "fornitore-prova.example"),
            ("mario.rossi@gmail.com", null, "mario.rossi@gmail.com"), ("anna@libero.it", null, "anna@libero.it"), ("ordini@negozio-prova.com", "negozio-prova.com", "negozio-prova.com"),
            ("x@governo-prova.it", "governo-prova.it", "governo-prova.it"), ("y@posta.padovana-prova.it", "padovana-prova.it", "padovana-prova.it"), ("z@pd.negozio-prova.it", "negozio-prova.it", "negozio-prova.it"),
        ];
        var moved = normal.Where(n => SenderLogos.DomainOf(n.Email) != n.Logo || Settings.ImageKey(n.Email) != n.Images).Select(n => $"{n.Email}: {SenderLogos.DomainOf(n.Email) ?? "nessuno"} e {Settings.ImageKey(n.Email)}").ToList();
        Check(moved.Count == 0 && Safety.LinkWarning("sky.it", new Uri("https://insider.sky.it/offerta")) == null && Safety.LinkWarning("www.negozio-prova.it", new Uri("https://pd.negozio-prova.it/")) == null
              && Safety.LinkWarning("negozio-prova.it", new Uri("https://altro-prova.it/")) != null,
              $"K-02, {normal.Length} indirizzi normali (.it, .com, .co.uk, posta personale, nomi che somigliano a un suffisso) hanno il dominio del logo e la voce di \"Mostra immagini\" di prima, e gli avvisi sui link sono quelli di prima" +
              (moved.Count == 0 ? "" : ": cambiati " + string.Join("; ", moved)));
    }

    // ---------------------------------------------------------------- K-03: gli identificativi nei percorsi di Graph

    /// <summary>
    /// K-03: gli identificativi di messaggi, cartelle e allegati entrano nei percorsi delle richieste a Graph. Arrivano da
    /// Graph, ma anche da posti che un altro può aver scritto: i posposti e i contrassegni delle impostazioni di una copia
    /// importata, la coda delle azioni, gli argomenti di una notifica. Con dentro una barra, un punto di domanda, un
    /// cancelletto o "..", la richiesta (con il token dell'account) andava a un altro percorso di Graph. Il rapporto
    /// propone Uri.EscapeDataString: non ferma "..", che non ha nulla da codificare. Qui il gestore HTTP finto scrive dove
    /// va ogni richiesta.
    /// </summary>
    static async Task K3GraphIdsAsync()
    {
        const string root = "https://graph.microsoft.com/v1.0/";
        string tag = Guid.NewGuid().ToString("N")[..8];
        var seen = new List<string>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            lock (seen) seen.Add($"{request.Method} {request.RequestUri!.AbsoluteUri}");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"nuovo\",\"value\":[]}", Encoding.UTF8, "application/json") };
        }));
        var consents = (GraphProvider.TestContactsConsent, GraphProvider.TestCalendarConsent, GraphProvider.TestSettingsConsent, GraphProvider.TestSharedConsent);
        var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@studio-prova.example", Tenant = MicrosoftAuth.Organizations, Id = "k3-lavoro-" + tag };
        var info = new MailAccount { Kind = AccountKind.Microsoft, Email = "info-k3@studio-prova.example", SharedOf = work.Id, Tenant = work.Tenant, Id = "k3-condivisa-" + tag };
        Store.Settings.Accounts.Add(work);
        Store.Settings.Accounts.Add(info);
        try
        {
            (GraphProvider.TestContactsConsent, GraphProvider.TestCalendarConsent, GraphProvider.TestSettingsConsent, GraphProvider.TestSharedConsent) = (true, true, true, true);
            GraphProvider provider = new(work), shared = new(info);
            static MessageSummary Msg(string id) => new() { Id = id, FolderId = "inbox", Subject = "Prova" };
            MailFolder inbox = new() { Id = "inbox", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, snoozed = new() { Id = "AAMkposposti-prova=", Name = "Posposti" };
            // Che cosa parte per una chiamata: le richieste viste dal gestore finto, e se la chiamata è finita con "non c'è".
            async Task<(List<string> Sent, bool Gone)> Run(Func<Task> call)
            {
                lock (seen) seen.Clear();
                bool gone = false;
                try { await call(); }
                catch (Exception ex) { gone = GraphProvider.IsNotFound(ex) || ex is KeyNotFoundException; }
                lock (seen) return (seen.ToList(), gone);
            }

            // ---------------------------------------------------------------- identificativi che non sono di Graph
            const string other = "altra-casella@studio-prova.example";
            (string What, Func<Task> Call)[] hostile =
            [
                // WakeAsync non solleva: per un messaggio che non c'è risponde null, e Posponi lo dà per eliminato.
                ("il ritorno di un posposto con ../mailFolders/archive", async () => { if (await provider.WakeAsync("../mailFolders/archive", snoozed, inbox) == null) throw new KeyNotFoundException(); }),
                ("un'eliminazione con ../mailFolders/archive", () => provider.DeletePermanentlyAsync([Msg("../mailFolders/archive")])),
                ("la stessa con le barre rovesciate", () => provider.DeletePermanentlyAsync([Msg("..\\mailFolders\\archive")])),
                ("un punto di domanda", () => provider.SetReadAsync([Msg("m1?$select=id")], true)),
                ("un cancelletto", () => provider.MoveAsync([Msg("m1#")], snoozed)),
                ("due punti soli", () => provider.DeletePermanentlyAsync([Msg("..")])),
                ("il percento", () => provider.SetReadAsync([Msg("%2E%2E%2Fcontacts%2Fc1")], true)),
                ("uno spazio", () => provider.SetReadAsync([Msg("m1 m2")], true)),
                ("un carattere che non si vede fra due punti", () => provider.DeletePermanentlyAsync([Msg("." + (char)0x200E + ".")])),
                ("una lettera accentata", () => provider.SetReadAsync([Msg("messaggio-è")], true)),
                ("vuoto", () => provider.DeletePermanentlyAsync([Msg("")])),
                ("una cartella verso un'altra casella", () => provider.GetMessagesAsync(new MailFolder { Id = $"../../users/{other}/mailFolders/inbox", Name = "Clienti" }, null, null)),
                ("una cartella da svuotare", () => provider.EmptyFolderAsync(new MailFolder { Id = "inbox/../..", Name = "Clienti" }, null)),
                ("un allegato verso un'altra casella", () => provider.GetAttachmentAsync(Msg("AAMkmessaggio-prova="), new Attachment { Id = $"../../../../users/{other}/messages/m/attachments/a", Name = "a.pdf" })),
                ("nella casella condivisa", () => shared.DeletePermanentlyAsync([Msg("../mailFolders/archive")])),
                ("un contatto di due punti", () => provider.DeleteContactAsync(new Contact { ServerId = ".." })),
                ("un evento di due punti", () => provider.DeleteEventAsync(new CalendarEvent { Id = ".." }, false)),
                ("una regola di due punti", () => provider.DeleteRuleAsync(new MailRule { ServerId = ".." })),
            ];
            var went = new List<string>();
            int stopped = 0;
            foreach (var (what, call) in hostile)
            {
                var (sent, gone) = await Run(call);
                if (sent.Count == 0 && gone) stopped++;
                else went.Add($"{what}: {(sent.Count == 0 ? "nessuna richiesta, ma senza dire che non c'è" : string.Join(" e ", sent.Select(s => s.Replace(root, "").Split('?')[0])))}");
            }
            // Con la barra o il punto di domanda dentro un identificativo di contatti, eventi e regole la richiesta parte, ma
            // l'identificativo resta un pezzo solo del percorso (codificato: così era già).
            var (encoded, _) = await Run(() => provider.DeleteContactAsync(new Contact { ServerId = "../messages/m1?x=1" }));
            Check(stopped == hostile.Length && encoded is [var one] && one == $"DELETE {root}me/contacts/..%2Fmessages%2Fm1%3Fx%3D1",
                  $"K-03, {hostile.Length} identificativi che non sono di Graph (con una barra, una barra rovesciata, un punto di domanda, un cancelletto, un percento, uno spazio, un carattere non ASCII, solo punti, vuoti), per messaggi, cartelle, allegati, " +
                  $"contatti, eventi e regole: nessuna richiesta parte, e chi la chiedeva riceve \"non c'è\" (fermati {stopped} su {hostile.Length})" + (went.Count == 0 ? "" : ". Partite: " + string.Join(" | ", went)));

            // ---------------------------------------------------------------- gli identificativi veri: le richieste di prima, byte per byte
            // Identificativi fatti come quelli di Outlook (base64 con - e _ al posto di + e /, e = in fondo), e gli alias.
            const string m = "AAMkAGI2NGVhZTVlLTI1OGMtNDI4My1iZmE5LTA5OGJmYzAwOWZjYQBGAAAAAAC-_x4aBcDeFgHiJkLmNoPqBwAAAA==", f = "AQMkADAwATM0MDAAMS1iNTcwLWI2NTEtMDACLTAwCgAuAAADx_y-zAAAAA==",
                         a = "AAMkAGI2NGVhAAABEgAQAK-_3xYzAbCdEfGhIjKlMnO=", c = "AAMkcontatto-_prova==";
            var folder = new MailFolder { Id = f, Name = "Clienti" };
            (string What, Func<Task> Call, string[] Expected)[] real =
            [
                ("segna come letto", () => provider.SetReadAsync([Msg(m)], true), [$"PATCH {root}me/messages/{m}"]),
                ("sposta", () => provider.MoveAsync([Msg(m)], folder), [$"POST {root}me/messages/{m}/move"]),
                ("copia", () => provider.CopyAsync([Msg(m)], folder), [$"POST {root}me/messages/{m}/copy"]),
                ("elimina", () => provider.DeletePermanentlyAsync([Msg(m)]), [$"DELETE {root}me/messages/{m}"]),
                ("il ritorno di un posposto", () => provider.WakeAsync(m, snoozed, inbox), [$"PATCH {root}me/messages/{m}", $"POST {root}me/messages/{m}/move"]),
                ("posponi", () => provider.SnoozeAsync(Msg(m), snoozed), [$"POST {root}me/messages/{m}/move"]),
                ("un allegato", () => provider.GetAttachmentAsync(Msg(m), new Attachment { Id = a, Name = "a.pdf" }), [$"GET {root}me/messages/{m}/attachments/{a}/$value"]),
                ("l'origine", () => provider.GetRawAsync(Msg(m)), [$"GET {root}me/messages/{m}/$value"]),
                ("l'elenco di una cartella", () => provider.GetMessagesAsync(folder, null, null), [$"GET {root}me/mailFolders/{f}/messages?$top=*"]),
                ("l'elenco della Posta in arrivo per nome", () => provider.GetMessagesAsync(inbox, null, null), [$"GET {root}me/mailFolders/inbox/messages?$top=*"]),
                ("la ricerca in una cartella", () => provider.SearchAsync(folder, "preventivo"), [$"GET {root}me/mailFolders/{f}/messages?$search=*"]),
                ("rinomina cartella", () => provider.RenameFolderAsync(folder, "Fornitori"), [$"PATCH {root}me/mailFolders/{f}"]),
                ("sposta cartella", () => provider.MoveFolderAsync(folder, null), [$"POST {root}me/mailFolders/{f}/move"]),
                ("nuova sottocartella", () => provider.CreateFolderAsync("Nuova", folder), [$"POST {root}me/mailFolders/{f}/childFolders"]),
                ("elimina cartella", () => provider.DeleteFolderAsync(folder), [$"DELETE {root}me/mailFolders/{f}"]),
                ("segna tutti come letti", () => provider.MarkAllReadAsync(folder), [$"GET {root}me/mailFolders/{f}/messages?$filter=*"]),
                ("un contatto", () => provider.DeleteContactAsync(new Contact { ServerId = c }), [$"DELETE {root}me/contacts/{c.Replace("=", "%3D")}"]),
                ("un evento", () => provider.DeleteEventAsync(new CalendarEvent { Id = c }, false), [$"DELETE {root}me/events/{c.Replace("=", "%3D")}"]),
                ("una regola", () => provider.DeleteRuleAsync(new MailRule { ServerId = "AQAAAJ5d+/A=" }), [$"DELETE {root}me/mailFolders/inbox/messageRules/AQAAAJ5d%2B%2FA%3D"]),
                ("nella casella condivisa", () => shared.SetReadAsync([Msg(m)], false), [$"PATCH {root}users/info-k3%40studio-prova.example/messages/{m}"]),
                ("una cartella della casella condivisa", () => shared.GetMessagesAsync(folder, null, null), [$"GET {root}users/info-k3%40studio-prova.example/mailFolders/{f}/messages?$top=*"]),
            ];
            var different = new List<string>();
            foreach (var (what, call, expected) in real)
            {
                var (sent, _) = await Run(call);
                bool same = sent.Count == expected.Length && sent.Zip(expected, (s, e) => e.EndsWith('*') ? s.StartsWith(e[..^1], StringComparison.Ordinal) : s == e).All(x => x);
                if (!same) different.Add($"{what}: {string.Join(" e ", sent.Select(s => s.Replace(root, "")))}");
            }
            Check(different.Count == 0,
                  $"K-03, {real.Length} richieste con identificativi fatti come quelli veri di Outlook (lettere, cifre, - e _, = in fondo) e con gli alias (inbox), nella casella dell'account e in una condivisa (users/indirizzo/): " +
                  "partono agli stessi indirizzi di prima, byte per byte (messaggi, cartelle e allegati com'è scritto l'identificativo; contatti, eventi e regole codificati, com'erano)" +
                  (different.Count == 0 ? "" : ". Diverse: " + string.Join(" | ", different)));
        }
        finally
        {
            (GraphProvider.TestContactsConsent, GraphProvider.TestCalendarConsent, GraphProvider.TestSettingsConsent, GraphProvider.TestSharedConsent) = consents;
            Store.Settings.Accounts.Remove(work);
            Store.Settings.Accounts.Remove(info);
            GraphProvider.UseTestHandler(new NoHandler("Graph"));
        }

        // ---------------------------------------------------------------- un punto solo
        // Nel sorgente senza i commenti nessun identificativo entra in un percorso se non da Segment: dopo una barra, in una
        // stringa, c'è solo Segment(...), un percorso già fatto (MessageUrl, FolderUrl) o il nome fisso di un'azione.
        if (Ch1Code("Core", "GraphProvider.cs") is { } code)
        {
            var raw = System.Text.RegularExpressions.Regex.Matches(code, @"/\{(?!Segment\()([^{}]*)\}").Select(x => x.Groups[1].Value)
                         .Where(x => x is not ("action" or "response.Version") && !x.StartsWith("EventId(", StringComparison.Ordinal)).Distinct().ToList(); // un'azione fissa, una riga di registro, e EventId chiama Segment
            var joined = System.Text.RegularExpressions.Regex.Matches(code, "(messages|mailFolders|attachments|events|calendars|contacts|childFolders|messageRules)/\" \\+ (?!Segment\\()").Select(x => x.Value).Distinct().ToList();
            bool onePoint = code.Contains("internal static string Segment(string? id, bool escape = false)") && code.Contains("string MessageUrl(string id) => me + \"messages/\" + Segment(id);")
                            && code.Contains("string FolderUrl(string id) => me + \"mailFolders/\" + Segment(id);");
            // Solo Segment e il nome della casella condivisa codificano un pezzo di percorso; le altre codifiche sono di filtri e ricerche.
            int escapes = code.Split("Uri.EscapeDataString(").Length - 1, inQueries = System.Text.RegularExpressions.Regex.Matches(code, @"(=\{|= |\+ )Uri\.EscapeDataString\(").Count;
            Check(raw.Count == 0 && joined.Count == 0 && onePoint,
                  $"K-03, in GraphProvider ogni identificativo entra in un percorso da un punto solo (Segment, e MessageUrl e FolderUrl che lo chiamano): nessun altro valore messo dopo una barra ({raw.Count}" +
                  $"{(raw.Count == 0 ? "" : ": " + string.Join(", ", raw.Take(6)))}) né attaccato a un nome di raccolta ({joined.Count}); Uri.EscapeDataString resta in {escapes} punti, {inQueries} dei quali per filtri, ricerche e date");
        }
    }

    // ---------------------------------------------------------------- K-04: l'HTML citato nelle risposte e negli inoltri

    /// <summary>Un account finto da cui parte la risposta a un .eml: tiene il messaggio che gli arriva da inviare.</summary>
    public class K4Sender : DispatchProxy
    {
        public OutgoingMessage? Sent;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case "get_Account": return new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example" };
                case "SendAsync":
                    Sent = (OutgoingMessage)args![0]!;
                    return Task.CompletedTask;
                default: throw new NotSupportedException(method?.Name);
            }
        }
    }

    /// <summary>Quello che una citazione non deve portare: ciò che esegue o invia. Cercato nell'HTML che parte, in minuscolo.</summary>
    static readonly string[] K4Active =
    [
        "<script", "alert(", "<form", "<input", "<select", "<textarea", "<button", "<iframe", "<object", "<embed", "javascript:", "vbscript:", "data:text/html",
        "onload=", "onclick=", "onerror=", "http-equiv=\"refresh\"", "http-equiv=refresh",
    ];

    /// <summary>
    /// K-04: con Gmail, IMAP e i file .eml la citazione di una risposta o di un inoltro la fa Missiva (con Graph il server), e
    /// ci metteva l'HTML dell'originale com'era arrivato: script, moduli, attributi di evento, link javascript: ripartivano
    /// verso il destinatario. Non si pulisce come il riquadro di lettura (MailHtml.Prepare), che blocca le immagini esterne
    /// e riscrive stili e colori: un inoltro deve arrivare com'era l'originale. Qui il messaggio MIME vero che partirebbe
    /// (MimeOptions.BuildAsync), con originali costruiti apposta e con una serie di messaggi normali.
    /// </summary>
    static async Task K4QuotedHtmlAsync()
    {
        var account = new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", DisplayName = "Mario Rossi" };
        var original = new MessageSummary
        {
            Id = "7", FolderId = "INBOX", StableId = "originale-prova", From = new Address { Name = "Negozio di prova", Email = "notizie@negozio-prova.example" },
            Subject = "Offerta", Date = new DateTime(2026, 10, 12, 9, 30, 0),
        };
        List<Address> to = [new Address { Name = "Maria Rossi", Email = "maria.rossi@example.com" }];
        // L'HTML del messaggio che partirebbe: quello scritto, poi la citazione.
        async Task<string> Outgoing(string quoted, ComposeMode mode = ComposeMode.Reply, List<Attachment>? attachments = null)
        {
            var mail = new OutgoingMessage
            {
                Mode = mode, To = to, Subject = "R: Offerta", Html = "<p>Grazie</p>", Text = "Grazie", Original = original,
                OriginalBody = new MessageBody { Html = quoted, Attachments = attachments ?? [] },
            };
            var message = await MimeOptions.BuildAsync(mail, account, new ReplySources(), _ => Task.FromResult(mail.OriginalBody), _ => Task.FromResult<IMessageSummary?>(null),
                                                       (_, _) => Task.FromResult(Array.Empty<byte>()));
            return message.HtmlBody ?? "";
        }
        // Da dove comincia la citazione, e come finisce: quello che BuildAsync mette attorno all'originale.
        string frame = await Outgoing("x");
        string prefix = frame[..^"x</blockquote>".Length];
        static List<string> Found(string html) => K4Active.Where(a => html.Contains(a, StringComparison.OrdinalIgnoreCase)).ToList();

        // ---------------------------------------------------------------- un originale costruito apposta
        const string hostile =
            "<html><head><title>Offerta</title><meta http-equiv=\"refresh\" content=\"0;url=https://altrove-prova.example/\"><script>alert(1)</script><style>p{color:#333}</style></head>" +
            "<body onload=\"alert(2)\"><p onclick=\"alert(3)\">Gentile cliente,</p>" +
            "<form action=\"https://altrove-prova.example/raccogli\" method=\"post\"><p>Conferma i tuoi dati:</p><input type=\"text\" name=\"utente\"><input type=\"password\" name=\"parola\">" +
            "<select name=\"x\"><option>a</option></select><textarea name=\"note\">scrivi</textarea><button type=\"submit\">Invia la conferma</button></form>" +
            "<a href=\"javascript:alert(4)\">Apri</a> <a href=\"  java&#9;script:alert(5)\">Apri ancora</a> <a href=\"vbscript:msgbox(1)\">E ancora</a> <a href=\"data:text/html,&lt;script&gt;alert(6)&lt;/script&gt;\">Pagina</a> " +
            "<a href=\"https://negozio-prova.example/offerta\">Vai all'offerta</a> <a href=\"mailto:info@negozio-prova.example\">Scrivici</a> <a href=\"tel:+390000000000\">Chiamaci</a>" +
            "<img src=\"https://immagini.negozio-prova.example/banner.png\" width=\"600\" alt=\"Banner\"><img src=\"cid:logo@prova\" alt=\"Logo\"><img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"Punto\">" +
            "<img src=\"https://immagini.negozio-prova.example/pixel.gif\" width=\"1\" height=\"1\" onerror=\"alert(7)\">" +
            "<iframe src=\"https://altrove-prova.example/\"></iframe><object data=\"https://altrove-prova.example/x.swf\"></object><embed src=\"https://altrove-prova.example/y.swf\">" +
            "<svg width=\"10\" height=\"10\"><script>alert(8)</script><a xlink:href=\"javascript:alert(9)\"><rect width=\"10\" height=\"10\" onclick=\"alert(10)\"/></a></svg>" +
            "<table><tr><td style=\"background:#eee\">Prezzo: 10 euro</td></tr></table></body></html>";
        // Quello che deve restare: il testo, i link veri, le immagini dell'originale (anche quella di un pixel: in un inoltro restano), gli stili.
        string[] kept =
        [
            "Gentile cliente,", "Conferma i tuoi dati:", "Invia la conferma", "href=\"https://negozio-prova.example/offerta\"", "Vai all'offerta", "href=\"mailto:info@negozio-prova.example\"", "href=\"tel:+390000000000\"",
            "src=\"https://immagini.negozio-prova.example/banner.png\"", "src=\"https://immagini.negozio-prova.example/pixel.gif\"", "src=\"cid:logo@prova\"", "src=\"data:image/png;base64,iVBORw0KGgo=\"",
            "Prezzo: 10 euro", "background:#eee", "p{color:#333}", ">Apri<", ">Pagina<",
        ];
        var logo = new Attachment { Id = "1", Name = "logo.png", ContentType = "image/png", IsInline = true, ContentId = "logo@prova", Content = [1, 2, 3] };
        string reply = await Outgoing(hostile), forward = await Outgoing(hostile, ComposeMode.Forward, [logo]);
        var missing = kept.Where(k => !reply.Contains(k, StringComparison.Ordinal) || !forward.Contains(k, StringComparison.Ordinal)).ToList();
        Check(Found(reply).Count == 0 && Found(forward).Count == 0 && missing.Count == 0 && reply.StartsWith(prefix, StringComparison.Ordinal) && reply.EndsWith("</blockquote>", StringComparison.Ordinal),
              $"K-04, risposta e inoltro (Gmail, IMAP) di un messaggio con script, un modulo con i suoi campi, frame, oggetti, attributi di evento e link javascript:, vbscript: e data:text/html: nella citazione che parte non ce n'è nessuno " +
              $"(rimasti nella risposta: {(Found(reply).Count == 0 ? "nessuno" : string.Join(" ", Found(reply)))}); restano il testo, anche quello del modulo e del pulsante, i link http, mailto e tel, gli stili e le immagini dell'originale, " +
              $"comprese quelle esterne e quella di un pixel{(missing.Count == 0 ? "" : " (MANCANO: " + string.Join(" | ", missing) + ")")}");

        // ---------------------------------------------------------------- lo stesso da un file .eml
        var eml = new MimeMessage { Subject = "Offerta", Body = new TextPart("html") { Text = hostile } };
        eml.From.Add(new MailboxAddress("Negozio di prova", "notizie@negozio-prova.example"));
        var stub = DispatchProxy.Create<IMailProvider, K4Sender>();
        var file = new EmlProvider(eml, stub);
        await file.SendAsync(new OutgoingMessage { Mode = ComposeMode.Forward, To = to, Subject = "I: Offerta", Html = "<p>Vedi sotto</p>", Text = "Vedi sotto", Original = file.Summary });
        string fromFile = ((K4Sender)(object)stub).Sent?.Html ?? "";
        Check(fromFile.StartsWith("<p>Vedi sotto</p><br><div>", StringComparison.Ordinal) && Found(fromFile).Count == 0 && kept.All(k => fromFile.Contains(k, StringComparison.Ordinal)),
              $"K-04, lo stesso messaggio aperto da un file .eml e inoltrato: la citazione parte senza script, moduli, attributi di evento e link che eseguono (rimasti: {(Found(fromFile).Count == 0 ? "nessuno" : string.Join(" ", Found(fromFile)))}), con il resto com'era");

        // ---------------------------------------------------------------- i messaggi normali: la citazione di prima, byte per byte
        (string Name, string Html)[] normal =
        [
            ("una risposta breve", "<div dir=\"ltr\">Buongiorno,<br>confermo l'appuntamento di gioved&igrave;.<br><br>Cordiali saluti<br>Maria Rossi</div>"),
            ("una catena di risposte", "<div dir=\"ltr\">Va bene.</div><br><div class=\"gmail_quote\"><div dir=\"ltr\" class=\"gmail_attr\">Il giorno lun 12 ott 2026 Mario Rossi ha scritto:<br></div>" +
                                       "<blockquote class=\"gmail_quote\" style=\"margin:0px 0px 0px 0.8ex;border-left:1px solid rgb(204,204,204);padding-left:1ex\">Ci vediamo alle 10?</blockquote></div>"),
            ("una newsletter", "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\r\n<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"it\">\r\n<head>\r\n" +
                               "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" />\r\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"/>\r\n<meta name=\"color-scheme\" content=\"light dark\">\r\n" +
                               "<title>Le offerte di ottobre</title>\r\n<!--[if mso]><style>table{border-collapse:collapse}</style><![endif]-->\r\n" +
                               "<style type=\"text/css\">\r\n  body{margin:0;padding:0}\r\n  @media only screen and (max-width:600px){.colonna{width:100% !important}}\r\n  a[x-apple-data-detectors]{color:inherit !important}\r\n</style>\r\n</head>\r\n" +
                               "<body bgcolor=\"#f4f4f4\" style=\"margin:0\">\r\n<center>\r\n<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" class=\"colonna\">\r\n<tr><td align=\"center\">" +
                               "<a href=\"https://negozio-prova.example/offerte?utm_source=posta&amp;utm_campaign=ottobre\" target=\"_blank\" rel=\"noopener\"><img src=\"https://immagini.negozio-prova.example/ottobre.jpg\" width=\"600\" height=\"240\" alt=\"Le offerte di ottobre\" style=\"display:block;border:0\" /></a></td></tr>\r\n" +
                               "<tr><td style=\"font-family:Arial,sans-serif;font-size:14px;color:#333333;padding:16px\">Gentile cliente,<br />fino a domenica spedizione gratuita&nbsp;sopra i 49&nbsp;&euro;.<br /><font color=\"#cc0000\"><b>Solo online</b></font></td></tr>\r\n" +
                               "<tr><td><a href=\"mailto:assistenza@negozio-prova.example?subject=Informazioni\">Scrivici</a> &middot; <a href=\"tel:+390000000000\">Chiamaci</a> &middot; <a href=\"https://negozio-prova.example/annulla?id=123\">Annulla l'iscrizione</a></td></tr>\r\n" +
                               "</table>\r\n</center>\r\n<img src=\"https://tracce.negozio-prova.example/apri.gif?m=123\" width=\"1\" height=\"1\" alt=\"\" />\r\n</body>\r\n</html>"),
            ("un messaggio scritto con Word", "<html xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w=\"urn:schemas-microsoft-com:office:word\" xmlns=\"http://www.w3.org/TR/REC-html40\">" +
                               "<head><meta http-equiv=Content-Type content=\"text/html; charset=utf-8\"><meta name=Generator content=\"Microsoft Word 15 (filtered medium)\"><!--[if !mso]><style>v\\:* {behavior:url(#default#VML);}</style><![endif]-->" +
                               "<style><!--\r\n/* Font Definitions */\r\n@font-face\r\n\t{font-family:Calibri;\r\n\tpanose-1:2 15 5 2 2 2 4 3 2 4;}\r\np.MsoNormal, li.MsoNormal\r\n\t{margin:0cm;\r\n\tfont-size:11.0pt;}\r\n--></style>" +
                               "<!--[if gte mso 9]><xml>\r\n<o:shapedefaults v:ext=\"edit\" spidmax=\"1026\" />\r\n</xml><![endif]--></head><body lang=IT link=\"#0563C1\" vlink=\"#954F72\" style='word-wrap:break-word'><div class=WordSection1>" +
                               "<p class=MsoNormal>Buongiorno,<o:p></o:p></p><p class=MsoNormal><![if !supportLists]><span style='mso-list:Ignore'>1.<span style='font:7.0pt \"Times New Roman\"'>&nbsp;&nbsp;</span></span><![endif]>primo punto<o:p></o:p></p>" +
                               "<p class=MsoNormal><img width=120 height=40 style='width:1.25in;height:.4166in' id=\"Immagine_x0020_1\" src=\"cid:image001.png@01DC0000.AAAA0000\" alt=\"Logo\"><o:p></o:p></p></div></body></html>"),
            ("solo testo", "<pre>Riga 1\r\nRiga 2 con &lt;un tag&gt; scritto\r\n   e degli spazi</pre>"),
            ("immagini incorporate e data:", "<p>In allegato la foto <img src=\"cid:foto1@prova\" alt=\"foto\"> e un punto <img src=\"data:image/gif;base64,R0lGODlhAQABAAAAACw=\" width=\"1\"></p>"),
            ("un pixel dentro noscript, base e un foglio di stile esterno", "<html><head><base target=\"_blank\"><link rel=\"stylesheet\" href=\"https://negozio-prova.example/posta.css\"></head><body><p>Novit&agrave;</p>" +
                               "<noscript><img src=\"https://tracce.negozio-prova.example/n.gif\" width=\"1\" height=\"1\"></noscript></body></html>"),
            ("un testo che parla di script", "<p>Nel codice c'era &lt;script&gt;alert(1)&lt;/script&gt; con onclick=&quot;x&quot;.</p><p>Il link javascript:alert(1) non va aperto. <img src=\"https://negozio-prova.example/a.png\" alt=\"Nota: onclick= e javascript: sono parole\" title=\"data:text/html\"></p>"),
            ("segni di minore nel testo", "<p>Se a < b e 3 <4 allora <<fine>></p>"),
            ("tag non chiusi e fuori posto", "<p>Uno<p>Due <b>grassetto <i>corsivo</b> ancora</i><table><td>cella<tr><td>altra</table>fine"),
            ("maiuscole e attributi senza virgolette", "<TABLE WIDTH=600 BGCOLOR=#FFFFFF BORDER=0><TR><TD ALIGN=center VALIGN=top><FONT FACE=Arial SIZE=2>Listino</FONT><BR><A HREF=https://negozio-prova.example/listino.pdf>Scarica</A></TD></TR></TABLE>"),
            ("link di ogni tipo", "<p><a href=\"#sezione\">Vai</a> <a href=\"/pagina\">Pagina</a> <a href=\"pagina.html\">Altra</a> <a href=\"//negozio-prova.example/x\">Senza schema</a> <a name=\"sezione\"></a>" +
                               "<img src=\"//immagini.negozio-prova.example/a.png\" srcset=\"https://immagini.negozio-prova.example/a.png 1x, https://immagini.negozio-prova.example/a2.png 2x\"></p>"),
            ("un disegno", "<p><svg width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><defs><path id=\"icona\" d=\"M0 0h24v24H0z\"/></defs><use href=\"#icona\" fill=\"none\" stroke=\"#333\"/></svg> Consegnato</p>"),
            ("vuoto", ""),
        ];
        var changed = new List<string>();
        foreach (var (name, html) in normal)
        {
            string sent = await Outgoing(html), expected = prefix + html + "</blockquote>";
            if (sent != expected) changed.Add(name);
        }
        Check(changed.Count == 0,
              $"K-04, {normal.Length} messaggi normali (risposte brevi e a catena, una newsletter con tabelle, immagini esterne, pixel e commenti per Outlook, un messaggio scritto con Word, immagini incorporate, tag non chiusi, link mailto e tel): " +
              $"la citazione che parte è quella di prima, byte per byte{(changed.Count == 0 ? "" : ". Cambiati: " + string.Join(", ", changed))}");

        // ---------------------------------------------------------------- oltre i tetti dell'analisi
        // 600 div uno dentro l'altro (il tetto è 512 livelli) con uno script in fondo: il riquadro lo mostra come solo testo.
        string deep = string.Concat(Enumerable.Repeat("<div>", 600)) + "<script>alert(1)</script><p onclick=\"alert(2)\">Testo in fondo</p>" + string.Concat(Enumerable.Repeat("</div>", 600));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string tooDeep = await Outgoing(deep);
        clock.Stop();
        Check(Found(tooDeep).Count == 0 && tooDeep.Contains("Testo in fondo", StringComparison.Ordinal) && !tooDeep.Contains("<div><div><div>", StringComparison.Ordinal) && tooDeep.StartsWith(prefix, StringComparison.Ordinal),
              $"K-04, un originale oltre i tetti dell'analisi (600 livelli, con uno script e un attributo di evento in fondo): nella citazione va il suo testo soltanto, come lo mostra il riquadro, in {clock.ElapsedMilliseconds} ms " +
              $"(rimasti: {(Found(tooDeep).Count == 0 ? "nessuno" : string.Join(" ", Found(tooDeep)))})");

        // ---------------------------------------------------------------- un messaggio normale con uno script: i dati per Gmail
        // Le conferme d'ordine portano spesso uno script di soli dati (JSON-LD): non esegue nulla, ma è uno script, e la
        // citazione si riscrive dall'albero. Il resto deve restare.
        const string order = "<html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\"><script type=\"application/ld+json\">{\"@context\":\"http://schema.org\",\"@type\":\"Order\",\"orderNumber\":\"123\"}</script></head>" +
                             "<body><table width=\"600\"><tr><td style=\"font-family:Arial\">Ordine n. 123 confermato<br><img src=\"https://immagini.negozio-prova.example/pacco.png\" width=\"64\" alt=\"Pacco\"></td></tr></table>" +
                             "<p><a href=\"https://negozio-prova.example/ordini/123\">Segui la spedizione</a></p></body></html>";
        string confirmed = await Outgoing(order);
        string[] orderKept = ["http-equiv=\"Content-Type\"", "<table width=\"600\">", "style=\"font-family:Arial\"", "Ordine n. 123 confermato<br>", "src=\"https://immagini.negozio-prova.example/pacco.png\"", "href=\"https://negozio-prova.example/ordini/123\"", "Segui la spedizione"];
        Check(Found(confirmed).Count == 0 && !confirmed.Contains("orderNumber", StringComparison.Ordinal) && orderKept.All(k => confirmed.Contains(k, StringComparison.Ordinal)),
              "K-04, una conferma d'ordine con i dati per Gmail in uno script (JSON-LD): lo script non riparte, e il resto della citazione (intestazioni, tabella, stile, immagine esterna, link) resta com'era scritto");

        // ---------------------------------------------------------------- una volta sola per originale
        // La bozza di una risposta si salva più volte mentre si scrive: la pulizia si fa una volta per l'HTML dell'originale, come
        // il testo della citazione (audit, v2 V-28), non a ogni salvataggio.
        string again = hostile + "<p>Seconda copia</p>", large = "<table>" + string.Concat(Enumerable.Range(0, 2500).Select(i => $"<tr><td style=\"padding:4px\"><a href=\"https://negozio-prova.example/p/{i}\">Prodotto {i}</a></td><td><img src=\"https://immagini.negozio-prova.example/{i}.jpg\" width=\"80\"></td></tr>")) + "</table>";
        int runs = MimeOptions.QuoteHtmlRuns;
        for (int i = 0; i < 5; i++) await Outgoing(again);
        int forFive = MimeOptions.QuoteHtmlRuns - runs;
        clock.Restart();
        string first = await Outgoing(large);
        long firstMs = clock.ElapsedMilliseconds;
        clock.Restart();
        string second = await Outgoing(large);
        long secondMs = clock.ElapsedMilliseconds;
        // Quanto costa la sola pulizia, su una copia della stessa newsletter (un'altra stringa: non è fra quelle già pulite).
        string copy = new(large.AsSpan());
        clock.Restart();
        bool same = ReferenceEquals(MailHtml.Outgoing(copy), copy);
        long cleanMs = clock.ElapsedMilliseconds;
        Check(forFive == 1 && first == prefix + large + "</blockquote>" && second == first && same,
              $"K-04, cinque salvataggi della bozza di una risposta: l'originale si pulisce una volta sola ({forFive}); una newsletter di {large.Length / 1024} KB con 2.500 righe, senza nulla da togliere, riparte uguale " +
              $"(primo salvataggio {firstMs} ms, di cui la pulizia circa {cleanMs}; il secondo {secondMs} ms)");
    }

    // ---------------------------------------------------------------- K-05: i link del testo scritto, e il Ccn di un mailto:

    /// <summary>
    /// K-05, due cose. (a) Un collegamento nel testo scritto: da Ctrl+K e da una bozza ripresa entra solo con http, https o
    /// mailto, ma da un RTF (incollato da un documento scritto da altri, o di una firma o di un modello, anche arrivati con
    /// una copia di sicurezza) entra con l'indirizzo che ha, e FlowToHtml lo spediva com'era. (b) Il Ccn di un link
    /// mailto:: il rapporto lo dà per messo in silenzio. Si legge il sorgente della finestra di scrittura, che non si apre
    /// nelle prove. Sul thread STA di Main, come le altre prove con i documenti di WPF.
    /// </summary>
    static void K5Links()
    {
        // ---------------------------------------------------------------- (a) all'uscita: solo http, https, mailto e tel
        (string Address, bool Leaves)[] links =
        [
            ("https://studio-prova.example/listino?anno=2026&mese=10", true), ("http://studio-prova.example/", true), ("mailto:info@studio-prova.example?subject=Preventivo", true), ("tel:+390000000000", true),
            ("javascript:alert(1)", false), ("vbscript:msgbox(1)", false), ("data:text/html,<script>alert(1)</script>", false), ("file:///C:/Windows/System32/calc.exe", false),
            ("file://server-prova/condivisa/listino.docx", false), ("ms-settings:privacy", false), ("search-ms:query=fatture", false), ("ftp://server-prova.example/x", false),
        ];
        var document = new System.Windows.Documents.FlowDocument();
        var paragraph = new System.Windows.Documents.Paragraph();
        for (int i = 0; i < links.Length; i++)
            paragraph.Inlines.Add(new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run($"testo {i};")) { NavigateUri = new Uri(links[i].Address) });
        paragraph.Inlines.Add(new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("testo relativo;")) { NavigateUri = new Uri("pagina.html", UriKind.Relative) });
        paragraph.Inlines.Add(new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("testo senza indirizzo;")));
        document.Blocks.Add(paragraph);
        string written = FlowToHtml.Convert(document);
        var wrong = new List<string>();
        for (int i = 0; i < links.Length; i++)
        {
            string linked = $"<a href=\"{System.Net.WebUtility.HtmlEncode(links[i].Address)}\">testo {i};</a>";
            bool leaves = written.Contains(linked, StringComparison.Ordinal), text = written.Contains($"testo {i};", StringComparison.Ordinal);
            if (leaves != links[i].Leaves || !text) wrong.Add($"{links[i].Address.Split(':')[0]}: {(leaves ? "parte" : text ? "resta il testo" : "sparisce anche il testo")}");
        }
        int anchors = written.Split("<a href=").Length - 1;
        Check(wrong.Count == 0 && anchors == links.Count(l => l.Leaves) && written.Contains("testo relativo;", StringComparison.Ordinal) && written.Contains("testo senza indirizzo;", StringComparison.Ordinal),
              $"K-05, i link del testo scritto all'uscita (FlowToHtml): di {links.Length} indirizzi partono come link solo quelli http, https, mailto e tel ({anchors} link nell'HTML); degli altri (javascript:, vbscript:, data:, file:, " +
              $"ms-settings:, search-ms:, ftp:, uno relativo, uno vuoto) resta il testo, senza link{(wrong.Count == 0 ? "" : ". Non va: " + string.Join("; ", wrong))}");

        // Da dove entra davvero: un RTF con un campo HYPERLINK (incollato, o di una firma o di un modello), letto da WPF.
        const string rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs22 Cordiali saluti\par " +
                           @"{\field{\*\fldinst{HYPERLINK ""javascript:alert(1)""}}{\fldrslt{Scheda dello studio}}}\par " +
                           @"{\field{\*\fldinst{HYPERLINK ""file://server-prova/condivisa/firma.docx""}}{\fldrslt{Il nostro listino}}}\par " +
                           @"{\field{\*\fldinst{HYPERLINK ""https://studio-prova.example/""}}{\fldrslt{Il sito}}}\par }";
        var signature = new System.Windows.Documents.FlowDocument();
        signature.Blocks.AddRange(UI.RichEditor.BlocksFromRtf(rtf));
        var read = signature.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(p => p.Inlines).SelectMany(K5Hyperlinks).Select(h => h.NavigateUri?.OriginalString ?? "").ToList();
        string signed = FlowToHtml.Convert(signature);
        Check(read.Count == 3 && signed.Contains("<a href=\"https://studio-prova.example/\">", StringComparison.Ordinal) && signed.Split("<a href=").Length - 1 == 1
              && signed.Contains("Scheda dello studio", StringComparison.Ordinal) && signed.Contains("Il nostro listino", StringComparison.Ordinal)
              && !signed.Contains("javascript:", StringComparison.OrdinalIgnoreCase) && !signed.Contains("file:", StringComparison.OrdinalIgnoreCase),
              $"K-05, una firma (o un testo incollato) in RTF con tre campi HYPERLINK: WPF ne fa tre link, con gli indirizzi che hanno ({string.Join(", ", read.Select(r => r.Split(':')[0] + ":"))}); " +
              "nell'HTML che parte resta link solo quello https, degli altri due resta il testo");

        // All'ingresso lo schema si controllava già, per le altre due strade: una bozza ripresa (HtmlToFlow) e Ctrl+K (LinkWindow).
        var resumed = new System.Windows.Documents.FlowDocument();
        resumed.Blocks.AddRange(HtmlToFlow.Blocks("<p><a href=\"javascript:alert(1)\">Uno</a> <a href=\"file:///C:/x\">Due</a> <a href=\"https://studio-prova.example/\">Tre</a> <a href=\"mailto:info@studio-prova.example\">Quattro</a></p>"));
        string again = FlowToHtml.Convert(resumed);
        bool typed = Ch1Code("UI", "LinkWindow.cs") is { } window && window.Contains("uri.Scheme is \"http\" or \"https\" or \"mailto\" ? uri : null")
                     && Ch1Code("Core", "HtmlToFlow.cs") is { } flow && flow.Contains("out var uri) && uri.Scheme is \"http\" or \"https\" or \"mailto\")");
        Check(again.Split("<a href=").Length - 1 == 2 && again.Contains("Uno", StringComparison.Ordinal) && again.Contains("Due", StringComparison.Ordinal) && !again.Contains("javascript:", StringComparison.Ordinal) && typed,
              $"K-05, all'ingresso lo schema si controllava già: una bozza ripresa tiene come link solo http, https e mailto (qui 2 su 4, degli altri il testo), e Inserisci collegamento (Ctrl+K) accetta solo quelli ({typed})");

        // ---------------------------------------------------------------- (b) il Ccn di un link mailto:
        var parsed = Mailto.Parse("mailto:info@studio-prova.example?bcc=archivio@altrove-prova.example&subject=Preventivo");
        if (Ch1Code("UI", "ComposeWindow.xaml.cs") is { } compose && BehSource("UI", "ComposeWindow.xaml") is { } xaml)
        {
            var fill = Ch3Part(compose, "public void FillFromMailto(string link)", "Log.Write($\"scrittura da un link mailto:");
            var show = Ch3Part(compose, "void OnShowCcBcc(object sender, RoutedEventArgs e)", "CcBccButton.Visibility");
            // Il Ccn del link va nel campo, e se c'è (anche da solo, senza Cc) i campi Cc e Ccn si aprono; nessuno li richiude.
            bool filled = Ch3InOrder(fill, "BccBox.Text = initialBcc = string.Join(\"; \", mailto.Bcc);", "if (mailto.Cc.Count + mailto.Bcc.Count > 0) OnShowCcBcc(this, new RoutedEventArgs());");
            bool opened = show.Contains("BccLabel.Visibility = BccBox.Visibility = Visibility.Visible;");
            bool stays = compose.Split("BccBox.Visibility").Length == 2 && !compose.Contains("BccBox.IsEnabled") && xaml.Contains("x:Name=\"BccBox\"") && xaml.Contains("x:Name=\"BccLabel\" Grid.Row=\"3\" Text=\"{local:T 'Ccn'}\"");
            // Il mailto: che Missiva costruisce da sé non ha Cc né Ccn, e resta com'è.
            var own = Mailto.Parse(UI.AdminConsentWindow.MailtoFor("mario@studio-prova.example", "https://login.example/consenso"));
            Check(parsed.Bcc is ["archivio@altrove-prova.example"] && filled && opened && stays && own is { To.Count: 0, Cc.Count: 0, Bcc.Count: 0 } && own.Subject.Length > 0 && own.Body.Contains("https://login.example/consenso"),
                  $"K-05, il Ccn di un link mailto: non entra in silenzio: va nel campo Ccn e la finestra di scrittura apre i campi Cc e Ccn ogni volta che il link ne ha uno (sorgente: campo riempito e aperto {filled}, " +
                  $"reso visibile {opened}, mai richiuso {stays}); il mailto: di \"Scrivi all'amministratore\" ha solo oggetto e testo");
        }
    }

    // ---------------------------------------------------------------- K-06: un file firmato da più persone

    /// <summary>
    /// Certificati di prova per le firme, emessi da una radice di prova con il numero di serie scelto: in una busta le
    /// firme parallele stanno in un insieme che la codifica mette in ordine, e fra due firme con lo stesso emittente decide
    /// il numero di serie. Così si costruiscono i due ordini. Mai installati: la radice vale solo con Pec.TestRoots.
    /// </summary>
    sealed class K6Keys : IDisposable
    {
        readonly RSA rootKey = RSA.Create(2048);
        readonly List<X509Certificate2> made = new();
        public readonly X509Certificate2 Root;
        const string Mail = "1.3.6.1.5.5.7.3.4";
        public const string WebSite = "1.3.6.1.5.5.7.3.1";

        public K6Keys()
        {
            var request = new CertificateRequest("CN=Radice delle firme di prova, O=Missiva, C=IT", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            Root = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-400), DateTimeOffset.Now.AddYears(1));
        }

        /// <param name="serial">Il primo byte del numero di serie (sotto 0x80): fra due firme, prima quella con il più piccolo.</param>
        /// <param name="untilDays">Fino a quando vale, in giorni da oggi: negativo per un certificato scaduto.</param>
        /// <param name="email">L'indirizzo nel certificato (subjectAltName), per le firme della posta.</param>
        public X509Certificate2 Leaf(string subject, byte serial, string usage = Mail, int untilDays = 180, string? email = null)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(usage)], false));
            if (email != null)
            {
                var names = new SubjectAlternativeNameBuilder();
                names.AddEmailAddress(email);
                request.CertificateExtensions.Add(names.Build());
            }
            var now = DateTimeOffset.Now;
            using var issued = request.Create(Root, now.AddDays(untilDays < 0 ? -90 : -2), now.AddDays(untilDays), [serial, 1, 2, 3, 4, 5, 6, 7]);
            using var withKey = issued.CopyWithPrivateKey(key);
            var loaded = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
            made.Add(loaded);
            return loaded;
        }

        public void Dispose()
        {
            foreach (var certificate in made) certificate.Dispose();
            Root.Dispose();
            rootKey.Dispose();
        }
    }

    /// <summary>Un file .p7m con le firme parallele di quei certificati sullo stesso contenuto (CAdES, contenuto dentro la busta).</summary>
    static byte[] K6File(byte[] content, params X509Certificate2[] signers)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: false);
        foreach (var signer in signers)
        {
            var who = new CmsSigner(signer) { IncludeOption = X509IncludeOption.EndCertOnly };
            who.SignedAttributes.Add(new Pkcs9SigningTime(new DateTime(2026, 9, 26, 8, 30, 0, DateTimeKind.Utc)));
            cms.ComputeSignature(who);
        }
        return cms.Encode();
    }

    /// <summary>
    /// Un messaggio firmato S/MIME (multipart/signed) da maria.rossi@studio-prova.example con le firme parallele di quei
    /// certificati: MimeKit ne mette una sola, quindi la parte della firma si rifà con una busta staccata sugli stessi byte
    /// che MimeKit ha firmato.
    /// </summary>
    static byte[] K6SignedMail(params X509Certificate2[] signers)
    {
        var mail = new MimeMessage { Subject = "Preventivo firmato", Date = new DateTimeOffset(2026, 9, 26, 10, 30, 0, TimeSpan.FromHours(2)) };
        mail.From.Add(new MailboxAddress("Maria Rossi", "maria.rossi@studio-prova.example"));
        mail.To.Add(new MailboxAddress("", "mario@studio-prova.example"));
        using var context = new MimeKit.Cryptography.TemporarySecureMimeContext();
        var signed = MimeKit.Cryptography.MultipartSigned.Create(context, new MimeKit.Cryptography.CmsSigner(signers[0]), new TextPart("plain") { Text = "In allegato il preventivo." });
        // I byte firmati: la prima parte, con gli a capo di rete, come la scrive chi verifica.
        var options = FormatOptions.Default.Clone();
        options.NewLineFormat = NewLineFormat.Dos;
        using var content = new MemoryStream();
        signed[0].WriteTo(options, content);
        var cms = new SignedCms(new ContentInfo(content.ToArray()), detached: true);
        foreach (var signer in signers) cms.ComputeSignature(new CmsSigner(signer) { IncludeOption = X509IncludeOption.EndCertOnly });
        ((MimePart)signed[1]).Content = new MimeContent(new MemoryStream(cms.Encode()));
        mail.Body = signed;
        using var raw = new MemoryStream();
        mail.WriteTo(raw);
        return raw.ToArray();
    }

    /// <summary>I firmatari di una busta nell'ordine in cui stanno nel file.</summary>
    static List<string> K6Order(byte[] file)
    {
        var cms = new SignedCms();
        cms.Decode(file);
        return cms.SignerInfos.Cast<SignerInfo>().Select(s => s.Certificate?.GetNameInfo(X509NameType.SimpleName, false) ?? "").ToList();
    }

    /// <summary>
    /// K-06: un file .p7m con più firme parallele. P7m.Check guardava il certificato della sola prima firma: con una buona
    /// e una no (certificato scaduto, o di un sito web) la finestra diceva "firma valida" o no secondo l'ordine nel file,
    /// e dell'altro firmatario non c'era traccia. Ora lo stato è il peggiore fra tutte le firme, di tutte le buste, e i
    /// firmatari si leggono tutti.
    /// </summary>
    static void K6Signatures()
    {
        using var keys = new K6Keys();
        var roots = Pec.TestRoots;
        try
        {
            Pec.TestRoots = [keys.Root];
            byte[] content = Encoding.UTF8.GetBytes(Fattura);
            // Due certificati buoni della stessa persona, uno con il numero di serie prima e uno dopo quello degli altri: i due ordini.
            X509Certificate2 early = keys.Leaf("CN=Maria Rossi, C=IT", 0x20), late = keys.Leaf("CN=Maria Rossi, C=IT", 0x60);
            X509Certificate2 expired = keys.Leaf("CN=Mario Rossi, C=IT", 0x40, untilDays: -1), site = keys.Leaf("CN=sito-prova.example", 0x40, K6Keys.WebSite), ada = keys.Leaf("CN=Ada Bianchi, C=IT", 0x40);
            (string What, X509Certificate2 Other, string Name, SignatureState State, string Tone)[] pairs =
            [
                ("un certificato scaduto", expired, "Mario Rossi", SignatureState.Untrusted, "warn"),
                ("il certificato di un sito web", site, "sito-prova.example", SignatureState.Unsuitable, "danger"),
                ("un'altra firma buona", ada, "Ada Bianchi", SignatureState.Verified, "ok"),
            ];
            var wrong = new List<string>();
            var said = new List<string>();
            foreach (var (what, other, name, state, tone) in pairs)
            {
                byte[] goodFirst = K6File(content, early, other), goodLast = K6File(content, other, late);
                List<string> orderFirst = K6Order(goodFirst), orderLast = K6Order(goodLast);
                if (!(orderFirst is ["Maria Rossi", var second] && second == name && orderLast is [var first, "Maria Rossi"] && first == name))
                    wrong.Add($"{what}: i due file non hanno i due ordini ({string.Join(", ", orderFirst)} e {string.Join(", ", orderLast)})");
                foreach (var (file, order) in new[] { (goodFirst, "buona per prima"), (goodLast, "buona per ultima") })
                {
                    var opened = P7m.Open(file, "contratto.pdf.p7m");
                    var (text, shown) = P7m.Band(opened);
                    bool names = text.Contains("Maria Rossi", StringComparison.Ordinal) && text.Contains(name, StringComparison.Ordinal);
                    // Dove una firma non va, la fascia dice di chi è il certificato che non va.
                    bool blames = state == SignatureState.Verified || text.Contains("il certificato di " + name, StringComparison.Ordinal);
                    if (opened.Signature != state || shown != tone || !names || !blames) wrong.Add($"{what}, {order}: {opened.Signature}, \"{text}\"");
                    else if (order == "buona per prima") said.Add(text);
                }
            }
            Check(wrong.Count == 0,
                  $"K-06, un file .p7m con due firme parallele, una buona e accanto {string.Join(", ", pairs.Select(p => p.What))}, nei due ordini: lo stato è il peggiore fra le due (giallo, rosso, verde) qualunque sia la prima, " +
                  $"la fascia nomina tutti e due i firmatari e dice di chi è il certificato che non va{(wrong.Count == 0 ? ": \"" + string.Join("\" | \"", said) + "\"" : ". Non va: " + string.Join(" | ", wrong))}");

            // Una busta dentro l'altra: fuori una firma buona, dentro quella con il certificato di un sito web. Lo stato era già
            // il peggiore, ma la fascia dava la colpa al firmatario di fuori.
            var layered = P7m.Open(K6File(K6File(content, site), early), "contratto.pdf.p7m.p7m");
            string layeredText = P7m.Band(layered).Text;
            // Tre firme, e una busta manomessa con due firme.
            var three = P7m.Open(K6File(content, early, keys.Leaf("CN=Ada Bianchi, C=IT", 0x50), expired), "contratto.pdf.p7m"); // un numero di serie suo: due certificati con lo stesso non si distinguono
            byte[] broken = K6File(content, early, ada);
            broken[broken.AsSpan().IndexOf("1000.00"u8)] = (byte)'9';
            var tampered = P7m.Open(broken, "fattura.xml.p7m");
            Check(layered is { Signature: SignatureState.Unsuitable, Layers: 2 } && layeredText.Contains("il certificato di sito-prova.example", StringComparison.Ordinal) && layeredText.Contains("Maria Rossi", StringComparison.Ordinal)
                  && three.Signature == SignatureState.Untrusted && new[] { "Maria Rossi", "Ada Bianchi", "Mario Rossi" }.All(n => P7m.Band(three).Text.Contains(n, StringComparison.Ordinal))
                  && tampered.Signature == SignatureState.Invalid && P7m.Band(tampered).Tone == "danger",
                  $"K-06, una firma buona attorno a una busta firmata con il certificato di un sito web: la fascia dice di chi è il certificato che non va, non il firmatario di fuori (\"{layeredText}\"); " +
                  $"con tre firme di cui una scaduta si leggono tutti e tre i nomi; due firme su un contenuto cambiato dopo: firma non valida");

            // Una firma sola: la fascia di prima, parola per parola.
            var when = new DateTime(2026, 9, 26, 8, 30, 0, DateTimeKind.Utc).ToLocalTime();
            string at = $" il {when:dd/MM/yyyy} alle {when:HH:mm}";
            (X509Certificate2[] Signers, string Name, string Text, string Tone)[] single =
            [
                ([early], "f.xml.p7m", $"Firmato da Maria Rossi{at}: firma valida, certificato riconosciuto da Windows.", "ok"),
                ([expired], "f.xml.p7m", $"Firmato da Mario Rossi{at}: il documento è integro, ma il certificato non è fra quelli riconosciuti da Windows " +
                                         "(per i certificati di firma italiani serve l'elenco dell'AgID, come nei programmi di firma).", "warn"),
                ([site], "f.xml.p7m", "Firma non valida: il certificato di sito-prova.example non serve a firmare documenti (per esempio è quello di un sito web). Non fidarti del contenuto.", "danger"),
            ];
            var changed = single.Where(s => P7m.Band(P7m.Open(K6File(content, s.Signers), s.Name)) != (s.Text, s.Tone)).Select(s => P7m.Band(P7m.Open(K6File(content, s.Signers), s.Name)).Text).ToList();
            var twice = P7m.Open(K6File(K6File(content, early), early), "f.xml.p7m.p7m");
            Check(changed.Count == 0 && P7m.Band(twice) == ($"Firmato da Maria Rossi{at} (2 firme una dentro l'altra): firma valida, certificato riconosciuto da Windows.", "ok") && twice.Signer == "Maria Rossi",
                  $"K-06, con una firma sola (buona, scaduta, di un sito web) e con la stessa persona che firma due volte la fascia è quella di prima, parola per parola{(changed.Count == 0 ? "" : ". Cambiate: " + string.Join(" | ", changed))}");

            // Lo stesso per un messaggio firmato S/MIME con due firme: Pec.Verify guardava solo la prima.
            const string from = "maria.rossi@studio-prova.example";
            X509Certificate2 mailEarly = keys.Leaf("CN=Maria Rossi, C=IT", 0x20, email: from), mailLate = keys.Leaf("CN=Maria Rossi, C=IT", 0x60, email: from),
                             mailExpired = keys.Leaf("CN=Maria Rossi (vecchio), C=IT", 0x40, untilDays: -1, email: from);
            static PecInfo? Signed(byte[] raw)
            {
                var message = MimeMessage.Load(new MemoryStream(raw));
                return Pec.Open(raw, EmlProvider.BodyOf(message)).Pec;
            }
            PecInfo? alone = Signed(K6SignedMail(mailEarly)), both = Signed(K6SignedMail(mailEarly, mailLate)), goodFirstMail = Signed(K6SignedMail(mailEarly, mailExpired)), goodLastMail = Signed(K6SignedMail(mailExpired, mailLate));
            Check(alone is { Signature: SignatureState.Verified, Signer: "Maria Rossi" } && both is { Signature: SignatureState.Verified }
                  && goodFirstMail is { Signature: SignatureState.Untrusted, Signer: "Maria Rossi (vecchio)" } && goodLastMail is { Signature: SignatureState.Untrusted, Signer: "Maria Rossi (vecchio)" },
                  $"K-06, un messaggio firmato S/MIME con due firme, una buona e una con un certificato scaduto: nei due ordini vale la peggiore, con il suo firmatario (buona per prima: {goodFirstMail?.Signature} di {goodFirstMail?.Signer}; " +
                  $"buona per ultima: {goodLastMail?.Signature} di {goodLastMail?.Signer}); una firma sola e due buone restano verificate ({alone?.Signature}, {both?.Signature})");
        }
        finally
        {
            Pec.TestRoots = roots;
        }
    }

    // ---------------------------------------------------------------- scelte del 14 ottobre: i suffissi di .it dalla lista pubblica (K-02)

    /// <summary>
    /// K-02, scelte del 14 ottobre 2026: l'elenco dei suffissi di .it (Safety.IsItalianSuffix) è quello della lista pubblica
    /// dei suffissi, sezione ICANN, con le varianti che il 13 ottobre mancavano: nomi composti, in altre lingue, sigle a tre
    /// lettere delle regioni, nomi con le lettere accentate (scritti come nei nomi di dominio, xn--...). Per ognuno le tre
    /// regole danno l'ente, non il suffisso; un dominio normale sotto .it resta com'era. Gli enti sono finti, i suffissi veri.
    /// </summary>
    static void K2PublicList()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        // Due composti, due in un'altra lingua, la sigla di una regione, quella del Sud Sardegna, una variante del Trentino,
        // e due con le lettere accentate: südtirol e forlì-cesena.
        string[] added = ["reggio-emilia", "la-spezia", "bozen", "florence", "lom", "su", "trentino-sudtirol", "xn--sdtirol-n2a", "xn--forl-cesena-fcb"];
        var wrong = new List<string>();
        foreach (var name in added)
        {
            string organization = $"comune-{tag}.{name}.it", email = "protocollo@posta." + organization;
            string? logo = SenderLogos.DomainOf(email);
            string images = Settings.ImageKey(email), site = Safety.BaseDomain("servizi." + organization);
            if (logo != organization || images != organization || site != organization) wrong.Add($"{name}.it: {logo ?? "nessuno"}, {images}, {site}");
        }
        // Da vicino, con due dei nomi nuovi: "Mostra immagini" su un comune non vale per un altro ente sotto lo stesso nome, e
        // un link che mostra un ente e porta a un altro ha l'avviso.
        var allowed = new Settings();
        allowed.AllowImagesFrom($"protocollo@comune-{tag}.bozen.it");
        bool neighbour = allowed.ShowsImagesFrom($"info@altro-{tag}.bozen.it");
        string? warning = Safety.LinkWarning($"comune-{tag}.reggio-emilia.it", new Uri($"https://altro-{tag}.reggio-emilia.it/accesso"));
        Check(wrong.Count == 0 && allowed.ImageSenders is [var only] && only == $"comune-{tag}.bozen.it" && !neighbour && warning != null,
              $"K-02 (scelte del 14 ottobre), l'elenco dalla lista pubblica dei suffissi: {added.Length} nomi che prima mancavano (composti come reggio-emilia e la-spezia, in altre lingue come bozen e florence, " +
              "la sigla di una regione, quella del Sud Sardegna, una variante del Trentino, südtirol e forlì-cesena con le lettere accentate scritte come nei nomi di dominio) danno ognuno l'ente e non il suffisso " +
              "per i loghi (SenderLogos.DomainOf), per \"Mostra immagini\" (Settings.ImageKey) e per gli avvisi sui link (Safety.BaseDomain); \"Mostra immagini\" su un comune sotto bozen.it non vale per un altro ente lì sotto, " +
              "e un link fra due enti sotto reggio-emilia.it ha l'avviso" + (wrong.Count == 0 ? "" : ": " + string.Join("; ", wrong)));

        // Un dominio normale sotto .it, e nomi che contengono uno dei nomi nuovi senza esserlo: tutto come prima.
        (string Email, string Organization)[] normal =
        [
            ("info@ditta-prova.it", "ditta-prova.it"), ("ordini@negozio.ditta-prova.it", "ditta-prova.it"), ("x@posta.reggio-emilia-prova.it", "reggio-emilia-prova.it"),
            ("y@bozen.ditta-prova.it", "ditta-prova.it"), ("z@florence-prova.it", "florence-prova.it"), ("a@lom.ditta-prova.com", "ditta-prova.com"), ("b@servizi.bozen.example", "bozen.example"),
        ];
        var moved = normal.Where(n => SenderLogos.DomainOf(n.Email) != n.Organization || Settings.ImageKey(n.Email) != n.Organization || Safety.BaseDomain(n.Email[(n.Email.IndexOf('@') + 1)..]) != n.Organization)
                          .Select(n => $"{n.Email}: {SenderLogos.DomainOf(n.Email) ?? "nessuno"}, {Settings.ImageKey(n.Email)}, {Safety.BaseDomain(n.Email[(n.Email.IndexOf('@') + 1)..])}").ToList();
        Check(moved.Count == 0 && Safety.LinkWarning("www.ditta-prova.it", new Uri("https://ordini.ditta-prova.it/")) == null && Safety.LinkWarning("ditta-prova.it", new Uri("https://altra-ditta-prova.it/")) != null,
              $"K-02 (scelte del 14 ottobre), {normal.Length} indirizzi normali sotto .it (ditta-prova.it, anche con un sottodominio che si chiama come un suffisso) e nomi che somigliano ai suffissi nuovi senza esserlo " +
              "(reggio-emilia-prova.it, florence-prova.it, bozen.example): l'organizzazione è quella di prima per le tre regole, e gli avvisi sui link sono quelli di prima" +
              (moved.Count == 0 ? "" : ": cambiati " + string.Join("; ", moved)));
    }

    // ---------------------------------------------------------------- scelte del 14 ottobre: le voci larghe all'avvio (scelta 25)

    /// <summary>
    /// Scelta 25 (scelte del 14 ottobre 2026; T-17 e K-02): all'avvio (UpgradeImageSenders, da Store.Load) escono tutte le
    /// voci larghe di "Mostra immagini", con la regola di una copia importata (Settings.IsBroadImageKey): un suffisso da
    /// solo, italiano o di un altro paese, e un servizio di posta personale intero. Prima all'avvio uscivano solo i suffissi
    /// italiani, e una voce co.uk o gmail.com faceva caricare da sole le immagini di chiunque scrivesse da lì. Impostazioni
    /// finte, scritte e rilette da una cartella di prova; mai i dati veri.
    /// </summary>
    static void K2BroadAtStart()
    {
        string tag = Guid.NewGuid().ToString("N")[..8], firm = $"ditta-{tag}.pd.it";
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-scelta25-" + tag);
        try
        {
            var before = new Settings
            {
                ImageSenders = ["co.uk", "gmail.com", "amazon.it", "LIBERO.IT", "unibo.it", "com.au", "news.amazon.co.uk", "pd.it", "mario.rossi@gmail.example", "x@co.uk", "y@pd.it", "info@" + firm,
                                "gov.it", "fornitore-prova.example"],
            };
            Store.TryWrite(dir, before, null, out _);
            var after = Store.Read(dir, out _);
            var gone = after.UpgradeImageSenders(); // come Store.Load
            string line = Store.BroadImagesLine(gone);
            string[] kept = ["amazon.it", "unibo.it", "amazon.co.uk", "mario.rossi@gmail.example", "x@co.uk", "y@pd.it", firm, "fornitore-prova.example"];
            string[] closed = ["a@negozio-prova.co.uk", "mario.rossi@gmail.com", "anna@libero.it", "b@negozio-prova.com.au", $"x@altro-{tag}.pd.it", $"urp@ente-{tag}.gov.it"];
            var open = closed.Where(after.ShowsImagesFrom).ToList();
            // Salvate e rilette: all'avvio dopo non esce più nulla. "Mostra immagini" premuto di nuovo su un mittente di Gmail ricorda il suo indirizzo.
            Store.TryWrite(dir, after, null, out _);
            var again = Store.Read(dir, out _);
            var second = again.UpgradeImageSenders();
            again.AllowImagesFrom("ada.prova@gmail.com");
            Check(gone.SequenceEqual(["co.uk", "gmail.com", "libero.it", "com.au", "pd.it", "gov.it"]) && after.ImageSenders.SequenceEqual(kept) && open.Count == 0 && second.Count == 0
                  && again.ImageSenders.SequenceEqual([.. kept, "ada.prova@gmail.com"]) && !again.ShowsImagesFrom("mario.rossi@gmail.com") && after.ShowsImagesFrom("ordini@amazon.it") && after.ShowsImagesFrom("info@" + firm)
                  && line == "immagini: 6 voci di \"Mostra immagini\" (co.uk, gmail.com, libero.it, com.au, pd.it, gov.it) valevano per chiunque scriva da un intero servizio di posta o da un intero suffisso " +
                             "e sono uscite; per quei mittenti le immagini restano bloccate finché non si preme di nuovo \"Mostra immagini\""
                  && Store.BroadImagesLine(["co.uk"]).StartsWith("immagini: 1 voce di \"Mostra immagini\" (co.uk) valeva per chiunque", StringComparison.Ordinal),
                  $"scelta 25 (scelte del 14 ottobre), all'avvio escono tutte le voci larghe di \"Mostra immagini\": oltre ai suffissi italiani (pd.it, gov.it) un suffisso di un altro paese (co.uk, com.au) " +
                  $"e un servizio di posta intero (gmail.com, libero.it), fuori {gone.Count} su {before.ImageSenders.Count}; restano gli indirizzi, anche di chi scrive da un suffisso (x@co.uk), e le organizzazioni " +
                  $"(amazon.it, unibo.it, amazon.co.uk, una ditta sotto pd.it); le immagini di chi scrive da lì restano bloccate (caricate da sole per {open.Count} su {closed.Length}) finché non si preme di nuovo " +
                  "\"Mostra immagini\", che ricorda l'indirizzo; la riga del registro dice quante voci e quali, e all'avvio dopo non esce più nulla");
        }
        finally
        {
            DeleteQuietly(dir);
        }

        // La stessa regola da una copia importata (DropBroadImageSenders), e un elenco che su questo PC non si decifra resta
        // com'è (il cifrato è quello di S-26: nessun testo nuovo fra quelli che non si decifrano).
        string[] mixed = ["co.uk", "gmail.com", "amazon.it", "com", "pd.it", "comune-prova.pd.it", "veneto.it", "mario.rossi@gmail.example", "x@gov.it", "newsletter-prova.example", "pec.it"];
        var atStart = new Settings { ImageSenders = [.. mixed] };
        var outAtStart = atStart.UpgradeImageSenders();
        var imported = new Settings { ImageSenders = [.. mixed] };
        int outImported = imported.DropBroadImageSenders();
        var unreadable = System.Text.Json.JsonSerializer.Deserialize("{\"ImageSenders\":\"dpapi:CCCC\"}", SettingsJson.Default.Settings)!;
        var outUnreadable = unreadable.UpgradeImageSenders();
        Check(atStart.ImageSenders.SequenceEqual(imported.ImageSenders) && atStart.ImageSenders.SequenceEqual(["amazon.it", "comune-prova.pd.it", "mario.rossi@gmail.example", "x@gov.it", "newsletter-prova.example"])
              && outImported == mixed.Length - imported.ImageSenders.Count && outAtStart.All(Settings.IsBroadImageKey) && !imported.ImageSenders.Any(Settings.IsBroadImageKey)
              && outUnreadable.Count == 0 && unreadable.ImageSenders is [var placeholder] && ProtectedTextConverter.IsUnreadable(placeholder),
              $"scelta 25, all'avvio e da una copia importata escono le stesse voci, con la stessa regola (Settings.IsBroadImageKey: fuori {outAtStart.Count} su {mixed.Length}, più un nome senza punto che usciva già); " +
              "un elenco che su questo PC non si decifra resta com'è, e il suo cifrato torna nel file");
    }

    /// <summary>I link dentro un pezzo di testo, anche annidati in uno Span.</summary>
    static IEnumerable<System.Windows.Documents.Hyperlink> K5Hyperlinks(System.Windows.Documents.Inline inline) => inline switch
    {
        System.Windows.Documents.Hyperlink link => [link],
        System.Windows.Documents.Span span => span.Inlines.SelectMany(K5Hyperlinks),
        _ => [],
    };
}
#endif
