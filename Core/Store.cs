using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Missiva;

public enum TextSize { Small, Medium, Large }

public enum ReadingPanePosition { Right, Bottom, Off }

/// <summary>Il tema di Missiva (Impostazioni, Generali, Aspetto): quello delle app di Windows, oppure sempre chiaro o scuro.</summary>
public enum AppTheme { System, Light, Dark }

/// <summary>Quando un messaggio aperto nel riquadro diventa letto (Impostazioni, Posta, Gestione messaggi, come in Outlook).</summary>
public enum MarkReadMode { Immediately, AfterDelay, OnSelectionChange, Never }

public sealed class Settings
{
    /// <summary>ID dell'app registrata su Microsoft Entra, necessario per gli account Microsoft.</summary>
    public string? MicrosoftClientId { get; set; }
    /// <summary>Client OAuth di tipo "App desktop" creato su Google Cloud, necessario per gli account Gmail.</summary>
    public string? GoogleClientId { get; set; }
    /// <summary>Il suo client secret, cifrato. Per le app desktop Google non lo considera riservato, ma resta cifrato lo stesso.</summary>
    public string? ProtectedGoogleClientSecret { get; set; }
    public List<MailAccount> Accounts { get; set; } = new();

    // Generali
    public AppTheme Theme { get; set; } = AppTheme.System;
    /// <summary>Account senza notifiche dei nuovi messaggi (identificativi).</summary>
    public List<string> NotificationsOff { get; set; } = new();
    public bool NotificationSound { get; set; } = true;
    /// <summary>
    /// Chiudendo la finestra Missiva resta nell'area di notifica (Impostazioni, Generali, Avvio e chiusura), come Outlook
    /// classico: controlla la posta, mostra le notifiche, manda gli invii pianificati. Falso: si chiude del tutto.
    /// </summary>
    public bool KeepInTray { get; set; } = true;
    /// <summary>La prima volta che la finestra si chiude nell'area di notifica una notifica lo spiega; poi non più.</summary>
    public bool TrayExplained { get; set; }
    /// <summary>Avvia Missiva all'accesso a Windows, nascosta nell'area di notifica (HKCU Run con --nascosta).</summary>
    public bool StartWithWindows { get; set; }
    /// <summary>L'avviso dei testi cifrati che non si decifrano più (App, all'avvio) è già stato dato: una volta, non a ogni avvio.</summary>
    public bool UnreadableTold { get; set; }

    // Elenco messaggi (come Impostazioni, Posta, Elenco messaggi di Outlook)
    /// <summary>Posta in arrivo evidenziata, per account (solo Microsoft). Se manca vale "sì".</summary>
    public Dictionary<string, bool> Focused { get; set; } = new();
    public TextSize TextSize { get; set; } = TextSize.Medium;
    public bool GroupByConversation { get; set; } = true;
    public bool ReadingWholeConversation { get; set; } = true;
    public int PreviewLines { get; set; } = 1;
    public bool SenderFirst { get; set; } = true;
    public bool ShowSenderImages { get; set; } = true;
    /// <summary>Il logo del marchio al posto delle iniziali (SenderLogos): verificato (BIMI) o, per i domini noti, del sito.</summary>
    public bool ShowSenderLogos { get; set; } = true;
    public bool ShowAttachmentPreviews { get; set; } = true;
    public bool ShowDateHeaders { get; set; } = true;

    // Riquadro di lettura
    public ReadingPanePosition ReadingPane { get; set; } = ReadingPanePosition.Right;
    public bool LightReadingPane { get; set; }
    public int Zoom { get; set; } = 100;

    // Gestione messaggi
    public MarkReadMode MarkRead { get; set; } = MarkReadMode.Immediately;
    /// <summary>Con MarkReadMode.AfterDelay: i secondi che il messaggio resta aperto prima di diventare letto.</summary>
    public int MarkReadSeconds { get; set; } = 5;

    // Scrittura e risposta
    /// <summary>Annulla invio: i secondi che il messaggio aspetta prima di partire (0, 5 o 10), come nel nuovo Outlook.</summary>
    public int UndoSendSeconds { get; set; } = 5;
    /// <summary>
    /// Avviso di allegato dimenticato, come nel nuovo Outlook: a Invia, se il testo parla di un allegato e non ce n'è nessuno,
    /// una domanda (ComposeWindow.AttachmentMention). Acceso di base.
    /// </summary>
    public bool AttachmentReminder { get; set; } = true;
    /// <summary>
    /// Formato del messaggio (Impostazioni, Posta, Scrittura e risposta), come nel nuovo Outlook: il carattere, la dimensione
    /// in punti e il colore ("#RRGGBB", vuoto per automatico) dei nuovi messaggi, delle risposte e degli inoltri.
    /// </summary>
    /// <remarks>
    /// Solo un nome di carattere (HtmlToFlow.FontName), anche se in impostazioni.json o in una copia di sicurezza qualcuno ha
    /// scritto altro: un indirizzo al posto del nome farebbe cercare il carattere in rete a ogni finestra di scrittura
    /// (audit dell'11 ottobre, T-31).
    /// </remarks>
    public string ComposeFont { get; set => field = HtmlToFlow.FontName(value) ?? "Segoe UI"; } = "Segoe UI";
    public double ComposeFontSize { get; set; } = 11;
    public string ComposeColor { get; set; } = "";
    /// <summary>Invii pianificati di Gmail e IMAP, che fa Missiva: la bozza nelle Bozze e l'ora (Graph li tiene Exchange).</summary>
    public List<ScheduledSend> ScheduledSends { get; set; } = new();
    /// <summary>I modelli di risposta (Impostazioni, Posta, Modelli; pulsante Modelli nella scrittura), cifrati con DPAPI.</summary>
    public List<MailTemplate> Templates { get; set; } = new();
    /// <summary>
    /// Le regole di Missiva per Gmail e IMAP (Impostazioni, Posta, Regole), nell'ordine in cui si applicano; parole, indirizzi
    /// e cartelle cifrati con DPAPI. Quelle degli account Microsoft sono le regole di Outlook e restano sul server.
    /// </summary>
    public List<MailRule> Rules { get; set; } = new();
    /// <summary>
    /// I messaggi a cui le regole di Missiva sono già state applicate (un'impronta dell'account e della chiave che non cambia,
    /// non l'indirizzo né l'oggetto), gli ultimi 2000: una regola non agisce due volte sullo stesso messaggio.
    /// </summary>
    public List<string> RulesSeen { get; set; } = new();

    /// <summary>
    /// Ultime ricerche fatte, la più recente prima: il pannello della ricerca le propone, come il nuovo Outlook. Cifrate come le
    /// regole (ProtectedListConverter): con i filtri e la scheda del mittente dicono nomi di clienti e pratiche
    /// (da:"Avv. Mario Rossi" oggetto:"Pratica Bianchi"). Quelle in chiaro di prima si leggono ancora.
    /// </summary>
    [JsonConverter(typeof(ProtectedListConverter))] public List<string> RecentSearches { get; set; } = new();

    public void RememberSearch(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;
        RecentSearches.RemoveAll(ProtectedTextConverter.IsUnreadable); // ricerche di un altro PC che non si decifrano: l'elenco riparte
        RecentSearches.RemoveAll(q => q.Equals(query, StringComparison.CurrentCultureIgnoreCase));
        RecentSearches.Insert(0, query);
        if (RecentSearches.Count > 10) RecentSearches.RemoveRange(10, RecentSearches.Count - 10);
    }

    /// <summary>
    /// Mittenti di cui mostrare sempre le immagini esterne, per tutti gli account: domini delle organizzazioni
    /// (google.com vale anche per accounts.google.com, perché le aziende scrivono da molti indirizzi) e, per la posta
    /// personale (gmail.com, outlook.it...), indirizzi interi. Vedi <see cref="ImageKey"/>.
    /// </summary>
    /// <remarks>Cifrati nel file come le ricerche recenti (audit, blocco 10, S-26): dicono da chi si riceve posta. Quelli in chiaro di prima si leggono ancora.</remarks>
    [JsonConverter(typeof(ProtectedListConverter))] public List<string> ImageSenders { get; set; } = new();

    public bool ShowsImagesFrom(string email)
    {
        var address = email.Trim().ToLowerInvariant();
        int at = address.LastIndexOf('@');
        if (at < 0) return false;
        var host = address[(at + 1)..];
        return ImageSenders.Any(s => s == address || !s.Contains('@') && (host == s || host.EndsWith("." + s)));
    }

    public void AllowImagesFrom(string email)
    {
        var key = ImageKey(email);
        if (key.Length == 0) return;
        ImageSenders.RemoveAll(ProtectedTextConverter.IsUnreadable); // l'elenco di un altro PC che non si decifra: riparte da qui
        if (!ImageSenders.Contains(key)) ImageSenders.Add(key);
    }

    /// <summary>
    /// Le versioni precedenti ricordavano solo indirizzi interi: diventano domini dove serve. Il segnaposto di un elenco che
    /// non si decifra resta com'è, così al salvataggio torna nel file il cifrato di prima.
    /// </summary>
    public void UpgradeImageSenders() => ImageSenders = ImageSenders.Select(k => ProtectedTextConverter.IsUnreadable(k) ? k : ImageKey(k)).Where(k => k.Length > 0).Distinct().ToList();

    /// <summary>
    /// Per le impostazioni che arrivano da una copia di sicurezza (Backup): le voci diventano come le scrive ImageKey, ed
    /// escono quelle che "Mostra sempre le immagini" non scrive mai e che valgono per tutti i mittenti di un servizio: un
    /// servizio di posta personale intero ("gmail.com": lì si ricorda l'indirizzo) e un suffisso nazionale da solo
    /// ("co.uk"). Una copia preparata da altri con "gmail.com" faceva caricare da sole le immagini di chiunque scrivesse
    /// da Gmail (audit dell'11 ottobre, T-17). Quante ne sono uscite.
    /// </summary>
    internal int DropBroadImageSenders()
    {
        static bool Broad(string key)
        {
            if (key.Contains('@')) return false;
            var labels = key.Split('.');
            return labels.Length < 2 || PersonalMail.Contains(labels[0]) || labels.Length == 2 && labels[1].Length == 2 && SecondLevels.Contains(labels[0]);
        }
        var keys = ImageSenders.Select(ImageKey).Distinct().ToList();
        ImageSenders = keys.Where(k => k.Length > 0 && !Broad(k)).ToList();
        return keys.Count - ImageSenders.Count;
    }

    /// <summary>Servizi di posta personale: accettare un indirizzo non deve valere per tutti i loro utenti.</summary>
    static readonly HashSet<string> PersonalMail = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail", "googlemail", "outlook", "hotmail", "live", "msn", "passport", "yahoo", "ymail", "icloud", "me", "mac",
        "aol", "libero", "virgilio", "alice", "tim", "tin", "email", "tiscali", "fastwebnet", "inwind", "iol", "blu",
        "gmx", "web", "proton", "protonmail", "pm", "yandex", "mail", "zoho", "tutanota", "tuta", "pec", "legalmail",
        "postacert", "postecert", "aruba", "arubapec",
    };

    /// <summary>Secondi livelli dei domini nazionali (amazon.co.uk): l'organizzazione è il livello sotto.</summary>
    static readonly HashSet<string> SecondLevels = new(StringComparer.OrdinalIgnoreCase) { "co", "com", "net", "org", "gov", "ac", "edu", "ne", "or", "go" };

    /// <summary>Quello che si ricorda per un mittente: il dominio dell'organizzazione o, per la posta personale, l'indirizzo.</summary>
    public static string ImageKey(string email)
    {
        var address = email.Trim().ToLowerInvariant();
        int at = address.LastIndexOf('@');
        var host = at >= 0 ? address[(at + 1)..] : address;
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2) return at >= 0 ? address : "";
        int take = labels.Length > 2 && labels[^1].Length == 2 && SecondLevels.Contains(labels[^2]) ? 3 : 2;
        var organization = labels[^take..];
        if (at >= 0 && PersonalMail.Contains(organization[0])) return address;
        return string.Join('.', organization);
    }

    public List<string> CollapsedAccounts { get; set; } = new();
    /// <summary>Il gruppo "Tutti gli account" in cima alle cartelle è chiuso.</summary>
    public bool UnifiedCollapsed { get; set; }
    /// <summary>Il gruppo "Tutti gli account" in cima alle cartelle (Impostazioni, Generali, Aspetto): di base sì.</summary>
    public bool ShowUnified { get; set; } = true;

    // Calendario
    /// <summary>La vista del calendario: Day, WorkWeek, Week o Month.</summary>
    public string CalendarMode { get; set; } = "WorkWeek";
    /// <summary>Calendari nascosti nella vista, come "account|calendario": per Google l'identificativo è un indirizzo, anche di altre persone. Cifrati nel file (audit, blocco 10, S-26).</summary>
    [JsonConverter(typeof(ProtectedListConverter))] public List<string> HiddenCalendars { get; set; } = new();
    /// <summary>Ordine delle cartelle scelto trascinandole, per account (identificativi nell'ordine mostrato).</summary>
    public Dictionary<string, List<string>> FolderOrder { get; set; } = new();
    /// <summary>Cartelle con le sottocartelle chiuse, come "account|cartella".</summary>
    public List<string> CollapsedFolders { get; set; } = new();
    /// <summary>Cartelle nascoste dall'elenco (restano sul server), come "account|cartella".</summary>
    public List<string> HiddenFolders { get; set; } = new();

    /// <summary>Messaggi posposti, in attesa di tornare in Posta in arrivo.</summary>
    public List<SnoozedMessage> Snoozed { get; set; } = new();
    /// <summary>Contrassegni con scadenza o promemoria (Flags): le date di Gmail e IMAP e i promemoria di tutti.</summary>
    public List<FlagDate> Flags { get; set; } = new();
    /// <summary>Messaggi tornati da Posponi ("account|chiave" → ora del ritorno): nell'elenco valgono con quell'ora.</summary>
    public Dictionary<string, DateTime> Woken { get; set; } = new();

    /// <summary>Colori delle categorie scelti in Missiva ("account|nome" → colore 0-24): Gmail non li espone.</summary>
    public Dictionary<string, int> CategoryColors { get; set; } = new();
    /// <summary>Categorie Microsoft create in Missiva quando Graph non dà accesso all'elenco di Outlook, per account.</summary>
    public Dictionary<string, List<string>> LocalCategories { get; set; } = new();

    /// <summary>Menu del tasto destro sui messaggi: ordine delle voci ("-" è un separatore); null è quello di Outlook.</summary>
    public List<string>? MessageMenuOrder { get; set; }
    /// <summary>Voci del menu spente dall'utente; null sono quelle predefinite.</summary>
    public List<string>? MessageMenuHidden { get; set; }
    /// <summary>Menu del tasto destro sulle cartelle: ordine e voci spente, come per i messaggi.</summary>
    public List<string>? FolderMenuOrder { get; set; }
    public List<string>? FolderMenuHidden { get; set; }

    /// <summary>Preferiti, in cima all'elenco delle cartelle, come "account|cartella" nell'ordine scelto.</summary>
    public List<string> Favorites { get; set; } = new();
    public bool FavoritesCollapsed { get; set; }
    /// <summary>Colore dell'icona delle cartelle ("account|cartella" → colore 0-24), come Cambia colore in Outlook.</summary>
    public Dictionary<string, int> FolderColors { get; set; } = new();
    /// <summary>Messaggi fissati, per account. Solo in Missiva: Microsoft Graph non espone il "fissato" di Outlook.</summary>
    public Dictionary<string, List<string>> Pinned { get; set; } = new();
    /// <summary>Firme, per account (come Impostazioni, Account, Firme nel nuovo Outlook).</summary>
    public Dictionary<string, Signature> Signatures { get; set; } = new();

    /// <summary>La firma da mettere in un messaggio nuovo o in una risposta (o inoltro) di quell'account, o null.</summary>
    public Signature? SignatureFor(MailAccount account, bool reply) =>
        Signatures.TryGetValue(account.Id, out var s) && s.RtfFor(reply).Length > 0 ? s : null;

    /// <summary>La firma di un altro indirizzo dell'account ("account|identità"), se ne ha una; altrimenti quella dell'account.</summary>
    public Signature? SignatureFor(MailAccount account, bool reply, SendIdentity? identity) =>
        identity != null && Signatures.TryGetValue(account.Id + "|" + identity.Id, out var s) && s.All().Any(x => x.Rtf.Length > 0)
            ? s.RtfFor(reply).Length > 0 ? s : null
            : SignatureFor(account, reply);

    /// <summary>Le firme di chi scrive (l'altro indirizzo se ne ha, altrimenti l'account), per il pulsante Firma; null se nessuna.</summary>
    public Signature? SignaturesOf(MailAccount account, SendIdentity? identity) =>
        identity != null && Signatures.TryGetValue(account.Id + "|" + identity.Id, out var own) && own.All().Any(x => x.Rtf.Length > 0) ? own
        : Signatures.TryGetValue(account.Id, out var s) && s.All().Any(x => x.Rtf.Length > 0) ? s : null;

    /// <summary>Evidenziata e Altra: solo nella casella degli account Microsoft, non nelle caselle condivise.</summary>
    public bool IsFocused(MailAccount account) =>
        account.Kind == AccountKind.Microsoft && account.SharedOf == null && Focused.GetValueOrDefault(account.Id, true);

    public bool IsPinned(MailAccount account, string messageId) =>
        Pinned.TryGetValue(account.Id, out var list) && list.Contains(messageId);

    public void SetPinned(MailAccount account, string messageId, bool pinned)
    {
        if (!Pinned.TryGetValue(account.Id, out var list)) Pinned[account.Id] = list = new();
        list.Remove(messageId);
        if (pinned) list.Add(messageId);
    }

    /// <summary>
    /// Un account rimosso: esce tutto quello che le impostazioni tengono per lui, non solo l'account. Contrassegni, modelli,
    /// posposti e invii pianificati hanno oggetti, mittenti e testi dei clienti (IBAN, prezzi); firme, cartelle, categorie e
    /// calendari i nomi. I modelli di tutti gli account (AccountId null) restano.
    /// </summary>
    public void ForgetAccount(string id)
    {
        bool Mine(string key) => key == id || key.StartsWith(id + "|", StringComparison.Ordinal); // "account" o "account|..."
        void Drop<T>(Dictionary<string, T> map)
        {
            foreach (var key in map.Keys.Where(Mine).ToList()) map.Remove(key);
        }
        Flags.RemoveAll(f => f.AccountId == id);
        Templates.RemoveAll(t => t.AccountId == id);
        Rules.RemoveAll(r => r.AccountId == id);
        Snoozed.RemoveAll(s => s.AccountId == id);
        ScheduledSends.RemoveAll(s => s.AccountId == id);
        Drop(Woken);
        Drop(Signatures);
        Drop(CategoryColors);
        Drop(FolderColors);
        Drop(LocalCategories);
        Drop(FolderOrder);
        Drop(Focused);
        Drop(Pinned);
        NotificationsOff.RemoveAll(Mine);
        CollapsedAccounts.RemoveAll(Mine);
        HiddenCalendars.RemoveAll(Mine);
        CollapsedFolders.RemoveAll(Mine);
        HiddenFolders.RemoveAll(Mine);
        Favorites.RemoveAll(Mine);
    }
}

/// <summary>
/// Una firma: il testo formattato come lo salva l'editor (RTF, che l'editor di WPF rilegge senza eseguire nulla) e dove
/// metterla. Parte con l'HTML del messaggio (FlowToHtml).
/// </summary>
public sealed class Signature
{
    /// <summary>
    /// Il testo della firma com'è nel file (audit, blocco 10, S-26): cifrato con DPAPI come i modelli (nome, telefono, a volte
    /// IBAN); una firma scritta in chiaro dalle versioni di prima si legge ancora e al salvataggio dopo si cifra. Se su questo
    /// PC il cifrato non si apre (impostazioni di un altro utente di Windows) qui c'è il segnaposto, che al salvataggio torna
    /// il cifrato com'era: per l'app usa <see cref="Rtf"/>, che in quel caso è vuoto.
    /// </summary>
    [JsonPropertyName("Rtf"), JsonConverter(typeof(ProtectedTextConverter))] public string StoredRtf { get; set; } = "";
    /// <summary>Il testo della firma; vuoto se non c'è o se non si decifra più (il segnaposto non va mai in un messaggio).</summary>
    [JsonIgnore] public string Rtf { get => ProtectedTextConverter.IsUnreadable(StoredRtf) ? "" : StoredRtf; set => StoredRtf = value; }
    public bool NewMessages { get; set; } = true;
    public bool Replies { get; set; } = true;
    /// <summary>
    /// Le altre firme dello stesso indirizzo, come le firme con nome del nuovo Outlook (una breve per le risposte, una in
    /// inglese); quella sopra è la principale. Il pulsante Firma della scrittura le propone tutte.
    /// </summary>
    public List<NamedSignature> More { get; set; } = new();
    /// <summary>La firma dei nuovi messaggi e quella delle risposte e degli inoltri: vuoto la principale, altrimenti l'Id di una di More.</summary>
    public string NewId { get; set; } = "";
    public string ReplyId { get; set; } = "";

    /// <summary>Il testo della firma che va nei nuovi messaggi o nelle risposte; vuoto se non ce n'è.</summary>
    public string RtfFor(bool reply)
    {
        if (!(reply ? Replies : NewMessages)) return "";
        var id = reply ? ReplyId : NewId;
        return id.Length == 0 ? Rtf : More.FirstOrDefault(m => m.Id == id)?.Rtf ?? Rtf;
    }

    /// <summary>Tutte le firme con il loro nome, la principale per prima.</summary>
    public IEnumerable<NamedSignature> All() => More.Prepend(new NamedSignature { Id = "", Name = "Firma principale", Rtf = Rtf });

    /// <summary>Fuori i testi che su questo PC non si decifrano (per la copia di sicurezza: il segnaposto non deve entrarci); quanti.</summary>
    internal int DropUnreadable()
    {
        int dropped = 0;
        if (ProtectedTextConverter.IsUnreadable(StoredRtf)) { StoredRtf = ""; dropped++; }
        foreach (var named in More.Where(m => ProtectedTextConverter.IsUnreadable(m.StoredRtf))) { named.StoredRtf = ""; dropped++; }
        return dropped;
    }
}

/// <summary>Un'altra firma di un indirizzo, con un nome (Breve, Inglese...): il testo RTF come la principale.</summary>
public sealed class NamedSignature
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Come <see cref="Signature.StoredRtf"/>: cifrato nel file, il segnaposto se non si decifra.</summary>
    [JsonPropertyName("Rtf"), JsonConverter(typeof(ProtectedTextConverter))] public string StoredRtf { get; set; } = "";
    [JsonIgnore] public string Rtf { get => ProtectedTextConverter.IsUnreadable(StoredRtf) ? "" : StoredRtf; set => StoredRtf = value; }
}

/// <summary>Un invio pianificato fatto da Missiva: l'account, la bozza (StableId: X-GM-MSGID o Message-Id) e quando partire.</summary>
public sealed class ScheduledSend
{
    public string AccountId { get; set; } = "";
    public string DraftKey { get; set; } = "";
    public DateTime At { get; set; }
    [JsonConverter(typeof(ProtectedTextConverter))] public string Subject { get; set; } = "";
}

/// <summary>
/// Un modello di risposta: nome, oggetto facoltativo e testo (RTF dell'editor delle firme), per tutti gli account (AccountId
/// null) o per uno. Nome, oggetto e testo cifrati con DPAPI in impostazioni.json: ci stanno IBAN, prezzi, dati dei clienti.
/// </summary>
public sealed class MailTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? AccountId { get; set; }
    [JsonConverter(typeof(ProtectedTextConverter))] public string Name { get; set; } = "";
    [JsonConverter(typeof(ProtectedTextConverter))] public string Subject { get; set; } = "";
    [JsonConverter(typeof(ProtectedTextConverter))] public string Rtf { get; set; } = "";
}

/// <summary>
/// Un messaggio contrassegnato con una scadenza o un promemoria: come ritrovarlo (la chiave che non cambia, e l'id e la
/// cartella per agire dalla notifica del promemoria), inizio, scadenza e promemoria. Oggetto e mittente cifrati.
/// </summary>
public sealed class FlagDate
{
    public string AccountId { get; set; } = "";
    public string Key { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string FolderId { get; set; } = "";
    public DateTime? Start { get; set; }
    public DateTime? Due { get; set; }
    public DateTime? Reminder { get; set; }
    [JsonConverter(typeof(ProtectedTextConverter))] public string Subject { get; set; } = "";
    [JsonConverter(typeof(ProtectedTextConverter))] public string From { get; set; } = "";
}

/// <summary>Un messaggio posposto: dove sta, come ritrovarlo e quando deve tornare.</summary>
public sealed class SnoozedMessage
{
    public string AccountId { get; set; } = "";
    public string Key { get; set; } = "";
    public string FolderId { get; set; } = "";
    public DateTime Until { get; set; }
    [JsonConverter(typeof(ProtectedTextConverter))] public string Subject { get; set; } = "";
    [JsonConverter(typeof(ProtectedTextConverter))] public string From { get; set; } = "";
}

/// <summary>
/// I testi delle impostazioni che dicono di che cosa parla un messaggio (oggetto e mittente di Posposti e invii
/// pianificati): in impostazioni.json cifrati con DPAPI come i token ("dpapi:..."), come la copia locale. Quelli scritti in
/// chiaro dalle versioni di prima si leggono ancora e al prossimo salvataggio si cifrano. Lo stesso testo dà lo stesso
/// cifrato finché resta nelle impostazioni: il file non cambia a ogni salvataggio. Dopo ogni Store.Save restano in memoria
/// solo i testi di quel salvataggio (KeepOnlyWritten): la pagina Modelli salva mezzo secondo dopo l'ultima lettera, e ogni
/// versione del testo di un modello (con un logo 100-200 KB di RTF) restava due volte, testo e cifrato, fino alla chiusura.
/// </summary>
public sealed class ProtectedTextConverter : JsonConverter<string>
{
    const string Prefix = "dpapi:";
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> known = new();
    /// <summary>I testi scritti dall'ultimo KeepOnlyWritten.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> written = new();

    /// <summary>Dopo un salvataggio: via i testi che non ci sono più (un modello cambiato, un posposto tornato).</summary>
    internal static void KeepOnlyWritten()
    {
        foreach (var text in known.Keys)
            if (!written.ContainsKey(text)) known.TryRemove(text, out _);
        written.Clear();
    }

#if DEBUG
    /// <summary>Prove: quanti testi sono in memoria.</summary>
    internal static int KnownCount => known.Count;

    /// <summary>Prove: come un processo appena avviato (un altro PC, Store.TestEntropy): nessun testo né cifrato ricordato.</summary>
    internal static void TestForget()
    {
        known.Clear();
        written.Clear();
        lock (unreadable) unreadable.Clear();
    }
#endif

    /// <summary>
    /// Vero solo dentro Backup (copia di sicurezza), sul suo thread: i testi si scrivono e si leggono in chiaro, senza DPAPI,
    /// perché il file della copia ha la sua cifratura e deve aprirsi anche su un altro PC, dove DPAPI non li leggerebbe.
    /// </summary>
    [ThreadStatic] internal static bool Portable;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Portable ? reader.GetString() ?? "" : Open(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(Portable ? value : Seal(value));

    /// <summary>Il testo in chiaro di un valore letto dal file: cifrato ("dpapi:...") o, dalle versioni di prima, in chiaro.</summary>
    internal static string Open(string text)
    {
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return text;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var plain = Store.Unprotect(text[Prefix.Length..]);
            known.TryAdd(plain, text);
            return plain;
        }
        catch
        {
            // Cifrato da un altro utente di Windows (profilo cambiato, impostazioni copiate da un altro PC) o rovinato. Prima
            // diventava vuoto in silenzio e al salvataggio dopo il cifrato si perdeva; una regola restava senza condizioni
            // e valeva per ogni messaggio (audit, F-44 punto 4). Ora al suo posto c'è un testo che lo dice, lo stesso per lo
            // stesso cifrato, e Seal rimette nel file il cifrato com'era: tornando al profilo di prima si rilegge.
            // Il riferimento viene dal cifrato (non un contatore): lo stesso a ogni avvio, e due cifrati diversi non si confondono.
            var placeholder = $"(testo cifrato non leggibile su questo PC, rif. {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..6]})";
            lock (unreadable) unreadable[placeholder] = text;
            return placeholder;
        }
        finally
        {
            Interlocked.Increment(ref opened);
            Interlocked.Add(ref openTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started);
        }
    }

    static long opened, openTicks;

    /// <summary>Quanti testi cifrati sono stati decifrati finora e in quanto tempo (DPAPI): Store.Load lo scrive nei tempi dell'avvio.</summary>
    internal static (long Count, double Milliseconds) OpenCost => (Interlocked.Read(ref opened), Interlocked.Read(ref openTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

    /// <summary>I testi messi al posto dei cifrati che non si decifrano, con il loro cifrato.</summary>
    static readonly Dictionary<string, string> unreadable = new();

    /// <summary>Quanti cifrati diversi delle impostazioni non si sono decifrati in questa sessione: App lo dice all'avvio.</summary>
    internal static int UnreadableCount { get { lock (unreadable) return unreadable.Count; } }

    /// <summary>Il testo è quello messo al posto di un cifrato che non si decifra (o lo contiene: gli elenchi delle regole).</summary>
    internal static bool IsUnreadable(string value)
    {
        if (!value.StartsWith("(testo cifrato non leggibile", StringComparison.Ordinal)) return false;
        lock (unreadable) return unreadable.ContainsKey(value);
    }

    /// <summary>Il valore da scrivere nel file: il testo cifrato, lo stesso finché il testo resta nelle impostazioni.</summary>
    internal static string Seal(string value)
    {
        if (value.Length == 0) return "";
        if (value[0] == '(')
            lock (unreadable)
                if (unreadable.TryGetValue(value, out var original)) return original; // non decifrato: torna nel file com'era
        written.TryAdd(value, 0);
        return known.GetOrAdd(value, plain => Prefix + Store.Protect(plain));
    }
}

/// <summary>
/// Un elenco di testi delle impostazioni cifrato come ProtectedTextConverter, tutto in un valore solo ("dpapi:...", i testi
/// uno per riga): le parole e gli indirizzi delle regole di Missiva (mittenti, oggetti, testi cercati) dicono con chi e di
/// che cosa si lavora. Un elenco scritto in chiaro (un array) si legge ancora.
/// </summary>
public sealed class ProtectedListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<string>();
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                if (reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: > 0 } item) list.Add(item);
            return list;
        }
        if (reader.TokenType != JsonTokenType.String) return list;
        // Nella copia di sicurezza (Portable) nulla passa da DPAPI: lì gli elenchi sono in chiaro, e un valore "dpapi:..."
        // scritto a mano in una copia resta il testo che è. Prima si decifrava: un cifrato di questo PC messo nella copia da
        // chi lo aveva visto (una password dentro un impostazioni.json mandato in giro) tornava in chiaro fra le parole o le
        // categorie di una regola (audit dell'11 ottobre, T-22).
        var text = reader.GetString() ?? "";
        list.AddRange((ProtectedTextConverter.Portable ? text : ProtectedTextConverter.Open(text)).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        if (ProtectedTextConverter.Portable)
        {
            // La copia di sicurezza (Backup): un elenco in chiaro, che Read legge già, come quelli delle versioni di prima.
            writer.WriteStartArray();
            foreach (var v in value.Where(v => v.Length > 0)) writer.WriteStringValue(v.Replace('\n', ' '));
            writer.WriteEndArray();
            return;
        }
        writer.WriteStringValue(ProtectedTextConverter.Seal(string.Join('\n', value.Where(v => v.Length > 0).Select(v => v.Replace('\n', ' ')))));
    }
}

/// <summary>
/// Il JSON delle impostazioni scritto dal compilatore (generatore di System.Text.Json), lo stesso di prima: all'avvio la
/// lettura costava 80-100 ms, quasi tutti per preparare il serializzatore per riflessione.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
sealed partial class SettingsJson : JsonSerializerContext;

/// <summary>
/// Scrittura dei file dei dati che non lascia mai un file a metà: un file temporaneo accanto, svuotato fino al disco
/// (Flush(true): senza, dopo un'interruzione di corrente NTFS può tenere la rinomina e non i dati, e il file vero resta
/// di zeri), poi la rinomina sopra quello vero. Per impostazioni, contatti, coda delle azioni e rubrica (audit, F-44).
/// </summary>
public static class SafeFile
{
    /// <param name="keepOldAs">Dove tenere il file di prima (la copia di riserva); null per non tenerlo.</param>
    public static void Write(string path, byte[] bytes, string? keepOldAs = null)
    {
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        if (keepOldAs != null && File.Exists(path))
        {
            try
            {
                File.Replace(tmp, path, keepOldAs, ignoreMetadataErrors: true); // il nuovo al posto del vecchio, il vecchio come copia
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // La copia di riserva non si lascia sostituire (in uso): il file nuovo va comunque al suo posto, qui sotto.
            }
        }
        // Un antivirus o l'indicizzazione che tengono il file aperto per un attimo: qualche tentativo prima di arrendersi.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(tmp, path, true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < 3 && !Directory.Exists(path))
                {
                    Thread.Sleep(50);
                    continue;
                }
                try { File.Delete(tmp); } catch { }
                throw;
            }
        }
    }

    /// <summary>
    /// Per le copie che si rifanno (gli elenchi e i corpi della copia locale, il calendario, i token di accesso, i loghi):
    /// un file temporaneo accanto e la rinomina sopra quello vero, senza svuotare fino al disco. Chi legge non trova mai un
    /// file scritto a metà; dopo un'interruzione di corrente il file può mancare o non valere, e si rifà. Lo svuotamento di
    /// Write costa, e i corpi sul disco si scrivono a centinaia. Erano le stesse due righe scritte in cinque punti (audit
    /// dell'11 ottobre, C-17); il nome del file temporaneo resta quello di prima, il nome del file più ".tmp".
    /// </summary>
    public static void WriteCopy(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, true);
    }

    /// <summary>
    /// Un file intero di questo PC cifrato per l'utente di Windows (DPAPI): elenchi, corpi, calendario, contatti, rubrica,
    /// coda delle azioni, token di accesso. Nel file ci sono questi byte e nient'altro; era la stessa chiamata scritta in
    /// ventuno punti di sette file (C-17). Senza l'entropia delle prove di Store.ProtectBytes, che vale per i testi
    /// cifrati dentro le impostazioni e per quello che la copia di sicurezza legge e scrive: questi file valgono sempre
    /// per l'utente di Windows che li ha scritti, anche dentro una prova che fa da altro PC.
    /// </summary>
    public static byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);

    /// <summary>Il contenuto di un file cifrato con Protect; solleva CryptographicException se non è di questo utente di Windows.</summary>
    public static byte[] Unprotect(byte[] sealedBytes) => ProtectedData.Unprotect(sealedBytes, null, DataProtectionScope.CurrentUser);

    /// <summary>
    /// Un file che non si legge messo da parte accanto, con "illeggibile" e la data nel nome: mai cancellato e mai
    /// sovrascritto, perché chi aiuta possa recuperarlo. Il nome nuovo, o null se non si riesce a spostarlo (in uso).
    /// Con un'altra etichetta ("prima-dell-importazione") lo stesso per i file sostituiti da una copia di sicurezza (Backup).
    /// </summary>
    public static string? SetAside(string path, string label = "illeggibile")
    {
        try
        {
            if (!File.Exists(path)) return null;
            var stem = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}-{label}-{DateTime.Now:yyyyMMdd-HHmmss}");
            for (int n = 1; n < 100; n++)
            {
                var target = stem + (n == 1 ? "" : "-" + n) + Path.GetExtension(path);
                if (File.Exists(target)) continue;
                File.Move(path, target);
                return target;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"file non messo da parte ({Path.GetFileName(path)}, {label})", ex);
        }
        return null;
    }
}

/// <summary>
/// impostazioni.json non si leggeva all'avvio: perché, dove è finito il file rotto, da che copia si è ripartiti (null: da
/// zero, senza account) e se i salvataggi sono fermi (il file non si è potuto spostare). Heading e Body sono l'avviso.
/// </summary>
public sealed record StoreRecovery(string Why, string? BrokenFile, DateTime? BackupDate, string? BrokenBackup, bool Blocked, string Location)
{
    public string Heading => BrokenFile == null && !Blocked ? "Le impostazioni di Missiva non c'erano più" : "Le impostazioni di Missiva non si leggevano";

    public string Body
    {
        get
        {
            var what = Blocked ? $"Il file delle impostazioni non si riesce a leggere né a spostare, forse perché è in uso da un altro programma ({Why})."
                : BrokenFile == null ? "Il file delle impostazioni non c'era più."
                : $"Il file delle impostazioni era rovinato ({Why}).";
            var restart = BackupDate is { } date
                ? $"Missiva è ripartita dalla copia di riserva del {date.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("it-IT"))} alle {date:HH:mm}: account e accessi ci sono, le modifiche fatte dopo possono mancare."
                : "Non c'è una copia di riserva che si legga: Missiva è ripartita senza account. Aggiungili di nuovo da Impostazioni, Account.";
            var where = Blocked
                ? $"Per non sovrascriverlo, in questa sessione Missiva non salva le impostazioni. Chiudi Missiva e riaprila; se succede ancora, il file è:\n{Location}"
                : BrokenFile != null ? $"Il file rovinato non è stato cancellato: è stato messo da parte e Missiva non lo tocca più.\n{BrokenFile}"
                : "";
            if (BrokenBackup != null) where += $"\nAnche la copia di riserva non si leggeva, ed è da parte:\n{BrokenBackup}";
            return $"{what} {restart}{(where.Length > 0 ? "\n\n" + where : "")}";
        }
    }
}

/// <summary>Impostazioni e account in %LOCALAPPDATA%\Missiva.</summary>
public static class Store
{
    public static readonly string Dir =
#if DEBUG || PROVA
        // Prove: con MISSIVA_DATA la versione di prova lavora su una copia dei dati (strumenti\prova.ps1), non su quelli veri.
        Environment.GetEnvironmentVariable("MISSIVA_DATA") is { Length: > 0 } sandbox ? sandbox :
#endif
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Missiva");
    internal const string FileName = "impostazioni.json", BackupName = "impostazioni.bak";

#if DEBUG
    /// <summary>Prove: la copia locale (cache) sotto un'altra cartella dei dati, temporanea, per MailCache, BodyCache, Calendars e ContactBook.</summary>
    internal static string? TestDataRoot;
    internal static string DataRoot => TestDataRoot ?? Dir;
#else
    internal static string DataRoot => Dir;
#endif

    /// <summary>
    /// Il nome sul disco di quello che la copia locale tiene per un account (cache\nome, cache\calendario-nome.json,
    /// cache\contatti-nome.dat): il suo identificativo, se è fatto come quelli di Missiva (MailAccount.ValidId); altrimenti
    /// un'impronta dell'identificativo, che è sempre un nome solo. Così un identificativo con "..", una barra o un percorso
    /// intero non diventa mai un pezzo di percorso, da qualunque file arrivi (audit dell'11 ottobre, T-14).
    /// </summary>
    internal static string CacheName(string? accountId) =>
        MailAccount.ValidId(accountId) ? accountId : "id-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId ?? "")))[..32].ToLowerInvariant();

    /// <summary>Un file o una cartella della copia locale: cache\nome nella cartella dei dati.</summary>
    internal static string CachePath(string name) => Path.Combine(DataRoot, "cache", name);

    /// <summary>
    /// Vero se il percorso, risolto ("..", barre, lettere di unità), è dentro la cartella e non è la cartella stessa. Chi
    /// cancella una cartella intera lo chiede prima (MailCache.Delete, BodyCache.Delete): mai nulla fuori dalla copia locale.
    /// </summary>
    internal static bool IsInside(string path, string folder)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.Length > root.Length && full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static Settings Settings { get; private set; } = new();

    /// <summary>Che cosa è successo all'ultima lettura, se impostazioni.json non si leggeva; null se è andata bene. Lo dice App all'avvio.</summary>
    public static StoreRecovery? Recovery { get; private set; }

    /// <summary>Un salvataggio non riuscito (disco pieno, file bloccato), una volta per sessione e da qualunque thread: App lo dice.</summary>
    public static event Action<Exception>? SaveFailed;

    public static void Load()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Settings = Read(Dir, out var recovery);
        Recovery = recovery;
        var (texts, dpapi) = ProtectedTextConverter.OpenCost;
        Log.Time($"impostazioni lette ({texts} testi decifrati con DPAPI in {dpapi:0.0} ms; {Settings.Flags.Count} contrassegni, {Settings.Snoozed.Count} posposti, {Settings.Rules.Count} regole, {Settings.Templates.Count} modelli)", clock);
        if (recovery != null) Log.Write($"impostazioni non lette ({recovery.Why}): {recovery.Body.Replace("\n\n", " ").Replace('\n', ' ')}");
        Settings.UpgradeImageSenders();
    }

    /// <summary>
    /// Le impostazioni della cartella. Un file che non si legge (vuoto, troncato, di zeri dopo un'interruzione di corrente,
    /// scritto da una versione più nuova) prima diventava "nessuna impostazione" in silenzio, e il primo salvataggio lo
    /// sovrascriveva: account, accessi, firme e regole persi senza una copia (audit, F-44 punto 1). Ora il file rotto si mette
    /// da parte con la data nel nome e non si tocca più, e si riparte da impostazioni.bak (il salvataggio precedente, tenuto
    /// da TryWrite); se nemmeno quella si legge va da parte anche lei e si riparte senza account. Un file che non si riesce
    /// né a leggere né a spostare (in uso da un altro programma) blocca i salvataggi della sessione: mai sovrascritto.
    /// </summary>
    internal static Settings Read(string dir, out StoreRecovery? recovery)
    {
        recovery = null;
        string file = Path.Combine(dir, FileName), backup = Path.Combine(dir, BackupName);
        bool hasFile = File.Exists(file), hasBackup = File.Exists(backup);
        if (!hasFile && !hasBackup) return new(); // primo avvio
        var why = "il file non c'era più";
        if (hasFile)
        {
            if (TryParse(file, out var settings, out why))
            {
                // La prima volta dopo l'aggiornamento: la copia di riserva nasce da un file appena letto bene.
                if (!hasBackup) try { File.Copy(file, backup); } catch (Exception ex) { Log.Error("copia di riserva delle impostazioni", ex); }
                return settings;
            }
        }
        var aside = hasFile ? SafeFile.SetAside(file) : null;
        bool blocked = hasFile && aside == null;
        if (hasBackup && TryParse(backup, out var saved, out _))
        {
            recovery = new StoreRecovery(why, aside, File.GetLastWriteTime(backup), null, blocked, file);
            // Il file torna al suo posto subito: senza, un avvio senza salvataggi darebbe lo stesso avviso la volta dopo.
            if (!blocked) try { File.Copy(backup, file); } catch (Exception ex) { Log.Error("impostazioni rimesse dalla copia di riserva", ex); }
            return saved;
        }
        recovery = new StoreRecovery(why, aside, null, hasBackup ? SafeFile.SetAside(backup) : null, blocked, file);
        return new();
    }

    /// <summary>Il file si legge ed è un JSON di impostazioni. Un file in uso da un altro programma si riprova per mezzo secondo.</summary>
    internal static bool TryParse(string path, out Settings settings, out string why)
    {
        settings = new();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.Settings) is not { } parsed)
                {
                    why = "il file non contiene impostazioni";
                    return false;
                }
                // Un file cambiato a mano: un identificativo di account che non è di Missiva ("..\..\cartella", un percorso)
                // non entra, come un file rovinato (audit dell'11 ottobre, T-14).
                if (MailAccount.Problem(parsed.Accounts) is { } problem)
                {
                    why = problem;
                    return false;
                }
                settings = parsed;
                why = "";
                return true;
            }
            catch (IOException) when (attempt < 3 && File.Exists(path))
            {
                Thread.Sleep(150);
            }
            catch (Exception ex)
            {
                why = ex is JsonException ? "il contenuto non è valido" : ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// Scrive le impostazioni nella cartella senza mai lasciare un file a metà (SafeFile.Write) e tenendo il file di prima
    /// come impostazioni.bak. Non solleva eccezioni: false e il motivo. Con una lettura bloccata (recovery.Blocked) non scrive.
    /// </summary>
    internal static bool TryWrite(string dir, Settings settings, StoreRecovery? recovery, out Exception? error)
    {
        if (recovery is { Blocked: true })
        {
            error = new IOException("impostazioni.json non si leggeva né si spostava all'avvio: in questa sessione non si sovrascrive");
            return false;
        }
        return Snapshot(settings, out error) is { } json && TryWrite(dir, json, recovery, out error);
    }

    /// <summary>Le impostazioni come vanno nel file (l'istantanea), sul thread di chi chiama. Non solleva eccezioni: null e il motivo.</summary>
    internal static byte[]? Snapshot(Settings settings, out Exception? error)
    {
        error = null;
        try
        {
            try
            {
                return JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJson.Default.Settings);
            }
            catch (InvalidOperationException)
            {
                // Un elenco cambiato dal thread dell'interfaccia proprio durante la scrittura da un altro thread: una seconda volta.
                return JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJson.Default.Settings);
            }
        }
        catch (Exception ex)
        {
            error = ex;
            return null;
        }
    }

    /// <summary>Un'istantanea già presa (Snapshot) nel file della cartella, con le garanzie di TryWrite.</summary>
    internal static bool TryWrite(string dir, byte[] json, StoreRecovery? recovery, out Exception? error)
    {
        error = null;
        if (recovery is { Blocked: true })
        {
            error = new IOException("impostazioni.json non si leggeva né si spostava all'avvio: in questa sessione non si sovrascrive");
            return false;
        }
        try
        {
            Directory.CreateDirectory(dir);
            SafeFile.Write(Path.Combine(dir, FileName), json, keepOldAs: Path.Combine(dir, BackupName));
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    static readonly SettingsSaver saver = NewSaver();

    static SettingsSaver NewSaver()
    {
        var made = new SettingsSaver(Dir, () => Settings, () => Recovery);
        made.Failed += error =>
        {
            Log.Error("impostazioni non salvate (non lo riscrivo in questa sessione)", error);
            try { SaveFailed?.Invoke(error); } catch (Exception ex) { Log.Error("avviso delle impostazioni non salvate", ex); }
        };
        return made;
    }

    /// <summary>
    /// Salva le impostazioni subito, sul thread di chi chiama: al ritorno sono sul disco. Per quello che non si può
    /// rischiare (account, accessi, importazione, contrassegni e posposti, uscita). Prima un errore (disco pieno, file
    /// bloccato da un antivirus) era un'eccezione in chi chiamava: sul thread dell'interfaccia finiva solo nel registro e
    /// il resto del gestore non girava. Ora false, il registro e, una volta per sessione, SaveFailed (audit, F-44 punto 5).
    /// </summary>
    public static bool Save() => saver.Now();

    /// <summary>
    /// Salva le impostazioni fra poco (SettingsSaver.Soon), per i cambi piccoli e frequenti che ridisegnano e basta: gruppi
    /// aperti o chiusi, zoom, ricerche recenti, messaggi già visti dalle regole, firme e modelli mentre si scrivono.
    /// </summary>
    public static void SaveSoon() => saver.Soon();

    /// <summary>
    /// Quello che SaveSoon non ha ancora portato sul disco ci va adesso: uscita, fine della sessione di Windows, finestra
    /// chiusa nell'area di notifica. Non fa nulla se non c'è niente in attesa.
    /// </summary>
    public static void Flush() => saver.Flush();

    /// <summary>
    /// App all'avvio: da qui SaveSoon accorpa sul thread dell'interfaccia. Senza (prove, strumenti da riga di comando)
    /// SaveSoon salva subito. All'uscita del processo, comunque sia chiesta, quello che resta in attesa si scrive.
    /// </summary>
    internal static void SaveSoonOn(System.Windows.Threading.Dispatcher ui)
    {
        saver.Ui = ui;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => saver.Flush();
    }

#if DEBUG
    /// <summary>
    /// Prove della copia di sicurezza: un'entropia in più per DPAPI, solo sul thread della prova, fa da "altro PC". Quello che
    /// è cifrato con un'entropia non si decifra con un'altra, come i cifrati di un altro utente di Windows.
    /// </summary>
    [ThreadStatic] internal static byte[]? TestEntropy;
    static byte[]? Entropy => TestEntropy;
#else
    const byte[]? Entropy = null;
#endif

    public static string Protect(string secret) => Convert.ToBase64String(ProtectBytes(Encoding.UTF8.GetBytes(secret)));

    public static string Unprotect(string protectedSecret) => Encoding.UTF8.GetString(UnprotectBytes(Convert.FromBase64String(protectedSecret)));

    /// <summary>DPAPI per l'utente di Windows, sui byte: i file interi (contatti.dat, rubrica.json) che la copia di sicurezza legge e scrive.</summary>
    public static byte[] ProtectBytes(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public static byte[] UnprotectBytes(byte[] sealedBytes) => ProtectedData.Unprotect(sealedBytes, Entropy, DataProtectionScope.CurrentUser);

    /// <summary>
    /// Come Unprotect, ma un segreto che non si decifra più (cifrato da un altro utente di Windows: profilo cambiato o
    /// impostazioni copiate da un altro PC; o rovinato) dà false invece di un'eccezione di Windows ("Dati non validi").
    /// </summary>
    public static bool TryUnprotect(string? protectedSecret, out string secret)
    {
        secret = "";
        if (protectedSecret == null) return false;
        try
        {
            secret = Unprotect(protectedSecret);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// I salvataggi delle impostazioni di una cartella (audit, v2 V-10 e v1 V-21). Prima ogni piccolo cambio (un gruppo aperto
/// o chiuso, una tacca di zoom, una ricerca) serializzava tutto e scriveva il file sul thread dell'interfaccia, con lo
/// svuotamento su disco: 2,6-3,2 ms l'uno. Now fa ancora così, per quello che non si può rischiare. Soon accorpa: dopo
/// l'ultima richiesta aspetta Delay (al più MaxWait dalla prima), prende l'istantanea sul thread dell'interfaccia, dove le
/// impostazioni cambiano, e la scrive su un thread del pool. Le istantanee sono numerate: sul file arriva sempre l'ultima,
/// mai una più vecchia sopra una più nuova. Flush porta subito sul file quello che è ancora in attesa.
/// </summary>
internal sealed class SettingsSaver(string dir, Func<Settings> settings, Func<StoreRecovery?> recovery)
{
    internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);
    const int MaxWait = 2000;

    readonly object taking = new();  // un'istantanea alla volta: il suo numero e i testi cifrati tenuti (KeepOnlyWritten) vanno insieme
    readonly object writing = new(); // un solo scrittore del file
    long taken;                      // numero dell'ultima istantanea presa
    long onDisk;                     // numero dell'ultima arrivata sul file
    volatile bool soon;              // una richiesta di Soon che nessuna istantanea ha ancora preso
    long firstAsked;                 // quando è partita l'attesa in corso (TickCount64)
    System.Windows.Threading.DispatcherTimer? timer;
    bool failureTold;

    /// <summary>Il thread dell'interfaccia. Senza (prove, strumenti da riga di comando) Soon salva subito.</summary>
    internal System.Windows.Threading.Dispatcher? Ui;

    /// <summary>Un salvataggio non riuscito, una volta per sessione e da qualunque thread.</summary>
    internal event Action<Exception>? Failed;

    /// <summary>Quante volte il file è stato scritto, e quante istantanee sono state saltate perché ne era già arrivata una più nuova.</summary>
    internal int Writes, Skipped;

    /// <summary>Il thread dell'ultima scrittura del file (prove).</summary>
    internal int LastWriteThread;

    /// <summary>Subito, sul thread di chi chiama: al ritorno le impostazioni di adesso (o più nuove) sono sul disco.</summary>
    public bool Now()
    {
        soon = false; // prima dell'istantanea: comprende anche quello che Soon aspettava di salvare
        var (json, number, error) = Take();
        return Put(json, number, error, "subito");
    }

    public void Soon()
    {
        var ui = Ui;
        if (ui == null || ui.HasShutdownStarted)
        {
            Now();
            return;
        }
        soon = true;
        if (ui.CheckAccess()) Arm(ui);
        else ui.BeginInvoke(() => Arm(ui));
    }

    public void Flush()
    {
        if (soon || Interlocked.Read(ref onDisk) != Interlocked.Read(ref taken)) Now();
    }

    void Arm(System.Windows.Threading.Dispatcher ui)
    {
        if (timer == null)
        {
            // A priorità Background: l'istantanea si prende quando l'interfaccia non ha tasti, clic o disegni in attesa.
            timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, ui) { Interval = Delay };
            timer.Tick += (_, _) => OnTick();
        }
        if (!timer.IsEnabled) firstAsked = Environment.TickCount64;
        else if (Environment.TickCount64 - firstAsked > MaxWait) return; // richieste senza sosta: il salvataggio in programma parte comunque
        timer.Stop();
        timer.Start();
    }

    void OnTick()
    {
        timer!.Stop();
        if (!soon) return; // l'ha già preso un Now
        soon = false;
        var (json, number, error) = Take();
        _ = Task.Run(() => Put(json, number, error, "accorpate, in sottofondo"));
    }

    /// <summary>L'istantanea delle impostazioni di adesso, con il suo numero.</summary>
    internal (byte[]? Json, long Number, Exception? Error) Take()
    {
        lock (taking)
        {
            var json = Store.Snapshot(settings(), out var error);
            ProtectedTextConverter.KeepOnlyWritten(); // in memoria solo i testi cifrati di questa istantanea
            return (json, Interlocked.Increment(ref taken), error);
        }
    }

    /// <summary>Un'istantanea sul file, se non ce n'è già una più nuova.</summary>
    internal bool Put(byte[]? json, long number, Exception? error, string how)
    {
        bool ok = false;
        if (json != null)
            lock (writing)
            {
                if (number < Interlocked.Read(ref onDisk))
                {
                    Skipped++;
                    return true; // sul file ce n'è già una più nuova: l'ultima vince
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                ok = Store.TryWrite(dir, json, recovery(), out error);
                Log.Time($"impostazioni scritte ({json.Length / 1024} KB, {how})", clock);
                if (ok)
                {
                    Interlocked.Exchange(ref onDisk, number);
                    Writes++;
                    LastWriteThread = Environment.CurrentManagedThreadId;
                }
            }
        if (ok || error == null) return ok;
        lock (writing)
        {
            if (failureTold) return false; // una riga e un avviso per sessione, non a ogni salvataggio
            failureTold = true;
        }
        Failed?.Invoke(error);
        return false;
    }
}

public static class Log
{
    static readonly object Gate = new();
#if DEBUG
    /// <summary>Prove: il registro (e il file degli errori di scrittura) in un'altra cartella, temporanea.</summary>
    internal static string? TestDir;
    static string Dir => TestDir ?? Store.Dir;
#else
    static string Dir => Store.Dir;
#endif
    /// <summary>
    /// Calcolato a ogni scrittura, non una volta sola: il 27 settembre 2026 una Missiva installata non ha scritto una riga
    /// dall'avvio (nemmeno i comandi apri ed esci), mentre il file si poteva scrivere; riavviata, scriveva di nuovo.
    /// </summary>
    static string FilePath => Path.Combine(Dir, "missiva.log");
    /// <summary>
    /// Dove si annota che il registro non si scrive: accanto al registro, nella cartella dei dati di questa versione. Prima
    /// era sempre %LOCALAPPDATA%\Missiva: una versione di prova, che lavora su una copia dei dati (MISSIVA_DATA), scriveva
    /// lì, nella cartella della Missiva installata (audit dell'11 ottobre, X-2). Per la versione installata è la stessa
    /// cartella di prima.
    /// </summary>
    internal static string FailurePath => Path.Combine(Dir, "missiva-errori.log");
    /// <summary>L'ultimo errore di scrittura già annotato in missiva-errori.log, per non scriverlo a ogni riga.</summary>
    static string? lastFailure;

    public static void Error(string context, Exception ex) =>
#if DEBUG || PROVA
        Write($"ERRORE ({context}): {ex}");
#else
        Write($"ERRORE ({context}): {WithoutAddresses(ex.ToString())}");
#endif

    static int watching;

    /// <summary>
    /// Gli errori che nessun gestore prende, nel registro (audit dell'11 ottobre, C-24). Prima c'era solo quello
    /// dell'interfaccia (App, DispatcherUnhandledException): un lavoro lanciato senza aspettarlo ("_ = ...Async()",
    /// Task.Run) che falliva spariva senza una riga, e un errore su un altro thread (la funzione di un Timer, un async void
    /// partito fuori dall'interfaccia) chiudeva Missiva senza dire perché. Il primo .NET lo dice solo quando il Task viene
    /// raccolto, quindi la riga può arrivare tardi; il secondo chiude comunque il processo, ma dopo la riga. Nessun avviso
    /// a schermo: solo il registro. Una volta sola.
    /// </summary>
    internal static void WatchUnhandled()
    {
        if (Interlocked.Exchange(ref watching, 1) == 1) return;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Unobserved(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Unhandled(e.ExceptionObject, e.IsTerminating);
    }

    /// <summary>Un Task fallito che nessuno aspettava (da WatchUnhandled; le prove li contano).</summary>
    static void Unobserved(AggregateException error)
    {
        var one = error.InnerExceptions.Count == 1 ? error.InnerExceptions[0] : error;
#if DEBUG
        var where = new System.Diagnostics.StackTrace(one).GetFrame(0)?.GetMethod();
        lock (UnobservedSeen) UnobservedSeen.Add($"{one.GetType().Name} in {where?.DeclaringType?.FullName}.{where?.Name}");
#endif
        Error("lavoro in sottofondo che nessuno aspettava", one);
    }

    /// <summary>Un errore fuori dal thread dell'interfaccia che nessuno ha preso (da WatchUnhandled): di solito Missiva si chiude subito dopo.</summary>
    static void Unhandled(object? error, bool terminating) =>
        Error(terminating ? "errore non gestito fuori dall'interfaccia, Missiva si chiude" : "errore non gestito fuori dall'interfaccia",
              error as Exception ?? new InvalidOperationException(error?.ToString() ?? "errore senza descrizione"));

#if DEBUG
    /// <summary>Prove: i Task falliti che nessuno aspettava arrivati a Unobserved in questo processo (tipo dell'errore e dove è nato).</summary>
    internal static readonly List<string> UnobservedSeen = new();
#endif

    /// <summary>
    /// Un lavoro che nessuno aspetta, o da cui dipende quello che viene dopo (l'uscita di Missiva finite le azioni di una
    /// notifica): un suo errore va nel registro con il contesto, subito, e non esce da qui. Vero se è finito senza errori.
    /// </summary>
    public static async Task<bool> GuardAsync(string context, Func<Task> work)
    {
        try
        {
            await work();
            return true;
        }
        catch (Exception ex)
        {
            Error(context, ex);
            return false;
        }
    }

    /// <summary>
    /// Il testo di un'eccezione senza indirizzi di posta (audit, blocco 10, S-27): gli errori di SMTP e IMAP riportano la
    /// risposta del server ("550 5.1.1 &lt;mario.rossi@example.com&gt;: Recipient address rejected"), e il registro della
    /// versione installata si manda a chi aiuta. Nella versione di prova restano, per capire chi ha sbagliato.
    /// </summary>
    internal static string WithoutAddresses(string text)
    {
        try { return MailAddressLike.Value.Replace(text, "(indirizzo)"); }
        catch (RegexMatchTimeoutException) { return "(testo dell'errore non registrato)"; }
    }

    static readonly Lazy<Regex> MailAddressLike = new(() =>
        new Regex(@"[A-Za-z0-9._%+'\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)));

    /// <summary>
    /// Oggetti di messaggi e titoli di eventi nel registro solo nella versione di prova. Quello della versione installata si
    /// apre dal menu ("Apri la cartella dei registri") e si manda a chi aiuta: non deve dire di che cosa parlano messaggi e
    /// riunioni ("Acquisizione X", "Colloquio Rossi").
    /// </summary>
    public static string Private(string text) =>
#if DEBUG || PROVA
        $"\"{text}\"";
#else
        "(nascosto)";
#endif

    /// <summary>Tempi delle operazioni, solo nella versione di prova (e nella finale compilata per le prove).</summary>
    [System.Diagnostics.Conditional("DEBUG"), System.Diagnostics.Conditional("PROVA")]
    public static void Time(string what, System.Diagnostics.Stopwatch clock) => Write($"tempi: {what} {clock.ElapsedMilliseconds} ms");

    /// <summary>
    /// Le fasi dell'avvio, contate dall'avvio del processo come "finestra pronta" (solo nella versione di prova): dove
    /// vanno i millisecondi prima che la finestra si veda.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG"), System.Diagnostics.Conditional("PROVA")]
    public static void Startup(string phase)
    {
#if DEBUG || PROVA
        StartupClock.Note(phase);
#endif
    }

    /// <summary>
    /// Le righe dell'avvio tenute in memoria vanno nel registro, tutte insieme e con il loro istante: all'elenco nel disegno,
    /// o quando la finestra nascosta ha scelto la Posta in arrivo. Da lì in poi Startup scrive subito.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG"), System.Diagnostics.Conditional("PROVA")]
    public static void StartupFlush()
    {
#if DEBUG || PROVA
        StartupClock.Flush();
#endif
    }

#if DEBUG || PROVA
    /// <summary>Le righe dell'avvio sono ancora tenute in memoria (prima di StartupFlush).</summary>
    public static bool StartupHeld => StartupClock.Held;

    /// <summary>
    /// Solo per Startup: nella versione installata, senza le righe "tempi:", non c'è nemmeno. Fino a Flush le righe restano in
    /// memoria (audit del 7 ottobre, A-10): ogni riga scritta subito apriva e chiudeva il file sul thread dell'interfaccia,
    /// dentro il tratto che si stava misurando (misurato: 0,48 ms a riga, 58 righe 27,7 ms). Dopo 15 secondi senza Flush (un
    /// avvio che si ferma) si scrivono comunque.
    /// </summary>
    static class StartupClock
    {
        static readonly List<(string Phase, DateTime At, int Thread)> held = new();
        /// <summary>Per il confronto (MISSIVA_REGISTRO_SUBITO=1): ogni riga scritta subito, come prima.</summary>
        static volatile bool flushed = Environment.GetEnvironmentVariable("MISSIVA_REGISTRO_SUBITO") == "1";
        static int firstThread = Environment.CurrentManagedThreadId;
        /// <summary>La riga con il costo delle scritture è già nel registro.</summary>
        static bool told;

        public static bool Held => !flushed;

        public static void Note(string phase)
        {
            var now = DateTime.Now;
            lock (held)
            {
                if (!flushed)
                {
                    if (held.Count == 0)
                    {
                        firstThread = Environment.CurrentManagedThreadId;
                        _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ => Flush(), TaskScheduler.Default);
                    }
                    held.Add((phase, now, Environment.CurrentManagedThreadId));
                    return;
                }
            }
            Write(Line(phase, now, Started(), Environment.CurrentManagedThreadId));
        }

        public static void Flush()
        {
            List<(string Phase, DateTime At, int Thread)> lines;
            lock (held)
            {
                if (told) return;
                told = flushed = true;
                lines = [.. held];
                held.Clear();
            }
            var started = Started();
            var (count, ticks) = (writes, writeTicks);
            var text = new System.Text.StringBuilder();
            foreach (var (phase, at, thread) in lines) text.Append($"{at:yyyy-MM-dd HH:mm:ss}  {Line(phase, at, started, thread)}{Environment.NewLine}");
            Append(text.ToString());
            Write($"tempi: registro durante l'avvio: {count} righe scritte subito in {ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0} ms, {lines.Count} tenute in memoria e scritte ora");
        }

        static DateTime? startedAt;

        /// <summary>L'avvio del processo secondo Windows, chiesto una volta sola (non alla prima riga: costa, e sposterebbe "Main").</summary>
        static DateTime Started()
        {
            if (startedAt is { } known) return known;
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            startedAt = self.StartTime;
            return self.StartTime;
        }

        static string Line(string phase, DateTime at, DateTime started, int thread) =>
            $"tempi: avvio, {phase} a {(at - started).TotalMilliseconds:0} ms{(thread == firstThread ? "" : $" (thread {thread})")}";
    }

    /// <summary>Quante righe ha scritto il registro e in quanto tempo (solo nella versione di prova: StartupClock.Flush).</summary>
    static int writes;
    static long writeTicks;
#endif

    public static void Write(string message) => Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {OneEntry(message)}{Environment.NewLine}");

    /// <summary>
    /// Il testo di una voce del registro, che non può fingerne un'altra (audit dell'11 ottobre, T-36). Nei messaggi entrano
    /// testi scritti da altri (il nome di una cartella IMAP, l'errore di un server): con un a capo dentro, quello che segue
    /// sembrava una riga nuova del registro, con la sua data. Ora una voce comincia in prima colonna con la data, e le righe
    /// che continuano (anche quelle vere, come la traccia di un errore) sono rientrate di quattro spazi; gli altri caratteri
    /// di controllo diventano uno spazio.
    /// </summary>
    internal static string OneEntry(string message)
    {
        static bool Breaks(char c) => c < ' ' && c != '\t' || c is '\u007f' or '\u0085' or (char)0x2028 or (char)0x2029;
        int first = 0;
        while (first < message.Length && !Breaks(message[first])) first++;
        if (first == message.Length) return message;
        var text = new System.Text.StringBuilder(message.Length + 32).Append(message, 0, first);
        for (int i = first; i < message.Length; i++)
        {
            char c = message[i];
            if (c == '\r' && i + 1 < message.Length && message[i + 1] == '\n') continue; // \r\n: conta il \n
            if (c is '\n' or '\r' or '\u0085' or (char)0x2028 or (char)0x2029) text.Append(Environment.NewLine).Append("    ");
            else text.Append(Breaks(c) ? ' ' : c);
        }
        return text.ToString();
    }

    static void Append(string text)
    {
        try
        {
            lock (Gate)
            {
#if DEBUG || PROVA
                long before = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                Directory.CreateDirectory(Dir);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000) File.Move(FilePath, FilePath + ".old", true);
                File.AppendAllText(FilePath, text);
                lastFailure = null;
#if DEBUG || PROVA
                writes++;
                writeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - before;
#endif
            }
        }
        catch (Exception ex)
        {
            // Il registro non si scrive: il motivo in un file a parte, una volta per tipo di errore, così si ritrova.
            try
            {
                var failure = ex.GetType().Name + ": " + ex.Message;
                if (failure == lastFailure) return;
                lastFailure = failure;
                File.AppendAllText(FailurePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  registro non scritto: {ex}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
