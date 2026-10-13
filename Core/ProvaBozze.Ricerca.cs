#if DEBUG
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MimeKit;

namespace Missiva;

/// <summary>Le prove del giro P5 (--prova-bozze): ricerca con i filtri.</summary>
static partial class ProvaBozze
{
    /// <summary>
    /// Ricerca con i filtri (giro P5): il testo con le parole chiave letto e riscritto, e le stringhe esatte per i tre provider
    /// sui server finti (X-GM-RAW di Gmail, $search e $filter di Graph, criteri SEARCH di IMAP), con le date del 25 ottobre
    /// 2026, il giorno del ritorno all'ora solare (25 ore).
    /// </summary>
    static async Task SearchFiltersAsync()
    {
        Report.Add("--- Ricerca con i filtri: Gmail, Graph e IMAP con i server finti, 25 ottobre");
        var terms = SearchTerms.Parse("fattura da:TIM a:\"Mario Rossi\" oggetto:marzo ha:allegati dal:25/10/2026 al:25/10/26 è:nonletto e:contrassegnato from:x OR to:y dal:ieri");
        Check(terms is { Text: "fattura from:x OR to:y dal:ieri", From: "TIM", To: "Mario Rossi", Subject: "marzo", HasAttachments: true, Unread: true, Flagged: true }
              && terms.Since == new DateTime(2026, 10, 25) && terms.Until == new DateTime(2026, 10, 25)
              && terms.ToString() == "fattura from:x OR to:y dal:ieri da:TIM a:\"Mario Rossi\" oggetto:marzo dal:25/10/2026 al:25/10/2026 ha:allegati è:nonletto è:contrassegnato"
              && SearchTerms.Parse(terms.ToString()) == terms && terms.Without("From").From.Length == 0 && !SearchTerms.Parse("fattura TIM").HasFilters,
              $"filtri letti dal testo (anche scritti a mano, con le virgolette, \"e:\" senza accento), il resto com'è (la sintassi di Gmail), riscritti uguali: {terms}");
        // 25 ottobre 2026: mezzanotte dell'ora legale (22:00 UTC del 24) e mezzanotte dell'ora solare del 26 (23:00 UTC del 25).
        long after = new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long before = new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var query = "fattura da:TIM ha:allegati dal:25/10/2026 al:25/10/2026";

        // Gmail: X-GM-RAW in "Tutti i messaggi".
        using (var fake = new FakeImap())
        {
            GmailProvider.TestPort = fake.Port;
            try
            {
                using var gmail = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com" });
                fake.Add(FakeImap.AllMail, "Fattura TIM di ottobre");
                fake.Commands.Clear();
                var page = await gmail.SearchAsync(null, query + " è:nonletto è:contrassegnato");
                var raw = fake.Commands.FirstOrDefault(c => c.Contains("X-GM-RAW")) ?? "";
                Report.Add("      " + raw);
                Check(raw.Contains($"X-GM-RAW \"fattura from:(TIM) has:attachment is:unread is:starred after:{after} before:{before}\"") && page.Messages.Count == 1,
                      $"Gmail: X-GM-RAW con from:(TIM), has:attachment, is:unread, is:starred e il 25 ottobre dalla mezzanotte alla mezzanotte in secondi Unix (25 ore)");
            }
            finally
            {
                GmailProvider.TestPort = null;
            }
        }

        // Graph: $search (KQL) con le date allargate e il giorno esatto sulla pagina; senza parole $filter con le date esatte.
        var paths = new List<string>();
        static JsonObject Message(string id, string when, bool read) => new()
        {
            ["id"] = id, ["subject"] = "Fattura " + id, ["receivedDateTime"] = when, ["isRead"] = read, ["parentFolderId"] = "inbox",
            ["from"] = new JsonObject { ["emailAddress"] = new JsonObject { ["name"] = "TIM", ["address"] = "fatture@tim.example" } },
        };
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            paths.Add(Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..]);
            var json = new JsonObject
            {
                ["value"] = new JsonArray(
                    Message("sera-del-24", "2026-10-24T21:30:00Z", false), Message("mattina", "2026-10-25T10:00:00Z", false),
                    Message("letta", "2026-10-25T12:00:00Z", true), Message("sera-del-25", "2026-10-25T22:30:00Z", false),
                    Message("notte-del-26", "2026-10-25T23:30:00Z", false)),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@outlook.example" });
        var found = await graph.SearchAsync(null, query + " è:nonletto");
        var search = paths.Single();
        Report.Add("      " + search);
        Check(search.StartsWith("me/messages?$search=\"fattura from:TIM hasattachments:true received>=2026-10-24 received<=2026-10-26\"")
              && found.Messages.Select(m => m.Id).OrderBy(i => i).SequenceEqual(["mattina", "sera-del-25"]),
              $"Graph: $search con from:TIM, hasattachments:true e le date allargate; sulla pagina solo il 25 ottobre dell'ora di Windows (le 23:30 CET del 25 sì, le 00:30 del 26 no) e i non letti: {string.Join(", ", found.Messages.Select(m => m.Id))}");
        paths.Clear();
        await graph.SearchAsync(null, "è:nonletto è:contrassegnato dal:25/10/2026 al:25/10/2026");
        Report.Add("      " + paths.Single());
        Check(paths.Single().StartsWith("me/messages?$filter=receivedDateTime ge 2026-10-24T22:00:00Z and receivedDateTime lt 2026-10-25T23:00:00Z and isRead eq false and flag/flagStatus eq 'flagged'&$orderby=receivedDateTime desc"),
              "Graph senza parole da cercare: $filter con il 25 ottobre esatto in UTC (25 ore), non letti e contrassegnati, receivedDateTime per primo");

        // IMAP: i criteri di SEARCH nella cartella e in tutte le cartelle.
        using (var fake = new FakeImap(new Flavor(Gmail: false)))
        {
            ImapProvider.TestPlain = true;
            try
            {
                var account = new MailAccount
                {
                    Kind = AccountKind.Imap, Email = "mario@studio-prova.it",
                    Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = fake.Port, SmtpHost = "127.0.0.1", SmtpPort = 1, UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect("x") },
                };
                using var imap = new ImapProvider(account);
                var mime = new MimeMessage { Subject = "Fattura TIM", Date = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(1)), Body = new TextPart("html") { Text = "<p>fattura</p>" } };
                mime.From.Add(new MailboxAddress("TIM", "fatture@tim.example"));
                fake.Add("INBOX", mime);
                var inbox = new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox };
                fake.Commands.Clear();
                await imap.SearchAsync(inbox, query + " è:nonletto è:contrassegnato");
                var here = fake.Commands.FirstOrDefault(c => c.StartsWith("UID SEARCH")) ?? "";
                fake.Commands.Clear();
                await imap.SearchAsync(null, "da:TIM dal:25/10/2026");
                var everywhere = fake.Commands.FirstOrDefault(c => c.StartsWith("UID SEARCH")) ?? "";
                Report.Add("      " + here);
                Report.Add("      " + everywhere);
                bool Has(string command, string part) => System.Text.RegularExpressions.Regex.IsMatch(command, part, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                Check(Has(here, "FROM \"?TIM\"?") && Has(here, "SINCE 25-Oct-2026") && Has(here, "BEFORE 26-Oct-2026") && Has(here, "UNSEEN") && Has(here, "FLAGGED")
                      && Has(here, "multipart/mixed") && Has(here, "BODY \"?fattura\"?") && Has(everywhere, "FROM \"?TIM\"?") && Has(everywhere, "SINCE 25-Oct-2026") && !Has(everywhere, "BEFORE"),
                      "IMAP: criteri SEARCH FROM, SINCE 25-Oct-2026, BEFORE 26-Oct-2026 (il giorno dopo \"Al\"), UNSEEN, FLAGGED, allegati dal multipart/mixed; anche in tutte le cartelle");
            }
            finally
            {
                ImapProvider.TestPlain = false;
            }
        }
    }
}
#endif
