using System.Diagnostics;
using System.Windows;

namespace Missiva.UI;

/// <summary>
/// Lavoro senza rete (giro P5, parte 6a), come l'Outlook classico e il nuovo Outlook: Elimina, Archivia, Sposta, i segni di
/// letto e il contrassegno fatti senza rete vanno nella coda delle azioni (ActionQueue, cifrata su disco), la riga cambia
/// subito e sotto l'account compare "N azioni in attesa"; Invia senza rete mette il messaggio nella Posta in uscita (Outbox),
/// con la barra in fondo all'elenco. Al ritorno della rete (il controllo dei nuovi messaggi riuscito, le cartelle rilette,
/// Windows che dice che la rete è cambiata) tutto riparte una volta sola, anche con Missiva nell'area di notifica.
/// </summary>
public partial class MainWindow
{
    /// <summary>Sotto l'account le azioni in coda e i messaggi della Posta in uscita.</summary>
    void UpdateQueued(AccountItem account)
    {
        var id = account.Provider.Account.Id;
        account.Queued = ActionQueue.Count(id) + Outbox.Count(id);
    }

    /// <summary>
    /// Un'azione sulla casella che senza rete va in coda: con l'account già non in linea (Network.KnownOffline) subito, senza
    /// provare il server; altrimenti se la richiesta fallisce per la rete. true fatta, false non riuscita per un altro motivo
    /// (dopo l'avviso, come RunAsync), null messa in coda da <paramref name="queue"/>.
    /// </summary>
    async Task<bool?> RunOrQueueAsync(AccountItem account, string what, Func<Task> action, Action queue)
    {
        var mail = account.Provider.Account;
        // Un .eml aperto in una finestra non sta nella casella: le sue azioni non vanno mai in coda.
        bool queues = account.Provider is not EmlProvider;
        if (queues && Network.KnownOffline(mail))
        {
            queue();
            return null;
        }
        try
        {
            await action();
            return true;
        }
        catch (Exception ex) when (queues && Network.IsOffline(ex))
        {
            Network.Note(mail, ex);
            account.Status = Network.Explain(ex) ?? account.Status;
            Log.Write($"{what}: {mail.Email} non in linea, l'azione va in coda");
            queue();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(what, ex);
            Dialog.Show(this, "Operazione non riuscita", ex.Message);
            return false;
        }
    }

    /// <summary>Le azioni in coda per l'account (ActionQueue), e il conteggio sotto l'account.</summary>
    void Enqueue(AccountItem account, IReadOnlyList<MessageSummary> messages, QueuedKind kind, string targetFolderId = "")
    {
        ActionQueue.Add(account.Provider.Account, messages, kind, targetFolderId);
        UpdateQueued(account);
    }

    /// <summary>Gli account che stanno ripetendo la loro coda: un ritorno della rete alla volta.</summary>
    readonly HashSet<string> replaying = new();
    /// <summary>Per account, il motivo dell'attesa della coda già detto (un accesso da rifare): non lo si ripete a ogni controllo.</summary>
    readonly Dictionary<string, string> queueWaitTold = new();

    /// <summary>
    /// La rete dell'account è tornata: le azioni in coda si ripetono (una volta sola, ActionQueue.ReplayAsync) e la Posta in
    /// uscita parte. Quelle non più possibili (messaggi eliminati o spostati altrove intanto) e quelle rifiutate dal server si
    /// dicono con un avviso, che con Missiva nell'area di notifica è una notifica; quelle che aspettano per un altro motivo
    /// (un accesso da rifare, Microsoft occupata) si dicono una volta, con il motivo vero, e restano in coda.
    /// </summary>
    public async Task ReplayQueueAsync(AccountItem account)
    {
        var id = account.Provider.Account.Id;
        if (only != null || ActionQueue.Count(id) + Outbox.Count(id) == 0 || !replaying.Add(id)) return;
        try
        {
            // In sottofondo: un accesso da rifare non apre la finestra di Microsoft, l'azione aspetta.
            var result = await MicrosoftAuth.InBackgroundAsync(async () =>
            {
                var replay = await ActionQueue.ReplayAsync(account.Provider, () => FoldersOfAsync(account.Provider));
                await Outbox.SendAsync(account.Provider);
                return replay;
            });
            string Others() => result.Done > 0 ? $" Le altre {result.Done} sono fatte." : "";
            static string Sentence(string? text) => text is { Length: > 0 } t ? (t.TrimEnd().EndsWith('.') ? t.TrimEnd() : t.TrimEnd() + ".") : "";
            if (result.Gone > 0)
                Tell("Azioni non più possibili",
                     (result.Gone == 1 ? "Un'azione fatta senza rete non si è potuta ripetere" : $"{result.Gone} azioni fatte senza rete non si sono potute ripetere") +
                     $" su {account.Header}: i messaggi non ci sono più (eliminati o spostati altrove, per esempio dal telefono)." + Others());
            if (result.Failed > 0)
                Tell("Azioni non riuscite",
                     (result.Failed == 1 ? "Un'azione fatta senza rete non è riuscita" : $"{result.Failed} azioni fatte senza rete non sono riuscite") +
                     $" su {account.Header}: {Sentence(result.Refused)} I messaggi restano com'erano sul server." + Others());
            if (result.Waiting > 0 && result.Reason is { } reason)
            {
                if (queueWaitTold.GetValueOrDefault(id) != reason)
                    Tell("Azioni in attesa",
                         (result.Waiting == 1 ? "Un'azione fatta senza rete aspetta ancora" : $"{result.Waiting} azioni fatte senza rete aspettano ancora") +
                         $" su {account.Header}: {Sentence(reason)} Ripartono da sole al prossimo controllo riuscito, o con Riprova." + Others());
                queueWaitTold[id] = reason;
            }
            else if (result.Waiting == 0) queueWaitTold.Remove(id);
            if (result.Done > 0 && folder?.Account == account && !searching) await RefreshListAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"coda delle azioni di {account.Header}", ex);
        }
        finally
        {
            replaying.Remove(id);
            UpdateQueued(account);
        }
    }

    // ---------------------------------------------------------------- Posta in uscita

    /// <summary>Le scritture nella Posta in uscita, dalla più vecchia.</summary>
    static List<ComposeWindow> OutboxWindows() => Application.Current.Windows.OfType<ComposeWindow>().Where(w => w.InOutbox).ToList();

    /// <summary>La Posta in uscita o la coda delle azioni sono cambiate: la barra in fondo all'elenco e i conteggi sotto gli account.</summary>
    void OnOutboxChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnOutboxChanged);
            return;
        }
        foreach (var account in accounts) UpdateQueued(account);
        var waiting = OutboxWindows();
        OutboxText.Text = waiting.Count switch
        {
            0 => "",
            1 => waiting[0].PendingSubject is { Length: > 0 } subject ? $"Posta in uscita: \"{subject}\" partirà quando torna la rete" : "Posta in uscita: il messaggio partirà quando torna la rete",
            var n => $"Posta in uscita: {n} messaggi partiranno quando torna la rete",
        };
        // La Posta in uscita non va su disco: il suggerimento lo dice, perché nessuno creda il messaggio al sicuro dopo un riavvio.
        OutboxText.ToolTip = OutboxText.Text + ". Resta finché Missiva è aperta, anche nell'area di notifica: uscendo da Missiva il messaggio si riapre, " +
                             "da salvare nelle Bozze.";
        OutboxBar.Visibility = waiting.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Riprova, sulla barra: la coda e la Posta in uscita di ogni account ripartono adesso (se la rete manca, restano).</summary>
    async void OnOutboxRetry(object sender, RoutedEventArgs e)
    {
        Log.Write("Posta in uscita: Riprova");
        foreach (var account in accounts.ToList()) await ReplayQueueAsync(account);
    }

    /// <summary>Annulla, sulla barra: l'ultimo messaggio entrato esce dalla Posta in uscita e la sua finestra si riapre.</summary>
    void OnOutboxUndo(object sender, RoutedEventArgs e)
    {
        if (OutboxWindows().LastOrDefault() is { } last) last.LeaveOutbox("Invio annullato: il messaggio non è partito.");
    }

    // ---------------------------------------------------------------- ricerca senza rete

    /// <summary>In MessagePage.Next: i risultati vengono dalla copia sul PC, che non ha altre pagine.</summary>
    const string OfflineResults = "\u0000sul PC";

    /// <summary>
    /// La ricerca sul server o, senza rete (l'account già non in linea, o la richiesta fallita per la rete), nella copia sul
    /// PC: i messaggi aperti e la Posta in arrivo degli ultimi 30 giorni (BodyCache.Search), con Next a OfflineResults.
    /// </summary>
    async Task<MessagePage> SearchOrOfflineAsync(AccountItem account, FolderItem? where, string query, string? next)
    {
        var mail = account.Provider.Account;
        if (next == null && Network.KnownOffline(mail)) return await OfflineSearchAsync(account, where, query);
        // Invio subito dopo i suggerimenti: la stessa ricerca, appena finita o ancora in corso, non si rifà (audit, v1 V-05).
        if (next == null && suggestSearches.Take(SuggestSearches.Key(mail.Id, where?.Folder.Id, query)) is { } asked)
        {
            try
            {
                bool done = asked.IsCompleted;
                var page = await asked;
                Log.Write($"ricerca: riusata quella dei suggerimenti ({(done ? "già finita" : "era in corso")}, {page.Messages.Count} risultati)");
                return page;
            }
            catch (Exception ex)
            {
                Log.Write($"ricerca: quella dei suggerimenti non è riuscita ({ex.GetType().Name}), la rifaccio");
            }
        }
        try
        {
            return await account.Provider.SearchAsync(where?.Folder, query, next);
        }
        catch (Exception ex) when (next == null && Network.IsOffline(ex))
        {
            Network.Note(mail, ex);
            return await OfflineSearchAsync(account, where, query);
        }
    }

    /// <summary>
    /// Le ricerche nella copia sul PC partite per la ricerca di adesso: si fermano, fra un file e l'altro, quando parte
    /// un'altra ricerca o la ricerca si chiude (CancelOfflineSearch). Prima ogni scansione arrivava in fondo su tutti i
    /// processori anche se nessuno la aspettava più (audit, v2 V-21).
    /// </summary>
    CancellationTokenSource offlineSearch = new();

    void CancelOfflineSearch()
    {
        offlineSearch.Cancel();
        offlineSearch = new CancellationTokenSource();
    }

    async Task<MessagePage> OfflineSearchAsync(AccountItem account, FolderItem? where, string query)
    {
        var mail = account.Provider.Account;
        var clock = Stopwatch.StartNew();
        var cancel = offlineSearch.Token;
        try
        {
            var found = await Task.Run(() => BodyCache.Search(mail, query, where?.Folder.Id, 200, cancel));
            Log.Time($"ricerca nella copia sul PC di {mail.Email}, non in linea ({found.Count} risultati)", clock);
            return new MessagePage { Messages = found, Next = OfflineResults };
        }
        catch (OperationCanceledException)
        {
            // Nessuno aspetta più questi risultati (chi li riceve li scarta: listVersion è cambiata).
            Log.Time($"ricerca nella copia sul PC di {mail.Email} fermata: ne è partita un'altra o la ricerca è stata chiusa", clock);
            return new MessagePage { Next = OfflineResults };
        }
    }

    // ---------------------------------------------------------------- ultimi 30 giorni sul PC (parte 6b)

    /// <summary>Ogni quanto si riguarda la Posta in arrivo di un account, e dopo quanto tempo senza usare la finestra si comincia.</summary>
    static readonly TimeSpan OfflinePass = TimeSpan.FromMinutes(30), OfflineIdle = TimeSpan.FromSeconds(30), OfflineRetry = TimeSpan.FromMinutes(5);

    /// <summary>L'ora da segnare come "ultimo giro" dopo un giro non riuscito: il prossimo parte fra OfflineRetry.</summary>
    internal static DateTime DoneAfterError(DateTime now) => now - OfflinePass + OfflineRetry;

    readonly System.Windows.Threading.DispatcherTimer offlineTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    /// <summary>L'ultimo giro finito di ogni account.</summary>
    readonly Dictionary<string, DateTime> offlineDone = new();
    /// <summary>L'ultima volta che si è usata la finestra (mouse, tastiera, rotellina): la copia aspetta che resti ferma.</summary>
    DateTime lastActivity = DateTime.UtcNow;
    /// <summary>Il giro in corso, fermato appena si torna a usare la finestra.</summary>
    CancellationTokenSource? offlineRun;

    /// <summary>
    /// Come l'Outlook classico, che tiene sul PC la posta degli ultimi giorni: i corpi della Posta in arrivo degli ultimi 30
    /// giorni si scaricano in sottofondo nella copia su disco (BodyCache, cifrata), solo con la finestra ferma da 30 secondi
    /// (o nell'area di notifica), un messaggio alla volta sulla connessione in sottofondo di Gmail e IMAP (EXAMINE e BODY.PEEK:
    /// nulla diventa letto) o con le GET di Graph, fino a 180 MB per account. Un giro per account ogni mezz'ora.
    /// </summary>
    void StartOfflineCopy()
    {
        PreviewMouseDown += (_, _) => NoteActivity();
        PreviewKeyDown += (_, _) => NoteActivity();
        PreviewMouseWheel += (_, _) => NoteActivity();
        MessageList.SelectionChanged += (_, _) => NoteActivity(); // anche scelta con UI Automation
        offlineTimer.Tick += async (_, _) =>
        {
            await OfflineCopyAsync();
            CompactWhenIdle();
        };
        offlineTimer.Start();
    }

    /// <summary>
    /// A finestra aperta e ferma .NET non raccoglie (nessuno alloca) e Missiva restava a 236-243 MB privati per ore: ferma da
    /// CompactIdle, una volta per pausa e mai durante la copia degli ultimi 30 giorni, la stessa raccolta compattata di
    /// quando si riduce a icona (da 237 a 191 MB, 13-90 ms). Mouse, tastiera, rotellina o una scelta nell'elenco aprono una
    /// pausa nuova (lastActivity).
    /// </summary>
    static readonly TimeSpan CompactIdle = TimeSpan.FromMinutes(5);
    /// <summary>La pausa (il suo lastActivity) in cui la memoria è già stata compattata.</summary>
    DateTime compactedPause;

    void CompactWhenIdle()
    {
        if (!IsVisible || WindowState == WindowState.Minimized || offlineRun != null || compactedPause == lastActivity
            || DateTime.UtcNow - lastActivity < CompactIdle) return;
        compactedPause = lastActivity;
        var clock = Stopwatch.StartNew();
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        Log.Time($"memoria compattata a finestra ferma (heap .NET {GC.GetGCMemoryInfo().HeapSizeBytes / 1_048_576} MB)", clock);
    }

    void NoteActivity()
    {
        lastActivity = DateTime.UtcNow;
        if (offlineRun is { IsCancellationRequested: false } run)
        {
            run.Cancel(); // si ferma fra un messaggio e l'altro; riprende al prossimo minuto fermo
            Log.Write("copia degli ultimi 30 giorni: ferma, la finestra si sta usando");
        }
    }

    /// <summary>La finestra è ferma: nascosta, ridotta a icona, o senza mouse e tastiera da OfflineIdle.</summary>
    bool WindowIdle => !IsVisible || WindowState == WindowState.Minimized || DateTime.UtcNow - lastActivity >= OfflineIdle;

    async Task OfflineCopyAsync()
    {
        if (offlineRun != null || !WindowIdle) return;
        var due = accounts.FirstOrDefault(a => a.Folders.Any(f => f.Folder.Kind == FolderKind.Inbox) && !Network.KnownOffline(a.Provider.Account)
                                               && a.Provider is not EmlProvider && DateTime.UtcNow - offlineDone.GetValueOrDefault(a.Provider.Account.Id) >= OfflinePass);
        if (due == null) return;
        var run = offlineRun = new CancellationTokenSource();
        try
        {
            // In sottofondo, con l'utente lontano dal PC: un accesso da rifare non apre la finestra di Microsoft.
            int saved = due.Folders.FirstOrDefault(f => f.Folder.Kind == FolderKind.Inbox) is { } inbox
                ? await MicrosoftAuth.InBackgroundAsync(() => OfflineCopy.RunAsync(due.Provider, inbox.Folder, run.Token)) : 0;
            offlineDone[due.Provider.Account.Id] = DateTime.UtcNow;
            // Centinaia di corpi letti e cifrati lasciano la memoria di .NET gonfia (misurato: Missiva da 136 a 240 MB privati):
            // a finestra ferma una raccolta compattata la restituisce a Windows, come nell'area di notifica.
            if (saved > 0) CompactAfterCopy();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Senza rete, o il server non risponde: si riprova fra OfflineRetry, non fra mezz'ora come dopo un giro riuscito
            // (prima un errore qualunque, anche una connessione chiusa all'avvio, rimandava l'account di 30 minuti: audit, A-18).
            offlineDone[due.Provider.Account.Id] = DoneAfterError(DateTime.UtcNow);
            Log.Write($"copia degli ultimi 30 giorni di {due.Header} non fatta: {ex.Message} (di nuovo fra {OfflineRetry.TotalMinutes:0} minuti)");
        }
        finally
        {
            offlineRun = null;
        }
    }

    static void CompactAfterCopy()
    {
        var clock = Stopwatch.StartNew();
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        Log.Time($"memoria compattata dopo la copia degli ultimi 30 giorni (heap .NET {GC.GetGCMemoryInfo().HeapSizeBytes / 1_048_576} MB)", clock);
    }
}
