using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Missiva;

/// <summary>
/// Una regola della Posta in arrivo, come Impostazioni, Posta, Regole del nuovo Outlook e i filtri dei messaggi di
/// Thunderbird: condizioni (devono valere tutte; dentro una condizione basta una delle parole) e azioni, in un ordine,
/// attiva o spenta. Per gli account Microsoft sono le regole di Outlook sul server (me/mailFolders/inbox/messageRules):
/// girano su Exchange anche a Missiva chiusa, e i campi "solo Graph" vengono da lì. Per Gmail e IMAP sono regole di
/// Missiva (Settings.Rules), con parole, indirizzi e cartelle cifrati con DPAPI, applicate ai nuovi messaggi della Posta
/// in arrivo a ogni controllo: solo con Missiva attiva, anche nell'area di notifica.
/// </summary>
public sealed class MailRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "";
    [JsonConverter(typeof(ProtectedTextConverter))] public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    // ---------------------------------------------------------------- condizioni

    /// <summary>Il mittente è uno di questi indirizzi (Graph fromAddresses).</summary>
    [JsonConverter(typeof(ProtectedListConverter))] public List<string> FromAddresses { get; set; } = new();
    /// <summary>Il nome o l'indirizzo del mittente contiene una di queste parole (Graph senderContains).</summary>
    [JsonConverter(typeof(ProtectedListConverter))] public List<string> SenderContains { get; set; } = new();
    /// <summary>Inviato a me: il mio indirizzo (o un altro indirizzo dell'account) è fra i destinatari A.</summary>
    public bool SentToMe { get; set; }
    /// <summary>Il mio indirizzo è in Cc.</summary>
    public bool SentCcMe { get; set; }
    [JsonConverter(typeof(ProtectedListConverter))] public List<string> SubjectContains { get; set; } = new();
    [JsonConverter(typeof(ProtectedListConverter))] public List<string> BodyContains { get; set; } = new();
    public bool HasAttachments { get; set; }
    /// <summary>Importanza del messaggio (alta o bassa); null per qualsiasi.</summary>
    public Importance? WithImportance { get; set; }

    // ---------------------------------------------------------------- azioni

    /// <summary>La cartella in cui spostare (identificativo sul server: percorso IMAP, id di Graph) e il suo nome.</summary>
    [JsonConverter(typeof(ProtectedTextConverter))] public string MoveTo { get; set; } = "";
    [JsonConverter(typeof(ProtectedTextConverter))] public string MoveToName { get; set; } = "";
    [JsonConverter(typeof(ProtectedTextConverter))] public string CopyTo { get; set; } = "";
    [JsonConverter(typeof(ProtectedTextConverter))] public string CopyToName { get; set; } = "";
    public bool MarkRead { get; set; }
    /// <summary>Contrassegna: solo nelle regole di Missiva (le regole di Outlook non hanno il contrassegno fra le azioni).</summary>
    public bool Flag { get; set; }
    [JsonConverter(typeof(ProtectedListConverter))] public List<string> Categories { get; set; } = new();
    /// <summary>Elimina, cioè nel Cestino (mai per sempre).</summary>
    public bool Delete { get; set; }
    /// <summary>Non applicare altre regole a questo messaggio.</summary>
    public bool StopProcessing { get; set; }

    // ---------------------------------------------------------------- solo Graph (regole di Outlook, mai nelle impostazioni)

    [JsonIgnore] public string ServerId { get; set; } = "";
    [JsonIgnore] public int Sequence { get; set; }
    /// <summary>Inoltra, reindirizza, inoltra come allegato: la regola manda la posta fuori dalla casella.</summary>
    [JsonIgnore] public List<Address> ForwardTo { get; set; } = new();
    [JsonIgnore] public List<Address> RedirectTo { get; set; } = new();
    [JsonIgnore] public List<Address> ForwardAsAttachmentTo { get; set; } = new();
    /// <summary>Outlook dice che la regola ha un errore (per esempio una cartella che non c'è più) o che non si può modificare.</summary>
    [JsonIgnore] public bool HasError { get; set; }
    [JsonIgnore] public bool IsReadOnly { get; set; }
    /// <summary>
    /// Le condizioni e le azioni com'erano sul server, anche quelle che Missiva non mostra (intestazioni, dimensione,
    /// eliminazione definitiva...): la modifica le rimanda com'erano, cambiando solo le sue.
    /// </summary>
    [JsonIgnore] public JsonObject? RawConditions { get; set; }
    [JsonIgnore] public JsonObject? RawActions { get; set; }
    /// <summary>Quello che la regola di Outlook fa e Missiva non mostra, in parole ("eccezioni", "eliminazione definitiva").</summary>
    [JsonIgnore] public List<string> Hidden { get; set; } = new();
    /// <summary>
    /// Le condizioni e le eccezioni di Outlook che Missiva non sa controllare ("oggetto o testo", "destinatari", "eccezioni"):
    /// con queste la regola non corrisponde mai in Missiva (Matches), ed "Esegui ora" non c'è. Varrebbero come assenti, e la
    /// regola toccherebbe messaggi che Outlook lascia stare.
    /// </summary>
    [JsonIgnore] public List<string> HiddenConditions { get; set; } = new();

    [JsonIgnore] public bool HasConditions => FromAddresses.Count + SenderContains.Count + SubjectContains.Count + BodyContains.Count > 0 || SentToMe || SentCcMe ||
                                 HasAttachments || WithImportance != null;

    /// <summary>
    /// Una regola di Missiva con parole, indirizzi o cartelle che non si decifrano più (ProtectedTextConverter.IsUnreadable:
    /// impostazioni di un altro PC): non corrisponde mai (Matches). Prima le parole diventavano un elenco vuoto, e "Se il
    /// mittente è ...: elimina" valeva per ogni messaggio.
    /// </summary>
    [JsonIgnore] public bool Unreadable =>
        new[] { Name, MoveTo, MoveToName, CopyTo, CopyToName }.Concat(FromAddresses).Concat(SenderContains).Concat(SubjectContains).Concat(BodyContains).Concat(Categories)
            .Any(ProtectedTextConverter.IsUnreadable);

    [JsonIgnore] public bool HasActions => MoveTo.Length > 0 || CopyTo.Length > 0 || MarkRead || Flag || Categories.Count > 0 || Delete || AllForwards.Any();

    /// <summary>Tutti gli indirizzi a cui la regola manda la posta (inoltro, reindirizzamento, inoltro come allegato).</summary>
    [JsonIgnore] public IEnumerable<Address> AllForwards => ForwardTo.Concat(RedirectTo).Concat(ForwardAsAttachmentTo);

    /// <summary>Una copia da modificare: la finestra della regola non cambia quella dell'elenco finché non si salva.</summary>
    public MailRule Copy() => new()
    {
        Id = Id, AccountId = AccountId, Name = Name, Enabled = Enabled, FromAddresses = FromAddresses.ToList(), SenderContains = SenderContains.ToList(),
        SentToMe = SentToMe, SentCcMe = SentCcMe, SubjectContains = SubjectContains.ToList(), BodyContains = BodyContains.ToList(),
        HasAttachments = HasAttachments, WithImportance = WithImportance, MoveTo = MoveTo, MoveToName = MoveToName, CopyTo = CopyTo,
        CopyToName = CopyToName, MarkRead = MarkRead, Flag = Flag, Categories = Categories.ToList(), Delete = Delete, StopProcessing = StopProcessing,
        ServerId = ServerId, Sequence = Sequence, ForwardTo = ForwardTo.ToList(), RedirectTo = RedirectTo.ToList(),
        ForwardAsAttachmentTo = ForwardAsAttachmentTo.ToList(), HasError = HasError, IsReadOnly = IsReadOnly,
        RawConditions = RawConditions?.DeepClone().AsObject(), RawActions = RawActions?.DeepClone().AsObject(), Hidden = Hidden.ToList(),
        HiddenConditions = HiddenConditions.ToList(),
    };
}

/// <summary>
/// Le regole: descrizione in parole, controllo dell'inoltro fuori dall'organizzazione, conversione da e verso le regole di
/// Outlook (Graph messageRule), confronto di un messaggio con le condizioni ed esecuzione delle azioni con IMailProvider.
/// </summary>
public static class Rules
{
    // ---------------------------------------------------------------- regole di Missiva (Gmail e IMAP)

    /// <summary>Le regole di Missiva dell'account, nell'ordine in cui si applicano.</summary>
    public static List<MailRule> For(MailAccount account) => Store.Settings.Rules.Where(r => r.AccountId == account.Id).ToList();

    /// <summary>Salva una regola di Missiva, nuova (in fondo) o cambiata (al suo posto).</summary>
    public static void Put(MailRule rule)
    {
        var list = Store.Settings.Rules;
        int at = list.FindIndex(r => r.Id == rule.Id);
        if (at >= 0) list[at] = rule;
        else list.Add(rule);
        Store.Save();
    }

    public static void Remove(MailRule rule)
    {
        Store.Settings.Rules.RemoveAll(r => r.Id == rule.Id);
        Store.Save();
    }

    /// <summary>Su (-1) o giù (+1) fra le regole dello stesso account.</summary>
    public static void Move(MailRule rule, int delta)
    {
        var list = Store.Settings.Rules;
        var mine = list.Where(r => r.AccountId == rule.AccountId).ToList();
        int at = mine.FindIndex(r => r.Id == rule.Id), to = at + delta;
        if (at < 0 || to < 0 || to >= mine.Count) return;
        int a = list.IndexOf(mine[at]), b = list.IndexOf(mine[to]);
        (list[a], list[b]) = (list[b], list[a]);
        Store.Save();
    }

    // ---------------------------------------------------------------- parole

    /// <summary>Le parole scritte in un campo, separate da punto e virgola o virgola, senza doppioni.</summary>
    public static List<string> Words(string text) =>
        text.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

    static string Quoted(IEnumerable<string> words, string or = " o ") => string.Join(or, words.Select(w => $"\"{w}\""));

    /// <summary>
    /// La regola in una riga, come l'elenco delle regole del nuovo Outlook: "Se il mittente è x@y e l'oggetto contiene
    /// "fattura": sposta in Fatture, segna come letto".
    /// </summary>
    public static string Describe(MailRule r, Func<string, string?>? folderName = null)
    {
        var when = new List<string>();
        var from = r.FromAddresses.Concat(r.SenderContains.Select(w => $"\"{w}\"")).ToList();
        if (from.Count > 0) when.Add((r.SenderContains.Count == 0 ? "il mittente è " : "il mittente è o contiene ") + string.Join(" o ", from));
        if (r.SentToMe) when.Add("è inviato a me");
        if (r.SentCcMe) when.Add("sono in Cc");
        if (r.SubjectContains.Count > 0) when.Add("l'oggetto contiene " + Quoted(r.SubjectContains));
        if (r.BodyContains.Count > 0) when.Add("il testo contiene " + Quoted(r.BodyContains));
        if (r.HasAttachments) when.Add("ha allegati");
        if (r.WithImportance is { } importance) when.Add(importance == Importance.Low ? "l'importanza è bassa" : "l'importanza è alta");
        if (r.HiddenConditions.Count > 0) when.Add("altre condizioni di Outlook (" + string.Join(", ", r.HiddenConditions) + ")");

        string Folder(string id, string name) => folderName?.Invoke(id) ?? (name.Length > 0 ? name : "una cartella");
        var then = new List<string>();
        if (r.MoveTo.Length > 0) then.Add("sposta in " + Folder(r.MoveTo, r.MoveToName));
        if (r.CopyTo.Length > 0) then.Add("copia in " + Folder(r.CopyTo, r.CopyToName));
        if (r.Delete) then.Add("elimina (nel Cestino)");
        if (r.MarkRead) then.Add("segna come letto");
        if (r.Flag) then.Add("contrassegna");
        if (r.Categories.Count > 0) then.Add("categoria " + Quoted(r.Categories, ", "));
        if (r.ForwardTo.Count > 0) then.Add("inoltra a " + string.Join(", ", r.ForwardTo.Select(a => a.Email)));
        if (r.RedirectTo.Count > 0) then.Add("reindirizza a " + string.Join(", ", r.RedirectTo.Select(a => a.Email)));
        if (r.ForwardAsAttachmentTo.Count > 0) then.Add("inoltra come allegato a " + string.Join(", ", r.ForwardAsAttachmentTo.Select(a => a.Email)));
        if (r.Hidden.Except(r.HiddenConditions).Any()) then.Add("altre azioni di Outlook (" + string.Join(", ", r.Hidden.Except(r.HiddenConditions)) + ")");
        if (r.StopProcessing) then.Add("non applicare altre regole");
        if (then.Count == 0) then.Add("nessuna azione");
        return (when.Count == 0 ? "Per tutti i messaggi" : "Se " + string.Join(" e ", when)) + ": " + string.Join(", ", then);
    }

    /// <summary>
    /// Gli indirizzi fuori dall'organizzazione a cui la regola manda la posta: il trucco di chi entra in una casella è
    /// proprio una regola che inoltra tutto altrove, di nascosto. Per gli account di lavoro conta il dominio (i sottodomini
    /// sono dell'organizzazione); per quelli personali ogni indirizzo che non è dell'account stesso.
    /// </summary>
    public static List<string> ExternalForwards(MailRule rule, MailAccount account)
    {
        var own = AccountTypes.DomainOf(account.Email);
        var mine = account.Identities.Select(i => i.Email).Append(account.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool work = AccountTypes.IsWork(account);
        return rule.AllForwards.Select(a => a.Email.Trim()).Where(e => e.Length > 0 && !mine.Contains(e))
            .Where(e => !work || !SenderCheck.SameOrganization(AccountTypes.DomainOf(e), own))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------------------------------------------------------------- regole di Outlook (Graph messageRule)

    static string Str(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    static bool Bool(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    static List<string> Strings(JsonNode? n, string key) =>
        (n?[key] as JsonArray ?? new JsonArray()).Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").Where(s => s.Length > 0).ToList();

    static List<Address> Recipients(JsonNode? n, string key) =>
        (n?[key] as JsonArray ?? new JsonArray()).Select(x => new Address { Name = Str(x?["emailAddress"], "name"), Email = Str(x?["emailAddress"], "address") })
            .Where(a => a.Email.Length > 0).ToList();

    /// <summary>Le condizioni di Outlook che Missiva mostra; le altre restano com'erano (RawConditions).</summary>
    static readonly string[] KnownConditions = ["senderContains", "fromAddresses", "sentToMe", "sentCcMe", "subjectContains", "bodyContains", "hasAttachments", "importance"];
    static readonly string[] KnownActions = ["moveToFolder", "copyToFolder", "markAsRead", "assignCategories", "delete", "stopProcessingRules", "forwardTo", "redirectTo", "forwardAsAttachmentTo"];

    /// <summary>Un valore che conta: non null, non falso, non un elenco vuoto.</summary>
    static bool Set(JsonNode? value) => value switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0 && s != "normal",
        JsonObject o => o.Count > 0,
        _ => true,
    };

    /// <summary>Le parole con cui l'elenco dice quello che una regola di Outlook fa e Missiva non mostra.</summary>
    static string HiddenName(string key) => key switch
    {
        "permanentDelete" => "elimina definitivamente",
        "markImportance" => "cambia l'importanza",
        "headerContains" => "intestazioni",
        "recipientContains" or "sentToAddresses" => "destinatari",
        "bodyOrSubjectContains" => "oggetto o testo",
        "withinSizeRange" => "dimensione",
        "messageActionFlag" => "contrassegno",
        "sensitivity" => "riservatezza",
        "notSentToMe" or "sentOnlyToMe" or "sentToOrCcMe" => "a chi è inviato",
        "isAutomaticReply" or "isAutomaticForward" => "risposte o inoltri automatici",
        _ => key,
    };

    public static MailRule FromGraph(JsonNode? r, string accountId)
    {
        var conditions = r?["conditions"] as JsonObject;
        var actions = r?["actions"] as JsonObject;
        var rule = new MailRule
        {
            AccountId = accountId, ServerId = Str(r, "id"), Name = Str(r, "displayName"), Enabled = Bool(r, "isEnabled"),
            Sequence = r?["sequence"] is JsonValue s && s.TryGetValue<int>(out var n) ? n : 0,
            HasError = Bool(r, "hasError"), IsReadOnly = Bool(r, "isReadOnly"),
            SenderContains = Strings(conditions, "senderContains"),
            FromAddresses = Recipients(conditions, "fromAddresses").Select(a => a.Email).ToList(),
            SentToMe = Bool(conditions, "sentToMe"), SentCcMe = Bool(conditions, "sentCcMe"),
            SubjectContains = Strings(conditions, "subjectContains"), BodyContains = Strings(conditions, "bodyContains"),
            HasAttachments = Bool(conditions, "hasAttachments"),
            WithImportance = Str(conditions, "importance") switch { "high" => Importance.High, "low" => Importance.Low, _ => null },
            MoveTo = Str(actions, "moveToFolder"), CopyTo = Str(actions, "copyToFolder"), MarkRead = Bool(actions, "markAsRead"),
            Categories = Strings(actions, "assignCategories"), Delete = Bool(actions, "delete"), StopProcessing = Bool(actions, "stopProcessingRules"),
            ForwardTo = Recipients(actions, "forwardTo"), RedirectTo = Recipients(actions, "redirectTo"),
            ForwardAsAttachmentTo = Recipients(actions, "forwardAsAttachmentTo"),
            RawConditions = conditions?.DeepClone().AsObject(), RawActions = actions?.DeepClone().AsObject(),
        };
        foreach (var (key, value) in conditions ?? new JsonObject())
            if (!KnownConditions.Contains(key) && !key.StartsWith('@') && Set(value)) rule.HiddenConditions.Add(HiddenName(key));
        if (r?["exceptions"] is JsonObject exceptions && exceptions.Any(x => !x.Key.StartsWith('@') && Set(x.Value))) rule.HiddenConditions.Add("eccezioni");
        rule.HiddenConditions = rule.HiddenConditions.Distinct().ToList();
        rule.Hidden.AddRange(rule.HiddenConditions);
        foreach (var (key, value) in actions ?? new JsonObject())
            if (!KnownActions.Contains(key) && !key.StartsWith('@') && Set(value)) rule.Hidden.Add(HiddenName(key));
        rule.Hidden = rule.Hidden.Distinct().ToList();
        return rule;
    }

    static JsonArray Array(IEnumerable<string> words) => new(words.Select(w => (JsonNode)JsonValue.Create(w)!).ToArray());

    static JsonArray People(IEnumerable<Address> people) =>
        new(people.Select(a => (JsonNode)new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = a.Email, ["name"] = a.Name.Length > 0 ? a.Name : a.Email } }).ToArray());

    /// <summary>
    /// Mette un valore di Missiva fra quelli di Outlook: quando vale, il valore; tolto, un elenco vuoto, falso o null (per
    /// testi ed enumerazioni), solo se prima c'era. Per una regola nuova restano solo quelli che valgono.
    /// </summary>
    static void Put(JsonObject o, string key, JsonNode? value, bool on)
    {
        if (on) o[key] = value;
        else if (o.ContainsKey(key)) o[key] = value switch { JsonArray => new JsonArray(), JsonValue v when v.TryGetValue<bool>(out _) => false, _ => null };
    }

    /// <summary>
    /// Le condizioni come le vuole Graph: quelle di prima che Missiva non mostra restano com'erano, quelle di Missiva
    /// prendono il valore della regola.
    /// </summary>
    static JsonObject ConditionsOf(MailRule r)
    {
        var o = r.RawConditions?.DeepClone().AsObject() ?? new JsonObject();
        Put(o, "senderContains", Array(r.SenderContains), r.SenderContains.Count > 0);
        Put(o, "fromAddresses", People(r.FromAddresses.Select(e => new Address { Email = e })), r.FromAddresses.Count > 0);
        Put(o, "sentToMe", true, r.SentToMe);
        Put(o, "sentCcMe", true, r.SentCcMe);
        Put(o, "subjectContains", Array(r.SubjectContains), r.SubjectContains.Count > 0);
        Put(o, "bodyContains", Array(r.BodyContains), r.BodyContains.Count > 0);
        Put(o, "hasAttachments", true, r.HasAttachments);
        Put(o, "importance", r.WithImportance == Importance.Low ? "low" : "high", r.WithImportance != null);
        foreach (var key in o.Where(x => x.Key.StartsWith('@')).Select(x => x.Key).ToList()) o.Remove(key);
        return o;
    }

    static JsonObject ActionsOf(MailRule r)
    {
        var o = r.RawActions?.DeepClone().AsObject() ?? new JsonObject();
        Put(o, "moveToFolder", r.MoveTo, r.MoveTo.Length > 0);
        Put(o, "copyToFolder", r.CopyTo, r.CopyTo.Length > 0);
        Put(o, "markAsRead", true, r.MarkRead);
        Put(o, "assignCategories", Array(r.Categories), r.Categories.Count > 0);
        Put(o, "delete", true, r.Delete);
        Put(o, "stopProcessingRules", true, r.StopProcessing);
        Put(o, "forwardTo", People(r.ForwardTo), r.ForwardTo.Count > 0);
        Put(o, "redirectTo", People(r.RedirectTo), r.RedirectTo.Count > 0);
        Put(o, "forwardAsAttachmentTo", People(r.ForwardAsAttachmentTo), r.ForwardAsAttachmentTo.Count > 0);
        foreach (var key in o.Where(x => x.Key.StartsWith('@')).Select(x => x.Key).ToList()) o.Remove(key);
        return o;
    }

    /// <summary>Il corpo del POST di una regola nuova.</summary>
    public static JsonObject ToGraph(MailRule r) => new()
    {
        ["displayName"] = r.Name, ["sequence"] = r.Sequence, ["isEnabled"] = r.Enabled, ["conditions"] = ConditionsOf(r), ["actions"] = ActionsOf(r),
    };

    /// <summary>
    /// Il corpo del PATCH: solo i campi cambiati (nome, ordine, attiva, condizioni, azioni); vuoto se non è cambiato nulla,
    /// e allora nessuna richiesta. Condizioni e azioni cambiate vanno intere, con quelle di Outlook che Missiva non mostra.
    /// </summary>
    public static JsonObject Changes(MailRule before, MailRule after)
    {
        var patch = new JsonObject();
        if (after.Name != before.Name) patch["displayName"] = after.Name;
        if (after.Sequence != before.Sequence) patch["sequence"] = after.Sequence;
        if (after.Enabled != before.Enabled) patch["isEnabled"] = after.Enabled;
        var conditions = ConditionsOf(after);
        if (!JsonNode.DeepEquals(conditions, ConditionsOf(before))) patch["conditions"] = conditions;
        var actions = ActionsOf(after);
        if (!JsonNode.DeepEquals(actions, ActionsOf(before))) patch["actions"] = actions;
        return patch;
    }

    /// <summary>I campi del PATCH in parole, per il registro della prova.</summary>
    public static string ChangedText(JsonObject patch) => string.Join(", ", patch.Select(p => p.Key switch
    {
        "displayName" => "nome", "sequence" => "ordine", "isEnabled" => patch["isEnabled"]?.GetValue<bool>() == true ? "accesa" : "spenta",
        "conditions" => "condizioni", "actions" => "azioni", var other => other,
    }));

    // ---------------------------------------------------------------- confronto con un messaggio

    /// <summary>Se per decidere serve il corpo del messaggio: il testo, o il Cc (che l'elenco non ha).</summary>
    public static bool NeedsBody(MailRule r) => r.BodyContains.Count > 0 || r.SentCcMe;

    /// <summary>
    /// Se il messaggio soddisfa le condizioni, come Outlook: tutte le condizioni insieme, e dentro ognuna basta una parola
    /// (o un indirizzo). body serve solo per il testo e il Cc (NeedsBody); senza, quelle condizioni non valgono.
    /// </summary>
    /// <param name="text">Il testo del corpo, se chi chiama l'ha già calcolato (RunAsync: una volta per messaggio).</param>
    public static bool Matches(MailRule r, MessageSummary m, MessageBody? body, MailAccount account, string? text = null)
    {
        if (!MatchesBesidesText(r, m, body, account)) return false;
        if (r.BodyContains.Count == 0) return true;
        if (body == null) return false;
        var words = text ?? BodyText(body);
        return r.BodyContains.Any(w => words.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Tutte le condizioni tranne il testo del corpo, che costa: si guarda per ultimo, e solo se le altre valgono.</summary>
    static bool MatchesBesidesText(MailRule r, MessageSummary m, MessageBody? body, MailAccount account)
    {
        // Condizioni o eccezioni di Outlook che Missiva non sa controllare (oggetto o testo, destinatari, intestazioni...):
        // come assenti, "Se oggetto o testo contiene newsletter: sposta" varrebbe per tutti i messaggi. Mai, invece.
        if (r.HiddenConditions.Count > 0) return false;
        if (r.Unreadable) return false; // parole o cartelle che non si decifrano più: mai, invece che per tutti
        var mine = account.Identities.Select(i => i.Email).Append(account.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (r.FromAddresses.Count > 0 || r.SenderContains.Count > 0)
        {
            bool address = r.FromAddresses.Any(a => a.Equals(m.From.Email, StringComparison.OrdinalIgnoreCase));
            bool word = r.SenderContains.Any(w => m.From.Email.Contains(w, StringComparison.CurrentCultureIgnoreCase) || m.From.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase));
            if (!address && !word) return false;
        }
        if (r.SentToMe && !(body?.To.Count > 0 ? body.To : m.To).Any(a => mine.Contains(a.Email))) return false;
        if (r.SentCcMe && !(body?.Cc ?? []).Any(a => mine.Contains(a.Email))) return false;
        if (r.SubjectContains.Count > 0 && !r.SubjectContains.Any(w => m.Subject.Contains(w, StringComparison.CurrentCultureIgnoreCase))) return false;
        if (r.HasAttachments && !m.HasAttachments) return false;
        // L'elenco sa solo dell'importanza alta: "bassa" non corrisponde mai nelle regole di Missiva.
        if (r.WithImportance == Importance.High && !m.IsImportant) return false;
        if (r.WithImportance == Importance.Low) return false;
        return true;
    }

#if DEBUG
    /// <summary>Prove: quante volte il testo di un corpo è stato calcolato per le regole, e su quale thread l'ultima volta.</summary>
    internal static int TextReads, TextThread;
#endif

    /// <summary>Il testo di un corpo come lo si legge (MailHtml.ReadableText): un'analisi intera dell'HTML, 26 ms per una newsletter da 211 KB.</summary>
    static string BodyText(MessageBody body)
    {
#if DEBUG
        Interlocked.Increment(ref TextReads);
        TextThread = Environment.CurrentManagedThreadId;
#endif
        return MailHtml.ReadableText(body.Html);
    }

    // ---------------------------------------------------------------- esecuzione

    /// <summary>Quello che le regole hanno fatto: i messaggi usciti dalla cartella (spostati o nel Cestino), segnati letti, contrassegnati.</summary>
    public sealed class Outcome
    {
        public HashSet<MessageSummary> Removed { get; } = new();
        public HashSet<MessageSummary> Read { get; } = new();
        public HashSet<MessageSummary> Flagged { get; } = new();
        public int Matched { get; set; }
        /// <summary>Le azioni fatte, in parole, per il registro e per "Esegui ora".</summary>
        public List<string> Done { get; } = new();
        /// <summary>Azioni che solo Exchange sa fare (inoltro): con "Esegui ora" non si fanno.</summary>
        public bool SkippedForwards { get; set; }
        /// <summary>Quanti messaggi le azioni cambierebbero (chiesto a confirm prima di agire).</summary>
        public int Touched { get; set; }
        /// <summary>La conferma di "Esegui ora" non è arrivata: nessuna azione fatta.</summary>
        public bool Canceled { get; set; }
        /// <summary>
        /// Le azioni non riuscite, in parole, una per gruppo ("3 non segnati come letti: ..."): un errore non ferma più gli
        /// altri gruppi (scelte del 12 ottobre, C-51).
        /// </summary>
        public List<string> Errors { get; } = new();
        /// <summary>
        /// I messaggi a cui resta qualcosa da fare e che si possono riprovare, ognuno con le azioni da non rifare (quelle
        /// riuscite, e quelle che non si ripetono anche se non riuscite: Rules.MoveAction e le copie).
        /// </summary>
        public Dictionary<MessageSummary, HashSet<string>> Pending { get; } = new();
    }

    /// <summary>
    /// I nomi delle azioni su un messaggio, per sapere al controllo dopo quali non rifare (Outcome.Pending). Segna come
    /// letto, contrassegna e categoria si possono ripetere: rifarle non cambia nulla. Una copia no (sarebbe un doppione):
    /// partita una volta non si riprova mai, nemmeno se non è riuscita. Sposta ed elimina (nel Cestino) si riprovano solo
    /// se il server ha rifiutato lo spostamento per intero (MailException.Repeatable).
    /// </summary>
    internal const string ReadAction = "letto", FlagAction = "contrassegno", CategoryAction = "categoria:", CopyAction = "copia:", MoveAction = "sposta";

    /// <summary>
    /// Le regole applicate ai messaggi, nell'ordine, con le azioni raggruppate: una sola richiesta per azione e cartella
    /// (un UID MOVE per tutti i messaggi che vanno nella stessa cartella). Spostamenti ed eliminazioni per ultimi, perché
    /// cambiano l'identificativo; se più regole spostano, vale la prima, come in Outlook.
    /// Ogni gruppo di azioni va per conto suo: se uno non riesce l'errore finisce nell'esito (Outcome.Errors) e gli altri
    /// gruppi partono lo stesso. Prima il primo errore fermava tutto, e i messaggi dei gruppi dopo, mai toccati, non
    /// passavano più dalle regole (audit dell'11 ottobre, C-51). Un messaggio con un'azione non riuscita resta dov'è:
    /// non si sposta e non si elimina in questo giro, perché dopo non lo si ritroverebbe per finire il resto, ed è fra
    /// quelli da riprovare (Outcome.Pending).
    /// </summary>
    /// <param name="folders">Le cartelle dell'account (per trovare quella di Sposta e Copia, e il Cestino).</param>
    /// <param name="confirm">
    /// "Esegui ora": deciso che cosa fare, prima di agire, quanti messaggi le azioni cambiano; falso e nulla si fa
    /// (Outcome.Canceled). Null per i nuovi messaggi, dove la regola l'ha già voluta l'utente.
    /// </param>
    /// <param name="before">Per i messaggi che si riprovano: le azioni da non rifare (Outcome.Pending del giro di prima).</param>
    public static async Task<Outcome> RunAsync(IMailProvider provider, IReadOnlyList<MessageSummary> messages, IReadOnlyList<MailRule> rules,
                                               Func<Task<List<MailFolder>>> folders, CancellationToken cancel = default, Func<int, Task<bool>>? confirm = null,
                                               IReadOnlyDictionary<MessageSummary, HashSet<string>>? before = null)
    {
        var outcome = new Outcome();
        var active = rules.Where(r => r.Enabled && r.HasActions).ToList();
        if (active.Count == 0 || messages.Count == 0) return outcome;
        bool Skip(MessageSummary m, string action) => before != null && before.TryGetValue(m, out var not) && not.Contains(action);
        var read = new List<MessageSummary>();
        var flag = new List<MessageSummary>();
        var categories = new Dictionary<string, List<MessageSummary>>(StringComparer.CurrentCultureIgnoreCase);
        var copies = new Dictionary<string, List<MessageSummary>>();
        var moves = new Dictionary<string, List<MessageSummary>>();
        var trash = new List<MessageSummary>();
        foreach (var m in messages)
        {
            cancel.ThrowIfCancellationRequested();
            MessageBody? body = null;
            // Il testo del corpo: una volta per messaggio e su un thread del pool. Prima ogni regola sul testo lo ricalcolava
            // sul thread di chi chiama, quello dell'interfaccia (audit, v2 V-08: tre regole e 30 newsletter, 90 analisi).
            string? text = null;
            // Uno spostamento già partito e non da ripetere: il messaggio non si sposta e non si elimina più.
            bool matched = false, moved = Skip(m, MoveAction);
            foreach (var rule in active)
            {
                if (NeedsBody(rule) && body == null)
                {
                    try { body = provider.KeptBody(m) ?? await provider.GetBodyAsync(m, cancel); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { Log.Write($"regole: corpo di {m.Id} non letto, le condizioni sul testo non valgono: {ex.Message}"); }
                }
                if (rule.BodyContains.Count > 0 && body is { } whole && text == null && MatchesBesidesText(rule, m, body, provider.Account))
                    text = await Task.Run(() => BodyText(whole), cancel);
                if (!Matches(rule, m, body, provider.Account, text)) continue;
                matched = true;
                if (rule.MarkRead && !m.IsRead && !read.Contains(m) && !Skip(m, ReadAction)) read.Add(m);
                if (rule.Flag && !m.IsFlagged && !flag.Contains(m) && !Skip(m, FlagAction)) flag.Add(m);
                foreach (var c in rule.Categories)
                {
                    if (Skip(m, CategoryAction + c.ToUpperInvariant())) continue;
                    if (!categories.TryGetValue(c, out var list)) categories[c] = list = new();
                    if (!list.Contains(m)) list.Add(m);
                }
                if (rule.CopyTo.Length > 0 && !Skip(m, CopyAction + rule.CopyTo))
                {
                    if (!copies.TryGetValue(rule.CopyTo, out var list)) copies[rule.CopyTo] = list = new();
                    if (!list.Contains(m)) list.Add(m);
                }
                if (!moved && rule.MoveTo.Length > 0 && rule.MoveTo != m.FolderId)
                {
                    if (!moves.TryGetValue(rule.MoveTo, out var list)) moves[rule.MoveTo] = list = new();
                    list.Add(m);
                    moved = true;
                }
                else if (!moved && rule.Delete)
                {
                    trash.Add(m);
                    moved = true;
                }
                if (rule.AllForwards.Any()) outcome.SkippedForwards = true;
                if (rule.StopProcessing) break;
            }
            if (matched) outcome.Matched++;
        }
        outcome.Touched = read.Concat(flag).Concat(categories.Values.SelectMany(l => l)).Concat(copies.Values.SelectMany(l => l))
            .Concat(moves.Values.SelectMany(l => l)).Concat(trash).Distinct().Count();
        if (outcome.Touched > 0 && confirm != null && !await confirm(outcome.Touched))
        {
            outcome.Canceled = true;
            return outcome;
        }

        // Per messaggio, le azioni da non rifare se lo si riprova; i messaggi con un'azione non riuscita in questo giro; e, di
        // questi, quelli a cui resta qualcosa che si può riprovare.
        var notAgain = new Dictionary<MessageSummary, HashSet<string>>();
        var failed = new HashSet<MessageSummary>();
        var retry = new HashSet<MessageSummary>();
        HashSet<string> NotAgain(MessageSummary m)
        {
            if (!notAgain.TryGetValue(m, out var set)) notAgain[m] = set = before != null && before.TryGetValue(m, out var old) ? new(old) : new();
            return set;
        }
        // Un gruppo di azioni: riuscito, non si rifà. Non riuscito, l'errore va nell'esito e gli altri gruppi partono lo
        // stesso; si riprova solo se rifarlo non può lasciare doppioni (repeatable, o il rifiuto intero di uno spostamento).
        async Task<bool> GroupAsync(string? action, IReadOnlyList<MessageSummary> list, Func<Task> work, string failure, bool repeatable = true)
        {
            try
            {
                await work();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                bool again = repeatable || ex is MailException { Repeatable: true };
                outcome.Errors.Add($"{failure}{(again ? "" : ", e non si riprova")}: {ex.Message}");
                foreach (var m in list)
                {
                    failed.Add(m);
                    if (again) retry.Add(m);
                    else if (action != null) NotAgain(m).Add(action);
                }
                return false;
            }
            if (action != null) foreach (var m in list) NotAgain(m).Add(action);
            return true;
        }
        if (read.Count > 0 && await GroupAsync(ReadAction, read, () => provider.SetReadAsync(read, true), $"{read.Count} non segnati come letti"))
        {
            foreach (var m in read) { m.IsRead = true; outcome.Read.Add(m); }
            outcome.Done.Add($"{read.Count} segnati come letti");
        }
        if (flag.Count > 0 && await GroupAsync(FlagAction, flag, () => provider.SetFlagAsync(flag, true), $"{flag.Count} non contrassegnati"))
        {
            foreach (var m in flag) { m.IsFlagged = true; outcome.Flagged.Add(m); }
            outcome.Done.Add($"{flag.Count} contrassegnati");
        }
        foreach (var (category, list) in categories)
            if (await GroupAsync(CategoryAction + category.ToUpperInvariant(), list, () => provider.SetCategoryAsync(list, category, true), $"{list.Count} senza la categoria \"{category}\""))
                outcome.Done.Add($"{list.Count} con la categoria \"{category}\"");

        // Le cartelle dell'account, lette qui la prima volta che servono: se non si leggono, nessuna copia e nessuno
        // spostamento è partito, e quei messaggi si riprovano.
        List<MailFolder> known = [];
        var placed = copies.Values.Concat(moves.Values).SelectMany(l => l).Concat(trash).Distinct().ToList();
        if (placed.Count > 0 && !await GroupAsync(null, placed, async () => known = await folders(), $"{placed.Count} non copiati né spostati, le cartelle non si sono lette"))
        {
            copies.Clear();
            moves.Clear();
            trash.Clear();
        }
        foreach (var (id, list) in copies)
        {
            if (known.FirstOrDefault(f => f.Id == id) is not { } folder) { outcome.Done.Add($"cartella per la copia non trovata ({list.Count} messaggi)"); continue; }
            if (await GroupAsync(CopyAction + id, list, () => provider.CopyAsync(list, folder), $"{list.Count} non copiati in {folder.Name}", repeatable: false))
                outcome.Done.Add($"{list.Count} copiati in {folder.Name}");
        }
        // Spostamenti ed eliminazioni solo per i messaggi a cui è riuscito tutto il resto: gli altri restano dove sono, da riprovare.
        List<MessageSummary> Ready(List<MessageSummary> list, string where)
        {
            var held = list.Where(failed.Contains).ToList();
            if (held.Count == 0) return list;
            foreach (var m in held) retry.Add(m);
            outcome.Errors.Add($"{held.Count} lasciati dove sono, non {where}: un'azione di prima non è riuscita");
            return list.Except(held).ToList();
        }
        foreach (var (id, list) in moves)
        {
            if (known.FirstOrDefault(f => f.Id == id) is not { } folder) { outcome.Done.Add($"cartella per lo spostamento non trovata ({list.Count} messaggi)"); continue; }
            var leaving = Ready(list, $"spostati in {folder.Name}");
            if (leaving.Count == 0 || !await GroupAsync(MoveAction, leaving, () => provider.MoveAsync(leaving, folder), $"{leaving.Count} non spostati in {folder.Name}", repeatable: false)) continue;
            foreach (var m in leaving) outcome.Removed.Add(m);
            outcome.Done.Add($"{leaving.Count} spostati in {folder.Name}");
        }
        if (trash.Count > 0)
        {
            var bin = known.FirstOrDefault(f => f.Kind == FolderKind.Deleted);
            bool asked = bin != null || await GroupAsync(null, trash, async () => bin = await provider.EnsureTrashAsync(), $"{trash.Count} non eliminati, il Cestino non si è potuto creare");
            if (asked && bin == null) outcome.Done.Add($"nessun Cestino ({trash.Count} messaggi non eliminati)");
            else if (bin != null)
            {
                var away = trash.Where(m => m.FolderId != bin.Id).ToList();
                var leaving = Ready(away, "messi nel Cestino");
                if (leaving.Count > 0 ? await GroupAsync(MoveAction, leaving, () => provider.MoveAsync(leaving, bin), $"{leaving.Count} non messi nel Cestino", repeatable: false) : away.Count == 0)
                {
                    foreach (var m in leaving) outcome.Removed.Add(m);
                    outcome.Done.Add($"{leaving.Count} nel Cestino");
                }
            }
        }
        foreach (var m in retry) outcome.Pending[m] = NotAgain(m);
        return outcome;
    }

    /// <summary>L'impronta di un messaggio per RulesSeen: dall'account e dalla chiave che non cambia, senza nulla in chiaro.</summary>
    internal static string SeenKey(MailAccount account, MessageSummary m)
    {
        var key = m.StableId.Length > 0 ? m.StableId : $"{m.FolderId}|{m.Id}|{m.Date.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Id + "|" + key)))[..20];
    }

    /// <summary>
    /// Per account, fin dove le regole di Missiva hanno guardato la Posta in arrivo ("UIDVALIDITY:UID"). Solo in memoria, e
    /// solo finché l'account ha regole attive: dopo un riavvio, o con una regola nuova, si parte dai messaggi arrivati dal
    /// controllo di prima, come nell'elenco dei nuovi (le regole girano con Missiva aperta, non su quello arrivato a Missiva
    /// chiusa).
    /// </summary>
    static readonly Dictionary<string, string> marks = new();

    /// <summary>
    /// Un messaggio che le regole non hanno finito (un'azione non riuscita): quante volte ci hanno provato, e le azioni da
    /// non rifare. Solo in memoria, per account: dopo un riavvio non si riprova (RulesSeen lo ricorda come già guardato).
    /// </summary>
    sealed record Unfinished(MessageSummary Message, int Tries, HashSet<string> NotAgain);

    static readonly Dictionary<string, List<Unfinished>> unfinished = new();

    /// <summary>Quante volte in tutto le regole provano un messaggio, la prima compresa: poi lo lasciano dov'è, con una riga nel registro.</summary>
    internal const int MaxTries = 3;

    /// <summary>
    /// Le regole di Missiva sui messaggi arrivati nella Posta in arrivo (Gmail, IMAP), dal controllo dei nuovi messaggi:
    /// quelli dopo l'ultimo guardato (IMailProvider.GetArrivedAsync), letti o no e anche più di 20, non solo i non letti che
    /// le notifiche mostrano: dopo una notte con il portatile chiuso le regole li vedono tutti. Ogni messaggio una volta sola,
    /// anche se il controllo dopo lo ritrova (RulesSeen). null se l'account non ha regole attive.
    /// Un messaggio con un'azione non riuscita si riprova al controllo dopo, se è ancora nella Posta in arrivo
    /// (StillThereAsync), solo per le azioni che restano e al più MaxTries volte in tutto (scelte del 12 ottobre, C-51):
    /// prima dopo un errore a metà nessun messaggio di quel giro passava più dalle regole.
    /// </summary>
    public static async Task<Outcome?> ApplyToNewAsync(IMailProvider provider, DateTime sinceUtc, Func<Task<List<MailFolder>>> folders)
    {
        if (provider.HasServerRules || provider.Account.Kind == AccountKind.Microsoft) return null;
        var id = provider.Account.Id;
        var rules = For(provider.Account).Where(r => r.Enabled).ToList();
        if (rules.Count == 0)
        {
            lock (marks)
            {
                marks.Remove(id);
                unfinished.Remove(id);
            }
            return null;
        }
        string? mark;
        lock (marks) mark = marks.GetValueOrDefault(id);
        if (await provider.GetArrivedAsync(mark, sinceUtc) is not { } arrived) return null;
        List<Unfinished> again;
        lock (marks)
        {
            if (arrived.Mark != null) marks[id] = arrived.Mark;
            // Presi qui, non lasciati nell'elenco: un altro controllo dello stesso account partito intanto non li riprova insieme a questo.
            again = unfinished.Remove(id, out var waiting) ? waiting : [];
        }
        var seen = Store.Settings.RulesSeen;
        var fresh = arrived.Messages.Where(m => !seen.Contains(SeenKey(provider.Account, m))).ToList();
        var next = new List<Unfinished>();
        int left = 0, gone = 0;
        void Later(Unfinished u, HashSet<string> notAgain)
        {
            if (u.Tries + 1 >= MaxTries) left++;
            else next.Add(new(u.Message, u.Tries + 1, notAgain));
        }
        if (again.Count > 0)
        {
            // Solo quelli ancora nella Posta in arrivo: uno spostato o eliminato intanto (a mano, o da uno spostamento di prima
            // riuscito senza la risposta) non si tocca più. Se non si riesce a saperlo, per questo giro restano come sono.
            try
            {
                if (await provider.StillThereAsync(again.Select(u => u.Message).ToList()) is { } there)
                {
                    gone = again.Count - there.Count;
                    again = again.Where(u => there.Contains(u.Message)).ToList();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Write($"regole di Missiva su {provider.Account.Email}: non si è potuto vedere se {again.Count} messaggi da riprovare ci sono ancora ({ex.GetType().Name}): per ora restano come sono");
                foreach (var u in again) Later(u, u.NotAgain);
                again = [];
            }
        }
        if (fresh.Count > 0)
        {
            // Ricordati prima di agire: se il giro si interrompe a metà (Missiva chiusa, un errore imprevisto), al controllo
            // dopo non si ripete su quelli già toccati. Quelli con un'azione non riuscita si riprovano dall'elenco in memoria.
            foreach (var m in fresh) seen.Add(SeenKey(provider.Account, m));
            const int remembered = 2000; // quanti messaggi già passati dalle regole si ricordano: i più vecchi escono
            if (seen.Count > remembered) seen.RemoveRange(0, seen.Count - remembered);
            Store.SaveSoon();
        }
        Outcome? outcome = null;
        if (fresh.Count + again.Count > 0)
        {
            var tried = again.ToDictionary(u => u.Message);
            outcome = await RunAsync(provider, [.. again.Select(u => u.Message), .. fresh], rules, folders, before: again.ToDictionary(u => u.Message, u => u.NotAgain));
            foreach (var (m, notAgain) in outcome.Pending) Later(tried.GetValueOrDefault(m) ?? new(m, 0, notAgain), notAgain);
            if (outcome.Matched > 0)
                Log.Write($"regole di Missiva su {provider.Account.Email}: {MatchedLine(outcome.Matched, fresh.Count, again.Count)}; {Log.Private(string.Join(", ", outcome.Done))}");
            if (outcome.Errors.Count > 0)
                Log.Write($"regole di Missiva su {provider.Account.Email}: non riuscito ({outcome.Errors.Count}): {Log.Private(string.Join("; ", outcome.Errors))}");
        }
        lock (marks)
        {
            if (next.Count > 0) unfinished[id] = [.. unfinished.GetValueOrDefault(id) ?? [], .. next];
        }
        if (next.Count + left + gone > 0) Log.Write($"regole di Missiva su {provider.Account.Email}: " + RetryLine(next.Count, left, gone));
        return outcome;
    }

    /// <summary>Quanti messaggi corrispondono alle regole, per il registro: com'era con i soli nuovi, e con i riprovati detti a parte.</summary>
    internal static string MatchedLine(int matched, int fresh, int again) =>
        again == 0 ? $"{matched} di {fresh} nuovi messaggi corrispondono" : $"{matched} di {fresh + again} messaggi corrispondono ({fresh} nuovi, {again} riprovati)";

    /// <summary>La riga del registro sui messaggi non finiti: quanti si riprovano, quanti si lasciano dopo MaxTries tentativi, quanti non c'erano più. Mai oggetti né mittenti.</summary>
    internal static string RetryLine(int next, int left, int gone) => string.Join("; ", new[]
    {
        next == 0 ? "" : next == 1 ? "un messaggio non finito, si riprova al prossimo controllo" : $"{next} messaggi non finiti, si riprovano al prossimo controllo",
        left == 0 ? "" : left == 1 ? $"un messaggio lasciato com'è dopo {MaxTries} tentativi, le regole non lo riprovano più" : $"{left} messaggi lasciati come sono dopo {MaxTries} tentativi, le regole non li riprovano più",
        gone == 0 ? "" : gone == 1 ? "un messaggio da riprovare non è più nella Posta in arrivo" : $"{gone} messaggi da riprovare non sono più nella Posta in arrivo",
    }.Where(t => t.Length > 0));

#if DEBUG
    /// <summary>Prove: le regole ripartono senza il segno di dove erano arrivate (come dopo un riavvio), e senza messaggi da riprovare.</summary>
    internal static void ForgetMarks()
    {
        lock (marks)
        {
            marks.Clear();
            unfinished.Clear();
        }
    }

    /// <summary>Prove: quanti messaggi dell'account aspettano di essere riprovati.</summary>
    internal static int TestUnfinished(string accountId) { lock (marks) return unfinished.GetValueOrDefault(accountId)?.Count ?? 0; }
#endif
}
