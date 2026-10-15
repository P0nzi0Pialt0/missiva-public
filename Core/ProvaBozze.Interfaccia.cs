#if DEBUG
using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;

namespace Missiva;

/// <summary>
/// Revisione del 14 ottobre 2026: cinque consigli di sicurezza sull'interfaccia (docs\audit\sicurezza-ui.md, U-01..U-05;
/// l'esito in docs\audit\ESITO.md). Il revisore ha letto i sorgenti senza eseguire: ogni punto qui si riproduce prima di
/// dirlo vero. Per U-01 la pagina vera del riquadro si carica nel motore vero senza finestra (Sec2Engine, lo stesso di
/// T-01) e si guarda che cosa finisce fuori dalla shadow root dell'email. Solo dati finti, nessuna finestra, nessuna rete.
/// Con "Missiva.exe --prova-audit risultato.txt interfaccia" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void UiReviewTests()
    {
        Report.Add("--- Revisione del 14 ottobre: cinque consigli sull'interfaccia (U-01..U-05)");
        U1DumpSamples();
        U1Cleaning();
        try
        {
            U1Engine();
        }
        catch (Exception ex)
        {
            Check(false, "U-01, il motore vero del riquadro (WebView2 senza finestra) non ha finito la prova: " + ex.Message);
        }
        U2ForwardsAsync().GetAwaiter().GetResult();
        U3TempFiles();
        U4EmptyPage();
        U5Launches();
        Report.Add("--- Scelte del 14 ottobre: le conferme sugli inoltri (U-02), il template nelle citazioni (U-01)");
        U2CancelFirst();
        U2AllMessages();
        U1OutgoingTemplate();
    }

    // ---------------------------------------------------------------- U-01: uscire dalla shadow root dell'email

    /// <summary>
    /// Quello che un'email proverebbe a portare fuori dalla sua shadow root: la chiusura del template e dei due contenitori
    /// che la pagina le scrive attorno, un elemento con un segno (la classe resta dopo la pulizia; l'attributo di evento
    /// solo se la pulizia non l'ha mai visto come elemento) e un foglio di stile che ricolora le fasce della pagina.
    /// </summary>
    const string U1Payload = "</template></div></div><i class=\"fuori\" onclick=\"1\">FUORI</i><style>.warn{color:rgb(1,2,3) !important}</style>";

    /// <summary>I candidati di U-01: un nome e l'HTML dell'email. {P} è il carico, {E} lo stesso scritto con le entità, {N} con le entità numeriche.</summary>
    static List<(string What, string Html)> U1Candidates()
    {
        string numeric = string.Concat(U1Payload.Select(c => c is '<' or '>' or '"' or '&' ? $"&#{(int)c};" : c.ToString()));
        (string What, string Html)[] written =
        [
            // Commenti chiusi come li chiude il motore.
            ("commento chiuso da --!>", "<p>a</p><!-- a --!>{P}-->"),
            ("commento <!-->", "<p>a</p><!-->{P}-->"),
            ("commento <!--->", "<p>a</p><!--->{P}-->"),
            ("commento con <!-- annidato", "<p>a</p><!-- a <!-- b -->{P}-->"),
            ("commento mai chiuso", "<p>a</p><!-- {P}"),
            ("commento mai chiuso dopo --!>", "<p>a</p><!-- a --!>{P}"),
            ("commento con -- > dentro", "<p>a</p><!-- a -- >{P}-->"),
            ("commento che finisce con --!-->", "<p>a</p><!-- a --!-->{P}"),
            ("commento che finisce con --->", "<p>a</p><!-- a --->{P}"),
            ("commento con un trattino solo <!->", "<p>a</p><!->{P}-->"),
            ("dichiarazione <!x", "<p>a</p><!x {P}"),
            ("istruzione <?xml", "<p>a</p><?xml {P} ?>"),
            ("chiusura senza nome </ ", "<p>a</p></ {P}"),
            ("CDATA nell'HTML", "<p>a</p><![CDATA[{P}]]>"),
            ("commento condizionale di Outlook", "<p>a</p><!--[if mso]>{P}<![endif]-->"),
            ("commento condizionale aperto agli altri", "<p>a</p><!--[if !mso]><!-->{P}<!--<![endif]-->"),
            ("commento dentro una tabella", "<table><!-- a --!>{P}--><tr><td>x</td></tr></table>"),
            // Disegni e formule.
            ("svg, commento chiuso da --!>", "<svg><!-- a --!>{P}--></svg>"),
            ("svg, commento <!-->", "<svg><!-->{P}--></svg>"),
            ("svg, CDATA", "<svg><![CDATA[{P}]]></svg>"),
            ("math, CDATA", "<math><![CDATA[{P}]]></math>"),
            ("svg, CDATA chiuso due volte", "<svg><![CDATA[ a ]]>{P}]]></svg>"),
            ("svg desc, CDATA", "<svg><desc><![CDATA[{P}]]></desc></svg>"),
            ("svg foreignObject, CDATA", "<svg><foreignObject><![CDATA[{P}]]></foreignObject></svg>"),
            ("svg foreignObject p, CDATA", "<svg><foreignObject><p><![CDATA[{P}]]></p></foreignObject></svg>"),
            ("svg style", "<svg><style>{P}</style></svg>"),
            ("svg style, CDATA", "<svg><style><![CDATA[{P}]]></style></svg>"),
            ("svg style con le entità", "<svg><style>{E}</style></svg>"),
            ("svg style con un elemento e </style> in un attributo", "<svg><style><a title=\"</style>{E}\"></a></style></svg>"),
            ("svg, </p> e poi style", "<svg></p><style><a title=\"</style>{E}\"></a></style></svg>"),
            ("math annotation-xml text/html", "<math><annotation-xml encoding=\"text/html\">{P}</annotation-xml></math>"),
            ("math annotation-xml TEXT/HTML, xmp", "<math><annotation-xml encoding=\"TEXT/HTML\"><xmp>{P}</xmp></annotation-xml></math>"),
            ("math annotation-xml senza encoding, xmp", "<math><annotation-xml><xmp>{P}</xmp></annotation-xml></math>"),
            ("math annotation-xml application/xhtml+xml, xmp", "<math><annotation-xml encoding=\"application/xhtml+xml\"><xmp>{P}</xmp></annotation-xml></math>"),
            ("svg foreignObject", "<svg><foreignObject>{P}</foreignObject></svg>"),
            ("svg foreignObject, xmp", "<svg><foreignObject><xmp>{P}</xmp></foreignObject></svg>"),
            ("svg desc, xmp", "<svg><desc><xmp>{P}</xmp></desc></svg>"),
            ("svg template", "<svg><template>{P}</template></svg>"),
            ("svg template shadowrootmode", "<svg><template shadowrootmode=\"open\">{P}</template></svg>"),
            ("svg template chiuso in sé", "<svg><template/>{P}</svg>"),
            ("math template", "<math><template>{P}</template></math>"),
            ("svg title", "<svg><title>{P}</title></svg>"),
            ("svg title, xmp", "<svg><title><xmp>{P}</xmp></title></svg>"),
            ("svg script", "<svg><script>{P}</script></svg>"),
            ("svg plaintext", "<svg><plaintext>{P}</plaintext></svg>"),
            ("svg xmp", "<svg><xmp>{P}</xmp></svg>"),
            ("math xmp", "<math><xmp>{P}</xmp></math>"),
            ("svg, </p> e poi xmp", "<svg></p><xmp>{P}</xmp></svg>"),
            ("svg, </br> e poi xmp", "<svg></br><xmp>{P}</xmp></svg>"),
            ("math, </p> e poi xmp", "<math></p><xmp>{P}</xmp></math>"),
            ("math mi, </p> e poi xmp", "<math><mi></p><xmp>{P}</xmp></mi></math>"),
            ("svg, </p> e poi noembed", "<svg></p><noembed>{P}</noembed></svg>"),
            ("svg, </p> e poi plaintext", "<svg></p><plaintext>{P}"),
            ("svg, </p> e poi template chiuso in sé", "<svg></p><template/>{P}</svg>"),
            ("math mtext table mglyph xmp", "<math><mtext><table><mglyph><xmp>{P}</xmp></mglyph></table></mtext></math>"),
            ("math mtext table mglyph noembed", "<math><mtext><table><mglyph><noembed>{P}</noembed></mglyph></table></mtext></math>"),
            ("math mtext table mglyph noframes", "<math><mtext><table><mglyph><noframes>{P}</noframes></mglyph></table></mtext></math>"),
            ("math mi table malignmark xmp", "<math><mi><table><malignmark><xmp>{P}</xmp></malignmark></table></mi></math>"),
            ("math mtext table mglyph style con commento", "<math><mtext><table><mglyph><style><!--</style><img title=\"--&gt;&lt;/mglyph&gt;{E}\"></mglyph></table></mtext></math>"),
            ("math mtext table mglyph xmp con commento", "<math><mtext><table><mglyph><xmp><!--</xmp><img title=\"--&gt;&lt;/mglyph&gt;{E}\"></mglyph></table></mtext></math>"),
            ("math mtext mglyph xmp", "<math><mtext><mglyph><xmp>{P}</xmp></mglyph></mtext></math>"),
            ("math mtext b mglyph xmp", "<math><mtext><b><mglyph><xmp>{P}</xmp></mglyph></b></mtext></math>"),
            ("math mtext form mglyph xmp", "<form><math><mtext></form><form><mglyph><xmp>{P}</xmp></mglyph></form></mtext></math></form>"),
            ("math mtext a a mglyph xmp", "<math><mtext><a><table><a><mglyph><xmp>{P}</xmp></mglyph></a></table></a></mtext></math>"),
            ("svg dentro una tabella, xmp", "<table><svg><xmp>{P}</xmp></svg></table>"),
            ("select con svg e xmp", "<select><svg><xmp>{P}</xmp></svg></select>"),
            // Elementi dal contenuto grezzo.
            ("xmp", "<xmp>{P}</xmp>"),
            ("xmp con un commento dentro", "<xmp><!--{P}--></xmp>"),
            ("xmp mai chiuso", "<p>a</p><xmp>{P}"),
            ("noembed", "<noembed>{P}</noembed>"),
            ("noframes", "<noframes>{P}</noframes>"),
            ("listing", "<listing>{P}</listing>"),
            ("textarea", "<textarea>{P}</textarea>"),
            ("title", "<title>{P}</title>"),
            ("title nel corpo", "<p>a</p><title>{P}</title>"),
            ("noscript", "<noscript>{P}</noscript>"),
            ("noscript nella testa", "<head><noscript>{P}</noscript></head><p>a</p>"),
            ("iframe", "<iframe>{P}</iframe>"),
            ("script", "<script>{P}</script>"),
            ("plaintext", "<p>a</p><plaintext>{P}"),
            ("xmp in una tabella", "<table><xmp>{P}</xmp></table>"),
            ("frameset e poi noframes", "<frameset></frameset><noframes>{P}</noframes>"),
            ("frameset con noframes", "<frameset><noframes>{P}</noframes></frameset>"),
            // Attributi.
            ("attributo fra virgolette", "<a title=\"{E}\">x</a>"),
            ("attributo fra apici", "<a title='</template></div></div><i class=\"fuori\" onclick=\"1\">FUORI</i>'>x</a>"),
            ("attributo senza virgolette", "<a title=</template>x</a>{P}"),
            ("attributo con entità numeriche", "<a title=\"{N}\">x</a>"),
            ("attributo con le virgolette chiuse dalle entità", "<a title=\"&quot;&gt;{E}\">x</a>"),
            ("testo alternativo di un'immagine incorporata", "<img src=\"data:image/gif;base64,R0lGODlhAQABAAAAACw=\" alt=\"{E}\">"),
            ("classe, stile e colori del corpo", "<body class=\"&quot;&gt;{E}\" style=\"&quot;&gt;{E}\" bgcolor=\"&quot;&gt;{E}\" text=\"&quot;&gt;{E}\"><p>a</p></body>"),
            ("nome di attributo con <", "<a </template>=\"x\" <i=\"y\">x</a>{P}"),
            ("valore di style con </template>", "<p style=\"background:url('{E}')\">x</p>"),
            ("href di un link", "<a href=\"https://www.esempio-prova.example/{E}\">x</a>"),
            // Fogli di stile.
            ("foglio con </template> in una stringa", "<style>a::after{content:\"{P}\"}</style><p>a</p>"),
            ("foglio con </TEMPLATE>", "<style>a::after{content:\"</TEMPLATE></DIV></DIV><I CLASS=fuori ONCLICK=1>FUORI</I>\"}</style><p>a</p>"),
            ("foglio con <\\/template>", "<style>a::after{content:\"<\\/template><\\/div><\\/div><i class=fuori onclick=1>FUORI<\\/i>\"}</style><p>a</p>"),
            ("foglio con i codici del CSS", "<style>a::after{content:\"\\3C /template>\\3C /div>\\3C /div>\\3C i class=fuori onclick=1>FUORI\"}</style><p>a</p>"),
            ("foglio con </template> in un commento del CSS", "<style>/* {P} */ a{color:red}</style><p>a</p>"),
            ("foglio con <!-- e -->", "<style><!-- {P} --> a{color:red}</style><p>a</p>"),
            ("foglio con </template> in url()", "<style>a{background:url({P})}</style><p>a</p>"),
            ("foglio con @import in mezzo a </template>", "<style><@import url(x);/template><@import url(x);/div><@import url(x);/div><i class=fuori onclick=1>FUORI</i></style><p>a</p>"),
            ("foglio con </style> scritto con @import in mezzo", "<svg><style>&lt;@import url(x);/style&gt;{E}</style></svg>"),
            ("foglio chiuso da </style e uno spazio", "<style>a{}</style >{P}"),
            ("foglio con <!-- e poi </style>", "<style><!--</style>{P}-->"),
            // template e </template> dove l'analisi li tratta in modo strano.
            ("</template> da solo", "<p>a</p></template>{P}"),
            ("template", "<template>{P}</template>"),
            ("template in un template", "<template><template>{P}</template></template>{P}"),
            ("template shadowrootmode", "<div><template shadowrootmode=\"open\">{P}</template></div>"),
            ("template in una tabella", "<table><template>{P}</template></table>"),
            ("</template> in una tabella", "<table></template>{P}</table>"),
            ("</template> in una cella", "<table><tr><td></template>{P}</td></tr></table>"),
            ("template in un colgroup", "<table><colgroup><template>{P}</template></colgroup></table>"),
            ("template in un select", "<select><template>{P}</template></select>"),
            ("</template> in un select", "<select></template>{P}</select>"),
            ("template in un frameset", "<frameset><template>{P}</template></frameset>"),
            ("dopo </body>", "<body><p>a</p></body>{P}"),
            ("dopo </html>", "<html><body><p>a</p></body></html>{P}"),
            ("</template> con spazi e una barra", "<p>a</p></template / ></TEMPLATE\n>{P}"),
            ("testo con le entità", "<p>{E}</p>"),
            ("testo con le entità numeriche", "<p>{N}</p>"),
            ("un carattere nullo nel nome", "<p>a</p><\0/template></temp\0late>{P}"),
        ];
        var all = written.ToList();
        // Dove l'analisi passa dal disegno o dalla formula all'HTML e ritorno: nei due versi. Con {P} il contenuto grezzo di un
        // xmp che il motore potrebbe leggere come elementi; con </xmp> in un attributo, elementi che il motore potrebbe
        // leggere come contenuto grezzo, chiuso a metà dell'attributo.
        const string closing = "<a title=\"&lt;/xmp&gt;{E}\">x</a>";
        foreach (var encoding in new[] { " text/html", "text/html ", "text/html;charset=utf-8", "Text/Html", "text/htm", "TEXT/HTML\t", "application/xhtml+xml ", "APPLICATION/XHTML+XML" })
        {
            string shown = encoding.Replace("\t", "[tab]");
            all.Add(($"math annotation-xml encoding=\"{shown}\", xmp", $"<math><annotation-xml encoding=\"{encoding}\"><xmp>{{P}}</xmp></annotation-xml></math>"));
            all.Add(($"math annotation-xml encoding=\"{shown}\", xmp con </xmp> in un attributo", $"<math><annotation-xml encoding=\"{encoding}\"><xmp>{closing}</xmp></annotation-xml></math>"));
        }
        all.Add(("math annotation-xml con due encoding, xmp", "<math><annotation-xml encoding=\"x\" encoding=\"text/html\"><xmp>{P}</xmp></annotation-xml></math>"));
        all.Add(("math annotation-xml con due encoding, xmp con </xmp> in un attributo", $"<math><annotation-xml encoding=\"x\" encoding=\"text/html\"><xmp>{closing}</xmp></annotation-xml></math>"));
        all.Add(("math annotation-xml ENCODING maiuscolo, xmp", "<math><annotation-xml ENCODING=\"text/html\"><xmp>{P}</xmp></annotation-xml></math>"));
        foreach (var name in new[] { "foreignObject", "foreignobject", "FOREIGNOBJECT", "desc", "g", "text", "a", "switch", "annotation-xml", "mtext" })
        {
            all.Add(($"svg {name}, xmp", $"<svg><{name}><xmp>{{P}}</xmp></{name}></svg>"));
            all.Add(($"svg {name}, xmp con </xmp> in un attributo", $"<svg><{name}><xmp>{closing}</xmp></{name}></svg>"));
        }
        foreach (var name in new[] { "mi", "mo", "mn", "ms", "mtext", "MTEXT", "mrow", "semantics", "annotation", "merror", "foreignObject" })
        {
            all.Add(($"math {name}, xmp", $"<math><{name}><xmp>{{P}}</xmp></{name}></math>"));
            all.Add(($"math {name}, xmp con </xmp> in un attributo", $"<math><{name}><xmp>{closing}</xmp></{name}></math>"));
            all.Add(($"math {name}, tabella, mglyph, xmp", $"<math><{name}><table><mglyph><xmp>{{P}}</xmp></mglyph></table></{name}></math>"));
        }
        // mglyph e malignmark messi dall'analisi fuori da una tabella, in altri punti della tabella e in altri contenitori.
        all.Add(("math mtext, mglyph nella riga di una tabella, xmp", "<math><mtext><table><tr><mglyph><xmp>{P}</xmp></mglyph></tr></table></mtext></math>"));
        all.Add(("math mtext, mglyph dopo la didascalia, xmp", "<math><mtext><table><caption>c</caption><mglyph><xmp>{P}</xmp></mglyph></table></mtext></math>"));
        all.Add(("math mtext, mglyph in un colgroup, xmp", "<math><mtext><table><colgroup><mglyph><xmp>{P}</xmp></mglyph></colgroup></table></mtext></math>"));
        all.Add(("svg foreignObject, math mtext, tabella, mglyph, xmp", "<svg><foreignObject><math><mtext><table><mglyph><xmp>{P}</xmp></mglyph></table></mtext></math></foreignObject></svg>"));
        all.Add(("tabella, math mtext, tabella, mglyph, xmp", "<table><tr><td><math><mtext><table><mglyph><xmp>{P}</xmp></mglyph></table></mtext></math></td></tr></table>"));
        all.Add(("math mtext, tabella, mglyph, annotation-xml text/html, xmp", "<math><mtext><table><mglyph><annotation-xml encoding=\"text/html\"><xmp>{P}</xmp></annotation-xml></mglyph></table></mtext></math>"));
        all.Add(("math mtext, tabella, mglyph, mi, xmp", "<math><mtext><table><mglyph><mi><xmp>{P}</xmp></mi></mglyph></table></mtext></math>"));
        all.Add(("math mtext, tabella, mglyph, svg, xmp con </xmp> in un attributo", $"<math><mtext><table><mglyph><svg><xmp>{closing}</xmp></svg></mglyph></table></mtext></math>"));
        all.Add(("math mtext, tabella, mglyph, pre con un a capo e listing", "<math><mtext><table><mglyph><pre>\n{E}</pre><listing>{E}</listing></mglyph></table></mtext></math>"));
        all.Add(("math mtext, tabella, mglyph, immagine con il carico nel titolo", "<math><mtext><table><mglyph><img title=\"{E}\"></mglyph></table></mtext></math>"));
        all.Add(("math mtext, tabella, svg, xmp con </xmp> in un attributo", $"<math><mtext><table><svg><xmp>{closing}</xmp></svg></table></mtext></math>"));
        all.Add(("math mtext, tabella, mglyph, svg, mtext, xmp con </xmp> in un attributo", $"<math><mtext><table><mglyph><svg><mtext><xmp>{closing}</xmp></mtext></svg></mglyph></table></mtext></math>"));
        all.Add(("math mtext, tabella, mglyph, svg, foreignObject, xmp", "<math><mtext><table><mglyph><svg><foreignObject><xmp>{P}</xmp></foreignObject></svg></mglyph></table></mtext></math>"));
        all.Add(("math mtext, tabella, mglyph, svg, desc, noembed", "<math><mtext><table><mglyph><svg><desc><noembed>{P}</noembed></desc></svg></mglyph></table></mtext></math>"));
        all.Add(("math mtext, tabella, malignmark, svg, mi, noframes con </noframes> in un attributo", "<math><mtext><table><malignmark><svg><mi><noframes><a title=\"&lt;/noframes&gt;{E}\">x</a></noframes></mi></svg></malignmark></table></mtext></math>"));
        all.Add(("svg xmp con </xmp> in un attributo", $"<svg><xmp>{closing}</xmp></svg>"));
        all.Add(("svg noembed con </noembed> in un attributo", "<svg><noembed><a title=\"&lt;/noembed&gt;{E}\">x</a></noembed></svg>"));
        all.Add(("svg noframes con </noframes> in un attributo", "<svg><noframes><a title=\"&lt;/noframes&gt;{E}\">x</a></noframes></svg>"));
        all.Add(("svg iframe con </iframe> in un attributo", "<svg><iframe><a title=\"&lt;/iframe&gt;{E}\">x</a></iframe></svg>"));
        all.Add(("svg textarea con </textarea> in un attributo", "<svg><textarea><a title=\"&lt;/textarea&gt;{E}\">x</a></textarea></svg>"));
        all.Add(("svg noscript con </noscript> in un attributo", "<svg><noscript><a title=\"&lt;/noscript&gt;{E}\">x</a></noscript></svg>"));
        // Dove finisce un nome: i caratteri che per il motore separano il nome di un elemento dai suoi attributi.
        static string Ch(int code) => ((char)code).ToString();
        foreach (var (what, gap) in new[] { ("avanzamento di pagina", Ch(0x0C)), ("ritorno a capo da solo", Ch(0x0D)), ("tabulazione verticale", Ch(0x0B)), ("spazio unificatore", Ch(0xA0)), ("riga successiva", Ch(0x85)),
                                            ("separatore di riga", Ch(0x2028)), ("spazio ideografico", Ch(0x3000)), ("segno dell'ordine dei byte", Ch(0xFEFF)), ("carattere nullo", Ch(0)), ("barra", "/") })
        {
            all.Add(($"nome di elemento seguito da {what}", $"<p>a</p><i{gap}class=fuori{gap}onclick=1>FUORI</i>"));
            all.Add(($"nome di attributo seguito da {what}", $"<p>a</p><i class=\"fuori\" x{gap}onclick=1>FUORI</i>"));
        }
        var list = all.Select(c => (c.What, c.Html.Replace("{P}", U1Payload).Replace("{E}", WebUtility.HtmlEncode(U1Payload)).Replace("{N}", numeric))).ToList();
        // La strada del solo testo: oltre i tetti dell'analisi (600 livelli), con il carico nel testo e in un indirizzo.
        string deep = string.Concat(Enumerable.Repeat("<div>", 600));
        list.Add(("solo testo, oltre i tetti", deep + "x " + U1Payload));
        list.Add(("solo testo, il carico scritto con le entità", deep + "x " + WebUtility.HtmlEncode(U1Payload)));
        list.Add(("solo testo, un indirizzo con il carico", deep + "x https://www.esempio-prova.example/a\"&gt;&lt;/a&gt;" + WebUtility.HtmlEncode(U1Payload) + " y"));
        list.Add(("solo testo, il carico in un contenuto grezzo", deep + "<xmp>" + U1Payload + "</xmp><style>" + U1Payload + "</style>x"));
        list.Add(("un messaggio di solo testo", MailHtml.FromText("Gentile cliente,\nvedi https://www.esempio-prova.example/a\"></a>" + U1Payload + "\n" + U1Payload)));
        return list;
    }

    /// <summary>Quello che il motore dice di una pagina: il DOM fuori dalle shadow root, i segni, il colore della fascia vera.</summary>
    const string U1Reading =
        "(() => { const all = []; const walk = r => { for (const e of r.querySelectorAll('*')) { all.push(e); if (e.shadowRoot) walk(e.shadowRoot); } }; walk(document); " +
        "const mail = document.querySelector('.mail'); const warn = document.querySelector('.warn.danger'); const root = mail ? mail.shadowRoot : null; " +
        "return { light: document.body.innerHTML, marks: document.querySelectorAll('.fuori').length, dirty: all.filter(e => e.hasAttribute('onclick')).length, " +
        "inside: root ? root.querySelectorAll('.fuori').length : -1, shadow: root ? root.innerHTML : '(nessuna shadow root)', templates: document.querySelectorAll('template').length, " +
        "color: warn ? getComputedStyle(warn).color : '', display: warn ? getComputedStyle(warn).display : '' }; })()";

    /// <summary>
    /// I testi HTML che la suite ha già: le stringhe dei file delle prove (non di questo, che ne ha di costruite per uscire)
    /// con dentro almeno un elemento. Servono a dire che cosa cambia nella pagina dei messaggi di prova: null fuori dalla
    /// cartella dei sorgenti.
    /// </summary>
    static List<string>? U1Samples()
    {
        if (SourceRoot() is not { } root) return null;
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(root, "Core"), "*.cs").Where(f => Path.GetFileName(f) is var n && (n.StartsWith("ProvaBozze") || n.Contains("Samples")) && n != "ProvaBozze.Interfaccia.cs"))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "(?<![@\\\\'])\"((?:[^\"\\\\\\r\\n]|\\\\.)*)\""))
            {
                string text;
                try { text = System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value.Replace("{{", "{").Replace("}}", "}")); }
                catch (ArgumentException) { continue; }
                if (text.Length >= 16 && System.Text.RegularExpressions.Regex.IsMatch(text, "<[a-zA-Z!][^<>]*>")) found.Add(text);
            }
        return found.OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Con MISSIVA_PROVA_FRAMMENTI=file il frammento pulito di ogni testo di prova (tema chiaro e scuro) finisce nel file,
    /// uno per riga: per confrontare la pagina fra due compilazioni.
    /// </summary>
    static void U1DumpSamples()
    {
        if (Environment.GetEnvironmentVariable("MISSIVA_PROVA_FRAMMENTI") is not { Length: > 0 } dump || U1Samples() is not { } samples) return;
        static string Line(string s) => string.Concat(s.Select(c => c == '\n' ? "\\n" : c == '\r' ? "\\r" : char.IsSurrogate(c) || c < ' ' ? "?" : c.ToString()));
        var lines = new List<string>();
        foreach (var html in samples)
            foreach (bool dark in new[] { false, true })
            {
                var r = MailHtml.Prepare(html, dark, false, _ => null);
                lines.Add($"{Line(html)}\t{dark}\t{r.BlockedImages}\t{r.Links.Count}\t{r.PlainOnly}\t{Line(r.Fragment)}");
            }
        File.WriteAllLines(dump, lines);
    }

    sealed record U1Seen(string What, bool Plain, string Fragment, string Light, int Marks, int Dirty, int Inside, string Shadow, int Templates, string Color, string Display);

    /// <summary>La data delle pagine di prova: fissa, perché la pagina la scrive e il confronto con la pagina di riferimento non dipenda dal minuto.</summary>
    static readonly DateTime U1Date = new(2026, 10, 14, 9, 30, 0);

    static UI.ReadingPage U1Page(string html)
    {
        var mail = new MessageSummary { Id = "1", FolderId = "INBOX", Subject = "Prova", Date = U1Date, From = new Address { Name = "Banca di prova", Email = "avvisi@banca-prova.example" } };
        return UI.ReadingPage.Build([new UI.PageEntry(mail, new MessageBody { Html = html }, true, false, null, false, new SenderNotes("banca-prova.example", "DMARC non superato", false, null))],
                                    light: true, header: new UI.PageHeader("Prova", false));
    }

    /// <summary>
    /// Una conversazione di due messaggi (il primo chiuso, con l'anteprima) dove tutto quello che la pagina prende dall'email
    /// senza passare dalla pulizia porta lo stesso testo: oggetto, nome e indirizzo del mittente, anteprima, destinatari,
    /// Rispondi a, categorie, nomi degli allegati, le righe delle fasce e di "Annulla iscrizione", l'account.
    /// </summary>
    static UI.ReadingPage U1FieldsPage(string text)
    {
        Address Who(string name) => new() { Name = text + name, Email = "x@dominio-ostile.example" };
        var closed = new MessageSummary { Id = "1", FolderId = "INBOX", Subject = text, Date = U1Date, From = Who("uno"), Preview = text };
        var open = new MessageSummary { Id = "2", FolderId = "INBOX", Subject = text, Date = U1Date, From = Who("due"), Categories = [text, "Clienti/" + text], ReplyTo = [Who("tre")] };
        var body = new MessageBody
        {
            Html = "<p>Prova</p><img src=\"https://immagini.esempio-prova.example/a.png\" width=\"200\" height=\"100\" alt=\"Foto\">", To = [Who("quattro")], Cc = [Who("cinque")],
            Attachments = [new Attachment { Id = "a", Name = text + ".pdf", ContentType = text, Size = 2048 }, new Attachment { Id = "b", Name = "nota." + text, Size = 10 }],
        };
        var notes = new SenderNotes("banca-prova.example", text, true, text) { NameAddress = text };
        return UI.ReadingPage.Build([new UI.PageEntry(closed, null, false, false, null, false), new UI.PageEntry(open, body, true, false, null, false, notes, Unsubscribe: text, ImagesOnce: text)],
                                    light: true, header: new UI.PageHeader(text, false, (text, System.Windows.Media.Colors.Teal)));
    }

    /// <summary>Di una pagina con quei campi: la forma del DOM (elementi e classi), i segni, il colore della fascia rossa, l'oggetto come lo legge il motore.</summary>
    const string U1FieldsReading =
        "(() => { const all = [...document.querySelectorAll('*')]; const warn = document.querySelector('.warn.danger'); " +
        "return { shape: all.map(e => e.tagName + '.' + e.className).join('|'), marks: document.querySelectorAll('.fuori').length, dirty: all.filter(e => e.hasAttribute('onclick')).length, " +
        "color: warn ? getComputedStyle(warn).color : '', subject: document.querySelector('h1').textContent, roots: all.filter(e => e.shadowRoot).length }; })()";

    /// <summary>
    /// U-01 nel motore vero (WebView2 senza finestra, lo stesso di T-01): per ogni email costruita per uscire, la pagina vera
    /// del riquadro con una fascia rossa vera. Fuori dalla shadow root dell'email il DOM deve essere quello della pagina
    /// dello stesso messaggio con un corpo innocuo, carattere per carattere, e il colore della fascia quello di prima; in
    /// nessun punto, dentro o fuori, un elemento con un attributo di evento (la pulizia li toglie a tutti quelli che vede).
    /// </summary>
    static void U1Engine()
    {
        var candidates = U1Candidates();
        string deep = string.Concat(Enumerable.Repeat("<div>", 600)) + "x";
        // Il carico in un attributo, e lo stesso con le maiuscole: dopo la guardia il motore deve rileggere lo stesso valore.
        string upper = U1Payload.ToUpperInvariant();
        string attributes = $"<a id=\"uno\" title=\"{WebUtility.HtmlEncode(U1Payload)}\">x</a><a id=\"due\" title=\"{WebUtility.HtmlEncode(upper)}\" data-nota=\"&lt;/template\">y</a>";
        const string attributesReading = "(() => { const r = document.querySelector('.mail').shadowRoot; return [r.getElementById('uno').title, r.getElementById('due').title, r.getElementById('due').dataset.nota]; })()";
        // xmp e pre, e i due che non si vedono: come li disegna il motore.
        const string drawn = "<!doctype html><html><body><xmp id=\"a\">x</xmp><pre id=\"b\">x</pre><noembed id=\"c\">x</noembed><noframes id=\"d\">x</noframes></body></html>";
        const string drawnReading =
            "(() => { const s = id => getComputedStyle(document.getElementById(id)); const look = c => [c.display, c.fontFamily, c.fontSize, c.whiteSpace, c.marginTop, c.marginBottom, c.marginLeft, c.unicodeBidi].join(', '); " +
            "return [look(s('a')), look(s('b')), s('c').display, s('d').display]; })()";
        var (version, seen, normal, plain, read, look, hostile, harmless) = Sec2Pump(async () =>
        {
            var engine = await Sec2Engine.StartAsync();
            try
            {
                async Task<U1Seen> ReadAsync(string what, string html)
                {
                    var page = U1Page(html);
                    await engine.ShowAsync(page, settle: 0);
                    var r = await engine.ScriptAsync(U1Reading);
                    var fragment = MailHtml.Prepare(html, false, false, _ => null);
                    return new U1Seen(what, fragment.PlainOnly, fragment.Fragment, r.GetProperty("light").GetString()!.Replace(page.Token, "T"), r.GetProperty("marks").GetInt32(), r.GetProperty("dirty").GetInt32(),
                                      r.GetProperty("inside").GetInt32(), r.GetProperty("shadow").GetString()!, r.GetProperty("templates").GetInt32(), r.GetProperty("color").GetString()!, r.GetProperty("display").GetString()!);
                }
                var reference = await ReadAsync("riferimento", "<p>Prova</p>");
                var plainReference = await ReadAsync("riferimento, solo testo", deep);
                var list = new List<U1Seen>();
                foreach (var (what, html) in candidates) list.Add(await ReadAsync(what, html));
                await engine.ShowAsync(U1Page(attributes), settle: 0);
                var values = (await engine.ScriptAsync(attributesReading)).EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                await engine.ShowRawAsync(drawn);
                var styles = (await engine.ScriptAsync(drawnReading)).EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                await engine.ShowAsync(U1FieldsPage("\"></span></div></article>" + U1Payload + "<!--"), settle: 0);
                var fields = await engine.ScriptAsync(U1FieldsReading);
                await engine.ShowAsync(U1FieldsPage("Prova"), settle: 0);
                var plainFields = await engine.ScriptAsync(U1FieldsReading);
                return (engine.Version, list, reference, plainReference, values, styles, fields, plainFields);
            }
            finally
            {
                engine.Close();
            }
        }, seconds: 240);

        bool Out(U1Seen s) => s.Light != (s.Plain ? plain : normal).Light || s.Marks > 0 || s.Templates > 0 || s.Color != normal.Color || s.Display != normal.Display || s.Inside < 0;
        var outside = seen.Where(Out).ToList();
        var dirty = seen.Where(s => s.Dirty > 0).ToList();
        // Con MISSIVA_PROVA_USCITE=file ogni candidato finisce nel file, con il frammento pulito e quello che il motore ne ha fatto.
        if (Environment.GetEnvironmentVariable("MISSIVA_PROVA_USCITE") is { Length: > 0 } dump)
        {
            static string Cut(string s) => string.Concat((s.Length > 1500 ? s[..1500] + "..." : s).Select(c => c < ' ' && c != '\n' || char.IsSurrogate(c) ? '?' : c));
            var text = new StringBuilder($"motore {version}; riferimento: colore {normal.Color}, display {normal.Display}\nLIGHT di riferimento:\n{normal.Light}\n\n");
            foreach (var s in seen)
                text.Append($"=== {s.What}\n    esce: {(Out(s) ? "SÌ" : "no")}; segni fuori {s.Marks}, mai visti dalla pulizia {s.Dirty}, segni dentro {s.Inside}, template fuori {s.Templates}, colore {s.Color}, display {s.Display}, solo testo {s.Plain}, " +
                            $"DOM fuori uguale {s.Light == (s.Plain ? plain : normal).Light}\n    FRAMMENTO: {Cut(s.Fragment)}\n    SHADOW: {Cut(s.Shadow)}\n" +
                            (s.Light == (s.Plain ? plain : normal).Light ? "" : $"    LIGHT: {Cut(s.Light)}\n"));
            File.WriteAllText(dump, text.ToString());
        }
        Report.Add($"      (motore: WebView2 {version}; {seen.Count} email di prova; colore della fascia rossa vera {normal.Color})");
        Check(seen.Count > 200 && outside.Count == 0,
              $"U-01, {seen.Count} email costruite per uscire dalla shadow root, nel motore vero: fuori dall'email il DOM della pagina è quello di un messaggio innocuo, carattere per carattere, e la fascia rossa ha il suo colore" +
              (outside.Count == 0 ? "" : $": escono {outside.Count}, " + string.Join("; ", outside.Select(s => s.What))));
        Check(dirty.Count == 0,
              "U-01, in quelle pagine nessun elemento che la pulizia non ha mai visto arriva al motore (nessun attributo di evento, né dentro né fuori dalla shadow root)" +
              (dirty.Count == 0 ? "" : $": in {dirty.Count}, " + string.Join("; ", dirty.Select(s => s.What))));
        // La guardia non cambia il significato di un attributo, e non lascia la sequenza in nessun frammento.
        var sealedLeft = seen.Where(s => s.Fragment.Contains("</template", StringComparison.OrdinalIgnoreCase)).Select(s => s.What).ToList();
        Check(read.Count == 3 && read[0] == U1Payload && read[1] == upper && read[2] == "</template" && sealedLeft.Count == 0,
              $"U-01, la guardia finale: \"</template\" non compare nel frammento di nessuna delle {seen.Count} email, e un attributo che la contiene il motore lo rilegge uguale, anche in maiuscolo" +
              (sealedLeft.Count == 0 ? "" : ": resta in " + string.Join("; ", sealedLeft)));
        Check(look.Count == 4 && look[0] == look[1] && look[2] == "none" && look[3] == "none",
              $"U-01, un xmp scritto come pre si vede uguale (per il motore: {(look.Count > 1 ? look[1] : "?")}), e noembed e noframes, che non si scrivono più, non si vedevano ({string.Join(", ", look.Skip(2))})" +
              (look.Count == 4 && look[0] != look[1] ? $": xmp {look[0]}" : ""));
        // Quello che la pagina scrive dall'email senza passare dalla pulizia.
        string shape = hostile.GetProperty("shape").GetString()!;
        Check(shape == harmless.GetProperty("shape").GetString() && shape.Split('|').Length > 60 && hostile.GetProperty("marks").GetInt32() == 0 && hostile.GetProperty("dirty").GetInt32() == 0
              && hostile.GetProperty("color").GetString() == normal.Color && hostile.GetProperty("subject").GetString() == "\"></span></div></article>" + U1Payload + "<!--" && hostile.GetProperty("roots").GetInt32() == 1,
              $"U-01, oggetto, mittente, anteprima, destinatari, categorie, nomi degli allegati, fasce e account con dentro la chiusura dei contenitori e del template: la pagina ha gli stessi {shape.Split('|').Length} elementi di quella con testi innocui, e il motore legge l'oggetto com'è scritto");
    }

    /// <summary>U-01 senza il motore: che cosa scrive la pulizia per il riquadro e che cosa resta nell'HTML che riparte.</summary>
    static void U1Cleaning()
    {
        static string Pane(string html) => MailHtml.Prepare(html, false, false, _ => null).Fragment;
        // L'HTML in uscita riscritto dall'albero: con uno script da togliere (senza nulla da togliere torna la stessa stringa).
        static string Sent(string html) => MailHtml.Outgoing("<script>x</script>" + html);
        const string head = "<html><head></head><body>", foot = "</body></html>";

        // I commenti: mai nella pagina del riquadro, sempre nell'HTML che riparte (quelli condizionali servono a Outlook).
        const string outlook = "<p>Notizie</p><!--[if mso]><table><tr><td>Solo Outlook</td></tr></table><![endif]--><!-- nota --><p>Fine</p><!--[if !mso]><!--><p>Gli altri</p><!--<![endif]-->";
        string same = MailHtml.Outgoing(outlook);
        Check(Pane(outlook) == "<div class=\"missiva-body\"><p>Notizie</p><p>Fine</p><p>Gli altri</p></div>" && ReferenceEquals(same, outlook) && Sent(outlook) == head + outlook + foot,
              "U-01, i commenti: nella pagina del riquadro non si scrivono (nemmeno quelli condizionali di Outlook, che il motore ignora); nell'HTML che riparte in una risposta o in un inoltro restano tutti, com'erano");

        // Il contenuto grezzo: xmp come pre con il testo scritto come testo; noembed e noframes no; in uscita xmp resta com'era.
        const string raw = "<p>a</p><xmp class=\"codice\" onclick=\"x()\">\n<b>1 & 2</b> &amp; </xmp><noembed><i>b</i></noembed><noframes><i>c</i></noframes><p>d</p>";
        Check(Pane(raw) == "<div class=\"missiva-body\"><p>a</p><pre class=\"codice\">\n\n&lt;b&gt;1 &amp; 2&lt;/b&gt; &amp;amp; </pre><p>d</p></div>"
              && Pane("<svg><xmp><a href=\"https://www.esempio-prova.example/\">x</a></xmp><rect/></svg><math><xmp><mi>y</mi></xmp></math>") == "<div class=\"missiva-body\"><svg><rect></rect></svg><math></math></div>"
              && Sent("<xmp>\n<b>1 & 2</b></xmp><p>d</p>") == head + "<xmp>\n<b>1 & 2</b></xmp><p>d</p>" + foot,
              "U-01, il contenuto grezzo: nella pagina del riquadro un xmp è un pre con lo stesso testo, scritto come testo (e con i suoi attributi, puliti); noembed, noframes e un xmp dentro un disegno o una formula non si scrivono; " +
              "nell'HTML che riparte xmp resta com'era");

        // template: mai nella pagina del riquadro. Nell'HTML che riparte restava com'era, con il contenuto non pulito; dalle
        // scelte del 14 ottobre si toglie anche lì (U1OutgoingTemplate), e il ramo che ne scriveva il contenuto non c'è più.
        const string template = "<p>a</p><template><p onclick=\"x()\">dentro</p><template>più dentro</template></template><p>b</p>";
        string sentTemplate = Sent(template);
        Check(Pane(template) == "<div class=\"missiva-body\"><p>a</p><p>b</p></div>" && !Pane("<svg><template><rect/></template></svg><table><template><tr></tr></template></table>").Contains("template")
              && sentTemplate == head + "<p>a</p><p>b</p>" + foot,
              "U-01, template: nella pagina del riquadro non si scrive mai, in nessun punto e con nessun contenuto; nell'HTML che riparte, dalle scelte del 14 ottobre, nemmeno (prima restava com'era, con quello che conteneva, non pulito)");

        // La guardia da sola.
        (string Before, string After)[] guarded =
        [
            ("</template>", "&lt;/template>"), ("a</TEMPLATE >b", "a&lt;/TEMPLATE >b"), ("</TeMpLaTe", "&lt;/TeMpLaTe"), ("<a title=\"</template></template>\">", "<a title=\"&lt;/template>&lt;/template>\">"),
            ("x</templatex></template\n>y", "x&lt;/templatex>&lt;/template\n>y"), ("<</template", "<&lt;/template"),
        ];
        string[] untouched = ["", "<p>template</p>", "<template>", "< /template>", "</ template>", "</templat>", "&lt;/template&gt;", "<\\/template>", "</temp" + (char)0 + "late>", "\\3C/template>"];
        var wrong = guarded.Where(g => MailHtml.Sealed(g.Before) != g.After).Select(g => g.Before).Concat(untouched.Where(u => !ReferenceEquals(MailHtml.Sealed(u), u))).ToList();
        var result = new MailHtml.Result("<p title=\"</template>\">x</p>", 0, []);
        var fragmentProperty = typeof(MailHtml.Result).GetProperty(nameof(MailHtml.Result.Fragment))!;
        Check(wrong.Count == 0 && result.Fragment == "<p title=\"&lt;/template>\">x</p>" && (result with { PlainOnly = true }).Fragment == result.Fragment && !fragmentProperty.CanWrite
              && Pane("<a title=\"&lt;/template&gt;x\" href=\"https://www.esempio-prova.example/&lt;/template&gt;\">y</a>") == "<div class=\"missiva-body\"><a title=\"&lt;/template>x\" href=\"https://www.esempio-prova.example/%3C/template%3E\">y</a></div>",
              $"U-01, la guardia finale: \"</template\", comunque scritto, esce con \"&lt;\" al posto di \"<\" ({guarded.Length} forme) e il resto non si tocca ({untouched.Length} testi tornano la stessa stringa); " +
              "un frammento nasce già passato di lì e non si riassegna" + (wrong.Count == 0 ? "" : ": sbagliati " + string.Join(" | ", wrong)));

        // I testi di prova che la suite ha già: la guardia non ne tocca nessuno, e nessuno ha più commenti o contenuto grezzo.
        if (U1Samples() is { } samples)
        {
            int fragments = 0, touched = 0, comments = 0, rawText = 0;
            foreach (var html in samples)
                foreach (bool dark in new[] { false, true })
                {
                    var fragment = MailHtml.Prepare(html, dark, false, _ => null).Fragment;
                    fragments++;
                    // Dove la guardia è passata resta "&lt;/template": se non c'è, il frammento è quello di prima della guardia, byte per byte.
                    if (fragment.Contains("&lt;/template", StringComparison.OrdinalIgnoreCase)) touched++;
                    // Riletto dall'analisi: "<!--" può stare in un attributo o nel foglio di stile, dove non è un commento.
                    var reread = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(fragment);
                    if (reread.Descendants().Any(n => n is AngleSharp.Dom.IComment)) comments++;
                    if (reread.QuerySelectorAll("xmp,noembed,noframes,template,plaintext,textarea,iframe,script,noscript,title").Length > 0) rawText++;
                }
            Check(samples.Count > 400 && touched == 0 && comments == 0 && rawText == 0,
                  $"U-01, i {samples.Count} testi HTML che le prove hanno già, puliti per il tema chiaro e per lo scuro ({fragments} frammenti): la guardia non ne tocca nessuno ({touched}), e nessuno ha commenti ({comments}) o elementi dal contenuto grezzo ({rawText})");
        }
        else Report.Add("      (sorgenti non trovati accanto all'eseguibile: i testi HTML delle altre prove non sono stati riletti)");

        // Il costo della guardia su un frammento grande senza la sequenza: una ricerca.
        string source = "<table>" + string.Concat(Enumerable.Repeat("<tr><td style=\"padding:4px\"><a href=\"https://www.esempio-prova.example/pagina\">Offerta</a> del giorno</td><td>12,50</td></tr><!-- riga -->", 8000)) + "</table>";
        Pane(source);
        // La pulizia intera dello stesso messaggio (48.000 elementi e 8.000 commenti), per confronto: il secondo giro.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string big = Pane(source);
        double whole = clock.Elapsed.TotalMilliseconds;
        MailHtml.Sealed(big);
        clock.Restart();
        for (int i = 0; i < 20; i++) MailHtml.Sealed(big);
        double each = clock.Elapsed.TotalMilliseconds / 20;
        Check(each < 20, $"U-01, il costo della guardia su un frammento di {big.Length / 1024} KB senza la sequenza: {each:0.00} ms, una volta per corpo (la pulizia di quel messaggio, {source.Length / 1024} KB, ne costa {whole:0})");

        // Dove il frammento entra nella pagina: un punto solo, e ci arriva da un Result.
        if (Ch1Code("UI", "ReadingPage.cs") is { } page)
        {
            int templates = System.Text.RegularExpressions.Regex.Matches(page, "shadowrootmode").Count;
            Check(templates == 1 && page.Contains("<template shadowrootmode=\\\"open\\\"><style>{ShadowCss(light)}</style>{prepared.Fragment}</template>") && page.Contains("var prepared = done.Result;")
                  && System.Text.RegularExpressions.Regex.Matches(page, @"\.Fragment\b").Count == 1,
                  $"U-01, nella pagina del riquadro il template di una shadow root si scrive in un punto solo ({templates}), e dentro ci va solo il frammento di un MailHtml.Result");
        }
        else Report.Add("      (sorgenti non trovati accanto all'eseguibile: il punto in cui il frammento entra nella pagina non è stato letto)");
    }

    // ---------------------------------------------------------------- U-02: inoltri verso fuori già presenti, regole riaccese, Esegui ora

    /// <summary>
    /// Com'era in RuleWindow.SaveAsync fino al 14 ottobre 2026, per il confronto: gli indirizzi esterni da confermare erano
    /// quelli che la regola in modifica non aveva già (e la casella che riaccende una regola non chiedeva nulla).
    /// </summary>
    static List<string> U2Before(MailRule? editing, MailRule rule, MailAccount account) =>
        Rules.ExternalForwards(rule, account).Where(e => editing == null || !Rules.ExternalForwards(editing, account).Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// U-02: una regola di Outlook creata altrove (da chi è entrato nella casella: è il modo classico di restarci) che manda
    /// la posta fuori. Prima la conferma compariva solo per gli indirizzi aggiunti nella finestra, e nominava solo quelli;
    /// la casella dell'elenco riaccendeva una regola spenta senza chiedere. Qui la regola della domanda (Rules.ForwardQuestion),
    /// la casella con il gestore HTTP finto di Graph (RulesView.EnableAsync), e la domanda di "Esegui ora".
    /// </summary>
    static async Task U2ForwardsAsync()
    {
        var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.it", Tenant = "organizations" };
        var personal = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@outlook.it" };
        const string redirect = "archivio.7731@esterno-prova.example", attached = "copia@altro-prova.example", added = "nuovo@terzo-prova.example";
        static System.Text.Json.Nodes.JsonObject Person(string address) => new() { ["emailAddress"] = new System.Text.Json.Nodes.JsonObject { ["address"] = address, ["name"] = address } };
        // Come la dà Graph: senza condizioni, reindirizza e inoltra come allegato (le due azioni che la finestra di Missiva non mostra nei campi).
        MailRule Planted(bool enabled) => Rules.FromGraph(new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = "r9", ["displayName"] = "Archivio", ["sequence"] = 1, ["isEnabled"] = enabled, ["conditions"] = new System.Text.Json.Nodes.JsonObject(),
            ["actions"] = new System.Text.Json.Nodes.JsonObject { ["redirectTo"] = new System.Text.Json.Nodes.JsonArray(Person(redirect)), ["forwardAsAttachmentTo"] = new System.Text.Json.Nodes.JsonArray(Person(attached)) },
        }, work.Id);
        MailRule With(MailRule rule, Action<MailRule> change)
        {
            var copy = rule.Copy();
            change(copy);
            return copy;
        }
        MailRule dormant = Planted(false), active = Planted(true);

        // La casella che riaccende: tutti gli indirizzi, anche quelli nascosti. Con la regola di prima nessuno era "nuovo".
        var turnOn = Rules.ForwardQuestion(dormant, With(dormant, r => r.Enabled = true), work);
        int before = U2Before(dormant, With(dormant, r => r.Enabled = true), work).Count;
        Check(Rules.ExternalForwards(dormant, work).SequenceEqual([redirect, attached]) && before == 0
              && turnOn is { Confirm: "Accendi la regola" } on && on.Body.Contains(redirect) && on.Body.Contains(attached) && on.Body.Contains("tutti i messaggi che arrivano") && on.Body.Contains("\"Archivio\""),
              $"U-02, una regola di Outlook spenta che reindirizza e inoltra come allegato fuori, riaccesa: la domanda c'è e nomina i due indirizzi (con la regola di prima gli indirizzi da confermare erano {before}, e la casella non chiedeva)");

        // Quando non si chiede: già accesa con gli stessi inoltri, spenta che resta spenta, spegnere, inoltro dentro l'organizzazione, nessun inoltro.
        var inside = With(active, r => { r.RedirectTo = []; r.ForwardAsAttachmentTo = []; r.ForwardTo = [new Address { Email = "anna@uffici.studio-prova.it" }]; });
        var plain = new MailRule { AccountId = work.Id, ServerId = "r3", Name = "Newsletter", SenderContains = ["newsletter"], MarkRead = true, Enabled = false };
        Check(Rules.ForwardQuestion(active, With(active, r => r.Name = "Archivio 2"), work) == null && Rules.ForwardQuestion(dormant, With(dormant, r => r.Name = "Archivio 2"), work) == null
              && Rules.ForwardQuestion(active, With(active, r => r.Enabled = false), work) == null && Rules.ForwardQuestion(null, inside, work) == null
              && Rules.ForwardQuestion(With(inside, r => r.Enabled = false), inside, work) == null && Rules.ForwardQuestion(plain, With(plain, r => r.Enabled = true), work) == null,
              "U-02, nessuna domanda quando nessun inoltro verso fuori diventa attivo: una regola già accesa salvata con gli stessi inoltri, una spenta che resta spenta, spegnere, un inoltro dentro l'organizzazione, una regola che non inoltra");

        // Un indirizzo nuovo: la domanda li nomina tutti (prima solo quello aggiunto); una regola nuova; un account personale; una regola spenta.
        var more = With(active, r => r.ForwardTo = [new Address { Email = added }]);
        var grown = Rules.ForwardQuestion(active, more, work);
        var fresh = Rules.ForwardQuestion(null, With(plain, r => { r.Enabled = true; r.ForwardTo = [new Address { Email = added }]; }), work);
        var friend = Rules.ForwardQuestion(null, new MailRule { Name = "Copia", ForwardTo = [new Address { Email = "giulia@outlook.it" }] }, personal);
        var asleep = Rules.ForwardQuestion(dormant, With(dormant, r => r.ForwardTo = [new Address { Email = added }]), work);
        Check(U2Before(active, more, work).SequenceEqual([added]) && grown is { Confirm: "Inoltra a 3 indirizzi" } g && g.Body.Contains(added) && g.Body.Contains(redirect) && g.Body.Contains(attached)
              && fresh is { } f && f.Confirm == "Inoltra a " + added && f.Body.StartsWith("I messaggi che corrispondono verranno inoltrati a " + added + ", fuori dalla tua organizzazione")
              // Senza condizioni, dalle scelte del 14 ottobre, la domanda dice prima che la regola agirà su ogni messaggio (U2AllMessages).
              && friend is { } p && p.Body.Contains("fuori dalla tua casella") && p.Body.StartsWith("Non hai scelto condizioni: la regola agirà su ogni messaggio che arriva nella Posta in arrivo, e tutti verranno inoltrati")
              && asleep is { Confirm: "Salva" } s && s.Body.StartsWith("La regola è spenta") && s.Body.Contains(redirect) && s.Body.Contains(added),
              "U-02, un indirizzo esterno nuovo in una regola che ne ha già di nascosti: la domanda li nomina tutti e tre (prima solo quello aggiunto); una regola nuova e un account personale chiedono come prima " +
              "(senza condizioni, dalle scelte del 14 ottobre, dicendo anche che la regola agirà su ogni messaggio); in una regola spenta lo dice");

        // La casella, con Graph finto: senza il sì nessuna richiesta parte.
        var requests = new List<(HttpMethod Method, string Path, string Body)>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            requests.Add((request.Method, Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..], request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("", Encoding.UTF8, "application/json") };
        }));
        var graph = new GraphProvider(work);
        var asked = new List<string>();
        MailRule? refused, accepted, off, quiet;
        int afterNo, afterYes;
        (HttpMethod Method, string Path, string Body) patch;
        try
        {
            GraphProvider.TestSettingsConsent = true;
            refused = await UI.RulesView.EnableAsync(graph, dormant, true, question => { asked.Add(question.Body); return false; });
            afterNo = requests.Count;
            accepted = await UI.RulesView.EnableAsync(graph, dormant, true, question => { asked.Add(question.Body); return true; });
            patch = requests.LastOrDefault();
            afterYes = requests.Count;
            off = await UI.RulesView.EnableAsync(graph, active, false, question => { asked.Add(question.Body); return false; });
            quiet = await UI.RulesView.EnableAsync(graph, plain, true, question => { asked.Add(question.Body); return false; });
        }
        finally
        {
            GraphProvider.TestSettingsConsent = false;
        }
        Check(refused == null && afterNo == 0 && accepted is { Enabled: true } && afterYes == 1 && patch.Method == HttpMethod.Patch && patch.Path == "me/mailFolders/inbox/messageRules/r9"
              && patch.Body.Replace(" ", "") == "{\"isEnabled\":true}" && asked.Count == 2 && asked.All(a => a.Contains(redirect) && a.Contains(attached))
              && off is { Enabled: false } && quiet is { Enabled: true } && requests.Count == 3,
              $"U-02, la casella dell'elenco su una regola spenta che manda la posta fuori: senza il sì nessuna richiesta a Outlook ({afterNo}), con il sì la regola si accende ({afterYes} PATCH); spegnere, e accendere una regola che non inoltra, non chiedono ({asked.Count} domande in tutto)");

        // Le due strade passano di lì, e la finestra non accende e non spegne.
        if (Ch1Code("UI", "RuleWindow.cs") is { } window && Ch1Code("UI", "RulesView.cs") is { } view)
        {
            int question = window.IndexOf("Rules.ForwardQuestion(editing, rule, provider.Account)", StringComparison.Ordinal), saved = window.IndexOf("provider.SaveRuleAsync(editing, rule)", StringComparison.Ordinal);
            int put = window.IndexOf("Rules.Put(rule)", StringComparison.Ordinal);
            int viewSaves = System.Text.RegularExpressions.Regex.Matches(view, @"SaveRuleAsync\(rule, after\)|Rules\.Put\(").Count;
            string enable = view[view.IndexOf("static async Task<MailRule?> EnableAsync", StringComparison.Ordinal)..];
            enable = enable[..enable.IndexOf("\n    }", StringComparison.Ordinal)];
            Check(question > 0 && saved > question && put > question && !window.Contains(".Enabled =") && !window.Contains("ExternalForwards(")
                  && viewSaves == 2 && enable.Contains("SaveRuleAsync(rule, after)") && enable.Contains("Rules.Put(after)") && enable.IndexOf("Rules.ForwardQuestion(rule, after, provider.Account)", StringComparison.Ordinal) is > 0 and var q
                  && q < enable.IndexOf("SaveRuleAsync", StringComparison.Ordinal) && view.Contains("await EnableAsync(host.Provider, rule, on,"),
                  "U-02, nei sorgenti: la finestra della regola chiede (Rules.ForwardQuestion) prima di salvare e non accende né spegne; nell'elenco accendere e spegnere passano da un punto solo (EnableAsync), che chiede prima di salvare");
        }
        else Report.Add("      (sorgenti non trovati accanto all'eseguibile: le due strade del salvataggio di una regola non sono state lette)");

        // "Esegui ora": la domanda dice per prima cosa quanti messaggi vanno nel Cestino, e che è il Cestino. Con una regola
        // sola che elimina, il numero della domanda è quello dei messaggi che ci finiscono (server IMAP finto).
        using var fake = new FakeImap(new Flavor(Gmail: false));
        bool plainBefore = ImapProvider.TestPlain;
        ImapProvider.TestPlain = true;
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        MailCache.Delete(account);
        try
        {
            for (int i = 0; i < 3; i++) fake.Add("INBOX", SpeedMail($"Offerta {i + 1}", "Sconto"));
            var other = new MimeKit.MimeMessage { Subject = "Pranzo", Date = DateTimeOffset.Now, Body = new MimeKit.TextPart("html") { Text = "<p>Ci sei?</p>" } };
            other.From.Add(new MimeKit.MailboxAddress("Ada", "ada.prova@gmail.com"));
            fake.Add("INBOX", other);
            using var imap = new ImapProvider(account);
            var folders = await imap.GetFoldersAsync();
            var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
            var bin = folders.First(f => f.Kind == FolderKind.Deleted);
            var listed = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            var rule = new MailRule { AccountId = account.Id, Name = "Offerte", SubjectContains = ["offerta"], Delete = true, MarkRead = true };
            string description = Rules.Describe(rule), text = "";
            int binBefore = fake.Folders[bin.Id].Count;
            var outcome = await Rules.RunAsync(imap, listed, [rule], () => Task.FromResult(folders), confirm: n => { text = UI.RulesView.RunQuestion(rule, inbox, n, listed.Count, description); return Task.FromResult(true); });
            int inBin = fake.Folders[bin.Id].Count - binBefore;
            var move = new MailRule { Name = "Clienti", SubjectContains = ["offerta"], MoveTo = "x", MoveToName = "Clienti" };
            Check(listed.Count == 4 && outcome.Removed.Count == 3 && inBin == 3 && fake.Folders["INBOX"].Count == 1
                  && text == $"La regola mette nel Cestino 3 dei 4 messaggi di {inbox.Name}: nel Cestino, non per sempre, da lì si riprendono.\n\nSe l'oggetto contiene \"offerta\": elimina (nel Cestino), segna come letto"
                  && UI.RulesView.RunQuestion(move, inbox, 3, 4, Rules.Describe(move)) == $"La regola cambia 3 dei 4 messaggi di {inbox.Name}.\n\nSe l'oggetto contiene \"offerta\": sposta in Clienti"
                  && UI.RulesView.RunQuestion(new MailRule { Delete = true }, inbox, 4, 4, "x").Contains("nel Cestino 4 dei 4") && UI.RulesView.RunQuestion(new MailRule { Delete = true }, inbox, 4, 4, "x").Contains("vale per ogni messaggio")
                  && UI.RulesView.RunQuestion(rule, bin, 0, 2, description).StartsWith("La regola cambia 0 dei 2"),
                  $"U-02, \"Esegui ora\" con una regola che elimina: la domanda dice per prima cosa \"mette nel Cestino 3 dei 4 messaggi\" (nel Cestino, non per sempre), e nel Cestino del server ne arrivano {inBin}; per le altre regole la frase è quella di prima");
        }
        finally
        {
            ImapProvider.TestPlain = plainBefore;
            MailCache.Delete(account);
            BodyCache.Delete(account);
        }
    }

    // ---------------------------------------------------------------- U-03: allegati nella cartella temporanea, salvataggi

    /// <summary>
    /// Una giunzione (un collegamento a un'altra cartella, che un utente crea senza permessi in più) da link verso target,
    /// tutte e due in una cartella di prova. Con mklink di cmd.exe, chiamato con il suo percorso intero e senza finestra.
    /// </summary>
    static bool U3Junction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        process.WaitForExit(10_000);
        return process.ExitCode == 0 && Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>Toglie una giunzione di prova (solo il collegamento) se c'è ancora.</summary>
    static void U3Unlink(string link)
    {
        try { if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Directory.Delete(link, false); } catch (Exception) { }
    }

    /// <summary>
    /// U-03: gli allegati aperti con un altro programma (cartella temporanea) e "Salva tutti". Tutto in una cartella di
    /// prova fra i file temporanei, mai in quella vera di Missiva; le giunzioni puntano solo ad altre cartelle di prova.
    /// </summary>
    static void U3TempFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "missiva-prova-u03-" + Guid.NewGuid().ToString("N")[..8]);
        string[] links = [Path.Combine(root, "Missiva", "collegata"), Path.Combine(root, "Missiva", "vera", "dentro"), Path.Combine(root, "Missiva-collegata")];
        try
        {
            // ---- La pulizia e i collegamenti.
            // (a) Dentro la cartella degli allegati: una sottocartella che è una giunzione, e una giunzione dentro una sottocartella vera.
            string temp = Path.Combine(root, "Missiva"), elsewhere = Path.Combine(root, "altrove"), deeper = Path.Combine(root, "altrove-2");
            foreach (var dir in new[] { temp, elsewhere, deeper, Path.Combine(elsewhere, "pratiche"), Path.Combine(temp, "vera") }) Directory.CreateDirectory(dir);
            string kept = Path.Combine(elsewhere, "pratiche", "Contratto.docx"), keptToo = Path.Combine(deeper, "Bilancio.xlsx");
            File.WriteAllText(kept, "prova");
            File.WriteAllText(keptToo, "prova");
            File.WriteAllText(Path.Combine(temp, "vera", "Allegato.pdf"), "prova");
            bool made = U3Junction(links[0], elsewhere) && U3Junction(links[1], deeper);
            // Con il limite di un'ora la data che conta non è quella dei file della cartella di arrivo (appena scritti): le due sottocartelle sono "vecchie".
            Directory.SetCreationTimeUtc(Path.Combine(temp, "vera"), DateTime.UtcNow.AddHours(-3));
            File.SetLastWriteTimeUtc(Path.Combine(temp, "vera", "Allegato.pdf"), DateTime.UtcNow.AddHours(-3));
            Safety.CleanTemp(TimeSpan.FromHours(1), temp);
            bool agedOut = !File.Exists(Path.Combine(temp, "vera", "Allegato.pdf")) && !Directory.Exists(links[0]);
            Safety.CleanTemp(TimeSpan.Zero, temp); // .NET, tolta una giunzione trovata più dentro, lascia la cartella vuota per il giro dopo
            bool insideSafe = File.Exists(kept) && File.Exists(keptToo), cleaned = Directory.GetFileSystemEntries(temp).Length == 0;

            // (b) La cartella degli allegati è lei stessa una giunzione verso un'altra cartella, con le sue sottocartelle.
            string victim = Path.Combine(root, "documenti");
            Directory.CreateDirectory(Path.Combine(victim, "Clienti"));
            string client = Path.Combine(victim, "Clienti", "Rossi.docx");
            File.WriteAllText(client, "prova");
            bool madeRoot = U3Junction(links[2], victim);
            Safety.CleanTemp(TimeSpan.Zero, links[2]);
            bool rootSafe = File.Exists(client);
            string refused = "";
            try { Safety.NewTempFolder("Fattura.pdf", links[2]); }
            catch (IOException ex) { refused = ex.Message; }
            bool nothingWritten = Directory.GetFileSystemEntries(victim).Length == 1;
            Check(made && madeRoot && insideSafe && agedOut && cleaned && rootSafe,
                  $"U-03, la pulizia degli allegati temporanei e le giunzioni: con una giunzione fra le sottocartelle e una più dentro se ne vanno i collegamenti e i file delle cartelle di arrivo restano ({insideSafe}); " +
                  $"se la cartella degli allegati è lei una giunzione, le sottocartelle della cartella di arrivo restano ({rootSafe}: prima la pulizia le cancellava con quello che contenevano)");
            Check(madeRoot && refused.Contains("è un collegamento a un'altra cartella") && nothingWritten,
                  "U-03, se la cartella degli allegati temporanei di Missiva è una giunzione, un allegato non ci si scrive: l'errore lo dice, e nella cartella di arrivo non compare nulla");

            // ---- La cartella nuova di un allegato.
            string fresh = Path.Combine(root, "nuove");
            Directory.CreateDirectory(Path.Combine(fresh, "aaaaaaaa"));
            File.WriteAllText(Path.Combine(fresh, "aaaaaaaa", "Di un altro.docx"), "prova");
            // Com'era: Directory.CreateDirectory su un nome che c'è già non se ne accorge, e l'allegato finiva lì dentro.
            Directory.CreateDirectory(Path.Combine(fresh, "aaaaaaaa"));
            bool silent = File.Exists(Path.Combine(fresh, "aaaaaaaa", "Di un altro.docx"));
            int tried = 0;
            string made2 = Safety.NewTempFolder("Fattura.pdf", fresh, _ => tried++ == 0 ? "aaaaaaaa" : "bbbbbbbb");
            string always = "";
            try { Safety.NewTempFolder("Fattura.pdf", fresh, _ => "aaaaaaaa"); }
            catch (IOException ex) { always = ex.Message; }
            string normal = Safety.NewTempFolder("Fattura di ottobre.pdf", fresh);
            Check(silent && Path.GetFileName(made2) == "bbbbbbbb" && tried == 2 && Directory.GetFileSystemEntries(Path.Combine(fresh, "aaaaaaaa")).Length == 1 && always.Length > 0
                  && Path.GetFileName(normal).Length == 32 && Path.GetFileName(normal).All(Uri.IsHexDigit) && Directory.GetFileSystemEntries(normal).Length == 0,
                  $"U-03, la cartella di un allegato è nuova davvero: un nome che c'è già non si riusa (se ne prova un altro; prima il file finiva nella cartella che c'era), e il nome ha {Path.GetFileName(normal).Length} cifre casuali");

            // La lunghezza del nome e il percorso: mai più lungo di prima dove conta (Excel apre fino a 218 caratteri).
            string longName = new string('n', 146) + ".xls", real = Safety.TempDir;
            (int Root, int Name)[] cases = [(real.Length, 20), (real.Length, 60), (real.Length, 150), (40, 150), (40, 130), (40, 136), (55, 150), (55, 100), (80, 150), (120, 150)];
            var wrong = cases.Where(c =>
            {
                int length = Safety.TempNameLength(new string('r', c.Root), new string('n', c.Name)), path = c.Root + 1 + length + 1 + c.Name, before = c.Root + 1 + 8 + 1 + c.Name;
                // Entro 218 se ci stava anche prima; e con le cifre che ci stanno.
                return length is not (8 or 16 or 32) || before <= 218 && path > 218 || length < 32 && c.Root + 1 + 32 + 1 + c.Name <= 218 || length == 8 && c.Root + 1 + 16 + 1 + c.Name <= 218;
            }).ToList();
            string longFolder = Safety.NewTempFolder(longName, fresh);
            int realLong = Safety.TempNameLength(real, longName), realNormal = Safety.TempNameLength(real, "Fattura di ottobre.pdf");
            Check(wrong.Count == 0 && Path.GetFileName(longFolder).Length == Safety.TempNameLength(fresh, longName) && realNormal == 32,
                  $"U-03, il nome della cartella si accorcia quando con 32 cifre il percorso passerebbe i 218 caratteri che Excel apre: su questo PC la cartella degli allegati è lunga {real.Length} caratteri, " +
                  $"e un allegato dal nome di 150 caratteri ha un percorso di {real.Length + 1 + realLong + 1 + 150} con {realLong} cifre (con 8, com'era, {real.Length + 10 + 150}; con 32 sarebbe {real.Length + 34 + 150}); uno dal nome normale ne ha 32");

            // ---- Il file con il nome neutro finché non si apre.
            byte[] content = Encoding.UTF8.GetBytes("Gentile cliente, in allegato la fattura.");
            var written = Safety.WriteTemp("Fattura.docm", content, fresh);
            var waiting = Directory.GetFileSystemEntries(written.Folder).Select(Path.GetFileName).ToList();
            bool markedWaiting = written.Marked && File.ReadAllText(written.Pending + ":Zone.Identifier").Contains("ZoneId=3");
            string opened = written.Release();
            bool afterRelease = opened == Path.Combine(written.Folder, "Fattura.docm") && File.ReadAllBytes(opened).SequenceEqual(content) && !File.Exists(written.Pending)
                                && File.ReadAllText(opened + ":Zone.Identifier").Contains("ZoneId=3");
            var dropped = Safety.WriteTemp("Listino.xlsm", content, fresh);
            bool neverNamed = !File.Exists(dropped.Final);
            dropped.Discard();
            string format = new DriveInfo(Path.GetPathRoot(Path.GetTempPath())!).DriveFormat;
            Check(waiting is ["Fattura.docm.parte"] && markedWaiting && afterRelease && neverNamed && !Directory.Exists(dropped.Folder),
                  $"U-03, un allegato da aprire si scrive e si marca con un nome neutro ({string.Join(", ", waiting)}) e prende il suo solo quando si apre, con il marchio web che lo segue; rifiutato, il file con il suo nome non è mai esistito " +
                  $"(qui la cartella temporanea è su {format}, dove il marchio si scrive: {written.Marked})");

            // ---- "Salva tutti": mai sopra un file che c'è già.
            string chosen = Path.Combine(root, "scelta");
            Directory.CreateDirectory(Path.Combine(chosen, "Elenco.pdf")); // una cartella con il nome di un allegato
            File.WriteAllText(Path.Combine(chosen, "Contratto.pdf"), "il documento di prima");
            string first = Safety.WriteNewAsync(chosen, "Contratto.pdf", content).GetAwaiter().GetResult(), second = Safety.WriteNewAsync(chosen, "Contratto.pdf", content).GetAwaiter().GetResult();
            string free = Safety.WriteNewAsync(chosen, "Nuovo.pdf", content).GetAwaiter().GetResult(), besideFolder = Safety.WriteNewAsync(chosen, "Elenco.pdf", content).GetAwaiter().GetResult();
            // Una giunzione con il nome di un allegato, verso un'altra cartella di prova: il nome non si usa e là non arriva nulla.
            string away = Path.Combine(root, "lontano"), joined = Path.Combine(chosen, "Verbale.pdf");
            Directory.CreateDirectory(away);
            bool junction = U3Junction(joined, away);
            string besideLink = Safety.WriteNewAsync(chosen, "Verbale.pdf", content).GetAwaiter().GetResult();
            bool linkLeft = junction && Path.GetFileName(besideLink) == "Verbale (2).pdf" && Directory.GetFileSystemEntries(away).Length == 0;
            U3Unlink(joined);
            Check(linkLeft && File.ReadAllText(Path.Combine(chosen, "Contratto.pdf")) == "il documento di prima" && Path.GetFileName(first) == "Contratto (2).pdf" && Path.GetFileName(second) == "Contratto (3).pdf"
                  && Path.GetFileName(free) == "Nuovo.pdf" && Path.GetFileName(besideFolder) == "Elenco (2).pdf" && File.ReadAllBytes(first).SequenceEqual(content),
                  "U-03, \"Salva tutti\" in una cartella dove c'è già un file con il nome di un allegato: il file di prima resta com'è e l'allegato prende \" (2)\", poi \" (3)\" (era già così; ora il file si crea solo se non esiste, in un passo); lo stesso accanto a una cartella o a una giunzione con quel nome");
            // Un collegamento simbolico senza arrivo con il nome dell'allegato: per File.Exists non c'è. Serve il permesso di crearne (modalità sviluppatore).
            string target = Path.Combine(root, "fuori", "Preso.pdf"), link = Path.Combine(chosen, "Ricevuta.pdf"), targetBefore = Path.Combine(root, "fuori", "Preso prima.pdf"), linkBefore = Path.Combine(chosen, "Ricevuta di prima.pdf");
            Directory.CreateDirectory(Path.Combine(root, "fuori"));
            bool linked;
            try
            {
                File.CreateSymbolicLink(link, target);
                File.CreateSymbolicLink(linkBefore, targetBefore);
                linked = true;
            }
            catch (Exception) { linked = false; }
            if (linked)
            {
                // Com'era: si guardava File.Exists, poi si scriveva al nome.
                bool seen = File.Exists(linkBefore);
                File.WriteAllBytes(linkBefore, content);
                string beside = Safety.WriteNewAsync(chosen, "Ricevuta.pdf", content).GetAwaiter().GetResult();
                Check(!seen && File.Exists(targetBefore) && !File.Exists(target) && Path.GetFileName(beside) == "Ricevuta (2).pdf",
                      "U-03, \"Salva tutti\" con un collegamento simbolico senza arrivo che ha il nome dell'allegato: il file non si scrive più dove punta il collegamento (prima sì: per File.Exists il nome era libero), prende \" (2)\"");
            }
            else Report.Add("      (U-03: su questo PC non si possono creare collegamenti simbolici senza permessi in più: il caso del collegamento con il nome dell'allegato in \"Salva tutti\" non è stato provato)");
        }
        finally
        {
            // Prima i collegamenti, poi il resto: mai una cancellazione che li attraversi.
            foreach (var link in links) U3Unlink(link);
            try { Directory.Delete(root, true); } catch (Exception) { }
        }

        // Chi scrive nella cartella temporanea: solo Safety. E "Salva tutti" passa da WriteNewAsync.
        if (SourceRoot() is { } source)
        {
            var others = Directory.GetFiles(Path.Combine(source, "UI"), "*.cs").Concat(Directory.GetFiles(Path.Combine(source, "Core"), "*.cs")).Append(Path.Combine(source, "Program.cs"))
                .Where(f => Path.GetFileName(f) is var n && n != "Safety.cs" && !n.StartsWith("ProvaBozze")).Where(f => File.ReadAllText(f).Contains("Safety.TempDir")).Select(Path.GetFileName).ToList();
            string reading = Ch1Code("UI", "ReadingView.xaml.cs") ?? "", signed = Ch1Code("UI", "P7mWindow.cs") ?? "";
            Check(others.Count == 0 && reading.Contains("Safety.WriteTemp(name, bytes)") && signed.Contains("Safety.WriteTemp(name, result.Content)") && reading.Contains("await Safety.WriteNewAsync(dialog.FolderName,")
                  && !reading.Contains("Guid.NewGuid()") && !signed.Contains("Guid.NewGuid()"),
                  "U-03, nei sorgenti: nella cartella degli allegati temporanei scrive solo Safety (WriteTemp, dal riquadro e dalla finestra dei file firmati), e \"Salva tutti\" scrive con WriteNewAsync" +
                  (others.Count == 0 ? "" : ": la nominano anche " + string.Join(", ", others)));
        }
        else Report.Add("      (sorgenti non trovati accanto all'eseguibile: chi scrive nella cartella temporanea non è stato letto)");
    }

    // ---------------------------------------------------------------- U-04: la pagina del riquadro vuoto, e che cosa la pagina scrive senza codifica

    /// <summary>
    /// Le interpolazioni di un sorgente che non passano da E(...): i buchi delle stringhe $"..." e quelli, fra doppie graffe,
    /// della stringa grezza di ReadingPage.Document. Le righe di commento non contano.
    /// </summary>
    static List<string> U4Holes(string source)
    {
        var holes = new List<string>();
        bool raw = false;
        foreach (var line in source.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.TrimStart().StartsWith("//")) continue;
            if (line.Contains("$$\"\"\"")) raw = true;
            if (raw)
            {
                holes.AddRange(System.Text.RegularExpressions.Regex.Matches(line, @"\{\{((?:[^{}]|\{[^{}]*\})*)\}\}").Select(m => m.Groups[1].Value.Trim()));
                if (line.Contains("\"\"\"") && !line.Contains("$$\"\"\"")) raw = false;
                continue;
            }
            if (!line.Contains("$\"")) continue;
            for (int j = 0; j < line.Length; j++)
            {
                if (line[j] != '{') continue;
                if (j + 1 < line.Length && line[j + 1] == '{') { j++; continue; }
                int depth = 1, k = j + 1;
                for (; k < line.Length && depth > 0; k++) depth += line[k] == '{' ? 1 : line[k] == '}' ? -1 : 0;
                holes.Add(line[(j + 1)..(k - 1)].Trim());
                j = k - 1;
            }
        }
        return holes.Where(h => !(h.StartsWith("E(") && h.EndsWith(')'))).Distinct().ToList();
    }

    /// <summary>
    /// Le interpolazioni di ReadingPage.cs che possono non passare da E(...), lette una per una il 14 ottobre 2026: numeri,
    /// indici, colori calcolati (Hex, CategoryPalette), indirizzi delle azioni (origine, gettone esadecimale, percorso
    /// fisso), classi e testi fissi, pezzi di pagina già costruiti con E(...) (who, pic, cardTitle, html, mark, body), il
    /// frammento già pulito e passato dalla guardia, e i fogli di stile di Missiva. Nessuna porta com'è un testo che viene
    /// da un'email o dalle impostazioni. Una interpolazione nuova senza E(...) va letta e aggiunta qui, o codificata.
    /// </summary>
    static readonly string[] U4Allowed =
    [
            "CssFonts(look.Fonts)", "Px(look.FontSize)", "Px(2 * look.Down)", "Px(48)", "Hex(look.Glyph)", "Px(12)", "Hex(look.Text)", "f", "Hex(color)", "blocked", "ActionUrl(\"images/0\")", "i",
            "(entry.Expanded ? \" open\" : \"\")", "(toggles ? \" tg\" : \"\")", "Hex(Avatars.ColorOf(s.From))", "ActionUrl($\"person/{i}\")", "picClass", "picStyle", "cardTitle", "pic", "real",
            "tip", "tipEnd", "CategoryPalette.Hex(preset)", "CategoryPalette.TextHex(preset)", "who", "ActionUrl($\"toggle/{i}\")", "what", "ActionUrl($\"edit/{i}\")",
            "ActionUrl($\"unsubscribe/{i}\")", "System.Text.Json.JsonSerializer.Serialize(UnsubscribeRow(i, unsubscribe))", "pec.Signer", "(tone == \"warn\" ? \"\" : tone)", "glyph",
            "(invite.Cancelled ? \" cancelled\" : \"\")", "label", "html", "ActionUrl($\"consent/{i}\")", "ActionUrl($\"accept/{i}\")", "ActionUrl($\"tentative/{i}\")", "ActionUrl($\"decline/{i}\")",
            "ActionUrl($\"propose/{i}\")", "ActionUrl($\"open/{i}/{j}\")", "(dangerous ? \" danger\" : \"\")", "(dangerous ? \"Attenzione: questo tipo di file può avviare programmi\" : \"Apri\")",
            "j", "ActionUrl($\"saveall/{i}\")", "(label.Length > 3 ? \"5.5\" : \"7\")", "sheet", "edge", "color", "mark", "key", "Uri.EscapeDataString(cid)", "ShadowCss(light)", "prepared.Fragment",
            "(cls.Length > 0 ? \" \" + cls : \"\")", "tip.Split(' ')[0]", "Origin", "Token", "path", "ActionUrl(path)", "cls", "c.R:X2", "c.G:X2", "c.B:X2", "bytes", "bytes / 1024.0:0",
            "bytes / 1048576.0:0.#", "(light ? \"#005FB8\" : \"#6CB8F6\")", "(hold ? \" class=\\\"fitting\\\" style=\\\"scrollbar-gutter: stable\\\"\" : \"\")", "images",
            "(light ? \"light\" : \"dark\")", "paper", "ink", "subtle", "hover", "link", "press", "(light ? \"#FFF4CE\" : \"#433519\")", "(light ? \"#5C4400\" : \"#FCE100\")",
            "(light ? \"#FDE7E9\" : \"#442726\")", "(light ? \"#A4262C\" : \"#FF99A4\")", "(light ? \"#DFF6DD\" : \"#1F3B20\")", "(light ? \"#0E5C0E\" : \"#9FD89F\")", "rule", "thumb", "thumbHover",
            "body",
    ];

    /// <summary>
    /// U-04: ReadingPage.Empty metteva nella pagina il carattere e i due colori del segnaposto senza codifica. Vengono
    /// dalle risorse di WPF (ReadingView.ShowBlank), non da un'email né dalle impostazioni: nessuno ci guadagna, è una
    /// difesa in più. Con i valori di sempre la pagina deve restare quella di prima, carattere per carattere.
    /// </summary>
    static void U4EmptyPage()
    {
        // Com'era: il valore di font-family lo scriveva il riquadro, i colori erano testi "#RRGGBB" fatti lì.
        static string OldFonts(string source) => string.Join(",", source.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(f => $"'{f}'")) + ",'Segoe UI',sans-serif";
        static string OldHex(System.Windows.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        static System.Windows.Media.Color Rgb(int value) => System.Windows.Media.Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        (bool Light, string Fonts, double Size, int Glyph, int Text, double Zoom, double Down)[] looks =
        [
            (true, "Segoe UI", 14, 0xC9C7C5, 0xA9A7A6, 1, 0), (false, "Segoe UI", 14, 0xD1D1D1, 0xEAEAEA, 1, 0),
            (true, "Segoe UI Variable, Segoe UI", 14, 0xC9C7C5, 0xA9A7A6, 1.25, 7), (false, "Segoe UI Variable Text,Segoe UI Variable , Segoe UI", 15.5, 0xD1D1D1, 0xEAEAEA, 0.9, -3.5),
        ];
        var different = new List<string>();
        foreach (var look in looks)
        {
            string html = UI.ReadingPage.Empty(look.Light, new UI.EmptyLook(look.Fonts, look.Size, Rgb(look.Glyph), Rgb(look.Text), look.Zoom, look.Down)).Html;
            string Px(double v) => (v / look.Zoom).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "px";
            // Il segno della busta è un testo fisso della pagina: si prende da lì, il resto della riga è quello di prima.
            var glyph = System.Text.RegularExpressions.Regex.Match(html, "<span class=\"glyph\" style=\"[^\"]*\">([^<]*)</span>").Groups[1].Value;
            string before = $"<body><div class=\"empty\" style=\"font-family:{OldFonts(look.Fonts)};font-size:{Px(look.Size)};padding-top:{Px(2 * look.Down)}\">" +
                            $"<span class=\"glyph\" style=\"font-size:{Px(48)};color:{OldHex(Rgb(look.Glyph))}\">{glyph}</span>" +
                            $"<span style=\"margin-top:{Px(12)};color:{OldHex(Rgb(look.Text))}\">Seleziona un elemento da leggere</span></div></body>";
            if (glyph.Length != 1 || !html.Contains(before)) different.Add(look.Fonts + (look.Light ? ", chiaro" : ", scuro"));
        }
        Check(different.Count == 0,
              $"U-04, la pagina del riquadro vuoto con i caratteri e i colori di sempre ({looks.Length} casi, tema chiaro e scuro): il corpo della pagina è quello di prima, carattere per carattere" +
              (different.Count == 0 ? "" : ": diversa con " + string.Join("; ", different)));

        // Nomi che non sono nomi di caratteri: nel CSS entrano solo lettere, cifre, spazi e trattini, e i due colori non sono testi.
        string hostile = UI.ReadingPage.CssFonts("x';} body{display:none} .a{font-family:'y, Arial\" onmouseover=\"1, ./#Icone, https://www.esempio-prova.example/f#a, </style><i class=fuori>, Segoe UI");
        var types = typeof(UI.EmptyLook).GetProperties().Where(p => p.Name is "Glyph" or "Text").Select(p => p.PropertyType).ToList();
        Check(System.Text.RegularExpressions.Regex.IsMatch(hostile, @"^(?:'[\p{L}\p{N} -]+',)*'Segoe UI',sans-serif$") && hostile.EndsWith("'Segoe UI','Segoe UI',sans-serif")
              && UI.ReadingPage.CssFonts("") == "'Segoe UI',sans-serif" && types.Count == 2 && types.All(t => t == typeof(System.Windows.Media.Color)),
              $"U-04, un nome di carattere con apici, graffe, virgolette o un indirizzo non arriva nel foglio di stile della pagina vuota (resta {hostile}), e i colori del segnaposto sono colori, non testi");

        // Le altre interpolazioni della pagina: nessuna nuova senza E(...) che non sia stata letta.
        if (SourceRoot() is { } root)
        {
            var holes = U4Holes(File.ReadAllText(Path.Combine(root, "UI", "ReadingPage.cs")));
            var unknown = holes.Except(U4Allowed).ToList();
            Check(holes.Count > 60 && unknown.Count == 0,
                  $"U-04, in ReadingPage.cs le interpolazioni che non passano da E(...) sono {holes.Count}, tutte fra le {U4Allowed.Length} lette e ammesse (numeri, colori calcolati, indirizzi delle azioni, testi fissi, pezzi già codificati)" +
                  (unknown.Count == 0 ? "" : ": nuove " + string.Join(" | ", unknown)));
        }
        else Report.Add("      (sorgenti non trovati accanto all'eseguibile: le interpolazioni di ReadingPage.cs non sono state lette)");
    }

    // ---------------------------------------------------------------- U-05: avvii di altri programmi, link aperti da Windows

    /// <summary>
    /// dotnet con il suo percorso intero, per le prove che chiedono a MSBuild le proprietà del progetto: quello del runtime
    /// che sta girando, altrimenti il primo del PATH, mai un nome da cercare nella cartella corrente (U-05).
    /// </summary>
    static string DotnetExe()
    {
        var beside = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        if (File.Exists(beside)) return beside;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (Path.IsPathFullyQualified(dir) && File.Exists(Path.Combine(dir, "dotnet.exe"))) return Path.Combine(dir, "dotnet.exe");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
    }

    /// <summary>
    /// Gli avvii di un altro programma in un sorgente: per ogni "new ProcessStartInfo(" il primo argomento (che cosa si
    /// avvia) e se passa dalla shell di Windows; per ogni "Process.Start(" che non riceve un ProcessStartInfo, il primo
    /// argomento con shell null.
    /// </summary>
    static List<(string First, bool? Shell)> U5Starts(string code)
    {
        var starts = new List<(string, bool?)>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(code, @"\bProcessStartInfo\(|\bProcess\.Start\("))
        {
            int from = m.Index + m.Length, depth = 0, k = from;
            bool quoted = false;
            for (; k < code.Length; k++)
            {
                char c = code[k];
                if (quoted) { if (c == '\\') k++; else if (c == '"') quoted = false; }
                else if (c == '"') quoted = true;
                else if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') { if (depth-- == 0) break; }
                else if (c == ',' && depth == 0) break;
            }
            string first = System.Text.RegularExpressions.Regex.Replace(code[from..Math.Min(k, code.Length)], @"\s+", " ").Trim();
            if (m.Value.StartsWith("Process.Start"))
            {
                // Process.Start(new ProcessStartInfo(...)) o Process.Start(start): conta il ProcessStartInfo, che ha la sua voce.
                if (!first.StartsWith("new ") && first != "start") starts.Add((first, null));
                continue;
            }
            int end = code.IndexOf(';', from);
            var shell = System.Text.RegularExpressions.Regex.Match(code[from..(end < 0 ? code.Length : end)], @"UseShellExecute = (true|false)");
            starts.Add((first, shell.Success ? shell.Groups[1].Value == "true" : null));
        }
        return starts;
    }

    /// <summary>
    /// U-05: un programma avviato con il solo nome ("explorer.exe") Windows lo cerca prima nella cartella di Missiva e nella
    /// cartella corrente, e un link aperto da Windows senza guardare lo schema apre qualunque cosa. Qui: tutti gli avvii dei
    /// sorgenti letti uno per uno, e i link della finestra Aggiungi account. Nessun programma si avvia da queste prove.
    /// </summary>
    static void U5Launches()
    {
        // Com'è Windows (misurato a mano il 14 ottobre 2026, non qui: un nome di programma che Windows non riesce ad avviare apre un
        // suo avviso sullo schermo, e una prova non deve): un programma avviato con il solo nome si cerca nella cartella di chi
        // lo avvia, poi nella cartella corrente, e solo dopo in quelle di sistema; con la variabile
        // NoDefaultCurrentDirectoryInExePath nell'ambiente la cartella corrente si salta. Qui: Esplora file ha il suo percorso intero.
        Check(Path.IsPathFullyQualified(Safety.ExplorerPath) && File.Exists(Safety.ExplorerPath) && Safety.ExplorerPath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase),
              $"U-05, Esplora file si avvia dal suo percorso intero, nella cartella di Windows ({Safety.ExplorerPath}), non con il solo nome");

        if (SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: gli avvii di altri programmi non sono stati letti)");
            return;
        }
        static string Code(string path) =>
            System.Text.RegularExpressions.Regex.Replace(string.Join("\n", File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//"))), @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        var files = Directory.GetFiles(Path.Combine(root, "UI"), "*.cs").Concat(Directory.GetFiles(Path.Combine(root, "Core"), "*.cs")).Append(Path.Combine(root, "Program.cs")).ToList();
        // In nessun sorgente, prove comprese, il nome di un programma scritto fra virgolette come primo argomento.
        var literal = files.Where(f => System.Text.RegularExpressions.Regex.IsMatch(Code(f), @"\b(?:ProcessStartInfo|Process\.Start)\(\s*[$@]*""[\w.\\/:%-]")).Select(Path.GetFileName).ToList();
        // Nel programma (non nelle prove): ogni avvio letto, con che cosa avvia e se passa dalla shell.
        // Dal 15 ottobre 2026 anche gli aggiornamenti (Updates): il nuovo eseguibile scaricato e controllato, provato e poi avviato
        // per la sostituzione (prepared.Exe), e da lui quello installato, il percorso dell'eseguibile che girava (installed).
        string[] opened = ["uri.AbsoluteUri", "url", "WebRuntimeLink", "Store.Dir", "temp.Release()"], programs = ["Environment.ProcessPath!", "ExplorerPath", "prepared.Exe", "installed"];
        var starts = files.Where(f => !Path.GetFileName(f).StartsWith("ProvaBozze")).SelectMany(f => U5Starts(Code(f)).Select(s => (File: Path.GetFileName(f), s.First, s.Shell))).ToList();
        var wrong = starts.Where(s => s.Shell == null || (s.Shell == true ? !opened.Contains(s.First) : !programs.Contains(s.First))).Select(s => $"{s.File}: {s.First}").ToList();
        // Il percorso passato a Esplora file sta fra virgolette: uno che ne contiene una non si passa.
        string safety = Code(Path.Combine(root, "Core", "Safety.cs"));
        int quote = safety.IndexOf("if (path.Contains('\"')) return;", StringComparison.Ordinal), explorer = safety.IndexOf("ProcessStartInfo(ExplorerPath, $\"/select,\\\"{path}\\\"\")", StringComparison.Ordinal);
        Check(literal.Count == 0 && wrong.Count == 0 && starts.Count >= 12 && starts.Count(s => s.Shell == false) == 5 && quote > 0 && explorer > quote,
              $"U-05, gli avvii di altri programmi nei sorgenti: nessuno con il nome di un programma senza percorso, prove comprese; nel programma sono {starts.Count}, tutti letti: {starts.Count(s => s.Shell == true)} passano a Windows " +
              $"un indirizzo o un file da aprire ({string.Join(", ", starts.Where(s => s.Shell == true).Select(s => s.First).Distinct())}), {starts.Count(s => s.Shell == false)} avviano un programma dal suo percorso intero " +
              $"({string.Join(", ", starts.Where(s => s.Shell == false).Select(s => s.First))})" +
              (literal.Count + wrong.Count == 0 ? "" : ": da guardare " + string.Join(" | ", literal.Concat(wrong))));

        // I link della finestra Aggiungi account: tutti https nel XAML, e il gestore non apre altro. È l'unico gestore di RequestNavigate.
        string xaml = File.ReadAllText(Path.Combine(root, "UI", "AccountWindow.xaml")), window = Code(Path.Combine(root, "UI", "AccountWindow.xaml.cs"));
        var links = System.Text.RegularExpressions.Regex.Matches(xaml, "NavigateUri=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();
        var handlers = Directory.GetFiles(Path.Combine(root, "UI")).Where(f => f.EndsWith(".xaml") || f.EndsWith(".cs")).Where(f => File.ReadAllText(f).Contains("RequestNavigate")).Select(Path.GetFileName).OrderBy(n => n).ToList();
        int check = window.IndexOf("if (e.Uri is not { IsAbsoluteUri: true } uri || uri.Scheme != Uri.UriSchemeHttps) return;", StringComparison.Ordinal);
        int start = window.IndexOf("Process.Start(new ProcessStartInfo(uri.AbsoluteUri)", StringComparison.Ordinal);
        Check(links.Count >= 2 && links.All(l => l.StartsWith("https://")) && handlers.SequenceEqual(["AccountWindow.xaml", "AccountWindow.xaml.cs"]) && check > 0 && start > check && !window.Contains("e.Uri.AbsoluteUri"),
              $"U-05, i link della finestra Aggiungi account ({string.Join(", ", links)}) sono tutti https, e il loro gestore, l'unico di questo tipo, non passa a Windows un indirizzo che non lo è");
    }

    // ---------------------------------------------------------------- scelte del 14 ottobre: Annulla nelle conferme sugli inoltri (U-02)

    /// <summary>
    /// U-02, scelte del 14 ottobre 2026: le due conferme di una regola che manda la posta fuori (Rules.ForwardQuestion,
    /// nella finestra della regola e nella casella dell'elenco) hanno Annulla come pulsante predefinito
    /// (Dialog.AskCancelFirst, come Elimina di un contatto): prima Invio salvava la regola e Invio di nuovo confermava
    /// l'inoltro. Le finestre non si aprono nelle prove: si leggono i sorgenti senza i commenti.
    /// </summary>
    static void U2CancelFirst()
    {
        if (Ch1Code("UI", "Dialog.cs") is not { } dialog || Ch1Code("UI", "RuleWindow.cs") is not { } window || Ch1Code("UI", "RulesView.cs") is not { } view)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: le conferme sugli inoltri non sono state lette)");
            return;
        }
        // AskCancelFirst: Annulla è il pulsante di Invio e di Esc, e ha il fuoco; quello che conferma non è predefinito.
        string build = Ch3Part(dialog, "static Choice Build(", "window.ShowDialog();");
        bool cancelFirst = dialog.Contains("public static Choice AskCancelFirst(Window? owner, string heading, string body, string primary) => Build(owner, heading, body, primary, true, cancelFirst: true);")
                           && build.Contains("IsCancel = true, IsDefault = cancelFirst") && build.Contains("if (cancelFirst) window.Loaded += (_, _) => no.Focus();") && build.Contains("IsDefault = !cancelFirst");
        // Le due conferme sugli inoltri passano di lì, e sono le sole: le altre domande della finestra e dell'elenco restano com'erano.
        string save = Ch3Part(window, "async Task SaveAsync()", "form.IsEnabled = save.IsEnabled = false;"), toggle = Ch3Part(view, "async Task SetEnabledAsync(", "Show();");
        static int Count(string text, string part) => text.Split(part).Length - 1;
        bool inWindow = save.Contains("Dialog.AskCancelFirst(this, forward.Heading, forward.Body, forward.Confirm) != Choice.Primary") && !save.Contains("Dialog.Ask(this, forward.");
        bool inList = toggle.Contains("question => Dialog.AskCancelFirst(Owner, question.Heading, question.Body, question.Confirm) == Choice.Primary");
        Check(cancelFirst && inWindow && inList && Count(window, "Dialog.AskCancelFirst(") == 1 && Count(view, "Dialog.AskCancelFirst(") == 1,
              $"U-02 (scelte del 14 ottobre), le conferme di una regola che manda la posta fuori hanno Annulla come pulsante predefinito (Dialog.AskCancelFirst: Invio ed Esc annullano, il fuoco è su Annulla): " +
              $"nella finestra della regola ({inWindow}) e nella casella dell'elenco che la accende ({inList}); le altre domande delle regole restano com'erano");
    }

    /// <summary>
    /// U-02, scelte del 14 ottobre 2026: una regola senza condizioni che manda anche la posta fuori. Prima la finestra
    /// chiedeva "La regola vale per tutti i messaggi" senza nominare l'inoltro (una regola accesa che inoltrava già, a cui si
    /// toglievano le condizioni), e se l'inoltro era nuovo chiedeva subito dopo anche quella sugli inoltri. Ora una domanda
    /// sola: quella sugli inoltri dice anche che la regola agirà su ogni messaggio (Rules.ForwardQuestion); quando per gli
    /// inoltri non c'è nulla da chiedere, quella sulle condizioni li nomina (Rules.AllMessagesQuestion).
    /// </summary>
    static void U2AllMessages()
    {
        var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.it", Tenant = "organizations" };
        var personal = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@outlook.it" };
        const string outside = "archivio@esterno-prova.example", colleague = "anna@uffici.studio-prova.it";
        const string plainText = "Non hai scelto condizioni: la regola agirà su ogni messaggio che arriva nella Posta in arrivo. Salvarla lo stesso?";
        MailRule Rule(bool conditions, bool enabled, params string[] to) => new()
        {
            AccountId = work.Id, ServerId = "r7", Name = "Fatture", SenderContains = conditions ? ["fatture"] : [], MarkRead = true, Enabled = enabled,
            ForwardTo = to.Select(a => new Address { Email = a }).ToList(),
        };

        // Una regola accesa che inoltra fuori, a cui si tolgono tutte le condizioni: per gli inoltri nulla da chiedere (sono gli stessi), e la domanda sulle condizioni li nomina.
        var active = Rule(true, true, outside);
        var bare = Rule(false, true, outside);
        var reminded = Rules.AllMessagesQuestion(bare, work);
        // Senza inoltri verso fuori la domanda è quella di prima, parola per parola; con le condizioni (anche nascoste) non c'è.
        var hidden = Rule(false, true);
        hidden.HiddenConditions = ["bodyOrSubjectContains"];
        bool plain = Rules.AllMessagesQuestion(Rule(false, true), work) is { } none && none == ("La regola vale per tutti i messaggi", plainText, "Salva")
                     && Rules.AllMessagesQuestion(Rule(false, true, colleague), work)?.Body == plainText && Rules.AllMessagesQuestion(active, work) == null && Rules.AllMessagesQuestion(hidden, work) == null;
        // Un inoltro nuovo senza condizioni: una domanda sola, che dice tutte e due le cose (accesa, spenta, account personale).
        var merged = Rules.ForwardQuestion(null, Rule(false, true, outside), work);
        var asleep = Rules.ForwardQuestion(null, Rule(false, false, outside), work);
        var mine = Rules.ForwardQuestion(null, Rule(false, true, "giulia@outlook.it"), personal);
        var narrow = Rules.ForwardQuestion(null, Rule(true, true, outside), work);
        Check(Rules.ForwardQuestion(active, bare, work) == null
              && reminded == ("La regola vale per tutti i messaggi", $"Non hai scelto condizioni: la regola agirà su ogni messaggio che arriva nella Posta in arrivo, e lo manderà a {outside}, fuori dalla tua organizzazione. Salvarla lo stesso?", "Salva")
              && plain
              && merged is { Heading: "La regola vale per tutti i messaggi e li manda fuori", Confirm: "Inoltra a " + outside } m
              && m.Body.StartsWith($"Non hai scelto condizioni: la regola agirà su ogni messaggio che arriva nella Posta in arrivo, e tutti verranno inoltrati a {outside}, fuori dalla tua organizzazione, anche quando non guardi. Chi entra di nascosto")
              && asleep is { Confirm: "Salva" } a && a.Body.StartsWith($"La regola è spenta: quando la accendi, agirà su ogni messaggio che arriva nella Posta in arrivo (non hai scelto condizioni), e tutti verranno inoltrati a {outside}")
              && mine is { } p && p.Body.Contains("fuori dalla tua casella") && narrow is { Heading: "La regola manda la tua posta fuori" } n && n.Body.StartsWith($"I messaggi che corrispondono verranno inoltrati a {outside}"),
              "U-02 (scelte del 14 ottobre), una regola senza condizioni che manda la posta fuori: una domanda sola, che dice tutte e due le cose. Accesa e con gli inoltri di prima, la domanda \"vale per tutti i messaggi\" " +
              "nomina l'indirizzo e dice che è fuori (prima no); con un inoltro nuovo quella sugli inoltri dice anche che la regola agirà su ogni messaggio, accesa o spenta; senza inoltri verso fuori, e con le condizioni, " +
              "le domande sono quelle di prima");

        // La finestra: prima la domanda sugli inoltri, e quella sulle condizioni solo se non c'è stata; nessuna domanda scritta lì.
        if (Ch1Code("UI", "RuleWindow.cs") is { } window)
        {
            string save = Ch3Part(window, "async Task SaveAsync()", "form.IsEnabled = save.IsEnabled = false;");
            Check(Ch3InOrder(save, "var question = Rules.ForwardQuestion(editing, rule, provider.Account);", "if (question is { } forward && Dialog.AskCancelFirst(",
                             "if (question == null && Rules.AllMessagesQuestion(rule, provider.Account) is { } all && Dialog.Ask(this, all.Heading, all.Body, all.Confirm) != Choice.Primary) return;")
                  && !window.Contains("vale per tutti i messaggi") && save.Split("Dialog.Ask").Length - 1 == 2,
                  "U-02 (scelte del 14 ottobre), nella finestra della regola le due domande non vengono mai di fila: prima quella sugli inoltri, poi quella sulle condizioni solo se la prima non c'è stata");
        }
        else Report.Add("      (sorgenti non trovati accanto all'eseguibile: l'ordine delle domande della finestra della regola non è stato letto)");
    }

    // ---------------------------------------------------------------- scelte del 14 ottobre: il template nelle citazioni (U-01)

    /// <summary>
    /// U-01, scelte del 14 ottobre 2026: nell'HTML che riparte in una risposta o in un inoltro (MailHtml.Outgoing, K-04) un
    /// template si toglie con quello che contiene, come uno script. Prima restava, e il suo contenuto ripartiva non pulito:
    /// la visita di Outgoing non entra nel contenuto di un template, e se fuori non c'era nulla da togliere tornava la stessa
    /// stringa, con lo script e gli attributi di evento di là dentro. Per chi riceve era inerte, ma non pulito.
    /// </summary>
    static void U1OutgoingTemplate()
    {
        const string inside = "<template><script>alert(1)</script><p onclick=\"alert(2)\">dentro</p><template><img src=\"https://immagini.negozio-prova.example/a.png\" onerror=\"alert(3)\"></template></template>";
        const string clean = "<html><head></head><body><p>Prima</p><p>Dopo</p></body></html>";
        // Da solo (prima non c'era nulla da togliere, e tornava la stessa stringa) e con un attributo di evento fuori (prima si riscriveva dall'albero, con il template com'era).
        string alone = MailHtml.Outgoing("<p>Prima</p>" + inside + "<p>Dopo</p>"), mixed = MailHtml.Outgoing("<p onclick=\"x()\">Prima</p>" + inside + "<p>Dopo</p>");
        // Anche dove l'analisi lo mette in testa, o in una tabella.
        string head = MailHtml.Outgoing("<html><head>" + inside + "</head><body><p>Prima</p><p>Dopo</p></body></html>"), table = MailHtml.Outgoing("<table>" + inside + "<tr><td>Cella</td></tr></table>");
        string[] marks = ["<template", "<script", "alert(", "onclick", "onerror", "dentro", "a.png"];
        var left = new[] { alone, mixed, head, table }.SelectMany(h => marks.Where(m => h.Contains(m, StringComparison.OrdinalIgnoreCase))).Distinct().ToList();
        Check(left.Count == 0 && alone == clean && mixed == clean && head == clean && table.Contains("<td>Cella</td>", StringComparison.Ordinal),
              "U-01 (scelte del 14 ottobre), nell'HTML che riparte in una risposta o in un inoltro un template si toglie con tutto quello che contiene (uno script, un attributo di evento, un template dentro un altro), " +
              "nel corpo, in testa e in una tabella: prima, se fuori non c'era altro da togliere, ripartiva com'era, con lo script e l'onclick" + (left.Count == 0 ? "" : ": restano " + string.Join(", ", left)));
    }
}
#endif
