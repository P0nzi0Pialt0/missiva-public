using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Missiva;

/// <summary>
/// Rubrica dei suggerimenti dei destinatari, come nel nuovo Outlook ma senza chiedere il permesso dei Contatti: le
/// persone da cui arriva posta (elenchi, copia locale, messaggi aperti) e soprattutto quelle a cui scrivi (Posta inviata
/// di ogni account, letta la prima volta e poi al massimo ogni ora in sottofondo, e i messaggi inviati da Missiva).
/// Sta in rubrica.json nella cartella dei dati, cifrata con DPAPI come la copia locale (nomi e indirizzi di tutti i
/// corrispondenti non restano in chiaro; quella scritta in chiaro dalle versioni di prima si legge ancora e si cifra alla
/// prossima scrittura). Si conta un messaggio solo se è più recente dell'ultimo già visto per quell'indirizzo, così
/// ricaricare un elenco non gonfia i conteggi.
/// </summary>
public static class AddressBook
{
    public sealed class Entry
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public int Received { get; set; }
        public int Sent { get; set; }
        public DateTime LastReceived { get; set; }
        public DateTime LastSent { get; set; }

        [JsonIgnore] public DateTime Last => LastSent > LastReceived ? LastSent : LastReceived;
        /// <summary>
        /// Le persone a cui scrivi contano più di quelle che ti scrivono (spesso newsletter); i contatti di Persone valgono come
        /// tre messaggi scritti, così un contatto a cui non hai ancora scritto sta fra le prime proposte.
        /// </summary>
        [JsonIgnore] public int Score => Sent * 3 + Received + (Contact ? 9 : 0);
        /// <summary>Dai contatti (Missiva o Outlook) o dai colleghi di Microsoft 365: l'azienda, per la seconda riga.</summary>
        [JsonIgnore] public string Company { get; set; } = "";
        /// <summary>Viene dai contatti di Persone o di Outlook.</summary>
        [JsonIgnore] public bool Contact { get; set; }
        /// <summary>Un gruppo di contatti: scelto, nel campo vanno tutte le sue persone.</summary>
        [JsonIgnore] public Contact? Group { get; set; }
        /// <summary>Un collega trovato in Microsoft 365 (me/people).</summary>
        [JsonIgnore] public bool Colleague { get; set; }

        /// <summary>Il nome per come si vede (SenderCheck.Skeleton), per LookAlike: calcolato una volta per nome.</summary>
        [JsonIgnore]
        internal string Skeleton
        {
            get
            {
                if (!ReferenceEquals(skeletonOf, Name)) (skeletonOf, skeleton) = (Name, SenderCheck.Skeleton(Name.Trim().Trim('"', '\'')));
                return skeleton;
            }
        }
        string? skeletonOf;
        string skeleton = "";

        /// <summary>La seconda riga del suggerimento: indirizzo e azienda, o le persone del gruppo.</summary>
        [JsonIgnore]
        public string Detail => Group is { } g ? g.Members.Count == 1 ? T("Gruppo, {0} persona", g.Members.Count) : T("Gruppo, {0} persone", g.Members.Count)
            : Company.Length > 0 ? $"{Email} · {Company}" : Email;

        /// <summary>Il nome per UI Automation e Assistente vocale: la persona con l'azienda, il gruppo con quante persone ha.</summary>
        [JsonIgnore]
        public string AutomationName => Group != null ? $"{Name}, {Detail}" : !Email.Contains('@') ? Name : ToString() + (Company.Length > 0 ? ", " + Company : "");

        /// <summary>Come va nel campo A (Address.FieldText); un gruppo, tutte le sue persone separate da punto e virgola.</summary>
        public override string ToString() => Group is { } g
            ? string.Join("; ", g.MemberAddresses.Select(a => a.FieldText()))
            : new Address { Name = Name, Email = Email }.FieldText();
    }

    sealed class Data
    {
        public List<Entry> Entries { get; set; } = new();
        /// <summary>Ultima lettura della Posta inviata, per account.</summary>
        public Dictionary<string, DateTime> SentScans { get; set; } = new();
    }

    /// <summary>Messaggi inviati letti per account: quanto chiede A (gli ultimi 500), una richiesta sola.</summary>
    public const int SentScanSize = 500;

    static readonly TimeSpan ScanInterval = TimeSpan.FromHours(1);
    static readonly object Gate = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    static Dictionary<string, Entry>? entries;
    static Dictionary<string, DateTime> sentScans = new();
    static Timer? saveTimer;
    static readonly HashSet<string> scanning = new();

    static string FilePath => Path.Combine(Store.Dir, "rubrica.json");

    /// <summary>Carica la rubrica in sottofondo all'avvio, così il primo elenco non la aspetta.</summary>
    public static void Preload() => Task.Run(() =>
    {
        lock (Gate) Ensure();
        Contacts?.Invoke(); // anche i contatti (contatti.dat e le copie di Outlook, cifrati): la prima lettera non li aspetta
    });

    /// <summary>Sotto Gate. La prima volta, senza file, si parte dai mittenti della copia locale di ogni account.</summary>
    static Dictionary<string, Entry> Ensure()
    {
        if (entries != null) return entries;
        entries = new(StringComparer.OrdinalIgnoreCase);
        bookVersion++;
        try
        {
            if (File.Exists(FilePath))
            {
                var bytes = File.ReadAllBytes(FilePath);
                if (bytes.Length > 0 && bytes[0] != (byte)'{') bytes = SafeFile.Unprotect(bytes);
                var data = JsonSerializer.Deserialize<Data>(bytes, Json) ?? new();
                foreach (var e in data.Entries.Where(e => e.Email.Length > 0)) entries[e.Email] = e;
                sentScans = data.SentScans;
                return entries;
            }
            foreach (var account in Store.Settings.Accounts)
                foreach (var m in MailCache.AllMessages(account).OrderBy(m => m.Date))
                    Add(m.From, m.Date, sent: false);
            if (entries.Count > 0) ScheduleSave();
        }
        catch (Exception ex)
        {
            Log.Error("lettura della rubrica", ex);
        }
        return entries;
    }

    /// <summary>Mittenti di un elenco (non della Posta indesiderata: lo decide chi chiama).</summary>
    public static void Received(IEnumerable<MessageSummary> messages)
    {
        lock (Gate)
        {
            Ensure();
            bool changed = false;
            foreach (var m in messages.OrderBy(m => m.Date)) changed |= Add(m.From, m.Date, sent: false);
            if (changed) ScheduleSave();
        }
    }

    /// <summary>Persone di un messaggio ricevuto: mittente e altri destinatari.</summary>
    public static void Received(IEnumerable<Address> people, DateTime date) => Record(people, date, sent: false);

    /// <summary>Destinatari di un messaggio inviato.</summary>
    public static void Sent(IEnumerable<Address> recipients, DateTime date) => Record(recipients, date, sent: true);

    static void Record(IEnumerable<Address> people, DateTime date, bool sent)
    {
        lock (Gate)
        {
            Ensure();
            bool changed = false;
            foreach (var a in people) changed |= Add(a, date, sent);
            if (changed) ScheduleSave();
        }
    }

    /// <summary>Sotto Gate. true se la rubrica è cambiata.</summary>
    static bool Add(Address a, DateTime date, bool sent)
    {
        var email = a.Email.Trim();
        if (!Wanted(email)) return false;
        if (!sent && Store.Settings.Accounts.Any(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase))) return false;
        if (!entries!.TryGetValue(email, out var e))
        {
            entries[email] = e = new Entry { Email = email };
            bookVersion++; // una voce nuova, anche se poi non cambia altro: l'indice dei suggerimenti è da rifare
        }
        bool changed = false;
        // Il nome più recente vince (le persone cambiano come si firmano); un nome che è l'indirizzo non conta, e nemmeno
        // un nome che è l'indirizzo o il sito di un altro ("assistenza@banca.example" <x@altro.example>, SenderCheck.NameAddress):
        // prima la rubrica lo imparava, e i suggerimenti lo proponevano con quel nome (audit, blocco 3).
        var name = a.Name.Trim().Trim('"', '\'');
        if (name.Length > 0 && !name.Equals(email, StringComparison.OrdinalIgnoreCase) && name != e.Name && date >= e.Last
            && SenderCheck.NameAddress(new Address { Name = name, Email = email }) == null)
        {
            e.Name = name;
            changed = true;
        }
        if (sent && date > e.LastSent)
        {
            e.Sent++;
            e.LastSent = date;
            changed = true;
        }
        else if (!sent && date > e.LastReceived)
        {
            e.Received++;
            e.LastReceived = date;
            changed = true;
        }
        if (changed) bookVersion++;
        return changed;
    }

    /// <summary>
    /// La rubrica di una copia di sicurezza come entra in questo PC (Backup, scelte del 12 ottobre 2026 sull'audit dell'11
    /// ottobre, T-17): gli indirizzi con i loro nomi, e nient'altro. Conteggi, date e letture della Posta inviata non
    /// entrano: sono loro a dire a chi hai scritto, e una copia preparata da altri con "Mario Rossi" a un suo indirizzo e
    /// 900 messaggi scritti diventava il primo suggerimento per "mario", e un messaggio di "Mario Rossi" da lì non
    /// accendeva la fascia dei sosia (LookAlike guarda Sent). Azzerati, valgono solo i messaggi di questo PC: la Posta
    /// inviata si rilegge al primo avvio (ScanSentAsync, senza l'ora dell'ultima lettura), gli altri contano come
    /// arrivano. Le date azzerate servono quanto i conteggi: una data nel futuro fermerebbe per sempre conteggio e nome
    /// di quella voce (Add). I nomi entrano come li imparerebbe Add: puliti, mai l'indirizzo o il sito di un altro.
    /// Una rubrica che non è quella che Missiva scrive solleva JsonException: la copia non si importa.
    /// </summary>
    internal static string Imported(string json)
    {
        var data = JsonSerializer.Deserialize<Data>(json, Json) ?? new();
        var kept = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in data.Entries)
        {
            var email = e.Email.Trim();
            if (!Wanted(email)) continue;
            var name = SenderCheck.CleanName(e.Name).Trim('"', '\'');
            bool named = name.Length > 0 && !name.Equals(email, StringComparison.OrdinalIgnoreCase) && SenderCheck.NameAddress(new Address { Name = name, Email = email }) == null;
            kept[email] = new Entry { Email = email, Name = named ? name : "" };
        }
        return JsonSerializer.Serialize(new Data { Entries = kept.Values.ToList() }, Json);
    }

    /// <summary>
    /// Non si propongono gli indirizzi a cui non si scrive: i "noreply" dei servizi e i propri account (che mandano la
    /// Posta inviata: i propri indirizzi entrano solo come destinatari di un messaggio inviato, se ti scrivi).
    /// </summary>
    static bool Wanted(string email)
    {
        int at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1 || email.Contains(' ')) return false;
        var local = email[..at].ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(".", "");
        return !(local.Contains("noreply") || local.Contains("donotreply") || local.StartsWith("mailerdaemon") || local == "postmaster");
    }

    /// <summary>
    /// Fino a max persone il cui nome o indirizzo comincia con quello che si sta scrivendo (ogni parola della ricerca
    /// all'inizio di una parola del nome, della parte prima della @ o del dominio), le più usate e recenti prima.
    /// </summary>
    /// <remarks>
    /// Prima ogni lettera rifaceva tutto: la rubrica ricopiata, una voce nuova per ogni indirizzo dei contatti, nome, azienda
    /// e indirizzo di ogni voce rimessi in minuscolo e divisi in parole (audit, v2 V-15 e v1 V-07). Ora quel lavoro sta in
    /// un indice in memoria (Index), rifatto solo quando la rubrica o i contatti cambiano; le persone proposte e il loro
    /// ordine sono quelli di prima.
    /// </remarks>
    public static List<Entry> Suggest(string query, int max, ICollection<string>? exclude = null)
    {
        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return new();
        // I contatti di Persone (di Missiva e le copie di Outlook), con l'azienda e tutti i loro indirizzi, e i gruppi: come
        // nel nuovo Outlook anche le persone a cui non si è ancora scritto. Presi fuori dal lucchetto della rubrica.
        var contacts = Contacts?.Invoke() ?? [];
        long mark = Mark(contacts);
        lock (Gate)
        {
            var book = Ensure();
            if (index == null || index.Book != bookVersion || index.Contacts != mark) index = new Index(bookVersion, mark, Build(book, contacts));
            var found = new List<(Entry e, int rank)>();
            foreach (var item in index.Items)
            {
                if (exclude != null && item.Entry.Group == null && exclude.Contains(item.Entry.Email)) continue;
                int rank = item.Rank(terms);
                if (rank > 0) found.Add((item.Entry, rank));
            }
            return found.OrderByDescending(x => x.rank).ThenByDescending(x => x.e.Score).ThenByDescending(x => x.e.Last)
                .Take(max).Select(x => x.e).ToList();
        }
    }

    /// <summary>Una voce dei suggerimenti con il lavoro di Rank già fatto: nome e indirizzo in minuscolo e le loro parole.</summary>
    sealed class Item(Entry entry)
    {
        public readonly Entry Entry = entry;
        readonly string name = entry.Name.ToLowerInvariant(), email = entry.Email.ToLowerInvariant();
        readonly string[] words = Words(entry);

        /// <summary>0 se non corrisponde; 2 se la prima parola della ricerca è l'inizio del nome o dell'indirizzo, altrimenti 1.</summary>
        public int Rank(string[] terms)
        {
            foreach (var t in terms)
            {
                bool any = false;
                foreach (var w in words)
                    if (w.StartsWith(t, StringComparison.Ordinal))
                    {
                        any = true;
                        break;
                    }
                if (!any) return 0;
            }
            return name.StartsWith(terms[0], StringComparison.Ordinal) || email.StartsWith(terms[0], StringComparison.Ordinal) ? 2 : 1;
        }
    }

    /// <summary>L'indice dei suggerimenti: le voci nell'ordine di prima, per la versione della rubrica e il segno dei contatti con cui è nato.</summary>
    sealed record Index(long Book, long Contacts, List<Item> Items);

    static Index? index;
    /// <summary>Sotto Gate. Cresce a ogni cambio della rubrica (Add, lettura dal disco): l'indice di prima non vale più.</summary>
    static long bookVersion;

    static readonly char[] NameBreaks = [' ', '.', '-', '(', ')', '"'], CompanyBreaks = [' ', '.', '-', '(', ')', '"', ','], LocalBreaks = ['.', '-', '_', '+'];

    /// <summary>Le parole di una voce in minuscolo: del nome, dell'azienda, della parte prima della @, poi l'indirizzo intero e il dominio.</summary>
    static string[] Words(Entry e)
    {
        var email = e.Email.ToLowerInvariant();
        int at = email.IndexOf('@');
        return e.Name.ToLowerInvariant().Split(NameBreaks, StringSplitOptions.RemoveEmptyEntries)
            .Concat(e.Company.ToLowerInvariant().Split(CompanyBreaks, StringSplitOptions.RemoveEmptyEntries))
            .Concat(at > 0 ? email[..at].Split(LocalBreaks, StringSplitOptions.RemoveEmptyEntries) : [])
            .Concat(at > 0 ? [email, email[(at + 1)..]] : []).ToArray();
    }

    /// <summary>
    /// Sotto Gate. Le voci dei suggerimenti: la rubrica, poi i contatti (che prendono il posto della voce della rubrica con
    /// lo stesso indirizzo, con i suoi conteggi), poi i gruppi. Lo stesso ordine che Suggest costruiva a ogni chiamata.
    /// </summary>
    static List<Item> Build(Dictionary<string, Entry> book, IReadOnlyList<Contact> contacts)
    {
        var all = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in book.Values) all[e.Email] = e;
        var groups = new List<Entry>();
        foreach (var c in contacts)
        {
            if (c.IsGroup)
            {
                if (c.Members.Count > 0) groups.Add(new Entry { Name = c.Name, Group = c, Contact = true });
                continue;
            }
            foreach (var email in c.Emails.Where(Wanted))
            {
                all.TryGetValue(email, out var known);
                all[email] = new Entry
                {
                    Email = email, Name = c.Name.Contains('@') && known?.Name.Length > 0 ? known.Name : c.Name, Company = c.Company, Contact = true,
                    Sent = known?.Sent ?? 0, Received = known?.Received ?? 0, LastSent = known?.LastSent ?? default, LastReceived = known?.LastReceived ?? default,
                };
            }
        }
        return all.Values.Concat(groups).Select(e => new Item(e)).ToList();
    }

    /// <summary>
    /// Il segno dei contatti: cambia se cambia l'elenco, un contatto, o un suo campo che i suggerimenti usano (nome, azienda,
    /// indirizzi, se è un gruppo e se ha persone). Guarda gli oggetti, non i testi (un testo cambiato è un altro oggetto):
    /// qualche decina di microsecondi per migliaia di contatti, a ogni lettera.
    /// </summary>
    static long Mark(IReadOnlyList<Contact> contacts)
    {
        static int Of(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        var hash = new HashCode();
        hash.Add(contacts.Count);
        long sum = 0;
        foreach (var c in contacts)
        {
            hash.Add(Of(c));
            hash.Add(Of(c.DisplayName));
            hash.Add(Of(c.GivenName));
            hash.Add(Of(c.Surname));
            hash.Add(Of(c.Company));
            hash.Add(c.IsGroup ? c.Members.Count + 1 : 0);
            hash.Add(c.Emails.Count);
            foreach (var email in c.Emails) hash.Add(Of(email));
            sum += Of(c);
        }
        return ((long)hash.ToHashCode() << 32) ^ sum;
    }

#if DEBUG
    /// <summary>Prove: Suggest com'era prima dell'audit, blocco 8 (tutto rifatto a ogni chiamata), per confrontare risultati e tempi.</summary>
    internal static List<Entry> SuggestOld(string query, int max, ICollection<string>? exclude = null)
    {
        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return new();
        var contacts = Contacts?.Invoke() ?? [];
        lock (Gate)
        {
            var book = Ensure();
            var all = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in book.Values) all[e.Email] = e;
            var groups = new List<Entry>();
            foreach (var c in contacts)
            {
                if (c.IsGroup)
                {
                    if (c.Members.Count > 0) groups.Add(new Entry { Name = c.Name, Group = c, Contact = true });
                    continue;
                }
                foreach (var email in c.Emails.Where(Wanted))
                {
                    all.TryGetValue(email, out var known);
                    all[email] = new Entry
                    {
                        Email = email, Name = c.Name.Contains('@') && known?.Name.Length > 0 ? known.Name : c.Name, Company = c.Company, Contact = true,
                        Sent = known?.Sent ?? 0, Received = known?.Received ?? 0, LastSent = known?.LastSent ?? default, LastReceived = known?.LastReceived ?? default,
                    };
                }
            }
            return all.Values.Where(e => exclude == null || !exclude.Contains(e.Email)).Concat(groups)
                .Select(e => (e, rank: Rank(e, terms)))
                .Where(x => x.rank > 0)
                .OrderByDescending(x => x.rank).ThenByDescending(x => x.e.Score).ThenByDescending(x => x.e.Last)
                .Take(max).Select(x => x.e).ToList();
        }
    }

    /// <summary>
    /// Prove: una rubrica finta in memoria al posto di quella vera, mai salvata; con null torna quella vera. Così le prove
    /// con migliaia di voci non gonfiano rubrica.json della cartella di prova.
    /// </summary>
    internal static void TestBook(IEnumerable<Entry>? fake)
    {
        lock (Gate)
        {
            if (fake != null)
            {
                realBook ??= Ensure();
                entries = new(StringComparer.OrdinalIgnoreCase);
                foreach (var e in fake) entries[e.Email] = e;
            }
            else if (realBook != null)
            {
                entries = realBook;
                realBook = null;
            }
            bookVersion++;
        }
    }

    static Dictionary<string, Entry>? realBook;
#endif

    /// <summary>
    /// I suggerimenti con i colleghi trovati in Microsoft 365 (me/people) in fondo: ogni indirizzo una volta sola, né già
    /// proposto né già nel campo.
    /// </summary>
    public static List<Entry> WithColleagues(IEnumerable<Entry> shown, IEnumerable<Contact> colleagues, ICollection<string> present)
    {
        var result = shown.ToList();
        var known = result.Select(e => e.Email).Where(e => e.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var person in colleagues)
            foreach (var email in person.Emails.Where(e => !known.Contains(e) && !present.Contains(e)))
            {
                result.Add(new Entry { Name = person.Name, Email = email, Company = person.Company, Colleague = true });
                known.Add(email);
            }
        return result;
    }

    /// <summary>
    /// Da dove vengono i contatti dei suggerimenti: di base quelli di Persone (ContactBook.ForSuggestions); le prove lo
    /// cambiano. null: solo la rubrica.
    /// </summary>
    public static Func<IReadOnlyList<Contact>>? Contacts { get; set; } = ContactBook.ForSuggestions;

    /// <summary>
    /// Un mittente che si presenta con il nome di una persona a cui hai già scritto, ma da un altro indirizzo a cui non hai
    /// mai scritto: il trucco delle frodi sui bonifici ("Mario Rossi" da un indirizzo nuovo). Restituisce l'indirizzo che
    /// conosci per quel nome, o null.
    /// </summary>
    /// <remarks>
    /// Dall'audit (S-24): i nomi si confrontano per come si vedono (SenderCheck.Skeleton: caratteri invisibili, lettere
    /// cirilliche e greche che imitano le latine, larghezza intera, accenti aggiunti); dentro un servizio di posta personale
    /// (gmail.com, libero.it: Settings.ImageKey) conta l'indirizzo intero, perché lì lo stesso dominio non è la stessa
    /// organizzazione; mai per un proprio account o un altro proprio indirizzo.
    /// </remarks>
    public static string? LookAlike(Address from)
    {
        var name = from.Name.Trim().Trim('"', '\'');
        if (name.Contains('@')) return null; // un nome che è un indirizzo non è il nome di una persona
        var skeleton = SenderCheck.Skeleton(name);
        if (skeleton.Length < 4) return null;
        var email = from.Email.Trim();
        if (Store.Settings.Accounts.Any(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase)
                                             || a.Identities.Any(i => i.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))) return null;
        lock (Gate)
        {
            var all = Ensure();
            if (all.TryGetValue(email, out var self) && self.Sent > 0) return null;
            return all.Values.Where(e => e.Sent > 0 && e.Skeleton == skeleton && !e.Email.Equals(email, StringComparison.OrdinalIgnoreCase) && !SameSender(e.Email, email))
                .OrderByDescending(e => e.Sent).Select(e => e.Email).FirstOrDefault();
        }
    }

    /// <summary>
    /// Due indirizzi diversi che non sono un'imitazione: lo stesso dominio di un'organizzazione, o un suo sottodominio (il
    /// supporto e le newsletter di un servizio, i colleghi). Mai per la posta personale, dove ogni indirizzo è una persona.
    /// </summary>
    static bool SameSender(string known, string email) =>
        !Settings.ImageKey(known).Contains('@') && !Settings.ImageKey(email).Contains('@')
        && SenderCheck.SameOrganization(AccountTypes.DomainOf(known), AccountTypes.DomainOf(email));

#if DEBUG
    /// <summary>Solo per SuggestOld: il confronto di prima, che rifà le parole di ogni voce a ogni chiamata.</summary>
    static int Rank(Entry e, string[] terms)
    {
        var name = e.Name.ToLowerInvariant();
        var email = e.Email.ToLowerInvariant();
        int at = email.IndexOf('@');
        var words = name.Split(new[] { ' ', '.', '-', '(', ')', '"' }, StringSplitOptions.RemoveEmptyEntries)
            .Concat(e.Company.ToLowerInvariant().Split(new[] { ' ', '.', '-', '(', ')', '"', ',' }, StringSplitOptions.RemoveEmptyEntries))
            .Concat(at > 0 ? email[..at].Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries) : [])
            .Concat(at > 0 ? [email, email[(at + 1)..]] : []).ToList();
        foreach (var t in terms)
            if (!words.Any(w => w.StartsWith(t, StringComparison.Ordinal))) return 0;
        return name.StartsWith(terms[0], StringComparison.Ordinal) || email.StartsWith(terms[0], StringComparison.Ordinal) ? 2 : 1;
    }
#endif

    /// <summary>
    /// Destinatari della Posta inviata dell'account, se non letti nell'ultima ora: i messaggi dal più vecchio, perché
    /// contano solo quelli più recenti dell'ultimo già visto per ogni indirizzo.
    /// </summary>
    public static async Task ScanSentAsync(IMailProvider provider)
    {
        var id = provider.Account.Id;
        lock (Gate)
        {
            Ensure();
            if (sentScans.TryGetValue(id, out var last) && DateTime.UtcNow - last < ScanInterval || !scanning.Add(id)) return;
        }
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var sent = await provider.GetSentRecipientsAsync(SentScanSize);
            Log.Time($"rubrica: {sent.Count} messaggi inviati di {provider.Account.Email}", clock);
            lock (Gate)
            {
                foreach (var m in sent.OrderBy(m => m.Date))
                    foreach (var a in m.Recipients) Add(a, m.Date, sent: true);
                sentScans[id] = DateTime.UtcNow;
                ScheduleSave();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"rubrica dalla Posta inviata di {provider.Account.Email}", ex);
        }
        finally
        {
            lock (Gate) scanning.Remove(id);
        }
    }

    /// <summary>Sotto Gate. Si salva qualche secondo dopo l'ultima modifica, non a ogni elenco.</summary>
    static void ScheduleSave() => saveTimer ??= new Timer(_ => Save(), null, 5000, Timeout.Infinite);

    /// <summary>Una scrittura della rubrica alla volta, fuori da Gate.</summary>
    static readonly object writing = new();
    /// <summary>Le copie prese per il file, numerate (sotto Gate), e l'ultima arrivata sul file (sotto writing).</summary>
    static long snapshots, written;

    /// <summary>
    /// Sotto Gate solo la copia in JSON (0,3 ms con 364 voci); cifratura e file (11 ms con Flush, di più con una rubrica
    /// grande) fuori: Gate lo prende anche l'interfaccia a ogni messaggio aperto (LookAlike), a ogni elenco (Received) e a
    /// ogni lettera nel campo A (Suggest), e prima aspettava tutta la scrittura (audit del 7 ottobre, B-14). Le copie sono
    /// numerate: sul file non torna mai una più vecchia di quella che c'è. Chi chiama torna a scrittura finita (l'uscita, la
    /// copia di sicurezza).
    /// </summary>
    public static void Save()
    {
        byte[] json;
        long number;
        lock (Gate)
        {
            saveTimer?.Dispose();
            saveTimer = null;
            var book = entries;
#if DEBUG
            if (realBook != null) book = realBook; // prove con una rubrica finta (TestBook): quella non si salva mai
#endif
            if (book == null) return;
            try
            {
                json = JsonSerializer.SerializeToUtf8Bytes(new Data { Entries = book.Values.ToList(), SentScans = sentScans }, Json);
            }
            catch (Exception ex)
            {
                Log.Error("scrittura della rubrica", ex);
                return;
            }
            number = ++snapshots;
        }
        lock (writing)
        {
            if (number < written) return; // sul file c'è già una copia più nuova
            try
            {
#if DEBUG
                TestWriting?.Invoke();
#endif
                Directory.CreateDirectory(Store.Dir);
                SafeFile.Write(FilePath, SafeFile.Protect(json));
                written = number;
            }
            catch (Exception ex)
            {
                Log.Error("scrittura della rubrica", ex);
            }
        }
    }

#if DEBUG
    /// <summary>Prove: chiamata a ogni scrittura della rubrica, fuori da Gate, prima di cifrare e scrivere.</summary>
    internal static Action? TestWriting;

    /// <summary>Prove: la rubrica si rilegge dal file.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            saveTimer?.Dispose();
            saveTimer = null;
            entries = null;
            sentScans = new();
            index = null;
        }
    }
#endif
}
