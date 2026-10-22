using System.Text.Json.Nodes;

namespace Missiva;

/// <summary>
/// I contatti di Google (People API v1, https://people.googleapis.com/v1/) letti con lo stesso accesso della posta e
/// l'ambito contacts.readonly (GoogleAuth.ContactsScope, "Consenti l'accesso ai contatti") o contacts (ContactsWriteScope,
/// "Consenti la modifica dei contatti", che permette anche di scrivere: GoogleContacts.Write.cs).
/// Qui la richiesta e il passaggio dal JSON di una persona di Google a un Contact di Missiva.
/// </summary>
public static partial class GoogleContacts
{
    const string Base = "https://people.googleapis.com/v1/";
    static readonly GoogleService Service = new("Google People API", "people",
        N("Missiva non ha il permesso di usare i contatti di questo account: premi \"Consenti l'accesso ai contatti\" in Persone."),
        N("Google ha limitato le richieste (i contatti): riprova fra poco."));

    /// <summary>I campi della scheda: nomi, indirizzi, telefoni, azienda e ruolo, indirizzo, note.</summary>
    public const string PersonFields = "names,emailAddresses,phoneNumbers,organizations,addresses,biographies,urls";

    /// <summary>Una pagina di contatti (1000 al più, dal nome), da page (nextPageToken della pagina prima).</summary>
    public static Task<JsonNode?> ListAsync(MailAccount account, string? page, CancellationToken cancel = default) =>
        GoogleApi.SendAsync(Service, account, HttpMethod.Get,
            $"{Base}people/me/connections?personFields={PersonFields}&pageSize=1000&sortOrder=FIRST_NAME_ASCENDING" + (page != null ? "&pageToken=" + Uri.EscapeDataString(page) : ""),
            cancel: cancel);

    static string Str(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>Il valore preferito di un elenco di Google: quello segnato come principale (metadata.primary), altrimenti il primo.</summary>
    static JsonNode? Main(JsonNode? list)
    {
        var all = (list?.AsArray() ?? new JsonArray()).OfType<JsonNode>().ToList();
        return all.FirstOrDefault(x => x["metadata"]?["primary"] is JsonValue p && p.TryGetValue<bool>(out var primary) && primary) ?? all.FirstOrDefault();
    }

    /// <summary>
    /// Le voci dei tre posti della scheda fra i telefoni di una persona: cellulare (mobile), ufficio (work, main), casa (home); gli
    /// altri tipi (altro, cercapersone) riempiono il primo posto libero, i fax no. La stessa regola legge (FromPerson) e scrive (Merge).
    /// </summary>
    internal static (JsonNode? Mobile, JsonNode? Business, JsonNode? Home) PhoneItems(JsonArray? phones)
    {
        JsonNode? mobile = null, business = null, home = null;
        var others = new List<JsonNode>();
        foreach (var phone in (phones ?? new JsonArray()).OfType<JsonNode>())
        {
            if (Str(phone, "value").Trim().Length == 0) continue;
            switch (Str(phone, "type").ToLowerInvariant())
            {
                case "mobile" or "workmobile": if (mobile == null) mobile = phone; else others.Add(phone); break;
                case "work" or "main": if (business == null) business = phone; else others.Add(phone); break;
                case "home": if (home == null) home = phone; else others.Add(phone); break;
                case "homefax" or "workfax" or "otherfax": break;
                default: others.Add(phone); break;
            }
        }
        foreach (var phone in others)
        {
            if (mobile == null) mobile = phone;
            else if (business == null) business = phone;
            else if (home == null) home = phone;
        }
        return (mobile, business, home);
    }

    /// <summary>Una persona di Google come contatto di Missiva. Telefoni nei tre posti di <see cref="PhoneItems"/>. L'indirizzo in una riga.</summary>
    public static Contact FromPerson(JsonNode p, string accountId)
    {
        var name = Main(p["names"]);
        var org = Main(p["organizations"]);
        var (mobileItem, businessItem, homeItem) = PhoneItems(p["phoneNumbers"]?.AsArray());
        string mobile = Str(mobileItem, "value").Trim(), business = Str(businessItem, "value").Trim(), home = Str(homeItem, "value").Trim();
        var address = Str(Main(p["addresses"]), "formattedValue").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new Contact
        {
            Source = accountId,
            ServerId = Str(p, "resourceName"),
            DisplayName = Str(name, "displayName"),
            GivenName = Str(name, "givenName"),
            Surname = Str(name, "familyName"),
            Emails = (p["emailAddresses"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>().Select(e => Str(e, "value").Trim())
                .Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Mobile = mobile,
            BusinessPhone = business,
            HomePhone = home,
            Company = Str(org, "name"),
            JobTitle = Str(org, "title"),
            Address = string.Join(", ", address),
            Notes = Str(Main(p["biographies"]), "value").Trim(),
            Website = Str(Main(p["urls"]), "value").Trim(),
        };
    }
}
