using System.Net.Http.Headers;
using System.Text;

namespace Missiva;

/// <summary>Come si annulla l'iscrizione: una POST di Missiva (RFC 8058), un messaggio da inviare a mano, una pagina nel browser.</summary>
public enum UnsubscribeKind { OneClick, Mailto, Web }

/// <summary>
/// Quello che "Annulla iscrizione" farà per un messaggio (Unsubscribe.For). Target: l'indirizzo https del mittente
/// (OneClick, Web) o il link mailto: ridotto a un destinatario, oggetto e testo (Mailto).
/// </summary>
public sealed record UnsubscribeAction(UnsubscribeKind Kind, string Target)
{
    /// <summary>Quello che la conferma mostra: il sito (in punycode se ha caratteri non latini) o l'indirizzo a cui si scrive.</summary>
    public string Where => Kind == UnsubscribeKind.Mailto ? Mailto.Parse(Target).To[0] : new Uri(Target).IdnHost;

    /// <summary>
    /// L'indirizzo intero come lo mostrano le finestre, al più 300 caratteri: per un sito con il nome in punycode, come
    /// Where. Uri.AbsoluteUri lo tiene con le lettere non latine: la conferma mostrava lo stesso sito in due forme, e in
    /// quella lunga una lettera sosia non si distingueva (audit dell'11 ottobre, T-13).
    /// </summary>
    public string Shown
    {
        get
        {
            var text = Kind != UnsubscribeKind.Mailto && Uri.TryCreate(Target, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.IdnHost}{uri.PathAndQuery}{uri.Fragment}" : Target;
            return text.Length > 300 ? text[..300] + "…" : text;
        }
    }
}

/// <summary>
/// Annulla iscrizione, come nel nuovo Outlook e in Gmail (audit, F-11): dalle intestazioni List-Unsubscribe (RFC 2369) e
/// List-Unsubscribe-Post (RFC 8058) del messaggio, lette insieme al corpo. Niente parte da solo: For dice solo che cosa il
/// comando farebbe; la POST parte da OneClickAsync, dopo la conferma. Mai nella Posta indesiderata (confermerebbe
/// l'indirizzo a chi manda spam), mai con la fascia rossa del mittente non verificato; la POST solo se il server della
/// casella dice che il messaggio viene davvero dal dominio del mittente, con una sua firma DKIM (SenderCheck.Signed).
/// </summary>
public static class Unsubscribe
{
    /// <summary>Oltre queste misure l'intestazione o un suo link non si guardano: nessun mittente onesto ci arriva.</summary>
    const int MaxHeader = 8192, MaxLink = 2048, MaxLinks = 8;

    /// <summary>
    /// I link di una List-Unsubscribe: solo quelli fra parentesi angolari (il resto sono commenti), senza gli spazi degli a
    /// capo dell'intestazione. Il primo https valido (senza nome utente, sulla porta di https, con un nome di sito e non un
    /// numero) e il primo mailto: valido, ridotto a un destinatario, oggetto e testo. http: e tutto il resto non contano.
    /// </summary>
    public static (string? Https, string? Mailto) Links(string? header)
    {
        string? https = null, mailto = null;
        if (header == null || header.Length > MaxHeader) return (null, null);
        int from = 0;
        for (int n = 0; n < MaxLinks; n++)
        {
            int close = header.IndexOf('>', from);
            if (close < 0) break;
            int open = close == 0 ? -1 : header.LastIndexOf('<', close - 1);
            if (open >= from)
            {
                var link = new string(header.AsSpan(open + 1, close - open - 1).ToArray().Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
                if (link.Length is > 0 and <= MaxLink)
                {
                    if (Missiva.Mailto.Is(link)) mailto ??= CleanMailto(link);
                    else if (https == null && Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 &&
                             uri.IsDefaultPort && uri.HostNameType == UriHostNameType.Dns && uri.Host.Contains('.'))
                        https = uri.AbsoluteUri;
                }
            }
            from = close + 1;
        }
        return (https, mailto);
    }

    /// <summary>
    /// Il mailto: di una List-Unsubscribe rifatto con il solo primo destinatario, l'oggetto ("unsubscribe" se manca, come
    /// nell'esempio della RFC 2369) e il testo, accorciati: mai Cc e Ccn scelti dal mittente. Null se non c'è un indirizzo.
    /// </summary>
    static string? CleanMailto(string link)
    {
        var parsed = Missiva.Mailto.Parse(link);
        static bool Valid(string a) =>
            a.Length <= 254 && a.IndexOf('@') is > 0 and var at && at == a.LastIndexOf('@') && a.IndexOf('.', at) > at + 1 && !a.EndsWith('.') &&
            a.All(c => c > ' ' && c < 0x7F && c is not ('<' or '>' or '"' or '(' or ')' or ',' or ';' or ':' or '\\'));
        if (parsed.To.FirstOrDefault(Valid) is not { } to) return null;
        static string Short(string text, int max) => text.Length > max ? text[..max] : text;
        var subject = Short(new string(parsed.Subject.Where(c => !char.IsControl(c)).ToArray()).Trim(), 200);
        var body = Short(new string(parsed.Body.Where(c => c == '\n' || !char.IsControl(c)).ToArray()).Trim(), 2000);
        var clean = $"mailto:{Uri.EscapeDataString(to).Replace("%40", "@")}?subject={Uri.EscapeDataString(subject.Length > 0 ? subject : "unsubscribe")}";
        if (body.Length > 0) clean += "&body=" + Uri.EscapeDataString(body);
        return Uri.TryCreate(clean, UriKind.Absolute, out _) ? clean : null;
    }

    /// <summary>List-Unsubscribe-Post dice "un clic" (RFC 8058): il valore è esattamente List-Unsubscribe=One-Click.</summary>
    public static bool IsOneClick(string? post) =>
        post != null && post.Length < 100 && new string(post.Where(c => !char.IsWhiteSpace(c)).ToArray()).Equals("List-Unsubscribe=One-Click", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Che cosa farebbe "Annulla iscrizione" per questo messaggio, o null se il comando non c'è: senza List-Unsubscribe, nelle
    /// bozze, nella Posta indesiderata, con la fascia rossa (mittente non verificato, busta PEC alterata). Un clic (POST)
    /// solo con List-Unsubscribe-Post, un indirizzo https e il messaggio autenticato per il dominio del mittente con una
    /// firma DKIM; altrimenti il mailto:, altrimenti la pagina https nel browser.
    /// </summary>
    public static UnsubscribeAction? For(MailAccount account, MessageSummary message, MessageBody? body, bool junk)
    {
        if (body?.ListUnsubscribe is not { Length: > 0 } header || junk || message.IsDraft) return null;
        var own = SenderCheck.Own(account, body.AuthenticationResults);
        if (own != null && SenderCheck.Failed(own)) return null;
        if (body.Pec is { } pec && Pec.Band(pec).Tone == "danger") return null;
        var (https, mailto) = Links(header);
        // Con una firma DKIM del dominio del mittente, non con il solo SPF: è l'unica prova che la RFC 8058 accetta (T-12).
        if (https != null && IsOneClick(body.ListUnsubscribePost) && own != null &&
            SenderLogos.DomainOf(message.From.Email) is { } domain && SenderCheck.Signed(own, domain))
            return new(UnsubscribeKind.OneClick, https);
        if (mailto != null) return new(UnsubscribeKind.Mailto, mailto);
        return https != null ? new(UnsubscribeKind.Web, https) : null;
    }

    /// <summary>
    /// L'indirizzo dell'account a cui il messaggio era scritto: uno dei suoi altri indirizzi (un alias), se è il caso, o il
    /// principale. È quello iscritto alla lista: la conferma nomina quello, e il messaggio di annullamento parte da lì, come
    /// una risposta (SendIdentity.For). Prima partiva sempre dall'indirizzo principale, e chi gestisce la lista lo veniva
    /// a sapere (audit dell'11 ottobre, T-11).
    /// </summary>
    public static (SendIdentity? Identity, string Email) Recipient(MailAccount account, MessageSummary message, MessageBody? body)
    {
        var identity = SendIdentity.For(account, message, body);
        return (identity, identity?.Email ?? account.Email);
    }

    /// <summary>La domanda prima di fare qualcosa: che cosa succederà e verso quale sito o indirizzo, con l'indirizzo intero in vista.</summary>
    public static (string Heading, string Body, string Primary) Confirmation(UnsubscribeAction action, string sender, string account)
    {
        var shown = action.Shown;
        return action.Kind switch
        {
            UnsubscribeKind.OneClick => ("Annullare l'iscrizione?",
                $"Missiva chiede al sito {action.Where} di non mandare più a {account} i messaggi di {sender}. La richiesta va all'indirizzo indicato dal mittente:\n{shown}\n\n" +
                "Non si apre nessuna pagina, e i messaggi già arrivati restano dove sono.", "Annulla iscrizione"),
            UnsubscribeKind.Mailto => ("Annullare l'iscrizione?",
                $"{sender} chiede di annullare l'iscrizione con un messaggio a {action.Where}. Missiva lo prepara con destinatario, oggetto e testo: " +
                "parte solo quando premi Invia.", "Prepara il messaggio"),
            _ => ("Annullare l'iscrizione?",
                $"{sender} chiede di annullare l'iscrizione da una pagina del sito {action.Where}. Missiva la apre nel browser:\n{shown}", "Apri nel browser"),
        };
    }

    // ---------------------------------------------------------------- rete

    /// <summary>Oltre questi secondi la richiesta di annullamento si dà per non riuscita.</summary>
    const int RequestSeconds = 15;
    static HttpClient Http = CreateHttp();

#if DEBUG
    /// <summary>Prove (--prova-bozze): la richiesta a un gestore finto, al posto della rete; null rimette quello vero.</summary>
    internal static void UseTestHandler(HttpMessageHandler? handler) =>
        Http = handler == null ? CreateHttp() : SenderLogos.Browser(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(RequestSeconds) });
#endif

    /// <summary>
    /// Come per i loghi (SenderLogos.CreateHandler): senza cookie, senza il proxy di sistema, solo verso indirizzi pubblici
    /// (mai la rete di casa o il PC). In più nessun reindirizzamento seguito, e 15 secondi in tutto.
    /// </summary>
    internal static SocketsHttpHandler CreateHandler()
    {
        var handler = SenderLogos.CreateHandler();
        handler.AllowAutoRedirect = false;
        return handler;
    }

    static HttpClient CreateHttp() => SenderLogos.Browser(new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromSeconds(RequestSeconds) });

    /// <param name="Confirmed">La conferma è stata data: solo allora qualcosa è partito.</param>
    /// <param name="Error">Perché non è riuscito, o null se il sito ha accettato la richiesta.</param>
    public sealed record Outcome(bool Confirmed, string? Error);

    /// <summary>
    /// L'unica strada verso la POST di un clic (RFC 8058): prima la conferma (titolo, testo, pulsante: Confirmation), poi
    /// una sola richiesta https con il corpo List-Unsubscribe=One-Click, senza cookie né credenziali. Riuscito solo con una
    /// risposta 2xx: un reindirizzamento non si segue (né verso http né altrove), e la risposta non si legge.
    /// </summary>
    /// <param name="dryRun">Versione di prova: nessuna richiesta, solo il registro.</param>
    public static async Task<Outcome> OneClickAsync(UnsubscribeAction action, string sender, string account, Func<string, string, string, bool> confirm,
                                                    bool dryRun = false, CancellationToken cancel = default)
    {
        if (action.Kind != UnsubscribeKind.OneClick || !Uri.TryCreate(action.Target, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            return new(false, "Questo messaggio non ha un indirizzo per annullare l'iscrizione con un clic.");
        var (heading, body, primary) = Confirmation(action, sender, account);
        if (!confirm(heading, body, primary)) return new(false, null);
        var host = url.IdnHost;
        if (dryRun)
        {
            Log.Write($"prova: annullerei l'iscrizione con una POST a {host}");
            return new(true, null);
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("List-Unsubscribe=One-Click")) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            int status = (int)response.StatusCode;
            Log.Write($"annulla iscrizione: POST a {host}, risposta {status}");
            if (status is >= 200 and < 300) return new(true, null);
            return new(true, status is >= 300 and < 400
                ? $"Il sito {host} ha risposto rimandando a un'altra pagina (risposta {status}), che Missiva non segue: non si sa se l'iscrizione è stata annullata."
                : $"Il sito {host} non ha accettato la richiesta (risposta {status}).");
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new(true, $"Il sito {host} non ha risposto entro 15 secondi.");
        }
        catch (HttpRequestException ex)
        {
            Log.Write($"annulla iscrizione: POST a {host} non riuscita ({ex.Message})");
            return new(true, $"Non è stato possibile collegarsi al sito {host}.");
        }
    }
}
