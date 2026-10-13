using System.Windows;
using System.Windows.Threading;

namespace Missiva.UI;

/// <summary>
/// Il calendario nella finestra principale, come il nuovo Outlook: il pulsante Calendario della colonna delle app
/// sostituisce la posta con CalendarView; Posta torna com'era. E i promemoria degli eventi come notifiche di Windows,
/// dagli eventi di oggi e domani riletti ogni mezz'ora (solo per gli account che hanno già dato il consenso: senza, nessuna
/// richiesta e nessuna finestra).
/// </summary>
public partial class MainWindow
{
    CalendarView? calendar;
    bool calendarShown;
    /// <summary>Le parti della posta nascoste mentre si vede il calendario (WebView2 non si lascia coprire da WPF).</summary>
    readonly List<(UIElement Element, Visibility Was)> mailParts = new();

    /// <summary>Da dove arrivano gli eventi: gli account; con --calendario-finto (solo Debug) eventi finti.</summary>
    Func<IMailProvider, DateTime, DateTime, CancellationToken, Task<CalendarData?>> CalendarSource =>
#if DEBUG
        // --calendario-lento: gli eventi finti arrivano un account alla volta, dopo 4, 6, 8... secondi, per vedere che cosa fa
        // la vista quando i dati di un account arrivano mentre la si guarda (audit del 7 ottobre, B-10).
        Environment.GetCommandLineArgs().Contains("--calendario-finto")
            ? Environment.GetCommandLineArgs().Contains("--calendario-lento")
                ? async (p, from, to, cancel) =>
                {
                    await Task.Delay(4000 + 2000 * Math.Max(0, Providers.ToList().IndexOf(p)), cancel);
                    return CalendarSamples.For(p.Account, from, to);
                }
                : (p, from, to, _) => Task.FromResult<CalendarData?>(CalendarSamples.For(p.Account, from, to)) :
#endif
        (p, from, to, cancel) => p.GetCalendarAsync(from, to, cancel);

#if DEBUG
    /// <summary>Prove ("calendario stato", "calendario scorri N" in MISSIVA_KEYS).</summary>
    internal CalendarView? TestCalendar => calendar;
#endif

    void OnCalendarRail(object sender, RoutedEventArgs e) => ShowCalendar(true);

    void OnMailRail(object sender, RoutedEventArgs e)
    {
        ShowCalendar(false);
        ShowPeople(false);
    }

    /// <summary>Ctrl+Maiusc+A, il "nuovo appuntamento" di Outlook: da qualunque vista apre il calendario e la finestra Nuovo evento.</summary>
    void NewEventShortcut()
    {
        if (accounts.Count == 0) return;
        ShowCalendar(true);
        calendar?.NewEventWhenReady();
    }

    void ShowCalendar(bool show, DateTime? day = null, CalendarMode? view = null)
    {
        if (only != null) return;
        if (show && peopleShown) ShowPeople(false);
        if (show && calendar == null)
        {
#if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--calendario-finto")) EventWindow.ScheduleSource = (_, emails, from, to) => Task.FromResult<List<Availability>?>(CalendarSamples.Schedule(emails, from, to));
#endif
            calendar = new CalendarView(() => Providers.ToList()) { Source = CalendarSource, Margin = new Thickness(0, 0, 0, 0) };
            calendar.AllowRequested = AllowCalendarAsync;
            calendar.EventsArrived += (p, data) => RememberEvents(p, data);
            System.Windows.Controls.Grid.SetColumn(calendar, 1);
            System.Windows.Controls.Grid.SetColumnSpan(calendar, 5);
            MainArea.Children.Add(calendar);
        }
        if (show != calendarShown)
        {
            calendarShown = show;
            if (show)
            {
                mailParts.Clear();
                foreach (UIElement part in MainArea.Children)
                    if (part != calendar && System.Windows.Controls.Grid.GetColumn(part) >= 1)
                    {
                        mailParts.Add((part, part.Visibility));
                        part.Visibility = Visibility.Collapsed;
                    }
                CommandArea.Visibility = Visibility.Collapsed;
                calendar!.Visibility = Visibility.Visible;
            }
            else
            {
                foreach (var (part, was) in mailParts) part.Visibility = was;
                mailParts.Clear();
                CommandArea.Visibility = Visibility.Visible;
                if (calendar != null) calendar.Visibility = Visibility.Collapsed;
                ShowFolders(); // la finestra può aver cambiato misura intanto
            }
            PaintRail(); // il pulsante dell'app aperta ha il fondo e il glifo colorato, come nel nuovo Outlook
            Log.Write(show ? "calendario aperto" : "posta aperta");
        }
        if (show) calendar!.Open(day, view);
    }

    /// <summary>
    /// "Consenti l'accesso al calendario": la finestra di Microsoft, solo al clic (nella prova solo il registro). Se
    /// l'organizzazione chiede un amministratore, lo si spiega con il link del consenso.
    /// </summary>
    async Task<bool> AllowCalendarAsync(IMailProvider provider)
    {
        try
        {
            await provider.AllowCalendarAsync();
            return true;
        }
        catch (Exception ex) when (MicrosoftAuth.Canceled(ex))
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("consenso al calendario", ex);
            ReadingView.ShowConsentError(this, "Accesso al calendario non consentito", ex, provider.Account, "usare il calendario");
            return false;
        }
    }

    // ---------------------------------------------------------------- promemoria

    /// <summary>Gli eventi letti, fin dove i promemoria di ogni account sono stati guardati e quelli già mostrati: ognuno una volta sola.</summary>
    readonly CalendarReminders reminders = new(DateTime.UtcNow);
    DispatcherTimer? reminderTimer;
    /// <summary>Quando rileggere gli eventi (UTC: la notte del ritorno all'ora solare un'ora dell'orologio aspetterebbe un'ora in più).</summary>
    DateTime nextCalendarFetch = DateTime.UtcNow.AddSeconds(20);

    void RememberEvents(IMailProvider provider, CalendarData data) => reminders.Remember(provider.Account.Id, data, DateTime.UtcNow);

    /// <summary>
    /// Gli eventi da tenere per i promemoria dopo una lettura (CalendarReminders.Upcoming): quelli dell'intervallo letto con
    /// il promemoria non ancora guardato, e di quelli tenuti da prima (fuori dall'intervallo) solo quelli il cui promemoria
    /// deve ancora arrivare. Prima quelli fuori dall'intervallo restavano tutti, anche di ieri: con Missiva aperta per giorni
    /// l'elenco, scorso ogni 30 secondi, cresceva di un giorno di eventi al giorno (audit del 7 ottobre, B-26).
    /// </summary>
    internal static List<CalendarEvent> Upcoming(IEnumerable<CalendarEvent>? before, CalendarData data, DateTime now, DateTime remindedUpTo) =>
        CalendarReminders.Upcoming(before, data, now, remindedUpTo);

    /// <summary>Ogni mezzo minuto i promemoria scaduti; ogni mezz'ora gli eventi di oggi e domani degli account con il consenso.</summary>
    void StartReminders()
    {
        if (only != null || reminderTimer != null) return;
        reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        reminderTimer.Tick += async (_, _) => await CheckRemindersAsync();
        reminderTimer.Start();
    }

    /// <summary>Un giro dei promemoria in corso: il timer non ne fa partire un altro mentre questo aspetta i calendari.</summary>
    bool checkingReminders;
    /// <summary>Gli account il cui calendario non si è letto, già scritti nel registro.</summary>
    readonly HashSet<string> calendarErrorTold = new();

    /// <summary>
    /// Quando rileggere gli eventi per i promemoria: ogni mezz'ora; dopo una lettura non riuscita fra un minuto se mancava la
    /// rete, fra cinque per un altro errore.
    /// </summary>
    internal static DateTime NextCalendarFetch(DateTime now, bool failed, bool offline) =>
        now + (!failed ? TimeSpan.FromMinutes(30) : offline ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5));

    /// <summary>La rete è cambiata (App.OnNetworkChanged): se l'ultima lettura dei calendari non era riuscita, al prossimo giro si rilegge.</summary>
    public void RetryCalendarSoon()
    {
        if (calendarErrorTold.Count > 0) nextCalendarFetch = DateTime.UtcNow;
    }

    /// <summary>
    /// Un giro alla volta: con la rete che torna (portatile appena sveglio) la lettura dei calendari può restare appesa più
    /// di 30 s, e un giro sovrapposto mostrava un promemoria che il primo, finendo, riportava indietro con la sua ora presa
    /// prima dell'attesa (così si ripeteva). L'ora si prende dopo la lettura. Un account il cui calendario non si è ancora
    /// letto (rete assente all'avvio) non perde i promemoria scaduti intanto: suonano alla prima lettura riuscita
    /// (CalendarReminders; audit dell'11 ottobre, C-56). I calendari nascosti nella vista (Settings.HiddenCalendars) non
    /// mandano promemoria. Quelli in ritardo di riunioni già finite non suonano: una riga nel registro dice quanti, senza
    /// titoli (scelte del 12 ottobre, C-56).
    /// </summary>
    async Task CheckRemindersAsync()
    {
        if (checkingReminders) return;
        checkingReminders = true;
        try
        {
            if (DateTime.UtcNow >= nextCalendarFetch)
            {
                var today = DateTime.Today;
                nextCalendarFetch = DateTime.UtcNow.AddMinutes(30); // intanto: la lettura può durare, e il timer non ne fa partire un'altra
                bool failed = false, offline = false;
                foreach (var p in Providers.Where(p => p.HasCalendar).ToList())
                {
                    try
                    {
                        if (await CalendarSource(p, today, today.AddDays(2), CancellationToken.None) is { } data) RememberEvents(p, data);
                        else reminders.NothingToRead(p.Account.Id, DateTime.UtcNow); // senza consenso: nulla da recuperare quando arriverà
                        calendarErrorTold.Remove(p.Account.Id);
                    }
                    catch (Exception ex)
                    {
                        failed = true;
                        offline |= Network.IsOffline(ex);
                        // Una riga per account finché non torna a leggersi, non una a ogni tentativo.
                        if (!calendarErrorTold.Add(p.Account.Id)) continue;
                        if (Network.IsOffline(ex)) Log.Write($"promemoria del calendario di {p.Account.Email}: non in linea, si riprova fra un minuto (non lo riscrivo finché non torna)");
                        else Log.Error($"promemoria del calendario di {p.Account.Email}", ex);
                    }
                }
                // Una lettura non riuscita si ripete presto: prima la prossima era comunque dopo mezz'ora, e con la rete assente
                // all'avvio (rete a 30 s dall'accesso a Windows) i promemoria di quella mezz'ora non arrivavano (audit, A-17).
                nextCalendarFetch = NextCalendarFetch(DateTime.UtcNow, failed, offline);
            }
            var due = reminders.Due(DateTime.UtcNow, out int ended, (id, e) => Store.Settings.HiddenCalendars.Contains(id + "|" + e.CalendarId));
            foreach (var reminder in due) Remind(reminder);
            if (ended > 0) Log.Write(CalendarReminders.EndedLine(ended));
        }
        finally
        {
            checkingReminders = false;
        }
    }

    /// <summary>
    /// Il promemoria come notifica di Windows, come in Outlook; nella versione di prova solo nel registro. Uno in ritardo ha
    /// una riga in più che lo dice, con l'ora in cui doveva arrivare (CalendarReminders.Text); uno puntuale è com'era.
    /// </summary>
    void Remind(CalendarReminders.DueReminder reminder)
    {
        var e = reminder.Event;
        var email = accounts.FirstOrDefault(a => a.Provider.Account.Id == reminder.AccountId)?.Header ?? "";
        var (when, late) = CalendarReminders.Text(e, reminder.Was, DateTime.Today);
        if (Testing)
        {
            Log.Write($"prova: notificherei il promemoria di \"{e.Subject}\" {when}{(late != null ? ", " + late : "")} ({email})");
            return;
        }
        try
        {
            var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                .AddText(e.Subject.Length > 0 ? e.Subject : "(nessun titolo)")
                .AddText(when + (e.Location.Length > 0 ? " · " + e.Location : ""));
            if (late != null) toast.AddText(late);
            toast.AddAttributionText(email);
            App.Show(toast);
            Log.Write($"promemoria{(late != null ? " in ritardo" : "")} di {Log.Private(e.Subject)} ({email})");
        }
        catch (Exception ex)
        {
            Log.Error("promemoria del calendario", ex);
        }
    }
}
