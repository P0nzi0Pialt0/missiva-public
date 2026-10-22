using System.Windows.Documents;

namespace Missiva.UI;

/// <summary>
/// I modelli di risposta, come I miei modelli del nuovo Outlook, le Parti rapide dell'Outlook classico e i Modelli di
/// Thunderbird: nome, oggetto facoltativo e testo scritto con l'editor delle firme (RTF), per tutti gli account o per uno
/// (Settings.Templates, cifrati con DPAPI: dentro ci possono essere IBAN e dati dei clienti; mai nel registro). Nel testo e
/// nell'oggetto {nome} diventa il nome del primo destinatario e {data} la data di oggi; in tutte e due le lingue valgono
/// anche {name} e {date} (Missiva in inglese, 21 ottobre 2026), e la data è scritta nella lingua di Missiva.
/// </summary>
public static class Templates
{
    static bool Of(MailTemplate template, MailAccount? account) => template.AccountId == null || template.AccountId == account?.Id;

    /// <summary>
    /// I modelli per un account da usare nella scrittura (il pannello Modelli, "Rispondi con modello"): i suoi e quelli per
    /// tutti gli account, per nome. Non quelli che su questo PC non si decifrano (Unreadable): nell'elenco avevano per
    /// nome il testo "(testo cifrato non leggibile...)" e, scelti, non inserivano nulla (scelte del 12 ottobre 2026 sull'audit
    /// dell'11 ottobre, T-21). Restano in Impostazioni, Posta, Modelli, che legge Settings.Templates: lì si eliminano.
    /// </summary>
    public static List<MailTemplate> For(MailAccount? account) =>
        Store.Settings.Templates.Where(t => Of(t, account) && !Unreadable(t))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Quanti modelli dell'account non sono nel pannello perché su questo PC non si decifrano.</summary>
    public static int Hidden(MailAccount? account) => Store.Settings.Templates.Count(t => Of(t, account) && Unreadable(t));

    /// <summary>
    /// La nota del pannello Modelli della scrittura, sotto la ricerca; vuota se non c'è nulla da dire. Dice anche dei
    /// modelli che non sono in elenco perché non si leggono, e dove trovarli: senza, sembrerebbero spariti.
    /// </summary>
    /// <param name="all">I modelli nelle impostazioni, di tutti gli account.</param>
    /// <param name="shown">Quelli in elenco adesso.</param>
    /// <param name="hidden">Quelli dell'account che non si leggono (Hidden).</param>
    /// <param name="searching">C'è del testo nella ricerca.</param>
    public static string PanelNote(int all, int shown, int hidden, bool searching)
    {
        if (all == 0)
            return T("Nessun modello. Scrivi il testo e premi \"Salva come modello\", oppure crealo in Impostazioni, Posta, Modelli. {nome} diventa il nome del primo destinatario, {data} la data di oggi.");
        string unreadable = hidden switch
        {
            <= 0 => "",
            1 => T("Un modello non è in elenco perché su questo PC non si legge: è cifrato per un altro utente di Windows. In Impostazioni, Posta, Modelli c'è ancora, e lì si elimina."),
            _ => T("{0} modelli non sono in elenco perché su questo PC non si leggono: sono cifrati per un altro utente di Windows. In Impostazioni, Posta, Modelli ci sono ancora, e lì si eliminano.", hidden),
        };
        if (shown > 0) return searching ? "" : unreadable;
        return searching || hidden == 0 ? T("Nessun modello con queste parole.") : unreadable;
    }

    /// <summary>
    /// Il nome per {nome}: la prima parola del nome del destinatario ("Mario" da "Mario Rossi"); senza nome resta {nome},
    /// da scrivere a mano (la parte prima della @ di info@ non è un nome).
    /// </summary>
    public static string? FirstName(Address? person) =>
        person?.Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(w => w.Any(char.IsLetter));

    /// <summary>
    /// I segnaposto di un testo: {nome} o {name} (se si sa) e {data} o {date}, "27 settembre 2026" in italiano e
    /// "27 September 2026" in inglese (Lingua.Culture). Le due forme valgono in tutte e due le lingue: un modello scritto
    /// con Missiva in italiano funziona anche dopo il passaggio all'inglese, e il contrario.
    /// </summary>
    public static string Fill(string text, string? name, DateTime today)
    {
        if (name != null)
            text = text.Replace("{nome}", name, StringComparison.OrdinalIgnoreCase).Replace("{name}", name, StringComparison.OrdinalIgnoreCase);
        var date = today.ToString("d MMMM yyyy", Lingua.Culture);
        return text.Replace("{data}", date, StringComparison.OrdinalIgnoreCase).Replace("{date}", date, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Un modello con una parte che su questo PC non si decifra più (impostazioni di un altro utente di Windows): al suo
    /// posto c'è il testo che lo dice (ProtectedTextConverter.Open), e quel testo non deve finire in un messaggio. Prima
    /// usando il modello diventava l'oggetto e il testo del messaggio (audit dell'11 ottobre, T-21).
    /// </summary>
    public static bool Unreadable(MailTemplate template) => new[] { template.Name, template.Subject, template.Rtf }.Any(ProtectedTextConverter.IsUnreadable);

    /// <summary>L'oggetto del modello con i segnaposto sostituiti; vuoto se non ne ha o se non si decifra più.</summary>
    public static string Subject(MailTemplate template, string? name, DateTime today) =>
        ProtectedTextConverter.IsUnreadable(template.Subject) ? "" : Fill(template.Subject.Trim(), name, today);

    static readonly Dictionary<string, string> plain = new();

    /// <summary>Il testo del modello senza formattazione, per la ricerca nel pannello (tenuto per RTF: i modelli sono pochi).</summary>
    public static string PlainText(MailTemplate template)
    {
        if (ProtectedTextConverter.IsUnreadable(template.Rtf)) return "";
        if (plain.TryGetValue(template.Rtf, out var known)) return known;
        // La chiave è tutto l'RTF (centinaia di KB con un logo), una voce nuova a ogni modifica del modello: oltre 50 si
        // riparte, i modelli si rileggono in pochi ms (audit del 7 ottobre, B-26).
        if (plain.Count >= 50) plain.Clear();
        var scratch = new FlowDocument();
        scratch.Blocks.AddRange(RichEditor.BlocksFromRtf(template.Rtf));
        return plain[template.Rtf] = new TextRange(scratch.ContentStart, scratch.ContentEnd).Text;
    }

    /// <summary>
    /// Il testo del modello pronto da inserire: i blocchi del suo RTF (come le firme: RichEditor.BlocksFromRtf, poi
    /// FlowToHtml quando parte) con i segnaposto sostituiti in ogni pezzo di testo.
    /// </summary>
    public static List<Block> Blocks(MailTemplate template, string? name, DateTime today)
    {
        if (ProtectedTextConverter.IsUnreadable(template.Rtf)) return new(); // il testo che dice "non leggibile" non è il testo del modello
        var blocks = RichEditor.BlocksFromRtf(template.Rtf);
        var scratch = new FlowDocument();
        scratch.Blocks.AddRange(blocks);
        var runs = new List<Run>();
        for (var at = scratch.ContentStart; at != null && at.CompareTo(scratch.ContentEnd) < 0; at = at.GetNextContextPosition(LogicalDirection.Forward))
            if (at.Parent is Run run && !runs.Contains(run)) runs.Add(run);
        foreach (var run in runs.Where(r => r.Text.Contains('{'))) run.Text = Fill(run.Text, name, today);
        var result = scratch.Blocks.ToList();
        scratch.Blocks.Clear();
        return result;
    }
}
