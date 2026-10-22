#if DEBUG
using System.Net;
using System.Text;
using System.Text.Json;

namespace Missiva;

/// <summary>
/// Gruppo "lingua-servizio" (Missiva in inglese, 21 ottobre 2026): i controlli dell'inglese dell'area "servizio", scritti da
/// chi la traduce, uno per ogni logica nuova in inglese (date, ricorrenze, plurali, parole chiave, formati, testi composti),
/// con Lingua.Force("en") e l'italiano di nuovo in un finally (Lingua.Force("it")): un gruppo che lascia l'inglese è un
/// controllo non passato. Le traduzioni stanno in Assets/lingue/en/servizio.json; che ogni testo dei sorgenti ne abbia
/// una lo controlla già il gruppo "lingua". Da solo con "Missiva.exe --prova-audit risultato.txt lingua-servizio".
/// Già registrato in ProvaBozze.Gruppi.cs: non serve toccarlo. Vuoto non è un errore (zero controlli).
/// </summary>
/// <remarks>
/// La cultura inglese è sempre data (en-GB o en-US, Lingua.Force con i formati di Windows), così i risultati non dipendono
/// dai formati del PC su cui gira la suite. Ogni controllo guarda anche che in italiano il testo sia quello di prima.
/// </remarks>
static partial class ProvaBozze
{
    static void LanguageServiceTests()
    {
        Report.Add("--- Lingua, area servizio: l'inglese");
        try
        {
            LsIconAndNetwork();
            LsFolders();
            LsRows();
            LsAccounts();
            LsProviders();
            LsDownloadsAndPdf();
            LsUpdates();
            LsApp();
        }
        finally
        {
            Lingua.Force("it");
            UI.MessageItem.TestToday = null;
        }
    }

    /// <summary>Lo stesso lavoro in italiano, in inglese (en-GB) e in inglese americano (en-US): i tre risultati.</summary>
    static (R It, R En, R Us) LsThree<R>(Func<R> work)
    {
        Lingua.Force("it");
        var it = work();
        Lingua.Force("en", "en-GB");
        var en = work();
        Lingua.Force("en", "en-US");
        var us = work();
        Lingua.Force("it");
        return (it, en, us);
    }

    /// <summary>Il numero sull'icona (descrizione e suggerimento dell'area di notifica) e "Non in linea".</summary>
    static void LsIconAndNetwork()
    {
        var icon = LsThree(() => string.Join(" | ", IconCount.Describe(IconNumberKind.New, 1), IconCount.Describe(IconNumberKind.New, 3),
                                             IconCount.Describe(IconNumberKind.Unread, 5), IconCount.TrayTip("Missiva", IconNumberKind.Unread, 5), IconCount.TrayTip("Missiva", IconNumberKind.None, 5)));
        Check(icon.It == "1 nuovo | 3 nuovi | 5 da leggere | Missiva: 5 da leggere | Missiva" && icon.En == "1 new | 3 new | 5 unread | Missiva: 5 unread | Missiva",
              $"lingua-servizio, il numero sull'icona: descrizione e suggerimento dell'area di notifica in inglese, in italiano come prima ({icon.En})");
        var offline = LsThree(() => Network.Explain(new System.Net.Sockets.SocketException(10013)) + " | " + Network.Explain(new System.Net.Sockets.SocketException(10051)));
        Check(offline.It.StartsWith("Non in linea: un firewall") && offline.It.EndsWith("| Non in linea: controlla la connessione a Internet.")
              && offline.En == "Offline: a firewall (for example Portmaster) is blocking Missiva. Allow Missiva.exe in the firewall. | Offline: check your internet connection.",
              $"lingua-servizio, \"Non in linea\" (Network.Explain) in inglese comincia con Offline ({offline.En})");
    }

    /// <summary>I nomi delle cartelle speciali, per tipo di account, e la copia locale scritta nell'altra lingua.</summary>
    static void LsFolders()
    {
        static string Names(AccountKind account) => string.Join(", ", new[] { FolderKind.Inbox, FolderKind.Drafts, FolderKind.Sent, FolderKind.Deleted, FolderKind.Archive, FolderKind.Junk }
            .Select(k => MailFolder.SpecialShown(account, k)));
        var names = LsThree(() => $"{Names(AccountKind.Microsoft)} / {Names(AccountKind.Gmail)} / {Names(AccountKind.Imap)} / [{MailFolder.SpecialShown(AccountKind.Imap, FolderKind.Other)}]");
        Check(names.It == "Posta in arrivo, Bozze, Posta inviata, Posta eliminata, Archivio, Posta indesiderata / Posta in arrivo, Bozze, Posta inviata, Cestino, Tutti i messaggi, Spam / " +
                          "Posta in arrivo, Bozze, Posta inviata, Cestino, Archivio, Posta indesiderata / []"
              && names.En == "Inbox, Drafts, Sent Items, Deleted Items, Archive, Junk Email / Inbox, Drafts, Sent, Trash, All Mail, Spam / Inbox, Drafts, Sent Items, Deleted Items, Archive, Junk Email / []"
              && names.Us == names.En,
              $"lingua-servizio, i nomi delle cartelle speciali (MailFolder.SpecialName) in inglese quelli di Outlook per Microsoft e IMAP e quelli del sito di Gmail per Gmail, in italiano quelli di prima ({names.En})");

        // La copia locale delle cartelle scritta in italiano, letta in inglese, e il contrario: le speciali nella lingua di adesso, le altre come il server.
        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = "lingua.cartelle@studio-prova.example" };
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "lingua.cartelle@gmail-prova.example" };
        MailCache.Delete(account);
        MailCache.Delete(gmail);
        try
        {
            MailCache.SaveFolders(account,
            [
                new MailFolder { Id = "in", Name = MailFolder.SpecialShown(AccountKind.Microsoft, FolderKind.Inbox), Kind = FolderKind.Inbox, Unread = 3 },
                new MailFolder { Id = "el", Name = MailFolder.SpecialShown(AccountKind.Microsoft, FolderKind.Deleted), Kind = FolderKind.Deleted },
                new MailFolder { Id = "cl", Name = "Clienti", Kind = FolderKind.Other, ParentId = "in" },
            ]);
            Lingua.Force("en", "en-GB");
            var english = MailCache.LoadFolders(account) ?? [];
            MailCache.SaveFolders(account, english);
            Lingua.Force("it");
            var italian = MailCache.LoadFolders(account) ?? [];
            static string Shape(List<MailFolder> f) => string.Join(", ", f.Select(x => $"{x.Id}={x.Name}/{x.Unread}/{x.ParentId}"));
            Check(Shape(english) == "in=Inbox/3/, el=Deleted Items/0/, cl=Clienti/0/in" && Shape(italian) == "in=Posta in arrivo/3/, el=Posta eliminata/0/, cl=Clienti/0/in",
                  $"lingua-servizio, la copia locale delle cartelle scritta in un'altra lingua: le speciali nella lingua di adesso, con conteggi e genitore, le cartelle dell'utente come il server ({Shape(english)} | {Shape(italian)})");

            // Lo stesso per Gmail, che in inglese ha Sent e Trash del sito di Gmail.
            MailCache.SaveFolders(gmail,
            [
                new MailFolder { Id = "[Gmail]/Posta inviata", Name = MailFolder.SpecialShown(AccountKind.Gmail, FolderKind.Sent), Kind = FolderKind.Sent },
                new MailFolder { Id = "[Gmail]/Cestino", Name = MailFolder.SpecialShown(AccountKind.Gmail, FolderKind.Deleted), Kind = FolderKind.Deleted },
            ]);
            Lingua.Force("en", "en-GB");
            var gmailEnglish = MailCache.LoadFolders(gmail) ?? [];
            MailCache.SaveFolders(gmail, gmailEnglish);
            Lingua.Force("it");
            var gmailItalian = MailCache.LoadFolders(gmail) ?? [];
            Check(Shape(gmailEnglish) == "[Gmail]/Posta inviata=Sent/0/, [Gmail]/Cestino=Trash/0/" && Shape(gmailItalian) == "[Gmail]/Posta inviata=Posta inviata/0/, [Gmail]/Cestino=Cestino/0/",
                  $"lingua-servizio, la copia locale delle cartelle di Gmail: in inglese Sent e Trash, di nuovo in italiano Posta inviata e Cestino ({Shape(gmailEnglish)} | {Shape(gmailItalian)})");
        }
        finally
        {
            Lingua.Force("it");
            MailCache.Delete(account);
            MailCache.Delete(gmail);
        }
    }

    /// <summary>Le righe dell'elenco: date, gruppi, nome per UI Automation, segnaposto, suggerimenti.</summary>
    static void LsRows()
    {
        var today = new DateTime(2026, 10, 21); // un mercoledì
        UI.MessageItem.TestToday = today;
        try
        {
            string Dates() => string.Join(" | ", UI.MessageItem.Format(today.AddHours(9).AddMinutes(54)), UI.MessageItem.Format(today.AddDays(-1).AddHours(21).AddMinutes(54)),
                                          UI.MessageItem.Format(new DateTime(2026, 3, 12, 8, 0, 0)), UI.MessageItem.Format(new DateTime(2025, 3, 12, 8, 0, 0)));
            var dates = LsThree(Dates);
            Check(dates.It == "09:54 | mar 21:54 | 12 mar | 12/03/2025" && dates.En == "09:54 | Tue 21:54 | 12 Mar | 12/03/2025" && dates.Us == "9:54 AM | Tue 9:54 PM | Mar 12 | 3/12/2025",
                  $"lingua-servizio, le date delle righe: in italiano come prima ({dates.It}), in inglese l'ora e la data come la cultura, giorno e mese nel suo ordine ({dates.En} / {dates.Us})");

            string Spoken() => string.Join(" | ", UI.MessageItem.Spoken(today.AddHours(9).AddMinutes(54)), UI.MessageItem.Spoken(today.AddDays(-1).AddHours(21).AddMinutes(54)),
                                           UI.MessageItem.Spoken(today.AddDays(-3).AddHours(8)), UI.MessageItem.Spoken(new DateTime(2026, 3, 12, 8, 0, 0)));
            var spoken = LsThree(Spoken);
            Check(spoken.It == "oggi 09:54 | ieri 21:54 | domenica 08:00 | 12 marzo 2026" && spoken.En == "today 09:54 | yesterday 21:54 | Sunday 08:00 | 12 March 2026"
                  && spoken.Us == "today 9:54 AM | yesterday 9:54 PM | Sunday 8:00 AM | March 12, 2026",
                  $"lingua-servizio, la data letta dall'Assistente vocale (\"oggi 09:54\"): in inglese \"today\", il giorno e il mese in lettere nell'ordine della cultura ({spoken.En} / {spoken.Us})");

            string Groups() => string.Join(", ", new[] { today, today.AddDays(-1), today.AddDays(-2), today.AddDays(-8), today.AddDays(-15), new DateTime(2026, 9, 3), new DateTime(2026, 5, 3), new DateTime(2025, 5, 3) }
                .Select(UI.MessageItem.GroupFor));
            var groups = LsThree(Groups);
            Check(groups.It == "Oggi, Ieri, Questa settimana, Settimana scorsa, Questo mese, Mese scorso, Maggio, Maggio 2025"
                  && groups.En == "Today, Yesterday, This week, Last week, This month, Last month, May, May 2025",
                  $"lingua-servizio, i gruppi dell'elenco per data in inglese, con il mese in inglese e la maiuscola ({groups.En})");

            // Il nome della riga per UI Automation: le parti nella lingua di Missiva, i dati del messaggio come sono.
            var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "lingua.righe@studio-prova.example", Imap = new ImapServer() }));
            var message = new MessageSummary
            {
                Id = "1", FolderId = "INBOX", Subject = "Fattura di ottobre", Date = today.AddHours(9).AddMinutes(54), From = new Address { Name = "Mario Rossi", Email = "mario.rossi@studio-prova.example" },
                HasAttachments = true, IsImportant = true,
            };
            string Row()
            {
                var row = new UI.MessageItem(message, account);
                var blank = new UI.MessageItem(new MessageSummary { Id = "2", FolderId = "INBOX", Date = today }, account);
                var draft = new UI.MessageItem(new MessageSummary { Id = "3", FolderId = "Bozze", LabelKind = FolderKind.Drafts, Subject = "Preventivo", Date = today, IsRead = true }, account);
                // "[Bozza]" sta prima dell'oggetto, nella prima o nella seconda riga secondo Elenco messaggi: una delle due è vuota.
                return string.Join(" | ", row.AutomationName, blank.Sender, blank.Subject, draft.Line1Tag + draft.Line2Tag + draft.Subject, draft.Sender, draft.AutomationName,
                                   row.ReadTip, row.FlagTip, row.PinTip, row.ChevronTip);
            }
            var rows = LsThree(Row);
            Check(rows.It == "Non letto, Mario Rossi, Fattura di ottobre, oggi 09:54, con allegato, importanza alta | (nessun mittente) | (nessun oggetto) | [Bozza] Preventivo | (nessun destinatario) | " +
                             "Bozza, A (nessun destinatario), Preventivo, oggi 00:00 | Segna come letto (Ctrl+Q) | Contrassegna | Fissa in alto | Espandi la conversazione"
                  && rows.En == "Unread, Mario Rossi, Fattura di ottobre, today 09:54, has attachment, high importance | (No sender) | (No subject) | [Draft] Preventivo | (No recipients) | " +
                                "Draft, To (No recipients), Preventivo, today 00:00 | Mark as read (Ctrl+Q) | Flag | Pin | Expand conversation",
                  $"lingua-servizio, la riga: nome per UI Automation, segnaposto, \"[Bozza]\" e i suggerimenti delle azioni rapide in inglese, in italiano come prima ({rows.En})");
        }
        finally
        {
            UI.MessageItem.TestToday = null;
        }
    }

    /// <summary>Le intestazioni e gli stati sotto gli account, le cartelle per UI Automation, Tutti gli account.</summary>
    static void LsAccounts()
    {
        string Account()
        {
            var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "lingua.account@studio-prova.example", Imap = new ImapServer() }));
            account.Queued = 2;
            string loading = account.StatusLine;
            account.Queued = 1;
            string one = account.StatusLine;
            // Il motivo della barra: dal testo dell'errore, anche in inglese (le frasi di ImapProvider.CredentialsOf).
            account.Fix = AccountFix.Password;
            account.Status = T("Su questo PC manca la password di {0}: scrivila con Aggiorna password.", "lingua");
            string missing = account.FixMessage;
            account.Status = T("La password di {0} salvata su questo PC non si legge più (profilo di Windows cambiato, o impostazioni copiate da un altro PC): scrivila di nuovo con Aggiorna password.", "lingua");
            string unreadable = account.FixMessage;
            account.Status = "530 5.7.0 Authentication failed";
            string refused = account.FixMessage;
            var inbox = new UI.FolderItem(account, new MailFolder { Id = "INBOX", Name = MailFolder.SpecialShown(AccountKind.Imap, FolderKind.Inbox), Kind = FolderKind.Inbox, Unread = 3 });
            var sent = new UI.FolderItem(account, new MailFolder { Id = "Sent", Name = MailFolder.SpecialShown(AccountKind.Imap, FolderKind.Sent), Kind = FolderKind.Sent, Unread = 1 });
            var unified = new UI.UnifiedItem(UI.UnifiedView.Flagged);
            return string.Join(" | ", loading, one, account.FixText, missing, unreadable, refused, inbox.AutomationName, sent.AutomationName, unified.Name, unified.AutomationName, account.HiddenToggleText);
        }
        var shown = LsThree(Account);
        Check(shown.It == "Caricamento… · 2 azioni in attesa | Caricamento… · 1 azione in attesa | Aggiorna password | Su questo PC manca la password dell'account. | La password salvata non si legge più su questo PC. | " +
                          "Password non accettata dal server. | Posta in arrivo, 3 non letti | Posta inviata | Contrassegnati | Contrassegnati di tutti gli account | Mostra 0 cartelle nascoste"
              && shown.En == "Loading… · 2 pending actions | Loading… · 1 pending action | Update password | This PC doesn't have the account's password. | The saved password can't be read on this PC anymore. | " +
                             "The server didn't accept the password. | Inbox, 3 unread | Sent Items | Flagged | Flagged, all accounts | Show 0 hidden folders",
              $"lingua-servizio, sotto l'account (stato, azioni in attesa, barra con il motivo riconosciuto anche dal testo inglese dell'errore), cartelle per UI Automation e Tutti gli account ({shown.En})");

        // Gli accessi di Google e Microsoft: i loro errori li traduce l'area impostazioni (GoogleAuth, MicrosoftAuth). Le frasi
        // inglesi di oggi in quel file, scritte qui com'erano il 21 ottobre 2026: FixMessage ci trova i pezzi che cerca.
        Lingua.Force("en", "en-GB");
        try
        {
            var account = new UI.AccountItem(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "lingua.accesso@studio-prova.example", Imap = new ImapServer() })) { Fix = AccountFix.SignIn };
            account.Status = "This PC doesn't have the Google sign-in for ada@studio-prova.example: Sign in again (Settings, Accounts, Manage).";
            string missing = account.FixMessage;
            account.Status = "The Google sign-in for ada@studio-prova.example saved on this PC can't be read anymore (Windows profile changed, or settings copied from another PC): Sign in again (Settings, Accounts, Manage).";
            string unreadable = account.FixMessage;
            account.Status = "This PC doesn't have the Microsoft sign-in for ada@studio-prova.example: select Sign in again, under the account.";
            string microsoft = account.FixMessage;
            account.Status = "AADSTS50076: the account needs to sign in again";
            string other = account.FixMessage;
            Check(missing == "This PC doesn't have the sign-in for the account." && unreadable == "The saved sign-in can't be read on this PC anymore." && microsoft == missing
                  && other == "The account needs you to sign in again.",
                  $"lingua-servizio, la barra \"Accedi di nuovo\" in inglese riconosce dalle frasi inglesi di GoogleAuth e MicrosoftAuth l'accesso che manca e quello che non si legge più ({missing} | {unreadable} | {other})");
            // L'avanzamento del caricamento a pezzi (GraphProvider.UploadLargeAsync): la forma che MainWindow.UploadShort riconosce in inglese.
            var progress = T("Caricamento di {0}: {1} di {2} MB…", "relazione.zip", "3.5", "12");
            Check(System.Text.RegularExpressions.Regex.IsMatch(progress, @"^Uploading (?<name>.+): (?<done>[\d,.]+) of (?<total>[\d,.]+) MB…$"),
                  $"lingua-servizio, l'avanzamento del caricamento degli allegati grandi in inglese ha la forma \"Uploading nome: 3.5 of 12 MB…\" ({progress})");
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    /// <summary>Gli errori dei provider che arrivano sullo schermo, con le frasi intere al posto dei pezzi incollati.</summary>
    static void LsProviders()
    {
        string Errors() => string.Join(" | ",
            ImapProvider.Explain(new MailKit.Security.AuthenticationException(), "imap.studio-prova.example", 993, "IMAP")?.Message,
            ImapProvider.Explain(new System.Net.Sockets.SocketException(10060), "imap.studio-prova.example", 993, "IMAP")?.Message,
            ImapProvider.Explain(new NotSupportedException("STARTTLS"), "smtp.studio-prova.example", 587, "SMTP")?.Message,
            GraphProvider.SharedError(HttpStatusCode.Forbidden, "", "info@studio-prova.example"),
            GmailProvider.ImapOff("ada.prova@gmail.com"), GmailProvider.ImapOff("ada@studio-prova.example"),
            MailException.ClosedText("ada@studio-prova.example", removed: true));
        var errors = LsThree(Errors);
        Check(errors.It.StartsWith("imap.studio-prova.example ha rifiutato nome utente o password (IMAP). Controllali") && errors.It.Contains("non offre la cifratura STARTTLS sulla porta 587: Missiva non gli manda la password in chiaro. Prova SSL/TLS (di solito porta 465).")
              && errors.It.Contains("Microsoft non ti lascia aprire la casella condivisa info@studio-prova.example") && errors.It.EndsWith("Il collegamento di ada@studio-prova.example usato da questa finestra è stato chiuso: l'account è stato rimosso da Missiva. Da questa finestra non si può più leggere né cambiare nulla di quell'account: chiudila; se stavi scrivendo, copia prima il testo.")
              && errors.En.StartsWith("imap.studio-prova.example refused the user name or password (IMAP). Check them") && errors.En.Contains("imap.studio-prova.example isn't responding on port 993 (IMAP)")
              && errors.En.Contains("smtp.studio-prova.example doesn't offer STARTTLS encryption on port 587") && errors.En.Contains("(usually port 465)")
              && errors.En.Contains("Microsoft doesn't let you open the shared mailbox info@studio-prova.example") && errors.En.Contains("Gmail doesn't accept IMAP access for this account")
              && errors.En.Contains("turned off IMAP access for studio-prova.example") && errors.En.EndsWith("The connection to ada@studio-prova.example used by this window was closed: the account was removed from Missiva. This window can no longer read or change anything in that account: close it. If you were writing, copy the text first."),
              $"lingua-servizio, gli errori dei provider in inglese con i valori al loro posto (ImapProvider.Explain, caselle condivise, IMAP di Gmail spento, provider chiuso), in italiano come prima ({errors.En[..Math.Min(160, errors.En.Length)]}...)");
        var day = LsThree(() => ScheduledSending.Day(new DateTime(2026, 10, 21, 8, 0, 0)));
        Check(day == ("21/10", "21 October", "October 21"), $"lingua-servizio, il giorno negli avvisi degli invii pianificati: \"{day.It}\", in inglese con il mese in lettere ({day.En}, {day.Us})");
    }

    /// <summary>Il gestore degli scaricamenti (stato, avanzamento con i decimali, motivi) e i motivi dell'anteprima dei PDF.</summary>
    static void LsDownloadsAndPdf()
    {
        string Downloads()
        {
            var waiting = new DownloadItem("fattura.pdf", 3 * 1048576, temporary: false, waiting: true);
            var running = new DownloadItem("fattura.pdf", 3 * 1048576, temporary: false, waiting: false);
            ((IProgress<long>)running).Report(1572864);
            return string.Join(" | ", waiting.Status, running.Status, DownloadItem.SizeText(1572864), DownloadItem.Progress(300 * 1024, 820 * 1024),
                               DownloadList.Reason(new UnauthorizedAccessException()), DownloadList.Reason(new MailException("Non riesco a scaricare l'allegato.", status: 500)));
        }
        var downloads = LsThree(Downloads);
        Check(downloads.It == "In coda | 1,5 di 3,0 MB, 50% | 1,5 MB | 300 di 820 KB | Missiva non può scrivere in quella cartella. | Non riesco a scaricare l'allegato (il server ha risposto 500)."
              && downloads.En == "Queued | 1.5 of 3.0 MB, 50% | 1.5 MB | 300 of 820 KB | Missiva can't write to that folder. | Non riesco a scaricare l'allegato (the server responded 500).",
              $"lingua-servizio, gli scaricamenti: stato, avanzamento e misure con i decimali della lingua (1,5 e 1.5), motivi in inglese ({downloads.En})");
        var pdf = LsThree(() => string.Join(" | ", PdfRender.Explain(new TimeoutException()), PdfRender.Explain(new InvalidDataException("troppe pagine per l'anteprima (6000, al massimo 5000)")),
                                            PdfRender.Explain(new Sandbox.SandboxException("prova"))));
        Check(pdf.It.StartsWith("Il PDF non si è disegnato in tempo") && pdf.It.Contains("(oltre 5000)") && pdf.En.StartsWith("The PDF didn't render in time")
              && pdf.En.Contains("The PDF has too many pages to preview (over 5000)") && pdf.En.EndsWith("Missiva doesn't open PDFs without protection: open it with the Windows app (Open) or download it."),
              $"lingua-servizio, l'anteprima dei PDF: i motivi in inglese, riconosciuti dai testi italiani del processo dell'anteprima ({pdf.En[..Math.Min(120, pdf.En.Length)]}...)");
    }

    /// <summary>Gli aggiornamenti: le novità inglesi del manifesto, il titolo, l'esito scritto in italiano dal nuovo eseguibile.</summary>
    static void LsUpdates()
    {
        var current = new Version(0, 3, 500);
        byte[] Manifest(Action<Dictionary<string, object>>? change = null)
        {
            var fields = new Dictionary<string, object>
            {
                ["prodotto"] = "Missiva", ["numero"] = "0.3.999", ["versione"] = "0.3.999 del 21/10/2026 (abcdef0)", ["data"] = "2026-10-21", ["commit"] = "abcdef0",
                ["file"] = "Missiva-0.3.999.zip", ["dimensione"] = 102760448, ["sha256"] = new string('0', 64), ["novita"] = new[] { "Prima novità", "Seconda novità" },
            };
            change?.Invoke(fields);
            return JsonSerializer.SerializeToUtf8Bytes(fields);
        }
        var without = Manifest();
        var english = Manifest(f => f["news"] = new[] { "First change", "Second change" });
        var unknown = Manifest(f => f["futuro"] = 1);
        var parts = LsThree(() =>
        {
            var a = Updates.ReadManifest(without, current);
            var b = Updates.ReadManifest(english, current);
            var c = Updates.ReadManifest(unknown, current);
            static string Fields(Updates.Manifest m) => $"{m.Number} {m.Text} {m.Date:yyyy-MM-dd} {m.Commit} {m.FileName} {m.Size} {m.Sha256} {string.Join("+", m.NewsItalian)}";
            bool same = Fields(a) == Fields(b) && Fields(a) == Fields(c) && a.NewsEnglish == null && c.NewsEnglish == null && b.NewsEnglish is ["First change", "Second change"];
            return $"{same} | {a.Title} | {string.Join(" + ", a.News)} | {string.Join(" + ", b.News)}";
        });
        Check(parts.It == "True | Missiva 0.3.999 del 21/10/2026 | Prima novità + Seconda novità | Prima novità + Seconda novità"
              && parts.En == "True | Missiva 0.3.999 (21/10/2026) | Prima novità + Seconda novità | First change + Second change"
              && parts.Us == "True | Missiva 0.3.999 (10/21/2026) | Prima novità + Seconda novità | First change + Second change",
              "lingua-servizio, il manifesto con il campo \"news\": per il resto si legge come senza (come uno con un campo che nessuno conosce), le novità mostrate (Manifest.News) sono le " +
              $"inglesi in inglese se ci sono, altrimenti le italiane, in italiano sempre le italiane; il titolo con la data della cultura ({parts.En} / {parts.Us})");
        var badNews = Manifest(f => f["news"] = new[] { "riga\u0007con un carattere di controllo" });
        string? refused = null;
        try { Updates.ReadManifest(badNews, current); }
        catch (Updates.Refused ex) { refused = ex.Message; }
        Check(refused == "nel manifesto una riga delle novità non è come deve essere",
              $"lingua-servizio, una riga delle novità inglesi fuori regola fa rifiutare il manifesto come una delle italiane ({refused ?? "accettato"})");

        // L'esito della sostituzione, scritto in italiano dal nuovo eseguibile (Replace, qui con il file installato bloccato) e mostrato tradotto all'avvio dopo.
        var root = Path.Combine(Store.Dir, "lingua-servizio-aggiornamento");
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            var folder = Directory.CreateDirectory(Path.Combine(root, "0.3.999")).FullName;
            var source = Path.Combine(folder, "Missiva.exe");
            var installed = Path.Combine(root, "installata", "Missiva.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
            File.WriteAllText(source, "nuovo");
            File.WriteAllText(installed, "vecchio");
            Updates.Replacement result;
            using (new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.None)) result = Updates.Replace(source, installed, 2, TimeSpan.Zero);
            File.WriteAllText(Path.Combine(folder, Updates.OutcomeName), result.Message);
            File.WriteAllText(Path.Combine(root, Updates.PendingName), JsonSerializer.Serialize(new Updates.Pending("0.3.500", "0.3.999", folder, installed)));
            Lingua.Force("en", "en-GB");
            var notice = Updates.AfterStart(root, "0.3.500", _ => false);
            var notStarted = Updates.Outcome("il nuovo eseguibile non è partito (accesso negato)") + " | " + Updates.Outcome("il nuovo eseguibile non è partito") + " | " + Updates.Outcome("IOException: disco pieno");
            Lingua.Force("it");
            Check(!result.Done && result.Message.StartsWith("il file installato era ancora in uso dopo 2 tentativi (") && notice != null
                  && notice.StartsWith("Missiva didn't update to 0.3.999: the installed file was still in use after 2 attempts (") && notice.EndsWith("). Version 0.3.500 is still installed.")
                  && notStarted == "the new executable didn't start (accesso negato) | the new executable didn't start | IOException: disco pieno",
                  $"lingua-servizio, l'esito del nuovo eseguibile, scritto in italiano, all'avvio dopo in inglese (Updates.Outcome): \"{notice}\"; {notStarted}");
        }
        finally
        {
            Lingua.Force("it");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        var lines = LsThree(() => Updates.AutomaticAsync(false, null, "", current, Updates.Http).GetAwaiter().GetResult().Line + " | "
                                  + Updates.CheckAsync(null, "", current, Updates.Http).GetAwaiter().GetResult().Line);
        Check(lines.It == "Il controllo automatico è spento. | Compilata dai sorgenti: non controlla gli aggiornamenti." && lines.En == "Automatic checking is turned off. | Built from source: it doesn't check for updates.",
              $"lingua-servizio, la riga degli aggiornamenti in Informazioni senza richieste in inglese ({lines.En})");
    }

    /// <summary>Le notifiche (pulsanti che entrano, voci del menu) e l'avviso dei testi cifrati.</summary>
    static void LsApp()
    {
        string Toast()
        {
            var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder().AddArgument("account", "a").AddArgument("message", "7");
            UI.App.AddActions(toast, new Microsoft.Toolkit.Uwp.Notifications.ToastArguments().Add("account", "a").Add("message", "7"));
            var xml = toast.GetToastContent().GetContent();
            return string.Join(" | ", System.Text.RegularExpressions.Regex.Matches(xml, "<action [^>]*content=\"([^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)));
        }
        var toast = LsThree(Toast);
        var buttons = toast.En.Split(" | ").Take(3).ToList();
        Check(toast.It == "Rispondi | Segna | Elimina | Posponi di un'ora | Posponi..." && toast.En == "Reply | Flag | Delete | Snooze for 1 hour | Snooze..." && buttons.All(b => b.Length <= 11),
              $"lingua-servizio, i pulsanti della notifica di un messaggio in inglese, ognuno entro le 11 lettere che la notifica di Windows tiene su una riga, e le voci del menu \"...\" ({toast.En})");
        var notice = LsThree(() => (One: UI.App.UnreadableNotice(1), Three: UI.App.UnreadableNotice(3)));
        Check(notice.It.One.StartsWith("Un testo cifrato delle impostazioni di Missiva (") && notice.It.One.Contains(") non si decifra più: ")
              && notice.It.Three.StartsWith("3 testi cifrati delle impostazioni di Missiva (") && notice.It.Three.Contains(") non si decifrano più: ")
              && notice.En.One.StartsWith("One encrypted text in Missiva's settings (") && notice.En.One.Contains(") can no longer be decrypted: ") && notice.En.One.Contains("In its place")
              && notice.En.Three.StartsWith("3 encrypted texts in Missiva's settings (") && notice.En.Three.Contains("In their place"),
              $"lingua-servizio, l'avviso dei testi cifrati che non si leggono: due frasi intere, al singolare e al plurale ({notice.En.Three[..60]}...)");
    }
}
#endif
