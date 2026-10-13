#if DEBUG
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MimeKit;

namespace Missiva;

/// <summary>Le prove del giro P5 (--prova-bozze): lavoro senza rete, coda delle azioni e Posta in uscita.</summary>
static partial class ProvaBozze
{
    /// <summary>
    /// Lavoro senza rete (giro P5, parte 6a): la coda delle azioni cifrata su disco (nessun identificativo in chiaro, mai
    /// l'oggetto), gli elenchi riletti con le azioni in coda, Annulla che toglie dalla coda; la coda ripetuta sul server IMAP
    /// finto (rete ancora assente: tutto torna in coda; due ritorni della rete insieme: un solo UID MOVE; un messaggio sparito
    /// detto) e con il gestore finto di Graph (un 404 per il messaggio sparito); la Posta in uscita che parte una volta sola
    /// via SMTP finto e Graph finto, e che senza rete resta.
    /// </summary>
    static async Task OfflineQueueAsync()
    {
        Report.Add("--- Senza rete: coda delle azioni e Posta in uscita (server IMAP e SMTP finti, gestore finto di Graph)");
        var file = Path.Combine(Store.Dir, "coda.dat");
        File.Delete(file);
        ActionQueue.Reset();

        // Stato "non in linea" dell'account: da un errore di rete fino alla prima richiesta riuscita.
        var probe = new MailAccount { Kind = AccountKind.Imap, Email = "sonda@studio-prova.it" };
        Network.Note(probe, new IOException("x", new SocketException(10065)));
        bool away = Network.KnownOffline(probe);
        Network.Note(probe, new MailException("password rifiutata"));
        bool stillAway = Network.KnownOffline(probe);
        Network.Note(probe, null);
        Check(away && stillAway && !Network.KnownOffline(probe),
              "account non in linea dal primo errore di rete fino alla prima richiesta riuscita (un altro errore non lo rimette in linea): intanto le azioni vanno in coda subito");

        // Il file della coda.
        var secret = "Riservato " + Guid.NewGuid().ToString("N")[..8];
        var local = new MailAccount { Kind = AccountKind.Imap, Email = "coda@studio-prova.it" };
        var hidden = new MessageSummary { Id = "7", FolderId = "Clienti Bianchi", StableId = "<chiave-7@studio-prova.it>", Subject = secret, From = new Address { Email = "bianchi@cliente.example" } };
        ActionQueue.Add(local, [hidden], QueuedKind.Move, "Trash");
        ActionQueue.Add(local, [hidden], QueuedKind.Read);
        ActionQueue.Add(local, [hidden], QueuedKind.Unread);
        var bytes = File.ReadAllBytes(file);
        string plain = Encoding.UTF8.GetString(bytes) + Encoding.Unicode.GetString(bytes);
        var opened = Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        Check(!plain.Contains("Clienti Bianchi") && !plain.Contains(local.Id) && !plain.Contains("chiave-7") && opened.Contains("Clienti Bianchi")
              && !opened.Contains(secret) && !opened.Contains("bianchi@cliente.example") && ActionQueue.Count(local.Id) == 2,
              "coda delle azioni in coda.dat cifrata con DPAPI (né cartelle né identificativi in chiaro), dentro mai oggetto o mittente; letto e poi da leggere valgono come uno");
        ActionQueue.Reset();
        var list = new List<MessageSummary>
        {
            new() { Id = "7", FolderId = "Clienti Bianchi", Subject = "a" },
            new() { Id = "7", FolderId = "Altra cartella", Subject = "b" },
            new() { Id = "9", FolderId = "Clienti Bianchi", Subject = "c" },
        };
        ActionQueue.Apply(local.Id, list);
        Check(ActionQueue.Count(local.Id) == 2 && list.Select(m => m.Subject).SequenceEqual(["b", "c"]),
              "coda riletta dal file dopo la chiusura; l'elenco riletto senza rete non mostra il messaggio spostato in coda (lo stesso UID in un'altra cartella sì)");
        int undone = ActionQueue.Remove(local, [hidden], "Trash");
        ActionQueue.Forget(local.Id);
        Check(undone == 1 && ActionQueue.Count(local.Id) == 0 && !File.Exists(file),
              "Annulla toglie lo spostamento dalla coda prima che parta; account rimosso: le sue azioni escono, e senza azioni coda.dat sparisce");

        // La coda ripetuta sul server IMAP finto.
        using var fake = new FakeImap(new Flavor(Gmail: false));
        using var smtp = new FakeSmtp();
        ImapProvider.TestPlain = true;
        ImapProvider.TestCertificate = smtp.Certificate.GetCertHashString();
        var account = new MailAccount
        {
            Kind = AccountKind.Imap, Email = "mario@studio-prova.it", DisplayName = "Mario Rossi",
            Imap = new ImapServer
            {
                ImapHost = "127.0.0.1", ImapPort = fake.Port, ImapSecurity = MailSecurity.Ssl, SmtpHost = "127.0.0.1", SmtpPort = smtp.Port,
                SmtpSecurity = MailSecurity.StartTls, UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect("Password-di-prova"),
            },
        };
        try
        {
            using var imap = new ImapProvider(account);
            fake.Add("INBOX", "Fattura Rossi");
            var gone = fake.Add("INBOX", "Preventivo Verdi");
            fake.Add("INBOX", "Riunione di lunedì");
            fake.Add("INBOX", "Scadenza F24");
            var folders = await imap.GetFoldersAsync();
            var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
            var trash = folders.First(f => f.Kind == FolderKind.Deleted);
            var page = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            MessageSummary Named(string subject) => page.Single(m => m.Subject == subject);
            ActionQueue.Add(account, [Named("Fattura Rossi"), Named("Preventivo Verdi")], QueuedKind.Move, trash.Id);
            ActionQueue.Add(account, [Named("Riunione di lunedì")], QueuedKind.Read);
            ActionQueue.Add(account, [Named("Scadenza F24")], QueuedKind.Flag);
            fake.Folders["INBOX"].Remove(gone); // eliminato dal telefono mentre Missiva era senza rete
            Task<List<MailFolder>> Folders() => Task.FromResult(folders);

            await imap.ResetConnectionsAsync();
            Network.TestOffline = true;
            fake.Commands.Clear();
            var down = await ActionQueue.ReplayAsync(imap, Folders);
            Network.TestOffline = false;
            Check(down is { Done: 0, Waiting: 4 } && ActionQueue.Count(account.Id) == 4 && !fake.Commands.Any(c => c.StartsWith("UID ")),
                  "ritorno della rete che non c'è ancora: le quattro azioni tornano in coda, nello stesso ordine, e al server non arriva nulla");

            fake.Commands.Clear();
            var twice = await Task.WhenAll(ActionQueue.ReplayAsync(imap, Folders), ActionQueue.ReplayAsync(imap, Folders));
            var commands = fake.Commands.ToList();
            Report.AddRange(commands.Where(c => c.StartsWith("UID ")).Select(c => "      " + c));
            Check(commands.Count(c => c.StartsWith("UID MOVE")) == 1 && commands.Count(c => c.StartsWith("UID STORE") && c.Contains("\\Seen")) == 1
                  && commands.Count(c => c.StartsWith("UID STORE") && c.Contains("\\Flagged")) == 1 && twice.Sum(r => r.Done) == 3 && twice.Sum(r => r.Gone) == 1
                  && fake.Folders[fake.TrashFolder].Count == 1 && ActionQueue.Count(account.Id) == 0,
                  $"due ritorni della rete insieme: la coda parte una volta sola (un UID MOVE nel Cestino, un segno di letto, un contrassegno); il messaggio eliminato altrove non si sposta ed è detto ({twice.Sum(r => r.Gone)} non più possibile)");
            fake.Commands.Clear();
            var after = await ActionQueue.ReplayAsync(imap, Folders);
            Check(after == new ActionQueue.Replay(0, 0, 0) && !fake.Commands.Any(c => c.StartsWith("UID ")), "al ritorno dopo nulla si ripete");

            // La Posta in uscita via SMTP finto.
            int sentCalls = 0, failedCalls = 0;
            var entry = new Outbox.Entry
            {
                AccountId = account.Id, Sent = _ => sentCalls++, Failed = _ => failedCalls++,
                Message = new OutgoingMessage { To = [new Address { Email = "cliente@example.com" }], Subject = "Preventivo scritto in treno", Html = "<p>Ecco il preventivo</p>", Text = "Ecco il preventivo" },
            };
            Outbox.Add(entry, account.Email);
            Network.TestOffline = true;
            var none = await Outbox.SendAsync(imap);
            Network.TestOffline = false;
            Check(none == 0 && Outbox.Count(account.Id) == 1 && smtp.Received.Count == 0 && sentCalls == 0,
                  "Posta in uscita con la rete ancora assente: il messaggio resta, nulla parte");
            var sentBoth = await Task.WhenAll(Outbox.SendAsync(imap), Outbox.SendAsync(imap));
            Check(sentBoth.Sum() == 1 && smtp.Received.Count == 1 && sentCalls == 1 && failedCalls == 0 && Outbox.Count(account.Id) == 0,
                  "Posta in uscita al ritorno della rete: il messaggio parte una volta sola via SMTP finto, anche con due ritorni insieme, e la finestra si chiude");
            var removedBefore = new Outbox.Entry { AccountId = account.Id, Message = entry.Message, Sent = _ => sentCalls++ };
            Outbox.Add(removedBefore, account.Email);
            bool taken = Outbox.Remove(removedBefore);
            Check(taken && await Outbox.SendAsync(imap) == 0 && smtp.Received.Count == 1, "Annulla sulla barra della Posta in uscita: il messaggio non parte più");
        }
        finally
        {
            Network.TestOffline = false;
            ImapProvider.TestPlain = false;
            ImapProvider.TestCertificate = null;
            ActionQueue.Forget(account.Id);
        }

        // Graph: la coda messaggio per messaggio, un 404 per quello che non c'è più; la Posta in uscita con /send una volta.
        var requests = new List<(HttpMethod Method, string Path)>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            lock (requests) requests.Add((request.Method, path));
            if (path.StartsWith("me/messages/m2"))
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"ErrorItemNotFound\",\"message\":\"The specified object was not found in the store.\"}}", Encoding.UTF8, "application/json"),
                };
            JsonNode json = path.EndsWith("/move") ? new JsonObject { ["id"] = "spostato-" + path.Split('/')[2] }
                : request.Method == HttpMethod.Post && path == "me/messages" ? new JsonObject { ["id"] = "bozza-uscita" } : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var graphAccount = new MailAccount { Kind = AccountKind.Microsoft, Email = "coda@outlook.example" };
        var graph = new GraphProvider(graphAccount);
        try
        {
            var deleted = new MailFolder { Id = "cartella-eliminata", Name = "Posta eliminata", Kind = FolderKind.Deleted };
            MessageSummary M(string id) => new() { Id = id, FolderId = "cartella-arrivo" };
            ActionQueue.Add(graphAccount, [M("m1"), M("m2")], QueuedKind.Move, deleted.Id);
            ActionQueue.Add(graphAccount, [M("m3")], QueuedKind.Read);
            Task<List<MailFolder>> GraphFolders() => Task.FromResult(new List<MailFolder> { deleted });
            Network.TestOffline = true;
            var down = await ActionQueue.ReplayAsync(graph, GraphFolders);
            Network.TestOffline = false;
            Check(down.Waiting == 3 && requests.Count == 0, "Graph, rete ancora assente: le tre azioni restano in coda, nessuna richiesta");
            var back = await ActionQueue.ReplayAsync(graph, GraphFolders);
            List<(HttpMethod Method, string Path)> seen;
            lock (requests) seen = requests.ToList();
            Check(back is { Done: 2, Gone: 1, Waiting: 0 } && seen.Count(r => r.Method == HttpMethod.Post && r.Path == "me/messages/m1/move") == 1
                  && seen.Count(r => r.Path == "me/messages/m2/move") == 1 && seen.Count(r => r.Method == HttpMethod.Patch && r.Path == "me/messages/m3") == 1 && seen.Count == 3,
                  $"Graph, al ritorno della rete: una richiesta per messaggio, il 404 del messaggio eliminato altrove detto come non più possibile ({string.Join(", ", seen.Select(r => r.Method + " " + r.Path))})");
            lock (requests) requests.Clear();
            var again = await ActionQueue.ReplayAsync(graph, GraphFolders);
            Check(again.Done == 0 && requests.Count == 0, "Graph: al ritorno dopo nessuna richiesta");

            int sent = 0;
            Outbox.Add(new Outbox.Entry
            {
                AccountId = graphAccount.Id, Sent = _ => sent++,
                Message = new OutgoingMessage { To = [new Address { Email = "cliente@example.com" }], Subject = "Dall'aereo", Html = "<p>Atterrato</p>", Text = "Atterrato" },
            }, graphAccount.Email);
            Network.TestOffline = true;
            await Outbox.SendAsync(graph);
            Network.TestOffline = false;
            bool waited = requests.Count == 0 && Outbox.Count(graphAccount.Id) == 1;
            await Task.WhenAll(Outbox.SendAsync(graph), Outbox.SendAsync(graph));
            lock (requests) seen = requests.ToList();
            Check(waited && sent == 1 && seen.Count(r => r.Path.EndsWith("/send")) == 1 && Outbox.Count(graphAccount.Id) == 0,
                  $"Graph, Posta in uscita: senza rete nessuna richiesta, al ritorno un solo /send ({string.Join(", ", seen.Select(r => r.Method + " " + r.Path))})");
        }
        finally
        {
            Network.TestOffline = false;
            ActionQueue.Forget(graphAccount.Id);
        }

        // Correzioni del giro P5: le richieste in sottofondo (controllo, delta, copia, coda) non aprono mai la finestra di
        // Microsoft, e il segno non esce dalla loro chiamata.
        var probeAccount = new MailAccount { Kind = AccountKind.Microsoft, Email = "sonda@outlook.example" };
        bool mayBefore = MicrosoftAuth.MayAsk(probeAccount);
        bool mayInside = await MicrosoftAuth.InBackgroundAsync(async () => { await Task.Yield(); return MicrosoftAuth.MayAsk(probeAccount); });
        bool mayInTask = await MicrosoftAuth.InBackgroundAsync(() => Task.Run(() => MicrosoftAuth.MayAsk(probeAccount)));
        bool mayAfter = MicrosoftAuth.MayAsk(probeAccount);
        Check(mayBefore && !mayInside && !mayInTask && mayAfter,
              "richieste in sottofondo (MicrosoftAuth.InBackgroundAsync): dentro, anche nei Task che partono da lì, nessuna finestra di Microsoft da sola; fuori, come prima");

        // "Non più possibile" solo per i messaggi che non ci sono più. Un 429 ripetuto aspetta con il motivo vero, un 403
        // esce come rifiutato (non "eliminato altrove"), un accesso da rifare o annullato aspetta.
        bool busy = true;
        var tried = new List<string>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            lock (tried) tried.Add(request.Method + " " + path);
            var (status, code) = path.StartsWith("me/messages/m5") ? (HttpStatusCode.Forbidden, "ErrorAccessDenied")
                : path.StartsWith("me/messages/m4") && busy ? (HttpStatusCode.TooManyRequests, "ApplicationThrottled") : (HttpStatusCode.OK, "");
            var answer = new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? "{}" : $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"{code}\"}}}}", Encoding.UTF8, "application/json"),
            };
            if (status == HttpStatusCode.TooManyRequests) answer.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(50));
            return answer;
        }));
        var busyAccount = new MailAccount { Kind = AccountKind.Microsoft, Email = "occupata@outlook.example" };
        var busyGraph = new GraphProvider(busyAccount);
        var stubAccount = new MailAccount { Kind = AccountKind.Imap, Email = "accesso@studio-prova.it" };
        var stub = DispatchProxy.Create<IMailProvider, QueueStub>();
        var queueStub = (QueueStub)(object)stub;
        queueStub.Account = stubAccount;
        try
        {
            var trashFolder = new MailFolder { Id = "cestino", Name = "Posta eliminata", Kind = FolderKind.Deleted };
            Task<List<MailFolder>> BusyFolders() => Task.FromResult(new List<MailFolder> { trashFolder });
            MessageSummary G(string id) => new() { Id = id, FolderId = "arrivo" };
            ActionQueue.Add(busyAccount, [G("m5")], QueuedKind.Move, trashFolder.Id);
            ActionQueue.Add(busyAccount, [G("m4")], QueuedKind.Read);
            ActionQueue.Add(busyAccount, [G("m6")], QueuedKind.Read);
            var throttled = await ActionQueue.ReplayAsync(busyGraph, BusyFolders);
            Check(throttled is { Done: 0, Gone: 0, Failed: 1, Waiting: 2 } && throttled.Reason?.Contains("ApplicationThrottled") == true
                  && throttled.Refused?.Contains("ErrorAccessDenied") == true && ActionQueue.Count(busyAccount.Id) == 2
                  && !tried.Any(t => t.Contains("m6")),
                  $"Graph 403 su uno spostamento: rifiutato con il motivo, non \"eliminato altrove\"; 429 ripetuto: l'azione e le seguenti restano in coda ({throttled})");
            busy = false;
            var calm = await ActionQueue.ReplayAsync(busyGraph, BusyFolders);
            Check(calm is { Done: 2, Gone: 0, Failed: 0, Waiting: 0 } && ActionQueue.Count(busyAccount.Id) == 0, "Graph non più occupata: al ritorno dopo le due azioni rimaste partono");

            ActionQueue.Add(stubAccount, [G("7"), G("8")], QueuedKind.Move, trashFolder.Id);
            queueStub.StillThere = () => Task.FromException<List<MessageSummary>?>(new MailException("Gmail chiede un nuovo accesso.") { Fix = AccountFix.SignIn });
            var signIn = await ActionQueue.ReplayAsync(stub, BusyFolders);
            Check(signIn is { Gone: 0, Waiting: 2 } && signIn.Reason == "Gmail chiede un nuovo accesso." && queueStub.Calls == 0 && ActionQueue.Count(stubAccount.Id) == 2,
                  "accesso da rifare (invalid_grant) mentre si controlla quali messaggi ci sono: nessuna azione esce come eliminata altrove, tutte aspettano con il motivo");
            queueStub.StillThere = () => Task.FromResult<List<MessageSummary>?>(null);
            queueStub.Act = () => Task.FromException(new Microsoft.Identity.Client.MsalClientException("authentication_canceled", "annullato"));
            var canceled = await ActionQueue.ReplayAsync(stub, BusyFolders);
            Check(canceled is { Gone: 0, Failed: 0, Waiting: 2 } && ActionQueue.Count(stubAccount.Id) == 2, "Annulla nella finestra di Microsoft durante la ripetizione: le azioni restano in coda");
            queueStub.Act = () => Task.FromException(new MailException("Il server non ha spostato il messaggio: NO [CANNOT] cartella in sola lettura"));
            var refused = await ActionQueue.ReplayAsync(stub, BusyFolders);
            Check(refused is { Gone: 0, Failed: 2, Waiting: 0 } && refused.Refused!.Contains("sola lettura") && ActionQueue.Count(stubAccount.Id) == 0,
                  "il server rifiuta lo spostamento (un NO di IMAP): le azioni escono come rifiutate, con il motivo del server");
        }
        finally
        {
            ActionQueue.Forget(busyAccount.Id);
            ActionQueue.Forget(stubAccount.Id);
        }
    }

    /// <summary>Un provider finto per la coda delle azioni: quali messaggi ci sono ancora e che cosa fanno le azioni.</summary>
    public class QueueStub : DispatchProxy
    {
        public MailAccount Account = new();
        public Func<Task<List<MessageSummary>?>> StillThere = () => Task.FromResult<List<MessageSummary>?>(null);
        public Func<Task> Act = () => Task.CompletedTask;
        public int Calls;
        /// <summary>I messaggi su cui un'azione è partita, nell'ordine (uno stesso id due volte: ripetuta).</summary>
        public readonly List<string> Acted = new();

        void Note(object?[]? args)
        {
            Calls++;
            if (args is [IReadOnlyList<MessageSummary> messages, ..]) lock (Acted) Acted.AddRange(messages.Select(m => m.Id));
        }

        async Task<List<MovedMessage>> MoveAsync(object?[]? args)
        {
            Note(args);
            await Act();
            return new();
        }

        async Task PlainAsync(object?[]? args)
        {
            Note(args);
            await Act();
        }

        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Account" => Account,
            "StillThereAsync" => StillThere(),
            "MoveAsync" => MoveAsync(args),
            "SetReadAsync" or "SetFlagAsync" => PlainAsync(args),
            _ => throw new NotSupportedException(method?.Name),
        };
    }

    /// <summary>
    /// Lavoro senza rete (giro P5, parte 6b): la Posta in arrivo degli ultimi 30 giorni sul PC, sul server IMAP finto (i corpi
    /// dei messaggi recenti con BODY.PEEK sulla connessione in sottofondo in sola lettura: sotto 300 KB interi, di quelli
    /// grandi con allegati solo il testo, ImapProvider.Prefetchable; non quelli vecchi né quelli con oltre 2 MB da leggere, e
    /// al giro dopo nulla), cifrata su disco, e la ricerca nella copia con le parole e i filtri.
    /// </summary>
    static async Task OfflineCopyTestsAsync()
    {
        Report.Add("--- Senza rete: Posta in arrivo degli ultimi 30 giorni sul PC e ricerca nella copia (server IMAP finto)");
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var account = new MailAccount
        {
            Kind = AccountKind.Imap, Email = "mario@studio-prova.it",
            Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = fake.Port, SmtpHost = "127.0.0.1", SmtpPort = 1, UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect("x") },
        };
        MimeMessage Mail(string subject, string from, string text, int daysAgo, byte[]? attachment = null)
        {
            var body = new BodyBuilder { HtmlBody = $"<style>.x{{color:red}}</style><p>{text}</p>" };
            if (attachment != null) body.Attachments.Add("scansione.pdf", attachment);
            var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now.AddDays(-daysAgo), Body = body.ToMessageBody() };
            mime.From.Add(new MailboxAddress(from, from.ToLowerInvariant().Replace(' ', '.') + "@fornitori.example"));
            mime.To.Add(new MailboxAddress("Mario", "mario@studio-prova.it"));
            return mime;
        }
        var secret = "riservatissimo" + Guid.NewGuid().ToString("N")[..6];
        fake.Add("INBOX", Mail("Fattura di settembre", "Rossi Forniture", $"In allegato la fattura della fornitura {secret}", 1));
        fake.Add("INBOX", Mail("Riunione di condominio", "Anna Bianchi", "Ci vediamo giovedì alle 18", 3));
        fake.Add("INBOX", Mail("Fattura di luglio", "Rossi Forniture", "La fattura vecchia", 40));
        fake.Add("INBOX", Mail("Scansioni del cantiere", "Geometra Verdi", "Le foto del cantiere", 2, new byte[400_000]));
        // Oltre 2 MB da leggere (ImapProvider.ReadableLimit): l'unico recente che la copia lascia sul server.
        fake.Add("INBOX", Mail("Verbale lunghissimo", "Geometra Verdi", string.Concat(Enumerable.Repeat("verbale del sopralluogo riga per riga ", 56_000)), 5));
        MailCache.Delete(account);
        try
        {
            using var imap = new ImapProvider(account);
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            fake.Commands.Clear();
            int saved = await OfflineCopy.RunAsync(imap, inbox, CancellationToken.None);
            Task.WaitAll(BodyCache.TestWrites.ToArray());
            var commands = fake.Commands.ToList();
            Report.AddRange(commands.Where(c => c.StartsWith("UID ") || c.StartsWith("EXAMINE") || c.StartsWith("SELECT")).Select(c => "      " + c));
            // Dal 28 settembre 2026 (ImapProvider.Prefetchable) un messaggio grande con allegati non resta fuori: se ne copia la
            // parte da leggere (BODY.PEEK[1] con le sue intestazioni), mai l'allegato (BODY.PEEK[2]), che resta un nome sulla scheda.
            // Fino al blocco 6 dell'audit questo controllo diceva "non quello da 400 KB", e passava solo perché il server finto non
            // rispondeva a BODY.PEEK[1.MIME] e la lettura parte per parte falliva.
            static bool Body(string c) => c.Contains("BODY.PEEK[") && !c.Contains("HEADER.FIELDS (MESSAGE-ID)", StringComparison.OrdinalIgnoreCase);
            var wholeBodies = commands.Where(c => c.Contains("BODY.PEEK[]")).ToList();
            var textOnly = commands.Where(c => c.Contains("BODY.PEEK[1]")).ToList();
            var bigRow = BodyCache.Search(account, "oggetto:scansioni", null, 5).FirstOrDefault();
            var bigBody = bigRow == null ? null : BodyCache.Load(account, bigRow);
            Check(saved == 3 && wholeBodies.Count == 2 && wholeBodies.All(c => c.StartsWith("UID FETCH 1 ") || c.StartsWith("UID FETCH 2 "))
                  && textOnly is [var part] && part.StartsWith("UID FETCH 4 ") && part.Contains("BODY.PEEK[1.MIME]") && !commands.Any(c => c.Contains("BODY.PEEK[2"))
                  && !commands.Any(c => Body(c) && (c.StartsWith("UID FETCH 3 ") || c.StartsWith("UID FETCH 5 ")))
                  && commands.Any(c => c.StartsWith("EXAMINE")) && !commands.Any(c => c.StartsWith("SELECT"))
                  && !commands.Any(c => c.StartsWith("UID STORE")) && BodyCache.Size(account).Count == 3
                  && bigBody is { Attachments: [{ Name: "scansione.pdf", Content: null, IsInline: false }] } && bigBody.Html.Contains("Le foto del cantiere"),
                  "ultimi 30 giorni sul PC, sulla connessione in sottofondo in sola lettura (EXAMINE): i due messaggi recenti sotto 300 KB interi con BODY.PEEK[]; di quello da 400 KB " +
                  $"con un allegato solo il testo (BODY.PEEK[1], l'allegato resta un nome sulla scheda e non si scarica); non quello di 40 giorni fa né quello con oltre 2 MB di testo ({saved} scaricati)");
            fake.Commands.Clear();
            int again = await OfflineCopy.RunAsync(imap, inbox, CancellationToken.None);
            Check(again == 0 && !fake.Commands.Any(Body), "al giro dopo i corpi già sul PC non si riscaricano");
            // Giro D1: il giro guarda prima solo UID, segni, data e Message-Id; busta e struttura solo di quello che manca: qui il
            // messaggio con oltre 2 MB di testo (UID 5), che la copia non prende mai e a ogni giro manca ancora. Prima rileggeva
            // busta e struttura di tutti a ogni giro.
            var whole = fake.Commands.Where(c => c.StartsWith("UID FETCH") && c.Contains("BODYSTRUCTURE")).ToList();
            Report.AddRange(fake.Commands.Where(c => c.StartsWith("UID FETCH")).Select(c => "      " + c));
            Check(whole is [var only] && only.StartsWith("UID FETCH 5 ") && fake.Commands.Any(c => c.StartsWith("UID FETCH") && c.Contains("MESSAGE-ID", StringComparison.OrdinalIgnoreCase)),
                  "ultimi 30 giorni: al giro dopo solo UID, segni, data e Message-Id, e busta e struttura del solo messaggio che manca ancora (quello troppo grande da leggere, che resta sul server)");
            fake.Add("INBOX", Mail("Preventivo nuovo", "Anna Bianchi", "Il preventivo che aspettavi", 0));
            var invite = new BodyBuilder { HtmlBody = "<p>Ci vediamo in cantiere</p>" };
            invite.Attachments.Add("invito.ics", Encoding.UTF8.GetBytes("BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nEND:VCALENDAR\r\n"), MimeKit.ContentType.Parse("text/calendar"));
            var inviteMail = new MimeMessage { Subject = "Invito: sopralluogo", Date = DateTimeOffset.Now, Body = invite.ToMessageBody() };
            inviteMail.From.Add(new MailboxAddress("Geometra Verdi", "geometra.verdi@fornitori.example"));
            inviteMail.To.Add(new MailboxAddress("Mario", "mario@studio-prova.it"));
            fake.Add("INBOX", inviteMail);
            fake.Commands.Clear();
            int fresh = await OfflineCopy.RunAsync(imap, inbox, CancellationToken.None);
            Task.WaitAll(BodyCache.TestWrites.ToArray());
            Check(fresh == 2 && fake.Commands.Count(c => c.Contains("BODY.PEEK[]")) == 2, $"ultimi 30 giorni: i messaggi nuovi sul server si scaricano al giro dopo ({fresh} scaricati)");

            // Giro D1: in linea il corpo si apre dalla copia su disco (senza "Non in linea") e l'anticipo lo salta; non un invito,
            // che la copia non tiene (Accetta e Rifiuta vengono dal server). Un provider nuovo, senza i corpi in memoria.
            using var reopened = new ImapProvider(account);
            var rows = (await reopened.GetMessagesAsync(inbox, null, null)).Messages;
            var fromDisk = rows.First(m => m.Subject == "Preventivo nuovo");
            var invitation = rows.First(m => m.Subject == "Invito: sopralluogo");
            fake.Commands.Clear();
            var saved1 = reopened.SavedBody(fromDisk);
            await reopened.PrefetchAsync(rows.Where(m => m.Subject is "Preventivo nuovo" or "Fattura di settembre").ToList());
            Check(saved1 is { OfflineCopy: null } && saved1.Html.Contains("preventivo che aspettavi") && reopened.SavedBody(invitation) == null
                  && !fake.Commands.Any(c => c.Contains("BODY")),
                  "in linea: corpo dalla copia su disco senza la fascia \"Non in linea\" e nessun corpo in anticipo per chi è già sul PC; un invito invece dal server");

            var folder = Path.Combine(Store.Dir, "cache", account.Id, "corpi");
            bool plain = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Any(f => Encoding.UTF8.GetString(File.ReadAllBytes(f)).Contains(secret) || Encoding.UTF8.GetString(File.ReadAllBytes(f)).Contains("Fattura"));
            var byWord = BodyCache.Search(account, secret, null, 50);
            var bySender = BodyCache.Search(account, "da:rossi", null, 50);
            var byFilter = BodyCache.Search(account, "giovedì oggetto:riunione", "INBOX", 50);
            var none = BodyCache.Search(account, "color", null, 50);
            var elsewhere = BodyCache.Search(account, secret, "Clienti", 50);
            Check(!plain && byWord is [{ Subject: "Fattura di settembre" }] && bySender is [{ Subject: "Fattura di settembre" }] && byFilter is [{ Subject: "Riunione di condominio" }]
                  && none.Count == 0 && elsewhere.Count == 0 && byWord[0].From.Name == "Rossi Forniture" && byWord[0].FolderId == "INBOX",
                  "ricerca nella copia sul PC senza rete: parole del testo, da:, oggetto: e la cartella; non gli stili dell'email; nulla in chiaro su disco");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            MailCache.Delete(account);
        }
    }
}
#endif
