#if DEBUG
using System.Diagnostics;
using System.Globalization;

namespace Missiva;

/// <summary>
/// I gruppi della suite in una tabella sola (audit dell'11 ottobre, C-40): il nome con cui un gruppo si avvia da solo
/// ("Missiva.exe --prova-audit risultato.txt nome"), che cosa fa girare e se fa parte del giro degli audit
/// ("--prova-audit risultato.txt" senza nome). La suite intera ("--prova-bozze risultato.txt") li fa girare tutti,
/// nell'ordine della tabella. Ogni gruppo gira nel suo try: un errore ferma quel gruppo e non quelli dopo. Prima c'era
/// un try solo per tutta la suite, e un nome sconosciuto faceva girare altri gruppi con "tutto bene" alla fine.
/// Fra un gruppo e l'altro i ganci di prova tornano a riposo (C-44) e si guarda che cosa il gruppo ha lasciato acceso,
/// e se i server finti hanno sollevato errori che nessuno ha visto (C-45). I dati di un giro stanno in una cartella
/// nuova, che alla fine si cancella (C-49).
/// </summary>
static partial class ProvaBozze
{
    /// <param name="Name">Il nome per --prova-audit.</param>
    /// <param name="Audit">Fa parte del giro degli audit (--prova-audit senza nome).</param>
    /// <param name="Manual">Solo a mano, con il suo nome: mai dalla suite né dal giro degli audit (chiude il processo).</param>
    sealed record TestGroup(string Name, Action Run, bool Audit = false, bool Manual = false);

    static void Wait(Func<Task> work) => work().GetAwaiter().GetResult();

    /// <summary>
    /// Tutti i gruppi, nell'ordine in cui la suite li fa girare. Un gruppo nuovo va in fondo, con un nome senza accenti
    /// né spazi che non c'è già (lo controlla SuiteAuditTests).
    /// </summary>
    static readonly TestGroup[] Groups =
    [
        new("graph", () => Wait(GraphAsync)),
        new("gmail", () => Wait(GmailAsync)),
        new("annulla", () => Wait(UndoAsync)),
        new("account", () => Wait(AccountsAsync)),
        new("imap", () => Wait(ImapAsync)),
        new("gestisci", () => Wait(ManageAsync)),
        new("autoconfig", () => Wait(AutoconfigAsync)),
        new("rispondi-a", () => Wait(ReplyToAsync)),
        new("inviti", () => Wait(InvitesAsync)),
        new("opzioni-invio", () => Wait(SendOptionsAsync)),
        new("calendario", () => Wait(CalendarAsync)),
        new("calendario-scrive", () => Wait(CalendarWriteAsync)),
        new("calendario-modifica", () => Wait(CalendarEditAsync)),
        new("calendario-google", () => Wait(GoogleCalendarAsync)),
        new("contatti-google", () => Wait(GoogleContactsAsync)),
        new("contatti-google-scrive", () => Wait(GoogleContactsWriteAsync)), // unione, etag, una alla volta (ProvaBozze.ContattiGoogle.cs)
        new("risposte-automatiche", () => Wait(AutoRepliesAsync)),
        new("pec", () => Wait(PecTestsAsync)),
        new("allegati-pericolosi", DangerousTypes),
        new("loghi-mittenti", SenderLogoTests),
        new("impostazioni", StartupTests),
        new("identita", () => Wait(IdentitiesAsync)),
        new("condivise", () => Wait(SharedMailboxAsync)),
        new("contrassegni", () => Wait(FlagsAsync)),
        new("contatti", () => Wait(ContactsAsync)),
        new("regole", () => Wait(RulesAsync)),
        new("suggerimenti", () => Wait(PeopleSuggestAsync)),
        new("ricerca", () => Wait(SearchFiltersAsync)),
        new("idle", () => Wait(IdleTestsAsync)),
        new("coda", () => Wait(OfflineQueueAsync)),
        new("senza-rete", () => Wait(OfflineCopyTestsAsync)),
        new("account-scelto", ChosenAccount),
        new("non-in-linea", OfflineTests),
        new("pdf", () => Task.Run(PdfTestsAsync).GetAwaiter().GetResult()), // il motore PDF di Windows, fuori dal thread STA
        new("bozze-riprese", RoundTrip), // sul thread STA di Main: i documenti di WPF non si creano altrove
        new("notifiche", NotificationActions), // anche questa: la risposta veloce è un FlowDocument
        new("modelli", TemplatesTest), // e i modelli: RTF e FlowDocument
        new("formato", FormatRoundTrip), // e il formato del testo (giro P5)
        new("righe", UnifiedRows),
        new("accessibilita", AccessibilityTests),
        new("persone2", PeopleActionsTests), // Persone: chiamare, Google Contatti, sito, elenco a pixel (ProvaBozze.Persone2.cs)
        new("barra", SidebarMarksTests), // barra laterale: intestazione dell'account chiuso con la cartella aperta (ProvaBozze.BarraAccount.cs)
        new("persone3", PeopleSectionsTests), // Persone divise per account: sezioni, ricerca, filtro, 4000 voci (ProvaBozze.Persone3.cs)
        new("scheda-mittente", SenderCardHeaderTests), // intestazioni: foto e nome del mittente aprono la scheda (ProvaBozze.SchedaMittente.cs)
        new("contatti-google-sezioni", () => Wait(GoogleContactsFlowAsync)), // Google e Outlook: nuovo, modifica, elimina nelle sezioni (ProvaBozze.ContattiGoogle.cs)
        new("html", HtmlAuditTests, Audit: true), // audit, blocco 1: la pulizia dell'HTML con messaggi costruiti apposta (ProvaBozze.Audit.cs)
        new("loghi", LogoAuditTests, Audit: true), // audit, blocco 2: i loghi dei mittenti con SVG, immagini e domini costruiti apposta
        new("mittente", SenderAuditTests, Audit: true), // audit, blocco 3: chi scrive davvero, con intestazioni e mittenti costruiti apposta
        new("velocita", SpeedAuditTests, Audit: true), // audit, blocco 4: velocità di tutti i giorni, contando le operazioni sui server finti
        new("difetti", DefectAuditTests, Audit: true), // audit, blocco 5: i difetti veri del rapporto sulle funzionalità
        new("funzioni", FeatureAuditTests, Audit: true), // audit, blocco 6: allegato dimenticato, annulla iscrizione, allegati grandi con Microsoft
        new("copia", BackupAuditTests, Audit: true), // audit, blocco 7: copia di sicurezza, esporta e importa anche su un altro PC
        new("velocita2", Speed2AuditTests, Audit: true), // audit, blocco 8: salvataggi accorpati, copia dei corpi, Persone, suggerimenti
        new("velocita3", Speed3AuditTests, Audit: true), // audit, blocco 9: rete e disegno
        new("sicurezza", SecurityLowAuditTests, Audit: true), // audit, blocco 10: sicurezza, punti di gravità Bassa
        new("arrivi", ArrivalsAuditTests, Audit: true), // dopo l'installazione: non letti ed elenco dopo un arrivo, segnalazioni dell'autore del 7 ottobre
        new("avvio", StartupAuditTests, Audit: true), // audit del 7 ottobre, parte A: avvio e rete in ritardo all'accesso
        new("aperto", RunningAuditTests, Audit: true), // audit del 7 ottobre, parte B, primo gruppo: i gruppi dell'elenco al cambio di giorno
        new("dati", DataAuditTests, Audit: true), // audit del 7 ottobre, parte B, secondo gruppo: dati, rete, memoria
        new("copia2", Backup2AuditTests, Audit: true), // audit dell'11 ottobre, sicurezza, primo gruppo, T-14..T-22 (ProvaBozze.Copia2.cs)
        new("sicurezza2", Security2AuditTests, Audit: true), // audit dell'11 ottobre, sicurezza, secondo gruppo, T-01..T-10 (ProvaBozze.Sicurezza2.cs)
        new("sicurezza3", Security3AuditTests, Audit: true), // audit dell'11 ottobre, sicurezza, terzo gruppo, T-11..T-13 e T-23..T-36 (ProvaBozze.Sicurezza3.cs)
        new("comportamento", BehaviourAuditTests, Audit: true), // audit dell'11 ottobre, codice, primo gruppo, C-50..C-63 (ProvaBozze.Comportamento.cs)
        new("robustezza", RobustnessAuditTests, Audit: true), // audit dell'11 ottobre, codice, secondo gruppo, C-24..C-36, X-1 e X-2 (ProvaBozze.Robustezza.cs)
        new("prove", SuiteAuditTests, Audit: true), // audit dell'11 ottobre, codice, terzo gruppo, soglie e prove, C-38..C-49 (ProvaBozze.Prove.cs)
        new("residui", CleanupAuditTests, Audit: true), // audit dell'11 ottobre, codice, quarto gruppo, residui e doppioni, C-01..C-23 e C-64..C-66 (ProvaBozze.Residui.cs)
        new("scelte1", Choices1Tests, Audit: true), // scelte del 12 ottobre dopo gli audit dell'11, primo gruppo: lettura, account, copia di sicurezza (ProvaBozze.Scelte1.cs)
        new("scelte2", Choices2Tests, Audit: true), // scelte del 12 ottobre, secondo gruppo: promemoria, regole, finestre, ricerca, cartelle (ProvaBozze.Scelte2.cs)
        new("scelte3", Choices3Tests, Audit: true), // scelte del 12 ottobre, terzo gruppo: prove e guardie per chi sviluppa, TestingProvider, cartelle, non letti (ProvaBozze.Scelte3.cs)
        new("core", CoreReviewTests, Audit: true), // revisione del 13 ottobre: sei consigli sul modulo Core, K-01..K-06 (ProvaBozze.Core.cs)
        new("interfaccia", UiReviewTests, Audit: true), // revisione del 14 ottobre: cinque consigli sull'interfaccia, U-01..U-05 (ProvaBozze.Interfaccia.cs)
        new("aggiornamenti", UpdatesTests), // aggiornamenti da GitHub, 15 ottobre: firma, manifesto, rete, zip, sostituzione (ProvaBozze.Aggiornamenti.cs)
        new("caduta", RobCrash, Manual: true), // solo a mano: chiude il processo con un errore su un altro thread (C-24)
    ];

    /// <summary>
    /// I gruppi chiesti: tutti quelli degli audit senza nome, quello con quel nome altrimenti; null se nessun gruppo si
    /// chiama così (prima un nome sbagliato, per esempio "velocità" con l'accento, faceva girare i blocchi 4 e 5).
    /// </summary>
    static List<TestGroup>? Pick(string? only)
    {
        if (only == null) return Groups.Where(g => g.Audit && !g.Manual).ToList();
        var named = Groups.Where(g => g.Name == only).ToList();
        return named.Count == 0 ? null : named;
    }

    /// <summary>"--prova-bozze risultato.txt": tutti i gruppi; con "inverso" dall'ultimo al primo (C-44: l'esito non deve dipendere dall'ordine).</summary>
    public static void Run(string output, string? order = null)
    {
        var groups = Groups.Where(g => !g.Manual).ToList();
        if (order == "inverso") groups.Reverse();
        Finish(output, order is null or "inverso" ? groups : null, $"l'ordine \"{order}\" non c'è: dopo il file del risultato niente, oppure inverso");
    }

    /// <summary>"--prova-audit risultato.txt [nome]": i gruppi degli audit, o il solo gruppo con quel nome.</summary>
    public static void RunAudit(string output, string? only = null) =>
        Finish(output, Pick(only), $"nessun gruppo si chiama \"{only}\". I nomi: {string.Join(", ", Groups.Select(g => g.Name))}");

    /// <summary>Fa girare i gruppi (null: richiesta sbagliata, che è un errore) e scrive il file del risultato.</summary>
    static void Finish(string output, List<TestGroup>? groups, string wrong)
    {
        // "caduta" resta sulla cartella data: chiude il processo, e la sua riga si guarda nel registro di quella cartella.
        bool fresh = groups != null && !groups.Any(g => g.Manual);
        try
        {
            if (groups == null)
            {
                failures++;
                Report.Add("ERRORE: " + wrong);
            }
            else if (!fresh && Environment.GetEnvironmentVariable("MISSIVA_DATA") is not { Length: > 0 })
            {
                failures++;
                Report.Add("ERRORE: questo gruppo lavora nella cartella data con MISSIVA_DATA, che manca: senza userebbe quella della Missiva installata");
            }
            else
            {
                if (fresh) FreshData();
                Log.WatchUnhandled(); // come Program.Main: i lavori in sottofondo falliti durante le prove si contano (RobUnobservedMeasure)
                var times = RunGroups(groups);
                Report.Add("--- Gruppi di questo giro: controlli e secondi (il nome è quello di --prova-audit)");
                foreach (var (name, count, seconds) in times) Report.Add($"      {name}: {count} controlli, {seconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
                Report.Add($"      in tutto: {times.Sum(t => t.Checks)} controlli, {times.Sum(t => t.Seconds).ToString("0.0", CultureInfo.InvariantCulture)} s");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Report.Add("ERRORE: " + ex);
        }
        // I dati di un giro riuscito si cancellano; se un controllo non è passato restano, con il registro, e il risultato dice dove.
        bool keep = fresh && runData.Length > 0 && (failures != 0 || Environment.GetEnvironmentVariable("MISSIVA_PROVA_TIENI") == "1");
        if (keep) Report.Add($"      (i dati e il registro di questo giro restano in {runData}; fra un giorno li toglie il primo giro che parte)");
        Report.Add(failures == 0 ? "tutto bene" : $"{failures} controlli non passati");
        File.WriteAllLines(output, Report);
        Environment.ExitCode = failures == 0 ? 0 : 1;
        if (fresh && !keep && runData.Length > 0) DeleteQuietly(runData);
    }

    /// <summary>
    /// I gruppi uno dopo l'altro, ognuno nel suo try: un errore conta come un controllo non passato, lascia la sua riga
    /// "ERRORE:" con il nome del gruppo e non ferma i gruppi dopo. Prima e dopo ogni gruppo i ganci tornano a riposo.
    /// </summary>
    static List<(string Name, int Checks, double Seconds)> RunGroups(IReadOnlyList<TestGroup> groups)
    {
        var times = new List<(string, int, double)>();
        foreach (var group in groups)
        {
            RestHooks();
            int before = checks;
            var clock = Stopwatch.StartNew();
            try
            {
                group.Run();
            }
            catch (Exception ex)
            {
                failures++;
                Report.Add($"ERRORE: il gruppo \"{group.Name}\" si è fermato qui (i gruppi dopo girano lo stesso): {ex}");
            }
            double seconds = clock.Elapsed.TotalSeconds;
            int own = checks - before;
            LeftBehind(group.Name);
            times.Add((group.Name, own, seconds));
        }
        RestHooks();
        return times;
    }

    // ---------------------------------------------------------------- dati di un giro (C-49)

    /// <summary>La cartella data con MISSIVA_DATA (o una temporanea, se manca), e dentro quella nuova di questo giro.</summary>
    static string suiteRoot = "", runData = "";

    /// <summary>
    /// Ogni giro lavora in una cartella dei dati nuova e vuota, "giro-..." dentro quella di MISSIVA_DATA: rubrica, registro,
    /// impostazioni e contatti di un giro non arrivano a quello dopo (prima restavano, e un controllo poteva passare per
    /// una riga del registro o una voce della rubrica scritta giorni prima). Senza MISSIVA_DATA la cartella sta fra i file
    /// temporanei: la suite non scrive mai nella cartella della Missiva installata. Va chiamata prima di toccare Store,
    /// che legge MISSIVA_DATA una volta sola. A giro riuscito la cartella si cancella; se un controllo non passa, o con
    /// MISSIVA_PROVA_TIENI=1, resta (con il registro) e il risultato dice dove.
    /// </summary>
    static void FreshData()
    {
        suiteRoot = Environment.GetEnvironmentVariable("MISSIVA_DATA") is { Length: > 0 } given ? given : Path.Combine(Path.GetTempPath(), "missiva-prova-suite");
        Directory.CreateDirectory(suiteRoot);
        // Le cartelle dei giri finiti male restano per guardarle: dopo un giorno si tolgono.
        foreach (var old in Directory.GetDirectories(suiteRoot, "giro-*"))
            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(old) > TimeSpan.FromDays(1)) DeleteQuietly(old);
        runData = Path.Combine(suiteRoot, $"giro-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
        Directory.CreateDirectory(runData);
        Environment.SetEnvironmentVariable("MISSIVA_DATA", runData);
        if (!string.Equals(DataDir(), runData, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"la cartella dei dati era già stata letta ({DataDir()}): il giro non parte da una cartella vuota");
    }

    /// <summary>Store.Dir, letta da un metodo a parte: FreshData non deve nominare Store prima di aver dato la cartella.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static string DataDir() => Store.Dir;

    /// <summary>
    /// Dove stanno i profili di WebView2 delle prove (WebView2-prove, WebView2-prove-finestre): nella cartella data, non
    /// in quella del giro. Non sono dati di Missiva, e rifarli a ogni giro costa secondi; i processi del motore li tengono
    /// aperti ancora un momento dopo la fine.
    /// </summary>
    static string EngineRoot => suiteRoot.Length > 0 ? suiteRoot : Store.Dir;

    static void DeleteQuietly(string dir)
    {
        try { Directory.Delete(dir, true); } catch (Exception) { }
    }
}
#endif
