using System.Text.RegularExpressions;
using MailKit;
using MimeKit;

namespace Missiva;

/// <summary>
/// Quello che l'intestazione del riquadro dice di chi scrive davvero, calcolato da Missiva dalle intestazioni, senza
/// eseguire nulla del messaggio: mittente non verificato (Authentication-Results del server della propria casella con
/// DMARC fallito, o SPF e DKIM entrambi falliti), risposte che vanno a un altro indirizzo (Reply-To), il nome di un
/// contatto con un indirizzo diverso, mittente esterno all'organizzazione (account di lavoro).
/// </summary>
/// <param name="Unverified">Il dominio che non si è potuto verificare, o null.</param>
/// <param name="Why">La spiegazione per il suggerimento della fascia rossa.</param>
/// <param name="LookAlike">L'indirizzo che conosci per il nome del mittente, se il messaggio viene da un altro.</param>
public sealed record SenderNotes(string? Unverified, string Why, bool External, string? LookAlike)
{
    /// <summary>L'indirizzo (o il dominio) scritto nel nome del mittente, se non è quello da cui il messaggio arriva (SenderCheck.NameAddress).</summary>
    public string? NameAddress { get; init; }
}

public static class SenderCheck
{
    /// <summary>Una Authentication-Results letta (RFC 8601): chi l'ha scritta e i risultati dei tre controlli.</summary>
    public sealed record Results(string? AuthservId, string? Dmarc, string? Spf, List<string> Dkim)
    {
        /// <summary>Il dominio del From per cui vale il risultato di DMARC (header.from).</summary>
        public string? DmarcFrom { get; init; }
        /// <summary>Il dominio del mittente della busta per cui vale il risultato di SPF (smtp.mailfrom).</summary>
        public string? SpfDomain { get; init; }
        /// <summary>I domini delle firme DKIM superate (header.d, o header.i dopo la chiocciola).</summary>
        public List<string> DkimDomains { get; init; } = new();
        /// <summary>
        /// Scritta male o in modo ambiguo (una virgoletta o una parentesi mai chiusa, una virgoletta dentro un commento): i
        /// risultati non superati valgono lo stesso, ma non basta per dire autenticato (Authenticated).
        /// </summary>
        public bool Malformed { get; init; }
        /// <summary>
        /// Non è certo che l'abbia scritta il server della casella (audit dell'11 ottobre, T-03 e T-04): se dice che qualcosa
        /// non va vale lo stesso, perché chi manda non ha interesse a scriverlo (la fascia rossa, Failed); se dice che va
        /// tutto bene non prova nulla (Authenticated). Lo decide <see cref="Own"/>.
        /// </summary>
        public bool Unproven { get; init; }
    }

    /// <summary>
    /// "mx.google.com; dkim=pass header.i=@x.it; spf=pass (commento) smtp.mailfrom=...; dmarc=pass header.from=x.it", oppure
    /// come Microsoft senza authserv-id: "spf=pass (sender IP is ...) smtp.mailfrom=x.it; dkim=pass ...;dmarc=pass action=none".
    /// Dentro ci sono pezzi scelti da chi scrive (il mittente della busta, che Gmail ripete anche in un commento), quindi
    /// (audit, S-23): i risultati si dividono ai ";" fuori dalle stringhe fra virgolette e dai commenti (Split); il metodo è
    /// solo il primo nome=valore di ogni risultato, mai quello che sta nel valore di una proprietà; con due risultati di
    /// DMARC o di SPF in disaccordo vale il peggiore (le firme DKIM sono più d'una per natura: restano tutte); una
    /// proprietà scritta due volte con valori diversi non dice di chi è il risultato.
    /// </summary>
    public static Results Parse(string header)
    {
        var parts = Split(header, out bool malformed);
        string? authserv = null, dmarc = null, spf = null, dmarcFrom = null, spfDomain = null;
        var dkim = new List<string>();
        var dkimDomains = new List<string>();
        for (int k = 0; k < parts.Count; k++)
        {
            var part = parts[k].Text.Trim();
            var result = Regex.Match(part, @"^([A-Za-z][A-Za-z0-9-]*)(?:\s*/\s*[0-9]+)?\s*=\s*([A-Za-z]+)");
            if (k == 0 && !result.Success)
            {
                authserv = part.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
                continue;
            }
            if (!result.Success) continue;
            var value = result.Groups[2].Value.ToLowerInvariant();
            // Dopo il punto in cui l'intestazione è scritta male un "pass" potrebbe averlo scritto chi manda: non conta.
            if (!parts[k].Trusted && value is "pass" or "bestguesspass") continue;
            var words = Words(part);
            switch (result.Groups[1].Value.ToLowerInvariant())
            {
                case "dmarc":
                    if (dmarc != null && Badness(value) <= Badness(dmarc)) break;
                    dmarc = value;
                    dmarcFrom = Property(words, "header.from") is { Length: > 0 } from ? from : null;
                    break;
                case "spf":
                    if (spf != null && Badness(value) <= Badness(spf)) break;
                    spf = value;
                    spfDomain = Property(words, "smtp.mailfrom") is { Length: > 0 } sender ? sender : null;
                    break;
                case "dkim":
                    dkim.Add(value);
                    if (value == "pass" && (Property(words, "header.d") ?? Property(words, "header.i")) is { Length: > 0 } signer) dkimDomains.Add(signer);
                    break;
            }
        }
        return new Results(authserv, dmarc, spf, dkim) { DmarcFrom = dmarcFrom, SpfDomain = spfDomain, DkimDomains = dkimDomains, Malformed = malformed };
    }

    /// <summary>Quanto è cattivo un risultato di DMARC o di SPF: fra due in disaccordo vale il peggiore.</summary>
    static int Badness(string value) => value switch { "pass" => 0, "bestguesspass" => 1, "none" => 2, "softfail" => 4, "fail" => 5, _ => 3 };

    /// <summary>
    /// I risultati di una Authentication-Results (RFC 8601, con le regole della RFC 5322): divisi ai ";" che stanno fuori
    /// dalle stringhe fra virgolette e dai commenti fra parentesi; "\" protegge il carattere dopo, i commenti si annidano e
    /// valgono uno spazio, le stringhe restano con le loro virgolette. Una stringa o un commento mai chiusi, o una
    /// virgoletta dentro un commento (per la RFC è un carattere qualsiasi, ma Gmail ci scrive il mittente della busta con le
    /// sue virgolette: le due letture non concordano), dicono che lì dentro c'è qualcosa scelto da chi manda: da quel punto
    /// (malformed) ogni carattere vale per sé e si divide a ogni ";", così un risultato vero non resta nascosto, e i pezzi
    /// cominciati dopo non sono fidati (Trusted falso: Parse ne prende solo i risultati non superati). Una passata, al più
    /// due.
    /// </summary>
    static List<(string Text, bool Trusted)> Split(string header, out bool malformed)
    {
        var parts = new List<(string, bool)>();
        var text = new System.Text.StringBuilder();
        bool plain = false, trusted = true, quoted = false;
        int depth = 0, opener = 0, kept = 0; // dove comincia la stringa o il commento più esterno in corso, e il testo che c'era
        for (int i = 0; i <= header.Length; i++)
        {
            bool end = i == header.Length;
            if (end && (plain || !quoted && depth == 0)) break;
            char c = end ? '\0' : header[i];
            if (plain)
            {
                if (c == ';')
                {
                    parts.Add((text.ToString(), trusted));
                    text.Clear();
                    trusted = false;
                }
                else text.Append(c);
            }
            else if (end || depth > 0 && c == '"')
            {
                // Mai chiuso, o ambiguo: si riparte dall'apertura, senza più stringhe né commenti.
                plain = true;
                quoted = false;
                depth = 0;
                text.Length = kept;
                i = opener - 1;
            }
            else if (depth > 0)
            {
                if (c == '\\' && i + 1 < header.Length) i++;
                else if (c == '(') depth++;
                else if (c == ')' && --depth == 0) text.Append(' ');
            }
            else if (quoted)
            {
                text.Append(c);
                if (c == '\\' && i + 1 < header.Length) text.Append(header[++i]);
                else if (c == '"') quoted = false;
            }
            else if (c == '"' || c == '(')
            {
                opener = i;
                kept = text.Length;
                if (c == '(') depth = 1;
                else
                {
                    quoted = true;
                    text.Append(c);
                }
            }
            else if (c == ';')
            {
                parts.Add((text.ToString(), true));
                text.Clear();
            }
            else text.Append(c);
        }
        parts.Add((text.ToString(), trusted));
        malformed = plain;
        return parts;
    }

    /// <summary>
    /// Le parole di un risultato: divise agli spazi fuori dalle virgolette, con gli spazi attorno a "=" che non dividono
    /// (header.from = x.it): così una proprietà scritta dentro il valore di un'altra, fra virgolette, non è una proprietà.
    /// </summary>
    static List<string> Words(string part)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < part.Length; i++)
        {
            char c = part[i];
            if (quoted)
            {
                word.Append(c);
                if (c == '\\' && i + 1 < part.Length) word.Append(part[++i]);
                else if (c == '"') quoted = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                int next = i;
                while (next < part.Length && char.IsWhiteSpace(part[next])) next++;
                bool joins = word.Length > 0 && (word[^1] == '=' || next < part.Length && part[next] == '=');
                if (!joins && word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
                i = next - 1;
            }
            else
            {
                if (c == '"') quoted = true;
                word.Append(c);
            }
        }
        if (word.Length > 0) words.Add(word.ToString());
        return words;
    }

    /// <summary>
    /// Il dominio di una proprietà del risultato (header.from=x.it, header.i=@x.it, smtp.mailfrom=rimbalzi@x.it,
    /// smtp.mailfrom="a b"@x.it): null se non c'è o è vuota, una stringa vuota se c'è due volte con valori diversi.
    /// </summary>
    static string? Property(List<string> words, string name)
    {
        string? found = null;
        foreach (var word in words)
        {
            if (word.Length <= name.Length || word[name.Length] != '=' || !word.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            var value = word[(name.Length + 1)..].Trim('"', '<', '>');
            value = value[(value.LastIndexOf('@') + 1)..].TrimEnd('.').ToLowerInvariant();
            if (value.Length == 0) continue;
            if (found != null && found != value) return "";
            found = value;
        }
        return found;
    }

    /// <summary>
    /// Il messaggio viene davvero dal dominio del suo From (audit, S-04; domain è il dominio del marchio,
    /// SenderLogos.DomainOf): il server della casella dice DMARC superato per quel dominio. Mai con la fascia rossa
    /// (Failed), con un'intestazione scritta male (Malformed: audit, S-23) o che non si sa se l'ha scritta il server
    /// della casella (Unproven). Dalle scelte del 12 ottobre 2026 (audit dell'11 ottobre, T-06, e la scelta in attesa dal
    /// blocco 2) DMARC superato serve sempre: prima, per i domini senza DMARC, bastava quello che DMARC controllerebbe
    /// (<see cref="Aligned"/>), con l'allineamento fatto qui a due etichette: un messaggio da cliente1.piattaforma.example
    /// firmato da cliente2.piattaforma.example valeva come autenticato per piattaforma.example. Ora l'allineamento lo
    /// giudica il server, con la sua lista dei suffissi; un dominio senza DMARC resta senza logo e senza l'annullamento
    /// con un clic (nella copia di prova dei dati: 4 domini su 114).
    /// </summary>
    public static bool Authenticated(Results r, string domain) => r.Dmarc == "pass" && Aligned(r, domain);

    /// <summary>
    /// La regola fino al 12 ottobre 2026: DMARC superato per quel dominio, oppure, senza un risultato di DMARC (dominio
    /// senza politica, o "bestguesspass" di Microsoft), una firma DKIM superata di quel dominio o SPF superato per un
    /// mittente della busta di quel dominio. Da sola non autentica più nessuno: <see cref="Authenticated"/> la chiede
    /// insieme a DMARC superato (con DMARC superato ma senza header.from dice per quale dominio vale), e
    /// SenderLogos.RecheckFromCopies la usa per ritrovare i domini che avevano il segno solo così.
    /// </summary>
    internal static bool Aligned(Results r, string domain)
    {
        bool Same(string? other) => other != null && string.Equals(SenderLogos.DomainOf("x@" + other), domain, StringComparison.OrdinalIgnoreCase);
        if (Failed(r) || r.Malformed || r.Unproven) return false;
        if (r.Dmarc == "pass" && r.DmarcFrom != null) return Same(r.DmarcFrom);
        if (r.Dmarc is not (null or "pass" or "none" or "bestguesspass")) return false;
        return r.DkimDomains.Any(Same) || (r.Spf == "pass" && Same(r.SpfDomain));
    }

    /// <summary>
    /// Autenticato (Authenticated) e con una firma DKIM superata del dominio del From, non con il solo SPF: per
    /// l'annullamento dell'iscrizione con un clic la RFC 8058 accetta solo quella (audit dell'11 ottobre, T-12). Con DMARC
    /// superato via SPF, da un servizio d'invio condiviso, le intestazioni List-Unsubscribe non le ha firmate nessuno.
    /// Che la firma copra proprio quelle due intestazioni (h= della DKIM-Signature) qui non si vede.
    /// </summary>
    public static bool Signed(Results r, string domain) =>
        Authenticated(r, domain) && r.DkimDomains.Any(d => string.Equals(SenderLogos.DomainOf("x@" + d), domain, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// La Authentication-Results del server della propria casella, mai una aggiunta dal mittente: Gmail la prima con
    /// authserv-id mx.google.com; Microsoft la prima in alto (Exchange Online la scrive senza authserv-id o con il suo);
    /// IMAP la prima in alto, solo se l'authserv-id è del dominio del server IMAP o dell'indirizzo.
    /// Dall'audit dell'11 ottobre, dove non è certo che l'abbia scritta il server il risultato è Unproven, e vale solo per
    /// la fascia rossa. Su un account IMAP sempre (T-03): molti server non scrivono la loro e non tolgono quelle che
    /// arrivano, e bastava mettere in cima al messaggio "mail.dominio-del-destinatario; dmarc=pass" per avere il dominio
    /// autenticato per sempre e l'annullamento con un clic. Con Gmail quando la sua non è la prima in alto (T-04): sopra ce
    /// n'è una di un altro server, quindi il messaggio non è arrivato a Gmail per posta (preso da un'altra casella,
    /// importato) e quella con il suo nome può averla scritta chi manda. Con Microsoft il nome deve essere il suo o stare
    /// sotto il suo, non solo finire allo stesso modo (T-05: "xoutlook.com" passava).
    /// </summary>
    public static Results? Own(MailAccount account, IReadOnlyList<string> headers)
    {
        if (headers.Count == 0) return null;
        switch (account.Kind)
        {
            case AccountKind.Gmail:
                for (int k = 0; k < headers.Count; k++)
                    if (Parse(headers[k]) is { AuthservId: "mx.google.com" } google) return k == 0 ? google : google with { Unproven = true };
                return null;
            case AccountKind.Microsoft:
            {
                var top = Parse(headers[0]);
                return top.AuthservId == null || MailAutoconfig.InDomain(top.AuthservId, "outlook.com") || MailAutoconfig.InDomain(top.AuthservId, "microsoft.com") ? top : null;
            }
            default:
            {
                var top = Parse(headers[0]);
                var domains = new[] { Settings.ImageKey(account.Imap?.ImapHost ?? ""), Settings.ImageKey(AccountTypes.DomainOf(account.Email)) };
                return top.AuthservId != null && domains.Contains(Settings.ImageKey(top.AuthservId)) ? top with { Unproven = true } : null;
            }
        }
    }

    /// <summary>DMARC fallito, oppure SPF fallito e DKIM non superato (senza un DMARC superato).</summary>
    public static bool Failed(Results r) =>
        r.Dmarc == "fail" || r.Spf is "fail" or "softfail" && !r.Dkim.Contains("pass") && r.Dmarc != "pass";

    public static SenderNotes Of(MailAccount account, MessageSummary summary, MessageBody body)
    {
        var domain = AccountTypes.DomainOf(summary.From.Email);
        string? unverified = null, why = "";
        if (Own(account, body.AuthenticationResults) is { } r && Failed(r))
        {
            unverified = domain.Length > 0 ? domain : summary.From.Email;
            why = T("Il server della tua casella non ha potuto verificare il mittente (DMARC {0}, SPF {1}, DKIM {2}). Il messaggio potrebbe non venire da chi dice: non aprire link o allegati e non fare pagamenti senza una conferma per un'altra strada.",
                    r.Dmarc ?? T("assente"), r.Spf ?? T("assente"), r.Dkim.Count == 0 ? T("assente") : string.Join(", ", r.Dkim));
        }
        bool external = AccountTypes.IsWork(account) && domain.Length > 0 && !SameOrganization(domain, AccountTypes.DomainOf(account.Email));
        return new SenderNotes(unverified, why, external, AddressBook.LookAlike(summary.From)) { NameAddress = NameAddress(summary.From) };
    }

    /// <summary>
    /// Perché Rispondi dalla notifica non va, o null se va. La notifica non ha i segni che il riquadro mostra contro le frodi
    /// sui bonifici: risposte verso un altro dominio (Reply-To), mittente esterno per un account di lavoro, il nome di un
    /// contatto da un altro indirizzo, un nome che è l'indirizzo di un altro. In quei casi si risponde dalla finestra di
    /// scrittura, dove A si vede.
    /// </summary>
    public static string? QuickReplyRisk(MailAccount account, MessageSummary message)
    {
        var domain = AccountTypes.DomainOf(message.From.Email);
        if (message.ReplyTo.Any(a => !SameOrganization(AccountTypes.DomainOf(a.Email), domain)))
            return T("le risposte vanno a un dominio diverso da quello del mittente");
        var notes = Of(account, message, new MessageBody());
        if (notes.External) return T("mittente esterno all'organizzazione");
        if (notes.LookAlike != null) return T("nome di un contatto con un altro indirizzo");
        if (notes.NameAddress != null) return T("nome del mittente con l'indirizzo di un altro");
        return null;
    }

    /// <summary>
    /// Un nome come va mostrato (audit, S-25): senza i caratteri che non si vedono (cambi di direzione del testo, larghezza
    /// zero, trattino facoltativo, riempitivi, caratteri di controllo), che servono a nascondere o a girare quello che c'è
    /// scritto, e con gli spazi di ogni tipo e gli a capo ridotti a uno spazio. Ci passano tutti i nomi (Address.Name).
    /// Quasi sempre non c'è nulla da togliere, e torna la stessa stringa.
    /// </summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        bool clean = name[0] != ' ' && name[^1] != ' ';
        for (int i = 0; clean && i < name.Length; i++)
            clean = name[i] is >= ' ' and < (char)0x7F && !(name[i] == ' ' && name[i - 1] == ' ');
        if (clean) return name;
        var result = new System.Text.StringBuilder(name.Length);
        bool space = false;
        foreach (var rune in name.EnumerateRunes())
        {
            if (System.Text.Rune.IsWhiteSpace(rune) || rune.Value == 0x2800) space = result.Length > 0;
            else if (!Unseen(rune, shown: true))
            {
                if (space) result.Append(' ');
                space = false;
                result.Append(rune.ToString());
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// L'indirizzo, o il dominio, scritto nel nome mostrato quando non è quello da cui il messaggio arriva (audit, S-25):
    /// "assistenza@banca.example" &lt;x@altro.example&gt;, il travestimento più comune, perché elenco e notifiche mostrano il
    /// nome. Null se nel nome non c'è un indirizzo, se è lo stesso del mittente, o se è della stessa organizzazione (il nome
    /// "assistenza@negozio.example" da ticket+123@posta.negozio.example; il marchio "Negozio.example" da lì); dentro la
    /// posta personale (gmail.com: Settings.ImageKey) vale solo lo stesso indirizzo. Una passata sul nome; Address.Display lo
    /// chiede una volta sola per indirizzo.
    /// </summary>
    public static string? NameAddress(Address from)
    {
        var name = from.Name;
        var email = from.Email.Trim();
        if (name.Length < 4 || email.Length == 0) return null;
        bool ascii = true, marked = false;
        foreach (char c in name)
        {
            if (c >= 0x80) ascii = false;
            else if (c is '@' or '.') marked = true;
        }
        if (ascii && !marked) return null;
        if (!ascii)
        {
            // La chiocciola e il punto a larghezza intera o piccoli si vedono come quelli veri.
            try { name = name.Normalize(System.Text.NormalizationForm.FormKC); }
            catch (ArgumentException) { }
        }
        string own = Settings.ImageKey(AccountTypes.DomainOf(email));
        static bool InDomain(char c) => char.IsLetterOrDigit(c) || c is '.' or '-';
        // Un indirizzo: qualcosa@dominio.tld, o solo @dominio.tld.
        char[]? rest = null; // il nome senza gli indirizzi già guardati
        for (int at = name.IndexOf('@'); at >= 0; at = name.IndexOf('@', at + 1))
        {
            int start = at, end = at + 1;
            while (start > 0 && (InDomain(name[start - 1]) || name[start - 1] is '_' or '+')) start--;
            while (end < name.Length && InDomain(name[end])) end++;
            Array.Fill(rest ??= name.ToCharArray(), ' ', start, end - start);
            string local = name[start..at], domain = name[(at + 1)..end].Trim('.', '-');
            if (!domain.Contains('.')) continue;
            string found = local + "@" + domain;
            if (found.Equals(email, StringComparison.OrdinalIgnoreCase)) continue;
            bool personal = Settings.ImageKey("x@" + domain).Contains('@');
            if (local.Length > 0 && personal || !Settings.ImageKey(domain).Equals(own, StringComparison.OrdinalIgnoreCase)) return local.Length > 0 ? found : domain;
        }
        // Un dominio scritto per esteso (www.banca.example, Banca.example): solo con una fine di dominio fra le più comuni,
        // perché "Dr.Rossi" e "Mario.Rossi" non lo sono.
        if (rest != null) name = new string(rest);
        for (int i = 0; i < name.Length; i++)
        {
            if (!InDomain(name[i])) continue;
            int start = i;
            while (i < name.Length && InDomain(name[i])) i++;
            var word = name[start..i].Trim('.', '-');
            int dot = word.LastIndexOf('.');
            if (dot < 2 || !CommonEndings.Contains(word[(dot + 1)..]) || word.Contains("..")) continue;
            if (!Settings.ImageKey(word).Equals(own, StringComparison.OrdinalIgnoreCase)) return word;
        }
        return null;
    }

    /// <summary>Le fini di dominio con cui un nome come "Banca-prova.it" si legge come un sito (NameAddress); "example" è quella delle prove.</summary>
    static readonly HashSet<string> CommonEndings = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "it", "net", "org", "eu", "info", "biz", "io", "co", "uk", "de", "fr", "es", "ch", "at", "nl", "be", "us", "app", "online", "shop", "store", "site", "example",
    };

    /// <summary>
    /// Un nome ridotto a quello che l'occhio vede, per confrontarlo con i nomi dei contatti (audit, S-24); non è un nome da
    /// mostrare. Forma di compatibilità di Unicode (lettere a larghezza intera, matematiche, legature) senza gli accenti
    /// aggiunti; senza caratteri invisibili, di formato e di controllo; le lettere cirilliche e greche che imitano le
    /// latine scritte come quelle; minuscole, con i e 1 come l, 0 come o, "rn" come "m"; gli spazi di ogni tipo ridotti a
    /// uno. Non è l'elenco intero dei caratteri che si confondono (Unicode TR 39): solo i trucchi più comuni.
    /// </summary>
    public static string Skeleton(string name)
    {
        string text;
        try { text = name.Normalize(System.Text.NormalizationForm.FormKD); }
        catch (ArgumentException) { text = name; } // un surrogato spaiato
        var result = new System.Text.StringBuilder(text.Length);
        bool space = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (System.Text.Rune.IsWhiteSpace(rune))
            {
                space = result.Length > 0;
                continue;
            }
            if (Unseen(rune) || rune.Value is >= 0x0300 and <= 0x036F) continue;
            if (space) result.Append(' ');
            space = false;
            char latin = Latin(rune.Value);
            if (latin == '\0') result.Append(System.Text.Rune.ToLowerInvariant(rune).ToString());
            else result.Append(latin switch { 'i' or '1' => 'l', '0' => 'o', _ => latin });
        }
        return result.Replace("rn", "m").ToString();
    }

    /// <summary>
    /// Un carattere che non si vede: di controllo, di formato (larghezza zero, cambi di direzione del testo, trattino
    /// facoltativo, etichette), un surrogato spaiato, un riempitivo che si disegna vuoto.
    /// </summary>
    /// <param name="shown">Per un nome da mostrare: restano i caratteri che .NET non conosce ancora (emoji nuove) e il segno di un carattere rovinato.</param>
    internal static bool Unseen(System.Text.Rune rune, bool shown = false) =>
        System.Text.Rune.GetUnicodeCategory(rune) switch
        {
            System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Surrogate => true,
            System.Globalization.UnicodeCategory.OtherNotAssigned => !shown,
            _ => rune.Value is 0x034F or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or 0x2800 or 0x3164 or 0xFFA0 || rune.Value == 0xFFFD && !shown,
        };

    /// <summary>La lettera latina minuscola (o la cifra) per un carattere ASCII o per una lettera che la imita; '\0' per gli altri.</summary>
    static char Latin(int c) => c switch
    {
        >= 'A' and <= 'Z' => (char)(c + 32),
        < 0x80 => (char)c,
        // Latine che non si scompongono: ı, ł, ø, đ.
        0x0131 => 'i', 0x0142 or 0x0141 => 'l', 0x00F8 or 0x00D8 => 'o', 0x0111 or 0x0110 => 'd',
        // Greche: Α Β Ε Ζ Η Ι Κ Μ Ν Ο Ρ Τ Υ Χ, poi α γ ε η ι κ μ ν ο ρ τ υ χ ω e le forme ϲ ϳ.
        0x0391 => 'a', 0x0392 => 'b', 0x0395 => 'e', 0x0396 => 'z', 0x0397 => 'h', 0x0399 => 'i', 0x039A => 'k', 0x039C => 'm', 0x039D => 'n',
        0x039F => 'o', 0x03A1 => 'p', 0x03A4 => 't', 0x03A5 => 'y', 0x03A7 => 'x',
        0x03B1 => 'a', 0x03B3 => 'y', 0x03B5 => 'e', 0x03B7 => 'n', 0x03B9 => 'i', 0x03BA => 'k', 0x03BC => 'u', 0x03BD => 'v', 0x03BF => 'o',
        0x03C1 => 'p', 0x03C4 => 't', 0x03C5 => 'u', 0x03C7 => 'x', 0x03C9 => 'w', 0x03F2 => 'c', 0x03F3 => 'j',
        // Cirilliche: А В Е К М Н О Р С Т У Х, poi а е к м о р с т у х г п, e Ѕ І Ј Һ Ӏ Ԁ Ԛ Ԝ Ѵ con le loro minuscole.
        0x0410 => 'a', 0x0412 => 'b', 0x0415 => 'e', 0x041A => 'k', 0x041C => 'm', 0x041D => 'h', 0x041E => 'o', 0x0420 => 'p', 0x0421 => 'c',
        0x0422 => 't', 0x0423 => 'y', 0x0425 => 'x',
        0x0430 => 'a', 0x0435 => 'e', 0x043A => 'k', 0x043C => 'm', 0x043E => 'o', 0x0440 => 'p', 0x0441 => 'c', 0x0442 => 't', 0x0443 => 'y',
        0x0445 => 'x', 0x0433 => 'r', 0x043F => 'n',
        0x0405 or 0x0455 => 's', 0x0406 or 0x0456 => 'i', 0x0408 or 0x0458 => 'j', 0x04BA or 0x04BB => 'h', 0x04C0 or 0x04CF => 'l',
        0x0500 or 0x0501 => 'd', 0x051A or 0x051B => 'q', 0x051C or 0x051D => 'w', 0x0474 or 0x0475 => 'v',
        _ => '\0',
    };

    /// <summary>Lo stesso dominio, o un suo sottodominio (studio.it e mail.studio.it).</summary>
    public static bool SameOrganization(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase) || b.EndsWith("." + a, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Solo le intestazioni Authentication-Results e, nella stessa richiesta, le due di "Annulla iscrizione" (Unsubscribe)
    /// (BODY.PEEK[HEADER.FIELDS (...)]): per i messaggi grandi, scaricati parte per parte, insieme al corpo. Quelli piccoli
    /// arrivano interi, intestazioni comprese.
    /// </summary>
    internal static async Task<HeaderList> FetchAsync(IMailFolder folder, UniqueId uid) =>
        await FetchIfThereAsync(folder, uid) ?? new HeaderList();

    /// <summary>Lo stesso, ma null se il messaggio non c'è più (spostato o eliminato): "non le ha" e "non lo so" restano distinti.</summary>
    internal static async Task<HeaderList?> FetchIfThereAsync(IMailFolder folder, UniqueId uid)
    {
        var request = new FetchRequest(MessageSummaryItems.UniqueId, new[] { "Authentication-Results", "List-Unsubscribe", "List-Unsubscribe-Post" });
        var s = (await folder.FetchAsync(new[] { uid }, request)).FirstOrDefault();
        return s == null ? null : s.Headers ?? new HeaderList();
    }

    public static List<string> FromHeaders(HeaderList headers) =>
        headers.Where(h => h.Field.Equals("Authentication-Results", StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToList();
}
