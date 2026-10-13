#if DEBUG
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;

namespace Missiva;

/// <summary>
/// Audit dell'11 ottobre, codice, terzo gruppo (docs\audit\ESITO.md): le soglie scritte in più posti (C-38, C-39) e la
/// suite stessa (C-40..C-49). Qui i controlli nuovi: i numeri che devono restare uguali o in ordine fra loro, e la suite
/// messa alla prova con gruppi finti (uno che solleva, uno che lascia acceso un gancio, uno che fa una richiesta senza
/// gestore finto) e con il server IMAP finto fatto sbagliare apposta. Gli altri punti hanno cambiato controlli che
/// c'erano già (la ricerca annullata, il disegno che non finisce, i non letti dopo una scrittura).
/// Con "Missiva.exe --prova-audit risultato.txt prove" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void SuiteAuditTests()
    {
        Report.Add("--- Audit dell'11 ottobre, codice, terzo gruppo: soglie e prove (C-38..C-49)");
        SuiSharedLimits();
        SuiLinkedLimits();
        SuiGroupNames();
        SuiBrokenGroup();
        SuiLeftHooks();
        SuiFreshData();
        Wait(SuiServerErrorAsync);
        Wait(SuiAttachedMessageAsync);
        Wait(SuiUnknownCommandAsync);
        SuiFolderKeys();
        SuiMailto();
    }

    /// <summary>Il valore di una costante privata di una classe, letto dai metadati; null se la classe non ne ha una con quel nome.</summary>
    static object? SuiConstant(Type type, string name) =>
        type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) is { IsLiteral: true } field ? field.GetRawConstantValue() : null;

    /// <summary>
    /// C-38: i numeri che Gmail, IMAP e Graph devono avere uguali vengono da MailLimits. Ogni provider tiene il suo nome
    /// (PageSize, SmallMessage...), che ora vale quello comune: se qualcuno ne riscrive uno con un numero diverso, cade.
    /// </summary>
    static void SuiSharedLimits()
    {
        (string Name, object Shared, Type[] Users)[] limits =
        [
            ("PageSize", MailLimits.PageSize, [typeof(GmailProvider), typeof(ImapProvider), typeof(GraphProvider)]),
            ("BodyCacheSize", MailLimits.BodyCacheSize, [typeof(GmailProvider), typeof(ImapProvider)]),
            ("SmallMessage", MailLimits.SmallMessage, [typeof(GmailProvider), typeof(ImapProvider)]),
            ("KeepAttachment", MailLimits.KeepAttachment, [typeof(GmailProvider), typeof(ImapProvider)]),
        ];
        var apart = (from limit in limits from user in limit.Users let own = SuiConstant(user, limit.Name) where !Equals(own, limit.Shared)
                     select $"{user.Name}.{limit.Name} vale {own ?? "(manca)"} e non {limit.Shared}").ToList();
        Check(apart.Count == 0,
              $"C-38, i numeri che i provider devono avere uguali vengono da un posto solo: messaggi per pagina ({MailLimits.PageSize}) per Gmail, IMAP e Graph; corpi in memoria " +
              $"({MailLimits.BodyCacheSize}), messaggio scaricato intero (sotto {MailLimits.SmallMessage:N0} byte) e allegati della bozza tenuti in memoria (sotto {MailLimits.KeepAttachment / 1024 / 1024} MB) " +
              $"per Gmail e IMAP" + (apart.Count == 0 ? "" : " [" + string.Join("; ", apart) + "]"));
    }

    /// <summary>
    /// C-39: le soglie che devono restare in ordine fra loro. Quelle che si potevano legare lo sono nel codice (la rete di
    /// sicurezza del controllo periodico al giro del suo timer, la copia degli ultimi giorni al limite e alla finestra di
    /// BodyCache); qui si guarda l'ordine, che vale anche per quelle rimaste numeri (a riposo: le prove ne cambiano due).
    /// </summary>
    static void SuiLinkedLimits()
    {
        var wrong = new List<string>();
        void Must(bool ok, string what) { if (!ok) wrong.Add(what); }
        Must(UI.App.SafetyNet < UI.App.PollEvery && UI.App.SafetyNet > UI.App.PollEvery / 2,
             $"la rete di sicurezza del controllo ({UI.App.SafetyNet.TotalSeconds:0} s) deve stare poco sotto il giro del timer ({UI.App.PollEvery.TotalSeconds:0} s)");
        Must(UI.App.DeltaEvery < UI.App.DeltaFresh,
             $"una query delta vale \"da poco\" per {UI.App.DeltaFresh.TotalSeconds:0} s, che deve essere più del suo giro ({UI.App.DeltaEvery.TotalSeconds:0} s)");
        Must(OfflineCopy.TestFull < BodyCache.TestLimit && BodyCache.TestLimit == BodyCache.DefaultLimit,
             $"la copia degli ultimi giorni è piena a {OfflineCopy.TestFull / 1024 / 1024} MB, che deve stare sotto il limite di BodyCache ({BodyCache.TestLimit / 1024 / 1024} MB)");
        Must(OfflineCopy.Days <= BodyCache.MaxDays, $"la copia scarica gli ultimi {OfflineCopy.Days} giorni, che non devono superare quelli che BodyCache tiene ({BodyCache.MaxDays})");
        Must(GraphProvider.UploadChunk % (320 * 1024) == 0 && GraphProvider.UploadChunk < 4 * 1024 * 1024 && GraphProvider.UploadChunk < GraphProvider.LargeAttachment,
             $"un pezzo di un allegato grande ({GraphProvider.UploadChunk / 1024} KiB) deve essere multiplo di 320 KiB, sotto i 4 MB di Graph e sotto la misura da cui si carica a pezzi");
        Check(wrong.Count == 0,
              $"C-39, le soglie che devono restare in ordine fra loro: rete di sicurezza {UI.App.SafetyNet.TotalSeconds:0} s sotto il giro del controllo di {UI.App.PollEvery.TotalSeconds:0} s, " +
              $"query delta ogni {UI.App.DeltaEvery.TotalSeconds:0} s e \"da poco\" per {UI.App.DeltaFresh.TotalSeconds:0}, copia piena a {OfflineCopy.TestFull / 1024 / 1024} MB sotto i " +
              $"{BodyCache.TestLimit / 1024 / 1024} di BodyCache, {OfflineCopy.Days} giorni come BodyCache, pezzi di {GraphProvider.UploadChunk / 1024} KiB" +
              (wrong.Count == 0 ? "" : " [" + string.Join("; ", wrong) + "]"));
    }

    /// <summary>
    /// C-40: i nomi dei gruppi. Uno per gruppo, senza accenti né spazi; quelli che c'erano prima della tabella ci sono
    /// ancora e avviano lo stesso gruppo; un nome che non c'è non fa girare nulla (prima "velocità", con l'accento, faceva
    /// girare i blocchi 4 e 5 e finiva con "tutto bene").
    /// </summary>
    static void SuiGroupNames()
    {
        var names = Groups.Select(g => g.Name).ToList();
        var odd = names.Where(n => !Regex.IsMatch(n, "^[a-z0-9]+(-[a-z0-9]+)*$")).ToList();
        var twice = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        (string Name, Action Run)[] before =
        [
            ("senza-rete", () => Wait(OfflineCopyTestsAsync)), ("coda", () => Wait(OfflineQueueAsync)), ("copia", BackupAuditTests), ("copia2", Backup2AuditTests),
            ("sicurezza2", Security2AuditTests), ("sicurezza3", Security3AuditTests), ("comportamento", BehaviourAuditTests), ("robustezza", RobustnessAuditTests),
            ("caduta", RobCrash), ("velocita", SpeedAuditTests), ("velocita2", Speed2AuditTests), ("velocita3", Speed3AuditTests), ("sicurezza", SecurityLowAuditTests),
            ("arrivi", ArrivalsAuditTests), ("avvio", StartupAuditTests), ("aperto", RunningAuditTests), ("dati", DataAuditTests), ("difetti", DefectAuditTests),
            ("funzioni", FeatureAuditTests), ("pdf", () => { }),
        ];
        // Il metodo che un gruppo fa girare: dove la tabella lo nomina direttamente è quello; dietro una funzione anonima basta che il nome ci sia.
        var changed = before.Where(b => Pick(b.Name) is not [var only] || b.Run.Method.Name is { } method && !method.StartsWith('<') && only.Run.Method.Name != method).Select(b => b.Name).ToList();
        var audit = Pick(null)!.Select(g => g.Name).ToList();
        string[] auditBefore = ["html", "loghi", "mittente", "velocita", "difetti", "funzioni", "copia", "velocita2", "velocita3", "sicurezza", "arrivi", "avvio", "aperto", "dati",
                                "copia2", "sicurezza2", "sicurezza3", "comportamento", "robustezza", "prove", "residui"];
        bool unknown = Pick("velocità") == null && Pick("Velocita") == null && Pick("") == null && Pick("velocita ") == null && Pick("sconosciuto") == null;
        bool manual = Groups.Count(g => g.Manual) == 1 && Pick("caduta") is [{ Manual: true }] && !audit.Contains("caduta");
        // I gruppi nati dopo (dalle scelte del 12 ottobre: "scelte1" e seguenti) stanno in fondo, dopo quelli di prima.
        Check(odd.Count == 0 && twice.Count == 0 && changed.Count == 0 && unknown && manual && audit.Take(auditBefore.Length).SequenceEqual(auditBefore),
              $"C-40, i gruppi in una tabella sola: {names.Count} nomi diversi, senza accenti né spazi; i {before.Length} nomi di prima avviano gli stessi gruppi; senza nome girano i " +
              $"{audit.Count} gruppi degli audit nell'ordine di prima; un nome che non c'è (\"velocità\" con l'accento, una maiuscola, uno spazio) non fa girare nulla; \"caduta\" solo a mano" +
              (odd.Count + twice.Count + changed.Count == 0 ? "" : $" [strani: {string.Join(", ", odd)}; doppi: {string.Join(", ", twice)}; cambiati: {string.Join(", ", changed)}]"));
    }

    /// <summary>
    /// Fa girare dei gruppi finti come la suite fa girare i suoi (RunGroups) e toglie dal risultato quello che hanno
    /// scritto: le righe, i controlli e gli errori tornano a chi li guarda, e il giro vero resta com'era.
    /// </summary>
    static (List<string> Lines, int Failed, List<(string Name, int Checks, double Seconds)> Times) SuiRunApart(params TestGroup[] groups)
    {
        LeftBehind("prove"); // quello che questo gruppo ha lasciato fin qui si dice ora, non si confonde con i gruppi finti
        int failedBefore = failures, checksBefore = checks, linesBefore = Report.Count;
        var times = RunGroups(groups);
        var lines = Report.Skip(linesBefore).ToList();
        Report.RemoveRange(linesBefore, Report.Count - linesBefore);
        int failed = failures - failedBefore;
        (failures, checks) = (failedBefore, checksBefore);
        return (lines, failed, times);
    }

    /// <summary>
    /// C-40: un gruppo che solleva un errore non ferma i gruppi dopo. Prima c'era un try solo per tutta la suite: il
    /// risultato diceva "1 controlli non passati" e centinaia di controlli non erano nemmeno partiti.
    /// </summary>
    static void SuiBrokenGroup()
    {
        var ran = new List<string>();
        var (lines, failed, times) = SuiRunApart(
            new TestGroup("primo", () => { ran.Add("primo"); Check(true, "controllo del primo gruppo finto"); }),
            new TestGroup("rotto", () => { ran.Add("rotto"); Check(true, "controllo del gruppo che poi solleva"); throw new InvalidOperationException("errore di prova C-40"); }),
            new TestGroup("terzo", () => { ran.Add("terzo"); Check(true, "controllo del terzo gruppo finto"); Check(false, "controllo non passato del terzo gruppo finto"); }));
        var errors = lines.Where(l => l.StartsWith("ERRORE: ")).ToList();
        Check(ran.SequenceEqual(["primo", "rotto", "terzo"]) && failed == 2 && errors.Count == 1 && errors[0].Contains("\"rotto\"") && errors[0].Contains("errore di prova C-40")
              && lines.Count(l => l.StartsWith("ok    ")) == 3 && lines.Count(l => l.StartsWith("NO    ")) == 1 && times.Select(t => (t.Name, t.Checks)).SequenceEqual([("primo", 1), ("rotto", 1), ("terzo", 2)]),
              $"C-40, un gruppo che solleva un errore non ferma quelli dopo: di tre gruppi finti girano tutti ({string.Join(", ", ran)}), l'errore lascia una riga con il nome del gruppo e conta " +
              $"come un controllo non passato ({failed} con quello del terzo gruppo), e ogni gruppo ha i suoi controlli contati ({string.Join(", ", times.Select(t => $"{t.Name} {t.Checks}"))}); " +
              "prima il primo errore fermava tutta la suite");
    }

    /// <summary>
    /// C-44: un gruppo che lascia acceso un gancio di prova, o che fa una richiesta a Graph senza aver installato un
    /// gestore finto, viene detto; e il gruppo dopo parte con i ganci a riposo. Prima i ganci restavano com'erano
    /// all'ultima prova (il gestore di Graph non si toglieva mai), e una prova che dimenticava il suo riceveva le
    /// risposte di quello del gruppo di prima.
    /// </summary>
    static void SuiLeftHooks()
    {
        var found = new List<string>();
        string asked = "";
        var (lines, failed, _) = SuiRunApart(
            new TestGroup("lascia", () =>
            {
                ImapProvider.TestPlain = !ImapProvider.TestPlain;
                GraphProvider.TestCalendarConsent = true;
                MailDns.TestTxt = _ => [];
                GraphProvider.UseTestHandler(new FakeHttp(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"value\":[{\"id\":\"lasciata\",\"displayName\":\"Cartella del gruppo di prima\"}]}") }));
            }),
            new TestGroup("dopo", () =>
            {
                found.AddRange(HooksLeft());
                // Senza gestore suo: prima avrebbe ricevuto la cartella finta del gruppo "lascia".
                try { asked = string.Join(", ", new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@studio-prova.example", Id = "prova-c44" }).GetFoldersAsync().GetAwaiter().GetResult().Select(f => f.Name)); }
                catch (Exception ex) { asked = "errore: " + ex.GetType().Name; }
            }),
            new TestGroup("pulito", () => { }));
        var said = lines.Where(l => l.StartsWith("NO    ")).ToList();
        bool hooks = said.Any(l => l.Contains("\"lascia\"") && l.Contains("ImapProvider.TestPlain") && l.Contains("GraphProvider.TestCalendarConsent") && l.Contains("MailDns.TestTxt"));
        bool stray = said.Any(l => l.Contains("\"dopo\"") && l.Contains("senza un gestore finto") && l.Contains("Graph: GET"));
        Check(hooks && stray && said.Count == 2 && failed == 2 && found.Count == 0 && asked.StartsWith("errore: ") && !said.Any(l => l.Contains("\"pulito\"")) && HooksLeft().Count == 0,
              $"C-44, un gruppo che lascia accesi tre ganci viene detto con i loro nomi, e il gruppo dopo li trova a riposo ({found.Count} accesi); la sua richiesta a Graph fatta senza " +
              $"gestore finto non riceve le risposte del gruppo di prima ({asked}) e viene detta; un gruppo a posto non scrive nulla. I ganci tenuti d'occhio sono {Hooks.Length}");
    }

    /// <summary>
    /// C-49 (e C-43): i dati di questo giro stanno in una cartella nata con il giro. Prima la rubrica, il registro e le
    /// impostazioni della cartella di prova restavano da un giro all'altro: un controllo che cerca una riga nel registro
    /// la trovava anche se a scriverla era stato un giro di giorni prima.
    /// </summary>
    static void SuiFreshData()
    {
        using var self = Process.GetCurrentProcess();
        var started = self.StartTime.ToUniversalTime().AddSeconds(-1);
        var log = Path.Combine(Store.Dir, "missiva.log");
        Log.Write("prove: riga del giro in corso (C-49)");
        var old = Directory.EnumerateFileSystemEntries(Store.Dir).Where(e => File.GetCreationTimeUtc(e) < started).Select(Path.GetFileName).ToList();
        // La prima riga del registro: "2026-10-12 10:54:33  ...", all'ora del PC, al secondo.
        string first = File.ReadLines(log).FirstOrDefault() ?? "";
        bool ownLog = first.Length >= 19 && DateTime.TryParseExact(first[..19], "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                                                                 System.Globalization.DateTimeStyles.AssumeLocal, out var at) && at.ToUniversalTime() >= started.AddSeconds(-1);
        Check(runData.Length > 0 && Store.Dir == runData && Path.GetFileName(runData).StartsWith("giro-") && runData.StartsWith(suiteRoot, StringComparison.OrdinalIgnoreCase)
              && old.Count == 0 && ownLog,
              $"C-49, i dati di questo giro stanno in una cartella nata con il giro ({Path.GetFileName(runData)} dentro quella data): nulla lì dentro è più vecchio di questo processo " +
              $"(di prima: {(old.Count == 0 ? "niente" : string.Join(", ", old))}), e il registro che i controlli leggono comincia in questo giro");
    }

    /// <summary>
    /// C-45: un errore del server IMAP finto mentre serve una connessione. Prima l'errore si perdeva nel lavoro scartato:
    /// il client vedeva la connessione cadere (subito: il rapporto temeva un minuto di attesa, misurato non c'è) e la
    /// prova falliva senza la causa. Ora il server chiude con BYE e l'errore resta scritto: il gruppo che l'ha provocato
    /// viene detto, con il tipo e il testo dell'errore.
    /// </summary>
    static async Task SuiServerErrorAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        fake.Add("INBOX", SpeedMail("Preventivo", "Il preventivo di ottobre"));
        bool plain = ImapProvider.TestPlain;
        ImapProvider.TestPlain = true;
        try
        {
            using var provider = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            IMailProvider imap = provider;
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            fake.FailOn = "UID SEARCH";
            var clock = Stopwatch.StartNew();
            string outcome;
            try { outcome = $"{(await imap.SearchAsync(inbox, "preventivo")).Messages.Count} messaggi"; }
            catch (Exception ex) { outcome = ex.GetType().Name; }
            long ms = clock.ElapsedMilliseconds;
            fake.FailOn = null;
            List<string> errors;
            lock (ServerErrors) { errors = ServerErrors.ToList(); ServerErrors.Clear(); } // provocati apposta: il gruppo non li lascia
            var again = await imap.SearchAsync(inbox, "preventivo");
            Check(errors.Count > 0 && errors.All(e => e.StartsWith("IMAP, dopo \"UID\": InvalidOperationException: errore di prova")) && outcome is not ("TimeoutException" or "1 messaggi")
                  && again.Messages.Count == 1,
                  $"C-45, un errore del server IMAP finto mentre serve una connessione non si perde: la connessione si chiude con BYE ({outcome} per il client, dopo {ms} ms), l'errore " +
                  $"resta scritto per il gruppo con la sua causa ({errors.Count} volte: {errors.FirstOrDefault()}), e tolto il difetto la ricerca dopo trova il messaggio; prima il client " +
                  "vedeva solo la connessione cadere");
        }
        finally
        {
            ImapProvider.TestPlain = plain;
        }
    }

    /// <summary>
    /// C-45: un messaggio con un .eml allegato prima di un file. Il server finto elencava nella struttura solo i file, ma
    /// contava anche il .eml quando il client chiedeva una parte per numero: per il primo file prendeva il .eml, con un
    /// errore di conversione che faceva cadere la connessione.
    /// </summary>
    static async Task SuiAttachedMessageAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        const string text = "contenuto della fattura di prova";
        var mime = SpeedMail("Inoltro con un messaggio allegato", "Vedi gli allegati");
        mime.Body = new Multipart("mixed")
        {
            new TextPart("html") { Text = "<p>Vedi gli allegati</p>" },
            new MessagePart { Message = SpeedMail("Messaggio inoltrato", "Il messaggio dentro"), ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "inoltrato.eml" } },
            new MimePart("text", "plain")
            {
                Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(text))), ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64, FileName = "fattura.txt",
            },
        };
        fake.Add("INBOX", mime);
        bool plain = ImapProvider.TestPlain;
        ImapProvider.TestPlain = true;
        try
        {
            using var provider = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            IMailProvider imap = provider;
            var inbox = (await imap.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var message = (await imap.GetMessagesAsync(inbox, null, null)).Messages.Single();
            var clock = Stopwatch.StartNew();
            string got;
            try { got = Encoding.UTF8.GetString(await imap.GetAttachmentAsync(message, new Attachment { Id = "2", Name = "fattura.txt" })); }
            catch (Exception ex) { got = "errore: " + ex.GetType().Name; }
            long ms = clock.ElapsedMilliseconds;
            int errors;
            lock (ServerErrors) errors = ServerErrors.Count;
            Check(got == text && errors == 0,
                  $"C-45, sul server IMAP finto un messaggio con un .eml allegato prima di un file: il file chiesto per numero arriva ({(got == text ? "contenuto giusto" : got)}, {ms} ms), " +
                  $"senza errori del server ({errors}); prima al posto del file prendeva il .eml, con un errore di conversione che faceva cadere la connessione");
        }
        finally
        {
            ImapProvider.TestPlain = plain;
        }
    }

    /// <summary>
    /// C-47: rinominare o spostare una cartella ne cambia l'identificativo (in Gmail e in IMAP è il percorso), e quello che
    /// Missiva ricorda per identificativo deve seguirla: Preferiti, cartelle nascoste e chiuse, colori, ordine. Nessun
    /// controllo lo guardava. Qui MainWindow.MigrateFolderKeys sulle impostazioni, con una cartella che ha sottocartelle,
    /// una dal nome che comincia uguale e la stessa cartella in un altro account, che non devono cambiare.
    /// </summary>
    static void SuiFolderKeys()
    {
        var s = Store.Settings;
        const string id = "prova-c47", other = "altro-c47";
        using var provider = new ImapProvider(new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Id = id, Imap = new ImapServer() });
        var account = new UI.AccountItem(provider);
        void Forget()
        {
            bool Mine(string key) => key.StartsWith(id + "|", StringComparison.Ordinal) || key.StartsWith(other + "|", StringComparison.Ordinal);
            s.Favorites.RemoveAll(Mine);
            s.HiddenFolders.RemoveAll(Mine);
            s.CollapsedFolders.RemoveAll(Mine);
            foreach (var key in s.FolderColors.Keys.Where(Mine).ToList()) s.FolderColors.Remove(key);
            foreach (var key in s.CategoryColors.Keys.Where(Mine).ToList()) s.CategoryColors.Remove(key);
            s.FolderOrder.Remove(id);
            s.FolderOrder.Remove(other);
        }
        try
        {
            s.Favorites.AddRange([$"{id}|Lavoro", $"{id}|Lavoro/Clienti", $"{id}|Lavoro2", $"{other}|Lavoro"]);
            s.HiddenFolders.Add($"{id}|Lavoro/Archivio 2025");
            s.CollapsedFolders.Add($"{id}|Lavoro");
            (s.FolderColors[$"{id}|Lavoro"], s.FolderColors[$"{id}|Lavoro/Clienti"], s.FolderColors[$"{other}|Lavoro"]) = (3, 5, 7);
            s.CategoryColors[$"{id}|Lavoro"] = 9;
            (s.FolderOrder[id], s.FolderOrder[other]) = (["INBOX", "Lavoro", "Lavoro/Clienti", "Lavoro2"], ["Lavoro"]);
            UI.MainWindow.MigrateFolderKeys(account, "Lavoro", "Progetti");
            static string Colors(Dictionary<string, int> all, string a, string b) =>
                string.Join(", ", all.Where(c => c.Key.StartsWith(a + "|") || c.Key.StartsWith(b + "|")).OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key}={c.Value}"));
            string favorites = string.Join(", ", s.Favorites.Where(f => f.Contains("-c47|")));
            string hidden = string.Join(", ", s.HiddenFolders.Where(f => f.Contains("-c47|"))), collapsed = string.Join(", ", s.CollapsedFolders.Where(f => f.Contains("-c47|")));
            string colors = Colors(s.FolderColors, id, other), categories = Colors(s.CategoryColors, id, other);
            string order = string.Join(", ", s.FolderOrder[id]), otherOrder = string.Join(", ", s.FolderOrder[other]);
            Check(favorites == $"{id}|Progetti, {id}|Progetti/Clienti, {id}|Lavoro2, {other}|Lavoro" && hidden == $"{id}|Progetti/Archivio 2025" && collapsed == $"{id}|Progetti"
                  && colors == $"{other}|Lavoro=7, {id}|Progetti=3, {id}|Progetti/Clienti=5" && categories == $"{id}|Progetti=9" && order == "INBOX, Progetti, Progetti/Clienti, Lavoro2" && otherOrder == "Lavoro",
                  $"C-47, una cartella rinominata (Lavoro in Progetti) porta con sé quello che Missiva ricorda per identificativo, anche delle sue sottocartelle: Preferiti ({favorites}), " +
                  $"nascoste ({hidden}), chiuse ({collapsed}), colori ({colors}; categoria {categories}), ordine ({order}); una cartella dal nome che comincia uguale e quella di un altro account restano");

            // Misura, non controllo: un server IMAP con il punto come separatore (INBOX.Clienti.Rossi). Fino alle scelte del 12
            // ottobre le sottocartelle si riconoscevano dalla barra; ora dal separatore dell'account, letto dalle sue cartelle
            // (il controllo è in Choices2Tests).
            Forget();
            account.SetFolders([new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox }, new MailFolder { Id = "INBOX.Clienti", Name = "Clienti", ParentId = "INBOX" },
                                new MailFolder { Id = "INBOX.Clienti.Rossi", Name = "Rossi", ParentId = "INBOX.Clienti" }]);
            s.Favorites.AddRange([$"{id}|INBOX.Clienti", $"{id}|INBOX.Clienti.Rossi"]);
            UI.MainWindow.MigrateFolderKeys(account, "INBOX.Clienti", "INBOX.Fornitori");
            Report.Add($"      (C-47, misura: con il punto come separatore, rinominata INBOX.Clienti in INBOX.Fornitori, i Preferiti diventano {string.Join(", ", s.Favorites.Where(f => f.Contains("-c47|")))}: " +
                       $"la sottocartella {(s.Favorites.Contains($"{id}|INBOX.Fornitori.Rossi") ? "segue, con il separatore dell'account" : "non segue, perché le sottocartelle si cercano con la barra")})");
        }
        finally
        {
            Forget();
            Store.Save();
        }
    }

    /// <summary>
    /// C-47: i link mailto: (RFC 6068) letti da soli. Prima Mailto.Parse era provata solo di passaggio dentro Annulla
    /// iscrizione, dove Cc e Ccn si tolgono prima: destinatari prima e dopo il punto di domanda, Cc, Ccn, oggetto e testo
    /// con la codifica %, il "+" che resta un "+", una % senza cifre, intestazioni che Missiva non usa.
    /// </summary>
    static void SuiMailto()
    {
        var full = Mailto.Parse("mailto:mario.rossi@example.com,%20maria.rossi@example.com?cc=ufficio@example.com&bcc=archivio@example.com" +
                                "&subject=Preventivo%20di%20ottobre%20%E2%82%AC&body=Prima%20riga%0D%0ASeconda%20riga&to=terzo@example.com");
        var plus = Mailto.Parse("mailto:mario+fatture@example.com?subject=1+1%3D2");
        var upper = Mailto.Parse("MAILTO:Mario.Rossi@Example.com?SUBJECT=Ciao&Body=x");
        var percent = Mailto.Parse("mailto:mario@example.com?subject=100%&body=50%25");
        var later = Mailto.Parse("mailto:?to=a@example.com%2Cb@example.com;c@example.com");
        var unknown = Mailto.Parse("mailto:a@example.com?in-reply-to=%3Cx@example.com%3E&subject=Re");
        var empty = Mailto.Parse("mailto:");
        bool whole = full.To.SequenceEqual(["mario.rossi@example.com", "maria.rossi@example.com", "terzo@example.com"]) && full.Cc.SequenceEqual(["ufficio@example.com"])
                     && full.Bcc.SequenceEqual(["archivio@example.com"]) && full.Subject == "Preventivo di ottobre €" && full.Body == "Prima riga\nSeconda riga";
        bool rest = plus is { To: ["mario+fatture@example.com"], Subject: "1+1=2" } && upper is { To: ["Mario.Rossi@Example.com"], Subject: "Ciao", Body: "x" }
                    && percent is { Subject: "100%", Body: "50%" } && later.To.SequenceEqual(["a@example.com", "b@example.com", "c@example.com"])
                    && unknown is { To: ["a@example.com"], Subject: "Re", Body: "", Cc.Count: 0 } && empty is { To.Count: 0, Cc.Count: 0, Bcc.Count: 0, Subject: "", Body: "" }
                    && Mailto.Is("MailTo:x@example.com") && !Mailto.Is("https://example.com/mailto:") && !Mailto.Is(null);
        Check(whole && rest,
              $"C-47, un link mailto: letto da solo: destinatari prima e dopo il punto di domanda ({string.Join(", ", full.To)}), Cc ({string.Join(", ", full.Cc)}), Ccn " +
              $"({string.Join(", ", full.Bcc)}), oggetto (\"{full.Subject}\") e testo su due righe; il \"+\" resta un \"+\" ({string.Join("", plus.To)}, \"{plus.Subject}\"), una % senza cifre " +
              $"resta com'è (\"{percent.Subject}\"), maiuscole, virgole codificate e intestazioni che Missiva non usa");
    }

    /// <summary>
    /// C-43: un comando che il server IMAP finto non conosce riceve BAD. Prima rispondeva OK a tutto quello che non
    /// conosceva, senza fare nulla: rinominare o eliminare una cartella sembrava riuscire, e una prova sulle cartelle
    /// sarebbe passata a vuoto. Fino alle scelte del 12 ottobre qui si provavano RENAME e DELETE, che il server non aveva: ora
    /// li fa davvero (le loro prove sono in Choices3Tests). Qui quello che continua a non fare: SUBSCRIBE, che non conosce
    /// (Missiva non lo manda mai: lo manda MailKit da solo), e l'eliminazione di una cartella che ne contiene un'altra.
    /// </summary>
    static async Task SuiUnknownCommandAsync()
    {
        using var fake = new FakeImap(new Flavor(Gmail: false));
        string inside = fake.UserFolder + "/Rossi";
        lock (fake.Folders) fake.Folders[inside] = new();
        bool plain = ImapProvider.TestPlain;
        ImapProvider.TestPlain = true;
        try
        {
            using var provider = new ImapProvider(SpeedAccount(fake, "mario@studio-prova.example"));
            IMailProvider imap = provider;
            var folders = await imap.GetFoldersAsync();
            var clienti = folders.First(f => f.Id == fake.UserFolder);
            async Task<string> Try(Func<Task> action)
            {
                try { await action(); return "riuscito"; }
                catch (Exception ex) { return ex.GetType().Name; }
            }
            string deleted = await Try(() => provider.DeleteFolderAsync(clienti));
            string subscribed;
            using (var client = new MailKit.Net.Imap.ImapClient())
            {
                await client.ConnectAsync("127.0.0.1", fake.Port, MailKit.Security.SecureSocketOptions.None);
                await client.AuthenticateAsync("mario@studio-prova.example", "x");
                subscribed = await Try(async () => await (await client.GetFolderAsync(fake.UserFolder)).SubscribeAsync());
                await client.DisconnectAsync(true);
            }
            List<string> sent;
            lock (fake.Traffic) sent = fake.Traffic.Select(t => t.Text).Where(c => c.StartsWith("SUBSCRIBE") || c.StartsWith("DELETE")).Select(c => c.Split(' ')[0]).ToList();
            var after = await imap.GetFoldersAsync();
            int errors;
            lock (ServerErrors) errors = ServerErrors.Count;
            Check(deleted != "riuscito" && subscribed != "riuscito" && sent.SequenceEqual(["DELETE", "SUBSCRIBE"]) && after.Any(f => f.Id == fake.UserFolder) && after.Any(f => f.Id == inside)
                  && fake.Folders.ContainsKey(fake.UserFolder) && errors == 0,
                  $"C-43, un comando che il server IMAP finto non ha riceve BAD: eliminare una cartella che ne contiene un'altra ({deleted}) e SUBSCRIBE ({subscribed}) danno un errore a chi li chiede, " +
                  "e le cartelle restano; prima il server rispondeva OK senza fare nulla e le azioni sembravano riuscite. Rinominare ed eliminare una cartella ora li fa davvero (scelte del 12 ottobre, C-47)");
        }
        finally
        {
            ImapProvider.TestPlain = plain;
        }
    }
}
#endif
