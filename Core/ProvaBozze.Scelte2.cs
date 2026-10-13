#if DEBUG
using MimeKit;

namespace Missiva;

/// <summary>
/// Scelte del 12 ottobre 2026 dopo la verifica dei due audit dell'11 ottobre, secondo gruppo (docs\audit\ESITO.md):
/// promemoria del calendario in ritardo, regole di Missiva dopo un errore a metà, un messaggio in una finestra sua dopo un
/// ricarico degli account, la finestra di tempo delle ricerche riusate, le sottocartelle sui server che separano con il
/// punto, le due divergenze fra Gmail e IMAP (scelte 3, 8, 15, 17, 18 e 36 del riepilogo). Solo dati finti, server finti su
/// 127.0.0.1 e orologi simulati; nessuna finestra: delle parti che stanno nelle finestre si provano le funzioni che
/// decidono, e si legge il sorgente senza i commenti (Ch1Code) per vedere che le finestre le chiamino.
/// Con "Missiva.exe --prova-audit risultato.txt scelte2" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void Choices2Tests()
    {
        Report.Add("--- Scelte del 12 ottobre, secondo gruppo: promemoria, regole, finestre, ricerca, cartelle (scelte 3, 8, 15, 17, 18, 36)");
        Ch2LateReminders();
        Ch2RulesAsync().GetAwaiter().GetResult();
        Ch2MessageWindowAsync().GetAwaiter().GetResult();
        Ch2SearchReuseAsync().GetAwaiter().GetResult();
        Ch2FolderSeparator();
        Ch2GmailAttachmentsAsync().GetAwaiter().GetResult();
        Ch2GmailDraftSearchAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Scelta 36, seconda divergenza (C-11): la ricerca della bozza appena scritta, quando la connessione cade dopo APPEND e
    /// il salvataggio riparte. Negli account IMAP la ricerca per Message-Id salta i messaggi segnati \Deleted; in Gmail no.
    /// Il caso che le distingue: fra la caduta e la ripresa un altro programma segna la bozza come eliminata, e il server
    /// non l'ha ancora tolta. Con il server finto: DropAfterAppend, e Hold per segnare \Deleted proprio quando arriva la
    /// ricerca. Gmail e IMAP, lo stesso caso.
    /// </summary>
    static async Task Ch2GmailDraftSearchAsync()
    {
        // Salva una bozza con la connessione che cade dopo APPEND; alla ripresa la bozza appena scritta è segnata \Deleted.
        static async Task<(int Appends, bool Searched, int Stored, bool ReturnedDeleted, bool ReturnedLast, string Html)> SaveAsync(FakeImap fake, IMailProvider provider)
        {
            fake.Commands.Clear();
            fake.DropAfterAppend = true;
            fake.Hold = command =>
            {
                if (command != "UID SEARCH" || fake.Stored.Count == 0) return Task.CompletedTask;
                fake.Hold = null; // solo la prima ricerca
                fake.Stored[0].Set("\\Deleted", true);
                return Task.CompletedTask;
            };
            try
            {
                var saved = await provider.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Bozza di prova", Html = "<p>Testo della bozza</p>", Text = "Testo della bozza" });
                var returned = fake.Stored.FirstOrDefault(m => m.Uid.ToString() == saved.Message.Id);
                return (fake.Commands.Count(c => c.StartsWith("APPEND")), fake.Commands.Any(c => c.StartsWith("UID SEARCH") && c.Contains("HEADER")), fake.Stored.Count,
                        returned?.Has("\\Deleted") != false, returned != null && returned == fake.Stored[^1], returned == null ? "" : MimeMessage.Load(new MemoryStream(returned.Raw)).HtmlBody ?? "");
            }
            finally
            {
                fake.Hold = null;
                fake.DropAfterAppend = false;
            }
        }
        (int Appends, bool Searched, int Stored, bool ReturnedDeleted, bool ReturnedLast, string Html) gmail, imap;
        try
        {
            using var fake = new FakeImap();
            GmailProvider.TestPort = fake.Port;
            using var provider = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "gmail-scelta36-bozza" });
            gmail = await SaveAsync(fake, provider);
        }
        finally
        {
            GmailProvider.TestPort = null;
        }
        ImapProvider.TestPlain = true;
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            using var provider = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            imap = await SaveAsync(fake, provider);
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
        Check(gmail is { Appends: 2, Searched: true, Stored: 2, ReturnedDeleted: false, ReturnedLast: true } && gmail.Html.Contains("Testo della bozza") && imap == gmail,
              $"scelta 36 (C-11), Gmail, una bozza salvata con la connessione che cade dopo APPEND, e alla ripresa la bozza appena scritta è segnata \\Deleted da un altro programma: la ricerca per Message-Id non la prende, " +
              $"la bozza si riscrive ({gmail.Appends} APPEND) e quella restituita alla finestra è la nuova, non eliminata ({!gmail.ReturnedDeleted}), come negli account IMAP ({imap.Appends} APPEND, non eliminata: {!imap.ReturnedDeleted}). " +
              "Prima Gmail restituiva quella segnata \\Deleted, senza riscriverla: al primo EXPUNGE la bozza spariva");
    }

    /// <summary>
    /// Scelta 36, prima divergenza (C-08): per leggere gli allegati di un messaggio inoltrato o di una bozza ripresa, sulla
    /// connessione delle bozze, Gmail apriva la cartella con SELECT e gli account IMAP con EXAMINE. Ora anche Gmail in sola
    /// lettura. Sul server finto, guardando i comandi della connessione su cui arriva la lettura dell'allegato e i segni
    /// del messaggio prima e dopo: \Seen non cambia in nessuno dei due modi (MailKit legge con BODY.PEEK); \Recent, che un
    /// server vero toglie con SELECT, resta. L'apertura dal riquadro di lettura, sulla connessione principale, resta SELECT.
    /// </summary>
    static async Task Ch2GmailAttachmentsAsync()
    {
        static MimeMessage WithFile(string subject, string content)
        {
            var builder = new BodyBuilder { HtmlBody = "<p>In allegato il preventivo</p>" };
            builder.Attachments.Add("preventivo.txt", System.Text.Encoding.UTF8.GetBytes(content), new ContentType("text", "plain"));
            var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now.AddMinutes(-5), Body = builder.ToMessageBody() };
            mime.From.Add(new MailboxAddress("Maria Rossi", "maria.rossi@fornitori-prova.example"));
            mime.To.Add(new MailboxAddress("Mario Rossi", "prova@example.com"));
            return mime;
        }
        // Come è stata aperta la cartella sulla connessione su cui è arrivata la lettura dell'allegato (BODY.PEEK[2]).
        static string OpenedFor(FakeImap fake, string folder)
        {
            List<(int Connection, string Text)> traffic;
            lock (fake.Traffic) traffic = fake.Traffic.ToList();
            int read = traffic.FindLastIndex(t => t.Text.Contains("BODY.PEEK[2]"));
            if (read < 0) return "allegato mai letto";
            var opened = traffic.Take(read).LastOrDefault(t => t.Connection == traffic[read].Connection && (t.Text.StartsWith("SELECT") || t.Text.StartsWith("EXAMINE")));
            return opened.Text == null ? "cartella mai aperta" : $"{opened.Text.Split(' ')[0]} {(opened.Text.Contains(folder) ? "della cartella" : "di un'altra cartella")}";
        }
        // Inoltra dalla Posta in arrivo, salvato come bozza: l'allegato dell'originale si legge sulla connessione delle bozze.
        static OutgoingMessage Forward(MessageSummary original) => new()
        {
            Mode = ComposeMode.Forward, To = To, Subject = "I: Preventivo di prova", Html = "<p>Ti giro il preventivo</p>", Text = "Ti giro il preventivo", Original = original,
            OriginalBody = new MessageBody { Html = "<p>In allegato il preventivo</p>", Attachments = [new Attachment { Id = "2", Name = "preventivo.txt", ContentType = "text/plain", Size = 21 }] },
        };
        string gmailOpen, gmailFlags, paneOpen, resumedOpen, imapOpen;
        bool gmailRecent, gmailSame, paneSame, resumedSame, imapRecent, imapSame;
        int resumedDrafts;
        try
        {
            using var fake = new FakeImap();
            GmailProvider.TestPort = fake.Port;
            using var gmail = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "gmail-scelta36" });
            var stored = fake.Add("INBOX", WithFile("Preventivo di prova", "preventivo di ottobre"));
            stored.Recent = true; // arrivato, e nessun programma ha ancora aperto la cartella
            var original = new MessageSummary { Id = stored.Uid.ToString(), FolderId = "INBOX", Subject = "Preventivo di prova", From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" }, Date = DateTime.Now.AddMinutes(-5) };
            var draft = await gmail.SaveDraftAsync(Forward(original));
            (gmailOpen, gmailFlags, gmailRecent) = (OpenedFor(fake, "INBOX"), stored.Flags, stored.Recent);
            gmailSame = fake.Stored.Count == 1 && AttachmentText(fake.Stored[0]) == "preventivo di ottobre";

            // La bozza ripresa dalle Bozze: i suoi allegati sono solo sul server, e si rileggono sulla stessa connessione.
            // Sulla connessione delle bozze le Bozze sono già aperte in lettura e scrittura dal salvataggio di prima: restano così.
            static SavedDraft Resumed(SavedDraft saved) =>
                new() { Message = saved.Message, Attachments = saved.Attachments.Select(a => new Attachment { Id = a.Id, Name = a.Name, ContentType = a.ContentType, Size = a.Size }).ToList() };
            var resumed = Resumed(draft);
            var second = await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "I: Preventivo di prova", Html = "<p>Seconda</p>", Text = "Seconda", Draft = resumed, KeptAttachments = resumed.Attachments });
            resumedOpen = OpenedFor(fake, "Bozze");
            (resumedDrafts, resumedSame) = (fake.Stored.Count, fake.Stored.Count == 1 && AttachmentText(fake.Stored[0]) == "preventivo di ottobre");

            // Misura: la stessa bozza ripresa dopo un riavvio (connessioni nuove). Le Bozze si aprono per leggere l'allegato e poi per scrivere.
            using (var restarted = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "gmail-scelta36" }))
            {
                var again = Resumed(second);
                lock (fake.Traffic) fake.Traffic.Clear();
                await restarted.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "I: Preventivo di prova", Html = "<p>Terza</p>", Text = "Terza", Draft = again, KeptAttachments = again.Attachments });
                List<string> opens;
                lock (fake.Traffic) opens = fake.Traffic.Where(t => t.Text.StartsWith("SELECT") || t.Text.StartsWith("EXAMINE")).Select(t => t.Text.Split(' ')[0]).ToList();
                Report.Add($"      (scelta 36, misura: una bozza con un allegato ripresa dopo un riavvio, al primo salvataggio le Bozze si aprono così: {string.Join(", ", opens)}; " +
                           $"con SELECT per leggere l'allegato era un comando solo. Resta una bozza sola, uguale: {fake.Stored.Count == 1 && AttachmentText(fake.Stored[0]) == "preventivo di ottobre"})");
            }

            // Dal riquadro di lettura (Apri, Salva con nome): sulla connessione principale, in lettura e scrittura come le azioni. Com'era.
            lock (fake.Traffic) fake.Traffic.Clear();
            var bytes = await gmail.GetAttachmentAsync(original, new Attachment { Id = "2", Name = "preventivo.txt", ContentType = "text/plain", Size = 21 });
            (paneOpen, paneSame) = (OpenedFor(fake, "INBOX"), System.Text.Encoding.UTF8.GetString(bytes) == "preventivo di ottobre");
        }
        finally
        {
            GmailProvider.TestPort = null;
        }
        // Lo stesso inoltro con un account IMAP: il termine di paragone, già in sola lettura.
        ImapProvider.TestPlain = true;
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            using var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            var stored = fake.Add("INBOX", WithFile("Preventivo di prova", "preventivo di ottobre"));
            stored.Recent = true;
            var original = new MessageSummary { Id = stored.Uid.ToString(), FolderId = "INBOX", Subject = "Preventivo di prova", From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" }, Date = DateTime.Now.AddMinutes(-5) };
            await imap.SaveDraftAsync(Forward(original));
            (imapOpen, imapRecent) = (OpenedFor(fake, "INBOX"), stored.Recent);
            imapSame = fake.Stored.Count == 1 && AttachmentText(fake.Stored[0]) == "preventivo di ottobre";
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
        Check(gmailOpen == "EXAMINE della cartella" && gmailFlags == "" && gmailRecent && gmailSame && imapOpen == gmailOpen && imapRecent && imapSame,
              $"scelta 36 (C-08), Gmail, l'allegato di un messaggio inoltrato si legge con la cartella in sola lettura, come negli account IMAP ({gmailOpen}; IMAP: {imapOpen}): l'allegato arriva nella bozza uguale, " +
              $"e sul server finto il messaggio resta com'era, senza \\Seen (segni: \"{gmailFlags}\") e ancora \\Recent ({gmailRecent}). Prima Gmail apriva con SELECT: \\Seen non cambiava nemmeno allora (BODY.PEEK), \\Recent sì");
        Check(resumedOpen == "SELECT della cartella" && resumedSame && resumedDrafts == 1 && paneOpen == "SELECT della cartella" && paneSame,
              $"scelta 36 (C-08), Gmail, quello che non cambia: la bozza ripresa rilegge il suo allegato dalle Bozze già aperte per scrivere ({resumedOpen}) e resta una sola, uguale ({resumedDrafts} bozza); " +
              $"dal riquadro di lettura l'allegato si legge sulla connessione principale, in lettura e scrittura come le azioni ({paneOpen})");
    }

    /// <summary>
    /// Scelta 18 (trovata con C-47): rinominando o spostando una cartella, Preferiti, cartelle nascoste e chiuse, colori e
    /// ordine devono seguire anche le sue sottocartelle. MainWindow.MigrateFolderKeys le riconosceva dalla barra: su un
    /// server IMAP che separa con il punto (INBOX.Clienti.Rossi) non seguivano, e una cartella con una barra nel nome
    /// passava per sottocartella. Ora il separatore è quello dell'account, letto dalle sue cartelle. Sulle impostazioni, con
    /// le cartelle come le dà ImapProvider; nessun server.
    /// </summary>
    static void Ch2FolderSeparator()
    {
        var s = Store.Settings;
        const string id = "prova-scelta18", other = "altro-scelta18", slash = "barra-scelta18";
        static MailFolder Folder(string path, char separator, FolderKind kind = FolderKind.Other)
        {
            int cut = path.LastIndexOf(separator);
            return new MailFolder { Id = path, Name = path[(cut + 1)..], Kind = kind, ParentId = kind == FolderKind.Other && cut > 0 ? path[..cut] : null };
        }
        using var dotted = new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Id = id, Imap = new ImapServer() });
        using var slashed = new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "maria@studio-prova.example", Id = slash, Imap = new ImapServer() });
        using var flat = new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "ufficio@studio-prova.example", Id = other, Imap = new ImapServer() });
        void Forget()
        {
            bool Mine(string key) => key.Contains("-scelta18|");
            s.Favorites.RemoveAll(Mine);
            s.HiddenFolders.RemoveAll(Mine);
            s.CollapsedFolders.RemoveAll(Mine);
            foreach (var key in s.FolderColors.Keys.Where(Mine).ToList()) s.FolderColors.Remove(key);
            foreach (var key in new[] { id, other, slash }) s.FolderOrder.Remove(key);
        }
        try
        {
            Forget();
            // Un server che separa con il punto, con una cartella dal nome che comincia uguale e una con una barra nel nome.
            var account = new UI.AccountItem(dotted);
            account.SetFolders([Folder("INBOX", '.', FolderKind.Inbox), Folder("INBOX.Bozze", '.', FolderKind.Drafts), Folder("INBOX.Clienti", '.'), Folder("INBOX.Clienti.Rossi", '.'),
                                Folder("INBOX.Clienti.Rossi.2026", '.'), Folder("INBOX.Clienti2", '.'), Folder("INBOX.Clienti/Vecchi", '.')]);
            s.Favorites.AddRange([$"{id}|INBOX.Clienti", $"{id}|INBOX.Clienti.Rossi", $"{id}|INBOX.Clienti2", $"{id}|INBOX.Clienti/Vecchi", $"{other}|INBOX.Clienti.Rossi"]);
            s.HiddenFolders.Add($"{id}|INBOX.Clienti.Rossi.2026");
            s.CollapsedFolders.Add($"{id}|INBOX.Clienti.Rossi");
            (s.FolderColors[$"{id}|INBOX.Clienti"], s.FolderColors[$"{id}|INBOX.Clienti.Rossi"]) = (3, 4);
            s.FolderOrder[id] = ["INBOX", "INBOX.Clienti", "INBOX.Clienti.Rossi", "INBOX.Clienti2"];
            char separator = UI.MainWindow.FolderSeparator(account);
            UI.MainWindow.MigrateFolderKeys(account, "INBOX.Clienti", "INBOX.Fornitori");
            string favorites = string.Join(", ", s.Favorites.Where(f => f.Contains("-scelta18|")).Select(f => f[(f.IndexOf('|') + 1)..]));
            string hidden = string.Join(", ", s.HiddenFolders.Where(f => f.StartsWith(id + "|")).Select(f => f[(id.Length + 1)..])), closed = string.Join(", ", s.CollapsedFolders.Where(f => f.StartsWith(id + "|")).Select(f => f[(id.Length + 1)..]));
            string colors = string.Join(", ", s.FolderColors.Where(c => c.Key.StartsWith(id + "|")).OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key[(id.Length + 1)..]}={c.Value}"));
            string order = string.Join(", ", s.FolderOrder[id]);
            Check(separator == '.' && favorites == "INBOX.Fornitori, INBOX.Fornitori.Rossi, INBOX.Clienti2, INBOX.Clienti/Vecchi, INBOX.Clienti.Rossi" && hidden == "INBOX.Fornitori.Rossi.2026" && closed == "INBOX.Fornitori.Rossi"
                  && colors == "INBOX.Fornitori=3, INBOX.Fornitori.Rossi=4" && order == "INBOX, INBOX.Fornitori, INBOX.Fornitori.Rossi, INBOX.Clienti2",
                  $"scelta 18, su un server che separa con il punto, rinominata INBOX.Clienti in INBOX.Fornitori: le sottocartelle seguono nei Preferiti ({favorites}), fra le nascoste ({hidden}) e le chiuse ({closed}), nei colori ({colors}) " +
                  $"e nell'ordine ({order}); INBOX.Clienti2, una cartella con una barra nel nome e la stessa cartella di un altro account restano. Prima seguiva solo INBOX.Clienti, e \"INBOX.Clienti/Vecchi\" passava per una sua sottocartella");

            // Il separatore letto dalle cartelle: la barra dove le sottocartelle sono scritte con la barra, e senza sottocartelle; lì tutto come prima.
            var withSlash = new UI.AccountItem(slashed);
            withSlash.SetFolders([Folder("INBOX", '/', FolderKind.Inbox), Folder("Lavoro", '/'), Folder("Lavoro/Clienti", '/'), Folder("Lavoro.2026", '/')]);
            var noChildren = new UI.AccountItem(flat);
            noChildren.SetFolders([Folder("INBOX", '/', FolderKind.Inbox), Folder("Lavoro", '/')]);
            s.Favorites.AddRange([$"{slash}|Lavoro", $"{slash}|Lavoro/Clienti", $"{slash}|Lavoro.2026"]);
            UI.MainWindow.MigrateFolderKeys(withSlash, "Lavoro", "Progetti");
            string slashFavorites = string.Join(", ", s.Favorites.Where(f => f.StartsWith(slash + "|")).Select(f => f[(slash.Length + 1)..]));
            Check(UI.MainWindow.FolderSeparator(withSlash) == '/' && UI.MainWindow.FolderSeparator(noChildren) == '/' && slashFavorites == "Progetti, Progetti/Clienti, Lavoro.2026",
                  $"scelta 18, dove le sottocartelle sono scritte con la barra (Gmail, molti server IMAP) nulla cambia: rinominata Lavoro in Progetti segue Lavoro/Clienti e non Lavoro.2026 ({slashFavorites}); senza sottocartelle il separatore è la barra");
        }
        finally
        {
            Forget();
            Store.Save();
        }
    }

    /// <summary>
    /// Scelta 8 (C-39): le due finestre di tempo in cui una ricerca già fatta si riusa. Invio riusava quella dei
    /// suggerimenti per 30 secondi "e poi si rifà"; ma su un account IMAP la ricerca rifatta con le stesse parole riceveva
    /// quella di prima fino a 60 secondi dall'inizio. Ora una finestra sola, MailLimits.SearchReuseSeconds (30). Con
    /// l'orologio di SuggestSearches spostato a mano e la ricerca di IMAP invecchiata (ImapProvider.TestAgeSearch), sul
    /// server IMAP finto, contando le SEARCH.
    /// </summary>
    static async Task Ch2SearchReuseAsync()
    {
        // I suggerimenti: una ricerca finita vale per Invio a 30 secondi, non a 31 (com'era).
        var searches = new UI.SuggestSearches();
        long clock = 500_000;
        searches.Clock = () => clock;
        int asked = 0;
        Task<MessagePage> Server(CancellationToken _)
        {
            asked++;
            return Task.FromResult(new MessagePage());
        }
        var key = UI.SuggestSearches.Key("prova", null, "fattura");
        _ = searches.Start(key, Server);
        clock += 30_000;
        bool within = searches.Take(key) != null;
        _ = searches.Start(key, Server);
        clock += 31_000;
        bool beyond = searches.Take(key) != null;

        // IMAP, in tutte le cartelle: le stesse parole a 20 secondi riusano, a 31 la ricerca si rifà davvero. Venti e non
        // ventinove: l'età è quella vera più quella aggiunta, e un secondo perso dal PC non deve far cadere il controllo.
        int first, again, at20, at31, folders;
        ImapProvider.TestPlain = true;
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            fake.Add("INBOX", SpeedMail("Fattura di prova", "In allegato la fattura"));
            using var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            await imap.GetFoldersAsync();
            folders = fake.Folders.Count - 2; // tutte tranne Cestino e Posta indesiderata
            static int Searches(FakeImap fake) => fake.Commands.Count(c => c.StartsWith("UID SEARCH"));
            async Task<int> SearchAsync()
            {
                fake.Commands.Clear();
                await imap.SearchAsync(null, "fattura");
                return Searches(fake);
            }
            first = await SearchAsync();
            again = await SearchAsync();
            imap.TestAgeSearch(TimeSpan.FromSeconds(20));
            at20 = await SearchAsync();
            imap.TestAgeSearch(TimeSpan.FromSeconds(11)); // ora ha 31 secondi
            at31 = await SearchAsync();
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
        Check(MailLimits.SearchReuseSeconds == 30 && UI.SuggestSearches.Fresh == TimeSpan.FromSeconds(30) && ImapProvider.ReuseSearch == UI.SuggestSearches.Fresh
              && within && !beyond && asked == 2 && first == folders && again == 0 && at20 == 0 && at31 == folders,
              $"scelta 8 (C-39), una finestra sola di {MailLimits.SearchReuseSeconds} secondi per le ricerche riusate: Invio riusa quella dei suggerimenti a 30 secondi ({within}) e non a 31 ({beyond}); " +
              $"su IMAP le stesse parole riusano la ricerca in tutte le cartelle subito e a 20 secondi ({again} e {at20} SEARCH), e a 31 si rifà davvero ({at31} SEARCH, una per cartella come la prima: {first}). " +
              "Prima fra 30 e 60 secondi la ricerca \"rifatta\" su IMAP era quella di prima");
        if (Ch1Code("Core", "ImapProvider.cs") is { } imapSource && Ch1Code("UI", "MainWindow.Suggestions.cs") is { } suggestions)
            Check(imapSource.Contains("internal static readonly TimeSpan ReuseSearch = TimeSpan.FromSeconds(MailLimits.SearchReuseSeconds);")
                  && suggestions.Contains("internal static readonly TimeSpan Fresh = TimeSpan.FromSeconds(MailLimits.SearchReuseSeconds);") && imapSource.Contains("DateTime.UtcNow - search.Started > ReuseSearch"),
                  "scelta 8 (C-39), le due finestre leggono lo stesso numero, MailLimits.SearchReuseSeconds: cambiarne una sola non si può più (lettura del sorgente)");
    }

    /// <summary>
    /// Scelta 17 (X-1): un messaggio aperto in una finestra sua mentre gli account si ricaricano (Aggiungi account, Aggiungi
    /// casella condivisa, Rimuovi account). La finestra teneva l'account di prima, con il provider chiuso: ogni azione dava
    /// l'errore che dice di chiudere e riaprire. Ora la barra della finestra (MainWindow.CurrentItem) e il suo riquadro
    /// (ReadingView.provider) passano al provider nuovo dello stesso account per la strada della scrittura
    /// (ComposeWindow.Current). Sul server IMAP finto, con CurrentItem chiamata come fa la finestra; la finestra non si
    /// apre: che la barra e il riquadro passino di lì si legge dal sorgente.
    /// </summary>
    static async Task Ch2MessageWindowAsync()
    {
        static async Task<string> Try(Func<Task> request)
        {
            try
            {
                await request();
                return "riuscito";
            }
            catch (MailException ex) { return ex.Message; }
            catch (Exception ex) { return ex.GetType().Name; }
        }
        ImapProvider.TestPlain = true;
        MailAccount? listed = null;
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            var stored = fake.Add("INBOX", SpeedMail("Messaggio di prova", "Buongiorno"));
            var account = SpeedAccount(fake, "mario@studio-prova.example");
            Store.Settings.Accounts.Add(listed = account); // l'account c'è fra quelli di Missiva, come quando si aggiunge un altro account
            var old = new ImapProvider(account);
            var inbox = (await old.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var before = new UI.AccountItem(old);
            var item = new UI.MessageItem((await old.GetMessagesAsync(inbox, null, null)).Messages.Single(), before);
            // Finché il provider è aperto la finestra resta sulla sua riga, anche se ce ne fosse un altro dello stesso account.
            using var other = new ImapProvider(account);
            bool untouched = UI.MainWindow.CurrentItem(item, [before, new UI.AccountItem(other)], [], p => UI.ComposeWindow.Current(p, _ => other)) == item;

            // Gli account si ricaricano (MainWindow.ListAccountsAsync): provider di prima chiusi, account e righe nuovi.
            old.Dispose();
            await Task.Delay(300);
            using var fresh = new ImapProvider(account);
            var after = new UI.AccountItem(fresh);
            var row = new UI.MessageItem((await fresh.GetMessagesAsync(inbox, null, null)).Messages.Single(), after);
            IMailProvider Reloaded(IMailProvider p) => UI.ComposeWindow.Current(p, id => id == account.Id ? fresh : null);
            string stale = await Try(() => item.Provider.SetReadAsync([item.Summary], true)); // com'era: l'azione sul provider della finestra
            bool readStale = stored.Has("\\Seen");
            var now = UI.MainWindow.CurrentItem(item, [after], [row], Reloaded);
            var alone = UI.MainWindow.CurrentItem(item, [after], [], Reloaded); // il messaggio non è nell'elenco di adesso: una riga nuova sullo stesso messaggio
            string moved = await Try(() => now.Provider.SetReadAsync([now.Summary], true));
            bool read = stored.Has("\\Seen");
            string flagged = await Try(() => alone.Provider.SetFlagAsync([alone.Summary], true));
            Check(untouched && stale == MailException.ClosedText(account.Email, removed: false) && stale.Contains("gli account sono stati ricaricati") && stale.Contains("Chiudi la finestra e riaprila") && !readStale
                  && now == row && alone != item && alone.Account == after && alone.Summary == item.Summary && moved == "riuscito" && read && flagged == "riuscito" && stored.Has("\\Flagged"),
                  "scelta 17 (X-1), un messaggio aperto in una finestra sua mentre gli account si ricaricano: la barra della finestra passa alla riga dell'account di adesso (quella dell'elenco, o una nuova sullo stesso messaggio) " +
                  $"e da lì Già letto e Contrassegna arrivano al server ({moved}, {flagged}); un provider aperto non si cambia mai. Prima l'azione dava: \"{stale}\"");

            // L'account rimosso: non c'è un provider nuovo, la finestra resta com'è e l'errore dice perché.
            Store.Settings.Accounts.Remove(account);
            fresh.Dispose();
            await Task.Delay(300);
            var gone = UI.MainWindow.CurrentItem(item, [], [], p => UI.ComposeWindow.Current(p, _ => null));
            string removed = await Try(() => gone.Provider.SetFlagAsync([gone.Summary], false));
            Check(gone == item && removed == MailException.ClosedText(account.Email, removed: true) && removed.Contains("è stato chiuso: l'account è stato rimosso da Missiva") && !removed.Contains("riaprila") && stored.Has("\\Flagged"),
                  $"scelta 17 (X-1), se l'account della finestra è quello rimosso la finestra resta sulla riga di prima e l'azione dà l'errore, che dice perché e non propone più di riaprirla (\"{removed}\")");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            if (listed != null) Store.Settings.Accounts.Remove(listed);
        }

        // Una strada sola (lettura del sorgente, senza i commenti: le finestre non si aprono).
        if (Ch1Code("UI", "ReadingView.xaml.cs") is { } reading && Ch1Code("UI", "MainWindow.xaml.cs") is { } main && SourceRoot() is { } root)
        {
            bool pane = reading.Contains("IMailProvider? provider => opener is { } known ? ComposeWindow.Current(known) : null;") && reading.Contains("opener = p;")
                        && System.Text.RegularExpressions.Regex.Matches(reading, @"\bopener\b").Count == 3;
            int from = main.IndexOf("StackPanel MessageWindowBar(MessageItem item, ReadingView reader, Window window)", StringComparison.Ordinal), to = main.IndexOf("MessageItem CurrentItem(MessageItem item) =>", StringComparison.Ordinal);
            string bar = from >= 0 && to > from ? main[from..to] : "";
            string[] handlers =
            [
                "MessageItem Now() => item = CurrentItem(item);", "await DeleteFromWindowAsync(Now(), window);", "await ArchiveAsync([Now()])", "in Now().Account.Folders.Where(", "await MoveAsync([Now()], _ => target, \"spostamento\")",
                "await SetReadAsync([Now()], !item.IsRead);", "await ToggleFlagAsync([Now()]);", "OpenMessageMenu(Now(), reader, more, null);", "ShowInMainWindow(Now());",
            ];
            var missing = handlers.Where(h => !bar.Contains(h)).ToList();
            bool barNow = bar.Length > 0 && missing.Count == 0 && !bar.Contains("[item]") && main.Contains("MessageItem CurrentItem(MessageItem item) => CurrentItem(item, accounts, rows.Rows, ComposeWindow.Current);");
            // Chi decide se un provider è chiuso e quale lo sostituisce è solo ComposeWindow.Current: nessun'altra finestra guarda IsClosed.
            var deciding = Directory.GetFiles(Path.Combine(root, "UI"), "*.cs").Where(f => Ch1Code("UI", Path.GetFileName(f))?.Contains("IsClosed") == true).Select(Path.GetFileName).ToList();
            Check(pane && barNow && deciding is ["ComposeWindow.xaml.cs"],
                  $"scelta 17 (X-1), una strada sola: il riquadro del messaggio prende il provider da ComposeWindow.Current a ogni uso ({pane}), i comandi della barra della finestra passano da CurrentItem, che lo chiede a ComposeWindow.Current " +
                  $"({barNow}{(missing.Count > 0 ? ", mancano: " + string.Join(" | ", missing) : "")}), e in tutta l'interfaccia solo ComposeWindow guarda se un provider è chiuso ({string.Join(", ", deciding)})");
        }
    }

    /// <summary>
    /// Scelta 15 (C-51): le regole di Missiva dopo un errore a metà. Prima il primo errore fermava tutto, e nessun messaggio
    /// di quel giro passava più dalle regole, nemmeno quelli mai toccati. Ora ogni gruppo di azioni va per conto suo, l'errore
    /// è nell'esito, e i messaggi non finiti si riprovano al controllo dopo: solo le azioni che restano, mai una copia già
    /// partita né uno spostamento che il server non ha rifiutato per intero, e al più tre volte in tutto. Sul server IMAP
    /// finto (con MOVE e senza) e su quello di Gmail, facendo rifiutare un comando con RefuseOnce.
    /// </summary>
    static async Task Ch2RulesAsync()
    {
        int seenBefore = Store.Settings.RulesSeen.Count;
        var accounts = new List<string>();
        var since = DateTime.UtcNow.AddMinutes(-10);
        static void Mail(FakeImap fake, string subject, string from = "fatture@fornitore-prova.example", int minutesAgo = 1)
        {
            var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now.AddMinutes(-minutesAgo), Body = new TextPart("html") { Text = "<p>In allegato la fattura</p>" } };
            mime.From.Add(new MailboxAddress("Fornitore di prova", from));
            mime.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.example"));
            fake.Add("INBOX", mime);
        }
        // Un errore sollevato dalle regole (com'era prima: il primo errore fermava tutto) qui è un esito che manca, non un gruppo che si ferma.
        static async Task<Rules.Outcome?> Apply(IMailProvider provider, DateTime since, Func<Task<List<MailFolder>>> folders)
        {
            try { return await Rules.ApplyToNewAsync(provider, since, folders); }
            catch (Exception ex)
            {
                Report.Add($"      (scelta 15: le regole hanno sollevato {ex.GetType().Name})");
                return null;
            }
        }
        static int Sent(FakeImap fake, string command) => fake.Commands.Count(c => c.StartsWith(command + " ", StringComparison.Ordinal));
        static string Texts(Rules.Outcome? outcome) => outcome == null ? "nessun messaggio da guardare" : outcome.Errors.Count == 0 ? "nessun errore" : string.Join("; ", outcome.Errors);
        MailRule Rule(MailAccount account, MailFolder? moveTo = null, MailFolder? copyTo = null, bool read = false, bool flag = false, string sender = "fornitore-prova")
        {
            var rule = new MailRule { AccountId = account.Id, Name = "Regola di prova", SenderContains = [sender], MarkRead = read, Flag = flag, MoveTo = moveTo?.Id ?? "", MoveToName = moveTo?.Name ?? "", CopyTo = copyTo?.Id ?? "", CopyToName = copyTo?.Name ?? "" };
            Store.Settings.Rules.Add(rule);
            if (!accounts.Contains(account.Id)) accounts.Add(account.Id);
            return rule;
        }
        ImapProvider.TestPlain = true;
        try
        {
            Rules.ForgetMarks();
            // ---------------------------------------------------------------- il caso misurato dall'audit: NO al primo UID STORE
            using (var fake = new FakeImap(new Flavor(Gmail: false)))
            {
                var account = SpeedAccount(fake, "mario@studio-prova.example");
                using var imap = new ImapProvider(account);
                for (int i = 1; i <= 3; i++) Mail(fake, $"Fattura n. {i} di prova", minutesAgo: i);
                var folders = await imap.GetFoldersAsync();
                var target = folders.First(f => f.Id == fake.UserFolder);
                Rule(account, moveTo: target, read: true);
                fake.RefuseOnce = "UID STORE";
                fake.Commands.Clear();
                var first = await Apply(imap, since, () => Task.FromResult(folders));
                int firstStore = Sent(fake, "UID STORE"), firstMove = Sent(fake, "UID MOVE"), movedFirst = fake.Folders[fake.UserFolder].Count, waiting = Rules.TestUnfinished(account.Id);
                fake.Commands.Clear();
                var second = await Apply(imap, DateTime.UtcNow, () => Task.FromResult(folders));
                int secondStore = Sent(fake, "UID STORE"), secondMove = Sent(fake, "UID MOVE"), movedSecond = fake.Folders[fake.UserFolder].Count;
                bool allRead = fake.Folders[fake.UserFolder].All(m => m.Has("\\Seen"));
                fake.Commands.Clear();
                var third = await Apply(imap, DateTime.UtcNow, () => Task.FromResult(folders));
                int thirdWrites = fake.Commands.Count(c => c.StartsWith("UID STORE") || c.StartsWith("UID MOVE") || c.StartsWith("UID COPY"));
                Check(first is { Matched: 3, Errors.Count: 2, Removed.Count: 0, Read.Count: 0, Pending.Count: 3 } && firstStore == 1 && firstMove == 0 && movedFirst == 0 && waiting == 3
                      && first.Errors[0].StartsWith("3 non segnati come letti: ") && first.Errors[1].StartsWith("3 lasciati dove sono, non spostati in ")
                      && second is { Matched: 3, Errors.Count: 0, Removed.Count: 3, Read.Count: 3, Pending.Count: 0 } && secondStore == 1 && secondMove == 1 && movedSecond == 3 && allRead
                      && fake.Folders["INBOX"].Count == 0 && Rules.TestUnfinished(account.Id) == 0 && third == null && thirdWrites == 0,
                      $"scelta 15 (C-51), tre messaggi nuovi e una regola \"segna come letto e sposta\", con il server che rifiuta il primo UID STORE: al primo controllo nessun errore sollevato, l'esito dice che cosa non è riuscito ({Texts(first)}), " +
                      $"nessuno spostamento parte ({firstMove} UID MOVE) e i {waiting} messaggi restano da riprovare; al controllo dopo un UID STORE e un UID MOVE, spostati {movedSecond} di 3, tutti letti; a quello dopo ancora nulla ({thirdWrites} comandi di scrittura). " +
                      "Prima: un errore al primo controllo, e 0 spostati di 3 per sempre");

            }

            // ---------------------------------------------------------------- Esegui ora con lo stesso rifiuto: nessuna eccezione, e il testo dice che cosa è riuscito e che cosa no
            using (var fake = new FakeImap(new Flavor(Gmail: false)))
            {
                var account = SpeedAccount(fake, "mario@studio-prova.example");
                using var imap = new ImapProvider(account);
                for (int i = 1; i <= 2; i++) Mail(fake, $"Fattura n. {i} di prova");
                var folders = await imap.GetFoldersAsync();
                var target = folders.First(f => f.Id == fake.UserFolder);
                var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
                var listed = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
                var now = new MailRule { AccountId = account.Id, Name = "Fatture di prova", SenderContains = ["fornitore-prova"], MarkRead = true, Flag = true, MoveTo = target.Id, MoveToName = target.Name };
                fake.RefuseOnce = "UID STORE";
                string text;
                try { text = UI.RulesView.RunResult(now.Name, inbox.Name, listed.Count, await Rules.RunAsync(imap, listed, [now], () => Task.FromResult(folders), confirm: _ => Task.FromResult(true))); }
                catch (Exception ex) { text = "errore " + ex.GetType().Name; }
                Check(text.StartsWith($"Esegui ora \"Fatture di prova\" su {inbox.Name}: 2 dei 2 messaggi corrispondono; 2 contrassegnati. Non riuscito: 2 non segnati come letti: ") && text.Contains("; 2 lasciati dove sono, non spostati in ")
                      && fake.Folders["INBOX"].Count == 2 && fake.Folders["INBOX"].All(m => m.Has("\\Flagged") && !m.Has("\\Seen")),
                      $"scelta 15 (C-51), Esegui ora con lo stesso rifiuto: nessun errore sollevato, le azioni dopo quella rifiutata partono (contrassegnati) e il testo dice che cosa non è riuscito (\"{text}\"); prima si vedeva solo \"Esegui ora non riuscito\"");
            }

            // ---------------------------------------------------------------- i messaggi mai toccati: un altro gruppo di azioni parte lo stesso
            using (var fake = new FakeImap(new Flavor(Gmail: false)))
            {
                var account = SpeedAccount(fake, "mario@studio-prova.example");
                using var imap = new ImapProvider(account);
                for (int i = 1; i <= 2; i++) Mail(fake, $"Fattura n. {i} di prova", "fatture@fornitore-a.example");
                for (int i = 1; i <= 2; i++) Mail(fake, $"Ordine n. {i} di prova", "ordini@fornitore-b.example");
                var folders = await imap.GetFoldersAsync();
                var target = folders.First(f => f.Id == fake.UserFolder);
                Rule(account, moveTo: target, read: true, sender: "fornitore-a");
                Rule(account, flag: true, sender: "fornitore-b");
                fake.RefuseOnce = "UID STORE";
                var first = await Apply(imap, since, () => Task.FromResult(folders));
                int flaggedFirst = fake.Folders["INBOX"].Count(m => m.Has("\\Flagged")), inboxFirst = fake.Folders["INBOX"].Count;
                fake.Commands.Clear();
                var second = await Apply(imap, DateTime.UtcNow, () => Task.FromResult(folders));
                int secondStore = Sent(fake, "UID STORE"), secondMove = Sent(fake, "UID MOVE");
                Check(first is { Matched: 4, Flagged.Count: 2, Removed.Count: 0, Pending.Count: 2 } && flaggedFirst == 2 && inboxFirst == 4
                      && second is { Matched: 2, Errors.Count: 0, Removed.Count: 2, Flagged.Count: 0 } && secondStore == 1 && secondMove == 1
                      && fake.Folders[fake.UserFolder].Count == 2 && fake.Folders[fake.UserFolder].All(m => m.Has("\\Seen")) && fake.Folders["INBOX"].Count == 2 && fake.Folders["INBOX"].All(m => m.Has("\\Flagged")),
                      $"scelta 15 (C-51), due regole e quattro messaggi, con il server che rifiuta il \"segna come letto\" della prima: i due messaggi dell'altra regola, mai toccati dall'errore, sono contrassegnati lo stesso ({flaggedFirst} su 2); " +
                      $"al controllo dopo i due della prima sono letti e spostati, e sugli altri due non parte più nulla ({secondStore} UID STORE, {secondMove} UID MOVE). Prima: nessuno dei quattro passava più dalle regole");
            }

            // ---------------------------------------------------------------- una copia fatta non si rifà; uno spostamento rifiutato per intero si riprova
            using (var fake = new FakeImap(new Flavor(Gmail: false)))
            {
                var account = SpeedAccount(fake, "mario@studio-prova.example");
                using var imap = new ImapProvider(account);
                for (int i = 1; i <= 2; i++) Mail(fake, $"Fattura n. {i} di prova");
                var folders = await imap.GetFoldersAsync();
                MailFolder target = folders.First(f => f.Id == fake.UserFolder), archive = folders.First(f => f.Id == "Archive");
                Rule(account, moveTo: target, copyTo: archive);
                fake.RefuseOnce = "UID MOVE";
                var first = await Apply(imap, since, () => Task.FromResult(folders));
                int copiedFirst = fake.Folders["Archive"].Count, movedFirst = fake.Folders[fake.UserFolder].Count;
                bool kept = first?.Pending.Values.All(not => not.Contains(Rules.CopyAction + "Archive") && !not.Contains(Rules.MoveAction)) == true;
                fake.Commands.Clear();
                var second = await Apply(imap, DateTime.UtcNow, () => Task.FromResult(folders));
                int secondCopy = Sent(fake, "UID COPY"), secondMove = Sent(fake, "UID MOVE");
                Check(first is { Errors.Count: 1, Pending.Count: 2, Removed.Count: 0 } && first.Errors[0].StartsWith("2 non spostati in ") && !first.Errors[0].Contains("non si riprova") && copiedFirst == 2 && movedFirst == 0 && kept
                      && second is { Errors.Count: 0, Removed.Count: 2 } && secondCopy == 0 && secondMove == 1 && fake.Folders["Archive"].Count == 2 && fake.Folders[fake.UserFolder].Count == 2 && fake.Folders["INBOX"].Count == 0,
                      $"scelta 15 (C-51), una regola \"copia e sposta\" con il server che rifiuta l'UID MOVE: la copia è fatta ({copiedFirst}), lo spostamento rifiutato per intero si riprova al controllo dopo, e la copia non si rifà " +
                      $"({secondCopy} UID COPY, nell'archivio restano {fake.Folders["Archive"].Count} messaggi e non 4)");
            }
            using (var fake = new FakeImap(new Flavor(Gmail: false)))
            {
                var account = SpeedAccount(fake, "mario@studio-prova.example");
                using var imap = new ImapProvider(account);
                for (int i = 1; i <= 2; i++) Mail(fake, $"Fattura n. {i} di prova");
                var folders = await imap.GetFoldersAsync();
                MailFolder target = folders.First(f => f.Id == fake.UserFolder), archive = folders.First(f => f.Id == "Archive");
                Rule(account, moveTo: target, copyTo: archive);
                fake.RefuseOnce = "UID COPY";
                var first = await Apply(imap, since, () => Task.FromResult(folders));
                int firstMove = Sent(fake, "UID MOVE");
                fake.Commands.Clear();
                var second = await Apply(imap, DateTime.UtcNow, () => Task.FromResult(folders));
                int secondCopy = Sent(fake, "UID COPY"), secondMove = Sent(fake, "UID MOVE");
                Check(first is { Errors.Count: 2, Pending.Count: 2, Removed.Count: 0 } && first.Errors[0].StartsWith("2 non copiati in ") && first.Errors[0].Contains(", e non si riprova: ") && firstMove == 0
                      && second is { Errors.Count: 0, Removed.Count: 2 } && secondCopy == 0 && secondMove == 1 && fake.Folders["Archive"].Count == 0 && fake.Folders[fake.UserFolder].Count == 2,
                      $"scelta 15 (C-51), la stessa regola con il server che rifiuta l'UID COPY: una copia partita non si riprova mai, nemmeno se non è riuscita (l'esito lo dice: \"{first?.Errors.FirstOrDefault()}\"); " +
                      $"al controllo dopo parte solo lo spostamento ({secondCopy} UID COPY, {secondMove} UID MOVE), e nell'archivio non c'è nessuna copia");
            }

            // ---------------------------------------------------------------- senza MOVE uno spostamento rifiutato a metà non si ripete
            using (var fake = new FakeImap(new Flavor(Gmail: false, Move: false)))
            {
                var account = SpeedAccount(fake, "mario@studio-prova.example");
                using var imap = new ImapProvider(account);
                for (int i = 1; i <= 2; i++) Mail(fake, $"Fattura n. {i} di prova");
                var folders = await imap.GetFoldersAsync();
                var target = folders.First(f => f.Id == fake.UserFolder);
                Rule(account, moveTo: target);
                fake.RefuseOnce = "UID STORE"; // la copia riesce, il segno \Deleted sugli originali no
                var first = await Apply(imap, since, () => Task.FromResult(folders));
                int waiting = Rules.TestUnfinished(account.Id);
                fake.Commands.Clear();
                var second = await Apply(imap, DateTime.UtcNow, () => Task.FromResult(folders));
                int secondWrites = fake.Commands.Count(c => c.StartsWith("UID STORE") || c.StartsWith("UID COPY") || c.StartsWith("UID EXPUNGE"));
                Check(first is { Errors.Count: 1, Pending.Count: 0 } && first.Errors[0].StartsWith("2 non spostati in ") && first.Errors[0].Contains(", e non si riprova: ") && waiting == 0
                      && second == null && secondWrites == 0 && fake.Folders[fake.UserFolder].Count == 2 && fake.Folders["INBOX"].Count == 2,
                      $"scelta 15 (C-51), su un server senza MOVE lo spostamento è copia più eliminazione: rifiutato dopo la copia non si riprova (\"{first?.Errors.FirstOrDefault()}\"), " +
                      $"e nella cartella di arrivo restano {fake.Folders[fake.UserFolder].Count} copie, non 4; i due messaggi restano nella Posta in arrivo ({secondWrites} comandi di scrittura al controllo dopo)");
            }

            // ---------------------------------------------------------------- dopo tre tentativi si lascia; un messaggio sparito intanto non si tocca; le cartelle non lette
            using (var fake = new FakeImap(new Flavor(Gmail: false)))
            {
                var account = SpeedAccount(fake, "mario@studio-prova.example");
                using var imap = new ImapProvider(account);
                Mail(fake, "Fattura n. 1 di prova");
                var folders = await imap.GetFoldersAsync();
                var target = folders.First(f => f.Id == fake.UserFolder);
                Rule(account, moveTo: target, read: true);
                var tries = new List<int>();
                fake.Commands.Clear();
                for (int check = 1; check <= 3; check++)
                {
                    fake.RefuseOnce = "UID STORE";
                    await Apply(imap, check == 1 ? since : DateTime.UtcNow, () => Task.FromResult(folders));
                    tries.Add(Rules.TestUnfinished(account.Id));
                }
                int refused = Sent(fake, "UID STORE");
                fake.Commands.Clear();
                var after = await Apply(imap, DateTime.UtcNow, () => Task.FromResult(folders));
                int afterWrites = fake.Commands.Count(c => c.StartsWith("UID STORE") || c.StartsWith("UID MOVE"));
                string line = Rules.RetryLine(0, 1, 0);
                Check(Rules.MaxTries == 3 && tries.SequenceEqual([1, 1, 0]) && refused == 3 && after == null && afterWrites == 0 && fake.Folders["INBOX"].Count == 1 && !fake.Folders["INBOX"][0].Has("\\Seen")
                      && line == "un messaggio lasciato com'è dopo 3 tentativi, le regole non lo riprovano più" && Rules.RetryLine(2, 0, 1) == "2 messaggi non finiti, si riprovano al prossimo controllo; un messaggio da riprovare non è più nella Posta in arrivo"
                      && Rules.MatchedLine(2, 3, 0) == "2 di 3 nuovi messaggi corrispondono" && Rules.MatchedLine(1, 0, 1) == "1 di 1 messaggi corrispondono (0 nuovi, 1 riprovati)",
                      $"scelta 15 (C-51), un messaggio la cui azione fallisce a ogni controllo: provato {refused} volte, poi lasciato dov'è (in attesa dopo ogni controllo: {string.Join(", ", tries)}); " +
                      $"al controllo dopo non parte più nulla ({afterWrites} comandi di scrittura), e il registro lo dice senza oggetti né mittenti (\"{line}\")");
            }
            using (var fake = new FakeImap(new Flavor(Gmail: false)))
            {
                var account = SpeedAccount(fake, "mario@studio-prova.example");
                using var imap = new ImapProvider(account);
                for (int i = 1; i <= 2; i++) Mail(fake, $"Fattura n. {i} di prova", minutesAgo: i);
                var folders = await imap.GetFoldersAsync();
                var target = folders.First(f => f.Id == fake.UserFolder);
                Rule(account, moveTo: target, read: true);
                int asked = 0;
                Task<List<MailFolder>> Folders() => ++asked == 1 ? Task.FromException<List<MailFolder>>(new InvalidOperationException("cartelle non lette (prova)")) : Task.FromResult(folders);
                var first = await Apply(imap, since, Folders);
                bool readFirst = fake.Folders["INBOX"].All(m => m.Has("\\Seen"));
                // Intanto uno dei due esce dalla Posta in arrivo (eliminato a mano da un altro programma).
                var deleted = fake.Folders["INBOX"][0];
                fake.Folders["INBOX"].Remove(deleted);
                fake.Commands.Clear();
                var second = await Apply(imap, DateTime.UtcNow, Folders);
                Check(first is { Errors.Count: 1, Pending.Count: 2, Read.Count: 2, Removed.Count: 0 } && first.Errors[0].StartsWith("2 non copiati né spostati, le cartelle non si sono lette: ") && readFirst
                      && second is { Matched: 1, Errors.Count: 0, Removed.Count: 1, Read.Count: 0 } && Sent(fake, "UID STORE") == 0 && Sent(fake, "UID MOVE") == 1 && fake.Folders[fake.UserFolder].Count == 1 && fake.Folders["INBOX"].Count == 0
                      && Rules.TestUnfinished(account.Id) == 0,
                      "scelta 15 (C-51), le cartelle dell'account non si leggono al primo controllo: i due messaggi sono segnati letti e restano da spostare; al controllo dopo uno dei due non è più nella Posta in arrivo e non si tocca, " +
                      $"l'altro si sposta senza rifare il \"segna come letto\" ({Sent(fake, "UID STORE")} UID STORE, {Sent(fake, "UID MOVE")} UID MOVE)");
            }
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }

        // ---------------------------------------------------------------- lo stesso con Gmail
        try
        {
            using var fake = new FakeImap();
            GmailProvider.TestPort = fake.Port;
            var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", Id = "gmail-scelta15" };
            using var gmail = new GmailProvider(account);
            for (int i = 1; i <= 2; i++) Mail(fake, $"Fattura n. {i} di prova", minutesAgo: i);
            var folders = await gmail.GetFoldersAsync();
            var target = folders.First(f => f.Id == fake.UserFolder);
            Rule(account, moveTo: target, read: true);
            fake.RefuseOnce = "UID STORE";
            var first = await Apply(gmail, since, () => Task.FromResult(folders));
            int movedFirst = fake.Folders[fake.UserFolder].Count;
            fake.RefuseOnce = "UID MOVE";
            var second = await Apply(gmail, DateTime.UtcNow, () => Task.FromResult(folders));
            int movedSecond = fake.Folders[fake.UserFolder].Count;
            fake.Commands.Clear();
            var third = await Apply(gmail, DateTime.UtcNow, () => Task.FromResult(folders));
            Check(first is { Errors.Count: 2, Pending.Count: 2, Removed.Count: 0 } && movedFirst == 0
                  && second is { Errors.Count: 1, Pending.Count: 2, Read.Count: 2, Removed.Count: 0 } && !second.Errors[0].Contains("non si riprova") && movedSecond == 0
                  && third is { Errors.Count: 0, Removed.Count: 2 } && Sent(fake, "UID STORE") == 0 && Sent(fake, "UID MOVE") == 1 && fake.Folders[fake.UserFolder].Count == 2 && fake.Folders[fake.UserFolder].All(m => m.Has("\\Seen"))
                  && fake.Folders["INBOX"].Count == 0 && Rules.TestUnfinished(account.Id) == 0,
                  $"scelta 15 (C-51), lo stesso con Gmail: rifiutato il \"segna come letto\" al primo controllo ({Texts(first)}) e lo spostamento al secondo ({Texts(second)}), " +
                  $"al terzo i due messaggi sono letti e spostati, con un solo UID MOVE e senza rifare il segno ({Sent(fake, "UID STORE")} UID STORE)");
        }
        finally
        {
            GmailProvider.TestPort = null;
            Store.Settings.Rules.RemoveAll(r => accounts.Contains(r.AccountId));
            if (Store.Settings.RulesSeen.Count > seenBefore) Store.Settings.RulesSeen.RemoveRange(seenBefore, Store.Settings.RulesSeen.Count - seenBefore);
            Rules.ForgetMarks();
            Store.Save();
        }

        // Chi lo usa: il controllo dei nuovi messaggi aggiorna le righe dei messaggi finiti in ritardo, ed Esegui ora mostra l'esito con gli errori.
        if (Ch1Code("UI", "App.cs") is { } app && Ch1Code("UI", "RulesView.cs") is { } view)
        {
            bool rows = app.Contains("foreach (var m in ruled.Read.Union(ruled.Flagged))") && app.Contains("?.ApplyIdleFlags(p, m.Id, ruled.Read.Contains(m) ? true : null, ruled.Flagged.Contains(m) ? true : null);");
            bool shown = view.Contains("result.Text = RunResult(rule.Name, folder.Name, messages.Count, outcome);");
            Check(rows && shown,
                  $"scelta 15 (C-51), il controllo dei nuovi messaggi cambia i segni sulle righe dei messaggi che le regole finiscono a un controllo dopo ({rows}), ed Esegui ora mostra l'esito con le azioni non riuscite ({shown}) (lettura del sorgente)");
        }
    }

    /// <summary>
    /// Un pezzo di giornata con i promemoria, come li fa CheckRemindersAsync (un giro ogni 30 secondi mentre il PC è
    /// acceso, la lettura di oggi e domani ogni mezz'ora, dopo una non riuscita fra un minuto): quali promemoria suonano, a
    /// che istante e se in ritardo, e quanti non suonano perché la riunione era già finita.
    /// </summary>
    static (List<(string Id, DateTime At, DateTime? Was)> Rung, int Ended) Ch2ReminderDay(List<CalendarEvent> calendar, DateTime start, DateTime end, Func<DateTime, bool>? reads = null, Func<DateTime, bool>? awake = null)
    {
        var rung = new List<(string, DateTime, DateTime?)>();
        var reminders = new UI.CalendarReminders(start);
        var nextFetch = start.AddSeconds(20);
        int ended = 0;
        for (var t = start.AddSeconds(30); t <= end; t = t.AddSeconds(30))
        {
            if (awake?.Invoke(t) == false) continue;
            var clock = t.ToLocalTime();
            if (t >= nextFetch)
            {
                bool ok = reads?.Invoke(t) != false;
                if (ok) reminders.Remember("a", new CalendarData { Events = calendar.Where(e => e.Start >= clock.Date && e.Start < clock.Date.AddDays(2)).ToList(), From = clock.Date, To = clock.Date.AddDays(2) }, t);
                nextFetch = t + (ok ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(1));
            }
            var due = reminders.Due(t, out int skipped);
            foreach (var reminder in due) rung.Add((reminder.Event.Id, t, reminder.Was));
            ended += skipped;
        }
        return (rung, ended);
    }

    /// <summary>
    /// Scelta 3 (C-56): i promemoria del calendario in ritardo. Dall'audit dell'11 ottobre, dopo un avvio senza rete o
    /// riaprendo il PC dopo giorni, suonavano tutti quelli scaduti, anche di riunioni finite. Ora quelli in ritardo suonano
    /// solo se la riunione non è ancora finita, e il testo lo dice ("era alle ..."); quelli delle riunioni finite non
    /// suonano e si contano per il registro. Un promemoria puntuale è com'era. Con orologi finti, anche la notte del 25
    /// ottobre 2026; la notifica non si mostra qui: si provano il giro, il testo e la riga del registro, e si legge dal
    /// sorgente che CheckRemindersAsync e Remind li usino.
    /// </summary>
    static void Ch2LateReminders()
    {
        static CalendarEvent Event(string id, DateTime start, int minutes = 15, int lasts = 60, bool allDay = false, string location = "") =>
            new() { Id = id, Subject = "Riunione di prova", Start = start, End = start.AddMinutes(lasts), ReminderOn = true, ReminderMinutes = minutes, AllDay = allDay, Location = location };
        static DateTime Utc(int month, int day, int hour, int minute) => new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();
        static string Told(List<(string Id, DateTime At, DateTime? Was)> rung) =>
            rung.Count == 0 ? "nessuno" : string.Join(", ", rung.Select(r => $"{r.Id} alle {r.At.ToLocalTime():HH:mm}{(r.Was is { } was ? $" (era alle {was:HH:mm})" : "")}"));

        // Il PC si ferma venerdì 16 ottobre alle 18:00 e riparte lunedì 19 alle 08:55, con la rete.
        var week = new List<CalendarEvent>
        {
            Event("venerdi-1730", new DateTime(2026, 10, 16, 17, 30, 0)), // promemoria alle 17:15, puntuale
            Event("venerdi-1830", new DateTime(2026, 10, 16, 18, 30, 0)), // promemoria alle 18:15, a PC fermo: riunione finita venerdì
            Event("sabato-1000", new DateTime(2026, 10, 17, 10, 0, 0)),   // finita sabato
            Event("lunedi-0800", new DateTime(2026, 10, 19, 8, 0, 0), lasts: 30), // finita alle 08:30
            Event("lunedi-0830", new DateTime(2026, 10, 19, 8, 30, 0)),   // cominciata, finisce alle 09:30
            Event("lunedi-0900", new DateTime(2026, 10, 19, 9, 0, 0)),    // promemoria alle 08:45, non ancora cominciata
            Event("lunedi-1100", new DateTime(2026, 10, 19, 11, 0, 0)),   // promemoria alle 10:45, puntuale
        };
        DateTime asleep = Utc(10, 16, 18, 0), awake = Utc(10, 19, 8, 55);
        var (weekend, weekendEnded) = Ch2ReminderDay(week, Utc(10, 16, 17, 0), Utc(10, 19, 11, 30), awake: t => t <= asleep || t >= awake);
        var late = weekend.Where(r => r.Was != null).ToList();
        Check(weekend.Select(r => r.Id).SequenceEqual(["venerdi-1730", "lunedi-0830", "lunedi-0900", "lunedi-1100"]) && weekendEnded == 3
              && late.Select(r => r.Id).SequenceEqual(["lunedi-0830", "lunedi-0900"]) && late.All(r => r.At == awake) && late[0].Was == new DateTime(2026, 10, 19, 8, 15, 0) && late[1].Was == new DateTime(2026, 10, 19, 8, 45, 0),
              $"scelta 3 (C-56), PC fermo da venerdì alle 18:00 a lunedì alle 08:55: alla riapertura suonano in ritardo solo i promemoria delle riunioni non ancora finite ({Told(late)}); " +
              $"quelli delle {weekendEnded} riunioni già finite (venerdì sera, sabato, lunedì alle 08:00) non suonano e si contano; prima suonavano tutti e {weekend.Count + weekendEnded}, {late.Count + weekendEnded} insieme alle 08:55");

        // Un promemoria puntuale non cambia: una giornata con la rete, senza fermate. Nessuno è "in ritardo", nessuno saltato, e
        // il testo è quello di prima, senza la riga in più. Anche un evento senza durata con il promemoria all'inizio.
        var monday = new List<CalendarEvent>
        {
            Event("riunione-0915", new DateTime(2026, 10, 12, 9, 15, 0), location: "Sala 2"), Event("riunione-1000", new DateTime(2026, 10, 12, 10, 0, 0)),
            Event("scadenza-1100", new DateTime(2026, 10, 12, 11, 0, 0), minutes: 0, lasts: 0), Event("riunione-1200", new DateTime(2026, 10, 12, 12, 0, 0), minutes: 60),
        };
        var (onTime, onTimeEnded) = Ch2ReminderDay(monday, Utc(10, 12, 8, 55), Utc(10, 12, 12, 30));
        var today = new DateTime(2026, 10, 12);
        static string Before(CalendarEvent e, DateTime today) => e.AllDay ? "tutto il giorno" : e.Start.Date == today ? $"alle {e.Start:HH:mm}" : Calendars.When(e); // com'era in MainWindow.Remind
        var tomorrow = Event("domani-0900", new DateTime(2026, 10, 13, 9, 0, 0), minutes: 1440);
        var wholeDay = Event("giornata", new DateTime(2026, 10, 12), allDay: true, lasts: 1440);
        bool sameText = monday.Append(tomorrow).Append(wholeDay).All(e => UI.CalendarReminders.Text(e, null, today) is var text && text.Late == null && text.When == Before(e, today));
        Check(onTime.Select(r => r.Id).SequenceEqual(["riunione-0915", "riunione-1000", "scadenza-1100", "riunione-1200"]) && onTime.All(r => r.Was == null) && onTimeEnded == 0
              && onTime[0].At == Utc(10, 12, 9, 0) && onTime[2].At == Utc(10, 12, 11, 0) && onTime[3].At == Utc(10, 12, 11, 0) && sameText
              && UI.CalendarReminders.Text(monday[0], null, today).When == "alle 09:15" && UI.CalendarReminders.Text(wholeDay, null, today).When == "tutto il giorno",
              $"scelta 3 (C-56), un promemoria puntuale non cambia: in una giornata senza fermate suonano tutti alla loro ora ({Told(onTime)}), nessuno in ritardo e nessuno saltato, " +
              "anche quello di un evento senza durata; e il testo è quello di prima, senza righe in più (oggi, domani, tutto il giorno)");

        // Il confine: fino a due minuti di ritardo è puntuale (un giro tenuto fermo dalla lettura dei calendari), oltre è in ritardo.
        static (List<UI.CalendarReminders.DueReminder> Due, int Ended) At(CalendarEvent e, DateTime read, DateTime now)
        {
            var reminders = new UI.CalendarReminders(read.AddMinutes(-120));
            reminders.Remember("a", new CalendarData { Events = [e], From = e.Start.Date, To = e.Start.Date.AddDays(2) }, read);
            return (reminders.Due(now, out int ended), ended);
        }
        var brief = Event("breve", new DateTime(2026, 10, 12, 10, 0, 0), minutes: 0, lasts: 1); // finita alle 10:01
        var atTwo = At(brief, Utc(10, 12, 9, 30), Utc(10, 12, 10, 2));
        var afterTwo = At(brief, Utc(10, 12, 9, 30), Utc(10, 12, 10, 2).AddSeconds(30));
        var running = At(Event("lunga", new DateTime(2026, 10, 12, 10, 0, 0), minutes: 0, lasts: 60), Utc(10, 12, 9, 30), Utc(10, 12, 10, 59));
        var justOver = At(Event("lunga", new DateTime(2026, 10, 12, 10, 0, 0), minutes: 0, lasts: 60), Utc(10, 12, 9, 30), Utc(10, 12, 11, 0));
        Check(UI.CalendarReminders.Late == TimeSpan.FromMinutes(2) && atTwo is { Ended: 0, Due: [{ Was: null }] } && afterTwo is { Ended: 1, Due.Count: 0 }
              && running is { Ended: 0, Due: [{ Was: { Hour: 10, Minute: 0 } }] } && justOver is { Ended: 1, Due.Count: 0 },
              "scelta 3 (C-56), il confine: fino a due minuti dopo la sua ora un promemoria è puntuale e suona com'era, anche se la riunione (di un minuto) è finita; oltre è in ritardo, " +
              "e allora suona se la riunione è ancora in corso (un minuto prima della fine) e non suona più dal momento in cui finisce");

        // Il testo di un promemoria in ritardo, e la riga del registro per quelli saltati (quanti, senza titoli).
        var started = UI.CalendarReminders.Text(week[4], new DateTime(2026, 10, 19, 8, 15, 0), new DateTime(2026, 10, 19));
        var yesterday = UI.CalendarReminders.Text(Event("giornata", new DateTime(2026, 10, 19), allDay: true, lasts: 1440), new DateTime(2026, 10, 18, 23, 45, 0), new DateTime(2026, 10, 19));
        var days = UI.CalendarReminders.Text(Event("lunga", new DateTime(2026, 10, 16, 17, 30, 0), lasts: 5760), new DateTime(2026, 10, 16, 17, 15, 0), new DateTime(2026, 10, 19));
        string one = UI.CalendarReminders.EndedLine(1), three = UI.CalendarReminders.EndedLine(3);
        Check(started == ("alle 08:30", "Promemoria in ritardo: era alle 08:15") && yesterday == ("tutto il giorno", "Promemoria in ritardo: era ieri alle 23:45")
              && days.Late == "Promemoria in ritardo: era venerdì 16 ottobre alle 17:15" && days.When.StartsWith("venerdì 16 ottobre, 17:30")
              && one == "promemoria del calendario: uno in ritardo non mostrato, la riunione era già finita" && three == "promemoria del calendario: 3 in ritardo non mostrati, le riunioni erano già finite"
              && !one.Contains("Riunione") && !three.Contains("Riunione"),
              $"scelta 3 (C-56), il testo di un promemoria in ritardo ha una riga in più con l'ora in cui doveva arrivare (\"{started.Late}\", \"{yesterday.Late}\", \"{days.Late}\"); " +
              $"per quelli non mostrati il registro dice solo quanti (\"{three}\"), mai i titoli");

        // La notte del 25 ottobre 2026 (alle 03:00 legali tornano le 02:00). Il PC è fermo dalle 00:30 (ora legale) alle 03:20
        // (ora solare): quattro ore e cinquanta minuti di orologio che sono tre ore e cinquanta vere.
        var rome = TimeZoneInfo.Local;
        if (rome.IsAmbiguousTime(new DateTime(2026, 10, 25, 2, 55, 0)) && !rome.IsAmbiguousTime(new DateTime(2026, 10, 25, 3, 10, 0)))
        {
            var night = new List<CalendarEvent>
            {
                Event("notte-0100", new DateTime(2026, 10, 25, 1, 0, 0), lasts: 45),  // finita all'01:45 legale
                Event("notte-0230", new DateTime(2026, 10, 25, 2, 30, 0)),            // le 02:30 solari, fino alle 03:30: promemoria alle 02:15
                Event("notte-0310", new DateTime(2026, 10, 25, 3, 10, 0)),            // promemoria alle 02:55, un'ora che c'è due volte
                Event("notte-0400", new DateTime(2026, 10, 25, 4, 0, 0)),             // promemoria alle 03:45, puntuale
            };
            DateTime from = new(2026, 10, 24, 21, 0, 0, DateTimeKind.Utc), sleep = new(2026, 10, 24, 22, 30, 0, DateTimeKind.Utc), wake = new(2026, 10, 25, 2, 20, 0, DateTimeKind.Utc);
            var (nightRung, nightEnded) = Ch2ReminderDay(night, from, new DateTime(2026, 10, 25, 4, 0, 0, DateTimeKind.Utc), awake: t => t <= sleep || t >= wake);
            // Il ritardo vero, fra istanti: le 02:55 solari sono le 01:55 UTC, quindi 25 minuti, non 85 né un ritardo negativo.
            double Minutes(string id) => nightRung.Where(r => r.Id == id && r.Was is not null).Select(r => (r.At - UI.CalendarReminders.Instant(r.Was!.Value)).TotalMinutes).DefaultIfEmpty(-1).First();
            Check(nightRung.Select(r => r.Id).SequenceEqual(["notte-0230", "notte-0310", "notte-0400"]) && nightEnded == 1 && nightRung[0].At == wake && nightRung[1].At == wake
                  && nightRung[0].Was == new DateTime(2026, 10, 25, 2, 15, 0) && nightRung[1].Was == new DateTime(2026, 10, 25, 2, 55, 0) && Minutes("notte-0230") == 65 && Minutes("notte-0310") == 25
                  && nightRung[2].Was == null && nightRung[2].At == UI.CalendarReminders.Instant(night[3].Start).AddMinutes(-15)
                  && UI.CalendarReminders.Text(night[2], nightRung[1].Was, new DateTime(2026, 10, 25)) == ("alle 03:10", "Promemoria in ritardo: era alle 02:55"),
                  $"scelta 3 (C-56), la notte del 25 ottobre 2026, PC fermo dalle 00:30 legali alle 03:20 solari: suonano in ritardo i promemoria delle riunioni non finite ({Told(nightRung.Take(2).ToList())}, " +
                  $"in ritardo di {Minutes("notte-0230"):0} e {Minutes("notte-0310"):0} minuti veri), non quello della riunione finita all'01:45 ({nightEnded} saltato), e quello delle 03:45 è puntuale");

            // La stessa notte senza fermate: il promemoria delle 02:55 suona alla seconda delle due 02:55, puntuale, com'era dopo l'audit.
            var (nightOnTime, nightOnTimeEnded) = Ch2ReminderDay(night, from, new DateTime(2026, 10, 25, 4, 0, 0, DateTimeKind.Utc));
            Check(nightOnTime.Count == 4 && nightOnTime.All(r => r.Was == null) && nightOnTimeEnded == 0 && nightOnTime.Single(r => r.Id == "notte-0310").At == UI.CalendarReminders.Instant(night[2].Start).AddMinutes(-15),
                  $"scelta 3 (C-56), la stessa notte senza fermate: i quattro promemoria suonano puntuali, quello delle 02:55 alla seconda delle due, senza la riga del ritardo ({Told(nightOnTime)})");
        }
        else Report.Add("      (scelta 3, la notte del 25 ottobre: non misurato, il fuso di questo PC non ha il ritorno all'ora solare quel giorno)");

        // Un calendario nascosto: i suoi promemoria in ritardo non suonano e non si contano fra i saltati.
        var hidden = new UI.CalendarReminders(Utc(10, 16, 17, 0));
        hidden.Remember("a", new CalendarData { Events = week.Where(e => e.Start >= new DateTime(2026, 10, 16)).ToList(), From = new DateTime(2026, 10, 16), To = new DateTime(2026, 10, 18) }, Utc(10, 16, 17, 1));
        var hiddenDue = hidden.Due(Utc(10, 17, 12, 0), out int hiddenEnded, (_, e) => e.Id == "sabato-1000");
        Check(hiddenDue.Count == 0 && hiddenEnded == 2,
              $"scelta 3 (C-56), un calendario nascosto: il suo promemoria in ritardo non suona e non entra nel conto di quelli saltati ({hiddenEnded}: i due di venerdì, di riunioni finite)");

        // Chi li usa (lettura del sorgente: sono metodi della finestra).
        if (Ch1Code("UI", "MainWindow.Calendar.cs") is { } calendar)
        {
            int check = calendar.IndexOf("async Task CheckRemindersAsync()", StringComparison.Ordinal), remind = calendar.IndexOf("void Remind(CalendarReminders.DueReminder reminder)", StringComparison.Ordinal);
            string giro = check >= 0 && remind > check ? calendar[check..remind] : "", notifica = remind >= 0 ? calendar[remind..] : "";
            bool counted = giro.Contains("var due = reminders.Due(DateTime.UtcNow, out int ended,") && giro.Contains("foreach (var reminder in due) Remind(reminder);") && giro.Contains("if (ended > 0) Log.Write(CalendarReminders.EndedLine(ended));");
            bool told = notifica.Contains("var (when, late) = CalendarReminders.Text(e, reminder.Was, DateTime.Today);") && notifica.Contains("if (late != null) toast.AddText(late);")
                        && notifica.Contains(".AddText(when + (e.Location.Length > 0 ? \" · \" + e.Location : \"\"))");
            Check(counted && told,
                  $"scelta 3 (C-56), il giro dei promemoria conta quelli saltati e lo scrive nel registro, senza titoli ({counted}); la notifica prende il testo da CalendarReminders.Text e aggiunge la riga del ritardo solo quando c'è ({told}) " +
                  "(lettura del sorgente: nessuna prova mostra la notifica)");
        }
    }
}
#endif
