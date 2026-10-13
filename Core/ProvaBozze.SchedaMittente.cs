#if DEBUG
using System.Text.RegularExpressions;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Le prove delle intestazioni del riquadro di lettura (1 ottobre 2026, agente scheda-mittente-I): in ogni messaggio, da solo o della
/// conversazione, aperto o chiuso, la foto e il nome del mittente sono pulsanti veri verso la stessa azione "person/i" (con il gettone
/// della pagina e il nome accessibile "Scheda di ..."), mai dentro l'elemento che apre e chiude il messaggio (un pulsante vuoto, che copre
/// l'intestazione dietro tutto il resto), e senza indirizzo non sono pulsanti. Sull'HTML generato; il comportamento dei clic e dei tasti
/// si prova a parte (loop\strumenti\cdp-scheda-I.ps1 con Edge headless e UI Automation sulla copia di prova).
/// </summary>
static partial class ProvaBozze
{
    static void SenderCardHeaderTests()
    {
        Report.Add("--- Intestazioni: foto e nome del mittente aprono la scheda, il resto apre e chiude");
        var to = new List<Address> { new() { Name = "Ada", Email = "ada@example.com" }, new() { Name = "Maria Rossi", Email = "maria.rossi@example.com" } };
        MessageSummary Make(string id, string name, string email, string preview, params string[] cats) => new()
        {
            Id = id, FolderId = "prova", Subject = "Prova", Date = new DateTime(2026, 10, 1, 9, 30, 0), Preview = preview,
            From = new Address { Name = name, Email = email }, Categories = cats.ToList(),
        };
        var logo = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        // I pezzi di ogni messaggio: dall'<article> al successivo
        static string[] Articles(string html) => Regex.Split(html, "(?=<article id=\"m\\d+\")").Where(p => p.StartsWith("<article")).Select(p => p.Split("</article>")[0]).ToArray();
        // Un pulsante dentro un altro (con o senza form di mezzo) non è HTML valido e il secondo non riceverebbe il clic
        static bool Nested(string html) => Regex.IsMatch(html, "<button[^>]*>(?:(?!</button>).)*<button", RegexOptions.Singleline);
        static int Count(string html, string pattern) => Regex.Matches(html, pattern, RegexOptions.Singleline).Count;

        // --- Messaggio da solo, aperto: nessun pulsante di apertura, foto e nome verso person/0, nessuna icona accanto a Inoltra
        var one = ReadingPage.Build([new PageEntry(Make("1", "Mario Rossi", "mario@example.com", ""), new MessageBody { Html = "<p>Ciao</p>", To = to }, true, false, null, false)], true);
        var lone = Articles(one.Html)[0];
        string person0 = $"<form action=\"https://missiva.example/do/{one.Token}/person/0\">";
        Check(!lone.Contains("class=\"tog\"") && lone.Contains("<div class=\"head\">") && !lone.Contains("class=\"head tg\"") && !lone.Contains("toggle/"),
              "messaggio da solo e aperto: nessun pulsante che apre e chiude (.tog), intestazione senza la classe delle conversazioni (tg)");
        Check(Count(lone, Regex.Escape(person0) + "<button class=\"avatar pic\" style=\"background:#[0-9A-F]{6}\" title=\"Scheda di Mario Rossi\" aria-label=\"Scheda di Mario Rossi\">MR</button></form>") == 1
              && Count(lone, Regex.Escape(person0) + "<button class=\"person\" title=\"Scheda di Mario Rossi\" aria-label=\"Scheda di Mario Rossi\"><b>Mario Rossi</b></button></form>") == 1
              && Count(lone, "person/0") == 2,
              "messaggio da solo: la foto (iniziali MR) e il nome sono pulsanti nel modulo person/0 con il gettone della pagina e il nome accessibile \"Scheda di Mario Rossi\"; solo loro due (niente icona accanto a Inoltra)");
        Check(!Nested(lone) && !lone.Contains("<span class=\"from\">"), "messaggio da solo: nessun pulsante dentro un altro");

        // --- Conversazione: chiuso con nome lungo e logo, aperto con destinatari e categoria, proprio chiuso, chiuso senza indirizzo, nome con virgolette
        const string quoted = "Mario \"Il Rosso\" <x> & Co";
        var thread = ReadingPage.Build(
        [
            new PageEntry(Make("1", "Giovanni Battista Alessandro Montefiore-Castelnuovo di Sant'Agata", "gb@example.com", "Ti scrivo per la riunione di giovedì"), null, false, false, null, false, null, logo),
            new PageEntry(Make("2", "Mario Rossi", "mario@example.com", "", "Clienti"), new MessageBody { Html = "<p>Ciao</p>", To = to, Cc = to.Take(1).ToList() }, true, false, null, false, new SenderNotes(null, "", true, null)),
            new PageEntry(Make("3", "Ada", "ada.prova@gmail.com", "Grazie, confermo"), null, false, false, null, false),
            new PageEntry(Make("4", "Nessun indirizzo", "", "Un messaggio senza indirizzo"), null, false, false, null, false),
            new PageEntry(Make("5", quoted, "rosso@example.com", "Anteprima"), null, false, false, null, false),
        ], true);
        var parts = Articles(thread.Html);
        Check(parts.Length == 5 && parts.All(p => p.Contains("<div class=\"head tg\">")), $"conversazione: {parts.Length} messaggi, tutte le intestazioni con la classe tg (il resto apre e chiude)");
        bool toggles = true, forms = true, persons = true, buttonsOnly = true, previewOutside = true, expandedState = true;
        for (int k = 0; k < parts.Length; k++)
        {
            var p = parts[k];
            bool expanded = k == 1, hasMail = k != 3;
            // un solo pulsante di apertura per messaggio, vuoto (niente dentro che possa contenere foto e nome), con lo stato e il nome accessibile
            toggles &= Count(p, "<button id=\"t" + k + "\" class=\"tog\"") == 1 && Count(p, "class=\"tog\"") == 1;
            forms &= Regex.IsMatch(p, "<form action=\"https://missiva.example/do/" + thread.Token + "/toggle/" + k + "\"><button id=\"t" + k + "\" class=\"tog\" title=\"" + (expanded ? "Comprimi" : "Espandi")
                                      + "\" aria-label=\"" + (expanded ? "Comprimi" : "Espandi") + " il messaggio di [^\"]+\"></button></form>") && !p.Contains("aria-expanded");
            expandedState &= p.Contains("class=\"msg" + (expanded ? " open" : "") + "\"");
            // foto e nome: due moduli person/k con i loro pulsanti, solo se c'è l'indirizzo
            persons &= Count(p, "/person/" + k + "\"><button class=\"avatar[ a-z]* pic\"") == (hasMail ? 1 : 0)
                       && Count(p, "/person/" + k + "\"><button class=\"person\"") == (hasMail ? 1 : 0)
                       && Count(p, "person/") == (hasMail ? 2 : 0)
                       && Count(p, "aria-label=\"Scheda di ") == (hasMail ? 2 : 0);
            buttonsOnly &= !Nested(p);
            // l'anteprima dei chiusi sta fuori da ogni pulsante (prima il pulsante intero la conteneva: il clic non la distingueva)
            if (!expanded) previewOutside &= Regex.Replace(p, "<button.*?</button>", "", RegexOptions.Singleline).Contains("<span class=\"line preview\">");
        }
        Check(toggles && forms && expandedState, "conversazione: ogni messaggio ha un solo pulsante di apertura (toggle/i con il gettone), vuoto, con \"Espandi\" o \"Comprimi\" e il suo nome accessibile (il verbo cambia con lo stato: niente aria-expanded, che toglierebbe Invoke a UI Automation)");
        Check(persons, "conversazione: foto e nome sono pulsanti person/i (due, con \"Scheda di ...\") in ogni messaggio con l'indirizzo, chiuso o aperto; in quello senza indirizzo nessuno");
        Check(buttonsOnly && previewOutside, "conversazione: nessun pulsante dentro un altro, né foto e nome nel pulsante di apertura; l'anteprima dei messaggi chiusi sta fuori da ogni pulsante");
        Check(parts[0].Contains("<button class=\"avatar logo pic\" title=\"Scheda di Giovanni") && parts[0].Contains("<img src=\"/logo/0.png\" alt=\"\"></button>") && thread.Resources.ContainsKey("/logo/0.png")
              && !parts[2].Contains("/logo/") && parts[2].Contains("class=\"avatar pic\""),
              "conversazione: con il logo la foto è lo stesso pulsante (avatar logo pic, immagine /logo/0.png servita da Missiva), senza logo le iniziali");
        Check(parts[3].Contains("<span class=\"avatar\" style=") && parts[3].Contains("<span class=\"from\"><b>Nessun indirizzo</b>") && !parts[3].Contains("person/"),
              "senza indirizzo (come prima del cambio, per il nome): foto e nome non sono pulsanti, il clic su di loro è quello della riga");
        // testi con un suggerimento (destinatari, categoria, Esterno) nella conversazione: label verso il pulsante di apertura, che il clic gli inoltra
        Check(parts[1].Contains("<label for=\"t1\" class=\"line recipients\" title=\"") && parts[1].Contains("<label for=\"t1\" class=\"cat\"") && parts[1].Contains("<label for=\"t1\" class=\"ext\" title=")
              && lone.Contains("<span class=\"line recipients\" title=\"") && !lone.Contains("<label"),
              "suggerimenti dei destinatari, della categoria e di Esterno: nella conversazione label verso il pulsante di apertura (il clic lo apre, il suggerimento resta), nel messaggio da solo span come prima");
        // nome con virgolette, segni minore e maggiore ed &: mai nell'HTML com'è
        Check(!thread.Html.Contains("Mario \"Il Rosso\"") && parts[4].Contains("aria-label=\"Scheda di Mario &quot;Il Rosso&quot; &lt;x&gt; &amp; Co\"") && !Regex.IsMatch(parts[4], "<x>"),
              "nome con virgolette, < > ed &: nel nome accessibile e nel suggerimento sempre codificato");
        // lo stile: pulsanti sopra, apertura dietro, testo che non prende i clic
        var css = Regex.Match(thread.Html, "<style>.*?</style>", RegexOptions.Singleline).Value;
        Check(css.Contains(".tog { position: absolute;") && css.Contains("z-index: -1;") && css.Contains(".head.tg .who, .head.tg .side { pointer-events: none; }")
              && css.Contains(".head.tg .pic, .head.tg button.person, .head.tg label, .head.tg .act, .head.tg .edit { pointer-events: auto; }")
              && css.Contains("button.pic:focus-visible") && css.Contains("button.person:focus-visible") && css.Contains(".tog:focus-visible") && css.Contains("button.pic:hover") && css.Contains("button.person:hover b"),
              "stile: apertura dietro (z-index -1), testo senza clic e pulsanti sopra, mano, passaggio del mouse e fuoco da tastiera visibili per foto, nome e apertura");
        Check(thread.Html.Contains("script-src 'none'") && !Regex.IsMatch(thread.Html, "<script", RegexOptions.IgnoreCase) && thread.Html.Contains("/do/" + thread.Token + "/"),
              "sicurezza invariata: nessuno script nella pagina, CSP script-src 'none', azioni solo verso /do/gettone/");
        Check(thread.Html.Split("/do/").Skip(1).All(s => s.StartsWith(thread.Token + "/")), "tutte le azioni della pagina hanno il gettone della pagina");
    }
}
#endif
