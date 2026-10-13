#if DEBUG
namespace Missiva;

/// <summary>
/// Solo in Debug: i contatti finti di --contatti-finti per gli account che hanno contatti, per fotografare Persone senza leggere
/// nessun contatto vero (nella prova il consenso ai contatti non c'è). Per gli account Gmail l'id è un resourceName di Google
/// (people/c...), come quello vero; con --contatti-molti se ne aggiungono migliaia, per provare l'elenco virtualizzato.
/// </summary>
static class ContactSamples
{
    static string Id(MailAccount account, int n) => account.Kind == AccountKind.Gmail ? "people/c" + (84613292873479820 + n) : "finto-" + n;

    /// <summary>
    /// I quattro contatti completi, più tre più semplici: ogni account ne riceve un gruppo diverso (dal suo posto in Settings.Accounts), così
    /// Persone divisa per account si vede con elenchi diversi e con lo stesso nome in più sezioni (Anna Bianchi è in tutte).
    /// </summary>
    static List<Contact> All(MailAccount account) =>
    [
        new() { Source = account.Id, ServerId = Id(account, 1), GivenName = "Anna", Surname = "Bianchi", DisplayName = "Anna Bianchi", Emails = ["anna.bianchi@studio-prova.example"],
                BusinessPhone = "+39 051 000 0001", Mobile = "+39 333 000 0001", Company = "Studio Bianchi", JobTitle = "Commercialista",
                Address = "Via dell'Indipendenza 1, 40121 Bologna", Notes = "PEC studiobianchi@pec-prova.example\nP. IVA 00000000001", Website = "www.studiobianchi-prova.example" },
        new() { Source = account.Id, ServerId = Id(account, 2), GivenName = "Luca", Surname = "Verdi", DisplayName = "Luca Verdi", Emails = ["luca.verdi@cliente-prova.example", "l.verdi@privato-prova.example", "verdi.costruzioni@pec-prova.example"],
                Mobile = "347 000 0002", BusinessPhone = "051-000.0004", HomePhone = "(051) 000 0005", Company = "Verdi Costruzioni", JobTitle = "Direttore tecnico",
                Address = "Strada Maggiore 12, 40125 Bologna, Italia", Website = "https://www.verdicostruzioni-prova.example/chi-siamo",
                Notes = "Cantiere di via Zamboni: riunione il lunedì alle 9.\nPreferisce essere chiamato al mattino." },
        new() { Source = account.Id, ServerId = Id(account, 3), DisplayName = "Segreteria studio", Emails = ["segreteria@studio-prova.example"], BusinessPhone = "+39 051 000 0003" },
        new() { Source = account.Id, ServerId = Id(account, 4), GivenName = "Maria Concetta", Surname = "Rossi Bianchi dei Conti di Montefalcone", DisplayName = "Maria Concetta Rossi Bianchi dei Conti di Montefalcone",
                Emails = ["maria.concetta.rossi.bianchi.montefalcone@un-dominio-molto-lungo-per-la-prova.example.org"], Mobile = "333 abc", BusinessPhone = "int. 12", HomePhone = "+39 06 000 0006",
                Company = "Fondazione per lo Studio e la Conservazione del Patrimonio Storico e Artistico", JobTitle = "Presidente del consiglio di amministrazione",
                Address = "Piazza della Repubblica 1, Palazzo dei Conti di Montefalcone, scala B, interno 4, 00185 Roma, Italia",
                Website = "https://fondazione-prova.example/",
                Notes = "Una nota molto lunga per vedere come va a capo il testo nella scheda: " + string.Concat(Enumerable.Repeat("parole di prova, ", 14)) + "\n\nSeconda riga dopo una riga vuota." },
        new() { Source = account.Id, ServerId = Id(account, 5), GivenName = "Paolo", Surname = "Gialli", DisplayName = "Paolo Gialli", Emails = ["paolo.gialli@esempio-prova.example"], Mobile = "338 000 0005", Company = "Gialli e Figli" },
        new() { Source = account.Id, ServerId = Id(account, 6), GivenName = "Elena", Surname = "Marino", DisplayName = "Elena Marino", Emails = ["elena.marino@esempio-prova.example"], JobTitle = "Architetto" },
    ];

    /// <summary>
    /// I contatti finti di un account: il primo account (slot 0) i quattro completi, il secondo Anna, Paolo ed Elena, il terzo Luca e la
    /// Segreteria, il quarto nessuno. Anna Bianchi è quindi anche negli altri account e in Missiva.
    /// </summary>
    public static List<Contact> For(MailAccount account)
    {
        int slot = Math.Max(0, Store.Settings.Accounts.FindIndex(a => a.Id == account.Id)) % 4;
        var all = All(account);
        return slot switch { 0 => all[..4], 1 => [all[0], all[4], all[5]], 2 => [all[1], all[2]], _ => [] };
    }

    /// <summary>
    /// Come Persone legge i contatti di un account nelle prove: quelli finti, con --contatti-molti mille e cinquecento in più,
    /// con --contatti-senza-consenso null per gli account Microsoft (manca il consenso: compare "Consenti l'accesso ai contatti") e con
    /// --contatti-vuoto nessun contatto per il terzo account (una sezione "Nessun contatto").
    /// </summary>
    public static List<Contact>? Load(MailAccount account, IReadOnlyCollection<string> args)
    {
        if (account.Kind == AccountKind.Microsoft && args.Contains("--contatti-senza-consenso")) return null;
        if (args.Contains("--contatti-vuoto") && Store.Settings.Accounts.FindIndex(a => a.Id == account.Id) == 2) return [];
        var list = For(account);
        if (args.Contains("--contatti-molti")) list.AddRange(Many(account, 1500));
        return list;
    }

    /// <summary>I contatti finti di Missiva (la sezione "Contatti di Missiva"), solo in memoria: mai salvati su disco dalla prova se non li si cambia.</summary>
    public static void AddLocal()
    {
        if (ContactBook.Local.Count > 0) return;
        ContactBook.Add(
        [
            new() { ServerId = null, GivenName = "Anna", Surname = "Bianchi", DisplayName = "Anna Bianchi", Emails = ["anna.bianchi@studio-prova.example"], Mobile = "+39 333 000 0001", Company = "Studio Bianchi" },
            new() { GivenName = "Beatrice", Surname = "Fontana", DisplayName = "Beatrice Fontana", Emails = ["beatrice.fontana@esempio-prova.example"] },
            new() { GivenName = "Carlo", Surname = "Esposito", DisplayName = "Carlo Esposito", Emails = ["carlo.esposito@esempio-prova.example"], HomePhone = "06 000 0007" },
        ]);
    }

    /// <summary>Migliaia di contatti con nomi diversi (--contatti-molti): l'elenco deve creare solo le righe in vista.</summary>
    public static IEnumerable<Contact> Many(MailAccount account, int count) =>
        Enumerable.Range(0, count).Select(i => new Contact
        {
            Source = account.Id, ServerId = Id(account, 100 + i), DisplayName = $"Persona {i:00000} di prova", Emails = [$"persona{i}@massa-prova.example"], Mobile = "+39 333 " + (1000000 + i),
        });
}
#endif
