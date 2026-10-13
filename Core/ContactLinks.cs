using System.Text;
using System.Text.RegularExpressions;

namespace Missiva;

/// <summary>
/// I collegamenti della scheda di una persona (Persone): numeri da chiamare (tel:), sito web e Google Contatti. Qui solo
/// testo in entrata e indirizzo in uscita, niente apertura: aprire è di <c>PeopleView.OpenExternal</c>, che accetta solo
/// quello che esce da qui (<see cref="IsAllowed"/>): un tel: con cifre, un indirizzo http o https del sito, o la pagina di
/// Google Contatti. Mai altri schemi, mai argomenti.
/// </summary>
static partial class ContactLinks
{
    public const int MinDigits = 3, MaxDigits = 20;

    /// <summary>
    /// Il numero da chiamare: le cifre, con un "+" solo all'inizio; spazi, trattini, punti, barre e parentesi si scartano.
    /// Null (nessun collegamento, il testo resta com'è) se ci sono lettere o altri segni (un "1-800-FIORI" o un codice come
    /// *21# chiamerebbero un numero diverso da quello scritto), o se le cifre sono meno di 3 o più di 20.
    /// </summary>
    public static string? NormalizePhone(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        var digits = new StringBuilder();
        bool plus = false;
        for (int i = 0; i < t.Length; i++)
        {
            char ch = t[i];
            if (char.IsAsciiDigit(ch)) digits.Append(ch);
            else if (ch == '+' && i == 0) plus = true;
            else if (ch is ' ' or ' ' or '\t' or '-' or '.' or '/' or '(' or ')') continue;
            else return null;
        }
        if (digits.Length < MinDigits || digits.Length > MaxDigits) return null;
        return (plus ? "+" : "") + digits;
    }

    /// <summary>L'indirizzo tel: di un numero scritto come capita (null se non è un numero valido).</summary>
    public static string? TelUri(string? text) => NormalizePhone(text) is { } number ? "tel:" + number : null;

    [GeneratedRegex(@"^people/([A-Za-z0-9-]{1,64})\z")]
    private static partial Regex PersonId();

    [GeneratedRegex(@"^https://contacts\.google\.com/(person/[A-Za-z0-9-]{1,64})?(\?authuser=[A-Za-z0-9._%\-]{1,330})?\z")]
    private static partial Regex GoogleAddress();

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]{1,64}@[A-Za-z0-9.\-]{1,255}\z")]
    private static partial Regex AccountEmail();

    [GeneratedRegex(@"^tel:\+?[0-9]{3,20}\z")]
    private static partial Regex TelAddress();

    /// <summary>
    /// La scheda di una persona di Google in Google Contatti (https://contacts.google.com/person/c123 per "people/c123").
    /// L'unica cosa del contatto che entra nell'indirizzo è l'id, e solo se è fatto di lettere, cifre e trattino; senza id
    /// o con un id strano la pagina di Google Contatti. Con due account Google il browser aprirebbe quello predefinito e non
    /// troverebbe il contatto: con l'indirizzo dell'account di Missiva (non del contatto), se è un indirizzo semplice, si aggiunge
    /// ?authuser=indirizzo, che dice a Google quale account usare.
    /// </summary>
    public static string GoogleContactsUrl(string? resourceName, string? accountEmail = null)
    {
        string url = resourceName != null && PersonId().Match(resourceName) is { Success: true } m ? "https://contacts.google.com/person/" + m.Groups[1].Value : "https://contacts.google.com/";
        return accountEmail != null && AccountEmail().IsMatch(accountEmail) ? url + "?authuser=" + Uri.EscapeDataString(accountEmail) : url;
    }

    /// <summary>
    /// Il sito di un contatto come indirizzo da aprire: solo http e https, con un nome di sito, senza utente e password;
    /// "www.esempio.it" diventa https. Un indirizzo numerico vale solo se è di Internet: quello di un contatto arrivato da
    /// Google, da Outlook o da una vCard non apre il browser sul PC o sulla rete di casa (SenderLogos.IsPublic, come per i
    /// loghi: audit dell'11 ottobre, T-35).
    /// </summary>
    public static string? WebUrl(string? text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > 2048 || t.Any(char.IsWhiteSpace) || t.Any(char.IsControl)) return null;
        if (!t.Contains("://")) t = "https://" + t;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        if (uri.UserInfo.Length > 0 || !uri.Host.Contains('.')) return null;
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 && !(System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && SenderLogos.IsPublic(ip))) return null;
        return uri.AbsoluteUri;
    }

    /// <summary>Se PeopleView.OpenExternal può aprire questo indirizzo: un tel: con cifre, Google Contatti o un sito http o https.</summary>
    public static bool IsAllowed(string? url) =>
        url != null && (TelAddress().IsMatch(url) || GoogleAddress().IsMatch(url) || WebUrl(url) == url);

    /// <summary>La descrizione di un indirizzo per il registro della prova ("chiamerei +39 333 1234567").</summary>
    public static string Describe(string url) =>
        TelAddress().IsMatch(url) ? "chiamerei " + url[4..] : GoogleAddress().IsMatch(url) ? "aprirei Google Contatti: " + url : "aprirei il sito " + url;
}
