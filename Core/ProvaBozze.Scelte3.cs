#if DEBUG
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using MimeKit;

namespace Missiva;

/// <summary>
/// Scelte del 12 ottobre 2026 dopo la verifica dei due audit dell'11 ottobre, terzo gruppo (docs\audit\ESITO.md): prove
/// nuove e due guardie per chi sviluppa (scelte 26, 30, 33 e 48 del riepilogo). Una prova per TestingProvider, che nella
/// versione di prova ferma le scritture sulla casella vera; le azioni sulle cartelle e "segna tutti come letti" sui
/// server finti; gli avvisi di nullità come errori solo in Debug; la Debug senza MISSIVA_DATA che si chiude sempre. Solo
/// dati finti e server finti su 127.0.0.1; nessuna finestra e nessun eseguibile avviato senza MISSIVA_DATA: di quello che
/// sta nelle finestre e in Program.Main si provano le funzioni che decidono, e si legge il sorgente senza i commenti
/// (Ch1Code). Con "Missiva.exe --prova-audit risultato.txt scelte3" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void Choices3Tests()
    {
        Report.Add("--- Scelte del 12 ottobre, terzo gruppo: prove e guardie per chi sviluppa (scelte 26, 30, 33, 48)");
        Ch3TestingProvider();
        Ch3TestingOnServersAsync().GetAwaiter().GetResult();
        Ch3FoldersAsync().GetAwaiter().GetResult();
        Ch3MarkAllReadAsync().GetAwaiter().GetResult();
        Ch3NullableWarnings();
        Ch3ClosesWithoutData();
    }

    // ---------------------------------------------------------------- scelta 26 (T-28): la Debug senza MISSIVA_DATA

    /// <summary>
    /// Scelta 26 (T-28): una versione con i ganci di prova avviata senza MISSIVA_DATA si chiude sempre, anche la Debug dalla
    /// sua cartella bin\Debug, che fino a qui partiva sui dati veri. Nessun eseguibile si avvia senza la variabile, nemmeno
    /// per provarlo: qui la funzione che decide (Program.ClosesWithoutData, che ora guarda solo la variabile), la riga
    /// per chi l'ha avviata, e il sorgente di Program.Main: la regola viene prima di tutto ciò che apre i dati, e chi si
    /// chiude non scrive nel registro né apre finestre.
    /// </summary>
    static void Ch3ClosesWithoutData()
    {
        string?[] missing = [null, ""];
        string[] given = [@"C:\Progetti\Missiva\prova-dati", @"C:\Progetti\Missiva\prova-bozze-dati\giro-1", "dati"];
        const string debug = @"C:\Progetti\Missiva\Missiva-sorgente\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Missiva.exe";
        var notice = Program.WithoutDataNotice(debug);
        Check(missing.All(Program.ClosesWithoutData) && given.All(d => !Program.ClosesWithoutData(d))
              && typeof(Program).GetMethod(nameof(Program.ClosesWithoutData), BindingFlags.Static | BindingFlags.NonPublic)!.GetParameters() is [{ Name: "data" } only] && only.ParameterType == typeof(string)
              && notice.Contains("MISSIVA_DATA") && notice.Contains("dati veri") && notice.Contains(@"strumenti\prova.ps1") && notice.EndsWith(debug) && !notice.Contains('\n'),
              $"scelta 26 (T-28), senza MISSIVA_DATA (mancante o vuota) una versione con i ganci di prova si chiude sempre, e con una cartella data parte: la regola guarda solo la variabile, non più " +
              $"la cartella dell'eseguibile né gli argomenti, quindi vale anche per la Debug in bin\\Debug. A chi l'ha avviata dice: \"{notice}\"");

        if (Ch1Code("Program.cs") is { } program && Ch1Code("Core", "Store.cs") is { } store)
        {
            var main = Ch3Part(program, "static void Main(string[] args)", "internal const string Build");
            const string rule = "if (ClosesWithoutData(Environment.GetEnvironmentVariable(\"MISSIVA_DATA\")))";
            // Prima di aprire i dati: il registro, il mutex, le impostazioni, l'applicazione. Prima della regola solo chi non li apre.
            // --versione (la prova di installa.ps1 sull'eseguibile appena compilato) esce per primo, e non apre nulla.
            bool first = Ch3InOrder(main, "if (args is [\"--versione\"]) return;", "PdfWorker.Serve()", "DevTools.TryRun(args)", "#if DEBUG || PROVA", rule, "TellClosedWithoutData();", "return;", "--prova-anteprima", "Log.Startup(", "Log.WatchUnhandled();",
                                    "new Mutex(", "UI.App.LoadSettingsEarly();", "new UI.App()")
                         && main.Split("ClosesWithoutData(").Length == 2;
            var before = Ch3Part(main, "static void Main(string[] args)", rule);
            var tell = Ch3Part(program, "static void TellClosedWithoutData()", "static extern bool AttachConsole");
            string[] loud = ["Log.", "Store.", "Dialog", "MessageBox", "new UI.", "Mutex"];
            bool quiet = !loud.Any(before.Contains) && !loud.Any(tell.Contains) && tell.Contains("Environment.ExitCode = 2;") && tell.Contains("Console.Error.WriteLine(WithoutDataNotice(Environment.ProcessPath));");
            // La stessa regola di Store.Dir: se la variabile vale per l'una vale per l'altra.
            bool same = store.Contains("Environment.GetEnvironmentVariable(\"MISSIVA_DATA\") is { Length: > 0 } sandbox ? sandbox :")
                        && Ch3Part(program, "internal static bool ClosesWithoutData(", ";") == "internal static bool ClosesWithoutData(string? data) => data is not { Length: > 0 }";
            Check(first && quiet && same,
                  $"scelta 26 (T-28), in Program.Main la regola viene prima di tutto ciò che apre i dati (registro, mutex, impostazioni, finestra) e dopo i soli strumenti che escono da sé ({first}); " +
                  $"chi si chiude lascia una riga sull'uscita degli errori e il codice 2, senza registro, senza finestre e senza toccare la cartella dei dati ({quiet}); la variabile conta come per Store.Dir ({same})");
        }
    }

    // ---------------------------------------------------------------- scelta 30 (C-37): avvisi di nullità

    /// <summary>
    /// Più proprietà del progetto come le calcola MSBuild con quelle proprietà in più sulla riga di comando, senza
    /// compilare ("dotnet msbuild -getProperty", che per più nomi risponde in JSON); null se dotnet non c'è o non risponde.
    /// </summary>
    static Dictionary<string, string>? Ch3Properties(string project, string[] names, string[] properties)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(DotnetExe()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in (string[])["msbuild", project, "-nologo", .. names.Select(n => "-getProperty:" + n), .. properties]) start.ArgumentList.Add(a);
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(true);
                return null;
            }
            if (process.ExitCode != 0 || JsonNode.Parse(output.GetAwaiter().GetResult())?["Properties"] is not JsonObject found) return null;
            return names.ToDictionary(n => n, n => found[n]?.GetValue<string>() ?? "");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Scelta 30 (C-37): gli avvisi di nullità come errori, solo in Debug. Dal 12 ottobre gli avvisi sono zero; ora in
    /// Debug uno nuovo ferma la compilazione, e in Release resta un avviso: installa.ps1 e la versione finale di prova non
    /// si fermano mai per questo, nemmeno quella con gli strumenti di prova, che ha DEBUG fra le costanti ma è una
    /// Release. Qui si chiede a MSBuild che cosa darebbe al compilatore nei quattro modi di compilare, senza compilare.
    /// La compilazione vera con un avviso messo apposta è stata fatta a mano (docs\audit\ESITO.md).
    /// </summary>
    static void Ch3NullableWarnings()
    {
        if (SourceRoot() is not { } root)
        {
            Report.Add("      (scelta 30: sorgenti non trovati accanto all'eseguibile, gli avvisi che fermano la compilazione non sono stati chiesti a MSBuild)");
            return;
        }
        var project = Path.Combine(root, "Missiva.csproj");
        string[] names = ["WarningsAsErrors", "TreatWarningsAsErrors", "Nullable", "DefineConstants"];
        (string Name, string[] Properties)[] builds =
        [
            ("Debug", []),
            ("versione che si installa", ["-p:Configuration=Release"]),
            ("versione finale di prova", ["-p:Configuration=Release", "-p:MissivaProva=true"]),
            ("versione finale con gli strumenti di prova", ["-p:Configuration=Release", "-p:MissivaProva=true", "-p:DefineConstants=DEBUG%3BTRACE"]),
        ];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var answers = builds.AsParallel().AsOrdered().WithDegreeOfParallelism(builds.Length).Select(b => Ch3Properties(project, names, b.Properties)).ToArray();
        if (answers.Any(a => a == null))
        {
            Report.Add("      (scelta 30: dotnet msbuild non ha risposto, gli avvisi che fermano la compilazione non sono stati letti)");
            return;
        }
        static bool Among(string list, string name) => list.Split(';', ',').Any(x => x.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        bool Stops(int i) => Among(answers[i]!["WarningsAsErrors"], "nullable") || answers[i]!["TreatWarningsAsErrors"].Equals("true", StringComparison.OrdinalIgnoreCase);
        // Il progetto senza i commenti: la condizione sta sulla configurazione, non sulle costanti.
        var xml = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(project), "<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        bool written = Ch3InOrder(xml, "<PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">", "<WarningsAsErrors>$(WarningsAsErrors);nullable</WarningsAsErrors>", "</PropertyGroup>")
                       && xml.Split("WarningsAsErrors>").Length == 3 && !xml.Contains("TreatWarningsAsErrors");
        Check(written && Stops(0) && !Stops(1) && !Stops(2) && !Stops(3) && answers.All(a => a!["Nullable"] == "enable")
              && Among(answers[3]!["DefineConstants"], "DEBUG") && Among(answers[3]!["DefineConstants"], "PROVA") && !Among(answers[1]!["DefineConstants"], "DEBUG"),
              $"scelta 30 (C-37), un avviso di nullità ferma la compilazione solo in Debug: gli avvisi che MSBuild dà al compilatore come errori sono \"{answers[0]!["WarningsAsErrors"]}\" in Debug, " +
              string.Join(", ", builds.Skip(1).Select((b, i) => $"\"{answers[i + 1]!["WarningsAsErrors"]}\" nella {b.Name}")) +
              $"; la versione finale con gli strumenti di prova ha DEBUG fra le costanti (\"{answers[3]!["DefineConstants"]}\") ma è una Release, e lì resta un avviso ({clock.ElapsedMilliseconds} ms)");
    }

    // ---------------------------------------------------------------- scelta 48 (C-47): segna tutti come letti

    /// <summary>
    /// Scelta 48 (C-47), seconda parte: "Contrassegna tutti come letti" di una cartella (MarkAllReadAsync), che nessun
    /// controllo chiamava. Sul server: tutti i non letti della cartella diventano letti con il minimo dei comandi, e le
    /// altre cartelle non cambiano. Nel conto che Missiva mostra: quello che fa MainWindow dopo (le cartelle rilette), e
    /// per la Posta in arrivo il riconteggio di App.RecountSoon, perché subito dopo una scrittura i non letti del server
    /// non si prendono (UnreadSync). Account IMAP e Gmail sul server IMAP finto, Microsoft su un Graph finto con 120 non
    /// letti (tre pagine da 50).
    /// </summary>
    static async Task Ch3MarkAllReadAsync()
    {
        var settle = UnreadSync.Settle;
        UnreadSync.Settle = TimeSpan.FromMilliseconds(150);
        try
        {
            ImapProvider.TestPlain = true;
            try
            {
                using var fake = new FakeImap(new Flavor(Gmail: false));
                using var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
                imap.Account.Id = "scelta48-letti-imap";
                await Ch3StepAsync("account IMAP, segna tutti come letti", () => Ch3MarkOnImapAsync("account IMAP", fake, imap));
            }
            finally
            {
                ImapProvider.TestPlain = false;
            }
            try
            {
                using var fake = new FakeImap();
                GmailProvider.TestPort = fake.Port;
                using var gmail = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "scelta48-letti-gmail" });
                await Ch3StepAsync("account Gmail, segna tutti come letti", () => Ch3MarkOnImapAsync("account Gmail", fake, gmail));
            }
            finally
            {
                GmailProvider.TestPort = null;
            }
            await Ch3StepAsync("account Microsoft, segna tutti come letti", Ch3MarkOnGraphAsync);
        }
        finally
        {
            UnreadSync.Settle = settle;
        }
        if (Ch1Code("UI", "MainWindow.xaml.cs") is { } main)
        {
            var action = Ch3Part(main, "async Task MarkAllReadAsync(FolderItem f)", "async Task FolderActionAsync(");
            var reload = Ch3Part(main, "async Task FolderActionAsync(", "internal static void MigrateFolderKeys(");
            var folders = Ch3Part(main, "async Task LoadFoldersAsync(AccountItem account)", "internal static bool ReloadsFolders(");
            Check(Ch3InOrder(action, "FolderActionAsync(f.Account,", "f.Account.Provider.MarkAllReadAsync(f.Folder)", "if (folder == f) foreach (var row in rows.Rows) row.IsRead = true;")
                  && Ch3InOrder(reload, "await action();", "await LoadFoldersAsync(account);")
                  && Ch3InOrder(folders, "UnreadSync.Ticket(account.Provider.Account.Id)", "account.SetServerFolders(await account.Provider.GetFoldersAsync(), ticket)", "App.Current.RecountSoon(account.Provider)"),
                  "scelta 48 (C-47), nella finestra \"Contrassegna tutti come letti\" fa quello che qui si prova pezzo per pezzo: il comando al provider, le cartelle rilette con il biglietto dei non letti " +
                  "(e il riconteggio se il biglietto non vale), poi le righe dell'elenco aperto segnate lette");
        }
    }

    /// <summary>Il riconteggio dei non letti della Posta in arrivo come lo fa App.RecountSoon: aspetta che il biglietto valga, chiede il numero al server e lo scrive se vale ancora.</summary>
    static async Task<(int Counted, bool Applied, int Waited)> Ch3RecountAsync(UI.AccountItem item)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long? ticket = null;
        for (int waited = 0; waited < 250 && (ticket = UnreadSync.Ticket(item.Provider.Account.Id)) == null; waited++) await Task.Delay(20);
        int unread = await item.Provider.GetInboxUnreadAsync();
        var inbox = item.Folders.First(f => f.Folder.Kind == FolderKind.Inbox);
        return (unread, ticket != null && inbox.ApplyServerUnread(unread, ticket, "ricontati dal server"), (int)clock.ElapsedMilliseconds);
    }

    static async Task Ch3MarkOnImapAsync(string who, FakeImap fake, IMailProvider provider)
    {
        string user = fake.UserFolder;
        foreach (var (subject, flags) in new[] { ("Primo da leggere", ""), ("Secondo da leggere", ""), ("Già letto", "\\Seen"), ("Terzo da leggere", "\\Flagged"), ("Letto e contrassegnato", "\\Seen \\Flagged") })
            fake.Add("INBOX", Ch3Mail(subject), flags);
        foreach (var (subject, flags) in new[] { ("Preventivo", ""), ("Sollecito", ""), ("Contratto firmato", "\\Seen") })
            fake.Add(user, Ch3Mail(subject), flags);
        var item = new UI.AccountItem(provider);
        UI.FolderItem Item(string folder) => item.Folders.First(f => f.Folder.Id == folder);
        int Unseen(string folder) { lock (fake.Folders) return fake.Folders[folder].Count(m => !m.Has("\\Seen")); }
        string Others(string folder) { lock (fake.Folders) return string.Join(" | ", fake.Folders.Where(f => f.Key != folder).OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + ":" + string.Join(",", f.Value.Select(m => $"{m.Uid}[{m.Flags}]")))); }
        List<string> Since(int from) { lock (fake.Traffic) return fake.Traffic.Skip(from).Select(t => t.Text).ToList(); }
        int Now() { lock (fake.Traffic) return fake.Traffic.Count; }
        try
        {
            await Ch3ReloadAsync(item);
            bool start = Item("INBOX").Unread == 3 && Item(user).Unread == 2 && item.InboxUnread == 3;

            // Una cartella qualsiasi: il numero nuovo arriva con le cartelle rilette.
            string othersBefore = Others(user);
            int mark = Now();
            await provider.MarkAllReadAsync(Item(user).Folder);
            var sent = Since(mark);
            var stores = sent.Where(c => c.StartsWith("UID STORE")).ToList();
            bool taken = await Ch3ReloadAsync(item);
            bool folderDone = Unseen(user) == 0 && Others(user) == othersBefore && stores.Count == 1 && stores[0].Contains("\\Seen") && stores[0].Contains("+FLAGS")
                              && sent.Count(c => c.StartsWith("UID SEARCH") && c.Contains("UNSEEN")) == 1 && !sent.Any(c => c.StartsWith("APPEND") || c.Contains("EXPUNGE") || c.Contains("MOVE") || c.Contains("COPY"))
                              && Item(user) is { Unread: 0, UnreadText: "" } && item.InboxUnread == 3;
            Check(start && folderDone,
                  $"scelta 48 (C-47), {who}, \"Contrassegna tutti come letti\" su una cartella con 2 da leggere su 3: sul server restano {Unseen(user)} da leggere, con una ricerca e un solo comando " +
                  $"({string.Join("; ", stores)}), e le altre cartelle non cambiano; rilette le cartelle, Missiva mostra {Item(user).Unread} per quella cartella e {item.InboxUnread} per la Posta in arrivo, che non è stata toccata");

            // La Posta in arrivo: subito dopo la scrittura il numero del server non si prende, e lo rimette a posto il riconteggio.
            othersBefore = Others("INBOX");
            mark = Now();
            await provider.MarkAllReadAsync(Item("INBOX").Folder);
            sent = Since(mark);
            stores = sent.Where(c => c.StartsWith("UID STORE")).ToList();
            bool inboxTaken = await Ch3ReloadAsync(item);
            int shownAtOnce = item.InboxUnread;
            var recount = await Ch3RecountAsync(item);
            bool flagsKept;
            lock (fake.Folders) flagsKept = fake.Folders["INBOX"].Count(m => m.Has("\\Flagged")) == 2 && fake.Folders["INBOX"].Count == 5;
            Check(Unseen("INBOX") == 0 && Others("INBOX") == othersBefore && flagsKept && stores.Count == 1 && recount is { Counted: 0, Applied: true } && item.InboxUnread == 0
                  && Item("INBOX") is { Unread: 0, UnreadText: "" } && item.HeaderUnreadText == "",
                  $"scelta 48 (C-47), {who}, lo stesso sulla Posta in arrivo con 3 da leggere su 5: sul server 0 da leggere con un solo comando, i contrassegni restano; rilette le cartelle subito dopo, " +
                  $"il numero del server {(inboxTaken ? "viene preso" : "non viene preso (una scrittura di Missiva è appena finita)")} e Missiva mostra ancora {shownAtOnce}; il riconteggio, " +
                  $"{recount.Waited} ms dopo, legge {recount.Counted} dal server e Missiva mostra {item.InboxUnread}");

            // Senza nulla da leggere: nessun comando che scrive.
            mark = Now();
            await provider.MarkAllReadAsync(Item("INBOX").Folder);
            sent = Since(mark);
            Check(!sent.Any(c => c.StartsWith("UID STORE")) && sent.Count(c => c.StartsWith("UID SEARCH")) == 1,
                  $"scelta 48 (C-47), {who}, \"Contrassegna tutti come letti\" su una cartella senza messaggi da leggere: una ricerca e nessun comando che scrive ({string.Join("; ", sent)})");
        }
        finally
        {
            MailCache.Delete(provider.Account);
        }
    }

    /// <summary>
    /// Graph non ha un comando per segnare letta una cartella: MarkAllReadAsync chiede i non letti a pagine di 50 e manda
    /// un PATCH per messaggio, quattro alla volta. Un Graph finto con 120 non letti su 130 nella Posta in arrivo e 3 su 4
    /// in un'altra cartella.
    /// </summary>
    static async Task Ch3MarkOnGraphAsync()
    {
        var gate = new object();
        var unread = new Dictionary<string, HashSet<string>> { ["posta"] = Enumerable.Range(1, 120).Select(i => $"posta-{i}").ToHashSet(), ["clienti"] = ["clienti-1", "clienti-2", "clienti-3"] };
        var total = new Dictionary<string, int> { ["posta"] = 130, ["clienti"] = 4 };
        var patched = new List<string>();
        var pages = new List<string>();
        var strange = new List<string>();
        static HttpResponseMessage Json(JsonNode json) => new(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            var parts = path.Split('?')[0].Split('/');
            lock (gate)
            {
                JsonObject FolderOf(string id) => new() { ["id"] = id, ["displayName"] = id == "posta" ? "Posta in arrivo" : "Clienti", ["unreadItemCount"] = unread[id].Count, ["totalItemCount"] = total[id], ["childFolderCount"] = 0 };
                if (request.Method == HttpMethod.Get && path.StartsWith("me/mailFolders?")) return Json(new JsonObject { ["value"] = new JsonArray(FolderOf("posta"), FolderOf("clienti")) });
                if (request.Method == HttpMethod.Get && parts is ["me", "mailFolders", "inbox"]) return Json(FolderOf("posta"));
                if (request.Method == HttpMethod.Get && parts is ["me", "mailFolders", _]) return Json(new JsonObject { ["id"] = "altra-" + parts[2] });
                if (request.Method == HttpMethod.Get && parts is ["me", "mailFolders", var folder, "messages"] && unread.TryGetValue(folder, out var waiting) && path.Contains("$filter=isRead eq false"))
                {
                    pages.Add($"{folder}: {Math.Min(50, waiting.Count)}");
                    return Json(new JsonObject { ["value"] = new JsonArray(waiting.OrderBy(m => m, StringComparer.Ordinal).Take(50).Select(m => (JsonNode)new JsonObject { ["id"] = m }).ToArray()) });
                }
                if (request.Method == HttpMethod.Patch && parts is ["me", "messages", var message])
                {
                    var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (body.Replace(" ", "") != "{\"isRead\":true}") strange.Add($"PATCH {message} {body}");
                    patched.Add(message);
                    foreach (var set in unread.Values) set.Remove(message);
                    return Json(new JsonObject { ["id"] = message, ["isRead"] = true });
                }
                strange.Add($"{request.Method} {path}");
                return Json(new JsonObject { ["value"] = new JsonArray() });
            }
        }));
        var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio-prova.example", DisplayName = "Mario Rossi", Id = "scelta48-letti-graph" });
        var item = new UI.AccountItem(graph);
        UI.FolderItem Item(string folder) => item.Folders.First(f => f.Folder.Id == folder);
        int Left(string folder) { lock (gate) return unread[folder].Count; }
        try
        {
            await Ch3ReloadAsync(item);
            bool start = Item("posta") is { Unread: 120, Folder.Kind: FolderKind.Inbox } && Item("clienti").Unread == 3 && item.InboxUnread == 120;

            await graph.MarkAllReadAsync(Item("clienti").Folder);
            bool taken = await Ch3ReloadAsync(item);
            List<string> firstPatched, firstPages;
            lock (gate) (firstPatched, firstPages) = (patched.ToList(), pages.ToList());
            bool folderDone = Left("clienti") == 0 && Left("posta") == 120 && firstPatched.Order().SequenceEqual(["clienti-1", "clienti-2", "clienti-3"]) && firstPages.SequenceEqual(["clienti: 3", "clienti: 0"])
                              && Item("clienti") is { Unread: 0, UnreadText: "" } && item.InboxUnread == 120;

            await graph.MarkAllReadAsync(Item("posta").Folder);
            bool inboxTaken = await Ch3ReloadAsync(item);
            int shownAtOnce = item.InboxUnread;
            var recount = await Ch3RecountAsync(item);
            List<string> allPatched, allPages, odd;
            lock (gate) (allPatched, allPages, odd) = (patched.Skip(3).ToList(), pages.Skip(2).ToList(), strange.ToList());
            Check(start && folderDone && Left("posta") == 0 && allPatched.Count == 120 && allPatched.Distinct().Count() == 120 && allPages.SequenceEqual(["posta: 50", "posta: 50", "posta: 20", "posta: 0"])
                  && odd.Count == 0 && recount is { Counted: 0, Applied: true } && item.InboxUnread == 0 && Item("posta") is { Unread: 0, UnreadText: "" },
                  $"scelta 48 (C-47), account Microsoft, \"Contrassegna tutti come letti\" sul Graph finto: una cartella con 3 da leggere su 4 (3 PATCH isRead, {Left("clienti")} restano, Missiva mostra " +
                  $"{Item("clienti").Unread} appena rilette le cartelle e la Posta in arrivo resta a 120); poi la Posta in arrivo con 120 da leggere su 130: {allPatched.Count} PATCH, uno per messaggio e nessuno " +
                  $"due volte, chiesti in {allPages.Count} pagine ({string.Join(", ", allPages.Select(p => p.Split(' ')[1]))}), sul server {Left("posta")} da leggere; subito dopo Missiva mostra ancora {shownAtOnce} " +
                  $"({(inboxTaken ? "numero del server preso" : "numero del server non preso")}), il riconteggio {recount.Waited} ms dopo legge {recount.Counted} e Missiva mostra {item.InboxUnread}" +
                  (odd.Count == 0 ? "" : $". RICHIESTE NON ATTESE: {string.Join("; ", odd.Take(5))}"));
        }
        finally
        {
            GraphProvider.UseTestHandler(new NoHandler("Graph"));
            MailCache.Delete(graph.Account);
        }
    }

    // ---------------------------------------------------------------- scelta 48 (C-47): cartelle

    /// <summary>
    /// Quello che la finestra principale fa dopo ogni azione sulle cartelle (MainWindow.LoadFoldersAsync): le rilegge dal
    /// server con i loro non letti e ne salva la copia. Dice se i non letti del server sono stati presi (falso: intanto
    /// Missiva ne ha scritti di suoi, e la finestra li fa ricontare).
    /// </summary>
    static async Task<bool> Ch3ReloadAsync(UI.AccountItem item)
    {
        var ticket = UnreadSync.Ticket(item.Provider.Account.Id);
        bool taken = item.SetServerFolders(await item.Provider.GetFoldersAsync(), ticket);
        MailCache.SaveFolders(item.Provider.Account, item.Snapshot());
        return taken;
    }

    /// <summary>
    /// Una prova lunga dentro un try: se si ferma a metà (una cartella che non c'è più, un errore del provider) cade un
    /// controllo che dice dove e perché, e le prove dopo girano lo stesso.
    /// </summary>
    static async Task Ch3StepAsync(string what, Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (Exception ex)
        {
            Check(false, $"scelta 48 (C-47), {what}: la prova si è fermata prima della fine ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>Le scritture della copia dei corpi su disco partite fin qui, aspettate.</summary>
    static void Ch3Written()
    {
        Task[] writes;
        lock (BodyCache.TestWrites) writes = [.. BodyCache.TestWrites];
        Task.WaitAll(writes);
    }

    /// <summary>
    /// Scelta 48 (C-47), prima parte: crea, rinomina, sposta ed elimina una cartella, sul server IMAP finto che ora ha
    /// RENAME e DELETE veri. Nessun controllo provava il caso che riesce (C-47 aveva trovato zero riferimenti a
    /// MoveFolderAsync, e di RenameFolderAsync e DeleteFolderAsync solo il rifiuto). Le finestre non si aprono: qui il
    /// provider vero sul server finto e poi, una per una, le cose che MainWindow fa dopo (MigrateFolderKeys, le cartelle
    /// rilette in un AccountItem, la copia locale). Tre server: IMAP con la barra, IMAP con il punto e le cartelle dentro
    /// INBOX, Gmail.
    /// </summary>
    static async Task Ch3FoldersAsync()
    {
        ImapProvider.TestPlain = true;
        try
        {
            using (var fake = new FakeImap(new Flavor(Gmail: false)))
            using (var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example")))
            {
                imap.Account.Id = "scelta48-imap-barra";
                await Ch3StepAsync("account IMAP con la barra", () => Ch3FolderLifeAsync("account IMAP con la barra", fake, imap, prefix: "", sep: '/', taken: "Archive"));
            }
            using (var fake = new FakeImap(new Flavor(Gmail: false, SpecialUse: false, Move: false, UidPlus: false)))
            using (var imap = new ImapProvider(SpeedAccount(fake, "maria@studio-prova.example")))
            {
                imap.Account.Id = "scelta48-imap-punto";
                await Ch3StepAsync("account IMAP con il punto", () => Ch3FolderLifeAsync("account IMAP con il punto e le cartelle dentro INBOX", fake, imap, prefix: "INBOX.", sep: '.', taken: "Bozze"));
            }
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
        try
        {
            using var fake = new FakeImap();
            GmailProvider.TestPort = fake.Port;
            using var gmail = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "scelta48-gmail" });
            await Ch3StepAsync("account Gmail", () => Ch3FolderLifeAsync("account Gmail", fake, gmail, prefix: "", sep: '/', taken: "Pratiche 2025"));
        }
        finally
        {
            GmailProvider.TestPort = null;
        }

        // Una sottocartella rinominata come prima cosa su una connessione nuova, con un server che a LIST risponde solo per
        // il nome chiesto (ExactList, come un server vero): MailKit non conosce ancora la cartella che la contiene. Deve
        // restare lì dentro; rinominata nel primo livello sarebbe uno spostamento (C-37, mai provato: il server finto non
        // aveva RENAME).
        static async Task<(string Id, bool Stayed, int Asked)> NestedAsync(FakeImap fake, IMailProvider provider, string nested, string expected)
        {
            fake.ExactList = true;
            lock (fake.Folders) fake.Folders[nested] = new();
            fake.Add(nested, Ch3Mail("Fattura di Rossi"));
            string renamed = await provider.RenameFolderAsync(new MailFolder { Id = nested, Name = "Rossi", Kind = FolderKind.Other, ParentId = fake.UserFolder }, "Bianchi");
            lock (fake.Folders)
                lock (fake.Traffic)
                    return (renamed, fake.Folders.ContainsKey(expected) && !fake.Folders.ContainsKey(nested) && !fake.Folders.ContainsKey("Bianchi") && fake.Folders[expected].Count == 1,
                            fake.Traffic.Count(t => t.Text == $"LIST \"\" {fake.UserFolder}" || t.Text == $"LIST \"\" \"{fake.UserFolder}\""));
        }
        (string Id, bool Stayed, int Asked) nestedImap, nestedDot, nestedGmail;
        ImapProvider.TestPlain = true;
        try
        {
            using (var fake = new FakeImap(new Flavor(Gmail: false)))
            using (var imap = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example")))
                nestedImap = await NestedAsync(fake, imap, "Clienti/Rossi", "Clienti/Bianchi");
            using (var fake = new FakeImap(new Flavor(Gmail: false, SpecialUse: false)))
            using (var imap = new ImapProvider(SpeedAccount(fake, "maria@studio-prova.example")))
                nestedDot = await NestedAsync(fake, imap, "INBOX.Clienti.Rossi", "INBOX.Clienti.Bianchi");
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
        try
        {
            using var fake = new FakeImap();
            GmailProvider.TestPort = fake.Port;
            using var gmail = new GmailProvider(new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "scelta48-gmail-annidata" });
            nestedGmail = await NestedAsync(fake, gmail, "Lavoro/Rossi", "Lavoro/Bianchi");
        }
        finally
        {
            GmailProvider.TestPort = null;
        }
        if (Ch1Code("UI", "MainWindow.xaml.cs") is { } main)
        {
            var delete = Ch3Part(main, "async Task DeleteFolderAsync(FolderItem f)", "internal static void ForgetFolderKeys(");
            var forget = Ch3Part(main, "internal static void ForgetFolderKeys(", "void OnFolderSelected(");
            Check(Ch3InOrder(delete, "await f.Account.Provider.DeleteFolderAsync(f.Folder);", "catch (Exception ex)", "return;", "ForgetFolderKeys(f.Account, f.Folder.Id);", "await LoadFoldersAsync(f.Account);")
                  && delete.Split("ForgetFolderKeys(").Length == 2
                  && new[] { "S.Favorites.RemoveAll(", "S.HiddenFolders.RemoveAll(", "S.CollapsedFolders.RemoveAll(", "S.FolderColors.Remove(", "order.RemoveAll(", "BodyCache.ForgetFolder(", "MailCache.ForgetFolder(" }.All(forget.Contains),
                  "scelta 48 (C-47), nella finestra Elimina cartella toglie quello che Missiva teneva per quella cartella (MainWindow.ForgetFolderKeys: Preferito, nascosta, chiusa, colore, ordine, copie sul PC) " +
                  "solo dopo che il server l'ha eliminata, e prima di rileggere le cartelle; se il server rifiuta, esce prima e non toglie nulla");
        }
        Check(nestedImap is { Id: "Clienti/Bianchi", Stayed: true, Asked: 1 } && nestedDot is { Id: "INBOX.Clienti.Bianchi", Stayed: true, Asked: 1 } && nestedGmail is { Id: "Lavoro/Bianchi", Stayed: true, Asked: 1 },
              $"scelta 48 (C-47 e C-37), una sottocartella rinominata come prima cosa su una connessione nuova, con un server che a LIST risponde solo per il nome chiesto, resta dentro la sua " +
              $"cartella con il suo messaggio: IMAP {nestedImap.Id} ({nestedImap.Stayed}), IMAP con il punto {nestedDot.Id} ({nestedDot.Stayed}), Gmail {nestedGmail.Id} ({nestedGmail.Stayed}); " +
              $"la cartella che la contiene MailKit la chiede da sé al server prima di rinominare ({nestedImap.Asked}, {nestedDot.Asked} e {nestedGmail.Asked} LIST con il suo nome)");
    }

    /// <summary>
    /// La vita di una cartella dell'utente su un server finto: nuova sottocartella, quello che Missiva ricorda per
    /// identificativo (Preferiti, colori, chiusa, ordine), rinomina con la cartella aperta, spostamento dentro un'altra e
    /// poi in cima, eliminazione, e i rifiuti del server. A ogni passo: il server, e l'elenco che Missiva mostrerebbe.
    /// </summary>
    /// <param name="prefix">Dove stanno le cartelle dell'utente: in cima (vuoto) o dentro INBOX ("INBOX.").</param>
    /// <param name="taken">Un nome che accanto alla cartella "Amministrazione" c'è già, per la rinomina rifiutata.</param>
    static async Task Ch3FolderLifeAsync(string who, FakeImap fake, IMailProvider provider, string prefix, char sep, string taken)
    {
        var s = Store.Settings;
        var account = provider.Account;
        bool gmail = account.Kind == AccountKind.Gmail;
        string id = account.Id, user = fake.UserFolder, sibling = prefix + "Amministrazione";
        string Key(string folder) => id + "|" + folder;
        lock (fake.Folders) fake.Folders[sibling] = new();
        fake.Add(user, Ch3Mail("Preventivo di ottobre", "Il preventivo di ottobre", daysAgo: 1));
        fake.Add(user, Ch3Mail("Sollecito", daysAgo: 2));
        fake.Add(user, Ch3Mail("Contratto firmato", daysAgo: 3), "\\Seen");
        fake.Add("INBOX", Ch3Mail("Benvenuto"));
        fake.Add("INBOX", Ch3Mail("Vecchio avviso", daysAgo: 1), "\\Seen");
        if (gmail) fake.Add("INBOX", "Con l'etichetta della cartella", user).Set("\\Seen", true);
        var item = new UI.AccountItem(provider);
        UI.FolderItem? Item(string folder) => item.Folders.FirstOrDefault(f => f.Folder.Id == folder);
        MailFolder Folder(string folder) => Item(folder)?.Folder ?? throw new InvalidOperationException($"la cartella {folder} non è nell'elenco di Missiva");
        // I Preferiti che si vedrebbero: le voci delle impostazioni che hanno ancora una cartella (MainWindow.UpdateFavorites).
        List<string> Shown() => s.Favorites.Where(k => k.StartsWith(id + "|", StringComparison.Ordinal) && item.Folders.Any(f => f.Key == k)).ToList();
        int Index(string folder) => item.Folders.ToList().FindIndex(f => f.Folder.Id == folder);
        int Color(string folder) => s.FolderColors.GetValueOrDefault(Key(folder), -1);
        bool OnServer(string folder) { lock (fake.Folders) return fake.Folders.ContainsKey(folder); }
        string Messages(string folder) { lock (fake.Folders) return fake.Folders.TryGetValue(folder, out var list) ? string.Join(",", list.Select(m => $"{m.Uid}[{m.Flags}]")) : "la cartella non c'è"; }
        static async Task<string> Try(Func<Task> action)
        {
            try { await action(); return "riuscito"; }
            catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message}"; }
        }
        try
        {
            await Ch3ReloadAsync(item);

            // Nuova sottocartella (MainWindow.CreateSubfolderAsync): dentro una cartella dell'utente, e dentro la Posta in arrivo
            // (in Gmail le cartelle di sistema non ne hanno dentro: nasce in cima).
            string rossi = user + sep + "Rossi", second = gmail ? "Pratiche 2025" : "INBOX" + sep + "Pratiche 2025";
            var made = await provider.CreateFolderAsync(" Rossi ", Folder(user));
            var made2 = await provider.CreateFolderAsync("Pratiche 2025", Folder("INBOX"));
            bool born = OnServer(rossi) && OnServer(second);
            if (OnServer(rossi)) fake.Add(rossi, Ch3Mail("Fattura di Rossi", "La fattura di settembre"));
            await Ch3ReloadAsync(item);
            Check(born && made is { Name: "Rossi", Kind: FolderKind.Other } && made.Id == rossi && made.ParentId == user && made2.Id == second && made2.ParentId == (gmail ? null : "INBOX")
                  && Item(rossi) is { Unread: 1, Folder.Total: 1 } r1 && r1.Parent == Item(user) && Item(user)!.HasChildren && Item(second) is { Unread: 0 } p1 && p1.Parent == (gmail ? null : Item("INBOX")),
                  $"scelta 48 (C-47), {who}, nuova sottocartella: \" Rossi \" dentro {user} nasce sul server come {made.Id} (senza gli spazi), \"Pratiche 2025\" dentro la Posta in arrivo come {made2.Id}" +
                  $"{(gmail ? " (in cima: in Gmail le cartelle di sistema non ne hanno dentro)" : "")}; rilette le cartelle, stanno nell'elenco sotto la loro, e un messaggio arrivato nella nuova si conta " +
                  $"({Item(rossi)?.Unread} non letto)");

            // Quello che Missiva ricorda per identificativo: Preferiti, colori, cartella chiusa, ordine scelto trascinando.
            s.Favorites.AddRange([Key(user), Key(rossi)]);
            (s.FolderColors[Key(user)], s.FolderColors[Key(rossi)]) = (3, 5);
            s.CollapsedFolders.Add(Key(user));
            s.FolderOrder[id] = [user, sibling];
            if (gmail) s.CategoryColors[Key(user)] = 7;
            await Ch3ReloadAsync(item);
            bool orderedBefore = Index(user) >= 0 && Index(user) < Index(sibling);

            // La cartella aperta, con la copia della prima pagina e del corpo di un messaggio sul PC.
            var page = (await provider.GetMessagesAsync(Folder(user), null, null)).Messages;
            var first = page.First(m => m.Subject == "Preventivo di ottobre");
            var body = await provider.GetBodyAsync(first);
            MailCache.SaveMessages(account, user, null, page);
            BodyCache.Save(account, first, body);
            Ch3Written();
            string before = Messages(user), inboxBefore = Messages("INBOX");

            // Rinomina (MainWindow.RenameFolderAsync), con la cartella aperta e gli spazi attorno al nome.
            string renamed = prefix + "Fornitori", renamedRossi = renamed + sep + "Rossi";
            string renamedId = await provider.RenameFolderAsync(Folder(user), " Fornitori ");
            UI.MainWindow.MigrateFolderKeys(item, user, renamedId);
            await Ch3ReloadAsync(item);
            var again = Item(renamedId) is { } reopenedFolder ? (await provider.GetMessagesAsync(reopenedFolder.Folder, null, null)).Messages : [];
            var reopened = again.FirstOrDefault(m => m.Id == first.Id);
            string html = reopened != null ? (await provider.GetBodyAsync(reopened)).Html : "";
            bool onServer = renamedId == renamed && OnServer(renamed) && OnServer(renamedRossi) && !OnServer(user) && !OnServer(rossi) && Messages(renamed) == before && Messages("INBOX") == inboxBefore;
            bool inList = Item(renamed) is { Unread: 2, Folder.Total: 3, expanded: false } n1 && Item(renamedRossi) is { Unread: 1 } n2 && n2.Parent == n1 && Item(user) == null && Item(rossi) == null
                          && again.Select(m => m.Id).Order().SequenceEqual(page.Select(m => m.Id).Order()) && again.Count(m => !m.IsRead) == 2 && html.Length > 0 && html == body.Html;
            bool remembered = Shown().SequenceEqual([Key(renamed), Key(renamedRossi)]) && Color(renamed) == 3 && Color(renamedRossi) == 5 && Color(user) == -1 && Color(rossi) == -1
                              && orderedBefore && Index(renamed) < Index(sibling) && !s.CollapsedFolders.Contains(Key(user));
            var labels = gmail ? (await provider.GetMessagesAsync(Folder("INBOX"), null, null)).Messages.FirstOrDefault(m => m.Subject == "Con l'etichetta della cartella")?.Categories ?? [] : [];
            bool category = !gmail || labels.SequenceEqual([renamed]) && CategoryPalette.PresetOf(id, renamed) == 7 && (await provider.GetCategoriesAsync()).Any(c => c.Name == renamed && c.Preset == 7);
            Check(onServer && inList && remembered && category,
                  $"scelta 48 (C-47), {who}, rinomina di {user} in \" Fornitori \" con la cartella aperta: sul server diventa {renamedId} con gli stessi messaggi (stessi UID e segni) e la sua " +
                  $"sottocartella la segue ({onServer}); in Missiva la cartella riletta ha gli stessi 3 messaggi, 2 da leggere, e il corpo si riapre ({inList}); Preferiti ({string.Join(", ", Shown())}), " +
                  $"colori ({Color(renamed)} e {Color(renamedRossi)}), cartella chiusa e ordine scelto la seguono, lei e la sottocartella ({remembered})" +
                  (gmail ? $"; il messaggio con quell'etichetta ha la categoria con il nome nuovo e il colore di prima ({category}: {string.Join(", ", labels)})" : ""));

            // Misura: la copia locale non segue. Le copie restano con l'identificativo di prima fino a 30 giorni.
            var found = BodyCache.Search(account, "preventivo", null, 50);
            Report.Add($"      (scelta 48, {who}, misura: la copia locale non segue la cartella rinominata. La copia della prima pagina sotto il nome nuovo {(MailCache.LoadMessages(account, renamed, null) == null ? "non c'è" : "c'è")}, " +
                       $"quella sotto il nome di prima {(MailCache.LoadMessages(account, user, null) == null ? "non c'è" : "resta")}; la copia del corpo già aperto {(reopened != null && BodyCache.Has(account, reopened) ? "vale" : "non vale")} " +
                       $"per il messaggio nella cartella rinominata, che si riscarica, e {(BodyCache.Has(account, first) ? "resta" : "non resta")} sotto la cartella di prima: la ricerca senza rete trova {found.Count} " +
                       $"messaggi, nella cartella {string.Join(", ", found.Select(m => m.FolderId).Distinct())})");

            // Sposta (MainWindow.MoveFolderAsync): la cartella con la sua sottocartella dentro un'altra, poi la sottocartella in cima.
            string moved = sibling + sep + "Fornitori", movedRossi = moved + sep + "Rossi";
            string movedId = await provider.MoveFolderAsync(Folder(renamed), Folder(sibling));
            UI.MainWindow.MigrateFolderKeys(item, renamed, movedId);
            await Ch3ReloadAsync(item);
            bool inside = movedId == moved && OnServer(moved) && OnServer(movedRossi) && !OnServer(renamed) && !OnServer(renamedRossi) && Messages(moved) == before
                          && Item(moved) is { Unread: 2, Folder.Total: 3 } m1 && m1.Parent == Item(sibling) && Item(sibling)!.HasChildren && Item(movedRossi) is { Unread: 1 } m2 && m2.Parent == m1
                          && Shown().SequenceEqual([Key(moved), Key(movedRossi)]) && Color(moved) == 3 && Color(movedRossi) == 5 && Color(renamed) == -1;
            string topId = await provider.MoveFolderAsync(Folder(movedRossi), null);
            UI.MainWindow.MigrateFolderKeys(item, movedRossi, topId);
            await Ch3ReloadAsync(item);
            bool top = topId == "Rossi" && OnServer("Rossi") && !OnServer(movedRossi) && Item("Rossi") is { Unread: 1, Parent: null } && !Item(moved)!.HasChildren
                       && Shown().SequenceEqual([Key(moved), Key("Rossi")]) && Color("Rossi") == 5 && Color(movedRossi) == -1 && Messages("INBOX") == inboxBefore;
            Check(inside && top,
                  $"scelta 48 (C-47), {who}, sposta: {renamed} dentro {sibling} diventa {movedId} con i suoi messaggi e la sua sottocartella, e Preferiti e colori le seguono tutte e due ({inside}); " +
                  $"poi la sottocartella portata al primo livello diventa {topId}, con il suo non letto, il suo Preferito e il suo colore ({top})");

            // Il server rifiuta: una rinomina verso un nome che c'è già, e un'eliminazione. Nulla cambia, e chi chiede lo sa.
            string clash = await Try(() => provider.RenameFolderAsync(Folder(sibling), taken));
            fake.RefuseOnce = "DELETE";
            string denied = await Try(() => provider.DeleteFolderAsync(Folder("Rossi")));
            fake.RefuseOnce = null;
            await Ch3ReloadAsync(item);
            Check(clash != "riuscito" && denied != "riuscito" && OnServer(sibling) && OnServer(moved) && OnServer("Rossi") && Item(sibling) != null && Item("Rossi") is { Unread: 1 }
                  && Shown().SequenceEqual([Key(moved), Key("Rossi")]),
                  $"scelta 48 (C-47), {who}, il server rifiuta: rinominare {sibling} in \"{taken}\", che c'è già, e un'eliminazione rifiutata danno un errore a chi li chiede, e cartelle, messaggi e " +
                  $"Preferiti restano com'erano (rinomina: {clash.Split(':')[0]}; eliminazione: {denied.Split(':')[0]})");

            // Elimina (MainWindow.DeleteFolderAsync), con la cartella aperta, fra i Preferiti, colorata e con le sue copie sul PC.
            var rossiPage = (await provider.GetMessagesAsync(Folder("Rossi"), null, null)).Messages;
            var invoice = rossiPage.Single();
            MailCache.SaveMessages(account, "Rossi", null, rossiPage);
            BodyCache.Save(account, invoice, await provider.GetBodyAsync(invoice));
            Ch3Written();
            s.FolderOrder[id].Add("Rossi");
            s.CollapsedFolders.Add(Key("Rossi"));
            s.HiddenFolders.Add(Key(second));
            bool copied = BodyCache.Has(account, invoice) && MailCache.LoadMessages(account, "Rossi", null) is { Count: 1 };
            await provider.DeleteFolderAsync(Folder("Rossi"));
            // Quello che MainWindow.DeleteFolderAsync fa dopo un'eliminazione riuscita, prima di rileggere le cartelle.
            UI.MainWindow.ForgetFolderKeys(item, "Rossi");
            await Ch3ReloadAsync(item);
            Check(copied && !OnServer("Rossi") && OnServer(moved) && Messages(moved) == before && Messages("INBOX") == inboxBefore && Item("Rossi") == null && Item(moved) is { Unread: 2 }
                  && Item("INBOX") is { Unread: 1 } && Shown().SequenceEqual([Key(moved)]),
                  $"scelta 48 (C-47), {who}, elimina: la cartella Rossi, aperta e con un messaggio da leggere, sparisce dal server con il suo messaggio e dall'elenco di Missiva; le altre cartelle, " +
                  $"i loro messaggi e i non letti della Posta in arrivo ({Item("INBOX")?.Unread}) restano, e fra i Preferiti si vede solo l'altra");

            // Che cosa resta della cartella eliminata, e che cosa eredita una cartella nuova con lo stesso nome.
            var offline = BodyCache.Search(account, "fattura", null, 50);
            bool nothing = !s.Favorites.Contains(Key("Rossi")) && Color("Rossi") == -1 && !s.FolderOrder[id].Contains("Rossi") && !s.CollapsedFolders.Contains(Key("Rossi"))
                           && !BodyCache.Has(account, invoice) && offline.Count == 0 && MailCache.LoadMessages(account, "Rossi", null) == null;
            // Solo lei: l'altra cartella tiene Preferito, colore, posto nell'ordine e le sue copie sul PC, e una terza resta nascosta.
            bool others = s.Favorites.Contains(Key(moved)) && Color(moved) == 3 && s.FolderOrder[id].Contains(moved) && s.HiddenFolders.Contains(Key(second))
                          && MailCache.LoadMessages(account, user, null) is { Count: 3 } && BodyCache.Has(account, first);
            await provider.CreateFolderAsync("Rossi", null);
            await Ch3ReloadAsync(item);
            bool fresh = Item("Rossi") is { Unread: 0, Folder.Total: 0 } && !Shown().Contains(Key("Rossi")) && Color("Rossi") == -1 && MailCache.LoadMessages(account, "Rossi", null) == null;
            Check(nothing && others && fresh,
                  $"scelta 48 (C-47), {who}, della cartella eliminata non resta nulla ({nothing}): né fra i Preferiti ({!s.Favorites.Contains(Key("Rossi"))}), né il colore ({Color("Rossi") == -1}), né " +
                  $"nell'ordine ({!s.FolderOrder[id].Contains("Rossi")}), né la copia della prima pagina ({MailCache.LoadMessages(account, "Rossi", null) == null}), né la copia del corpo del suo " +
                  $"messaggio ({!BodyCache.Has(account, invoice)}; la ricerca senza rete ne trova {offline.Count}); le altre cartelle tengono tutto ({others}); una cartella nuova con lo stesso nome nasce " +
                  $"vuota, senza Preferito e senza colore ({fresh}). Prima restavano Preferito, colore, posto nell'ordine e copie sul PC, e la cartella nuova li ereditava");
        }
        finally
        {
            bool Mine(string key) => key.StartsWith(id + "|", StringComparison.Ordinal);
            s.Favorites.RemoveAll(Mine);
            s.HiddenFolders.RemoveAll(Mine);
            s.CollapsedFolders.RemoveAll(Mine);
            foreach (var key in s.FolderColors.Keys.Where(Mine).ToList()) s.FolderColors.Remove(key);
            foreach (var key in s.CategoryColors.Keys.Where(Mine).ToList()) s.CategoryColors.Remove(key);
            s.FolderOrder.Remove(id);
            Store.Save();
            Ch3Written();
            MailCache.Delete(account);
        }
    }

    // ---------------------------------------------------------------- scelta 33 (C-65): TestingProvider

    /// <summary>
    /// I metodi di IMailProvider che leggono soltanto: TestingProvider li può passare al provider vero. Ognuno è stato
    /// letto sui tre provider il 13 ottobre 2026 (GraphProvider, GmailProvider con Calendar e Contacts, ImapProvider): GET
    /// di Graph e di Google, LIST, STATUS, SELECT o EXAMINE, FETCH con BODY.PEEK (MailKit non chiede mai BODY[] senza
    /// PEEK: leggere non segna letto), SEARCH, NOOP, IDLE. Due sono POST che non scrivono nulla: GetScheduleAsync (Graph
    /// getSchedule, Google freeBusy). ResetConnectionsAsync chiude solo le connessioni di Missiva. SELECT, dove la
    /// cartella si apre anche per le azioni, toglie \Recent ai suoi messaggi, come fa ogni programma di posta che la
    /// apre: nient'altro cambia sulla casella.
    /// Un metodo nuovo va qui solo dopo aver letto che cosa fa su tutti e tre: un "leggi" che segna come già letto scrive.
    /// </summary>
    static readonly string[] Ch3Reads =
    [
        "GetFoldersAsync", "GetMessagesAsync", "SortsOnServer", "GetPreviewsAsync", "SearchAsync", "GetBodyAsync", "PrefetchAsync", "KeptBody", "SavedBody",
        "GetListHeadersAsync", "GetRecentAsync", "GetRecentKeysAsync", "GetConversationAsync", "GetAttachmentAsync", "StillThereAsync", "GetCategoriesAsync",
        "GetRawAsync", "GetNewAsync", "GetArrivedAsync", "GetInboxUnreadAsync", "WatchInboxAsync", "GetInboxChangesAsync", "GetSentRecipientsAsync",
        "GetCalendarAsync", "GetScheduleAsync", "GetContactsAsync", "SearchPeopleAsync", "GetAutoRepliesAsync", "GetRulesAsync", "ResetConnectionsAsync",
    ];

    /// <summary>
    /// I metodi di IMailProvider che scrivono sulla casella, sul calendario, sui contatti o sulle impostazioni del server,
    /// che inviano, o che aprono una finestra di consenso (Allow...): TestingProvider non li passa mai al provider vero.
    /// EnsureTrashAsync ed EnsureFolderAsync creano una cartella quando manca: nella prova leggono l'elenco e, se manca,
    /// ne restituiscono una finta.
    /// </summary>
    static readonly string[] Ch3Writes =
    [
        "DeleteFolderAsync", "SetReadAsync", "SetFlagAsync", "SetFlagDueAsync", "CompleteFlagAsync", "MoveAsync", "UndoMoveAsync", "CreateCategoryAsync",
        "SetCategoryAsync", "CreateFolderAsync", "RenameFolderAsync", "MoveFolderAsync", "EmptyFolderAsync", "MarkAllReadAsync", "EnsureFolderAsync", "SnoozeAsync",
        "WakeAsync", "CopyAsync", "DeletePermanentlyAsync", "SendAsync", "SaveDraftAsync", "DiscardDraftAsync", "RespondAsync", "AllowCalendarAsync",
        "CreateEventAsync", "RespondToEventAsync", "UpdateEventAsync", "DeleteEventAsync", "CancelEventAsync", "AllowContactsAsync", "AllowContactsWriteAsync",
        "SaveContactAsync", "DeleteContactAsync", "AllowPeopleAsync", "SetAutoRepliesAsync", "AllowMailboxSettingsAsync", "SaveRuleAsync", "DeleteRuleAsync",
        "AllowSharedAsync", "EnsureTrashAsync", "SendDraftAsync",
    ];

    /// <summary>
    /// Il provider "vero" delle prove di TestingProvider: non fa nulla, scrive il nome di ogni metodo che gli arriva e
    /// restituisce un valore qualsiasi del tipo giusto. Le proprietà (Account, HasCalendar...) non contano come chiamate.
    /// </summary>
    public class Ch3Recorder : DispatchProxy
    {
        public readonly List<string> Calls = new();
        public readonly MailAccount Account = new() { Kind = AccountKind.Imap, Email = "prova@example.com", DisplayName = "Mario Rossi", Id = "prova-c65" };

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method == null) return null;
            if (method.Name == "get_Account") return Account;
            if (!method.IsSpecialName) lock (Calls) Calls.Add(method.Name);
            return Ch3Sample(method.ReturnType);
        }
    }

    /// <summary>
    /// Un valore qualsiasi di un tipo, per chiamare un metodo di cui si sa solo la firma: testo "prova", numeri a zero,
    /// elenchi con un elemento, oggetti di Missiva con le proprietà obbligatorie riempite, un Task già finito. Un tipo che
    /// non sa costruire (un'interfaccia nuova) solleva: chi chiama lo dice con il nome del metodo, invece di saltarlo.
    /// </summary>
    static object? Ch3Sample(Type type, int depth = 0)
    {
        if (type == typeof(void)) return null;
        if (type == typeof(string)) return "prova";
        if (type == typeof(CancellationToken)) return CancellationToken.None;
        if (type == typeof(Task)) return Task.CompletedTask;
        if (Nullable.GetUnderlyingType(type) != null) return null;
        if (type.IsValueType) return Activator.CreateInstance(type);
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (depth > 6) throw new NotSupportedException($"il tipo {type.Name} contiene sé stesso troppe volte");
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type.GenericTypeArguments[0]).Invoke(null, [Ch3Sample(type.GenericTypeArguments[0], depth + 1)]);
        if (typeof(Delegate).IsAssignableFrom(type))
        {
            var invoke = type.GetMethod("Invoke")!;
            return Expression.Lambda(type, Expression.Default(invoke.ReturnType), invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType))).Compile();
        }
        if (type.IsGenericType && type.GenericTypeArguments.Length == 1 && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
            && typeof(List<>).MakeGenericType(type.GenericTypeArguments[0]) is var listType && type.IsAssignableFrom(listType))
        {
            var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
            list.Add(Ch3Sample(type.GenericTypeArguments[0], depth + 1));
            return list;
        }
        if (type.IsInterface || type.IsAbstract) throw new NotSupportedException($"non so costruire un valore di tipo {type.Name}");
        var constructor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).FirstOrDefault() ?? throw new NotSupportedException($"il tipo {type.Name} non ha un costruttore pubblico");
        var made = constructor.Invoke(constructor.GetParameters().Select(p => Ch3Sample(p.ParameterType, depth + 1)).ToArray());
        // Le proprietà obbligatorie (required) che il costruttore lascia vuote: quelle dichiarate non null e rimaste null.
        var nullability = new NullabilityInfoContext();
        foreach (var property in type.GetProperties().Where(p => p.CanWrite && p.GetIndexParameters().Length == 0 && !p.PropertyType.IsValueType))
            if (property.GetValue(made) == null && nullability.Create(property).WriteState == NullabilityState.NotNull)
                property.SetValue(made, Ch3Sample(property.PropertyType, depth + 1));
        return made;
    }

    /// <summary>
    /// Chiama ogni metodo di IMailProvider, preso dall'interfaccia per riflessione, sul provider che wrap costruisce sopra
    /// un Ch3Recorder nuovo, con argomenti qualsiasi, e dice per ognuno quali metodi sono arrivati al registratore; Error
    /// se il metodo non si è potuto chiamare o ha sollevato (allora non si è visto nulla).
    /// </summary>
    static List<(MethodInfo Method, List<string> Inner, string? Error)> Ch3Probe(Func<IMailProvider, IMailProvider> wrap)
    {
        var seen = new List<(MethodInfo, List<string>, string?)>();
        foreach (var method in typeof(IMailProvider).GetMethods().Where(m => !m.IsSpecialName))
        {
            var recorder = DispatchProxy.Create<IMailProvider, Ch3Recorder>();
            string? error = null;
            try
            {
                var subject = wrap(recorder);
                var returned = method.Invoke(subject, method.GetParameters().Select(p => Ch3Sample(p.ParameterType)).ToArray());
                if (returned is Task task && !task.Wait(TimeSpan.FromSeconds(10))) error = "non è finito in dieci secondi";
            }
            catch (Exception ex)
            {
                var cause = ex;
                while (cause is TargetInvocationException or AggregateException && cause.InnerException != null) cause = cause.InnerException;
                error = $"{cause.GetType().Name}: {cause.Message}";
            }
            var calls = ((Ch3Recorder)(object)recorder).Calls;
            lock (calls) seen.Add((method, calls.ToList(), error));
        }
        return seen;
    }

    /// <summary>Il pezzo di un sorgente da un testo a un altro che viene dopo (il primo compreso), o vuoto se mancano.</summary>
    static string Ch3Part(string source, string start, string end) =>
        source.IndexOf(start, StringComparison.Ordinal) is >= 0 and var from && source.IndexOf(end, from, StringComparison.Ordinal) is var to && to > from ? source[from..to] : "";

    /// <summary>I testi ci sono tutti, ognuno dopo quello di prima.</summary>
    static bool Ch3InOrder(string text, params string[] parts)
    {
        int at = 0;
        foreach (var part in parts)
        {
            int found = text.IndexOf(part, at, StringComparison.Ordinal);
            if (found < 0) return false;
            at = found + part.Length;
        }
        return true;
    }

    /// <summary>Un metodo con i tipi dei suoi argomenti, per dire quale di due con lo stesso nome.</summary>
    static string Ch3Name(MethodInfo method) => $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    /// <summary>
    /// Scelta 33 (C-65): TestingProvider è quello che, nella versione di prova (MISSIVA_NO_READ, prova.ps1 su una copia dei
    /// dati veri), ferma tutto ciò che scriverebbe sulla casella vera. Un metodo nuovo di IMailProvider si scrive in quattro
    /// posti, e uno che scrive, passato per sbaglio al provider vero, scriverebbe davvero dalla versione di prova: nulla lo
    /// controllava. Qui ogni metodo dell'interfaccia, preso per riflessione (uno nuovo entra da solo), deve stare in uno
    /// dei due elenchi qui sopra, e chiamato su TestingProvider può far arrivare al provider interno solo letture.
    /// </summary>
    static void Ch3TestingProvider()
    {
        var methods = typeof(IMailProvider).GetMethods().Where(m => !m.IsSpecialName).ToList();
        var names = methods.Select(m => m.Name).Distinct().ToList();
        var unknown = names.Where(n => !Ch3Reads.Contains(n) && !Ch3Writes.Contains(n)).ToList();
        var twice = Ch3Reads.Intersect(Ch3Writes).Concat(Ch3Reads.GroupBy(n => n).Concat(Ch3Writes.GroupBy(n => n)).Where(g => g.Count() > 1).Select(g => g.Key)).ToList();
        var gone = Ch3Reads.Concat(Ch3Writes).Where(n => !names.Contains(n)).ToList();
        Check(unknown.Count == 0 && twice.Count == 0 && gone.Count == 0 && methods.Count > 70,
              $"scelta 33 (C-65), ogni metodo di IMailProvider è in uno dei due elenchi della prova: {methods.Count} metodi con {names.Count} nomi, {Ch3Reads.Length} di sola lettura e " +
              $"{Ch3Writes.Length} che scrivono, inviano o aprono una finestra di consenso" +
              (unknown.Count == 0 ? "" : $". NUOVI, IN NESSUN ELENCO: {string.Join(", ", unknown)}: leggi che cosa fanno sui tre provider e mettili in Ch3Reads o in Ch3Writes (ProvaBozze.Scelte3.cs)") +
              (twice.Count == 0 ? "" : $". In tutti e due gli elenchi, o due volte: {string.Join(", ", twice)}") +
              (gone.Count == 0 ? "" : $". Negli elenchi ma non più nell'interfaccia: {string.Join(", ", gone)}"));

        var probe = Ch3Probe(real => new TestingProvider(real));
        var unseen = probe.Where(p => p.Error != null).Select(p => $"{Ch3Name(p.Method)}: {p.Error}").ToList();
        // Quello che arriva al provider vero e non è una lettura: una scrittura inoltrata, o un metodo che nessuno ha ancora guardato.
        var leaks = probe.Where(p => p.Inner.Any(c => !Ch3Reads.Contains(c))).Select(p => $"{Ch3Name(p.Method)} chiama {string.Join(" e ", p.Inner.Where(c => !Ch3Reads.Contains(c)).Distinct())}").ToList();
        var stopped = probe.Where(p => !Ch3Reads.Contains(p.Method.Name)).ToList();
        var reading = stopped.Where(p => p.Inner.Count > 0).Select(p => $"{p.Method.Name} legge con {string.Join(", ", p.Inner.Distinct())}").ToList();
        Check(unseen.Count == 0 && leaks.Count == 0 && stopped.Count >= Ch3Writes.Length,
              $"scelta 33 (C-65), nessuna scrittura arriva al provider vero dalla versione di prova: i {stopped.Count} metodi che scrivono, chiamati su TestingProvider sopra un provider che " +
              $"registra ogni chiamata, non gli arrivano; {stopped.Count - reading.Count} non lo toccano affatto, {reading.Count} gli chiedono solo una lettura ({string.Join("; ", reading)})" +
              (leaks.Count == 0 ? "" : $". ARRIVANO AL PROVIDER VERO: {string.Join("; ", leaks)}") + (unseen.Count == 0 ? "" : $". Non si sono potuti chiamare: {string.Join("; ", unseen)}"));

        var read = probe.Where(p => Ch3Reads.Contains(p.Method.Name)).ToList();
        var lost = read.Where(p => !p.Inner.Contains(p.Method.Name)).Select(p => Ch3Name(p.Method)).ToList();
        Check(lost.Count == 0 && read.Count >= Ch3Reads.Length,
              $"scelta 33 (C-65), le {read.Count} letture arrivano tutte al provider vero, ognuna con il suo metodo: una dimenticata in TestingProvider, nella versione di prova, risponderebbe " +
              "con il valore di base dell'interfaccia (nessun calendario, nessuna regola) senza dirlo" + (lost.Count == 0 ? "" : $". NON ARRIVANO: {string.Join(", ", lost)}"));

        // Il controllo che la prova veda: lo stesso giro su un provider che passa tutto (il registratore stesso) deve trovare
        // ogni scrittura arrivata, una per una, con gli stessi argomenti di prima.
        var open = Ch3Probe(real => real);
        var caught = open.Where(p => !Ch3Reads.Contains(p.Method.Name) && p.Inner.Contains(p.Method.Name) && p.Error == null).ToList();
        Check(caught.Count == stopped.Count && caught.Count > 40,
              $"scelta 33 (C-65), il controllo vede: lo stesso giro su un provider che inoltra tutto trova arrivate al provider vero {caught.Count} scritture su {stopped.Count}");

        // I provider veri nascono in un posto solo, che nella versione di prova li avvolge: un secondo posto li userebbe nudi.
        if (SourceRoot() is { } root && Ch1Code("UI", "MainWindow.xaml.cs") is { } main)
        {
            var created = new System.Text.RegularExpressions.Regex(@"new (GraphProvider|GmailProvider|ImapProvider)\(");
            var places = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f))
                .Where(f => !f.StartsWith("bin" + Path.DirectorySeparatorChar) && !f.StartsWith("obj" + Path.DirectorySeparatorChar) && !Path.GetFileName(f).StartsWith("ProvaBozze"))
                .Select(f => (File: f, Count: created.Matches(Ch1Code(f.Split(Path.DirectorySeparatorChar)) ?? "").Count)).Where(x => x.Count > 0).ToList();
            var factory = Ch3Part(main, "static IMailProvider NewProvider(MailAccount account)", "readonly TaskCompletionSource accountsReady");
            bool wrapped = created.Matches(factory).Count == 3 && Ch3InOrder(factory, "if (Testing)", "provider = new TestingProvider(provider);", "return provider;");
            Check(places is [{ File: var where, Count: 3 }] && where == Path.Combine("UI", "MainWindow.xaml.cs") && wrapped,
                  $"scelta 33 (C-65), i provider veri nascono in un posto solo, MainWindow.NewProvider, che nella versione di prova li mette dentro TestingProvider prima di darli a chiunque " +
                  $"({string.Join(", ", places.Select(p => $"{p.File}: {p.Count}"))}; avvolti: {wrapped})");
        }
    }

    /// <summary>Un messaggio da leggere per i server finti di questo gruppo, se serve con un allegato di testo.</summary>
    static MimeMessage Ch3Mail(string subject, string text = "Testo di prova", bool file = false, int daysAgo = 0)
    {
        var builder = new BodyBuilder { HtmlBody = $"<p>{text}</p>" };
        if (file) builder.Attachments.Add("preventivo.txt", Encoding.UTF8.GetBytes("preventivo di ottobre"), new ContentType("text", "plain"));
        var mime = new MimeMessage { Subject = subject, Date = DateTimeOffset.Now.AddDays(-daysAgo).AddMinutes(-5), Body = builder.ToMessageBody() };
        mime.From.Add(new MailboxAddress("Maria Rossi", "maria.rossi@fornitori-prova.example"));
        mime.To.Add(new MailboxAddress("Mario Rossi", "mario@studio-prova.example"));
        return mime;
    }

    /// <summary>Le cartelle di un server IMAP finto con i loro messaggi, segni ed etichette, in una riga: per vedere se qualcosa è cambiato.</summary>
    static string Ch3State(FakeImap fake) =>
        string.Join(" | ", fake.Folders.OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}: {string.Join(", ", f.Value.Select(m => $"{m.Uid}[{m.Flags}]{(m.Labels.Count > 0 ? "{" + string.Join("+", m.Labels) + "}" : "")}"))}"));

    /// <summary>
    /// Ogni metodo di IMailProvider chiamato su TestingProvider sopra un provider vero collegato a un server finto, con
    /// argomenti che su quel server esistono (un messaggio, la sua cartella, il suo allegato): come la versione di prova
    /// sulla casella vera. Dice quali non si sono potuti chiamare; che cosa è arrivato al server lo guarda chi chiama.
    /// </summary>
    /// <param name="watching">Aspetta che la sorveglianza della Posta in arrivo (IDLE) sia arrivata al server, prima di fermarla.</param>
    static async Task<List<string>> Ch3CallAllAsync(IMailProvider real, MessageSummary message, MailFolder inbox, Func<Task>? watching = null)
    {
        var testing = new TestingProvider(real);
        var failed = new List<string>();
        using var watch = new CancellationTokenSource();
        var attachment = new Attachment { Id = "2", Name = "preventivo.txt", ContentType = "text/plain", Size = 21 };
        object? Argument(ParameterInfo p) =>
            p.ParameterType == typeof(MessageSummary) ? message
            : p.ParameterType == typeof(IReadOnlyList<MessageSummary>) ? new List<MessageSummary> { message }
            : p.ParameterType == typeof(MailFolder) ? inbox
            : p.ParameterType == typeof(Attachment) ? attachment
            : p.ParameterType == typeof(DateTime) ? (p.Name == "to" ? DateTime.Now.AddDays(1) : p.Name == "from" ? DateTime.Now.AddDays(-2) : DateTime.UtcNow.AddDays(-2))
            : p.ParameterType == typeof(int) ? (p.Name == "limit" ? 2000 : 10)
            : p.ParameterType == typeof(string) && p.Name is "next" or "mark" ? null
            : p.ParameterType == typeof(CancellationToken) && p.Member.Name == "WatchInboxAsync" ? watch.Token
            : Ch3Sample(p.ParameterType);
        static string Why(Exception ex)
        {
            while (ex is TargetInvocationException or AggregateException && ex.InnerException != null) ex = ex.InnerException;
            return $"{ex.GetType().Name}: {ex.Message}";
        }
        foreach (var method in typeof(IMailProvider).GetMethods().Where(m => !m.IsSpecialName))
        {
            try
            {
                if (method.Invoke(testing, method.GetParameters().Select(Argument).ToArray()) is not Task task) continue;
                if (method.Name == "WatchInboxAsync")
                {
                    if (watching != null) await watching();
                    watch.Cancel();
                }
                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15))) != task) failed.Add($"{Ch3Name(method)}: non è finito in quindici secondi");
                else await task;
            }
            catch (Exception ex)
            {
                failed.Add($"{Ch3Name(method)}: {Why(ex)}");
            }
        }
        // La ricerca in tutte le cartelle (cartella null) fa un'altra strada; e il corpo ancora una volta, che aspetta quello chiesto in anticipo da PrefetchAsync.
        try
        {
            await testing.SearchAsync(null, "prova");
            await testing.GetBodyAsync(message);
        }
        catch (Exception ex)
        {
            failed.Add($"ricerca in tutte le cartelle e corpo: {Why(ex)}");
        }
        return failed;
    }

    /// <summary>
    /// Scelta 33 (C-65), l'altra metà: l'elenco delle letture dice "questi metodi non scrivono", e la prova con il provider
    /// che registra non lo può sapere. Qui TestingProvider sta sopra i tre provider veri, collegati ai server finti, e si
    /// chiamano tutti i metodi dell'interfaccia: ai server devono arrivare solo comandi che leggono (IMAP: LIST, STATUS,
    /// SELECT, EXAMINE, FETCH sempre con BODY.PEEK, SEARCH, NOOP, IDLE; Graph e Google: GET, più le due POST che chiedono
    /// la disponibilità), e sui server IMAP messaggi, segni, etichette e cartelle devono restare com'erano. Se una lettura
    /// domani segnasse letto il messaggio che apre, cadrebbe qui.
    /// </summary>
    static async Task Ch3TestingOnServersAsync()
    {
        string[] readVerbs = ["CAPABILITY", "AUTHENTICATE", "LOGIN", "LIST", "STATUS", "SELECT", "EXAMINE", "FETCH", "UID FETCH", "UID SEARCH", "SEARCH", "NOOP", "IDLE", "LOGOUT"];
        static string Verb(string command) => command.Split(' ') is ["UID" or "uid", var second, ..] ? "UID " + second.ToUpperInvariant() : command.Split(' ')[0].ToUpperInvariant();
        static HttpResponseMessage Json(JsonNode json) => new(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        var inbox = new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox };
        // L'invio dei due account va a un server SMTP finto su questo PC: se un invio sfuggisse a TestingProvider arriverebbe
        // qui, e si vedrebbe, invece di cercare il server vero.
        using var smtp = new FakeSmtp();

        // Un server IMAP finto con due messaggi da leggere, uno con un allegato; poi tutti i metodi, e che cosa è arrivato.
        async Task<(List<string> Failed, List<string> Verbs, List<string> Writes, bool Peek, bool Same, int Commands, int Smtp)> OnImapAsync(FakeImap fake, Func<IMailProvider> create, bool gmail)
        {
            int smtpBefore;
            lock (smtp.Commands) smtpBefore = smtp.Commands.Count + smtp.Received.Count;
            var stored = fake.Add("INBOX", Ch3Mail("Preventivo di prova", file: true));
            fake.Add("INBOX", Ch3Mail("Secondo messaggio"));
            var message = new MessageSummary
            {
                Id = stored.Uid.ToString(), FolderId = "INBOX", Subject = "Preventivo di prova", Date = DateTime.Now.AddMinutes(-5),
                From = new Address { Name = "Maria Rossi", Email = "maria.rossi@fornitori-prova.example" },
                StableId = gmail ? stored.MsgId.ToString() : MimeMessage.Load(new MemoryStream(stored.Raw)).MessageId ?? "", ConversationId = gmail ? stored.MsgId.ToString() : "",
            };
            string before = Ch3State(fake);
            var real = create();
            List<string> failed;
            try
            {
                failed = await Ch3CallAllAsync(real, message, inbox, () => Until(() => fake.Idling == 1, TimeSpan.FromSeconds(5)));
            }
            finally
            {
                (real as IDisposable)?.Dispose();
            }
            List<string> commands;
            lock (fake.Traffic) commands = fake.Traffic.Select(t => t.Text).ToList();
            var verbs = commands.Select(Verb).Distinct().ToList();
            int smtpNow;
            lock (smtp.Commands) smtpNow = smtp.Commands.Count + smtp.Received.Count;
            return (failed, verbs, verbs.Where(v => !readVerbs.Contains(v)).ToList(), !commands.Any(c => c.Contains("BODY[", StringComparison.OrdinalIgnoreCase)), Ch3State(fake) == before, commands.Count,
                    smtpNow - smtpBefore);
        }
        string Told((List<string> Failed, List<string> Verbs, List<string> Writes, bool Peek, bool Same, int Commands, int Smtp) r) =>
            $"{r.Commands} comandi, solo {string.Join(", ", r.Verbs.Where(v => v is not ("CAPABILITY" or "AUTHENTICATE" or "LOGIN" or "LOGOUT")).Order())}; nessun BODY[] senza PEEK ({r.Peek}); " +
            $"messaggi, segni, etichette e cartelle come prima ({r.Same}); al server SMTP finto {(r.Smtp == 0 ? "nulla" : $"{r.Smtp} COMANDI")}" +
            (r.Writes.Count == 0 ? "" : $". COMANDI CHE SCRIVONO: {string.Join(", ", r.Writes)}") +
            (r.Failed.Count == 0 ? "" : $". Non si sono potuti chiamare: {string.Join("; ", r.Failed)}");
        static bool Good((List<string> Failed, List<string> Verbs, List<string> Writes, bool Peek, bool Same, int Commands, int Smtp) r) =>
            r.Failed.Count == 0 && r.Writes.Count == 0 && r.Peek && r.Same && r.Smtp == 0 && r.Verbs.Contains("UID FETCH") && r.Verbs.Contains("UID SEARCH") && r.Verbs.Contains("IDLE") && r.Verbs.Contains("LIST");

        ImapProvider.TestPlain = true;
        ImapProvider.TestCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            var account = SpeedAccount(fake, "mario@studio-prova.example");
            (account.Imap!.SmtpPort, account.Imap.SmtpSecurity) = (smtp.Port, MailSecurity.StartTls);
            var imap = await OnImapAsync(fake, () => new ImapProvider(account), gmail: false);
            Check(Good(imap), $"scelta 33 (C-65), tutti i metodi di IMailProvider chiamati su TestingProvider sopra un account IMAP vero, sul server finto: al server arrivano {Told(imap)}");
        }
        finally
        {
            ImapProvider.TestPlain = false;
            ImapProvider.TestCertificate = null;
        }

        // Gmail: lo stesso sul server finto di Gmail, più il calendario e i contatti di Google su un gestore finto.
        var google = new List<string>();
        try
        {
            using var fake = new FakeImap();
            GmailProvider.TestPort = fake.Port;
            (GmailProvider.TestSmtpPort, GmailProvider.TestSmtpCertificate) = (smtp.Port, smtp.Certificate.GetCertHashString());
            GoogleAuth.TestToken = "prova-google";
            GoogleApi.UseTestHandler(new FakeHttp(request =>
            {
                lock (google) google.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
                return Json(new JsonObject { ["items"] = new JsonArray(), ["connections"] = new JsonArray(), ["calendars"] = new JsonObject() });
            }));
            var account = new MailAccount
            {
                Kind = AccountKind.Gmail, Email = "prova@example.com", DisplayName = "Prova", Id = "gmail-scelta33",
                GoogleScopes = $"https://mail.google.com/ {GoogleAuth.CalendarScope} {GoogleAuth.ContactsScope}",
            };
            var gmail = await OnImapAsync(fake, () => new GmailProvider(account), gmail: true);
            List<string> asked;
            lock (google) asked = google.ToList();
            var written = asked.Where(r => !r.StartsWith("GET ") && !(r.StartsWith("POST ") && r.EndsWith("/freeBusy"))).Distinct().ToList();
            Check(Good(gmail) && written.Count == 0 && asked.Any(r => r.Contains("calendarList")) && asked.Any(r => r.Contains("connections")) && asked.Any(r => r.EndsWith("/freeBusy")),
                  $"scelta 33 (C-65), lo stesso sopra un account Gmail vero: al server IMAP finto arrivano {Told(gmail)}; a Google (calendario, contatti) {asked.Count} richieste, tutte GET " +
                  "tranne la POST che chiede la disponibilità (freeBusy)" + (written.Count == 0 ? "" : $". RICHIESTE CHE SCRIVONO: {string.Join(", ", written)}"));
        }
        finally
        {
            GmailProvider.TestPort = null;
            (GmailProvider.TestSmtpPort, GmailProvider.TestSmtpCertificate) = (null, null);
            GoogleAuth.TestToken = null;
            GoogleApi.UseTestHandler(new NoHandler("Google"));
        }

        // Graph: con tutti i consensi già dati (calendario, impostazioni della casella, contatti, persone) e un account di lavoro.
        var requests = new List<string>();
        var consents = (GraphProvider.TestCalendarConsent, GraphProvider.TestSettingsConsent, GraphProvider.TestContactsConsent, GraphProvider.TestPeopleConsent);
        try
        {
            GraphProvider.UseTestHandler(new FakeHttp(request =>
            {
                var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
                lock (requests) requests.Add($"{request.Method} {path}");
                if (path.EndsWith("/$value")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("Subject: Prova\r\n\r\nTesto")) };
                return Json(new JsonObject
                {
                    ["value"] = new JsonArray(), ["id"] = "prova", ["unreadItemCount"] = 0, ["hasAttachments"] = false,
                    ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = "<p>Testo di prova</p>" },
                });
            }));
            (GraphProvider.TestCalendarConsent, GraphProvider.TestSettingsConsent, GraphProvider.TestContactsConsent, GraphProvider.TestPeopleConsent) = (true, true, true, true);
            var graph = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio-prova.example", DisplayName = "Mario Rossi", Tenant = MicrosoftAuth.Organizations, Id = "graph-scelta33" });
            var message = new MessageSummary { Id = "messaggio-1", FolderId = "posta-in-arrivo", Subject = "Preventivo di prova", StableId = "messaggio-1", ConversationId = "conversazione-1", Date = DateTime.Now.AddMinutes(-5) };
            var failed = await Ch3CallAllAsync(graph, message, new MailFolder { Id = "posta-in-arrivo", Name = "Posta in arrivo", Kind = FolderKind.Inbox });
            List<string> asked;
            lock (requests) asked = requests.ToList();
            var written = asked.Where(r => !r.StartsWith("GET ") && !(r.StartsWith("POST ") && r.EndsWith("/me/calendar/getSchedule"))).Distinct().ToList();
            string[] wanted = ["/mailFolders", "/messages/messaggio-1", "/$value", "/me/calendars", "/me/contacts", "/me/people", "/me/mailboxSettings/automaticRepliesSetting", "/messageRules", "/me/outlook/masterCategories", "/me/calendar/getSchedule"];
            var missing = wanted.Where(w => !asked.Any(r => r.Contains(w))).ToList();
            Check(failed.Count == 0 && written.Count == 0 && missing.Count == 0,
                  $"scelta 33 (C-65), lo stesso sopra un account Microsoft vero con tutti i consensi dati, sul gestore finto di Graph: {asked.Count} richieste, tutte GET (cartelle, elenchi, corpo, " +
                  "allegato, calendario, contatti, persone, risposte automatiche, regole, categorie) tranne la POST che chiede la disponibilità (getSchedule)" +
                  (written.Count == 0 ? "" : $". RICHIESTE CHE SCRIVONO: {string.Join(", ", written)}") + (missing.Count == 0 ? "" : $". Mai chieste: {string.Join(", ", missing)}") +
                  (failed.Count == 0 ? "" : $". Non si sono potuti chiamare: {string.Join("; ", failed)}"));
        }
        finally
        {
            (GraphProvider.TestCalendarConsent, GraphProvider.TestSettingsConsent, GraphProvider.TestContactsConsent, GraphProvider.TestPeopleConsent) = consents;
            GraphProvider.UseTestHandler(new NoHandler("Graph"));
        }
    }
}
#endif
