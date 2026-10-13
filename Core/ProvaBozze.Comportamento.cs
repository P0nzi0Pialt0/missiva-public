#if DEBUG
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MimeKit;

namespace Missiva;

/// <summary>
/// Audit dell'11 ottobre, codice, primo gruppo (docs\audit\ESITO.md): i difetti di comportamento, C-50..C-63. Solo dati
/// finti, server finti su 127.0.0.1 e orologi simulati; nessuna finestra: delle parti che stanno nelle finestre si provano
/// le funzioni che decidono, e si legge il sorgente per vedere che le finestre le chiamino.
/// Con "Missiva.exe --prova-audit risultato.txt comportamento" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void BehaviourAuditTests()
    {
        Report.Add("--- Audit dell'11 ottobre, codice, primo gruppo: difetti di comportamento (C-50..C-63)");
        BehDurations();
        BehFlagReminders();
        BehForeignTimes();
        BehSearchTerms();
        BehScheduledSendAsync().GetAwaiter().GetResult();
        BehCalendarReminders();
        BehFailedMoveAsync().GetAwaiter().GetResult();
        BehUndoQueuedAsync().GetAwaiter().GetResult();
        BehSignatureChoice();
        BehOutboxOnRemoval();
        BehFolderWindows();
        BehRulesMeasureAsync().GetAwaiter().GetResult();
        BehDisposedProviderAsync().GetAwaiter().GetResult();
        BehSecondaryTextMeasure();
    }

    /// <summary>Un sorgente del repository, o null fuori dalla cartella dei sorgenti (versione pubblicata altrove).</summary>
    static string? BehSource(params string[] parts) =>
        SourceRoot() is { } root && Path.Combine([root, .. parts]) is var path && File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>
    /// C-57: le durate misurate come differenza fra due ore dell'orologio. La notte del ritorno all'ora solare la differenza
    /// è negativa per un'ora: quanto, con il fuso dell'Italia, e che nei sorgenti non resti nessuna durata fatta così.
    /// </summary>
    static void BehDurations()
    {
        TimeZoneInfo? rome = null;
        try { rome = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); } catch (Exception) { }
        if (rome != null)
        {
            var before = new DateTime(2026, 10, 25, 0, 50, 0, DateTimeKind.Utc); // le 02:50, ancora ora legale
            var after = before.AddMinutes(20);                                    // le 02:10, già ora solare
            var onClock = TimeZoneInfo.ConvertTimeFromUtc(after, rome) - TimeZoneInfo.ConvertTimeFromUtc(before, rome);
            var spring = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2027, 3, 28, 1, 10, 0, DateTimeKind.Utc), rome) - TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2027, 3, 28, 0, 50, 0, DateTimeKind.Utc), rome);
            Report.Add($"      (C-57, misura: il 25 ottobre 2026 fra le 02:50 e le 02:10 di venti minuti dopo l'orologio dice {onClock.TotalMinutes:0} minuti; " +
                       $"il 28 marzo 2027 fra le 01:50 e le 03:10 di venti minuti dopo ne dice {spring.TotalMinutes:0})");
        }
        if (SourceRoot() is not { } root) return;
        var found = new List<string>();
        foreach (var dir in new[] { "Core", "UI" })
            foreach (var file in Directory.GetFiles(Path.Combine(root, dir), "*.cs"))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("ProvaBozze") || name.StartsWith("DevTools") || name.EndsWith("Samples.cs")) continue;
                int n = 0;
                foreach (var line in File.ReadLines(file))
                {
                    n++;
                    // "finestra pronta N ms dopo l'avvio": due ore dell'orologio prese a un secondo di distanza, solo per il registro.
                    if (Regex.IsMatch(line, @"DateTime\.Now\s*-\s|-\s*DateTime\.Now\b") && !line.Contains("StartTime")) found.Add($"{dir}\\{name}:{n}");
                }
            }
        Check(found.Count == 0,
              "C-57, le durate non si misurano più come differenza fra due ore dell'orologio (DateTime.Now): rete assente da meno di un minuto, ricerca IMAP riusata, finestra ferma e copia degli ultimi 30 giorni, " +
              "pannello dei filtri, salvataggio automatico delle bozze, età dei file temporanei e della copia dei contatti sono in UTC" + (found.Count == 0 ? "" : $" (restano: {string.Join(", ", found)})"));
    }

    /// <summary>C-58: il promemoria del contrassegno di un account che non è fra gli account resta, invece di azzerarsi senza notifica.</summary>
    static void BehFlagReminders()
    {
        var now = new DateTime(2026, 10, 12, 9, 0, 0);
        DateTime past = now.AddMinutes(-5), future = now.AddHours(3);
        var flags = new List<FlagDate>
        {
            new() { AccountId = "presente", Key = "a", Reminder = past },
            new() { AccountId = "assente", Key = "b", Reminder = past },
            new() { AccountId = "presente", Key = "c", Reminder = future },
        };
        var due = UI.MainWindow.TakeDueReminders(flags, now, id => id == "presente");
        bool kept = flags[1].Reminder == past;
        var later = UI.MainWindow.TakeDueReminders(flags, now, _ => true);
        var none = UI.MainWindow.TakeDueReminders(flags, now, _ => true);
        Check(due is [{ Key: "a" }] && flags[0].Reminder == null && kept && flags[2].Reminder == future && later is [{ Key: "b" }] && none.Count == 0,
              "C-58, promemoria dei contrassegni: quello arrivato di un account che c'è si notifica una volta sola; quello di un account che non è fra gli account resta nella voce " +
              "(prima si azzerava senza notifica) e si notifica quando l'account c'è");
    }

    /// <summary>
    /// C-59: un orario scritto in un altro fuso a un'ora che lì non esiste (la notte in cui comincia l'ora legale). L'invito
    /// (.ics con TZID) e l'evento di Graph ({dateTime, timeZone}) si leggono, con l'ora spostata avanti del salto.
    /// </summary>
    static void BehForeignTimes()
    {
        static string Ics(string start, string end) =>
            "BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:prova-c59@example.com\r\nDTSTAMP:20260301T080000Z\r\n" + start + "\r\n" + end +
            "\r\nSUMMARY:Riunione di prova\r\nORGANIZER;CN=Maria Rossi:mailto:maria.rossi@example.com\r\nATTENDEE;CN=Mario Rossi:mailto:mario.rossi@example.com\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        // New York, 8 marzo 2026: dalle 02:00 si passa alle 03:00. Le 02:30 non esistono: valgono le 03:30, cioè le 07:30 UTC.
        var wanted = new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc).ToLocalTime();
        var inGap = Invitation.Parse(Ics("DTSTART;TZID=America/New_York:20260308T023000", "DTEND;TZID=America/New_York:20260308T033000"));
        var afterGap = Invitation.Parse(Ics("DTSTART;TZID=America/New_York:20260308T033000", "DTEND;TZID=America/New_York:20260308T043000"));
        var beforeGap = Invitation.Parse(Ics("DTSTART;TZID=America/New_York:20260308T013000", "DTEND;TZID=America/New_York:20260308T033000"));
        Check(inGap?.Start == wanted && afterGap?.Start == wanted && beforeGap?.Start == wanted.AddHours(-1),
              "C-59, un invito da New York alle 02:30 della notte in cui lì comincia l'ora legale (un'ora che non esiste): la scheda c'è, con l'ora spostata avanti del salto " +
              $"(letto {(inGap == null ? "nessun invito" : inGap.Start.ToString("dd/MM HH:mm"))}, atteso {wanted:dd/MM HH:mm}); prima l'invito restava senza scheda né pulsanti");

        string? thrown = null;
        DateTime graph = default, prague = default;
        try
        {
            graph = Calendars.LocalTime(new JsonObject { ["dateTime"] = "2026-03-08T02:30:00.0000000", ["timeZone"] = "Eastern Standard Time" });
            // Praga, 29 marzo 2026: le 02:30 non esistono, valgono le 03:30, cioè le 01:30 UTC.
            prague = Calendars.LocalTime(new JsonObject { ["dateTime"] = "2026-03-29T02:30:00.0000000", ["timeZone"] = "Central Europe Standard Time" });
        }
        catch (Exception ex) { thrown = ex.GetType().Name; }
        // Tutte le altre ore si leggono come prima: un anno di mezz'ore in tre fusi, confrontate con la conversione di .NET.
        int compared = 0, different = 0, gaps = 0;
        foreach (var id in new[] { "Eastern Standard Time", "Central Europe Standard Time", "AUS Eastern Standard Time", "UTC" })
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            for (var t = new DateTime(2026, 1, 1); t < new DateTime(2027, 1, 1); t = t.AddMinutes(30))
            {
                if (zone.IsInvalidTime(t))
                {
                    gaps++;
                    try { Calendars.FromZone(t, zone); } catch (Exception) { different++; }
                    continue;
                }
                compared++;
                if (Calendars.FromZone(t, zone) != TimeZoneInfo.ConvertTime(t, zone, TimeZoneInfo.Local)) different++;
            }
        }
        Check(thrown == null && graph == wanted && prague == new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc).ToLocalTime() && different == 0 && gaps > 0,
              "C-59, un evento di Graph con un orario in un altro fuso a un'ora che lì non esiste: la lettura non fallisce più" + (thrown == null ? "" : $" (eccezione {thrown})") +
              $"; le altre ore sono quelle di prima ({compared} mezz'ore del 2026 in quattro fusi uguali alla conversione di .NET, {gaps} che non esistono lette senza errori)");
        if (BehSource("Core", "GraphProvider.cs") is { } source)
            Check(!source.Contains("TimeZoneInfo.ConvertTime(") && source.Contains("Calendars.FromZone(time, zone)") && !BehSource("Core", "Invitation.cs")!.Contains("TimeZoneInfo.ConvertTime("),
                  "C-59, gli inviti di Exchange (GraphProvider.EventInviteAsync) e gli .ics passano dalla stessa conversione (Calendars.FromZone): nessuna chiamata diretta a TimeZoneInfo.ConvertTime");
    }

    /// <summary>
    /// C-62: i filtri della ricerca scritti a mano. Una virgoletta mai chiusa, una virgoletta o una parentesi dentro un
    /// valore, una data senza anno che quest'anno deve ancora venire.
    /// </summary>
    static void BehSearchTerms()
    {
        var open = SearchTerms.Parse("da:\"Mario fattura");
        var openFree = SearchTerms.Parse("misura 3\" tubo da:mario");
        var closed = SearchTerms.Parse("fattura da:\"Mario Rossi\" ha:allegati");
        Check(open is { From: "Mario", Text: "fattura" } && openFree is { From: "mario", Text: "misura 3\" tubo" } && closed is { From: "Mario Rossi", Text: "fattura", HasAttachments: true },
              "C-62, una virgoletta mai chiusa non mette nel filtro tutto il resto del testo: da:\"Mario fattura cerca fattura da Mario " +
              $"(letto: da \"{open.From}\", parole \"{open.Text}\"); con le virgolette chiuse come prima");

        var odd = new SearchTerms { Text = "fattura", From = "Rossi \"Studio Srl", Subject = "saldo \"finale" };
        var written = odd.ToString();
        var read = SearchTerms.Parse(written);
        var again = SearchTerms.Parse(read.ToString());
        Check(read is { Text: "fattura", From: "Rossi Studio Srl", Subject: "saldo finale" } && again == read,
              $"C-62, un valore con una virgoletta dentro si scrive e si rilegge uguale ({written}): prima il filtro dopo finiva dentro quello prima");

        var gmail = new SearchTerms { Text = "fattura", From = "Rossi (Studio", To = "a)b", Subject = "saldo \"finale" }.ToGmail();
        var empty = new SearchTerms { Text = "fattura", From = "()" }.ToGmail();
        Check(gmail == "fattura from:(Rossi Studio) to:(ab) subject:(saldo finale)" && empty == "fattura",
              $"C-62, per Gmail i valori dei filtri vanno senza parentesi né virgolette spaiate ({gmail})");

        var january = new DateTime(2027, 1, 10);
        var october = new DateTime(2026, 10, 12);
        Check(SearchTerms.ParseDate("25/12", january) == new DateTime(2026, 12, 25) && SearchTerms.ParseDate("10/1", january) == january && SearchTerms.ParseDate("11/1", january) == new DateTime(2026, 1, 11)
              && SearchTerms.ParseDate("25/10", october) == new DateTime(2025, 10, 25) && SearchTerms.ParseDate("12/10", october) == october && SearchTerms.ParseDate("29/2", october) == new DateTime(2024, 2, 29)
              && SearchTerms.ParseDate("31/2", october) == null && SearchTerms.ParseDate("25/12/2026", january) == new DateTime(2026, 12, 25) && SearchTerms.ParseDate("25/12/26") == new DateTime(2026, 12, 25)
              && SearchTerms.ParseDate("2026-10-25") == new DateTime(2026, 10, 25) && SearchTerms.ParseDate("domani") == null,
              "C-62, una data senza anno è l'ultima volta che quel giorno c'è stato: dal:25/12 scritto il 10 gennaio 2027 è il 25 dicembre 2026 (prima il 25 dicembre 2027, nel futuro, e nessun risultato); " +
              "oggi resta oggi, il 29/2 è quello dell'ultimo anno bisestile, con l'anno scritto nulla cambia");
    }

    /// <summary>
    /// C-61: l'invio pianificato quando la connessione SMTP cade dopo che il server ha ricevuto tutto il messaggio e prima
    /// della sua risposta. Il server SMTP finto tiene il messaggio e chiude (DropAfterData): quante volte lo riceve nei
    /// controlli dei minuti dopo. Gli errori di prima del messaggio (la connessione che cade a MAIL) si riprovano come
    /// sempre, e un rifiuto del server dopo DATA resta un rifiuto.
    /// </summary>
    static async Task BehScheduledSendAsync()
    {
        using var fake = new FakeImap();
        using var smtp = new FakeSmtp();
        GmailProvider.TestPort = fake.Port;
        GmailProvider.TestSmtpPort = smtp.Port;
        GmailProvider.TestSmtpCertificate = smtp.Certificate.GetCertHashString();
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "gmail-c61" };
        using var gmail = new GmailProvider(account);
        var kept = Store.Settings.ScheduledSends.ToList();
        try
        {
            Store.Settings.ScheduledSends.Clear();
            IMailProvider? Provider(string id) => id == account.Id ? gmail : null;
            var when = new DateTime(2026, 10, 12, 8, 0, 0);
            var problems = new List<string>();
            async Task<int[]> ThreeMinutesAsync(string subject)
            {
                var draft = await gmail.SaveDraftAsync(new OutgoingMessage { To = To, Subject = subject, Html = "<p>Una volta sola</p>", Text = "Una volta sola" });
                ScheduledSending.Add(account, draft.Message.StableId, when, subject);
                var sent = new int[3];
                for (int minute = 0; minute < 3; minute++) sent[minute] = await ScheduledSending.RunDueAsync(when.AddMinutes(minute), Provider, (_, why) => problems.Add(why));
                return sent;
            }
            int Arrived(string subject) => smtp.Received.Count(raw => MimeMessage.Load(new MemoryStream(raw)).Subject == subject);
            int InDrafts(string subject) => fake.Stored.Count(m => !m.Has("\\Deleted") && MimeMessage.Load(new MemoryStream(m.Raw)).Subject == subject);

            // Il server ha tutto il messaggio e la connessione cade prima del 250, per tre minuti di fila (un server lento a rispondere).
            smtp.DropAfterData = 3;
            var dropped = await ThreeMinutesAsync("Offerta di prova");
            int copies = Arrived("Offerta di prova");
            smtp.DropAfterData = 0;
            Check(copies == 1 && dropped.Sum() == 0 && Store.Settings.ScheduledSends.Count == 0 && problems is [var told] && told.Contains("non si sa se è partito") && told.Contains("non riprova da sola")
                  && told.Contains("Bozze") && InDrafts("Offerta di prova") == 1,
                  $"C-61, invio pianificato con la connessione SMTP che cade dopo il messaggio e prima della risposta: il server lo riceve {(copies == 1 ? "una volta" : copies + " volte")} in tre minuti (prima una al minuto), " +
                  "l'invio esce dall'elenco con un avviso che dice che forse è partito, e la bozza resta nelle Bozze" + (problems.Count == 1 ? "" : $" ({problems.Count} avvisi)"));

            // La connessione che cade prima del messaggio (a MAIL FROM): un errore di rete come sempre, si riprova il minuto dopo.
            problems.Clear();
            smtp.DropOnce = "MAIL";
            var early = await ThreeMinutesAsync("Sollecito di prova");
            Check(early is [0, 1, 0] && Arrived("Sollecito di prova") == 1 && Store.Settings.ScheduledSends.Count == 0 && problems is [var waiting] && waiting.Contains("non è ancora partito") && InDrafts("Sollecito di prova") == 0,
                  "C-61, la connessione che cade prima del messaggio resta un errore di rete: l'invio resta in elenco, un avviso, e al minuto dopo parte una volta sola");

            // Il server risponde dopo DATA e rifiuta: non è partito di sicuro, come prima.
            problems.Clear();
            smtp.RefuseData = "554 5.7.1 messaggio rifiutato (prova)";
            var refused = await ThreeMinutesAsync("Rifiutato di prova");
            Check(refused.Sum() == 0 && Arrived("Rifiutato di prova") == 0 && Store.Settings.ScheduledSends.Count == 0 && problems is [var no] && no.Contains("non è partito:") && !no.Contains("non si sa"),
                  "C-61, un rifiuto del server dopo DATA (554) resta un rifiuto: \"non è partito\", con il motivo, e la bozza nelle Bozze");
        }
        finally
        {
            GmailProvider.TestPort = null;
            GmailProvider.TestSmtpPort = null;
            GmailProvider.TestSmtpCertificate = null;
            Store.Settings.ScheduledSends.Clear();
            Store.Settings.ScheduledSends.AddRange(kept);
            Store.Save();
        }

        // Lo stesso con un account IMAP (Invia, non pianificato): l'errore dice che forse è partito, e non è "non in linea",
        // quindi il messaggio non va nella Posta in uscita, che al ritorno della rete lo manderebbe di nuovo.
        ImapProvider.TestPlain = true;
        ImapProvider.TestCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            using var server = new FakeImap(new Flavor(Gmail: false));
            using var imap = new ImapProvider(new MailAccount
            {
                Kind = AccountKind.Imap, Email = "mario@studio-prova.it",
                Imap = new ImapServer { ImapHost = "127.0.0.1", ImapPort = server.Port, SmtpHost = "127.0.0.1", SmtpPort = smtp.Port, SmtpSecurity = MailSecurity.StartTls,
                                        UserName = "mario@studio-prova.it", ProtectedPassword = Store.Protect("x") },
            });
            smtp.Received.Clear();
            smtp.DropAfterData = 1;
            Exception? error = null;
            try { await imap.SendAsync(new OutgoingMessage { To = To, Subject = "Preventivo di prova", Html = "<p>Ciao</p>", Text = "Ciao" }); }
            catch (Exception ex) { error = ex; }
            var timeout = new MailException("x", new IOException("y", new SocketException(10060)));
            var maybe = new MailException("x", new IOException("y", new SocketException(10060))) { MaybeSent = true };
            Check(error is MailException { MaybeSent: true } && !Network.IsOffline(error) && smtp.Received.Count == 1 && server.Folders[server.SentFolder].Count == 0
                  && Network.IsOffline(timeout) && !Network.IsOffline(maybe) && MailException.MaybeSentOf(new AggregateException(maybe)) && !MailException.MaybeSentOf(timeout)
                  && UI.ComposeWindow.SendFailure(error!).Body.Contains("non si sa se è partito"),
                  "C-61, Invia con un account IMAP e la connessione che cade dopo il messaggio: l'errore dice che forse è partito" + (error == null ? " (nessun errore)" : $" ({error.GetType().Name}: {error.Message})") +
                  ", non conta come \"non in linea\" (non va nella Posta in uscita, che lo ripeterebbe), e la finestra dell'errore lo dice");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            ImapProvider.TestCertificate = null;
        }
    }

    /// <summary>I promemoria del calendario com'erano prima (MainWindow.Calendar.cs al commit 05e9616): un'ora sola per tutti, ore dell'orologio.</summary>
    sealed class BehOldReminders(DateTime start)
    {
        readonly Dictionary<string, List<CalendarEvent>> upcoming = new();
        DateTime remindedUpTo = start;

        public void Remember(string accountId, CalendarData data, DateTime now)
        {
            var soon = data.Events.Where(e => e.ReminderAt is { } at && at > now.AddMinutes(-1) && at < now.AddDays(2));
            var kept = (upcoming.GetValueOrDefault(accountId) ?? []).Where(e => (e.Start < data.From || e.Start >= data.To) && e.ReminderAt is { } at && at > remindedUpTo);
            upcoming[accountId] = kept.Concat(soon).ToList();
        }

        public List<CalendarEvent> Due(DateTime now)
        {
            var due = upcoming.Values.SelectMany(e => e).Where(e => e.ReminderAt is { } at && at > remindedUpTo && at <= now).ToList();
            if (now > remindedUpTo) remindedUpTo = now;
            return due;
        }
    }

    /// <summary>
    /// Un pezzo di giornata con i promemoria, come li fa CheckRemindersAsync: un giro ogni 30 secondi (non mentre il PC è
    /// fermo), la lettura degli eventi di oggi e domani ogni mezz'ora (dopo una non riuscita, fra un minuto), poi i
    /// promemoria arrivati. Restituisce quali promemoria hanno suonato e a che istante, con il codice di adesso o con
    /// quello di prima.
    /// </summary>
    /// <param name="reads">La lettura riesce a quell'istante.</param>
    /// <param name="awake">Il PC è acceso a quell'istante (di base sempre).</param>
    static List<(string Id, DateTime At)> BehReminderDay(bool old, List<CalendarEvent> calendar, DateTime start, DateTime end, Func<DateTime, bool> reads, Func<DateTime, bool>? awake = null)
    {
        var rung = new List<(string, DateTime)>();
        var now = new UI.CalendarReminders(start);
        var before = new BehOldReminders(start.ToLocalTime());
        var nextFetch = start.AddSeconds(20);
        for (var t = start.AddSeconds(30); t <= end; t = t.AddSeconds(30))
        {
            if (awake?.Invoke(t) == false) continue;
            var clock = t.ToLocalTime();
            if (t >= nextFetch)
            {
                bool ok = reads(t);
                if (ok)
                {
                    var data = new CalendarData { Events = calendar.Where(e => e.Start >= clock.Date && e.Start < clock.Date.AddDays(2)).ToList(), From = clock.Date, To = clock.Date.AddDays(2) };
                    if (old) before.Remember("a", data, clock);
                    else now.Remember("a", data, t);
                }
                nextFetch = t + (ok ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(1));
            }
            foreach (var e in old ? before.Due(clock) : now.Due(t).Select(d => d.Event).ToList()) rung.Add((e.Id, t));
        }
        return rung;
    }

    /// <summary>
    /// C-56: i promemoria del calendario quando gli eventi non si sono potuti leggere (rete assente all'avvio, PC riaperto il
    /// lunedì) e la notte del 25 ottobre 2026, con orologi finti. Ogni caso con il codice di prima (BehOldReminders) e con
    /// quello di adesso (CalendarReminders).
    /// </summary>
    static void BehCalendarReminders()
    {
        static CalendarEvent Event(string id, DateTime start, int minutes = 15) => new() { Id = id, Subject = "Riunione di prova", Start = start, End = start.AddHours(1), ReminderOn = true, ReminderMinutes = minutes };
        static DateTime Utc(int month, int day, int hour, int minute) => new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();
        static string Told(List<(string Id, DateTime At)> rung) => rung.Count == 0 ? "nessuno" : string.Join(", ", rung.Select(r => $"{r.Id} alle {r.At.ToLocalTime():HH:mm}"));

        // Lunedì 12 ottobre: Missiva parte alle 08:55 senza rete, che torna alle 09:05. La riunione delle 09:15 ha il promemoria alle 09:00.
        var monday = new List<CalendarEvent> { Event("riunione-0915", new DateTime(2026, 10, 12, 9, 15, 0)), Event("riunione-1000", new DateTime(2026, 10, 12, 10, 0, 0)), Event("riunione-1200", new DateTime(2026, 10, 12, 12, 0, 0)) };
        var back = Utc(10, 12, 9, 5);
        var offlineOld = BehReminderDay(true, monday, Utc(10, 12, 8, 55), Utc(10, 12, 12, 30), t => t >= back);
        var offlineNew = BehReminderDay(false, monday, Utc(10, 12, 8, 55), Utc(10, 12, 12, 30), t => t >= back);
        Check(offlineNew.Select(r => r.Id).SequenceEqual(["riunione-0915", "riunione-1000", "riunione-1200"]) && offlineNew[0].At >= back && offlineNew[0].At < back.AddMinutes(2)
              && offlineNew[1].At == Utc(10, 12, 9, 45) && offlineNew[2].At == Utc(10, 12, 11, 45) && !offlineOld.Any(r => r.Id == "riunione-0915"),
              $"C-56, Missiva avviata alle 08:55 senza rete, che torna alle 09:05: il promemoria delle 09:00 per la riunione delle 09:15 suona alla prima lettura riuscita ({Told(offlineNew)}); prima non suonava mai ({Told(offlineOld)})");

        // Venerdì 16 ottobre il PC si ferma alle 18:00 e riparte lunedì 19 alle 08:55, con la rete: la riunione di lunedì alle 09:00 non era fra gli eventi letti venerdì (oggi e domani).
        var week = new List<CalendarEvent> { Event("venerdi-1730", new DateTime(2026, 10, 16, 17, 30, 0)), Event("sabato-1000", new DateTime(2026, 10, 17, 10, 0, 0)), Event("lunedi-0900", new DateTime(2026, 10, 19, 9, 0, 0)), Event("lunedi-1100", new DateTime(2026, 10, 19, 11, 0, 0)) };
        DateTime asleep = Utc(10, 16, 18, 0), awake = Utc(10, 19, 8, 55);
        var weekendOld = BehReminderDay(true, week, Utc(10, 16, 17, 0), Utc(10, 19, 11, 30), _ => true, t => t <= asleep || t >= awake);
        var weekendNew = BehReminderDay(false, week, Utc(10, 16, 17, 0), Utc(10, 19, 11, 30), _ => true, t => t <= asleep || t >= awake);
        // Dalle scelte del 12 ottobre (C-56) il promemoria di sabato, in ritardo e di una riunione finita, non suona più (Choices2Tests).
        Check(weekendNew.Select(r => r.Id).Order().SequenceEqual(["lunedi-0900", "lunedi-1100", "venerdi-1730"]) && weekendNew.Single(r => r.Id == "lunedi-0900").At == awake
              && weekendNew.Single(r => r.Id == "lunedi-1100").At == Utc(10, 19, 10, 45) && !weekendOld.Any(r => r.Id == "lunedi-0900") && weekendOld.Any(r => r.Id == "sabato-1000"),
              $"C-56, PC fermo da venerdì alle 18:00 a lunedì alle 08:55: il promemoria delle 08:45 per la riunione di lunedì alle 09:00 suona alla riapertura ({Told(weekendNew)}); prima no, e suonava solo quello di sabato ({Told(weekendOld)}), " +
              "che ora, di una riunione finita, non suona più");

        // La notte del 25 ottobre 2026 (alle 03:00 legali tornano le 02:00): una riunione alle 03:10, dopo il cambio, con il promemoria alle 02:55, un'ora che c'è due volte.
        var rome = TimeZoneInfo.Local;
        if (rome.IsAmbiguousTime(new DateTime(2026, 10, 25, 2, 55, 0)) && !rome.IsAmbiguousTime(new DateTime(2026, 10, 25, 3, 10, 0)))
        {
            var night = new List<CalendarEvent> { Event("notte-0310", new DateTime(2026, 10, 25, 3, 10, 0)), Event("notte-0400", new DateTime(2026, 10, 25, 4, 0, 0)) };
            DateTime from = new DateTime(2026, 10, 24, 21, 0, 0, DateTimeKind.Utc), to = new DateTime(2026, 10, 25, 4, 0, 0, DateTimeKind.Utc);
            var meeting = UI.CalendarReminders.Instant(night[0].Start);
            var nightOld = BehReminderDay(true, night, from, to, _ => true);
            var nightNew = BehReminderDay(false, night, from, to, _ => true);
            double early = nightOld.Where(r => r.Id == "notte-0310").Select(r => (meeting - r.At).TotalMinutes).DefaultIfEmpty(-1).First();
            Check(nightNew.Count == 2 && nightNew.Single(r => r.Id == "notte-0310").At == meeting.AddMinutes(-15) && nightNew.Single(r => r.Id == "notte-0400").At == UI.CalendarReminders.Instant(night[1].Start).AddMinutes(-15)
                  && nightOld.Count == 2 && early == 75,
                  $"C-56, la notte del 25 ottobre 2026: il promemoria di una riunione alle 03:10 suona 15 minuti prima; prima suonava {early:0} minuti prima, alla prima delle due 02:55 " +
                  $"(nessun promemoria perso né doppio, prima e ora: {nightOld.Count} e {nightNew.Count})");
        }
        else Report.Add("      (C-56, la notte del 25 ottobre: non misurato, il fuso di questo PC non ha il ritorno all'ora solare quel giorno)");

        // Le letture della vista del calendario: una seconda lettura subito dopo la prima non toglie i promemoria in ritardo non
        // ancora mostrati; un'altra settimana letta dalla vista non fa ripetere nulla; un calendario nascosto non suona, nemmeno dopo.
        var start = Utc(10, 12, 8, 55);
        var later = Utc(10, 12, 9, 5);
        var view = new UI.CalendarReminders(start);
        CalendarData Read(DateTime day, int days) => new() { Events = monday.Concat(week).Where(e => e.Start >= day && e.Start < day.AddDays(days)).ToList(), From = day, To = day.AddDays(days) };
        view.Remember("a", Read(new DateTime(2026, 10, 12), 2), later);
        view.Remember("a", Read(new DateTime(2026, 10, 12), 7), later.AddSeconds(10));
        var first = view.Due(later.AddSeconds(30));
        view.Remember("a", Read(new DateTime(2026, 10, 19), 7), later.AddSeconds(40));
        var again = view.Due(later.AddSeconds(60));
        var hiddenNow = view.Due(Utc(10, 12, 9, 45), (_, e) => e.Id == "riunione-1000");
        var hiddenLater = view.Due(Utc(10, 12, 9, 46));
        // Un account senza il consenso al calendario, che arriva alle 15:00: non suonano insieme i promemoria della mattina.
        var consent = new UI.CalendarReminders(start);
        for (var t = start; t < Utc(10, 12, 15, 0); t = t.AddMinutes(30)) consent.NothingToRead("b", t);
        var hourly = Enumerable.Range(9, 10).Select(h => Event($"ora-{h}", new DateTime(2026, 10, 12, h, 0, 0))).ToList();
        consent.Remember("b", new CalendarData { Events = hourly, From = new DateTime(2026, 10, 12), To = new DateTime(2026, 10, 14) }, Utc(10, 12, 15, 0));
        var afterConsent = consent.Due(Utc(10, 12, 16, 50)).Select(d => d.Event.Id).ToList();
        Check(first is [{ Event.Id: "riunione-0915" }] && again.Count == 0 && hiddenNow.Count == 0 && hiddenLater.Count == 0 && afterConsent.SequenceEqual(["ora-16", "ora-17"]),
              "C-56, le letture della vista del calendario non tolgono un promemoria in ritardo non ancora mostrato e non ne fanno ripetere nessuno; un calendario nascosto non suona, nemmeno dopo; " +
              $"il consenso al calendario dato alle 15:00 non fa suonare insieme i promemoria della mattina ({string.Join(", ", afterConsent)})");

        // Trenta giorni come nel controllo di B-26, con la classe intera: gli eventi tenuti non crescono e nessun promemoria suona due volte.
        var month = new List<CalendarEvent>();
        for (int d = 0; d < 31; d++)
            for (int h = 8; h <= 20; h += h == 17 ? 2 : 1) month.Add(Event($"giorno-{d}-{h}", new DateTime(2026, 11, 2, h, 0, 0).AddDays(d)));
        var long1 = new UI.CalendarReminders(Utc(11, 2, 7, 0));
        int most = 0, rings = 0;
        var seen = new HashSet<string>();
        bool twice = false;
        for (int d = 0; d < 30; d++)
            for (var t = Utc(11, 2, 7, 0).AddDays(d); t <= Utc(11, 2, 18, 0).AddDays(d); t = t.AddMinutes(30))
            {
                var day = t.ToLocalTime().Date;
                long1.Remember("a", new CalendarData { Events = month.Where(e => e.Start >= day && e.Start < day.AddDays(2)).ToList(), From = day, To = day.AddDays(2) }, t);
                foreach (var (_, e) in long1.Due(t))
                {
                    rings++;
                    twice |= !seen.Add(e.Id);
                }
                most = Math.Max(most, long1.Kept);
            }
        // Dalle scelte del 12 ottobre (C-56) i due promemoria della sera, in ritardo alla riapertura e di riunioni finite, non suonano più: 10 al giorno invece di 12.
        Check(most <= 30 && !twice && rings == 30 * 10,
              $"C-56, trenta giorni di letture ogni mezz'ora (il PC fermo dalle 18 alle 7): gli eventi tenuti sono al più {most}, e ognuno dei {rings} promemoria suona una volta sola; quelli della sera, di riunioni finite alla riapertura, non suonano più");
    }

    /// <summary>
    /// C-50: Sposta, Archivia o Elimina che il server rifiuta. Sul server IMAP finto un UID MOVE rifiutato (NO): l'errore non
    /// è "non in linea" (quindi non va in coda: è il ramo dell'avviso "Operazione non riuscita") e il messaggio resta nella
    /// sua cartella. Le righe tolte prima della richiesta tornano al loro posto (MessageRows.RestoreFailed): solo quelle
    /// dell'account rifiutato, anche una conversazione chiusa. Nessuna prova crea la finestra: che MoveAsync e DeleteAsync lo
    /// chiamino si legge dal sorgente.
    /// </summary>
    static async Task BehFailedMoveAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        try
        {
            fake.Add("INBOX", SpeedMail("Preventivo di prova", "Buongiorno"));
            using var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            var folders = await imap.GetFoldersAsync();
            var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
            var trash = folders.First(f => f.Kind == FolderKind.Deleted);
            var listed = (await imap.GetMessagesAsync(inbox, null, null)).Messages;
            fake.RefuseOnce = "UID MOVE";
            Exception? refused = null;
            try { await imap.MoveAsync(listed, trash); }
            catch (Exception ex) { refused = ex; }
            Check(refused != null && !Network.IsOffline(refused) && fake.Folders["INBOX"].Count == 1 && fake.Folders[fake.TrashFolder].Count == 0,
                  $"C-50, Elimina con il server che rifiuta lo spostamento (NO a UID MOVE): l'errore non è \"non in linea\" ({refused?.GetType().Name}), quindi nessuna coda e l'avviso \"Operazione non riuscita\"; il messaggio è ancora nella Posta in arrivo");
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }

        // L'elenco di due account (Tutti gli account): sei righe, una è una conversazione chiusa di tre messaggi.
        UI.AccountItem Account(string email) => new(new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = email, Imap = new ImapServer() }));
        var one = Account("studio@uno.example");
        var two = Account("mario@due.example");
        int minutes = 0;
        MessageSummary Message(string subject, string conversation = "") =>
            new() { Id = (100 + minutes).ToString(), FolderId = "INBOX", Subject = subject, Date = new DateTime(2026, 10, 12, 9, 0, 0).AddMinutes(-minutes++), ConversationId = conversation };
        var rows = new UI.MessageRows { ShowAccounts = true };
        MessageSummary a1 = Message("a1"), b1 = Message("b1"), c1 = Message("c1", "pratica"), b2 = Message("b2"), a2 = Message("a2"), a3 = Message("a3"), c2 = Message("c2", "pratica"), c3 = Message("c3", "pratica");
        rows.Append([(a1, one), (b1, two), (c1, one), (b2, two), (a2, one), (a3, one), (c2, one), (c3, one)]);
        string Order() => string.Join(" ", rows.Rows.Select(r => r.Summary.Subject + (r.Count > 1 ? $"({r.Count})" : "")));
        var before = Order();
        // Elimina su quattro righe scelte: b1 (secondo account), la conversazione, a2 e b2. Come RemoveRows: dov'erano e con quali messaggi.
        var chosen = rows.Rows.Where(r => r.Summary.Subject is "b1" or "c1" or "b2" or "a2").ToList();
        var removed = chosen.Select(i => (Index: rows.Rows.IndexOf(i), Messages: UI.MessageRows.MessagesOf(i))).ToList();
        foreach (var i in chosen) rows.Remove(i);
        var without = Order();
        // Il primo account rifiuta (la conversazione e a2 restano sul server), il secondo riesce (b1 e b2 sono nel Cestino).
        var failed = new[] { c1, c2, c3, a2 }.ToDictionary(m => m, _ => one);
        var restored = rows.RestoreFailed(removed, failed);
        var after = Order();
        Check(before == "a1 b1 c1(3) b2 a2 a3" && without == "a1 a3" && after == "a1 c1(3) a2 a3" && restored.Count == 2 && restored.All(r => r.Account == one),
              $"C-50, le righe tolte prima della richiesta tornano al loro posto quando il server rifiuta: di quattro righe eliminate in due account ({before}) restano fuori solo quelle spostate davvero " +
              $"({after}); prima restavano fuori tutte ({without}) fino alla rilettura, e Annulla non le riportava");
        if (BehSource("UI", "MainWindow.xaml.cs") is { } source)
        {
            string move = source[source.IndexOf("async Task<bool> MoveAsync(List<MessageItem> items")..source.IndexOf("static void FollowFlags(")],
                   delete = source[source.IndexOf("async Task<bool> DeleteAsync(List<MessageItem> items")..source.IndexOf("sealed record MovePart(")];
            Check(Regex.IsMatch(move, @"case false:\s+ok = false;\s+foreach \(var m in messages\) failed\[m\] = account;") && move.Contains("PutBackFailed(removed, failed, version);") && move.Contains("rows.RestoreFailed(removed, failed)")
                  && delete.Contains("foreach (var m in inTrash.Concat(forever)) failed[m] = account;") && delete.Contains("foreach (var m in forever) failed[m] = account;") && delete.Contains("PutBackFailed(removed, failed, version);"),
                  "C-50, MoveAsync e DeleteAsync rimettono le righe dei messaggi rifiutati (PutBackFailed), anche quelle di un'eliminazione definitiva non riuscita, solo se l'elenco è ancora quello di prima");
        }
    }

    /// <summary>
    /// C-55: Annulla di uno spostamento fatto senza rete quando la rete è tornata per un momento e una parte è già partita.
    /// Tre spostamenti in coda, uno alla volta come Graph: il primo riesce, poi la rete manca di nuovo. Annulla toglie dalla
    /// coda i due rimasti e sa quali sono (ActionQueue.Take): quelli tornano nell'elenco.
    /// </summary>
    static async Task BehUndoQueuedAsync()
    {
        var file = Path.Combine(Store.Dir, "coda.dat");
        var saved = File.Exists(file) ? File.ReadAllBytes(file) : null;
        var account = new MailAccount { Id = "coda-c55", Kind = AccountKind.Microsoft, Email = "mario.rossi@outlook-prova.example" };
        var stub = System.Reflection.DispatchProxy.Create<IMailProvider, QueueStub>();
        var queue = (QueueStub)(object)stub;
        queue.Account = account;
        var trash = new MailFolder { Id = "cestino", Name = "Posta eliminata", Kind = FolderKind.Deleted };
        Task<List<MailFolder>> Folders() => Task.FromResult(new List<MailFolder> { trash });
        try
        {
            File.Delete(file);
            ActionQueue.Reset();
            var messages = Enumerable.Range(1, 3).Select(n => new MessageSummary { Id = "m" + n, FolderId = "arrivo" }).ToList();
            ActionQueue.Add(account, messages, QueuedKind.Move, trash.Id);
            queue.Act = () => queue.Calls == 2 ? Task.FromException(new IOException("rete assente", new SocketException(10065))) : Task.CompletedTask;
            var replay = await ActionQueue.ReplayAsync(stub, Folders);
            var taken = ActionQueue.Take(account, messages, trash.Id);
            int left = ActionQueue.Count(account.Id);
            // Mentre una ripetizione le ha prese tutte (in viaggio), Annulla non ne toglie nessuna.
            ActionQueue.Add(account, messages, QueuedKind.Move, trash.Id);
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Calls = 0;
            queue.Act = () => hold.Task;
            var running = ActionQueue.ReplayAsync(stub, Folders);
            for (int wait = 0; wait < 300 && queue.Calls < 1; wait++) await Task.Delay(10);
            var none = ActionQueue.Take(account, messages, trash.Id);
            hold.SetResult();
            await running;
            string some = UI.MainWindow.UndoStartedText(1, 3, trash.Name), two = UI.MainWindow.UndoStartedText(2, 3, trash.Name), all = UI.MainWindow.UndoStartedText(3, 3, trash.Name);
            Check(replay is { Done: 1, Waiting: 2 } && taken.Count == 2 && ReferenceEquals(taken[0], messages[1]) && ReferenceEquals(taken[1], messages[2]) && left == 0 && none.Count == 0
                  && some.Contains("uno spostamento era già partito") && some.Contains("Gli altri sono tornati") && two.Contains("2 spostamenti erano già partiti") && all == "La rete è tornata e lo spostamento è già stato fatto: i messaggi sono in Posta eliminata.",
                  $"C-55, Annulla di tre spostamenti fatti senza rete, con il primo già partito al ritorno della rete: dalla coda escono gli altri {taken.Count}, e Annulla sa quali sono ({string.Join(", ", taken.Select(m => m.Id))}) per rimetterli nell'elenco; " +
                  "prima li dava tutti per spostati. Con la ripetizione in corso non ne esce nessuno, come prima");
            if (BehSource("UI", "MainWindow.xaml.cs") is { } source)
            {
                var undo = source[source.IndexOf("async Task UndoAsync()")..source.IndexOf("internal static string UndoStartedText(")];
                Check(undo.Contains("ActionQueue.Take(part.Account.Provider.Account, originals, part.Destination.Id)") && undo.Contains("back.AddRange(part.Moved.Where(x => taken.Contains(x.Original))")
                      && Regex.Matches(undo, @"if \(stale\) await RefreshListAsync\(\);").Count == 3,
                      "C-55, UndoAsync rimette nell'elenco i messaggi usciti dalla coda anche quando gli altri sono già partiti, e poi rilegge l'elenco");
            }
        }
        finally
        {
            ActionQueue.Forget(account.Id);
            ActionQueue.Reset();
            if (saved != null) File.WriteAllBytes(file, saved);
            else File.Delete(file);
        }
    }

    /// <summary>
    /// C-52: "Nessuna firma" scelta a mano e poi un altro "Da". La decisione è ComposeWindow.FollowsFrom; che il pulsante
    /// Firma la ricordi e che il cambio del Da la chieda si legge dal sorgente (nessuna prova crea la finestra di scrittura).
    /// </summary>
    static void BehSignatureChoice()
    {
        // (Nessuna firma scelta, c'è una firma di Missiva, non toccata) -> la firma segue il Da?
        bool afterNone = UI.ComposeWindow.FollowsFrom(noneChosen: true, hasSignature: false, untouched: false);
        bool untouched = UI.ComposeWindow.FollowsFrom(false, true, true), touched = UI.ComposeWindow.FollowsFrom(false, true, false), neverHad = UI.ComposeWindow.FollowsFrom(false, false, false);
        Check(!afterNone && untouched && !touched && neverHad,
              "C-52, cambiando il Da dopo \"Nessuna firma\" scelta a mano il messaggio resta senza firma (prima tornava la firma predefinita dell'altro indirizzo); " +
              "la firma messa da Missiva e non toccata segue il Da, quella toccata no, e un indirizzo senza firma prende quella del nuovo, come prima");
        if (BehSource("UI", "ComposeWindow.Format.cs") is { } format && BehSource("UI", "ComposeWindow.xaml.cs") is { } compose)
        {
            var replace = format[format.IndexOf("void ReplaceSignature(string rtf)")..];
            var change = compose[compose.IndexOf("void ChangeSignature(IMailProvider provider")..compose.IndexOf("void OnSubjectChanged(")];
            Check(Regex.IsMatch(replace, @"PutSignature\(rtf\);\s+noSignature = signature == null;") && format.Contains("ReplaceSignature(\"\");")
                  && change.Contains("if (!FollowsFrom(noSignature, signature != null, SignatureUntouched)) return;"),
                  "C-52, il pulsante Firma ricorda \"Nessuna firma\" (e la dimentica scegliendo una firma), e il cambio del Da lo chiede prima di mettere la firma dell'altro indirizzo");
        }
    }

    /// <summary>
    /// C-54: Rimuovi account con messaggi nella Posta in uscita. I messaggi dell'account escono (Outbox.Forget) e la domanda
    /// lo dice; le finestre si riaprono da RemoveAccountAsync (letto dal sorgente).
    /// </summary>
    static void BehOutboxOnRemoval()
    {
        Outbox.Entry Entry(string accountId, string subject) => new() { AccountId = accountId, Message = new OutgoingMessage { To = To, Subject = subject, Html = "<p>Ciao</p>", Text = "Ciao" } };
        int changes = 0;
        void Changed() => changes++;
        Outbox.Changed += Changed;
        try
        {
            var stays = Entry("resta-c54", "Resta in uscita");
            Outbox.Add(Entry("via-c54", "Primo"), "mario@uno.example");
            Outbox.Add(Entry("via-c54", "Secondo"), "mario@uno.example");
            Outbox.Add(stays, "maria@due.example");
            changes = 0;
            int before = Outbox.Count("via-c54"), removed = Outbox.Forget("via-c54"), again = Outbox.Forget("via-c54");
            Check(before == 2 && removed == 2 && again == 0 && changes == 1 && Outbox.Count("via-c54") == 0 && Outbox.Count("resta-c54") == 1
                  && UI.MainWindow.OutboxRemovalText(0) == "" && UI.MainWindow.OutboxRemovalText(1).Contains("Un messaggio nella Posta in uscita non partirà") && UI.MainWindow.OutboxRemovalText(3).Contains("3 messaggi nella Posta in uscita non partiranno"),
                  $"C-54, account rimosso con {before} messaggi nella Posta in uscita: escono (prima restavano, e la barra continuava a dire \"partirà quando torna la rete\"), quello di un altro account resta, " +
                  "e la domanda di Rimuovi account lo dice prima");
            Outbox.Remove(stays);
        }
        finally
        {
            Outbox.Changed -= Changed;
            Outbox.Forget("via-c54");
            Outbox.Forget("resta-c54");
        }
        if (BehSource("UI", "MainWindow.xaml.cs") is { } source)
        {
            var remove = source[source.IndexOf("public async Task RemoveAccountAsync(MailAccount account)")..source.IndexOf("internal static string OutboxRemovalText(")];
            Check(remove.Contains("text += OutboxRemovalText(gone.Sum(Outbox.Count));") && remove.Contains("OutboxWindows().Where(w => w.OutboxAccountId is { } id && gone.Contains(id))") && remove.Contains("waiting.LeaveOutbox(")
                  && remove.Contains("Outbox.Forget(removed.Id);") && remove.IndexOf("waiting.LeaveOutbox(") > remove.IndexOf("Dialog.Ask(") && remove.IndexOf("waiting.LeaveOutbox(") < remove.IndexOf("await LoadAccountsAsync();"),
                  "C-54, Rimuovi account, dopo la conferma e prima di chiudere i provider, riapre le scritture della Posta in uscita di quell'account e delle sue caselle condivise, con il perché");
        }
    }

    /// <summary>
    /// C-60: la finestra di una sola cartella. App parla solo alla finestra principale; che la principale passi alle sue
    /// finestre di una cartella i messaggi nuovi, quelli tolti dalle regole e i segni cambiati altrove si legge dal sorgente:
    /// nessuna prova crea le finestre.
    /// </summary>
    static void BehFolderWindows()
    {
        if (BehSource("UI", "MainWindow.xaml.cs") is not { } main || BehSource("UI", "MainWindow.Notifications.cs") is not { } notifications || BehSource("UI", "App.cs") is not { } app) return;
        int onlyMain = Regex.Matches(app, @"\(MainWindow as MainWindow\)\?\.(AddIncoming|RemoveRuledRows|RefreshInboxOf|ApplyIdleFlags|ApplyInboxChanges)\(").Count;
        var incoming = main[main.IndexOf("public void AddIncoming(IMailProvider provider")..main.IndexOf("void KeepInSavedPage(")];
        var show = main[main.IndexOf("void ShowIncoming(IMailProvider provider")..main.IndexOf("/// I messaggi appena arrivati anche nella prima pagina salvata")];
        Check(onlyMain >= 5 && main.Contains("IEnumerable<MainWindow> FolderWindows => only == null ? folderWindows.OfType<MainWindow>().ToList() : [];")
              && incoming.Contains("foreach (var window in FolderWindows) window.ShowIncoming(provider, incoming);")
              && show.Contains("folder != inbox || searching || focused == false") && show.Contains("m.WithId(m.Id)") && !show.Contains("SetUnread") && !show.Contains("KeepInSavedPage(") && !show.Contains("MailCache")
              && notifications.Contains("foreach (var window in FolderWindows) window.ApplyInboxChanges(provider, changes);") && notifications.Contains("foreach (var window in FolderWindows) window.RefreshInboxOf(provider);")
              && notifications.Contains("foreach (var window in FolderWindows) window.ApplyIdleFlags(provider, id, read, flagged);") && notifications.Contains("foreach (var window in FolderWindows) window.RemoveRuledRows(provider, messages);"),
              $"C-60, la finestra principale passa alle finestre di una sola cartella quello che App dice solo a lei ({onlyMain} chiamate in App.cs): i messaggi nuovi (solo le righe, copie loro: non i conteggi né la copia locale), " +
              "le righe tolte dalle regole, i segni cambiati altrove e la rilettura dopo un avviso dell'IDLE");
    }

    /// <summary>
    /// C-51, sola misura: le regole di Missiva dopo un errore a metà. Una regola "segna come letto e sposta" su tre messaggi
    /// nuovi, con il server IMAP finto che rifiuta il primo UID STORE: che cosa succede subito e al controllo dopo.
    /// </summary>
    static async Task BehRulesMeasureAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        ImapProvider.TestPlain = true;
        var account = SpeedAccount(fake, "mario@studio-prova.example");
        int seenBefore = Store.Settings.RulesSeen.Count;
        try
        {
            using var imap = new ImapProvider(account);
            for (int i = 1; i <= 3; i++)
            {
                var mime = new MimeMessage { Subject = $"Fattura n. {i} di prova", Date = DateTimeOffset.Now.AddMinutes(-i), Body = new TextPart("html") { Text = "<p>In allegato la fattura</p>" } };
                mime.From.Add(new MailboxAddress("Fornitore di prova", "fatture@fornitore-prova.example"));
                mime.To.Add(new MailboxAddress("Mario Rossi", account.Email));
                fake.Add("INBOX", mime);
            }
            var folders = await imap.GetFoldersAsync();
            var target = folders.First(f => f.Id == fake.UserFolder);
            Store.Settings.Rules.Add(new MailRule { AccountId = account.Id, Name = "Fatture di prova", SenderContains = ["fornitore-prova"], MarkRead = true, MoveTo = target.Id, MoveToName = target.Name });
            Rules.ForgetMarks();
            var since = DateTime.UtcNow.AddMinutes(-10);
            fake.RefuseOnce = "UID STORE";
            string first;
            try { first = (await Rules.ApplyToNewAsync(imap, since, () => Task.FromResult(folders)))?.Matched + " corrispondono"; }
            catch (Exception ex) { first = "errore " + ex.GetType().Name; }
            int movedFirst = fake.Folders[fake.UserFolder].Count;
            fake.Commands.Clear();
            var second = await Rules.ApplyToNewAsync(imap, DateTime.UtcNow, () => Task.FromResult(folders));
            int movedSecond = fake.Folders[fake.UserFolder].Count;
            Report.Add($"      (C-51, misura: tre messaggi nuovi e una regola \"segna come letto e sposta\", con il server che rifiuta il primo UID STORE: al primo controllo {first}, spostati {movedFirst} di 3; " +
                       $"al controllo dopo {(second == null ? "nessun messaggio da guardare" : second.Matched + " corrispondono")}, spostati {movedSecond} di 3, {fake.Commands.Count(c => c.StartsWith("UID MOVE") || c.StartsWith("UID STORE"))} comandi di scrittura: " +
                       (movedSecond == 3 ? "la regola recupera" : "i messaggi mai toccati restano nella Posta in arrivo, per sempre") + ". Dalle scelte del 12 ottobre i messaggi non finiti si riprovano al controllo dopo: Choices2Tests)");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            Store.Settings.Rules.RemoveAll(r => r.AccountId == account.Id);
            if (Store.Settings.RulesSeen.Count > seenBefore) Store.Settings.RulesSeen.RemoveRange(seenBefore, Store.Settings.RulesSeen.Count - seenBefore);
            Rules.ForgetMarks();
        }
    }

    /// <summary>
    /// C-53: un provider chiuso (account rimosso o ricaricato: LoadAccountsAsync) mentre una sua richiesta è in corso, sul
    /// server IMAP finto con EXAMINE lento, per IMAP e per Gmail: la chiusura aspetta la richiesta. E, sola misura, lo stesso
    /// provider usato ancora dopo la chiusura (una finestra di scrittura aperta prima tiene il provider di prima).
    /// </summary>
    static async Task BehDisposedProviderAsync()
    {
        async Task<(string During, long Ms, string After, string Draft, string Sent)> CloseDuringAsync(FakeImap fake, IMailProvider provider, FakeSmtp? smtp = null)
        {
            fake.Add("INBOX", SpeedMail("Messaggio di prova", "Buongiorno"));
            var folders = await provider.GetFoldersAsync();
            var inbox = folders.First(f => f.Kind == FolderKind.Inbox);
            var listed = (await provider.GetMessagesAsync(inbox, null, null)).Messages;
            fake.ExamineDelay = TimeSpan.FromMilliseconds(600);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var running = provider.StillThereAsync(listed); // sulla connessione in sottofondo, con EXAMINE
            await Task.Delay(150);
            ((IDisposable)provider).Dispose();
            string during;
            if (await Task.WhenAny(running, Task.Delay(5000)) != running) during = "ancora ferma";
            else
                try { during = (await running)?.Count + " messaggi trovati, nessun errore"; }
                catch (Exception ex) { during = $"errore {ex.GetType().Name} ({ex.Message.Split('\n')[0]})"; }
            long ms = clock.ElapsedMilliseconds;
            fake.ExamineDelay = TimeSpan.Zero;
            await Task.Delay(300);
            string after, draft;
            try { after = (await provider.GetMessagesAsync(inbox, null, null)).Messages.Count + " messaggi letti, nessun errore"; }
            catch (Exception ex) { after = $"errore {ex.GetType().Name}"; }
            try { await provider.SaveDraftAsync(new OutgoingMessage { To = To, Subject = "Bozza di prova", Html = "<p>Ciao</p>", Text = "Ciao" }); draft = "salvata"; }
            catch (Exception ex) { draft = $"errore {ex.GetType().Name}"; }
            // Un messaggio nuovo inviato dal provider chiuso: l'SMTP ha un collegamento suo, la copia in Posta inviata no.
            string sent = "";
            if (smtp != null)
            {
                try { await provider.SendAsync(new OutgoingMessage { To = To, Subject = "Inviato dal provider chiuso", Html = "<p>Ciao</p>", Text = "Ciao" }); sent = "nessun errore"; }
                catch (Exception ex) { sent = $"errore {ex.GetType().Name}"; }
                sent += $", {smtp.Received.Count} arrivati al server SMTP, {fake.Folders[fake.SentFolder].Count} copie in Posta inviata";
            }
            return (during, ms, after, draft, sent);
        }

        (string During, long Ms, string After, string Draft, string Sent) imap, gmail;
        using var smtp = new FakeSmtp();
        ImapProvider.TestPlain = true;
        ImapProvider.TestCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            var account = SpeedAccount(fake, "mario@studio-prova.example");
            account.Imap!.SmtpPort = smtp.Port;
            account.Imap.SmtpSecurity = MailSecurity.StartTls;
            imap = await CloseDuringAsync(fake, new ImapProvider(account), smtp);
        }
        finally
        {
            ImapProvider.TestPlain = false;
            ImapProvider.TestCertificate = null;
        }
        try
        {
            using var fake = new FakeImap();
            GmailProvider.TestPort = fake.Port;
            gmail = await CloseDuringAsync(fake, new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", Id = "gmail-c53" }));
        }
        finally
        {
            GmailProvider.TestPort = null;
        }
        Check(imap.During == "1 messaggi trovati, nessun errore" && imap.Ms < 3000 && gmail.During == "1 messaggi trovati, nessun errore" && gmail.Ms < 3000,
              $"C-53, un provider chiuso (account rimosso o ricaricato) mentre una sua richiesta è in corso: la chiusura aspetta la richiesta, che finisce (IMAP: {imap.During} dopo {imap.Ms} ms; Gmail: {gmail.During} dopo {gmail.Ms} ms); " +
              "prima il LOGOUT partiva in mezzo al comando e la richiesta restava ferma fino al tempo scaduto di MailKit");
        Report.Add($"      (C-53, misura: lo stesso provider usato dopo la chiusura, come farebbe una finestra di scrittura aperta prima che gli account si ricarichino: IMAP elenco {imap.After}, bozza {imap.Draft}; " +
                   $"Gmail elenco {gmail.After}, bozza {gmail.Draft}. Un messaggio nuovo inviato dal provider IMAP chiuso: {imap.Sent}. Dal secondo gruppo un provider chiuso rifiuta tutto: X-1, RobustnessAuditTests)");
    }

    /// <summary>
    /// C-63, sola misura: quanti punti dell'interfaccia usano per il testo secondario il pennello di Fluent
    /// (TextFillColorSecondaryBrush) e quanti quello di Missiva (SecondaryTextBrush), contati nei sorgenti, e i due colori di
    /// Missiva. Quali siano voluti si giudica a finestra: lasciato all'autore.
    /// </summary>
    static void BehSecondaryTextMeasure()
    {
        if (SourceRoot() is not { } root) return;
        int fluent = 0, missiva = 0;
        var places = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "UI")).Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml")))
        {
            var text = File.ReadAllText(file);
            int f = Regex.Matches(text, @"\bTextFillColorSecondaryBrush\b").Count, m = Regex.Matches(text, @"(?<!Fill)\bSecondaryTextBrush\b").Count;
            fluent += f;
            missiva += m;
            if (f > 0) places.Add($"{Path.GetFileName(file)} {f}");
        }
        var colors = BehSource("UI", "App.cs") is { } app && Regex.Match(app, @"dark \? Color\.FromRgb\(0x(..), 0x(..), 0x(..)\) : Color\.FromRgb\(0x(..), 0x(..), 0x(..)\)\);\s+brush\.Freeze\(\);\s+Resources\[""SecondaryTextBrush""\]") is { Success: true } c
            ? $"#{c.Groups[4].Value}{c.Groups[5].Value}{c.Groups[6].Value} nel chiaro e #{c.Groups[1].Value}{c.Groups[2].Value}{c.Groups[3].Value} nello scuro" : "";
        Report.Add($"      (C-63, misura: il testo secondario usa il pennello di Fluent (TextFillColorSecondaryBrush) in {fluent} punti ({string.Join(", ", places)}) e quello di Missiva (SecondaryTextBrush) in {missiva}" +
                   (colors.Length > 0 ? $"; quello di Missiva è {colors} (App.SetTextColors)" : "") + ". Quali siano voluti si vede solo a finestra: lasciato all'autore)");
    }
}
#endif
