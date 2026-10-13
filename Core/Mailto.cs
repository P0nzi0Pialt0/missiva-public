namespace Missiva;

/// <summary>
/// Un link mailto: (RFC 6068), da un'email o da Windows quando Missiva è l'app di posta: destinatari prima del "?", poi
/// to, cc, bcc, subject e body, con la codifica % in UTF-8. Il "+" resta un "+" (come negli indirizzi mario+tag@...):
/// nei mailto: lo spazio è %20.
/// </summary>
public sealed record Mailto(List<string> To, List<string> Cc, List<string> Bcc, string Subject, string Body)
{
    public static bool Is(string? text) => text != null && text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    public static Mailto Parse(string link)
    {
        var rest = Is(link) ? link["mailto:".Length..] : link;
        var question = rest.IndexOf('?');
        var path = question < 0 ? rest : rest[..question];
        List<string> to = Addresses(path), cc = new(), bcc = new();
        string subject = "", body = "";
        if (question >= 0)
        {
            foreach (var pair in rest[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                var value = kv.Length > 1 ? Unescape(kv[1]) : "";
                switch (Unescape(kv[0]).ToLowerInvariant())
                {
                    case "to": to.AddRange(Addresses(kv.Length > 1 ? kv[1] : "")); break;
                    case "cc": cc.AddRange(Addresses(kv.Length > 1 ? kv[1] : "")); break;
                    case "bcc": bcc.AddRange(Addresses(kv.Length > 1 ? kv[1] : "")); break;
                    case "subject": subject = value; break;
                    case "body": body = value; break;
                }
            }
        }
        return new Mailto(to, cc, bcc, subject, body.Replace("\r\n", "\n").Replace('\r', '\n'));
    }

    /// <summary>Indirizzi separati da virgole (anche codificate come %2C).</summary>
    static List<string> Addresses(string encoded) =>
        Unescape(encoded).Split(',', ';').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();

    static string Unescape(string text)
    {
        try { return Uri.UnescapeDataString(text); }
        catch (Exception) { return text; } // una % senza cifre dopo: il testo com'è
    }
}
