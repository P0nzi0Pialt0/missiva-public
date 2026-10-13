using MailKit;
using MailKit.Net.Smtp;
using MimeKit;

namespace Missiva;

/// <summary>
/// Le opzioni dei messaggi che partono via SMTP (Gmail e IMAP), come in Outlook e Thunderbird: importanza (X-Priority e
/// Importance), conferma di lettura (Disposition-Notification-To) e conferma di recapito (DSN di SMTP); e l'importanza dei
/// messaggi ricevuti, dalle stesse intestazioni lette con l'elenco.
/// </summary>
public static class MimeOptions
{
    /// <summary>Le intestazioni dell'importanza, chieste con l'elenco (BODY.PEEK[HEADER.FIELDS (...)]).</summary>
    public static readonly string[] ImportanceHeaders = ["Importance", "X-Priority", "Priority"];

    /// <summary>La richiesta dell'elenco: le voci date e le intestazioni dell'importanza.</summary>
    public static FetchRequest ListRequest(MessageSummaryItems items) => new(items, ImportanceHeaders);

    /// <summary>Importanza alta: Importance: high, X-Priority 1 o 2, Priority: urgent.</summary>
    public static bool IsImportant(HeaderList? headers) =>
        headers != null && (headers["Importance"]?.Trim().Equals("high", StringComparison.OrdinalIgnoreCase) == true ||
                            headers["X-Priority"]?.TrimStart() is { Length: > 0 } x && x[0] is '1' or '2' ||
                            headers["Priority"]?.Trim().Equals("urgent", StringComparison.OrdinalIgnoreCase) == true);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, string> quotes = new();
    /// <summary>Quante citazioni sono state calcolate davvero (per le prove).</summary>
    internal static int QuoteRuns;

    /// <summary>
    /// Il testo semplice della citazione di una risposta ("> " a ogni riga). Finché la finestra di scrittura è aperta
    /// l'HTML dell'originale è lo stesso oggetto: si calcola una volta, non a ogni salvataggio della bozza (audit, v2 V-28).
    /// </summary>
    public static string QuoteText(string html) => quotes.GetValue(html, static h =>
    {
        Interlocked.Increment(ref QuoteRuns);
        return string.Join("\n", MailHtml.ToText(h).Split('\n').Select(l => "> " + l));
    });

    /// <summary>
    /// Il mittente: l'indirizzo dell'account o l'identità scelta nel Da (alias, "Invia come"), con il suo Rispondi a. Via SMTP
    /// l'identità è anche il mittente della busta (MAIL FROM), come in Thunderbird: il server deve accettarla.
    /// </summary>
    public static void SetFrom(MimeMessage message, OutgoingMessage mail, MailAccount account)
    {
        if (mail.From is not { } identity)
        {
            message.From.Add(new MailboxAddress(account.DisplayName, account.Email));
            return;
        }
        message.From.Add(new MailboxAddress(identity.Name.Trim().Length > 0 ? identity.Name.Trim() : account.DisplayName, identity.Email));
        if (identity.ReplyTo.Trim().Length > 0 && MailboxAddress.TryParse(identity.ReplyTo.Trim(), out var replyTo)) message.ReplyTo.Add(replyTo);
    }

    public static void Apply(MimeMessage message, OutgoingMessage mail, MailAccount account)
    {
        if (mail.Importance != Importance.Normal)
        {
            message.Importance = mail.Importance == Importance.High ? MessageImportance.High : MessageImportance.Low;
            message.XPriority = mail.Importance == Importance.High ? XMessagePriority.Highest : XMessagePriority.Lowest;
        }
        if (mail.ReadReceipt) message.Headers[HeaderId.DispositionNotificationTo] = new MailboxAddress(account.DisplayName, account.Email).ToString();
    }

    /// <summary>
    /// Il messaggio da inviare o da salvare come bozza, per Gmail e per gli account IMAP: destinatari, oggetto, testo e
    /// HTML con le sue immagini; per le risposte la citazione e i riferimenti della conversazione; gli allegati nuovi e
    /// quelli che restano della bozza che continua. Era scritto due volte, uguale riga per riga nei due provider tranne la
    /// connessione di una richiesta (audit dell'11 ottobre, C-10): qui una volta sola, e ogni provider dà le tre cose che
    /// fa a modo suo.
    /// </summary>
    /// <param name="bodyOf">Il corpo dell'originale, se la finestra di scrittura non l'ha già (GetBodyAsync del provider).</param>
    /// <param name="headersOf">Message-Id e References dell'originale, con una richiesta leggera sulla connessione che il provider sceglie.</param>
    /// <param name="attachmentOf">Il contenuto di un allegato dell'originale o della bozza di prima, sulla connessione delle bozze.</param>
    internal static async Task<MimeMessage> BuildAsync(OutgoingMessage mail, MailAccount account, ReplySources replySources, Func<MessageSummary, Task<MessageBody>> bodyOf,
                                                       Func<MessageSummary, Task<IMessageSummary?>> headersOf, Func<MessageSummary, Attachment, Task<byte[]>> attachmentOf)
    {
        var message = new MimeMessage();
        SetFrom(message, mail, account); // l'account o l'identità scelta nel Da
        foreach (var a in mail.To) message.To.Add(new MailboxAddress(a.Name, a.Email));
        foreach (var a in mail.Cc) message.Cc.Add(new MailboxAddress(a.Name, a.Email));
        foreach (var a in mail.Bcc) message.Bcc.Add(new MailboxAddress(a.Name, a.Email));
        message.Subject = mail.Subject;
        Apply(message, mail, account); // importanza e conferma di lettura

        var html = mail.Html;
        var text = mail.Text;
        var builder = new BodyBuilder();
        if (mail.Original is { } original && mail.Mode != ComposeMode.New)
        {
            // Il testo dell'originale è già in memoria (riquadro di lettura o corpi letti): non si scarica più il
            // messaggio intero, allegati compresi, che con 20 MB di allegati faceva aspettare secondi prima dell'invio.
            var quoted = mail.OriginalBody ?? await bodyOf(original);
            if (mail.Mode != ComposeMode.Forward)
            {
                // Per la conversazione bastano Message-Id e References: una richiesta leggera, e una volta sola (non cambiano):
                // prima a ogni salvataggio della bozza (audit, v2 V-28).
                if (replySources.Get(original) is not { } source) source = replySources.Set(original, await headersOf(original));
                if (source.MessageId.Length > 0)
                {
                    message.InReplyTo = source.MessageId;
                    foreach (var r in source.References) message.References.Add(r);
                    message.References.Add(source.MessageId);
                }
            }
            var header = $"Il {original.Date:dddd d MMMM yyyy HH:mm}, {original.From} ha scritto:";
            html += $"<br><div>{System.Net.WebUtility.HtmlEncode(header)}</div><blockquote style=\"margin:0 0 0 .8ex;border-left:2px solid #ccc;padding-left:1ex\">" +
                    quoted.Html + "</blockquote>";
            text += "\n\n" + header + "\n" + QuoteText(quoted.Html);

            // Le immagini incorporate della citazione (cid:): senza, arrivano rotte.
            foreach (var a in quoted.Attachments.Where(a => a.IsInline && a.Content != null && a.ContentId != null))
            {
                var part = builder.LinkedResources.Add(a.Name.Length > 0 ? a.Name : "immagine", a.Content!, TypeOf(a));
                part.Headers.Replace(HeaderId.ContentId, $"<{a.ContentId}>");
            }
            // Come in Outlook, Inoltra porta con sé gli allegati dell'originale (scaricati uno per uno, non tutto il messaggio).
            // Dopo il primo salvataggio della bozza sono fra quelli della bozza (KeptAttachments), e l'utente può toglierli.
            if (mail.Mode == ComposeMode.Forward && mail.Draft == null)
                foreach (var a in quoted.Attachments.Where(a => !a.IsInline))
                    builder.Attachments.Add(a.Name.Length > 0 ? a.Name : "allegato", await attachmentOf(original, a), TypeOf(a));
        }

        builder.HtmlBody = html;
        builder.TextBody = text;
        // Le immagini del testo scritto (incollate o trascinate nel corpo), incorporate come nel nuovo Outlook: con le
        // parti collegate BodyBuilder fa multipart/related, e l'HTML le mostra da cid:.
        foreach (var image in mail.Images)
        {
            var part = builder.LinkedResources.Add(image.Name, image.Content,
                ContentType.TryParse(image.ContentType, out var type) ? type : new ContentType("image", "png"));
            part.Headers.Replace(HeaderId.ContentId, $"<{image.ContentId}>");
        }
        // Gli allegati della bozza che restano, scaricati parte per parte (la bozza di prima si elimina dopo il salvataggio)
        // sulla connessione delle bozze, non su quella principale; quelli tenuti dal salvataggio di prima non si riscaricano.
        if (mail.Draft is { } draft)
            foreach (var a in mail.KeptAttachments)
                builder.Attachments.Add(a.Name.Length > 0 ? a.Name : "allegato", await attachmentOf(draft.Message, a), TypeOf(a));
        foreach (var path in mail.AttachmentPaths) await builder.Attachments.AddAsync(path);
        // Risposta a un invito (iMIP): testo, HTML e la parte text/calendar, invece del corpo normale.
        message.Body = mail.Calendar != null ? Invitation.MimeBody(mail) : builder.ToMessageBody();
        return message;
    }

    static ContentType TypeOf(Attachment a) =>
        ContentType.TryParse(a.ContentType, out var type) ? type : new ContentType("application", "octet-stream");

    /// <summary>
    /// Il client SMTP che chiede la conferma di recapito (DSN, RFC 3461: NOTIFY=SUCCESS,FAILURE) quando serve: il server
    /// del destinatario risponde con un rapporto di consegna. Senza, come sempre.
    /// </summary>
    internal sealed class DsnSmtpClient(bool deliveryReceipt) : SmtpClient
    {
        protected override DeliveryStatusNotification? GetDeliveryStatusNotifications(MimeMessage message, MailboxAddress mailbox) =>
            deliveryReceipt ? DeliveryStatusNotification.Success | DeliveryStatusNotification.Failure : null;
    }

    /// <summary>Quanto del messaggio MailKit ha già consegnato alla connessione SMTP (dopo DATA).</summary>
    sealed class Delivered : MailKit.ITransferProgress
    {
        public bool Whole { get; private set; }
        public void Report(long bytesTransferred, long totalSize) => Whole |= bytesTransferred >= totalSize;
        public void Report(long bytesTransferred) { }
    }

    /// <summary>
    /// L'invio via SMTP. Se la connessione cade, o il tempo scade, dopo che tutto il messaggio è stato consegnato al server e
    /// prima della sua risposta, non si sa com'è andata: il server può averlo accettato. Quell'errore esce come
    /// MailException.MaybeSent, che nessuno ripete da solo. Prima era un errore di rete come gli altri: l'invio pianificato
    /// ripartiva ogni minuto e la Posta in uscita al ritorno della rete, e il messaggio poteva arrivare due volte, o una
    /// volta al minuto con un server lento a rispondere (audit dell'11 ottobre, C-61). Un rifiuto del server dopo DATA
    /// (una risposta c'è: SmtpCommandException) resta un rifiuto.
    /// </summary>
    internal static async Task SendAsync(SmtpClient smtp, MimeMessage message, string host)
    {
        var delivered = new Delivered();
        try
        {
            await smtp.SendAsync(message, progress: delivered);
        }
        catch (Exception ex) when (delivered.Whole && ex is not (SmtpCommandException or MailException))
        {
            Log.Write($"SMTP di {host}: connessione persa dopo la consegna del messaggio e prima della risposta ({ex.GetType().Name}): forse è partito, non si riprova da soli");
            throw new MailException($"La connessione con {host} si è interrotta mentre il server riceveva il messaggio: non si sa se è partito.", ex) { MaybeSent = true };
        }
    }
}

/// <summary>
/// Message-Id e References dei messaggi a cui si sta rispondendo (Gmail e IMAP), per provider. Non cambiano mai, e ogni
/// salvataggio della bozza li richiedeva al server, con una SELECT in più se nel frattempo era aperta un'altra cartella
/// (audit, v2 V-28). La chiave ha anche l'identificativo stabile: un UID riusato da un altro messaggio non la trova.
/// Un originale non trovato non si ricorda: si richiede al salvataggio dopo.
/// </summary>
sealed class ReplySources
{
    readonly Dictionary<string, (string MessageId, List<string> References)> known = new();

    static string Key(MessageSummary original) => original.FolderId + "|" + original.Id + "|" + original.StableId;

    public (string MessageId, List<string> References)? Get(MessageSummary original)
    {
        lock (known) return known.TryGetValue(Key(original), out var found) ? found : null;
    }

    public (string MessageId, List<string> References) Set(MessageSummary original, IMessageSummary? headers)
    {
        if (headers?.Envelope?.MessageId is not { Length: > 0 } id) return ("", []);
        var source = (id, (headers.References ?? new MessageIdList()).ToList());
        lock (known)
        {
            if (known.Count >= 32) known.Clear(); // poche finestre di scrittura insieme: non cresce
            known[Key(original)] = source;
        }
        return source;
    }
}
