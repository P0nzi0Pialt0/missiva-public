#if DEBUG
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Missiva;

/// <summary>
/// Gruppo "lingua-agenda" (Missiva in inglese, 21 ottobre 2026): i controlli dell'inglese dell'area "agenda", scritti da
/// chi la traduce, uno per ogni logica nuova in inglese (date, ricorrenze, plurali, parole chiave, formati, testi composti),
/// con Lingua.Force("en") e l'italiano di nuovo in un finally (Lingua.Force("it")): un gruppo che lascia l'inglese è un
/// controllo non passato. Le traduzioni stanno in Assets/lingue/en/agenda.json; che ogni testo dei sorgenti ne abbia
/// una lo controlla già il gruppo "lingua". Da solo con "Missiva.exe --prova-audit risultato.txt lingua-agenda".
/// Già registrato in ProvaBozze.Gruppi.cs: non serve toccarlo. Vuoto non è un errore (zero controlli).
/// Qui: la ricorrenza in parole (ramo inglese, e l'italiano di sempre sugli stessi casi), la scadenza dei contrassegni,
/// quando di eventi e inviti, la risposta iMIP all'organizzatore (oggetto e testo in inglese, la parte text/calendar uguale),
/// i promemoria in ritardo, le risposte automatiche, la regola in parole e le sue domande, Esegui ora, Persone (numeri,
/// righe, conferma di eliminazione, contatto senza nome), l'annullamento di una riunione, Posponi, gli errori di Google.
/// Le prove in inglese usano en-GB (Force("en", "en-GB")): la cultura che Missiva sceglie su un Windows con i formati
/// italiani; la ricorrenza anche con en-US, dove la data breve e il giorno del mese cambiano ordine.
/// </summary>
static partial class ProvaBozze
{
    static void LanguageCalendarTests()
    {
        Report.Add("--- Lingua, area agenda: l'inglese");
        try
        {
            LinAgRecurrence();
            LinAgFlags();
            LinAgWhen();
            LinAgReply();
            LinAgReminders();
            LinAgAutoReplies();
            LinAgRules();
            LinAgPeople();
            LinAgCalendarTexts();
            Wait(LinAgGoogleErrorsAsync);
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    /// <summary>La differenza fra due elenchi di testi, per il resoconto: solo quelli diversi.</summary>
    static string LinAgDiff(IReadOnlyList<string> got, IReadOnlyList<string> want) =>
        string.Join(" | ", got.Zip(want).Where(p => p.First != p.Second).Select(p => $"\"{p.First}\" invece di \"{p.Second}\""));

    /// <summary>Invitation.RecurrenceText: in inglese frasi inglesi (RecurrenceEnglish), in italiano byte per byte come prima.</summary>
    static void LinAgRecurrence()
    {
        var monday = new DateTime(2026, 10, 5, 9, 0, 0);
        var sunday = new DateTime(2026, 10, 4, 9, 0, 0);
        (string Rule, DateTime Start, string It, string En)[] cases =
        [
            ("FREQ=DAILY", monday, "Ogni giorno", "Every day"),
            ("FREQ=DAILY;INTERVAL=3", monday, "Ogni 3 giorni", "Every 3 days"),
            ("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE", monday, "Ogni 2 settimane il lunedì e il mercoledì", "Every 2 weeks on Monday and Wednesday"),
            ("FREQ=WEEKLY;BYDAY=MO,WE,FR;UNTIL=20261231T235959Z", monday, "Ogni settimana il lunedì, il mercoledì e il venerdì, fino al 31/12/2026",
             "Every week on Monday, Wednesday, and Friday, until 31/12/2026"),
            ("FREQ=WEEKLY", monday, "Ogni settimana il lunedì", "Every week on Monday"),
            ("FREQ=WEEKLY", sunday, "Ogni settimana la domenica", "Every week on Sunday"),
            ("FREQ=WEEKLY;BYDAY=SU,SA", monday, "Ogni settimana la domenica e il sabato", "Every week on Sunday and Saturday"),
            ("FREQ=MONTHLY;BYDAY=-1FR", monday, "Ogni mese, l'ultimo venerdì", "Every month on the last Friday"),
            ("FREQ=MONTHLY;BYDAY=2TU;COUNT=6", monday, "Ogni mese, il 2° martedì, 6 volte", "Every month on the 2nd Tuesday, 6 times"),
            ("FREQ=MONTHLY;BYDAY=3TH", monday, "Ogni mese, il 3° giovedì", "Every month on the 3rd Thursday"),
            ("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=15", monday, "Ogni 2 mesi, il giorno 15", "Every 2 months on day 15"),
            ("FREQ=MONTHLY", monday, "Ogni mese, il giorno 5", "Every month on day 5"),
            ("FREQ=YEARLY;COUNT=1", monday, "Ogni anno il 5 ottobre, 1 volte", "Every year on 5 October, once"),
            ("FREQ=YEARLY;INTERVAL=2", monday, "Ogni 2 anni il 5 ottobre", "Every 2 years on 5 October"),
            ("FREQ=HOURLY", monday, "Si ripete", "Repeats"),
        ];
        Lingua.Force("it");
        var italian = cases.Select(c => Invitation.RecurrenceText(c.Rule, c.Start)).ToList();
        Lingua.Force("en", "en-GB");
        var english = cases.Select(c => Invitation.RecurrenceText(c.Rule, c.Start)).ToList();
        string lastDay = Invitation.RecurrenceText("FREQ=MONTHLY;BYMONTHDAY=-1", monday), fifth = Invitation.RecurrenceText("FREQ=MONTHLY;BYDAY=11MO", monday);
        Lingua.Force("en", "en-US");
        string us = Invitation.RecurrenceText("FREQ=YEARLY;UNTIL=20271231", monday);
        Lingua.Force("it");
        var wantIt = cases.Select(c => c.It).ToList();
        var wantEn = cases.Select(c => c.En).ToList();
        Check(italian.SequenceEqual(wantIt),
              $"lingua-agenda, la ricorrenza in parole in italiano è quella di sempre, con articoli e accordi ({cases.Length} casi: ogni giorno, ogni 2 settimane il lunedì e il mercoledì, la domenica, l'ultimo venerdì, il 2° martedì, il giorno 15, ogni anno, volte e fino al)" +
              LinAgDiff(italian, wantIt));
        Check(english.SequenceEqual(wantEn) && lastDay == "Every month on the last day" && fifth == "Every month on the 11th Monday" && us == "Every year on October 5, until 12/31/2027",
              $"lingua-agenda, la ricorrenza in inglese è una frase inglese: \"{english[2]}\", \"{english[3]}\", \"{english[7]}\", \"{english[8]}\", \"{english[12]}\"; l'ultimo giorno del mese " +
              $"(\"{lastDay}\"), l'11° (\"{fifth}\"); con en-US il giorno e la data breve all'americana (\"{us}\")" + LinAgDiff(english, wantEn));
    }

    /// <summary>Flags: la scadenza della riga ("Due today", "Due Fri 2 Oct"), i gruppi per scadenza e le scelte del sottomenu.</summary>
    static void LinAgFlags()
    {
        var today = new DateTime(2026, 9, 28); // un lunedì
        string[] Texts() =>
        [
            Flags.DueText(today, today), Flags.DueText(today.AddDays(1), today), Flags.DueText(today.AddDays(-1), today), Flags.DueText(new DateTime(2026, 10, 2), today),
            Flags.DueText(new DateTime(2027, 1, 15), today),
            Flags.Group(today.AddDays(-2), today), Flags.Group(today, today), Flags.Group(today.AddDays(1), today), Flags.Group(new DateTime(2026, 10, 2), today),
            Flags.Group(today.AddDays(9), today), Flags.Group(null, today),
            string.Join(", ", Flags.Choices(today).Select(c => c.Name)),
        ];
        Lingua.Force("it");
        var italian = Texts();
        Lingua.Force("en", "en-GB");
        var english = Texts();
        Lingua.Force("it");
        string[] wantIt = ["Scadenza: oggi", "Scadenza: domani", "Scadenza: ieri", "Scadenza: ven 2 ott", "Scadenza: 15 gen 2027", "In ritardo", "Oggi", "Domani", "Questa settimana", "Più avanti", "Senza data",
                           "Oggi, Domani, Questa settimana, Settimana prossima"];
        string[] wantEn = ["Due today", "Due tomorrow", "Due yesterday", "Due Fri 2 Oct", "Due 15 Jan 2027", "Overdue", "Today", "Tomorrow", "This week", "Later", "No date",
                           "Today, Tomorrow, This week, Next week"];
        Check(italian.SequenceEqual(wantIt) && english.SequenceEqual(wantEn),
              $"lingua-agenda, contrassegni: \"Scadenza: oggi\" diventa \"{english[0]}\", il giorno \"{english[3]}\" (giorno e mese della cultura inglese), i gruppi \"{english[5]}\", \"{english[8]}\", \"{english[10]}\" e le scelte del sottomenu; " +
              "in italiano come prima" + LinAgDiff(italian, wantIt) + LinAgDiff(english, wantEn));
    }

    /// <summary>Quando, in parole: Calendars.When per gli eventi e Invitation.When per gli inviti.</summary>
    static void LinAgWhen()
    {
        var oneDay = new CalendarEvent { Id = "a", Start = new DateTime(2026, 9, 26), End = new DateTime(2026, 9, 27), AllDay = true };
        var threeDays = new CalendarEvent { Id = "b", Start = new DateTime(2026, 9, 26), End = new DateTime(2026, 9, 29), AllDay = true };
        var timed = new CalendarEvent { Id = "c", Start = new DateTime(2026, 9, 26, 9, 0, 0), End = new DateTime(2026, 9, 26, 10, 0, 0) };
        var inviteDay = new Invitation { Start = new DateTime(2026, 10, 21), End = new DateTime(2026, 10, 22), AllDay = true };
        var inviteDays = new Invitation { Start = new DateTime(2026, 10, 21), End = new DateTime(2026, 10, 24), AllDay = true };
        string[] Texts() => [Calendars.When(oneDay), Calendars.When(threeDays), Calendars.When(timed), inviteDay.When, inviteDays.When,
                             Invitation.Verb(InviteAnswer.Accept), Invitation.Verb(InviteAnswer.Tentative), Invitation.Verb(InviteAnswer.Decline), Invitation.Verb(InviteAnswer.Propose)];
        Lingua.Force("it");
        var italian = Texts();
        Lingua.Force("en", "en-GB");
        var english = Texts();
        Lingua.Force("it");
        string[] wantIt = ["sabato 26 settembre, tutto il giorno", "dal sabato 26 settembre al lunedì 28 settembre, tutto il giorno", "sabato 26 settembre, 09:00 - 10:00",
                           "mercoledì 21 ottobre 2026, tutto il giorno", "da mercoledì 21 ottobre a venerdì 23 ottobre 2026", "Accettato", "Provvisorio", "Rifiutato", "Nuovo orario proposto"];
        string[] wantEn = ["Saturday 26 September, all day", "Saturday 26 September to Monday 28 September, all day", "Saturday 26 September, 09:00 - 10:00",
                           "Wednesday 21 October 2026, all day", "Wednesday 21 October to Friday 23 October 2026", "Accepted", "Tentative", "Declined", "New time proposed"];
        Check(italian.SequenceEqual(wantIt) && english.SequenceEqual(wantEn),
              $"lingua-agenda, quando in parole: \"{english[0]}\", \"{english[1]}\", \"{english[2]}\", l'invito \"{english[4]}\", e l'oggetto della risposta (\"{english[5]}\", \"{english[8]}\"); in italiano come prima" +
              LinAgDiff(italian, wantIt) + LinAgDiff(english, wantEn));
    }

    /// <summary>
    /// La risposta iMIP a un invito: oggetto e testo nella lingua di Missiva (una frase intera per risposta), la parte
    /// text/calendar uguale nelle due lingue (a parte DTSTAMP, l'ora in cui si scrive).
    /// </summary>
    static void LinAgReply()
    {
        const string ics = "BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:prova-lingua-1\r\nSEQUENCE:2\r\nDTSTART;VALUE=DATE:20261021\r\nDTEND;VALUE=DATE:20261022\r\n" +
                           "SUMMARY:Riunione di prova\r\nORGANIZER;CN=Ada Galli:mailto:ada@studio-prova.example\r\nATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:mario@studio-prova.example\r\n" +
                           "END:VEVENT\r\nEND:VCALENDAR\r\n";
        var invite = Invitation.Parse(ics)!;
        var account = new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", DisplayName = "Mario Rossi" };
        var accept = new InviteResponse(InviteAnswer.Accept, "", true);
        var propose = new InviteResponse(InviteAnswer.Propose, "Va bene il pomeriggio?", true, new DateTime(2026, 10, 21, 15, 0, 0), new DateTime(2026, 10, 21, 16, 30, 0));
        static string Calendar(OutgoingMessage m) => string.Join("\n", (m.Calendar ?? "").Split("\r\n").Where(l => !l.StartsWith("DTSTAMP:", StringComparison.Ordinal)));
        Lingua.Force("it");
        var itAccept = invite.ReplyMessage(account, accept);
        var itPropose = invite.ReplyMessage(account, propose);
        Lingua.Force("en", "en-GB");
        var enAccept = invite.ReplyMessage(account, accept);
        var enPropose = invite.ReplyMessage(account, propose);
        var enDecline = invite.ReplyMessage(account, new InviteResponse(InviteAnswer.Decline, "", true));
        Lingua.Force("it");
        bool italian = itAccept.Subject == "Accettato: Riunione di prova" && itAccept.Text == "Mario Rossi ha accettato l'invito \"Riunione di prova\"."
                       && itPropose.Text == "Mario Rossi propone un nuovo orario per l'invito \"Riunione di prova\": mercoledì 21 ottobre 2026, 15:00 - 16:30.\n\nVa bene il pomeriggio?";
        bool english = enAccept.Subject == "Accepted: Riunione di prova" && enAccept.Text == "Mario Rossi accepted the invitation \"Riunione di prova\"."
                       && enDecline.Subject == "Declined: Riunione di prova" && enDecline.Text == "Mario Rossi declined the invitation \"Riunione di prova\"."
                       && enPropose.Subject == "New time proposed: Riunione di prova"
                       && enPropose.Text == "Mario Rossi proposed a new time for the invitation \"Riunione di prova\": Wednesday 21 October 2026, 15:00 - 16:30.\n\nVa bene il pomeriggio?"
                       && enAccept.Html.Contains("accepted the invitation");
        bool sameCalendar = Calendar(itAccept) == Calendar(enAccept) && Calendar(itPropose) == Calendar(enPropose) && enAccept.CalendarMethod == "REPLY" && enPropose.CalendarMethod == "COUNTER"
                            && Calendar(enAccept).Contains("PARTSTAT=ACCEPTED") && Calendar(enPropose).Contains("METHOD:COUNTER");
        Check(italian && english && sameCalendar,
              $"lingua-agenda, la risposta all'organizzatore: in inglese \"{enAccept.Subject}\", \"{enAccept.Text}\", \"{enPropose.Text.Split('\n')[0]}\" (italiano come prima: {italian}); " +
              $"la parte text/calendar è la stessa nelle due lingue, senza DTSTAMP ({sameCalendar})");
    }

    /// <summary>CalendarReminders.Text: il testo della notifica e la riga del promemoria in ritardo, tre frasi intere (oggi, ieri, un altro giorno).</summary>
    static void LinAgReminders()
    {
        var today = new DateTime(2026, 10, 21);
        var meeting = new CalendarEvent { Id = "r", Start = today.AddHours(9.25), End = today.AddHours(10), ReminderOn = true, ReminderMinutes = 15 };
        var whole = new CalendarEvent { Id = "w", Start = today, End = today.AddDays(1), AllDay = true };
        string[] Texts() =>
        [
            UI.CalendarReminders.Text(meeting, null, today).When, UI.CalendarReminders.Text(whole, null, today).When,
            UI.CalendarReminders.Text(meeting, today.AddHours(9), today).Late ?? "", UI.CalendarReminders.Text(meeting, today.AddDays(-1).AddHours(23.75), today).Late ?? "",
            UI.CalendarReminders.Text(meeting, new DateTime(2026, 10, 16, 17, 15, 0), today).Late ?? "",
        ];
        Lingua.Force("it");
        var italian = Texts();
        Lingua.Force("en", "en-GB");
        var english = Texts();
        Lingua.Force("it");
        string[] wantIt = ["alle 09:15", "tutto il giorno", "Promemoria in ritardo: era alle 09:00", "Promemoria in ritardo: era ieri alle 23:45", "Promemoria in ritardo: era venerdì 16 ottobre alle 17:15"];
        string[] wantEn = ["Today at 09:15", "All day", "Late reminder: it was due at 09:00", "Late reminder: it was due yesterday at 23:45", "Late reminder: it was due on Friday 16 October at 17:15"];
        Check(italian.SequenceEqual(wantIt) && english.SequenceEqual(wantEn),
              $"lingua-agenda, la notifica del promemoria: \"{english[0]}\", \"{english[1]}\", e in ritardo \"{english[2]}\", \"{english[3]}\", \"{english[4]}\"; in italiano come prima" +
              LinAgDiff(italian, wantIt) + LinAgDiff(english, wantEn));
    }

    /// <summary>AutoReplies.Describe: la riga della barra sotto l'account, con gli istanti in parole.</summary>
    static void LinAgAutoReplies()
    {
        var now = new DateTime(2026, 10, 21, 12, 0, 0);
        AutoReplies Scheduled(DateTime start, DateTime end) => new() { State = AutoReplyState.Scheduled, Start = start, End = end };
        string[] Texts() =>
        [
            new AutoReplies { State = AutoReplyState.On }.Describe(now), new AutoReplies().Describe(now),
            Scheduled(new DateTime(2026, 10, 23, 18, 0, 0), new DateTime(2026, 10, 26, 9, 0, 0)).Describe(now),
            Scheduled(new DateTime(2026, 10, 20, 8, 0, 0), new DateTime(2027, 1, 4, 9, 30, 0)).Describe(now),
            Scheduled(new DateTime(2026, 10, 19, 8, 0, 0), new DateTime(2026, 10, 20, 9, 0, 0)).Describe(now),
        ];
        Lingua.Force("it");
        var italian = Texts();
        Lingua.Force("en", "en-GB");
        var english = Texts();
        Lingua.Force("it");
        string[] wantIt = ["Risposte automatiche attive", "Risposte automatiche spente", "Risposte automatiche programmate da venerdì 23 ottobre alle 18:00 a lunedì 26 ottobre alle 9:00",
                           "Risposte automatiche attive fino a lunedì 4 gennaio 2027 alle 9:30", "Risposte automatiche spente: il periodo è finito martedì 20 ottobre alle 9:00"];
        string[] wantEn = ["Automatic replies are on", "Automatic replies are off", "Automatic replies are scheduled from Friday 23 October at 18:00 to Monday 26 October at 9:00",
                           "Automatic replies are on until Monday 4 January 2027 at 9:30", "Automatic replies are off. The scheduled time ended on Tuesday 20 October at 9:00"];
        Check(italian.SequenceEqual(wantIt) && english.SequenceEqual(wantEn),
              $"lingua-agenda, risposte automatiche: \"{english[2]}\", \"{english[3]}\", \"{english[4]}\"; in italiano come prima" + LinAgDiff(italian, wantIt) + LinAgDiff(english, wantEn));
    }

    /// <summary>Regole: la regola in parole, le domande prima di salvare o accendere, la domanda e l'esito di Esegui ora, le azioni fatte.</summary>
    static void LinAgRules()
    {
        var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = "organizations" };
        var personal = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@outlook.example" };
        var full = new MailRule
        {
            Name = "Fatture", FromAddresses = ["amministrazione@fornitore.example"], SenderContains = ["news"], SubjectContains = ["fattura", "avviso"], MoveTo = "f1", MoveToName = "Fatture",
            MarkRead = true, Categories = ["Lavoro"], StopProcessing = true,
        };
        var outward = new MailRule { Name = "Archivio", ForwardTo = [new Address { Email = "copia@esterno.example" }] };
        var dormant = new MailRule { Name = "Archivio", Enabled = false, SubjectContains = ["x"], ForwardTo = [new Address { Email = "a@esterno.example" }, new Address { Email = "b@altro.example" }] };
        var woken = dormant.Copy();
        woken.Enabled = true;
        var inbox = new MailFolder { Id = "INBOX", Name = "Inbox", Kind = FolderKind.Inbox };
        var outcome = new Rules.Outcome { Matched = 3, SkippedForwards = true };
        string[] Texts()
        {
            var forward = Rules.ForwardQuestion(null, outward, work)!.Value;
            var turnOn = Rules.ForwardQuestion(dormant, woken, personal)!.Value;
            var all = Rules.AllMessagesQuestion(new MailRule { MarkRead = true }, work)!.Value;
            outcome.Done.Clear();
            outcome.Errors.Clear();
            outcome.Done.Add(T("{0} segnati come letti", 3));
            outcome.Errors.Add(T("{0} non spostati in {1}", 2, "Fatture") + ": prova");
            return
            [
                Rules.Describe(full), Rules.Describe(new MailRule()), forward.Heading, forward.Body, forward.Confirm, turnOn.Heading, turnOn.Body, turnOn.Confirm, all.Heading, all.Body, all.Confirm,
                UI.RulesView.RunQuestion(new MailRule { Delete = true }, inbox, 4, 10, "-"), UI.RulesView.RunResult("Fatture", "Inbox", 10, outcome),
                UI.RulesView.RunResult("Fatture", "Inbox", 10, new Rules.Outcome()),
            ];
        }
        Lingua.Force("it");
        var italian = Texts();
        Lingua.Force("en", "en-GB");
        var english = Texts();
        Lingua.Force("it");
        string[] wantIt =
        [
            "Se il mittente è o contiene amministrazione@fornitore.example o \"news\" e l'oggetto contiene \"fattura\" o \"avviso\": sposta in Fatture, segna come letto, categoria \"Lavoro\", non applicare altre regole",
            "Per tutti i messaggi: nessuna azione",
            "La regola vale per tutti i messaggi e li manda fuori",
            "Non hai scelto condizioni: la regola agirà su ogni messaggio che arriva nella Posta in arrivo, e tutti verranno inoltrati a copia@esterno.example, fuori dalla tua organizzazione, anche quando non guardi. " +
            "Chi entra di nascosto in una casella crea proprio regole così: salvala solo se l'indirizzo è tuo o di qualcuno di cui ti fidi.",
            "Inoltra a copia@esterno.example",
            "La regola manda la tua posta fuori",
            "Con la regola \"Archivio\" accesa, i messaggi che corrispondono verranno inoltrati a a@esterno.example, b@altro.example, fuori dalla tua casella, anche quando non guardi. " +
            "Chi entra di nascosto in una casella lascia proprio regole così: accendila solo se l'hai creata tu e gli indirizzi sono tuoi o di qualcuno di cui ti fidi.",
            "Accendi la regola",
            "La regola vale per tutti i messaggi",
            "Non hai scelto condizioni: la regola agirà su ogni messaggio che arriva nella Posta in arrivo. Salvarla lo stesso?",
            "Salva",
            "La regola mette nel Cestino 4 dei 10 messaggi di Inbox: nel Cestino, non per sempre, da lì si riprendono. La regola non ha condizioni: vale per ogni messaggio.\n\n-",
            "Esegui ora \"Fatture\" su Inbox: 3 dei 10 messaggi corrispondono; 3 segnati come letti. Non riuscito: 2 non spostati in Fatture: prova. L'inoltro lo fa solo Outlook, sui messaggi che arrivano.",
            "Esegui ora \"Fatture\" su Inbox: nessuno dei 10 messaggi corrisponde.",
        ];
        string[] wantEn =
        [
            "If the sender is or contains amministrazione@fornitore.example or \"news\" and the subject contains \"fattura\" or \"avviso\": move to Fatture, mark as read, category \"Lavoro\", stop processing more rules",
            "For all messages: no action",
            "The rule applies to all messages and sends them outside",
            "You didn't choose any conditions: the rule will act on every message that arrives in the Inbox, and all of them will be forwarded to copia@esterno.example, outside your organization, even when you're not looking. " +
            "Someone who secretly gets into a mailbox creates exactly this kind of rule: save it only if the address is yours or belongs to someone you trust.",
            "Forward to copia@esterno.example",
            "The rule sends your mail outside",
            "With the rule \"Archivio\" on, matching messages will be forwarded to a@esterno.example, b@altro.example, outside your mailbox, even when you're not looking. " +
            "Someone who secretly gets into a mailbox leaves exactly this kind of rule: turn it on only if you created it and the addresses are yours or belong to someone you trust.",
            "Turn on the rule",
            "The rule applies to all messages",
            "You didn't choose any conditions: the rule will act on every message that arrives in the Inbox. Save it anyway?",
            "Save",
            "The rule moves 4 of the 10 messages in Inbox to Deleted Items: not permanently, you can get them back from there. The rule has no conditions: it applies to every message.\n\n-",
            "Run now \"Fatture\" on Inbox: 3 of the 10 messages match; 3 marked as read. Didn't work: 2 not moved to Fatture: prova. Only Outlook forwards, on incoming messages.",
            "Run now \"Fatture\" on Inbox: none of the 10 messages match.",
        ];
        Check(italian.SequenceEqual(wantIt),
              "lingua-agenda, regole in italiano come prima: la regola in parole, le domande sugli inoltri verso fuori (regola nuova senza condizioni, regola spenta che si accende con due indirizzi), " +
              "quella senza condizioni, la domanda e l'esito di Esegui ora" + LinAgDiff(italian, wantIt));
        Check(english.SequenceEqual(wantEn),
              $"lingua-agenda, regole in inglese, una frase intera per caso: \"{english[0]}\"; \"{english[3][..60]}...\"; \"{english[6][..60]}...\"; \"{english[12]}\"" + LinAgDiff(english, wantEn));
    }

    /// <summary>Persone: i numeri, il nome per UI Automation delle righe, la conferma di eliminazione, il contatto senza nome in inglese.</summary>
    static void LinAgPeople()
    {
        using var google = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "ada@prova.example" });
        var outlook = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@prova.example" });
        var anna = new Contact { DisplayName = "Anna Bianchi", Emails = ["anna@prova.example"] };
        var group = new Contact { IsGroup = true, DisplayName = "Soci", Members = ["Luca <luca@prova.example>"] };
        const string vcards = "BEGIN:VCARD\r\nVERSION:3.0\r\nNOTE:solo una nota\r\nEND:VCARD\r\nBEGIN:VCARD\r\nVERSION:3.0\r\nFN:Paolo Neri\r\nEND:VCARD\r\n";
        string[] Texts() =>
        [
            UI.PeopleView.PeopleCount(1), UI.PeopleView.PeopleCount(3), new UI.PeopleView.Row(anna, google).ToString(), new UI.PeopleView.Row(group, null).ToString(),
            new UI.PeopleView.Row(group, null).Detail, UI.PeopleView.DeleteWarning(google), UI.PeopleView.DeleteWarning(outlook), new Contact().Name,
            string.Join(", ", ContactFiles.VCard(vcards).Select(c => c.Name)),
        ];
        Lingua.Force("it");
        var italian = Texts();
        Lingua.Force("en", "en-GB");
        var english = Texts();
        Lingua.Force("it");
        string[] wantIt = ["1 persona", "3 persone", "Anna Bianchi, anna@prova.example, Google di ada@prova.example", "Gruppo Soci, 1 persone, in Missiva", "Gruppo, 1 persona",
                           "Elimina da Google Contatti: sparisce anche dal telefono e da tutti i tuoi dispositivi.",
                           "Elimina da Outlook: sparisce anche dalle altre app e dai dispositivi che usano mario@prova.example.", "(senza nome)", "Paolo Neri"];
        string[] wantEn = ["1 person", "3 people", "Anna Bianchi, anna@prova.example, Google (ada@prova.example)", "Group Soci, 1 person, in Missiva", "Group, 1 person",
                           "Delete from Google Contacts: it also disappears from your phone and all your devices.",
                           "Delete from Outlook: it also disappears from the other apps and devices that use mario@prova.example.", "(No name)", "Paolo Neri"];
        Check(italian.SequenceEqual(wantIt) && english.SequenceEqual(wantEn),
              $"lingua-agenda, Persone: \"{english[0]}\", \"{english[1]}\", le righe \"{english[2]}\" e \"{english[3]}\" (in italiano il gruppo dice ancora \"1 persone\", com'era), la conferma \"{english[5]}\"; " +
              $"un contatto vuoto è \"{english[7]}\" e la lettura delle vCard lo scarta anche in inglese ({english[8]})" + LinAgDiff(italian, wantIt) + LinAgDiff(english, wantEn));
    }

    /// <summary>Calendario: la conferma di Annulla riunione (Google e Microsoft) e Posponi.</summary>
    static void LinAgCalendarTexts()
    {
        var later = DateTime.Today.AddDays(10).AddHours(8);
        string[] Texts() =>
        [
            UI.CalendarView.CancelPrompt(AccountKind.Gmail, "Bilancio", 3).Text, UI.CalendarView.CancelPrompt(AccountKind.Microsoft, "Bilancio", 3).Text,
            UI.SnoozeWindow.Describe(later),
            UI.CalendarView.CancelPrompt(AccountKind.Gmail, "Bilancio", 1).Text, UI.CalendarView.CancelPrompt(AccountKind.Microsoft, "Bilancio", 1).Text,
        ];
        Lingua.Force("it");
        var italian = Texts();
        Lingua.Force("en", "en-GB");
        var english = Texts();
        Lingua.Force("it");
        string[] wantIt =
        [
            "\"Bilancio\" esce dai calendari dei 3 partecipanti, che ricevono da Google l'avviso che è annullata. Google Calendar non permette di aggiungere una nota all'annullamento: se serve, scrivi loro un messaggio.",
            "\"Bilancio\" esce dai calendari dei 3 partecipanti, che ricevono l'annullamento con questa nota.",
            later.ToString("d MMM HH:mm", System.Globalization.CultureInfo.GetCultureInfo("it-IT")),
            "\"Bilancio\" esce dai calendari dei 1 partecipanti, che ricevono da Google l'avviso che è annullata. Google Calendar non permette di aggiungere una nota all'annullamento: se serve, scrivi loro un messaggio.",
            "\"Bilancio\" esce dai calendari dei 1 partecipanti, che ricevono l'annullamento con questa nota.",
        ];
        string[] wantEn =
        [
            "\"Bilancio\" will be removed from the calendars of its 3 attendees, and Google will let them know it's canceled. Google Calendar doesn't let you add a note to a cancellation: if you need to, send them a message.",
            "\"Bilancio\" will be removed from the calendars of its 3 attendees, who'll get the cancellation with this note.",
            later.ToString("d MMM HH:mm", System.Globalization.CultureInfo.GetCultureInfo("en-GB")),
            "\"Bilancio\" will be removed from the attendee's calendar, and Google will let them know it's canceled. Google Calendar doesn't let you add a note to a cancellation: if you need to, send them a message.",
            "\"Bilancio\" will be removed from the attendee's calendar, and they'll get the cancellation with this note.",
        ];
        Check(italian.SequenceEqual(wantIt) && english.SequenceEqual(wantEn),
              $"lingua-agenda, Annulla riunione (\"{english[1]}\", con un partecipante solo \"{english[4]}\") e Posponi (\"{english[2]}\", il mese della cultura inglese); in italiano come prima" + LinAgDiff(italian, wantIt) + LinAgDiff(english, wantEn));
    }

    /// <summary>
    /// Gli errori di Google spiegati: GoogleService ha due frasi intere segnate con N (senza permesso, richieste limitate),
    /// tradotte dove si mostrano; gestore HTTP finto, nessuna rete.
    /// </summary>
    static async Task LinAgGoogleErrorsAsync()
    {
        HttpStatusCode status = HttpStatusCode.Forbidden;
        string reason = "insufficientPermissions";
        GoogleApi.UseTestHandler(new FakeHttp(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(new JsonObject { ["error"] = new JsonObject { ["code"] = (int)status, ["message"] = "Request had insufficient authentication scopes.",
                                                                                      ["errors"] = new JsonArray(new JsonObject { ["reason"] = reason }) } }.ToJsonString(), Encoding.UTF8, "application/json"),
        }));
        GoogleAuth.TestToken = "prova-google";
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "ada@prova.example", GoogleScopes = "https://mail.google.com/ " + GoogleAuth.ContactsScope };
        using var google = new GmailProvider(account);
        async Task<string> Error()
        {
            try
            {
                await google.GetContactsAsync();
                return "";
            }
            catch (MailException ex)
            {
                return ex.Message;
            }
        }
        try
        {
            var texts = new List<string>();
            foreach (var language in new[] { "it", "en" })
            {
                if (language == "en") Lingua.Force("en", "en-GB");
                else Lingua.Force("it");
                (status, reason) = (HttpStatusCode.Forbidden, "insufficientPermissions");
                texts.Add(await Error());
                (status, reason) = ((HttpStatusCode)429, "rateLimitExceeded");
                texts.Add(await Error());
                (status, reason) = (HttpStatusCode.NotFound, "notFound");
                texts.Add(await Error());
            }
            Lingua.Force("it");
            string[] want =
            [
                "Missiva non ha il permesso di usare i contatti di questo account: premi \"Consenti l'accesso ai contatti\" in Persone.",
                "Google ha limitato le richieste (i contatti): riprova fra poco.",
                "L'elemento non c'è più su Google.",
                "Missiva doesn't have permission to use this account's contacts. Select \"Allow access to contacts\" in People.",
                "Google is limiting requests (contacts). Try again in a moment.",
                "The item is no longer on Google.",
            ];
            Check(texts.SequenceEqual(want),
                  $"lingua-agenda, gli errori di Google (gestore finto): senza permesso, richieste limitate e 404 in italiano come prima e in inglese (\"{texts.ElementAtOrDefault(3)}\")" + LinAgDiff(texts, want));
        }
        finally
        {
            GoogleAuth.TestToken = null;
            Lingua.Force("it");
        }
    }
}
#endif
