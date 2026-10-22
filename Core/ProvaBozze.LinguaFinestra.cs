#if DEBUG
using MimeKit;

namespace Missiva;

/// <summary>
/// Gruppo "lingua-finestra" (Missiva in inglese, 21 ottobre 2026): i controlli dell'inglese dell'area "finestra", scritti da
/// chi la traduce, uno per ogni logica nuova in inglese (date, ricorrenze, plurali, parole chiave, formati, testi composti),
/// con Lingua.Force("en") e l'italiano di nuovo in un finally (Lingua.Force("it")): un gruppo che lascia l'inglese è un
/// controllo non passato. Le traduzioni stanno in Assets/lingue/en/finestra.json; che ogni testo dei sorgenti ne abbia
/// una lo controlla già il gruppo "lingua". Da solo con "Missiva.exe --prova-audit risultato.txt lingua-finestra".
/// Già registrato in ProvaBozze.Gruppi.cs: non serve toccarlo. Vuoto non è un errore (zero controlli).
/// </summary>
static partial class ProvaBozze
{
    static void LanguageWindowTests()
    {
        Report.Add("--- Lingua, area finestra: l'inglese");
        try
        {
            LfSearchItalian();
            LfSearchEnglish();
            LfSearchDates();
            LfWindowTexts();
            LfQuickReplySubject();
            LfSnoozeFolder();
            LfOneMessage();
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    // ---------------------------------------------------------------- la ricerca con i filtri (Core/SearchTerms.cs)

    /// <summary>Tutti i filtri insieme, con le date del 1 e del 20 marzo 2026.</summary>
    static SearchTerms LfAllFilters() => new()
    {
        Text = "fattura", From = "TIM", To = "Maria Rossi", Subject = "offerta", Since = new DateTime(2026, 3, 1), Until = new DateTime(2026, 3, 20),
        HasAttachments = true, Unread = true, Flagged = true,
    };

    /// <summary>
    /// In italiano la ricerca è quella di sempre: le parole chiave italiane, le date scritte d/M/yyyy, le etichette delle
    /// pastiglie con le date italiane; from:, after:, has:attachment e le altre inglesi restano parole libere (passano al
    /// provider, come la sintassi di Gmail).
    /// </summary>
    static void LfSearchItalian()
    {
        Lingua.Force("it");
        var all = LfAllFilters();
        string text = all.ToString();
        var labels = all.Parts().Select(p => p.Label).ToList();
        var english = SearchTerms.Parse("fattura from:TIM to:Rossi subject:offerta after:1/3/2026 before:20/3/2026 has:attachment is:unread is:flagged");
        Check(text == "fattura da:TIM a:\"Maria Rossi\" oggetto:offerta dal:1/3/2026 al:20/3/2026 ha:allegati è:nonletto è:contrassegnato"
              && labels is ["Da: TIM", "A: Maria Rossi", "Oggetto: offerta", "Dal 1 mar 2026", "Al 20 mar 2026", "Con allegati", "Non letti", "Contrassegnati"]
              && SearchTerms.Parse(text) == all && !english.HasFilters
              && english.Text == "fattura from:TIM to:Rossi subject:offerta after:1/3/2026 before:20/3/2026 has:attachment is:unread is:flagged",
              $"ricerca in italiano com'era: \"{text}\", pastiglie {string.Join(" | ", labels)}; le parole chiave inglesi restano parole libere (\"{english.Text}\")");
    }

    /// <summary>
    /// In inglese: from:, to:, subject:, after:, before:, has:attachment, is:unread e is:flagged sono filtri, e le parole chiave
    /// italiane restano accettate (le ricerche recenti fatte in italiano); ToString scrive quelle inglesi, con le date come
    /// 2026-03-01, che si rileggono uguali con en-GB e con en-US; le etichette delle pastiglie sono in inglese, con la data
    /// nell'ordine della cultura.
    /// </summary>
    static void LfSearchEnglish()
    {
        try
        {
            Lingua.Force("en", "en-GB");
            var all = LfAllFilters();
            string text = all.ToString();
            var labels = all.Parts().Select(p => p.Label).ToList();
            var typed = SearchTerms.Parse("fattura FROM:TIM to:\"Maria Rossi\" Subject:offerta after:1/3/2026 before:\"20 March 2026\" has:attachments is:Unread is:flagged");
            var italian = SearchTerms.Parse("fattura da:TIM a:\"Maria Rossi\" oggetto:offerta dal:1/3/2026 al:20/3/2026 ha:allegati è:nonletto è:contrassegnato");
            var left = SearchTerms.Parse("from: is:starred has:drive participants:x@example.com");
            bool gb = text == "fattura from:TIM to:\"Maria Rossi\" subject:offerta after:2026-03-01 before:2026-03-20 has:attachment is:unread is:flagged"
                      && labels is ["From: TIM", "To: Maria Rossi", "Subject: offerta", "Since 1 Mar 2026", "Until 20 Mar 2026", "Has attachments", "Unread", "Flagged"]
                      && SearchTerms.Parse(text) == all && typed == all && italian == all
                      && !left.HasFilters && left.Text == "from: is:starred has:drive participants:x@example.com";

            Lingua.Force("en", "en-US");
            string us = all.ToString();
            var usLabels = all.Parts().Where(p => p.Key is "Since" or "Until").Select(p => p.Label).ToList();
            bool usOk = us == text && SearchTerms.Parse(us) == all && usLabels is ["Since Mar 1, 2026", "Until Mar 20, 2026"]
                        && SearchTerms.Parse("after:3/1/2026 dal:1/3/2026").Since == new DateTime(2026, 3, 1)
                        && SearchTerms.Parse("after:3/1/2026").Since == new DateTime(2026, 3, 1) && SearchTerms.Parse("dal:3/1/2026").Since == new DateTime(2026, 1, 3);
            Check(gb && usOk,
                  $"ricerca in inglese: filtri dalle parole chiave inglesi (anche maiuscole, \"20 March 2026\" e has:attachments) e da quelle italiane; ToString \"{text}\", " +
                  $"pastiglie {string.Join(" | ", labels)}; con en-US le date delle pastiglie {string.Join(" | ", usLabels)}, after:3/1/2026 è il 1 marzo e dal:3/1/2026 resta il 3 gennaio; " +
                  $"is:starred, has:drive e from: senza valore restano parole libere [{gb} {usOk}]");
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    /// <summary>
    /// Le date scritte in inglese (SearchTerms.ParseEnglishDate e ParseDate con english): in cifre nell'ordine della cultura
    /// (12/03 è il 12 marzo con en-GB e il 3 dicembre con en-US), con il nome del mese, senza anno l'ultima volta che quel
    /// giorno c'è stato; una data che con la cultura non si legge si legge come sempre (25/10/2026 anche con en-US).
    /// </summary>
    static void LfSearchDates()
    {
        var today = new DateTime(2026, 10, 21);
        var gb = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        var usCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        DateTime? Gb(string text) => SearchTerms.ParseEnglishDate(text, gb, today);
        DateTime? Us(string text) => SearchTerms.ParseEnglishDate(text, usCulture, today);
        var gbDays = new[] { Gb("12/03"), Gb("12/03/2026"), Gb("12/03/26"), Gb("12 March"), Gb("12 Mar 2025"), Gb("March 12, 2026"), Gb("Mar 12"), Gb("29 February"), Gb("2026-03-12") };
        var usDays = new[] { Us("12/03"), Us("12/03/2026"), Us("March 12"), Us("12 March 2026"), Us("10/21"), Us("10/22") };
        var none = new[] { Gb("31/02"), Gb("12 Marzo"), Gb("13/13"), Us("25/10/2026"), Gb("") };
        bool gbOk = gbDays.SequenceEqual(new DateTime?[] { new(2026, 3, 12), new(2026, 3, 12), new(2026, 3, 12), new(2026, 3, 12), new(2025, 3, 12), new(2026, 3, 12), new(2026, 3, 12), new(2024, 2, 29), new(2026, 3, 12) });
        bool usOk = usDays.SequenceEqual(new DateTime?[] { new(2025, 12, 3), new(2026, 12, 3), new(2026, 3, 12), new(2026, 3, 12), new(2026, 10, 21), new(2025, 10, 22) });
        bool fallback;
        try
        {
            Lingua.Force("en", "en-US");
            fallback = SearchTerms.ParseDate("25/10/2026", today, english: true) == new DateTime(2026, 10, 25) && SearchTerms.ParseDate("12/03", today, english: true) == new DateTime(2025, 12, 3)
                       && SearchTerms.ParseDate("12/03", today) == new DateTime(2026, 3, 12);
        }
        finally
        {
            Lingua.Force("it");
        }
        Check(gbOk && usOk && none.All(d => d == null) && fallback,
              $"ricerca in inglese, le date scritte: en-GB {string.Join(" ", gbDays.Select(d => d?.ToString("yyyy-MM-dd") ?? "-"))}; en-US {string.Join(" ", usDays.Select(d => d?.ToString("yyyy-MM-dd") ?? "-"))}; " +
              $"non lette con la cultura: 31/02, \"12 Marzo\", 13/13, 25/10/2026 in en-US (che ParseDate legge poi come sempre: {fallback})");
    }

    // ---------------------------------------------------------------- testi composti della finestra

    /// <summary>
    /// I testi della finestra che in inglese non sono una traduzione parola per parola: i plurali (1 item, 3 items), le frasi
    /// intere al posto dei pezzi incollati (la barra di Annulla, quella degli invii, la Posta in uscita nella domanda di Rimuovi
    /// account, la rete tornata con gli spostamenti già partiti), "Annulla" che dopo un'azione è Undo e "Nuovo messaggio" che come
    /// comando è New mail, il giorno accanto alle scelte di Contrassegna, il pulsante dell'aggiornamento. In italiano gli stessi
    /// testi di prima, carattere per carattere.
    /// </summary>
    static void LfWindowTexts()
    {
        var due = new DateTime(2026, 10, 20);
        string Italian()
        {
            Lingua.Force("it");
            return string.Join(" | ", UI.MainWindow.FolderCountText(1, 0), UI.MainWindow.FolderCountText(1234, 5), UI.MainWindow.UndoBarText(FolderKind.Deleted, 1, "Cestino"),
                UI.MainWindow.UndoBarText(FolderKind.Archive, 3, "Archivio"), UI.MainWindow.UndoBarText(FolderKind.Other, 2, "Clienti"), UI.MainWindow.OutboxRemovalText(1),
                UI.MainWindow.OutboxRemovalText(2), UI.MainWindow.SendBarText([("Preventivo", null)]), UI.MainWindow.SendBarText([("a", null), ("b", null)]),
                UI.MainWindow.UndoStartedText(1, 3, "Archivio"), UI.MainWindow.UndoWord, UI.MainWindow.NewMailWord, UI.MainWindow.FlagDay(due),
                UI.MainWindow.UpdateButtonText(UI.MainWindow.UpdateLook.Downloading, 100));
        }
        string English(string format)
        {
            Lingua.Force("en", format);
            return string.Join(" | ", UI.MainWindow.FolderCountText(1, 0), UI.MainWindow.FolderCountText(1234, 5), UI.MainWindow.UndoBarText(FolderKind.Deleted, 1, "Deleted Items"),
                UI.MainWindow.UndoBarText(FolderKind.Archive, 3, "Archive"), UI.MainWindow.UndoBarText(FolderKind.Other, 2, "Clients"), UI.MainWindow.OutboxRemovalText(1),
                UI.MainWindow.OutboxRemovalText(2), UI.MainWindow.SendBarText([("Quote", null)]), UI.MainWindow.SendBarText([("a", null), ("b", null)]),
                UI.MainWindow.SendBarText([("Quote", "Uploading plan.pdf: 3.5 of 12 MB…")]), UI.MainWindow.UndoStartedText(1, 3, "Archive"),
                UI.MainWindow.UndoStartedText(2, 3, "Archive"), UI.MainWindow.UndoStartedText(3, 3, "Archive"), UI.MainWindow.UndoWord, UI.MainWindow.NewMailWord,
                UI.MainWindow.FlagDay(due), UI.MainWindow.UpdateButtonText(UI.MainWindow.UpdateLook.Offer, 0), UI.MainWindow.UpdateButtonText(UI.MainWindow.UpdateLook.Downloading, 100),
                UI.MainWindow.UpdateButtonText(UI.MainWindow.UpdateLook.Failed, 0), UI.MainWindow.UpdateProgressText(new Version(0, 3, 600), 42 * 1048576L, 100 * 1048576L));
        }
        try
        {
            string it = Italian(), gb = English("en-GB"), us = English("en-US");
            bool reloads = UI.MainWindow.ReloadsFolders("Offline: check your Internet connection.", false) && UI.MainWindow.ReloadsFolders("Non in linea: controlla la connessione a Internet.", false)
                           && !UI.MainWindow.ReloadsFolders("Loading…", false);
            Check(it == "1 elementi (0 non letti) | 1.234 elementi (5 non letti) | Elemento eliminato | 3 elementi archiviati | 2 elementi spostati in Clienti"
                        + " |  Un messaggio nella Posta in uscita non partirà: la sua finestra si riapre. |  2 messaggi nella Posta in uscita non partiranno: le loro finestre si riaprono."
                        + " | Invio in corso: Preventivo | Invio di 2 messaggi in corso… | La rete è tornata e uno spostamento era già partito: quel messaggio è in Archivio. Gli altri sono tornati al loro posto."
                        + " | Annulla | Nuovo messaggio | mar 20/10 | Aggiornamento 100%"
                  && gb == "1 item (0 unread) | 1,234 items (5 unread) | Item deleted | 3 items archived | 2 items moved to Clients"
                        + " |  A message in the Outbox won't be sent: its window opens again. |  2 messages in the Outbox won't be sent: their windows open again."
                        + " | Sending: Quote | Sending 2 messages… | Sending: 3.5 of 12 MB uploaded (plan.pdf)"
                        + " | The connection came back and one move had already started: that message is in Archive. The others went back to where they were."
                        + " | The connection came back and 2 moves had already started: those messages are in Archive. The others went back to where they were."
                        + " | The connection came back and the move has already been done: the messages are in Archive."
                        + " | Undo | New mail | Tue 20 Oct | Update | Update 100% | Update failed | Downloading Missiva 0.3.600: 42.0 of 100.0 MB"
                  && us.Contains(" | Tue Oct 20 | ") && reloads,
                  $"finestra in inglese, i testi composti: in italiano com'erano ({it}); in inglese {gb}; con en-US il giorno delle scelte di Contrassegna \"Tue Oct 20\"; lo stato \"Offline: ...\" " +
                  $"fa rileggere le cartelle come \"Non in linea: ...\" ({reloads})");
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    /// <summary>
    /// Rispondi dalla notifica (MainWindow.QuickReply) mette l'oggetto come Rispondi (ComposeWindow.SubjectFor): in italiano
    /// "R: " come sempre, in inglese "RE: ", e un prefisso delle due lingue già presente non si ripete.
    /// </summary>
    static void LfQuickReplySubject()
    {
        var account = new MailAccount { Kind = AccountKind.Imap, Email = "prova@example.com", Id = "lingua-notifica" };
        MessageSummary Mail(string subject) => new() { Id = "7", FolderId = "INBOX", Subject = subject, From = new Address { Name = "Mario Rossi", Email = "mario@studio.example" } };
        string Subjects() => string.Join(" | ", new[] { "Preventivo", "R: Preventivo", "RE: Preventivo", "re: Preventivo" }.Select(s => UI.MainWindow.QuickReply(account, Mail(s), "Grazie").Subject));
        try
        {
            Lingua.Force("it");
            string it = Subjects();
            Lingua.Force("en");
            string en = Subjects();
            Check(it == "R: Preventivo | R: Preventivo | RE: Preventivo | re: Preventivo" && en == "RE: Preventivo | R: Preventivo | RE: Preventivo | re: Preventivo",
                  $"Rispondi dalla notifica, l'oggetto come Rispondi: in italiano {it}; in inglese {en}");
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    // ---------------------------------------------------------------- un messaggio solo

    /// <summary>
    /// Le frasi con un numero di messaggi che in inglese con 1 hanno il singolare (Svuota, Elimina cartella, Esegui ora di una
    /// regola): "The 1 messages", "its 1 messages", "1 of the 1 messages" non vanno. In italiano le stesse frasi di prima, anche
    /// con 1, carattere per carattere.
    /// </summary>
    static void LfOneMessage()
    {
        var inbox = new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox };
        var rule = new MailRule();
        var trash = new MailRule { Delete = true };
        string Texts(int n) => string.Join(" | ", UI.MainWindow.EmptyFolderDetail(true, n), UI.MainWindow.EmptyFolderDetail(false, n),
            UI.MainWindow.DeleteFolderDetail(AccountKind.Gmail, n), UI.MainWindow.DeleteFolderDetail(AccountKind.Imap, n), UI.MainWindow.DeleteFolderDetail(AccountKind.Microsoft, n),
            UI.RulesView.RunQuestion(rule, inbox, n, n, "x"), UI.RulesView.RunQuestion(trash, inbox, n, n, "x"),
            UI.RulesView.RunResult("Fatture", "Inbox", n, new Rules.Outcome()), UI.RulesView.RunResult("Fatture", "Inbox", n, new Rules.Outcome { Matched = n }));
        try
        {
            Lingua.Force("it");
            string it = Texts(1);
            Lingua.Force("en");
            string one = Texts(1), two = Texts(2);
            Check(it == "I 1 messaggi verranno eliminati per sempre e non si potranno recuperare. | Sono 1 messaggi. Dal Cestino si possono ancora recuperare."
                        + " | In Gmail le cartelle sono etichette: i suoi 1 messaggi non si cancellano, restano in Tutti i messaggi. Perdono solo l'etichetta."
                        + " | La cartella si elimina dal server insieme ai suoi 1 messaggi. | La cartella finisce in Posta eliminata insieme ai suoi 1 messaggi."
                        + " | La regola cambia 1 dei 1 messaggi di Posta in arrivo. La regola non ha condizioni: vale per ogni messaggio.\n\nx"
                        + " | La regola mette nel Cestino 1 dei 1 messaggi di Posta in arrivo: nel Cestino, non per sempre, da lì si riprendono. La regola non ha condizioni: vale per ogni messaggio.\n\nx"
                        + " | Esegui ora \"Fatture\" su Inbox: nessuno dei 1 messaggi corrisponde. | Esegui ora \"Fatture\" su Inbox: 1 dei 1 messaggi corrispondono."
                  && one == "The message will be deleted permanently and can't be recovered. | That's 1 message. You can still recover it from Deleted Items."
                        + " | In Gmail, folders are labels: its message isn't deleted, it stays in All Mail. It only loses the label."
                        + " | The folder is deleted from the server together with its message. | The folder goes to Deleted Items together with its message."
                        + " | The rule changes the only message in Posta in arrivo. The rule has no conditions: it applies to every message.\n\nx"
                        + " | The rule moves the only message in Posta in arrivo to Deleted Items: not permanently, you can get it back from there. The rule has no conditions: it applies to every message.\n\nx"
                        + " | Run now \"Fatture\" on Inbox: the only message doesn't match. | Run now \"Fatture\" on Inbox: the only message matches."
                  && two.StartsWith("The 2 messages will be deleted permanently") && two.Contains("its 2 messages") && two.Contains("The rule changes 2 of the 2 messages")
                  && two.Contains("none of the 2 messages match"),
                  $"un messaggio solo: in italiano le frasi di prima ({it.Replace("\n", " ")}); in inglese il singolare ({one.Replace("\n", " ")}), con 2 il plurale");
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    // ---------------------------------------------------------------- Posponi: la cartella sul server

    /// <summary>
    /// La cartella di Posponi (MainWindow.SnoozeFolderFor, IsSnoozeName): "Posposti" e "Snoozed" sono la stessa cartella in
    /// tutte e due le lingue; se l'account ne ha già una al primo livello si usa quella (con tutte e due, quella della lingua),
    /// altrimenti si crea con il nome della lingua di Missiva. In italiano, senza cartella, "Posposti" come sempre.
    /// </summary>
    static void LfSnoozeFolder()
    {
        static string Pick(params string[] names) => UI.MainWindow.SnoozeFolderFor(names);
        try
        {
            Lingua.Force("it");
            string it = string.Join(" | ", Pick(), Pick("Clienti", "posposti"), Pick("Snoozed"), Pick("Snoozed", "Posposti"));
            Lingua.Force("en");
            string en = string.Join(" | ", Pick(), Pick("Clienti", "posposti"), Pick("Snoozed"), Pick("Posposti", "SNOOZED"));
            bool names = UI.MainWindow.IsSnoozeName("Posposti") && UI.MainWindow.IsSnoozeName("snoozed") && !UI.MainWindow.IsSnoozeName("Posposto")
                         && !UI.MainWindow.IsSnoozeName("Snooze") && !UI.MainWindow.IsSnoozeName("");
            Check(it == "Posposti | posposti | Snoozed | Posposti" && en == "Snoozed | posposti | Snoozed | SNOOZED" && names,
                  $"Posponi, il nome della cartella: in italiano {it}; in inglese {en}; riconosciute Posposti e Snoozed, maiuscole o no ({names})");
        }
        finally
        {
            Lingua.Force("it");
        }
        Wait(LfSnoozeImapAsync);
    }

    /// <summary>
    /// Posponi con il server IMAP finto, attraverso un cambio di lingua: posposto in italiano nella cartella Posposti creata
    /// da Missiva; in inglese la cartella di Posponi resta quella (nessuna Snoozed nuova, un CREATE solo) e il ritorno, che
    /// cerca nella cartella con l'identificativo scritto quando è stato posposto, riporta il messaggio in Posta in arrivo, da
    /// leggere. Un account nuovo in inglese ha la cartella Snoozed.
    /// </summary>
    static async Task LfSnoozeImapAsync()
    {
        ImapProvider.TestPlain = true;
        try
        {
            MailAccount AccountOf(FakeImap server) => new()
            {
                Kind = AccountKind.Imap, Email = "mario@studio-prova.example", DisplayName = "Mario Rossi",
                Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = server.Port, SmtpHost = "127.0.0.1", SmtpPort = server.Port + 1, SmtpSecurity = MailSecurity.StartTls,
                                        UserName = "mario@studio-prova.example", ProtectedPassword = Store.Protect("Password-di-prova-lingua") },
            };
            static string TopName(List<MailFolder> folders) => UI.MainWindow.SnoozeFolderFor(folders.Where(f => f.ParentId == null).Select(f => f.Name));

            using var fake = new FakeImap(new Flavor(Gmail: false));
            var mime = new MimeMessage { Subject = "Preventivo", MessageId = "preventivo-lingua@studio-prova.example", Body = new TextPart("html") { Text = "<p>Il preventivo</p>" } };
            mime.From.Add(new MailboxAddress("Fornitore", "preventivi@fornitore.example"));
            fake.Add("INBOX", mime, "\\Seen");
            using var imap = new ImapProvider(AccountOf(fake));

            Lingua.Force("it");
            var folders = await imap.GetFoldersAsync();
            var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
            string italianName = TopName(folders);
            var target = await imap.EnsureFolderAsync(italianName);
            var message = (await imap.GetMessagesAsync(inbox, null, null)).Messages.Single();
            var key = await imap.SnoozeAsync(message, target);

            Lingua.Force("en");
            var again = await imap.GetFoldersAsync();
            string englishName = TopName(again);
            var sameTarget = await imap.EnsureFolderAsync(englishName);
            var woken = await imap.WakeAsync(key, new MailFolder { Id = target.Id, Name = englishName }, inbox);
            bool back = fake.Folders["INBOX"].Count == 1 && !fake.Folders["INBOX"][0].Has("\\Seen") && fake.Folders[target.Id].All(m => m.Has("\\Deleted"));

            using var fresh = new FakeImap(new Flavor(Gmail: false));
            using var freshImap = new ImapProvider(AccountOf(fresh));
            string freshName = TopName(await freshImap.GetFoldersAsync());
            var created = await freshImap.EnsureFolderAsync(freshName);

            Check(italianName == "Posposti" && target.Id == "Posposti" && englishName == "Posposti" && sameTarget.Id == target.Id && woken == key && back
                  && fake.Commands.Count(c => c.StartsWith("CREATE")) == 1 && !fake.Folders.ContainsKey("Snoozed")
                  && freshName == "Snoozed" && created.Id == "Snoozed" && fresh.Folders.ContainsKey("Snoozed") && !fresh.Folders.ContainsKey("Posposti"),
                  $"Posponi IMAP fra due lingue: posposto in italiano in \"{target.Id}\"; in inglese la cartella resta \"{sameTarget.Id}\" ({fake.Commands.Count(c => c.StartsWith("CREATE"))} CREATE) " +
                  $"e il ritorno riporta il messaggio in Posta in arrivo da leggere ({woken == key}, {back}); un account nuovo in inglese crea \"{created.Id}\"");
        }
        finally
        {
            Lingua.Force("it");
            ImapProvider.TestPlain = false;
        }
    }
}
#endif
