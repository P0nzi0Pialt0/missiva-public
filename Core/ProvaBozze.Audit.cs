#if DEBUG
using System.Buffers.Binary;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Missiva;

/// <summary>
/// Le prove degli audit (docs\audit\ESITO.md). Blocco 1: la pulizia dell'HTML delle email (MailHtml) con i messaggi
/// costruiti apposta che prima chiudevano Missiva o la fermavano per secondi, e con alcune uscite scritte per esteso.
/// Solo dati finti. Missiva.exe --prova-audit risultato.txt le esegue da sole, in pochi secondi.
/// </summary>
static partial class ProvaBozze
{
    /// <summary>
    /// Audit, blocco 5: i difetti veri trovati dal rapporto sulle funzionalità (docs\audit\funzionalita.md). Con
    /// "Missiva.exe --prova-audit risultato.txt difetti" solo queste.
    /// </summary>
    static void DefectAuditTests()
    {
        Report.Add("--- Audit, blocco 5: difetti veri del rapporto sulle funzionalità");
        StoreAuditTests();
        DataFilesAuditTests();
        UnreadableSecretsAuditAsync().GetAwaiter().GetResult();
        SendFailureAuditTests();
        CalendarAuditTests();
        AutoRepliesAuditTests();
        RulesOrderAuditAsync().GetAwaiter().GetResult();
        GoogleScopesAuditTests();
        SmallDefectsAuditTests();
        LeftoversAuditTests();
    }

    /// <summary>
    /// I tre punti lasciati dai blocchi precedenti: la rubrica che impara come nome l'indirizzo di un altro (blocco 3),
    /// l'HTML delle bozze analizzato senza i tetti di MailHtml (blocco 1), la chiusura di un provider IMAP che aspetta il
    /// LOGOUT sul thread di chi chiama (blocco 4).
    /// </summary>
    static void LeftoversAuditTests()
    {
        // La rubrica: "assistenza@banca-prova.example" <truffa.NNN@altro-prova.example> non diventa il nome di quell'indirizzo.
        var tag = Guid.NewGuid().ToString("N")[..8];
        string fake = $"truffa.{tag}@altro-prova.example", honest = $"maria.{tag}@fornitori-prova.example";
        AddressBook.Received([new Address { Name = "assistenza@banca-prova.example", Email = fake }, new Address { Name = "Maria Rossi", Email = honest }], DateTime.Now);
        var learned = AddressBook.Suggest(tag, 5);
        Check(learned.FirstOrDefault(e => e.Email == fake) is { Name: "" } && learned.FirstOrDefault(e => e.Email == honest) is { Name: "Maria Rossi" },
              $"rubrica: un nome che è l'indirizzo di un altro non si impara come nome di quella persona (imparato: \"{learned.FirstOrDefault(e => e.Email == fake)?.Name}\"); un nome vero sì");

        // L'HTML di una bozza costruito per costare (elementi di formato mai chiusi, riaperti in ogni paragrafo): sotto i tetti
        // di MailHtml l'editor riceve il solo testo, subito. Un HTML normale tiene il formato come prima.
        // (Senza i tetti: 700 font e 700 paragrafi, 21 KB, 259 ms; il costo cresce con il quadrato, 2000 e 2000 sarebbero secondi e gigabyte.)
        var bomb = string.Concat(Enumerable.Repeat("<font color=red>", 2000)) + string.Concat(Enumerable.Range(1, 2000).Select(n => $"<p>riga {n} &egrave; qui</p>"));
        var clock = Stopwatch.StartNew();
        var blocks = HtmlToFlow.Blocks(bomb);
        var took = clock.ElapsedMilliseconds;
        var text = string.Join("\n", blocks.OfType<System.Windows.Documents.Paragraph>().Select(p => new System.Windows.Documents.TextRange(p.ContentStart, p.ContentEnd).Text));
        Check(took < 500 && text.Contains("riga 1 è qui") && text.Contains("riga 2000 è qui") && !text.Contains('<'),
              $"bozza con un HTML costruito per costare (2000 font mai chiusi, 2000 paragrafi, {bomb.Length / 1024} KB): l'editor riceve il solo testo in {took} ms, senza costruire l'albero");
        var normal = HtmlToFlow.Blocks("<p>Buongiorno <b>Maria</b>,</p><ul><li>uno</li><li>due</li></ul>");
        var shape = string.Join(" ", normal.Select(b => b is System.Windows.Documents.Paragraph p ? $"P[{string.Join(",", p.Inlines.Select(i => i.GetType().Name + (i.FontWeight == System.Windows.FontWeights.Bold ? "*" : "")))}]" : b is System.Windows.Documents.List l ? $"L{l.ListItems.Count}" : b.GetType().Name));
        Check(shape == "P[Run,Run*,Run] L2", $"bozza con un HTML normale: paragrafo con il grassetto ed elenco di due voci, come prima ({shape})");

        // La chiusura di un provider IMAP con un server che non risponde al LOGOUT: chi chiama non aspetta.
        using (var silent = new FakeImap(new Flavor(Gmail: false)))
        {
            ImapProvider.TestPlain = true;
            try
            {
                var imap = new ImapProvider(SpeedAccount(silent, "mario@studio-prova.example"));
                imap.GetFoldersAsync().GetAwaiter().GetResult();
                silent.SilentLogout = true;
                clock.Restart();
                imap.Dispose();
                Check(clock.ElapsedMilliseconds < 1000, $"provider IMAP chiuso con un server che non risponde al LOGOUT: chi chiama aspetta {clock.ElapsedMilliseconds} ms (prima fino a 60 s)");
            }
            finally
            {
                ImapProvider.TestPlain = false;
            }
        }
    }

    /// <summary>
    /// I difetti piccoli: P-08 (avvio con Windows, su chiavi di prova del registro, mai quelle vere), F-02 (versione nel
    /// manifesto), F-19 (suggerimento dell'ordinamento), F-01 (disinstallazione con Windows PowerShell: i file dei sorgenti,
    /// letti e mai eseguiti).
    /// </summary>
    static void SmallDefectsAuditTests()
    {
        // P-08: lo stato vero dell'avvio con Windows, con Run e StartupApproved in una chiave di prova.
        static string RunValues()
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            using var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            return string.Join("\n", (run?.GetValueNames() ?? []).Order().Select(n => n + "=" + run!.GetValue(n))) + "\n" +
                   string.Join("\n", (approved?.GetValueNames() ?? []).Order().Select(n => n + "=" + Convert.ToHexString(approved!.GetValue(n) as byte[] ?? [])));
        }
        var real = RunValues();
        var root = @"Software\Missiva-prova-audit-" + Guid.NewGuid().ToString("N")[..8];
        string run = root + @"\Run", approved = root + @"\StartupApproved";
        const string command = "\"C:\\prova\\Missiva.exe\" --nascosta";
        try
        {
            void Windows(byte first)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(approved);
                key.SetValue("Missiva", new byte[] { first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, Microsoft.Win32.RegistryValueKind.Binary);
            }
            bool Starts() => UI.App.StartsWithWindows(run, approved);
            bool none = !Starts();
            UI.App.WriteStartWithWindows(true, command, byUser: false, run, approved);
            bool on = Starts();
            Windows(3); // spenta da App di avvio di Windows
            bool offByWindows = !Starts();
            UI.App.WriteStartWithWindows(true, command, byUser: false, run, approved); // all'avvio di Missiva: la scelta fatta in Windows resta
            bool stillOff = !Starts();
            UI.App.WriteStartWithWindows(true, command, byUser: true, run, approved); // dalla casella delle Impostazioni
            bool onAgain = Starts();
            Windows(2);
            bool enabled2 = Starts();
            Windows(6);
            bool enabled6 = Starts();
            Windows(7);
            bool disabled7 = !Starts();
            UI.App.WriteStartWithWindows(false, command, byUser: true, run, approved);
            bool off = !Starts();
            Check(none && on && offByWindows && stillOff && onAgain && enabled2 && enabled6 && disabled7 && off,
                  "avvio con Windows, su chiavi di prova: la casella legge lo stato vero (valore in Run, e non spento da App di avvio di Windows); spenta in Windows resta spenta " +
                  $"all'avvio di Missiva e si riaccende dalla casella [{none} {on} {offByWindows} {stillOff} {onAgain} {enabled2} {enabled6} {disabled7} {off}]");
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(root, false);
        }
        Check(RunValues() == real, "avvio con Windows: le chiavi vere di Windows (Run e StartupApproved) sono uguali a prima della prova");

        // F-19: il suggerimento dell'ordinamento non dice "Gmail" per IMAP e .eml.
        Check(UI.MainWindow.SortOnlyByDate(AccountKind.Gmail).Contains("Gmail") && !UI.MainWindow.SortOnlyByDate(AccountKind.Imap).Contains("Gmail"),
              "ordinamento solo per data: il suggerimento dice Gmail solo per gli account Gmail");

        // F-02 e F-01: i file dei sorgenti, se l'eseguibile gira dalla cartella di compilazione (altrimenti non si controllano).
        var source = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && source != null && !File.Exists(Path.Combine(source, "Missiva.csproj")); i++) source = Path.GetDirectoryName(source.TrimEnd('\\'));
        if (source == null || !File.Exists(Path.Combine(source, "Missiva.csproj")))
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: versione del manifesto e script di disinstallazione non controllati)");
            return;
        }
        // Dal 14 ottobre 2026 il progetto scrive solo i primi due numeri (MissivaVersion); il terzo lo conta git a ogni
        // compilazione (MissivaGitVersion), e il manifesto, che non cambia a ogni compilazione, tiene i primi due.
        var project = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Path.Combine(source, "Missiva.csproj")), @"<MissivaVersion>(\d+\.\d+)</MissivaVersion>").Groups[1].Value;
        var manifest = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Path.Combine(source, "app.manifest")), "assemblyIdentity version=\"([\\d.]+)\"").Groups[1].Value;
        Check(project.Length > 0 && manifest == project + ".0.0", $"versione: app.manifest ({manifest}) dice i primi due numeri di Missiva.csproj ({project})");
        // L'eseguibile della suite gira dalla cartella di compilazione, che è un repository git: il terzo numero c'è.
        var built = typeof(AppVersion).Assembly.GetName().Version;
        Check(built != null && $"{built.Major}.{built.Minor}" == project && built.Build > 0 && AppVersion.Number == $"{project}.{built.Build}"
              && System.Text.RegularExpressions.Regex.IsMatch(AppVersion.Text, $@"^{System.Text.RegularExpressions.Regex.Escape(AppVersion.Number)} del \d\d/\d\d/\d{{4}} \([0-9a-f]{{7,}}\)(, con modifiche non ancora in un commit)?$"),
              $"versione: l'eseguibile dice \"{AppVersion.Text}\": i due numeri del progetto, il terzo dai commit, la data e il commit dell'ultima modifica compresa");
        var uninstall = File.ReadAllBytes(Path.Combine(source, "disinstalla.ps1"));
        var uninstallText = Encoding.UTF8.GetString(uninstall);
        var install = File.ReadAllText(Path.Combine(source, "installa.ps1"));
        var uninstallLine = install.Split('\n').FirstOrDefault(l => l.Contains("UninstallString =")) ?? "";
        Check(uninstall is [0xEF, 0xBB, 0xBF, ..] && !uninstallText.Contains("pwsh.exe") && !uninstallText.Contains("::CurrentUserOnly")
              && uninstallLine.Contains("$windowsPowerShell") && !uninstallLine.Contains("pwsh") && install.Contains(@"System32\WindowsPowerShell\v1.0\powershell.exe"),
              "disinstallazione: la voce di App installate chiama Windows PowerShell (powershell.exe), e disinstalla.ps1 è UTF-8 con BOM, senza pwsh.exe né l'opzione che in 5.1 non esiste");
    }

    /// <summary>P-03 e P-06: un nuovo accesso a Google che non dà più un permesso che c'era (calendario, contatti) lo dice.</summary>
    static void GoogleScopesAuditTests()
    {
        const string mail = "https://mail.google.com/ openid email";
        string calendar = GoogleAuth.CalendarScope, read = GoogleAuth.ContactsScope, write = GoogleAuth.ContactsWriteScope;
        var heard = new List<string>();
        void OnLost(MailAccount account, string notice) => heard.Add(account.Email + "|" + notice);
        GoogleAuth.PermissionsLost += OnLost;
        try
        {
            var account = new MailAccount { Kind = AccountKind.Gmail, Email = "mario.prova@gmail.example", GoogleScopes = $"{mail} {calendar} {write}" };
            // "Consenti la modifica dei contatti" con la casella del calendario tolta nella pagina di Google.
            var notice = GoogleAuth.SetScopes(account, $"{mail} {write}");
            Check(notice != null && notice.Contains("il calendario") && notice.Contains(account.Email) && !notice.Contains("contatti di") && heard.Count == 1 && !GoogleAuth.HasCalendar(account) && GoogleAuth.HasContactsWrite(account),
                  "nuovo accesso a Google senza la casella del calendario: l'avviso dice che manca il calendario, di quale account, e come riaverlo");
            Check(GoogleAuth.Lost($"{mail} {calendar} {write}", $"{mail} {calendar} {read}") is ["la modifica dei contatti (restano in sola lettura)"]
                  && GoogleAuth.Lost($"{mail} {calendar} {write}", mail) is ["il calendario", "i contatti"]
                  && GoogleAuth.Lost($"{mail} {read}", $"{mail} {write}").Count == 0 && GoogleAuth.Lost(null, mail).Count == 0 && GoogleAuth.Lost($"{mail} {calendar}", $"{mail} {calendar} {read}").Count == 0,
                  "permessi di Google tolti: modifica dei contatti, contatti, calendario riconosciuti uno per uno; nessun avviso se ci sono tutti, se ne arriva uno in più o per un account di prima");
            heard.Clear();
            Check(GoogleAuth.SetScopes(account, $"{mail} {write} {calendar}") == null && heard.Count == 0 && GoogleAuth.HasCalendar(account), "nuovo accesso a Google con tutti i permessi: nessun avviso");
        }
        finally
        {
            GoogleAuth.PermissionsLost -= OnLost;
        }
    }

    /// <summary>
    /// P-02: Su e Giù sulle regole di Outlook con un PATCH che fallisce a metà. Il gestore finto di Graph tiene i numeri
    /// delle regole come un server: quattro regole, la terza in cima vuol dire tre PATCH, e il secondo risponde 500.
    /// </summary>
    static async Task RulesOrderAuditAsync()
    {
        var server = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["d"] = 4 };
        int patches = 0, failAt = 2, gets = 0;
        bool failGet = false;
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            if (request.Method == HttpMethod.Get && path == "me/mailFolders/inbox/messageRules")
            {
                gets++;
                if (failGet) return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":{\"code\":\"InternalServerError\",\"message\":\"prova\"}}", Encoding.UTF8, "application/json") };
                var value = new System.Text.Json.Nodes.JsonArray(server.Select(r => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                {
                    ["id"] = r.Key, ["displayName"] = "Regola " + r.Key, ["sequence"] = r.Value, ["isEnabled"] = true,
                    ["conditions"] = new System.Text.Json.Nodes.JsonObject { ["senderContains"] = new System.Text.Json.Nodes.JsonArray(r.Key + "@fornitori-prova.example") },
                    ["actions"] = new System.Text.Json.Nodes.JsonObject { ["markAsRead"] = true },
                }).ToArray());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new System.Text.Json.Nodes.JsonObject { ["value"] = value }.ToJsonString(), Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Patch)
            {
                if (++patches == failAt) return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":{\"code\":\"InternalServerError\",\"message\":\"prova\"}}", Encoding.UTF8, "application/json") };
                var body = System.Text.Json.Nodes.JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!;
                server[path[(path.LastIndexOf('/') + 1)..]] = (int)body["sequence"]!;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("", Encoding.UTF8, "application/json") };
        }));
        GraphProvider.TestSettingsConsent = true;
        try
        {
            var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = "organizations" });
            static string Order(IEnumerable<MailRule> rules) => string.Concat(rules.Select(r => r.ServerId));
            string OnServer() => string.Concat(server.OrderBy(r => r.Value).ThenBy(r => r.Key).Select(r => r.Key));

            // "c" dal terzo al secondo posto: due PATCH (c diventa 2, b diventa 3), il secondo fallisce: sul server c e b hanno tutti e due il 2.
            var rules = (await graph.GetRulesAsync())!;
            var (shown, problem) = await UI.RulesView.ReorderAsync(graph, rules, 2, 1);
            Check(patches == 2 && problem is { Heading: "Ordine delle regole cambiato solo in parte" } && problem.Value.Body.Contains("1 di 2") && gets == 2
                  && Order(shown) == string.Concat(shown.OrderBy(r => server[r.ServerId]).Select(r => r.ServerId)) && shown.All(r => r.Sequence == server[r.ServerId]),
                  $"regole di Outlook, Su con il secondo PATCH rifiutato: Missiva rilegge le regole, mostra i numeri che il server ha davvero ({string.Join(" ", shown.Select(r => r.ServerId + "=" + r.Sequence))}) e dice \"solo in parte, 1 di 2\"" +
                  $" [avviso: {problem?.Heading}]");

            // Il primo PATCH rifiutato: nulla è cambiato, e lo dice così.
            server = new() { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["d"] = 4 };
            (patches, failAt) = (0, 1);
            rules = (await graph.GetRulesAsync())!;
            (shown, problem) = await UI.RulesView.ReorderAsync(graph, rules, 2, 1);
            Check(problem is { Heading: "Ordine delle regole non cambiato" } && Order(shown) == "abcd" && OnServer() == "abcd",
                  "regole di Outlook, Su con il primo PATCH rifiutato: \"Ordine delle regole non cambiato\", ed è vero (abcd sul server e in Missiva)");

            // Tutto riuscito: nessun avviso, ordine nuovo.
            (patches, failAt) = (0, 99);
            (shown, problem) = await UI.RulesView.ReorderAsync(graph, rules, 2, 1);
            Check(problem == null && Order(shown) == "acbd" && OnServer() == "acbd" && patches == 2, "regole di Outlook, Su riuscito: due PATCH, nessun avviso, ordine acbd in Missiva e sul server");

            // A metà e senza poter rileggere: l'ordine che risulta dalle richieste riuscite, e l'avviso dice che può non essere quello vero.
            server = new() { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["d"] = 4 };
            rules = (await graph.GetRulesAsync())!;
            (patches, failAt, failGet) = (0, 2, true);
            (shown, problem) = await UI.RulesView.ReorderAsync(graph, rules, 3, 2);
            Check(problem is { Heading: "Ordine delle regole cambiato solo in parte" } && problem.Value.Body.Contains("non è riuscita a rileggere") && shown.Count == 4
                  && shown.Single(r => r.ServerId == "d").Sequence == 3,
                  "regole di Outlook, a metà e con la rilettura non riuscita: Missiva mostra quello che risulta dalle richieste riuscite e dice che può non essere l'ordine vero");
        }
        finally
        {
            GraphProvider.TestSettingsConsent = false;
        }
    }

    /// <summary>P-01: un pubblico esterno delle risposte automatiche che Missiva non conosce resta com'è, se nessuno lo cambia.</summary>
    static void AutoRepliesAuditTests()
    {
        static System.Text.Json.Nodes.JsonNode Setting(string? audience) => System.Text.Json.Nodes.JsonNode.Parse(
            "{\"status\":\"alwaysEnabled\",\"internalReplyMessage\":\"<p>Sono fuori sede.</p>\",\"externalReplyMessage\":\"<p>Fuori sede.</p>\"" +
            (audience == null ? "" : $",\"externalAudience\":\"{audience}\"") + "}")!;
        static string? Written(AutoReplies replies) => (string?)replies.ToGraph()["automaticRepliesSetting"]!["externalAudience"];
        var unknown = AutoReplies.FromGraph(Setting("partnersOnly"));
        var missing = AutoReplies.FromGraph(Setting(null));
        Check(Written(unknown) == "partnersOnly" && Written(unknown with { InternalHtml = "<p>Torno lunedì.</p>", State = AutoReplyState.Off }) == "partnersOnly",
              $"risposte automatiche: un pubblico esterno sconosciuto (\"partnersOnly\") salvato senza toccarlo resta com'è, non diventa \"all\" (scritto: {Written(unknown)})");
        Check(Written(unknown with { External = AutoReplyAudience.Contacts }) == "contactsOnly" && Written(unknown with { External = AutoReplyAudience.None }) == "none"
              && !((System.Text.Json.Nodes.JsonObject)missing.ToGraph()["automaticRepliesSetting"]!).ContainsKey("externalAudience")
              && new[] { "none", "contactsOnly", "all" }.All(a => Written(AutoReplies.FromGraph(Setting(a))) == a) && Written(new AutoReplies()) == "all",
              "risposte automatiche: scelto un altro pubblico vale quello; un pubblico che mancava non viene scritto; none, contactsOnly e all tornano uguali");
    }

    /// <summary>F-42 e 1-06: gli eventi di un giorno intero oltre il terzo, e la nota dell'annullamento che Google non ha.</summary>
    static void CalendarAuditTests()
    {
        Check(UI.CalendarView.MoreAfter(5, dayView: true) == 5 && UI.CalendarView.MoreAfter(5, dayView: false) == 3 && UI.CalendarView.MoreAfter(2, dayView: false) == 2,
              "calendario: nella vista Giorno si vedono tutti gli eventi di un giorno intero (5 su 5), nelle settimane tre e \"+2 altri\", che ora è un pulsante verso il giorno");
        var google = UI.CalendarView.CancelPrompt(AccountKind.Gmail, "Riunione di bilancio", 3);
        var microsoft = UI.CalendarView.CancelPrompt(AccountKind.Microsoft, "Riunione di bilancio", 3);
        Check(!google.AsksNote && google.Text.Contains("non permette di aggiungere una nota") && !google.Text.Contains("con questa nota")
              && microsoft.AsksNote && microsoft.Text.Contains("con questa nota"),
              "Annulla riunione: per Google nessuna casella della nota, e la conferma dice che Google non la manda; per Microsoft la nota resta");
    }

    /// <summary>F-44, punto 2: la finestra dell'errore d'invio propone il rimedio giusto e dice che il messaggio c'è ancora.</summary>
    static void SendFailureAuditTests()
    {
        var plain = UI.ComposeWindow.SendFailure(new MailException("Il server ha rifiutato il destinatario."));
        var password = UI.ComposeWindow.SendFailure(new MailException("Password rifiutata.") { Fix = AccountFix.Password }, "Dalla Posta in uscita: ");
        var signIn = UI.ComposeWindow.SendFailure(new AggregateException(new MailException("Accesso scaduto.") { Fix = AccountFix.SignIn }));
        var consent = UI.ComposeWindow.SendFailure(new MailException("Manca il consenso.") { Fix = AccountFix.Consent });
        Check(plain is { Primary: "Riprova", Fix: AccountFix.None } && plain.Body.StartsWith("Il server ha rifiutato il destinatario.") && plain.Body.Contains("Il messaggio è ancora qui")
              && password is { Primary: "Aggiorna password", Fix: AccountFix.Password } && password.Body.StartsWith("Dalla Posta in uscita: Password rifiutata.")
              && signIn is { Primary: "Accedi di nuovo", Fix: AccountFix.SignIn } && consent is { Primary: "Riprova", Fix: AccountFix.None },
              "errore d'invio: la finestra dice che il messaggio è ancora lì e propone Riprova, oppure Aggiorna password o Accedi di nuovo secondo l'errore (con Salva nelle Bozze e Torna al messaggio)");

        // F-44, punto 3: WebView2 che non parte. Il Runtime assente si distingue dagli altri errori.
        var missing = new Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException("prova");
        var other = new IOException("Spazio su disco insufficiente.");
        var (missingText, isMissing) = UI.ReadingView.WebErrorText(missing, UI.ReadingView.WebRuntimeMissing(missing));
        var (otherText, otherMissing) = UI.ReadingView.WebErrorText(other, UI.ReadingView.WebRuntimeMissing(other));
        Check(isMissing && missingText.Contains("WebView2 Runtime") && UI.ReadingView.WebRuntimeLink.StartsWith("https://go.microsoft.com/fwlink/")
              && !otherMissing && otherText.Contains("Spazio su disco insufficiente") && !otherText.Contains("Runtime"),
              "WebView2 che non parte: con il Runtime assente l'avviso lo dice, con il link di Microsoft; con un altro errore (su questo PC il Runtime c'è) dice l'errore vero, non del Runtime");
    }

    /// <summary>
    /// F-44, punto 4: quello che è cifrato con DPAPI e non si decifra più (profilo di Windows cambiato, impostazioni copiate
    /// da un altro PC). I testi delle impostazioni non devono diventare vuoti e perdersi al salvataggio dopo; le regole con
    /// parti non leggibili non devono agire; password e accesso degli account devono chiedere il rimedio giusto.
    /// </summary>
    static async Task UnreadableSecretsAuditAsync()
    {
        // Cifrati che non si aprono: uno vero con otto byte cambiati, uno a caso, uno che non è nemmeno base64.
        var real = Convert.FromBase64String(Store.Protect("Preventivo per Maria Rossi"));
        for (int i = real.Length / 2; i < real.Length / 2 + 8; i++) real[i] ^= 0x5A;
        string a = "dpapi:" + Convert.ToBase64String(real), b = "dpapi:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(120)), c = "dpapi:non è base64!";
        var json = $$"""
            {
              "Templates": [ { "Id": "m1", "Name": "{{a}}", "Subject": "{{b}}", "Rtf": "{{c}}" } ],
              "Rules": [ { "Id": "r1", "AccountId": "prova", "Name": "{{a}}", "FromAddresses": "{{b}}", "Delete": true },
                         { "Id": "r2", "AccountId": "prova", "Name": "A me", "SentToMe": true, "MoveTo": "{{b}}", "MoveToName": "{{a}}" } ],
              "Snoozed": [ { "AccountId": "prova", "Key": "k", "Subject": "{{a}}", "From": "{{b}}" } ],
              "RecentSearches": "{{b}}"
            }
            """;
        // Quante volte il valore compare fra i testi del JSON (letti, non cercati nel testo: il serializzatore scrive "+" come +).
        static int Times(string text, string value)
        {
            static int Count(System.Text.Json.JsonElement e, string value) => e.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => e.GetString() == value ? 1 : 0,
                System.Text.Json.JsonValueKind.Array => e.EnumerateArray().Sum(x => Count(x, value)),
                System.Text.Json.JsonValueKind.Object => e.EnumerateObject().Sum(x => Count(x.Value, value)),
                _ => 0,
            };
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            return Count(doc.RootElement, value);
        }
        var settings = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.Settings)!;
        var first = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJson.Default.Settings);
        ProtectedTextConverter.KeepOnlyWritten(); // come dopo ogni Store.Save
        var second = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize(first, SettingsJson.Default.Settings)!, SettingsJson.Default.Settings);
        Check(Times(first, a) == 4 && Times(first, b) == 5 && Times(first, c) == 1 && second == first,
              $"testi cifrati che non si decifrano più (modello, regole, posposto, ricerche): al salvataggio dopo restano nel file com'erano, anche dopo un secondo giro (trovati {Times(first, a)} di 4, {Times(first, b)} di 5, {Times(first, c)} di 1)");
        Check(settings.Templates[0].Name.Length > 0 && settings.Templates[0].Name == settings.Snoozed[0].Subject && settings.Templates[0].Name != settings.Templates[0].Subject,
              $"un testo cifrato che non si decifra non diventa vuoto: al suo posto \"{settings.Templates[0].Name}\", lo stesso per lo stesso cifrato");
        Check(ProtectedTextConverter.UnreadableCount == 3 && ProtectedTextConverter.IsUnreadable(settings.Templates[0].Name) && !ProtectedTextConverter.IsUnreadable("(testo cifrato non leggibile)")
              && UI.App.UnreadableNotice(3).Contains("3 testi cifrati") && settings.RecentSearches.Count == 1,
              "i cifrati che non si decifrano si contano (3 diversi) per l'avviso dell'avvio, che dice quanti sono e cosa fare");
        settings.RememberSearch("fattura");
        Check(settings.RecentSearches is ["fattura"], "ricerche recenti che non si decifrano: non si propongono, e alla prima ricerca nuova l'elenco riparte da quella");
        var account = new MailAccount { Id = "prova", Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer() };
        var mail = new MessageSummary { Id = "1", FolderId = "INBOX", Subject = "Fattura", From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" }, To = [new Address { Email = account.Email }] };
        Check(!Rules.Matches(settings.Rules[0], mail, null, account) && !Rules.Matches(settings.Rules[1], mail, null, account),
              "regole di Missiva con parole o cartelle che non si decifrano più: non agiscono su nessun messaggio (prima \"da ...: elimina\" restava senza condizioni e valeva per tutti)");

        // La password IMAP che non si decifra: Aggiorna password, non un errore di Windows.
        using (var fake = new FakeImap(new Flavor(Gmail: false)))
        {
            ImapProvider.TestPlain = true;
            try
            {
                var imapAccount = SpeedAccount(fake, "mario@studio-prova.example");
                imapAccount.Imap!.ProtectedPassword = a["dpapi:".Length..];
                Exception? error = null;
                using var imap = new ImapProvider(imapAccount);
                try { await imap.GetFoldersAsync(); }
                catch (Exception ex) { error = ex; }
                Check(error is MailException && MailException.FixOf(error!) == AccountFix.Password && error!.Message.Contains("password", StringComparison.OrdinalIgnoreCase),
                      $"password IMAP che non si decifra più: l'account chiede \"Aggiorna password\" ({error?.GetType().Name}: {error?.Message})");
            }
            finally
            {
                ImapProvider.TestPlain = false;
            }
        }

        // Il token di aggiornamento di Google e il segreto dell'app che non si decifrano: Accedi di nuovo, e l'app da reinserire.
        var (id, secret, token) = (Store.Settings.GoogleClientId, Store.Settings.ProtectedGoogleClientSecret, GoogleAuth.TestToken);
        try
        {
            GoogleAuth.TestToken = null;
            Store.Settings.GoogleClientId = "prova.apps.googleusercontent.example";
            Store.Settings.ProtectedGoogleClientSecret = Store.Protect("segreto di prova");
            var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "mario.prova@gmail.example", ProtectedRefreshToken = a["dpapi:".Length..] };
            Exception? error = null;
            try { await GoogleAuth.TokenAsync(gmail); }
            catch (Exception ex) { error = ex; }
            Check(error is MailException && MailException.FixOf(error!) == AccountFix.SignIn,
                  $"accesso a Google salvato che non si decifra più: l'account chiede \"Accedi di nuovo\" ({error?.GetType().Name}: {error?.Message})");
            Store.Settings.ProtectedGoogleClientSecret = a["dpapi:".Length..];
            gmail.ProtectedRefreshToken = Store.Protect("aggiornamento finto");
            error = null;
            try { await GoogleAuth.TokenAsync(gmail); }
            catch (Exception ex) { error = ex; }
            Check(error is MailException && error.Message.Contains("app Google"),
                  $"segreto dell'app Google che non si decifra più: detto in chiaro, con cosa fare ({error?.GetType().Name}: {error?.Message})");
        }
        finally
        {
            (Store.Settings.GoogleClientId, Store.Settings.ProtectedGoogleClientSecret, GoogleAuth.TestToken) = (id, secret, token);
        }
    }

    /// <summary>
    /// F-44, punto 1, gli altri dati dell'utente: contatti.dat e coda.dat che non si leggono vanno da parte e non vengono
    /// sovrascritti dal primo salvataggio dopo. Nella cartella dei dati della suite, rimessa com'era.
    /// </summary>
    static void DataFilesAuditTests()
    {
        var zeros = new byte[300];
        static string[] Aside(string name) => Directory.GetFiles(Store.Dir, name + "-illeggibile-*.dat");
        void Try(string name, Action reset, Action read, Action write, string what)
        {
            var file = Path.Combine(Store.Dir, name + ".dat");
            var before = File.Exists(file) ? File.ReadAllBytes(file) : null;
            foreach (var old in Aside(name)) File.Delete(old);
            try
            {
                File.WriteAllBytes(file, zeros);
                reset();
                read();
                var aside = Aside(name);
                bool moved = aside.Length == 1 && File.ReadAllBytes(aside[0]).SequenceEqual(zeros) && !File.Exists(file);
                write();
                Check(moved && File.Exists(file) && !File.ReadAllBytes(file).SequenceEqual(zeros) && File.ReadAllBytes(aside[0]).SequenceEqual(zeros) && !File.Exists(file + ".tmp"),
                      $"{name}.dat illeggibile (zeri): messo da parte uguale byte per byte, {what} non lo sovrascrive e scrive un file nuovo senza lasciare il temporaneo");
            }
            finally
            {
                foreach (var old in Aside(name)) File.Delete(old);
                if (before != null) File.WriteAllBytes(file, before);
                else File.Delete(file);
                reset();
            }
        }
        var contact = new Contact { DisplayName = "Maria Rossi", Emails = ["maria.rossi@studio-prova.example"] };
        Try("contatti", ContactBook.Reset, () => _ = ContactBook.Local.Count, () => ContactBook.Put(contact), "il primo contatto salvato dopo");
        var account = new MailAccount { Id = "coda-prova", Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer() };
        Try("coda", ActionQueue.Reset, () => ActionQueue.Count(account.Id),
            () => ActionQueue.Add(account, [new MessageSummary { Id = "1", FolderId = "INBOX" }], QueuedKind.Read), "la prima azione in coda dopo");
    }

    /// <summary>
    /// F-44, punto 1: impostazioni.json che non si legge (vuoto, troncato, JSON non valido, zeri, scritto da una versione più
    /// nuova, in uso da un altro programma). Ogni caso in una cartella temporanea sua: mai la cartella dei dati.
    /// </summary>
    static void StoreAuditTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "missiva-prova-impostazioni-" + Guid.NewGuid().ToString("N")[..8]);
        static Settings Sample(int accounts) => new()
        {
            Accounts = Enumerable.Range(1, accounts).Select(n => new MailAccount
            {
                Id = "prova" + n, Kind = AccountKind.Imap, Email = $"mario{n}@studio-prova.example", Imap = new ImapServer { ImapHost = "imap.studio-prova.example", ProtectedPassword = "x" },
            }).ToList(),
        };
        static byte[] Json(Settings s) => Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(s, SettingsJson.Default.Settings));
        string Folder(string name)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            return dir;
        }
        static bool Same(string? path, byte[] bytes) => path != null && File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes);
        try
        {
            var good = Json(Sample(2));
            (string Name, byte[] Bytes)[] broken =
            [
                ("vuoto", []),
                ("troncato a metà", good[..(good.Length / 2)]),
                ("JSON non valido", Encoding.UTF8.GetBytes("{ \"Accounts\": [ }")),
                ("tutto zeri", new byte[good.Length]),
                ("solo null", Encoding.UTF8.GetBytes("null")),
                ("un valore di una versione più nuova", Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(good).Replace("\"Theme\": \"System\"", "\"Theme\": \"Futuro\""))),
            ];

            // Primo avvio e giro normale.
            var fresh = Folder("primo-avvio");
            bool first = Store.Read(fresh, out var noRecovery).Accounts.Count == 0 && noRecovery == null && Directory.GetFiles(fresh).Length == 0;
            Check(first, "impostazioni: al primo avvio (cartella vuota) nessun avviso e nessun file creato dalla sola lettura");
            var normal = Folder("normale");
            bool wrote = Store.TryWrite(normal, Sample(1), null, out _) && Store.TryWrite(normal, Sample(2), null, out _);
            Check(wrote && Same(Path.Combine(normal, Store.FileName), good) && Same(Path.Combine(normal, Store.BackupName), Json(Sample(1)))
                  && !File.Exists(Path.Combine(normal, Store.FileName + ".tmp")) && Store.Read(normal, out var none).Accounts.Count == 2 && none == null,
                  "impostazioni: dopo due salvataggi il file è l'ultimo, impostazioni.bak il penultimo, nessun file temporaneo rimasto, e si rilegge senza avvisi");
            var old = Folder("senza-bak");
            File.WriteAllBytes(Path.Combine(old, Store.FileName), good);
            Check(Store.Read(old, out _).Accounts.Count == 2 && Same(Path.Combine(old, Store.BackupName), good),
                  "impostazioni scritte da una versione di prima (senza impostazioni.bak): alla prima lettura riuscita nasce la copia di riserva");

            // File illeggibile con la copia di riserva buona.
            var failed = new List<string>();
            foreach (var (name, bytes) in broken)
            {
                var dir = Folder("con-copia-" + Array.IndexOf(broken, (name, bytes)));
                File.WriteAllBytes(Path.Combine(dir, Store.FileName), bytes);
                File.WriteAllBytes(Path.Combine(dir, Store.BackupName), good);
                var read = Store.Read(dir, out var recovery);
                bool ok = read.Accounts.Count == 2 && recovery is { BackupDate: not null, Blocked: false } && Same(recovery.BrokenFile, bytes);
                read.Accounts.Add(Sample(3).Accounts[2]);
                ok = ok && Store.TryWrite(dir, read, recovery, out _) && Same(recovery!.BrokenFile, bytes)
                     && Store.Read(dir, out var again).Accounts.Count == 3 && again == null;
                if (!ok) failed.Add(name);
            }
            Check(failed.Count == 0, $"impostazioni.json illeggibile ({string.Join(", ", broken.Select(b => b.Name))}) con la copia di riserva: Missiva riparte dalla copia con i due account e lo dice, " +
                                     "il file rotto resta da parte uguale byte per byte anche dopo il primo salvataggio, e alla lettura dopo non c'è più nessun avviso" +
                                     (failed.Count > 0 ? $" [non passano: {string.Join(", ", failed)}]" : ""));

            // File illeggibile senza copia di riserva: si riparte da zero, ma il file non si sovrascrive.
            failed.Clear();
            foreach (var (name, bytes) in broken)
            {
                var dir = Folder("senza-copia-" + Array.IndexOf(broken, (name, bytes)));
                File.WriteAllBytes(Path.Combine(dir, Store.FileName), bytes);
                var read = Store.Read(dir, out var recovery);
                bool ok = read.Accounts.Count == 0 && recovery is { BackupDate: null, Blocked: false } && Same(recovery.BrokenFile, bytes);
                ok = ok && Store.TryWrite(dir, Sample(1), recovery, out _) && Same(recovery!.BrokenFile, bytes) && Store.Read(dir, out _).Accounts.Count == 1;
                if (!ok) failed.Add(name);
            }
            Check(failed.Count == 0, "impostazioni.json illeggibile senza copia di riserva (gli stessi sei casi): Missiva riparte senza account e lo dice, il file rotto resta da parte " +
                                     "uguale byte per byte e il primo salvataggio non lo sovrascrive" + (failed.Count > 0 ? $" [non passano: {string.Join(", ", failed)}]" : ""));

            // Rotti tutti e due.
            var both = Folder("tutti-e-due");
            File.WriteAllBytes(Path.Combine(both, Store.FileName), broken[3].Bytes);
            File.WriteAllBytes(Path.Combine(both, Store.BackupName), broken[1].Bytes);
            var empty = Store.Read(both, out var bothRecovery);
            Check(empty.Accounts.Count == 0 && bothRecovery is { BackupDate: null } && Same(bothRecovery.BrokenFile, broken[3].Bytes) && Same(bothRecovery.BrokenBackup, broken[1].Bytes)
                  && Store.TryWrite(both, Sample(1), bothRecovery, out _) && Store.TryWrite(both, Sample(2), bothRecovery, out _)
                  && Same(bothRecovery.BrokenFile, broken[3].Bytes) && Same(bothRecovery.BrokenBackup, broken[1].Bytes),
                  "impostazioni.json e impostazioni.bak illeggibili tutti e due: messi da parte tutti e due, uguali anche dopo due salvataggi");

            // Il file non c'è più, la copia sì.
            var gone = Folder("sparito");
            File.WriteAllBytes(Path.Combine(gone, Store.BackupName), good);
            Check(Store.Read(gone, out var goneRecovery).Accounts.Count == 2 && goneRecovery is { BrokenFile: null, BackupDate: not null } && Same(Path.Combine(gone, Store.FileName), good)
                  && Store.Read(gone, out var goneAgain).Accounts.Count == 2 && goneAgain == null,
                  "impostazioni.json sparito con la copia di riserva al suo posto: Missiva riparte dalla copia, lo dice una volta e rimette il file");

            // In uso da un altro programma: non si legge e non si sposta. Nessun salvataggio lo sovrascrive.
            var locked = Folder("in-uso");
            var lockedFile = Path.Combine(locked, Store.FileName);
            File.WriteAllBytes(lockedFile, good);
            File.WriteAllBytes(Path.Combine(locked, Store.BackupName), Json(Sample(1)));
            bool lockedOk;
            using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var read = Store.Read(locked, out var lockedRecovery);
                lockedOk = read.Accounts.Count == 1 && lockedRecovery is { Blocked: true, BrokenFile: null } && !Store.TryWrite(locked, Sample(3), lockedRecovery, out _);
            }
            Check(lockedOk && Same(lockedFile, good) && Store.Read(locked, out var unlocked).Accounts.Count == 2 && unlocked == null,
                  "impostazioni.json in uso da un altro programma (non si legge e non si sposta): Missiva riparte dalla copia, in quella sessione non salva, il file resta com'era e all'avvio dopo si legge");

            // Un salvataggio che non riesce non solleva eccezioni.
            var stuck = Folder("non-si-scrive");
            Directory.CreateDirectory(Path.Combine(stuck, Store.FileName)); // una cartella al posto del file: la rinomina non può riuscire
            bool threw = false, saved = true;
            Exception? error = null;
            try { saved = Store.TryWrite(stuck, Sample(1), null, out error); }
            catch { threw = true; }
            Check(!threw && !saved && error != null, $"un salvataggio delle impostazioni che non riesce non solleva eccezioni: dice di no e perché ({error?.GetType().Name})");

            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) Store.TryWrite(normal, Sample(2 + i % 2), null, out _);
            Report.Add($"      (20 salvataggi delle impostazioni con lo svuotamento su disco: {clock.ElapsedMilliseconds / 20.0:0.0} ms l'uno)");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>
    /// Audit, blocco 4: la velocità di tutti i giorni (docs\audit\velocita.md e velocita-2.md), contando le operazioni sui
    /// server finti. Con "Missiva.exe --prova-audit risultato.txt velocita" solo queste.
    /// </summary>
    static void SpeedAuditTests()
    {
        Report.Add("--- Audit, blocco 4: velocità di tutti i giorni, contando le operazioni");
        KnownCacheAuditAsync().GetAwaiter().GetResult();
        ReadCountAuditTests();
        ImapDraftAuditAsync().GetAwaiter().GetResult();
        NextRowAuditTests();
        // Il server finto nasce qui, fuori dal thread "dell'interfaccia" della prova: lì dentro risponderebbe su quel thread,
        // e la chiusura del provider (un LOGOUT che aspetta la risposta) resterebbe ferma fino allo scadere dei 60 s di MailKit.
        using (var fake = new FakeImap(new Flavor(Gmail: false))) OnUiThread(() => RulesTextAuditAsync(fake));
        ArrivalGroupsAuditTests();
    }

    /// <summary>
    /// Audit, blocco 8: velocità, secondo gruppo (thread dell'interfaccia e disco). Tutto in cartelle temporanee e con dati
    /// finti. Con "Missiva.exe --prova-audit risultato.txt velocita2" solo queste.
    /// </summary>
    static void Speed2AuditTests()
    {
        Report.Add("--- Audit, blocco 8: velocità, secondo gruppo (interfaccia e disco)");
        OnUiThread(SaveSoonAuditAsync);
        BodyCacheAuditTests();
        PeopleSearchAuditTests();
        SuggestIndexAuditTests();
        OfflineSearchAuditTests();
        SuggestReuseAuditTests();
        PdfStartAuditTests();
        ProviderWorkMeasure();
    }

    /// <summary>
    /// Audit, blocco 9: velocità, terzo gruppo (rete e disegno). Server finti sul PC e dati finti. Con
    /// "Missiva.exe --prova-audit risultato.txt velocita3" solo queste.
    /// </summary>
    static void Speed3AuditTests()
    {
        Report.Add("--- Audit, blocco 9: velocità, terzo gruppo (rete e disegno)");
        ApiHandlerAuditAsync().GetAwaiter().GetResult();
        MailboxGateAuditAsync().GetAwaiter().GetResult();
        GraphFoldersAuditAsync().GetAwaiter().GetResult();
        NotifyFirstAuditAsync().GetAwaiter().GetResult();
        ReplySourceAuditAsync(gmail: false).GetAwaiter().GetResult();
        ReplySourceAuditAsync(gmail: true).GetAwaiter().GetResult();
        NewMailCommandsAuditAsync(gmail: false).GetAwaiter().GetResult();
        NewMailCommandsAuditAsync(gmail: true).GetAwaiter().GetResult();
        OldCopyUnsubscribeAuditAsync(gmail: false).GetAwaiter().GetResult();
        OldCopyUnsubscribeAuditAsync(gmail: true).GetAwaiter().GetResult();
        SendBarAuditTests();
        OnUiThread(SnoozeSavesAuditAsync);
    }

    /// <summary>
    /// Dopo l'installazione: i due difetti segnalati dall'autore il 7 ottobre 2026 (il numero dei non letti che resta alto con
    /// tutti i messaggi letti, l'elenco che si aggiorna dopo la notifica). Server IMAP finto e dati finti. Con
    /// "Missiva.exe --prova-audit risultato.txt arrivi" solo queste.
    /// </summary>
    static void ArrivalsAuditTests()
    {
        Report.Add("--- Dopo l'installazione: non letti ed elenco dopo un arrivo (segnalazioni dell'autore, 7 ottobre 2026)");
        FreshFirstPageAuditAsync(gmail: false).GetAwaiter().GetResult();
        FreshFirstPageAuditAsync(gmail: true).GetAwaiter().GetResult();
        SavedPageAuditTests();
        UnreadSyncAuditAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// L'elenco riletto dopo un arrivo. La connessione principale resta ferma sulla cartella aperta e la conta com'era
    /// all'ultimo annuncio del server (qui, come nei server veri, i messaggi arrivati dopo si annunciano con NOOP): la prima
    /// pagina usciva senza l'ultimo arrivato. Con la cartella appena aperta (SELECT) il NOOP non serve e non si manda, e
    /// nemmeno per le pagine dopo.
    /// </summary>
    static async Task FreshFirstPageAuditAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail)) { AnnounceOnNoop = true };
        ImapProvider.TestPlain = true;
        try
        {
            for (int k = 1; k <= 60; k++) fake.Add("INBOX", SpeedMail("Pratica " + k, "Testo della pratica " + k, daysAgo: 70 - k), "\\Seen");
            var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
            using var closing = provider as IDisposable;
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            // Solo i comandi della connessione principale sulla Posta in arrivo (Gmail intanto prepara quella delle conversazioni).
            static string Shape(IEnumerable<string> commands) => string.Join(", ", commands
                .Where(c => c.StartsWith("NOOP") || c.Contains("FETCH") || c.StartsWith("SELECT") && c.Contains("INBOX", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.StartsWith("NOOP") ? "NOOP" : c.StartsWith("SELECT") ? "SELECT" : "FETCH"));
            fake.Commands.Clear();
            var opened = await provider.GetMessagesAsync(inbox, null, null);            // la cartella si apre ora: già aggiornata
            await Task.Delay(300);
            string openedCommands = Shape(fake.Commands.ToList());
            // Arriva un messaggio: la connessione principale non lo sa finché il server non lo annuncia.
            fake.Add("INBOX", SpeedMail("Preventivo appena arrivato", "In allegato il preventivo"));
            fake.Commands.Clear();
            var again = await provider.GetMessagesAsync(inbox, null, null);             // la stessa cartella riletta
            string againCommands = Shape(fake.Commands.ToList());
            fake.Commands.Clear();
            var older = await provider.GetMessagesAsync(inbox, null, again.Next);        // la pagina dopo, scorrendo
            string olderCommands = Shape(fake.Commands.ToList());
            Report.Add($"      {(gmail ? "Gmail" : "IMAP")}, cartella appena aperta: {openedCommands}; riletta: {againCommands}; pagina dopo: {olderCommands}");
            Check(opened.Messages.Count == 50 && !opened.Messages.Any(m => m.Subject.StartsWith("Preventivo"))
                  && again.Messages.Count == 50 && again.Messages[0].Subject == "Preventivo appena arrivato" && !again.Messages[0].IsRead
                  && older.Messages.Count == 11 && !older.Messages.Any(m => m.Subject.StartsWith("Preventivo"))
                  && !openedCommands.Contains("NOOP") && againCommands == "NOOP, FETCH" && !olderCommands.Contains("NOOP"),
                  $"elenco riletto dopo un arrivo con {(gmail ? "Gmail" : "IMAP")}: la prima pagina ha in cima il messaggio appena arrivato (prima usciva senza: la connessione contava la cartella com'era); " +
                  $"un NOOP solo se la cartella era già aperta ({againCommands}), nessuno appena aperta né per le pagine dopo");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
        }
    }

    /// <summary>
    /// La copia locale della prima pagina dopo un arrivo (MailCache.Prepend): la Posta in arrivo aperta dopo mostra subito i
    /// nuovi in cima, senza doppioni; una cartella mai aperta resta senza copia.
    /// </summary>
    static void SavedPageAuditTests()
    {
        var account = new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer() };
        static MessageSummary Mail(string id, double hoursAgo) => new()
        {
            Id = id, FolderId = "INBOX", Subject = "Pratica " + id, Date = DateTime.Now.AddHours(-hoursAgo),
            From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" },
        };
        try
        {
            MailCache.SaveMessages(account, "INBOX", null, [Mail("3", 3), Mail("2", 20), Mail("1", 50)]);
            int added = MailCache.Prepend(account, "INBOX", null, [Mail("4", 1), Mail("5", 0.5), Mail("3", 3)]);
            string saved = string.Join(",", (MailCache.LoadMessages(account, "INBOX", null) ?? []).Select(m => m.Id));
            int again = MailCache.Prepend(account, "INBOX", null, [Mail("5", 0.5)]);
            int never = MailCache.Prepend(account, "Clienti", null, [Mail("9", 0.2)]);
            Check(added == 2 && saved == "5,4,3,2,1" && again == 0 && never == 0 && MailCache.LoadMessages(account, "Clienti", null) == null,
                  $"copia locale dopo un arrivo: i due nuovi in cima alla prima pagina salvata, dal più recente, senza doppioni ({saved}); già presenti non rientrano; una cartella mai aperta resta senza copia");
        }
        finally
        {
            MailCache.Delete(account);
        }
    }

    /// <summary>
    /// I non letti contati dal server e le modifiche fatte qui (UnreadSync). Un conteggio chiesto prima di una lettura e
    /// tornato dopo non si scrive; lo stesso con una scrittura di Missiva in viaggio, e per le cartelle rilette; finita la
    /// scrittura Written avvisa (da lì il riconteggio), anche per le scritture di Gmail e IMAP (InboxIdle.OwnAsync).
    /// </summary>
    static async Task UnreadSyncAuditAsync()
    {
        var settle = UnreadSync.Settle;
        var written = new List<string>();
        void OnWritten(string id) { lock (written) written.Add(id); }
        UnreadSync.Written += OnWritten;
        try
        {
            // L'attesa dopo una scrittura (Settle) qui non si aspetta: vale "non ancora passata" (un'ora) o "passata" (zero),
            // secondo il punto. Prima era di 150 ms, con il conto da fare entro quel tempo e una pausa di 250 ms dopo: sotto
            // carico il controllo poteva arrivare tardi (audit dell'11 ottobre, C-42).
            UnreadSync.Settle = TimeSpan.Zero;
            var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer() }));
            var id = account.Provider.Account.Id;
            static MailFolder Folder(string name, FolderKind kind, int unread) => new() { Id = name, Name = name, Kind = kind, Unread = unread };
            account.SetFolders(new[] { Folder("INBOX", FolderKind.Inbox, 2), Folder("Clienti", FolderKind.Other, 4) });
            var inbox = account.Folders.First(f => f.Folder.Kind == FolderKind.Inbox);
            var other = account.Folders.First(f => f.Folder.Id == "Clienti");

            // 1. Il controllo chiede il conteggio (2 da leggere), poi qui si legge un messaggio, poi torna il 2 di prima.
            var asked = UnreadSync.Ticket(id);
            inbox.SetUnread(inbox.Unread - 1, "letto nel riquadro");
            bool stale = !inbox.ApplyServerUnread(2, asked, "contati dal server") && inbox.Unread == 1;
            // Chiesto dopo, senza altre modifiche: vale. Una lettura in un'altra cartella non tocca il conteggio della Posta in arrivo.
            var fresh = UnreadSync.Ticket(id);
            other.SetUnread(other.Unread - 1, "letto nel riquadro");
            bool applied = fresh != null && inbox.ApplyServerUnread(1, fresh, "contati dal server") && inbox.Unread == 1;

            // 2. Una scrittura di Missiva in viaggio (il segno di lettura non è ancora sul server): non si chiede nemmeno, e un
            // numero chiesto prima non vale né durante né dopo; finita, dopo un attimo, si riconta.
            var before = UnreadSync.Ticket(id);
            var server = new TaskCompletionSource();
            inbox.SetUnread(inbox.Unread - 1, "letto nel riquadro");
            var write = InboxIdle.OwnAsync(id, () => server.Task);
            bool busy = UnreadSync.Ticket(id) == null && !inbox.ApplyServerUnread(1, before, "contati dal server") && inbox.Unread == 0;
            int during;
            lock (written) during = written.Count;
            UnreadSync.Settle = TimeSpan.FromHours(1);
            server.SetResult();
            await write;
            bool justDone = UnreadSync.Ticket(id) == null && !UnreadSync.Valid(id, before);
            UnreadSync.Settle = TimeSpan.Zero;
            var after = UnreadSync.Ticket(id);
            bool recounted = after != null && inbox.ApplyServerUnread(0, after, "ricontati dal server") && inbox.Unread == 0;
            int told;
            lock (written) told = written.Count(w => w == id);

            // 3. Un arrivo stimato (i non letti di prima più i nuovi) supera il conteggio chiesto prima dell'arrivo.
            var early = UnreadSync.Ticket(id);
            inbox.SetUnread(inbox.Unread + 1, "1 arrivati");
            bool arrival = !inbox.ApplyServerUnread(0, early, "contati dal server") && inbox.Unread == 1;

            // 4. Le cartelle rilette: i non letti della Posta in arrivo cambiati mentre arrivavano restano quelli di qui.
            var listing = UnreadSync.Ticket(id);
            inbox.SetUnread(inbox.Unread - 1, "letto nel riquadro");
            bool kept = !account.SetServerFolders([Folder("INBOX", FolderKind.Inbox, 1), Folder("Clienti", FolderKind.Other, 7)], listing)
                        && inbox.Unread == 0 && other.Unread == 7;
            bool taken = account.SetServerFolders([Folder("INBOX", FolderKind.Inbox, 3), Folder("Clienti", FolderKind.Other, 7)], UnreadSync.Ticket(id)) && inbox.Unread == 3;

            // 5. Una scrittura non riuscita avvisa lo stesso (il numero si riconta), e l'errore arriva a chi l'ha chiesta.
            bool failed = false;
            try { await UnreadSync.WriteAsync(id, () => Task.FromException(new MailException("prova"))); }
            catch (MailException) { failed = true; }
            int toldAfterFailure;
            lock (written) toldAfterFailure = written.Count(w => w == id);

            Check(stale && applied && busy && during == 0 && justDone && recounted && told == 1 && arrival && kept && taken && failed && toldAfterFailure == 2,
                  $"non letti: un conteggio del server chiesto prima di una lettura fatta qui non si scrive ({stale}: prima rimetteva il numero vecchio), chiesto dopo sì ({applied}); con una scrittura di Missiva in viaggio " +
                  $"non si chiede e non si scrive ({busy}), nemmeno appena finita ({justDone}), e dopo un attimo si riconta ({recounted}); la fine di ogni scrittura avvisa una volta, anche se non riesce " +
                  $"({told}, poi {toldAfterFailure}); un arrivo supera il conteggio di prima ({arrival}); le cartelle rilette non riportano indietro i non letti cambiati intanto ({kept}) e altrimenti valgono ({taken})");
        }
        finally
        {
            UnreadSync.Settle = settle;
            UnreadSync.Written -= OnWritten;
        }
    }

    /// <summary>
    /// Trovato dal blocco 8: Posponi salvava le impostazioni subito a ogni messaggio (12-26 ms l'uno sul thread
    /// dell'interfaccia). Ora, come lo fanno MainWindow.SnoozeAsync e CheckSnoozedAsync: SaveSoon a ogni messaggio e un
    /// salvataggio subito alla fine. Qui lo stesso schema su un SettingsSaver suo, in una cartella temporanea, con i
    /// posposti che crescono mentre "il server" risponde.
    /// </summary>
    static async Task SnoozeSavesAuditAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-posponi-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var settings = SampleSettings(false);
            StoreRecovery? recovery = null;
            var saver = new SettingsSaver(dir, () => settings, () => recovery) { Ui = System.Windows.Threading.Dispatcher.CurrentDispatcher };
            saver.Now();
            int ui = Environment.CurrentManagedThreadId, start = settings.Snoozed.Count;
            async Task<(int Writes, int OnUi, double UiMs)> SnoozeAsync(int messages, int serverMs, bool each)
            {
                int writes = saver.Writes, onUi = 0;
                var busy = new Stopwatch();
                for (int i = 0; i < messages; i++)
                {
                    await Task.Delay(serverMs); // lo spostamento sul server
                    settings.Snoozed.Add(new SnoozedMessage { AccountId = "a", Key = "k" + settings.Snoozed.Count, FolderId = "f", Until = DateTime.Today.AddDays(1), Subject = "Posposto", From = "Mario Rossi" });
                    busy.Start();
                    if (each) saver.Now();
                    else saver.Soon();
                    busy.Stop();
                    if (each && saver.LastWriteThread == ui) onUi++;
                }
                busy.Start();
                if (!each)
                {
                    saver.Now();
                    if (saver.LastWriteThread == ui) onUi++;
                }
                busy.Stop();
                return (saver.Writes - writes, onUi, busy.Elapsed.TotalMilliseconds);
            }
            var before = await SnoozeAsync(10, 20, each: true);
            var after = await SnoozeAsync(10, 20, each: false);
            bool saved = Store.Read(dir, out _).Snoozed.Count == start + 20;
            await Task.Delay(SettingsSaver.Delay + TimeSpan.FromMilliseconds(300)); // il salvataggio accorpato rimasto in programma non ha più nulla da scrivere
            int late = saver.Writes;
            // Un Posponi lungo (trenta messaggi, 3 s): intanto qualche scrittura in sottofondo, e alla fine tutto sul disco.
            var slow = await SnoozeAsync(30, 100, each: false);
            Check(before.Writes == 10 && before.OnUi == 10 && after.Writes == 1 && after.OnUi == 1 && saved && late == saver.Writes - slow.Writes
                  && slow.OnUi == 1 && slow.Writes is >= 2 and <= 6 && Store.Read(dir, out _).Snoozed.Count == start + 50,
                  $"Posponi di dieci messaggi: prima {before.Writes} salvataggi sul thread dell'interfaccia ({before.UiMs:0} ms in tutto), ora {after.Writes} ({after.UiMs:0} ms), con tutti i posposti sul disco alla fine; " +
                  $"trenta messaggi in 3 s: {slow.Writes} scritture, una sola sul thread dell'interfaccia (le altre accorpate, in sottofondo, al più ogni 2 s)");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// Segnalato dall'autore il 4 ottobre 2026 (allegati grandi con Microsoft: "non c'è nessuna parte che indichi l'avanzamento"):
    /// con Annulla invio la scrittura è nascosta, e l'avanzamento va nella barra "Invio in corso" della finestra principale.
    /// Qui il testo della barra; la barra, il suo Annulla e la scrittura che si riapre si vedono con MISSIVA_CARICAMENTO_FINTO.
    /// </summary>
    static void SendBarAuditTests()
    {
        const string progress = "Caricamento di relazione-prova.zip: 3,8 di 24 MB…"; // com'è scritto da GraphProvider.UploadLargeAsync
        static string Bar(params (string, string?)[] sends) => UI.MainWindow.SendBarText(sends);
        Check(Bar() == "" && Bar(("Relazione di settembre", null)) == "Invio in corso: Relazione di settembre" && Bar(("", null)) == "Invio in corso…"
              && Bar(("Relazione di settembre", progress)) == "Invio in corso: 3,8 di 24 MB caricati (relazione-prova.zip)"
              && Bar(("Relazione", "Caricamento di catalogo-prova.pdf: 0 di 3 MB…")) == "Invio in corso: 0 di 3 MB caricati (catalogo-prova.pdf)"
              && Bar(("Preventivo", null), ("Relazione di settembre", progress)) == "Invio di 2 messaggi in corso: 3,8 di 24 MB caricati (relazione-prova.zip)"
              && Bar(("Preventivo", null), ("Relazione", null)) == "Invio di 2 messaggi in corso…" && Bar(("Relazione", "Altro avviso")) == "Invio in corso: Altro avviso"
              && UI.MainWindow.UploadShort("Caricamento di Relazione finale: bozza 3.pdf: 11,3 di 142 MB…") == "11,3 di 142 MB caricati (Relazione finale: bozza 3.pdf)",
              $"allegati grandi, barra degli invii della finestra principale: durante il caricamento dice i MB caricati sul totale e il file, con i numeri per primi (\"{Bar(("Relazione di settembre", progress))}\"); " +
              "senza caricamento resta \"Invio in corso: oggetto\"; lo stesso testo corto nella riga di stato della scrittura, anche salvando una bozza");
    }

    /// <summary>
    /// Segnalato dall'autore il 4 ottobre 2026 ("non c'è annulla iscrizione"): Gmail e IMAP aprono i messaggi dalla copia su disco,
    /// e le copie fatte prima dell'audit, blocco 6, non hanno List-Unsubscribe. Qui una newsletter sul server IMAP finto e la
    /// sua copia fatta come allora.
    /// </summary>
    static async Task OldCopyUnsubscribeAuditAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        ImapProvider.TestPlain = true;
        string name = gmail ? "Gmail" : "IMAP";
        var news = SpeedMail("Notizie della settimana", "Le novita' della settimana");
        news.From.Clear();
        news.From.Add(new MimeKit.MailboxAddress("Notizie di prova", "notizie@" + NewsDomain));
        news.Headers.Add("Authentication-Results", (gmail ? "mx.google.com" : "studio-prova.example") + "; dkim=pass header.i=@" + NewsDomain + "; dmarc=pass header.from=" + NewsDomain);
        news.Headers.Add("List-Unsubscribe", $"<{NewsHttps}>, <{NewsMailto}>");
        news.Headers.Add("List-Unsubscribe-Post", OneClick);
        fake.Add("INBOX", news);
        fake.Add("INBOX", SpeedMail("Preventivo", "Ecco il preventivo"));
        var provider = FakeProvider(fake, gmail, gmail ? "mario.prova@gmail.example" : "mario@studio-prova.example");
        var account = provider.Account;
        MailCache.Delete(account);
        BodyCache.Delete(account);
        static void Written()
        {
            Task[] writes;
            lock (BodyCache.TestWrites) writes = [.. BodyCache.TestWrites];
            Task.WaitAll(writes);
        }
        try
        {
            using var closing = provider as IDisposable;
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var rows = (await provider.GetMessagesAsync(inbox, null, null)).Messages;
            var letter = rows.First(m => m.Subject == "Notizie della settimana");
            var plain = rows.First(m => m.Subject == "Preventivo");
            var fresh = await provider.GetBodyAsync(letter);
            var wanted = Unsubscribe.For(account, letter, fresh, false);
            bool inMemory = provider.KeptBody(letter) is { ListHeadersUnread: false } kept && Unsubscribe.For(account, letter, kept, false) == wanted;
            // Le copie com'erano prima: senza le due intestazioni e senza dire se sono state lette.
            BodyCache.Save(account, letter, new MessageBody { Html = fresh.Html, To = fresh.To, AuthenticationResults = fresh.AuthenticationResults, ListHeadersUnread = true });
            BodyCache.Save(account, plain, new MessageBody { Html = "<p>Ecco il preventivo</p>", ListHeadersUnread = true });
            Written();
            var old = provider.SavedBody(letter);
            var oldPlain = provider.SavedBody(plain);
            bool absent = old is { ListHeadersUnread: true } && Unsubscribe.For(account, letter, old, false) == null && oldPlain is { ListHeadersUnread: true };

            // Come fa il riquadro (ReadingView.FillListHeadersAsync): le sole intestazioni dal server, nel corpo e nella copia.
            fake.Commands.Clear();
            var read = await provider.GetListHeadersAsync(letter);
            var readPlain = await provider.GetListHeadersAsync(plain);
            var fetches = fake.Commands.Where(c => c.Contains("FETCH")).ToList();
            bool light = fetches.Count == 2 && fetches.All(c => c.Contains("HEADER.FIELDS") && c.Contains("LIST-UNSUBSCRIBE", StringComparison.OrdinalIgnoreCase)
                                                             && !c.Contains("BODY.PEEK[]") && !c.Contains("BODY.PEEK[1") && !c.Contains("BODY.PEEK[TEXT"));
            old!.ListUnsubscribe = read?.Unsubscribe;
            old.ListUnsubscribePost = read?.Post;
            old.ListHeadersUnread = false;
            await BodyCache.SetListHeaders(account, letter, read!);
            await BodyCache.SetListHeaders(account, plain, readPlain!);
            var again = provider.SavedBody(letter);
            var againPlain = provider.SavedBody(plain);
            bool filled = Unsubscribe.For(account, letter, old, false) == wanted && again is { ListHeadersUnread: false } && Unsubscribe.For(account, letter, again, false) == wanted
                          && again.Html == fresh.Html;
            bool known = readPlain is { Unsubscribe: null, Post: null } && againPlain is { ListHeadersUnread: false, ListUnsubscribe: null, Html: "<p>Ecco il preventivo</p>" };
            // Un messaggio che non c'è più: non si sa, e la copia resta com'è. Una copia fatta oggi dal corpo del server sa già tutto.
            var gone = await provider.GetListHeadersAsync(new MessageSummary { Id = "9999", FolderId = letter.FolderId, StableId = "sparito" });
            BodyCache.Forget(account, [plain]);
            BodyCache.Save(account, plain, await provider.GetBodyAsync(plain));
            Written();
            bool today = provider.SavedBody(plain) is { ListHeadersUnread: false };
            // La riga che entra nella pagina già mostrata è la stessa che la pagina avrebbe avuto.
            var entry = new UI.PageEntry(letter, old, true, false, null, false, Unsubscribe: "");
            var page = UI.ReadingPage.Build([entry], true);
            var row = page.UnsubscribeRow(0, "");
            bool sameRow = page.Html.Contains(row) && page.UnsubscribeScript(0, "").Contains("insertAdjacentHTML") && row.Contains("Annulla iscrizione") && row.Contains("/unsubscribe/0")
                           && ReferenceEquals(page.Bodies[0], old);
            // Dall'audit dell'11 ottobre (T-03) su un account IMAP l'intestazione con il nome del server non prova che il
            // messaggio è autenticato (può averla scritta chi manda): niente clic singolo, resta il messaggio da inviare.
            Check(wanted is { } command && command.Kind == (gmail ? UnsubscribeKind.OneClick : UnsubscribeKind.Mailto) && inMemory && absent && light && filled && known && gone == null && today && sameRow,
                  $"annulla iscrizione, {name}, messaggio aperto da una copia su disco fatta prima che Missiva leggesse List-Unsubscribe: la copia dice che non le ha mai lette e il comando non c'è ({absent}, " +
                  $"com'era per l'autore); le sole intestazioni si chiedono al server ({fetches.Count} FETCH per due messaggi, senza corpo: {light}) e il comando compare, lo stesso del server ({filled}); " +
                  $"la copia si aggiorna e non si chiedono più, nemmeno per il messaggio che non le ha ({known}); un messaggio sparito lascia tutto com'è; una copia fatta oggi le ha già ({today}); " +
                  $"il corpo in memoria le ha ({inMemory}); la riga aggiunta alla pagina mostrata è quella della pagina ({sameRow})");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
            MailCache.Delete(account);
            BodyCache.Delete(account);
        }
    }

    /// <summary>Un provider IMAP o Gmail sul server finto, per le prove che valgono per tutti e due.</summary>
    static IMailProvider FakeProvider(FakeImap fake, bool gmail, string email)
    {
        if (!gmail) return new ImapProvider(SpeedAccount(fake, email));
        GmailProvider.TestPort = fake.Port;
        return new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = email, DisplayName = "Mario Rossi" });
    }

    /// <summary>
    /// v2 V-28: una risposta con Gmail e IMAP. A ogni salvataggio della bozza l'originale si rileggeva dal server (Message-Id
    /// e References) e il testo della citazione si ricalcolava. Qui tre salvataggi della stessa risposta sul server IMAP
    /// finto: una lettura sola, e la bozza resta com'era (In-Reply-To, References, citazione).
    /// </summary>
    static async Task ReplySourceAuditAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        ImapProvider.TestPlain = true;
        try
        {
            var mail = SpeedMail("Preventivo per il bagno", "Buongiorno, ecco il preventivo.<br>Cordiali saluti<br>Maria");
            mail.MessageId = "preventivo-7@fornitori-prova.example";
            mail.References.Add("richiesta-1@studio-prova.example");
            mail.References.Add("risposta-2@fornitori-prova.example");
            fake.Add("INBOX", mail);
            var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
            using var closing = provider as IDisposable;
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var original = (await provider.GetMessagesAsync(inbox, null, null)).Messages.Single();
            var body = await provider.GetBodyAsync(original); // come la finestra di scrittura: letto una volta, passato a ogni salvataggio
            OutgoingMessage Reply(string text, SavedDraft? draft) => new()
            {
                Mode = ComposeMode.Reply, Original = original, OriginalBody = body, To = To, Subject = "R: Preventivo per il bagno",
                Html = $"<p>{text}</p>", Text = text, Draft = draft,
            };
            fake.Commands.Clear();
            long before = MimeOptions.QuoteRuns;
            var saved = await provider.SaveDraftAsync(Reply("Grazie", null));
            saved = await provider.SaveDraftAsync(Reply("Grazie, va bene", saved));
            saved = await provider.SaveDraftAsync(Reply("Grazie, va bene così", saved));
            int reads = fake.Commands.Count(c => c.StartsWith("UID FETCH") && c.Contains("REFERENCES", StringComparison.OrdinalIgnoreCase));
            long texts = MimeOptions.QuoteRuns - before;
            var draft = MimeKit.MimeMessage.Load(new MemoryStream(fake.Stored.Single().Raw));
            bool same = draft.InReplyTo == "preventivo-7@fornitori-prova.example"
                        && draft.References.SequenceEqual(["richiesta-1@studio-prova.example", "risposta-2@fornitori-prova.example", "preventivo-7@fornitori-prova.example"])
                        && draft.HtmlBody?.Contains("va bene così") == true && draft.HtmlBody.Contains("<blockquote") && draft.HtmlBody.Contains("ecco il preventivo")
                        && draft.TextBody?.Replace("\r", "").Contains("> Buongiorno, ecco il preventivo.\n> Cordiali saluti\n> Maria") == true;
            Check(reads == 1 && texts == 1 && same,
                  $"V-28, risposta con {(gmail ? "Gmail" : "IMAP")}, tre salvataggi della bozza: l'originale letto dal server {reads} volta (prima 3) e il testo della citazione calcolato {texts} volta (prima 3); " +
                  $"la bozza ha In-Reply-To, i tre References e la citazione, come prima ({same})");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
        }
    }

    /// <summary>
    /// v2 V-26: la fine del controllo dei nuovi messaggi (App.ShowThenCountAsync). La notifica e le righe nuove arrivano
    /// prima che il conteggio dei non letti sia chiesto al server, anche se il conteggio non torna mai o non riesce; quando
    /// torna corregge il numero.
    /// </summary>
    static async Task NotifyFirstAuditAsync()
    {
        static List<MessageSummary> One() => [new MessageSummary { Id = "nuovo", FolderId = "INBOX", Subject = "Preventivo", From = new Address { Name = "Maria Rossi", Email = "maria.rossi@studio-prova.example" } }];
        var steps = new List<string>();
        var server = new TaskCompletionSource<int>();
        var check = UI.App.ShowThenCountAsync(One(),
            (rows, unread) => steps.Add($"elenco {rows.Count} {(unread?.ToString() ?? "stimati")}"), () => steps.Add("notifica"),
            () =>
            {
                steps.Add("conteggio chiesto");
                return server.Task;
            },
            ex => steps.Add("errore"));
        // Il server non ha ancora risposto: la notifica c'è già.
        bool first = !check.IsCompleted && steps.SequenceEqual(["elenco 1 stimati", "notifica", "conteggio chiesto"]);
        server.SetResult(7);
        await check;
        bool counted = steps.SequenceEqual(["elenco 1 stimati", "notifica", "conteggio chiesto", "elenco 0 7"]);

        // Conteggio non riuscito: la notifica è partita lo stesso e il numero resta quello stimato. Nessun messaggio nuovo: solo il numero.
        var broken = new List<string>();
        await UI.App.ShowThenCountAsync(One(), (rows, unread) => broken.Add($"elenco {rows.Count} {(unread?.ToString() ?? "stimati")}"), () => broken.Add("notifica"),
            () => Task.FromException<int>(new MailException("prova")), ex => broken.Add("errore " + ex.Message));
        var quiet = new List<string>();
        await UI.App.ShowThenCountAsync([], (rows, unread) => quiet.Add($"elenco {rows.Count} {(unread?.ToString() ?? "stimati")}"), () => quiet.Add("notifica"),
            () => Task.FromResult(3), ex => quiet.Add("errore"));
        Check(first && counted && broken.SequenceEqual(["elenco 1 stimati", "notifica", "errore prova"]) && quiet.SequenceEqual(["notifica", "elenco 0 3"]),
              "V-26, controllo dei nuovi messaggi: le righe nell'elenco e la notifica prima del conteggio dei non letti, che non le fa più aspettare (con il server che non risponde la notifica c'è già: " +
              $"{first}); quando arriva corregge il numero; se non riesce resta quello stimato; senza messaggi nuovi cambia solo il numero");
    }

    /// <summary>
    /// v2 V-38 (e la seconda parte di v1 V-19): le cartelle di un account Microsoft. Un Graph finto con sottocartelle su tre
    /// livelli: l'elenco è quello di prima, nello stesso ordine (ogni cartella seguita dalle sue, per nome), con le figlie
    /// chieste insieme invece che una dopo l'altra, e con la copia locale le cinque cartelle speciali che non cambiano mai
    /// identificativo non si richiedono; l'Archivio sì, e una cartella speciale della copia che non c'è più si richiede.
    /// </summary>
    static async Task GraphFoldersAuditAsync()
    {
        // (id, nome, figlie): l'albero finto. I nomi mettono alla prova l'ordine della lingua (accenti, maiuscole).
        var tree = new Dictionary<string, (string Id, string Name)[]>
        {
            [""] = [("in", "Inbox"), ("dr", "Drafts"), ("se", "Sent Items"), ("de", "Deleted Items"), ("ar", "Archive"), ("ju", "Junk Email"),
                    ("for", "Fornitori"), ("cli", "Clienti"), ("vec", "Vecchio archivio"), ("zeta", "zeta")],
            ["in"] = [("in-2", "Ricevute"), ("in-1", "Da fare")],
            ["cli"] = [("cli-r", "rossi"), ("cli-b", "Bianchi"), ("cli-a", "Àncora")],
            ["cli-r"] = [("cli-r-26", "2026"), ("cli-r-25", "2025")],
            ["cli-r-26"] = [("cli-r-26-f", "Fatture")],
            ["for"] = [("for-2", "Trasporti"), ("for-1", "Carta")],
        };
        var aliases = new Dictionary<string, string> { ["inbox"] = "in", ["drafts"] = "dr", ["sentitems"] = "se", ["deleteditems"] = "de", ["archive"] = "ar", ["junkemail"] = "ju" };
        JsonNode Folder((string Id, string Name) f) => new JsonObject
            { ["id"] = f.Id, ["displayName"] = f.Name, ["unreadItemCount"] = f.Id.Length, ["totalItemCount"] = 10 + f.Id.Length, ["childFolderCount"] = tree.TryGetValue(f.Id, out var kids) ? kids.Length : 0 };
        var fake = new SlowGraph
        {
            Answer = path =>
            {
                if (path.Contains("/childFolders")) return new JsonObject { ["value"] = new JsonArray(tree[path.Split('/')[^2]].Select(Folder).ToArray()) };
                if (path.Contains("mailFolders?")) return new JsonObject { ["value"] = new JsonArray(tree[""].Select(Folder).ToArray()) };
                return path.Contains("mailFolders/") && aliases.TryGetValue(path.Split('/')[^1].Split('?')[0], out var id) ? new JsonObject { ["id"] = id } : null;
            },
        };
        GraphProvider.UseTestHandler(fake);
        // Quello che deve uscire: le sei speciali nell'ordine di Missiva, le altre per nome, poi ogni albero in profondità.
        var expected = new List<string> { "in:Inbox", "dr:Drafts", "se:Sent", "de:Deleted", "ar:Archive", "ju:Junk", "cli:Other", "for:Other", "vec:Other", "zeta:Other" };
        void Below(string parent)
        {
            foreach (var c in tree[parent].OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                expected.Add($"{c.Id}:Other<{parent}");
                if (tree.ContainsKey(c.Id)) Below(c.Id);
            }
        }
        foreach (var top in tree[""].Where(t => tree.ContainsKey(t.Id))) Below(top.Id);
        static List<string> Shape(List<MailFolder> folders) => folders.Select(f => $"{f.Id}:{f.Kind}{(f.ParentId != null ? "<" + f.ParentId : "")}").ToList();

        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "maria.rossi@studio-prova.example", Tenant = "organizations" };
        var provider = new GraphProvider(account);
        try
        {
            var clock = Stopwatch.StartNew();
            var cold = await provider.GetFoldersAsync();
            long coldMs = clock.ElapsedMilliseconds;
            int coldSent = fake.Sent;
            bool same = Shape(cold).SequenceEqual(expected) && cold.First(f => f.Id == "cli-a") is { Name: "Àncora", Unread: 5, Total: 15 };

            // Con la copia locale (quella che la finestra salva dopo ogni lettura delle cartelle).
            MailCache.SaveFolders(account, cold);
            fake.Order.Clear();
            fake.MostOthers = 0;
            clock.Restart();
            var warm = await provider.GetFoldersAsync();
            long warmMs = clock.ElapsedMilliseconds;
            var asked = fake.Order.Where(o => aliases.Keys.Any(a => o.Contains("mailFolders/" + a + "?"))).ToList();
            int warmSent = fake.Order.Count;
            Check(same && Shape(warm).SequenceEqual(expected) && coldSent == 12 && warmSent == 7 && asked is [var only] && only.Contains("archive") && fake.MostOthers <= 3,
                  $"V-38, cartelle di Graph con sottocartelle su tre livelli: lo stesso elenco nello stesso ordine ({cold.Count} cartelle); senza copia locale {coldSent} richieste in {coldMs} ms, " +
                  $"con la copia locale {warmSent} in {warmMs} ms (delle cartelle speciali si chiede solo l'Archivio), al più {fake.MostOthers} richieste insieme; " +
                  "prima 12 richieste a ogni lettura, le cinque delle sottocartelle una dopo l'altra (243-257 ms con questo server finto a 25 ms per risposta)");

            // La copia dice una Posta inviata che non c'è più (casella migrata): si richiede. E l'Archivio spostato su un'altra cartella si vede.
            var stale = cold.Select(f => f.Kind == FolderKind.Sent ? new MailFolder { Id = "vecchia", Name = f.Name, Kind = f.Kind } : f).ToList();
            MailCache.SaveFolders(account, stale);
            aliases["archive"] = "vec";
            fake.Order.Clear();
            var moved = await provider.GetFoldersAsync();
            var again = fake.Order.Where(o => aliases.Keys.Any(a => o.Contains("mailFolders/" + a + "?"))).Select(o => o.Split('/')[^1].Split('?')[0]).Order().ToList();
            Check(again.SequenceEqual(["archive", "sentitems"]) && moved.Single(f => f.Kind == FolderKind.Sent).Id == "se" && moved.Single(f => f.Kind == FolderKind.Archive).Id == "vec"
                  && moved.Single(f => f.Id == "ar").Kind == FolderKind.Other,
                  "V-38, copia locale non più buona: una cartella speciale della copia che non è più nell'elenco si richiede a Graph, e l'Archivio scelto altrove (Outlook classico) si vede alla lettura dopo");
        }
        finally { MailCache.Delete(account); }
    }

    /// <summary>
    /// Un Graph finto che risponde dopo un'attesa, o quando la prova lo dice (Hold), e conta le richieste in corso insieme:
    /// tutte, quelle che non sono l'apertura di un messaggio, e gli scaricamenti.
    /// </summary>
    sealed class SlowGraph : HttpMessageHandler
    {
        public int Delay = 25, Sent, Now, Most, OthersNow, MostOthers, DownloadsNow, MostDownloads;
        /// <summary>Con un valore, le richieste restano senza risposta finché non si completa.</summary>
        public TaskCompletionSource? Hold;
        /// <summary>Le risposte della prova (percorso della richiesta): null lascia quelle di sempre.</summary>
        public Func<string, JsonNode?>? Answer;
        public readonly List<string> Order = new();
        readonly object counting = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            string path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            bool download = path.EndsWith("/$value");
            bool opening = !download && request.Method == HttpMethod.Get && (path.Contains("/messages/corpo") || path.Contains("conversationId eq"));
            lock (counting)
            {
                Sent++;
                Order.Add(request.Method.Method + " " + path);
                Most = Math.Max(Most, ++Now);
                if (!opening) MostOthers = Math.Max(MostOthers, ++OthersNow);
                if (download) MostDownloads = Math.Max(MostDownloads, ++DownloadsNow);
            }
            try
            {
                if (Hold is { } hold) await hold.Task.WaitAsync(cancel);
                else await Task.Delay(Delay, cancel);
            }
            finally
            {
                lock (counting)
                {
                    Now--;
                    if (!opening) OthersNow--;
                    if (download) DownloadsNow--;
                }
            }
            if (download) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2000]) };
            JsonNode answer;
            if (Answer?.Invoke(path) is { } given) answer = given;
            else if (path.Contains("/attachments/")) answer =new JsonObject { ["contentId"] = path[(path.LastIndexOf('/') + 1)..], ["contentBytes"] = Convert.ToBase64String(new byte[300]) };
            else if (path.Contains("/messages/corpo"))
                answer = new JsonObject
                {
                    ["id"] = "corpo", ["hasAttachments"] = false,
                    ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = "<p>Buongiorno</p>" + string.Concat(Enumerable.Range(1, 6).Select(i => $"<img src=\"cid:logo{i}\">")) },
                    ["attachments"] = new JsonArray(Enumerable.Range(1, 6).Select(i => (JsonNode)new JsonObject
                        { ["id"] = "logo" + i, ["name"] = $"logo{i}.png", ["contentType"] = "image/png", ["size"] = 300, ["isInline"] = true }).ToArray()),
                };
            else answer = new JsonObject { ["value"] = new JsonArray() };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>
    /// v2 V-24: il limite delle richieste insieme per casella di Outlook (GraphProvider.MailboxGate). Con un Graph finto che
    /// risponde piano: tante chiamate insieme, come all'avvio e peggio, non superano mai le quattro richieste; l'apertura di
    /// un messaggio passa anche con tre altre richieste ferme; gli scaricamenti sono al più due; una richiesta annullata
    /// mentre aspetta non si tiene il posto.
    /// </summary>
    static async Task MailboxGateAuditAsync()
    {
        var fake = new SlowGraph();
        GraphProvider.UseTestHandler(fake);
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = "organizations" };
        // Due provider della stessa casella (la finestra principale e una di una cartella sola): il limite è uno.
        GraphProvider first = new(account), second = new(account);
        var inbox = new MailFolder { Id = "inbox", Name = "Posta in arrivo", Kind = FolderKind.Inbox };
        static MessageSummary Mail(string id) => new() { Id = id, FolderId = "inbox", ConversationId = "conv-" + id, Subject = "Prova " + id, From = new Address { Name = "Maria Rossi", Email = "maria.rossi@studio-prova.example" } };
        var many = Enumerable.Range(0, 12).Select(i => Mail("m" + i)).ToList();

        // Come all'avvio, e peggio: cartelle (sei speciali insieme), elenco, dodici segni, categorie da un provider; corpo con
        // sei immagini, conversazione e un allegato dall'altro.
        var clock = Stopwatch.StartNew();
        var body = second.GetBodyAsync(Mail("corpo"));
        await Task.WhenAll(first.GetFoldersAsync(), first.GetMessagesAsync(inbox, null, null), first.SetReadAsync(many, true), second.GetFoldersAsync(),
                           body, second.GetConversationAsync(Mail("corpo")), second.GetAttachmentAsync(Mail("corpo"), new Attachment { Id = "a1", Name = "a.pdf" }));
        bool images = body.Result.Attachments.Count(a => a.IsInline && a.Content is { Length: 300 }) == 6;
        Check(fake.Most == GraphProvider.MailboxGate.Together && fake.MostOthers <= GraphProvider.MailboxGate.NotOpening && images,
              $"V-24, limite per casella di Outlook: {fake.Sent} richieste di sette chiamate insieme da due provider della stessa casella, mai più di {fake.Most} in corso " +
              $"(prima il limite valeva dentro ogni chiamata: fino a 4 per chiamata, e le sei cartelle speciali senza limite), di cui al più {fake.MostOthers} che non aprono un messaggio; " +
              $"tutte finite in {clock.ElapsedMilliseconds} ms con il server finto a {fake.Delay} ms per risposta");

        // Tre richieste qualsiasi ferme: la quarta aspetta, l'apertura di un messaggio no.
        fake.Hold = new TaskCompletionSource();
        int before = fake.Sent;
        var stuck = Enumerable.Range(0, 4).Select(i => first.SetReadAsync([Mail("fermo" + i)], true)).ToList();
        await Task.Delay(60);
        int others = fake.Sent - before;
        var conversation = second.GetConversationAsync(Mail("corpo"));
        await Task.Delay(60);
        bool passed = fake.Sent - before == others + 1;
        // Annullata mentre aspetta: non parte e non tiene il posto.
        using var cancelled = new CancellationTokenSource();
        var waiting = first.GetConversationAsync(Mail("in-attesa"), cancelled.Token);
        await Task.Delay(30);
        int sentBeforeCancel = fake.Sent - before;
        cancelled.Cancel();
        bool gaveUp = false;
        try { await waiting; }
        catch (OperationCanceledException) { gaveUp = true; }
        fake.Hold.SetResult();
        await Task.WhenAll(stuck.Append(conversation));
        fake.Hold = null;
        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => second.GetConversationAsync(Mail("dopo" + i))));
        Check(others == 3 && passed && sentBeforeCancel == others + 1 && gaveUp && fake.Sent - before == 4 + 1 + 4,
              $"V-24, il posto per l'apertura di un messaggio: con quattro segni chiesti e il server fermo ne partono {others}, la conversazione del messaggio aperto parte lo stesso ({passed}); " +
              $"un'altra apertura, a posti finiti, aspetta senza partire e annullata si ferma ({gaveUp}) senza tenersi il posto: dopo, quattro aperture insieme partono tutte");

        // Scaricamenti: al più due insieme, e una richiesta breve passa lo stesso.
        fake.Hold = new TaskCompletionSource();
        before = fake.Sent;
        fake.MostDownloads = 0;
        var files = Enumerable.Range(0, 4).Select(i => first.GetAttachmentAsync(Mail("grande"), new Attachment { Id = "file" + i, Name = $"file{i}.pdf" })).ToList();
        await Task.Delay(60);
        int downloading = fake.Sent - before;
        var shortOne = second.SetReadAsync([Mail("breve")], true);
        await Task.Delay(60);
        bool shortSent = fake.Sent - before == downloading + 1;
        fake.Hold.SetResult();
        fake.Hold = null;
        await Task.WhenAll(files.Append<Task>(shortOne));
        Check(downloading == GraphProvider.MailboxGate.Lengthy && shortSent && fake.MostDownloads == 2 && files.All(f => f.Result.Length == 2000),
              $"V-24, scaricamenti: di quattro allegati chiesti insieme ne partono {downloading} alla volta, e un segno chiesto nel frattempo parte subito ({shortSent}); alla fine arrivano tutti");
    }

    /// <summary>
    /// v2 V-23: il gestore HTTP di Graph e delle API di Google (Network.ApiHandler, Network.ApiRequest) contro un server su
    /// 127.0.0.1 che parla solo HTTP/1.1 e comprime quando glielo si chiede: la richiesta chiede la risposta compressa e la
    /// legge, quella in HTTP/2 ripiega su 1.1, e uno scaricamento a blocchi (come GraphProvider.DownloadAsync, da un secondo
    /// client sullo stesso gestore) riusa la connessione della richiesta di prima e arriva intero anche compresso.
    /// </summary>
    static async Task ApiHandlerAuditAsync()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port, connections = 0;
        var heads = new List<string>();
        string json = "{\"value\":[" + string.Join(",", Enumerable.Range(0, 50).Select(i =>
            $"{{\"id\":\"AQMkAD{i:000000}\",\"subject\":\"Fattura numero {i}\",\"from\":{{\"emailAddress\":{{\"name\":\"Mario Rossi\",\"address\":\"mario.rossi@studio-prova.example\"}}}},\"isRead\":false,\"bodyPreview\":\"Buongiorno, in allegato la fattura del mese. Cordiali saluti\"}}")) + "]}";
        // Un "allegato" da 3 MB che si comprime poco (una metà è casuale), mandato compresso lo stesso.
        var big = new byte[3_000_000];
        new Random(9).NextBytes(big.AsSpan(0, big.Length / 2));
        static byte[] Gzip(byte[] plain)
        {
            using var packed = new MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(packed, System.IO.Compression.CompressionLevel.Fastest)) gzip.Write(plain);
            return packed.ToArray();
        }
        long wire = 0;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                System.Net.Sockets.TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch (Exception) { return; }
                Interlocked.Increment(ref connections);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (client)
                        {
                            var stream = client.GetStream();
                            var reader = new StreamReader(stream, Encoding.ASCII);
                            while (true)
                            {
                                var head = new StringBuilder();
                                string? line;
                                while ((line = await reader.ReadLineAsync()) is { Length: > 0 }) head.AppendLine(line);
                                if (line == null) return;
                                lock (heads) heads.Add(head.ToString());
                                bool packed = head.ToString().Split('\n').Any(l => l.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase) && l.Contains("gzip"));
                                var body = head.ToString().StartsWith("GET /grande") ? big : Encoding.UTF8.GetBytes(json);
                                if (packed) body = Gzip(body);
                                Interlocked.Add(ref wire, body.Length);
                                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n{(packed ? "Content-Encoding: gzip\r\n" : "")}\r\n"));
                                await stream.WriteAsync(body);
                            }
                        }
                    }
                    catch (Exception) { }
                });
            }
        });
        try
        {
            using var handler = Network.ApiHandler();
            using var http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(20) };
            using var downloads = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            string text;
            Version version;
            using (var request = Network.ApiRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/elenco"))
            using (var response = await http.SendAsync(request))
            {
                text = await response.Content.ReadAsStringAsync();
                version = response.Version;
            }
            long listWire = Interlocked.Read(ref wire);
            // Come DownloadAsync: intestazioni, poi il contenuto a blocchi.
            using var arrived = new MemoryStream();
            long? declared;
            using (var request = Network.ApiRequest(HttpMethod.Get, $"http://127.0.0.1:{port}/grande"))
            using (var response = await downloads.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                declared = response.Content.Headers.ContentLength;
                await using var stream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[81920];
                for (int read; (read = await stream.ReadAsync(buffer)) > 0;) arrived.Write(buffer, 0, read);
            }
            bool asked;
            lock (heads) asked = heads.Count == 2 && heads.All(h => h.Split('\n').Any(l => l.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase) && l.Contains("gzip")));
            Check(asked && text == json && version == HttpVersion.Version11 && arrived.ToArray().AsSpan().SequenceEqual(big) && declared == null && connections == 1,
                  $"V-23, gestore HTTP di Graph e di Google: le richieste chiedono la risposta compressa e la leggono (un elenco finto di 50 messaggi: {Encoding.UTF8.GetByteCount(json) / 1024.0:0.0} KB, " +
                  $"sul filo {listWire / 1024.0:0.0}); chiesta in HTTP/2 a un server che parla solo 1.1, arriva in HTTP/{version}; uno scaricamento a blocchi di 3 MB dal secondo client arriva intero " +
                  $"anche compresso (senza Content-Length) e riusa la connessione della richiesta di prima ({connections} connessione; prima i due client avevano ognuno le sue)");
        }
        finally { listener.Stop(); }
    }

    /// <summary>
    /// v2 V-36 = v1 V-20: il controllo dei nuovi messaggi di Gmail e IMAP, come lo fa App.CheckAccountAsync (GetNewAsync, poi
    /// GetInboxUnreadAsync), con i comandi contati sul server IMAP finto. Con due non letti di oggi già annunciati ogni
    /// controllo richiedeva le loro date; e il conteggio rifaceva il NOOP di un attimo prima.
    /// </summary>
    static async Task NewMailCommandsAuditAsync(bool gmail)
    {
        using var fake = new FakeImap(new Flavor(Gmail: gmail));
        ImapProvider.TestPlain = true;
        try
        {
            fake.Add("INBOX", SpeedMail("Fattura di settembre", "In allegato la fattura"));
            fake.Add("INBOX", SpeedMail("Sollecito", "Le ricordiamo la scadenza"));
            var provider = FakeProvider(fake, gmail, "mario@studio-prova.example");
            using var closing = provider as IDisposable;
            static string Shape(IEnumerable<string> commands) => string.Join(", ", commands.Select(c =>
                c.StartsWith("NOOP") ? "NOOP" : c.Contains("SEARCH") ? (c.Contains("RETURN") ? "SEARCH (numero)" : "SEARCH") :
                c.Contains("FETCH") ? (c.Contains("ENVELOPE") ? "FETCH (righe)" : c.Contains("BODY.PEEK[") ? "FETCH (anteprime)" : "FETCH (date)") : c.Split(' ')[0]));
            async Task<(List<MessageSummary> New, int Unread, string Commands)> CheckAsync(DateTime since)
            {
                fake.Commands.Clear();
                var incoming = await provider.GetNewAsync(since);
                int unread = await provider.GetInboxUnreadAsync();
                return (incoming, unread, Shape(fake.Commands.ToList()));
            }
            var start = DateTime.UtcNow.AddMinutes(-30);
            var first = await CheckAsync(start);   // il primo controllo: i due non letti sono nuovi
            var second = await CheckAsync(DateTime.UtcNow.AddSeconds(1)); // già annunciati: niente di nuovo
            var third = await CheckAsync(DateTime.UtcNow.AddSeconds(1));
            // Le date di IMAP sono al secondo: un momento fra i due vecchi e quello nuovo.
            await Task.Delay(1100);
            var mark = DateTime.UtcNow;
            await Task.Delay(1100);
            fake.Add("INBOX", SpeedMail("Conferma dell'ordine", "Ordine confermato"));
            var arrival = await CheckAsync(mark);  // ne arriva uno: solo quello
            var after = await CheckAsync(DateTime.UtcNow.AddSeconds(1));
            Report.Add($"      {(gmail ? "Gmail" : "IMAP")}, primo controllo: {first.Commands}");
            Report.Add($"      {(gmail ? "Gmail" : "IMAP")}, controllo senza novità: {third.Commands}");
            Report.Add($"      {(gmail ? "Gmail" : "IMAP")}, controllo con un arrivo: {arrival.Commands}");
            Check(first.New.Count == 2 && first.Unread == 2 && second.New.Count == 0 && second.Unread == 2 && third.New.Count == 0
                  && arrival.New is [{ Subject: "Conferma dell'ordine" }] && arrival.Unread == 3 && after.New.Count == 0 && after.Unread == 3
                  && third.Commands == "NOOP, SEARCH, SEARCH" && after.Commands == third.Commands && arrival.Commands.StartsWith("NOOP, SEARCH, FETCH (date), FETCH (righe)"),
                  $"V-36, controllo dei nuovi messaggi con {(gmail ? "Gmail" : "IMAP")} e due non letti di oggi già annunciati: {third.Commands.Split(", ").Length} comandi a controllo " +
                  "(prima 5: NOOP, SEARCH, FETCH delle date, NOOP, SEARCH del numero); con un arrivo la data si chiede solo del messaggio nuovo; gli stessi messaggi nuovi e gli stessi non letti di prima");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            GmailProvider.TestPort = null;
        }
    }

    /// <summary>
    /// v2 V-29: il processo dei PDF si avviava sul thread di chi chiede l'anteprima (quello dell'interfaccia). Ora parte su un
    /// thread del pool, insieme alla richiesta: chi chiede riprende subito.
    /// </summary>
    static void PdfStartAuditTests()
    {
        using var worker = new PdfWorker();
        var clock = Stopwatch.StartNew();
        var loading = worker.LoadAsync(Pdf(1)); // su questo thread solo fino al primo await
        double asked = clock.Elapsed.TotalMilliseconds;
        bool waiting = !loading.IsCompleted;
        var (pages, _, _) = loading.GetAwaiter().GetResult();
        double loaded = clock.Elapsed.TotalMilliseconds;
        Check(pages == 1 && waiting && asked < 15 && worker.TestInJob() && worker.TestInAppContainer(),
              $"V-29, prima anteprima di un PDF con il processo da avviare: chi chiede riprende dopo {asked:0.0} ms (prima restava fermo per tutto l'avvio, 42-56 ms su questo PC); " +
              $"il documento è caricato dopo {loaded:0} ms, nel suo job e nel suo AppContainer come prima");
    }

    /// <summary>
    /// v1 V-05, v2 V-27: Invio riusa la ricerca che i suggerimenti hanno appena fatto, o stanno facendo, con lo stesso testo
    /// (SuggestSearches). Qui il "server" è una funzione che conta le richieste e risponde quando la prova lo dice.
    /// </summary>
    static void SuggestReuseAuditTests()
    {
        var searches = new UI.SuggestSearches();
        long now = 100_000;
        searches.Clock = () => now;
        var asked = new List<(TaskCompletionSource<MessagePage> Done, CancellationToken Cancel)>();
        Task<MessagePage> Server(CancellationToken cancel)
        {
            var done = new TaskCompletionSource<MessagePage>();
            cancel.Register(() => done.TrySetCanceled());
            asked.Add((done, cancel));
            return done.Task;
        }
        static MessagePage Page(string id) => new() { Messages = [new MessageSummary { Id = id, FolderId = "INBOX", Subject = "Fattura " + id }], Next = "50" };
        static string Key(string query) => UI.SuggestSearches.Key("prova", null, query);

        // I suggerimenti cercano messaggi e file; finita la prima, Invio con lo stesso testo: nessuna richiesta in più.
        var mail = searches.Start(Key("fattura"), Server);
        var files = searches.Start(Key("filename:fattura"), Server);
        bool once = searches.Start(Key("fattura"), Server) == mail; // la stessa chiesta due volte (IMAP: messaggi e file): una richiesta
        var first = Page("1");
        asked[0].Done.SetResult(first);
        searches.CancelPending([Key("fattura")]); // la ricerca di Invio chiude il pannello tenendo la sua
        var reused = searches.Take(Key("fattura"));
        bool finished = once && reused == mail && reused!.Result == first && asked.Count == 2 && files.IsCanceled && searches.Take(Key("fattura")) == null;

        // Invio mentre i suggerimenti cercano ancora: la ricerca in corso non si ferma e si aspetta quella.
        var running = searches.Start(Key("preventivo"), Server);
        var other = searches.Start(Key("filename:preventivo"), Server);
        searches.CancelPending([Key("preventivo")]);
        var waited = searches.Take(Key("preventivo"));
        bool inFlight = waited == running && !running.IsCompleted && !asked[2].Cancel.IsCancellationRequested && other.IsCanceled;
        asked[2].Done.SetResult(Page("2"));
        inFlight = inFlight && waited!.Result.Messages[0].Id == "2" && asked.Count == 4;

        // Un'altra lettera (nessuna ricerca da tenere): quella in corso si ferma, e Invio dopo cerca di nuovo.
        var superseded = searches.Start(Key("ordin"), Server);
        searches.CancelPending();
        bool stopped = superseded.IsCanceled && searches.Take(Key("ordin")) == null;

        // Una ricerca finita da più di 30 secondi non vale più per Invio; entro i 30 sì. Una non riuscita mai.
        searches.Start(Key("vecchia"), Server);
        asked[^1].Done.SetResult(Page("3"));
        searches.Start(Key("recente"), Server);
        asked[^1].Done.SetResult(Page("4"));
        searches.Start(Key("rotta"), Server);
        asked[^1].Done.SetException(new MailException("prova"));
        now += 29_000;
        bool fresh = searches.Take(Key("recente")) is { Result.Messages: [{ Id: "4" }] };
        now += 2_000;
        bool stale = searches.Take(Key("vecchia")) == null && searches.Take(Key("rotta")) == null && searches.Take(Key("mai chiesta")) == null;
        int before = asked.Count;
        var again = searches.Start(Key("rotta"), Server); // i suggerimenti dopo un errore richiedono
        Check(finished && inFlight && stopped && fresh && stale && asked.Count == before + 1 && !again.IsCompleted,
              "V-05/V-27, Invio dopo i suggerimenti: la ricerca con lo stesso testo appena finita si riusa senza richieste in più (una volta: un secondo Invio cerca di nuovo), quella ancora in corso " +
              "non si ferma e si aspetta, le altre del pannello si fermano; dopo un'altra lettera, dopo 30 secondi o dopo un errore si cerca di nuovo " +
              $"(finita {finished}, in corso {inFlight}, superata {stopped}, entro 30 s {fresh}, vecchia o rotta {stale})");
    }

    /// <summary>
    /// v2 V-21 (e v1 V-12, solo l'annullamento): la ricerca senza rete nella copia sul PC. Prima una ricerca lanciata subito
    /// dopo un'altra non fermava la prima: due scansioni intere insieme, ognuna su tutti i processori. Ora la prima si ferma
    /// fra un file e l'altro. In una cartella temporanea, con 300 corpi finti da 60 KB.
    /// </summary>
    static void OfflineSearchAuditTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "missiva-prova-ricerca-" + Guid.NewGuid().ToString("N")[..8]);
        BodyCache.TestRoot = root;
        try
        {
            var account = new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Id = "prova-ricerca" };
            var filler = string.Concat(Enumerable.Range(0, 600).Select(i => $"<tr><td style=\"padding:8px\">Riga {i} del preventivo, con il dettaglio dei lavori e il prezzo concordato.</td></tr>\r\n"));
            const int count = 300;
            for (int i = 0; i < count; i++)
                BodyCache.Save(account, new MessageSummary { Id = "r" + i, FolderId = "INBOX", Subject = "Pratica " + i, Date = DateTime.Now.AddMinutes(-i), From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" } },
                               new MessageBody { Html = $"<table>{filler}</table><p>{(i % 10 == 0 ? "parola-rara" : "niente")} numero {i}</p>", To = [new Address { Name = "Mario Rossi", Email = "mario@studio-prova.example" }] });
            Task.WhenAll(BodyCache.TestWrites.ToArray()).GetAwaiter().GetResult();
            long bytes = BodyCache.Size(account).Bytes;

            // Una ricerca sola, e due insieme com'era prima (la seconda lanciata 30 ms dopo la prima, nessuna si ferma).
            BodyCache.Search(account, "parola-rara", null, 200);
            var clock = Stopwatch.StartNew();
            var alone = BodyCache.Search(account, "parola-rara", null, 200);
            double one = clock.Elapsed.TotalMilliseconds;
            int reads = BodyCache.TestSearchReads;
            clock.Restart();
            var first = Task.Run(() => BodyCache.Search(account, "niente", null, 200));
            Thread.Sleep(30);
            var second = BodyCache.Search(account, "parola-rara", null, 200);
            double twoSecond = clock.Elapsed.TotalMilliseconds - 30;
            first.GetAwaiter().GetResult();
            int readsTwo = BodyCache.TestSearchReads - reads;

            // Com'è: la prima si ferma quando parte la seconda.
            reads = BodyCache.TestSearchReads;
            using var stop = new CancellationTokenSource();
            clock.Restart();
            var stopped = Task.Run(() => BodyCache.Search(account, "niente", null, 200, stop.Token));
            Thread.Sleep(30);
            stop.Cancel();
            var wanted = BodyCache.Search(account, "parola-rara", null, 200, CancellationToken.None);
            double nowSecond = clock.Elapsed.TotalMilliseconds - 30;
            bool cancelled = false;
            try { stopped.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            int readsNow = BodyCache.TestSearchReads - reads;

            // Già annullata prima di partire: nessun file letto.
            reads = BodyCache.TestSearchReads;
            bool never = false;
            try { BodyCache.Search(account, "niente", null, 200, new CancellationToken(true)); }
            catch (OperationCanceledException) { never = true; }
            int readsNever = BodyCache.TestSearchReads - reads;

            static string Ids(List<MessageSummary> found) => string.Join(",", found.Select(m => m.Id));
            Check(alone.Count == count / 10 && Ids(alone) == Ids(second) && Ids(alone) == Ids(wanted) && cancelled && never && readsNever == 0 && readsTwo == 2 * count && readsNow < 2 * count - count / 4,
                  $"V-21, ricerca senza rete in {count} corpi finti ({bytes / 1024 / 1024} MB): una ricerca {one:0} ms; lanciandone un'altra 30 ms dopo, la prima si ferma fra un file e l'altro " +
                  $"({readsNow - count} file letti invece di {count}) e la seconda arriva in {nowSecond:0} ms invece di {twoSecond:0}; gli stessi risultati ({wanted.Count}); annullata prima di partire non legge nulla");
        }
        finally
        {
            BodyCache.TestRoot = null;
            try { Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>
    /// v2 V-17, v1 V-13 e v2 V-11: solo misure, nessun controllo. Quanto costano su questo PC i pezzi che i provider fanno
    /// dopo un await sul thread dell'interfaccia (il JSON di Graph con il corpo dentro, il base64 delle immagini, il testo di
    /// una parte MIME di Gmail e IMAP) e una decifratura DPAPI di un testo delle impostazioni.
    /// </summary>
    static void ProviderWorkMeasure()
    {
        static double Time(int times, Action work)
        {
            work();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < times; i++) work();
            return clock.Elapsed.TotalMilliseconds / times;
        }
        // Una newsletter finta: tabelle, stili in linea, link e lettere accentate.
        var row = "<tr><td style=\"padding:12px 24px;font-family:Arial,sans-serif;font-size:14px;color:#333333\"><a href=\"https://negozio-prova.example/offerta?id={0}&amp;utm=posta\" style=\"color:#0a66c2\">Offerta n. {0}</a>: perché è così conveniente, già da oggi.</td></tr>\r\n";
        string Newsletter(int kb)
        {
            var sb = new StringBuilder("<html><head><style>td{line-height:1.4}</style></head><body><table width=\"600\">");
            for (int i = 0; sb.Length < kb * 1024; i++) sb.AppendFormat(row, i);
            return sb.Append("</table><img src=\"cid:logo@prova\"></body></html>").ToString();
        }
        var parts = new List<string>();
        foreach (int kb in new[] { 60, 211, 1000 })
        {
            var html = Newsletter(kb);
            var message = new JsonObject
            {
                ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = html },
                ["toRecipients"] = new JsonArray(new JsonObject { ["emailAddress"] = new JsonObject { ["name"] = "Mario Rossi", ["address"] = "mario.rossi@studio-prova.example" } }),
                ["internetMessageHeaders"] = new JsonArray(Enumerable.Range(0, 40).Select(i => (JsonNode)new JsonObject { ["name"] = "X-Prova-" + i, ["value"] = new string('v', 80) }).ToArray()),
            };
            var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
            double graph = Time(10, () =>
            {
                var text = Encoding.UTF8.GetString(bytes); // ReadAsStringAsync
                var m = JsonNode.Parse(text);              // Result
                var content = m!["body"]!["content"]!.GetValue<string>();
                _ = content.Contains("cid:logo@prova", StringComparison.OrdinalIgnoreCase);
                _ = m["internetMessageHeaders"]!.AsArray().Count(h => h!["name"]!.GetValue<string>().Equals("Authentication-Results", StringComparison.OrdinalIgnoreCase));
            });
            var part = new MimeKit.TextPart("html") { Text = html, ContentTransferEncoding = MimeKit.ContentEncoding.QuotedPrintable };
            using var stream = new MemoryStream();
            part.WriteTo(stream);
            double mime = Time(10, () =>
            {
                stream.Position = 0;
                var loaded = (MimeKit.TextPart)MimeKit.MimeEntity.Load(stream); // dentro MailKit, fuori dal thread dell'interfaccia
                _ = loaded.Text;
            });
            stream.Position = 0;
            var once = (MimeKit.TextPart)MimeKit.MimeEntity.Load(stream);
            double textOnly = Time(10, () => _ = once.Text); // quello che resta sul thread dell'interfaccia
            parts.Add($"corpo di {kb} KB: JSON di Graph {graph:0.0} ms; testo della parte MIME {textOnly:0.0} ms (con la lettura della parte {mime:0.0})");
        }
        var image = new byte[300 * 1024];
        new Random(3).NextBytes(image);
        var attachment = Encoding.UTF8.GetBytes(new JsonObject { ["contentId"] = "logo@prova", ["contentBytes"] = Convert.ToBase64String(image) }.ToJsonString());
        double inline = Time(10, () =>
        {
            var a = JsonNode.Parse(Encoding.UTF8.GetString(attachment));
            _ = Convert.FromBase64String(a!["contentBytes"]!.GetValue<string>());
        });
        var imagePart = new MimeKit.MimePart("image", "png") { Content = new MimeKit.MimeContent(new MemoryStream(image)), ContentTransferEncoding = MimeKit.ContentEncoding.Base64 };
        using var imageStream = new MemoryStream();
        imagePart.WriteTo(imageStream);
        imageStream.Position = 0;
        var loadedImage = (MimeKit.MimePart)MimeKit.MimeEntity.Load(imageStream);
        double decode = Time(10, () =>
        {
            using var ms = new MemoryStream();
            loadedImage.Content!.DecodeTo(ms);
        });
        Report.Add($"      (V-17, il lavoro dei provider dopo un await, su questo PC: {string.Join("; ", parts)}; un'immagine incorporata di 300 KB: Graph {inline:0.0} ms, MIME {decode:0.0} ms)");

        // V-11: una decifratura DPAPI di un testo corto delle impostazioni (oggetto o mittente di un contrassegno).
        var sealedTexts = Enumerable.Range(0, 500).Select(i => Store.Protect("Pratica " + i + " di Maria Rossi")).ToList();
        double dpapi = Time(1, () => { foreach (var s in sealedTexts) Store.Unprotect(s); });
        Report.Add($"      (V-11, DPAPI all'avvio: 500 testi delle impostazioni decifrati in {dpapi:0.0} ms, {dpapi * 1000 / 500:0} microsecondi l'uno)");
    }

    /// <summary>
    /// v2 V-15, v1 V-07: i suggerimenti dei destinatari. Prima ogni lettera ricopiava la rubrica, rifaceva una voce per ogni
    /// indirizzo dei contatti e rimetteva in minuscolo e in parole ogni voce; ora un indice in memoria, rifatto solo quando
    /// rubrica o contatti cambiano. Le persone proposte e il loro ordine devono essere quelli di prima (SuggestOld), anche
    /// dopo ogni tipo di cambio. Con una rubrica finta in memoria (TestBook), mai salvata.
    /// </summary>
    static void SuggestIndexAuditTests()
    {
        var contactsBefore = AddressBook.Contacts;
        try
        {
            var random = new Random(15);
            // 5.000 voci di rubrica: molte con gli stessi conteggi e le stesse date (l'ordine fra pari è quello della rubrica).
            var people = ManyContacts("rubrica", 5000, 5);
            var day = new DateTime(2026, 9, 1);
            var book = people.Where(c => c.Emails.Count > 0).Select((c, i) => new AddressBook.Entry
            {
                Name = i % 13 == 0 ? "" : c.Name, Email = c.Emails[0], Sent = random.Next(4), Received = random.Next(3),
                LastSent = day.AddDays(random.Next(5)), LastReceived = day.AddDays(random.Next(5)),
            }).ToList();
            AddressBook.TestBook(book);
            // 4.500 contatti: uno su sei con un indirizzo che è già in rubrica, alcuni con due indirizzi, con un nome che è un
            // indirizzo, con un "noreply", e qualche gruppo (uno senza persone).
            var contacts = ManyContacts("contatti", 4500, 6);
            for (int i = 0; i < contacts.Count; i++)
            {
                if (i % 6 == 0) contacts[i].Emails = [book[random.Next(book.Count)].Email];
                if (i % 10 == 0) contacts[i].Emails.Add($"secondo.{i}@altro-prova.example");
                if (i % 50 == 0 && contacts[i].Emails.Count > 0) (contacts[i].DisplayName, contacts[i].GivenName, contacts[i].Surname) = (contacts[i].Emails[0], "", "");
                if (i % 70 == 0) contacts[i].Emails.Add($"noreply.{i}@massa-prova.example");
            }
            contacts.Add(new Contact { DisplayName = "Marketing di prova", IsGroup = true, Members = ["Mario Rossi <mario.rossi@massa-prova.example>", "maria.rossi@massa-prova.example"] });
            contacts.Add(new Contact { DisplayName = "Magazzino vuoto", IsGroup = true });
            AddressBook.Contacts = () => contacts;

            string[] letters = "abcdefghilmnoprstuvz0123456789@.-".Select(c => c.ToString()).ToArray();
            var queries = letters.Concat(["ma", "mar", "mari", "mario", "mario r", "MARIO ROSSI", "ro ma", "niccolò", "NICCOLÒ", "nicc", "d'am", "de luca", "luca de", "studio", "studio r", "massa", "massa-prova",
                                          "mario.rossi", "mario.rossi.1", "secondo", "altro-prova.example", "noreply", "marketing", "magazzino", "rossi 4", "  rossi   ma  ", "zzz", "m r s"]).ToList();
            var present = new List<string> { book[0].Email, book[7].Email, contacts[1].Emails.FirstOrDefault() ?? "x@prova.example" };
            static string Shape(List<AddressBook.Entry> found) => string.Join("\n", found.Select(e =>
                $"{e.Email}|{e.Name}|{e.Company}|{e.Contact}|{e.Group?.Name}|{e.Sent}|{e.Received}|{e.LastSent:s}|{e.LastReceived:s}"));
            var different = new List<string>();
            int compared = 0;
            void Compare(string after)
            {
                foreach (var q in queries)
                    foreach (int max in new[] { 8, 10, 100000 })
                    {
                        compared++;
                        if (Shape(AddressBook.SuggestOld(q, max, max == 8 ? present : null)) != Shape(AddressBook.Suggest(q, max, max == 8 ? present : null))) different.Add($"\"{q}\" {after}");
                    }
            }
            Compare("all'inizio");
            int some = AddressBook.Suggest("mario", 100000).Count;

            // Tempi e memoria per lettera, com'era e com'è (dopo la prima, che costruisce l'indice).
            string[] typed = ["m", "ma", "mar", "mari", "mario", "mario ", "mario r"];
            (double Ms, long Bytes) Measure(Func<string, int, ICollection<string>?, List<AddressBook.Entry>> suggest)
            {
                foreach (var t in typed) suggest(t, 8, present);
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 5; i++)
                    foreach (var t in typed) suggest(t, 8, present);
                return (clock.Elapsed.TotalMilliseconds / (5 * typed.Length), (GC.GetAllocatedBytesForCurrentThread() - bytes) / (5 * typed.Length));
            }
            var was = Measure(AddressBook.SuggestOld);
            var now = Measure(AddressBook.Suggest);
            var build = Stopwatch.StartNew();
            AddressBook.Received([new Address { Name = "Mario Rossi", Email = "mario.rossi.nuovo@massa-prova.example" }], day.AddDays(9)); // la rubrica cambia: indice da rifare
            AddressBook.Suggest("m", 8, present);
            double rebuilt = build.Elapsed.TotalMilliseconds;

            // Ogni tipo di cambio, poi di nuovo il confronto con il calcolo di prima.
            Compare("dopo una voce nuova in rubrica");
            AddressBook.Sent([new Address { Name = "Maria Rossi", Email = book[20].Email }, new Address { Name = "Marta Nuova", Email = contacts[6].Emails[0] }], day.AddDays(10));
            Compare("dopo un messaggio inviato (conteggi e nome cambiati, anche di un contatto)");
            contacts[3].DisplayName = "Mario Cambiato di prova";
            contacts[4].Company = "Mario e figli";
            Compare("dopo un contatto cambiato sul posto (nome, azienda)");
            contacts[5].Emails.Add("mario.aggiunto@altro-prova.example");
            contacts[8].Emails = ["mario.sostituito@altro-prova.example"];
            Compare("dopo gli indirizzi di un contatto cambiati");
            contacts.RemoveAt(10);
            contacts.Insert(0, new Contact { DisplayName = "Mario Primo di prova", Emails = ["mario.primo@altro-prova.example"], Company = "Studio Primo" });
            Compare("dopo un contatto tolto e uno aggiunto");
            contacts[^1].Members.Add("mario.rossi@massa-prova.example");
            contacts[^2].Members.Clear();
            Compare("dopo i gruppi cambiati (uno riempito, uno svuotato)");
            AddressBook.Contacts = null;
            Compare("senza contatti");
            Check(different.Count == 0 && some > 20,
                  $"V-15, suggerimenti dei destinatari con una rubrica finta di {book.Count} voci e {contacts.Count} contatti: {compared} ricerche, le stesse persone nello stesso ordine di prima, anche dopo ogni cambio " +
                  "(voce nuova, messaggio inviato, contatto cambiato sul posto, indirizzi, contatti tolti e aggiunti, gruppi, senza contatti)" +
                  (different.Count > 0 ? $" [diverse {different.Count}: {string.Join("; ", different.Take(5))}]" : ""));
            Check(now.Ms < was.Ms && now.Bytes < was.Bytes / 10,
                  $"V-15, a ogni lettera: {now.Ms:0.00} ms e {now.Bytes / 1024} KB allocati invece di {was.Ms:0.0} ms e {was.Bytes / 1024 / 1024.0:0.0} MB; l'indice si rifà solo quando rubrica o contatti cambiano ({rebuilt:0.0} ms la lettera dopo un cambio)");
        }
        finally
        {
            AddressBook.Contacts = contactsBefore;
            AddressBook.TestBook(null);
        }
    }

    /// <summary>Contatti finti in quantità, con nomi, aziende, telefoni e note diversi (e lettere accentate, maiuscole e minuscole).</summary>
    static List<Contact> ManyContacts(string source, int count, int seed)
    {
        string[] given = ["Mario", "Maria", "Anna", "Luca", "Niccolò", "Agata", "Elia", "Zeno", "Marta", "marco", "Beatrice", "Carlo", "Élise", "Ugo", "Oscar", "Paola", "Giulia", "Franco", "Sara", "Davide", "Elena", "Pietro", "Chiara", "èlia"];
        string[] family = ["Rossi", "Bianchi", "Verdi", "Neri", "Fontana", "Esposito", "De Luca", "d'Amico", "Marini", "Martini", "Ubaldi", "Zanella"];
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(i =>
        {
            string g = given[random.Next(given.Length)], f = family[random.Next(family.Length)];
            return new Contact
            {
                Source = source, ServerId = source + "-" + i, GivenName = g, Surname = f, DisplayName = i % 7 == 0 ? "" : $"{g} {f} {i % 97}",
                Emails = i % 11 == 0 ? [] : [$"{g}.{f}.{i}@massa-prova.example".ToLowerInvariant().Replace(' ', '-').Replace("'", "")],
                Mobile = "+39 333 " + (1000000 + i), BusinessPhone = i % 3 == 0 ? "02 000 " + i : "", Company = i % 4 == 0 ? "Studio " + f : "", JobTitle = i % 9 == 0 ? "Responsabile acquisti" : "",
                Notes = i % 5 == 0 ? $"Conosciuto alla fiera di prova del {1 + i % 28} marzo; preferisce il telefono. Pratica {i}." : "",
            };
        }).ToList();
    }

    /// <summary>
    /// v2 V-16, v1 V-06: Persone, la ricerca a ogni lettera. Prima ogni campo di ogni contatto passava dal confronto della
    /// lingua di Windows (22-31 ms a lettera con 4.500 contatti); ora per la via svelta dove il risultato è lo stesso
    /// (CultureSearch). Le persone trovate e il loro ordine devono essere quelli di prima.
    /// </summary>
    static void PeopleSearchAuditTests()
    {
        var accounts = Store.Settings.Accounts;
        var before = accounts.ToList();
        try
        {
            var a = new MailAccount { Kind = AccountKind.Gmail, Email = "mario.prova@posta-prova.example", GoogleScopes = GoogleAuth.ContactsWriteScope };
            var b = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@outlook.example" };
            var c = new MailAccount { Kind = AccountKind.Gmail, Email = "famiglia.prova@posta-prova.example", GoogleScopes = GoogleAuth.ContactsWriteScope };
            accounts.Clear();
            accounts.AddRange([a, b, c]);
            using var ga = new GmailProvider(a);
            var gb = new GraphProvider(b);
            using var gc = new GmailProvider(c);
            var local = ManyContacts(Contact.Local, 300, 1);
            var remote = new Dictionary<IMailProvider, List<Contact>?> { [ga] = ManyContacts(a.Id, 1500, 2), [gb] = ManyContacts(b.Id, 1500, 3), [gc] = ManyContacts(c.Id, 1200, 4) };
            var errors = new Dictionary<IMailProvider, string>();
            var origins = UI.PeopleView.Origins([ga, gb, gc]);
            // Casi che il confronto della lingua tratta a modo suo: devono restare uguali a prima.
            local.Add(new Contact { DisplayName = "Nicolò D'Amico-Verdi", Emails = ["nicolo.damico@massa-prova.example"], Notes = "Riga uno\r\nriga\tdue: co­operativa" }); // "ò" come o più accento, trattino facoltativo
            local.Add(new Contact { DisplayName = "MARIA ROSSI", Company = "Società Caffè & Tè S.p.A.", Mobile = "+39 (333) 12-34-567" });
            local.Add(new Contact { DisplayName = "Gruppo di prova", IsGroup = true, Members = ["Mario Rossi <mario.rossi@massa-prova.example>", "Élise Fontana <elise@massa-prova.example>"] });
            string[] searches = ["m", "ma", "mar", "mario r", "MARIO ROSSI 4", "niccolo", "niccolò", "NICOLÒ", "nicolo", "à", "è", "fiera", "333 1000", "zzz", "studio ne", "d'amico", "damico", "amico-verdi",
                                 "cooperativa", "co-op", "riga", "due:", "caffe", "caffè", "s.p.a", "(333) 12", "12-34", "1234", "élise", "ELISE@", "<mario", "@massa-prova.example", "  ", "é", "Élise", "ß", "ss"];
            var everyone = local.Concat(remote.Values.SelectMany(l => l!)).ToList();
            var different = new List<string>();
            double oldTime = 0, newTime = 0;
            var slowest = (Words: "", Old: 0.0, New: 0.0);
            foreach (var words in searches)
            {
                Contact.TestOldMatch = true;
                var was = UI.PeopleView.BuildEntries(origins, local, remote, errors, words, "");
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 3; i++) UI.PeopleView.BuildEntries(origins, local, remote, errors, words, "");
                double old = clock.Elapsed.TotalMilliseconds / 3;
                Contact.TestOldMatch = false;
                var now = UI.PeopleView.BuildEntries(origins, local, remote, errors, words, "");
                clock.Restart();
                for (int i = 0; i < 3; i++) UI.PeopleView.BuildEntries(origins, local, remote, errors, words, "");
                double fresh = clock.Elapsed.TotalMilliseconds / 3;
                oldTime += old;
                newTime += fresh;
                if (old > slowest.Old) slowest = (words, old, fresh);
                static string Shape(List<object> entries) => string.Join("|", entries.Select(e => e is UI.PeopleView.Row r ? r.Contact.Id : e.ToString()));
                if (Shape(was) != Shape(now)) different.Add(words);
            }
            Check(different.Count == 0 && searches.Length > 30,
                  $"V-16, Persone con {everyone.Count} contatti finti e {searches.Length} ricerche (accenti scritti in due modi, apostrofi, trattini, numeri, gruppi): le stesse persone nello stesso ordine di prima" +
                  (different.Count > 0 ? $" [diverse: {string.Join(", ", different)}]" : "") +
                  $"; a ricerca in media {newTime / searches.Length:0.0} ms invece di {oldTime / searches.Length:0.0} (la più lenta di prima, \"{slowest.Words}\": {slowest.New:0.0} invece di {slowest.Old:0.0})");

            // La via svelta di CultureSearch vale solo se fra caratteri ASCII stampabili il confronto della lingua e quello ordinale
            // dicono lo stesso: provato qui con tutti i caratteri, a coppie e a caso.
            var random = new Random(8);
            var printable = Enumerable.Range(' ', '~' - ' ' + 1).Select(c => (char)c).ToArray();
            var texts = Enumerable.Range(0, 300).Select(_ => new string(Enumerable.Range(0, 5 + random.Next(40)).Select(_ => printable[random.Next(printable.Length)]).ToArray())).ToList();
            texts.Add(new string(printable));
            var needles = printable.Select(c => c.ToString()).Concat(Enumerable.Range(0, 3000).Select(_ =>
            {
                var source = texts[random.Next(texts.Count)];
                int at = random.Next(source.Length), length = Math.Min(1 + random.Next(4), source.Length - at);
                var piece = source.Substring(at, length);
                return random.Next(3) switch { 0 => piece.ToUpperInvariant(), 1 => piece.ToLowerInvariant(), _ => piece };
            })).ToList();
            int wrong = 0, checkedPairs = 0;
            string firstWrong = "";
            foreach (var needle in needles)
                foreach (var text in texts)
                {
                    checkedPairs++;
                    if (CultureSearch.Contains(text, needle) == text.Contains(needle, StringComparison.CurrentCultureIgnoreCase)) continue;
                    if (wrong++ == 0) firstWrong = $"\"{needle}\" in \"{text}\"";
                }
            Check(wrong == 0, $"V-16, la via svelta del confronto (testi di soli caratteri ASCII stampabili) dice lo stesso del confronto della lingua di Windows ({CultureInfo.CurrentCulture.Name}): " +
                              $"{checkedPairs} coppie con ogni carattere, {wrong} diverse{(wrong > 0 ? " (la prima: " + firstWrong + ")" : "")}");
        }
        finally
        {
            accounts.Clear();
            accounts.AddRange(before);
        }
    }

    /// <summary>
    /// v2 V-20, v1 V-11: la copia dei corpi su disco. Prima ogni corpo salvato elencava e ordinava tutta la copia (Trim) sotto
    /// il lucchetto che Forget prende dal thread dell'interfaccia. Ora la pulizia si fa la prima volta, oltre il limite o dopo
    /// un'ora, fuori da quel lucchetto, e Forget non aspetta le scritture. In una cartella temporanea, con 2.000 file finti.
    /// </summary>
    static void BodyCacheAuditTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "missiva-prova-corpi-" + Guid.NewGuid().ToString("N")[..8]);
        BodyCache.TestRoot = root;
        try
        {
            var account = new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Id = "prova-corpi" };
            var folder = Path.Combine(root, "cache", account.Id, "corpi");
            var filler = new byte[2048];
            int made = 0;
            void Fill(int count)
            {
                for (; made < count; made++)
                {
                    var dir = Path.Combine(folder, (made % 12).ToString("x16"));
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(Path.Combine(dir, made.ToString("x24") + ".bin"), filler);
                }
            }
            void Written() => Task.WhenAll(BodyCache.TestWrites.ToArray()).GetAwaiter().GetResult();
            var body = new MessageBody { Html = "<p>Buongiorno Maria, in allegato il preventivo.</p>", To = [new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" }] };
            MessageSummary Mail(int n) => new() { Id = "n" + n, FolderId = "INBOX", Subject = "Pratica " + n, Date = DateTime.Now };
            int next = 0;
            Fill(2000);
            BodyCache.TestForgetSizes(); // come all'avvio: nessun conto in memoria
            var clock = Stopwatch.StartNew();
            BodyCache.Trim(account);
            double trim = clock.Elapsed.TotalMilliseconds;

            // Trenta corpi salvati (tre cartelle aperte, con i dieci corpi in anticipo l'una): nessuna pulizia in più.
            BodyCache.TestForgetSizes();
            int trims = BodyCache.TestTrims;
            for (int i = 0; i < 30; i++) BodyCache.Save(account, Mail(next++), body);
            Written();
            int afterThirty = BodyCache.TestTrims - trims;
            Check(afterThirty == 1 && Enumerable.Range(0, 30).All(i => BodyCache.Has(account, Mail(i))),
                  $"V-20, trenta corpi salvati in una copia di 2.000 file: la pulizia intera ({trim:0.0} ms, elenca e ordina tutti i file) si fa solo all'inizio ({afterThirty} volte; prima 30, una per corpo, sotto il lucchetto di Forget)");

            // Forget dal thread di chi chiama (quello dell'interfaccia: Elimina, Sposta) mentre dieci corpi si scrivono in sottofondo.
            var waits = new List<double>();
            for (int round = 0; round < 20; round++)
            {
                for (int i = 0; i < 10; i++) BodyCache.Save(account, Mail(next++), body);
                Thread.Sleep(round % 4);
                clock.Restart();
                BodyCache.Forget(account, [Mail(next - 20)]);
                waits.Add(clock.Elapsed.TotalMilliseconds);
                Written();
            }
            waits.Sort();
            Check(waits[10] < 10 && !BodyCache.Has(account, Mail(next - 20)) && BodyCache.Has(account, Mail(next - 1)),
                  $"V-20, Forget mentre dieci corpi si scrivono in sottofondo, venti volte: chi chiama aspetta {waits[10]:0.0} ms in mediana e {waits[^1]:0.0} al massimo " +
                  "(prima 26-39 ms in mediana e 46-50 al massimo già con 625 file, 69 e 86 con 5.000), e la copia del messaggio è tolta");

            // Il limite per account vale ancora: oltre, la pulizia toglie i meno recenti; i corpi tolti con Forget si scontano.
            long one = new FileInfo(Directory.GetFiles(Path.Combine(folder), "*.bin", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).First()).Length;
            BodyCache.Trim(account); // il conto esatto, a scritture ferme
            long before = BodyCache.Size(account).Bytes;
            long room = one * 4 + one / 2; // posto per quattro corpi ancora
            BodyCache.TestLimit = before + room;
            trims = BodyCache.TestTrims;
            for (int i = 0; i < 3; i++)
            {
                BodyCache.Save(account, Mail(next++), body);
                Written();
            }
            int under = BodyCache.TestTrims - trims;
            BodyCache.Forget(account, Enumerable.Range(next - 3, 3).Select(Mail)); // tre tolti: il conto scende
            for (int i = 0; i < 3; i++)
            {
                BodyCache.Save(account, Mail(next++), body);
                Written();
            }
            int afterForget = BodyCache.TestTrims - trims;
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(5); // date di scrittura diverse
                BodyCache.Save(account, Mail(next++), body);
                Written();
            }
            long after = BodyCache.Size(account).Bytes;
            Check(under == 0 && afterForget == 0 && BodyCache.TestTrims - trims >= 1 && after <= before + room && BodyCache.Has(account, Mail(next - 1)),
                  $"V-20, il limite per account vale ancora: sotto il limite nessuna pulizia ({under}), i corpi tolti con Forget si scontano ({afterForget}), oltre il limite la pulizia toglie i meno recenti " +
                  $"e resta l'ultimo salvato ({after - before} byte in più su {room} concessi, un corpo {one} byte)");
            BodyCache.TestLimit = BodyCache.DefaultLimit;

            // Dopo un riavvio (nessun conto in memoria) la prima scrittura fa la pulizia: escono i file più vecchi di 30 giorni.
            var old = Directory.GetFiles(folder, "*.bin", SearchOption.AllDirectories).Take(40).ToList();
            foreach (var f in old) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-31));
            BodyCache.Save(account, Mail(next++), body);
            Written();
            bool still = old.All(File.Exists);
            BodyCache.TestForgetSizes();
            BodyCache.Save(account, Mail(next++), body);
            Written();
            Check(still && !old.Any(File.Exists), "V-20, i corpi più vecchi di 30 giorni non escono più a ogni scrittura: escono alla prima scrittura dopo l'avvio (e 30 s dopo l'avvio, e al più ogni ora)");
        }
        finally
        {
            BodyCache.TestRoot = null;
            try { Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>Impostazioni finte della misura di quelle dell'autore (13 KB) o, con big, di anni d'uso (contrassegni, regole, un modello con un logo).</summary>
    static Settings SampleSettings(bool big)
    {
        var s = new Settings
        {
            Accounts = Enumerable.Range(1, 3).Select(n => new MailAccount
            {
                Id = "prova" + n, Kind = AccountKind.Imap, Email = $"mario{n}@studio-prova.example", Imap = new ImapServer { ImapHost = "imap.studio-prova.example", ProtectedPassword = "x" },
            }).ToList(),
            RulesSeen = Enumerable.Range(0, big ? 2000 : 150).Select(n => $"prova1|{n:x8}{n * 7919:x8}").ToList(),
            Favorites = ["prova1|INBOX", "prova2|INBOX"], RecentSearches = ["fattura", "preventivo Rossi"],
            Signatures = { ["prova1"] = new Signature { Rtf = "{\\rtf1 Mario Rossi\\par Studio di prova}" } },
        };
        for (int i = 0; i < (big ? 500 : 12); i++)
            s.Flags.Add(new FlagDate { AccountId = "prova1", Key = "k" + i, MessageId = "m" + i, FolderId = "INBOX", Due = DateTime.Today.AddDays(i % 9), Subject = "Pratica " + i, From = "Maria Rossi" });
        if (big) s.Templates.Add(new MailTemplate { Name = "Preventivo", Subject = "Preventivo del {data}", Rtf = "{\\rtf1 {\\pict " + new string('a', 150_000) + "}}" });
        return s;
    }

    /// <summary>
    /// v2 V-10, v1 V-21: Store.SaveSoon accorpa i salvataggi piccoli e frequenti e scrive fuori dal thread dell'interfaccia;
    /// niente si perde uscendo subito dopo un cambio (Flush), l'ultima istantanea vince, un file che non si può sovrascrivere
    /// resta com'è e un errore non è un'eccezione. Con un SettingsSaver suo in una cartella temporanea, mai quella dei dati.
    /// </summary>
    static async Task SaveSoonAuditAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "missiva-prova-salvataggi-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            int ui = Environment.CurrentManagedThreadId;
            var wait = SettingsSaver.Delay + TimeSpan.FromMilliseconds(400);
            foreach (bool big in new[] { false, true })
            {
                var dir = Path.Combine(root, big ? "grandi" : "normali");
                var settings = SampleSettings(big);
                StoreRecovery? recovery = null;
                var saver = new SettingsSaver(dir, () => settings, () => recovery) { Ui = System.Windows.Threading.Dispatcher.CurrentDispatcher };
                Settings OnDisk() => Store.Read(dir, out _);
                saver.Now(); // il primo cifra i testi (DPAPI): fuori dalla misura, come dopo l'avvio
                long size = new FileInfo(Path.Combine(dir, Store.FileName)).Length;

                // Com'era: venti cambi, venti salvataggi interi sul thread di chi chiama.
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 20; i++)
                {
                    settings.Zoom = 100 + i;
                    saver.Now();
                }
                double before = clock.Elapsed.TotalMilliseconds / 20;
                bool sameThread = saver.LastWriteThread == ui;
                // Com'è: venti cambi di fila (le tacche di Ctrl+rotellina), una scrittura sola, dopo, su un altro thread.
                int writes = saver.Writes;
                clock.Restart();
                for (int i = 0; i < 20; i++)
                {
                    settings.Zoom = 130 + i;
                    saver.Soon();
                }
                double after = clock.Elapsed.TotalMilliseconds / 20;
                bool notYet = saver.Writes == writes && OnDisk().Zoom == 119;
                clock.Restart();
                var snapshot = Store.Snapshot(settings, out _);
                double take = clock.Elapsed.TotalMilliseconds;
                await Task.Delay(wait);
                Check(sameThread && notYet && saver.Writes == writes + 1 && saver.LastWriteThread != ui && OnDisk().Zoom == 149 && snapshot != null,
                      $"V-10, impostazioni {(big ? "grandi" : "come quelle di oggi")} ({size / 1024} KB): venti cambi di fila con SaveSoon fanno una scrittura sola, dopo {SettingsSaver.Delay.TotalMilliseconds:0} ms e su un altro thread " +
                      $"(prima {before:0.00} ms a cambio sul thread dell'interfaccia; ora {after:0.000} ms a cambio, più {take:0.00} ms una volta per l'istantanea)");
                if (big) continue;

                // Uscire subito dopo un cambio: Flush (uscita, fine sessione, area di notifica) lo porta sul disco senza aspettare.
                settings.Zoom = 77;
                settings.CollapsedAccounts.Add("prova2");
                saver.Soon();
                writes = saver.Writes;
                saver.Flush();
                var closed = OnDisk();
                saver.Flush(); // niente in attesa: non riscrive
                await Task.Delay(wait); // e il timer rimasto non riscrive quello che Flush ha già scritto
                Check(closed.Zoom == 77 && closed.CollapsedAccounts.Contains("prova2") && saver.Writes == writes + 1,
                      "V-10, uscita subito dopo un cambio: quello che SaveSoon aspettava di scrivere è sul disco al ritorno di Flush, una volta sola");

                // Un salvataggio accorpato che non riesce (il file temporaneo in uso da un altro programma): nessuna eccezione, un
                // avviso solo, e all'uscita Flush riprova e ci riesce.
                int told = 0;
                saver.Failed += _ => Interlocked.Increment(ref told);
                using (new FileStream(Path.Combine(dir, Store.FileName + ".tmp"), FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    settings.Zoom = 88;
                    saver.Soon();
                    await Task.Delay(wait);
                    settings.Zoom = 89;
                    saver.Soon();
                    await Task.Delay(wait);
                }
                bool failed = told == 1 && OnDisk().Zoom == 77;
                saver.Flush();
                Check(failed && OnDisk().Zoom == 89, $"V-10, un salvataggio accorpato che non riesce due volte: nessuna eccezione, un avviso solo ({told}), il file resta quello di prima, e all'uscita Flush riprova e lo scrive");

                // L'ultima vince: salvataggi subito da altri thread mescolati a quelli accorpati, poi l'uscita.
                var others = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
                {
                    for (int i = 0; i < 25; i++)
                    {
                        if (i % 4 != 3) saver.Now(); else saver.Soon();
                    }
                })).ToArray();
                for (int i = 0; i < 60; i++)
                {
                    settings.Zoom = 50 + i; // le impostazioni cambiano solo su questo thread, come nell'app
                    saver.Soon();
                    await Task.Delay(5);
                }
                await Task.WhenAll(others);
                settings.Zoom = 199;
                saver.Soon();
                saver.Flush();
                int last = OnDisk().Zoom;
                await Task.Delay(wait);
                bool mixed = last == 199 && OnDisk().Zoom == 199 && !File.Exists(Path.Combine(dir, Store.FileName + ".tmp"));
                // E un'istantanea presa prima che arriva al disco dopo una più nuova (lo scrittore in sottofondo in ritardo): saltata.
                settings.Zoom = 60;
                var late = saver.Take();
                settings.Zoom = 61;
                saver.Now();
                int skipped = saver.Skipped;
                bool said = saver.Put(late.Json, late.Number, late.Error, "in ritardo");
                Check(mixed && said && saver.Skipped == skipped + 1 && OnDisk().Zoom == 61,
                      "V-10, salvataggi subito da quattro thread mescolati a quelli accorpati: sul disco resta l'ultimo stato; un'istantanea più vecchia che arriva dopo una più nuova non la sovrascrive");

                // Le garanzie del blocco 5 valgono anche per i salvataggi accorpati: copia di riserva, e un file che all'avvio non
                // si leggeva né si spostava non viene mai sovrascritto.
                var previous = File.ReadAllBytes(Path.Combine(dir, Store.FileName));
                settings.Zoom = 120;
                saver.Soon();
                await Task.Delay(wait);
                bool kept = File.ReadAllBytes(Path.Combine(dir, Store.BackupName)).SequenceEqual(previous) && OnDisk().Zoom == 120;
                var untouched = File.ReadAllBytes(Path.Combine(dir, Store.FileName));
                recovery = new StoreRecovery("in uso", null, null, null, Blocked: true, Path.Combine(dir, Store.FileName));
                settings.Zoom = 150;
                saver.Soon();
                await Task.Delay(wait);
                saver.Flush();
                Check(kept && File.ReadAllBytes(Path.Combine(dir, Store.FileName)).SequenceEqual(untouched),
                      "V-10, salvataggi accorpati: il file di prima resta come impostazioni.bak, e con le impostazioni bloccate all'avvio (in uso da un altro programma) il file non si tocca, nemmeno all'uscita");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>
    /// v2 V-12, v1 V-17: un messaggio nuovo in cima non rifà più i gruppi dell'elenco se il suo gruppo c'è già. Vale solo
    /// se WPF, da solo, lascia l'elenco raggruppato com'è dopo averli rifatti: qui si confrontano le due forme.
    /// </summary>
    static void ArrivalGroupsAuditTests()
    {
        var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer() }));
        MessageSummary Mail(string id, double daysAgo, string conversation = "") => new()
        {
            Id = id, FolderId = "INBOX", Subject = "Pratica " + id, Date = DateTime.Today.AddHours(12).AddDays(-daysAgo), ConversationId = conversation,
            From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" },
        };
        (UI.MessageRows Rows, System.Windows.Data.ListCollectionView View) List(params MessageSummary[] messages)
        {
            var rows = new UI.MessageRows();
            var view = (System.Windows.Data.ListCollectionView)System.Windows.Data.CollectionViewSource.GetDefaultView(rows.Rows);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(UI.MessageItem.Group)));
            rows.Append(messages, account);
            view.Refresh();
            return (rows, view);
        }
        static string Shape(System.Windows.Data.ListCollectionView view) => string.Join(" | ", view.Groups.Cast<System.Windows.Data.CollectionViewGroup>()
            .Select(g => g.Name + ": " + string.Join(",", g.Items.Cast<UI.MessageItem>().Select(i => i.Summary.Id))));
        bool grouped = Store.Settings.GroupByConversation;
        try
        {
            Store.Settings.GroupByConversation = true;
            // Il gruppo di oggi c'è già: due arrivi, il secondo in una conversazione di ieri (la riga sale in cima, fra quelle di oggi).
            var (rows, view) = List(Mail("1", 0), Mail("2", 0.1), Mail("3", 1, "c"), Mail("4", 1.1), Mail("5", 9));
            var today = view.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(g => g.Name as string).ToHashSet();
            var added = rows.Prepend([Mail("6", -0.01), Mail("7", -0.02, "c")], account);
            bool known = added.All(a => today.Contains(a.Group));
            string alone = Shape(view);
            view.Refresh();
            string redone = Shape(view);
            // Il gruppo di oggi non c'è (elenco fermo a ieri): la riga apre un gruppo nuovo, e lì i gruppi si rifanno come prima.
            var (oldRows, oldView) = List(Mail("3", 1), Mail("4", 1.1), Mail("5", 9));
            var yesterday = oldView.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(g => g.Name as string).ToHashSet();
            var late = oldRows.Prepend([Mail("6", -0.01)], account);
            string before = Shape(oldView);
            oldView.Refresh();
            Check(known && alone == redone && alone.StartsWith("Oggi: 7,6,1,2 | ") && late.Any(a => !yesterday.Contains(a.Group)) && Shape(oldView).StartsWith("Oggi: 6 | "),
                  $"V-12, un messaggio nuovo in un gruppo che c'è già: senza rifare i gruppi l'elenco è uguale a quello rifatto ({alone}); una riga che apre un gruppo nuovo si riconosce, e lì i gruppi si rifanno " +
                  $"(senza rifarli: {before})");
        }
        finally
        {
            Store.Settings.GroupByConversation = grouped;
        }
    }

    /// <summary>
    /// Come nell'app: dopo ogni await il lavoro riprende su questo thread (quello dell'interfaccia), con un Dispatcher che
    /// gira finché non ha finito. Senza, nelle prove ogni ripresa cade su un thread del pool e non si vede che cosa
    /// resterebbe sul thread dell'interfaccia.
    /// </summary>
    static void OnUiThread(Func<Task> work)
    {
        var before = SynchronizationContext.Current;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
        try
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var task = work();
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    /// <summary>
    /// v2 V-08: con più regole sul testo, il testo di ogni messaggio (un'analisi intera dell'HTML) si ricalcolava per ogni
    /// regola, sul thread di chi chiama (quello dell'interfaccia). Ora una volta per messaggio, su un thread del pool.
    /// </summary>
    static async Task RulesTextAuditAsync(FakeImap fake)
    {
        ImapProvider.TestPlain = true;
        try
        {
            using var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            for (int k = 1; k <= 4; k++) fake.Add("INBOX", SpeedMail("Pratica " + k, k == 4 ? "Solo un saluto" : "Il preventivo numero " + k));
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var messages = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            var rules = new List<MailRule>
            {
                new() { Name = "Fatture", BodyContains = ["fattura"], Flag = true },
                new() { Name = "Solleciti", BodyContains = ["sollecito", "scadenza"], MarkRead = true },
                new() { Name = "Preventivi", BodyContains = ["preventivo"], Flag = true },
            };
            Rules.TextReads = 0;
            int caller = Environment.CurrentManagedThreadId;
            var outcome = await Rules.RunAsync(imap, messages, rules, () => imap.GetFoldersAsync());
            int reads = Rules.TextReads;
            bool elsewhere = Rules.TextThread != caller;
            // Una regola che cade già sul mittente non fa calcolare il testo (com'era).
            Rules.TextReads = 0;
            var other = await Rules.RunAsync(imap, messages, [new() { Name = "Altro mittente", SenderContains = ["nessuno-prova"], BodyContains = ["preventivo"], MarkRead = true }], () => imap.GetFoldersAsync());
            Check(messages.Count == 4 && outcome.Matched == 3 && outcome.Flagged.Count == 3 && reads == 4 && elsewhere && other.Matched == 0 && Rules.TextReads == 0,
                  $"V-08, tre regole sul testo e quattro messaggi: il testo di ogni messaggio si calcola una volta ({reads} volte per {messages.Count} messaggi e {rules.Count} regole), " +
                  $"fuori dal thread di chi chiama ({elsewhere}); le regole trovano gli stessi messaggi ({outcome.Matched} su 4); una regola che cade sul mittente non lo calcola ({Rules.TextReads})");
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
    }

    /// <summary>
    /// v1 V-04, v2 V-13: Elimina, Archivia e Sposta ora scelgono la riga successiva prima di togliere le altre
    /// (MessageRows.After). Deve essere la stessa riga che prima restava scelta dopo averle tolte.
    /// </summary>
    static void NextRowAuditTests()
    {
        bool grouped = Store.Settings.GroupByConversation;
        try
        {
            Store.Settings.GroupByConversation = true;
            var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer() }));
            UI.MessageRows Rows()
            {
                // 14 righe: due conversazioni di tre messaggi (la prima aperta: testa e due figli) e messaggi soli.
                var rows = new UI.MessageRows();
                rows.Append(Enumerable.Range(0, 16).Select(k => new MessageSummary
                {
                    Id = k.ToString(), FolderId = "INBOX", Subject = "Pratica " + k, Date = DateTime.Today.AddHours(-k),
                    ConversationId = k is 2 or 3 or 4 ? "a" : k is 9 or 10 or 11 ? "b" : "",
                    From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" },
                }), account);
                rows.Toggle(rows.Rows[2]);
                return rows;
            }
            // Com'era: tolte le righe, quella al posto della prima tolta, o l'ultima.
            static UI.MessageItem? Before(UI.MessageRows rows, List<UI.MessageItem> items)
            {
                int index = items.Select(rows.Rows.IndexOf).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
                foreach (var i in items) rows.Remove(i);
                return index >= 0 && rows.Rows.Count > 0 ? rows.Rows[Math.Min(index, rows.Rows.Count - 1)] : null;
            }
            var random = new Random(4);
            int same = 0, cases = 0, none = 0;
            var wrong = new List<string>();
            for (int round = 0; round < 400; round++)
            {
                var rows = Rows();
                int count = round < 14 ? 1 : random.Next(1, rows.Rows.Count + 1);
                var items = round < 14 ? [rows.Rows[round]] : rows.Rows.OrderBy(_ => random.Next()).Take(count).ToList();
                var picked = string.Join(",", items.Select(rows.Rows.IndexOf).Order());
                var next = rows.After(items);
                bool stays = next == null || !items.Contains(next);
                var before = Before(rows, items);
                cases++;
                if (next == null) none++;
                if (ReferenceEquals(next, before) && stays && (next == null || rows.Rows.Contains(next))) same++;
                else if (wrong.Count < 3) wrong.Add(picked);
            }
            var whole = Rows();
            var outside = new UI.MessageItem(new MessageSummary { Id = "fuori", FolderId = "INBOX" }, account);
            Check(same == cases && none > 0 && Rows().Rows.Count == 14 && whole.After([outside]) == null && whole.After([]) == null
                  && whole.After([whole.Rows[2]]) == whole.Rows[5] && whole.After([whole.Rows[13]]) == whole.Rows[12],
                  $"V-04/V-13, la riga scelta prima di togliere le altre è la stessa che prima restava scelta dopo: {same} casi su {cases} (ogni riga da sola, 386 scelte a caso, " +
                  $"una conversazione aperta che esce con i figli, tutte le righe: {none} volte nessuna){(wrong.Count > 0 ? "; diversi: " + string.Join(" | ", wrong) : "")}");
        }
        finally
        {
            Store.Settings.GroupByConversation = grouped;
        }
    }

    /// <summary>
    /// v2 V-06: con un account IMAP ogni salvataggio di una bozza con allegati li riscaricava tutti dalla bozza di prima
    /// (la struttura e poi ogni parte) prima di riscriverli. Ora, come in Gmail, quelli sotto 5 MB restano in memoria.
    /// </summary>
    static async Task ImapDraftAuditAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        try
        {
            using var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            var text = string.Concat(Enumerable.Range(0, 2000).Select(k => $"riga {k} del preventivo di prova\n"));
            var file = TempFile("preventivo-prova.txt", text);
            var note = TempFile("nota-prova.txt", "nota di prova");
            var first = await imap.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Bozza con allegati", Html = "<p>Uno</p>", Text = "Uno", AttachmentPaths = [file, note] });
            fake.Commands.Clear();
            var second = await imap.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Bozza con allegati", Html = "<p>Due</p>", Text = "Due",
                                                                       Draft = first, KeptAttachments = first.Attachments });
            var fetches = fake.Commands.Where(c => c.Contains("BODY.PEEK[")).ToList();
            int structures = fake.Commands.Count(c => c.StartsWith("UID FETCH") && c.Contains("BODYSTRUCTURE"));
            Report.AddRange(fake.Commands.Where(c => c.StartsWith("UID FETCH") || c.StartsWith("APPEND")).Select(c => "      " + (c.Length > 120 ? c[..120] + "…" : c)));
            string Stored(int k)
            {
                using var content = new MemoryStream();
                ((MimeKit.MimePart)MimeKit.MimeMessage.Load(new MemoryStream(fake.Stored.Single().Raw)).Attachments.ElementAt(k)).Content!.DecodeTo(content);
                return Encoding.UTF8.GetString(content.ToArray());
            }
            // Dopo APPEND resta la sola lettura della bozza nuova (busta e struttura): nessuna parte riscaricata.
            Check(fetches.Count == 0 && structures == 1 && fake.Stored.Count == 1 && Stored(0) == text && Stored(1) == "nota di prova"
                  && second.Attachments.Select(a => a.Name).SequenceEqual(["preventivo-prova.txt", "nota-prova.txt"]) && second.Attachments.All(a => a.Content != null),
                  $"V-06, account IMAP, secondo salvataggio di una bozza con due allegati: nessuna parte riscaricata ({fetches.Count} FETCH di parti, {structures} di struttura), una sola bozza, allegati uguali");

            // Una bozza ripresa dalle Bozze ha gli allegati solo sul server: lì si riscaricano, e restano uguali.
            fake.Commands.Clear();
            var resumed = new SavedDraft { Message = second.Message, Attachments = second.Attachments.Select(a => new Attachment { Id = a.Id, Name = a.Name, ContentType = a.ContentType, Size = a.Size }).ToList() };
            var third = await imap.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Bozza con allegati", Html = "<p>Tre</p>", Text = "Tre", Draft = resumed, KeptAttachments = resumed.Attachments });
            int again = fake.Commands.Count(c => c.Contains("BODY.PEEK["));
            bool once = again == 2 && fake.Stored.Count == 1 && Stored(0) == text && Stored(1) == "nota di prova";
            // E dopo un salvataggio con gli allegati in memoria, tolto il primo: resta solo il secondo, uguale.
            fake.Commands.Clear();
            await imap.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Bozza con allegati", Html = "<p>Quattro</p>", Text = "Quattro", Draft = third, KeptAttachments = [third.Attachments[1]] });
            Check(once && !fake.Commands.Any(c => c.Contains("BODY.PEEK[")) && fake.Stored.Count == 1 && Stored(0) == "nota di prova",
                  $"V-06, bozza ripresa dalle Bozze: i due allegati si riscaricano una volta e restano uguali ({again} FETCH di parti); tolto poi il primo, resta il secondo senza riscaricarlo");
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
    }

    /// <summary>
    /// v2 V-05, v1 V-09: segnare letti (o da leggere) molti messaggi cambiava il conteggio della cartella una volta per
    /// messaggio, e a ogni cambio della Posta in arrivo si rifacevano icona, area di notifica e riga di registro.
    /// </summary>
    static void ReadCountAuditTests()
    {
        var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer() }));
        static MailFolder Folder(string id, FolderKind kind, int unread) => new() { Id = id, Name = id, Kind = kind, Unread = unread };
        account.SetFolders(new[] { Folder("INBOX", FolderKind.Inbox, 60), Folder("Clienti", FolderKind.Other, 4) });
        account.IsExpanded = false; // il contatore sull'intestazione si vede con l'account chiuso
        Store.Settings.CollapsedAccounts.Remove(account.Provider.Account.Id); // la scelta non resta fra le impostazioni di prova
        var inbox = account.Folders.First(f => f.Folder.Kind == FolderKind.Inbox);
        var other = account.Folders.First(f => f.Folder.Id == "Clienti");
        int inboxChanges = 0, otherChanges = 0, headerChanges = 0;
        inbox.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(UI.FolderItem.Unread)) inboxChanges++; };
        other.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(UI.FolderItem.Unread)) otherChanges++; };
        account.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(UI.AccountItem.HeaderUnreadText)) headerChanges++; };
        var messages = Enumerable.Range(1, 50).Select(k => new MessageSummary { Id = k.ToString(), FolderId = "INBOX" })
            .Concat(Enumerable.Range(51, 3).Select(k => new MessageSummary { Id = k.ToString(), FolderId = "Clienti" }))
            .Append(new MessageSummary { Id = "99", FolderId = "Sparita" }).ToList();
        account.CountRead(messages, read: true);
        bool down = inbox.Unread == 10 && other.Unread == 1 && account.InboxUnread == 10 && account.HeaderUnreadText == "10" && inboxChanges == 1 && otherChanges == 1 && headerChanges == 1;
        account.CountRead(messages, read: false);
        bool up = inbox.Unread == 60 && other.Unread == 4 && account.HeaderUnreadText == "60" && inboxChanges == 2 && headerChanges == 2;
        account.CountRead(messages.Concat(messages).ToList(), read: true); // più letti di quanti il server ne contava: mai sotto zero
        Check(down && up && inbox.Unread == 0 && other.Unread == 0 && account.HeaderUnreadText == "" && inboxChanges == 3,
              $"V-05, 50 messaggi della Posta in arrivo segnati letti e poi da leggere: il conteggio della cartella cambia una volta per azione ({inboxChanges} cambi per 3 azioni), " +
              "il contatore sull'intestazione dell'account resta giusto (60, 10, 60, nessuno) e non scende sotto zero");

        var accent = Color.FromRgb(0x00, 0x5F, 0xB8);
        var seven = UI.MainWindow.BadgeImage("7", accent);
        Check(ReferenceEquals(seven, UI.MainWindow.BadgeImage("7", accent)) && !ReferenceEquals(seven, UI.MainWindow.BadgeImage("8", accent))
              && !ReferenceEquals(seven, UI.MainWindow.BadgeImage("7", Color.FromRgb(0xC4, 0x2B, 0x1C))) && seven.IsFrozen && seven.Width == 32,
              "V-05, l'immagine del numero sull'icona si disegna una volta per testo e colore");
    }

    /// <summary>Un account sul server IMAP finto (senza TLS, solo verso 127.0.0.1).</summary>
    static MailAccount SpeedAccount(FakeImap fake, string email) => new()
    {
        Kind = AccountKind.Imap, Email = email,
        Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = fake.Port, SmtpHost = "127.0.0.1", SmtpPort = 1, UserName = email, ProtectedPassword = Store.Protect("x") },
    };

    static MimeKit.MimeMessage SpeedMail(string subject, string text, int daysAgo = 0)
    {
        var mime = new MimeKit.MimeMessage { Subject = subject, Date = DateTimeOffset.Now.AddDays(-daysAgo), Body = new MimeKit.TextPart("html") { Text = $"<p>{text}</p>" } };
        mime.From.Add(new MimeKit.MailboxAddress("Maria Rossi", "maria.rossi@fornitori-prova.example"));
        mime.To.Add(new MimeKit.MailboxAddress("Mario Rossi", "mario@studio-prova.example"));
        return mime;
    }

    /// <summary>
    /// v1 V-01, v2 V-18: le strutture dei messaggi elencati (known) si svuotavano tutte alla voce 1001. Da lì un messaggio
    /// già copiato sul PC non si apriva più dalla copia (SavedBody) ma dal server, con una FETCH in più per la struttura.
    /// </summary>
    static async Task KnownCacheAuditAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        for (int k = 1; k <= 3; k++) fake.Add("INBOX", SpeedMail("Preventivo " + k, "Il preventivo numero " + k));
        for (int k = 1; k <= 1100; k++) fake.Add(fake.UserFolder, SpeedMail("Pratica " + k, "Pratica " + k, 60));
        MailCache.Delete(account);
        try
        {
            using (var first = new ImapProvider(account))
            {
                var box = (await first.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
                await OfflineCopy.RunAsync(first, box, CancellationToken.None);
                Task.WaitAll(BodyCache.TestWrites.ToArray());
            }
            using var imap = new ImapProvider(account); // senza corpi in memoria: come dopo un riavvio
            var folders = await imap.GetFoldersAsync();
            var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
            var other = folders.First(f => f.Id == fake.UserFolder);
            var rows = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            bool before = rows.Count == 3 && rows.All(m => imap.SavedBody(m) != null);
            // 22 pagine di un'altra cartella: 1100 strutture nuove.
            int listed = 0;
            string? next = null;
            do
            {
                var page = await imap.GetMessagesAsync(other, null, next);
                listed += page.Messages.Count;
                next = page.Next;
            } while (next != null);
            int saved = rows.Count(m => imap.SavedBody(m) != null);
            fake.Commands.Clear();
            var body = imap.SavedBody(rows[0]) ?? await imap.GetBodyAsync(rows[0]);
            int fetches = fake.Commands.Count(c => c.StartsWith("UID FETCH"));
            Check(before && listed == 1100 && saved == 3 && fetches == 0 && body.Html.Contains("preventivo numero"),
                  $"V-01/V-18, dopo {listed} messaggi elencati in un'altra cartella i 3 già sul PC si aprono ancora dalla copia su disco (prima {before}, poi {saved} su 3 dalla copia, {fetches} UID FETCH al clic)");

            // Oltre il tetto esce solo la voce elencata o letta da più tempo.
            var cache = new SummaryCache(3);
            static MailKit.IMessageSummary Entry(int n) => new MailKit.MessageSummary(n);
            cache["a"] = Entry(1);
            cache["b"] = Entry(2);
            cache["c"] = Entry(3);
            bool read = cache.TryGetValue("a", out var a) && a.Index == 1; // letta: ora la più vecchia è b
            cache["d"] = Entry(4);
            cache["c"] = Entry(5); // elencata di nuovo: una voce sola, con la struttura nuova
            cache["e"] = Entry(6); // esce a
            Check(read && cache.Count == 3 && !cache.TryGetValue("b", out _) && !cache.TryGetValue("a", out _) && cache.TryGetValue("c", out var c) && c.Index == 5
                  && cache.TryGetValue("d", out _) && cache.TryGetValue("e", out _) && SummaryCache.Limit >= 1000,
                  $"V-01/V-18, oltre il tetto ({SummaryCache.Limit} voci) esce una voce sola, quella elencata o letta da più tempo; le altre restano");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            MailCache.Delete(account);
        }
    }

    /// <summary>
    /// Audit, blocco 3: gli avvisi su chi scrive davvero (SenderCheck, AddressBook.LookAlike, il nome mostrato, le immagini
    /// nella Posta indesiderata), con intestazioni e mittenti costruiti apposta. Solo dati finti.
    /// </summary>
    static void SenderAuditTests()
    {
        Report.Add("--- Audit, blocco 3: chi scrive davvero, con intestazioni e mittenti costruiti apposta");
        AuthResultsAuditTests();
        LookAlikeAuditTests();
        ShownNameAuditTests();
        JunkImagesAuditTests();
    }

    /// <summary>
    /// F-12: le immagini esterne si caricavano da sole anche per un messaggio nella Posta indesiderata, se il suo From era
    /// di un mittente consentito con "Mostra immagini" (vero o falsificato): chi lo manda sapeva che era stato letto.
    /// </summary>
    static void JunkImagesAuditTests()
    {
        const string shop = "negozio-prova.example";
        var before = Store.Settings.ImageSenders;
        try
        {
            Store.Settings.ImageSenders = [];
            Store.Settings.AllowImagesFrom("notizie@" + shop);
            static MessageSummary Mail(string folder, FolderKind? label = null) =>
                new() { Id = "1", FolderId = folder, LabelKind = label, Subject = "Offerte", Date = DateTime.Now, From = new Address { Name = "Negozio di prova", Email = "offerte@posta." + shop } };
            var body = new MessageBody { Html = $"<p>Offerte</p><img src=\"https://immagini.{shop}/a.png\" width=\"200\" height=\"100\" alt=\"Offerta\">" };
            bool inbox = UI.ReadingView.LoadsImages(Mail("INBOX"), body, "Spam") && UI.ReadingView.LoadsImages(Mail("INBOX"), body, null);
            bool junk = UI.ReadingView.LoadsImages(Mail("Spam"), body, "Spam"), label = UI.ReadingView.LoadsImages(Mail("Tutti i messaggi", FolderKind.Junk), body, null);
            bool once = UI.ReadingView.LoadsImages(Mail("Spam"), body, "Spam", once: true);
            // Una busta di posta certificata: decide il mittente vero (ImagesFrom), ma non nella Posta indesiderata.
            var envelope = new MessageSummary { Id = "2", FolderId = "Spam", From = new Address { Email = "posta-certificata@pec-prova.example" } };
            bool certified = UI.ReadingView.LoadsImages(envelope, new MessageBody { Html = body.Html, ImagesFrom = "notizie@" + shop }, "Spam");
            Check(inbox && !junk && !label && !certified && once,
                  $"F-12, un mittente consentito: le immagini si caricano da sole nella Posta in arrivo ({inbox}), non nella Posta indesiderata (cartella {junk}, etichetta {label}, posta certificata {certified}); " +
                  $"lì solo dopo \"Mostra immagini\" ({once})");

            UI.ReadingPage Page(string folder, bool allow, bool isJunk) =>
                UI.ReadingPage.Build([new UI.PageEntry(Mail(folder), body, true, false, null, allow, Junk: isJunk)], light: true, header: new UI.PageHeader("Offerte", false));
            Store.Settings.ImageSenders = [];
            UI.ReadingPage blocked = Page("Spam", false, true), shown = Page("Spam", true, true), other = Page("INBOX", false, false);
            Check(blocked.Blocked[0] == 1 && !blocked.RemoteAllowed && !blocked.Html.Contains("https://immagini." + shop) && blocked.Html.Contains("Mostra immagini")
                  && blocked.Html.Contains("solo per questa volta") && !blocked.Html.Contains("in tutti gli account")
                  && shown.Blocked[0] == 0 && shown.RemoteAllowed && shown.Html.Contains($"https://immagini.{shop}/a.png")
                  && other.Html.Contains($"per {shop}, in tutti gli account") && !other.Html.Contains("solo per questa volta"),
                  "F-12, nella pagina di un messaggio della Posta indesiderata l'immagine è bloccata e \"Mostra immagini\" dice che vale solo per quella volta; altrove dice che ricorderà il mittente");
        }
        finally
        {
            Store.Settings.ImageSenders = before;
        }
    }

    /// <summary>
    /// S-25: un nome mostrato che contiene un indirizzo diverso da quello vero ("assistenza@banca" &lt;x@altro&gt;) non dava
    /// nessun avviso, e in elenco, notifica e messaggi chiusi si vedeva solo quello; i caratteri invisibili e i cambi di
    /// direzione del testo restavano nei nomi mostrati.
    /// </summary>
    static void ShownNameAuditTests()
    {
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "ada.prova@gmail.com" };
        var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "studio@uno.example", Imap = new ImapServer() }));
        const string claimed = "assistenza@banca-prova.example", real = "x@dominio-ostile.example", shop = "negozio-prova.example";
        static MessageSummary Mail(string name, string email, string id = "1") =>
            new() { Id = id, FolderId = "INBOX", Subject = "Avviso", Date = DateTime.Now, From = new Address { Name = name, Email = email }, Preview = "Anteprima di prova" };
        var body = new MessageBody { Html = "<p>Prova</p>", To = [new Address { Name = "Ada", Email = "ada.prova@gmail.com" }] };
        // La pagina del riquadro per un messaggio aperto (con le note sul mittente, come ReadingView) o chiuso in una conversazione.
        string Page(MessageSummary m, bool open) => UI.ReadingPage.Build(
        [
            new UI.PageEntry(m, open ? body : null, open, false, null, false, open ? SenderCheck.Of(gmail, m, body) : null),
            new UI.PageEntry(Mail("Maria Rossi", "maria.rossi@example.com", "2"), body, true, false, null, false),
        ], light: true).Html;
        static string Bands(string html) => string.Concat(System.Text.RegularExpressions.Regex.Matches(html, "<div class=\"warn\"[^>]*>.*?</div>").Select(m => m.Value));
        static string Head(string html) => html[html.IndexOf("<article id=\"m0\"")..html.IndexOf("<article id=\"m1\"")];

        var fake = Mail(claimed, real);
        string open = Page(fake, true), closed = Page(fake, false);
        Check(Bands(open).Contains(claimed) && Bands(open).Contains(real) && Head(open).Contains($"<b>{real}</b>") && !Head(open).Contains($"<b>{claimed}</b>"),
              $"S-25, nel riquadro un nome che è l'indirizzo di un altro dà la fascia gialla con tutti e due, e in grassetto c'è l'indirizzo vero ({(Bands(open).Length == 0 ? "nessuna fascia" : "fascia presente")})");
        var row = new UI.MessageItem(fake, account);
        Check(row.Sender == real && row.ToString().Contains(real) && !row.ToString().Contains(claimed) && fake.From.Display == real
              && Head(closed).Contains($"<b>{real}</b>") && !Head(closed).Contains(claimed) && SenderCheck.QuickReplyRisk(gmail, fake) != null,
              $"S-25, in elenco, nella notifica e nel messaggio chiuso di una conversazione si vede l'indirizzo vero (riga: \"{row.Sender}\"), e dalla notifica non si risponde senza vederlo");
        // L'elenco è il percorso più caldo: quello che la riga mostra si calcola una volta, non a ogni disegno.
        var sent = new UI.MessageItem(new MessageSummary
        {
            Id = "3", FolderId = "Inviata", LabelKind = FolderKind.Sent, Subject = "Documenti", Date = DateTime.Now, From = new Address { Name = "Ada", Email = "ada.prova@gmail.com" },
            To = [new Address { Name = "Mario Rossi", Email = "mario.rossi@example.com" }, new Address { Name = claimed, Email = real }],
        }, account);
        Check(ReferenceEquals(row.Sender, row.Sender) && (ReferenceEquals(row.Line1, row.Sender) || ReferenceEquals(row.Line2, row.Sender)) && ReferenceEquals(sent.Sender, sent.Sender) && sent.Sender == "Mario Rossi; " + real
              && ReferenceEquals(fake.From.Display, fake.From.Display) && ReferenceEquals(fake.From.Display, fake.From.Email),
              "S-25, il testo del mittente della riga (anche i destinatari in Posta inviata) è lo stesso oggetto a ogni lettura: calcolato una volta per riga");

        // Le forme: fra parentesi, solo il dominio dopo la chiocciola, la chiocciola a larghezza intera, uno spazio a
        // larghezza zero in mezzo, il sito scritto per esteso, un altro indirizzo dello stesso servizio di posta personale.
        string[] forms =
        [
            $"Assistenza ({claimed})", "Assistenza @banca-prova.example", "assistenza＠banca-prova.example", "assistenza​@banca-prova.example", "<" + claimed + ">",
            "www.banca-prova.example", "Banca-prova.example Assistenza clienti",
        ];
        var missed = forms.Where(f => Mail(f, real).From.Display != real).ToList();
        Check(missed.Count == 0 && Mail("mario.rossi-prova@gmail.com", "mario.rossi.studio-prova@gmail.com").From.Display == "mario.rossi.studio-prova@gmail.com",
              $"S-25, l'indirizzo di un altro nel nome si riconosce in {forms.Length} forme, e dentro la posta personale conta l'indirizzo intero" + (missed.Count == 0 ? "" : ": non riconosciuto in " + string.Join(" | ", missed)));

        // Chi non inganna resta com'è: il nome che è il proprio indirizzo, un indirizzo o il dominio della stessa organizzazione, i nomi con un punto.
        (string Name, string Email)[] honest =
        [
            ($"servizio@{shop}", $"servizio@{shop}"), ($"Servizio@{shop}", $"servizio@{shop}"), ($"assistenza@{shop}", $"ticket+123@posta.{shop}"),
            ($"Negozio-prova.example", $"notizie@posta.{shop}"), ("Mario Rossi", "mario.rossi@example.com"), ("Dr.Rossi", "studio@example.com"), ("Mario.Rossi", "mario@example.com"),
            ("Rossi S.p.A.", "info@example.com"), ("Maria Rossi (Studio Rossi)", "maria@example.com"), ("mario.rossi-prova", "mario.rossi-prova@gmail.com"),
        ];
        var wrong = honest.Where(h => Mail(h.Name, h.Email).From.Display != h.Name || Bands(Page(Mail(h.Name, h.Email), true)).Contains("si presenta come")).Select(h => h.Name).ToList();
        string plain = Page(Mail("Mario Rossi", "mario.rossi@example.com"), true);
        Check(wrong.Count == 0 && Head(plain).Contains("<b>Mario Rossi</b>") && Head(plain).Contains("&lt;mario.rossi@example.com&gt;"),
              $"S-25, {honest.Length} nomi che non ingannano (il proprio indirizzo, la stessa organizzazione, nomi con un punto) restano com'erano, senza fascia" + (wrong.Count == 0 ? "" : ": cambiati " + string.Join(" | ", wrong)));

        // I caratteri che non si vedono escono dai nomi, in un punto solo (Address.Name); gli altri restano.
        var hidden = new Address { Name = "‮Mario​ Ros­si⁦⁩﻿ \t\r\n", Email = "mario.rossi@example.com" };
        var kept = new Address { Name = "Caffè שלום \U0001F389️  Offerte", Email = "notizie@example.com" };
        Check(hidden.Name == "Mario Rossi" && hidden.Display == "Mario Rossi" && hidden.ToString() == "Mario Rossi <mario.rossi@example.com>"
              && kept.Name == "Caffè שלום \U0001F389️ Offerte" && new Address { Email = "a@example.com" }.Name == "",
              $"S-25, cambi di direzione del testo, caratteri a larghezza zero e a capo escono dal nome mostrato ovunque (\"{string.Concat(hidden.Name.Select(c => c < 0x20 || c > 0x2000 && c < 0x2100 || c == 0xFEFF || c == 0xAD ? $"[{(int)c:X4}]" : c.ToString()))}\"); accenti, altri alfabeti ed emoji restano");
    }

    /// <summary>
    /// S-24: la fascia gialla "nome di una persona a cui hai scritto, da un altro indirizzo" non scattava dentro lo stesso
    /// servizio di posta personale (gmail.com valeva come "stessa organizzazione") e si aggirava con un carattere
    /// invisibile o una lettera sosia nel nome.
    /// </summary>
    static void LookAlikeAuditTests()
    {
        // Nomi e domini nuovi a ogni giro: la rubrica della cartella dei dati della suite resta fra un giro e l'altro.
        string tag = Guid.NewGuid().ToString("N")[..6];
        string mario = $"Mario Rossi {tag}", maria = $"Maria Rossi {tag}";
        string known = $"mario.rossi.{tag}@gmail.com", office = $"maria.rossi@studio-{tag}.example", hostile = $"mario@dominio-ostile-{tag}.example";
        AddressBook.Sent([new Address { Name = mario, Email = known }, new Address { Name = maria, Email = office }], DateTime.Now.AddDays(-3));
        static string? Look(string name, string email) => AddressBook.LookAlike(new Address { Name = name, Email = email });

        Check(Look(mario, $"mario.rossi.studio.{tag}@gmail.com") == known && Look(mario, $"mario.rossi.{tag}@googlemail.com") == known && Look(mario, hostile) == known && Look(mario, known) == null,
              "S-24, posta personale: il nome di una persona a cui hai scritto da un altro indirizzo dello stesso servizio (gmail.com) dà la fascia; dal suo indirizzo no");
        Check(Look(maria, $"segreteria@studio-{tag}.example") == null && Look(maria, $"maria@posta.studio-{tag}.example") == null && Look(maria, $"maria.rossi@studio-{tag}-srl.example") == office
              && Look(maria, $"maria.rossi.{tag}@libero.it") == office,
              "S-24, organizzazioni: dallo stesso dominio o da un suo sottodominio niente fascia, da un dominio che gli somiglia o dalla posta personale sì");

        var tricks = new (string What, string Name)[]
        {
            ("uno spazio a larghezza zero", mario.Replace("Mario", "Ma​rio")),
            ("un cambio di direzione del testo", "‮" + mario + "‬"),
            ("un trattino facoltativo", mario.Replace("Rossi", "Ros­si")),
            ("uno spazio unificatore e due spazi", mario.Replace("Mario Rossi", "Mario  Rossi") + " "),
            ("tutto maiuscolo", mario.ToUpperInvariant()),
            ("la o cirillica", mario.Replace("Mario", "Mariо")),
            ("la a cirillica e la omicron greca", mario.Replace("Mario", "Mаriο")),
            ("la M greca e la i ucraina", mario.Replace("Mario", "Μarіo")),
            ("lettere a larghezza intera", mario.Replace("Mario", "Ｍａｒｉｏ")),
            ("un accento aggiunto", mario.Replace("Mario", "Mário")),
            ("la elle al posto della i", mario.Replace("Mario", "Marlo")),
            ("lo zero al posto della o", mario.Replace("Rossi", "R0ssi")),
        };
        var missed = tricks.Where(t => Look(t.Name, hostile) != known).Select(t => t.What).ToList();
        Check(missed.Count == 0,
              $"S-24, il nome di un contatto scritto con un carattere invisibile o una lettera sosia è ancora il suo ({tricks.Length} modi)" + (missed.Count == 0 ? "" : ": non riconosciuto con " + string.Join(", ", missed)));

        // I falsi allarmi evidenti: i propri account e gli altri propri indirizzi, un indirizzo a cui si è già scritto, un altro nome.
        var own = new MailAccount { Kind = AccountKind.Imap, Email = $"mario.rossi@casa-{tag}.example", Identities = [new SendIdentity { Name = mario, Email = $"info@casa-{tag}.example" }] };
        string second = $"mario.rossi@lavoro-{tag}.example";
        Store.Settings.Accounts.Add(own);
        try
        {
            bool mine = Look(mario, own.Email) == null && Look(mario, $"INFO@casa-{tag}.example") == null;
            bool before = Look(mario, second) == known;
            AddressBook.Sent([new Address { Name = mario, Email = second }], DateTime.Now.AddDays(-1));
            Check(mine && before && Look(mario, second) == null && Look($"Mario Bianchi {tag}", hostile) == null && Look($"Mario Rossini {tag}", hostile) == null && Look("Mar", hostile) == null
                  && Look(known, hostile) == null,
                  "S-24, nessuna fascia per un proprio account o un altro proprio indirizzo, per un indirizzo a cui si è già scritto, per un altro nome");
        }
        finally
        {
            Store.Settings.Accounts.Remove(own);
        }
    }

    /// <summary>
    /// S-23: la Authentication-Results si divideva a ogni ";" senza guardare le virgolette, e valeva il primo dmarc=: un
    /// mittente della busta come "a;dmarc=pass"@dominio (scelto da chi scrive) faceva leggere "pass" e toglieva la fascia
    /// rossa. Lo stesso con una parentesi nel mittente della busta, che Gmail ripete in un commento.
    /// </summary>
    static void AuthResultsAuditTests()
    {
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "ada.prova@gmail.com" };
        const string bank = "banca-prova.example", hostile = "dominio-ostile.example";
        static SenderCheck.Results P(string header) => SenderCheck.Parse(header);
        // Come scrive Gmail: il mittente della busta nel commento e nella proprietà smtp.mailfrom.
        static string Google(string spf, string mailFrom, string rest) =>
            $"mx.google.com; dkim=none; spf={spf} (google.com: domain of {mailFrom} designates 192.0.2.9 as permitted sender) smtp.mailfrom={mailFrom}{rest}";
        string dmarcFail = $"; dmarc=fail (p=NONE sp=NONE dis=NONE) header.from={bank}";
        var summary = new MessageSummary { Id = "1", FolderId = "INBOX", From = new Address { Name = "Banca di prova", Email = "avvisi@" + bank } };

        // L'intestazione del rapporto.
        string report = $"mx.google.com; dkim=none; spf=pass smtp.mailfrom=\"a;dmarc=pass\"@{hostile}{dmarcFail}";
        var quoted = P(report);
        var notes = SenderCheck.Of(gmail, summary, new MessageBody { AuthenticationResults = [report] });
        Check(quoted is { Dmarc: "fail", Spf: "pass", SpfDomain: hostile, DmarcFrom: bank } && SenderCheck.Failed(quoted) && notes.Unverified == bank,
              $"S-23, un \";dmarc=pass\" fra virgolette nel mittente della busta non è un risultato: DMARC {quoted.Dmarc}, SPF di {quoted.SpfDomain}, fascia rossa per {notes.Unverified}");

        var escaped = P($"mx.google.com; spf=pass smtp.mailfrom=\"a\\\";dmarc=pass;\\\"\"@{hostile}{dmarcFail}");
        Check(escaped is { Dmarc: "fail", SpfDomain: hostile } && SenderCheck.Failed(escaped),
              $"S-23, lo stesso con le virgolette protette da \"\\\" dentro la stringa: DMARC {escaped.Dmarc}, SPF di {escaped.SpfDomain}");

        // Come scrive Gmail, con il mittente della busta ripetuto nel commento: lì le virgolette non dicono dove finisce, quindi
        // l'intestazione è ambigua. Resta la fascia rossa, e niente lì dentro autentica qualcuno.
        var commented = P(Google("pass", $"\"a;dmarc=pass\"@{hostile}", dmarcFail));
        var honest = P(Google("pass", $"\"a b\"@{bank}", $"; dmarc=pass (p=NONE sp=NONE dis=NONE) header.from={bank}"));
        Check(commented.Dmarc == "fail" && SenderCheck.Failed(commented) && !SenderCheck.Authenticated(commented, hostile) && !SenderCheck.Failed(honest) && !SenderCheck.Authenticated(honest, bank),
              $"S-23, lo stesso ripetuto da Gmail nel commento: DMARC {commented.Dmarc}, fascia rossa; un mittente della busta con le virgolette e tutto superato non ha la fascia, ma non vale come autenticato");

        var first = P($"mx.google.com; dmarc=pass header.from={bank}; dmarc=fail header.from={bank}");
        var second = P($"mx.google.com; dmarc=fail header.from={bank}; dmarc=pass header.from={hostile}");
        var spfs = P($"mx.google.com; spf=pass smtp.mailfrom=a@{bank}; spf=softfail smtp.mailfrom=a@{hostile}; dkim=none");
        Check(first.Dmarc == "fail" && second is { Dmarc: "fail", DmarcFrom: bank } && SenderCheck.Failed(first) && SenderCheck.Failed(second)
              && spfs is { Spf: "softfail", SpfDomain: hostile } && SenderCheck.Failed(spfs) && !SenderCheck.Authenticated(first, bank) && !SenderCheck.Authenticated(spfs, bank),
              $"S-23, due risultati dello stesso metodo in disaccordo: vale il peggiore, in qualunque ordine (DMARC {first.Dmarc} e {second.Dmarc}, SPF {spfs.Spf})");

        // Una parentesi nel mittente della busta: il commento di Gmail non si chiude più e si portava via il dmarc=fail vero.
        var paren = P(Google("pass", $"\"a(\"@{hostile}", dmarcFail));
        Check(paren.Dmarc == "fail" && SenderCheck.Failed(paren) && !SenderCheck.Authenticated(paren, hostile),
              $"S-23, una parentesi mai chiusa nel mittente della busta non nasconde i risultati dopo: DMARC {paren.Dmarc ?? "assente"}");

        // Un commento chiuso prima del tempo dal mittente della busta, con un risultato inventato dentro (dominio senza DMARC).
        string inject = $"\"a);dkim=pass header.d={bank};x=(\"@{bank}";
        var injected = P($"mx.google.com; spf=softfail (google.com: domain of transitioning {inject} does not designate 192.0.2.9 as permitted sender) smtp.mailfrom={inject}");
        Check(SenderCheck.Failed(injected) && !injected.Dkim.Contains("pass") && !SenderCheck.Authenticated(injected, bank),
              $"S-23, un \"dkim=pass\" scritto nel mittente della busta e ripetuto nel commento non vale: DKIM {(injected.Dkim.Count == 0 ? "assente" : string.Join(", ", injected.Dkim))}, fascia rossa");

        // Una virgoletta mai chiusa: da lì si divide a ogni ";" e i "pass" non contano.
        var open = P($"mx.google.com; spf=softfail smtp.mailfrom=\"a@{bank};dkim=pass header.d={bank}; dmarc=fail header.from={bank}");
        var openPass = P($"mx.google.com; spf=softfail smtp.mailfrom=\"a@{bank};dkim=pass header.d={bank};dmarc=pass header.from={bank}");
        Check(open.Dmarc == "fail" && SenderCheck.Failed(open) && SenderCheck.Failed(openPass) && !SenderCheck.Authenticated(openPass, bank),
              $"S-23, una virgoletta mai chiusa: i risultati dopo si leggono lo stesso, ma solo quelli non superati (DMARC {open.Dmarc ?? "assente"} e {openPass.Dmarc ?? "assente"})");

        // La stessa proprietà due volte con valori diversi non dice di chi è il risultato.
        var twice = P($"mx.google.com; spf=pass smtp.mailfrom={bank} smtp.mailfrom={hostile}");
        var inside = P($"mx.google.com; spf=pass smtp.helo=\"x smtp.mailfrom={bank}\" smtp.mailfrom=a@{hostile}; dkim=pass reason=\"header.d={bank}\" header.d={hostile}");
        Check(twice.SpfDomain == null && !SenderCheck.Authenticated(twice, bank) && inside.SpfDomain == hostile && inside.DkimDomains.SequenceEqual([hostile]) && !SenderCheck.Authenticated(inside, bank),
              $"S-23, una proprietà scritta due volte con valori diversi non vale, e una scritta dentro il valore di un'altra non si legge (SPF di {twice.SpfDomain ?? "nessuno"} e di {inside.SpfDomain})");

        // Le forme vere di Gmail e di Microsoft: tutto come prima.
        var google = P($"mx.google.com; dkim=pass header.i=@{bank} header.s=s1 header.b=abc; dkim=fail (body hash; did not verify) header.i=@invii-prova.example; arc=pass (i=1 spf=pass spfdomain={bank}); " +
                       $"spf=pass (google.com: domain of rimbalzi@posta.{bank} designates 192.0.2.1 as permitted sender) smtp.mailfrom=rimbalzi@posta.{bank}; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from={bank}");
        var exchange = P($"spf=pass (sender IP is 192.0.2.1) smtp.mailfrom={bank}; dkim=pass (signature was verified) header.d={bank};dmarc=pass action=none header.from={bank};compauth=pass reason=100");
        var reason = P($"posta.studio-prova.example; dkim=fail reason=\"signature verification failed; bad key\" header.d={bank}; spf=softfail smtp.mailfrom=a@{bank}");
        Check(google is { AuthservId: "mx.google.com", Dmarc: "pass", Spf: "pass", DmarcFrom: bank, SpfDomain: "posta." + bank } && google.Dkim.SequenceEqual(["pass", "fail"]) && google.DkimDomains.SequenceEqual([bank])
              && !SenderCheck.Failed(google) && SenderCheck.Authenticated(google, bank)
              && exchange is { AuthservId: null, Dmarc: "pass", Spf: "pass", DmarcFrom: bank, SpfDomain: bank } && exchange.Dkim.SequenceEqual(["pass"]) && exchange.DkimDomains.SequenceEqual([bank])
              && SenderCheck.Authenticated(exchange, bank)
              && reason is { AuthservId: "posta.studio-prova.example", Spf: "softfail" } && reason.Dkim.SequenceEqual(["fail"]) && SenderCheck.Failed(reason),
              "S-23, le forme di Gmail e di Microsoft (commenti con \";\", più firme DKIM, un motivo fra virgolette) si leggono come prima");

        // Un'intestazione enorme scritta da chi manda (Gmail legge tutte le Authentication-Results per trovare la sua): una passata.
        var clock = Stopwatch.StartNew();
        var huge = P("mx.finto.example; spf=pass " + new string('(', 300_000) + new string('"', 300_001) + string.Concat(Enumerable.Repeat(";dmarc=pass", 50_000)));
        Check(clock.ElapsedMilliseconds < 1500 && huge.Dmarc == null && huge.Spf == "pass",
              $"S-23, un'intestazione di 1,1 MB con 300.000 parentesi e virgolette mai chiuse: letta in {clock.ElapsedMilliseconds} ms, nessun \"pass\" preso da lì");
    }

    /// <summary>
    /// Audit, blocco 2: i loghi dei mittenti (SenderLogos) con SVG, immagini, pagine e domini costruiti apposta. Niente rete
    /// vera: un ascoltatore su 127.0.0.1 che conta chi bussa, nomi che non esistono, DNS e HTTP finti.
    /// </summary>
    static void LogoAuditTests()
    {
        Report.Add("--- Audit, blocco 2: i loghi dei mittenti con SVG, immagini e domini costruiti apposta");
        SvgAuditTests();
        RasterAuditTests();
        LogoTrustTests();
        LogoNetworkTests();
        MarkAuditTests();
        SitePageTests();
    }

    /// <summary>
    /// La pagina di un sito, da cui si leggono le icone dichiarate: prima passava da AngleSharp senza i tetti che ha la
    /// pulizia delle email (blocco 1), e una pagina di poche decine di KB costruita apposta costava secondi e gigabyte.
    /// </summary>
    static void SitePageTests()
    {
        static string Times(string text, int times) => string.Concat(Enumerable.Repeat(text, times));
        var site = new Uri("https://www.negozio-prova.example/it/");
        const string head = "<html><head><title>Negozio di prova</title><link rel=\"icon\" href=\"/icona-64.png\" sizes=\"64x64\"></head><body>";
        static long Allocated() => GC.GetTotalAllocatedBytes();
        (string Name, string Page)[] hostile =
        [
            ("1.000 <font> mai chiusi riaperti in ogni paragrafo (25 KB)", head + string.Concat(Enumerable.Range(0, 1000).Select(i => $"<p><font color=#{i:x6}>x"))),
            ("20.000 <div> annidati (100 KB)", head + Times("<div>", 20_000)),
            ("100.000 \"<\" sciolti (98 KB)", head + Times("<", 100_000)),
        ];
        var slow = new List<string>();
        long worstMs = 0, worstMb = 0;
        foreach (var (name, page) in hostile)
        {
            long before = Allocated();
            var clock = Stopwatch.StartNew();
            int found = SenderLogos.DeclaredIcons(page, site).Count();
            long ms = clock.ElapsedMilliseconds, megabytes = (Allocated() - before) >> 20;
            (worstMs, worstMb) = (Math.Max(worstMs, ms), Math.Max(worstMb, megabytes));
            if (ms > 500 || megabytes > 200) slow.Add($"{name}: {ms} ms, {megabytes} MB chiesti, {found} icone");
        }
        Check(slow.Count == 0, $"icone dichiarate, {hostile.Length} pagine costruite apposta: ognuna entro 500 ms e 200 MB (al massimo {worstMs} ms e {worstMb} MB; prima fino a 3 s e 9,5 GB)" + (slow.Count == 0 ? "" : $" [{string.Join("; ", slow)}]"));

        string normal =
            "<!DOCTYPE html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>Negozio di prova</title>" +
            "<link rel=\"stylesheet\" href=\"/stile.css\"><link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"/icona-32.png\">" +
            "<link rel=\"shortcut icon\" href=\"icona-16.ico\" sizes=\"16x16\"><link rel=\"apple-touch-icon\" href=\"https://immagini.negozio-prova.example/tocco.png\">" +
            "<link rel=\"icon\" type=\"image/svg+xml\" href=\"/icona.svg\"><link rel=\"icon\" sizes=\"192x192\" href=\"http://www.negozio-prova.example/in-chiaro.png\">" +
            "<link rel=\"mask-icon\" href=\"/maschera.svg\"></head><body>" + Times("<div class=\"riga\"><p>Prodotto <b>di prova</b> <a href=\"/p\">vedi</a></p></div>", 2000) + "</body></html>";
        var icons = SenderLogos.DeclaredIcons(normal, site).ToList();
        Check(icons.SequenceEqual(["https://www.negozio-prova.example/icona.svg", "https://immagini.negozio-prova.example/tocco.png", "https://www.negozio-prova.example/icona-32.png"]),
              $"icone dichiarate da una pagina normale di {normal.Length / 1024} KB: come prima, dalla più grande, solo https ({string.Join(", ", icons)})");
    }

    /// <summary>
    /// S-17: il logo BIMI è quello dentro il certificato del marchio, non l'SVG dell'indirizzo l= (che chi falsifica il DNS
    /// sceglie a piacere accanto al certificato vero di un altro), e un logo salvato si riverifica dopo 30 giorni.
    /// </summary>
    static void MarkAuditTests()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        string brand = $"marchio-{tag}.example", other = $"altro-{tag}.example", old = $"vecchio-{tag}.example";
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "prova.loghi@gmail.com" };
        var now = DateTime.Now;
        void Trust(string domain)
        {
            MessageSummary Mail(string id, DateTime date) => new() { Id = id + domain, FolderId = "INBOX", From = new Address { Email = "notizie@" + domain }, Subject = "Prova", Date = date };
            SenderLogos.Note(Mail("1", now.AddDays(-15)));
            SenderLogos.NoteBody(gmail, Mail("2", now), new MessageBody { AuthenticationResults = [$"mx.google.com; dkim=pass header.i=@{domain}; dmarc=pass (p=REJECT) header.from={domain}"] });
        }
        static void Wait(string domain)
        {
            for (int i = 0; i < 100 && SenderLogos.IsBusy(domain); i++) Thread.Sleep(50);
            Thread.Sleep(50);
        }
        static string Logo(string color) => SvgOpen + $"<rect width=\"10\" height=\"10\" fill=\"{color}\"/><circle cx=\"5\" cy=\"5\" r=\"3\" fill=\"#ffffff\"/></svg>";
        static byte[] Zipped(string text)
        {
            using var output = new MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true)) gzip.Write(Encoding.UTF8.GetBytes(text));
            return output.ToArray();
        }
        // Una radice di prova e il certificato del marchio: uso BIMI, il dominio fra i nomi, il logo (blu) dentro l'estensione 1.3.6.1.5.5.7.1.12.
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Radice dei marchi di prova, O=Missiva, C=IT", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.Now.AddDays(-2), DateTimeOffset.Now.AddYears(1));
        string Mark(string domain, string svg, X509Certificate2? issuer = null, string? issuerAt = null)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Marchio di prova, O=Negozio di prova, C=IT", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (issuerAt != null) request.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension(null, [issuerAt]));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.31")], false));
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(domain);
            request.CertificateExtensions.Add(names.Build());
            var zipped = Zipped(svg);
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2)))
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            using (writer.PushSequence())
            using (writer.PushSequence())
            using (writer.PushSequence())
            {
                writer.WriteCharacterString(UniversalTagNumber.IA5String, "image/svg+xml");
                using (writer.PushSequence())
                using (writer.PushSequence())
                {
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                        writer.WriteNull();
                    }
                    writer.WriteOctetString(SHA256.HashData(zipped));
                }
                using (writer.PushSequence()) writer.WriteCharacterString(UniversalTagNumber.IA5String, "data:image/svg+xml;base64," + Convert.ToBase64String(zipped));
            }
            request.CertificateExtensions.Add(new X509Extension("1.3.6.1.5.5.7.1.12", writer.Encode(), false));
            // Cinque mesi, dentro i sei dell'autorità di mezzo: con sei per tutti e due, a cavallo di un secondo il certificato
            // scadeva un secondo dopo chi lo firma, e la prova si fermava con un'eccezione (visto il 4 ottobre 2026).
            using var leaf = request.Create(issuer ?? root, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddMonths(5), RandomNumberGenerator.GetBytes(8));
            return issuer == null ? leaf.ExportCertificatePem() + "\n" + root.ExportCertificatePem() : leaf.ExportCertificatePem();
        }
        string pem = Mark(brand, Logo("#113355"));
        // Un certificato a cui manca l'anello di mezzo, con dentro l'indirizzo da cui scaricarlo: un ascoltatore su 127.0.0.1.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int knocks = 0;
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref knocks);
                }
            }
            catch (Exception) { }
        });
        string chained = $"catena-{tag}.example", chainPem;
        using (var middleKey = RSA.Create(2048))
        {
            var middleRequest = new CertificateRequest("CN=Autorità di mezzo di prova, O=Missiva, C=IT", middleKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            middleRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var middlePublic = middleRequest.Create(root, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddMonths(6), RandomNumberGenerator.GetBytes(8));
            using var middle = middlePublic.CopyWithPrivateKey(middleKey);
            chainPem = Mark(chained, Logo("#113355"), middle, $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/mezzo.cer");
        }
        var asked = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var icon = new[] { LogoPicture(new PngBitmapEncoder(), 180) };
        bool down = false;
        MailDns.TestTxt = name =>
        {
            asked.Enqueue("TXT " + name);
            // Il record di "altro" punta al certificato vero di "marchio" con un logo suo: quello che fa chi falsifica il DNS.
            return name.EndsWith(old) ? [] : [$"v=BIMI1; l=https://{(name.EndsWith(other) ? other : brand)}/logo-l.svg; a=https://{(name.EndsWith(chained) ? chained : brand)}/marchio.pem"];
        };
        SenderLogos.UseTestHandler(new FakeHttp(request =>
        {
            asked.Enqueue("GET " + request.RequestUri);
            if (down) throw new HttpRequestException("prova: rete assente");
            (byte[] Data, string Type)? found = request.RequestUri!.AbsolutePath switch
            {
                "/marchio.pem" => (Encoding.ASCII.GetBytes(request.RequestUri.Host == chained ? chainPem : pem), "application/x-pem-file"),
                "/logo-l.svg" => (Encoding.UTF8.GetBytes(Logo("#cc0000")), "image/svg+xml"),
                "/apple-touch-icon.png" when request.RequestUri.Host == old && icon[0].Length > 0 => (icon[0], "image/png"),
                _ => null,
            };
            var response = new HttpResponseMessage(found != null ? HttpStatusCode.OK : HttpStatusCode.NotFound) { RequestMessage = request };
            if (found is var (data, type)) response.Content = new ByteArrayContent(data) { Headers = { ContentType = new(type) } };
            return response;
        }));
        SenderLogos.TestMarkRoot = root;
        try
        {
            Trust(brand);
            bool started = SenderLogos.Request(brand);
            Wait(brand);
            var png = File.Exists(SenderLogos.PathOf(brand)) ? File.ReadAllBytes(SenderLogos.PathOf(brand)) : null;
            Check(started && LogoPixel(png, 3, 3) == (0x11, 0x33, 0x55) && LogoPixel(png, 48, 48) == (255, 255, 255) && asked.SequenceEqual(["TXT default._bimi." + brand, $"GET https://{brand}/marchio.pem"]),
                  $"S-17, BIMI con il certificato del marchio (radice di prova): il logo è quello dentro il certificato (blu), l'indirizzo l= (che darebbe un logo rosso) non si scarica nemmeno ({string.Join(", ", asked)})");

            // Lo stesso certificato accanto al DNS di un altro dominio: non vale, e il logo l= di chi falsifica non si usa.
            asked.Clear();
            Trust(other);
            SenderLogos.Request(other);
            Wait(other);
            Check(!File.Exists(SenderLogos.PathOf(other)) && asked.Contains($"GET https://{brand}/marchio.pem") && !asked.Any(a => a.Contains("logo-l.svg")),
                  "S-17, il certificato di un marchio nel record BIMI di un altro dominio: nessun logo, e l'SVG dell'indirizzo l= non si scarica");

            // Il certificato che dice dove scaricare l'anello che manca: Windows non ci va (prima bussava all'indirizzo scritto dal mittente).
            Trust(chained);
            SenderLogos.Request(chained);
            Wait(chained);
            Thread.Sleep(200);
            Check(Volatile.Read(ref knocks) == 0 && !File.Exists(SenderLogos.PathOf(chained)) && asked.Contains($"GET https://{chained}/marchio.pem"),
                  $"S-17, un certificato del marchio a cui manca l'anello di mezzo e dice di scaricarlo da 127.0.0.1: nessuna connessione ({Volatile.Read(ref knocks)}) e nessun logo");
            listener.Stop();

            // Un logo salvato (l'icona del sito) da 31 giorni: si riverifica, e se il sito ne ha uno nuovo lo prende.
            Trust(old);
            SenderLogos.Request(old);
            Wait(old);
            bool first = File.Exists(SenderLogos.PathOf(old));
            asked.Clear();
            SenderLogos.Request(old);
            Wait(old);
            bool fresh = asked.IsEmpty; // appena salvato: nessuna richiesta
            if (first) File.SetLastWriteTimeUtc(SenderLogos.PathOf(old), DateTime.UtcNow.AddDays(-31));
            SenderLogos.Request(old);
            Wait(old);
            bool renewed = first && File.Exists(SenderLogos.PathOf(old)) && asked.Contains($"GET https://{old}/apple-touch-icon.png")
                           && DateTime.UtcNow - File.GetLastWriteTimeUtc(SenderLogos.PathOf(old)) < TimeSpan.FromMinutes(5);
            // Rete assente alla riverifica: il logo resta, fino a 60 giorni; il sito che non ha più l'icona: il logo esce.
            if (first) File.SetLastWriteTimeUtc(SenderLogos.PathOf(old), DateTime.UtcNow.AddDays(-31));
            down = true;
            SenderLogos.Request(old);
            Wait(old);
            bool kept = File.Exists(SenderLogos.PathOf(old));
            if (kept) File.SetLastWriteTimeUtc(SenderLogos.PathOf(old), DateTime.UtcNow.AddDays(-61));
            SenderLogos.Request(old);
            Wait(old);
            bool dropped = !File.Exists(SenderLogos.PathOf(old));
            down = false;
            SenderLogos.TestForget([old]);
            Trust(old);
            SenderLogos.Request(old);
            Wait(old);
            bool again = File.Exists(SenderLogos.PathOf(old));
            if (again) File.SetLastWriteTimeUtc(SenderLogos.PathOf(old), DateTime.UtcNow.AddDays(-31));
            icon[0] = [];
            SenderLogos.Request(old);
            Wait(old);
            bool gone = again && !File.Exists(SenderLogos.PathOf(old));
            Check(first && fresh && renewed && kept && dropped && gone,
                  $"S-17, un logo salvato da 31 giorni si riverifica (appena salvato no: {fresh}; rinnovato: {renewed}); senza rete resta ({kept}) ma non oltre 60 giorni ({dropped}); se il sito non lo ha più esce ({gone})");
        }
        finally
        {
            MailDns.TestTxt = null;
            SenderLogos.UseTestHandler(null);
            SenderLogos.TestMarkRoot = null;
            SenderLogos.TestForget([brand, other, old, chained]);
            listener.Stop();
        }
    }

    /// <summary>
    /// S-16: gli indirizzi a cui Missiva non si collega per un logo. Prima un IPv6 con dentro un IPv4 privato (NAT64, 6to4)
    /// passava per pubblico, e con un proxy di sistema il controllo guardava l'indirizzo del proxy, non la destinazione.
    /// </summary>
    static void LogoNetworkTests()
    {
        string[] inside =
        [
            "64:ff9b::c0a8:101", "64:ff9b::7f00:1", "64:ff9b::a00:5", "64:ff9b:1::c0a8:101", "2002:c0a8:101::1", "2002:7f00:1::", "2002:a9fe:101::1", "::c0a8:101", "::ffff:10.0.0.1",
            "::", "::1", "fc00::1", "fd12:3456::1", "fe80::1", "fec0::1", "ff02::1", "2001:db8::1", "2001:0:4136:e378:8000:63bf:3fff:fdd2", "100::1",
            "0.0.0.0", "10.1.2.3", "100.64.0.1", "127.0.0.1", "169.254.169.254", "172.31.255.255", "192.0.0.1", "192.0.2.1", "192.168.178.1", "198.18.0.1", "198.19.255.1", "198.51.100.7",
            "203.0.113.9", "224.0.0.1", "240.0.0.1", "255.255.255.255",
        ];
        string[] outside = ["151.101.1.1", "8.8.8.8", "1.1.1.1", "100.128.0.1", "172.32.0.1", "192.0.1.1", "198.17.0.1", "198.20.0.1", "64:ff9b::9765:101", "2002:9765:101::1", "2a00:1450:4002::65", "2606:4700::1111"];
        var wrongInside = inside.Where(a => SenderLogos.IsPublic(IPAddress.Parse(a))).ToList();
        var wrongOutside = outside.Where(a => !SenderLogos.IsPublic(IPAddress.Parse(a))).ToList();
        Check(wrongInside.Count == 0 && wrongOutside.Count == 0,
              $"S-16, {inside.Length} indirizzi di reti private, locali, di prova o con un IPv4 privato dentro (NAT64, 6to4) rifiutati e {outside.Length} pubblici accettati" +
              (wrongInside.Count + wrongOutside.Count == 0 ? "" : $" [presi per pubblici: {string.Join(", ", wrongInside)}; presi per privati: {string.Join(", ", wrongOutside)}]"));

        // Con un proxy di sistema: il gestore dei loghi deve chiedere di collegarsi al sito, non al proxy (203.0.113.5 è un indirizzo di prova).
        var system = HttpClient.DefaultProxy;
        var seen = new List<string>();
        try
        {
            HttpClient.DefaultProxy = new WebProxy("http://203.0.113.5:8080");
            SenderLogos.TestConnect = endpoint => { lock (seen) seen.Add(endpoint); };
            using var handler = SenderLogos.CreateHandler();
            using var http = new HttpClient(handler);
            try { Task.Run(() => http.GetAsync("https://loghi-prova.example/icona.png")).GetAwaiter().GetResult(); }
            catch (HttpRequestException) { }
            Check(!handler.UseProxy && seen.SequenceEqual(["loghi-prova.example:443"]),
                  $"S-16, con un proxy di sistema il gestore dei loghi non lo usa: il controllo degli indirizzi guarda il sito ({string.Join(", ", seen)})");
        }
        finally
        {
            SenderLogos.TestConnect = null;
            HttpClient.DefaultProxy = system;
        }
    }

    /// <summary>
    /// S-03 e S-04: prima il logo si cercava (DNS di BIMI, poi l'indirizzo scelto dal mittente) per qualunque dominio alla
    /// sola comparsa di una riga, e si mostrava per qualunque From. Ora solo per i domini noti e autenticati: un messaggio
    /// ricevuto da lì con DMARC superato secondo il server della propria casella.
    /// </summary>
    static void LogoTrustTests()
    {
        // Domini nuovi a ogni giro: la cartella dei dati della suite resta fra un giro e l'altro.
        string tag = Guid.NewGuid().ToString("N")[..8];
        string fresh = $"nuovo-{tag}.example", known = $"noto-{tag}.example", young = $"giovane-{tag}.example", trusted = $"vero-{tag}.example",
               plain = $"semplice-{tag}.example", copied = $"copie-{tag}.example";
        string[] all = [fresh, known, young, trusted, plain, copied];
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "prova.loghi@gmail.com" };
        var now = DateTime.Now;
        MessageSummary Mail(string id, string domain, DateTime date) =>
            new() { Id = id + tag, FolderId = "INBOX", StableId = id + tag, From = new Address { Name = "Negozio di prova", Email = "notizie@posta." + domain }, Subject = "Prova", Date = date };
        static MessageBody Body(params string[] results) => new() { Html = "<p>Prova</p>", AuthenticationResults = [.. results] };
        static string Pass(string d) => $"mx.google.com; dkim=pass header.i=@{d} header.s=s1 header.b=abc; spf=pass (google.com: domain of rimbalzi@posta.{d} designates 192.0.2.1 as permitted sender) " +
                                        $"smtp.mailfrom=rimbalzi@posta.{d}; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from={d}";
        static string Fail(string d) => $"mx.google.com; dkim=none; spf=softfail (google.com: domain of transitioning x@{d} does not designate 192.0.2.9 as permitted sender) smtp.mailfrom=x@{d}; " +
                                        $"dmarc=fail (p=NONE sp=NONE dis=NONE) header.from={d}";

        var asked = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var agents = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var icon = LogoPicture(new PngBitmapEncoder(), 180);
        MailDns.TestTxt = name =>
        {
            asked.Enqueue("TXT " + name);
            return name == $"default._bimi.{trusted}" ? [] : [$"v=BIMI1; l=https://loghi.{tag}.example/logo.svg; a=https://loghi.{tag}.example/marchio.pem"];
        };
        SenderLogos.UseTestHandler(new FakeHttp(request =>
        {
            asked.Enqueue("GET " + request.RequestUri);
            agents.Enqueue(request.Headers.UserAgent.ToString());
            bool found = request.RequestUri is { AbsolutePath: "/apple-touch-icon.png" } uri && uri.Host == trusted;
            var response = new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound) { RequestMessage = request };
            if (found) response.Content = new ByteArrayContent(icon) { Headers = { ContentType = new("image/png") } };
            return response;
        }));
        var changed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        Action<string> onChange = changed.Enqueue;
        SenderLogos.Arrived += onChange;
        static void Wait(string domain)
        {
            for (int i = 0; i < 100 && SenderLogos.IsBusy(domain); i++) Thread.Sleep(50);
            Thread.Sleep(50);
        }
        try
        {
            // Un dominio mai visto, con un record BIMI che punta dove vuole lui: la riga compare, nessuna richiesta.
            SenderLogos.Note(Mail("1", fresh, now));
            var freshAddress = new Address { Email = "notizie@" + fresh };
            bool freshLogo = UI.Logos.DomainFor(freshAddress) != null || UI.Logos.Get(fresh) != null;
            bool freshAsked = SenderLogos.Request(fresh);
            Wait(fresh);
            Check(!freshLogo && !freshAsked && asked.IsEmpty,
                  $"S-03, un dominio appena comparso con un record BIMI suo: nessuna richiesta DNS né HTTP alla comparsa della riga ({asked.Count}: {string.Join(", ", asked.Take(3))})");

            // Noto da 15 giorni ma mai autenticato; autenticato ma arrivato solo oggi: ancora nessuna richiesta.
            SenderLogos.Note(Mail("2", known, now.AddDays(-15)));
            SenderLogos.Note(Mail("3", known, now));
            SenderLogos.NoteBody(gmail, Mail("4", young, now), Body(Pass(young)));
            bool knownAsked = SenderLogos.Request(known), youngAsked = SenderLogos.Request(young);
            UI.Logos.Get(known);
            UI.Logos.Get(young);
            Wait(known);
            Wait(young);
            Check(SenderLogos.IsKnown(known) && !SenderLogos.IsAuthenticated(known) && !knownAsked && UI.Logos.DomainFor(new Address { Email = "a@" + known }) == null
                  && SenderLogos.IsAuthenticated(young) && !SenderLogos.IsKnown(young) && !youngAsked && asked.IsEmpty,
                  $"S-03 e S-04, un dominio noto ma senza un messaggio autenticato, e uno autenticato ma arrivato solo oggi: nessuna richiesta ({asked.Count}) e nessun logo");

            // Noto e autenticato: BIMI (qui assente), poi l'icona del sito; senza dire chi chiede.
            SenderLogos.Note(Mail("5", trusted, now.AddDays(-15)));
            var real = Mail("6", trusted, now);
            SenderLogos.NoteBody(gmail, real, Body(Pass(trusted)));
            var trustedAddress = new Address { Email = "notizie@posta." + trusted };
            bool started = UI.Logos.DomainFor(trustedAddress) == trusted && SenderLogos.Request(trusted);
            Wait(trusted);
            Check(started && File.Exists(SenderLogos.PathOf(trusted)) && asked.Contains("TXT default._bimi." + trusted) && asked.Contains($"GET https://{trusted}/apple-touch-icon.png")
                  && asked.All(a => a.Contains(trusted)) && !agents.IsEmpty && agents.All(a => a.StartsWith("Mozilla/5.0") && !a.Contains("Missiva", StringComparison.OrdinalIgnoreCase))
                  && changed.Count(d => d == trusted) == 2 && LogoPixel(UI.Logos.Bytes(trusted), 48, 48) == (255, 255, 255),
                  $"S-03 e S-04, un dominio noto da 15 giorni con un messaggio che ha superato DMARC: il logo si cerca e si mostra ({string.Join(", ", asked)}), chi lo mostra viene avvisato, " +
                  $"User-Agent senza il nome di Missiva");

            // Un From falsificato di quel dominio: il dominio resta autenticato, quel messaggio no.
            var forged = Mail("7", trusted, now);
            SenderLogos.NoteBody(gmail, forged, Body(Fail(trusted)));
            var silent = Mail("8", trusted, now);
            SenderLogos.NoteBody(gmail, silent, Body());
            SenderLogos.Note(Mail("9", plain, now.AddDays(-15)));
            SenderLogos.Note(Mail("10", plain, now));
            // Una Authentication-Results scritta dal mittente (un altro authserv-id), e una del server vero che fallisce.
            SenderLogos.NoteBody(gmail, Mail("11", plain, now), Body($"mx.finto.example; dmarc=pass header.from={plain}"));
            SenderLogos.NoteBody(gmail, Mail("12", plain, now), Body(Fail(plain), Pass(plain).Replace("mx.google.com", "mx.finto.example")));
            Check(SenderLogos.IsAuthenticated(trusted) && SenderLogos.IsDoubtful(gmail, forged) && !SenderLogos.IsDoubtful(gmail, real) && !SenderLogos.IsDoubtful(gmail, silent)
                  && !SenderLogos.IsAuthenticated(plain) && !SenderLogos.Request(plain) && SenderLogos.IsDoubtful(gmail, Mail("12", plain, now)) && !SenderLogos.IsDoubtful(gmail, Mail("11", plain, now)),
                  "S-04, un From falsificato (DMARC fallito) di un dominio autenticato resta senza logo; una Authentication-Results scritta dal mittente non autentica nessuno");

            var page = UI.ReadingPage.Build([new UI.PageEntry(forged, Body(Fail(trusted)), true, false, null, false, new SenderNotes(trusted, "perché", false, null), icon)], light: true);
            var fine = UI.ReadingPage.Build([new UI.PageEntry(real, Body(Pass(trusted)), true, false, null, false, new SenderNotes(null, "", false, null), icon)], light: true);
            Check(!page.Html.Contains("/logo/") && page.Resources.Count == 0 && page.Html.Contains("avatar") && fine.Html.Contains("/logo/0.png") && fine.Resources.ContainsKey("/logo/0.png"),
                  "S-04, nel riquadro con la fascia rossa \"mittente non verificato\" il logo non c'è (restano le iniziali); senza la fascia c'è");

            // Che cosa conta come autenticato. Dalle scelte del 12 ottobre 2026 (T-06) solo con DMARC superato: le tre forme senza
            // DMARC, che il 4 ottobre valevano (una firma DKIM o un SPF di quel dominio, "bestguesspass"), ora non valgono più.
            static bool Auth(string header, string domain) => SenderCheck.Authenticated(SenderCheck.Parse(header), domain);
            const string d = "negozio-prova.example";
            Check(Auth(Pass(d), d) && Auth(Pass("posta." + d), d) && !Auth(Pass("altro-prova.example"), d) && !Auth(Fail(d), d)
                  && Auth($"spf=pass (sender IP is 192.0.2.1) smtp.mailfrom={d}; dkim=pass (signature was verified) header.d={d};dmarc=pass action=none header.from={d};compauth=pass reason=100", d)
                  && !Auth($"mx.google.com; dkim=pass header.i=@{d} header.s=s1; spf=pass smtp.mailfrom=x@invii-prova.example", d)
                  && !Auth($"mx.google.com; dkim=none; spf=pass smtp.mailfrom=rimbalzi@posta.{d}", d)
                  && !Auth($"spf=pass smtp.mailfrom=posta.{d}; dkim=none; dmarc=bestguesspass action=none header.from={d}", d)
                  && !Auth($"mx.google.com; dkim=pass header.i=@invii-prova.example; spf=pass smtp.mailfrom=x@invii-prova.example", d)
                  && !Auth($"mx.google.com; dkim=pass header.i=@{d}; spf=pass smtp.mailfrom=x@{d}; dmarc=fail (p=REJECT) header.from={d}", d)
                  && !Auth($"mx.google.com; dkim=pass header.i=@{d}; dmarc=temperror header.from={d}", d)
                  && !Auth($"mx.google.com; dkim=fail header.i=@{d}; spf=softfail smtp.mailfrom=x@{d}", d) && !Auth("mx.google.com; none", d),
                  "S-04, autenticato: DMARC superato per il dominio del From (anche da un sottodominio); mai con DMARC fallito o di un altro dominio, e dalle scelte del 12 ottobre nemmeno senza DMARC, " +
                  "con la sola firma DKIM o il solo SPF di quel dominio");

            // L'aggiornamento: i domini di prima non hanno il segno, ma le copie dei corpi sul PC sì.
            SenderLogos.Note(Mail("13", copied, now.AddDays(-20)));
            var onDisk = Mail("14", copied, now);
            SenderLogos.Note(onDisk);
            BodyCache.Save(gmail, onDisk, Body(Pass(copied)));
            BodyCache.Save(gmail, Mail("15", plain, now), Body(Fail(plain)));
            Task[] writes;
            lock (BodyCache.TestWrites) writes = [.. BodyCache.TestWrites];
            Task.WaitAll(writes);
            bool before = SenderLogos.IsAuthenticated(copied);
            File.Delete(SenderLogos.MigrationMark);
            SenderLogos.AuthenticateFromCopies([gmail]);
            bool after = SenderLogos.IsAuthenticated(copied) && !SenderLogos.IsAuthenticated(plain) && File.Exists(SenderLogos.MigrationMark);
            SenderLogos.TestReload();
            Check(!before && after && SenderLogos.IsAuthenticated(copied) && SenderLogos.IsKnown(copied) && SenderLogos.IsAuthenticated(trusted) && !SenderLogos.IsAuthenticated(known)
                  && SenderLogos.TestReadsOld(),
                  "S-04, dopo l'aggiornamento: un dominio noto con un messaggio autenticato fra le copie sul PC torna autenticato (una volta sola), e il segno resta in domini.dat riletto; il file di prima si legge");
        }
        finally
        {
            SenderLogos.Arrived -= onChange;
            MailDns.TestTxt = null;
            SenderLogos.UseTestHandler(null);
            SenderLogos.TestForget(all);
            BodyCache.Delete(gmail);
        }
    }

    static (int R, int G, int B)? LogoPixel(byte[]? png, int x, int y)
    {
        if (png == null) return null;
        var frame = new FormatConvertedBitmap(new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        if (frame.PixelWidth != SenderLogos.Pixels || frame.PixelHeight != SenderLogos.Pixels) return null;
        var one = new byte[4];
        frame.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), one, 4, 0);
        return (one[2], one[1], one[0]);
    }

    /// <summary>Un quadrato blu con un quadrato bianco in mezzo, nel formato di ogni codificatore.</summary>
    static byte[] LogoPicture(BitmapEncoder encoder, int side = 64)
    {
        var pixels = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                bool white = x >= side / 4 && x < side * 3 / 4 && y >= side / 4 && y < side * 3 / 4;
                int i = (y * side + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = white ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)0x55, (byte)0x33, (byte)0x11, (byte)255);
            }
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, pixels, side * 4)));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
    /// <summary>
    /// S-02: prima le icone passavano da BitmapDecoder.Create, che sceglie da solo fra tutti i codec di Windows (TIFF,
    /// WebP, DDS...) e con OnLoad decodifica tutto prima che si guardino le misure (un PNG di 256 KB: 245 MB).
    /// </summary>
    static void RasterAuditTests()
    {
        static byte[]? Render(byte[] data) => Task.Run(() => SenderLogos.RenderRasterAsync(data, 32)).GetAwaiter().GetResult();
        static void Chunk(Stream output, string type, byte[] data)
        {
            byte[] body = [.. Encoding.ASCII.GetBytes(type), .. data];
            uint crc = 0xFFFFFFFF;
            foreach (var b in body)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            var number = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            output.Write(number);
            output.Write(body);
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            output.Write(number);
        }
        // Un PNG vero, tutto nero a 8 bit di grigio, di side pixel di lato: compresso sta in pochi KB.
        static byte[] HugePng(int side)
        {
            using var output = new MemoryStream();
            output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), side);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), side);
            header[8] = 8;
            Chunk(output, "IHDR", header);
            using var packed = new MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                var row = new byte[side + 1];
                for (int y = 0; y < side; y++) zlib.Write(row);
            }
            Chunk(output, "IDAT", packed.ToArray());
            Chunk(output, "IEND", []);
            return output.ToArray();
        }
        // Un .ico con una sola immagine PNG dentro, dichiarata di 64 pixel.
        static byte[] Ico(byte[] png) => [0, 0, 1, 0, 1, 0, 64, 64, 0, 0, 1, 0, 32, 0, .. BitConverter.GetBytes(png.Length), 22, 0, 0, 0, .. png];

        // Un .ico come quelli di una volta: un bitmap di 32 pixel a 32 bit (dal basso in alto, alto il doppio per la maschera) e la maschera.
        static byte[] IcoBitmap()
        {
            var bmp = LogoPicture(new BmpBitmapEncoder(), 32);
            int at = BitConverter.ToInt32(bmp, 10);
            byte[] header = [40, 0, 0, 0, 32, 0, 0, 0, 64, 0, 0, 0, 1, 0, 32, 0, .. new byte[24]];
            byte[] image = [.. header, .. bmp[at..(at + 32 * 32 * 4)], .. new byte[32 * 4]];
            return [0, 0, 1, 0, 1, 0, 32, 32, 0, 0, 1, 0, 32, 0, .. BitConverter.GetBytes(image.Length), 22, 0, 0, 0, .. image];
        }

        var tiff = LogoPicture(new TiffBitmapEncoder());
        byte[] webp = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8L"u8, .. new byte[24]];
        Check(Render(tiff) == null && Render(webp) == null && Render([.. "DDS "u8, .. new byte[124]]) == null,
              $"S-02, un TIFF di {tiff.Length} byte (prima diventava un logo), un WebP e un DDS: rifiutati dalla firma, senza cercare un codec");

        var huge = HugePng(16_000);
        static long Private()
        {
            using var process = Process.GetCurrentProcess();
            return process.PrivateMemorySize64;
        }
        long start = Private(), top = start;
        using var done = new CancellationTokenSource();
        var sampler = Task.Run(() =>
        {
            while (!done.IsCancellationRequested)
            {
                top = Math.Max(top, Private());
                Thread.Sleep(5);
            }
        });
        var clock = Stopwatch.StartNew();
        bool refused = Render(huge) == null && Render(Ico(huge)) == null;
        long ms = clock.ElapsedMilliseconds;
        top = Math.Max(top, Private());
        done.Cancel();
        sampler.Wait();
        long grown = (top - start) / 1048576;
        Check(refused && grown < 60 && ms < 300,
              $"S-02, un PNG di {huge.Length / 1024} KB che dichiara 16.000 x 16.000 pixel, da solo e dentro un .ico: rifiutato in {ms} ms, memoria privata al massimo +{grown} MB (prima 245 MB l'uno)");

        (string Name, byte[] Data)[] good =
        [
            ("PNG", LogoPicture(new PngBitmapEncoder())), ("JPEG", LogoPicture(new JpegBitmapEncoder { QualityLevel = 100 })), ("GIF", LogoPicture(new GifBitmapEncoder())),
            ("BMP", LogoPicture(new BmpBitmapEncoder())), ("ICO con un PNG", Ico(LogoPicture(new PngBitmapEncoder()))), ("ICO con un bitmap", IcoBitmap()), ("PNG di 1024 pixel", LogoPicture(new PngBitmapEncoder(), 1024)),
        ];
        static bool Near((int R, int G, int B)? got, int r, int g, int b) => got is { } p && Math.Abs(p.R - r) + Math.Abs(p.G - g) + Math.Abs(p.B - b) < 24;
        var wrong = good.Where(g => Render(g.Data) is not { } png || !Near(LogoPixel(png, 4, 4), 0x11, 0x33, 0x55) || !Near(LogoPixel(png, 48, 48), 255, 255, 255)).Select(g => g.Name).ToList();
        Check(wrong.Count == 0, "S-02, le icone di sempre si disegnano ancora: PNG, JPEG, GIF, BMP, ICO con un PNG e con un bitmap, PNG di 1024 pixel" + (wrong.Count == 0 ? "" : $" [sbagliate: {string.Join(", ", wrong)}]"));

        // Misure oltre il limite o intestazioni bugiarde: rifiutate leggendo solo l'intestazione.
        byte[] bigFrame = [.. "GIF89a"u8, 64, 0, 64, 0, 0x80, 0, 0, 0, 0, 0, 255, 255, 255, 0x21, 0xF9, 4, 0, 0, 0, 0, 0, 0x2C, 0, 0, 0, 0, 0x88, 0x13, 0x88, 0x13, 0, 2, 2, 0x4C, 1, 0, 0x3B];
        var bmp = LogoPicture(new BmpBitmapEncoder());
        var bigBmp = (byte[])bmp.Clone();
        BitConverter.GetBytes(40_000).CopyTo(bigBmp, 18);
        var jpeg = LogoPicture(new JpegBitmapEncoder());
        var bigJpeg = (byte[])jpeg.Clone();
        for (int i = 2; i + 9 < bigJpeg.Length;)
        {
            if (bigJpeg[i] != 0xFF) break;
            if (bigJpeg[i + 1] is 0xC0 or 0xC1 or 0xC2)
            {
                (bigJpeg[i + 5], bigJpeg[i + 6]) = (0xFF, 0xFF); // alta 65.535
                break;
            }
            i += 2 + (bigJpeg[i + 2] << 8 | bigJpeg[i + 3]);
        }
        (string Name, byte[] Data)[] lying =
        [
            ("PNG di 1025 pixel", LogoPicture(new PngBitmapEncoder(), 1025)), ("GIF con un fotogramma di 5000 pixel", bigFrame), ("BMP largo 40.000", bigBmp), ("JPEG alto 65.535", bigJpeg),
            ("ICO con 200 immagini", [0, 0, 1, 0, 200, 0, .. new byte[200 * 16]]), ("ICO con l'immagine fuori dal file", [0, 0, 1, 0, 1, 0, 64, 64, 0, 0, 1, 0, 32, 0, 0, 0, 16, 0, 22, 0, 0, 0, 1, 2, 3]),
            ("PNG tagliato", LogoPicture(new PngBitmapEncoder())[..20]), ("testo", Encoding.ASCII.GetBytes("<html>non trovato</html>")),
        ];
        var taken = lying.Where(l => Render(l.Data) != null).Select(l => l.Name).ToList();
        Check(taken.Count == 0 && !bigJpeg.AsSpan().SequenceEqual(jpeg),
              $"S-02, {lying.Length} immagini oltre 1024 pixel o con l'intestazione che non torna: rifiutate" + (taken.Count == 0 ? "" : $" [prese: {string.Join(", ", taken)}]"));
    }

    const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 10 10\">";
    const string SvgBlue = "<rect width=\"10\" height=\"10\" fill=\"#113355\"/>";

    static byte[]? RenderSvg(byte[] data) => Task.Run(() => SenderLogos.RenderSvgAsync(data)).GetAwaiter().GetResult();
    static byte[]? RenderSvg(string svg) => RenderSvg(new UTF8Encoding(false).GetBytes(svg));

    /// <summary>
    /// S-01: prima il filtro era una lista nera sul testo e SharpVectors, con le sue impostazioni, usciva: un SVG con
    /// &#64;import, in UTF-16 o gzip passava il filtro e faceva partire richieste http anche verso 127.0.0.1.
    /// </summary>
    static void SvgAuditTests()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port, knocks = 0;
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref knocks);
                }
            }
            catch (Exception) { }
        });
        string http = $"http://127.0.0.1:{port}";
        static byte[] Utf16(string s) => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(s)];
        static byte[] Gzip(string s)
        {
            // Con "<svg" nel campo del nome del file: il filtro di prima ci trovava quello che cercava.
            var data = Encoding.UTF8.GetBytes(s);
            using var output = new MemoryStream();
            output.Write([0x1F, 0x8B, 8, 0x08, 0, 0, 0, 0, 0, 0xFF, (byte)'<', (byte)'s', (byte)'v', (byte)'g', 0]);
            using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true)) deflate.Write(data);
            uint crc = 0xFFFFFFFF;
            foreach (var b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            output.Write(BitConverter.GetBytes(~crc));
            output.Write(BitConverter.GetBytes((uint)data.Length));
            return output.ToArray();
        }
        (string Name, byte[] Data)[] outside =
        [
            ("&#64;import", Encoding.UTF8.GetBytes($"{SvgOpen}<style>&#64;import \"{http}/a.css\";</style>{SvgBlue}</svg>")),
            ("UTF-16 con un'immagine", Utf16($"{SvgOpen}<!--猼杶-->{SvgBlue}<image xlink:href=\"{http}/b.png\" width=\"5\" height=\"5\"/></svg>")),
            ("gzip con un'immagine", Gzip($"{SvgOpen}{SvgBlue}<image xlink:href=\"{http}/c.png\" width=\"5\" height=\"5\"/></svg>")),
            ("UTF-16 con un foglio di stile", Utf16($"<?xml-stylesheet type=\"text/css\" href=\"{http}/d.css\"?>{SvgOpen}<!--猼杶-->{SvgBlue}</svg>")),
            ("UTF-16 con use", Utf16($"{SvgOpen}<!--猼杶-->{SvgBlue}<use xlink:href=\"{http}/e.svg#g\"/></svg>")),
            ("UTF-16 con un colore preso fuori", Utf16($"{SvgOpen}<!--猼杶-->{SvgBlue}<rect width=\"5\" height=\"5\" fill=\"url({http}/f.svg#g)\"/></svg>")),
        ];
        var drawn = new List<string>();
        foreach (var (name, data) in outside)
            if (RenderSvg(data) != null) drawn.Add(name);
        Thread.Sleep(300);
        Check(Volatile.Read(ref knocks) == 0 && drawn.Count == 0,
              $"S-01, sei SVG che puntano a 127.0.0.1 (&#64;import, UTF-16, gzip: immagine, foglio di stile, use, colore): nessuna connessione ({Volatile.Read(ref knocks)}) e nessun logo ({string.Join(", ", drawn)})");
        listener.Stop();

        // Percorsi di rete (\\server\...): con un nome che non esiste Windows ci mette più di due secondi a dire di no.
        var clock = Stopwatch.StartNew();
        bool unc = RenderSvg(Utf16($"{SvgOpen}<!--猼杶-->{SvgBlue}<use xlink:href=\"\\\\nome-che-non-esiste-missiva-prova-1\\x\\a.svg#g\"/></svg>")) == null
                   && RenderSvg(Utf16($"{SvgOpen}<!--猼杶--><style>@import \"file://nome-che-non-esiste-missiva-prova-2/x/b.css\";</style>{SvgBlue}</svg>")) == null
                   && RenderSvg($"{SvgOpen}{SvgBlue}<use href=\"file://nome-che-non-esiste-missiva-prova-3/x/c.svg#g\"/></svg>") == null;
        Check(unc && clock.ElapsedMilliseconds < 1500, $"S-01, tre SVG che puntano a un percorso di rete: nessun logo e nessuna ricerca del server ({clock.ElapsedMilliseconds} ms; prima più di 2 s l'uno)");

        // Il filtro: quello che resta di un SVG normale, scritto per esteso.
        static string? Clean(string svg) => LogoSvg.Clean(Encoding.UTF8.GetBytes(svg));
        const string full =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- Generatore di prova -->\n" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" xmlns:inkscape=\"http://www.inkscape.org/namespaces/inkscape\" version=\"1.2\" " +
            "baseProfile=\"tiny-ps\" viewBox=\"0 0 10 10\" onload=\"x()\" inkscape:version=\"1.3\" data-nome=\"prova\">\n" +
            "  <title>Esempio Prova</title>\n  <metadata><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><image/></rdf:RDF></metadata>\n" +
            "  <defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#113355\"/><stop offset=\"1\" stop-color=\"#3366aa\"/></linearGradient>" +
            "<clipPath id=\"c\"><circle cx=\"5\" cy=\"5\" r=\"5\"/></clipPath><filter id=\"f\"><feImage xlink:href=\"https://x.example/a.png\"/></filter></defs>\n" +
            "  <style>/* nota */ .a{fill:url(#g);cursor:pointer} @media (prefers-color-scheme: dark){.a{fill:#fff}} rect:hover{fill:red}</style>\n" +
            "  <g clip-path=\"url(#c)\" filter=\"url(#f)\"><rect class=\"a\" width=\"10\" height=\"10\" style=\"stroke:#000; -inkscape-stroke:none; stroke-width:0.5\" onclick=\"x()\"/>" +
            "<use xlink:href=\"#r\"/><text x=\"1\" y=\"8\" font-family=\"'Segoe UI', Arial\">Prova <tspan>due</tspan></text></g>\n</svg>";
        const string cleaned =
            "<svg version=\"1.2\" baseProfile=\"tiny-ps\" viewBox=\"0 0 10 10\" xmlns=\"http://www.w3.org/2000/svg\">" +
            "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#113355\" /><stop offset=\"1\" stop-color=\"#3366aa\" /></linearGradient>" +
            "<clipPath id=\"c\"><circle cx=\"5\" cy=\"5\" r=\"5\" /></clipPath></defs><style>.a{fill:url(#g);}</style>" +
            "<g clip-path=\"url(#c)\"><rect class=\"a\" width=\"10\" height=\"10\" style=\"stroke:#000;stroke-width:0.5;\" />" +
            "<use xlink:href=\"#r\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" /><text x=\"1\" y=\"8\" font-family=\"'Segoe UI', Arial\">Prova <tspan>due</tspan></text></g></svg>";
        var got = Clean(full);
        Check(got == cleaned, "S-01, il filtro riscrive l'SVG: restano il disegno, gli stili semplici e i riferimenti interni; via commenti, metadati, titolo, filtri, gestori, " +
                              "attributi di altri programmi, @media e :hover" + (got == cleaned ? "" : $" [{got}]"));

        static string Times(string text, int times) => string.Concat(Enumerable.Repeat(text, times));
        static string Nest(int groups) => SvgOpen + Times("<g>", groups) + SvgBlue + Times("</g>", groups) + "</svg>";
        static string Bomb(int levels) =>
            SvgOpen + "<defs><g id=\"a0\">" + SvgBlue + "</g>" + string.Concat(Enumerable.Range(1, levels).Select(i => $"<g id=\"a{i}\">" + Times($"<use href=\"#a{i - 1}\"/>", 10) + "</g>")) +
            $"</defs><use href=\"#a{levels}\"/></svg>";
        (string Name, string Svg)[] refused =
        [
            ("DTD", "<!DOCTYPE svg [<!ENTITY a \"x\">]>" + SvgOpen + SvgBlue + "</svg>"),
            ("script", SvgOpen + SvgBlue + "<script>alert(1)</script></svg>"),
            ("immagine incorporata", SvgOpen + SvgBlue + "<image href=\"data:image/png;base64,AAAA\" width=\"5\" height=\"5\"/></svg>"),
            ("foreignObject", SvgOpen + SvgBlue + "<foreignObject width=\"5\" height=\"5\"><p xmlns=\"http://www.w3.org/1999/xhtml\">x</p></foreignObject></svg>"),
            ("use esterno", SvgOpen + SvgBlue + "<use xlink:href=\"https://x.example/a.svg#b\"/></svg>"),
            ("use di un file accanto", SvgOpen + SvgBlue + "<use href=\"a.svg#b\"/></svg>"),
            ("colore preso fuori", SvgOpen + "<rect width=\"10\" height=\"10\" fill=\"url(https://x.example/a.svg#g)\"/></svg>"),
            ("colore preso fuori, nello stile, con u&#114;l", SvgOpen + "<rect width=\"10\" height=\"10\" style=\"fill:u&#114;l(https://x.example/a.svg#g)\"/></svg>"),
            ("url( mai chiusa", SvgOpen + "<rect width=\"10\" height=\"10\" style=\"fill:url(#g\"/></svg>"),
            ("rgb( mai chiusa", SvgOpen + "<rect width=\"10\" height=\"10\" fill=\"rgb(1,2\"/></svg>"),
            ("&#64;import", SvgOpen + "<style>&#64;import 'https://x.example/a.css';</style>" + SvgBlue + "</svg>"),
            ("u\\72l() nel foglio di stile", SvgOpen + "<style>rect{fill:u\\72l(https://x.example/a.svg#g)}</style>" + SvgBlue + "</svg>"),
            ("url() esterna nel foglio di stile", SvgOpen + "<style>rect{fill:url(https://x.example/a.svg#g)}</style>" + SvgBlue + "</svg>"),
            ("xml-stylesheet", "<?xml-stylesheet type=\"text/css\" href=\"https://x.example/a.css\"?>" + SvgOpen + SvgBlue + "</svg>"),
            ("65 livelli", Nest(63)),
            ("3.000 gruppi annidati", Nest(3000)),
            ("use che si moltiplicano", Bomb(6)),
            ("use che usa sé stesso", SvgOpen + "<g id=\"a\">" + SvgBlue + "<use href=\"#a\"/></g></svg>"),
            ("70 use in catena", SvgOpen + SvgBlue + string.Concat(Enumerable.Range(0, 70).Select(i => $"<use id=\"u{i}\" href=\"#u{i + 1}\"/>")) + "</svg>"),
            ("maschera che usa sé stessa", SvgOpen + "<mask id=\"m\"><rect width=\"10\" height=\"10\" fill=\"#fff\" mask=\"url(#m)\"/></mask><rect width=\"10\" height=\"10\" mask=\"url(#m)\"/></svg>"),
            ("motivo dentro un motivo", SvgOpen + "<pattern id=\"p\" width=\"1\" height=\"1\">" + SvgBlue + "</pattern><pattern id=\"q\" width=\"1\" height=\"1\"><rect width=\"1\" height=\"1\" fill=\"url(#p)\"/></pattern>" +
                                        "<rect width=\"10\" height=\"10\" fill=\"url(#q)\"/></svg>"),
            ("motivo dato da un foglio di stile", SvgOpen + "<style>.a{fill:url(#p)}</style><pattern id=\"p\" width=\"1\" height=\"1\">" + SvgBlue + "</pattern><rect class=\"a\" width=\"10\" height=\"10\"/></svg>"),
            ("motivo ereditato dal suo contenitore", SvgOpen + "<g fill=\"url(#p)\"><pattern id=\"p\" width=\"1\" height=\"1\"><rect width=\"1\" height=\"1\"/></pattern><rect width=\"10\" height=\"10\"/></g></svg>"),
            ("motivo di 200 elementi su 200 elementi", SvgOpen + "<pattern id=\"p\" width=\"1\" height=\"1\">" + Times(SvgBlue, 200) + "</pattern><g fill=\"url(#p)\">" +
                                                       Times("<rect width=\"1\" height=\"1\"/>", 200) + "</g></svg>"),
            ("10.001 elementi", SvgOpen + Times(SvgBlue, 10_000) + "</svg>"),
            ("una pagina HTML", "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><svg xmlns=\"http://www.w3.org/2000/svg\"/></body></html>"),
            ("XML scritto male", SvgOpen + SvgBlue + "<g></svg>"),
            ("un byte nullo", SvgOpen + SvgBlue + "<!--\0--></svg>"),
        ];
        var passed = refused.Where(r => Clean(r.Svg) != null).Select(r => r.Name).ToList();
        // Le regole @ non arrivano mai a SharpVectors, nemmeno spezzate da un commento.
        if (Clean(SvgOpen + "<style>@imp/**/ort 'https://x.example/a.css'; @font-face{font-family:x;src:url(https://x.example/a.ttf)} rect{fill:#135}</style>" + SvgBlue + "</svg>")
            is not { } rules || rules.Contains("x.example") || rules.Contains('@') || !rules.Contains("<style>rect{fill:#135;}</style>"))
            passed.Add("regole @");
        Check(passed.Count == 0 && LogoSvg.Clean(Utf16(SvgOpen + SvgBlue + "</svg>")) == null && LogoSvg.Clean(Gzip(SvgOpen + SvgBlue + "</svg>")) == null,
              $"S-01, {refused.Length + 2} SVG rifiutati dal filtro: DTD, script, immagini, riferimenti esterni comunque scritti, parentesi mai chiuse, annidamenti e " +
              $"moltiplicazioni oltre i tetti, cicli, UTF-16, gzip" + (passed.Count == 0 ? "" : $" [passano: {string.Join(", ", passed)}]"));

        (string Name, string Svg)[] accepted =
        [
            ("64 livelli", Nest(62)),
            ("10.000 elementi", SvgOpen + Times(SvgBlue, 9_999) + "</svg>"),
            ("sfumatura e ritaglio da un foglio di stile", SvgOpen + "<style>.a{fill:url(#g)}.b{clip-path:url(#c)}</style><linearGradient id=\"g\" xlink:href=\"#h\"/><linearGradient id=\"h\"><stop offset=\"0\"/></linearGradient>" +
                                                           "<clipPath id=\"c\"><rect width=\"5\" height=\"5\"/></clipPath><g class=\"b\"><rect class=\"a\" width=\"10\" height=\"10\"/></g></svg>"),
            ("simbolo usato dieci volte", SvgOpen + "<symbol id=\"s\" viewBox=\"0 0 1 1\">" + SvgBlue + "</symbol>" + string.Concat(Enumerable.Range(0, 10).Select(i => $"<use href=\"#s\" x=\"{i}\" width=\"1\" height=\"1\"/>")) + "</svg>"),
            ("un motivo su venti elementi", SvgOpen + "<pattern id=\"p\" width=\"1\" height=\"1\" patternUnits=\"userSpaceOnUse\">" + SvgBlue + "</pattern><g fill=\"url(#p)\">" +
                                            Times("<rect width=\"1\" height=\"1\"/>", 20) + "</g></svg>"),
            ("una maschera su un gruppo", SvgOpen + "<mask id=\"m\"><rect width=\"10\" height=\"10\" fill=\"#fff\"/></mask><g mask=\"url(#m)\">" + SvgBlue + "</g></svg>"),
            ("la radice che usa una sfumatura definita dentro", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\" fill=\"url(#g)\"><linearGradient id=\"g\"><stop offset=\"0\"/></linearGradient><rect width=\"10\" height=\"10\"/></svg>"),
            ("senza lo spazio dei nomi", "<svg viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\"/></svg>"),
        ];
        var lost = accepted.Where(a => Clean(a.Svg) == null).Select(a => a.Name).ToList();
        Check(lost.Count == 0, $"S-01, {accepted.Length} SVG normali accettati: fino a 64 livelli e 10.000 elementi, sfumature e ritagli dai fogli di stile, simboli, un motivo, una maschera" +
                               (lost.Count == 0 ? "" : $" [rifiutati: {string.Join(", ", lost)}]"));

        // I loghi si disegnano ancora: un quadrato blu con un cerchio bianco in mezzo, scritto come un BIMI, come l'icona di un sito
        // con il tema scuro (@media: prima SharpVectors non finiva mai) e con classi, sfumatura, ritaglio e use.
        const string circle = "<circle cx=\"5\" cy=\"5\" r=\"3\" fill=\"#ffffff\"/>";
        (string Name, string Svg)[] logos =
        [
            ("come un BIMI", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<svg version=\"1.2\" baseProfile=\"tiny-ps\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\" xml:space=\"preserve\">" +
                             "<title>Esempio Prova</title><path d=\"M0 0h10v10H0z\" fill=\"#113355\"/>" + circle + "</svg>"),
            ("icona con il tema scuro", SvgOpen + "<style>.f{fill:#113355}@media (prefers-color-scheme: dark){.f{fill:#ffffff}}</style><rect class=\"f\" width=\"10\" height=\"10\"/>" + circle + "</svg>"),
            ("classi, sfumatura, ritaglio e use", SvgOpen + "<defs><style type=\"text/css\"><![CDATA[.st0{fill:url(#g);}.st1{clip-path:url(#c);fill:#FFFFFF;}]]></style>" +
                "<linearGradient id=\"g\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"0\" x2=\"10\" y2=\"0\"><stop offset=\"0\" style=\"stop-color:#113355\"/><stop offset=\"1\" style=\"stop-color:#113355\"/></linearGradient>" +
                "<clipPath id=\"c\"><circle cx=\"5\" cy=\"5\" r=\"3\"/></clipPath><rect id=\"q\" width=\"2\" height=\"2\" fill=\"#113355\"/></defs>" +
                "<rect class=\"st0\" width=\"10\" height=\"10\"/><rect class=\"st1\" width=\"10\" height=\"10\"/><use xlink:href=\"#q\"/></svg>"),
        ];
        clock.Restart();
        var wrong = logos.Where(l => RenderSvg(l.Svg) is not { } png || LogoPixel(png, 3, 3) != (0x11, 0x33, 0x55) || LogoPixel(png, 48, 48) != (255, 255, 255)).Select(l => l.Name).ToList();
        Check(wrong.Count == 0 && clock.ElapsedMilliseconds < 4000,
              $"S-01, tre loghi disegnati come prima (blu negli angoli, bianco al centro): come un BIMI, con @media del tema scuro, con classi sfumatura ritaglio e use; {clock.ElapsedMilliseconds} ms" +
              (wrong.Count == 0 ? "" : $" [sbagliati: {string.Join(", ", wrong)}]"));

        // Un disegno che non finisce si ferma davvero, e dopo si disegna ancora. Che il suo thread non resti a girare si vede
        // dal thread stesso, che finisce (Join), e dai giri del suo ciclo, che non crescono più: prima si misurava il tempo
        // di processore di tutto il processo nei 400 ms dopo (meno di 200 ms), che conta anche la raccolta della memoria e
        // i lavori rimasti delle prove di prima (266 ms in un giro su cinque: audit dell'11 ottobre, C-42). E che sia
        // stato fermato al limite, non lasciato a girare dopo il doppio del tempo, lo dice DrawingSuspended.
        clock.Restart();
        Thread? drawing = null;
        long spins = 0;
        var stuck = Task.Run(() => SenderLogos.OnStaAsync(() =>
        {
            drawing = Thread.CurrentThread;
            while (true)
            {
                Thread.SpinWait(100);
                Interlocked.Increment(ref spins);
            }
        }, 1)).GetAwaiter().GetResult();
        long waited = clock.ElapsedMilliseconds;
        bool ended = drawing?.Join(TimeSpan.FromSeconds(5)) == true;
        long atStop = Interlocked.Read(ref spins);
        Thread.Sleep(50);
        long later = Interlocked.Read(ref spins);
        Check(stuck == null && waited >= 900 && ended && atStop > 0 && later == atStop && !SenderLogos.DrawingSuspended && LogoPixel(RenderSvg(logos[0].Svg), 48, 48) == (255, 255, 255),
              $"S-01, un disegno che non finisce: fermato dopo {waited} ms (limite di prova 1 s), il suo thread è finito e non resta a girare " +
              $"({(ended ? "finito" : "ancora vivo dopo 5 s")}, {later - atStop} giri del ciclo dopo la fermata) e il logo dopo si disegna");
    }

    /// <summary>
    /// MailHtml con HTML costruiti apposta (audit, V-01, V-02, V-03, V-04, V-07, S-29, S-30): niente overflow dello stack, ogni
    /// caso entro un tetto di tempo largo per la Debug, nessuna eccezione nel tema scuro, url('...') con parentesi bloccato,
    /// ParseColor uguale a prima, e le uscite di messaggi normali uguali carattere per carattere a quelle di prima.
    /// </summary>
    static void HtmlAuditTests()
    {
        Report.Add("--- Audit, blocco 1: la pulizia dell'HTML (MailHtml) con messaggi costruiti apposta");
        static string Wrap(string body) => "<html><head></head><body>" + body + "</body></html>";
        static string Repeat(string text, int times) => new StringBuilder(text.Length * times).Insert(0, text, times).ToString();
        static string? NoCid(string id) => null;

        // V-01: prima TextContent di AngleSharp, ricorsivo, finiva lo stack (sul PC dell'autore a 8.031 livelli con 1,5 MB, a 1.292
        // con 256 KB) e il processo si chiudeva con 0xC00000FD. Su un thread con lo stack piccolo: se torna, non c'è ricorsione.
        string? deepText = null;
        MailHtml.Result? deepLink = null;
        Exception? deepError = null;
        var small = new Thread(() =>
        {
            try
            {
                deepText = MailHtml.ReadableText(Wrap(Repeat("<div>x", 20_000)));
                deepLink = MailHtml.Prepare("<html><body><a href=\"https://www.esempio-prova.example/\">" + Repeat("<span>x", 20_000) + "</a></body></html>", false, false, NoCid);
            }
            catch (Exception ex)
            {
                deepError = ex;
            }
        }, 256 * 1024);
        var clock = Stopwatch.StartNew();
        small.Start();
        small.Join();
        Check(deepError == null && deepText?.Count(c => c == 'x') == 20_000 && deepLink is { PlainOnly: true } && deepLink.Fragment.Contains(Repeat("x", 20_000))
              && clock.ElapsedMilliseconds < 5000,
              $"20.000 elementi annidati su un thread con 256 KB di stack: testo e pagina pronti in {clock.ElapsedMilliseconds} ms, senza chiudere il processo ({deepError?.GetType().Name ?? "nessun errore"})");

        // Il tetto di profondità è quello di Blink (512 elementi aperti): fino a lì il messaggio si mostra com'è.
        var under = MailHtml.Prepare(Wrap(Repeat("<div>x", 510)), true, false, NoCid);
        var over = MailHtml.Prepare(Wrap(Repeat("<div>x", 511)), true, false, NoCid);
        Check(!under.PlainOnly && under.Fragment.StartsWith("<div class=\"missiva-body\"><div>x<div>x") && under.Fragment.EndsWith("</div></div></div>")
              && MailHtml.ReadableText(Wrap(Repeat("<div>x", 510))) == Repeat("x", 510)
              && over.PlainOnly && over.Fragment.StartsWith("<div class=\"missiva-body\"><div style=\"white-space: pre-wrap\">x\nx\n") && over.Links.Count == 0
              && MailHtml.ReadableText(Wrap(Repeat("<div>x", 511))) == string.Join("\n", Enumerable.Repeat("x", 511)),
              "510 <div> annidati (512 livelli con html e body) si mostrano come sono; con 511 solo il testo, una riga per blocco");
        var plain = MailHtml.Prepare(Wrap("<p>Gentile cliente,</p>" + Repeat("<div>riga con https://www.esempio-prova.example/pagina?a=1&b=2 e <b>grassetto</b> ", 600)
                                          + "<script>alert(1)</script><style>p{color:red}</style><!-- nota --><br>3 &lt; 5 &amp; fine"), false, false, NoCid);
        Check(plain.PlainOnly && plain.BlockedImages == 0 && plain.Links.Count == 600 && plain.Links.All(l => l is { Href: "https://www.esempio-prova.example/pagina?a=1&b=2", Text: "https://www.esempio-prova.example/pagina?a=1&b=2" })
              && plain.Fragment.StartsWith("<div class=\"missiva-body\"><div style=\"white-space: pre-wrap\">Gentile cliente,\nriga con <a href=\"https://www.esempio-prova.example/pagina?a=1&amp;b=2\">")
              && plain.Fragment.EndsWith(" e grassetto\n3 &lt; 5 &amp; fine</div></div>") && !plain.Fragment.Contains("alert") && !plain.Fragment.Contains("color:red") && !plain.Fragment.Contains("nota"),
              "oltre il tetto: il testo senza script, stili e commenti, le entità lette, gli indirizzi web come link (con il controllo dei link di sempre)");
        MessageSummary summary = new() { Id = "1", FolderId = "prova", Subject = "Prova", Date = new DateTime(2026, 10, 4, 9, 0, 0), From = new Address { Name = "Mario Rossi", Email = "mario.rossi@esempio-prova.example" } };
        string Page(string html) => UI.ReadingPage.Build([new UI.PageEntry(summary, new MessageBody { Html = html }, true, false, null, false)], light: true).Html;
        const string band = "Messaggio troppo complesso: mostrato come solo testo.";
        Check(Page(Wrap(Repeat("<div>x", 600))).Contains("<div class=\"warn\"") && Page(Wrap(Repeat("<div>x", 600))).Contains(band) && !Page(Wrap(Repeat("<div>x", 100))).Contains(band),
              $"nel riquadro la fascia \"{band}\" solo per il messaggio oltre il tetto");

        // Ogni caso entro un tetto largo per la Debug: sul PC dell'autore prima erano secondi (fra parentesi nella riga), ora millisecondi.
        void Fast(string what, string html, bool plainOnly, string before, int limit = 1500)
        {
            var watch = Stopwatch.StartNew();
            var light = MailHtml.Prepare(html, false, false, NoCid);
            long lightMs = watch.ElapsedMilliseconds;
            watch.Restart();
            var dark = MailHtml.Prepare(html, true, false, NoCid);
            long darkMs = watch.ElapsedMilliseconds;
            watch.Restart();
            MailHtml.ReadableText(html);
            long textMs = watch.ElapsedMilliseconds;
            Check(lightMs < limit && darkMs < limit && textMs < limit && light.PlainOnly == plainOnly && dark.PlainOnly == plainOnly,
                  $"{what} ({html.Length / 1024} KB): pulizia in {lightMs} ms col tema chiaro e {darkMs} ms con lo scuro, testo in {textMs} ms (tetto {limit} ms; prima {before}); " +
                  (plainOnly ? "mostrato come solo testo" : "mostrato com'è"));
        }
        Fast("V-02, 100.000 \"<\" nel testo", Wrap("<p>" + new string('<', 100_000) + "</p>"), false, "1,2 s e 9,5 GB chiesti");
        Fast("V-02, 40.000 \"a < b\"", Wrap("<p>" + Repeat("a < b ", 40_000) + "</p>"), false, "1,1 s e 9,2 GB");
        Fast("S-30, testo spezzato da 40.000 tag di chiusura che non chiudono nulla", Wrap("<p>" + Repeat("x</i>", 40_000)), true, "1,5 GB chiesti dalla sola analisi");
        Fast("V-03, 20.000 <div> mai chiusi", Wrap(Repeat("<div>x", 20_000)), true, "4,5 s chiaro, 6,5 s scuro, 14,9 s il testo");
        Fast("V-03, 20.000 <b><i> mai chiusi", Wrap(Repeat("<b><i>x", 20_000)), true, "4,7 s chiaro, 22,9 s scuro, 14,3 s il testo");
        Fast("V-03, 1.000 tabelle annidate",
             Wrap(string.Concat(Enumerable.Range(0, 1000).Select(d => $"<table width=\"100%\" style=\"border:1px solid #cccccc;background-color:#fafafa\"><tr><td style=\"padding:2px;color:#333333\">Livello {d} <a href=\"https://x.example/{d}\">link</a>"))
                  + "Fondo" + Repeat("</td></tr></table>", 1000)), true, "0,13 s chiaro, 0,30 s scuro, 0,60 s il testo");
        Fast("S-30, 2.000 <font> di colore diverso mai chiusi, riaperti in ogni paragrafo",
             Wrap(string.Concat(Enumerable.Range(0, 2000).Select(k => $"<p><font color=\"#{k:X6}\">x"))), true, "18 s chiaro, 55 s scuro e 3,7 GB: 2 milioni di elementi da 51 KB");
        Fast("S-30, 500 <font> mai chiusi riaperti in 20.000 paragrafi",
             Wrap(string.Concat(Enumerable.Range(0, 500).Select(k => $"<font color=\"#{k:X6}\">")) + Repeat("<p>x", 20_000)), true, "non misurato: 10 milioni di elementi");
        Fast("S-30, un tag con 20.000 attributi", Wrap("<div " + string.Concat(Enumerable.Range(0, 20_000).Select(k => $"onx{k}=1 ")) + ">x</div>"), true, "0,05 s, col quadrato degli attributi");
        Fast("100.000 righe chiuse da <br>", Wrap("<div>" + Repeat("riga<br>", 100_000) + "</div>"), false, "16,4 s chiaro, 19,9 s scuro, 128 s il testo", 4000);
        Fast("100.000 <input> da togliere", Wrap(Repeat("<input name=\"a\">", 100_000) + "<p>fine</p>"), false, "1,2 s", 4000);
        Fast("V-04, \"<meta \" 10.000 volte senza chiusura", "<html><head>" + Repeat("<meta ", 10_000) + "</head><body>x</body></html>", false, "1,7 s con il tema scuro");
        Fast("V-04, style con \"url(\" 10.000 volte", Wrap("<div style=\"background:" + Repeat("url(", 10_000) + "\">x</div>"), false, "12,5 s chiaro, 13,0 s scuro");
        Fast("V-04, src con \"pixel\" 20.000 volte", Wrap("<img src=\"https://img.esempio-prova.example/" + Repeat("pixel", 20_000) + "\">"), false, "0,9 s chiaro, 1,1 s scuro");
        Fast("V-04, attributo con \"url(\" e 30.000 spazi", Wrap("<div data-x=\"url(" + new string(' ', 30_000) + "x\">x</div>"), false, "2,5 s");
        Fast("V-04, style con una parola di 30.000 lettere e una \\", Wrap("<div style=\"font-family:" + new string('a', 30_000) + " \\\">x</div>"), false, "3,7 s chiaro, 3,9 s scuro");
        Fast("S-29, style con 2.000 \\ di fila", Wrap("<div style=\"" + new string('\\', 2000) + "\">x</div>"), false, "3,9 s chiaro, 4,0 s scuro");
        Fast("S-29, style con 50.000 \\ di fila", Wrap("<div style=\"" + new string('\\', 50_000) + "\">x</div>"), false, "non misurato: a 2.000 erano 4 s, e ogni raddoppio otto volte tanto");
        Fast("V-04, style con color: e \"rgb(\" 20.000 volte", Wrap("<div style=\"color:" + Repeat("rgb(", 20_000) + "\">x</div>"), false, "0,16 s e 20.000 eccezioni con il tema scuro");

        // V-07: nel tema scuro ColorConverter lanciava una FormatException per ogni parola che non è un colore.
        var styled = new StringBuilder("<html><head><style>");
        for (int i = 0; i < 100; i++)
            styled.Append($".r{i} td, body .r{i} > a {{ color: #333333; background: #fafafa url(https://img.esempio-prova.example/bg{i}.png) no-repeat center top; border: 1px solid #cccccc !important; outline: thin dotted Gray; }}\n");
        styled.Append("p { color: rgb(10, 20, 30); background-color: rgba(0,0,0,0.5); border-color: hsl(120, 50%, 50%) transparent currentColor inherit; background: linear-gradient(to right, #fff, #00000080) }");
        styled.Append("a { color: var(--blu); border: 2px dashed rgb(var(--r), 0, 0); background: #12345 none; outline-color: rgb(10 20 30 / 50%) } </style></head><body bgcolor=\"white\" text=\"#222222\">");
        for (int i = 0; i < 200; i++)
            styled.Append($"<table bgcolor=\"#f2f2f2\"><tr><td style=\"color:#333333;background-color:#f4f4f4;border:1px solid #dddddd;font-size:14px;padding:4px 6px\">voce {i} <font color=\"navy\">blu</font></td></tr></table>");
        styled.Append("<div style=\"background:#0a66c2 url('https://img.esempio-prova.example/p.png') no-repeat;color:white\"><span style=\"color:black\">pulsante</span></div></body></html>");
        int thrown = 0, thread = Environment.CurrentManagedThreadId;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> count = (_, _) => { if (Environment.CurrentManagedThreadId == thread) thrown++; };
        AppDomain.CurrentDomain.FirstChanceException += count;
        MailHtml.Result darkResult;
        try
        {
            darkResult = MailHtml.Prepare(styled.ToString(), true, false, NoCid);
            MailHtml.Prepare(styled.ToString(), true, true, NoCid);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= count;
        }
        Check(thrown == 0 && darkResult.Fragment.Contains("border: 1px solid #404040 !important") && darkResult.Fragment.Contains("outline: thin dotted #666666")
              && darkResult.Fragment.Contains("<span style=\"color:black\">pulsante</span>") && darkResult.Fragment.Contains("<font color=\"#7F7FFF\">blu</font>")
              && darkResult.Fragment.Contains("border: 2px dashed rgb(var(--r), 0, 0); background: #12345 none; outline-color: rgba(45,64,83,0.498)"),
              $"tema scuro, 100 regole e 200 celle con px, solid, none, no-repeat, !important, rgb(), hsl(), var(): {thrown} eccezioni (prima una per parola che non è un colore), colori adattati come prima");

        // ParseColor: null esattamente dove dava null prima (dove ColorConverter lancia), senza più passare dalle eccezioni.
        static Color? Before(string value)
        {
            try
            {
                if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = value[(value.IndexOf('(') + 1)..value.IndexOf(')')].Split(',', ' ', '/').Where(p => p.Length > 0).ToArray();
                    byte C(string p) => (byte)Math.Clamp(p.EndsWith('%') ? double.Parse(p[..^1], CultureInfo.InvariantCulture) * 2.55 : double.Parse(p, CultureInfo.InvariantCulture), 0, 255);
                    double alpha = parts.Length > 3 ? (parts[3].EndsWith('%') ? double.Parse(parts[3][..^1], CultureInfo.InvariantCulture) / 100 : double.Parse(parts[3], CultureInfo.InvariantCulture)) : 1;
                    return Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), C(parts[0]), C(parts[1]), C(parts[2]));
                }
                if (value.StartsWith('#') && value.Length == 4) value = "#" + string.Concat(value[1..].Select(ch => $"{ch}{ch}"));
                if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return null;
                return ColorConverter.ConvertFromString(value) is Color color ? color : null;
            }
            catch
            {
                return null;
            }
        }
        var names = typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(p => p.Name).ToList();
        var random = new Random(11);
        var tokens = new List<string>();
        foreach (var name in names)
        {
            tokens.AddRange([name, name.ToLowerInvariant(), name.ToUpperInvariant(), name + "x", name[1..], name.ToLowerInvariant().Replace('k', '\u212A'), name.Replace("e", "ee")]);
            for (int k = 0; k < 40; k++) tokens.Add(new string(name.Select(c => random.Next(2) == 0 ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray()));
        }
        tokens.AddRange(["px", "solid", "none", "no", "repeat", "center", "top", "important", "auto", "inherit", "currentColor", "transparent", "Transparent", "rgb", "rgba", "hsl", "url", "var", "sc",
                         "grey", "windowtext", "ButtonFace", "rebeccapurple", "ContextColor", "", "a", "blac\u212A", "\u212A",
                         "rgb(0,0,0)", "RGB(255, 255, 255)", "rgb(1 2 3)", "rgba(0,0,0,0.5)", "rgba(0,0,0,50%)", "rgb(100%,0%,50%)", "rgb(0 0 0 / 50%)", "rgb()", "rgb(1,2)", "rgb(a,b,c)", "rgb(1,2,3,x)",
                         "rgb(var(--x)", "rgb(calc(1+2)", "rgb(1e3,2,3)", "rgb(-5,300,1.5)", "rgb(,,,)", "rgb(1,2,3,)", "rgb(NaN,0,0)", "rgb(Infinity,0,0)", "rgb(1.5.2,0,0)", "rgb(1,2,3,4,5)", "rgb(%,1,2)",
                         "rgb(1,2,3,%)", "rgb(1_000,2,3)", "rgb(+1,+2,+3)", "rgb(0x10,2,3)", "rgba(1,2,3,1e-2)", "rgba(1,2,3,.5)", "rgb(1,2,3,-1)", "rgb(1,2,3,200%)", "rgb(\t1,\n2, 3)",
                         "hsl(0,0%,0%)", "hsla(0,0%,0%,0.5)", "HSL()", "url(x)", "url()", "URL('https://img.esempio-prova.example/black/red.png')"]);
        const string hex = "0123456789abcdefABCDEF";
        for (int length = 0; length <= 10; length++)
            for (int k = 0; k < 300; k++) tokens.Add("#" + new string(Enumerable.Range(0, length).Select(_ => hex[random.Next(hex.Length)]).ToArray()));
        for (int k = 0; k < 20_000; k++) tokens.Add(new string(Enumerable.Range(0, 1 + random.Next(10)).Select(_ => (char)((random.Next(2) == 0 ? 'a' : 'A') + random.Next(26))).ToArray()));
        var expected = tokens.Select(Before).ToList();
        thrown = 0;
        AppDomain.CurrentDomain.FirstChanceException += count;
        List<Color?> actual;
        try
        {
            actual = tokens.Select(MailHtml.ParseColor).ToList();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= count;
        }
        var wrong = tokens.Where((_, i) => actual[i] != expected[i]).Take(5).ToList();
        Check(wrong.Count == 0 && thrown == 0 && names.Count == 141 && actual.Count(c => c != null) > names.Count * 43,
              wrong.Count == 0
                  ? $"ParseColor su {tokens.Count} valori (i {names.Count} nomi di Colors con maiuscole a caso e storpiati, # da 0 a 10 cifre, rgb(), hsl(), url(), parole a caso): " +
                    $"gli stessi colori e gli stessi null di prima, {thrown} eccezioni"
                  : "ParseColor diverso da prima per: " + string.Join(", ", wrong));

        // "Fuori tema" del rapporto: url('...') fra virgolette con una parentesi nell'indirizzo passava. Come lo legge il CSS:
        // fra virgolette anche con l'altro tipo di virgolette o con "\'", senza virgolette anche con "\'" (provato con Edge).
        (string Css, string What)[] remote =
        [
            ("background:url('https://img.esempio-prova.example/a(b).png')", "fra apici con parentesi"),
            ("background:url( 'https://img.esempio-prova.example/a\"b.png' )", "fra apici con virgolette"),
            ("background:url('https://img.esempio-prova.example/a\\'b.png')", "fra apici con \\'"),
            ("background:url(https://img.esempio-prova.example/a\\'b.png)", "senza virgolette con \\'"),
            ("background:url('\\68ttps://img.esempio-prova.example/a)b.png')", "fra apici con la h scritta \\68 e una parentesi"),
            ("background:URL(https://img.esempio-prova.example/a.png)", "senza virgolette"),
        ];
        var leaks = remote.Where(r =>
        {
            var blocked = MailHtml.Prepare($"<div style=\"{WebUtility.HtmlEncode(r.Css)}\">x</div>", false, false, NoCid);
            var allowed = MailHtml.Prepare($"<div style=\"{WebUtility.HtmlEncode(r.Css)}\">x</div>", false, true, NoCid);
            // Dall'audit dell'11 ottobre (T-01) l'indirizzo si giudica senza i codici del CSS: anche quello con la h scritta
            // \68 si conta fra le immagini bloccate e, con le immagini consentite, resta com'è (prima: non contato, e tolto).
            return blocked.Fragment != "<div class=\"missiva-body\"><div style=\"background:none\">x</div></div>" || blocked.BlockedImages != 1
                   || allowed.Fragment != $"<div class=\"missiva-body\"><div style=\"{r.Css.Replace("\"", "&quot;")}\">x</div></div>";
        }).Select(r => r.What).ToList();
        var quoted = MailHtml.Prepare("<style>.a{background:url(\"https://img.esempio-prova.example/a(b).png\")} .b{background:url(\"data:image/svg+xml,<svg fill='rgb(0,0,0)'/>\")} " +
                                      ".c{background:url('cid:logo(1)@prova')}</style><p class=\"a\">x</p>", false, false, id => "https://missiva.example/cid/9/" + id);
        Check(leaks.Count == 0 && quoted.BlockedImages == 1
              && quoted.Fragment == "<style>.a{background:none} .b{background:url(\"data:image/svg+xml,<svg fill='rgb(0,0,0)'/>\")} .c{background:url('https://missiva.example/cid/9/logo(1)@prova')}\n</style>" +
                                    "<div class=\"missiva-body\"><p class=\"a\">x</p></div>",
              leaks.Count == 0 ? "url(...) esterni bloccati e contati anche fra virgolette con parentesi, con l'altro tipo di virgolette o con \\'; data: e cid: con parentesi tenuti; con le immagini consentite restano com'erano"
                               : "url(...) non bloccati: " + string.Join("; ", leaks));
        // Le forme che già si riconoscevano danno lo stesso risultato di prima, anche dove è storto. Con un "\" davanti alla
        // parentesi la corrispondenza può essere quella lunga (come la legge il CSS) invece di quella corta: bloccato comunque.
        var known = MailHtml.Prepare("<div style=\"a:url(https://x.example/a b.png);b:url(x(y)z);c:url( 'cid:1' );d:url(  &quot;//x.example/a.png&quot; );e:url();f:url(data:image/png;base64,AA==)\">x</div>", false, false, NoCid);
        var escapedParen = MailHtml.Prepare("<div style=\"a:url(https://x.example/a\\)b.png);b:url('https://x.example/a\\')c')\">x</div>", false, false, NoCid);
        Check(known.Fragment == "<div class=\"missiva-body\"><div style=\"a:none;b:nonez);c:none;d:none;e:none;f:url(data:image/png;base64,AA==)\">x</div></div>" && known.BlockedImages == 2
              && !escapedParen.Fragment.Contains("x.example") && escapedParen.BlockedImages == 2,
              "le forme di url(...) riconosciute anche prima danno lo stesso risultato di prima (si fermano alla prima parentesi chiusa); con \\) o \\' dentro, bloccato comunque");

        // V-02: più di 64 "<" sciolti si riscrivono come &lt; prima dell'analisi; l'albero, e quindi la pagina, è lo stesso.
        // Dalla revisione del 14 ottobre (U-01) la pagina del riquadro non ha più commenti, e un xmp ci sta come pre con il
        // suo testo scritto come testo: che l'analisi non abbia toccato né il commento né il contenuto grezzo si legge
        // dall'HTML in uscita (MailHtml.Outgoing: stessa analisi, e lì restano come sono), riscritto dall'albero perché c'è
        // uno script da togliere.
        const string strayTail = "</p><!-- c < d --><p title=\"e < f\">g</p><xmp>h < i</xmp>";
        Check(MailHtml.Prepare("<p>" + Repeat("a < b <3 << ", 40) + strayTail, false, false, NoCid).Fragment
              == "<div class=\"missiva-body\"><p>" + Repeat("a &lt; b &lt;3 &lt;&lt; ", 40) + "</p><p title=\"e < f\">g</p><pre>h &lt; i</pre></div>"
              && MailHtml.Outgoing("<script>x</script><p>" + Repeat("a < b <3 << ", 40) + strayTail) == "<html><head></head><body><p>" + Repeat("a &lt; b &lt;3 &lt;&lt; ", 40) + strayTail + "</body></html>"
              && MailHtml.ReadableText("<p>" + Repeat("a < b ", 70) + "</p>") == Repeat("a < b ", 70).Trim(),
              "160 \"<\" sciolti nel testo: la stessa pagina e lo stesso testo; commenti, attributi e contenuti grezzi non si toccano");

        // Messaggi normali: frammento, immagini bloccate, link e testo uguali carattere per carattere a quelli della versione
        // di prima (presi dal banco dell'audit, con MailHtml di git 3ae3675).
        static string? Cid(string id) => "https://missiva.example/cid/1/" + id;
        const string invoice =
            "<html><head><title>Titolo</title><style>body { color: #333333; background: white url(https://img.esempio-prova.example/fondo.png) } .box { border: 1px solid #cccccc } " +
            "@import url(https://stili.esempio-prova.example/a.css);</style></head><body bgcolor=\"#ffffff\" text=\"black\" class=\"corpo\"><table width=\"100%\" style=\"background-color:#fafafa\"><tr>" +
            "<td style=\"color:#333333;padding:4px\">Gentile cliente,<br>la fattura <b>n. 12</b> è pronta &amp; allegata.</td></tr></table><p>Vai al " +
            "<a href=\"https://www.esempio-prova.example/fatture?id=12&amp;x=1\" target=\"_blank\" onclick=\"x()\">sito <i>di prova</i></a> o scrivi a <a href=\"mailto:info@esempio-prova.example\">info</a>.</p>" +
            "<img src=\"https://t.esempio-prova.example/o.gif\" width=\"1\" height=\"1\"><script>alert(1)</script><form action=\"/do/x\"><input name=\"a\"></form><!-- commento --><pre>\n\nriga 1\n  riga 2</pre></body></html>";
        const string invoiceBody =
            "<tbody><tr><td style=\"{1}\">Gentile cliente,<br>la fattura <b>n. 12</b> è pronta &amp; allegata.</td></tr></tbody></table><p>Vai al <a href=\"https://www.esempio-prova.example/fatture?id=12&amp;x=1\">" +
            "sito <i>di prova</i></a> o scrivi a <a href=\"mailto:info@esempio-prova.example\">info</a>.</p><img src=\"data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7\" width=\"1\" height=\"1\">" +
            "<pre>\n\nriga 1\n  riga 2</pre></div>"; // dalla revisione del 14 ottobre (U-01) senza "<!-- commento -->": nella pagina del riquadro i commenti non si scrivono
        var invoiceLight = MailHtml.Prepare(invoice, false, false, Cid);
        var invoiceDark = MailHtml.Prepare(invoice, true, false, Cid);
        Check(invoiceLight.Fragment == "<style>.missiva-body { color: #333333; background: white none } .box { border: 1px solid #cccccc } \n</style><div class=\"missiva-body corpo\" style=\"background-color:#ffffff;color:black\">" +
                                       "<table width=\"100%\" style=\"background-color:#fafafa\">" + invoiceBody.Replace("{1}", "color:#333333;padding:4px")
              && invoiceDark.Fragment == "<style>.missiva-body { color: #CCCCCC; background: #292929 none } .box { border: 1px solid #404040 } \n</style><div class=\"missiva-body corpo\" style=\"background-color:#292929;color:#FFFFFF\">" +
                                         "<table width=\"100%\" style=\"background-color: #2C2C2C\">" + invoiceBody.Replace("{1}", "color: #CCCCCC;padding:4px")
              && invoiceLight.BlockedImages == 1 && invoiceDark.BlockedImages == 1 && !invoiceLight.PlainOnly
              && invoiceLight.Links.SequenceEqual([new("https://www.esempio-prova.example/fatture?id=12&x=1", "sito di prova"), new("mailto:info@esempio-prova.example", "info")])
              && MailHtml.ToText(invoice) == "Gentile cliente,\nla fattura n. 12 è pronta & allegata.\n\nVai al sito di prova o scrivi a info.\n\nriga 1\nriga 2"
              && MailHtml.ReadableText(invoice) == MailHtml.ToText(invoice),
              "messaggio con foglio di stile, tabella, link, pixel, script, modulo, commento e pre: pagina chiara e scura, link e testo uguali a prima (dal 14 ottobre, U-01, senza il commento)");
        const string drawing =
            "<p>Logo: <img src=\"cid:logo@prova\" alt=\"Logo\"> <svg width=\"10\" height=\"10\"><image href=\"https://img.esempio-prova.example/a.png\"/><rect fill=\"url(#g)\" width=\"10\" height=\"10\"/>" +
            "<a xlink:href=\"https://www.esempio-prova.example/\"><text>apri</text></a></svg></p>" +
            "<div style=\"background: #0a66c2 url('cid:fondo@prova') no-repeat; color: white; border: 1px solid rgb(0, 0, 0)\">Pulsante <span style=\"color:#ffffff\">bianco</span></div>";
        const string drawingStart =
            "<div class=\"missiva-body\"><p>Logo: <img src=\"https://missiva.example/cid/1/logo@prova\" alt=\"Logo\"> <svg width=\"10\" height=\"10\"><image></image><rect fill=\"url(#g)\" width=\"10\" height=\"10\"></rect>" +
            "<a xlink:href=\"https://www.esempio-prova.example/\"><text>apri</text></a></svg></p>";
        var drawingLight = MailHtml.Prepare(drawing, false, false, Cid);
        var drawingDark = MailHtml.Prepare(drawing, true, false, Cid);
        Check(drawingLight.Fragment == drawingStart + "<div style=\"background: #0a66c2 url('https://missiva.example/cid/1/fondo@prova') no-repeat; color: white; border: 1px solid rgb(0, 0, 0)\">Pulsante <span style=\"color:#ffffff\">bianco</span></div></div>"
              && drawingDark.Fragment == drawingStart + "<div style=\"background: #0A66C2 url('https://missiva.example/cid/1/fondo@prova') no-repeat; color: white; border: 1px solid #404040\">Pulsante <span style=\"color:#ffffff\">bianco</span></div></div>"
              && drawingLight.BlockedImages == 1 && drawingLight.Links.SequenceEqual([new("https://www.esempio-prova.example/", "apri")])
              && MailHtml.ToText(drawing) == "Logo:  apri\nPulsante bianco",
              "immagini cid:, disegno SVG con un'immagine esterna, pulsante dal fondo vivace: uguali a prima");
        const string soup = "<p>uno<p>due <b>grassetto <i>corsivo</b> ancora</i> fine<ul><li>a<li>b</ul>3 < 5 e 7 > 2 <font color=red>rosso<table><tr><td>cella</table>dopo";
        const string soupStart = "<div class=\"missiva-body\"><p>uno</p><p>due <b>grassetto <i>corsivo</i></b><i> ancora</i> fine</p><ul><li>a</li><li>b</li></ul>3 &lt; 5 e 7 &gt; 2 ";
        Check(MailHtml.Prepare(soup, false, false, Cid).Fragment == soupStart + "<font color=\"red\">rosso<table><tbody><tr><td>cella</td></tr></tbody></table>dopo</font></div>"
              && MailHtml.Prepare(soup, true, false, Cid).Fragment == soupStart + "<font color=\"#FF3D3D\">rosso<table><tbody><tr><td>cella</td></tr></tbody></table>dopo</font></div>"
              && MailHtml.ToText(soup) == "uno\ndue grassetto corsivo ancora fine\na\nb\n3 < 5 e 7 > 2 rossocella\n\ndopo",
              "HTML scritto male (tag mai chiusi o accavallati, un \"<\" nel testo): uguale a prima");
        const string lines =
            "<div>Prima riga</div><div>Seconda&nbsp;riga   con   spazi</div><table><tr><th>A</th><td>1</td></tr><tr><th>B</th><td>2</td></tr></table><blockquote>citazione<br>su due righe</blockquote><hr>fine" +
            "<pre>  tenuto\n    così</pre><style>p{}</style><script>x</script>\u200Binvisibile\u200B";
        Check(MailHtml.Prepare(lines, false, false, Cid).Fragment ==
              "<style>p{}\n</style><div class=\"missiva-body\"><div>Prima riga</div><div>Seconda&nbsp;riga   con   spazi</div><table><tbody><tr><th>A</th><td>1</td></tr><tr><th>B</th><td>2</td></tr></tbody></table>" +
              "<blockquote>citazione<br>su due righe</blockquote><hr>fine<pre>  tenuto\n    così</pre>\u200Binvisibile\u200B</div>"
              && MailHtml.ToText(lines) == "Prima riga\nSeconda riga con spazi\nA 1\nB 2\n\ncitazione\nsu due righe\n\nfine  tenuto\ncosì\u200Binvisibile\u200B"
              && MailHtml.ReadableText(lines) == "Prima riga\nSeconda riga con spazi\nA 1\nB 2\n\ncitazione\nsu due righe\n\nfine  tenuto\ncosìinvisibile",
              "testo di un messaggio con blocchi, tabella, br, pre, spazi unificatori e caratteri invisibili: uguale a prima");
    }

    /// <summary>
    /// Audit, blocco 7: la copia di sicurezza (F-29, Backup e BackupFile). Tutto in cartelle temporanee sue, mai la cartella
    /// dei dati della suite né quella vera; solo dati finti. "Un altro PC" è un'entropia in più per DPAPI sul thread della prova
    /// (Store.TestEntropy): quello che è cifrato per un PC non si decifra sull'altro, come fra due utenti di Windows. Con
    /// "Missiva.exe --prova-audit risultato.txt copia" solo queste.
    /// </summary>
    static void BackupAuditTests()
    {
        Report.Add("--- Audit, blocco 7: copia di sicurezza (esporta e importa, anche su un altro PC)");
        var root = Path.Combine(Path.GetTempPath(), "missiva-prova-copia-" + Guid.NewGuid().ToString("N")[..8]);
        byte[] pcA = [1, 7, 7, 1], pcB = [2, 9, 9, 2], pcX = [3, 5, 5, 3];
        static void Pc(byte[]? entropy)
        {
            Store.TestEntropy = entropy;
            ProtectedTextConverter.TestForget(); // come un processo appena avviato: nessun cifrato dell'altro PC ricordato
        }
        string Folder(string name)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            return dir;
        }
        static bool Has(byte[] bytes, string text) => bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0 || bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0;
        static BackupProblem? Refused(byte[] file, string password)
        {
            try { Backup.Read(file, password); return null; }
            catch (BackupException ex) { return ex.Problem; }
        }
        // Quello che è dell'utente, senza accessi e senza invii pianificati: uguale prima e dopo la copia.
        static string Shape(Settings settings)
        {
            var copy = Backup.ParseSettings(Backup.PortableJson(settings));
            copy.ProtectedGoogleClientSecret = null;
            foreach (var a in copy.Accounts)
            {
                a.HomeAccountId = a.ProtectedRefreshToken = null;
                if (a.Imap != null) a.Imap.ProtectedPassword = null;
            }
            copy.ScheduledSends.Clear();
            copy.UnreadableTold = false;
            return Backup.PortableJson(copy);
        }
        string password = "Parola-prova-" + Guid.NewGuid().ToString("N")[..10], imapPassword = "password-imap-prova-" + Guid.NewGuid().ToString("N")[..6];
        const string googleToken = "token-aggiornamento-prova", googleSecret = "segreto-google-prova", home = "casa-prova.directory-prova";
        using var fake = new FakeImap(new Flavor(Gmail: false));
        var testToken = GoogleAuth.TestToken;
        try
        {
            // ---------------------------------------------------------------- il formato del file
            var content = Encoding.UTF8.GetBytes("contenuto di prova di Mario Rossi, " + new string('x', 5000));
            var clock = Stopwatch.StartNew();
            var sealed1 = BackupFile.Seal(content, password);
            var derive = clock.ElapsedMilliseconds;
            var sealed2 = BackupFile.Seal(content, password);
            var opened = BackupFile.Unseal(sealed1, password);
            Check(Encoding.ASCII.GetString(sealed1, 0, 8) == "MSVCOPIA" && BinaryPrimitives.ReadUInt16LittleEndian(sealed1.AsSpan(8)) == 1 && sealed1[10] == 1 && sealed1[11] == 1
                  && BinaryPrimitives.ReadUInt32LittleEndian(sealed1.AsSpan(12)) == 600_000 && sealed1[16] == 16 && sealed1[17] == 12 && sealed1[18] == 16
                  && BinaryPrimitives.ReadUInt64LittleEndian(sealed1.AsSpan(20)) == (ulong)content.Length && sealed1.Length == 84 + content.Length + 16
                  && opened.SequenceEqual(content) && !sealed1.AsSpan(28, 40).SequenceEqual(sealed2.AsSpan(28, 40)) && !sealed1.AsSpan(84, 64).SequenceEqual(sealed2.AsSpan(84, 64))
                  && !Has(sealed1, "Mario") && !Has(sealed1, "xxxxxxxx"),
                  $"formato del file: intestazione in chiaro con solo versione (1) e parametri (PBKDF2-HMAC-SHA256 a 600.000 iterazioni, AES-256-GCM, lunghezze), sale e nonce casuali " +
                  $"(due file dallo stesso contenuto e dalla stessa password sono diversi), contenuto uguale dopo andata e ritorno (chiave derivata e file cifrato in {derive} ms)");

            // ---------------------------------------------------------------- il PC di partenza (A)
            Pc(pcA);
            var dirA = Folder("pc-a");
            var original = new Settings
            {
                MicrosoftClientId = "id-app-prova", GoogleClientId = "google-prova.apps.example", ProtectedGoogleClientSecret = Store.Protect(googleSecret),
                Accounts =
                [
                    new MailAccount { Id = "ms-prova", Kind = AccountKind.Microsoft, Email = "mario.rossi@outlook-prova.example", DisplayName = "Mario Rossi", HomeAccountId = home,
                                      Identities = [new SendIdentity { Id = "alias-prova", Name = "Studio Rossi", Email = "info@studio-prova.example" }] },
                    new MailAccount { Id = "gm-prova", Kind = AccountKind.Gmail, Email = "maria.rossi@gmail-prova.example", DisplayName = "Maria Rossi",
                                      ProtectedRefreshToken = Store.Protect(googleToken), GoogleScopes = "https://mail.google.com/ calendario-prova" },
                    new MailAccount { Id = "imap-prova", Kind = AccountKind.Imap, Email = "mario@studio-prova.example", DisplayName = "Mario Rossi",
                                      Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = fake.Port, SmtpHost = "127.0.0.1", SmtpPort = 1, UserName = "mario@studio-prova.example",
                                                              ProtectedPassword = Store.Protect(imapPassword) } },
                ],
                Theme = AppTheme.Dark, TextSize = TextSize.Large, ReadingPane = ReadingPanePosition.Bottom, MarkRead = MarkReadMode.AfterDelay, UndoSendSeconds = 10, KeepInTray = false,
                PreviewLines = 2, ComposeFont = "Calibri", ComposeFontSize = 12, AttachmentReminder = false, NotificationsOff = ["gm-prova"], Focused = { ["ms-prova"] = false },
                Signatures =
                {
                    ["imap-prova"] = new Signature { Rtf = @"{\rtf1 Mario Rossi, firma-prova}", Replies = false, More = [new NamedSignature { Id = "breve", Name = "Breve", Rtf = @"{\rtf1 MR firma-breve-prova}" }], ReplyId = "breve" },
                    ["ms-prova|alias-prova"] = new Signature { Rtf = @"{\rtf1 Studio Rossi, firma-alias-prova}" },
                },
                Templates = [new MailTemplate { Id = "modello", Name = "Preventivo modello-prova", Subject = "Preventivo", Rtf = @"{\rtf1 IBAN IT00 iban-prova}" }],
                Rules =
                [
                    new MailRule { Id = "regola", AccountId = "imap-prova", Name = "Fatture regola-prova", FromAddresses = ["fatture@fornitore-prova.example"], SubjectContains = ["fattura-prova", "nota di credito"],
                                   MoveTo = "INBOX/Fatture-prova", MoveToName = "Fatture-prova", MarkRead = true, Categories = ["Clienti-prova"] },
                ],
                RulesSeen = ["impronta-prova"], RecentSearches = ["da:\"Maria Rossi\" ricerca-prova"], ImageSenders = ["fornitore-prova.example"],
                CategoryColors = { ["gm-prova|Clienti-prova"] = 7 }, LocalCategories = { ["ms-prova"] = ["Urgente-prova"] },
                Favorites = ["imap-prova|INBOX/Fatture-prova", "gm-prova|INBOX"], FolderOrder = { ["imap-prova"] = ["INBOX", "INBOX/Fatture-prova", "Archivio-prova"] },
                FolderColors = { ["imap-prova|INBOX/Fatture-prova"] = 3 }, HiddenFolders = ["gm-prova|Promozioni-prova"], Pinned = { ["imap-prova"] = ["7"] },
                Flags = [new FlagDate { AccountId = "imap-prova", Key = "chiave-prova", MessageId = "7", FolderId = "INBOX", Due = new DateTime(2026, 10, 20), Reminder = new DateTime(2026, 10, 19, 9, 0, 0),
                                        Subject = "Contrassegno oggetto-prova", From = "Maria Rossi" }],
                Snoozed = [new SnoozedMessage { AccountId = "gm-prova", Key = "posposto-prova", FolderId = "INBOX", Until = new DateTime(2026, 10, 25, 8, 0, 0), Subject = "Posposto oggetto-prova", From = "Mario Rossi" }],
                Woken = { ["gm-prova|sveglio-prova"] = new DateTime(2026, 10, 3, 8, 0, 0) },
                ScheduledSends = [new ScheduledSend { AccountId = "imap-prova", DraftKey = "bozza-prova", At = new DateTime(2026, 10, 25, 2, 30, 0), Subject = "Pianificato oggetto-prova" }],
                MessageMenuOrder = ["reply", "-", "delete"], CalendarMode = "Month", HiddenCalendars = ["ms-prova|calendario-prova"],
            };
            string[] marks = ["firma-prova", "firma-breve-prova", "firma-alias-prova", "modello-prova", "iban-prova", "regola-prova", "fattura-prova", "Fatture-prova", "Clienti-prova", "Urgente-prova",
                              "Contrassegno oggetto-prova", "Posposto oggetto-prova", "ricerca-prova", "fornitore-prova.example", "Archivio-prova", "alias-prova", "sveglio-prova", "impronta-prova"];
            List<Contact> contacts =
            [
                new Contact { Id = "contatto", DisplayName = "Maria Rossi", Emails = ["maria.rossi@clienti-prova.example"], Mobile = "333 0000000", Company = "Clienti di prova", Notes = "nota-contatto-prova" },
                new Contact { Id = "gruppo", IsGroup = true, DisplayName = "Gruppo-prova", Members = ["Mario Rossi <mario.rossi@clienti-prova.example>", "maria.rossi@clienti-prova.example"] },
            ];
            const string book = "{\"Entries\":[{\"Name\":\"Mario Rossi\",\"Email\":\"destinatario-prova@clienti-prova.example\",\"Received\":2,\"Sent\":5,\"LastReceived\":\"2026-10-01T10:00:00\",\"LastSent\":\"2026-10-02T10:00:00\"}],\"SentScans\":{\"imap-prova\":\"2026-10-02T10:00:00Z\"}}";
            const string domains = "{\"dominio-prova.example\":{\"First\":\"2026-09-01T00:00:00Z\",\"Last\":\"2026-10-01T00:00:00Z\",\"Verified\":\"2026-10-01T00:00:00Z\"}}";
            void WriteData(string dir, Settings settings, List<Contact>? people, string? addressBook, string? knownDomains)
            {
                Store.TryWrite(dir, settings, null, out _);
                if (people != null) File.WriteAllBytes(Path.Combine(dir, "contatti.dat"), Store.ProtectBytes(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(people, ContactsJson.Default.ListContact)));
                if (addressBook != null) File.WriteAllBytes(Path.Combine(dir, "rubrica.json"), Store.ProtectBytes(Encoding.UTF8.GetBytes(addressBook)));
                if (knownDomains == null) return;
                Directory.CreateDirectory(Path.Combine(dir, "loghi"));
                File.WriteAllText(Path.Combine(dir, "loghi", "domini.dat"), Store.Protect(knownDomains));
                File.WriteAllBytes(Path.Combine(dir, "loghi", "dominio-prova.example.png"), [1, 2, 3]); // un logo: nella copia non entra
            }
            string ReadDat(string path) => Encoding.UTF8.GetString(Store.UnprotectBytes(File.ReadAllBytes(path)));
            WriteData(dirA, original, contacts, book, domains);
            var shape = Shape(original);

            // ---------------------------------------------------------------- esportazione (senza password IMAP: la scelta di base)
            var data = Backup.Collect(Store.Read(dirA, out _), dirA, withPasswords: false, out var notes);
            var file = Backup.Write(data, password);
            var summary = Backup.Describe(Backup.Read(file, password));
            string inside = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            Check(notes.Count == 0 && marks.All(shape.Contains) && marks.All(data.Settings.Contains) && Shape(Backup.ParseSettings(data.Settings)) == shape
                  && data.Contacts!.Contains("nota-contatto-prova") && data.Contacts.Contains("Gruppo-prova") && data.AddressBook == book && data.KnownDomains == null
                  && summary.Contents == "3 account (mario.rossi@outlook-prova.example, maria.rossi@gmail-prova.example, mario@studio-prova.example), 3 firme, 1 modello, 1 regola di Missiva, " +
                                         "1 contatto di Missiva, 1 gruppo, 1 indirizzo nella rubrica dei destinatari, 1 messaggio posposto, 1 contrassegno con data, e le impostazioni",
                  $"esportazione: nella copia impostazioni, firme (anche quella di un alias), modelli, regole di Missiva, categorie e colori, Preferiti e ordine delle cartelle, contrassegni, posposti, " +
                  $"contatti e gruppi di Missiva e rubrica dei destinatari, uguali a quelli di partenza; i domini noti dei loghi non ci sono più (audit dell'11 ottobre, T-18) ({summary.Contents})");
            Check(data.ImapPasswords.Count == 0 && data.GoogleClientSecret == googleSecret && !inside.Contains(imapPassword) && !inside.Contains(googleToken) && !inside.Contains(home)
                  && !inside.Contains("dpapi:") && !inside.Contains("Pianificato oggetto-prova") && !inside.Contains("bozza-prova")
                  && Backup.ParseSettings(data.Settings).Accounts.All(a => a.HomeAccountId == null && a.ProtectedRefreshToken == null && a.Imap?.ProtectedPassword == null)
                  && Backup.ParseSettings(data.Settings).ProtectedGoogleClientSecret == null,
                  "accessi: nella copia non ci sono il token di Google, l'identificativo dell'accesso a Microsoft, le password IMAP (casella spenta), nessun valore cifrato con DPAPI, e nemmeno gli invii pianificati; " +
                  "c'è il segreto dell'app Google, senza il quale gli account Gmail non rientrano");
            // Le frasi delle finestre (UI.BackupUi): la password scelta, la domanda prima di sostituire, il resoconto dell'esportazione.
            var replace = UI.BackupUi.ConfirmText(summary, data.App, hasData: true);
            var firstTime = UI.BackupUi.ConfirmText(summary, data.App, hasData: false);
            var exported = UI.BackupUi.ExportedText(@"C:\prova\Missiva di prova.missiva-copia", summary, ["Una nota di prova."]);
            Check(UI.BackupUi.PasswordProblem("corta", "corta") is { } tooShortText && tooShortText.Contains("almeno 8 caratteri")
                  && UI.BackupUi.PasswordProblem("frase lunga di prova", "frase lunga di Prova") is { } differ && differ.Contains("non sono uguali")
                  && UI.BackupUi.PasswordProblem("frase lunga di prova", "frase lunga di prova") == null
                  && replace is { Heading: "Sostituire tutto con questa copia?", Primary: "Sostituisci tutto" } && replace.Body.Contains("le due cose non si uniscono")
                  && replace.Body.Contains("messi da parte con la data") && replace.Body.Contains("3 account") && replace.Body.Contains("chiederanno di nuovo l'accesso")
                  && firstTime is { Heading: "Importare questa copia?", Primary: "Importa" } && !firstTime.Body.Contains("sostituito")
                  && exported.Contains("Non contiene la posta") && exported.Contains("ogni account chiede di nuovo l'accesso.") && exported.Contains("Attenzione: Una nota di prova.")
                  && exported.Contains("Missiva non può recuperarla"),
                  "finestre: una password corta o scritta in due modi diversi non passa; con impostazioni già presenti la domanda è \"Sostituire tutto con questa copia?\" (dice che non si unisce e " +
                  "che i dati di adesso restano da parte), senza è \"Importare questa copia?\"; dopo l'esportazione si dice che cosa c'è, che cosa no e quello che non è potuto entrare");
            string[] plainWords = ["Mario", "Rossi", "Maria", "prova", "example", "Accounts", "Signatures", "studio", "password", "MailAccount", password, imapPassword];
            Check(!plainWords.Any(w => Has(file, w)) && file.Length < inside.Length,
                  $"niente in chiaro nel file della copia ({file.Length:N0} byte): cercati nomi, indirizzi, parole delle impostazioni e le due password, in UTF-8 e in UTF-16");

            // ---------------------------------------------------------------- un altro PC (B): la cartella copiata e la copia di sicurezza
            Pc(pcB);
            var carried = Store.Read(dirA, out _); // come copiare la cartella dei dati sul PC nuovo: quello che F-29 racconta
            int unreadableCarried = ProtectedTextConverter.UnreadableCount;
            bool carriedLost = carried.Templates[0].Name.StartsWith("(testo cifrato non leggibile") && carried.Rules[0].Unreadable && !Store.TryUnprotect(carried.Accounts[2].Imap!.ProtectedPassword, out _);
            bool datLost = false;
            try { ReadDat(Path.Combine(dirA, "contatti.dat")); }
            catch (CryptographicException) { datLost = true; }
            Pc(pcB);
            var dirB = Folder("pc-b");
            Backup.SavePending(dirB, Backup.Read(file, password));
            var pendingBytes = File.ReadAllBytes(Path.Combine(dirB, Backup.PendingName));
            bool pendingKnown = Backup.HasPending(dirB) && Backup.Pending(dirB)?.Created == data.Created;
            var outcome = Backup.ApplyPending(dirB);
            var onB = Store.Read(dirB, out var recoveryB);
            Check(carriedLost && datLost && unreadableCarried > 5
                  && outcome is { Applied: true, Error: null, SetAside.Count: 0 } && recoveryB == null && Shape(onB) == shape && ProtectedTextConverter.UnreadableCount == 0
                  && onB.Templates[0].Rtf.Contains("iban-prova") && onB.Rules[0] is { Unreadable: false, FromAddresses: ["fatture@fornitore-prova.example"] }
                  && ReadDat(Path.Combine(dirB, "contatti.dat")) == data.Contacts
                  // Dalle scelte del 12 ottobre (T-17) la rubrica entra con nomi e indirizzi, senza conteggi, date e letture della Posta inviata.
                  && ReadDat(Path.Combine(dirB, "rubrica.json")) == "{\"Entries\":[{\"Name\":\"Mario Rossi\",\"Email\":\"destinatario-prova@clienti-prova.example\",\"Received\":0,\"Sent\":0," +
                                                                    "\"LastReceived\":\"0001-01-01T00:00:00\",\"LastSent\":\"0001-01-01T00:00:00\"}],\"SentScans\":{}}"
                  && !Directory.Exists(Path.Combine(dirB, "loghi"))
                  && Store.TryUnprotect(onB.ProtectedGoogleClientSecret, out var secretB) && secretB == googleSecret
                  && !Backup.HasPending(dirB) && Directory.GetFiles(dirB, "*.importato", SearchOption.AllDirectories).Length == 0 && pendingKnown && !plainWords.Any(w => Has(pendingBytes, w)),
                  $"su un altro PC: copiando la cartella dei dati {unreadableCarried} testi cifrati non si leggono (modelli, regole, password, contatti); con la copia di sicurezza tutto si legge " +
                  "e viene cifrato di nuovo per l'utente di quel PC (impostazioni, contatti, rubrica); i loghi e i loro domini noti non passano; il file in attesa del riavvio è cifrato e poi tolto");
            bool foreign = false; // quello che l'importazione ha scritto è del PC B: sul PC A non si legge
            Pc(pcA);
            try { ReadDat(Path.Combine(dirB, "contatti.dat")); }
            catch (CryptographicException) { foreign = true; }
            foreign &= Store.Read(dirB, out _).Templates[0].Name.StartsWith("(testo cifrato non leggibile");
            Pc(pcB);
            Check(foreign, "i file scritti dall'importazione sono cifrati per il PC di arrivo: sul PC di partenza non si leggerebbero (la prova distingue davvero i due PC)");

            // Gli account arrivati senza accesso: ci sono tutti e tre, ognuno con la sua barra, e nessuna richiesta al server.
            ImapProvider.TestPlain = true;
            GoogleAuth.TestToken = null;
            fake.Commands.Clear();
            Exception? imapError = null, googleError = null, microsoftError = null;
            var imapOnB = onB.Accounts.First(a => a.Kind == AccountKind.Imap);
            using (var provider = new ImapProvider(imapOnB))
            {
                try { provider.GetFoldersAsync().GetAwaiter().GetResult(); }
                catch (Exception ex) { imapError = ex; }
                try { GoogleAuth.TokenAsync(onB.Accounts.First(a => a.Kind == AccountKind.Gmail)).GetAwaiter().GetResult(); }
                catch (Exception ex) { googleError = ex; }
                try { MicrosoftAuth.TokenAsync(onB.Accounts.First(a => a.Kind == AccountKind.Microsoft)).GetAwaiter().GetResult(); }
                catch (Exception ex) { microsoftError = ex; }
                UI.AccountItem Bar(Exception? error) => new(provider) { Status = error?.Message ?? "", Fix = MailException.FixOf(error) };
                UI.AccountItem imapBar = Bar(imapError), googleBar = Bar(googleError), microsoftBar = Bar(microsoftError);
                Check(onB.Accounts.Select(a => a.Id).SequenceEqual(["ms-prova", "gm-prova", "imap-prova"]) && outcome!.NeedAccess.Select(a => a.Id).SequenceEqual(["ms-prova", "gm-prova", "imap-prova"])
                      && outcome.Body.Contains("mario.rossi@outlook-prova.example, maria.rossi@gmail-prova.example, mario@studio-prova.example") && outcome.Body.Contains("Accedi di nuovo o Aggiorna password")
                      && imapBar is { Fix: AccountFix.Password, FixText: "Aggiorna password", FixMessage: "Su questo PC manca la password dell'account.", FixVisibility: System.Windows.Visibility.Visible }
                      && googleBar is { Fix: AccountFix.SignIn, FixText: "Accedi di nuovo", FixMessage: "Su questo PC manca l'accesso all'account.", FixVisibility: System.Windows.Visibility.Visible }
                      && microsoftBar is { Fix: AccountFix.SignIn, FixText: "Accedi di nuovo", FixMessage: "Su questo PC manca l'accesso all'account.", FixVisibility: System.Windows.Visibility.Visible }
                      && fake.Commands.Count == 0 && fake.Password == null,
                      "account importati senza accesso: ci sono tutti e tre e l'avviso dopo il riavvio li nomina; ognuno ha la sua barra (Aggiorna password per IMAP, Accedi di nuovo per Google e " +
                      $"Microsoft) senza errori di Windows e senza tentativi verso il server con una password vuota ({fake.Commands.Count} comandi al server IMAP finto)");
            }

            // ---------------------------------------------------------------- sopra impostazioni che ci sono già: tutto sostituito, quelle di prima da parte
            var dirC = Folder("pc-b-con-dati");
            var old = new Settings
            {
                Accounts = [new MailAccount { Id = "vecchio-prova", Kind = AccountKind.Imap, Email = "vecchio@studio-prova.example", Imap = new ImapServer { UserName = "vecchio@studio-prova.example", ProtectedPassword = Store.Protect("vecchia") } }],
                Templates = [new MailTemplate { Name = "Modello vecchio-prova", Rtf = @"{\rtf1 vecchio}" }], Theme = AppTheme.Light,
            };
            WriteData(dirC, old, [new Contact { DisplayName = "Contatto vecchio-prova" }], null, null);
            Store.TryWrite(dirC, old, null, out _); // due salvataggi: c'è anche impostazioni.bak
            var oldSettingsBytes = File.ReadAllBytes(Path.Combine(dirC, Store.FileName));
            Backup.SavePending(dirC, Backup.Read(file, password));
            var replaced = Backup.ApplyPending(dirC);
            var onC = Store.Read(dirC, out var recoveryC);
            string[] asideC = Directory.GetFiles(dirC, "*-prima-dell-importazione-*").Select(Path.GetFileName).Order().ToArray()!;
            var asideSettings = Directory.GetFiles(dirC, "impostazioni-prima-dell-importazione-*.json").FirstOrDefault();
            var asideContacts = Directory.GetFiles(dirC, "contatti-prima-dell-importazione-*.dat").FirstOrDefault();
            Check(replaced is { Applied: true, Error: null, SetAside.Count: 3 } && recoveryC == null && Shape(onC) == shape && !Backup.PortableJson(onC).Contains("vecchio-prova")
                  && !ReadDat(Path.Combine(dirC, "contatti.dat")).Contains("vecchio-prova")
                  && asideC.Length == 3 && asideSettings != null && File.ReadAllBytes(asideSettings).SequenceEqual(oldSettingsBytes) && Directory.GetFiles(dirC, "impostazioni-prima-dell-importazione-*.bak").Length == 1
                  && asideContacts != null && ReadDat(asideContacts).Contains("Contatto vecchio-prova")
                  && replaced.Body.Contains("non sono stati cancellati") && File.Exists(Path.Combine(dirC, Store.BackupName)),
                  $"importazione sopra impostazioni che ci sono già: tutto sostituito senza unire (l'account, il modello e il contatto di prima non ci sono più), e i file di prima da parte con la data nel nome, " +
                  $"uguali byte per byte ({string.Join(", ", asideC)})");

            // ---------------------------------------------------------------- sullo stesso PC: gli accessi che ci sono restano, e gli invii pianificati pure
            Pc(pcA);
            var dirD = Folder("pc-a-di-nuovo");
            WriteData(dirD, original, contacts, book, domains);
            Backup.SavePending(dirD, Backup.Read(file, password));
            var restored = Backup.ApplyPending(dirD);
            var onD = Store.Read(dirD, out _);
            Check(restored is { Applied: true, NeedAccess.Count: 0 } && Shape(onD) == shape && onD.Accounts[0].HomeAccountId == home
                  && Store.TryUnprotect(onD.Accounts[1].ProtectedRefreshToken, out var tokenD) && tokenD == googleToken
                  && Store.TryUnprotect(onD.Accounts[2].Imap!.ProtectedPassword, out var passwordD) && passwordD == imapPassword
                  && onD.ScheduledSends is [{ DraftKey: "bozza-prova", Subject: "Pianificato oggetto-prova" }] && !restored.Body.Contains("di nuovo l'accesso"),
                  "copia rimessa sullo stesso PC: gli account che c'erano già (stesso identificativo e indirizzo) tengono l'accesso di questo PC e non chiedono nulla; gli invii pianificati di questo PC restano");

            // ---------------------------------------------------------------- con le password IMAP (casella accesa), verso un altro PC
            var withPasswords = Backup.Collect(Store.Read(dirA, out _), dirA, withPasswords: true, out _);
            var fileWithPasswords = Backup.Write(withPasswords, password);
            string insideWithPasswords = System.Text.Json.JsonSerializer.Serialize(withPasswords);
            Pc(null); // il PC di arrivo è questo com'è davvero (DPAPI senza entropia): il provider decifra la password anche dai suoi thread
            var dirF = Folder("pc-c-con-password");
            var readWithPasswords = Backup.Read(fileWithPasswords, password);
            Backup.SavePending(dirF, readWithPasswords);
            var outcomeF = Backup.ApplyPending(dirF);
            var onF = Store.Read(dirF, out _);
            var imapOnF = onF.Accounts.First(a => a.Kind == AccountKind.Imap);
            fake.ExpectedPassword = imapPassword;
            fake.Commands.Clear();
            bool connected;
            using (var provider = new ImapProvider(imapOnF))
                try { connected = provider.GetFoldersAsync().GetAwaiter().GetResult().Any(f => f.Kind == FolderKind.Inbox); }
                catch (Exception ex) { connected = false; Report.Add("      " + ex.Message); }
            Check(withPasswords.ImapPasswords is { Count: 1 } && withPasswords.ImapPasswords["imap-prova"] == imapPassword && Backup.Describe(readWithPasswords).Passwords == 1
                  && !insideWithPasswords.Contains(googleToken) && !insideWithPasswords.Contains(home) && !insideWithPasswords.Contains("dpapi:")
                  && !plainWords.Any(w => Has(fileWithPasswords, w))
                  && outcomeF!.NeedAccess.Select(a => a.Id).SequenceEqual(["ms-prova", "gm-prova"]) && connected && fake.Password == imapPassword && Shape(onF) == shape,
                  "con la casella \"Includi le password degli account IMAP\": la password è nella copia (cifrata con tutto il resto, mai in chiaro nel file), sul PC di arrivo l'account IMAP si collega " +
                  "senza chiedere nulla; Google e Microsoft chiedono comunque l'accesso, e il loro token non c'è nemmeno così");
            fake.ExpectedPassword = null;

            // ---------------------------------------------------------------- file che non si aprono
            var wrong = Refused(file, password + "x");
            string wrongText = "";
            try { Backup.Read(file, password.ToUpperInvariant()); }
            catch (BackupException ex) { wrongText = ex.Message; }
            Check(wrong == BackupProblem.WrongPassword && wrongText.StartsWith("La password non è quella di questa copia"),
                  $"password sbagliata: detta come tale (\"{wrongText}\")");
            BackupException? cut = null, cutHeader = null, notBackup = null, tooShort = null, longer = null;
            try { BackupFile.Check(file.AsSpan(0, file.Length - 40)); } catch (BackupException ex) { cut = ex; }
            try { BackupFile.Check(file.AsSpan(0, 20)); } catch (BackupException ex) { cutHeader = ex; }
            try { BackupFile.Check(Encoding.UTF8.GetBytes("{ \"Accounts\": [] } non è una copia di Missiva, è un altro file di prova")); } catch (BackupException ex) { notBackup = ex; }
            try { BackupFile.Check(file.AsSpan(0, 5)); } catch (BackupException ex) { tooShort = ex; }
            try { BackupFile.Check([.. file, 0]); } catch (BackupException ex) { longer = ex; }
            Check(cut is { Problem: BackupProblem.Incomplete } && cut.Message.Contains("mancano 40 byte") && Refused(file[..(file.Length - 40)], password) == BackupProblem.Incomplete
                  && cutHeader is { Problem: BackupProblem.Incomplete } && notBackup is { Problem: BackupProblem.NotABackup } && tooShort is { Problem: BackupProblem.NotABackup }
                  && longer is { Problem: BackupProblem.Damaged },
                  $"file troncato: detto prima ancora di chiedere la password, con quanti byte mancano (\"{cut?.Message}\"); un file che non è una copia di Missiva detto come tale; un byte in più in fondo rifiutato");
            var flips = new List<string>();
            bool flipsRefused = true, bodySaid = true;
            foreach (int at in new[] { 0, 9, 13, 21, 30, 70, 84 + (file.Length - 100) / 2, file.Length - 1 })
            {
                var changed = (byte[])file.Clone();
                changed[at] ^= 0x01;
                var problem = Refused(changed, password);
                flips.Add($"{at}: {problem?.ToString() ?? "ACCETTATO"}");
                flipsRefused &= problem != null;
                if (at >= 84) bodySaid &= problem == BackupProblem.Damaged;
            }
            string damagedText = "";
            var damaged = (byte[])file.Clone();
            damaged[200] ^= 0x80;
            try { Backup.Read(damaged, password); }
            catch (BackupException ex) { damagedText = ex.Message; }
            Check(flipsRefused && bodySaid && damagedText.Contains("rovinato o è stato modificato dopo l'esportazione"),
                  $"un byte cambiato (un bit, in otto punti del file): sempre rifiutato; nel contenuto e nel suo tag detto come file rovinato o modificato, con la password giusta ({string.Join("; ", flips)})");
            var future = (byte[])file.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(future.AsSpan(8), 2);
            BackupException? newer = null;
            try { BackupFile.Check(future); } catch (BackupException ex) { newer = ex; }
            Check(newer is { Problem: BackupProblem.NewerFormat } && newer.Message.Contains("più nuova") && newer.Message.Contains("formato 2") && newer.Message.Contains("Aggiorna Missiva")
                  && Refused(future, password) == BackupProblem.NewerFormat,
                  $"versione futura del formato: rifiutata senza chiedere la password, con la spiegazione (\"{newer?.Message}\")");

            // ---------------------------------------------------------------- importazione annullata o in attesa rovinata: non cambia nulla
            Pc(pcB);
            var dirG = Folder("annullata");
            WriteData(dirG, old, null, null, null);
            var before = File.ReadAllBytes(Path.Combine(dirG, Store.FileName));
            Backup.SavePending(dirG, Backup.Read(file, password));
            Backup.CancelPending(dirG);
            bool cancelled = Backup.ApplyPending(dirG) == null && File.ReadAllBytes(Path.Combine(dirG, Store.FileName)).SequenceEqual(before);
            File.WriteAllBytes(Path.Combine(dirG, Backup.PendingName), new byte[300]);
            var broken = Backup.ApplyPending(dirG);
            Check(cancelled && broken is { Applied: false } && broken.Heading == "Importazione non riuscita" && broken.Body.Contains("Non è cambiato nulla")
                  && File.ReadAllBytes(Path.Combine(dirG, Store.FileName)).SequenceEqual(before) && !Backup.HasPending(dirG) && Directory.GetFiles(dirG, "importazione-illeggibile-*").Length == 1
                  && Directory.GetFiles(dirG, "*-prima-dell-importazione-*").Length == 0,
                  "importazione annullata prima del riavvio: nulla da applicare; file in attesa rovinato: detto, messo da parte, e le impostazioni restano quelle di prima byte per byte");

            // ---------------------------------------------------------------- testi che sul PC di partenza non si leggono più
            Pc(pcX);
            var dirE = Folder("pc-a-con-illeggibili");
            var mixed = new Settings
            {
                Accounts = [new MailAccount { Id = "imap-prova", Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer { UserName = "mario@studio-prova.example", ProtectedPassword = Store.Protect("x") } }],
                Rules = [new MailRule { AccountId = "imap-prova", Name = "Regola di un altro PC", SenderContains = ["spam-prova"], Delete = true }],
                Templates = [new MailTemplate { Name = "Modello di un altro PC", Rtf = @"{\rtf1 testo}" }],
                Snoozed = [new SnoozedMessage { AccountId = "imap-prova", Key = "k", FolderId = "INBOX", Until = new DateTime(2026, 10, 25), Subject = "Oggetto di un altro PC", From = "Mario Rossi" }],
                Signatures = { ["imap-prova"] = new Signature { Rtf = @"{\rtf1 firma-leggibile-prova}" } },
            };
            WriteData(dirE, mixed, [new Contact { DisplayName = "Maria Rossi" }], null, null);
            Pc(pcA);
            var partial = Backup.Collect(Store.Read(dirE, out _), dirE, withPasswords: true, out var partialNotes);
            var partialSettings = Backup.ParseSettings(partial.Settings);
            // Dal blocco 10 (S-26) anche la firma è cifrata con DPAPI: scritta sul PC X, sul PC A non si legge più e resta fuori come
            // regola e modello (prima era in chiaro nel file e passava: "firma-leggibile-prova"); il resto, l'account, passa.
            Check(partialNotes.Count == 5 && partialSettings.Rules.Count == 0 && partialSettings.Templates.Count == 0 && partialSettings.Snoozed is [{ Subject: "", From: "", Key: "k" }]
                  && partialSettings.Signatures["imap-prova"].Rtf.Length == 0 && partialNotes.Any(n => n.Contains("firma")) && partialSettings.Accounts.Count == 1
                  && !partial.Settings.Contains("non leggibile") && !partial.Settings.Contains("firma-leggibile-prova") && partial.Contacts == null && partial.ImapPasswords.Count == 0,
                  $"testi cifrati che sul PC di partenza non si leggono più: la regola, il modello e la firma restano fuori dalla copia (una regola con una parola sbagliata agirebbe sui messaggi sbagliati) " +
                  $"e l'esportazione lo dice ({string.Join(" ", partialNotes)})");

            // ---------------------------------------------------------------- la password della copia non resta da nessuna parte
            Pc(null);
            var written = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            var log = Path.Combine(Store.Dir, "missiva.log");
            Check(!written.Any(f => Has(File.ReadAllBytes(f), password)) && !(File.Exists(log) && Has(File.ReadAllBytes(log), password)),
                  $"la password della copia non è in nessuno dei {written.Length} file scritti (impostazioni, importazioni in attesa, file messi da parte) né nel registro");
        }
        finally
        {
            Pc(null);
            ImapProvider.TestPlain = false;
            GoogleAuth.TestToken = testToken;
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Audit, blocco 6: le funzioni chieste dal rapporto sulle funzionalità (allegato dimenticato, annulla iscrizione,
    /// allegati grandi con Microsoft). Con "Missiva.exe --prova-audit risultato.txt funzioni" solo queste.
    /// </summary>
    static void FeatureAuditTests()
    {
        Report.Add("--- Audit, blocco 6: funzioni (allegato dimenticato, annulla iscrizione, allegati grandi con Microsoft)");
        ForgottenAttachmentAuditTests();
        UnsubscribeAuditTests();
        UnsubscribeRequestsAuditAsync().GetAwaiter().GetResult();
        UnsubscribeHeadersAuditAsync().GetAwaiter().GetResult();
        LargeAttachmentsAuditAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Un Graph finto per gli allegati a pezzi (F-15): bozze con i loro allegati, sessioni di caricamento su un altro sito
    /// (come Outlook), e i controlli su ogni PUT: senza token, Content-Range in fila e della misura dichiarata, pezzi multipli
    /// di 320 KiB e sotto 4 MB (tranne l'ultimo). Il contenuto ricomposto resta, per confrontarlo con il file.
    /// </summary>
    sealed class UploadGraph : HttpMessageHandler
    {
        public sealed record Stored(string Id, string Name, byte[] Content, bool Inline, string? ContentId);
        sealed class Session(string message, string name, long size, bool inline, string? contentId)
        {
            public string Message = message, Name = name;
            public long Size = size;
            public bool Inline = inline;
            public string? ContentId = contentId;
            public MemoryStream Data = new();
        }

        public readonly List<string> Requests = new();
        public readonly List<string> Wrong = new();
        public readonly Dictionary<string, List<Stored>> Drafts = new();
        readonly Dictionary<string, Session> sessions = new();
        public readonly List<string> ClosedSessions = new();
        public readonly List<string> Sent = new();
        /// <summary>Il PUT numero N (da 1, contati tutti) risponde con questo stato; 0 mai.</summary>
        public int FailPut;
        public HttpStatusCode FailWith = HttpStatusCode.InternalServerError;
        /// <summary>Chiamata a ogni PUT, con il suo numero, prima di rispondere.</summary>
        public Action<int>? OnPut;
        /// <summary>La lettura numero N dell'elenco degli allegati (da 1) risponde 500; 0 mai.</summary>
        public int FailListing;
        public int Puts, Listings;
        int next;

        static HttpResponseMessage Json(JsonNode? json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json?.ToJsonString() ?? "", Encoding.UTF8, "application/json") };

        static HttpResponseMessage Error(HttpStatusCode status, string message) =>
            Json(new JsonObject { ["error"] = new JsonObject { ["code"] = "ErroreDiProva", ["message"] = message } }, status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "carica-prova.example")
            {
                var key = uri.AbsolutePath;
                if (request.Headers.Authorization != null) Wrong.Add($"{request.Method} alla sessione con il token dell'account");
                if (!uri.Query.Contains("authtoken=")) Wrong.Add("indirizzo della sessione senza la sua chiave");
                if (request.Method == HttpMethod.Delete)
                {
                    Requests.Add("DELETE sessione");
                    ClosedSessions.Add(key);
                    sessions.Remove(key);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                var range = request.Content?.Headers.ContentRange;
                var bytes = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(cancel);
                Requests.Add($"PUT sessione {range}");
                int number = ++Puts;
                OnPut?.Invoke(number);
                cancel.ThrowIfCancellationRequested();
                if (number == FailPut)
                    return FailWith == HttpStatusCode.TooManyRequests ? new HttpResponseMessage(FailWith) { Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(20)) } }
                                                                    : Error(FailWith, "il server delle prove ha rifiutato il pezzo");
                if (request.Method != HttpMethod.Put || !sessions.TryGetValue(key, out var s)) return Error(HttpStatusCode.NotFound, "sessione sconosciuta");
                if (range == null || range.From != s.Data.Length || range.To - range.From + 1 != bytes.Length || range.Length != s.Size)
                    Wrong.Add($"Content-Range sbagliato: {range} con {bytes.Length} byte, attesi da {s.Data.Length} su {s.Size}");
                if (request.Content?.Headers.ContentType?.MediaType != "application/octet-stream") Wrong.Add("pezzo senza application/octet-stream");
                bool last = s.Data.Length + bytes.Length >= s.Size;
                if (bytes.Length >= 4 * 1024 * 1024 || !last && bytes.Length % (320 * 1024) != 0) Wrong.Add($"pezzo di {bytes.Length} byte");
                s.Data.Write(bytes);
                if (!last) return Json(new JsonObject { ["expirationDateTime"] = "2026-10-05T10:00:00Z", ["nextExpectedRanges"] = new JsonArray(s.Data.Length.ToString()) });
                var id = $"grande-{++next}";
                Drafts[s.Message].Add(new Stored(id, s.Name, s.Data.ToArray(), s.Inline, s.ContentId));
                sessions.Remove(key);
                var created = new HttpResponseMessage(HttpStatusCode.Created);
                created.Headers.Location = new Uri($"https://outlook.office.com/api/v2.0/Users('prova')/Messages('{s.Message}')/Attachments('{id}')");
                return created;
            }

            if (request.Headers.Authorization == null) Wrong.Add($"{request.Method} a Graph senza token");
            var path = Uri.UnescapeDataString(uri.PathAndQuery)["/v1.0/".Length..];
            var body = request.Content == null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancel));
            var bare = path.Split('?')[0];
            Requests.Add($"{request.Method} {bare}");
            var parts = bare.Split('/');
            int at = Array.IndexOf(parts, "messages"); // dopo me/ o users/indirizzo/
            string? draft = at >= 0 && parts.Length > at + 1 ? parts[at + 1] : null;
            string rest = at >= 0 ? string.Join('/', parts.Skip(at + 2)) : bare;
            if (request.Method == HttpMethod.Post && at >= 0 && draft == null)
            {
                var id = $"bozza-{++next}";
                Drafts[id] = new();
                return Json(new JsonObject { ["id"] = id });
            }
            if (draft == null || !Drafts.TryGetValue(draft, out var list)) return Error(HttpStatusCode.NotFound, "bozza sconosciuta");
            if (request.Method == HttpMethod.Post && rest == "attachments/createUploadSession")
            {
                var item = body?["AttachmentItem"];
                if (item?["attachmentType"]?.GetValue<string>() != "file" || item["name"] == null || item["size"] == null) Wrong.Add("createUploadSession senza tipo, nome o misura");
                var key = $"/sessioni/{++next}";
                sessions[key] = new Session(draft, item?["name"]?.GetValue<string>() ?? "", item?["size"]?.GetValue<long>() ?? 0, item?["isInline"]?.GetValue<bool>() ?? false, item?["contentId"]?.GetValue<string>());
                return Json(new JsonObject { ["uploadUrl"] = $"https://carica-prova.example{key}?authtoken=chiave-di-prova", ["expirationDateTime"] = "2026-10-05T10:00:00Z", ["nextExpectedRanges"] = new JsonArray("0-") }, HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Post && rest == "attachments")
            {
                var content = Convert.FromBase64String(body?["contentBytes"]?.GetValue<string>() ?? "");
                if (content.Length >= 3 * 1024 * 1024) Wrong.Add($"allegato di {content.Length} byte in una richiesta sola");
                var stored = new Stored($"piccolo-{++next}", body?["name"]?.GetValue<string>() ?? "", content, body?["isInline"]?.GetValue<bool>() ?? false, body?["contentId"]?.GetValue<string>());
                list.Add(stored);
                return Json(new JsonObject { ["id"] = stored.Id }, HttpStatusCode.Created);
            }
            JsonArray Listed() => new(list.Select(a => (JsonNode)new JsonObject { ["id"] = a.Id, ["name"] = a.Name, ["contentType"] = "application/octet-stream", ["size"] = a.Content.Length, ["isInline"] = a.Inline }).ToArray());
            if (request.Method == HttpMethod.Get && rest == "attachments")
                return ++Listings == FailListing ? Error(HttpStatusCode.InternalServerError, "elenco degli allegati non disponibile") : Json(new JsonObject { ["value"] = Listed() });
            if (request.Method == HttpMethod.Get && rest == "")
                return Json(new JsonObject { ["id"] = draft, ["subject"] = "Prova", ["isDraft"] = true, ["parentFolderId"] = "bozze", ["attachments"] = Listed() });
            if (request.Method == HttpMethod.Delete && rest.StartsWith("attachments/"))
                return new HttpResponseMessage(list.RemoveAll(a => a.Id == rest["attachments/".Length..]) > 0 ? HttpStatusCode.NoContent : HttpStatusCode.NotFound) { Content = new StringContent("") };
            if (request.Method == HttpMethod.Delete && rest == "")
            {
                Drafts.Remove(draft);
                return new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new StringContent("") };
            }
            if (request.Method == HttpMethod.Post && rest == "send")
            {
                Sent.Add(draft);
                return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("") };
            }
            return Json(new JsonObject { ["id"] = draft }); // PATCH
        }
    }

    sealed class ProgressList : IProgress<string>
    {
        public readonly List<string> Texts = new();
        public void Report(string value) => Texts.Add(value);
    }

    /// <summary>
    /// F-15: allegati da 3 MB in su con gli account Microsoft, a pezzi (createUploadSession), su un Graph finto che controlla
    /// i Content-Range e ricompone il contenuto: bozza, salvataggio dopo senza ricaricare, invio, errore e annullamento a
    /// metà con la bozza com'era, limite di 150 MB detto prima di cominciare, casella condivisa, immagine grande nel testo.
    /// </summary>
    static async Task LargeAttachmentsAuditAsync()
    {
        var fake = new UploadGraph();
        GraphProvider.UseTestHandler(fake);
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@outlook.example" });
        static byte[] Noise(int length, int seed)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }
        string Write(string name, byte[] bytes)
        {
            var path = Path.Combine(Path.GetDirectoryName(TempFile("vuoto-prova.txt", ""))!, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        int large = GraphProvider.LargeAttachment + 123;
        byte[] smallBytes = Noise(1500, 1), firstBytes = Noise(large, 2), secondBytes = Noise(large + GraphProvider.UploadChunk, 3);
        string small = Write("nota-prova.txt", smallBytes), first = Write("catalogo-prova.pdf", firstBytes), second = Write("listino-prova.pdf", secondBytes);
        var progress = new ProgressList();

        // Bozza nuova con un file piccolo e uno grande.
        var saved = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Catalogo", Html = "<p>In allegato il catalogo</p>", AttachmentPaths = [small, first], Progress = progress });
        var draftId = saved.Message.Id;
        var onServer = fake.Drafts[draftId];
        var puts = fake.Requests.Where(r => r.StartsWith("PUT")).ToList();
        int chunk = GraphProvider.UploadChunk;
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(chunk % (320 * 1024) == 0 && chunk < 4 * 1024 * 1024
              && puts.SequenceEqual([$"PUT sessione bytes 0-{chunk - 1}/{large}", $"PUT sessione bytes {chunk}-{2 * chunk - 1}/{large}", $"PUT sessione bytes {2 * chunk}-{large - 1}/{large}"])
              && fake.Requests.Count(r => r == $"POST me/messages/{draftId}/attachments/createUploadSession") == 1 && fake.Requests.Count(r => r == $"POST me/messages/{draftId}/attachments") == 1
              && fake.Wrong.Count == 0,
              $"allegati grandi: un file di {large / 1048576.0:0.0} MB parte con createUploadSession e {puts.Count} PUT da {chunk / 1024} KiB con i Content-Range in fila, senza il token; quello piccolo come prima" +
              (fake.Wrong.Count > 0 ? $" (sbagliato: {string.Join("; ", fake.Wrong)})" : ""));
        Check(onServer.Count == 2 && onServer[0].Content.SequenceEqual(smallBytes) && onServer[1].Name == "catalogo-prova.pdf" && onServer[1].Content.SequenceEqual(firstBytes),
              "allegati grandi: il contenuto ricomposto sul server è uguale al file, byte per byte");
        Check(saved.Attachments.Select(a => (a.Id, a.Name)).SequenceEqual(onServer.Select(a => (a.Id, a.Name))) && progress.Texts.Count == 3
              && progress.Texts[0] == "Caricamento di catalogo-prova.pdf: 0 di 3 MB…" && progress.Texts[1].Contains("1,3 di 3 MB"),
              $"allegati grandi: la finestra riceve i due allegati con gli id del server, e il caricamento si dice pezzo per pezzo ({string.Join(" | ", progress.Texts)})");

        // Il salvataggio automatico dopo, e l'invio: l'allegato è già sulla bozza, niente da ricaricare.
        fake.Requests.Clear();
        var again = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Catalogo", Html = "<p>In allegato il catalogo, come promesso</p>", Draft = saved, KeptAttachments = saved.Attachments });
        await graph.SendAsync(new OutgoingMessage { To = To, Subject = "Catalogo", Html = "<p>In allegato il catalogo, come promesso</p>", Draft = again, KeptAttachments = again.Attachments });
        Check(!fake.Requests.Any(r => r.StartsWith("PUT") || r.Contains("createUploadSession") || r.StartsWith("DELETE") || r == $"POST me/messages/{draftId}/attachments")
              && fake.Sent.SequenceEqual([draftId]) && fake.Drafts[draftId].Count == 2 && again.Attachments.Count == 2,
              $"allegati grandi: il salvataggio dopo e l'invio non ricaricano l'allegato già sulla bozza ({fake.Requests.Count(r => r.StartsWith("PUT"))} PUT), e il messaggio parte con i due allegati");

        // Errore a metà su una bozza che c'era già: il primo file grande è già sulla bozza, il secondo si ferma al secondo pezzo.
        var kept = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Listino", Html = "<p>Ecco</p>", AttachmentPaths = [small] });
        var keptId = kept.Message.Id;
        fake.Requests.Clear();
        fake.Puts = 0;
        fake.FailPut = 5; // tre pezzi del primo, poi il secondo pezzo del secondo
        string? stopped = null;
        try { await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Listino", Html = "<p>Ecco</p>", AttachmentPaths = [first, second], Draft = kept, KeptAttachments = kept.Attachments }); }
        catch (MailException ex) { stopped = ex.Message; }
        fake.FailPut = 0;
        var afterFailure = fake.Drafts[keptId].Select(a => a.Name).ToList();
        Check(stopped != null && stopped.StartsWith("Caricamento di listino-prova.pdf interrotto a 1,3 di 4,3 MB: l'allegato non è stato aggiunto.") && stopped.Contains("il server delle prove ha rifiutato il pezzo")
              && afterFailure.SequenceEqual(["nota-prova.txt"]) && fake.ClosedSessions.Count == 1,
              $"allegati grandi, errore a metà: detto in italiano con il file e il punto ({stopped}); la bozza resta com'era ({string.Join(", ", afterFailure)}), la sessione lasciata a metà si chiude");
        var retried = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Listino", Html = "<p>Ecco</p>", AttachmentPaths = [first, second], Draft = kept, KeptAttachments = kept.Attachments });
        Check(fake.Drafts[keptId].Select(a => a.Name).SequenceEqual(["nota-prova.txt", "catalogo-prova.pdf", "listino-prova.pdf"]) && fake.Drafts[keptId][2].Content.SequenceEqual(secondBytes)
              && retried.Attachments.Count == 3 && fake.Wrong.Count == 0,
              "allegati grandi: il salvataggio dopo l'errore carica tutto una volta sola, senza doppioni");

        // Una risposta 429 a un pezzo: lo stesso pezzo si ripete, e il contenuto è giusto.
        fake.Puts = 0;
        fake.FailPut = 2;
        fake.FailWith = HttpStatusCode.TooManyRequests;
        var busy = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Occupato", Html = "<p>Ecco</p>", AttachmentPaths = [first] });
        fake.FailPut = 0;
        fake.FailWith = HttpStatusCode.InternalServerError;
        Check(fake.Puts == 4 && fake.Drafts[busy.Message.Id].Single().Content.SequenceEqual(firstBytes) && fake.Wrong.Count == 0,
              $"allegati grandi: un pezzo rifiutato con 429 si ripete uguale ({fake.Puts} PUT per 3 pezzi), e il file arriva intero");

        // L'ultimo pezzo arriva ma l'elenco degli allegati non si legge: l'allegato che Outlook ha già messo sulla bozza esce.
        fake.Listings = 0;
        fake.FailListing = 2; // la prima lettura è quella di prima del caricamento
        string? unread = null;
        try { await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Listino", Html = "<p>Ecco</p>", AttachmentPaths = [first], Draft = busy, KeptAttachments = busy.Attachments }); }
        catch (MailException ex) { unread = ex.Message; }
        fake.FailListing = 0;
        Check(unread != null && fake.Drafts[busy.Message.Id].Count == 1 && fake.Drafts[busy.Message.Id][0].Id == busy.Attachments[0].Id,
              $"allegati grandi: caricato ma non riletto ({unread}), l'allegato in più esce dalla bozza: nessun doppione al salvataggio dopo");

        // Annullamento durante il secondo pezzo: su una bozza che c'era resta com'era; un messaggio nuovo in invio non parte e la sua bozza si elimina.
        using var leaving = new CancellationTokenSource();
        fake.Puts = 0;
        fake.OnPut = n => { if (n == 2) leaving.Cancel(); };
        bool cancelled = false;
        try { await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Listino", Html = "<p>Ecco</p>", AttachmentPaths = [second], Draft = busy, KeptAttachments = busy.Attachments, Cancel = leaving.Token }); }
        catch (OperationCanceledException) { cancelled = true; }
        int putsWhenCancelled = fake.Puts, draftsBefore = fake.Drafts.Count, sentBefore = fake.Sent.Count;
        using var leavingSend = new CancellationTokenSource();
        fake.Puts = 0;
        fake.OnPut = n => { if (n == 2) leavingSend.Cancel(); };
        bool sendCancelled = false;
        try { await graph.SendAsync(new OutgoingMessage { To = To, Subject = "Nuovo", Html = "<p>Ecco</p>", AttachmentPaths = [small, second], Cancel = leavingSend.Token }); }
        catch (OperationCanceledException) { sendCancelled = true; }
        fake.OnPut = null;
        Check(cancelled && putsWhenCancelled == 2 && fake.Drafts[busy.Message.Id].Count == 1 && sendCancelled && fake.Puts == 2 && fake.Drafts.Count == draftsBefore && fake.Sent.Count == sentBefore,
              $"allegati grandi, annullamento a metà: il caricamento si ferma al pezzo in corso ({putsWhenCancelled} PUT su 4), la bozza resta com'era, il messaggio nuovo non parte e la sua bozza si elimina");

        // Un errore a metà di un invio nuovo: niente bozza lasciata lì, niente partito.
        fake.Puts = 0;
        fake.FailPut = 2;
        string? notSent = null;
        try { await graph.SendAsync(new OutgoingMessage { To = To, Subject = "Nuovo", Html = "<p>Ecco</p>", AttachmentPaths = [first] }); }
        catch (MailException ex) { notSent = ex.Message; }
        fake.FailPut = 0;
        Check(notSent != null && notSent.Contains("catalogo-prova.pdf") && fake.Drafts.Count == draftsBefore && fake.Sent.Count == sentBefore,
              "allegati grandi, errore a metà di un invio: il messaggio non parte e la bozza creata per inviarlo si elimina");

        // Il limite di Outlook, detto prima di cominciare: nessuna richiesta.
        fake.Requests.Clear();
        string? tooBig = null;
        var heavy = new Attachment { Id = "pesante", Name = "archivio-prova.zip", Size = GraphProvider.MaxAttachments - 1024 * 1024 };
        try { await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Troppo", Html = "<p>Ecco</p>", AttachmentPaths = [first], Draft = busy, KeptAttachments = [heavy] }); }
        catch (MailException ex) { tooBig = ex.Message; }
        Check(tooBig != null && tooBig.Contains("152 MB") && tooBig.Contains("al massimo 150") && fake.Requests.Count == 0,
              $"allegati grandi: oltre 150 MB in tutto lo si dice prima di cominciare, senza richieste ({tooBig})");

        // Una foto grande nel testo: a pezzi anche lei, incorporata con il suo contentId, fra le immagini e non fra gli allegati.
        var photo = new InlineImage(EditorImages.NewContentId(), "foto-prova.png", "image/png", Noise(large, 4));
        var withPhoto = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Foto", Html = $"<p>Guarda</p><img src=\"cid:{photo.ContentId}\">", Images = [photo] });
        var photoOnServer = fake.Drafts[withPhoto.Message.Id].Single();
        fake.Requests.Clear();
        var samePhoto = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Foto", Html = $"<p>Guarda bene</p><img src=\"cid:{photo.ContentId}\">", Images = [photo], Draft = withPhoto });
        Check(photoOnServer is { Inline: true } && photoOnServer.ContentId == photo.ContentId && photoOnServer.Content.SequenceEqual(photo.Content) && withPhoto.Attachments.Count == 0
              && withPhoto.Images.Single().Id == photoOnServer.Id && !fake.Requests.Any(r => r.StartsWith("PUT") || r.Contains("createUploadSession")) && samePhoto.Images.Count == 1,
              "allegati grandi: una foto da 3 MB nel testo si carica a pezzi come immagine incorporata (isInline, contentId), e il salvataggio dopo non la ricarica");

        // Casella condivisa: users/indirizzo/ al posto di me/.
        var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = MicrosoftAuth.Organizations, Id = "lavoro-allegati-prova" };
        var info = new MailAccount { Kind = AccountKind.Microsoft, Email = "info@studio-prova.example", SharedOf = work.Id, Tenant = work.Tenant, Id = "info-allegati-prova" };
        Store.Settings.Accounts.Add(work);
        Store.Settings.Accounts.Add(info);
        GraphProvider.TestSharedConsent = true;
        try
        {
            fake.Requests.Clear();
            var shared = new GraphProvider(info);
            var fromShared = await shared.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Dalla condivisa", Html = "<p>Ecco</p>", AttachmentPaths = [first] });
            Check(fake.Requests.Contains($"POST users/info@studio-prova.example/messages/{fromShared.Message.Id}/attachments/createUploadSession") && !fake.Requests.Any(r => r.Contains(" me/"))
                  && fake.Drafts[fromShared.Message.Id].Single().Content.SequenceEqual(firstBytes) && fake.Wrong.Count == 0,
                  "allegati grandi, casella condivisa: la sessione si apre su users/indirizzo/ e il file arriva intero");
        }
        finally
        {
            GraphProvider.TestSharedConsent = false;
            Store.Settings.Accounts.Remove(work);
            Store.Settings.Accounts.Remove(info);
        }
        foreach (var path in new[] { small, first, second }) File.Delete(path);
    }

    const string NewsDomain = "notizie-prova.example";
    const string NewsHttps = "https://liste.notizie-prova.example/via?id=123&k=abc";
    const string NewsMailto = "mailto:via@notizie-prova.example?subject=unsubscribe";
    const string OneClick = "List-Unsubscribe=One-Click";
    static readonly MailAccount NewsGmail = new() { Kind = AccountKind.Gmail, Email = "mario.prova@gmail.example" };

    static string NewsAuth(string domain = NewsDomain, string result = "pass") =>
        $"mx.google.com; dkim={result} header.i=@{domain}; spf={result} smtp.mailfrom=x@{domain}; dmarc={result} header.from={domain}";

    static MessageSummary NewsMail(string from = "notizie@" + NewsDomain, string folder = "INBOX", bool draft = false) =>
        new() { Id = "n1", FolderId = folder, Subject = "Le novità della settimana", Date = DateTime.Now, From = new Address { Name = "Notizie di prova", Email = from }, IsDraft = draft };

    static MessageBody NewsBody(string? list, string? post = null, string? auth = null) =>
        new() { Html = "<p>Le novità</p>", ListUnsubscribe = list, ListUnsubscribePost = post, AuthenticationResults = auth == null ? [] : [auth] };

    /// <summary>F-11: List-Unsubscribe di ogni tipo, e quando il comando c'è e che cosa farebbe. Nessuna richiesta di rete.</summary>
    static void UnsubscribeAuditTests()
    {
        static string Show((string? Https, string? Mailto) l) => $"{l.Https ?? "-"} | {l.Mailto ?? "-"}";
        var both = Unsubscribe.Links($"<{NewsHttps}>, <{NewsMailto}>");
        var reversed = Unsubscribe.Links($"(annulla qui) <{NewsMailto}> ,\r\n <{NewsHttps}> (oppure dal sito)");
        var folded = Unsubscribe.Links("<https://liste.notizie-prova.example/via?\r\n id=123&\r\n\tk=abc>");
        var many = Unsubscribe.Links("<https://uno.notizie-prova.example/a>, <https://due.notizie-prova.example/b>, <mailto:uno@notizie-prova.example>, <mailto:due@notizie-prova.example>");
        Check(Unsubscribe.Links($"<{NewsMailto}>") == (null, NewsMailto) && Unsubscribe.Links($"<{NewsHttps}>") == (NewsHttps, null) && both == (NewsHttps, NewsMailto) && reversed == both
              && folded == (NewsHttps, null)
              && many == ("https://uno.notizie-prova.example/a", "mailto:uno@notizie-prova.example?subject=unsubscribe"),
              $"annulla iscrizione: List-Unsubscribe con solo mailto, solo https, tutti e due in ogni ordine, con commenti e a capo dentro il link, più indirizzi (vale il primo di ogni tipo: {Show(many)})");

        // Scritte male o con indirizzi che non si accettano: nessun comando, nessuna eccezione.
        string[] bad =
        [
            "", " ", NewsHttps, "<" + NewsHttps, NewsHttps + ">", "<>", "< >", "<http://liste.notizie-prova.example/via>", "<javascript:alert(1)>", "<file:///C:/Windows/win.ini>",
            "<ftp://liste.notizie-prova.example/via>", "<https://utente:segreto@liste.notizie-prova.example/via>", "<https://127.0.0.1/via>", "<https://[::1]/via>", "<https://localhost/via>",
            "<https://liste.notizie-prova.example:8443/via>", "<https://>", "<//liste.notizie-prova.example/via>", "<mailto:>", "<mailto:senza-chiocciola>", "<mailto:due@@notizie-prova.example>",
            "<mailto:via@senzapunto>", "<mailto:?to=&subject=unsubscribe>", "<<<<<<<<", ">>>>>>>>", "\0\0\0", "<https://liste.notizie-prova.example/" + new string('a', 3000) + ">",
            "<" + NewsHttps + ">" + new string(' ', 9000),
        ];
        var accepted = bad.Where(b => Unsubscribe.Links(b) != (null, null)).ToList();
        var late = string.Concat(Enumerable.Range(0, 8).Select(k => $"<http://n{k}.notizie-prova.example/>, ")) + $"<{NewsHttps}>";
        var afterLong = Unsubscribe.Links("<https://liste.notizie-prova.example/" + new string('a', 3000) + ">, <" + NewsHttps + ">");
        Check(accepted.Count == 0 && Unsubscribe.Links(null) == (null, null) && Unsubscribe.Links(late) == (null, null) && afterLong == (NewsHttps, null)
              && Unsubscribe.Links("<<" + NewsHttps + ">") == (NewsHttps, null),
              $"annulla iscrizione: {bad.Length} intestazioni scritte male, con http, altri schemi, nome utente, indirizzi numerici o locali, un'altra porta, mailto senza indirizzo, lunghe 9 KB: nessun comando" +
              (accepted.Count > 0 ? $" (accettate: {string.Join(" | ", accepted.Select(a => a.Length > 60 ? a[..60] : a))})" : "") + "; solo i primi 8 link si guardano, uno di 3000 caratteri si salta");

        // Il mailto: ridotto a un destinatario, oggetto e testo: mai Cc e Ccn scelti dal mittente.
        var crowded = Unsubscribe.Links("<mailto:via@notizie-prova.example,altro@altrove-prova.example?cc=terzo@altrove-prova.example&bcc=quarto@altrove-prova.example&subject=Annulla%20123&body=riga%20uno%0D%0Ariga%20due&to=quinto@altrove-prova.example>").Mailto;
        var parsed = crowded == null ? null : Mailto.Parse(crowded);
        var plus = Unsubscribe.Links("<mailto:via+abc=123@notizie-prova.example>").Mailto;
        Check(parsed is { To: ["via@notizie-prova.example"], Cc.Count: 0, Bcc.Count: 0, Subject: "Annulla 123", Body: "riga uno\nriga due" }
              && plus != null && Mailto.Parse(plus) is { To: ["via+abc=123@notizie-prova.example"], Subject: "unsubscribe", Body: "" },
              $"annulla iscrizione: il mailto: diventa una scrittura con un solo destinatario, l'oggetto e il testo del link, senza Cc né Ccn ({crowded})");

        Check(Unsubscribe.IsOneClick(OneClick) && Unsubscribe.IsOneClick(" list-unsubscribe=one-click\r\n") && !Unsubscribe.IsOneClick(null) && !Unsubscribe.IsOneClick("")
              && !Unsubscribe.IsOneClick(OneClick + ", altro") && !Unsubscribe.IsOneClick("One-Click") && !Unsubscribe.IsOneClick(new string('x', 5000)),
              "annulla iscrizione: List-Unsubscribe-Post vale solo se dice esattamente List-Unsubscribe=One-Click");

        // Che cosa farebbe il comando.
        var list = $"<{NewsHttps}>, <{NewsMailto}>";
        UnsubscribeAction? For(MessageBody body, MailAccount? account = null, MessageSummary? mail = null, bool junk = false) => Unsubscribe.For(account ?? NewsGmail, mail ?? NewsMail(), body, junk);
        var oneClick = For(NewsBody(list, OneClick, NewsAuth()));
        Check(oneClick == new UnsubscribeAction(UnsubscribeKind.OneClick, NewsHttps) && oneClick.Where == "liste.notizie-prova.example"
              && For(NewsBody($"<{NewsMailto}>", OneClick, NewsAuth())) == new UnsubscribeAction(UnsubscribeKind.Mailto, NewsMailto)
              && For(NewsBody(list, null, NewsAuth())) is { Kind: UnsubscribeKind.Mailto, Where: "via@notizie-prova.example" }
              && For(NewsBody($"<{NewsHttps}>", null, NewsAuth())) == new UnsubscribeAction(UnsubscribeKind.Web, NewsHttps)
              && For(NewsBody($"<{NewsHttps}>", "List-Unsubscribe=Altro", NewsAuth())) is { Kind: UnsubscribeKind.Web },
              "annulla iscrizione: un clic (POST) con List-Unsubscribe-Post, https e messaggio autenticato; con il solo mailto o senza One-Click la scrittura già compilata; con il solo https la pagina nel browser");

        // Senza autenticazione per il dominio del mittente niente POST: resta la scrittura o la pagina, che fa l'utente.
        var imapAccount = new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer { ImapHost = "imap.studio-prova.example" } };
        var microsoft = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example" };
        UnsubscribeAction?[] unauthenticated =
        [
            For(NewsBody(list, OneClick)), // nessuna Authentication-Results
            For(NewsBody(list, OneClick, NewsAuth("altro-prova.example"))), // autenticato, ma per un altro dominio
            For(NewsBody(list, OneClick, NewsAuth(NewsDomain, "none"))),
            For(NewsBody(list, OneClick, "mittente.example; dkim=pass header.i=@" + NewsDomain + "; dmarc=pass header.from=" + NewsDomain)), // scritta dal mittente, non da Gmail
            For(NewsBody(list, OneClick, NewsAuth("gmail.example")), mail: NewsMail("qualcuno@gmail.com")), // posta personale: nessun dominio del marchio
            For(NewsBody(list, OneClick, NewsAuth()), imapAccount), // IMAP: l'authserv-id non è del server della casella
            For(NewsBody(list, OneClick, "mx.google.com; dkim=pass header.i=@" + NewsDomain + "; dmarc=pass header.from=\"" + NewsDomain)), // scritta male
        ];
        Check(unauthenticated.All(a => a is { Kind: UnsubscribeKind.Mailto }) && For(NewsBody($"<{NewsHttps}>", OneClick)) is { Kind: UnsubscribeKind.Web }
              // Dall'audit dell'11 ottobre (T-03): su IMAP nemmeno con il nome del server della casella, che può averlo scritto chi manda.
              && For(NewsBody(list, OneClick, "imap.studio-prova.example; dkim=pass header.i=@" + NewsDomain + "; dmarc=pass header.from=" + NewsDomain), imapAccount) is { Kind: UnsubscribeKind.Mailto }
              && For(NewsBody(list, OneClick, "spf=pass smtp.mailfrom=" + NewsDomain + "; dkim=pass header.d=" + NewsDomain + "; dmarc=pass action=none header.from=" + NewsDomain), microsoft) is { Kind: UnsubscribeKind.OneClick },
              $"annulla iscrizione: senza un messaggio autenticato per il dominio del mittente (7 casi: nessuna intestazione, altro dominio, risultato none, scritta dal mittente, posta personale, IMAP di un altro server, scritta male) mai la POST ({string.Join(", ", unauthenticated.Select(a => a?.Kind.ToString() ?? "nessuno"))}); " +
              "con Microsoft autenticato sì; su IMAP mai, nemmeno con il nome del server della casella");

        // Mai nella Posta indesiderata, con la fascia rossa, nelle bozze, senza intestazione.
        Check(For(NewsBody(list, OneClick, NewsAuth()), junk: true) == null && For(NewsBody(list, OneClick, NewsAuth(NewsDomain, "fail"))) == null
              && SenderCheck.Of(NewsGmail, NewsMail(), NewsBody(list, OneClick, NewsAuth(NewsDomain, "fail"))).Unverified != null
              && For(NewsBody(list, OneClick, NewsAuth()), mail: NewsMail(draft: true)) == null && For(NewsBody(null, OneClick, NewsAuth())) == null && For(NewsBody("", null, NewsAuth())) == null
              && Unsubscribe.For(NewsGmail, NewsMail(), null, false) == null && For(NewsBody("<http://liste.notizie-prova.example/via>", OneClick, NewsAuth())) == null,
              "annulla iscrizione: nessun comando nella Posta indesiderata, con la fascia rossa del mittente non verificato, nelle bozze, senza List-Unsubscribe o con il solo http");

        // La conferma dice che cosa succede e verso dove, con l'indirizzo intero.
        var (h1, b1, p1) = Unsubscribe.Confirmation(oneClick!, "Notizie di prova", NewsGmail.Email);
        var (_, b2, p2) = Unsubscribe.Confirmation(new(UnsubscribeKind.Mailto, NewsMailto), "Notizie di prova", NewsGmail.Email);
        var (_, b3, p3) = Unsubscribe.Confirmation(new(UnsubscribeKind.Web, NewsHttps), "Notizie di prova", NewsGmail.Email);
        var lookalike = new UnsubscribeAction(UnsubscribeKind.Web, Unsubscribe.Links("<https://liste.nоtizie-prova.example/via>").Https ?? ""); // la "о" è cirillica
        Check(h1 == "Annullare l'iscrizione?" && b1.Contains("liste.notizie-prova.example") && b1.Contains(NewsHttps) && b1.Contains(NewsGmail.Email) && b1.Contains("Notizie di prova") && p1 == "Annulla iscrizione"
              && b2.Contains("via@notizie-prova.example") && b2.Contains("solo quando premi Invia") && p2 == "Prepara il messaggio"
              && b3.Contains("liste.notizie-prova.example") && b3.Contains(NewsHttps) && p3 == "Apri nel browser"
              && lookalike.Target.Length > 0 && lookalike.Where.Contains("xn--") && Safety.LinkWarning("", new Uri(lookalike.Target)) != null,
              $"annulla iscrizione: la conferma dice che cosa succede e verso quale sito o indirizzo, con l'indirizzo intero; un sito con lettere non latine si vede in punycode ({lookalike.Where}) e passa dal controllo dei link");

        // La riga nella pagina: il comando è un pulsante di modulo verso /do/gettone/unsubscribe/N; senza comando non c'è.
        var withLine = UI.ReadingPage.Build([new UI.PageEntry(NewsMail(), NewsBody(list), true, false, null, false, Unsubscribe: "")], true);
        var done = UI.ReadingPage.Build([new UI.PageEntry(NewsMail(), NewsBody(list), true, false, null, false, Unsubscribe: "Richiesta inviata a <b>prova</b>.")], true);
        var without = UI.ReadingPage.Build([new UI.PageEntry(NewsMail(), NewsBody(list), true, false, null, false)], true);
        var closed = UI.ReadingPage.Build([new UI.PageEntry(NewsMail(), null, false, false, null, false, Unsubscribe: ""), new UI.PageEntry(NewsMail(), NewsBody(list), true, false, null, false)], true);
        Check(withLine.Html.Contains($"/do/{withLine.Token}/unsubscribe/0\"") && withLine.Html.Contains(">Annulla iscrizione</button>")
              && !done.Html.Contains("/unsubscribe/") && done.Html.Contains("Richiesta inviata a &lt;b&gt;prova&lt;/b&gt;.")
              && !without.Html.Contains("/unsubscribe/") && !closed.Html.Contains("/unsubscribe/"),
              "annulla iscrizione: nella pagina il comando è un pulsante verso /do/gettone/unsubscribe/N, solo nei messaggi aperti che lo hanno; dopo la richiesta al suo posto c'è il testo");
    }

    /// <summary>
    /// F-11: le richieste contate su un gestore HTTP finto: nessuna senza conferma, in Posta indesiderata o senza
    /// autenticazione; con la conferma una sola POST, con il corpo della RFC 8058, senza cookie né credenziali, e nessun
    /// reindirizzamento seguito. Nessuna richiesta di rete vera.
    /// </summary>
    static async Task UnsubscribeRequestsAuditAsync()
    {
        var seen = new List<(string Method, string Url, string? Type, string Body, bool Cookie, bool Authorization)>();
        HttpStatusCode status = HttpStatusCode.OK;
        string? location = null;
        bool unreachable = false;
        Unsubscribe.UseTestHandler(new FakeHttp(request =>
        {
            seen.Add((request.Method.Method, request.RequestUri!.AbsoluteUri, request.Content?.Headers.ContentType?.ToString(), request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "",
                      request.Headers.Contains("Cookie"), request.Headers.Authorization != null));
            if (unreachable) throw new HttpRequestException("prova: sito non raggiungibile");
            var response = new HttpResponseMessage(status) { Content = new StringContent("fatto") };
            if (location != null) response.Headers.Location = new Uri(location);
            return response;
        }));
        try
        {
            var list = $"<{NewsHttps}>, <{NewsMailto}>";
            var action = Unsubscribe.For(NewsGmail, NewsMail(), NewsBody(list, OneClick, NewsAuth()), false)!;
            string asked = "";
            bool Yes(string heading, string body, string primary) { asked = heading + "\n" + body + "\n" + primary; return true; }
            bool No(string heading, string body, string primary) { asked = heading + "\n" + body + "\n" + primary; return false; }
            Task<Unsubscribe.Outcome> Run(UnsubscribeAction a, Func<string, string, string, bool> confirm, bool dryRun = false) => Unsubscribe.OneClickAsync(a, "Notizie di prova", NewsGmail.Email, confirm, dryRun);

            // Leggere il messaggio, calcolare il comando e costruire la pagina non chiede nulla a nessuno.
            _ = UI.ReadingPage.Build([new UI.PageEntry(NewsMail(), NewsBody(list, OneClick, NewsAuth()), true, false, null, false, Unsubscribe: "")], true);
            int afterShowing = seen.Count;
            var refused = await Run(action, No);
            Check(afterShowing == 0 && seen.Count == 0 && refused == new Unsubscribe.Outcome(false, null) && asked.Contains(NewsHttps) && asked.Contains("liste.notizie-prova.example"),
                  $"annulla iscrizione: senza il clic e senza la conferma nessuna richiesta ({seen.Count}); la conferma mostra il sito e l'indirizzo intero");

            // Posta indesiderata e messaggio non autenticato: il comando di un clic non esiste, e OneClickAsync rifiuta le altre azioni.
            var junk = Unsubscribe.For(NewsGmail, NewsMail(), NewsBody(list, OneClick, NewsAuth()), junk: true);
            var plain = Unsubscribe.For(NewsGmail, NewsMail(), NewsBody(list, OneClick), false)!;
            var web = Unsubscribe.For(NewsGmail, NewsMail(), NewsBody($"<{NewsHttps}>", OneClick), false)!;
            asked = "";
            var notOneClick = await Run(plain, Yes);
            var notOneClickWeb = await Run(web, Yes);
            Check(junk == null && plain.Kind == UnsubscribeKind.Mailto && web.Kind == UnsubscribeKind.Web && seen.Count == 0 && asked == ""
                  && notOneClick is { Confirmed: false, Error: not null } && notOneClickWeb is { Confirmed: false, Error: not null },
                  $"annulla iscrizione: nella Posta indesiderata e senza autenticazione nessuna richiesta ({seen.Count}), nemmeno chiedendola con la conferma già data");

            // Con la conferma: una POST, come la vuole la RFC 8058.
            var ok = await Run(action, Yes);
            Check(ok == new Unsubscribe.Outcome(true, null) && seen.Count == 1
                  && seen[0] == ("POST", NewsHttps, "application/x-www-form-urlencoded", OneClick, false, false),
                  $"annulla iscrizione: con la conferma una sola POST all'indirizzo del messaggio, corpo \"{seen.FirstOrDefault().Body}\", tipo {seen.FirstOrDefault().Type}, senza cookie né credenziali");

            // Un reindirizzamento (verso http, o verso un altro sito) non si segue; un errore del sito e un sito che non risponde si dicono.
            seen.Clear();
            status = HttpStatusCode.Found;
            location = "http://liste.notizie-prova.example/fatto";
            var toHttp = await Run(action, Yes);
            location = "https://altrove-prova.example/fatto";
            var elsewhere = await Run(action, Yes);
            int afterRedirects = seen.Count;
            location = null;
            status = HttpStatusCode.InternalServerError;
            var failed = await Run(action, Yes);
            status = HttpStatusCode.OK;
            unreachable = true;
            var down = await Run(action, Yes);
            unreachable = false;
            Check(afterRedirects == 2 && seen.All(s => s.Url == NewsHttps) && toHttp is { Confirmed: true, Error: not null } && toHttp.Error!.Contains("non segue") && elsewhere.Error != null
                  && failed.Error!.Contains("500") && down.Error!.Contains("collegarsi") && seen.Count == 4,
                  $"annulla iscrizione: un reindirizzamento (verso http o verso un altro sito) non si segue e non conta come riuscito; errore 500 e sito non raggiungibile detti ({seen.Count} richieste per 4 tentativi)");

            // Versione di prova: solo il registro.
            seen.Clear();
            var dry = await Run(action, Yes, dryRun: true);
            Check(dry == new Unsubscribe.Outcome(true, null) && seen.Count == 0, "annulla iscrizione: nella versione di prova la POST va solo nel registro (nessuna richiesta)");

            // Il gestore vero: nessun reindirizzamento, nessun cookie, nessun proxy, e il collegamento passa dal controllo degli
            // indirizzi pubblici dei loghi (qui fermato dal gancio di prova prima di cercare il nome: niente rete).
            var handler = Unsubscribe.CreateHandler();
            var connects = new List<string>();
            SenderLogos.TestConnect = connects.Add;
            Unsubscribe.UseTestHandler(null);
            Unsubscribe.Outcome real;
            try { real = await Run(action, Yes); }
            finally { SenderLogos.TestConnect = null; }
            Check(!handler.AllowAutoRedirect && !handler.UseCookies && !handler.UseProxy && handler.ConnectCallback != null && handler.Credentials == null
                  && connects.SequenceEqual(["liste.notizie-prova.example:443"]) && real is { Confirmed: true, Error: not null },
                  $"annulla iscrizione: il gestore vero non segue reindirizzamenti, non tiene cookie, non usa il proxy e si collega solo passando dal controllo degli indirizzi pubblici ({string.Join(", ", connects)})");
        }
        finally
        {
            Unsubscribe.UseTestHandler(null);
        }
    }

    /// <summary>
    /// F-11: le due intestazioni arrivano con il corpo, senza richieste in più: IMAP nella stessa richiesta delle
    /// Authentication-Results (messaggio grande) o nel messaggio intero (piccolo), Graph in internetMessageHeaders; e restano
    /// nella copia su disco.
    /// </summary>
    static async Task UnsubscribeHeadersAuditAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false)); // il messaggio grande si legge parte per parte (BODY.PEEK[1.MIME] e [1])
        ImapProvider.TestPlain = true;
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        var list = $"<{NewsHttps}>,\r\n <{NewsMailto}>";
        MimeKit.MimeMessage News(string subject, int attachment)
        {
            var mime = new MimeKit.MimeMessage { Subject = subject, Date = DateTimeOffset.Now };
            mime.From.Add(new MimeKit.MailboxAddress("Notizie di prova", "notizie@" + NewsDomain));
            mime.To.Add(new MimeKit.MailboxAddress("Mario Rossi", "mario@studio-prova.example"));
            mime.Headers.Add("Authentication-Results", "studio-prova.example; dkim=pass header.i=@" + NewsDomain + "; dmarc=pass header.from=" + NewsDomain);
            mime.Headers.Add("List-Unsubscribe", list);
            mime.Headers.Add("List-Unsubscribe-Post", OneClick);
            var builder = new MimeKit.BodyBuilder { HtmlBody = "<p>Notizie della settimana</p>" }; // solo ASCII: il server finto dà il testo senza codifica
            if (attachment > 0) builder.Attachments.Add("catalogo-prova.bin", Enumerable.Range(0, attachment).Select(k => (byte)(k * 31)).ToArray());
            mime.Body = builder.ToMessageBody();
            return mime;
        }
        fake.Add("INBOX", News("Piccolo", 0));
        fake.Add("INBOX", News("Grande", 400_000));
        MailCache.Delete(account);
        BodyCache.Delete(account);
        try
        {
            using var imap = new ImapProvider(account);
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var rows = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            var small = rows.First(m => m.Subject == "Piccolo");
            var large = rows.First(m => m.Subject == "Grande");
            fake.Commands.Clear();
            var smallBody = await imap.GetBodyAsync(small);
            int smallHeaders = fake.Commands.Count(c => c.Contains("HEADER.FIELDS"));
            fake.Commands.Clear();
            var largeBody = await imap.GetBodyAsync(large);
            var headerFetches = fake.Commands.Where(c => c.Contains("HEADER.FIELDS")).ToList();
            bool together = headerFetches.Count == 1 && headerFetches[0].Contains("AUTHENTICATION-RESULTS", StringComparison.OrdinalIgnoreCase)
                            && headerFetches[0].Contains("LIST-UNSUBSCRIBE LIST-UNSUBSCRIBE-POST", StringComparison.OrdinalIgnoreCase);
            var fromSmall = Unsubscribe.For(account, small, smallBody, false);
            var fromLarge = Unsubscribe.For(account, large, largeBody, false);
            // Il comando è il messaggio da inviare, non il clic singolo: dall'audit dell'11 ottobre (T-03) su un account IMAP
            // l'intestazione con il nome del server non prova che il messaggio è autenticato.
            Check(smallHeaders == 0 && together && fromSmall is { Kind: UnsubscribeKind.Mailto } && fromSmall.Target.StartsWith("mailto:via@" + NewsDomain) && fromLarge == fromSmall
                  && largeBody.AuthenticationResults.Count == 1 && largeBody.Attachments.Count == 1,
                  $"annulla iscrizione, IMAP: le due intestazioni arrivano con il corpo, nel messaggio intero (piccolo, {smallHeaders} richieste di intestazioni) o nella stessa richiesta delle Authentication-Results (grande: {headerFetches.Count}); il comando è lo stesso ({fromLarge?.Kind})");

            // La copia su disco le tiene: il comando c'è anche aprendo il messaggio dalla copia.
            BodyCache.Save(account, large, largeBody);
            Task[] writes;
            lock (BodyCache.TestWrites) writes = [.. BodyCache.TestWrites];
            Task.WaitAll(writes);
            var copy = BodyCache.Load(account, large);
            Check(copy != null && Unsubscribe.For(account, large, copy, false) == fromLarge,
                  "annulla iscrizione: le intestazioni restano nella copia su disco del messaggio, e il comando è lo stesso");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            MailCache.Delete(account);
            BodyCache.Delete(account);
        }

        // Graph: internetMessageHeaders è già nel $select del corpo; una richiesta sola.
        var requests = new List<string>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            requests.Add(request.Method.Method + " " + Uri.UnescapeDataString(request.RequestUri!.PathAndQuery));
            var message = new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = "n1", ["hasAttachments"] = false,
                ["body"] = new System.Text.Json.Nodes.JsonObject { ["contentType"] = "html", ["content"] = "<p>Le novità della settimana</p>" },
                ["toRecipients"] = new System.Text.Json.Nodes.JsonArray(), ["ccRecipients"] = new System.Text.Json.Nodes.JsonArray(), ["bccRecipients"] = new System.Text.Json.Nodes.JsonArray(),
                ["attachments"] = new System.Text.Json.Nodes.JsonArray(),
                ["internetMessageHeaders"] = new System.Text.Json.Nodes.JsonArray(
                    new System.Text.Json.Nodes.JsonObject { ["name"] = "Authentication-Results", ["value"] = "spf=pass smtp.mailfrom=" + NewsDomain + "; dkim=pass header.d=" + NewsDomain + "; dmarc=pass action=none header.from=" + NewsDomain },
                    new System.Text.Json.Nodes.JsonObject { ["name"] = "list-unsubscribe", ["value"] = list },
                    new System.Text.Json.Nodes.JsonObject { ["name"] = "List-Unsubscribe-Post", ["value"] = OneClick }),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var microsoft = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = "organizations" };
        var graph = new GraphProvider(microsoft);
        var graphBody = await graph.GetBodyAsync(NewsMail());
        Check(requests.Count == 1 && requests[0].Contains("internetMessageHeaders") && Unsubscribe.For(microsoft, NewsMail(), graphBody, false) == new UnsubscribeAction(UnsubscribeKind.OneClick, NewsHttps),
              $"annulla iscrizione, Microsoft: le intestazioni arrivano con il corpo in una sola richiesta ({requests.Count}), e il comando è un clic");
    }

    /// <summary>F-14: le parole che parlano di un allegato nel testo scritto, non nella firma né nel messaggio citato.</summary>
    static void ForgottenAttachmentAuditTests()
    {
        static string? Found(string text, params string[] signatures) => UI.ComposeWindow.AttachmentMention(text, signatures);
        string[] yes =
        [
            "Buongiorno,\r\nti mando in allegato il preventivo.\r\nMario", "Allego la fattura di settembre", "Vi alleghiamo il contratto firmato", "La fattura allegata è di ieri",
            "ecco i documenti allegati.", "Trovi accluso il modulo", "Please find attached the invoice", "See the ATTACHMENT for details", "I'm attaching the report",
            "Two attachments, as promised", "Please find enclosed our offer", "Te lo mando allegandolo qui? No: è allegato.", "Da: Roma parto domani, in allegato il biglietto",
        ];
        string[] no =
        [
            "Ci vediamo domani alle 10", "È un'allegoria collegata al tema", "Puoi allegare il file quando rispondi?", "Please attach your CV", "Ti scrivo senza allegati",
            "Nessun allegato questa volta", "Stavolta non allego nulla", "No attachment this time, sorry", "sent without any attachments", "", "reattached unattached attachments_old",
        ];
        var missedYes = yes.Where(t => Found(t) == null).ToList();
        var wrongNo = no.Where(t => Found(t) != null).ToList();
        Check(missedYes.Count == 0 && wrongNo.Count == 0 && Found(yes[0]) == "allegato" && Found(yes[1]) == "Allego",
              $"allegato dimenticato: {yes.Length} frasi che parlano di un allegato riconosciute (parole intere, italiano e inglese), {no.Length} che non ne parlano no" +
              (missedYes.Count + wrongNo.Count > 0 ? $" (sbagliate: {string.Join(" | ", missedYes.Concat(wrongNo))})" : ""));

        // La firma: "questo messaggio e i suoi allegati sono riservati" non è un allegato promesso; scritto sopra la firma sì.
        const string signature = "Mario Rossi\r\nStudio Rossi, via Roma 1\r\nQuesto  messaggio e i suoi allegati sono riservati. This message and any attachments are confidential.";
        const string signed = "\r\n\r\nMario Rossi\r\nStudio Rossi, via Roma 1\r\nQuesto messaggio e i suoi allegati sono riservati. This message and any attachments are confidential.\r\n";
        Check(Found("Ciao Maria,\r\na presto" + signed, signature) == null && Found("Ciao Maria,\r\na presto" + signed) == "allegati"
              && Found("Ciao Maria,\r\nin allegato il listino" + signed, signature) == "allegato" && Found("Ciao" + signed, "", "Un'altra firma", signature) == null,
              "allegato dimenticato: le righe della firma non contano (anche con gli spazi cambiati e fra più firme), il testo sopra la firma sì");

        // La firma com'è nella finestra: dall'RTF delle impostazioni a un documento, e il testo del documento.
        var document = new System.Windows.Documents.FlowDocument(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Ciao Maria, a domani")));
        document.Blocks.Add(new System.Windows.Documents.Paragraph());
        const string rtf = @"{\rtf1\ansi Mario Rossi\par Questo messaggio e i suoi allegati sono riservati.\par}";
        var section = new System.Windows.Documents.Section();
        section.Blocks.AddRange(UI.RichEditor.BlocksFromRtf(rtf));
        document.Blocks.Add(section);
        var signatureText = new System.Windows.Documents.TextRange(section.ContentStart, section.ContentEnd).Text;
        var whole = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd).Text;
        Check(signatureText.Contains("allegati") && Found(whole, signatureText) == null && Found(whole) == "allegati",
              "allegato dimenticato: la firma letta dall'RTF delle impostazioni e messa nel documento non fa scattare l'avviso");

        // Il messaggio citato (una bozza ripresa di una risposta lo ha nel testo): intestazione di Missiva e Gmail, di Outlook, righe con ">".
        string[] quoted =
        [
            "Va bene, grazie.\r\n\r\nIl lunedì 5 ottobre 2026 10:00, Maria Rossi <maria@fornitori-prova.example> ha scritto:\r\nIn allegato il listino.",
            "Ok.\r\nOn Mon, Oct 5, 2026 at 10:00 AM Maria Rossi <maria@fornitori-prova.example> wrote:\r\nPlease find attached the price list.",
            "Ok.\r\nDa: Maria Rossi <maria@fornitori-prova.example>\r\nInviato: lunedì 5 ottobre 2026 10:00\r\nA: Mario Rossi\r\nOggetto: Listino\r\nIn allegato il listino.",
            "Ok.\r\n-----Messaggio originale-----\r\nIn allegato il listino.", "Ok.\r\n> In allegato il listino.\r\n> Maria",
        ];
        var wrongQuoted = quoted.Where(t => Found(t) != null).ToList();
        Check(wrongQuoted.Count == 0 && Found("Ti allego la mia risposta.\r\n" + quoted[0]) == "allego",
              $"allegato dimenticato: l'allegato di cui parla il messaggio citato non conta ({quoted.Length} forme di citazione), quello scritto sopra sì" +
              (wrongQuoted.Count > 0 ? $" (sbagliate: {string.Join(" | ", wrongQuoted)})" : ""));
        Check(new Settings().AttachmentReminder && System.Text.Json.JsonSerializer.Deserialize<Settings>("{}")!.AttachmentReminder,
              "allegato dimenticato: l'avviso è acceso di base, anche con le impostazioni di una versione di prima");
    }

    // ---------------------------------------------------------------- blocco 10: sicurezza, punti di gravità Bassa

    /// <summary>
    /// Audit, blocco 10: i punti di sicurezza di gravità Bassa (docs\audit\sicurezza.md). Con
    /// "Missiva.exe --prova-audit risultato.txt sicurezza" solo queste.
    /// </summary>
    static void SecurityLowAuditTests()
    {
        Report.Add("--- Audit, blocco 10: sicurezza, punti di gravità Bassa");
        DangerousMoreAuditTests();
        TempCleanAuditTests();
        WebMarkAuditTests();
        AttachmentNameAuditTests();
        AutoconfigAuditAsync().GetAwaiter().GetResult();
        NotificationReplyAuditTests();
        PipeCommandAuditAsync().GetAwaiter().GetResult();
        LinkHostAuditTests();
        EditorImageLimitsAuditTests();
        Task.Run(PdfWorkerSecurityAuditAsync).GetAwaiter().GetResult(); // il processo dei PDF, come PdfTestsAsync
        LogPrivacyAuditTests();
        SettingsPrivacyAuditTests();
    }

    /// <summary>
    /// S-26: firme, mittenti delle immagini e calendari nascosti non stanno in chiaro in impostazioni.json (cifrati con DPAPI
    /// come modelli e regole); quelli scritti in chiaro dalle versioni di prima si leggono e si cifrano; un cifrato che non si
    /// decifra non entra in un messaggio come firma, torna nel file com'era e non entra nella copia di sicurezza.
    /// </summary>
    static void SettingsPrivacyAuditTests()
    {
        const string iban = "IT60X0542811101000000123456";
        var mainRtf = @"{\rtf1 Mario Rossi\par IBAN " + iban + @"\par Tel 051 000000\par}";
        var shortRtf = @"{\rtf1 M. Rossi, +39 333 0000000}";
        string Serialize(Settings s) => System.Text.Json.JsonSerializer.Serialize(s, SettingsJson.Default.Settings);
        Settings Read(string json) => System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.Settings)!;
        var account = new MailAccount { Id = "a", Kind = AccountKind.Imap, Email = "mario@studio-prova.example" };

        var settings = new Settings { ImageSenders = ["newsletter-prova.example", "maria.rossi@gmail.example"], HiddenCalendars = ["ms-prova|maria.rossi@example.com"] };
        settings.Signatures["a"] = new Signature { Rtf = mainRtf, More = [new NamedSignature { Id = "b", Name = "Breve", Rtf = shortRtf }] };
        var json = Serialize(settings);
        Check(!json.Contains(iban) && !json.Contains("Mario Rossi") && !json.Contains("333 0000000") && !json.Contains("newsletter-prova") && !json.Contains("maria.rossi") && json.Contains("dpapi:")
              && json.Contains("\"Breve\""), $"S-26: nel JSON delle impostazioni la firma (nome, IBAN, telefono), i mittenti delle immagini e i calendari nascosti non sono in chiaro ({json.Length} caratteri)");
        var back = Read(json);
        Check(back.Signatures["a"].Rtf == mainRtf && back.Signatures["a"].More[0].Rtf == shortRtf && back.Signatures["a"].More[0].Name == "Breve" && back.ImageSenders.SequenceEqual(settings.ImageSenders)
              && back.HiddenCalendars.SequenceEqual(settings.HiddenCalendars) && Serialize(back) == json && back.SignatureFor(account, reply: false) != null,
              "S-26: riletti, firma principale e firma con nome, mittenti delle immagini e calendari nascosti sono quelli di prima, e il file non cambia a un secondo salvataggio");

        // Quello che le versioni di prima hanno scritto in chiaro (firme come testo, elenchi come array) si legge e al salvataggio si cifra.
        var legacy = Read("{\"Signatures\":{\"a\":{\"Rtf\":\"{\\\\rtf1 Mario Rossi IBAN " + iban + "}\",\"More\":[{\"Id\":\"b\",\"Name\":\"Breve\",\"Rtf\":\"{\\\\rtf1 breve}\"}]}}," +
                          "\"ImageSenders\":[\"newsletter-prova.example\"],\"HiddenCalendars\":[\"ms-prova|cal-vecchio\"]}");
        var resealed = Serialize(legacy);
        Check(legacy.Signatures["a"].Rtf.Contains(iban) && legacy.Signatures["a"].More[0].Rtf == @"{\rtf1 breve}" && legacy.ImageSenders is ["newsletter-prova.example"] && legacy.HiddenCalendars is ["ms-prova|cal-vecchio"]
              && !resealed.Contains(iban) && !resealed.Contains("newsletter-prova") && !resealed.Contains("cal-vecchio") && Read(resealed).Signatures["a"].Rtf == legacy.Signatures["a"].Rtf,
              "S-26: le impostazioni scritte in chiaro da una versione di prima si leggono, e al salvataggio dopo firme, mittenti e calendari si cifrano");

        // Un cifrato che su questo PC non si apre: mai una firma nei messaggi, e nel file torna com'era.
        var broken = Read("{\"Signatures\":{\"a\":{\"Rtf\":\"dpapi:AAAA\",\"More\":[{\"Id\":\"b\",\"Name\":\"Breve\",\"Rtf\":\"dpapi:BBBB\"}]}},\"ImageSenders\":\"dpapi:CCCC\",\"HiddenCalendars\":\"dpapi:DDDD\"}");
        broken.UpgradeImageSenders(); // come a ogni avvio
        var kept = Serialize(broken);
        Check(broken.Signatures["a"].Rtf == "" && broken.Signatures["a"].More[0].Rtf == "" && broken.SignatureFor(account, reply: false) == null && broken.SignatureFor(account, reply: true) == null
              && broken.SignaturesOf(account, null) == null && broken.Signatures["a"].All().All(s => s.Rtf.Length == 0)
              && broken.ImageSenders.Count == 1 && ProtectedTextConverter.IsUnreadable(broken.ImageSenders[0]) && !broken.ShowsImagesFrom("info@newsletter-prova.example"),
              "S-26: firme che non si decifrano: nessuna firma nei messaggi (il segnaposto non entra mai nel testo), né nella scelta della firma; un elenco che non si decifra non fa mostrare immagini a nessuno");
        Check(kept.Contains("dpapi:AAAA") && kept.Contains("dpapi:BBBB") && kept.Contains("dpapi:CCCC") && kept.Contains("dpapi:DDDD") && Serialize(Read(kept)) == kept,
              "S-26: i quattro cifrati che non si decifrano tornano nel file com'erano, anche dopo l'aggiornamento dei mittenti all'avvio e un secondo giro");
        broken.AllowImagesFrom("info@nuova-prova.example");
        Check(broken.ImageSenders is ["nuova-prova.example"], "S-26: scegliendo di mostrare le immagini di un mittente, l'elenco che non si decifrava riparte da quello");
        var note = Read("{\"Signatures\":{\"a\":{\"Rtf\":\"dpapi:EEEE\"}},\"ImageSenders\":\"dpapi:FFFF\",\"HiddenCalendars\":\"dpapi:GGGG\"}");
        var dir = Path.Combine(Path.GetTempPath(), "missiva-audit-x-copia-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var data = Backup.Collect(note, dir, withPasswords: false, out var notes);
            Check(!data.Settings.Contains("testo cifrato non leggibile") && !data.Settings.Contains("dpapi:") && notes.Any(n => n.Contains("firma")),
                  $"S-26: nella copia di sicurezza il segnaposto delle firme, dei mittenti e dei calendari non entra, e lo dice ({string.Join(" | ", notes)})");
            var full = Backup.Collect(settings, dir, withPasswords: false, out var fullNotes);
            var round = Backup.ParseSettings(full.Settings);
            Check(round.Signatures["a"].Rtf == mainRtf && round.Signatures["a"].More[0].Rtf == shortRtf && round.ImageSenders.SequenceEqual(settings.ImageSenders) && round.HiddenCalendars.SequenceEqual(settings.HiddenCalendars)
                  && !full.Settings.Contains("dpapi:") && fullNotes.Count == 0,
                  "S-26: la copia di sicurezza porta firme, mittenti e calendari in chiaro dentro il suo file cifrato (senza DPAPI), e si rilegge uguale");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// S-27: il registro della versione installata non dice nomi di allegati e di calendari (Log.Private) né gli indirizzi
    /// nel testo degli errori di SMTP e IMAP. Log.Private e Log.Error, nella Debug, lasciano tutto per la prova: qui si prova
    /// la pulizia (Log.WithoutAddresses, quella dell'installata) e che le righe usano Log.Private (lettura dei sorgenti).
    /// </summary>
    static void LogPrivacyAuditTests()
    {
        var smtp = new MailKit.Net.Smtp.SmtpCommandException(MailKit.Net.Smtp.SmtpErrorCode.RecipientNotAccepted, MailKit.Net.Smtp.SmtpStatusCode.MailboxUnavailable,
                                                             new MimeKit.MailboxAddress("", "mario.rossi@example.com"), "550 5.1.1 <mario.rossi@example.com>: Recipient address rejected: User unknown");
        var imap = new InvalidOperationException("[NONEXISTENT] Mailbox does not exist for maria.rossi+clienti@studio-prova.example (folder Clienti/Rossi/Causa)");
        var cleanSmtp = Log.WithoutAddresses(smtp.ToString());
        var cleanImap = Log.WithoutAddresses(imap.ToString());
        const string plain = "Impossibile connettersi al server imap.studio-prova.example:993 (timeout dopo 30 s)";
        Check(!cleanSmtp.Contains('@') && cleanSmtp.Contains("SmtpCommandException") && cleanSmtp.Contains("Recipient address rejected") && cleanSmtp.Contains("(indirizzo)")
              && !cleanImap.Contains('@') && cleanImap.Contains("NONEXISTENT") && cleanImap.Contains("Clienti/Rossi/Causa") && Log.WithoutAddresses(plain) == plain
              && Log.WithoutAddresses("") == "",
              $"S-27: dal testo degli errori di SMTP e IMAP escono gli indirizzi, restano il tipo e la risposta del server ({cleanSmtp.Split('\n')[0]})");
        Check(Log.Private("Referto_Rossi.pdf") != "Referto_Rossi.pdf", "S-27: Log.Private non lascia il nome nudo");
        if (SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: le righe del registro non sono state lette)");
            return;
        }
        string Source(string file) => File.ReadAllText(Path.Combine(root, file));
        Check(Source(@"UI\ReadingView.xaml.cs").Contains("Log.Write($\"anteprima di {Log.Private(attachment.Name)}\")")
              && Source(@"UI\P7mWindow.cs").Contains("Log.Write($\"file firmato {Log.Private(result.Name)}")
              && Source(@"UI\ComposeWindow.xaml.cs").Contains("Log.Write($\"immagine nel testo: {Log.Private(data.Name)}")
              && Source(@"Core\GmailProvider.Calendar.cs").Contains("Log.Write($\"calendario {Log.Private(c.Name)} di"),
              "S-27: nel registro i nomi di allegati (anteprima, file firmato, immagine nel testo) e di calendari passano da Log.Private");
    }

    /// <summary>
    /// S-13: il processo dei PDF non riceve le variabili d'ambiente di Missiva, ne ha una lista bianca; il job è
    /// obbligatorio. Dentro il vero AppContainer: una variabile finta non si vede, e gli appunti e lo schermo non si prendono.
    /// </summary>
    static async Task PdfWorkerSecurityAuditAsync()
    {
        static int Workers() => Process.GetProcessesByName("Missiva").Length;
        using var worker = new PdfWorker();

        // S-13, l'ambiente: il blocco passato a CreateProcess ha solo la lista bianca, e il processo vero non vede la variabile.
        Environment.SetEnvironmentVariable("MISSIVA_PROVA_TOKEN", "token finto");
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", @"C:\finto\hook.dll");
        string block;
        try { block = Sandbox.EnvironmentBlock(); }
        finally { Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null); }
        var names = block.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(v => v[..v.IndexOf('=')]).ToHashSet(StringComparer.OrdinalIgnoreCase); // Windows non distingue le maiuscole
        string[] allowed = ["SystemRoot", "windir", "SystemDrive", "TEMP", "TMP", "LOCALAPPDATA", "PATHEXT", "OS", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS"];
        Check(!names.Contains("MISSIVA_PROVA_TOKEN") && !names.Contains("DOTNET_STARTUP_HOOKS") && !names.Contains("USERPROFILE") && names.Contains("SystemRoot")
              && block.Contains("DOTNET_EnableDiagnostics=0") && names.All(n => allowed.Contains(n, StringComparer.OrdinalIgnoreCase) || n.StartsWith("DOTNET_")),
              $"S-13: l'ambiente del processo dei PDF è la lista bianca ({string.Join(", ", names)}), senza la variabile finta, DOTNET_STARTUP_HOOKS né i percorsi dell'utente");
        try
        {
            var secret = TempFile("segreto-x.txt", "token finto");
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var reach = await worker.TestReachAsync(secret, ((IPEndPoint)listener.LocalEndpoint).Port, "MISSIVA_PROVA_TOKEN");
            listener.Stop();
            Report.Add("      dal processo dei PDF: " + reach);
            Check(reach.Contains("variabile MISSIVA_PROVA_TOKEN: assente") && reach.StartsWith("file non letto") && reach.Contains("non collegato") && reach.Contains("appunti non aperti"),
                  $"S-13: il processo dei PDF, nel suo AppContainer, non vede la variabile d'ambiente di Missiva, non legge file, non si collega, non apre gli appunti ({reach})");
            // Lo schermo (GetDC(NULL)) il processo lo ottiene lo stesso, anche con i limiti dell'interfaccia: non è un divieto che questo job dà.
            // Non ha dove mandarlo (niente rete, file, appunti: solo il tubo verso Missiva), quindi non si controlla.
        }
        finally
        {
            Environment.SetEnvironmentVariable("MISSIVA_PROVA_TOKEN", null);
        }

        // S-13, il job è obbligatorio: se non si crea, il processo avviato sospeso si termina, e l'anteprima lo dice.
        using (var failing = new PdfWorker())
        {
            int before = Workers();
            PdfWorker.TestFailJob = true;
            string? refused = null;
            try { await failing.LoadAsync(Pdf(1)); }
            catch (Exception ex) { refused = PdfRender.Explain(ex); }
            finally { PdfWorker.TestFailJob = false; }
            for (int i = 0; i < 20 && Workers() > before; i++) await Task.Delay(100);
            Check(refused?.Contains("anteprima protetta non si è potuta avviare") == true && failing.TestProcess == null && Workers() == before,
                  $"S-13: se il job non si crea il processo si termina (processi di Missiva prima {before}, dopo {Workers()}) e l'anteprima lo dice ({refused})");
            var (okPages, _, _) = await failing.LoadAsync(Pdf(2));
            Check(okPages == 2 && failing.TestInJob(), "S-13: senza l'errore di prova lo stesso lavoro riparte, nel job");
        }
        await worker.LoadAsync(Pdf(1));
        var jobLimits = worker.TestJobLimits();
        Check(worker.TestInJob() && worker.TestInAppContainer() && jobLimits is { } l && (l.Flags & 0x2108) == 0x2108 && (l.Flags & 0x100) != 0 && l.ActiveProcesses == 1
              && l.Memory == 3UL * 1024 * 1024 * 1024 && l.Ui == 0xFF,
              $"S-13: il processo dei PDF è nell'AppContainer e nel job con i limiti che Windows riporta: un solo processo, chiusura con Missiva, memoria {(jobLimits?.Memory ?? 0) >> 20} MB, interfaccia 0x{jobLimits?.Ui:X}");

        // S-12, i tetti del processo: una pagina molto alta si disegna più stretta, con le sue proporzioni; oltre 5000 pagine no.
        var (_, tallWidth, tallHeight) = await worker.LoadAsync(Pdf(1, mediaBox: "0 0 100 20000"));
        var tallPage = await worker.RenderAsync(0, 1000);
        bool tallOk = PdfRender.IsAcceptablePng(tallPage, out var pw, out var ph);
        Check(tallOk && ph <= PdfRender.MaxPixelHeight && (long)pw * ph <= PdfRender.MaxPixels && Math.Abs(ph / (double)pw - tallHeight / tallWidth) < 3,
              $"S-12: una pagina 100 x 20000 punti chiesta larga 1000 pixel arriva {pw} x {ph} (altezza al massimo {PdfRender.MaxPixelHeight}, proporzioni {ph / (double)pw:0.#} come {tallHeight / tallWidth:0.#}), non 1000 x 200000");
        string? tooMany = null;
        try { await worker.LoadAsync(Pdf(PdfRender.MaxPages + 1)); }
        catch (Exception ex) { tooMany = PdfRender.Explain(ex); }
        Check(tooMany?.Contains("troppe pagine") == true, $"S-12: un PDF con più di {PdfRender.MaxPages} pagine non va in anteprima, e lo dice ({tooMany})");
        var again = await worker.LoadAsync(Pdf(3));
        Check(again.Pages == 3, "S-12: il processo, che ha risposto con un errore onesto, resta e il PDF dopo si carica");

        // S-12, le immagini: un 1440 x 65535 arriva rimpicciolito, non intero; oltre 150 milioni di pixel si rifiuta.
        var tallImage = await worker.ImageAsync(BlankPng(1440, 65535), 1440);
        bool imageOk = PdfRender.IsAcceptablePng(tallImage, out var iw, out var ih);
        Check(imageOk && ih <= PdfRender.MaxPixelHeight && iw <= 1440 && (long)iw * ih <= PdfRender.MaxPixels && Math.Abs(ih / (double)iw - 65535 / 1440.0) < 2,
              $"S-12: un'immagine 1440 x 65535 nel processo arriva {iw} x {ih} (non intera: ~377 MB), con le sue proporzioni");
        worker.TestProcess?.Refresh();
        Report.Add($"      memoria del processo dei PDF dopo le richieste più grandi: picco {(worker.TestProcess?.PeakPagedMemorySize64 ?? 0) >> 20} MB impegnati, {(worker.TestProcess?.PeakWorkingSet64 ?? 0) >> 20} MB in uso (limite 3072)");
        string? hugeImage = null;
        try { await worker.ImageAsync(BlankPng(13000, 11600), 1440); }
        catch (Exception ex) { hugeImage = ex.Message; }
        Check(hugeImage?.Contains("troppo grande") == true, $"S-12: un'immagine da 150 milioni di pixel o più non si decodifica ({hugeImage})");

        // S-12, quello che Missiva legge dal processo: risposte costruite a mano, come le manderebbe un processo compromesso.
        static BinaryReader Reader(Action<BinaryWriter> write) { var ms = new MemoryStream(); var w = new BinaryWriter(ms); write(w); w.Flush(); ms.Position = 0; return new BinaryReader(ms); }
        static string? Rejects(Action action) { try { action(); return null; } catch (Exception ex) { return ex.GetType().Name; } }
        var pagesOk = PdfWorker.ReadLoaded(Reader(w => { w.Write(3); w.Write(595.0); w.Write(842.0); }));
        var pageBombs = new (string Name, Action<BinaryWriter> Write)[]
        {
            ("2^31-1 pagine", w => { w.Write(int.MaxValue); w.Write(595.0); w.Write(842.0); }),
            ("0 pagine", w => { w.Write(0); w.Write(595.0); w.Write(842.0); }),
            ("pagine negative", w => { w.Write(-5); w.Write(595.0); w.Write(842.0); }),
            ("larghezza NaN", w => { w.Write(3); w.Write(double.NaN); w.Write(842.0); }),
            ("altezza infinita", w => { w.Write(3); w.Write(595.0); w.Write(double.PositiveInfinity); }),
            ("misura zero", w => { w.Write(3); w.Write(0.0); w.Write(842.0); }),
        };
        var notRejected = pageBombs.Where(b => Rejects(() => PdfWorker.ReadLoaded(Reader(b.Write))) != "WorkerProtocolException").Select(b => b.Name).ToList();
        Check(pagesOk is (3, 595.0, 842.0) && notRejected.Count == 0, $"S-12: Missiva non accetta dal processo pagine e misure fuori misura (non rifiutate: {string.Join(", ", notRejected)})");

        static byte[] HeaderPng(int width, int height)
        {
            var png = new byte[40];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(png, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
            return png;
        }
        var pngBombs = new (string Name, byte[] Png)[]
        {
            ("IHDR 100000 x 100000", HeaderPng(100_000, 100_000)), ("IHDR 4000 x 30000", HeaderPng(4000, 30_000)), ("IHDR 4000 x 12000 (48 Mpixel)", HeaderPng(4000, 12_000)),
            ("IHDR larghezza 0", HeaderPng(0, 10)), ("IHDR negativo", HeaderPng(-1, 10)), ("non un PNG", new byte[64]),
        };
        var pngNotRejected = pngBombs.Where(b => Rejects(() => PdfWorker.ReadPng(Reader(w => { w.Write(b.Png.Length); w.Write(b.Png); }))) != "WorkerProtocolException").Select(b => b.Name).ToList();
        var fine = HeaderPng(1470, 2079);
        var hugeLength = Rejects(() => PdfWorker.ReadPng(Reader(w => w.Write(int.MaxValue))));
        Check(pngNotRejected.Count == 0 && PdfWorker.ReadPng(Reader(w => { w.Write(fine.Length); w.Write(fine); })).Length == fine.Length && hugeLength == "WorkerProtocolException",
              $"S-12: Missiva non decodifica un PNG con l'intestazione fuori misura (non rifiutati: {string.Join(", ", pngNotRejected)}), né legge una risposta da 2 GB; uno normale passa");

        string? longError = null, newlineError = null, hugeError = null;
        try { PdfWorker.Expect(Reader(w => { w.Write((byte)'E'); w.Write(-1); w.Write(new string('x', 3000)); }), 'P'); } catch (Exception ex) { longError = ex.Message; }
        try { PdfWorker.Expect(Reader(w => { w.Write((byte)'E'); w.Write(-1); w.Write("riga uno\r\nriga due\0fine"); }), 'P'); } catch (Exception ex) { newlineError = ex.Message; }
        hugeError = Rejects(() => PdfWorker.Expect(Reader(w => { w.Write((byte)'E'); w.Write(-1); w.Write(new string('y', 100_000)); }), 'P'));
        Check(longError?.Length == PdfRender.MaxMessage && newlineError == "riga unoriga duefine" && hugeError == "WorkerProtocolException",
              $"S-12: il messaggio d'errore del processo è al massimo {PdfRender.MaxMessage} caratteri senza a capo né caratteri di controllo ({longError?.Length}, \"{newlineError}\"), e uno da 100.000 si rifiuta");
    }

    /// <summary>Un PNG a toni di grigio tutto nero, grande quanto si vuole (compresso: pochi KB per milioni di pixel).</summary>
    static byte[] BlankPng(int width, int height)
    {
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, new byte[(long)width * height], width);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// S-15: le immagini che il testo decodifica (incollate, trascinate, o riprese da una bozza) hanno un tetto anche
    /// sull'altezza e sui pixel di partenza, non solo sulla larghezza; quelle normali escono come prima. Il cid dell'immagine
    /// non cambia (vedi ESITO.md): un cid con un HMAC farebbe perdere le immagini delle bozze già salvate.
    /// </summary>
    static void EditorImageLimitsAuditTests()
    {
        var tall = BlankPng(1440, 65535); // l'esempio del rapporto: arrivava intera, 377 MB
        var clock = Stopwatch.StartNew();
        var decodedTall = EditorImages.Decode(tall);
        var tallTime = clock.ElapsedMilliseconds;
        Check(decodedTall is { PixelHeight: <= 8192, PixelWidth: <= 2 * (int)EditorImages.MaxWidth } && decodedTall.PixelHeight * 8 >= 8192 * 7,
              $"S-15: un'immagine 1440 x 65535 si decodifica rimpicciolita ({decodedTall?.PixelWidth} x {decodedTall?.PixelHeight} pixel, tetto 8192 di altezza, {tallTime} ms), non intera (377 MB)");
        var narrow = EditorImages.Decode(BlankPng(100, 30000));
        Check(narrow is { PixelHeight: <= 8192, PixelWidth: > 0 }, $"S-15: anche una stretta e altissima (100 x 30000 diventa {narrow?.PixelWidth} x {narrow?.PixelHeight})");
        var huge = EditorImages.Decode(BlankPng(10500, 10000)); // 105 milioni di pixel
        Check(huge == null, "S-15: oltre 100 milioni di pixel (10500 x 10000) non si decodifica nemmeno rimpicciolita");
        var photo = EditorImages.Decode(BlankPng(4000, 3000));
        var small = EditorImages.Decode(BlankPng(300, 200));
        var screen = EditorImages.Decode(BlankPng(1440, 3000));
        Check(photo is { PixelWidth: 1440, PixelHeight: 1080 } && small is { PixelWidth: 300, PixelHeight: 200 } && screen is { PixelWidth: 1440, PixelHeight: 3000 },
              $"S-15: le immagini normali escono come prima (4000 x 3000 a {photo?.PixelWidth} x {photo?.PixelHeight}, 300 x 200 intatta, 1440 x 3000 intatta)");
        // E per la finestra: l'Image che si mette nel testo ha una larghezza che non supera MaxWidth e la sua bitmap il tetto.
        var picture = EditorImages.Create(new InlineImage(EditorImages.NewContentId(), "alta.png", "image/png", tall));
        Check(picture is { Source: BitmapSource { PixelHeight: <= 8192 } } && picture.Width <= EditorImages.MaxWidth, "S-15: l'immagine alta messa nel testo ha la bitmap col tetto e la larghezza al massimo di MaxWidth");
    }

    /// <summary>
    /// S-05: un link il cui host, scritto con il punto ideografico, lettere a larghezza intera o il trattino facoltativo,
    /// Chromium legge come quello della pagina del riquadro (missiva.example) non resta fra i link: l'host si normalizza
    /// (IDN) prima del confronto. Gli altri link restano com'erano.
    /// </summary>
    static void LinkHostAuditTests()
    {
        static MailHtml.Result Prepare(string href) => MailHtml.Prepare($"<html><body><p><a href=\"{href}\">clicca</a></p></body></html>", false, false, _ => null);
        string[] disguised =
        [
            "https://missiva。example/x", "https://missiva．example/x", "https://missiva｡example/x", "https://ｍｉｓｓｉｖａ.example/x",
            "https://miss­iva.example/x", "https://MISSIVA.EXAMPLE./x", "https://missiva.example:443/x", "http://missiva。example。/x",
        ];
        var kept = disguised.Where(h => Prepare(h).Links.Count > 0).ToList();
        Check(kept.Count == 0, $"S-05: i link verso l'host della pagina del riquadro scritto con punto ideografico, larghezza intera, trattino facoltativo o punto finale non restano (restano: {string.Join(" | ", kept.Select(h => string.Concat(h.Select(c => c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString()))))})");
        string[] normal = ["https://www.esempio-prova.example/pagina?a=1", "http://esempio-prova.example/", "https://münchen-prova.example/", "https://missiva.example.esempio-prova.example/", "https://esempio-prova.example/missiva.example"];
        var results = normal.Select(h => Prepare(h).Links.Select(l => l.Href).ToList()).ToList();
        Check(results.All(r => r.Count == 1) && results[0][0] == "https://www.esempio-prova.example/pagina?a=1" && results[1][0] == "http://esempio-prova.example/",
              $"S-05: i link normali restano com'erano (anche con un host non latino e con \"missiva.example\" dentro un altro nome): {string.Join(" | ", results.Select(r => string.Join(",", r)))}");
    }

    /// <summary>
    /// S-22: il canale con nome legge una riga di al più 64 KB entro la scadenza: un client fermo, o che manda troppo senza
    /// a capo, non tiene occupata la copia aperta. Su un canale di prova vero (un nome loro), non quello di Missiva. Il
    /// primo piano va al processo all'altro capo del canale, non a tutti.
    /// </summary>
    static async Task PipeCommandAuditAsync()
    {
        var name = "missiva-audit-x-" + Guid.NewGuid().ToString("N")[..8];
        async Task<(string? Line, long Ms)> Serve(Func<System.IO.Pipes.NamedPipeClientStream, Task> client, TimeSpan timeout, int max = UI.App.MaxCommandBytes)
        {
            using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Byte,
                                                                         System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
            using var peer = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.CurrentUserOnly);
            var connected = server.WaitForConnectionAsync();
            peer.Connect(3000);
            await connected;
            var clock = Stopwatch.StartNew();
            var writing = Task.Run(async () => { try { await client(peer); } catch { } });
            var line = await UI.App.ReadCommandAsync(server, max, timeout);
            var took = clock.ElapsedMilliseconds;
            server.Dispose(); // come il giro dopo di ListenForMailto: il collegamento si chiude
            await Task.WhenAny(writing, Task.Delay(3000));
            return (line, took);
        }
        static async Task Write(System.IO.Pipes.NamedPipeClientStream peer, string text) { var bytes = new UTF8Encoding(false).GetBytes(text); await peer.WriteAsync(bytes); await peer.FlushAsync(); }

        var link = "mailto:maria.rossi@example.com?subject=Ciao%20caff%C3%A8&body=Perch%C3%A9 no";
        var normal = await Serve(p => Write(p, link + "\r\n"), TimeSpan.FromSeconds(3));
        var open = await Serve(p => Write(p, "apri\n"), TimeSpan.FromSeconds(3));
        var noNewline = await Serve(async p => { await Write(p, "esci"); p.Dispose(); }, TimeSpan.FromSeconds(3));
        var utf8 = await Serve(p => Write(p, "mailto:?subject=Città di Perù\n"), TimeSpan.FromSeconds(3));
        Check(normal.Line == link && open.Line == "apri" && noNewline.Line == "esci" && utf8.Line == "mailto:?subject=Città di Perù",
              $"S-22: sul canale una riga normale arriva intera (mailto con \\r\\n, \"apri\", \"esci\" senza a capo, accentate) in {normal.Ms}, {open.Ms}, {noNewline.Ms} ms");

        // Un client che si collega e non scrive mai: la lettura finisce alla scadenza (qui 400 ms, nell'app 3 s) e il canale si libera.
        var silent = await Serve(async p => await Task.Delay(1500), TimeSpan.FromMilliseconds(400));
        var halfLine = await Serve(async p => { await Write(p, "mailto:maria.rossi@example.com"); await Task.Delay(1500); }, TimeSpan.FromMilliseconds(400));
        Check(silent.Line == null && halfLine.Line == null && silent.Ms is >= 300 and < 1400 && halfLine.Ms is >= 300 and < 1400,
              $"S-22: un client fermo, o che scrive mezza riga e si ferma, non tiene il canale oltre la scadenza ({silent.Ms} e {halfLine.Ms} ms con 400 ms; nell'app 3 s)");

        // Troppo senza a capo: oltre 64 KB la riga non si accetta, senza leggere tutto (qui 2 MB nel canale).
        var flood = await Serve(p => Write(p, new string('a', 2 * 1024 * 1024)), TimeSpan.FromSeconds(3));
        var justEnough = await Serve(p => Write(p, new string('b', UI.App.MaxCommandBytes) + "\n"), TimeSpan.FromSeconds(3));
        var justOver = await Serve(p => Write(p, new string('c', UI.App.MaxCommandBytes + 1) + "\n"), TimeSpan.FromSeconds(3));
        Check(flood.Line == null && flood.Ms < 1500 && justEnough.Line?.Length == UI.App.MaxCommandBytes && justOver.Line == null,
              $"S-22: una riga senza a capo di 2 MB si rifiuta in {flood.Ms} ms; 64 KB giusti passano, un byte in più no");

        // Il primo piano: il processo all'altro capo del canale (qui questo), non tutti.
        using (var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Byte,
                                                                      System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly))
        using (var peer = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.CurrentUserOnly))
        {
            var connected = server.WaitForConnectionAsync();
            peer.Connect(3000);
            await connected;
            Check(Program.ServerProcessId(peer) == (uint)Environment.ProcessId, $"S-22: il client sa chi sta all'altro capo del canale ({Program.ServerProcessId(peer)}, questo processo è {Environment.ProcessId}): il permesso di primo piano va a lui");
        }
        if (SourceRoot() is { } root)
            Check(!File.ReadAllText(Path.Combine(root, "Program.cs")).Contains("AllowSetForegroundWindow(-1)"), "S-22: Program.cs non dà più il primo piano a qualunque processo (AllowSetForegroundWindow(-1))");
    }

    /// <summary>La cartella dei sorgenti, se l'eseguibile gira dalla cartella di compilazione; altrimenti null (i controlli sui sorgenti si saltano).</summary>
    static string? SourceRoot()
    {
        var source = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && source != null && !File.Exists(Path.Combine(source, "Missiva.csproj")); i++) source = Path.GetDirectoryName(source.TrimEnd('\\'));
        return source != null && File.Exists(Path.Combine(source, "Missiva.csproj")) ? source : null;
    }

    /// <summary>
    /// S-21: un'azione "reply" con un testo (da una notifica di prima, o da un processo che attiva il server COM delle
    /// notifiche) non invia più nulla senza finestra: il testo va in una scrittura. Nessun test crea la finestra principale,
    /// quindi qui: il metodo che inviava non c'è più, e il ramo "reply con testo" di NotificationActionAsync apre la scrittura
    /// e non chiama nessun invio (lettura del sorgente).
    /// </summary>
    static void NotificationReplyAuditTests()
    {
        var sender = typeof(UI.MainWindow).GetMethod("ReplyFromNotificationAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Check(sender == null, "S-21: MainWindow non ha più il metodo che inviava la risposta di una notifica senza finestra (ReplyFromNotificationAsync)");
        if (SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: il ramo reply di NotificationActionAsync non è stato letto)");
            return;
        }
        var source = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.Notifications.cs"));
        int start = source.IndexOf("case \"reply\" when", StringComparison.Ordinal), end = start < 0 ? -1 : source.IndexOf("case \"reply\":", start, StringComparison.Ordinal);
        var branch = start >= 0 && end > start ? source[start..end] : "";
        Check(branch.Contains("OpenQuickReplyWindow(account, message, text") && !branch.Contains("SendAsync") && !source.Contains(".SendAsync("),
              "S-21: il ramo \"reply\" con un testo apre la scrittura con quel testo (OpenQuickReplyWindow) e in tutto MainWindow.Notifications.cs non c'è nessuna chiamata a SendAsync");
    }

    /// <summary>
    /// S-19: un file di configurazione con un DOCTYPE (entità che si espandono) non si legge, e la configurazione che arriva
    /// dal server di posta (MX) verso server di un altro dominio si segna da confermare, come gli SRV.
    /// </summary>
    static async Task AutoconfigAuditAsync()
    {
        static string Xml(string imapHost, string smtpHost, string doctype = "") => $"""
            <?xml version="1.0" encoding="UTF-8"?>{doctype}
            <clientConfig version="1.1"><emailProvider id="prova"><domain>prova</domain>
              <incomingServer type="imap"><hostname>{imapHost}</hostname><port>993</port><socketType>SSL</socketType><username>%EMAILADDRESS%</username><authentication>password-cleartext</authentication></incomingServer>
              <outgoingServer type="smtp"><hostname>{smtpHost}</hostname><port>465</port><socketType>SSL</socketType><username>%EMAILADDRESS%</username><authentication>password-cleartext</authentication></outgoingServer>
            </emailProvider></clientConfig>
            """;
        var pages = new Dictionary<string, string>
        {
            // Le entità interne si espandono: nel nome del server (o, peggio, fino a milioni di caratteri).
            ["https://autoconfig.entita-prova.it/mail/config-v1.1.xml"] = Xml("&h;", "&h;", "<!DOCTYPE clientConfig [<!ENTITY h \"imap.entita-prova.it\">]>"),
            ["https://autoconfig.normale-prova.it/mail/config-v1.1.xml"] = Xml("imap.normale-prova.it", "smtp.normale-prova.it"),
            ["https://autoconfig.thunderbird.net/v1.1/hosting-prova.net"] = Xml("imap.hosting-prova.net", "smtp.hosting-prova.net"),
            ["https://autoconfig.thunderbird.net/v1.1/ospite-prova.net"] = Xml("imap.proprio-prova.it", "smtp.proprio-prova.it"),
        };
        MailAutoconfig.UseTestHandler(new FakeHttp(request =>
            pages.TryGetValue(request.RequestUri!.GetLeftPart(UriPartial.Path), out var xml)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") }
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        MailDns.TestAnswers = (name, type) => (name, type) switch
        {
            ("cliente-prova.it", "MX") => [new("mx01.hosting-prova.net", 0, 10)], // un dominio proprio ospitato da un provider del database
            ("proprio-prova.it", "MX") => [new("mx.ospite-prova.net", 0, 10)], // il database dà i server del dominio stesso
            _ => [],
        };
        MailAutoconfig.TestProbe = (_, _, _, _) => Task.FromResult(false);
        try
        {
            var entity = await MailAutoconfig.FindAsync("mario@entita-prova.it", CancellationToken.None);
            var normal = await MailAutoconfig.FindAsync("mario@normale-prova.it", CancellationToken.None);
            Check(entity == null && normal?.Server is { ImapHost: "imap.normale-prova.it", SmtpHost: "smtp.normale-prova.it" } && !normal.Foreign,
                  $"S-19: una configurazione con un DOCTYPE e entità non si legge (trovata: {entity?.Server.ImapHost ?? "nessuna"}); una senza DOCTYPE sì");
            var byMx = await MailAutoconfig.FindAsync("mario@cliente-prova.it", CancellationToken.None);
            Check(byMx is { Foreign: true, Server.ImapHost: "imap.hosting-prova.net", Server.SmtpHost: "smtp.hosting-prova.net" } && byMx.Source.Contains("dal server di posta"),
                  $"S-19: i server presi dal database attraverso il server di posta (MX), di un altro dominio, sono da confermare ({byMx?.Server.ImapHost}, Foreign {byMx?.Foreign})");
            var own = await MailAutoconfig.FindAsync("anna@proprio-prova.it", CancellationToken.None);
            Check(own is { Foreign: false, Server.ImapHost: "imap.proprio-prova.it" },
                  $"S-19: se il database dà server del dominio dell'indirizzo, anche passando dall'MX, nessuna conferma ({own?.Server.ImapHost}, Foreign {own?.Foreign})");
        }
        finally
        {
            MailDns.TestAnswers = null;
            MailAutoconfig.TestProbe = null;
        }
    }

    /// <summary>
    /// S-09: un solo nome per allegato, pulito alla nascita: scheda, pastiglia, domande, IsDangerous e salvataggio dicono la
    /// stessa cosa, e un cambio di direzione del testo non gira il nome. Anche per le copie locali scritte prima.
    /// </summary>
    static void AttachmentNameAuditTests()
    {
        // "Prima": il nome grezzo di scheda e pastiglia contava per IsDangerous in un modo, quello pulito di Apri in un altro.
        string[] raw = ["Relazione‮fdp.exe", "virus.ex?e", "Fattura​.exe​", "Offerta⁦.pdf⁩.scr", "doc.pdf؜", "a.vbs:", "CON.txt", "  nota.js  ", "Relazione.exe‮.pdf"];
        var before = raw.Where(r => Safety.IsDangerous(r) != Safety.IsDangerous(Safety.SafeFileName(r))).ToList();
        var names = raw.Select(r => new Attachment { Id = "1", Name = r }.Name).ToList();
        var invisible = names.Where(n => n.EnumerateRunes().Any(r => SenderCheck.Unseen(r, shown: true))).ToList();
        var rule = names.Zip(raw, (n, r) => Safety.IsDangerous(n) == Safety.IsDangerous(Safety.SafeFileName(n)) && Safety.SafeFileName(n) == n && Safety.SafeFileName(r) == Safety.SafeFileName(n)).All(x => x);
        Check(before.Count > 0 && invisible.Count == 0 && rule,
              $"S-09: prima scheda e Apri decidevano su due nomi diversi per {before.Count} nomi su {raw.Length} (per esempio {string.Join(" | ", before.Take(3).Select(Escape))}); ora il nome è uno, senza caratteri invisibili, e salvato è identico ({string.Join(" | ", names.Select(Escape))})");
        Check(names[0] == "Relazionefdp.exe" && Safety.IsDangerous(names[0]) && names[1] == "virus.exe" && Safety.IsDangerous(names[1]) && names[2] == "Fattura.exe" && Safety.IsDangerous(names[2])
              && names[3] == "Offerta.pdf.scr" && Safety.IsDangerous(names[3]) && names[5] == "a.vbs" && names[6] == "_CON.txt" && names[7] == "nota.js" && Safety.IsDangerous(names[7])
              && Safety.SafeFileName("Fattura‮fdp") == "Fatturafdp" && Safety.SafeFileName("‮​") == "allegato",
              "S-09: i travestimenti con U+202E, U+200B, U+2066, caratteri vietati e due punti finali diventano il nome vero, e restano segnati come pericolosi; anche SafeFileName (per i nomi proposti da Salva con nome) toglie i caratteri invisibili");
        string[] fine = ["fattura.pdf", "Relazione città 2026.docx", "ß ä 日本語.txt", "foto (2).jpg", ".gitignore", "a.b.c.zip", "Verbale 12-10-2026 (Rossi).pdf"];
        var tooLong = Safety.CleanAttachmentName(new string('x', 400) + ".pdf");
        Check(fine.All(n => Safety.CleanAttachmentName(n) == n) && ReferenceEquals(Safety.CleanAttachmentName("fattura.pdf"), "fattura.pdf")
              && Safety.CleanAttachmentName(null) == "" && new Attachment { Id = "1", Name = "" }.Name == "" && tooLong.Length == 150 && tooLong.EndsWith(".pdf"),
              "S-09: i nomi normali (accenti, giapponese, parentesi, punto iniziale) restano com'erano; vuoto resta vuoto; oltre 150 caratteri si tiene la fine, con l'estensione");

        // Le righe dell'elenco: i nomi della pastiglia sono gli stessi dell'allegato.
        var summary = new MessageSummary { Id = "1", FolderId = "INBOX", Subject = "Prova", AttachmentNames = ["Relazione‮fdp.exe", "fattura.pdf"] };
        Check(summary.AttachmentNames is ["Relazionefdp.exe", "fattura.pdf"] && new MessageSummary { Id = "2", FolderId = "INBOX", Subject = "Prova", AttachmentNames = null! }.AttachmentNames.Count == 0,
              "S-09: i nomi delle pastiglie dell'elenco (MessageSummary.AttachmentNames) escono dallo stesso calcolo");

        // Le copie locali scritte prima, con il nome grezzo: si puliscono rileggendole.
        var oldList = System.Text.Json.JsonSerializer.Deserialize("[{\"Id\":\"1\",\"FolderId\":\"INBOX\",\"Subject\":\"Prova\",\"AttachmentNames\":[\"Offerta\\u202Efdp.exe\",\"nota.txt\"]}]", typeof(List<MessageSummary>), CacheJson.Default) as List<MessageSummary>;
        var oldBody = System.Text.Json.JsonSerializer.Deserialize("{\"Html\":\"<p>x</p>\",\"Attachments\":[{\"Id\":\"a\",\"Name\":\"Offerta\\u202Efdp.exe\",\"ContentType\":\"application/octet-stream\"}]}", SavedBodyJson.Default.SavedBody);
        Check(oldList?[0].AttachmentNames is ["Offertafdp.exe", "nota.txt"] && oldBody?.Attachments[0].Name == "Offertafdp.exe",
              "S-09: l'elenco e i corpi della copia locale scritti prima, con il cambio di direzione dentro il nome, si rileggono puliti");

        static string Escape(string s) => string.Concat(s.Select(c => c < ' ' || c >= 0x7F && char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Control ? $"\\u{(int)c:X4}" : c.ToString()));
    }

    /// <summary>
    /// S-10: il messaggio salvato come .eml riceve il marchio web (flusso Zone.Identifier), e un marchio che non si scrive
    /// si sa (prima l'errore era ingoiato); per i documenti di Office e i tipi pericolosi conta, per gli altri no.
    /// </summary>
    static void WebMarkAuditTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "missiva-audit-x-marchio-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            var plain = Path.Combine(dir, "senza-marchio.eml");
            File.WriteAllBytes(plain, "From: mario@example.com\r\n\r\nciao"u8.ToArray());
            var unmarked = !HasStream(plain);
            var eml = Path.Combine(dir, "Messaggio.eml");
            var content = "From: mario@example.com\r\nSubject: Prova\r\n\r\nciao"u8.ToArray();
            var marked = Safety.SaveDownloadedAsync(eml, content).GetAwaiter().GetResult();
            Check(unmarked && marked && HasStream(eml) && File.ReadAllText(eml + ":Zone.Identifier").Contains("ZoneId=3") && File.ReadAllBytes(eml).SequenceEqual(content),
                  $"S-10: un .eml scritto come prima non ha il flusso Zone.Identifier ({unmarked}); salvato con SaveDownloadedAsync sì, con ZoneId=3 e lo stesso contenuto");
            var impossible = Safety.MarkFromInternet(Path.Combine(dir, "non-esiste", "Fattura.docx"));
            Check(!impossible, "S-10: un marchio che non si può scrivere (cartella che non c'è) lo dice, non lo ingoia");
            string[] needed = ["Relazione.docx", "Conti.XLSM", "Slide.pptx", "Lettera.rtf", "setup.exe", "Appunti.one", "Relazione.docx. "];
            string[] notNeeded = ["fattura.pdf", "foto.jpg", "nota.txt", "dati.csv", "Messaggio.eml"];
            Check(needed.All(Safety.NeedsWebMark) && !notNeeded.Any(Safety.NeedsWebMark) && UI.Dialog.ConfirmOpenUnmarked(null, "fattura.pdf") && UI.Dialog.ConfirmOpenUnmarked(null, "foto.jpg"),
                  "S-10: senza marchio contano i documenti di Office e i tipi pericolosi (domanda prima di aprire); PDF, immagini, testi e .eml si aprono come prima, senza domanda");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        static bool HasStream(string path)
        {
            try { return File.ReadAllText(path + ":Zone.Identifier").Length > 0; }
            catch { return false; }
        }
    }

    /// <summary>
    /// S-28: gli allegati aperti si cancellano dopo un'ora, non all'uscita; non quelli aperti in un programma né quelli
    /// modificati da poco. Su una cartella temporanea loro, non su %TEMP%\Missiva.
    /// </summary>
    static void TempCleanAuditTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "missiva-audit-x-temp-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(root);
            string Make(string name, TimeSpan openedAgo, TimeSpan writtenAgo)
            {
                var dir = Path.Combine(root, name);
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "Relazione Rossi.docx");
                File.WriteAllText(file, "prova");
                Safety.MarkFromInternet(file);
                File.SetLastWriteTime(file, DateTime.Now - writtenAgo);
                Directory.SetCreationTime(dir, DateTime.Now - openedAgo);
                return dir;
            }
            var old = Make("vecchio", TimeSpan.FromHours(2), TimeSpan.FromHours(2));
            var fresh = Make("recente", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            var edited = Make("modificato", TimeSpan.FromHours(2), TimeSpan.FromMinutes(10));
            var inUse = Make("in-uso", TimeSpan.FromHours(3), TimeSpan.FromHours(3));
            using (new FileStream(Path.Combine(inUse, "Relazione Rossi.docx"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Safety.CleanTemp(TimeSpan.FromHours(1), root);
                Check(!Directory.Exists(old) && Directory.Exists(fresh) && Directory.Exists(edited) && Directory.Exists(inUse),
                      $"S-28: dopo un'ora via solo l'allegato vecchio (con il suo marchio web); restano quello di cinque minuti fa ({Directory.Exists(fresh)}), quello modificato dieci minuti fa ({Directory.Exists(edited)}) e quello aperto in un programma ({Directory.Exists(inUse)})");
            }
            Safety.CleanTemp(TimeSpan.FromHours(1), root);
            Check(!Directory.Exists(inUse) && Directory.Exists(fresh) && Directory.Exists(edited), "S-28: chiuso il programma, l'allegato che era in uso se ne va al giro dopo");
            Safety.CleanTemp(TimeSpan.Zero, root);
            Check(Directory.GetDirectories(root).Length == 0, "S-28: all'uscita (zero) via tutto, anche i file toccati da poco");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>S-11: i pacchetti e i blocchi di OneNote (e i moduli di PowerShell) sono fra i tipi che chiedono conferma; i documenti con macro no.</summary>
    static void DangerousMoreAuditTests()
    {
        string[] added = ["Appunti.one", "Quaderno.onepkg", "Modulo.psm1", "APPUNTI.ONE", "Appunti.one. "];
        Check(added.All(Safety.IsDangerous), $"S-11: pericolosi anche .one, .onepkg e .psm1 ({string.Join(", ", added.Select(n => n.Trim()))})");
        string[] macros = ["Conti.xlsm", "Relazione.docm", "Slide.pptm", "fattura.pdf", "foto.jpg", "Nota.onetoc2", "uno.txt"];
        Check(!macros.Any(Safety.IsDangerous), $"S-11: i documenti con macro restano alla protezione del marchio web, non alla conferma ({string.Join(", ", macros)})");
    }

    /// <summary>
    /// Audit del 7 ottobre 2026, parte B, primo gruppo (B-01..B-10: scorrimento, animazioni, ingresso; esito in ESITO.md).
    /// Quello che si controlla senza una finestra: i gruppi dell'elenco al cambio di giorno. Il resto (tasti e clic veri
    /// durante lo scorrimento, la sospensione del hit test, il cursore della barra, il calendario, il cambio di tema) si
    /// misura nella versione di prova: strumenti\ingresso.ps1 con MISSIVA_INGRESSO=1 e i comandi di MISSIVA_KEYS. Con
    /// "Missiva.exe --prova-audit risultato.txt aperto" solo queste.
    /// </summary>
    static void RunningAuditTests()
    {
        Report.Add("--- Audit del 7 ottobre, parte B, primo gruppo: scorrimento, animazioni, ingresso");
        DayChangeAuditTests();
    }

    /// <summary>
    /// B-08: a mezzanotte, con Missiva aperta, i gruppi dell'elenco e le date delle righe si rifanno. Il giorno è finto
    /// (MessageItem.TestToday: mercoledì 11 marzo 2026, poi giovedì 12). Prima il gruppo restava quello dell'inserimento: i
    /// messaggi di ieri restavano sotto "Oggi", e uno arrivato dopo mezzanotte finiva con loro senza che i gruppi si rifacessero.
    /// </summary>
    static void DayChangeAuditTests()
    {
        var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Imap = new ImapServer() }));
        var day = new DateTime(2026, 3, 11);
        MessageSummary Mail(string id, DateTime date) => new()
        {
            Id = id, FolderId = "INBOX", Subject = "Pratica " + id, Date = date,
            From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" },
        };
        MessageSummary[] Week() =>
        [
            Mail("1", day.AddHours(18)), Mail("2", day.AddHours(9)), Mail("3", day.AddDays(-1).AddHours(21)),
            Mail("4", day.AddDays(-2).AddHours(10)), Mail("5", day.AddDays(-8).AddHours(10)),
        ];
        (UI.MessageRows Rows, System.Windows.Data.ListCollectionView View) List()
        {
            var rows = new UI.MessageRows();
            var view = (System.Windows.Data.ListCollectionView)System.Windows.Data.CollectionViewSource.GetDefaultView(rows.Rows);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(UI.MessageItem.Group)));
            rows.Append(Week(), account);
            view.Refresh();
            return (rows, view);
        }
        static string Shape(System.Windows.Data.ListCollectionView view) => string.Join(" | ", view.Groups.Cast<System.Windows.Data.CollectionViewGroup>()
            .Select(g => g.Name + ": " + string.Join(",", g.Items.Cast<UI.MessageItem>().Select(i => i.Summary.Id))));
        bool grouped = Store.Settings.GroupByConversation;
        try
        {
            Store.Settings.GroupByConversation = false;
            UI.MessageItem.TestToday = day;
            var (rows, view) = List();
            var (staleRows, staleView) = List();
            string before = Shape(view), dateBefore = rows.Rows[0].DateText;
            int dateChanges = 0;
            rows.Rows[0].PropertyChanged += (_, e) => { if (e.PropertyName == nameof(UI.MessageItem.DateText)) dateChanges++; };

            // Mezzanotte. Com'era: i gruppi fermi, e un messaggio arrivato dopo trova "Oggi" fra i gruppi mostrati, quindi
            // (MainWindow.PrependRows) i gruppi non si rifanno e finisce insieme a quelli di ieri.
            UI.MessageItem.TestToday = day.AddDays(1);
            var shown = staleView.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(g => g.Name as string).ToHashSet();
            var lateStale = staleRows.Prepend([Mail("6", day.AddDays(1).AddMinutes(20))], account);
            bool wouldRegroup = lateStale.Any(a => !shown.Contains(a.Group));
            string stale = Shape(staleView);

            // Ora: al cambio di giorno i gruppi si ricalcolano, e il messaggio arrivato dopo sta da solo sotto "Oggi".
            bool changed = rows.NewDay();
            view.Refresh();
            string after = Shape(view), dateAfter = rows.Rows[0].DateText;
            int dateChangesAtMidnight = dateChanges;
            rows.Prepend([Mail("6", day.AddDays(1).AddMinutes(20))], account);
            view.Refresh();
            string withLate = Shape(view);
            bool again = rows.NewDay();
            Check(before == "Oggi: 1,2 | Ieri: 3 | Questa settimana: 4 | Settimana scorsa: 5"
                  && stale == "Oggi: 6,1,2 | Ieri: 3 | Questa settimana: 4 | Settimana scorsa: 5" && !wouldRegroup
                  && changed && after == "Ieri: 1,2 | Questa settimana: 3,4 | Settimana scorsa: 5"
                  && withLate == "Oggi: 6 | Ieri: 1,2 | Questa settimana: 3,4 | Settimana scorsa: 5" && !again
                  && dateBefore == "18:00" && dateAfter == "mer 18:00" && dateChangesAtMidnight == 1,
                  $"B-08, mezzanotte con Missiva aperta: i gruppi si rifanno (prima del cambio: {before}; com'era dopo mezzanotte con un arrivo: {stale}; " +
                  $"ora: {withLate}), la data della riga passa da \"{dateBefore}\" a \"{dateAfter}\", e un secondo controllo nello stesso giorno non rifà nulla ({again})");
        }
        finally
        {
            UI.MessageItem.TestToday = null;
            Store.Settings.GroupByConversation = grouped;
        }
    }

    /// <summary>
    /// Audit del 7 ottobre 2026, parte A: l'avvio (docs\audit\avvio-e-velocita.md, esito in ESITO.md). Quello che dell'avvio si
    /// controlla senza una finestra: le impostazioni lette da un altro thread, la sessione di Windows per il nome del canale,
    /// il messaggio finto del riscaldamento. Con "Missiva.exe --prova-audit risultato.txt avvio" solo queste.
    /// </summary>
    static void StartupAuditTests()
    {
        Report.Add("--- Audit del 7 ottobre, parte A: avvio");
        StartupPathAuditTests();
        LateNetworkAuditAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// La rete in ritardo all'accesso a Windows (A-13..A-19): l'IDLE che si sveglia quando la rete torna e dice quando è
    /// collegato, le cartelle rilette anche dopo un errore che non è "non in linea", i tentativi che si ripetono, i tempi
    /// scaduti del token di Google che non si mettono in fila. Server IMAP finto e gestori finti, nessuna rete vera.
    /// </summary>
    static async Task LateNetworkAuditAsync()
    {
        // A-16: un IDLE caduto aspetta la sua attesa (qui 30 s); RetryNow lo fa riprovare subito.
        var (renew, retry) = (InboxIdle.Renew, InboxIdle.FirstRetry);
        InboxIdle.FirstRetry = TimeSpan.FromSeconds(30);
        try
        {
            const string who = "mario.rossi@studio-prova.example";
            int calls = 0;
            bool up = false;
            using var stop = new CancellationTokenSource();
            var running = InboxIdle.RunAsync(who, async token =>
            {
                Interlocked.Increment(ref calls);
                if (!Volatile.Read(ref up)) throw new System.Net.Sockets.SocketException(10051);
                await Task.Delay(Timeout.Infinite, token);
                return true;
            }, stop.Token);
            bool failed = await Until(() => calls == 1 && !InboxIdle.Watching(who), TimeSpan.FromSeconds(2));
            await Task.Delay(400);
            bool waiting = calls == 1; // senza segnale aspetta i suoi 30 s
            Volatile.Write(ref up, true);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            InboxIdle.RetryNow();
            bool again = await Until(() => calls == 2 && InboxIdle.Watching(who), TimeSpan.FromSeconds(2));
            long took = clock.ElapsedMilliseconds;
            stop.Cancel();
            bool ended = await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(3))) == running;
            Check(failed && waiting && again && ended && !InboxIdle.Watching(who),
                  $"A-16: l'IDLE caduto senza rete aspettava la sua attesa (30 s nella prova, fino a 5 minuti nell'uso); quando la rete torna (RetryNow) riprova subito, in {took} ms, ed è di nuovo in sessione");
        }
        finally
        {
            (InboxIdle.Renew, InboxIdle.FirstRetry) = (renew, retry);
        }

        // A-15: una sessione IDLE collegata lo dice (Connected), prima di ogni altro avviso: da lì "Non in linea" si toglie
        // senza aspettare il controllo dei nuovi messaggi.
        ImapProvider.TestPlain = true;
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            fake.Add("INBOX", SpeedMail("Pratica di prova", "Testo della pratica"));
            using var imap = new ImapProvider(new MailAccount
            {
                Kind = AccountKind.Imap, Email = "mario.rossi@studio-prova.example",
                Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = fake.Port, SmtpHost = "127.0.0.1", SmtpPort = 1, UserName = "mario.rossi@studio-prova.example", ProtectedPassword = Store.Protect("x") },
            });
            var events = new System.Collections.Concurrent.ConcurrentQueue<InboxNotice>();
            using var stop = new CancellationTokenSource();
            var watching = imap.WatchInboxAsync(e => events.Enqueue(e), stop.Token);
            bool idling = await Until(() => fake.Idling == 1, TimeSpan.FromSeconds(5));
            Check(idling && events.TryPeek(out var first) && first.Kind == InboxEvent.Connected && events.Count == 1,
                  $"A-15: la sessione IDLE collegata lo dice una volta (Connected), senza altri avvisi ({string.Join(", ", events.Select(e => e.Kind))})");
            stop.Cancel();
            await Task.WhenAny(watching, Task.Delay(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }

        // A-15: quando si rileggono le cartelle dopo una richiesta riuscita, e quali errori sono "non in linea".
        var reset = new System.Net.Sockets.SocketException(10054);
        var refused = new IOException("collegamento", new System.Net.Sockets.SocketException(10061));
        var firewall = new System.Net.Sockets.SocketException(10013);
        Check(UI.MainWindow.ReloadsFolders("Non in linea: controlla la connessione a Internet.", false)
              && UI.MainWindow.ReloadsFolders("Non raggiungibile: Connessione in corso interrotta forzatamente dall'host remoto.", true)
              && UI.MainWindow.ReloadsFolders("", true) && !UI.MainWindow.ReloadsFolders("", false) && !UI.MainWindow.ReloadsFolders("Caricamento…", false)
              && !Network.IsOffline(reset) && Network.Explain(reset) == null && !Network.IsOffline(refused) && !Network.IsOffline(new TimeoutException())
              && Network.IsOffline(firewall) && Network.Explain(firewall) != null,
              "A-15: dopo una richiesta riuscita le cartelle si rileggono se l'account era \"Non in linea\" o se la loro lettura non era riuscita per un altro errore " +
              "(connessione chiusa 10054, rifiutata 10061, tempo scaduto: \"Non raggiungibile\", che prima restava fino al riavvio); non se era tutto a posto");

        // A-17 e A-18: i tentativi che si ripetono.
        var noon = new DateTime(2026, 10, 7, 12, 0, 0);
        var signIn = new MailException("mario.rossi@studio-prova.example chiede un nuovo accesso: premi Accedi di nuovo, sotto l'account.") { Fix = AccountFix.SignIn };
        Check(UI.MainWindow.NextCalendarFetch(noon, failed: false, offline: false) == noon.AddMinutes(30)
              && UI.MainWindow.NextCalendarFetch(noon, failed: true, offline: true) == noon.AddMinutes(1)
              && UI.MainWindow.NextCalendarFetch(noon, failed: true, offline: false) == noon.AddMinutes(5),
              "A-17: i promemoria del calendario rileggono gli eventi ogni mezz'ora; dopo una lettura non riuscita fra un minuto senza rete e fra cinque per un altro errore (prima sempre mezz'ora)");
        Check(UI.MainWindow.SnoozeWait(firewall) == "non in linea" && UI.MainWindow.SnoozeWait(new IOException("x", new System.Net.Sockets.SocketException(11001))) == "non in linea"
              && UI.MainWindow.SnoozeWait(signIn) == signIn.Message && UI.MainWindow.SnoozeWait(new InvalidOperationException("altro")) == null,
              "A-17: un ritorno da Posponi che non riesce per la rete o per un accesso da rifare aspetta con una riga sola nel registro; gli altri errori restano per intero");
        var afterError = UI.MainWindow.DoneAfterError(noon);
        Check(noon.AddMinutes(4) - afterError < TimeSpan.FromMinutes(30) && noon.AddMinutes(5) - afterError >= TimeSpan.FromMinutes(30),
              "A-18: dopo un giro non riuscito la copia degli ultimi 30 giorni di quell'account riparte fra 5 minuti, non fra 30");

        // A-19: quattro connessioni chiedono insieme il token di Google con una rete che non risponde: una richiesta sola, e le
        // altre ricevono lo stesso errore subito; passata la memoria dell'errore si riprova.
        var (id, secret, token, memory) = (Store.Settings.GoogleClientId, Store.Settings.ProtectedGoogleClientSecret, GoogleAuth.TestToken, GoogleAuth.FailureMemory);
        var silent = new SilentNetwork(TimeSpan.FromMilliseconds(500));
        try
        {
            GoogleAuth.TestToken = null;
            GoogleAuth.TestHttp = new HttpClient(silent);
            GoogleAuth.FailureMemory = TimeSpan.FromMilliseconds(1500);
            Store.Settings.GoogleClientId = "prova.apps.googleusercontent.example";
            Store.Settings.ProtectedGoogleClientSecret = Store.Protect("segreto di prova");
            var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "mario.prova@gmail.example", ProtectedRefreshToken = Store.Protect("aggiornamento finto") };
            async Task<Exception?> Ask()
            {
                try { await GoogleAuth.TokenAsync(gmail); return null; }
                catch (Exception ex) { return ex; }
            }
            // Com'era prima (senza memoria dell'errore): una richiesta dopo l'altra, ognuna con la sua attesa.
            GoogleAuth.FailureMemory = TimeSpan.Zero;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(Ask)));
            (long before, int requestsBefore) = (clock.ElapsedMilliseconds, silent.Requests);
            GoogleAuth.FailureMemory = TimeSpan.FromMilliseconds(1500);
            GoogleAuth.TestForgetFailure();
            clock.Restart();
            var errors = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(Ask)));
            long all = clock.ElapsedMilliseconds;
            int requests = silent.Requests - requestsBefore;
            await Task.Delay(1700);
            var later = await Ask();
            Check(errors.All(e => e != null && Network.IsOffline(e)) && requestsBefore == 4 && before >= 1900 && requests == 1 && all < 1500 && later != null && silent.Requests == requestsBefore + 2,
                  $"A-19: quattro richieste insieme del token di Google con la rete che non risponde (0,5 s nella prova, 30 s nell'uso): {requests} sola richiesta e tutte e quattro con l'errore di rete in {all} ms; " +
                  $"senza la memoria dell'errore, com'era, {requestsBefore} richieste una dopo l'altra in {before} ms. Passata la memoria si riprova");
        }
        finally
        {
            GoogleAuth.TestHttp = null;
            GoogleAuth.TestForgetFailure();
            (Store.Settings.GoogleClientId, Store.Settings.ProtectedGoogleClientSecret, GoogleAuth.TestToken, GoogleAuth.FailureMemory) = (id, secret, token, memory);
        }
    }

    /// <summary>Una rete che non risponde: ogni richiesta aspetta e poi fallisce come un collegamento scaduto (10060).</summary>
    sealed class SilentNetwork(TimeSpan wait) : HttpMessageHandler
    {
        int requests;
        public int Requests => requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Interlocked.Increment(ref requests);
            await Task.Delay(wait, cancel);
            throw new HttpRequestException("Il server non risponde.", new System.Net.Sockets.SocketException(10060));
        }
    }

    static void StartupPathAuditTests()
    {
        // A-01: le impostazioni lette su un thread del pool (App.LoadSettingsEarly) sono quelle lette sul thread che le usa,
        // testi cifrati compresi.
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-avvio-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            var sample = new Settings
            {
                Theme = AppTheme.Dark,
                Accounts = [new MailAccount { Id = "prova1", Kind = AccountKind.Imap, Email = "mario.rossi@studio-prova.example", Imap = new ImapServer { ImapHost = "imap.studio-prova.example", ProtectedPassword = "x" } }],
                Templates = [new MailTemplate { Name = "Preventivo di prova", Subject = "Preventivo per Maria Rossi", Rtf = new string('r', 150_000) }],
            };
            static string Json(Settings s) => System.Text.Json.JsonSerializer.Serialize(s, SettingsJson.Default.Settings);
            bool wrote = Store.TryWrite(dir, sample, null, out _);
            int uiThread = Environment.CurrentManagedThreadId, otherThread = 0;
            var here = Store.Read(dir, out var hereRecovery);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var there = Task.Run(() =>
            {
                otherThread = Environment.CurrentManagedThreadId;
                return Store.Read(dir, out _);
            }).GetAwaiter().GetResult();
            Check(wrote && hereRecovery == null && otherThread != uiThread && Json(here) == Json(there) && there.Theme == AppTheme.Dark
                  && there.Templates is [{ Subject: "Preventivo per Maria Rossi", Rtf.Length: 150_000 }] && there.Accounts is [{ Email: "mario.rossi@studio-prova.example" }],
                  $"A-01: le impostazioni lette da un altro thread (come all'avvio, mentre WPF costruisce l'applicazione) sono le stesse, con il tema e un modello cifrato da 150 KB (letto in {clock.ElapsedMilliseconds} ms)");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        // A-05: la sessione di Windows per il nome del canale è quella che diceva Process.SessionId (le copie di prima e di
        // dopo l'aggiornamento devono trovarsi sullo stesso canale).
        using (var self = System.Diagnostics.Process.GetCurrentProcess())
            Check(Program.WindowsSession() == self.SessionId, $"A-05: la sessione di Windows senza elencare i processi ({Program.WindowsSession()}) è quella di Process.SessionId ({self.SessionId})");

        // A-22: il messaggio finto del riscaldamento passa da SpacerName (immagine esterna bloccata con il nome di uno
        // spaziatore) e da CssEscapedFunction (stile con una barra rovesciata), e non lascia indirizzi da chiedere.
        var warm = MailHtml.Prepare(MailHtml.WarmUpSample, true, false, _ => null);
        Check(warm.BlockedImages == 1 && warm.Fragment.Contains("missiva-bloccato(") && !warm.Fragment.Contains("missiva.invalid") && !warm.PlainOnly,
              $"A-22: il riscaldamento della pulizia costruisce anche le regex degli spaziatori e delle funzioni CSS scritte con la barra rovesciata ({warm.BlockedImages} immagine bloccata, nessun indirizzo esterno rimasto)");
    }

    /// <summary>
    /// Audit del 7 ottobre 2026, parte B, secondo gruppo (B-11..B-34: dati, rete, memoria; esito in ESITO.md). Con
    /// "Missiva.exe --prova-audit risultato.txt dati" solo queste.
    /// </summary>
    static void DataAuditTests()
    {
        Report.Add("--- Audit del 7 ottobre, parte B, secondo gruppo: dati, rete, memoria");
        QueueReplayAuditAsync().GetAwaiter().GetResult();
        DraftBreaksAuditTests();
        BigDraftMeasure();
        DataWritesMeasure();
        AddressBookSaveAuditTests();
        CleanMeasure();
        SuggestIndexMeasure();
        UnreadRecountAuditAsync().GetAwaiter().GetResult();
        OnUiThread(PreferenceSavesAuditAsync);
        LogAddressesAuditTests();
        LongRunAuditTests();
        SummaryCacheMeasureAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// B-26: i contenitori che crescevano con Missiva aperta per giorni. I messaggi già annunciati (RecentKeys, gli ultimi
    /// 2000) e gli eventi tenuti per i promemoria (MainWindow.Upcoming): trenta giorni di letture ogni mezz'ora con dieci
    /// eventi al giorno, e un promemoria non ancora arrivato che non si perde.
    /// </summary>
    static void LongRunAuditTests()
    {
        var notified = new UI.RecentKeys(2000);
        for (int i = 0; i < 10_000; i++) notified.Add("account|messaggio-" + i);
        notified.Add("account|messaggio-9999"); // lo stesso due volte: una voce sola
        bool recent = notified.Contains("account|messaggio-9999") && notified.Contains("account|messaggio-8000") && !notified.Contains("account|messaggio-7999");

        static CalendarEvent Event(DateTime start, int n) => new() { Id = $"evento-{start:yyyyMMdd}-{n}", Subject = "Riunione di prova", Start = start, End = start.AddHours(1), ReminderOn = true, ReminderMinutes = 15 };
        // Trenta giorni: dieci eventi di giorno e due di sera; il PC lavora dalle 7 alle 18 (letture ogni mezz'ora, come
        // CheckRemindersAsync: prima la lettura, poi i promemoria scaduti) e poi si ferma fino al mattino. I promemoria della
        // sera non sono ancora passati quando si ferma: al risveglio devono esserci ancora (si mostrano in ritardo), e poi uscire.
        var first = new DateTime(2026, 3, 2);
        List<CalendarEvent> Day(DateTime day) => Enumerable.Range(0, 10).Select(n => Event(day.AddHours(8 + n), n)).Append(Event(day.AddHours(19), 10)).Append(Event(day.AddHours(20), 11)).ToList();
        List<CalendarEvent> kept = new(), keptOld = new();
        int most = 0, mostOld = 0, late = 0;
        var remindedUpTo = first.AddHours(7);
        for (int d = 0; d < 30; d++)
            for (var now = first.AddDays(d).AddHours(7); now <= first.AddDays(d).AddHours(18); now = now.AddMinutes(30))
            {
                var today = now.Date;
                var data = new CalendarData { Events = Day(today).Concat(Day(today.AddDays(1))).ToList(), From = today, To = today.AddDays(2) };
                kept = UI.MainWindow.Upcoming(kept, data, now, remindedUpTo);
                // Com'era: tutto quello che è fuori dall'intervallo appena letto resta.
                keptOld = keptOld.Where(e => e.Start < data.From || e.Start >= data.To)
                    .Concat(data.Events.Where(e => e.ReminderAt is { } at && at > now.AddMinutes(-1) && at < now.AddDays(2))).ToList();
                late += kept.Count(e => e.ReminderAt is { } at && at > remindedUpTo && at <= now && e.Start.Date < today); // i promemoria di ieri sera, mostrati al risveglio
                remindedUpTo = now;
                most = Math.Max(most, kept.Count);
                mostOld = Math.Max(mostOld, keptOld.Count);
            }
        Check(notified.Count == 2000 && recent && most <= 30 && mostOld >= most + 50 && late == 29 * 2,
              $"B-26: con Missiva aperta per giorni i messaggi già annunciati restano gli ultimi {notified.Count} (prima uno per messaggio arrivato, senza tetto: qui 10.000), " +
              $"e gli eventi tenuti per i promemoria dopo trenta giorni sono al più {most} (prima {mostOld}: restavano per sempre quelli il cui promemoria non era ancora passato quando il PC si è fermato, " +
              $"due a sera); i {late} promemoria della sera si mostrano ancora al risveglio");
    }

    /// <summary>
    /// B-23, sola misura: quanto pesa in memoria una voce di SummaryCache (busta e struttura di MailKit di un messaggio
    /// elencato), sul server IMAP finto con messaggi con testo, HTML e un allegato: la differenza di memoria gestita fra
    /// 50 e 300 messaggi elencati, divisa per 250.
    /// </summary>
    static async Task SummaryCacheMeasureAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        try
        {
            for (int i = 0; i < 300; i++)
            {
                var body = new MimeKit.BodyBuilder { TextBody = "Gentile cliente, in allegato la fattura.", HtmlBody = "<p>Gentile cliente, in allegato la <b>fattura</b>.</p>" };
                body.Attachments.Add($"fattura-{i}.pdf", new byte[2000]);
                var mime = new MimeKit.MimeMessage { Subject = "Fattura di prova numero " + i, Date = DateTimeOffset.Now.AddMinutes(-i), Body = body.ToMessageBody() };
                mime.From.Add(new MimeKit.MailboxAddress("Maria Rossi", $"maria.rossi{i % 40}@fornitori-prova.example"));
                mime.To.Add(new MimeKit.MailboxAddress("Mario Rossi", "mario.rossi@studio-prova.example"));
                mime.Cc.Add(new MimeKit.MailboxAddress("Studio di prova", "studio@studio-prova.example"));
                fake.Add("INBOX", mime);
            }
            using var imap = new ImapProvider(SpeedAccount(fake, "mario.rossi@studio-prova.example"));
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var page = await imap.GetMessagesAsync(inbox, null, null);
            int listed = page.Messages.Count;
            long after50 = GC.GetTotalMemory(true);
            while (page.Next != null && listed < 300)
            {
                page = await imap.GetMessagesAsync(inbox, null, page.Next);
                listed += page.Messages.Count;
            }
            page = null!;
            long after300 = GC.GetTotalMemory(true);
            double each = (after300 - after50) / (double)Math.Max(1, listed - 50);
            Report.Add($"      misura B-23: {listed} messaggi elencati su un account IMAP: {each / 1024:0.0} KB di memoria gestita a messaggio tenuto (busta e struttura di MailKit), " +
                       $"cioè circa {each * 2000 / 1024 / 1024:0.0} MB per le 2.000 voci di un account che le riempie tutte");
            GC.KeepAlive(imap);
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
    }

    /// <summary>
    /// B-22, misura con un controllo: gli indirizzi tolti dagli errori del registro della versione installata
    /// (Log.WithoutAddresses, una regex con un tempo massimo di 250 ms, oltre il quale la riga dell'errore va persa). Il
    /// rapporto temeva un tempo che cresce col quadrato su una sequenza lunga di caratteri ammessi: qui sei forme di testo
    /// da 80.000 caratteri, il tempo di ognuna, e nessuna riga persa.
    /// </summary>
    static void LogAddressesAuditTests()
    {
        Log.WithoutAddresses("riscaldamento di mario.rossi@studio-prova.example");
        var times = new List<string>();
        bool kept = true;
        double worst = 0;
        foreach (int n in new[] { 20_000, 80_000 })
        {
            var run = new string('a', n);
            var shapes = new[]
            {
                ("senza @", run), ("con @ e senza dominio", run + "@ fine dell'errore"),
                ("a pezzi di 2000 divisi da @", string.Join("@", Enumerable.Repeat(new string('a', 2000), n / 2000))),
                ("a pezzi di 20 divisi da @", string.Join("@", Enumerable.Repeat(new string('a', 20), n / 20))),
                ("con @ e un dominio senza punto", run + "@" + run + " fine"),
                ("di punti e lettere con @ in fondo", string.Concat(Enumerable.Repeat("a.", n / 2)) + "@"),
            };
            foreach (var (name, text) in shapes)
            {
                var clock = Stopwatch.StartNew();
                var result = Log.WithoutAddresses(text);
                double ms = clock.Elapsed.TotalMilliseconds;
                kept &= result == text;
                worst = Math.Max(worst, ms);
                if (n == 80_000) times.Add($"{name} {ms:0.0} ms");
            }
        }
        Check(kept && worst < 100 && Log.WithoutAddresses("550 <mario.rossi@studio-prova.example>: rifiutato") == "550 <(indirizzo)>: rifiutato",
              $"B-22: Log.WithoutAddresses su 80.000 caratteri ammessi di fila non cresce col quadrato e non perde la riga ({string.Join("; ", times)}; il tempo massimo della regex è 250 ms)");
    }

    /// <summary>
    /// B-19: le preferenze passate da Store.Save a Store.SaveSoon (Fissa, colore, ordine e cartelle nascoste, "Mostra
    /// immagini", le scelte di Visualizza, la vista e i calendari nascosti del calendario). Con un SettingsSaver suo in una
    /// cartella temporanea: quanto costa un salvataggio subito (quello che esce dal percorso del clic), e all'uscita normale
    /// (Flush, come OnExit, la fine della sessione e la chiusura nell'area di notifica) nessuna di quelle scelte si perde.
    /// </summary>
    static async Task PreferenceSavesAuditAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-preferenze-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var settings = SampleSettings(false);
            StoreRecovery? recovery = null;
            var saver = new SettingsSaver(dir, () => settings, () => recovery) { Ui = System.Windows.Threading.Dispatcher.CurrentDispatcher };
            saver.Now();
            long size = new FileInfo(Path.Combine(dir, Store.FileName)).Length;
            static double Median(List<double> times) => times.OrderBy(t => t).ElementAt(times.Count / 2);
            List<double> now = new(), take = new(), put = new();
            for (int i = 0; i < 9; i++)
            {
                settings.Zoom = 100 + i;
                var clock = Stopwatch.StartNew();
                saver.Now();
                now.Add(clock.Elapsed.TotalMilliseconds);
                settings.Zoom = 120 + i;
                clock.Restart();
                var (json, number, error) = saver.Take();
                take.Add(clock.Elapsed.TotalMilliseconds);
                clock.Restart();
                saver.Put(json, number, error, "prova");
                put.Add(clock.Elapsed.TotalMilliseconds);
            }

            // Le scelte passate a SaveSoon, poi subito l'uscita.
            int ui = Environment.CurrentManagedThreadId, writes = saver.Writes;
            var account = settings.Accounts[0];
            settings.SetPinned(account, "messaggio-fissato", true);
            settings.FolderColors[account.Id + "|INBOX"] = 3;
            settings.FolderOrder[account.Id] = ["Clienti", "INBOX"];
            settings.HiddenFolders.Add(account.Id + "|Vecchie");
            settings.AllowImagesFrom("maria.rossi@fornitori-prova.example");
            settings.GroupByConversation = !settings.GroupByConversation;
            settings.CalendarMode = "Month";
            settings.HiddenCalendars.Add(account.Id + "|festivi");
            bool grouped = settings.GroupByConversation;
            var clockSoon = Stopwatch.StartNew();
            saver.Soon();
            double soon = clockSoon.Elapsed.TotalMilliseconds;
            bool notYet = saver.Writes == writes;
            saver.Flush();
            bool onExitThread = saver.LastWriteThread == ui && saver.Writes == writes + 1;
            var disk = Store.Read(dir, out _);
            await Task.Delay(SettingsSaver.Delay + TimeSpan.FromMilliseconds(400)); // il timer rimasto non riscrive
            Check(notYet && onExitThread && saver.Writes == writes + 1
                  && disk.Pinned.Values.Any(p => p.Contains("messaggio-fissato")) && disk.FolderColors.GetValueOrDefault(account.Id + "|INBOX") == 3
                  && disk.FolderOrder.GetValueOrDefault(account.Id) is ["Clienti", "INBOX"] && disk.HiddenFolders.Contains(account.Id + "|Vecchie")
                  && disk.ImageSenders.Count == settings.ImageSenders.Count && disk.ImageSenders.Count > 0 && disk.GroupByConversation == grouped && disk.CalendarMode == "Month"
                  && disk.HiddenCalendars.Contains(account.Id + "|festivi"),
                  $"B-19: Fissa, colore, ordine e cartelle nascoste, \"Mostra immagini\", Visualizza e calendario salvati con SaveSoon ({soon:0.000} ms sul clic) e subito l'uscita: Flush li porta tutti sul disco, una scrittura sola " +
                  $"(impostazioni di {size / 1024} KB: un salvataggio subito costava {Median(now):0.0} ms sul clic, di cui {Median(take):0.0} l'istantanea e {Median(put):0.0} il file; mediane di 9)");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// B-18, sola misura: un messaggio arrivato da un mittente già in rubrica cambia solo un contatore, ma l'indice dei
    /// suggerimenti dei destinatari si rifà alla lettera dopo. Quanto costa quella lettera, con una rubrica come quella di
    /// oggi e con una dieci volte più grande (rubrica e contatti finti, in memoria).
    /// </summary>
    static void SuggestIndexMeasure()
    {
        var contactsBefore = AddressBook.Contacts;
        try
        {
            foreach (var (book, people) in new[] { (364, 1_000), (4_500, 4_500) })
            {
                var entries = Enumerable.Range(0, book).Select(i => new AddressBook.Entry
                {
                    Name = "Maria Rossi " + i, Email = $"maria.rossi{i}@clienti-prova.example", Received = 1 + i % 5, LastReceived = new DateTime(2026, 9, 1).AddMinutes(i),
                }).ToList();
                var contacts = Enumerable.Range(0, people).Select(i => new Contact { DisplayName = "Mario Rossi " + i, Company = "Studio di prova", Emails = [$"mario.rossi{i}@studio-prova.example"] }).ToList();
                AddressBook.Contacts = () => contacts;
                AddressBook.TestBook(entries);
                AddressBook.Suggest("maria", 8);
                double Letter()
                {
                    var clock = Stopwatch.StartNew();
                    AddressBook.Suggest("maria ro", 8);
                    return clock.Elapsed.TotalMilliseconds;
                }
                var warm = Enumerable.Range(0, 9).Select(_ => Letter()).OrderBy(t => t).ElementAt(4);
                var after = Enumerable.Range(0, 9).Select(k =>
                {
                    // Un arrivo da un mittente già noto: cambia solo Received e LastReceived.
                    AddressBook.Received([new MessageSummary { Id = k.ToString(), FolderId = "INBOX", From =new Address { Name = entries[k].Name, Email = entries[k].Email }, Date = DateTime.Now.AddSeconds(k) }]);
                    return Letter();
                }).OrderBy(t => t).ElementAt(4);
                Report.Add($"      misura B-18: rubrica di {book} voci e {people} contatti: una lettera nel campo A {warm:0.00} ms con l'indice pronto, {after:0.00} ms dopo un arrivo da un mittente già noto (l'indice si rifà); mediane di 9");
            }
        }
        finally
        {
            AddressBook.TestBook(null);
            AddressBook.Contacts = contactsBefore;
        }
    }

    /// <summary>
    /// B-17: un contrassegno o una categoria messi su un account IMAP non fanno ricontare i non letti (UnreadSync.Written,
    /// da cui App.RecountSoon: NOOP e SEARCH sulla connessione in sottofondo); un segno di letto sì, e l'eco all'IDLE vale
    /// per tutti e tre. Sul server IMAP finto.
    /// </summary>
    static async Task UnreadRecountAuditAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        int written = 0;
        void Count(string _) => Interlocked.Increment(ref written);
        UnreadSync.Written += Count;
        try
        {
            fake.Add("INBOX", "Fattura di prova");
            fake.Add("INBOX", "Preventivo di prova");
            using var imap = new ImapProvider(SpeedAccount(fake, "mario.rossi@studio-prova.example"));
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var page = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            fake.Commands.Clear();
            for (int k = 0; k < 10; k++) await imap.SetFlagAsync([page[0]], k % 2 == 0);
            int afterFlags = written;
            bool flagEcho = InboxIdle.OwnEcho(imap.Account.Id);
            bool ticketAfterFlags = UnreadSync.Ticket(imap.Account.Id) != null;
            await imap.SetCategoryAsync([page[1]], "Clienti", true);
            int afterCategory = written;
            await imap.SetReadAsync([page[0]], true);
            int afterRead = written;
            bool noTicket = UnreadSync.Ticket(imap.Account.Id) == null;
            int stores = fake.Commands.Count(c => c.StartsWith("UID STORE"));
            Check(afterFlags == 0 && afterCategory == 0 && afterRead == 1 && flagEcho && ticketAfterFlags && noTicket && stores == 12,
                  $"B-17: dieci contrassegni e una categoria non fanno ricontare i non letti (riconteggi chiesti: {afterCategory}, prima uno per scrittura: 11), un segno di letto sì ({afterRead - afterCategory}); " +
                  $"l'eco all'IDLE resta ignorato anche per i contrassegni, e un conteggio del server chiesto subito dopo un contrassegno vale ({stores} UID STORE)");
        }
        finally
        {
            UnreadSync.Written -= Count;
            ImapProvider.TestPlain = false;
        }
    }

    /// <summary>
    /// B-16, sola misura, e solo con MISSIVA_MISURA_CORPI=cartella dei dati di una copia di prova (prova-dati): quanto costa
    /// la pulizia dell'HTML (MailHtml.Prepare, quella che un clic su un messaggio già scaricato aspetta) sui corpi veri della
    /// copia su disco. Solo tempi e misure: nessun testo dei messaggi esce da qui.
    /// </summary>
    static void CleanMeasure()
    {
        if (Environment.GetEnvironmentVariable("MISSIVA_MISURA_CORPI") is not { Length: > 0 } root || !Directory.Exists(Path.Combine(root, "cache"))) return;
        var bodies = new System.Collections.Concurrent.ConcurrentBag<string>();
        var old = BodyCache.TestRoot;
        try
        {
            BodyCache.TestRoot = root;
            foreach (var dir in Directory.GetDirectories(Path.Combine(root, "cache")))
                BodyCache.Each(new MailAccount { Id = Path.GetFileName(dir) }, saved => bodies.Add(saved.Html));
        }
        finally { BodyCache.TestRoot = old; }
        var all = bodies.OrderBy(h => h.Length).ToList();
        if (all.Count == 0) return;
        foreach (var html in all.Take(30)) MailHtml.Prepare(html, false, false, _ => null); // riscaldamento
        foreach (bool dark in new[] { false, true })
        {
            var times = new List<double>();
            long cleanedChars = 0;
            foreach (var html in all)
            {
                var clock = Stopwatch.StartNew();
                MailHtml.Result result;
                try { result = MailHtml.Prepare(html, dark, false, _ => null); }
                catch { continue; }
                times.Add(clock.Elapsed.TotalMilliseconds);
                cleanedChars += result.Fragment.Length;
            }
            times.Sort();
            double At(double p) => times[(int)Math.Min(times.Count - 1, p * times.Count)];
            Report.Add($"      misura B-16: pulizia dell'HTML di {times.Count} corpi veri della copia di prova, tema {(dark ? "scuro" : "chiaro")}: mediana {At(0.5):0.0} ms, 75° {At(0.75):0.0}, 90° {At(0.9):0.0}, " +
                       $"99° {At(0.99):0.0}, massimo {times[^1]:0} ms; oltre 8 ms {times.Count(t => t > 8)}, oltre 16 ms {times.Count(t => t > 16)}; HTML {all.Sum(h => (long)h.Length) / 1024 / all.Count} KB in media, " +
                       $"pulito {cleanedChars / 1024 / times.Count} KB in media");
        }
    }

    /// <summary>
    /// B-14: la rubrica dei suggerimenti si scrive fuori dal lucchetto che l'interfaccia prende a ogni messaggio aperto e a
    /// ogni lettera. Una scrittura tenuta ferma sul disco (AddressBook.TestWriting): intanto LookAlike, una voce nuova e
    /// Suggest non aspettano, e il salvataggio dopo (quello dell'uscita) porta sul file anche la voce arrivata intanto.
    /// Sulla rubrica della cartella di prova, rimessa com'era.
    /// </summary>
    static void AddressBookSaveAuditTests()
    {
        var file = Path.Combine(Store.Dir, "rubrica.json");
        var before = File.Exists(file) ? File.ReadAllBytes(file) : null;
        string tag = Guid.NewGuid().ToString("N")[..8];
        var first = new Address { Name = "Maria Rossi " + tag, Email = $"maria.rossi.{tag}@clienti-prova.example" };
        var second = new Address { Name = "Mario Rossi", Email = $"mario.rossi.{tag}@clienti-prova.example" };
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            AddressBook.Sent([first], DateTime.Now);
            int writes = 0;
            AddressBook.TestWriting = () =>
            {
                if (Interlocked.Increment(ref writes) != 1) return;
                entered.Set();
                release.Wait(10_000);
            };
            var slow = Task.Run(AddressBook.Save);
            bool started = entered.Wait(5000);
            var clock = Stopwatch.StartNew();
            var ui = Task.Run(() =>
            {
                var alike = AddressBook.LookAlike(new Address { Name = first.Name, Email = $"altra.{tag}@altrove-prova.example" });
                AddressBook.Sent([second], DateTime.Now);
                return (alike, AddressBook.Suggest(second.Email, 8).Count);
            });
            bool free = ui.Wait(3000);
            double waited = clock.Elapsed.TotalMilliseconds;
            var exit = Task.Run(AddressBook.Save); // come all'uscita: torna solo a scrittura finita
            bool exitWaits = !exit.Wait(300);
            release.Set();
            bool finished = Task.WaitAll([slow, exit], 10_000);
            string disk = File.Exists(file) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser)) : "";
            Check(started && free && ui.Result.alike == first.Email && ui.Result.Count == 1 && exitWaits && finished && writes == 2
                  && disk.Contains(first.Email) && disk.Contains(second.Email),
                  $"B-14: con una scrittura della rubrica ferma sul disco l'interfaccia non aspetta (LookAlike, una voce nuova e Suggest in {waited:0} ms); " +
                  $"il salvataggio dell'uscita aspetta la scrittura in corso e porta sul file anche la voce arrivata intanto ({writes} scritture, nulla perso)");
        }
        finally
        {
            release.Set();
            AddressBook.TestWriting = null;
            if (before != null) File.WriteAllBytes(file, before);
            else File.Delete(file);
            AddressBook.Reset();
        }
    }

    /// <summary>
    /// B-14 e B-15, sola misura: quanto costano le scritture che oggi stanno sotto un lucchetto preso anche dall'interfaccia
    /// (rubrica.json, contatti.dat) o sul thread dell'interfaccia (la copia locale degli elenchi), con i dati di oggi e con
    /// molti di più. Gli stessi passi delle scritture vere (JSON, DPAPI, SafeFile.Write o file temporaneo e rinomina), su
    /// file temporanei.
    /// </summary>
    static void DataWritesMeasure()
    {
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-scritture-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var account = new MailAccount { Id = "misura-b15-" + Guid.NewGuid().ToString("N")[..6], Kind = AccountKind.Imap, Email = "mario.rossi@studio-prova.example", Imap = new ImapServer() };
        static double Median(Func<double> once, int times = 9) => Enumerable.Range(0, times).Select(_ => once()).OrderBy(t => t).ElementAt(times / 2);
        static double Ms(Action work)
        {
            var clock = Stopwatch.StartNew();
            work();
            return clock.Elapsed.TotalMilliseconds;
        }
        try
        {
            foreach (int n in new[] { 364, 5_000, 20_000 })
            {
                var book = Enumerable.Range(0, n).Select(i => new AddressBook.Entry
                {
                    Name = "Maria Rossi " + i, Email = $"maria.rossi{i}@clienti-prova.example", Received = i % 7, Sent = i % 3, LastReceived = DateTime.Now.AddDays(-i % 300), LastSent = DateTime.Now.AddDays(-i % 90),
                }).ToList();
                byte[] json = [], sealedBytes = [];
                double serialize = Median(() => Ms(() => json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { Entries = book, SentScans = new Dictionary<string, DateTime> { ["a"] = DateTime.UtcNow } })));
                double protect = Median(() => Ms(() => sealedBytes = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser)));
                double write = Median(() => Ms(() => SafeFile.Write(Path.Combine(dir, "rubrica.json"), sealedBytes)));
                Report.Add($"      misura B-14: rubrica di {n} voci ({json.Length / 1024} KB): JSON {serialize:0.0} ms, DPAPI {protect:0.0} ms, scrittura con Flush {write:0.0} ms (mediane di 9)");
            }
            foreach (int n in new[] { 200, 4_500 })
            {
                var contacts = Enumerable.Range(0, n).Select(i => new Contact
                {
                    DisplayName = "Mario Rossi " + i, GivenName = "Mario", Surname = "Rossi " + i, Company = "Studio di prova", Emails = [$"mario.rossi{i}@studio-prova.example"], Mobile = "347 000 " + (1000 + i % 9000),
                }).ToList();
                byte[] json = [], sealedBytes = [];
                double serialize = Median(() => Ms(() => json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(contacts, ContactsJson.Default.ListContact)));
                double protect = Median(() => Ms(() => sealedBytes = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser)));
                double write = Median(() => Ms(() => SafeFile.Write(Path.Combine(dir, "contatti.dat"), sealedBytes)));
                Report.Add($"      misura B-14: {n} contatti ({json.Length / 1024} KB): JSON {serialize:0.0} ms, DPAPI {protect:0.0} ms, scrittura con Flush {write:0.0} ms (mediane di 9)");
            }
            foreach (int n in new[] { 50, 200 })
            {
                var page = Enumerable.Range(0, n).Select(i => new MessageSummary
                {
                    Id = (1000 + i).ToString(), FolderId = "INBOX", StableId = $"<messaggio-{i}@fornitori-prova.example>", Subject = "Fattura di prova numero " + i, Date = DateTime.Now.AddHours(-i),
                    From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" }, To = [new Address { Name = "Mario Rossi", Email = "mario.rossi@studio-prova.example" }],
                    Preview = string.Concat(Enumerable.Repeat("Gentile cliente, in allegato la fattura del mese. ", 4)),
                }).ToList();
                double save = Median(() => Ms(() => MailCache.SaveMessages(account, "INBOX", null, page)), 21);
                double prepend = Median(() => Ms(() => MailCache.Prepend(account, "INBOX", null, [new MessageSummary { Id = Guid.NewGuid().ToString("N"), FolderId = "INBOX", Subject = "Nuovo", Date = DateTime.Now }])), 5);
                Report.Add($"      misura B-15: copia locale di un elenco di {n} righe: scrittura {save:0.0} ms (mediana di 21); un arrivo con la cartella chiusa (rilettura e riscrittura, MailCache.Prepend) {prepend:0.0} ms");
            }
            double folders = Median(() => Ms(() => MailCache.SaveFolders(account, Enumerable.Range(0, 40).Select(i => new MailFolder { Id = "cartella-" + i, Name = "Cartella " + i }).ToList())), 21);
            Report.Add($"      misura B-15: copia locale di 40 cartelle: scrittura {folders:0.0} ms (mediana di 21)");
        }
        finally
        {
            MailCache.Delete(account);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// B-12, sola misura: una bozza ripresa che porta con sé la citazione di una newsletter (tabelle annidate, stili scritti
    /// negli elementi, link, immagini da Internet). Quanto costano l'analisi dell'HTML, la costruzione dell'albero di WPF,
    /// l'ingresso nel documento e la prima impaginazione di un RichTextBox fuori schermo.
    /// </summary>
    static void BigDraftMeasure()
    {
        static string Newsletter(int bytes)
        {
            var html = new StringBuilder("<div style=\"font-family:Arial,sans-serif;font-size:12pt\"><p>Buongiorno Maria,<br>in risposta alla vostra comunicazione.</p><blockquote>");
            html.Append("<table width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"background-color:#ffffff\"><tbody>");
            for (int row = 0; html.Length < bytes; row++)
            {
                html.Append("<tr><td style=\"padding:12px 24px;background-color:#f4f4f4\"><table width=\"100%\"><tr>");
                for (int cell = 0; cell < 2; cell++)
                    html.Append("<td style=\"padding:8px;border-width:1px;border-color:#dddddd\"><a href=\"https://www.negozio-prova.example/offerta/").Append(row).Append('/').Append(cell)
                        .Append("?utm_source=newsletter&amp;utm_campaign=ottobre\"><img src=\"https://immagini.negozio-prova.example/p/").Append(row).Append(".jpg\" width=\"260\" alt=\"Prodotto\"></a>")
                        .Append("<p style=\"margin:0;font-size:16px;color:#222222\"><b>Offerta numero ").Append(row).Append("</b></p><p style=\"margin:0;font-size:13px;color:#666666\">Descrizione del prodotto di prova, ")
                        .Append("con <span style=\"color:#c50f1f\">sconto</span> e <i>consegna</i> in due giorni.<br>Solo fino a domenica.</p></td>");
                html.Append("</tr></table></td></tr>");
            }
            return html.Append("</tbody></table></blockquote></div>").ToString();
        }
        HtmlToFlow.Blocks(Newsletter(20 * 1024)); // la prima volta paga anche le regex e i tipi di WPF
        foreach (int kb in new[] { 200, 1000 })
        {
            var html = Newsletter(kb * 1024);
            var clock = Stopwatch.StartNew();
            var dom = MailHtml.ParseWithinLimits(html);
            double parse = clock.Elapsed.TotalMilliseconds;
            int elements = dom?.All.Length ?? 0;
            clock.Restart();
            var blocks = HtmlToFlow.Blocks(html);
            double all = clock.Elapsed.TotalMilliseconds;
            var editor = new System.Windows.Controls.RichTextBox { Width = 760, Height = 500 };
            clock.Restart();
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.AddRange(blocks);
            double added = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            editor.Measure(new System.Windows.Size(760, 500));
            editor.Arrange(new System.Windows.Rect(0, 0, 760, 500));
            editor.UpdateLayout();
            double layout = clock.Elapsed.TotalMilliseconds;
            Report.Add($"      misura B-12: bozza con una newsletter citata di {html.Length / 1024} KB ({elements} elementi, {(dom == null ? "oltre i tetti: solo testo" : "sotto i tetti")}): analisi {parse:0} ms, " +
                       $"HtmlToFlow.Blocks in tutto {all:0} ms, ingresso nel documento {added:0} ms, prima impaginazione fuori schermo {layout:0} ms");
        }
    }

    /// <summary>Un albero dell'editor scritto per esteso: blocchi, testo, a capo e formato, per confrontarne due.</summary>
    static string FlowShape(IEnumerable<System.Windows.Documents.Block> blocks)
    {
        var text = new StringBuilder();
        void Inlines(System.Windows.Documents.InlineCollection inlines)
        {
            foreach (var inline in inlines)
                switch (inline)
                {
                    case System.Windows.Documents.Run r:
                        text.Append("R(").Append(r.Text).Append(r.FontWeight == System.Windows.FontWeights.Bold ? "|b" : "").Append(r.FontStyle == System.Windows.FontStyles.Italic ? "|i" : "")
                            .Append(r.TextDecorations is { Count: > 0 } ? "|u" : "").Append(')');
                        break;
                    case System.Windows.Documents.LineBreak: text.Append("BR "); break;
                    case System.Windows.Documents.Hyperlink h:
                        text.Append("A(").Append(h.NavigateUri).Append(' ');
                        Inlines(h.Inlines);
                        text.Append(')');
                        break;
                    default: text.Append(inline.GetType().Name).Append(' '); break;
                }
        }
        void Blocks(IEnumerable<System.Windows.Documents.Block> list)
        {
            foreach (var block in list)
                switch (block)
                {
                    case System.Windows.Documents.Paragraph p:
                        text.Append("P[").Append(p.TextAlignment).Append(p.Margin.Left).Append(' ');
                        Inlines(p.Inlines);
                        text.Append("] ");
                        break;
                    case System.Windows.Documents.List l:
                        text.Append("L").Append(l.MarkerStyle).Append('[');
                        foreach (var item in l.ListItems)
                        {
                            text.Append("I[");
                            Blocks(item.Blocks);
                            text.Append("] ");
                        }
                        text.Append("] ");
                        break;
                    case System.Windows.Documents.Table t:
                        text.Append("T[");
                        foreach (var row in t.RowGroups.SelectMany(g => g.Rows))
                        {
                            text.Append("r[");
                            foreach (var cell in row.Cells)
                            {
                                text.Append("c[");
                                Blocks(cell.Blocks);
                                text.Append("] ");
                            }
                            text.Append("] ");
                        }
                        text.Append("] ");
                        break;
                    default: text.Append(block.GetType().Name).Append(' '); break;
                }
        }
        Blocks(blocks);
        return text.ToString();
    }

    /// <summary>
    /// B-11: gli a capo in fondo a un blocco nell'HTML di una bozza ripresa (HtmlToFlow). L'albero dell'editor deve essere lo
    /// stesso di prima (HtmlToFlow.TestOld rifà il conto con NextSibling) su bozze di ogni forma, scritte a mano e generate;
    /// poi il tempo con migliaia di a capo fratelli, prima e ora.
    /// </summary>
    static void DraftBreaksAuditTests()
    {
        static string Old(string html)
        {
            HtmlToFlow.TestOld = 1;
            try { return FlowShape(HtmlToFlow.Blocks(html)); }
            finally { HtmlToFlow.TestOld = 0; }
        }
        string[] byHand =
        [
            "a<br>b", "a<br>", "<br>", "<p>a<br></p>", "<p><br></p>", "<div>a<br> </div>", "<div>a<br>\n<br>\n</div>", "<div>a<br><!-- nota --></div>", "<div>a<br><!-- nota -->b</div>",
            "<div><br><br></div>", "<div><br><br><br>x</div>", "<span>a<br></span>", "<div><span>a<br></span></div>", "<div><span>a<br></span><br></div>", "<ul><li>a<br></li><li><br></li><li>b<br>c</li></ul>",
            "<b>x<br></b>y", "<div><b>x<br></b></div>", "<pre>uno\ndue<br>tre<br></pre>", "<table><tr><td>a<br></td><td><br>b</td></tr></table>", "<blockquote>a<br>b<br></blockquote>",
            "<div>a<br>&nbsp;</div>", "<div>a<br>&nbsp; </div>", "<div>a<br><span> </span></div>", "<div>a<br><img src=\"x\"></div>", "<div>a<br><style>p{}</style></div>", "<p>a<br><br></p><p>b</p>",
            "<div><a href=\"https://www.example.com/\">x<br></a></div>", "<h1>T<br></h1>riga<br>", "<div>a<br>b<br>c<br></div><div><br></div>", "<center>a<br></center>", "<td>a<br></td>",
            "<div>Mario Rossi<br>Via di prova 1<br><br></div>", "<body>a<br> <br> </body>", "testo<br><div>x</div><br>", "<ol><li>1<br><ul><li>2<br></li></ul><br></li></ol>",
        ];
        // Generate: elementi annidati a caso con a capo, testo, spazi e commenti in ogni posizione (seme fisso).
        var random = new Random(20261007);
        string[] tags = ["div", "p", "span", "b", "i", "blockquote", "li", "td", "a", "center", "pre", "u"];
        string Make(int depth)
        {
            var html = new StringBuilder();
            int n = random.Next(1, 6);
            for (int k = 0; k < n; k++)
                switch (random.Next(depth > 3 ? 5 : 7))
                {
                    case 0 or 1: html.Append("<br>"); break;
                    case 2: html.Append("riga ").Append(random.Next(100)); break;
                    case 3: html.Append(random.Next(2) == 0 ? " " : "\n  "); break;
                    case 4: html.Append("<!-- c -->"); break;
                    default:
                        var tag = tags[random.Next(tags.Length)];
                        html.Append('<').Append(tag).Append('>').Append(Make(depth + 1)).Append("</").Append(tag).Append('>');
                        break;
                }
            return html.ToString();
        }
        var generated = Enumerable.Range(0, 400).Select(_ => Make(0)).ToList();
        var different = byHand.Concat(generated).Where(html => Old(html) != FlowShape(HtmlToFlow.Blocks(html))).ToList();
        int breaks = byHand.Concat(generated).Sum(html => html.Split("<br>").Length - 1);
        string sample = FlowShape(HtmlToFlow.Blocks("<div>a<br>b<br></div><p><br></p>"));
        static int Count(string text, string part) => text.Split(part).Length - 1;
        Check(different.Count == 0 && Count(sample, "P[") == 2 && Count(sample, "BR ") == 1 && sample.Contains("R(a)BR R(b)]"),
              $"B-11: bozza ripresa, l'albero dell'editor è quello di prima su {byHand.Length} bozze scritte a mano e {generated.Count} generate ({breaks} a capo in tutto; " +
              $"diverse: {different.Count}{(different.Count > 0 ? ", la prima: " + different[0] : "")}); un a capo in fondo a un blocco non aggiunge una riga, in un blocco vuoto sì ({sample.Trim()})");

        // Il tempo con N a capo fratelli nello stesso elemento ("riga i<br>"). Prima cresceva col quadrato per due motivi:
        // Inlines.Count di WPF chiesto a ogni pezzo di testo (il grosso: il rapporto, scritto su Linux, non poteva vederlo)
        // e NextSibling di AngleSharp a ogni a capo (quello del rapporto).
        static string Lines(int n) => "<div>" + string.Concat(Enumerable.Range(0, n).Select(i => $"riga {i}<br>")) + "</div>";
        static double Time(string html, int old)
        {
            HtmlToFlow.TestOld = old;
            try
            {
                var clock = Stopwatch.StartNew();
                HtmlToFlow.Blocks(html);
                return clock.Elapsed.TotalMilliseconds;
            }
            finally { HtmlToFlow.TestOld = 0; }
        }
        Time(Lines(200), 0);
        Time(Lines(200), 1);
        var times = new List<string>();
        double before2 = 0, now2 = 0, now40 = 0, sibling40 = 0;
        foreach (int n in new[] { 1_000, 2_000, 4_000 })
        {
            var html = Lines(n);
            double now = Time(html, 0), old = Time(html, 1);
            if (n == 2_000) (before2, now2) = (old, now);
            times.Add($"{n} a capo: prima {old:0} ms, ora {now:0} ms");
        }
        foreach (int n in new[] { 10_000, 20_000, 40_000 })
        {
            var html = Lines(n);
            double now = Time(html, 0), sibling = Time(html, 2);
            if (n == 40_000) (now40, sibling40) = (now, sibling);
            times.Add($"{n} a capo: ora {now:0} ms, con il solo NextSibling di prima {sibling:0} ms");
        }
        bool same = Old(Lines(300)) == FlowShape(HtmlToFlow.Blocks(Lines(300)));
        Check(same && now2 * 5 < before2 && now40 < now2 * 20 * 4 && now40 < 5000,
              $"B-11: migliaia di a capo fratelli in una bozza ripresa, tempo di HtmlToFlow.Blocks ({string.Join("; ", times)}): ora cresce in proporzione alle righe");
    }

    /// <summary>
    /// B-13: la coda delle azioni fatte senza rete interrotta a metà della ripetizione. Un provider finto (QueueStub) che,
    /// come Graph, ripete un'azione per messaggio e ne tiene una in viaggio: in quel momento si guarda che cosa c'è in
    /// coda.dat, cioè che cosa troverebbe una Missiva chiusa o caduta lì e riavviata. Prima il file era già vuoto.
    /// </summary>
    static async Task QueueReplayAuditAsync()
    {
        var file = Path.Combine(Store.Dir, "coda.dat");
        var before = File.Exists(file) ? File.ReadAllBytes(file) : null;
        List<string> OnDisk() => !File.Exists(file) ? [] :
            (System.Text.Json.JsonSerializer.Deserialize(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser), QueueJson.Default.ListQueuedAction) ?? [])
            .Select(a => a.MessageId).ToList();
        var account = new MailAccount { Id = "coda-b13", Kind = AccountKind.Microsoft, Email = "mario.rossi@outlook-prova.example" };
        var stub = DispatchProxy.Create<IMailProvider, QueueStub>();
        var queue = (QueueStub)(object)stub;
        queue.Account = account;
        var trash = new MailFolder { Id = "cestino", Name = "Posta eliminata", Kind = FolderKind.Deleted };
        Task<List<MailFolder>> Folders() => Task.FromResult(new List<MailFolder> { trash });
        static MessageSummary M(int n) => new() { Id = "m" + n, FolderId = "arrivo" };
        var nothing = new ActionQueue.Replay(0, 0, 0);
        try
        {
            File.Delete(file);
            ActionQueue.Reset();
            // Sei spostamenti fatti senza rete. Al ritorno della rete il terzo resta in viaggio: qui Missiva esce o cade.
            ActionQueue.Add(account, Enumerable.Range(1, 6).Select(M).ToList(), QueuedKind.Move, trash.Id);
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Act = () => queue.Calls == 3 ? hold.Task : Task.CompletedTask;
            var replay = ActionQueue.ReplayAsync(stub, Folders);
            for (int wait = 0; wait < 300 && queue.Calls < 3; wait++) await Task.Delay(10);
            var crash = File.Exists(file) ? File.ReadAllBytes(file) : null;
            var left = OnDisk();
            int waiting = ActionQueue.Count(account.Id);
            Check(left.SequenceEqual(["m3", "m4", "m5", "m6"]) && waiting == 4,
                  $"B-13: a metà della ripetizione (due spostamenti fatti, il terzo in viaggio) coda.dat ha ancora quelli non finiti, nell'ordine ({left.Count} nel file: {string.Join(",", left)}; " +
                  $"{waiting} in attesa sotto l'account): se Missiva esce o cade qui non si perdono");

            // Intanto un secondo ritorno della rete non ripete nulla, e Annulla su uno spostamento già preso dice "già partito".
            var second = await ActionQueue.ReplayAsync(stub, Folders);
            int callsAfterSecond = queue.Calls;
            int undone = ActionQueue.Remove(account, [M(5)], trash.Id);
            hold.SetResult();
            var done = await replay;
            Check(second == nothing && callsAfterSecond == 3 && undone == 0 && done is { Done: 6, Gone: 0, Waiting: 0, Failed: 0 }
                  && queue.Acted.SequenceEqual(["m1", "m2", "m3", "m4", "m5", "m6"]) && !File.Exists(file) && ActionQueue.Count(account.Id) == 0,
                  $"B-13: un secondo ritorno della rete durante la ripetizione non ripete nulla, Annulla non toglie uno spostamento già preso, e alla fine ognuno è partito una volta sola " +
                  $"({string.Join(",", queue.Acted)}) e il file non c'è più");

            // La Missiva riavviata dopo la caduta: rilegge il file di quel momento e riparte da dove era.
            if (crash != null) File.WriteAllBytes(file, crash);
            ActionQueue.Reset();
            queue.Acted.Clear();
            queue.Act = () => Task.CompletedTask;
            int found = ActionQueue.Count(account.Id);
            var restarted = await ActionQueue.ReplayAsync(stub, Folders);
            Check(found == 4 && restarted is { Done: 4, Waiting: 0 } && queue.Acted.SequenceEqual(["m3", "m4", "m5", "m6"]) && !File.Exists(file),
                  $"B-13: riavviata dopo la caduta, Missiva ritrova le {found} azioni non finite e le ripete ({string.Join(",", queue.Acted)}): quella in viaggio un'altra volta, le due già fatte no");

            // La rete cade di nuovo al terzo: fatte due, le altre restano in coda e nel file nello stesso ordine, e ripartono dopo.
            ActionQueue.Add(account, Enumerable.Range(1, 6).Select(M).ToList(), QueuedKind.Read);
            queue.Acted.Clear();
            queue.Calls = 0;
            queue.Act = () => queue.Calls == 3 ? Task.FromException(new IOException("rete", new System.Net.Sockets.SocketException(10065))) : Task.CompletedTask;
            var dropped = await ActionQueue.ReplayAsync(stub, Folders);
            var kept = OnDisk();
            queue.Act = () => Task.CompletedTask;
            queue.Acted.Clear();
            var resumed = await ActionQueue.ReplayAsync(stub, Folders);
            Check(dropped is { Done: 2, Waiting: 4, Gone: 0, Failed: 0 } && kept.SequenceEqual(["m3", "m4", "m5", "m6"]) && resumed is { Done: 4, Waiting: 0 }
                  && queue.Acted.SequenceEqual(["m3", "m4", "m5", "m6"]) && !File.Exists(file),
                  $"B-13: la rete cade di nuovo alla terza azione: due fatte, quattro restano in coda nello stesso ordine ({string.Join(",", kept)}) e ripartono al ritorno dopo, una volta sola");

            // Un segno cambiato mentre il suo contrario è in viaggio prende il suo posto e parte al giro dopo; un account
            // rimosso a metà non lascia azioni nel file (prima, se la rete cadeva, tornavano in coda per sempre).
            ActionQueue.Add(account, [M(1)], QueuedKind.Read);
            hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Calls = 0;
            queue.Act = () => hold.Task;
            replay = ActionQueue.ReplayAsync(stub, Folders);
            for (int wait = 0; wait < 300 && queue.Calls < 1; wait++) await Task.Delay(10);
            ActionQueue.Add(account, [M(1)], QueuedKind.Unread);
            int whileFlying = ActionQueue.Count(account.Id);
            hold.SetResult();
            await replay;
            int afterFirst = ActionQueue.Count(account.Id);
            queue.Act = () => Task.CompletedTask;
            var opposite = await ActionQueue.ReplayAsync(stub, Folders);
            ActionQueue.Add(account, [M(7), M(8), M(9)], QueuedKind.Flag);
            hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Calls = 0;
            queue.Act = () => hold.Task;
            replay = ActionQueue.ReplayAsync(stub, Folders);
            for (int wait = 0; wait < 300 && queue.Calls < 1; wait++) await Task.Delay(10);
            ActionQueue.Forget(account.Id);
            hold.SetException(new IOException("rete", new System.Net.Sockets.SocketException(10065)));
            await replay;
            Check(whileFlying == 1 && afterFirst == 1 && opposite is { Done: 1 } && ActionQueue.Count(account.Id) == 0 && !File.Exists(file),
                  $"B-13: \"da leggere\" chiesto mentre \"letto\" è in viaggio prende il suo posto e parte al giro dopo ({afterFirst} in attesa dopo il primo giro); " +
                  $"account rimosso a metà della ripetizione con la rete che cade: nessuna azione torna nel file ({ActionQueue.Count(account.Id)})");

            // Misura: 300 azioni una per messaggio, quanto costano le scritture del file (una per azione finita).
            ActionQueue.Add(account, Enumerable.Range(1, 300).Select(M).ToList(), QueuedKind.Move, trash.Id);
            long size = new FileInfo(file).Length;
            queue.Act = () => Task.CompletedTask;
            var clock = Stopwatch.StartNew();
            var many = await ActionQueue.ReplayAsync(stub, Folders);
            Report.Add($"      misura B-13: 300 spostamenti ripetuti uno per messaggio con il provider finto in {clock.ElapsedMilliseconds} ms in tutto (coda.dat di {size} byte all'inizio; {many.Done} fatti): " +
                       "è il costo delle scritture del file durante la ripetizione");
        }
        finally
        {
            ActionQueue.Forget(account.Id);
            if (before != null) File.WriteAllBytes(file, before);
            else File.Delete(file);
            ActionQueue.Reset();
        }
    }
}
#endif
