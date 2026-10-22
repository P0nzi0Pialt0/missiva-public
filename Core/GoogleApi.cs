using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Missiva;

/// <summary>
/// Un'API di Google usata da Missiva oltre alla posta, con quello che serve a spiegare gli errori: come si chiama, la sua
/// pagina nella libreria di Google Cloud, e due frasi intere segnate con N (si traducono dove si mostrano, con T): senza
/// il permesso (che cosa manca, il pulsante che lo dà e dove sta) e con le richieste limitate.
/// </summary>
public sealed record GoogleService(string Name, string Library, string NoPermission, string Limited);

/// <summary>
/// Le richieste alle API di Google (Calendar, People) con il token di accesso dell'account, lo stesso della posta
/// (GoogleAuth.TokenAsync). Gli errori diventano MailException in italiano, con il codice HTTP.
/// </summary>
public static class GoogleApi
{
    /// <summary>Come per Graph (Network.ApiHandler): risposte compresse, HTTP/2, connessioni tenute cinque minuti.</summary>
    static HttpClient Http = new(Network.ApiHandler()) { Timeout = TimeSpan.FromSeconds(MailLimits.GoogleHttpSeconds) };

#if DEBUG
    /// <summary>Prove (Missiva.exe --prova-bozze): un gestore HTTP finto al posto di Google.</summary>
    internal static void UseTestHandler(HttpMessageHandler handler) => Http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(MailLimits.GoogleHttpSeconds) };
#endif

    /// <summary>
    /// Una richiesta a un indirizzo intero. Un 401 (token rifiutato) lo fa dimenticare e riprova una volta.
    /// </summary>
    public static async Task<JsonNode?> SendAsync(GoogleService service, MailAccount account, HttpMethod method, string url, JsonNode? body = null, CancellationToken cancel = default)
    {
        for (int attempt = 0; ; attempt++)
        {
            var token = await GoogleAuth.TokenAsync(account);
            using var request = Network.ApiRequest(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var response = await Http.SendAsync(request, cancel);
            var text = await response.Content.ReadAsStringAsync(cancel);
            Log.Time($"Google {method} {url.Split('?')[0]} ({text.Length / 1024} KB, HTTP/{response.Version}{(response.IsSuccessStatusCode ? "" : $", risposta {(int)response.StatusCode}")})", clock);
            if (response.IsSuccessStatusCode) return text.Length == 0 ? null : JsonNode.Parse(text);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                GoogleAuth.Forget(account);
                continue;
            }
            throw Error(service, (int)response.StatusCode, text);
        }
    }

    static string Str(JsonNode? n, string key) => n?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>L'errore di Google (error.message e il motivo di error.errors) spiegato nella lingua di Missiva.</summary>
    static MailException Error(GoogleService service, int status, string text)
    {
        JsonNode? error = null;
        try { error = JsonNode.Parse(text)?["error"]; }
        catch (System.Text.Json.JsonException) { }
        var reason = Str(error?["errors"]?.AsArray().FirstOrDefault(), "reason");
        var message = Str(error, "message");
        string explained;
        if (reason == "accessNotConfigured")
            explained = T("{0} non è attiva sul progetto Google Cloud di Missiva: abilitala su https://console.cloud.google.com/apis/library/{1}.googleapis.com e riprova.", service.Name, service.Library);
        else if (reason == "insufficientPermissions" || status == 403 && message.Contains("insufficient", StringComparison.OrdinalIgnoreCase))
            explained = T(service.NoPermission);
        else if (status == 429 || reason is "rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded" or "dailyLimitExceeded")
            explained = T(service.Limited);
        else if (status == 404)
            explained = T("L'elemento non c'è più su Google.");
        else if (status == 410)
            explained = T("L'elemento è già stato eliminato su Google.");
        else
            explained = message.Length > 0 ? T("{0} ha risposto con un errore: {1}", service.Name, message) : T("{0} ha risposto con un errore ({1})", service.Name, status);
        return new MailException(explained, null, status) { Code = reason.Length > 0 ? reason : Str(error, "status") };
    }
}
