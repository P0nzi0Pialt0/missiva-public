using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using MimeKit;
using MimeKit.Cryptography;
using X509KeyUsageFlags = System.Security.Cryptography.X509Certificates.X509KeyUsageFlags;

namespace Missiva;

/// <summary>
/// Com'è la firma di un messaggio o di un file: verificata (anche la catena, con i certificati di Windows), con un
/// certificato non riconosciuto, alterata, assente, oppure integra ma con un certificato che non va bene: non serve a
/// firmare posta o documenti (un certificato di un sito web) o non è di chi dice di aver mandato il messaggio.
/// </summary>
public enum SignatureState { Verified, Untrusted, Invalid, Missing, Unsuitable }

/// <summary>
/// La posta certificata (o un messaggio firmato S/MIME), letta dalla busta: che cosa è (Kind: posta-certificata,
/// accettazione, avvenuta-consegna, non-accettazione, errore-consegna, preavviso-errore-consegna, presa-in-carico,
/// rilevazione-virus, anomalia; vuoto per un messaggio solo firmato), chi l'ha mandata, il gestore, quando, a chi è stata
/// consegnata, l'errore, e la firma del gestore (o del mittente): chi ha firmato, il suo indirizzo nel certificato e, se
/// è un gestore PEC che Missiva conosce, il suo nome (Gestore).
/// </summary>
public sealed record PecInfo(string Kind, string Sender, string Subject, string Provider, DateTime? When, List<string> Recipients,
                             string? DeliveredTo, string? Error, string? Reference, SignatureState Signature, string Signer,
                             string SignerEmail = "", string? Gestore = null)
{
    public bool IsPec => Kind.Length > 0;
    /// <summary>Il messaggio non è arrivato: mancata accettazione, mancata consegna, virus.</summary>
    public bool Failed => Kind is "non-accettazione" or "errore-consegna" or "preavviso-errore-consegna" or "rilevazione-virus";
}

/// <summary>
/// Posta certificata, come le webmail dei gestori (Aruba PEC, Legalmail): la busta del gestore (X-Trasporto:
/// posta-certificata, con postacert.eml, daticert.xml e smime.p7s) si apre, e il riquadro mostra il messaggio vero
/// (postacert.eml) con i suoi allegati, sotto una fascia che dice chi l'ha mandato, quando, il gestore e se la sua firma è
/// verificata; le ricevute (X-Ricevuta) si leggono da daticert.xml, la busta di anomalia (X-Trasporto: errore) si dice
/// come tale. La firma S/MIME del gestore si verifica con MimeKit (integrità) e la catena con i certificati di Windows
/// (X509Chain, senza controllo di revoca in rete), per la protezione della posta; poi il certificato deve essere quello
/// del From della busta (posta-certificata@dominio, regole tecniche, appendice B) e il dominio di un gestore noto. Lo
/// stesso per i messaggi firmati S/MIME qualsiasi, con il certificato del mittente. Tutto come dati: XML senza DTD,
/// niente eseguito, il contenuto va al riquadro confinato.
/// </summary>
public static class Pec
{
    /// <summary>Oltre non si legge: una busta PEC con allegati enormi resta com'è.</summary>
    public const int MaxSize = 30 * 1024 * 1024;

#if DEBUG
    /// <summary>Prove: certificati di prova considerati attendibili in aggiunta a quelli di Windows (mai installati nel sistema).</summary>
    internal static X509Certificate2Collection? TestRoots;
#endif

    /// <summary>Un allegato della busta PEC o della firma S/MIME: daticert.xml, o la firma .p7s.</summary>
    public static bool IsSignaturePart(string? name, string contentType) =>
        name != null && (name.Equals("daticert.xml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".p7s", StringComparison.OrdinalIgnoreCase))
        || contentType.Equals("application/pkcs7-signature", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/x-pkcs7-signature", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// I nomi degli allegati come li mostrano l'elenco (le pastiglie), i suggerimenti della ricerca e la scheda della
    /// persona: senza le parti tecniche della busta PEC e della firma S/MIME, come il riquadro (IsSignaturePart: daticert.xml
    /// e le firme .p7s; nella busta di una PEC anche postacert.eml, che il riquadro apre come il messaggio vero). Dal 16
    /// ottobre 2026, scelta dell'autore: prima l'elenco mostrava le pastiglie daticert.xml e smime.p7s. La graffetta resta:
    /// dentro postacert.eml possono esserci allegati veri, e dall'elenco non si vedono.
    /// </summary>
    public static IEnumerable<string> ShownAttachments(IReadOnlyList<string> names)
    {
        bool envelope = names.Any(n => n.Equals("daticert.xml", StringComparison.OrdinalIgnoreCase));
        return names.Where(n => !IsSignaturePart(n, "") && !(envelope && n.Equals("postacert.eml", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Un messaggio da aprire come PEC o come messaggio firmato: la busta ha daticert.xml, o la firma smime.p7s.</summary>
    public static bool Wants(MessageBody body) => body.Pec == null && body.Attachments.Any(a => IsSignaturePart(a.Name, a.ContentType));

    /// <summary>
    /// La busta com'è sul server (per verificare la firma servono i byte esatti), poi aperta in sottofondo. I byte già
    /// arrivati con il corpo (Raw: Gmail e IMAP sotto 300 KB) non si chiedono di nuovo; la busta aperta resta fra le ultime
    /// (Recall), così tornando su una PEC con 10 MB di allegati non si riscaricano.
    /// </summary>
    public static async Task<MessageBody> OpenAsync(IMailProvider provider, MessageSummary summary, MessageBody body, CancellationToken cancel)
    {
        var raw = body.Raw ?? await provider.GetRawAsync(summary, MaxSize, cancel);
        if (raw.Length >= MaxSize) return body;
        var opened = await Task.Run(() => Open(raw, body), cancel);
        if (KeyOf(provider, summary) is { } key) Remember(key, opened);
        return opened;
    }

    // ---------------------------------------------------------------- buste già aperte

    /// <summary>Le ultime buste aperte, dalla meno recente: al massimo OpenedCount, e OpenedBytes di allegati in tutto.</summary>
    static readonly List<(string Key, MessageBody Body, long Bytes)> opened = new();
    const int OpenedCount = 8;
    const long OpenedBytes = 64L * 1024 * 1024;

    /// <summary>Per account, cartella e messaggio; null per gli .eml aperti da un file, che hanno tutti lo stesso identificativo (e sono già in memoria).</summary>
    static string? KeyOf(IMailProvider provider, MessageSummary summary) =>
        provider is EmlProvider ? null : $"{provider.Account.Id}|{summary.FolderId}|{summary.Id}";

    /// <summary>La busta di questo messaggio già aperta, o null.</summary>
    public static MessageBody? Recall(IMailProvider provider, MessageSummary summary)
    {
        if (KeyOf(provider, summary) is not { } key) return null;
        lock (opened)
        {
            int k = opened.FindIndex(o => o.Key == key);
            if (k < 0) return null;
            var found = opened[k];
            opened.RemoveAt(k);
            opened.Add(found);
            return found.Body;
        }
    }

    /// <summary>
    /// Le buste tenute si lasciano andare: chiudendo la finestra nell'area di notifica, dove Missiva resta per giorni e
    /// fino a 64 MB di allegati di buste già lette non servono (audit del 7 ottobre, B-27). Riaperta, una busta si riscarica.
    /// </summary>
    /// <returns>Quante buste e quanti byte teneva.</returns>
    public static (int Count, long Bytes) ForgetOpened()
    {
        lock (opened)
        {
            var held = (opened.Count, opened.Sum(o => o.Bytes));
            opened.Clear();
            return held;
        }
    }

    static void Remember(string key, MessageBody body)
    {
        long bytes = body.Html.Length * 2L + body.Attachments.Sum(a => (long)(a.Content?.Length ?? 0));
        lock (opened)
        {
            opened.RemoveAll(o => o.Key == key);
            opened.Add((key, body, bytes));
            while (opened.Count > 1 && (opened.Count > OpenedCount || opened.Sum(o => o.Bytes) > OpenedBytes)) opened.RemoveAt(0);
        }
    }

    // ---------------------------------------------------------------- la busta

    /// <summary>
    /// La busta aperta: per la posta certificata il corpo e gli allegati di postacert.eml; per le ricevute e le anomalie il
    /// testo del gestore; in ogni caso senza gli allegati tecnici (daticert.xml, smime.p7s, postacert.eml già mostrato), con Pec.
    /// </summary>
    public static MessageBody Open(byte[] raw, MessageBody original)
    {
        var message = MimeMessage.Load(new MemoryStream(raw));
        var (signature, certificate) = Verify(message);
        var transport = message.Headers["X-Trasporto"]?.Trim().ToLowerInvariant();
        var receipt = message.Headers["X-Ricevuta"]?.Trim().ToLowerInvariant();
        var daticert = message.BodyParts.OfType<MimePart>().FirstOrDefault(p => "daticert.xml".Equals(p.FileName, StringComparison.OrdinalIgnoreCase));
        var data = daticert?.Content != null ? ReadDaticert(Bytes(daticert)) : null;
        var postacert = message.BodyParts.OfType<MessagePart>().FirstOrDefault(p => p.Message != null &&
            ("postacert.eml".Equals(p.ContentDisposition?.FileName, StringComparison.OrdinalIgnoreCase) || "postacert.eml".Equals(p.ContentType.Name, StringComparison.OrdinalIgnoreCase)));
        // X-Trasporto: errore è la busta di anomalia (regole tecniche, 6.4.2): un messaggio non certificato (posta ordinaria o
        // busta non valida) arrivato nella casella PEC, allegato com'era; non una mancata consegna.
        var kind = transport == "errore" && receipt == null ? "anomalia" : data?.Kind ?? receipt ?? transport ?? "";
        var from = message.From.Mailboxes.FirstOrDefault();

        // Chi ha firmato. Per la posta certificata il certificato del gestore ha nel subjectAltName l'indirizzo del From della
        // busta, posta-certificata@dominio (regole tecniche, appendice B): un altro certificato, anche riconosciuto da Windows,
        // non è la firma di un gestore. Per un messaggio firmato qualsiasi il certificato deve essere del mittente.
        var emails = certificate != null ? Emails(certificate) : new List<string>();
        var signer = certificate?.GetNameInfo(X509NameType.SimpleName, false) ?? "";
        string? gestore = null;
        var address = from?.Address ?? "";
        if (signature is SignatureState.Verified or SignatureState.Untrusted)
        {
            bool own = address.Length > 0 && emails.Contains(address, StringComparer.OrdinalIgnoreCase);
            if (kind.Length > 0 && !(own && address.StartsWith("posta-certificata@", StringComparison.OrdinalIgnoreCase))) signature = SignatureState.Unsuitable;
            else if (kind.Length == 0 && !own) signature = SignatureState.Unsuitable;
            else if (kind.Length > 0) gestore = GestoreOf(address[(address.IndexOf('@') + 1)..]);
        }
        var subject = data?.Subject ?? message.Subject ?? "";
        if (kind == "anomalia" && subject.StartsWith("ANOMALIA MESSAGGIO:", StringComparison.OrdinalIgnoreCase)) subject = subject["ANOMALIA MESSAGGIO:".Length..].Trim();
        var info = new PecInfo(kind, data?.Sender ?? OnBehalfOf(from) ?? address, subject,
                               data?.Provider ?? "", data?.When, data?.Recipients ?? new(), data?.DeliveredTo, data?.Error,
                               message.Headers["X-Riferimento-Message-ID"]?.Trim(), signature, signer, emails.FirstOrDefault() ?? "", gestore);
        static bool Technical(Attachment a) => IsSignaturePart(a.Name, a.ContentType);
        if (info.Kind == "posta-certificata" && postacert?.Message is { } inner)
        {
            var content = EmlProvider.BodyOf(inner);
            return new MessageBody
            {
                Html = content.Html, To = original.To, Cc = original.Cc, Bcc = original.Bcc, Attachments = content.Attachments,
                AuthenticationResults = original.AuthenticationResults, Invite = content.Invite ?? original.Invite, Pec = info,
                // Le immagini esterne le decide il mittente vero, non il gestore (lo stesso indirizzo per tutti i suoi clienti).
                ImagesFrom = inner.From.Mailboxes.FirstOrDefault()?.Address ?? "",
            };
        }
        return new MessageBody
        {
            Html = original.Html, To = original.To, Cc = original.Cc, Bcc = original.Bcc,
            // La ricevuta completa di consegna e l'anomalia portano anche postacert.eml: il messaggio, che resta da aprire.
            Attachments = original.Attachments.Where(a => !Technical(a)).ToList(),
            AuthenticationResults = original.AuthenticationResults, Invite = original.Invite, Pec = info,
            ImagesFrom = info.IsPec ? "" : null, // la busta di un gestore non ha immagini da consentire
        };
    }

    /// <summary>"Per conto di: mario.rossi@dominio.it" nel nome del From della busta: il mittente originale.</summary>
    static string? OnBehalfOf(MailboxAddress? from)
    {
        const string prefix = "Per conto di:";
        var name = from?.Name?.Trim() ?? "";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name[prefix.Length..].Trim() is { Length: > 0 } who ? who : null;
    }

    /// <summary>Il gestore PEC noto a cui appartiene il dominio del From della busta, o null.</summary>
    internal static string? GestoreOf(string domain)
    {
#if DEBUG
        if (TestRoots != null && MailAutoconfig.InDomain(domain, "pec-prova.it")) return "Gestore PEC di prova";
        if (TestRoots != null && MailAutoconfig.InDomain(domain, "pec-esempio.example")) return "Gestore PEC Esempio"; // le foto della pagina GitHub (ProvaBozze.Foto.cs)
#endif
        return MailAutoconfig.PecProviderOf(domain);
    }

    static byte[] Bytes(MimePart part)
    {
        using var ms = new MemoryStream();
        part.Content!.DecodeTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// La firma S/MIME (multipart/signed) del messaggio: l'integrità con MimeKit, poi il certificato (per firmare la posta) e
    /// la sua catena con quelli di Windows e gli intermedi della firma. Il certificato serve a chi chiama per il firmatario.
    /// Con più firme sulla stessa busta vale la peggiore, con il suo certificato; alla pari, la prima (revisione del 13
    /// ottobre 2026, K-06): prima contava solo la prima, e una firma buona davanti a una con il certificato scaduto dava
    /// "firma verificata". La busta di un gestore PEC ha una firma sola.
    /// </summary>
    static (SignatureState, X509Certificate2?) Verify(MimeMessage message)
    {
        if (message.Body is not MultipartSigned signed) return (SignatureState.Missing, null);
        try
        {
            using var context = new TemporarySecureMimeContext();
            var signatures = signed.Verify(context);
            if (signatures.Count == 0) return (SignatureState.Missing, null);
            X509Certificate2Collection? extra = null;
            (SignatureState State, X509Certificate2? Leaf) worst = default;
            for (int i = 0; i < signatures.Count; i++)
            {
                var signature = signatures[i];
                var leaf = signature.SignerCertificate is SecureMimeDigitalCertificate certificate
                    ? X509CertificateLoader.LoadCertificate(certificate.Certificate.GetEncoded()) : null;
                bool intact;
                try { intact = signature.Verify(true); }
                catch (DigitalSignatureVerifyException) { intact = false; }
                SignatureState state;
                if (!intact) state = SignatureState.Invalid;
                else if (leaf == null) state = SignatureState.Untrusted;
                else
                {
                    if (extra == null)
                    {
                        extra = new X509Certificate2Collection();
                        if (signed.OfType<MimePart>().FirstOrDefault(p => p is ApplicationPkcs7Signature) is { Content: not null } p7s)
                        {
                            var cms = new SignedCms();
                            cms.Decode(Bytes(p7s));
                            extra.AddRange(cms.Certificates);
                        }
                    }
                    state = Check(leaf, extra, CertificateUse.Mail);
                }
                if (i == 0 || P7m.Rank(state) > P7m.Rank(worst.State)) worst = (state, leaf);
            }
            return worst;
        }
        catch (Exception ex)
        {
            Log.Write("firma S/MIME non verificata: " + ex.Message);
            return (SignatureState.Invalid, null);
        }
    }

    /// <summary>A che cosa deve servire il certificato di chi firma: la posta (S/MIME, PEC) o i documenti (.p7m).</summary>
    internal enum CertificateUse { Mail, Document }

    const string EmailProtection = "1.3.6.1.5.5.7.3.4", AnyUsage = "2.5.29.37.0";

    /// <summary>
    /// Scopi (extendedKeyUsage) con cui un certificato firma documenti: protezione della posta, documentSigning (RFC 9336),
    /// firma dei documenti di Microsoft e di Adobe, qualsiasi scopo. Un certificato solo per i siti web (serverAuth) no.
    /// </summary>
    static readonly string[] DocumentUses = [EmailProtection, "1.3.6.1.5.5.7.3.36", "1.3.6.1.4.1.311.10.3.12", "1.2.840.113583.1.1.5", AnyUsage];

    /// <summary>
    /// Il certificato di chi firma: adatto (keyUsage con la firma digitale o il non ripudio; extendedKeyUsage, se c'è, per
    /// la posta o per i documenti), poi la catena fino a una radice di Windows, per la posta con lo scopo protezione della
    /// posta anche negli intermedi (senza controllo di revoca in rete: niente richieste a terzi). Un certificato TLS gratuito,
    /// riconosciuto da Windows, non fa più una firma "verificata".
    /// </summary>
    internal static SignatureState Check(X509Certificate2 leaf, X509Certificate2Collection extra, CertificateUse use)
    {
        if (leaf.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault() is { } usage
            && (usage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0)
            return SignatureState.Unsuitable;
        if (leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault() is { } enhanced)
        {
            string[] wanted = use == CertificateUse.Mail ? [EmailProtection, AnyUsage] : DocumentUses;
            if (!enhanced.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value != null && wanted.Contains(o.Value))) return SignatureState.Unsuitable;
        }
        using var chain = Chain(extra, use);
        if (chain.Build(leaf)) return SignatureState.Verified;
        if (chain.ChainStatus.Any(s => s.Status.HasFlag(X509ChainStatusFlags.NotValidForUsage))) return SignatureState.Unsuitable;
#if DEBUG
        if (TestRoots is { Count: > 0 } roots)
        {
            using var test = Chain(extra, use);
            test.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            test.ChainPolicy.CustomTrustStore.AddRange(roots);
            if (test.Build(leaf)) return SignatureState.Verified;
            if (test.ChainStatus.Any(s => s.Status.HasFlag(X509ChainStatusFlags.NotValidForUsage))) return SignatureState.Unsuitable;
        }
#endif
        return SignatureState.Untrusted;
    }

    static X509Chain Chain(X509Certificate2Collection extra, CertificateUse use)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true; // niente richieste in rete per gli intermedi mancanti
        chain.ChainPolicy.ExtraStore.AddRange(extra);
        if (use == CertificateUse.Mail) chain.ChainPolicy.ApplicationPolicy.Add(new Oid(EmailProtection));
        return chain;
    }

    /// <summary>Gli indirizzi email del certificato: rfc822Name del subjectAltName e, nei certificati vecchi, E= del Subject.</summary>
    internal static List<string> Emails(X509Certificate2 certificate)
    {
        var found = new List<string>();
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != "2.5.29.17") continue;
            try
            {
                var names = new AsnReader(extension.RawData, AsnEncodingRules.BER).ReadSequence();
                var rfc822 = new Asn1Tag(TagClass.ContextSpecific, 1);
                while (names.HasData)
                {
                    if (names.PeekTag().HasSameClassAndValue(rfc822)) found.Add(names.ReadCharacterString(UniversalTagNumber.IA5String, rfc822).Trim());
                    else names.ReadEncodedValue();
                }
            }
            catch (Exception ex) when (ex is AsnContentException or CryptographicException)
            {
                Log.Write("subjectAltName del certificato non letto: " + ex.Message);
            }
        }
        if (certificate.GetNameInfo(X509NameType.EmailName, false) is { Length: > 0 } email && !found.Contains(email, StringComparer.OrdinalIgnoreCase))
            found.Add(email);
        return found;
    }

    sealed record Daticert(string Kind, string Sender, string Subject, string Provider, DateTime? When, List<string> Recipients, string? DeliveredTo, string? Error);

    /// <summary>
    /// daticert.xml (DPR 68/2005): tipo, mittente, destinatari, oggetto, gestore, data e ora, consegna, errore. Letto come
    /// dati: niente DTD né entità esterne, al massimo un milione di caratteri.
    /// </summary>
    static Daticert? ReadDaticert(byte[] xml)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 };
            using var reader = XmlReader.Create(new MemoryStream(xml), settings);
            var root = XDocument.Load(reader).Root;
            if (root == null) return null;
            string Text(string path) => root.XPathValue(path);
            var date = root.Element("dati")?.Element("data");
            DateTime? when = null;
            if (date != null && DateTime.TryParseExact($"{date.Element("giorno")?.Value.Trim()} {date.Element("ora")?.Value.Trim()}", "dd/MM/yyyy HH:mm:ss",
                                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            {
                var zone = (string?)date.Attribute("zona") ?? "";
                when = zone.Length == 5 && int.TryParse(zone[1..3], out var h) && int.TryParse(zone[3..], out var m)
                    ? new DateTimeOffset(at, TimeSpan.FromMinutes((h * 60 + m) * (zone[0] == '-' ? -1 : 1))).LocalDateTime
                    : at;
            }
            var error = Text("dati/errore-esteso");
            return new Daticert(
                ((string?)root.Attribute("tipo") ?? "").Trim().ToLowerInvariant(),
                Text("intestazione/mittente"),
                Text("intestazione/oggetto"),
                Text("dati/gestore-emittente"),
                when,
                root.Element("intestazione")?.Elements("destinatari").Select(d => d.Value.Trim()).Where(d => d.Length > 0).ToList() ?? new(),
                Text("dati/consegna") is { Length: > 0 } delivered ? delivered : null,
                error.Length > 0 ? error : ((string?)root.Attribute("errore") is { Length: > 0 } e && e != "nessuno" ? e : null));
        }
        catch (Exception ex)
        {
            Log.Write("daticert.xml non letto: " + ex.Message);
            return null;
        }
    }

    static string XPathValue(this XElement root, string path)
    {
        XElement? e = root;
        foreach (var name in path.Split('/')) e = e?.Element(name);
        return e?.Value.Trim() ?? "";
    }

    /// <summary>
    /// La fascia sopra il messaggio: il testo e il tono (ok, attenzione, pericolo), come le avvertenze sul mittente. Verde
    /// solo con la firma verificata di un gestore noto (o, per un messaggio firmato, del mittente); rossa se la busta è stata
    /// alterata, se non ha la firma di un gestore (senza firma, o firmata con un altro certificato: una busta finta) o se il
    /// messaggio non è arrivato; gialla quando la firma è integra ma il certificato non si può verificare.
    /// </summary>
    public static (string Text, string Tone) Band(PecInfo pec)
    {
        // Le frasi passano da T (Lingua): una frase intera per chiave, con i pezzi che possono mancare (data, gestore,
        // oggetto, errore) come valori senza parole o con la loro piccola chiave; in italiano il testo è quello di sempre.
        var when = pec.When is { } w ? When(w) : "";
        var provider = pec.Provider.Length > 0 ? T(", gestore {0}", pec.Provider) : "";
        var subject = pec.Subject.Length > 0 ? $" \"{pec.Subject}\"" : "";
        var error = pec.Error != null ? " (" + pec.Error + ")" : "";
        var who = pec.SignerEmail.Length > 0 ? pec.SignerEmail : pec.Signer.Length > 0 ? pec.Signer : T("chi ha firmato");
        if (!pec.IsPec)
            return pec.Signature switch
            {
                SignatureState.Verified => (T("Messaggio firmato da {0}{1}: firma verificata.", pec.Signer, pec.SignerEmail.Length > 0 ? $" ({pec.SignerEmail})" : ""), "ok"),
                SignatureState.Untrusted => (T("Messaggio firmato da {0}: firma integra, ma il certificato non è fra quelli riconosciuti da Windows.", pec.Signer), "warn"),
                SignatureState.Invalid => (T("Messaggio firmato, ma alterato dopo la firma: non fidarti del contenuto."), "danger"),
                SignatureState.Unsuitable => (T("Messaggio firmato con il certificato di {0}, che non è quello del mittente {1} o non serve a firmare la posta: non fidarti del mittente.", who, pec.Sender), "danger"),
                _ => (T("Messaggio firmato."), "warn"),
            };
        // Una busta senza la firma di un gestore non è posta certificata, qualunque cosa dica: le vere sono tutte firmate.
        if (pec.Signature is SignatureState.Unsuitable or SignatureState.Missing)
            return ((pec.Signature == SignatureState.Missing, pec.Sender.Length > 0) switch
            {
                (true, true) => T("Sembra posta certificata di {0}, ma la busta non ha la firma del gestore: non fidarti del mittente né del contenuto.", pec.Sender),
                (true, false) => T("Sembra posta certificata, ma la busta non ha la firma del gestore: non fidarti del mittente né del contenuto."),
                (false, true) => T("Sembra posta certificata di {0}, ma non è firmata da un gestore riconosciuto (il certificato è di {1}): non fidarti del mittente né del contenuto.", pec.Sender, who),
                (false, false) => T("Sembra posta certificata, ma non è firmata da un gestore riconosciuto (il certificato è di {0}): non fidarti del mittente né del contenuto.", who),
            }, "danger");
        var firm = pec.Signature switch
        {
            SignatureState.Verified when pec.Gestore != null => T("firma del gestore verificata ({0})", pec.Gestore),
            SignatureState.Verified => T("firma di {0} verificata, ma {1} non è fra i gestori PEC che Missiva conosce: controlla che sia nell'elenco dei gestori dell'AgID",
                                         who, who.Contains('@') ? who[(who.IndexOf('@') + 1)..] : T("chi ha firmato")),
            SignatureState.Untrusted => T("firma integra, ma il certificato di {0} non è fra quelli riconosciuti da Windows", pec.Signer.Length > 0 ? pec.Signer : T("chi ha firmato")),
            _ => T("la busta è stata alterata dopo la firma del gestore: non fidarti del contenuto"),
        };
        string tone = pec.Signature == SignatureState.Invalid || pec.Failed ? "danger"
            : pec.Signature == SignatureState.Verified && pec.Gestore != null && pec.Kind != "anomalia" ? "ok" : "warn";
        var to = pec.DeliveredTo ?? string.Join(", ", pec.Recipients);
        var text = pec.Kind switch
        {
            "posta-certificata" => T("Posta certificata: inviata da {0}{1}{2}; {3}.", pec.Sender, when, provider, firm),
            "accettazione" => T("Ricevuta di accettazione: il tuo gestore ha accettato il messaggio{0}{1}; {2}.", subject, when, firm),
            "avvenuta-consegna" => T("Ricevuta di consegna: il messaggio{0} è stato consegnato a {1}{2}; {3}.", subject, to, when, firm),
            "presa-in-carico" => T("Presa in carico: il gestore del destinatario ha ricevuto il messaggio{0}{1}; {2}.", subject, when, firm),
            "non-accettazione" => T("Mancata accettazione: il messaggio{0} non è partito{1}; {2}.", subject, error, firm),
            "rilevazione-virus" => T("Virus: il messaggio{0} non è stato consegnato perché contiene un virus; {1}.", subject, firm),
            "errore-consegna" or "preavviso-errore-consegna" => T("Mancata consegna: il messaggio{0} non è stato consegnato a {1}{2}{3}; {4}.", subject, to, error, when, firm),
            "anomalia" when pec.Sender.Length > 0 =>
                T("Anomalia: il messaggio{0} di {1} è arrivato nella casella di posta certificata, ma non è certificato (posta ordinaria o busta non valida): l'originale è allegato, non fidarti del mittente; {2}.", subject, pec.Sender, firm),
            "anomalia" =>
                T("Anomalia: il messaggio{0} è arrivato nella casella di posta certificata, ma non è certificato (posta ordinaria o busta non valida): l'originale è allegato, non fidarti del mittente; {1}.", subject, firm),
            _ => T("Posta certificata ({0}){1}{2}; {3}.", pec.Kind, when, provider, firm),
        };
        return (text, tone);
    }

    /// <summary>
    /// " il 21/10/2026 alle 14:30" dopo un verbo delle fasce (Pec.Band, P7m.Band): in italiano giorno e ora scritti come
    /// sempre (dd/MM/yyyy, HH:mm); in inglese la data e l'ora brevi della cultura di Missiva (Lingua.Culture).
    /// </summary>
    internal static string When(DateTime w) => T(" il {0} alle {1}", Day(w), Time(w));

    /// <summary>
    /// Il giorno di una fascia (questa, P7m e "Non in linea: copia del ..." del riquadro): 21/10/2026 in italiano, come
    /// sempre; in inglese la data breve di Lingua.Culture (21/10/2026 in en-GB, 10/21/2026 in en-US).
    /// </summary>
    internal static string Day(DateTime d) => d.ToString(Lingua.English ? "d" : "dd/MM/yyyy", Lingua.Culture);

    /// <summary>L'ora di una fascia: 14:30 in italiano, come sempre; in inglese l'ora breve di Lingua.Culture (14:30, 2:30 PM).</summary>
    internal static string Time(DateTime d) => d.ToString(Lingua.English ? "t" : "HH:mm", Lingua.Culture);
}
