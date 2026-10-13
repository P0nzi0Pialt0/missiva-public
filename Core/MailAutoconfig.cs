using System.Xml;
using System.Xml.Linq;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Missiva;

/// <summary>
/// Configurazione automatica di un account IMAP, come Thunderbird, in quest'ordine: autoconfig del dominio
/// (autoconfig.dominio e /.well-known/autoconfig), il database dei provider di Thunderbird (ISPDB), l'ISPDB per il dominio
/// del server di posta (MX), i record SRV (RFC 6186 e 8314), infine imap., mail. e smtp. del dominio provati con un
/// collegamento. Solo HTTPS, e solo server con TLS: una configurazione con socketType plain non si usa.
/// </summary>
public static class MailAutoconfig
{
    /// <summary>I server trovati e da dove vengono (per la finestra di aggiunta e il registro).</summary>
    /// <param name="Foreign">
    /// Server di un altro dominio indicati dal DNS senza garanzie, dai record SRV o dal server di posta (MX) che porta a un
    /// provider del database (una rete ostile può falsificarli): la finestra di aggiunta chiede conferma prima di mandare
    /// la password.
    /// </param>
    public sealed record Found(ImapServer Server, string Source, bool Foreign = false);

    static HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    /// <summary>Un file di configurazione è piccolo: oltre non si legge.</summary>
    const int MaxXml = 256 * 1024;

#if DEBUG
    /// <summary>Prove: le richieste HTTP a un gestore finto, e i collegamenti di prova (imap., smtp.) a una funzione finta.</summary>
    internal static void UseTestHandler(HttpMessageHandler handler) => Http = new HttpClient(handler);
    internal static Func<string, int, MailSecurity, bool, Task<bool>>? TestProbe;
#endif

    public static async Task<Found?> FindAsync(string email, CancellationToken cancel)
    {
        var domain = AccountTypes.DomainOf(email);
        var address = Uri.EscapeDataString(email.Trim());
        foreach (var (url, source) in new[]
        {
            ($"https://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={address}", $"autoconfig.{domain}"),
            ($"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml?emailaddress={address}", $"{domain}/.well-known/autoconfig"),
            ($"https://autoconfig.thunderbird.net/v1.1/{domain}", "database dei provider di Thunderbird"),
        })
        {
            if (await FromXmlAsync(url, email, source, cancel) is { } found) return found;
        }

        // Il dominio del server di posta: mx1.provider.it vale provider.it (con i domini come co.uk, tre livelli). Prima i
        // gestori PEC noti: un dominio proprio con la PEC di Aruba ha l'MX mx.pec.aruba.it, e il database di Thunderbird
        // per aruba.it darebbe i server della posta normale.
        var mx = await MailDns.MxAsync(domain);
        if (Pec(domain, mx.Select(m => m.Host)) is { } pec) return pec with { Server = WithUser(pec.Server, email) };
        if (mx.FirstOrDefault() is { } first && BaseDomain(first.Host) is { } mxDomain && mxDomain != domain &&
            await FromXmlAsync($"https://autoconfig.thunderbird.net/v1.1/{mxDomain}", email, $"database dei provider di Thunderbird ({mxDomain}, dal server di posta)", cancel) is { } byMx)
        {
            // Il dominio del server di posta viene dal DNS, come gli SRV: chi falsifica l'MX sceglie quale provider del
            // database (anche uno che non esiste più, con i server di chi lo ha ricomprato). Fuori dal dominio
            // dell'indirizzo la password parte solo dopo una conferma (audit, S-19).
            bool inDomain = InDomain(byMx.Server.ImapHost, domain) && InDomain(byMx.Server.SmtpHost, domain);
            if (!inDomain) Log.Write($"configurazione automatica: il server di posta di {domain} porta ai server {byMx.Server.ImapHost} e {byMx.Server.SmtpHost}, fuori dal dominio");
            return byMx with { Foreign = !inDomain };
        }

        // Un SRV verso un altro dominio viene dal DNS, che nessuno garantisce: prima i nomi del dominio stesso, provati con
        // un collegamento TLS; se nessuno risponde, l'SRV, da confermare.
        var bySrv = await FromSrvAsync(email, domain);
        if (bySrv is { Foreign: false }) return bySrv;
        return await GuessAsync(email, domain, cancel) ?? bySrv;
    }

    /// <summary>
    /// I gestori di posta certificata noti, riconosciuti dal dominio dell'indirizzo o dal server di posta (MX): i loro server
    /// IMAP e SMTP sono sempre gli stessi, anche per i domini propri. Solo quelli di cui si conoscono i nomi con certezza.
    /// </summary>
    static readonly (string Name, string[] Domains, string[] MxDomains, string Imap, string Smtp)[] PecProviders =
    [
        ("Aruba PEC", ["pec.it", "arubapec.it"], ["pec.aruba.it"], "imaps.pec.aruba.it", "smtps.pec.aruba.it"),
        ("Legalmail (InfoCert)", ["legalmail.it"], ["legalmail.it"], "mbox.cert.legalmail.it", "sendm.cert.legalmail.it"),
    ];

    /// <summary>
    /// Il nome del gestore PEC noto a cui appartiene il dominio (dei suoi indirizzi o dei suoi server): per la posta
    /// certificata, il From della busta (posta-certificata@pec.aruba.it, posta-certificata@legalmail.it). Null se non è noto.
    /// </summary>
    internal static string? PecProviderOf(string domain) =>
        PecProviders.FirstOrDefault(p => p.Domains.Concat(p.MxDomains).Any(d => InDomain(domain, d))).Name;

    /// <summary>Il gestore PEC dell'indirizzo o del suo server di posta, con IMAP 993 e SMTP 465 (SSL/TLS), o null.</summary>
    internal static Found? Pec(string domain, IEnumerable<string> mxHosts)
    {
        foreach (var (name, domains, mxDomains, imap, smtp) in PecProviders)
        {
            bool byDomain = domains.Any(d => InDomain(domain, d));
            bool byMx = !byDomain && mxHosts.Any(h => mxDomains.Any(d => InDomain(h, d)));
            if (!byDomain && !byMx) continue;
            return new Found(new ImapServer { ImapHost = imap, ImapPort = 993, ImapSecurity = MailSecurity.Ssl, SmtpHost = smtp, SmtpPort = 465, SmtpSecurity = MailSecurity.Ssl },
                             $"gestori PEC noti: {name}{(byMx ? ", dal server di posta" : "")}");
        }
        return null;
    }

    static ImapServer WithUser(ImapServer server, string email)
    {
        server.UserName = email.Trim();
        return server;
    }

    /// <summary>Se il server è del dominio (o di un suo sottodominio).</summary>
    public static bool InDomain(string host, string domain)
    {
        host = host.TrimEnd('.').ToLowerInvariant();
        domain = domain.TrimEnd('.').ToLowerInvariant();
        return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
    }

    static string? BaseDomain(string host)
    {
        var labels = host.TrimEnd('.').Split('.');
        if (labels.Length < 2) return null;
        int take = labels.Length > 2 && labels[^1].Length == 2 && labels[^2] is "co" or "com" or "net" or "org" ? 3 : 2;
        return string.Join('.', labels[^take..]).ToLowerInvariant();
    }

    static async Task<Found?> FromXmlAsync(string url, string email, string source, CancellationToken cancel)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            Log.Write($"configurazione automatica: {url.Split('?')[0]} risposta {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxXml) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancel);
            var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancel)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxXml) return null;
            }
            buffer.Position = 0;
            // DTD vietate (audit, S-19): XDocument.Load(Stream) le accetta, con entità che si espandono fino a 10 milioni di
            // caratteri da un file di 256 KB. Un file con un DOCTYPE non è una configurazione di Thunderbird: non si usa.
            using var reader = XmlReader.Create(buffer, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXml, IgnoreWhitespace = true,
            });
            var server = Parse(XDocument.Load(reader), email);
            return server == null ? null : new Found(server, source);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Write($"configurazione automatica: {url.Split('?')[0]} non letta: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Il formato di Thunderbird (config-v1.1.xml): il primo server IMAP e il primo SMTP con SSL o STARTTLS e una password
    /// (non OAuth2), con %EMAILADDRESS%, %EMAILLOCALPART% e %EMAILDOMAIN% al posto dell'indirizzo.
    /// </summary>
    internal static ImapServer? Parse(XDocument xml, string email)
    {
        var address = email.Trim();
        var local = address[..address.LastIndexOf('@')];
        var domain = AccountTypes.DomainOf(address);
        string Fill(string? value) => (value ?? "").Trim().Replace("%EMAILADDRESS%", address).Replace("%EMAILLOCALPART%", local).Replace("%EMAILDOMAIN%", domain);
        static MailSecurity? Security(string? socket) => socket?.Trim().ToUpperInvariant() switch
        {
            "SSL" or "TLS" => MailSecurity.Ssl,
            "STARTTLS" => MailSecurity.StartTls,
            _ => null, // plain: mai
        };
        static bool Password(XElement server) =>
            !server.Elements("authentication").Any() ||
            server.Elements("authentication").Any(a => a.Value.Trim() is "password-cleartext" or "password-encrypted" or "plain" or "secure");
        static (MailSecurity Security, XElement Element)? Pick(IEnumerable<XElement> servers) =>
            servers.Select(s => (Security: Security(s.Element("socketType")?.Value), Element: s))
                   .Where(s => s.Security != null && Password(s.Element) && int.TryParse(s.Element.Element("port")?.Value, out _)
                               && (s.Element.Element("hostname")?.Value.Trim().Length ?? 0) > 0)
                   .OrderBy(s => s.Security == MailSecurity.Ssl ? 0 : 1)
                   .Select(s => ((MailSecurity, XElement)?)(s.Security!.Value, s.Element)).FirstOrDefault();

        var provider = xml.Root?.Element("emailProvider");
        if (provider == null) return null;
        var imap = Pick(provider.Elements("incomingServer").Where(s => (string?)s.Attribute("type") == "imap"));
        var smtp = Pick(provider.Elements("outgoingServer").Where(s => (string?)s.Attribute("type") == "smtp"));
        if (imap is not { } i || smtp is not { } o) return null;
        var user = Fill(i.Element.Element("username")?.Value);
        return new ImapServer
        {
            ImapHost = Fill(i.Element.Element("hostname")!.Value).ToLowerInvariant(),
            ImapPort = int.Parse(i.Element.Element("port")!.Value.Trim()),
            ImapSecurity = i.Security,
            SmtpHost = Fill(o.Element.Element("hostname")!.Value).ToLowerInvariant(),
            SmtpPort = int.Parse(o.Element.Element("port")!.Value.Trim()),
            SmtpSecurity = o.Security,
            UserName = user.Length > 0 ? user : address,
        };
    }

    /// <summary>
    /// RFC 6186 e 8314: _imaps._tcp (SSL) o _imap._tcp (STARTTLS), _submissions._tcp (SSL) o _submission._tcp (STARTTLS).
    /// Un bersaglio "." vuol dire che il servizio non c'è. Un bersaglio fuori dal dominio dell'indirizzo è segnato Foreign.
    /// </summary>
    static async Task<Found?> FromSrvAsync(string email, string domain)
    {
        async Task<(MailDns.Answer Answer, MailSecurity Security)?> FirstAsync(string ssl, string startTls)
        {
            foreach (var (name, security) in new[] { (ssl, MailSecurity.Ssl), (startTls, MailSecurity.StartTls) })
                if ((await MailDns.SrvAsync($"{name}.{domain}")).FirstOrDefault(a => a.Host.Length > 0 && a.Host != "." && a.Port > 0) is { } found)
                    return (found, security);
            return null;
        }
        var imap = await FirstAsync("_imaps._tcp", "_imap._tcp");
        var smtp = await FirstAsync("_submissions._tcp", "_submission._tcp");
        if (imap is not { } i || smtp is not { } o) return null;
        var server = new ImapServer
        {
            ImapHost = i.Answer.Host.TrimEnd('.').ToLowerInvariant(), ImapPort = i.Answer.Port, ImapSecurity = i.Security,
            SmtpHost = o.Answer.Host.TrimEnd('.').ToLowerInvariant(), SmtpPort = o.Answer.Port, SmtpSecurity = o.Security, UserName = email.Trim(),
        };
        if (InDomain(server.ImapHost, domain) && InDomain(server.SmtpHost, domain)) return new Found(server, "record SRV del dominio");
        Log.Write($"configurazione automatica: i record SRV di {domain} indicano {server.ImapHost} e {server.SmtpHost}, fuori dal dominio");
        return new Found(server, "record SRV del dominio, verso un server di un altro dominio", Foreign: true);
    }

    /// <summary>
    /// Come l'ultimo passo di Thunderbird: i nomi più comuni, provati con un collegamento TLS (certificato compreso),
    /// senza mandare la password.
    /// </summary>
    static async Task<Found?> GuessAsync(string email, string domain, CancellationToken cancel)
    {
        (string Host, int Port, MailSecurity Security)? imap = null, smtp = null;
        foreach (var candidate in new[] { ($"imap.{domain}", 993, MailSecurity.Ssl), ($"mail.{domain}", 993, MailSecurity.Ssl), ($"imap.{domain}", 143, MailSecurity.StartTls) })
            if (await ProbeAsync(candidate.Item1, candidate.Item2, candidate.Item3, imap: true, cancel)) { imap = candidate; break; }
        if (imap == null) return null;
        foreach (var candidate in new[] { ($"smtp.{domain}", 465, MailSecurity.Ssl), ($"smtp.{domain}", 587, MailSecurity.StartTls),
                                          ($"mail.{domain}", 465, MailSecurity.Ssl), ($"mail.{domain}", 587, MailSecurity.StartTls) })
            if (await ProbeAsync(candidate.Item1, candidate.Item2, candidate.Item3, imap: false, cancel)) { smtp = candidate; break; }
        if (smtp == null) return null;
        return new Found(new ImapServer
        {
            ImapHost = imap.Value.Host, ImapPort = imap.Value.Port, ImapSecurity = imap.Value.Security,
            SmtpHost = smtp.Value.Host, SmtpPort = smtp.Value.Port, SmtpSecurity = smtp.Value.Security, UserName = email.Trim(),
        }, "nomi comuni del dominio, provati");
    }

    static async Task<bool> ProbeAsync(string host, int port, MailSecurity security, bool imap, CancellationToken cancel)
    {
#if DEBUG
        if (TestProbe is { } fake) return await fake(host, port, security, imap);
#endif
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var options = security == MailSecurity.Ssl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        try
        {
            if (imap)
            {
                using var client = new ImapClient();
                await client.ConnectAsync(host, port, options, timeout.Token);
                await client.DisconnectAsync(true, timeout.Token);
            }
            else
            {
                using var client = new SmtpClient();
                await client.ConnectAsync(host, port, options, timeout.Token);
                await client.DisconnectAsync(true, timeout.Token);
            }
            Log.Write($"configurazione automatica: {host}:{port} risponde");
            return true;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Write($"configurazione automatica: {host}:{port} no ({ex.GetType().Name})");
            return false;
        }
    }
}
