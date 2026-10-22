using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Missiva;

/// <summary>
/// Il calendario di Google (Calendar API v3, https://www.googleapis.com/calendar/v3/) per gli account Gmail e Google Workspace,
/// con lo stesso accesso della posta e l'ambito del calendario (GoogleAuth.CalendarScope), chiesto solo dal pulsante
/// "Consenti l'accesso al calendario". Qui il passaggio fra i JSON di Google e i tipi di Missiva (CalendarEvent, NewEvent);
/// le richieste sono di GoogleApi; GmailProvider usa tutto per i metodi del calendario di IMailProvider.
/// </summary>
public static class GoogleCalendar
{
    const string Base = "https://www.googleapis.com/calendar/v3/";
    static readonly GoogleService Service = new("Google Calendar API", "calendar-json",
        N("Missiva non ha il permesso di usare il calendario di questo account: premi \"Consenti l'accesso al calendario\" nel calendario."),
        N("Google ha limitato le richieste (il calendario): riprova fra poco."));

#if DEBUG
    /// <summary>Prove (Missiva.exe --prova-bozze): un gestore HTTP finto al posto di Google.</summary>
    internal static void UseTestHandler(HttpMessageHandler handler) => GoogleApi.UseTestHandler(handler);
#endif

    // ---------------------------------------------------------------- richieste

    /// <summary>Una richiesta all'API del calendario (il percorso dopo /calendar/v3/) con il token dell'account; errori in italiano.</summary>
    public static Task<JsonNode?> SendAsync(MailAccount account, HttpMethod method, string path, JsonNode? body = null, CancellationToken cancel = default) =>
        GoogleApi.SendAsync(Service, account, method, Base + path, body, cancel);

    // ---------------------------------------------------------------- lettura

    static string Str(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    static bool Flag(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    /// <summary>
    /// Un istante di Google ({dateTime} con la differenza da UTC, oppure {date} per i giorni interi) nell'ora di Windows:
    /// l'ora dell'orologio di adesso, anche per gli eventi scritti in un altro fuso.
    /// </summary>
    public static DateTime LocalTime(JsonNode? t)
    {
        if (Str(t, "dateTime") is { Length: > 0 } at && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            return DateTime.SpecifyKind(instant.LocalDateTime, DateTimeKind.Unspecified);
        if (Str(t, "date") is { Length: >= 10 } day && DateTime.TryParseExact(day[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        return DateTime.MinValue;
    }

    /// <summary>Minuti del primo promemoria a comparsa (popup) di un elenco di Google; null se non ce ne sono.</summary>
    public static int? PopupMinutes(JsonNode? reminders) =>
        (reminders?.AsArray() ?? new JsonArray()).OfType<JsonNode>().Where(r => Str(r, "method") != "email")
            .Select(r => r["minutes"] is JsonValue m && m.TryGetValue<int>(out var minutes) ? minutes : (int?)null).FirstOrDefault(m => m != null);

    static string ResponseOf(string status) => status switch
    {
        "accepted" => "accepted",
        "tentative" => "tentativelyAccepted",
        "declined" => "declined",
        _ => "none",
    };

    /// <summary>Il testo della descrizione, senza i tag HTML che Google ci lascia (a capo, elenchi, link).</summary>
    static string PlainText(string description)
    {
        if (description.Length == 0) return "";
        var text = Regex.Replace(description, @"<\s*(br|/p|/div|/li)\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", ""));
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\s*\n\s*", "\n").Trim();
        return text.Length > 500 ? text[..500] + "…" : text;
    }

    /// <summary>I campi chiesti agli eventi: solo quelli che la vista e i dettagli usano, senza il resto di Google.</summary>
    public const string EventFields = "nextPageToken,items(id,status,summary,description,location,start,end,transparency,eventType,organizer,attendees(email,displayName,responseStatus,optional,self)," +
                                      "recurringEventId,hangoutLink,conferenceData/entryPoints,reminders)";

    /// <param name="defaultReminder">I minuti del promemoria predefinito del calendario, per gli eventi che usano quello.</param>
    public static CalendarEvent Event(JsonNode e, string calendarId, int? defaultReminder)
    {
        bool allDay = e["start"]?["date"] != null;
        var start = LocalTime(e["start"]);
        var end = LocalTime(e["end"]);
        if (allDay && end <= start) end = start.AddDays(1);
        var attendees = (e["attendees"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>().ToList();
        var mine = attendees.FirstOrDefault(a => Flag(a, "self"));
        bool organizer = Flag(e["organizer"], "self");
        var organizerEmail = Str(e["organizer"], "email");
        var join = Str(e, "hangoutLink");
        if (join.Length == 0)
            join = Str((e["conferenceData"]?["entryPoints"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>().FirstOrDefault(p => Str(p, "entryPointType") == "video"), "uri");
        int? reminder = Flag(e["reminders"], "useDefault") ? defaultReminder : PopupMinutes(e["reminders"]?["overrides"]);
        return new CalendarEvent
        {
            Id = Str(e, "id"),
            CalendarId = calendarId,
            Subject = Str(e, "summary"),
            Location = Str(e, "location"),
            Start = start,
            End = end < start ? start : end,
            AllDay = allDay,
            ShowAs = Str(e, "eventType") switch
            {
                "outOfOffice" => "oof",
                "workingLocation" => "workingElsewhere",
                _ => Str(e, "transparency") == "transparent" ? "free" : Str(e, "status") == "tentative" ? "tentative" : "busy",
            },
            Organizer = organizerEmail.Length > 0 ? new Address { Name = Str(e["organizer"], "displayName"), Email = organizerEmail } : null,
            Response = organizer ? "organizer" : mine != null ? ResponseOf(Str(mine, "responseStatus")) : "none",
            JoinUrl = Uri.TryCreate(join, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null,
            IsRecurring = Str(e, "recurringEventId").Length > 0,
            SeriesMasterId = Str(e, "recurringEventId"),
            IsOrganizer = organizer,
            IsCancelled = Str(e, "status") == "cancelled",
            ReminderOn = reminder != null,
            ReminderMinutes = reminder ?? 15,
            Preview = PlainText(Str(e, "description")),
            Attendees = attendees.Where(a => Str(a, "email").Length > 0)
                .Select(a => new Attendee(new Address { Name = Str(a, "displayName"), Email = Str(a, "email") }, ResponseOf(Str(a, "responseStatus")), Flag(a, "optional"))).ToList(),
        };
    }

    /// <summary>Il colore di un calendario di Google (backgroundColor #RRGGBB) o uno della tavolozza secondo la posizione.</summary>
    public static string Color(JsonNode? c, int index)
    {
        var hex = Str(c, "backgroundColor");
        return hex.Length == 7 && hex[0] == '#' && hex[1..].All(Uri.IsHexDigit) ? hex : Palette[index % Palette.Length];
    }

    static readonly string[] Palette = ["#0F6CBD", "#C239B3", "#107C10", "#CA5010", "#8764B8", "#038387", "#A4262C", "#4F6BED"];

    // ---------------------------------------------------------------- scrittura

    /// <summary>
    /// Il fuso di Windows come nome IANA (Europe/Rome), che Google vuole per gli eventi che si ripetono; null se Windows non
    /// lo sa convertire (allora gli orari partono con la differenza da UTC e la ripetizione si rifiuta).
    /// </summary>
    public static string? Zone => TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : null;

    /// <summary>
    /// Un orario come lo scrive l'utente: l'ora dell'orologio con il fuso (il 25 ottobre, con il cambio d'ora, le 9 restano le
    /// 9); la giornata intera come data.
    /// </summary>
    static JsonObject Time(DateTime clock, bool allDay, string? zone)
    {
        if (allDay) return new JsonObject { ["date"] = clock.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        if (zone == null)
        {
            var local = DateTime.SpecifyKind(clock, DateTimeKind.Unspecified);
            return new JsonObject { ["dateTime"] = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture) };
        }
        return new JsonObject { ["dateTime"] = clock.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), ["timeZone"] = zone };
    }

    static (DateTime Start, DateTime End) Span(bool allDay, DateTime start, DateTime end) =>
        allDay ? (start.Date, end.Date > start.Date ? end.Date : start.Date.AddDays(1)) : (start, end);

    static JsonObject Reminders(int? minutes) => minutes is { } m
        ? new JsonObject { ["useDefault"] = false, ["overrides"] = new JsonArray(new JsonObject { ["method"] = "popup", ["minutes"] = m }) }
        : new JsonObject { ["useDefault"] = false };

    static readonly string[] DayCodes = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    /// <summary>La ripetizione semplice come RRULE di iCalendar; la fine per gli eventi con ora in UTC, come vuole la regola.</summary>
    static JsonArray Recurrence(NewEvent e, DateTime start)
    {
        var parts = new List<string>();
        switch (e.Repeat)
        {
            case EventRepeat.Daily:
                parts.Add("FREQ=DAILY");
                break;
            case EventRepeat.Weekly:
                var days = (e.RepeatDays.Count > 0 ? e.RepeatDays : [start.DayOfWeek]).Distinct().OrderBy(d => ((int)d + 6) % 7);
                parts.Add("FREQ=WEEKLY");
                parts.Add("BYDAY=" + string.Join(",", days.Select(d => DayCodes[(int)d])));
                break;
            case EventRepeat.Monthly:
                parts.Add("FREQ=MONTHLY");
                parts.Add("BYMONTHDAY=" + start.Day);
                break;
        }
        if (e.RepeatUntil is { } until)
        {
            var lastMoment = DateTime.SpecifyKind(until.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Unspecified);
            parts.Add("UNTIL=" + (e.AllDay ? until.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : TimeZoneInfo.ConvertTimeToUtc(lastMoment, TimeZoneInfo.Local).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)));
        }
        return new JsonArray("RRULE:" + string.Join(";", parts));
    }

    static JsonObject Person(Address a, bool optional, string? response = null)
    {
        var p = new JsonObject { ["email"] = a.Email };
        if (a.Name.Length > 0 && a.Name != a.Email) p["displayName"] = a.Name;
        if (optional) p["optional"] = true;
        if (response != null) p["responseStatus"] = response;
        return p;
    }

    /// <summary>
    /// L'evento nuovo per Google (POST calendars/{id}/events): ora dell'orologio con il fuso IANA, giornata intera come date,
    /// ripetizione semplice come RRULE, partecipanti necessari e facoltativi (con sendUpdates=all Google manda gli inviti),
    /// promemoria a comparsa. La descrizione è l'HTML dell'editor: Google ne tiene i tag semplici.
    /// </summary>
    public static JsonObject ToGoogle(NewEvent e, string? zone)
    {
        var (start, end) = Span(e.AllDay, e.Start, e.End);
        var body = new JsonObject
        {
            ["summary"] = e.Subject.Trim(),
            ["start"] = Time(start, e.AllDay, zone),
            ["end"] = Time(end, e.AllDay, zone),
            ["transparency"] = e.ShowAs == "free" ? "transparent" : "opaque",
            ["reminders"] = Reminders(e.ReminderMinutes),
        };
        if (e.Html.Length > 0) body["description"] = e.Html;
        if (e.Location.Trim().Length > 0) body["location"] = e.Location.Trim();
        if (e.IsMeeting)
            body["attendees"] = new JsonArray(e.Required.Select(a => (JsonNode)Person(a, false)).Concat(e.Optional.Select(a => Person(a, true))).ToArray());
        if (e.Repeat != EventRepeat.None) body["recurrence"] = Recurrence(e, start);
        return body;
    }

    static string Key(Address a, bool optional) => a.Email.Trim().ToLowerInvariant() + (optional ? "|facoltativo" : "");

    /// <summary>Le risposte dei partecipanti già presenti si tengono: cambiare l'elenco non le azzera.</summary>
    static string? Kept(CalendarEvent before, Address a) => before.Attendees.FirstOrDefault(x => x.Person.Email.Equals(a.Email, StringComparison.OrdinalIgnoreCase))?.Response switch
    {
        "accepted" => "accepted",
        "tentativelyAccepted" => "tentative",
        "declined" => "declined",
        _ => null,
    };

    /// <summary>
    /// Un evento cambiato nella finestra, per Google (PATCH calendars/{id}/events/{id}): solo i campi cambiati. Inizio e fine
    /// insieme; i partecipanti tutti (Google sostituisce l'elenco). Vuoto se non è cambiato nulla.
    /// </summary>
    public static JsonObject Changes(CalendarEvent before, NewEvent after, string? zone)
    {
        var patch = new JsonObject();
        if (after.Subject.Trim() != before.Subject) patch["summary"] = after.Subject.Trim();
        if (after.Location.Trim() != before.Location) patch["location"] = after.Location.Trim();
        var (start, end) = Span(after.AllDay, after.Start, after.End);
        if (after.AllDay != before.AllDay || start != before.Start || end != before.End)
        {
            patch["start"] = Time(start, after.AllDay, zone);
            patch["end"] = Time(end, after.AllDay, zone);
        }
        int? reminder = before.ReminderOn ? before.ReminderMinutes : null;
        if (after.ReminderMinutes != reminder) patch["reminders"] = Reminders(after.ReminderMinutes);
        if (after.ShowAs != before.ShowAs) patch["transparency"] = after.ShowAs == "free" ? "transparent" : "opaque";
        var was = before.Attendees.Select(a => Key(a.Person, a.Optional)).ToHashSet();
        var now = after.Required.Select(a => Key(a, false)).Concat(after.Optional.Select(a => Key(a, true))).ToHashSet();
        if (!was.SetEquals(now))
            patch["attendees"] = new JsonArray(after.Required.Select(a => (JsonNode)Person(a, false, Kept(before, a)))
                .Concat(after.Optional.Select(a => Person(a, true, Kept(before, a)))).ToArray());
        if (after.Html.Length > 0) patch["description"] = after.Html;
        return patch;
    }

    /// <summary>Inizio e fine di Changes scritti per la serie (Calendars.SeriesTimes), nel fuso dell'utente.</summary>
    public static void SetTimes(JsonObject patch, DateTime start, DateTime end, bool allDay, string? zone)
    {
        var (s, e) = Span(allDay, start, end);
        patch["start"] = Time(s, allDay, zone);
        patch["end"] = Time(e, allDay, zone);
    }

    /// <summary>Che cosa cambia un PATCH di Changes, in parole, per il registro e per la prova.</summary>
    public static string ChangedText(JsonObject patch) => string.Join(", ", patch.Select(p => p.Key switch
    {
        "summary" => "titolo",
        "location" => "luogo",
        "start" => "inizio",
        "end" => "fine",
        "reminders" => "promemoria",
        "transparency" => "mostra come",
        "attendees" => "partecipanti",
        "description" => "descrizione",
        _ => p.Key,
    }));

    /// <summary>
    /// I partecipanti di un evento con la mia risposta cambiata (e la nota): l'elenco intero, perché il PATCH di Google lo
    /// sostituisce e gli altri non devono sparire.
    /// </summary>
    public static JsonArray WithMyAnswer(JsonNode? attendees, InviteAnswer answer, string comment)
    {
        var status = answer switch { InviteAnswer.Accept => "accepted", InviteAnswer.Decline => "declined", _ => "tentative" };
        var list = new JsonArray();
        foreach (var a in (attendees?.AsArray() ?? new JsonArray()).OfType<JsonNode>())
        {
            var copy = JsonNode.Parse(a.ToJsonString())!.AsObject();
            if (Flag(a, "self"))
            {
                copy["responseStatus"] = status;
                if (comment.Trim().Length > 0) copy["comment"] = comment.Trim();
            }
            list.Add(copy);
        }
        return list;
    }

    /// <summary>
    /// La disponibilità (freeBusy) a mezz'ore da from a to: un carattere per mezz'ora, 0 libero, 2 occupato, come la vista di
    /// Outlook (Availability.View).
    /// </summary>
    public static string ViewOf(JsonNode? busy, DateTime from, DateTime to)
    {
        static DateTime Instant(JsonNode? n) => LocalTime(new JsonObject { ["dateTime"] = n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "" });
        var periods = (busy?.AsArray() ?? new JsonArray()).OfType<JsonNode>().Select(b => (Start: Instant(b["start"]), End: Instant(b["end"]))).ToList();
        var view = new StringBuilder();
        for (var slot = from; slot < to; slot = slot.AddMinutes(30))
            view.Append(periods.Any(p => p.Start < slot.AddMinutes(30) && p.End > slot) ? '2' : '0');
        return view.ToString();
    }
}
