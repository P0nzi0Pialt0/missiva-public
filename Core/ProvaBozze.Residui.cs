#if DEBUG
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;

namespace Missiva;

/// <summary>
/// Audit dell'11 ottobre, codice, quarto gruppo (docs\audit\ESITO.md): residui, doppioni, file grandi e semplificazioni
/// (C-01..C-23, C-64..C-66). Quasi tutto il gruppo toglie o unisce codice che la suite provava già: qui stanno i pochi
/// controlli nuovi, dove una correzione o un'unione non era coperta.
/// Con "Missiva.exe --prova-audit risultato.txt residui" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void CleanupAuditTests()
    {
        Report.Add("--- Audit dell'11 ottobre, codice, quarto gruppo: residui, doppioni, file grandi, semplificazioni (C-01..C-23, C-64..C-66)");
        CleSignInOwner();
        CleSignInOwnerSources();
        CleSilence();
        CleGoogleApply();
        CleCustomMenu();
        CleCriticalRed();
        CleSameFiles();
        CleOldCopies();
    }

    /// <summary>
    /// C-06: la cifratura delle copie scritte in chiaro dalle versioni di prima (MailCache.EncryptOld), che gira 20 secondi
    /// dopo ogni avvio su tutta la cartella della copia locale. Nessuna prova la copriva. Qui una cartella finta: un
    /// elenco e un calendario in chiaro, com'erano prima del giro P3, fra elenchi già cifrati e migliaia di corpi; il primo
    /// giro cifra i due in chiaro e l'elenco si legge come prima, il secondo non trova nulla. La riga di misura dice
    /// quanto costa quel secondo giro, che è quello di ogni avvio: quanti file guarda, quanti ne legge per intero.
    /// </summary>
    static void CleOldCopies()
    {
        const int Lists = 60, Bodies = 2000, ListBytes = 30_000;
        string root = Path.Combine(Path.GetTempPath(), "missiva-prova-c06-" + Guid.NewGuid().ToString("N"));
        var saved = Store.TestDataRoot;
        try
        {
            Store.TestDataRoot = root;
            var account = new MailAccount { Id = "c06prova", Kind = AccountKind.Imap, Email = "mario.rossi@studio-prova.example", Imap = new ImapServer() };
            var page = new List<MessageSummary> { new() { Id = "7", FolderId = "INBOX", Subject = "Copia di prima, in chiaro", From = new Address { Name = "Maria Rossi", Email = "maria.rossi@studio-prova.example" } } };
            // L'elenco di una cartella scritto dal codice di oggi (per avere il nome del file), poi rimesso in chiaro com'era una volta.
            MailCache.SaveMessages(account, "INBOX", null, page);
            string dir = Path.Combine(root, "cache", account.Id), listFile = Directory.GetFiles(dir, "*.json").Single();
            File.WriteAllBytes(listFile, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(page));
            string calendarFile = Path.Combine(root, "cache", "calendario-c06prova.json");
            File.WriteAllText(calendarFile, "{\"Calendars\":[],\"Events\":[]}");
            // Il resto della cartella: elenchi già cifrati (qui byte qualunque che non cominciano come un JSON) e i corpi, in venti sottocartelle.
            var sealedList = new byte[ListBytes];
            Random.Shared.NextBytes(sealedList);
            sealedList[0] = 1;
            for (int i = 0; i < Lists; i++) File.WriteAllBytes(Path.Combine(dir, $"elenco{i:000}.json"), sealedList);
            var bodyBytes = new byte[1024];
            for (int i = 0; i < Bodies; i++)
            {
                string folder = Path.Combine(dir, "corpi", $"cartella{i % 20:00}");
                if (i < 20) Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, $"corpo{i:0000}.bin"), bodyBytes);
            }
            int first = 0, second = 0;
            MailCache.EncryptOldIn(Path.Combine(root, "cache"), ref first);
            bool sealedNow = File.ReadAllBytes(listFile)[0] is not ((byte)'[' or (byte)'{') && File.ReadAllBytes(calendarFile)[0] is not ((byte)'[' or (byte)'{');
            bool readable = MailCache.LoadMessages(account, "INBOX", null) is [{ Id: "7", Subject: "Copia di prima, in chiaro" }] && Calendars.Load(account) is { Events.Count: 0 };
            bool untouched = File.ReadAllBytes(Path.Combine(dir, "elenco000.json")).AsSpan().SequenceEqual(sealedList);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            MailCache.EncryptOldIn(Path.Combine(root, "cache"), ref second);
            double ms = clock.Elapsed.TotalMilliseconds;
            Check(first == 2 && second == 0 && sealedNow && readable && untouched,
                  $"C-06, le copie in chiaro delle versioni di prima si cifrano al primo giro e si leggono come prima: cifrati un elenco e un calendario ({first}), letti dopo ({readable}), " +
                  $"quelli già cifrati non toccati ({untouched}), niente da fare al giro dopo ({second})");
            Report.Add(FormattableString.Invariant($"      misura, C-06: il giro di ogni avvio, quando non c'è più nulla da cifrare, su una copia con {Lists + 2} file .json da {ListBytes / 1000} KB e {Bodies} corpi: ") +
                       FormattableString.Invariant($"{ms:0.0} ms, {(Lists + 2) * (long)ListBytes / 1_000_000.0:0.0} MB letti per guardare il primo byte di ogni .json; gira 20 secondi dopo l'avvio, su un thread in sottofondo"));
        }
        finally
        {
            Store.TestDataRoot = saved;
            DeleteQuietly(root);
        }
    }

    /// <summary>
    /// C-17: i file che Missiva cifra per intero con DPAPI e scrive con un file temporaneo e una rinomina (la copia degli
    /// elenchi, i corpi sul disco, la copia del calendario) restano quelli di prima: DPAPI dell'utente di Windows, senza
    /// altro, sul JSON. Andata e ritorno con i file scritti da un'altra compilazione: con
    /// MISSIVA_PROVA_FILE_DI_PRIMA=cartella il primo giro (cartella vuota) li scrive e i giri dopo li leggono soltanto, così
    /// si scrivono con la compilazione di prima e si leggono con quella di adesso. Senza la variabile, in una cartella
    /// temporanea: scritti da questa compilazione e riletti "come prima" (ProtectedData chiamata qui a mano), e scritti
    /// "come prima" e riletti da questa.
    /// </summary>
    static void CleSameFiles()
    {
        static byte[] Open(string path) => System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(path), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        static void WriteOld(string path, byte[] json) => File.WriteAllBytes(path, System.Security.Cryptography.ProtectedData.Protect(json, null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);

        string? given = Environment.GetEnvironmentVariable("MISSIVA_PROVA_FILE_DI_PRIMA");
        string root = given is { Length: > 0 } ? given : Path.Combine(Path.GetTempPath(), "missiva-prova-c17-" + Guid.NewGuid().ToString("N"));
        bool fromBefore = Directory.Exists(Path.Combine(root, "cache")); // già scritti da un giro di prima (un'altra compilazione)
        var (savedData, savedBodies) = (Store.TestDataRoot, BodyCache.TestRoot);
        try
        {
            Store.TestDataRoot = root;
            BodyCache.TestRoot = null;
            var account = new MailAccount { Id = "c17prova", Kind = AccountKind.Imap, Email = "mario.rossi@studio-prova.example", Imap = new ImapServer() };
            MessageSummary Mail(int n) => new()
            {
                Id = n.ToString(), FolderId = "INBOX", StableId = $"<{n}@studio-prova.example>", Subject = $"Preventivo n. {n} per la sala riunioni",
                From = new Address { Name = "Maria Rossi", Email = "maria.rossi@studio-prova.example" }, Preview = "Buongiorno, in allegato il preventivo.", Date = new DateTime(2026, 10, 1, 9, n, 0),
            };
            var list = new List<MessageSummary> { Mail(3), Mail(2), Mail(1) };
            var body = new MessageBody { Html = "<p>Buongiorno, in allegato il <b>preventivo</b> che avevamo concordato.</p>", To = [new Address { Name = "Mario Rossi", Email = account.Email }], ListUnsubscribe = "<https://liste.example/via>" };
            var calendar = new CalendarData
            {
                Calendars = [new CalendarInfo("cal-1", "Calendario", "#0F6CBD", true)], From = new DateTime(2026, 10, 5), To = new DateTime(2026, 10, 12),
                Events = [new CalendarEvent { Id = "ev-1", CalendarId = "cal-1", Subject = "Riunione di studio", Location = "Sala Rossa", Start = new DateTime(2026, 10, 6, 10, 0, 0), End = new DateTime(2026, 10, 6, 11, 0, 0) }],
            };
            if (!fromBefore)
            {
                MailCache.SaveMessages(account, "INBOX", null, list);
                BodyCache.Save(account, list[0], body);
                Task[] writes;
                lock (BodyCache.TestWrites) writes = BodyCache.TestWrites.ToArray();
                Task.WaitAll(writes);
                Calendars.Save(account, calendar);
            }
            // Ritorno: i tre file letti dal codice di questa compilazione.
            static string Rows(IEnumerable<MessageSummary> messages) => Json(messages.Select(m => new { m.Id, m.FolderId, m.StableId, m.Subject, m.From.Name, m.From.Email, m.Preview, m.Date }));
            bool readList = MailCache.LoadMessages(account, "INBOX", null) is { Count: 3 } gotList && Rows(gotList) == Rows(list);
            bool readBody = BodyCache.Load(account, list[0]) is { } gotBody && gotBody.Html == body.Html && gotBody.To is [{ Email: var to }] && to == account.Email && gotBody.ListUnsubscribe == body.ListUnsubscribe;
            bool readCalendar = Calendars.Load(account) is { } gotCalendar && Json(gotCalendar) == Json(calendar);

            // Come stanno sul disco: aperti a mano come li apriva il codice di prima, sono JSON e nient'altro; nessun file temporaneo rimasto.
            var files = Directory.GetFiles(Path.Combine(root, "cache"), "*", SearchOption.AllDirectories);
            string listFile = files.Single(f => f.EndsWith(".json") && !Path.GetFileName(f).StartsWith("calendario-")), bodyFile = files.Single(f => f.EndsWith(".bin")),
                   calendarFile = files.Single(f => Path.GetFileName(f).StartsWith("calendario-"));
            bool plain = Open(listFile) is [(byte)'[', ..] && Open(bodyFile) is [(byte)'{', ..] && Open(calendarFile) is [(byte)'{', ..] && files.Length == 3;

            // Andata "come prima": gli stessi tre file riscritti a mano come li scriveva il codice di prima, con un altro contenuto, e riletti da questo.
            // Solo nella cartella temporanea: quelli della cartella data devono restare come li ha scritti la compilazione che li ha scritti.
            bool oldToNew = true, byHand = given is not { Length: > 0 };
            if (byHand)
            {
                var list2 = new List<MessageSummary> { Mail(9) };
                WriteOld(listFile, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(list2));
                var saved = System.Text.Json.Nodes.JsonNode.Parse(Open(bodyFile))!;
                saved["Html"] = "<p>Scritto come prima.</p>";
                WriteOld(bodyFile, System.Text.Encoding.UTF8.GetBytes(saved.ToJsonString()));
                var calendar2 = new CalendarData { Calendars = calendar.Calendars, From = calendar.From, To = calendar.To };
                WriteOld(calendarFile, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(calendar2));
                oldToNew = MailCache.LoadMessages(account, "INBOX", null) is [{ Id: "9" }] && BodyCache.Load(account, list[0])?.Html == "<p>Scritto come prima.</p>" && Calendars.Load(account) is { Events.Count: 0, Calendars.Count: 1 };
            }
            // I due aiuti da soli: la cifratura è quella di prima nei due versi, e la scrittura con il file temporaneo
            // mette il file nuovo al posto del vecchio senza lasciare il temporaneo.
            byte[] sample = System.Text.Encoding.UTF8.GetBytes("{\"prova\":\"C-17, àèìòù\"}");
            string copy = Path.Combine(root, "prova-c17.dat");
            bool helpers = SafeFile.Unprotect(System.Security.Cryptography.ProtectedData.Protect(sample, null, System.Security.Cryptography.DataProtectionScope.CurrentUser)).AsSpan().SequenceEqual(sample)
                           && System.Security.Cryptography.ProtectedData.Unprotect(SafeFile.Protect(sample), null, System.Security.Cryptography.DataProtectionScope.CurrentUser).AsSpan().SequenceEqual(sample);
            File.WriteAllBytes(copy, [1, 2, 3]);
            SafeFile.WriteCopy(copy, sample);
            helpers &= File.ReadAllBytes(copy).AsSpan().SequenceEqual(sample) && !File.Exists(copy + ".tmp");
            File.Delete(copy);
            // Nei sorgenti DPAPI si chiama in un file solo (Store.cs: SafeFile e Store.ProtectBytes).
            var direct = CleSources().Where(s => s.Text.Contains("ProtectedData.")).Select(s => s.Name).ToList();
            helpers &= direct.Count == 0 || direct is ["Store.cs"];
            Check(readList && readBody && readCalendar && plain && oldToNew && helpers,
                  "C-17, la copia degli elenchi, i corpi sul disco e la copia del calendario sono gli stessi file di prima (DPAPI dell'utente sul JSON, e nient'altro): " +
                  (fromBefore ? $"scritti da un'altra compilazione in {root} e letti da questa" : "scritti da questa compilazione e letti") +
                  $" (elenco {readList}, corpo {readBody}, calendario {readCalendar}), aperti a mano come li apriva il codice di prima ({plain})" +
                  (byHand ? $", e scritti a mano come li scriveva il codice di prima e letti da questa ({oldToNew})" : "") +
                  $"; la cifratura e la scrittura con il file temporaneo, da sole, e DPAPI chiamata in un file solo ({helpers}: {string.Join(", ", direct)})");
        }
        finally
        {
            (Store.TestDataRoot, BodyCache.TestRoot) = (savedData, savedBodies);
            if (given is not { Length: > 0 }) DeleteQuietly(root);
        }
    }

    /// <summary>
    /// C-16: il rosso degli errori nelle finestre. Era #C42B1C scritto in ogni finestra, uguale nei due temi; ora è il
    /// rosso del tema (la risorsa CriticalBrush), preso con un riferimento che segue il tema anche a finestra aperta.
    /// Qui non c'è l'applicazione con le sue risorse: la risorsa si mette in un riquadro che fa da finestra, e si cambia
    /// come fa App.SetTextColors al cambio di tema. Poi i sorgenti: il rosso fisso resta solo dove non è un errore.
    /// </summary>
    static void CleCriticalRed()
    {
        System.Windows.Media.Brush light = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC5, 0x0F, 0x1F)),
                                   dark = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x99, 0xA4));
        var error = UI.Dialog.ErrorText(14);
        var card = new System.Windows.Controls.Border { Child = error };
        UI.Dialog.Critical(card);
        var window = new System.Windows.Controls.ContentControl { Content = card };
        window.Resources[UI.Dialog.CriticalBrush] = light;
        bool made = error.Visibility == Visibility.Collapsed && error.Margin == new Thickness(0, 14, 0, 0) && error.TextWrapping == TextWrapping.Wrap;
        bool inLight = ReferenceEquals(error.Foreground, light) && ReferenceEquals(card.BorderBrush, light);
        window.Resources[UI.Dialog.CriticalBrush] = dark; // il tema cambia con la finestra aperta
        bool inDark = ReferenceEquals(error.Foreground, dark) && ReferenceEquals(card.BorderBrush, dark);

        // Nei sorgenti dell'interfaccia il rosso fisso resta in cinque posti che non sono testi d'errore: la riga dell'ora
        // nel calendario, il colore di ripiego del numero sull'icona, il tono rosso delle icone dei menu (che ha già il suo
        // valore per lo scuro), il segno del colore del testo nella scrittura e l'icona di un allegato pericoloso nella pagina.
        var fixedRed = new List<string>();
        if (SourceRoot() is { } root)
            foreach (var file in Directory.GetFiles(Path.Combine(root, "UI")).Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml")))
            {
                int n = Regex.Matches(File.ReadAllText(file), @"0xC4,\s*0x2B,\s*0x1C|#C42B1C", RegexOptions.IgnoreCase).Count;
                if (n > 0) fixedRed.Add($"{Path.GetFileName(file)} {n}");
            }
        else Report.Add("      (sorgenti non trovati accanto all'eseguibile: il rosso fisso nelle finestre non è stato cercato)");
        string[] allowed = ["CalendarView.cs 1", "ComposeWindow.xaml 1", "ContextMenus.cs 1", "MainWindow.xaml.cs 1", "ReadingPage.cs 1"];
        bool read = SourceRoot() == null || fixedRed.Order().SequenceEqual(allowed);
        Check(made && inLight && inDark && read,
              $"C-16, il testo d'errore delle finestre prende il rosso del tema e lo segue a finestra aperta: nascosto con il suo margine ({made}), nel chiaro ({inLight}), poi nello scuro senza rifarlo ({inDark}); " +
              $"nei sorgenti il rosso fisso #C42B1C resta solo dove non è un errore ({string.Join(", ", fixedRed.Order())})");
    }

    /// <summary>
    /// C-15: il riempimento dei menu del tasto destro (cartelle e messaggi), ora in un metodo solo. Nessuna prova costruiva
    /// quei menu, perché serve la finestra principale: qui il metodo da solo, con l'ordine e le voci nascoste scelti
    /// nelle impostazioni (rimessi alla fine) e una funzione che per alcune voci non dà nulla, come fanno i due menu veri
    /// (Rinomina su una cartella di sistema, Posponi fuori dall'elenco).
    /// </summary>
    static void CleCustomMenu()
    {
        var settings = Store.Settings;
        var (order, hidden) = (settings.FolderMenuOrder, settings.FolderMenuHidden);
        var (messageOrder, messageHidden) = (settings.MessageMenuOrder, settings.MessageMenuHidden);
        try
        {
            const string Sep = UI.ContextMenus.Separator;
            // Due separatori in cima, due di fila in mezzo, uno fra due voci che non ci sono, una voce nascosta, uno in fondo.
            settings.FolderMenuOrder = [Sep, Sep, "count", "newFolder", Sep, Sep, "rename", Sep, "color", Sep, "move", "delete", "empty", "markRead", "favorite", "up", "down", "window", "hide", "resetOrder", Sep];
            settings.FolderMenuHidden = ["newFolder", "down"];
            string[] absent = ["color", "up", "window", "hide", "resetOrder"]; // voci che qui non valgono: la funzione dà null
            var asked = new List<string>();
            System.Windows.Controls.MenuItem? Build(string key)
            {
                asked.Add(key);
                return absent.Contains(key) ? null : new System.Windows.Controls.MenuItem { Header = key };
            }
            var menu = new System.Windows.Controls.ContextMenu();
            UI.MainWindow.FillCustomMenu(menu, UI.ContextMenus.Folders, Build, null);
            var shown = menu.Items.Cast<object>().Select(i => i is System.Windows.Controls.Separator ? "|" : (i as System.Windows.Controls.MenuItem)?.Header as string ?? "?").ToList();
            string got = string.Join(" ", shown);
            // Le nascoste non si chiedono nemmeno; i separatori restano solo fra due voci presenti; in fondo un separatore e Personalizza.
            bool right = got == "count | rename | move delete empty markRead favorite | Personalizza il menu…" && !asked.Contains("newFolder") && !asked.Contains("down") && !asked.Contains(Sep);

            // Senza scelte salvate vale l'ordine di base, e se nessuna voce c'è resta la sola Personalizza, senza separatore sopra.
            settings.FolderMenuOrder = settings.MessageMenuOrder = null;
            settings.FolderMenuHidden = settings.MessageMenuHidden = null;
            var empty = new System.Windows.Controls.ContextMenu();
            UI.MainWindow.FillCustomMenu(empty, UI.ContextMenus.Folders, _ => null, null);
            var all = new System.Windows.Controls.ContextMenu();
            UI.MainWindow.FillCustomMenu(all, UI.ContextMenus.Messages, key => new System.Windows.Controls.MenuItem { Header = key }, null);
            var allShown = all.Items.Cast<object>().Select(i => i is System.Windows.Controls.Separator ? UI.ContextMenus.Separator : (i as System.Windows.Controls.MenuItem)?.Header as string ?? "?").ToList();
            var expected = UI.ContextMenus.Messages.DefaultOrder.Where(k => !UI.ContextMenus.Messages.DefaultHidden.Contains(k)).Append(UI.ContextMenus.Separator).Append("Personalizza il menu…").ToList();
            bool basic = empty.Items.Count == 1 && allShown.SequenceEqual(expected);
            Check(right && basic,
                  $"C-15, i menu del tasto destro di cartelle e messaggi si riempiono con lo stesso metodo: voci nascoste fuori e mai chieste, separatori solo fra due voci presenti, in fondo Personalizza " +
                  $"(\"{got}\"); senza scelte salvate l'ordine di base dei messaggi con le sue {UI.ContextMenus.Messages.DefaultHidden.Length} voci spente, e con nessuna voce la sola Personalizza ({empty.Items.Count})");
        }
        finally
        {
            (settings.FolderMenuOrder, settings.FolderMenuHidden) = (order, hidden);
            (settings.MessageMenuOrder, settings.MessageMenuHidden) = (messageOrder, messageHidden);
        }
    }

    /// <summary>
    /// C-14: un nuovo accesso a Google messo nell'account da un posto solo (GoogleAuth.Apply). I tre punti che lo chiamano
    /// aprono il browser e la suite non ci arriva: qui si prova la funzione, con un accesso in cui Google non ha ridato il
    /// calendario, e nei sorgenti si guarda che i tre punti passino di lì e che nessun altro scriva il token a mano.
    /// </summary>
    static void CleGoogleApply()
    {
        const string Mail = "https://mail.google.com/";
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "mario.prova@gmail.example", ProtectedRefreshToken = Store.Protect("accesso di prima"), GoogleScopes = $"{Mail} {GoogleAuth.CalendarScope}" };
        var heard = new List<string>();
        void OnLost(MailAccount who, string notice) => heard.Add(who.Email + ": " + notice);
        GoogleAuth.PermissionsLost += OnLost;
        try
        {
            GoogleAuth.TestRemember(account, "ya29.prova-di-prima");
            bool before = GoogleAuth.TestRemembers(account);
            GoogleAuth.Apply(account, new GoogleAuth.SignInResult(account.Email, "Mario Rossi", "accesso nuovo", "ya29.prova-nuovo", Mail));
            bool token = Store.TryUnprotect(account.ProtectedRefreshToken, out var saved) && saved == "accesso nuovo";
            bool scopes = account.GoogleScopes == Mail && !GoogleAuth.HasCalendar(account);
            bool told = heard.Count == 1 && heard[0].StartsWith(account.Email) && heard[0].Contains("il calendario");
            bool forgotten = before && !GoogleAuth.TestRemembers(account);
            var sources = CleSources();
            var callers = sources.Where(s => s.Text.Contains("GoogleAuth.Apply(")).Select(s => s.Name).Order().ToList();
            // Fuori da GoogleAuth nessuno scrive più a mano il token di aggiornamento di un account che c'è già (quello di un account nuovo nasce con lui).
            var byHand = sources.Where(s => s.Name != "GoogleAuth.cs" && Regex.IsMatch(s.Text, @"\.ProtectedRefreshToken\s*=\s*Store\.Protect")).Select(s => s.Name).ToList();
            bool read = sources.Count == 0 || callers is ["AccountWindow.xaml.cs", "GmailProvider.Calendar.cs", "ManageAccountWindow.cs"] && byHand.Count == 0;
            if (sources.Count == 0) Report.Add("      (sorgenti non trovati accanto all'eseguibile: chi chiama GoogleAuth.Apply non è stato letto)");
            Check(token && scopes && told && forgotten && read,
                  $"C-14, un nuovo accesso a Google entra nell'account da un posto solo: token di aggiornamento cifrato e riletto ({token}), permessi quelli dati adesso ({scopes}), il calendario tolto detto una volta ({told}), " +
                  $"token di accesso di prima dimenticato ({forgotten}); nei sorgenti lo chiamano i tre punti di prima ({string.Join(", ", callers)}) e nessuno scrive il token a mano ({byHand.Count})");
        }
        finally
        {
            GoogleAuth.PermissionsLost -= OnLost;
            GoogleAuth.Forget(account);
        }
    }

    /// <summary>
    /// C-09: il tempo scaduto di MailKit sulle connessioni di Gmail e degli account IMAP. Prima IMAP scriveva 60.000 ms in
    /// due punti e Gmail non scriveva nulla, cioè prendeva il valore di base di MailKit. Ora i due numeri hanno un nome in
    /// MailLimits e ogni client che i due provider creano lo dice. Nessun valore è cambiato: quello di Gmail è ancora il
    /// valore di base di MailKit (se una versione nuova lo cambia, questo controllo cade e la differenza si vede).
    /// </summary>
    static void CleSilence()
    {
        int imapDefault, smtpDefault;
        using (var imap = new MailKit.Net.Imap.ImapClient()) imapDefault = imap.Timeout;
        using (var smtp = new MailKit.Net.Smtp.SmtpClient()) smtpDefault = smtp.Timeout;
        var sources = CleSources();
        string Source(string name) => sources.FirstOrDefault(s => s.Name == name).Text ?? "";
        string gmail = Source("GmailProvider.cs"), imapSource = Source("ImapProvider.cs");
        // Ogni "new ImapClient" e ogni client SMTP dei due file: quanti in tutto, e quanti con il tempo scaduto preso da MailLimits.
        const string Created = @"(?:ImapClient Client\b.*?=\s*new\(\)|new ImapClient\b|new MimeOptions\.DsnSmtpClient\([^)]*\))[^;]*;";
        int Named(string text, string limit) => Regex.Matches(text, Created).Count(m => m.Value.Contains("Timeout = MailLimits." + limit));
        int All(string text) => Regex.Matches(text, Created).Count;
        bool read = sources.Count > 0;
        if (!read) Report.Add("      (sorgenti non trovati accanto all'eseguibile: i client di Gmail e IMAP non sono stati letti)");
        // L'unico altro numero è quello della prova del server di un account IMAP (30 secondi), che è un'altra cosa.
        bool written = !read || All(gmail) == 4 && Named(gmail, "GmailSilenceMs") == 4 && All(imapSource) == 3 && Named(imapSource, "ImapSilenceMs") == 2 && imapSource.Contains("new ImapClient { Timeout = 30_000 }");
        Check(MailLimits.ImapSilenceMs == 60_000 && MailLimits.GmailSilenceMs == 120_000 && imapDefault == MailLimits.GmailSilenceMs && smtpDefault == MailLimits.GmailSilenceMs && written,
              $"C-09, il tempo scaduto di MailKit ha un nome e vale come prima: account IMAP {MailLimits.ImapSilenceMs} ms, Gmail {MailLimits.GmailSilenceMs} ms, che è ancora il valore di base di MailKit " +
              $"(IMAP {imapDefault}, SMTP {smtpDefault}); nei sorgenti i client di Gmail lo dicono tutti ({(read ? Named(gmail, "GmailSilenceMs") : -1)} su {(read ? All(gmail) : -1)}) e quelli degli account IMAP " +
              $"anche, tranne la prova del server ({(read ? Named(imapSource, "ImapSilenceMs") : -1)} su {(read ? All(imapSource) : -1)})");
        Report.Add($"      misura, C-09: un controllo dei nuovi messaggi si dà per fermo dopo {UI.App.StuckCheck.TotalSeconds:0} s; una connessione muta scade dopo {MailLimits.GmailSilenceMs / 1000} s con Gmail " +
                   $"e dopo {MailLimits.ImapSilenceMs / 1000} s con un account IMAP");
    }

    /// <summary>I sorgenti di Missiva senza le prove e gli strumenti: (nome del file, testo). Vuoto se l'eseguibile non gira dalla cartella di compilazione.</summary>
    static List<(string Name, string Text)> CleSources()
    {
        var found = new List<(string, string)>();
        if (SourceRoot() is not { } root) return found;
        foreach (var dir in new[] { "Core", "UI" })
            foreach (var file in Directory.GetFiles(Path.Combine(root, dir), "*.cs"))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("ProvaBozze") || name.StartsWith("DevTools")) continue;
                found.Add((name, File.ReadAllText(file)));
            }
        return found;
    }

    /// <summary>Una finestra di WPF vera, fuori dallo schermo e senza prendere il fuoco: serve il suo riferimento per Windows.</summary>
    static Window CleWindow(string title)
    {
        var window = new Window
        {
            Width = 240, Height = 120, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.ToolWindow, Title = title,
        };
        window.Show();
        return window;
    }

    /// <summary>
    /// C-13: sopra quale finestra si apre la finestra account di Windows. MSAL non ha un doppio di prova e senza un account
    /// la finestra vera non si apre: qui si guarda che cosa Missiva gli darebbe come genitore (MicrosoftAuth.CurrentOwner),
    /// con tre finestre vere. Prima ogni finestra scriveva MicrosoftAuth.Owner e dopo una regola, le Impostazioni o la
    /// scrittura nessuno lo rimetteva: la funzione rimasta teneva la finestra chiusa, e alla richiesta dopo dava il suo
    /// riferimento (il controllo dice quale: l'ultima riga rifà i conti di prima con una funzione uguale a quella).
    /// </summary>
    static void CleSignInOwner()
    {
        var saved = MicrosoftAuth.Owner;
        var open = new List<Window>();
        Window Open(string role)
        {
            var window = CleWindow("Missiva, prova C-13 (" + role + ")");
            open.Add(window);
            return window;
        }
        static IntPtr HandleOf(Window window) => new WindowInteropHelper(window).Handle;
        try
        {
            var main = Open("principale");
            var mainHandle = HandleOf(main);
            MicrosoftAuth.Owner = () => HandleOf(main);
            int waiting = MicrosoftAuth.TestAskedOwners;
            bool alone = mainHandle != IntPtr.Zero && MicrosoftAuth.CurrentOwner() == mainHandle;

            Window rule = Open("regola"), compose = Open("scrittura");
            IntPtr ruleHandle = HandleOf(rule), composeHandle = HandleOf(compose);
            MicrosoftAuth.OpenOver(rule);
            bool overRule = MicrosoftAuth.CurrentOwner() == ruleHandle;
            MicrosoftAuth.OpenOver(compose);
            MicrosoftAuth.OpenOver(compose); // un secondo clic sullo stesso pulsante: vale una volta
            bool overCompose = MicrosoftAuth.CurrentOwner() == composeHandle && MicrosoftAuth.TestAskedOwners == waiting + 2;
            Func<IntPtr> before = () => HandleOf(compose); // com'era: l'ultima finestra restava in MicrosoftAuth.Owner

            compose.Hide(); // la scrittura nella Posta in uscita resta nascosta, non chiusa: non fa da genitore
            bool hidden = MicrosoftAuth.CurrentOwner() == ruleHandle;
            compose.Show();
            rule.Close(); // chiusa la più vecchia: vale ancora la scrittura
            bool newest = MicrosoftAuth.CurrentOwner() == composeHandle && MicrosoftAuth.TestAskedOwners == waiting + 1;
            compose.Close();
            var after = MicrosoftAuth.CurrentOwner();
            var beforeAfter = before();
            Check(alone && overRule && overCompose && hidden && newest && after == mainHandle && MicrosoftAuth.TestAskedOwners == waiting,
                  "C-13, la finestra account di Windows si apre sopra la finestra che l'ha chiesta finché quella si vede, poi di nuovo sopra la principale: " +
                  $"da sola la principale ({alone}), sopra la regola ({overRule}), sopra la scrittura chiesta due volte ({overCompose}), non sopra la scrittura nascosta ({hidden}), " +
                  $"chiusa la regola ancora la scrittura ({newest}), chiuse tutte e due la principale ({after == mainHandle}) e nessuna finestra chiusa tenuta ({MicrosoftAuth.TestAskedOwners - waiting})");
            Report.Add($"      misura, C-13: com'era prima, chiusa la scrittura, alla richiesta dopo MSAL riceveva come genitore {beforeAfter} al posto della finestra principale ({mainHandle})");
        }
        finally
        {
            foreach (var window in open)
                try { window.Close(); } catch (InvalidOperationException) { }
            MicrosoftAuth.Owner = saved;
        }
    }

    /// <summary>
    /// C-13, nei sorgenti: MicrosoftAuth.Owner lo scrive solo la finestra principale, alla nascita; le finestre da cui si
    /// chiede un accesso o un consenso a Microsoft passano tutte da OpenOver. Una finestra nuova che scrive Owner
    /// rimetterebbe il difetto.
    /// </summary>
    static void CleSignInOwnerSources()
    {
        var sources = CleSources();
        if (sources.Count == 0)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: chi scrive MicrosoftAuth.Owner non è stato letto)");
            return;
        }
        var writers = sources.Where(s => Regex.IsMatch(s.Text, @"MicrosoftAuth\.Owner\s*=(?![=>])")).Select(s => s.Name).ToList();
        var askers = sources.Where(s => s.Text.Contains("MicrosoftAuth.OpenOver(")).Select(s => s.Name).Order().ToList();
        string[] expected = ["AccountWindow.xaml.cs", "AutoRepliesWindow.cs", "ComposeWindow.xaml.cs", "ManageAccountWindow.cs", "RuleWindow.cs", "RulesView.cs"];
        Check(writers is ["MainWindow.xaml.cs"] && expected.All(askers.Contains),
              $"C-13, nei sorgenti MicrosoftAuth.Owner lo scrive solo la finestra principale ({string.Join(", ", writers)}) e le finestre che chiedono un accesso a Microsoft passano da OpenOver ({string.Join(", ", askers)})");
    }
}
#endif
