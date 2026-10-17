using System.Globalization;

namespace Missiva;

/// <summary>Che numero porta l'icona di Missiva (Impostazioni, Generali, Notifiche, "Numero sull'icona di Missiva").</summary>
public enum IconNumberKind
{
    /// <summary>I messaggi da leggere nelle Posta in arrivo: com'era prima della scelta, e la scelta di base.</summary>
    Unread,
    /// <summary>I messaggi nuovi arrivati da quando si è guardato Missiva (FreshMail).</summary>
    New,
    /// <summary>Nessun numero.</summary>
    None,
}

/// <summary>
/// Il numero sull'icona di Missiva nella barra delle applicazioni e nel suggerimento dell'icona dell'area di notifica, con
/// una regola sola per tutti e due (ritocchi del 16 ottobre 2026, richiesta dell'autore: "deve esserci la scelta di come e
/// se far comparire il contatore delle notifiche sull'icona dell'app"). Le scelte sono in Settings.IconNumber e
/// Settings.IconNumberOff (gli account che non contano). Funzioni pure: le chiamano la finestra principale e le prove.
/// </summary>
public static class IconCount
{
    /// <summary>
    /// Il numero: con Unread la somma dei non letti delle Posta in arrivo, con New i messaggi nuovi di FreshMail, con None
    /// zero; in tutti e due i casi solo per gli account che non sono fra gli esclusi.
    /// </summary>
    /// <param name="inboxes">Ogni Posta in arrivo con l'account e i suoi non letti.</param>
    public static int Count(IconNumberKind kind, IEnumerable<(string AccountId, int Unread)> inboxes, FreshMail fresh, ICollection<string> excluded) => kind switch
    {
        IconNumberKind.Unread => inboxes.Where(i => !excluded.Contains(i.AccountId)).Sum(i => Math.Max(0, i.Unread)),
        IconNumberKind.New => fresh.Count(excluded),
        _ => 0,
    };

    /// <summary>Il testo del cerchio sull'icona, "99+" oltre 99; null senza numero (niente cerchio).</summary>
    public static string? Badge(int count) => count <= 0 ? null : count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Che cosa dice il numero, per la descrizione dell'icona e il suggerimento dell'area di notifica: vuoto senza numero.</summary>
    public static string Describe(IconNumberKind kind, int count) =>
        count <= 0 || kind == IconNumberKind.None ? ""
        : kind == IconNumberKind.New ? (count == 1 ? "1 nuovo" : $"{count} nuovi")
        : $"{count} da leggere";

    /// <summary>Il suggerimento dell'icona dell'area di notifica: il nome, e con un numero anche quello, come sull'icona della barra.</summary>
    public static string TrayTip(string name, IconNumberKind kind, int count) => Describe(kind, count) is { Length: > 0 } said ? $"{name}: {said}" : name;
}

/// <summary>
/// I messaggi nuovi arrivati da quando si è guardato Missiva, per il numero sull'icona con IconNumberKind.New: quelli che
/// Missiva annuncia (gli stessi delle notifiche, anche con le notifiche spente) mentre la finestra principale non è quella
/// attiva; si azzerano quando la finestra principale diventa attiva. Ognuno conta una volta (account, cartella e
/// identificativo). Non scendono leggendoli altrove: dicono che cosa è arrivato, non che cosa resta da leggere.
/// </summary>
public sealed class FreshMail
{
    readonly HashSet<(string Account, string Folder, string Id)> messages = new();

    /// <param name="looking">La finestra principale è quella attiva: chi la guarda li vede arrivare, e non contano.</param>
    public void Add(string accountId, IEnumerable<MessageSummary> arrived, bool looking)
    {
        if (looking) return;
        foreach (var m in arrived) messages.Add((accountId, m.FolderId, m.Id));
    }

    /// <summary>La finestra principale è diventata attiva: si riparte da zero. Vero se c'era qualcosa da togliere.</summary>
    public bool Clear()
    {
        if (messages.Count == 0) return false;
        messages.Clear();
        return true;
    }

    /// <summary>Quanti, senza quelli degli account esclusi.</summary>
    public int Count(ICollection<string> excluded) => messages.Count(m => !excluded.Contains(m.Account));
}
