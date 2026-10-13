#if DEBUG
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MimeKit;

namespace Missiva;

/// <summary>Le prove del giro P5 (--prova-bozze): nuovi messaggi subito, IDLE e query delta.</summary>
static partial class ProvaBozze
{
    /// <summary>
    /// Nuovi messaggi subito (giro P5): IDLE sul server IMAP finto (un messaggio aggiunto durante l'IDLE avvisa entro 2
    /// secondi, il rinnovo manda DONE prima di ogni altro comando, la connessione tagliata si riprende, i segni cambiati
    /// avvisano, un server senza IDLE lascia il controllo di ogni due minuti) e la query delta di Graph con il gestore finto
    /// (punto di partenza, messaggio nuovo, letto altrove, eliminato altrove, deltaLink seguito).
    /// </summary>
    static async Task IdleTestsAsync()
    {
        Report.Add("--- Nuovi messaggi subito: IDLE sul server IMAP finto, query delta di Graph con il gestore finto");
        static MimeMessage Mail(string subject)
        {
            var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now, Body = new TextPart("html") { Text = "<p>" + subject + "</p>" } };
            mime.From.Add(new MailboxAddress("Cliente", "cliente@esempio-prova.it"));
            return mime;
        }
        var (renew, retry) = (InboxIdle.Renew, InboxIdle.FirstRetry);
        InboxIdle.Renew = TimeSpan.FromSeconds(2);
        InboxIdle.FirstRetry = TimeSpan.FromMilliseconds(200);
        ImapProvider.TestPlain = true;
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            MailAccount Account(int port) => new()
            {
                Kind = AccountKind.Imap, Email = "mario@studio-prova.it",
                Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = port, SmtpHost = "127.0.0.1", SmtpPort = 1, UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect("x") },
            };
            using var imap = new ImapProvider(Account(fake.Port));
            fake.Add("INBOX", Mail("Primo"));
            fake.Add("INBOX", Mail("Secondo"));
            var events = new System.Collections.Concurrent.ConcurrentQueue<InboxNotice>();
            using var stop = new CancellationTokenSource();
            var watching = imap.WatchInboxAsync(e => events.Enqueue(e), stop.Token);
            Check(await Until(() => fake.Idling == 1, TimeSpan.FromSeconds(5)), "IDLE sulla Posta in arrivo, sulla sua connessione (EXAMINE, poi IDLE)");

            var clock = System.Diagnostics.Stopwatch.StartNew();
            fake.Add("INBOX", Mail("Urgente"));
            fake.Push($"* {fake.Folders["INBOX"].Count} EXISTS");
            bool arrived = await Until(() => events.Any(e => e.Kind == InboxEvent.Arrived), TimeSpan.FromSeconds(2));
            Check(arrived, $"messaggio aggiunto durante l'IDLE: avviso in {clock.ElapsedMilliseconds} ms (entro 2 s), e da lì il controllo dell'account");

            fake.Push("* 1 FETCH (UID 1 FLAGS (\\Seen))");
            Check(await Until(() => events.Any(e => e is { Kind: InboxEvent.Changed, Id: "1", Read: true, Flagged: false }), TimeSpan.FromSeconds(2)),
                  "segno cambiato altrove (FETCH con UID e FLAGS durante l'IDLE): avviso con il messaggio e i segni, cambia solo la riga");
            fake.Push("* 2 FETCH (FLAGS (\\Flagged))");
            Check(await Until(() => events.Any(e => e is { Kind: InboxEvent.Changed, Id: null }), TimeSpan.FromSeconds(2)),
                  "segno cambiato senza UID nella FETCH: avviso senza messaggio, e la Posta in arrivo aperta si rilegge");

            // L'eco delle azioni di Missiva: durante una scrittura e per poco dopo gli avvisi di segni e usciti si ignorano.
            var window = InboxIdle.OwnWindow;
            InboxIdle.OwnWindow = TimeSpan.FromMilliseconds(300);
            try
            {
                var inboxFolder = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
                var firstMessage = (await imap.GetMessagesAsync(inboxFolder, null, null)).Messages.First();
                bool quietBefore = !InboxIdle.OwnEcho(imap.Account.Id);
                await imap.SetReadAsync([firstMessage], true);
                bool echo = InboxIdle.OwnEcho(imap.Account.Id);
                await Task.Delay(450);
                Check(quietBefore && echo && !InboxIdle.OwnEcho(imap.Account.Id) && !InboxIdle.OwnEcho("un altro account"),
                      "eco delle azioni di Missiva: dopo Già letto (UID STORE) gli avvisi dell'IDLE di quell'account si ignorano per poco, poi di nuovo");
            }
            finally
            {
                InboxIdle.OwnWindow = window;
            }

            bool renewed = await Until(() => { lock (fake.IdleLog) return fake.IdleLog.Count(l => l == "IDLE") >= 2; }, TimeSpan.FromSeconds(6));
            List<string> log;
            lock (fake.IdleLog) log = fake.IdleLog.ToList();
            bool alternating = log.Where(l => l is "IDLE" or "DONE").Select((l, i) => l == (i % 2 == 0 ? "IDLE" : "DONE")).All(ok => ok);
            Check(renewed && alternating && !log.Any(l => l.StartsWith("durante")),
                  $"rinnovo dell'IDLE: DONE prima di ogni altro comando, poi di nuovo IDLE ({string.Join(", ", log.Take(6))})");

            int before;
            lock (fake.IdleLog) before = fake.IdleLog.Count(l => l == "IDLE");
            fake.DropIdlers();
            bool back = await Until(() => { lock (fake.IdleLog) return fake.IdleLog.Count(l => l == "IDLE") > before && fake.Idling == 1; }, TimeSpan.FromSeconds(6));
            Check(back, "connessione dell'IDLE tagliata: si ricollega da sola e torna in IDLE");

            stop.Cancel();
            var ended = await Task.WhenAny(watching, Task.Delay(TimeSpan.FromSeconds(5))) == watching;
            Check(ended, "Missiva che esce (o l'account rimosso): l'IDLE finisce");

            // Gestisci account con un altro server (trasloco della posta): l'IDLE si ricollega subito a quello nuovo.
            using var moved = new FakeImap(new Flavor(Gmail: false));
            using var moving = new ImapProvider(Account(fake.Port));
            using var stopMoving = new CancellationTokenSource();
            var watchingMoving = moving.WatchInboxAsync(_ => { }, stopMoving.Token);
            bool onOld = await Until(() => fake.Idling == 1, TimeSpan.FromSeconds(5));
            moving.Account.Imap = Account(moved.Port).Imap;
            await moving.ResetConnectionsAsync();
            bool onNew = await Until(() => moved.Idling == 1 && fake.Idling == 0, TimeSpan.FromSeconds(5));
            stopMoving.Cancel();
            await Task.WhenAny(watchingMoving, Task.Delay(TimeSpan.FromSeconds(5)));
            Check(onOld && onNew, "Gestisci account con un altro server: l'IDLE lascia subito il server di prima e si ricollega a quello nuovo, senza aspettare il rinnovo");

            using var plain = new FakeImap(new Flavor(Gmail: false, Idle: false));
            using var old = new ImapProvider(Account(plain.Port));
            var none = old.WatchInboxAsync(_ => { }, CancellationToken.None);
            Check(await Task.WhenAny(none, Task.Delay(TimeSpan.FromSeconds(5))) == none && !plain.Commands.Any(c => c.StartsWith("IDLE")),
                  "server senza IDLE: nessun IDLE, restano i controlli di ogni due minuti");
        }
        finally
        {
            (InboxIdle.Renew, InboxIdle.FirstRetry) = (renew, retry);
            ImapProvider.TestPlain = false;
        }

        // Query delta di Graph.
        var urls = new List<string>();
        int call = 0;
        static JsonObject M(string id, bool read) => new() { ["id"] = id, ["isRead"] = read, ["receivedDateTime"] = "2026-09-27T08:00:00Z" };
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            urls.Add(Uri.UnescapeDataString(request.RequestUri!.PathAndQuery));
            var json = (call++) switch
            {
                0 => new JsonObject { ["value"] = new JsonArray(M("m1", false)), ["@odata.nextLink"] = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$skiptoken=P2" },
                1 => new JsonObject { ["value"] = new JsonArray(M("m2", true)), ["@odata.deltaLink"] = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=A" },
                2 => new JsonObject
                {
                    ["value"] = new JsonArray(M("m3", false), M("m1", true), new JsonObject { ["id"] = "m2", ["@removed"] = new JsonObject { ["reason"] = "deleted" } }),
                    ["@odata.deltaLink"] = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=B",
                },
                _ => new JsonObject { ["value"] = new JsonArray(), ["@odata.deltaLink"] = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages/delta?$deltatoken=C" },
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "prova@outlook.example" });
        // Windows con l'ora scritta con il punto ("HH.mm.ss"): il filtro deve avere comunque i due punti.
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var dotted = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.GetCultureInfo("it-IT").Clone();
        dotted.DateTimeFormat.TimeSeparator = ".";
        System.Globalization.CultureInfo.CurrentCulture = dotted;
        InboxChanges? start;
        try { start = await graph.GetInboxChangesAsync(); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        var changes = await graph.GetInboxChangesAsync();
        var quiet = await graph.GetInboxChangesAsync();
        Check(start == null && urls.Count == 4 && urls[0].StartsWith("/v1.0/me/mailFolders/inbox/messages/delta?$select=id,isRead,receivedDateTime&$filter=receivedDateTime ge ")
              && System.Text.RegularExpressions.Regex.IsMatch(urls[0], @"ge \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")
              && urls[1].Contains("$skiptoken=P2") && urls[2].Contains("$deltatoken=A") && urls[3].Contains("$deltatoken=B"),
              $"delta di Graph: la prima lettura (due pagine) fa da punto di partenza senza avvisi, con i due punti nell'ora anche se Windows usa il punto ({urls[0][^24..]}); poi dal deltaLink, sempre l'ultimo");
        Check(changes is { } c && c.Seen.Contains(("m3", false)) && c.Seen.Contains(("m1", true)) && c.Removed.SequenceEqual(["m2"]) && quiet is { Any: false },
              "delta di Graph: messaggio nuovo (m3 da leggere), letto altrove (m1), eliminato altrove (m2 con @removed); poi niente");
        var testing = new TestingProvider(graph);
        Check(testing.HasInboxDelta && await testing.GetInboxChangesAsync() is { Any: false }, "nella prova la query delta passa al provider vero: è una lettura");
    }
}
#endif
