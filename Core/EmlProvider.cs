using System.Net;
using MimeKit;

namespace Missiva;

/// <summary>
/// Un messaggio .eml aperto in Missiva (un allegato .eml, e in Debug Missiva.exe --eml file): si legge dal file, già
/// analizzato con MimeKit come i messaggi di Gmail, e si mostra nel riquadro confinato. Rispondere, inoltrare e
/// rispondere a un invito passano dall'account del messaggio che lo conteneva (sender); le azioni sulla casella no, perché
/// il messaggio non ci sta.
/// </summary>
public sealed class EmlProvider(MimeMessage message, IMailProvider sender) : IMailProvider
{
    public MailAccount Account => sender.Account;
    public bool SupportsFocused => false;

    public MessageSummary Summary { get; } = SummaryOf(message);
    readonly MessageBody body = BodyOf(message);

    static MessageSummary SummaryOf(MimeMessage m)
    {
        var from = m.From.Mailboxes.FirstOrDefault();
        var sender = new Address { Name = from?.Name ?? "", Email = from?.Address ?? "" };
        return new MessageSummary
        {
            Id = "eml",
            FolderId = "eml",
            From = sender,
            Subject = m.Subject ?? "",
            Date = m.Date == DateTimeOffset.MinValue ? DateTime.Now : m.Date.LocalDateTime,
            StableId = m.MessageId ?? "",
            IsRead = true,
            To = m.To.Mailboxes.Take(10).Select(a => new Address { Name = a.Name ?? "", Email = a.Address }).ToList(),
            ReplyTo = Address.ReplyToOf(m.ReplyTo.Mailboxes.Select(a => new Address { Name = a.Name ?? "", Email = a.Address }), sender),
        };
    }

    /// <summary>Testo, destinatari, allegati (con il contenuto: il file è già tutto qui), intestazioni di verifica e invito.</summary>
    public static MessageBody BodyOf(MimeMessage m)
    {
        var html = m.HtmlBody ?? "<pre>" + WebUtility.HtmlEncode(m.TextBody ?? "") + "</pre>";
        var attachments = new List<Attachment>();
        foreach (var entity in m.BodyParts)
        {
            var cid = entity.ContentId?.Trim('<', '>');
            bool inline = cid != null && !entity.IsAttachment && html.Contains("cid:" + cid, StringComparison.OrdinalIgnoreCase);
            if (!entity.IsAttachment && !inline && entity is not MessagePart) continue;
            using var ms = new MemoryStream();
            string name;
            switch (entity)
            {
                case MimePart { Content: { } content } part:
                    content.DecodeTo(ms);
                    name = part.FileName ?? "allegato";
                    break;
                case MessagePart { Message: { } inner }:
                    inner.WriteTo(ms);
                    name = Safety.SafeFileName((inner.Subject ?? "messaggio") + ".eml");
                    break;
                default:
                    continue;
            }
            attachments.Add(new Attachment
            {
                Id = attachments.Count.ToString(),
                Name = name,
                ContentType = entity.ContentType.MimeType,
                Size = ms.Length,
                IsInline = inline,
                ContentId = cid,
                Content = ms.ToArray(),
            });
        }
        static List<Address> People(InternetAddressList list) => list.Mailboxes.Select(a => new Address { Name = a.Name ?? "", Email = a.Address }).ToList();
        return new MessageBody
        {
            Html = html,
            To = People(m.To),
            Cc = People(m.Cc),
            Attachments = attachments,
            AuthenticationResults = SenderCheck.FromHeaders(m.Headers),
            Invite = Invitation.FromMime(m),
        };
    }

    static MailException NotHere() => new("È un messaggio aperto da un file .eml: non sta nella casella, quindi non si sposta né si segna.");

    // ---------------------------------------------------------------- letture: dal file

    public Task<MessageBody> GetBodyAsync(MessageSummary m, CancellationToken cancel = default) => Task.FromResult(body);
    public Task<List<MessageSummary>> GetConversationAsync(MessageSummary m, CancellationToken cancel = default) => Task.FromResult(new List<MessageSummary> { Summary });
    /// <summary>Gli allegati di un .eml sono già in memoria: arrivano subito, tutti in un passo dell'avanzamento.</summary>
    public Task<byte[]> GetAttachmentAsync(MessageSummary m, Attachment attachment, IProgress<long>? progress = null, CancellationToken cancel = default)
    {
        if (attachment.Content == null) return Task.FromException<byte[]>(new MailException("Allegato non trovato."));
        progress?.Report(attachment.Content.Length);
        return Task.FromResult(attachment.Content);
    }
    public Task<byte[]> GetRawAsync(MessageSummary m, int limit = int.MaxValue, CancellationToken cancel = default, IProgress<long>? progress = null)
    {
        using var ms = new MemoryStream();
        message.WriteTo(ms);
        var raw = ms.ToArray();
        raw = raw.Length > limit ? raw[..limit] : raw;
        progress?.Report(raw.Length);
        return Task.FromResult(raw);
    }

    // ---------------------------------------------------------------- risposte: dall'account del messaggio che lo conteneva

    public Task SendAsync(OutgoingMessage mail) => sender.SendAsync(Unlink(mail));
    public Task<SavedDraft> SaveDraftAsync(OutgoingMessage mail) => sender.SaveDraftAsync(Unlink(mail));

    /// <summary>
    /// Una risposta o un inoltro del file non può appoggiarsi al messaggio sul server (che non c'è): parte come messaggio
    /// nuovo, con la citazione nel testo e i riferimenti della conversazione del file.
    /// </summary>
    OutgoingMessage Unlink(OutgoingMessage mail)
    {
        if (mail.Original == null || mail.Mode == ComposeMode.New) return mail;
        var header = $"Il {Summary.Date:dddd d MMMM yyyy HH:mm}, {Summary.From} ha scritto:";
        return new OutgoingMessage
        {
            Mode = ComposeMode.New,
            To = mail.To, Cc = mail.Cc, Bcc = mail.Bcc, Subject = mail.Subject,
            Html = mail.Html + $"<br><div>{WebUtility.HtmlEncode(header)}</div><blockquote style=\"margin:0 0 0 .8ex;border-left:2px solid #ccc;padding-left:1ex\">{MimeOptions.QuoteHtml(body.Html)}</blockquote>",
            Text = mail.Text + "\n\n" + header + "\n" + MimeOptions.QuoteText(body.Html),
            AttachmentPaths = mail.AttachmentPaths, Images = mail.Images, Draft = mail.Draft, KeptAttachments = mail.KeptAttachments,
            Calendar = mail.Calendar, CalendarMethod = mail.CalendarMethod,
            Importance = mail.Importance, ReadReceipt = mail.ReadReceipt, DeliveryReceipt = mail.DeliveryReceipt, SendAt = mail.SendAt,
        };
    }
    public Task DiscardDraftAsync(SavedDraft draft) => sender.DiscardDraftAsync(draft);
    public Task RespondAsync(MessageSummary m, Invitation invite, InviteResponse response) => sender.RespondAsync(m, invite, response);
    /// <summary>Pianifica invio come l'account che parte: con Outlook lo tiene Exchange, con Gmail e IMAP Missiva.</summary>
    public bool SchedulesOnServer => sender.SchedulesOnServer;
    public Task<bool> SendDraftAsync(string draftKey) => sender.SendDraftAsync(draftKey);

    // ---------------------------------------------------------------- la casella: non c'entra

    public Task<List<MailFolder>> GetFoldersAsync() => Task.FromResult(new List<MailFolder>());
    public Task DeleteFolderAsync(MailFolder folder) => Task.FromException(NotHere());
    public Task<MessagePage> GetMessagesAsync(MailFolder folder, bool? focused, string? next, ListOptions? options = null) => Task.FromResult(new MessagePage());
    public bool SortsOnServer(SortBy sort) => false;
    public Task<MessagePage> SearchAsync(MailFolder? folder, string query, string? next = null, CancellationToken cancel = default) => Task.FromResult(new MessagePage());
    public Task SetReadAsync(IReadOnlyList<MessageSummary> messages, bool read) => Task.CompletedTask; // aprirlo non segna nulla
    public Task SetFlagAsync(IReadOnlyList<MessageSummary> messages, bool flagged) => Task.FromException(NotHere());
    public Task<List<MovedMessage>> MoveAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination) => Task.FromException<List<MovedMessage>>(NotHere());
    public Task<List<MovedMessage>> UndoMoveAsync(IReadOnlyList<MovedMessage> moved, MailFolder destination) => Task.FromException<List<MovedMessage>>(NotHere());
    public Task<List<Category>> GetCategoriesAsync() => Task.FromResult(new List<Category>());
    public Task<Category> CreateCategoryAsync(string name, int preset) => Task.FromException<Category>(NotHere());
    public Task SetCategoryAsync(IReadOnlyList<MessageSummary> messages, string category, bool on) => Task.FromException(NotHere());
    public Task<MailFolder> CreateFolderAsync(string name, MailFolder? parent) => Task.FromException<MailFolder>(NotHere());
    public Task<string> RenameFolderAsync(MailFolder folder, string name) => Task.FromException<string>(NotHere());
    public Task<string> MoveFolderAsync(MailFolder folder, MailFolder? parent) => Task.FromException<string>(NotHere());
    public Task EmptyFolderAsync(MailFolder folder, MailFolder? trash) => Task.FromException(NotHere());
    public Task MarkAllReadAsync(MailFolder folder) => Task.FromException(NotHere());
    public Task<MailFolder> EnsureFolderAsync(string name) => Task.FromException<MailFolder>(NotHere());
    public Task<string> SnoozeAsync(MessageSummary m, MailFolder snoozeFolder) => Task.FromException<string>(NotHere());
    public Task<string?> WakeAsync(string key, MailFolder snoozeFolder, MailFolder inbox) => Task.FromResult<string?>(null);
    public Task CopyAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination) => Task.FromException(NotHere());
    public Task DeletePermanentlyAsync(IReadOnlyList<MessageSummary> messages) => Task.FromException(NotHere());
    public Task<List<MessageSummary>> GetNewAsync(DateTime sinceUtc) => Task.FromResult(new List<MessageSummary>());
    public Task<int> GetInboxUnreadAsync() => Task.FromResult(0);
    public Task<List<SentRecipients>> GetSentRecipientsAsync(int max) => Task.FromResult(new List<SentRecipients>());
}
