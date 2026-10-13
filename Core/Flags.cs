using System.Globalization;

namespace Missiva;

/// <summary>
/// Contrassegna con scadenza, come il nuovo Outlook e l'Outlook classico: Oggi, Domani, Questa settimana, Settimana
/// prossima, Nessuna data, una data scelta, e il promemoria. Graph tiene inizio e scadenza sul server (flag
/// startDateTime e dueDateTime); per Gmail e IMAP il server ha solo \Flagged, e le date le tiene Missiva
/// (Settings.Flags, oggetto e mittente cifrati come i Posposti). I promemoria li mostra Missiva per tutti.
/// </summary>
public static class Flags
{
    static readonly CultureInfo Italian = new("it-IT");

    /// <summary>Una scelta del sottomenu: il nome, l'inizio e la scadenza (giorni interi).</summary>
    public sealed record Choice(string Name, DateTime Start, DateTime Due);

    /// <summary>
    /// Le date di Outlook: Oggi e Domani quel giorno; Questa settimana fino al venerdì (dal giorno dopo domani, non oltre
    /// il venerdì), e di sabato e domenica non c'è; Settimana prossima dal lunedì al venerdì dopo.
    /// </summary>
    public static List<Choice> Choices(DateTime now)
    {
        var today = now.Date;
        var list = new List<Choice> { new("Oggi", today, today), new("Domani", today.AddDays(1), today.AddDays(1)) };
        if (today.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            var friday = today.AddDays(DayOfWeek.Friday - today.DayOfWeek);
            var start = today.AddDays(2) < friday ? today.AddDays(2) : friday;
            if (friday > today.AddDays(1)) list.Add(new("Questa settimana", start, friday));
        }
        int toMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var monday = today.AddDays(toMonday == 0 ? 7 : toMonday);
        list.Add(new("Settimana prossima", monday, monday.AddDays(4)));
        return list;
    }

    /// <summary>Come la dice la riga: "Scadenza: oggi", "domani", "ieri", o il giorno ("ven 2 ott").</summary>
    public static string DueText(DateTime due, DateTime today)
    {
        var d = due.Date;
        var when = d == today ? "oggi" : d == today.AddDays(1) ? "domani" : d == today.AddDays(-1) ? "ieri"
            : d.Year == today.Year ? d.ToString("ddd d MMM", Italian) : d.ToString("d MMM yyyy", Italian);
        return "Scadenza: " + when;
    }

    /// <summary>Scaduto: la scadenza è prima di oggi (la riga lo dice in rosso).</summary>
    public static bool Overdue(DateTime due, DateTime today) => due.Date < today;

    /// <summary>I gruppi di Contrassegnati in Tutti gli account, per scadenza, come l'elenco delle attività di Outlook.</summary>
    public static string Group(DateTime? due, DateTime today)
    {
        if (due is not { } d) return "Senza data";
        d = d.Date;
        var sunday = today.AddDays((7 - (int)today.DayOfWeek) % 7);
        return d < today ? "In ritardo" : d == today ? "Oggi" : d == today.AddDays(1) ? "Domani" : d <= sunday ? "Questa settimana" : "Più avanti";
    }

    /// <summary>
    /// Se il server va toccato: un messaggio non contrassegnato sì; già contrassegnato, per Graph solo se la scadenza
    /// cambia, per Gmail e IMAP mai (\Flagged c'è già, le date sono di Missiva). Niente PATCH né STORE senza modifiche.
    /// </summary>
    public static bool NeedsWrite(MailAccount account, MessageSummary m, DateTime? due) =>
        !m.IsFlagged || account.Kind == AccountKind.Microsoft && m.FlagDue?.Date != due?.Date;

    /// <summary>
    /// La chiave del messaggio nelle date tenute da Missiva: quella che non cambia spostandolo (Gmail X-GM-MSGID, IMAP
    /// Message-Id, Graph internetMessageId: per Graph StableId è l'id, che cambia a ogni spostamento). Vuota se il
    /// messaggio non ne ha una (IMAP senza Message-Id): allora vale solo id più cartella, mai l'UID da solo.
    /// </summary>
    public static string KeyOf(MailAccount account, MessageSummary m) => account.Kind == AccountKind.Microsoft ? m.InternetMessageId : m.StableId;

    /// <summary>
    /// La voce è di quel messaggio in quella cartella (l'UID di Gmail e IMAP si ripete da una cartella all'altra); un UID
    /// riusato con un'altra chiave no. Le voci scritte prima delle correzioni del giro P4 hanno come chiave l'id (Graph, IMAP
    /// senza Message-Id): quella non conta.
    /// </summary>
    static bool SamePlace(FlagDate f, MessageSummary m, string key) =>
        f.MessageId == m.Id && f.FolderId == m.FolderId && (f.Key.Length == 0 || f.Key == f.MessageId || key.Length == 0 || f.Key == key);

    /// <summary>
    /// La voce di un messaggio: prima quella dello stesso id nella stessa cartella; poi, per un messaggio contrassegnato,
    /// quella con la stessa chiave (spostato, anche da un altro programma: il contrassegno viaggia con il messaggio). Un
    /// messaggio non contrassegnato con la stessa chiave è un'altra copia (Posta inviata di un messaggio a sé stessi) e
    /// non prende le date di quell'altro.
    /// </summary>
    public static FlagDate? Find(MailAccount account, MessageSummary m)
    {
        var key = KeyOf(account, m);
        var mine = Store.Settings.Flags.Where(f => f.AccountId == account.Id);
        return mine.FirstOrDefault(f => SamePlace(f, m, key)) ?? (m.IsFlagged && key.Length > 0 ? mine.FirstOrDefault(f => f.Key == key) : null);
    }

    /// <summary>La scadenza di un messaggio contrassegnato: quella di Graph, o quella tenuta da Missiva; null senza data.</summary>
    public static DateTime? DueOf(MailAccount account, MessageSummary m) => !m.IsFlagged ? null : m.FlagDue ?? Find(account, m)?.Due;

    /// <summary>Le date del contrassegno (e il promemoria) di un messaggio, tenute da Missiva; senza date e promemoria si tolgono.</summary>
    public static void Remember(MailAccount account, MessageSummary m, DateTime? start, DateTime? due, DateTime? reminder)
    {
        Forget(account, m);
        if (start == null && due == null && reminder == null) return;
        Store.Settings.Flags.Add(new FlagDate
        {
            AccountId = account.Id, Key = KeyOf(account, m), MessageId = m.Id, FolderId = m.FolderId, Start = start, Due = due, Reminder = reminder,
            Subject = m.Subject, From = m.From.Display,
        });
    }

    /// <summary>Toglie la voce del messaggio (quella che Find trova), non quelle di altri messaggi con lo stesso UID o la stessa chiave.</summary>
    public static void Forget(MailAccount account, MessageSummary m)
    {
        if (Find(account, m) is { } f) Store.Settings.Flags.Remove(f);
    }

    /// <summary>
    /// Spostato da Missiva (Sposta, Archivia, Elimina nel Cestino, Annulla): la voce segue il messaggio nella cartella di
    /// arrivo con il suo id nuovo (in Gmail e IMAP l'UID e in Graph l'id cambiano), così Segna come completato dalla notifica
    /// del promemoria agisce su di lui. Vero se una voce è cambiata (le impostazioni vanno salvate).
    /// </summary>
    public static bool Follow(MailAccount account, string fromId, string fromFolder, string toId, string toFolder)
    {
        // Senza COPYUID non si sa dove sia, e lo stesso id vuol dire che è rimasto dov'era (Gmail, da "Tutti i messaggi" cambiano
        // solo le etichette): resta la voce com'è, e la chiave lo ritrova negli elenchi.
        if (toId.Length == 0 || toId == fromId) return false;
        var f = Store.Settings.Flags.FirstOrDefault(x => x.AccountId == account.Id && x.MessageId == fromId && x.FolderId == fromFolder);
        if (f == null) return false;
        f.MessageId = toId;
        f.FolderId = toFolder;
        return true;
    }
}
