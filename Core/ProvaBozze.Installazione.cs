#if DEBUG
using Microsoft.Win32;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Missiva che si installa da sola (12 novembre 2026, UI/SelfInstall.cs): quando si offre, l'installazione vera in una
/// cartella di prova con il menu Start di prova e le chiavi sotto una chiave di prova del registro (mai la Missiva
/// installata, mai HKCU\Software vero: nella shell di Claude è comunque una copia), le stesse chiavi di installa.ps1, i
/// file che l'eseguibile porta dentro di sé, e nei sorgenti l'aggancio in Program.Main. Le finestre non si aprono.
/// Nessuna rete. Con "Missiva.exe --prova-audit risultato.txt installazione" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void SelfInstallTests()
    {
        Report.Add("--- Missiva che si installa da sola (12 novembre 2026)");
        SiOffers();
        SiQuestion();
        SiInstall();
        SiFlow();
        SiSources();
    }

    /// <summary>
    /// Il percorso di SelfInstall.Run con le finestre sostituite: Installa installa e apre quella installata; Annulla non
    /// tocca nulla; con la stessa versione già installata Apri Missiva la apre senza installare e Installa di nuovo la
    /// installa; un errore lo dice e non apre nulla; se l'apertura non riesce, lo dice ma l'installazione resta.
    /// </summary>
    static void SiFlow()
    {
        var work = Path.Combine(Path.GetTempPath(), "missiva-prova-installazione-" + Guid.NewGuid().ToString("N")[..8]);
        var keyPath = @"Software\Missiva-prova-installazione\" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Directory.CreateDirectory(work);
            var source = Path.Combine(work, "Missiva.exe");
            File.Copy(Environment.ProcessPath!, source);
            using var root = Registry.CurrentUser.CreateSubKey(keyPath);
            string start = Path.Combine(work, "Menu Start");
            var asked = new List<SelfInstall.Prompt>();
            var shown = new List<string>();
            var opened = new List<string>();
            string? Sync(Func<IProgress<string>, string?> job) => job(new SiSteps());
            int Go(string folder, Choice answer, Action<string>? launch = null) =>
                SelfInstall.Flow(source, folder, root, start, p => { asked.Add(p); return answer; }, Sync, (h, b) => shown.Add(h + " | " + b), launch ?? opened.Add);

            string cancelled = Path.Combine(work, "Annullata");
            int no = Go(cancelled, Choice.Cancel);
            bool cancel = no == 2 && !Directory.Exists(cancelled) && opened.Count == 0 && asked.Count == 1 && asked[0].Heading == "Installare Missiva?";

            string folder = Path.Combine(work, @"Programs\Missiva");
            string installed = Path.Combine(folder, "Missiva.exe"), license = Path.Combine(folder, "LICENSE");
            int yes = Go(folder, Choice.Primary);
            bool done = yes == 0 && File.Exists(installed) && File.Exists(license) && opened.SequenceEqual([installed]) && shown.Count == 0
                        && asked[1].Body.Contains("Missiva " + SelfInstall.VersionOf(source) + " si installa");

            // Di nuovo sulla stessa cartella: la domanda dice che c'è già, e Apri Missiva la apre senza installare (LICENSE,
            // tolto qui, resta tolto); Installa di nuovo la installa (LICENSE torna).
            File.Delete(license);
            int open = Go(folder, Choice.Primary);
            bool opensOnly = open == 0 && asked[2].Heading == "Missiva è già installata" && asked[2].OpensInstalled && !File.Exists(license) && opened.Count == 2 && opened[1] == installed;
            int again = Go(folder, Choice.Secondary);
            bool reinstalled = again == 0 && File.Exists(license) && opened.Count == 3 && shown.Count == 0;

            // Un file al posto della cartella di installazione: l'errore si dice, niente si apre.
            string blocked = Path.Combine(work, "Bloccata");
            File.WriteAllText(blocked, "un file, non una cartella");
            int failed = Go(Path.Combine(blocked, "Missiva"), Choice.Primary);
            bool error = failed == 1 && opened.Count == 3 && shown.Count == 1 && shown[0].StartsWith("Missiva non è stata installata");

            // L'apertura che non riesce: installata lo stesso, e lo dice.
            int unopened = Go(folder, Choice.Secondary, _ => throw new System.ComponentModel.Win32Exception(2));
            bool start2 = unopened == 0 && shown.Count == 2 && shown[1].StartsWith("Missiva è installata") && shown[1].Contains("menu Start");
            Check(cancel && done && opensOnly && reinstalled && error && start2,
                  $"installazione da sola, il percorso con le finestre sostituite: Annulla non tocca nulla ({cancel}); Installa installa e apre quella installata ({done}); di nuovo, " +
                  $"Apri Missiva la apre senza installare ({opensOnly}) e Installa di nuovo la installa ({reinstalled}); un errore lo dice e non apre nulla ({error}); l'apertura che " +
                  $"non riesce lo dice e l'installazione resta ({start2})");
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Missiva-prova-installazione", throwOnMissingSubKey: false); } catch (Exception) { }
            try { Directory.Delete(work, true); } catch (Exception) { }
        }
    }

    /// <summary>Si offre solo avviata senza argomenti da fuori della cartella di installazione.</summary>
    static void SiOffers()
    {
        const string folder = @"C:\Users\prova\AppData\Local\Programs\Missiva";
        bool download = SelfInstall.Offers(@"C:\Users\prova\Downloads\Missiva-0.3.700\Missiva.exe", [], folder);
        bool renamed = SelfInstall.Offers(@"C:\Users\prova\Downloads\Missiva (1).exe", [], folder);
        bool installed = SelfInstall.Offers(@"c:\users\PROVA\appdata\local\programs\missiva\Missiva.exe", [], folder + @"\");
        string[][] tools = [["--versione"], ["--installa-aggiornamento", "12", folder + @"\Missiva.exe"], ["--pdf-worker"], ["--mailto", "mailto:a@b.example"], ["--nascosta"], ["-ToastActivated"]];
        bool quiet = tools.All(a => !SelfInstall.Offers(@"C:\Users\prova\Downloads\Missiva.exe", a, folder)) && !SelfInstall.Offers(null, [], folder);
        Check(download && renamed && !installed && quiet,
              $"installazione da sola: si offre con un doppio clic sull'eseguibile fuori dalla cartella di installazione ({download}), anche rinominato ({renamed}); " +
              $"non da quella cartella, scritta in un altro modo ({!installed}), né con gli argomenti degli strumenti, dei link mailto:, dell'avvio con Windows e delle notifiche ({quiet})");
    }

    /// <summary>
    /// La domanda dice che cosa fa, quale versione c'era e che quella aperta si chiude; con la stessa versione offre di
    /// aprirla, con una più nuova installata lo dice nel titolo; nelle due lingue.
    /// </summary>
    static void SiQuestion()
    {
        var fresh = SelfInstall.Question("0.3.700", null, false);
        var over = SelfInstall.Question("0.3.700", "0.3.690", true);
        bool it = fresh.Heading == "Installare Missiva?" && fresh.Primary == "Installa" && fresh.Secondary == null && !fresh.OpensInstalled
                  && fresh.Body.Contains("Missiva 0.3.700 si installa per il tuo utente") && fresh.Body.Contains("Non servono permessi di amministratore") && !fresh.Body.Contains("Sul PC")
                  && over.Heading == "Installare Missiva?" && over.Body.Contains("Sul PC c'è Missiva 0.3.690: la sostituisce.") && over.Body.Contains("Missiva è aperta: si chiude da sola");
        var back = SelfInstall.Question("0.3.690", "0.3.700", false);
        bool older = back.Heading == "Installare una versione precedente?" && back.Primary == "Installa comunque" && !back.OpensInstalled
                     && back.Body.Contains("Sul PC c'è Missiva 0.3.700, più nuova di questa: la sostituisce con la 0.3.690, più vecchia.");
        bool numbers = SelfInstall.Question("0.3.10", "0.3.9", false).Heading == "Installare Missiva?" && SelfInstall.Question("0.3.9", "0.3.10", false).Heading == "Installare una versione precedente?";
        var same = SelfInstall.Question("0.3.700", "0.3.700", true);
        bool again = same.Heading == "Missiva è già installata" && same.Primary == "Apri Missiva" && same.Secondary == "Installa di nuovo" && same.OpensInstalled
                     && same.Body.Contains("Missiva 0.3.700 è già installata su questo PC") && same.Body.Contains("installandola di nuovo si chiude da sola");
        SelfInstall.Prompt e1, e2, e3;
        try
        {
            Lingua.Force("en");
            e1 = SelfInstall.Question("0.3.700", "0.3.690", true);
            e2 = SelfInstall.Question("0.3.690", "0.3.700", false);
            e3 = SelfInstall.Question("0.3.700", "0.3.700", false);
        }
        finally
        {
            Lingua.Force("it");
        }
        bool en = e1.Heading == "Install Missiva?" && e1.Primary == "Install" && e1.Body.Contains("Missiva 0.3.700 will be installed for your user") && e1.Body.Contains("This PC has Missiva 0.3.690")
                  && e1.Body.Contains("Missiva is open") && e2.Heading == "Install an older version?" && e2.Primary == "Install anyway" && e3.Primary == "Open Missiva" && e3.Secondary == "Install again";
        Check(it && older && numbers && again && en,
              $"installazione da sola, la domanda: che cosa fa, la versione che c'era, la Missiva aperta che si chiude ({it}); una più nuova installata lo dice nel titolo ({older}), " +
              $"con i numeri confrontati come numeri ({numbers}); la stessa offre Apri Missiva e Installa di nuovo ({again}); in inglese ({en})");
    }

    /// <summary>
    /// L'installazione vera, con il Missiva.exe di questa prova copiato da solo in una cartella (come uno scaricato senza lo
    /// zip), verso una cartella di installazione di prova: l'eseguibile, i tre file accanto presi da dentro l'eseguibile, il
    /// collegamento, la voce di App installate e Missiva fra le app di posta, come installa.ps1; RegisteredApplications non
    /// si svuota. Poi di nuovo da uno "zip" con i suoi file accanto: l'eseguibile e quei file sostituiscono quelli di prima.
    /// </summary>
    static void SiInstall()
    {
        var work = Path.Combine(Path.GetTempPath(), "missiva-prova-installazione-" + Guid.NewGuid().ToString("N")[..8]);
        var keyPath = @"Software\Missiva-prova-installazione\" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var download = Path.Combine(work, "Scaricati");
            Directory.CreateDirectory(download);
            var source = Path.Combine(download, "Missiva.exe");
            File.Copy(Environment.ProcessPath!, source);
            string folder = Path.Combine(work, @"Programs\Missiva"), start = Path.Combine(work, "Menu Start");
            using var root = Registry.CurrentUser.CreateSubKey(keyPath);
            using (var others = root.CreateSubKey(@"Software\RegisteredApplications")) others.SetValue("AltraApp", @"Software\AltraApp\Capabilities");
            var steps = new SiSteps();
            var error = SelfInstall.Install(source, folder, root, start, steps);
            var installed = Path.Combine(folder, "Missiva.exe");
            bool files = error == null && File.Exists(installed) && File.ReadAllBytes(installed).AsSpan().SequenceEqual(File.ReadAllBytes(source))
                         && Updates.BesideFiles.All(n => File.Exists(Path.Combine(folder, n)) && new FileInfo(Path.Combine(folder, n)).Length > 100);
            // I file accanto presi da dentro l'eseguibile sono quelli del repository.
            bool same = SourceRoot() is not { } repo || Updates.BesideFiles.All(n => File.ReadAllBytes(Path.Combine(folder, n)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(repo, n))));
            bool link = File.Exists(Path.Combine(start, "Missiva.lnk")) && new FileInfo(Path.Combine(start, "Missiva.lnk")).Length > 0;
            using var entry = root.OpenSubKey(Updates.UninstallKey);
            using var command = root.OpenSubKey(@"Software\Classes\Missiva.Mailto\shell\open\command");
            using var urls = root.OpenSubKey(@"Software\Missiva\Capabilities\URLAssociations");
            using var registered = root.OpenSubKey(@"Software\RegisteredApplications");
            bool keys = entry?.GetValue("DisplayName") as string == "Missiva" && entry.GetValue("DisplayVersion") as string == SelfInstall.VersionOf(source)
                        && entry.GetValue("InstallLocation") as string == folder && entry.GetValue("DisplayIcon") as string == installed + ",0"
                        && entry.GetValue("UninstallString") is string uninstall && uninstall.Contains(@"WindowsPowerShell\v1.0\powershell.exe") && uninstall.EndsWith($"-File \"{Path.Combine(folder, "disinstalla.ps1")}\"")
                        && entry.GetValue("NoModify") is 1 && entry.GetValue("EstimatedSize") is int size && size > 0
                        && command?.GetValue("") as string == $"\"{installed}\" --mailto \"%1\"" && urls?.GetValue("mailto") as string == "Missiva.Mailto"
                        && registered?.GetValue("Missiva") as string == @"Software\Missiva\Capabilities" && registered.GetValue("AltraApp") is string;
            // Di nuovo, da uno "zip" con i suoi file accanto all'eseguibile: quelli sostituiscono i precedenti.
            var zip = Path.Combine(work, "Zip");
            Directory.CreateDirectory(zip);
            File.Copy(Environment.ProcessPath!, Path.Combine(zip, "Missiva.exe"));
            foreach (var n in Updates.BesideFiles) File.WriteAllText(Path.Combine(zip, n), "dallo zip " + n);
            var again = SelfInstall.Install(Path.Combine(zip, "Missiva.exe"), folder, root, start, null);
            bool replaced = again == null && Updates.BesideFiles.All(n => File.ReadAllText(Path.Combine(folder, n)) == "dallo zip " + n);
            bool said = steps.Said.Any(s => s.StartsWith("Copio Missiva")) && steps.Said.Any(s => s.StartsWith("Registro Missiva"));
            Check(files && same && link && keys && replaced && said,
                  $"installazione da sola, vera in una cartella di prova: l'eseguibile copiato e i tre file accanto presi da dentro di lui, uguali a quelli del repository ({files}, {same}); " +
                  $"il collegamento ({link}); App installate, Missiva fra le app di posta con --mailto e RegisteredApplications senza togliere le altre app ({keys}); di nuovo da uno zip, " +
                  $"i suoi file al posto di quelli di prima ({replaced}); i passi detti nella finestra ({said}){(error != null ? ": " + error : "")}");
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Missiva-prova-installazione", throwOnMissingSubKey: false); } catch (Exception) { }
            try { Directory.Delete(work, true); } catch (Exception) { }
        }
    }

    /// <summary>I passi detti alla finestra, subito (Progress&lt;T&gt; li manderebbe al pool, dopo).</summary>
    sealed class SiSteps : IProgress<string>
    {
        public List<string> Said { get; } = new();
        public void Report(string value) => Said.Add(value);
    }

    /// <summary>
    /// Nei sorgenti: l'aggancio in Program.Main solo nella versione che si installa, prima del mutex; SelfInstall.Run
    /// installa nella cartella di sempre, con HKCU e il menu Start veri, e apre quella installata senza la shell; le chiavi
    /// e i valori sono quelli di installa.ps1, che resta nello zip.
    /// </summary>
    static void SiSources()
    {
        if (Ch1Code("Program.cs") is not { } program || Ch1Code("UI", "SelfInstall.cs") is not { } install || SourceRoot() is not { } root)
        {
            Report.Add("      (sorgenti non trovati accanto all'eseguibile: l'installazione da sola nei sorgenti non è stata letta)");
            return;
        }
        var main = Ch3Part(program, "static void Main(string[] args)", "internal const string Build");
        bool hook = Ch3InOrder(main, "PdfWorker.Serve()", "#if !DEBUG && !PROVA", "if (UI.SelfInstall.Offers(Environment.ProcessPath, args, Updates.InstallFolder))",
                               "Environment.ExitCode = UI.SelfInstall.Run(Environment.ProcessPath!, args);", "#endif", "#if DEBUG || PROVA", "new Mutex(");
        string run = Ch3Part(install, "internal static int Run(", "internal sealed record Prompt(");
        bool real = run.Contains("var folder = Updates.InstallFolder;") && run.Contains("Flow(exe, folder, Registry.CurrentUser, Environment.GetFolderPath(Environment.SpecialFolder.Programs),")
                    && run.Contains("Process.Start(new ProcessStartInfo(installed) { UseShellExecute = false, WorkingDirectory = folder })")
                    && run.Contains("var error = work(progress => Install(exe, folder, root, startMenu, progress));")
                    && run.Contains("Dialog.AskAlone(") && run.Contains("Dialog.ShowAlone,") && run.Contains("if (prompt.OpensInstalled && choice == Choice.Primary)");
        string script = File.ReadAllText(Path.Combine(root, "installa.ps1"));
        string[] both = ["Software\\Classes\\Missiva.Mailto", "Software\\Missiva\\Capabilities", "URLAssociations", "Software\\RegisteredApplications", "\"URL Protocol\"", "DefaultIcon", "shell\\open\\command",
                         "--mailto", "ApplicationName", "ApplicationDescription", "ApplicationIcon", "Posta, senza il nuovo Outlook", "Link di posta (mailto:)", "-NoProfile -ExecutionPolicy Bypass -File"];
        string[] values = ["DisplayName", "DisplayVersion", "Publisher", "DisplayIcon", "InstallLocation", "EstimatedSize", "NoModify", "NoRepair", "UninstallString"];
        var missing = both.Where(s => !script.Contains(s) || !install.Contains(s)).Concat(values.Where(v => !script.Contains(v) || !install.Contains($"\"{v}\""))).ToList();
        Check(hook && real && missing.Count == 0,
              $"installazione da sola nei sorgenti: l'aggancio in Program.Main solo nella versione che si installa, prima del mutex ({hook}); la cartella, HKCU e il menu Start veri, le " +
              $"domande con il loro pulsante nella barra delle applicazioni e Missiva installata aperta senza la shell ({real}); le chiavi e i valori di installa.ps1 ({(missing.Count == 0 ? "tutti" : "mancano " + string.Join(", ", missing))})");
    }
}
#endif
