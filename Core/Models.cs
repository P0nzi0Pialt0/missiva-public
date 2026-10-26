namespace Missiva;

public enum AccountKind { Microsoft, Gmail, Imap }

public enum FolderKind { Inbox, Drafts, Sent, Deleted, Archive, Junk, Other }

/// <summary>
/// Un account configurato. Per Gmail il token di Google e per gli account IMAP la password sono cifrati con la protezione
/// dati di Windows.
/// </summary>
public sealed class MailAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Vero se l'identificativo può fare da nome di cartella e da chiave: lettere e cifre ASCII, trattino e trattino basso,
    /// da 1 a 64 caratteri, e non un nome di periferica di Windows (CON, NUL, COM1...). Quelli che Missiva genera sono 32
    /// cifre esadecimali. L'identificativo finisce nei percorsi della copia locale (Store.CacheName) e nelle chiavi
    /// "account|cartella": niente punti, barre, due punti né "|". Con "..\..\Documenti" scritto in una copia di sicurezza
    /// costruita apposta, rimuovere l'account cancellava quella cartella (audit dell'11 ottobre, T-14): chi legge
    /// identificativi da un file (Backup.Read, Store.Read) li controlla qui.
    /// </summary>
    public static bool ValidId([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? id) =>
        id is { Length: > 0 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') && !DeviceName(id);

    static bool DeviceName(string id) =>
        id.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL"
        || id.Length == 4 && char.IsAsciiDigit(id[3]) && (id.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || id.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Perché questi account, letti da un file, non si possono usare, o null se vanno bene: una voce vuota, un
    /// identificativo non valido (ValidId) o lo stesso identificativo due volte (anche solo con maiuscole diverse: sul disco
    /// sarebbero la stessa cartella).
    /// </summary>
    internal static string? Problem(IReadOnlyList<MailAccount?>? accounts)
    {
        if (accounts == null || accounts.Any(a => a == null)) return T("l'elenco degli account non è valido");
        if (accounts.Any(a => !ValidId(a!.Id))) return T("un account ha un identificativo che Missiva non usa");
        if (accounts.Select(a => a!.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Count) return T("due account hanno lo stesso identificativo");
        return null;
    }

    public AccountKind Kind { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Microsoft: identificativo dell'account in MSAL.</summary>
    public string? HomeAccountId { get; set; }
    /// <summary>
    /// Microsoft: la directory (tenant) degli account di lavoro o della scuola, "organizations" per qualsiasi
    /// organizzazione, oppure l'ID o il dominio della directory. Null per gli account personali, che accedono come sempre.
    /// </summary>
    public string? Tenant { get; set; }
    /// <summary>Microsoft: l'ID dell'app registrata dall'organizzazione, solo per questo account; null è l'app di Missiva.</summary>
    public string? ClientId { get; set; }
    /// <summary>Gmail: token di aggiornamento di Google, cifrato (DPAPI, solo per questo utente di Windows).</summary>
    public string? ProtectedRefreshToken { get; set; }
    /// <summary>
    /// Gmail: i permessi che Google ha dato a Missiva con l'ultimo accesso (l'elenco degli ambiti, separati da spazi), per sapere
    /// se c'è quello del calendario (GoogleAuth.HasCalendar). Null per gli account entrati prima: solo la posta.
    /// </summary>
    public string? GoogleScopes { get; set; }
    /// <summary>IMAP: server, porte, sicurezza e credenziali (la password cifrata).</summary>
    public ImapServer? Imap { get; set; }
    /// <summary>Gli altri indirizzi da cui scrive questo account (alias, "Invia come"), nel campo Da (Gestisci account).</summary>
    public List<SendIdentity> Identities { get; set; } = new();
    /// <summary>
    /// Microsoft 365: una casella condivisa (info@, segreteria@) aperta con l'account di lavoro che ha questo Id, come
    /// "Aggiungi cartella o casella condivisa" del nuovo Outlook. Email è l'indirizzo della casella, Graph la legge da
    /// users/{indirizzo}/ con il token dell'account di lavoro e i permessi Mail.ReadWrite.Shared e Mail.Send.Shared.
    /// Null per gli account normali.
    /// </summary>
    public string? SharedOf { get; set; }
}

/// <summary>
/// Un altro indirizzo da cui scrivere con lo stesso account, come le identità di Thunderbird e gli altri indirizzi del Da di
/// Outlook: un alias (info@, segreteria@) o un indirizzo "Invia come". Nome, indirizzo e, se serve, un Rispondi a; la firma
/// è in Settings.Signatures con la chiave "account|identità". Il server deve accettarlo: Gmail solo se l'indirizzo è
/// configurato anche in Gmail (Invia messaggio come), Microsoft 365 con il permesso Invia come.
/// </summary>
public sealed class SendIdentity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>Dove vanno le risposte (Reply-To), vuoto per l'indirizzo stesso.</summary>
    public string ReplyTo { get; set; } = "";

    /// <summary>
    /// L'identità a cui era indirizzato il messaggio (A, Cc: i primi destinatari dell'elenco), per rispondere dallo stesso
    /// indirizzo, come Thunderbird; null se era per l'indirizzo principale o per nessuna identità.
    /// </summary>
    public static SendIdentity? For(MailAccount account, MessageSummary original, MessageBody? body = null)
    {
        if (account.Identities.Count == 0) return null;
        var to = original.To.Concat(body?.To ?? []).Concat(body?.Cc ?? []).Select(a => a.Email).ToList();
        if (to.Any(e => e.Equals(account.Email, StringComparison.OrdinalIgnoreCase))) return null;
        return account.Identities.FirstOrDefault(i => to.Any(e => e.Equals(i.Email, StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>Come si protegge il collegamento: sempre cifrato, dall'inizio (SSL/TLS) o dopo il saluto (STARTTLS).</summary>
public enum MailSecurity { Ssl, StartTls }

/// <summary>
/// I server di un account IMAP (dominio proprio su Aruba, Register.it, OVH, Zoho, cPanel, Exchange con IMAP): trovati
/// dalla configurazione automatica o scritti a mano. La password è cifrata con DPAPI, solo per questo utente di Windows.
/// </summary>
public sealed class ImapServer
{
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public MailSecurity ImapSecurity { get; set; } = MailSecurity.Ssl;
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 465;
    public MailSecurity SmtpSecurity { get; set; } = MailSecurity.Ssl;
    public string UserName { get; set; } = "";
    public string? ProtectedPassword { get; set; }
    /// <summary>
    /// Se il server salva da sé in Posta inviata i messaggi partiti via SMTP (come Gmail e Microsoft 365): allora Missiva
    /// non ne aggiunge una copia. Di solito no, e Missiva la aggiunge con APPEND, come Thunderbird.
    /// </summary>
    public bool ServerSavesSent { get; set; }
}

public sealed class MailFolder
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public FolderKind Kind { get; init; }
    public int Unread { get; set; }
    /// <summary>Messaggi in tutto (per l'avviso prima di eliminare la cartella).</summary>
    public int Total { get; set; }
    /// <summary>Cartella che la contiene (null se è al primo livello).</summary>
    public string? ParentId { get; init; }

    /// <summary>La stessa cartella con un altro nome (la copia locale, scritta in un'altra lingua: MailCache.LoadFolders).</summary>
    internal MailFolder Renamed(string name) => new() { Id = Id, Name = name, Kind = Kind, Unread = Unread, Total = Total, ParentId = ParentId };

    /// <summary>
    /// Il nome che Missiva dà a una cartella speciale, che il server chiama come vuole ("Sent", "[Gmail]/Posta inviata",
    /// "INBOX.Sent"): la chiave italiana, da mostrare con T (i provider la traducono dove la cartella nasce, e la copia locale
    /// la ritraduce se è stata scritta in un'altra lingua). Dipende dal tipo di account, come nei siti dei tre servizi:
    /// Posta eliminata e Archivio di Outlook, Cestino e Tutti i messaggi di Gmail, Spam di Gmail (che in inglese resta Spam).
    /// Null per le cartelle dell'utente (FolderKind.Other), che tengono il nome del server.
    /// </summary>
    public static string? SpecialName(AccountKind account, FolderKind kind) => kind switch
    {
        FolderKind.Inbox => N("Posta in arrivo"),
        FolderKind.Drafts => N("Bozze"),
        FolderKind.Sent => N("Posta inviata"),
        FolderKind.Deleted => account == AccountKind.Microsoft ? N("Posta eliminata") : N("Cestino"),
        FolderKind.Archive => account == AccountKind.Gmail ? N("Tutti i messaggi") : N("Archivio"),
        FolderKind.Junk => account == AccountKind.Gmail ? N("Spam") : N("Posta indesiderata"),
        _ => null,
    };

    /// <summary>
    /// Il nome di una cartella speciale nella lingua di Missiva (T di SpecialName); per FolderKind.Other vuoto. In inglese gli
    /// account Gmail hanno i nomi del sito di Gmail (Sent, Trash, All Mail, Spam), Microsoft e IMAP quelli di Outlook (Sent
    /// Items, Deleted Items, Junk Email): la chiave italiana "Posta inviata" e "Cestino" è la stessa di IMAP, quindi per Gmail
    /// in inglese il nome si scrive qui. In italiano nulla cambia.
    /// </summary>
    public static string SpecialShown(AccountKind account, FolderKind kind) =>
        account == AccountKind.Gmail && Lingua.English && GmailEnglish(kind) is { } gmail ? gmail
        : SpecialName(account, kind) is { } key ? T(key) : "";

    /// <summary>I nomi inglesi del sito di Gmail che non sono quelli di Outlook (le altre speciali hanno lo stesso nome nei due).</summary>
    static string? GmailEnglish(FolderKind kind) => kind switch
    {
        FolderKind.Sent => "Sent",
        FolderKind.Deleted => "Trash",
        _ => null,
    };
}

public sealed class Address
{
    readonly string name = "";
    string? display;

    /// <summary>
    /// Il nome scritto da chi manda, senza i caratteri che non si vedono (SenderCheck.CleanName: cambi di direzione del
    /// testo, larghezza zero): l'unico punto da cui passano i nomi, da qualunque provider e dalla copia locale.
    /// </summary>
    public string Name { get => name; init => name = SenderCheck.CleanName(value); }
    public string Email { get; init; } = "";

    /// <summary>
    /// Quello che elenco, notifiche e riquadro mostrano per la persona: il nome, o l'indirizzo se il nome manca o contiene
    /// l'indirizzo di un altro (SenderCheck.NameAddress; audit, S-25). Calcolato una volta per indirizzo, non a ogni disegno.
    /// </summary>
    public string Display => display ??= Name.Length == 0 || Email.Length > 0 && SenderCheck.NameAddress(this) != null ? Email : Name;
    public override string ToString() => Name.Length > 0 && Name != Email ? $"{Name} <{Email}>" : Email;

    /// <summary>
    /// Come va nei campi A, Cc e Ccn: "Nome &lt;indirizzo&gt;", senza virgole e punti e virgola nel nome, che separano i
    /// destinatari ("Rossi, Mario" diventerebbe due indirizzi, e "Rossi" non è valido).
    /// </summary>
    public string FieldText()
    {
        var words = Name.Split([' ', ',', ';', '<', '>'], StringSplitOptions.RemoveEmptyEntries);
        return new Address { Name = string.Join(' ', words), Email = Email }.ToString();
    }

    /// <summary>
    /// Reply-To come va tenuto: vuoto se sono gli stessi indirizzi del mittente (la busta IMAP ripete il mittente quando
    /// Reply-To manca).
    /// </summary>
    public static List<Address> ReplyToOf(IEnumerable<Address> replyTo, Address from)
    {
        var list = replyTo.Where(a => a.Email.Length > 0).ToList();
        return list.All(a => a.Email.Equals(from.Email, StringComparison.OrdinalIgnoreCase)) ? new() : list;
    }
}

public sealed class MessageSummary
{
    public required string Id { get; init; }
    public required string FolderId { get; init; }
    public Address From { get; init; } = new();
    public string Subject { get; init; } = "";
    /// <summary>Anteprima del testo: per Gmail arriva dopo l'elenco, in sottofondo.</summary>
    public string Preview { get; set; } = "";
    /// <summary>Data di arrivo; per un messaggio tornato da Posponi, l'ora in cui è tornato (come in Outlook).</summary>
    public DateTime Date { get; set; }
    /// <summary>
    /// La data come istante (UTC), per ordinare le righe; quella da mostrare resta Date, nell'ora di Windows. Nell'ora ripetuta
    /// della notte del ritorno all'ora solare due ore locali uguali sono due istanti diversi (DateTime confronta i tick, senza
    /// guardare quale delle due è): per ora locale un messaggio delle 2:10 dopo il cambio finiva sotto uno delle 2:50 di prima
    /// (audit di affidabilità del 25 ottobre 2026, elenco; nota del rapporto sull'arrivo). Le date locali delle righe vengono
    /// dall'istante del server (ToLocalTime, LocalDateTime), che ricorda quale delle due ore è.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime Instant => Date.Kind == DateTimeKind.Utc ? Date : Date.ToUniversalTime();
    /// <summary>Identificativo che non cambia spostando il messaggio (Gmail: X-GM-MSGID); serve a Posponi.</summary>
    public string StableId { get; init; } = "";
    public bool IsRead { get; set; }
    public bool IsFlagged { get; set; }
    public bool HasAttachments { get; init; }
    /// <summary>Conversazione a cui appartiene (vuoto se sconosciuta).</summary>
    public string ConversationId { get; init; } = "";
    /// <summary>
    /// Nomi degli allegati, per l'anteprima nell'elenco: gli stessi di <see cref="Attachment.Name"/>, puliti alla nascita
    /// (Safety.CleanAttachmentName, audit S-09; anche quelli della copia locale scritta prima).
    /// </summary>
    public List<string> AttachmentNames
    {
        get => attachmentNames;
        init
        {
            value ??= new();
            for (int i = 0; i < value.Count; i++)
                if (Safety.CleanAttachmentName(value[i]) is var clean && !ReferenceEquals(clean, value[i])) value[i] = clean;
            attachmentNames = value;
        }
    }
    List<string> attachmentNames = new();
    /// <summary>Categorie: etichette di Gmail o categorie di Outlook.</summary>
    public List<string> Categories { get; set; } = new();
    /// <summary>Nei risultati della ricerca in tutte le cartelle, la cartella del messaggio (vuoto altrove).</summary>
    public string FolderName { get; set; } = "";
    /// <summary>
    /// Destinatari (A), al massimo i primi 10: in Posta inviata e Bozze la riga dell'elenco mostra loro al posto del
    /// mittente, come in Outlook. Si salvano anche nella copia locale.
    /// </summary>
    public List<Address> To { get; set; } = new();
    /// <summary>
    /// Nei risultati della ricerca di Gmail in tutte le cartelle, il tipo della cartella del messaggio preso dalle
    /// etichette (Posta inviata, Bozze...): lì la cartella del messaggio è "Tutti i messaggi". Null altrove.
    /// </summary>
    public FolderKind? LabelKind { get; set; }
    /// <summary>Una bozza (Gmail: \Draft, Graph: isDraft): nel riquadro al posto di Rispondi c'è Modifica.</summary>
    public bool IsDraft { get; set; }
    /// <summary>
    /// Reply-To, solo se diverso dal mittente (IMAP e Gmail dalla busta, Graph dal campo replyTo): Rispondi va lì, come in
    /// Outlook e Thunderbird, e il riquadro lo dice ("Le risposte vanno a").
    /// </summary>
    public List<Address> ReplyTo { get; set; } = new();
    /// <summary>Importanza alta (Graph importance; X-Priority 1 o 2, Importance: high, Priority: urgent): il punto esclamativo sulla riga.</summary>
    public bool IsImportant { get; set; }
    /// <summary>Graph: la scadenza del contrassegno (flag dueDateTime), nell'ora di Windows; null senza data (Gmail e IMAP: Flags).</summary>
    public DateTime? FlagDue { get; set; }
    /// <summary>
    /// Graph: internetMessageId, il Message-Id dell'intestazione, che resta uguale spostando il messaggio (l'id no, e per
    /// Graph StableId è l'id). Serve ai contrassegni per ritrovare il messaggio (Flags.KeyOf). Vuoto per Gmail e IMAP.
    /// </summary>
    public string InternetMessageId { get; init; } = "";
    /// <summary>
    /// Graph: la scheda della Posta in arrivo (inferenceClassification), vero per Evidenziata e falso per Altra; null altrove e
    /// per Gmail e IMAP. Un messaggio nuovo entra in cima solo nella sua scheda (MainWindow.AddIncoming; audit di affidabilità
    /// del 25 ottobre 2026, elenco, E-09).
    /// </summary>
    public bool? Focused { get; set; }

    /// <summary>
    /// Lo stesso messaggio con un altro identificativo, per esempio tornato nella sua cartella con Annulla (in Gmail l'UID e in
    /// Graph l'id cambiano a ogni spostamento). Un campo nuovo di MessageSummary va copiato anche qui.
    /// </summary>
    public MessageSummary WithId(string id) => new()
    {
        Id = id, FolderId = FolderId, From = From, Subject = Subject, Preview = Preview, Date = Date, StableId = StableId,
        IsRead = IsRead, IsFlagged = IsFlagged, HasAttachments = HasAttachments, ConversationId = ConversationId,
        AttachmentNames = AttachmentNames, Categories = Categories, FolderName = FolderName, To = To, LabelKind = LabelKind,
        IsDraft = IsDraft, ReplyTo = ReplyTo, IsImportant = IsImportant, FlagDue = FlagDue, InternetMessageId = InternetMessageId, Focused = Focused,
    };
}

/// <summary>
/// Un messaggio spostato, per Annulla: com'era (cartella e identificativo) e l'identificativo nella cartella di arrivo, che
/// in Gmail (UID) e in Graph cambia con lo spostamento. Labels: in Gmail le etichette che aveva prima (X-GM-LABELS), lette
/// solo quando servono per rimetterle com'erano; null se non lette.
/// </summary>
public sealed record MovedMessage(MessageSummary Original, string NewId, IReadOnlyList<string>? Labels = null);

public enum MessageFilter { All, Unread, Flagged, Attachments }

public enum SortBy { Date, From, Subject }

/// <summary>
/// Come mostrare l'elenco (intestazione sopra l'elenco, come nel nuovo Outlook): filtro, ordinamento e "Vai a"
/// (solo i messaggi arrivati fino a quel giorno). Vale per tutta la cartella, perché lo applica il server.
/// </summary>
public sealed record ListOptions(MessageFilter Filter = MessageFilter.All, SortBy Sort = SortBy.Date, bool Ascending = false, DateTime? UpTo = null)
{
    public static readonly ListOptions Default = new();
    public bool IsDefault => this == Default;
}

public sealed class MessagePage
{
    public List<MessageSummary> Messages { get; init; } = new();
    /// <summary>Per caricare i messaggi successivi; null se sono finiti.</summary>
    public string? Next { get; init; }
}

public sealed class Attachment
{
    string name = "";

    public required string Id { get; init; }
    /// <summary>
    /// Il nome com'è mostrato e usato ovunque, senza i caratteri che non si vedono e come lo salverebbe Windows
    /// (Safety.CleanAttachmentName, audit S-09): l'unico punto da cui passano i nomi, da qualunque provider e dalla copia locale.
    /// </summary>
    public string Name { get => name; init => name = Safety.CleanAttachmentName(value); }
    public string ContentType { get; init; } = "";
    public long Size { get; init; }
    public bool IsInline { get; init; }
    public string? ContentId { get; init; }
    /// <summary>Contenuto già scaricato (immagini incorporate); altrimenti si scarica su richiesta.</summary>
    public byte[]? Content { get; set; }
}

public sealed class MessageBody
{
    public string Html { get; init; } = "";
    public List<Address> To { get; init; } = new();
    public List<Address> Cc { get; init; } = new();
    /// <summary>Ccn: c'è solo nei messaggi scritti da te (bozze, Posta inviata).</summary>
    public List<Address> Bcc { get; init; } = new();
    public List<Attachment> Attachments { get; init; } = new();
    /// <summary>
    /// Le intestazioni Authentication-Results del messaggio, dall'alto (la prima è l'ultima aggiunta, di solito dal server
    /// della propria casella): SenderCheck ne legge solo quella del proprio server.
    /// </summary>
    public List<string> AuthenticationResults { get; init; } = new();
    /// <summary>
    /// Le intestazioni List-Unsubscribe e List-Unsubscribe-Post (RFC 2369 e 8058) com'erano scritte, o null: arrivano con il
    /// corpo, come le Authentication-Results, e le legge Unsubscribe per il comando "Annulla iscrizione".
    /// </summary>
    public string? ListUnsubscribe { get; set; }
    public string? ListUnsubscribePost { get; set; }
    /// <summary>
    /// Le due intestazioni qui sopra non sono mai state lette: il corpo viene da una copia su disco fatta prima che Missiva
    /// le leggesse (BodyCache.Load). Null allora non vuol dire "il messaggio non le ha": il riquadro le chiede al server in
    /// sottofondo (IMailProvider.GetListHeadersAsync) e le scrive qui e nella copia.
    /// </summary>
    public bool ListHeadersUnread { get; set; }
    /// <summary>Un invito a una riunione nel messaggio (parte text/calendar o .ics, o eventMessage di Graph), o null.</summary>
    public Invitation? Invite { get; init; }
    /// <summary>Posta certificata aperta (o messaggio firmato S/MIME): che cosa è, chi, quando, la firma (Pec.Open), o null.</summary>
    public PecInfo? Pec { get; init; }
    /// <summary>
    /// Il messaggio com'è sul server, solo per le buste PEC e i messaggi firmati che Gmail e IMAP hanno già scaricato interi
    /// (sotto 300 KB): Pec li apre con questi byte invece di chiederli di nuovo.
    /// </summary>
    public byte[]? Raw { get; init; }
    /// <summary>
    /// Il mittente che decide le immagini esterne, se non è quello della riga: per la posta certificata il mittente vero
    /// (From di postacert.eml), non il gestore della busta (posta-certificata@pec.aruba.it è lo stesso per tutti i suoi clienti).
    /// </summary>
    public string? ImagesFrom { get; init; }
    /// <summary>Senza rete, dalla copia su disco (BodyCache): quando era stata salvata. Il riquadro lo dice.</summary>
    public DateTime? OfflineCopy { get; init; }
}

/// <summary>Le intestazioni List-Unsubscribe e List-Unsubscribe-Post di un messaggio, com'erano scritte; null quelle che non ha.</summary>
public sealed record ListHeaders(string? Unsubscribe, string? Post);

public enum ComposeMode { New, Reply, ReplyAll, Forward }

/// <summary>
/// A chi va una risposta, come in Outlook e Thunderbird: a Reply-To se c'è, altrimenti al mittente; Rispondi a tutti
/// aggiunge in Cc gli altri destinatari (A e Cc dell'originale), senza sé stessi e senza doppioni.
/// </summary>
public static class Replies
{
    public static (List<Address> To, List<Address> Cc) Recipients(MessageSummary original, MessageBody? body, string me, ComposeMode mode)
    {
        var to = original.ReplyTo.Count > 0 ? original.ReplyTo.ToList() : [original.From];
        if (mode != ComposeMode.ReplyAll || body == null) return (to, new());
        var cc = body.To.Concat(body.Cc)
            .Where(a => !a.Email.Equals(me, StringComparison.OrdinalIgnoreCase) && !to.Any(r => r.Email.Equals(a.Email, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(a => a.Email.ToLowerInvariant()).Select(g => g.First()).ToList();
        return (to, cc);
    }
}

/// <summary>
/// Una bozza salvata nella cartella Bozze del server, che la finestra di scrittura continua: il messaggio (in Graph conta
/// l'identificativo, in Gmail l'X-GM-MSGID, che resta lo stesso in tutte le cartelle), i suoi allegati come sono sul
/// server e, per le risposte di Graph, il corpo con la citazione creato da createReply, in cui va il testo nuovo.
/// </summary>
public sealed class SavedDraft
{
    public required MessageSummary Message { get; init; }
    public List<Attachment> Attachments { get; init; } = new();
    public string? QuoteHtml { get; init; }
    /// <summary>
    /// Le immagini incorporate nel testo che la bozza ha sul server (Graph): restano se il testo le mostra ancora, si
    /// eliminano se no, e quelle nuove si aggiungono. Gmail riscrive la bozza intera e non le usa.
    /// </summary>
    public List<Attachment> Images { get; init; } = new();
}

/// <summary>
/// Un'immagine nel testo scritto (incollata o trascinata nel corpo, o della bozza ripresa), come nel nuovo Outlook: parte
/// incorporata, con l'HTML che la mostra da cid:ContentId. Graph la aggiunge come fileAttachment con isInline e
/// contentId, Gmail come parte di multipart/related.
/// </summary>
public sealed record InlineImage(string ContentId, string Name, string ContentType, byte[] Content);

public sealed class OutgoingMessage
{
    public ComposeMode Mode { get; init; }
    public MessageSummary? Original { get; init; }
    public MessageBody? OriginalBody { get; init; }
    public List<Address> To { get; init; } = new();
    public List<Address> Cc { get; init; } = new();
    public List<Address> Bcc { get; init; } = new();
    public string Subject { get; init; } = "";
    public string Html { get; init; } = "";
    public string Text { get; init; } = "";
    public List<string> AttachmentPaths { get; init; } = new();
    /// <summary>Le immagini del testo (Html le mostra con cid:), nell'ordine in cui compaiono.</summary>
    public List<InlineImage> Images { get; init; } = new();
    /// <summary>La bozza che il messaggio continua: salvando si sostituisce, inviando sparisce dalle Bozze.</summary>
    public SavedDraft? Draft { get; init; }
    /// <summary>Gli allegati della bozza che restano (gli altri si tolgono): Graph li lascia sul server, Gmail li riscarica.</summary>
    public List<Attachment> KeptAttachments { get; init; } = new();
    /// <summary>
    /// Risposta a un invito (iMIP, RFC 6047): il testo iCalendar da mettere come parte text/calendar con il metodo
    /// CalendarMethod (REPLY o COUNTER), accanto al testo.
    /// </summary>
    public string? Calendar { get; init; }
    public string CalendarMethod { get; init; } = "REPLY";
    /// <summary>Opzioni della finestra di scrittura: importanza (Graph importance; X-Priority e Importance nel MIME).</summary>
    public Importance Importance { get; init; }
    /// <summary>Conferma di lettura (Graph isReadReceiptRequested; Disposition-Notification-To).</summary>
    public bool ReadReceipt { get; init; }
    /// <summary>Conferma di recapito (Graph isDeliveryReceiptRequested; DSN di SMTP con NOTIFY=SUCCESS,FAILURE).</summary>
    public bool DeliveryReceipt { get; init; }
    /// <summary>Invio pianificato che tiene il server (Graph: PidTagDeferredSendTime), null per subito.</summary>
    public DateTime? SendAt { get; init; }
    /// <summary>Da un altro indirizzo dell'account (alias, "Invia come"); null dall'indirizzo principale.</summary>
    public SendIdentity? From { get; init; }
    /// <summary>
    /// Ferma un caricamento lungo (gli allegati grandi di Graph, a pezzi: audit, F-15): lo annulla la finestra di scrittura
    /// che si chiude. Il provider lascia la bozza come dopo un errore, e ne esce OperationCanceledException.
    /// </summary>
    public CancellationToken Cancel { get; init; }
    /// <summary>Che cosa si sta caricando ("Caricamento di catalogo.pdf: 12 di 48 MB…"), per la riga di stato della scrittura.</summary>
    public IProgress<string>? Progress { get; init; }
}

public enum Importance { Normal, High, Low }

/// <summary>Quello che deve saper fare ogni tipo di account.</summary>
public interface IMailProvider
{
    MailAccount Account { get; }
    /// <summary>
    /// Chiuso: l'account è stato rimosso, o gli account sono stati ricaricati e al suo posto c'è un altro provider (Gmail e
    /// IMAP, che tengono connessioni aperte; Graph non si chiude mai). Una finestra di scrittura aperta prima passa a
    /// quello nuovo (ComposeWindow.Current).
    /// </summary>
    bool IsClosed => false;
    bool SupportsFocused { get; }
    Task<List<MailFolder>> GetFoldersAsync();
    /// <summary>
    /// Elimina una cartella creata dall'utente. Gmail: si toglie l'etichetta, i messaggi restano in Tutti i messaggi.
    /// Microsoft: la cartella con i suoi messaggi finisce in Posta eliminata.
    /// </summary>
    Task DeleteFolderAsync(MailFolder folder);
    /// <param name="focused">Solo per la Posta in arrivo Microsoft: true Evidenziata, false Altra, null tutta.</param>
    /// <param name="options">Filtro, ordinamento e "Vai a"; null è la vista normale (tutti, dal più recente).</param>
    Task<MessagePage> GetMessagesAsync(MailFolder folder, bool? focused, string? next, ListOptions? options = null);
    /// <summary>Se il server sa ordinare così tutta la cartella (Gmail via IMAP solo per data).</summary>
    bool SortsOnServer(SortBy sort);
    /// <summary>
    /// Anteprime dei messaggi che ne sono senza, per identificativo. Gmail le calcola scaricando parte del testo,
    /// lentamente: per questo non arrivano con l'elenco. Microsoft Graph le dà già con l'elenco.
    /// </summary>
    Task<Dictionary<string, string>> GetPreviewsAsync(MailFolder folder, IReadOnlyList<MessageSummary> messages) =>
        Task.FromResult(new Dictionary<string, string>());
    /// <summary>
    /// Ricerca in una cartella o, con folder null, in tutte quelle dell'account, come nel nuovo Outlook. I risultati
    /// arrivano a pagine di 50, dal più recente; next è quello della pagina precedente (null per la prima).
    /// </summary>
    Task<MessagePage> SearchAsync(MailFolder? folder, string query, string? next = null, CancellationToken cancel = default);
    /// <param name="cancel">
    /// Annullato quando si apre un altro messaggio (frecce, Ctrl+. tenuti premuti): Gmail non manda le richieste ancora in
    /// coda sulla connessione, Graph interrompe quelle partite.
    /// </param>
    Task<MessageBody> GetBodyAsync(MessageSummary message, CancellationToken cancel = default);
    /// <summary>
    /// Corpi scaricati in anticipo, come nel nuovo Outlook, perché poi GetBodyAsync li abbia subito; nell'ordine dato, senza
    /// segnarli letti e senza far aspettare le richieste dell'utente. Annullato, non comincia quelli che restano.
    /// </summary>
    Task PrefetchAsync(IReadOnlyList<MessageSummary> messages, CancellationToken cancel = default) => Task.CompletedTask;
    /// <summary>
    /// Gmail scarica in anticipo i primi messaggi dell'elenco e i due vicini di quello aperto (una FETCH costa 0,2-0,9 s);
    /// Graph, che apre già in 0,1-0,25 s, solo il successivo.
    /// </summary>
    bool PrefetchesMany => false;
    /// <summary>Il corpo già in memoria (aperto o scaricato in anticipo), senza nessuna richiesta; null se non c'è.</summary>
    MessageBody? KeptBody(MessageSummary message) => null;
    /// <summary>
    /// Il corpo dalla copia su disco (BodyCache.LoadOnline) anche in linea, senza nessuna richiesta: in Gmail e IMAP un
    /// messaggio non cambia (il nome del file ha l'UID e l'identificativo stabile). Null se non c'è, se la copia non basta (un
    /// invito, secondo la struttura arrivata con l'elenco) e per Graph, che resta al server (gli inviti eventMessage).
    /// </summary>
    MessageBody? SavedBody(MessageSummary message) => null;
    /// <summary>
    /// Le sole intestazioni List-Unsubscribe e List-Unsubscribe-Post di un messaggio, senza il corpo, per i corpi presi da una
    /// copia su disco che non le ha mai lette (MessageBody.ListHeadersUnread): Gmail e IMAP, sulla connessione in sottofondo
    /// in sola lettura. Null se il provider non lo fa (Graph le ha sempre con il corpo) o il messaggio non c'è più.
    /// </summary>
    Task<ListHeaders?> GetListHeadersAsync(MessageSummary message, CancellationToken cancel = default) => Task.FromResult<ListHeaders?>(null);
    /// <summary>
    /// Lavoro senza rete (OfflineSync): i messaggi della Posta in arrivo arrivati da sinceUtc, dal più recente, al massimo
    /// max, di cui scaricare i corpi in sottofondo. Di base le pagine dell'elenco (Graph); Gmail e IMAP sulla connessione in
    /// sottofondo, in sola lettura.
    /// </summary>
    async Task<List<MessageSummary>> GetRecentAsync(MailFolder inbox, DateTime sinceUtc, int max)
    {
        var recent = new List<MessageSummary>();
        string? next = null;
        do
        {
            var page = await GetMessagesAsync(inbox, null, next);
            recent.AddRange(page.Messages.Where(m => m.Date.ToUniversalTime() >= sinceUtc));
            if (page.Messages.Count == 0 || page.Messages.Min(m => m.Date).ToUniversalTime() < sinceUtc) break;
            next = page.Next;
        }
        while (next != null && recent.Count < max);
        return recent.Take(max).ToList();
    }
    /// <summary>
    /// Per la copia degli ultimi 30 giorni (OfflineCopy): gli stessi messaggi, ma di ognuno solo quello che serve a
    /// BodyCache.Has (identificativo, cartella, identificativo stabile, bozza, data). Di solito i corpi sono già tutti sul PC,
    /// e rileggere ogni mezz'ora le righe intere di centinaia di messaggi costava secondi di rete e centinaia di ms di CPU.
    /// Di base le righe intere.
    /// </summary>
    Task<List<MessageSummary>> GetRecentKeysAsync(MailFolder inbox, DateTime sinceUtc, int max) => GetRecentAsync(inbox, sinceUtc, max);
    /// <summary>Le righe intere dei messaggi dati da GetRecentKeysAsync (quelli che mancano alla copia): di base sono già quelle.</summary>
    Task<List<MessageSummary>> GetRecentAsync(MailFolder inbox, IReadOnlyList<MessageSummary> keys) => Task.FromResult(keys.ToList());
    /// <summary>Tutti i messaggi della conversazione, anche di altre cartelle (per esempio le tue risposte).</summary>
    Task<List<MessageSummary>> GetConversationAsync(MessageSummary message, CancellationToken cancel = default);
    /// <summary>Un allegato. Uno già in memoria (Attachment.Content: .eml, posta certificata) torna subito.</summary>
    /// <param name="progress">
    /// I byte arrivati, man mano (ritocchi del 16 ottobre 2026, il gestore degli scaricamenti, DownloadList): con Graph dalla
    /// lettura della risposta, con Gmail e IMAP dall'avanzamento di MailKit, nella misura di Attachment.Size (per una parte
    /// in base64 i tre quarti dei byte trasferiti). Se il server dice quanti ne arriveranno e l'avanzamento è anche un
    /// IExpectedSize, lo sa prima.
    /// </param>
    /// <param name="cancel">
    /// Annulla del gestore degli scaricamenti: ferma davvero la richiesta. Graph smette di leggere; con Gmail e IMAP il
    /// comando si interrompe, e MailKit chiude la connessione, che si riapre alla richiesta dopo.
    /// </param>
    Task<byte[]> GetAttachmentAsync(MessageSummary message, Attachment attachment, IProgress<long>? progress = null, CancellationToken cancel = default);
    // Le azioni sui messaggi valgono per più messaggi insieme: Gmail manda un solo comando per cartella, Graph più
    // richieste alla volta. Le varianti per un messaggio solo passano da quelle.
    Task SetReadAsync(IReadOnlyList<MessageSummary> messages, bool read);
    Task SetReadAsync(MessageSummary message, bool read) => SetReadAsync([message], read);
    Task SetFlagAsync(IReadOnlyList<MessageSummary> messages, bool flagged);
    Task SetFlagAsync(MessageSummary message, bool flagged) => SetFlagAsync([message], flagged);
    /// <summary>
    /// Contrassegna con inizio e scadenza (giorni interi; null e null: senza data). Graph li scrive nel contrassegno; Gmail e
    /// IMAP hanno solo \Flagged, e le date le tiene Missiva (Flags).
    /// </summary>
    Task SetFlagDueAsync(IReadOnlyList<MessageSummary> messages, DateTime? start, DateTime? due) => SetFlagAsync(messages, true);
    /// <summary>Segna come completato: Graph flagStatus complete con completedDateTime; Gmail e IMAP tolgono \Flagged.</summary>
    Task CompleteFlagAsync(IReadOnlyList<MessageSummary> messages) => SetFlagAsync(messages, false);
    /// <summary>
    /// Sposta; i messaggi che sono già nella cartella di destinazione restano dove sono. Restituisce quelli spostati, con
    /// l'identificativo nella cartella di arrivo, per Annulla (UndoMoveAsync).
    /// </summary>
    Task<List<MovedMessage>> MoveAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination);
    Task MoveAsync(MessageSummary message, MailFolder destination) => MoveAsync([message], destination);
    /// <summary>
    /// Annulla, come nel nuovo Outlook: i messaggi spostati da MoveAsync tornano nelle loro cartelle. Restituisce quelli
    /// tornati, con l'identificativo che hanno ora (NewId), per rimetterli nell'elenco.
    /// </summary>
    Task<List<MovedMessage>> UndoMoveAsync(IReadOnlyList<MovedMessage> moved, MailFolder destination);
    /// <summary>
    /// Coda delle azioni fatte senza rete (ActionQueue), prima di ripeterle: quali di questi messaggi ci sono ancora nella
    /// loro cartella, con la stessa chiave che non cambia (un UID può tornare a un altro messaggio se la cartella rinasce),
    /// gli stessi oggetti dati. Una lettura, in sola lettura (EXAMINE). null se il provider non lo sa dire prima (Graph: lo
    /// dice un 404 sull'azione stessa, che allora parte messaggio per messaggio).
    /// </summary>
    Task<List<MessageSummary>?> StillThereAsync(IReadOnlyList<MessageSummary> messages) => Task.FromResult<List<MessageSummary>?>(null);
    /// <summary>Categorie disponibili: in Gmail le etichette create dall'utente, in Microsoft quelle di Outlook.</summary>
    Task<List<Category>> GetCategoriesAsync();
    Task<Category> CreateCategoryAsync(string name, int preset);
    /// <summary>Aggiunge o toglie una categoria ai messaggi (e aggiorna message.Categories).</summary>
    Task SetCategoryAsync(IReadOnlyList<MessageSummary> messages, string category, bool on);
    /// <summary>Nuova cartella, dentro parent o al primo livello (in Gmail le cartelle di sistema non ne hanno dentro).</summary>
    Task<MailFolder> CreateFolderAsync(string name, MailFolder? parent);
    /// <summary>Rinomina; restituisce il nuovo identificativo (in Gmail è il percorso, quindi cambia).</summary>
    Task<string> RenameFolderAsync(MailFolder folder, string name);
    /// <summary>Sposta la cartella dentro un'altra (null: al primo livello); restituisce il nuovo identificativo.</summary>
    Task<string> MoveFolderAsync(MailFolder folder, MailFolder? parent);
    /// <summary>Svuota: dal Cestino e dallo Spam i messaggi si eliminano per sempre, dalle altre cartelle vanno nel Cestino.</summary>
    Task EmptyFolderAsync(MailFolder folder, MailFolder? trash);
    Task MarkAllReadAsync(MailFolder folder);
    /// <summary>La cartella con quel nome al primo livello, creata se manca (per Posposti).</summary>
    Task<MailFolder> EnsureFolderAsync(string name);
    /// <summary>Posponi: sposta il messaggio nella cartella e restituisce la chiave per ritrovarlo.</summary>
    Task<string> SnoozeAsync(MessageSummary message, MailFolder snoozeFolder);
    /// <summary>
    /// Fine di Posponi: il messaggio torna in Posta in arrivo, da leggere. Restituisce la sua chiave dopo il ritorno,
    /// o null se non c'è più (eliminato o spostato nel frattempo).
    /// </summary>
    Task<string?> WakeAsync(string key, MailFolder snoozeFolder, MailFolder inbox);
    /// <summary>Una copia nella cartella indicata (in Gmail: un'etichetta in più).</summary>
    Task CopyAsync(IReadOnlyList<MessageSummary> messages, MailFolder destination);
    /// <summary>Il messaggio originale in formato MIME, com'è sul server, come file .eml.</summary>
    /// <param name="limit">Al massimo tanti byte dall'inizio, e il resto non si scarica: l'Origine ne mostra 2 MB.</param>
    /// <param name="cancel">
    /// Superato (si è aperto un altro messaggio): Gmail e IMAP non partono se aspettavano la connessione, Graph smette di
    /// leggere fra un blocco e l'altro. Per il messaggio intero (senza limit: Salva con nome .eml, dal gestore degli
    /// scaricamenti) anche Gmail e IMAP interrompono il comando (ritocchi del 16 ottobre 2026), sulla loro connessione
    /// delle bozze, che si riapre alla richiesta dopo; l'Origine e la posta certificata, con un limit, restano com'erano.
    /// </param>
    /// <param name="progress">I byte arrivati, man mano, come in GetAttachmentAsync.</param>
    Task<byte[]> GetRawAsync(MessageSummary message, int limit = int.MaxValue, CancellationToken cancel = default, IProgress<long>? progress = null);
    /// <summary>Eliminazione definitiva (dal Cestino).</summary>
    Task DeletePermanentlyAsync(IReadOnlyList<MessageSummary> messages);
    /// <summary>Invia; se il messaggio continua una bozza, la bozza sparisce dalle Bozze (Graph invia proprio quella).</summary>
    Task SendAsync(OutgoingMessage message);
    /// <summary>
    /// Salva il messaggio nelle Bozze, al posto della bozza che continua (message.Draft), come nel nuovo Outlook. Graph:
    /// POST me/messages (o createReply) la prima volta, poi PATCH dello stesso messaggio. Gmail: APPEND nelle Bozze con
    /// \Draft e \Seen, e la bozza di prima si elimina (UID EXPUNGE).
    /// </summary>
    Task<SavedDraft> SaveDraftAsync(OutgoingMessage message);
    /// <summary>
    /// Scarta, nella finestra di scrittura, come nel nuovo Outlook: la bozza salvata da quella finestra (da sola o con
    /// Ctrl+S) esce dalle Bozze; anche cambiando "Da", prima di ricrearla nell'altro account. Graph: DELETE del messaggio.
    /// Gmail: \Deleted e UID EXPUNGE nelle Bozze, come per la bozza di prima in SaveDraftAsync.
    /// </summary>
    Task DiscardDraftAsync(SavedDraft draft);
    /// <summary>Messaggi non letti arrivati nella Posta in arrivo dopo l'istante indicato (per le notifiche).</summary>
    Task<List<MessageSummary>> GetNewAsync(DateTime sinceUtc);
    /// <summary>
    /// Per le notifiche (App.CheckAccountAsync): i messaggi da leggere arrivati nella Posta in arrivo dopo mark
    /// ("UIDVALIDITY:UID" dell'ultimo visto dal controllo di prima), con il mark nuovo da passare la volta dopo; senza mark
    /// (la prima volta, o con una UIDVALIDITY nuova) quelli arrivati dopo sinceUtc, come GetNewAsync. Di base (Graph) è
    /// GetNewAsync, senza mark.
    /// </summary>
    async Task<Arrivals> GetNewAsync(string? mark, DateTime sinceUtc) => new(await GetNewAsync(sinceUtc), null);
    /// <summary>
    /// Per le regole di Missiva (Gmail, IMAP): i messaggi arrivati nella Posta in arrivo dopo mark (UIDVALIDITY e l'ultimo UID
    /// visto dalle regole), letti o no e senza il limite dei 20 delle notifiche, dal più vecchio, al massimo 500 per volta;
    /// senza mark (la prima volta, o con una UIDVALIDITY nuova) quelli arrivati dopo sinceUtc. Con il mark nuovo da passare
    /// la volta dopo. null se il provider non ce l'ha (Graph: le regole sono quelle di Outlook, sul server).
    /// </summary>
    Task<Arrivals?> GetArrivedAsync(string? mark, DateTime sinceUtc) => Task.FromResult<Arrivals?>(null);
    /// <summary>I non letti della Posta in arrivo contati dal server: scendono anche leggendo altrove (telefono, web).</summary>
    Task<int> GetInboxUnreadAsync();
    /// <summary>
    /// La Posta in arrivo sorvegliata dal server (IMAP IDLE, InboxIdle): changed a ogni avviso (nuovo, segni cambiati con
    /// l'UID se il server lo dice, uscito), sul thread della connessione. Finisce con cancel, o subito se il server non ha
    /// IDLE; Graph non ce l'ha.
    /// </summary>
    Task WatchInboxAsync(Action<InboxNotice> changed, CancellationToken cancel) => Task.CompletedTask;
    /// <summary>Se l'account legge i cambiamenti della Posta in arrivo con la query delta (Graph), ogni 30 secondi.</summary>
    bool HasInboxDelta => false;
    /// <summary>I cambiamenti della Posta in arrivo dall'ultima volta (Graph delta); null alla prima lettura, che fa da punto di partenza.</summary>
    Task<InboxChanges?> GetInboxChangesAsync(CancellationToken cancel = default) => Task.FromResult<InboxChanges?>(null);
    /// <summary>Destinatari (A e Cc) degli ultimi max messaggi della Posta inviata, per la rubrica dei suggerimenti.</summary>
    Task<List<SentRecipients>> GetSentRecipientsAsync(int max);
    /// <summary>
    /// Risposta a un invito. Di base iMIP (RFC 6047): se va mandata, un messaggio all'organizzatore con METHOD:REPLY (o
    /// COUNTER per un nuovo orario), senza permessi nuovi. Graph, per gli inviti di Exchange (eventMessage), risponde
    /// sull'evento del calendario (accept, tentativelyAccept, decline), che serve il consenso al calendario.
    /// </summary>
    Task RespondAsync(MessageSummary message, Invitation invite, InviteResponse response) =>
        response.Send && invite.Organizer != null ? SendAsync(invite.ReplyMessage(Account, response)) : Task.CompletedTask;
    /// <summary>
    /// Il consenso al calendario (solo Graph, Calendars.ReadWrite), chiesto solo quando l'utente preme "Consenti l'accesso
    /// al calendario": apre la finestra di Microsoft. Mai all'accesso di base né da solo.
    /// </summary>
    Task AllowCalendarAsync() => Task.CompletedTask;
    /// <summary>
    /// Se l'account ha un calendario che Missiva legge: Microsoft (Graph) e Google (Calendar API, dal permesso del calendario
    /// dato dal pulsante); gli account IMAP aspettano CalDAV.
    /// </summary>
    bool HasCalendar => false;
    /// <summary>
    /// Calendari ed eventi fra from e to (ore di Windows). null se manca il consenso al calendario: senza finestre, lo chiede
    /// solo AllowCalendarAsync dal pulsante.
    /// </summary>
    Task<CalendarData?> GetCalendarAsync(DateTime from, DateTime to, CancellationToken cancel = default) => Task.FromResult<CalendarData?>(null);
    /// <summary>
    /// Un evento nuovo nel calendario (Graph, con il consenso al calendario); con partecipanti è una riunione e il server
    /// manda gli inviti. Restituisce l'identificativo dell'evento. Una scrittura: nella prova solo nel registro.
    /// </summary>
    Task<string> CreateEventAsync(NewEvent e) => Task.FromException<string>(new NotSupportedException());
    /// <summary>
    /// Accetta, Provvisorio o Rifiuta dai dettagli di un evento del calendario (Graph accept, tentativelyAccept, decline),
    /// con la nota e con o senza risposta all'organizzatore. Una scrittura: nella prova solo nel registro.
    /// </summary>
    Task RespondToEventAsync(CalendarEvent e, InviteAnswer answer, string comment, bool send) => Task.FromException(new NotSupportedException());
    /// <summary>
    /// Un evento proprio cambiato (Modifica, trascinato): solo i campi cambiati (Calendars.Changes); con series tutta la
    /// serie invece della sola occorrenza. Con partecipanti Exchange manda l'aggiornamento. Una scrittura: nella prova solo
    /// nel registro.
    /// </summary>
    Task UpdateEventAsync(CalendarEvent original, NewEvent changed, bool series) => Task.FromException(new NotSupportedException());
    /// <summary>Elimina un evento proprio senza partecipanti (o tutta la sua serie). Una scrittura: nella prova solo nel registro.</summary>
    Task DeleteEventAsync(CalendarEvent e, bool series) => Task.FromException(new NotSupportedException());
    /// <summary>
    /// Annulla una riunione organizzata da sé, con la nota per i partecipanti (Graph cancel con Comment): la riunione esce dai
    /// calendari di tutti. Una scrittura: nella prova solo nel registro.
    /// </summary>
    Task CancelEventAsync(CalendarEvent e, string comment, bool series) => Task.FromException(new NotSupportedException());
    /// <summary>
    /// La disponibilità dei partecipanti fra from e to, a mezz'ore (Graph getSchedule): solo per gli account di lavoro o della
    /// scuola, con il consenso al calendario; null senza consenso (nessuna richiesta). Una lettura.
    /// </summary>
    Task<List<Availability>?> GetScheduleAsync(IReadOnlyList<string> emails, DateTime from, DateTime to) => Task.FromResult<List<Availability>?>(null);
    /// <summary>
    /// Se l'account ha dei contatti che Missiva legge: quelli di Outlook (Graph me/contacts) e quelli di Google (People API);
    /// per IMAP ci sono solo quelli di Missiva (ContactBook).
    /// </summary>
    bool HasContacts => false;
    /// <summary>
    /// I contatti dell'account si leggono soltanto (Google senza l'ambito di scrittura, GoogleAuth.ContactsWriteScope): Persone apre Modifica
    /// in Google Contatti, senza Elimina, e propone "Consenti la modifica dei contatti". Con l'ambito, o per Outlook, si scrivono da Missiva.
    /// </summary>
    bool ContactsReadOnly => false;
    /// <summary>I contatti dell'account; null senza il consenso ai contatti (nessuna richiesta, nessuna finestra).</summary>
    Task<List<Contact>?> GetContactsAsync(CancellationToken cancel = default) => Task.FromResult<List<Contact>?>(null);
    /// <summary>Il consenso ai contatti (Graph Contacts.ReadWrite), solo dal pulsante "Consenti l'accesso ai contatti".</summary>
    Task AllowContactsAsync() => Task.CompletedTask;
    /// <summary>Il consenso a modificare i contatti di Google (ambito contacts), solo dal pulsante "Consenti la modifica dei contatti". Mai da solo.</summary>
    Task AllowContactsWriteAsync() => Task.CompletedTask;
    /// <summary>
    /// Un contatto nuovo (before null: POST) o cambiato (PATCH dei soli campi cambiati); restituisce l'id sul server. Una
    /// scrittura: nella prova solo nel registro.
    /// </summary>
    Task<string> SaveContactAsync(Contact? before, Contact after) => Task.FromException<string>(new NotSupportedException());
    /// <summary>Elimina un contatto dell'account. Una scrittura: nella prova solo nel registro.</summary>
    Task DeleteContactAsync(Contact contact) => Task.FromException(new NotSupportedException());
    /// <summary>Se l'account cerca anche fra i colleghi della directory (Graph me/people, solo Microsoft 365 di lavoro o della scuola).</summary>
    bool HasPeople => false;
    /// <summary>Le persone rilevanti (colleghi compresi) che corrispondono al testo; null senza il consenso alle persone (nessuna richiesta).</summary>
    Task<List<Contact>?> SearchPeopleAsync(string text, CancellationToken cancel = default) => Task.FromResult<List<Contact>?>(null);
    /// <summary>Il consenso alle persone (Graph People.Read), solo da "Cerca anche fra i colleghi" nei suggerimenti. Mai da solo.</summary>
    Task AllowPeopleAsync() => Task.CompletedTask;
    /// <summary>Se l'account ha le risposte automatiche di Outlook (Graph, me/mailboxSettings). Gmail e IMAP no.</summary>
    bool HasAutoReplies => false;
    /// <summary>Le risposte automatiche; null se manca il consenso alle impostazioni della casella (nessuna finestra).</summary>
    Task<AutoReplies?> GetAutoRepliesAsync() => Task.FromResult<AutoReplies?>(null);
    /// <summary>Cambia le risposte automatiche (una scrittura: nella prova solo nel registro).</summary>
    Task SetAutoRepliesAsync(AutoReplies replies) => Task.FromException(new NotSupportedException());
    /// <summary>
    /// Il consenso alle impostazioni della casella (solo Graph, MailboxSettings.ReadWrite: risposte automatiche e categorie
    /// di Outlook), chiesto solo dal pulsante "Consenti l'accesso alle impostazioni della casella". Mai da solo.
    /// </summary>
    Task AllowMailboxSettingsAsync() => Task.CompletedTask;
    /// <summary>
    /// Se l'account ha le regole di Outlook sul server (Graph me/mailFolders/inbox/messageRules, non per le caselle
    /// condivise): girano su Exchange anche a Missiva chiusa. Gmail e IMAP hanno le regole di Missiva (Rules).
    /// </summary>
    bool HasServerRules => false;
    /// <summary>
    /// Le regole della Posta in arrivo sul server, nell'ordine in cui si applicano; null se manca il consenso alle impostazioni
    /// della casella (nessuna richiesta, nessuna finestra: lo chiede AllowMailboxSettingsAsync dal pulsante).
    /// </summary>
    Task<List<MailRule>?> GetRulesAsync() => Task.FromResult<List<MailRule>?>(null);
    /// <summary>
    /// Una regola nuova (before null: POST) o cambiata (PATCH dei soli campi cambiati, nessuna richiesta senza modifiche);
    /// restituisce l'id sul server. Una scrittura: nella prova solo nel registro.
    /// </summary>
    Task<string> SaveRuleAsync(MailRule? before, MailRule after) => Task.FromException<string>(new NotSupportedException());
    /// <summary>Elimina una regola del server. Una scrittura: nella prova solo nel registro.</summary>
    Task DeleteRuleAsync(MailRule rule) => Task.FromException(new NotSupportedException());
    /// <summary>
    /// Il consenso alle caselle condivise (solo Graph, Mail.ReadWrite.Shared e Mail.Send.Shared), chiesto solo dal pulsante
    /// "Consenti l'accesso alle caselle condivise" sotto la casella. Mai all'avvio né da solo.
    /// </summary>
    Task AllowSharedAsync() => Task.CompletedTask;
    /// <summary>
    /// Il Cestino, creato al primo bisogno dove manca (IMAP, come Thunderbird: molti server lo creano solo quando un
    /// programma lo chiede). null se l'account non lo crea: Graph e Gmail lo hanno sempre.
    /// </summary>
    Task<MailFolder?> EnsureTrashAsync() => Task.FromResult<MailFolder?>(null);
    /// <summary>Se l'invio pianificato lo tiene il server (Graph: Exchange, con PidTagDeferredSendTime), anche a Missiva chiusa.</summary>
    bool SchedulesOnServer => false;
    /// <summary>
    /// L'invio pianificato fatto da Missiva (Gmail, IMAP): la bozza con quella chiave (StableId) parte così com'è sul server,
    /// poi esce dalle Bozze. false se la bozza non c'è più (eliminata o modificata nel frattempo).
    /// </summary>
    Task<bool> SendDraftAsync(string draftKey) => Task.FromException<bool>(new NotSupportedException());
    /// <summary>
    /// Dopo Gestisci account (server o password nuovi, nuovo accesso): le connessioni aperte si chiudono, e la prossima
    /// richiesta si collega con le impostazioni nuove. Graph e Gmail prendono già i token nuovi da sé.
    /// </summary>
    Task ResetConnectionsAsync() => Task.CompletedTask;
    /// <summary>
    /// Dopo la ripresa del PC o un cambio di rete (App.WakeAsync): tutte le connessioni si buttano subito, senza saluto e
    /// senza aspettare chi le sta usando, perché dopo la sospensione sono morte senza che TCP lo sappia. Chi stava aspettando
    /// una risposta (un controllo, una sessione dell'IDLE) riceve un errore e riprova una volta su una connessione nuova
    /// (audit di affidabilità del 25 ottobre 2026, A-04). Gmail e IMAP; Graph usa connessioni HTTP, che .NET rifà da sé.
    /// </summary>
    void DropConnections() { }
}

/// <summary>Un messaggio della Posta inviata, per la rubrica: quando è partito e per chi.</summary>
public sealed record SentRecipients(DateTime Date, List<Address> Recipients);

/// <summary>I messaggi arrivati per le regole di Missiva (IMailProvider.GetArrivedAsync) e fin dove si è visto ("UIDVALIDITY:UID").</summary>
public sealed record Arrivals(List<MessageSummary> Messages, string? Mark);

/// <param name="status">Il codice HTTP della risposta di Microsoft Graph (0 per gli altri errori).</param>
public sealed class MailException(string message, Exception? inner = null, int status = 0) : Exception(message, inner)
{
    public int Status { get; } = status;
    /// <summary>Il codice d'errore di Microsoft Graph (ErrorSendAsDenied, ErrorAccessDenied...), vuoto per gli altri.</summary>
    public string Code { get; init; } = "";
    /// <summary>
    /// Quello che l'utente deve rifare perché l'account torni a funzionare: la password (il server IMAP l'ha rifiutata) o
    /// l'accesso (Microsoft o Google lo chiedono di nuovo). Sotto l'account la barra lo propone (Impostazioni, Account,
    /// Gestisci), senza aprire nulla da sola.
    /// </summary>
    public AccountFix Fix { get; init; }
    /// <summary>
    /// Un invio via SMTP interrotto quando il messaggio era già stato consegnato per intero al server, senza la sua risposta
    /// (connessione caduta, tempo scaduto): il server può averlo accettato lo stesso. Chi riprova da solo (invii
    /// pianificati, Posta in uscita) non lo fa, per non mandarlo due volte: lo dice, e decide chi scrive (audit dell'11
    /// ottobre, C-61).
    /// </summary>
    public bool MaybeSent { get; init; }
    /// <summary>
    /// Uno spostamento che il server ha rifiutato con un comando solo (UID MOVE, RFC 6851): ogni messaggio è stato spostato
    /// oppure è rimasto com'era, e rifarlo su quelli rimasti non lascia doppioni. Senza MOVE lo spostamento è una copia
    /// seguita da un'eliminazione, e un rifiuto a metà può aver già copiato: quello non si ripete. Lo guardano le regole di
    /// Missiva, che riprovano uno spostamento non riuscito solo con questo (Rules.RunAsync; scelte del 12 ottobre, C-51).
    /// </summary>
    public bool Repeatable { get; init; }

    /// <summary>
    /// Il provider è stato chiuso (account rimosso, o account ricaricati dopo un'aggiunta) e una finestra aperta prima lo usa
    /// ancora. Prima usciva l'errore di .NET per un oggetto già rilasciato, e un invio partiva lo stesso via SMTP senza copia
    /// in Posta inviata, in silenzio (audit dell'11 ottobre, X-1). La scrittura e il messaggio aperto in una finestra sua
    /// passano da soli al provider nuovo dello stesso account (ComposeWindow.Current): lì questo errore arriva solo quando
    /// l'account è stato rimosso, e lo dice; altrove (una finestra che tiene ancora il provider di prima) dice di riaprirla.
    /// </summary>
    public static MailException Closed(MailAccount account)
    {
        // Può arrivare da un thread in sottofondo mentre l'interfaccia cambia l'elenco degli account: se non si riesce a guardarlo, il testo che dice di riaprire.
        bool removed = false;
        try { removed = !Store.Settings.Accounts.ToArray().Any(a => a?.Id == account.Id); }
        catch (Exception) { }
        return new(ClosedText(account.Email, removed));
    }

    /// <summary>Il testo dell'errore di un provider chiuso: perché, e che cosa fare.</summary>
    internal static string ClosedText(string email, bool removed) =>
        removed
            ? T("Il collegamento di {0} usato da questa finestra è stato chiuso: l'account è stato rimosso da Missiva. Da questa finestra non si può più leggere né cambiare nulla di quell'account: chiudila; se stavi scrivendo, copia prima il testo.", email)
            : T("Il collegamento di {0} usato da questa finestra è stato chiuso: gli account sono stati ricaricati (un account o una casella condivisa aggiunti o rimossi). Chiudi la finestra e riaprila; se stavi scrivendo, copia prima il testo.", email);

    /// <summary>L'errore, o uno di quelli che contiene, dice che il messaggio può essere partito lo stesso (MaybeSent).</summary>
    public static bool MaybeSentOf(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is MailException { MaybeSent: true }) return true;
            if (e is AggregateException { InnerExceptions: [var only] }) return MaybeSentOf(only);
        }
        return false;
    }

    /// <summary>Il rimedio chiesto dall'errore o da uno di quelli che contiene (None se non ce n'è).</summary>
    public static AccountFix FixOf(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is MailException { Fix: not AccountFix.None } m) return m.Fix;
            if (e is AggregateException { InnerExceptions: [var only] }) return FixOf(only);
        }
        return AccountFix.None;
    }
}

/// <summary>
/// Il rimedio per un account che non funziona più: nuova password (IMAP), nuovo accesso (Microsoft, Google) o, per le
/// caselle condivise di Microsoft 365, il consenso a usarle (chiesto solo dal pulsante della barra).
/// </summary>
public enum AccountFix { None, Password, SignIn, Consent }
