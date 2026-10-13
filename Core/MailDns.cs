using System.Runtime.InteropServices;

namespace Missiva;

/// <summary>
/// I record DNS che servono per la posta: MX (chi riceve la posta del dominio, per riconoscere Microsoft 365 e Google
/// Workspace) e SRV (RFC 6186, i server IMAP e SMTP del dominio). Con DnsQuery_W di Windows, senza pacchetti in più.
/// </summary>
public static class MailDns
{
    /// <summary>Una risposta: il server, la porta (per MX 0) e la priorità (per MX la preferenza), più bassa è meglio.</summary>
    public sealed record Answer(string Host, int Port, int Priority);

    const ushort TypeMx = 15, TypeSrv = 33, TypeTxt = 16;

    /// <summary>Oltre non si aspetta: un dominio che non risponde non deve fermare la finestra di aggiunta.</summary>
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

#if DEBUG
    /// <summary>Prove (Missiva.exe --prova-bozze): le risposte finte per nome e tipo ("MX" o "SRV"), al posto del DNS.</summary>
    internal static Func<string, string, List<Answer>>? TestAnswers;
    /// <summary>Prove: i record TXT finti per nome, al posto del DNS.</summary>
    internal static Func<string, List<string>>? TestTxt;
#endif

    /// <summary>
    /// I record TXT del nome (per esempio default._bimi.dominio), ognuno con le sue stringhe unite; vuoto se non ce ne
    /// sono o se il DNS non risponde.
    /// </summary>
    public static async Task<List<string>> TxtAsync(string name)
    {
#if DEBUG
        if (TestTxt is { } fake) return fake(name);
#endif
        try
        {
            return await Task.Run(() => QueryTxt(name)).WaitAsync(Timeout);
        }
        catch (Exception ex)
        {
            Log.Write($"DNS TXT di {name} non letto: {ex.Message}");
            return new();
        }
    }

    /// <summary>DNS_TXT_DATAW: dwStringCount, poi l'elenco dei puntatori alle stringhe (allineato: a 64 bit 4 byte dopo).</summary>
    static List<string> QueryTxt(string name)
    {
        var result = new List<string>();
        int status = DnsQuery(name, TypeTxt, 0, IntPtr.Zero, out var list, IntPtr.Zero);
        if (status is 9003 or 9501) return result;
        if (status != 0) throw new InvalidOperationException($"errore DNS {status}");
        try
        {
            int data = IntPtr.Size == 8 ? 32 : 24;
            for (var record = list; record != IntPtr.Zero; record = Marshal.ReadIntPtr(record))
            {
                if ((ushort)Marshal.ReadInt16(record, IntPtr.Size * 2) != TypeTxt) continue;
                int count = Marshal.ReadInt32(record, data);
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < Math.Min(count, 64); i++)
                    text.Append(Marshal.PtrToStringUni(Marshal.ReadIntPtr(record, data + IntPtr.Size + i * IntPtr.Size)));
                result.Add(text.ToString());
            }
        }
        finally
        {
            DnsRecordListFree(list, 1);
        }
        return result;
    }

    /// <summary>I server di posta del dominio, dal preferito; vuoto se non ne ha o se il DNS non risponde.</summary>
    public static Task<List<Answer>> MxAsync(string domain) => QueryAsync(domain, TypeMx);

    /// <summary>I record SRV del nome (per esempio _imaps._tcp.dominio), dal preferito.</summary>
    public static Task<List<Answer>> SrvAsync(string name) => QueryAsync(name, TypeSrv);

    static async Task<List<Answer>> QueryAsync(string name, ushort type)
    {
#if DEBUG
        if (TestAnswers is { } fake) return fake(name, type == TypeMx ? "MX" : "SRV");
#endif
        try
        {
            var answers = await Task.Run(() => Query(name, type)).WaitAsync(Timeout);
            return answers.OrderBy(a => a.Priority).ToList();
        }
        catch (Exception ex)
        {
            Log.Write($"DNS {(type == TypeMx ? "MX" : "SRV")} di {name} non letto: {ex.Message}");
            return new();
        }
    }

    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int DnsQuery(string name, ushort type, uint options, IntPtr extra, out IntPtr results, IntPtr reserved);

    [DllImport("dnsapi.dll")]
    static extern void DnsRecordListFree(IntPtr list, int freeType);

    /// <summary>
    /// La struttura DNS_RECORDW: pNext, pName, wType, wDataLength, Flags, dwTtl, dwReserved, poi i dati (a 32 byte
    /// dall'inizio a 64 bit). MX: pNameExchange, wPreference. SRV: pNameTarget, wPriority, wWeight, wPort.
    /// </summary>
    static List<Answer> Query(string name, ushort type)
    {
        var result = new List<Answer>();
        int status = DnsQuery(name, type, 0, IntPtr.Zero, out var list, IntPtr.Zero);
        // 9003: il nome non esiste; 9501: esiste ma senza record di quel tipo. Sono risposte, non errori.
        if (status is 9003 or 9501) return result;
        if (status != 0) throw new InvalidOperationException($"errore DNS {status}");
        try
        {
            int data = IntPtr.Size == 8 ? 32 : 24;
            for (var record = list; record != IntPtr.Zero; record = Marshal.ReadIntPtr(record))
            {
                if ((ushort)Marshal.ReadInt16(record, IntPtr.Size * 2) != type) continue; // CNAME e record aggiunti
                var host = Marshal.PtrToStringUni(Marshal.ReadIntPtr(record, data))?.TrimEnd('.').ToLowerInvariant();
                if (string.IsNullOrEmpty(host)) continue;
                if (type == TypeMx)
                    result.Add(new Answer(host, 0, (ushort)Marshal.ReadInt16(record, data + IntPtr.Size)));
                else
                    result.Add(new Answer(host, (ushort)Marshal.ReadInt16(record, data + IntPtr.Size + 4), (ushort)Marshal.ReadInt16(record, data + IntPtr.Size)));
            }
        }
        finally
        {
            DnsRecordListFree(list, 1); // DnsFreeRecordList
        }
        return result;
    }
}
