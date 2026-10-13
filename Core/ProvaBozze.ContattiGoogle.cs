#if DEBUG
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Contatti di Google in scrittura e il percorso di modifica di Outlook (2 ottobre 2026, agente contatti-III). People API finta con
/// etag (list, get, create, update che sostituisce per intero i campi elencati, delete, errori), token finto, nessuna richiesta vera:
/// l'unione che conserva fax e voci sconosciute e cambia solo i campi cambiati, etag scaduto con una sola ripetizione, scritture una
/// alla volta, 403 senza ambito che propone il consenso, ContactsReadOnly che segue gli ambiti, creazione e modifica ed eliminazione
/// nella sezione giusta (Google e Outlook), righe di consenso, conferme di eliminazione, scritture solo nel registro nella prova.
/// </summary>
static partial class ProvaBozze
{
    /// <summary>La People API di Google, finta: le persone in memoria con il loro etag, e quello che serve a provare gli errori.</summary>
    sealed class PeopleApi : HttpMessageHandler
    {
        public readonly Dictionary<string, JsonObject> People = new();
        public readonly List<(HttpMethod Method, string Path, string Body, string Token)> Requests = new();
        /// <summary>Pausa per richiesta (per provare le scritture una alla volta) e il massimo di richieste insieme.</summary>
        public int Delay, InFlight, MaxInFlight;
        /// <summary>Quante PATCH rifiutare con l'etag scaduto (400 FAILED_PRECONDITION o StaleStatus), cambiando prima la persona "altrove".</summary>
        public int Stale;
        public int StaleStatus = 400;
        public Action<JsonObject>? ChangeElsewhere;
        public bool Forbid;
        int counter = 100, etags = 1;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            lock (this) MaxInFlight = Math.Max(MaxInFlight, ++InFlight);
            try
            {
                if (Delay > 0) await Task.Delay(Delay, cancel);
                return Answer(request);
            }
            finally
            {
                lock (this) InFlight--;
            }
        }

        static HttpResponseMessage Json(HttpStatusCode status, JsonNode json) => new(status) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };

        static HttpResponseMessage Error(HttpStatusCode status, string code, string message, string? reason = null)
        {
            var error = new JsonObject { ["code"] = (int)status, ["message"] = message, ["status"] = code };
            if (reason != null) error["errors"] = new JsonArray(new JsonObject { ["reason"] = reason });
            return Json(status, new JsonObject { ["error"] = error });
        }

        JsonNode Out(JsonObject person)
        {
            var copy = (JsonObject)JsonNode.Parse(person.ToJsonString())!;
            foreach (var name in (copy["names"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>())
                if (name["unstructuredName"] == null) name["displayName"] = $"{name["givenName"]?.GetValue<string>()} {name["familyName"]?.GetValue<string>()}".Trim();
                else name["displayName"] = name["unstructuredName"]!.GetValue<string>();
            return copy;
        }

        HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.PathAndQuery["/v1/".Length..];
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            lock (this) Requests.Add((request.Method, path, body, request.Headers.Authorization?.Parameter ?? ""));
            if (Forbid) return Error(HttpStatusCode.Forbidden, "PERMISSION_DENIED", "Request had insufficient authentication scopes.", "insufficientPermissions");
            var query = request.RequestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
            var route = request.RequestUri.AbsolutePath["/v1/".Length..];
            if (request.Method == HttpMethod.Get && route == "people/me/connections")
                return Json(HttpStatusCode.OK, new JsonObject { ["connections"] = new JsonArray(People.Values.Select(Out).ToArray()) });
            if (request.Method == HttpMethod.Post && route == "people:createContact")
            {
                var person = (JsonObject)JsonNode.Parse(body)!;
                var id = "people/c" + ++counter;
                person["resourceName"] = id;
                person["etag"] = "e" + ++etags;
                People[id] = person;
                return Json(HttpStatusCode.OK, Out(person));
            }
            var colon = route.IndexOf(':');
            var key = colon < 0 ? route : route[..colon];
            if (!People.TryGetValue(key, out var stored)) return Error(HttpStatusCode.NotFound, "NOT_FOUND", "Requested entity was not found.");
            if (request.Method == HttpMethod.Get && colon < 0) return Json(HttpStatusCode.OK, Out(stored));
            if (request.Method == HttpMethod.Delete && route.EndsWith(":deleteContact"))
            {
                People.Remove(key);
                return Json(HttpStatusCode.OK, new JsonObject());
            }
            if (request.Method == HttpMethod.Patch && route.EndsWith(":updateContact"))
            {
                var sent = (JsonObject)JsonNode.Parse(body)!;
                if (Stale > 0)
                {
                    Stale--;
                    ChangeElsewhere?.Invoke(stored);
                    stored["etag"] = "e" + ++etags;
                    return Error((HttpStatusCode)StaleStatus, "FAILED_PRECONDITION", "Request person.etag is different than the current person.etag. Clear local cache and get the latest person.");
                }
                if (sent["etag"]?.GetValue<string>() != stored["etag"]?.GetValue<string>()) return Error(HttpStatusCode.BadRequest, "FAILED_PRECONDITION", "Request person.etag is different than the current person.etag.");
                foreach (var field in query["updatePersonFields"].Split(','))
                    if (sent[field] is { } value) stored[field] = value.DeepClone();
                    else stored.Remove(field); // un campo elencato e non mandato si svuota: l'aggiornamento sostituisce per intero
                stored["etag"] = "e" + ++etags;
                return Json(HttpStatusCode.OK, Out(stored));
            }
            return Error(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Richiesta non prevista dalla People API finta: " + request.Method + " " + route);
        }
    }

    /// <summary>Anna Bianchi com'è su Google: voci che Missiva non mostra (secondo nome, fax, un telefono di altro tipo, reparto, un secondo indirizzo e sito, etichette).</summary>
    static JsonObject GoogleAnna() => (JsonObject)JsonNode.Parse("""
        {
          "resourceName": "people/c1", "etag": "e1",
          "names": [ { "displayName": "Anna Bianchi", "givenName": "Anna", "familyName": "Bianchi", "middleName": "Maria", "metadata": { "primary": true } } ],
          "emailAddresses": [ { "value": "anna@studio.example", "type": "home" }, { "value": "anna.b@lavoro.example", "type": "work", "formattedType": "Lavoro" } ],
          "phoneNumbers": [
            { "value": "+39 347 1", "type": "mobile", "canonicalForm": "+393471" }, { "value": "+39 051 2", "type": "work" },
            { "value": "+39 051 3", "type": "home" }, { "value": "+39 051 9", "type": "workFax" }, { "value": "+39 02 7", "type": "other" } ],
          "organizations": [ { "name": "Studio Rossi", "title": "Avvocato", "department": "Legale", "metadata": { "primary": true } } ],
          "addresses": [ { "formattedValue": "Via Roma 1\n40100 Bologna BO", "streetAddress": "Via Roma 1", "city": "Bologna", "postalCode": "40100", "type": "work" },
                         { "formattedValue": "Via Casa 2, Modena", "type": "home" } ],
          "urls": [ { "value": "https://anna.example/", "type": "homePage" }, { "value": "https://altro.example/", "type": "other" } ],
          "biographies": [ { "value": "PEC anna@pec.example", "contentType": "TEXT_PLAIN" } ]
        }
        """)!;

    static string Js(JsonNode? node, string key) => node?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    static IEnumerable<JsonNode> Items(JsonNode? list) => (list?.AsArray() ?? new JsonArray()).OfType<JsonNode>();

    static async Task GoogleContactsWriteAsync()
    {
        Report.Add("--- Contatti di Google: scrittura (People API finta con etag)");
        const string mail = "https://mail.google.com/ ";

        // ---- L'unione: solo i campi cambiati, le voci sconosciute restano.
        var current = GoogleAnna();
        var was = GoogleContacts.FromPerson(current, "g");
        Check(was.Name == "Anna Bianchi" && was.Mobile == "+39 347 1" && was.BusinessPhone == "+39 051 2" && was.HomePhone == "+39 051 3" && was.Website == "https://anna.example/" && was.Address == "Via Roma 1, 40100 Bologna BO",
              "partenza: la scheda di Anna com'è letta (fax, telefono di altro tipo, secondo nome, secondo indirizzo e sito non si vedono)");
        var now = was.Clone();
        now.Mobile = "+39 347 9";
        now.Company = "Studio Verdi";
        var merged = GoogleContacts.Merge(current, was, now);
        var phones = Items(merged.Person["phoneNumbers"]).ToList();
        var org = Items(merged.Person["organizations"]).Single();
        Check(merged.Fields.SequenceEqual(["phoneNumbers", "organizations"]) && merged.Person.Count == 2 && phones.Count == 5 && Js(phones[0], "value") == "+39 347 9" && Js(phones[0], "type") == "mobile"
              && phones[0]["canonicalForm"] == null && phones.Any(p => Js(p, "type") == "workFax" && Js(p, "value") == "+39 051 9") && phones.Any(p => Js(p, "type") == "other" && Js(p, "value") == "+39 02 7")
              && Js(org, "name") == "Studio Verdi" && Js(org, "title") == "Avvocato" && Js(org, "department") == "Legale" && Js(Items(current["phoneNumbers"]).First(), "value") == "+39 347 1",
              "unione: cambiati cellulare e azienda, vanno solo phoneNumbers e organizations; il fax e l'altro telefono restano, il tipo del cellulare resta, ruolo e reparto restano; la persona letta non si tocca");
        var mail2 = was.Clone();
        mail2.Emails = ["anna@studio.example", "nuova@x.example"];
        var mailMerged = GoogleContacts.Merge(current, was, mail2);
        var mails = Items(mailMerged.Person["emailAddresses"]).ToList();
        Check(mailMerged.Fields.SequenceEqual(["emailAddresses"]) && mails.Count == 2 && Js(mails[0], "value") == "anna@studio.example" && Js(mails[0], "type") == "home" && Js(mails[1], "value") == "nuova@x.example",
              "unione: un indirizzo tolto e uno aggiunto, quello che resta tiene il suo tipo");
        var addr = was.Clone();
        addr.Address = "Piazza Maggiore 6, Bologna";
        var addrMerged = GoogleContacts.Merge(current, was, addr);
        var addrs = Items(addrMerged.Person["addresses"]).ToList();
        Check(addrMerged.Fields.SequenceEqual(["addresses"]) && addrs.Count == 2 && Js(addrs[0], "formattedValue") == "Piazza Maggiore 6, Bologna" && addrs[0]["streetAddress"] == null && addrs[0]["city"] == null
              && addrs[0]["postalCode"] == null && Js(addrs[0], "type") == "work" && Js(addrs[1], "formattedValue") == "Via Casa 2, Modena",
              "indirizzo cambiato: la voce mostrata diventa il testo scritto (formattedValue) senza le parti vecchie che dicevano un'altra cosa, il tipo e l'altro indirizzo restano");
        var name = was.Clone();
        name.GivenName = "Anna Maria";
        var nameItem = Items(GoogleContacts.Merge(current, was, name).Person["names"]).Single();
        var site = was.Clone();
        site.Website = "https://nuovo.example/";
        var siteMerged = GoogleContacts.Merge(current, was, site);
        var sites = Items(siteMerged.Person["urls"]).ToList();
        var noPhone = was.Clone();
        noPhone.Mobile = "";
        var noNotes = was.Clone();
        noNotes.Notes = "";
        Check(Js(nameItem, "givenName") == "Anna Maria" && Js(nameItem, "familyName") == "Bianchi" && Js(nameItem, "middleName") == "Maria" && nameItem["displayName"] == null
              && siteMerged.Fields.SequenceEqual(["urls"]) && sites.Count == 2 && Js(sites[0], "value") == "https://nuovo.example/" && Js(sites[1], "value") == "https://altro.example/"
              && Items(GoogleContacts.Merge(current, was, noPhone).Person["phoneNumbers"]).Count() == 4 && Items(GoogleContacts.Merge(current, was, noPhone).Person["phoneNumbers"]).All(p => Js(p, "type") != "mobile")
              && Items(GoogleContacts.Merge(current, was, noNotes).Person["biographies"]).Count() == 0 && GoogleContacts.Merge(current, was, was.Clone()).Fields.Count == 0,
              "unione: il nome tiene il secondo nome, il sito cambiato lascia l'altro, un telefono svuotato esce (gli altri no), note svuotate, nessuna modifica nessun campo");
        var fresh = GoogleContacts.Merge(null, null, new Contact { GivenName = "Giulia", Surname = "Ferri", Emails = ["giulia@cliente.example"], Mobile = "+39 333 1", BusinessPhone = "+39 051 1",
                                                                  Company = "Ferri srl", JobTitle = "Amministrazione", Address = "Via X 1, Roma", Notes = "P. IVA 1" });
        Check(fresh.Fields.SequenceEqual(["names", "emailAddresses", "phoneNumbers", "organizations", "addresses", "biographies"]) && Items(fresh.Person["phoneNumbers"]).Select(p => Js(p, "type")).SequenceEqual(["mobile", "work"])
              && Js(Items(fresh.Person["biographies"]).Single(), "contentType") == "TEXT_PLAIN" && Js(Items(fresh.Person["names"]).Single(), "givenName") == "Giulia" && Js(Items(fresh.Person["addresses"]).Single(), "formattedValue") == "Via X 1, Roma"
              && fresh.Person["etag"] == null && fresh.Person["urls"] == null,
              "contatto nuovo: solo i campi scritti, telefoni mobile e work, note TEXT_PLAIN, indirizzo come formattedValue, niente etag");

        // ---- Gli ambiti e ContactsReadOnly dinamico.
        var readOnly = new MailAccount { Kind = AccountKind.Gmail, Email = "lettura@gmail.com", Id = "g-lettura", GoogleScopes = mail + GoogleAuth.ContactsScope };
        var writable = new MailAccount { Kind = AccountKind.Gmail, Email = "scrittura@gmail.com", Id = "g-scrittura", GoogleScopes = mail + GoogleAuth.CalendarScope + " " + GoogleAuth.ContactsWriteScope };
        var nothing = new MailAccount { Kind = AccountKind.Gmail, Email = "niente@gmail.com", Id = "g-niente", GoogleScopes = mail };
        using var gRead = new GmailProvider(readOnly);
        using var gWrite = new GmailProvider(writable);
        using var gNone = new GmailProvider(nothing);
        Check(gRead.ContactsReadOnly && !gWrite.ContactsReadOnly && gNone.ContactsReadOnly && GoogleAuth.HasContacts(writable) && GoogleAuth.HasContactsWrite(writable) && !GoogleAuth.HasContactsWrite(readOnly) && GoogleAuth.HasContacts(readOnly),
              "ContactsReadOnly segue gli ambiti: vero senza contacts (con o senza contacts.readonly), falso con contacts; chi può scrivere legge anche");
        var both = new MailAccount { Kind = AccountKind.Gmail, Email = "x@gmail.com", GoogleScopes = mail + GoogleAuth.CalendarScope + " " + GoogleAuth.ContactsScope + " " + GoogleAuth.ContactsWriteScope };
        Check(GoogleAuth.ExtraScopesOf(both).SequenceEqual([GoogleAuth.CalendarScope, GoogleAuth.ContactsWriteScope]) && GoogleAuth.ExtraScopesOf(readOnly).SequenceEqual([GoogleAuth.ContactsScope])
              && GoogleAuth.ExtraScopesOf(writable).SequenceEqual([GoogleAuth.CalendarScope, GoogleAuth.ContactsWriteScope]) && GoogleAuth.ContactsWriteScope == "https://www.googleapis.com/auth/contacts",
              "un nuovo accesso rimette calendario e contacts (mai contacts.readonly insieme a contacts); l'ambito di scrittura è .../auth/contacts");
        var live = new MailAccount { Kind = AccountKind.Gmail, Email = "dopo@gmail.com", Id = "g-dopo", GoogleScopes = mail + GoogleAuth.ContactsScope };
        using var gLive = new GmailProvider(live);
        bool before = gLive.ContactsReadOnly;
        live.GoogleScopes = mail + GoogleAuth.ContactsWriteScope;
        Check(before && !gLive.ContactsReadOnly, "lo stesso provider: dopo il consenso (ambiti aggiornati) ContactsReadOnly diventa falso");

        // ---- Le chiamate.
        var api = new PeopleApi();
        api.People["people/c1"] = GoogleAnna();
        GoogleApi.UseTestHandler(api);
        GoogleAuth.TestToken = "prova-google";
        try
        {
            string? noConsent = null;
            try { await gRead.SaveContactAsync(null, new Contact { DisplayName = "X" }); }
            catch (MailException ex) { noConsent = ex.Message; }
            try { await gRead.DeleteContactAsync(was); }
            catch (MailException) { }
            Check(noConsent?.Contains("Consenti la modifica dei contatti") == true && noConsent.Contains("in Persone") && api.Requests.Count == 0,
                  "senza l'ambito di scrittura: nessuna richiesta, e il messaggio propone il pulsante \"Consenti la modifica dei contatti\"");

            var all = await gWrite.GetContactsAsync();
            Check(all is [{ Name: "Anna Bianchi" }] && api.Requests.Count == 1 && api.Requests[0].Path.StartsWith("people/me/connections?personFields=") && api.Requests[0].Token == "prova-google",
                  "con l'ambito di scrittura la lettura (people/me/connections) funziona come prima");

            // Modifica: GET fresco poi PATCH con l'etag e i soli campi cambiati; il fax e il resto restano su Google.
            api.Requests.Clear();
            var anna = all![0];
            var annaNow = anna.Clone();
            annaNow.Mobile = "+39 347 9";
            var id = await gWrite.SaveContactAsync(anna, annaNow);
            var patch = JsonNode.Parse(api.Requests[1].Body)!.AsObject();
            var stored = api.People["people/c1"];
            Check(id == "people/c1" && api.Requests.Count == 2 && api.Requests[0].Method == HttpMethod.Get && api.Requests[0].Path.StartsWith("people/c1?personFields=" + GoogleContacts.PersonFields)
                  && api.Requests[1].Method == HttpMethod.Patch && api.Requests[1].Path.StartsWith("people/c1:updateContact?updatePersonFields=phoneNumbers&personFields=")
                  && Js(patch, "etag") == "e1" && patch.Select(p => p.Key).OrderBy(k => k).SequenceEqual(["etag", "phoneNumbers"]),
                  "Modifica: prima GET people/c1 (etag fresco), poi PATCH :updateContact con updatePersonFields=phoneNumbers e l'etag nel corpo, solo quel campo");
            Check(Items(stored["phoneNumbers"]).Count() == 5 && Items(stored["phoneNumbers"]).Any(p => Js(p, "type") == "workFax") && Items(stored["phoneNumbers"]).Any(p => Js(p, "value") == "+39 347 9")
                  && Js(Items(stored["names"]).Single(), "middleName") == "Maria" && Items(stored["urls"]).Count() == 2 && Js(Items(stored["organizations"]).Single(), "department") == "Legale"
                  && annaNow.Mobile == "+39 347 9" && Js(stored, "etag") != "e1",
                  "su Google (People finta, che sostituisce i campi elencati) restano fax, telefono di altro tipo, secondo nome, siti e reparto; l'elenco locale prende la persona restituita");
            api.Requests.Clear();
            await gWrite.SaveContactAsync(annaNow, annaNow.Clone());
            Check(api.Requests.Count == 0, "Modifica senza cambiamenti: nessuna richiesta");

            // Etag scaduto: rileggi e riprova una volta (400 FAILED_PRECONDITION e 409); la modifica fatta altrove non si perde.
            foreach (var status in new[] { 400, 409 })
            {
                api.Requests.Clear();
                api.Stale = 1;
                api.StaleStatus = status;
                api.ChangeElsewhere = p => Items(p["phoneNumbers"]).ToList()[0]["value"] = "+39 347 55";
                var base0 = GoogleContacts.FromPerson(api.People["people/c1"], writable.Id);
                var edit = base0.Clone();
                edit.HomePhone = "+39 051 " + status;
                await gWrite.SaveContactAsync(base0, edit);
                var final = api.People["people/c1"];
                Check(string.Join(",", api.Requests.Select(r => r.Method.Method)) == "GET,PATCH,GET,PATCH" && Items(final["phoneNumbers"]).Any(p => Js(p, "value") == "+39 051 " + status)
                      && Items(final["phoneNumbers"]).Any(p => Js(p, "value") == "+39 347 55") && Items(final["phoneNumbers"]).Any(p => Js(p, "type") == "workFax"),
                      $"etag scaduto ({status}): GET, PATCH rifiutata, GET, PATCH riuscita (una ripetizione sola); il cambiamento fatto altrove resta nella persona");
            }
            api.ChangeElsewhere = null;
            api.Requests.Clear();
            api.Stale = 99;
            var baseNow = GoogleContacts.FromPerson(api.People["people/c1"], writable.Id);
            var again = baseNow.Clone();
            again.JobTitle = "Socio";
            ContactChangedException? changed = null;
            try { await gWrite.SaveContactAsync(baseNow, again); }
            catch (ContactChangedException ex) { changed = ex; }
            Check(changed != null && api.Requests.Count == 4 && changed.Message == "Il contatto è stato cambiato altrove: riaperto con i dati nuovi." && changed.Fresh.Name == "Anna Bianchi" && changed.Fresh.ServerId == "people/c1",
                  "etag scaduto anche la seconda volta: dopo una sola ripetizione il messaggio chiaro e la persona riletta per riaprire la finestra, mai un ciclo");
            api.Stale = 0;

            // Creazione.
            api.Requests.Clear();
            var giulia = new Contact { GivenName = "Giulia", Surname = "Ferri", Emails = ["giulia@cliente.example"], Mobile = "+39 333 1", Company = "Ferri srl" };
            var newId = await gWrite.SaveContactAsync(null, giulia);
            var posted = JsonNode.Parse(api.Requests.Single().Body)!.AsObject();
            Check(api.Requests.Single().Method == HttpMethod.Post && api.Requests.Single().Path.StartsWith("people:createContact?personFields=" + GoogleContacts.PersonFields) && newId.StartsWith("people/c")
                  && posted.Select(p => p.Key).OrderBy(k => k).SequenceEqual(["emailAddresses", "names", "organizations", "phoneNumbers"]) && giulia.ServerId == newId && giulia.DisplayName == "Giulia Ferri" && api.People.ContainsKey(newId),
                  "Nuovo contatto: POST people:createContact con i soli campi scritti, e il contatto prende l'identificativo e il nome composto da Google");

            // Una scrittura alla volta, per account (le letture no: il controllo misura davvero).
            api.Delay = 40;
            api.MaxInFlight = 0;
            await Task.WhenAll(Enumerable.Range(0, 4).Select(i => gWrite.SaveContactAsync(null, new Contact { DisplayName = "Cliente " + i, Emails = [$"c{i}@x.example"] })));
            int writesAtOnce = api.MaxInFlight;
            api.MaxInFlight = 0;
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => gWrite.GetContactsAsync()));
            Check(writesAtOnce == 1 && api.MaxInFlight > 1 && api.People.Values.Count(p => Js(Items(p["names"]).FirstOrDefault(), "unstructuredName").StartsWith("Cliente ")) == 4,
                  $"quattro scritture insieme partono una alla volta (al massimo {writesAtOnce} richiesta in volo), mentre le letture insieme no ({api.MaxInFlight})");
            api.Delay = 0;

            // Elimina; già eliminato altrove (404) è come eliminato; modificare un contatto sparito lo dice.
            api.Requests.Clear();
            await gWrite.DeleteContactAsync(annaNow);
            var deleted = api.Requests.Single();
            await gWrite.DeleteContactAsync(annaNow); // di nuovo: 404 di Google, nessun errore
            string? gone = null;
            try { await gWrite.SaveContactAsync(annaNow, GoogleContacts.FromPerson(GoogleAnna(), writable.Id)); }
            catch (MailException ex) when (ex.Status == 404) { gone = ex.Message; }
            Check(deleted.Method == HttpMethod.Delete && deleted.Path == "people/c1:deleteContact" && !api.People.ContainsKey("people/c1") && gone?.Contains("eliminato su Google") == true,
                  "Elimina: DELETE people/c1:deleteContact; già eliminato altrove non è un errore; modificare un contatto sparito dice che è stato eliminato su Google (404)");

            // 403 con l'ambito tolto da Google nel frattempo: il consenso, non un errore generico.
            api.Forbid = true;
            string? forbidden = null;
            try { await gWrite.SaveContactAsync(null, new Contact { DisplayName = "Y" }); }
            catch (MailException ex) { forbidden = ex.Message; }
            api.Forbid = false;
            Check(forbidden?.Contains("Consenti la modifica dei contatti") == true && forbidden.Contains("in Persone"), "403 per ambito mancante: propone \"Consenti la modifica dei contatti\" in Persone");

            // Nella prova le scritture di Google vanno solo nel registro.
            api.Requests.Clear();
            var testing = new TestingProvider(gWrite);
            await testing.AllowContactsWriteAsync();
            await testing.SaveContactAsync(null, new Contact { DisplayName = "Nuovo cliente" });
            var edited = annaNow.Clone();
            edited.Mobile = "+39 347 0";
            await testing.SaveContactAsync(annaNow, edited);
            await testing.DeleteContactAsync(annaNow);
            var logged = File.ReadLines(Path.Combine(Store.Dir, "missiva.log")).TakeLast(4).ToList();
            Check(api.Requests.Count == 0 && logged[0].Contains("prova: chiederei il consenso alla modifica dei contatti") && logged[1].Contains("prova: creerei il contatto \"Nuovo cliente\"")
                  && logged[2].Contains("prova: aggiornerei il contatto \"Anna Bianchi\"") && logged[2].Contains("phoneNumbers") && logged[3].Contains("prova: eliminerei il contatto \"Anna Bianchi\"") && !testing.ContactsReadOnly,
                  "nella prova (TestingProvider) consenso, creazione, aggiornamento ed eliminazione di Google vanno solo nel registro: nessuna richiesta alla People API");
        }
        finally
        {
            GoogleAuth.TestToken = null;
        }
    }

    /// <summary>Il percorso di modifica nelle sezioni di Persone, per Google e per Outlook: nuovo, modificato ed eliminato restano nella sezione giusta, senza doppioni.</summary>
    static async Task GoogleContactsFlowAsync()
    {
        Report.Add("--- Persone: modifica di Google e di Outlook nelle sezioni, righe di consenso");
        const string mail = "https://mail.google.com/ ";
        var google = new MailAccount { Kind = AccountKind.Gmail, Email = "flusso@gmail.com", Id = "g-flusso", GoogleScopes = mail + GoogleAuth.ContactsWriteScope };
        var outlookAccount = new MailAccount { Kind = AccountKind.Microsoft, Email = "flusso@outlook.example", Id = "o-flusso" };
        using var g = new GmailProvider(google);
        var o = new GraphProvider(outlookAccount);
        var api = new PeopleApi();
        api.People["people/c1"] = GoogleAnna();
        GoogleApi.UseTestHandler(api);
        GoogleAuth.TestToken = "prova-google";
        var graph = new List<(HttpMethod Method, string Path, string Body)>();
        GraphProvider.UseTestHandler(new FakeHttp(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)["/v1.0/".Length..];
            graph.Add((request.Method, path, request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            JsonNode json = request.Method == HttpMethod.Get
                ? new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = "c1", ["displayName"] = "Anna Bianchi", ["mobilePhone"] = "+39 333 1" }, new JsonObject { ["id"] = "c2", ["displayName"] = "Luca Verdi" }) }
                : request.Method == HttpMethod.Post ? new JsonObject { ["id"] = "nuovo" } : new JsonObject();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }));
        GraphProvider.TestContactsConsent = true;
        try
        {
            var googleList = (await g.GetContactsAsync())!;
            var outlookList = (await o.GetContactsAsync())!;
            var remote = new Dictionary<IMailProvider, List<Contact>?> { [g] = googleList, [o] = outlookList };
            var origins = PeopleView.Origins([g, o]);
            var local = new List<Contact> { Person("Zeno Neri", "zeno@x.example") };
            List<(PeopleView.Header Header, List<object> Items)> Sections() => SectionsOf(PeopleView.BuildEntries(origins, local, remote, new Dictionary<IMailProvider, string>(), "", ""));
            string Counts() => string.Join("|", Sections().Select(s => s.Header.Count));
            Check(Counts() == "1|1|2" && Sections()[1].Header.Detail == "Google" && Sections()[2].Header.Detail == "Outlook" && Sections()[1].Items.All(i => i is PeopleView.Row),
                  "partenza: Missiva 1, Google 1 (scrivibile: tipo \"Google\", nessuna riga di consenso), Outlook 2");

            // Google: nuovo, modifica, elimina.
            api.Requests.Clear();
            var giulia = new Contact { GivenName = "Giulia", Surname = "Ferri", Emails = ["giulia@cliente.example"] };
            giulia.ServerId = await g.SaveContactAsync(null, giulia);
            giulia.Source = google.Id;
            PeopleView.PutRemote(googleList, giulia);
            Check(Counts() == "1|2|2" && Names(Sections()[1].Items) == "Anna Bianchi, Giulia Ferri" && Names(Sections()[0].Items) == "Zeno Neri" && Names(Sections()[2].Items) == "Anna Bianchi, Luca Verdi",
                  "Nuovo contatto in Google: compare subito nella sezione di Google (e solo lì), senza rileggere l'elenco");
            var giuliaNow = giulia.Clone();
            giuliaNow.Mobile = "+39 333 8";
            giuliaNow.ServerId = await g.SaveContactAsync(giulia, giuliaNow);
            PeopleView.PutRemote(googleList, giuliaNow);
            Check(Counts() == "1|2|2" && Names(Sections()[1].Items) == "Anna Bianchi, Giulia Ferri" && googleList.Count(c => c.Id == giulia.Id) == 1 && googleList.Single(c => c.Id == giulia.Id).Mobile == "+39 333 8"
                  && api.Requests.Count == 3 && api.Requests[2].Path.Contains("updatePersonFields=phoneNumbers&"),
                  "Modifica in Google: stessa sezione, nessun doppione, il nuovo cellulare; una PATCH del solo campo cambiato");
            await g.DeleteContactAsync(giuliaNow);
            googleList.RemoveAll(c => c.Id == giulia.Id);
            Check(Counts() == "1|1|2" && !api.People.ContainsKey(giulia.ServerId), "Elimina in Google: esce dalla sezione e da Google");

            // Outlook: lo stesso percorso (ContactWindow, Salva, conferma): richieste e sezione.
            graph.Clear();
            var luca = new Contact { GivenName = "Marta", Surname = "Neri", Emails = ["marta@x.example"] };
            luca.ServerId = await o.SaveContactAsync(null, luca);
            luca.Source = outlookAccount.Id;
            PeopleView.PutRemote(outlookList, luca);
            var created = JsonNode.Parse(graph.Single().Body)!.AsObject();
            Check(graph.Single().Method == HttpMethod.Post && graph.Single().Path == "me/contacts" && created.Select(p => p.Key).OrderBy(k => k).SequenceEqual(["emailAddresses", "givenName", "surname"])
                  && Counts() == "1|1|3" && Names(Sections()[2].Items) == "Anna Bianchi, Luca Verdi, Marta Neri" && Names(Sections()[0].Items) == "Zeno Neri",
                  "Nuovo contatto in Outlook: POST dei soli campi scritti, compare nella sezione di Outlook (3 persone) e non in Missiva");
            graph.Clear();
            var lucaNow = luca.Clone();
            lucaNow.Mobile = "+39 347 5";
            lucaNow.ServerId = await o.SaveContactAsync(luca, lucaNow);
            PeopleView.PutRemote(outlookList, lucaNow);
            var patched = JsonNode.Parse(graph.Single().Body)!.AsObject();
            Check(graph.Single().Method == HttpMethod.Patch && graph.Single().Path == "me/contacts/nuovo" && patched.Select(p => p.Key).SequenceEqual(["mobilePhone"]) && Counts() == "1|1|3"
                  && outlookList.Count(c => c.Id == luca.Id) == 1 && Names(Sections()[2].Items).Split(", ").Count(n => n == "Marta Neri") == 1,
                  "Modifica in Outlook: PATCH del solo cellulare, stessa sezione, nessun doppione");
            graph.Clear();
            await o.DeleteContactAsync(lucaNow);
            outlookList.RemoveAll(c => c.Id == luca.Id);
            Check(graph.Single().Method == HttpMethod.Delete && graph.Single().Path == "me/contacts/nuovo" && Counts() == "1|1|2" && Names(Sections()[2].Items) == "Anna Bianchi, Luca Verdi",
                  "Elimina in Outlook: DELETE e il contatto esce dalla sezione");

            // Le righe di consenso e i testi.
            GmailProvider.TestContactsWritable = null;
            var readOnly = new MailAccount { Kind = AccountKind.Gmail, Email = "lettura@gmail.com", Id = "g-lettura2", GoogleScopes = mail + GoogleAuth.ContactsScope };
            var nothing = new MailAccount { Kind = AccountKind.Gmail, Email = "niente@gmail.com", Id = "g-niente2", GoogleScopes = mail };
            var outlookNo = new MailAccount { Kind = AccountKind.Microsoft, Email = "senza@outlook.example", Id = "o-senza" };
            using var gRead = new GmailProvider(readOnly);
            using var gNone = new GmailProvider(nothing);
            var oNo = new GraphProvider(outlookNo);
            var states = new Dictionary<IMailProvider, List<Contact>?> { [gRead] = [Person("Maria Rossi", "maria@x.example")], [g] = googleList, [gNone] = null, [oNo] = null };
            var originsB = PeopleView.Origins([gRead, g, gNone, oNo]);
            var sectionsB = SectionsOf(PeopleView.BuildEntries(originsB, [], states, new Dictionary<IMailProvider, string>(), "", ""));
            var writeNote = sectionsB[1].Items.OfType<PeopleView.SectionNote>().SingleOrDefault();
            var noneNote = sectionsB[3].Items.OfType<PeopleView.SectionNote>().Single();
            var outlookNote = sectionsB[4].Items.OfType<PeopleView.SectionNote>().Single();
            Check(sectionsB[1].Header.Detail == "Google · sola lettura" && writeNote is { Write: true } && ReferenceEquals(writeNote.Allow, gRead) && writeNote.ButtonText == "Consenti la modifica dei contatti"
                  && writeNote.ButtonName == "Consenti la modifica dei contatti di lettura@gmail.com" && writeNote.Text == "Missiva potrà creare, cambiare ed eliminare i tuoi contatti di Google." && ReferenceEquals(sectionsB[1].Items[0], writeNote)
                  && sectionsB[2].Header.Detail == "Google" && !sectionsB[2].Items.OfType<PeopleView.SectionNote>().Any(),
                  "Google senza ambito di scrittura: sotto l'intestazione la riga con \"Consenti la modifica dei contatti\" e la frase che spiega; con l'ambito nessuna riga");
            Check(noneNote is { Write: false } && noneNote.ButtonText == "Consenti l'accesso ai contatti" && outlookNote is { Write: false } && outlookNote.ButtonText == "Consenti l'accesso ai contatti"
                  && outlookNote.Text == "Consenti l'accesso ai contatti di Outlook: potrai vederli e modificarli in Missiva." && ReferenceEquals(outlookNote.Allow, oNo),
                  "Outlook senza consenso: la riga dice che serve per vedere e modificare, con \"Consenti l'accesso ai contatti\"; Google senza nessun consenso chiede prima la lettura");
            Check(!SectionsOf(PeopleView.BuildEntries(originsB, [], states, new Dictionary<IMailProvider, string>(), "x.example", "")).SelectMany(s => s.Items).OfType<PeopleView.SectionNote>().Any(),
                  "con la ricerca nessuna riga di consenso");

            // Modifica di Google: il link a Google Contatti solo senza l'ambito; conferme di eliminazione; flag di prova.
            var person = new Contact { ServerId = "people/c555" };
            Check(PeopleView.GoogleEditUrl(person, gRead) is { } url && url.StartsWith("https://contacts.google.com/person/c555") && PeopleView.GoogleEditUrl(person, g) == null && PeopleView.GoogleEditUrl(person, o) == null,
                  "Modifica: senza l'ambito apre Google Contatti nel browser, con l'ambito (e per Outlook) apre la finestra del contatto");
            Check(PeopleView.DeleteWarning(g) == "Elimina da Google Contatti: sparisce anche dal telefono e da tutti i tuoi dispositivi." && PeopleView.DeleteWarning(o).StartsWith("Elimina da Outlook:") && PeopleView.DeleteWarning(o).Contains("flusso@outlook.example"),
                  "la conferma di eliminazione dice \"Elimina da Google Contatti\" (anche dal telefono e da tutti i dispositivi) o \"Elimina da Outlook\"");
            GmailProvider.TestContactsWritable = true;
            bool forcedWrite = !gRead.ContactsReadOnly;
            GmailProvider.TestContactsWritable = false;
            bool forcedRead = g.ContactsReadOnly;
            GmailProvider.TestContactsWritable = null;
            Check(forcedWrite && forcedRead && gRead.ContactsReadOnly && !g.ContactsReadOnly, "--google-scrittura e --google-sola-lettura (solo Debug) forzano ContactsReadOnly senza toccare gli ambiti veri");
        }
        finally
        {
            GmailProvider.TestContactsWritable = null;
            GraphProvider.TestContactsConsent = false;
            GoogleAuth.TestToken = null;
        }
    }
}
#endif
