using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Missiva;

/// <summary>Protezioni su allegati e link, sul modello di Outlook.</summary>
public static partial class Safety
{
    /// <summary>
    /// Tipi di file che possono eseguire codice o collegarsi da soli a un server: gli stessi che Outlook blocca (livello 1,
    /// pagina "Blocked attachments in Outlook"), con .library-ms e .search-ms (nel nuovo Outlook da luglio 2025: puntano a
    /// una cartella di rete, \\server\cartella, e Esplora file, solo aprendo la cartella che li contiene, manda al server
    /// l'hash NTLM, CVE-2025-24054), .searchconnector-ms, i pacchetti di app (.appx, .appxbundle, .appinstaller) e .rdp
    /// (collegamento al computer di chi l'ha mandato, con dischi e appunti condivisi: Midnight Blizzard, ottobre 2024).
    /// Dall'audit, blocco 10 (S-11): anche .one e .onepkg (i blocchi di OneNote, che possono portare file incorporati: Outlook
    /// li blocca dal 2023) e .psm1 (modulo di PowerShell, come .psd1 e .ps1xml già qui). I documenti con macro (.docm, .xlsm,
    /// .pptm) restano fuori: li ferma Office per i file col marchio web, non una conferma a ogni apertura.
    /// Aprirli da un'email è il modo più comune di infettare un PC.
    /// </summary>
    static readonly HashSet<string> Dangerous = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ade", ".adp", ".app", ".appinstaller", ".application", ".appref-ms", ".appx", ".appxbundle", ".asp", ".aspx", ".asx", ".bas",
        ".bat", ".bgi", ".cab", ".cer", ".chm", ".cmd", ".cnt", ".com", ".cpl", ".crt", ".csh", ".der", ".diagcab", ".exe", ".fxp",
        ".gadget", ".grp", ".hlp", ".hpj", ".hta", ".htc", ".img", ".inf", ".ins", ".iso", ".isp", ".its", ".jar", ".jnlp", ".js",
        ".jse", ".ksh", ".library-ms", ".lnk", ".mad", ".maf", ".mag", ".mam", ".maq", ".mar", ".mas", ".mat", ".mau", ".mav",
        ".maw", ".mcf", ".mda", ".mdb", ".mde", ".mdt", ".mdw", ".mdz", ".msc", ".msh", ".msh1", ".msh2", ".mshxml", ".msh1xml",
        ".msh2xml", ".msi", ".msix", ".msixbundle", ".msp", ".mst", ".msu", ".one", ".onepkg", ".ops", ".osd", ".pcd", ".pif", ".pl", ".plg", ".prf",
        ".prg", ".printerexport", ".ps1", ".ps1xml", ".ps2", ".ps2xml", ".psc1", ".psc2", ".psd1", ".psdm1", ".psm1", ".pst", ".py", ".pyc",
        ".pyo", ".pyw", ".pyz", ".pyzw", ".rdp", ".reg", ".scf", ".scr", ".sct", ".search-ms", ".searchconnector-ms",
        ".settingcontent-ms", ".shb", ".shs", ".theme", ".tmp", ".url", ".vb", ".vbe", ".vbp", ".vbs", ".vhd", ".vhdx",
        ".vsmacros", ".vsw", ".webpnp", ".website", ".ws", ".wsb", ".wsc", ".wsf", ".wsh", ".xbap", ".xll", ".xnk",
        // Dall'audit dell'11 ottobre (T-10): come .settingcontent-ms (un XML che avvia programmi), il collegamento a una base
        // di dati (.udl: aprendolo Windows si presenta al server scritto dentro) e il pacchetto di diagnostica (come .diagcab).
        ".appcontent-ms", ".udl", ".diagpkg",
    };

    public static bool IsDangerous(string fileName) => Dangerous.Contains(Extension(fileName));

    /// <summary>L'estensione che conta per Windows: senza gli spazi e i punti in fondo al nome, che Windows toglie.</summary>
    static string Extension(string fileName) => Path.GetExtension(fileName.Trim().TrimEnd('.', ' '));

    /// <summary>
    /// Le pagine web allegate (scelte del 12 ottobre 2026 sull'audit dell'11 ottobre, T-10): Apri le passa al browser, dove
    /// una pagina arrivata per posta si presenta come un sito qualunque e può chiedere una password (è il phishing con
    /// l'allegato .html: la pagina non passa da nessun filtro dei siti). Una classe a parte, non fra i Dangerous: non
    /// avviano programmi, quindi niente rosso sulla scheda e "Salva tutti" le salva; chiedono solo una conferma con un
    /// testo loro (WebPageQuestion) prima di aprirsi. Anche i disegni .svg, che il browser apre come pagine, con script e
    /// moduli; .mht e .mhtml sono pagine salvate in un file solo.
    /// </summary>
    static readonly HashSet<string> WebPages = new(StringComparer.OrdinalIgnoreCase) { ".htm", ".html", ".shtml", ".xhtml", ".svg", ".mht", ".mhtml" };

    public static bool IsWebPage(string fileName) => WebPages.Contains(Extension(fileName));

    /// <summary>La domanda prima di aprire una pagina web allegata: che cos'è, dove si apre, e che non ci si scrivono password.</summary>
    public static (string Heading, string Body, string Primary) WebPageQuestion(string fileName)
    {
        bool drawing = Extension(fileName).Equals(".svg", StringComparison.OrdinalIgnoreCase);
        return ("Aprire una pagina web arrivata per posta?",
                $"\"{fileName}\" è {(drawing ? "un disegno che il browser apre come una pagina web: è arrivato" : "una pagina web: è arrivata")} per posta, non viene da un sito. " +
                "Si apre fuori da Missiva, di solito nel browser, e può imitare la pagina di accesso di una banca o di un servizio. " +
                $"Non scriverci password, codici o dati di pagamento. {(drawing ? "Aprilo solo se lo aspettavi e ti fidi di chi lo ha mandato." : "Aprila solo se la aspettavi e ti fidi di chi l'ha mandata.")}",
                drawing ? "Apri il disegno" : "Apri la pagina");
    }

    /// <summary>
    /// I nomi che Windows riserva ai dispositivi. Dall'audit dell'11 ottobre (T-09) anche quelli che il rapporto dà per
    /// mancanti: COM0 e LPT0, quelli con la cifra in apice (COM¹, COM², COM³, LPT¹, LPT², LPT³) e i nomi della console
    /// CONIN$ e CONOUT$; e si riconoscono anche seguiti da spazi ("CON .txt"). Su Windows 11 (10.0.26300) questi nomi
    /// dentro una cartella sono risultati file normali (docs\audit\ESITO.md); su un Windows più vecchio non è stato provato.
    /// </summary>
    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// Nome di file sicuro: niente percorsi, caratteri vietati, nomi vuoti o riservati ai dispositivi (CON, NUL...); dall'audit,
    /// blocco 10 (S-09), anche senza i caratteri che non si vedono (cambi di direzione del testo come U+202E, larghezza zero).
    /// Al massimo 150 caratteri, gli ultimi: l'estensione resta. Dalla revisione del 13 ottobre 2026 (K-01) il taglio viene
    /// prima del controllo dei nomi riservati, e quello che lascia davanti (spazi, punti, la seconda metà di un carattere
    /// fatto di due) si toglie: prima un nome lungo poteva uscire dal taglio come "NUL.txt" o con uno spazio in testa, e a
    /// un nome riservato lungo giusto 150 il taglio toglieva il "_" appena messo. Così il nome che esce, ripassato di qui,
    /// non cambia: quello mostrato (<see cref="CleanAttachmentName"/>) è quello con cui si salva.
    /// </summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '\\', '/' }).ToArray();
        var clean = string.Concat(WithoutUnseen(name ?? "").Split(invalid)).TrimStart();
        // In fondo via spazi (di ogni tipo) e punti, finché ce ne sono: prima uno spazio unificatore (U+00A0) seguito da un
        // punto restava, e ripassando il nome di qui spariva.
        int end = clean.Length;
        while (end > 0 && (clean[end - 1] == '.' || char.IsWhiteSpace(clean[end - 1]))) end--;
        clean = clean[..end];
        if (clean.Length > MaxName)
        {
            int start = clean.Length - MaxName;
            if (char.IsLowSurrogate(clean[start])) start++;
            while (clean[start] == '.' || char.IsWhiteSpace(clean[start])) start++; // si ferma: l'ultimo carattere non è né l'uno né l'altro
            clean = clean[start..];
        }
        if (clean.Length == 0) return "allegato";
        // Con il "_" davanti un nome già lungo 150 perde il primo carattere, non l'ultimo: l'estensione decide IsDangerous.
        if (ReservedNames.Contains(clean.Split('.')[0].TrimEnd(' '))) clean = "_" + (clean.Length < MaxName ? clean : clean[1..]);
        return clean;
    }

    /// <summary>La lunghezza massima di un nome di file che viene dalla posta.</summary>
    const int MaxName = 150;

    /// <summary>
    /// Il nome di un allegato come va mostrato e usato (audit, blocco 10, S-09): senza i caratteri che non si vedono (cambi di
    /// direzione del testo come U+202E, larghezza zero, controllo: "Relazione[RLO]fdp.exe" si leggeva "Relazioneexe.pdf") e
    /// come lo salverebbe <see cref="SafeFileName"/> (niente percorsi né caratteri vietati, nomi riservati, al massimo 150
    /// caratteri). Si calcola una volta, alla nascita dell'allegato (<c>Attachment.Name</c> e <c>MessageSummary.AttachmentNames</c>,
    /// da qualunque provider e dalla copia locale), così scheda, pastiglia, titoli, domande, <see cref="IsDangerous"/> e
    /// salvataggio usano la stessa stringa. Un nome vuoto resta vuoto; uno normale torna la stessa stringa senza allocare.
    /// </summary>
    public static string CleanAttachmentName(string? name) =>
        string.IsNullOrEmpty(name) ? "" : IsPlainName(name) ? name : SafeFileName(name);

    /// <summary>Il testo senza i caratteri che non si vedono (di controllo, di formato: cambi di direzione, larghezza zero), com'è per i nomi delle persone.</summary>
    static string WithoutUnseen(string text)
    {
        if (!text.Any(c => c < ' ' || c >= 0x7F)) return text;
        var visible = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            if (!SenderCheck.Unseen(rune, shown: true)) visible.Append(rune.ToString());
        return visible.ToString();
    }

    /// <summary>Un nome che <see cref="CleanAttachmentName"/> lascia com'è: ASCII visibile, senza caratteri vietati, senza spazi o punti ai bordi, non riservato, al massimo 150 caratteri.</summary>
    static bool IsPlainName(string name)
    {
        if (name.Length > MaxName || name[0] == ' ' || name[^1] is ' ' or '.') return false;
        foreach (var c in name)
            if (c < ' ' || c >= 0x7F || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*') return false;
        var dot = name.IndexOf('.');
        return !ReservedNames.GetAlternateLookup<ReadOnlySpan<char>>().Contains((dot < 0 ? name : name.AsSpan(0, dot)).TrimEnd(' '));
    }

    // ---------------------------------------------------------------- marchio "scaricato da Internet"

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    /// <summary>
    /// Aggiunge al file il marchio web (flusso Zone.Identifier, zona Internet), come fanno browser e Outlook:
    /// Word lo apre in Visualizzazione protetta e SmartScreen controlla gli eseguibili prima di avviarli.
    /// </summary>
    /// <returns>
    /// Se il marchio è stato scritto. Falso, per esempio, su chiavette FAT e cartelle di rete senza flussi alternativi: chi
    /// apre il file lo sa (<see cref="NeedsWebMark"/>), non lo ingoia più come prima (audit, blocco 10, S-10).
    /// </returns>
    public static bool MarkFromInternet(string path)
    {
        try
        {
            using var handle = CreateFileW(path + ":Zone.Identifier", 0x40000000 /* GENERIC_WRITE */, 0, IntPtr.Zero, 2 /* CREATE_ALWAYS */, 0, IntPtr.Zero);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            using var stream = new FileStream(handle, FileAccess.Write);
            stream.Write(Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n"));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"marchio web su {path}", ex); // per esempio su chiavette FAT, che non hanno i flussi alternativi
            return false;
        }
    }

    /// <summary>Scrive un file che viene dalla posta (un messaggio .eml, un documento firmato) e lo marca come scaricato da Internet.</summary>
    /// <returns>Se il marchio è stato scritto.</returns>
    public static async Task<bool> SaveDownloadedAsync(string path, byte[] content)
    {
        await File.WriteAllBytesAsync(path, content);
        return MarkFromInternet(path);
    }

    /// <summary>
    /// I documenti che Office apre in Visualizzazione protetta solo se hanno il marchio web (e disattiva le macro): per questi
    /// e per i tipi pericolosi un file aperto senza marchio è un file senza il controllo di Office e di SmartScreen.
    /// </summary>
    static readonly HashSet<string> OfficeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm", ".rtf", ".xls", ".xlsx", ".xlsm", ".xlsb", ".xlt", ".xltx", ".xltm", ".xla",
        ".xlam", ".ppt", ".pptx", ".pptm", ".pps", ".ppsx", ".ppsm", ".pot", ".potx", ".potm", ".ppa", ".ppam", ".pub", ".vsd", ".vsdx",
        ".vsdm", ".accdb", ".accde", ".mpp",
    };

    /// <summary>Un file per cui il marchio web conta davvero: un documento di Office o un tipo che chiede conferma.</summary>
    public static bool NeedsWebMark(string fileName) => IsDangerous(fileName) || OfficeTypes.Contains(Extension(fileName));

    // ---------------------------------------------------------------- Esplora file

    /// <summary>
    /// Apre Esplora file sulla cartella di un file, con il file scelto. Esplora file si avvia con il suo percorso intero
    /// (revisione del 14 ottobre 2026, U-05): con il solo nome "explorer.exe" Windows lo cerca prima nella cartella di
    /// Missiva e nella cartella corrente, che le finestre di scelta dei file possono spostare dove si è appena salvato, e
    /// solo dopo in quelle di sistema: un file con quel nome messo lì da un altro partirebbe al suo posto. Un percorso con
    /// una virgoletta (Windows non ne ammette) non si passa: chiuderebbe quella dell'argomento.
    /// </summary>
    public static void ShowInFolder(string path)
    {
        if (path.Contains('"')) return;
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExplorerPath, $"/select,\"{path}\"") { UseShellExecute = false });
    }

    /// <summary>Esplora file, dove lo tiene Windows.</summary>
    internal static string ExplorerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    // ---------------------------------------------------------------- file temporanei

    public static readonly string TempDir = Path.Combine(Path.GetTempPath(), "Missiva");

    /// <summary>
    /// Cancella gli allegati aperti in precedenza. Quelli ancora aperti in un altro programma restano per la volta dopo.
    /// Dall'audit, blocco 10 (S-28): oltre che all'avvio (un giorno) e all'uscita (subito) la chiama anche il timer di ogni
    /// minuto della finestra principale, per i file più vecchi di un'ora. L'età è quella dell'ultimo momento fra l'apertura
    /// e l'ultima modifica dei file: un allegato modificato e salvato da Word resta un'altra ora dopo l'ultimo salvataggio.
    /// <paramref name="root"/> solo per le prove.
    /// </summary>
    public static void CleanTemp(TimeSpan olderThan, string? root = null)
    {
        try
        {
            root ??= TempDir;
            // Se la cartella degli allegati è un collegamento a un'altra (una giunzione: Missiva non ne crea), le sue
            // "sottocartelle" sono quelle della cartella di arrivo: prima questa pulizia le cancellava, con tutto quello che
            // contenevano (revisione del 14 ottobre 2026, U-03, provato con una giunzione verso una cartella di prova).
            if (!Directory.Exists(root) || IsLink(root)) return;
            foreach (var dir in Directory.GetDirectories(root))
            {
                try
                {
                    // Un collegamento fra le sottocartelle: via lui, senza guardare né toccare dove porta.
                    if (IsLink(dir))
                    {
                        Directory.Delete(dir, false);
                        continue;
                    }
                    var last = Directory.GetCreationTimeUtc(dir);
                    if (olderThan > TimeSpan.Zero)
                        foreach (var file in Directory.EnumerateFiles(dir, "*", NoLinks))
                            if (File.GetLastWriteTimeUtc(file) is var written && written > last) last = written;
                    // Directory.Delete di .NET toglie i collegamenti che trova più dentro senza seguirli (misurato in U-03).
                    if (olderThan <= TimeSpan.Zero || DateTime.UtcNow - last >= olderThan) Directory.Delete(dir, true);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Tutti i file sotto una cartella, senza entrare nei collegamenti ad altre cartelle e senza contarli.</summary>
    static readonly EnumerationOptions NoLinks = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };

    /// <summary>La cartella (o il file) è un collegamento: una giunzione, un collegamento simbolico, un punto di montaggio.</summary>
    static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateDirectoryW(string path, IntPtr security);

    /// <summary>
    /// Il percorso più lungo che un programma di Office apre: Excel si ferma a 218 caratteri fra cartella e nome (Word e
    /// PowerPoint a 259). Il nome di un allegato arriva a 150 (<see cref="MaxName"/>).
    /// </summary>
    const int TightestPath = 218;

    /// <summary>
    /// Una cartella nuova per un allegato da aprire con un altro programma, dentro <see cref="TempDir"/> (revisione del 14
    /// ottobre 2026, U-03). Nuova davvero: creata con una chiamata che non riesce se il nome c'è già (Directory.CreateDirectory
    /// non lo dice, e prima il file finiva in una cartella che c'era, con quello che conteneva), quindi non è la cartella né
    /// il collegamento di un altro. Il nome ha 32 cifre esadecimali casuali (128 bit; prima 8) finché il percorso del file
    /// resta entro quello che Excel apre (<see cref="TightestPath"/>); con un nome di allegato più lungo 16, poi 8 come
    /// prima: mai un percorso più lungo di quello di prima dove contava. Se la cartella degli allegati di Missiva è un
    /// collegamento a un'altra cartella non ci si scrive: lì un collegamento non è mai legittimo, la crea Missiva (%TEMP% e
    /// le cartelle sopra non si guardano: possono esserlo per buoni motivi).
    /// </summary>
    /// <param name="root">Solo per le prove: al posto di <see cref="TempDir"/>.</param>
    /// <param name="names">Solo per le prove: i nomi da provare, data la lunghezza, al posto di quelli casuali.</param>
    public static string NewTempFolder(string fileName, string? root = null, Func<int, string>? names = null)
    {
        root ??= TempDir;
        Directory.CreateDirectory(root);
        if (IsLink(root)) throw new IOException($"La cartella degli allegati temporanei di Missiva ({root}) è un collegamento a un'altra cartella: Missiva non ci scrive. Va tolta a mano.");
        int length = TempNameLength(root, fileName);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var dir = Path.Combine(root, names?.Invoke(length) ?? System.Security.Cryptography.RandomNumberGenerator.GetHexString(length, lowercase: true));
            if (CreateDirectoryW(dir, IntPtr.Zero)) return IsLink(dir) ? throw new IOException("La cartella appena creata per l'allegato è un collegamento.") : dir;
            int error = Marshal.GetLastWin32Error();
            if (error != 183 /* ERROR_ALREADY_EXISTS */) throw new Win32Exception(error);
        }
        throw new IOException("Non riesco a creare una cartella nuova per l'allegato.");
    }

    /// <summary>
    /// Quante cifre ha il nome della cartella di un allegato: 32 se il percorso del file resta entro <see cref="TightestPath"/>,
    /// altrimenti 16, altrimenti 8 (com'era per tutti).
    /// </summary>
    internal static int TempNameLength(string root, string fileName)
    {
        int room = TightestPath - (root.Length + 1 + 1 + fileName.Length);
        return room >= 32 ? 32 : room >= 16 ? 16 : 8;
    }

    /// <summary>
    /// Un allegato scritto nella cartella temporanea per aprirlo con un altro programma, ancora con un nome neutro
    /// (<see cref="Pending"/>): il nome vero lo prende solo quando si apre davvero (<see cref="Release"/>).
    /// </summary>
    /// <param name="Marked">Il marchio web è stato scritto (<see cref="MarkFromInternet"/>); resta sul file quando cambia nome.</param>
    public sealed record TempFile(string Folder, string Pending, string Final, bool Marked)
    {
        /// <summary>Il file prende il suo nome: il percorso da aprire.</summary>
        public string Release()
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(Pending, Final);
                    return Final;
                }
                // Un altro programma ha ancora aperto il file appena scritto (un antivirus che lo guarda): un momento, e di nuovo.
                catch (IOException) when (attempt < 3 && File.Exists(Pending))
                {
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>Non si apre più: via il file con la sua cartella.</summary>
        public void Discard()
        {
            try { Directory.Delete(Folder, true); } catch (Exception) { }
        }
    }

    /// <summary>In coda al nome di un allegato mentre si scrive e si marca: un tipo che Windows non apre con nulla.</summary>
    const string PendingSuffix = ".parte";

    /// <summary>
    /// Scrive un allegato in una cartella temporanea nuova (<see cref="NewTempFolder"/>) e lo marca come scaricato da
    /// Internet, con un nome neutro: quello vero, con la sua estensione, lo prende solo con <see cref="TempFile.Release"/>.
    /// Prima il file nasceva già con il nome finale, e quando il marchio non si scriveva (una cartella temporanea su un
    /// disco senza flussi alternativi, FAT o di rete) restava lì, senza marchio e apribile, per tutto il tempo della
    /// domanda "Aprire un file senza il controllo di Windows?" (revisione del 14 ottobre 2026, U-03).
    /// </summary>
    public static TempFile WriteTemp(string fileName, byte[] content, string? root = null)
    {
        var folder = NewTempFolder(fileName, root);
        try
        {
            string final = Path.Combine(folder, fileName), pending = final + PendingSuffix;
            // La cartella è appena nata vuota: il file non può esserci, e se c'è non si scrive.
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(content);
            return new TempFile(folder, pending, final, MarkFromInternet(pending));
        }
        catch
        {
            try { Directory.Delete(folder, true); } catch (Exception) { }
            throw;
        }
    }

    /// <summary>
    /// Scrive un file con un nome preso dalla posta in una cartella scelta dall'utente ("Salva tutti") senza mai
    /// sovrascrivere: se il nome c'è già prende " (2)", " (3)"... come nel browser. Il file si crea solo se non esiste, in
    /// un passo solo (CreateNew): prima si guardava se c'era e poi si scriveva. Un nome che è già di una cartella o di un
    /// collegamento non si usa (revisione del 14 ottobre 2026, U-03; provato con una giunzione, non con un collegamento
    /// simbolico a un file, che senza permessi in più non si crea).
    /// </summary>
    /// <returns>Il percorso del file scritto.</returns>
    public static async Task<string> WriteNewAsync(string folder, string fileName, byte[] content)
    {
        for (int n = 1; ; n++)
        {
            var path = Path.Combine(folder, n == 1 ? fileName : $"{Path.GetFileNameWithoutExtension(fileName)} ({n}){Path.GetExtension(fileName)}");
            // Una cartella o un collegamento con quel nome (gli attributi sono quelli del collegamento, non di dove porta): il prossimo.
            var taken = new FileInfo(path).Attributes;
            if ((int)taken != -1 && (taken & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) continue;
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await stream.WriteAsync(content);
                return path;
            }
            catch (IOException ex) when (n < 10_000 && (ex.HResult & 0xFFFF) is 80 /* ERROR_FILE_EXISTS */ or 183 /* ERROR_ALREADY_EXISTS */)
            {
                // il nome è preso (anche da un collegamento): il prossimo
            }
        }
    }

    // ---------------------------------------------------------------- link ingannevoli

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?((?:[\p{L}\p{N}-]+\.)+[\p{L}]{2,})(?:[:/?#]\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DomainInText();

    /// <summary>
    /// Secondi livelli generici dei domini nazionali (co.uk, com.au, or.jp, gob.mx): sotto di loro il dominio di chi lo
    /// registra ha tre parti. Prima bastava un secondo livello di tre lettere, e tim.it, ing.it o sky.it davano l'avviso.
    /// </summary>
    static readonly HashSet<string> SecondLevels = new(StringComparer.Ordinal)
    {
        "ac", "co", "com", "edu", "go", "gob", "gov", "govt", "gv", "ltd", "mil", "ne", "net", "nhs", "nic", "or", "org", "plc", "sch",
    };

    /// <summary>
    /// I secondi livelli di .it sotto cui organizzazioni diverse registrano ognuna il proprio nome (revisione del 13 ottobre
    /// 2026, K-02): gov ed edu (ministeri, agenzie, scuole) e i nomi geografici che il registro .it riserva, cioè le regioni
    /// e le province, con il nome e con la sigla. Sotto pd.it stanno il comune di un paese e una ditta qualunque: non sono
    /// la stessa organizzazione. Un elenco solo per le tre regole che ricavano l'organizzazione da un nome: loghi e segno
    /// "autenticato" (SenderLogos.DomainOf), "Mostra immagini" (Settings.ImageKey) e gli avvisi sui link (BaseDomain); prima
    /// gov ed edu mancavano alla prima, i nomi geografici a tutte. I nomi sono tutti i secondi livelli di .it della lista
    /// pubblica dei suffissi (Public Suffix List), sezione ICANN, letta il 14 ottobre 2026: 407, con le varianti (nomi uniti
    /// o con il trattino, in altre lingue, sigle a tre lettere delle regioni, le province sarde soppresse) e quelli con
    /// lettere accentate scritti come nei nomi di dominio (xn--...), come li portano gli indirizzi di posta. Fino ad allora
    /// erano 229, scritti a memoria e senza le varianti. I domini della sezione privata della lista (servizi di una ditta,
    /// come blogspot.it) non ci sono apposta: non sono nomi che il registro riserva, e vanno e vengono con i servizi della
    /// ditta.
    /// </summary>
    static readonly HashSet<string> ItalianSecondLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "edu", "gov",
        // Regioni, con le varianti della lista: nomi uniti o con il trattino, in altre lingue, sigle a tre lettere.
        "abr", "abruzzo", "aosta-valley", "aostavalley", "bas", "basilicata", "cal", "calabria", "cam", "campania", "emilia-romagna", "emiliaromagna", "emr",
        "friuli-v-giulia", "friuli-ve-giulia", "friuli-vegiulia", "friuli-venezia-giulia", "friuli-veneziagiulia", "friuli-vgiulia", "friuliv-giulia",
        "friulive-giulia", "friulivegiulia", "friulivenezia-giulia", "friuliveneziagiulia", "friulivgiulia", "fvg", "laz", "lazio", "lig", "liguria", "lom",
        "lombardia", "lombardy", "lucania", "mar", "marche", "mol", "molise", "piedmont", "piemonte", "pmn", "pug", "puglia", "sar", "sardegna", "sardinia",
        "sic", "sicilia", "sicily", "taa", "tos", "toscana", "trentin-sud-tirol", "trentin-sudtirol", "trentin-sued-tirol", "trentin-suedtirol",
        "trentino-a-adige", "trentino-aadige", "trentino-alto-adige", "trentino-altoadige", "trentino-s-tirol", "trentino-stirol", "trentino-sud-tirol",
        "trentino-sudtirol", "trentino-sued-tirol", "trentino-suedtirol", "trentinoa-adige", "trentinoaadige", "trentinoalto-adige", "trentinoaltoadige",
        "trentinos-tirol", "trentinostirol", "trentinosud-tirol", "trentinosued-tirol", "trentinosuedtirol", "trentinsud-tirol", "trentinsudtirol",
        "trentinsued-tirol", "trentinsuedtirol", "tuscany", "umb", "umbria", "val-d-aosta", "val-daosta", "vald-aosta", "valle-aosta", "valle-d-aosta",
        "valle-daosta", "valleaosta", "valled-aosta", "valledaosta", "vallee-aoste", "vallee-d-aoste", "valleeaoste", "valleedaoste", "vao", "vda", "ven",
        "veneto",
        // Le stesse con le lettere accentate (trentino-süd-tirol, vallée-aoste), scritte come nei nomi di dominio.
        "xn--trentin-sd-tirol-rzb", "xn--trentin-sdtirol-7vb", "xn--trentino-sd-tirol-c3b", "xn--trentino-sdtirol-szb", "xn--trentinosd-tirol-rzb",
        "xn--trentinosdtirol-7vb", "xn--trentinsd-tirol-6vb", "xn--trentinsdtirol-nsb", "xn--valle-aoste-ebb", "xn--valle-d-aoste-ehb", "xn--valleaoste-e7a",
        "xn--valledaoste-ebb",
        // Province, con la sigla.
        "ag", "al", "an", "ao", "ap", "aq", "ar", "at", "av", "ba", "bg", "bi", "bl", "bn", "bo", "br", "bs", "bt", "bz", "ca", "cb", "ce", "ch", "ci", "cl",
        "cn", "co", "cr", "cs", "ct", "cz", "en", "fc", "fe", "fg", "fi", "fm", "fr", "ge", "go", "gr", "im", "is", "kr", "lc", "le", "li", "lo", "lt", "lu",
        "mb", "mc", "me", "mi", "mn", "mo", "ms", "mt", "na", "no", "nu", "og", "or", "ot", "pa", "pc", "pd", "pe", "pg", "pi", "pn", "po", "pr", "pt", "pu",
        "pv", "pz", "ra", "rc", "re", "rg", "ri", "rm", "rn", "ro", "sa", "si", "so", "sp", "sr", "ss", "su", "sv", "ta", "te", "tn", "to", "tp", "tr", "ts",
        "tv", "ud", "va", "vb", "vc", "ve", "vi", "vr", "vs", "vt", "vv",
        // Province, con il nome e le sue varianti: composti, in altre lingue, uniti o con il trattino.
        "agrigento", "alessandria", "alto-adige", "altoadige", "ancona", "andria-barletta-trani", "andria-trani-barletta", "andriabarlettatrani",
        "andriatranibarletta", "aosta", "aoste", "arezzo", "ascoli-piceno", "ascolipiceno", "asti", "avellino", "balsan", "balsan-sudtirol", "balsan-suedtirol",
        "bari", "barletta-trani-andria", "barlettatraniandria", "belluno", "benevento", "bergamo", "biella", "bologna", "bolzano", "bolzano-altoadige", "bozen",
        "bozen-sudtirol", "bozen-suedtirol", "brescia", "brindisi", "bulsan", "bulsan-sudtirol", "bulsan-suedtirol", "cagliari", "caltanissetta",
        "campidano-medio", "campidanomedio", "campobasso", "carbonia-iglesias", "carboniaiglesias", "carrara-massa", "carraramassa", "caserta", "catania",
        "catanzaro", "cesena-forli", "cesenaforli", "chieti", "como", "cosenza", "cremona", "crotone", "cuneo", "dell-ogliastra", "dellogliastra", "enna",
        "fermo", "ferrara", "firenze", "florence", "foggia", "forli-cesena", "forlicesena", "frosinone", "genoa", "genova", "gorizia", "grosseto",
        "iglesias-carbonia", "iglesiascarbonia", "imperia", "isernia", "la-spezia", "laquila", "laspezia", "latina", "lecce", "lecco", "livorno", "lodi",
        "lucca", "macerata", "mantova", "massa-carrara", "massacarrara", "matera", "medio-campidano", "mediocampidano", "messina", "milan", "milano", "modena",
        "monza", "monza-brianza", "monza-e-della-brianza", "monzabrianza", "monzaebrianza", "monzaedellabrianza", "naples", "napoli", "novara", "nuoro",
        "ogliastra", "olbia-tempio", "olbiatempio", "oristano", "padova", "padua", "palermo", "parma", "pavia", "perugia", "pesaro-urbino", "pesarourbino",
        "pescara", "piacenza", "pisa", "pistoia", "pordenone", "potenza", "prato", "ragusa", "ravenna", "reggio-calabria", "reggio-emilia", "reggiocalabria",
        "reggioemilia", "rieti", "rimini", "roma", "rome", "rovigo", "salerno", "sassari", "savona", "siena", "siracusa", "sondrio", "sud-sardegna",
        "sudsardegna", "suedtirol", "taranto", "tempio-olbia", "tempioolbia", "teramo", "terni", "torino", "trani-andria-barletta", "trani-barletta-andria",
        "traniandriabarletta", "tranibarlettaandria", "trapani", "trentino", "trento", "treviso", "trieste", "turin", "udine", "urbino-pesaro", "urbinopesaro",
        "varese", "venezia", "venice", "verbania", "verbano-cusio-ossola", "vercelli", "verona", "vibo-valentia", "vibovalentia", "vicenza", "viterbo",
        // Gli stessi con le lettere accentate (südtirol, forlì-cesena), scritti come nei nomi di dominio.
        "xn--balsan-sdtirol-nsb", "xn--bozen-sdtirol-2ob", "xn--bulsan-sdtirol-nsb", "xn--cesena-forl-mcb", "xn--cesenaforl-i8a", "xn--forl-cesena-fcb",
        "xn--forlcesena-c8a", "xn--sdtirol-n2a",
    };

    /// <summary>Il nome a due etichette è un suffisso di .it sotto cui ognuno registra il suo (gov.it, pd.it, veneto.it): da solo non è di nessuno.</summary>
    internal static bool IsItalianSuffix(string second, string top) => top.Equals("it", StringComparison.OrdinalIgnoreCase) && ItalianSecondLevels.Contains(second);

    /// <summary>Un suffisso come co.uk, com.au o gov.it, che da solo non è il sito di nessuno.</summary>
    static bool IsSuffix(string[] labels) => labels.Length == 2 && (labels[1].Length == 2 && SecondLevels.Contains(labels[0]) || IsItalianSuffix(labels[0], labels[1]));

    /// <summary>Dominio principale (unibo.it, paypal.com, bbc.co.uk).</summary>
    internal static string BaseDomain(string host)
    {
        var labels = host.Split('.');
        if (labels.Length <= 2) return host;
        return string.Join('.', labels[^(IsSuffix(labels[^2..]) ? 3 : 2)..]);
    }

    /// <summary>Il nome del sito come lo scrive il testo dei link: minuscolo, senza "www." e senza il punto finale.</summary>
    static string SiteName(string host)
    {
        host = host.ToLowerInvariant().TrimEnd('.');
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>
    /// Avviso se il link è sospetto: il testo mostra un sito e il link porta altrove, oppure l'indirizzo usa
    /// caratteri che imitano quelli di un sito noto. Null se non c'è niente da segnalare.
    /// </summary>
    public static string? LinkWarning(string visibleText, Uri target)
    {
        if (target.Scheme is not ("http" or "https")) return null;
        var host = target.IdnHost;
        if (host.Split('.').Any(l => l.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)))
            return $"L'indirizzo {target.Host} contiene caratteri non latini che possono imitare quelli di un sito conosciuto.";

        var m = DomainInText().Match(visibleText.Trim());
        if (!m.Success) return null; // il testo non è un indirizzo ("Clicca qui"): niente da confrontare
        var shown = SiteName(new IdnMapping().GetAscii(m.Groups[1].Value.ToLowerInvariant()));
        var site = SiteName(host);
        if (site == shown || BaseDomain(site) == BaseDomain(shown)) return null;
        // Un sito dentro quello mostrato (servizi.salute.gov.it per salute.gov.it), purché il testo non sia solo co.uk.
        if (site.EndsWith("." + shown, StringComparison.Ordinal) && !IsSuffix(shown.Split('.'))) return null;
        return $"Il testo del link mostra {m.Groups[1].Value}, ma il link porta a {target.Host}. " +
               "È il trucco più comune dei messaggi di phishing.";
    }
}
