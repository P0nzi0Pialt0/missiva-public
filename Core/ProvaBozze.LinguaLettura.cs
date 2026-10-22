#if DEBUG
using System.Net;
using System.Windows.Media;

namespace Missiva;

/// <summary>
/// Gruppo "lingua-lettura" (Missiva in inglese, 21 ottobre 2026): i controlli dell'inglese dell'area "lettura", scritti da
/// chi la traduce, uno per ogni logica nuova in inglese (date, ricorrenze, plurali, parole chiave, formati, testi composti),
/// con Lingua.Force("en") e l'italiano di nuovo in un finally (Lingua.Force("it")): un gruppo che lascia l'inglese è un
/// controllo non passato. Le traduzioni stanno in Assets/lingue/en/lettura.json; che ogni testo dei sorgenti ne abbia
/// una lo controlla già il gruppo "lingua". Da solo con "Missiva.exe --prova-audit risultato.txt lingua-lettura".
/// Già registrato in ProvaBozze.Gruppi.cs: non serve toccarlo. Vuoto non è un errore (zero controlli).
/// </summary>
/// <remarks>
/// Che cosa si prova: la pagina del riquadro (lang, intestazione, data, destinatari, fasce del mittente e della posta
/// certificata, immagini bloccate, Annulla iscrizione, allegati, scheda dell'invito, riquadro vuoto), senza un testo che
/// resti in italiano né una traduzione che manchi (il registro); le fasce di Pec e P7m con le date nella cultura
/// inglese; quello che parte con un messaggio (prefissi dell'oggetto, riga della citazione, anche nel MIME costruito);
/// il riconoscimento dei prefissi delle due lingue in tutte e due; e in italiano, dove la logica è nuova, che il testo
/// sia quello di prima. Le date sono fisse (21 ottobre 2026, 14:30) e la cultura inglese è en-US (Force("en", "en-US")),
/// così i controlli non dipendono dai formati di Windows.
/// </remarks>
static partial class ProvaBozze
{
    static void LanguageReadingTests()
    {
        Report.Add("--- Lingua, area lettura: l'inglese");
        try
        {
            LletPage();
            LletInvite();
            LletBands();
            LletOutgoing();
            LletQuestions();
        }
        finally
        {
            Lingua.Force("it");
        }
    }

    static readonly DateTime LletDate = new(2026, 10, 21, 14, 30, 0);

    /// <summary>Lo spazio stretto che ICU mette prima di AM e PM, come uno spazio normale: i controlli non dipendono dalla versione di ICU.</summary>
    static string LletSpaces(string text) => text.Replace(' ', ' ').Replace(' ', ' ');

    /// <summary>Le righe "lingua: manca la traduzione" del registro di questo giro.</summary>
    static int LletMissingLines()
    {
        var log = Path.Combine(Log.TestDir ?? Store.Dir, "missiva.log");
        return File.Exists(log) ? File.ReadAllLines(log).Count(l => l.Contains("lingua: manca la traduzione")) : 0;
    }

    /// <summary>Un messaggio con tutto quello che la pagina può dire: destinatari, Reply-To, fasce, posta certificata, immagine esterna, allegati.</summary>
    static UI.PageEntry LletEntry()
    {
        var summary = new MessageSummary
        {
            Id = "1", FolderId = "INBOX", Subject = "Offerta", Date = LletDate,
            From = new Address { Name = "Mario Rossi", Email = "mario.rossi@esempio-prova.example" },
            ReplyTo = [new Address { Name = "Ufficio", Email = "ufficio@altro-prova.example" }],
        };
        var body = new MessageBody
        {
            Html = "<html><body><p>Buongiorno</p><img src=\"https://immagini.esempio-prova.example/foto.png\" width=\"300\" height=\"200\" alt=\"foto\"></body></html>",
            To = [new Address { Name = "Ada Galli", Email = "ada@studio-prova.example" }],
            Cc = [new Address { Name = "Luca Bianchi", Email = "luca@studio-prova.example" }],
            Attachments =
            [
                new Attachment { Id = "0", Name = "preventivo.pdf", ContentType = "application/pdf", Size = 1572864 },
                new Attachment { Id = "1", Name = "installa.exe", ContentType = "application/octet-stream", Size = 2048 },
            ],
            Pec = new PecInfo("posta-certificata", "mario.rossi@pec-prova.example", "Offerta", "Gestore Prova", LletDate, ["ada@pec-prova.example"], null, null, null,
                              SignatureState.Verified, "Gestore Prova", "posta-certificata@pec-prova.example", "Gestore Prova"),
        };
        var notes = new SenderNotes("esempio-prova.example", "perché", true, "mario.rossi@vecchio-prova.example") { NameAddress = "altro" };
        return new UI.PageEntry(summary, body, true, false, null, false, notes, null, false, "", null);
    }

    static void LletPage()
    {
        var entry = LletEntry();
        var header = new UI.PageHeader("Offerta", true, ("ada@studio-prova.example", Colors.SteelBlue));
        static bool Has(string html, string text) => LletSpaces(html).Contains(LletSpaces(WebUtility.HtmlEncode(text)), StringComparison.Ordinal);
        string english, empty, italian, date;
        int missingBefore = LletMissingLines(), missingAfter;
        try
        {
            Lingua.Force("en", "en-US");
            english = UI.ReadingPage.Build([entry], light: true, header: header).Html;
            empty = UI.ReadingPage.Empty(true, new UI.EmptyLook("Segoe UI", 14, Colors.Gray, Colors.DimGray, 1, 0)).Html;
            date = UI.ReadingPage.DateText(LletDate);
            missingAfter = LletMissingLines();
        }
        finally
        {
            Lingua.Force("it");
        }
        italian = UI.ReadingPage.Build([entry], light: true, header: header).Html;

        Check(english.Contains("<html lang=\"en\"") && italian.Contains("<html lang=\"it\"") && empty.Contains("<html lang=\"en\"") && Has(empty, "Select an item to read"),
              "lingua-lettura, la pagina del riquadro ha lang=\"en\" in inglese e \"it\" in italiano (Lingua.Tag), anche il riquadro vuoto (\"Select an item to read\")");
        Check(LletSpaces(date) == "Wed 10/21/2026 2:30 PM" && Has(english, date) && UI.ReadingPage.DateText(LletDate) == "mer 21/10/2026 14:30" && italian.Contains("mer 21/10/2026 14:30"),
              $"lingua-lettura, la data dell'intestazione in inglese è giorno, data e ora brevi della cultura (en-US: \"{date}\"), in italiano \"mer 21/10/2026 14:30\" come prima");

        bool head = Has(english, "Reply") && Has(english, "Reply all") && Has(english, "Forward") && Has(english, "More actions") && Has(english, "To: Ada Galli")
                    && Has(english, "Replies go to: Ufficio <ufficio@altro-prova.example>") && Has(english, "External") && Has(english, "Profile card for Mario Rossi")
                    && Has(english, "This message is in the ada@studio-prova.example account") && Has(english, "Show the message on a dark background");
        Check(head, "lingua-lettura, l'intestazione in inglese: Reply, Reply all, Forward, More actions, \"To:\", \"Replies go to:\", External, la scheda della persona, l'account e il sole");

        bool bands = Has(english, "We couldn't verify that this message comes from esempio-prova.example. Be careful with links, attachments, and payment requests.")
                     && Has(english, "Mario Rossi is the name of one of your contacts, who you know as mario.rossi@vecchio-prova.example: this message comes from a different address, mario.rossi@esempio-prova.example.")
                     && Has(english, "The sender presents themselves as “Mario Rossi”, but this message comes from a different address: mario.rossi@esempio-prova.example.")
                     && Has(english, "Certified email: sent by mario.rossi@pec-prova.example on 10/21/2026 at 2:30 PM, provider Gestore Prova; provider signature verified (Gestore Prova).")
                     && Has(english, "Signed by Gestore Prova");
        Check(bands, "lingua-lettura, le fasce in inglese: mittente non verificato, nome di un contatto, nome con l'indirizzo di un altro, posta certificata con data e ora en-US e il suggerimento della firma");

        bool images = Has(english, "External images blocked to protect your privacy.") && Has(english, "Show images")
                      && Has(english, "An external image was blocked: if it's downloaded, the sender can tell that you read the message. If you show them, Missiva will remember it for ");
        bool unsubscribe = Has(english, "Getting too many messages from this sender?") && Has(english, "Unsubscribe")
                           && Has(english, "Asks the sender to stop sending you these messages. Missiva tells you what it will do first.");
        bool files = Has(english, "Save all") && Has(english, "Save as…") && Has(english, "Warning: this type of file can run programs") && Has(english, "1.5 MB")
                     && Has(english, "Save all attachments to a folder") && italian.Contains("1,5 MB");
        Check(images && unsubscribe && files,
              $"lingua-lettura, in inglese immagini bloccate (riga, pulsante e suggerimento in due frasi), Annulla iscrizione, allegati (Save all, Save as…, l'avviso del file che avvia programmi, \"1.5 MB\"; in italiano \"1,5 MB\") ({images}, {unsubscribe}, {files})");

        string[] italianWords = ["Rispondi", "Inoltra", "Altre azioni", "Mostra immagini", "Salva tutti", "Esterno", "Le risposte vanno a", "Scheda di", "Annulla iscrizione", "Posta certificata", "Seleziona un elemento"];
        // Senza il foglio di stile della pagina, che ha i commenti in italiano (non vanno sullo schermo).
        static string Shown(string html) => System.Text.RegularExpressions.Regex.Replace(html, "<style>.*?</style>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        var left = italianWords.Where(w => Shown(english).Contains(w) || Shown(empty).Contains(w)).ToList();
        Check(left.Count == 0 && missingAfter == missingBefore,
              $"lingua-lettura, nella pagina inglese nessun testo di Missiva resta in italiano ({(left.Count == 0 ? "nessuno" : string.Join(", ", left))}) e nessuna traduzione manca " +
              $"(righe \"lingua: manca la traduzione\" nel registro: {missingAfter - missingBefore} nuove)");
    }

    static void LletInvite()
    {
        var request = Invitation.Parse(Invites[1].Ics)!;
        var reply = Invitation.Parse(Invites[1].Ics.Replace("METHOD:REQUEST", "METHOD:REPLY").Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=ACCEPTED"))!;
        string Card(Invitation invite)
        {
            var summary = new MessageSummary { Id = "1", FolderId = "INBOX", Subject = invite.Summary, From = new Address { Email = "luca.verdi@esempio-prova.example" }, Date = LletDate };
            return UI.ReadingPage.Build([new UI.PageEntry(summary, new MessageBody { Html = "<p>Invito</p>", Invite = invite }, true, false, null, false)], light: true).Html;
        }
        string requestCard, replyCard, italianReply;
        try
        {
            Lingua.Force("en", "en-US");
            requestCard = Card(request);
            replyCard = Card(reply);
        }
        finally
        {
            Lingua.Force("it");
        }
        italianReply = Card(reply);
        static bool Has(string html, string text) => html.Contains(WebUtility.HtmlEncode(text), StringComparison.Ordinal);
        bool buttons = Has(requestCard, "Accept") && Has(requestCard, "Tentative") && Has(requestCard, "Decline") && Has(requestCard, "Propose new time")
                       && requestCard.Contains("aria-label=\"Invitation\"") && Has(requestCard, "When") && Has(requestCard, "Organizer") && Has(requestCard, "Attendees")
                       && Has(requestCard, "Join the online meeting (meet.google.com)");
        Check(buttons && Has(replyCard, "ada.prova@gmail.com accepted: Punto settimanale") && replyCard.Contains("aria-label=\"Response to an invitation\"")
              && italianReply.Contains("ada.prova@gmail.com ha accettato: Punto settimanale"),
              "lingua-lettura, la scheda dell'invito in inglese: righe (When, Organizer, Attendees), link della riunione, pulsanti (Accept, Tentative, Decline, Propose new time), " +
              "il titolo di una risposta come frase intera (\"... accepted: Punto settimanale\"; in italiano \"... ha accettato: ...\" come prima)");
    }

    static void LletBands()
    {
        var verified = new PecInfo("", "mario.rossi@esempio-prova.example", "", "", null, [], null, null, null, SignatureState.Verified, "Mario Rossi", "mario.rossi@esempio-prova.example");
        var fake = new PecInfo("posta-certificata", "mario.rossi@pec-prova.example", "Offerta", "", LletDate, [], null, null, null, SignatureState.Missing, "");
        var anomaly = new PecInfo("anomalia", "", "Offerta", "", null, [], null, null, null, SignatureState.Untrusted, "Gestore Prova");
        var notDelivered = new PecInfo("errore-consegna", "", "Offerta", "", LletDate, ["ada@pec-prova.example"], null, "casella piena", null, SignatureState.Verified, "Gestore Prova",
                                       "posta-certificata@pec-prova.example", "Gestore Prova");
        var two = new P7m.Result([], "fattura.xml", "Anna Neri", null, SignatureState.Untrusted, 2, ["Anna Neri", "Bruno Verdi"]);
        var one = new P7m.Result([], "fattura.xml", "Anna Neri", LletDate, SignatureState.Verified, 1, ["Anna Neri"]);
        string[] english, italian;
        string[] Bands() =>
        [
            Pec.Band(verified).Text, Pec.Band(fake).Text, Pec.Band(anomaly).Text, Pec.Band(notDelivered).Text, P7m.Band(two).Text, P7m.Band(one).Text,
        ];
        try
        {
            Lingua.Force("en", "en-US");
            english = Bands().Select(LletSpaces).ToArray();
        }
        finally
        {
            Lingua.Force("it");
        }
        italian = Bands();
        string[] wantedEnglish =
        [
            "Message signed by Mario Rossi (mario.rossi@esempio-prova.example): signature verified.",
            "This looks like certified email from mario.rossi@pec-prova.example, but the envelope doesn't have the provider's signature: don't trust the sender or the content.",
            "Anomaly: the message \"Offerta\" arrived in the certified mailbox, but it isn't certified (regular email or an invalid envelope): the original is attached, don't trust the sender; the signature is intact, but the certificate of Gestore Prova isn't one that Windows recognizes.",
            "Not delivered: the message \"Offerta\" wasn't delivered to ada@pec-prova.example (casella piena) on 10/21/2026 at 2:30 PM; provider signature verified (Gestore Prova).",
            "Signed by Anna Neri and Bruno Verdi (2 nested signatures): the document is intact, but the certificate of Anna Neri isn't one that Windows recognizes (Italian signing certificates need AgID's list, as in signing apps).",
            "Signed by Anna Neri on 10/21/2026 at 2:30 PM: valid signature, certificate recognized by Windows.",
        ];
        string[] wantedItalian =
        [
            "Messaggio firmato da Mario Rossi (mario.rossi@esempio-prova.example): firma verificata.",
            "Sembra posta certificata di mario.rossi@pec-prova.example, ma la busta non ha la firma del gestore: non fidarti del mittente né del contenuto.",
            "Anomalia: il messaggio \"Offerta\" è arrivato nella casella di posta certificata, ma non è certificato (posta ordinaria o busta non valida): l'originale è allegato, non fidarti del mittente; firma integra, ma il certificato di Gestore Prova non è fra quelli riconosciuti da Windows.",
            "Mancata consegna: il messaggio \"Offerta\" non è stato consegnato a ada@pec-prova.example (casella piena) il 21/10/2026 alle 14:30; firma del gestore verificata (Gestore Prova).",
            "Firmato da Anna Neri e Bruno Verdi (2 firme una dentro l'altra): il documento è integro, ma il certificato di Anna Neri non è fra quelli riconosciuti da Windows (per i certificati di firma italiani serve l'elenco dell'AgID, come nei programmi di firma).",
            "Firmato da Anna Neri il 21/10/2026 alle 14:30: firma valida, certificato riconosciuto da Windows.",
        ];
        var wrongEnglish = english.Where((t, i) => t != wantedEnglish[i]).ToList();
        var wrongItalian = italian.Where((t, i) => t != wantedItalian[i]).ToList();
        Check(wrongEnglish.Count == 0 && wrongItalian.Count == 0,
              "lingua-lettura, le fasce di Pec e P7m sono frasi intere in inglese (firma del mittente, busta senza firma con il mittente, anomalia senza mittente, mancata consegna con errore e data en-US, " +
              "due firmatari con le buste una dentro l'altra, un firmatario con la data) e in italiano le stesse di prima" +
              (wrongEnglish.Count + wrongItalian.Count == 0 ? "" : ": diverse " + string.Join(" | ", wrongEnglish.Concat(wrongItalian))));
    }

    static void LletOutgoing()
    {
        // I prefissi: in ogni lingua quello suo, e uno già presente delle due lingue non si ripete.
        (ComposeMode Mode, string Subject, string Italian, string English)[] cases =
        [
            (ComposeMode.Reply, "Offerta", "R: Offerta", "RE: Offerta"),
            (ComposeMode.ReplyAll, "Offerta", "R: Offerta", "RE: Offerta"),
            (ComposeMode.Forward, "Offerta", "I: Offerta", "FW: Offerta"),
            (ComposeMode.Reply, "R: Offerta", "R: Offerta", "R: Offerta"),
            (ComposeMode.Reply, "RE: Offerta", "RE: Offerta", "RE: Offerta"),
            (ComposeMode.Reply, "re: Offerta", "re: Offerta", "re: Offerta"),
            (ComposeMode.Forward, "I: Offerta", "I: Offerta", "I: Offerta"),
            (ComposeMode.Forward, "FW: Offerta", "FW: Offerta", "FW: Offerta"),
            (ComposeMode.Forward, "Fw: Offerta", "Fw: Offerta", "Fw: Offerta"),
            (ComposeMode.Reply, "FW: Offerta", "R: FW: Offerta", "RE: FW: Offerta"),
            (ComposeMode.Forward, "RE: Offerta", "I: RE: Offerta", "FW: RE: Offerta"),
            (ComposeMode.Reply, "{0} e {1}", "R: {0} e {1}", "RE: {0} e {1}"),
        ];
        var original = new MessageSummary { Id = "7", FolderId = "INBOX", Subject = "Offerta", Date = LletDate, From = new Address { Name = "Mario Rossi", Email = "mario.rossi@esempio-prova.example" } };
        var body = new MessageBody { Html = "<p>Ecco il preventivo.</p>" };
        var account = new MailAccount { Email = "ada@studio-prova.example", Kind = AccountKind.Imap };
        Task<MimeKit.MimeMessage> Reply() => MimeOptions.BuildAsync(
            new OutgoingMessage { Mode = ComposeMode.Reply, Original = original, OriginalBody = body, To = [original.From], Subject = UI.ComposeWindow.SubjectFor(ComposeMode.Reply, original.Subject), Html = "<p>Grazie</p>", Text = "Grazie" },
            account, new ReplySources(), _ => Task.FromResult(body), _ => Task.FromResult<MailKit.IMessageSummary?>(null), (_, _) => Task.FromResult(Array.Empty<byte>()));
        string[] english, italian;
        string englishHeader, italianHeader, englishSent;
        MimeKit.MimeMessage englishMime, italianMime;
        try
        {
            Lingua.Force("en", "en-US");
            english = cases.Select(c => UI.ComposeWindow.SubjectFor(c.Mode, c.Subject)).ToArray();
            englishHeader = LletSpaces(MimeOptions.QuoteHeader(original));
            englishSent = LletSpaces(MimeOptions.QuoteDate(LletDate));
            englishMime = Reply().GetAwaiter().GetResult();
        }
        finally
        {
            Lingua.Force("it");
        }
        italian = cases.Select(c => UI.ComposeWindow.SubjectFor(c.Mode, c.Subject)).ToArray();
        italianHeader = MimeOptions.QuoteHeader(original);
        italianMime = Reply().GetAwaiter().GetResult();
        var wrong = cases.Select((c, i) => (c, i)).Where(x => english[x.i] != x.c.English || italian[x.i] != x.c.Italian)
            .Select(x => $"{x.c.Mode} \"{x.c.Subject}\": \"{italian[x.i]}\" e \"{english[x.i]}\"").ToList();
        Check(wrong.Count == 0,
              "lingua-lettura, l'oggetto di risposte e inoltri: \"R: \" e \"I: \" in italiano, \"RE: \" e \"FW: \" in inglese; un prefisso già presente delle due lingue (R:, RE:, I:, FW:, " +
              "anche in minuscolo) non si ripete in nessuna delle due, e un inoltro di una risposta (o il contrario) prende il suo" + (wrong.Count == 0 ? "" : ": " + string.Join("; ", wrong)));

        const string wantedEnglish = "On Wednesday, October 21, 2026 2:30 PM, Mario Rossi <mario.rossi@esempio-prova.example> wrote:";
        const string wantedItalian = "Il mercoledì 21 ottobre 2026 14:30, Mario Rossi <mario.rossi@esempio-prova.example> ha scritto:";
        bool mime = englishMime.Subject == "RE: Offerta" && LletSpaces(englishMime.HtmlBody ?? "").Contains(WebUtility.HtmlEncode(wantedEnglish)) && LletSpaces(englishMime.TextBody ?? "").Contains(wantedEnglish)
                    && italianMime.Subject == "R: Offerta" && (italianMime.HtmlBody ?? "").Contains(WebUtility.HtmlEncode(wantedItalian)) && (italianMime.TextBody ?? "").Contains(wantedItalian);
        Check(englishHeader == wantedEnglish && italianHeader == wantedItalian && englishSent == "Wednesday, October 21, 2026 2:30 PM" && mime,
              $"lingua-lettura, la riga sopra la citazione parte nella lingua di Missiva: \"{englishHeader}\" (data lunga e ora breve en-US), in italiano \"{italianHeader}\" come prima; " +
              $"nel messaggio costruito (MIME di una risposta IMAP) oggetto, HTML e testo la portano ({mime})");
    }

    static void LletQuestions()
    {
        string[] english;
        string[] Texts()
        {
            var oneClick = Unsubscribe.Confirmation(new UnsubscribeAction(UnsubscribeKind.OneClick, "https://liste.esempio-prova.example/via?u=1"), "Notizie di prova", "ada@studio-prova.example");
            var page = Safety.WebPageQuestion("Accesso.html");
            var failure = UI.ComposeWindow.SendFailure(new MailException("Il server ha rifiutato il messaggio."), T("Dalla Posta in uscita: "));
            var group = new AddressBook.Entry { Name = "Soci", Group = new Contact { IsGroup = true, Members = ["Anna <anna@esempio-prova.example>", "Bruno <bruno@esempio-prova.example>"] } };
            return
            [
                oneClick.Heading, oneClick.Body, oneClick.Primary, page.Heading, page.Primary,
                Safety.LinkWarning("www.banca-prova.example", new Uri("https://truffa-prova.example/accesso")) ?? "",
                failure.Body, failure.Primary, group.Detail, UI.ReadingView.WebErrorText(new IOException("Disk full."), false).Text,
                UI.ReadingView.ImagesOnce(null, new MessageSummary { Id = "1", FolderId = "INBOX" }, null, null, eml: true) ?? "",
            ];
        }
        try
        {
            Lingua.Force("en", "en-US");
            english = Texts();
        }
        finally
        {
            Lingua.Force("it");
        }
        string[] wanted =
        [
            "Unsubscribe?",
            "Missiva asks the site liste.esempio-prova.example to stop sending messages from Notizie di prova to ada@studio-prova.example. The request goes to the address the sender provided:\nhttps://liste.esempio-prova.example/via?u=1\n\nNo page opens, and the messages you already have stay where they are.",
            "Unsubscribe", "Open a web page that came by email?", "Open page",
            "The link text shows banca-prova.example, but the link goes to truffa-prova.example. It's the most common trick in phishing messages.",
            "From the Outbox: Il server ha rifiutato il messaggio.\n\nThe message is still here, with its recipients, text, and attachments. You can try again, save it to Drafts, or go back to writing it.",
            "Try again", "Group, 2 people",
            "The reading pane couldn't start: Disk full. This can be caused by Missiva's data folder (locked, or the disk is full). ",
            "This message was opened from an attachment, and no one has verified who wrote it",
        ];
        var wrong = english.Where((t, i) => t != wanted[i]).ToList();
        Check(wrong.Count == 0,
              "lingua-lettura, le domande e gli avvisi in inglese sono frasi intere con i valori al loro posto: Annulla iscrizione con un clic, la pagina web allegata, il link ingannevole, " +
              "l'errore d'invio dalla Posta in uscita con il suo rimedio, il gruppo nei suggerimenti, WebView2 che non parte, il perché di \"Mostra immagini\" una volta sola" +
              (wrong.Count == 0 ? "" : ": diversi " + string.Join(" | ", wrong)));
    }
}
#endif
