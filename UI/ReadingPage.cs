using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Un messaggio della conversazione, come va mostrato; Notes dice chi scrive davvero (SenderCheck), se c'è il corpo. Junk:
/// è nella Posta indesiderata, dove "Mostra immagini" vale solo per quella volta (ReadingView.LoadsImages). Unsubscribe: la
/// riga "Annulla iscrizione" sotto l'intestazione (Core/Unsubscribe.cs): null non c'è, vuoto la domanda con il comando,
/// altrimenti il testo da mostrare al suo posto (richiesta in corso o già inviata). ImagesOnce: perché "Mostra immagini"
/// vale solo per quella volta (ReadingView.ImagesOnce: Posta indesiderata, mittente non verificato, firma non valida, un
/// .eml aperto da un allegato), o null.
/// </summary>
public sealed record PageEntry(MessageSummary Summary, MessageBody? Body, bool Expanded, bool Loading, string? Error, bool AllowRemote, SenderNotes? Notes = null,
                               byte[]? Logo = null, bool Junk = false, string? Unsubscribe = null, string? ImagesOnce = null);

/// <summary>
/// Sopra la conversazione: l'oggetto e, nel tema scuro di Windows, il pulsante per il fondo chiaro o scuro. Account: nelle
/// viste di tutti gli account, l'indirizzo e il colore dell'account del messaggio (come sulla sua riga).
/// </summary>
public sealed record PageHeader(string Subject, bool Sun, (string Email, Color Color)? Account = null);

/// <summary>
/// Come appare il segnaposto di WPF del riquadro vuoto, per rifarlo uguale nella pagina (ReadingPage.Empty): il carattere e
/// la sua dimensione, i colori della busta e del testo sul fondo della pagina, lo zoom del riquadro e di quanto il centro
/// del riquadro sta sotto quello della pagina (i margini attorno a WebView2). Fonts sono i nomi dei caratteri com'è scritto
/// FontFamily.Source di WPF, separati da virgole: il valore per il CSS lo scrive la pagina (ReadingPage.CssFonts), e i due
/// colori sono colori, non testi: nella pagina non entra una stringa fatta altrove (revisione del 14 ottobre 2026, U-04).
/// </summary>
public sealed record EmptyLook(string Fonts, double FontSize, Color Glyph, Color Text, double Zoom, double Down);

/// <summary>
/// La pagina del riquadro di lettura: tutta la conversazione, con intestazioni nello stile Fluent di Missiva e il
/// corpo di ogni email isolato in una shadow root dichiarativa (niente JavaScript). Le azioni (rispondi, apri,
/// allegati) sono pulsanti di moduli verso https://missiva.example/do/gettone/..., che Missiva intercetta senza navigare.
/// </summary>
public sealed class ReadingPage
{
    /// <summary>Un nome .example, non .local: con .local ogni pagina aspettava 2 secondi la ricerca del nome in rete (mDNS).</summary>
    public const string Host = MailHtml.PageHost;
    public const string Origin = "https://" + Host;

    /// <summary>
    /// Gettone casuale di questa pagina, dentro l'indirizzo di ogni azione: Missiva esegue solo le azioni che lo portano.
    /// L'email non può leggerlo (niente script, e i suoi stili restano nella shadow root), quindi un suo link verso
    /// /do/... non preme i pulsanti del riquadro, per esempio per aprire un allegato senza i controlli.
    /// </summary>
    public string Token { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    static readonly CultureInfo Italian = new("it-IT");

    public string Html { get; private set; } = "";
    /// <summary>Immagini incorporate (cid:), per percorso.</summary>
    public Dictionary<string, (byte[] Data, string Type)> Resources { get; } = new();
    /// <summary>Per ogni indirizzo di link, i testi con cui compare: servono al controllo dei link ingannevoli.</summary>
    public Dictionary<string, List<string>> LinkTexts { get; } = new();
    /// <summary>
    /// Gli stessi testi per nome del sito (HostKey), per gli indirizzi che Chromium apre scritti diversamente da come li
    /// scrive .NET (per esempio i nomi con lettere accentate, che Chromium apre in punycode).
    /// </summary>
    public Dictionary<string, List<string>> HostTexts { get; } = new();

    public static string HostKey(Uri uri) => uri.IdnHost.TrimEnd('.').ToLowerInvariant();
    /// <summary>Immagini esterne bloccate, per messaggio.</summary>
    public int[] Blocked { get; private set; } = [];
    public bool RemoteAllowed { get; private set; }
    /// <summary>Il corpo con cui è stato disegnato ogni messaggio (null se non era arrivato): per sapere se questa pagina lo mostra.</summary>
    public IReadOnlyList<MessageBody?> Bodies { get; private set; } = [];

    string accountId = "";

    /// <param name="accountId">Per i colori delle categorie.</param>
    /// <param name="header">Oggetto e pulsanti sopra la conversazione; null per le pagine senza messaggio vero.</param>
    public static ReadingPage Build(IReadOnlyList<PageEntry> entries, bool light, string accountId = "", PageHeader? header = null)
    {
        var page = new ReadingPage { Blocked = new int[entries.Count], accountId = accountId, Bodies = entries.Select(e => e.Body).ToList() };
        var messages = new StringBuilder();
        for (int i = 0; i < entries.Count; i++) page.Message(messages, entries[i], i, entries.Count == 1, light);
        page.RemoteAllowed = entries.Any(e => e.Expanded && e.AllowRemote);
        var html = new StringBuilder();
        if (header != null) page.Header(html, header, entries, light);
        html.Append(messages);
        page.Html = Document(html.ToString(), light, page.RemoteAllowed, hold: entries.Count > 0);
        return page;
    }

    /// <summary>
    /// Il riquadro vuoto come il segnaposto di WPF (ReadingView.EmptyPanel): la busta e "Seleziona un elemento da leggere",
    /// negli stessi colori, carattere e posto (look), sul fondo della pagina. Così dal messaggio al riquadro vuoto e ritorno
    /// si passa dentro Chromium, che tiene la pagina di prima fino al primo disegno della nuova: prima WebView2 si nascondeva
    /// e, tornando visibile, mostrava per 55-60 ms l'ultima immagine disegnata, un foglio bianco (giro D1 di A).
    /// </summary>
    public static ReadingPage Empty(bool light, EmptyLook look)
    {
        // Le misure di WPF in pixel dello schermo: la pagina ha lo zoom del riquadro, che le ingrandirebbe.
        string Px(double v) => (v / look.Zoom).ToString("0.###", CultureInfo.InvariantCulture) + "px";
        var body = $"<div class=\"empty\" style=\"font-family:{CssFonts(look.Fonts)};font-size:{Px(look.FontSize)};padding-top:{Px(2 * look.Down)}\">" +
                   $"<span class=\"glyph\" style=\"font-size:{Px(48)};color:{Hex(look.Glyph)}\"></span>" +
                   $"<span style=\"margin-top:{Px(12)};color:{Hex(look.Text)}\">Seleziona un elemento da leggere</span></div>";
        return new ReadingPage { Html = Document(body, light, false, hold: false) };
    }

    /// <summary>
    /// I caratteri del segnaposto come valore di font-family dentro un attributo style: ogni nome fra apici, poi quelli di
    /// riserva. Lì la codifica dell'HTML (E) non basterebbe: protegge il bordo dell'attributo, non la sintassi del CSS, dove
    /// un apice o un punto e virgola nel nome chiuderebbero la stringa o la dichiarazione. Quindi entra solo un nome di
    /// carattere (HtmlToFlow.FontName: lettere, cifre, spazi, trattini; mai un indirizzo), gli altri si saltano. I nomi
    /// vengono dalle risorse di WPF, non da un'email né dalle impostazioni: è una difesa in più, e con i nomi di sempre il
    /// valore è quello di prima, carattere per carattere.
    /// </summary>
    internal static string CssFonts(string families) =>
        string.Join(",", families.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(HtmlToFlow.FontName).OfType<string>().Select(f => $"'{f}'")
            .Append("'Segoe UI'").Append("sans-serif"));

    /// <summary>
    /// Oggetto e avviso delle immagini bloccate. Stanno nella pagina, non nella finestra, perché cambino insieme al corpo:
    /// nativi, WPF li cambiava 30-50 ms prima che WebView2 disegnasse la pagina nuova, e l'avviso spingeva giù il messaggio
    /// vecchio.
    /// </summary>
    void Header(StringBuilder h, PageHeader header, IReadOnlyList<PageEntry> entries, bool light)
    {
        h.Append("<header class=\"subject\">");
        h.Append($"<h1>{E(header.Subject)}</h1>");
        if (header.Sun) h.Append(Action("sun/0", "\uE706", light ? "Mostra il messaggio su fondo scuro" : "Mostra il messaggio su fondo chiaro"));
        h.Append("</header>");
        if (header.Account is var (email, color))
            h.Append($"<div class=\"account\" title=\"Il messaggio \u00E8 nell'account {E(email)}\"><span class=\"dot\" style=\"background:{Hex(color)}\"></span>{E(email)}</div>");

        // Una riga sottile, come nel nuovo Outlook (prima tre righe a 1320 DIP e sei a 900): la spiegazione nel suggerimento.
        int blocked = Blocked.Sum();
        if (blocked == 0) return;
        // Dalla Posta indesiderata il mittente non si ricorda: lì le immagini si mostrano solo per quella volta (audit, F-12).
        // Lo stesso quando chi scrive non è verificato (audit dell'11 ottobre, T-02): il motivo lo dice ImagesOnce.
        static string? Once(PageEntry e) => e.ImagesOnce ?? (e.Junk ? "Questo messaggio è nella Posta indesiderata" : null);
        var keys = entries.Where((e, k) => Blocked[k] > 0 && Once(e) == null).Select(e => Settings.ImageKey(ReadingView.ImageSender(e.Summary, e.Body))).Where(k => k.Length > 0).Distinct();
        var reasons = entries.Where((e, k) => Blocked[k] > 0).Select(Once).Distinct().ToList();
        var why = (blocked == 1 ? "Un'immagine esterna è stata bloccata" : $"{blocked} immagini esterne sono state bloccate") +
                  ": scaricandole, il mittente può sapere che hai letto il messaggio. " +
                  (reasons.All(r => r != null) ? (reasons.Count == 1 ? reasons[0] + ": se" : "Se") + " le mostri, vale solo per questa volta."
                                               : "Se le mostri, Missiva lo ricorderà per " + string.Join(", ", keys) + ", in tutti gli account.");
        h.Append($"<div class=\"banner\" title=\"{E(why)}\"><span class=\"glyph\"></span>");
        h.Append("<span class=\"text\">Immagini esterne bloccate per la tua privacy.</span>");
        h.Append($"<form action=\"{ActionUrl("images/0")}\"><button class=\"show\" title=\"{E(why)}\">Mostra immagini</button></form></div>");
    }

    void Message(StringBuilder h, PageEntry entry, int i, bool single, bool light)
    {
        var s = entry.Summary;
        // Un clic sul resto dell'intestazione apre o chiude il messaggio (non se è l'unico e già aperto); la foto e il nome del
        // mittente aprono invece la scheda della persona, in ogni messaggio. L'apertura è un pulsante che copre l'intestazione
        // dietro tutto il resto (z-index -1), e foto e nome sono pulsanti veri sopra di lui: un pulsante dentro un altro non è HTML.
        bool toggles = !(single && entry.Expanded);
        bool personLink = s.From.Email.Length > 0;
        string tip = toggles ? $"label for=\"t{i}\"" : "span", tipEnd = toggles ? "label" : "span";
        h.Append($"<article id=\"m{i}\" class=\"msg{(entry.Expanded ? " open" : "")}\"><div class=\"head{(toggles ? " tg" : "")}\">");

        var who = new StringBuilder();
        // Il logo del marchio (Logos), servito da Missiva come le immagini incorporate; altrimenti le iniziali. Mai accanto
        // alla fascia rossa "mittente non verificato": il logo dice fiducia, la fascia dice il contrario (audit, S-04).
        string pic, picClass = "avatar", picStyle = "";
        if (entry.Logo is { } logo && entry.Notes?.Unverified == null)
        {
            Resources[$"/logo/{i}.png"] = (logo, "image/png");
            pic = $"<img src=\"/logo/{i}.png\" alt=\"\">";
            picClass = "avatar logo";
        }
        else
        {
            pic = E(Avatars.Initials(s.From));
            picStyle = $" style=\"background:{Hex(Avatars.ColorOf(s.From))}\"";
        }
        var cardTitle = $"Scheda di {E(s.From.Display)}";
        who.Append(personLink
            ? $"<form action=\"{ActionUrl($"person/{i}")}\"><button class=\"{picClass} pic\"{picStyle} title=\"{cardTitle}\" aria-label=\"{cardTitle}\">{pic}</button></form><span class=\"info\">"
            : $"<span class=\"{picClass}\"{picStyle}>{pic}</span><span class=\"info\">");
        // Il nome del mittente apre la sua scheda (contatto, ultimi messaggi e file), come nel nuovo Outlook, e così la foto:
        // la stessa azione "person" in ogni intestazione, del messaggio da solo e di ogni messaggio della conversazione.
        // Un nome che è l'indirizzo di un altro (SenderCheck.NameAddress): Display è l'indirizzo vero, che qui va a capo
        // invece di accorciarsi (classe "real"), perché nel riquadro stretto non sparisca; il nome scritto lo dice la fascia.
        string real = SenderCheck.NameAddress(s.From) != null ? " real" : "";
        who.Append(personLink
            ? $"<span class=\"from pl{real}\"><form action=\"{ActionUrl($"person/{i}")}\"><button class=\"person\" title=\"{cardTitle}\" aria-label=\"{cardTitle}\"><b>{E(s.From.Display)}</b></button></form>"
            : $"<span class=\"from{real}\"><b>{E(s.From.Display)}</b>");
        // L'indirizzo fra < > solo se aggiunge qualcosa: PayPal scrive come nome l'indirizzo stesso.
        if (entry.Expanded && real.Length == 0 && s.From.Name.Length > 0 && !s.From.Name.Trim('"', '\'', ' ').Equals(s.From.Email, StringComparison.OrdinalIgnoreCase))
            who.Append($" <span class=\"addr\">&lt;{E(s.From.Email)}&gt;</span>");
        // Account di lavoro: il mittente fuori dall'organizzazione, come "Esterno" in Outlook.
        if (entry.Expanded && entry.Notes?.External == true)
            who.Append($"<{tip} class=\"ext\" title=\"Il mittente non fa parte della tua organizzazione\">Esterno</{tipEnd}>");
        who.Append("</span>");
        if (entry.Expanded && entry.Body != null)
        {
            if (entry.Body.To.Count > 0) who.Append(Recipients("A", entry.Body.To, tip: tip));
            if (entry.Body.Cc.Count > 0) who.Append(Recipients("Cc", entry.Body.Cc, tip: tip));
        }
        else if (entry.Expanded && entry.Error == null)
        {
            // I destinatari arrivano con il corpo: intanto una barretta al posto della riga "A:", così l'intestazione
            // non si sposta quando arrivano.
            who.Append("<span class=\"line recipients\"><span class=\"bar\"></span></span>");
        }
        // Reply-To diverso dal mittente: Rispondi va lì, e qui si vede (moduli dei siti, ticket, ma anche le frodi).
        if (entry.Expanded && s.ReplyTo.Count > 0) who.Append(Recipients("Le risposte vanno a", s.ReplyTo, "replyto", tip));
        var categories = s.Categories.Where(c => !c.Equals(s.FolderId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (entry.Expanded && categories.Count > 0)
        {
            who.Append("<span class=\"cats\">");
            foreach (var c in categories)
            {
                int preset = CategoryPalette.PresetOf(accountId, c);
                who.Append($"<{tip} class=\"cat\" style=\"background:{CategoryPalette.Hex(preset)};color:{CategoryPalette.TextHex(preset)}\" title=\"{E(c)}\">{E(c.Contains('/') ? c[(c.LastIndexOf('/') + 1)..] : c)}</{tipEnd}>");
            }
            who.Append("</span>");
        }
        else if (!entry.Expanded)
        {
            who.Append($"<span class=\"line preview\">{E(s.Preview)}</span>");
        }
        who.Append("</span>");
        h.Append($"<div class=\"who\">{who}</div>");
        // Il pulsante di apertura sta dopo il mittente nell'ordine di tabulazione (foto, nome, apri, azioni); è fuori dal flusso.
        if (toggles)
        {
            var what = entry.Expanded ? "Comprimi" : "Espandi";
            h.Append($"<form action=\"{ActionUrl($"toggle/{i}")}\"><button id=\"t{i}\" class=\"tog\" title=\"{what}\" aria-label=\"{what} il messaggio di {E(s.From.Display)}\"></button></form>");
        }

        h.Append("<div class=\"side\">");
        if (entry.Expanded && s.IsDraft)
        {
            // Una bozza non si risponde: si continua, come nel nuovo Outlook.
            h.Append("<div class=\"actions\">");
            h.Append($"<form action=\"{ActionUrl($"edit/{i}")}\"><button class=\"edit\" title=\"Continua a scrivere la bozza\" aria-label=\"Modifica\">" +
                     "<span class=\"glyph\"></span>Modifica</button></form>");
            h.Append(Action($"more/{i}", "", "Altre azioni"));
            h.Append("</div>");
        }
        else if (entry.Expanded)
        {
            h.Append("<div class=\"actions\">");
            h.Append(Action($"reply/{i}", "", "Rispondi"));
            h.Append(Action($"replyall/{i}", "<span class=\"back\"></span>", "Rispondi a tutti", "act all"));
            h.Append(Action($"forward/{i}", "<span class=\"mirror\"></span>", "Inoltra"));
            // La scheda del mittente non ha più un'azione qui: la aprono la foto e il nome (ogni intestazione ha la stessa azione "person"),
            // quindi le azioni sono le stesse da solo e nella conversazione (così alla sua venuta l'indirizzo non si accorcia).
            // Altre azioni, come nel nuovo Outlook: il menu del messaggio (MainWindow.BuildMessageMenu) per questo messaggio.
            h.Append(Action($"more/{i}", "", "Altre azioni"));
            h.Append("</div>");
        }
        h.Append($"<div class=\"date\">{E(s.Date.ToString("ddd dd/MM/yyyy HH:mm", Italian))}</div></div></div>");

        if (entry.Expanded)
        {
            Warnings(h, entry, light);
            // Annulla iscrizione (List-Unsubscribe), come la riga del nuovo Outlook in cima al messaggio: una domanda e il comando,
            // o al suo posto com'è andata. Che cosa fa lo decide Unsubscribe.For, e nulla parte senza la conferma.
            if (entry.Unsubscribe is { } unsubscribe) h.Append(UnsubscribeRow(i, unsubscribe));
            if (entry.Body?.Invite is { } invite) Invite(h, invite, i);
            // Come nel nuovo Outlook gli allegati stanno fra l'intestazione e il corpo: in fondo, sotto il piè di pagina di
            // una ricevuta, sembrava che mancassero.
            Files(h, entry, i, light);
            h.Append("<div class=\"content\">");
            if (entry.Error != null) h.Append($"<div class=\"status\">{E(entry.Error)}</div>");
            else if (entry.Body == null) h.Append("<div class=\"placeholder\" title=\"Caricamento…\"><span></span><span></span><span class=\"short\"></span></div>");
            else h.Append(Body(entry, i, light));
            h.Append("</div>");
        }
        h.Append("</article>");
    }

    /// <summary>
    /// La riga "Annulla iscrizione" del messaggio i di questa pagina (PageEntry.Unsubscribe: vuoto la domanda con il comando,
    /// altrimenti com'è andata). Anche da sola: il riquadro la aggiunge alla pagina già mostrata quando le intestazioni
    /// arrivano dopo il corpo (ReadingView.FillListHeadersAsync).
    /// </summary>
    internal string UnsubscribeRow(int i, string unsubscribe) => unsubscribe.Length > 0
        ? $"<div class=\"unsub\"><span>{E(unsubscribe)}</span></div>"
        : $"<div class=\"unsub\"><span>Ricevi troppi messaggi da questo mittente?</span><form action=\"{ActionUrl($"unsubscribe/{i}")}\">" +
          "<button title=\"Chiede al mittente di non mandarti più questi messaggi. Prima Missiva ti dice che cosa farà.\">Annulla iscrizione</button></form></div>";

    /// <summary>
    /// Lo script che mette quella riga al suo posto nella pagina già mostrata: sotto l'intestazione e le fasce, prima
    /// dell'invito, degli allegati e del testo; nulla se c'è già.
    /// </summary>
    internal string UnsubscribeScript(int i, string unsubscribe) =>
        $"(function(){{var a=document.getElementById('m{i}');if(!a||a.querySelector(':scope > .unsub'))return false;" +
        "var t=a.querySelector(':scope > .invite, :scope > .files, :scope > .content');if(!t)return false;" +
        $"t.insertAdjacentHTML('beforebegin',{System.Text.Json.JsonSerializer.Serialize(UnsubscribeRow(i, unsubscribe))});return true;}})()";

    /// <summary>
    /// Sotto l'intestazione, come le fasce di Outlook: rossa se il server della casella non ha potuto verificare il
    /// mittente, gialla se il nome è di un contatto ma l'indirizzo no.
    /// </summary>
    static void Warnings(StringBuilder h, PageEntry entry, bool light)
    {
        // Oltre i tetti dell'analisi dell'HTML (MailHtml.ParseGuard: migliaia di elementi annidati o riaperti, testo spezzato
        // all'infinito) il messaggio si apre lo stesso, come solo testo. La pulizia è quella che Body riusa subito dopo.
        if (PlainOnly(entry, light))
            h.Append("<div class=\"warn\" title=\"Il contenuto ha troppi elementi annidati o ripetuti per mostrarlo com'è: qui c'è il suo testo, senza formato né immagini.\">" +
                     "<span class=\"glyph\"></span><span>Messaggio troppo complesso: mostrato come solo testo.</span></div>");
        // Senza rete, dalla copia su disco (BodyCache).
        if (entry.Body?.OfflineCopy is { } saved)
            h.Append($"<div class=\"warn\" title=\"Missiva tiene una copia cifrata dei messaggi aperti: quando torna la rete si apre quello del server.\">" +
                     $"<span class=\"glyph\"></span><span>Non in linea: copia del {E(saved.ToString("dd/MM/yyyy"))} alle {E(saved.ToString("HH:mm"))}.</span></div>");
        // Dalla copia, una busta PEC o un messaggio firmato: la firma la controlla Pec con la busta del server, che senza rete
        // non c'è. Senza questa fascia una busta finta sembrerebbe vera, senza la rossa che avrebbe avuto in linea.
        if (entry.Body is { OfflineCopy: not null, Pec: null } copy && Missiva.Pec.Wants(copy))
            h.Append("<div class=\"warn pec\" title=\"La firma si verifica con il messaggio com'è sul server.\"><span class=\"glyph\"></span>" +
                     "<span>Posta certificata o messaggio firmato: la firma si controlla solo in linea. Fino ad allora non fidarti di mittente e contenuto.</span></div>");
        // Posta certificata e messaggi firmati (Pec): verde se la firma è verificata, gialla se il certificato non è
        // riconosciuto, rossa se la busta è stata alterata o il messaggio non è stato consegnato.
        if (entry.Body?.Pec is { } pec)
        {
            var (text, tone) = Missiva.Pec.Band(pec);
            var glyph = tone switch { "ok" => "", "danger" => "", _ => "" };
            var tip = pec.Signer.Length > 0 ? $"Firma di {pec.Signer}" : "Nessuna firma";
            h.Append($"<div class=\"warn pec {(tone == "warn" ? "" : tone)}\" title=\"{E(tip)}\"><span class=\"glyph\">{glyph}</span><span>{E(text)}</span></div>");
        }
        if (entry.Notes is not { } notes) return;
        if (notes.Unverified is { } domain)
            h.Append($"<div class=\"warn danger\" title=\"{E(notes.Why)}\"><span class=\"glyph\">\uE7BA</span>" +
                     $"<span>Non è stato possibile verificare che questo messaggio venga da {E(domain)}. Attenzione a link, allegati e richieste di pagamento.</span></div>");
        if (notes.LookAlike is { } known)
            h.Append($"<div class=\"warn\" title=\"Chi scrive usa il nome di una persona a cui hai già scritto, ma da un indirizzo a cui non hai mai scritto.\">" +
                     $"<span class=\"glyph\">\uE77B</span><span>{E(entry.Summary.From.Name)} è il nome di un tuo contatto, che conosci come {E(known)}: " +
                     $"questo messaggio arriva da un altro indirizzo, {E(entry.Summary.From.Email)}.</span></div>");
        // Un nome che è l'indirizzo (o il sito) di un altro: l'intestazione, l'elenco e le notifiche mostrano quello vero
        // (Address.Display), e qui si dice che cosa c'era scritto.
        if (notes.NameAddress != null)
        {
            var written = entry.Summary.From.Name;
            h.Append("<div class=\"warn\" title=\"Chi scrive ha messo nel suo nome un indirizzo o un sito che non è quello da cui il messaggio arriva. Missiva mostra l'indirizzo vero.\">" +
                     $"<span class=\"glyph\"></span><span>Il mittente si presenta come «{E(written.Length > 120 ? written[..120] + "…" : written)}», " +
                     $"ma questo messaggio arriva da un altro indirizzo: {E(entry.Summary.From.Email)}.</span></div>");
        }
    }

    /// <summary>
    /// La scheda dell'invito, come in Outlook, Thunderbird ed eM Client: titolo, quando (nel fuso di Windows), ricorrenza,
    /// dove, organizzatore, partecipanti, link della riunione online (un link come quelli delle email, con il loro controllo)
    /// e, solo per le richieste (METHOD:REQUEST), Accetta, Provvisorio, Rifiuta, Proponi un nuovo orario. Annullata, una
    /// risposta ("Mario ha accettato"), una proposta di un nuovo orario o un evento pubblicato: lo dice, senza pulsanti.
    /// </summary>
    void Invite(StringBuilder h, Invitation invite, int i)
    {
        var who = invite.Responder is { } r ? r.Display : "Un partecipante";
        var (prefix, label) = invite.Method switch
        {
            "CANCEL" => ("Riunione annullata: ", "Invito"),
            "REPLY" => (who + " " + invite.ResponseStatus switch
            {
                "ACCEPTED" => "ha accettato",
                "DECLINED" => "ha rifiutato",
                "TENTATIVE" => "ha accettato in modo provvisorio",
                "DELEGATED" => "ha delegato",
                _ => "ha risposto a",
            } + ": ", "Risposta a un invito"),
            "COUNTER" => (who + " propone un nuovo orario: ", "Proposta di un nuovo orario"),
            "DECLINECOUNTER" => ("Nuovo orario non accettato: ", "Risposta a una proposta"),
            "REQUEST" => ("", "Invito"),
            _ => ("", "Evento"),
        };
        h.Append($"<section class=\"invite{(invite.Cancelled ? " cancelled" : "")}\" aria-label=\"{label}\">");
        h.Append($"<div class=\"title\"><span class=\"glyph\"></span>{E(prefix)}{E(invite.Summary.Length > 0 ? invite.Summary : "(senza titolo)")}</div>");
        void Row(string label, string html) => h.Append($"<div class=\"row\"><span class=\"k\">{label}</span><span class=\"v\">{html}</span></div>");
        Row(invite.Method == "COUNTER" ? "Nuovo orario" : "Quando", E(invite.When));
        if (invite.Recurrence != null) Row("Ricorrenza", E(invite.Recurrence));
        if (invite.Location != null) Row("Dove", E(invite.Location));
        if (invite.Organizer is { } organizer) Row("Organizzatore", E(organizer.ToString()));
        if (invite.Attendees.Count > 0)
        {
            var names = invite.Attendees.Select(a => a.Display).ToList();
            Row("Partecipanti", $"<span title=\"{E(string.Join("; ", invite.Attendees))}\">{E(string.Join(", ", names.Take(4)) + (names.Count > 4 ? $" e altri {names.Count - 4}" : ""))}</span>");
        }
        if (invite.OnlineUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var text = "Partecipa alla riunione online (" + HostKey(uri) + ")";
            if (!LinkTexts.TryGetValue(uri.AbsoluteUri, out var texts)) LinkTexts[uri.AbsoluteUri] = texts = new();
            texts.Add(text);
            if (!HostTexts.TryGetValue(HostKey(uri), out var hostTexts)) HostTexts[HostKey(uri)] = hostTexts = new();
            hostTexts.Add(text);
            Row("Online", $"<a href=\"{E(uri.AbsoluteUri)}\">{E(text)}</a>");
        }
        if (invite.Answerable)
        {
            h.Append("<div class=\"answers\">");
            if (invite.NeedsConsent)
            {
                h.Append("<span class=\"note\">Per rispondere agli inviti di Outlook, Missiva deve poter usare il tuo calendario.</span>");
                h.Append($"<form action=\"{ActionUrl($"consent/{i}")}\"><button class=\"answer primary\">Consenti l'accesso al calendario</button></form>");
            }
            else
            {
                h.Append($"<form action=\"{ActionUrl($"accept/{i}")}\"><button class=\"answer primary\"><span class=\"glyph\"></span>Accetta</button></form>");
                h.Append($"<form action=\"{ActionUrl($"tentative/{i}")}\"><button class=\"answer\"><span class=\"glyph\"></span>Provvisorio</button></form>");
                h.Append($"<form action=\"{ActionUrl($"decline/{i}")}\"><button class=\"answer\"><span class=\"glyph\"></span>Rifiuta</button></form>");
                h.Append($"<form action=\"{ActionUrl($"propose/{i}")}\"><button class=\"answer\"><span class=\"glyph\"></span>Proponi un nuovo orario</button></form>");
            }
            h.Append("</div>");
        }
        h.Append("</section>");
    }

    /// <summary>Schede degli allegati: icona del tipo di file, nome e dimensione, Apri (clic) e Salva con nome; con due o più, Salva tutti.</summary>
    void Files(StringBuilder h, PageEntry entry, int i, bool light)
    {
        var files = entry.Body?.Attachments.Select((a, j) => (a, j)).Where(x => !x.a.IsInline).ToList() ?? new();
        if (files.Count == 0) return;
        h.Append("<div class=\"files\">");
        foreach (var (a, j) in files)
        {
            bool dangerous = Safety.IsDangerous(a.Name);
            h.Append("<span class=\"file\">");
            h.Append($"<form action=\"{ActionUrl($"open/{i}/{j}")}\"><button class=\"chip{(dangerous ? " danger" : "")}\" title=\"{(dangerous ? "Attenzione: questo tipo di file può avviare programmi" : "Apri")}\">");
            h.Append(dangerous ? "<span class=\"glyph\"></span>" : FileIcon(a.Name, a.ContentType, light));
            h.Append($"<span class=\"meta\"><span class=\"name\">{E(a.Name)}</span>");
            if (a.Size > 0) h.Append($"<span class=\"size\">{E(SizeText(a.Size))}</span>");
            h.Append("</span></button></form>");
            h.Append(Action($"save/{i}/{j}", "", "Salva con nome…", "save"));
            h.Append("</span>");
        }
        if (files.Count > 1)
            h.Append($"<form action=\"{ActionUrl($"saveall/{i}")}\"><button class=\"saveall\" title=\"Salva tutti gli allegati in una cartella\">Salva tutti</button></form>");
        h.Append("</div>");
    }

    /// <summary>
    /// Icona del tipo di file, come nel nuovo Outlook: un foglio con una fascia colorata, PDF rosso, Word blu, Excel verde,
    /// PowerPoint arancione, archivi ocra, immagini con una montagna, audio e video viola; per gli altri tipi l'estensione
    /// su fondo grigio. Colori in FileKinds, gli stessi dell'elenco.
    /// </summary>
    static string FileIcon(string name, string contentType, bool light)
    {
        var kind = FileKinds.Of(name, contentType);
        var ext = FileKinds.Extension(name);
        bool image = kind == FileKinds.Kind.Image;
        var color = FileKinds.Band(kind, light);
        var label = kind switch
        {
            FileKinds.Kind.Pdf => "PDF",
            FileKinds.Kind.Word => "W",
            FileKinds.Kind.Excel => "X",
            FileKinds.Kind.PowerPoint => "P",
            FileKinds.Kind.Archive => "ZIP",
            _ => ext.Length is > 0 and <= 4 ? ext.ToUpperInvariant() : "",
        };
        string sheet = light ? "#FFFFFF" : "#3A3A3A", edge = light ? "#8A8A8A" : "#9A9A9A";
        var mark = image
            ? "<path d=\"M3.5 21l4-4.6 2.8 3 2.1-2.2 3.4 3.8z\" fill=\"#fff\"/><circle cx=\"13.6\" cy=\"15.6\" r=\"1.5\" fill=\"#fff\"/>"
            : $"<text x=\"9.5\" y=\"17.9\" text-anchor=\"middle\" dominant-baseline=\"central\" font-family=\"Segoe UI\" font-weight=\"700\" " +
              $"font-size=\"{(label.Length > 3 ? "5.5" : "7")}\" fill=\"#fff\">{E(label)}</text>";
        return "<svg class=\"ficon\" viewBox=\"0 0 24 28\" aria-hidden=\"true\">" +
               $"<path d=\"M5.5 1.5h9.8l5.2 5.2v18.3a1.5 1.5 0 0 1-1.5 1.5h-13.5a1.5 1.5 0 0 1-1.5-1.5v-22a1.5 1.5 0 0 1 1.5-1.5z\" fill=\"{sheet}\" stroke=\"{edge}\"/>" +
               $"<path d=\"M15.3 1.5v4.2a1 1 0 0 0 1 1h4.2\" fill=\"none\" stroke=\"{edge}\"/>" +
               $"<rect x=\"1\" y=\"12.5\" width=\"17\" height=\"10.5\" rx=\"2\" fill=\"{color}\"/>{mark}</svg>";
    }

    /// <summary>Un corpo già pulito (MailHtml.Prepare) per un tema e per le immagini consentite o no, con le sue immagini cid:.</summary>
    sealed record Prepared(bool Dark, bool AllowRemote, MailHtml.Result Result, Dictionary<string, (byte[] Data, string Type)> Images);

    /// <summary>
    /// La pulizia dell'HTML (AngleSharp, fogli di stile, colori del tema scuro) costa decine di ms sulle newsletter grandi,
    /// e il riquadro ridisegna la pagina più volte per lo stesso corpo (in Gmail quando arriva la conversazione, e aprendo
    /// un altro suo messaggio): si fa una volta per corpo, tema e immagini consentite. Legata al corpo, sparisce con lui.
    /// </summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MessageBody, Prepared> cleaned = new();
    /// <summary>Numero delle pulizie, nei percorsi delle immagini cid: (una pagina può avere più corpi con lo stesso cid).</summary>
    static int preparations;

    /// <summary>
    /// Pulisce il corpo, se non è già pulito per quel tema e per quelle immagini: ReadingView lo chiama in sottofondo appena
    /// il corpo arriva, così il disegno non ferma l'interfaccia. Si può chiamare da qualunque thread.
    /// </summary>
    public static void Warm(MessageBody body, bool light, bool allowRemote) => PrepareBody(body, light, allowRemote);

    static Prepared PrepareBody(MessageBody body, bool light, bool allowRemote)
    {
        if (cleaned.TryGetValue(body, out var done) && done.Dark == !light && done.AllowRemote == allowRemote) return done;
        var images = new Dictionary<string, (byte[] Data, string Type)>();
        var key = Interlocked.Increment(ref preparations);
        string? CidUrl(string cid)
        {
            var a = body.Attachments.FirstOrDefault(x => x.Content != null && string.Equals(x.ContentId?.Trim('<', '>'), cid, StringComparison.OrdinalIgnoreCase));
            if (a == null) return null;
            var path = $"/cid/{key}/{Uri.EscapeDataString(cid)}";
            images[path] = (a.Content!, a.ContentType.Length > 0 ? a.ContentType : "application/octet-stream");
            return Origin + path;
        }
        done = new Prepared(!light, allowRemote, MailHtml.Prepare(body.Html, !light, allowRemote, CidUrl), images);
        cleaned.AddOrUpdate(body, done);
        return done;
    }

    /// <summary>Il corpo è mostrato come solo testo (MailHtml.Result.PlainOnly). Se la pulizia fallisce lo dice Body.</summary>
    static bool PlainOnly(PageEntry entry, bool light)
    {
        if (entry.Body == null || entry.Error != null) return false;
        try
        {
            return PrepareBody(entry.Body, light, entry.AllowRemote).Result.PlainOnly;
        }
        catch
        {
            return false;
        }
    }

    string Body(PageEntry entry, int i, bool light)
    {
        Prepared done;
        try
        {
            done = PrepareBody(entry.Body!, light, entry.AllowRemote);
        }
        catch (Exception ex)
        {
            Log.Error("preparazione messaggio", ex);
            return "<div class=\"status\">Non riesco a mostrare questo messaggio.</div>";
        }
        foreach (var (path, image) in done.Images) Resources[path] = image;
        var prepared = done.Result;
        Blocked[i] = prepared.BlockedImages;
        foreach (var link in prepared.Links)
        {
            if (!Uri.TryCreate(link.Href, UriKind.Absolute, out var uri)) continue;
            if (!LinkTexts.TryGetValue(uri.AbsoluteUri, out var texts)) LinkTexts[uri.AbsoluteUri] = texts = new();
            texts.Add(link.Text);
            if (uri.Scheme is not ("http" or "https")) continue;
            if (!HostTexts.TryGetValue(HostKey(uri), out var hostTexts)) HostTexts[HostKey(uri)] = hostTexts = new();
            hostTexts.Add(link.Text);
        }
        return $"<div class=\"mail\"><template shadowrootmode=\"open\"><style>{ShadowCss(light)}</style>{prepared.Fragment}</template></div>";
    }

    /// <summary>Destinatari su una riga, accorciati con i puntini se non ci stanno: tutti gli indirizzi nel suggerimento.</summary>
    static string Recipients(string label, List<Address> list, string cls = "", string tip = "span") =>
        $"<{tip} class=\"line recipients{(cls.Length > 0 ? " " + cls : "")}\" title=\"{E(string.Join("; ", list))}\">" +
        $"{label}: {E(string.Join("; ", list.Select(a => cls.Length > 0 ? a.ToString() : a.Display)))}</{tip.Split(' ')[0]}>";

    string ActionUrl(string path) => $"{Origin}/do/{Token}/{path}";

    string Action(string path, string glyph, string title, string cls = "act") =>
        $"<form action=\"{ActionUrl(path)}\"><button class=\"{cls}\" title=\"{E(title)}\" aria-label=\"{E(title)}\">{glyph}</button></form>";

    static string E(string text) => WebUtility.HtmlEncode(text);

    static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1048576.0:0.#} MB",
    };

    /// <summary>Valori predefiniti dentro ogni email: :where() li rende deboli, così vince sempre lo stile dell'email.</summary>
    static string ShadowCss(bool light) =>
        ":host{display:block}" +
        ":where(.missiva-body){font-family:'Segoe UI',sans-serif;font-size:14px;overflow-wrap:break-word}" +
        $":where(.missiva-body) a{{color:{(light ? "#005FB8" : "#6CB8F6")}}}" +
        ":where(.missiva-body) img{border:0}";

    /// <param name="hold">
    /// Una pagina con messaggi nasce nascosta (classe "fitting"): ReadingView la mostra quando è adattata al riquadro
    /// (FitAsync) e le immagini sono arrivate, e intanto Chromium tiene la pagina di prima. Prima si vedeva per 80-300 ms la
    /// mail a grandezza piena o senza immagini, poi rimpicciolita o spostata (misurato con lampi.ps1). Se lo script di
    /// Missiva non arriva, dopo 600 ms si mostra da sola (l'animazione del CSS). E tiene sempre lo spazio della barra di
    /// scorrimento (scrollbar-gutter): la conversazione che arriva e allunga la pagina non la stringe più di 8 pixel.
    /// </param>
    static string Document(string body, bool light, bool remote, bool hold)
    {
        string paper = light ? "#FFFFFF" : Hex(MailHtml.DarkPaper), ink = light ? "#1B1B1B" : "#E8E8E8", subtle = light ? "#616161" : "#ADADAD";
        string rule = light ? "#E5E5E5" : "#3A3A3A", hover = light ? "rgba(0,0,0,.05)" : "rgba(255,255,255,.07)", press = light ? "rgba(0,0,0,.09)" : "rgba(255,255,255,.11)";
        // I comandi della pagina (Mostra immagini, Salva tutti, inviti) nel colore d'accento di Windows, come il resto di
        // Missiva (richiesta dell'autore, giro D1): la tonalità scura sulla pagina chiara e la chiara sulla scura, come il testo
        // d'accento di Fluent. I link dentro le email restano blu (ShadowCss).
        string link = Hex(light ? System.Windows.SystemColors.AccentColorDark2 : System.Windows.SystemColors.AccentColorLight3);
        string thumb = light ? "rgba(0,0,0,.36)" : "rgba(255,255,255,.36)", thumbHover = light ? "rgba(0,0,0,.52)" : "rgba(255,255,255,.52)";
        var images = remote ? " https: http:" : "";
        return $$"""
            <!doctype html><html lang="it"{{(hold ? " class=\"fitting\" style=\"scrollbar-gutter: stable\"" : "")}}><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src {{Origin}} data:{{images}}; font-src data:; form-action {{Origin}}">
            <meta name="referrer" content="no-referrer">
            <style>
              :root { color-scheme: {{(light ? "light" : "dark")}}; }
              html, body { margin: 0; background: {{paper}}; color: {{ink}}; }
              body { font: 14px "Segoe UI Variable Text", "Segoe UI", sans-serif; padding: 4px 0 12px; }
              .fitting body { visibility: hidden; animation: missiva-show 0s linear .6s forwards; }
              /* Il riquadro vuoto (Empty), come il segnaposto di WPF: al centro, un po' più in basso quanto i margini attorno */
              .empty { position: fixed; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center;
                       user-select: none; cursor: default; }
              .empty .glyph { font-family: "Segoe Fluent Icons", "Segoe MDL2 Assets"; }
              @keyframes missiva-show { to { visibility: visible; } }
              .leaving body { opacity: .4; transition: opacity .15s; }
              form { display: contents; }
              button { font: inherit; color: inherit; background: none; border: 0; margin: 0; padding: 0; text-align: left; }
              .subject { display: flex; align-items: flex-start; gap: 8px; padding: 8px 0 6px 8px; }
              .subject h1 { flex: 1; min-width: 0; margin: 2px 0 0; font-size: 20px; font-weight: 600; line-height: 1.3; overflow-wrap: anywhere; }
              .account { display: flex; align-items: center; gap: 6px; margin: -2px 0 6px 8px; font-size: 12px; color: {{subtle}}; }
              .account .dot { flex: none; width: 8px; height: 8px; border-radius: 4px; }
              .banner { display: flex; align-items: center; gap: 8px; margin: 0 0 4px; padding: 5px 6px 5px 12px; border-radius: 6px;
                        background: {{hover}}; font-size: 12px; line-height: 16px; }
              .banner .glyph { flex: none; font-family: "Segoe Fluent Icons", "Segoe MDL2 Assets"; font-size: 13px; color: {{subtle}}; }
              .banner .text { flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
              .banner .show { flex: none; padding: 2px 6px; border-radius: 4px; color: {{link}}; font-weight: 600; white-space: nowrap; }
              .banner .show:hover { background: {{hover}}; text-decoration: underline; }
              .banner .show:active { background: {{press}}; }
              /* Chi scrive davvero (SenderCheck): fascia rossa per il mittente non verificato, gialla per il nome di un contatto */
              .warn { display: flex; align-items: flex-start; gap: 8px; margin: 10px 0 0 48px; padding: 6px 10px; border-radius: 6px;
                      font-size: 12px; line-height: 16px; background: {{(light ? "#FFF4CE" : "#433519")}}; color: {{(light ? "#5C4400" : "#FCE100")}}; }
              .warn.danger { background: {{(light ? "#FDE7E9" : "#442726")}}; color: {{(light ? "#A4262C" : "#FF99A4")}}; }
              .warn.ok { background: {{(light ? "#DFF6DD" : "#1F3B20")}}; color: {{(light ? "#0E5C0E" : "#9FD89F")}}; }
              .warn .glyph { flex: none; font-family: "Segoe Fluent Icons", "Segoe MDL2 Assets"; font-size: 13px; line-height: 16px; }
              /* Annulla iscrizione: una riga discreta sotto l'intestazione, con il comando scritto come un link */
              .unsub { display: flex; flex-wrap: wrap; align-items: center; gap: 0 4px; margin: 8px 0 0 48px; font-size: 12px; line-height: 20px; color: {{subtle}}; }
              .unsub button { padding: 0 4px; border-radius: 4px; color: {{link}}; font-weight: 600; font-size: 12px; line-height: 20px; }
              .unsub button:hover { background: {{hover}}; text-decoration: underline; }
              .unsub button:active { background: {{press}}; }
              .ext { font-size: 11px; margin-left: 6px; padding: 0 6px 1px; border-radius: 4px; border: 1px solid {{rule}}; color: {{subtle}}; vertical-align: 1px; }
              .replyto { font-weight: 600; }
              /* Scheda dell'invito, fra l'intestazione e gli allegati */
              .invite { margin: 10px 0 0 48px; padding: 10px 14px 12px; border: 1px solid {{rule}}; border-left: 3px solid {{link}}; border-radius: 6px; max-width: 640px; }
              .invite.cancelled { border-left-color: {{subtle}}; }
              .invite .title { font-weight: 600; font-size: 15px; margin-bottom: 6px; display: flex; gap: 8px; align-items: baseline; }
              .invite .title .glyph, .answer .glyph { font-family: "Segoe Fluent Icons", "Segoe MDL2 Assets"; font-weight: normal; }
              .invite .title .glyph { color: {{link}}; font-size: 14px; }
              .invite .row { display: flex; gap: 10px; font-size: 13px; line-height: 20px; }
              .invite .k { flex: none; width: 110px; color: {{subtle}}; }
              .invite .v { min-width: 0; overflow-wrap: anywhere; }
              .invite a { color: {{link}}; }
              .answers { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-top: 10px; }
              .answers .note { font-size: 12px; color: {{subtle}}; width: 100%; }
              .answer { display: inline-flex; align-items: center; gap: 6px; padding: 4px 12px; border: 1px solid {{rule}}; border-radius: 4px; font-size: 13px; }
              .answer.primary { border-color: {{link}}; color: {{link}}; font-weight: 600; }
              .answer:hover { background: {{hover}}; }
              .answer:active { background: {{press}}; }
              @media print { .answers { display: none; } }
              /* Barra di scorrimento sottile come quelle di WPF Fluent, senza frecce, invece di quella vecchia di Chromium */
              ::-webkit-scrollbar { width: 8px; height: 8px; background: transparent; }
              ::-webkit-scrollbar-button { display: none; }
              ::-webkit-scrollbar-track, ::-webkit-scrollbar-corner { background: transparent; }
              ::-webkit-scrollbar-thumb { background: {{thumb}}; background-clip: padding-box; border: 2px solid transparent; border-radius: 4px; }
              ::-webkit-scrollbar-thumb:hover { background: {{thumbHover}}; background-clip: padding-box; border: 0; }
              @media print { .subject .act, .banner, .actions, .files .save, .saveall, .unsub { display: none; } }
              .msg { padding: 12px 20px; border-bottom: 1px solid {{rule}}; }
              .msg:last-child { border-bottom: 0; }
              .head { display: flex; align-items: flex-start; gap: 12px; position: relative; isolation: isolate; }
              .who { flex: 1; min-width: 0; display: flex; gap: 12px; padding: 4px; margin: -4px; }
              /* Aprire e chiudere (conversazioni): un pulsante dietro tutta l'intestazione (z-index -1 nel suo contesto: il testo ci sta
                 sopra ma non prende i clic, che arrivano a lui); foto, nome, azioni e i testi con un suggerimento (label verso di lui) sopra. */
              .tog { position: absolute; left: -4px; top: -4px; width: calc(100% + 8px); height: calc(100% + 8px); z-index: -1; border-radius: 6px; cursor: pointer; }
              .tog:hover { background: {{hover}}; }
              .tog:active { background: {{press}}; }
              .tog:focus-visible { outline: 2px solid {{link}}; outline-offset: -2px; }
              .head.tg .who, .head.tg .side { pointer-events: none; }
              .head.tg .pic, .head.tg button.person, .head.tg label, .head.tg .act, .head.tg .edit { pointer-events: auto; }
              .head.tg label { cursor: pointer; }
              .avatar { flex: none; width: 36px; height: 36px; border-radius: 50%; color: #fff; font-weight: 600; font-size: 13px;
                        display: flex; align-items: center; justify-content: center; }
              .avatar.logo { background: #fff; }
              .avatar.logo img { width: 36px; height: 36px; border-radius: 50%; box-sizing: border-box; border: 1px solid rgba(0,0,0,.12); }
              .info { min-width: 0; display: flex; flex-direction: column; justify-content: center; min-height: 36px; }
              .from { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
              .from b { font-weight: 600; }
              button.person, button.pic { cursor: pointer; }
              button.person:hover b { text-decoration: underline; text-underline-offset: 2px; }
              button.person:focus-visible { outline: 2px solid {{link}}; outline-offset: -2px; border-radius: 3px; }
              button.pic:hover { box-shadow: 0 0 0 2px {{link}}; }
              button.pic:focus-visible { outline: 2px solid {{link}}; outline-offset: 2px; }
              .from.pl { display: flex; align-items: center; }
              .from.pl button.person { flex: 0 0 auto; max-width: 100%; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
              .from.pl .addr { flex: 0 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; margin-left: .3em; }
              .from.pl .ext { flex: none; }
              /* Il nome era l'indirizzo di un altro: qui c'è l'indirizzo vero, che va a capo invece di accorciarsi */
              .from.real, .from.real button.person { white-space: normal; overflow-wrap: anywhere; text-align: left; }
              .addr, .line, .date, .size, .status { color: {{subtle}}; }
              .line { font-size: 12px; }
              .preview, .recipients { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
              .side { flex: none; display: flex; flex-direction: column; align-items: flex-end; gap: 2px; }
              .actions { display: flex; }
              .act, .save { font-family: "Segoe Fluent Icons", "Segoe MDL2 Assets"; font-size: 15px; padding: 7px 9px; border-radius: 4px; }
              .cats { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 4px; }
              .cat { font-size: 11px; padding: 1px 7px 2px; border-radius: 4px; }
              .mirror { display: inline-block; transform: scaleX(-1); }
              .all { position: relative; padding-left: 13px; }
              .all .back { position: absolute; left: 5px; width: 7px; overflow: hidden; }
              .act:hover, .save:hover, .chip:hover { background: {{hover}}; }
              .act:active, .save:active, .chip:active { background: {{press}}; }
              .date { font-size: 12px; padding-right: 4px; }
              /* L'email resta nel suo riquadro: position fixed o absolute non esce da .content (transform ne fa il
                 riquadro di riferimento) e quello che sborda sopra o sotto si taglia, così non può coprire mittente e
                 pulsanti veri. Di lato no: un'email ancora troppo larga dopo il rimpicciolimento (FitAsync) fa scorrere di
                 lato la pagina, con la barra in fondo al riquadro come in Outlook, invece di una barra in fondo all'email
                 che nessuno vedeva. Non contain: layout, con cui quello che sborda non conta per lo scorrimento della pagina. */
              .content { margin: 10px 0 4px 48px; display: flow-root; overflow-x: visible; overflow-y: clip; position: relative; transform: translate(0); }
              .status { padding: 8px 0; }
              /* Segnaposto del corpo che tarda (oltre 300 ms): righe grigie invece della scritta "Caricamento" */
              .placeholder { padding: 6px 0; }
              .placeholder span { display: block; height: 10px; margin: 8px 0 14px; border-radius: 5px; background: {{rule}}; max-width: 520px; }
              .placeholder .short { max-width: 300px; }
              .bar { display: inline-block; width: 160px; max-width: 60%; height: 8px; border-radius: 4px; background: {{rule}}; vertical-align: middle; }
              .preview:empty::before { content: "\a0"; }
              /* Allegati fra intestazione e corpo, come schede del nuovo Outlook: icona del tipo, nome e sotto la dimensione */
              .files { margin: 10px 0 0 48px; display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
              .file { display: inline-flex; align-items: stretch; min-width: 0; max-width: 300px; border: 1px solid {{rule}}; border-radius: 6px; }
              .chip { display: inline-flex; align-items: center; gap: 10px; min-width: 0; padding: 5px 10px 5px 8px; border-radius: 5px 0 0 5px; }
              .ficon { flex: none; width: 24px; height: 28px; }
              .chip .glyph { flex: none; width: 24px; text-align: center; font-family: "Segoe Fluent Icons", "Segoe MDL2 Assets"; font-size: 18px; }
              .chip.danger .glyph { color: #C42B1C; }
              .chip .meta { display: flex; flex-direction: column; min-width: 0; line-height: 17px; }
              .chip .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
              .chip .size { font-size: 12px; line-height: 15px; }
              .save { display: flex; align-items: center; border-radius: 0 5px 5px 0; border-left: 1px solid {{rule}}; font-size: 13px; }
              .edit { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; border: 1px solid {{rule}}; border-radius: 4px; font-weight: 600; }
              .edit .glyph { font-family: "Segoe Fluent Icons", "Segoe MDL2 Assets"; font-weight: normal; }
              .edit:hover { background: {{hover}}; }
              .edit:active { background: {{press}}; }
              .saveall { padding: 6px 8px; border-radius: 4px; color: {{link}}; font-weight: 600; font-size: 13px; white-space: nowrap; }
              .saveall:hover { background: {{hover}}; text-decoration: underline; }
              .saveall:active { background: {{press}}; }
              @media (max-width: 600px) { .msg { padding: 12px; } .content, .files, .warn, .invite, .unsub { margin-left: 0; } }
              /* Riquadro stretto: la data va sotto il mittente, i pulsanti restano in alto a destra */
              @media (max-width: 480px) {
                .head { display: grid; grid-template-columns: minmax(0, 1fr) auto; column-gap: 4px; }
                .side { display: contents; }
                .who { grid-column: 1; grid-row: 1; }
                .actions { grid-column: 2; grid-row: 1; align-self: start; }
                .date { grid-column: 1 / -1; grid-row: 2; white-space: nowrap; padding: 2px 0 0 52px; }
              }
            </style></head><body>{{body}}</body></html>
            """;
    }
}
