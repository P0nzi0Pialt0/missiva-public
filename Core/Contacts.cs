using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Missiva;

/// <summary>
/// Un contatto della vista Persone, come Persone del nuovo Outlook: nome, indirizzi, telefoni, azienda, ruolo, indirizzo e
/// note (dove stanno anche PEC e partita IVA dei clienti). Source dice dove sta: "local" in Missiva, oppure l'Id
/// dell'account: Microsoft (me/contacts, ServerId è il suo id) o Google (People API, in sola lettura, ServerId è il
/// resourceName).
/// </summary>
public sealed class Contact
{
    public const string Local = "local";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = Local;
    public string? ServerId { get; set; }
    public string DisplayName { get; set; } = "";
    public string GivenName { get; set; } = "";
    public string Surname { get; set; } = "";
    public List<string> Emails { get; set; } = new();
    public string Mobile { get; set; } = "";
    public string BusinessPhone { get; set; } = "";
    public string HomePhone { get; set; } = "";
    public string Company { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>Il sito web (Google urls, Outlook businessHomePage, vCard URL): si legge e si apre dalla scheda, non si scrive in Missiva.</summary>
    public string Website { get; set; } = "";
    /// <summary>
    /// Un gruppo di contatti di Missiva (Nuovo gruppo in Persone), come i gruppi di contatti di Outlook: il nome in
    /// DisplayName e le persone in Members, ognuna come "Nome &lt;indirizzo&gt;"; nel campo A si espande nei loro indirizzi.
    /// </summary>
    public bool IsGroup { get; set; }
    public List<string> Members { get; set; } = new();

    /// <summary>Graph: gli altri numeri dell'ufficio e di casa (la scheda mostra il primo), rimandati com'erano quando il primo cambia.</summary>
    [JsonIgnore] public List<string> MoreBusinessPhones { get; set; } = new();
    [JsonIgnore] public List<string> MoreHomePhones { get; set; } = new();
    /// <summary>
    /// Graph: il campo da cui viene l'indirizzo (businessAddress, homeAddress, otherAddress) e le sue parti non vuote in
    /// ordine (street, postalCode, city, state, countryOrRegion): l'indirizzo cambiato torna lì, con le stesse parti.
    /// </summary>
    [JsonIgnore] public string AddressField { get; set; } = "";
    [JsonIgnore] public List<string> AddressKeys { get; set; } = new();

    /// <summary>Il nome da mostrare: quello scelto, o nome e cognome, o l'indirizzo, o l'azienda.</summary>
    [JsonIgnore]
    public string Name => DisplayName.Trim().Length > 0 ? DisplayName.Trim()
        : $"{GivenName} {Surname}".Trim() is { Length: > 0 } full ? full
        : Emails.FirstOrDefault() ?? (Company.Length > 0 ? Company : "(senza nome)");

    public Contact Clone() => new()
    {
        Id = Id, Source = Source, ServerId = ServerId, DisplayName = DisplayName, GivenName = GivenName, Surname = Surname, Emails = Emails.ToList(),
        Mobile = Mobile, BusinessPhone = BusinessPhone, HomePhone = HomePhone, Company = Company, JobTitle = JobTitle, Address = Address, Notes = Notes, Website = Website,
        MoreBusinessPhones = MoreBusinessPhones.ToList(), MoreHomePhones = MoreHomePhones.ToList(), AddressField = AddressField, AddressKeys = AddressKeys.ToList(),
        IsGroup = IsGroup, Members = Members.ToList(),
    };

    /// <summary>
    /// Se il contatto corrisponde alla ricerca: nome, indirizzi, azienda, telefoni o note (di un gruppo anche le persone).
    /// Con il confronto della lingua di Windows, come prima, ma per la via svelta dove dà lo stesso risultato (CultureSearch).
    /// </summary>
    public bool Matches(string words)
    {
        if (words.Length == 0) return true;
#if DEBUG
        if (TestOldMatch) return MatchesByCulture(words);
#endif
        if (CultureSearch.Contains(Name, words) || CultureSearch.Contains(Company, words) || CultureSearch.Contains(JobTitle, words) || CultureSearch.Contains(Mobile, words)
            || CultureSearch.Contains(BusinessPhone, words) || CultureSearch.Contains(HomePhone, words) || CultureSearch.Contains(Notes, words)) return true;
        foreach (var email in Emails)
            if (CultureSearch.Contains(email, words)) return true;
        foreach (var member in Members)
            if (CultureSearch.Contains(member, words)) return true;
        return false;
    }

#if DEBUG
    /// <summary>Prove: Matches com'era prima dell'audit, blocco 8 (ogni campo con il confronto della lingua), per il confronto dei risultati e dei tempi.</summary>
    internal static bool TestOldMatch;

    internal bool MatchesByCulture(string words) => words.Length == 0 ||
        new[] { Name, Company, JobTitle, Mobile, BusinessPhone, HomePhone, Notes }.Concat(Emails).Concat(Members).Any(t => t.Contains(words, StringComparison.CurrentCultureIgnoreCase));
#endif

    /// <summary>Le persone di un gruppo come indirizzi (quelle scritte male restano fuori).</summary>
    [JsonIgnore]
    public List<Address> MemberAddresses => Members.Select(ParseMember).OfType<Address>().ToList();

    /// <summary>"Nome &lt;indirizzo&gt;" o solo l'indirizzo; null se non c'è un indirizzo valido.</summary>
    public static Address? ParseMember(string text)
    {
        text = text.Trim();
        int open = text.LastIndexOf('<'), close = text.LastIndexOf('>');
        var email = open >= 0 && close > open ? text[(open + 1)..close].Trim() : text;
        var name = open > 0 ? text[..open].Trim().Trim('"') : "";
        return AccountTypes.LooksValid(email) ? new Address { Name = name, Email = email } : null;
    }
}

[JsonSerializable(typeof(List<Contact>))]
sealed partial class ContactsJson : JsonSerializerContext;

/// <summary>
/// "Contiene, senza badare a maiuscole e minuscole" con il confronto della lingua di Windows (CurrentCultureIgnoreCase), per la
/// via svelta dove il risultato è lo stesso. Il confronto della lingua (ICU) costa circa un microsecondo a chiamata: in
/// Persone, con 4.500 contatti, 22-31 ms a ogni lettera (audit, v2 V-16). Fra due testi di soli caratteri ASCII stampabili
/// l'italiano e l'inglese non hanno regole proprie, e il confronto ordinale senza maiuscole dà lo stesso risultato, cento
/// volte più svelto; con una lettera accentata o un altro carattere, da una parte o dall'altra, resta il confronto della
/// lingua (che trova anche "è" scritta come "e" più l'accento, come nelle vCard di un Mac).
/// </summary>
static class CultureSearch
{
    /// <summary>
    /// La via svelta vale solo per l'italiano, l'inglese e la lingua neutra (altre hanno lettere doppie: "ch", "ll", "aa"), e
    /// solo se qui il confronto della lingua non ignora trattino e apostrofo (lo fa il confronto di Windows di prima, NLS).
    /// </summary>
    static readonly bool Fast = Probe();

    static bool Probe()
    {
        try
        {
            var name = System.Globalization.CultureInfo.CurrentCulture.CompareInfo.Name;
            if (name.Length > 0 && !name.StartsWith("it", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return false;
            const StringComparison culture = StringComparison.CurrentCultureIgnoreCase;
            return !"co-op d'amico".Contains("coop", culture) && !"co-op d'amico".Contains("damico", culture) && !"coop".Contains("co-op", culture)
                   && "CO-OP D'AMICO".Contains("o-op d'a", culture);
        }
        catch (Exception)
        {
            return false;
        }
    }

    static bool Plain(string text) => text.AsSpan().IndexOfAnyExceptInRange(' ', '~') < 0;

    public static bool Contains(string text, string words) =>
        Fast && Plain(words) && Plain(text) ? text.Contains(words, StringComparison.OrdinalIgnoreCase) : text.Contains(words, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>
/// I contatti tenuti da Missiva (contatti.dat nella cartella dei dati), tutto il file cifrato con DPAPI come la rubrica e
/// la copia locale: nomi, telefoni e note dei clienti non restano in chiaro sul disco.
/// </summary>
public static class ContactBook
{
    static string FilePath => Path.Combine(Store.Dir, "contatti.dat");
    static List<Contact>? local;
    static readonly object gate = new();

    public static List<Contact> Local
    {
        get
        {
            lock (gate) return local ??= Load();
        }
    }

    static List<Contact> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var bytes = SafeFile.Unprotect(File.ReadAllBytes(FilePath));
            return JsonSerializer.Deserialize(bytes, ContactsJson.Default.ListContact) ?? new();
        }
        catch (Exception ex)
        {
            // Il file che non si legge (rovinato, o cifrato da un altro utente di Windows) va da parte con la data nel nome:
            // prima il primo contatto salvato dopo lo sovrascriveva (audit, F-44).
            Log.Error($"contatti di Missiva non letti, file messo da parte: {SafeFile.SetAside(FilePath) ?? "non riuscito"}", ex);
            return new();
        }
    }

    public static void Save()
    {
        lock (gate)
        {
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(local ?? new(), ContactsJson.Default.ListContact);
                SafeFile.Write(FilePath, SafeFile.Protect(bytes));
            }
            catch (Exception ex)
            {
                Log.Error("contatti di Missiva non salvati", ex);
            }
        }
    }

    /// <summary>Un contatto nuovo o cambiato fra quelli di Missiva.</summary>
    public static void Put(Contact contact)
    {
        Add([contact]);
        Save();
    }

    /// <summary>
    /// Contatti nuovi o cambiati, senza salvare: Importa li aggiunge tutti e poi salva una volta sola (Save in sottofondo).
    /// Prima ogni contatto riscriveva e cifrava tutto il file: 2000 contatti 22,6 s, 5000 contatti 159 s con circa 5 GB
    /// scritti, con la finestra ferma. L'elenco si cambia sul thread di chi lo legge (l'interfaccia), sotto il lucchetto di Save.
    /// </summary>
    public static void Add(IReadOnlyCollection<Contact> contacts)
    {
        var ids = contacts.Select(c => c.Id).ToHashSet();
        lock (gate)
        {
            local ??= Load();
            local.RemoveAll(c => ids.Contains(c.Id));
            foreach (var c in contacts) c.Source = Contact.Local;
            local.AddRange(contacts);
        }
    }

    public static void Remove(Contact contact)
    {
        lock (gate) (local ??= Load()).RemoveAll(c => c.Id == contact.Id);
        Save();
    }

    // ---------------------------------------------------------------- contatti di Outlook per i suggerimenti

    /// <summary>
    /// La copia dei contatti di Outlook di ogni account Microsoft, per i suggerimenti dei destinatari: cifrata con DPAPI come
    /// contatti.dat, in cache\contatti-account.dat (tolta con l'account). Si aggiorna quando Persone legge i contatti e poco
    /// dopo l'avvio, solo con il consenso ai contatti già dato: senza, nessuna richiesta e nessuna copia.
    /// </summary>
    static readonly Dictionary<string, List<Contact>> remote = new();

    static string RemoteFile(string accountId) => Store.CachePath($"contatti-{Store.CacheName(accountId)}.dat");

    /// <summary>I contatti di Outlook dell'account appena letti: in memoria subito, sul disco in sottofondo.</summary>
    public static void SetRemote(string accountId, List<Contact> contacts)
    {
        var copy = contacts.Select(c => c.Clone()).ToList();
        lock (gate) remote[accountId] = copy;
        _ = Task.Run(() =>
        {
            lock (gate)
            {
                if (!remote.TryGetValue(accountId, out var now) || now != copy) return; // intanto è arrivata un'altra lettura, o l'account è stato rimosso
                try
                {
                    var path = RemoteFile(accountId);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(copy, ContactsJson.Default.ListContact);
                    SafeFile.Write(path, SafeFile.Protect(bytes));
                }
                catch (Exception ex)
                {
                    Log.Error("copia dei contatti di Outlook non salvata", ex);
                }
            }
        });
    }

    /// <summary>I contatti di Outlook dell'account dalla copia (in memoria, o dal disco la prima volta); vuoto senza copia.</summary>
    public static List<Contact> Remote(string accountId)
    {
        lock (gate)
        {
            if (remote.TryGetValue(accountId, out var list)) return list;
            try
            {
                var path = RemoteFile(accountId);
                list = File.Exists(path)
                    ? JsonSerializer.Deserialize(SafeFile.Unprotect(File.ReadAllBytes(path)), ContactsJson.Default.ListContact) ?? new()
                    : new();
            }
            catch (Exception ex)
            {
                Log.Error("copia dei contatti di Outlook non letta", ex);
                list = new();
            }
            return remote[accountId] = list;
        }
    }

    /// <summary>
    /// Dopo l'avvio, per i suggerimenti: i contatti di Outlook dell'account riletti se la copia ha più di 12 ore, solo con il
    /// consenso ai contatti già dato (senza, GetContactsAsync non chiede nulla e restituisce null).
    /// </summary>
    public static async Task RefreshRemoteAsync(IMailProvider provider)
    {
        if (!provider.HasContacts) return;
        var path = RemoteFile(provider.Account.Id);
        if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromHours(12)) return;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (await provider.GetContactsAsync() is not { } found) return;
            SetRemote(provider.Account.Id, found);
            Log.Time($"persone: copia dei contatti di Outlook di {provider.Account.Email} ({found.Count})", clock);
        }
        catch (Exception ex)
        {
            Log.Write($"persone: contatti di Outlook di {provider.Account.Email} non riletti: {ex.Message}");
        }
    }

    /// <summary>Account rimosso: la sua copia dei contatti esce dal PC.</summary>
    public static void ForgetRemote(string accountId)
    {
        lock (gate)
        {
            remote.Remove(accountId);
            try { File.Delete(RemoteFile(accountId)); }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) { Log.Error("copia dei contatti dell'account rimosso non cancellata", ex); }
        }
    }

    /// <summary>Tutti i contatti per i suggerimenti: quelli di Missiva (gruppi compresi) e le copie di Outlook e di Google degli account.</summary>
    public static List<Contact> ForSuggestions()
    {
        // La copia sotto il lucchetto: la chiede anche AddressBook.Preload, da un thread del pool, mentre l'interfaccia può
        // aggiungere o togliere contatti (prima fuori: audit dell'11 ottobre, C-30). Le copie di Outlook e di Google non si
        // cambiano mai sul posto (SetRemote ne mette una nuova).
        List<Contact> all;
        lock (gate) all = (local ??= Load()).ToList();
        foreach (var account in Store.Settings.Accounts.Where(a => a.Kind is AccountKind.Microsoft or AccountKind.Gmail && a.SharedOf == null)) all.AddRange(Remote(account.Id));
        return all;
    }

#if DEBUG
    /// <summary>Prove: un altro file (o di nuovo quello vero), da rileggere.</summary>
    internal static void Reset() { lock (gate) { local = null; remote.Clear(); } }
#endif
}

/// <summary>
/// vCard (.vcf, RFC 6350 e 2.1 di Outlook) e CSV di Outlook e di Gmail, letti come testo: righe piegate riunite, testo
/// quoted-printable e codifiche dichiarate decodificati a mano, PHOTO e ogni altro binario ignorati (niente da decodificare
/// o eseguire), al massimo 5 MB e 5000 contatti. In uscita vCard 3.0 in UTF-8.
/// </summary>
public static class ContactFiles
{
    public const int MaxBytes = 5 * 1024 * 1024;
    const int MaxContacts = 5000;

    /// <summary>Un file troppo grande non si legge nemmeno.</summary>
    public static List<Contact> Read(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new MailException($"Il file è troppo grande ({info.Length / 1024 / 1024} MB): Missiva legge rubriche fino a 5 MB.");
        var text = File.ReadAllText(path, Encoding.UTF8);
        return path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? Csv(text) : VCard(text);
    }

    // ---------------------------------------------------------------- vCard

    public static List<Contact> VCard(string text)
    {
        var result = new List<Contact>();
        Contact? current = null;
        foreach (var line in Unfold(text))
        {
            if (result.Count >= MaxContacts) break;
            int colon = line.IndexOf(':');
            if (colon <= 0 || colon > 512) continue;
            var head = line[..colon];
            var value = line[(colon + 1)..];
            var parts = head.Split(';');
            var name = parts[0].Contains('.') ? parts[0][(parts[0].LastIndexOf('.') + 1)..] : parts[0]; // item1.EMAIL
            name = name.ToUpperInvariant();
            var parameters = parts.Skip(1).Select(p => p.ToUpperInvariant()).ToList();
            if (name == "BEGIN" && value.Trim().Equals("VCARD", StringComparison.OrdinalIgnoreCase)) { current = new Contact(); continue; }
            if (current == null) continue;
            if (name == "END")
            {
                if (current.Name != "(senza nome)" || current.Emails.Count > 0) result.Add(current);
                current = null;
                continue;
            }
            if (name is "PHOTO" or "LOGO" or "SOUND" or "KEY") continue; // binari: non si decodificano
            if (value.Length > 64 * 1024) continue;
            if (parameters.Any(p => p is "ENCODING=QUOTED-PRINTABLE" or "QUOTED-PRINTABLE")) value = QuotedPrintable(value, Charset(parameters));
            string Text(string v) => v.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
            bool Is(string type) => parameters.Any(p => p.Contains(type));
            switch (name)
            {
                case "FN": current.DisplayName = Text(value); break;
                case "N":
                    var n = value.Split(';');
                    current.Surname = Text(n.ElementAtOrDefault(0) ?? "");
                    current.GivenName = Text(n.ElementAtOrDefault(1) ?? "");
                    break;
                case "EMAIL":
                    var email = Text(value);
                    if (AccountTypes.LooksValid(email) && !current.Emails.Contains(email, StringComparer.OrdinalIgnoreCase)) current.Emails.Add(email);
                    break;
                case "TEL":
                    var tel = Text(value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ? value[4..] : value);
                    if (Is("CELL") && current.Mobile.Length == 0) current.Mobile = tel;
                    else if (Is("HOME") && current.HomePhone.Length == 0) current.HomePhone = tel;
                    else if (current.BusinessPhone.Length == 0) current.BusinessPhone = tel;
                    else if (current.Mobile.Length == 0) current.Mobile = tel;
                    break;
                case "ORG": current.Company = Text(value.Split(';')[0]); break;
                case "TITLE": current.JobTitle = Text(value); break;
                case "ADR":
                    current.Address = string.Join(", ", value.Split(';').Select(Text).Where(p => p.Length > 0));
                    break;
                case "NOTE": current.Notes = Text(value); break;
                case "URL": if (current.Website.Length == 0) current.Website = Text(value); break;
            }
        }
        return result;
    }

    /// <summary>
    /// Le righe piegate (che cominciano con uno spazio o una tabulazione) riunite; nel quoted-printable il "=" in fondo. In
    /// una sola passata: se la riga è quoted-printable si decide una volta, dalla testa (prima dei due punti), e una riga
    /// riunita oltre 256 KB (una foto, o una vCard costruita apposta) si salta tutta. Prima ogni continuazione del
    /// quoted-printable rileggeva tutta la riga già riunita: una vCard di 5 MB fermava Missiva per 57 s.
    /// </summary>
    static IEnumerable<string> Unfold(string text)
    {
        const int MaxLine = 256 * 1024;
        var line = new StringBuilder();
        bool open = false;      // c'è una riga in corso
        bool quoted = false;    // la riga in corso dichiara QUOTED-PRINTABLE nella testa
        bool headSeen = false;  // i due punti della riga in corso sono arrivati (o la testa è troppo lunga per esserlo)
        bool soft = false;      // la riga in corso finisce con "=": nel quoted-printable continua sulla riga dopo
        bool skipped = false;   // la riga in corso ha passato il limite: si salta tutta
        void Take(string raw, int from)
        {
            if (skipped) { soft = raw.Length > from && raw[^1] == '='; return; }
            int start = line.Length;
            line.Append(raw, from, raw.Length - from);
            if (!headSeen)
            {
                int colon = raw.IndexOf(':', from);
                if (colon >= 0)
                {
                    headSeen = true;
                    quoted = line.ToString(0, start + colon - from).Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.Length > 1024) headSeen = true; // nessuna testa così lunga: VCard la scarta comunque
            }
            soft = line.Length > 0 && line[^1] == '=';
            if (line.Length > MaxLine) { skipped = true; line.Clear(); }
        }
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (open && raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t')) { Take(raw, 1); continue; }
            if (open && quoted && soft)
            {
                if (!skipped) line.Length--; // quoted-printable: "=" in fondo continua sulla riga dopo
                Take(raw, 0);
                continue;
            }
            if (open && !skipped) yield return line.ToString();
            line.Clear();
            open = true;
            quoted = headSeen = soft = skipped = false;
            Take(raw, 0);
        }
        if (open && !skipped && line.Length > 0) yield return line.ToString();
    }

    static Encoding Charset(List<string> parameters)
    {
        var name = parameters.FirstOrDefault(p => p.StartsWith("CHARSET="))?[8..];
        if (name is "WINDOWS-1252" or "CP1252" or "ISO-8859-15") return Encoding.Latin1; // le vCard 2.1 di Outlook: le lettere accentate sono le stesse
        try { return name != null ? Encoding.GetEncoding(name) : Encoding.UTF8; }
        catch (ArgumentException) { return Encoding.UTF8; }
    }

    static string QuotedPrintable(string value, Encoding encoding)
    {
        var bytes = new List<byte>(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
        }
        return encoding.GetString(bytes.ToArray());
    }

    /// <summary>vCard 3.0 in UTF-8, come la esportano Outlook e Gmail (senza foto).</summary>
    public static string WriteVCard(IEnumerable<Contact> contacts)
    {
        static string Escape(string v) => v.Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;").Replace("\r\n", "\\n").Replace("\n", "\\n");
        var sb = new StringBuilder();
        foreach (var c in contacts)
        {
            sb.Append("BEGIN:VCARD\r\nVERSION:3.0\r\n");
            sb.Append($"FN:{Escape(c.Name)}\r\nN:{Escape(c.Surname)};{Escape(c.GivenName)};;;\r\n");
            foreach (var e in c.Emails) sb.Append($"EMAIL;TYPE=INTERNET:{Escape(e)}\r\n");
            if (c.Mobile.Length > 0) sb.Append($"TEL;TYPE=CELL:{Escape(c.Mobile)}\r\n");
            if (c.BusinessPhone.Length > 0) sb.Append($"TEL;TYPE=WORK:{Escape(c.BusinessPhone)}\r\n");
            if (c.HomePhone.Length > 0) sb.Append($"TEL;TYPE=HOME:{Escape(c.HomePhone)}\r\n");
            if (c.Company.Length > 0) sb.Append($"ORG:{Escape(c.Company)}\r\n");
            if (c.JobTitle.Length > 0) sb.Append($"TITLE:{Escape(c.JobTitle)}\r\n");
            if (c.Address.Length > 0) sb.Append($"ADR;TYPE=WORK:;;{Escape(c.Address)};;;;\r\n");
            if (c.Notes.Length > 0) sb.Append($"NOTE:{Escape(c.Notes)}\r\n");
            if (c.Website.Length > 0) sb.Append($"URL:{Escape(c.Website)}\r\n");
            sb.Append("END:VCARD\r\n");
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- CSV di Outlook e Gmail

    /// <summary>
    /// Il CSV esportato da Outlook ("First Name", "E-mail Address", "Mobile Phone"...) o da Gmail ("Given Name", "E-mail 1 -
    /// Value", "Phone 1 - Value"...), anche con le intestazioni in italiano di Outlook ("Nome", "Cognome", "Indirizzo posta
    /// elettronica"). Campi fra virgolette con virgole e a capo.
    /// </summary>
    public static List<Contact> Csv(string text)
    {
        var rows = CsvRows(text).ToList();
        if (rows.Count < 2) return new();
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));
        var cols = new
        {
            Name = Col("name", "nome visualizzato", "display name"), Given = Col("first name", "given name", "nome"), Family = Col("last name", "family name", "cognome"),
            Mail1 = Col("e-mail address", "e-mail 1 - value", "indirizzo posta elettronica", "email address"), Mail2 = Col("e-mail 2 address", "e-mail 2 - value", "indirizzo posta elettronica 2"),
            Mobile = Col("mobile phone", "cellulare", "phone 1 - value"), Business = Col("business phone", "ufficio", "telefono ufficio", "phone 2 - value"),
            Company = Col("company", "organization 1 - name", "società"), Title = Col("job title", "organization 1 - title", "posizione"), Notes = Col("notes", "note"),
        };
        string At(List<string> row, int i) => i >= 0 && i < row.Count ? row[i].Trim() : "";
        var result = new List<Contact>();
        foreach (var row in rows.Skip(1).Take(MaxContacts))
        {
            var c = new Contact
            {
                DisplayName = At(row, cols.Name), GivenName = At(row, cols.Given), Surname = At(row, cols.Family), Mobile = At(row, cols.Mobile),
                BusinessPhone = At(row, cols.Business), Company = At(row, cols.Company), JobTitle = At(row, cols.Title), Notes = At(row, cols.Notes),
            };
            foreach (var mail in new[] { At(row, cols.Mail1), At(row, cols.Mail2) }.Where(AccountTypes.LooksValid)) c.Emails.Add(mail);
            if (c.Name != "(senza nome)") result.Add(c);
        }
        return result;
    }

    /// <summary>Le righe del CSV; il separatore dalla prima riga (Excel in italiano mette ";").</summary>
    static IEnumerable<List<string>> CsvRows(string text)
    {
        int end = text.IndexOf('\n');
        var first = end < 0 ? text : text[..end];
        char separator = first.Count(c => c == ';') > first.Count(c => c == ',') ? ';' : ',';
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == separator) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(f => f.Length > 0)) yield return row;
                row = new List<string>();
            }
            else field.Append(c);
        }
        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0)) yield return row;
    }

    // ---------------------------------------------------------------- Graph

    /// <summary>Le parti di un indirizzo di Graph (physicalAddress), nell'ordine in cui la scheda le mostra.</summary>
    static readonly string[] AddressParts = ["street", "postalCode", "city", "state", "countryOrRegion"];

    /// <summary>
    /// Un contatto di me/contacts. Tutti i telefoni (la scheda mostra il primo); l'indirizzo dal primo campo che ha qualcosa,
    /// di lavoro, di casa o altro: Graph dà {} per un indirizzo vuoto, e con businessAddress ?? homeAddress quello di casa non
    /// si vedeva mai.
    /// </summary>
    public static Contact FromGraph(JsonNode c, string accountId)
    {
        static string Str(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        static List<string> All(JsonNode? list) => (list?.AsArray() ?? new JsonArray()).Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").Where(s => s.Length > 0).ToList();
        var (field, address) = new[] { "businessAddress", "homeAddress", "otherAddress" }.Select(k => (Field: k, Address: c[k]))
            .FirstOrDefault(x => AddressParts.Any(p => Str(x.Address, p).Trim().Length > 0));
        var keys = AddressParts.Where(p => Str(address, p).Trim().Length > 0).ToList();
        var business = All(c["businessPhones"]);
        var home = All(c["homePhones"]);
        return new Contact
        {
            Source = accountId, ServerId = Str(c, "id"), DisplayName = Str(c, "displayName"), GivenName = Str(c, "givenName"), Surname = Str(c, "surname"),
            Emails = (c["emailAddresses"]?.AsArray() ?? new JsonArray()).Select(e => Str(e, "address")).Where(e => e.Length > 0).ToList(),
            Mobile = Str(c, "mobilePhone"), BusinessPhone = business.FirstOrDefault() ?? "", HomePhone = home.FirstOrDefault() ?? "",
            MoreBusinessPhones = business.Skip(1).ToList(), MoreHomePhones = home.Skip(1).ToList(),
            Company = Str(c, "companyName"), JobTitle = Str(c, "jobTitle"), Notes = Str(c, "personalNotes"), Website = Str(c, "businessHomePage"),
            Address = string.Join(", ", keys.Select(k => Str(address, k).Trim())), AddressField = field ?? "", AddressKeys = keys,
        };
    }

    public const string GraphFields = "id,displayName,givenName,surname,emailAddresses,mobilePhone,businessPhones,homePhones,companyName,jobTitle,businessAddress,homeAddress,otherAddress,personalNotes,businessHomePage";

    /// <summary>
    /// Il contatto per Graph (POST me/contacts, o PATCH con before): solo i campi cambiati, come per gli eventi; vuoto se
    /// non è cambiato nulla. Un telefono cambiato va con gli altri numeri dello stesso tipo com'erano (businessPhones e
    /// homePhones sono elenchi, e la PATCH li sostituisce interi); l'indirizzo torna nel campo da cui è stato letto, intero
    /// (PostalAddress). after resta com'è su Outlook dopo la richiesta: primo telefono e parti dell'indirizzo mandate.
    /// </summary>
    public static JsonObject ToGraph(Contact? before, Contact after)
    {
        var patch = new JsonObject();
        void Set(string key, string now, string? was)
        {
            if (before == null ? now.Trim().Length > 0 : now.Trim() != (was ?? "").Trim()) patch[key] = now.Trim();
        }
        Set("displayName", after.DisplayName, before?.DisplayName);
        Set("givenName", after.GivenName, before?.GivenName);
        Set("surname", after.Surname, before?.Surname);
        Set("mobilePhone", after.Mobile, before?.Mobile);
        Set("companyName", after.Company, before?.Company);
        Set("jobTitle", after.JobTitle, before?.JobTitle);
        Set("personalNotes", after.Notes, before?.Notes);
        if (before == null ? after.Emails.Count > 0 : !after.Emails.SequenceEqual(before.Emails, StringComparer.OrdinalIgnoreCase))
            patch["emailAddresses"] = new JsonArray(after.Emails.Select(e => (JsonNode)new JsonObject { ["address"] = e, ["name"] = after.Name }).ToArray());
        // Il primo numero cambiato, gli altri com'erano; tolto il primo, il secondo diventa il primo (come in Outlook).
        List<string> Phones(string first, List<string>? more) => (first.Trim().Length > 0 ? [first.Trim()] : new List<string>()).Concat(more ?? []).ToList();
        if (before == null ? after.BusinessPhone.Length > 0 : after.BusinessPhone.Trim() != before.BusinessPhone.Trim())
        {
            var all = Phones(after.BusinessPhone, before?.MoreBusinessPhones);
            patch["businessPhones"] = new JsonArray(all.Select(p => (JsonNode?)p).ToArray());
            (after.BusinessPhone, after.MoreBusinessPhones) = (all.FirstOrDefault() ?? "", all.Skip(1).ToList());
        }
        if (before == null ? after.HomePhone.Length > 0 : after.HomePhone.Trim() != before.HomePhone.Trim())
        {
            var all = Phones(after.HomePhone, before?.MoreHomePhones);
            patch["homePhones"] = new JsonArray(all.Select(p => (JsonNode?)p).ToArray());
            (after.HomePhone, after.MoreHomePhones) = (all.FirstOrDefault() ?? "", all.Skip(1).ToList());
        }
        if (before == null ? after.Address.Length > 0 : after.Address.Trim() != before.Address.Trim())
        {
            var field = before?.AddressField is { Length: > 0 } f ? f : "businessAddress"; // dove stava; quello nuovo al lavoro, come prima
            var address = PostalAddress(after.Address, before?.AddressKeys);
            patch[field] = address;
            after.AddressField = field;
            after.AddressKeys = AddressParts.Where(p => address[p]?.GetValue<string>() is { Length: > 0 }).ToList();
        }
        return patch;
    }

    /// <summary>
    /// L'indirizzo scritto nella scheda per Graph, con tutte le parti: la PATCH di un indirizzo lo sostituisce intero, e con
    /// la sola via CAP, città e provincia sparivano. Le parti separate da virgole tornano ognuna al suo posto, dalla fine
    /// (la via può avere una virgola: "Via Roma, 1"); se sono meno di quelle lette, o la via non c'era, e non tornano una per
    /// una, tutto il testo va nella via (niente si perde né si ripete).
    /// </summary>
    static JsonObject PostalAddress(string text, List<string>? keys)
    {
        var pieces = text.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var address = new JsonObject();
        foreach (var part in AddressParts) address[part] = "";
        if (keys is { Count: > 0 } && (pieces.Count == keys.Count || pieces.Count > keys.Count && keys[0] == "street"))
        {
            int extra = pieces.Count - keys.Count; // pezzi in più: della via
            address[keys[0]] = string.Join(", ", pieces.Take(extra + 1));
            for (int i = 1; i < keys.Count; i++) address[keys[i]] = pieces[extra + i];
        }
        else if (pieces.Count > 0) address["street"] = string.Join(", ", pieces);
        return address;
    }
}
