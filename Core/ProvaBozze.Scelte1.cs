#if DEBUG
namespace Missiva;

/// <summary>
/// Scelte del 12 ottobre 2026 dopo la verifica dei due audit dell'11 ottobre, primo gruppo (docs\audit\ESITO.md): lettura
/// della posta, account e copia di sicurezza (scelte 4, 5, 10, 14, 22, 23 e 24 del riepilogo). Solo dati finti, cartelle
/// temporanee loro e un Google finto; nessuna finestra: delle parti che stanno nelle finestre si provano le funzioni che
/// decidono, e si legge il sorgente per vedere che le finestre le chiamino.
/// Con "Missiva.exe --prova-audit risultato.txt scelte1" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void Choices1Tests()
    {
        Report.Add("--- Scelte del 12 ottobre, primo gruppo: lettura, account, copia di sicurezza (scelte 4, 5, 10, 14, 22, 23, 24)");
        Ch1EmlImages();
        Ch1WebPages();
        Ch1Dmarc();
        Ch1GoogleRevokeAsync().GetAwaiter().GetResult();
        var root = Path.Combine(Path.GetTempPath(), "missiva-prova-scelte1-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            Ch1ImportedBook(root);
            Ch1Aside(root);
            Ch1Templates(root);
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    /// <summary>
    /// Scelta 24 (T-21): i modelli che su questo PC non si decifrano (impostazioni scritte da un altro utente di Windows)
    /// non compaiono più nel pannello Modelli della scrittura né sotto "Rispondi con modello": lì avevano per nome il testo
    /// "(testo cifrato non leggibile...)" e, scelti, non inserivano nulla. Restano in Impostazioni, Posta, Modelli, dove si
    /// eliminano. Con modelli scritti davvero da "un altro utente" (Store.TestEntropy), in una cartella temporanea; il
    /// pannello non si apre qui: si provano l'elenco e la nota, e si legge dal sorgente chi li usa.
    /// </summary>
    static void Ch1Templates(string root)
    {
        var dir = Path.Combine(root, "t21-modelli");
        var saved = Store.Settings.Templates.ToList();
        MailAccount first = new() { Id = "conto-a-prova", Kind = AccountKind.Imap, Email = "mario@studio-prova.example" }, second = new() { Id = "conto-b-prova", Kind = AccountKind.Imap, Email = "maria@studio-prova.example" };
        try
        {
            // Due modelli scritti da un altro utente di Windows: uno per tutti gli account, uno per il primo.
            Store.TestEntropy = [9, 4, 9, 4];
            ProtectedTextConverter.TestForget();
            Store.TryWrite(dir, new Settings
            {
                Templates = [new MailTemplate { Name = "Sollecito", Subject = "Sollecito del {data}", Rtf = @"{\rtf1 Gentile \{nome\}, un sollecito.}" },
                             new MailTemplate { Name = "Coordinate", AccountId = first.Id, Rtf = @"{\rtf1 Coordinate di prova}" }],
            }, null, out _);
            Store.TestEntropy = null; // questo PC
            ProtectedTextConverter.TestForget();
            var foreign = Store.Read(dir, out _).Templates;
            List<MailTemplate> mine =
            [
                new() { Name = "Preventivo", Subject = "Preventivo del {data}", Rtf = @"{\rtf1 Buongiorno \{nome\}, ecco il preventivo.}" },
                new() { Name = "Auguri", AccountId = first.Id, Rtf = @"{\rtf1 Tanti auguri.}" },
                new() { Name = "Benvenuto", AccountId = second.Id, Rtf = @"{\rtf1 Benvenuto fra i clienti.}" },
            ];
            Store.Settings.Templates.Clear();
            Store.Settings.Templates.AddRange(foreign.Concat(mine));
            static string Names(IEnumerable<MailTemplate> templates) => string.Join(", ", templates.Select(t => t.Name));
            string forFirst = Names(UI.Templates.For(first)), forSecond = Names(UI.Templates.For(second)), forNone = Names(UI.Templates.For(null));
            int hiddenFirst = UI.Templates.Hidden(first), hiddenSecond = UI.Templates.Hidden(second);
            bool placeholders = foreign.Count == 2 && foreign.All(UI.Templates.Unreadable) && foreign.All(t => t.Name.StartsWith("(testo cifrato non leggibile"));
            // Nelle Impostazioni ci sono tutti (la pagina legge Settings.Templates), e lì si eliminano: tolti, non manca più nulla.
            int inSettings = Store.Settings.Templates.Count;
            // Un account che ha solo modelli illeggibili: nessun modello da usare ("Rispondi con modello" non compare).
            Store.Settings.Templates.Clear();
            Store.Settings.Templates.AddRange(foreign);
            int onlyForeign = UI.Templates.For(first).Count, onlyForeignHidden = UI.Templates.Hidden(first);
            Store.Settings.Templates.RemoveAll(UI.Templates.Unreadable);
            int afterDelete = UI.Templates.Hidden(first) + Store.Settings.Templates.Count;
            Check(placeholders && forFirst == "Auguri, Preventivo" && forSecond == "Benvenuto, Preventivo" && forNone == "Preventivo" && hiddenFirst == 2 && hiddenSecond == 1 && inSettings == 5
                  && onlyForeign == 0 && onlyForeignHidden == 2 && afterDelete == 0,
                  $"scelta 24 (T-21), i modelli da usare nella scrittura: con cinque modelli, due dei quali cifrati per un altro utente di Windows, il primo account vede {forFirst}, il secondo {forSecond}, " +
                  $"e nessuno dei due vede quelli che non si leggono ({hiddenFirst} e {hiddenSecond} nascosti; prima erano in elenco con il nome \"(testo cifrato non leggibile...)\"); nelle impostazioni ci sono tutti e {inSettings}; " +
                  $"con i soli illeggibili l'account non ha modelli da usare ({onlyForeign})");

            // La nota del pannello: quando dice dei modelli nascosti, e che cosa dice negli altri casi (come prima).
            const string none = "Nessun modello con queste parole.";
            string one = UI.Templates.PanelNote(5, 2, 1, searching: false), two = UI.Templates.PanelNote(5, 2, 2, searching: false), onlyHidden = UI.Templates.PanelNote(2, 0, 2, searching: false);
            Check(UI.Templates.PanelNote(0, 0, 0, false).StartsWith("Nessun modello. Scrivi il testo") && UI.Templates.PanelNote(3, 3, 0, false) == "" && UI.Templates.PanelNote(3, 1, 0, true) == ""
                  && UI.Templates.PanelNote(3, 0, 0, true) == none && UI.Templates.PanelNote(3, 0, 0, false) == none && UI.Templates.PanelNote(5, 0, 2, true) == none && UI.Templates.PanelNote(5, 1, 2, true) == ""
                  && one.StartsWith("Un modello non è in elenco perché su questo PC non si legge") && one.Contains("In Impostazioni, Posta, Modelli c'è ancora, e lì si elimina")
                  && two.StartsWith("2 modelli non sono in elenco perché su questo PC non si leggono") && two.Contains("lì si eliminano") && onlyHidden == two,
                  $"scelta 24 (T-21), la nota del pannello Modelli: senza modelli illeggibili è quella di prima (nessun modello, nessuno con queste parole, o niente); con dei modelli che non si leggono dice quanti " +
                  $"non sono in elenco e che in Impostazioni, Posta, Modelli ci sono ancora e si eliminano (\"{one}\"), anche quando in elenco non resta nulla; durante una ricerca non lo ripete");

            // Chi li usa: il pannello e "Rispondi con modello" passano da Templates.For; la pagina delle Impostazioni da Settings.Templates.
            if (Ch1Code("UI", "ComposeWindow.xaml.cs") is { } compose && Ch1Code("UI", "MainWindow.xaml.cs") is { } main && Ch1Code("UI", "SettingsWindow.cs") is { } settings)
            {
                int start = settings.IndexOf("StackPanel TemplatesPage()", StringComparison.Ordinal), end = settings.IndexOf("StackPanel AccountsPage()", StringComparison.Ordinal);
                string page = start >= 0 && end > start ? settings[start..end] : "";
                bool panel = compose.Contains("var list = Templates.For(FromProvider?.Account)")
                             && compose.Contains("TemplatesNote.Text = Templates.PanelNote(Store.Settings.Templates.Count, list.Count, Templates.Hidden(FromProvider?.Account), words.Length > 0);");
                bool reply = main.Contains("case \"replyTemplate\" when single != null && Templates.For(single.Account.Provider.Account).Count > 0:") && main.Contains("foreach (var t in Templates.For(single.Account.Provider.Account))");
                bool kept = page.Contains("list.ItemsSource = S.Templates.OrderBy(") && !page.Contains("Templates.For(") && !page.Contains("Unreadable") && page.Contains("S.Templates.Remove(t);");
                Check(panel && reply && kept,
                      $"scelta 24 (T-21), chi usa l'elenco senza gli illeggibili: il pannello Modelli della scrittura, con la sua nota ({panel}), e \"Rispondi con modello\" nel menu del messaggio ({reply}); " +
                      $"la pagina Impostazioni, Posta, Modelli li elenca tutti e li lascia eliminare ({kept}) (lettura del sorgente: nessuna prova apre le finestre)");
            }
        }
        finally
        {
            Store.TestEntropy = null;
            ProtectedTextConverter.TestForget();
            Store.Settings.Templates.Clear();
            Store.Settings.Templates.AddRange(saved);
        }
    }

    /// <summary>
    /// Scelta 23 (T-20): i file che un'importazione mette da parte tengono gli accessi degli account di prima (la password
    /// IMAP, il token di Google, cifrati per questo utente di Windows). Missiva non li toglie da sola: la pagina Copia di
    /// sicurezza, solo quando ci sono, ha "Elimina i dati di prima", con una domanda che dice che cosa si perde. Tutto in
    /// cartelle temporanee di questa prova: quali file sono "di prima" e quali no, che cosa si cancella e che cosa resta, un
    /// file in uso, i testi. La pagina delle Impostazioni si legge dal sorgente.
    /// </summary>
    static void Ch1Aside(string root)
    {
        // ---------------------------------------------------------------- un'importazione vera sopra due account con i loro accessi
        var dir = Path.Combine(root, "t20-importazione");
        var imap = Copy2Imap("di-prima-imap", email: "prima@studio-prova.example");
        imap.Imap!.ProtectedPassword = Store.Protect("password-di-prima");
        var before = new Settings { Accounts = [imap, new MailAccount { Id = "di-prima-gmail", Kind = AccountKind.Gmail, Email = "maria.rossi@gmail-prova.example", ProtectedRefreshToken = Store.Protect("token-di-prima") }] };
        Store.TryWrite(dir, before, null, out _);
        Store.TryWrite(dir, before, null, out _); // due salvataggi: c'è anche impostazioni.bak
        File.WriteAllBytes(Path.Combine(dir, "contatti.dat"), Store.ProtectBytes("[]"u8.ToArray()));
        File.WriteAllBytes(Path.Combine(dir, "rubrica.json"), SafeFile.Protect("{\"Entries\":[]}"u8.ToArray()));
        bool noneBefore = Backup.AsideFiles(dir).Count == 0;
        var content = Copy2Content(new Settings { Accounts = [Copy2Imap("nuovo-prova")] });
        (content["Contacts"], content["AddressBook"]) = ("[]", "{\"Entries\":[]}");
        Copy2Pending(dir, content);
        string how = Copy2Apply(dir, out var outcome);
        var aside = Backup.AsideFiles(dir);
        // Gli accessi di prima che si decifrano ancora, in qualunque file delle impostazioni della cartella.
        int Secrets() => Directory.GetFiles(dir, "impostazioni*").Sum(path => Store.TryParse(path, out var kept, out _)
            ? kept.Accounts.Count(a => Store.TryUnprotect(a.Imap?.ProtectedPassword, out var password) && password == "password-di-prima"
                                       || Store.TryUnprotect(a.ProtectedRefreshToken, out var token) && token == "token-di-prima") : 0);
        var current = File.ReadAllBytes(Path.Combine(dir, Store.FileName));
        int secretsBefore = Secrets();
        bool told = outcome?.Body.Contains("si eliminano da Impostazioni, Generali, Copia di sicurezza") == true;
        var (deleted, failed) = Backup.DeleteAside(dir);
        var left = Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToList();
        Check(noneBefore && how == "applicata" && aside.Count == 4 && secretsBefore == 4 && told && deleted == 4 && failed.Count == 0 && Backup.AsideFiles(dir).Count == 0 && Secrets() == 0
              && File.ReadAllBytes(Path.Combine(dir, Store.FileName)).SequenceEqual(current) && left.SequenceEqual(["contatti.dat", "impostazioni.json", "rubrica.json"]),
              $"scelta 23 (T-20), dopo un'importazione sopra due account: da parte {aside.Count} file (le impostazioni di prima con la loro copia di riserva, i contatti, la rubrica), e nelle impostazioni di prima si decifrano ancora " +
              $"{secretsBefore} accessi; l'avviso dopo il riavvio dice dove si eliminano ({told}). \"Elimina i dati di prima\" toglie quei {deleted} file e nient'altro: restano {string.Join(", ", left)}, " +
              $"le impostazioni di adesso uguali byte per byte, e degli accessi di prima sul disco non ce n'è più ({Secrets()})");

        // ---------------------------------------------------------------- quali file sono "di prima", e quali no
        var names = Path.Combine(root, "t20-nomi");
        Directory.CreateDirectory(Path.Combine(names, "cache"));
        string[] mine =
        [
            "impostazioni-prima-dell-importazione-20261003-091500.json", "impostazioni-prima-dell-importazione-20261003-091500.bak", "contatti-prima-dell-importazione-20261003-091501.dat",
            "rubrica-prima-dell-importazione-20261003-091501.json", "impostazioni-prima-dell-importazione-20261012-101500-2.json", "Rubrica-Prima-Dell-Importazione-20261012-101500.JSON",
        ];
        string[] others =
        [
            "impostazioni.json", "impostazioni.bak", "contatti.dat", "rubrica.json", "coda.dat", "importazione.dat", "missiva.log", "impostazioni-illeggibile-20261003-091500.json",
            "importazione-illeggibile-20261003-091500.dat", "note-prima-dell-importazione-20261003-091500.txt", "impostazioni-prima-dell-importazione-20261003-091500.json.txt",
            "impostazioni-prima-dell-importazione-copia.json", "copia di impostazioni-prima-dell-importazione-20261003-091500.json", "impostazioni-prima-dell-importazione-20261003-091500.dat",
            "impostazioni-prima-dell-importazione-20261003-091500-100.json",
        ];
        foreach (var name in mine.Concat(others)) File.WriteAllText(Path.Combine(names, name), "prova");
        // Lo stesso nome dentro una sottocartella, e una cartella che si chiama come un file di prima: non sono della pagina.
        File.WriteAllText(Path.Combine(names, "cache", mine[0]), "prova");
        Directory.CreateDirectory(Path.Combine(names, "contatti-prima-dell-importazione-20261003-091502.dat"));
        var found = Backup.AsideFiles(names).Select(Path.GetFileName).ToList();
        bool dates = Backup.AsideDate(mine[0]) == new DateTime(2026, 10, 3, 9, 15, 0) && Backup.AsideDate(mine[4]) == new DateTime(2026, 10, 12, 10, 15, 0) && Backup.AsideDate(others[11]) == null;
        List<string> busy;
        int firstDeleted;
        using (new FileStream(Path.Combine(names, mine[3]), FileMode.Open, FileAccess.Read, FileShare.Read)) // tenuto aperto da "un altro programma"
            (firstDeleted, busy) = Backup.DeleteAside(names);
        var afterFirst = Backup.AsideFiles(names).Select(Path.GetFileName).ToList();
        var (secondDeleted, secondFailed) = Backup.DeleteAside(names);
        bool restStays = others.All(name => File.Exists(Path.Combine(names, name))) && File.Exists(Path.Combine(names, "cache", mine[0]))
                         && Directory.Exists(Path.Combine(names, "contatti-prima-dell-importazione-20261003-091502.dat")) && Directory.GetFiles(names).Length == others.Length;
        Check(found.Count == mine.Length && mine.All(name => found.Contains(name, StringComparer.OrdinalIgnoreCase)) && dates && firstDeleted == mine.Length - 1 && busy is [var held] && held == mine[3]
              && afterFirst is [var again] && again == mine[3] && secondDeleted == 1 && secondFailed.Count == 0 && Backup.AsideFiles(names).Count == 0 && restStays
              && Backup.AsideFiles(Path.Combine(root, "t20-non-esiste")).Count == 0,
              $"scelta 23 (T-20), sono \"dati di prima\" solo i file con i nomi che Missiva dà mettendoli da parte ({found.Count} su {mine.Length + others.Length}: le impostazioni .json e .bak, i contatti, la rubrica, anche con \"-2\" in fondo); " +
              $"restano i file di adesso, quelli messi da parte perché illeggibili, i nomi che gli somigliano ({others.Length} file), lo stesso nome in una sottocartella e una cartella con quel nome ({restStays}); " +
              $"un file in uso da un altro programma non si elimina, viene detto e resta per la volta dopo ({string.Join(", ", busy)}), gli altri sì ({firstDeleted}, poi {secondDeleted})");

        // ---------------------------------------------------------------- i testi della pagina e della domanda
        string line = UI.BackupUi.AsideLine(mine.Select(name => Path.Combine(names, name)).ToList()), one = UI.BackupUi.AsideLine([Path.Combine(names, mine[0])]);
        var question = UI.BackupUi.AsideQuestion(mine.Select(name => Path.Combine(names, name)).ToList());
        Check(line.Contains("6 file, messi da parte fra il 3 ottobre 2026 e il 12 ottobre 2026") && line.Contains("gli accessi degli account di allora") && one.Contains("un file, messo da parte il 3 ottobre 2026")
              && question is { Heading: "Eliminare i dati di prima dell'importazione?", Primary: "Elimina" } && question.Body.Contains("Sono 6 file") && question.Body.Contains("non potrai più tornare a com'era Missiva prima dell'importazione")
              && question.Body.Contains("con gli account di allora e i loro accessi") && question.Body.Contains("I file non vanno nel Cestino") && question.Body.Contains("Quello che Missiva ha adesso non cambia"),
              $"scelta 23 (T-20), la pagina dice quanti sono i file di prima, di quando, e che tengono ancora gli accessi degli account di allora; la domanda dice che cosa si perde (non si torna più a prima dell'importazione), " +
              $"che i file non vanno nel Cestino e che i dati di adesso non cambiano (\"{question.Body.Replace("\n\n", " ")}\")");

        // ---------------------------------------------------------------- la pagina: solo quando ci sono, e mai da soli
        if (Ch1Code("UI", "SettingsWindow.cs") is { } settingsPage && SourceRoot() is { } sources)
        {
            int start = settingsPage.IndexOf("StackPanel BackupPage()", StringComparison.Ordinal), end = settingsPage.IndexOf("StackPanel MessageHandlingPage()", StringComparison.Ordinal);
            string page = start >= 0 && end > start ? settingsPage[start..end] : "";
            int At(string text) => page.IndexOf(text, StringComparison.Ordinal);
            bool onlyIfThere = At("if (Backup.AsideFiles(Store.Dir) is { Count: > 0 } aside)") > 0 && At("Content = T(\"Elimina i dati di prima…\")") > At("if (Backup.AsideFiles(Store.Dir) is { Count: > 0 } aside)");
            bool asksFirst = At("Dialog.AskCancelFirst(this, heading, body, primary) == Choice.Primary") > At("BackupUi.AsideQuestion(now)") && At("Backup.DeleteAside(Store.Dir)") > At("Dialog.AskCancelFirst(this, heading, body, primary) == Choice.Primary")
                             && At("BackupUi.AsideQuestion(now)") > 0;
            // Nessun altro punto di Missiva li elimina: una sola chiamata, quella del pulsante.
            var callers = new[] { "Core", "UI" }.SelectMany(folder => Directory.GetFiles(Path.Combine(sources, folder), "*.cs")).Where(file => !Path.GetFileName(file).StartsWith("ProvaBozze"))
                .SelectMany(file => File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//") && l.Contains("DeleteAside(") && !l.Contains("public static")).Select(_ => Path.GetFileName(file))).ToList();
            Check(onlyIfThere && asksFirst && callers is ["SettingsWindow.cs"],
                  $"scelta 23 (T-20), Impostazioni, Copia di sicurezza: la parte con \"Elimina i dati di prima\" c'è solo quando quei file ci sono ({onlyIfThere}), il pulsante chiede prima con Annulla come scelta di base ({asksFirst}), " +
                  $"e in tutta Missiva nessun altro punto li elimina: mai da soli (chiamate: {string.Join(", ", callers)})");
        }
    }

    /// <summary>
    /// Scelta 22 (T-17): la rubrica dei destinatari di una copia importata entra con nomi e indirizzi, senza i conteggi dei
    /// messaggi scritti e ricevuti, le date e l'ora dell'ultima lettura della Posta inviata. Prima entrava com'era: con
    /// conteggi scritti da altri il primo suggerimento per "mario" era l'indirizzo di chi aveva preparato la copia, e un
    /// messaggio di "Mario Rossi" da lì non accendeva la fascia dei sosia. In cartelle temporanee, con importazioni scritte
    /// a mano come quelle di Backup2AuditTests; la rubrica importata si prova in memoria (AddressBook.TestBook).
    /// </summary>
    static void Ch1ImportedBook(string root)
    {
        const string real = "mario.rossi@studio-prova.example", hostile = "mario.rossi@altro-prova.example";
        // La rubrica di una copia preparata da altri: l'indirizzo loro con il nome di una persona a cui scrivi, conteggi e date
        // a piacere (anche nel futuro), un nome che è l'indirizzo di un altro, un "noreply", un nome con un cambio di direzione,
        // e la Posta inviata data per letta fino al 2099.
        string book = "{\"Entries\":[" +
                      $"{{\"Name\":\"Mario Rossi\",\"Email\":\"{hostile}\",\"Received\":5000,\"Sent\":900,\"LastReceived\":\"2099-01-01T10:00:00\",\"LastSent\":\"2099-01-01T10:00:00\"}}," +
                      $"{{\"Name\":\"Mario Rossi\",\"Email\":\"{real}\",\"Received\":3,\"Sent\":12,\"LastReceived\":\"2026-10-01T10:00:00\",\"LastSent\":\"2026-10-02T10:00:00\"}}," +
                      "{\"Name\":\"assistenza@banca-prova.example\",\"Email\":\"sportello@altro-prova.example\",\"Received\":1,\"Sent\":3,\"LastReceived\":\"2026-10-01T10:00:00\",\"LastSent\":\"2026-10-02T10:00:00\"}," +
                      "{\"Name\":\"Servizio di prova\",\"Email\":\"noreply@servizi-prova.example\",\"Received\":40,\"Sent\":0,\"LastReceived\":\"2026-10-01T10:00:00\",\"LastSent\":\"0001-01-01T00:00:00\"}," +
                      "{\"Name\":\"Maria\\u202E Rossi\",\"Email\":\"maria.rossi@clienti-prova.example\",\"Received\":2,\"Sent\":1,\"LastReceived\":\"2026-10-01T10:00:00\",\"LastSent\":\"2026-10-02T10:00:00\"}]," +
                      "\"SentScans\":{\"imap-prova\":\"2099-01-01T00:00:00Z\"}}";
        static List<AddressBook.Entry> Entries(string json) =>
            System.Text.Json.JsonSerializer.Deserialize<List<AddressBook.Entry>>(System.Text.Json.Nodes.JsonNode.Parse(json)!["Entries"]!.ToJsonString()) ?? [];
        var settings = new Settings { Accounts = [Copy2Imap("imap-prova")] };
        System.Text.Json.Nodes.JsonObject Content(string? addressBook)
        {
            var content = Copy2Content(settings);
            content["AddressBook"] = addressBook;
            return content;
        }

        // ---------------------------------------------------------------- che cosa arriva sul disco
        var dir = Path.Combine(root, "t17-rubrica");
        Copy2Pending(dir, Content(book));
        string how = Copy2Apply(dir, out _);
        string onDisk = File.Exists(Path.Combine(dir, "rubrica.json")) ? System.Text.Encoding.UTF8.GetString(Store.UnprotectBytes(File.ReadAllBytes(Path.Combine(dir, "rubrica.json")))) : "{\"Entries\":[]}";
        var imported = Entries(onDisk);
        string scans = System.Text.Json.Nodes.JsonNode.Parse(onDisk)!["SentScans"]?.ToJsonString() ?? "manca";
        Check(how == "applicata" && imported.Select(e => e.Email).SequenceEqual([hostile, real, "sportello@altro-prova.example", "maria.rossi@clienti-prova.example"])
              && imported.All(e => e is { Sent: 0, Received: 0 } && e.LastSent == default && e.LastReceived == default) && scans == "{}"
              && imported.Select(e => e.Name).SequenceEqual(["Mario Rossi", "Mario Rossi", "", "Maria Rossi"]),
              $"scelta 22 (T-17), la rubrica di una copia importata: entrano gli indirizzi con i loro nomi ({imported.Count} su 5: non il \"noreply\"), con i conteggi dei messaggi scritti e ricevuti a zero, " +
              $"senza date e senza l'ora dell'ultima lettura della Posta inviata ({scans}); un nome che è l'indirizzo di un altro non entra come nome, e uno con un cambio di direzione entra pulito");

        // ---------------------------------------------------------------- che cosa ne fanno suggerimenti e fascia dei sosia
        var (accounts, contacts) = (Store.Settings.Accounts, AddressBook.Contacts);
        try
        {
            Store.Settings.Accounts = [];
            AddressBook.Contacts = null;
            var fake = new Address { Name = "Mario Rossi", Email = hostile };
            // Com'era: la rubrica della copia così com'è.
            AddressBook.TestBook(Entries(book));
            string? firstBefore = AddressBook.Suggest("mario", 3).FirstOrDefault()?.Email, bandBefore = AddressBook.LookAlike(fake);
            // Com'è: la rubrica importata, e poi la Posta inviata di questo PC riletta (un messaggio alla persona vera).
            AddressBook.TestBook(null);
            AddressBook.TestBook(imported);
            string? bandEmpty = AddressBook.LookAlike(fake);
            AddressBook.Sent([new Address { Name = "Mario Rossi", Email = real }], new DateTime(2026, 10, 12, 9, 0, 0));
            string? firstAfter = AddressBook.Suggest("mario", 3).FirstOrDefault()?.Email, bandAfter = AddressBook.LookAlike(fake);
            int suggested = AddressBook.Suggest("mario", 5).Count;
            Check(firstBefore == hostile && bandBefore == null && bandEmpty == null && firstAfter == real && bandAfter == real && suggested == 2,
                  $"scelta 22 (T-17), con la rubrica di una copia preparata da altri: prima il primo suggerimento per \"mario\" era l'indirizzo di chi l'aveva preparata e un messaggio di \"Mario Rossi\" da lì non aveva la fascia dei sosia " +
                  $"({firstBefore}, fascia {bandBefore ?? "nessuna"}); ora i suoi conteggi non entrano: riletta la Posta inviata di questo PC il primo suggerimento è la persona a cui scrivi ({firstAfter}) e quel messaggio ha la fascia " +
                  $"({bandAfter ?? "nessuna"}); prima di quella rilettura nessuno ha conteggi, e la fascia non c'è ancora ({bandEmpty ?? "nessuna"}); gli indirizzi della copia restano fra i suggerimenti ({suggested})");
        }
        finally
        {
            AddressBook.TestBook(null);
            (Store.Settings.Accounts, AddressBook.Contacts) = (accounts, contacts);
        }

        // ---------------------------------------------------------------- una copia fatta da Missiva, e quelle che non si importano
        var source = Path.Combine(root, "t17-partenza");
        Store.TryWrite(source, settings, null, out _);
        const string own = "{\"Entries\":[{\"Name\":\"Maria Rossi\",\"Email\":\"maria.rossi@clienti-prova.example\",\"Received\":7,\"Sent\":4,\"LastReceived\":\"2026-10-01T10:00:00\",\"LastSent\":\"2026-10-02T10:00:00\"}]," +
                           "\"SentScans\":{\"imap-prova\":\"2026-10-02T10:00:00Z\"}}";
        File.WriteAllBytes(Path.Combine(source, "rubrica.json"), SafeFile.Protect(System.Text.Encoding.UTF8.GetBytes(own)));
        var collected = Backup.Collect(Store.Read(source, out _), source, withPasswords: false, out _);
        var read = Backup.Read(Backup.Write(collected, Copy2Password), Copy2Password);
        var summary = Backup.Describe(read);
        bool exported = collected.AddressBook == own && Entries(read.AddressBook!) is [{ Name: "Maria Rossi", Email: "maria.rossi@clienti-prova.example", Sent: 0, Received: 0 }] && summary.AddressBook == 1;
        string Read(string addressBook) => Copy2Read(Content(addressBook), out _, out _);
        string wrongType = Read(own.Replace("\"Sent\":4", "\"Sent\":\"quattro\"")), nullEntry = Read("{\"Entries\":[null]}"), notList = Read("{\"Entries\":7}"),
               nullList = Read("{\"Entries\":null,\"SentScans\":null}"), nullName = Read("{\"Entries\":[{\"Name\":null,\"Email\":\"maria.rossi@clienti-prova.example\"}]}"), empty = Read("{}");
        Check(exported && wrongType == "Damaged" && nullEntry == "Damaged" && notList == "Damaged" && nullList == "letta" && nullName == "letta" && empty == "letta",
              $"scelta 22 (T-17), una copia fatta da Missiva: nel file la rubrica c'è com'è sul PC, e chi la apre per importarla la riceve con gli stessi nomi e indirizzi e i conteggi a zero ({exported}; la conferma dice ancora " +
              $"\"{summary.Contents}\"); una rubrica che non è quella che Missiva scrive (un conteggio che non è un numero, una voce null, un elenco che non è un elenco) fa rifiutare la copia ({wrongType}, {nullEntry}, {notList}), " +
              $"una proprietà scritta null vale come assente ({nullList}, {nullName})");
    }

    /// <summary>
    /// Scelta 14 (C-32): rimuovendo un account Google, se la revoca presso Google non riesce lo si dice a schermo dopo la
    /// rimozione, con dove toglierla a mano. Prima era una riga del registro. Con un Google finto (GoogleAuth.TestHttp):
    /// quando l'avviso c'è e quando no, che cosa dice, e che nella versione di prova la revoca non parte e l'avviso non
    /// c'è. La finestra di Rimuovi account non si apre qui: si legge nel sorgente che l'avviso venga dopo la rimozione.
    /// </summary>
    static async Task Ch1GoogleRevokeAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-scelte1-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        const string token = "aggiornamento-finto-scelta14";
        var requests = new List<string>();
        Func<HttpResponseMessage> answer = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") };
        string? noRead = Environment.GetEnvironmentVariable("MISSIVA_NO_READ");
        try
        {
            Log.TestDir = dir;
            GoogleAuth.TestHttp = new HttpClient(new FakeHttp(request =>
            {
                requests.Add(request.RequestUri!.ToString());
                return answer();
            }));
            var account = new MailAccount { Kind = AccountKind.Gmail, Email = "mario.prova@gmail.example", ProtectedRefreshToken = Store.Protect(token) };
            static HttpResponseMessage Refused(System.Net.HttpStatusCode code, string body) => new(code) { Content = new StringContent(body) };
            async Task<(GoogleRevocation Outcome, (string Heading, string Body)? Notice)> RemoveAsync(MailAccount which)
            {
                var outcome = await GoogleAuth.SignOutAsync(which);
                return (outcome, GoogleAuth.RevocationNotice(which, outcome));
            }

            var revoked = await RemoveAsync(account);
            answer = () => Refused(System.Net.HttpStatusCode.BadRequest, "{\"error\":\"invalid_token\",\"error_description\":\"Token expired or revoked\"}");
            var expired = await RemoveAsync(account);
            answer = () => Refused(System.Net.HttpStatusCode.BadRequest, "{\"error\":\"invalid_request\"}");
            var refused = await RemoveAsync(account);
            answer = () => Refused(System.Net.HttpStatusCode.BadRequest, "non è JSON");
            var garbled = await RemoveAsync(account);
            answer = () => Refused(System.Net.HttpStatusCode.ServiceUnavailable, "");
            var busy = await RemoveAsync(account);
            answer = () => throw new HttpRequestException("rete di prova assente");
            var offline = await RemoveAsync(account);
            int asked = requests.Count;
            var none = await RemoveAsync(new MailAccount { Kind = AccountKind.Gmail, Email = "senza.accesso@gmail.example" });
            // La versione di prova: la revoca non parte, e non c'è nulla da dire.
            Environment.SetEnvironmentVariable("MISSIVA_NO_READ", "1");
            var trial = await RemoveAsync(account);
            Environment.SetEnvironmentVariable("MISSIVA_NO_READ", noRead);
            int after = requests.Count;

            var quiet = new[] { revoked, expired, none, trial };
            var told = new[] { refused, garbled, busy, offline };
            Check(revoked.Outcome == GoogleRevocation.Revoked && expired.Outcome == GoogleRevocation.NotNeeded && none.Outcome == GoogleRevocation.NotNeeded && trial.Outcome == GoogleRevocation.NotNeeded
                  && quiet.All(q => q.Notice == null) && told.All(t => t.Outcome == GoogleRevocation.Failed && t.Notice != null) && asked == 6 && after == 6,
                  $"scelta 14 (C-32), rimozione di un account Google: l'avviso c'è solo quando la revoca è stata chiesta e non è riuscita (un 400 che non dice \"invalid_token\", una risposta che non si legge, un 503, la rete che cade: " +
                  $"{told.Count(t => t.Notice != null)} su {told.Length}); non c'è quando Google revoca, quando risponde che l'autorizzazione non valeva già più, senza un accesso salvato e nella versione di prova, " +
                  $"dove la richiesta non parte nemmeno ({quiet.Count(q => q.Notice == null)} su {quiet.Length}; richieste a Google {after}, tutte prima)");

            var (heading, body) = offline.Notice ?? ("", "");
            var log = File.ReadAllText(Path.Combine(dir, "missiva.log"));
            Check(heading == "Account rimosso, ma l'accesso di Google può restare" && body.StartsWith("mario.prova@gmail.example è uscito da Missiva") && body.Contains("Google non ha confermato la revoca")
                  && body.Contains("si toglie a mano", StringComparison.OrdinalIgnoreCase) && body.Contains("myaccount.google.com/permissions") && !body.Contains(token) && !body.Contains('\n') && body.Length < 400
                  && !log.Contains(token) && log.Contains("l'autorizzazione non era più valida") && log.Contains("prova: chiederei a Google di revocare"),
                  $"scelta 14 (C-32), l'avviso dice che l'account è uscito da Missiva, che Google non ha confermato la revoca e dove toglierla a mano, in un paragrafo solo e senza il token (\"{body}\")");

            // Nella finestra: l'esito della revoca si tiene, la rimozione si fa, e l'avviso viene per ultimo (lettura del sorgente).
            if (Ch1Code("UI", "MainWindow.xaml.cs") is { } main)
            {
                int start = main.IndexOf("public async Task RemoveAccountAsync(MailAccount account)", StringComparison.Ordinal), end = main.IndexOf("internal static string OutboxRemovalText(", StringComparison.Ordinal);
                string remove = start >= 0 && end > start ? main[start..end] : "";
                int At(string text) => remove.IndexOf(text, StringComparison.Ordinal);
                Check(At("revocation = await GoogleAuth.SignOutAsync(account);") > 0 && At("S.Accounts.RemoveAll(") > At("GoogleAuth.SignOutAsync(account)") && At("await LoadAccountsAsync();") > At("S.Accounts.RemoveAll(")
                      && At("if (GoogleAuth.RevocationNotice(account, revocation) is var (heading, body)) Dialog.Show(this, heading, body);") > At("await LoadAccountsAsync();"),
                      "scelta 14 (C-32), Rimuovi account tiene l'esito della revoca di Google, toglie l'account, ricarica gli account e solo allora mostra l'avviso, se c'è (lettura del sorgente: nessuna prova crea la finestra)");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("MISSIVA_NO_READ", noRead);
            GoogleAuth.TestHttp = null;
            Log.TestDir = null;
            DeleteQuietly(dir);
        }
    }

    /// <summary>
    /// Scelta 10 (T-06, e la scelta in attesa dal blocco 2 del 4 ottobre): un mittente vale come autenticato, per il logo e
    /// per l'annullamento dell'iscrizione con un clic, solo con DMARC superato. Prima, senza un risultato di DMARC, bastava
    /// una firma DKIM o un SPF dello stesso dominio a due etichette: la firma di un altro cliente della stessa piattaforma
    /// autenticava la piattaforma. La fascia rossa (chi fallisce) e l'intestazione di Exchange senza nome non cambiano. I
    /// domini che avevano il segno solo per la regola di prima lo perdono, una volta, rileggendo le copie dei corpi.
    /// </summary>
    static void Ch1Dmarc()
    {
        // Domini nuovi a ogni giro: domini.dat è della cartella dei dati del giro, ma altri gruppi la usano prima e dopo.
        string tag = Guid.NewGuid().ToString("N")[..8];
        string shop = $"negozio-{tag}.example", platform = $"piattaforma-{tag}.example";
        static SenderCheck.Results P(string header) => SenderCheck.Parse(header);
        static string Pass(string d) => $"mx.google.com; dkim=pass header.i=@{d} header.s=s1; spf=pass smtp.mailfrom=rimbalzi@posta.{d}; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from={d}";
        static string Signed(string d) => $"mx.google.com; dkim=pass header.i=@{d} header.s=s1; spf=pass smtp.mailfrom=x@invii-prova.example";
        static string Fail(string d) => $"mx.google.com; dkim=none; spf=softfail smtp.mailfrom=x@{d}; dmarc=fail (p=NONE sp=NONE dis=NONE) header.from={d}";
        string exchange = $"spf=pass (sender IP is 192.0.2.1) smtp.mailfrom={shop}; dkim=pass (signature was verified) header.d={shop};dmarc=pass action=none header.from={shop};compauth=pass reason=100";

        // ---------------------------------------------------------------- la regola: com'era (Aligned) e com'è
        (string Name, string Header, string Domain, bool Before, bool Now)[] forms =
        [
            ("DMARC superato secondo Gmail", Pass(shop), shop, true, true),
            ("DMARC superato da un sottodominio", $"mx.google.com; dkim=pass header.i=@posta.{shop}; dmarc=pass (p=NONE) header.from=posta.{shop}", shop, true, true),
            ("DMARC superato secondo Exchange", exchange, shop, true, true),
            ("DMARC superato senza header.from, con la firma del dominio", $"mx.google.com; dkim=pass header.d={shop}; dmarc=pass", shop, true, true),
            ("senza DMARC, firma DKIM del dominio", Signed(shop), shop, true, false),
            ("senza DMARC, SPF del dominio", $"mx.google.com; dkim=none; spf=pass smtp.mailfrom=rimbalzi@posta.{shop}", shop, true, false),
            ("bestguesspass di Microsoft", $"spf=pass smtp.mailfrom=posta.{shop}; dkim=none; dmarc=bestguesspass action=none header.from={shop}", shop, true, false),
            ("dmarc=none, firma del dominio", $"mx.google.com; dkim=pass header.d={shop}; dmarc=none header.from={shop}", shop, true, false),
            ("senza DMARC, la firma di un altro cliente della piattaforma", $"mx.google.com; dkim=pass header.d=cliente2.{platform}; spf=none smtp.mailfrom=x@cliente2.{platform}", platform, true, false),
            ("DMARC superato per un altro dominio", $"mx.google.com; dkim=pass header.d={shop}; dmarc=pass header.from=altro-prova.example", shop, false, false),
            ("DMARC superato senza header.from, con la firma di un altro", "mx.google.com; dkim=pass header.d=invii-prova.example; dmarc=pass", shop, false, false),
            ("DMARC fallito", Fail(shop), shop, false, false),
        ];
        var wrong = forms.Where(f => SenderCheck.Aligned(P(f.Header), f.Domain) != f.Before || SenderCheck.Authenticated(P(f.Header), f.Domain) != f.Now)
                         .Select(f => $"{f.Name}: prima {SenderCheck.Aligned(P(f.Header), f.Domain)}, ora {SenderCheck.Authenticated(P(f.Header), f.Domain)}").ToList();
        Check(wrong.Count == 0,
              $"scelta 10 (T-06), autenticato solo con DMARC superato: di {forms.Length} intestazioni {forms.Count(f => f.Now)} valgono (DMARC superato per il dominio del From, anche da un sottodominio, secondo Gmail e secondo Exchange), " +
              $"{forms.Count(f => f.Before && !f.Now)} valevano fino all'11 ottobre e non valgono più (senza DMARC la firma DKIM o l'SPF del dominio, \"bestguesspass\", dmarc=none, la firma di un altro cliente della stessa piattaforma), " +
              $"{forms.Count(f => !f.Before)} non valevano nemmeno prima" + (wrong.Count == 0 ? "" : $" [NON VA: {string.Join("; ", wrong)}]"));

        // ---------------------------------------------------------------- quello che non cambia: la fascia rossa, Exchange
        var gmail = new MailAccount { Kind = AccountKind.Gmail, Email = "maria.prova@gmail.example" };
        var microsoft = new MailAccount { Kind = AccountKind.Microsoft, Email = "maria.prova@outlook.example" };
        var now = DateTime.Now;
        MessageSummary Mail(string id, string domain, DateTime date) =>
            new() { Id = id + tag, FolderId = "INBOX", StableId = id + tag, From = new Address { Name = "Negozio di prova", Email = "notizie@posta." + domain }, Subject = "Prova", Date = date };
        static MessageBody Body(string results) => new() { Html = "<p>Prova</p>", AuthenticationResults = [results] };
        string? red = SenderCheck.Of(gmail, Mail("0", shop, now), Body(Fail(shop))).Unverified, quiet = SenderCheck.Of(gmail, Mail("0", shop, now), Body(Signed(shop))).Unverified;
        bool softfail = SenderCheck.Failed(P($"mx.google.com; dkim=none; spf=softfail smtp.mailfrom=x@{shop}")), signedFails = SenderCheck.Failed(P(Signed(shop)));
        bool nameless = SenderCheck.Own(microsoft, [exchange]) is { AuthservId: null } own && SenderCheck.Authenticated(own, shop);
        Check(red == "posta." + shop && quiet == null && softfail && !signedFails && nameless,
              $"scelta 10 (T-06), quello che non cambia: la fascia rossa per chi fallisce c'è come prima (DMARC fallito: {red}; SPF fallito senza firma: {softfail}), un messaggio senza DMARC ma firmato non la prende " +
              $"({quiet ?? "nessuna"}: non è autenticato, non è nemmeno respinto), e l'intestazione di Exchange senza il nome di chi l'ha scritta autentica come prima ({nameless})");

        // ---------------------------------------------------------------- logo e annullamento con un clic
        string known = $"noto-{tag}.example", dormant = $"fermo-{tag}.example", forged = $"falso-{tag}.example", elsewhere = $"altrove-{tag}.example";
        string[] all = [shop, platform, known, dormant, forged, elsewhere];
        var imap = new MailAccount { Kind = AccountKind.Imap, Email = "studio@uno.example", Imap = new ImapServer { ImapHost = "posta.uno.example" } };
        string copies = Path.Combine(Path.GetTempPath(), "missiva-prova-scelte1-" + tag);
        var (savedRoot, savedMark) = (BodyCache.TestRoot, File.Exists(SenderLogos.RecheckMark));
        try
        {
            BodyCache.TestRoot = copies;
            SenderLogos.Note(Mail("1", shop, now.AddDays(-15)));
            var unsigned = Mail("2", shop, now);
            SenderLogos.NoteBody(gmail, unsigned, Body(Signed(shop)));
            bool markedWithout = SenderLogos.IsAuthenticated(shop), doubted = SenderLogos.IsDoubtful(gmail, unsigned), logoWithout = UI.Logos.DomainFor(unsigned.From) != null;
            var proven = Mail("3", shop, now);
            SenderLogos.NoteBody(gmail, proven, Body(Pass(shop)));
            bool marked = SenderLogos.IsAuthenticated(shop) && !SenderLogos.IsDoubtful(gmail, proven) && SenderLogos.IsDoubtful(gmail, unsigned) && UI.Logos.DomainFor(proven.From) == shop;
            Check(!markedWithout && doubted && !logoWithout && marked,
                  $"scelta 10 (T-06), logo: un dominio noto da 15 giorni di cui arriva un messaggio firmato ma senza DMARC non diventa autenticato ({markedWithout}) e quel messaggio resta senza logo; " +
                  $"lo diventa con il primo messaggio che supera DMARC ({marked}). Prima bastava il primo");

            const string list = "<https://liste.notizie-prova.example/via?id=1>, <mailto:via@liste.notizie-prova.example?subject=via>";
            UnsubscribeAction? Click(string results) => Unsubscribe.For(gmail, Mail("4", shop, now),
                new MessageBody { Html = "<p>Notizie</p>", AuthenticationResults = [results], ListUnsubscribe = list, ListUnsubscribePost = "List-Unsubscribe=One-Click" }, junk: false);
            Check(Click(Pass(shop)) is { Kind: UnsubscribeKind.OneClick } && Click(Signed(shop)) is { Kind: UnsubscribeKind.Mailto } && Click(Fail(shop)) == null,
                  $"scelta 10 (T-06), Annulla iscrizione: con un clic solo con DMARC superato e la firma del dominio; con la sola firma, senza DMARC, resta il messaggio da inviare ({Click(Signed(shop))?.Kind}; prima un clic); con la fascia rossa il comando non c'è");

            // ---------------------------------------------------------------- una volta sola: chi aveva il segno senza DMARC lo perde
            // Cinque domini con il segno di prima (TestMark): di uno le copie hanno solo messaggi firmati senza DMARC; di uno anche un
            // messaggio che supera DMARC; uno non ha copie; uno ha solo un From falsificato; uno ha copie solo in un account IMAP.
            foreach (var domain in new[] { platform, known, dormant, forged, elsewhere })
            {
                SenderLogos.Note(Mail("5" + domain, domain, now.AddDays(-20)));
                SenderLogos.Note(Mail("5b" + domain, domain, now));
                SenderLogos.TestMark(domain);
            }
            BodyCache.Save(gmail, Mail("6", platform, now), Body(Signed(platform)));
            BodyCache.Save(gmail, Mail("7", platform, now), Body($"mx.google.com; dkim=none; spf=pass smtp.mailfrom=rimbalzi@posta.{platform}"));
            BodyCache.Save(gmail, Mail("8", known, now), Body(Signed(known)));
            BodyCache.Save(gmail, Mail("9", known, now), Body(Pass(known)));
            BodyCache.Save(gmail, Mail("10", forged, now), Body(Fail(forged)));
            BodyCache.Save(imap, Mail("11", elsewhere, now), Body($"posta.uno.example; dkim=pass header.d={elsewhere}; spf=pass smtp.mailfrom=x@{elsewhere}"));
            Task[] writes;
            lock (BodyCache.TestWrites) writes = [.. BodyCache.TestWrites];
            Task.WaitAll(writes);
            var changed = new System.Collections.Concurrent.ConcurrentQueue<string>();
            Action<string> onChange = changed.Enqueue;
            bool allMarked = new[] { platform, known, dormant, forged, elsewhere, shop }.All(SenderLogos.IsAuthenticated);
            SenderLogos.Arrived += onChange;
            try
            {
                if (savedMark) File.Delete(SenderLogos.RecheckMark);
                SenderLogos.RecheckFromCopies([gmail, imap]);
            }
            finally { SenderLogos.Arrived -= onChange; }
            // Avvisato una volta per il dominio che perde il segno, mai per gli altri di questa prova (un logo di un altro gruppo
            // che arriva proprio adesso non conta).
            var told = changed.Where(all.Contains).ToList();
            bool lost = !SenderLogos.IsAuthenticated(platform) && UI.Logos.DomainFor(new Address { Email = "notizie@" + platform }) == null && told is [var only] && only == platform;
            bool kept = new[] { known, dormant, forged, elsewhere, shop }.All(SenderLogos.IsAuthenticated) && File.Exists(SenderLogos.RecheckMark);
            SenderLogos.TestReload(); // quello che è arrivato su domini.dat
            bool onDisk = !SenderLogos.IsAuthenticated(platform) && SenderLogos.IsKnown(platform) && SenderLogos.IsAuthenticated(known) && SenderLogos.IsAuthenticated(dormant);
            // Il secondo giro non fa nulla: un dominio segnato dopo non si tocca più.
            SenderLogos.TestMark(platform);
            SenderLogos.RecheckFromCopies([gmail, imap]);
            bool once = SenderLogos.IsAuthenticated(platform);
            Check(allMarked && lost && kept && onDisk && once,
                  $"scelta 10 (T-06), dopo l'aggiornamento, una volta sola: di cinque domini con il segno di prima lo perde solo quello che fra le copie sul PC ha messaggi firmati ma nessuno con DMARC superato ({lost}; chi mostra " +
                  $"i loghi viene avvisato, e su domini.dat resta noto ma non autenticato: {onDisk}); lo tengono quello con un messaggio che supera DMARC, quello senza copie, quello con il solo From falsificato e " +
                  $"quello con le copie in un account IMAP ({kept}); il giro dopo non tocca più nulla ({once})");
        }
        finally
        {
            BodyCache.TestRoot = savedRoot;
            DeleteQuietly(copies);
            if (!savedMark && File.Exists(SenderLogos.RecheckMark)) File.Delete(SenderLogos.RecheckMark);
            SenderLogos.TestForget(all);
        }
    }

    /// <summary>
    /// Scelta 5 (T-10): le pagine web allegate (.html, .htm, .shtml, .xhtml, .svg, .mht, .mhtml). Prima Apri le passava al
    /// browser senza una parola. Ora una conferma con un testo loro, da una classe a parte: non sono fra i tipi che
    /// avviano programmi, quindi niente rosso sulla scheda e "Salva tutti" le salva. La finestra della conferma non si
    /// apre qui: si provano la regola e il testo, e si legge nei sorgenti che Apri la chieda prima di scrivere il file.
    /// </summary>
    static void Ch1WebPages()
    {
        string[] pages = ["Accesso.html", "Accesso.HTM", "pagina.shtml", "pagina.xhtml", "logo.svg", "pagina.mht", "pagina.mhtml", "Fattura.pdf.html", "conto.html. "];
        string[] others = ["fattura.pdf", "foto.png", "nota.txt", "html", "pagina.html.pdf", "archivio.zip", "relazione.docx", "messaggio.eml", "fattura.xml.p7m"];
        string[] programs = ["programma.exe", "collegamento.url", "pagina.hta", "script.js"];
        bool noWindow = UI.Dialog.ConfirmOpenWebPage(null, "fattura.pdf") && UI.Dialog.ConfirmOpenWebPage(null, "programma.exe"); // chi non è una pagina web non chiede nulla: nessuna finestra
        Check(pages.All(Safety.IsWebPage) && !pages.Any(Safety.IsDangerous) && !others.Any(Safety.IsWebPage) && programs.All(Safety.IsDangerous) && !programs.Any(Safety.IsWebPage) && noWindow,
              $"scelta 5 (T-10), le pagine web allegate sono una classe a parte: chiedono la loro conferma {string.Join(", ", pages.Take(7).Select(p => Path.GetExtension(p).ToLowerInvariant()))} " +
              $"(anche con due estensioni, o con spazi e punti in fondo), e nessuna è fra i tipi che avviano programmi; PDF, immagini, testi, archivi, documenti, .eml e .p7m non la chiedono; " +
              $".exe, .url, .hta e .js restano fra quelli in rosso, con la loro domanda" +
              (pages.Where(p => !Safety.IsWebPage(p)).ToList() is { Count: > 0 } missed ? $" [non riconosciute: {string.Join(", ", missed)}]" : ""));

        var page = Safety.WebPageQuestion("Accesso alla banca.html");
        var drawing = Safety.WebPageQuestion("logo.svg");
        Check(page.Heading == "Aprire una pagina web arrivata per posta?" && page.Body.StartsWith("\"Accesso alla banca.html\" è una pagina web") && page.Body.Contains("per posta, non viene da un sito")
              && page.Body.Contains("Non scriverci password") && page.Primary == "Apri la pagina" && !page.Body.Contains("avviare programmi") && !page.Body.Contains("eseguire codice")
              && drawing.Body.StartsWith("\"logo.svg\" è un disegno che il browser apre come una pagina web") && drawing.Body.Contains("Non scriverci password") && drawing.Primary == "Apri il disegno",
              $"scelta 5 (T-10), la domanda prima di aprire una pagina web allegata dice che è una pagina arrivata per posta, che si apre fuori da Missiva e di non scriverci password; non è quella dei tipi che avviano programmi (\"{page.Body}\")");

        // Niente rosso: la scheda di una pagina web è quella di un allegato qualunque; quella di un programma resta rossa.
        var mail = new MessageSummary { Id = "1", FolderId = "INBOX", Subject = "Documenti", From = new Address { Name = "Maria Rossi", Email = "maria.rossi@studio-prova.example" } };
        var body = new MessageBody
        {
            Html = "<p>In allegato.</p>",
            Attachments = [new Attachment { Id = "0", Name = "Accesso.html", ContentType = "text/html", Size = 2000 }, new Attachment { Id = "1", Name = "logo.svg", ContentType = "image/svg+xml", Size = 900 },
                           new Attachment { Id = "2", Name = "programma.exe", ContentType = "application/octet-stream", Size = 5000 }],
        };
        string html = UI.ReadingPage.Build([new UI.PageEntry(mail, body, true, false, null, false)], light: true).Html;
        int red = System.Text.RegularExpressions.Regex.Matches(html, "class=\"chip danger\"").Count, chips = System.Text.RegularExpressions.Regex.Matches(html, "class=\"chip( danger)?\"").Count;
        Check(chips == 3 && red == 1 && html.Contains("Accesso.html") && html.Contains("logo.svg"),
              $"scelta 5 (T-10), nel riquadro la scheda di una pagina web allegata non è rossa: di tre allegati (.html, .svg, .exe) è rossa solo quella del programma ({red} su {chips})");

        // Apri chiede la conferma prima di scrivere il file e di passarlo a Windows, dal riquadro e dalla finestra dei file firmati.
        if (Ch1Code("UI", "ReadingView.xaml.cs") is { } reading && Ch1Code("UI", "P7mWindow.cs") is { } signed && Ch1Code("UI", "Dialog.cs") is { } dialog)
        {
            static string Method(string source, string start, string end) =>
                source.IndexOf(start, StringComparison.Ordinal) is >= 0 and var from && source.IndexOf(end, from, StringComparison.Ordinal) is var to && to > from ? source[from..to] : "";
            string open = Method(reading, "async Task OpenAttachmentAsync(", "bool savingAll;"), signedOpen = Method(signed, "void Open()", "catch (Exception ex)");
            int Before(string text, string first, string then) => text.IndexOf(first, StringComparison.Ordinal) is >= 0 and var a && text.IndexOf(then, StringComparison.Ordinal) > a ? 1 : 0;
            const string ask = "if (!save && !Dialog.ConfirmOpenWebPage(owner, name)) return;", askSigned = "if (!Dialog.ConfirmOpenWebPage(this, name)) return;";
            // Dalla revisione del 14 ottobre (U-03) il file da aprire lo scrive Safety.WriteTemp, dal riquadro e dalla finestra dei file firmati;
            // dai ritocchi del 16 ottobre 2026 dal riquadro passa dal gestore degli scaricamenti (DownloadList.FetchToTempAsync, con WriteTemp).
            const string write = "Safety.WriteTemp(name", writePane = "DownloadList.Session.FetchToTempAsync(name";
            bool fromPane = Before(open, ask, writePane) == 1 && Before(open, ask, "Process.Start(") == 1 && Before(open, "Safety.IsDangerous(name)", ask) == 1;
            bool fromSigned = Before(signedOpen, askSigned, write) == 1 && Before(signedOpen, askSigned, "Process.Start(") == 1;
            bool rule = Method(dialog, "public static bool ConfirmOpenWebPage(", "public static Choice AskCancelFirst(") is var confirm && confirm.Contains("if (!Safety.IsWebPage(fileName)) return true;")
                        && confirm.Contains("Safety.WebPageQuestion(fileName)") && confirm.Contains("== Choice.Primary");
            Check(fromPane && fromSigned && rule,
                  $"scelta 5 (T-10), Apri chiede la conferma delle pagine web prima di scrivere il file e di passarlo a Windows: dal riquadro, dopo la domanda dei tipi in rosso e solo per Apri, non per Salva ({fromPane}); " +
                  $"dalla finestra dei file firmati ({fromSigned}); la conferma chiede solo per le pagine web, con il loro testo ({rule})");
        }
    }

    /// <summary>
    /// Un sorgente del repository senza le righe di commento e senza i commenti fra "/*" e "*/" (una riga o un pezzo spento
    /// così non conta come codice), o null fuori dalla cartella dei sorgenti.
    /// </summary>
    static string? Ch1Code(params string[] parts) =>
        BehSource(parts) is { } source
            ? System.Text.RegularExpressions.Regex.Replace(string.Join("\n", source.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !l.TrimStart().StartsWith("//"))), @"/\*.*?\*/", "",
                                                           System.Text.RegularExpressions.RegexOptions.Singleline)
            : null;

    /// <summary>
    /// Scelta 4 (T-02): le immagini di un .eml allegato a un messaggio normale. Prima, con il From di un mittente
    /// consentito, si caricavano da sole (solo l'allegato di un messaggio della Posta indesiderata no): quel From non lo
    /// verifica nessuno. Ora mai da sole, e "Mostra immagini" vale per quella volta senza ricordare il mittente.
    /// </summary>
    static void Ch1EmlImages()
    {
        const string bank = "banca-prova.example";
        const string picture = "<p>Avviso</p><img src=\"https://immagini.banca-prova.example/a.png\" width=\"200\" height=\"100\" alt=\"Avviso\">";
        var before = Store.Settings.ImageSenders;
        try
        {
            Store.Settings.ImageSenders = [];
            Store.Settings.AllowImagesFrom("avvisi@" + bank);
            var account = new MailAccount { Kind = AccountKind.Imap, Email = "studio@uno.example", Imap = new ImapServer() };
            var attached = new MimeKit.MimeMessage { Subject = "Avviso" };
            attached.From.Add(new MimeKit.MailboxAddress("Banca di prova", "avvisi@" + bank));
            attached.Body = new MimeKit.BodyBuilder { HtmlBody = picture }.ToMessageBody();
            var eml = new EmlProvider(attached, new ImapProvider(account));
            var body = eml.GetBodyAsync(eml.Summary).GetAwaiter().GetResult();
            string? normal = UI.ReadingView.EmlJunk(eml, fromJunk: false);

            // Il controllo che la prova veda: lo stesso messaggio arrivato nella casella, da quel mittente, le carica da solo.
            var received = new MessageSummary { Id = "1", FolderId = "INBOX", Subject = "Avviso", From = new Address { Name = "Banca di prova", Email = "avvisi@" + bank } };
            bool inbox = UI.ReadingView.LoadsImages(received, body, "Spam", account: account);
            bool alone = UI.ReadingView.LoadsImages(eml.Summary, body, normal, account: eml.Account, eml: true);
            bool once = UI.ReadingView.LoadsImages(eml.Summary, body, normal, once: true, account: eml.Account, eml: true);
            Check(normal == null && inbox && !alone && once,
                  $"scelta 4 (T-02), un .eml con il From di un mittente consentito, allegato a un messaggio normale: le immagini non si caricano più da sole ({alone}), solo dopo \"Mostra immagini\" ({once}); " +
                  $"lo stesso messaggio arrivato nella casella da quel mittente le carica come prima ({inbox})");

            // "Mostra immagini" lì non ricorda nessuno.
            Store.Settings.ImageSenders = [];
            bool remembered = UI.ReadingView.RememberImages(eml.Account, eml.Summary, body, normal, eml: true);
            int kept = Store.Settings.ImageSenders.Count;
            bool rememberedInbox = UI.ReadingView.RememberImages(account, received, body, "Spam");
            Check(!remembered && kept == 0 && rememberedInbox && Store.Settings.ImageSenders.SequenceEqual([bank]),
                  $"scelta 4 (T-02), \"Mostra immagini\" in un .eml aperto vale per quella volta e non ricorda il mittente ({kept} ricordati); nel messaggio arrivato nella casella lo ricorda ({string.Join(", ", Store.Settings.ImageSenders)})");

            // La pagina: bloccate con il suo motivo, e mostrate dopo il clic.
            UI.ReadingPage Page(bool shown) => UI.ReadingPage.Build(
                [new UI.PageEntry(eml.Summary, body, true, false, null, UI.ReadingView.LoadsImages(eml.Summary, body, normal, shown, eml.Account, eml: true), SenderCheck.Of(eml.Account, eml.Summary, body),
                                  ImagesOnce: UI.ReadingView.ImagesOnce(eml.Account, eml.Summary, body, normal, eml: true))], light: true, header: new UI.PageHeader("Avviso", false));
            var blocked = Page(false);
            var visible = Page(true);
            Check(blocked.Blocked[0] == 1 && !blocked.RemoteAllowed && !blocked.Html.Contains("https://immagini." + bank) && blocked.Html.Contains("Mostra immagini")
                  && blocked.Html.Contains("aperto da un allegato") && blocked.Html.Contains("vale solo per questa volta") && !blocked.Html.Contains("in tutti gli account")
                  && visible.Blocked[0] == 0 && visible.RemoteAllowed && visible.Html.Contains($"https://immagini.{bank}/a.png"),
                  "scelta 4 (T-02), la pagina di un .eml aperto: l'immagine esterna è bloccata, la fascia dice che il messaggio viene da un allegato e che \"Mostra immagini\" vale solo per questa volta; dopo il clic l'immagine c'è");

            // Il riquadro lo dice a ogni decisione sulle immagini (lettura del sorgente: sono metodi della finestra).
            if (Ch1Code("UI", "ReadingView.xaml.cs") is { } source)
            {
                var calls = source.Split('\n').Select(l => l.Trim()).Where(l => l.Contains("LoadsImages(") || l.Contains("ImagesOnce(") || l.Contains("RememberImages(")).ToList();
                var without = calls.Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"\b(IsEml|eml)\b")).ToList();
                Check(calls.Count(l => l.Contains("IsEml")) == 5 && without.Count == 0 && source.Contains("bool IsEml => provider is EmlProvider;"),
                      $"scelta 4 (T-02), il riquadro dice che il messaggio è un .eml a ogni decisione sulle immagini: all'apertura, a ogni disegno (due volte), a \"Mostra immagini\" e al ridisegno dopo " +
                      $"({calls.Count(l => l.Contains("IsEml"))} chiamate su 5{(without.Count > 0 ? "; senza: " + string.Join(" | ", without) : "")})");
            }
        }
        finally
        {
            Store.Settings.ImageSenders = before;
        }
    }
}
#endif
