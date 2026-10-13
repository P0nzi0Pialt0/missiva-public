using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Missiva;

/// <summary>Perché un file non si apre come copia di sicurezza: ognuno ha la sua frase (BackupException.Message).</summary>
public enum BackupProblem
{
    /// <summary>Non è un file di Missiva (o è troppo corto per esserlo).</summary>
    NotABackup,
    /// <summary>Formato più nuovo di quello che questa Missiva legge.</summary>
    NewerFormat,
    /// <summary>Mancano dei byte in fondo: copia o trasferimento interrotti.</summary>
    Incomplete,
    /// <summary>La password non è quella della copia.</summary>
    WrongPassword,
    /// <summary>Intestazione non valida, o contenuto cambiato dopo l'esportazione (lo dice AES-GCM).</summary>
    Damaged,
}

public sealed class BackupException(BackupProblem problem, string message) : Exception(message)
{
    public BackupProblem Problem { get; } = problem;
}

/// <summary>
/// Il formato del file della copia di sicurezza (.missiva-copia), versione 1: un'intestazione in chiaro con il numero di
/// versione e i parametri, poi tutto cifrato. Con le classi di .NET: chiave da PBKDF2-HMAC-SHA256 (600.000 iterazioni,
/// sale casuale di 16 byte), AES-256-GCM con nonce casuali di 12 byte e tag di 16.
/// <code>
///   0   8  "MSVCOPIA"
///   8   2  versione del formato (1)
///  10   1  derivazione della chiave (1 = PBKDF2-HMAC-SHA256)
///  11   1  cifratura (1 = AES-256-GCM)
///  12   4  iterazioni
///  16   1  lunghezza del sale (16)      17  1  del nonce (12)      18  1  del tag (16)      19  1  zero
///  20   8  lunghezza del contenuto cifrato
///  28  16  sale
///  44  12  nonce del sigillo dell'intestazione
///  56  12  nonce del contenuto
///  68  16  sigillo dell'intestazione: il tag GCM di un contenuto vuoto, con i byte 0-67 come dati associati
///  84   N  contenuto cifrato (dati associati: i byte 0-83)
///  84+N 16 tag del contenuto
/// </code>
/// I numeri sono little-endian. Il sigillo dell'intestazione serve a dire "password sbagliata" senza confonderla con "file
/// rovinato": GCM da solo non distingue. In chiaro non c'è altro: né nomi, né date, né quanti account.
/// </summary>
public static class BackupFile
{
    public const int Version = 1;
    public const int Iterations = 600_000;
    /// <summary>
    /// Oltre, un file costruito apposta terrebbe ferma la finestra della password a derivare la chiave: con 20 milioni,
    /// il tetto di prima, 2,6 secondi a ogni tentativo su questo PC; con 5 milioni 0,6 (audit dell'11 ottobre, T-19).
    /// Missiva ne scrive 600.000: restano otto volte tanto per le versioni che verranno.
    /// </summary>
    const int MaxIterations = 5_000_000;
    const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32;
    const int Fixed = 28, Header = Fixed + SaltSize + NonceSize + NonceSize, Body = Header + TagSize;
    /// <summary>Il contenuto è compresso: non può superare di molto quello in chiaro, che ha il suo tetto (Backup.MaxPlain).</summary>
    const long MaxContent = Backup.MaxPlain + (1 << 20);
    static ReadOnlySpan<byte> Magic => "MSVCOPIA"u8;

    /// <summary>Il file: intestazione, sigillo, contenuto cifrato e tag. La chiave vive solo qui dentro e viene azzerata.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> content, ReadOnlySpan<char> password)
    {
        var file = new byte[Body + content.Length + TagSize];
        var header = file.AsSpan(0, Header);
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], Version);
        header[10] = 1;
        header[11] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], Iterations);
        header[16] = SaltSize;
        header[17] = NonceSize;
        header[18] = TagSize;
        BinaryPrimitives.WriteUInt64LittleEndian(header[20..], (ulong)content.Length);
        var salt = header.Slice(Fixed, SaltSize);
        Span<byte> headerNonce = header.Slice(Fixed + SaltSize, NonceSize), bodyNonce = header.Slice(Fixed + SaltSize + NonceSize, NonceSize);
        RandomNumberGenerator.Fill(header[Fixed..]);
        while (headerNonce.SequenceEqual(bodyNonce)) RandomNumberGenerator.Fill(bodyNonce); // mai lo stesso nonce due volte con una chiave
        var key = new byte[KeySize];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(password, salt, key, Iterations, HashAlgorithmName.SHA256);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(headerNonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, file.AsSpan(Header, TagSize), header);
            aes.Encrypt(bodyNonce, content, file.AsSpan(Body, content.Length), file.AsSpan(Body + content.Length, TagSize), file.AsSpan(0, Body));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return file;
    }

    /// <summary>
    /// Quello che si capisce senza la password: se è una copia di Missiva, di una versione che si legge, intera. Prima di
    /// chiedere la password, così un file sbagliato non la fa scrivere per niente.
    /// </summary>
    public static void Check(ReadOnlySpan<byte> file)
    {
        if (file.Length < Magic.Length || !file[..Magic.Length].SequenceEqual(Magic))
            throw new BackupException(BackupProblem.NotABackup, "Questo file non è una copia di sicurezza di Missiva.");
        if (file.Length < Fixed)
            throw new BackupException(BackupProblem.Incomplete, "Il file è incompleto: si ferma dopo pochi byte. Forse la copia o il trasferimento si sono interrotti: copialo di nuovo dal PC di partenza.");
        int version = BinaryPrimitives.ReadUInt16LittleEndian(file[8..]);
        if (version > Version)
            throw new BackupException(BackupProblem.NewerFormat,
                $"Questa copia di sicurezza è stata fatta con una versione più nuova di Missiva (formato {version}; questa Missiva legge fino al formato {Version}). " +
                "Aggiorna Missiva su questo PC e riprova: il file va bene così com'è.");
        uint iterations = BinaryPrimitives.ReadUInt32LittleEndian(file[12..]);
        ulong length = BinaryPrimitives.ReadUInt64LittleEndian(file[20..]);
        if (version != Version || file[10] != 1 || file[11] != 1 || file[16] != SaltSize || file[17] != NonceSize || file[18] != TagSize || file[19] != 0
            || iterations < Iterations || iterations > MaxIterations || length > MaxContent)
            throw new BackupException(BackupProblem.Damaged, "Il file è rovinato: la sua intestazione non è valida. Serve un'altra copia.");
        long expected = Body + (long)length + TagSize;
        if (file.Length < expected)
            throw new BackupException(BackupProblem.Incomplete,
                $"Il file è incompleto: mancano {expected - file.Length:N0} byte su {expected:N0}. Forse la copia o il trasferimento si sono interrotti: copialo di nuovo dal PC di partenza.");
        if (file.Length > expected)
            throw new BackupException(BackupProblem.Damaged, "Il file è più lungo di come Missiva l'ha scritto: è stato modificato dopo l'esportazione. Serve un'altra copia.");
    }

    /// <summary>Il contenuto in chiaro, o una BackupException che dice perché no. Chi chiama lo azzera quando ha finito.</summary>
    public static byte[] Unseal(ReadOnlySpan<byte> file, ReadOnlySpan<char> password)
    {
        Check(file);
        int iterations = (int)BinaryPrimitives.ReadUInt32LittleEndian(file[12..]);
        int length = (int)BinaryPrimitives.ReadUInt64LittleEndian(file[20..]);
        var key = new byte[KeySize];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(password, file.Slice(Fixed, SaltSize), key, iterations, HashAlgorithmName.SHA256);
            using var aes = new AesGcm(key, TagSize);
            try
            {
                aes.Decrypt(file.Slice(Fixed + SaltSize, NonceSize), ReadOnlySpan<byte>.Empty, file.Slice(Header, TagSize), Span<byte>.Empty, file[..Header]);
            }
            catch (CryptographicException)
            {
                throw new BackupException(BackupProblem.WrongPassword, "La password non è quella di questa copia. Controlla maiuscole, minuscole e il blocco delle maiuscole, e riprova.");
            }
            var content = new byte[length];
            try
            {
                aes.Decrypt(file.Slice(Fixed + SaltSize + NonceSize, NonceSize), file.Slice(Body, length), file.Slice(Body + length, TagSize), content, file[..Body]);
            }
            catch (CryptographicException)
            {
                CryptographicOperations.ZeroMemory(content);
                throw new BackupException(BackupProblem.Damaged,
                    "La password è giusta, ma il file è rovinato o è stato modificato dopo l'esportazione: il contenuto non corrisponde più al suo sigillo, e Missiva non lo importa. " +
                    "Serve un'altra copia del file.");
            }
            return content;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}

/// <summary>
/// Quello che c'è dentro una copia di sicurezza, in chiaro: esiste solo in memoria e, cifrato, nel file. Le impostazioni
/// sono il JSON di sempre ma senza nulla di cifrato con DPAPI (i testi in chiaro, gli accessi tolti); contatti e rubrica
/// sono il JSON dei loro file, decifrato.
/// </summary>
public sealed class BackupData
{
    public int Format { get; set; } = BackupFile.Version;
    /// <summary>Quando è stata fatta (UTC).</summary>
    public DateTime Created { get; set; }
    /// <summary>La versione di Missiva che l'ha fatta.</summary>
    public string App { get; set; } = "";
    public string Settings { get; set; } = "";
    /// <summary>Il segreto dell'app Google (non riservato per le app desktop, ma senza non si rientra negli account Gmail).</summary>
    public string? GoogleClientSecret { get; set; }
    /// <summary>Le password degli account IMAP, per identificativo dell'account: solo con la casella dell'esportazione.</summary>
    public Dictionary<string, string> ImapPasswords { get; set; } = new();
    public string? Contacts { get; set; }
    /// <summary>
    /// La rubrica dei destinatari. Nel file della copia c'è com'è sul PC; chi apre la copia per importarla la riceve con
    /// i soli nomi e indirizzi, senza conteggi e date (Backup.Validate, AddressBook.Imported: scelte del 12 ottobre, T-17).
    /// </summary>
    public string? AddressBook { get; set; }
    /// <summary>
    /// I domini noti dei loghi (domini.dat), nelle copie fatte fino all'11 ottobre 2026: si legge ancora ma non si importa
    /// più e non si scrive più (audit dell'11 ottobre, T-18). Dice da quando un dominio scrive e se è autenticato, cioè
    /// quando mostrarne il logo: una copia preparata da altri dava per noto da mesi un dominio appena nato. Ogni PC lo
    /// ricostruisce dalle date che i server danno ai messaggi (SenderLogos.Note).
    /// </summary>
    public string? KnownDomains { get; set; }
}

[JsonSerializable(typeof(BackupData))]
sealed partial class BackupJson : JsonSerializerContext;

/// <summary>Che cosa contiene una copia, per dirlo prima di importarla.</summary>
public sealed record BackupSummary(DateTime Created, IReadOnlyList<MailAccount> Accounts, int Signatures, int Templates, int Rules, int Contacts, int Groups,
                                   int AddressBook, int Snoozed, int Flags, int Passwords)
{
    static string Count(int n, string one, string many) => n == 1 ? one : $"{n} {many}";

    public string When => Created.ToLocalTime().ToString("d MMMM yyyy 'alle' HH:mm", System.Globalization.CultureInfo.GetCultureInfo("it-IT"));

    /// <summary>"2 account (mario@..., maria@...), 3 firme, ...": solo quello che c'è.</summary>
    public string Contents
    {
        get
        {
            var parts = new List<string>
            {
                Accounts.Count == 0 ? "nessun account" : Count(Accounts.Count, "1 account", "account") + " (" + string.Join(", ", Accounts.Select(a => a.Email)) + ")",
            };
            if (Signatures > 0) parts.Add(Count(Signatures, "1 firma", "firme"));
            if (Templates > 0) parts.Add(Count(Templates, "1 modello", "modelli"));
            if (Rules > 0) parts.Add(Count(Rules, "1 regola di Missiva", "regole di Missiva"));
            if (Contacts > 0) parts.Add(Count(Contacts, "1 contatto di Missiva", "contatti di Missiva"));
            if (Groups > 0) parts.Add(Count(Groups, "1 gruppo", "gruppi"));
            if (AddressBook > 0) parts.Add(Count(AddressBook, "1 indirizzo nella rubrica dei destinatari", "indirizzi nella rubrica dei destinatari"));
            if (Snoozed > 0) parts.Add(Count(Snoozed, "1 messaggio posposto", "messaggi posposti"));
            if (Flags > 0) parts.Add(Count(Flags, "1 contrassegno con data", "contrassegni con data"));
            return string.Join(", ", parts) + ", e le impostazioni";
        }
    }

    // Quello che la conferma dell'importazione dice in più (audit dell'11 ottobre, T-17). Prima mostrava indirizzi e numeri:
    // una copia preparata da altri poteva portare un altro server per la stessa casella, una regola che elimina tutto
    // quello che arriva o le immagini accese per interi servizi di posta, senza che nulla lo dicesse. Una riga per voce,
    // già accorciata (Backup.Describe).

    /// <summary>Ogni account con il suo tipo e, per IMAP, i server e l'utente.</summary>
    public IReadOnlyList<string> AccountLines { get; init; } = [];
    /// <summary>Le regole di Missiva in parole (Rules.Describe), con il loro account: prima quelle che valgono per tutti i messaggi e quelle che eliminano.</summary>
    public IReadOnlyList<string> RuleLines { get; init; } = [];
    /// <summary>I mittenti di cui mostrare sempre le immagini, come entreranno (Settings.DropBroadImageSenders).</summary>
    public IReadOnlyList<string> ImageSenders { get; init; } = [];
    /// <summary>
    /// Da guardare prima di importare: regole che valgono per tutti i messaggi, e account che ci sono già su questo PC ma
    /// che nella copia entrano da un'altra parte (altri server, un'altra app).
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Che cosa è successo all'avvio con un'importazione in attesa (Backup.ApplyPending): riuscita, con gli account che chiedono
/// di nuovo l'accesso e i file messi da parte, oppure no, con il motivo. Heading e Body sono l'avviso che App mostra.
/// </summary>
public sealed record BackupOutcome(bool Applied, DateTime Created, IReadOnlyList<MailAccount> NeedAccess, IReadOnlyList<string> SetAside, string? Error)
{
    public string Heading => Applied ? "Copia di sicurezza importata" : "Importazione non riuscita";

    public string Body
    {
        get
        {
            if (!Applied) return $"La copia di sicurezza non è stata importata: {Error}";
            var text = $"Missiva è ripartita con i dati della copia del {Created.ToLocalTime().ToString("d MMMM yyyy 'alle' HH:mm", System.Globalization.CultureInfo.GetCultureInfo("it-IT"))}.";
            if (NeedAccess.Count == 1)
                text += $"\n\nSu questo PC {NeedAccess[0].Email} chiede di nuovo l'accesso, perché la copia non contiene gli accessi: sotto l'account, nell'elenco delle cartelle, " +
                        "c'è la barra con Accedi di nuovo o Aggiorna password.";
            else if (NeedAccess.Count > 1)
                text += "\n\nSu questo PC questi account chiedono di nuovo l'accesso, perché la copia non contiene gli accessi: " + string.Join(", ", NeedAccess.Select(a => a.Email)) +
                        ". Sotto ogni account, nell'elenco delle cartelle, c'è la barra con Accedi di nuovo o Aggiorna password.";
            if (SetAside.Count > 0)
                text += "\n\nI dati che c'erano prima non sono stati cancellati: sono nella cartella dei dati di Missiva, con \"prima-dell-importazione\" e la data nel nome. " +
                        "Dentro ci sono anche gli accessi degli account di prima: quando non ti servono più si eliminano da Impostazioni, Generali, Copia di sicurezza.";
            foreach (var note in Notes) text += "\n\n" + note;
            if (Error != null) text += "\n\n" + Error;
            return text;
        }
    }

    /// <summary>Altro da sapere dopo un'importazione riuscita: le regole di Missiva arrivate accese, le voci non importate.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Copia di sicurezza (audit, F-29): tutto ciò che è dell'utente in un file solo, cifrato con una password sua
/// (BackupFile), da rimettere su questo PC o su un altro. Dentro: le impostazioni con firme, modelli, regole di Missiva,
/// categorie e colori, Preferiti e ordine delle cartelle, contrassegni e posposti (i testi cifrati con DPAPI decifrati,
/// perché su un altro PC DPAPI non li leggerebbe); i contatti e i gruppi di Missiva; la rubrica dei destinatari. Non la
/// posta (si riscarica), né i loghi e i loro domini noti (si ricostruiscono). Accessi: mai i token di Microsoft e di Google;
/// le password IMAP solo se chiesto. Gli invii pianificati no: sono lavoro in corso di quel PC, e una copia vecchia li
/// rifarebbe partire.
/// L'importazione non tocca nulla mentre Missiva gira: lascia nella cartella dei dati un file in attesa (cifrato con DPAPI)
/// che il prossimo avvio applica prima di leggere le impostazioni (ApplyPending), mettendo da parte i file di prima.
/// </summary>
public static class Backup
{
    public const string Extension = ".missiva-copia";
    internal const string PendingName = "importazione.dat", AsideLabel = "prima-dell-importazione";
    const string ContactsName = "contatti.dat", BookName = "rubrica.json";
    /// <summary>
    /// Quanto può essere grande il contenuto di una copia, in chiaro e prima della compressione: 64 MB. Una copia vera sta
    /// sotto il MB (una firma o un modello con un logo sono 100-200 KB). Il tetto di prima era 1 GB: un file di un mega
    /// costruito apposta, con la sua password, si espandeva fin lì, e prima di accorgersi che non era una copia Missiva
    /// chiedeva in tutto più di dieci volte tanto di memoria (audit dell'11 ottobre, T-19: con 100 MB, 1,1 GB). Vale anche
    /// per l'esportazione: una copia che poi non si potrebbe importare non si scrive.
    /// </summary>
    internal const int MaxPlain = 64 << 20;

    // ---------------------------------------------------------------- esportazione

    /// <summary>
    /// Raccoglie la copia dalle impostazioni date e dai file della cartella. notes: quello che non è potuto entrare (testi e
    /// file che su questo PC non si decifrano più), da dire dopo l'esportazione.
    /// </summary>
    public static BackupData Collect(Settings settings, string dir, bool withPasswords, out List<string> notes)
    {
        notes = new();
        var copy = ParseSettings(PortableJson(settings)); // un'altra istanza: quella di Missiva non si tocca
        var data = new BackupData { Created = DateTime.UtcNow, App = typeof(Backup).Assembly.GetName().Version?.ToString(3) ?? "" };

        // I testi che su questo PC non si decifrano più (ProtectedTextConverter.Open) non entrano: le regole e i modelli che
        // li contengono restano fuori, perché una regola con una parola sbagliata agirebbe sui messaggi sbagliati.
        int rules = copy.Rules.RemoveAll(r => r.Unreadable);
        int templates = copy.Templates.RemoveAll(t => new[] { t.Name, t.Subject, t.Rtf }.Any(ProtectedTextConverter.IsUnreadable));
        copy.RecentSearches.RemoveAll(ProtectedTextConverter.IsUnreadable);
        // Firme, mittenti delle immagini e calendari nascosti, cifrati dal blocco 10 (S-26): il segnaposto non entra nella copia.
        int signatures = copy.Signatures.Values.Sum(s => s.DropUnreadable());
        copy.ImageSenders.RemoveAll(ProtectedTextConverter.IsUnreadable);
        copy.HiddenCalendars.RemoveAll(ProtectedTextConverter.IsUnreadable);
        static string Readable(string text) => ProtectedTextConverter.IsUnreadable(text) ? "" : text;
        foreach (var s in copy.Snoozed) (s.Subject, s.From) = (Readable(s.Subject), Readable(s.From));
        foreach (var f in copy.Flags) (f.Subject, f.From) = (Readable(f.Subject), Readable(f.From));
        if (rules > 0) notes.Add(rules == 1 ? "Una regola di Missiva ha parti che su questo PC non si leggono più: non è nella copia." : $"{rules} regole di Missiva hanno parti che su questo PC non si leggono più: non sono nella copia.");
        if (templates > 0) notes.Add(templates == 1 ? "Un modello su questo PC non si legge più: non è nella copia." : $"{templates} modelli su questo PC non si leggono più: non sono nella copia.");
        if (signatures > 0) notes.Add(signatures == 1 ? "Una firma su questo PC non si legge più: non è nella copia." : $"{signatures} firme su questo PC non si leggono più: non sono nella copia.");
        copy.ScheduledSends.Clear();
        copy.UnreadableTold = false;

        // Accessi. Il segreto dell'app Google entra (senza non si rifà l'accesso agli account Gmail); i token mai; le password
        // IMAP solo se chiesto. Nel JSON delle impostazioni della copia non resta nessun valore cifrato con DPAPI.
        if (copy.ProtectedGoogleClientSecret != null)
        {
            if (Store.TryUnprotect(copy.ProtectedGoogleClientSecret, out var secret)) data.GoogleClientSecret = secret;
            else notes.Add("Il segreto dell'app Google su questo PC non si legge più: non è nella copia, andrà riscritto in Impostazioni, Account.");
            copy.ProtectedGoogleClientSecret = null;
        }
        foreach (var account in copy.Accounts)
        {
            account.HomeAccountId = null;
            account.ProtectedRefreshToken = null;
            if (account.Imap is not { } server) continue;
            if (withPasswords && server.ProtectedPassword != null)
            {
                if (Store.TryUnprotect(server.ProtectedPassword, out var password)) data.ImapPasswords[account.Id] = password;
                else notes.Add($"La password di {account.Email} su questo PC non si legge più: non è nella copia.");
            }
            server.ProtectedPassword = null;
        }
        data.Settings = PortableJson(copy);

        data.Contacts = ReadFile(Path.Combine(dir, ContactsName), "I contatti di Missiva", notes, Unprotected);
        // La rubrica scritta in chiaro dalle prime versioni comincia con una graffa (AddressBook.Ensure).
        data.AddressBook = ReadFile(Path.Combine(dir, BookName), "La rubrica dei destinatari", notes, raw => raw[0] == (byte)'{' ? Encoding.UTF8.GetString(raw) : Unprotected(raw));
        return data;
    }

    /// <summary>Il testo di un file cifrato con DPAPI; i byte in chiaro si azzerano subito.</summary>
    static string Unprotected(byte[] raw)
    {
        var plain = Store.UnprotectBytes(raw);
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    static string? ReadFile(string path, string what, List<string> notes, Func<byte[], string> open)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var raw = File.ReadAllBytes(path);
            if (raw.Length == 0) return null;
            var json = open(raw);
            using var parsed = JsonDocument.Parse(json); // un file che non è JSON non entra nella copia
            return json;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or IOException or UnauthorizedAccessException)
        {
            notes.Add($"{what} su questo PC non si leggono: non sono nella copia.");
            Log.Write($"copia di sicurezza: {Path.GetFileName(path)} non letto ({ex.GetType().Name})");
            return null;
        }
    }

    /// <summary>Il file da scrivere: il contenuto compresso e cifrato con la password. I vettori in chiaro si azzerano qui.</summary>
    public static byte[] Write(BackupData data, ReadOnlySpan<char> password)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(data, BackupJson.Default.BackupData), packed = [];
        try
        {
            if (json.Length > MaxPlain)
                throw new InvalidOperationException($"i dati da copiare superano i {MaxPlain >> 20} MB che una copia di sicurezza può tenere. " +
                                                    "Di solito sono firme o modelli con immagini molto grandi: alleggeriscili e riprova.");
            packed = Compress(json);
            return BackupFile.Seal(packed, password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
            CryptographicOperations.ZeroMemory(packed);
        }
    }

    // ---------------------------------------------------------------- lettura

    /// <summary>
    /// Apre una copia con la password. BackupException con la frase giusta per: file che non è una copia, formato più nuovo,
    /// file incompleto, password sbagliata, file rovinato o modificato. Quello che torna è già controllato: le impostazioni e
    /// le altre parti si leggono.
    /// </summary>
    public static BackupData Read(ReadOnlySpan<byte> file, ReadOnlySpan<char> password)
    {
        var packed = BackupFile.Unseal(file, password);
        try
        {
            return Parse(packed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(packed);
        }
    }

    static BackupData Parse(byte[] packed)
    {
        byte[] json = [];
        try
        {
            json = Decompress(packed);
            var data = JsonSerializer.Deserialize(json, BackupJson.Default.BackupData) ?? throw new JsonException("vuoto");
            if (data.Format > BackupFile.Version)
                throw new BackupException(BackupProblem.NewerFormat,
                    $"Questa copia di sicurezza è stata fatta con una versione più nuova di Missiva (formato {data.Format}). Aggiorna Missiva su questo PC e riprova.");
            Validate(data);
            return data;
        }
        catch (Exception ex) when (ex is not (BackupException or OutOfMemoryException))
        {
            // Non solo il JSON che non si legge: qualunque cosa esca da un contenuto che non è quello di Missiva (un tipo al
            // posto di un altro, una proprietà scritta due volte) è un file che non si importa, mai un'eccezione che arriva
            // a chi chiama: all'avvio chiuderebbe Missiva (audit dell'11 ottobre, T-16).
            throw new BackupException(BackupProblem.Damaged,
                $"Il contenuto della copia non si legge ({ex.GetType().Name}): viene da una versione di Missiva più nuova di questa, oppure è rovinato. Aggiorna Missiva e riprova; se non basta serve un'altra copia.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    /// <summary>
    /// Il contenuto appena letto, controllato prima che qualcuno lo usi (audit dell'11 ottobre, T-16). Una copia costruita a
    /// mano con un null dove Missiva non lo scrive mai (la password di un account, un account, l'elenco dei mittenti delle
    /// immagini) passava di qui: all'avvio dopo, l'importazione o la lettura delle impostazioni appena importate chiudevano
    /// Missiva prima della finestra, a ogni avvio. Ora: le impostazioni si leggono con ParseSettings (le proprietà null
    /// valgono come assenti, una voce null in un elenco fa rifiutare la copia); ogni account ha un identificativo valido
    /// (T-14) e un indirizzo; le password sono solo di account della copia con un server IMAP; i contatti si riscrivono
    /// come li scrive Missiva, e la rubrica dei destinatari anche, senza i conteggi (AddressBook.Imported).
    /// </summary>
    static void Validate(BackupData data)
    {
        data.App ??= "";
        data.ImapPasswords ??= new();
        if (data.Settings == null) throw new JsonException("senza impostazioni");
        var settings = ParseSettings(data.Settings);
        // Gli identificativi degli account diventano nomi di cartelle e chiavi: una copia costruita apposta con
        // "..\..\Documenti" o un percorso intero non entra (audit dell'11 ottobre, T-14).
        if (MailAccount.Problem(settings.Accounts) is { } problem) throw Foreign(problem);
        if (settings.Accounts.Any(a => a.Email.Trim().Length == 0)) throw Foreign("un account è senza indirizzo");
        var withServer = settings.Accounts.Where(a => a.Imap != null).Select(a => a.Id).ToHashSet();
        if (data.ImapPasswords.Any(p => p.Value == null || !withServer.Contains(p.Key))) throw Foreign("una password non è di un account IMAP della copia");
        if (data.Contacts != null)
        {
            var contacts = (JsonNode.Parse(data.Contacts) ?? throw new JsonException("contatti vuoti")).AsArray();
            StripNulls(contacts);
            data.Contacts = JsonSerializer.Serialize(contacts.Deserialize(ContactsJson.Default.ListContact) ?? new(), ContactsJson.Default.ListContact);
        }
        if (data.AddressBook != null)
        {
            // La rubrica entra senza i conteggi dei messaggi scritti e ricevuti (scelte del 12 ottobre, T-17): solo nomi e
            // indirizzi, riscritti come li scrive Missiva (AddressBook.Imported), con la regola dei null di tutto il resto.
            var book = JsonNode.Parse(data.AddressBook) as JsonObject ?? throw new JsonException("non è un oggetto");
            StripNulls(book);
            data.AddressBook = AddressBook.Imported(book.ToJsonString());
        }
        data.KnownDomains = null; // quello delle copie di prima non si importa (T-18), e non resta nel file in attesa
    }

    /// <summary>
    /// Toglie le proprietà scritte null, a ogni livello: per chi legge valgono come assenti, e resta il valore di base
    /// (un elenco vuoto, un testo vuoto; null solo dove null è già il valore di base). Così nelle impostazioni importate
    /// non c'è un null dove il resto di Missiva non se lo aspetta. Una voce null dentro un elenco non si può mettere a
    /// posto così, e Missiva non la scrive mai: la copia si rifiuta (Foreign).
    /// </summary>
    static void StripNulls(JsonNode node)
    {
        if (node is JsonObject map)
        {
            foreach (var (key, value) in map.ToList())
                if (value == null) map.Remove(key);
                else StripNulls(value);
        }
        else if (node is JsonArray list)
            foreach (var item in list)
                StripNulls(item ?? throw Foreign("un elenco ha una voce vuota"));
    }

    /// <summary>
    /// La copia si apre con la sua password, ma dentro non c'è quello che Missiva scrive: è stata costruita o cambiata da chi
    /// conosce la password. Si rifiuta come un file rovinato, dicendo perché.
    /// </summary>
    static BackupException Foreign(string why) => new(BackupProblem.Damaged,
        $"Il contenuto della copia non è quello che Missiva scrive ({why}): il file è stato costruito o cambiato da altri, e Missiva non lo importa.");

    /// <param name="current">Le impostazioni di questo PC, per dire nella conferma che cosa la copia cambia degli account che ci sono già; null per non confrontare.</param>
    public static BackupSummary Describe(BackupData data, Settings? current = null)
    {
        var settings = ParseSettings(data.Settings);
        settings.DropBroadImageSenders(); // come entreranno (Apply)
        var contacts = data.Contacts == null ? new() : JsonSerializer.Deserialize(data.Contacts, ContactsJson.Default.ListContact) ?? new();
        int book = 0;
        if (data.AddressBook != null)
        {
            using var parsed = JsonDocument.Parse(data.AddressBook);
            if (parsed.RootElement.TryGetProperty("Entries", out var entries) && entries.ValueKind == JsonValueKind.Array) book = entries.GetArrayLength();
        }
        return new BackupSummary(data.Created, settings.Accounts,
            settings.Signatures.Values.Sum(s => s.All().Count(x => x.Rtf.Length > 0)), settings.Templates.Count, settings.Rules.Count,
            contacts.Count(c => !c.IsGroup), contacts.Count(c => c.IsGroup), book, settings.Snoozed.Count, settings.Flags.Count, data.ImapPasswords.Count)
        {
            AccountLines = settings.Accounts.Select(a => AccountLine(settings, a)).ToList(),
            RuleLines = RuleLines(settings),
            ImageSenders = settings.ImageSenders.Select(s => Short(s, 80)).ToList(),
            Warnings = Warnings(settings, current),
        };
    }

    /// <summary>
    /// Un testo della copia su una riga sola, senza i caratteri che non si vedono o che girano il testo (SenderCheck.CleanName)
    /// e non più lungo di tanto: quello che arriva da un file può essere qualunque cosa, e qui lo si mostra per decidere.
    /// </summary>
    static string Short(string text, int max = 240)
    {
        var line = SenderCheck.CleanName(text);
        return line.Length <= max ? line : line[..max] + "…";
    }

    static string Server(string host, int port) => $"{Short(host, 80)}:{port}";

    static string AccountLine(Settings settings, MailAccount account)
    {
        string kind = account.SharedOf != null
            ? "casella condivisa di Microsoft 365" + (settings.Accounts.FirstOrDefault(a => a.Id == account.SharedOf) is { } owner ? $", aperta con {owner.Email}" : "")
            : account.Kind switch
            {
                AccountKind.Microsoft when account.Tenant == null => "Microsoft (Outlook.com)",
                AccountKind.Microsoft => "Microsoft 365" + (account.Tenant != MicrosoftAuth.Organizations ? $", directory {account.Tenant}" : "") + (account.ClientId != null ? $", app {account.ClientId}" : ""),
                AccountKind.Gmail => AccountTypes.IsGmailDomain(account.Email) ? "Gmail" : "Google Workspace",
                _ when account.Imap is { } server => $"IMAP, posta in arrivo da {Server(server.ImapHost, server.ImapPort)}, in uscita da {Server(server.SmtpHost, server.SmtpPort)}" +
                                                    (server.UserName.Length > 0 && !server.UserName.Equals(account.Email, StringComparison.OrdinalIgnoreCase) ? $", utente {server.UserName}" : ""),
                _ => "IMAP, senza server",
            };
        return Short($"{account.Email}: {kind}{(settings.NotificationsOff.Contains(account.Id) ? ", notifiche spente" : "")}", 400);
    }

    static string OwnerOf(Settings settings, MailRule rule) => settings.Accounts.FirstOrDefault(a => a.Id == rule.AccountId)?.Email ?? "un account che nella copia non c'è";

    /// <summary>Una regola accesa, senza condizioni e con un'azione: vale per ogni messaggio che arriva.</summary>
    static bool ForEveryMessage(MailRule rule) => rule.Enabled && rule.HasActions && !rule.HasConditions;

    const int MaxRuleLines = 12;

    static List<string> RuleLines(Settings settings)
    {
        // Prima quelle che fanno più danno se non le si è volute: per tutti i messaggi, poi elimina, poi segna come letto.
        var ordered = settings.Rules.OrderBy(r => ForEveryMessage(r) ? 0 : r.Delete ? 1 : r.MarkRead ? 2 : 3).ToList();
        var lines = ordered.Take(MaxRuleLines).Select(r => Short($"{OwnerOf(settings, r)}: {Rules.Describe(r)}{(r.Enabled ? "" : " (spenta)")}", 400)).ToList();
        if (ordered.Count > MaxRuleLines) lines.Add($"e altre {ordered.Count - MaxRuleLines}: dopo l'importazione sono in Impostazioni, Posta, Regole");
        return lines;
    }

    static List<string> Warnings(Settings incoming, Settings? current)
    {
        var lines = new List<string>();
        foreach (var rule in incoming.Rules.Where(ForEveryMessage))
        {
            var words = Rules.Describe(rule); // "Per tutti i messaggi: elimina (nel Cestino), ...": qui servono le azioni
            lines.Add(Short($"Una regola di {OwnerOf(incoming, rule)} vale per tutti i messaggi che arrivano: {words[(words.IndexOf(": ", StringComparison.Ordinal) + 2)..]}.", 400));
        }
        if (current == null) return lines;
        bool google = false;
        foreach (var account in incoming.Accounts)
        {
            if (current.Accounts.FirstOrDefault(a => a.Id == account.Id && a.Kind == account.Kind && a.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase)) is not { } mine) continue;
            if (account.Kind == AccountKind.Imap && account.Imap is { } theirs && mine.Imap is { } here && !SameServer(here, theirs))
                lines.Add(Short($"{account.Email} su questo PC c'è già, con altri server: qui {Server(here.ImapHost, here.ImapPort)} e {Server(here.SmtpHost, here.SmtpPort)}, utente {here.UserName}; " +
                                $"nella copia {Server(theirs.ImapHost, theirs.ImapPort)} e {Server(theirs.SmtpHost, theirs.SmtpPort)}, utente {theirs.UserName}. La password di questo PC non va ai server della copia.", 600));
            else if (account.Kind == AccountKind.Microsoft && account.SharedOf == null && MicrosoftApp(current, mine) != MicrosoftApp(incoming, account))
                lines.Add(Short($"{account.Email} su questo PC c'è già, ma nella copia entra in Microsoft con un'altra app o un'altra directory: l'accesso di questo PC non vale, e lo chiederà di nuovo.", 400));
            else if (account.Kind == AccountKind.Gmail && current.GoogleClientId != incoming.GoogleClientId) google = true;
        }
        if (google) lines.Add("Gli account Google che su questo PC ci sono già nella copia usano un altro client di Google: l'accesso di questo PC non vale, e lo chiederanno di nuovo.");
        return lines;
    }

    // ---------------------------------------------------------------- le impostazioni senza DPAPI

    /// <summary>Le impostazioni come JSON con i testi in chiaro (ProtectedTextConverter.Portable): solo per il contenuto della copia.</summary>
    internal static string PortableJson(Settings settings)
    {
        ProtectedTextConverter.Portable = true;
        try { return JsonSerializer.Serialize(settings, SettingsJson.Default.Settings); }
        finally { ProtectedTextConverter.Portable = false; }
    }

    /// <summary>
    /// Le impostazioni di una copia, dal loro JSON con i testi in chiaro. Le proprietà scritte null valgono come assenti
    /// (StripNulls), e ogni account deve avere scritti il suo identificativo e il suo indirizzo: senza, ne nascerebbe uno
    /// nuovo a ogni lettura.
    /// </summary>
    internal static Settings ParseSettings(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("le impostazioni non sono un oggetto");
        StripNulls(node);
        if (node["Accounts"] is JsonArray accounts && accounts.Any(a => a is not JsonObject account || account["Id"] is not JsonValue || account["Email"] is not JsonValue))
            throw Foreign("un account è senza identificativo o senza indirizzo");
        ProtectedTextConverter.Portable = true;
        try { return node.Deserialize(SettingsJson.Default.Settings) ?? throw new JsonException("impostazioni vuote"); }
        finally { ProtectedTextConverter.Portable = false; }
    }

    static byte[] Compress(byte[] plain)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(plain);
        var result = packed.ToArray();
        CryptographicOperations.ZeroMemory(packed.GetBuffer());
        return result;
    }

    /// <summary>
    /// Al massimo MaxPlain. In due passate: la prima conta soltanto, senza tenere nulla, e si ferma appena supera il tetto;
    /// la seconda riempie un vettore della misura giusta. Prima un MemoryStream che raddoppiava e poi si copiava: per
    /// accorgersi che un contenuto era troppo grande ne teneva in memoria più del doppio.
    /// </summary>
    static byte[] Decompress(byte[] packed)
    {
        var buffer = new byte[81920];
        try
        {
            int length = 0;
            using (var gzip = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress))
                for (int read; (read = gzip.Read(buffer)) > 0;)
                    if ((length += read) > MaxPlain) throw new InvalidDataException("contenuto troppo grande");
            var plain = new byte[length];
            try
            {
                using var gzip = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress);
                gzip.ReadExactly(plain);
                return plain;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(plain);
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    // ---------------------------------------------------------------- importazione

    public static bool HasPending(string dir) => File.Exists(Path.Combine(dir, PendingName));

    /// <summary>
    /// La copia aperta, pronta per il prossimo avvio: nella cartella dei dati, cifrata con DPAPI come le impostazioni (la
    /// password della copia non serve più e non si scrive da nessuna parte). Finché Missiva gira non cambia altro.
    /// </summary>
    public static void SavePending(string dir, BackupData data)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(data, BackupJson.Default.BackupData), packed = [];
        try
        {
            packed = Compress(json);
            Directory.CreateDirectory(dir);
            SafeFile.Write(Path.Combine(dir, PendingName), Store.ProtectBytes(packed));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
            CryptographicOperations.ZeroMemory(packed);
        }
    }

    /// <summary>L'importazione in attesa, se c'è e si legge (per dire di quando è la copia); null altrimenti.</summary>
    public static BackupData? Pending(string dir)
    {
        try
        {
            var path = Path.Combine(dir, PendingName);
            if (!File.Exists(path)) return null;
            var packed = Store.UnprotectBytes(File.ReadAllBytes(path));
            try { return Parse(packed); }
            finally { CryptographicOperations.ZeroMemory(packed); }
        }
        catch (Exception ex) when (ex is BackupException or CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void CancelPending(string dir)
    {
        try { File.Delete(Path.Combine(dir, PendingName)); }
        catch (Exception ex) { Log.Error("importazione in attesa non tolta", ex); }
    }

    // ---------------------------------------------------------------- i dati di prima di un'importazione

    /// <summary>
    /// Il nome che SafeFile.SetAside dà, con l'etichetta delle importazioni, a uno dei quattro file che un'importazione
    /// sostituisce: "impostazioni-prima-dell-importazione-20261012-093000.json" (anche .bak), "contatti-...dat",
    /// "rubrica-...json", con "-2", "-3" in fondo se nello stesso secondo ce n'era già uno.
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex AsideName = new(
        "^(?:" + string.Join("|", new[] { Store.FileName, Store.BackupName, ContactsName, BookName }.Select(name =>
            $"{System.Text.RegularExpressions.Regex.Escape(Path.GetFileNameWithoutExtension(name))}-{AsideLabel}-(?<quando>[0-9]{{8}}-[0-9]{{6}})(?:-[0-9]{{1,2}})?{System.Text.RegularExpressions.Regex.Escape(Path.GetExtension(name))}")) + ")$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// I file che le importazioni hanno messo da parte nella cartella dei dati (Apply): le impostazioni di prima con la
    /// loro copia di riserva, i contatti di Missiva e la rubrica. Missiva non li tocca più e non li toglie da sola: servono
    /// a tornare indietro. Ma nelle impostazioni di prima ci sono ancora gli accessi degli account di allora (la password
    /// IMAP, il token di Google), cifrati per questo utente di Windows (audit dell'11 ottobre, T-20): dalle scelte del 12
    /// ottobre 2026 la pagina Copia di sicurezza li mostra e li lascia eliminare (DeleteAside). Solo i file con quei
    /// nomi, e solo nella cartella stessa: mai una sottocartella, mai un file che gli somiglia.
    /// </summary>
    public static List<string> AsideFiles(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return [];
            return Directory.EnumerateFiles(dir, $"*-{AsideLabel}-*").Where(path => AsideName.IsMatch(Path.GetFileName(path))).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("copia di sicurezza: file messi da parte non elencati", ex);
            return [];
        }
    }

    /// <summary>Quando è stato messo da parte un file di AsideFiles (dal suo nome), o null se il nome non lo dice.</summary>
    public static DateTime? AsideDate(string path) =>
        AsideName.Match(Path.GetFileName(path)) is { Success: true } match
        && DateTime.TryParseExact(match.Groups["quando"].Value, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when) ? when : null;

    /// <summary>
    /// Elimina i file messi da parte dalle importazioni (AsideFiles), per sempre: non vanno nel Cestino, perché lo scopo è
    /// che gli accessi di prima non restino sul disco. Chi chiama ha già chiesto conferma. Quanti ne ha tolti, e i nomi di
    /// quelli che non si sono lasciati togliere (in uso da un altro programma): restano, e si riprova.
    /// </summary>
    public static (int Deleted, List<string> Failed) DeleteAside(string dir)
    {
        int deleted = 0;
        var failed = new List<string>();
        foreach (var path in AsideFiles(dir))
        {
            try
            {
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(Path.GetFileName(path));
                Log.Error($"copia di sicurezza: {Path.GetFileName(path)} non eliminato", ex);
            }
        }
        Log.Write($"copia di sicurezza: eliminati {deleted} file di prima dell'importazione{(failed.Count > 0 ? $", {failed.Count} non si sono lasciati eliminare" : "")}");
        return (deleted, failed);
    }

    /// <summary>
    /// All'avvio, prima che Missiva legga qualunque cosa: se c'è un'importazione in attesa, i file di adesso (impostazioni e
    /// la loro copia di riserva, contatti, rubrica) vanno da parte con "prima-dell-importazione" e la data nel
    /// nome, e al loro posto vanno quelli della copia, cifrati con DPAPI per questo utente di Windows. Null se non c'era
    /// nulla in attesa. Prima si scrivono tutti i file nuovi accanto, con un altro nome; solo dopo si spostano: un disco
    /// pieno non lascia le cose a metà. Sostituisce tutto, senza unire; due sole cose restano di questo PC, perché non sono
    /// dati ma lavoro e accessi suoi: gli invii pianificati, e gli accessi degli account che c'erano già con lo stesso
    /// identificativo, lo stesso indirizzo e gli stessi server o la stessa app (Merge: rimettendo una copia sullo stesso PC
    /// non serve rientrare). Non solleva eccezioni: un'importazione che non si può applicare va da parte, e lo dice l'esito.
    /// </summary>
    public static BackupOutcome? ApplyPending(string dir)
    {
        var pending = Path.Combine(dir, PendingName);
        if (!File.Exists(pending)) return null;
        try
        {
            return Apply(dir, pending);
        }
        catch (Exception ex)
        {
            // Mai un'eccezione da qui: risalirebbe dal costruttore di App e chiuderebbe Missiva prima della finestra, e con
            // il file in attesa ancora lì succederebbe a ogni avvio (audit dell'11 ottobre, T-16). Il file va da parte.
            Log.Error("importazione della copia di sicurezza", ex);
            SafeFile.SetAside(pending);
            return new BackupOutcome(false, default, [], [], $"l'importazione si è fermata per un errore che Missiva non si aspettava ({ex.GetType().Name}). Controlla account e impostazioni: " +
                $"se qualcosa era già stato sostituito, i file di prima sono nella cartella dei dati di Missiva, con \"{AsideLabel}\" nel nome.");
        }
    }

    static BackupOutcome Apply(string dir, string pending)
    {
        string settingsPath = Path.Combine(dir, Store.FileName), backupPath = Path.Combine(dir, Store.BackupName);
        BackupData data;
        Settings incoming;
        List<MailAccount> needAccess;
        var notes = new List<string>();
        var files = new List<(string Path, byte[]? Bytes)>();
        try
        {
            // Tutto quello che si fa con il contenuto prima di toccare il disco sta qui dentro: letto, unito agli accessi di
            // questo PC e scritto in memoria. Prima Merge e la scrittura in memoria erano fuori, e una password null o un
            // account senza identificativo erano un'eccezione all'avvio (audit dell'11 ottobre, T-16).
            var packed = Store.UnprotectBytes(File.ReadAllBytes(pending));
            try { data = Parse(packed); }
            finally { CryptographicOperations.ZeroMemory(packed); }
            incoming = ParseSettings(data.Settings);
            int broad = incoming.DropBroadImageSenders();
            if (broad > 0)
                notes.Add((broad == 1 ? "Una voce dell'elenco dei mittenti di cui mostrare sempre le immagini non è stata importata: valeva" : $"{broad} voci dell'elenco dei mittenti di cui mostrare sempre le immagini non sono state importate: valevano") +
                          " per chiunque scriva da un intero servizio di posta o da un intero suffisso (come \"co.uk\"), e Missiva non ne scrive di così.");
            if (incoming.Rules.Count(r => r.Enabled) is > 0 and var active)
                notes.Add(active == 1 ? "La copia aveva una regola di Missiva accesa: vale da subito per i messaggi che arrivano. È in Impostazioni, Posta, Regole."
                    : $"La copia aveva {active} regole di Missiva accese: valgono da subito per i messaggi che arrivano. Sono in Impostazioni, Posta, Regole.");
            var current = File.Exists(settingsPath) && Store.TryParse(settingsPath, out var now, out _) ? now : null;
            needAccess = Merge(incoming, data, current);
            static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
            var json = JsonSerializer.SerializeToUtf8Bytes(incoming, SettingsJson.Default.Settings);
            // Le impostazioni che stanno per andare al loro posto si rileggono come farà l'avvio (Store.Load): se non si
            // leggono non ci vanno, e Missiva riparte con quelle di prima invece di non ripartire più.
            var again = JsonSerializer.Deserialize(json, SettingsJson.Default.Settings) ?? throw new JsonException("impostazioni vuote");
            if (MailAccount.Problem(again.Accounts) is { } problem) throw new JsonException(problem);
            again.UpgradeImageSenders();
            files.Add((settingsPath, json)); // per prime: se non si spostano non cambia nulla
            files.Add((Path.Combine(dir, ContactsName), data.Contacts == null ? null : Store.ProtectBytes(Utf8(data.Contacts))));
            files.Add((Path.Combine(dir, BookName), data.AddressBook == null ? null : Store.ProtectBytes(Utf8(data.AddressBook))));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SafeFile.SetAside(pending);
            Log.Write($"copia di sicurezza: importazione in attesa non applicata ({(ex as BackupException)?.Problem.ToString() ?? ex.GetType().Name}), file messo da parte");
            return new BackupOutcome(false, default, [], [], $"il file dell'importazione in attesa non si leggeva ({(ex is BackupException ? "il contenuto non è valido" : ex.GetType().Name)}). " +
                "Non è cambiato nulla: rifai l'importazione da Impostazioni, Generali, Copia di sicurezza.");
        }

        var staged = new List<string>();
        try
        {
            foreach (var (path, bytes) in files)
            {
                if (bytes == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                staged.Add(path + ".importato");
                using var stream = new FileStream(path + ".importato", FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.None);
                stream.Write(bytes);
                stream.Flush(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            foreach (var path in staged) try { File.Delete(path); } catch { }
            CancelPending(dir);
            return new BackupOutcome(false, data.Created, [], [], $"i file nuovi non si sono potuti scrivere ({ex.Message}). Non è cambiato nulla: rifai l'importazione da Impostazioni, Generali, Copia di sicurezza.");
        }

        var aside = new List<string>();
        string? trouble = null;
        foreach (var (path, bytes) in files)
        {
            string? moved = null;
            try
            {
                if (File.Exists(path))
                    moved = Retry(() => SafeFile.SetAside(path, AsideLabel)) ?? throw new IOException("non si lascia spostare, forse è in uso da un altro programma");
                if (bytes != null) Retry(() => { File.Move(path + ".importato", path); return path; });
                if (moved != null) aside.Add(Path.GetFileName(moved));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Il file di prima torna al suo posto, se era già da parte: di questo file non cambia nulla.
                if (moved != null && !File.Exists(path)) try { File.Move(moved, path); } catch { aside.Add(Path.GetFileName(moved)); }
                try { File.Delete(path + ".importato"); } catch { }
                if (path == settingsPath)
                {
                    // Le impostazioni non si sostituiscono: ci si ferma prima di toccare il resto.
                    foreach (var other in staged) try { File.Delete(other); } catch { }
                    CancelPending(dir);
                    return new BackupOutcome(false, data.Created, [], [], $"il file delle impostazioni di adesso non si è potuto sostituire ({ex.Message}). Non è cambiato nulla: chiudi Missiva, riaprila e rifai l'importazione.");
                }
                trouble = (trouble == null ? "Attenzione: " : trouble + " ") + $"{Path.GetFileName(path)} non è stato sostituito ({ex.Message}).";
            }
        }
        // La copia di riserva delle impostazioni di prima non deve tornare al posto di quelle importate (Store.Read): da parte anche lei.
        if (File.Exists(backupPath) && SafeFile.SetAside(backupPath, AsideLabel) is { } oldBackup) aside.Add(Path.GetFileName(oldBackup));
        CancelPending(dir);
        Log.Write($"copia di sicurezza importata: {incoming.Accounts.Count} account, {needAccess.Count} chiedono di nuovo l'accesso; messi da parte: {string.Join(", ", aside)}{(trouble != null ? "; " + trouble : "")}");
        return new BackupOutcome(true, data.Created, needAccess, aside, trouble) { Notes = notes };
    }

    /// <summary>Un antivirus o l'indicizzazione che tengono un file aperto per un attimo: qualche tentativo.</summary>
    static string? Retry(Func<string?> move)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var done = move();
                if (done != null || attempt >= 3) return done;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 3) { }
            Thread.Sleep(100);
        }
    }

    /// <summary>
    /// Gli accessi nelle impostazioni che arrivano: le password IMAP e il segreto dell'app Google della copia cifrati per
    /// questo utente di Windows; per il resto quelli che questo PC ha già per lo stesso account (stesso identificativo, tipo
    /// e indirizzo), ma solo se la copia lo fa entrare dalla stessa parte: stessi server e stesso utente per IMAP, stessa
    /// app e stessa directory per Microsoft, stesso client per Google. Prima bastavano identificativo e indirizzo: una copia
    /// "sistemata" da altri, con il server IMAP cambiato, al riavvio mandava a quel server la password di questo PC (audit
    /// dell'11 ottobre, T-15). Gli altri account restano senza accesso: tornano quelli che lo devono rifare.
    /// </summary>
    static List<MailAccount> Merge(Settings incoming, BackupData data, Settings? current)
    {
        incoming.ProtectedGoogleClientSecret = data.GoogleClientSecret is { Length: > 0 } secret ? Store.Protect(secret)
            : current != null && current.GoogleClientId == incoming.GoogleClientId ? current.ProtectedGoogleClientSecret : null;
        var needAccess = new List<MailAccount>();
        foreach (var account in incoming.Accounts)
        {
            var same = current?.Accounts.FirstOrDefault(a => a.Id == account.Id && a.Kind == account.Kind && a.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase));
            // I tre accessi partono vuoti, qualunque cosa la copia abbia scritto in quei campi: Missiva non ce li mette mai,
            // e un cifrato preso altrove non deve diventare l'accesso di un account.
            string? home = null, token = null, password = null;
            switch (account.Kind)
            {
                case AccountKind.Microsoft:
                    if (same != null && MicrosoftApp(current!, same) == MicrosoftApp(incoming, account)) home = same.HomeAccountId;
                    break;
                case AccountKind.Gmail:
                    if (same != null && current!.GoogleClientId == incoming.GoogleClientId) token = same.ProtectedRefreshToken;
                    break;
                default:
                    if (account.Imap is not { } server) break;
                    password = data.ImapPasswords.TryGetValue(account.Id, out var copied) ? Store.Protect(copied)
                        : same?.Imap is { } mine && SameServer(mine, server) ? mine.ProtectedPassword : null;
                    break;
            }
            account.HomeAccountId = home;
            account.ProtectedRefreshToken = token;
            if (account.Imap != null) account.Imap.ProtectedPassword = password;
            bool has = account.Kind switch
            {
                AccountKind.Microsoft => home != null || account.SharedOf != null, // una casella condivisa entra con l'account di lavoro
                AccountKind.Gmail => token != null,
                _ => account.Imap == null || password != null,
            };
            if (!has) needAccess.Add(account);
        }
        incoming.ScheduledSends = current?.ScheduledSends.Where(s => incoming.Accounts.Any(a => a.Id == s.AccountId)).ToList() ?? new();
        incoming.UnreadableTold = false;
        return needAccess;
    }

    /// <summary>Stessi server, porte, sicurezza e utente, per IMAP e per SMTP: solo allora la password che questo PC ha già va agli stessi posti.</summary>
    internal static bool SameServer(ImapServer a, ImapServer b)
    {
        static string Host(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();
        return Host(a.ImapHost) == Host(b.ImapHost) && a.ImapPort == b.ImapPort && a.ImapSecurity == b.ImapSecurity
               && Host(a.SmtpHost) == Host(b.SmtpHost) && a.SmtpPort == b.SmtpPort && a.SmtpSecurity == b.SmtpSecurity && a.UserName == b.UserName;
    }

    /// <summary>
    /// L'app e la directory con cui un account Microsoft entra (come MicrosoftAuth.AppOf): l'app delle impostazioni per gli
    /// account personali, la sua con la sua directory per quelli di lavoro.
    /// </summary>
    internal static (string? App, string? Tenant) MicrosoftApp(Settings settings, MailAccount account) =>
        account.Tenant == null ? (settings.MicrosoftClientId, null) : (account.ClientId, account.Tenant);
}
