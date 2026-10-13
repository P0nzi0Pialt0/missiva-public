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
    /// </summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '\\', '/' }).ToArray();
        var clean = string.Concat(WithoutUnseen(name ?? "").Split(invalid)).Trim().TrimEnd('.', ' ');
        if (clean.Length == 0) return "allegato";
        if (ReservedNames.Contains(clean.Split('.')[0].TrimEnd(' '))) clean = "_" + clean;
        return clean.Length > 150 ? clean[^150..] : clean;
    }

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
        if (name.Length > 150 || name[0] == ' ' || name[^1] is ' ' or '.') return false;
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
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root))
            {
                try
                {
                    var last = Directory.GetCreationTimeUtc(dir);
                    if (olderThan > TimeSpan.Zero)
                        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                            if (File.GetLastWriteTimeUtc(file) is var written && written > last) last = written;
                    if (olderThan <= TimeSpan.Zero || DateTime.UtcNow - last >= olderThan) Directory.Delete(dir, true);
                }
                catch { }
            }
        }
        catch { }
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

    /// <summary>Un suffisso come co.uk o com.au, che da solo non è il sito di nessuno.</summary>
    static bool IsSuffix(string[] labels) => labels.Length == 2 && labels[1].Length == 2 && SecondLevels.Contains(labels[0]);

    /// <summary>Dominio principale (unibo.it, paypal.com, bbc.co.uk).</summary>
    static string BaseDomain(string host)
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
