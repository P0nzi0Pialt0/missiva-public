using System.Globalization;
using System.Text.Json.Nodes;

namespace Missiva;

public enum AutoReplyState { Off, On, Scheduled }

/// <summary>A chi fuori dall'organizzazione (o, per gli account personali, a chi scrive da un altro indirizzo) si risponde.</summary>
public enum AutoReplyAudience { None, Contacts, All }

/// <summary>
/// Le risposte automatiche di Outlook (fuori sede), come nel nuovo Outlook: spente, attive, o attive in un periodo; un
/// messaggio per l'organizzazione e uno per gli esterni, in HTML. Graph le tiene in me/mailboxSettings
/// (automaticRepliesSetting), con il permesso MailboxSettings.ReadWrite, chiesto solo dal pulsante della finestra. Gli
/// orari qui sono nell'ora di Windows; verso Graph vanno in UTC.
/// </summary>
public sealed record AutoReplies
{
    public AutoReplyState State { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public string InternalHtml { get; init; } = "";
    public string ExternalHtml { get; init; } = "";
    public AutoReplyAudience External { get; init; } = AutoReplyAudience.All;
    /// <summary>
    /// Il valore di externalAudience letto da Graph quando non è uno dei tre che Missiva conosce (vuoto se mancava): si
    /// mostra come "tutti", ma finché External resta All torna sul server com'era. Prima un valore sconosciuto veniva letto
    /// come "tutti" e Salva, anche senza cambiare nulla, lo scriveva come "all": risposte a chiunque (audit, P-01).
    /// </summary>
    public string? UnknownAudience { get; init; }

    /// <summary>Se in questo momento rispondono: attive, o nel periodo scelto.</summary>
    public bool ActiveAt(DateTime now) => State == AutoReplyState.On || State == AutoReplyState.Scheduled && now >= Start && now < End;

    /// <summary>Se sono programmate per dopo (il periodo non è ancora cominciato).</summary>
    public bool PlannedAfter(DateTime now) => State == AutoReplyState.Scheduled && now < Start;

    /// <summary>Un istante in parole: "venerdì 2 ottobre alle 18:00" (con l'anno se non è quello di adesso), nella lingua di Missiva.</summary>
    public static string When(DateTime time, DateTime now) =>
        T("{0} alle {1}", time.ToString(time.Year == now.Year ? "dddd d MMMM" : "dddd d MMMM yyyy", Lingua.Culture), time.ToString("H:mm", Lingua.Culture));

    /// <summary>La riga della barra sopra l'elenco: attive, fino a quando, o da quando.</summary>
    public string Describe(DateTime now) => State switch
    {
        AutoReplyState.On => T("Risposte automatiche attive"),
        AutoReplyState.Scheduled when now < Start => T("Risposte automatiche programmate da {0} a {1}", When(Start, now), When(End, now)),
        AutoReplyState.Scheduled when now >= End => T("Risposte automatiche spente: il periodo è finito {0}", When(End, now)),
        AutoReplyState.Scheduled => T("Risposte automatiche attive fino a {0}", When(End, now)),
        _ => T("Risposte automatiche spente"),
    };

    public static AutoReplies FromGraph(JsonNode? s)
    {
        static string Str(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<string>(out var text) ? text : "";
        var audience = Str(s, "externalAudience");
        return new AutoReplies
        {
            UnknownAudience = audience is "none" or "contactsOnly" or "all" ? null : audience,
            State = Str(s, "status") switch { "alwaysEnabled" => AutoReplyState.On, "scheduled" => AutoReplyState.Scheduled, _ => AutoReplyState.Off },
            Start = Calendars.LocalTime(s?["scheduledStartDateTime"]),
            End = Calendars.LocalTime(s?["scheduledEndDateTime"]),
            InternalHtml = Str(s, "internalReplyMessage"),
            ExternalHtml = Str(s, "externalReplyMessage"),
            External = audience switch { "none" => AutoReplyAudience.None, "contactsOnly" => AutoReplyAudience.Contacts, _ => AutoReplyAudience.All },
        };
    }

    /// <summary>Il corpo del PATCH di me/mailboxSettings: solo automaticRepliesSetting, gli orari in UTC.</summary>
    public JsonObject ToGraph()
    {
        static JsonObject Utc(DateTime local)
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            // L'ora che non esiste (le 2:30 dell'ultima domenica di marzo) è quella dopo il cambio.
            if (TimeZoneInfo.Local.IsInvalidTime(local)) local = local.AddHours(1);
            return new JsonObject
            {
                ["dateTime"] = TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                ["timeZone"] = "UTC",
            };
        }
        var setting = new JsonObject
        {
            ["status"] = State switch { AutoReplyState.On => "alwaysEnabled", AutoReplyState.Scheduled => "scheduled", _ => "disabled" },
        };
        // Il pubblico esterno che Missiva non conosce e che nessuno ha cambiato: com'era (se mancava, il PATCH non lo tocca).
        if (UnknownAudience == null || External != AutoReplyAudience.All)
            setting["externalAudience"] = External switch { AutoReplyAudience.None => "none", AutoReplyAudience.Contacts => "contactsOnly", _ => "all" };
        else if (UnknownAudience.Length > 0) setting["externalAudience"] = UnknownAudience;
        setting["internalReplyMessage"] = InternalHtml;
        setting["externalReplyMessage"] = ExternalHtml;
        if (State == AutoReplyState.Scheduled)
        {
            setting["scheduledStartDateTime"] = Utc(Start);
            setting["scheduledEndDateTime"] = Utc(End);
        }
        return new JsonObject { ["automaticRepliesSetting"] = setting };
    }
}
