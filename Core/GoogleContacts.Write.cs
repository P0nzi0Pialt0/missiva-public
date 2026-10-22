using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Missiva;

/// <summary>
/// Un contatto di Google cambiato mentre lo si modificava (etag scaduto anche dopo una rilettura): Fresh è la persona com'è ora
/// su Google, per riaprire la finestra con i dati nuovi.
/// </summary>
public sealed class ContactChangedException(Contact fresh) : Exception(T("Il contatto è stato cambiato altrove: riaperto con i dati nuovi."))
{
    public Contact Fresh { get; } = fresh;
}

/// <summary>
/// Scrivere i contatti di Google (2 ottobre 2026, agente contatti-III): createContact, updateContact e deleteContact della People API,
/// con l'ambito .../auth/contacts (GoogleAuth.ContactsWriteScope) chiesto solo dal pulsante "Consenti la modifica dei contatti".
/// Regole della documentazione, tutte rispettate qui: updatePersonFields SOSTITUISCE per intero i campi elencati, quindi prima di
/// aggiornare si rilegge la persona (etag fresco e JSON vero) e le modifiche di Missiva si applicano sopra, solo ai campi cambiati,
/// tenendo le voci che Missiva non mostra (fax, altri tipi di telefono, etichette, altri indirizzi e siti, parti del nome);
/// un etag scaduto (400 FAILED_PRECONDITION o 409) rilegge e riprova una volta sola; le scritture dello stesso account vanno
/// una alla volta; chi scrive riceve la persona restituita da Google (che nelle letture può comparire solo dopo qualche secondo).
/// </summary>
public static partial class GoogleContacts
{
    static readonly GoogleService WriteService = new("Google People API", "people",
        N("Missiva non ha il permesso di usare la modifica dei contatti di questo account: premi \"Consenti la modifica dei contatti\" in Persone."),
        N("Google ha limitato le richieste (la modifica dei contatti): riprova fra poco."));
    static readonly Regex ResourceNameRule = new(@"^people/[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();


    static string Resource(string? resourceName) =>
        resourceName != null && ResourceNameRule.IsMatch(resourceName) ? resourceName : throw new MailException(T("Il contatto non ha un identificativo di Google valido: rileggi i contatti."));

    /// <summary>Una sola scrittura alla volta per account (la People API lo chiede: altrimenti errori di concorrenza).</summary>
    static async Task<IDisposable> GateAsync(MailAccount account, CancellationToken cancel)
    {
        var gate = Gates.GetOrAdd(account.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancel);
        return new Release(gate);
    }

    sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    /// <summary>Una persona di Google com'è ora (people/{id} con gli stessi campi dell'elenco): l'etag fresco e il JSON vero.</summary>
    static async Task<JsonNode> GetAsync(MailAccount account, string resourceName, CancellationToken cancel)
    {
        try
        {
            return await GoogleApi.SendAsync(WriteService, account, HttpMethod.Get, $"{Base}{resourceName}?personFields={PersonFields}", cancel: cancel)
                   ?? throw new MailException(T("Google non ha risposto con il contatto."));
        }
        catch (MailException ex) when (ex.Status == 404)
        {
            throw new MailException(T("Il contatto è stato eliminato su Google (da un altro dispositivo): non c'è più."), ex, 404);
        }
    }

    /// <summary>Un etag non più valido: il contatto è cambiato altrove (400 FAILED_PRECONDITION o 409).</summary>
    static bool IsStale(MailException ex) => ex.Status == 409 || ex.Status == 400 && (ex.Code == "FAILED_PRECONDITION" || ex.Message.Contains("etag", StringComparison.OrdinalIgnoreCase));

    /// <summary>Un contatto nuovo: POST people:createContact con i soli campi scritti. Restituisce la persona come l'ha salvata Google.</summary>
    public static async Task<Contact> CreateAsync(MailAccount account, Contact contact, CancellationToken cancel = default)
    {
        var body = Merge(null, null, contact).Person;
        using var _ = await GateAsync(account, cancel);
        var answer = await GoogleApi.SendAsync(WriteService, account, HttpMethod.Post, $"{Base}people:createContact?personFields={PersonFields}", body, cancel)
                     ?? throw new MailException(T("Google non ha risposto con il contatto creato."));
        return FromPerson(answer, account.Id);
    }

    /// <summary>
    /// Un contatto cambiato: rilegge la persona, applica sopra le modifiche (solo i campi che differiscono da before), PATCH
    /// people/{id}:updateContact con updatePersonFields e l'etag. Etag scaduto: rilegge e riprova UNA volta, poi
    /// <see cref="ContactChangedException"/>. Null se nulla è cambiato (nessuna richiesta).
    /// </summary>
    public static async Task<Contact?> UpdateAsync(MailAccount account, Contact before, Contact after, CancellationToken cancel = default)
    {
        var resource = Resource(before.ServerId);
        if (Merge(null, before, after).Fields.Count == 0) return null;
        using var _ = await GateAsync(account, cancel);
        for (int attempt = 0; ; attempt++)
        {
            var fresh = await GetAsync(account, resource, cancel);
            var merged = Merge(fresh, before, after);
            if (merged.Fields.Count == 0) return FromPerson(fresh, account.Id); // già com'è su Google
            merged.Person["etag"] = Str(fresh, "etag");
            try
            {
                var answer = await GoogleApi.SendAsync(WriteService, account, HttpMethod.Patch,
                    $"{Base}{resource}:updateContact?updatePersonFields={string.Join(",", merged.Fields)}&personFields={PersonFields}", merged.Person, cancel);
                return FromPerson(answer ?? throw new MailException(T("Google non ha risposto con il contatto aggiornato.")), account.Id);
            }
            catch (MailException ex) when (IsStale(ex))
            {
                if (attempt > 0) throw new ContactChangedException(FromPerson(fresh, account.Id));
                Log.Write("contatti di Google: etag scaduto, rileggo e riprovo una volta");
            }
        }
    }

    /// <summary>Elimina: DELETE people/{id}:deleteContact. Un contatto già eliminato altrove (404) è come eliminato.</summary>
    public static async Task DeleteAsync(MailAccount account, Contact contact, CancellationToken cancel = default)
    {
        var resource = Resource(contact.ServerId);
        using var _ = await GateAsync(account, cancel);
        try
        {
            await GoogleApi.SendAsync(WriteService, account, HttpMethod.Delete, $"{Base}{resource}:deleteContact", cancel: cancel);
        }
        catch (MailException ex) when (ex.Status == 404)
        {
            Log.Write("contatti di Google: il contatto era già stato eliminato altrove");
        }
    }

    /// <summary>I campi di un contatto salvato (come lo dice Google) rimessi in quello dell'elenco, che tiene il suo Id.</summary>
    internal static void CopyInto(Contact saved, Contact target)
    {
        target.ServerId = saved.ServerId;
        target.Source = saved.Source;
        target.DisplayName = saved.DisplayName;
        target.GivenName = saved.GivenName;
        target.Surname = saved.Surname;
        target.Emails = saved.Emails.ToList();
        target.Mobile = saved.Mobile;
        target.BusinessPhone = saved.BusinessPhone;
        target.HomePhone = saved.HomePhone;
        target.Company = saved.Company;
        target.JobTitle = saved.JobTitle;
        target.Address = saved.Address;
        target.Notes = saved.Notes;
        target.Website = saved.Website;
    }

    // ---------------------------------------------------------------- l'unione

    /// <summary>Il corpo della persona (solo i campi cambiati, senza etag) e i loro nomi per updatePersonFields.</summary>
    public sealed record PersonChange(JsonObject Person, List<string> Fields);

    static bool Differs(string? a, string? b) => (a ?? "").Trim() != (b ?? "").Trim();

    static JsonArray ListOf(JsonNode? current, string key) =>
        new((current?[key]?.AsArray() ?? new JsonArray()).OfType<JsonNode>().Select(n => n.DeepClone()).ToArray());

    /// <summary>La voce preferita di un elenco (segnata principale, o la prima) come oggetto da cambiare.</summary>
    static JsonObject? MainItem(JsonArray list) => Main(list) as JsonObject;

    /// <summary>Se di una voce resta qualcosa oltre ai campi che Google compone da sé (metadata, tipo, nome mostrato...).</summary>
    static bool HasContent(JsonObject item, params string[] ignore) =>
        item.Any(p => !ignore.Contains(p.Key) && p.Key != "metadata" && p.Value is not null && !(p.Value is JsonValue v && v.TryGetValue<string>(out var s) && s.Length == 0));

    static void SetOrRemove(JsonObject item, string key, string value)
    {
        if (value.Trim().Length > 0) item[key] = value.Trim();
        else item.Remove(key);
    }

    /// <summary>
    /// Le modifiche di Missiva applicate sopra la persona di Google com'è ora (current; senza, un contatto nuovo): per ogni campo
    /// a elenco cambiato rispetto a before (il contatto com'era quando è stato letto) la nuova lista, intera, con le voci che Missiva
    /// non mostra o non cambia rimaste dove erano; i campi che non sono cambiati non si mandano affatto (updatePersonFields non
    /// li nomina e Google li lascia come sono). Telefoni al contrario di FromPerson: lo stesso posto (cellulare, ufficio, casa) che
    /// la scheda mostrava, anche se la voce è di altro tipo; i fax e gli altri numeri restano. Indirizzo: una riga, come
    /// formattedValue senza le parti (via, città...) che dicevano un'altra cosa; non lo cambio se non è cambiato. Nome mostrato:
    /// Google lo compone da nome e cognome, e lo scrivo (unstructuredName) solo se nome e cognome sono vuoti.
    /// </summary>
    public static PersonChange Merge(JsonNode? current, Contact? before, Contact after)
    {
        var was = before ?? new Contact();
        var person = new JsonObject();
        var fields = new List<string>();
        void Put(string field, JsonArray list)
        {
            person[field] = list;
            fields.Add(field);
        }

        // names: nome e cognome sulla voce principale; le altre parti (secondo nome, titoli, fonetica) restano.
        bool structured = Differs(after.GivenName, was.GivenName) || Differs(after.Surname, was.Surname);
        bool unstructured = !structured && Differs(after.DisplayName, was.DisplayName) && after.GivenName.Trim().Length == 0 && after.Surname.Trim().Length == 0;
        if (structured || unstructured)
        {
            var list = ListOf(current, "names");
            var item = MainItem(list);
            if (item == null)
            {
                item = new JsonObject();
                list.Add(item);
            }
            if (structured)
            {
                SetOrRemove(item, "givenName", after.GivenName);
                SetOrRemove(item, "familyName", after.Surname);
                item.Remove("displayName");
                item.Remove("displayNameLastFirst");
                if (after.GivenName.Trim().Length == 0 && after.Surname.Trim().Length == 0) SetOrRemove(item, "unstructuredName", after.DisplayName);
                else item.Remove("unstructuredName");
            }
            else SetOrRemove(item, "unstructuredName", after.DisplayName);
            if (!HasContent(item, "displayName", "displayNameLastFirst", "type", "formattedType")) list.Remove(item);
            Put("names", list);
        }

        // emailAddresses: tolte quelle tolte, aggiunte quelle aggiunte; le altre con il loro tipo.
        var removedMail = was.Emails.Where(e => !after.Emails.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();
        var addedMail = after.Emails.Where(e => !was.Emails.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();
        if (removedMail.Count + addedMail.Count > 0)
        {
            var list = ListOf(current, "emailAddresses");
            foreach (var gone in list.OfType<JsonNode>().Where(n => removedMail.Contains(Str(n, "value").Trim(), StringComparer.OrdinalIgnoreCase)).ToList()) list.Remove(gone);
            foreach (var added in addedMail)
                if (!list.OfType<JsonNode>().Any(n => Str(n, "value").Trim().Equals(added, StringComparison.OrdinalIgnoreCase))) list.Add(new JsonObject { ["value"] = added });
            Put("emailAddresses", list);
        }

        // phoneNumbers: i tre posti della scheda; fax, cercapersone e altri numeri non si toccano.
        if (Differs(after.Mobile, was.Mobile) || Differs(after.BusinessPhone, was.BusinessPhone) || Differs(after.HomePhone, was.HomePhone))
        {
            var list = ListOf(current, "phoneNumbers");
            var (mobileItem, businessItem, homeItem) = PhoneItems(list);
            void Slot(JsonNode? item, string nowText, string wasText, string type)
            {
                if (!Differs(nowText, wasText)) return;
                if (nowText.Trim().Length == 0)
                {
                    if (item != null) list.Remove(item);
                }
                else if (item is JsonObject existing)
                {
                    existing["value"] = nowText.Trim();
                    existing.Remove("canonicalForm");
                }
                else list.Add(new JsonObject { ["value"] = nowText.Trim(), ["type"] = type });
            }
            Slot(mobileItem, after.Mobile, was.Mobile, "mobile");
            Slot(businessItem, after.BusinessPhone, was.BusinessPhone, "work");
            Slot(homeItem, after.HomePhone, was.HomePhone, "home");
            Put("phoneNumbers", list);
        }

        // organizations: azienda e ruolo della voce principale; reparto e il resto restano.
        if (Differs(after.Company, was.Company) || Differs(after.JobTitle, was.JobTitle))
        {
            var list = ListOf(current, "organizations");
            var item = MainItem(list);
            if (item == null)
            {
                item = new JsonObject();
                list.Add(item);
            }
            if (Differs(after.Company, was.Company)) SetOrRemove(item, "name", after.Company);
            if (Differs(after.JobTitle, was.JobTitle)) SetOrRemove(item, "title", after.JobTitle);
            if (!HasContent(item, "type", "formattedType", "current", "startDate", "endDate")) list.Remove(item);
            Put("organizations", list);
        }

        // addresses: la voce che la scheda mostrava diventa il testo scritto (formattedValue), senza le parti vecchie; le altre restano.
        if (Differs(after.Address, was.Address))
        {
            var list = ListOf(current, "addresses");
            var item = MainItem(list);
            if (after.Address.Trim().Length == 0)
            {
                if (item != null) list.Remove(item);
            }
            else
            {
                if (item == null)
                {
                    item = new JsonObject();
                    list.Add(item);
                }
                foreach (var part in new[] { "poBox", "streetAddress", "extendedAddress", "city", "region", "postalCode", "country", "countryCode" }) item.Remove(part);
                item["formattedValue"] = after.Address.Trim();
            }
            Put("addresses", list);
        }

        // urls: il sito della voce principale (la finestra non lo mostra: se non cambia non si manda, e i siti restano).
        if (Differs(after.Website, was.Website))
        {
            var list = ListOf(current, "urls");
            var item = MainItem(list);
            if (after.Website.Trim().Length == 0)
            {
                if (item != null) list.Remove(item);
            }
            else if (item != null) item["value"] = after.Website.Trim();
            else list.Add(new JsonObject { ["value"] = after.Website.Trim() });
            Put("urls", list);
        }

        // biographies: le note, testo semplice.
        if (Differs(after.Notes, was.Notes))
        {
            var list = ListOf(current, "biographies");
            var item = MainItem(list);
            if (after.Notes.Trim().Length == 0)
            {
                if (item != null) list.Remove(item);
            }
            else
            {
                if (item == null)
                {
                    item = new JsonObject();
                    list.Add(item);
                }
                item["value"] = after.Notes.Trim();
                item["contentType"] = "TEXT_PLAIN";
            }
            Put("biographies", list);
        }
        return new PersonChange(person, fields);
    }
}
