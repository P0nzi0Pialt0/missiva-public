using MailKit;

namespace Missiva;

/// <summary>
/// Un avanzamento (IProgress&lt;long&gt;, i byte arrivati) che vuole sapere anche quanti ne arriveranno, quando il server lo
/// dice: la lunghezza della risposta di Graph, la misura della parte per MailKit. Facoltativo: un provider lo dice solo se
/// l'avanzamento che riceve lo è anche (DownloadItem, ritocchi del 16 ottobre 2026).
/// </summary>
public interface IExpectedSize
{
    void Expect(long bytes);
}

/// <summary>
/// L'avanzamento di MailKit (ITransferProgress, i byte trasferiti di un comando) passato a un IProgress&lt;long&gt; nella
/// misura degli allegati (Attachment.Size): per una parte in base64 i tre quarti dei byte trasferiti, come la dimensione
/// che ImapProvider e GmailProvider danno all'allegato dalla struttura del messaggio. Per una parte MailKit chiede insieme
/// le sue intestazioni (BODY[2.MIME], poche decine di byte) e il contenuto, e dice la misura di ognuno: quella annunciata
/// è la più grande vista.
/// </summary>
sealed class TransferProgress(IProgress<long> progress, double scale) : ITransferProgress
{
    long told;

    /// <summary>Per una parte della struttura: base64 si conta in tre quarti, il resto com'è.</summary>
    public static TransferProgress? For(IProgress<long>? progress, string? encoding) =>
        progress == null ? null : new(progress, "base64".Equals(encoding, StringComparison.OrdinalIgnoreCase) ? 0.75 : 1);

    public void Report(long bytesTransferred, long totalSize)
    {
        long size = (long)(totalSize * scale);
        if (size > told && progress is IExpectedSize expected)
        {
            told = size;
            expected.Expect(size);
        }
        Report(bytesTransferred);
    }

    public void Report(long bytesTransferred) => progress.Report((long)(bytesTransferred * scale));
}
