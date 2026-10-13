#if DEBUG
namespace Missiva;

/// <summary>
/// Solo in Debug: gli eventi finti di --calendario-finto, per fotografare la vista senza toccare nessun calendario vero.
/// Ogni settimana: un evento ricorrente, tre che si sovrappongono, uno di più giorni interi, uno a cavallo della
/// mezzanotte, una riunione di Teams con partecipanti, uno provvisorio e uno annullato; il 25 ottobre 2026, con il
/// ritorno all'ora solare, due eventi nelle ore giuste; oggi uno con il promemoria fra un minuto.
/// </summary>
static class CalendarSamples
{
    static readonly Address Anna = new() { Name = "Anna Bianchi", Email = "anna.bianchi@studio-prova.example" };
    static readonly Address Luca = new() { Name = "Luca Verdi", Email = "luca.verdi@cliente-prova.example" };

    /// <summary>La disponibilità finta dei partecipanti (--calendario-finto): occupati la mattina, uno fuori sede il pomeriggio.</summary>
    public static List<Availability> Schedule(IReadOnlyList<string> emails, DateTime from, DateTime to)
    {
        int slots = (int)((to - from).TotalMinutes / 30);
        return emails.Select((e, i) => new Availability(e, new string(Enumerable.Range(0, slots)
            .Select(s => from.AddMinutes(30 * s).Hour switch { >= 9 and < 11 => i % 2 == 0 ? '2' : '1', >= 14 and < 17 when i % 2 == 1 => '3', _ => '0' }).ToArray()),
            e.EndsWith(".example") && i > 2 ? "calendario non condiviso" : null)).ToList();
    }

    public static CalendarData For(MailAccount account, DateTime from, DateTime to)
    {
        var calendars = new List<CalendarInfo>
        {
            new("principale", "Calendario", "#0F6CBD", true),
            new("compleanni", "Compleanni", "#107C10", false, CanEdit: false),
        };
        var events = new List<CalendarEvent>();
        int n = 0;
        CalendarEvent E(string subject, DateTime start, DateTime end, string calendar = "principale", bool allDay = false, string location = "",
                        string response = "organizer", bool recurring = false, bool cancelled = false, string? join = null, List<Attendee>? attendees = null,
                        bool reminder = false, int minutes = 15, string preview = "") =>
            new()
            {
                Id = $"finto-{account.Id}-{++n}", CalendarId = calendar, Subject = subject, Start = start, End = end, AllDay = allDay, Location = location,
                Response = response, IsRecurring = recurring, IsCancelled = cancelled, JoinUrl = join, Attendees = attendees ?? new(), ReminderOn = reminder,
                IsOrganizer = response == "organizer", SeriesMasterId = recurring ? $"finto-serie-{account.Id}-{subject.Length}" : "",
                ReminderMinutes = minutes, Preview = preview, Organizer = response == "organizer" ? null : Anna,
                ShowAs = response == "tentativelyAccepted" ? "tentative" : "busy",
            };
        var firstMonday = from.AddDays(-(((int)from.DayOfWeek + 6) % 7));
        for (var monday = firstMonday; monday < to; monday = monday.AddDays(7))
        {
            events.Add(E("Punto settimanale", monday.AddHours(9.5), monday.AddHours(10), recurring: true, location: "Sala Verdi"));
            events.Add(E("Compleanno di Giulia", monday.AddDays(3), monday.AddDays(4), "compleanni", allDay: true));
            events.Add(E("Ferie di Anna", monday.AddDays(1), monday.AddDays(4), allDay: true));
            // Cinque eventi di un giorno intero il giovedì: "+2 altri" nella settimana e nel mese, tutti nella vista Giorno.
            events.Add(E("Festa del patrono", monday.AddDays(3), monday.AddDays(4), allDay: true));
            events.Add(E("Studio chiuso", monday.AddDays(3), monday.AddDays(4), allDay: true));
            events.Add(E("Scadenza delle dichiarazioni", monday.AddDays(3), monday.AddDays(4), allDay: true));
            events.Add(E("Cliente Rossi", monday.AddDays(2).AddHours(10), monday.AddDays(2).AddHours(11.5), location: "Studio"));
            events.Add(E("Chiamata con il commercialista", monday.AddDays(2).AddHours(10.5), monday.AddDays(2).AddHours(11), response: "accepted"));
            events.Add(E("Revisione del contratto", monday.AddDays(2).AddHours(11), monday.AddDays(2).AddHours(12), response: "tentativelyAccepted"));
            events.Add(E("Riunione di bilancio", monday.AddDays(3).AddHours(14), monday.AddDays(3).AddHours(15), response: "accepted",
                join: "https://teams.microsoft.com/l/meetup-join/19%3ameeting_prova", reminder: true,
                attendees: [new(Anna, "accepted", false), new(Luca, "tentativelyAccepted", false), new(new Address { Email = "segreteria@studio-prova.example" }, "none", true)],
                preview: "Ordine del giorno: chiusura del trimestre, fatture da incassare, budget del 2027."));
            events.Add(E("Pranzo con Luca", monday.AddDays(3).AddHours(13), monday.AddDays(3).AddHours(14), response: "declined"));
            // Una riunione organizzata da sé: Modifica con "Invia aggiornamento" e Annulla riunione.
            events.Add(E("Preventivo con lo studio Bianchi", monday.AddDays(1).AddHours(15), monday.AddDays(1).AddHours(16), location: "Studio", reminder: true,
                attendees: [new(Anna, "accepted", false), new(Luca, "none", true)], preview: "Preventivo per il nuovo impianto."));
            events.Add(E("Udienza rinviata", monday.AddDays(4).AddHours(9), monday.AddDays(4).AddHours(10), response: "accepted", cancelled: true, location: "Tribunale"));
            events.Add(E("Volo per Madrid", monday.AddDays(4).AddHours(22.5), monday.AddDays(5).AddHours(1.5), location: "Aeroporto di Bologna"));
        }
        // Il 25 ottobre 2026 si torna all'ora solare (dalle 3 alle 2): gli eventi stanno all'ora dell'orologio.
        var change = new DateTime(2026, 10, 25);
        if (change >= from && change < to)
        {
            events.Add(E("Backup del server", change.AddHours(1.5), change.AddHours(4), location: "Sala server"));
            events.Add(E("Colazione con i soci", change.AddHours(9), change.AddHours(10)));
        }
        // Oggi, fra un minuto il promemoria (15 minuti prima dell'inizio).
        var soon = DateTime.Now.AddMinutes(16);
        soon = soon.AddSeconds(-soon.Second);
        if (soon >= from && soon < to)
            events.Add(E("Telefonata con il notaio", soon, soon.AddMinutes(30), reminder: true, minutes: 15, response: "accepted"));
        return new CalendarData { Calendars = calendars, Events = events.Where(e => e.End > from && e.Start < to).ToList(), From = from, To = to };
    }
}
#endif
