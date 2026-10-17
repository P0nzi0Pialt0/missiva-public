using System.Globalization;
using System.Net.Sockets;

namespace Missiva;

/// <summary>Come va uno scaricamento del gestore (DownloadList).</summary>
public enum DownloadState { Waiting, Running, Done, Failed, Canceled }

/// <summary>
/// Uno scaricamento del gestore: un allegato aperto con un programma o salvato, un messaggio salvato come .eml, il
/// documento di un file firmato. È anche l'avanzamento che riceve il provider (i byte arrivati, e quanti il server ne
/// annuncia: IExpectedSize), e porta il suo annullamento (Token), che arriva fino alla richiesta.
/// </summary>
public sealed class DownloadItem : IProgress<long>, IExpectedSize
{
    internal DownloadItem(string name, long? expected, bool temporary, bool waiting)
    {
        Name = name;
        this.expected = expected is > 0 and var size ? size : 0;
        Temporary = temporary;
        State = waiting ? DownloadState.Waiting : DownloadState.Running;
    }

    /// <summary>Il nome del file come si scrive (Safety.SafeFileName, o quello scelto nella finestra Salva con nome).</summary>
    public string Name { get; }
    /// <summary>Aperto con un programma: il file sta nella cartella temporanea di Missiva (Safety.WriteTemp).</summary>
    public bool Temporary { get; }
    /// <summary>L'account del messaggio da cui viene: un .eml salvato si riapre in Missiva come allegato di quell'account.</summary>
    public string? AccountId { get; init; }
    /// <summary>Il messaggio da cui viene stava nella Posta indesiderata: un .eml si riapre come se ci stesse anche lui.</summary>
    public bool FromJunk { get; init; }
    public DateTime Started { get; } = DateTime.Now;

    long expected, received;
    readonly CancellationTokenSource cancel = new();

    /// <summary>I byte attesi: la dimensione dell'allegato, o quella annunciata dal server; null se non si sa (avanzamento indeterminato).</summary>
    public long? Expected => Interlocked.Read(ref expected) is > 0 and var size ? size : null;
    public long Received => Interlocked.Read(ref received);
    public DownloadState State { get; private set; }
    /// <summary>Il motivo, se non è riuscito.</summary>
    public string? Error { get; private set; }
    /// <summary>Il file scritto, a scaricamento finito.</summary>
    public string? Path { get; private set; }
    /// <summary>Il file ha il marchio web (Safety.MarkFromInternet).</summary>
    public bool Marked { get; private set; }
    /// <summary>Un errore già visto nel pannello: l'indicatore non lo segnala più.</summary>
    public bool Seen { get; set; }
    /// <summary>Il file finito non c'è più dov'era (spostato, cancellato, tolto dalla pulizia dei temporanei).</summary>
    public bool Gone { get; set; }

    public bool Active => State is DownloadState.Waiting or DownloadState.Running;
    public CancellationToken Token => cancel.Token;

    /// <summary>Annulla: ferma la richiesta (il token arriva al provider); a un elemento finito non fa nulla.</summary>
    public void Cancel()
    {
        if (Active) cancel.Cancel();
    }

    /// <summary>I byte arrivati, dal thread del provider: mai all'indietro (MailKit conta a parte le intestazioni della parte).</summary>
    public void Report(long value)
    {
        long seen;
        do seen = Interlocked.Read(ref received);
        while (value > seen && Interlocked.CompareExchange(ref received, value, seen) != seen);
    }

    public void Expect(long bytes)
    {
        if (bytes > 0) Interlocked.Exchange(ref expected, bytes);
    }

    /// <summary>La percentuale, se si sa quanto manca: al più 99 finché non è finito.</summary>
    public int? Percent => State == DownloadState.Done ? 100
        : Expected is long total ? (int)Math.Clamp(Received * 100 / total, 0, 99) : null;

    /// <summary>
    /// Lo stato in una riga, per il pannello: "In coda", "1,2 di 2,9 MB, 42%", "Salvato, 2,9 MB", "Aperto, 2,9 MB",
    /// "Non riuscito: motivo", "Annullato", "Il file non c'è più".
    /// </summary>
    public string Status => State switch
    {
        DownloadState.Waiting => "In coda",
        DownloadState.Running when Expected is long total => $"{Progress(Received, total)}, {Percent}%",
        DownloadState.Running => Received > 0 ? SizeText(Received) : "Scaricamento in corso",
        DownloadState.Done when Gone => "Il file non c'è più",
        DownloadState.Done => $"{(Temporary ? "Aperto" : "Salvato")}, {SizeText(Received)}",
        DownloadState.Failed => "Non riuscito: " + Error,
        _ => "Annullato",
    };

    /// <summary>Come le schede degli allegati del riquadro (ReadingPage.SizeText).</summary>
    public static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1048576.0:0.#} MB",
    };

    /// <summary>"1,2 di 2,9 MB", "300 di 820 KB": i due numeri nell'unità del totale.</summary>
    public static string Progress(long done, long total) => total < 1024 * 1024
        ? $"{done / 1024.0:0} di {total / 1024.0:0} KB"
        : $"{done / 1048576.0:0.0} di {total / 1048576.0:0.0} MB";

    internal void Begin() => State = DownloadState.Running;

    /// <summary>Lo scaricamento è finito: i byte arrivati sono quelli veri (l'avanzamento di MailKit per il base64 è una stima).</summary>
    internal void Arrived(long size) => Interlocked.Exchange(ref received, size);

    internal void Finish(string path, bool marked, long size)
    {
        (State, Path, Marked) = (DownloadState.Done, path, marked);
        Interlocked.Exchange(ref received, size);
    }

    internal void Fail(string why) => (State, Error) = (DownloadState.Failed, why);

    internal void Stop() => State = DownloadState.Canceled;
}

/// <summary>
/// Il gestore degli scaricamenti degli allegati (ritocchi del 16 ottobre 2026, richiesta dell'autore: "manca completamente
/// un sistema di gestione dei download degli allegati, non si vede se il download è stato avviato, terminato, né
/// l'avanzamento"), come quello di un browser: gli scaricamenti di questa sessione, i più recenti in cima, al più Max.
/// Contano un allegato aperto con un programma o salvato (Salva con nome, Salva tutti: uno per file), un messaggio salvato
/// come .eml e il "Salva il documento" di un file firmato; non l'anteprima nel riquadro, che si vede già caricare, né un
/// .eml o un .p7m allegati che si aprono in Missiva. Il gestore non scrive i file a modo suo: usa Safety (il marchio web,
/// WriteTemp per i file da aprire, WriteNewAsync per Salva tutti), come prima. Annulla ferma la richiesta, e nessun file
/// nasce prima che lo scaricamento sia finito: un file a metà non c'è mai, né nella cartella scelta né fra i temporanei.
/// L'indicatore e il pannello nella barra del titolo sono di MainWindow.Downloads.cs.
/// </summary>
public sealed class DownloadList
{
    /// <summary>Gli elementi che il pannello tiene: oltre, esce il più vecchio fra quelli finiti.</summary>
    public const int Max = 20;

    /// <summary>Gli scaricamenti di questa sessione di Missiva.</summary>
    public static DownloadList Session { get; } = new();

    readonly List<DownloadItem> items = new();

    /// <summary>Un elemento in più o in meno, o uno che cambia stato (sul thread di chi lo cambia: nell'app quello dell'interfaccia).</summary>
    public event Action? Changed;
    /// <summary>Uno scaricamento finito bene: l'indicatore mostra per qualche secondo il segno di fatto.</summary>
    public event Action<DownloadItem>? Finished;

    /// <summary>Gli elementi, i più recenti in cima.</summary>
    public IReadOnlyList<DownloadItem> Items
    {
        get { lock (items) return items.ToList(); }
    }

    /// <summary>
    /// Un elemento nuovo in cima. Oltre Max esce il più vecchio fra quelli finiti, non riusciti o annullati; quelli in
    /// corso restano sempre (perderebbero Annulla).
    /// </summary>
    public DownloadItem Add(string name, long? expected, bool temporary, bool waiting = false, string? accountId = null, bool fromJunk = false)
    {
        var item = new DownloadItem(name, expected, temporary, waiting) { AccountId = accountId, FromJunk = fromJunk };
        lock (items)
        {
            items.Insert(0, item);
            while (items.Count > Max && items.FindLastIndex(i => !i.Active) is var old and >= 0) items.RemoveAt(old);
        }
        Log.Write($"scaricamenti: {Log.Private(name)}{(waiting ? " in coda" : "")}{(expected is > 0 ? $", {expected} byte attesi" : "")}");
        Changed?.Invoke();
        return item;
    }

    /// <summary>"Cancella l'elenco": escono quelli finiti, non riusciti e annullati; quelli in corso restano. I file restano dove sono.</summary>
    public void Clear()
    {
        lock (items) items.RemoveAll(i => !i.Active);
        Changed?.Invoke();
    }

    /// <summary>
    /// Scarica per un elemento: i byte, o null se è stato annullato o non è riuscito (l'elemento lo dice, con il motivo).
    /// Il provider riceve l'elemento come avanzamento e il suo annullamento.
    /// </summary>
    public async Task<byte[]?> FetchAsync(DownloadItem item, Func<IProgress<long>, CancellationToken, Task<byte[]>> fetch)
    {
        if (item.Token.IsCancellationRequested)
        {
            Stopped(item);
            return null;
        }
        if (item.State == DownloadState.Waiting)
        {
            item.Begin();
            Changed?.Invoke();
        }
        try
        {
            var bytes = await fetch(item, item.Token);
            if (item.Token.IsCancellationRequested)
            {
                Stopped(item);
                return null;
            }
            item.Arrived(bytes.Length);
            return bytes;
        }
        catch (OperationCanceledException) when (item.Token.IsCancellationRequested)
        {
            Stopped(item);
            return null;
        }
        catch (Exception ex)
        {
            Failed(item, ex);
            return null;
        }
    }

    /// <summary>
    /// Salva con nome (un allegato, il messaggio come .eml, il documento di un file firmato): nel file scelto nella finestra
    /// di Windows, che ha già chiesto se sovrascrivere, com'è, con il marchio web (Safety.SaveDownloadedAsync). Il file si
    /// scrive solo a scaricamento finito.
    /// </summary>
    public async Task<DownloadItem> SaveAsync(string path, long? expected, Func<IProgress<long>, CancellationToken, Task<byte[]>> fetch, string? accountId = null, bool fromJunk = false)
    {
        var item = Add(System.IO.Path.GetFileName(path), expected, temporary: false, accountId: accountId, fromJunk: fromJunk);
        if (await FetchAsync(item, fetch) is not { } bytes) return item;
        try
        {
            bool marked = await Safety.SaveDownloadedAsync(path, bytes);
            Done(item, path, marked, bytes.Length);
        }
        catch (Exception ex)
        {
            Failed(item, ex);
        }
        return item;
    }

    /// <summary>Un file da salvare con Salva tutti: il nome sicuro, la dimensione attesa e come scaricarlo.</summary>
    public sealed record SaveAllFile(string Name, long? Expected, Func<IProgress<long>, CancellationToken, Task<byte[]>> Fetch);

    /// <summary>
    /// Salva tutti: un elemento per file, tutti subito in coda (il primo in cima), scaricati uno alla volta e scritti nella
    /// cartella scelta senza mai coprire un file che c'è già (Safety.WriteNewAsync, " (2)"), con il marchio web. Uno annullato
    /// o non riuscito non ferma gli altri.
    /// </summary>
    public async Task<List<DownloadItem>> SaveAllAsync(string folder, IReadOnlyList<SaveAllFile> files, string? accountId = null, bool fromJunk = false)
    {
        var queued = files.Reverse().Select(f => Add(f.Name, f.Expected, temporary: false, waiting: true, accountId: accountId, fromJunk: fromJunk)).Reverse().ToList();
        for (int i = 0; i < files.Count; i++)
        {
            var item = queued[i];
            if (await FetchAsync(item, files[i].Fetch) is not { } bytes) continue;
            try
            {
                var path = await Safety.WriteNewAsync(folder, files[i].Name, bytes);
                Done(item, path, Safety.MarkFromInternet(path), bytes.Length);
            }
            catch (Exception ex)
            {
                Failed(item, ex);
            }
        }
        return queued;
    }

    /// <summary>
    /// Apri con un programma: scaricato, poi scritto in una cartella temporanea nuova con un nome neutro (Safety.WriteTemp,
    /// come prima). Chi chiama fa le domande e lo apre (Opened) o lo butta (NotOpened). Null se annullato o non riuscito.
    /// </summary>
    public async Task<(DownloadItem Item, Safety.TempFile? Temp)> FetchToTempAsync(string name, long? expected, Func<IProgress<long>, CancellationToken, Task<byte[]>> fetch, string? root = null)
    {
        var item = Add(name, expected, temporary: true);
        if (await FetchAsync(item, fetch) is not { } bytes) return (item, null);
        try
        {
            return (item, await Task.Run(() => Safety.WriteTemp(name, bytes, root)));
        }
        catch (Exception ex)
        {
            Failed(item, ex);
            return (item, null);
        }
    }

    /// <summary>Il file temporaneo è stato aperto con il suo programma.</summary>
    public void Opened(DownloadItem item, Safety.TempFile temp) => Done(item, temp.Final, temp.Marked, item.Received);

    /// <summary>Il file temporaneo non si apre (una risposta no alla domanda): via il file, e l'elemento è annullato.</summary>
    public void NotOpened(DownloadItem item, Safety.TempFile temp)
    {
        temp.Discard();
        Stopped(item);
    }

    /// <summary>Non è riuscito dopo lo scaricamento (il programma che doveva aprirlo): l'elemento lo dice.</summary>
    public void Failed(DownloadItem item, Exception ex)
    {
        Log.Error($"scaricamento di {Log.Private(item.Name)}", ex);
        item.Fail(Reason(ex));
        Changed?.Invoke();
    }

    void Done(DownloadItem item, string path, bool marked, long size)
    {
        item.Finish(path, marked, size);
        Log.Write($"scaricamenti: {Log.Private(item.Name)} finito, {size} byte{(marked ? "" : ", senza marchio web")}");
        Changed?.Invoke();
        Finished?.Invoke(item);
    }

    void Stopped(DownloadItem item)
    {
        item.Stop();
        Log.Write($"scaricamenti: {Log.Private(item.Name)} annullato");
        Changed?.Invoke();
    }

    /// <summary>L'avanzamento dell'indicatore: quanti in corso, e la parte fatta di tutti (null se di uno non si sa quanto manca).</summary>
    public (int Active, double? Fraction) Overall()
    {
        var active = Items.Where(i => i.Active).ToList();
        if (active.Count == 0 || active.Any(i => i.Expected == null)) return (active.Count, null);
        long total = active.Sum(i => i.Expected!.Value), done = active.Sum(i => Math.Min(i.Received, i.Expected!.Value));
        return (active.Count, total > 0 ? Math.Min(1, done / (double)total) : null);
    }

    /// <summary>Il motivo di un errore in parole per chi usa Missiva: quello del provider, una connessione caduta, un rifiuto del server.</summary>
    public static string Reason(Exception ex) => ex switch
    {
        MailException mail when mail.Status > 0 => $"{mail.Message.TrimEnd('.')} (il server ha risposto {mail.Status}).",
        MailException mail => mail.Message,
        MailKit.Net.Imap.ImapCommandException refused => $"il server ha rifiutato la richiesta ({refused.ResponseText})",
        UnauthorizedAccessException => "Missiva non può scrivere in quella cartella.",
        IOException { InnerException: SocketException } or SocketException or MailKit.ServiceNotConnectedException or HttpRequestException => "la connessione si è interrotta.",
        _ => ex.Message,
    };
}
