using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Missiva;

/// <summary>
/// Un file firmato .p7m (CAdES: le fatture elettroniche dello SdI, i documenti firmati digitalmente): il contenuto estratto
/// dalla busta CMS, i firmatari, la data della firma e com'è la firma (integrità con SignedCms, catena con i
/// certificati di Windows come per la PEC). Anche le buste in base64 e quelle firmate più volte (p7m dentro p7m, o più
/// firme sulla stessa busta): vale la firma peggiore. Solo dati: niente viene eseguito, e l'XML si legge senza DTD per
/// mostrarlo come testo.
/// </summary>
public static class P7m
{
    /// <param name="Signer">Di chi è la firma di cui parla <paramref name="Signature"/>: la peggiore fra tutte, la prima se sono alla pari.</param>
    /// <param name="Signers">Tutti i firmatari, di ogni busta, ognuno una volta: prima quelli della busta di fuori.</param>
    public sealed record Result(byte[] Content, string Name, string Signer, DateTime? SignedAt, SignatureState Signature, int Layers, IReadOnlyList<string> Signers);

    /// <summary>Oltre non si apre (una fattura è di qualche decina di KB, un PDF firmato di qualche MB).</summary>
    public const int MaxSize = 50 * 1024 * 1024;

    public static bool IsP7m(string name) => name.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase);

    /// <summary>La busta aperta; il nome del contenuto è quello del file senza .p7m (FATTURA.xml.p7m diventa FATTURA.xml).</summary>
    public static Result Open(byte[] file, string name)
    {
        if (file.Length > MaxSize) throw new MailException("Il file firmato è troppo grande per aprirlo in Missiva: salvalo e aprilo con il programma di firma.");
        var content = file;
        var signatures = new List<(SignatureState State, string Signer, DateTime? SignedAt)>();
        int layers = 0;
        // Fino a tre buste una dentro l'altra (un documento firmato da più persone in momenti diversi), ognuna con le sue
        // firme parallele (più persone sulla stessa busta).
        while (layers < 3 && Decode(content) is { } cms)
        {
            layers++;
            signatures.AddRange(Check(cms));
            content = cms.ContentInfo.Content;
        }
        if (layers == 0) throw new MailException("Non è un file firmato valido (.p7m): la busta della firma non si legge.");
        var inner = name;
        while (IsP7m(inner)) inner = inner[..^4];
        // Vale la firma peggiore fra tutte, con il suo firmatario; alla pari, la prima (revisione del 13 ottobre 2026, K-06).
        // Prima contava il certificato della sola prima firma di ogni busta, e il nome era quello della busta di fuori: con
        // una firma buona e una no la finestra diceva "firma valida" o no secondo l'ordine nel file.
        var worst = signatures[0];
        foreach (var one in signatures)
            if (Rank(one.State) > Rank(worst.State)) worst = one;
        var signers = signatures.Select(s => s.Signer).Where(s => s.Length > 0).Distinct().ToList();
        return new Result(content, inner.Length > 0 ? inner : "documento", worst.Signer, worst.SignedAt, worst.State, layers, signers);
    }

    /// <summary>Quanto è grave lo stato di una firma: fra più firme vale la più grave (anche per le buste S/MIME: Pec.Verify).</summary>
    internal static int Rank(SignatureState s) => s switch
    {
        SignatureState.Invalid => 4, SignatureState.Unsuitable => 3, SignatureState.Missing => 2, SignatureState.Untrusted => 1, _ => 0,
    };

    /// <summary>
    /// La busta CMS, anche scritta in base64 (con o senza le righe PEM); null se non lo è. Il base64 si prova solo se i byte
    /// non sono già una busta DER: prima si decodificava sempre, anche un PDF firmato da 20 MB (260 ms e 118 MB ogni volta).
    /// </summary>
    static SignedCms? Decode(byte[] data)
    {
        if (TryDecode(data) is { } der) return der;
        return FromBase64(data) is { } decoded ? TryDecode(decoded) : null;
    }

    static SignedCms? TryDecode(byte[] candidate)
    {
        if (candidate is not { Length: > 0 } || candidate[0] != 0x30) return null; // una SEQUENCE DER
        try
        {
            var cms = new SignedCms();
            cms.Decode(candidate);
            if (cms.ContentInfo.Content.Length > 0 || cms.SignerInfos.Count > 0) return cms;
        }
        catch (CryptographicException) { }
        return null;
    }

    /// <summary>Il base64 del file, senza le righe PEM (-----BEGIN ... -----) e gli spazi; null se non lo è. Senza LINQ né stringhe.</summary>
    static byte[]? FromBase64(byte[] data)
    {
        if (data.Length > MaxSize || data.Length < 16) return null;
        var clean = new byte[data.Length];
        int n = 0;
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            // Una riga PEM che comincia con "-----": si salta fino a capo.
            if (b == (byte)'-' && (i == 0 || data[i - 1] is (byte)'\n' or (byte)'\r'))
            {
                while (i < data.Length && data[i] != (byte)'\n') i++;
                continue;
            }
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
            clean[n++] = b;
        }
        var decoded = new byte[Base64.GetMaxDecodedFromUtf8Length(n)];
        return Base64.DecodeFromUtf8(clean.AsSpan(0, n), decoded, out _, out int written) == OperationStatus.Done ? decoded[..written] : null;
    }

    /// <summary>
    /// Le firme di una busta, una per firmatario (le firme parallele): di ognuna l'integrità (CheckSignature solo della
    /// firma), poi la catena del suo certificato. Una busta senza firme conta come una firma che manca.
    /// </summary>
    static List<(SignatureState State, string Signer, DateTime? SignedAt)> Check(SignedCms cms)
    {
        var found = new List<(SignatureState, string, DateTime?)>();
        foreach (var info in cms.SignerInfos)
        {
            var certificate = info.Certificate;
            var signer = certificate?.GetNameInfo(X509NameType.SimpleName, false) ?? "";
            DateTime? when = null;
            foreach (var attribute in info.SignedAttributes)
                if (attribute.Oid.Value == "1.2.840.113549.1.9.5" && attribute.Values.Count > 0) // signingTime
                    when = new Pkcs9SigningTime(attribute.Values[0].RawData).SigningTime.ToLocalTime();
            bool intact = true;
            try
            {
                info.CheckSignature(verifySignatureOnly: true);
            }
            catch (CryptographicException)
            {
                intact = false;
            }
            var state = !intact ? SignatureState.Invalid : certificate != null ? Pec.Check(certificate, cms.Certificates, Pec.CertificateUse.Document) : SignatureState.Untrusted;
            found.Add((state, signer, when));
        }
        if (found.Count == 0) found.Add((SignatureState.Missing, "", null));
        return found;
    }

    /// <summary>Se il contenuto va mostrato come testo: XML (le fatture) e testo semplice.</summary>
    public static bool IsText(Result r) =>
        r.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || r.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || LooksLikeXml(r.Content);

    static bool LooksLikeXml(byte[] content)
    {
        int i = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        while (i < content.Length && i < 64 && char.IsWhiteSpace((char)content[i])) i++;
        return i < content.Length && content[i] == (byte)'<';
    }

    /// <summary>
    /// Il testo da mostrare: l'XML ben rientrato, letto come dati (niente DTD né entità esterne, al massimo 5 milioni di
    /// caratteri); se non si legge, il testo com'è. Mai interpretato: né fogli di stile né script.
    /// </summary>
    public static string Text(Result r)
    {
        if (r.Content.Length > 5_000_000) return Encoding.UTF8.GetString(r.Content, 0, 5_000_000) + "\n…";
        if (LooksLikeXml(r.Content))
        {
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 5_000_000 };
                using var reader = XmlReader.Create(new MemoryStream(r.Content), settings);
                return XDocument.Load(reader).ToString();
            }
            catch (Exception ex) when (ex is XmlException or InvalidOperationException)
            {
                Log.Write($"XML del file firmato non letto come XML: {ex.Message}");
            }
        }
        return new UTF8Encoding(false, false).GetString(r.Content);
    }

    /// <summary>
    /// La firma in parole, per la fascia della finestra. Con più firmatari (firme parallele, o buste una dentro l'altra
    /// firmate da persone diverse) li nomina tutti, e dove una firma non va dice di chi è (revisione del 13 ottobre 2026,
    /// K-06); con un firmatario solo le frasi sono quelle di sempre.
    /// </summary>
    public static (string Text, string Tone) Band(Result r)
    {
        var layers = r.Layers > 1 ? $" ({r.Layers} firme una dentro l'altra)" : "";
        if (r.Signers.Count > 1)
        {
            // Ogni firma ha la sua ora: con più firmatari non se ne dice una sola.
            var all = string.Join(", ", r.Signers.Take(r.Signers.Count - 1)) + " e " + r.Signers[^1];
            var whose = r.Signer.Length > 0 ? r.Signer : "uno dei firmatari";
            return r.Signature switch
            {
                SignatureState.Verified => ($"Firmato da {all}{layers}: firme valide, certificati riconosciuti da Windows.", "ok"),
                SignatureState.Untrusted => ($"Firmato da {all}{layers}: il documento è integro, ma il certificato di {whose} non è fra quelli riconosciuti da Windows " +
                                             "(per i certificati di firma italiani serve l'elenco dell'AgID, come nei programmi di firma).", "warn"),
                SignatureState.Invalid => ($"Firma non valida: il documento è stato modificato dopo la firma. Non fidarti del contenuto. Le firme sono di {all}.", "danger"),
                SignatureState.Unsuitable => ($"Firma non valida: il certificato di {whose} non serve a firmare documenti (per esempio è quello di un sito web). " +
                                              $"Non fidarti del contenuto. Le firme sono di {all}.", "danger"),
                _ => ($"Una delle buste del file non ha una firma. Le altre sono di {all}.", "warn"),
            };
        }
        var when = r.SignedAt is { } w ? $" il {w:dd/MM/yyyy} alle {w:HH:mm}" : "";
        return r.Signature switch
        {
            SignatureState.Verified => ($"Firmato da {r.Signer}{when}{layers}: firma valida, certificato riconosciuto da Windows.", "ok"),
            SignatureState.Untrusted => ($"Firmato da {r.Signer}{when}{layers}: il documento è integro, ma il certificato non è fra quelli riconosciuti da Windows " +
                                         "(per i certificati di firma italiani serve l'elenco dell'AgID, come nei programmi di firma).", "warn"),
            SignatureState.Invalid => ($"Firma non valida: il documento è stato modificato dopo la firma{(r.Signer.Length > 0 ? $" ({r.Signer})" : "")}. Non fidarti del contenuto.", "danger"),
            SignatureState.Unsuitable => ($"Firma non valida: il certificato di {(r.Signer.Length > 0 ? r.Signer : "chi ha firmato")} non serve a firmare documenti " +
                                          "(per esempio è quello di un sito web). Non fidarti del contenuto.", "danger"),
            _ => ("Il file non ha una firma.", "warn"),
        };
    }
}
