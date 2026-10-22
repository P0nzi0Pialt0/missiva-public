using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Missiva;

/// <summary>Come si risponde a un invito: accetta, provvisorio, rifiuta, oppure proponi un nuovo orario.</summary>
public enum InviteAnswer { Accept, Tentative, Decline, Propose }

/// <param name="Send">Se la risposta va all'organizzatore (come in Outlook si può rispondere senza mandarla).</param>
/// <param name="NewStart">Per Proponi un nuovo orario: inizio e fine proposti, nell'ora di Windows.</param>
public sealed record InviteResponse(InviteAnswer Answer, string Comment, bool Send, DateTime? NewStart = null, DateTime? NewEnd = null);

/// <summary>
/// Un invito a una riunione (iCalendar, RFC 5545), letto come testo e con limiti di dimensione: METHOD:REQUEST o CANCEL,
/// titolo, orari nel fuso di Windows, luogo, organizzatore e partecipanti, ricorrenza in parole e link della riunione
/// online (Teams, Zoom, Meet, Webex). Per Microsoft Graph, un messaggio di tipo eventMessage senza .ics (GraphMessageId).
/// Le risposte via posta sono iMIP (RFC 6047): un messaggio all'organizzatore con METHOD:REPLY (o COUNTER per un nuovo
/// orario) e il PARTSTAT scelto.
/// </summary>
public sealed class Invitation
{
    public string Method { get; init; } = "REQUEST";
    public string Uid { get; init; } = "";
    public int Sequence { get; init; }
    public string Summary { get; init; } = "";
    public string? Location { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool AllDay { get; init; }
    public Address? Organizer { get; init; }
    public List<Address> Attendees { get; init; } = new();
    public string? Recurrence { get; init; }
    public string? OnlineUrl { get; init; }
    /// <summary>Le righe originali di DTSTART, DTEND e RECURRENCE-ID, ripetute nella risposta.</summary>
    public List<string> TimeLines { get; init; } = new();
    /// <summary>Graph: l'invito è un eventMessage (si risponde con accept, tentativelyAccept, decline sull'evento).</summary>
    public string? GraphMessageId { get; init; }
    /// <summary>Graph: manca il consenso al calendario (Calendars.ReadWrite): la scheda offre "Consenti l'accesso al calendario".</summary>
    public bool NeedsConsent { get; set; }

    public bool Cancelled => Method == "CANCEL";

    /// <summary>
    /// Solo una richiesta (METHOD:REQUEST scritto nell'invito) aspetta Accetta, Provvisorio, Rifiuta o Proponi. Le risposte
    /// (REPLY), le proposte di un nuovo orario (COUNTER) e gli eventi pubblicati (PUBLISH, o senza METHOD: un biglietto,
    /// un webinar) si mostrano senza pulsanti: Accetta risponderebbe a sé stessi o manderebbe nome e nota a chi pubblica.
    /// </summary>
    public bool Answerable => Method == "REQUEST";

    /// <summary>REPLY e COUNTER: chi risponde o propone (il primo ATTENDEE) e, per REPLY, come (PARTSTAT).</summary>
    public Address? Responder { get; init; }
    public string? ResponseStatus { get; init; }

    /// <summary>Un .ics più grande non si legge: gli inviti veri sono di pochi KB.</summary>
    public const int MaxLength = 256 * 1024;
    /// <summary>Oltre queste righe (riunite) l'invito non si legge: quelli veri ne hanno qualche decina, con i fusi qualche centinaio.</summary>
    const int MaxLines = 5000;
    /// <summary>Nome e parametri di una riga (quello che sta prima dei due punti) più lunghi di così: la riga si ignora.</summary>
    const int MaxHead = 1024;

    // ---------------------------------------------------------------- lettura

    /// <summary>
    /// Le righe di iCalendar, con quelle spezzate (che continuano con uno spazio o una tabulazione) riunite. La riga in corso
    /// cresce in uno StringBuilder: con "+=" una riga spezzata in migliaia di pezzi si ricopiava a ogni pezzo.
    /// </summary>
    static List<string> Unfold(string text)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if ((raw.StartsWith(' ') || raw.StartsWith('\t')) && current.Length > 0) current.Append(raw, 1, raw.Length - 1);
            else if (raw.Length > 0)
            {
                if (current.Length > 0) lines.Add(current.ToString());
                current.Clear().Append(raw);
            }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    /// <summary>NOME;PARAMETRO=valore;...:VALORE, con i parametri fra virgolette che possono contenere : e ;.</summary>
    sealed record Property(string Name, Dictionary<string, string> Parameters, string Value, string Line);

    /// <summary>
    /// Una sola passata sui caratteri: i due punti e i punti e virgola fuori dalle virgolette. Prima i parametri si
    /// dividevano con una regex che per ogni ";" riscorreva il resto della riga (quadratica: un .ics costruito apposta
    /// bloccava Missiva per minuti). null per una riga con nome e parametri oltre MaxHead.
    /// </summary>
    static Property? ParseLine(string line)
    {
        int colon = -1;
        bool quoted = false;
        var cuts = new List<int>();
        for (int k = 0; k < line.Length && k <= MaxHead; k++)
        {
            if (line[k] == '"') quoted = !quoted;
            else if (quoted) continue;
            else if (line[k] == ':') { colon = k; break; }
            else if (line[k] == ';') cuts.Add(k);
        }
        if (colon < 0 ? line.Length > MaxHead : colon > MaxHead) return null;
        var head = colon < 0 ? line : line[..colon];
        var value = colon < 0 ? "" : line[(colon + 1)..];
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int c = 0; c < cuts.Count; c++)
        {
            var p = head[(cuts[c] + 1)..(c + 1 < cuts.Count ? cuts[c + 1] : head.Length)];
            int eq = p.IndexOf('=');
            if (eq > 0) parameters[p[..eq]] = p[(eq + 1)..].Trim('"');
        }
        return new Property((cuts.Count > 0 ? head[..cuts[0]] : head).ToUpperInvariant(), parameters, value, line);
    }

    /// <summary>I testi di iCalendar: \n è un a capo, \, \; e \\ i caratteri.</summary>
    static string Text(string value) =>
        Regex.Replace(value, @"\\([nN,;\\])", m => m.Groups[1].Value is "n" or "N" ? "\n" : m.Groups[1].Value);

    /// <summary>
    /// L'invito letto da un'altra parte che il thread dell'interfaccia: i provider continuano su quel thread, e un .ics
    /// grande non deve fermare la finestra (come ReadingPage.Warm per la pulizia dell'HTML).
    /// </summary>
    public static Task<Invitation?> ParseAsync(string? ics) => Task.Run(() => Parse(ics));

    /// <summary>
    /// METHOD come dice l'invito; senza, un evento pubblicato (PUBLISH, RFC 5546): un .ics senza METHOD non chiede risposte.
    /// </summary>
    public static Invitation? Parse(string? ics)
    {
        if (string.IsNullOrEmpty(ics) || ics.Length > MaxLength || !ics.Contains("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var unfolded = Unfold(ics);
            if (unfolded.Count > MaxLines)
            {
                Log.Write($"invito non letto: {unfolded.Count} righe");
                return null;
            }
            // Ogni riga si legge una volta sola, per i fusi e per l'evento.
            var lines = unfolded.Select(ParseLine).OfType<Property>().ToList();
            string method = "PUBLISH";
            var timeZones = VTimeZones(lines);
            var props = new List<Property>();
            bool inEvent = false, done = false;
            int nested = 0;
            foreach (var p in lines)
            {
                if (p.Name == "METHOD" && !inEvent) method = p.Value.Trim().ToUpperInvariant();
                else if (p.Name == "BEGIN" && p.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase) && !done) inEvent = true;
                else if (inEvent && p.Name == "BEGIN") nested++; // VALARM dentro l'evento
                else if (inEvent && p.Name == "END" && nested > 0) nested--;
                else if (inEvent && p.Name == "END" && p.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) { inEvent = false; done = true; }
                else if (inEvent && nested == 0) props.Add(p);
            }
            if (props.Count == 0) return null;
            Property? Get(string name) => props.FirstOrDefault(p => p.Name == name);
            var start = Get("DTSTART");
            if (start == null) return null;
            var (from, allDay) = Time(start, timeZones);
            var endProp = Get("DTEND");
            DateTime to;
            if (endProp != null) to = Time(endProp, timeZones).Value;
            else if (Get("DURATION") is { } duration && Duration(duration.Value) is { } span) to = from + span;
            else to = allDay ? from.AddDays(1) : from;
            var organizer = Get("ORGANIZER") is { } o ? Person(o) : null;
            var attendees = props.Where(p => p.Name == "ATTENDEE").Select(Person).Where(a => a.Email.Length > 0).ToList();
            var description = Get("DESCRIPTION") is { } d ? Text(d.Value) : "";
            var location = Get("LOCATION") is { } l ? Text(l.Value).Trim() : null;
            // Una risposta o una proposta: chi l'ha mandata è il partecipante (di solito l'unico ATTENDEE).
            var responder = method is "REPLY" or "COUNTER" ? props.FirstOrDefault(p => p.Name == "ATTENDEE" && p.Value.Trim().Length > 0) : null;
            return new Invitation
            {
                Method = method,
                Responder = responder != null ? Person(responder) : null,
                ResponseStatus = responder?.Parameters.GetValueOrDefault("PARTSTAT")?.Trim().ToUpperInvariant(),
                Uid = Get("UID")?.Value.Trim() ?? "",
                Sequence = int.TryParse(Get("SEQUENCE")?.Value, out var seq) ? seq : 0,
                Summary = Get("SUMMARY") is { } s ? Text(s.Value).Trim() : "",
                Location = string.IsNullOrWhiteSpace(location) ? null : location,
                Start = from,
                End = to,
                AllDay = allDay,
                Organizer = organizer,
                Attendees = attendees,
                Recurrence = Get("RRULE") is { } rule ? RecurrenceText(rule.Value, from) : null,
                OnlineUrl = OnlineLink(props, description, location),
                TimeLines = props.Where(p => p.Name is "DTSTART" or "DTEND" or "RECURRENCE-ID").Select(p => p.Line).ToList(),
            };
        }
        catch (Exception ex)
        {
            Log.Write("invito non letto: " + ex.Message);
            return null;
        }
    }

    static Address Person(Property p)
    {
        var email = Regex.Replace(p.Value.Trim(), "^mailto:", "", RegexOptions.IgnoreCase);
        return new Address { Name = p.Parameters.GetValueOrDefault("CN") ?? "", Email = email };
    }

    /// <summary>
    /// I fusi definiti nell'invito (VTIMEZONE): quando il TZID non è un nome che Windows conosce ("Customized Time Zone"),
    /// si usa lo scarto dell'ora solare (STANDARD) e di quella legale (DAYLIGHT) con le loro regole.
    /// </summary>
    static Dictionary<string, TimeZoneInfo> VTimeZones(List<Property> lines)
    {
        var result = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);
        string? id = null;
        TimeSpan? standard = null, daylight = null;
        var block = "";
        foreach (var p in lines)
        {
            if (p.Name == "BEGIN" && p.Value.Equals("VTIMEZONE", StringComparison.OrdinalIgnoreCase)) (id, standard, daylight) = (null, null, null);
            else if (p.Name == "TZID" && id == null) id = p.Value.Trim();
            else if (p.Name == "TZOFFSETTO" && Offset(p.Value) is { } offset)
            {
                // L'ultimo BEGIN visto dice se è STANDARD o DAYLIGHT: basta ricordare il primo di ognuno.
                if (block == "DAYLIGHT") daylight ??= offset;
                else standard ??= offset;
            }
            if (p.Name == "BEGIN") block = p.Value.Trim().ToUpperInvariant();
            if (p.Name == "END" && p.Value.Equals("VTIMEZONE", StringComparison.OrdinalIgnoreCase) && id != null && standard != null)
            {
                // Senza le regole esatte: lo scarto dell'ora solare, e dell'ora legale fra l'ultima domenica di marzo e di ottobre
                // (le regole europee, quelle degli inviti che arrivano a Missiva). Solo per i fusi che Windows non conosce.
                var rules = daylight is { } dst && dst != standard
                    ? new[] { TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, dst - standard.Value,
                        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)) }
                    : [];
                result[id] = TimeZoneInfo.CreateCustomTimeZone(id, standard.Value, id, id, id, rules);
            }
        }
        return result;
    }

    static TimeSpan? Offset(string value)
    {
        var m = Regex.Match(value.Trim(), @"^([+-])(\d{2})(\d{2})");
        if (!m.Success) return null;
        var span = new TimeSpan(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), 0);
        return m.Groups[1].Value == "-" ? -span : span;
    }

    /// <summary>Un orario di iCalendar nel fuso di Windows: UTC (Z), con TZID, di un giorno intero (VALUE=DATE) o "fluttuante".</summary>
    static (DateTime Value, bool AllDay) Time(Property p, Dictionary<string, TimeZoneInfo> zones)
    {
        var value = p.Value.Trim();
        if (value.Length == 8 || p.Parameters.GetValueOrDefault("VALUE")?.Equals("DATE", StringComparison.OrdinalIgnoreCase) == true)
            return (DateTime.ParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture), true);
        bool utc = value.EndsWith('Z');
        var time = DateTime.ParseExact(value.TrimEnd('Z')[..15], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        if (utc) return (DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime(), false);
        if (p.Parameters.GetValueOrDefault("TZID") is { Length: > 0 } tzid && Zone(tzid, zones) is { } zone)
            return (Calendars.FromZone(time, zone), false);
        return (time, false); // senza fuso: l'ora vale ovunque (RFC 5545, "floating")
    }

    /// <summary>Il fuso: un nome di Windows (W. Europe Standard Time) o IANA (Europe/Rome), o uno definito nell'invito.</summary>
    static TimeZoneInfo? Zone(string tzid, Dictionary<string, TimeZoneInfo> zones)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tzid); }
        catch (Exception) { }
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(tzid, out var windows))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(windows); }
            catch (Exception) { }
        }
        return zones.GetValueOrDefault(tzid);
    }

    static TimeSpan? Duration(string value)
    {
        if (value.Length > 64) return null;
        var m = Regex.Match(value.Trim(), @"^P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$");
        if (!m.Success) return null;
        // Number e non N: N è Lingua.N (global using static), e una funzione locale con quel nome la nasconderebbe qui.
        int Number(int g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value) : 0;
        return new TimeSpan(Number(1) * 7 + Number(2), Number(3), Number(4), Number(5));
    }

    static readonly string[] Days = ["domenica", "lunedì", "martedì", "mercoledì", "giovedì", "venerdì", "sabato"];
    static readonly Dictionary<string, int> DayCodes = new() { ["SU"] = 0, ["MO"] = 1, ["TU"] = 2, ["WE"] = 3, ["TH"] = 4, ["FR"] = 5, ["SA"] = 6 };

    /// <summary>
    /// La ricorrenza in parole: "Ogni settimana il lunedì e il mercoledì, fino al 31/12/2026". In inglese una frase inglese
    /// (RecurrenceEnglish: "Every 2 weeks on Monday and Wednesday, until 31/12/2026"); l'italiano, con articoli e accordi,
    /// resta il ramo di sempre.
    /// </summary>
    public static string RecurrenceText(string rule, DateTime start)
    {
        var parts = rule.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1], StringComparer.OrdinalIgnoreCase);
        int interval = int.TryParse(parts.GetValueOrDefault("INTERVAL"), out var n) && n > 1 ? n : 1;
        var days = (parts.GetValueOrDefault("BYDAY") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Regex.Match(d, @"^([+-]?\d*)([A-Z]{2})$")).Where(m => m.Success && DayCodes.ContainsKey(m.Groups[2].Value)).ToList();
        if (Lingua.English) return RecurrenceEnglish(parts, interval, days, start);
        static string Join(IEnumerable<string> items)
        {
            var list = items.ToList();
            return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " e " + list[^1];
        }
        string DayList() => Join(days.Select(m => "il " + Days[DayCodes[m.Groups[2].Value]]).Select(s => s.Replace("il domenica", "la domenica")));
        var text = parts.GetValueOrDefault("FREQ")?.ToUpperInvariant() switch
        {
            "DAILY" => interval == 1 ? "Ogni giorno" : $"Ogni {interval} giorni",
            "WEEKLY" => (interval == 1 ? "Ogni settimana" : $"Ogni {interval} settimane") + " " +
                        (days.Count > 0 ? DayList() : (start.DayOfWeek == DayOfWeek.Sunday ? "la " : "il ") + Days[(int)start.DayOfWeek]),
            "MONTHLY" => (interval == 1 ? "Ogni mese" : $"Ogni {interval} mesi") +
                         (days.Count == 1 && days[0].Groups[1].Value is { Length: > 0 } ord && int.TryParse(ord, out var o)
                             ? $", {(o == -1 ? "l'ultimo" : $"il {o}°")} {Days[DayCodes[days[0].Groups[2].Value]]}"
                             : $", il giorno {(int.TryParse(parts.GetValueOrDefault("BYMONTHDAY"), out var md) ? md : start.Day)}"),
            "YEARLY" => interval == 1 ? $"Ogni anno il {start.ToString("d MMMM", Lingua.Culture)}" : $"Ogni {interval} anni il {start.ToString("d MMMM", Lingua.Culture)}",
            _ => "Si ripete",
        };
        if (parts.GetValueOrDefault("COUNT") is { } count && int.TryParse(count, out var c)) text += $", {c} volte";
        else if (parts.GetValueOrDefault("UNTIL") is { Length: >= 8 } until &&
                 DateTime.TryParseExact(until[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            text += $", fino al {end:dd/MM/yyyy}";
        return text;
    }

    /// <summary>
    /// La ricorrenza in inglese, come la dice Outlook: "Every day", "Every 2 weeks on Monday and Wednesday", "Every month on
    /// the last Friday", "Every month on day 15", "Every year on 26 September", con ", 5 times" o ", until 31/12/2026" (la
    /// data breve della cultura inglese). Stessi casi del ramo italiano; i giorni e i mesi dalla cultura di Lingua.
    /// </summary>
    static string RecurrenceEnglish(Dictionary<string, string> parts, int interval, List<Match> days, DateTime start)
    {
        var culture = Lingua.Culture;
        string Day(int code) => culture.DateTimeFormat.DayNames[code];
        static string Join(List<string> list) =>
            list.Count <= 1 ? string.Concat(list) : list.Count == 2 ? $"{list[0]} and {list[1]}" : string.Join(", ", list.Take(list.Count - 1)) + ", and " + list[^1];
        static string Ordinal(int k) => k % 100 is 11 or 12 or 13 ? $"{k}th" : (k % 10) switch { 1 => $"{k}st", 2 => $"{k}nd", 3 => $"{k}rd", _ => $"{k}th" };
        string Every(string one, string many) => interval == 1 ? "Every " + one : $"Every {interval} {many}";
        var text = parts.GetValueOrDefault("FREQ")?.ToUpperInvariant() switch
        {
            "DAILY" => Every("day", "days"),
            "WEEKLY" => Every("week", "weeks") + " on " +
                        (days.Count > 0 ? Join(days.Select(m => Day(DayCodes[m.Groups[2].Value])).ToList()) : Day((int)start.DayOfWeek)),
            "MONTHLY" => Every("month", "months") +
                         (days.Count == 1 && days[0].Groups[1].Value is { Length: > 0 } ord && int.TryParse(ord, out var o)
                             ? $" on the {(o == -1 ? "last" : o < 0 ? Ordinal(-o) + " to last" : Ordinal(o))} {Day(DayCodes[days[0].Groups[2].Value])}"
                             : int.TryParse(parts.GetValueOrDefault("BYMONTHDAY"), out var md) && md == -1 ? " on the last day"
                             : $" on day {(int.TryParse(parts.GetValueOrDefault("BYMONTHDAY"), out var day) ? day : start.Day)}"),
            "YEARLY" => Every("year", "years") + " on " + start.ToString("M", culture),
            _ => "Repeats",
        };
        if (parts.GetValueOrDefault("COUNT") is { } count && int.TryParse(count, out var c)) text += c == 1 ? ", once" : $", {c} times";
        else if (parts.GetValueOrDefault("UNTIL") is { Length: >= 8 } until &&
                 DateTime.TryParseExact(until[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            text += ", until " + end.ToString("d", culture);
        return text;
    }

    static readonly string[] MeetingHosts = ["teams.microsoft.com", "teams.live.com", "zoom.us", "meet.google.com", "webex.com", "gotomeeting.com", "whereby.com"];

    /// <summary>Il link della riunione online: le proprietà di Teams e Google, poi il primo indirizzo di un servizio noto.</summary>
    static string? OnlineLink(List<Property> props, string description, string? location)
    {
        foreach (var name in new[] { "X-MICROSOFT-SKYPETEAMSMEETINGURL", "X-GOOGLE-CONFERENCE", "URL" })
            if (props.FirstOrDefault(p => p.Name == name)?.Value.Trim() is { } url && Safe(url) is { } safe && IsMeetingSite(safe))
                return safe;
        foreach (Match m in Regex.Matches(description + " " + location, @"https://[^\s<>""]+"))
            if (Safe(m.Value.TrimEnd('.', ',', ')', ';')) is { } url && IsMeetingSite(url))
                return url;
        return null;
    }

    static string? Safe(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri.AbsoluteUri : null;

    static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.IdnHost.ToLowerInvariant() : "";

    /// <summary>
    /// L'indirizzo è di un servizio di riunioni noto: il suo nome, o un nome che ci sta sotto, con il punto (come
    /// CalendarView.OpenMeeting). Prima bastava che il nome finisse allo stesso modo, e un sito di un altro con il nome del
    /// servizio in coda diventava "Partecipa alla riunione online" nella scheda dell'invito (audit dell'11 ottobre, T-33).
    /// </summary>
    internal static bool IsMeetingSite(string url) =>
        HostOf(url) is { Length: > 0 } host && MeetingHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));

    // ---------------------------------------------------------------- testi

    /// <summary>"giovedì 1 ottobre 2026, 9:00 - 10:00", o per più giorni e per i giorni interi (giorni e mesi nella lingua di Missiva).</summary>
    public string When
    {
        get
        {
            var culture = Lingua.Culture;
            if (AllDay)
            {
                var last = End > Start ? End.AddDays(-1) : Start;
                return last.Date == Start.Date ? T("{0}, tutto il giorno", Start.ToString("dddd d MMMM yyyy", culture))
                    : T("da {0} a {1}", Start.ToString("dddd d MMMM", culture), last.ToString("dddd d MMMM yyyy", culture));
            }
            return End.Date == Start.Date
                ? $"{Start.ToString("dddd d MMMM yyyy", culture)}, {Start:H:mm} - {End:H:mm}"
                : $"{Start.ToString("dddd d MMMM yyyy, H:mm", culture)} - {End.ToString("dddd d MMMM yyyy, H:mm", culture)}";
        }
    }

    /// <summary>La risposta come la dice l'oggetto del messaggio all'organizzatore ("Accettato: titolo", in inglese "Accepted: ...").</summary>
    public static string Verb(InviteAnswer answer) => answer switch
    {
        InviteAnswer.Accept => T("Accettato"),
        InviteAnswer.Tentative => T("Provvisorio"),
        InviteAnswer.Decline => T("Rifiutato"),
        _ => T("Nuovo orario proposto"),
    };

    // ---------------------------------------------------------------- risposte (iMIP)

    static string Escape(string text) => text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    static string Utc(DateTime local) => local.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Le righe lunghe si spezzano a 75 caratteri, come vuole iCalendar.</summary>
    static string Fold(string line)
    {
        if (line.Length <= 75) return line;
        var sb = new StringBuilder(line[..75]);
        for (int k = 75; k < line.Length; k += 74) sb.Append("\r\n ").Append(line[k..Math.Min(line.Length, k + 74)]);
        return sb.ToString();
    }

    /// <summary>
    /// La risposta iMIP all'organizzatore: METHOD:REPLY con il PARTSTAT (ACCEPTED, TENTATIVE, DECLINED), o METHOD:COUNTER con
    /// il nuovo orario. Stessi UID, SEQUENCE e orari dell'invito.
    /// </summary>
    public string Reply(Address me, InviteResponse response)
    {
        var partStat = response.Answer switch
        {
            InviteAnswer.Accept => "ACCEPTED",
            InviteAnswer.Decline => "DECLINED",
            _ => "TENTATIVE",
        };
        bool counter = response.Answer == InviteAnswer.Propose && response.NewStart != null && response.NewEnd != null;
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR", "PRODID:-//Missiva//Missiva//IT", "VERSION:2.0", "METHOD:" + (counter ? "COUNTER" : "REPLY"), "BEGIN:VEVENT",
            "UID:" + Uid, "SEQUENCE:" + Sequence, "DTSTAMP:" + Utc(DateTime.Now),
        };
        if (counter)
        {
            lines.Add("DTSTART:" + Utc(response.NewStart!.Value));
            lines.Add("DTEND:" + Utc(response.NewEnd!.Value));
            lines.AddRange(TimeLines.Where(l => l.StartsWith("RECURRENCE-ID", StringComparison.OrdinalIgnoreCase)));
        }
        else lines.AddRange(TimeLines);
        if (Organizer != null) lines.Add($"ORGANIZER{(Organizer.Name.Length > 0 ? $";CN=\"{Organizer.Name.Replace("\"", "'")}\"" : "")}:mailto:{Organizer.Email}");
        lines.Add($"ATTENDEE;PARTSTAT={partStat}{(me.Name.Length > 0 ? $";CN=\"{me.Name.Replace("\"", "'")}\"" : "")}:mailto:{me.Email}");
        if (Summary.Length > 0) lines.Add("SUMMARY:" + Escape(Summary));
        if (response.Comment.Trim().Length > 0) lines.Add("COMMENT:" + Escape(response.Comment.Trim()));
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }

    /// <summary>
    /// Il corpo MIME di una risposta iMIP: multipart/alternative con il testo, l'HTML e la parte text/calendar con il suo
    /// metodo (i programmi di calendario leggono quella).
    /// </summary>
    public static MimeKit.MimeEntity MimeBody(OutgoingMessage mail)
    {
        var calendar = new MimeKit.TextPart("calendar") { Text = mail.Calendar ?? "" };
        calendar.ContentType.Parameters.Add("method", mail.CalendarMethod);
        calendar.ContentType.Charset = "utf-8";
        calendar.ContentTransferEncoding = MimeKit.ContentEncoding.QuotedPrintable;
        return new MimeKit.MultipartAlternative
        {
            new MimeKit.TextPart("plain") { Text = mail.Text },
            new MimeKit.TextPart("html") { Text = mail.Html },
            calendar,
        };
    }

    /// <summary>Il testo di una parte text/calendar o di un .ics allegato, se non è troppo grande.</summary>
    static bool IsCalendar(MimeKit.ContentType type, string? name) =>
        type.IsMimeType("text", "calendar") || type.IsMimeType("application", "ics") || name?.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Il messaggio (dalla struttura che Gmail e IMAP mandano con l'elenco) può avere un invito: la copia su disco non lo
    /// tiene, quindi il corpo si chiede al server (BodyCache.LoadOnline).
    /// </summary>
    internal static bool MayHave(MailKit.IMessageSummary summary) => summary.BodyParts.Any(p => IsCalendar(p.ContentType, p.FileName));

    /// <summary>Lo stesso per un allegato già letto (il tipo com'è scritto e il nome).</summary>
    internal static bool IsCalendar(string contentType, string name) =>
        MimeKit.ContentType.TryParse(contentType, out var type) ? IsCalendar(type, name) : name.EndsWith(".ics", StringComparison.OrdinalIgnoreCase);

    /// <summary>L'invito di un messaggio intero (i messaggi piccoli, gli .eml).</summary>
    public static Invitation? FromMime(MimeKit.MimeMessage message)
    {
        var part = message.BodyParts.OfType<MimeKit.MimePart>().FirstOrDefault(p => IsCalendar(p.ContentType, p.FileName));
        if (part?.Content == null) return null;
        using var ms = new MemoryStream();
        part.Content.DecodeTo(ms);
        if (ms.Length > MaxLength) return null;
        var charset = part.ContentType.Charset is { Length: > 0 } c ? c : "utf-8";
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(charset); }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        return Parse(encoding.GetString(ms.ToArray()));
    }

    /// <summary>
    /// L'invito di un messaggio IMAP: dal messaggio intero se c'è, altrimenti la sola parte text/calendar (o l'.ics), sotto
    /// il limite di dimensione, con BODY.PEEK. Letto fuori dal thread dell'interfaccia (anche per i corpi in anticipo).
    /// </summary>
    internal static async Task<Invitation?> FindAsync(MailKit.IMailFolder folder, MailKit.UniqueId uid, MailKit.IMessageSummary summary, MimeKit.MimeMessage? whole)
    {
        if (whole != null) return await Task.Run(() => FromMime(whole));
        var part = summary.BodyParts.FirstOrDefault(p => IsCalendar(p.ContentType, p.FileName) && p.Octets < MaxLength);
        if (part == null) return null;
        switch (await folder.GetBodyPartAsync(uid, part))
        {
            case MimeKit.TextPart text:
                return await ParseAsync(text.Text);
            case MimeKit.MimePart { Content: { } content }:
                using (var ms = new MemoryStream())
                {
                    await content.DecodeToAsync(ms);
                    return await ParseAsync(Encoding.UTF8.GetString(ms.ToArray()));
                }
            default:
                return null;
        }
    }

    /// <summary>Il link della riunione online in un testo qualunque (il corpo degli inviti di Exchange).</summary>
    public static string? FindMeetingLink(string text)
    {
        foreach (Match m in Regex.Matches(text, @"https://[^\s<>""']+"))
            if (Safe(System.Net.WebUtility.HtmlDecode(m.Value).TrimEnd('.', ',', ')', ';')) is { } url && IsMeetingSite(url))
                return url;
        return null;
    }

    /// <summary>
    /// Il messaggio iMIP all'organizzatore (oggetto "Accettato: ..." come Outlook), con la parte text/calendar. Oggetto e
    /// testo nella lingua di Missiva (una frase intera per risposta); la parte text/calendar è sempre la stessa.
    /// </summary>
    public OutgoingMessage ReplyMessage(MailAccount account, InviteResponse response)
    {
        if (!Answerable) throw new MailException(T("Non è una richiesta di partecipazione: non si risponde."));
        var me = new Address { Name = account.DisplayName, Email = account.Email };
        var who = account.DisplayName.Length > 0 ? account.DisplayName : account.Email;
        var said = response.Answer switch
        {
            InviteAnswer.Accept => T("{0} ha accettato l'invito \"{1}\".", who, Summary),
            InviteAnswer.Tentative => T("{0} ha accettato in modo provvisorio l'invito \"{1}\".", who, Summary),
            InviteAnswer.Decline => T("{0} ha rifiutato l'invito \"{1}\".", who, Summary),
            _ => response.NewStart is { } s && response.NewEnd is { } e
                ? T("{0} propone un nuovo orario per l'invito \"{1}\": {2} - {3}.", who, Summary, s.ToString("dddd d MMMM yyyy, H:mm", Lingua.Culture), e.ToString("H:mm"))
                : T("{0} propone un nuovo orario per l'invito \"{1}\".", who, Summary),
        };
        var text = said + (response.Comment.Trim().Length > 0 ? "\n\n" + response.Comment.Trim() : "");
        var method = response.Answer == InviteAnswer.Propose ? "COUNTER" : "REPLY";
        return new OutgoingMessage
        {
            To = Organizer is { } o ? [o] : [],
            Subject = $"{Verb(response.Answer)}: {Summary}",
            Text = text,
            Html = "<p>" + System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>") + "</p>",
            Calendar = Reply(me, response),
            CalendarMethod = method,
        };
    }
}
