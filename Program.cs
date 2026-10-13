using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Missiva;

static class Program
{
    /// <summary>
    /// Il canale con cui una seconda copia passa a quella aperta il link mailto: con cui Windows l'ha avviata, o i comandi
    /// "apri" ed "esci" (App.ListenForMailto). Uno per sessione di Windows, e un altro per la versione di prova, come il mutex.
    /// </summary>
    public static string MailtoPipe { get; private set; } = "";

    /// <summary>
    /// Gli argomenti che contano. Per un link mailto: Windows avvia Missiva con il comando "Missiva.exe" --mailto "%1"
    /// (installa.ps1) e mette il link al posto di %1 senza toccare le virgolette: un link con dentro una virgoletta arriva
    /// spezzato in più argomenti, e quello che la segue si leggeva come un'opzione (--nascosta, --riavvio: misurato con un
    /// protocollo di prova, audit dell'11 ottobre, T-27). Dopo --mailto vale solo il link, cioè il primo argomento che è
    /// un mailto:, e nient'altro. Senza --mailto (il comando registrato dalle versioni di prima) tutto com'era.
    /// </summary>
    internal static string[] Arguments(string[] args) =>
        args is ["--mailto", ..] ? args.Skip(1).Where(Mailto.Is).Take(1).ToArray() : args;

    [STAThread]
    static void Main(string[] args)
    {
        args = Arguments(args);
        // installa.ps1 prova così l'eseguibile appena compilato, prima di metterlo al posto di quello installato: arrivare
        // qui vuol dire che .NET ha caricato Missiva. Niente finestre, niente dati, niente mutex, e codice di uscita 0
        // (13 ottobre 2026: con un file intermedio rimasto vecchio l'eseguibile si chiudeva prima di Main, a ogni avvio).
        if (args is ["--versione"]) return;
        // Il processo dei PDF dell'anteprima degli allegati (PdfWorker): niente finestre, niente dati, niente mutex.
        if (args is ["--pdf-worker"])
        {
            Environment.ExitCode = PdfWorker.Serve();
            return;
        }
#if DEBUG
        if (DevTools.TryRun(args)) return;
#endif
#if DEBUG || PROVA
        // Le prove lavorano su una copia dei dati (MISSIVA_DATA, da strumenti\prova.ps1). Senza, la prova userebbe i dati
        // veri accanto alla Missiva installata (il mutex è un altro) e segnerebbe letti i messaggi aperti: succede con il
        // clic su una sua notifica rimasta (Windows avvia l'eseguibile che l'ha mostrata, con -ToastActivated), con un
        // doppio clic sull'eseguibile e con uno script che dimentica la variabile. Si chiude subito, sempre, e lo dice.
        if (ClosesWithoutData(Environment.GetEnvironmentVariable("MISSIVA_DATA")))
        {
            TellClosedWithoutData();
            return;
        }
        // L'anteprima protetta con l'eseguibile così com'è (anche la versione finale di prova, che ha le librerie native
        // estratte da .NET): Missiva.exe --prova-anteprima file.pdf|immagine risultato.txt, senza finestre né rete.
        if (args is ["--prova-anteprima", var file, var output])
        {
            Environment.ExitCode = PdfWorker.TestFile(file, output);
            return;
        }
        const string name = @"Local\Missiva-prova";
        Log.Startup("Main entrata");
        // Prove (MISSIVA_ASSEMBLY=1): gli assembly caricati durante l'avvio, con l'istante e il thread, fra le righe "tempi: avvio".
        if (Environment.GetEnvironmentVariable("MISSIVA_ASSEMBLY") == "1")
            AppDomain.CurrentDomain.AssemblyLoad += (_, a) => { if (Log.StartupHeld) Log.Startup("assembly " + a.LoadedAssembly.GetName().Name); };
#else
        const string name = @"Local\Missiva";
#endif
        // Gli errori che nessuno prende (un lavoro in sottofondo fallito, un errore su un altro thread) lasciano la riga nel
        // registro. Non per il processo dei PDF né per gli strumenti di prova, che escono più sopra.
        Log.WatchUnhandled();
        // La sessione di Windows da ProcessIdToSessionId: Process.GetCurrentProcess().SessionId elencava tutti i processi del
        // PC per dirla (9 ms misurati all'avvio, audit del 7 ottobre, A-05).
        MailtoPipe = $"{name[@"Local\".Length..]}-mailto-{WindowsSession()}";
        // Una sola Missiva alla volta, come Outlook: due copie aperte si sovrascriverebbero le impostazioni a vicenda.
        // Aprirla di nuovo riporta in primo piano quella già aperta; con un link mailto: (Missiva app di posta di Windows)
        // il link passa a lei, che apre il messaggio nuovo.
        using var single = new Mutex(true, name, out bool first);
        // Riavviata da Missiva stessa (App.Restart, dopo l'importazione di una copia di sicurezza): la copia di prima sta
        // uscendo, e le si lascia il tempo di farlo invece di riportarla in primo piano.
        if (!first && args.Contains("--riavvio"))
        {
            try { first = single.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { first = true; } // uscita senza rilasciarlo: ora è di questa copia
        }
        if (!first)
        {
            if (args.FirstOrDefault(Mailto.Is) is { } link) SendToRunning(link);
            // Aperta di nuovo dal menu Start: quella aperta si mostra, anche se è nascosta nell'area di notifica (senza
            // finestra visibile ShowRunning non la troverebbe). Avviata di nuovo con Windows (--nascosta): niente.
            else if (args.Contains("--nascosta") || !SendToRunning("apri")) ShowRunning();
            return;
        }
        Log.Startup("Main");
        UI.App.LoadSettingsEarly(); // le impostazioni si leggono mentre WPF costruisce l'applicazione
        var app = new UI.App();
        app.Run();
    }

    /// <summary>
    /// Le costanti con cui è stato compilato questo eseguibile, scritte nel file: si leggono senza avviarlo (un campo
    /// costante di Program nei metadati). Vuoto nella versione che si installa (audit dell'11 ottobre, T-28).
    /// </summary>
    internal const string Build =
#if DEBUG && PROVA
        "DEBUG PROVA";
#elif DEBUG
        "DEBUG";
#elif PROVA
        "PROVA";
#else
        "";
#endif

#if DEBUG || PROVA
    /// <summary>
    /// Una versione con i ganci di prova (Debug o PROVA) avviata senza MISSIVA_DATA si chiude, sempre: da qualunque
    /// cartella e con qualunque argomento lavorerebbe sui dati veri, accanto alla Missiva installata. data è il valore
    /// della variabile: vale solo se non è vuoto, come per Store.Dir. Fino alle scelte del 12 ottobre la Debug si
    /// chiudeva solo se avviata da una sua notifica rimasta o da fuori bin\Debug (audit dell'11 ottobre, T-28): dalla sua
    /// cartella, con un doppio clic o da uno script senza la variabile, partiva sui dati veri. Gli strumenti che escono
    /// prima di qui non passano da questa regola: --pdf-worker e, in Debug, quelli di DevTools.TryRun (la suite, che senza
    /// la variabile lavora fra i file temporanei, le pagine, il server finto).
    /// </summary>
    internal static bool ClosesWithoutData(string? data) => data is not { Length: > 0 };

    /// <summary>La riga per chi ha avviato senza MISSIVA_DATA una versione con i ganci di prova: perché non parte, e come avviarla.</summary>
    internal static string WithoutDataNotice(string? exe) =>
        $"Missiva di prova ({Build}) non parte senza MISSIVA_DATA: lavorerebbe sui dati veri, quelli della Missiva installata. " +
        $"Avviala con strumenti\\prova.ps1, oppure imposta MISSIVA_DATA con una cartella di prova. Eseguibile: {exe ?? "sconosciuto"}";

    /// <summary>
    /// Dice perché si chiude, senza finestre (la avvia anche Windows, per una notifica) e senza scrivere nel registro, che
    /// senza MISSIVA_DATA starebbe fra i dati veri: una riga sull'uscita degli errori, che arriva a chi la legge (uno
    /// script che la raccoglie) e alla console da cui è stata avviata, se c'è; e il codice di uscita 2. Con un doppio clic
    /// o da una notifica non c'è nessuna console: si chiude e basta.
    /// </summary>
    static void TellClosedWithoutData()
    {
        Environment.ExitCode = 2;
        try
        {
            AttachConsole(-1); // ATTACH_PARENT_PROCESS: Missiva è un programma a finestre, e una console sua non ce l'ha
            Console.Error.WriteLine(WithoutDataNotice(Environment.ProcessPath));
        }
        catch (Exception)
        {
            // senza un posto dove scriverla la riga si perde: resta il codice di uscita
        }
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
#endif

    /// <summary>La sessione di Windows di questo processo (la stessa di Process.SessionId), per il nome del canale.</summary>
    internal static int WindowsSession() => ProcessIdToSessionId((uint)Environment.ProcessId, out var session) ? (int)session : 0;

    /// <summary>
    /// Il link mailto: alla copia aperta, o un suo comando ("apri"); false se non risponde (per esempio perché si sta ancora
    /// avviando).
    /// </summary>
    static bool SendToRunning(string link)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", MailtoPipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            // La copia aperta può portare in primo piano il messaggio nuovo: il permesso va a lei, il processo all'altro capo
            // del canale, e non a qualunque processo (ASFW_ANY, -1) fino al prossimo input (audit, blocco 10, S-22).
            if (ServerProcessId(client) is { } serverId) AllowSetForegroundWindow((int)serverId);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.WriteLine(link);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Il processo che sta all'altro capo del canale con nome (la copia aperta di Missiva), o null se Windows non lo dice.</summary>
    internal static uint? ServerProcessId(NamedPipeClientStream client) =>
        GetNamedPipeServerProcessId(client.SafePipeHandle, out var id) ? id : null;

    /// <summary>
    /// In primo piano la finestra della copia aperta dello stesso eseguibile: la versione di prova e quella installata
    /// hanno lo stesso nome di processo, ma l'una non deve mai toccare l'altra.
    /// </summary>
    static void ShowRunning()
    {
        using var self = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(self.ProcessName))
        {
            using (other)
            {
                if (other.Id == self.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                try
                {
                    if (!string.Equals(other.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) continue;
                }
                catch (Exception)
                {
                    continue; // processo di un altro utente o già chiuso
                }
                if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("kernel32.dll")] static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
}
