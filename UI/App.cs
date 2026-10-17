using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;

namespace Missiva.UI;

public sealed class App : Application
{
    readonly Dictionary<string, DateTime> lastCheck = new();
    /// <summary>
    /// I messaggi già annunciati (account e id), gli ultimi 2000: servono solo a non annunciare due volte i messaggi del
    /// minuto di margine fra un controllo e l'altro. Prima un insieme senza tetto, una voce per messaggio arrivato finché
    /// Missiva restava aperta (audit del 7 ottobre, B-26).
    /// </summary>
    readonly RecentKeys notified = new(2000);
    List<IMailProvider> providers = new();
    DispatcherTimer? poll;
    bool checking;
    /// <summary>L'avviso di un errore non gestito dell'interfaccia è già stato dato in questa sessione.</summary>
    bool uiErrorTold;

    /// <summary>
    /// La lettura delle impostazioni partita in Program.Main su un altro thread (LoadSettingsEarly), con l'esito di
    /// un'importazione di una copia di sicurezza in attesa.
    /// </summary>
    static Task<BackupOutcome?>? loading;
    readonly BackupOutcome? imported;

    /// <summary>
    /// Le impostazioni si leggono su un altro thread mentre WPF costruisce l'applicazione (audit del 7 ottobre, A-01): la
    /// lettura (JSON e DPAPI, 34-49 ms) stava sul thread dell'interfaccia dopo il tema Fluent, e con lei aspettavano il
    /// token di Microsoft e la connessione di Gmail. Prima un'importazione di una copia di sicurezza in attesa (Backup), che
    /// si applica prima che Missiva legga impostazioni, contatti e rubrica; l'esito si dice in OnStartup, a finestra aperta.
    /// Nessuno legge Store.Settings prima che il costruttore di App abbia aspettato la fine.
    /// </summary>
    internal static void LoadSettingsEarly() => loading = Task.Run(LoadSettings);

    static BackupOutcome? LoadSettings()
    {
        var imported = Backup.ApplyPending(Store.Dir);
        Store.Load();
        return imported;
    }

    public App()
    {
        Log.Startup("Application costruita");
        imported = loading != null ? loading.GetAwaiter().GetResult() : LoadSettings();
        loading = null;
        Log.Startup("impostazioni lette");
#if DEBUG || PROVA
        // Prove senza rete: i provider falliscono come con un firewall che blocca Missiva (errore 10013), prima di collegarsi.
        // Con --senza-rete (Debug) o MISSIVA_SENZA_RETE=1 (anche la versione finale di prova: le misure dell'avvio, ripetute
        // decine di volte, non aprono ogni volta le connessioni a Gmail).
        Network.TestOffline = Environment.GetCommandLineArgs().Contains("--senza-rete") || Environment.GetEnvironmentVariable("MISSIVA_SENZA_RETE") == "1";
        // --errore-rete 10054: i collegamenti falliscono con quell'errore di Windows, che non è "non in linea" ("Non raggiungibile").
        if (int.TryParse(Environment.GetCommandLineArgs().SkipWhile(a => a != "--errore-rete").Skip(1).FirstOrDefault(), out var networkError))
            (Network.TestOffline, Network.TestOfflineCode) = (true, networkError);
#endif
        // Token e connessioni in sottofondo mentre si caricano il tema e la finestra: le prime richieste a Graph e a Gmail li
        // trovano pronti. Da qui, e non da OnStartup, partono prima del tema Fluent (115-130 ms).
        MicrosoftAuth.WarmUp(Store.Settings.Accounts);
        GmailProvider.Preconnect(Store.Settings.Accounts);
        Log.Startup("riscaldamenti avviati");
        // Fluent di Windows 11, chiaro o scuro come scelto nelle impostazioni (di base come il sistema): assegnato una volta sola.
        ThemeMode = ModeOf(Store.Settings.Theme);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Log.Startup("tema Fluent");
    }

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Startup("OnStartup");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/UI/Styles.xaml") });
        WheelScrolling.Enable();
        Log.Startup("stili");
        // Le impostazioni sono già lette (costruttore, LoadSettingsEarly), dopo un'importazione di una copia di sicurezza in
        // attesa: il suo esito si dice più sotto, a finestra aperta.
        Store.SaveSoonOn(Dispatcher); // i cambi piccoli e frequenti si salvano accorpati, fuori da questo thread (Store.SaveSoon)
        // Un salvataggio delle impostazioni che non riesce (disco pieno, file bloccato): detto una volta per sessione, da
        // qualunque thread arrivi. Con il file bloccato già all'avvio lo dice l'avviso della lettura, più sotto.
        Store.SaveFailed += error => Dispatcher.BeginInvoke(() =>
        {
            if (Store.Recovery is { Blocked: true }) return;
            var body = "Missiva non è riuscita a scrivere il file delle impostazioni: quello che hai appena cambiato può non esserci al prossimo avvio. " +
                       $"Controlla lo spazio sul disco. Missiva riprova da sola al prossimo salvataggio, e in questa sessione non lo ripete.\n\n{error.Message}";
            if (Dialog.Show(MainWindow, "Impostazioni non salvate", body, "Apri la cartella") == Choice.Secondary) OpenDataFolder();
        });
        // Un nuovo accesso a Google che toglie il calendario o i contatti (GoogleAuth.SetScopes): detto, sopra la finestra attiva.
        GoogleAuth.PermissionsLost += (_, notice) => Dispatcher.BeginInvoke(() =>
            Dialog.Show(Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? MainWindow, "Google ha tolto un permesso a Missiva", notice), DispatcherPriority.ApplicationIdle);
        Log.Startup("gestori delle impostazioni");
        AddressBook.Preload();
        Safety.CleanTemp(TimeSpan.FromDays(1)); // allegati rimasti da sessioni precedenti
        MailCache.EncryptOld(); // la copia locale scritta in chiaro dalle versioni di prima
        BodyCache.TrimAll(Store.Settings.Accounts); // corpi su disco più vecchi di 30 giorni
        // Una volta sola dopo l'aggiornamento: i domini che hanno già un messaggio autenticato fra le copie sul PC tengono
        // il logo (SenderLogos.AuthenticateFromCopies), in sottofondo, dopo il primo disegno.
        var logoAccounts = Store.Settings.Accounts.ToList();
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            SenderLogos.AuthenticateFromCopies(logoAccounts);
            // E una volta sola dopo le scelte del 12 ottobre: chi aveva il segno senza DMARC superato lo perde.
            SenderLogos.RecheckFromCopies(logoAccounts);
        });
        Log.Startup("rubrica e temporanei");
        DispatcherUnhandledException += (_, ev) =>
        {
            Log.Error("interfaccia", ev.Exception);
            ev.Handled = true;
            // Prima solo nel registro: un pulsante che non fa nulla, senza che si sappia perché (audit, F-44 punto 5). Ora
            // detto una volta per sessione, quando l'interfaccia è di nuovo ferma, con il testo da copiare e la cartella.
            if (uiErrorTold) return;
            uiErrorTold = true;
            var error = ev.Exception;
            Dispatcher.BeginInvoke(() =>
            {
                var body = "L'ultima operazione può non essere riuscita. Puoi continuare a usare Missiva: se si ripete, il registro (missiva.log) dice che cosa è successo. " +
                           $"In questa sessione l'avviso non si ripete.\n\n{error.GetType().Name}: {error.Message}";
                if (Dialog.Show(MainWindow is { IsVisible: true } w ? w : null, "Missiva ha avuto un errore imprevisto", body, "Apri la cartella") == Choice.Secondary) OpenDataFolder();
            }, DispatcherPriority.ApplicationIdle);
        };
        SessionEnding += (_, ev) =>
        {
            Exiting = true; // la finestra si chiude davvero, non nell'area di notifica
            SaveDraftsBeforeExit($"fine della sessione di Windows ({(ev.ReasonSessionEnding == ReasonSessionEnding.Logoff ? "uscita" : "spegnimento o riavvio")})");
            Store.Flush(); // le impostazioni in attesa di SaveSoon: Windows può chiudere il processo senza passare da OnExit
            SenderLogos.Flush(); // e i domini visti negli ultimi secondi
        };
        // Nell'area di notifica nessuna finestra di Microsoft si apre da sola: un token scaduto accende la barra sotto l'account.
        MicrosoftAuth.Quiet = () => WindowHidden;
        StallLog.Start(Dispatcher); // blocchi oltre 50 ms e fotogrammi oltre 33 ms nel registro, anche nella versione installata
        // Quale Missiva gira, in testa alle righe di questa sessione (AppVersion): a interfaccia ferma, fuori dai tempi dell'avvio.
        _ = Dispatcher.BeginInvoke(() => Log.Write($"Missiva {AppVersion.Text} avviata"), DispatcherPriority.ApplicationIdle);

        ReadingView.ResetDark();
        dark = ReadingView.Dark;
        SetTextColors();
        themeCheck.Tick += (_, _) => ApplyTheme();
        Log.Startup("colori");
        SystemEvents.UserPreferenceChanged += OnUserPreference;
        Log.Startup("SystemEvents");

        // Nascosta nell'area di notifica: avvio con Windows (--nascosta) o Windows che la avvia per un'azione di una
        // notifica (Rispondi, Posponi, Elimina); il clic su una notifica la mostra poi da OnToast.
        // Windows mette -ToastActivated nella riga di comando: lo stesso che guarda WasCurrentProcessToastActivated, senza
        // chiamarla qui (la prima chiamata al Toolkit registra Missiva presso Windows, vedi ListenForToasts).
        var args = Program.Arguments(e.Args); // da un link mailto: solo il link, mai opzioni (T-27)
        startedForNotification = args.Contains("-ToastActivated");
        bool hidden = args.Contains("--nascosta") || startedForNotification;
        // Avviata da Windows per una notifica (che aspetta il gestore per consegnare l'azione) o nascosta: il gestore subito,
        // come prima. Con la finestra, dopo il primo disegno (più sotto).
        if (hidden) ListenForToasts();
        // Nascosta, WPF disegna senza la scheda video: niente dispositivo Direct3D né memoria del driver (misurato: 64 MB
        // privati invece di 116). Alla prima apertura ShowFromTray rimette il disegno normale.
        if (hidden) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var main = new MainWindow();
        MainWindow = main;
#if DEBUG
        DevTools.HideForPhotos(main); // le foto della pagina GitHub (--foto): la finestra non si vede e non prende il fuoco
#endif
        Log.Startup("finestra creata");
        if (hidden)
        {
            WindowHidden = true;
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle(); // senza mostrarla né disegnarla
            Log.Write($"avvio nascosto nell'area di notifica ({(args.Contains("--nascosta") ? "--nascosta" : "azione di una notifica")}): niente finestra né WebView2 finché non si apre");
            // L'icona prima di account, cartelle ed elenco della copia locale (il pezzo sincrono di StartAsync): senza
            // finestra è l'unico segno che Missiva è partita (audit del 7 ottobre, A-14).
            UpdateTray();
            Log.Startup("icona");
            // Nessuno aspetta questo avvio: un suo errore nel registro, subito (con la finestra lo prende il gestore dell'interfaccia).
            _ = Log.GuardAsync("avvio nascosto nell'area di notifica", main.StartAsync);
        }
        else
        {
            main.Show();
            Log.Startup("finestra mostrata");
            // Con la finestra, l'icona dell'area di notifica e il gestore delle notifiche dopo il primo disegno e dopo
            // l'elenco, quando l'interfaccia non ha altro da fare: prima del primo disegno costavano 14-20 e 35-45 ms
            // (audit del 7 ottobre, A-05). Chiudendo la finestra prima, l'icona la mette HideToTray.
            Dispatcher.BeginInvoke(() =>
            {
                UpdateTray();
                Log.Startup("icona");
                ListenForToasts();
            }, DispatcherPriority.Background);
        }
        // Le impostazioni non si leggevano (Store.Read): detto subito, anche avviata nascosta, con dove sono i file (audit,
        // F-44 punto 1).
        if (Store.Recovery is { } recovery)
            Dispatcher.BeginInvoke(() =>
            {
                if (Dialog.Show(main, recovery.Heading, recovery.Body, "Apri la cartella") == Choice.Secondary) OpenDataFolder();
            }, DispatcherPriority.ApplicationIdle);
        if (imported != null)
            Dispatcher.BeginInvoke(() =>
            {
                if (Dialog.Show(main, imported.Heading, imported.Body, "Apri la cartella") == Choice.Secondary) OpenDataFolder();
            }, DispatcherPriority.ApplicationIdle);
        // Testi cifrati delle impostazioni che non si decifrano più (ProtectedTextConverter.Open: impostazioni di un altro PC
        // o di un altro utente di Windows): detto una volta, non a ogni avvio (audit, F-44 punto 4).
        int unreadable = ProtectedTextConverter.UnreadableCount;
        if (unreadable == 0) Store.Settings.UnreadableTold = false;
        else if (!Store.Settings.UnreadableTold)
        {
            Store.Settings.UnreadableTold = true;
            Store.Save();
            Log.Write($"impostazioni: {unreadable} testi cifrati non si decifrano più su questo PC (restano nel file com'erano)");
            Dispatcher.BeginInvoke(() => Dialog.Show(main, "Alcuni dati salvati non si leggono su questo PC", UnreadableNotice(unreadable)), DispatcherPriority.ApplicationIdle);
        }
        if (Store.Settings.StartWithWindows) ApplyStartWithWindows(); // il percorso giusto anche dopo un aggiornamento
        // Avviata da un link mailto: (Missiva app di posta di Windows): il messaggio nuovo appena ci sono gli account.
        if (args.FirstOrDefault(Mailto.Is) is { } link) _ = main.OpenMailtoAsync(link);
        ListenForMailto();
        Log.Startup("canale mailto");
        // Aggiornamenti da GitHub (Updates, solo nei rilasci): il controllo due minuti dopo l'avvio; e, dopo un aggiornamento,
        // com'è andato (una riga nel registro, la voce di App installate, la barra se non è riuscito) e la cartella scaricata
        // che si toglie, in sottofondo, quando l'avvio è finito da un pezzo.
        main.StartUpdateChecks();
        _ = Log.GuardAsync("dopo un aggiornamento", async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            var notice = await Task.Run(() => Updates.AfterStart(Path.Combine(Store.Dir, Updates.FolderName), AppVersion.Number, WriteInstalledVersion));
            if (notice != null) main.ShowUpdateFailure(notice);
        });
#if DEBUG
        StartThemeFlip();
        DevTools.WatchKeys();
        InputProbe.Start(Dispatcher); // MISSIVA_INGRESSO=1: tasti e clic veri durante lo scorrimento (strumenti\ingresso.ps1)
#endif

        // Nuovi messaggi: un controllo ogni due minuti, finché Missiva è aperta (anche nascosta nell'area di notifica).
        poll = new DispatcherTimer { Interval = PollEvery };
#if DEBUG
        // Prove: --controllo 15 controlla ogni 15 secondi, per vederlo nel registro anche a finestra chiusa senza aspettare.
        if (int.TryParse(e.Args.SkipWhile(a => a != "--controllo").Skip(1).FirstOrDefault(), out var every) && every > 0) poll.Interval = TimeSpan.FromSeconds(every);
#endif
        poll.Tick += async (_, _) => await CheckNewAsync(periodic: true);
        poll.Start();
        // Finita una scrittura di Missiva che può cambiare i non letti (letto, eliminato, spostato), il numero si riconta dal
        // server poco dopo, senza aspettare il controllo di ogni due minuti.
        UnreadSync.Written += id => Dispatcher.BeginInvoke(() =>
        {
            if (providers.FirstOrDefault(p => p.Account.Id == id) is { } p) RecountSoon(p);
        });
        // La rete che torna (Wi-Fi ritrovato, cavo, VPN): il controllo subito, e con lui la coda delle azioni fatte senza rete e
        // la Posta in uscita (MainWindow.NoteAccountState), invece di aspettare fino a due minuti.
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        Log.Startup("OnStartup finito");
    }

    bool toastsHeard;

    /// <summary>
    /// Il gestore dei clic sulle notifiche e delle loro azioni. La prima chiamata al Toolkit delle notifiche registra Missiva
    /// presso Windows (attivatore COM, chiavi di registro, icona estratta e salvata su disco): 35-45 ms sul thread
    /// dell'interfaccia, misurati il 7 ottobre 2026 (audit, A-05), che stavano prima del primo disegno. Una volta sola.
    /// </summary>
    void ListenForToasts()
    {
        if (toastsHeard) return;
        toastsHeard = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var input = e.UserInput.ToDictionary(v => v.Key, v => v.Value);
            Dispatcher.BeginInvoke(() => OnToast(e.Argument, input));
        };
        // La riga di comando e il Toolkit devono dire la stessa cosa (OnStartup decide con la riga di comando).
        if (ToastNotificationManagerCompat.WasCurrentProcessToastActivated() != startedForNotification)
            Log.Write($"notifiche: il Toolkit dice avviata da una notifica {!startedForNotification}, la riga di comando {startedForNotification}");
        Log.Time("notifiche: gestore dei clic registrato presso Windows", clock);
        Log.Startup("notifiche: gestore registrato");
    }

    internal static string UnreadableNotice(int count) =>
        $"{(count == 1 ? "Un testo cifrato" : count + " testi cifrati")} delle impostazioni di Missiva (modelli, firme, parole e cartelle delle regole, oggetti dei messaggi " +
        "posposti o contrassegnati, ricerche recenti, mittenti delle immagini, calendari nascosti) non si " + (count == 1 ? "decifra" : "decifrano") + " più: succede quando le impostazioni vengono da un altro PC o da un " +
        "altro utente di Windows.\n\nAl loro posto vedi \"(testo cifrato non leggibile su questo PC)\". Nel file restano com'erano, e sul PC di prima si leggono ancora. " +
        "Le regole di Missiva che li usano restano ferme finché non le correggi.\n\nAnche le password e gli accessi degli account possono non leggersi più: lo dice la barra " +
        "sotto l'account, con Aggiorna password o Accedi di nuovo.";

    /// <summary>La cartella dei dati di Missiva in Esplora file (impostazioni, copie messe da parte, registri).</summary>
    public static void OpenDataFolder()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Store.Dir) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("apertura della cartella dei dati", ex); }
    }

    /// <summary>Un controllo già in programma dopo un cambio della rete (Windows ne manda diversi di fila).</summary>
    bool networkCheckPending;

    void OnNetworkChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(async () =>
    {
        if (networkCheckPending) return;
        networkCheckPending = true;
        await Task.Delay(TimeSpan.FromSeconds(5)); // la rete appena cambiata non ha ancora indirizzo e DNS
        networkCheckPending = false;
        OnNetworkBack();
    });

    /// <summary>
    /// La rete è cambiata (Windows lo dice; nelle prove il comando "rete tornata"): gli IDLE caduti riprovano subito invece
    /// di aspettare la loro attesa cresciuta (InboxIdle.RetryNow), i promemoria del calendario non letti si rileggono, e il
    /// controllo dei nuovi messaggi parte se un account è non in linea, se le sue cartelle non si erano lette (anche per un
    /// errore che non è "non in linea") o se ci sono azioni in coda o Posta in uscita. Se un controllo è già in corso (fermo
    /// su un account che non risponde, fino a due minuti l'uno) ne parte un altro appena finisce: prima il cambio della
    /// rete in quel momento andava perso (audit del 7 ottobre, A-15, A-16, A-17, A-19).
    /// </summary>
    internal async void OnNetworkBack()
    {
        InboxIdle.RetryNow();
        var main = MainWindow as MainWindow;
        main?.RetryCalendarSoon();
        if (!providers.Any(p => Network.KnownOffline(p.Account) || ActionQueue.Count(p.Account.Id) + Outbox.Count(p.Account.Id) > 0) && main?.AnyFoldersFailed != true) return;
        if (checking)
        {
            checkAfterThis = true;
            Log.Write("rete cambiata durante un controllo dei nuovi messaggi: ne parte un altro appena finisce");
            return;
        }
        Log.Write("rete cambiata: controllo subito (account non in linea, cartelle non lette, azioni in coda o Posta in uscita)");
        try { await CheckNewAsync(); }
        catch (Exception ex) { Log.Error("controllo dopo il cambio della rete", ex); }
    }

    /// <summary>La rete è cambiata mentre un controllo dei nuovi messaggi era in corso: finito quello, un altro (OnNetworkBack).</summary>
    bool checkAfterThis;
    /// <summary>Per account, l'ultimo tentativo di rileggere i contatti di Outlook dopo un controllo riuscito (CheckAccountAsync).</summary>
    readonly Dictionary<string, DateTime> contactsTried = new();

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        tray = null;
        SystemEvents.UserPreferenceChanged -= OnUserPreference;
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        Safety.CleanTemp(TimeSpan.Zero); // allegati aperti in questa sessione
        AddressBook.Save();
        SenderLogos.Flush(); // i domini visti negli ultimi secondi, in attesa del loro salvataggio
        Store.Flush(); // le impostazioni in attesa di SaveSoon (un gruppo chiuso, lo zoom, una ricerca fatti un attimo prima di uscire)
        base.OnExit(e);
        // Aggiornamento confermato (UpdateOnExit): il nuovo eseguibile aspetta che questo processo sia uscito e prende il posto
        // dell'eseguibile che gira, poi riapre Missiva (Updates.HandOver).
        if (updateOnExit is { } update)
        {
            try
            {
                if (Environment.ProcessPath is { } installed)
                    Updates.HandOver(update, AppVersion.Number, installed, Environment.ProcessId, Path.Combine(Store.Dir, Updates.FolderName));
            }
            catch (Exception ex)
            {
                Log.Error("passaggio al nuovo eseguibile", ex);
            }
            return;
        }
        if (!restartAfterExit) return;
        // Riavvio chiesto dopo l'importazione di una copia di sicurezza: la copia nuova aspetta che questa sia uscita (--riavvio).
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--riavvio") { UseShellExecute = false }); }
        catch (Exception ex) { Log.Error("riavvio di Missiva", ex); }
    }

    bool restartAfterExit;

    /// <summary>
    /// Chiude Missiva e la riapre (Riavvia ora, dopo l'importazione di una copia di sicurezza): l'uscita è quella di Esci,
    /// con le domande delle scritture aperte; se una viene annullata Missiva resta e non si riavvia.
    /// </summary>
    public void Restart()
    {
        Log.Write("riavvio di Missiva chiesto dopo l'importazione di una copia di sicurezza");
        restartAfterExit = true;
        ExitAll();
    }

    /// <summary>L'aggiornamento scaricato, controllato e confermato (MainWindow.Updates.cs): all'uscita passa la mano al nuovo eseguibile.</summary>
    Updates.Prepared? updateOnExit;

    /// <summary>
    /// Aggiorna, dopo la conferma: Missiva esce come con Esci, con le domande delle scritture aperte; se una viene annullata
    /// resta aperta e non si aggiorna (CancelExit). Mai nella versione di prova, dove Aggiorna scrive solo nel registro.
    /// </summary>
    public void UpdateOnExit(Updates.Prepared prepared)
    {
        Log.Write($"aggiornamento a {prepared.Manifest.Number} confermato: Missiva esce come con Esci");
        updateOnExit = prepared;
        ExitAll();
    }

    // ---------------------------------------------------------------- area di notifica

    TrayIcon? tray;
    int trayCount;
    IconNumberKind trayKind;

    /// <summary>La finestra principale è nascosta nell'area di notifica (letta anche fuori dal thread dell'interfaccia).</summary>
    public static volatile bool WindowHidden;

    /// <summary>Missiva sta uscendo davvero (Esci, "esci" sul canale, fine della sessione): chiudere la finestra non la nasconde.</summary>
    public bool Exiting { get; private set; }

    /// <summary>
    /// L'icona c'è quando chiudendo Missiva resta nell'area di notifica (Impostazioni, Generali, Avvio e chiusura), e
    /// comunque mentre la finestra è nascosta (avvio con Windows con l'opzione spenta), altrimenti non la si riaprirebbe.
    /// </summary>
    public void UpdateTray()
    {
        bool want = !Exiting && (Store.Settings.KeepInTray || WindowHidden);
        if (want == (tray != null)) return;
        if (!want)
        {
            tray!.Dispose();
            tray = null;
            return;
        }
        try
        {
#if DEBUG || PROVA
            tray = new TrayIcon("Missiva (prova)");
#else
            tray = new TrayIcon("Missiva");
#endif
            tray.OpenRequested += () => (MainWindow as MainWindow)?.ShowFromTray();
            tray.NewMessageRequested += () => (MainWindow as MainWindow)?.NewFromTray();
            tray.ExitRequested += ExitAll;
            UpdateTrayTip(trayKind, trayCount);
            Log.Write("icona nell'area di notifica");
        }
        catch (Exception ex)
        {
            Log.Error("icona nell'area di notifica", ex);
        }
    }

    /// <summary>
    /// Nel suggerimento dell'icona lo stesso numero dell'icona nella barra delle applicazioni, con la stessa regola
    /// (IconCount: da leggere, nuovi o nessuno, per gli account scelti; ritocchi del 16 ottobre 2026).
    /// </summary>
    public void UpdateTrayTip(IconNumberKind kind, int count)
    {
        (trayKind, trayCount) = (kind, count);
        if (tray == null) return;
        var name = tray.Tip.StartsWith("Missiva (prova)") ? "Missiva (prova)" : "Missiva";
        tray.Tip = IconCount.TrayTip(name, kind, count);
    }

#if DEBUG
    /// <summary>Prove (MISSIVA_KEYS "menu area di notifica"): il menu dell'icona, da fotografare con foto.ps1 -Popup.</summary>
    internal void ShowTrayMenu() => tray?.ShowMenu();
#endif

    /// <summary>
    /// Esci, dal menu dell'icona o con "esci" sul canale (installa.ps1 che aggiorna Missiva): la finestra si chiude davvero,
    /// con le domande delle scritture aperte (Salvare la bozza?) come quando si chiudeva senza area di notifica.
    /// </summary>
    public void ExitAll()
    {
        Log.Write("uscita da Missiva");
        Exiting = true;
        UpdateTray();
        if (MainWindow is MainWindow main) main.Close();
        else Shutdown();
    }

    /// <summary>Annulla in una domanda delle scritture aperte (Salvare la bozza?) durante l'uscita: Missiva resta, con l'icona.</summary>
    public void CancelExit()
    {
        if (!Exiting) return;
        Exiting = false;
        restartAfterExit = false;
        UpdateTray();
        Log.Write("uscita annullata");
        if (updateOnExit is { } update)
        {
            updateOnExit = null;
            Log.Write($"aggiornamento a {update.Manifest.Number} annullato con l'uscita");
            (MainWindow as MainWindow)?.UpdateCancelled(update);
        }
    }

    /// <summary>
    /// Avvia Missiva all'accesso a Windows (Impostazioni, Generali, Avvio e chiusura): il valore Missiva in HKCU\...\Run con
    /// --nascosta, che la apre nell'area di notifica senza finestra né WebView2. Nella versione di prova la chiave non si
    /// scrive mai: solo il registro.
    /// </summary>
    /// <param name="byUser">
    /// Dalla casella delle Impostazioni: accendendola si toglie anche lo "spento" che Windows tiene per le App di avvio
    /// (Impostazioni di Windows, o Gestione attività), altrimenti il valore in Run ci sarebbe ma Windows non la avvierebbe.
    /// All'avvio di Missiva no: lì si aggiorna solo il percorso, e una scelta fatta in Windows resta.
    /// </param>
    public static void ApplyStartWithWindows(bool byUser = false)
    {
        var command = $"\"{Environment.ProcessPath}\" --nascosta";
#if DEBUG || PROVA
        Log.Write(Store.Settings.StartWithWindows ? $"prova: scriverei l'avvio automatico {command} in HKCU\\{RunKey}" : $"prova: toglierei l'avvio automatico da HKCU\\{RunKey}");
#else
        try
        {
            WriteStartWithWindows(Store.Settings.StartWithWindows, command, byUser);
        }
        catch (Exception ex)
        {
            Log.Error("avvio con Windows", ex);
        }
#endif
    }

    /// <summary>
    /// Dopo un aggiornamento riuscito, il numero nuovo nella voce di App installate (Updates.WriteDisplayVersion), se Missiva
    /// sta dove la mette installa.ps1. Nella versione di prova la chiave non si scrive mai: solo il registro.
    /// </summary>
    static bool WriteInstalledVersion(string version)
    {
#if DEBUG || PROVA
        Log.Write($"prova: scriverei DisplayVersion {version} in HKCU\\{Updates.UninstallKey}");
        return false;
#else
        try
        {
            return Environment.ProcessPath is { } exe && Updates.WriteDisplayVersion(version, exe, Updates.InstallFolder);
        }
        catch (Exception ex)
        {
            Log.Error("voce di App installate dopo l'aggiornamento", ex);
            return false;
        }
#endif
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    /// <summary>Dove Windows tiene acceso o spento ogni programma di Run (App di avvio): il primo byte pari è acceso, dispari spento.</summary>
    const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// Lo stato vero dell'avvio con Windows, per la casella delle Impostazioni: il valore in Run c'è e Windows non l'ha
    /// spento in App di avvio. Prima la casella mostrava solo l'impostazione di Missiva: spenta in Windows, restava accesa
    /// (audit, P-08). Le chiavi sono parametri per le prove, che lavorano su chiavi loro.
    /// </summary>
    internal static bool StartsWithWindows(string run = RunKey, string approved = StartupApprovedKey, string name = "Missiva")
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(run);
            if (key?.GetValue(name) is not string { Length: > 0 }) return false;
            using var state = Registry.CurrentUser.OpenSubKey(approved);
            return state?.GetValue(name) is not byte[] { Length: > 0 } bytes || bytes[0] % 2 == 0;
        }
        catch (Exception ex)
        {
            Log.Error("lettura dell'avvio con Windows", ex);
            return false;
        }
    }

    /// <summary>Scrive o toglie il valore in Run (ApplyStartWithWindows); le chiavi sono parametri per le prove.</summary>
    internal static void WriteStartWithWindows(bool on, string command, bool byUser, string run = RunKey, string approved = StartupApprovedKey, string name = "Missiva")
    {
        using var key = Registry.CurrentUser.CreateSubKey(run);
        if (on)
        {
            if (byUser)
            {
                using var state = Registry.CurrentUser.OpenSubKey(approved, writable: true);
                if (state?.GetValue(name) is byte[] { Length: > 0 } bytes && bytes[0] % 2 == 1)
                {
                    state.DeleteValue(name, false); // senza il valore Windows la considera accesa
                    Log.Write("avvio con Windows: tolto lo \"spento\" di App di avvio di Windows");
                }
            }
            if (key.GetValue(name) as string == command) return;
            key.SetValue(name, command);
            Log.Write("avvio con Windows acceso");
        }
        else if (key.GetValue(name) != null)
        {
            key.DeleteValue(name, false);
            Log.Write("avvio con Windows spento");
        }
    }

    // ---------------------------------------------------------------- link mailto: e comandi

    /// <summary>
    /// I link mailto: che Windows dà a una seconda copia di Missiva (Program.SendMailto) arrivano qui, su un canale con
    /// nome solo per questo utente: si apre il messaggio nuovo in questa. Il testo del link non esegue nulla: riempie A,
    /// Cc, Ccn, oggetto e testo di una scrittura, che parte solo con Invia. Sullo stesso canale due comandi: "apri" (la
    /// seconda copia avviata dal menu Start, con questa nascosta nell'area di notifica) ed "esci" (installa.ps1, che la
    /// aggiorna e prima la chiude come con Esci).
    /// </summary>
    /// <summary>La riga di un comando sul canale con nome: al massimo questa misura (un link mailto: ha una riga sola) e questo tempo.</summary>
    internal const int MaxCommandBytes = 64 * 1024;
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Una riga dal canale con nome, per al massimo <paramref name="maxBytes"/> e <paramref name="timeout"/>. Null se la riga
    /// è più lunga, se il tempo scade prima dell'a capo, o se il client chiude senza scrivere nulla. Il client chiuso senza
    /// l'a capo vale com'è, come con StreamReader.ReadLine.
    /// </summary>
    internal static async Task<string?> ReadCommandAsync(Stream stream, int maxBytes, TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        using var line = new MemoryStream();
        var chunk = new byte[4096];
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(chunk, cancel.Token);
                if (read == 0) break;
                int end = Array.IndexOf(chunk, (byte)'\n', 0, read);
                int take = end >= 0 ? end : read;
                if (line.Length + take > maxBytes) return null;
                line.Write(chunk, 0, take);
                if (end >= 0) break;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        if (line.Length == 0) return null;
        return System.Text.Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
    }

    void ListenForMailto() => _ = ServeCommandsAsync(Program.MailtoPipe, OnPipeCommand);

    /// <summary>Un comando arrivato dal canale con nome: "esci", "apri" o un link mailto:. Il resto non fa nulla.</summary>
    void OnPipeCommand(string link)
    {
        if (link == "esci")
        {
            Log.Write("comando \"esci\" da un'altra copia di Missiva");
            ExitAll();
            return;
        }
        if (link == "apri")
        {
            Log.Write("comando \"apri\" da un'altra copia di Missiva");
            (MainWindow as MainWindow)?.ShowFromTray();
            return;
        }
        Log.Write("link mailto: da un'altra copia di Missiva");
        if (Mailto.Is(link) && MainWindow is MainWindow main) _ = Open();

        async Task Open()
        {
            try { await main.OpenMailtoAsync(link); }
            catch (Exception ex) { Log.Error("link mailto: da un'altra copia di Missiva", ex); }
        }
    }

    /// <summary>
    /// Il ciclo del canale con nome: un'istanza alla volta, finché Missiva è aperta. Dall'audit dell'11 ottobre (T-25):
    /// se il canale non si apre (il nome è di tutto il PC, e un altro processo può averlo preso prima; o un errore di
    /// Windows) si riprova per sempre, sempre più di rado fino a un minuto, invece di smettere dopo cinque errori e lasciare
    /// Missiva senza mailto:, "apri" ed "esci" per tutta la sessione; un collegamento riuscito azzera il conto; e il canale
    /// si chiude prima di eseguire il comando, così mentre un mailto: apre la sua finestra il collegamento dopo trova già
    /// l'istanza nuova.
    /// </summary>
    /// <param name="waitMs">Prove: l'attesa dopo l'errore numero N; di serie 1, 2, 4... 60 secondi.</param>
    internal static async Task ServeCommandsAsync(string pipe, Action<string> handle, CancellationToken stop = default, Func<int, int>? waitMs = null)
    {
        int failures = 0;
        while (!stop.IsCancellationRequested)
        {
            string? line;
            try
            {
                using var server = new System.IO.Pipes.NamedPipeServerStream(pipe, System.IO.Pipes.PipeDirection.In, 1,
                    System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop);
                failures = 0;
                // Una riga di al più 64 KB, scritta entro pochi secondi (audit, blocco 10, S-22): un client che non manda mai
                // l'a capo, o che ne manda a milioni, non tiene più occupata l'unica copia (niente più "esci", "apri" e mailto:).
                line = await ReadCommandAsync(server, MaxCommandBytes, CommandTimeout);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                if (failures == 1 || failures % 20 == 0) Log.Error($"canale con nome: non si apre (tentativo {failures}), si riprova", ex);
                int wait = waitMs?.Invoke(failures) ?? (int)Math.Min(1000L << Math.Min(failures - 1, 6), 60_000);
                try { await Task.Delay(wait, stop); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            if (line == null)
            {
                Log.Write("canale con nome: nessuna riga valida (troppo lunga, o ferma per più di tre secondi): collegamento chiuso");
                continue;
            }
            try { handle(line); }
            catch (Exception ex) { Log.Error("comando dal canale con nome", ex); }
        }
    }

    // ---------------------------------------------------------------- fine della sessione

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool ShutdownBlockReasonCreate(IntPtr hwnd, string reason);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShutdownBlockReasonDestroy(IntPtr hwnd);

    /// <summary>
    /// Windows chiude la sessione (riavvio per gli aggiornamenti, spegnimento, uscita): WPF chiude le finestre senza
    /// tenere conto di e.Cancel, e senza "Salvare la bozza?" il testo delle scritture aperte si perderebbe. Qui ognuna
    /// salva la sua bozza, se ha qualcosa di nuovo, e si aspetta la fine (al massimo 20 s) facendo girare l'interfaccia;
    /// intanto Windows mostra il motivo dell'attesa nella schermata di spegnimento.
    /// </summary>
    public void SaveDraftsBeforeExit(string why)
    {
        var open = Windows.OfType<ComposeWindow>().ToList();
        Log.Write($"{why}: {open.Count} scritture aperte");
        if (open.Count == 0) return;
        var handle = MainWindow is { } main ? new System.Windows.Interop.WindowInteropHelper(main).Handle : IntPtr.Zero;
        if (handle != IntPtr.Zero) ShutdownBlockReasonCreate(handle, "Missiva sta salvando le bozze dei messaggi aperti.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var all = Task.WhenAll(open.Select(w => w.SaveForSessionEndAsync()));
        var frame = new DispatcherFrame();
        _ = all.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        var limit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        limit.Tick += (_, _) => frame.Continue = false;
        limit.Start();
        Dispatcher.PushFrame(frame);
        limit.Stop();
        if (handle != IntPtr.Zero) ShutdownBlockReasonDestroy(handle);
        var saved = all.IsCompletedSuccessfully ? all.Result.Count(ok => ok) : 0;
        Log.Write($"{why}: bozze a posto {saved} di {open.Count} in {clock.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------------- tema di Windows

    /// <summary>
    /// Il tema di Windows è cambiato con Missiva aperta (anche da solo, con il cambio automatico all'alba e al tramonto).
    /// Finestre e controlli Fluent li aggiorna WPF; con questo evento si aggiorna quello che Missiva colora per conto suo:
    /// il riquadro di lettura, le icone colorate, i menu del tasto destro, i colori presi dalle risorse.
    /// </summary>
    public static event Action? ThemeChanged;

    /// <summary>Il tema mostrato adesso: Windows avvisa più volte per un solo cambio, e anche per il solo colore principale.</summary>
    bool dark;
    int themeAttempts;
    readonly DispatcherTimer themeCheck = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>Il cambio di tema arriva come WM_SETTINGCHANGE "ImmersiveColorSet", che SystemEvents passa come General.</summary>
    void OnUserPreference(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        themeAttempts = 0;
        themeCheck.Stop();
        themeCheck.Start();
    }

    /// <summary>
    /// Dopo WPF, che rimette le risorse Fluent del tema nuovo quando lo stesso avviso arriva alla sua finestra nascosta:
    /// prima, i colori ripresi dalle risorse sarebbero ancora quelli vecchi. Se WPF non ci è ancora arrivato si riprova.
    /// </summary>
    void ApplyTheme()
    {
        themeCheck.Stop();
        ReadingView.ResetDark();
        bool now = ReadingView.Dark;
        if (now == dark) return;
        if (ResourcesDark() != now && ++themeAttempts < 8)
        {
            themeCheck.Start();
            return;
        }
        dark = now;
        Log.Write($"tema cambiato: {(dark ? "scuro" : "chiaro")}");
        SetTextColors();
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Il testo secondario di Missiva (anteprime e ore nell'elenco, cartella dei risultati, "Mostra cartelle nascoste",
    /// descrizioni delle Impostazioni, intestazione dell'originale nella scrittura), grigio e leggibile in tutti e due i temi
    /// come nel nuovo Outlook, e come nel riquadro di lettura (ReadingPage). Quello di Fluent, trasparente, nel chiaro
    /// veniva sbiadito e nello scuro quasi bianco come il testo principale. Si usa con {DynamicResource SecondaryTextBrush}.
    /// </summary>
    void SetTextColors()
    {
        var brush = new SolidColorBrush(dark ? Color.FromRgb(0xAD, 0xAD, 0xAD) : Color.FromRgb(0x61, 0x61, 0x61));
        brush.Freeze();
        Resources["SecondaryTextBrush"] = brush;
        // Il rosso di bandierine, "Importanza alta" e scadenze passate: #C50F1F sulle schede scure #323232 aveva un
        // contrasto di 2,1 a 1 (per le icone ne serve almeno 3), nello scuro il rosso di Fluent per i fondi scuri (6,3 a 1).
        Resources["CriticalBrush"] = Frozen(dark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC5, 0x0F, 0x1F));
        // Righe dell'elenco dei messaggi: l'accento tenue è dei non letti, il grigio di dove si è (selezione, più scuro
        // del passaggio del mouse). Nello scuro la stessa tinta si vede meno, quindi un po' più forte.
        Resources["RowHoverBrush"] = Frozen(dark ? Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0A, 0, 0, 0));
        Resources["RowSelectedBrush"] = Frozen(dark ? Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x17, 0, 0, 0));
        Resources["UnreadTintOpacity"] = dark ? 0.10 : 0.07;
        // Schede dei messaggi in rilievo: piene (le ombre sotto non devono trasparire), un po' più chiare dell'elenco, con
        // il bordo più scuro in basso (nel tema scuro più chiaro in alto, come i controlli di Fluent), un alone su tutti i
        // lati (due anelli, scelta 2 dell'autore) e una striscia d'ombra sotto.
        Resources["RowCardBrush"] = Frozen(dark ? Color.FromRgb(0x32, 0x32, 0x32) : Colors.White);
        Resources["ListPanelBrush"] = Frozen(dark ? Color.FromRgb(0x24, 0x24, 0x24) : Color.FromRgb(0xF2, 0xF2, 0xF2));
        // Anelli di un pixel (MainWindow.xaml): la riga sotto la scheda, poi l'anello vicino, poi quello lontano.
        Resources["RowShadowBrush"] = Frozen(dark ? Color.FromArgb(0x78, 0, 0, 0) : Color.FromArgb(0x1E, 0, 0, 0));
        Resources["RowHaloNearBrush"] = Frozen(dark ? Color.FromArgb(0x48, 0, 0, 0) : Color.FromArgb(0x0F, 0, 0, 0));
        Resources["RowHaloBrush"] = Frozen(dark ? Color.FromArgb(0x20, 0, 0, 0) : Color.FromArgb(0x06, 0, 0, 0));
        var border = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        border.GradientStops.Add(new GradientStop(dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0, 0, 0), 0));
        border.GradientStops.Add(new GradientStop(dark ? Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0, 0, 0), 0.8));
        border.GradientStops.Add(new GradientStop(dark ? Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0, 0, 0), 1));
        border.Freeze();
        Resources["RowBorderBrush"] = border;
    }

    static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Il tema scelto in Impostazioni, Generali, Aspetto, come nel nuovo Outlook: si salva e si applica subito, con lo stesso
    /// gestore del cambio di tema di Windows (riquadro di lettura, menu, icone colorate, sole). Con Chiaro o Scuro Missiva
    /// non segue più Windows, nemmeno al cambio automatico dell'alba e del tramonto.
    /// </summary>
    public void SetTheme(AppTheme theme)
    {
        if (Store.Settings.Theme == theme) return;
        Store.Settings.Theme = theme;
        Store.Save();
        ThemeMode = ModeOf(theme);
        themeAttempts = 0;
        ApplyTheme();
    }

    static ThemeMode ModeOf(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeMode.Light,
        AppTheme.Dark => ThemeMode.Dark,
        _ => ThemeMode.System,
    };

    /// <summary>Il tema delle risorse Fluent in uso: il testo principale è chiaro nel tema scuro.</summary>
    bool ResourcesDark() => TryFindResource("TextFillColorPrimaryBrush") is SolidColorBrush { Color: var c } && c.R > 128;

#if DEBUG
    /// <summary>
    /// Prova del cambio di tema senza toccare le impostazioni di Windows: Missiva.exe --theme-flip 20 inverte il tema ogni
    /// 20 secondi (ThemeMode Dark o Light), e Dark lo segue.
    /// </summary>
    void StartThemeFlip()
    {
        var args = Environment.GetCommandLineArgs();
        if (!int.TryParse(args.SkipWhile(a => a != "--theme-flip").Skip(1).FirstOrDefault(), out var seconds) || seconds <= 0) return;
        var flip = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        flip.Tick += (_, _) =>
        {
            ReadingView.TestDark = !ReadingView.Dark;
            ThemeMode = ReadingView.TestDark == true ? ThemeMode.Dark : ThemeMode.Light;
            ApplyTheme(); // lo stesso gestore del cambio vero
        };
        flip.Start();
    }
#endif

    // ---------------------------------------------------------------- account

    public IMailProvider? ProviderFor(string accountId) => providers.FirstOrDefault(p => p.Account.Id == accountId);

    public void SetProviders(List<IMailProvider> list)
    {
        providers = list;
        foreach (var p in list) lastCheck.TryAdd(p.Account.Id, DateTime.UtcNow);
        _ = ScanSentLaterAsync(list);
        StartWatchers(list);
    }

    /// <summary>La rubrica dei suggerimenti legge la Posta inviata dopo l'avvio, quando elenchi e anteprime sono arrivati.</summary>
    static async Task ScanSentLaterAsync(List<IMailProvider> list)
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        // In sottofondo: nessuna finestra di Microsoft da sola (MicrosoftAuth.InBackgroundAsync).
        await MicrosoftAuth.InBackgroundAsync(async () =>
        {
            foreach (var p in list) await AddressBook.ScanSentAsync(p);
            // E la copia dei contatti di Outlook per i suggerimenti, solo con il consenso ai contatti già dato.
            foreach (var p in list) await ContactBook.RefreshRemoteAsync(p);
        });
    }

    // ---------------------------------------------------------------- nuovi messaggi subito

    /// <summary>L'IDLE di ogni provider (Gmail, IMAP), fermato quando il provider esce dall'elenco (account rimosso o ricaricato).</summary>
    readonly Dictionary<IMailProvider, CancellationTokenSource> watchers = new();
    /// <summary>La query delta di Graph ogni 30 secondi.</summary>
    DispatcherTimer? deltaTimer;
    bool deltaRunning;
    /// <summary>Gli account con un controllo già in programma dopo un avviso dell'IDLE, e quelli da rileggere (segni, usciti).</summary>
    readonly HashSet<string> pendingChecks = new(), pendingRefresh = new(), pendingArrived = new();
    /// <summary>Per account, l'errore della query delta già scritto nel registro (senza rete sarebbe una riga ogni 30 secondi).</summary>
    readonly HashSet<string> deltaErrorLogged = new();

    /// <summary>
    /// Nuovi messaggi subito, come Thunderbird ed eM Client: per Gmail e IMAP l'IDLE sulla Posta in arrivo (InboxIdle,
    /// anche con la finestra nell'area di notifica), per Graph la query delta ogni 30 secondi. Il controllo di ogni due
    /// minuti resta per tutti (e per i server senza IDLE).
    /// </summary>
    void StartWatchers(List<IMailProvider> list)
    {
        foreach (var (p, stop) in watchers.Where(w => !list.Contains(w.Key)).ToList())
        {
            stop.Cancel();
            watchers.Remove(p);
        }
        foreach (var p in list.Where(p => !watchers.ContainsKey(p) && !p.HasInboxDelta))
        {
            var stop = new CancellationTokenSource();
            watchers[p] = stop;
            _ = Task.Run(() => p.WatchInboxAsync(e => Dispatcher.BeginInvoke(() => OnInboxEvent(p, e)), stop.Token));
        }
        if (deltaTimer == null && list.Any(p => p.HasInboxDelta))
        {
            deltaTimer = new DispatcherTimer { Interval = DeltaEvery };
            deltaTimer.Tick += async (_, _) => await CheckDeltasAsync();
            deltaTimer.Start();
            _ = CheckDeltasAsync(); // il punto di partenza
        }
    }

    /// <summary>Per account, quando il registro ha detto l'ultima volta di un eco ignorato: una riga per raffica, non una per EXPUNGE.</summary>
    readonly Dictionary<string, DateTime> echoLogged = new();

    /// <summary>
    /// Un avviso dell'IDLE: il controllo di quell'account poco dopo (gli avvisi che arrivano insieme ne fanno uno solo). Un
    /// segno cambiato altrove con l'UID cambia solo la riga; senza UID, e per i messaggi usciti, la Posta in arrivo aperta si
    /// rilegge (unendo le righe, RefreshListAsync). Gli avvisi che sono l'eco di un'azione di Missiva (Elimina, Archivia,
    /// letto, entro pochi secondi: InboxIdle.OwnEcho) si ignorano: l'elenco è già giusto, e ogni Canc rileggeva la prima pagina
    /// sulla connessione principale e rifaceva l'elenco.
    /// </summary>
    async void OnInboxEvent(IMailProvider provider, InboxNotice e)
    {
        var id = provider.Account.Id;
        if (e.Kind == InboxEvent.Connected)
        {
            // La sessione dell'IDLE è collegata: il server si raggiunge (MainWindow.AccountReached). Nessun controllo da qui.
            (MainWindow as MainWindow)?.AccountReached(provider, "IDLE collegato");
            return;
        }
        if (e.Kind != InboxEvent.Arrived && InboxIdle.OwnEcho(id))
        {
            if (DateTime.UtcNow - echoLogged.GetValueOrDefault(id) > TimeSpan.FromSeconds(10))
                Log.Write($"IDLE: segni o messaggi usciti nella Posta in arrivo di {provider.Account.Email} dopo un'azione di Missiva: nessuna rilettura");
            echoLogged[id] = DateTime.UtcNow;
            return;
        }
        if (e is { Kind: InboxEvent.Changed, Id: { } uid }) (MainWindow as MainWindow)?.ApplyIdleFlags(provider, uid, e.Read, e.Flagged);
        else if (e.Kind != InboxEvent.Arrived) pendingRefresh.Add(id);
        if (e.Kind == InboxEvent.Arrived) pendingArrived.Add(id);
        if (!pendingChecks.Add(id)) return;
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        pendingChecks.Remove(id);
        bool refresh = pendingRefresh.Remove(id);
        bool arrived = pendingArrived.Remove(id);
        var what = arrived ? "nuovo messaggio" : refresh ? "segni o messaggi cambiati" : e.Kind == InboxEvent.Changed ? "segno cambiato altrove, solo la riga" : "avviso";
        Log.Write($"IDLE: {what} nella Posta in arrivo di {provider.Account.Email}");
        int found = await MicrosoftAuth.InBackgroundAsync(() => CheckAccountAsync(provider, "avviso dell'IDLE"));
        // Gmail rende visibile il messaggio alle altre connessioni anche qualche secondo dopo l'avviso dell'IDLE: se il controllo
        // non lo trova ancora, si riprova (un messaggio già letto, per esempio sul telefono, non si troverà mai: due controlli in più).
        foreach (var seconds in arrived ? new[] { 3, 10 } : [])
        {
            if (found != 0) break; // trovato, o un controllo già in corso che ripartirà da sé (CheckAccountAsync)
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            found = await MicrosoftAuth.InBackgroundAsync(() => CheckAccountAsync(provider, $"avviso dell'IDLE, di nuovo dopo {seconds} s"));
        }
        if (refresh) (MainWindow as MainWindow)?.RefreshInboxOf(provider);
    }

    /// <summary>
    /// La query delta degli account di Graph: letti ed eliminati altrove nell'elenco, e il controllo se c'è un nuovo da leggere.
    /// In sottofondo (MicrosoftAuth.InBackgroundAsync): un accesso da rifare non apre la finestra di Microsoft ogni 30 secondi.
    /// </summary>
    Task CheckDeltasAsync() => MicrosoftAuth.InBackgroundAsync(async () =>
    {
        if (deltaRunning) return;
        deltaRunning = true;
        try
        {
            foreach (var p in providers.Where(p => p.HasInboxDelta).ToList())
            {
                try
                {
                    var changes = await p.GetInboxChangesAsync();
                    deltaErrorLogged.Remove(p.Account.Id);
                    deltaOk[p.Account.Id] = DateTime.UtcNow;
                    // Graph ha risposto: un account rimasto "Non in linea" o con le cartelle non lette si rimette a posto ora.
                    (MainWindow as MainWindow)?.AccountReached(p, "query delta riuscita");
                    if (changes is not { Any: true }) continue;
                    Log.Write($"delta della Posta in arrivo di {p.Account.Email}: {changes.Seen.Count} nuovi o cambiati, {changes.Removed.Count} usciti");
                    (MainWindow as MainWindow)?.ApplyInboxChanges(p, changes);
                    // Anche per i soli letti o usciti altrove: il controllo riconta i non letti dal server (numero sull'icona e
                    // sulla cartella) subito, senza aspettare il controllo di ogni due minuti.
                    await CheckAccountAsync(p, changes.Seen.Any(s => !s.Read) ? "query delta" : "query delta, letti o usciti altrove");
                }
                catch (Exception ex)
                {
                    if (deltaErrorLogged.Add(p.Account.Id)) Log.Write($"delta della Posta in arrivo di {p.Account.Email} non letta: {ex.Message} (non lo riscrivo finché non torna)");
                }
            }
        }
        finally
        {
            deltaRunning = false;
        }
    });

    /// <summary>Per account, lo stato da sistemare (AccountFix) già scritto nel registro dal controllo dei nuovi messaggi.</summary>
    readonly Dictionary<string, AccountFix> accountFixLogged = new();
    /// <summary>
    /// Gli account che si stanno controllando (dal controllo di ogni due minuti, da un avviso dell'IDLE, dalla query delta), con
    /// quando è cominciato, a che punto è e un numero del controllo. Un controllo fermo da più di StuckCheck non blocca più
    /// quelli dopo: prima un controllo che non finiva scartava come "già in corso" ogni avviso dell'IDLE, e da lì nessuna
    /// notifica partiva più (27 settembre 2026, sera).
    /// </summary>
    readonly Dictionary<string, (DateTime Since, string Stage, int Run)> checkingAccounts = new();
    /// <summary>Gli account in cui è arrivato un avviso mentre un controllo era già in corso: finito quello, se ne fa un altro.</summary>
    readonly HashSet<string> checkAgain = new();
    int checkRuns;
    internal static readonly TimeSpan StuckCheck = TimeSpan.FromMinutes(2);

    void CheckStage(string accountId, int run, string stage)
    {
        if (checkingAccounts.TryGetValue(accountId, out var c) && c.Run == run) checkingAccounts[accountId] = (c.Since, stage, run);
    }

    /// <summary>Per account, l'ultima query delta riuscita e l'ultimo controllo di ogni due minuti fatto davvero.</summary>
    readonly Dictionary<string, DateTime> deltaOk = new(), polled = new();
    /// <summary>
    /// Dove l'IDLE è in sessione (Gmail, IMAP) o la query delta riesce (Graph, ogni 30 s) i nuovi messaggi e i segni cambiati
    /// arrivano da soli, e il controllo periodico è una rete di sicurezza, ogni SafetyNet. Il giro D1 l'aveva portata a 10
    /// minuti (0,3 ms di CPU al secondo in meno), ma un IDLE caduto senza accorgersene faceva arrivare la notifica fino a
    /// 10 minuti dopo, e i ritardi erano proprio il problema dell'autore: di nuovo a ogni giro del timer di due minuti (100 s:
    /// con 2 minuti esatti un timer in anticipo di poco saltava un giro su due). I tempi si tengono fra loro (audit
    /// dell'11 ottobre, C-39): SafetyNet è il giro del timer (PollEvery) meno venti secondi, e DeltaFresh, per quanto una
    /// query delta riuscita vale come "da poco", deve restare più lungo del giro delle query delta (DeltaEvery). Prima
    /// erano numeri scritti a mano in tre punti; i valori sono quelli di prima.
    /// </summary>
    internal static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(2), SafetyNet = PollEvery - TimeSpan.FromSeconds(20),
                                      DeltaEvery = TimeSpan.FromSeconds(30), DeltaFresh = TimeSpan.FromMinutes(2);

    bool Pushed(IMailProvider p) => p.HasInboxDelta
        ? DateTime.UtcNow - deltaOk.GetValueOrDefault(p.Account.Id) < DeltaFresh
        : InboxIdle.Watching(p.Account.Email);

    /// <param name="periodic">
    /// Il controllo di ogni due minuti: salta gli account con l'IDLE in sessione o con la query delta riuscita da poco,
    /// controllati da lui solo ogni SafetyNet. Gli altri (all'avvio, al ritorno della rete) controllano tutti.
    /// </param>
    async Task CheckNewAsync(bool periodic = false)
    {
        if (checking) return;
        var due = providers.Where(p => !periodic || !Pushed(p) || DateTime.UtcNow - polled.GetValueOrDefault(p.Account.Id) >= SafetyNet).ToList();
        if (due.Count == 0) return;
        checking = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int arrived = 0;
        try
        {
            // In sottofondo, anche con la finestra aperta: un nuovo accesso chiesto da Microsoft non apre la finestra account di
            // Windows sopra quello che si sta facendo; la barra sotto l'account propone "Accedi di nuovo". Un account che non
            // risponde non ferma gli altri: dopo StuckCheck si passa al successivo.
            foreach (var p in due)
            {
                polled[p.Account.Id] = DateTime.UtcNow;
                var check = MicrosoftAuth.InBackgroundAsync(() => CheckAccountAsync(p));
                if (await Task.WhenAny(check, Task.Delay(StuckCheck)) == check) arrived += Math.Max(0, await check);
                else Log.Write($"controllo dei nuovi messaggi di {p.Account.Email} fermo da più di {StuckCheck.TotalMinutes:0} minuti: passo agli altri account");
            }
        }
        finally
        {
            checking = false;
            Log.Time($"controllo dei nuovi messaggi ({due.Count}{(due.Count < providers.Count ? $" di {providers.Count}" : "")} account, {arrived} nuovi" +
                     $"{(WindowHidden ? ", finestra nell'area di notifica" : "")})", clock);
            if (checkAfterThis)
            {
                checkAfterThis = false;
                _ = Dispatcher.BeginInvoke(() => OnNetworkBack());
            }
        }
    }

    /// <summary>
    /// Il controllo dei nuovi messaggi di un account: regole di Missiva, elenco, non letti, notifiche. Restituisce quanti ne
    /// sono arrivati; -1 se l'account si stava già controllando (allora, finito quel controllo, ne parte un altro: il messaggio
    /// dell'avviso poteva arrivare dopo la sua lettura). Nel registro l'esito dei controlli non periodici e di quelli che
    /// trovano qualcosa, così una notifica mancata si ricostruisce.
    /// </summary>
    /// <param name="reason">Chi l'ha chiesto: il controllo di ogni due minuti, l'avviso dell'IDLE, la query delta.</param>
    async Task<int> CheckAccountAsync(IMailProvider p, string reason = "controllo di ogni due minuti")
    {
        var id = p.Account.Id;
        if (checkingAccounts.TryGetValue(id, out var running))
        {
            var age = DateTime.UtcNow - running.Since;
            if (age < StuckCheck)
            {
                checkAgain.Add(id);
                Log.Write($"controllo di {p.Account.Email} ({reason}) dopo quello in corso da {age.TotalSeconds:0} s ({running.Stage})");
                return -1;
            }
            Log.Write($"controllo di {p.Account.Email} fermo da {age.TotalSeconds:0} s ({running.Stage}): ne parte un altro ({reason})");
        }
        int run = ++checkRuns;
        checkingAccounts[id] = (DateTime.UtcNow, "nuovi messaggi dal server", run);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var since = lastCheck.GetValueOrDefault(p.Account.Id, DateTime.UtcNow);
            var started = DateTime.UtcNow;
            // Segnati come notificati solo dopo l'annuncio (sotto): un controllo interrotto a metà non li perde.
            var incoming = (await p.GetNewAsync(since)).Where(m => !notified.Contains(p.Account.Id + "|" + m.Id)).ToList();
            lastCheck[p.Account.Id] = started.AddMinutes(-1); // un minuto di margine; i doppioni li toglie "notified"
            CheckStage(id, run, "regole");
            // Le regole di Missiva (Gmail, IMAP) prima delle notifiche, su tutti i messaggi arrivati dall'ultima volta (letti o
            // no, anche più di 20): quello che spostano o eliminano non si annuncia, quello che segnano letto entra nell'elenco
            // senza notifica. I messaggi delle regole sono letti a parte: si riconoscono da cartella e UID.
            var silent = new HashSet<(string, string)>();
            try
            {
                if (await Rules.ApplyToNewAsync(p, since, () => MainWindow is MainWindow w ? w.FoldersOfAsync(p) : p.GetFoldersAsync()) is { } ruled)
                {
                    static (string, string) Key(MessageSummary m) => (m.FolderId, m.Id);
                    var removed = ruled.Removed.Select(Key).ToHashSet();
                    var read = ruled.Read.Select(Key).ToHashSet();
                    var flagged = ruled.Flagged.Select(Key).ToHashSet();
                    incoming = incoming.Where(m => !removed.Contains(Key(m))).ToList();
                    if (ruled.Removed.Count > 0) (MainWindow as MainWindow)?.RemoveRuledRows(p, ruled.Removed);
                    foreach (var m in incoming)
                    {
                        if (read.Contains(Key(m))) m.IsRead = true;
                        if (flagged.Contains(Key(m))) m.IsFlagged = true;
                    }
                    silent.UnionWith(read);
                    // Un messaggio che le regole hanno finito solo ora (riprovato dopo un'azione non riuscita al controllo
                    // di prima) è già nell'elenco: i segni cambiano sulla sua riga.
                    foreach (var m in ruled.Read.Union(ruled.Flagged))
                        (MainWindow as MainWindow)?.ApplyIdleFlags(p, m.Id, ruled.Read.Contains(m) ? true : null, ruled.Flagged.Contains(m) ? true : null);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"regole di Missiva su {p.Account.Email}", ex);
            }
            // Prima le righe nell'elenco e la notifica, poi i non letti dal server (con i messaggi letti sul telefono o sul web il
            // numero sull'icona non resta alto): ShowThenCountAsync.
            CheckStage(id, run, "elenco e notifiche");
            foreach (var m in incoming) notified.Add(p.Account.Id + "|" + m.Id);
            var announce = incoming.Where(m => !silent.Contains((m.FolderId, m.Id))).ToList();
            long announced = 0;
            // Il biglietto si prende prima di chiedere il conteggio (UnreadSync): un numero tornato dopo una lettura fatta qui
            // non si scrive. Con una scrittura di Missiva in viaggio non si chiede nemmeno: lo riconta RecountSoon quando finisce.
            long? ticket = null;
            await ShowThenCountAsync(incoming,
                (rows, unread) => (MainWindow as MainWindow)?.AddIncoming(p, rows, unread, ticket),
                () =>
                {
                    // Gli stessi messaggi, anche con le notifiche spente, contano per il numero "Messaggi nuovi" sull'icona
                    // (IconCount, ritocchi del 16 ottobre).
                    if (announce.Count > 0) (MainWindow as MainWindow)?.CountArrived(p, announce);
                    if (announce.Count > 0) Notify(p, announce);
                    announced = clock.ElapsedMilliseconds;
                },
                () =>
                {
                    CheckStage(id, run, "non letti dal server");
                    if ((ticket = UnreadSync.Ticket(id)) == null) throw new OperationCanceledException("scrittura in corso");
                    return p.GetInboxUnreadAsync();
                },
                ex =>
                {
                    if (ex is OperationCanceledException && ticket == null) RecountSoon(p);
                    else Log.Error($"non letti di {p.Account.Email}", ex);
                });
            (MainWindow as MainWindow)?.NoteAccountState(p, null);
            accountFixLogged.Remove(p.Account.Id);
            // Al massimo una volta all'ora, senza aspettarla e dopo il conteggio: in Gmail e IMAP usa la stessa connessione in
            // sottofondo (2 s), e partendo prima faceva aspettare il conteggio, e con lui la notifica.
            _ = AddressBook.ScanSentAsync(p);
            // E la copia dei contatti di Outlook per i suggerimenti, se ha più di 12 ore: prima si provava una volta sola, 15
            // secondi dopo l'avvio, e senza rete in quel momento restava vecchia per tutta la sessione (audit, A-17). Al
            // massimo un tentativo ogni mezz'ora per account.
            if (p.HasContacts && DateTime.UtcNow - contactsTried.GetValueOrDefault(id) > TimeSpan.FromMinutes(30))
            {
                contactsTried[id] = DateTime.UtcNow;
                _ = ContactBook.RefreshRemoteAsync(p);
            }
            if (incoming.Count > 0 || reason != "controllo di ogni due minuti")
                Log.Write($"controllo di {p.Account.Email} ({reason}): {incoming.Count} nuovi da leggere, {announce.Count} annunciati, {clock.ElapsedMilliseconds} ms" +
                          (announce.Count > 0 ? $" (notifica a {announced} ms)" : ""));
            return incoming.Count;
        }
        catch (Exception ex)
        {
            // Consenso mancante, password rifiutata, nuovo accesso: la barra sotto l'account lo dice, e nel registro
            // basta una riga quando comincia (una casella condivisa senza consenso scriveva lo stack ogni due minuti).
            var fix = MailException.FixOf(ex);
            if (fix == AccountFix.None) Log.Error($"controllo nuovi messaggi di {p.Account.Email}", ex);
            else if (accountFixLogged.GetValueOrDefault(p.Account.Id) != fix)
                Log.Write($"controllo nuovi messaggi di {p.Account.Email}: {ex.Message} (barra sotto l'account, {fix}; non lo riscrivo finché non cambia)");
            if (fix != AccountFix.None) accountFixLogged[p.Account.Id] = fix;
            (MainWindow as MainWindow)?.NoteAccountState(p, ex);
            return 0;
        }
        finally
        {
            // Solo il proprio: un controllo fermo che finisce tardi non toglie quello partito al suo posto.
            if (checkingAccounts.TryGetValue(id, out var mine) && mine.Run == run)
            {
                checkingAccounts.Remove(id);
                if (checkAgain.Remove(id)) _ = MicrosoftAuth.InBackgroundAsync(() => CheckAccountAsync(p, "arrivato un avviso durante il controllo"));
            }
        }
    }

    /// <summary>Gli account con un riconteggio dei non letti già in programma (RecountSoon).</summary>
    readonly HashSet<string> recounting = new();

    /// <summary>
    /// I non letti della Posta in arrivo di un account ricontati dal server poco dopo: finita una scrittura di Missiva
    /// (UnreadSync.Written: letto, eliminato, spostato, posposto), o quando un conteggio è tornato superato da una modifica
    /// fatta qui. Uno per account alla volta; aspetta che le scritture siano finite e il server abbia il numero nuovo
    /// (UnreadSync.Ticket), poi una sola richiesta. Prima un messaggio da leggere eliminato senza aprirlo (dalla notifica, con
    /// il pulsante della riga) lasciava il numero alto fino al controllo di ogni due minuti: nel registro della versione
    /// installata, 6 ottobre 2026, Elimina dalla notifica alle 11:15:33 e numero corretto alle 11:17:18.
    /// </summary>
    internal async void RecountSoon(IMailProvider p)
    {
        var id = p.Account.Id;
        if (!providers.Contains(p) || !recounting.Add(id)) return;
        try
        {
            long? ticket = null;
            for (int waited = 0; waited < 600 && (ticket = UnreadSync.Ticket(id)) == null; waited++) await Task.Delay(300);
            recounting.Remove(id);
            if (ticket == null || !providers.Contains(p)) return;
            int unread = await MicrosoftAuth.InBackgroundAsync(() => p.GetInboxUnreadAsync());
            (MainWindow as MainWindow)?.AddIncoming(p, [], unread, ticket, "ricontati dal server");
        }
        catch (Exception ex)
        {
            recounting.Remove(id);
            // Senza rete il numero resta quello di qui: lo rifà il controllo quando la rete torna.
            if (!Network.IsOffline(ex)) Log.Error($"non letti di {p.Account.Email}", ex);
        }
    }

    /// <summary>
    /// La fine di un controllo dei nuovi messaggi (audit, v2 V-26): prima le righe nell'elenco e la notifica, poi i non letti
    /// contati dal server, che correggono il numero stimato (quelli di prima più i nuovi). Prima la notifica aspettava il
    /// conteggio (Gmail: NOOP e SEARCH sulla connessione in sottofondo; Graph una richiesta) e, al primo controllo di ogni
    /// ora, dietro a lui sulla stessa connessione la Posta inviata letta per la rubrica. Un conteggio che non riesce, o che
    /// non torna, non ferma più la notifica.
    /// </summary>
    /// <param name="add">Le righe nuove nell'elenco e i non letti della Posta in arrivo (null: stimati).</param>
    internal static async Task ShowThenCountAsync(List<MessageSummary> incoming, Action<List<MessageSummary>, int?> add, Action notify,
                                                  Func<Task<int>> count, Action<Exception> failed)
    {
        if (incoming.Count > 0) add(incoming, null);
        notify();
        int? unread = null;
        try { unread = await count(); }
        catch (Exception ex) { failed(ex); }
        if (unread != null) add([], unread);
    }

    // ---------------------------------------------------------------- notifiche

    /// <summary>
    /// I messaggi notificati di recente (account, cartella e identificativo), gli ultimi 50: il clic e le azioni della notifica
    /// li ritrovano anche se non sono nell'elenco. Oltre, esce il più vecchio, non tutti.
    /// </summary>
    readonly Dictionary<string, MessageSummary> notifiedMessages = new();
    readonly Queue<string> notifiedOrder = new();

    static string NotifiedKey(string account, string? folder, string message) => account + "|" + folder + "|" + message;

    void RememberNotified(IMailProvider provider, MessageSummary m)
    {
        var key = NotifiedKey(provider.Account.Id, m.FolderId, m.Id);
        if (notifiedMessages.TryAdd(key, m)) notifiedOrder.Enqueue(key);
        else notifiedMessages[key] = m;
        while (notifiedOrder.Count > 50) notifiedMessages.Remove(notifiedOrder.Dequeue());
    }

    /// <summary>
    /// Come nel nuovo Outlook, la notifica porta account, cartella e messaggio (per "N nuovi messaggi" solo l'account): il
    /// clic apre la Posta in arrivo di quell'account con il messaggio selezionato (OnToast). Con un Reply-To la notifica dice
    /// dove vanno le risposte, come il riquadro.
    /// </summary>
    internal void Notify(IMailProvider provider, List<MessageSummary> incoming)
    {
        // Impostazioni, Generali, Notifiche: spente per questo account, i messaggi arrivano nell'elenco senza avviso.
        if (Store.Settings.NotificationsOff.Contains(provider.Account.Id))
        {
            Log.Write($"notifica non mostrata: notifiche spente per {provider.Account.Email} ({incoming.Count} messaggi nuovi)");
            return;
        }
        try
        {
            if (incoming.Count > 3)
            {
                Show(new ToastContentBuilder()
                    .AddArgument("account", provider.Account.Id)
                    .AddText($"{incoming.Count} nuovi messaggi")
                    .AddText(string.Join(", ", incoming.Select(m => m.From.Display).Distinct().Take(4)))
                    .AddAttributionText(provider.Account.Email));
                Log.Write($"notifica mostrata: {incoming.Count} nuovi messaggi di {provider.Account.Email}");
                return;
            }
            foreach (var m in incoming)
            {
                RememberNotified(provider, m);
#if DEBUG
                Log.Write("notifica: " + new ToastArguments().Add("account", provider.Account.Id).Add("folder", m.FolderId).Add("message", m.Id));
#endif
                var target = new ToastArguments().Add("account", provider.Account.Id).Add("folder", m.FolderId).Add("message", m.Id);
                var toast = new ToastContentBuilder()
                    .AddArgument("account", provider.Account.Id)
                    .AddArgument("folder", m.FolderId)
                    .AddArgument("message", m.Id)
                    .AddText(m.From.Display)
                    .AddText(m.Subject.Length > 0 ? m.Subject : "(nessun oggetto)")
                    .AddText(m.Preview)
                    .AddAttributionText(provider.Account.Email);
                // Il logo del marchio del mittente, se è già stato trovato (Logos), nel cerchio come nell'elenco; altrimenti il
                // cerchio con le sue iniziali (NotificationAvatar): la notifica ha sempre l'immagine e il nome del mittente
                // (il nome è la prima riga; la riga in alto con "Missiva" è dell'app registrata in Windows).
                var image = Logos.DomainFor(m.From) is { } domain && File.Exists(SenderLogos.PathOf(domain)) ? SenderLogos.PathOf(domain) : NotificationAvatar.PathOf(m.From);
                if (image != null) toast.AddAppLogoOverride(new Uri(image), ToastGenericAppLogoCrop.Circle);
                AddActions(toast, target);
#if DEBUG
                Log.Write("notifica, XML: " + toast.GetToastContent().GetContent());
#endif
                Show(toast);
                Log.Write($"notifica mostrata: messaggio {m.Id} di {provider.Account.Email}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("notifica", ex);
        }
    }

    /// <summary>
    /// Il pulsante del contrassegno: "Contrassegna" (12 lettere) non ci sta nella terza parte della larghezza della notifica
    /// (Windows 11: "Contrassegn" e la "a" a capo, 29 settembre 2026), "Segna" sì.
    /// </summary>
    internal const string FlagButton = "Segna";

    /// <summary>
    /// Le notifiche di un solo messaggio (non "N nuovi messaggi"), come le ha chieste l'autore: tre pulsanti, Rispondi, Contrassegna
    /// ed Elimina, e Posponi nel menu "..." della notifica: "Posponi di un'ora" subito, "Posponi..." con la scelta di giorno e
    /// ora di Missiva. Windows mette un pulsante accanto a un campo solo se è una casella di testo (la scelta di Posponi con il
    /// suo pulsante faceva quattro pulsanti sotto, con i nomi spezzati) e ammette al massimo cinque fra pulsanti e voci del
    /// menu (27 settembre 2026). Rispondi e "Posponi..." aprono una finestra (attivazione in primo piano); Posponi di un'ora,
    /// Contrassegna ed Elimina lavorano senza aprirla (in sottofondo: con Missiva chiusa Windows la avvia nascosta). Il
    /// messaggio lo ritrova Missiva dall'account e dall'identificativo degli argomenti, mai dal testo
    /// (MainWindow.NotificationActionAsync).
    /// </summary>
    /// <param name="target">Account, cartella e messaggio: le voci del menu non ricevono gli argomenti del costruttore.</param>
    internal static void AddActions(ToastContentBuilder toast, ToastArguments target)
    {
        toast.AddButton(new ToastButton().SetContent("Rispondi").AddArgument("action", "reply"))
            .AddButton(new ToastButton().SetContent(FlagButton).AddArgument("action", "flag").SetBackgroundActivation())
            .AddButton(new ToastButton().SetContent("Elimina").AddArgument("action", "delete").SetBackgroundActivation());
        if (toast.GetToastContent().Actions is not ToastActionsCustom actions) return;
        string For(string action, string? choice = null)
        {
            var arguments = new ToastArguments();
            foreach (var pair in target) arguments.Add(pair.Key, pair.Value);
            arguments.Add("action", action);
            if (choice != null) arguments.Add("snooze", choice);
            return arguments.ToString();
        }
        actions.ContextMenuItems.Add(new ToastContextMenuItem("Posponi di un'ora", For("snooze", "1h")) { ActivationType = ToastActivationType.Background });
        actions.ContextMenuItems.Add(new ToastContextMenuItem("Posponi...", For("snoozepick")));
    }

    /// <summary>
    /// L'ora di Posponi scelta nella notifica, calcolata al clic (la notifica può restare nel centro notifiche per ore): fra
    /// un'ora; stasera alle 18 (passate, fra un'ora); domani alle 8, o stamattina alle 8 prima delle 5, come il menu Posponi.
    /// </summary>
    internal static DateTime SnoozeUntil(string? choice, DateTime now) => choice switch
    {
        "sera" when now.Hour < 18 => now.Date.AddHours(18),
        "domani" => (now.Hour < 5 ? now.Date : now.Date.AddDays(1)).AddHours(8),
        _ => now.AddHours(1),
    };

    /// <summary>
    /// Nella versione di prova (anche la finale compilata per le prove) le notifiche spariscono dal centro notifiche dopo
    /// dieci minuti: Windows lega il clic all'eseguibile che le ha mostrate, e il giorno dopo lo avvierebbe senza la copia
    /// dei dati di prova.ps1 (Program.Main in quel caso chiude subito).
    /// </summary>
    internal static void Show(ToastContentBuilder toast)
    {
        if (!Store.Settings.NotificationSound) toast.AddAudio(new ToastAudio { Silent = true }); // Impostazioni, Generali, Notifiche
#if DEBUG || PROVA
        toast.Show(t => t.ExpirationTime = DateTimeOffset.Now.AddMinutes(10));
#else
        toast.Show();
#endif
    }

    /// <summary>
    /// Il clic su una notifica: Missiva in primo piano, sulla Posta in arrivo dell'account e, per un messaggio solo, con il
    /// messaggio selezionato e aperto nel riquadro. Anche con Missiva chiusa: Windows la avvia e l'evento arriva dopo.
    /// Rispondi, Posponi ed Elimina (action) lavorano invece senza mostrare la finestra, con quello scritto o scelto nella
    /// notifica (input).
    /// </summary>
    internal void OnToast(string argument, IReadOnlyDictionary<string, object>? input = null)
    {
        if (MainWindow is not MainWindow main) return;
        ToastArguments args;
        try { args = ToastArguments.Parse(argument); }
        catch (Exception ex) { Log.Error("argomenti della notifica", ex); main.ShowFromTray(); return; }
        args.TryGetValue("account", out string? account);
        args.TryGetValue("message", out string? message);
        args.TryGetValue("folder", out string? folder); // manca nelle notifiche mostrate prima del giro P3, correzioni
        // Il promemoria di un contrassegno: Posponi promemoria e Segna come completato, senza aprire la finestra.
        // Id e cartella del messaggio hanno nomi loro (flagmessage, flagfolder): il clic sulla notifica apre solo la Posta in
        // arrivo, e con "message" selezionerebbe lì un altro messaggio con lo stesso UID.
        if (args.TryGetValue("action", out string? flagAction) && flagAction is "flagdone" or "flagsnooze" && account != null && args.TryGetValue("flag", out string? flagKey))
        {
            args.TryGetValue("flagmessage", out string? flagMessage);
            args.TryGetValue("flagfolder", out string? flagFolder);
            Log.Write($"azione dal promemoria del contrassegno: {flagAction} ({(WindowHidden ? "finestra nell'area di notifica" : "finestra aperta")})");
            _ = CountActionAsync(() => main.FlagReminderActionAsync(account, flagKey, flagMessage, flagFolder, flagAction));
            return;
        }
        if (args.TryGetValue("action", out string? action) && account != null && message != null && action is "reply" or "snooze" or "snoozepick" or "flag" or "delete")
        {
            Log.Write($"azione dalla notifica: {action} ({(WindowHidden ? "finestra nell'area di notifica" : "finestra aperta")})");
            args.TryGetValue("snooze", out string? snoozeChoice); // Posponi dal menu "...": la scelta è negli argomenti
            _ = ActOnNotificationAsync(main, account, folder, message, action, input, snoozeChoice);
            return;
        }
        // Un messaggio solo: si apre sempre in una finestra sua (come il doppio clic sulla riga), senza toccare la finestra
        // principale: in Persone o nel Calendario il clic sulla notifica non cambiava nulla di visibile (richiesta dell'autore, 28
        // settembre 2026). Il 29 settembre per qualche ora, senza nessuna finestra aperta, apriva l'app con la mail nel
        // riquadro; l'autore ha voluto il pop-up sempre, con un pulsante nella finestra per portarla nel riquadro di lettura
        // (MessageWindowBar, ShowInMainWindow).
        if (account != null && message != null)
        {
            _ = main.OpenMessageWindowFromNotificationAsync(account, folder, message, notifiedMessages.GetValueOrDefault(NotifiedKey(account, folder, message)));
            return;
        }
        main.ShowFromTray();
        if (account == null) return; // le notifiche di Posponi portano solo in primo piano
        _ = main.OpenFromNotificationAsync(account, message, null); // più messaggi: la Posta in arrivo di quell'account
    }

    /// <summary>
    /// Le azioni delle notifiche in corso (Rispondi, Posponi, Elimina, e dal promemoria di un contrassegno Posponi promemoria
    /// e Segna come completato): Missiva avviata solo per loro esce quando sono finite tutte.
    /// </summary>
    int notificationActions;

    /// <param name="snoozeChoice">Posponi dal menu "...": 1h, sera o domani; nelle notifiche di prima la scelta era nell'input.</param>
    Task ActOnNotificationAsync(MainWindow main, string account, string? folder, string message, string action, IReadOnlyDictionary<string, object>? input, string? snoozeChoice)
    {
        string? Value(string key) => input != null && input.TryGetValue(key, out var v) ? v as string : null;
        return CountActionAsync(() => main.NotificationActionAsync(account, folder, message, action, Value("reply"), SnoozeUntil(snoozeChoice ?? Value("snooze"), DateTime.Now),
                                                                   notifiedMessages.GetValueOrDefault(NotifiedKey(account, folder, message))));
    }

    /// <summary>Un'azione di una notifica, contata in notificationActions; finita l'ultima, Missiva avviata solo per loro esce.</summary>
    async Task CountActionAsync(Func<Task> action)
    {
        notificationActions++;
        // Un'azione che fallisce va nel registro e non salta il controllo dell'uscita qui sotto: prima l'errore si perdeva
        // (nessuno aspetta questo Task) e Missiva, avviata solo per l'azione, restava accesa e nascosta (audit dell'11
        // ottobre, C-26).
        await Log.GuardAsync("azione dalla notifica", action);
        notificationActions--;
        // Windows l'ha avviata solo per le azioni, e chiudendo non deve restare nell'area di notifica: finite tutte (un
        // Elimina a metà lascerebbe il messaggio in due cartelle, un Segna come completato a metà il contrassegno sul server
        // senza più il promemoria), esce. Non con una scrittura aperta (una risposta non partita).
        if (notificationActions == 0 && startedForNotification && WindowHidden && !Store.Settings.KeepInTray && !Windows.OfType<ComposeWindow>().Any())
        {
            Log.Write("azioni dalla notifica fatte: Missiva, avviata solo per queste, esce");
            ExitAll();
        }
    }

    /// <summary>Windows ha avviato Missiva per un'azione di una notifica (ToastNotificationManagerCompat.WasCurrentProcessToastActivated).</summary>
    bool startedForNotification;
}

/// <summary>Un insieme di chiavi con un tetto: oltre, esce quella entrata per prima. Per chi tiene Missiva aperta per giorni.</summary>
internal sealed class RecentKeys(int max)
{
    readonly HashSet<string> keys = new();
    readonly Queue<string> order = new();

    public int Count => keys.Count;

    public bool Contains(string key) => keys.Contains(key);

    public void Add(string key)
    {
        if (!keys.Add(key)) return;
        order.Enqueue(key);
        while (order.Count > max) keys.Remove(order.Dequeue());
    }
}
