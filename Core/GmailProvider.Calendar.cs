using System.Text.Json.Nodes;

namespace Missiva;

/// <summary>
/// Il calendario di Google di un account Gmail o Google Workspace (Calendar API, vedi <see cref="GoogleCalendar"/>). Il
/// permesso si chiede solo dal pulsante "Consenti l'accesso al calendario": senza, null e nessuna richiesta, come Graph.
/// La risposta agli inviti che arrivano per posta resta iMIP (IMailProvider.RespondAsync): Google li mette da sé nel
/// calendario, e la risposta la vede.
/// </summary>
public sealed partial class GmailProvider
{
    static string CalendarConsentNeeded => T("Per scrivere nel calendario Missiva deve poterlo usare: premi \"Consenti l'accesso al calendario\" nel calendario.");

    public bool HasCalendar => true;

    /// <summary>
    /// Il permesso del calendario: un nuovo accesso nel browser con l'ambito in più, per lo stesso account (il token di prima
    /// è sostituito, e la posta continua senza interruzioni). Solo dal pulsante, mai da sola.
    /// </summary>
    public Task AllowCalendarAsync() => AllowAsync(GoogleAuth.CalendarScope, "il calendario",
        chosen => T("Nel browser hai scelto {0}, non {1}: per il calendario serve lo stesso account.", chosen, Account.Email),
        T("Non hai dato a Missiva il permesso di usare il calendario. Riprova e lascia spuntata la casella del calendario."));

    /// <summary>
    /// Un permesso in più (calendario, contatti): un nuovo accesso nel browser per lo stesso account, con l'ambito nuovo e
    /// quelli che l'account ha già. Il token nuovo sostituisce il vecchio; solo dal pulsante, mai da solo.
    /// </summary>
    /// <param name="what">Il permesso, per il registro ("il calendario").</param>
    /// <param name="otherAccount">L'errore quando nel browser si sceglie un altro account (quello scelto), già tradotto.</param>
    /// <param name="refused">L'errore quando la casella del permesso è stata tolta, già tradotto.</param>
    async Task AllowAsync(string scope, string what, Func<string, string> otherAccount, string refused)
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromMinutes(MailLimits.BrowserSignInMinutes));
        GoogleAuth.SignInResult result;
        try
        {
            var scopes = GoogleAuth.ExtraScopesOf(Account).Append(scope).Distinct().ToList();
            if (scopes.Contains(GoogleAuth.ContactsWriteScope)) scopes.Remove(GoogleAuth.ContactsScope); // la scrittura comprende la lettura
            result = await GoogleAuth.SignInAsync(wait.Token, Account.Email, scopes);
        }
        catch (OperationCanceledException)
        {
            throw new MailException(T("L'accesso nel browser non è stato completato in tempo. Riprova."));
        }
        if (!result.Email.Equals(Account.Email, StringComparison.OrdinalIgnoreCase))
            throw new MailException(otherAccount(result.Email));
        if (!result.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope))
            throw new MailException(refused);
        GoogleAuth.Apply(Account, result); // se nella pagina di Google è stata tolta un'altra casella, lo dice
        Store.Save();
        Log.Write($"permesso per {what} dato da {Account.Email}");
    }

    static string CalendarPath(string calendarId) => Uri.EscapeDataString(calendarId.Length > 0 ? calendarId : "primary");

    /// <summary>
    /// I calendari dell'account (calendarList, con i colori) e gli eventi dell'intervallo di ognuno (events.list con
    /// singleEvents, che espande le ricorrenze), con gli orari nell'ora di Windows. Null senza il permesso del calendario.
    /// Un calendario che Google non lascia leggere (403, 404) resta senza eventi, senza fermare gli altri.
    /// </summary>
    public async Task<CalendarData?> GetCalendarAsync(DateTime from, DateTime to, CancellationToken cancel = default)
    {
        if (!GoogleAuth.HasCalendar(Account)) return null;
        var list = await GoogleCalendar.SendAsync(Account, HttpMethod.Get,
            "users/me/calendarList?minAccessRole=freeBusyReader&maxResults=250&fields=items(id,summary,summaryOverride,backgroundColor,primary,accessRole,hidden,defaultReminders)", cancel: cancel);
        static string Str(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        var entries = (list?["items"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>()
            .Where(c => Str(c, "id").Length > 0 && !(c["hidden"] is JsonValue h && h.TryGetValue<bool>(out var hidden) && hidden)).ToList();
        var calendars = entries.Select((c, i) => new CalendarInfo(Str(c, "id"),
            Str(c, "summaryOverride") is { Length: > 0 } name ? name : Str(c, "summary"), GoogleCalendar.Color(c, i),
            c["primary"] is JsonValue p && p.TryGetValue<bool>(out var primary) && primary, Str(c, "accessRole") is "owner" or "writer")).ToList();
        var reminders = entries.ToDictionary(c => Str(c, "id"), c => GoogleCalendar.PopupMinutes(c["defaultReminders"]));
        var range = $"timeMin={Uri.EscapeDataString(Calendars.Iso(from))}&timeMax={Uri.EscapeDataString(Calendars.Iso(to))}";
        // Al più tre calendari alla volta: Google limita le richieste al secondo per utente.
        using var gate = new SemaphoreSlim(3);
        var events = await Task.WhenAll(calendars.Select(async c =>
        {
            await gate.WaitAsync(cancel);
            try
            {
                var found = new List<CalendarEvent>();
                string? page = null;
                for (int n = 0; n < 10; n++)
                {
                    var url = $"calendars/{CalendarPath(c.Id)}/events?singleEvents=true&orderBy=startTime&maxResults=250&{range}&fields={Uri.EscapeDataString(GoogleCalendar.EventFields)}" +
                              (page != null ? "&pageToken=" + Uri.EscapeDataString(page) : "");
                    var answer = await GoogleCalendar.SendAsync(Account, HttpMethod.Get, url, cancel: cancel);
                    found.AddRange((answer?["items"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>()
                        .Select(e => GoogleCalendar.Event(e, c.Id, reminders.GetValueOrDefault(c.Id)))
                        .Where(e => !e.IsCancelled));
                    page = Str(answer, "nextPageToken") is { Length: > 0 } next ? next : null;
                    if (page == null) break;
                }
                return found;
            }
            catch (MailException ex) when (ex.Status is 403 or 404 && ex.Code != "accessNotConfigured" && ex.Code != "insufficientPermissions")
            {
                Log.Write($"calendario {Log.Private(c.Name)} di {Account.Email} non letto: {ex.Message}"); // il nome di un calendario dice di chi e di che cosa (audit, S-27)
                return new List<CalendarEvent>();
            }
            finally
            {
                gate.Release();
            }
        }));
        return new CalendarData { Calendars = calendars, Events = events.SelectMany(e => e).ToList(), From = from, To = to };
    }

    void RequireCalendar()
    {
        if (!GoogleAuth.HasCalendar(Account)) throw new MailException(CalendarConsentNeeded);
    }

    /// <summary>
    /// Nuovo evento: POST calendars/{id}/events con sendUpdates=all, così con dei partecipanti Google manda gli inviti.
    /// Le riunioni online di Google Meet non ci sono ancora (l'opzione della finestra è solo per Teams).
    /// </summary>
    public async Task<string> CreateEventAsync(NewEvent e)
    {
        RequireCalendar();
        var zone = GoogleCalendar.Zone;
        if (e.Repeat != EventRepeat.None && zone == null)
            throw new MailException(T("Windows non dice a Missiva in che fuso orario sei: un evento che si ripete non si può scrivere sul calendario di Google."));
        var created = await GoogleCalendar.SendAsync(Account, HttpMethod.Post, $"calendars/{CalendarPath(e.CalendarId)}/events?sendUpdates=all", GoogleCalendar.ToGoogle(e, zone));
        return created?["id"]?.GetValue<string>() ?? "";
    }

    /// <summary>L'evento su cui agire: l'occorrenza scelta, o per tutta la serie la serie (recurringEventId).</summary>
    static string EventId(CalendarEvent e, bool series) => Uri.EscapeDataString(series && e.SeriesMasterId.Length > 0 ? e.SeriesMasterId : e.Id);

    /// <summary>
    /// Modifica: PATCH dei soli campi cambiati (GoogleCalendar.Changes), nel fuso dell'utente. Per tutta la serie il PATCH va
    /// alla serie, con l'orario spostato quanto quello dell'occorrenza (Calendars.SeriesTimes, dall'inizio della serie letto
    /// prima). Nessuna richiesta se non è cambiato nulla; con dei partecipanti Google manda l'aggiornamento (sendUpdates=all).
    /// </summary>
    public async Task UpdateEventAsync(CalendarEvent original, NewEvent changed, bool series)
    {
        RequireCalendar();
        var zone = GoogleCalendar.Zone;
        var patch = GoogleCalendar.Changes(original, changed, zone);
        if (patch.Count == 0)
        {
            Log.Write($"evento {original.Id}: nessuna modifica, nessuna richiesta");
            return;
        }
        series &= original.SeriesMasterId.Length > 0;
        var calendar = CalendarPath(original.CalendarId);
        if (series && patch.ContainsKey("start"))
        {
            if (changed.Start.Date != original.Start.Date || changed.AllDay != original.AllDay)
                throw new MailException(T("Tutta la serie si sposta solo di orario, nello stesso giorno: per cambiare i giorni della ripetizione usa Google Calendar sul web."));
            var master = await GoogleCalendar.SendAsync(Account, HttpMethod.Get, $"calendars/{calendar}/events/{EventId(original, true)}?fields=start,end");
            var (start, end) = Calendars.SeriesTimes(original, changed, GoogleCalendar.LocalTime(master?["start"]));
            GoogleCalendar.SetTimes(patch, start, end, changed.AllDay, zone);
        }
        await GoogleCalendar.SendAsync(Account, new HttpMethod("PATCH"), $"calendars/{calendar}/events/{EventId(original, series)}?sendUpdates=all", patch);
    }

    /// <summary>Elimina un evento proprio senza partecipanti (o tutta la sua serie): DELETE, senza avvisare nessuno.</summary>
    public async Task DeleteEventAsync(CalendarEvent e, bool series)
    {
        RequireCalendar();
        await GoogleCalendar.SendAsync(Account, HttpMethod.Delete, $"calendars/{CalendarPath(e.CalendarId)}/events/{EventId(e, series)}?sendUpdates=none");
    }

    /// <summary>
    /// Annulla una riunione organizzata da sé: DELETE con sendUpdates=all, e Google avvisa i partecipanti che è annullata.
    /// Google non ha la nota dell'annullamento: quella scritta nella finestra non parte.
    /// </summary>
    public async Task CancelEventAsync(CalendarEvent e, string comment, bool series)
    {
        RequireCalendar();
        if (comment.Trim().Length > 0) Log.Write("annullamento su Google Calendar: la nota non si può mandare");
        await GoogleCalendar.SendAsync(Account, HttpMethod.Delete, $"calendars/{CalendarPath(e.CalendarId)}/events/{EventId(e, series)}?sendUpdates=all");
    }

    /// <summary>
    /// Accetta, Provvisorio o Rifiuta dai dettagli dell'evento: si legge l'elenco dei partecipanti e lo si rimanda con la mia
    /// risposta (e la nota) cambiata, perché il PATCH di Google lo sostituisce tutto; con send Google avvisa l'organizzatore.
    /// </summary>
    public async Task RespondToEventAsync(CalendarEvent e, InviteAnswer answer, string comment, bool send)
    {
        RequireCalendar();
        var path = $"calendars/{CalendarPath(e.CalendarId)}/events/{Uri.EscapeDataString(e.Id)}";
        var current = await GoogleCalendar.SendAsync(Account, HttpMethod.Get, path + "?fields=attendees");
        if (current?["attendees"] is not JsonArray { Count: > 0 })
            throw new MailException(T("Questo evento non ha partecipanti a cui rispondere."));
        await GoogleCalendar.SendAsync(Account, new HttpMethod("PATCH"), path + $"?sendUpdates={(send ? "all" : "none")}",
            new JsonObject { ["attendees"] = GoogleCalendar.WithMyAnswer(current["attendees"], answer, comment) });
    }

    /// <summary>
    /// La disponibilità dei partecipanti (freeBusy): per ognuno il suo calendario, se lo condivide con te (di solito solo dentro
    /// la stessa organizzazione Google Workspace); per gli altri Google risponde "non trovato" e lo si dice.
    /// </summary>
    public async Task<List<Availability>?> GetScheduleAsync(IReadOnlyList<string> emails, DateTime from, DateTime to)
    {
        if (!GoogleAuth.HasCalendar(Account) || emails.Count == 0) return null;
        var answer = await GoogleCalendar.SendAsync(Account, HttpMethod.Post, "freeBusy", new JsonObject
        {
            ["timeMin"] = Calendars.Iso(from),
            ["timeMax"] = Calendars.Iso(to),
            ["items"] = new JsonArray(emails.Select(e => (JsonNode)new JsonObject { ["id"] = e }).ToArray()),
        });
        return emails.Select(email =>
        {
            var calendar = answer?["calendars"]?[email];
            var errors = calendar?["errors"]?.AsArray();
            if (calendar == null || errors is { Count: > 0 })
                return new Availability(email, "", errors?.FirstOrDefault()?["reason"]?.GetValue<string>() == "notFound"
                    ? T("il calendario non è condiviso con te") : T("non disponibile"));
            return new Availability(email, GoogleCalendar.ViewOf(calendar["busy"], from, to), null);
        }).ToList();
    }
}
