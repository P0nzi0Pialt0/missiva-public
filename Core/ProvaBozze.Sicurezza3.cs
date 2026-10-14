#if DEBUG
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Documents;
using System.Windows.Media;

namespace Missiva;

/// <summary>
/// Audit dell'11 ottobre, sicurezza, terzo gruppo (docs\audit\ESITO.md): annulla iscrizione, Graph, canale, ganci di
/// prova, editor (T-11..T-13, T-23..T-36). Solo dati finti, server finti su 127.0.0.1, cartelle temporanee loro.
/// Con "Missiva.exe --prova-audit risultato.txt sicurezza3" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void Security3AuditTests()
    {
        Report.Add("--- Audit dell'11 ottobre, sicurezza, terzo gruppo: annulla iscrizione, Graph, canale, ganci di prova, editor (T-11..T-13, T-23..T-36)");
        Sec3Unsubscribe();
        Sec3GraphAsync().GetAwaiter().GetResult();
        Task.Run(Sec3ChannelAsync).GetAwaiter().GetResult();
        Sec3MailtoCommand();
        Sec3TestBuild();
        Sec3Fonts();
        Sec3Paste();
        Sec3Small();
    }

    /// <summary>
    /// T-33, T-34, T-35 e T-36: il link della riunione di un invito su un sito che finisce soltanto come un servizio noto;
    /// le regole che inoltrano fuori (solo la misura del confronto: l'avviso è una scelta dell'autore); il sito di un contatto
    /// verso il PC o la rete di casa; il registro con gli a capo di un testo scritto da altri.
    /// </summary>
    static void Sec3Small()
    {
        // T-33: il nome del servizio, o un nome che ci sta sotto con il punto.
        string[] others =
        [
            "https://malzoom.us/j/123", "https://xteams.microsoft.com/l/meetup-join/1", "https://notwebex.com/meet/mario", "https://riunioni-whereby.com/stanza", "https://finto-meet.google.com/abc",
            "https://zoom.us.altrove-prova.example/j/123", "https://altrove-prova.example/zoom.us", "http://zoom.us/j/123",
        ];
        string[] known = ["https://zoom.us/j/123", "https://us02web.zoom.us/j/123?pwd=abc", "https://teams.microsoft.com/l/meetup-join/19%3ameeting", "https://meet.google.com/abc-defg-hij", "https://studio-prova.webex.com/meet/mario", "https://ZOOM.US/j/123"];
        var taken = others.Where(u => Invitation.FindMeetingLink($"Partecipa alla riunione: {u} a domani") != null || Invitation.IsMeetingSite(u) && u.StartsWith("https://")).ToList();
        var lost = known.Where(u => Invitation.FindMeetingLink($"Partecipa alla riunione: {u} a domani") == null).ToList();
        string Ics(string url) => "BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:prova-t33@example.com\r\nDTSTAMP:20261012T080000Z\r\nDTSTART:20261112T133000Z\r\nDTEND:20261112T143000Z\r\n" +
                                 $"SUMMARY:Riunione di prova\r\nORGANIZER;CN=Maria Rossi:mailto:maria.rossi@example.com\r\nATTENDEE;CN=Mario Rossi:mailto:mario.rossi@example.com\r\nURL:{url}\r\n" +
                                 $"DESCRIPTION:Partecipa da qui: {url}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var fromOther = Invitation.Parse(Ics("https://malzoom.us/j/123"));
        var fromKnown = Invitation.Parse(Ics("https://us02web.zoom.us/j/123"));
        Check(taken.Count == 0 && lost.Count == 0 && fromOther is { OnlineUrl: null } && fromKnown is { OnlineUrl: "https://us02web.zoom.us/j/123" },
              $"T-33, il link della riunione di un invito: {others.Length} indirizzi di altri siti (il nome di un servizio in coda al nome, o nel percorso) non sono la riunione online, {known.Length} dei servizi veri sì" +
              (taken.Count + lost.Count == 0 ? "" : $" (presi: {string.Join(" | ", taken)}; persi: {string.Join(" | ", lost)})") + "; lo stesso in un .ics, in URL e nella descrizione");

        // T-34, solo misura: quali inoltri di una regola di Outlook sono fuori dall'organizzazione.
        var work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = "organizations", Identities = [new SendIdentity { Email = "info@studio-prova.example" }] };
        var rule = new MailRule
        {
            Name = "Copia",
            ForwardTo = [new Address { Email = "collega@studio-prova.example" }, new Address { Email = "copia@studio-prova.example.altrove-prova.example" }],
            RedirectTo = [new Address { Email = "copia@altrostudio-prova.example" }, new Address { Email = "info@studio-prova.example" }],
            ForwardAsAttachmentTo = [new Address { Email = "copia@posta.studio-prova.example" }, new Address { Email = "copia@altrove-prova.example" }],
        };
        var outside = Rules.ExternalForwards(rule, work);
        Report.Add($"      (T-34, misura: di sei inoltri di una regola di Outlook (inoltra, reindirizza, inoltra come allegato) sono fuori dall'organizzazione {outside.Count}: {string.Join(", ", outside)}; " +
                   "il collega, un altro indirizzo dell'account e un sottodominio no. L'avviso quando una regola così compare non c'è: lasciato all'autore)");

        // T-35: il sito di un contatto.
        string[] local =
        [
            "http://10.0.0.1/admin", "https://127.0.0.1/", "http://192.168.1.1", "192.168.178.1", "http://169.254.169.254/latest", "http://172.16.0.5:8080/", "http://[::1]/", "http://[fe80::1]/", "http://[fd00::1]/x",
            "http://0x7f.0.0.1/", "http://2130706433/", "http://100.64.0.1/", "http://[::ffff:192.168.1.1]/",
        ];
        (string Written, string Wanted)[] sites =
        [
            ("www.esempio.it", "https://www.esempio.it/"), ("https://studio-prova.example/chi-siamo?x=1", "https://studio-prova.example/chi-siamo?x=1"), ("http://93.184.216.34/", "http://93.184.216.34/"),
            ("10.example.com", "https://10.example.com/"), ("http://[::ffff:93.184.216.34]/", "http://[::ffff:93.184.216.34]/"),
        ];
        var opened = local.Where(l => ContactLinks.WebUrl(l) != null || ContactLinks.IsAllowed(l)).ToList();
        var refused = sites.Where(s => ContactLinks.WebUrl(s.Written) != s.Wanted).Select(s => $"{s.Written} dà {ContactLinks.WebUrl(s.Written) ?? "nulla"}").ToList();
        Check(opened.Count == 0 && refused.Count == 0,
              $"T-35, il sito di un contatto: {local.Length} indirizzi numerici del PC o della rete di casa (anche scritti in esadecimale, come un numero solo, IPv6 locali) non diventano un collegamento, " +
              $"{sites.Length} siti veri (anche con un indirizzo numerico di Internet) sì" + (opened.Count + refused.Count == 0 ? "" : $" (aperti: {string.Join(" | ", opened)}; rifiutati: {string.Join("; ", refused)})"));

        // T-36: una voce del registro non può fingerne un'altra.
        const string forged = "Archivio\r\n2026-10-11 09:00:00  accesso riuscito";
        var entry = Log.OneEntry($"ricerca in \"{forged}\" non riuscita: NO\nserver \u2028giù\u0000\u001b[31m\u0085fine");
        var lines = entry.Split(Environment.NewLine);
        var trace = Log.OneEntry("ERRORE (prova): System.IO.IOException: prima riga\r\n   at Missiva.Prova.Uno()\r\n   at Missiva.Prova.Due()");
        var path = Path.Combine(Store.Dir, "missiva.log");
        var tag = "prova-t36-" + Guid.NewGuid().ToString("N")[..8];
        Log.Write($"{tag} cartella \"{forged}\" letta");
        var written = File.ReadLines(path).SkipWhile(l => !l.Contains(tag)).Take(2).ToList();
        bool hides = Log.Private("Clienti\r\nriservati") is "(nascosto)" or "\"Clienti\r\nriservati\"";
        Check(lines.Length == 5 && lines.Skip(1).All(l => l.StartsWith("    ")) && !entry.Any(c => c < ' ' && c is not ('\r' or '\n')) && !entry.Contains('\u2028') && !entry.Contains('\u0085')
              && Log.OneEntry("riga normale con\ttabulazione e accenti àèìòù") == "riga normale con\ttabulazione e accenti àèìòù"
              && trace.Split(Environment.NewLine) is [_, var one, var two] && one == "       at Missiva.Prova.Uno()" && two == "       at Missiva.Prova.Due()"
              && written.Count == 2 && System.Text.RegularExpressions.Regex.IsMatch(written[0], @"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d  ") && written[1] == "    2026-10-11 09:00:00  accesso riuscito\" letta" && hides,
              "T-36, il registro: un testo scritto da altri con dentro un a capo e una riga che sembra del registro (il nome di una cartella, l'errore di un server) resta una voce sola, " +
              $"con le righe che continuano rientrate di quattro spazi ({lines.Length} righe da una voce con cinque a capo di tipi diversi) e senza altri caratteri di controllo; la traccia di un errore resta leggibile");
        if (SourceRoot() is { } root)
        {
            var imap = File.ReadAllText(Path.Combine(root, "Core", "ImapProvider.cs"));
            int clear = System.Text.RegularExpressions.Regex.Matches(imap, @"Log\.(Write|Error)\([^\n]*\{[a-z]+\.FullName\}").Count;
            Check(clear == 0, $"T-36, nel registro della versione installata i nomi delle cartelle IMAP passano da Log.Private ({clear} righe li scrivono ancora in chiaro)");
        }
    }

    /// <summary>
    /// T-11, T-12 e T-13: Annulla iscrizione. L'indirizzo iscritto è quello a cui il messaggio era scritto (un alias); il
    /// clic singolo chiede una firma DKIM del dominio del mittente, non il solo SPF; la conferma mostra il sito in una forma
    /// sola. Nessuna richiesta: solo che cosa il comando farebbe e che cosa direbbe.
    /// </summary>
    static void Sec3Unsubscribe()
    {
        // T-11: una lista arrivata a un alias dell'account.
        var alias = new SendIdentity { Name = "Mario Rossi", Email = "liste@studio-prova.example" };
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "mario.prova@gmail.example", Identities = [alias] };
        MessageSummary To(params string[] to) => new()
        {
            Id = "n1", FolderId = "INBOX", Subject = "Le novità della settimana", Date = DateTime.Now, From = new Address { Name = "Notizie di prova", Email = "notizie@" + NewsDomain },
            To = to.Select(e => new Address { Email = e }).ToList(),
        };
        var list = $"<{NewsHttps}>, <{NewsMailto}>";
        var toAlias = Unsubscribe.Recipient(account, To("liste@studio-prova.example"), NewsBody(list));
        var toMain = Unsubscribe.Recipient(account, To("mario.prova@gmail.example"), NewsBody(list));
        var toBoth = Unsubscribe.Recipient(account, To("liste@studio-prova.example", "mario.prova@gmail.example"), NewsBody(list));
        var toNobody = Unsubscribe.Recipient(account, To("lista-nascosta@notizie-prova.example"), null);
        var oneClick = Unsubscribe.For(account, To("liste@studio-prova.example"), NewsBody(list, OneClick, NewsAuth()), false)!;
        var asked = Unsubscribe.Confirmation(oneClick, "Notizie di prova", toAlias.Email).Body;
        Check(toAlias == (alias, "liste@studio-prova.example") && toMain == (null, account.Email) && toBoth == (null, account.Email) && toNobody == (null, account.Email)
              && asked.Contains("liste@studio-prova.example") && !asked.Contains(account.Email),
              "T-11, Annulla iscrizione di una lista arrivata a un altro indirizzo dell'account: l'indirizzo iscritto è quello (la conferma lo nomina e la scrittura parte da lì), " +
              "non il principale; con l'indirizzo principale fra i destinatari, o con nessuno dei due, il principale");
        if (SourceRoot() is { } root)
        {
            string reading = File.ReadAllText(Path.Combine(root, "UI", "ReadingView.xaml.cs")), main = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.xaml.cs")),
                   compose = File.ReadAllText(Path.Combine(root, "UI", "ComposeWindow.xaml.cs"));
            Check(reading.Contains("var (identity, subscribed) = Unsubscribe.Recipient(from.Account, entry.Summary, entry.Body);") && reading.Contains("MailtoRequested?.Invoke(from, new Uri(action.Target), identity);")
                  && !reading.Contains("Unsubscribe.Confirmation(action, sender, from.Account.Email)") && !reading.Contains("OneClickAsync(action, sender, from.Account.Email")
                  && main.Contains("ComposeMode.New, null, null, sendAs: identity)") && compose.Contains(": sendAs != null && from.Account.Identities.Contains(sendAs) ? sendAs : null;"),
                  "T-11, nel riquadro la conferma e la scrittura di Annulla iscrizione ricevono quell'indirizzo (lettura del sorgente: nessuna prova crea le finestre)");
        }
        else Report.Add("      (T-11: sorgenti non trovati accanto all'eseguibile, il passaggio dell'indirizzo alla finestra di scrittura non è stato letto)");

        // T-12: il clic singolo solo con una firma DKIM del dominio del mittente.
        string spfOnly = $"mx.google.com; spf=pass smtp.mailfrom=x@{NewsDomain}; dmarc=pass header.from={NewsDomain}",
               otherSigner = $"mx.google.com; dkim=pass header.i=@servizio-invii.example; spf=pass smtp.mailfrom=x@{NewsDomain}; dmarc=pass header.from={NewsDomain}",
               failedSignature = $"mx.google.com; dkim=fail header.i=@{NewsDomain}; spf=pass smtp.mailfrom=x@{NewsDomain}; dmarc=pass header.from={NewsDomain}",
               subdomain = $"mx.google.com; dkim=pass header.d=posta.{NewsDomain}; spf=pass smtp.mailfrom=x@{NewsDomain}; dmarc=pass header.from={NewsDomain}";
        UnsubscribeAction? For(string auth) => Unsubscribe.For(NewsGmail, NewsMail(), NewsBody(list, OneClick, auth), false);
        var results = SenderCheck.Own(NewsGmail, [spfOnly])!;
        Check(SenderCheck.Authenticated(results, NewsDomain) && !SenderCheck.Signed(results, NewsDomain)
              && For(spfOnly) is { Kind: UnsubscribeKind.Mailto } && For(otherSigner) is { Kind: UnsubscribeKind.Mailto } && For(failedSignature) is { Kind: UnsubscribeKind.Mailto }
              && For(NewsAuth()) is { Kind: UnsubscribeKind.OneClick } && For(subdomain) is { Kind: UnsubscribeKind.OneClick }
              && Unsubscribe.For(NewsGmail, NewsMail(), NewsBody($"<{NewsHttps}>", OneClick, spfOnly), false) is { Kind: UnsubscribeKind.Web },
              "T-12, Annulla iscrizione con un clic solo con una firma DKIM superata del dominio del mittente (anche di un suo sottodominio): con DMARC superato per il solo SPF, " +
              "con la firma di un altro dominio o con la firma non superata resta il messaggio da inviare, o la pagina");

        // T-13: lo stesso sito in una forma sola, in punycode.
        var lookalike = new UnsubscribeAction(UnsubscribeKind.Web, Unsubscribe.Links("<https://liste.nоtizie-prova.example/via?id=1#fine>").Https ?? ""); // la "о" è cirillica
        var click = new UnsubscribeAction(UnsubscribeKind.OneClick, lookalike.Target);
        var texts = new[] { Unsubscribe.Confirmation(lookalike, "Notizie di prova", account.Email).Body, Unsubscribe.Confirmation(click, "Notizie di prova", account.Email).Body };
        var longLink = new UnsubscribeAction(UnsubscribeKind.Web, "https://liste.notizie-prova.example/" + new string('a', 400));
        Check(lookalike.Target.Contains('о') && lookalike.Where.StartsWith("liste.xn--") && lookalike.Shown == $"https://{lookalike.Where}/via?id=1#fine"
              && texts.All(t => !t.Contains('о') && t.Contains(lookalike.Shown)) && new UnsubscribeAction(UnsubscribeKind.Web, NewsHttps).Shown == NewsHttps
              && new UnsubscribeAction(UnsubscribeKind.Mailto, NewsMailto).Shown == NewsMailto && longLink.Shown.Length == 301 && longLink.Shown.EndsWith('…'),
              $"T-13, la conferma di Annulla iscrizione mostra il sito in una forma sola: anche nell'indirizzo intero il nome con una lettera non latina è in punycode ({lookalike.Shown}); " +
              "un indirizzo normale e un mailto: restano com'erano, uno lungo si accorcia a 300 caratteri");
    }

    /// <summary>
    /// Un ascoltatore su 127.x.y.z che segna la prima riga di ogni richiesta e risponde 404. Per i percorsi di rete (UNC)
    /// sta sulla porta 80 di un indirizzo di loopback scelto a caso: quando la condivisione non c'è, Windows prova lo stesso
    /// percorso via WebDAV (servizio WebClient) su quella porta, e ricorda per un minuto i server che non hanno risposto,
    /// quindi ogni prova ha il suo indirizzo. Nulla esce dal PC.
    /// </summary>
    sealed class Sec3Listener : IDisposable
    {
        readonly TcpListener listener;
        readonly List<string> requests = new();
        public string Host { get; }
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        Sec3Listener(TcpListener listener, string host, string? response = null)
        {
            this.listener = listener;
            Host = host;
            var reply = Encoding.ASCII.GetBytes(response ?? "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        using var client = await listener.AcceptTcpClientAsync();
                        client.ReceiveTimeout = 1500;
                        var buffer = new byte[2048];
                        int read = 0;
                        try { read = client.GetStream().Read(buffer, 0, buffer.Length); } catch (IOException) { }
                        lock (requests) requests.Add(Encoding.ASCII.GetString(buffer, 0, read).Split('\r', '\n')[0]);
                        try { client.GetStream().Write(reply); } catch (IOException) { }
                    }
                }
                catch (Exception) { } // chiuso
            });
        }

        /// <summary>Sulla porta 80 di un indirizzo di loopback a caso; null se la porta non si può prendere.</summary>
        public static Sec3Listener? Dav()
        {
            var host = $"127.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(2, 255)}";
            try
            {
                var listener = new TcpListener(IPAddress.Parse(host), 80);
                listener.Start();
                return new Sec3Listener(listener, host);
            }
            catch (SocketException)
            {
                return null;
            }
        }

        /// <summary>Su una porta libera di 127.0.0.1, per gli indirizzi http; response è la risposta intera, se non il 404.</summary>
        public static Sec3Listener Http(string? response = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new Sec3Listener(listener, "127.0.0.1", response);
        }

        /// <summary>Le richieste arrivate, dopo aver aspettato che ne arrivi almeno una (al più wait millisecondi).</summary>
        public List<string> Requests(int wait)
        {
            for (var clock = Stopwatch.StartNew(); clock.ElapsedMilliseconds < wait; Thread.Sleep(25))
                lock (requests)
                    if (requests.Count > 0) break;
            lock (requests) return requests.ToList();
        }

        public void Dispose() => listener.Stop();
    }

    /// <summary>
    /// I blocchi in un RichTextBox senza finestra, disposto come lo disporrebbe la finestra di scrittura: è lì che WPF
    /// cerca i caratteri. Dà l'eccezione del disegno, o null.
    /// </summary>
    static string? Sec3Layout(IEnumerable<System.Windows.Documents.Block> blocks)
    {
        try
        {
            var document = new FlowDocument();
            document.Blocks.AddRange(blocks);
            var box = new System.Windows.Controls.RichTextBox(document);
            box.Measure(new System.Windows.Size(600, 400));
            box.Arrange(new System.Windows.Rect(0, 0, 600, 400));
            box.UpdateLayout();
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }

    /// <summary>I nomi dei caratteri scritti negli elementi di questi blocchi.</summary>
    static List<string> Sec3FontsOf(IEnumerable<System.Windows.Documents.Block> blocks)
    {
        var document = new FlowDocument();
        var list = blocks.ToList();
        document.Blocks.AddRange(list);
        var fonts = new List<string>();
        for (var p = document.ContentStart; p != null; p = p.GetNextContextPosition(LogicalDirection.Forward))
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is TextElement e
                && e.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily family)
                fonts.Add(family.Source);
        document.Blocks.Clear();
        return fonts;
    }

    /// <summary>
    /// T-31: il font-family di una bozza ripresa (o di una risposta automatica) arrivava a new FontFamily com'era. Per WPF
    /// "indirizzo#nome" è un carattere da cercare a quell'indirizzo: con un percorso di rete il disegno dell'editor apre
    /// una connessione verso quel server. Qui il server è un indirizzo di loopback con un ascoltatore sulla porta 80: prima
    /// si guarda che la prova veda (un Run con quel carattere, disposto, fa arrivare una richiesta), poi che la stessa cosa
    /// passata da HtmlToFlow non ne faccia arrivare. Lo stesso per il carattere delle impostazioni e per l'RTF di una firma.
    /// </summary>
    static void Sec3Fonts()
    {
        string[] locations =
        [
            @"\\server.example\caratteri\#Prova", "file://server.example/caratteri/#Prova", @"\\127.0.0.1\C$\#Prova", "./#Prova", "#Prova", "C:/Windows/Fonts/#Arial", "pack://application:,,,/#Prova",
            "http://server.example/#Prova", @"..\..\#Prova", "Arial#", "/caratteri/Prova", "C:Prova",
        ];
        (string Written, string Kept)[] names =
        [
            ("Georgia", "Georgia"), ("Times New Roman", "Times New Roman"), ("Segoe UI Semibold", "Segoe UI Semibold"), ("-apple-system", "-apple-system"), ("Noto Sans CJK JP", "Noto Sans CJK JP"),
            ("Source Sans 3", "Source Sans 3"), ("Arial_MT", "ArialMT"), (".SF NS Text", "SF NS Text"), ("游ゴシック", "游ゴシック"),
        ];
        var passed = locations.Where(l => HtmlToFlow.FontName(l) != null || !HtmlToFlow.IsFontLocation(l)).ToList();
        var changed = names.Where(n => HtmlToFlow.FontName(n.Written) != n.Kept || HtmlToFlow.IsFontLocation(n.Written)).Select(n => n.Written).ToList();
        Check(passed.Count == 0 && changed.Count == 0 && HtmlToFlow.FontName(null) == null && HtmlToFlow.FontName("") == null && HtmlToFlow.FontName(new string('a', 65)) == null && HtmlToFlow.FontName("!!!") == null,
              $"T-31, il nome di un carattere scritto da altri: {locations.Length} indirizzi (percorsi di rete, file://, pack://, ./#nome) non sono un nome, {names.Length} nomi veri restano" +
              (passed.Count + changed.Count == 0 ? "" : $" (passati: {string.Join(" | ", passed)}; cambiati: {string.Join(" | ", changed)})"));

        // La bozza: ogni forma in un elemento diverso, più due caratteri veri.
        static string Draft(string host) =>
            $"<div style=\"font-family:'\\\\{host}\\caratteri\\#Prova'\"><p>uno</p></div><p><span style='font-family:file://{host}/caratteri/#Prova'>due</span> " +
            $"<span style='font-family:\"\\\\{host}\\DavWWWRoot\\caratteri\\#Prova\", Arial'>tre</span></p><table><tr><td><b style=\"font-family:\\\\{host}\\caratteri\\#Altro\">quattro</b></td></tr></table>" +
            "<p style=\"font-family:Georgia\">cinque <span style=\"font-family:'Times New Roman',serif\">sei</span></p>";
        var fonts = Sec3FontsOf(HtmlToFlow.Blocks(Draft("server.example")));
        Check(fonts.Count == 2 && fonts.Contains("Georgia") && fonts.Contains("Times New Roman"),
              $"T-31, una bozza con quattro font-family che sono percorsi di rete e due caratteri veri: nell'editor arrivano solo i due veri ({string.Join(" | ", fonts)})");

        // Il carattere delle impostazioni (anche da una copia di sicurezza scritta da altri) e l'RTF di una firma o di un modello.
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new Settings { ComposeFont = "Calibri" }, SettingsJson.Default.Settings))!;
        var honest = System.Text.Json.JsonSerializer.Deserialize(json.ToJsonString(), SettingsJson.Default.Settings)!;
        bool written = (string?)json["ComposeFont"] == "Calibri";
        json["ComposeFont"] = @"\\server.example\caratteri\#Prova";
        var hostile = System.Text.Json.JsonSerializer.Deserialize(json.ToJsonString(), SettingsJson.Default.Settings)!;
        static string Rtf(string host) => @"{\rtf1\ansi{\fonttbl{\f0 Georgia;}{\f1 \\\\" + host + @"\\caratteri\\#Prova;}{\f2 file://" + host + @"/caratteri/#Prova;}}\f0 uno \f1 due \f2 tre\par}";
        var rtfFonts = Sec3FontsOf(UI.RichEditor.BlocksFromRtf(Rtf("server.example")));
        Check(written && hostile.ComposeFont == "Segoe UI" && honest.ComposeFont == "Calibri" && rtfFonts.Count > 0 && rtfFonts.All(f => f == "Georgia"),
              $"T-31, un indirizzo al posto del carattere dei messaggi nelle impostazioni diventa \"{hostile.ComposeFont}\", un nome vero resta; dell'RTF di una firma con tre caratteri, due dei quali indirizzi, resta {string.Join(" | ", rtfFonts.Distinct())}");

        // Nel motore di WPF: che cosa parte. Prima il controllo che la prova veda.
        using var control = Sec3Listener.Dav();
        using var draft = Sec3Listener.Dav();
        using var signature = Sec3Listener.Dav();
        if (control == null || draft == null || signature == null)
        {
            Report.Add("      (T-31: la porta 80 di un indirizzo di loopback non si può prendere, la ricerca dei caratteri in rete non è stata misurata)");
            return;
        }
        var clock = Stopwatch.StartNew();
        var raw = new Paragraph(new Run("controllo") { FontFamily = new FontFamily($@"\\{control.Host}\caratteri\#Prova") });
        var thrown = Sec3Layout([raw]);
        var seen = control.Requests(3000);
        long controlMs = clock.ElapsedMilliseconds;
        if (seen.Count == 0)
        {
            Report.Add($"      (T-31: un carattere con un percorso di rete non ha fatto arrivare nessuna richiesta all'ascoltatore in {controlMs} ms: su questo PC la prova non vede (il servizio WebClient è spento?), non misurato)");
            return;
        }
        var draftThrown = Sec3Layout(HtmlToFlow.Blocks(Draft(draft.Host)));
        var signatureThrown = Sec3Layout(UI.RichEditor.BlocksFromRtf(Rtf(signature.Host)));
        Thread.Sleep(700);
        var fromDraft = draft.Requests(0);
        var fromSignature = signature.Requests(0);
        Check(fromDraft.Count == 0 && fromSignature.Count == 0 && draftThrown == null && signatureThrown == null,
              $"T-31, nel motore di WPF: un Run con il carattere \\\\{control.Host}\\caratteri\\#Prova, disposto, fa arrivare all'ascoltatore \"{seen[0]}\" in {controlMs} ms" +
              $"{(thrown == null ? "" : " e solleva " + thrown.Split(':')[0])}; la bozza e la firma con gli stessi caratteri, disposte allo stesso modo, nessuna richiesta ({fromDraft.Count + fromSignature.Count}) e nessuna eccezione" +
              ((draftThrown ?? signatureThrown) is { } error ? $" [{error}]" : ""));
    }

    /// <summary>
    /// T-32: Incolla in un editor di Missiva. WPF sceglie per primo lo XAML degli appunti, che leggendolo scarica le immagini
    /// con un indirizzo; e un RTF con un carattere che è un indirizzo lo cerca in rete (T-31). Gli appunti veri non si
    /// toccano: la scelta del formato si prova con oggetti dati fatti a mano e con l'evento che WPF solleva prima di
    /// incollare; che cosa farebbero XAML e RTF si misura caricandoli con TextRange.Load, che usa gli stessi convertitori.
    /// </summary>
    static void Sec3Paste()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        static System.Windows.DataObject Data(params (string Format, object Value)[] formats)
        {
            var data = new System.Windows.DataObject();
            foreach (var (format, value) in formats) data.SetData(format, value);
            return data;
        }
        const string plainRtf = @"{\rtf1\ansi{\fonttbl{\f0 Georgia;}}\f0 testo # normale}";
        const string fontRtf = @"{\rtf1\ansi{\fonttbl{\f0 \\\\server.example\\caratteri\\#Prova;}}\f0 testo}";
        const string escapedRtf = @"{\rtf1\ansi{\fonttbl{\f0 \\\\server.example\\caratteri\\\'23Prova;}}\f0 testo}";
        var xaml = $"<Section {ns}><Paragraph>testo</Paragraph></Section>";
        string rtf = System.Windows.DataFormats.Rtf, text = System.Windows.DataFormats.UnicodeText, xamlFormat = System.Windows.DataFormats.Xaml, package = System.Windows.DataFormats.XamlPackage;
        (string What, string? Got, string? Wanted)[] choices =
        [
            ("XamlPackage, Xaml, RTF e testo", UI.RichEditor.PasteFormat(Data((package, new MemoryStream([1])), (xamlFormat, xaml), (rtf, plainRtf), (text, "testo")), package), rtf),
            ("Xaml, RTF e testo", UI.RichEditor.PasteFormat(Data((xamlFormat, xaml), (rtf, plainRtf), (text, "testo")), xamlFormat), rtf),
            ("Xaml e testo", UI.RichEditor.PasteFormat(Data((xamlFormat, xaml), (text, "testo")), xamlFormat), text),
            ("solo Xaml", UI.RichEditor.PasteFormat(Data((xamlFormat, xaml)), xamlFormat), null),
            ("solo XamlPackage", UI.RichEditor.PasteFormat(Data((package, new MemoryStream([1]))), package), null),
            ("RTF normale e testo", UI.RichEditor.PasteFormat(Data((rtf, plainRtf), (text, "testo")), rtf), rtf),
            ("RTF con un carattere che è un indirizzo, e testo", UI.RichEditor.PasteFormat(Data((rtf, fontRtf), (text, "testo")), rtf), text),
            ("lo stesso, con Xaml davanti", UI.RichEditor.PasteFormat(Data((xamlFormat, xaml), (rtf, fontRtf), (text, "testo")), xamlFormat), text),
            ("lo stesso RTF senza testo", UI.RichEditor.PasteFormat(Data((rtf, fontRtf)), rtf), null),
            ("solo testo", UI.RichEditor.PasteFormat(Data((text, "testo")), text), text),
            ("un formato che non è testo", UI.RichEditor.PasteFormat(Data(("PNG", new MemoryStream([1]))), "PNG"), "PNG"),
        ];
        var wrong = choices.Where(c => c.Got != c.Wanted).Select(c => $"{c.What}: {c.Got ?? "nulla"}").ToList();
        bool escaped = UI.RichEditor.HasFontLocation(escapedRtf), escapedLoads = Sec3FontsOf(Sec3LoadRaw(escapedRtf, rtf)).Any(HtmlToFlow.IsFontLocation);
        Check(wrong.Count == 0 && escaped == escapedLoads && !UI.RichEditor.HasFontLocation(plainRtf) && UI.RichEditor.HasFontLocation(fontRtf),
              $"T-32, che cosa si incolla: {choices.Length} combinazioni di formati negli appunti; lo XAML mai (al suo posto l'RTF, o il testo), l'RTF con un carattere che è un indirizzo come testo" +
              (wrong.Count == 0 ? "" : " (sbagliate: " + string.Join("; ", wrong) + ")") + $"; il cancelletto scritto \\'23 nell'RTF {(escapedLoads ? "WPF lo legge come #, ed è visto" : "WPF non lo legge come #")}");

        // L'evento che WPF solleva prima di incollare (e di lasciar cadere un trascinamento), su un editor vero: gli appunti non servono.
        var editor = new UI.RichEditor();
        System.Windows.DataObjectPastingEventArgs Raise(System.Windows.DataObject data, string format, bool drop = false)
        {
            var args = new System.Windows.DataObjectPastingEventArgs(data, drop, format);
            editor.RaiseEvent(args);
            return args;
        }
        var fromWpf = Raise(Data((package, new MemoryStream([1])), (xamlFormat, xaml), (rtf, plainRtf), (text, "testo")), package);
        var dropped = Raise(Data((xamlFormat, xaml), (text, "testo")), xamlFormat, drop: true);
        var onlyXaml = Raise(Data((xamlFormat, xaml)), xamlFormat);
        var word = Raise(Data((rtf, plainRtf), (text, "testo")), rtf);
        // Quello che si copia da un editor di Missiva porta il suo segno (l'evento di Copia), e il suo XAML si incolla com'è.
        var copied = Data((package, new MemoryStream([1])), (xamlFormat, xaml), (rtf, plainRtf), (text, "testo"));
        new UI.RichEditor().RaiseEvent(new System.Windows.DataObjectCopyingEventArgs(copied, false));
        var own = Raise(copied, package);
        // Il segno deve arrivare agli appunti come lo chiede Windows (un blocco di memoria per quel formato), altrimenti la
        // copia intera fallirebbe: si chiede all'oggetto dati per la strada di OLE, senza toccare gli appunti.
        string marked;
        try
        {
            var ole = (System.Runtime.InteropServices.ComTypes.IDataObject)copied;
            var wanted = new System.Runtime.InteropServices.ComTypes.FORMATETC
            {
                cfFormat = unchecked((short)System.Windows.DataFormats.GetDataFormat(UI.RichEditor.OwnCopy).Id), dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
            };
            int query = ole.QueryGetData(ref wanted);
            ole.GetData(ref wanted, out var medium);
            try { marked = $"{query}, {(medium.tymed == System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL && medium.unionmember != IntPtr.Zero ? "un blocco di " + Sec3GlobalSize(medium.unionmember) + " byte" : "nessun blocco")}"; }
            finally { Sec3ReleaseStgMedium(ref medium); }
        }
        catch (Exception ex)
        {
            marked = ex.GetType().Name + ": " + ex.Message;
        }
        var forged = Data((package, new MemoryStream([1])), (rtf, plainRtf), (text, "testo"), ("Missiva.Editor." + new string('0', 32), new MemoryStream([1])));
        Check(fromWpf is { FormatToApply: var a, CommandCancelled: false } && a == rtf && dropped is { CommandCancelled: false } && dropped.FormatToApply == text && onlyXaml.CommandCancelled
              && word is { CommandCancelled: false } && word.FormatToApply == rtf
              && copied.GetDataPresent(UI.RichEditor.OwnCopy, false) && own is { CommandCancelled: false } && own.FormatToApply == package && UI.RichEditor.PasteFormat(forged, package) == rtf
              && marked.StartsWith("0, un blocco di "),
              "T-32, l'evento di Incolla su un editor di Missiva: con lo XAML di un altro programma negli appunti si incolla l'RTF, in un trascinamento senza RTF il testo, con il solo XAML nulla; " +
              $"un RTF normale resta RTF; quello copiato da un editor di questa Missiva porta il suo segno e si incolla com'è, un segno con un altro nome non vale; il segno si dà a Windows come gli altri formati ({marked})");

        // Che cosa farebbe lo XAML, se si incollasse: un'immagine con un indirizzo parte appena lo si legge.
        using (var http = Sec3Listener.Http())
        {
            var blocks = Sec3LoadRaw($"<Section {ns}><Paragraph><InlineUIContainer><Image Source=\"http://127.0.0.1:{http.Port}/immagine.png\" Width=\"10\" Height=\"10\"/></InlineUIContainer>testo</Paragraph></Section>", xamlFormat);
            var seen = http.Requests(3000);
            Report.Add($"      (T-32, misura: uno XAML con un'immagine da http://127.0.0.1, letto come lo leggerebbe Incolla, {(seen.Count > 0 ? $"fa partire \"{seen[0]}\"" : "non fa partire nulla")}; " +
                       $"nel documento {(blocks.Count > 0 ? "entra" : "non entra")})");
        }

        // Copia e incolla dentro Missiva: ora passa dall'RTF che WPF mette negli appunti insieme al suo XAML. Che cosa si perde.
        var source = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 11 * 96 / 72.0 };
        var first = new Paragraph();
        first.Inlines.Add(new Run("Avviso ") { FontFamily = new FontFamily("Georgia"), FontSize = 16 * 96 / 72.0 });
        first.Inlines.Add(new Run("in rosso") { Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)) });
        first.Inlines.Add(new Run(" evidenziato") { Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0x00)) });
        first.Inlines.Add(new Run(" grassetto") { FontWeight = System.Windows.FontWeights.Bold });
        first.Inlines.Add(new Run(" corsivo") { FontStyle = System.Windows.FontStyles.Italic });
        first.Inlines.Add(new Run(" sottolineato") { TextDecorations = System.Windows.TextDecorations.Underline });
        source.Blocks.Add(first);
        source.Blocks.Add(new Paragraph(new Run("Testo al centro")) { TextAlignment = System.Windows.TextAlignment.Center });
        source.Blocks.Add(new Paragraph(new Run("Testo rientrato")) { Margin = new System.Windows.Thickness(40, 0, 0, 0) });
        var list = new System.Windows.Documents.List { MarkerStyle = System.Windows.TextMarkerStyle.Disc };
        list.ListItems.Add(new ListItem(new Paragraph(new Run("voce uno"))));
        list.ListItems.Add(new ListItem(new Paragraph(new Run("voce due"))));
        source.Blocks.Add(list);
        var link = new Hyperlink(new Run("un link")) { NavigateUri = new Uri("https://www.example.com/pagina") };
        source.Blocks.Add(new Paragraph(link));
        var table = UI.ComposeWindow.NewTable(3, 2);
        int n = 0;
        foreach (var cell in table.RowGroups[0].Rows.SelectMany(r => r.Cells)) ((Paragraph)cell.Blocks.FirstBlock).Inlines.Add(new Run($"cella {++n}"));
        source.Blocks.Add(table);
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(24, 12, 96, 96, PixelFormats.Bgra32, null, new byte[24 * 12 * 4].Select((_, i) => (byte)(i % 4 == 3 ? 255 : i % 7 * 30)).ToArray(), 24 * 4);
        source.Blocks.Add(new Paragraph(new InlineUIContainer(EditorImages.Create(EditorImages.FromBitmap(bitmap), 24)!)));
        string Through(string format)
        {
            using var stream = new MemoryStream();
            new TextRange(source.ContentStart, source.ContentEnd).Save(stream, format);
            stream.Position = 0;
            var target = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 11 * 96 / 72.0 };
            new TextRange(target.ContentStart, target.ContentEnd).Load(stream, format);
            var images = new List<InlineImage>();
            var html = FlowToHtml.Convert(target, images);
            foreach (var image in images) html = html.Replace(image.ContentId, "immagine");
            return html;
        }
        string viaPackage = Through(package), viaRtf = Through(rtf);
        var original = FlowToHtml.Convert(source, new List<InlineImage>());
        int common = 0;
        while (common < Math.Min(viaPackage.Length, viaRtf.Length) && viaPackage[common] == viaRtf[common]) common++;
        Report.Add($"      (T-32, misura: un testo di Missiva con carattere, colori, grassetto, elenco, link, tabella e immagine, copiato e incollato: via XamlPackage {viaPackage.Length} caratteri di HTML, via RTF {viaRtf.Length}, " +
                   (viaPackage == viaRtf ? "uguali" : $"diversi dal carattere {common}: XamlPackage \"...{Sec3Cut(viaPackage, common)}\", RTF \"...{Sec3Cut(viaRtf, common)}\"") + $"; l'originale {original.Length})");

        // Il tempo della lettura in più (l'RTF con un cancelletto si legge due volte): 2.000 paragrafi.
        var big = new StringBuilder(@"{\rtf1\ansi{\fonttbl{\f0 Georgia;}}\f0 ");
        for (int k = 0; k < 2000; k++) big.Append("riga numero #").Append(k).Append(@" con un po' di testo per arrivare a una misura vera\par ");
        big.Append('}');
        var clock = Stopwatch.StartNew();
        bool found = UI.RichEditor.HasFontLocation(big.ToString());
        Check(!found && clock.ElapsedMilliseconds < 3000, $"T-32, un RTF di {big.Length / 1024} KB con 2.000 paragrafi e un cancelletto in ogni riga si controlla in {clock.ElapsedMilliseconds} ms, e resta RTF");
    }

    static string Sec3Cut(string text, int from) => text[Math.Max(0, from - 30)..Math.Min(text.Length, from + 90)];

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GlobalSize")] static extern nuint Sec3GlobalSize(IntPtr handle);
    [System.Runtime.InteropServices.DllImport("ole32.dll", EntryPoint = "ReleaseStgMedium")] static extern void Sec3ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);

    /// <summary>Un testo in un formato degli appunti, caricato in un documento come lo caricherebbe Incolla, senza nessun filtro.</summary>
    static List<System.Windows.Documents.Block> Sec3LoadRaw(string content, string format)
    {
        var scratch = new FlowDocument();
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            new TextRange(scratch.ContentStart, scratch.ContentEnd).Load(stream, format);
        }
        catch (Exception)
        {
            return [];
        }
        var blocks = scratch.Blocks.ToList();
        scratch.Blocks.Clear();
        return blocks;
    }

    /// <summary>Un gestore HTTP finto con qualcosa da fare prima di ogni richiesta (cambiare un file a metà di un salvataggio).</summary>
    sealed class Sec3Before(HttpMessageHandler inner, Action<HttpRequestMessage> before) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            before(request);
            return base.SendAsync(request, cancel);
        }
    }

    /// <summary>
    /// T-23 e T-24: Graph. Un indirizzo intero in una risposta (nextLink) riceve il token solo se è di Graph; i pezzi di un
    /// allegato grande non seguono un reindirizzamento del server di caricamento; un file che cambia fra la misura e il
    /// caricamento non parte troncato.
    /// </summary>
    static async Task Sec3GraphAsync()
    {
        // T-23: gli indirizzi a cui può andare il token.
        const string graph = "https://graph.microsoft.com/v1.0/";
        string[] refused =
        [
            "https://altrove-prova.example/v1.0/me/messages?$skip=50", "https://graph.microsoft.com.altrove-prova.example/v1.0/me/messages", "https://graph.microsoft.com:8443/v1.0/me/messages",
            "https://utente@graph.microsoft.com/v1.0/me/messages", "https://graph.microsoft.com@altrove-prova.example/v1.0/me", "https://xgraph.microsoft.com/v1.0/me", "https://127.0.0.1/v1.0/me", "https://",
        ];
        var passed = refused.Where(url => { try { GraphProvider.Target(url); return true; } catch (MailException) { return false; } }).ToList();
        Check(passed.Count == 0 && GraphProvider.Target("me/messages?$top=5") == graph + "me/messages?$top=5" && GraphProvider.Target(graph + "me/messages?$skip=50") == graph + "me/messages?$skip=50"
              && GraphProvider.Target("https://GRAPH.microsoft.com/v1.0/me") == "https://GRAPH.microsoft.com/v1.0/me" && GraphProvider.Target("http://altrove-prova.example/x").StartsWith(graph),
              $"T-23, Graph: una richiesta con il token va sotto {graph} o a un indirizzo intero di Graph; {refused.Length} indirizzi interi di altri (un altro sito, il nome di Graph come prefisso o come nome utente, un'altra porta) non partono" +
              (passed.Count == 0 ? "" : " (passati: " + string.Join(" | ", passed) + ")"));

        // Lo stesso dentro una lettura a pagine: la seconda pagina è a un indirizzo di un altro.
        var seen = new List<(string Host, bool Token)>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            seen.Add((request.RequestUri!.Host, request.Headers.Authorization != null));
            var page = new System.Text.Json.Nodes.JsonObject
            {
                ["value"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["id"] = "m" + seen.Count, ["isDraft"] = false, ["receivedDateTime"] = "2026-10-10T08:00:00Z" }),
                ["@odata.nextLink"] = "https://altrove-prova.example/v1.0/me/mailFolders/inbox/messages?$skip=1",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var provider = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@outlook.example" });
        string? stopped = null;
        try { await provider.GetRecentKeysAsync(new MailFolder { Id = "inbox", Name = "Posta in arrivo" }, DateTime.UtcNow.AddDays(-30), 100); }
        catch (MailException ex) { stopped = ex.Message; }
        Check(stopped != null && seen.Count == 1 && seen[0] == ("graph.microsoft.com", true),
              $"T-23, una pagina di Graph che rimanda la successiva a un altro sito: la lettura si ferma (\"{stopped}\") e il token non ci va " +
              $"({string.Join(", ", seen.GroupBy(s => s).Select(g => $"{g.Count()} a {g.Key.Host}{(g.Key.Token ? " con il token" : " senza")}"))})");

        // I pezzi di un allegato grande: un 307 del server di caricamento. Con il gestore delle altre richieste il pezzo
        // arriva dove dice il reindirizzamento; con quello dei pezzi no. Due ascoltatori su 127.0.0.1.
        using (var elsewhere = Sec3Listener.Http())
        using (var upload = Sec3Listener.Http($"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{elsewhere.Port}/altrove\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"))
        {
            async Task<(int Status, List<string> Elsewhere)> Put(HttpMessageHandler handler)
            {
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                using var response = await client.PutAsync($"http://127.0.0.1:{upload.Port}/sessione", new ByteArrayContent("un pezzo del file"u8.ToArray()));
                return ((int)response.StatusCode, elsewhere.Requests(400));
            }
            var withPieces = await Put(GraphProvider.UploadHandler());
            int afterPieces = withPieces.Elsewhere.Count;
            var withOthers = await Put(Network.ApiHandler());
            Check(withPieces.Status == 307 && afterPieces == 0 && withOthers.Elsewhere.Count == 1 && withOthers.Elsewhere[0].StartsWith("PUT /altrove"),
                  $"T-23, un 307 del server a cui si caricano i pezzi: con il gestore dei pezzi la risposta resta {withPieces.Status} e all'altro indirizzo non arriva nulla ({afterPieces}); " +
                  $"con il gestore delle altre richieste, che i pezzi usavano prima, ci arriva \"{withOthers.Elsewhere.FirstOrDefault()}\"");
        }

        // T-24: un file grande che cresce dopo la misura, prima del caricamento (qui mentre Graph riceve il testo della bozza).
        var fake = new UploadGraph();
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-sicurezza3-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string growing = Path.Combine(dir, "contratto-prova.pdf"), steady = Path.Combine(dir, "listino-prova.pdf"), small = Path.Combine(dir, "nota-prova.txt");
            int large = GraphProvider.LargeAttachment + 4321;
            var bytes = new byte[large];
            new Random(7).NextBytes(bytes);
            File.WriteAllBytes(growing, bytes);
            File.WriteAllBytes(steady, bytes);
            File.WriteAllBytes(small, bytes[..2000]);
            bool grown = false;
            GraphProvider.UseTestHandler(new Sec3Before(fake, request =>
            {
                if (grown || request.Method != HttpMethod.Patch) return;
                grown = true;
                using var more = new FileStream(growing, FileMode.Append, FileAccess.Write);
                more.Write(new byte[5000]);
            }));
            var graphProvider = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.prova@outlook.example" });
            List<Address> to = [new Address { Email = "maria.rossi@example.com" }];
            string? changed = null;
            try { await graphProvider.SaveDraftAsync(new OutgoingMessage { To = to, Subject = "Contratto", Html = "<p>In allegato</p>", AttachmentPaths = [growing] }); }
            catch (MailException ex) { changed = ex.Message; }
            int putsAfterGrowing = fake.Puts;
            var truncated = fake.Drafts.Values.SelectMany(d => d).Where(a => a.Name == "contratto-prova.pdf").Select(a => a.Content.Length).ToList();
            // Un file che non cambia parte come prima, e uno piccolo anche.
            var saved = await graphProvider.SaveDraftAsync(new OutgoingMessage { To = to, Subject = "Listino", Html = "<p>In allegato</p>", AttachmentPaths = [steady, small] });
            var stored = fake.Drafts[saved.Message.Id];
            Check(grown && changed != null && changed.Contains("contratto-prova.pdf") && putsAfterGrowing == 0 && truncated.Count == 0
                  && stored.Count == 2 && stored.Any(a => a.Name == "listino-prova.pdf" && a.Content.SequenceEqual(bytes)) && stored.Any(a => a.Name == "nota-prova.txt" && a.Content.Length == 2000) && fake.Wrong.Count == 0,
                  $"T-24, un allegato grande che cresce di 5.000 byte dopo la misura: non parte troncato ({putsAfterGrowing} pezzi inviati, sul server {(truncated.Count == 0 ? "nulla" : truncated[0] + " byte dei " + (large + 5000))}), " +
                  $"e lo si dice (\"{changed}\"); un file che non cambia e uno piccolo partono come prima");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// T-25: il canale con nome di Missiva (mailto:, "apri", "esci") con il nome già preso da un altro processo. Su un
    /// canale di prova con un nome suo: che cosa fa Windows quando il nome c'è già, e il ciclo che riprova oltre i cinque
    /// errori di prima e riparte quando il nome si libera. Un secondo account di Windows qui non c'è: chi occupa il nome
    /// è questo stesso processo.
    /// </summary>
    static async Task Sec3ChannelAsync()
    {
        var name = "Missiva-prova-sicurezza3-" + Guid.NewGuid().ToString("N")[..8];
        System.IO.Pipes.NamedPipeServerStream Server() => new(name, System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        async Task SendAsync(string line)
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(5000);
            await client.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        }
        var squatter = Server();
        string taken;
        try
        {
            using var second = Server();
            taken = "si apre lo stesso";
        }
        catch (Exception ex)
        {
            taken = ex.GetType().Name;
        }
        var received = new List<string>();
        int failures = 0;
        using var stop = new CancellationTokenSource();
        var serving = UI.App.ServeCommandsAsync(name, line => { lock (received) received.Add(line); }, stop.Token, n => { failures = n; return 15; });
        for (var clock = Stopwatch.StartNew(); failures < 12 && clock.ElapsedMilliseconds < 5000;) await Task.Delay(20);
        int whileTaken = failures;
        bool stillTrying = !serving.IsCompleted;
        squatter.Dispose();
        string? error = null;
        try
        {
            await SendAsync("mailto:maria.rossi@example.com?subject=Prova");
            await SendAsync("apri");
            await SendAsync("esci");
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
        }
        for (var clock = Stopwatch.StartNew(); clock.ElapsedMilliseconds < 3000;)
        {
            lock (received)
                if (received.Count >= 3) break;
            await Task.Delay(20);
        }
        stop.Cancel();
        bool ended = await Task.WhenAny(serving, Task.Delay(3000)) == serving;
        List<string> got;
        lock (received) got = received.ToList();
        Check(taken != "si apre lo stesso" && whileTaken >= 12 && stillTrying && error == null && got.SequenceEqual(["mailto:maria.rossi@example.com?subject=Prova", "apri", "esci"]) && ended,
              $"T-25, il canale con nome già preso (qui da questo stesso processo): Windows non lo lascia aprire una seconda volta ({taken}); Missiva riprova ({whileTaken} tentativi, prima smetteva al quinto per tutta la sessione) " +
              $"e quando il nome si libera riceve di nuovo mailto:, \"apri\" ed \"esci\" ({got.Count} comandi in fila{(error == null ? "" : ", errore " + error)})");
    }

    /// <summary>
    /// T-27: il comando con cui Windows avvia Missiva per un link mailto:. Windows mette il link al posto di %1 senza toccare
    /// le virgolette, e quello che segue una virgoletta arriva come un argomento a parte (misurato con un protocollo di prova,
    /// vedi ESITO.md): dopo --mailto conta solo il link.
    /// </summary>
    static void Sec3MailtoCommand()
    {
        static string Show(string[] args) => "[" + string.Join(" | ", args) + "]";
        (string[] Given, string[] Wanted)[] cases =
        [
            (["--mailto", "mailto:a@b.example"], ["mailto:a@b.example"]),
            (["--mailto", "mailto:a@b.example", "--nascosta", "x"], ["mailto:a@b.example"]),
            (["--mailto", "mailto:a@b.example", "--riavvio", "-ToastActivated", "--pdf-worker"], ["mailto:a@b.example"]),
            (["--mailto", "--nascosta", "mailto:a@b.example", "mailto:altro@b.example"], ["mailto:a@b.example"]),
            (["--mailto", "--nascosta"], []),
            (["--mailto"], []),
            (["mailto:a@b.example"], ["mailto:a@b.example"]),
            (["--nascosta"], ["--nascosta"]),
            (["--riavvio", "--nascosta"], ["--riavvio", "--nascosta"]),
            (["-ToastActivated", "-Embedding"], ["-ToastActivated", "-Embedding"]),
            (["--pdf-worker"], ["--pdf-worker"]),
            ([], []),
        ];
        var wrong = cases.Where(c => !Program.Arguments(c.Given).SequenceEqual(c.Wanted)).Select(c => $"{Show(c.Given)} dà {Show(Program.Arguments(c.Given))}").ToList();
        string? command = SourceRoot() is { } root ? File.ReadAllLines(Path.Combine(root, "installa.ps1")).FirstOrDefault(l => l.Contains(@"shell\open\command"" -Value")) : null;
        Check(wrong.Count == 0 && (command == null || command.Contains("`\"$exe`\" --mailto `\"%1`\"")),
              $"T-27, avviata per un link mailto: (--mailto) Missiva prende solo il link: quello che segue una virgoletta del link non è più un'opzione ({cases.Length} righe di comando" +
              (wrong.Count == 0 ? "" : "; sbagliate: " + string.Join("; ", wrong)) + $"); {(command == null ? "installa.ps1 non letto" : "installa.ps1 registra il comando con --mailto")}; senza --mailto tutto com'era");
    }

    /// <summary>
    /// T-28: la versione finale con gli strumenti di prova (dotnet publish -c Release -p:MissivaProva=true
    /// -p:DefineConstants=DEBUG%3BTRACE, come dice CLAUDE.md) deve avere anche PROVA, che la chiude se parte senza
    /// MISSIVA_DATA. Qui si chiede a MSBuild quali costanti darebbe al compilatore, senza compilare; e la regola con cui
    /// una versione con i ganci di prova si chiude da sola (Program.ClosesWithoutData), senza avviare nulla. Fino alle
    /// scelte del 12 ottobre la regola guardava la cartella e gli argomenti, e la Debug nella sua cartella bin\Debug
    /// partiva lo stesso: ora guarda solo la variabile, e si chiude sempre (il controllo della scelta è in Choices3Tests).
    /// </summary>
    static void Sec3TestBuild()
    {
        Check(Program.Build.Contains("DEBUG") && Program.ClosesWithoutData(null) && Program.ClosesWithoutData("") && !Program.ClosesWithoutData(@"C:\Progetti\Missiva\prova-dati"),
              $"T-28, senza MISSIVA_DATA un eseguibile con i ganci di prova (questo: {Program.Build}) si chiude da solo: sempre, dovunque stia (prova-release, la scrivania, bin\\Debug) e chiunque " +
              "lo avvii (una notifica, un doppio clic, uno script); fino alle scelte del 12 ottobre la Debug nella sua cartella bin\\Debug no");

        if (SourceRoot() is not { } root)
        {
            Report.Add("      (T-28: sorgenti non trovati accanto all'eseguibile, le costanti di compilazione non sono state chieste a MSBuild)");
            return;
        }
        string? Constants(params string[] properties) => Property("DefineConstants", properties);
        string? Property(string name, params string[] properties)
        {
            try
            {
                var start = new ProcessStartInfo(DotnetExe()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in (string[])["msbuild", Path.Combine(root, "Missiva.csproj"), "-nologo", "-getProperty:" + name, .. properties]) start.ArgumentList.Add(a);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(120_000))
                {
                    process.Kill(true);
                    return null;
                }
                return process.ExitCode == 0 ? output.GetAwaiter().GetResult().Trim() : null;
            }
            catch (Exception)
            {
                return null; // dotnet non c'è nel percorso
            }
        }
        static bool Has(string? constants, string name) => constants != null && constants.Split(';').Any(c => c.Trim() == name);
        // T-29: nessuna variabile d'ambiente MISSIVA_ nel codice che entra nella versione installata (fuori da #if DEBUG o
        // PROVA), letta dai sorgenti riga per riga con le direttive del compilatore.
        var read = new List<string>();
        int files = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar) || relative.StartsWith("obj" + Path.DirectorySeparatorChar)) continue;
            files++;
            var conditions = new Stack<string>();
            int number = 0;
            foreach (var raw in File.ReadLines(file))
            {
                number++;
                var line = raw.TrimStart();
                if (line.StartsWith("#if ")) conditions.Push(line[4..].Trim());
                else if (line.StartsWith("#elif ") && conditions.Count > 0) { conditions.Pop(); conditions.Push(line[6..].Trim()); }
                else if (line.StartsWith("#else") && conditions.Count > 0) conditions.Push("!(" + conditions.Pop() + ")");
                else if (line.StartsWith("#endif") && conditions.Count > 0) conditions.Pop();
                else
                {
                    bool testOnly = conditions.Any(c => !c.StartsWith('!') && (c.Contains("DEBUG") || c.Contains("PROVA")) || c.StartsWith("!(!(") || c.StartsWith("!(!"));
                    int comment = line.IndexOf("//", StringComparison.Ordinal);
                    var code = comment >= 0 ? line[..comment] : line;
                    if (!testOnly && System.Text.RegularExpressions.Regex.Match(code, "\"(MISSIVA_[A-Z_]+)\"") is { Success: true } m) read.Add($"{relative}:{number} {m.Groups[1].Value}");
                }
            }
        }
        Check(read.Count == 0 && files > 100,
              $"T-29, nel codice che entra nella versione installata nessuna variabile d'ambiente MISSIVA_ ({files} file letti con le loro direttive #if)" + (read.Count == 0 ? "" : ": restano " + string.Join("; ", read)));

        var clock = Stopwatch.StartNew();
        var tools = Constants("-p:Configuration=Release", "-p:MissivaProva=true", "-p:DefineConstants=DEBUG%3BTRACE");
        var test = Constants("-p:Configuration=Release", "-p:MissivaProva=true");
        var installed = Constants("-p:Configuration=Release");
        if (tools == null || test == null || installed == null)
        {
            Report.Add("      (T-28: dotnet msbuild non ha risposto, le costanti di compilazione non sono state lette)");
            return;
        }
        Check(Has(tools, "DEBUG") && Has(tools, "PROVA") && Has(test, "PROVA") && !Has(test, "DEBUG") && !Has(installed, "PROVA") && !Has(installed, "DEBUG"),
              $"T-28, le costanti che MSBuild dà al compilatore: versione finale con gli strumenti di prova (-p:MissivaProva=true -p:DefineConstants=DEBUG%3BTRACE) \"{tools}\", con PROVA; " +
              $"versione finale di prova \"{test}\"; versione che si installa \"{installed}\", senza DEBUG né PROVA ({clock.ElapsedMilliseconds} ms)");
        string? hooks = Property("StartupHookSupport", "-p:Configuration=Release"), debugHooks = Property("StartupHookSupport");
        Check(hooks == "false" && debugHooks == "",
              $"T-29, la versione che si installa ha StartupHookSupport \"{hooks}\": .NET non carica gli assembly di DOTNET_STARTUP_HOOKS; la Debug non lo imposta (\"{debugHooks}\")");
    }
}
#endif
