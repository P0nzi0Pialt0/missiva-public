#if DEBUG
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MimeKit;

namespace Missiva;

/// <summary>Le prove del giro P5 (--prova-bozze): regole.</summary>
static partial class ProvaBozze
{
    /// <summary>
    /// Regole (giro P5): quelle di Outlook con il gestore HTTP finto (senza consenso nessuna richiesta, lettura con un
    /// inoltro esterno, POST con condizioni e azioni, PATCH dei soli campi con le condizioni che Missiva non mostra, DELETE,
    /// nella prova solo il registro) e quelle di Missiva sul server IMAP finto (due messaggi nuovi, uno corrisponde: un solo
    /// UID MOVE, e al controllo dopo nulla), cifrate nelle impostazioni.
    /// </summary>
    static async Task RulesAsync()
    {
        Report.Add("--- Regole: Outlook con il gestore finto, Missiva con il server IMAP finto");
        var requests = new List<(HttpMethod Method, string Path, string Token, string Body)>();
        static JsonObject Person(string address) => new() { ["emailAddress"] = new JsonObject { ["address"] = address, ["name"] = address } };
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add((request.Method, path, request.Headers.Authorization?.Parameter ?? "", request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            JsonNode json = request.Method == HttpMethod.Get && path == "me/mailFolders/inbox/messageRules"
                ? new JsonObject
                {
                    ["value"] = new JsonArray(
                        new JsonObject
                        {
                            ["id"] = "r2", ["displayName"] = "Newsletter", ["sequence"] = 2, ["isEnabled"] = true, ["hasError"] = false, ["isReadOnly"] = false,
                            ["conditions"] = new JsonObject { ["senderContains"] = new JsonArray("newsletter"), ["headerContains"] = new JsonArray("X-Mailer") },
                            ["actions"] = new JsonObject { ["markAsRead"] = true, ["moveToFolder"] = "AAMkNewsletter", ["stopProcessingRules"] = false },
                        },
                        new JsonObject
                        {
                            ["id"] = "r1", ["displayName"] = "Copia", ["sequence"] = 1, ["isEnabled"] = true,
                            ["conditions"] = new JsonObject(), ["actions"] = new JsonObject { ["forwardTo"] = new JsonArray(Person("copia.segreta@gmail.com")) },
                            ["exceptions"] = new JsonObject { ["subjectContains"] = new JsonArray("privato") },
                        }),
                }
                : request.Method == HttpMethod.Post ? new JsonObject { ["id"] = "nuova-regola" } : new JsonObject();
            var text = request.Method == HttpMethod.Patch || request.Method == HttpMethod.Delete ? "" : json.ToJsonString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }));
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.it", Tenant = "organizations" };
        var graph = new GraphProvider(account);
        try
        {
            GraphProvider.TestSettingsConsent = false;
            var none = await graph.GetRulesAsync();
            Check(none == null && requests.Count == 0, "Outlook, senza consenso alle impostazioni della casella: regole null, nessuna richiesta");

            GraphProvider.TestSettingsConsent = true;
            var rules = await graph.GetRulesAsync();
            Check(rules is [{ Name: "Copia", ServerId: "r1" } copy, { Name: "Newsletter" } news] && requests.Single() is { Token: "prova-impostazioni" } get && get.Method == HttpMethod.Get
                  && news.SenderContains is ["newsletter"] && news.MarkRead && news.MoveTo == "AAMkNewsletter" && !news.StopProcessing && news.Hidden.SequenceEqual(["intestazioni"])
                  && copy.ForwardTo is [{ Email: "copia.segreta@gmail.com" }] && copy.Hidden.SequenceEqual(["eccezioni"]),
                  "Outlook: regole lette in ordine (sequence), con il token delle impostazioni della casella; condizioni, azioni e quello che Missiva non mostra");
            var copyRule = rules![0];
            var newsRule = rules[1];
            var internalForward = copyRule.Copy();
            internalForward.ForwardTo = [new Address { Email = "anna@uffici.studio-prova.it" }];
            var personal = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@outlook.it" };
            var toFriend = copyRule.Copy();
            toFriend.ForwardTo = [new Address { Email = "giulia@outlook.it" }];
            Check(Rules.ExternalForwards(copyRule, account) is ["copia.segreta@gmail.com"] && Rules.ExternalForwards(newsRule, account).Count == 0
                  && Rules.ExternalForwards(internalForward, account).Count == 0 && Rules.ExternalForwards(toFriend, personal) is ["giulia@outlook.it"],
                  "inoltro fuori dall'organizzazione riconosciuto (un sottodominio dello studio no; per un account personale ogni altro indirizzo, anche @outlook.it)");
            Check(Rules.Describe(newsRule, id => id == "AAMkNewsletter" ? "Newsletter" : null) ==
                  "Se il mittente è o contiene \"newsletter\" e altre condizioni di Outlook (intestazioni): sposta in Newsletter, segna come letto",
                  "descrizione: " + Rules.Describe(newsRule, id => id == "AAMkNewsletter" ? "Newsletter" : null));
            // Correzioni del giro P5: le condizioni e le eccezioni che Missiva non sa controllare non valgono come assenti.
            var fromNewsletter = new MessageSummary { Id = "7", FolderId = "inbox", From = new Address { Name = "Newsletter", Email = "newsletter@negozio.example" }, Subject = "Saldi" };
            var outlookOnly = Rules.FromGraph(new JsonObject
            {
                ["id"] = "r9", ["displayName"] = "Newsletter per oggetto", ["isEnabled"] = true,
                ["conditions"] = new JsonObject { ["bodyOrSubjectContains"] = new JsonArray("newsletter") }, ["actions"] = new JsonObject { ["delete"] = true, ["permanentDelete"] = false },
            }, account.Id);
            Check(newsRule.HiddenConditions is ["intestazioni"] && copyRule.HiddenConditions is ["eccezioni"] && !outlookOnly.HasConditions && outlookOnly.HiddenConditions is ["oggetto o testo"]
                  && !Rules.Matches(outlookOnly, fromNewsletter, null, account) && !Rules.Matches(newsRule, fromNewsletter, null, account)
                  && !Rules.Matches(copyRule, fromNewsletter, null, account) && outlookOnly.Copy().HiddenConditions is ["oggetto o testo"],
                  "Esegui ora: con condizioni o eccezioni che solo Outlook controlla (oggetto o testo, intestazioni, eccezioni) la regola non corrisponde mai in Missiva");

            requests.Clear();
            var fresh = new MailRule
            {
                AccountId = account.Id, Name = "Fatture Rossi", Sequence = 3, FromAddresses = ["fatture@rossi.example"], SubjectContains = ["fattura"],
                HasAttachments = true, MoveTo = "AAMkFatture", MarkRead = true, StopProcessing = true,
            };
            var id = await graph.SaveRuleAsync(null, fresh);
            var post = requests.Single();
            var body = JsonNode.Parse(post.Body)!.AsObject();
            var conditions = body["conditions"]!.AsObject();
            var actions = body["actions"]!.AsObject();
            Check(post.Method == HttpMethod.Post && post.Path == "me/mailFolders/inbox/messageRules" && post.Token == "prova-impostazioni" && id == "nuova-regola"
                  && body["displayName"]!.GetValue<string>() == "Fatture Rossi" && body["sequence"]!.GetValue<int>() == 3 && body["isEnabled"]!.GetValue<bool>()
                  && conditions.Count == 3 && conditions["fromAddresses"]![0]!["emailAddress"]!["address"]!.GetValue<string>() == "fatture@rossi.example"
                  && conditions["subjectContains"]![0]!.GetValue<string>() == "fattura" && conditions["hasAttachments"]!.GetValue<bool>()
                  && actions.Count == 3 && actions["moveToFolder"]!.GetValue<string>() == "AAMkFatture" && actions["markAsRead"]!.GetValue<bool>()
                  && actions["stopProcessingRules"]!.GetValue<bool>(),
                  $"POST della regola nuova con conditions e actions giusti, moveToFolder con l'id della cartella: {post.Body}");

            requests.Clear();
            var off = newsRule.Copy();
            off.Enabled = false;
            await graph.SaveRuleAsync(newsRule, off);
            var patch = requests.Single();
            Check(patch.Method == HttpMethod.Patch && patch.Path == "me/mailFolders/inbox/messageRules/r2" && JsonNode.Parse(patch.Body) is JsonObject { Count: 1 } p
                  && p["isEnabled"]!.GetValue<bool>() == false,
                  $"Spegni: PATCH del solo isEnabled ({patch.Body})");
            requests.Clear();
            var changed = newsRule.Copy();
            changed.SubjectContains = ["offerta"];
            changed.MarkRead = false;
            await graph.SaveRuleAsync(newsRule, changed);
            var p2 = JsonNode.Parse(requests.Single().Body)!.AsObject();
            Check(p2.Count == 2 && p2["conditions"]!["subjectContains"]![0]!.GetValue<string>() == "offerta" && p2["conditions"]!["senderContains"]![0]!.GetValue<string>() == "newsletter"
                  && p2["conditions"]!["headerContains"]![0]!.GetValue<string>() == "X-Mailer" && p2["actions"]!["markAsRead"]!.GetValue<bool>() == false
                  && p2["actions"]!["moveToFolder"]!.GetValue<string>() == "AAMkNewsletter",
                  $"Modifica: PATCH di condizioni e azioni soltanto, con le condizioni di Outlook che Missiva non mostra ({requests.Single().Body})");
            requests.Clear();
            await graph.SaveRuleAsync(newsRule, newsRule.Copy());
            Check(requests.Count == 0, "Salva senza modifiche: nessuna richiesta");
            await graph.DeleteRuleAsync(newsRule);
            Check(requests.Single() is { Path: "me/mailFolders/inbox/messageRules/r2" } delete && delete.Method == HttpMethod.Delete, "Elimina: DELETE della regola");

            requests.Clear();
            var testing = new TestingProvider(graph);
            await testing.SaveRuleAsync(null, fresh);
            await testing.SaveRuleAsync(newsRule, off);
            await testing.DeleteRuleAsync(newsRule);
            var logged = File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).TakeLast(5).ToList();
            Check(requests.Count == 0 && logged.Any(l => l.Contains("prova: creerei la regola \"Fatture Rossi\" in mario.rossi@studio-prova.it"))
                  && logged.Any(l => l.Contains("prova: modificherei la regola \"Newsletter\"") && l.Contains("spenta"))
                  && logged.Any(l => l.Contains("prova: eliminerei la regola \"Newsletter\"")),
                  "nella prova creare, spegnere ed eliminare vanno solo nel registro, nessuna richiesta");
        }
        finally
        {
            GraphProvider.TestSettingsConsent = false;
        }

        // Regole di Missiva su un account IMAP: due messaggi nuovi, uno corrisponde.
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var imapAccount = new MailAccount
        {
            Kind = AccountKind.Imap, Email = "mario@studio-prova.it",
            Imap = new ImapServer
            {
                ImapHost = "127.0.0.1", ImapPort = fake.Port, ImapSecurity = MailSecurity.Ssl, SmtpHost = "127.0.0.1", SmtpPort = 1,
                UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect("Password-di-prova"),
            },
        };
        var seenBefore = Store.Settings.RulesSeen.Count;
        try
        {
            using var imap = new ImapProvider(imapAccount);
            var invoice = new MimeMessage { Subject = "Fattura n. 12 di settembre", Date = DateTimeOffset.Now.AddMinutes(-2), Body = new TextPart("html") { Text = "<p>In allegato la fattura</p>" } };
            invoice.From.Add(new MailboxAddress("Rossi Forniture", "fatture@rossi.example"));
            invoice.To.Add(new MailboxAddress("Mario", "mario@studio-prova.it"));
            fake.Add("INBOX", invoice);
            var lunch = new MimeMessage { Subject = "Pranzo di giovedì", Date = DateTimeOffset.Now.AddMinutes(-1), Body = new TextPart("html") { Text = "<p>Ci sei?</p>" } };
            lunch.From.Add(new MailboxAddress("Anna", "anna@cliente.example"));
            lunch.To.Add(new MailboxAddress("Mario", "mario@studio-prova.it"));
            fake.Add("INBOX", lunch);
            var folders = await imap.GetFoldersAsync();
            var clienti = folders.First(f => f.Id == fake.UserFolder);
            var rule = new MailRule
            {
                AccountId = imapAccount.Id, Name = "Fatture Rossi", FromAddresses = ["fatture@rossi.example"], SubjectContains = ["fattura"],
                MoveTo = clienti.Id, MoveToName = clienti.Name,
            };
            Store.Settings.Rules.Add(rule);
            Rules.ForgetMarks();
            var since = DateTime.UtcNow.AddMinutes(-10);
            var incoming = await imap.GetNewAsync(since);
            fake.Commands.Clear();
            var outcome = await Rules.ApplyToNewAsync(imap, since, () => Task.FromResult(folders));
            var commands = fake.Commands.ToList();
            Report.AddRange(commands.Select(c => "      " + c));
            Check(incoming.Count == 2 && outcome is { Matched: 1 } && outcome.Removed.Single().Subject.StartsWith("Fattura") && commands.Count(c => c.StartsWith("UID MOVE")) == 1
                  && !commands.Any(c => c.StartsWith("UID STORE") || c.StartsWith("UID COPY")) && fake.Folders[fake.UserFolder].Count == 1 && fake.Folders["INBOX"].Count == 1,
                  "IMAP: due messaggi nuovi, uno corrisponde (mittente e oggetto): un solo UID MOVE verso Clienti, l'altro resta");
            fake.Commands.Clear();
            var second = await Rules.ApplyToNewAsync(imap, since, () => Task.FromResult(folders));
            Check(second == null && !fake.Commands.Any(c => c.StartsWith("UID MOVE") || c.StartsWith("UID STORE") || c.StartsWith("UID FETCH")),
                  "al controllo dopo nulla di nuovo dopo l'ultimo UID guardato: nessuna azione e nemmeno la lettura dei messaggi (e ognuno una volta sola, RulesSeen)");
            rule.Enabled = false;
            var third = await Rules.ApplyToNewAsync(imap, since, () => Task.FromResult(folders));
            Check(third == null, "una regola spenta non agisce");
            rule.Enabled = true;

            // Correzioni del giro P5: le regole vedono tutti i messaggi arrivati dall'ultima volta, anche già letti (sul telefono) e
            // più di 20 (una notte con il portatile chiuso), dall'ultimo UID guardato e non dall'ora dell'ultimo controllo.
            var suppliers = new MailRule { AccountId = imapAccount.Id, Name = "Fornitori", SenderContains = ["fornitore"], MoveTo = clienti.Id, MoveToName = clienti.Name };
            Store.Settings.Rules.Add(suppliers);
            await Rules.ApplyToNewAsync(imap, since, () => Task.FromResult(folders)); // il segno di dove si è arrivati
            for (int i = 0; i < 25; i++)
            {
                var night = new MimeMessage { Subject = $"Ordine {i + 1}", Date = DateTimeOffset.Now.AddHours(-8).AddMinutes(i), Body = new TextPart("plain") { Text = "Ordine" } };
                night.From.Add(new MailboxAddress("Ufficio fornitore", "ordini@fornitore.example"));
                fake.Add("INBOX", night, i % 5 == 0 ? "\\Seen" : "");
            }
            fake.Commands.Clear();
            var nightOutcome = await Rules.ApplyToNewAsync(imap, DateTime.UtcNow, () => Task.FromResult(folders));
            Check(nightOutcome is { Matched: 25 } && nightOutcome.Removed.Count == 25 && fake.Commands.Count(c => c.StartsWith("UID MOVE")) == 1
                  && fake.Folders[fake.UserFolder].Count == 26 && fake.Folders["INBOX"].Count == 1 && !fake.Commands.Any(c => c.Contains("UNSEEN")),
                  "regole: 25 messaggi arrivati di notte (5 già letti, tutti più vecchi dell'ultimo controllo) spostati tutti con un UID MOVE, non solo i 20 non letti più recenti");
            Store.Settings.Rules.Remove(suppliers);

            // Esegui ora: prima di agire la conferma con quanti messaggi cambiano; senza conferma nulla parte.
            fake.Commands.Clear();
            int asked = 0;
            var everything = new MailRule { AccountId = imapAccount.Id, Name = "Tutto letto", MarkRead = true };
            var refused = await Rules.RunAsync(imap, incoming, [everything], () => Task.FromResult(folders), confirm: n => { asked = n; return Task.FromResult(false); });
            Check(refused is { Canceled: true, Touched: 2, Matched: 2 } && asked == 2 && !fake.Commands.Any(c => c.StartsWith("UID ")),
                  "Esegui ora: la conferma dice quanti messaggi cambiano (2); annullata, nessun comando al server");

            var json = System.Text.Json.JsonSerializer.Serialize(Store.Settings, SettingsJson.Default.Settings);
            var back = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.Settings)!.Rules.Single(r => r.Id == rule.Id);
            var legacy = System.Text.Json.JsonSerializer.Deserialize("{\"Rules\":[{\"Name\":\"Vecchia\",\"FromAddresses\":[\"a@b.example\"]}]}", SettingsJson.Default.Settings)!;
            Check(!json.Contains("fatture@rossi.example") && !json.Contains("Fatture Rossi") && json.Contains("\"FromAddresses\": \"dpapi:") && json.Contains("\"MoveTo\": \"dpapi:")
                  && back is { Name: "Fatture Rossi", FromAddresses: ["fatture@rossi.example"], SubjectContains: ["fattura"] } && back.MoveTo == clienti.Id
                  && Store.Settings.RulesSeen.Skip(seenBefore).All(k => k.Length == 20 && !k.Contains('@')) && legacy.Rules[0].FromAddresses is ["a@b.example"],
                  "impostazioni: nome, indirizzi, parole e cartelle delle regole cifrati con DPAPI (\"dpapi:\"), rilette uguali; le impronte dei messaggi senza indirizzi");

            // Confronto con le condizioni, senza server.
            var me = new MailAccount { Email = "io@studio.example", Identities = [new SendIdentity { Email = "info@studio.example" }] };
            var message = new MessageSummary { Id = "1", FolderId = "INBOX", From = new Address { Name = "Banca Prova", Email = "avvisi@banca.example" }, Subject = "Estratto conto", To = [new Address { Email = "info@studio.example" }], HasAttachments = true };
            var withBody = new MessageBody { Html = "<p>Il saldo è di 1.200 euro</p>", Cc = [new Address { Email = "io@studio.example" }] };
            Check(Rules.Matches(new MailRule { SenderContains = ["banca"], SentToMe = true, HasAttachments = true }, message, null, me)
                  && !Rules.Matches(new MailRule { SenderContains = ["banca"], SubjectContains = ["fattura"] }, message, null, me)
                  && Rules.Matches(new MailRule { BodyContains = ["saldo"], SentCcMe = true }, message, withBody, me) && !Rules.Matches(new MailRule { BodyContains = ["saldo"] }, message, null, me)
                  && Rules.Matches(new MailRule { SubjectContains = ["fattura", "estratto"] }, message, null, me) && !Rules.Matches(new MailRule { WithImportance = Importance.High }, message, null, me)
                  && Rules.NeedsBody(new MailRule { SentCcMe = true }) && !Rules.NeedsBody(new MailRule { SentToMe = true }),
                  "condizioni: tutte insieme, dentro una basta una parola; inviato a un altro indirizzo dell'account; testo e Cc dal corpo");
        }
        finally
        {
            Store.Settings.Rules.RemoveAll(r => r.AccountId == imapAccount.Id);
            if (Store.Settings.RulesSeen.Count > seenBefore) Store.Settings.RulesSeen.RemoveRange(seenBefore, Store.Settings.RulesSeen.Count - seenBefore);
            Store.Save();
            ImapProvider.TestPlain = false;
        }
    }
}
#endif
