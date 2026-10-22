using System.Globalization;
using System.Text;

namespace Missiva;

/// <summary>
/// Una ricerca con i filtri, come i Filtri della ricerca del nuovo Outlook: le parole libere più Da, A, Oggetto, Dal, Al,
/// Con allegati, Non letti e Contrassegnati. Scritta come testo con parole chiave italiane ("fattura da:TIM ha:allegati
/// dal:1/3/2026"), che si possono anche battere a mano e che restano così nelle ricerche recenti; il resto del testo
/// (anche la sintassi di Gmail, from:, OR, participants:) passa com'è. Con Missiva in inglese valgono anche le parole chiave
/// inglesi ("invoice from:TIM has:attachment after:2026-03-01"), che sono quelle che ToString scrive; le italiane restano
/// accettate sempre (le ricerche recenti fatte in italiano). Ogni provider la traduce nella sua ricerca: Gmail
/// X-GM-RAW (ToGmail), Graph $search KQL o $filter (ToKql, Filter, Matches), IMAP i criteri di SEARCH.
/// </summary>
public sealed record SearchTerms
{
    /// <summary>Le parole libere, com'erano scritte.</summary>
    public string Text { get; init; } = "";
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Subject { get; init; } = "";
    /// <summary>Dal giorno (compreso), nell'ora di Windows.</summary>
    public DateTime? Since { get; init; }
    /// <summary>Al giorno (compreso), nell'ora di Windows.</summary>
    public DateTime? Until { get; init; }
    public bool HasAttachments { get; init; }
    public bool Unread { get; init; }
    public bool Flagged { get; init; }

    /// <summary>Se c'è almeno un filtro oltre alle parole libere.</summary>
    public bool HasFilters => From.Length > 0 || To.Length > 0 || Subject.Length > 0 || Since != null || Until != null || HasAttachments || Unread || Flagged;

    /// <summary>Solo i filtri, senza le parole libere.</summary>
    public SearchTerms Filters => this with { Text = "" };

    // ---------------------------------------------------------------- testo

    /// <summary>
    /// Le parole di un testo, con le virgolette che tengono insieme più parole (da:"Mario Rossi"). Una virgoletta mai chiusa
    /// non tiene insieme nulla: prima da:"Mario fattura metteva nel filtro tutto il resto del testo (audit dell'11 ottobre,
    /// C-62). Le virgolette si chiudono a coppie da sinistra: quella spaiata è l'ultima aperta, e alla seconda passata è un
    /// carattere qualsiasi.
    /// </summary>
    static List<string> Tokens(string text)
    {
        int stray = -1;
        while (true)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            int opened = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' && i != stray)
                {
                    quoted = !quoted;
                    if (quoted) opened = i;
                }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (current.Length > 0) tokens.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0) tokens.Add(current.ToString());
            if (!quoted) return tokens;
            stray = opened;
        }
    }

    /// <summary>Il valore di un filtro senza le sue virgolette, nemmeno quelle in mezzo: riscritto (Quote) non si rileggerebbe uguale.</summary>
    static string Unquote(string value) => value.Replace("\"", "");

    static string Quote(string value) => Unquote(value) is var plain && plain.Any(char.IsWhiteSpace) ? $"\"{plain}\"" : plain;

    /// <summary>Per Gmail, dentro from:(...): senza parentesi né virgolette, che spaiate cambiano il senso di tutta la ricerca.</summary>
    static string Plain(string value) => string.Concat(value.Where(c => c is not ('(' or ')' or '"'))).Trim();

    /// <summary>
    /// Una data scritta come 25/10/2026, 25/10/26, 25/10 o 2026-10-25. Senza l'anno è l'ultima volta che quel giorno c'è
    /// stato: quest'anno, o l'anno scorso se quest'anno deve ancora venire (dal:25/12 scritto a gennaio era il prossimo
    /// dicembre, e la ricerca non trovava nulla: audit dell'11 ottobre, C-62).
    /// </summary>
    /// <param name="today">Il giorno di oggi (le prove lo simulano).</param>
    /// <param name="english">
    /// La data di una parola chiave inglese (after:, before:): prima come la scrive chi usa l'inglese, con la cultura di
    /// Lingua.Culture (12/03 è il 12 marzo in en-GB e il 3 dicembre in en-US; anche "12 March", "March 12, 2026"), poi
    /// come sempre. Le parole chiave italiane leggono le date come sempre, in qualunque lingua.
    /// </param>
    public static DateTime? ParseDate(string text, DateTime? today = null, bool english = false)
    {
        text = text.Trim();
        if (english && ParseEnglishDate(text, Lingua.Culture, today) is { } written) return written;
        string[] formats = ["d/M/yyyy", "d/M/yy", "d-M-yyyy", "d.M.yyyy", "yyyy-M-d"];
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return day.Date;
        // Con l'anno 2000, bisestile: 29/2 si legge, e vale per l'ultimo anno che l'ha avuto.
        if (!DateTime.TryParseExact(text + "/2000", "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) return null;
        return LastTime(day, today);
    }

    /// <summary>Un giorno senza anno (day, con l'anno 2000) l'ultima volta che c'è stato fino a oggi, negli ultimi otto anni.</summary>
    static DateTime? LastTime(DateTime day, DateTime? today)
    {
        var now = (today ?? DateTime.Today).Date;
        for (int year = now.Year; year > now.Year - 8; year--)
            if (day.Day <= DateTime.DaysInMonth(year, day.Month) && new DateTime(year, day.Month, day.Day) is var found && found <= now) return found;
        return null;
    }

    /// <summary>Nella data corta della cultura il mese viene prima del giorno (en-US: M/d/yyyy).</summary>
    static bool MonthFirst(CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.ShortDatePattern.Replace("y", "");
        int month = pattern.IndexOf('M'), day = pattern.IndexOf('d');
        return month >= 0 && day >= 0 && month < day;
    }

    /// <summary>
    /// Una data scritta in inglese, con la cultura data: in cifre nell'ordine della sua data corta (12/03/2026, 12/03/26,
    /// 12-03-2026, 12.03.2026, 12/03), o con il nome del mese ("12 March", "12 Mar 2026", "March 12, 2026", "Mar 12").
    /// Senza l'anno è l'ultima volta che quel giorno c'è stato, come in ParseDate. Null se non si legge così.
    /// </summary>
    internal static DateTime? ParseEnglishDate(string text, CultureInfo culture, DateTime? today = null)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        const DateTimeStyles style = DateTimeStyles.AllowWhiteSpaces;
        bool monthFirst = MonthFirst(culture);
        string[] withYear = monthFirst
            ? ["M/d/yyyy", "M/d/yy", "M-d-yyyy", "M.d.yyyy", "MMMM d yyyy", "MMMM d, yyyy", "MMM d yyyy", "MMM d, yyyy", "d MMMM yyyy", "d MMM yyyy", "yyyy-M-d"]
            : ["d/M/yyyy", "d/M/yy", "d-M-yyyy", "d.M.yyyy", "d MMMM yyyy", "d MMM yyyy", "MMMM d yyyy", "MMMM d, yyyy", "MMM d yyyy", "MMM d, yyyy", "yyyy-M-d"];
        if (DateTime.TryParseExact(text, withYear, culture, style, out var day)) return day.Date;
        // Senza l'anno, con l'anno 2000 aggiunto in fondo (bisestile: 29 February si legge).
        string[] withoutYear = monthFirst ? ["M/d", "M-d", "MMMM d", "MMM d", "d MMMM", "d MMM"] : ["d/M", "d-M", "d MMMM", "d MMM", "MMMM d", "MMM d"];
        if (DateTime.TryParseExact(text + " 2000", withoutYear.Select(f => f + " yyyy").ToArray(), culture, style, out day)) return LastTime(day, today);
        return null;
    }

    /// <summary>
    /// La ricerca da un testo: da:, a:, oggetto:, dal:, al:, ha:allegati, è:nonletto, è:contrassegnato (anche senza accento,
    /// e:) diventano filtri; il resto sono le parole libere. Una data che non si legge resta fra le parole. Con Missiva in
    /// inglese anche from:, to:, subject:, after:, before:, has:attachment, is:unread e is:flagged (in italiano restano
    /// parole libere, come sempre: la sintassi di Gmail passa al provider).
    /// </summary>
    public static SearchTerms Parse(string text)
    {
        var terms = new SearchTerms();
        var free = new List<string>();
        bool english = Lingua.English;
        foreach (var token in Tokens(text))
        {
            int colon = token.IndexOf(':');
            var key = colon > 0 ? token[..colon].ToLowerInvariant() : "";
            var value = colon > 0 ? Unquote(token[(colon + 1)..]).Trim() : "";
            switch (key)
            {
                case "da" when value.Length > 0: terms = terms with { From = value }; continue;
                case "a" when value.Length > 0: terms = terms with { To = value }; continue;
                case "oggetto" when value.Length > 0: terms = terms with { Subject = value }; continue;
                case "dal" when ParseDate(value) is { } since: terms = terms with { Since = since }; continue;
                case "al" when ParseDate(value) is { } until: terms = terms with { Until = until }; continue;
                case "ha" when value.Equals("allegati", StringComparison.OrdinalIgnoreCase): terms = terms with { HasAttachments = true }; continue;
                case "è" or "e" when value.Replace("-", "").Replace(" ", "").Equals("nonletto", StringComparison.OrdinalIgnoreCase): terms = terms with { Unread = true }; continue;
                case "è" or "e" when value.Equals("contrassegnato", StringComparison.OrdinalIgnoreCase): terms = terms with { Flagged = true }; continue;
                case "from" when english && value.Length > 0: terms = terms with { From = value }; continue;
                case "to" when english && value.Length > 0: terms = terms with { To = value }; continue;
                case "subject" when english && value.Length > 0: terms = terms with { Subject = value }; continue;
                case "after" when english && ParseDate(value, english: true) is { } since: terms = terms with { Since = since }; continue;
                case "before" when english && ParseDate(value, english: true) is { } until: terms = terms with { Until = until }; continue;
                case "has" when english && (value.Equals("attachment", StringComparison.OrdinalIgnoreCase) || value.Equals("attachments", StringComparison.OrdinalIgnoreCase)):
                    terms = terms with { HasAttachments = true };
                    continue;
                case "is" when english && value.Equals("unread", StringComparison.OrdinalIgnoreCase): terms = terms with { Unread = true }; continue;
                case "is" when english && value.Equals("flagged", StringComparison.OrdinalIgnoreCase): terms = terms with { Flagged = true }; continue;
            }
            free.Add(token);
        }
        return terms with { Text = string.Join(' ', free) };
    }

    /// <summary>Le parole libere con questi filtri (quelli di prima sostituiti).</summary>
    public SearchTerms WithText(string text) => this with { Text = text.Trim() };

    /// <summary>
    /// I filtri in parole, per le pastiglie e il testo della ricerca: ("da", "TIM"), ("ha", "allegati")... Le parole chiave
    /// sono quelle della lingua di Missiva (in inglese from:, after: con la data come 2026-03-01, has:attachment...), e le
    /// etichette delle pastiglie sono tradotte, con le date nella cultura di Lingua.
    /// </summary>
    public IEnumerable<(string Key, string Label, string Token)> Parts()
    {
        bool english = Lingua.English;
        if (From.Length > 0) yield return ("From", T("Da: {0}", From), (english ? "from:" : "da:") + Quote(From));
        if (To.Length > 0) yield return ("To", T("A: {0}", To), (english ? "to:" : "a:") + Quote(To));
        if (Subject.Length > 0) yield return ("Subject", T("Oggetto: {0}", Subject), (english ? "subject:" : "oggetto:") + Quote(Subject));
        if (Since is { } s) yield return ("Since", T("Dal {0}", Shown(s)), english ? "after:" + Written(s) : "dal:" + s.ToString("d/M/yyyy", CultureInfo.InvariantCulture));
        if (Until is { } u) yield return ("Until", T("Al {0}", Shown(u)), english ? "before:" + Written(u) : "al:" + u.ToString("d/M/yyyy", CultureInfo.InvariantCulture));
        if (HasAttachments) yield return ("HasAttachments", T("Con allegati"), english ? "has:attachment" : "ha:allegati");
        if (Unread) yield return ("Unread", T("Non letti"), english ? "is:unread" : "è:nonletto");
        if (Flagged) yield return ("Flagged", T("Contrassegnati"), english ? "is:flagged" : "è:contrassegnato");
    }

    /// <summary>Una data nella pastiglia: "3 mar 2026" in italiano, "3 Mar 2026" o "Mar 3, 2026" in inglese (secondo la cultura).</summary>
    static string Shown(DateTime day) =>
        day.ToString(Lingua.English && MonthFirst(Lingua.Culture) ? "MMM d, yyyy" : "d MMM yyyy", Lingua.Culture);

    /// <summary>Una data dopo after: e before:, 2026-03-01: si rilegge uguale con qualunque cultura inglese.</summary>
    static string Written(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Un filtro tolto (la x della pastiglia).</summary>
    public SearchTerms Without(string key) => key switch
    {
        "From" => this with { From = "" },
        "To" => this with { To = "" },
        "Subject" => this with { Subject = "" },
        "Since" => this with { Since = null },
        "Until" => this with { Until = null },
        "HasAttachments" => this with { HasAttachments = false },
        "Unread" => this with { Unread = false },
        "Flagged" => this with { Flagged = false },
        _ => this,
    };

    /// <summary>Il testo della ricerca: le parole libere e poi i filtri con le parole chiave.</summary>
    public override string ToString() => string.Join(' ', new[] { Text.Trim() }.Concat(Parts().Select(p => p.Token)).Where(t => t.Length > 0));

    // ---------------------------------------------------------------- traduzioni

    /// <summary>La mezzanotte di un giorno dell'ora di Windows in secondi Unix (anche il 25 ottobre, con il cambio d'ora).</summary>
    static long Epoch(DateTime day) => new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified), TimeZoneInfo.Local)).ToUnixTimeSeconds();

    /// <summary>
    /// Per Gmail (X-GM-RAW, la sintassi della ricerca di Gmail): from:(...), to:(...), subject:(...), has:attachment,
    /// is:unread, is:starred e le date in secondi Unix dalla mezzanotte dell'ora di Windows: after:/before: con le date
    /// scritte Gmail le legge nel fuso del Pacifico.
    /// </summary>
    public string ToGmail()
    {
        var parts = new List<string>();
        if (Text.Trim().Length > 0) parts.Add(Text.Trim());
        if (Plain(From) is { Length: > 0 } from) parts.Add($"from:({from})");
        if (Plain(To) is { Length: > 0 } to) parts.Add($"to:({to})");
        if (Plain(Subject) is { Length: > 0 } subject) parts.Add($"subject:({subject})");
        if (HasAttachments) parts.Add("has:attachment");
        if (Unread) parts.Add("is:unread");
        if (Flagged) parts.Add("is:starred");
        if (Since is { } s) parts.Add($"after:{Epoch(s)}");
        if (Until is { } u) parts.Add($"before:{Epoch(u.AddDays(1))}");
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Per Graph, il testo di $search (KQL): parole, from:, to:, subject: (una parola per volta: niente virgolette dentro
    /// quelle di $search), hasattachments:true e le date allargate di un giorno per parte (KQL le legge in UTC; il giorno
    /// esatto lo decide Matches sulla pagina). Vuoto se non c'è niente da cercare così: allora Filter.
    /// </summary>
    public string ToKql()
    {
        var parts = new List<string>();
        var text = Text.Replace("\"", "").Trim();
        if (text.Length > 0) parts.Add(text);
        static IEnumerable<string> Each(string prefix, string value) => value.Replace("\"", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => prefix + w);
        parts.AddRange(Each("from:", From));
        parts.AddRange(Each("to:", To));
        parts.AddRange(Each("subject:", Subject));
        if (HasAttachments) parts.Add("hasattachments:true");
        if (parts.Count == 0) return "";
        if (Since is { } s) parts.Add($"received>={s.AddDays(-1):yyyy-MM-dd}");
        if (Until is { } u) parts.Add($"received<={u.AddDays(1):yyyy-MM-dd}");
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Per Graph senza parole da cercare (solo date, non letti, contrassegnati): $filter con le date esatte in UTC dalla
    /// mezzanotte dell'ora di Windows; receivedDateTime per primo, come vuole Graph con $orderby.
    /// </summary>
    public string Filter()
    {
        static string Utc(DateTime day) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified), TimeZoneInfo.Local).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var conditions = new List<string> { Since is { } s ? $"receivedDateTime ge {Utc(s)}" : "receivedDateTime ge 1900-01-01T00:00:00Z" };
        if (Until is { } u) conditions.Add($"receivedDateTime lt {Utc(u.AddDays(1))}");
        if (Unread) conditions.Add("isRead eq false");
        if (Flagged) conditions.Add("flag/flagStatus eq 'flagged'");
        if (HasAttachments) conditions.Add("hasAttachments eq true");
        return string.Join(" and ", conditions);
    }

    /// <summary>
    /// Quello che $search di Graph non sa fare insieme alle parole, controllato sulla pagina: il giorno esatto, i non letti e
    /// i contrassegnati ($search non si combina con $filter).
    /// </summary>
    public bool Matches(MessageSummary m) =>
        (Since is not { } s || m.Date >= s.Date) && (Until is not { } u || m.Date < u.Date.AddDays(1)) && (!Unread || !m.IsRead) && (!Flagged || m.IsFlagged);
}
