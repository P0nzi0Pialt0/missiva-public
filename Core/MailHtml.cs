using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Missiva;

/// <summary>
/// Prepara l'HTML di un'email per il riquadro di lettura (WebView2 con JavaScript spento e rete bloccata).
/// Toglie tutto ciò che può eseguire codice o raccogliere dati (script, frame, moduli), blocca le risorse esterne
/// se il mittente non è tra quelli consentiti, collega le immagini incorporate (cid:), raccoglie i link per il
/// controllo antiphishing e, nel tema scuro, adatta i colori come fa Outlook. Il risultato è un frammento da mettere
/// in una shadow root: gli stili dell'email restano isolati da quelli di Missiva e delle altre email.
/// </summary>
public static partial class MailHtml
{
    public static readonly Color DarkPaper = Color.FromRgb(0x29, 0x29, 0x29);
    /// <summary>La luminosità (HSL) di <see cref="DarkPaper"/>, a cui arrivano i fondi bianchi delle email nel tema scuro.</summary>
    static readonly double PaperLightness = DarkPaper.R / 255.0;

    /// <summary>Il nome da cui il riquadro di lettura serve la pagina e le immagini incorporate (ReadingPage.Host).</summary>
    public const string PageHost = "missiva.example";

    static readonly string[] Removed =
    {
        "script", "noscript", "iframe", "frame", "frameset", "object", "embed", "applet", "meta", "link", "base", "title",
        "audio", "video", "source", "track", "portal", "template", "slot", "form", "input", "select", "textarea", "dialog",
        // Tutto quello che segue <plaintext> diventa testo, anche il resto della pagina del riquadro dopo l'email.
        "plaintext",
        // Il loro contenuto è testo grezzo, che il motore non mostra mai (display: none): vedi NeverWritten.
        "noembed", "noframes",
    };

    /// <summary>
    /// Gli elementi che nella pagina del riquadro non si scrivono mai, comunque siano arrivati fino a <see cref="InnerHtml"/>
    /// e in qualunque spazio dei nomi (revisione del 14 ottobre 2026, U-01): template, che chiuderebbe quello della shadow
    /// root dell'email, e quelli che per l'HTML hanno un contenuto grezzo, letto come testo fino al tag di chiusura. Chi
    /// rilegge la pagina non è AngleSharp ma il motore del riquadro, e non sempre nello stesso contesto: un mglyph che
    /// l'analisi ha messo fuori da una tabella dentro un mtext qui è un elemento dell'HTML, e riletto lì è della formula, con
    /// tutto quello che contiene. Il testo grezzo di un xmp al suo interno diventava allora elementi che la pulizia non aveva
    /// mai visto, e il primo era un &lt;/template&gt;: il resto dell'email finiva nella pagina, fuori dalla shadow root, con i
    /// suoi stili sulle fasce vere (misurato nel WebView2 vero, ProvaBozze.Interfaccia.cs). Nell'altro verso, un xmp di un
    /// disegno riletto come quello dell'HTML si chiudeva a metà di un attributo. Senza questi nomi nella pagina dell'email non
    /// c'è contenuto grezzo, tranne il foglio di stile che la apre: ogni tag che il motore legge è uno scritto da qui per un
    /// elemento già pulito, in qualunque contesto lo rilegga. xmp, l'unico che si vede, resta come pre (PaneTag).
    /// </summary>
    static readonly HashSet<string> NeverWritten = new(StringComparer.OrdinalIgnoreCase)
    {
        "template", "script", "style", "title", "textarea", "iframe", "noembed", "noframes", "noscript", "plaintext",
    };

    public sealed record Link(string Href, string Text);

    /// <param name="Fragment">Il frammento per la shadow root dell'email, dopo la guardia finale (<see cref="Sealed"/>).</param>
    /// <param name="PlainOnly">
    /// Il messaggio supera i tetti dell'analisi (<see cref="ParseGuard"/>) ed è mostrato come solo testo: il riquadro lo dice
    /// con una fascia (ReadingPage.Warnings).
    /// </param>
    public sealed record Result(string Fragment, int BlockedImages, List<Link> Links, bool PlainOnly = false)
    {
        /// <summary>
        /// Nasce già passato dalla guardia e non si riassegna: qualunque strada porti a un Result (la pulizia, il solo testo,
        /// il ripiego), nella pagina del riquadro non entra un frammento che non ci sia passato.
        /// </summary>
        public string Fragment { get; } = Sealed(Fragment);
    }

    /// <summary>
    /// La guardia finale sul frammento di un'email, prima che entri nel template della sua shadow root (ReadingPage.Body):
    /// la sequenza "&lt;/template", maiuscola o minuscola, non c'è mai, qualunque cosa abbia fatto la pulizia a monte
    /// (revisione del 14 ottobre 2026, U-01). Dopo la pulizia può stare solo nel valore di un attributo, dove "&amp;lt;" si
    /// legge come lo stesso carattere: il significato non cambia. Un frammento senza quella sequenza, cioè quello di ogni
    /// messaggio normale, torna com'è, la stessa stringa. Sta qui, dove nasce il frammento, e non dove la pagina lo scrive:
    /// si fa una volta per corpo e non a ogni disegno, e chi costruisce la pagina non può dimenticarla.
    /// </summary>
    internal static string Sealed(string fragment)
    {
        const string closing = "</template";
        int at = fragment.IndexOf(closing, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return fragment;
        var safe = new StringBuilder(fragment.Length + 16);
        int copied = 0;
        for (; at >= 0; at = fragment.IndexOf(closing, copied, StringComparison.OrdinalIgnoreCase))
        {
            safe.Append(fragment, copied, at - copied).Append("&lt;");
            copied = at + 1;
        }
        return safe.Append(fragment, copied, fragment.Length - copied).ToString();
    }

    /// <summary>
    /// Il messaggio finto con cui il riquadro riscalda la pulizia prima del primo clic (ReadingView.Prepare, in sottofondo):
    /// stile, tabella, link, immagine incorporata e, dall'audit del 7 ottobre (A-22), un'immagine esterna bloccata con il
    /// nome di uno spaziatore e uno stile con una barra rovesciata, che costruiscono SpacerName e CssEscapedFunction. Prima
    /// le pagava il primo messaggio con immagini esterne (misurato: 11-16 ms la prima, 2 la seconda; 38 ms se è la prima
    /// regex senza backtracking del processo). L'indirizzo è di un dominio che non esiste, e bloccato non si chiede.
    /// </summary>
    internal const string WarmUpSample =
        "<div style=\"color:#202020;background:#ffffff\"><p>Missiva</p><table><tr><td><a href=\"https://example.com\">link</a>" +
        "<img src=\"cid:logo\"><img src=\"https://missiva.invalid/spacer.gif\" width=\"300\"><span style=\"background:u\\72l(x)\">.</span></td></tr></table></div>";

    /// <param name="cidUrl">Indirizzo con cui servire un'immagine incorporata (cid:), o null se non esiste.</param>
    public static Result Prepare(string html, bool dark, bool allowRemote, Func<string, string?> cidUrl)
    {
        try
        {
            return Clean(html, dark, allowRemote, cidUrl);
        }
        catch (TooComplexException)
        {
            // Oltre i tetti dell'analisi: solo il testo, letto senza costruire l'albero, con gli indirizzi web come link.
            var text = Lines(RoughText(html));
            try
            {
                return Clean(FromText(text), dark, allowRemote, cidUrl) with { PlainOnly = true };
            }
            catch (TooComplexException)
            {
                return new Result($"<div class=\"missiva-body\" style=\"white-space: pre-wrap\">{WebUtility.HtmlEncode(text)}</div>", 0, new(), true);
            }
        }
    }

    static Result Clean(string html, bool dark, bool allowRemote, Func<string, string?> cidUrl)
    {
        // Le email con una versione scura propria (come LinkedIn e Amazon) la usano: i loro fogli di stile non si toccano.
        // I colori scritti negli elementi (style, bgcolor, font color) si adattano comunque, come fa il nuovo Outlook:
        // quelle email li lasciano chiari contando sul programma di posta, e sopra ci mettono i colori del loro tema scuro
        // (Amazon: riquadri #F0F2F2 con i link azzurri #6ED6E6, illeggibili).
        bool sheetsDark = dark && !OwnDarkMode().IsMatch(html);

        var dom = Parse(html);
        // Quello che si toglie resta nell'albero e in questo insieme, che il resto salta: per staccare un nodo AngleSharp lo
        // cerca fra i fratelli dal primo, e il costo cresce con il quadrato (100.000 input: 1,2 s invece di 0,5).
        var gone = new HashSet<IElement>(dom.QuerySelectorAll(string.Join(",", Removed)));

        int blocked = 0;
        var links = new List<Link>();
        var styles = new StringBuilder();
        // Elementi il cui testo mantiene il suo colore: quelli con un fondo vivace lasciato com'è (i pulsanti) e ciò che contengono.
        var vivid = new HashSet<IElement>();
        foreach (var e in Elements(dom, gone))
        {
            foreach (var a in e.Attributes.Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ToList())
                e.RemoveAttribute(a.Name);

            // Gli elementi arrivano in ordine di documento: il genitore è già passato di qui e sa se sta dentro un fondo
            // vivace. Prima si risaliva ogni volta fino alla radice: con 20.000 elementi annidati, 23 s invece di 5.
            bool keepText = dark && e.ParentElement is { } parent && vivid.Contains(parent);
            if (AnimatesAddress(e))
            {
                gone.Add(e);
                continue;
            }
            if (e.LocalName == "style")
            {
                styles.Append(Css(TextOf(e, gone), sheetsDark, allowRemote, cidUrl, ref blocked, keepText: false)).Append('\n');
                if (keepText) vivid.Add(e);
                gone.Add(e);
                continue;
            }
            if (dark)
            {
                keepText = keepText || IsVivid(e.GetAttribute("bgcolor")) || IsVivid(BackgroundIn(e.GetAttribute("style")));
                if (keepText) vivid.Add(e);
            }
            if (e.GetAttribute("style") is { Length: > 0 } style)
                e.SetAttribute("style", Css(style, dark, allowRemote, cidUrl, ref blocked, keepText));

            foreach (var name in new[] { "src", "background", "poster" })
            {
                // L'indirizzo come lo legge il motore (KindOf), non come sembra scritto: audit dell'11 ottobre, T-01.
                var value = e.GetAttribute(name);
                if (string.IsNullOrEmpty(value)) continue;
                var kind = KindOf(value);
                if (kind == UrlKind.Cid)
                {
                    if (cidUrl(Unpadded(value)[4..].Trim('<', '>')) is { } url) e.SetAttribute(name, url);
                    else e.RemoveAttribute(name);
                }
                else if (kind == UrlKind.Remote && !allowRemote)
                {
                    bool pixel = e.LocalName == "img" && IsPixel(e);
                    if (!pixel) blocked++;
                    if (name != "src") e.RemoveAttribute(name);
                    // Pixel di tracciamento e spaziatori: un'immagine trasparente, resta lo spazio senza l'icona dell'immagine rotta.
                    else if (pixel || e.LocalName != "img" || IsSpacer(e, value)) e.SetAttribute(name, TransparentPixel);
                    else
                    {
                        // Le altre, come in Outlook, un segnaposto che si vede, non più alto di 160 pixel: trasparenti, alte
                        // quanto l'immagine, lasciavano una schermata bianca (Sky: 600 pixel vuoti prima del testo).
                        e.SetAttribute(name, BlockedImage(e.GetAttribute("alt"), dark));
                        var own = e.GetAttribute("style")?.Trim().TrimEnd(';');
                        // Senza altezza (height:auto, attributo vuoto) il segnaposto, che non ha misure proprie, sarebbe alto
                        // 150 pixel: basso come una riga di testo (64), largo quanto dice l'email.
                        double width = Number(e.GetAttribute("width"));
                        var ratio = Number(e.GetAttribute("height")) > 0 ? ""
                            : $"aspect-ratio:{(width > 0 ? width : 300).ToString(CultureInfo.InvariantCulture)} / 64;";
                        e.SetAttribute("style", (string.IsNullOrEmpty(own) ? "" : own + ";") + ratio + "max-height:160px !important");
                    }
                }
                else if (kind == UrlKind.Other)
                {
                    // javascript:, come prima, e ogni altro schema che non è né un sito né un'immagine dell'email (file:, blob:...).
                    e.RemoveAttribute(name);
                }
            }
            if (!allowRemote && e.HasAttribute("srcset")) e.RemoveAttribute("srcset");

            if (e.LocalName is "a" or "area")
            {
                // href e, nei disegni SVG, xlink:href. Restano solo i link che Missiva sa aprire: gli altri, e soprattutto
                // quelli relativi come "/do/...", si risolverebbero sull'indirizzo della pagina del riquadro. Il link resta
                // con l'indirizzo letto da .NET: il controllo antiphishing (ReadingPage.LinkTexts) cerca così lo stesso
                // indirizzo che il riquadro apre.
                foreach (var a in e.Attributes.Where(a => a.LocalName == "href").ToList())
                {
                    if (LinkTarget(a.Value) is { } href)
                    {
                        a.Value = href;
                        links.Add(new Link(href, WhitespaceRun().Replace(TextOf(e, gone), " ").Trim()));
                    }
                    else e.RemoveAttribute(a.Name);
                }
                e.RemoveAttribute("target"); // i link li apre Missiva nel browser, dopo il controllo
                e.RemoveAttribute("ping"); // richieste di tracciamento al clic
            }
            else
            {
                // Riferimenti dei disegni SVG (image, feImage, use): come le immagini; "#id" punta dentro l'email stessa.
                foreach (var a in e.Attributes.Where(a => a.LocalName == "href").ToList())
                {
                    var kind = KindOf(a.Value);
                    if (kind is UrlKind.Fragment or UrlKind.Data || allowRemote && kind == UrlKind.Remote) continue;
                    if (kind == UrlKind.Cid && cidUrl(Unpadded(a.Value)[4..].Trim('<', '>')) is { } url)
                    {
                        e.SetAttribute(a.Name, url);
                        continue;
                    }
                    if (kind == UrlKind.Remote) blocked++;
                    e.RemoveAttribute(a.Name);
                }
            }

            // Qualunque altro attributo con un indirizzo esterno (cite, fill="url(...)" nei disegni SVG...). Il blocco non
            // deve dipendere dalla pagina: con un altro messaggio consentito aperto accanto, il riquadro lascerebbe passare.
            if (!allowRemote)
                foreach (var a in e.Attributes.Where(a => a.Name is not ("style" or "title" or "alt") && !(a.LocalName == "href" && e.LocalName is "a" or "area")
                                                          && (KindOf(a.Value) == UrlKind.Remote || LoadsFromCss(a.Value) || a.LocalName == "values" && RemoteInList(a.Value))).ToList())
                    e.RemoveAttribute(a.Name);

            if (!dark) continue;
            if (e.GetAttribute("bgcolor") is { Length: > 0 } bg) e.SetAttribute("bgcolor", Recolor(bg, Role.Background));
            if (e.LocalName == "font" && !keepText && e.GetAttribute("color") is { Length: > 0 } fc) e.SetAttribute("color", Recolor(fc, Role.Text));
        }

        // Il corpo dell'email diventa un div: nella shadow root non esiste <body>, e i suoi attributi vanno conservati.
        var body = dom.Body is { } element && !gone.Contains(element) ? element : null;
        var wrapper = new StringBuilder("<div class=\"missiva-body");
        if (body?.GetAttribute("class") is { Length: > 0 } cls) wrapper.Append(' ').Append(WebUtility.HtmlEncode(cls));
        wrapper.Append('"');
        var bodyStyle = new StringBuilder(body?.GetAttribute("style") ?? "");
        // Anche questi passano dalla pulizia dei fogli di stile: bgcolor="red;background-image:url(...)" è CSS a tutti gli effetti.
        var bodyAttributes = new StringBuilder();
        if (body?.GetAttribute("bgcolor") is { Length: > 0 } bodyBg) bodyAttributes.Append(";background-color:").Append(bodyBg);
        if (body?.GetAttribute("text") is { Length: > 0 } bodyText) bodyAttributes.Append(";color:").Append(dark ? Recolor(bodyText, Role.Text) : bodyText);
        if (body?.GetAttribute("background") is { Length: > 0 } bodyImage) bodyAttributes.Append(";background-image:url('").Append(bodyImage.Replace("'", "%27")).Append("')");
        if (bodyAttributes.Length > 0) bodyStyle.Append(Css(bodyAttributes.ToString(), false, allowRemote, cidUrl, ref blocked, keepText: false));
        if (bodyStyle.Length > 0) wrapper.Append(" style=\"").Append(WebUtility.HtmlEncode(bodyStyle.ToString().TrimStart(';'))).Append('"');
        wrapper.Append('>').Append(InnerHtml(body ?? dom.DocumentElement, gone, pane: true)).Append("</div>");

        // I fogli di stile vanno nella pagina come testo grezzo, senza la protezione che l'HTML dà al resto: un "</style>"
        // arrivato dalle entità di un <style> dei disegni SVG, o formato togliendo @import, chiuderebbe il foglio, e quello
        // che segue diventerebbe HTML mai pulito, fuori dalla shadow root e da .content. Nel CSS "</" compare solo in
        // stringhe, url() e commenti, dove \3C/ (il codice di escape di "<") non cambia nulla.
        var css = styles.Length > 0 ? RootSelectors().Replace(styles.ToString(), "$1.missiva-body").Replace("</", "\\3C/") : "";
        var fragment = css.Length > 0 ? $"<style>{css}</style>{wrapper}" : wrapper.ToString();
        return new Result(fragment, blocked, links);
    }

    // ---------------------------------------------------------------- l'originale dentro una risposta o un inoltro

    /// <summary>
    /// Quello che una citazione non porta, con tutto il contenuto: ciò che esegue, i frame, gli oggetti, i campi di un
    /// modulo, e dalle scelte del 14 ottobre 2026 (U-01) i template: la visita di <see cref="Outgoing"/> non entra nel loro
    /// contenuto, che ripartiva non pulito (inerte per chi riceve, ma con script e attributi di evento com'erano).
    /// </summary>
    static readonly HashSet<string> NotQuoted = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "iframe", "frame", "frameset", "noframes", "object", "embed", "noembed", "applet", "portal", "input", "select", "textarea", "template",
    };

    /// <summary>Gli attributi che portano un indirizzo (href anche con uno spazio dei nomi: xlink:href nei disegni SVG).</summary>
    static readonly HashSet<string> AddressAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "action", "formaction", "background", "poster", "data", "codebase", "cite", "longdesc", "lowsrc", "dynsrc", "manifest", "usemap", "classid", "archive",
    };

    /// <summary>
    /// L'HTML di un messaggio ricevuto come può ripartire dentro la citazione di una risposta o di un inoltro (Gmail, IMAP,
    /// file .eml; con Graph la citazione la fa il server): senza quello che esegue o invia (revisione del 13 ottobre 2026,
    /// K-04). Prima ripartiva com'era arrivato, con script, moduli, attributi di evento e link javascript:. Non è la
    /// pulizia del riquadro (<see cref="Prepare"/>), che blocca le immagini esterne e riscrive stili e colori: un inoltro
    /// deve arrivare al destinatario com'era l'originale, immagini comprese, anche quelle di un pixel (lo fa ogni
    /// programma di posta). Si tolgono: script, frame, oggetti, template e i campi di un modulo, con il contenuto; il
    /// modulo e il pulsante, lasciando quello che contengono; il meta che manda a un'altra pagina; gli attributi di evento
    /// (on...); gli indirizzi che non sono http, https, mailto, tel, cid o un'immagine data: (quelli senza schema restano:
    /// non portano da nessuna parte); le animazioni dei disegni che cambiano un indirizzo. Il resto non si tocca: se non
    /// c'è nulla da togliere torna la stessa stringa, e la citazione di un messaggio normale resta quella di prima, byte per
    /// byte.
    /// Solo quando c'è qualcosa da togliere l'HTML si riscrive dall'albero. Oltre i tetti dell'analisi
    /// (<see cref="ParseGuard"/>) va il solo testo, come lo mostra il riquadro.
    /// </summary>
    public static string Outgoing(string html)
    {
        AngleSharp.Html.Dom.IHtmlDocument dom;
        try
        {
            dom = Parse(html);
        }
        catch (TooComplexException)
        {
            return FromText(Lines(RoughText(html)));
        }
        HashSet<IElement> gone = new(), bare = new();
        bool changed = false;
        var stack = new Stack<INode>();
        stack.Push(dom);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is IElement e)
            {
                var name = e.LocalName;
                if (NotQuoted.Contains(name) || AnimatesAddress(e)
                    || name.Equals("meta", StringComparison.OrdinalIgnoreCase) && (e.GetAttribute("http-equiv")?.Trim().Equals("refresh", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    gone.Add(e);
                    changed = true;
                    continue;
                }
                if (name.Equals("form", StringComparison.OrdinalIgnoreCase) || name.Equals("button", StringComparison.OrdinalIgnoreCase))
                {
                    bare.Add(e);
                    changed = true;
                }
                else if (e.Attributes.Length > 0)
                {
                    foreach (var a in e.Attributes.Where(a => !QuotedAttribute(e, a)).ToList())
                    {
                        e.RemoveAttribute(a.NamespaceUri, a.LocalName);
                        changed = true;
                    }
                }
            }
            var children = node.ChildNodes;
            for (int i = children.Length - 1; i >= 0; i--)
                if (children[i] is IElement) stack.Push(children[i]);
        }
        return changed ? InnerHtml(dom, gone, bare) : html;
    }

    /// <summary>L'attributo può restare in una citazione: non è un attributo di evento, e se porta un indirizzo è di quelli che non eseguono.</summary>
    static bool QuotedAttribute(IElement e, IAttr a)
    {
        if (a.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)) return false;
        if (a.LocalName.Equals("srcset", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in a.Value.Split(','))
                if (!QuotedAddress(candidate.Trim().Split(' ', '\t', '\n', '\r', '\f')[0], link: false)) return false;
            return true;
        }
        if (!AddressAttributes.Contains(a.LocalName)) return true;
        return QuotedAddress(a.Value, link: e.LocalName is "a" or "area");
    }

    /// <summary>
    /// L'indirizzo può restare in una citazione, letto come lo legge un motore (<see cref="KindOf(string?)"/>): il web, cid:,
    /// mailto: e tel:, quelli senza schema, e un'immagine data: dove si carica un'immagine, non dove si apre un link.
    /// </summary>
    static bool QuotedAddress(string value, bool link) => KindOf(value) switch
    {
        UrlKind.Data => !link && Unpadded(value).StartsWith("data:image/", StringComparison.OrdinalIgnoreCase),
        UrlKind.Other => Unpadded(value) is var read && (read.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || read.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)),
        _ => true,
    };

    /// <summary>
    /// Il testo di un'email HTML, per la parte di solo testo delle risposte: gli spazi come li mostra il browser, un a
    /// capo per ogni br e per ogni paragrafo, riga o cella di tabella.
    /// </summary>
    public static string ToText(string html)
    {
        AngleSharp.Html.Dom.IHtmlDocument dom;
        try
        {
            dom = Parse(html);
        }
        catch (TooComplexException)
        {
            return Lines(RoughText(html));
        }
        // Una visita sola con una pila, senza toccare l'albero. Prima il testo si prendeva con TextContent, che in AngleSharp
        // è ricorsivo (8.000 elementi annidati finivano lo stack e chiudevano Missiva), dopo aver cercato un pre fra gli
        // antenati di ogni testo e sostituito ogni br, che AngleSharp cerca fra i fratelli (100.000 righe con br: 128 s).
        var text = new StringBuilder();
        if (dom.Body is { } body)
        {
            // (nodo, dentro un pre, uscita): all'uscita di un elemento lo spazio delle celle e l'a capo dei blocchi.
            var stack = new Stack<(INode Node, bool Pre, bool Exit)>();
            stack.Push((body, false, false));
            while (stack.Count > 0)
            {
                var (node, pre, exit) = stack.Pop();
                if (node is IText t)
                {
                    text.Append(pre ? t.Data : WhitespaceRun().Replace(t.Data, " "));
                    continue;
                }
                if (node is not IElement e) continue;
                var name = e.LocalName;
                if (exit)
                {
                    if (name is "td" or "th") text.Append(' ');
                    if (TextBlocks.Contains(name)) text.Append('\n');
                    continue;
                }
                if (name is "script" or "style" or "head" or "title") continue;
                if (name == "br")
                {
                    text.Append('\n');
                    continue;
                }
                pre = pre || name == "pre";
                stack.Push((e, pre, true));
                var children = e.ChildNodes;
                for (int i = children.Length - 1; i >= 0; i--) stack.Push((children[i], pre, false));
            }
        }
        return Lines(text.ToString());
    }

    /// <summary>Gli elementi che in <see cref="ToText"/> finiscono con un a capo.</summary>
    static readonly HashSet<string> TextBlocks = new(StringComparer.OrdinalIgnoreCase) { "p", "div", "tr", "li", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "table", "hr" };
    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> TextBlockNames = TextBlocks.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Le righe del testo senza spazi attorno (quelli unificatori diventano normali), al più una riga vuota di fila.</summary>
    static string Lines(string text)
    {
        var lines = text.Replace('\u00A0', ' ').Split('\n').Select(l => l.Trim());
        return BlankLines().Replace(string.Join("\n", lines), "\n\n").Trim();
    }

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    // ---------------------------------------------------------------- analisi con i tetti

    /// <summary>
    /// Livelli di annidamento. Il motore di WebView2 tiene 512 elementi aperti e oltre attacca i nodi come fratelli (con Edge
    /// 154 l'albero non supera i 513 livelli): un messaggio più profondo già non si vedrebbe com'è scritto.
    /// </summary>
    const int MaxDepth = 512;
    /// <summary>Elementi di un messaggio, compresi quelli che l'analisi riapre da sola: non più di due per carattere, e mai più di questi.</summary>
    const int MaxElements = 250_000;
    /// <summary>Passi dell'analisi (un gettone per la profondità a cui arriva): a ogni tag AngleSharp ripercorre gli elementi aperti.</summary>
    const long MaxWork = 16_000_000;
    /// <summary>Attributi di un solo tag: Missiva e AngleSharp li cercano per nome uno alla volta.</summary>
    const int MaxAttributes = 256;
    /// <summary>Memoria che l'analisi può chiedere: una base più tanto per carattere (le email vere stanno sotto 31 byte per carattere).</summary>
    const long BaseBytes = 8 << 20, BytesPerChar = 1024;

    /// <summary>Il messaggio supera i tetti dell'analisi: si mostra come solo testo. Nel messaggio dell'eccezione, quale tetto.</summary>
    sealed class TooComplexException(string limit) : Exception(limit);

    /// <summary>
    /// L'albero dell'HTML, costruito da AngleSharp sotto i tetti di <see cref="ParseGuard"/>; se li supera lancia
    /// <see cref="TooComplexException"/> prima di aver speso tempo e memoria.
    /// </summary>
    static AngleSharp.Html.Dom.IHtmlDocument Parse(string html)
    {
        var guard = new ParseGuard(html.Length);
        return new HtmlParser(new HtmlParserOptions { OnToken = guard.Token, OnCreated = guard.Created }).ParseDocument(EscapeStrayLt(html));
    }

    /// <summary>
    /// Lo stesso albero, sotto gli stessi tetti, per l'altro HTML che arriva da fuori (le pagine dei siti da cui SenderLogos
    /// legge le icone dichiarate); null se li supera.
    /// </summary>
    internal static AngleSharp.Html.Dom.IHtmlDocument? ParseWithinLimits(string html)
    {
        try
        {
            return Parse(html);
        }
        catch (TooComplexException)
        {
            return null;
        }
    }

    /// <summary>
    /// I tetti dell'analisi. Il costo di AngleSharp cresce con il quadrato in più casi, e un'email di poche decine di KB
    /// costruita apposta fermava la pulizia per secondi o minuti e chiedeva gigabyte: elementi annidati a migliaia (a ogni
    /// tag ripercorre quelli aperti), elementi di formato mai chiusi (li riapre tutti in ogni paragrafo: 2.000 font, 2
    /// milioni di elementi), testo spezzato da tag che non contano (lo ricopia a ogni pezzo), migliaia di attributi in un
    /// tag. Qui si conta mentre l'analisi procede, sull'albero vero: un messaggio normale non ci arriva mai vicino.
    /// </summary>
    sealed class ParseGuard(int length)
    {
        readonly long maxElements = Math.Min(10_000 + 2L * length, MaxElements);
        readonly long maxBytes = GC.GetAllocatedBytesForCurrentThread() + BaseBytes + BytesPerChar * length;
        IElement? last;
        int depth, tokens;
        long elements, work;

        public void Token(AngleSharp.Html.Parser.Tokens.HtmlToken token, AngleSharp.Text.TextRange range)
        {
            if (token is AngleSharp.Html.Parser.Tokens.HtmlTagToken tag && tag.Attributes.Count > MaxAttributes) throw new TooComplexException("attributi");
            work += depth;
            if (work > MaxWork) throw new TooComplexException("passi");
            if ((++tokens & 63) == 0 && GC.GetAllocatedBytesForCurrentThread() > maxBytes) throw new TooComplexException("memoria");
        }

        public void Created(IElement element, AngleSharp.Text.TextPosition position)
        {
            // L'elemento appena creato non è ancora nell'albero: si misura quello di prima, che ora c'è. Sopra ha gli antenati,
            // sotto la catena degli elementi di formato che l'analisi gli ha riaperto dentro senza passare di qui.
            if (last != null)
            {
                int up = 0, down = 0;
                for (INode? node = last; node != null && up <= MaxDepth; node = node.Parent) up++;
                for (var node = last.LastChild; node is IElement && down <= MaxDepth; node = node.LastChild) down++;
                depth = up + down;
                elements += down;
            }
            last = element;
            if (depth > MaxDepth) throw new TooComplexException("profondità");
            if (++elements > maxElements) throw new TooComplexException("elementi");
        }
    }

    /// <summary>
    /// Un "&lt;" del testo che non apre un tag ("a &lt; b", "&lt;&lt;", "&lt;3") scritto come "&amp;lt;": per l'HTML è lo stesso
    /// carattere, ma AngleSharp a ognuno spezza il testo e poi lo ricopia pezzo per pezzo (100.000 in 98 KB: 1,2 s e 9,5 GB).
    /// Solo dove l'analisi lo leggerebbe come testo: non dentro i tag, i commenti e gli elementi dal contenuto grezzo, dove
    /// "&amp;lt;" resterebbe scritto così. Nel dubbio non si tocca: resta soltanto più lento. E solo se sono più di
    /// <paramref name="few"/>: pochi non costano nulla, e lasciando l'HTML com'è l'albero resta di sicuro lo stesso (con i
    /// pezzi uniti, AngleSharp mette fuori da una tabella anche gli spazi attorno a un "&lt;" scritto fra le sue righe).
    /// </summary>
    static string EscapeStrayLt(string html, int few = 64)
    {
        List<int>? stray = null;
        for (int i = html.IndexOf('<'); i >= 0; i = i < html.Length ? html.IndexOf('<', i) : -1)
        {
            bool closing = i + 1 < html.Length && html[i + 1] == '/';
            int name = i + (closing ? 2 : 1);
            if (name < html.Length && char.IsAsciiLetter(html[name]))
            {
                int nameEnd = TagNameEnd(html, name);
                i = TagEnd(html, nameEnd);
                if (!closing && RawText.Contains(html.AsSpan(name, nameEnd - name))) i = RawTextEnd(html, i, html.AsSpan(name, nameEnd - name));
            }
            else if (DeclarationEnd(html, i) is >= 0 and var end) i = end;
            else (stray ??= new()).Add(i++);
        }
        if (stray == null || stray.Count <= few) return html;
        var escaped = new StringBuilder(html.Length + 3 * stray.Count);
        int copied = 0;
        foreach (int at in stray)
        {
            escaped.Append(html, copied, at - copied).Append("&lt;");
            copied = at + 1;
        }
        return escaped.Append(html, copied, html.Length - copied).ToString();
    }

    /// <summary>Elementi il cui contenuto l'analisi legge come testo fino al tag di chiusura (in più noscript, per prudenza).</summary>
    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> RawText = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "title", "textarea", "xmp", "iframe", "noembed", "noframes", "noscript", "plaintext" }.GetAlternateLookup<ReadOnlySpan<char>>();

    static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    static int TagNameEnd(string html, int i)
    {
        while (i < html.Length && !IsSpace(html[i]) && html[i] is not ('/' or '>')) i++;
        return i;
    }

    /// <summary>
    /// La fine di un tag, dopo il suo "&gt;", dalla fine del nome: come la trova l'analisi dell'HTML, per cui un "&gt;" fra
    /// le virgolette del valore di un attributo non lo chiude. Un tag mai chiuso arriva in fondo.
    /// </summary>
    static int TagEnd(string html, int i)
    {
        while (i < html.Length)
        {
            char c = html[i];
            if (c == '>') return i + 1;
            if (IsSpace(c) || c == '/')
            {
                i++;
                continue;
            }
            // Il nome di un attributo (un "=" all'inizio ne fa parte), poi se c'è il suo valore.
            i++;
            while (i < html.Length && !IsSpace(html[i]) && html[i] is not ('/' or '>' or '=')) i++;
            while (i < html.Length && IsSpace(html[i])) i++;
            if (i >= html.Length || html[i] != '=') continue;
            i++;
            while (i < html.Length && IsSpace(html[i])) i++;
            if (i >= html.Length) break;
            if (html[i] is '"' or '\'')
            {
                int close = html.IndexOf(html[i], i + 1);
                if (close < 0) break;
                i = close + 1;
            }
            else while (i < html.Length && !IsSpace(html[i]) && html[i] != '>') i++;
        }
        return html.Length;
    }

    /// <summary>Dove comincia il tag che chiude un elemento dal contenuto grezzo aperto prima di from; la fine del testo se non c'è.</summary>
    static int RawTextEnd(string html, int from, ReadOnlySpan<char> name)
    {
        if (name.Equals("plaintext", StringComparison.OrdinalIgnoreCase)) return html.Length;
        for (int i = from; i < html.Length && (i = html.IndexOf("</", i, StringComparison.Ordinal)) >= 0; i += 2)
        {
            int end = i + 2 + name.Length;
            if (end <= html.Length && html.AsSpan(i + 2, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase)
                && (end == html.Length || IsSpace(html[end]) || html[end] is '/' or '>'))
                return i;
        }
        return html.Length;
    }

    /// <summary>
    /// Dove finisce ciò che comincia con il "&lt;" in posizione i e non è un tag: un commento, il DOCTYPE, una sezione CDATA,
    /// un commento fasullo ("&lt;?...&gt;", "&lt;/ &gt;"). -1 se è un "&lt;" del testo.
    /// </summary>
    static int DeclarationEnd(string html, int i)
    {
        int After(string end, int from)
        {
            int at = html.IndexOf(end, from, StringComparison.Ordinal);
            return at < 0 ? html.Length : at + end.Length;
        }
        char next = i + 1 < html.Length ? html[i + 1] : '\0';
        if (next is '?' or '/') return After(">", i + 2);
        if (next != '!') return -1;
        var rest = html.AsSpan(i + 2);
        // "<!-->" e "<!--->" sono commenti già chiusi: la ricerca parte dai trattini di apertura.
        return rest.StartsWith("--") ? After("-->", i + 2) : rest.StartsWith("[CDATA[") ? After("]]>", i + 9) : After(">", i + 2);
    }

    /// <summary>Gli elementi dell'albero in ordine di documento, come dom.All, senza quelli tolti e ciò che contengono.</summary>
    static List<IElement> Elements(INode root, HashSet<IElement> gone)
    {
        var list = new List<IElement>();
        var stack = new Stack<INode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is IElement e)
            {
                if (gone.Contains(e)) continue;
                list.Add(e);
            }
            var children = node.ChildNodes;
            for (int i = children.Length - 1; i >= 0; i--)
                if (children[i] is IElement) stack.Push(children[i]);
        }
        return list;
    }

    /// <summary>
    /// Il testo dentro un nodo, come TextContent di AngleSharp ma con una pila al posto della ricorsione (un link con 8.000
    /// elementi annidati finiva lo stack e chiudeva Missiva), e senza gli elementi tolti.
    /// </summary>
    static string TextOf(INode root, HashSet<IElement> gone)
    {
        var text = new StringBuilder();
        var stack = new Stack<INode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is IText t)
            {
                text.Append(t.Data);
                continue;
            }
            var children = node.ChildNodes;
            for (int i = children.Length - 1; i >= 0; i--)
                if (children[i] is IText || children[i].HasChildNodes && !(children[i] is IElement e && gone.Contains(e))) stack.Push(children[i]);
        }
        return text.ToString();
    }

    /// <summary>
    /// L'HTML del contenuto di un nodo, come InnerHtml di AngleSharp (Node.ToHtml), senza gli elementi tolti e in una
    /// passata: AngleSharp a ogni foglia chiede il fratello successivo, che cerca fra i figli del genitore dal primo
    /// (un messaggio di 100.000 righe chiuse da br: 16 s di pulizia, ora 0,3).
    /// </summary>
    /// <param name="bare">Gli elementi di cui resta solo il contenuto, senza il loro tag (<see cref="Outgoing"/>: un modulo, un pulsante).</param>
    /// <param name="pane">
    /// Per la pagina del riquadro (<see cref="Clean"/>), che il motore rilegge dentro il template di una shadow root: senza
    /// commenti, che lì non servono a niente (quelli condizionali di Outlook il motore li ignora) e sono la strada classica
    /// per cui due analisi dello stesso testo non coincidono; senza gli elementi di <see cref="NeverWritten"/> e con xmp
    /// scritto come pre (<see cref="PaneTag"/>); e tutto il testo scritto come testo, mai grezzo. Falso per l'HTML che
    /// riparte in una risposta o in un inoltro (<see cref="Outgoing"/>): lì i commenti condizionali devono restare, o una
    /// newsletter inoltrata cambia aspetto in Outlook. Un template non si scrive in nessuno dei due: nel riquadro è fra
    /// <see cref="NeverWritten"/>, in uscita fra gli elementi tolti (NotQuoted, dalle scelte del 14 ottobre 2026). Il ramo
    /// che ne scriveva il contenuto non c'è più: un template che arrivasse fin qui si scriverebbe vuoto.
    /// </param>
    static string InnerHtml(INode root, HashSet<IElement> gone, HashSet<IElement>? bare = null, bool pane = false)
    {
        var formatter = AngleSharp.Html.HtmlMarkupFormatter.Instance;
        var html = new StringBuilder();
        var stack = new Stack<(INode Node, bool Close)>();
        void Push(INode parent)
        {
            var children = parent.ChildNodes;
            for (int i = children.Length - 1; i >= 0; i--)
                if (!(children[i] is IElement e && gone.Contains(e))) stack.Push((children[i], false));
        }
        Push(root);
        while (stack.Count > 0)
        {
            var (node, close) = stack.Pop();
            switch (node)
            {
                case IElement e when close:
                    html.Append(pane && PaneTag(e) is { } renamed ? $"</{renamed}>" : formatter.CloseTag(e, e.Flags.HasFlag(NodeFlags.SelfClosing)));
                    break;
                case IComment or IProcessingInstruction or IDocumentType when pane:
                    break; // nella pagina del riquadro nessun commento (e nessuna dichiarazione, che l'analisi dell'HTML non crea)
                case IComment comment:
                    html.Append(formatter.Comment(comment));
                    break;
                case IProcessingInstruction instruction:
                    html.Append(formatter.Processing(instruction));
                    break;
                case ICharacterData data:
                    html.Append(!pane && (data.Parent?.Flags.HasFlag(NodeFlags.LiteralText) ?? false) ? formatter.LiteralText(data) : formatter.Text(data));
                    break;
                case IDocumentType type:
                    html.Append(formatter.Doctype(type));
                    break;
                case IElement e when bare != null && bare.Contains(e):
                    Push(e);
                    break;
                case IElement e when pane && (NeverWritten.Contains(e.LocalName) || e.LocalName.Equals("xmp", StringComparison.OrdinalIgnoreCase) && PaneTag(e) == null):
                    break; // con tutto quello che contiene
                case IElement e:
                    bool selfClosing = e.Flags.HasFlag(NodeFlags.SelfClosing);
                    var open = formatter.OpenTag(e, selfClosing);
                    // xmp come pre: cambia il nome, gli attributi restano; quello che conteneva, scritto come testo, si vede uguale.
                    bool asPre = pane && PaneTag(e) != null;
                    if (asPre && !open.StartsWith("<xmp", StringComparison.Ordinal)) break; // un xmp dell'HTML si scrive sempre così: altrimenti nulla
                    if (asPre) html.Append("<pre").Append(open, "<xmp".Length, open.Length - "<xmp".Length);
                    else html.Append(open);
                    stack.Push((e, true));
                    int before = stack.Count;
                    Push(e);
                    // pre, textarea e listing: chi rilegge l'HTML toglie il primo a capo, quindi se il testo comincia con uno se ne scrive un altro.
                    if (!selfClosing && (asPre || e.Flags.HasFlag(NodeFlags.LineTolerance)) && stack.Count > before && stack.Peek().Node is IText first && first.Data.StartsWith('\n'))
                        html.Append('\n');
                    break;
            }
        }
        return html.ToString();
    }

    /// <summary>
    /// Il nome con cui un xmp dell'HTML si scrive nella pagina del riquadro: pre, che il motore disegna allo stesso modo
    /// (blocco, carattere a larghezza fissa, spazi e a capo tenuti) ma legge come testo normale e non come contenuto grezzo;
    /// null per ogni altro elemento. Un xmp dentro un disegno o una formula non è quello dell'HTML e non si scrive affatto.
    /// </summary>
    static string? PaneTag(IElement e) => e is AngleSharp.Html.Dom.IHtmlElement && e.LocalName == "xmp" ? "pre" : null;

    /// <summary>
    /// Il testo di un messaggio senza costruirne l'albero, per quelli oltre i tetti dell'analisi: una passata sui tag. Niente
    /// script, fogli di stile e altri contenuti grezzi; un a capo per br e attorno ai blocchi, uno spazio dopo le celle.
    /// </summary>
    static string RoughText(string html)
    {
        var text = new StringBuilder();
        int pre = 0;
        for (int i = 0; i < html.Length;)
        {
            int lt = html.IndexOf('<', i);
            if (lt < 0) lt = html.Length;
            if (lt > i)
            {
                var piece = WebUtility.HtmlDecode(html[i..lt]);
                text.Append(pre > 0 ? piece : WhitespaceRun().Replace(piece, " "));
            }
            if (lt >= html.Length) break;
            bool closing = lt + 1 < html.Length && html[lt + 1] == '/';
            int start = lt + (closing ? 2 : 1);
            if (start >= html.Length || !char.IsAsciiLetter(html[start]))
            {
                i = DeclarationEnd(html, lt);
                if (i >= 0) continue;
                text.Append('<');
                i = lt + 1;
                continue;
            }
            int nameEnd = TagNameEnd(html, start);
            var name = html.AsSpan(start, nameEnd - start);
            i = TagEnd(html, nameEnd);
            if (!closing && RawText.Contains(name))
            {
                i = RawTextEnd(html, i, name);
                continue;
            }
            if (name.Equals("br", StringComparison.OrdinalIgnoreCase)) text.Append('\n');
            else if (name.Equals("pre", StringComparison.OrdinalIgnoreCase)) pre = Math.Max(0, pre + (closing ? -1 : 1));
            else if (closing && (name.Equals("td", StringComparison.OrdinalIgnoreCase) || name.Equals("th", StringComparison.OrdinalIgnoreCase))) text.Append(' ');
            if (TextBlockNames.Contains(name) && text.Length > 0 && text[^1] != '\n') text.Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// Il testo di un'email da leggere, come l'originale sotto una risposta: quello di ToText, senza i caratteri invisibili
    /// che le newsletter usano per allungare l'anteprima (LinkedIn ne mette righe intere).
    /// </summary>
    public static string ReadableText(string html)
    {
        var lines = Invisible().Replace(ToText(html), "").Split('\n').Select(l => l.Trim());
        return BlankLines().Replace(string.Join("\n", lines), "\n\n").Trim();
    }

    /// <summary>Testo semplice (email senza HTML), con gli indirizzi web trasformati in link.</summary>
    public static string FromText(string text)
    {
        var encoded = WebUtility.HtmlEncode(text.Replace("\r\n", "\n"));
        var linked = UrlInText().Replace(encoded, m => $"<a href=\"{m.Value}\">{m.Value}</a>");
        return $"<div style=\"white-space: pre-wrap\">{linked}</div>";
    }

    /// <summary>
    /// L'anteprima dell'elenco solo come testo, come nel nuovo Outlook: senza i segnaposto delle immagini della parte di
    /// testo ("[image: Google]"), senza indirizzi web (restano i testi dei link scritti come [testo](indirizzo)), senza i
    /// caratteri invisibili che le newsletter usano per allungare l'anteprima, con gli spazi ridotti a uno. Le entità HTML
    /// scritte nel testo si decodificano prima: Subito mandava "Riepilogo annunci &amp;zwnj; &amp;zwnj;", che il nuovo Outlook
    /// mostra come caratteri invisibili (e quindi toglie).
    /// </summary>
    public static string PlainPreview(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        // L'anteprima di Gmail è tagliata a una lunghezza fissa, anche a metà di un'entità ("annunci &zw…"): quel pezzo si toglie.
        var clean = text.Contains('&') ? CutEntity().Replace(WebUtility.HtmlDecode(text), "") : text;
        clean = ImagePlaceholder().Replace(clean, " ");
        clean = MarkdownLink().Replace(clean, "$1");
        clean = CutMarkdownLink().Replace(clean, "$1");
        clean = AngleMailto().Replace(clean, "$1");
        clean = WebAddress().Replace(clean, " ");
        clean = Invisible().Replace(clean, "");
        // Un "<" o un ">" rimasto solo all'inizio o alla fine, da un indirizzo tagliato; "< 100 euro" in mezzo resta.
        return EdgeBracket().Replace(WhitespaceRun().Replace(clean, " ").Trim(), "").Trim();
    }

    /// <summary>
    /// Un'entità tagliata in fondo (minuscole come &amp;zwnj;, o numerica), prima dei puntini che MailKit aggiunge a
    /// un'anteprima tagliata: "AT&amp;T" resta com'è.
    /// </summary>
    [GeneratedRegex(@"&(?:#x?[0-9A-Fa-f]{0,6}|[a-z][a-z0-9]{0,9})?(?=…?$)")]
    private static partial Regex CutEntity();

    [GeneratedRegex(@"\[image:[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex ImagePlaceholder();

    [GeneratedRegex(@"\[([^\]]*)\]\(\s*(?:https?://|mailto:)[^)\s]*\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownLink();

    /// <summary>
    /// Un link [testo](indirizzo) tagliato in fondo dall'anteprima di Gmail, senza la parentesi chiusa (e magari a metà
    /// dell'indirizzo): resta il testo, come per il link intero. Sky tg24 Insider mostrava "[Leggi da browser](".
    /// </summary>
    [GeneratedRegex(@"\[([^\]]*)\]\([^)\s]*(?=…?$)")]
    private static partial Regex CutMarkdownLink();

    /// <summary>
    /// Un indirizzo web, anche fra parentesi o fra &lt; e &gt; come nelle parti di testo, e tagliato in fondo (anche senza il
    /// &gt; finale: le newsletter che cominciano con "&lt;https://..." lasciavano "&lt;" come anteprima).
    /// </summary>
    [GeneratedRegex(@"\(\s*https?://[^)\s]*\s*\)|<\s*https?://[^>\s]*\s*>?|https?://\S*", RegexOptions.IgnoreCase)]
    private static partial Regex WebAddress();

    /// <summary>Un indirizzo email fra &lt; e &gt; con mailto: (le parti di testo delle newsletter): resta l'indirizzo.</summary>
    [GeneratedRegex(@"<\s*mailto:([^>\s]+)\s*>?", RegexOptions.IgnoreCase)]
    private static partial Regex AngleMailto();

    /// <summary>Un "&lt;" o un "&gt;" da solo all'inizio o alla fine del testo.</summary>
    [GeneratedRegex(@"^[<>](?=\s|$)|(?<=\s)[<>]$")]
    private static partial Regex EdgeBracket();

    [GeneratedRegex(@"[\u00AD\u034F\u061C\u115F\u1160\u17B4\u17B5\u180E\u200B-\u200F\u202A-\u202E\u2060-\u206F\u3164\uFEFF\uFFA0]")]
    private static partial Regex Invisible();

    const string TransparentPixel = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    /// <summary>
    /// Il segnaposto di un'immagine bloccata, come in Outlook senza le immagini: fondo grigio tenue con il bordo e gli angoli
    /// arrotondati, l'icona di un'immagine e il testo alternativo, se c'è. Un disegno SVG senza misure proprie: prende quella
    /// che l'email dà all'immagine (attributi, stili) e non si deforma, perché è tutto in percentuali dal centro.
    /// </summary>
    static string BlockedImage(string? alt, bool dark)
    {
        var (fill, stroke, ink) = dark ? ("#333333", "#474747", "#BDBDBD") : ("#F3F3F3", "#E0E0E0", "#707070");
        var text = WhitespaceRun().Replace(alt ?? "", " ").Trim();
        if (text.Length > 60) text = text[..59] + "…";
        // Il bordo è il tratto di 2 pixel del rettangolo, per metà fuori dal disegno: se ne vede 1.
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100%\" height=\"100%\">")
            .Append($"<rect width=\"100%\" height=\"100%\" rx=\"4\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"2\"/>")
            .Append("<svg x=\"50%\" y=\"50%\" overflow=\"visible\">")
            .Append($"<g transform=\"translate(-10 {(text.Length > 0 ? -19 : -10)})\" fill=\"none\" stroke=\"{ink}\" stroke-width=\"1.3\">")
            .Append("<rect x=\"2\" y=\"3\" width=\"16\" height=\"14\" rx=\"2\"/><path d=\"M4.5 15l4-4.5 3 3 2-2 2.5 3.5\"/><circle cx=\"13\" cy=\"7.5\" r=\"1.5\"/></g>");
        if (text.Length > 0)
            svg.Append($"<text y=\"15\" text-anchor=\"middle\" font-family=\"Segoe UI, sans-serif\" font-size=\"12\" fill=\"{ink}\">{WebUtility.HtmlEncode(text)}</text>");
        svg.Append("</svg></svg>");
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString()));
    }

    /// <summary>
    /// Spaziatori: immagini sottili (fino a 8 pixel di lato) o con il nome di un'immagine trasparente. Bloccati restano
    /// trasparenti: come segnaposto sarebbero righe grigie in mezzo all'email.
    /// </summary>
    static bool IsSpacer(IElement e, string src) =>
        Number(e.GetAttribute("width")) is > 0 and <= 8 || Number(e.GetAttribute("height")) is > 0 and <= 8 || SpacerName().IsMatch(src);

    // NonBacktracking qui e sotto: il motore che torna indietro riparte da ogni posizione e rilegge fino in fondo, quindi su
    // una ripetizione senza chiusura costa il quadrato ("pixel" 20.000 volte nell'indirizzo di un'immagine 1,1 s, "<meta "
    // 10.000 volte 1,7 s). Questo legge il testo una volta sola e trova le stesse corrispondenze.
    [GeneratedRegex(@"(?:spacer|blank|transparent|pixel|clear)[\w-]*\.(?:gif|png)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex SpacerName();

    [GeneratedRegex(@"prefers-color-scheme\s*:\s*dark|<meta[^>]+(?:supported-)?color-scheme[^>]+dark", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex OwnDarkMode();

    /// <summary>
    /// L'indirizzo di un link, se Missiva sa aprirlo (assoluto verso il web o mailto:, mai verso la pagina del riquadro di
    /// lettura), altrimenti null. Letto come lo legge Chromium: senza tabulazioni e a capo, con "\" al posto di "/" prima
    /// di ? e # negli indirizzi web. Questi si riscrivono come li scrive .NET (Uri.AbsoluteUri); quelli che .NET non sa
    /// leggere si tolgono, perché il controllo dei link ingannevoli non li vedrebbe mentre Chromium li aprirebbe.
    /// </summary>
    static string? LinkTarget(string href)
    {
        var clean = href.Replace("\t", "").Replace("\r", "").Replace("\n", "").Trim().Trim(ControlChars);
        if (clean.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return clean;
        if (!(clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return null;
        int rest = clean.IndexOfAny(['?', '#']);
        if (rest < 0) rest = clean.Length;
        clean = clean[..rest].Replace('\\', '/') + clean[rest..];
        if (!Uri.TryCreate(clean, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        // L'host come lo legge Chromium (audit, blocco 10, S-05): .NET tiene com'è "missiva。example" (punto ideografico),
        // le lettere a larghezza intera e il trattino facoltativo, che Chromium porta a missiva.example, la pagina del
        // riquadro. IdnHost li normalizza (IDNA); un host che non si normalizza si toglie, come quelli che .NET non legge.
        string host;
        try { host = uri.IdnHost; }
        catch (Exception) { return null; }
        if (host.TrimEnd('.').Equals(PageHost, StringComparison.OrdinalIgnoreCase)) return null;
        return uri.AbsoluteUri;
    }

    /// <summary>Caratteri di controllo che Chromium toglie all'inizio e alla fine di un indirizzo.</summary>
    static readonly char[] ControlChars = Enumerable.Range(0, 0x20).Select(c => (char)c).ToArray();

    /// <summary>Dove porta un indirizzo scritto in un'email, letto come lo legge il motore del riquadro dalla sua pagina (<see cref="KindOf(string?)"/>).</summary>
    internal enum UrlKind
    {
        /// <summary>Resta sulla pagina del riquadro: vuoto, relativo ("a.png", "/a", "?a", "https:a") o servito da Missiva.</summary>
        Page,
        /// <summary>"#id": un elemento dell'email stessa.</summary>
        Fragment,
        /// <summary>Un altro sito, comunque sia scritto: "http://sito", "//sito", "http:\\sito", "/\sito", "http:sito".</summary>
        Remote,
        Data,
        Cid,
        /// <summary>Un altro schema (file:, javascript:, blob:, ftp:...): niente che un'email debba far caricare.</summary>
        Other,
    }

    /// <summary>
    /// Dove porta un indirizzo quando lo legge il motore del riquadro (Chromium, dalla pagina https://missiva.example/), non
    /// come sembra scritto (audit dell'11 ottobre, T-01): prima era esterno solo quello che cominciava con "http://",
    /// "https://" o "//", e "http:\\sito", "/\sito", "http:/sito", "http:sito" e "ht[tab]tp://sito" restavano nel messaggio di
    /// un mittente non consentito, per partire appena la pagina lasciava passare le immagini di un altro. Le regole sono
    /// quelle con cui il motore legge un indirizzo (URL Standard), misurate nel WebView2 vero (ProvaBozze.Sicurezza2.cs):
    /// via gli spazi e i caratteri di controllo ai bordi, via tabulazioni e a capo ovunque; lo schema è lettere, cifre, "+",
    /// "-" e "." fino ai due punti; "\" vale "/"; dopo "http:" le barre non servono (lo schema è un altro da quello della
    /// pagina); "https:" senza due barre è relativo alla pagina, come un indirizzo senza schema.
    /// </summary>
    internal static UrlKind KindOf(string? value)
    {
        if (string.IsNullOrEmpty(value)) return UrlKind.Page;
        // Senza copiare l'indirizzo (può essere un data: di qualche MB): bastano i suoi primi caratteri.
        int at = 0, end = value.Length;
        while (at < end && value[at] <= ' ') at++;
        while (end > at && value[end - 1] <= ' ') end--;
        int first = Read(value, ref at, end);
        if (first < 0) return UrlKind.Page;
        if (first == '#') return UrlKind.Fragment;
        if (IsBar(first)) return IsBar(Read(value, ref at, end)) ? UrlKind.Remote : UrlKind.Page;
        if (!char.IsAsciiLetter((char)first)) return UrlKind.Page;
        Span<char> scheme = stackalloc char[5];
        int length = 0;
        for (int c = first; c != ':'; c = Read(value, ref at, end))
        {
            if (c < 0 || !(char.IsAsciiLetterOrDigit((char)c) || c is '+' or '-' or '.')) return UrlKind.Page;
            if (length < scheme.Length) scheme[length] = char.ToLowerInvariant((char)c);
            length++;
        }
        var name = scheme[..Math.Min(length, scheme.Length)];
        if (length == 4 && name is "http") return UrlKind.Remote;
        if (length == 4 && name is "data") return UrlKind.Data;
        if (length == 3 && name is "cid") return UrlKind.Cid;
        if (!(length == 5 && name is "https")) return UrlKind.Other;
        // Lo schema della pagina: quello che Missiva stessa serve, poi un altro sito solo con due barre.
        int probe = at;
        bool own = true;
        foreach (char expected in PagePrefix.AsSpan("https:".Length))
        {
            int c = Read(value, ref probe, end);
            if (c >= 0 && char.ToLowerInvariant((char)c) == expected) continue;
            own = false;
            break;
        }
        if (own) return UrlKind.Page;
        return IsBar(Read(value, ref at, end)) && IsBar(Read(value, ref at, end)) ? UrlKind.Remote : UrlKind.Page;
    }

    const string PagePrefix = "https://" + PageHost + "/";

    /// <summary>Una barra, dritta o rovesciata: per il motore sono la stessa cosa.</summary>
    static bool IsBar(int c) => c is '/' or '\\';

    /// <summary>Il prossimo carattere di un indirizzo che il motore legge (tabulazioni e a capo li salta), o -1 alla fine.</summary>
    static int Read(string value, ref int at, int end)
    {
        while (at < end && value[at] is '\t' or '\n' or '\r') at++;
        return at < end ? value[at++] : -1;
    }

    /// <summary>Come il motore prima di leggere un indirizzo: senza spazi e caratteri di controllo ai bordi, senza tabulazioni e a capo in mezzo.</summary>
    static string Unpadded(string value)
    {
        int start = 0, end = value.Length;
        while (start < end && value[start] <= ' ') start++;
        while (end > start && value[end - 1] <= ' ') end--;
        var inner = value.AsSpan(start, end - start);
        if (inner.IndexOfAny('\t', '\n', '\r') < 0) return inner.Length == value.Length ? value : inner.ToString();
        var clean = new StringBuilder(inner.Length);
        foreach (char c in inner)
            if (c is not ('\t' or '\n' or '\r')) clean.Append(c);
        return clean.ToString();
    }

    public static bool IsRemote(string? src) => KindOf(src) == UrlKind.Remote;

    /// <summary>Indirizzi serviti dalla pagina del riquadro stessa: le immagini incorporate già collegate da cidUrl.</summary>
    static bool IsPageUrl(string value) => value.TrimStart().StartsWith(PagePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Una funzione del CSS che carica qualcosa, fuori dai fogli di stile: gli attributi dei disegni SVG (fill, mask, cursor,
    /// filter) e i valori delle loro animazioni si leggono come CSS, e il motore chiede come immagine quello che c'è dentro
    /// url(...) e image-set(...). Resta solo url(#id), un elemento dell'email stessa, e url(data:...). Il resto si toglie,
    /// comunque sia scritto: prima si riconosceva solo "url(http://", "url(https://" e "url(//", e passavano
    /// url(http:\\sito), url( 'http:/sito'), image-set('http://sito' 1x) e il nome scritto con i codici del CSS, u\72l(
    /// (audit dell'11 ottobre, T-01, misurato nel WebView2 vero). Quasi nessun attributo ha una parentesi: lì non si cerca.
    /// </summary>
    static bool LoadsFromCss(string value)
    {
        if (!value.Contains('(')) return false;
        if (value.Contains('\\') || value.Contains("image-set(", StringComparison.OrdinalIgnoreCase)) return true;
        for (int at = value.IndexOf("url(", StringComparison.OrdinalIgnoreCase); at >= 0; at = value.IndexOf("url(", at + 4, StringComparison.OrdinalIgnoreCase))
        {
            var inside = value.AsSpan(at + 4).TrimStart(CssSpaces);
            if (inside.Length > 0 && inside[0] is '"' or '\'') inside = inside[1..].TrimStart(CssSpaces);
            if (!(inside.StartsWith("#") || inside.StartsWith("data:", StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    const string CssSpaces = " \t\n\r\f";

    /// <summary>Un elenco di valori separati da ";" (values, nelle animazioni dei disegni SVG) con dentro un indirizzo esterno.</summary>
    static bool RemoteInList(string values)
    {
        foreach (var part in values.Split(';'))
            if (KindOf(part) == UrlKind.Remote) return true;
        return false;
    }

    /// <summary>
    /// Un'animazione dei disegni SVG che cambia l'indirizzo di un'immagine (set o animate con attributeName href): il
    /// motore la applica dopo la pulizia, e l'immagine si chiede all'indirizzo scritto in to o in values. A un'email non
    /// serve, e si toglie per intero: con "#a" fra i valori l'immagine era la pagina stessa del riquadro, richiesta a ogni
    /// giro dell'animazione (audit dell'11 ottobre, T-01, misurato nel WebView2 vero).
    /// </summary>
    static bool AnimatesAddress(IElement e)
    {
        var name = e.LocalName;
        if (!(name.StartsWith("anim", StringComparison.OrdinalIgnoreCase) || name.Equals("set", StringComparison.OrdinalIgnoreCase))) return false;
        foreach (var a in e.Attributes)
            if (a.LocalName.Equals("attributeName", StringComparison.OrdinalIgnoreCase) && a.Value.Contains("href", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Immagini di 1 o 2 pixel: servono solo a sapere se hai aperto il messaggio.</summary>
    static bool IsPixel(IElement e) =>
        Number(e.GetAttribute("width")) is > 0 and <= 2 || Number(e.GetAttribute("height")) is > 0 and <= 2;

    static double Number(string? value) =>
        double.TryParse(value?.Replace("px", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"\bhttps?://[^\s<>""]+[^\s<>"".,;:!?)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex UrlInText();

    /// <summary>Selettori html, body e :root dei fogli di stile dell'email: nella shadow root diventano il div del corpo.</summary>
    [GeneratedRegex(@"(^|[\s,{}>+~(])(?:html|body|:root)(?![\w-])", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex RootSelectors();

    // ---------------------------------------------------------------- fogli di stile

    /// <summary>
    /// url(...) di un foglio di stile. Le prime tre forme sono quelle di sempre (fra apici, fra virgolette, senza: dentro né
    /// apici né virgolette né parentesi chiuse). Le altre tre sono le stesse come le legge il CSS, che Chromium caricherebbe e
    /// che prima passavano: fra virgolette un indirizzo può contenere parentesi e l'altro tipo di virgolette
    /// (url('https://sito/a(b).png')), e ovunque un carattere preceduto da "\". NonBacktracking: con il motore che torna
    /// indietro "url(" ripetuto 10.000 volte senza chiusura (40 KB) fermava la pulizia per 13 s. Senza gruppi: questo motore,
    /// dove due forme danno corrispondenze di lunghezza diversa (solo con un "\" davanti a una parentesi o a una virgoletta),
    /// può scegliere la più lunga e lasciare il gruppo vuoto; l'indirizzo si legge dalla corrispondenza (<see cref="Css"/>).
    /// </summary>
    [GeneratedRegex(@"url\(\s*(?:'[^'"")]*'|""[^'"")]*""|[^'"")]*|'(?:[^'\\]|\\[\s\S])*'|""(?:[^""\\]|\\[\s\S])*""|(?:[^'"")\\]|\\[\s\S])*)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex CssUrl();

    [GeneratedRegex(@"@import[^;]*;?", RegexOptions.IgnoreCase)]
    private static partial Regex CssImport();

    /// <summary>image-set() e -webkit-image-set(), che accettano anche indirizzi fra virgolette senza url().</summary>
    [GeneratedRegex(@"(?:-webkit-)?image-set\((?:[^()]|\([^()]*\))*\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssImageSet();

    /// <summary>
    /// Funzioni con il nome scritto con i codici di escape del CSS (u\72l( è url(): nessuna email vera lo fa, ma così un
    /// indirizzo sfuggirebbe a CssUrl. NonBacktracking: con il motore che torna indietro 2.000 "\" di fila (2 KB) costavano
    /// 4 s, e ogni raddoppio otto volte tanto.
    /// </summary>
    [GeneratedRegex(@"(?:[-\w]|\\[0-9a-fA-F]{1,6}\s?|\\[^\r\n0-9a-fA-F])*\\(?:[0-9a-fA-F]{1,6}\s?|[^\r\n0-9a-fA-F])(?:[-\w]|\\[0-9a-fA-F]{1,6}\s?|\\[^\r\n0-9a-fA-F])*\(", RegexOptions.NonBacktracking)]
    private static partial Regex CssEscapedFunction();

    /// <summary>Foglio di stile o attributo style: niente @import, url() esterni solo se consentiti, colori per il tema scuro.</summary>
    static string Css(string css, bool dark, bool allowRemote, Func<string, string?> cidUrl, ref int blocked, bool keepText)
    {
        css = CssImport().Replace(css, "");
        int stripped = 0;
        if (!allowRemote)
        {
            if (css.Contains('\\')) css = CssEscapedFunction().Replace(css, "missiva-bloccato(");
            css = CssImageSet().Replace(css, m =>
            {
                if (m.Value.Contains("//")) stripped++;
                return "none";
            });
        }
        css = CssUrl().Replace(css, m =>
        {
            // L'indirizzo: quello che sta fra "url(" e ")", senza gli spazi e le virgolette attorno.
            var inside = m.ValueSpan[4..^1].Trim();
            if (inside.Length >= 2 && inside[0] is '\'' or '"' && inside[^1] == inside[0]) inside = inside[1..^1];
            var url = inside.Trim().ToString();
            if (url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                return cidUrl(url[4..].Trim('<', '>')) is { } local ? $"url('{local}')" : "none";
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || IsPageUrl(url)) return m.Value;
            // Esterno o no lo dice l'indirizzo com'è per il motore, senza i codici del CSS (KindOf).
            bool remote = IsRemote(CssUnescaped(url));
            if (allowRemote && remote) return m.Value;
            if (remote) stripped++;
            return "none";
        });
        if (!allowRemote) css = WithoutUnclosedLoads(css, ref stripped);
        blocked += stripped;
        return dark ? DarkColors(css, keepText) : css;
    }

    /// <summary>
    /// Quello che CssUrl e CssImageSet non riconoscono perché non è chiuso. Per il CSS la fine del foglio, o dell'attributo
    /// style, chiude da sola parentesi e virgolette: "background:url(http://sito/a.png" senza la parentesi si caricava come
    /// le altre, e così image-set('http://sito/a.png' 1x (audit dell'11 ottobre, T-01, misurato nel WebView2 vero). Dopo la
    /// pulizia ogni url( rimasta deve essere di quelle tenute (data:, o servita da Missiva) e non deve restare nessun
    /// image-set(: le altre diventano una funzione che non esiste, come quelle scritte con i codici del CSS. Un foglio di
    /// stile normale non ne ha, e torna com'è.
    /// </summary>
    static string WithoutUnclosedLoads(string css, ref int stripped)
    {
        StringBuilder? clean = null;
        int copied = 0;
        for (int at = 0; at < css.Length; at++)
        {
            at = css.IndexOf('(', at);
            if (at < 0) break;
            // "url" da solo, non la fine di un altro nome ("curl(1)" nel testo di un foglio mai chiuso).
            bool url = Ends(css, at, "url") && !(at > 3 && (char.IsAsciiLetterOrDigit(css[at - 4]) || css[at - 4] is '-' or '_' or >= (char)0x80));
            bool set = !url && Ends(css, at, "image-set");
            if (!url && !set) continue;
            var inside = css.AsSpan(at + 1).TrimStart();
            if (inside.Length > 0 && inside[0] is '"' or '\'') inside = inside[1..].TrimStart();
            if (url && (inside.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || inside.StartsWith(PagePrefix, StringComparison.OrdinalIgnoreCase))) continue;
            // Per il conto basta l'inizio dell'indirizzo: cercarne la fine fino in fondo, a ogni "url(" di un foglio che ne
            // ripete migliaia senza chiuderle, costerebbe il quadrato.
            if (inside.Length > 64) inside = inside[..64];
            int end = inside.IndexOfAny(" \t\n\r\f\"')");
            if (IsRemote(CssUnescaped((end < 0 ? inside : inside[..end]).ToString()))) stripped++;
            int name = at - (url ? 3 : 9);
            (clean ??= new StringBuilder(css.Length + 32)).Append(css, copied, name - copied).Append("missiva-bloccato");
            copied = at;
        }
        return clean == null ? css : clean.Append(css, copied, css.Length - copied).ToString();

        static bool Ends(string text, int at, string name) =>
            at >= name.Length && text.AsSpan(at - name.Length, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Un indirizzo scritto in un foglio di stile senza i codici del CSS ("\2f" è "/", "\\" è "\"): com'è per il motore.</summary>
    static string CssUnescaped(string text)
    {
        if (!text.Contains('\\')) return text;
        var plain = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 == text.Length)
            {
                plain.Append(text[i]);
                continue;
            }
            int digits = 0, code = 0;
            while (digits < 6 && i + 1 + digits < text.Length && Uri.IsHexDigit(text[i + 1 + digits])) code = code * 16 + Uri.FromHex(text[i + 1 + digits++]);
            if (digits == 0)
            {
                // "\" e un a capo, dentro una stringa, non è nulla; altrimenti vale il carattere dopo.
                if (text[i + 1] is not ('\n' or '\r' or '\f')) plain.Append(text[i + 1]);
                i++;
                continue;
            }
            i += digits;
            if (i + 1 < text.Length && CssSpaces.Contains(text[i + 1])) i++;
            plain.Append(code is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF) ? "?" : char.ConvertFromUtf32(code));
        }
        return plain.ToString();
    }

    // ---------------------------------------------------------------- tema scuro

    enum Role { Text, Background, Border }

    [GeneratedRegex(@"(?<![-\w])(?<prop>background-color|background|border(?:-(?:top|right|bottom|left))?(?:-color)?|outline(?:-color)?|color)\s*:\s*(?<value>[^;{}""]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ColorDeclaration();

    [GeneratedRegex(@"url\([^)]*\)|#[0-9a-fA-F]{3,8}\b|rgba?\([^)]*\)|hsla?\([^)]*\)|[a-zA-Z]+", RegexOptions.IgnoreCase)]
    private static partial Regex ColorToken();

    /// <summary>
    /// Colori di un foglio di stile adattati al fondo scuro, come fa Outlook: i fondi chiari si scuriscono, i testi
    /// scuri si schiariscono mantenendo la tinta, i colori vivaci dei pulsanti e dei marchi restano come sono.
    /// </summary>
    static string DarkColors(string css, bool keepText) =>
        // Blocco per blocco (le regole tra graffe): in un blocco con fondo vivace il colore del testo resta com'è.
        CssBlock().Replace(css, block =>
        {
            bool keep = keepText || IsVivid(BackgroundIn(block.Value));
            return ColorDeclaration().Replace(block.Value, m =>
            {
                var prop = m.Groups["prop"].Value.ToLowerInvariant();
                var role = prop == "color" ? Role.Text : prop.StartsWith("border") || prop.StartsWith("outline") ? Role.Border : Role.Background;
                return role == Role.Text && keep ? m.Value : prop + ": " + Recolor(m.Groups["value"].Value, role);
            });
        });

    [GeneratedRegex(@"[^{}]+")]
    private static partial Regex CssBlock();

    /// <summary>Il valore di background o background-color in un elenco di dichiarazioni.</summary>
    static string? BackgroundIn(string? declarations)
    {
        if (string.IsNullOrEmpty(declarations)) return null;
        var m = ColorDeclaration().Matches(declarations).LastOrDefault(x => x.Groups["prop"].Value.StartsWith("background", StringComparison.OrdinalIgnoreCase));
        return m?.Groups["value"].Value;
    }

    /// <summary>Un fondo che nel tema scuro resta com'è: colore saturo di media luminosità (pulsanti, marchi).</summary>
    static bool IsVivid(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        int tail = OpenTail(value);
        foreach (Match t in ColorToken().Matches(tail < 0 ? value : value[..tail]))
            if (ParseColor(t.Value) is { A: > 0 } c && Vivid(c)) return true;
        if (tail >= 0)
            foreach (Match t in WordToken().Matches(value, tail))
                if (ParseColor(t.Value) is { A: > 0 } c && Vivid(c)) return true;
        return false;
    }

    static bool Vivid(Color c)
    {
        var (_, s, l) = ToHsl(c);
        return s > 0.45 && l is > 0.25 and < 0.7;
    }

    static string Recolor(string value, Role role)
    {
        string Adapted(Match t)
        {
            if (t.Value.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || ParseColor(t.Value) is not { } c || c.A == 0) return t.Value;
            var adapted = Adapt(c, role);
            return c.A == 255 ? $"#{adapted.R:X2}{adapted.G:X2}{adapted.B:X2}"
                : $"rgba({adapted.R},{adapted.G},{adapted.B},{(c.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})";
        }
        int tail = OpenTail(value);
        return tail < 0 ? ColorToken().Replace(value, Adapted) : ColorToken().Replace(value[..tail], Adapted) + WordToken().Replace(value[tail..], Adapted);
    }

    /// <summary>Le forme di <see cref="ColorToken"/> che non hanno parentesi: # e parole.</summary>
    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|[a-zA-Z]+", RegexOptions.IgnoreCase)]
    private static partial Regex WordToken();

    /// <summary>
    /// Dove comincia, in un valore, la coda con una parentesi aperta e mai chiusa; -1 se non c'è (tutti i valori normali).
    /// Dopo l'ultima ")" url(, rgb( e hsl( non possono più chiudersi, ma ColorToken li riprova a ogni "(" leggendo fino in
    /// fondo: con "rgb(" ripetuto 20.000 volte, il quadrato. Lì restano solo # e parole (<see cref="WordToken"/>), cioè quello
    /// che ColorToken troverebbe comunque.
    /// </summary>
    static int OpenTail(string value)
    {
        int tail = value.LastIndexOf(')') + 1;
        return value.IndexOf('(', tail) < 0 ? -1 : tail;
    }

    internal static Color? ParseColor(string value)
    {
        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            // Numeri o percentuali separati da virgole, spazi o "/": tre, più l'opacità. Quello che non si legge non è un colore.
            int open = value.IndexOf('('), close = value.IndexOf(')');
            if (close <= open) return null;
            var parts = value[(open + 1)..close].Split(',', ' ', '/').Where(p => p.Length > 0).ToArray();
            if (parts.Length < 3 || !Part(parts[0], out var r) || !Part(parts[1], out var g) || !Part(parts[2], out var b)) return null;
            double alpha = 1;
            if (parts.Length > 3)
            {
                if (!Part(parts[3], out var a)) return null;
                alpha = a.Percent ? a.Number / 100 : a.Number;
            }
            static byte C((double Number, bool Percent) p) => (byte)Math.Clamp(p.Percent ? p.Number * 2.55 : p.Number, 0, 255);
            return Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), C(r), C(g), C(b));
        }
        if (value.StartsWith('#') && value.Length == 4) value = "#" + string.Concat(value[1..].Select(ch => $"{ch}{ch}"));
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return null;
        // ColorConverter per ogni parola che non è un colore ("px", "solid", "none", "important") lancia una FormatException, e
        // nel tema scuro erano migliaia per messaggio (24.000 in un foglio di stile con 2.000 regole). Una parola è un nome di
        // colore, uno di quelli di Colors, o niente; quello che di sicuro non sa leggere non gli si chiede.
        if (!value.AsSpan().ContainsAnyExcept(Letters)) return ColorNames.All.TryGetValue(value, out var named) ? named : null;
        if (NotAColor(value)) return null;
        try
        {
            return ColorConverter.ConvertFromString(value) is Color color ? color : null;
        }
        catch
        {
            return null;
        }
    }

    static bool Part(string part, out (double Number, bool Percent) read)
    {
        bool percent = part.EndsWith('%');
        bool ok = double.TryParse(percent ? part.AsSpan(0, part.Length - 1) : part.AsSpan(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number);
        read = (number, percent);
        return ok;
    }

    /// <summary>
    /// Vero solo dove ColorConverter lancerebbe di sicuro: un # con sole cifre esadecimali ma non 3, 4, 6 o 8, url(...) e
    /// hsl(...). Il resto lo decide ancora lui.
    /// </summary>
    static bool NotAColor(string value) =>
        value.StartsWith('#') ? value.Length is not (4 or 5 or 7 or 9) && !value.AsSpan(1).ContainsAnyExcept(HexDigits)
        : value.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || value.StartsWith("hsl(", StringComparison.OrdinalIgnoreCase)
          || value.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase);

    static readonly System.Buffers.SearchValues<char> HexDigits = System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");
    /// <summary>Le lettere che [a-zA-Z] di <see cref="ColorToken"/> accetta senza badare alle maiuscole: quelle ASCII e il segno del kelvin (U+212A), che vale "k".</summary>
    static readonly System.Buffers.SearchValues<char> Letters = System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ\u212A");

    /// <summary>I colori con un nome che ColorConverter conosce: quelli di System.Windows.Media.Colors. Letti al primo uso.</summary>
    static class ColorNames
    {
        internal static readonly Dictionary<string, Color> All = typeof(Colors).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .ToDictionary(p => p.Name, p => (Color)p.GetValue(null)!, StringComparer.OrdinalIgnoreCase);
    }

    static Color Adapt(Color c, Role role)
    {
        var (h, s, l) = ToHsl(c);
        switch (role)
        {
            case Role.Text:
                if (l < 0.5) l = 1 - l;
                return FromHsl(h, s, Math.Max(l, 0.62), c.A);
            case Role.Border:
                if (l > 0.5) l = 1 - l;
                return FromHsl(h, s * 0.6, Math.Clamp(l, 0.25, 0.4), c.A);
            default:
                if (Vivid(c)) return c;
                // Come il nuovo Outlook: il bianco diventa esattamente il colore del riquadro, e i fondi meno chiari restano
                // un po' più chiari di lui, nello stesso ordine, così le schede grigie si distinguono ancora dal fondo. Prima
                // il bianco diventava nero: un buco nel riquadro grigio. Il nero pieno resta solo se l'email lo chiede.
                // La saturazione tiene la stessa quantità di colore (max - min) dell'originale: nei grigi appena azzurrati
                // delle newsletter la saturazione HSL è alta, e scurendoli verrebbe un blu.
                if (l > 0.5)
                {
                    double dim = Math.Min(PaperLightness + (1 - l) * 0.6, 0.3);
                    double chroma = s * (1 - Math.Abs(2 * l - 1));
                    return FromHsl(h, Math.Min(s * 0.6, chroma / (1 - Math.Abs(2 * dim - 1))), dim, c.A);
                }
                return FromHsl(h, s * 0.6, Math.Min(l, 0.2), c.A);
        }
    }

    static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }
        return (h, s, l);
    }

    static Color FromHsl(double h, double s, double l, byte alpha)
    {
        double r, g, b;
        if (s == 0) r = g = b = l;
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            double Hue(double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
            }
            r = Hue(h + 1.0 / 3); g = Hue(h); b = Hue(h - 1.0 / 3);
        }
        return Color.FromArgb(alpha, (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}
