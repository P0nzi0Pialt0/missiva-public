#if DEBUG
using System.Net;
using System.Reflection;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using MimeKit;

namespace Missiva;

/// <summary>
/// Solo in Debug: Missiva.exe --prova-bozze risultato.txt prova il salvataggio delle bozze senza toccare nessuna casella
/// vera. Graph passa da un gestore HTTP finto (POST, PATCH e /send sullo stesso messaggio, allegati tolti eliminati,
/// citazione di createReply), Gmail da un server IMAP finto sul PC (APPEND con \Draft e \Seen, MIME, allegati della
/// bozza di prima riscaricati prima di eliminarla, UID EXPUNGE). Va avviato con MISSIVA_DATA: ogni giro lavora in una
/// sottocartella nuova di quella, "giro-...", che a giro riuscito si cancella (senza MISSIVA_DATA, fra i file
/// temporanei: mai fra i dati veri). Nel file i controlli, uno per riga, e in fondo controlli e secondi di ogni gruppo;
/// codice di uscita 1 se uno non passa. I gruppi, chi li fa girare e i dati di un giro sono in ProvaBozze.Gruppi.cs.
/// </summary>
static partial class ProvaBozze
{
    static readonly List<string> Report = new();
    static int failures, checks;

    /// <summary>
    /// Avvio più veloce (giro P3): impostazioni e copia locale scritte dal generatore di System.Text.Json uguali a quelle di
    /// prima (con la riflessione), e il token di accesso di Gmail salvato cifrato, riusato al prossimo avvio e dimenticato
    /// dopo un nuovo accesso.
    /// </summary>
    static void StartupTests()
    {
        Report.Add("--- Avvio: impostazioni, copia locale e token salvati");
        var reflection = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var settings = new Settings
        {
            MicrosoftClientId = "id", GoogleClientId = "google", ProtectedGoogleClientSecret = "segreto",
            Accounts =
            [
                new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio.example", Tenant = "organizations", HomeAccountId = "casa" },
                new MailAccount { Kind = AccountKind.Imap, Email = "info@studio.example", Imap = new ImapServer { ImapHost = "imap.studio.example", SmtpSecurity = MailSecurity.StartTls, ProtectedPassword = "x" } },
            ],
            Theme = AppTheme.Dark, NotificationsOff = ["a"], Focused = { ["a"] = false }, TextSize = TextSize.Large, ReadingPane = ReadingPanePosition.Bottom,
            MarkRead = MarkReadMode.AfterDelay, ScheduledSends = [new ScheduledSend { AccountId = "a", DraftKey = "k", At = new DateTime(2026, 10, 25, 2, 30, 0), Subject = "Pianificato" }],
            Snoozed = [new SnoozedMessage { AccountId = "a", Key = "k", FolderId = "f", Until = DateTime.Today, Subject = "Posposto", From = "Mario" }],
            Woken = { ["a|k"] = DateTime.Today }, CategoryColors = { ["a|Rossa"] = 3 }, LocalCategories = { ["a"] = ["Rossa"] },
            MessageMenuOrder = ["reply", "-"], Signatures = { ["a"] = new Signature { Rtf = "{\\rtf1 firma}", Replies = false } },
            Pinned = { ["a"] = ["m1"] }, FolderOrder = { ["a"] = ["INBOX"] }, HiddenCalendars = ["a|c"], RecentSearches = ["fattura"],
        };
        var before = System.Text.Json.JsonSerializer.Serialize(settings, reflection);
        var after = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJson.Default.Settings);
        Check(before == after, $"impostazioni: il JSON del generatore è lo stesso di prima, con la riflessione ({after.Length} caratteri, enumerazioni come testo, rientrato)");
        var back = System.Text.Json.JsonSerializer.Deserialize(before, SettingsJson.Default.Settings)!;
        Check(System.Text.Json.JsonSerializer.Serialize(back, reflection) == before && back.Accounts[1].Imap!.SmtpSecurity == MailSecurity.StartTls && back.Woken.Count == 1,
              "impostazioni scritte da una versione di prima rilette uguali (account, server IMAP, posposti, firme)");
        // Correzioni del giro P3: oggetto e mittente di Posposti e invii pianificati cifrati; quelli in chiaro di prima si leggono.
        var legacy = System.Text.Json.JsonSerializer.Deserialize("{\"Snoozed\":[{\"Subject\":\"Colloquio Rossi\",\"From\":\"Anna\"}]}", SettingsJson.Default.Settings)!;
        Check(!after.Contains("Posposto") && !after.Contains("Pianificato") && !after.Contains("\"Mario\"") && after.Contains("\"dpapi:")
              && back.Snoozed[0] is { Subject: "Posposto", From: "Mario" } && back.ScheduledSends[0].Subject == "Pianificato"
              && legacy.Snoozed[0] is { Subject: "Colloquio Rossi", From: "Anna" }
              && !System.Text.Json.JsonSerializer.Serialize(legacy, SettingsJson.Default.Settings).Contains("Colloquio Rossi"),
              "impostazioni: oggetto e mittente di Posposti e invii pianificati cifrati con DPAPI; quelli scritti in chiaro prima si leggono e si cifrano");
        // Correzioni del giro P5: le ricerche recenti (con filtri e indirizzi dei clienti) cifrate; quelle in chiaro di prima si leggono.
        var oldSearches = System.Text.Json.JsonSerializer.Deserialize("{\"RecentSearches\":[\"da:\\\"Avv. Mario Rossi\\\" oggetto:\\\"Pratica Bianchi\\\"\",\"fattura\"]}", SettingsJson.Default.Settings)!;
        var resealed = System.Text.Json.JsonSerializer.Serialize(oldSearches, SettingsJson.Default.Settings);
        Check(!after.Contains("\"fattura\"") && back.RecentSearches is ["fattura"] && oldSearches.RecentSearches is ["da:\"Avv. Mario Rossi\" oggetto:\"Pratica Bianchi\"", "fattura"]
              && !resealed.Contains("Rossi") && System.Text.Json.JsonSerializer.Deserialize(resealed, SettingsJson.Default.Settings)!.RecentSearches.SequenceEqual(oldSearches.RecentSearches),
              "impostazioni: ricerche recenti cifrate con DPAPI (filtri e indirizzi non in chiaro); quelle scritte in chiaro prima si leggono e si cifrano");
        var person = "persona." + Guid.NewGuid().ToString("N")[..8] + "@esempio-prova.it";
        AddressBook.Sent([new Address { Name = "Persona di prova", Email = person }], DateTime.Now);
        AddressBook.Save();
        var book = File.ReadAllBytes(Path.Combine(Store.Dir, "rubrica.json"));
        Check(!Encoding.UTF8.GetString(book).Contains(person) && Encoding.UTF8.GetString(ProtectedData.Unprotect(book, null, DataProtectionScope.CurrentUser)).Contains(person)
              && AddressBook.Suggest("persona di prova", 3).Any(e => e.Email == person),
              "rubrica dei suggerimenti cifrata con DPAPI: nomi e indirizzi non sono in chiaro in rubrica.json");
        // Il file vero della cartella del giro, scritto qui: in una cartella nuova c'è solo se un gruppo di prima ha già
        // salvato, e questo gruppo da solo saltava il controllo senza dirlo (audit dell'11 ottobre, C-49).
        Store.Save();
        var real = Path.Combine(Store.Dir, "impostazioni.json");
        {
            var text = File.ReadAllText(real);
            var byReflection = System.Text.Json.JsonSerializer.Deserialize<Settings>(text, reflection)!;
            var byGenerator = System.Text.Json.JsonSerializer.Deserialize(text, SettingsJson.Default.Settings)!;
            Check(System.Text.Json.JsonSerializer.Serialize(byReflection, reflection) == System.Text.Json.JsonSerializer.Serialize(byGenerator, SettingsJson.Default.Settings),
                  "impostazioni.json della cartella di prova: letto e riscritto uguale con i due serializzatori");
        }

        var cacheOptions = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        List<MessageSummary> page = [new() { Id = "1", FolderId = "INBOX", From = new Address { Name = "Mario", Email = "mario@example.com" }, Subject = "Ciao", Date = new DateTime(2026, 9, 27, 8, 0, 0),
                                             To = [new Address { Email = "io@example.com" }], LabelKind = FolderKind.Sent, Categories = ["Rossa"], AttachmentNames = ["a.pdf"], IsImportant = true }];
        List<MailFolder> folders = [new() { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox, Unread = 3, Total = 10 }, new() { Id = "x", Name = "Clienti", ParentId = "INBOX" }];
        Check(System.Text.Json.JsonSerializer.Serialize(page, cacheOptions) == System.Text.Json.JsonSerializer.Serialize(page, typeof(List<MessageSummary>), CacheJson.Default) &&
              System.Text.Json.JsonSerializer.Serialize(folders, cacheOptions) == System.Text.Json.JsonSerializer.Serialize(folders, typeof(List<MailFolder>), CacheJson.Default),
              "copia locale: cartelle e prima pagina con lo stesso JSON di prima");

        var fake = "ya29.prova-" + Guid.NewGuid().ToString("N");
        var (encrypted, reused, dropped) = GoogleAuth.TestSavedToken(
            new MailAccount { Kind = AccountKind.Gmail, Email = "prova.token@gmail.com", ProtectedRefreshToken = Store.Protect("aggiornamento finto") }, fake);
        Check(encrypted, "token di Gmail salvato cifrato con DPAPI: nel file né il token né \"ya29.\"");
        Check(reused, "token di Gmail riletto al prossimo avvio, finché vale (nessuna richiesta a Google)");
        Check(dropped, "dopo un nuovo accesso (token di aggiornamento diverso) il token salvato non vale più");

        // Avvio con Windows: nella versione di prova la chiave Run non si scrive mai, solo il registro.
        static string RunValues()
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return run == null ? "" : string.Join("\n", run.GetValueNames().Order().Select(n => n + "=" + run.GetValue(n)));
        }
        var runBefore = RunValues();
        var was = Store.Settings.StartWithWindows;
        Store.Settings.StartWithWindows = true;
        UI.App.ApplyStartWithWindows();
        Store.Settings.StartWithWindows = false;
        UI.App.ApplyStartWithWindows();
        Store.Settings.StartWithWindows = was;
        var logged = File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).TakeLast(2).ToList();
        Check(RunValues() == runBefore && logged.Count == 2 && logged[0].Contains("prova: scriverei l'avvio automatico") && logged[0].Contains("--nascosta") &&
              logged[1].Contains("prova: toglierei l'avvio automatico"),
              "avvio con Windows acceso e spento nella prova: HKCU\\...\\Run uguale a prima, solo \"prova: scriverei / toglierei l'avvio automatico\" nel registro");
    }

    /// <summary>
    /// Rispondi, Posponi ed Elimina dalle notifiche (richiesta dell'autore): la notifica di un messaggio con la casella, la scelta
    /// e tre pulsanti in sottofondo; l'ora di Posponi calcolata al clic; la risposta veloce che passa dal server SMTP finto
    /// (Gmail) e dal Graph finto (createReply, PATCH, send), al Reply-To e con la citazione. Sul thread STA di Main: la
    /// risposta si costruisce con un FlowDocument, come nella finestra.
    /// </summary>
    static void NotificationActions()
    {
        Report.Add("--- Azioni nelle notifiche");
        var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder().AddArgument("account", "a").AddArgument("message", "7").AddText("Mario");
        var target = new Microsoft.Toolkit.Uwp.Notifications.ToastArguments().Add("account", "a").Add("message", "7");
        UI.App.AddActions(toast, target);
        var xml = toast.GetToastContent().GetContent();
        var buttons = System.Text.RegularExpressions.Regex.Matches(xml, "<action (?![^>]*placement=\"contextMenu\")[^>]*>").Select(x => x.Value).ToList();
        var menu = System.Text.RegularExpressions.Regex.Matches(xml, "<action [^>]*placement=\"contextMenu\"[^>]*>").Select(x => x.Value).ToList();
        Check(!xml.Contains("<input") && buttons.Count == 3 && buttons[0].Contains("content=\"Rispondi\"") && !buttons[0].Contains("activationType=\"background\"") &&
              buttons[1].Contains($"content=\"{UI.App.FlagButton}\"") && UI.App.FlagButton.Length <= 10 && buttons[1].Contains("activationType=\"background\"") &&
              buttons[2].Contains("content=\"Elimina\"") && buttons[2].Contains("arguments=\"action=delete;account=a;message=7\"") &&
              menu.Count == 2 && menu.All(x => x.Contains("account=a") && x.Contains("message=7")) &&
              menu[0].Contains("content=\"Posponi di un'ora\"") && menu[0].Contains("action=snooze") && menu[0].Contains("snooze=1h") && menu[0].Contains("activationType=\"background\"") &&
              menu[1].Contains("content=\"Posponi...\"") && menu[1].Contains("action=snoozepick") && !menu[1].Contains("activationType=\"background\"") &&
              buttons.Count + menu.Count <= 5,
              "notifica di un messaggio (richiesta dell'autore): tre pulsanti, Rispondi (in primo piano), Contrassegna ed Elimina (in sottofondo); nel menu \"...\" Posponi di un'ora e Posponi... (la scelta di Missiva), cinque voci al massimo");
        // L'immagine del mittente senza logo: un PNG del suo colore e delle sue iniziali, lo stesso file per gli stessi, uno diverso per altri.
        var mario = UI.NotificationAvatar.PathOf(new Address { Name = "Mario Rossi", Email = "mario@studio.example" });
        var marioAgain = UI.NotificationAvatar.PathOf(new Address { Name = "Mario Rossi", Email = "altro@studio.example" });
        var anna = UI.NotificationAvatar.PathOf(new Address { Name = "Anna Bianchi", Email = "anna@studio.example" });
        var bare = UI.NotificationAvatar.PathOf(new Address { Email = "assistenza@paypal.it" });
        Check(mario != null && mario == marioAgain && anna != null && anna != mario && bare != null && File.Exists(mario) && File.Exists(bare) &&
              new FileInfo(mario).Length > 500 && File.ReadAllBytes(mario).Take(4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
              "immagine del mittente nella notifica senza logo: PNG con colore e iniziali dell'elenco (MR), lo stesso file per lo stesso nome, uno diverso per un altro, anche con il solo indirizzo");
        var ten = new DateTime(2026, 10, 1, 10, 20, 0);
        Check(UI.App.SnoozeUntil("1h", ten) == ten.AddHours(1) && UI.App.SnoozeUntil("sera", ten) == ten.Date.AddHours(18) &&
              UI.App.SnoozeUntil("sera", ten.Date.AddHours(19)) == ten.Date.AddHours(20) && UI.App.SnoozeUntil("domani", ten) == ten.Date.AddDays(1).AddHours(8) &&
              UI.App.SnoozeUntil("domani", ten.Date.AddHours(3)) == ten.Date.AddHours(8) && UI.App.SnoozeUntil(null, ten) == ten.AddHours(1),
              "Posponi calcolato al clic: fra un'ora, stasera alle 18 (passate, fra un'ora), domani alle 8 (prima delle 5, stamattina)");

        // Rispondi dalle notifiche di prima (con la casella) resta: il testo non parte se nasconderebbe una frode.
        var personal = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@gmail.com" };
        var office = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio-prova.it", Tenant = MicrosoftAuth.Organizations };
        MessageSummary Mail(string from, params string[] replyTo) => new()
        {
            Id = "9", FolderId = "INBOX", Subject = "Bonifico", From = new Address { Name = "Mario Rossi (amministratore)", Email = from },
            ReplyTo = replyTo.Select(e => new Address { Email = e }).ToList(),
        };
        Check(SenderCheck.QuickReplyRisk(personal, Mail("mario@studio.example", "pagamenti@attaccante.example")) != null &&
              SenderCheck.QuickReplyRisk(personal, Mail("wordpress@studio.example", "segreteria@mail.studio.example")) == null &&
              SenderCheck.QuickReplyRisk(personal, Mail("mario@studio.example")) == null &&
              SenderCheck.QuickReplyRisk(office, Mail("cliente@esterno.example")) != null && SenderCheck.QuickReplyRisk(office, Mail("anna@studio-prova.it")) == null,
              "Rispondi dalla notifica no con il Reply-To di un altro dominio o un mittente esterno per il lavoro; sì con lo stesso dominio");
        var alias = new SendIdentity { Name = "Studio Rossi", Email = "info@studio-prova.it" };
        var withAlias = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", Id = "gmail-alias-notifica", Identities = [alias] };
        var toInfo = new MessageSummary { Id = "5", FolderId = "INBOX", Subject = "Preventivo", From = new Address { Email = "cliente@example.com" }, To = [new Address { Email = "info@studio-prova.it" }] };
        Check(UI.MainWindow.QuickReply(withAlias, toInfo, "Grazie").From == alias && UI.MainWindow.QuickReply(withAlias, Mail("mario@studio.example"), "Grazie").From == null,
              "Rispondi dalla notifica dall'indirizzo a cui era scritto il messaggio (l'alias info@), come Rispondi");

        // Gmail: IMAP e SMTP finti.
        using var fake = new FakeImap();
        using var smtp = new FakeSmtp();
        GmailProvider.TestPort = fake.Port;
        GmailProvider.TestSmtpPort = smtp.Port;
        GmailProvider.TestSmtpCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            var form = new MimeMessage { Subject = "Richiesta dal sito", Body = new TextPart("html") { Text = "<p>Vorrei un preventivo per il bagno</p>" } };
            form.From.Add(new MailboxAddress("Sito dello studio", "wordpress@studio.example"));
            form.ReplyTo.Add(new MailboxAddress("Cliente", "cliente@example.com"));
            form.To.Add(new MailboxAddress("Prova", "prova@example.com"));
            form.MessageId = "richiesta-42@studio.example";
            fake.Add("INBOX", form);
            var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "gmail-notifica" };
            using var gmail = new GmailProvider(account);
            var original = gmail.GetMessagesAsync(new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, null, null)
                .GetAwaiter().GetResult().Messages.Single(m => m.Subject == "Richiesta dal sito");
            var reply = UI.MainWindow.QuickReply(account, original, "Grazie, ricevuto.\nLe mando il preventivo lunedì.");
            gmail.SendAsync(reply).GetAwaiter().GetResult();
            var sent = smtp.Received.Count == 1 ? MimeMessage.Load(new MemoryStream(smtp.Received[0])) : null;
            Check(sent != null && sent.To.Mailboxes.Single().Address == "cliente@example.com" && sent.Subject == "R: Richiesta dal sito" &&
                  sent.InReplyTo == "richiesta-42@studio.example" && sent.HtmlBody?.Contains("Grazie, ricevuto.") == true &&
                  sent.HtmlBody.Contains("Vorrei un preventivo per il bagno") && sent.HtmlBody.Contains("<blockquote"),
                  $"Gmail, Rispondi dalla notifica via SMTP finto: A il Reply-To (cliente@example.com), \"{sent?.Subject}\", In-Reply-To e la citazione");
        }
        finally
        {
            GmailProvider.TestPort = null;
            GmailProvider.TestSmtpPort = null;
            GmailProvider.TestSmtpCertificate = null;
        }

        // Graph finto: createReply, PATCH con i destinatari della notifica, poi send.
        var fakeGraph = new FakeGraph();
        GraphProvider.UseTestHandler(fakeGraph);
        var outlook = new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@example.com" };
        var graph = new GraphProvider(outlook);
        var fromSite = graph.GetMessagesAsync(new MailFolder { Id = "inbox", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, null, null).GetAwaiter().GetResult().Messages.Single();
        fakeGraph.Requests.Clear();
        var graphReply = UI.MainWindow.QuickReply(outlook, fromSite, "Ricevuto, grazie.");
        graph.SendAsync(graphReply).GetAwaiter().GetResult();
        Report.AddRange(fakeGraph.Requests.Select(r => "      " + r[..Math.Min(r.Length, 160)]));
        int create = fakeGraph.Requests.FindIndex(r => r.Contains("/createReply"));
        int patch = fakeGraph.Requests.FindIndex(r => r.StartsWith("PATCH "));
        int send = fakeGraph.Requests.FindIndex(r => r.StartsWith("POST ") && r.Contains("/send"));
        Check(create >= 0 && patch > create && send > patch && fakeGraph.LastPatch.Contains("\"toRecipients\":[{\"emailAddress\":{\"address\":\"cliente@example.com\"") &&
              !fakeGraph.LastPatch.Contains("wordpress@studio.example") && fakeGraph.LastPatch.Contains("Ricevuto, grazie."),
              "Graph, Rispondi dalla notifica: createReply (la citazione la mette Exchange), PATCH con il Reply-To e il testo, poi send");
    }

    /// <summary>
    /// Persone (giro P4, parte 3a): contatti di Outlook con un gestore HTTP finto (nessuna richiesta senza consenso, pagine
    /// con @odata.nextLink, POST dei campi scritti e PATCH dei soli cambiati, nessuna richiesta senza modifiche, DELETE),
    /// vCard e CSV letti come testo (UTF-8, quoted-printable, righe piegate, PHOTO ignorata, file troppo grande rifiutato),
    /// contatti di Missiva cifrati su disco, e nella prova le scritture solo nel registro.
    /// </summary>
    static async Task ContactsAsync()
    {
        Report.Add("--- Persone: contatti di Outlook, vCard e CSV, contatti di Missiva");
        var requests = new List<(HttpMethod Method, string Path, string Token, string Body)>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((request.Method, path, request.Headers.Authorization?.Parameter ?? "", request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            JsonNode json = request.Method == HttpMethod.Get && !path.Contains("skip")
                ? new JsonObject
                {
                    ["value"] = new JsonArray(
                        new JsonObject { ["id"] = "c1", ["displayName"] = "Anna Bianchi", ["emailAddresses"] = new JsonArray(new JsonObject { ["address"] = "anna@studio.example" }),
                                         ["mobilePhone"] = "+39 333 1", ["businessPhones"] = new JsonArray("+39 051 1"), ["companyName"] = "Studio Bianchi",
                                         ["businessAddress"] = new JsonObject { ["street"] = "Via Roma 1", ["city"] = "Bologna" } },
                        new JsonObject { ["id"] = "c2", ["givenName"] = "Luca", ["surname"] = "Verdi" }),
                    ["@odata.nextLink"] = "https://graph.microsoft.com/v1.0/me/contacts?skip=2",
                }
                : request.Method == HttpMethod.Get ? new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = "c3", ["displayName"] = "Segreteria" }) }
                : request.Method == HttpMethod.Post ? new JsonObject { ["id"] = "nuovo" } : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@outlook.example", Id = "outlook-contatti" });
        try
        {
            GraphProvider.TestContactsConsent = false;
            string? refused = null;
            try { await graph.SaveContactAsync(null, new Contact { DisplayName = "X" }); }
            catch (MailException ex) { refused = ex.Message; }
            Check(await graph.GetContactsAsync() == null && refused?.Contains("Consenti l'accesso ai contatti") == true && requests.Count == 0,
                  "senza consenso ai contatti: nessuna richiesta a me/contacts, e le scritture lo spiegano");

            GraphProvider.TestContactsConsent = true;
            var all = await graph.GetContactsAsync();
            var anna = all?.FirstOrDefault() ?? new Contact();
            Check(requests.Count == 2 && requests[0].Path.StartsWith("me/contacts?$top=100&$select=" + ContactFiles.GraphFields) && requests[0].Token == "prova-contatti"
                  && requests[1].Path.Contains("skip=2") && all is [{ Name: "Anna Bianchi" }, { Name: "Luca Verdi" }, { Name: "Segreteria" }]
                  && anna.Emails.Single() == "anna@studio.example" && anna.BusinessPhone == "+39 051 1" && anna.Address == "Via Roma 1, Bologna" && anna.ServerId == "c1",
                  "contatti di Outlook: me/contacts con i soli campi della scheda e la pagina dopo (@odata.nextLink), con il token dei contatti");

            requests.Clear();
            var id = await graph.SaveContactAsync(null, new Contact { GivenName = "Giulia", Surname = "Ferri", Emails = ["giulia@cliente.example"], Mobile = "+39 347 2" });
            var post = JsonNode.Parse(requests.Single().Body)!.AsObject();
            Check(id == "nuovo" && requests.Single().Method == HttpMethod.Post && requests.Single().Path == "me/contacts"
                  && post.Select(p => p.Key).OrderBy(k => k).SequenceEqual(["emailAddresses", "givenName", "mobilePhone", "surname"]),
                  "Nuovo contatto: POST me/contacts con i soli campi scritti");
            requests.Clear();
            var changed = anna.Clone();
            await graph.SaveContactAsync(anna, changed);
            changed.Mobile = "+39 333 9";
            await graph.SaveContactAsync(anna, changed);
            await graph.DeleteContactAsync(anna);
            Check(requests.Count == 2 && requests[0].Method == HttpMethod.Patch && requests[0].Path == "me/contacts/c1" && JsonNode.Parse(requests[0].Body)!.AsObject().Single().Key == "mobilePhone"
                  && requests[1].Method == HttpMethod.Delete && requests[1].Path == "me/contacts/c1",
                  "Modifica: nessuna richiesta senza modifiche, poi PATCH del solo cellulare; Elimina: DELETE");

            requests.Clear();
            var testing = new TestingProvider(graph);
            await testing.SaveContactAsync(null, new Contact { DisplayName = "Nuovo cliente" });
            await testing.SaveContactAsync(anna, changed);
            await testing.DeleteContactAsync(anna);
            var logged = File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).TakeLast(3).ToList();
            Check(requests.Count == 0 && logged[0].Contains("prova: creerei il contatto \"Nuovo cliente\"") && logged[1].Contains("prova: modificherei il contatto \"Anna Bianchi\"")
                  && logged[1].Contains("mobilePhone") && logged[2].Contains("prova: eliminerei il contatto \"Anna Bianchi\""),
                  "nella prova Nuovo, Modifica ed Elimina dei contatti di Outlook vanno solo nel registro");
        }
        finally
        {
            GraphProvider.TestContactsConsent = false;
        }

        var card = "BEGIN:VCARD\r\nVERSION:2.1\r\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Nicol=C3=B2;Andrea;;;\r\nFN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Andrea Nicol=C3=\r\n=B2\r\n" +
                   "TEL;CELL:+39 320 0000000\r\nTEL;WORK;VOICE:+39 051 0000000\r\nEMAIL;INTERNET:andrea@studio-prova.example\r\nNOTE:PEC andrea@pec-prova.example\\nP. IVA 01234567890\r\n" +
                   "PHOTO;ENCODING=BASE64;TYPE=JPEG:/9j/4AAQSkZJRgABAQAAAQABAAD\r\n /2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUV\r\n" +
                   "END:VCARD\r\nBEGIN:VCARD\r\nVERSION:3.0\r\nFN:Maria Rossi-Bianchi con un nome molto lu\r\n ngo e piegato\r\nitem1.EMAIL;type=INTERNET:maria@cliente-prova.example\r\n" +
                   "ORG:Rossi srl;Amministrazione\r\nADR;TYPE=WORK:;;Via Emilia 10;Modena;;41121;Italia\r\nEND:VCARD\r\n";
        var read = ContactFiles.VCard(card);
        Report.Add("      " + string.Join(" | ", read.Select(c => $"{c.Name} <{string.Join(",", c.Emails)}> {c.Mobile} {c.BusinessPhone} {c.Company} {c.Address} {c.Notes.Replace("\n", " / ")}")));
        Check(read.Count == 2 && read[0].Name == "Andrea Nicolò" && read[0].Surname == "Nicolò" && read[0].Mobile == "+39 320 0000000" && read[0].BusinessPhone == "+39 051 0000000"
              && read[0].Notes == "PEC andrea@pec-prova.example\nP. IVA 01234567890" && read[1].Name == "Maria Rossi-Bianchi con un nome molto lungo e piegato"
              && read[1].Emails.Single() == "maria@cliente-prova.example" && read[1].Company == "Rossi srl" && read[1].Address == "Via Emilia 10, Modena, 41121, Italia",
              "vCard 2.1 e 3.0: quoted-printable in UTF-8 anche spezzato, righe piegate, item1.EMAIL, telefoni per tipo, ORG e ADR; la foto ignorata");
        var again = ContactFiles.VCard(ContactFiles.WriteVCard(read));
        Check(again.Count == 2 && again[0].Name == read[0].Name && again[0].Notes == read[0].Notes && again[1].Address.StartsWith("Via Emilia 10"),
              "Esporta: vCard 3.0 riletta uguale (virgole, punti e virgola e a capo scritti come si deve)");
        var outlookCsv = "\"First Name\",\"Last Name\",\"Company\",\"Job Title\",\"E-mail Address\",\"Mobile Phone\",\"Notes\"\r\n\"Paolo\",\"Neri\",\"Neri, Bianchi e soci\",\"Avvocato\",\"paolo@neri-prova.example\",\"+39 335 1\",\"riga uno\r\nriga due\"\r\n";
        var gmailCsv = "Name,Given Name,Family Name,E-mail 1 - Value,Phone 1 - Value,Organization 1 - Name\nElena Gialli,Elena,Gialli,elena@gialli-prova.example,+39 340 2,Gialli spa\n";
        var fromOutlook = ContactFiles.Csv(outlookCsv);
        var fromGmail = ContactFiles.Csv(gmailCsv);
        Check(fromOutlook is [{ Name: "Paolo Neri", Company: "Neri, Bianchi e soci", JobTitle: "Avvocato", Mobile: "+39 335 1" } p] && p.Notes.Contains("riga due") && p.Emails.Single() == "paolo@neri-prova.example"
              && fromGmail is [{ Name: "Elena Gialli", Company: "Gialli spa", Mobile: "+39 340 2" } g] && g.Emails.Single() == "elena@gialli-prova.example",
              "CSV di Outlook (virgolette con virgole e a capo) e di Gmail");
        var big = Path.Combine(Path.GetTempPath(), "missiva-rubrica-grande.vcf");
        File.WriteAllBytes(big, new byte[ContactFiles.MaxBytes + 1]);
        string? tooBig = null;
        try { ContactFiles.Read(big); }
        catch (MailException ex) { tooBig = ex.Message; }
        finally { File.Delete(big); }
        Check(tooBig?.Contains("troppo grande") == true, "un file oltre 5 MB non si legge nemmeno");

        // Correzioni del giro P4: tutti i telefoni di Outlook tenuti, l'indirizzo di casa letto (Graph dà {} per quello di
        // lavoro vuoto) e riscritto lì intero; vCard quoted-printable costruita apposta e 5000 contatti in fretta.
        var office = ContactFiles.FromGraph(JsonNode.Parse("""
            { "id": "c9", "displayName": "Studio Neri", "businessPhones": ["+39 051 1", "+39 051 2"], "homePhones": [],
              "businessAddress": {}, "homeAddress": { "street": "Via Roma, 1", "postalCode": "40100", "city": "Bologna", "state": "BO" } }
            """)!, "outlook-contatti");
        var edited = office.Clone();
        edited.BusinessPhone = "+39 051 9";
        edited.Address = "Via Roma, 3, 40100, Bologna, BO";
        var phonePatch = ContactFiles.ToGraph(office, edited);
        var home = phonePatch["homeAddress"];
        var moved = office.Clone();
        moved.Address = "Piazza Maggiore 6, Bologna";
        var shorter = ContactFiles.ToGraph(office, moved)["homeAddress"];
        Report.Add("      " + phonePatch.ToJsonString());
        Check(office.Address == "Via Roma, 1, 40100, Bologna, BO" && office.AddressField == "homeAddress" && office.BusinessPhone == "+39 051 1"
              && phonePatch["businessPhones"]!.AsArray().Select(p => p!.GetValue<string>()).SequenceEqual(["+39 051 9", "+39 051 2"]) && phonePatch["businessAddress"] == null
              && home?["street"]?.GetValue<string>() == "Via Roma, 3" && home?["postalCode"]?.GetValue<string>() == "40100" && home?["city"]?.GetValue<string>() == "Bologna"
              && home?["state"]?.GetValue<string>() == "BO" && shorter?["street"]?.GetValue<string>() == "Piazza Maggiore 6, Bologna" && shorter?["city"]?.GetValue<string>() == "",
              "contatti di Outlook: il secondo numero dell'ufficio resta, l'indirizzo di casa si legge e torna in homeAddress intero (via con la virgola, CAP, città, provincia); con meno parti tutto nella via");
        var trap = new StringBuilder("BEGIN:VCARD\r\nVERSION:2.1\r\nFN:Biglietto\r\nNOTE;ENCODING=QUOTED-PRINTABLE:");
        while (trap.Length < ContactFiles.MaxBytes - 200) trap.Append("=41=42=43=44=45=46=47=48=49=4A=4B=4C=4D=4E=4F=50=51=52=53=54=55=56=57=58=59=5A=\r\n");
        trap.Append("fine\r\nEMAIL:biglietto@prova.example\r\nEND:VCARD\r\n");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var trapped = ContactFiles.VCard(trap.ToString());
        long trapMs = clock.ElapsedMilliseconds;
        var many = new StringBuilder();
        for (int i = 0; i < 5000; i++) many.Append($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Cliente {i}\r\nEMAIL:cliente{i}@studio-prova.example\r\nTEL;TYPE=WORK:+39 051 {i}\r\nNOTE:Pratica {i}\r\nEND:VCARD\r\n");
        clock.Restart();
        var imported = ContactFiles.VCard(many.ToString());
        long parseMs = clock.ElapsedMilliseconds;
        var contactsFile = Path.Combine(Store.Dir, "contatti.dat");
        var before5000 = File.Exists(contactsFile) ? File.ReadAllBytes(contactsFile) : null;
        try
        {
            clock.Restart();
            ContactBook.Add(imported);
            ContactBook.Save();
            long saveMs = clock.ElapsedMilliseconds;
            ContactBook.Reset();
            Report.Add($"      vCard quoted-printable di {trap.Length / 1024} KB: {trapMs} ms; 5000 contatti: lettura {parseMs} ms, aggiunti e salvati {saveMs} ms");
            Check(trapped is [{ Name: "Biglietto" } t] && t.Notes.Length == 0 && t.Emails.Single() == "biglietto@prova.example" && trapMs < 2000
                  && imported.Count == 5000 && ContactBook.Local.Count(c => c.Emails.FirstOrDefault()?.EndsWith("@studio-prova.example") == true) == 5000 && parseMs + saveMs < 3000,
                  $"vCard costruita apposta (5 MB di quoted-printable) letta in {trapMs} ms, la nota enorme saltata; 5000 contatti letti e salvati una volta sola in {parseMs + saveMs} ms");
        }
        finally
        {
            // Il file di prima rimesso com'era (togliere 5000 contatti uno alla volta li riscriverebbe 5000 volte).
            if (before5000 != null) File.WriteAllBytes(contactsFile, before5000);
            else File.Delete(contactsFile);
            ContactBook.Reset();
        }

        var existing = ContactBook.Local.ToList();
        try
        {
            ContactBook.Put(new Contact { DisplayName = "Cliente Riservato Srl", Emails = ["riservato@cliente-prova.example"], Notes = "IBAN IT00X0000000000000000000000" });
            var bytes = File.ReadAllBytes(Path.Combine(Store.Dir, "contatti.dat"));
            ContactBook.Reset();
            Check(!Encoding.UTF8.GetString(bytes).Contains("Riservato") && !Encoding.UTF8.GetString(bytes).Contains("IBAN") && ContactBook.Local.Any(c => c.DisplayName == "Cliente Riservato Srl"),
                  "contatti di Missiva su disco cifrati con DPAPI (contatti.dat senza nomi né note in chiaro), riletti uguali");
        }
        finally
        {
            foreach (var c in ContactBook.Local.ToList()) ContactBook.Remove(c);
            foreach (var c in existing) ContactBook.Put(c);
        }
    }

    /// <summary>
    /// Contrassegna con scadenza (giro P4): le date di Outlook (Oggi, Domani, Questa settimana, Settimana prossima, di
    /// domenica senza Questa settimana); Graph con startDateTime e dueDateTime nel fuso di Windows anche il 25 ottobre,
    /// complete con completedDateTime, la scadenza riletta dall'elenco; nessuna richiesta senza modifiche; Gmail (IMAP
    /// finto) solo STORE \Flagged; nella prova solo il registro; le date di Missiva cifrate in impostazioni.json.
    /// </summary>
    static async Task FlagsAsync()
    {
        Report.Add("--- Contrassegna con scadenza e promemoria");
        string Names(DateTime day) => string.Join(", ", Flags.Choices(day).Select(c => $"{c.Name} {c.Start:dd/MM}-{c.Due:dd/MM}"));
        var wednesday = Names(new DateTime(2026, 9, 30, 10, 0, 0));
        var sunday = Names(new DateTime(2026, 9, 27, 10, 0, 0));
        var thursday = Names(new DateTime(2026, 10, 1, 10, 0, 0));
        Report.Add($"      mercoledì: {wednesday}; domenica: {sunday}; giovedì: {thursday}");
        Check(wednesday == "Oggi 30/09-30/09, Domani 01/10-01/10, Questa settimana 02/10-02/10, Settimana prossima 05/10-09/10"
              && sunday == "Oggi 27/09-27/09, Domani 28/09-28/09, Settimana prossima 28/09-02/10" && !thursday.Contains("Questa settimana"),
              "date di Outlook: Questa settimana fino al venerdì (di domenica e quando il venerdì è domani non c'è), Settimana prossima da lunedì a venerdì");
        var today = new DateTime(2026, 9, 27);
        Check(Flags.Group(today.AddDays(-2), today) == "In ritardo" && Flags.Group(today, today) == "Oggi" && Flags.Group(today.AddDays(1), today) == "Domani"
              && Flags.Group(today.AddDays(8), today) == "Più avanti" && Flags.Group(null, today) == "Senza data"
              && Flags.Group(new DateTime(2026, 10, 2), new DateTime(2026, 9, 28)) == "Questa settimana"
              && Flags.DueText(today.AddDays(1), today) == "Scadenza: domani" && Flags.Overdue(today.AddDays(-1), today),
              "gruppi di Contrassegnati per scadenza e \"Scadenza: domani\" sulla riga, in rosso se passata");

        var requests = new List<(HttpMethod Method, string Path, string Body)>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((request.Method, path, request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            JsonNode json = path.Contains("/messages?") ? new JsonObject { ["value"] = new JsonArray(new JsonObject
            {
                ["id"] = "m1", ["subject"] = "Preventivo", ["internetMessageId"] = "<m1@cliente.example>", ["flag"] = new JsonObject
                {
                    ["flagStatus"] = "flagged", ["dueDateTime"] = new JsonObject { ["dateTime"] = "2026-10-25T00:00:00.0000000", ["timeZone"] = TimeZoneInfo.Local.Id },
                },
            }) } : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var outlook = new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@outlook.example", Id = "outlook-contrassegni" };
        var graph = new GraphProvider(outlook);
        var message = new MessageSummary { Id = "m1", FolderId = "inbox", Subject = "Preventivo" };
        await graph.SetFlagDueAsync([message], new DateTime(2026, 10, 25), new DateTime(2026, 10, 25));
        var flag = JsonNode.Parse(requests.Single().Body)?["flag"];
        var local = TimeZoneInfo.Local.Id;
        Report.Add("      " + requests.Single().Method + " " + requests.Single().Path + " " + requests.Single().Body);
        Check(requests.Single().Method == HttpMethod.Patch && requests.Single().Path == "me/messages/m1" && flag?["flagStatus"]?.GetValue<string>() == "flagged"
              && flag?["startDateTime"]?["dateTime"]?.GetValue<string>() == "2026-10-25T00:00:00" && flag?["dueDateTime"]?["dateTime"]?.GetValue<string>() == "2026-10-25T00:00:00"
              && flag?["dueDateTime"]?["timeZone"]?.GetValue<string>() == local,
              $"Graph: PATCH del contrassegno con startDateTime e dueDateTime il 25 ottobre nel fuso di Windows ({local})");
        requests.Clear();
        await graph.SetFlagDueAsync([message], null, new DateTime(2026, 10, 2));
        var withoutStart = JsonNode.Parse(requests.Single().Body)?["flag"];
        requests.Clear();
        await graph.CompleteFlagAsync([message]);
        var done = JsonNode.Parse(requests.Single().Body)?["flag"];
        Check(withoutStart?["startDateTime"]?["dateTime"]?.GetValue<string>() is { Length: > 0 } && done?["flagStatus"]?.GetValue<string>() == "complete"
              && done?["completedDateTime"]?["timeZone"]?.GetValue<string>() == local,
              "senza inizio Graph lo riceve lo stesso (altrimenti risponde 400); Segna come completato: complete con completedDateTime");
        requests.Clear();
        var listed = (await graph.GetMessagesAsync(new MailFolder { Id = "inbox", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, null, null)).Messages.Single();
        Check(listed.IsFlagged && listed.FlagDue == new DateTime(2026, 10, 25) && Flags.DueOf(outlook, listed) == new DateTime(2026, 10, 25)
              && listed.InternetMessageId == "<m1@cliente.example>" && requests.Any(r => r.Path.Contains("internetMessageId")) && Flags.KeyOf(outlook, listed) == "<m1@cliente.example>",
              "la scadenza di Graph riletta dall'elenco (flag dueDateTime), con internetMessageId per ritrovare il messaggio spostato");
        var flagged = listed.WithId("m1");
        Check(!Flags.NeedsWrite(outlook, flagged, new DateTime(2026, 10, 25)) && Flags.NeedsWrite(outlook, flagged, new DateTime(2026, 10, 26))
              && !Flags.NeedsWrite(new MailAccount { Kind = AccountKind.Gmail }, new MessageSummary { Id = "1", FolderId = "INBOX", IsFlagged = true }, DateTime.Today),
              "nessuna PATCH senza modifiche (stessa scadenza), e per Gmail e IMAP nessuno STORE su un messaggio già contrassegnato");

        using var fake = new FakeImap();
        GmailProvider.TestPort = fake.Port;
        try
        {
            using var gmail = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com" });
            var inbox = new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox };
            fake.Add("INBOX", "Da rispondere entro venerdì");
            var first = (await gmail.GetMessagesAsync(inbox, null, null)).Messages.First();
            fake.Commands.Clear();
            await ((IMailProvider)gmail).SetFlagDueAsync([first], DateTime.Today, DateTime.Today.AddDays(1));
            var stores = fake.Commands.Where(c => c.Contains("STORE")).ToList();
            Report.AddRange(stores.Select(c => "      " + c));
            Check(stores.Count == 1 && stores[0].Contains(@"+FLAGS") && stores[0].Contains(@"\Flagged") && !fake.Commands.Any(c => c.Contains("APPEND")),
                  "Gmail (IMAP finto): solo STORE +FLAGS \\Flagged, le date restano a Missiva");
        }
        finally
        {
            GmailProvider.TestPort = null;
        }

        requests.Clear();
        var testing = new TestingProvider(graph);
        // Le due righe "prova:" si cercano fra quelle scritte da qui in poi, non nelle ultime due del registro: il provider
        // di Gmail chiuso qui sopra finisce in sottofondo la richiesta che aveva in corso, e una sua riga "tempi:" può
        // cadere in mezzo (visto il 12 ottobre 2026, un giro su quattro della versione finale con gli strumenti di prova).
        var flagLog = Path.Combine(Store.Dir, "missiva.log");
        int flagLogBefore = File.Exists(flagLog) ? File.ReadLines(flagLog).Count() : 0;
        await testing.SetFlagDueAsync([message], new DateTime(2026, 9, 28), new DateTime(2026, 9, 28));
        await testing.CompleteFlagAsync([message]);
        var logged = File.ReadLines(flagLog).Skip(flagLogBefore).Where(l => l.Contains(" prova: ")).ToList();
        Check(requests.Count == 0 && logged.Count == 2 && logged[0].Contains("prova: contrassegnerei 1 messaggi di prova@outlook.example con scadenza 28/09/2026")
              && logged[1].Contains("prova: segnerei come completati 1 messaggi"),
              "nella prova Domani e Segna come completato vanno solo nel registro");

        var settings = new Settings { Flags = [new FlagDate { AccountId = "a", Key = "k", Due = new DateTime(2026, 9, 28), Reminder = new DateTime(2026, 9, 28, 9, 0, 0), Subject = "Offerta riservata Rossi", From = "Mario Bianchi" }] };
        var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJson.Default.Settings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.Settings);
        Check(json.Contains("\"dpapi:") && !json.Contains("Offerta riservata") && !json.Contains("Mario Bianchi") && back?.Flags.Single() is { Subject: "Offerta riservata Rossi", From: "Mario Bianchi" },
              "impostazioni.json: oggetto e mittente dei contrassegni cifrati (\"dpapi:\"), riletti uguali");

        // Correzioni del giro P4: la voce si ritrova con id e cartella (l'UID si ripete da una cartella all'altra), o con la
        // chiave che non cambia per un messaggio contrassegnato spostato; Graph con internetMessageId, non con l'id.
        var kept = Store.Settings.Flags.ToList();
        try
        {
            Store.Settings.Flags.Clear();
            var imap = new MailAccount { Kind = AccountKind.Imap, Email = "prova@studio.example", Id = "imap-contrassegni" };
            MessageSummary Mail(string id, string folder, string stable, bool flagged = true) => new() { Id = id, FolderId = folder, StableId = stable, Subject = "Fattura Rossi", IsFlagged = flagged };
            var invoice = Mail("57", "INBOX", "<fattura@rossi.example>");
            var noId = Mail("58", "INBOX", "");
            Flags.Remember(imap, invoice, null, DateTime.Today.AddDays(1), DateTime.Now.AddHours(1));
            Flags.Remember(imap, noId, null, DateTime.Today.AddDays(2), null);
            Flags.Forget(imap, Mail("57", "Sent", "<altro@studio.example>")); // Cancella contrassegno sull'UID 57 di Posta inviata
            Flags.Remember(imap, Mail("57", "Sent", "<altro@studio.example>", flagged: false), null, DateTime.Today, null); // Contrassegna Oggi sull'altro 57
            Check(Flags.Find(imap, invoice) is { Reminder: not null, Due: { } d1 } && d1 == DateTime.Today.AddDays(1) && Flags.DueOf(imap, Mail("58", "Sent", "")) == null
                  && Flags.DueOf(imap, Mail("57", "Sent", "<altro@studio.example>")) == DateTime.Today && Flags.Find(imap, noId)?.Due == DateTime.Today.AddDays(2)
                  && Flags.DueOf(imap, Mail("57", "INBOX", "<riusato@studio.example>")) == null && Store.Settings.Flags.Count == 3,
                  "contrassegni: lo stesso UID in un'altra cartella non prende né toglie scadenza e promemoria della fattura (anche senza Message-Id), un UID riusato nemmeno");
            Check(Flags.Follow(imap, "57", "INBOX", "12", "Archivio") && Flags.Find(imap, Mail("12", "Archivio", "<fattura@rossi.example>"))?.Reminder != null
                  && Flags.Find(imap, Mail("99", "Clienti", "<fattura@rossi.example>"))?.Due == DateTime.Today.AddDays(1)
                  && Flags.Find(imap, Mail("99", "Clienti", "<fattura@rossi.example>", flagged: false)) == null
                  && !Flags.Follow(imap, "58", "INBOX", "", "Archivio") && Flags.Find(imap, noId) != null,
                  "contrassegni: la voce segue il messaggio spostato da Missiva (id e cartella nuovi); spostato altrove lo ritrova la chiave, se è ancora contrassegnato");
            var outlook365 = new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@outlook.example", Id = "outlook-chiave" };
            var before = new MessageSummary { Id = "AAA", FolderId = "inbox", StableId = "AAA", InternetMessageId = "<preventivo@cliente.example>", IsFlagged = true, Subject = "Preventivo" };
            Flags.Remember(outlook365, before, null, null, DateTime.Now.AddHours(2));
            Store.Settings.Flags.Add(new FlagDate { AccountId = outlook365.Id, Key = "OLD", MessageId = "OLD", FolderId = "inbox", Due = DateTime.Today });
            Check(Store.Settings.Flags.Last(f => f.AccountId == outlook365.Id && f.MessageId == "AAA").Key == "<preventivo@cliente.example>"
                  && Flags.Find(outlook365, before.WithId("BBB"))?.Reminder != null
                  && Flags.Find(outlook365, new MessageSummary { Id = "OLD", FolderId = "inbox", StableId = "OLD", InternetMessageId = "<vecchio@cliente.example>", IsFlagged = true })?.Due == DateTime.Today,
                  "contrassegni di Graph: chiave internetMessageId (l'id cambia spostando il messaggio); le voci di prima, con l'id come chiave, si ritrovano ancora");
        }
        finally
        {
            Store.Settings.Flags.Clear();
            Store.Settings.Flags.AddRange(kept);
        }
    }

    /// <summary>
    /// Caselle condivise di Microsoft 365 (giro P4) con un gestore HTTP finto: senza il consenso alle caselle condivise
    /// nessuna richiesta (e la barra del consenso); con il consenso cartelle, elenco, corpo, ricerca, risposta e invio tutti
    /// su users/info@.../ con il token delle caselle condivise, niente Evidenziata; Invia come negato riprovato per conto di
    /// (from e sender), poi spiegato; 403 e casella inesistente con il testo italiano; nella prova il pulsante del consenso
    /// va solo nel registro; gli account personali restano su me/.
    /// </summary>
    static async Task SharedMailboxAsync()
    {
        Report.Add("--- Caselle condivise di Microsoft 365, gestore finto");
        var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.it", Tenant = MicrosoftAuth.Organizations, Id = "lavoro-condivise" };
        var info = new MailAccount { Kind = AccountKind.Microsoft, Email = "info@studio-prova.it", SharedOf = work.Id, Tenant = work.Tenant, Id = "info-condivisa" };
        Store.Settings.Accounts.Add(work);
        Store.Settings.Accounts.Add(info);
        var requests = new List<(HttpMethod Method, string Path, string Token, string Body)>();
        var sendDenied = 0; // quante /send rispondono ErrorSendAsDenied
        string? folderError = null; // il codice d'errore delle cartelle, per le prove dei 403 e 404
        HttpResponseMessage Error(HttpStatusCode status, string code) => new(status)
        {
            Content = new StringContent($"{{\"error\":{{\"code\":\"{code}\",\"message\":\"errore finto {code}\"}}}}", Encoding.UTF8, "application/json"),
        };
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((request.Method, path, request.Headers.Authorization?.Parameter ?? "", request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            if (folderError != null && path.Contains("mailFolders"))
                return folderError == "ErrorAccessDenied" ? Error(HttpStatusCode.Forbidden, folderError) : Error(HttpStatusCode.NotFound, folderError);
            if (path.EndsWith("/send") && sendDenied > 0)
            {
                sendDenied--;
                return Error(HttpStatusCode.Forbidden, "ErrorSendAsDenied");
            }
            JsonNode json = path.Contains("mailFolders?$top") ? new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = "inbox-info", ["displayName"] = "Inbox", ["unreadItemCount"] = 3 }) }
                : path.Contains("mailFolders/inbox?") ? new JsonObject { ["id"] = "inbox-info" }
                : path.Contains("/messages?") || path.Contains("/messages?$search") ? new JsonObject { ["value"] = new JsonArray(new JsonObject
                {
                    ["id"] = "m1", ["subject"] = "Richiesta preventivo", ["parentFolderId"] = "inbox-info", ["receivedDateTime"] = "2026-09-27T08:00:00Z",
                    ["from"] = new JsonObject { ["emailAddress"] = new JsonObject { ["name"] = "Cliente", ["address"] = "cliente@example.com" } },
                }) }
                : path.Contains("messages/m1?$select=body") ? new JsonObject { ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = "<p>Buongiorno</p>" } }
                : request.Method == HttpMethod.Post && path.EndsWith("/createReply") ? new JsonObject { ["id"] = "risposta-info", ["body"] = new JsonObject { ["content"] = "<html><body><p>Citazione</p></body></html>" } }
                : request.Method == HttpMethod.Post && path.EndsWith("/messages") ? new JsonObject { ["id"] = "bozza-info" }
                : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        const string mailbox = "users/info@studio-prova.it/";
        var shared = new GraphProvider(info);
        try
        {
            GraphProvider.TestSharedConsent = false;
            MailException? noConsent = null;
            try { await shared.GetFoldersAsync(); }
            catch (MailException ex) { noConsent = ex; }
            Check(noConsent is { Fix: AccountFix.Consent } && noConsent.Message.Contains("Consenti l'accesso alle caselle condivise") && requests.Count == 0,
                  "senza consenso alle caselle condivise: nessuna richiesta a users/, e la barra \"Consenti l'accesso alle caselle condivise\"");

            GraphProvider.TestSharedConsent = true;
            var folders = await shared.GetFoldersAsync();
            var inbox = folders.Single(f => f.Kind == FolderKind.Inbox);
            var page = await shared.GetMessagesAsync(inbox, shared.SupportsFocused ? true : null, null);
            var body = await shared.GetBodyAsync(page.Messages[0]);
            var found = await shared.SearchAsync(null, "preventivo");
            foreach (var r in requests.Take(4)) Report.Add($"      {r.Method} {r.Path[..Math.Min(r.Path.Length, 110)]}");
            Check(requests.All(r => r.Path.StartsWith(mailbox) && r.Token == "prova-condivise") && inbox.Id == "inbox-info" && inbox.Unread == 3
                  && page.Messages is [{ Subject: "Richiesta preventivo" }] && body.Html.Contains("Buongiorno") && found.Messages.Count == 1
                  && requests.Any(r => r.Path.StartsWith(mailbox + "messages?$search=")),
                  $"con il consenso cartelle, elenco, corpo e ricerca da {mailbox} con il token delle caselle condivise ({requests.Count} richieste)");
            Check(!shared.SupportsFocused && !requests.Any(r => r.Path.Contains("inferenceClassification")) && !Store.Settings.IsFocused(info)
                  && !shared.HasCalendar && !shared.HasAutoReplies && (await shared.GetCategoriesAsync()).Count == 0 && !requests.Any(r => r.Path.Contains("masterCategories")),
                  "nella casella condivisa niente Evidenziata, calendario, risposte automatiche né categorie di Outlook (che vorrebbero altri permessi)");

            requests.Clear();
            await shared.SendAsync(new OutgoingMessage { Mode = ComposeMode.Reply, Original = page.Messages[0], To = [new Address { Email = "cliente@example.com" }], Subject = "R: Richiesta preventivo", Html = "<p>Ecco il preventivo</p>" });
            Check(requests.Select(r => r.Method + " " + r.Path).SequenceEqual([$"POST {mailbox}messages/m1/createReply", $"PATCH {mailbox}messages/risposta-info", $"POST {mailbox}messages/risposta-info/send"])
                  && !requests[1].Body.Contains("\"from\""),
                  "Rispondi da un messaggio della casella condivisa: createReply, PATCH e send nella casella condivisa (parte da info@, copia nella sua Posta inviata)");

            requests.Clear();
            sendDenied = 1;
            await shared.SendAsync(new OutgoingMessage { To = [new Address { Email = "cliente@example.com" }], Subject = "Preventivo", Html = "<p>Salve</p>" });
            var behalf = requests.Where(r => r.Method == HttpMethod.Patch).Last();
            Check(requests.Count(r => r.Path.EndsWith("/send")) == 2 && behalf.Body.Contains("\"from\":{\"emailAddress\":{\"address\":\"info@studio-prova.it\"}}")
                  && behalf.Body.Contains("\"sender\":{\"emailAddress\":{\"address\":\"mario.rossi@studio-prova.it\"}}") && !requests.Any(r => r.Method == HttpMethod.Delete),
                  "Invia come negato: riprovato per conto di (from info@, sender l'account di lavoro), senza bozze eliminate");

            requests.Clear();
            sendDenied = 2;
            string? refused = null;
            try { await shared.SendAsync(new OutgoingMessage { To = [new Address { Email = "cliente@example.com" }], Subject = "Preventivo", Html = "<p>Salve</p>" }); }
            catch (MailException ex) { refused = ex.Message; }
            Check(refused?.Contains("Invia come") == true && refused.Contains("info@studio-prova.it") && requests.Any(r => r.Method == HttpMethod.Delete && r.Path == mailbox + "messages/bozza-info"),
                  $"né Invia come né per conto di: spiegato (\"{refused?[..Math.Min(refused.Length, 70)]}…\") e la bozza creata tolta");

            string? Folders(string code)
            {
                folderError = code;
                try { shared.GetFoldersAsync().GetAwaiter().GetResult(); return null; }
                catch (MailException ex) { return ex.Message; }
                finally { folderError = null; }
            }
            var denied = Folders("ErrorAccessDenied");
            var missing = Folders("ErrorInvalidUser");
            Check(denied?.Contains("Accesso completo") == true && missing?.Contains("non esiste") == true,
                  $"403 spiegato (\"{denied?[..Math.Min(denied.Length, 60)]}…\"), casella inesistente spiegata (\"{missing?[..Math.Min(missing.Length, 50)]}…\")");

            requests.Clear();
            await new TestingProvider(shared).AllowSharedAsync();
            var logged = File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).Last();
            Check(requests.Count == 0 && logged.Contains("prova: chiederei il consenso alle caselle condivise per info@studio-prova.it (con l'account mario.rossi@studio-prova.it)"),
                  "nella prova il pulsante del consenso va solo nel registro, nessuna richiesta né finestra");

            requests.Clear();
            await new GraphProvider(work).GetInboxUnreadAsync();
            Check(requests.Single().Path.StartsWith("me/mailFolders/inbox") && requests.Single().Token == "prova",
                  "l'account di lavoro (e quelli personali) restano su me/ con il token di sempre");
            Check(AccountTypes.Describe(info) == "Casella condivisa di Microsoft 365, aperta con mario.rossi@studio-prova.it" && shared.SignInAccount == work,
                  "nelle Impostazioni la casella condivisa dice con quale account si apre; i token sono quelli dell'account di lavoro");
        }
        finally
        {
            GraphProvider.TestSharedConsent = false;
            Store.Settings.Accounts.Remove(work);
            Store.Settings.Accounts.Remove(info);
        }
    }

    /// <summary>
    /// Altri indirizzi dell'account (identità, "Invia come"): la risposta sceglie l'indirizzo a cui era scritto il messaggio;
    /// via SMTP (Gmail) From e Reply-To dell'identità e la busta con l'identità; con Graph from e replyTo nel PATCH, e
    /// ErrorSendAsDenied spiegato in italiano.
    /// </summary>
    static async Task IdentitiesAsync()
    {
        Report.Add("--- Altri indirizzi (Invia come)");
        var info = new SendIdentity { Name = "Studio Rossi", Email = "info@studio-prova.it", ReplyTo = "segreteria@studio-prova.it" };
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "gmail-identita", Identities = [info] };
        MessageSummary To(params string[] to) => new() { Id = "1", FolderId = "INBOX", Subject = "Preventivo", To = to.Select(e => new Address { Email = e }).ToList() };
        Check(SendIdentity.For(account, To("Info@Studio-Prova.it")) == info && SendIdentity.For(account, To("prova@example.com", "info@studio-prova.it")) == null
              && SendIdentity.For(account, To("altro@example.com")) == null && SendIdentity.For(new MailAccount { Email = "x@y.it" }, To("info@studio-prova.it")) == null,
              "Rispondi dall'indirizzo a cui era scritto il messaggio (l'alias), non se c'era anche l'indirizzo principale");

        using var fake = new FakeImap();
        using var smtp = new FakeSmtp();
        GmailProvider.TestPort = fake.Port;
        GmailProvider.TestSmtpPort = smtp.Port;
        GmailProvider.TestSmtpCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            using var gmail = new GmailProvider(account);
            await gmail.SendAsync(new OutgoingMessage { To = [new Address { Email = "cliente@example.com" }], Subject = "Dall'alias", Html = "<p>Buongiorno</p>", Text = "Buongiorno", From = info });
            var sent = smtp.Received.Count == 1 ? MimeMessage.Load(new MemoryStream(smtp.Received[0])) : null;
            Check(sent?.From.Mailboxes.Single() is { Address: "info@studio-prova.it", Name: "Studio Rossi" } && sent.ReplyTo.Mailboxes.Single().Address == "segreteria@studio-prova.it"
                  && smtp.Commands.Any(c => c.StartsWith("MAIL FROM:<info@studio-prova.it>", StringComparison.OrdinalIgnoreCase)),
                  $"SMTP: From \"Studio Rossi <info@studio-prova.it>\", Reply-To della segreteria, busta dall'alias ({smtp.Commands.FirstOrDefault(c => c.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase))})");
        }
        finally
        {
            GmailProvider.TestPort = null;
            GmailProvider.TestSmtpPort = null;
            GmailProvider.TestSmtpCertificate = null;
        }

        var requests = new List<(HttpMethod Method, string Path, string Body)>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((request.Method, path, request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            if (path.EndsWith("/send"))
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"ErrorSendAsDenied\",\"message\":\"The user account which was used to submit this request does not have the right to send mail on behalf of the specified sending account.\"}}",
                                                Encoding.UTF8, "application/json"),
                };
            var json = request.Method == HttpMethod.Post && path == "me/messages" ? new JsonObject { ["id"] = "bozza-alias" } : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var work = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio-prova.it", DisplayName = "Mario Rossi", Tenant = MicrosoftAuth.Organizations });
        string? refused = null;
        try { await work.SendAsync(new OutgoingMessage { To = [new Address { Email = "cliente@example.com" }], Subject = "Da info@", Html = "<p>Salve</p>", From = info }); }
        catch (MailException ex) { refused = ex.Message; }
        var patch = requests.FirstOrDefault(r => r.Method == HttpMethod.Patch).Body ?? "";
        Check(patch.Contains("\"from\":{\"emailAddress\":{\"address\":\"info@studio-prova.it\",\"name\":\"Studio Rossi\"}}") && patch.Contains("\"replyTo\"")
              && patch.Contains("segreteria@studio-prova.it") && refused?.Contains("permesso Invia come") == true
              && requests.Any(r => r.Method == HttpMethod.Delete && r.Path == "me/messages/bozza-alias"),
              $"Graph: from e replyTo dell'altro indirizzo nel PATCH; ErrorSendAsDenied spiegato (\"{refused?[..Math.Min(refused.Length, 60)]}…\") e la bozza creata tolta");

        // Correzioni del giro P3: la bozza salvata da sola con l'alias, poi il Da torna all'indirizzo principale e si invia.
        requests.Clear();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((request.Method, path, request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            JsonNode json = request.Method == HttpMethod.Post && path == "me/messages" ? new JsonObject { ["id"] = "bozza-alias" }
                : request.Method == HttpMethod.Get && path.StartsWith("me/messages/bozza-alias?")
                    ? new JsonObject { ["id"] = "bozza-alias", ["isDraft"] = true, ["from"] = new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = "info@studio-prova.it" } } }
                    : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var studio = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio-prova.it", DisplayName = "Mario Rossi", Tenant = MicrosoftAuth.Organizations, Identities = [info] });
        var saved = await studio.SaveDraftAsync(new OutgoingMessage { To = [new Address { Email = "cliente@example.com" }], Subject = "Preventivo", Html = "<p>Salve</p>", From = info });
        await studio.SendAsync(new OutgoingMessage { To = [new Address { Email = "cliente@example.com" }], Subject = "Preventivo", Html = "<p>Salve</p>", Draft = saved });
        var back = requests.LastOrDefault(r => r.Method == HttpMethod.Patch).Body ?? "";
        Check(saved.Message.From.Email == "info@studio-prova.it" && back.Contains("\"from\":{\"emailAddress\":{\"address\":\"mario@studio-prova.it\",\"name\":\"Mario Rossi\"}}")
              && back.Contains("\"replyTo\":[]") && requests.Any(r => r.Method == HttpMethod.Post && r.Path == "me/messages/bozza-alias/send"),
              "Graph: bozza salvata dall'alias, poi inviata dall'indirizzo principale: from torna mario@studio-prova.it e Rispondi a si toglie");
    }

    /// <summary>
    /// Correzioni del giro P3: nella finestra di Microsoft aperta da un token scaduto o da "Consenti l'accesso a ..." si può
    /// scegliere un altro account; allora né HomeAccountId né il token restano sotto questo.
    /// </summary>
    static void ChosenAccount()
    {
        Report.Add("--- Account scelto nella finestra di Microsoft");
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "alex.prova@outlook.it", HomeAccountId = "casa-di-prima" };
        static Microsoft.Identity.Client.AuthenticationResult Chosen(string username) => new("token", false, "id", DateTimeOffset.Now.AddHours(1), DateTimeOffset.Now.AddHours(1),
            "tenant", new TestMsalAccount(username), "", ["User.Read"], Guid.NewGuid());
        MailException? refused = null;
        try { MicrosoftAuth.CheckChosen(account, Chosen("mario@lavoro-prova.it")); }
        catch (MailException ex) { refused = ex; }
        bool same = true;
        try { MicrosoftAuth.CheckChosen(account, Chosen("Alex.Prova@outlook.it")); }
        catch (MailException) { same = false; }
        Check(refused is { Fix: AccountFix.SignIn } && refused.Message.Contains("mario@lavoro-prova.it") && same && account.HomeAccountId == "casa-di-prima",
              "un altro account scelto nella finestra (token scaduto, consenso incrementale): rifiutato con \"Accedi di nuovo\", HomeAccountId com'era; lo stesso con le maiuscole va");
    }

    sealed class TestMsalAccount(string username) : Microsoft.Identity.Client.IAccount
    {
        public string Username => username;
        public string Environment => "login.microsoftonline.com";
        public Microsoft.Identity.Client.AccountId HomeAccountId => new("casa." + username, "casa", username);
    }

    /// <summary>
    /// Senza rete: gli errori di Windows detti "Non in linea" (10013 con il firewall), la copia su disco dei corpi cifrata
    /// (nessun oggetto o testo in chiaro), riletta con la data, il limite per account che toglie i più vecchi, e la copia
    /// degli elenchi cifrata ma ancora leggibile se scritta in chiaro dalle versioni di prima.
    /// </summary>
    static void OfflineTests()
    {
        Report.Add("--- Senza rete: copia su disco e \"Non in linea\"");
        Check(Network.Explain(new System.Net.Http.HttpRequestException("x", new SocketException(10013)))?.Contains("Portmaster") == true
              && Network.Explain(new MailException("imap.example.com non risponde", new SocketException(11001)))?.StartsWith("Non in linea") == true
              && Network.Explain(new MailException("password rifiutata")) == null && Network.IsOffline(new IOException("x", new SocketException(10060))),
              "10013 (firewall come Portmaster) e rete assente detti \"Non in linea\"; gli altri errori no");
        var account = new MailAccount { Kind = AccountKind.Imap, Email = "offline@example.com", Id = "prova-offline" };
        MailCache.Delete(account);
        var secret = "Oggetto riservato " + Guid.NewGuid().ToString("N")[..8];
        MessageSummary Message(string id) => new() { Id = id, FolderId = "INBOX", Subject = secret };
        var body = new MessageBody
        {
            Html = $"<p>{secret}: testo del messaggio</p>", To = [new Address { Email = "io@example.com" }],
            Attachments = [new Attachment { Id = "1", Name = "logo.png", IsInline = true, ContentId = "logo", Content = [1, 2, 3] }, new Attachment { Id = "2", Name = "fattura.pdf", Size = 5_000_000, Content = new byte[10] }],
        };
        BodyCache.Save(account, Message("1"), body);
        Task.WhenAll(BodyCache.TestWrites.ToArray()).GetAwaiter().GetResult();
        var folder = Path.Combine(Store.Dir, "cache", account.Id, "corpi");
        bool plain = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Any(f => Encoding.UTF8.GetString(File.ReadAllBytes(f)).Contains(secret) || Encoding.Unicode.GetString(File.ReadAllBytes(f)).Contains(secret));
        var back = BodyCache.Load(account, Message("1"));
        Check(!plain && back?.Html.Contains(secret) == true && back.OfflineCopy is { } at && DateTime.Now - at < TimeSpan.FromMinutes(1)
              && back.Attachments is [{ Content: [1, 2, 3] }, { Content: null, Name: "fattura.pdf" }],
              $"corpo su disco cifrato con DPAPI (l'oggetto non c'è in chiaro), riletto con la data della copia, l'immagine incorporata sì e l'allegato no (in chiaro {plain}, riletto {back != null}, data {back?.OfflineCopy}, allegati {back?.Attachments.Count}: {string.Join(",", back?.Attachments.Select(a => a.Content?.Length.ToString() ?? "-") ?? [])})");
        BodyCache.TestLimit = 1500;
        for (int i = 2; i <= 6; i++)
        {
            System.Threading.Thread.Sleep(30); // date di scrittura diverse
            BodyCache.Save(account, Message(i.ToString()), body);
            Task.WhenAll(BodyCache.TestWrites.ToArray()).GetAwaiter().GetResult();
        }
        var (count, bytes) = BodyCache.Size(account);
        Check(bytes <= 1500 && count >= 1 && BodyCache.Load(account, Message("6")) != null && BodyCache.Load(account, Message("1")) == null,
              $"limite per account: restano i più recenti ({count} corpi, {bytes} byte su 1500), il più vecchio esce");
        BodyCache.TestLimit = BodyCache.DefaultLimit;

        // Correzioni del giro P3: bozze, messaggi usciti dalla cartella, UID riusati, scadenza, account rimosso, posta certificata.
        void Written() => Task.WhenAll(BodyCache.TestWrites.ToArray()).GetAwaiter().GetResult();
        var draft = new MessageSummary { Id = "20", FolderId = "Drafts", Subject = secret, IsDraft = true };
        BodyCache.Save(account, draft, body);
        Written();
        Check(BodyCache.Load(account, draft) == null && BodyCache.Size(account).Count == count, "le bozze non vanno nella copia su disco (Modifica non riprende mai una versione vecchia)");
        MessageSummary Stable(string id, string stable, string folderId = "INBOX") => new() { Id = id, FolderId = folderId, StableId = stable, Subject = secret };
        BodyCache.Save(account, Stable("57", "<vecchio@example.com>"), body);
        Written();
        Check(BodyCache.Load(account, Stable("57", "<vecchio@example.com>")) != null && BodyCache.Load(account, Stable("57", "<nuovo@example.com>")) == null,
              "UID riusato (casella o cartella rifatta, UIDVALIDITY nuova): il messaggio 57 con un altro Message-Id non trova la copia del 57 vecchio");
        BodyCache.Forget(account, [Stable("57", "<vecchio@example.com>")]);
        BodyCache.Save(account, Stable("30", "a", "Trash"), body);
        BodyCache.Save(account, Stable("31", "b", "Trash"), body);
        Written();
        bool inTrash = BodyCache.Load(account, Stable("30", "a", "Trash")) != null;
        BodyCache.ForgetFolder(account, "Trash");
        Check(BodyCache.Load(account, Stable("57", "<vecchio@example.com>")) == null && inTrash
              && BodyCache.Load(account, Stable("30", "a", "Trash")) == null && BodyCache.Load(account, Stable("31", "b", "Trash")) == null,
              "Elimina, Sposta e Posponi tolgono la copia del messaggio; Svuota quelle di tutta la cartella");
        BodyCache.Save(account, Stable("40", "vecchio"), body);
        Written();
        foreach (var f in Directory.GetFiles(folder, "*.bin", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-31));
        File.WriteAllBytes(Path.Combine(folder, "formato-di-prima.bin"), [1, 2, 3]);
        BodyCache.TestForgetSizes(); // come dopo un riavvio: dall'audit, blocco 8, la pulizia non si fa più a ogni scrittura ma alla prima, oltre il limite e ogni ora
        BodyCache.Save(account, Stable("41", "nuovo"), body);
        Written();
        Check(BodyCache.Load(account, Stable("40", "vecchio")) == null && BodyCache.Load(account, Stable("41", "nuovo")) != null && !File.Exists(Path.Combine(folder, "formato-di-prima.bin")),
              "copie più vecchie di 30 giorni tolte, e i file della versione di prima (una cartella sola)");
        BodyCache.Save(account, Stable("50", "in-corso"), body);
        MailCache.Delete(account); // mentre la scrittura è in sottofondo
        Written();
        Check(!Directory.Exists(folder), "account rimosso durante una scrittura in sottofondo: la cartella dei corpi non rinasce");
        BodyCache.Save(account, Stable("51", "dopo"), body);
        Written();
        Check(BodyCache.Load(account, Stable("51", "dopo")) != null, "dopo la rimozione le scritture nuove vanno (stesso account aggiunto di nuovo, prove)");
        var envelope = new MessageBody
        {
            Html = "<p>Messaggio di posta certificata inviato da protocollo@pec.agenziaentrate.it</p>", OfflineCopy = DateTime.Now,
            Attachments = [new Attachment { Id = "2", Name = "daticert.xml", ContentType = "application/xml" }, new Attachment { Id = "3", Name = "smime.p7s", ContentType = "application/pkcs7-signature" }],
        };
        var pecPage = UI.ReadingPage.Build([new UI.PageEntry(Stable("60", "pec"), envelope, true, false, null, false)], true).Html;
        var onlinePage = UI.ReadingPage.Build([new UI.PageEntry(Stable("60", "pec"), new MessageBody { Html = "<p>x</p>" }, true, false, null, false)], true).Html;
        Check(pecPage.Contains("la firma si controlla solo in linea") && !onlinePage.Contains("la firma si controlla solo in linea"),
              "busta PEC dalla copia su disco (senza rete): fascia gialla \"la firma si controlla solo in linea\"");

        MailCache.SaveMessages(account, "INBOX", null, [Message("7")]);
        var cacheDir = Path.Combine(Store.Dir, "cache", account.Id);
        bool listPlain = Directory.GetFiles(cacheDir, "*.json").Any(f => Encoding.UTF8.GetString(File.ReadAllBytes(f)).Contains(secret));
        var plainFile = Directory.GetFiles(cacheDir, "*.json").Single();
        File.WriteAllText(plainFile, System.Text.Json.JsonSerializer.Serialize(new List<MessageSummary> { Message("8") }, typeof(List<MessageSummary>), CacheJson.Default));
        Check(!listPlain && MailCache.LoadMessages(account, "INBOX", null) is [{ Id: "8" }],
              "copia degli elenchi cifrata; quella scritta in chiaro dalle versioni di prima si legge ancora");
        MailCache.Delete(account);
        Check(!Directory.Exists(cacheDir), "tolta con l'account, corpi compresi");
    }

    /// <summary>I tipi di allegato che Outlook blocca e che mancavano: si aprono solo con la domanda e Salva tutti li salta.</summary>
    /// <summary>
    /// Il calendario di Google con un gestore HTTP finto (28 settembre 2026): senza il permesso del calendario nessuna
    /// richiesta; con il permesso calendarList e events.list di ognuno (pagine, calendario che Google non lascia leggere,
    /// nascosto), gli orari nell'ora di Windows, i giorni interi, le ricorrenze, la mia risposta, il link di Meet; poi nuovo
    /// evento (ora dell'orologio con il fuso IANA, RRULE con la fine in UTC, inviti), modifica dei soli campi cambiati (anche
    /// per la serie), risposta con l'elenco dei partecipanti intero, elimina e annulla, disponibilità, errori in italiano.
    /// </summary>
    static async Task GoogleCalendarAsync()
    {
        Report.Add("--- Calendario di Google, gestore finto");
        var requests = new List<(string Method, string Path, string Token, string Body)>();
        Func<HttpRequestMessage, string, HttpResponseMessage> answer = (_, _) => new HttpResponseMessage(HttpStatusCode.OK);
        static HttpResponseMessage Json(JsonNode json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        GoogleCalendar.UseTestHandler(new FakeHttp(request =>
        {
            var path = request.RequestUri!.PathAndQuery["/calendar/v3/".Length..];
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            requests.Add((request.Method.Method, path, request.Headers.Authorization?.Parameter ?? "", body));
            return answer(request, path);
        }));
        GoogleAuth.TestToken = "prova-google";
        const string Holidays = "it.italian#holiday@group.v.calendar.google.com";
        JsonNode Timed(string start, string end) => new JsonObject { ["dateTime"] = start };
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@gmail.com", GoogleScopes = "https://mail.google.com/ openid email profile" };
        using var google = new GmailProvider(account);
        var from = new DateTime(2026, 10, 19);
        var to = new DateTime(2026, 10, 26);
        try
        {
            var none = await google.GetCalendarAsync(from, to);
            Check(none == null && requests.Count == 0 && google.HasCalendar && !GoogleAuth.HasCalendar(account), "senza il permesso del calendario: null e nessuna richiesta");
            account.GoogleScopes += " " + GoogleAuth.CalendarScope;
            Check(GoogleAuth.HasCalendar(account), "il permesso del calendario si riconosce dagli ambiti dati da Google");

            answer = (_, path) =>
            {
                if (path.StartsWith("users/me/calendarList"))
                    return Json(new JsonObject { ["items"] = new JsonArray(
                        new JsonObject { ["id"] = "prova@gmail.com", ["summary"] = "prova@gmail.com", ["backgroundColor"] = "#9FE1E7", ["primary"] = true, ["accessRole"] = "owner",
                                         ["defaultReminders"] = new JsonArray(new JsonObject { ["method"] = "popup", ["minutes"] = 30 }) },
                        new JsonObject { ["id"] = "studio", ["summary"] = "Studio Rossi", ["summaryOverride"] = "Studio", ["backgroundColor"] = "#B99AFF", ["accessRole"] = "writer" },
                        new JsonObject { ["id"] = Holidays, ["summary"] = "Festività in Italia", ["backgroundColor"] = "non-un-colore", ["accessRole"] = "reader" },
                        new JsonObject { ["id"] = "nascosto", ["summary"] = "Nascosto", ["hidden"] = true }) });
                if (path.StartsWith("calendars/prova%40gmail.com/events?") && !path.Contains("pageToken"))
                    return Json(new JsonObject
                    {
                        ["nextPageToken"] = "pagina2",
                        ["items"] = new JsonArray(
                            new JsonObject { ["id"] = "e1", ["summary"] = "Colazione", ["start"] = Timed("2026-10-25T09:00:00+01:00", ""), ["end"] = Timed("2026-10-25T10:00:00+01:00", ""),
                                             ["organizer"] = new JsonObject { ["email"] = "prova@gmail.com", ["self"] = true }, ["reminders"] = new JsonObject { ["useDefault"] = true },
                                             ["description"] = "<p>Ordine del giorno</p><ul><li>Uno</li><li>Due &amp; tre</li></ul>" },
                            new JsonObject { ["id"] = "e2", ["summary"] = "Ferie", ["start"] = new JsonObject { ["date"] = "2026-10-20" }, ["end"] = new JsonObject { ["date"] = "2026-10-23" },
                                             ["transparency"] = "transparent", ["organizer"] = new JsonObject { ["email"] = "prova@gmail.com", ["self"] = true } },
                            new JsonObject { ["id"] = "serie_20261024T080000Z", ["summary"] = "Riunione di studio", ["recurringEventId"] = "serie",
                                             ["start"] = Timed("2026-10-24T10:00:00+02:00", ""), ["end"] = Timed("2026-10-24T11:00:00+02:00", ""),
                                             ["organizer"] = new JsonObject { ["email"] = "anna@studio.example", ["displayName"] = "Anna" }, ["hangoutLink"] = "https://meet.google.com/abc-defg-hij",
                                             ["reminders"] = new JsonObject { ["useDefault"] = false, ["overrides"] = new JsonArray(new JsonObject { ["method"] = "popup", ["minutes"] = 10 }) },
                                             ["attendees"] = new JsonArray(
                                                 new JsonObject { ["email"] = "anna@studio.example", ["displayName"] = "Anna", ["responseStatus"] = "accepted" },
                                                 new JsonObject { ["email"] = "prova@gmail.com", ["self"] = true, ["responseStatus"] = "needsAction" },
                                                 new JsonObject { ["email"] = "luca@studio.example", ["responseStatus"] = "tentative", ["optional"] = true }) },
                            new JsonObject { ["id"] = "e4", ["summary"] = "Annullato", ["status"] = "cancelled", ["start"] = Timed("2026-10-21T10:00:00+02:00", ""), ["end"] = Timed("2026-10-21T11:00:00+02:00", "") })
                    });
                if (path.StartsWith("calendars/prova%40gmail.com/events?"))
                    return Json(new JsonObject { ["items"] = new JsonArray(
                        new JsonObject { ["id"] = "e5", ["summary"] = "Seconda pagina", ["start"] = Timed("2026-10-22T11:00:00+02:00", ""), ["end"] = Timed("2026-10-22T12:00:00+02:00", ""),
                                         ["conferenceData"] = new JsonObject { ["entryPoints"] = new JsonArray(new JsonObject { ["entryPointType"] = "video", ["uri"] = "http://non-sicuro.example/meet" }) } }) });
                if (path.StartsWith("calendars/studio/events?"))
                    return Json(new JsonObject { ["items"] = new JsonArray(
                        new JsonObject { ["id"] = "s1", ["summary"] = "Scadenza", ["start"] = Timed("2026-10-23T15:00:00+02:00", ""), ["end"] = Timed("2026-10-23T16:00:00+02:00", ""), ["eventType"] = "outOfOffice",
                                         ["organizer"] = new JsonObject { ["email"] = "prova@gmail.com", ["self"] = true } }) });
                if (path.Contains("holiday"))
                    return Json(new JsonObject { ["error"] = new JsonObject { ["code"] = 403, ["message"] = "Forbidden", ["errors"] = new JsonArray(new JsonObject { ["reason"] = "forbidden" }) } }, HttpStatusCode.Forbidden);
                return Json(new JsonObject());
            };
            var data = await google.GetCalendarAsync(from, to);
            foreach (var r in requests) Report.Add($"      {r.Method} {r.Path[..Math.Min(r.Path.Length, 150)]}");
            var events = data!.Events.ToDictionary(e => e.Id);
            Check(requests[0].Path.StartsWith("users/me/calendarList?minAccessRole=freeBusyReader") && requests.All(r => r.Token == "prova-google")
                  && requests.Count(r => r.Path.Contains("/events?")) == 4
                  && requests.Where(r => r.Path.Contains("/events?")).All(r => r.Path.Contains("singleEvents=true") && r.Path.Contains("timeMin=2026-10-19T00%3A00%3A00%2B02%3A00") && r.Path.Contains("timeMax=2026-10-26T00%3A00%3A00%2B01%3A00"))
                  && requests.Any(r => r.Path.StartsWith("calendars/it.italian%23holiday%40group.v.calendar.google.com/events?")) && requests.Any(r => r.Path.Contains("pageToken=pagina2")),
                  "calendari, poi events.list di ognuno (singleEvents, l'intervallo +02:00 e +01:00 intorno al 25 ottobre, gli id con # e @ codificati) e la pagina dopo; nascosto escluso; il token di Google");
            Check(data.Calendars.Count == 3 && data.Calendars[0].IsDefault && data.Calendars[0].Color == "#9FE1E7" && data.Calendars[1].Name == "Studio" && data.Calendars[1].CanEdit
                  && !data.Calendars[2].CanEdit && data.Calendars[2].Color.Length == 7,
                  "tre calendari: il principale, Studio con il nome scelto (in scrittura), le festività in sola lettura con un colore di tavolozza (quello di Google non valido)");
            Check(events.Count == 5 && !events.ContainsKey("e4"), "cinque eventi (anche dalla seconda pagina), quello annullato no; il calendario che Google non lascia leggere non ferma gli altri");
            Check(events["e1"].Start == new DateTime(2026, 10, 25, 9, 0, 0) && events["serie_20261024T080000Z"].Start == new DateTime(2026, 10, 24, 10, 0, 0)
                  && events["e1"].ReminderOn && events["e1"].ReminderMinutes == 30 && events["e1"].IsOrganizer && events["e1"].Response == "organizer"
                  && events["e1"].Preview == "Ordine del giorno\nUno\nDue & tre",
                  "orari nell'ora di Windows (il 25 ottobre alle 9, il 24 alle 10), promemoria predefinito del calendario (30 minuti), organizzatore, descrizione in testo");
            Check(events["e2"].AllDay && events["e2"].Start == new DateTime(2026, 10, 20) && events["e2"].End == new DateTime(2026, 10, 23) && events["e2"].ShowAs == "free"
                  && Calendars.AllDayOn(data.Events, new DateTime(2026, 10, 22)).Any() && !Calendars.AllDayOn(data.Events, new DateTime(2026, 10, 23)).Any(),
                  "giorno intero di tre giorni (dal 20 al 22 compreso), libero");
            var series = events["serie_20261024T080000Z"];
            Check(series.IsRecurring && series.SeriesMasterId == "serie" && !series.IsOrganizer && series.Response == "none" && series.JoinUrl == "https://meet.google.com/abc-defg-hij"
                  && series.ReminderAt == new DateTime(2026, 10, 24, 9, 50, 0) && series.Attendees.Count == 3 && series.Attendees[0].Response == "accepted"
                  && series.Attendees[2].Response == "tentativelyAccepted" && series.Attendees[2].Optional && series.Organizer?.Email == "anna@studio.example",
                  "un'occorrenza: serie, non organizzatore e nessuna risposta mia, Google Meet, promemoria 10 minuti prima, tre partecipanti con le loro risposte");
            Check(events["e5"].JoinUrl == null && events["s1"].ShowAs == "oof" && events["s1"].CalendarId == "studio", "un link della riunione non https non vale; fuori sede; l'evento sa il suo calendario");

            // Nuovo evento.
            requests.Clear();
            answer = (request, path) => Json(request.Method == HttpMethod.Post ? new JsonObject { ["id"] = "evento-nuovo" } : new JsonObject());
            var zone = GoogleCalendar.Zone;
            var meeting = new NewEvent
            {
                CalendarId = "studio", Subject = "Riunione con il cliente", Start = new DateTime(2026, 10, 25, 9, 0, 0), End = new DateTime(2026, 10, 25, 10, 30, 0),
                Location = "Studio", ReminderMinutes = 15, ShowAs = "busy", Html = "<p>Ordine del giorno</p>",
                Repeat = EventRepeat.Weekly, RepeatDays = [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday], RepeatUntil = new DateTime(2026, 12, 18),
                Required = [new Address { Name = "Anna Bianchi", Email = "anna@cliente.example" }], Optional = [new Address { Email = "luca@studio.example" }],
            };
            var id = await google.CreateEventAsync(meeting);
            var post = requests.Single();
            var created = JsonNode.Parse(post.Body)!;
            Report.Add("      " + post.Method + " " + post.Path + " " + post.Body[..Math.Min(post.Body.Length, 420)]);
            Check(id == "evento-nuovo" && post.Method == "POST" && post.Path == "calendars/studio/events?sendUpdates=all"
                  && created["start"]?["dateTime"]?.GetValue<string>() == "2026-10-25T09:00:00" && created["start"]?["timeZone"]?.GetValue<string>() == zone
                  && created["end"]?["dateTime"]?.GetValue<string>() == "2026-10-25T10:30:00" && created["summary"]?.GetValue<string>() == "Riunione con il cliente"
                  && created["location"]?.GetValue<string>() == "Studio" && created["description"]?.GetValue<string>() == "<p>Ordine del giorno</p>" && created["transparency"]?.GetValue<string>() == "opaque",
                  $"POST calendars/studio/events con gli inviti (sendUpdates=all): il 25 ottobre (cambio d'ora) dalle 09:00 alle 10:30 con il fuso IANA ({zone})");
            var rule = created["recurrence"]?.AsArray().Select(r => r!.GetValue<string>()).ToList() ?? [];
            Check(rule.SequenceEqual(["RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR;UNTIL=20261218T225959Z"]), $"ripetizione ogni settimana lunedì, mercoledì e venerdì, con la fine alle 23:59 del 18 dicembre in UTC ({string.Join(";", rule)})");
            var invited = created["attendees"]?.AsArray() ?? [];
            Check(invited.Count == 2 && invited[0]?["email"]?.GetValue<string>() == "anna@cliente.example" && invited[0]?["displayName"]?.GetValue<string>() == "Anna Bianchi" && invited[0]?["optional"] == null
                  && invited[1]?["optional"]?.GetValue<bool>() == true && created["reminders"]?["useDefault"]?.GetValue<bool>() == false
                  && created["reminders"]?["overrides"]?[0]?["minutes"]?.GetValue<int>() == 15 && created["reminders"]?["overrides"]?[0]?["method"]?.GetValue<string>() == "popup",
                  "partecipanti necessari e facoltativi, promemoria a comparsa 15 minuti prima");

            requests.Clear();
            await google.CreateEventAsync(new NewEvent { Subject = "Ferie", Start = new DateTime(2026, 10, 26, 15, 0, 0), End = new DateTime(2026, 10, 26, 15, 0, 0), AllDay = true, ReminderMinutes = null, ShowAs = "free" });
            var allDay = JsonNode.Parse(requests.Single().Body)!;
            Check(requests.Single().Path == "calendars/primary/events?sendUpdates=all" && allDay["start"]?["date"]?.GetValue<string>() == "2026-10-26" && allDay["end"]?["date"]?.GetValue<string>() == "2026-10-27"
                  && allDay["start"]?["dateTime"] == null && allDay["transparency"]?.GetValue<string>() == "transparent" && allDay["reminders"]?["overrides"] == null
                  && allDay["recurrence"] == null && allDay["attendees"] == null,
                  "giornata intera con le sole date (fine il giorno dopo), libero, senza promemoria, nel calendario principale");

            // Modifica.
            requests.Clear();
            var colazione = events["e1"];
            var moved = Calendars.Moved(colazione, colazione.Start.AddHours(1), colazione.End.AddHours(1));
            await google.UpdateEventAsync(colazione, new NewEvent
            {
                CalendarId = moved.CalendarId, Subject = moved.Subject, Start = moved.Start, End = moved.End, Location = "Bar Centrale",
                ReminderMinutes = moved.ReminderMinutes, ShowAs = moved.ShowAs,
            }, series: false);
            var patch = requests.Single();
            var patched = JsonNode.Parse(patch.Body)!.AsObject();
            Check(patch.Method == "PATCH" && patch.Path == "calendars/prova%40gmail.com/events/e1?sendUpdates=all" && patched.Select(p => p.Key).Order().SequenceEqual(["end", "location", "start"])
                  && patched["start"]?["dateTime"]?.GetValue<string>() == "2026-10-25T10:00:00" && patched["location"]?.GetValue<string>() == "Bar Centrale",
                  "PATCH dei soli campi cambiati (luogo, inizio e fine): titolo, promemoria e mostra come non ci sono");
            requests.Clear();
            await google.UpdateEventAsync(colazione, Calendars.Moved(colazione, colazione.Start, colazione.End), series: false);
            Check(requests.Count == 0, "nessuna modifica: nessuna richiesta");

            // Tutta la serie: prima si legge l'inizio della serie, poi il PATCH va alla serie con l'orario spostato quanto l'occorrenza.
            requests.Clear();
            answer = (request, path) => path.Contains("/events/serie?fields") ? Json(new JsonObject { ["start"] = Timed("2026-09-03T10:00:00+02:00", ""), ["end"] = Timed("2026-09-03T11:00:00+02:00", "") }) : Json(new JsonObject());
            var occurrence = Calendars.Moved(series, series.Start.AddHours(1), series.End.AddHours(1));
            await google.UpdateEventAsync(series, new NewEvent { CalendarId = "prova@gmail.com", Subject = series.Subject, Start = occurrence.Start, End = occurrence.End, ReminderMinutes = 10,
                                                                 ShowAs = series.ShowAs, Required = occurrence.Required, Optional = occurrence.Optional }, series: true);
            Check(requests.Count == 2 && requests[0].Method == "GET" && requests[0].Path == "calendars/prova%40gmail.com/events/serie?fields=start,end"
                  && requests[1].Method == "PATCH" && requests[1].Path == "calendars/prova%40gmail.com/events/serie?sendUpdates=all"
                  && JsonNode.Parse(requests[1].Body)?["start"]?["dateTime"]?.GetValue<string>() == "2026-09-03T11:00:00"
                  && JsonNode.Parse(requests[1].Body)?["end"]?["dateTime"]?.GetValue<string>() == "2026-09-03T12:00:00",
                  "tutta la serie: si legge la serie, il PATCH va alla serie con l'inizio spostato di un'ora (3 settembre 11:00)");

            // Risposta a un evento: l'elenco dei partecipanti intero, con la mia risposta cambiata.
            requests.Clear();
            answer = (request, path) => request.Method == HttpMethod.Get
                ? Json(new JsonObject { ["attendees"] = new JsonArray(
                    new JsonObject { ["email"] = "anna@studio.example", ["organizer"] = true, ["responseStatus"] = "accepted" },
                    new JsonObject { ["email"] = "prova@gmail.com", ["self"] = true, ["responseStatus"] = "needsAction" },
                    new JsonObject { ["email"] = "luca@studio.example", ["optional"] = true, ["responseStatus"] = "declined" }) })
                : Json(new JsonObject());
            await google.RespondToEventAsync(series, InviteAnswer.Accept, " Ci sarò ", send: true);
            var reply = JsonNode.Parse(requests[1].Body)!["attendees"]!.AsArray();
            Check(requests.Count == 2 && requests[1].Method == "PATCH" && requests[1].Path == "calendars/prova%40gmail.com/events/serie_20261024T080000Z?sendUpdates=all"
                  && reply.Count == 3 && reply[1]?["responseStatus"]?.GetValue<string>() == "accepted" && reply[1]?["comment"]?.GetValue<string>() == "Ci sarò"
                  && reply[0]?["responseStatus"]?.GetValue<string>() == "accepted" && reply[0]?["organizer"]?.GetValue<bool>() == true && reply[2]?["responseStatus"]?.GetValue<string>() == "declined"
                  && reply[0]?["comment"] == null,
                  "Accetta: si legge l'elenco e si rimanda intero con la mia risposta e la nota, gli altri partecipanti come erano; sendUpdates=all");
            requests.Clear();
            await google.RespondToEventAsync(series, InviteAnswer.Decline, "", send: false);
            Check(requests[1].Path.EndsWith("?sendUpdates=none") && JsonNode.Parse(requests[1].Body)!["attendees"]![1]!["responseStatus"]!.GetValue<string>() == "declined" && JsonNode.Parse(requests[1].Body)!["attendees"]![1]!["comment"] == null,
                  "Rifiuta senza avvisare l'organizzatore: sendUpdates=none, nessuna nota");

            // Elimina e annulla.
            requests.Clear();
            answer = (_, _) => new HttpResponseMessage(HttpStatusCode.NoContent);
            await google.DeleteEventAsync(series, series: true);
            await google.DeleteEventAsync(series, series: false);
            await google.CancelEventAsync(events["s1"], "Rinviata", series: false);
            Check(requests.Select(r => r.Method + " " + r.Path).SequenceEqual([
                      "DELETE calendars/prova%40gmail.com/events/serie?sendUpdates=none",
                      "DELETE calendars/prova%40gmail.com/events/serie_20261024T080000Z?sendUpdates=none",
                      "DELETE calendars/studio/events/s1?sendUpdates=all"]),
                  "elimina la serie o la sola occorrenza senza avvisare nessuno; annulla la riunione avvisando i partecipanti (sendUpdates=all)");

            // Disponibilità.
            requests.Clear();
            answer = (_, _) => Json(new JsonObject { ["calendars"] = new JsonObject
            {
                ["anna@studio.example"] = new JsonObject { ["busy"] = new JsonArray(new JsonObject { ["start"] = "2026-10-25T09:30:00+01:00", ["end"] = "2026-10-25T10:30:00+01:00" }) },
                ["esterno@example.com"] = new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["domain"] = "global", ["reason"] = "notFound" }) },
            } });
            var schedule = await google.GetScheduleAsync(["anna@studio.example", "esterno@example.com"], new DateTime(2026, 10, 25, 9, 0, 0), new DateTime(2026, 10, 25, 11, 0, 0));
            var freeBusy = JsonNode.Parse(requests.Single().Body)!;
            Check(requests.Single().Path == "freeBusy" && freeBusy["items"]!.AsArray().Count == 2 && schedule![0].View == "0220" && schedule[0].Error == null
                  && schedule[1].Error?.Contains("non è condiviso") == true,
                  "disponibilità (freeBusy): una cifra per mezz'ora (libero, occupato dalle 9:30 alle 10:30, libero), e chi non condivide il calendario lo dice");

            // Errori di Google, in italiano.
            async Task<MailException?> Fails(HttpStatusCode status, string reason, string message)
            {
                answer = (_, _) => Json(new JsonObject { ["error"] = new JsonObject { ["code"] = (int)status, ["message"] = message, ["errors"] = new JsonArray(new JsonObject { ["reason"] = reason }) } }, status);
                try { await google.DeleteEventAsync(series, false); return null; }
                catch (MailException ex) { return ex; }
            }
            var off = await Fails(HttpStatusCode.Forbidden, "accessNotConfigured", "Google Calendar API has not been used in project 123 before or it is disabled.");
            var scope = await Fails(HttpStatusCode.Forbidden, "insufficientPermissions", "Request had insufficient authentication scopes.");
            var gone = await Fails(HttpStatusCode.NotFound, "notFound", "Not Found");
            Check(off?.Message.Contains("calendar-json.googleapis.com") == true && off.Status == 403 && scope?.Message.Contains("Consenti l'accesso al calendario") == true && gone?.Message.Contains("non c'è più") == true,
                  "errori di Google: API non attiva (con il link per abilitarla), permesso mancante (il pulsante), evento non più trovato");
            account.GoogleScopes = "https://mail.google.com/";
            string? noConsent = null;
            requests.Clear();
            try { await google.CreateEventAsync(meeting); }
            catch (MailException ex) { noConsent = ex.Message; }
            Check(noConsent?.Contains("Consenti l'accesso al calendario") == true && requests.Count == 0, "scrivere senza il permesso del calendario: nessuna richiesta, e lo si spiega");
        }
        finally
        {
            GoogleAuth.TestToken = null;
        }
    }

    /// <summary>
    /// I contatti di Google con un gestore HTTP finto (28 settembre 2026): senza il permesso dei contatti nessuna richiesta;
    /// con il permesso people/me/connections a pagine (1000, nextPageToken) con i soli campi della scheda; il passaggio al
    /// contatto di Missiva (nome principale, telefoni per tipo, fax esclusi, indirizzo in una riga, note, doppioni degli
    /// indirizzi); sola lettura; i suggerimenti dei destinatari li includono; gli errori di Google in italiano.
    /// </summary>
    static async Task GoogleContactsAsync()
    {
        Report.Add("--- Contatti di Google, gestore finto");
        var requests = new List<(string Path, string Token)>();
        Func<string, HttpResponseMessage> answer = _ => new HttpResponseMessage(HttpStatusCode.OK);
        static HttpResponseMessage Json(JsonNode json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        GoogleApi.UseTestHandler(new FakeHttp(request =>
        {
            var path = request.RequestUri!.PathAndQuery["/v1/".Length..];
            requests.Add((path, request.Headers.Authorization?.Parameter ?? ""));
            return answer(path);
        }));
        GoogleAuth.TestToken = "prova-google";
        JsonArray Values(params string[] values) => new(values.Select(v => (JsonNode)new JsonObject { ["value"] = v }).ToArray());
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@gmail.com", GoogleScopes = "https://mail.google.com/ " + GoogleAuth.CalendarScope };
        using var google = new GmailProvider(account);
        try
        {
            var none = await google.GetContactsAsync();
            Check(none == null && requests.Count == 0 && google.HasContacts && google.ContactsReadOnly && !GoogleAuth.HasContacts(account), "senza il permesso dei contatti: null e nessuna richiesta; sono di sola lettura");
            account.GoogleScopes += " " + GoogleAuth.ContactsScope;
            Check(GoogleAuth.HasContacts(account) && GoogleAuth.ExtraScopesOf(account).SequenceEqual([GoogleAuth.CalendarScope, GoogleAuth.ContactsScope]),
                  "il permesso dei contatti si riconosce dagli ambiti; un nuovo accesso rimette il calendario e i contatti che c'erano");

            answer = path => path.Contains("pageToken=p2")
                ? Json(new JsonObject { ["connections"] = new JsonArray(
                    new JsonObject { ["resourceName"] = "people/c5", ["names"] = new JsonArray(new JsonObject { ["displayName"] = "Solo Telefono" }), ["phoneNumbers"] = Values("+39 333 5") }) })
                : Json(new JsonObject
                {
                    ["nextPageToken"] = "p2",
                    ["connections"] = new JsonArray(
                        new JsonObject
                        {
                            ["resourceName"] = "people/c1",
                            ["names"] = new JsonArray(new JsonObject { ["displayName"] = "Anna Bianchi", ["givenName"] = "Anna", ["familyName"] = "Bianchi", ["metadata"] = new JsonObject { ["primary"] = true } }),
                            ["emailAddresses"] = Values("anna@studio.example", "ANNA@studio.example", "anna.b@casa.example"),
                            ["phoneNumbers"] = new JsonArray(
                                new JsonObject { ["value"] = "+39 347 1", ["type"] = "mobile" }, new JsonObject { ["value"] = "+39 051 2", ["type"] = "work" },
                                new JsonObject { ["value"] = "+39 051 3", ["type"] = "home" }, new JsonObject { ["value"] = "+39 051 9", ["type"] = "workFax" }),
                            ["organizations"] = new JsonArray(new JsonObject { ["name"] = "Studio Rossi", ["title"] = "Avvocato" }),
                            ["addresses"] = new JsonArray(new JsonObject { ["formattedValue"] = "Via Roma 1\n40100 Bologna BO" }),
                            ["biographies"] = Values("PEC anna@pec.example"),
                        },
                        new JsonObject
                        {
                            ["resourceName"] = "people/c2",
                            ["names"] = new JsonArray(new JsonObject { ["displayName"] = "Secondo", ["metadata"] = new JsonObject { ["primary"] = false } },
                                                      new JsonObject { ["displayName"] = "Principale", ["metadata"] = new JsonObject { ["primary"] = true } }),
                            ["phoneNumbers"] = new JsonArray(new JsonObject { ["value"] = "+39 02 1", ["type"] = "other" }, new JsonObject { ["value"] = "+39 02 2", ["type"] = "pager" }),
                        },
                        new JsonObject { ["resourceName"] = "people/c3", ["emailAddresses"] = Values("senza.nome@example.com") },
                        new JsonObject { ["resourceName"] = "people/c4" })
                });
            var all = await google.GetContactsAsync();
            foreach (var r in requests) Report.Add($"      {r.Path[..Math.Min(r.Path.Length, 170)]}");
            Check(requests.Count == 2 && requests.All(r => r.Token == "prova-google") && requests[0].Path.StartsWith("people/me/connections?personFields=" + GoogleContacts.PersonFields)
                  && requests[0].Path.Contains("pageSize=1000") && !requests[0].Path.Contains("pageToken") && requests[1].Path.Contains("pageToken=p2"),
                  "people/me/connections con i soli campi della scheda, 1000 alla volta, e la pagina dopo (nextPageToken), con il token di Google");
            Check(all is { Count: 4 } && all.All(c => c.Source == account.Id) && all[0].ServerId == "people/c1", "quattro contatti (quello vuoto no, quello con il solo telefono sì), con il loro identificativo di Google");
            var anna = all![0];
            Check(anna.Name == "Anna Bianchi" && anna.GivenName == "Anna" && anna.Surname == "Bianchi" && anna.Emails.SequenceEqual(["anna@studio.example", "anna.b@casa.example"])
                  && anna.Mobile == "+39 347 1" && anna.BusinessPhone == "+39 051 2" && anna.HomePhone == "+39 051 3" && anna.Company == "Studio Rossi" && anna.JobTitle == "Avvocato"
                  && anna.Address == "Via Roma 1, 40100 Bologna BO" && anna.Notes == "PEC anna@pec.example",
                  "la scheda: nome e cognome, indirizzi senza doppioni, cellulare, ufficio e casa per tipo (il fax no), azienda e ruolo, indirizzo in una riga, note");
            Check(all[1].Name == "Principale" && all[1].Mobile == "+39 02 1" && all[1].BusinessPhone == "+39 02 2" && all[2].Name == "senza.nome@example.com" && all[3].Name == "Solo Telefono" && all[3].Mobile == "+39 333 5",
                  "il nome segnato come principale, i telefoni di altro tipo nei posti liberi, senza nome vale l'indirizzo");

            // I suggerimenti dei destinatari includono la copia dei contatti di Google.
            Store.Settings.Accounts.Add(account);
            try
            {
                ContactBook.SetRemote(account.Id, all);
                Check(ContactBook.ForSuggestions().Any(c => c.Name == "Anna Bianchi" && c.Source == account.Id), "i suggerimenti dei destinatari includono i contatti di Google");
            }
            finally
            {
                Store.Settings.Accounts.Remove(account);
                ContactBook.ForgetRemote(account.Id);
            }

            // Errori di Google, in italiano.
            requests.Clear();
            answer = _ => Json(new JsonObject { ["error"] = new JsonObject { ["code"] = 403, ["message"] = "People API has not been used in project 123 before or it is disabled.",
                                                                               ["errors"] = new JsonArray(new JsonObject { ["reason"] = "accessNotConfigured" }) } }, HttpStatusCode.Forbidden);
            string? off = null;
            try { await google.GetContactsAsync(); }
            catch (MailException ex) { off = ex.Message; }
            answer = _ => Json(new JsonObject { ["error"] = new JsonObject { ["code"] = 403, ["message"] = "Request had insufficient authentication scopes.",
                                                                               ["errors"] = new JsonArray(new JsonObject { ["reason"] = "insufficientPermissions" }) } }, HttpStatusCode.Forbidden);
            string? scope = null;
            try { await google.GetContactsAsync(); }
            catch (MailException ex) { scope = ex.Message; }
            Check(off?.Contains("people.googleapis.com") == true && off.Contains("Google People API non è attiva") && scope?.Contains("Consenti l'accesso ai contatti") == true && scope.Contains("in Persone"),
                  "errori di Google: API non attiva (con il link per abilitarla) e permesso mancante (il pulsante in Persone)");
        }
        finally
        {
            GoogleAuth.TestToken = null;
        }
    }

    /// <summary>
    /// Loghi dei mittenti (28 settembre 2026): il dominio del marchio (niente posta personale né certificata), gli SVG che
    /// arrivano a SharpVectors (niente DTD, script, riferimenti esterni) e gli indirizzi a cui Missiva non si collega.
    /// </summary>
    static void SenderLogoTests()
    {
        Report.Add("--- Loghi dei mittenti");
        Check(SenderLogos.DomainOf("auto-confirm@amazon.it") == "amazon.it" && SenderLogos.DomainOf("newsletter@insider.sky.it") == "sky.it"
              && SenderLogos.DomainOf("news@email.amazon.co.uk") == "amazon.co.uk" && SenderLogos.DomainOf("Voucher@APPINX.Sovendus.com") == "sovendus.com",
              "dominio del marchio: sottodomini tolti, suffissi a due livelli tenuti");
        Check(SenderLogos.DomainOf("mario.rossi@gmail.com") == null && SenderLogos.DomainOf("anna@libero.it") == null
              && SenderLogos.DomainOf("studio@pec.aruba.it") == null && SenderLogos.DomainOf("x@legalmail.it") == null
              && SenderLogos.DomainOf("strano@[127.0.0.1]") == null && SenderLogos.DomainOf("a@1.2.3.4") == null && SenderLogos.DomainOf("senza-chiocciola") == null,
              "niente logo per posta personale, certificata e indirizzi strani");
        const string good = "<svg viewBox=\"0 0 10 10\" xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"/></defs>" +
                            "<rect fill=\"url(#g)\" width=\"10\" height=\"10\"/><use href=\"#g\"/></svg>";
        Check(SenderLogos.SafeSvg(good), "SVG di un logo con riferimenti interni: accettato");
        Check(!SenderLogos.SafeSvg("<!DOCTYPE svg [<!ENTITY a \"x\">]><svg/>") && !SenderLogos.SafeSvg("<svg><script>alert(1)</script></svg>")
              && !SenderLogos.SafeSvg("<svg><image href=\"https://traccia.example/p.png\"/></svg>")
              && !SenderLogos.SafeSvg("<svg><use xlink:href=\"https://x.example/a.svg#b\"/></svg>")
              && !SenderLogos.SafeSvg("<svg><rect style=\"fill:url(https://x.example/a)\"/></svg>")
              && !SenderLogos.SafeSvg("<svg><style>@import 'https://x.example/a.css';</style></svg>") && !SenderLogos.SafeSvg("<html>no</html>"),
              "SVG rifiutati: DTD, script, immagini e riferimenti esterni, @import, non SVG");
        Check(!SenderLogos.IsPublic(System.Net.IPAddress.Parse("127.0.0.1")) && !SenderLogos.IsPublic(System.Net.IPAddress.Parse("192.168.1.1"))
              && !SenderLogos.IsPublic(System.Net.IPAddress.Parse("10.0.0.5")) && !SenderLogos.IsPublic(System.Net.IPAddress.Parse("172.20.1.1"))
              && !SenderLogos.IsPublic(System.Net.IPAddress.Parse("169.254.10.1")) && !SenderLogos.IsPublic(System.Net.IPAddress.Parse("::1"))
              && !SenderLogos.IsPublic(System.Net.IPAddress.Parse("fe80::1")) && !SenderLogos.IsPublic(System.Net.IPAddress.Parse("fd00::1"))
              && !SenderLogos.IsPublic(System.Net.IPAddress.Parse("::ffff:192.168.0.1"))
              && SenderLogos.IsPublic(System.Net.IPAddress.Parse("151.101.1.1")) && SenderLogos.IsPublic(System.Net.IPAddress.Parse("2a00:1450:4002::65")),
              "loghi solo da indirizzi pubblici: niente rete di casa, niente PC stesso");
    }

    static void DangerousTypes()
    {
        Report.Add("--- Allegati pericolosi");
        string[] names = ["Contratto.library-ms", "ricerca.search-ms", "archivio.searchConnector-ms", "impostazioni.settingcontent-ms", "app.appx",
                          "app.appxbundle", "installa.appinstaller", "Assistenza.rdp", "Contratto.library-ms. "];
        Check(names.All(Safety.IsDangerous) && !Safety.IsDangerous("fattura.pdf") && !Safety.IsDangerous("foto.jpg"),
              $"pericolosi (domanda su Apri, saltati da Salva tutti, conferma negli allegati): {string.Join(", ", names.Select(n => n.Trim()))}; non fattura.pdf e foto.jpg");
    }

    static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) failures++;
        Report.Add((ok ? "ok    " : "NO    ") + what);
    }

    static string TempFile(string name, string text)
    {
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-bozze");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    static List<Address> To => [new Address { Name = "Mario Rossi", Email = "m.rossi@example.com" }];

    /// <summary>Un'immagine incollata nel testo: un PNG di un pixel.</summary>
    static InlineImage Picture => new(EditorImages.NewContentId(), "immagine.png", "image/png",
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

    // ---------------------------------------------------------------- testo della bozza ripresa

    /// <summary>
    /// Un PDF costruito a mano, con la tabella xref giusta: pagine con un rettangolo blu, e quello che si vuole aggiungere al
    /// catalogo (azioni di apertura) e alle pagine (link).
    /// </summary>
    static byte[] Pdf(int pages, string catalogExtra = "", string pageExtra = "", string mediaBox = "0 0 595 842")
    {
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pages).Select(i => $"{i + 4} 0 R"))}] /Count {pages} >>",
            "<< /Length 32 >>\nstream\n0 0 1 rg 72 600 200 100 re f\nendstream",
        };
        for (int i = 0; i < pages; i++) objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [{mediaBox}] /Contents 3 0 R {pageExtra} >>");
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) pdf.Append($"{o:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>
    /// L'anteprima dei PDF con il motore di Windows, nel processo dei PDF (PdfWorker, lo stesso eseguibile con
    /// --pdf-worker): un PDF con JavaScript all'apertura, uno con un'azione che avvia un programma e un link, uno da 500
    /// pagine (la prima senza disegnare le altre), uno rovinato (spiegato); il processo nel suo job, il documento
    /// ricaricato dopo la caduta del processo e dopo la chiusura per inattività, e il motore mai caricato in Missiva.
    /// </summary>
    static async Task PdfTestsAsync()
    {
        Report.Add("--- Anteprima dei PDF (Windows.Data.Pdf nel processo dei PDF)");
        using var worker = new PdfWorker();
        async Task<(int Pages, byte[] Png, long Ms)> First(byte[] pdf)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var (pages, _, _) = await worker.LoadAsync(pdf);
            var png = await worker.RenderAsync(0, 800);
            return (pages, png, clock.ElapsedMilliseconds);
        }
        static bool IsPng(byte[] b) => b.Length > 100 && b.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        static int Calculators() => System.Diagnostics.Process.GetProcessesByName("CalculatorApp").Length + System.Diagnostics.Process.GetProcessesByName("calc").Length;
        var calculators = Calculators();
        var plain = await First(Pdf(1));
        var firstId = worker.TestProcess?.Id;
        Check(worker.TestInJob(), $"processo dei PDF avviato ({plain.Ms} ms con l'avvio) e nel suo job (un solo processo attivo, chiuso con Missiva)");

        // L'AppContainer: un file dell'utente (in %TEMP%) non si legge, e nemmeno il PC stesso risponde (niente rete).
        var secret = TempFile("segreto.txt", "token finto");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var reach = await worker.TestReachAsync(secret, ((IPEndPoint)listener.LocalEndpoint).Port);
        bool knocked = listener.Pending();
        listener.Stop();
        Check(worker.TestInAppContainer() && reach.StartsWith("file non letto") && reach.Contains("non collegato") && !knocked,
              $"processo dei PDF in un AppContainer senza capability: {reach} (un file in %TEMP% e un server su 127.0.0.1)");

        // Le immagini dell'anteprima decodificate nel processo: un PNG grande rimpicciolito, una GIF, un PDF rifiutato.
        static int PngWidth(byte[] png) => PdfRender.PngWidth(png);
        var smaller = await worker.ImageAsync(plain.Png, 300);
        var gif = await worker.ImageAsync(Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"), 1440);
        string? notImage = null;
        try { await worker.ImageAsync(Pdf(1), 1440); }
        catch (Exception ex) { notImage = ex.Message; }
        Check(IsPng(smaller) && Math.Abs(PngWidth(plain.Png) - 800) <= 2 && PngWidth(smaller) == 300 && gif.Length > 8 && gif[0] == 0x89 && PngWidth(gif) == 1 && notImage != null,
              $"immagini nel processo dell'anteprima: PNG da {PngWidth(plain.Png)} pixel rimpicciolito a {PngWidth(smaller)}, GIF in PNG ({gif.Length} byte, " +
              $"{(gif.Length > 20 ? PngWidth(gif) : 0)} pixel), un PDF rifiutato ({notImage})");
        var script = await First(Pdf(1, "/OpenAction << /S /JavaScript /JS (app.launchURL\\(\"https://example.com/js\", true\\);) >>"));
        var launch = await First(Pdf(1, "/OpenAction << /S /Launch /F (calc.exe) >>",
                                     "/Annots [<< /Type /Annot /Subtype /Link /Rect [72 600 272 700] /A << /S /URI /URI (https://example.com/link) >> >>]"));
        Check(IsPng(plain.Png) && IsPng(script.Png) && IsPng(launch.Png) && plain.Pages == 1,
              $"PDF normale, con JavaScript all'apertura e con un'azione che avvia calc.exe e un link: pagine disegnate come PNG ({script.Ms}, {launch.Ms} ms), nessuna azione eseguita");
        Check(Math.Abs(PdfRender.PngWidth(plain.Png) - 800) <= 2 && Math.Abs(PdfRender.PngWidth(script.Png) - 800) <= 2,
              $"pagina chiesta larga 800 pixel: arriva larga {PdfRender.PngWidth(plain.Png)} (il motore vuole DIP: prima a 200% arrivava larga 1600, quattro volte la memoria)");
        var big = Pdf(500);
        var many = await First(big);
        Check(many.Pages == 500 && IsPng(many.Png) && many.Ms < 500, $"PDF da 500 pagine ({big.Length / 1024} KB): la prima disegnata in {many.Ms} ms senza le altre");
        string? broken = null;
        try { await First(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >>\ngarbage garbage")); }
        catch (Exception ex) { broken = PdfRender.Explain(ex); }
        // Un PDF troncato: o il motore lo disegna lo stesso, o è spiegato come rovinato; non vuoto, non un tempo scaduto.
        // (Prima il controllo chiedeva solo che ci fosse un testo, e c'era sempre: audit dell'11 ottobre, C-43.)
        string truncated;
        try { var cut = Pdf(3); var r = await First(cut[..(cut.Length / 3)]); truncated = IsPng(r.Png) ? "disegnato comunque" : "vuoto"; }
        catch (Exception ex) { truncated = PdfRender.Explain(ex); }
        Report.Add($"      troncato: {truncated}");
        Check(broken?.Contains("rovinato o incompleto") == true && (truncated == "disegnato comunque" || truncated.Contains("rovinato o incompleto")) && worker.TestProcess?.Id == firstId,
              $"PDF rovinato, spiegato, e il processo resta quello: {broken}");
        string? tooBig = null;
        try { await worker.LoadAsync(new byte[PdfWorker.MaxSize + 1]); }
        catch (Exception ex) { tooBig = PdfRender.Explain(ex); }
        Check(tooBig?.Contains("troppo grande") == true, $"PDF oltre 100 MB non mandato al processo: {tooBig}");

        // Pagine chieste scorrendo e poi uscite dalla vista: escono dalla coda senza essere disegnate.
        await worker.LoadAsync(Pdf(3));
        using (var scrolled = new CancellationTokenSource())
        {
            var queueClock = System.Diagnostics.Stopwatch.StartNew();
            var asked = Enumerable.Range(0, 10).Select(i => worker.RenderAsync(i % 3, 1600, i == 0 ? CancellationToken.None : scrolled.Token)).ToList();
            scrolled.Cancel();
            int drawn = 0, dropped = 0;
            foreach (var request in asked)
            {
                try { if (IsPng(await request)) drawn++; }
                catch (OperationCanceledException) { dropped++; }
            }
            Check(drawn == 1 && dropped == 9, $"dieci pagine chieste, nove uscite dalla vista mentre aspettavano: una disegnata e nove tolte dalla coda ({queueClock.ElapsedMilliseconds} ms)");
        }

        // Il processo cade (qui lo si chiude a forza): la pagina dopo riparte con un processo nuovo e lo stesso documento.
        await worker.LoadAsync(Pdf(3));
        worker.TestProcess?.Kill();
        worker.TestProcess?.WaitForExit(2000);
        byte[]? again = null;
        string? fell = null;
        try { again = await worker.RenderAsync(2, 400); }
        catch (Exception ex) { fell = ex.Message; }
        again ??= await worker.RenderAsync(2, 400);
        Check(IsPng(again) && worker.TestProcess?.Id != firstId, $"processo caduto: {(fell == null ? "la pagina arriva subito" : "una richiesta spiegata (" + fell + "), la dopo")} da un processo nuovo con il documento ricaricato");

        // Senza richieste si chiude da solo; la pagina dopo lo riavvia e ricarica il documento.
        worker.IdleTime = TimeSpan.FromMilliseconds(300);
        await worker.RenderAsync(0, 200);
        var idleId = worker.TestProcess?.Id ?? 0;
        await Task.Delay(1500);
        bool gone;
        try { using var old = System.Diagnostics.Process.GetProcessById(idleId); gone = old.HasExited; }
        catch (ArgumentException) { gone = true; }
        bool closed = worker.TestProcess == null && gone;
        var afterIdle = await worker.RenderAsync(1, 300);
        Check(closed && IsPng(afterIdle), "processo chiuso dopo l'inattività, poi riavviato con il documento ricaricato");
        worker.IdleTime = TimeSpan.FromMinutes(1);

        await Task.Delay(300);
        Check(Calculators() <= calculators, "nessun programma avviato dal PDF (la Calcolatrice di /Launch non si apre)");
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var engine = self.Modules.Cast<System.Diagnostics.ProcessModule>().Where(m => m.ModuleName is "Windows.Data.Pdf.dll" or "d2d1.dll" or "d3d11.dll").Select(m => m.ModuleName).ToList();
        Check(!engine.Contains("Windows.Data.Pdf.dll"), $"motore dei PDF mai caricato nel processo di Missiva ({(engine.Count == 0 ? "nessuno fra Windows.Data.Pdf, d2d1, d3d11" : string.Join(", ", engine))})");
    }

    /// <summary>
    /// Il calendario di Graph con un gestore HTTP finto: senza consenso nessuna richiesta; con il consenso i calendari, poi
    /// calendarView di ognuno con l'intervallo (anche a cavallo del 25 ottobre, con le due differenze da UTC), l'intestazione
    /// Prefer del fuso di Windows e le pagine successive; gli orari in UTC convertiti nell'ora di Windows, i giorni interi
    /// come date; e la disposizione nella giornata (sovrapposti in colonne, la mezzanotte in due giorni).
    /// </summary>
    static async Task CalendarAsync()
    {
        Report.Add("--- Calendario di Graph, gestore finto");
        var requests = new List<(string Path, string Prefer)>();
        static JsonObject Time(string at, string zone) => new() { ["dateTime"] = at, ["timeZone"] = zone };
        var local = TimeZoneInfo.Local.Id;
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((path, string.Join(" ", request.Headers.TryGetValues("Prefer", out var p) ? p : [])));
            JsonNode json = path.StartsWith("me/calendars?")
                ? new JsonObject { ["value"] = new JsonArray(
                    new JsonObject { ["id"] = "cal-1", ["name"] = "Calendario", ["hexColor"] = "#0F6CBD", ["isDefaultCalendar"] = true },
                    new JsonObject { ["id"] = "cal-2", ["name"] = "Studio", ["color"] = "lightGreen", ["isDefaultCalendar"] = false }) }
                : path.StartsWith("me/calendars/cal-1/calendarView") && !path.Contains("skip")
                ? new JsonObject
                {
                    ["value"] = new JsonArray(
                        new JsonObject { ["id"] = "e1", ["subject"] = "Colazione", ["start"] = Time("2026-10-25T09:00:00.0000000", local), ["end"] = Time("2026-10-25T10:00:00.0000000", local), ["type"] = "singleInstance" },
                        new JsonObject { ["id"] = "e2", ["subject"] = "In UTC dopo il cambio", ["start"] = Time("2026-10-25T08:00:00.0000000", "UTC"), ["end"] = Time("2026-10-25T09:00:00.0000000", "UTC") },
                        new JsonObject { ["id"] = "e3", ["subject"] = "In UTC prima del cambio", ["start"] = Time("2026-10-24T08:00:00.0000000", "UTC"), ["end"] = Time("2026-10-24T09:00:00.0000000", "UTC"),
                                         ["type"] = "occurrence", ["seriesMasterId"] = "serie", ["onlineMeeting"] = new JsonObject { ["joinUrl"] = "https://teams.microsoft.com/l/meetup-join/19%3aprova" },
                                         ["isReminderOn"] = true, ["reminderMinutesBeforeStart"] = 10,
                                         ["attendees"] = new JsonArray(new JsonObject { ["emailAddress"] = new JsonObject { ["name"] = "Anna", ["address"] = "anna@example.com" }, ["status"] = new JsonObject { ["response"] = "accepted" }, ["type"] = "required" }) },
                        new JsonObject { ["id"] = "e4", ["subject"] = "Ferie", ["isAllDay"] = true, ["start"] = Time("2026-10-20T00:00:00.0000000", "UTC"), ["end"] = Time("2026-10-23T00:00:00.0000000", "UTC") },
                        new JsonObject { ["id"] = "e5", ["subject"] = "Link non sicuro", ["start"] = Time("2026-10-21T15:00:00", local), ["end"] = Time("2026-10-21T16:00:00", local),
                                         ["onlineMeeting"] = new JsonObject { ["joinUrl"] = "http://riunione.example/entra" } }),
                    ["@odata.nextLink"] = "https://graph.microsoft.com/v1.0/me/calendars/cal-1/calendarView?skip=5",
                }
                : path.Contains("skip")
                ? new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = "e6", ["subject"] = "Seconda pagina", ["start"] = Time("2026-10-22T11:00:00", local), ["end"] = Time("2026-10-22T12:00:00", local) }) }
                : new JsonObject { ["value"] = new JsonArray() };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@example.com" });
        var from = new DateTime(2026, 10, 19);
        var to = new DateTime(2026, 10, 26);
        try
        {
            GraphProvider.TestCalendarConsent = false;
            var none = await graph.GetCalendarAsync(from, to);
            Check(none == null && requests.Count == 0, "senza consenso al calendario: null e nessuna richiesta");

            GraphProvider.TestCalendarConsent = true;
            var data = await graph.GetCalendarAsync(from, to);
            foreach (var (path, prefer) in requests) Report.Add($"      {path[..Math.Min(path.Length, 160)]} | {prefer}");
            var views = requests.Where(r => r.Path.Contains("/calendarView")).ToList();
            Check(requests[0].Path.StartsWith("me/calendars?$select=id,name,hexColor,color,isDefaultCalendar") && views.Count == 3
                  && views.All(v => v.Path.Contains("startDateTime=2026-10-19T00:00:00+02:00") || v.Path.Contains("skip"))
                  && views.Where(v => !v.Path.Contains("skip")).All(v => v.Path.Contains("endDateTime=2026-10-26T00:00:00+01:00") && v.Path.Contains("$select=" + Calendars.EventFields))
                  && views.All(v => v.Prefer == $"outlook.timezone=\"{local}\""),
                  $"calendari, poi calendarView di ognuno con l'intervallo (+02:00 e +01:00 intorno al 25 ottobre), $select stretto, Prefer del fuso di Windows ({local}) e la pagina dopo");
            var events = data!.Events.ToDictionary(e => e.Id);
            Check(data.Calendars.Count == 2 && data.Calendars[0].Color == "#0F6CBD" && data.Calendars[1].Color == "#5CB85C" && events.Count == 6,
                  "due calendari con i loro colori, sei eventi (anche dalla seconda pagina)");
            Check(events["e1"].Start == new DateTime(2026, 10, 25, 9, 0, 0) && events["e2"].Start == new DateTime(2026, 10, 25, 9, 0, 0)
                  && events["e3"].Start == new DateTime(2026, 10, 24, 10, 0, 0) && events["e3"].End == new DateTime(2026, 10, 24, 11, 0, 0),
                  $"orari nell'ora di Windows: 08:00 UTC del 25 ottobre sono le 9 (ora solare), del 24 le 10 (ora legale): {events["e2"].Start:HH:mm}, {events["e3"].Start:HH:mm}");
            Check(events["e4"].AllDay && events["e4"].Start == new DateTime(2026, 10, 20) && events["e4"].End == new DateTime(2026, 10, 23)
                  && Calendars.AllDayOn(data.Events, new DateTime(2026, 10, 22)).Any() && !Calendars.AllDayOn(data.Events, new DateTime(2026, 10, 23)).Any(),
                  "giorno intero di tre giorni: dal 20 al 22 compreso, come date");
            Check(events["e3"].IsRecurring && events["e3"].JoinUrl?.StartsWith("https://teams.microsoft.com/") == true && events["e3"].ReminderAt == new DateTime(2026, 10, 24, 9, 50, 0)
                  && events["e3"].Attendees is [{ Response: "accepted" }] && events["e5"].JoinUrl == null,
                  "ricorrenza, Partecipa di Teams, promemoria 10 minuti prima, partecipanti; un link della riunione non https non vale");

            // Disposizione nella giornata.
            var day = new DateTime(2026, 10, 21);
            CalendarEvent At(string id, double from, double to) => new() { Id = id, Start = day.AddHours(from), End = day.AddHours(to) };
            var placed = Calendars.Day([At("a", 10, 11.5), At("b", 10.5, 11), At("c", 11, 12), At("d", 14, 15), At("notte", 22.5, 25.5)], day).ToDictionary(p => p.Event.Id);
            var tomorrow = Calendars.Day([At("notte", 22.5, 25.5)], day.AddDays(1)).Single();
            Check(placed["a"].Column == 0 && placed["b"].Column == 1 && placed["c"].Column == 1 && placed["a"].Columns == 2 && placed["d"].Columns == 1
                  && Math.Abs(placed["notte"].Bottom - 1) < 1e-9 && tomorrow.Top == 0 && Math.Abs(tomorrow.Bottom * 24 - 1.5) < 1e-9,
                  "sovrapposti in due colonne, quello del pomeriggio largo tutto; il volo delle 22:30 finisce a mezzanotte e riprende il giorno dopo fino all'1:30");
        }
        finally
        {
            GraphProvider.TestCalendarConsent = false;
        }
    }

    /// <summary>
    /// Nuovo evento e risposte dal calendario con un gestore HTTP finto: senza consenso nessuna richiesta; con il consenso
    /// POST me/calendars/{id}/events con le ore dell'orologio e il fuso di Windows (il 25 ottobre, con il cambio d'ora, le 9
    /// restano le 9), la giornata intera come date, la ripetizione ogni settimana lunedì, mercoledì e venerdì, i partecipanti
    /// necessari e facoltativi, Teams solo per gli account di lavoro; accept con la nota; nella prova solo il registro.
    /// </summary>
    static async Task CalendarWriteAsync()
    {
        Report.Add("--- Calendario che scrive, gestore finto");
        var requests = new List<(HttpMethod Method, string Path, string Token, string Body)>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((request.Method, path, request.Headers.Authorization?.Parameter ?? "", request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            var json = request.Method == HttpMethod.Post && path.EndsWith("/events") ? new JsonObject { ["id"] = "evento-nuovo" } : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var local = TimeZoneInfo.Local.Id;
        var personal = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@outlook.example" });
        var work = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio.example", Tenant = MicrosoftAuth.Organizations });
        var meeting = new NewEvent
        {
            CalendarId = "cal-2", Subject = "Riunione con il cliente", Start = new DateTime(2026, 10, 25, 9, 0, 0), End = new DateTime(2026, 10, 25, 10, 30, 0),
            Location = "Studio", ReminderMinutes = 15, ShowAs = "busy", Html = "<p>Ordine del giorno</p>",
            Repeat = EventRepeat.Weekly, RepeatDays = [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday], RepeatUntil = new DateTime(2026, 12, 18),
            Required = [new Address { Name = "Anna Bianchi", Email = "anna@cliente.example" }], Optional = [new Address { Email = "luca@studio.example" }],
        };
        try
        {
            GraphProvider.TestCalendarConsent = false;
            string? refused = null;
            try { await personal.CreateEventAsync(meeting); }
            catch (MailException ex) { refused = ex.Message; }
            Check(refused?.Contains("Consenti l'accesso al calendario") == true && requests.Count == 0, "senza consenso al calendario: nessuna richiesta, e lo si spiega");

            GraphProvider.TestCalendarConsent = true;
            var id = await personal.CreateEventAsync(meeting);
            var post = requests.Single();
            var body = JsonNode.Parse(post.Body)!;
            Report.Add("      " + post.Method + " " + post.Path + " " + post.Body[..Math.Min(post.Body.Length, 400)]);
            Check(id == "evento-nuovo" && post.Method == HttpMethod.Post && post.Path == "me/calendars/cal-2/events" && post.Token == "prova-calendario"
                  && body["start"]?["dateTime"]?.GetValue<string>() == "2026-10-25T09:00:00" && body["start"]?["timeZone"]?.GetValue<string>() == local
                  && body["end"]?["dateTime"]?.GetValue<string>() == "2026-10-25T10:30:00" && body["isAllDay"]?.GetValue<bool>() == false,
                  $"POST me/calendars/cal-2/events con il token del calendario: il 25 ottobre (cambio d'ora) dalle 09:00 alle 10:30 nel fuso di Windows ({local})");
            var days = body["recurrence"]?["pattern"]?["daysOfWeek"]?.AsArray().Select(d => d!.GetValue<string>()).ToList() ?? [];
            Check(body["recurrence"]?["pattern"]?["type"]?.GetValue<string>() == "weekly" && days.SequenceEqual(["monday", "wednesday", "friday"])
                  && body["recurrence"]?["range"]?["type"]?.GetValue<string>() == "endDate" && body["recurrence"]?["range"]?["startDate"]?.GetValue<string>() == "2026-10-25"
                  && body["recurrence"]?["range"]?["endDate"]?.GetValue<string>() == "2026-12-18",
                  "ripetizione ogni settimana lunedì, mercoledì e venerdì fino al 18 dicembre");
            var attendees = body["attendees"]?.AsArray() ?? [];
            Check(attendees.Count == 2 && attendees[0]?["type"]?.GetValue<string>() == "required" && attendees[0]?["emailAddress"]?["address"]?.GetValue<string>() == "anna@cliente.example"
                  && attendees[1]?["type"]?.GetValue<string>() == "optional" && body["isOnlineMeeting"] == null
                  && body["reminderMinutesBeforeStart"]?.GetValue<int>() == 15 && body["location"]?["displayName"]?.GetValue<string>() == "Studio"
                  && body["body"]?["content"]?.GetValue<string>() == "<p>Ordine del giorno</p>",
                  "partecipanti necessari e facoltativi (una riunione: gli inviti li manda Exchange), promemoria, luogo, descrizione; niente Teams se non chiesto");

            requests.Clear();
            await personal.CreateEventAsync(new NewEvent { Subject = "Ferie", Start = new DateTime(2026, 10, 26, 15, 0, 0), End = new DateTime(2026, 10, 26, 15, 0, 0), AllDay = true, ReminderMinutes = null, ShowAs = "oof" });
            var allDay = JsonNode.Parse(requests.Single().Body)!;
            Check(requests.Single().Path == "me/events" && allDay["isAllDay"]?.GetValue<bool>() == true && allDay["start"]?["dateTime"]?.GetValue<string>() == "2026-10-26T00:00:00"
                  && allDay["end"]?["dateTime"]?.GetValue<string>() == "2026-10-27T00:00:00" && allDay["isReminderOn"]?.GetValue<bool>() == false
                  && allDay["showAs"]?.GetValue<string>() == "oof" && allDay["recurrence"] == null && allDay["attendees"] == null,
                  "giornata intera con le sole date (dalla mezzanotte a quella dopo), senza promemoria, fuori sede, nel calendario predefinito");

            requests.Clear();
            string? noTeams = null;
            try { await personal.CreateEventAsync(new NewEvent { Subject = "Teams", Start = meeting.Start, End = meeting.End, OnlineMeeting = true, Required = meeting.Required }); }
            catch (MailException ex) { noTeams = ex.Message; }
            await work.CreateEventAsync(new NewEvent { Subject = "Teams", Start = meeting.Start, End = meeting.End, OnlineMeeting = true, Required = meeting.Required });
            var teams = JsonNode.Parse(requests.Single().Body)!;
            Check(noTeams != null && teams["isOnlineMeeting"]?.GetValue<bool>() == true && teams["onlineMeetingProvider"]?.GetValue<string>() == "teamsForBusiness",
                  "riunione di Teams solo per l'account di lavoro (isOnlineMeeting, teamsForBusiness); per quello personale spiegato, nessuna richiesta");

            requests.Clear();
            var invited = new CalendarEvent { Id = "evento-invito", Subject = "Revisione", Organizer = new Address { Email = "anna@cliente.example" }, Response = "notResponded" };
            await personal.RespondToEventAsync(invited, InviteAnswer.Accept, " Ci sarò ", send: true);
            var accept = requests.Single();
            Check(accept.Method == HttpMethod.Post && accept.Path == "me/events/evento-invito/accept" && JsonNode.Parse(accept.Body)?["comment"]?.GetValue<string>() == "Ci sarò"
                  && JsonNode.Parse(accept.Body)?["sendResponse"]?.GetValue<bool>() == true,
                  "Accetta dai dettagli dell'evento: POST me/events/{id}/accept con la nota e la risposta all'organizzatore");

            requests.Clear();
            var testing = new TestingProvider(personal);
            await testing.CreateEventAsync(meeting);
            await testing.RespondToEventAsync(invited, InviteAnswer.Decline, "", send: false);
            var logged = File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).TakeLast(2).ToList();
            Check(requests.Count == 0 && logged[0].Contains("prova: creerei l'evento \"Riunione con il cliente\"") && logged[0].Contains("con 2 partecipanti")
                  && logged[1].Contains("prova: risponderei rifiuta all'evento \"Revisione\""),
                  "nella prova l'evento nuovo e la risposta vanno solo nel registro, nessuna richiesta");
        }
        finally
        {
            GraphProvider.TestCalendarConsent = false;
        }
    }

    /// <summary>
    /// Calendario che modifica (giro P4) con un gestore HTTP finto: senza consenso nessuna richiesta; PATCH dei soli campi
    /// cambiati con il fuso di Windows il 25 ottobre, nessuna richiesta senza modifiche; tutta la serie spostata dall'inizio
    /// della serie letto prima, e mai di giorno; partecipanti tutti; DELETE dell'occorrenza e della serie; cancel con
    /// Comment; Rifiuta con sendResponse; getSchedule con l'intervallo di mezz'ora e Prefer (e mai con gli account
    /// personali); nella prova solo il registro.
    /// </summary>
    static async Task CalendarEditAsync()
    {
        Report.Add("--- Calendario che modifica, sposta e annulla, gestore finto");
        var requests = new List<(HttpMethod Method, string Path, string Token, string Body, string Prefer)>();
        var local = TimeZoneInfo.Local.Id;
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((request.Method, path, request.Headers.Authorization?.Parameter ?? "", request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "",
                          string.Join(" ", request.Headers.TryGetValues("Prefer", out var p) ? p : [])));
            JsonNode json = path.StartsWith("me/events/serie-1?")
                ? new JsonObject { ["start"] = new JsonObject { ["dateTime"] = "2026-10-05T09:00:00.0000000", ["timeZone"] = local },
                                   ["end"] = new JsonObject { ["dateTime"] = "2026-10-05T10:00:00.0000000", ["timeZone"] = local } }
                : path == "me/calendar/getSchedule"
                ? new JsonObject { ["value"] = new JsonArray(
                    new JsonObject { ["scheduleId"] = "anna@studio.example", ["availabilityView"] = "000022220000000000000000" },
                    new JsonObject { ["scheduleId"] = "esterno@cliente.example", ["availabilityView"] = "", ["error"] = new JsonObject { ["message"] = "calendario non condiviso" } }) }
                : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var personal = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@outlook.example" });
        var work = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio.example", Tenant = MicrosoftAuth.Organizations });
        var anna = new Address { Name = "Anna Bianchi", Email = "anna@studio.example" };
        var occurrence = new CalendarEvent
        {
            Id = "occ-1", SeriesMasterId = "serie-1", IsRecurring = true, IsOrganizer = true, CalendarId = "cal-1", Subject = "Punto settimanale",
            Start = new DateTime(2026, 10, 25, 9, 0, 0), End = new DateTime(2026, 10, 25, 10, 0, 0), Location = "Studio", ReminderOn = true, ReminderMinutes = 15,
            ShowAs = "busy", Attendees = [new Attendee(anna, "accepted", false)],
        };
        NewEvent Changed(Func<NewEvent, NewEvent>? change = null)
        {
            var e = Calendars.Moved(occurrence, occurrence.Start, occurrence.End);
            return change?.Invoke(e) ?? e;
        }
        NewEvent With(DateTime? start = null, DateTime? end = null, string? location = null, List<Address>? optional = null) => new()
        {
            CalendarId = occurrence.CalendarId, Subject = occurrence.Subject, Start = start ?? occurrence.Start, End = end ?? occurrence.End, Location = location ?? occurrence.Location,
            ReminderMinutes = 15, ShowAs = "busy", Required = [anna], Optional = optional ?? new(),
        };
        try
        {
            GraphProvider.TestCalendarConsent = false;
            var refused = 0;
            foreach (var action in new Func<Task>[] { () => personal.UpdateEventAsync(occurrence, With(location: "Altrove"), false), () => personal.DeleteEventAsync(occurrence, false),
                                                       () => personal.CancelEventAsync(occurrence, "", false) })
            {
                try { await action(); }
                catch (MailException ex) when (ex.Message.Contains("Consenti l'accesso al calendario")) { refused++; }
            }
            Check(refused == 3 && requests.Count == 0 && await work.GetScheduleAsync(["anna@studio.example"], occurrence.Start, occurrence.End) == null && requests.Count == 0,
                  "senza consenso al calendario: Modifica, Elimina, Annulla riunione e disponibilità senza nessuna richiesta");

            GraphProvider.TestCalendarConsent = true;
            await personal.UpdateEventAsync(occurrence, Changed(), false);
            Check(requests.Count == 0, "Salva senza modifiche: nessuna richiesta");

            await personal.UpdateEventAsync(occurrence, With(end: new DateTime(2026, 10, 25, 10, 30, 0), location: "Sala Rossa"), false);
            var patch = requests.Single();
            var body = JsonNode.Parse(patch.Body)!.AsObject();
            Report.Add($"      {patch.Method} {patch.Path} {patch.Body}");
            Check(patch.Method == HttpMethod.Patch && patch.Path == "me/events/occ-1" && patch.Token == "prova-calendario"
                  && body.Select(k => k.Key).OrderBy(k => k).SequenceEqual(["end", "location", "start"])
                  && body["start"]?["dateTime"]?.GetValue<string>() == "2026-10-25T09:00:00" && body["end"]?["dateTime"]?.GetValue<string>() == "2026-10-25T10:30:00"
                  && body["end"]?["timeZone"]?.GetValue<string>() == local && body["location"]?["displayName"]?.GetValue<string>() == "Sala Rossa",
                  $"Modifica di un'occorrenza: PATCH me/events/occ-1 dei soli luogo, inizio e fine, il 25 ottobre nel fuso di Windows ({local}); titolo, partecipanti e descrizione non si toccano");

            requests.Clear();
            await personal.UpdateEventAsync(occurrence, With(start: new DateTime(2026, 10, 25, 9, 30, 0), end: new DateTime(2026, 10, 25, 11, 0, 0)), true);
            var series = requests.Last();
            var seriesBody = JsonNode.Parse(series.Body)!;
            Check(requests.Count == 2 && requests[0].Method == HttpMethod.Get && requests[0].Path.StartsWith("me/events/serie-1?$select=start,end,isAllDay")
                  && requests[0].Prefer == $"outlook.timezone=\"{local}\"" && series.Method == HttpMethod.Patch && series.Path == "me/events/serie-1"
                  && seriesBody["start"]?["dateTime"]?.GetValue<string>() == "2026-10-05T09:30:00" && seriesBody["end"]?["dateTime"]?.GetValue<string>() == "2026-10-05T11:00:00",
                  "tutta la serie: l'inizio della serie letto (5 ottobre alle 9), poi PATCH me/events/serie-1 spostata di mezz'ora con la durata nuova (9:30-11:00)");

            requests.Clear();
            string? otherDay = null;
            try { await personal.UpdateEventAsync(occurrence, With(start: new DateTime(2026, 10, 26, 9, 0, 0), end: new DateTime(2026, 10, 26, 10, 0, 0)), true); }
            catch (MailException ex) { otherDay = ex.Message; }
            Check(otherDay?.Contains("solo di orario") == true && requests.Count == 0, "tutta la serie in un altro giorno: spiegato, nessuna richiesta (i giorni della ripetizione restano)");

            requests.Clear();
            await personal.UpdateEventAsync(occurrence, With(optional: [new Address { Email = "luca@cliente.example" }]), false);
            var people = JsonNode.Parse(requests.Single().Body)!.AsObject();
            Check(people.Count == 1 && people["attendees"]?.AsArray().Count == 2 && people["attendees"]?[1]?["type"]?.GetValue<string>() == "optional",
                  "un partecipante in più: solo attendees, con tutti (Exchange manda l'aggiornamento)");

            requests.Clear();
            await personal.DeleteEventAsync(occurrence, false);
            await personal.DeleteEventAsync(occurrence, true);
            await personal.CancelEventAsync(occurrence, " Rinviata a novembre ", false);
            await personal.RespondToEventAsync(occurrence, InviteAnswer.Decline, "", send: true);
            Check(requests[0] is { Path: "me/events/occ-1" } d1 && d1.Method == HttpMethod.Delete && requests[1] is { Path: "me/events/serie-1" } d2 && d2.Method == HttpMethod.Delete
                  && requests[2].Method == HttpMethod.Post && requests[2].Path == "me/events/occ-1/cancel" && JsonNode.Parse(requests[2].Body)?["Comment"]?.GetValue<string>() == "Rinviata a novembre"
                  && requests[3].Path == "me/events/occ-1/decline" && JsonNode.Parse(requests[3].Body)?["sendResponse"]?.GetValue<bool>() == true,
                  "Elimina: DELETE dell'occorrenza e della serie; Annulla riunione: cancel con Comment; Rifiuta: decline con sendResponse");

            requests.Clear();
            string? personalSchedule = null;
            try { await personal.GetScheduleAsync(["anna@studio.example"], occurrence.Start, occurrence.End); }
            catch (MailException ex) { personalSchedule = ex.Message; }
            var schedule = await work.GetScheduleAsync(["anna@studio.example", "esterno@cliente.example"], new DateTime(2026, 10, 25, 8, 0, 0), new DateTime(2026, 10, 25, 20, 0, 0));
            var ask = JsonNode.Parse(requests.Single().Body)!;
            Check(personalSchedule?.Contains("solo con gli account di lavoro") == true && requests.Single().Path == "me/calendar/getSchedule"
                  && ask["availabilityViewInterval"]?.GetValue<int>() == 30 && ask["startTime"]?["dateTime"]?.GetValue<string>() == "2026-10-25T08:00:00"
                  && ask["startTime"]?["timeZone"]?.GetValue<string>() == local && ask["schedules"]?.AsArray().Count == 2 && requests.Single().Prefer == $"outlook.timezone=\"{local}\""
                  && schedule is [{ View: "000022220000000000000000", Error: null }, { Error: "calendario non condiviso" }],
                  "disponibilità: getSchedule con le persone, dalle 8 alle 20 nel fuso di Windows a mezz'ore, Prefer; l'errore di un esterno letto; con l'account personale spiegato senza richieste");

            requests.Clear();
            var testing = new TestingProvider(personal);
            await testing.UpdateEventAsync(occurrence, With(location: "Sala Rossa"), false);
            await testing.DeleteEventAsync(occurrence, true);
            await testing.CancelEventAsync(occurrence, "Rinviata", false);
            var logged = File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).TakeLast(3).ToList();
            Check(requests.Count == 0 && logged[0].Contains("prova: modificherei l'evento \"Punto settimanale\"") && logged[0].Contains("solo questo evento") && logged[0].Contains("luogo")
                  && logged[1].Contains("prova: eliminerei l'evento \"Punto settimanale\"") && logged[1].Contains("tutta la serie")
                  && logged[2].Contains("prova: annullerei la riunione \"Punto settimanale\"") && logged[2].Contains("con una nota"),
                  "nella prova Salva, Elimina e Annulla riunione vanno solo nel registro, nessuna richiesta");
        }
        finally
        {
            GraphProvider.TestCalendarConsent = false;
        }
    }

    /// <summary>
    /// Risposte automatiche e categorie di Outlook con un gestore HTTP finto: senza il consenso alle impostazioni della
    /// casella nessuna richiesta (prima masterCategories partiva a ogni avvio e tornava 403) e le categorie locali; con il
    /// consenso la lettura (orari in UTC nell'ora di Windows, messaggi, esterni) e il PATCH con gli orari in UTC; nella
    /// prova (TestingProvider) il cambio va solo nel registro, senza richieste.
    /// </summary>
    static async Task AutoRepliesAsync()
    {
        Report.Add("--- Risposte automatiche e categorie di Outlook, gestore finto");
        var requests = new List<(HttpMethod Method, string Path, string Token, string Body)>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((request.Method, path, request.Headers.Authorization?.Parameter ?? "", request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            JsonNode json = path == "me/mailboxSettings/automaticRepliesSetting"
                ? new JsonObject
                {
                    ["status"] = "scheduled", ["externalAudience"] = "contactsOnly",
                    ["scheduledStartDateTime"] = new JsonObject { ["dateTime"] = "2026-10-24T22:00:00.0000000", ["timeZone"] = "UTC" },
                    ["scheduledEndDateTime"] = new JsonObject { ["dateTime"] = "2026-10-26T17:00:00.0000000", ["timeZone"] = "UTC" },
                    ["internalReplyMessage"] = "<html><body><p>Sono in ferie fino al <b>26 ottobre</b>.</p></body></html>",
                    ["externalReplyMessage"] = "<p>Rispondo dal 27.</p>",
                }
                : path == "me/outlook/masterCategories"
                ? new JsonObject { ["value"] = new JsonArray(new JsonObject { ["displayName"] = "Clienti", ["color"] = "preset4" }) }
                : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Patch ? "" : json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@example.com" };
        var graph = new GraphProvider(account);
        Store.Settings.LocalCategories[account.Id] = ["Solo in Missiva"];
        try
        {
            GraphProvider.TestSettingsConsent = false;
            var none = await graph.GetAutoRepliesAsync();
            var local = await graph.GetCategoriesAsync();
            Check(none == null && requests.Count == 0 && local is [{ Name: "Solo in Missiva" }],
                  "senza consenso alle impostazioni della casella: risposte automatiche null, categorie locali, nessuna richiesta (niente 403 all'avvio)");

            GraphProvider.TestSettingsConsent = true;
            var master = await graph.GetCategoriesAsync();
            var replies = await graph.GetAutoRepliesAsync();
            Check(master is [{ Name: "Clienti" }] && requests.All(r => r.Token == "prova-impostazioni"),
                  "con il consenso: masterCategories con il token delle impostazioni della casella");
            // 22:00 UTC del 24 ottobre sono la mezzanotte del 25 (ora legale), 17:00 UTC del 26 le 18 (ora solare).
            Check(replies is { State: AutoReplyState.Scheduled, External: AutoReplyAudience.Contacts } r
                  && r.Start == new DateTime(2026, 10, 25, 0, 0, 0) && r.End == new DateTime(2026, 10, 26, 18, 0, 0)
                  && r.InternalHtml.Contains("<b>26 ottobre</b>") && r.ExternalHtml == "<p>Rispondo dal 27.</p>",
                  $"lettura: programmate dal {replies?.Start:dd/MM HH:mm} al {replies?.End:dd/MM HH:mm} (orari UTC nell'ora di Windows, a cavallo del 25 ottobre), solo i contatti, i due messaggi");
            Check(replies!.ActiveAt(new DateTime(2026, 10, 25, 12, 0, 0)) && !replies.ActiveAt(new DateTime(2026, 10, 26, 18, 0, 0))
                  && replies.PlannedAfter(new DateTime(2026, 10, 24, 20, 0, 0))
                  && replies.Describe(new DateTime(2026, 10, 25, 12, 0, 0)) == "Risposte automatiche attive fino a lunedì 26 ottobre alle 18:00",
                  $"barra: {replies.Describe(new DateTime(2026, 10, 25, 12, 0, 0))}");

            requests.Clear();
            var changed = replies with { State = AutoReplyState.Scheduled, Start = new DateTime(2026, 12, 23, 9, 0, 0), End = new DateTime(2027, 1, 7, 9, 0, 0), External = AutoReplyAudience.None };
            await graph.SetAutoRepliesAsync(changed);
            var patch = requests.Single();
            var body = JsonNode.Parse(patch.Body)?["automaticRepliesSetting"];
            Check(patch.Method == HttpMethod.Patch && patch.Path == "me/mailboxSettings" && patch.Token == "prova-impostazioni"
                  && body?["status"]?.GetValue<string>() == "scheduled" && body["externalAudience"]?.GetValue<string>() == "none"
                  && body["scheduledStartDateTime"]?["dateTime"]?.GetValue<string>() == "2026-12-23T08:00:00" && body["scheduledStartDateTime"]?["timeZone"]?.GetValue<string>() == "UTC"
                  && body["scheduledEndDateTime"]?["dateTime"]?.GetValue<string>() == "2027-01-07T08:00:00"
                  && body["internalReplyMessage"]?.GetValue<string>().Contains("26 ottobre") == true,
                  $"PATCH di me/mailboxSettings con solo automaticRepliesSetting, orari in UTC (9:00 d'inverno sono le 8:00 UTC): {patch.Body[..Math.Min(patch.Body.Length, 160)]}");
            requests.Clear();
            await graph.SetAutoRepliesAsync(changed with { State = AutoReplyState.Off });
            var offBody = JsonNode.Parse(requests.Single().Body)?["automaticRepliesSetting"];
            Check(offBody?["status"]?.GetValue<string>() == "disabled" && offBody["scheduledStartDateTime"] == null, "Disattiva: status disabled, messaggi tenuti, nessun orario");

            requests.Clear();
            var testing = new TestingProvider(graph);
            await testing.SetAutoRepliesAsync(changed);
            await testing.AllowMailboxSettingsAsync();
            var logged = File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).TakeLast(5).ToList();
            Check(requests.Count == 0 && logged.Any(l => l.Contains("prova: imposterei le risposte automatiche di prova@example.com dal 23/12/2026 09:00 al 07/01/2027 09:00"))
                  && logged.Any(l => l.Contains("prova: chiederei il consenso alle impostazioni della casella")),
                  "nella prova il cambio e il consenso vanno solo nel registro, nessuna richiesta");
        }
        finally
        {
            GraphProvider.TestSettingsConsent = false;
            Store.Settings.LocalCategories.Remove(account.Id);
        }
    }

    /// <summary>
    /// Tutti gli account: due account con lo stesso UID nella stessa cartella e la stessa conversazione restano su due righe,
    /// ognuna con il suo account; Annulla rimette ogni riga al suo account.
    /// </summary>
    /// <summary>
    /// Accessibilità e anteprime (giro P4): le righe e le cartelle hanno un nome da leggere invece del tipo .NET; le
    /// anteprime senza i resti degli indirizzi fra &lt; e &gt; (anche tagliati in fondo); Contrassegnati per scadenza.
    /// </summary>
    static void AccessibilityTests()
    {
        Report.Add("--- Accessibilità, anteprime e Contrassegnati per scadenza");
        Check(MailHtml.PlainPreview("<https://www.arci.it/newsletter/2026/settembre/numero-12?utm_source=newsletter&utm_medium=email") == ""
              && MailHtml.PlainPreview("<https://arci.it/x> Il programma di ottobre") == "Il programma di ottobre"
              && MailHtml.PlainPreview("Scrivi a <mailto:info@arci.it> entro venerdì <https://arci.it/iscriviti") == "Scrivi a info@arci.it entro venerdì"
              && MailHtml.PlainPreview("Offerta < 100 euro") == "Offerta < 100 euro",
              "anteprime: niente \"<\" da un indirizzo tagliato in fondo, <https://...> tolto, <mailto:...> diventa l'indirizzo");
        // Giro D1: Gmail taglia l'anteprima dentro un link [testo](indirizzo), senza la parentesi chiusa (Sky tg24 Insider).
        Check(MailHtml.PlainPreview("Le notizie del giorno [Leggi da browser](https://www.sky.it/abc") == "Le notizie del giorno Leggi da browser"
              && MailHtml.PlainPreview("[Leggi da browser](https://www.sky.it/abc") == "Leggi da browser"
              && MailHtml.PlainPreview("[Leggi da browser](https://www.sky.it/abc) Le notizie") == "Leggi da browser Le notizie"
              && MailHtml.PlainPreview("Offerte [Scopri](") == "Offerte Scopri",
              "anteprime: un link [testo](indirizzo) tagliato in fondo diventa il suo testo, come quello intero");
        var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "studio@uno.example", Imap = new ImapServer() }));
        var row = new UI.MessageItem(new MessageSummary
        {
            Id = "7", FolderId = "INBOX", Subject = "Spedito: \"3 Metri Canalina\"", From = new Address { Name = "Amazon.it", Email = "spedizioni@amazon.it" },
            Date = DateTime.Today.AddHours(9).AddMinutes(54), HasAttachments = true, IsFlagged = true,
        }, account);
        var folder = new UI.FolderItem(account, new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox, Unread = 3 });
        Report.Add($"      riga: {row}; cartella: {folder}");
        Check(row.ToString() == "Non letto, Amazon.it, Spedito: \"3 Metri Canalina\", oggi 09:54, con allegato, contrassegnato" && folder.ToString() == "Posta in arrivo, 3 non letti"
              && !row.ToString().Contains("Missiva.UI"),
              "UI Automation: la riga e la cartella si leggono (\"Non letto, Amazon.it, ..., con allegato, contrassegnato\", \"Posta in arrivo, 3 non letti\")");

        var flags = Store.Settings.Flags.ToList();
        try
        {
            Store.Settings.Flags.Clear();
            MessageSummary Flagged(string id, int? days, int minutes)
            {
                var m = new MessageSummary { Id = id, FolderId = "INBOX", StableId = id, Subject = id, IsFlagged = true, Date = DateTime.Now.AddMinutes(-minutes) };
                if (days is { } d) Flags.Remember(account.Provider.Account, m, null, DateTime.Today.AddDays(d), null);
                return m;
            }
            var rows = new UI.MessageRows { ByDue = true };
            rows.Append([Flagged("senza", null, 1), Flagged("domani", 1, 2), Flagged("ieri", -1, 3), Flagged("oggi", 0, 4), Flagged("oggi-prima", 0, 1)], account);
            var order = string.Join(", ", rows.Rows.Select(r => $"{r.Summary.Id}:{r.Group}"));
            Report.Add("      " + order);
            Check(order == "ieri:In ritardo, oggi-prima:Oggi, oggi:Oggi, domani:Domani, senza:Senza data",
                  "Contrassegnati di tutti gli account per scadenza: In ritardo, Oggi (la più recente prima), Domani, Senza data in fondo");
        }
        finally
        {
            Store.Settings.Flags.Clear();
            Store.Settings.Flags.AddRange(flags);
        }

        // Correzioni del giro P4: il messaggio tornato da Posponi con l'ora del ritorno sale al suo posto nella pagina per data
        // (le pagine di Gmail e IMAP sono ordinate per INTERNALDATE, che lo spostamento non cambia).
        var woken = new Dictionary<string, DateTime>(Store.Settings.Woken);
        try
        {
            Store.Settings.Woken["posponi-prova|<tornato@prova.example>"] = DateTime.Today.AddHours(8);
            MessageSummary At(string id, DateTime date, string stable = "") => new() { Id = id, FolderId = "INBOX", StableId = stable, Date = date };
            List<MessageSummary> Page() => [At("nove", DateTime.Today.AddHours(9)), At("sette", DateTime.Today.AddHours(7)), At("stesso-giorno", new DateTime(2026, 9, 20, 10, 0, 0)),
                                            At("tornato", new DateTime(2026, 9, 20, 10, 0, 0), "<tornato@prova.example>"), At("vecchio", new DateTime(2026, 9, 19))];
            var newest = Page();
            UI.MainWindow.ApplyWoken(newest, "posponi-prova", ListOptions.Default);
            var oldest = Page();
            oldest.Reverse();
            UI.MainWindow.ApplyWoken(oldest, "posponi-prova", new ListOptions(Ascending: true));
            var bySender = Page();
            UI.MainWindow.ApplyWoken(bySender, "posponi-prova", new ListOptions(Sort: SortBy.From));
            Report.Add($"      per data: {string.Join(", ", newest.Select(m => m.Id))}; dal più vecchio: {string.Join(", ", oldest.Select(m => m.Id))}");
            Check(string.Join(",", newest.Select(m => m.Id)) == "nove,tornato,sette,stesso-giorno,vecchio" && newest[1].Date == DateTime.Today.AddHours(8)
                  && string.Join(",", oldest.Select(m => m.Id)) == "vecchio,stesso-giorno,sette,tornato,nove"
                  && string.Join(",", bySender.Select(m => m.Id)) == "nove,sette,stesso-giorno,tornato,vecchio",
                  "Posponi: il messaggio tornato vale con l'ora del ritorno e la pagina per data si riordina (stabile); per mittente resta com'è");
        }
        finally
        {
            Store.Settings.Woken.Clear();
            foreach (var (k, v) in woken) Store.Settings.Woken[k] = v;
        }
    }

    static void UnifiedRows()
    {
        Report.Add("--- Tutti gli account: righe di più account");
        UI.AccountItem Account(string email) => new(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = email, Imap = new ImapServer() }));
        var one = Account("studio@uno.example");
        var two = Account("mario@due.example");
        MessageSummary Message(string subject, int minutes) =>
            new() { Id = "42", FolderId = "INBOX", Subject = subject, Date = DateTime.Now.AddMinutes(-minutes), ConversationId = "stessa" };
        var rows = new UI.MessageRows { ShowAccounts = true };
        var first = Message("dal primo", 5);
        var second = Message("dal secondo", 3);
        rows.Append([(second, two), (first, one)]);
        Check(rows.Rows.Count == 2 && rows.Rows[0].Account == two && rows.Rows[1].Account == one && rows.Rows.All(r => r.Count == 1 && r.ShowAccount)
              && rows.Rows[0].WhereText == "mario" && rows.Rows[1].WhereText == "studio",
              "stesso UID e stessa conversazione in due account: due righe, ognuna con il suo account");
        var removed = rows.Rows.Select((r, i) => (Index: i, Messages: UI.MessageRows.MessagesOf(r))).ToList();
        foreach (var r in rows.Rows.ToList()) rows.Remove(r);
        var back = new Dictionary<MessageSummary, MessageSummary> { [first] = first, [second] = second };
        var restored = rows.Restore(removed, back, m => m == first ? one : two);
        Check(rows.Rows.Count == 2 && restored.Count == 2 && rows.Rows.Single(r => r.Summary == first).Account == one && rows.Rows.Single(r => r.Summary == second).Account == two,
              "Annulla in tutti gli account: ogni riga torna con il suo account");
    }

    /// <summary>Dall'HTML di una bozza all'editor (HtmlToFlow) e di nuovo all'HTML da spedire (FlowToHtml).</summary>
    /// <summary>
    /// Modelli (giro P4): il testo di un modello con grassetto, elenco, link e segnaposto, salvato come RTF dall'editor come
    /// le firme, torna con {nome} e {data} sostituiti e parte con lo stesso FlowToHtml della firma; il nome del destinatario
    /// dalla prima parola, senza nome {nome} resta; in impostazioni.json nome, oggetto e testo cifrati.
    /// </summary>
    static void TemplatesTest()
    {
        Report.Add("--- Modelli di risposta");
        var doc = new System.Windows.Documents.FlowDocument();
        var greeting = new System.Windows.Documents.Paragraph();
        greeting.Inlines.Add(new System.Windows.Documents.Run("Gentile {nome}, in allegato il "));
        greeting.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run("preventivo")));
        greeting.Inlines.Add(new System.Windows.Documents.Run(" del {data}."));
        doc.Blocks.Add(greeting);
        var list = new System.Windows.Documents.List();
        list.ListItems.Add(new System.Windows.Documents.ListItem(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("IBAN IT60X0542811101000000123456"))));
        list.ListItems.Add(new System.Windows.Documents.ListItem(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Scadenza 30 giorni"))));
        doc.Blocks.Add(list);
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("condizioni")) { NavigateUri = new Uri("https://studio-prova.example/condizioni") };
        doc.Blocks.Add(new System.Windows.Documents.Paragraph(link));
        using var stream = new MemoryStream();
        new System.Windows.Documents.TextRange(doc.ContentStart, doc.ContentEnd).Save(stream, System.Windows.DataFormats.Rtf);
        var template = new MailTemplate { Name = "Preventivo", Subject = "Preventivo del {data}", Rtf = Encoding.UTF8.GetString(stream.ToArray()) };
        var today = new DateTime(2026, 9, 27);

        string Html(IEnumerable<System.Windows.Documents.Block> blocks)
        {
            var d = new System.Windows.Documents.FlowDocument();
            d.Blocks.AddRange(blocks.ToList());
            return FlowToHtml.Convert(d);
        }
        var name = UI.Templates.FirstName(new Address { Name = "Mario Rossi", Email = "mario@cliente.example" });
        var html = Html(UI.Templates.Blocks(template, name, today));
        Report.Add("      " + html[..Math.Min(html.Length, 400)]);
        Check(html.Contains("Gentile Mario, in allegato il <b>") && html.Contains("preventivo</b>") && html.Contains(" del 27 settembre 2026.") && html.Contains("<li>") && html.Contains("IBAN IT60X0542811101000000123456")
              && html.Contains("href=\"https://studio-prova.example/condizioni\"") && !html.Contains("{nome}") && !html.Contains("{data}")
              && UI.Templates.Fill(template.Subject, name, today) == "Preventivo del 27 settembre 2026",
              "modello inserito: grassetto, elenco e link come scritti, {nome} (Mario) e {data} sostituiti, anche nell'oggetto");
        var withoutName = Html(UI.Templates.Blocks(template, UI.Templates.FirstName(new Address { Email = "info@cliente.example" }), today));
        var signature = new System.Windows.Documents.FlowDocument();
        signature.Blocks.AddRange(UI.RichEditor.BlocksFromRtf(template.Rtf));
        Check(withoutName.Contains("Gentile {nome},") && withoutName.Replace("27 settembre 2026", "{data}") == FlowToHtml.Convert(signature),
              "senza il nome del destinatario {nome} resta da scrivere; a parte i segnaposto, il modello parte con lo stesso FlowToHtml della firma");
        var json = System.Text.Json.JsonSerializer.Serialize(new Settings { Templates = [template] }, SettingsJson.Default.Settings);
        Check(json.Contains("\"dpapi:") && !json.Contains("IBAN") && !json.Contains("Preventivo") && !json.Contains("{nome}")
              && System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.Settings)?.Templates.Single().Rtf == template.Rtf,
              "impostazioni.json: nome, oggetto e testo del modello cifrati (\"dpapi:\"), riletti uguali");

        // Correzioni del giro P4: la pagina Modelli salva a ogni pausa; in memoria restano solo i testi dell'ultimo salvataggio.
        var templates = Store.Settings.Templates.ToList();
        var settingsFile = Path.Combine(Store.Dir, "impostazioni.json");
        try
        {
            var typed = new MailTemplate { Name = "Modello con il logo", Rtf = template.Rtf };
            Store.Settings.Templates.Add(typed);
            Store.Save();
            int start = ProtectedTextConverter.KnownCount;
            for (int i = 0; i < 60; i++)
            {
                typed.Rtf = template.Rtf + $"{{\\pict {new string('a', 100_000)} {i}}}"; // 100 KB di immagine in esadecimale, come un logo
                Store.Save();
            }
            int after = ProtectedTextConverter.KnownCount;
            var first = File.ReadAllText(settingsFile);
            Store.Save();
            Report.Add($"      testi cifrati in memoria: {start} dopo il primo salvataggio, {after} dopo 60 versioni del modello");
            Check(after <= start && first == File.ReadAllText(settingsFile) && !first.Contains("Modello con il logo"),
                  "modelli scritti a pause: in memoria solo i testi cifrati dell'ultimo salvataggio (non 60 versioni del RTF), e senza modifiche il file resta uguale");
        }
        finally
        {
            Store.Settings.Templates.Clear();
            Store.Settings.Templates.AddRange(templates);
            Store.Save();
        }

        // Correzioni del giro P4: rimuovendo un account escono anche i suoi contrassegni, modelli, posposti, invii pianificati,
        // firme e il resto tenuto per lui; quelli degli altri account (anche con un Id che comincia allo stesso modo) restano.
        var removal = new Settings
        {
            Flags = [new FlagDate { AccountId = "via", Subject = "Fattura Rossi" }, new FlagDate { AccountId = "resta" }],
            Templates = [new MailTemplate { AccountId = "via", Name = "IBAN dello studio" }, new MailTemplate { Name = "Per tutti" }, new MailTemplate { AccountId = "resta" }],
            Snoozed = [new SnoozedMessage { AccountId = "via" }], ScheduledSends = [new ScheduledSend { AccountId = "via" }, new ScheduledSend { AccountId = "resta" }],
            Woken = { ["via|k"] = DateTime.Today, ["resta|k"] = DateTime.Today },
            Signatures = { ["via"] = new Signature(), ["via|alias"] = new Signature(), ["resta"] = new Signature() },
            Favorites = ["via|INBOX", "viale|INBOX"], HiddenCalendars = ["via|c"], Focused = { ["via"] = false }, Pinned = { ["via"] = ["m"] }, NotificationsOff = ["via", "resta"],
            FolderOrder = { ["via"] = ["INBOX"] }, CategoryColors = { ["via|Rossa"] = 1 }, CollapsedFolders = ["via|Clienti"], HiddenFolders = ["via|Rossi"],
        };
        removal.ForgetAccount("via");
        Check(removal.Flags.Single().AccountId == "resta" && removal.Templates.Select(t => t.AccountId ?? "tutti").SequenceEqual(["tutti", "resta"])
              && removal.Snoozed.Count == 0 && removal.ScheduledSends.Single().AccountId == "resta" && removal.Woken.Keys.Single() == "resta|k"
              && removal.Signatures.Keys.Single() == "resta" && removal.Favorites.Single() == "viale|INBOX" && removal.HiddenCalendars.Count == 0 && removal.Focused.Count == 0
              && removal.Pinned.Count == 0 && removal.NotificationsOff.Single() == "resta" && removal.FolderOrder.Count == 0 && removal.CategoryColors.Count == 0
              && removal.CollapsedFolders.Count == 0 && removal.HiddenFolders.Count == 0,
              "account rimosso: escono contrassegni, modelli suoi, posposti, invii pianificati, firme (anche degli altri indirizzi), cartelle e categorie; quelli degli altri account restano");
    }

    static void RoundTrip()
    {
        Report.Add("--- Testo delle bozze riprese");
        static string Again(string html)
        {
            var document = new System.Windows.Documents.FlowDocument();
            document.Blocks.AddRange(HtmlToFlow.Blocks(html));
            return FlowToHtml.Convert(document);
        }
        var pre = Again("<p>Risposta</p><pre>Il 26 set, Mario ha scritto:\r\n&gt; riga uno\n&gt;   rientrata\n\nfine</pre>");
        Report.Add("      " + pre);
        Check(pre.Contains("Il 26 set, Mario ha scritto:<br>&gt; riga uno<br>&gt; &nbsp; rientrata<br><br>fine"), "testo in <pre>: ogni riga resta una riga (<br>)");
        var bold = Again("<p>Ciao <b>Mario</b>,<br>a presto</p>");
        Check(bold.Contains("Ciao <b>Mario</b>,<br>a presto"), "paragrafo con grassetto e a capo come prima");

        // Un'immagine incorporata della bozza torna un'immagine del testo, con lo stesso cid, la stessa larghezza e gli stessi byte.
        var picture = Picture;
        var stored = new Attachment { Id = "img", Name = picture.Name, ContentType = picture.ContentType, IsInline = true, ContentId = picture.ContentId, Content = picture.Content };
        var document = new System.Windows.Documents.FlowDocument();
        document.Blocks.AddRange(HtmlToFlow.Blocks($"<p>Guarda <img src=\"cid:{picture.ContentId}\" width=\"320\"> e <img src=\"https://example.com/x.png\"></p>",
                                                   cid => cid == stored.ContentId ? stored : null));
        var images = new List<InlineImage>();
        var html = FlowToHtml.Convert(document, images);
        Report.Add("      " + html);
        Check(html.Contains($"<img src=\"cid:{picture.ContentId}\" width=\"320\"") && !html.Contains("example.com") && images.Count == 1
              && images[0].ContentId == picture.ContentId && images[0].Content.SequenceEqual(picture.Content),
              "bozza con un'immagine incorporata: stessa immagine nel testo e nell'HTML, quella da Internet no");

        // Riprendendo una bozza tornano immagini del testo solo quelle messe da Missiva: quelle dell'email a cui si risponde
        // (cid di chi l'ha mandata) no, e i byte che non sono PNG, JPEG, GIF o BMP nemmeno.
        var foreign = new Attachment { Id = "x", Name = "logo.png", ContentType = "image/png", IsInline = true, ContentId = "logo123@example.com", Content = picture.Content };
        var webp = new Attachment { Id = "y", Name = "foto.webp", ContentType = "image/png", IsInline = true, ContentId = "f00@missiva",
                                    Content = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 finto") };
        var quoted = new System.Windows.Documents.FlowDocument();
        quoted.Blocks.AddRange(HtmlToFlow.Blocks("<p>Risposta <img src=\"cid:logo123@example.com\"> <img src=\"cid:f00@missiva\"></p>",
                                                 cid => cid == foreign.ContentId ? foreign : cid == webp.ContentId ? webp : null));
        Check(!EditorImages.All(quoted).Any() && EditorImages.Create(new InlineImage("z@missiva", "foto.webp", "image/webp", webp.Content!)) == null,
              "bozza ripresa: le immagini con il cid di un altro e i byte WebP non passano da WIC");

        // Le foto grandi si decodificano rimpicciolite, con il decodificatore del loro tipo (PNG e JPEG).
        foreach (var encoder in new System.Windows.Media.Imaging.BitmapEncoder[] { new System.Windows.Media.Imaging.PngBitmapEncoder(), new System.Windows.Media.Imaging.JpegBitmapEncoder() })
        {
            var wide = System.Windows.Media.Imaging.BitmapSource.Create(3000, 20, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, new byte[3000 * 20 * 4], 3000 * 4);
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(wide));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            var bytes = stream.ToArray();
            var shown = EditorImages.Create(new InlineImage(EditorImages.NewContentId(), "foto", EditorImages.TypeOf(bytes) ?? "", bytes));
            var pixels = (shown?.Source as System.Windows.Media.Imaging.BitmapSource)?.PixelWidth;
            Check(shown != null && pixels == 1440 && shown.Width == EditorImages.MaxWidth && EditorImages.PixelWidth(bytes) == 3000,
                  $"foto {EditorImages.TypeOf(bytes)} larga 3000 pixel: decodificata a {pixels}, larga {shown?.Width} nel testo");
        }

        // Un'immagine messa da WPF da solo (Incolla, RTF), in un blocco suo e senza byte: parte lo stesso, in PNG.
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8);
        var pasted = new System.Windows.Documents.FlowDocument(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Prima")));
        pasted.Blocks.Add(new System.Windows.Documents.BlockUIContainer(new System.Windows.Controls.Image { Source = bitmap }));
        var pastedImages = new List<InlineImage>();
        var pastedHtml = FlowToHtml.Convert(pasted, pastedImages);
        Check(pastedImages.Count == 1 && pastedImages[0].ContentType == "image/png" && pastedHtml.Contains("cid:" + pastedImages[0].ContentId)
              && FlowToHtml.Convert(pasted) == pastedHtml,
              "immagine in un blocco messo da WPF: parte in PNG, e lo stesso HTML a ogni conversione (stesso cid)");

        // Una tabella (da Word o Excel, o una firma fatta con una tabella): righe, celle, bordi, testo e immagini delle celle.
        var table = new System.Windows.Documents.Table();
        var group = new System.Windows.Documents.TableRowGroup();
        var row = new System.Windows.Documents.TableRow();
        var logo = EditorImages.Create(Picture)!;
        row.Cells.Add(new System.Windows.Documents.TableCell(new System.Windows.Documents.Paragraph(new System.Windows.Documents.InlineUIContainer(logo))));
        row.Cells.Add(new System.Windows.Documents.TableCell(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Mario Rossi")))
        {
            BorderThickness = new System.Windows.Thickness(1), BorderBrush = System.Windows.Media.Brushes.Black, Padding = new System.Windows.Thickness(4, 2, 4, 2),
        });
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        var signed = new System.Windows.Documents.FlowDocument(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Ciao")));
        var signature = new System.Windows.Documents.Section();
        signature.Blocks.Add(table);
        signed.Blocks.Add(signature);
        var tableImages = new List<InlineImage>();
        var tableHtml = FlowToHtml.Convert(signed, tableImages);
        Report.Add("      " + tableHtml);
        Check(tableHtml.Contains("<table") && tableHtml.Contains("<tr><td") && tableHtml.Contains("Mario Rossi</p></td></tr></table>")
              && tableHtml.Contains("padding:2px 4px 2px 4px") && tableHtml.Contains("border-width:1px 1px 1px 1px;border-color:#000000")
              && tableImages.Count == 1 && tableHtml.Contains("cid:" + tableImages[0].ContentId) && EditorImages.All(signed).Single() == logo
              && EditorImages.All(signature.Blocks).Single() == logo,
              "tabella in una firma: <table> con righe e celle, bordo, testo e l'immagine della cella (anche per EditorImages.All)");
    }

    // ---------------------------------------------------------------- Graph

    static async Task GraphAsync()
    {
        Report.Add("--- Graph, gestore HTTP finto");
        var fake = new FakeGraph();
        GraphProvider.UseTestHandler(fake);
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@example.com" });

        var file = TempFile("a.txt", "allegato di prova");
        var first = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Prova", Html = "<p>Ciao</p>", AttachmentPaths = [file] });
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(fake.Requests.Count(r => r.StartsWith("POST me/messages ")) == 1, "prima volta: una sola POST me/messages");
        Check(fake.Requests.Any(r => r.StartsWith("PATCH me/messages/bozza-1 ") && r.Contains("m.rossi@example.com") && r.Contains("<p>Ciao</p>")), "PATCH con destinatari e corpo");
        Check(fake.Requests.Any(r => r.StartsWith("POST me/messages/bozza-1/attachments")), "allegato aggiunto alla bozza");
        Check(first.Message.Id == "bozza-1" && first.Attachments.Count == 1 && first.QuoteHtml == null, "bozza salvata: bozza-1 con un allegato");

        fake.Requests.Clear();
        var second = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Prova", Html = "<p>Ciao di nuovo</p>", Draft = first, KeptAttachments = [] });
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(!fake.Requests.Any(r => r.StartsWith("POST me/messages ")), "seconda volta: nessuna bozza nuova");
        Check(fake.Requests.Any(r => r.StartsWith("PATCH me/messages/bozza-1 ") && r.Contains("Ciao di nuovo")), "seconda volta: PATCH della stessa bozza");
        Check(fake.Requests.Any(r => r.StartsWith("DELETE me/messages/bozza-1/attachments/allegato-1")), "allegato tolto eliminato dalla bozza");
        Check(second.Message.Id == "bozza-1" && second.Attachments.Count == 0, "bozza salvata: stessa bozza, senza allegati");

        fake.Requests.Clear();
        await graph.SendAsync(new OutgoingMessage { To = To, Subject = "Prova", Html = "<p>Ciao, parto</p>", Draft = second });
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(fake.Requests.Any(r => r.StartsWith("PATCH me/messages/bozza-1 ")) && fake.Requests.Any(r => r.StartsWith("POST me/messages/bozza-1/send")),
              "invio: PATCH e /send sulla stessa bozza");
        Check(!fake.Requests.Any(r => r.StartsWith("POST me/messages ") || r.StartsWith("DELETE me/messages/bozza-1 ")), "invio: nessuna bozza nuova né eliminata");

        fake.Requests.Clear();
        var original = new MessageSummary { Id = "orig-1", FolderId = "inbox", Subject = "Domanda" };
        var reply = await graph.SaveDraftAsync(new OutgoingMessage { Mode = ComposeMode.Reply, Original = original, To = To, Subject = "R: Domanda", Html = "<p>Risposta</p>" });
        var again = await graph.SaveDraftAsync(new OutgoingMessage { Mode = ComposeMode.Reply, Original = original, To = To, Subject = "R: Domanda", Html = "<p>Risposta lunga</p>", Draft = reply });
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(fake.Requests.Count(r => r.Contains("/createReply")) == 1, "risposta: createReply una volta sola");
        Check(fake.Requests.Count(r => r.StartsWith("PATCH me/messages/bozza-2 ") && r.Contains("Citazione") && r.Contains("<body><p>Risposta")) == 2,
              "risposta: il testo nuovo va in cima al corpo con la citazione, anche la seconda volta");
        Check(reply.QuoteHtml?.Contains("Citazione") == true && again.Message.Id == "bozza-2", "risposta: citazione tenuta, stessa bozza");

        // Un'immagine incollata nel testo: allegato incorporato (isInline, contentId), non fra gli allegati della finestra;
        // salvando di nuovo resta com'è, tolta dal testo si elimina.
        fake.Requests.Clear();
        var picture = Picture;
        var withImage = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Foto", Html = $"<p>Guarda</p><img src=\"cid:{picture.ContentId}\">", Images = [picture] });
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(fake.Requests.Any(r => r.StartsWith("POST me/messages/bozza-1/attachments") && r.Contains("\"isInline\":true") && r.Contains($"\"contentId\":\"{picture.ContentId}\"")
                                     && r.Contains("\"contentType\":\"image/png\"")),
              "immagine del testo: fileAttachment con isInline e contentId");
        Check(withImage.Attachments.Count == 0 && withImage.Images.Select(i => i.ContentId).SequenceEqual([picture.ContentId]),
              "immagine del testo: fra le immagini della bozza, non fra gli allegati");
        fake.Requests.Clear();
        var sameImage = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Foto", Html = $"<p>Guarda bene</p><img src=\"cid:{picture.ContentId}\">", Images = [picture],
                                                                         Draft = withImage });
        Check(!fake.Requests.Any(r => r.Contains("/attachments")) && sameImage.Images.Count == 1, "salvataggio automatico con la stessa immagine: niente da ricaricare né da eliminare");
        fake.Requests.Clear();
        var noImage = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Foto", Html = "<p>Senza foto</p>", Draft = sameImage });
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(fake.Requests.Any(r => r.StartsWith($"DELETE me/messages/bozza-1/attachments/{withImage.Images[0].Id}")) && noImage.Images.Count == 0,
              "immagine tolta dal testo: eliminata dalla bozza");

        // Scarta: la bozza nata nella finestra si elimina.
        fake.Requests.Clear();
        await graph.DiscardDraftAsync(noImage);
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(fake.Requests.SequenceEqual(["DELETE me/messages/bozza-1"]), "Scarta: DELETE della bozza");

        // La bozza è scritta ma la lettura che segue non riesce: la finestra deve avere lo stesso il suo id.
        fake.Requests.Clear();
        fake.FailGet = true;
        var unread = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Prova", Html = "<p>Senza lettura</p>", AttachmentPaths = [file] });
        fake.FailGet = false;
        var next = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Prova", Html = "<p>Dopo</p>", Draft = unread, KeptAttachments = unread.Attachments });
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(unread.Message.Id == "bozza-1" && unread.Attachments is [{ Name: "a.txt" } added] && added.Id.StartsWith("allegato-"),
              "lettura dopo il salvataggio non riuscita: id della bozza e allegato aggiunto");
        Check(fake.Requests.Count(r => r.StartsWith("POST me/messages ")) == 1 && next.Message.Id == "bozza-1" && !fake.Requests.Any(r => r.StartsWith("DELETE")),
              "lettura non riuscita: il salvataggio dopo continua la stessa bozza, allegato tenuto");

        // Un salvataggio che si ferma a metà su una bozza che c'era già: l'immagine tolta dal testo è già eliminata, la prima
        // nuova è caricata, la seconda no. Quello che ha caricato si toglie, e il salvataggio dopo (con la bozza com'era per
        // la finestra) non trova più l'immagine tolta ma non si ferma, e non lascia doppioni.
        fake.Requests.Clear();
        var before = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Foto", Html = $"<img src=\"cid:{picture.ContentId}\">", Images = [picture] });
        var one = Picture;
        var two = Picture;
        var both = $"<img src=\"cid:{one.ContentId}\"><img src=\"cid:{two.ContentId}\">";
        fake.FailAfterPosts = 1;
        string? stopped = null;
        try { await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Foto", Html = both, Images = [one, two], Draft = before }); }
        catch (MailException ex) { stopped = ex.Message; }
        var afterFailure = fake.AttachmentsOf("bozza-1").ToList();
        var retried = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Foto", Html = both, Images = [one, two], Draft = before });
        Report.AddRange(fake.Requests.Select(r => "      " + r));
        Check(stopped != null && afterFailure.Count == 0, $"salvataggio fermo a metà: l'immagine già caricata esce dalla bozza ({stopped})");
        Check(retried.Images.Count == 2 && fake.AttachmentsOf("bozza-1").Count == 2,
              $"salvataggio dopo: immagine tolta già eliminata (404) senza errore, due immagini sulla bozza e nessun doppione ({fake.AttachmentsOf("bozza-1").Count})");

        // Il limite di 3 MB conta solo quello che si carica: l'immagine da 2 MB già sulla bozza più un file da 1,5 MB passano.
        fake.Requests.Clear();
        var big = new InlineImage(EditorImages.NewContentId(), "grande.png", "image/png", new byte[2 * 1024 * 1024]);
        var bigDraft = await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Grande", Html = $"<img src=\"cid:{big.ContentId}\">", Images = [big] });
        var pdf = Path.Combine(Path.GetDirectoryName(file)!, "documento.pdf");
        File.WriteAllBytes(pdf, new byte[1536 * 1024]);
        string? limit = null;
        try
        {
            await graph.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Grande", Html = $"<img src=\"cid:{big.ContentId}\">", Images = [big],
                                                             AttachmentPaths = [pdf], Draft = bigDraft });
        }
        catch (MailException ex) { limit = ex.Message; }
        Check(limit == null && fake.Requests.Count(r => r.StartsWith("POST me/messages/bozza-1/attachments")) == 2,
              $"limite di 3 MB: l'immagine già sulla bozza non conta ({limit ?? "salvata"})");

        // Scaricamenti: il limite vale per l'attesa, non per la durata (qui 1 s invece di 60).
        var idle = GraphProvider.DownloadIdle;
        GraphProvider.DownloadIdle = TimeSpan.FromSeconds(1);
        try
        {
            var slow = await graph.GetAttachmentAsync(new MessageSummary { Id = "lento", FolderId = "x" }, new Attachment { Id = "a1" });
            Check(slow.Length == 6000, $"allegato lento (2,4 s, un pezzo ogni 0,4 s) arrivato intero: {slow.Length} byte");
            string? error = null;
            try { await graph.GetAttachmentAsync(new MessageSummary { Id = "fermo", FolderId = "x" }, new Attachment { Id = "a1" }); }
            catch (MailException ex) { error = ex.Message; }
            Check(error?.Contains("non manda nulla") == true, $"allegato fermo per 1,5 s: interrotto ({error})");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var start = await graph.GetRawAsync(new MessageSummary { Id = "lento", FolderId = "x" }, 1500);
            Check(start.Length == 1500 && clock.ElapsedMilliseconds < 2000, $"Origine: solo i primi 1500 byte, senza aspettare il resto ({clock.ElapsedMilliseconds} ms invece di 2,4 s)");
        }
        finally
        {
            GraphProvider.DownloadIdle = idle;
        }
    }

    /// <summary>Uno scaricamento che arriva a pezzi: uno ogni gap, con una pausa lunga (stall) a metà.</summary>
    sealed class SlowStream(int chunks, TimeSpan gap, TimeSpan stall) : Stream
    {
        int sent;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            if (sent == chunks) return 0;
            await Task.Delay(sent == chunks / 2 ? stall : gap, cancel);
            int n = Math.Min(1000, buffer.Length);
            buffer.Span[..n].Fill((byte)('a' + sent));
            sent++;
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    sealed class FakeGraph : HttpMessageHandler
    {
        public readonly List<string> Requests = new();
        /// <summary>Il corpo intero dell'ultimo PATCH (nel registro delle richieste è accorciato).</summary>
        public string LastPatch = "";
        /// <summary>L'ultimo messaggio mandato in formato MIME (me/sendMail con text/plain), decodificato.</summary>
        public string? LastMime;
        /// <summary>Le letture rispondono 500: la bozza si scrive, ma dopo non si riesce a rileggerla.</summary>
        public bool FailGet;
        /// <summary>Dopo tante POST di allegati riuscite, la prossima risponde 500 (un salvataggio che si ferma a metà); -1 mai.</summary>
        public int FailAfterPosts = -1;
        readonly Dictionary<string, List<string>> attachments = new();

        /// <summary>Gli allegati (e le immagini del testo) che la bozza ha ora sul server finto.</summary>
        public IReadOnlyList<string> AttachmentsOf(string message) => attachments[message];
        /// <summary>Gli allegati aggiunti con isInline (le immagini del testo).</summary>
        readonly HashSet<string> inline = new();
        int next;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancel);
            // Leggibile: senza \u003C al posto di <.
            if (request.Content?.Headers.ContentType?.MediaType == "text/plain")
            {
                // Un messaggio MIME in base64 (me/sendMail delle risposte agli inviti): si tiene decodificato.
                LastMime = Encoding.UTF8.GetString(Convert.FromBase64String(body));
                body = $"(MIME, {LastMime.Length} caratteri)";
            }
            else if (body.Length > 0) body = JsonNode.Parse(body)!.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            if (request.Method == HttpMethod.Patch) LastPatch = body;
            var shortBody = body.Length > 300 ? body[..300] + "…" : body;
            Requests.Add($"{request.Method} {path} {shortBody}".TrimEnd());
            var bare = path.Split('?')[0];
            var parts = bare.Split('/');
            JsonNode? json = null;
            var status = HttpStatusCode.OK;
            if (bare.EndsWith("/$value"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(parts[2] == "fermo"
                        ? new SlowStream(4, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1500))
                        : new SlowStream(6, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400))),
                };
            if (FailGet && request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":{\"message\":\"lettura non riuscita (prova)\"}}", Encoding.UTF8, "application/json") };
            if (request.Method == HttpMethod.Post && bare == "me/messages")
            {
                json = new JsonObject { ["id"] = "bozza-1" };
                attachments["bozza-1"] = new();
            }
            else if (request.Method == HttpMethod.Post && bare.EndsWith("/createReply"))
            {
                json = new JsonObject { ["id"] = "bozza-2", ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = "<html><body><div>Citazione</div></body></html>" } };
                attachments["bozza-2"] = new();
            }
            else if (request.Method == HttpMethod.Post && parts.Length == 4 && parts[3] == "attachments")
            {
                if (FailAfterPosts == 0)
                {
                    FailAfterPosts = -1;
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":{\"message\":\"allegato non caricato (prova)\"}}", Encoding.UTF8, "application/json") };
                }
                if (FailAfterPosts > 0) FailAfterPosts--;
                var id = $"allegato-{++next}";
                attachments[parts[2]].Add(id);
                if (JsonNode.Parse(body)?["isInline"]?.GetValue<bool>() == true) inline.Add(id);
                json = new JsonObject { ["id"] = id };
            }
            else if (request.Method == HttpMethod.Delete && parts.Length == 5)
            {
                // Come Graph: un allegato che non c'è (più) risponde 404.
                status = attachments[parts[2]].Remove(parts[4]) ? HttpStatusCode.NoContent : HttpStatusCode.NotFound;
            }
            else if (request.Method == HttpMethod.Get && parts.Length == 3 && parts[2] == "invito-1")
            {
                // Un invito di Exchange (eventMessage): il corpo, i campi dell'invito (richiesta senza $select) e l'evento.
                static JsonObject Time(string at) => new() { ["dateTime"] = at, ["timeZone"] = "UTC" };
                json = path.Contains("$expand=microsoft.graph.eventMessage/event")
                    ? new JsonObject { ["id"] = "invito-1", ["event"] = new JsonObject { ["id"] = "evento-1" } }
                    : path.Contains("$select=body")
                    ? new JsonObject
                    {
                        ["@odata.type"] = "#microsoft.graph.eventMessageRequest", ["id"] = "invito-1", ["hasAttachments"] = false, ["toRecipients"] = new JsonArray(),
                        ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = "<p>Riunione di Teams: <a href=\"https://teams.microsoft.com/l/meetup-join/19%3ameeting_prova\">Partecipa</a></p>" },
                    }
                    : new JsonObject
                    {
                        ["@odata.type"] = "#microsoft.graph.eventMessageRequest", ["id"] = "invito-1", ["subject"] = "Revisione del bilancio",
                        ["meetingMessageType"] = "meetingRequest", ["startDateTime"] = Time("2026-10-05T07:30:00.0000000"), ["endDateTime"] = Time("2026-10-05T08:30:00.0000000"),
                        ["location"] = new JsonObject { ["displayName"] = "Sala Verdi" }, ["isAllDay"] = false,
                        ["from"] = new JsonObject { ["emailAddress"] = new JsonObject { ["name"] = "Commercialista", ["address"] = "studio@commercialista.example" } },
                        ["recurrence"] = new JsonObject
                        {
                            ["pattern"] = new JsonObject { ["type"] = "weekly", ["interval"] = 1, ["daysOfWeek"] = new JsonArray("monday") },
                            ["range"] = new JsonObject { ["type"] = "endDate", ["endDate"] = "2026-12-28" },
                        },
                    };
            }
            else if (request.Method == HttpMethod.Get && parts.Length == 4 && parts[3] == "messages")
            {
                // Un elenco con il modulo di contatto di un sito: mittente il sito, Reply-To il cliente.
                static JsonObject Mailbox(string name, string address) => new() { ["emailAddress"] = new JsonObject { ["name"] = name, ["address"] = address } };
                json = new JsonObject
                {
                    ["value"] = new JsonArray(new JsonObject
                    {
                        ["id"] = "modulo-1", ["subject"] = "Richiesta dal sito", ["from"] = Mailbox("Sito dello studio", "wordpress@studio.example"),
                        ["replyTo"] = new JsonArray(Mailbox("Cliente", "cliente@example.com")), ["toRecipients"] = new JsonArray(),
                        ["receivedDateTime"] = "2026-09-26T08:00:00Z", ["isRead"] = false, ["parentFolderId"] = "inbox",
                    }),
                };
            }
            else if (request.Method == HttpMethod.Get && parts.Length == 3)
            {
                json = new JsonObject
                {
                    ["id"] = parts[2], ["subject"] = "Prova", ["isDraft"] = true, ["parentFolderId"] = "bozze",
                    ["attachments"] = new JsonArray(attachments[parts[2]].Select(a => (JsonNode)new JsonObject
                    {
                        ["id"] = a, ["name"] = "a.txt", ["contentType"] = "text/plain", ["size"] = 17, ["isInline"] = inline.Contains(a),
                    }).ToArray()),
                };
            }
            else if (request.Method == HttpMethod.Post && bare.EndsWith("/move"))
                json = new JsonObject { ["id"] = $"{parts[2]}-{JsonNode.Parse(body)?["destinationId"]}" }; // un id nuovo, come Graph
            else if (request.Method == HttpMethod.Post && bare.EndsWith("/send")) status = HttpStatusCode.Accepted;
            else if (request.Method == HttpMethod.Delete) status = HttpStatusCode.NoContent;
            else if (request.Method == HttpMethod.Patch) json = new JsonObject { ["id"] = parts[2] };
            return new HttpResponseMessage(status) { Content = new StringContent(json?.ToJsonString() ?? "", Encoding.UTF8, "application/json") };
        }
    }

    // ---------------------------------------------------------------- Gmail

    static async Task GmailAsync()
    {
        Report.Add("--- Gmail, server IMAP finto");
        using var fake = new FakeImap();
        GmailProvider.TestPort = fake.Port;
        using var gmail = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova" });

        var file = TempFile("a.txt", "allegato di prova");
        var first = await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Prova bozza", Html = "<p>Ciao</p>", Text = "Ciao", AttachmentPaths = [file] });
        Check(fake.Stored.Count == 1 && fake.Stored[0].Flags.Contains(@"\Draft") && fake.Stored[0].Flags.Contains(@"\Seen"), "APPEND nelle Bozze con \\Draft e \\Seen");
        var mime = MimeMessage.Load(new MemoryStream(fake.Stored[0].Raw));
        Check(mime.Subject == "Prova bozza" && mime.To.Mailboxes.Single().Address == "m.rossi@example.com" && mime.From.Mailboxes.Single().Address == "prova@example.com",
              "MIME: oggetto, A e Da");
        Check(mime.HtmlBody?.Contains("<p>Ciao</p>") == true && mime.TextBody?.Contains("Ciao") == true, "MIME: parte HTML e parte di testo");
        Check(mime.Attachments.Select(a => a.ContentDisposition?.FileName).SequenceEqual(["a.txt"]), "MIME: l'allegato a.txt");
        Check(first.Message.StableId == fake.Stored[0].MsgId.ToString() && first.Attachments.Select(a => a.Name).SequenceEqual(["a.txt"]),
              "bozza salvata: X-GM-MSGID e allegato come sul server");
        Check(first.Attachments.Single().Content is { } kept && Encoding.UTF8.GetString(kept) == "allegato di prova", "l'allegato piccolo resta in memoria");

        fake.Commands.Clear();
        var second = await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Prova bozza", Html = "<p>Ciao di nuovo</p>", Text = "Ciao di nuovo",
                                                                    Draft = first, KeptAttachments = first.Attachments });
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(fake.Stored.Count == 1 && fake.Stored[0].MsgId.ToString() != first.Message.StableId, "la bozza di prima è stata eliminata, resta solo la nuova");
        Check(fake.Commands.Any(c => c.Contains("UID EXPUNGE")) && fake.Commands.Any(c => c.Contains("+FLAGS") && c.Contains(@"\Deleted")), "eliminazione con \\Deleted e UID EXPUNGE");
        Check(!fake.Commands.Any(c => c.Contains("BODY.PEEK[")), "seconda bozza: l'allegato tenuto in memoria non si riscarica");
        Check(AttachmentText(fake.Stored[0]) is var copy && copy == "allegato di prova" && MimeMessage.Load(new MemoryStream(fake.Stored[0].Raw)).HtmlBody?.Contains("Ciao di nuovo") == true,
              $"seconda bozza: testo nuovo e l'allegato della bozza di prima, uguale (\"{copy}\")");
        Check(second.Message.StableId == fake.Stored[0].MsgId.ToString(), "bozza salvata: X-GM-MSGID della nuova");

        // Una bozza ripresa dalle Bozze ha gli allegati solo sul server: si riscaricano, sulla connessione delle bozze.
        fake.Commands.Clear();
        var resumed = new SavedDraft { Message = second.Message, Attachments = second.Attachments.Select(a => new Attachment { Id = a.Id, Name = a.Name, ContentType = a.ContentType, Size = a.Size }).ToList() };
        var third = await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Prova bozza", Html = "<p>Terza</p>", Text = "Terza",
                                                                   Draft = resumed, KeptAttachments = resumed.Attachments });
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(fake.Commands.Any(c => c.Contains("BODY.PEEK[")) && AttachmentText(fake.Stored.Single()) == "allegato di prova",
              "bozza ripresa: l'allegato si riscarica ed è uguale all'originale");

        // La connessione cade dopo APPEND, prima della risposta: RunAsync riprova, ma la bozza non si scrive due volte.
        fake.Commands.Clear();
        fake.DropAfterAppend = true;
        var fourth = await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Prova bozza", Html = "<p>Quarta</p>", Text = "Quarta",
                                                                    Draft = third, KeptAttachments = third.Attachments });
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(fake.Commands.Count(c => c.StartsWith("APPEND")) == 1 && fake.Commands.Any(c => c.Contains("HEADER")), "connessione caduta dopo APPEND: ritrovata con il Message-Id, nessun secondo APPEND");
        Check(fake.Stored.Count == 1 && fake.Stored[0].MsgId.ToString() == fourth.Message.StableId && MimeMessage.Load(new MemoryStream(fake.Stored[0].Raw)).HtmlBody?.Contains("Quarta") == true,
              "connessione caduta dopo APPEND: una sola bozza, la nuova");

        // Corpi in anticipo: sulla connessione in sottofondo, con la cartella in sola lettura e BODY.PEEK, che non segna
        // letto; poi il clic trova il corpo in memoria e non chiede nulla al server.
        var bozze = new MailFolder { Id = "[Gmail]/Bozze", Name = "Bozze", Kind = FolderKind.Drafts };
        var listed = await gmail.GetMessagesAsync(bozze, null, null);
        fake.Commands.Clear();
        await gmail.PrefetchAsync(listed.Messages);
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(listed.Messages.Count == 1 && fake.Commands.Any(c => c.StartsWith("EXAMINE")) && !fake.Commands.Any(c => c.StartsWith("SELECT") && c.Contains("Bozze")),
              "anticipo: cartella aperta in sola lettura (EXAMINE)");
        Check(fake.Commands.Any(c => c.Contains("BODY.PEEK[]")) && !fake.Commands.Any(c => c.Contains("BODY[")), "anticipo: messaggio intero con BODY.PEEK[], mai BODY[");
        fake.Commands.Clear();
        var early = await gmail.GetBodyAsync(listed.Messages[0]);
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(!fake.Commands.Any(c => c.Contains("FETCH")) && early.Html.Contains("Quarta"), "clic dopo l'anticipo: corpo già in memoria, nessuna FETCH");

        // Un clic mentre l'anticipo apre ancora la cartella (EXAMINE lento): aspetta quello, il corpo arriva una volta sola.
        fake.Add("INBOX", "Anticipo lento");
        var slowList = await gmail.GetMessagesAsync(new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, null, null);
        fake.Commands.Clear();
        fake.ExamineDelay = TimeSpan.FromMilliseconds(600);
        var prefetching = gmail.PrefetchAsync(slowList.Messages);
        for (int wait = 0; wait < 100 && !fake.Commands.Any(c => c.StartsWith("EXAMINE")); wait++) await Task.Delay(20);
        var clicked = await gmail.GetBodyAsync(slowList.Messages[0]);
        await prefetching;
        fake.ExamineDelay = TimeSpan.Zero;
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(clicked.Html.Contains("Anticipo lento") && fake.Commands.Count(c => c.Contains("BODY.PEEK[]")) == 1,
              "clic mentre l'anticipo apre la cartella (EXAMINE): aspetta quello, una sola FETCH del corpo");

        // Salva con nome (.eml) e Origine: i byte del server così come sono, per l'Origine solo l'inizio.
        fake.Commands.Clear();
        var slowRaw = fake.Folders["INBOX"].Single().Raw;
        var eml = await gmail.GetRawAsync(slowList.Messages[0]);
        var start = await gmail.GetRawAsync(slowList.Messages[0], 100);
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(eml.SequenceEqual(slowRaw) && start.SequenceEqual(slowRaw.Take(100)) && fake.Commands.Any(c => c.Contains("BODY.PEEK[]<0.100>"))
              && !fake.Commands.Any(c => c.StartsWith("SELECT")),
              $"messaggio com'è sul server ({eml.Length} byte, uguali) e per l'Origine solo i primi 100 (BODY.PEEK[]<0.100>), cartella in sola lettura");

        // Un'immagine incollata nel testo: parte di multipart/related con il Content-Id che l'HTML mostra da cid:, non un allegato.
        var picture = Picture;
        await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Foto", Html = $"<p>Guarda</p><img src=\"cid:{picture.ContentId}\">", Text = "Guarda",
                                                         Images = [picture], Draft = fourth, KeptAttachments = fourth.Attachments });
        var withImage = MimeMessage.Load(new MemoryStream(fake.Stored.Single().Raw));
        var related = withImage.Body is { } top ? FindRelated(top) : null;
        var imagePart = related?.OfType<MimePart>().FirstOrDefault(p => p.ContentId == picture.ContentId);
        Check(imagePart != null && imagePart.ContentType.MimeType == "image/png" && !imagePart.IsAttachment && withImage.HtmlBody?.Contains("cid:" + picture.ContentId) == true
              && withImage.Attachments.Select(a => a.ContentDisposition?.FileName).SequenceEqual(["a.txt"]),
              $"immagine del testo: parte related con Content-Id <{picture.ContentId}>, l'allegato a.txt resta l'unico allegato");

        // Scarta: la bozza nata nella finestra esce dalle Bozze, le altre restano.
        var toDiscard = await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Da scartare", Html = "<p>No</p>", Text = "No" });
        int drafts = fake.Stored.Count;
        fake.Commands.Clear();
        await gmail.DiscardDraftAsync(toDiscard);
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(fake.Stored.Count == drafts - 1 && !fake.Stored.Any(m => m.MsgId.ToString() == toDiscard.Message.StableId)
              && fake.Commands.Any(c => c.Contains("+FLAGS") && c.Contains(@"\Deleted")) && fake.Commands.Any(c => c.Contains("UID EXPUNGE")),
              "Scarta: la bozza esce dalle Bozze con \\Deleted e UID EXPUNGE, le altre restano");
        GmailProvider.TestPort = null;
    }

    static MultipartRelated? FindRelated(MimeEntity entity) => entity switch
    {
        MultipartRelated related => related,
        Multipart multipart => multipart.Select(FindRelated).FirstOrDefault(r => r != null),
        _ => null,
    };

    // ---------------------------------------------------------------- Annulla

    /// <summary>
    /// Spostamenti e Annulla: Graph /move e ritorno sull'id restituito; Gmail UID MOVE e ritorno con i nuovi UID, e per i
    /// risultati di "Tutti i messaggi" solo etichette (\Inbox tolta e rimessa, l'etichetta della cartella tolta).
    /// </summary>
    static async Task UndoAsync()
    {
        Report.Add("--- Annulla, server finti");
        var fakeGraph = new FakeGraph();
        GraphProvider.UseTestHandler(fakeGraph);
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@example.com" });
        var deleted = new MailFolder { Id = "cestino", Name = "Posta eliminata", Kind = FolderKind.Deleted };
        var one = new MessageSummary { Id = "m1", FolderId = "inbox", Subject = "Uno" };
        var movedGraph = await graph.MoveAsync([one], deleted);
        var backGraph = await graph.UndoMoveAsync(movedGraph, deleted);
        Report.AddRange(fakeGraph.Requests.Select(r => "      " + r));
        Check(movedGraph.Single().NewId == "m1-cestino" && fakeGraph.Requests.Any(r => r.StartsWith("POST me/messages/m1/move") && r.Contains("cestino")),
              "Graph: /move nel Cestino, con l'id restituito");
        Check(backGraph.Single().NewId == "m1-cestino-inbox" && fakeGraph.Requests.Any(r => r.StartsWith("POST me/messages/m1-cestino/move") && r.Contains("\"inbox\"")),
              "Graph: Annulla, /move sull'id restituito verso la cartella di prima");

        using var fake = new FakeImap();
        GmailProvider.TestPort = fake.Port;
        using var gmail = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova" });
        fake.Add("INBOX", "Primo");
        fake.Add("INBOX", "Secondo");
        var inbox = new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox };
        var trash = new MailFolder { Id = FakeImap.Trash, Name = "Cestino", Kind = FolderKind.Deleted };
        var first = (await gmail.GetMessagesAsync(inbox, null, null)).Messages.Single(m => m.Subject == "Primo");
        fake.Commands.Clear();
        var gone = await gmail.MoveAsync([first], trash);
        var inTrash = fake.Folders[FakeImap.Trash].Single();
        Check(fake.Commands.Any(c => c.StartsWith("UID MOVE " + first.Id)) && fake.Folders["INBOX"].Count == 1 && gone.Single().NewId == inTrash.Uid.ToString(),
              "Gmail: UID MOVE nel Cestino, nuovo UID dal COPYUID");
        var returned = await gmail.UndoMoveAsync(gone, trash);
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        var again = fake.Folders["INBOX"].SingleOrDefault(m => m.MsgId == inTrash.MsgId);
        Check(fake.Commands.Any(c => c.StartsWith("UID MOVE " + inTrash.Uid) && c.Contains("INBOX")) && fake.Folders[FakeImap.Trash].Count == 0 && again != null
              && returned.Single().NewId == again.Uid.ToString() && again.Uid.ToString() != first.Id,
              $"Gmail: Annulla, UID MOVE di ritorno in Posta in arrivo, con il nuovo UID ({first.Id}, poi {again?.Uid})");

        // Un risultato della ricerca in tutte le cartelle, che era in Posta in arrivo: archiviarlo toglie \Inbox, Annulla la
        // rimette; spostato nell'etichetta Lavoro, Annulla toglie l'etichetta. Mai spostamenti o EXPUNGE da "Tutti i messaggi".
        var found = fake.Add(FakeImap.AllMail, "Fattura", @"\Inbox");
        var hit = (await gmail.SearchAsync(null, "fattura")).Messages.Single();
        var archive = new MailFolder { Id = FakeImap.AllMail, Name = "Tutti i messaggi", Kind = FolderKind.Archive };
        fake.Commands.Clear();
        var archived = await gmail.MoveAsync([hit], archive);
        Check(hit.LabelKind == FolderKind.Inbox && fake.Commands.Any(c => c.Contains("-X-GM-LABELS") && c.Contains("Inbox")) && !found.Labels.Contains(@"\Inbox"),
              "Gmail: archiviare un risultato di Tutti i messaggi toglie \\Inbox");
        await gmail.UndoMoveAsync(archived, archive);
        var lavoro = new MailFolder { Id = "Lavoro", Name = "Lavoro", Kind = FolderKind.Other };
        var labelled = await gmail.MoveAsync([hit], lavoro);
        bool wasLabelled = found.Labels.Contains("Lavoro") && !found.Labels.Contains(@"\Inbox");
        await gmail.UndoMoveAsync(labelled, lavoro);
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(fake.Commands.Any(c => c.Contains("+X-GM-LABELS") && c.Contains("Inbox")) && found.Labels.Contains(@"\Inbox") && wasLabelled && !found.Labels.Contains("Lavoro")
              && fake.Commands.Any(c => c.Contains("-X-GM-LABELS") && c.Contains("Lavoro")),
              "Gmail: Annulla rimette \\Inbox con +X-GM-LABELS e toglie l'etichetta Lavoro");
        Check(!fake.Commands.Any(c => c.Contains("EXPUNGE") || c.StartsWith("UID MOVE")), "Gmail: risultati di Tutti i messaggi mai spostati né eliminati (EXPUNGE)");

        // Le etichette tornano com'erano: chi aveva già Lavoro la tiene, sia fra i risultati della ricerca (niente
        // -X-GM-LABELS) sia dalla Posta in arrivo (ritorno con UID COPY, che in Gmail non toglie l'etichetta).
        var both = fake.Add(FakeImap.AllMail, "Contratto", @"\Inbox", "Lavoro");
        var contract = (await gmail.SearchAsync(null, "contratto")).Messages.Single(m => m.Subject == "Contratto");
        fake.Commands.Clear();
        await gmail.UndoMoveAsync(await gmail.MoveAsync([contract], lavoro), lavoro);
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        Check(both.Labels.Contains("Lavoro") && both.Labels.Contains(@"\Inbox") && !fake.Commands.Any(c => c.Contains("-X-GM-LABELS") && c.Contains("Lavoro")),
              "Gmail: Annulla lascia Lavoro a un risultato della ricerca che l'aveva già");
        var quote = fake.Add("INBOX", "Preventivo", "Lavoro");
        var listedQuote = (await gmail.GetMessagesAsync(inbox, null, null)).Messages.Single(m => m.Subject == "Preventivo");
        fake.Commands.Clear();
        var toWork = await gmail.MoveAsync([listedQuote], lavoro);
        var atWork = fake.Folders["Lavoro"].Single(m => m.MsgId == quote.MsgId);
        var home = await gmail.UndoMoveAsync(toWork, lavoro);
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        var inboxAgain = fake.Folders["INBOX"].SingleOrDefault(m => m.MsgId == quote.MsgId);
        Check(fake.Commands.Any(c => c.StartsWith("UID COPY " + atWork.Uid) && c.Contains("INBOX")) && !fake.Commands.Any(c => c.StartsWith("UID MOVE " + atWork.Uid))
              && fake.Folders["Lavoro"].Contains(atWork) && inboxAgain != null && home.Single().NewId == inboxAgain.Uid.ToString(),
              "Gmail: dalla Posta in arrivo a Lavoro e ritorno, chi aveva già Lavoro torna con UID COPY e resta anche in Lavoro");

        // Nella cartella Tutti i messaggi (non nella ricerca) l'elenco non dice chi è in Posta in arrivo: lo dicono le
        // etichette lette allo spostamento, e Annulla rimette \Inbox dopo Sposta in ed Elimina.
        var meeting = fake.Add(FakeImap.AllMail, "Riunione", @"\Inbox");
        var notice = fake.Add(FakeImap.AllMail, "Avviso", @"\Inbox");
        var allListed = (await gmail.GetMessagesAsync(archive, null, null)).Messages;
        var listedMeeting = allListed.Single(m => m.Subject == "Riunione");
        var listedNotice = allListed.Single(m => m.Subject == "Avviso");
        fake.Commands.Clear();
        var meetingMoved = await gmail.MoveAsync([listedMeeting], lavoro);
        bool leftInbox = !meeting.Labels.Contains(@"\Inbox") && meeting.Labels.Contains("Lavoro");
        await gmail.UndoMoveAsync(meetingMoved, lavoro);
        Check(listedMeeting.LabelKind == null && leftInbox && meeting.Labels.Contains(@"\Inbox") && !meeting.Labels.Contains("Lavoro"),
              "Gmail: dalla cartella Tutti i messaggi, Sposta in Lavoro e Annulla: di nuovo in Posta in arrivo, senza Lavoro");
        fake.Commands.Clear();
        var noticeTrashed = await gmail.MoveAsync([listedNotice], trash);
        await gmail.UndoMoveAsync(noticeTrashed, trash);
        Report.AddRange(fake.Commands.Select(c => "      " + c));
        var noticeBack = fake.Folders[FakeImap.AllMail].SingleOrDefault(m => m.MsgId == notice.MsgId);
        Check(noticeBack != null && fake.Commands.Any(c => c.StartsWith($"UID STORE {noticeBack.Uid} ") && c.Contains("+X-GM-LABELS") && c.Contains("Inbox")),
              "Gmail: dalla cartella Tutti i messaggi, Elimina e Annulla rimette \\Inbox");

        // Svuota su Tutti i messaggi manderebbe nel Cestino tutta la casella: rifiutato prima di cercare o spostare.
        fake.Commands.Clear();
        string? refused = null;
        try { await gmail.EmptyFolderAsync(archive, trash); }
        catch (MailException ex) { refused = ex.Message; }
        Check(refused != null && !fake.Commands.Any(c => c.StartsWith("UID SEARCH") || c.StartsWith("UID MOVE") || c.StartsWith("UID STORE")),
              $"Gmail: Svuota su Tutti i messaggi rifiutato, nessuna ricerca né spostamento ({refused})");
        GmailProvider.TestPort = null;
    }

    // ---------------------------------------------------------------- account aziendali

    /// <summary>
    /// Aggiungi account: il tipo riconosciuto dall'indirizzo (MX finti), le app MSAL per account (personale come sempre,
    /// lavoro con la sua app e la sua directory), gli errori di Microsoft e di Google spiegati e il link per l'amministratore.
    /// </summary>
    static async Task AccountsAsync()
    {
        Report.Add("--- Account aziendali");
        MailDns.TestAnswers = (name, type) => (name, type) switch
        {
            ("studio-legale.it", "MX") => [new("studiolegale-it.mail.protection.outlook.com", 0, 0)],
            ("azienda.it", "MX") => [new("aspmx.l.google.com", 0, 1), new("alt1.aspmx.l.google.com", 0, 5)],
            ("nuova-azienda.it", "MX") => [new("smtp.google.com", 0, 1)],
            ("famiglia-rossi.it", "MX") => [new("famiglia-rossi-it.olc.protection.outlook.com", 0, 0)],
            ("negozio.it", "MX") => [new("mx.aruba.it", 0, 10)],
            _ => [],
        };
        try
        {
            foreach (var (email, expected) in new[]
            {
                ("mario@gmail.com", AccountType.Google), ("mario@googlemail.com", AccountType.Google),
                ("mario@hotmail.it", AccountType.MicrosoftPersonal), ("mario@outlook.com", AccountType.MicrosoftPersonal),
                ("mario@live.co.uk", AccountType.MicrosoftPersonal), ("avvocato@studio-legale.it", AccountType.Microsoft365),
                ("anna@azienda.it", AccountType.GoogleWorkspace), ("anna@nuova-azienda.it", AccountType.GoogleWorkspace),
                ("papa@famiglia-rossi.it", AccountType.MicrosoftPersonal), ("ordini@negozio.it", AccountType.Imap),
                ("prof@live.unibo.it", AccountType.Imap), ("info@senza-mx.it", AccountType.Imap),
            })
            {
                var found = await AccountTypes.DetectAsync(email);
                Check(found == expected, $"{email}: {found}");
            }
        }
        finally
        {
            MailDns.TestAnswers = null;
        }
        Check(AccountTypes.LooksValid("mario.rossi@studio.it") && !AccountTypes.LooksValid("mario.rossi") && !AccountTypes.LooksValid("mario @studio.it")
              && !AccountTypes.LooksValid("mario@studio"), "indirizzo: completo sì, senza dominio, con spazi o senza punto no");

        // App MSAL per account: il personale con l'app di sempre e l'autorità dei soli account personali, il lavoro con la sua.
        var saved = Store.Settings.MicrosoftClientId;
        Store.Settings.MicrosoftClientId = MicrosoftAuth.DefaultClientId;
        try
        {
            const string studioApp = "11111111-2222-3333-4444-555555555555";
            var personal = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@outlook.it" };
            var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "avvocato@studio-legale.it", Tenant = "studiolegale.onmicrosoft.com", ClientId = studioApp };
            var school = new MailAccount { Kind = AccountKind.Microsoft, Email = "studente@studio.unibo.it", Tenant = MicrosoftAuth.Organizations };
            Check(MicrosoftAuth.AppOf(personal) == (MicrosoftAuth.DefaultClientId, null) && MicrosoftAuth.AuthorityOf(null).EndsWith("/consumers"),
                  "account personale: app di Missiva, autorità consumers (come sempre)");
            Check(MicrosoftAuth.AppOf(work) == (studioApp, "studiolegale.onmicrosoft.com") && MicrosoftAuth.AuthorityOf(work.Tenant).EndsWith("/studiolegale.onmicrosoft.com"),
                  "account di lavoro con l'app dello studio: la sua app e la sua directory, solo per lui");
            Check(MicrosoftAuth.AppOf(school) == (MicrosoftAuth.DefaultClientId, MicrosoftAuth.Organizations) && !MicrosoftAuth.Blocked(MicrosoftAuth.DefaultClientId, MicrosoftAuth.Organizations)
                  && !MicrosoftAuth.Blocked(MicrosoftAuth.DefaultClientId, "unibo.it") && !MicrosoftAuth.Blocked(studioApp, work.Tenant) && !MicrosoftAuth.Blocked(MicrosoftAuth.DefaultClientId, null),
                  "account di lavoro con l'app di Missiva (registrata per account personali e di lavoro): mai bloccato, come personale e app dello studio");
            // Le app MSAL create come all'accesso, senza chiedere token: nel registro l'autorità di ognuna.
            var logPath = Path.Combine(Store.Dir, "missiva.log");
            long logStart = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
            var authorities = new[] { await MicrosoftAuth.TestAppAsync(personal), await MicrosoftAuth.TestAppAsync(school), await MicrosoftAuth.TestAppAsync(work) };
            string newLog;
            using (var reader = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                reader.Seek(logStart, SeekOrigin.Begin);
                newLog = new StreamReader(reader).ReadToEnd();
            }
            Report.AddRange(newLog.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.Contains("app MSAL")).Select(l => "      " + l.Trim()));
            Check(authorities[0].EndsWith("/consumers") && authorities[1].EndsWith("/organizations")
                  && newLog.Contains($"app MSAL: client {MicrosoftAuth.DefaultClientId}, autorità https://login.microsoftonline.com/organizations")
                  && newLog.Contains($"app MSAL: client {MicrosoftAuth.DefaultClientId}, autorità https://login.microsoftonline.com/consumers")
                  && newLog.Contains($"app MSAL: client {studioApp}, autorità https://login.microsoftonline.com/studiolegale.onmicrosoft.com"),
                  "registro: app di Missiva con autorità organizations per il lavoro e consumers per il personale, l'app dello studio con la sua directory");
            Check(AccountTypes.Describe(personal) == "Microsoft (Outlook.com)" && AccountTypes.Describe(work).Contains(studioApp)
                  && AccountTypes.Describe(new MailAccount { Kind = AccountKind.Gmail, Email = "anna@azienda.it" }) == "Google Workspace"
                  && AccountTypes.Describe(new MailAccount { Kind = AccountKind.Gmail, Email = "anna@gmail.com" }) == "Gmail",
                  $"tipi nelle Impostazioni: \"{AccountTypes.Describe(work)}\"");

            // Errori di Microsoft come arrivano da MSAL (anche dalla finestra di Windows): il codice AADSTS nel messaggio.
            foreach (var (code, words, admin) in new[]
            {
                ("65001", "non ha ancora approvato Missiva", true), ("90094", "solo agli amministratori", true),
                ("90095", "Richiedi approvazione", true), ("50020", "non fa parte della directory", false),
                ("700016", $"L'app {studioApp} non esiste", false), ("50194", "registrata per una sola organizzazione", false),
                ("500200", "account Microsoft personale", false), ("53003", "accesso condizionale", false),
            })
            {
                var error = MicrosoftAuth.Explain(new Microsoft.Identity.Client.MsalServiceException("invalid_grant",
                    $"AADSTS{code}: messaggio inglese di Microsoft. Trace ID: 0"), work.Email, studioApp);
                Check(error.Code == code && error.Text.Contains(words) && error.AdminConsent == admin && !error.Text.Contains("Trace ID"),
                      $"AADSTS{code}: {error.Text}");
            }
            var link = MicrosoftAuth.AdminConsentUrl(studioApp, work.Tenant);
            var anyOrganization = MicrosoftAuth.AdminConsentUrl(MicrosoftAuth.DefaultClientId, MicrosoftAuth.Organizations);
            Report.Add("      " + link);
            Check(link == $"https://login.microsoftonline.com/studiolegale.onmicrosoft.com/v2.0/adminconsent?client_id={studioApp}&scope=https://graph.microsoft.com/.default&redirect_uri=http://localhost"
                  && anyOrganization.StartsWith("https://login.microsoftonline.com/organizations/v2.0/adminconsent?client_id=" + MicrosoftAuth.DefaultClientId),
                  "link del consenso dell'amministratore: con l'ID dell'app e la directory (o organizations)");
            // Con l'app di Missiva: il link con il suo ID e scope .default, che copre in un consenso anche calendario e impostazioni.
            foreach (var code in new[] { "65001", "90094" })
            {
                var missiva = MicrosoftAuth.Explain(new Microsoft.Identity.Client.MsalServiceException("invalid_grant", $"AADSTS{code}: Admin consent required. Trace ID: 0"),
                                                    school.Email, MicrosoftAuth.AppOf(school).ClientId);
                var calendar = MicrosoftAuth.Explain(new Microsoft.Identity.Client.MsalUiRequiredException("invalid_grant", $"AADSTS{code}: consent required"),
                                                     school.Email, MicrosoftAuth.AppOf(school).ClientId, "usare il calendario");
                Check(missiva.AdminConsent && missiva.Text.Contains("leggere e inviare la posta") && calendar.AdminConsent && calendar.Text.Contains("usare il calendario"),
                      $"AADSTS{code} con l'app di Missiva, all'accesso e per il calendario: {calendar.Text}");
            }
            Check(anyOrganization == "https://login.microsoftonline.com/organizations/v2.0/adminconsent?client_id=039bad62-0aa2-4072-9d7c-0b2c261fa04c&scope=https://graph.microsoft.com/.default&redirect_uri=http://localhost"
                  && MicrosoftAuth.AdminConsentScope(MicrosoftAuth.DefaultClientId).Contains("calendario") && MicrosoftAuth.AdminConsentScope(MicrosoftAuth.DefaultClientId).Contains("caselle condivise")
                  && !MicrosoftAuth.AdminConsentScope(studioApp).Contains("calendario"),
                  "link per l'amministratore con l'app di Missiva: client_id=039bad62..., scope .default, il testo dice che copre anche calendario e caselle condivise");
        }
        finally
        {
            Store.Settings.MicrosoftClientId = saved;
        }

        // Google Workspace: app non resa attendibile dall'amministratore, IMAP spento per il dominio.
        Check(GoogleAuth.Refused("admin_policy_enforced", "123.apps.googleusercontent.com").Contains("Controlli API")
              && GoogleAuth.Refused("admin_policy_enforced", "123.apps.googleusercontent.com").Contains("123.apps.googleusercontent.com"),
              "Google: admin_policy_enforced spiegato, con l'ID client da consentire");
        var imapOff = "[ALERT] IMAP access is disabled for your domain. Please contact your domain administrator for questions about this feature. (Failure)";
        Check(GmailProvider.ImapDisabled(imapOff) && GmailProvider.ImapOff("anna@azienda.it").Contains("azienda.it")
              && GmailProvider.ImapOff("anna@azienda.it").Contains("Console di amministrazione") && GmailProvider.ImapOff("anna@gmail.com").Contains("impostazioni di Gmail")
              && !GmailProvider.ImapDisabled("[AUTHENTICATIONFAILED] Invalid credentials (Failure)"),
              "Google: IMAP spento dal dominio spiegato (Workspace e Gmail), le credenziali sbagliate no");
    }

    // ---------------------------------------------------------------- IMAP generico

    /// <summary>
    /// Un account IMAP verso il server finto, senza le estensioni di Gmail, in tre varianti (SPECIAL-USE, MOVE e UIDPLUS;
    /// nessuna delle prime due; nessuna delle tre): cartelle speciali, elenco e corpo senza \Seen, spostamento e Annulla,
    /// bozze, invio con il server SMTP finto (STARTTLS) e copia in Posta inviata; poi un certificato non valido rifiutato e
    /// la password mai in chiaro nelle impostazioni né nel registro.
    /// </summary>
    static async Task ImapAsync()
    {
        Report.Add("--- IMAP generico, server finti");
        const string password = "Password-di-prova-7431!";
        using var smtp = new FakeSmtp();
        ImapProvider.TestPlain = true;
        ImapProvider.TestCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            foreach (var flavor in new[] { new Flavor(Gmail: false), new Flavor(Gmail: false, SpecialUse: false, Move: false), new Flavor(Gmail: false, SpecialUse: false, Move: false, UidPlus: false) })
            {
                using var fake = new FakeImap(flavor);
                var account = new MailAccount
                {
                    Kind = AccountKind.Imap, Email = "mario@studio-prova.it", DisplayName = "Mario Rossi",
                    Imap = new ImapServer
                    {
                        ImapHost = "127.0.0.1", ImapPort = fake.Port, ImapSecurity = MailSecurity.Ssl, SmtpHost = "127.0.0.1", SmtpPort = smtp.Port,
                        SmtpSecurity = MailSecurity.StartTls, UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect(password),
                    },
                };
                using var imap = new ImapProvider(account);
                var who = flavor.ToString();
                var first = new MimeMessage { Subject = "Preventivo", Date = DateTimeOffset.Now.AddHours(-2), Body = new TextPart("html") { Text = "<p>Il preventivo richiesto</p>" } };
                first.From.Add(new MailboxAddress("Fornitore", "preventivi@fornitore.example"));
                first.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.it"));
                fake.Add("INBOX", first);
                var second = new MimeMessage { Subject = "Riunione", Date = DateTimeOffset.Now.AddHours(-1), Body = new TextPart("html") { Text = "<p>Ci vediamo domani</p>" } };
                second.From.Add(new MailboxAddress("Anna", "anna@cliente.example"));
                fake.Add("INBOX", second, "\\Seen");

                var folders = await imap.GetFoldersAsync();
                string? IdOf(FolderKind kind) => folders.FirstOrDefault(f => f.Kind == kind)?.Id;
                Check(IdOf(FolderKind.Inbox) == "INBOX" && IdOf(FolderKind.Drafts) == fake.DraftsFolder && IdOf(FolderKind.Sent) == fake.SentFolder
                      && IdOf(FolderKind.Deleted) == fake.TrashFolder && IdOf(FolderKind.Junk) == fake.JunkFolder
                      && folders.Any(f => f.Id == fake.UserFolder && f.Kind == FolderKind.Other) && (!flavor.SpecialUse || IdOf(FolderKind.Archive) == "Archive"),
                      $"{who}: cartelle speciali {(flavor.SpecialUse ? "da SPECIAL-USE" : "dai nomi (INBOX.Bozze, INBOX.Posta inviata...)")}");
                Check(fake.Password == password && fake.Commands.Contains("AUTHENTICATE") && !fake.Commands.Any(c => c.Contains(password)),
                      $"{who}: accesso con la password dell'account, che nel registro dei comandi non compare");

                var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
                var listed = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
                Check(listed.Select(m => m.Subject).SequenceEqual(["Riunione", "Preventivo"]) && !listed[1].IsRead && listed[1].From.Email == "preventivi@fornitore.example",
                      $"{who}: elenco dal più recente, con mittente e letti");
                fake.Commands.Clear();
                var body = await imap.GetBodyAsync(listed[1]);
                Report.AddRange(fake.Commands.Select(c => "      " + c));
                Check(body.Html.Contains("Il preventivo richiesto") && fake.Commands.Any(c => c.Contains("BODY.PEEK[]")) && !fake.Commands.Any(c => c.Contains("BODY["))
                      && !fake.Commands.Any(c => c.Contains("\\Seen")) && !fake.Folders["INBOX"].First(m => m.Uid.ToString() == listed[1].Id).Has("\\Seen"),
                      $"{who}: corpo con BODY.PEEK[], il messaggio resta da leggere");

                var clienti = folders.First(f => f.Id == fake.UserFolder);
                fake.Commands.Clear();
                var moved = await imap.MoveAsync([listed[1]], clienti);
                var afterMove = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
                Report.AddRange(fake.Commands.Select(c => "      " + c));
                bool viaMove = fake.Commands.Any(c => c.StartsWith("UID MOVE"));
                bool viaCopy = fake.Commands.Any(c => c.StartsWith("UID COPY") || c.StartsWith("COPY ")) && fake.Commands.Any(c => c.StartsWith("UID STORE") && c.Contains("\\Deleted"));
                bool expungedOnly = fake.Commands.Any(c => c.StartsWith("UID EXPUNGE " + listed[1].Id));
                Check(fake.Folders[fake.UserFolder].Count == 1 && !fake.Commands.Any(c => c == "EXPUNGE") && afterMove.Select(m => m.Subject).SequenceEqual(["Riunione"])
                      && (flavor.Move ? viaMove : viaCopy && (flavor.UidPlus ? expungedOnly : !fake.Commands.Any(c => c.Contains("EXPUNGE")))),
                      $"{who}: spostamento {(flavor.Move ? "con UID MOVE" : flavor.UidPlus ? "con UID COPY, \\Deleted e UID EXPUNGE del solo messaggio" : "con COPY e \\Deleted, senza EXPUNGE (niente UIDPLUS)")}; nell'elenco non c'è più");
                fake.Commands.Clear();
                var back = await imap.UndoMoveAsync(moved, clienti);
                Report.AddRange(fake.Commands.Select(c => "      " + c));
                var returned = fake.Folders["INBOX"].Where(m => !m.Has("\\Deleted")).ToList();
                Check(returned.Count == 2 && fake.Folders[fake.UserFolder].Count(m => !m.Has("\\Deleted")) == 0 && back.Single().NewId == returned.Last().Uid.ToString()
                      && (flavor.UidPlus || fake.Commands.Any(c => c.Contains("HEADER"))),
                      $"{who}: Annulla, di nuovo in Posta in arrivo{(flavor.UidPlus ? "" : " (ritrovato con il Message-Id, senza COPYUID)")}");

                var draft = await imap.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Bozza IMAP", Html = "<p>Prima</p>", Text = "Prima" });
                fake.Commands.Clear();
                var again = await imap.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Bozza IMAP", Html = "<p>Seconda</p>", Text = "Seconda", Draft = draft });
                Report.AddRange(fake.Commands.Select(c => "      " + c));
                var live = fake.Stored.Where(m => !m.Has("\\Deleted")).ToList();
                Check(fake.Commands.Any(c => c.StartsWith("APPEND") && c.Contains(fake.DraftsFolder) && c.Contains("\\Draft")) && live.Count == 1
                      && MimeMessage.Load(new MemoryStream(live[0].Raw)).HtmlBody?.Contains("Seconda") == true && again.Message.StableId == MimeMessage.Load(new MemoryStream(live[0].Raw)).MessageId,
                      $"{who}: bozza con APPEND \\Draft nelle Bozze, la precedente tolta (ritrovata con il Message-Id)");

                smtp.Commands.Clear();
                smtp.Received.Clear();
                fake.Commands.Clear();
                await imap.SendAsync(new OutgoingMessage { To = To, Subject = "Invio IMAP", Html = "<p>Parto</p>", Text = "Parto", Draft = again });
                Report.AddRange(smtp.Commands.Select(c => "      smtp: " + c));
                var starttls = smtp.Commands.FindIndex(c => c.StartsWith("STARTTLS"));
                var auth = smtp.Commands.FindIndex(c => c.StartsWith("AUTH"));
                Check(!smtp.AuthBeforeTls && starttls >= 0 && auth > starttls && smtp.Password == password && smtp.Received.Count == 1
                      && Encoding.UTF8.GetString(smtp.Received[0]).Contains("Subject: Invio IMAP"),
                      $"{who}: invio SMTP con STARTTLS, AUTH solo dopo, con la password dell'account");
                var sent = fake.Folders[fake.SentFolder];
                Check(sent.Count == 1 && sent[0].Has("\\Seen") && MimeMessage.Load(new MemoryStream(sent[0].Raw)).Subject == "Invio IMAP"
                      && !fake.Stored.Any(m => !m.Has("\\Deleted")),
                      $"{who}: copia in Posta inviata con APPEND e \\Seen, la bozza esce dalle Bozze");
            }

            MailAccount AccountOf(FakeImap server) => new()
            {
                Kind = AccountKind.Imap, Email = "mario@studio-prova.it",
                Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = server.Port, SmtpHost = "127.0.0.1", SmtpPort = smtp.Port, SmtpSecurity = MailSecurity.StartTls,
                                        UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect(password) },
            };

            // Senza Cestino (casella nuova su un server che lo crea solo al primo uso, o un nome che Missiva non conosce):
            // Svuota di una cartella normale non elimina per sempre, e il Cestino si crea una volta sola, come in Thunderbird.
            using (var bare = new FakeImap(new Flavor(Gmail: false)))
            {
                bare.Folders.Remove(bare.TrashFolder);
                bare.Add(bare.UserFolder, "Contratto Rossi");
                using var imap = new ImapProvider(AccountOf(bare));
                var folders = await imap.GetFoldersAsync();
                var clienti = folders.First(f => f.Id == bare.UserFolder);
                string? notEmptied = null;
                try { await imap.EmptyFolderAsync(clienti, null); }
                catch (MailException ex) { notEmptied = ex.Message; }
                Check(!folders.Any(f => f.Kind == FolderKind.Deleted) && notEmptied?.Contains("non ha un Cestino") == true
                      && bare.Folders[bare.UserFolder].Count(m => !m.Has("\\Deleted")) == 1 && !bare.Commands.Any(c => c.Contains("EXPUNGE")),
                      "senza Cestino: Svuota di una cartella normale rifiutato, nessun messaggio eliminato");
                var trash = await imap.EnsureTrashAsync();
                var after = await imap.GetFoldersAsync();
                await imap.EmptyFolderAsync(clienti, trash);
                var again = await imap.EnsureTrashAsync();
                Check(trash?.Id == "Trash" && trash.Kind == FolderKind.Deleted && after.Any(f => f.Kind == FolderKind.Deleted && f.Id == "Trash")
                      && bare.Folders["Trash"].Count == 1 && bare.Folders[bare.UserFolder].Count(m => !m.Has("\\Deleted")) == 0
                      && again?.Id == "Trash" && bare.Commands.Count(c => c.StartsWith("CREATE")) == 1,
                      "Cestino creato al primo bisogno (CREATE Trash, una volta sola), poi Svuota sposta lì");
            }

            // Senza SPECIAL-USE le cartelle speciali si prendono dal nome solo in cima: INBOX.Clienti.Cestino (elencata prima
            // di INBOX.Cestino) e INBOX.Clienti.Rossi.Archivio restano cartelle dell'utente.
            using (var deep = new FakeImap(new Flavor(Gmail: false, SpecialUse: false)))
            {
                var trashed = deep.Folders[deep.TrashFolder];
                deep.Folders.Remove(deep.TrashFolder);
                deep.Folders["INBOX.Clienti.Cestino"] = new();
                deep.Folders[deep.TrashFolder] = trashed;
                deep.Folders["INBOX.Clienti.Rossi.Archivio"] = new();
                using var provider = new ImapProvider(AccountOf(deep));
                var folders = await provider.GetFoldersAsync();
                var listedFirst = deep.Folders.Keys.ToList().IndexOf("INBOX.Clienti.Cestino") < deep.Folders.Keys.ToList().IndexOf(deep.TrashFolder);
                Check(folders.Single(f => f.Kind == FolderKind.Deleted).Id == deep.TrashFolder && !folders.Any(f => f.Kind == FolderKind.Archive)
                      && folders.Any(f => f.Id == "INBOX.Clienti.Rossi.Archivio" && f.Kind == FolderKind.Other) && folders.Any(f => f.Id == "INBOX.Clienti.Cestino" && f.Kind == FolderKind.Other),
                      $"cartelle speciali dal nome solo in cima: Cestino {folders.Single(f => f.Kind == FolderKind.Deleted).Id}{(listedFirst ? " (INBOX.Clienti.Cestino elencata prima)" : "")}, nessun Archivio");
            }

            // Connessione caduta dopo che il server ha fatto la scrittura ma prima della risposta: la ripresa non la rifà
            // (COPY senza MOVE, copia in Posta inviata), cerca il Message-Id.
            using (var dropping = new FakeImap(new Flavor(Gmail: false, Move: false)))
            {
                dropping.Add("INBOX", "Da spostare");
                using var provider = new ImapProvider(AccountOf(dropping));
                var folders = await provider.GetFoldersAsync();
                var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
                var clienti = folders.First(f => f.Id == dropping.UserFolder);
                var message = (await provider.GetMessagesAsync(inbox, null, null)).Messages.Single();
                dropping.Commands.Clear();
                dropping.DropAfterCopy = true;
                await provider.MoveAsync([message], clienti);
                int copies = dropping.Commands.Count(c => c.StartsWith("UID COPY"));
                Check(dropping.Folders[dropping.UserFolder].Count == 1 && copies == 1 && dropping.Folders["INBOX"].All(m => m.Has("\\Deleted")),
                      $"spostamento senza MOVE, connessione caduta dopo la COPY: {copies} COPY, {dropping.Folders[dropping.UserFolder].Count} copia nella cartella di arrivo");
                smtp.Received.Clear();
                dropping.DropAfterAppend = true;
                await provider.SendAsync(new OutgoingMessage { To = To, Subject = "Copia unica", Html = "<p>Una</p>", Text = "Una" });
                var sentCopies = dropping.Folders[dropping.SentFolder].Count(m => MimeMessage.Load(new MemoryStream(m.Raw)).Subject == "Copia unica");
                Check(smtp.Received.Count == 1 && sentCopies == 1 && !dropping.DropAfterAppend,
                      $"copia in Posta inviata, connessione caduta dopo APPEND: {sentCopies} copia (la ripresa l'ha ritrovata con il Message-Id)");
            }

            // Ricerca in tutte le cartelle: in sottofondo (EXAMINE), SEARCH una volta per cartella anche con due richieste
            // uguali insieme (i suggerimenti: messaggi e file), busta e struttura solo per la pagina, Invio che riusa i
            // risultati; una ricerca che nessuno aspetta più si ferma.
            using (var many = new FakeImap(new Flavor(Gmail: false)))
            {
                for (int k = 0; k < 40; k++) many.Add("INBOX", $"Fattura {k} di settembre");
                for (int k = 0; k < 25; k++) many.Add(many.UserFolder, $"Fattura cliente {k}");
                many.Add(many.UserFolder, "Altro argomento");
                using var provider = new ImapProvider(AccountOf(many));
                IMailProvider imap = provider;
                await imap.GetFoldersAsync();
                many.Commands.Clear();
                var both = await Task.WhenAll(imap.SearchAsync(null, "fattura"), imap.SearchAsync(null, "fattura"));
                int searches = many.Commands.Count(c => c.StartsWith("UID SEARCH"));
                int structures = many.Commands.Count(c => c.Contains("BODYSTRUCTURE"));
                bool examined = many.Commands.Any(c => c.StartsWith("EXAMINE")) && !many.Commands.Any(c => c.StartsWith("SELECT"));
                many.Commands.Clear();
                var enter = await imap.SearchAsync(null, "fattura");
                bool reused = !many.Commands.Any(c => c.StartsWith("UID SEARCH"));
                var second = await imap.SearchAsync(null, "fattura", enter.Next);
                Report.Add($"      {searches} UID SEARCH, {structures} FETCH con la struttura; Invio: {(reused ? "risultati riusati" : "ricerca rifatta")}");
                Check(both[0].Messages.Count == 50 && both[1].Messages.Count == 50 && searches == many.Folders.Count - 2 && structures <= 4 && examined && reused
                      && enter.Next != null && second.Messages.Count == 15 && second.Next == null
                      && enter.Messages.Concat(second.Messages).Select(m => m.FolderId + m.Id).Distinct().Count() == 65
                      && enter.Messages.Any(m => m.FolderName == "Posta in arrivo") && second.Messages.All(m => m.Subject.StartsWith("Fattura")),
                      $"IMAP, ricerca in tutte le cartelle: due richieste uguali, una SEARCH per cartella ({searches}), struttura solo per la pagina, Invio riusa, pagina dopo con i 15 che restano");
                // Una ricerca che nessuno aspetta più si ferma fra una cartella e l'altra. Il server trattiene una risposta finché
                // la prova non ha annullato, così si sa dov'era la ricerca in quel momento: prima la prova annullava subito e
                // contava "al più 5 SEARCH" con quella dopo, ma la ricerca annullata, secondo i tempi, era già entrata nella
                // prima cartella (una volta su tre 6 SEARCH: audit dell'11 ottobre, C-41). E si aspetta la sua fine vera
                // (TestEverywhere), non 300 ms.
                async Task<(bool Cancelled, bool Stopped, int Searches)> Abandon(string heldCommand)
                {
                    many.Commands.Clear();
                    var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    many.Hold = async command =>
                    {
                        if (command != heldCommand) return;
                        many.Hold = null; // solo la prima volta
                        reached.TrySetResult();
                        await release.Task;
                    };
                    using var stop = new CancellationTokenSource();
                    var abandoned = imap.SearchAsync(null, "argomento", cancel: stop.Token);
                    // Se la ricerca finisce senza arrivare a quel comando (quella di prima riusata), non c'è nulla da annullare.
                    if (await Task.WhenAny(reached.Task, abandoned) == reached.Task) stop.Cancel();
                    bool cancelled = false, stopped = false;
                    try { await abandoned; }
                    catch (OperationCanceledException) { cancelled = true; }
                    many.Hold = null;
                    release.SetResult();
                    try { await provider.TestEverywhere!; }
                    catch (OperationCanceledException) { stopped = true; }
                    return (cancelled, stopped, many.Commands.Count(c => c.StartsWith("UID SEARCH")));
                }
                var atList = await Abandon("LIST");        // annullata prima dell'elenco delle cartelle: non ne apre nessuna
                var inFirst = await Abandon("UID SEARCH"); // annullata dentro la prima cartella: finisce quella e non apre la seconda
                many.Commands.Clear();
                var after = await imap.SearchAsync(null, "argomento");
                int afterSearches = many.Commands.Count(c => c.StartsWith("UID SEARCH"));
                Check(atList is (true, true, 0) && inFirst is (true, true, 1) && after.Messages.Count == 1 && afterSearches == many.Folders.Count - 2,
                      $"ricerca che nessuno aspetta più: annullata prima dell'elenco delle cartelle si ferma senza aprirne nessuna ({atList.Searches} SEARCH), annullata dentro la prima " +
                      $"finisce quella e si ferma ({inFirst.Searches} SEARCH); la ricerca dopo trova il messaggio con una SEARCH per cartella ({afterSearches})");
            }

            // Un server con un certificato che Windows non riconosce: rifiutato, con la spiegazione.
            ImapProvider.TestPlain = false;
            using var tls = new FakeTls();
            using var wrong = new ImapProvider(new MailAccount
            {
                Kind = AccountKind.Imap, Email = "mario@studio-prova.it",
                Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = tls.Port, UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect(password) },
            });
            string? refused = null;
            try { await wrong.GetFoldersAsync(); }
            catch (MailException ex) { refused = ex.Message; }
            Check(refused?.Contains("certificato") == true && refused.Contains("non si collega"), $"certificato non valido rifiutato: {refused}");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            ImapProvider.TestCertificate = null;
        }

        // La password: cifrata nelle impostazioni (DPAPI), mai in chiaro, né lì né nel registro.
        var saved = new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.it", Imap = new ImapServer { ProtectedPassword = Store.Protect(password) } };
        var json = System.Text.Json.JsonSerializer.Serialize(saved);
        var log = File.Exists(Path.Combine(Store.Dir, "missiva.log")) ? File.ReadAllText(Path.Combine(Store.Dir, "missiva.log")) : "";
        Check(!json.Contains(password) && Store.Unprotect(saved.Imap.ProtectedPassword!) == password && !log.Contains(password),
              "password di prova: cifrata nelle impostazioni, assente dal registro");
    }

    /// <summary>
    /// Gestisci account di un account IMAP, verso i server finti che accettano una password sola: la vecchia rifiutata
    /// all'avvio (rimedio "Aggiorna password"), Cambia password con una sbagliata che non salva nulla, poi quella giusta
    /// provata e salvata cifrata, con lo stesso account (firme e categorie restano) e le connessioni rifatte.
    /// </summary>
    static async Task ManageAsync()
    {
        Report.Add("--- Gestisci account (IMAP), server finti");
        const string oldPassword = "Vecchia-password-2026!", newPassword = "Nuova-password-dicembre-8842!", wrongPassword = "Sbagliata-1234!";
        using var smtp = new FakeSmtp { ExpectedPassword = newPassword };
        using var fake = new FakeImap(new Flavor(Gmail: false)) { ExpectedPassword = newPassword };
        ImapProvider.TestPlain = true;
        ImapProvider.TestCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            var server = new ImapServer
            {
                ImapHost = "127.0.0.1", ImapPort = fake.Port, SmtpHost = "127.0.0.1", SmtpPort = smtp.Port, SmtpSecurity = MailSecurity.StartTls,
                UserName = "studio@pec-prova.it", ProtectedPassword = Store.Protect(oldPassword),
            };
            var account = new MailAccount { Kind = AccountKind.Imap, Email = "studio@pec-prova.it", DisplayName = "Studio Rossi", Imap = server };
            var id = account.Id;
            var settings = new Settings { Accounts = { account } };
            settings.Signatures[id] = new Signature { Rtf = @"{\rtf1 Studio Rossi}" };
            settings.LocalCategories[id] = ["Clienti"];
            using var imap = new ImapProvider(account);

            // All'avvio la password scaduta: l'errore spiegato porta il rimedio "Aggiorna password".
            AccountFix fix = AccountFix.None;
            string? refused = null;
            try { await imap.GetFoldersAsync(); }
            catch (Exception ex) { fix = MailException.FixOf(ex); refused = ex.Message; }
            Check(fix == AccountFix.Password && refused?.Contains("ha rifiutato nome utente o password") == true && fake.Password == oldPassword,
                  $"password vecchia rifiutata dal server: rimedio Aggiorna password ({refused})");

            // Cambia password con una sbagliata: l'errore spiegato, e l'account resta com'era.
            var before = server.ProtectedPassword;
            string? wrong = null;
            try { await ImapProvider.ChangeServerAsync(account, Copy(server), wrongPassword, CancellationToken.None); }
            catch (MailException ex) { wrong = ex.Message; }
            Check(wrong?.Contains("ha rifiutato nome utente o password") == true && ReferenceEquals(account.Imap, server) && account.Imap.ProtectedPassword == before
                  && Store.Unprotect(account.Imap.ProtectedPassword!) == oldPassword,
                  "Cambia password sbagliata: rifiutata con la spiegazione, nulla salvato");

            // Quella giusta, ma Annulla durante la prova: anche se il server accetta, l'account resta com'era.
            using (var stopping = new CancellationTokenSource())
            {
                var change = ImapProvider.ChangeServerAsync(account, Copy(server), newPassword, stopping.Token);
                stopping.Cancel();
                // Annullata, anche spiegata come errore di collegamento: solo questi due. Prima valeva qualunque errore, e un
                // riferimento nullo dentro ChangeServerAsync avrebbe fatto passare il controllo (audit dell'11 ottobre, C-43).
                string? interrupted = null;
                try { await change; }
                catch (Exception ex) when (ex is OperationCanceledException or MailException) { interrupted = ex.GetType().Name; }
                Check(interrupted != null && ReferenceEquals(account.Imap, server) && Store.Unprotect(account.Imap.ProtectedPassword!) == oldPassword,
                      "Annulla durante la prova del server (anche con la password giusta): nulla cambia, l'account resta con la password di prima");
            }

            // Quella giusta: provata con IMAP e SMTP, salvata cifrata; stesso account.
            await ImapProvider.ChangeServerAsync(account, Copy(server), newPassword, CancellationToken.None);
            Check(account.Imap != server && Store.Unprotect(account.Imap!.ProtectedPassword!) == newPassword && account.Imap.ProtectedPassword != before
                  && fake.Password == newPassword && smtp.Password == newPassword,
                  "Cambia password giusta: provata con IMAP e SMTP, ProtectedPassword cambiata");
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            var logPath = Path.Combine(Store.Dir, "missiva.log");
            var log = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
            Check(!json.Contains(newPassword) && !json.Contains(wrongPassword) && !json.Contains(oldPassword) && !log.Contains(newPassword) && !log.Contains(wrongPassword)
                  && !log.Contains(oldPassword) && log.Contains("account IMAP studio@pec-prova.it cambiato, con una password nuova"),
                  "le tre password non compaiono né nelle impostazioni né nel registro");
            Check(account.Id == id && settings.SignatureFor(account, reply: false) != null && settings.LocalCategories[account.Id].SequenceEqual(["Clienti"]),
                  "stesso account: firma e categorie restano");

            // Le connessioni si rifanno con la password nuova: le cartelle arrivano.
            await imap.ResetConnectionsAsync();
            var folders = await imap.GetFoldersAsync();
            Check(folders.Any(f => f.Kind == FolderKind.Inbox) && MailException.FixOf(null) == AccountFix.None, "dopo Salva le cartelle arrivano con la password nuova");

            // Server cambiato a mano: stessi controlli di Aggiungi account.
            Check(UI.AccountWindow.ParseServer("imap.studio.it", "993", false, "smtp.studio.it", "465", false, "studio@studio.it") is { ImapSecurity: MailSecurity.Ssl }
                  && UI.AccountWindow.ParseServer("imap studio.it", "993", false, "smtp.studio.it", "465", false, "x") == null
                  && UI.AccountWindow.ParseServer("imap.studio.it", "99999", false, "smtp.studio.it", "465", false, "x") == null
                  && UI.AccountWindow.ParseServer("https://imap.studio.it", "993", false, "smtp.studio.it", "465", false, "x") == null
                  && UI.AccountWindow.ParseServer("imap.studio.it", "143", true, "smtp.studio.it", "587", true, "x") is { ImapSecurity: MailSecurity.StartTls, SmtpSecurity: MailSecurity.StartTls },
                  "server scritti a mano: nomi, porte e sicurezza controllati (sempre SSL/TLS o STARTTLS)");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            ImapProvider.TestCertificate = null;
        }

        static ImapServer Copy(ImapServer s) => new()
        {
            ImapHost = s.ImapHost, ImapPort = s.ImapPort, ImapSecurity = s.ImapSecurity, SmtpHost = s.SmtpHost, SmtpPort = s.SmtpPort,
            SmtpSecurity = s.SmtpSecurity, UserName = s.UserName, ServerSavesSent = s.ServerSavesSent,
        };
    }

    /// <summary>
    /// Configurazione automatica con un gestore HTTP finto, un DNS finto e collegamenti di prova finti: gli XML di
    /// Thunderbird letti bene, l'ordine dei tentativi, il ripiego su MX, SRV e nomi comuni, mai un server senza TLS.
    /// </summary>
    static async Task AutoconfigAsync()
    {
        Report.Add("--- Configurazione automatica, HTTP e DNS finti");
        static string Xml(string imapHost, int imapPort, string imapSocket, string smtpHost, int smtpPort, string smtpSocket, string user = "%EMAILADDRESS%") => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <clientConfig version="1.1">
              <emailProvider id="prova">
                <domain>prova</domain>
                <incomingServer type="pop3"><hostname>pop.%EMAILDOMAIN%</hostname><port>995</port><socketType>SSL</socketType><username>%EMAILADDRESS%</username><authentication>password-cleartext</authentication></incomingServer>
                <incomingServer type="imap"><hostname>chiaro.%EMAILDOMAIN%</hostname><port>143</port><socketType>plain</socketType><username>%EMAILADDRESS%</username><authentication>password-cleartext</authentication></incomingServer>
                <incomingServer type="imap"><hostname>{imapHost}</hostname><port>{imapPort}</port><socketType>{imapSocket}</socketType><username>{user}</username><authentication>password-cleartext</authentication></incomingServer>
                <outgoingServer type="smtp"><hostname>{smtpHost}</hostname><port>{smtpPort}</port><socketType>{smtpSocket}</socketType><username>{user}</username><authentication>password-cleartext</authentication></outgoingServer>
              </emailProvider>
            </clientConfig>
            """;
        var pages = new Dictionary<string, string>
        {
            ["https://autoconfig.dominio-a.it/mail/config-v1.1.xml"] = Xml("imaps.%EMAILDOMAIN%", 993, "SSL", "smtps.%EMAILDOMAIN%", 465, "SSL"),
            ["https://autoconfig.thunderbird.net/v1.1/dominio-b.it"] = Xml("imap.dominio-b.it", 143, "STARTTLS", "smtp.dominio-b.it", 587, "STARTTLS", "%EMAILLOCALPART%"),
            ["https://autoconfig.thunderbird.net/v1.1/hosting-prova.net"] = Xml("imap.hosting-prova.net", 993, "SSL", "smtp.hosting-prova.net", 587, "STARTTLS"),
            ["https://autoconfig.dominio-f.it/mail/config-v1.1.xml"] = Xml("imap.dominio-f.it", 143, "plain", "smtp.dominio-f.it", 25, "plain"),
        };
        var requests = new List<string>();
        MailAutoconfig.UseTestHandler(new FakeHttp(request =>
        {
            var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            requests.Add(url);
            return pages.TryGetValue(url, out var xml)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        MailDns.TestAnswers = (name, type) => (name, type) switch
        {
            ("dominio-c.it", "MX") => [new("mx01.hosting-prova.net", 0, 10)],
            ("studio-rossi-prova.it", "MX") => [new("mx.pec.aruba.it", 0, 10)], // un dominio proprio con la PEC di Aruba
            ("_imaps._tcp.dominio-d.it", "SRV") => [new("mail.dominio-d.it", 993, 0)],
            ("_submission._tcp.dominio-d.it", "SRV") => [new("mail.dominio-d.it", 587, 0)],
            ("_imaps._tcp.dominio-e.it", "SRV") => [new(".", 0, 0)], // "servizio assente" (RFC 2782)
            // Una rete ostile che risponde agli SRV con i server di un altro dominio (con un certificato valido per il loro nome).
            ("_imaps._tcp.dominio-g.it", "SRV") or ("_imaps._tcp.dominio-h.it", "SRV") => [new("imap.altro.net", 993, 0)],
            ("_submissions._tcp.dominio-g.it", "SRV") or ("_submissions._tcp.dominio-h.it", "SRV") => [new("smtp.altro.net", 465, 0)],
            _ => [],
        };
        var probes = new List<string>();
        MailAutoconfig.TestProbe = (host, port, security, imap) =>
        {
            probes.Add($"{host}:{port}");
            return Task.FromResult(host == "mail.dominio-e.it" && port == 993 || host == "smtp.dominio-e.it" && port == 587
                                   || host == "imap.dominio-h.it" && port == 993 || host == "smtp.dominio-h.it" && port == 465);
        };
        try
        {
            async Task<MailAutoconfig.Found?> Find(string email)
            {
                requests.Clear();
                probes.Clear();
                return await MailAutoconfig.FindAsync(email, CancellationToken.None);
            }
            string Describe(MailAutoconfig.Found? f) => f == null ? "nessuna" :
                $"{f.Server.ImapHost}:{f.Server.ImapPort} {f.Server.ImapSecurity}, {f.Server.SmtpHost}:{f.Server.SmtpPort} {f.Server.SmtpSecurity}, utente {f.Server.UserName} ({f.Source})";

            var a = await Find("mario@dominio-a.it");
            Check(a?.Server is { ImapHost: "imaps.dominio-a.it", ImapPort: 993, ImapSecurity: MailSecurity.Ssl, SmtpHost: "smtps.dominio-a.it", SmtpPort: 465, UserName: "mario@dominio-a.it" }
                  && requests.SequenceEqual(["https://autoconfig.dominio-a.it/mail/config-v1.1.xml"]),
                  $"autoconfig del dominio, primo tentativo: {Describe(a)}; il server IMAP senza TLS ignorato");
            var b = await Find("anna@dominio-b.it");
            Report.AddRange(requests.Select(r => "      " + r));
            Check(b?.Server is { ImapPort: 143, ImapSecurity: MailSecurity.StartTls, SmtpPort: 587, SmtpSecurity: MailSecurity.StartTls, UserName: "anna" }
                  && requests.SequenceEqual(["https://autoconfig.dominio-b.it/mail/config-v1.1.xml", "https://dominio-b.it/.well-known/autoconfig/mail/config-v1.1.xml",
                                             "https://autoconfig.thunderbird.net/v1.1/dominio-b.it"]),
                  $"database di Thunderbird dopo l'autoconfig del dominio e .well-known, con %EMAILLOCALPART%: {Describe(b)}");
            var c = await Find("luca@dominio-c.it");
            Check(c?.Server.ImapHost == "imap.hosting-prova.net" && requests.Last() == "https://autoconfig.thunderbird.net/v1.1/hosting-prova.net",
                  $"dal server di posta (MX mx01.hosting-prova.net) al database di Thunderbird: {Describe(c)}");
            var d = await Find("sara@dominio-d.it");
            Check(d?.Server is { ImapHost: "mail.dominio-d.it", ImapPort: 993, ImapSecurity: MailSecurity.Ssl, SmtpPort: 587, SmtpSecurity: MailSecurity.StartTls }
                  && d.Source.Contains("SRV") && probes.Count == 0, $"record SRV (_imaps._tcp, _submission._tcp): {Describe(d)}");
            var e = await Find("paolo@dominio-e.it");
            Report.Add("      provati: " + string.Join(", ", probes));
            Check(e?.Server is { ImapHost: "mail.dominio-e.it", ImapPort: 993, SmtpHost: "smtp.dominio-e.it", SmtpPort: 587, SmtpSecurity: MailSecurity.StartTls }
                  && probes.First() == "imap.dominio-e.it:993", $"SRV \".\" ignorato, poi nomi comuni provati con un collegamento: {Describe(e)}");
            var f = await Find("gino@dominio-f.it");
            Check(f == null, "configurazione con soli server senza TLS: non usata, e nessun nome comune risponde");
            var g = await Find("ugo@dominio-g.it");
            Check(g is { Foreign: true, Server.ImapHost: "imap.altro.net" } && probes.Count > 0 && !MailAutoconfig.InDomain("imap.altro.net", "dominio-g.it")
                  && MailAutoconfig.InDomain("mail.dominio-d.it", "dominio-d.it"),
                  $"SRV verso un altro dominio: prima i nomi del dominio provati, poi l'SRV segnato da confermare: {Describe(g)}");
            var h = await Find("rita@dominio-h.it");
            Check(h is { Foreign: false, Server.ImapHost: "imap.dominio-h.it", Server.SmtpHost: "smtp.dominio-h.it" },
                  $"SRV verso un altro dominio e server del dominio che rispondono: questi, non l'SRV: {Describe(h)}");
            // Posta certificata: i gestori noti, dal dominio o dal server di posta, prima del database di Thunderbird per aruba.it.
            var pec = await Find("prova@pec.it");
            var own = await Find("studio@studio-rossi-prova.it");
            Report.AddRange(requests.Select(r => "      " + r));
            Check(pec?.Server is { ImapHost: "imaps.pec.aruba.it", ImapPort: 993, ImapSecurity: MailSecurity.Ssl, SmtpHost: "smtps.pec.aruba.it", SmtpPort: 465, UserName: "prova@pec.it" }
                  && own?.Server is { ImapHost: "imaps.pec.aruba.it", SmtpHost: "smtps.pec.aruba.it", UserName: "studio@studio-rossi-prova.it" } && own.Source.Contains("dal server di posta")
                  && !requests.Any(r => r.Contains("thunderbird.net/v1.1/aruba.it")) && !own.Foreign,
                  $"PEC: prova@pec.it e un dominio con MX mx.pec.aruba.it portano a imaps.pec.aruba.it e smtps.pec.aruba.it: {Describe(own)}");
        }
        finally
        {
            MailDns.TestAnswers = null;
            MailAutoconfig.TestProbe = null;
        }
    }

    // ---------------------------------------------------------------- Reply-To e mittente verificato

    /// <summary>
    /// Rispondi a Reply-To (IMAP e Gmail dalla busta, Graph dal campo replyTo, fino al PATCH della risposta), Rispondi a
    /// tutti senza sé stessi, e le intestazioni Authentication-Results: solo quella del proprio server conta.
    /// </summary>
    static async Task ReplyToAsync()
    {
        Report.Add("--- Rispondi a Reply-To e mittente verificato");
        // IMAP finto: il modulo di contatto del sito, con Reply-To il cliente.
        ImapProvider.TestPlain = true;
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            var form = new MimeMessage { Subject = "Richiesta dal sito", Body = new TextPart("html") { Text = "<p>Vorrei un preventivo</p>" } };
            form.From.Add(new MailboxAddress("Sito dello studio", "wordpress@studio.example"));
            form.ReplyTo.Add(new MailboxAddress("Cliente", "cliente@example.com"));
            form.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.it"));
            form.Cc.Add(new MailboxAddress("Collega", "collega@studio-prova.it"));
            fake.Add("INBOX", form);
            var plain = new MimeMessage { Subject = "Senza Reply-To", Body = new TextPart("plain") { Text = "Ciao" } };
            plain.From.Add(new MailboxAddress("Anna", "anna@cliente.example"));
            fake.Add("INBOX", plain);
            using var imap = new ImapProvider(new MailAccount
            {
                Kind = AccountKind.Imap, Email = "mario@studio-prova.it",
                Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = fake.Port, UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect("x") },
            });
            var listed = (await imap.GetMessagesAsync(new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, null, null)).Messages;
            var withReplyTo = listed.Single(m => m.Subject == "Richiesta dal sito");
            var without = listed.Single(m => m.Subject == "Senza Reply-To");
            var body = await imap.GetBodyAsync(withReplyTo);
            var reply = Replies.Recipients(withReplyTo, body, "mario@studio-prova.it", ComposeMode.Reply);
            var all = Replies.Recipients(withReplyTo, body, "mario@studio-prova.it", ComposeMode.ReplyAll);
            Check(withReplyTo.ReplyTo.Select(a => a.Email).SequenceEqual(["cliente@example.com"]) && without.ReplyTo.Count == 0,
                  "IMAP: Reply-To dalla busta, e nessuno se la busta ripete il mittente");
            Check(reply.To.Select(a => a.Email).SequenceEqual(["cliente@example.com"]) && reply.Cc.Count == 0,
                  "Rispondi: A il Reply-To (il cliente), non il sito");
            Check(all.To.Select(a => a.Email).SequenceEqual(["cliente@example.com"]) && all.Cc.Select(a => a.Email).SequenceEqual(["collega@studio-prova.it"]),
                  "Rispondi a tutti: A il Reply-To, Cc gli altri senza me");
            Check(Replies.Recipients(without, null, "mario@studio-prova.it", ComposeMode.Reply).To.Single().Email == "anna@cliente.example",
                  "senza Reply-To la risposta va al mittente, come prima");
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }

        // Graph finto: replyTo nell'elenco, e il PATCH della risposta (createReply) con toRecipients uguale a replyTo.
        var fakeGraph = new FakeGraph();
        GraphProvider.UseTestHandler(fakeGraph);
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@example.com" });
        var graphList = (await graph.GetMessagesAsync(new MailFolder { Id = "inbox", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, null, null)).Messages;
        var fromSite = graphList.Single();
        fakeGraph.Requests.Clear();
        await graph.SaveDraftAsync(new OutgoingMessage
        {
            Mode = ComposeMode.Reply, Original = fromSite, To = Replies.Recipients(fromSite, null, "prova@example.com", ComposeMode.Reply).To,
            Subject = "R: Richiesta dal sito", Html = "<p>Ecco il preventivo</p>",
        });
        var patch = fakeGraph.Requests.FirstOrDefault(r => r.StartsWith("PATCH me/messages/bozza-2 ")) ?? "";
        Report.Add("      " + patch);
        Check(fromSite.ReplyTo.Single().Email == "cliente@example.com" && patch.Contains("\"toRecipients\":[{\"emailAddress\":{\"address\":\"cliente@example.com\",\"name\":\"Cliente\"}}]")
              && !patch.Contains("wordpress@studio.example"),
              "Graph: replyTo nell'elenco e nel PATCH della risposta (toRecipients), il sito no");

        // Authentication-Results: solo quella del server della propria casella.
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "ada@gmail.com" };
        var outlook = new MailAccount { Kind = AccountKind.Microsoft, Email = "ada@outlook.it" };
        const string googlePass = "mx.google.com; dkim=pass header.i=@paypal.it header.s=pp-dkim1 header.b=abc; spf=pass (google.com: domain of service@paypal.it designates 1.2.3.4 as permitted sender) smtp.mailfrom=service@paypal.it; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from=paypal.it";
        const string googleFail = "mx.google.com; dkim=none; spf=softfail (google.com: domain of transitioning x@banca.example does not designate 5.6.7.8 as permitted sender) smtp.mailfrom=x@banca.example; dmarc=fail (p=QUARANTINE sp=NONE dis=QUARANTINE) header.from=banca.example";
        const string forged = "mail.truffatore.example; dkim=pass header.d=banca.example; spf=pass; dmarc=pass header.from=banca.example";
        const string microsoft = "spf=pass (sender IP is 40.107.1.1) smtp.mailfrom=studio.example; dkim=pass (signature was verified) header.d=studio.example;dmarc=pass action=none header.from=studio.example;compauth=pass reason=100";
        Check(SenderCheck.Own(gmail, [googlePass]) is { Dmarc: "pass", Spf: "pass" } r1 && r1.Dkim.SequenceEqual(["pass"]) && !SenderCheck.Failed(r1),
              "Gmail: DMARC, SPF e DKIM superati letti da mx.google.com (commenti fra parentesi ignorati)");
        Check(SenderCheck.Own(gmail, [googleFail]) is { } r2 && SenderCheck.Failed(r2), "Gmail: dmarc=fail, mittente non verificato");
        Check(SenderCheck.Own(gmail, [forged]) == null && SenderCheck.Own(gmail, [googleFail, forged]) is { } r3 && SenderCheck.Failed(r3),
              "Gmail: la Authentication-Results aggiunta dal mittente (un altro authserv-id) non conta, né per salvare né per accusare");
        Check(SenderCheck.Own(outlook, [microsoft]) is { AuthservId: null, Dmarc: "pass" } r4 && !SenderCheck.Failed(r4) && SenderCheck.Own(outlook, [forged]) == null,
              "Microsoft: quella di Exchange Online in cima (senza authserv-id), non quella di un altro server");
        Check(SenderCheck.Failed(SenderCheck.Parse("mx.google.com; spf=fail smtp.mailfrom=a@b.example; dkim=fail header.d=b.example"))
              && !SenderCheck.Failed(SenderCheck.Parse("mx.google.com; spf=softfail smtp.mailfrom=a@b.example; dkim=pass header.d=b.example"))
              && !SenderCheck.Failed(SenderCheck.Parse("mx.google.com; spf=none; dkim=none")),
              "SPF e DKIM entrambi falliti: non verificato; uno dei due superato, o nessun controllo: niente fascia");
        var summary = new MessageSummary { Id = "1", FolderId = "INBOX", From = new Address { Name = "Banca", Email = "avvisi@banca.example" } };
        var notes = SenderCheck.Of(gmail, summary, new MessageBody { AuthenticationResults = [googleFail, forged] });
        Check(notes.Unverified == "banca.example" && notes.Why.Contains("DMARC fail") && !notes.External,
              $"fascia rossa per banca.example ({notes.Why[..60]}…); Gmail personale: niente \"Esterno\"");
        // Il nome di un contatto a cui hai scritto, da un indirizzo nuovo di un altro dominio (nella rubrica della cartella di prova).
        AddressBook.Sent([new Address { Name = "Giulia Verdi", Email = "giulia@commercialista.example" }], DateTime.Now.AddDays(-3));
        var fraud = AddressBook.LookAlike(new Address { Name = "Giulia Verdi", Email = "giulia.verdi.studio@gmail.com" });
        var colleague = AddressBook.LookAlike(new Address { Name = "Giulia Verdi", Email = "segreteria@commercialista.example" });
        var herself = AddressBook.LookAlike(new Address { Name = "Giulia Verdi", Email = "giulia@commercialista.example" });
        Check(fraud == "giulia@commercialista.example" && colleague == null && herself == null,
              "nome di un contatto da un altro dominio: avviso; stesso dominio o stesso indirizzo: niente");
        var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio.example", Tenant = MicrosoftAuth.Organizations };
        Check(SenderCheck.Of(work, summary, new MessageBody()).External && !SenderCheck.Of(work, new MessageSummary { Id = "2", FolderId = "x", From = new Address { Email = "anna@mail.studio.example" } }, new MessageBody()).External,
              "account di lavoro: \"Esterno\" per un altro dominio, non per un sottodominio del proprio");
    }

    // ---------------------------------------------------------------- inviti

    const string RomeZone = """
        BEGIN:VTIMEZONE
        TZID:{0}
        BEGIN:STANDARD
        DTSTART:16010101T030000
        TZOFFSETFROM:+0200
        TZOFFSETTO:+0100
        RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=10
        END:STANDARD
        BEGIN:DAYLIGHT
        DTSTART:16010101T020000
        TZOFFSETFROM:+0100
        TZOFFSETTO:+0200
        RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=3
        END:DAYLIGHT
        END:VTIMEZONE
        """;

    /// <summary>Tre inviti come li mandano Outlook (TZID Europe/Rome), Google Calendar (settimanale) e Teams (fuso di Windows).</summary>
    public static readonly (string File, string Ics)[] Invites =
    [
        ("invito-outlook.eml", "BEGIN:VCALENDAR\nMETHOD:REQUEST\nPRODID:Microsoft Exchange Server 2010\nVERSION:2.0\n" + string.Format(RomeZone, "Europe/Rome") + """

            BEGIN:VEVENT
            ORGANIZER;CN=Mario Rossi:mailto:mario.rossi@studio-prova.it
            ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE;CN=Ada:mailto:ada.prova@gmail.com
            ATTENDEE;ROLE=OPT-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE;CN=Anna Bianchi:mailto:anna@studio-prova.it
            DESCRIPTION;LANGUAGE=it-IT:Ordine del giorno:\n1. Bilancio\n2. Varie
            UID:040000008200E00074C5B7101A82E00800000000A0B1C2D3E4F5
            SUMMARY;LANGUAGE=it-IT:Riunione di studio
            DTSTART;TZID=Europe/Rome:20261001T090000
            DTEND;TZID=Europe/Rome:20261001T103000
            DTSTAMP:20260926T120000Z
            SEQUENCE:2
            LOCATION;LANGUAGE=it-IT:Sala riunioni\, piano 2
            BEGIN:VALARM
            DESCRIPTION:REMINDER
            TRIGGER;RELATED=START:-PT15M
            ACTION:DISPLAY
            END:VALARM
            END:VEVENT
            END:VCALENDAR
            """),
        ("invito-google.eml", """
            BEGIN:VCALENDAR
            PRODID:-//Google Inc//Google Calendar 70.9054//EN
            VERSION:2.0
            CALSCALE:GREGORIAN
            METHOD:REQUEST
            BEGIN:VEVENT
            DTSTART:20261005T070000Z
            DTEND:20261005T073000Z
            RRULE:FREQ=WEEKLY;BYDAY=MO,WE;UNTIL=20261216T225959Z
            DTSTAMP:20260926T100000Z
            ORGANIZER;CN=luca.verdi@gmail.com:mailto:luca.verdi@gmail.com
            UID:3k8d0f1a2b3c4d5e6f7g8h9i0j@google.com
            ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE;CN=ada.prova@gmail.com;X-NUM-GUESTS=0:mailto:ada.prova@gmail.com
            X-GOOGLE-CONFERENCE:https://meet.google.com/abc-defg-hij
            DESCRIPTION:Punto settimanale sul progetto.
            LOCATION:
            SEQUENCE:0
            STATUS:CONFIRMED
            SUMMARY:Punto settimanale
            END:VEVENT
            END:VCALENDAR
            """),
        ("invito-teams.eml", "BEGIN:VCALENDAR\nMETHOD:REQUEST\nPRODID:Microsoft Exchange Server 2010\nVERSION:2.0\n" + string.Format(RomeZone, "W. Europe Standard Time") + """

            BEGIN:VEVENT
            ORGANIZER;CN="Cliente S.p.A.":mailto:acquisti@cliente-prova.it
            ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE;CN=Ada:mailto:ada.prova@gmail.com
            DESCRIPTION;LANGUAGE=it-IT:____\nRiunione di Microsoft Teams\nFai clic qui per partecipare alla riun
             ione<https://teams.microsoft.com/l/meetup-join/19%3ameeting_ZmFrZQ%40thread.v2/0?context=%7b%7d>\n____
            UID:040000008200E00074C5B7101A82E00800000000FFEEDDCC
            SUMMARY;LANGUAGE=it-IT:Offerta 2027
            DTSTART;TZID=W. Europe Standard Time:20261112T143000
            DTEND;TZID=W. Europe Standard Time:20261112T151500
            SEQUENCE:0
            LOCATION;LANGUAGE=it-IT:Riunione di Microsoft Teams
            X-MICROSOFT-SKYPETEAMSMEETINGURL:https://teams.microsoft.com/l/meetup-join/19%3ameeting_ZmFrZQ%40thread.v2/0?context=%7b%7d
            END:VEVENT
            END:VCALENDAR
            """),
    ];

    /// <summary>Un invito come messaggio: testo, HTML e la parte text/calendar con METHOD:REQUEST, come Outlook e Google.</summary>
    public static MimeMessage InviteMessage(string ics)
    {
        var invite = Invitation.Parse(ics)!;
        var message = new MimeMessage { Subject = "Invito: " + invite.Summary, Date = DateTimeOffset.Now.AddHours(-1) };
        message.From.Add(new MailboxAddress(invite.Organizer?.Name ?? "", invite.Organizer?.Email ?? "organizzatore@example.com"));
        message.To.Add(new MailboxAddress("Ada", "ada.prova@gmail.com"));
        var calendar = new TextPart("calendar") { Text = ics.Replace("\n", "\r\n") };
        calendar.ContentType.Parameters.Add("method", invite.Method);
        calendar.ContentType.Charset = "utf-8";
        message.Body = new MultipartAlternative
        {
            new TextPart("plain") { Text = $"Sei invitato a {invite.Summary}." },
            new TextPart("html") { Text = $"<p>Sei invitato a <b>{System.Net.WebUtility.HtmlEncode(invite.Summary)}</b>.</p>" },
            calendar,
        };
        return message;
    }

    /// <summary>Missiva.exe --scrivi-inviti cartella: i tre inviti di prova come file .eml, da aprire con --eml.</summary>
    public static void WriteInvites(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (file, ics) in Invites) InviteMessage(ics).WriteTo(Path.Combine(folder, file));
    }

    // ---------------------------------------------------------------- posta certificata e file firmati

    /// <summary>
    /// I certificati delle prove della posta certificata, fatti al volo: una radice di prova; emessi da lei il certificato
    /// del gestore (posta-certificata@pec-prova.it nel subjectAltName, come vuole l'appendice B delle regole tecniche),
    /// uno per i siti web (solo serverAuth, come quelli gratuiti di Let's Encrypt), uno di posta di un dominio che non è un
    /// gestore (posta-certificata@altro-prova.it) e quello di una persona (mario.rossi@pec-prova.it); e uno di un estraneo,
    /// senza indirizzo, che nessuno riconosce. Mai installati nel sistema: la radice vale solo con Pec.TestRoots.
    /// </summary>
    sealed class PecKeys
    {
        public readonly X509Certificate2 Root, Provider, Stranger, Web, Other, Person;

        public PecKeys()
        {
            var now = DateTimeOffset.Now;
            using var rootKey = RSA.Create(2048);
            var rootRequest = new CertificateRequest("CN=Radice PEC di prova, O=Missiva, C=IT", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            Root = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
            Provider = Leaf("CN=Gestore PEC di prova S.p.A., O=Gestore di prova, C=IT", Root, now, "posta-certificata@pec-prova.it");
            Stranger = Leaf("CN=Estraneo di prova, C=IT", null, now, null);
            Web = Leaf("CN=pec-prova.it", Root, now, null, "1.3.6.1.5.5.7.3.1");
            Other = Leaf("CN=Posta Certificata, O=Altro di prova, C=IT", Root, now, "posta-certificata@altro-prova.it");
            Person = Leaf("CN=Mario Rossi, C=IT", Root, now, "mario.rossi@pec-prova.it");
        }

        /// <param name="usage">Lo scopo (extendedKeyUsage): di base la protezione della posta.</param>
        internal static X509Certificate2 Leaf(string subject, X509Certificate2? issuer, DateTimeOffset now, string? email, string usage = "1.3.6.1.5.5.7.3.4")
        {
            var key = RSA.Create(2048);
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
            var made = issuer == null ? request.CreateSelfSigned(now.AddDays(-1), now.AddMonths(6))
                                      : request.Create(issuer, now.AddDays(-1), now.AddMonths(6), RandomNumberGenerator.GetBytes(8)).CopyWithPrivateKey(key);
            // Da PFX, come per il server finto: così la chiave sta in un contenitore che le firme sanno usare.
            return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
        }
    }

    /// <summary>Un PDF minimo, l'allegato del messaggio certificato di prova.</summary>
    static readonly byte[] MinimalPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
                                                                 "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 100]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    static string Daticert(string kind, string error = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <postacert tipo="{kind}" errore="{(error.Length > 0 ? "no-dest" : "nessuno")}">
          <intestazione>
            <mittente>mario.rossi@pec-prova.it</mittente>
            <destinatari tipo="certificato">studio@pec-studio-prova.it</destinatari>
            <risposte>mario.rossi@pec-prova.it</risposte>
            <oggetto>Contratto firmato</oggetto>
          </intestazione>
          <dati>
            <gestore-emittente>Gestore PEC di prova S.p.A.</gestore-emittente>
            <data zona="+0200"><giorno>26/09/2026</giorno><ora>10:15:03</ora></data>
            <identificativo>opec-prova.20260926101503.0042@pec-prova.it</identificativo>
            <msgid>&lt;originale-42@pec-prova.it&gt;</msgid>
            {(kind is "avvenuta-consegna" or "errore-consegna" ? "<consegna>studio@pec-studio-prova.it</consegna>" : "")}
            {(error.Length > 0 ? $"<errore-esteso>{error}</errore-esteso>" : "")}
          </dati>
        </postacert>
        """;

    /// <summary>
    /// Una busta PEC come le fa un gestore: il testo del gestore, postacert.eml (il messaggio vero, con un PDF) per la
    /// posta certificata e per l'anomalia (X-Trasporto: errore, senza daticert.xml), daticert.xml, tutto firmato S/MIME
    /// (multipart/signed) dal certificato dato (null: senza firma). alter: dopo la firma l'importo nel messaggio cambia, e
    /// la firma non torna più. gestore: l'indirizzo del From della busta.
    /// </summary>
    static byte[] PecEnvelope(X509Certificate2? signer, string kind, bool alter = false, string error = "", string gestore = "posta-certificata@pec-prova.it")
    {
        var original = new MimeMessage { Subject = "Contratto firmato", MessageId = "originale-42@pec-prova.it", Date = new DateTimeOffset(2026, 9, 26, 10, 15, 0, TimeSpan.FromHours(2)) };
        original.From.Add(new MailboxAddress("Mario Rossi", "mario.rossi@pec-prova.it"));
        original.To.Add(new MailboxAddress("Studio Bianchi", "studio@pec-studio-prova.it"));
        var content = new BodyBuilder { HtmlBody = "<p>Buongiorno,</p>\r\n<p>in allegato il contratto firmato,\r\nper un importo di 1.000 euro.</p>\r\n<p>Mario Rossi</p>" };
        content.Attachments.Add("contratto.pdf", MinimalPdf, new ContentType("application", "pdf"));
        original.Body = content.ToMessageBody();
        bool carries = kind is "posta-certificata" or "anomalia";
        var inside = new Multipart("mixed")
        {
            new TextPart("plain") { Text = kind switch
            {
                "posta-certificata" => "Messaggio di posta certificata\r\nIl giorno 26/09/2026 alle ore 10:15:03 (+0200) il messaggio \"Contratto firmato\" e' stato inviato da \"mario.rossi@pec-prova.it\" indirizzato a: studio@pec-studio-prova.it\r\nIl messaggio originale e' incluso in allegato.",
                "anomalia" => "Anomalia nel messaggio\r\nIl giorno 26/09/2026 alle ore 10:15:03 (+0200) e' stato ricevuto il messaggio \"Contratto firmato\" proveniente da \"mario.rossi@pec-prova.it\" ed indirizzato a:\r\nstudio@pec-studio-prova.it\r\nTali dati non sono stati certificati per il seguente errore:\r\nmessaggio di posta ordinaria\r\nIl messaggio originale e' incluso in allegato.",
                _ => $"Ricevuta di {kind}\r\nIl giorno 26/09/2026 alle ore 10:15:03 (+0200) il messaggio \"Contratto firmato\" proveniente da \"mario.rossi@pec-prova.it\" ...\r\n",
            } },
        };
        if (carries)
            inside.Add(new MessagePart { Message = original, ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "postacert.eml" } });
        if (kind != "anomalia") // nella busta di anomalia nessun dato di certificazione (regole tecniche, 6.4.2)
            inside.Add(new MimePart("application", "xml")
            {
                Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(Daticert(kind, error)))),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "daticert.xml" },
                ContentTransferEncoding = ContentEncoding.Base64,
            });
        MimeEntity body = inside;
        if (signer != null)
        {
            using var context = new MimeKit.Cryptography.TemporarySecureMimeContext();
            body = MimeKit.Cryptography.MultipartSigned.Create(context, new MimeKit.Cryptography.CmsSigner(signer), inside);
        }
        var prefix = kind switch
        {
            "posta-certificata" => "POSTA CERTIFICATA: ", "anomalia" => "ANOMALIA MESSAGGIO: ", "accettazione" => "ACCETTAZIONE: ", "avvenuta-consegna" => "CONSEGNA: ",
            _ => "AVVISO DI MANCATA CONSEGNA: ",
        };
        var envelope = new MimeMessage { Subject = prefix + "Contratto firmato", Body = body, MessageId = $"opec-{kind}@pec-prova.it", Date = original.Date };
        envelope.From.Add(new MailboxAddress(carries ? "Per conto di: mario.rossi@pec-prova.it" : gestore, gestore));
        if (carries) envelope.ReplyTo.Add(new MailboxAddress("Mario Rossi", "mario.rossi@pec-prova.it"));
        envelope.To.Add(new MailboxAddress("", carries ? "studio@pec-studio-prova.it" : "mario.rossi@pec-prova.it"));
        if (carries) envelope.Headers.Add("X-Trasporto", kind == "anomalia" ? "errore" : "posta-certificata");
        else
        {
            envelope.Headers.Add("X-Ricevuta", kind);
            envelope.Headers.Add("X-Riferimento-Message-ID", "<originale-42@pec-prova.it>");
        }
        using var ms = new MemoryStream();
        envelope.WriteTo(ms);
        var raw = ms.ToArray();
        if (!alter) return raw;
        var text = Encoding.Latin1.GetString(raw);
        if (!text.Contains("1.000 euro")) throw new InvalidOperationException("prova: l'importo da alterare non è nel testo");
        return Encoding.Latin1.GetBytes(text.Replace("1.000 euro", "9.000 euro"));
    }

    const string Fattura = """<?xml version="1.0" encoding="UTF-8"?><p:FatturaElettronica versione="FPR12" xmlns:p="http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2"><FatturaElettronicaHeader><CedentePrestatore><DatiAnagrafici><Anagrafica><Denominazione>Fornitore di prova S.r.l.</Denominazione></Anagrafica></DatiAnagrafici></CedentePrestatore></FatturaElettronicaHeader><FatturaElettronicaBody><DatiGenerali><DatiGeneraliDocumento><TipoDocumento>TD01</TipoDocumento><Data>2026-09-26</Data><Numero>42</Numero><ImportoTotaleDocumento>1000.00</ImportoTotaleDocumento></DatiGeneraliDocumento></DatiGenerali></FatturaElettronicaBody></p:FatturaElettronica>""";

    /// <summary>Un file .p7m (CAdES, busta con il contenuto dentro) firmato dal certificato dato; tamper: un byte del contenuto cambiato dopo.</summary>
    static byte[] P7mFile(byte[] content, X509Certificate2 signer, bool tamper = false)
    {
        var cms = new System.Security.Cryptography.Pkcs.SignedCms(new System.Security.Cryptography.Pkcs.ContentInfo(content), detached: false);
        var who = new System.Security.Cryptography.Pkcs.CmsSigner(signer) { IncludeOption = X509IncludeOption.EndCertOnly };
        who.SignedAttributes.Add(new System.Security.Cryptography.Pkcs.Pkcs9SigningTime(new DateTime(2026, 9, 26, 8, 30, 0, DateTimeKind.Utc)));
        cms.ComputeSignature(who);
        var encoded = cms.Encode();
        if (!tamper) return encoded;
        var at = encoded.AsSpan().IndexOf(Encoding.ASCII.GetBytes("1000.00"));
        encoded[at] = (byte)'9';
        return encoded;
    }

    /// <summary>Un messaggio normale con un allegato (per aprire i .p7m di prova con --eml).</summary>
    static MimeMessage WithAttachment(string subject, string file, byte[] content)
    {
        var message = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now };
        message.From.Add(new MailboxAddress("Sistema di Interscambio (prova)", "sdi-prova@pec-prova.it"));
        message.To.Add(new MailboxAddress("", "studio@pec-studio-prova.it"));
        var builder = new BodyBuilder { HtmlBody = $"<p>In allegato {file}.</p>" };
        builder.Attachments.Add(file, content, new ContentType("application", "pkcs7-mime"));
        message.Body = builder.ToMessageBody();
        return message;
    }

    /// <summary>
    /// Missiva.exe --scrivi-pec cartella: buste PEC e file firmati di prova, da aprire con --eml (con --pec-radice
    /// radice-di-prova.cer la radice di prova vale come attendibile, per la fascia verde).
    /// </summary>
    public static void WritePec(string folder)
    {
        Directory.CreateDirectory(folder);
        var keys = new PecKeys();
        File.WriteAllBytes(Path.Combine(folder, "radice-di-prova.cer"), keys.Root.Export(X509ContentType.Cert));
        File.WriteAllBytes(Path.Combine(folder, "pec.eml"), PecEnvelope(keys.Provider, "posta-certificata"));
        File.WriteAllBytes(Path.Combine(folder, "pec-estraneo.eml"), PecEnvelope(keys.Stranger, "posta-certificata"));
        File.WriteAllBytes(Path.Combine(folder, "pec-certificato-web.eml"), PecEnvelope(keys.Web, "posta-certificata"));
        File.WriteAllBytes(Path.Combine(folder, "pec-senza-firma.eml"), PecEnvelope(null, "posta-certificata"));
        File.WriteAllBytes(Path.Combine(folder, "anomalia.eml"), PecEnvelope(keys.Provider, "anomalia"));
        File.WriteAllBytes(Path.Combine(folder, "pec-alterata.eml"), PecEnvelope(keys.Provider, "posta-certificata", alter: true));
        File.WriteAllBytes(Path.Combine(folder, "ricevuta-consegna.eml"), PecEnvelope(keys.Provider, "avvenuta-consegna"));
        File.WriteAllBytes(Path.Combine(folder, "ricevuta-errore.eml"), PecEnvelope(keys.Provider, "errore-consegna", error: "5.1.1 - casella di destinazione inesistente"));
        WithAttachment("Fattura 42", "IT01234567890_00042.xml.p7m", P7mFile(Encoding.UTF8.GetBytes(Fattura), keys.Provider)).WriteTo(Path.Combine(folder, "fattura.eml"));
        WithAttachment("Fattura 42 (manomessa)", "IT01234567890_00042.xml.p7m", P7mFile(Encoding.UTF8.GetBytes(Fattura), keys.Provider, tamper: true)).WriteTo(Path.Combine(folder, "fattura-manomessa.eml"));
    }

    /// <summary>
    /// Posta certificata: la busta firmata dal gestore di prova (fascia verde con la radice di prova, gialla senza, rossa se
    /// alterata), il corpo e gli allegati di postacert.eml, Rispondi al mittente vero e le immagini decise da lui, le
    /// ricevute di consegna e di mancata consegna, la busta di anomalia, daticert.xml con una DTD non letto; le buste finte:
    /// firmate con un certificato per i siti web riconosciuto (come uno gratuito di Let's Encrypt), con quello di posta di
    /// un dominio che non è un gestore, senza firma; i messaggi S/MIME firmati dal mittente o da un altro; la busta presa
    /// dai byte già arrivati con il corpo e poi dalle buste aperte; i file .p7m integri, manomessi, firmati con un
    /// certificato per i siti web, in base64, firmati due volte e grandi.
    /// </summary>
    static async Task PecTestsAsync()
    {
        Report.Add("--- Posta certificata e file firmati");
        // Le parti tecniche della busta e della firma non sono pastiglie nell'elenco, come nel riquadro (16 ottobre 2026, scelta dell'autore).
        var envelopeNames = new List<string> { "postacert.eml", "daticert.xml", "smime.p7s" };
        var signedNames = new List<string> { "Preventivo.pdf", "smime.p7s" };
        var normalNames = new List<string> { "postacert.eml", "foto.jpg" };
        var pecAccount = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "ada@pec-prova.example", Imap = new ImapServer() }));
        var pecRow = new UI.MessageItem(new MessageSummary { Id = "pec", FolderId = "INBOX", HasAttachments = true, AttachmentNames = envelopeNames }, pecAccount);
        var signedRow = new UI.MessageItem(new MessageSummary { Id = "firmato", FolderId = "INBOX", HasAttachments = true, AttachmentNames = signedNames }, pecAccount);
        Check(!Pec.ShownAttachments(envelopeNames).Any() && Pec.ShownAttachments(signedNames).SequenceEqual(new[] { "Preventivo.pdf" })
              && Pec.ShownAttachments(normalNames).SequenceEqual(normalNames)
              && pecRow.AttachmentChips.Count == 0 && pecRow.AttachmentVisibility == System.Windows.Visibility.Visible
              && signedRow.AttachmentChips.Select(c => c.Name).SequenceEqual(new[] { "Preventivo.pdf" }),
              "elenco: la busta di una PEC e la firma di un messaggio firmato non danno pastiglie (daticert.xml, smime.p7s, il postacert.eml della busta); " +
              "un postacert.eml fuori da una busta e gli allegati veri sì; la graffetta resta");
        var keys = new PecKeys();
        MessageBody Opened(byte[] raw, out MessageSummary summary)
        {
            var message = MimeMessage.Load(new MemoryStream(raw));
            summary = new EmlProvider(message, null!).Summary;
            var body = EmlProvider.BodyOf(message);
            Check(Pec.Wants(body), $"busta riconosciuta dagli allegati ({string.Join(", ", body.Attachments.Select(a => a.Name))})");
            return Pec.Open(raw, body);
        }
        try
        {
            Pec.TestRoots = [keys.Root];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var greenRaw = PecEnvelope(keys.Provider, "posta-certificata");
            var green = Opened(greenRaw, out var summary);
            Report.Add($"      aperta e verificata in {clock.ElapsedMilliseconds} ms");
            var (text, tone) = Pec.Band(green.Pec!);
            Report.Add("      " + text);
            Check(green.Pec is { Kind: "posta-certificata", Signature: SignatureState.Verified, Sender: "mario.rossi@pec-prova.it", Provider: "Gestore PEC di prova S.p.A.",
                                 SignerEmail: "posta-certificata@pec-prova.it", Gestore: "Gestore PEC di prova" }
                  && green.Pec.When == new DateTimeOffset(2026, 9, 26, 10, 15, 3, TimeSpan.FromHours(2)).LocalDateTime && tone == "ok"
                  && text.Contains("firma del gestore verificata") && green.Pec.Signer.Contains("Gestore PEC di prova"),
                  "busta firmata dal gestore (posta-certificata@pec-prova.it nel certificato, come il From della busta) con una radice attendibile: fascia verde, mittente, gestore, data e ora di daticert.xml");
            Check(green.Html.Contains("importo di 1.000 euro") && green.Attachments.Select(a => a.Name).SequenceEqual(["contratto.pdf"])
                  && green.Attachments[0].Content is { Length: > 0 } pdf && pdf.AsSpan().StartsWith("%PDF"u8),
                  "il corpo è il messaggio vero (postacert.eml) e gli allegati i suoi: contratto.pdf, senza daticert.xml né smime.p7s");
            Check(Replies.Recipients(summary, green, "studio@pec-studio-prova.it", ComposeMode.Reply).To.Single().Email == "mario.rossi@pec-prova.it",
                  "Rispondi va al mittente vero (Reply-To della busta), non al gestore");
            Check(summary.From.Email == "posta-certificata@pec-prova.it" && UI.ReadingView.ImageSender(summary, green) == "mario.rossi@pec-prova.it",
                  "immagini esterne decise dal mittente vero (mario.rossi@pec-prova.it), non dal gestore della busta, uguale per tutti i suoi clienti");

            // Buste finte: un certificato riconosciuto ma per i siti web, uno di posta di un dominio che non è un gestore, senza firma.
            var web = Opened(PecEnvelope(keys.Web, "posta-certificata"), out _);
            var (webText, webTone) = Pec.Band(web.Pec!);
            Report.Add("      " + webText);
            Check(web.Pec?.Signature == SignatureState.Unsuitable && webTone == "danger" && webText.StartsWith("Sembra posta certificata"),
                  "busta firmata con un certificato per i siti web che arriva a una radice attendibile (come Let's Encrypt): rossa, non verde");
            var other = Opened(PecEnvelope(keys.Other, "posta-certificata", gestore: "posta-certificata@altro-prova.it"), out _);
            var (otherText, otherTone) = Pec.Band(other.Pec!);
            Report.Add("      " + otherText);
            Check(other.Pec is { Signature: SignatureState.Verified, Gestore: null } && otherTone == "warn" && otherText.Contains("non è fra i gestori PEC che Missiva conosce"),
                  "busta firmata con un certificato di posta riconosciuto per posta-certificata@altro-prova.it, che non è un gestore noto: gialla con la spiegazione, non verde");
            var mismatch = Opened(PecEnvelope(keys.Other, "posta-certificata"), out _);
            Check(mismatch.Pec?.Signature == SignatureState.Unsuitable && Pec.Band(mismatch.Pec).Tone == "danger",
                  "busta con il From del gestore firmata con il certificato di un altro indirizzo: rossa");
            var unsigned = Opened(PecEnvelope(null, "posta-certificata"), out _);
            var (unsignedText, unsignedTone) = Pec.Band(unsigned.Pec!);
            Check(unsigned.Pec?.Signature == SignatureState.Missing && unsignedTone == "danger" && unsignedText.Contains("non ha la firma del gestore"),
                  "busta senza firma: rossa, le buste vere sono tutte firmate dal gestore");

            Pec.TestRoots = null;
            var yellow = Opened(PecEnvelope(keys.Provider, "posta-certificata"), out _);
            var stranger = Opened(PecEnvelope(keys.Stranger, "posta-certificata"), out _);
            Check(yellow.Pec?.Signature == SignatureState.Untrusted && Pec.Band(yellow.Pec).Tone == "warn" && Pec.Band(yellow.Pec).Text.Contains("non è fra quelli riconosciuti da Windows"),
                  "certificato del gestore che Windows non riconosce (senza la radice di prova): fascia gialla");
            Check(stranger.Pec?.Signature == SignatureState.Unsuitable && Pec.Band(stranger.Pec).Tone == "danger",
                  "busta firmata da un estraneo senza l'indirizzo del gestore nel certificato: rossa (prima era gialla come le vere)");
            var red = Opened(PecEnvelope(keys.Provider, "posta-certificata", alter: true), out _);
            Check(red.Pec?.Signature == SignatureState.Invalid && Pec.Band(red.Pec).Tone == "danger" && Pec.Band(red.Pec).Text.Contains("alterata"),
                  "busta alterata dopo la firma (l'importo cambiato): fascia rossa");

            Pec.TestRoots = [keys.Root];
            var delivered = Opened(PecEnvelope(keys.Provider, "avvenuta-consegna"), out _);
            var failed = Opened(PecEnvelope(keys.Provider, "errore-consegna", error: "5.1.1 - casella di destinazione inesistente"), out _);
            Report.Add("      " + Pec.Band(delivered.Pec!).Text);
            Report.Add("      " + Pec.Band(failed.Pec!).Text);
            Check(delivered.Pec is { Kind: "avvenuta-consegna", DeliveredTo: "studio@pec-studio-prova.it", Reference: "<originale-42@pec-prova.it>", Failed: false }
                  && Pec.Band(delivered.Pec).Tone == "ok" && !delivered.Attachments.Any(a => a.Name is "daticert.xml" or "smime.p7s")
                  && failed.Pec is { Failed: true, Error: "5.1.1 - casella di destinazione inesistente" } && Pec.Band(failed.Pec).Tone == "danger",
                  "ricevuta di consegna (a chi, con il Message-ID del messaggio mandato) e mancata consegna in rosso con l'errore");
            var anomaly = Opened(PecEnvelope(keys.Provider, "anomalia"), out _);
            var (anomalyText, anomalyTone) = Pec.Band(anomaly.Pec!);
            Report.Add("      " + anomalyText);
            Check(anomaly.Pec is { Kind: "anomalia", Failed: false, Sender: "mario.rossi@pec-prova.it", Subject: "Contratto firmato" } && anomalyTone == "warn"
                  && anomalyText.StartsWith("Anomalia") && anomalyText.Contains("non è certificato") && !anomalyText.Contains("Mancata consegna")
                  && anomaly.Attachments.Any(a => a.Name.EndsWith(".eml")) && anomaly.Html.Contains("Anomalia nel messaggio"),
                  "busta di anomalia (X-Trasporto: errore): messaggio arrivato ma non certificato, gialla, con l'originale allegato; non più \"Mancata consegna\"");

            // Messaggi firmati S/MIME qualsiasi: con il certificato del mittente, o con quello di un altro.
            static byte[] SignedMail(X509Certificate2 signer)
            {
                var mail = new MimeMessage { Subject = "Preventivo", Date = DateTimeOffset.Now };
                mail.From.Add(new MailboxAddress("Mario Rossi", "mario.rossi@pec-prova.it"));
                mail.To.Add(new MailboxAddress("", "studio@pec-studio-prova.it"));
                using var context = new MimeKit.Cryptography.TemporarySecureMimeContext();
                mail.Body = MimeKit.Cryptography.MultipartSigned.Create(context, new MimeKit.Cryptography.CmsSigner(signer), new TextPart("plain") { Text = "In allegato il preventivo." });
                using var ms = new MemoryStream();
                mail.WriteTo(ms);
                return ms.ToArray();
            }
            var own = Opened(SignedMail(keys.Person), out _);
            var foreign = Opened(SignedMail(keys.Other), out _);
            Report.Add("      " + Pec.Band(foreign.Pec!).Text);
            Check(own.Pec is { IsPec: false, Signature: SignatureState.Verified } && Pec.Band(own.Pec).Tone == "ok" && own.ImagesFrom == null
                  && foreign.Pec?.Signature == SignatureState.Unsuitable && Pec.Band(foreign.Pec).Tone == "danger",
                  "messaggio firmato S/MIME: verde con il certificato del mittente, rosso con quello di un altro indirizzo (anche se riconosciuto)");

            // I byte già arrivati con il corpo (Raw): nessuna richiesta al server; poi la busta aperta si riprende com'era.
            var stub = DispatchProxy.Create<IMailProvider, SenderStub>();
            ((SenderStub)(object)stub).Account = new MailAccount { Kind = AccountKind.Imap, Email = "pec@pec-studio-prova.it", Id = "pec-stub" };
            var envelopeMessage = MimeMessage.Load(new MemoryStream(greenRaw));
            var arrived = EmlProvider.BodyOf(envelopeMessage);
            var withRaw = new MessageBody { Html = arrived.Html, Attachments = arrived.Attachments, Raw = greenRaw };
            var stubSummary = new MessageSummary { Id = "42", FolderId = "INBOX", From = summary.From };
            var fromRaw = await Pec.OpenAsync(stub, stubSummary, withRaw, CancellationToken.None); // GetRawAsync del finto lancerebbe NotSupportedException
            var recalled = Pec.Recall(stub, stubSummary);
            Check(fromRaw.Pec?.Signature == SignatureState.Verified && recalled == fromRaw && Pec.Recall(stub, new MessageSummary { Id = "43", FolderId = "INBOX" }) == null,
                  "busta aperta dai byte già arrivati con il corpo (nessuna richiesta del messaggio intero) e ripresa dalle buste aperte per account, cartella e messaggio");

            // daticert.xml con una DTD (entità che si moltiplicano): non si legge, e niente esplode.
            var evil = new MimeMessage();
            evil.Headers.Add("X-Trasporto", "posta-certificata");
            evil.Body = new Multipart("mixed")
            {
                new TextPart("plain") { Text = "busta" },
                new MimePart("application", "xml")
                {
                    Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!DOCTYPE p [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">]><postacert tipo=\"accettazione\"><intestazione><oggetto>&b;</oggetto></intestazione></postacert>"))),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "daticert.xml" },
                },
            };
            using var evilRaw = new MemoryStream();
            evil.WriteTo(evilRaw);
            var dtd = Pec.Open(evilRaw.ToArray(), EmlProvider.BodyOf(evil));
            Check(dtd.Pec is { Kind: "posta-certificata", Subject: "", Signature: SignatureState.Missing } && !dtd.Pec.Subject.Contains("aaaa"),
                  "daticert.xml con una DTD: rifiutato (DtdProcessing.Prohibit), la busta resta senza firma e senza dati");

            // File firmati .p7m: una fattura elettronica integra, manomessa, firmata con un certificato per i siti web, in base64, firmata due volte.
            var fattura = Encoding.UTF8.GetBytes(Fattura);
            var ok = P7m.Open(P7mFile(fattura, keys.Provider), "IT01234567890_00042.xml.p7m");
            var tampered = P7m.Open(P7mFile(fattura, keys.Provider, tamper: true), "IT01234567890_00042.xml.p7m");
            var webSigned = P7m.Open(P7mFile(fattura, keys.Web), "IT01234567890_00042.xml.p7m");
            var base64 = P7m.Open(Encoding.ASCII.GetBytes(Convert.ToBase64String(P7mFile(fattura, keys.Provider), Base64FormattingOptions.InsertLineBreaks)), "fattura.xml.p7m");
            var pem = P7m.Open(Encoding.ASCII.GetBytes("-----BEGIN PKCS7-----\r\n" + Convert.ToBase64String(P7mFile(fattura, keys.Provider), Base64FormattingOptions.InsertLineBreaks) + "\r\n-----END PKCS7-----\r\n"), "fattura.xml.p7m");
            var twice = P7m.Open(P7mFile(P7mFile(fattura, keys.Provider), keys.Provider), "fattura.xml.p7m.p7m");
            Report.Add("      " + P7m.Band(ok).Text);
            Report.Add("      " + P7m.Band(tampered).Text);
            Report.Add("      " + P7m.Band(webSigned).Text);
            Check(ok is { Name: "IT01234567890_00042.xml", Signature: SignatureState.Verified, Layers: 1 } && ok.Signer.Contains("Gestore PEC di prova")
                  && ok.SignedAt == new DateTime(2026, 9, 26, 8, 30, 0, DateTimeKind.Utc).ToLocalTime() && P7m.IsText(ok)
                  && P7m.Text(ok).Contains("<ImportoTotaleDocumento>1000.00</ImportoTotaleDocumento>") && P7m.Text(ok).Contains("\n"),
                  "fattura .p7m: nome senza .p7m, firmatario, data della firma, firma valida, XML rientrato come testo");
            Check(tampered.Signature == SignatureState.Invalid && P7m.Band(tampered).Tone == "danger" && P7m.Band(tampered).Text.Contains("Firma non valida")
                  && webSigned.Signature == SignatureState.Unsuitable && P7m.Band(webSigned).Tone == "danger"
                  && base64.Signature == SignatureState.Verified && Encoding.UTF8.GetString(base64.Content) == Fattura
                  && pem.Signature == SignatureState.Verified && Encoding.UTF8.GetString(pem.Content) == Fattura
                  && twice is { Layers: 2, Name: "fattura.xml", Signature: SignatureState.Verified } && Encoding.UTF8.GetString(twice.Content) == Fattura,
                  "p7m manomesso: firma non valida (rossa); firmato con un certificato per i siti web: non valido (rosso, prima \"firma valida\"); in base64, PEM e firmato due volte: il contenuto estratto, firme valide");
            var large = new byte[20 * 1024 * 1024];
            new Random(42).NextBytes(large);
            var largeFile = P7mFile(large, keys.Provider);
            var timing = System.Diagnostics.Stopwatch.StartNew();
            var opened = P7m.Open(largeFile, "planimetria.pdf.p7m");
            timing.Stop();
            Check(opened.Signature == SignatureState.Verified && opened.Content.Length == large.Length && timing.ElapsedMilliseconds < 1500,
                  $"p7m da 20 MB: aperto e verificato in {timing.ElapsedMilliseconds} ms, senza provare il base64 su una busta DER");
            Pec.TestRoots = null;
            Check(P7m.Open(P7mFile(fattura, keys.Provider), "f.xml.p7m").Signature == SignatureState.Untrusted, "senza la radice di prova: integro, certificato non riconosciuto (giallo)");
            var dtdXml = P7m.Open(P7mFile(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"CIAO\">]><x>&a;&a;</x>"), keys.Provider), "x.xml.p7m");
            Check(P7m.Text(dtdXml).Contains("<!DOCTYPE") && !P7m.Text(dtdXml).Contains("CIAOCIAO"), "XML con una DTD nel p7m: mostrato com'è, senza espandere le entità");
            string? notSigned = null;
            try { P7m.Open(fattura, "falso.xml.p7m"); } catch (MailException ex) { notSigned = ex.Message; }
            Check(notSigned?.Contains("Non è un file firmato valido") == true, "un .p7m che non è una busta di firma: spiegato, niente aperto");
        }
        finally
        {
            Pec.TestRoots = null;
        }
    }

    /// <summary>
    /// Inviti: lettura (fusi, ricorrenza, link della riunione), risposta iMIP (REPLY e COUNTER) via SMTP finto con un
    /// account IMAP, e Graph finto per gli inviti di Exchange (senza consenso: niente; con: accept sull'evento) e per l'iMIP.
    /// </summary>
    static async Task InvitesAsync()
    {
        Report.Add("--- Inviti alle riunioni");
        static DateTime From(string zone, DateTime at) => TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(zone), TimeZoneInfo.Local);
        var outlook = Invitation.Parse(Invites[0].Ics)!;
        var google = Invitation.Parse(Invites[1].Ics)!;
        var teams = Invitation.Parse(Invites[2].Ics)!;
        Check(outlook.Summary == "Riunione di studio" && outlook.Start == From("Europe/Rome", new DateTime(2026, 10, 1, 9, 0, 0)) && outlook.End == From("Europe/Rome", new DateTime(2026, 10, 1, 10, 30, 0))
              && outlook.Location == "Sala riunioni, piano 2" && outlook.Organizer?.Email == "mario.rossi@studio-prova.it" && outlook.Attendees.Count == 2 && outlook.Sequence == 2
              && outlook.Recurrence == null && outlook.OnlineUrl == null,
              $"Outlook, TZID Europe/Rome: {outlook.When}, {outlook.Location}, {outlook.Attendees.Count} partecipanti (la sveglia VALARM non conta)");
        Check(google.Start == new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc).ToLocalTime() && google.Recurrence == "Ogni settimana il lunedì e il mercoledì, fino al 16/12/2026"
              && google.OnlineUrl == "https://meet.google.com/abc-defg-hij" && google.Location == null,
              $"Google, in UTC e settimanale: {google.When}; \"{google.Recurrence}\"; {google.OnlineUrl}");
        Check(teams.Start == From("W. Europe Standard Time", new DateTime(2026, 11, 12, 14, 30, 0)) && teams.OnlineUrl?.StartsWith("https://teams.microsoft.com/l/meetup-join/") == true
              && teams.Organizer?.Name == "Cliente S.p.A.",
              $"Teams, fuso di Windows e riga spezzata: {teams.When}; link {teams.OnlineUrl?[..40]}…");
        var cancelled = Invitation.Parse(Invites[1].Ics.Replace("METHOD:REQUEST", "METHOD:CANCEL"));
        Check(cancelled?.Cancelled == true && Invitation.Parse("BEGIN:VCALENDAR\nEND:VCALENDAR") == null && Invitation.Parse(new string('x', Invitation.MaxLength + 1) + "BEGIN:VEVENT") == null,
              "METHOD:CANCEL è una riunione annullata; senza VEVENT o troppo grande: nessun invito");

        // Un .ics costruito per bloccare la lettura: righe piegate a 75 caratteri come vuole iCalendar e un parametro lungo
        // pieno di ";" e virgolette (con la regex di prima, 256 KB costavano circa 150 s), poi una riga piegata a un carattere.
        static string Folded(string line, int width) => string.Join("\r\n ", Enumerable.Range(0, (line.Length + width - 1) / width).Select(k => line.Substring(k * width, Math.Min(width, line.Length - k * width))));
        var heavy = new StringBuilder("BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:pesante\r\nSUMMARY:Invito pesante\r\nDTSTART:20261001T090000Z\r\nDTEND:20261001T100000Z\r\n");
        heavy.Append(Folded("X-PESANTE;" + string.Concat(Enumerable.Repeat("A=\"x;y\";", 12_000)) + ":valore", 75)).Append("\r\n");
        heavy.Append(Folded("DESCRIPTION:" + new string('d', 30_000), 1)).Append("\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var parsed = heavy.Length <= Invitation.MaxLength ? Invitation.Parse(heavy.ToString()) : null;
        clock.Stop();
        Check(parsed?.Summary == "Invito pesante" && clock.ElapsedMilliseconds < 1000,
              $".ics di {heavy.Length / 1024} KB con un parametro di {12_000 * 8 / 1024} KB fra virgolette e una riga piegata a un carattere: letto in {clock.ElapsedMilliseconds} ms (al massimo 1000), la riga lunga ignorata");
        var manyLines = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n" + string.Concat(Enumerable.Repeat("X-A:b\r\n", 6000)) + "DTSTART:20261001T090000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        Check(Invitation.Parse(manyLines) == null, "oltre 5000 righe: l'invito non si legge");

        // Solo METHOD:REQUEST chiede una risposta: una risposta (REPLY), una proposta (COUNTER) e un evento pubblicato
        // (PUBLISH, o senza METHOD) si mostrano senza pulsanti, e non si risponde.
        var accepted = Invitation.Parse(Invites[1].Ics.Replace("METHOD:REQUEST", "METHOD:REPLY").Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=ACCEPTED"))!;
        var published = Invitation.Parse(Invites[1].Ics.Replace("METHOD:REQUEST", "X-SENZA-METHOD:1"))!;
        var proposed = Invitation.Parse(Invites[1].Ics.Replace("METHOD:REQUEST", "METHOD:COUNTER"))!;
        bool noReply;
        try { accepted.ReplyMessage(new MailAccount { Email = "luca.verdi@gmail.com" }, new InviteResponse(InviteAnswer.Accept, "", true)); noReply = false; }
        catch (MailException) { noReply = true; }
        Check(google.Answerable && !accepted.Answerable && accepted.Responder?.Email == "ada.prova@gmail.com" && accepted.ResponseStatus == "ACCEPTED"
              && published.Method == "PUBLISH" && !published.Answerable && proposed.Method == "COUNTER" && !proposed.Answerable && noReply,
              $"REQUEST con i pulsanti; REPLY da {accepted.Responder?.Email} ({accepted.ResponseStatus}), senza METHOD ({published.Method}) e COUNTER senza; nessuna risposta a una REPLY");
        string Card(Invitation invite)
        {
            var summary = new MessageSummary { Id = "1", FolderId = "INBOX", Subject = invite.Summary, From = new Address { Email = "luca.verdi@gmail.com" }, Date = DateTime.Now };
            var body = new MessageBody { Html = "<p>Invito</p>", Invite = invite };
            return UI.ReadingPage.Build([new UI.PageEntry(summary, body, true, false, null, false)], light: true).Html;
        }
        var requestCard = Card(google);
        var replyCard = Card(accepted);
        var publishCard = Card(published);
        Check(requestCard.Contains("/accept/0") && requestCard.Contains("/propose/0") && !replyCard.Contains("/accept/") && !replyCard.Contains("/decline/")
              && replyCard.Contains("ha accettato: Punto settimanale") && !publishCard.Contains("/accept/") && publishCard.Contains("aria-label=\"Evento\""),
              "scheda: Accetta e Proponi solo per REQUEST; la REPLY dice \"... ha accettato: Punto settimanale\" senza pulsanti; l'evento pubblicato senza pulsanti");
        var me = new Address { Name = "Ada", Email = "ada.prova@gmail.com" };
        var reply = outlook.Reply(me, new InviteResponse(InviteAnswer.Accept, "Ci sarò, grazie", true));
        Report.AddRange(reply.Split("\r\n").Select(l => "      " + l));
        Check(reply.Contains("METHOD:REPLY") && reply.Contains("UID:040000008200E00074C5B7101A82E00800000000A0B1C2D3E4F5") && reply.Contains("SEQUENCE:2")
              && reply.Contains("ATTENDEE;PARTSTAT=ACCEPTED;CN=\"Ada\":mailto:ada.prova@gmail.com") && reply.Contains("ORGANIZER;CN=\"Mario Rossi\":mailto:mario.rossi@studio-prova.it")
              && reply.Contains("DTSTART;TZID=Europe/Rome:20261001T090000") && reply.Contains("COMMENT:Ci sarò\\, grazie"),
              "risposta iMIP: METHOD:REPLY con UID, SEQUENCE, orari, ORGANIZER, ATTENDEE con PARTSTAT=ACCEPTED e la nota");

        // IMAP finto e SMTP finto: l'invito letto dal messaggio e la risposta che parte.
        using var smtp = new FakeSmtp();
        ImapProvider.TestPlain = true;
        ImapProvider.TestCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            fake.Add("INBOX", InviteMessage(Invites[0].Ics));
            IMailProvider imap = new ImapProvider(new MailAccount
            {
                Kind = AccountKind.Imap, Email = "ada.prova@gmail.com", DisplayName = "Ada",
                Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = fake.Port, SmtpHost = "127.0.0.1", SmtpPort = smtp.Port, SmtpSecurity = MailSecurity.StartTls,
                                        UserName = "ada.prova@gmail.com", ProtectedPassword = Store.Protect("x") },
            });
            var listed = (await imap.GetMessagesAsync(new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, null, null)).Messages.Single();
            var body = await imap.GetBodyAsync(listed);
            Check(body.Invite?.Summary == "Riunione di studio" && body.Invite.Uid.StartsWith("040000"), "IMAP: l'invito letto dalla parte text/calendar del messaggio");
            smtp.Received.Clear();
            await imap.RespondAsync(listed, body.Invite!, new InviteResponse(InviteAnswer.Accept, "Ci sarò", true));
            var sent = smtp.Received.Count == 1 ? MimeMessage.Load(new MemoryStream(smtp.Received[0])) : null;
            var part = sent?.BodyParts.OfType<TextPart>().FirstOrDefault(p => p.ContentType.IsMimeType("text", "calendar"));
            Check(sent?.Subject == "Accettato: Riunione di studio" && sent.To.Mailboxes.Single().Address == "mario.rossi@studio-prova.it"
                  && part?.ContentType.Parameters["method"] == "REPLY" && part.Text.Contains("PARTSTAT=ACCEPTED") && part.Text.Contains("UID:040000008200E00074C5B7101A82E00800000000A0B1C2D3E4F5")
                  && part.Text.Contains("SEQUENCE:2") && part.Text.Contains("ATTENDEE;"),
                  "SMTP finto: \"Accettato: ...\" all'organizzatore, con text/calendar method=REPLY, UID, SEQUENCE, ATTENDEE e PARTSTAT=ACCEPTED");
            smtp.Received.Clear();
            await imap.RespondAsync(listed, body.Invite!, new InviteResponse(InviteAnswer.Decline, "", false));
            Check(smtp.Received.Count == 0, "Rifiuta senza mandare la risposta: nessun messaggio");
            var newStart = new DateTime(2026, 10, 2, 15, 0, 0);
            await imap.RespondAsync(listed, body.Invite!, new InviteResponse(InviteAnswer.Propose, "Meglio venerdì", true, newStart, newStart.AddHours(1)));
            var counter = smtp.Received.Count == 1 ? MimeMessage.Load(new MemoryStream(smtp.Received[0])).BodyParts.OfType<TextPart>().FirstOrDefault(p => p.ContentType.IsMimeType("text", "calendar")) : null;
            Check(counter?.ContentType.Parameters["method"] == "COUNTER" && counter.Text.Contains("DTSTART:" + newStart.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")),
                  "Proponi un nuovo orario: METHOD:COUNTER con il nuovo inizio");
            (imap as IDisposable)?.Dispose();
        }
        finally
        {
            ImapProvider.TestPlain = false;
            ImapProvider.TestCertificate = null;
        }

        // Graph finto: invito di Exchange (eventMessage) senza e con il consenso al calendario, e l'iMIP per un .ics.
        var fakeGraph = new FakeGraph();
        GraphProvider.UseTestHandler(fakeGraph);
        IMailProvider graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@example.com", DisplayName = "Prova" });
        var eventSummary = new MessageSummary { Id = "invito-1", FolderId = "inbox", Subject = "Revisione del bilancio" };
        GraphProvider.TestCalendarConsent = false;
        try
        {
            var eventBody = await graph.GetBodyAsync(eventSummary);
            var ev = eventBody.Invite;
            Check(ev?.GraphMessageId == "invito-1" && ev.Summary == "Revisione del bilancio" && ev.Start == new DateTime(2026, 10, 5, 7, 30, 0, DateTimeKind.Utc).ToLocalTime()
                  && ev.Location == "Sala Verdi" && ev.Recurrence == "Ogni settimana il lunedì, fino al 28/12/2026" && ev.OnlineUrl?.StartsWith("https://teams.microsoft.com/") == true && ev.NeedsConsent,
                  $"Graph: eventMessage letto ({ev?.When}, \"{ev?.Recurrence}\"), senza consenso la scheda chiede il calendario");
            fakeGraph.Requests.Clear();
            string? refused = null;
            try { await graph.RespondAsync(eventSummary, ev!, new InviteResponse(InviteAnswer.Accept, "Ci sarò", true)); }
            catch (MailException ex) { refused = ex.Message; }
            Check(refused?.Contains("Consenti l'accesso al calendario") == true && !fakeGraph.Requests.Any(r => r.Contains("/events/")), "Graph senza consenso: nessuna richiesta, e la spiegazione");
            GraphProvider.TestCalendarConsent = true;
            fakeGraph.Requests.Clear();
            await graph.RespondAsync(eventSummary, ev!, new InviteResponse(InviteAnswer.Accept, "Ci sarò", true));
            await graph.RespondAsync(eventSummary, ev!, new InviteResponse(InviteAnswer.Propose, "Meglio alle 10", true, new DateTime(2026, 10, 5, 10, 0, 0), new DateTime(2026, 10, 5, 11, 0, 0)));
            Report.AddRange(fakeGraph.Requests.Select(r => "      " + r));
            Check(fakeGraph.Requests.Any(r => r.StartsWith("GET me/messages/invito-1?") && r.Contains("$expand=microsoft.graph.eventMessage/event"))
                  && fakeGraph.Requests.Any(r => r.StartsWith("POST me/events/evento-1/accept") && r.Contains("\"comment\":\"Ci sarò\"") && r.Contains("\"sendResponse\":true")),
                  "Graph con consenso: POST .../events/evento-1/accept con comment e sendResponse");
            Check(fakeGraph.Requests.Any(r => r.StartsWith("POST me/events/evento-1/tentativelyAccept") && r.Contains("proposedNewTime")),
                  "Graph: Proponi un nuovo orario con tentativelyAccept e proposedNewTime");
            fakeGraph.Requests.Clear();
            await graph.RespondAsync(new MessageSummary { Id = "g", FolderId = "inbox" }, google, new InviteResponse(InviteAnswer.Decline, "", true));
            var mime = fakeGraph.LastMime is { } raw ? MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(raw))) : null;
            var calendarPart = mime?.BodyParts.OfType<TextPart>().FirstOrDefault(p => p.ContentType.IsMimeType("text", "calendar"));
            Check(fakeGraph.Requests.Any(r => r.StartsWith("POST me/sendMail")) && calendarPart?.ContentType.Parameters["method"] == "REPLY" && calendarPart.Text.Contains("PARTSTAT=DECLINED")
                  && mime!.To.Mailboxes.Single().Address == "luca.verdi@gmail.com",
                  "Graph, invito arrivato come .ics: risposta iMIP in MIME (me/sendMail), text/calendar method=REPLY");
        }
        finally
        {
            GraphProvider.TestCalendarConsent = false;
        }
    }

    // ---------------------------------------------------------------- opzioni e invio pianificato

    /// <summary>
    /// Importanza, conferme e invio pianificato: Graph (PidTagDeferredSendTime, importance, isReadReceiptRequested), MIME di
    /// Gmail (X-Priority, Disposition-Notification-To), DSN di SMTP, punto esclamativo sulle righe ricevute, e l'invio
    /// pianificato di Gmail che parte solo all'ora (simulata) e toglie la bozza.
    /// </summary>
    static async Task SendOptionsAsync()
    {
        Report.Add("--- Opzioni e invio pianificato");
        var fakeGraph = new FakeGraph();
        GraphProvider.UseTestHandler(fakeGraph);
        IMailProvider graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@example.com" });
        var at = DateTime.Now.Date.AddDays(1).AddHours(8);
        await graph.SendAsync(new OutgoingMessage { To = To, Subject = "Pianificato", Html = "<p>Domani</p>", Importance = Importance.High, ReadReceipt = true, DeliveryReceipt = true, SendAt = at });
        Report.AddRange(fakeGraph.Requests.Select(r => "      " + r));
        var patchLine = fakeGraph.Requests.FirstOrDefault(r => r.StartsWith("PATCH me/messages/bozza-1 ")) ?? "";
        var patch = fakeGraph.LastPatch;
        Check(graph.SchedulesOnServer && patch.Contains("\"id\":\"SystemTime 0x3FEF\"") && patch.Contains($"\"value\":\"{at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)}\"")
              && patch.Contains("\"importance\":\"high\"") && patch.Contains("\"isReadReceiptRequested\":true") && patch.Contains("\"isDeliveryReceiptRequested\":true")
              && fakeGraph.Requests.IndexOf(fakeGraph.Requests.First(r => r.StartsWith("POST me/messages/bozza-1/send"))) > fakeGraph.Requests.IndexOf(patchLine),
              "Graph: PATCH con PidTagDeferredSendTime (SystemTime 0x3FEF), importance high e le conferme, poi /send (lo tiene Exchange)");

        using var fake = new FakeImap();
        using var smtp = new FakeSmtp();
        GmailProvider.TestPort = fake.Port;
        GmailProvider.TestSmtpPort = smtp.Port;
        GmailProvider.TestSmtpCertificate = smtp.Certificate.GetCertHashString();
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "gmail-pianificato" };
        using var gmail = new GmailProvider(account);
        try
        {
            var draft = await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Da inviare domani", Html = "<p>Buongiorno</p>", Text = "Buongiorno",
                                                                         Importance = Importance.High, ReadReceipt = true });
            var mime = MimeMessage.Load(new MemoryStream(fake.Stored.Single().Raw));
            Check(mime.Headers["X-Priority"]?.StartsWith("1") == true && mime.Headers["Importance"]?.Equals("high", StringComparison.OrdinalIgnoreCase) == true
                  && mime.Headers[HeaderId.DispositionNotificationTo]?.Contains("prova@example.com") == true,
                  $"MIME di Gmail: X-Priority \"{mime.Headers["X-Priority"]}\", Importance high, Disposition-Notification-To");

            Store.Settings.ScheduledSends.Clear();
            var when = DateTime.Now.AddMinutes(10);
            ScheduledSending.Add(account, draft.Message.StableId, when, "Da inviare domani");
            IMailProvider? Provider(string id) => id == account.Id ? gmail : null;
            int early = await ScheduledSending.RunDueAsync(when.AddMinutes(-1), Provider);
            Check(early == 0 && smtp.Received.Count == 0 && fake.Stored.Count == 1 && Store.Settings.ScheduledSends.Count == 1,
                  "invio pianificato di Gmail: un minuto prima non parte, la bozza resta");
            int onTime = await ScheduledSending.RunDueAsync(when, Provider);
            var delivered = smtp.Received.Count == 1 ? MimeMessage.Load(new MemoryStream(smtp.Received[0])) : null;
            Check(onTime == 1 && delivered?.Subject == "Da inviare domani" && fake.Stored.Count(m => !m.Has("\\Deleted")) == 0 && Store.Settings.ScheduledSends.Count == 0,
                  "all'ora simulata parte com'era nelle Bozze (via SMTP), poi la bozza esce dalle Bozze e l'invio dall'elenco");
            ScheduledSending.Add(account, "999999", when, "Bozza sparita");
            string? missing = null;
            await ScheduledSending.RunDueAsync(when, Provider, (_, why) => missing = why);
            Check(missing?.Contains("non c'è più") == true && Store.Settings.ScheduledSends.Count == 0, "bozza sparita nel frattempo: l'invio esce dall'elenco e lo si dice");

            // Partito il messaggio, la bozza che non esce dalle Bozze (STORE rifiutato) non lo fa ripartire al minuto dopo.
            smtp.Received.Clear();
            var second = await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Pulizia rifiutata", Html = "<p>Una volta</p>", Text = "Una volta" });
            ScheduledSending.Add(account, second.Message.StableId, when, "Pulizia rifiutata");
            fake.RefuseOnce = "UID STORE";
            int firstRun = await ScheduledSending.RunDueAsync(when, Provider);
            int secondRun = await ScheduledSending.RunDueAsync(when.AddMinutes(1), Provider);
            Check(firstRun == 1 && secondRun == 0 && smtp.Received.Count == 1 && Store.Settings.ScheduledSends.Count == 0 && fake.RefuseOnce == null,
                  $"SMTP riuscito e bozza non tolta (STORE rifiutato): partito una volta sola ({smtp.Received.Count}), l'invio esce dall'elenco");

            // Errori: di rete l'invio resta e lo si dice una volta; gli altri lo tolgono, con il motivo.
            var stub = DispatchProxy.Create<IMailProvider, SenderStub>();
            var stubAccount = new MailAccount { Kind = AccountKind.Imap, Email = "stub@example.com", Id = "stub" };
            ((SenderStub)(object)stub).Account = stubAccount;
            var problems = new List<string>();
            IMailProvider? StubOf(string id) => id == "stub" ? stub : null;
            ((SenderStub)(object)stub).Send = () => Task.FromException<bool>(new MailException("smtp.example.com non risponde sulla porta 465 (SMTP).", new SocketException(10060)));
            ScheduledSending.Add(stubAccount, "b1", when, "Offerta lunedì");
            await ScheduledSending.RunDueAsync(when, StubOf, (_, why) => problems.Add(why));
            await ScheduledSending.RunDueAsync(when.AddMinutes(1), StubOf, (_, why) => problems.Add(why));
            bool kept = Store.Settings.ScheduledSends.Count == 1;
            ((SenderStub)(object)stub).Send = () => Task.FromResult(true);
            int recovered = await ScheduledSending.RunDueAsync(when.AddMinutes(2), StubOf, (_, why) => problems.Add(why));
            Check(kept && problems.Count == 1 && problems[0].Contains("non è ancora partito") && problems[0].Contains("riprova") && recovered == 1 && Store.Settings.ScheduledSends.Count == 0,
                  $"rete assente: l'invio resta in elenco, un solo avviso in due tentativi, poi parte ({problems.FirstOrDefault()?.Split('\n')[0]})");
            problems.Clear();
            ((SenderStub)(object)stub).Send = () => Task.FromException<bool>(new MailException("smtp.example.com ha rifiutato il destinatario mario@exmaple.com."));
            ScheduledSending.Add(stubAccount, "b2", when, "Destinatario sbagliato");
            await ScheduledSending.RunDueAsync(when, StubOf, (_, why) => problems.Add(why));
            await ScheduledSending.RunDueAsync(when.AddMinutes(1), StubOf, (_, why) => problems.Add(why));
            Check(problems.Count == 1 && problems[0].Contains("non è partito: smtp.example.com ha rifiutato") && problems[0].Contains("Bozze") && Store.Settings.ScheduledSends.Count == 0,
                  "errore che non passa da solo (destinatario rifiutato): l'invio esce dall'elenco, l'avviso dice il motivo e che il messaggio è nelle Bozze");
        }
        finally
        {
            GmailProvider.TestPort = null;
            GmailProvider.TestSmtpPort = null;
            GmailProvider.TestSmtpCertificate = null;
            Store.Settings.ScheduledSends.Clear();
        }

        // DSN (conferma di recapito) e punto esclamativo sulle righe ricevute, con un account IMAP.
        ImapProvider.TestPlain = true;
        ImapProvider.TestCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            using var server = new FakeImap(new Flavor(Gmail: false));
            var urgent = new MimeMessage { Subject = "Urgente", Body = new TextPart("plain") { Text = "Serve subito" } };
            urgent.From.Add(new MailboxAddress("Cliente", "cliente@example.com"));
            urgent.Headers.Add("X-Priority", "1 (Highest)");
            urgent.Headers.Add("Importance", "High");
            server.Add("INBOX", urgent);
            server.Add("INBOX", "Normale");
            using var imap = new ImapProvider(new MailAccount
            {
                Kind = AccountKind.Imap, Email = "mario@studio-prova.it",
                Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = server.Port, SmtpHost = "127.0.0.1", SmtpPort = smtp.Port, SmtpSecurity = MailSecurity.StartTls,
                                        UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect("x") },
            });
            var rows = (await imap.GetMessagesAsync(new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, null, null)).Messages;
            Check(rows.Single(m => m.Subject == "Urgente").IsImportant && !rows.Single(m => m.Subject == "Normale").IsImportant
                  && server.Commands.Any(c => c.Contains("HEADER.FIELDS") && c.Contains("X-PRIORITY", StringComparison.OrdinalIgnoreCase)),
                  "righe ricevute: importanza alta da X-Priority e Importance, letta con l'elenco (HEADER.FIELDS)");
            smtp.Commands.Clear();
            await imap.SendAsync(new OutgoingMessage { To = To, Subject = "Con conferma", Html = "<p>Ciao</p>", Text = "Ciao", DeliveryReceipt = true });
            Report.AddRange(smtp.Commands.Select(c => "      smtp: " + c));
            Check(smtp.Commands.Any(c => c.StartsWith("RCPT TO:") && c.Contains("NOTIFY=SUCCESS,FAILURE")), "conferma di recapito: RCPT TO con NOTIFY=SUCCESS,FAILURE (DSN)");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            ImapProvider.TestCertificate = null;
        }
    }

    /// <summary>Un account finto per gli invii pianificati: solo Account e SendDraftAsync, il resto non serve.</summary>
    public class SenderStub : DispatchProxy
    {
        public MailAccount Account = new();
        public Func<Task<bool>> Send = () => Task.FromResult(true);

        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Account" => Account,
            "SendDraftAsync" => Send(),
            _ => throw new NotSupportedException(method?.Name),
        };
    }

    sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => Task.FromResult(answer(request));
    }

    /// <summary>Il testo dell'unico allegato di una bozza scritta sul server finto.</summary>
    static string AttachmentText(FakeImap.Message stored)
    {
        using var content = new MemoryStream();
        ((MimePart)MimeMessage.Load(new MemoryStream(stored.Raw)).Attachments.Single()).Content!.DecodeTo(content);
        return Encoding.UTF8.GetString(content.ToArray());
    }

    /// <summary>
    /// Com'è fatto il server IMAP finto: Gmail (X-GM-EXT-1, le cartelle di Gmail), oppure un server qualsiasi, con o senza
    /// SPECIAL-USE (senza, le cartelle stanno dentro INBOX con nomi italiani, come su Courier e cPanel), MOVE e UIDPLUS.
    /// </summary>
    sealed record Flavor(bool Gmail = true, bool SpecialUse = true, bool Move = true, bool UidPlus = true, bool Idle = true)
    {
        public override string ToString() => Gmail ? "Gmail" : $"IMAP{(SpecialUse ? " SPECIAL-USE" : "")}{(Move ? " MOVE" : "")}{(UidPlus ? " UIDPLUS" : "")}{(!SpecialUse && !Move && !UidPlus ? " senza estensioni" : "")}";
    }

    /// <summary>
    /// Un server IMAP minimo, solo quello che MailKit chiede nelle prove: CAPABILITY, AUTHENTICATE e LOGIN, LIST, STATUS,
    /// SELECT ed EXAMINE, APPEND, FETCH e UID FETCH (struttura, parti, messaggio intero), UID SEARCH (X-GM-MSGID, X-GM-RAW,
    /// HEADER, testo), UID STORE (flag, X-GM-LABELS), EXPUNGE e UID EXPUNGE, UID MOVE e UID COPY con COPYUID. Le cartelle
    /// sono in Folders; quella delle Bozze è Stored. Nella variante Gmail le cartelle di Gmail e X-GM-EXT-1; nelle altre
    /// solo le estensioni della variante. Un comando che il server non ha riceve BAD, anche quelli che non conosce affatto
    /// (SUBSCRIBE, CLOSE, UNSELECT, STORE senza UID): fino all'audit dell'11 ottobre (C-43) a quelli rispondeva OK senza
    /// fare nulla, e una prova sulle cartelle sarebbe passata a vuoto. Dalle scelte del 12 ottobre (C-47) RENAME e DELETE
    /// li fa davvero, e CREATE anche nella variante Gmail (dove prima una cartella creata non compariva in LIST): RENAME
    /// porta con sé messaggi e sottocartelle, e in Gmail l'etichetta sui messaggi; DELETE toglie la cartella con i suoi
    /// messaggi. Quello che continua a non fare riceve BAD: rinominare la Posta in arrivo, eliminare una cartella che ne
    /// contiene altre. Gli allegati sono le parti con un contenuto
    /// (MimePart), nella struttura e nelle parti chieste per numero: un .eml allegato non è fra quelli. Se servendo una
    /// connessione il server solleva un errore, la chiude con BYE e lo scrive in ServerErrors (C-45): prima l'errore si
    /// perdeva con il lavoro scartato, e il client vedeva solo una connessione caduta, senza la causa.
    /// </summary>
    sealed class FakeImap : IDisposable
    {
        public sealed class Message(uint uid, ulong msgId, string flags, byte[] raw)
        {
            public uint Uid { get; } = uid;
            public ulong MsgId { get; } = msgId;
            public string Flags { get; set; } = flags;
            public byte[] Raw { get; } = raw;
            /// <summary>Le etichette di Gmail (X-GM-LABELS), come \Inbox.</summary>
            public List<string> Labels { get; } = new();
            /// <summary>
            /// Il segno \Recent di un server vero ("nessun programma ha ancora aperto la cartella dopo l'arrivo"): lo mette la
            /// prova, un SELECT della cartella lo toglie a tutti i suoi messaggi, un EXAMINE no (RFC 3501). Non compare in FLAGS.
            /// </summary>
            public bool Recent { get; set; }
            public bool Has(string flag) => Flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(flag, StringComparer.OrdinalIgnoreCase);
            public void Set(string flag, bool on)
            {
                var flags = Flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(f => !f.Equals(flag, StringComparison.OrdinalIgnoreCase)).ToList();
                if (on) flags.Add(flag);
                Flags = string.Join(' ', flags);
            }
        }

        public const string Drafts = "[Gmail]/Bozze", Trash = "[Gmail]/Cestino", AllMail = "[Gmail]/Tutti i messaggi";
        public readonly List<Message> Stored;
        /// <summary>Gmail: Posta in arrivo, Bozze (Stored), Cestino, Tutti i messaggi e un'etichetta dell'utente, Lavoro.</summary>
        public readonly Dictionary<string, List<Message>> Folders;
        public readonly List<string> Commands = new();
        /// <summary>Gli stessi comandi con il numero della connessione su cui sono arrivati (la prima è 1).</summary>
        public readonly List<(int Connection, string Text)> Traffic = new();
        int connections;
        /// <summary>Al prossimo APPEND il messaggio si salva, poi la connessione cade senza risposta.</summary>
        public bool DropAfterAppend;
        /// <summary>Alla prossima COPY (anche UID COPY) i messaggi si copiano, poi la connessione cade senza risposta.</summary>
        public bool DropAfterCopy;
        /// <summary>Il prossimo comando con questo nome (per esempio "UID STORE") riceve NO, senza fare nulla.</summary>
        public string? RefuseOnce;
        /// <summary>
        /// Chiamata con il nome di ogni comando arrivato, prima della risposta: la prova può trattenerla finché non ha fatto
        /// la sua mossa (per esempio annullare una ricerca mentre il server non ha ancora risposto a LIST).
        /// </summary>
        public Func<string, Task>? Hold;
        /// <summary>Un comando con questo nome fa sollevare un errore al server (un difetto del server finto, fatto apposta).</summary>
        public string? FailOn;
        /// <summary>CREATE risponde OK ma la cartella non nasce: la LIST che MailKit fa subito dopo non la trova.</summary>
        public bool ForgetCreated;
        /// <summary>
        /// LIST di un nome preciso (senza * né %) risponde solo per quella cartella, e LIST (SPECIAL-USE) solo per le
        /// cartelle speciali, come un server vero; di base risponde sempre con tutte, e MailKit viene a sapere di ogni
        /// cartella alla prima domanda.
        /// </summary>
        public bool ExactList;
        /// <summary>Attesa prima di rispondere a EXAMINE, come Gmail su una cartella grande (fino a 0,3 s).</summary>
        public TimeSpan ExamineDelay;
        /// <summary>
        /// Le risposte a UID FETCH più lunghe di Chunk byte vanno a pezzi di quella misura, con Pause fra uno e l'altro, come
        /// da una rete lenta: per l'avanzamento di uno scaricamento e per Annulla a metà (ritocchi del 16 ottobre 2026). Null,
        /// di base: in una volta sola, come sempre.
        /// </summary>
        public (int Chunk, TimeSpan Pause)? Trickle;
        /// <summary>I byte delle risposte a pezzi già mandati, e quante volte il client ha chiuso la connessione a metà di una.</summary>
        public long TrickleSent;
        public int TrickleCut;
        /// <summary>Il server non risponde al LOGOUT (per un minuto e mezzo): la chiusura del provider non deve aspettarlo.</summary>
        public volatile bool SilentLogout;
        /// <summary>La password arrivata con AUTHENTICATE PLAIN o LOGIN (nel registro dei comandi non va).</summary>
        public string? Password;
        /// <summary>Se c'è, l'unica password accettata: le altre ricevono NO [AUTHENTICATIONFAILED], come un server vero.</summary>
        public string? ExpectedPassword;
        /// <summary>Ogni comando anche nel registro (il server di --imap-finto, che gira da solo).</summary>
        public bool LogCommands;
        /// <summary>
        /// Come un server vero, chi è in IDLE sulla Posta in arrivo viene a sapere quello che fanno le altre connessioni: i
        /// messaggi usciti (EXPUNGE) e i segni cambiati (FETCH con l'UID). Solo per il server di --imap-finto: le prove della
        /// suite decidono da sé che cosa annunciare (Push).
        /// </summary>
        public bool EchoToIdlers;
        /// <summary>
        /// Come un server vero, una connessione che tiene aperta una cartella viene a sapere dei messaggi arrivati dopo solo
        /// quando il server li annuncia (EXISTS), e qui li annuncia con NOOP: fino ad allora il client la conta com'era.
        /// </summary>
        public bool AnnounceOnNoop;
        public readonly Flavor Kind;
        readonly TcpListener listener;
        uint nextUid = 1;
        readonly string capabilities;
        readonly char separator;

        /// <summary>Le cartelle speciali della variante: Bozze, Posta inviata, Cestino, Posta indesiderata, e una dell'utente.</summary>
        public readonly string DraftsFolder, SentFolder, TrashFolder, JunkFolder, UserFolder;

        public FakeImap(Flavor? kind = null, int port = 0)
        {
            Kind = kind ?? new Flavor();
            listener = new TcpListener(IPAddress.Loopback, port);
            if (Kind.Gmail)
            {
                (DraftsFolder, SentFolder, TrashFolder, JunkFolder, UserFolder, separator) = (Drafts, "[Gmail]/Posta inviata", Trash, "[Gmail]/Spam", "Lavoro", '/');
                capabilities = "IMAP4rev1 UIDPLUS MOVE SPECIAL-USE IDLE X-GM-EXT-1 AUTH=XOAUTH2 SASL-IR";
                Folders = new() { ["INBOX"] = new(), [Drafts] = new(), [Trash] = new(), [AllMail] = new(), ["Lavoro"] = new() };
            }
            else
            {
                (DraftsFolder, SentFolder, TrashFolder, JunkFolder, UserFolder, separator) = Kind.SpecialUse
                    ? ("Drafts", "Sent", "Trash", "Junk", "Clienti", '/')
                    : ("INBOX.Bozze", "INBOX.Posta inviata", "INBOX.Cestino", "INBOX.Spam", "INBOX.Clienti", '.');
                capabilities = "IMAP4rev1" + (Kind.UidPlus ? " UIDPLUS" : "") + (Kind.Move ? " MOVE" : "") + (Kind.SpecialUse ? " SPECIAL-USE" : "") + (Kind.Idle ? " IDLE" : "") + " AUTH=PLAIN SASL-IR";
                Folders = new() { ["INBOX"] = new(), [DraftsFolder] = new(), [SentFolder] = new(), [TrashFolder] = new(), [JunkFolder] = new(), [UserFolder] = new() };
                if (Kind.SpecialUse) Folders["Archive"] = new();
            }
            Stored = Folders[DraftsFolder];
            listener.Start();
            _ = AcceptAsync();
        }

        /// <summary>Un messaggio nuovo nella cartella, con le etichette date.</summary>
        public Message Add(string folder, string subject, params string[] labels)
        {
            var mime = new MimeMessage { Subject = subject, Body = new TextPart("html") { Text = $"<p>{subject}</p>" } };
            mime.From.Add(new MailboxAddress("Mittente", "mittente@example.com"));
            var message = Add(folder, mime);
            message.Labels.AddRange(labels);
            return message;
        }

        /// <summary>Un messaggio qualsiasi nella cartella, con i flag dati.</summary>
        public Message Add(string folder, MimeMessage mime, string flags = "")
        {
            mime.MessageId ??= MimeKit.Utils.MimeUtils.GenerateMessageId();
            using var raw = new MemoryStream();
            mime.WriteTo(raw);
            var uid = nextUid++;
            var message = new Message(uid, 1000 + uid, flags, raw.ToArray());
            Folders[folder].Add(message);
            return message;
        }

        /// <summary>Il nome di una cartella all'inizio del testo, fra virgolette o no.</summary>
        static string Mailbox(string text)
        {
            text = text.Trim();
            return text.StartsWith('"') ? text[1..text.IndexOf('"', 1)] : text.Split(' ')[0];
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public void Dispose() => listener.Stop();

        /// <summary>Le connessioni in IDLE adesso, e quello che è successo su di loro (IDLE, DONE, comandi arrivati durante l'IDLE).</summary>
        readonly List<NetworkStream> idlers = new();
        public readonly List<string> IdleLog = new();
        public int Idling { get { lock (idlers) return idlers.Count; } }

        /// <summary>Una risposta non richiesta a chi è in IDLE, come fa il server quando arriva un messaggio (* N EXISTS).</summary>
        public void Push(string untagged)
        {
            lock (idlers)
                foreach (var stream in idlers)
                {
                    // A una connessione muta (MuteOpen, MuteIdlers) non arriva nulla, come a una morta.
                    if (idleConnection.TryGetValue(stream, out var c) && IsMuted(c)) continue;
                    try { stream.Write(Encoding.UTF8.GetBytes(untagged + "\r\n")); } catch (IOException) { }
                }
        }

        // ---------------------------------------------------------------- connessioni mute (audit di affidabilità, arrivo, 25 ottobre 2026)

        /// <summary>
        /// Le connessioni rese mute (per numero): il server legge quello che arriva e non risponde più, senza chiudere, come
        /// una connessione morta senza che nessuno se ne accorga (PC sospeso e ripreso, rete cambiata, NAT del router scaduto).
        /// Nate con le sonde dell'audit (ramo affidabilita-arrivo), qui per le prove delle correzioni (ProvaBozze.AffidabilitaArrivo.cs)
        /// e per le prove a finestra (strumenti\arrivo).
        /// </summary>
        readonly HashSet<int> muted = new();
        /// <summary>Anche le connessioni nuove sono mute, senza il saluto: un server che non risponde più a nessuno.</summary>
        public volatile bool MuteNew;
        /// <summary>
        /// Le connessioni nuove si chiudono subito, senza il saluto: un server che non si raggiunge (correzioni dell'audit di
        /// affidabilità del 25 ottobre 2026, elenco: la prima pagina che non arriva con la copia locale già visibile, E-01).
        /// </summary>
        public volatile bool RefuseNew;
        readonly HashSet<int> open = new();
        readonly Dictionary<NetworkStream, int> idleConnection = new();
        public int ConnectionCount => Volatile.Read(ref connections);
        /// <summary>Tutte le connessioni aperte adesso diventano mute; quelle nuove no (come dopo la ripresa del PC).</summary>
        public void MuteOpen() { lock (open) lock (muted) muted.UnionWith(open); }
        /// <summary>Solo le connessioni in IDLE adesso diventano mute (un NAT che lascia cadere la connessione ferma da più tempo).</summary>
        public void MuteIdlers() { lock (idlers) lock (muted) muted.UnionWith(idleConnection.Values); }
        bool IsMuted(int connection) { lock (muted) return muted.Contains(connection); }
        /// <summary>Le connessioni in IDLE adesso, per numero.</summary>
        public List<int> IdleConnections() { lock (idlers) return idleConnection.Values.ToList(); }

        /// <summary>Le connessioni in IDLE tagliate, come una rete che cade.</summary>
        public void DropIdlers()
        {
            lock (idlers)
            {
                foreach (var stream in idlers) stream.Close();
                idlers.Clear();
            }
        }

        async Task AcceptAsync()
        {
            try
            {
                while (true) _ = ServeAsync(await listener.AcceptTcpClientAsync());
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        /// <summary>La risposta a LIST: le cartelle della variante, con gli attributi SPECIAL-USE se li ha.</summary>
        string ListLines()
        {
            List<string> names;
            lock (Folders) names = Folders.Keys.ToList();
            string Children(string name) => names.Any(other => other.StartsWith(name + separator, StringComparison.Ordinal)) ? "\\HasChildren" : "\\HasNoChildren";
            // Gmail: le cartelle di sistema, fisse, poi le etichette dell'utente (Lavoro e quelle create o rinominate dopo).
            if (Kind.Gmail)
                return "* LIST (\\HasNoChildren) \"/\" \"INBOX\"\n* LIST (\\HasChildren \\Noselect) \"/\" \"[Gmail]\"\n* LIST (\\HasNoChildren \\Drafts) \"/\" \"[Gmail]/Bozze\"\n" +
                       "* LIST (\\HasNoChildren \\Trash) \"/\" \"[Gmail]/Cestino\"\n* LIST (\\HasNoChildren \\All) \"/\" \"[Gmail]/Tutti i messaggi\"\n" +
                       string.Concat(names.Where(name => name != "INBOX" && !name.StartsWith("[Gmail]", StringComparison.Ordinal)).Select(name => $"* LIST ({Children(name)}) \"/\" \"{name}\"\n"));
            string Attribute(string name) => !Kind.SpecialUse ? "" : name == DraftsFolder ? " \\Drafts" : name == SentFolder ? " \\Sent" : name == TrashFolder ? " \\Trash"
                                                                   : name == JunkFolder ? " \\Junk" : name == "Archive" ? " \\Archive" : "";
            return string.Concat(names.Select(name => $"* LIST ({Children(name)}{Attribute(name)}) \"{separator}\" \"{name}\"\n"));
        }

        /// <summary>Il primo nome di cartella di un testo (fra virgolette o no) e quello che lo segue.</summary>
        static (string Name, string After) TakeMailbox(string text)
        {
            text = text.Trim();
            if (text.StartsWith('"') && text.IndexOf('"', 1) is > 0 and var close) return (text[1..close], text[(close + 1)..].Trim());
            int space = text.IndexOf(' ');
            return space < 0 ? (text, "") : (text[..space], text[(space + 1)..].Trim());
        }

        async Task ServeAsync(TcpClient client)
        {
            using var _ = client;
            var stream = client.GetStream();
            async Task Send(string text) => await stream.WriteAsync(Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n")));
            int connection = Interlocked.Increment(ref connections);
            if (RefuseNew) return; // chiusa subito, come un server che non si raggiunge
            lock (open) open.Add(connection);
            if (MuteNew) lock (muted) muted.Add(connection); // un server che non risponde a nessuno: nemmeno il saluto
            if (!IsMuted(connection)) await Send($"* OK [CAPABILITY {capabilities}] server finto\n");
            var selected = DraftsFolder;
            int announced = 0; // i messaggi della cartella aperta che questa connessione conosce (AnnounceOnNoop)
            List<Message> Current()
            {
                lock (Folders) return Folders.GetValueOrDefault(selected) ?? new();
            }
            bool Has(string extension) => capabilities.Split(' ').Contains(extension);
            try
            {
                while (await ReadLineAsync(stream) is { } line)
                {
                    var tag = line[..line.IndexOf(' ')];
                    var rest = line[(tag.Length + 1)..];
                    var words = rest.Split(' ');
                    var command = words[0].ToUpperInvariant() == "UID" ? "UID " + words[1].ToUpperInvariant() : words[0].ToUpperInvariant();
                    Commands.Add(command is "AUTHENTICATE" or "LOGIN" ? command : rest);
                    lock (Traffic) Traffic.Add((connection, Commands[^1]));
                    if (LogCommands) Log.Write("server IMAP finto: " + Commands[^1] + (IsMuted(connection) ? $" (connessione {connection} muta, nessuna risposta)" : ""));
                    if (IsMuted(connection)) continue; // muta: legge e non risponde
                    if (Hold is { } hold) await hold(command);
                    if (FailOn == command) throw new InvalidOperationException("errore di prova del server IMAP finto");
                    if (RefuseOnce == command)
                    {
                        RefuseOnce = null;
                        await Send($"{tag} NO rifiutato (prova)\n");
                        continue;
                    }
                    switch (command)
                    {
                        case "CAPABILITY":
                            await Send($"* CAPABILITY {capabilities}\n{tag} OK fatto\n");
                            break;
                        case "CREATE":
                            // Una cartella nuova (il Cestino creato al primo bisogno): da qui in poi compare in LIST.
                            if (!ForgetCreated) lock (Folders) Folders.TryAdd(Mailbox(rest["CREATE ".Length..]), new());
                            await Send($"{tag} OK creata\n");
                            break;
                        case "RENAME":
                        {
                            // RFC 3501: la cartella prende il nome nuovo con i suoi messaggi (gli stessi UID), e con lei le sue
                            // sottocartelle; in Gmail il nome è anche l'etichetta dei messaggi. Rinominare la Posta in arrivo (che la
                            // svuota in una cartella nuova) questo server non lo fa: BAD.
                            var (oldName, then) = TakeMailbox(rest["RENAME ".Length..]);
                            var newName = TakeMailbox(then).Name;
                            string reply;
                            lock (Folders)
                            {
                                if (oldName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) reply = "BAD il server finto non rinomina la Posta in arrivo";
                                else if (!Folders.ContainsKey(oldName)) reply = "NO [NONEXISTENT] la cartella non c'è";
                                else if (newName.Length == 0 || Folders.ContainsKey(newName)) reply = "NO [ALREADYEXISTS] il nome c'è già";
                                else
                                {
                                    foreach (var name in Folders.Keys.Where(k => k == oldName || k.StartsWith(oldName + separator, StringComparison.Ordinal)).ToList())
                                    {
                                        var renamed = newName + name[oldName.Length..];
                                        Folders.Remove(name, out var kept);
                                        Folders[renamed] = kept!;
                                        if (Kind.Gmail)
                                            foreach (var m in Folders.Values.SelectMany(f => f))
                                                if (m.Labels.IndexOf(name) is >= 0 and var label) m.Labels[label] = renamed;
                                        if (selected == name) selected = ""; // su questa connessione non è più aperta, come la vede MailKit
                                    }
                                    reply = "OK rinominata";
                                }
                            }
                            await Send($"{tag} {reply}\n");
                            break;
                        }
                        case "DELETE":
                        {
                            // La cartella sparisce con i suoi messaggi (in Gmail l'etichetta si toglie anche dai messaggi delle altre
                            // cartelle). La Posta in arrivo non si elimina (RFC 3501). Una cartella che ne contiene altre un server
                            // vero la lascia come contenitore non selezionabile: questo server non lo fa, BAD.
                            var gone = Mailbox(rest["DELETE ".Length..]);
                            string reply;
                            lock (Folders)
                            {
                                if (gone.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) reply = "NO la Posta in arrivo non si elimina";
                                else if (!Folders.ContainsKey(gone)) reply = "NO [NONEXISTENT] la cartella non c'è";
                                else if (Folders.Keys.Any(k => k.StartsWith(gone + separator, StringComparison.Ordinal))) reply = "BAD il server finto non elimina una cartella che ne contiene altre";
                                else
                                {
                                    Folders.Remove(gone);
                                    if (Kind.Gmail) foreach (var m in Folders.Values.SelectMany(f => f)) m.Labels.Remove(gone);
                                    if (selected == gone) selected = "";
                                    reply = "OK eliminata";
                                }
                            }
                            await Send($"{tag} {reply}\n");
                            break;
                        }
                        case "AUTHENTICATE":
                            // PLAIN con la risposta iniziale (SASL-IR): "\0utente\0password" in base64.
                            if (words.Length > 2 && words[1].Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
                                Password = Encoding.UTF8.GetString(Convert.FromBase64String(words[2])).Split('\0').Last();
                            if (ExpectedPassword != null && Password != ExpectedPassword)
                            {
                                await Send($"{tag} NO [AUTHENTICATIONFAILED] credenziali non valide (prova)\n");
                                break;
                            }
                            await Send($"{tag} OK [CAPABILITY {capabilities.Replace(" AUTH=XOAUTH2 SASL-IR", "").Replace(" AUTH=PLAIN SASL-IR", "")}] entrato\n");
                            break;
                        case "LOGIN":
                            Password = words.Length > 2 ? words[2].Trim('"') : null;
                            await Send(ExpectedPassword != null && Password != ExpectedPassword ? $"{tag} NO [AUTHENTICATIONFAILED] credenziali non valide (prova)\n" : $"{tag} OK entrato\n");
                            break;
                        case "LIST":
                            if (rest.EndsWith("\"\"")) await Send($"* LIST (\\Noselect) \"{separator}\" \"\"\n");
                            else if (rest.Contains("\"INBOX\"", StringComparison.OrdinalIgnoreCase)) await Send($"* LIST (\\HasNoChildren) \"{separator}\" \"INBOX\"\n");
                            else if (ExactList && rest.Contains("(SPECIAL-USE)", StringComparison.OrdinalIgnoreCase))
                                await Send(string.Concat(ListLines().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                    .Where(l => new[] { "\\Drafts", "\\Sent", "\\Trash", "\\Junk", "\\Archive", "\\All" }.Any(l.Contains)).Select(l => l + "\n"))); // solo le cartelle speciali
                            else if (ExactList && TakeMailbox(TakeMailbox(rest["LIST ".Length..]).After).Name is var exact && !exact.Contains('*') && !exact.Contains('%'))
                                await Send(string.Concat(ListLines().Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.EndsWith($" \"{exact}\"", StringComparison.Ordinal)).Select(l => l + "\n")));
                            else await Send(ListLines());
                            await Send($"{tag} OK fatto\n");
                            break;
                        case "STATUS":
                            var asked = Mailbox(rest["STATUS ".Length..]);
                            var inside = Folders.GetValueOrDefault(asked) ?? new();
                            await Send($"* STATUS \"{asked}\" (MESSAGES {inside.Count} UNSEEN {inside.Count(m => !m.Has("\\Seen"))})\n{tag} OK fatto\n");
                            break;
                        case "SELECT" or "EXAMINE":
                            if (command == "EXAMINE" && ExamineDelay > TimeSpan.Zero) await Task.Delay(ExamineDelay);
                            selected = Mailbox(rest[(words[0].Length + 1)..]);
                            if (command == "SELECT") foreach (var m in Current()) m.Recent = false; // EXAMINE lascia tutto com'è
                            var count = Current().Count;
                            announced = count;
                            await Send("* FLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft)\n* OK [PERMANENTFLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft \\*)] flag\n" +
                                       $"* {count} EXISTS\n* 0 RECENT\n* OK [UIDVALIDITY 1] uid\n* OK [UIDNEXT {nextUid}] uid\n" +
                                       $"{tag} OK [{(command == "EXAMINE" ? "READ-ONLY" : "READ-WRITE")}] fatto\n");
                            break;
                        case "APPEND":
                            var into = Mailbox(rest["APPEND ".Length..]);
                            var open = rest.LastIndexOf('{');
                            var size = int.Parse(rest[(open + 1)..rest.LastIndexOf('}')].TrimEnd('+'));
                            if (!rest.EndsWith("+}")) await Send("+ pronto\n");
                            var raw = new byte[size];
                            await stream.ReadExactlyAsync(raw);
                            await ReadLineAsync(stream);
                            var flags = rest.Contains('(') ? rest[(rest.IndexOf('(') + 1)..rest.IndexOf(')')] : "";
                            var uid = nextUid++;
                            var message = new Message(uid, 1000 + uid, flags, raw);
                            Folders[into].Add(message);
                            if (DropAfterAppend)
                            {
                                DropAfterAppend = false;
                                return; // la connessione si chiude senza la risposta
                            }
                            await Send(Has("UIDPLUS") ? $"{tag} OK [APPENDUID 1 {message.Uid}] fatto\n" : $"{tag} OK fatto\n");
                            break;
                        case "UID FETCH":
                            foreach (var m in Current().Where(m => Uids(words[2]).Contains(m.Uid)).ToList())
                            {
                                var answer = Fetch(m, Current().IndexOf(m) + 1, rest);
                                if (Trickle is not { } slow || answer.Length <= slow.Chunk)
                                {
                                    await stream.WriteAsync(answer);
                                    continue;
                                }
                                // A pezzi, come da una rete lenta; se il client chiude la connessione a metà, il server smette e lo dice.
                                for (int at = 0; at < answer.Length; at += slow.Chunk)
                                {
                                    try
                                    {
                                        await stream.WriteAsync(answer.AsMemory(at, Math.Min(slow.Chunk, answer.Length - at)));
                                        await stream.FlushAsync();
                                    }
                                    catch (IOException)
                                    {
                                        Interlocked.Increment(ref TrickleCut);
                                        throw;
                                    }
                                    Interlocked.Add(ref TrickleSent, Math.Min(slow.Chunk, answer.Length - at));
                                    await Task.Delay(slow.Pause);
                                }
                            }
                            await Send($"{tag} OK fatto\n");
                            break;
                        case "FETCH":
                            // Per numero d'ordine: la prima pagina dell'elenco.
                            foreach (var seq in Uids(words[1]).Where(n => n >= 1 && n <= Current().Count).Order().ToList())
                                await stream.WriteAsync(Fetch(Current()[(int)seq - 1], (int)seq, rest));
                            await Send($"{tag} OK fatto\n");
                            break;
                        case "UID SEARCH" when rest.Contains("HEADER", StringComparison.OrdinalIgnoreCase):
                            // HEADER Message-Id valore: la bozza appena scritta, cercata dopo una connessione caduta.
                            var id = System.Text.RegularExpressions.Regex.Match(rest, "HEADER\\s+\"?Message-Id\"?\\s+\"?<?([^\">\\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
                            await Send($"* SEARCH {string.Join(' ', Current().Where(m => MimeMessage.Load(new MemoryStream(m.Raw)).MessageId == id && (!rest.Contains("UNDELETED") || !m.Has("\\Deleted"))).Select(m => m.Uid))}\n{tag} OK fatto\n");
                            break;
                        case "UID SEARCH" when rest.Contains("X-GM-RAW", StringComparison.OrdinalIgnoreCase):
                            // La ricerca di Gmail: qui trova tutti i messaggi della cartella.
                            await Send($"* SEARCH {string.Join(' ', Current().Select(m => m.Uid))}\n{tag} OK fatto\n");
                            break;
                        case "UID SEARCH" when rest.Contains("X-GM-MSGID", StringComparison.OrdinalIgnoreCase) || rest.Contains("X-GM-THRID", StringComparison.OrdinalIgnoreCase):
                            var wanted = ulong.Parse(words[^1]);
                            await Send($"* SEARCH {string.Join(' ', Current().Where(m => m.MsgId == wanted).Select(m => m.Uid))}\n{tag} OK fatto\n");
                            break;
                        case "UID SEARCH":
                            // Ricerca di un server qualsiasi: la prima stringa fra virgolette nell'oggetto, nel mittente o nel testo;
                            // UNSEEN, UNDELETED e ALL come da RFC 3501.
                            // Le parole semplici MailKit le manda senza virgolette: allora quella dopo SUBJECT.
                            var quoted = System.Text.RegularExpressions.Regex.Match(rest, "\"([^\"]*)\"");
                            var text = quoted.Success ? quoted.Groups[1].Value
                                     : System.Text.RegularExpressions.Regex.Match(rest, @"SUBJECT\s+(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
                            bool Matches(Message m)
                            {
                                if (rest.Contains("UNDELETED") && m.Has("\\Deleted")) return false;
                                if (rest.Contains("UNSEEN") && m.Has("\\Seen")) return false;
                                if (text.Length == 0) return true;
                                var mime = MimeMessage.Load(new MemoryStream(m.Raw));
                                return (mime.Subject + " " + mime.From + " " + mime.To + " " + mime.HtmlBody + " " + mime.TextBody).Contains(text, StringComparison.OrdinalIgnoreCase);
                            }
                            await Send($"* SEARCH {string.Join(' ', Current().Where(Matches).Select(m => m.Uid))}\n{tag} OK fatto\n");
                            break;
                        case "UID STORE":
                            var stored = Current().Where(m => Uids(words[2]).Contains(m.Uid)).ToList();
                            var list = rest.Contains('(') ? rest[(rest.IndexOf('(') + 1)..rest.LastIndexOf(')')] : words[^1];
                            var items = System.Text.RegularExpressions.Regex.Matches(list, "\"((?:[^\"\\\\]|\\\\.)*)\"|[^\\s\"]+")
                                .Select(x => x.Groups[1].Success ? x.Groups[1].Value.Replace("\\\\", "\\") : x.Value).ToList();
                            bool add = words[3].StartsWith('+');
                            if (rest.Contains("X-GM-LABELS", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (var m in stored)
                                    foreach (var label in items)
                                        if (add) { if (!m.Labels.Contains(label)) m.Labels.Add(label); }
                                        else m.Labels.Remove(label);
                            }
                            else foreach (var m in stored) foreach (var flag in items) m.Set(flag, add);
                            if (EchoToIdlers && selected == "INBOX") foreach (var m in stored) Push($"* {Current().IndexOf(m) + 1} FETCH (UID {m.Uid} FLAGS ({m.Flags}))");
                            await Send($"{tag} OK fatto\n");
                            break;
                        case "UID EXPUNGE" when !Has("UIDPLUS"):
                        case "UID MOVE" when !Has("MOVE"):
                            await Send($"{tag} BAD comando sconosciuto\n");
                            break;
                        case "UID EXPUNGE":
                            foreach (var m in Current().Where(m => Uids(words[2]).Contains(m.Uid) && m.Has("\\Deleted")).ToList())
                            {
                                await Send($"* {Current().IndexOf(m) + 1} EXPUNGE\n");
                                Current().Remove(m);
                            }
                            await Send($"{tag} OK fatto\n");
                            break;
                        case "EXPUNGE":
                            foreach (var m in Current().Where(m => m.Has("\\Deleted")).ToList())
                            {
                                await Send($"* {Current().IndexOf(m) + 1} EXPUNGE\n");
                                Current().Remove(m);
                            }
                            await Send($"{tag} OK fatto\n");
                            break;
                        case "SEARCH" when rest.Contains(" UID ", StringComparison.OrdinalIgnoreCase):
                            // Senza UIDPLUS MailKit copia per numero d'ordine: prima chiede i numeri degli UID.
                            var byUid = Uids(words[^1]);
                            await Send($"* SEARCH {string.Join(' ', Current().Select((m, k) => (m, k)).Where(x => byUid.Contains(x.m.Uid)).Select(x => x.k + 1))}\n{tag} OK fatto\n");
                            break;
                        case "UID MOVE" or "UID COPY" or "COPY":
                            // Nella cartella di arrivo con UID nuovi (e lo stesso X-GM-MSGID), detti con COPYUID se il server ha
                            // UIDPLUS; copiare in Gmail è anche aggiungere l'etichetta della cartella. COPY per numero d'ordine.
                            var set = command == "COPY" ? words[1] : words[2];
                            var targetName = Mailbox(rest[(rest.IndexOf(set, StringComparison.Ordinal) + set.Length + 1)..]);
                            var from = Current();
                            var picked = command == "COPY"
                                ? Uids(set).Where(n => n >= 1 && n <= from.Count).Select(n => from[(int)n - 1]).ToList()
                                : from.Where(m => Uids(set).Contains(m.Uid)).ToList();
                            var copies = picked.Select(m => new Message(nextUid++, m.MsgId, m.Flags, m.Raw)).ToList();
                            for (int k = 0; k < picked.Count; k++) copies[k].Labels.AddRange(picked[k].Labels);
                            Folders[targetName].AddRange(copies);
                            var copyUid = Has("UIDPLUS") ? $"[COPYUID 1 {string.Join(',', picked.Select(m => m.Uid))} {string.Join(',', copies.Select(m => m.Uid))}] " : "";
                            if (command != "UID MOVE")
                            {
                                if (Kind.Gmail) foreach (var m in picked.Where(m => !m.Labels.Contains(targetName))) m.Labels.Add(targetName);
                                if (DropAfterCopy)
                                {
                                    DropAfterCopy = false;
                                    return; // copiati, poi la connessione si chiude senza la risposta
                                }
                                await Send($"{tag} OK {copyUid}copiati\n");
                                break;
                            }
                            await Send($"* OK {copyUid}spostati\n");
                            foreach (var m in picked.OrderByDescending(from.IndexOf).ToList())
                            {
                                await Send($"* {from.IndexOf(m) + 1} EXPUNGE\n");
                                if (EchoToIdlers && selected == "INBOX") Push($"* {from.IndexOf(m) + 1} EXPUNGE");
                                from.Remove(m);
                            }
                            await Send($"{tag} OK fatto\n");
                            break;
                        case "IDLE" when !Has("IDLE"):
                            await Send($"{tag} BAD comando sconosciuto\n");
                            break;
                        case "IDLE":
                            // Fino al DONE: intanto il server può mandare EXISTS, FETCH ed EXPUNGE (Push); un comando qualsiasi
                            // durante l'IDLE è un errore del client, e si ricorda.
                            await Send("+ in attesa\n");
                            lock (idlers) { idlers.Add(stream); idleConnection[stream] = connection; IdleLog.Add("IDLE"); }
                            string? next;
                            while ((next = await ReadLineAsync(stream)) != null && !next.Equals("DONE", StringComparison.OrdinalIgnoreCase))
                                lock (idlers) IdleLog.Add("durante l'IDLE: " + next);
                            lock (idlers) { idlers.Remove(stream); idleConnection.Remove(stream); }
                            if (next == null) return;
                            lock (idlers) IdleLog.Add(IsMuted(connection) ? "DONE (connessione muta, nessuna risposta)" : "DONE");
                            if (LogCommands) Log.Write($"server IMAP finto: DONE{(IsMuted(connection) ? $" (connessione {connection} muta, nessuna risposta)" : "")}");
                            if (!IsMuted(connection)) await Send($"{tag} OK IDLE finito\n");
                            break;
                        case "NOOP" when AnnounceOnNoop && Current().Count > announced:
                            // Come un server vero: i messaggi arrivati dopo l'apertura della cartella si annunciano con NOOP.
                            announced = Current().Count;
                            await Send($"* {announced} EXISTS\n{tag} OK fatto\n");
                            break;
                        case "LOGOUT":
                            // SilentLogout: un server che non risponde più (rete caduta a metà): la connessione resta aperta e muta.
                            if (SilentLogout) await Task.Delay(TimeSpan.FromSeconds(90));
                            await Send($"* BYE\n{tag} OK fatto\n");
                            return;
                        case "NOOP":
                            await Send($"{tag} OK fatto\n");
                            break;
                        default:
                            await Send($"{tag} BAD comando sconosciuto\n");
                            break;
                    }
                }
            }
            catch (IOException) { }
            catch (Exception ex)
            {
                // Un difetto di questo server, non del client: detto e scritto, invece di una connessione che cade e basta.
                lock (ServerErrors) ServerErrors.Add($"IMAP, dopo \"{(Commands.Count > 0 ? Commands[^1].Split(' ')[0] : "")}\": {ex.GetType().Name}: {ex.Message}");
                try { await Send("* BYE errore del server finto\n"); } catch (Exception) { }
            }
            finally
            {
                lock (open) open.Remove(connection);
                // Una connessione chiusa dal client in mezzo all'IDLE (IOException, non la fine del flusso) non è più in IDLE.
                lock (idlers)
                {
                    idlers.Remove(stream);
                    idleConnection.Remove(stream);
                }
            }
        }

        static HashSet<uint> Uids(string set) =>
            set.Split(',').SelectMany(p => p.Split(':') is [var a, var b] ? Range(int.Parse(a), b == "*" ? 100000 : int.Parse(b)) : [uint.Parse(p)]).ToHashSet();

        /// <summary>Un intervallo di IMAP, anche scritto al contrario (3:1 è 1:3).</summary>
        static IEnumerable<uint> Range(int a, int b) => Enumerable.Range(Math.Min(a, b), Math.Abs(b - a) + 1).Select(i => (uint)i);

        /// <summary>La data di arrivo come la scrive IMAP: 26-Sep-2026 10:00:00 +0200.</summary>
        static string InternalDate(DateTimeOffset d) =>
            d.ToString("dd-MMM-yyyy HH:mm:ss ", System.Globalization.CultureInfo.InvariantCulture) + (d.Offset < TimeSpan.Zero ? "-" : "+") + d.Offset.ToString("hhmm");

        /// <summary>Una lista di indirizzi della busta (ENVELOPE): ((nome NIL casella dominio) ...) o NIL.</summary>
        static string Addresses(InternetAddressList list)
        {
            var boxes = list.Mailboxes.ToList();
            if (boxes.Count == 0) return "NIL";
            static string Quote(string? s) => s == null ? "NIL" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            return "(" + string.Concat(boxes.Select(b => $"({Quote(string.IsNullOrEmpty(b.Name) ? null : b.Name)} NIL {Quote(b.LocalPart)} {Quote(b.Domain)})")) + ")";
        }

        /// <summary>
        /// La struttura con i tipi veri degli allegati e con le immagini nel testo (cid:) come parti inline: solo per le
        /// caselle delle foto (ProvaBozze.Foto.cs). Di base ogni allegato è application/octet-stream e le immagini nel testo
        /// non ci sono, come le prove si aspettano.
        /// </summary>
        public bool TrueTypes;

        /// <summary>Le parti dopo il testo, nell'ordine della struttura (dalla 2): gli allegati, e con TrueTypes anche le immagini nel testo.</summary>
        List<MimePart> Files(MimeMessage mime) => TrueTypes
            ? mime.BodyParts.OfType<MimePart>().Where(p => p.IsAttachment || p.ContentId != null).ToList()
            : mime.Attachments.OfType<MimePart>().ToList();

        /// <summary>La risposta a FETCH: busta e struttura, il messaggio intero (BODY.PEEK[]) o le parti chieste (BODY.PEEK[2], BODY.PEEK[2.MIME]).</summary>
        byte[] Fetch(Message m, int seq, string request)
        {
            var mime = MimeMessage.Load(new MemoryStream(m.Raw));
            var output = new MemoryStream();
            void Write(string s) => output.Write(Encoding.UTF8.GetBytes(s));
            Write($"* {seq} FETCH (UID {m.Uid}");
            if (request.Contains("FLAGS")) Write($" FLAGS ({m.Flags})");
            if (request.Contains("INTERNALDATE")) Write($" INTERNALDATE \"{InternalDate(mime.Date)}\"");
            if (request.Contains("RFC822.SIZE")) Write($" RFC822.SIZE {m.Raw.Length}");
            if (request.Contains("ENVELOPE"))
                Write($" ENVELOPE (\"Sat, 26 Sep 2026 10:00:00 +0200\" \"{mime.Subject?.Replace("\"", "'")}\" {Addresses(mime.From)} {Addresses(mime.Sender != null ? new InternetAddressList { mime.Sender } : mime.From)} " +
                      $"{Addresses(mime.ReplyTo.Count > 0 ? mime.ReplyTo : mime.From)} {Addresses(mime.To)} {Addresses(mime.Cc)} {Addresses(mime.Bcc)} NIL \"<{mime.MessageId}>\")");
            if (request.Contains("BODYSTRUCTURE"))
            {
                var files = Files(mime).Select(a => !TrueTypes
                    ? $"(\"application\" \"octet-stream\" (\"name\" \"{a.FileName}\") NIL NIL \"base64\" {a.Content?.Stream?.Length ?? 0} NIL (\"attachment\" (\"filename\" \"{a.FileName}\")) NIL NIL)"
                    : $"(\"{a.ContentType.MediaType}\" \"{a.ContentType.MediaSubtype}\" (\"name\" \"{a.FileName ?? a.ContentType.Name}\") {(a.ContentId != null ? $"\"<{a.ContentId}>\"" : "NIL")} NIL " +
                      $"\"{(a.ContentTransferEncoding == ContentEncoding.Base64 ? "base64" : "7bit")}\" {a.Content?.Stream?.Length ?? 0}{(a.ContentType.MediaType == "text" ? " 1" : "")} NIL " +
                      $"(\"{(a.IsAttachment ? "attachment" : "inline")}\" (\"filename\" \"{a.FileName ?? a.ContentType.Name}\")) NIL NIL)");
                // Il testo con la sua misura vera, come un server vero (prima sempre 10): ImapProvider.Prefetchable la somma per
                // decidere se un messaggio grande si copia. È la misura di quello che BODY.PEEK[1] restituisce qui sotto.
                Write($" BODYSTRUCTURE ((\"text\" \"html\" (\"charset\" \"utf-8\") NIL NIL \"7bit\" {Encoding.UTF8.GetByteCount(mime.HtmlBody ?? mime.TextBody ?? "")} 1 NIL NIL NIL NIL)" +
                      string.Concat(files) + " \"mixed\" (\"boundary\" \"x\") NIL NIL NIL)");
            }
            if (request.Contains("X-GM-MSGID")) Write($" X-GM-MSGID {m.MsgId}");
            if (request.Contains("X-GM-THRID")) Write($" X-GM-THRID {m.MsgId}");
            if (request.Contains("X-GM-LABELS")) Write($" X-GM-LABELS ({string.Join(' ', m.Labels.Select(l => "\"" + l.Replace("\\", "\\\\") + "\""))})");
            // Il messaggio intero, come lo chiede GetMessageAsync di MailKit, o solo un pezzo (BODY.PEEK[]<inizio.quanti>).
            if (request.Contains("BODY.PEEK[]"))
            {
                var piece = System.Text.RegularExpressions.Regex.Match(request, @"BODY\.PEEK\[\]<(\d+)\.(\d+)>");
                var bytes = piece.Success ? m.Raw.Skip(int.Parse(piece.Groups[1].Value)).Take(int.Parse(piece.Groups[2].Value)).ToArray() : m.Raw;
                Write($" BODY[]{(piece.Success ? $"<{piece.Groups[1].Value}>" : "")} {{{bytes.Length}}}\r\n");
                output.Write(bytes);
            }
            // Le intestazioni chieste per nome (BODY.PEEK[HEADER.FIELDS (...)]), com'erano nel messaggio.
            var fields = System.Text.RegularExpressions.Regex.Match(request, @"BODY\.PEEK\[HEADER\.FIELDS \(([^)]*)\)\]");
            if (fields.Success)
            {
                var names = fields.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var head = new MemoryStream();
                foreach (var header in mime.Headers.Where(h => names.Contains(h.Field, StringComparer.OrdinalIgnoreCase)))
                {
                    head.Write(Encoding.ASCII.GetBytes(header.Field + ":"));
                    head.Write(header.RawValue);
                }
                head.Write("\r\n"u8);
                Write($" BODY[HEADER.FIELDS ({fields.Groups[1].Value})] {{{head.Length}}}\r\n");
                output.Write(head.ToArray());
            }
            // Le parti chieste per numero: la seconda figlia del multipart/mixed è il primo allegato, e così via.
            foreach (var section in System.Text.RegularExpressions.Regex.Matches(request, @"BODY\.PEEK\[(\d+)(\.MIME)?\](?:<(\d+)\.(\d+)>)?").Cast<System.Text.RegularExpressions.Match>())
            {
                var index = int.Parse(section.Groups[1].Value) - 2;
                if (index < 0 && section.Groups[2].Success)
                {
                    // BODY.PEEK[1.MIME]: le intestazioni della parte del testo, che GetBodyPartAsync chiede insieme alla parte (un
                    // messaggio grande, letto parte per parte). Sempre, come un server vero (fino al blocco 6 dell'audit non
                    // rispondeva, e quella lettura nelle prove non riusciva mai). Le intestazioni sono quelle che questo server
                    // dichiara nella struttura (text/html, utf-8, senza codifica), perché BODY.PEEK[1] qui sotto dà il testo
                    // già decodificato: con quelle vere della parte un testo quoted-printable verrebbe decodificato due volte.
                    var textHead = "Content-Type: text/html; charset=utf-8\r\nContent-Transfer-Encoding: 8bit\r\n\r\n"u8;
                    Write($" BODY[1.MIME] {{{textHead.Length}}}\r\n");
                    output.Write(textHead);
                    continue;
                }
                if (index < 0)
                {
                    // La parte 1 è il testo: MailKit ne chiede l'inizio per l'anteprima (BODY.PEEK[1]<0.N>).
                    var textBytes = Encoding.UTF8.GetBytes(mime.HtmlBody ?? mime.TextBody ?? "");
                    if (section.Groups[4].Success) textBytes = textBytes.Skip(int.Parse(section.Groups[3].Value)).Take(int.Parse(section.Groups[4].Value)).ToArray();
                    Write($" BODY[1]{(section.Groups[3].Success ? $"<{section.Groups[3].Value}>" : "")} {{{textBytes.Length}}}\r\n");
                    output.Write(textBytes);
                    continue;
                }
                var part = Files(mime)[index]; // gli stessi della struttura: un .eml allegato non è fra questi
                var data = new MemoryStream();
                if (section.Groups[2].Success)
                {
                    part.Headers.WriteTo(data); // con la riga vuota che chiude le intestazioni
                }
                else part.Content?.WriteTo(data); // un allegato senza contenuto: zero byte
                Write($" BODY[{section.Groups[1].Value}{section.Groups[2].Value}] {{{data.Length}}}\r\n");
                output.Write(data.ToArray());
            }
            Write(")\r\n");
            return output.ToArray();
        }
    }

    static async Task<string?> ReadLineAsync(Stream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one) == 1)
        {
            if (one[0] == '\n') return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.Add(one[0]);
        }
        return null;
    }

    /// <summary>Un certificato fatto al volo per 127.0.0.1, che Windows non riconosce: quello dei server finti con TLS.</summary>
    static X509Certificate2 TestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var made = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        // SChannel vuole la chiave in un contenitore: passando da PFX c'è.
        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx), null);
    }

    /// <summary>
    /// Un server SMTP minimo con STARTTLS, come la porta 587 dei provider: prima di STARTTLS non offre AUTH e lo rifiuta
    /// (530); dopo, AUTH PLAIN, MAIL, RCPT e DATA. Nel registro dei comandi niente credenziali.
    /// </summary>
    sealed class FakeSmtp : IDisposable
    {
        public readonly List<string> Commands = new();
        public readonly List<byte[]> Received = new();
        public string? Password;
        /// <summary>Se c'è, l'unica password accettata: le altre ricevono 535, come un server vero.</summary>
        public string? ExpectedPassword;
        /// <summary>Un AUTH arrivato prima di STARTTLS, cioè in chiaro.</summary>
        public bool AuthBeforeTls;
        /// <summary>Ai prossimi N DATA il server riceve tutto il messaggio (lo tiene: Received) e la connessione cade senza la risposta.</summary>
        public int DropAfterData;
        /// <summary>Il prossimo comando con questo nome (per esempio "MAIL") fa cadere la connessione, senza risposta.</summary>
        public string? DropOnce;
        /// <summary>Al prossimo DATA, ricevuto il messaggio, il server lo rifiuta con questa risposta (per esempio "554 5.7.1 rifiutato") e non lo tiene.</summary>
        public string? RefuseData;
        public readonly X509Certificate2 Certificate = TestCertificate();
        readonly TcpListener listener;

        public FakeSmtp(int port = 0)
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public void Dispose() => listener.Stop();

        async Task AcceptAsync()
        {
            try
            {
                while (true) _ = ServeAsync(await listener.AcceptTcpClientAsync());
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        async Task ServeAsync(TcpClient client)
        {
            using var _ = client;
            Stream stream = client.GetStream();
            bool tls = false;
            async Task Send(string text) => await stream.WriteAsync(Encoding.ASCII.GetBytes(text.Replace("\n", "\r\n")));
            try
            {
                await Send("220 server finto ESMTP\n");
                while (await ReadLineAsync(stream) is { } line)
                {
                    var verb = line.Split(' ')[0].ToUpperInvariant();
                    Commands.Add(verb == "AUTH" ? "AUTH " + line.Split(' ').ElementAtOrDefault(1) + (tls ? "" : " (in chiaro)") : line);
                    if (DropOnce == verb)
                    {
                        DropOnce = null;
                        return;
                    }
                    switch (verb)
                    {
                        case "EHLO" or "HELO":
                            await Send(tls ? "250-finto\n250-AUTH PLAIN LOGIN\n250-DSN\n250 8BITMIME\n" : "250-finto\n250-STARTTLS\n250 8BITMIME\n");
                            break;
                        case "STARTTLS":
                            await Send("220 via\n");
                            var secure = new SslStream(stream);
                            await secure.AuthenticateAsServerAsync(Certificate);
                            stream = secure;
                            tls = true;
                            break;
                        case "AUTH":
                            if (!tls)
                            {
                                AuthBeforeTls = true;
                                await Send("530 prima STARTTLS\n");
                                break;
                            }
                            var payload = line.Split(' ').ElementAtOrDefault(2);
                            if (payload == null)
                            {
                                await Send("334 \n");
                                payload = await ReadLineAsync(stream);
                            }
                            Password = Encoding.UTF8.GetString(Convert.FromBase64String(payload ?? "")).Split('\0').Last();
                            await Send(ExpectedPassword != null && Password != ExpectedPassword ? "535 5.7.8 credenziali non valide (prova)\n" : "235 entrato\n");
                            break;
                        case "DATA":
                            await Send("354 avanti\n");
                            var data = new MemoryStream();
                            while (await ReadLineAsync(stream) is { } row && row != ".")
                            {
                                data.Write(Encoding.UTF8.GetBytes((row.StartsWith("..") ? row[1..] : row) + "\r\n"));
                            }
                            if (RefuseData is { } refusal)
                            {
                                RefuseData = null;
                                await Send(refusal + "\n");
                                break;
                            }
                            Received.Add(data.ToArray());
                            if (DropAfterData > 0)
                            {
                                DropAfterData--;
                                return;
                            }
                            await Send("250 ricevuto\n");
                            break;
                        case "QUIT":
                            await Send("221 ciao\n");
                            return;
                        default:
                            await Send("250 ok\n");
                            break;
                    }
                }
            }
            catch (IOException) { }
            catch (System.Security.Authentication.AuthenticationException) { }
            catch (Exception ex)
            {
                // Come per il server IMAP finto (C-45): un difetto di questo server resta scritto, invece di una connessione che cade e basta.
                lock (ServerErrors) ServerErrors.Add($"SMTP, dopo \"{(Commands.Count > 0 ? Commands[^1].Split(' ')[0] : "")}\": {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Un server che parla solo TLS, con il certificato fatto al volo: un client che controlla il certificato lo rifiuta.</summary>
    sealed class FakeTls : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly X509Certificate2 certificate = TestCertificate();

        public FakeTls()
        {
            listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public void Dispose() => listener.Stop();

        async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            try { await new SslStream(client.GetStream()).AuthenticateAsServerAsync(certificate); }
                            catch (Exception) { } // il client chiude: è quello che si vuole
                        }
                    });
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
    }

    /// <summary>
    /// Solo in Debug: Missiva.exe --imap-finto porta tiene aperto un server IMAP finto (una variante senza Gmail, con
    /// SPECIAL-USE, MOVE e UIDPLUS) con qualche messaggio di prova, per provare un account IMAP nella versione di prova
    /// senza toccare nessuna casella vera. Si chiude chiudendo il processo.
    /// </summary>
    /// <param name="noTrash">--imap-finto porta senza-cestino: il server non ha il Cestino (lo crea Missiva al primo bisogno).</param>
    /// <param name="password">
    /// --imap-finto porta password=... : IMAP e SMTP accettano solo quella (Gestisci account, Cambia password). Il server
    /// SMTP finto (STARTTLS) sta sulla porta dopo; la versione di prova con MISSIVA_IMAP_FINTO=1 ne accetta il certificato.
    /// </param>
    /// <param name="old">--imap-finto porta vecchi: i messaggi di prova sono di qualche giorno fa (l'elenco parte senza il gruppo "Oggi").</param>
    /// <param name="photo">
    /// --imap-finto porta foto studio|personale|pec: al posto dei messaggi di prova una delle caselle finte delle foto della
    /// pagina GitHub (ProvaBozze.Foto.cs).
    /// </param>
    /// <param name="photoEnglish">--imap-finto porta foto studio en: la casella delle foto inglesi.</param>
    /// <param name="fill">
    /// --imap-finto porta riempi=120: prima dei messaggi di prova tanti messaggi letti dei giorni prima (uno ogni due ore, i più
    /// vecchi con l'UID più basso, come in una casella vera), per un elenco di più pagine (correzioni dell'audit di
    /// affidabilità del 25 ottobre 2026, elenco: E-12).
    /// </param>
    public static void ServeImap(int port, bool noTrash = false, string? password = null, bool old = false, string? photo = null, bool photoEnglish = false, int fill = 0)
    {
        var fake = new FakeImap(new Flavor(Gmail: false), port) { LogCommands = true, ExpectedPassword = password, EchoToIdlers = true, AnnounceOnNoop = true };
        var smtp = new FakeSmtp(port + 1) { ExpectedPassword = password };
        GC.KeepAlive(smtp);
        if (noTrash) fake.Folders.Remove(fake.TrashFolder);
        var oldest = DateTimeOffset.Now.AddHours(-2 * fill - 48);
        for (int k = 0; k < fill; k++)
        {
            var mime = new MimeMessage { Subject = $"Pratica {k + 1} di prova", Date = oldest.AddHours(2 * k), Body = new TextPart("html") { Text = $"<p>Pratica {k + 1} di prova.</p>" } };
            mime.From.Add(new MailboxAddress("Luca Bianchi", "luca.bianchi@cliente-prova.example"));
            mime.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.example"));
            fake.Add("INBOX", mime, "\\Seen");
        }
        if (photo != null) FillFoto(fake, photo, photoEnglish);
        else foreach (var (from, name, subject, html, flags) in new[]
        {
            ("preventivi@fornitore-prova.it", "Fornitore di prova", "Preventivo per la ristrutturazione dello studio", "<p>Buongiorno,</p><p>in allegato trova il preventivo richiesto.</p><p>Cordiali saluti</p>", ""),
            ("segreteria@ordine-prova.it", "Segreteria dell'Ordine", "Convocazione dell'assemblea di ottobre", "<p>Gentile iscritto,</p><p>l'assemblea si terrà il 15 ottobre alle 17.</p>", "\\Seen"),
            ("mario.bianchi@cliente-prova.it", "Mario Bianchi", "Documenti per la pratica", "<p>Ciao,</p><p>ti mando i documenti che mancavano.</p><p>Mario</p>", "\\Seen \\Flagged"),
        })
        {
            var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now.AddHours(-(fake.Folders["INBOX"].Count - fill) * 5 - 1).AddDays(old ? -3 : 0), Body = new TextPart("html") { Text = html } };
            mime.From.Add(new MailboxAddress(name, from));
            mime.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.it"));
            fake.Add("INBOX", mime, flags);
        }
        // Una newsletter con List-Unsubscribe (un clic, autenticata dal server della casella), per provare "Annulla iscrizione":
        // indirizzi .example, che non esistono; nella versione di prova la POST va comunque solo nel registro.
        if (photo == null)
        {
            var news = new MimeMessage { Subject = "Notizie della settimana", Date = DateTimeOffset.Now.AddHours(-20).AddDays(old ? -3 : 0), Body = new TextPart("html") { Text = "<p>Gentile iscritto,</p><p>ecco le notizie della settimana.</p>" } };
            news.From.Add(new MailboxAddress("Notizie di prova", "notizie@notizie-prova.example"));
            news.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.it"));
            news.Headers.Add("Authentication-Results", "studio-prova.it; dkim=pass header.i=@notizie-prova.example; dmarc=pass header.from=notizie-prova.example");
            news.Headers.Add("List-Unsubscribe", "<https://liste.notizie-prova.example/via?id=123>, <mailto:via@notizie-prova.example?subject=unsubscribe>");
            news.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
            fake.Add("INBOX", news, "\\Seen");
        }
        Log.Write($"server IMAP finto sulla porta {fake.Port}, SMTP finto sulla porta {smtp.Port}{(password != null ? ", con una password sola" : "")}");
        // Mentre gira, le righe scritte in comandi.txt nella sua cartella dei dati (MISSIVA_DATA): "arrivo Oggetto" mette un
        // messaggio da leggere nella Posta in arrivo e lo annuncia a chi è in IDLE (EXISTS), come un arrivo vero; "letto 7"
        // segna letto l'UID 7 e lo dice a chi è in IDLE (FETCH con i segni), come una lettura dal telefono; "rifiuta UID MOVE"
        // fa rispondere NO al prossimo comando con quel nome (RefuseOnce), per vedere Elimina o Sposta rifiutati dal server.
        // Dall'audit di affidabilità dell'arrivo (25 ottobre 2026): "muto" rende mute le connessioni aperte (leggono e non
        // rispondono, senza chiudersi: come dopo la ripresa del PC), "muto idle" solo quelle in IDLE, "muto tutto" anche le
        // nuove, "parla" fa rispondere di nuovo le nuove, "taglia" chiude quelle in IDLE; "arrivo-lento 5 Oggetto" annuncia
        // il messaggio all'IDLE subito e lo rende visibile 5 s dopo (come Gmail), "arrivo-datato 300 Oggetto" ne mette uno con
        // la data di arrivo di 300 s fa (strumenti\arrivo\finestra.ps1).
        var commands = Path.Combine(Store.Dir, "comandi.txt");
        while (true)
        {
            Thread.Sleep(200);
            GC.KeepAlive(smtp);
            string[] lines;
            try
            {
                if (!File.Exists(commands)) continue;
                lines = File.ReadAllLines(commands);
                File.Delete(commands);
            }
            catch (IOException) { continue; } // lo script lo sta ancora scrivendo
            foreach (var line in lines.Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                var inbox = fake.Folders["INBOX"];
                if (line.StartsWith("arrivo", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("arrivo-", StringComparison.OrdinalIgnoreCase))
                {
                    var subject = line.Length > 7 ? line[7..] : "Messaggio di prova appena arrivato";
                    var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now, Body = new TextPart("html") { Text = $"<p>Buongiorno,</p><p>{subject}.</p><p>Maria</p>" } };
                    mime.From.Add(new MailboxAddress("Maria Rossi", "maria.rossi@cliente-prova.example"));
                    mime.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.it"));
                    var added = fake.Add("INBOX", mime);
                    fake.Push($"* {inbox.Count} EXISTS");
                    Log.Write($"server IMAP finto: arrivato l'UID {added.Uid}, {inbox.Count} messaggi nella Posta in arrivo");
                }
                else if (line.StartsWith("letto ", StringComparison.OrdinalIgnoreCase) && uint.TryParse(line[6..], out var uid) && inbox.FirstOrDefault(m => m.Uid == uid) is { } read)
                {
                    read.Set("\\Seen", true);
                    fake.Push($"* {inbox.IndexOf(read) + 1} FETCH (UID {uid} FLAGS ({read.Flags}))");
                    Log.Write($"server IMAP finto: l'UID {uid} letto altrove");
                }
                else if (line.StartsWith("rifiuta ", StringComparison.OrdinalIgnoreCase) && line[8..].Trim() is { Length: > 0 } refused)
                {
                    fake.RefuseOnce = refused.ToUpperInvariant();
                    Log.Write($"server IMAP finto: il prossimo {fake.RefuseOnce} riceve NO");
                }
                // Audit di affidabilità, arrivo (25 ottobre 2026): connessioni che muoiono senza chiudersi ("muto", "muto idle",
                // "muto tutto", "parla", "taglia") e arrivi visibili tardi o con una data di arrivo (INTERNALDATE, qui la data del
                // messaggio) nel passato ("arrivo-lento N", "arrivo-datato N").
                else if (line.Equals("muto idle", StringComparison.OrdinalIgnoreCase))
                {
                    var idling = fake.IdleConnections();
                    fake.MuteIdlers();
                    Log.Write($"server IMAP finto: connessioni in IDLE mute ({string.Join(", ", idling)}), le altre e le nuove rispondono");
                }
                else if (line.Equals("muto tutto", StringComparison.OrdinalIgnoreCase))
                {
                    fake.MuteOpen();
                    fake.MuteNew = true;
                    Log.Write($"server IMAP finto: tutte le connessioni mute, anche le nuove (fino a {fake.ConnectionCount})");
                }
                else if (line.Equals("muto", StringComparison.OrdinalIgnoreCase))
                {
                    fake.MuteOpen();
                    Log.Write($"server IMAP finto: connessioni aperte mute (fino alla {fake.ConnectionCount}), le nuove rispondono");
                }
                else if (line.Equals("parla", StringComparison.OrdinalIgnoreCase))
                {
                    fake.MuteNew = false;
                    Log.Write("server IMAP finto: le connessioni nuove rispondono di nuovo");
                }
                else if (line.Equals("taglia", StringComparison.OrdinalIgnoreCase))
                {
                    fake.DropIdlers();
                    Log.Write("server IMAP finto: connessioni in IDLE chiuse");
                }
                else if (line.StartsWith("arrivo-lento ", StringComparison.OrdinalIgnoreCase) && line.Split(' ', 3) is [_, var lag, ..var lagRest] && double.TryParse(lag, System.Globalization.CultureInfo.InvariantCulture, out var lagSeconds))
                {
                    // Come Gmail, che lo annuncia all'IDLE e lo rende visibile alle altre connessioni anche qualche secondo dopo
                    // (App.OnInboxEvent riprova dopo 3 e 10 s): EXISTS subito a chi è in IDLE, il messaggio nella cartella dopo.
                    var subject = lagRest.Length > 0 ? lagRest[0] : "Messaggio di prova visibile in ritardo";
                    var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now, Body = new TextPart("html") { Text = $"<p>Buongiorno,</p><p>{subject}.</p><p>Maria</p>" } };
                    mime.From.Add(new MailboxAddress("Maria Rossi", "maria.rossi@cliente-prova.example"));
                    mime.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.example"));
                    int announcedCount = inbox.Count + 1;
                    fake.Push($"* {announcedCount} EXISTS");
                    Log.Write($"server IMAP finto: annunciato all'IDLE un messaggio nuovo ({announcedCount}), visibile alle altre connessioni fra {lagSeconds} s");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(lagSeconds));
                        var added = fake.Add("INBOX", mime);
                        Log.Write($"server IMAP finto: l'UID {added.Uid} ora è visibile, {inbox.Count} messaggi nella Posta in arrivo");
                    });
                }
                else if (line.StartsWith("arrivo-datato ", StringComparison.OrdinalIgnoreCase) && line.Split(' ', 3) is [_, var ago, ..var rest] && int.TryParse(ago, out var seconds))
                {
                    var subject = rest.Length > 0 ? rest[0] : "Messaggio di prova con la data di prima";
                    var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now.AddSeconds(-seconds), Body = new TextPart("html") { Text = $"<p>Buongiorno,</p><p>{subject}.</p><p>Maria</p>" } };
                    mime.From.Add(new MailboxAddress("Maria Rossi", "maria.rossi@cliente-prova.example"));
                    mime.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.example"));
                    var added = fake.Add("INBOX", mime);
                    fake.Push($"* {inbox.Count} EXISTS");
                    Log.Write($"server IMAP finto: arrivato l'UID {added.Uid} con la data di {seconds} s fa, {inbox.Count} messaggi nella Posta in arrivo");
                }
                // Correzioni dell'audit di affidabilità, elenco (25 ottobre 2026; strumenti\arrivo\elenco.ps1): un arrivo già letto
                // ("arrivo-letto Oggetto", come aperto sul telefono prima del controllo di Missiva), un arrivo in una cartella
                // dell'utente ("arrivo-cartella Oggetto", come un filtro del server), un messaggio tolto senza dirlo a nessuno
                // ("togli 7" o "togli ultimo", come eliminato dal telefono mentre la connessione dell'IDLE era morta) e un server
                // che non si raggiunge ("irraggiungibile": le connessioni nuove si chiudono subito; "raggiungibile"; non "rifiuta ...", che è RefuseOnce).
                else if (line.StartsWith("arrivo-letto", StringComparison.OrdinalIgnoreCase) || line.StartsWith("arrivo-cartella", StringComparison.OrdinalIgnoreCase))
                {
                    bool seen = line.StartsWith("arrivo-letto", StringComparison.OrdinalIgnoreCase);
                    var cut = line.IndexOf(' ');
                    var subject = cut > 0 ? line[(cut + 1)..] : seen ? "Messaggio già letto altrove" : "Messaggio arrivato in una cartella";
                    var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now, Body = new TextPart("html") { Text = $"<p>Buongiorno,</p><p>{subject}.</p><p>Maria</p>" } };
                    mime.From.Add(new MailboxAddress("Maria Rossi", "maria.rossi@cliente-prova.example"));
                    mime.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.example"));
                    var where = seen ? "INBOX" : fake.UserFolder;
                    var added = fake.Add(where, mime, seen ? "\\Seen" : "");
                    if (seen) fake.Push($"* {inbox.Count} EXISTS");
                    Log.Write($"server IMAP finto: arrivato l'UID {added.Uid} {(seen ? "già letto, nella Posta in arrivo" : "in " + where)}, {fake.Folders[where].Count} messaggi lì");
                }
                else if (line.StartsWith("togli ", StringComparison.OrdinalIgnoreCase))
                {
                    var which = line[6..].Trim();
                    var gone = which.Equals("ultimo", StringComparison.OrdinalIgnoreCase) ? inbox.LastOrDefault()
                             : uint.TryParse(which, out var removedUid) ? inbox.FirstOrDefault(m => m.Uid == removedUid) : null;
                    if (gone != null) lock (fake.Folders) inbox.Remove(gone);
                    Log.Write(gone != null ? $"server IMAP finto: l'UID {gone.Uid} tolto dalla Posta in arrivo senza avvisi, {inbox.Count} messaggi" : $"server IMAP finto: niente da togliere ({which})");
                }
                else if (line.Equals("irraggiungibile", StringComparison.OrdinalIgnoreCase) || line.Equals("raggiungibile", StringComparison.OrdinalIgnoreCase))
                {
                    fake.RefuseNew = line.Equals("irraggiungibile", StringComparison.OrdinalIgnoreCase);
                    Log.Write(fake.RefuseNew ? "server IMAP finto: le connessioni nuove si chiudono subito (server che non si raggiunge)" : "server IMAP finto: le connessioni nuove sono di nuovo accettate");
                }
            }
        }
    }
}
#endif
