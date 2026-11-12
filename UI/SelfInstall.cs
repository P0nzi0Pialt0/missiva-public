using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Missiva.UI;

/// <summary>
/// Missiva che si installa da sola (12 novembre 2026, l'autore: "perché il file di Missiva non è un exe, ma va installato
/// da terminale?"). Un doppio clic su Missiva.exe fuori dalla cartella di installazione (lo zip di un rilascio estratto, lo
/// scaricato) chiede "Installare Missiva?" e fa quello che fa installa.ps1 da un rilascio: chiude la Missiva installata se
/// è aperta (con "esci" sul suo canale, come Esci, poi come la X, poi terminandola), si copia nella cartella di
/// installazione (Updates.Replace, lo stesso scambio degli aggiornamenti), mette accanto disinstalla.ps1, LICENSE e
/// TERZE-PARTI.md (quelli dello zip, o quelli che porta dentro di sé), crea il collegamento nel menu Start, la voce in App
/// installate e la registrazione fra le app di posta per i link mailto:, come installa.ps1, e apre quella installata.
/// Impostazioni e account non si toccano. Solo nella versione che si installa (Program.Main), e solo avviata senza
/// argomenti: gli strumenti (--versione, --installa-aggiornamento, --pdf-worker) e i link mailto: non chiedono nulla.
/// installa.ps1 resta nello zip e fa lo stesso. Il primo doppio clic su un eseguibile scaricato da Internet passa da
/// SmartScreen ("Windows ha protetto il PC", poi Ulteriori informazioni, Esegui comunque): l'eseguibile non è firmato.
/// </summary>
public static class SelfInstall
{
    /// <summary>
    /// Si offre di installarsi: avviata senza argomenti (un doppio clic, il menu Start di un collegamento fatto a mano) da
    /// un eseguibile che non sta nella cartella di installazione.
    /// </summary>
    internal static bool Offers(string? exe, IReadOnlyList<string> args, string installFolder) =>
        exe != null && args.Count == 0 && !string.Equals(Folder(Path.GetDirectoryName(exe)!), Folder(installFolder), StringComparison.OrdinalIgnoreCase);

    static string Folder(string path) => Path.GetFullPath(path).TrimEnd('\\');

    /// <summary>Le domande, l'installazione e l'apertura di quella installata. Il codice di uscita: 0 installata, 1 non riuscita, 2 annullata.</summary>
    internal static int Run(string exe, IReadOnlyList<string> args)
    {
        Lingua.Start(null, args); // come Windows: le impostazioni di Missiva non si leggono
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
        var folder = Updates.InstallFolder;
        int result = Flow(exe, folder, Registry.CurrentUser, Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            prompt => Dialog.AskAlone(prompt.Heading, prompt.Body, prompt.Primary, prompt.Secondary), WithProgress, Dialog.ShowAlone,
            installed => Process.Start(new ProcessStartInfo(installed) { UseShellExecute = false, WorkingDirectory = folder })?.Dispose());
        GC.KeepAlive(app);
        return result;
    }

    /// <summary>
    /// Il percorso, con le finestre passate da Run (le prove le sostituiscono): la domanda (ask, con la scelta fatta),
    /// l'installazione (work, che riceve il lavoro da fare con il suo avanzamento), un avviso (show), l'apertura di quella
    /// installata (launch). Il codice di uscita: 0 installata (o, con la stessa versione già installata, aperta e basta),
    /// 1 non riuscita, 2 annullata.
    /// </summary>
    internal static int Flow(string exe, string folder, RegistryKey root, string startMenu, Func<Prompt, Choice> ask,
                             Func<Func<IProgress<string>, string?>, string?> work, Action<string, string> show, Action<string> launch)
    {
        var installed = Path.Combine(folder, "Missiva.exe");
        string? before = File.Exists(installed) ? VersionOf(installed) : null;
        var prompt = Question(VersionOf(exe), before, Running(installed).Count > 0);
        var choice = ask(prompt);
        if (choice == Choice.Cancel) return 2;
        if (prompt.OpensInstalled && choice == Choice.Primary)
        {
            Log.Write($"installazione: Missiva {before} è già installata, apro quella");
            Open(installed, launch, show);
            return 0;
        }
        Log.Write($"installazione: Missiva {VersionOf(exe)} dall'eseguibile avviato fuori dalla cartella di installazione" + (before != null ? $", al posto della {before}" : ""));
        var error = work(progress => Install(exe, folder, root, startMenu, progress));
        if (error != null)
        {
            Log.Write($"installazione non riuscita: {error}");
            show(T("Missiva non è stata installata"), error);
            return 1;
        }
        Log.Write($"installazione: Missiva {VersionOf(exe)} installata, la apro");
        Open(installed, launch, show);
        return 0;
    }

    static void Open(string installed, Action<string> launch, Action<string, string> show)
    {
        try
        {
            launch(installed);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Write($"installazione: Missiva installata non si è aperta ({ex.Message})");
            show(T("Missiva è installata"), T("Non si è aperta da sola: la trovi nel menu Start."));
        }
    }

    /// <summary>La finestra dell'installazione in corso: il passo del momento e una barra che gira; a metà non si chiude.</summary>
    static string? WithProgress(Func<IProgress<string>, string?> job)
    {
        string? error = null;
        bool finished = false;
        var status = new TextBlock { Text = T("Installo Missiva..."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        status.SetResourceReference(Control.ForegroundProperty, "TextFillColorSecondaryBrush");
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        panel.Children.Add(new TextBlock { Text = T("Installazione di Missiva"), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(status);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4 });
        var window = new Window
        {
            Title = "Missiva", Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel,
        };
        window.Closing += (_, e) => e.Cancel = !finished;
        window.Loaded += async (_, _) =>
        {
            var progress = new Progress<string>(text => status.Text = text); // creato qui: i passi tornano sul thread della finestra
            error = await Task.Run(() => job(progress));
            finished = true;
            window.Close();
        };
        window.ShowDialog();
        return error;
    }

    /// <summary>La domanda: titolo, testo, il pulsante principale e un secondo (o null), e se il principale apre quella installata.</summary>
    internal sealed record Prompt(string Heading, string Body, string Primary, string? Secondary, bool OpensInstalled);

    /// <summary>
    /// La domanda: che cosa fa, quale versione c'era, e che quella aperta si chiude. Con la stessa versione già installata
    /// il pulsante principale apre quella (chi fa di nuovo doppio clic sullo scaricato di solito vuole aprirla) e il secondo
    /// la installa di nuovo; con una più nuova installata lo dice già nel titolo, perché un eseguibile vecchio rimasto nei
    /// Download non la riporti indietro senza che ci si accorga.
    /// </summary>
    internal static Prompt Question(string version, string? before, bool open)
    {
        if (before == version)
        {
            var same = new StringBuilder(T("Missiva {0} è già installata su questo PC. Puoi aprirla, oppure installarla di nuovo: rimette il collegamento nel menu Start e la voce in App installate, e impostazioni e account restano come sono.", version));
            if (open) same.Append("\n\n").Append(T("Missiva è aperta: installandola di nuovo si chiude da sola (se stai scrivendo un messaggio, ti chiede che cosa fare della bozza) e si riapre."));
            return new(T("Missiva è già installata"), same.ToString(), T("Apri Missiva"), T("Installa di nuovo"), true);
        }
        bool older = before != null && Version.TryParse(before, out var had) && Version.TryParse(version, out var mine) && had > mine;
        var text = new StringBuilder(T("Missiva {0} si installa per il tuo utente, con il collegamento nel menu Start e la voce in App installate. Non servono permessi di amministratore, e impostazioni e account restano come sono.", version));
        if (before != null) text.Append("\n\n").Append(older ? T("Sul PC c'è Missiva {0}, più nuova di questa: la sostituisce con la {1}, più vecchia.", before, version) : T("Sul PC c'è Missiva {0}: la sostituisce.", before));
        if (open) text.Append("\n\n").Append(T("Missiva è aperta: si chiude da sola (se stai scrivendo un messaggio, ti chiede che cosa fare della bozza) e si riapre installata."));
        return older ? new(T("Installare una versione precedente?"), text.ToString(), T("Installa comunque"), null, false)
                     : new(T("Installare Missiva?"), text.ToString(), T("Installa"), null, false);
    }

    /// <summary>Il numero della versione di un eseguibile di Missiva (0.3.N), dalle proprietà del file.</summary>
    internal static string VersionOf(string exe)
    {
        var info = FileVersionInfo.GetVersionInfo(exe);
        return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
    }

    /// <summary>
    /// L'installazione, come installa.ps1 da un rilascio. folder è la cartella di installazione, root la chiave del
    /// registro sotto cui stanno Software\... (HKCU, o una chiave di prova), startMenu la cartella dei programmi del menu
    /// Start. Null se è riuscita, altrimenti il motivo da mostrare.
    /// </summary>
    internal static string? Install(string source, string folder, RegistryKey root, string startMenu, IProgress<string>? progress)
    {
        try
        {
            var installed = Path.Combine(folder, "Missiva.exe");
            if (Running(installed).Count > 0)
            {
                progress?.Report(T("Chiudo Missiva per aggiornarla..."));
                CloseRunning(installed);
            }
            progress?.Report(T("Copio Missiva nella cartella dei programmi..."));
            Directory.CreateDirectory(folder);
            var copied = Updates.Replace(source, installed, 40, TimeSpan.FromMilliseconds(500));
            if (!copied.Done) return T("Non riesco a sostituire Missiva: il file è ancora in uso. Chiudi Missiva e riprova.");
            // Accanto, quelli dello zip (Replace li ha copiati); se l'eseguibile è da solo, quelli che porta dentro di sé.
            foreach (var name in Updates.BesideFiles)
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(source)!, name))) WriteInside(name, folder);
            progress?.Report(T("Registro Missiva in Windows..."));
            Shortcut(Path.Combine(startMenu, "Missiva.lnk"), installed, folder);
            Register(root, installed, folder, VersionOf(installed));
            return null;
        }
        catch (Exception ex)
        {
            // Qualunque errore (file, registro, collegamento): l'installazione si ferma e lo dice, senza chiudere tutto.
            Log.Error("installazione", ex);
            return ex.Message;
        }
    }

    /// <summary>Le Missiva aperte da quell'eseguibile (quella installata), non questa né quelle di altre cartelle.</summary>
    static List<Process> Running(string installed)
    {
        var found = new List<Process>();
        foreach (var process in Process.GetProcessesByName("Missiva"))
        {
            bool same = false;
            try { same = process.Id != Environment.ProcessId && string.Equals(process.MainModule?.FileName, installed, StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { } // di un altro utente, o già uscita
            if (same) found.Add(process);
            else process.Dispose();
        }
        return found;
    }

    /// <summary>
    /// Come installa.ps1: "esci" sul canale con nome della Missiva aperta (App.ExitAll, con le domande delle scritture
    /// aperte), 30 secondi, poi come la X, 8 secondi, poi terminata.
    /// </summary>
    static void CloseRunning(string installed)
    {
        foreach (var process in Running(installed))
        {
            using (process)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", $"Missiva-mailto-{process.SessionId}", PipeDirection.Out, PipeOptions.CurrentUserOnly);
                    pipe.Connect(3000);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
                    writer.WriteLine("esci");
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException) { }
                if (process.WaitForExit(30_000)) continue;
                process.CloseMainWindow();
                if (process.WaitForExit(8_000)) continue;
                process.Kill();
                process.WaitForExit(10_000);
            }
        }
    }

    /// <summary>disinstalla.ps1, LICENSE o TERZE-PARTI.md dalle risorse dell'eseguibile (Missiva.csproj, "installa/").</summary>
    static void WriteInside(string name, string folder)
    {
        using var inside = typeof(SelfInstall).Assembly.GetManifestResourceStream("installa/" + name) ?? throw new IOException($"manca {name} dentro Missiva.exe");
        using var file = File.Create(Path.Combine(folder, name));
        inside.CopyTo(file);
    }

    /// <summary>Le chiavi che installa.ps1 scrive in HKCU: la voce di App installate e Missiva fra le app di posta.</summary>
    internal static void Register(RegistryKey root, string installed, string folder, string version)
    {
        long size = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
        // La disinstallazione con Windows PowerShell (powershell.exe 5.1), che c'è in ogni Windows, come in installa.ps1.
        var windowsPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
        using (var entry = root.CreateSubKey(Updates.UninstallKey))
        {
            entry.SetValue("DisplayName", "Missiva");
            entry.SetValue("DisplayVersion", version);
            entry.SetValue("Publisher", "Missiva");
            entry.SetValue("DisplayIcon", $"{installed},0");
            entry.SetValue("InstallLocation", folder);
            entry.SetValue("EstimatedSize", (int)Math.Min(size, int.MaxValue), RegistryValueKind.DWord);
            entry.SetValue("NoModify", 1, RegistryValueKind.DWord);
            entry.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            entry.SetValue("UninstallString", $"\"{windowsPowerShell}\" -NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(folder, "disinstalla.ps1")}\"");
        }
        using (var progId = root.CreateSubKey(@"Software\Classes\Missiva.Mailto"))
        {
            progId.SetValue("", T("Link di posta (mailto:)"));
            progId.SetValue("URL Protocol", "");
            using (var icon = progId.CreateSubKey("DefaultIcon")) icon.SetValue("", $"{installed},0");
            // --mailto davanti, come in installa.ps1: dopo --mailto Missiva prende solo il link (Program.Arguments).
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{installed}\" --mailto \"%1\"");
        }
        using (var capabilities = root.CreateSubKey(@"Software\Missiva\Capabilities"))
        {
            capabilities.SetValue("ApplicationName", "Missiva");
            capabilities.SetValue("ApplicationDescription", T("Posta, senza il nuovo Outlook"));
            capabilities.SetValue("ApplicationIcon", $"{installed},0");
            using var urls = capabilities.CreateSubKey("URLAssociations");
            urls.SetValue("mailto", "Missiva.Mailto");
        }
        // RegisteredApplications è di tutte le app: CreateSubKey la apre se c'è, senza svuotarla.
        using var registered = root.CreateSubKey(@"Software\RegisteredApplications");
        registered.SetValue("Missiva", @"Software\Missiva\Capabilities");
    }

    /// <summary>Il collegamento nel menu Start, come quello di installa.ps1 (IShellLink, senza WScript).</summary>
    internal static void Shortcut(string link, string installed, string folder)
    {
        var shell = (IShellLinkW)new ShellLink();
        try
        {
            shell.SetPath(installed);
            shell.SetWorkingDirectory(folder);
            shell.SetIconLocation(installed, 0);
            shell.SetDescription(T("Posta, senza il nuovo Outlook"));
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            ((IPersistFile)shell).Save(link, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr data, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int show);
        void SetShowCmd(int show);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
