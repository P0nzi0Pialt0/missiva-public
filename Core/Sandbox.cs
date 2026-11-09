using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Missiva;

/// <summary>
/// Un processo di Missiva (Missiva.exe con i suoi argomenti) in un AppContainer senza capability, come i processi dei
/// browser: niente rete (nemmeno verso il PC stesso), niente file, cartelle e registro dell'utente (impostazioni, token
/// di accesso, posta, documenti), e quindi niente DPAPI; vede solo quello che Windows dà a tutte le app e quello di
/// Missiva, a cui Missiva stessa dà la lettura al SID dell'AppContainer (GrantRead: il solo eseguibile unico, o la cartella
/// dell'eseguibile nella compilazione di prova, e quella dove .NET ha estratto le sue librerie). Gli unici handle ereditati
/// sono standard input e output (PROC_THREAD_ATTRIBUTE_HANDLE_LIST); l'ambiente è una lista bianca
/// (EnvironmentBlock); parte sospeso, così chi lo avvia lo mette nel suo job prima che esegua qualcosa (senza job
/// si termina: Abort); sta su un desktop di Missiva fuori dalla window station dell'utente (Desktop), senza i suoi appunti
/// e il suo schermo.
/// Il profilo dell'AppContainer (Missiva.Anteprima, senza capability) si registra per l'utente la prima volta.
/// </summary>
static class Sandbox
{
    /// <summary>Il nome dell'AppContainer, da cui Windows ricava il SID (sempre lo stesso per lo stesso nome).</summary>
    const string Name = "Missiva.Anteprima";

    /// <summary>Un processo avviato sospeso: Resume lo fa partire. Input e Output sono i tubi verso il suo standard input e output.</summary>
    internal sealed record Started(Process Process, IntPtr Handle, IntPtr Thread, Stream Input, Stream Output);

    /// <summary>L'AppContainer non si è potuto creare: niente processo, e nessun ripiego senza protezione.</summary>
    internal sealed class SandboxException(string message) : Exception(message);

    static SecurityIdentifier? sid;

    /// <summary>
    /// Il SID dell'AppContainer (S-1-15-2-...). Il profilo si crea la prima volta (CreateAppContainerProfile, senza
    /// capability: Windows vuole la registrazione per avviarci un processo) e poi si riusa, ricavando il SID dal nome;
    /// disinstalla.ps1 lo toglie.
    /// </summary>
    internal static SecurityIdentifier Sid()
    {
        if (sid != null) return sid;
        // Nome e descrizione li mostra Windows (il profilo nasce una volta, nella lingua di quel momento).
        int result = CreateAppContainerProfile(Name, T("Missiva, anteprima degli allegati"), T("Il processo che disegna i PDF e le immagini degli allegati, senza rete né file dell'utente"),
                                               IntPtr.Zero, 0, out var pointer);
        if (result == unchecked((int)0x800700B7)) result = DeriveAppContainerSidFromAppContainerName(Name, out pointer); // il profilo c'è già
        else if (result == 0) Log.Write("profilo dell'anteprima protetta creato (AppContainer " + Name + ")");
        if (result != 0) throw new SandboxException($"AppContainer non creato (0x{result:X8})");
        try { return sid = new SecurityIdentifier(pointer); }
        finally { FreeSid(pointer); }
    }

    /// <summary>Le cartelle già aperte all'AppContainer in questa sessione: il controllo si fa una volta sola.</summary>
    static readonly HashSet<string> granted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// La lettura (e l'esecuzione) delle cartelle di Missiva al SID dell'AppContainer, ereditata dai file: la cartella
    /// dell'eseguibile e, per l'eseguibile unico, quella in cui .NET ha estratto le librerie native (in %TEMP%\.net). Solo
    /// cartelle di Missiva, dell'utente; nient'altro diventa leggibile.
    /// </summary>
    static void GrantRead(SecurityIdentifier container)
    {
        foreach (var (path, isFile) in MissivaPaths())
        {
            if (granted.Contains(path) || !(isFile ? File.Exists(path) : Directory.Exists(path))) continue;
            FileSystemSecurity acl = isFile ? new FileInfo(path).GetAccessControl() : new DirectoryInfo(path).GetAccessControl();
            bool has = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>()
                          .Any(r => r.IdentityReference.Equals(container) && r.AccessControlType == AccessControlType.Allow);
            if (!has)
            {
                acl.AddAccessRule(new FileSystemAccessRule(container, FileSystemRights.ReadAndExecute,
                    isFile ? InheritanceFlags.None : InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                if (isFile) new FileInfo(path).SetAccessControl((FileSecurity)acl);
                else new DirectoryInfo(path).SetAccessControl((DirectorySecurity)acl);
                Log.Write($"{(isFile ? "file aperto" : "cartella aperta")} in lettura all'anteprima protetta: {path}");
            }
            granted.Add(path);
        }
    }

    /// <summary>
    /// Quello che il processo deve poter leggere: per l'eseguibile unico (senza Missiva.dll accanto) solo l'eseguibile, un
    /// file, senza ereditarietà, e le librerie native estratte da .NET; altrimenti (la compilazione di prova, con le sue
    /// librerie accanto) la cartella dell'eseguibile. Audit, blocco 10 (S-13): prima si dava la lettura a tutta la cartella
    /// da cui l'eseguibile unico era avviato, anche Download o la Scrivania, e restava lì. Provato con la versione finale
    /// di prova (un solo file): il processo parte e disegna con il solo file aperto.
    /// </summary>
    static IEnumerable<(string Path, bool IsFile)> MissivaPaths()
    {
        var exe = Environment.ProcessPath!;
        if (!File.Exists(Path.ChangeExtension(exe, ".dll"))) yield return (exe, true);
        else yield return (Path.GetDirectoryName(exe)!, false);
        if (Extracted() is { } extracted) yield return (extracted, false);
    }

    /// <summary>
    /// Dove .NET ha estratto le librerie native dell'eseguibile unico (IncludeNativeLibrariesForSelfExtract):
    /// base\Missiva\identificativo, fra le cartelle delle librerie native; null nella versione di prova, che non le estrae.
    /// </summary>
    static string? Extracted()
    {
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath!);
        var home = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Environment.ProcessPath!)!);
        return (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.TrimEndingDirectorySeparator)
            .FirstOrDefault(d => !d.Equals(home, StringComparison.OrdinalIgnoreCase)
                                 && Path.GetFileName(Path.GetDirectoryName(d))?.Equals(name, StringComparison.OrdinalIgnoreCase) == true
                                 && Directory.Exists(d));
    }

    /// <summary>
    /// Le sole variabili d'ambiente di Missiva che il processo riceve (audit, blocco 10, S-13): quelle di Windows che non dicono
    /// niente dell'utente, più LOCALAPPDATA, senza la quale CreateProcess di un AppContainer non riesce (errore 203, provato).
    /// Prima passavano tutte (eventuali token, DOTNET_STARTUP_HOOKS, USERPROFILE, PATH e quello che l'utente ha messo).
    /// </summary>
    static readonly string[] AllowedVariables =
        ["SystemRoot", "windir", "SystemDrive", "TEMP", "TMP", "LOCALAPPDATA", "PATHEXT", "OS", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS"];

    /// <summary>
    /// L'ambiente del processo: la lista bianca di sopra; DOTNET_EnableDiagnostics=0 (niente debugger, profiler o comandi
    /// dall'esterno, nessun'altra DOTNET_ di Missiva) e, per l'eseguibile unico, la cartella delle librerie già estratte, che
    /// il processo non potrebbe scrivere di suo.
    /// </summary>
    internal static string EnvironmentBlock()
    {
        var variables = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Where(e => AllowedVariables.Contains((string)e.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(e => (string)e.Key, e => e.Value as string ?? "", StringComparer.OrdinalIgnoreCase);
        variables["DOTNET_EnableDiagnostics"] = "0";
        if (Extracted() is { } extracted && Path.GetDirectoryName(Path.GetDirectoryName(extracted)) is { } root)
            variables["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = root;
        var block = new StringBuilder();
        foreach (var (name, value) in variables.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
            if (name.Length > 0 && !name.Contains('=')) block.Append(name).Append('=').Append(value).Append('\0');
        return block.Append('\0').ToString();
    }

    /// <summary>Missiva.exe con gli argomenti, nell'AppContainer, sospeso.</summary>
    internal static Started Start(string arguments)
    {
        var exe = Environment.ProcessPath!;
        var container = Sid();
        try
        {
            GrantRead(container);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            throw new SandboxException("cartella di Missiva non leggibile dall'anteprima protetta: " + ex.Message);
        }
        var ownDesktop = Desktop(container);

        var inherit = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = 1 };
        if (!CreatePipe(out var childInput, out var parentInput, ref inherit, 0)) throw Failed("CreatePipe");
        if (!CreatePipe(out var parentOutput, out var childOutput, ref inherit, 0))
        {
            childInput.Dispose();
            parentInput.Dispose();
            throw Failed("CreatePipe");
        }
        // Le estremità di Missiva non passano al processo.
        SetHandleInformation(parentInput, HANDLE_FLAG_INHERIT, 0);
        SetHandleInformation(parentOutput, HANDLE_FLAG_INHERIT, 0);

        var sidBytes = new byte[container.BinaryLength];
        container.GetBinaryForm(sidBytes, 0);
        IntPtr sidMemory = Marshal.AllocHGlobal(sidBytes.Length), capabilities = IntPtr.Zero, handles = IntPtr.Zero, list = IntPtr.Zero, environment = IntPtr.Zero,
               desktopName = IntPtr.Zero;
        bool listReady = false, done = false;
        try
        {
            Marshal.Copy(sidBytes, 0, sidMemory, sidBytes.Length);
            capabilities = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_CAPABILITIES>());
            Marshal.StructureToPtr(new SECURITY_CAPABILITIES { AppContainerSid = sidMemory }, capabilities, false); // nessuna capability
            handles = Marshal.AllocHGlobal(2 * IntPtr.Size);
            Marshal.WriteIntPtr(handles, 0, childInput.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, IntPtr.Size, childOutput.DangerousGetHandle());
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
            list = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(list, 2, 0, ref size)) throw Failed("InitializeProcThreadAttributeList");
            listReady = true;
            if (!UpdateProcThreadAttribute(list, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES, capabilities,
                                           (IntPtr)Marshal.SizeOf<SECURITY_CAPABILITIES>(), IntPtr.Zero, IntPtr.Zero))
                throw Failed("SECURITY_CAPABILITIES");
            if (!UpdateProcThreadAttribute(list, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_HANDLE_LIST, handles, (IntPtr)(2 * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                throw Failed("HANDLE_LIST");
            var startup = new STARTUPINFOEX { lpAttributeList = list };
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
            startup.StartupInfo.hStdInput = childInput.DangerousGetHandle();
            startup.StartupInfo.hStdOutput = childOutput.DangerousGetHandle();
            startup.StartupInfo.lpDesktop = desktopName = Marshal.StringToHGlobalUni(ownDesktop);
            environment = Marshal.StringToHGlobalUni(EnvironmentBlock());
            var command = new StringBuilder($"\"{exe}\" {arguments}");
            if (!CreateProcessW(exe, command, IntPtr.Zero, IntPtr.Zero, true,
                                EXTENDED_STARTUPINFO_PRESENT | CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
                                environment, Path.GetDirectoryName(exe), ref startup, out var info))
                throw Failed("CreateProcess");
            Process process;
            try
            {
                process = Process.GetProcessById(info.dwProcessId);
            }
            catch (Exception)
            {
                TerminateProcess(info.hProcess, 1);
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
                throw;
            }
            var started = new Started(process, info.hProcess, info.hThread,
                                      new FileStream(parentInput, FileAccess.Write, 65536), new FileStream(parentOutput, FileAccess.Read, 65536));
            done = true;
            return started;
        }
        finally
        {
            childInput.Dispose(); // il processo ha le sue copie
            childOutput.Dispose();
            if (!done)
            {
                parentInput.Dispose();
                parentOutput.Dispose();
            }
            if (listReady) DeleteProcThreadAttributeList(list);
            foreach (var memory in new[] { list, capabilities, handles, sidMemory, environment, desktopName })
                if (memory != IntPtr.Zero) Marshal.FreeHGlobal(memory);
        }
    }

    /// <summary>Il processo avviato sospeso non entra nel suo job: si termina senza che abbia eseguito niente, e si chiude tutto quello che era aperto.</summary>
    internal static void Abort(Started started)
    {
        TerminateProcess(started.Handle, 1);
        CloseHandle(started.Thread);
        CloseHandle(started.Handle);
        try { started.Input.Dispose(); } catch (Exception) { }
        try { started.Output.Dispose(); } catch (Exception) { }
        started.Process.Dispose();
    }

    /// <summary>Il processo, già nel suo job, parte.</summary>
    internal static void Resume(Started started)
    {
        ResumeThread(started.Thread);
        CloseHandle(started.Thread);
    }

    internal static void Close(IntPtr process)
    {
        if (process != IntPtr.Zero) CloseHandle(process);
    }

    /// <summary>"window station\desktop" del processo, per STARTUPINFO.lpDesktop; null finché non c'è.</summary>
    static string? desktopPath;

    static readonly object desktopLock = new();

    /// <summary>
    /// Il processo fuori dalla window station dell'utente (S-13, 8 novembre 2026), come i processi delle pagine di Chromium.
    /// Appunti, atomi globali e schermo sono della window station: su quella dell'utente (WinSta0) un processo dei PDF
    /// compromesso ne vedrebbe gli appunti, per quanto glielo lasciano l'AppContainer e i limiti del job, e lo schermo
    /// (BitBlt da GetDC(NULL) riusciva: docs/audit/ESITO.md, S-13). La window station è quella non interattiva della
    /// sessione di accesso: CreateWindowStation senza nome la crea o, se c'è, la apre (Service-0x0-...$), e con un nome
    /// scelto dà l'errore 5 a chi non è amministratore (provato). È la stessa
    /// in cui Chromium (Edge, WebView2) mette le sue pagine, ognuna sul suo desktop sbox_alternate_desktop_...: se la crea
    /// Missiva, la crea con la sicurezza della window station dell'utente, come Chromium, perché le pagine di WebView2 che
    /// arrivano dopo ci entrino come sempre (l'AppContainer ci entra con la voce di ALL APPLICATION PACKAGES). Il desktop è
    /// solo di questa Missiva (Missiva.Anteprima.pid): l'utente e SYSTEM tutto, il SID dell'AppContainer leggere, creare
    /// finestre e menu (il thread STA ha la sua finestra nascosta), elencare e scrivere i suoi oggetti, ma non agganciare,
    /// registrare, cambiare desktop né toccare la sicurezza (come i desktop di Chromium); integrità bassa, quella del
    /// processo. Da lì il processo vede appunti che nessuno usa (e il job comunque non glieli fa leggere né scrivere), un
    /// desktop senza finestre al posto dello schermo, e da un AppContainer non riapre WinSta0 (errore 5). Si crea alla prima
    /// anteprima e resta finché Missiva gira; se non si crea, nessun processo (SandboxException). CreateDesktop lavora sulla
    /// window station del processo, quindi per quella chiamata Missiva sta sull'altra (come Chromium): in quell'istante un
    /// altro thread di Missiva che usasse appunti o atomi troverebbe quelli dell'altra.
    /// </summary>
    static string Desktop(SecurityIdentifier container)
    {
        lock (desktopLock)
        {
            if (desktopPath != null) return desktopPath;
            var current = GetProcessWindowStation();
            int result = GetSecurityInfo(current, 7 /* SE_WINDOW_OBJECT */, 0x4 | 0x10 /* DACL, etichetta */, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var stationSecurity);
            if (result != 0) throw new SandboxException($"anteprima protetta non avviata (sicurezza della window station: errore {result})");
            // CC DC LC DT LO RC: leggere, creare finestre e menu, elencare, scrivere gli oggetti, leggere la sicurezza.
            var sddl = $"D:P(A;;GA;;;{WindowsIdentity.GetCurrent().User!.Value})(A;;GA;;;SY)(A;;CCDCLCDTLORC;;;{container.Value})S:(ML;;NW;;;LW)";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var desktopSecurity, IntPtr.Zero))
            {
                var failure = Failed("sicurezza del desktop");
                LocalFree(stationSecurity);
                throw failure;
            }
            try
            {
                var stationAttributes = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = stationSecurity };
                var desktopAttributes = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = desktopSecurity };
                var station = CreateWindowStationW(null, 0, WINSTA_ALL_ACCESS, ref stationAttributes);
                if (station == IntPtr.Zero) throw Failed("CreateWindowStation");
                var name = new char[256];
                if (!GetUserObjectInformationW(station, 2 /* UOI_NAME */, name, name.Length * 2, out int bytes) || bytes < 4)
                {
                    var failure = Failed("nome della window station");
                    CloseWindowStation(station);
                    throw failure;
                }
                var stationName = new string(name, 0, bytes / 2 - 1);
                if (stationName.Equals("WinSta0", StringComparison.OrdinalIgnoreCase))
                {
                    CloseWindowStation(station);
                    throw new SandboxException("anteprima protetta non avviata (window station dell'utente)");
                }
                var desktopName = $"Missiva.Anteprima.{Environment.ProcessId}";
                IntPtr desktop = IntPtr.Zero;
                int error;
                if (SetProcessWindowStation(station))
                {
                    desktop = CreateDesktopW(desktopName, IntPtr.Zero, IntPtr.Zero, 0, DESKTOP_ALL_ACCESS, ref desktopAttributes);
                    error = Marshal.GetLastWin32Error();
                    if (!SetProcessWindowStation(current)) Log.Write($"window station di Missiva non rimessa (errore {Marshal.GetLastWin32Error()})");
                }
                else error = Marshal.GetLastWin32Error();
                if (desktop == IntPtr.Zero)
                {
                    CloseWindowStation(station);
                    throw new SandboxException($"anteprima protetta non avviata (CreateDesktop: errore {error})");
                }
                // I due handle non si chiudono: restano finché Missiva gira, e con lei Windows li toglie.
                Log.Write($"desktop dell'anteprima protetta creato ({stationName}\\{desktopName})");
                return desktopPath = stationName + "\\" + desktopName;
            }
            finally
            {
                LocalFree(desktopSecurity);
                LocalFree(stationSecurity);
            }
        }
    }

    /// <summary>Per le prove: la window station e il desktop del processo ("window station\desktop"), null se non ci sono.</summary>
    internal static string? TestDesktop => desktopPath;

    /// <summary>Per le prove: il processo gira davvero in un AppContainer (TokenIsAppContainer).</summary>
    internal static bool InAppContainer(Process process)
    {
        if (!OpenProcessToken(process.Handle, TOKEN_QUERY, out var token)) return false;
        try { return GetTokenInformation(token, 29 /* TokenIsAppContainer */, out int value, 4, out _) && value != 0; }
        finally { CloseHandle(token); }
    }

    static SandboxException Failed(string what) => new($"anteprima protetta non avviata ({what}: errore {Marshal.GetLastWin32Error()})");

    const int HANDLE_FLAG_INHERIT = 1, STARTF_USESTDHANDLES = 0x100;
    const uint WINSTA_ALL_ACCESS = 0x37F | 0xF0000, DESKTOP_ALL_ACCESS = 0x1FF | 0xF0000;
    const uint EXTENDED_STARTUPINFO_PRESENT = 0x80000, CREATE_SUSPENDED = 0x4, CREATE_NO_WINDOW = 0x8000000, CREATE_UNICODE_ENVIRONMENT = 0x400, TOKEN_QUERY = 0x8;
    const int PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x20002, PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES = 0x20009;

    [StructLayout(LayoutKind.Sequential)]
    struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }

    [StructLayout(LayoutKind.Sequential)]
    struct SECURITY_CAPABILITIES { public IntPtr AppContainerSid; public IntPtr Capabilities; public uint CapabilityCount; public uint Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    static extern int CreateAppContainerProfile(string name, string displayName, string description, IntPtr capabilities, int count, out IntPtr sid);
    [DllImport("advapi32.dll")] static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SECURITY_ATTRIBUTES attributes, int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetHandleInformation(SafeHandle handle, int mask, int flags);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
                                      uint flags, IntPtr environment, string? directory, ref STARTUPINFOEX startup, out PROCESS_INFORMATION information);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr descriptor, IntPtr size);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("advapi32.dll")]
    static extern int GetSecurityInfo(IntPtr handle, int type, uint information, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr descriptor);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowStationW(string? name, uint flags, uint access, ref SECURITY_ATTRIBUTES attributes);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, ref SECURITY_ATTRIBUTES attributes);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetUserObjectInformationW(IntPtr handle, int index, [Out] char[] info, int length, out int needed);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetProcessWindowStation(IntPtr station);
    [DllImport("user32.dll")] static extern bool CloseWindowStation(IntPtr station);
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int infoClass, out int value, int length, out int returned);
}
