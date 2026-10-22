namespace Missiva;

/// <summary>I tipi di account che la finestra di aggiunta propone, come Outlook e Thunderbird partendo dall'indirizzo.</summary>
public enum AccountType { MicrosoftPersonal, Microsoft365, Google, GoogleWorkspace, Imap }

/// <summary>
/// Dall'indirizzo al tipo di account: i domini della posta personale di Google e Microsoft si riconoscono dal nome, gli
/// altri dal loro server di posta (MX): *.mail.protection.outlook.com è Microsoft 365, aspmx.l.google.com (o
/// smtp.google.com) è Google Workspace, il resto un account IMAP. La scelta resta sempre modificabile a mano.
/// </summary>
public static class AccountTypes
{
    static readonly HashSet<string> GoogleDomains = new(StringComparer.OrdinalIgnoreCase) { "gmail.com", "googlemail.com" };

    /// <summary>Outlook.com, Hotmail, Live, MSN: con qualsiasi dominio nazionale (outlook.it, hotmail.co.uk, live.com.br).</summary>
    static readonly HashSet<string> MicrosoftNames = new(StringComparer.OrdinalIgnoreCase) { "outlook", "hotmail", "live", "msn", "passport", "windowslive" };

    public static string DomainOf(string email)
    {
        var address = email.Trim().ToLowerInvariant();
        int at = address.LastIndexOf('@');
        return at >= 0 ? address[(at + 1)..].TrimEnd('.') : "";
    }

    /// <summary>Un indirizzo che ha la forma giusta: qualcosa, una @, un dominio con almeno un punto.</summary>
    public static bool LooksValid(string email)
    {
        var address = email.Trim();
        int at = address.LastIndexOf('@');
        var domain = DomainOf(address);
        return at > 0 && !address.Any(char.IsWhiteSpace) && domain.Contains('.') && !domain.StartsWith('.') && !domain.Contains("..");
    }

    /// <summary>I domini di Gmail (gmail.com, googlemail.com): con un altro dominio, un account Google è di Google Workspace.</summary>
    public static bool IsGmailDomain(string email) => GoogleDomains.Contains(DomainOf(email));

    static bool IsMicrosoftPersonalDomain(string domain)
    {
        var labels = domain.Split('.');
        return MicrosoftNames.Contains(labels[0]) &&
               (labels.Length == 2 || labels.Length == 3 && labels[1] is "co" or "com");
    }

    /// <summary>Il server di posta di Google Workspace, nelle forme di oggi e di prima.</summary>
    static bool IsGoogleMx(string host) =>
        host == "smtp.google.com" || host == "aspmx.l.google.com" || host.EndsWith(".aspmx.l.google.com") || host.EndsWith(".googlemail.com");

    /// <summary>Il tipo di account per l'indirizzo; per i domini propri guarda l'MX (al massimo qualche secondo).</summary>
    public static async Task<AccountType> DetectAsync(string email)
    {
        var domain = DomainOf(email);
        if (GoogleDomains.Contains(domain)) return AccountType.Google;
        if (IsMicrosoftPersonalDomain(domain)) return AccountType.MicrosoftPersonal;
        var mx = await MailDns.MxAsync(domain);
        Log.Write($"account: MX di {domain}: {(mx.Count == 0 ? "nessuno" : string.Join(", ", mx.Select(m => m.Host)))}");
        if (mx.Any(m => m.Host.EndsWith(".mail.protection.outlook.com"))) return AccountType.Microsoft365;
        // I domini propri aggiunti a Outlook.com (Microsoft 365 Family) ricevono con i server della posta personale.
        if (mx.Any(m => m.Host.EndsWith(".olc.protection.outlook.com"))) return AccountType.MicrosoftPersonal;
        if (mx.Any(m => IsGoogleMx(m.Host))) return AccountType.GoogleWorkspace;
        return AccountType.Imap;
    }

    /// <summary>Il tipo di un account già in Missiva.</summary>
    public static AccountType Of(MailAccount account) => account.Kind switch
    {
        AccountKind.Microsoft => account.Tenant == null ? AccountType.MicrosoftPersonal : AccountType.Microsoft365,
        AccountKind.Gmail => IsGmailDomain(account.Email) ? AccountType.Google : AccountType.GoogleWorkspace,
        _ => AccountType.Imap,
    };

    /// <summary>
    /// Un account di lavoro, dove ha senso dire "Esterno": Microsoft 365, Google Workspace, e gli account IMAP su un dominio
    /// proprio (non la posta personale come libero.it, che Settings.ImageKey riconosce).
    /// </summary>
    public static bool IsWork(MailAccount account) => Of(account) switch
    {
        AccountType.Microsoft365 or AccountType.GoogleWorkspace => true,
        AccountType.Imap => !Settings.ImageKey(account.Email).Contains('@'),
        _ => false,
    };

    public static string Name(AccountType type) => type switch
    {
        AccountType.MicrosoftPersonal => T("Microsoft (Outlook.com, Hotmail e Live)"),
        AccountType.Microsoft365 => T("Microsoft 365 (lavoro o scuola)"),
        AccountType.Google => "Gmail",
        AccountType.GoogleWorkspace => "Google Workspace",
        _ => T("Altro account (IMAP e SMTP)"),
    };

    /// <summary>Come lo mostrano le Impostazioni: il tipo e, per gli account di lavoro, l'app e la directory usate.</summary>
    public static string Describe(MailAccount account)
    {
        if (account.SharedOf != null)
            return Store.Settings.Accounts.FirstOrDefault(a => a.Id == account.SharedOf) is { } owner
                ? T("Casella condivisa di Microsoft 365, aperta con {0}", owner.Email)
                : T("Casella condivisa di Microsoft 365");
        return Of(account) switch
        {
            AccountType.MicrosoftPersonal => "Microsoft (Outlook.com)",
            AccountType.Microsoft365 => T("Microsoft 365 (lavoro o scuola)") +
                (account.ClientId != null ? T(", app dell'organizzazione {0}", account.ClientId) : "") +
                (account.Tenant is { } tenant && tenant != MicrosoftAuth.Organizations ? $", directory {tenant}" : ""),
            AccountType.Google => "Gmail",
            AccountType.GoogleWorkspace => "Google Workspace",
            _ => account.Imap is { } server ? $"IMAP ({server.ImapHost})" : "IMAP",
        };
    }
}
