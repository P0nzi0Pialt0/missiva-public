using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Missiva;

/// <summary>
/// I pezzi del controllo dei nuovi messaggi che non dipendono dalla finestra (App.CheckNewAsync, il risveglio), qui per
/// provarli nella suite (audit di affidabilità del 25 ottobre 2026, arrivo).
/// </summary>
public static class Arrival
{
    /// <summary>
    /// I controlli di più account, tutti insieme: un account lento (una connessione morta dopo la ripresa del PC aspetta il
    /// tempo scaduto di MailKit, fino a due minuti con Gmail) non fa aspettare gli altri. Prima si controllavano in fila, e
    /// ognuno teneva fermi quelli dopo fino a stuck (A-02: dopo la ripresa la notifica del secondo account arrivava dopo
    /// quella del primo, 180 e 240 s). Un controllo che non finisce entro stuck si lascia andare (onStuck), come prima, e
    /// non conta nel totale; quelli che finiscono contano i messaggi trovati (i negativi, "già in corso", come zero).
    /// </summary>
    public static async Task<int> RunAllAsync<T>(IReadOnlyList<T> items, Func<T, Task<int>> check, TimeSpan stuck, Action<T> onStuck)
    {
        var found = await Task.WhenAll(items.Select(async item =>
        {
            var running = check(item);
            if (await Task.WhenAny(running, Task.Delay(stuck)) != running)
            {
                onStuck(item);
                return 0;
            }
            return Math.Max(0, await running);
        }));
        return found.Sum();
    }

    // ---------------------------------------------------------------- i nuovi per UID (A-06)

    /// <summary>
    /// Il mark ("UIDVALIDITY:UID", IMailProvider.GetNewAsync) da tenere fra quello di adesso e uno nuovo: il nuovo se ha
    /// un'altra UIDVALIDITY o un UID più alto, altrimenti quello di adesso. Due controlli dello stesso account possono finire
    /// in un altro ordine (uno fermo oltre StuckCheck e quello partito al suo posto): il mark non torna indietro.
    /// </summary>
    public static string? LaterMark(string? current, string? candidate)
    {
        if (candidate == null) return current;
        if (current?.Split(':') is not [var cv, var cu] || candidate.Split(':') is not [var nv, var nu]) return candidate;
        if (cv != nv) return candidate;
        return uint.TryParse(nu, out var n) && uint.TryParse(cu, out var c) && n < c ? current : candidate;
    }

    // ---------------------------------------------------------------- dopo un avviso dell'IDLE (A-05)

    /// <summary>
    /// Dopo un avviso dell'IDLE il controllo si ripete dopo queste attese finché non ha trovato tanti messaggi quanti il server
    /// ne ha annunciati (KeepLooking). Gmail rende visibile il messaggio alle altre connessioni anche qualche secondo dopo
    /// l'avviso.
    /// </summary>
    public static readonly TimeSpan[] ArrivalRetries = [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>
    /// Se cercare ancora: trovati meno messaggi di quanti l'IDLE ne ha annunciati (almeno uno). Prima si ripeteva solo se il
    /// controllo ne trovava zero: con due messaggi vicini, il primo visibile e il secondo no, il secondo restava al controllo
    /// di riserva (prova a finestra F7d: 78 s).
    /// </summary>
    public static bool KeepLooking(int found, int announced) => found < Math.Max(1, announced);

    /// <summary>
    /// Un arrivo che il controllo non spiega: l'IDLE ha annunciato più messaggi di quanti il controllo ne ha trovati da
    /// annunciare. Sono messaggi entrati nella Posta in arrivo senza essere "nuovi da leggere": già letti altrove (sul
    /// telefono, nei secondi che l'IDLE di Gmail impiega ad avvisare), rimessi in Posta in arrivo con una data vecchia, oltre i
    /// venti di un controllo. Allora la Posta in arrivo aperta si rilegge (App.OnInboxEvent), perché li abbia: prima non entravano
    /// mai nell'elenco aperto (audit di affidabilità del 25 ottobre 2026, elenco, E-02).
    /// </summary>
    public static bool Unexplained(int found, int announced) => announced > 0 && found < announced;

    // ---------------------------------------------------------------- il risveglio (A-04)

    /// <summary>
    /// Le attese fra un tentativo e l'altro del controllo fatto al risveglio (ripresa dalla sospensione, rete cambiata), se
    /// non riesce per la rete: subito dopo la ripresa il Wi-Fi si ricollega in qualche secondo e il DNS non risponde ancora
    /// (nel registro dell'autore "Host sconosciuto" nei primi secondi di 6 riprese su 20, e due volte il controllo partito
    /// allora non l'ha ripetuto nessuno: quei messaggi non sono mai stati annunciati). In tutto circa un minuto, poi il
    /// controllo di riserva.
    /// </summary>
    public static readonly TimeSpan[] WakeRetries = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];

    /// <summary>Un errore della rete (nome non risolto, connessione caduta o rifiutata, tempo scaduto), che fra poco può non esserci più.</summary>
    public static bool Transient(Exception ex) => Network.IsOffline(ex) || ScheduledSending.Transient(ex);

    /// <summary>
    /// Un tentativo, poi altri dopo le attese date finché riesce o finché l'errore non è più di quelli da riprovare. attempt
    /// riceve vero all'ultimo tentativo (chi lo chiama allora dice l'errore come sempre) e restituisce l'errore, o null se
    /// è riuscito. Vero se uno dei tentativi è riuscito.
    /// </summary>
    public static async Task<bool> RetryAsync(Func<bool, Task<Exception?>> attempt, IReadOnlyList<TimeSpan> waits, Func<Exception, bool> retryable, CancellationToken cancel = default)
    {
        for (int i = 0; ; i++)
        {
            bool last = i >= waits.Count;
            var error = await attempt(last);
            if (error == null) return true;
            if (last || !retryable(error)) return false;
            await Task.Delay(waits[i], cancel);
        }
    }

    [DllImport("kernel32.dll")]
    static extern bool QueryUnbiasedInterruptTime(out ulong time);

    /// <summary>
    /// Quanti millisecondi il PC ha passato sospeso da quando Windows è partito: GetTickCount64 (Environment.TickCount64)
    /// conta anche la sospensione, QueryUnbiasedInterruptTime no. I timer di .NET contano con il secondo (sonda A-04), quindi
    /// non vedono la sospensione: questo sì.
    /// </summary>
    public static long SleptMs() => QueryUnbiasedInterruptTime(out var unbiased) ? Environment.TickCount64 - (long)(unbiased / 10_000) : 0;

    /// <summary>
    /// Si accorge che il PC ha dormito guardando SleptMs a ogni giro di un timer: la ripresa la dice Windows
    /// (SystemEvents.PowerModeChanged), ma non sempre a tutti i programmi (con la sospensione moderna i programmi desktop
    /// possono non riceverla); questo la vede comunque, al primo giro del timer dopo la ripresa.
    /// </summary>
    public sealed class SleepClock(Func<long>? slept = null)
    {
        readonly Func<long> slept = slept ?? SleptMs;
        long last = (slept ?? SleptMs)();

        /// <summary>Quanto il PC ha dormito dall'ultima volta che si è guardato (ms); 0 se non ha dormito (sotto 5 s è rumore).</summary>
        public long Check()
        {
            long now = slept(), asleep = now - last;
            last = now;
            return asleep >= 5000 ? asleep : 0;
        }
    }

    /// <summary>
    /// La rete del PC in una riga: per ogni scheda accesa (non quelle di prova di Windows e i tunnel di IPv6) gli indirizzi
    /// IPv4 e i gateway. Windows dice "indirizzi cambiati" (NetworkAddressChanged) anche quando la rete è la stessa (un
    /// indirizzo IPv6 temporaneo nuovo, una scheda virtuale): allora le connessioni sono vive e non si rifanno. Cambia
    /// davvero con un altro Wi-Fi, un cavo, una VPN, la rete che cade o torna.
    /// </summary>
    public static string NetworkPrint()
    {
        try
        {
            return string.Join(";", NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n =>
                {
                    var ip = n.GetIPProperties();
                    var v4 = ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).Order();
                    var gateways = ip.GatewayAddresses.Select(g => g.Address.ToString()).Order();
                    return $"{n.Id}={string.Join(",", v4)}|{string.Join(",", gateways)}";
                })
                .Order());
        }
        catch (NetworkInformationException)
        {
            return "";
        }
    }
}
