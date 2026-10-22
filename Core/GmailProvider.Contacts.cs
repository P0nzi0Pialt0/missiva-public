using System.Text.Json.Nodes;

namespace Missiva;

/// <summary>
/// I contatti di Google di un account Gmail o Google Workspace (People API). Due permessi, tutti e due solo dai pulsanti in Persone:
/// "Consenti l'accesso ai contatti" (contacts.readonly: si leggono soltanto) e "Consenti la modifica dei contatti" (contacts, che
/// sostituisce il primo: si leggono, si creano, si cambiano e si eliminano da Missiva). Senza permessi null e nessuna richiesta.
/// Servono a Persone e ai suggerimenti dei destinatari. Le scritture: vedi <see cref="GoogleContacts"/>.
/// </summary>
public sealed partial class GmailProvider
{
    static string ContactsWriteConsentNeeded => T("Per cambiare i contatti di Google Missiva deve poterli modificare: premi \"Consenti la modifica dei contatti\" in Persone.");

#if DEBUG
    /// <summary>Prove (--google-scrittura, --google-sola-lettura e --prova-bozze): decide se i contatti si dicono modificabili, senza guardare gli ambiti dell'account.</summary>
    internal static bool? TestContactsWritable;
#endif

    public bool HasContacts => true;

    /// <summary>
    /// Sola lettura finché l'account non ha dato l'ambito di scrittura (GoogleAuth.ContactsWriteScope): Persone apre Modifica in Google
    /// Contatti e propone "Consenti la modifica dei contatti"; con l'ambito Modifica, Elimina e Nuovo contatto lavorano da Missiva.
    /// </summary>
    public bool ContactsReadOnly =>
#if DEBUG
        TestContactsWritable is { } writable ? !writable :
#endif
        !GoogleAuth.HasContactsWrite(Account);

    public Task AllowContactsAsync() => AllowAsync(GoogleAuth.ContactsScope, "i contatti",
        chosen => T("Nel browser hai scelto {0}, non {1}: per i contatti serve lo stesso account.", chosen, Account.Email),
        T("Non hai dato a Missiva il permesso di usare i contatti. Riprova e lascia spuntata la casella dei contatti."));

    /// <summary>Il permesso di modificare i contatti: un nuovo accesso nel browser con contacts al posto di contacts.readonly e gli altri ambiti com'erano. Solo dal pulsante.</summary>
    public Task AllowContactsWriteAsync() => AllowAsync(GoogleAuth.ContactsWriteScope, "la modifica dei contatti",
        chosen => T("Nel browser hai scelto {0}, non {1}: per la modifica dei contatti serve lo stesso account.", chosen, Account.Email),
        T("Non hai dato a Missiva il permesso di usare la modifica dei contatti. Riprova e lascia spuntata la casella della modifica dei contatti."));

    /// <summary>
    /// I contatti di Google (people/me/connections) a pagine di 1000, al massimo 20 pagine, solo i campi della scheda. Senza il
    /// permesso dei contatti null e nessuna richiesta.
    /// </summary>
    public async Task<List<Contact>?> GetContactsAsync(CancellationToken cancel = default)
    {
        if (!GoogleAuth.HasContacts(Account)) return null;
        var found = new List<Contact>();
        string? page = null;
        for (int n = 0; n < 20; n++)
        {
            var answer = await GoogleContacts.ListAsync(Account, page, cancel);
            found.AddRange((answer?["connections"]?.AsArray() ?? new JsonArray()).OfType<JsonNode>().Select(p => GoogleContacts.FromPerson(p, Account.Id)));
            page = answer?["nextPageToken"] is JsonValue v && v.TryGetValue<string>(out var next) && next.Length > 0 ? next : null;
            if (page == null) break;
        }
        return found.Where(c => c.Emails.Count > 0 || c.Name != Contact.NoName).ToList();
    }

    /// <summary>
    /// Un contatto nuovo (before null: people:createContact) o cambiato (rilettura, unione dei soli campi cambiati, updateContact con l'etag).
    /// Dopo la scrittura after prende i campi come li ha salvati Google (la persona restituita), tenendo il suo Id: l'elenco di Persone
    /// non aspetta che Google li mostri nelle letture. Senza l'ambito di scrittura nessuna richiesta.
    /// </summary>
    public async Task<string> SaveContactAsync(Contact? before, Contact after)
    {
        if (!GoogleAuth.HasContactsWrite(Account)) throw new MailException(ContactsWriteConsentNeeded, null, 403);
        var saved = before?.ServerId is { Length: > 0 } ? await GoogleContacts.UpdateAsync(Account, before, after) : await GoogleContacts.CreateAsync(Account, after);
        if (saved == null) return before!.ServerId!; // nulla da cambiare, nessuna richiesta
        GoogleContacts.CopyInto(saved, after);
        Log.Write(before == null ? $"contatti di Google: contatto creato in {Account.Email}" : $"contatti di Google: contatto aggiornato in {Account.Email}");
        return saved.ServerId ?? "";
    }

    /// <summary>Elimina il contatto da Google Contatti: sparisce anche dal telefono e dagli altri dispositivi dell'account.</summary>
    public async Task DeleteContactAsync(Contact contact)
    {
        if (!GoogleAuth.HasContactsWrite(Account)) throw new MailException(ContactsWriteConsentNeeded, null, 403);
        await GoogleContacts.DeleteAsync(Account, contact);
        Log.Write($"contatti di Google: contatto eliminato da {Account.Email}");
    }
}
