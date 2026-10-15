#if DEBUG
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MimeKit;

namespace Missiva;

/// <summary>
/// Solo in Debug: le caselle finte delle foto della pagina GitHub (docs/immagini, README.md). Con
/// Missiva.exe --imap-finto porta foto studio|personale|pec il server IMAP finto ha, al posto dei messaggi di prova,
/// una casella credibile: quella di lavoro di Ada Galli (uno studio), la sua personale (con una newsletter, un preventivo,
/// una frode sulle coordinate bancarie) o la sua posta certificata (una fattura elettronica firmata, le ricevute). Tutto
/// inventato: persone finte, domini .example (che non esistono), immagini disegnate qui, nessun marchio. Le date sono
/// relative a oggi, così l'elenco ha Oggi, Ieri e la settimana scorsa. Niente di questo è nella versione che si installa.
/// </summary>
static partial class ProvaBozze
{
    /// <summary>Chi usa le caselle delle foto.</summary>
    const string FotoName = "Ada Galli";

    /// <summary>Gli indirizzi delle tre caselle delle foto, per nome (anche quelli che il launcher delle foto mette negli account).</summary>
    internal static readonly Dictionary<string, string> FotoMailboxes = new()
    {
        ["studio"] = "ada.galli@studio-esempio.example",
        ["personale"] = "ada.galli@posta-esempio.example",
        ["pec"] = "ada.galli@pec-esempio.example",
    };

    /// <summary>
    /// Riempie il server finto con la casella delle foto che ha quel nome (studio, personale, pec). Per la posta
    /// certificata scrive anche la radice di prova accanto ai dati del server (radice-foto.cer), da dare alla finestra con
    /// --pec-radice: solo con lei la firma del gestore finto è verde.
    /// </summary>
    static void FillFoto(FakeImap fake, string mailbox)
    {
        if (!FotoMailboxes.TryGetValue(mailbox, out var me)) throw new ArgumentException($"casella delle foto sconosciuta: {mailbox} (studio, personale o pec)");
        fake.TrueTypes = true; // le immagini della newsletter e i tipi veri degli allegati (anteprime delle foto e dei PDF)
        foreach (var folder in fake.Folders.Keys.Where(k => k is not ("INBOX" or "Drafts" or "Sent" or "Trash" or "Junk" or "Archive")).ToList()) fake.Folders.Remove(folder);
        // Qualche cartella dell'utente, vuota, come in una casella vera.
        foreach (var folder in mailbox switch { "studio" => new[] { "Progetti", "Amministrazione" }, "personale" => new[] { "Casa", "Viaggi" }, _ => Array.Empty<string>() }) fake.Folders.TryAdd(folder, new());
        var mail = FotoMail(mailbox, me);
        foreach (var (folder, message, flags) in mail)
        {
            fake.Folders.TryAdd(folder, new());
            fake.Add(folder, message, flags);
        }
        if (mailbox == "pec" && fotoRoot != null) File.WriteAllBytes(Path.Combine(Store.Dir, "radice-foto.cer"), fotoRoot);
        Log.Write($"server IMAP finto: casella delle foto \"{mailbox}\" ({me}), {mail.Count} messaggi");
    }

    static List<(string Folder, MimeMessage Message, string Flags)> FotoMail(string mailbox, string me) => mailbox switch
    {
        "studio" =>
        [
            ("INBOX", FotoMessage("Maria Rossi", "maria.rossi@studio-esempio.example", me, "Riunione di lunedì alle 10", FotoWhen(0, 18.6),
                "<p>Ciao Ada,</p><p>ti confermo la riunione di lunedì alle 10 in sala grande per il progetto della biblioteca. Porto io le tavole aggiornate e il computo.</p><p>Se riesci, guarda prima le osservazioni del Comune: ne parliamo lì.</p><p>Maria</p>"), "\\Flagged"),
            ("INBOX", FotoMessage("Giulia Neri", "giulia.neri@comune-esempio.example", me, "Progetto della biblioteca: osservazioni", FotoWhen(1, 15.7),
                "<p>Gentile architetta Galli,</p><p>le inviamo le osservazioni della commissione sul progetto preliminare della nuova biblioteca di quartiere. In sintesi: più posti a sedere nella sala lettura e un accesso separato per la sala ragazzi.</p><p>Cordiali saluti,<br>Giulia Neri<br>Ufficio Lavori pubblici</p>",
                ("Osservazioni della commissione.pdf", FotoPdf("Comune di Esempio, Ufficio Lavori pubblici", "", "Osservazioni sul progetto preliminare", "della biblioteca di quartiere", "", "1. Aumentare i posti a sedere della sala lettura.", "2. Prevedere un accesso separato per la sala ragazzi.", "3. Verificare l'illuminazione naturale del lato nord."), "application/pdf")), "\\Seen Clienti"),
            ("INBOX", FotoMessage("Mario Rossi", "mario.rossi@commercialisti-esempio.example", me, "Fattura di settembre", FotoWhen(8, 17.6),
                "<p>Buongiorno Ada,</p><p>in allegato la fattura di settembre per la consulenza dello studio. Il pagamento è come sempre a 30 giorni.</p><p>Un saluto,<br>Mario Rossi</p>",
                ("Fattura settembre.pdf", FotoPdf("Studio Rossi Commercialisti", "", "Fattura n. 118 del 30/09/2026", "", "Consulenza fiscale e contabile, settembre 2026", "Imponibile: 600,00 euro", "IVA 22%: 132,00 euro", "Totale: 732,00 euro"), "application/pdf")), "\\Seen Amministrazione"),
            ("INBOX", FotoMessage("Paolo Conti", "paolo.conti@impresa-esempio.example", me, "Sopralluogo in cantiere giovedì", FotoWhen(8, 9.3),
                "<p>Ciao Ada,</p><p>giovedì alle 14 ci vediamo in cantiere per il sopralluogo: è arrivata la struttura del soppalco e vorrei il tuo parere prima di chiudere i solai.</p><p>Paolo</p>"), "\\Seen"),
            ("INBOX", FotoMessage("Elena Marino", "elena.marino@studio-esempio.example", me, "Verbale della riunione di martedì", FotoWhen(8, 16.2),
                "<p>Ciao a tutti,</p><p>ecco il verbale della riunione di martedì, con le scadenze dei prossimi due mesi. Ditemi entro venerdì se c'è da correggere qualcosa.</p><p>Elena</p>"), "\\Seen"),
            ("INBOX", FotoMessage("Ordine degli Architetti", "segreteria@ordine-esempio.example", me, "Corso di aggiornamento di novembre", FotoWhen(9, 11.0),
                "<p>Gentili iscritti,</p><p>sono aperte le iscrizioni al corso di aggiornamento sull'efficienza energetica degli edifici, in quattro incontri a novembre. Il corso vale 12 crediti formativi.</p><p>La segreteria</p>"), "\\Seen"),
            ("Sent", FotoMessage(FotoName, me, "maria.rossi@studio-esempio.example", "Re: Tavole del progetto", FotoWhen(2, 10.5),
                "<p>Ciao Maria, le tavole vanno benissimo. Aggiungo solo le quote della sala ragazzi.</p><p>Ada</p>", toName: "Maria Rossi"), "\\Seen"),
        ],
        "personale" =>
        [
            ("INBOX", FotoNewsletter(me), ""),
            ("INBOX", FotoMessage("Luca Bianchi", "luca.bianchi@cucine-bianchi.example", me, "Preventivo per la cucina", FotoWhen(0, 17.4),
                "<p>Ciao Ada,</p><p>come promesso ti mando il preventivo per la cucina, con le misure prese durante il sopralluogo. Dentro trovi anche le due alternative per il piano di lavoro.</p><p>Se va bene, possiamo fissare il montaggio per la prima settimana di novembre.</p><p>Un saluto,<br>Luca Bianchi<br>Cucine Bianchi</p>",
                ("Preventivo cucina.pdf", FotoPdf("Cucine Bianchi S.r.l.", "", "Preventivo n. 42 per Ada Galli", "", "Cucina componibile su misura, 3,60 m: 3.900,00 euro", "Piano di lavoro in laminato: 450,00 euro", "Trasporto e montaggio: 500,00 euro", "", "Totale: 4.850,00 euro, IVA compresa"), "application/pdf")), "Casa"),
            ("INBOX", FotoFraud(me), ""),
            ("INBOX", FotoMessage("Chiara Fontana", "chiara.fontana@posta-esempio.example", me, "Foto della gita al lago", FotoWhen(7, 18.5),
                "<p>Ciao Ada!</p><p>Eccoti le foto della gita: la seconda è quella del tramonto che volevi. Alla prossima porto io il pranzo.</p><p>Un abbraccio,<br>Chiara</p>",
                ("Lago al mattino.jpg", FotoLake(sunset: false), "image/jpeg"), ("Tramonto sul lago.jpg", FotoLake(sunset: true), "image/jpeg")), "\\Seen \\Flagged"),
            ("INBOX", FotoMessage("Biblioteca civica", "prestiti@biblioteca-esempio.example", me, "Il libro che hai prenotato è arrivato", FotoWhen(8, 10.4),
                "<p>Gentile lettrice,</p><p>il libro che hai prenotato è disponibile al banco prestiti fino a sabato 24 ottobre.</p><p>Biblioteca civica</p>"), "\\Seen"),
            ("INBOX", FotoMessage("Palestra Esempio", "info@palestra-esempio.example", me, "Gli orari dei corsi di novembre", FotoWhen(10, 18.0),
                "<p>Ciao,</p><p>da novembre il corso di pilates del martedì passa alle 19. Gli altri orari restano uguali.</p><p>A presto in palestra!</p>"), "\\Seen"),
            // A Luca Bianchi Ada ha già scritto: il messaggio della frode, con il suo nome da un altro indirizzo, accende l'avviso del sosia.
            ("Sent", FotoMessage(FotoName, me, "luca.bianchi@cucine-bianchi.example", "Re: Sopralluogo per la cucina", FotoWhen(6, 9.0),
                "<p>Ciao Luca, per me va bene giovedì alle 18. Ti aspetto a casa.</p><p>Ada</p>", toName: "Luca Bianchi"), "\\Seen"),
        ],
        _ => FotoPecMail(me),
    };

    /// <summary>
    /// Un giorno e un'ora relativi a oggi (0 è oggi, 1 ieri). Un'ora di oggi non ancora arrivata (le foto fatte al mattino)
    /// diventa qualche minuto fa, tenendo l'ordine: più tardi nella giornata, più vicino ad adesso.
    /// </summary>
    static DateTimeOffset FotoWhen(int daysAgo, double hour)
    {
        var at = DateTime.Today.AddDays(-daysAgo).AddHours(hour);
        if (at > DateTime.Now.AddMinutes(-5)) at = DateTime.Now.AddMinutes(-5 - (24 - hour) * 4);
        return new DateTimeOffset(at);
    }

    /// <summary>Un'email in HTML (una pagina intera: l'anteprima dell'elenco la legge solo dentro il body) con gli allegati dati.</summary>
    static MimeMessage FotoMessage(string fromName, string fromEmail, string to, string subject, DateTimeOffset date, string html,
                                   params (string Name, byte[] Data, string Type)[] files) => FotoMessage(fromName, fromEmail, to, subject, date, html, FotoName, files);

    static MimeMessage FotoMessage(string fromName, string fromEmail, string to, string subject, DateTimeOffset date, string html, string toName,
                                   params (string Name, byte[] Data, string Type)[] files)
    {
        // Il Message-ID con il dominio del mittente: quello di serie di MimeKit porta il nome del PC.
        var message = new MimeMessage { Subject = subject, Date = date, MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(fromEmail[(fromEmail.IndexOf('@') + 1)..]) };
        message.From.Add(new MailboxAddress(fromName, fromEmail));
        message.To.Add(new MailboxAddress(toName, to));
        var builder = new BodyBuilder { HtmlBody = FotoPage(html) };
        foreach (var (name, data, type) in files) builder.Attachments.Add(name, data, ContentType.Parse(type));
        message.Body = builder.ToMessageBody();
        return message;
    }

    static string FotoPage(string body) =>
        $"<!DOCTYPE html><html lang=\"it\"><head><meta charset=\"utf-8\"></head><body style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:15px;line-height:1.5;color:#242424;\">{body}</body></html>";

    // ---------------------------------------------------------------- la newsletter

    /// <summary>
    /// La newsletter di una libreria inventata: la testata e le tre copertine sono immagini dentro il messaggio (cid:),
    /// disegnate qui; List-Unsubscribe per la riga "Annulla iscrizione"; DMARC superato per il suo dominio.
    /// </summary>
    static MimeMessage FotoNewsletter(string me)
    {
        const string domain = "libreria-verdi.example";
        var message = new MimeMessage { Subject = "Libreria Verdi: le novità di ottobre", Date = FotoWhen(0, 19.0), MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(domain) };
        message.From.Add(new MailboxAddress("Libreria Verdi", "novita@" + domain));
        message.To.Add(new MailboxAddress(FotoName, me));
        message.Headers.Add("Authentication-Results", $"mx.posta-esempio.example; dkim=pass header.i=@{domain}; spf=pass smtp.mailfrom={domain}; dmarc=pass header.from={domain}");
        message.Headers.Add("List-Unsubscribe", $"<https://www.{domain}/newsletter/annulla?id=2718>, <mailto:annulla@{domain}?subject=annulla>");
        message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        var books = new[]
        {
            ("Il faro di Levante", "Marta Ferri", "18,50 €", "Un romanzo di mare e di famiglia, su un'isola dove tutti si conoscono."),
            ("Cucinare con l'autunno", "Giorgio Lanza", "24,00 €", "Sessanta ricette di stagione, dalle zucche alle castagne."),
            ("La mappa delle stelle", "Sara Moretti", "16,90 €", "Il cielo di notte spiegato ai ragazzi dai nove anni."),
        };
        var cells = new StringBuilder();
        for (int i = 0; i < books.Length; i++)
        {
            var (title, author, price, blurb) = books[i];
            cells.Append($"""
                <td width="33%" valign="top" style="padding:10px;">
                  <img src="cid:libro{i + 1}@{domain}" width="160" alt="{title}" style="display:block;width:100%;height:auto;border:0;border-radius:4px;">
                  <p style="margin:12px 0 2px 0;font-family:Georgia,serif;font-size:17px;font-weight:bold;line-height:1.25;color:#1f3d31;">{title}</p>
                  <p style="margin:0 0 6px 0;font-size:13px;color:#6f6a60;">{author}</p>
                  <p style="margin:0 0 6px 0;font-size:13px;line-height:1.4;color:#3a3a3a;">{blurb}</p>
                  <p style="margin:0;font-size:14px;font-weight:600;color:#2e6e52;">{price}</p>
                </td>
                """);
        }
        var html = $"""
            <!DOCTYPE html><html lang="it"><head><meta charset="utf-8"><title>Le novità di ottobre</title></head>
            <body style="margin:0;padding:0;background:#f3efe6;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f3efe6;"><tr><td align="center" style="padding:20px 10px;">
            <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;border-radius:8px;font-family:'Segoe UI',Arial,sans-serif;color:#2b2b2b;">
              <tr><td><img src="cid:testata@{domain}" width="600" alt="" style="display:block;width:100%;height:auto;border:0;border-radius:8px 8px 0 0;"></td></tr>
              <tr><td style="padding:26px 34px 6px 34px;font-family:Georgia,serif;font-size:17px;line-height:1.6;">
                <p style="margin:0 0 12px 0;">Cara Ada,</p>
                <p style="margin:0;">ottobre porta le prime sere lunghe e una pila di libri nuovi sul nostro bancone. Ne abbiamo scelti tre da consigliarti, e sabato 24 ti aspettiamo per l'incontro con l'autrice del romanzo del mese.</p>
              </td></tr>
              <tr><td style="padding:10px 24px 0 24px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>{cells}</tr></table></td></tr>
              <tr><td align="center" style="padding:18px 34px 24px 34px;">
                <a href="https://www.{domain}/novita" style="display:inline-block;background:#2e6e52;color:#ffffff;text-decoration:none;font-size:16px;font-weight:600;padding:12px 30px;border-radius:24px;">Scopri tutte le novità</a>
              </td></tr>
              <tr><td style="padding:0 34px 26px 34px;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f1e7;border-radius:6px;"><tr><td style="padding:16px 20px;font-size:15px;line-height:1.5;">
                  <b>Sabato 24 ottobre, alle 17</b><br>Marta Ferri presenta <i>Il faro di Levante</i>. Ingresso libero, con un tè caldo per tutti.
                </td></tr></table>
              </td></tr>
              <tr><td style="padding:16px 34px 24px 34px;border-top:1px solid #e8e2d6;font-size:12px;line-height:1.5;color:#8a8478;">
                Libreria Verdi, via dei Librai 21. Aperta dal martedì alla domenica, dalle 9 alle 19.<br>
                Ricevi questa email perché sei iscritta alla nostra newsletter. <a href="https://www.{domain}/newsletter/annulla?id=2718" style="color:#2e6e52;">Annulla l'iscrizione</a>
              </td></tr>
            </table>
            </td></tr></table>
            </body></html>
            """;
        var builder = new BodyBuilder { HtmlBody = html };
        void Inline(string id, byte[] png)
        {
            var part = builder.LinkedResources.Add(id + ".png", png, new ContentType("image", "png"));
            part.ContentId = $"{id}@{domain}";
        }
        Inline("testata", FotoHeader());
        Inline("libro1", FotoCover(0));
        Inline("libro2", FotoCover(1));
        Inline("libro3", FotoCover(2));
        message.Body = builder.ToMessageBody();
        return message;
    }

    // ---------------------------------------------------------------- la frode

    /// <summary>
    /// La truffa delle coordinate bancarie: il nome di Luca Bianchi (a cui Ada ha scritto) da un altro indirizzo, le
    /// risposte verso un terzo dominio, DMARC fallito secondo il server della casella, un logo da Internet (bloccato) e un
    /// link che mostra un sito e porta altrove. Tutto su domini .example, che non esistono.
    /// </summary>
    static MimeMessage FotoFraud(string me)
    {
        var message = new MimeMessage { Subject = "Nuove coordinate per il pagamento", Date = FotoWhen(0, 16.2), MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId("cucine-bianchi-pagamenti.example") };
        message.From.Add(new MailboxAddress("Luca Bianchi", "amministrazione@cucine-bianchi-pagamenti.example"));
        message.ReplyTo.Add(new MailboxAddress("Ufficio pagamenti", "pagamenti@conto-veloce.example"));
        message.To.Add(new MailboxAddress(FotoName, me));
        message.Headers.Add("Authentication-Results", "mx.posta-esempio.example; dkim=none; spf=fail smtp.mailfrom=conto-veloce.example; dmarc=fail header.from=cucine-bianchi-pagamenti.example");
        message.Body = new BodyBuilder
        {
            HtmlBody = FotoPage("""
                <p><img src="https://cdn.cucine-bianchi-pagamenti.example/img/logo.png" alt="Cucine Bianchi" width="220" height="70"></p>
                <p>Gentile cliente,</p>
                <p>per un cambio di banca, da oggi i pagamenti vanno fatti sul nuovo conto qui sotto: quello indicato nel preventivo non è più attivo.</p>
                <p><b>IBAN: IT00 X000 0000 0000 0000 0000 000</b><br>Intestato a: Cucine Bianchi</p>
                <p>Per non perdere la data del montaggio la preghiamo di versare l'acconto di <b>1.450 euro</b> entro oggi. La fattura aggiornata è qui:
                <a href="https://conto-veloce.example/fattura?id=8812">www.cucine-bianchi.example/fatture</a></p>
                <p>Cordiali saluti,<br>Luca Bianchi<br>Cucine Bianchi S.r.l.</p>
                """),
        }.ToMessageBody();
        return message;
    }

    // ---------------------------------------------------------------- la posta certificata

    /// <summary>
    /// La casella PEC: una fattura elettronica arrivata per posta certificata (la busta firmata dal gestore finto, dentro
    /// il messaggio vero con la fattura .p7m firmata da Luca Bianchi), le ricevute di accettazione e di consegna di un
    /// messaggio mandato da Ada, e un'altra posta certificata della settimana scorsa. La radice di prova resta in fotoRoot.
    /// </summary>
    static List<(string Folder, MimeMessage Message, string Flags)> FotoPecMail(string me)
    {
        var now = DateTimeOffset.Now;
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Radice di prova delle foto, O=Missiva, C=IT", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-30), now.AddYears(1));
        fotoRoot = root.Export(X509ContentType.Cert);
        var gestore = PecKeys.Leaf("CN=Gestore PEC Esempio S.p.A., O=Gestore PEC Esempio, C=IT", root, now, "posta-certificata@pec-esempio.example");
        var luca = PecKeys.Leaf("CN=Luca Bianchi, O=Cucine Bianchi S.r.l., C=IT", root, now, "luca.bianchi@cucine-bianchi.example");

        // La fattura elettronica (un XML come quelli del Sistema di Interscambio, semplificato e senza dati veri), firmata.
        const string invoice = """
            <?xml version="1.0" encoding="UTF-8"?>
            <FatturaElettronica versione="FPR12"><FatturaElettronicaHeader><CedentePrestatore><DatiAnagrafici><IdFiscaleIVA><IdPaese>IT</IdPaese><IdCodice>00000000001</IdCodice></IdFiscaleIVA><Anagrafica><Denominazione>Cucine Bianchi S.r.l.</Denominazione></Anagrafica></DatiAnagrafici></CedentePrestatore><CessionarioCommittente><DatiAnagrafici><Anagrafica><Nome>Ada</Nome><Cognome>Galli</Cognome></Anagrafica></DatiAnagrafici></CessionarioCommittente></FatturaElettronicaHeader><FatturaElettronicaBody><DatiGenerali><DatiGeneraliDocumento><TipoDocumento>TD01</TipoDocumento><Divisa>EUR</Divisa><Numero>42</Numero><ImportoTotaleDocumento>1450.00</ImportoTotaleDocumento><Causale>Acconto per la cucina su misura</Causale></DatiGeneraliDocumento></DatiGenerali><DatiBeniServizi><DettaglioLinee><NumeroLinea>1</NumeroLinea><Descrizione>Acconto del 30% sul preventivo n. 42</Descrizione><Quantita>1.00</Quantita><PrezzoTotale>1318.18</PrezzoTotale><AliquotaIVA>10.00</AliquotaIVA></DettaglioLinee></DatiBeniServizi></FatturaElettronicaBody></FatturaElettronica>
            """;
        var cms = new System.Security.Cryptography.Pkcs.SignedCms(new System.Security.Cryptography.Pkcs.ContentInfo(Encoding.UTF8.GetBytes(invoice)), detached: false);
        var signer = new System.Security.Cryptography.Pkcs.CmsSigner(luca) { IncludeOption = X509IncludeOption.EndCertOnly };
        signer.SignedAttributes.Add(new System.Security.Cryptography.Pkcs.Pkcs9SigningTime(FotoWhen(0, 9.0).UtcDateTime));
        cms.ComputeSignature(signer);
        var p7m = cms.Encode();

        var fattura = FotoMessage("Cucine Bianchi S.r.l.", "cucine.bianchi@pec-esempio.example", me, "Fattura n. 42, acconto per la cucina", FotoWhen(0, 10.3),
            "<p>Gentile cliente,</p><p>le inviamo la fattura elettronica n. 42 per l'acconto sulla cucina su misura. Il file firmato è quello trasmesso al Sistema di Interscambio.</p><p>Cordiali saluti,<br>Amministrazione, Cucine Bianchi S.r.l.</p>",
            ("IT00000000001_00042.xml.p7m", p7m, "application/pkcs7-mime"));
        var condominio = FotoMessage("Studio Amministrazioni Esempio", "amministrazioni.esempio@pec-esempio.example", me, "Convocazione dell'assemblea di condominio", FotoWhen(7, 11.5),
            "<p>Gentili condomini,</p><p>l'assemblea ordinaria è convocata per giovedì 29 ottobre alle 18, nella sala comune. All'ordine del giorno il bilancio e il rifacimento della facciata.</p><p>L'amministratore</p>");
        var domanda = FotoMessage(FotoName, me, "protocollo@pec-comune-esempio.example", "Richiesta di permesso di costruire", FotoWhen(1, 12.0),
            "<p>Si trasmette la richiesta di permesso di costruire con i documenti allegati.</p><p>Arch. Ada Galli</p>", "Comune di Esempio");

        return
        [
            ("INBOX", FotoEnvelope(gestore, "posta-certificata", fattura, me, FotoWhen(0, 10.3)), ""),
            ("INBOX", FotoEnvelope(gestore, "avvenuta-consegna", domanda, me, FotoWhen(1, 12.02)), "\\Seen"),
            ("INBOX", FotoEnvelope(gestore, "accettazione", domanda, me, FotoWhen(1, 12.01)), "\\Seen"),
            ("INBOX", FotoEnvelope(gestore, "posta-certificata", condominio, me, FotoWhen(7, 11.5)), "\\Seen"),
            ("Sent", domanda, "\\Seen"),
        ];
    }

    /// <summary>La radice di prova dell'ultima casella PEC delle foto (FotoPecMail), che FillFoto scrive in radice-foto.cer.</summary>
    static byte[]? fotoRoot;

    /// <summary>
    /// Una busta PEC come la fa un gestore, firmata S/MIME dal certificato del gestore finto: per la posta certificata il
    /// testo del gestore, postacert.eml (il messaggio vero) e daticert.xml; per le ricevute il testo e daticert.xml.
    /// </summary>
    static MimeMessage FotoEnvelope(X509Certificate2 gestore, string kind, MimeMessage original, string to, DateTimeOffset when)
    {
        var sender = ((MailboxAddress)original.From[0]).Address;
        var recipient = ((MailboxAddress)original.To[0]).Address;
        bool carries = kind == "posta-certificata";
        original.MessageId ??= MimeKit.Utils.MimeUtils.GenerateMessageId("pec-esempio.example");
        var day = when.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var time = when.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var zone = (when.Offset < TimeSpan.Zero ? "-" : "+") + when.Offset.ToString("hhmm");
        var daticert = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <postacert tipo="{kind}" errore="nessuno">
              <intestazione><mittente>{sender}</mittente><destinatari tipo="certificato">{recipient}</destinatari><risposte>{sender}</risposte><oggetto>{original.Subject}</oggetto></intestazione>
              <dati><gestore-emittente>Gestore PEC Esempio S.p.A.</gestore-emittente><data zona="{zone}"><giorno>{day}</giorno><ora>{time}</ora></data>
              <identificativo>opec-foto.{when:yyyyMMddHHmmss}@pec-esempio.example</identificativo><msgid>&lt;{original.MessageId}&gt;</msgid>
              {(kind == "avvenuta-consegna" ? $"<consegna>{recipient}</consegna>" : "")}</dati>
            </postacert>
            """;
        var text = kind switch
        {
            "posta-certificata" => $"Messaggio di posta certificata\r\nIl giorno {day} alle ore {time} ({zone}) il messaggio \"{original.Subject}\" è stato inviato da \"{sender}\" indirizzato a: {recipient}\r\nIl messaggio originale è incluso in allegato.",
            "accettazione" => $"Ricevuta di accettazione\r\nIl giorno {day} alle ore {time} ({zone}) il messaggio \"{original.Subject}\" proveniente da \"{sender}\" ed indirizzato a: {recipient} è stato accettato dal sistema ed inoltrato.",
            _ => $"Ricevuta di avvenuta consegna\r\nIl giorno {day} alle ore {time} ({zone}) il messaggio \"{original.Subject}\" proveniente da \"{sender}\" ed indirizzato a \"{recipient}\" è stato depositato nella casella di destinazione.",
        };
        var inside = new Multipart("mixed") { new TextPart("plain") { Text = text } };
        if (carries) inside.Add(new MessagePart { Message = original, ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "postacert.eml" } });
        inside.Add(new MimePart("application", "xml")
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(daticert))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "daticert.xml" },
            ContentTransferEncoding = ContentEncoding.Base64,
        });
        using var context = new MimeKit.Cryptography.TemporarySecureMimeContext();
        var prefix = kind switch { "posta-certificata" => "POSTA CERTIFICATA: ", "accettazione" => "ACCETTAZIONE: ", _ => "CONSEGNA: " };
        var envelope = new MimeMessage { Subject = prefix + original.Subject, Date = when, MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId("pec-esempio.example"), Body = MimeKit.Cryptography.MultipartSigned.Create(context, new MimeKit.Cryptography.CmsSigner(gestore), inside) };
        envelope.From.Add(new MailboxAddress(carries ? "Per conto di: " + sender : "posta-certificata@pec-esempio.example", "posta-certificata@pec-esempio.example"));
        if (carries) envelope.ReplyTo.Add((MailboxAddress)original.From[0]);
        envelope.To.Add(new MailboxAddress("", to));
        if (carries) envelope.Headers.Add("X-Trasporto", "posta-certificata");
        else
        {
            envelope.Headers.Add("X-Ricevuta", kind);
            envelope.Headers.Add("X-Riferimento-Message-ID", $"<{original.MessageId}>");
        }
        return envelope;
    }

    // ---------------------------------------------------------------- allegati e immagini disegnati qui

    /// <summary>Un PDF di una pagina con le righe date (Helvetica, WinAnsi: niente simbolo dell'euro), con la tabella xref giusta.</summary>
    static byte[] FotoPdf(params string[] lines)
    {
        var text = new StringBuilder("BT /F1 13 Tf 64 770 Td 20 TL\n");
        foreach (var line in lines) text.Append('(').Append(line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") '\n");
        text.Append("ET");
        var stream = text.ToString();
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Length {Encoding.Latin1.GetByteCount(stream)} >>\nstream\n{stream}\nendstream",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = Encoding.Latin1.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }

    /// <summary>Un disegno di WPF in PNG o JPEG, della misura data in pixel (sul thread STA di Main, come il server finto).</summary>
    static byte[] FotoDrawn(int width, int height, Action<DrawingContext> draw, bool jpeg = false)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) draw(dc);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 85 } : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    static SolidColorBrush FotoBrush(uint rgb, byte alpha = 255) => new(Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    static FormattedText FotoText(string text, string font, double size, Brush brush, bool bold = false, bool italic = false, double maxWidth = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("it-IT"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily(font), italic ? FontStyles.Italic : FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, brush, 1.0);
        if (maxWidth > 0) formatted.MaxTextWidth = maxWidth;
        return formatted;
    }

    static StreamGeometry FotoShape(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], true, true);
            g.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>La testata della newsletter: verde scuro, foglie d'autunno, una mensola di libri e il titolo.</summary>
    static byte[] FotoHeader() => FotoDrawn(1200, 420, dc =>
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x1C, 0x46, 0x37), Color.FromRgb(0x2F, 0x6E, 0x52), 20), null, new Rect(0, 0, 1200, 420));
        var random = new Random(24);
        uint[] autumn = [0xE0892B, 0xC8552D, 0xE9B44C, 0xB33F2A];
        for (int i = 0; i < 26; i++)
        {
            var at = new Point(random.Next(560, 1180), random.Next(10, 230));
            dc.PushTransform(new RotateTransform(random.Next(0, 180), at.X, at.Y));
            dc.DrawEllipse(FotoBrush(autumn[i % autumn.Length], (byte)random.Next(120, 230)), null, at, random.Next(10, 22), random.Next(5, 9));
            dc.Pop();
        }
        // La mensola, con i dorsi dei libri.
        (uint Color, int Height, int Width)[] spines =
            [(0xF2E8D5, 170, 34), (0xC8552D, 200, 42), (0x2B3A55, 185, 30), (0xE9B44C, 150, 38), (0x8A9A5B, 210, 36), (0xF2E8D5, 175, 28),
             (0x6B3E5E, 195, 44), (0x3E7CB1, 160, 32), (0xD98E63, 205, 38)];
        double x = 742;
        foreach (var (color, h, w) in spines)
        {
            dc.DrawRoundedRectangle(FotoBrush(color), null, new Rect(x, 352 - h, w, h), 3, 3);
            dc.DrawRectangle(FotoBrush(0x000000, 40), null, new Rect(x, 352 - h + 22, w, 6));
            dc.DrawRectangle(FotoBrush(0x000000, 40), null, new Rect(x, 352 - 32, w, 6));
            x += w + 5;
        }
        dc.DrawRoundedRectangle(FotoBrush(0x8B5E3C), null, new Rect(720, 352, 420, 16), 3, 3);
        dc.DrawText(FotoText("LIBRERIA VERDI", "Segoe UI Semibold", 24, FotoBrush(0xFFFFFF, 200)), new Point(80, 96));
        dc.DrawText(FotoText("Le novità", "Georgia", 80, Brushes.White), new Point(74, 132));
        dc.DrawText(FotoText("di ottobre", "Georgia", 80, FotoBrush(0xF4D9A6), italic: true), new Point(74, 226));
    });

    /// <summary>Le copertine dei tre libri inventati della newsletter.</summary>
    static byte[] FotoCover(int which) => FotoDrawn(360, 540, dc =>
    {
        var rect = new Rect(0, 0, 360, 540);
        switch (which)
        {
            case 0: // Il faro di Levante: il mare di notte e un faro
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x14, 0x2A, 0x4A), Color.FromRgb(0x2D, 0x5D, 0x8C), 90), null, rect);
                dc.DrawGeometry(FotoBrush(0xFFF4D6, 70), null, FotoShape(new(200, 250), new(360, 170), new(360, 300)));
                dc.DrawGeometry(FotoBrush(0xF5F1E8), null, FotoShape(new(170, 400), new(186, 250), new(214, 250), new(230, 400)));
                dc.DrawRectangle(FotoBrush(0xC8552D), null, new Rect(178, 300, 44, 26));
                dc.DrawRectangle(FotoBrush(0xC8552D), null, new Rect(174, 350, 52, 26));
                dc.DrawRectangle(FotoBrush(0xFFE7A0), null, new Rect(184, 228, 32, 24));
                dc.DrawGeometry(FotoBrush(0x2B3A55), null, FotoShape(new(178, 230), new(200, 206), new(222, 230)));
                for (int i = 0; i < 4; i++)
                    dc.DrawRectangle(FotoBrush(0x0E1E35, (byte)(170 + i * 20)), null, new Rect(0, 400 + i * 35, 360, 35));
                dc.DrawText(FotoText("Il faro di Levante", "Georgia", 40, Brushes.White, maxWidth: 300), new Point(30, 34));
                dc.DrawText(FotoText("Marta Ferri", "Segoe UI", 20, FotoBrush(0xFFFFFF, 210)), new Point(30, 490));
                break;
            case 1: // Cucinare con l'autunno: una zucca su fondo arancio
                dc.DrawRectangle(FotoBrush(0xF0A04B), null, rect);
                dc.DrawEllipse(FotoBrush(0xD96B1E), null, new Point(130, 330), 70, 82);
                dc.DrawEllipse(FotoBrush(0xE57F2A), null, new Point(230, 330), 70, 82);
                dc.DrawEllipse(FotoBrush(0xEE8E36), null, new Point(180, 330), 72, 90);
                dc.DrawRoundedRectangle(FotoBrush(0x5E7A2E), null, new Rect(170, 228, 18, 34), 6, 6);
                dc.PushTransform(new RotateTransform(-30, 210, 240));
                dc.DrawEllipse(FotoBrush(0x7E9B3A), null, new Point(215, 240), 28, 12);
                dc.Pop();
                dc.DrawText(FotoText("Cucinare con l'autunno", "Georgia", 38, FotoBrush(0x4A2511), bold: true, maxWidth: 300), new Point(30, 34));
                dc.DrawText(FotoText("Giorgio Lanza", "Segoe UI", 20, FotoBrush(0x4A2511, 220)), new Point(30, 490));
                break;
            default: // La mappa delle stelle: un cielo viola con la luna
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2A, 0x1B, 0x4D), Color.FromRgb(0x4B, 0x2E, 0x7A), 90), null, rect);
                var random = new Random(7);
                for (int i = 0; i < 70; i++)
                    dc.DrawEllipse(FotoBrush(0xFFF6D5, (byte)random.Next(120, 255)), null, new Point(random.Next(8, 352), random.Next(150, 470)), random.Next(1, 4) * 0.8, random.Next(1, 4) * 0.8);
                dc.DrawEllipse(FotoBrush(0xF7D774), null, new Point(250, 260), 52, 52);
                dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(0x33, 0x22, 0x5E), Color.FromRgb(0x3C, 0x27, 0x6A), 90), null, new Point(276, 244), 50, 50);
                var pen = new Pen(FotoBrush(0xF7D774, 160), 2);
                dc.DrawLine(pen, new Point(60, 330), new Point(110, 300));
                dc.DrawLine(pen, new Point(110, 300), new Point(150, 350));
                dc.DrawLine(pen, new Point(150, 350), new Point(95, 400));
                dc.DrawText(FotoText("La mappa delle stelle", "Georgia", 40, FotoBrush(0xF7D774), maxWidth: 300), new Point(30, 34));
                dc.DrawText(FotoText("Sara Moretti", "Segoe UI", 20, FotoBrush(0xFFFFFF, 210)), new Point(30, 490));
                break;
        }
    });

    /// <summary>Le foto della gita (JPEG): montagne sul lago, al mattino o al tramonto.</summary>
    static byte[] FotoLake(bool sunset) => FotoDrawn(1280, 960, dc =>
    {
        var (skyTop, skyBottom) = sunset ? (Color.FromRgb(0x3B, 0x2C, 0x63), Color.FromRgb(0xF2, 0x9E, 0x6B)) : (Color.FromRgb(0x5D, 0x9B, 0xD5), Color.FromRgb(0xDC, 0xEB, 0xF5));
        dc.DrawRectangle(new LinearGradientBrush(skyTop, skyBottom, 90), null, new Rect(0, 0, 1280, 560));
        if (sunset) dc.DrawEllipse(FotoBrush(0xFFD27F), null, new Point(820, 470), 70, 70);
        uint far = sunset ? 0x6B4E7Au : 0x8FA9C4u, near = sunset ? 0x3E2F55u : 0x55708Fu;
        dc.DrawGeometry(FotoBrush(far), null, FotoShape(new(0, 560), new(0, 360), new(180, 250), new(330, 380), new(520, 220), new(700, 360), new(880, 270), new(1080, 380), new(1280, 300), new(1280, 560)));
        dc.DrawGeometry(FotoBrush(near), null, FotoShape(new(0, 560), new(0, 450), new(240, 380), new(420, 470), new(640, 400), new(860, 480), new(1060, 410), new(1280, 470), new(1280, 560)));
        var (waterTop, waterBottom) = sunset ? (Color.FromRgb(0xC9, 0x7E, 0x6E), Color.FromRgb(0x2C, 0x22, 0x45)) : (Color.FromRgb(0x7F, 0xB1, 0xD8), Color.FromRgb(0x2F, 0x5E, 0x84));
        dc.DrawRectangle(new LinearGradientBrush(waterTop, waterBottom, 90), null, new Rect(0, 560, 1280, 400));
        var light = new Pen(FotoBrush(0xFFFFFF, 70), 3);
        var random = new Random(sunset ? 5 : 3);
        for (int i = 0; i < 40; i++)
        {
            double x = random.Next(0, 1200), y = random.Next(580, 940);
            dc.DrawLine(light, new Point(x, y), new Point(x + random.Next(30, 90), y));
        }
        // Gli alberi sulla riva, a sinistra.
        for (int i = 0; i < 9; i++)
        {
            double x = 20 + i * 38, h = 120 + (i % 3) * 30;
            dc.DrawGeometry(FotoBrush(sunset ? 0x1F1830u : 0x2E4A2Eu), null, FotoShape(new(x, 580), new(x + 24, 580 - h), new(x + 48, 580)));
        }
    }, jpeg: true);

    // ---------------------------------------------------------------- i controlli (gruppo "foto")

    /// <summary>Gli indirizzi, i siti e i domini dei risultati di autenticazione e dei Message-ID: quelli che una foto può mostrare.</summary>
    static readonly System.Text.RegularExpressions.Regex FotoDomain = new(
        @"@([A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+)|://([A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+)|(?:header\.from|smtp\.mailfrom)=([A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+)|^\s*([A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+);");

    /// <summary>I domini di un messaggio delle foto: intestazioni, testi, daticert.xml e i messaggi dentro (postacert.eml).</summary>
    static IEnumerable<string> FotoDomains(MimeMessage message)
    {
        var texts = message.Headers.Select(h => h.Value).ToList();
        foreach (var part in message.BodyParts)
        {
            if (part is MessagePart { Message: { } inner }) foreach (var d in FotoDomains(inner)) yield return d;
            else if (part is TextPart text) texts.Add(text.Text);
            else if (part is MimePart { FileName: { } name, Content: { } content } && name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                using var ms = new MemoryStream();
                content.DecodeTo(ms);
                texts.Add(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        foreach (var text in texts)
            foreach (System.Text.RegularExpressions.Match m in FotoDomain.Matches(text))
                yield return m.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1).First(g => g.Success).Value;
    }

    /// <summary>
    /// Le caselle, i contatti e gli eventi delle foto della pagina GitHub: solo indirizzi .example (anche dentro la posta
    /// certificata e nei Message-ID, dove MimeKit di serie mette il nome del PC), nessun promemoria; le immagini della
    /// newsletter arrivano nel corpo letto da ImapProvider come immagini nel testo solo dal server con TrueTypes (quello di
    /// sempre resta com'era); la posta certificata è verde solo con la radice di prova, come la fattura firmata.
    /// </summary>
    static async Task PhotoDataTestsAsync()
    {
        Report.Add("--- Foto della pagina GitHub: dati finti, immagini nel testo dal server IMAP finto, posta certificata");
        // Tutto quello che si disegna (WPF) prima del primo await: sul thread STA di Main.
        var mail = FotoMailboxes.ToDictionary(b => b.Key, b => FotoMail(b.Key, b.Value));
        var root = X509CertificateLoader.LoadCertificate(fotoRoot!);
        using var withTypes = new FakeImap(new Flavor(Gmail: false)) { TrueTypes = true };
        using var plain = new FakeImap(new Flavor(Gmail: false));
        var newsletter = mail["personale"].First(m => m.Message.Subject?.StartsWith("Libreria Verdi") == true).Message;
        withTypes.Add("INBOX", newsletter);
        plain.Add("INBOX", newsletter);

        var domains = mail.Values.SelectMany(list => list).SelectMany(m => FotoDomains(m.Message)).ToList();
        var wrong = domains.Where(d => !d.EndsWith(".example", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        Check(domains.Count > 50 && wrong.Count == 0 && mail.Values.All(list => list.Count >= 5),
              $"foto: {domains.Count} indirizzi, siti e domini nelle tre caselle ({string.Join(", ", mail.Select(b => $"{b.Key} {b.Value.Count}"))} messaggi), tutti .example{(wrong.Count > 0 ? "; fuori: " + string.Join(", ", wrong) : "")}");

        var account = new MailAccount { Kind = AccountKind.Microsoft, Email = FotoMailboxes["studio"] };
        var contacts = ContactSamples.Photo(account).Concat(ContactSamples.PhotoLocal()).ToList();
        var calendar = CalendarSamples.Photo(account, DateTime.Today.AddDays(-7), DateTime.Today.AddDays(7));
        var people = contacts.SelectMany(c => c.Emails).Concat(calendar.Events.SelectMany(e => e.Attendees.Select(a => a.Person.Email).Append(e.Organizer?.Email ?? "x@vuoto.example"))).ToList();
        Check(contacts.Count >= 8 && calendar.Events.Count >= 20 && people.All(e => e.EndsWith(".example", StringComparison.OrdinalIgnoreCase))
              && contacts.All(c => c.Website.Length == 0 || c.Website.EndsWith(".example", StringComparison.OrdinalIgnoreCase)) && !calendar.Events.Any(e => e.ReminderOn),
              $"foto: {contacts.Count} contatti e {calendar.Events.Count} eventi di due settimane, persone e siti .example, nessun promemoria (nessuna notifica dalle foto)");

        Pec.TestRoots = [root];
        try
        {
            var pec = mail["pec"].First(m => m.Message.Subject?.StartsWith("POSTA CERTIFICATA: Fattura") == true).Message;
            using var raw = new MemoryStream();
            pec.WriteTo(raw);
            var opened = Pec.Open(raw.ToArray(), EmlProvider.BodyOf(MimeMessage.Load(new MemoryStream(raw.ToArray()))));
            var (band, tone) = Pec.Band(opened.Pec!);
            var p7m = opened.Attachments.Single(a => a.Name.EndsWith(".p7m", StringComparison.Ordinal));
            var invoice = P7m.Open(p7m.Content!, p7m.Name);
            Check(tone == "ok" && band.Contains("firma del gestore verificata (Gestore PEC Esempio)") && invoice.Signature == SignatureState.Verified && invoice.Signer.Contains("Luca Bianchi"),
                  $"foto: la fattura per posta certificata con la radice di prova, fascia verde (\"{band}\") e .p7m firmato da {invoice.Signer}, verificato");
        }
        finally
        {
            Pec.TestRoots = null;
        }
        Check(Pec.GestoreOf("pec-esempio.example") == null, "foto: senza la radice di prova il gestore delle foto non è un gestore noto");

        ImapProvider.TestPlain = true;
        try
        {
            // L'elenco di IMAP non chiede l'anteprima: la chiede dopo, per le righe che si vedono (GetPreviewsAsync).
            async Task<(string Preview, MessageBody Body)> Read(FakeImap fake)
            {
                using var imap = new ImapProvider(new MailAccount
                {
                    Kind = AccountKind.Imap, Email = FotoMailboxes["personale"],
                    Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = fake.Port, SmtpHost = "127.0.0.1", SmtpPort = 1, UserName = "foto", ProtectedPassword = Store.Protect("foto") },
                });
                var inbox = new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox };
                var row = (await imap.GetMessagesAsync(inbox, null, null)).Messages.Single();
                var previews = await imap.GetPreviewsAsync(inbox, [row]);
                return (previews.GetValueOrDefault(row.Id, ""), await imap.GetBodyAsync(row));
            }
            var (preview, body) = await Read(withTypes);
            var inline = body.Attachments.Where(a => a.IsInline && a.Content is { Length: > 0 }).ToList();
            Check(inline.Count == 4 && inline.All(a => a.ContentType == "image/png" && body.Html.Contains("cid:" + a.ContentId)) && !body.Attachments.Any(a => !a.IsInline)
                  && preview.StartsWith("Cara Ada", StringComparison.Ordinal),
                  $"foto: dal server con TrueTypes la newsletter ha {inline.Count} immagini nel testo (cid:) con il contenuto e nessun allegato, e l'anteprima \"{preview[..Math.Min(30, preview.Length)]}...\" (una pagina intera: l'anteprima di MailKit legge solo dentro il body)");
            var (_, before) = await Read(plain);
            Check(before.Attachments.Count == 0 && before.Html.Contains("cid:testata@"),
                  "foto: dal server di sempre (senza TrueTypes) la stessa newsletter ha la struttura di prima: nessuna immagine nel testo, come le prove si aspettano");
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
    }
}
#endif
