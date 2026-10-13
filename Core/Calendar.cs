using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Missiva;

/// <summary>Un calendario di un account (Graph me/calendars), con il suo colore (#RRGGBB); CanEdit falso per quelli di sola lettura (Compleanni, festività).</summary>
public sealed record CalendarInfo(string Id, string Name, string Color, bool IsDefault, bool CanEdit = true);

/// <summary>Un partecipante di un evento, con la sua risposta (accepted, tentativelyAccepted, declined, none...).</summary>
public sealed record Attendee(Address Person, string Response, bool Optional);

/// <summary>
/// Un evento del calendario, con gli orari già nell'ora di Windows (Graph li dà nel fuso chiesto con Prefer
/// outlook.timezone; quelli in un altro fuso si convertono). Solo testo: la descrizione HTML dell'evento non si mostra
/// fuori dal riquadro confinato, al suo posto l'anteprima in testo (bodyPreview).
/// </summary>
public sealed class CalendarEvent
{
    public required string Id { get; init; }
    public string CalendarId { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Location { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool AllDay { get; init; }
    /// <summary>free, tentative, busy, oof, workingElsewhere.</summary>
    public string ShowAs { get; init; } = "busy";
    public Address? Organizer { get; init; }
    /// <summary>La mia risposta: organizer, accepted, tentativelyAccepted, declined, notResponded, none.</summary>
    public string Response { get; init; } = "";
    /// <summary>Il link della riunione online (Teams), solo https.</summary>
    public string? JoinUrl { get; init; }
    /// <summary>Un'occorrenza (o un'eccezione) di un evento ricorrente.</summary>
    public bool IsRecurring { get; init; }
    /// <summary>La serie di un'occorrenza (Graph seriesMasterId): "Tutta la serie" si modifica ed elimina lì.</summary>
    public string SeriesMasterId { get; init; } = "";
    /// <summary>Un evento proprio (Graph isOrganizer): si modifica, si sposta, si elimina o, se è una riunione, si annulla.</summary>
    public bool IsOrganizer { get; init; }
    public bool IsCancelled { get; init; }
    public bool ReminderOn { get; init; }
    public int ReminderMinutes { get; init; }
    public string Preview { get; init; } = "";
    public List<Attendee> Attendees { get; init; } = new();

    /// <summary>Quando mostrare il promemoria, se c'è.</summary>
    public DateTime? ReminderAt => ReminderOn && !IsCancelled ? Start.AddMinutes(-ReminderMinutes) : null;
}

/// <summary>La ripetizione semplice di un evento nuovo, come la finestra del nuovo Outlook.</summary>
public enum EventRepeat { None, Daily, Weekly, Monthly }

/// <summary>
/// Un evento nuovo della finestra Nuovo evento, con gli orari nell'ora di Windows. Con dei partecipanti è una riunione: il
/// server manda gli inviti. La descrizione è l'HTML scritto con l'editor di Missiva (FlowToHtml).
/// </summary>
public sealed class NewEvent
{
    public string CalendarId { get; init; } = "";
    public string Subject { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool AllDay { get; init; }
    public string Location { get; init; } = "";
    /// <summary>Minuti prima dell'inizio; null nessun promemoria.</summary>
    public int? ReminderMinutes { get; init; } = 15;
    /// <summary>free, tentative, busy, oof, workingElsewhere.</summary>
    public string ShowAs { get; init; } = "busy";
    public EventRepeat Repeat { get; init; }
    /// <summary>Ogni settimana: i giorni (almeno uno).</summary>
    public List<DayOfWeek> RepeatDays { get; init; } = new();
    /// <summary>Fino al giorno (compreso); null senza fine.</summary>
    public DateTime? RepeatUntil { get; init; }
    public string Html { get; init; } = "";
    public List<Address> Required { get; init; } = new();
    public List<Address> Optional { get; init; } = new();
    /// <summary>Riunione di Teams (solo gli account di lavoro o della scuola).</summary>
    public bool OnlineMeeting { get; init; }
    public bool IsMeeting => Required.Count + Optional.Count > 0;
}

/// <summary>
/// La disponibilità di una persona (Graph getSchedule, account di lavoro): View ha una cifra per mezz'ora dall'inizio
/// chiesto (0 libero, 1 provvisorio, 2 occupato, 3 fuori sede, 4 lavoro altrove); Error se il server non la dà (persona
/// esterna, calendario non condiviso).
/// </summary>
public sealed record Availability(string Email, string View, string? Error);

/// <summary>I calendari di un account e gli eventi di un intervallo.</summary>
public sealed class CalendarData
{
    public List<CalendarInfo> Calendars { get; init; } = new();
    public List<CalendarEvent> Events { get; init; } = new();
    public DateTime From { get; init; }
    public DateTime To { get; init; }
}

/// <summary>Lettura degli eventi di Graph e disposizione nella giornata (colonne per quelli che si sovrappongono).</summary>
public static class Calendars
{
    /// <summary>
    /// I campi chiesti a calendarView, come consigliato: niente descrizione HTML, che non si mostra, e niente che non serve
    /// alla vista o ai dettagli.
    /// </summary>
    public const string EventFields = "id,subject,start,end,isAllDay,location,showAs,organizer,responseStatus,onlineMeeting,type,seriesMasterId," +
                                      "isCancelled,isReminderOn,reminderMinutesBeforeStart,bodyPreview,attendees,isOrganizer";

    /// <summary>Un istante di Graph ({dateTime, timeZone}) nell'ora di Windows; il fuso di Windows o IANA, UTC se manca.</summary>
    public static DateTime LocalTime(JsonNode? t)
    {
        if (!DateTime.TryParse(t?["dateTime"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return DateTime.MinValue;
        var name = t?["timeZone"]?.GetValue<string>() is { Length: > 0 } tz ? tz : "UTC";
        if (name == TimeZoneInfo.Local.Id) return DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(name); }
        catch (Exception) { zone = TimeZoneInfo.Utc; }
        return FromZone(time, zone);
    }

    /// <summary>
    /// Un'ora dell'orologio di un fuso nell'ora di Windows. Un'ora che in quel fuso non esiste (la notte in cui comincia l'ora
    /// legale: a New York le 02:30 della seconda domenica di marzo) vale con la differenza da UTC di prima del salto, come
    /// dice la RFC 5545: la stessa ora spostata avanti del salto. TimeZoneInfo.ConvertTime la rifiuta con un'eccezione, e
    /// l'invito restava senza scheda o la lettura del calendario falliva (audit dell'11 ottobre, C-59).
    /// </summary>
    public static DateTime FromZone(DateTime time, TimeZoneInfo zone)
    {
        time = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(time))
        {
            var day = time.Date;
            var jump = zone.GetAdjustmentRules().FirstOrDefault(r => r.DateStart <= day && day <= r.DateEnd)?.DaylightDelta ?? TimeSpan.Zero;
            time += jump > TimeSpan.Zero ? jump : TimeSpan.FromHours(1);
            for (int i = 0; i < 4 && zone.IsInvalidTime(time); i++) time = time.AddMinutes(30);
        }
        return TimeZoneInfo.ConvertTime(time, zone, TimeZoneInfo.Local);
    }

    /// <summary>L'inizio di un intervallo per calendarView: l'ora di Windows con la sua differenza da UTC (anche d'estate).</summary>
    public static string Iso(DateTime local) => new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(local))
        .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    static string Str(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    static Address? Person(JsonNode? n) =>
        n?["emailAddress"] is { } e ? new Address { Name = Str(e, "name"), Email = Str(e, "address") } : null;

    /// <summary>Un evento di calendarView.</summary>
    public static CalendarEvent Event(JsonNode e, string calendarId)
    {
        var join = Str(e["onlineMeeting"], "joinUrl");
        var allDay = e["isAllDay"]?.GetValue<bool>() ?? false;
        var start = LocalTime(e["start"]);
        var end = LocalTime(e["end"]);
        if (allDay)
        {
            // I giorni interi valgono come date, da mezzanotte a mezzanotte, in qualunque fuso li scriva Graph.
            start = DateTime.Parse(Str(e["start"], "dateTime")[..10], CultureInfo.InvariantCulture);
            end = DateTime.Parse(Str(e["end"], "dateTime")[..10], CultureInfo.InvariantCulture);
            if (end <= start) end = start.AddDays(1);
        }
        return new CalendarEvent
        {
            Id = Str(e, "id"),
            CalendarId = calendarId,
            Subject = Str(e, "subject"),
            Location = Str(e["location"], "displayName"),
            Start = start,
            End = end < start ? start : end,
            AllDay = allDay,
            ShowAs = Str(e, "showAs") is { Length: > 0 } showAs ? showAs : "busy",
            Organizer = Person(e["organizer"]),
            Response = Str(e["responseStatus"], "response"),
            JoinUrl = Uri.TryCreate(join, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null,
            IsRecurring = Str(e, "type") is "occurrence" or "exception",
            SeriesMasterId = Str(e, "seriesMasterId"),
            IsOrganizer = e["isOrganizer"] is JsonValue own && own.TryGetValue<bool>(out var isOrganizer) ? isOrganizer : Str(e["responseStatus"], "response") == "organizer",
            IsCancelled = e["isCancelled"]?.GetValue<bool>() ?? false,
            ReminderOn = e["isReminderOn"]?.GetValue<bool>() ?? false,
            ReminderMinutes = e["reminderMinutesBeforeStart"]?.GetValue<int>() ?? 15,
            Preview = Str(e, "bodyPreview").Trim(),
            Attendees = (e["attendees"]?.AsArray() ?? new JsonArray())
                .Select(a => Person(a) is { } p ? new Attendee(p, Str(a?["status"], "response"), Str(a, "type") == "optional") : null)
                .OfType<Attendee>().ToList(),
        };
    }

    /// <summary>
    /// Il colore di un calendario: hexColor se c'è, altrimenti il nome del colore di Outlook (lightBlue...), altrimenti uno
    /// della tavolozza secondo la posizione.
    /// </summary>
    public static string Color(JsonNode? c, int index)
    {
        var hex = Str(c, "hexColor");
        if (hex.Length is 7 or 9 && hex[0] == '#') return "#" + hex[^6..];
        return Str(c, "color") switch
        {
            "lightBlue" => "#4F9BD9",
            "lightGreen" => "#5CB85C",
            "lightOrange" => "#F0A30A",
            "lightGray" => "#8A8886",
            "lightYellow" => "#D5B300",
            "lightTeal" => "#2BB5A0",
            "lightPink" => "#E46BAE",
            "lightBrown" => "#A67A5B",
            "lightRed" => "#E05D5D",
            _ => Palette[index % Palette.Length],
        };
    }

    static readonly string[] Palette = ["#0F6CBD", "#C239B3", "#107C10", "#CA5010", "#8764B8", "#038387", "#A4262C", "#4F6BED"];

    /// <summary>Un pezzo di evento nella colonna di un giorno: da che ora a che ora (frazioni del giorno), in quale colonna.</summary>
    public sealed record Placed(CalendarEvent Event, double Top, double Bottom, int Column, int Columns);

    /// <summary>
    /// Gli eventi con un orario nella giornata, come nel nuovo Outlook: un evento che attraversa la mezzanotte ha un pezzo
    /// in ogni giorno; quelli che si sovrappongono si dividono la larghezza (colonne per gruppo di eventi che si toccano).
    /// Le ore sono quelle dell'orologio: il 25 ottobre, con l'ora solare, un evento alle 9 sta alle 9.
    /// </summary>
    public static List<Placed> Day(IEnumerable<CalendarEvent> events, DateTime day)
    {
        day = day.Date;
        var next = day.AddDays(1);
        var pieces = events.Where(e => !e.AllDay && e.Start < next && (e.End > day || e.End == e.Start && e.Start >= day))
            .Select(e =>
            {
                var from = e.Start < day ? day : e.Start;
                var to = e.End > next ? next : e.End;
                return (Event: e, From: from, To: to > from ? to : from.AddMinutes(15));
            })
            .OrderBy(p => p.From).ThenByDescending(p => p.To).ToList();
        var placed = new List<Placed>();
        var group = new List<(CalendarEvent Event, DateTime From, DateTime To, int Column)>();
        var groupEnd = DateTime.MinValue;
        void Flush()
        {
            int columns = group.Count == 0 ? 1 : group.Max(g => g.Column) + 1;
            foreach (var g in group)
                placed.Add(new Placed(g.Event, (g.From - day).TotalMinutes / 1440, Math.Min(1, (g.To - day).TotalMinutes / 1440), g.Column, columns));
            group.Clear();
        }
        foreach (var p in pieces)
        {
            if (group.Count > 0 && p.From >= groupEnd) Flush();
            // La prima colonna libera: quella in cui l'ultimo evento è già finito.
            int column = 0;
            while (group.Any(g => g.Column == column && g.To > p.From)) column++;
            group.Add((p.Event, p.From, p.To, column));
            groupEnd = group.Count == 1 || p.To > groupEnd ? p.To : groupEnd;
        }
        Flush();
        return placed;
    }

    /// <summary>Gli eventi di un giorno intero (o di più giorni) che toccano il giorno.</summary>
    public static IEnumerable<CalendarEvent> AllDayOn(IEnumerable<CalendarEvent> events, DateTime day) =>
        events.Where(e => e.AllDay && e.Start.Date <= day.Date && e.End > day.Date);

    /// <summary>Quando, in parole: "sabato 26 settembre, 09:00 - 10:00", "tutto il giorno", o due date.</summary>
    public static string When(CalendarEvent e)
    {
        var it = new CultureInfo("it-IT");
        if (e.AllDay)
        {
            var last = e.End.AddDays(-1);
            return last.Date <= e.Start.Date
                ? $"{e.Start.ToString("dddd d MMMM", it)}, tutto il giorno"
                : $"dal {e.Start.ToString("dddd d MMMM", it)} al {last.ToString("dddd d MMMM", it)}, tutto il giorno";
        }
        return e.End.Date == e.Start.Date || e.End == e.Start.Date.AddDays(1)
            ? $"{e.Start.ToString("dddd d MMMM", it)}, {e.Start:HH:mm} - {e.End:HH:mm}"
            : $"{e.Start.ToString("dddd d MMMM, HH:mm", it)} - {e.End.ToString("dddd d MMMM, HH:mm", it)}";
    }

    /// <summary>
    /// L'evento nuovo per Graph (POST me/calendars/{id}/events): inizio e fine come ore dell'orologio con il fuso di Windows
    /// (timeZone), così anche il 25 ottobre, con il cambio d'ora, le 9 restano le 9; la giornata intera come date, da
    /// mezzanotte a mezzanotte del giorno dopo; la ripetizione semplice come patternedRecurrence; i partecipanti necessari e
    /// facoltativi; la riunione di Teams solo se chiesta (onlineMeetingProvider teamsForBusiness, account di lavoro).
    /// </summary>
    public static JsonObject ToGraph(NewEvent e, string timeZone)
    {
        var start = e.AllDay ? e.Start.Date : e.Start;
        var end = e.AllDay ? (e.End.Date > e.Start.Date ? e.End.Date : e.Start.Date.AddDays(1)) : e.End;
        var body = new JsonObject
        {
            ["subject"] = e.Subject.Trim(),
            ["body"] = new JsonObject { ["contentType"] = "HTML", ["content"] = e.Html },
            ["start"] = Time(start, timeZone),
            ["end"] = Time(end, timeZone),
            ["isAllDay"] = e.AllDay,
            ["isReminderOn"] = e.ReminderMinutes != null,
            ["showAs"] = e.ShowAs,
        };
        if (e.ReminderMinutes is { } minutes) body["reminderMinutesBeforeStart"] = minutes;
        if (e.Location.Trim().Length > 0) body["location"] = new JsonObject { ["displayName"] = e.Location.Trim() };
        if (e.IsMeeting)
        {
            JsonObject Person(Address a, string type) => new()
            {
                ["emailAddress"] = new JsonObject { ["address"] = a.Email, ["name"] = a.Name.Length > 0 ? a.Name : a.Email },
                ["type"] = type,
            };
            body["attendees"] = new JsonArray(e.Required.Select(a => (JsonNode)Person(a, "required")).Concat(e.Optional.Select(a => Person(a, "optional"))).ToArray());
        }
        if (e.OnlineMeeting)
        {
            body["isOnlineMeeting"] = true;
            body["onlineMeetingProvider"] = "teamsForBusiness";
        }
        if (e.Repeat != EventRepeat.None)
        {
            var pattern = new JsonObject { ["interval"] = 1 };
            switch (e.Repeat)
            {
                case EventRepeat.Daily:
                    pattern["type"] = "daily";
                    break;
                case EventRepeat.Weekly:
                    pattern["type"] = "weekly";
                    var days = e.RepeatDays.Count > 0 ? e.RepeatDays : [start.DayOfWeek];
                    pattern["daysOfWeek"] = new JsonArray(days.Distinct().OrderBy(d => ((int)d + 6) % 7).Select(d => (JsonNode)d.ToString().ToLowerInvariant()).ToArray());
                    pattern["firstDayOfWeek"] = "monday";
                    break;
                case EventRepeat.Monthly:
                    pattern["type"] = "absoluteMonthly";
                    pattern["dayOfMonth"] = start.Day;
                    break;
            }
            var range = new JsonObject { ["startDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            if (e.RepeatUntil is { } until)
            {
                range["type"] = "endDate";
                range["endDate"] = until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else range["type"] = "noEnd";
            body["recurrence"] = new JsonObject { ["pattern"] = pattern, ["range"] = range };
        }
        return body;
    }

    static JsonObject Time(DateTime clock, string zone) => new()
    {
        ["dateTime"] = clock.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        ["timeZone"] = zone,
    };

    /// <summary>
    /// Un evento cambiato nella finestra (Modifica o trascinato), per Graph (PATCH me/events/{id}): solo i campi cambiati,
    /// come il nuovo Outlook, così una modifica del luogo non riscrive orari, partecipanti o descrizione. Inizio e fine
    /// insieme, come ore dell'orologio con il fuso di Windows (il 25 ottobre le 9 restano le 9), la giornata intera come
    /// date; i partecipanti tutti (Graph sostituisce l'elenco, ed Exchange manda l'aggiornamento ai nuovi e a chi esce).
    /// Vuoto se non è cambiato nulla.
    /// </summary>
    public static JsonObject Changes(CalendarEvent before, NewEvent after, string timeZone)
    {
        var patch = new JsonObject();
        if (after.Subject.Trim() != before.Subject) patch["subject"] = after.Subject.Trim();
        if (after.Location.Trim() != before.Location) patch["location"] = new JsonObject { ["displayName"] = after.Location.Trim() };
        var start = after.AllDay ? after.Start.Date : after.Start;
        var end = after.AllDay ? (after.End.Date > after.Start.Date ? after.End.Date : after.Start.Date.AddDays(1)) : after.End;
        if (after.AllDay != before.AllDay || start != before.Start || end != before.End)
        {
            patch["start"] = Time(start, timeZone);
            patch["end"] = Time(end, timeZone);
            if (after.AllDay != before.AllDay) patch["isAllDay"] = after.AllDay;
        }
        int? reminder = before.ReminderOn ? before.ReminderMinutes : null;
        if (after.ReminderMinutes != reminder)
        {
            patch["isReminderOn"] = after.ReminderMinutes != null;
            if (after.ReminderMinutes is { } minutes) patch["reminderMinutesBeforeStart"] = minutes;
        }
        if (after.ShowAs != before.ShowAs) patch["showAs"] = after.ShowAs;
        static string Key(Address a, bool optional) => a.Email.Trim().ToLowerInvariant() + (optional ? "|facoltativo" : "");
        var was = before.Attendees.Select(a => Key(a.Person, a.Optional)).ToHashSet();
        var now = after.Required.Select(a => Key(a, false)).Concat(after.Optional.Select(a => Key(a, true))).ToHashSet();
        if (!was.SetEquals(now))
        {
            JsonObject Person(Address a, string type) => new()
            {
                ["emailAddress"] = new JsonObject { ["address"] = a.Email, ["name"] = a.Name.Length > 0 ? a.Name : a.Email },
                ["type"] = type,
            };
            patch["attendees"] = new JsonArray(after.Required.Select(a => (JsonNode)Person(a, "required")).Concat(after.Optional.Select(a => Person(a, "optional"))).ToArray());
        }
        if (after.Html.Length > 0) patch["body"] = new JsonObject { ["contentType"] = "HTML", ["content"] = after.Html };
        return patch;
    }

    /// <summary>Che cosa cambia un PATCH di Changes, in parole, per il registro e per la prova.</summary>
    public static string ChangedText(JsonObject patch) => string.Join(", ", patch.Select(p => p.Key switch
    {
        "subject" => "titolo",
        "location" => "luogo",
        "start" => "inizio",
        "end" => "fine",
        "isAllDay" => "giornata intera",
        "isReminderOn" => "promemoria",
        "reminderMinutesBeforeStart" => "minuti del promemoria",
        "showAs" => "mostra come",
        "attendees" => "partecipanti",
        "body" => "descrizione",
        _ => p.Key,
    }));

    /// <summary>
    /// "Tutta la serie" spostata: la serie (seriesMasterId) comincia alla sua prima occorrenza, quindi il suo inizio si
    /// sposta dello stesso tempo dell'occorrenza scelta, con la durata nuova. Solo nello stesso giorno: i giorni della
    /// ripetizione restano quelli (per cambiarli servirebbe riscrivere la ricorrenza).
    /// </summary>
    public static (DateTime Start, DateTime End) SeriesTimes(CalendarEvent occurrence, NewEvent after, DateTime seriesStart)
    {
        var shift = after.Start - occurrence.Start;
        var start = seriesStart + shift;
        return (start, start + (after.End - after.Start));
    }

    /// <summary>
    /// L'evento com'è, come se fosse scritto nella finestra, con un altro orario: lo spostamento trascinandolo nella griglia
    /// (Changes ne manda solo inizio e fine).
    /// </summary>
    public static NewEvent Moved(CalendarEvent e, DateTime start, DateTime end) => new()
    {
        CalendarId = e.CalendarId, Subject = e.Subject, Start = start, End = end, AllDay = e.AllDay, Location = e.Location,
        ReminderMinutes = e.ReminderOn ? e.ReminderMinutes : null, ShowAs = e.ShowAs,
        Required = e.Attendees.Where(a => !a.Optional).Select(a => a.Person).ToList(),
        Optional = e.Attendees.Where(a => a.Optional).Select(a => a.Person).ToList(),
    };

    /// <summary>Inizio e fine di Changes scritti per la serie (SeriesTimes), con il fuso di Windows.</summary>
    public static void SetTimes(JsonObject patch, DateTime start, DateTime end, bool allDay, string timeZone)
    {
        patch["start"] = Time(allDay ? start.Date : start, timeZone);
        patch["end"] = Time(allDay ? (end.Date > start.Date ? end.Date : start.Date.AddDays(1)) : end, timeZone);
    }

    /// <summary>La risposta in parole, per i dettagli.</summary>
    public static string ResponseText(string response) => response switch
    {
        "organizer" => "Organizzatore",
        "accepted" => "Accettato",
        "tentativelyAccepted" => "Provvisorio",
        "declined" => "Rifiutato",
        "notResponded" => "Nessuna risposta",
        _ => "",
    };

    // ---------------------------------------------------------------- copia locale

    static string CacheFile(MailAccount account) => Store.CachePath($"calendario-{Store.CacheName(account.Id)}.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>
    /// La copia locale della settimana di oggi, per mostrarla subito all'apertura del calendario; cifrata con DPAPI come la
    /// copia della posta (MailCache), perché titoli, luoghi e partecipanti non restino in chiaro sul disco.
    /// </summary>
    public static void Save(MailAccount account, CalendarData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile(account))!);
            // Con un file temporaneo e la rinomina (C-17): prima si scriveva sul file vero, e una scrittura interrotta lo lasciava a metà.
            SafeFile.WriteCopy(CacheFile(account), SafeFile.Protect(JsonSerializer.SerializeToUtf8Bytes(data, Json)));
        }
        catch (Exception ex)
        {
            Log.Write("copia locale del calendario non salvata: " + ex.Message);
        }
    }

    public static CalendarData? Load(MailAccount account)
    {
        try
        {
            if (!File.Exists(CacheFile(account))) return null;
            var bytes = File.ReadAllBytes(CacheFile(account));
            if (bytes.Length > 0 && bytes[0] != (byte)'{') // quelle di prima, in chiaro, si leggono ancora
                bytes = SafeFile.Unprotect(bytes);
            return JsonSerializer.Deserialize<CalendarData>(bytes, Json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Delete(MailAccount account)
    {
        try { File.Delete(CacheFile(account)); }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) { Log.Error("copia del calendario dell'account rimosso non cancellata", ex); }
    }
}
