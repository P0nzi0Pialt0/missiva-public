namespace Missiva.UI;

/// <summary>
/// I promemoria del calendario da mostrare, ognuno una volta sola: gli eventi letti per account (dal giro dei promemoria e
/// dalla vista del calendario), fin dove i promemoria di ogni account sono stati guardati, e quelli già mostrati.
/// Prima c'era un'ora sola per tutti gli account, che avanzava a ogni giro anche senza gli eventi: i promemoria scaduti
/// mentre il calendario non si leggeva (rete assente all'avvio, PC riaperto dopo due giorni) non suonavano mai. Ora l'ora
/// di un account avanza solo finché gli eventi letti coprono quel momento, e alla prima lettura riuscita i promemoria del
/// buco suonano, in ritardo. I confronti sono fra istanti (UTC), non fra ore dell'orologio: la notte del ritorno all'ora
/// solare un promemoria suonava alla prima delle due ore uguali, un'ora prima del dovuto (audit dell'11 ottobre, C-56).
/// Dal 13 ottobre 2026 un promemoria in ritardo ha un tetto: suona solo se la riunione non è ancora finita, e lo dice; quelli
/// delle riunioni finite non suonano e si contano (scelte del 12 ottobre, C-56). Uno puntuale suona com'era.
/// </summary>
internal sealed class CalendarReminders(DateTime start)
{
    /// <summary>
    /// Oltre questo ritardo un promemoria è "in ritardo": i giri sono ogni 30 secondi, e con la rete appena tornata la
    /// lettura dei calendari può tenerne fermo uno per più di mezzo minuto. Sotto, è puntuale e nulla cambia.
    /// </summary>
    internal static readonly TimeSpan Late = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Un promemoria da mostrare: di quale account, per quale evento e, solo se arriva in ritardo (più di Late), l'ora
    /// dell'orologio in cui doveva arrivare.
    /// </summary>
    internal readonly record struct DueReminder(string AccountId, CalendarEvent Event, DateTime? Was = null)
    {
        public void Deconstruct(out string accountId, out CalendarEvent e) => (accountId, e) = (AccountId, Event);
    }

    /// <summary>Gli eventi con un promemoria ancora da guardare, per account.</summary>
    readonly Dictionary<string, List<CalendarEvent>> upcoming = new();
    /// <summary>Per account, fin dove i promemoria sono stati guardati con gli eventi di quel momento; senza, dall'avvio.</summary>
    readonly Dictionary<string, DateTime> looked = new();
    /// <summary>Per account, fin dove arrivano gli eventi letti: la fine (ora dell'orologio) dell'ultimo intervallo letto che conteneva il momento della lettura.</summary>
    readonly Dictionary<string, DateTime> knownTo = new();
    /// <summary>I promemoria già mostrati (account, evento, ora): gli ultimi 2000.</summary>
    readonly RecentKeys shown = new(2000);
    readonly DateTime started = Instant(start);

    /// <summary>
    /// L'istante (UTC) di un'ora dell'orologio. Un'ora che c'è due volte (la notte del ritorno all'ora solare) vale come la
    /// seconda, quella solare; una che non esiste non dà errori.
    /// </summary>
    internal static DateTime Instant(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Local).ToUniversalTime(),
    };

    DateTime Looked(string accountId) => looked.GetValueOrDefault(accountId, started);

    /// <summary>
    /// Gli eventi da tenere dopo una lettura: quelli dell'intervallo letto con il promemoria non ancora guardato (da
    /// <paramref name="lookedUpTo"/>, o dall'ultimo minuto se è più vecchio: prima sempre dall'ultimo minuto, e un promemoria
    /// scaduto durante un buco non entrava nemmeno) e fino a due giorni; di quelli tenuti da prima, fuori dall'intervallo,
    /// solo quelli con il promemoria non ancora guardato (audit del 7 ottobre, B-26).
    /// </summary>
    internal static List<CalendarEvent> Upcoming(IEnumerable<CalendarEvent>? before, CalendarData data, DateTime now, DateTime lookedUpTo)
    {
        DateTime instant = Instant(now), since = Instant(lookedUpTo);
        var from = since < instant.AddMinutes(-1) ? since : instant.AddMinutes(-1);
        var soon = data.Events.Where(e => e.ReminderAt is { } at && Instant(at) > from && Instant(at) < instant.AddDays(2));
        var kept = (before ?? []).Where(e => (e.Start < data.From || e.Start >= data.To) && e.ReminderAt is { } at && Instant(at) > since);
        return kept.Concat(soon).ToList();
    }

    /// <summary>Gli eventi appena letti di un account, dal giro dei promemoria o dalla vista del calendario.</summary>
    /// <param name="now">L'istante della lettura (UTC).</param>
    public void Remember(string accountId, CalendarData data, DateTime now)
    {
        upcoming[accountId] = Upcoming(upcoming.GetValueOrDefault(accountId), data, now, Looked(accountId));
        var clock = Instant(now).ToLocalTime();
        if (data.From <= clock && clock < data.To) knownTo[accountId] = data.To;
    }

    /// <summary>
    /// L'account non ha eventi da leggere (manca il consenso al calendario): fin qui non c'è nulla da recuperare. Senza, il
    /// giorno in cui il consenso arriva suonerebbero insieme tutti i promemoria dall'avvio.
    /// </summary>
    public void NothingToRead(string accountId, DateTime now)
    {
        if (!upcoming.ContainsKey(accountId)) looked[accountId] = Instant(now);
    }

    /// <summary>
    /// I promemoria arrivati e non ancora mostrati, da mostrare adesso: ognuno una volta sola. Quelli dei calendari nascosti
    /// (<paramref name="hidden"/>) contano come mostrati. L'ora di un account avanza solo se gli eventi letti coprono ancora
    /// adesso: altrimenti resta dov'è, e la prossima lettura riuscita porta i promemoria scaduti nel frattempo.
    /// Un promemoria in ritardo (più di Late) si mostra solo se la riunione non è ancora finita a quell'istante, con l'ora in
    /// cui doveva arrivare (DueReminder.Was); quelli delle riunioni finite non si mostrano, contano come mostrati e sono in
    /// <paramref name="ended"/>: prima, riaprendo il PC il lunedì, suonavano insieme anche quelli del sabato.
    /// </summary>
    /// <param name="now">L'istante di adesso (UTC).</param>
    /// <param name="ended">Quanti promemoria in ritardo non sono stati mostrati perché la riunione era già finita.</param>
    public List<DueReminder> Due(DateTime now, out int ended, Func<string, CalendarEvent, bool>? hidden = null)
    {
        var instant = Instant(now);
        var clock = instant.ToLocalTime();
        var due = new List<DueReminder>();
        ended = 0;
        foreach (var (accountId, events) in upcoming)
        {
            var from = Looked(accountId);
            foreach (var e in events)
            {
                if (e.ReminderAt is not { } at || Instant(at) is var when && (when <= from || when > instant)) continue;
                var key = $"{accountId}|{e.Id}|{when.Ticks}";
                if (shown.Contains(key)) continue;
                shown.Add(key);
                if (hidden?.Invoke(accountId, e) == true) continue;
                bool late = instant - when > Late;
                if (late && Over(e, instant)) ended++;
                else due.Add(new(accountId, e, late ? at : null));
            }
            if (knownTo.TryGetValue(accountId, out var to) && clock < to) looked[accountId] = instant;
        }
        return due;
    }

    public List<DueReminder> Due(DateTime now, Func<string, CalendarEvent, bool>? hidden = null) => Due(now, out _, hidden);

    /// <summary>
    /// La riunione è finita a quell'istante. Una fine in un'ora che c'è due volte vale come la seconda (Instant): quella
    /// notte una riunione può contare come non finita per un'ora in più, mai il contrario. Un evento senza una fine dopo
    /// l'inizio finisce quando comincia.
    /// </summary>
    internal static bool Over(CalendarEvent e, DateTime instant) => Instant(e.End > e.Start ? e.End : e.Start) <= instant;

    /// <summary>
    /// Il testo di un promemoria: quando è l'evento, com'era ("alle 09:15" se è oggi, altrimenti il giorno e le ore, o "tutto
    /// il giorno"), e solo per un promemoria in ritardo una riga in più che lo dice, con l'ora in cui doveva arrivare
    /// ("Promemoria in ritardo: era alle 09:00"; di un altro giorno, "era ieri alle 23:45" o "era venerdì 16 ottobre alle
    /// 17:15").
    /// </summary>
    /// <param name="was">L'ora dell'orologio in cui il promemoria doveva arrivare (DueReminder.Was); null se è puntuale.</param>
    internal static (string When, string? Late) Text(CalendarEvent e, DateTime? was, DateTime today)
    {
        var when = e.AllDay ? T("tutto il giorno") : e.Start.Date == today.Date ? T("alle {0}", e.Start.ToString("HH:mm")) : Calendars.When(e);
        if (was is not { } at) return (when, null);
        var time = at.ToString("HH:mm");
        var late = at.Date == today.Date ? T("Promemoria in ritardo: era alle {0}", time)
            : at.Date == today.Date.AddDays(-1) ? T("Promemoria in ritardo: era ieri alle {0}", time)
            : T("Promemoria in ritardo: era {0} alle {1}", at.ToString("dddd d MMMM", Lingua.Culture), time);
        return (when, late);
    }

    /// <summary>La riga del registro per i promemoria in ritardo non mostrati: quanti, mai i titoli delle riunioni.</summary>
    internal static string EndedLine(int ended) =>
        ended == 1 ? "promemoria del calendario: uno in ritardo non mostrato, la riunione era già finita"
                   : $"promemoria del calendario: {ended} in ritardo non mostrati, le riunioni erano già finite";

    /// <summary>Quanti eventi sono tenuti in tutto (per le prove: non devono crescere con i giorni).</summary>
    internal int Kept => upcoming.Values.Sum(e => e.Count);
}
