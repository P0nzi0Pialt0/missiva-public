#if DEBUG
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Missiva;

/// <summary>Le prove del giro P5 (--prova-bozze): contatti nei suggerimenti, colleghi di Microsoft 365, gruppi.</summary>
static partial class ProvaBozze
{
    /// <summary>
    /// Contatti nei suggerimenti (giro P5): i contatti di Persone (anche mai scritti) con l'azienda e tutti gli indirizzi,
    /// i gruppi che si espandono, i colleghi di me/people con il gestore finto (senza consenso nessuna richiesta, $search con
    /// il testo scritto, risultati uniti senza doppioni), la copia cifrata dei contatti di Outlook e i gruppi cifrati.
    /// </summary>
    static async Task PeopleSuggestAsync()
    {
        Report.Add("--- Contatti nei suggerimenti, colleghi, gruppi");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var giulia = new Contact { GivenName = "Giulia", Surname = "Ferri" + tag, Emails = [$"giulia.ferri{tag}@cliente-prova.example", $"g.ferri{tag}@pec-prova.example"], Company = "Ferri Impianti srl" };
        var group = new Contact
        {
            IsGroup = true, DisplayName = "Soci" + tag,
            Members = [$"Anna Bianchi <anna{tag}@studio-prova.example>", $"luca{tag}@studio-prova.example", $"Carla Neri <carla{tag}@consulenti-prova.example>"],
        };
        var before = AddressBook.Contacts;
        AddressBook.Contacts = () => [giulia, group];
        try
        {
            var byName = AddressBook.Suggest("Giu", 50);
            var bySurname = AddressBook.Suggest("Ferri" + tag, 8);
            var byCompany = AddressBook.Suggest("Impianti", 50);
            Check(byName.Any(e => e.Email == giulia.Emails[0] && e.Name == $"Giulia Ferri{tag}" && e.Detail == $"{giulia.Emails[0]} · Ferri Impianti srl" && e.Contact)
                  && bySurname.Count(e => e.Contact) == 2 && bySurname.Any(e => e.Email == giulia.Emails[1]) && byCompany.Any(e => e.Email == giulia.Emails[0]),
                  $"un contatto di Persone mai scritto esce scrivendo \"Giu\" e il cognome (anche l'altro suo indirizzo), e con l'azienda: {string.Join(", ", bySurname.Select(e => e.AutomationName))}");
            var soci = AddressBook.Suggest("soci" + tag, 8).SingleOrDefault();
            Check(soci is { Group: not null } && soci.Detail == "Gruppo, 3 persone"
                  && soci.ToString() == $"Anna Bianchi <anna{tag}@studio-prova.example>; luca{tag}@studio-prova.example; Carla Neri <carla{tag}@consulenti-prova.example>",
                  $"gruppo nei suggerimenti: scelto, diventa gli indirizzi delle sue persone ({soci})");
            // "Stai scrivendo a persone esterne": il gruppo espanso ha una persona di un altro dominio.
            var outside = group.MemberAddresses.Where(a => !SenderCheck.SameOrganization(AccountTypes.DomainOf(a.Email), "studio-prova.example")).Select(a => a.Email).ToList();
            Check(outside.SequenceEqual([$"carla{tag}@consulenti-prova.example"]), "il gruppo espanso nel campo A: fra le sue persone quella esterna allo studio (la barra delle persone esterne la conta)");
            var excluded = AddressBook.Suggest("Giu", 50, new HashSet<string>([giulia.Emails[0]], StringComparer.OrdinalIgnoreCase));
            Check(!excluded.Any(e => e.Email == giulia.Emails[0]), "chi è già nel campo non si propone di nuovo, anche se è un contatto");
        }
        finally
        {
            AddressBook.Contacts = before;
        }

        // Colleghi di Microsoft 365 con il gestore finto.
        var requests = new List<string>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            requests.Add(path + " " + request.Headers.Authorization?.Parameter);
            var json = new JsonObject
            {
                ["value"] = new JsonArray(
                    new JsonObject { ["displayName"] = "Giulia Ferri", ["companyName"] = "Studio Prova", ["scoredEmailAddresses"] = new JsonArray(new JsonObject { ["address"] = "giulia.ferri@studio-prova.it" }) },
                    new JsonObject { ["displayName"] = "Giulio Conti", ["jobTitle"] = "Praticante", ["scoredEmailAddresses"] = new JsonArray(new JsonObject { ["address"] = "giulio.conti@studio-prova.it" }, new JsonObject { ["address"] = "g.conti@studio-prova.it" }) },
                    new JsonObject { ["displayName"] = "Sala riunioni", ["scoredEmailAddresses"] = new JsonArray() }),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        var work = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.it", Tenant = "organizations" });
        var personal = new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "mario@outlook.it" });
        try
        {
            GraphProvider.TestPeopleConsent = false;
            var none = await work.SearchPeopleAsync("Giu");
            Check(none == null && requests.Count == 0 && work.HasPeople && !personal.HasPeople && await personal.SearchPeopleAsync("Giu") == null,
                  "colleghi: senza il consenso People.Read nessuna richiesta a me/people; per Outlook.com niente colleghi");
            GraphProvider.TestPeopleConsent = true;
            var found = await work.SearchPeopleAsync("Giu");
            Check(found is [{ Name: "Giulia Ferri", Company: "Studio Prova" }, { Emails.Count: 2 }] && requests.Single().StartsWith("me/people?$search=\"Giu\"&$top=10&$select=displayName,scoredEmailAddresses,companyName,jobTitle prova-persone"),
                  $"colleghi: $search con il testo scritto e il token delle persone ({requests.Single()}); le voci senza indirizzo restano fuori");
            var shown = new List<AddressBook.Entry> { new() { Name = "Giulia Ferri", Email = "giulia.ferri@studio-prova.it", Sent = 3 }, new() { Name = "Giuseppe Verdi", Email = "giuseppe@esempio.example" } };
            var merged = AddressBook.WithColleagues(shown, found!, new HashSet<string>(["g.conti@studio-prova.it"], StringComparer.OrdinalIgnoreCase));
            Check(merged.Select(e => e.Email).SequenceEqual(["giulia.ferri@studio-prova.it", "giuseppe@esempio.example", "giulio.conti@studio-prova.it"]) && merged[2].Colleague && merged[2].Name == "Giulio Conti",
                  "colleghi uniti ai suggerimenti senza doppioni (Giulia c'era già, g.conti è già nel campo)");
            requests.Clear();
            var testing = new TestingProvider(work);
            await testing.AllowPeopleAsync();
            Check(requests.Count == 0 && File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).TakeLast(3).Any(l => l.Contains("prova: chiederei il consenso alle persone (colleghi) per mario.rossi@studio-prova.it")),
                  "nella prova \"Cerca anche fra i colleghi\" senza consenso scrive solo nel registro, nessuna finestra");
        }
        finally
        {
            GraphProvider.TestPeopleConsent = false;
        }

        // Copia cifrata dei contatti di Outlook e gruppi cifrati in contatti.dat.
        var accountId = "prova-" + tag;
        ContactBook.SetRemote(accountId, [giulia]);
        var file = Path.Combine(Store.Dir, "cache", $"contatti-{accountId}.dat");
        for (int wait = 0; wait < 50 && !File.Exists(file); wait++) await Task.Delay(50);
        ContactBook.Reset();
        var back = ContactBook.Remote(accountId);
        var raw = File.Exists(file) ? File.ReadAllBytes(file) : [];
        Check(raw.Length > 0 && !Encoding.UTF8.GetString(raw).Contains("Ferri") && back is [{ Company: "Ferri Impianti srl" }] && back[0].Emails.Count == 2,
              "copia dei contatti di Outlook per i suggerimenti cifrata con DPAPI (in cache, senza nomi in chiaro), riletta uguale");
        ContactBook.ForgetRemote(accountId);
        Check(!File.Exists(file) && ContactBook.Remote(accountId).Count == 0, "account rimosso: la copia dei suoi contatti esce dal PC");
        ContactBook.Add([group]);
        ContactBook.Save();
        var stored = File.ReadAllBytes(Path.Combine(Store.Dir, "contatti.dat"));
        ContactBook.Reset();
        var reread = ContactBook.Local.FirstOrDefault(c => c.Id == group.Id);
        Check(!Encoding.UTF8.GetString(stored).Contains("Soci" + tag) && !Encoding.UTF8.GetString(stored).Contains("anna" + tag)
              && Encoding.UTF8.GetString(ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser)).Contains("Soci" + tag)
              && reread is { IsGroup: true, Members.Count: 3 } && reread.MemberAddresses.Count == 3,
              "gruppi di contatti in contatti.dat cifrato: né il nome né le persone in chiaro, riletti uguali");
        ContactBook.Remove(group);
    }
}
#endif
