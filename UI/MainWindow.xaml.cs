using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Missiva.UI;

public partial class MainWindow : Window
{
    readonly ObservableCollection<AccountItem> accounts = new();
    readonly MessageRows rows = new();
    readonly ListCollectionView view;

    FolderItem? folder;
    bool? focused;
    string? next;
    bool loading, searching, selectingFolder;
    int listVersion;
    bool regrouping;

    static Settings S => Store.Settings;

    /// <summary>Finestra di una sola cartella (Apri in un'altra finestra): account e cartella da mostrare.</summary>
    readonly (string AccountId, string FolderId)? only;
    /// <summary>Le finestre di una sola cartella aperte da questa: si chiudono quando gli account si ricaricano.</summary>
    readonly List<Window> folderWindows = new();
    readonly ObservableCollection<FolderItem> favorites = new();

    public MainWindow((string AccountId, string FolderId)? only = null)
    {
        this.only = only;
        if (only == null) Log.Startup("InitializeComponent");
        InitializeComponent();
        MessageList.Loaded += (_, _) => AttachListScrolling(); // il cursore della barra senza scatti e il mouse fermo fuori dal hit test mentre l'elenco scorre veloce
        if (only == null) Log.Startup("XAML della finestra");
        // La riga dell'elenco disegnata da un solo elemento (MessageRowElement); MISSIVA_RIGA_VECCHIA=1 rimette il modello di prima, per i
        // confronti: solo nelle versioni di prova, la versione installata non guarda variabili d'ambiente (audit dell'11 ottobre, T-29).
#if DEBUG || PROVA
        if (Environment.GetEnvironmentVariable("MISSIVA_RIGA_VECCHIA") != "1")
#endif
        MessageList.ItemTemplate = (DataTemplate)FindResource("MessageRowNew");
        CursorProbe.Setup(this, MessageList); // contatori del cursore sopra l'elenco: solo DEBUG (CursorProbe)
        AccountsList.ItemsSource = accounts;
        FavoritesList.ItemsSource = favorites;
        ShowUnifiedGroup();
        view = (ListCollectionView)CollectionViewSource.GetDefaultView(rows.Rows);
        MessageList.ItemsSource = view;
        Reader.ReplyRequested += OpenCompose;
        Reader.MessageRead += OnReaderRead;
        Reader.MailtoRequested += OnMailto;
        Reader.EditRequested += OpenDraft;
        Reader.MoreRequested += (provider, summary) => ShowMessageMenu(provider, summary, Reader, null);
        Reader.PersonRequested += (provider, person) => ShowPerson(provider, person, this);
        Reader.InviteRequested += OnInvite;
        Reader.EmlRequested += OpenEml;
        StateChanged += OnStateChanged;
        App.ThemeChanged += OnThemeChanged;
        if (only == null) Log.Startup("gestori");
        Logos.Arrived += OnLogo;
        if (only == null) Log.Startup("loghi");
        ReadingView.ZoomChanged += ShowZoom;
        if (only == null) WatchDownloads(); // il gestore degli scaricamenti nella barra del titolo (MainWindow.Downloads.cs)
        Closed += (_, _) =>
        {
            App.ThemeChanged -= OnThemeChanged;
            Logos.Arrived -= OnLogo;
            ReadingView.ZoomChanged -= ShowZoom;
            if (only == null) UnwatchDownloads();
            // Anche se non si è mai vista (avviata nascosta nell'area di notifica), con lei esce Missiva.
            if (only == null && Application.Current.MainWindow == this) Application.Current.Shutdown();
        };
        Closing += OnClosing;
#if DEBUG || PROVA
        if (only == null)
        {
            // Quanti giri di layout finiscono prima del primo disegno (audit del 7 ottobre, A-06).
            int layouts = 0;
            EventHandler count = (_, _) => layouts++;
            LayoutUpdated += count;
            ContentRendered += (_, _) =>
            {
                LayoutUpdated -= count;
                if (!App.WindowHidden && !hiddenAtStart) Log.Startup($"primo disegno ({layouts} giri di layout)");
            };
        }
#endif
        ContentRendered += (_, _) => rendered.TrySetResult();
        Loaded += async (_, _) =>
        {
#if DEBUG || PROVA
            if (!started) Log.Write($"tempi: finestra pronta {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:0} ms dopo l'avvio");
#endif
            await StartAsync();
        };
        UpdateCommands();
    }

    /// <summary>L'avvio di account, cartelle, timer e prove è già partito (anche a finestra nascosta, StartAsync).</summary>
    bool started;
    /// <summary>Avviata nascosta nell'area di notifica: il primo disegno non è quello dell'avvio.</summary>
    bool hiddenAtStart;

    /// <summary>
    /// Account, cartelle, elenco e timer (Posponi, invii pianificati, promemoria). Di solito alla comparsa della finestra
    /// (Loaded); con Missiva avviata nascosta nell'area di notifica (--nascosta, avvio con Windows, azione di una notifica)
    /// subito, senza disegnare nulla e senza WebView2: la finestra, quando si apre, trova tutto pronto.
    /// </summary>
    public async Task StartAsync()
    {
        if (started) return;
        started = true;
        hiddenAtStart = !IsVisible;
        // Solo la finestra principale: quella di una sola cartella, chiusa, resterebbe in memoria e WAM riceverebbe come
        // genitore una finestra che non esiste più.
        if (only == null) MicrosoftAuth.Owner = () => new WindowInteropHelper(this).Handle;
        ApplySettings(reload: false);
        if (only == null) Log.Startup("ApplySettings");
        ShowFolders();
        if (only == null) Log.Startup("disposizione");
        // WebView2 si prepara appena l'elenco è sullo schermo, senza rallentare la comparsa della finestra.
        if (IsVisible && S.ReadingPane != ReadingPanePosition.Off) _ = Dispatcher.BeginInvoke(Reader.Prepare, DispatcherPriority.ApplicationIdle);
        // Prima la cornice della finestra, poi account, cartelle ed elenco della copia locale, che costano 0,4-0,5 s di
        // disposizione: la finestra si vede quasi mezzo secondo prima, come il nuovo Outlook, e l'elenco allo stesso momento.
        if (only == null && IsVisible) await rendered.Task;
#if DEBUG
        // Casella condivisa finta (--condivisa-finta): un account di lavoro finto e info@ condivisa sotto di lui, nella copia
        // dei dati. Senza accesso né consenso: la barra dell'account e quella del consenso, nessuna finestra.
        if (only == null && Environment.GetCommandLineArgs().Contains("--condivisa-finta")) AddTestSharedMailbox();
        // Un modello di prova (--modello-finto) nella copia dei dati: grassetto, elenco, link, {nome} e {data}.
        if (only == null && Environment.GetCommandLineArgs().Contains("--modello-finto")) AddTestTemplate();
#endif
        // Gli account, la copia locale e la Posta in arrivo subito; le cartelle dal server arrivano dopo (serverFolders), e i
        // timer qui sotto non le aspettano più. Prima partivano solo quando tutte le cartelle erano lette o fallite: con la
        // rete in ritardo all'accesso a Windows (Wi-Fi o VPN non pronti, un firewall che trattiene i collegamenti) niente
        // ritorni da Posponi, invii pianificati, promemoria né copia degli ultimi 30 giorni fino al tempo scaduto più lungo,
        // 60-120 s (audit del 7 ottobre, A-13; con la rete buona partivano a 2,0-2,2 s dall'avvio invece che a 0,8).
        var serverFolders = LoadAccountsAsync();
        if (only != null)
        {
            await serverFolders;
            return; // notifiche, Posponi e prove le gestisce la finestra principale
        }
        if (accounts.Count == 0 && IsVisible) OnAddAccount(this, new RoutedEventArgs());
        // Posta in uscita (messaggi inviati senza rete): la barra in fondo all'elenco e i conteggi sotto gli account.
        Outbox.Changed += OnOutboxChanged;
        ActionQueue.Changed += OnOutboxChanged; // anche i letti nel riquadro senza rete (ReadingView.SetRead)
        Closed += (_, _) =>
        {
            Outbox.Changed -= OnOutboxChanged;
            ActionQueue.Changed -= OnOutboxChanged;
        };

        // Posponi: i ritorni scaduti mentre Missiva era chiusa, poi un controllo al minuto.
        foreach (var old in S.Woken.Where(w => w.Value < DateTime.Now.AddDays(-14)).Select(w => w.Key).ToList()) S.Woken.Remove(old);
        // I ritorni in sottofondo: un accesso da rifare non apre la finestra di Microsoft (MicrosoftAuth.InBackgroundAsync).
        snoozeTimer.Tick += async (_, _) => await MicrosoftAuth.InBackgroundAsync(CheckSnoozedAsync);
        // Invii pianificati di Gmail e IMAP: quelli scaduti mentre Missiva era chiusa partono adesso, poi al minuto.
        snoozeTimer.Tick += async (_, _) => await SendScheduledAsync();
        // Promemoria dei contrassegni, anche quelli scaduti mentre Missiva era chiusa.
        snoozeTimer.Tick += (_, _) => CheckFlagReminders(DateTime.Now);
        // Il cambio di giorno con Missiva aperta, anche nell'area di notifica: i gruppi Oggi e Ieri dell'elenco (CheckDay).
        snoozeTimer.Tick += (_, _) => CheckDay();
        Activated += (_, _) => CheckDay();
        // Guardata la finestra principale, i messaggi nuovi del numero sull'icona ripartono da zero (ritocchi del 16 ottobre).
        Activated += (_, _) => { if (only == null && fresh.Clear()) UpdateBadge(); };
        // Gli allegati aperti restano in %TEMP%\Missiva un'ora dopo l'ultima modifica, non fino all'uscita (audit, S-28).
        snoozeTimer.Tick += (_, _) => { _ = Task.Run(() => Safety.CleanTemp(TimeSpan.FromHours(1))); };
        CheckFlagReminders(DateTime.Now);
        snoozeTimer.Start();
        _ = MicrosoftAuth.InBackgroundAsync(CheckSnoozedAsync);
        _ = SendScheduledAsync();
        StartReminders(); // promemoria del calendario
        StartOfflineCopy(); // la Posta in arrivo degli ultimi 30 giorni sul PC, a finestra ferma
        Log.Startup("timer di Posponi, invii pianificati, promemoria e copia dei 30 giorni avviati");
        // Avviata nascosta: quando il computer è libero, la disposizione della finestra si fa senza mostrarla (modelli dei
        // controlli, cartelle, righe dell'elenco), così la prima apertura dall'area di notifica deve solo disegnarla.
        if (!IsVisible) _ = Dispatcher.BeginInvoke(LayOutHidden, DispatcherPriority.ApplicationIdle);
        await serverFolders; // le prove qui sotto partono con le cartelle dal server, come prima
#if DEBUG
        // Prove del menu del tasto destro: Missiva.exe --menu (lo apre), --menu-edit (apre la personalizzazione)
        if (Environment.GetCommandLineArgs().Contains("--menu-edit")) new MenuCustomizeWindow(ContextMenus.Messages) { Owner = this }.Show();
        if (Environment.GetCommandLineArgs().Contains("--folder-menu-edit")) new MenuCustomizeWindow(ContextMenus.Folders) { Owner = this }.Show();
        if (Environment.GetCommandLineArgs().Contains("--folder-window") && folder != null) OpenFolderWindow(folder);
        // Intestazione dell'elenco: --filter Unread|Flagged|Attachments, --jump 2024-03-12, --oldest
        var cli = Environment.GetCommandLineArgs();
        string? Arg(string name) => cli.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        // Ordine degli account: --move-account indirizzo posizione (0 in cima), come Sposta su e giù
        if (Arg("--move-account") is { } moved && accounts.FirstOrDefault(a => a.Provider.Account.Email.Equals(moved, StringComparison.OrdinalIgnoreCase)) is { } movedAccount &&
            int.TryParse(cli.SkipWhile(a => a != "--move-account").Skip(2).FirstOrDefault(), out var position))
            MoveAccount(movedAccount, position);
        // Posta in arrivo di un altro account: --account indirizzo
        if (Arg("--account") is { } email && accounts.FirstOrDefault(a => a.Provider.Account.Email.Equals(email, StringComparison.OrdinalIgnoreCase)) is { } other &&
            other.Folders.FirstOrDefault(x => x.Folder.Kind == FolderKind.Inbox) is { } otherInbox)
            SelectFolder(otherInbox);
        // Posta certificata di prova (--scrivi-pec): --pec-radice radice-di-prova.cer la fa valere come attendibile, solo qui
        if (Arg("--pec-radice") is { } root && File.Exists(root))
            Pec.TestRoots = [System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(root)];
        // Un messaggio locale nel riquadro (inviti di prova): --eml file.eml, con le risposte dall'account aperto; con
        // --eml-indesiderata come se fosse allegato a un messaggio della Posta indesiderata
        if (Arg("--eml") is { } emlPath && accounts.Count > 0)
            OpenEml(folder?.Account.Provider ?? accounts[0].Provider, await File.ReadAllBytesAsync(emlPath), Path.GetFileName(emlPath), cli.Contains("--eml-indesiderata"));
        if (Arg("--filter") is { } f && Enum.TryParse<MessageFilter>(f, out var filter)) ApplyListOptions(listOptions with { Filter = filter });
        if (Arg("--jump") is { } j && DateTime.TryParse(j, System.Globalization.CultureInfo.InvariantCulture, out var day)) ApplyListOptions(listOptions with { UpTo = day });
        if (cli.Contains("--oldest")) ApplyListOptions(listOptions with { Ascending = true });
        // Calendario: --calendario [2026-10-25] lo apre (su quel giorno), --calendario-vista Day|WorkWeek|Week|Month la vista,
        // --calendario-finto con gli eventi finti di CalendarSamples invece di quelli degli account
        // Persone: --persone apre la vista (--contatti-finti con i contatti finti di ContactSamples, --nuovo-contatto la
        // finestra del contatto nuovo compilata)
        if (cli.Contains("--persone"))
        {
            ShowPeople(true);
            _ = Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(1500);
                if (Arg("--filtro-persone") is { } only) people?.TestFilter(only);
                if (Arg("--cerca-persone") is { } find) people?.TestSearch(find);
                people?.TestSelect(Arg("--persona") is { } who && int.TryParse(who, out var whoIndex) ? whoIndex : 0);
                if (cli.Contains("--nuovo-contatto")) people?.TestNewContact();
                if (cli.Contains("--modifica-contatto")) people?.TestEdit();
            });
        }
        if (cli.Contains("--calendario"))
        {
            var on = Arg("--calendario") is { } d && DateTime.TryParse(d, System.Globalization.CultureInfo.InvariantCulture, out var date) ? date : (DateTime?)null;
            var mode = Arg("--calendario-vista") is { } m && Enum.TryParse<CalendarMode>(m, out var parsedMode) ? parsedMode : (CalendarMode?)null;
            ShowCalendar(true, on, mode);
            // --nuovo-evento: la finestra Nuovo evento compilata come da un utente (domani alle 10), Salva resta da premere
            if (cli.Contains("--nuovo-evento"))
                _ = Dispatcher.BeginInvoke(async () => { await Task.Delay(1500); calendar?.NewEvent(DateTime.Today.AddDays(1).AddHours(10)); });
            // --modifica-evento e --annulla-evento: la riunione organizzata da sé in modifica (luogo e fine cambiati, Salva da
            // premere) o da annullare; --sposta-evento sposta di un'ora un evento proprio, come trascinato (nella prova solo il registro)
            if (cli.Contains("--modifica-evento") || cli.Contains("--annulla-evento"))
                _ = Dispatcher.BeginInvoke(async () => { await Task.Delay(2500); calendar?.TestOpen(cancel: cli.Contains("--annulla-evento")); });
            if (cli.Contains("--sposta-evento"))
                _ = Dispatcher.BeginInvoke(async () => { await Task.Delay(2500); if (calendar != null) await calendar.TestMoveAsync(); });
        }
        // Contrassegni: --contrassegno 2 apre il sottomenu Contrassegna del messaggio 2; --contrassegno-promemoria 2 gli mette
        // un promemoria fra un minuto e fa passare il tempo (nella prova tutto solo nel registro)
        if (int.TryParse(Arg("--contrassegno-promemoria"), out var remindRow) && remindRow >= 0)
        {
            for (int wait = 0; wait < 100 && loading; wait++) await Task.Delay(100);
            if (remindRow < rows.Rows.Count)
            {
                await SetFlagReminderAsync([rows.Rows[remindRow]], DateTime.Now.AddMinutes(1));
                CheckFlagReminders(DateTime.Now.AddMinutes(2));
            }
        }
        // Rispondi con riunione: --rispondi-riunione 2 apre la finestra della riunione compilata dal messaggio 2 dell'elenco
        if (int.TryParse(Arg("--rispondi-riunione"), out var meetingRow) && meetingRow >= 0)
        {
            for (int wait = 0; wait < 100 && loading; wait++) await Task.Delay(100);
            if (meetingRow < rows.Rows.Count) _ = ReplyWithMeetingAsync(rows.Rows[meetingRow], null);
        }
        // Risposte automatiche finte (nella prova, senza consenso, non si leggono): --risposte-finte accende la barra del
        // primo account Microsoft e apre la finestra con i dati finti; Salva e Disattiva vanno solo nel registro
        if (cli.Contains("--risposte-finte") && accounts.FirstOrDefault(a => a.Provider.HasAutoReplies) is { } withReplies)
        {
            AutoRepliesWindow.TestSample = AutoRepliesWindow.Sample();
            withReplies.AutoReplies = AutoRepliesWindow.TestSample;
            _ = Dispatcher.BeginInvoke(() => _ = EditAutoRepliesAsync(withReplies.Provider.Account, this), DispatcherPriority.ApplicationIdle);
        }
        // Regole: --regole [indirizzo] apre Impostazioni, Posta, Regole (sull'account dato); --regole-finte mostra per il primo
        // account Microsoft tre regole di Outlook finte, fra cui un inoltro fuori (nella prova senza consenso non si leggono);
        // --crea-regola 2 apre Crea regola sul messaggio 2 dell'elenco. Salva, Spegni ed Esegui ora vanno solo nel registro.
        if (cli.Contains("--regole-finte") && accounts.FirstOrDefault(a => a.Provider.HasServerRules) is { } withRules)
        {
            for (int wait = 0; wait < 100 && withRules.Folders.Count == 0; wait++) await Task.Delay(100);
            RulesView.TestSample = RulesView.Sample(withRules.Provider.Account, withRules.Snapshot());
            RulesView.StartAccount ??= withRules.Provider.Account.Email;
        }
        // Una pagina delle impostazioni: --impostazioni "Account/Firme", "Posta/Scrittura e risposta"
        if (Arg("--impostazioni") is { } page && page.Split('/') is [var category, var section])
            _ = Dispatcher.BeginInvoke(() => OpenSettings(category, section), DispatcherPriority.ApplicationIdle);
        if (cli.Contains("--regole"))
        {
            if (Arg("--regole") is { } ruleAccount && ruleAccount.Contains('@')) RulesView.StartAccount = ruleAccount;
            _ = Dispatcher.BeginInvoke(() => OpenSettings("Posta", "Regole"), DispatcherPriority.ApplicationIdle);
        }
        if (int.TryParse(Arg("--crea-regola"), out var ruleRow) && ruleRow >= 0)
        {
            for (int wait = 0; wait < 100 && loading; wait++) await Task.Delay(100);
            if (ruleRow < rows.Rows.Count)
            {
                var row = rows.Rows[ruleRow];
                _ = Dispatcher.BeginInvoke(() => CreateRuleFrom(row), DispatcherPriority.ApplicationIdle);
            }
        }
        // Formato del testo: --formato-finto apre Nuovo messaggio con il testo formattato come con la barra (con MISSIVA_DUMP
        // la chiusura scrive l'HTML che partirebbe; con --foto quello delle foto della pagina GitHub); --rispondi 2 apre la
        // risposta al messaggio 2 (la firma delle risposte)
        if (cli.Contains("--formato-finto") && accounts.Count > 0)
        {
            var compose = new ComposeWindow(Providers.ToList(), folder?.Account.Provider ?? accounts[0].Provider, ComposeMode.New, null, null) { Owner = this };
            DevTools.HideForPhotos(compose); // con --foto non si vede e non prende il fuoco, e ha il testo delle foto della pagina GitHub
            compose.Loaded += (_, _) => { if (cli.Contains("--foto")) compose.TestPhoto(); else compose.TestFormat(); };
            ShowCompose(compose);
        }
        if (int.TryParse(Arg("--rispondi"), out var replyRow) && replyRow >= 0)
        {
            for (int wait = 0; wait < 100 && loading; wait++) await Task.Delay(100);
            if (replyRow < rows.Rows.Count) OpenCompose(rows.Rows[replyRow].Provider, rows.Rows[replyRow].Summary, ComposeMode.Reply, null);
        }
        // Persone: --gruppo-finto aggiunge ai contatti della copia dei dati il gruppo "Soci dello studio" (tre persone, una di
        // un altro dominio); --scheda-mittente 2 apre la scheda del mittente del messaggio 2, come il clic sul nome.
        if (cli.Contains("--gruppo-finto") && !ContactBook.Local.Any(c => c.IsGroup && c.DisplayName == "Soci dello studio"))
            ContactBook.Put(new Contact
            {
                IsGroup = true, DisplayName = "Soci dello studio",
                Members = ["Anna Bianchi <anna.bianchi@studio-prova.example>", "Luca Verdi <luca.verdi@studio-prova.example>", "Carla Neri <carla.neri@consulenti-prova.example>"],
            });
        if (int.TryParse(Arg("--scheda-mittente"), out var cardRow) && cardRow >= 0)
        {
            for (int wait = 0; wait < 100 && loading; wait++) await Task.Delay(100);
            if (cardRow < rows.Rows.Count) ShowPerson(rows.Rows[cardRow].Provider, rows.Rows[cardRow].Summary.From, this);
        }
        // Tutti gli account: --unified apre la loro Posta in arrivo (--unified Sent, Drafts, Unread, Flagged le altre viste)
        if (cli.Contains("--unified") && UnifiedPanel.Visibility == Visibility.Visible)
        {
            var which = Arg("--unified") is { } v && Enum.TryParse<UnifiedView>(v, out var parsed) ? parsed : UnifiedView.Inbox;
            SelectUnified(unifiedItems.First(u => u.View == which));
            await reloading;
        }
        // Ricerca: --search testo in tutte le cartelle, --search-here testo nella cartella aperta, --search-accounts testo
        // in tutti gli account
        if (Arg("--search-accounts") is { } across) { SetSearchScope(true, true); SearchBox.Text = across; await SearchAccountsAsync(across, _ => across); }
        // Filtri della ricerca: --filtri apre il pannello (con i filtri della ricerca di --search o --search-accounts)
        if (cli.Contains("--filtri")) _ = Dispatcher.BeginInvoke(ShowFilters, DispatcherPriority.ApplicationIdle);
        if (Arg("--search") is { } everywhere) { SearchBox.Text = everywhere; await SearchAsync(everywhere, null); }
        if (Arg("--search-here") is { } here) { SetSearchScope(false); SearchBox.Text = here; await SearchAsync(here, folder); }
        // Notifiche: --notify 2 mostra la notifica vera del messaggio 2 dell'elenco, --toast "argomenti" chiama il gestore
        // del clic (gli argomenti sono nel registro, alla riga "notifica:"), dopo che l'elenco di partenza si è visto
        if (int.TryParse(Arg("--notify"), out var shown)) TestNotify(shown);
        if (Arg("--toast") is { } toast)
        {
            await Task.Delay(3000);
            App.Current.OnToast(toast);
        }
        // Annulla: --delete 3 elimina il messaggio 3 dell'elenco solo nel registro, come la versione di prova, con la barra
        // di Annulla (nelle prove Elimina non si preme: la casella è quella vera)
        if (int.TryParse(Arg("--delete"), out var deleted) && deleted >= 0 && deleted < rows.Rows.Count)
        {
            for (int wait = 0; wait < 100 && loading; wait++) await Task.Delay(100); // l'elenco dal server, non la copia locale
            await Task.Delay(1500);
            MessageList.SelectedItem = rows.Rows[deleted];
            await DeleteAsync([rows.Rows[deleted]], simulate: true);
        }
        // Finestra del messaggio: --window-delete 3 apre il messaggio 3 in una finestra e, dopo 3 s, fa Elimina della sua
        // barra solo nel registro (la finestra si chiude, nella principale la barra di Annulla)
        if (int.TryParse(Arg("--window-delete"), out var inWindow) && inWindow >= 0 && inWindow < rows.Rows.Count)
        {
            for (int wait = 0; wait < 100 && loading; wait++) await Task.Delay(100);
            await Task.Delay(1500);
            var row = rows.Rows[inWindow];
            if (OpenInWindow(row) is { } messageWindow)
            {
                await Task.Delay(3000);
                await DeleteFromWindowAsync(row, messageWindow, simulate: true);
            }
        }
        // Trascinamento dei messaggi (di notte SendInput non arriva): --drop "2>Archivio" lascia il messaggio 2 sulla
        // cartella Archivio (prima dell'account aperto, poi degli altri) con lo stesso gestore del rilascio, solo nel
        // registro, --copy come con Ctrl; --drag-over Archivio evidenzia la cartella come durante il trascinamento
        FolderItem? FolderNamed(string name) => accounts.OrderByDescending(a => a == folder?.Account).SelectMany(a => a.Folders)
            .FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (Arg("--drop") is { } drop && drop.Split('>', 2) is [var at, var name] && int.TryParse(at, out var dropped) &&
            dropped >= 0 && dropped < rows.Rows.Count && FolderNamed(name) is { } dropOn)
        {
            for (int wait = 0; wait < 100 && loading; wait++) await Task.Delay(100);
            await Task.Delay(1500);
            MessageList.SelectedItem = rows.Rows[dropped];
            await DropMessagesAsync(dropOn, [rows.Rows[dropped]], copy: cli.Contains("--copy"), simulate: true);
        }
        if (Arg("--drag-over") is { } overName && FolderNamed(overName) is { } over) MarkDropTarget(over);
        // Menu delle cartelle su Posta in arrivo: Missiva.exe --folder-menu; su un'altra cartella: --folder-menu "Tutti i messaggi"
        if (Environment.GetCommandLineArgs().Contains("--folder-menu") && folder != null && FindLists().FirstOrDefault() is { ContextMenu: { } folderMenu } folderList)
        {
            await Task.Delay(1500);
            var menuOn = Arg("--folder-menu") is { } menuName && !menuName.StartsWith("--") && FolderNamed(menuName) is { } named ? named : folder;
            BuildFolderMenu(folderMenu, menuOn, fromFavorites: false);
            folderMenu.PlacementTarget = folderList;
            folderMenu.IsOpen = true;
        }
        if (Environment.GetCommandLineArgs().Contains("--menu") && rows.Rows.Count > 0)
        {
            MessageList.SelectedIndex = 0;
            await Task.Delay(1500);
            BuildMessageMenu(MessageList.ContextMenu!, Selected);
            MessageList.ContextMenu!.PlacementTarget = MessageList;
            MessageList.ContextMenu.IsOpen = true;
            // --menu Posponi: apre anche quel sottomenu
            var open = Environment.GetCommandLineArgs().SkipWhile(a => a != "--menu").Skip(1).FirstOrDefault();
            if (open != null && MessageList.ContextMenu.Items.OfType<MenuItem>()
                    .FirstOrDefault(i => (i.Header as string ?? (i.Header as TextBlock)?.Text) == open) is { } sub)
            {
                await Task.Delay(300);
                sub.IsSubmenuOpen = true;
            }
        }
#endif
    }

    IEnumerable<IMailProvider> Providers => accounts.Select(a => a.Provider);

    readonly TaskCompletionSource rendered = new();

    /// <summary>Le finestre di scrittura sono chiuse (o si stanno chiudendo, con le loro domande): ora si può chiudere questa.</summary>
    bool composeClosed, closingCompose;

    /// <summary>
    /// Chiudendo la finestra proprietaria WPF chiude le finestre di scrittura senza il loro Closing: niente "Salvare la
    /// bozza?" e il testo spariva. Si chiudono prima loro, una alla volta, come con la X (con Salva bozza la finestra aspetta
    /// il salvataggio); con Annulla, o se una bozza non si salva, Missiva resta aperta. La finestra principale chiude anche
    /// quelle delle finestre di una sola cartella, che si chiudono con lei. Poi, come Outlook classico, la finestra
    /// principale non si chiude ma va nell'area di notifica (HideToTray), tranne con Esci o con l'opzione spenta.
    /// </summary>
    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!composeClosed)
        {
            // Verso l'area di notifica la Posta in uscita resta com'è: le sue scritture sono nascoste e partono al ritorno della
            // rete. Uscendo davvero si riaprono, con le loro domande (CloseForOwnerAsync).
            bool toTray = only == null && S.KeepInTray && !App.Current.Exiting;
            var compose = (only == null ? Application.Current.Windows.OfType<ComposeWindow>() : OwnedWindows.OfType<ComposeWindow>())
                .Where(w => !(toTray && w.InOutbox)).ToList();
            if (compose.Count > 0)
            {
                e.Cancel = true;
                if (closingCompose) return; // le domande sono già in corso
                closingCompose = true;
                // Dopo: dentro Closing WPF non accetta un'altra Close di questa finestra.
                Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        foreach (var window in compose)
                            if (!await window.CloseForOwnerAsync(keepOutbox: toTray))
                            {
                                if (only == null) App.Current.CancelExit(); // Annulla: Missiva resta, anche con Esci
                                return;
                            }
                        composeClosed = true;
                        Close();
                    }
                    finally
                    {
                        closingCompose = false;
                    }
                });
                return;
            }
        }
        composeClosed = false;
        if (only == null && S.KeepInTray && !App.Current.Exiting)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(HideToTray); // fuori da Closing
        }
    }

    // ---------------------------------------------------------------- area di notifica

    /// <summary>Cambia a ogni chiusura e riapertura: la raccolta della memoria rimandata vale solo se la finestra è ancora chiusa.</summary>
    int hiddenVersion;

    /// <summary>
    /// Chiudendo, come Outlook classico: la finestra si nasconde e Missiva resta nell'area di notifica con i provider, il
    /// controllo dei nuovi messaggi, le notifiche, gli invii pianificati, Posponi e i promemoria. Le altre finestre (messaggi,
    /// una sola cartella, allegati) si chiudono; WebView2 si chiude davvero con i suoi processi (ReleaseWeb) e dopo qualche
    /// secondo una raccolta compattata restituisce a Windows la memoria. La prima volta una notifica lo spiega.
    /// </summary>
    void HideToTray()
    {
        // Non le scritture nella Posta in uscita: nascoste, partono al ritorno della rete anche da qui.
        foreach (var window in Application.Current.Windows.OfType<Window>().Where(w => w != this && w is not ComposeWindow { InOutbox: true }).ToList())
        {
            try { window.Close(); }
            catch (Exception ex) { Log.Error("chiusura di una finestra verso l'area di notifica", ex); }
        }
        CloseSuggestions(); // i riquadri a comparsa sono finestre a parte: resterebbero sullo schermo
        JumpPopup.IsOpen = false;
        Hide();
        App.WindowHidden = true;
        App.Current.UpdateTray();
        Reader.ReleaseWeb();
        // Senza la scheda video mentre è nascosta: WPF rilascia il dispositivo Direct3D e la memoria del driver (Debug: 81 MB
        // privati dopo 10 s invece di 123); ShowFromTray rimette il disegno normale prima di mostrarla.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var envelopes = Pec.ForgetOpened(); // le buste PEC già lette: fino a 64 MB che nell'area di notifica non servono
        Log.Write("finestra chiusa nell'area di notifica: WebView2 chiuso, Missiva resta attiva" +
                  (envelopes.Count > 0 ? $"; {envelopes.Count} buste PEC lasciate andare ({envelopes.Bytes / 1024 / 1024} MB)" : ""));
        Store.Flush(); // le impostazioni in attesa di SaveSoon: da qui Missiva può restare aperta per giorni, o essere chiusa da Windows
        if (!S.TrayExplained)
        {
            S.TrayExplained = true;
            Store.Save();
            try
            {
                App.Show(new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                    .AddText("Missiva è ancora aperta nell'area di notifica")
                    .AddText("Continua a controllare la posta, a mostrarti le notifiche e a mandare i messaggi pianificati. " +
                             "Per chiuderla del tutto: tasto destro sull'icona vicino all'orologio, Esci. Si cambia in Impostazioni, Generali."));
            }
            catch (Exception ex)
            {
                Log.Error("notifica dell'area di notifica", ex);
            }
        }
        _ = CompactLaterAsync(++hiddenVersion);
    }

    void LayOutHidden()
    {
        if (IsVisible || Content is not FrameworkElement content) return;
        var clock = Stopwatch.StartNew();
        var size = new Size(Width, Height);
        content.Measure(size);
        content.Arrange(new Rect(size));
        Log.Time("finestra disposta senza mostrarla", clock);
    }

    async Task CompactLaterAsync(int version)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (version != hiddenVersion || IsVisible) return;
        var clock = Stopwatch.StartNew();
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        var info = GC.GetGCMemoryInfo();
        Log.Time($"memoria compattata nell'area di notifica (heap .NET {info.HeapSizeBytes / 1_048_576} MB, impegnata {info.TotalCommittedBytes / 1_048_576} MB)", clock);
    }

    /// <summary>
    /// Dall'area di notifica (clic sull'icona, Apri, "apri" da una seconda copia, clic su una notifica, link mailto:): la
    /// finestra torna com'era, con la cartella di prima; WebView2 si riprepara in sottofondo.
    /// </summary>
    public void ShowFromTray()
    {
        var clock = Stopwatch.StartNew();
        bool wasHidden = !IsVisible;
        CheckDay(); // riaperta dopo mezzanotte (o dopo una sospensione del PC): i gruppi giusti prima di mostrarla
        if (wasHidden) Log.Startup("apertura dall'area di notifica");
        hiddenVersion++;
        App.WindowHidden = false;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default; // di nuovo con la scheda video (HideToTray)
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        App.Current.UpdateTray();
        if (!wasHidden) return;
        EventHandler? drawing = null;
        drawing = (_, _) =>
        {
            CompositionTarget.Rendering -= drawing;
            Log.Time("finestra dall'area di notifica, nel disegno", clock);
        };
        CompositionTarget.Rendering += drawing;
        // Avviata nascosta, le cartelle non erano ancora disegnate quando si è scelta la Posta in arrivo.
        if (folder != null) Dispatcher.BeginInvoke(() => { if (folder != null) MarkSelectedFolder(folder); }, DispatcherPriority.Loaded);
        if (S.ReadingPane != ReadingPanePosition.Off)
        {
            // Il messaggio che era aperto torna nel riquadro (WebView2 si era chiuso con la finestra); altrimenti WebView2 si
            // prepara in sottofondo per il primo clic.
            if (MessageList.SelectedItems.Count == 1 && MessageList.SelectedItem is MessageItem item) _ = ShowInReaderAsync(item);
            else Dispatcher.BeginInvoke(Reader.Prepare, DispatcherPriority.ApplicationIdle);
        }
        Log.Time("finestra dall'area di notifica", clock);
    }

#if DEBUG
    /// <summary>Prove (MISSIVA_KEYS "notifica 2", --notify 2): la notifica vera del messaggio dell'elenco, anche a finestra chiusa.</summary>
    internal void TestNotify(int index)
    {
        if (folder != null && index >= 0 && index < rows.Rows.Count) App.Current.Notify(folder.Account.Provider, [rows.Rows[index].Summary]);
    }
#endif

    /// <summary>Nuovo messaggio dal menu dell'icona: la finestra e la scrittura, dall'account aperto.</summary>
    public void NewFromTray()
    {
        ShowFromTray();
        OnNew(this, new RoutedEventArgs());
    }

    // ---------------------------------------------------------------- impostazioni e disposizione

    /// <summary>Applica le impostazioni di visualizzazione (dopo Impostazioni o il menu Visualizza).</summary>
    public void ApplySettings(bool reload = true)
    {
        ApplyLayout();
        view.GroupDescriptions.Clear();
        if (S.ShowDateHeaders) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MessageItem.Group)));
        LightToggle.IsChecked = S.LightReadingPane;
        CommandOverflow.SetAbsent(LightToggle, !ReadingView.Dark);
        ShowZoom();
        FitBars();
        Reader.Refresh();
        UpdateUnified(); // "Tutti gli account" acceso o spento: aperto e spento, si torna alla Posta in arrivo
        if (reload && folder != null) SelectFolder(folder); // le righe si ricreano con le nuove opzioni
        else if (reload && unified != null) SelectUnified(unified);
    }

    /// <summary>Lo zoom del riquadro sul pulsante Zoom; anche quando cambia da Ctrl+rotellina nel riquadro (ReadingView.ZoomChanged).</summary>
    void ShowZoom() => ZoomText.Text = S.Zoom == 100 ? "Zoom" : $"Zoom {S.Zoom}%";

    /// <summary>
    /// Il logo di un dominio è cambiato in sottofondo (SenderLogos: arrivato, dominio appena autenticato, o un messaggio
    /// che ora non lo può mostrare): le righe di quel dominio rileggono il loro, al posto delle iniziali o viceversa.
    /// </summary>
    void OnLogo(string domain)
    {
        foreach (var row in rows.Rows)
            if (row.IsOfDomain(domain)) row.RefreshLogo();
    }

    /// <summary>
    /// Il tema di Windows è cambiato con Missiva aperta. La finestra la ridisegna WPF, il riquadro di lettura si ridisegna
    /// da solo (ReadingView.OnThemeChanged); qui il resto: i colori che righe e cartelle prendono dalle risorse,
    /// "Riquadro di lettura chiaro" (c'è solo nel tema scuro) e i menu del tasto destro.
    /// </summary>
    void OnThemeChanged()
    {
        CommandOverflow.SetAbsent(LightToggle, !ReadingView.Dark);
        FitBars();
        foreach (var row in rows.Rows) row.RefreshTheme();
        foreach (var f in accounts.SelectMany(a => a.Folders).Concat(favorites)) f.RefreshColor();
        // Dopo che WPF ha rimesso il modello della finestra del tema nuovo: intanto la finestra è senza figli visibili.
        Dispatcher.BeginInvoke(RefreshMenus, DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(AttachListScrolling, DispatcherPriority.Loaded); // lo ScrollViewer dell'elenco è un altro
    }

    /// <summary>
    /// I menu del tasto destro non stanno nell'albero della finestra: WPF non li aggiorna al cambio di tema e resterebbero
    /// del tema di prima, anche aperti più tardi. Si sostituiscono con menu nuovi (le voci si creano all'apertura); quello
    /// dei messaggi, se è aperto, si riapre con le icone del tema nuovo. Solo i menu di Missiva (elenco, cartelle, Preferiti,
    /// account), messi nella finestra o nei suoi modelli: quello di Cerca (Taglia, Copia, Incolla) viene dallo stile Fluent,
    /// che al cambio di tema lo aggiorna da solo, e un menu messo qui lo lascerebbe vuoto.
    /// </summary>
    void RefreshMenus()
    {
        static bool Ours(FrameworkElement e) => e.ContextMenu != null &&
            DependencyPropertyHelper.GetValueSource(e, ContextMenuProperty).BaseValueSource is BaseValueSource.Local or BaseValueSource.ParentTemplate;
        var owners = VisualDescendants(this).OfType<FrameworkElement>().Where(Ours).ToList();
#if DEBUG
        Log.Write("menu del tasto destro rifatti: " + string.Join(", ", owners.Select(o => o.Name.Length > 0 ? o.Name : o.GetType().Name)));
#endif
        foreach (var owner in owners)
        {
            bool open = owner.ContextMenu!.IsOpen;
            owner.ContextMenu.IsOpen = false;
            owner.ContextMenu = new ContextMenu();
            if (!open || owner != MessageList || !BuildMessageMenu(MessageList.ContextMenu!, Selected)) continue;
            MessageList.ContextMenu.PlacementTarget = MessageList;
            MessageList.ContextMenu.IsOpen = true;
        }
    }

    static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (int k = 0; k < VisualTreeHelper.GetChildrenCount(parent); k++)
        {
            var child = VisualTreeHelper.GetChild(parent, k);
            yield return child;
            foreach (var inner in VisualDescendants(child)) yield return inner;
        }
    }

    /// <summary>Riquadro di lettura a destra, in basso o nascosto, come in Visualizza, Riquadro di lettura.</summary>
    void ApplyLayout()
    {
        WorkArea.ColumnDefinitions.Clear();
        WorkArea.RowDefinitions.Clear();
        foreach (UIElement e in new UIElement[] { ListCard, WorkSplitter, ReadingCard })
        {
            Grid.SetRow(e, 0);
            Grid.SetColumn(e, 0);
        }
        WorkSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
        WorkSplitter.VerticalAlignment = VerticalAlignment.Stretch;
        WorkSplitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;

        switch (S.ReadingPane)
        {
            case ReadingPanePosition.Right:
                WorkArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400), MinWidth = 280 });
                WorkArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                WorkArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
                WorkSplitter.ResizeDirection = GridResizeDirection.Columns;
                Grid.SetColumn(WorkSplitter, 1);
                Grid.SetColumn(ReadingCard, 2);
                break;
            case ReadingPanePosition.Bottom:
                WorkArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 160 });
                WorkArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
                WorkArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.4, GridUnitType.Star), MinHeight = 200 });
                WorkSplitter.ResizeDirection = GridResizeDirection.Rows;
                Grid.SetRow(WorkSplitter, 1);
                Grid.SetRow(ReadingCard, 2);
                break;
        }
        bool pane = S.ReadingPane != ReadingPanePosition.Off;
        WorkSplitter.Visibility = ReadingCard.Visibility = pane ? Visibility.Visible : Visibility.Collapsed;
        FitColumns();
    }

    // ---------------------------------------------------------------- finestra che si adatta

    /// <summary>Sotto questa larghezza le cartelle si chiudono e si aprono sopra l'elenco, come nel nuovo Outlook.</summary>
    const double CompactWidth = 1100;
    const double ListMinWidth = 280, ReadingMinWidth = 320, FolderMinWidth = 180;
    /// <summary>Nella finestra stretta l'elenco si stringe fin qui prima del riquadro di lettura, poi fino a ListMinWidth.</summary>
    const double ListShrinkWidth = 300;

    bool compact;
    /// <summary>Finestra stretta: le cartelle sono aperte sopra l'elenco.</summary>
    bool foldersOver;
    /// <summary>Finestra larga: le cartelle chiuse con il pulsante delle cartelle.</summary>
    bool foldersClosed;
    double folderWidth = 250;
    /// <summary>Larghezza dell'elenco scelta trascinando il divisore: torna appena la finestra ha di nuovo posto.</summary>
    double listWidth = 400;
    bool listDragged;

    void OnWindowSize(object sender, SizeChangedEventArgs e)
    {
        if (only == null && ActualWidth < CompactWidth != compact)
        {
            compact = !compact;
            foldersOver = false;
            ShowFolders();
        }
        FitMaximized();
        // La ricerca resta al centro senza coprire il nome né finire sotto i pulsanti di sistema e quelli della barra del
        // titolo (aggiornamento: MainWindow.Updates.cs, FitTitleBar).
        FitTitleBar();
        FitColumns();
    }

    /// <summary>Larghezza dei pulsanti di sistema (riduci, ingrandisci, chiudi) disegnati da Windows nella barra del titolo.</summary>
    double CaptionButtonsWidth()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && DwmGetWindowAttribute(handle, 5 /* DWMWA_CAPTION_BUTTON_BOUNDS */, out var r, System.Runtime.InteropServices.Marshal.SizeOf<Rect32>()) == 0 && r.Right > r.Left)
            return (r.Right - r.Left) / VisualTreeHelper.GetDpi(this).DpiScaleX;
        return 138;
    }

    /// <summary>
    /// Ingrandita, la finestra sborda dallo schermo di qualche pixel per lato (il bordo invisibile di Windows): con la barra
    /// del titolo disegnata da Missiva il contenuto rientra di altrettanto, e la parte trascinabile segue.
    /// </summary>
    void FitMaximized()
    {
        var margin = new Thickness(0);
        var handle = new WindowInteropHelper(this).Handle;
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (WindowState == WindowState.Maximized && handle != IntPtr.Zero && GetWindowRect(handle, out var r) &&
            GetMonitorInfo(MonitorFromWindow(handle, 2 /* MONITOR_DEFAULTTONEAREST */), ref info))
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            margin = new Thickness(Math.Max(0, info.Work.Left - r.Left) / scale, Math.Max(0, info.Work.Top - r.Top) / scale,
                                   Math.Max(0, r.Right - info.Work.Right) / scale, Math.Max(0, r.Bottom - info.Work.Bottom) / scale);
        }
        if (Root.Margin == margin) return;
        Root.Margin = margin;
        if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome) chrome.CaptionHeight = TitleBar.Height + margin.Top;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Rect32 { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MonitorInfo { public int Size; public Rect32 Monitor, Work; public uint Flags; }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect32 value, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Point32 { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetCursorPos(out Point32 point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>
    /// Cartelle affiancate all'elenco nella finestra larga; in quella stretta chiuse, e aperte dal pulsante delle
    /// cartelle in un riquadro sopra l'elenco, che si richiude scegliendo una cartella o facendo clic altrove.
    /// </summary>
    void ShowFolders()
    {
        if (calendarShown || peopleShown) return; // le cartelle si rimettono tornando alla posta (ShowCalendar, ShowPeople)
        bool docked = only == null && !compact && !foldersClosed;
        bool over = only == null && compact && foldersOver;
        FolderPane.Visibility = docked || over ? Visibility.Visible : Visibility.Collapsed;
        FolderColumn.Width = new GridLength(docked ? folderWidth : 0);
        FolderSplitter.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(FolderPane, over ? 5 : 1);
        FolderPane.HorizontalAlignment = over ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        if (!over) FolderPane.Width = double.NaN; // aperte sopra l'elenco, la larghezza la decide FitColumns
        FolderPane.Padding = over ? new Thickness(8, 8, 4, 8) : new Thickness(0);
        FolderPane.BorderThickness = new Thickness(over ? 1 : 0);
        if (over)
        {
            FolderPane.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
            FolderPane.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorDefaultBrush");
            FolderPane.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35, Direction = 270 };
        }
        else
        {
            FolderPane.ClearValue(Border.BackgroundProperty);
            FolderPane.ClearValue(Border.BorderBrushProperty);
            FolderPane.Effect = null;
        }
        // WebView2 si disegna sopra tutto: con il riquadro in basso coprirebbe le cartelle aperte sopra l'elenco.
        Reader.Visibility = over && S.ReadingPane == ReadingPanePosition.Bottom ? Visibility.Hidden : Visibility.Visible;
        FitColumns();
    }

    void CloseFoldersOver()
    {
        if (!foldersOver) return;
        foldersOver = false;
        ShowFolders();
    }

    /// <summary>Un clic fuori dalle cartelle aperte sopra l'elenco le richiude.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        var source = e.OriginalSource as DependencyObject;
        if (foldersOver && !IsInside(source, FolderPane) && !IsInside(source, FoldersButton)) CloseFoldersOver();
    }

    static bool IsInside(DependencyObject? d, DependencyObject container)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == container) return true;
        return false;
    }

    void OnFolderSplitterDragged(object sender, DragCompletedEventArgs e)
    {
        if (FolderColumn.ActualWidth > 0) folderWidth = FolderColumn.ActualWidth;
        FitColumns();
    }

    /// <summary>Le cartelle affiancate cambiano larghezza (divisore, anche da tastiera): l'elenco si rifà i conti.</summary>
    void OnFolderPaneSize(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && FolderSplitter.Visibility == Visibility.Visible) FitColumns();
    }

    void OnWorkSplitterDragged(object sender, DragCompletedEventArgs e)
    {
        if (S.ReadingPane != ReadingPanePosition.Right) return;
        listWidth = WorkArea.ColumnDefinitions[0].ActualWidth;
        listDragged = true;
    }

    /// <summary>
    /// Elenco e riquadro di lettura sempre dentro la finestra: l'elenco tiene la sua larghezza finché il riquadro è più
    /// largo di lui e ha il suo minimo, poi si stringe fino al proprio. Lo spazio si calcola dalla finestra: quando i minimi non ci
    /// stanno WPF allarga gli elementi oltre il bordo, e la loro ActualWidth non dice più quanto se ne vede.
    /// </summary>
    /// <remarks>
    /// Anche le cartelle hanno un limite: il loro divisore non può togliere all'elenco e al riquadro i loro minimi. Aperte
    /// sopra l'elenco non vanno oltre l'elenco, perché WebView2 si disegna sopra tutto e ne coprirebbe la parte destra.
    /// </remarks>
    void FitColumns()
    {
        if (Content is not FrameworkElement root) return;
        double client = LayoutInformation.GetLayoutSlot(root).Width - root.Margin.Left - root.Margin.Right;
        if (client <= 0) return;
        bool right = S.ReadingPane == ReadingPanePosition.Right && WorkArea.ColumnDefinitions.Count >= 3;
        var content = (Grid)WorkArea.Parent;
        double beside = client - content.Margin.Left - content.Margin.Right - content.ColumnDefinitions[0].Width.Value
                        - (FolderSplitter.Visibility == Visibility.Visible ? FolderSplitter.Width : 0);
        FolderColumn.MaxWidth = Math.Max(FolderMinWidth, beside - ListMinWidth - (right ? 8 + ReadingMinWidth : 0));
        double work = beside - Math.Min(FolderColumn.Width.Value, FolderColumn.MaxWidth);
        if (right)
        {
            // Come nel nuovo Outlook il riquadro di lettura è la colonna più larga: nella finestra stretta l'elenco non lo
            // supera e si stringe prima lui, fino a ListShrinkWidth; poi il riquadro fino al suo minimo, poi di nuovo
            // l'elenco. La larghezza scelta trascinando il divisore vale com'è, finché il riquadro ha il suo minimo.
            // Il riquadro perde 26 DIP fra bordo e margini attorno alla pagina (ReadingView.Paper): a metà l'elenco sembrerebbe più largo.
            double avail = work - 8;
            double list = listDragged ? listWidth : Math.Max(Math.Min(listWidth, (avail - 26) / 2), Math.Min(ListShrinkWidth, listWidth));
            WorkArea.ColumnDefinitions[0].Width = new GridLength(Math.Max(ListMinWidth, Math.Min(list, avail - ReadingMinWidth)));
        }
        if (only == null && compact && foldersOver)
            FolderPane.Width = right ? Math.Min(folderWidth + 12, WorkArea.ColumnDefinitions[0].Width.Value) : folderWidth + 12;
    }

    void OnBarSize(object sender, SizeChangedEventArgs e) => FitBars();

    /// <summary>I comandi che non entrano nella barra vanno nel menu "..." (Altre opzioni).</summary>
    void FitBars()
    {
        if (HomeBar.Visibility == Visibility.Visible && HomeBar.ActualWidth > 0)
            CommandOverflow.Fit(HomeCommands, HomeMore, HomeBar.ActualWidth - HomeRefresh.DesiredSize.Width);
        if (ViewBar.Visibility == Visibility.Visible && ViewBar.ActualWidth > 0)
            CommandOverflow.Fit(ViewCommands, ViewMore, ViewBar.ActualWidth - ViewRefresh.DesiredSize.Width);
    }

    void OnMoreCommands(object sender, RoutedEventArgs e)
    {
        if (sender == HomeMore) CommandOverflow.ShowMenu(HomeCommands, HomeMore);
        else CommandOverflow.ShowMenu(ViewCommands, ViewMore);
    }

    /// <summary>Dove aprire il menu di un comando: sotto il suo pulsante, o sotto "..." se il pulsante è lì dentro.</summary>
    FrameworkElement Anchor(FrameworkElement button) => CommandOverflow.Anchor(button, HomeBar.Visibility == Visibility.Visible ? HomeMore : ViewMore);

    /// <remarks>
    /// Le scelte di Visualizza sono preferenze: si salvano fra poco (Store.SaveSoon, scritte comunque all'uscita, a fine
    /// sessione e chiudendo nell'area di notifica), così la scrittura del file non sta fra il clic e l'elenco rifatto. Lo
    /// stesso per Fissa, il colore, l'ordine e le cartelle nascoste, "Mostra immagini" e la vista del calendario (audit del
    /// 7 ottobre, B-19). Account, accessi, contrassegni, posposti, regole e le caselle delle Impostazioni restano subito.
    /// </remarks>
    void SaveAndApply(bool reload = true)
    {
        Store.SaveSoon();
        ApplySettings(reload);
    }

    // ---------------------------------------------------------------- account e cartelle

    /// <remarks>
    /// Chi aspetta gli account (accountsListed: l'azione di una notifica, un link mailto:) o le cartelle (accountsReady: il
    /// clic su una notifica, il promemoria di un contrassegno) riparte anche se qui un passo fallisce: prima restava ad
    /// aspettare per sempre, e Missiva avviata da Windows per un'azione di una notifica non la faceva e non usciva più
    /// (audit dell'11 ottobre, C-25). Trova gli account che ci sono, e se il suo manca non fa nulla.
    /// </remarks>
    public async Task LoadAccountsAsync()
    {
        try
        {
            await ListAccountsAsync();
        }
        finally
        {
            accountsListed.TrySetResult();
            accountsReady.TrySetResult();
        }
    }

    async Task ListAccountsAsync()
    {
        if (only == null)
        {
            // Gli account cambiano: le finestre di una sola cartella usano i provider che stanno per chiudersi.
            foreach (var w in folderWindows.ToList()) w.Close();
            folderWindows.Clear();
            foreach (var existing in accounts) (existing.Provider as IDisposable)?.Dispose();
        }
        accounts.Clear();
        folder = null;
        unified = null;
        bool starting = !accountsListed.Task.IsCompleted && only == null;
        foreach (var account in S.Accounts)
        {
            if (only != null && account.Id != only.Value.AccountId) continue;
            // La finestra di una sola cartella usa le stesse connessioni della principale.
            IMailProvider provider = only != null && App.Current.ProviderFor(account.Id) is { } shared ? shared : NewProvider(account);
            accounts.Add(new AccountItem(provider));
        }
        if (starting) Log.Startup("provider creati");
        if (only == null) App.Current.SetProviders(Providers.ToList());
        // Le azioni rimaste in coda senza rete (anche da una Missiva chiusa così): sotto l'account, finché la rete non torna.
        if (only == null) foreach (var account in accounts) UpdateQueued(account);
        UpdateUnified();
        accountsListed.TrySetResult();
        if (starting) Log.Startup("account");

        // Subito la copia locale: cartelle e Posta in arrivo si vedono prima che il server risponda.
        foreach (var account in accounts)
        {
            if (MailCache.LoadFolders(account.Provider.Account) is not { Count: > 0 } cached) continue;
            account.SetFolders(cached);
            account.Status = "";
            WatchInboxes(account);
        }
        UpdateFavorites();
        if (starting) Log.Startup("cartelle dalla copia locale");
        SelectStart();
#if DEBUG || PROVA
        if (starting)
        {
            Log.Startup("Posta in arrivo scelta");
            // Il primo disegno dopo questo mostra l'elenco della copia locale.
            EventHandler? drawing = null;
            drawing = (_, _) =>
            {
                System.Windows.Media.CompositionTarget.Rendering -= drawing;
                Log.Startup($"elenco nel disegno ({VisualDescendants(MessageList).OfType<ListBoxItem>().Count()} righe create di {rows.Rows.Count})");
                Log.StartupFlush();
            };
            System.Windows.Media.CompositionTarget.Rendering += drawing;
            // Nascosta nell'area di notifica nessun disegno arriva: le righe dell'avvio si scrivono quando l'interfaccia è ferma.
            if (!IsVisible) _ = Dispatcher.BeginInvoke(() => Log.StartupFlush(), DispatcherPriority.ApplicationIdle);
        }
#endif

        await Task.WhenAll(accounts.Select(LoadFoldersAsync));
        if (folder == null) SelectStart();
        accountsReady.TrySetResult();
    }

#if DEBUG
    /// <summary>Prove (--modello-finto): il modello "Preventivo", solo nella versione di prova, sulla copia dei dati.</summary>
    void AddTestTemplate()
    {
        if (Environment.GetEnvironmentVariable("MISSIVA_NO_READ") != "1" || S.Templates.Any(t => t.Name == "Preventivo di prova")) return;
        // Come l'editor delle Impostazioni: Segoe UI 14.
        var doc = new System.Windows.Documents.FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 14 };
        var greeting = new System.Windows.Documents.Paragraph();
        greeting.Inlines.Add(new System.Windows.Documents.Run("Gentile {nome}, in allegato il "));
        greeting.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run("preventivo")));
        greeting.Inlines.Add(new System.Windows.Documents.Run(" del {data}. Riepilogo:"));
        doc.Blocks.Add(greeting);
        var list = new System.Windows.Documents.List();
        list.ListItems.Add(new System.Windows.Documents.ListItem(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Impianto e posa: 1.200 euro"))));
        list.ListItems.Add(new System.Windows.Documents.ListItem(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Pagamento a 30 giorni"))));
        doc.Blocks.Add(list);
        doc.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Condizioni generali"))
            { NavigateUri = new Uri("https://studio-prova.example/condizioni") }));
        using var stream = new MemoryStream();
        new System.Windows.Documents.TextRange(doc.ContentStart, doc.ContentEnd).Save(stream, DataFormats.Rtf);
        S.Templates.Add(new MailTemplate { Name = "Preventivo di prova", Subject = "Preventivo del {data}", Rtf = System.Text.Encoding.UTF8.GetString(stream.ToArray()) });
        Store.Save();
    }

    /// <summary>Prove: mario.rossi@studio-prova.it (lavoro, mai collegato) e la sua casella condivisa info@studio-prova.it.</summary>
    void AddTestSharedMailbox()
    {
        if (Environment.GetEnvironmentVariable("MISSIVA_NO_READ") != "1") return; // solo nella versione di prova, sulla copia dei dati
        var work = S.Accounts.FirstOrDefault(a => a.Email == "mario.rossi@studio-prova.it");
        if (work == null)
        {
            work = new MailAccount { Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.it", DisplayName = "Mario Rossi", Tenant = MicrosoftAuth.Organizations };
            S.Accounts.Add(work);
        }
        if (!S.Accounts.Any(a => a.SharedOf == work.Id))
            S.Accounts.Add(new MailAccount { Kind = AccountKind.Microsoft, Email = "info@studio-prova.it", SharedOf = work.Id, Tenant = work.Tenant });
        Store.Save();
    }
#endif

    /// <summary>
    /// Il provider dell'account. Nella versione di prova (MISSIVA_NO_READ) dentro TestingProvider: le letture arrivano alla
    /// casella vera, le scritture solo al registro, anche quelle che nessuno ha pensato di simulare.
    /// </summary>
    static IMailProvider NewProvider(MailAccount account)
    {
        IMailProvider provider = account.Kind switch
        {
            AccountKind.Microsoft => new GraphProvider(account),
            AccountKind.Gmail => new GmailProvider(account),
            _ => new ImapProvider(account),
        };
#if DEBUG || PROVA
        if (Testing)
        {
            provider = new TestingProvider(provider);
            Log.Write($"prova: le scritture di {account.Email} vanno solo nel registro");
        }
#endif
        return provider;
    }

    /// <summary>Account e cartelle arrivati dal server: il clic su una notifica, all'avvio, li aspetta.</summary>
    readonly TaskCompletionSource accountsReady = new();
    /// <summary>Gli account sono nell'elenco, anche prima delle cartelle: basta per scrivere (OpenMailtoAsync).</summary>
    readonly TaskCompletionSource accountsListed = new();

    /// <summary>
    /// Il clic su una notifica, come nel nuovo Outlook: la Posta in arrivo di quell'account e, per un messaggio solo, il
    /// messaggio selezionato e aperto nel riquadro. Se nell'elenco non c'è (oltre la prima pagina, o in Altra) si apre
    /// comunque nel riquadro, dal riepilogo della notifica.
    /// </summary>
    public async Task OpenFromNotificationAsync(string accountId, string? messageId, MessageSummary? summary, string? folderId = null)
    {
        await accountsReady.Task;
        if (accounts.FirstOrDefault(a => a.Provider.Account.Id == accountId) is not { } account ||
            account.Folders.FirstOrDefault(f => f.Folder.Kind == FolderKind.Inbox) is not { } inbox) return;
        // La cartella del messaggio (dalla sua finestra), altrimenti la Posta in arrivo (le notifiche, "N nuovi messaggi").
        var target = folderId != null ? account.Folders.FirstOrDefault(f => f.Folder.Id == folderId) ?? inbox : inbox;
        if (folder != target || searching) SelectFolder(target);
        await reloading;
        if (messageId == null) return;
        bool Wanted(MessageItem r) => r.Summary.Id == messageId && (folderId == null || r.Summary.FolderId == folderId);
        Log.Write("notifica aperta: " + (rows.Rows.Any(Wanted) ? "messaggio nell'elenco" : "messaggio fuori dall'elenco"));
        if (rows.Rows.FirstOrDefault(Wanted) is { } row)
        {
            MessageList.SelectedItem = row;
            MessageList.ScrollIntoView(row);
            MessageList.Focus();
        }
        else if (S.ReadingPane != ReadingPanePosition.Off && (summary ?? FindNotified(account, folderId, messageId)) is { } found)
        {
            // Fuori dall'elenco (la riga non c'è ancora, o è più giù delle pagine caricate): nel riquadro di lettura.
            MessageList.SelectedItems.Clear();
            await Reader.ShowAsync(account.Provider, found, S.ReadingWholeConversation, JunkOf(account.Provider));
        }
    }

    /// <summary>All'avvio la Posta in arrivo; nella finestra di una sola cartella, quella.</summary>
    void SelectStart()
    {
        if (only is { } o)
        {
            if (accounts.SelectMany(a => a.Folders).FirstOrDefault(f => f.Folder.Id == o.FolderId) is { } target)
            {
                Title = target.Name + " - " + target.Account.Header;
                SelectFolder(target);
            }
            return;
        }
        SelectInbox();
    }

    void SelectInbox()
    {
        if (accounts.SelectMany(a => a.Folders).FirstOrDefault(f => f.Folder.Kind == FolderKind.Inbox) is not { } inbox) return;
        // All'avvio la cartella scelta si vede evidenziata: con l'albero dell'account chiuso l'elenco sembrava di un altro account.
        if (!inbox.Account.IsExpanded) inbox.Account.IsExpanded = true;
        SelectFolder(inbox);
    }

    async Task LoadFoldersAsync(AccountItem account)
    {
        try
        {
            if (account.Folders.Count == 0) account.Status = "Caricamento…";
            // Le cartelle arrivano con i loro non letti: se intanto qui sono cambiati (una lettura, una scrittura in viaggio)
            // quelli della Posta in arrivo restano i nostri, e si ricontano dal server (UnreadSync).
            var ticket = UnreadSync.Ticket(account.Provider.Account.Id);
            if (!account.SetServerFolders(await account.Provider.GetFoldersAsync(), ticket) && only == null) App.Current.RecountSoon(account.Provider);
            Network.Note(account.Provider.Account, null);
            account.FoldersFailed = false;
            account.Status = "";
            account.Fix = AccountFix.None;
            WatchInboxes(account);
            MailCache.SaveFolders(account.Provider.Account, account.Snapshot());
            UpdateFavorites();
            _ = LoadCategoriesAsync(account);
            _ = LoadAutoRepliesAsync(account);
            _ = ReplayQueueAsync(account); // le azioni fatte senza rete, anche prima dell'ultima chiusura

        }
        catch (Exception ex)
        {
            Log.Error($"cartelle di {account.Header}", ex);
            Network.Note(account.Provider.Account, ex);
            account.FoldersFailed = true;
            account.Status = Network.Explain(ex) ?? "Non raggiungibile: " + ex.Message; // senza rete, "Non in linea" invece del testo di Windows
            account.Fix = MailException.FixOf(ex);
        }
    }

    /// <summary>
    /// Dopo una richiesta riuscita le cartelle si rileggono se l'account era "Non in linea" o se la loro ultima lettura non
    /// era riuscita per un altro motivo: "Non raggiungibile: ..." (connessione chiusa o rifiutata, tempo scaduto, TLS di una
    /// rete con portale) restava sotto l'account fino al riavvio, con albero e conteggi della copia (audit del 7 ottobre, A-15).
    /// </summary>
    internal static bool ReloadsFolders(string status, bool foldersFailed) => foldersFailed || status.StartsWith("Non in linea");

    /// <summary>
    /// Il server dell'account ha risposto fuori dal controllo dei nuovi messaggi (la query delta di Graph riuscita, una
    /// sessione IDLE collegata): se l'account risultava non in linea o le sue cartelle non erano state lette, lo stato si
    /// toglie, le cartelle si rileggono e la coda delle azioni riparte subito, come dopo un controllo riuscito. Prima lo
    /// faceva solo il controllo: con la rete tornata senza che Windows lo dicesse, fino a due minuti dopo (audit, A-15).
    /// </summary>
    public void AccountReached(IMailProvider provider, string how)
    {
        if (accounts.FirstOrDefault(a => a.Provider.Account.Id == provider.Account.Id) is not { } item) return;
        if (!item.FoldersFailed && !Network.KnownOffline(provider.Account)) return;
        Log.Write($"{provider.Account.Email} di nuovo raggiungibile ({how}): stato, cartelle e azioni in coda senza aspettare il controllo");
        NoteAccountState(provider, null);
    }

    /// <summary>Le cartelle di qualche account non si sono lette: al cambio della rete il controllo parte anche per loro (App.OnNetworkChanged).</summary>
    public bool AnyFoldersFailed => accounts.Any(a => a.FoldersFailed);

    /// <summary>
    /// Il controllo dei nuovi messaggi di un account (App.CheckNewAsync): una password rifiutata o un nuovo accesso chiesto
    /// accendono la barra sotto l'account; un controllo riuscito la spegne.
    /// </summary>
    public void NoteAccountState(IMailProvider provider, Exception? error)
    {
        if (accounts.FirstOrDefault(a => a.Provider.Account.Id == provider.Account.Id) is not { } item) return;
        item.RefreshAutoReplies(); // il periodo delle risposte automatiche può essere cominciato o finito
        bool wasOffline = Network.KnownOffline(provider.Account);
        Network.Note(provider.Account, error);
        // Non in linea: lo stato sotto l'account, e quando la rete torna le cartelle si rileggono e la coda delle azioni e la
        // Posta in uscita ripartono. Lo stesso se le cartelle non si erano lette per un altro motivo (ReloadsFolders).
        if (Network.Explain(error) is { } offline) item.Status = offline;
        else if (error == null && ReloadsFolders(item.Status, item.FoldersFailed))
        {
            item.Status = "";
            _ = LoadFoldersAsync(item);
        }
        else if (error == null) _ = ReplayQueueAsync(item);
        // L'account era non in linea e ha risposto: gli IDLE caduti riprovano subito (InboxIdle.RetryNow).
        if (error == null && wasOffline) InboxIdle.RetryNow();
        var fix = MailException.FixOf(error);
        if (fix != AccountFix.None)
        {
            item.Status = error!.Message;
            item.Fix = fix;
        }
        else if (error == null && item.Fix != AccountFix.None)
        {
            item.Fix = AccountFix.None;
            item.Status = "";
        }
    }

    void OnAccountFix(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccountItem item }) return;
        if (item.Fix == AccountFix.Consent) _ = AllowSharedAsync(item);
        else _ = ManageAccountAsync(item.Provider.Account, this, focusPassword: item.Fix == AccountFix.Password);
    }

    /// <summary>
    /// "Consenti l'accesso alle caselle condivise", sotto la casella condivisa: solo qui la finestra di Microsoft chiede
    /// Mail.ReadWrite.Shared e Mail.Send.Shared per l'account di lavoro (65001 e 90094 con il link per l'amministratore).
    /// Poi le cartelle si leggono. Nella prova (TestingProvider) solo "prova: chiederei il consenso ..." nel registro.
    /// </summary>
    async Task AllowSharedAsync(AccountItem item)
    {
        var owner = S.Accounts.FirstOrDefault(a => a.Id == item.Provider.Account.SharedOf);
        try
        {
            await item.Provider.AllowSharedAsync();
        }
        catch (Exception ex) when (!MicrosoftAuth.Canceled(ex))
        {
            Log.Error($"consenso alle caselle condivise per {item.Header}", ex);
            ReadingView.ShowConsentError(this, "Consenso non dato", ex, owner ?? item.Provider.Account, "aprire le caselle condivise");
            return;
        }
        catch (Exception)
        {
            return; // annullato nella finestra di Windows
        }
        await LoadFoldersAsync(item);
    }

    /// <summary>
    /// Aggiungi casella condivisa, dal menu di un account di lavoro, come "Aggiungi cartella o casella condivisa" del nuovo
    /// Outlook: l'indirizzo si scrive a mano (Graph non dice a quali caselle si ha accesso). La casella entra subito dopo
    /// l'account, con le sue cartelle; il consenso si chiede solo dal pulsante della barra sotto di lei.
    /// </summary>
    async Task AddSharedMailboxAsync(MailAccount work)
    {
        string? Check(string address)
        {
            if (!AccountTypes.LooksValid(address)) return "Non sembra un indirizzo email: per esempio info@studio.it.";
            if (S.Accounts.Any(a => a.Email.Equals(address, StringComparison.OrdinalIgnoreCase) && (a.SharedOf == work.Id || a.SharedOf == null)))
                return $"{address} è già in Missiva.";
            return null;
        }
        var window = new NameWindow("Aggiungi casella condivisa", "", "Aggiungi",
            $"L'indirizzo di una casella condivisa di Microsoft 365 su cui {work.Email} ha il permesso Accesso completo (per esempio info@ o segreteria@). " +
            "La prima volta Missiva chiede il consenso alle caselle condivise, solo quando premi il pulsante.", "Indirizzo", Check) { Owner = this };
        if (window.ShowDialog() != true) return;
        var shared = new MailAccount
        {
            Kind = AccountKind.Microsoft, Email = window.Value, SharedOf = work.Id, Tenant = work.Tenant, ClientId = work.ClientId,
        };
        // Subito dopo l'account e le sue altre caselle condivise.
        int at = S.Accounts.FindIndex(a => a.Id == work.Id);
        while (at + 1 < S.Accounts.Count && S.Accounts[at + 1].SharedOf == work.Id) at++;
        S.Accounts.Insert(at + 1, shared);
        Store.Save();
        Log.Write($"casella condivisa {shared.Email} aggiunta a {work.Email}");
        await LoadAccountsAsync();
    }

    /// <summary>
    /// Gestisci account (Impostazioni, Account; menu dell'account; barra "Aggiorna password"): nome, password e server
    /// degli account IMAP, nuovo accesso di Microsoft e Google. Con server, password o accesso nuovi le connessioni si
    /// rifanno e le cartelle si ricaricano; l'account resta lo stesso (firme, categorie e ordine delle cartelle).
    /// </summary>
    public async Task ManageAccountAsync(MailAccount account, Window owner, bool focusPassword = false)
    {
        var window = new ManageAccountWindow(account, focusPassword) { Owner = owner };
        window.ShowDialog();
        if (!window.Reconnect || accounts.FirstOrDefault(a => a.Provider.Account.Id == account.Id) is not { } item) return;
        await item.Provider.ResetConnectionsAsync();
        await LoadFoldersAsync(item);
        if (folder?.Account == item && !searching) await ReloadAsync();
    }

    // ---------------------------------------------------------------- numero sull'icona

    /// <summary>Il numero sull'icona nella barra delle applicazioni cambia con i non letti delle Posta in arrivo.</summary>
    void WatchInboxes(AccountItem account)
    {
        if (only != null) return;
        foreach (var inbox in account.Folders.Where(f => f.Folder.Kind == FolderKind.Inbox))
        {
            inbox.PropertyChanged -= OnInboxChanged;
            inbox.PropertyChanged += OnInboxChanged;
        }
        UpdateBadge();
    }

    void OnInboxChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderItem.Unread)) UpdateBadge();
    }

    int badge = -1;
    IconNumberKind badgeKind;
    bool badgeQueued;
    /// <summary>I messaggi nuovi da quando si è guardato Missiva, per il numero sull'icona con "Messaggi nuovi" (IconCount).</summary>
    readonly FreshMail fresh = new();

    /// <summary>
    /// I messaggi nuovi che Missiva annuncia (App.CheckAccountAsync, gli stessi delle notifiche, anche con le notifiche
    /// spente): con la finestra principale non attiva contano per il numero "Messaggi nuovi" sull'icona.
    /// </summary>
    internal void CountArrived(IMailProvider provider, List<MessageSummary> arrived)
    {
        if (only != null) return;
        fresh.Add(provider.Account.Id, arrived, looking: IsActive && IsVisible && WindowState != WindowState.Minimized);
        if (S.IconNumber == IconNumberKind.New) UpdateBadge();
    }

    /// <summary>Le scelte del numero sull'icona cambiate in Impostazioni, Generali, Notifiche: il numero si rifà subito.</summary>
    internal void RefreshBadge() => UpdateBadge();

    /// <summary>
    /// Come il nuovo Outlook, sull'icona nella barra delle applicazioni i non letti delle Posta in arrivo di tutti gli
    /// account, "99+" oltre 99; dai ritocchi del 16 ottobre 2026 a scelta (IconCount: da leggere, nuovi da quando si è
    /// guardato Missiva, nessuno) e solo per gli account scelti, e lo stesso numero nel suggerimento dell'area di notifica.
    /// Accorpato: più cambi di fila (le Posta in arrivo di più account, un controllo che ne tocca
    /// due) fanno un solo aggiornamento, dopo il disegno; prima icona, area di notifica e registro si rifacevano a ogni
    /// cambio, dentro il clic che apre un messaggio da leggere (audit, v2 V-05 e v1 V-09).
    /// </summary>
    void UpdateBadge()
    {
        if (badgeQueued) return;
        badgeQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            badgeQueued = false;
            UpdateBadgeNow();
        });
    }

    void UpdateBadgeNow()
    {
        UpdateUnified();
        var kind = S.IconNumber;
        int count = IconCount.Count(kind, accounts.SelectMany(a => a.Folders).Where(f => f.Folder.Kind == FolderKind.Inbox).Select(f => (f.Account.Provider.Account.Id, f.Unread)),
                                    fresh, S.IconNumberOff);
        if (count == badge && kind == badgeKind) return;
        badge = count;
        badgeKind = kind;
        App.Current.UpdateTrayTip(kind, count);
        TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
        var accent = Application.Current.TryFindResource("SystemAccentColorDark1") is Color c ? c : Color.FromRgb(0xC4, 0x2B, 0x1C);
        TaskbarItemInfo.Overlay = IconCount.Badge(count) is { } text ? BadgeImage(text, accent) : null;
        TaskbarItemInfo.Description = IconCount.Describe(kind, count);
        Log.Write($"numero sull'icona: {count}{(kind == IconNumberKind.Unread ? "" : kind == IconNumberKind.New ? " (messaggi nuovi)" : " (nessun numero)")}");
    }

    /// <summary>Le immagini del numero già disegnate, per testo e colore: "1".."99+" per ogni colore principale visto (4 KB l'una).</summary>
    static readonly Dictionary<(string Text, Color Color), ImageSource> badgeImages = new();

    /// <summary>Il numero in un cerchio del colore principale di Windows, disegnato a 32 pixel (Windows lo mostra a 16 per 100%).</summary>
    internal static ImageSource BadgeImage(string text, Color color)
    {
        if (badgeImages.TryGetValue((text, color), out var drawn)) return drawn;
        if (badgeImages.Count >= 400) badgeImages.Clear(); // molti colori principali cambiati nella sessione: si riparte
        return badgeImages[(text, color)] = DrawBadge(text, color);
    }

    static ImageSource DrawBadge(string text, Color color)
    {
        const double size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(color), null, new Point(size / 2, size / 2), size / 2, size / 2);
            var label = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                text.Length > 2 ? 13 : text.Length == 2 ? 17 : 21, Brushes.White, 1.0);
            dc.DrawText(label, new Point((size - label.Width) / 2, (size - label.Height) / 2));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size, (int)size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    void OnAccountToggle(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AccountItem account }) account.IsExpanded = !account.IsExpanded;
    }

    // ---------------------------------------------------------------- albero e riordino delle cartelle

    Point? dragStart;
    FolderItem? dragFolder;

    static FolderItem? FolderAt(ListBox list, object source) =>
        source is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListBoxItem { DataContext: FolderItem f } ? f : null;

    void OnFolderToggle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FolderItem f }) f.Account.Toggle(f);
        e.Handled = true;
    }

    void OnFolderMouseDown(object sender, MouseButtonEventArgs e)
    {
        dragFolder = FolderAt((ListBox)sender, e.OriginalSource);
        dragStart = e.GetPosition(this);
    }

    /// <summary>Oltre la soglia di trascinamento di Windows parte lo spostamento della cartella.</summary>
    void OnFolderMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragFolder is not { } dragged || dragStart is not { } start) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragFolder = null;
        dragStart = null;
        DragDrop.DoDragDrop((ListBox)sender, new DataObject(typeof(FolderItem), dragged), DragDropEffects.Move);
        ClearDropLines(dragged.Account);
    }

    /// <summary>
    /// Una cartella si può lasciare solo fra cartelle sorelle: la linea mostra se finirà sopra o sotto. I messaggi
    /// trascinati dall'elenco evidenziano la cartella sotto il puntatore; con Ctrl si copiano.
    /// </summary>
    void OnFolderDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        var list = (ListBox)sender;
        if (e.Data.GetData(typeof(MessageDrag)) is MessageDrag drag)
        {
            var over = FolderAt(list, e.OriginalSource);
            bool here = over != null && Targets(drag.Items).All(m => m.FolderId == over.Folder.Id);
            // Su una cartella dell'altro account il rilascio si lascia fare, per dire perché non si può (DropMessagesAsync).
            MarkDropTarget(over != null && !here && over.Account == drag.Items[0].Account ? over : null);
            if (over != null && !here)
                e.Effects = e.KeyStates.HasFlag(DragDropKeyStates.ControlKey) ? DragDropEffects.Copy : DragDropEffects.Move;
            return;
        }
        if (e.Data.GetData(typeof(FolderItem)) is not FolderItem dragged) return;
        var target = FolderAt(list, e.OriginalSource);
        foreach (var f in dragged.Account.Folders.Where(f => f != target)) f.DropSide = 0;
        if (target == null || target == dragged || target.Parent != dragged.Parent) return;
        if (list.ItemContainerGenerator.ContainerFromItem(target) is not ListBoxItem container) return;
        target.DropSide = e.GetPosition(container).Y > container.ActualHeight / 2 ? 1 : -1;
        e.Effects = DragDropEffects.Move;
    }

    void OnFolderDragLeave(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(FolderItem)) is FolderItem dragged) ClearDropLines(dragged.Account);
        if (e.Data.GetDataPresent(typeof(MessageDrag))) MarkDropTarget(null);
    }

    void OnFolderDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(MessageDrag)) is MessageDrag drag)
        {
            MarkDropTarget(null);
            bool copy = e.KeyStates.HasFlag(DragDropKeyStates.ControlKey);
            // Dopo il trascinamento: dentro Drop, Windows aspetta ancora la fine del trascinamento (e un avviso lo bloccherebbe).
            if (FolderAt((ListBox)sender, e.OriginalSource) is { } destination)
                Dispatcher.BeginInvoke(async () => await DropMessagesAsync(destination, drag.Items, copy));
            return;
        }
        if (e.Data.GetData(typeof(FolderItem)) is not FolderItem dragged) return;
        if (dragged.Account.Folders.FirstOrDefault(f => f.DropSide != 0) is { } target)
            dragged.Account.Move(dragged, target, after: target.DropSide > 0);
        ClearDropLines(dragged.Account);
    }

    static void ClearDropLines(AccountItem account)
    {
        foreach (var f in account.Folders) f.DropSide = 0;
    }

    // ---------------------------------------------------------------- messaggi trascinati nelle cartelle

    /// <summary>Le righe trascinate dall'elenco verso le cartelle e i Preferiti.</summary>
    sealed record MessageDrag(List<MessageItem> Items);

    Point? messageDragStart;
    /// <summary>
    /// Clic senza tasti su una riga già scelta fra più: la selezione resta, perché si possano trascinare tutte; se non
    /// si trascina, al rilascio resta scelta solo quella riga, come in Outlook. Con Seleziona (SelectionMode Multiple)
    /// invece al rilascio la riga esce dalla scelta, come con un clic normale in quella modalità.
    /// </summary>
    MessageItem? pendingClick;

    void OnMessageDragStart(object sender, MouseButtonEventArgs e)
    {
        messageDragStart = null;
        pendingClick = null;
        if (e.OriginalSource is not DependencyObject d || ItemsControl.ContainerFromElement(MessageList, d) is not ListBoxItem { DataContext: MessageItem item } row) return;
        // I pulsanti della riga (azioni rapide, conversazione, casella di Seleziona) restano pulsanti.
        for (DependencyObject? p = d; p != null && p != row; p = p is Visual ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p is System.Windows.Controls.Primitives.ButtonBase) return;
        messageDragStart = e.GetPosition(this);
        if (row.IsSelected && MessageList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            pendingClick = item;
            e.Handled = true;
        }
    }

    /// <summary>Oltre la soglia di trascinamento di Windows partono le righe scelte, come in Outlook.</summary>
    void OnMessageDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || messageDragStart is not { } start) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        messageDragStart = null;
        pendingClick = null;
        if (Selected is not { Count: > 0 } items) return;
        DragDrop.DoDragDrop(MessageList, new DataObject(typeof(MessageDrag), new MessageDrag(items)), DragDropEffects.Move | DragDropEffects.Copy);
        MarkDropTarget(null);
    }

    void OnMessageMouseUp(object sender, MouseButtonEventArgs e)
    {
        messageDragStart = null;
        if (pendingClick is not { } item) return;
        pendingClick = null;
        if (MessageList.SelectionMode == SelectionMode.Multiple) MessageList.SelectedItems.Remove(item);
        else MessageList.SelectedItem = item;
    }

    /// <summary>La cartella evidenziata sotto i messaggi trascinati (null: nessuna).</summary>
    void MarkDropTarget(FolderItem? target)
    {
        foreach (var f in accounts.SelectMany(a => a.Folders)) f.IsDropTarget = f == target;
    }

    /// <summary>
    /// Messaggi lasciati su una cartella o su un Preferito: lo stesso spostamento di Sposta in, con Annulla, o con Ctrl una
    /// copia. Solo nelle cartelle dello stesso account: altrove si dice perché.
    /// </summary>
    /// <param name="simulate">Solo nel registro (--drop, per le prove in Debug).</param>
    async Task DropMessagesAsync(FolderItem target, List<MessageItem> items, bool copy, bool simulate = false)
    {
        if (items.Count == 0) return;
        if (items.FirstOrDefault(i => i.Account != target.Account) is { } other)
        {
            Log.Write($"messaggi lasciati su {target.Name} di {target.Account.Header}: rifiutati, sono di {other.Account.Header}");
            Dialog.Show(this, "Cartella di un altro account",
                $"I messaggi di {other.Account.Header} si spostano solo nelle sue cartelle, e \"{target.Name}\" è di {target.Account.Header}.");
            return;
        }
        if (copy) await CopyAsync(items, target.Folder, simulate);
        else await MoveAsync(items, _ => target.Folder, "spostamento", simulate);
    }

    void OnFolderMenu(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBox { ContextMenu: { } menu } list || FolderAt(list, e.OriginalSource) is not { } f)
        {
            e.Handled = true;
            return;
        }
        BuildFolderMenu(menu, f, fromFavorites: list == FavoritesList);
    }

    /// <summary>
    /// Il menu del tasto destro su una cartella, come nel nuovo Outlook (le voci scelte in "Personalizza il menu…").
    /// Rinomina, Sposta ed Elimina valgono solo per le cartelle dell'utente; nei Preferiti Sposta su e giù ne cambiano l'ordine.
    /// </summary>
    void BuildFolderMenu(ContextMenu menu, FolderItem f, bool fromFavorites)
    {
        menu.Items.Clear();
        var account = f.Account;
        bool user = IsUserFolder(f);
        var siblings = fromFavorites ? favorites.ToList() : account.Folders.Where(x => x.Parent == f.Parent).ToList();
        int index = siblings.IndexOf(f);
        bool favorite = S.Favorites.Contains(f.Key);
        bool hidden = S.HiddenFolders.Contains(f.Key);

        MenuItem? Build(string key)
        {
            switch (key)
            {
                case "count":
                    var count = new TextBlock
                    {
                        Text = $"{f.Folder.Total.ToString("N0", Italian)} elementi ({f.Folder.Unread.ToString("N0", Italian)} non letti)",
                        FontWeight = FontWeights.SemiBold,
                    };
                    return new MenuItem { Header = count, IsHitTestVisible = false, Focusable = false };
                case "newFolder":
                    return MenuEntry("Crea nuova sottocartella", "", null, async (_, _) => await CreateSubfolderAsync(f));
                case "rename":
                    return MenuEntry("Rinomina", "", null, async (_, _) => await RenameFolderAsync(f), user);
                case "color":
                    return FolderColorMenu(f);
                case "move":
                    return FolderMoveMenu(f, user);
                case "delete":
                    return MenuEntry("Elimina", "", null, async (_, _) => await DeleteFolderAsync(f), user && !f.HasChildren);
                case "empty":
                    return MenuEntry("Svuota", "", null, async (_, _) => await EmptyFolderAsync(f), f.Folder.Total > 0 && CanEmpty(f));
                case "markRead":
                    return MenuEntry("Contrassegna tutti come letti", "", null, async (_, _) => await MarkAllReadAsync(f), f.Folder.Unread > 0);
                case "favorite":
                    return MenuEntry(favorite ? "Rimuovi da Preferiti" : "Aggiungi a Preferiti", favorite ? "" : "", null, (_, _) => ToggleFavorite(f));
                case "up":
                    return MenuEntry("Sposta su", "", null, (_, _) => StepFolder(f, -1, fromFavorites), index > 0);
                case "down":
                    return MenuEntry("Sposta giù", "", null, (_, _) => StepFolder(f, 1, fromFavorites), index >= 0 && index < siblings.Count - 1);
                case "window":
                    return MenuEntry("Apri in un'altra finestra", "", null, (_, _) => OpenFolderWindow(f));
                case "hide":
                    return MenuEntry(hidden ? "Mostra di nuovo nell'elenco" : "Nascondi dall'elenco", "", null,
                        (_, _) => { account.SetHidden(f, !hidden); UpdateFavorites(); }, f.Folder.Kind != FolderKind.Inbox);
                case "resetOrder" when !fromFavorites:
                    return MenuEntry("Ripristina l'ordine predefinito", "", null, (_, _) => account.ResetOrder(), account.HasCustomOrder);
                default:
                    return null;
            }
        }

        FillCustomMenu(menu, ContextMenus.Folders, Build, this);
    }

    /// <summary>
    /// Riempie un menu del tasto destro (cartelle o messaggi) con le voci scelte in "Personalizza il menu…", nel loro
    /// ordine: restano fuori quelle nascoste e quelle che qui non ci sono (build null). I separatori valgono solo fra due
    /// voci presenti: niente separatori doppi, in cima o in fondo. In fondo, "Personalizza il menu…". Era scritto uguale
    /// nei due menu (audit dell'11 ottobre, C-15). Statico, con la finestra che fa da proprietaria di "Personalizza" come
    /// parametro: le prove lo chiamano senza una finestra principale.
    /// </summary>
    internal static void FillCustomMenu(ContextMenu menu, ContextMenus.Definition definition, Func<string, MenuItem?> build, Window? owner)
    {
        var hidden = definition.Hidden();
        bool separator = false;
        foreach (var key in definition.Order())
        {
            if (key == ContextMenus.Separator)
            {
                separator = menu.Items.Count > 0;
                continue;
            }
            if (hidden.Contains(key) || build(key) is not { } item) continue;
            if (separator) menu.Items.Add(new Separator());
            separator = false;
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("Personalizza il menu…", "", null, (_, _) => new MenuCustomizeWindow(definition) { Owner = owner }.ShowDialog()));
    }

    static readonly System.Globalization.CultureInfo Italian = new("it-IT");

    /// <summary>Cambia colore: l'icona della cartella nei colori di Outlook, o nessun colore.</summary>
    MenuItem FolderColorMenu(FolderItem f)
    {
        var menu = MenuEntry("Cambia colore", "", null);
        S.FolderColors.TryGetValue(f.Key, out var current);
        bool colored = S.FolderColors.ContainsKey(f.Key);
        var none = new MenuItem { Header = "Nessun colore", IsChecked = !colored };
        none.Click += (_, _) => SetFolderColor(f, null);
        menu.Items.Add(none);
        menu.Items.Add(new Separator());
        for (int i = 0; i < 10; i++)
        {
            int preset = i;
            var entry = new MenuItem { Header = CategoryPalette.Presets[i].Name, Icon = Swatch(i), IsChecked = colored && current == i };
            entry.Click += (_, _) => SetFolderColor(f, preset);
            menu.Items.Add(entry);
        }
        return menu;
    }

    void SetFolderColor(FolderItem f, int? preset)
    {
        if (preset is { } p) S.FolderColors[f.Key] = p;
        else S.FolderColors.Remove(f.Key);
        Store.SaveSoon();
        f.RefreshColor();
    }

    /// <summary>
    /// Sposta la cartella dentro un'altra o al primo livello. In Gmail non si può mettere dentro una cartella di sistema.
    /// Non dentro sé stessa né dentro le sue sottocartelle.
    /// </summary>
    MenuItem FolderMoveMenu(FolderItem f, bool enabled)
    {
        var menu = MenuEntry("Sposta", "", null, enabled: enabled);
        if (!enabled) return menu;
        bool gmail = f.Account.Provider.Account.Kind == AccountKind.Gmail;
        bool Inside(FolderItem candidate)
        {
            for (var p = candidate; p != null; p = p.Parent) if (p == f) return true;
            return false;
        }
        var top = new MenuItem { Header = "Al primo livello", IsEnabled = f.Parent != null };
        top.Click += async (_, _) => await MoveFolderAsync(f, null);
        menu.Items.Add(top);
        menu.Items.Add(new Separator());
        foreach (var target in f.Account.Folders.Where(t => !Inside(t) && t != f.Parent && !t.IsHidden && (!gmail || IsUserFolder(t))))
        {
            var entry = new MenuItem { Header = new TextBlock { Text = target.Name, Margin = new Thickness(target.Depth * 16, 0, 0, 0) } };
            entry.Click += async (_, _) => await MoveFolderAsync(f, target);
            menu.Items.Add(entry);
        }
        return menu;
    }

    void StepFolder(FolderItem f, int direction, bool fromFavorites)
    {
        if (!fromFavorites)
        {
            f.Account.Step(f, direction);
            return;
        }
        int i = S.Favorites.IndexOf(f.Key), j = i + direction;
        if (i < 0 || j < 0 || j >= S.Favorites.Count) return;
        (S.Favorites[i], S.Favorites[j]) = (S.Favorites[j], S.Favorites[i]);
        Store.SaveSoon();
        UpdateFavorites();
    }

    // ---------------------------------------------------------------- Preferiti

    void ToggleFavorite(FolderItem f)
    {
        if (!S.Favorites.Remove(f.Key)) S.Favorites.Add(f.Key);
        Store.SaveSoon();
        UpdateFavorites();
        UpdateListHeader();
    }

    /// <summary>La sezione Preferiti, nell'ordine scelto; si vede solo se ce n'è almeno uno.</summary>
    void UpdateFavorites()
    {
        if (only != null) return;
        var all = accounts.SelectMany(a => a.Folders).GroupBy(f => f.Key).ToDictionary(g => g.Key, g => g.First());
        var wanted = S.Favorites.Where(all.ContainsKey).Select(k => all[k]).Where(f => !f.IsHidden).ToList();
        if (!favorites.SequenceEqual(wanted))
        {
            favorites.Clear();
            foreach (var f in wanted) favorites.Add(f);
        }
        FavoritesPanel.Visibility = favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FavoritesList.Visibility = S.FavoritesCollapsed ? Visibility.Collapsed : Visibility.Visible;
        FavoritesChevron.Text = S.FavoritesCollapsed ? "" : "";
        if (folder != null) MarkSelectedFolder(searching ? null : folder);
        UpdateGroupMarks();
    }

    void OnFavoritesToggle(object sender, RoutedEventArgs e)
    {
        S.FavoritesCollapsed = !S.FavoritesCollapsed;
        Store.SaveSoon();
        UpdateFavorites();
    }

    // ---------------------------------------------------------------- azioni sulle cartelle

    async Task CreateSubfolderAsync(FolderItem parent)
    {
        bool gmailSystem = parent.Account.Provider.Account.Kind == AccountKind.Gmail && !IsUserFolder(parent);
        var dialog = new NameWindow("Nuova sottocartella di " + parent.Name, "", "Crea",
            gmailSystem ? "In Gmail le cartelle di sistema non hanno sottocartelle: la nuova cartella sarà al primo livello." : null) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await FolderActionAsync(parent.Account, "nuova cartella", async () =>
        {
            await parent.Account.Provider.CreateFolderAsync(dialog.Value, gmailSystem ? null : parent.Folder);
            if (!parent.expanded && !gmailSystem) parent.Account.Toggle(parent);
        });
    }

    async Task RenameFolderAsync(FolderItem f)
    {
        var dialog = new NameWindow("Rinomina " + f.Name, f.Name, "Rinomina") { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == f.Name) return;
        bool wasOpen = folder == f;
        string? newId = null;
        await FolderActionAsync(f.Account, "rinomina", async () =>
        {
            newId = await f.Account.Provider.RenameFolderAsync(f.Folder, dialog.Value);
            MigrateFolderKeys(f.Account, f.Folder.Id, newId);
        }, () => wasOpen && newId != null ? f.Account.Folders.FirstOrDefault(x => x.Folder.Id == newId) : null);
    }

    async Task MoveFolderAsync(FolderItem f, FolderItem? target)
    {
        bool wasOpen = folder == f;
        string? newId = null;
        await FolderActionAsync(f.Account, "spostamento della cartella", async () =>
        {
            newId = await f.Account.Provider.MoveFolderAsync(f.Folder, target?.Folder);
            MigrateFolderKeys(f.Account, f.Folder.Id, newId);
            if (target is { expanded: false }) f.Account.Toggle(target);
        }, () => wasOpen && newId != null ? f.Account.Folders.FirstOrDefault(x => x.Folder.Id == newId) : null);
    }

    async Task EmptyFolderAsync(FolderItem f)
    {
        var total = f.Folder.Total.ToString("N0", Italian);
        bool permanent = f.Folder.Kind is FolderKind.Deleted or FolderKind.Junk;
        // Senza Cestino (IMAP) si crea; se non si può, una cartella normale non si svuota (TrashOfAsync lo dice).
        MailFolder? trash = null;
        if (!permanent && (trash = await TrashOfAsync(f.Account)) == null) return;
        var (question, detail, confirm) = permanent
            ? ($"Svuotare {f.Name}?", $"I {total} messaggi verranno eliminati per sempre e non si potranno recuperare.", "Elimina per sempre")
            : ($"Spostare nel Cestino tutti i messaggi di {f.Name}?", $"Sono {total} messaggi. Dal Cestino si possono ancora recuperare.", "Svuota");
        if (Dialog.Ask(this, question, detail, confirm) != Choice.Primary) return;
        bool open = folder == f;
        await FolderActionAsync(f.Account, "svuota", async () =>
        {
            await f.Account.Provider.EmptyFolderAsync(f.Folder, permanent ? null : trash);
            BodyCache.ForgetFolder(f.Account.Provider.Account, f.Folder.Id); // con i messaggi escono le loro copie su disco
        });
        if (open) await ReloadAsync();
    }

    async Task MarkAllReadAsync(FolderItem f)
    {
        await FolderActionAsync(f.Account, "segna tutti come letti", () => f.Account.Provider.MarkAllReadAsync(f.Folder));
        if (folder == f) foreach (var row in rows.Rows) row.IsRead = true;
    }

    /// <summary>Un'azione sulle cartelle: errori in una finestra, poi l'elenco delle cartelle si ricarica.</summary>
    async Task FolderActionAsync(AccountItem account, string what, Func<Task> action, Func<FolderItem?>? reopen = null)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Error(what, ex);
            Dialog.Show(this, "Operazione non riuscita", ex.Message);
        }
        await LoadFoldersAsync(account);
        if (reopen?.Invoke() is { } again) SelectFolder(again);
    }

    /// <summary>
    /// In Gmail l'identificativo di una cartella è il suo percorso: rinominandola o spostandola cambia, per lei e per le
    /// sue sottocartelle. Preferiti, colori, cartelle nascoste o chiuse, ordine e colori delle categorie lo seguono.
    /// Le sottocartelle si riconoscono dal separatore dei percorsi dell'account (FolderSeparator): con la barra fissa, su un
    /// server IMAP che separa con il punto (INBOX.Clienti.Rossi), rinominata INBOX.Clienti le sue sottocartelle non seguivano
    /// (scelte del 12 ottobre, trovato con C-47).
    /// </summary>
    internal static void MigrateFolderKeys(AccountItem account, string oldId, string newId)
    {
        if (oldId == newId) return;
        var prefix = account.Provider.Account.Id + "|";
        var below = oldId + FolderSeparator(account);
        string Map(string id) => id == oldId ? newId : id.StartsWith(below, StringComparison.Ordinal) ? newId + id[oldId.Length..] : id;
        string MapKey(string key) => key.StartsWith(prefix, StringComparison.Ordinal) ? prefix + Map(key[prefix.Length..]) : key;
        void MapList(List<string> list)
        {
            for (int i = 0; i < list.Count; i++) list[i] = MapKey(list[i]);
        }
        MapList(S.Favorites);
        MapList(S.HiddenFolders);
        MapList(S.CollapsedFolders);
        foreach (var dict in new[] { S.FolderColors, S.CategoryColors })
            foreach (var key in dict.Keys.ToList())
                if (MapKey(key) is var mapped && mapped != key)
                {
                    dict[mapped] = dict[key];
                    dict.Remove(key);
                }
        if (S.FolderOrder.TryGetValue(account.Provider.Account.Id, out var order))
            for (int i = 0; i < order.Count; i++) order[i] = Map(order[i]);
        Store.Save();
    }

    /// <summary>
    /// Il separatore dei percorsi delle cartelle dell'account: la barra in Gmail, spesso il punto sui server IMAP. Lo dice una
    /// cartella che ne ha una sopra: il suo identificativo è quello della madre, il separatore, il nome. Senza sottocartelle,
    /// o con identificativi che non sono percorsi (Graph, dove rinominare non li cambia), la barra.
    /// </summary>
    internal static char FolderSeparator(AccountItem account)
    {
        foreach (var f in account.Folders)
            if (f.Folder.ParentId is { Length: > 0 } parent && f.Folder.Id.Length > parent.Length + 1 && f.Folder.Id.StartsWith(parent, StringComparison.Ordinal))
                return f.Folder.Id[parent.Length];
        return '/';
    }

    /// <summary>Apri in un'altra finestra: la stessa finestra di Missiva, con quella sola cartella.</summary>
    void OpenFolderWindow(FolderItem f)
    {
        var window = new MainWindow((f.Account.Provider.Account.Id, f.Folder.Id)) { Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Left = Left + 40;
        window.Top = Top + 40;
        window.Closed += (_, _) => folderWindows.Remove(window);
        folderWindows.Add(window);
        window.Show();
    }

    /// <summary>
    /// Cartelle create dall'utente (o da altri programmi), che si possono eliminare. Non quelle di sistema: in Gmail
    /// Speciali e Importanti sono "altre" come le etichette, ma stanno direttamente sotto [Gmail].
    /// </summary>
    static bool IsUserFolder(FolderItem f) => f.Folder.Kind == FolderKind.Other && f.Folder.ParentId != "[Gmail]";

    /// <summary>
    /// Svuota, come nel sito di Gmail, non vale per Tutti i messaggi (la cartella di Archivia), che contiene tutta la
    /// casella, Posta in arrivo, Posta inviata e Bozze comprese, né per Speciali, i cui messaggi stanno anche altrove:
    /// finirebbero nel Cestino molto più dei messaggi che si pensa di togliere.
    /// </summary>
    static bool CanEmpty(FolderItem f) =>
        f.Account.Provider.Account.Kind != AccountKind.Gmail || f.Folder.Kind is not FolderKind.Archive && (f.Folder.Kind != FolderKind.Other || IsUserFolder(f));

    void OnToggleHidden(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem account }) account.ToggleShowHidden();
    }

    async Task DeleteFolderAsync(FolderItem f)
    {
        var total = f.Folder.Total;
        var gmail = f.Account.Provider.Account.Kind == AccountKind.Gmail;
        var detail = gmail
            ? "In Gmail le cartelle sono etichette: " + (total == 0
                ? "questa è vuota, non si perde nulla."
                : $"i suoi {total} messaggi non si cancellano, restano in Tutti i messaggi. Perdono solo l'etichetta.")
            : f.Account.Provider.Account.Kind == AccountKind.Imap
                ? total == 0 ? "È vuota." : $"La cartella si elimina dal server insieme ai suoi {total} messaggi."
            : total == 0 ? "È vuota." : $"La cartella finisce in Posta eliminata insieme ai suoi {total} messaggi.";
        if (Dialog.Ask(this, $"Eliminare la cartella \"{f.Name}\"?", detail + " L'eliminazione vale ovunque, anche sul web e sul telefono.", "Elimina") != Choice.Primary)
            return;
        try
        {
            await f.Account.Provider.DeleteFolderAsync(f.Folder);
        }
        catch (Exception ex)
        {
            Log.Error($"eliminazione della cartella {f.Folder.Id}", ex);
            Dialog.Show(this, "Cartella non eliminata", ex.Message);
            return;
        }
        ForgetFolderKeys(f.Account, f.Folder.Id);
        bool wasOpen = folder == f;
        await LoadFoldersAsync(f.Account);
        if (wasOpen) SelectInbox();
    }

    /// <summary>
    /// Di una cartella eliminata non resta nulla di quello che Missiva teneva per il suo identificativo: Preferito, colore,
    /// nascosta, chiusa, posto nell'ordine, e le copie sul PC (la prima pagina dell'elenco e i corpi dei suoi messaggi, che
    /// sul server non ci sono più o, in Gmail, hanno perso l'etichetta). Prima uscivano solo nascosta e chiusa: una
    /// cartella nuova con lo stesso nome nasceva fra i Preferiti, con il colore di prima e per un momento con l'elenco di
    /// prima; i corpi restavano sul disco fino a 30 giorni, e la ricerca senza rete li trovava. Con gli account Microsoft
    /// la cartella eliminata resta dentro Posta eliminata con lo stesso identificativo: restava anche fra i Preferiti
    /// (scelte del 12 ottobre, trovato con C-47).
    /// </summary>
    internal static void ForgetFolderKeys(AccountItem account, string folderId)
    {
        var mail = account.Provider.Account;
        var key = mail.Id + "|" + folderId;
        S.Favorites.RemoveAll(k => k == key);
        S.HiddenFolders.RemoveAll(k => k == key);
        S.CollapsedFolders.RemoveAll(k => k == key);
        S.FolderColors.Remove(key);
        if (S.FolderOrder.TryGetValue(mail.Id, out var order)) order.RemoveAll(id => id == folderId);
        Store.Save();
        BodyCache.ForgetFolder(mail, folderId);
        MailCache.ForgetFolder(mail, folderId);
    }

    void OnFolderSelected(object sender, SelectionChangedEventArgs e)
    {
        if (selectingFolder || sender is not ListBox { SelectedItem: FolderItem item }) return;
        CloseFoldersOver();
        SelectFolder(item);
    }

    /// <summary>Una sola cartella selezionata fra tutti gli account.</summary>
    void SelectFolder(FolderItem item)
    {
        MarkSelectedFolder(item);
        // All'avvio (cartelle dalla copia locale) gli elenchi degli account non sono ancora disegnati: si ripete dopo.
        Dispatcher.BeginInvoke(() => { if (folder == item) MarkSelectedFolder(item); }, DispatcherPriority.Loaded);

        folder = item;
        unified = null;
        searching = false;
        SearchBox.Text = "";
        bool focusable = S.IsFocused(item.Account.Provider.Account) && item.Folder.Kind == FolderKind.Inbox;
        FocusedTabs.Visibility = focusable ? Visibility.Visible : Visibility.Collapsed;
        focused = focusable ? FocusedTab.IsChecked == true : null;
        // Filtro, ordinamento e "Vai a" valgono per la cartella aperta: una cartella nuova parte dalla vista normale.
        listOptions = ListOptions.Default;
        rows.Sort = SortBy.Date;
        UpdateListHeader();
        ShowSearchScope();
        reloading = ReloadAsync(fromTop: true);
    }

    /// <summary>Il caricamento dell'elenco della cartella scelta per ultima.</summary>
    Task reloading = Task.CompletedTask;

    // ---------------------------------------------------------------- intestazione dell'elenco

    ListOptions listOptions = ListOptions.Default;

    void SaveFirstPage(MailAccount account, string folderId, bool? focusedTab, List<MessageSummary> page)
    {
        if (listOptions.IsDefault) MailCache.SaveMessages(account, folderId, focusedTab, page);
    }

    static string FilterName(MessageFilter filter) => filter switch
    {
        MessageFilter.Unread => "Da leggere",
        MessageFilter.Flagged => "Contrassegnati",
        MessageFilter.Attachments => "Con allegati",
        _ => "Tutti",
    };

    /// <summary>Titolo (con filtro e "Vai a" attivi), stella dei Preferiti e pulsanti colorati quando fanno qualcosa.</summary>
    void UpdateListHeader()
    {
        // Nelle viste di tutti gli account filtro, "Vai a" e ordinamento non ci sono: ogni account li farebbe a modo suo.
        JumpButton.Visibility = FilterButton.Visibility = SortButton.Visibility = folder == null && unified != null ? Visibility.Collapsed : Visibility.Visible;
        if (folder == null && unified is { } view)
        {
            // Come in Thunderbird il nome della vista; di quale account è ogni riga lo dice la riga.
            ListTitle.Text = view.Name;
            ListTitle.ToolTip = view.Tip;
            StarButton.Visibility = JumpResetButton.Visibility = Visibility.Collapsed;
            return;
        }
        if (folder == null) return;
        var title = folder.Name;
        if (listOptions.Filter != MessageFilter.All) title += " · " + FilterName(listOptions.Filter);
        if (listOptions.UpTo is { } upTo) title += " · fino al " + upTo.ToString("d MMM yyyy", Italian);
        ListTitle.Text = title;
        ListTitle.ToolTip = title;
        // Con più account "Posta in arrivo" da sola non dice di quale: accanto, più piccolo, l'account.
        if (accounts.Count > 1)
        {
            var who = new System.Windows.Documents.Run("  " + folder.Account.ShortName) { FontSize = 13, FontWeight = FontWeights.Normal };
            who.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "SecondaryTextBrush");
            ListTitle.Inlines.Add(who);
            ListTitle.ToolTip = $"{title}, {folder.Account.Header}";
        }

        bool favorite = S.Favorites.Contains(folder.Key);
        StarGlyph.Text = favorite ? "" : "";
        StarButton.ToolTip = favorite ? "Rimuovi da Preferiti" : "Aggiungi a Preferiti";
        StarButton.Visibility = only == null ? Visibility.Visible : Visibility.Collapsed;

        Brush? Active(bool on) => on ? (Brush)FindResource("AccentTextFillColorPrimaryBrush") : null;
        void Paint(TextBlock glyph, bool on)
        {
            if (Active(on) is { } brush) glyph.Foreground = brush;
            else glyph.ClearValue(TextBlock.ForegroundProperty);
        }
        Paint(FilterGlyph, listOptions.Filter != MessageFilter.All);
        Paint(JumpGlyph, listOptions.UpTo != null);
        Paint(SortGlyph, listOptions.Sort != SortBy.Date || listOptions.Ascending);
        JumpResetButton.Visibility = listOptions.UpTo != null ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnListHeaderSize(object sender, SizeChangedEventArgs e) => FitTitle();

    /// <summary>Il titolo lungo si accorcia con i puntini: stella e pulsanti restano sempre visibili.</summary>
    void FitTitle()
    {
        double used = HeaderButtons.ActualWidth + (StarButton.IsVisible ? StarButton.ActualWidth + 4 : 0)
                      + (SelectAllBox.IsVisible ? SelectAllBox.ActualWidth + 10 : 0) + 8;
        ListTitle.MaxWidth = Math.Max(60, ListHeader.ActualWidth - used);
    }

    void ApplyListOptions(ListOptions options)
    {
        if (folder == null || options == listOptions) return;
        listOptions = options;
        rows.Sort = options.Sort;
        if (searching) OnClearSearch(this, new RoutedEventArgs());
        UpdateListHeader();
        _ = ReloadAsync(fromTop: true);
    }

    void OnStar(object sender, RoutedEventArgs e)
    {
        if (folder == null) return;
        ToggleFavorite(folder);
        UpdateListHeader();
    }

    void OnFilterMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = FilterButton, Placement = PlacementMode.Bottom };
        void Option(MessageFilter filter, string glyph, Brush? color)
        {
            var item = MenuEntry(FilterName(filter), glyph, color, (_, _) => ApplyListOptions(listOptions with { Filter = filter }));
            item.IsChecked = listOptions.Filter == filter;
            menu.Items.Add(item);
        }
        Option(MessageFilter.All, "", null);
        Option(MessageFilter.Unread, "", null);
        Option(MessageFilter.Flagged, "", ContextMenus.Brush(ContextMenus.Tone.Red));
        Option(MessageFilter.Attachments, "", null);
        menu.IsOpen = true;
    }

    /// <summary>
    /// Ordina per data, mittente o oggetto, in un verso o nell'altro. Gmail via IMAP ordina sul server solo per data:
    /// mittente e oggetto lì restano spenti, perché varrebbero solo per i messaggi già caricati.
    /// </summary>
    /// <summary>Perché mittente e oggetto sono spenti: prima diceva "Gmail" anche per gli account IMAP (audit, F-19).</summary>
    internal static string SortOnlyByDate(AccountKind kind) => kind == AccountKind.Gmail
        ? "Gmail permette di ordinare tutta la cartella solo per data."
        : "Con questo account Missiva ordina tutta la cartella solo per data.";

    void OnSortMenu(object sender, RoutedEventArgs e)
    {
        if (folder == null) return;
        var provider = folder.Account.Provider;
        var menu = new ContextMenu { PlacementTarget = SortButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = new TextBlock { Text = "Ordina per", FontWeight = FontWeights.SemiBold }, IsHitTestVisible = false, Focusable = false });
        void By(SortBy sort, string name, string glyph)
        {
            bool possible = provider.SortsOnServer(sort);
            var item = MenuEntry(name, glyph, null, (_, _) => ApplyListOptions(listOptions with { Sort = sort, Ascending = sort != SortBy.Date }), possible);
            item.IsChecked = listOptions.Sort == sort;
            if (!possible)
            {
                item.ToolTip = SortOnlyByDate(provider.Account.Kind);
                ToolTipService.SetShowOnDisabled(item, true);
            }
            menu.Items.Add(item);
        }
        By(SortBy.Date, "Data", "");
        By(SortBy.From, "Mittente", "");
        By(SortBy.Subject, "Oggetto", "");
        menu.Items.Add(new Separator());
        bool date = listOptions.Sort == SortBy.Date;
        void Order(bool ascending, string name, string glyph)
        {
            var item = MenuEntry(name, glyph, null, (_, _) => ApplyListOptions(listOptions with { Ascending = ascending }));
            item.IsChecked = listOptions.Ascending == ascending;
            menu.Items.Add(item);
        }
        Order(false, date ? "Dal più recente" : "Dalla Z alla A", "");
        Order(true, date ? "Dal meno recente" : "Dalla A alla Z", "");
        menu.IsOpen = true;
    }

    void OnJumpTo(object sender, RoutedEventArgs e)
    {
        JumpCalendar.DisplayDateEnd = DateTime.Today;
        JumpCalendar.SelectedDate = listOptions.UpTo;
        JumpCalendar.DisplayDate = listOptions.UpTo ?? DateTime.Today;
        JumpPopup.IsOpen = true;
    }

    /// <summary>Vai a: i messaggi arrivati fino al giorno scelto, dal più recente.</summary>
    void OnJumpDate(object sender, SelectionChangedEventArgs e)
    {
        if (!JumpPopup.IsOpen || JumpCalendar.SelectedDate is not { } day) return;
        JumpPopup.IsOpen = false;
        Mouse.Capture(null); // il calendario trattiene il mouse dopo il clic
        ApplyListOptions(listOptions with { UpTo = day, Sort = SortBy.Date, Ascending = false });
    }

    void OnJumpReset(object sender, RoutedEventArgs e)
    {
        JumpPopup.IsOpen = false;
        ApplyListOptions(listOptions with { UpTo = null });
    }

    /// <summary>
    /// Seleziona: caselle su tutti i messaggi, e un clic aggiunge o toglie dalla selezione. Segue Checked e Unchecked, non
    /// Click: UI Automation (Toggle) e Assistente vocale accendevano il pulsante senza cambiare modalità.
    /// </summary>
    void OnSelectMode(object sender, RoutedEventArgs e)
    {
        bool on = SelectModeButton.IsChecked == true;
        MessageList.SelectionMode = on ? SelectionMode.Multiple : SelectionMode.Extended;
        MessageList.Tag = on ? "select" : null;
        MessageRowElement.RefreshAll(); // le righe mettono o tolgono la casella
        SelectAllBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SelectAllBox.IsChecked = false;
        Dispatcher.BeginInvoke(FitTitle, DispatcherPriority.Loaded);
    }

    void OnSelectAll(object sender, RoutedEventArgs e)
    {
        if (SelectAllBox.IsChecked == true) MessageList.SelectAll();
        else MessageList.UnselectAll();
    }

    /// <param name="item">null durante la ricerca: come in Outlook nessuna cartella è selezionata.</param>
    void MarkSelectedFolder(FolderItem? item)
    {
        selectingFolder = true;
        foreach (var list in FindLists())
            list.SelectedItem = list.Items.Contains(item) ? item : null;
        FavoritesList.SelectedItem = item != null && favorites.Contains(item) ? item : null;
        UnifiedList.SelectedItem = null; // una cartella sola, o nessuna durante la ricerca
        selectingFolder = false;
        markedFolder = item;
        markedUnified = null;
        SidebarMarks.Mark(accounts, item);
        UpdateGroupMarks();
    }

    /// <summary>Gli account della barra laterale (per le prove: DevTools).</summary>
    internal IReadOnlyList<AccountItem> TestAccounts() => accounts;

    FolderItem? markedFolder;
    UnifiedItem? markedUnified;

    /// <summary>Preferiti e Tutti gli account chiusi con la cartella o la vista aperta dentro: la loro intestazione si evidenzia (SidebarMarks).</summary>
    void UpdateGroupMarks()
    {
        FavoritesHeaderMark.Visibility = SidebarMarks.FavoritesHeader(S.FavoritesCollapsed, markedFolder, favorites) ? Visibility.Visible : Visibility.Collapsed;
        UnifiedHeaderMark.Visibility = SidebarMarks.UnifiedHeader(S.UnifiedCollapsed, markedUnified) ? Visibility.Visible : Visibility.Collapsed;
    }

    IEnumerable<ListBox> FindLists()
    {
        foreach (var account in accounts)
            if (AccountsList.ItemContainerGenerator.ContainerFromItem(account) is ContentPresenter cp &&
                FindChild<ListBox>(cp) is { } list)
                yield return list;
    }

    static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }

    void OnFocusedTab(object sender, RoutedEventArgs e)
    {
        if (folder == null || FocusedTabs.Visibility != Visibility.Visible) return;
        focused = FocusedTab.IsChecked == true;
        _ = ReloadAsync(fromTop: true);
    }

    // ---------------------------------------------------------------- elenco

    /// <param name="fromTop">
    /// Un elenco diverso (un'altra cartella o vista, un'altra scheda, un altro filtro): parte dall'alto. Senza, con le righe
    /// della copia locale messe subito dopo aver tolto quelle di prima, lo ScrollViewer teneva la posizione dell'elenco di
    /// prima e la cartella nuova si apriva già scorsa (visto l'8 ottobre 2026: da 1500 px, la cartella aperta a 852).
    /// </param>
    async Task ReloadAsync(bool fromTop = false)
    {
        listVersion++;
        CancelListPrefetch();
        rows.Clear();
        if (fromTop && ListScroller is { } top)
        {
            top.ScrollToTop();
            StableScrollBar.Replaced(top);
        }
        rows.ShowAccounts = Reader.ShowAccount = unified != null;
        rows.ByDue = unified is { View: UnifiedView.Flagged }; // Contrassegnati di tutti gli account: per scadenza
        searchAcross = null;
        next = null;
        Reader.Clear();
        await LoadMoreAsync(first: true);
    }

    /// <summary>Prima pagina della cartella aperta, come è ora nell'elenco (per la copia locale e le anteprime).</summary>
    List<MessageSummary> firstPage = new();

    /// <param name="first">
    /// Prima pagina di un elenco nuovo: parte sempre, anche se ne sta caricando un altro (quello, superato, verrà
    /// scartato). Le pagine successive invece aspettano che la precedente sia arrivata.
    /// </param>
    async Task LoadMoreAsync(bool first = false)
    {
        if (unified != null && folder == null)
        {
            await LoadUnifiedAsync(first);
            return;
        }
        if (folder == null || searching || !first && (loading || next == null)) return;
        loading = true;
        var version = listVersion;
        var current = folder;
        var account = current.Account.Provider.Account;
        bool fromCache = false, nothingNew = false;
        if (first)
        {
            // Subito la copia locale, se c'è; il server la aggiorna appena risponde.
            firstPage = listOptions.IsDefault ? MailCache.LoadMessages(account, current.Folder.Id, focused) ?? new() : new();
            ApplyWoken(firstPage, current.Account, listOptions);
            ActionQueue.Apply(account.Id, firstPage); // senza rete: spostati e segni in coda come se fossero fatti
            fromCache = firstPage.Count > 0;
            if (fromCache) rows.Append(firstPage, current.Account);
            SetListStatus(fromCache ? null : "Caricamento…");
        }
        try
        {
            var clock = Stopwatch.StartNew();
            var page = await current.Account.Provider.GetMessagesAsync(current.Folder, focused, first ? null : next, listOptions);
            Log.Time($"elenco di {current.Folder.Name} ({account.Email}, {(first ? "prima pagina" : "pagina successiva")}{(fromCache ? ", copia locale già visibile" : "")})", clock);
            if (current.Folder.Kind != FolderKind.Junk) AddressBook.Received(page.Messages); // per i suggerimenti dei destinatari
            if (version != listVersion) return; // nel frattempo è cambiata cartella
            ApplyWoken(page.Messages, current.Account, listOptions);
            ActionQueue.Apply(account.Id, page.Messages); // la rete è tornata, ma la coda non è ancora ripartita
            if (first) ShowFirstPage(page.Messages, current.Account, fromCache);
            else
            {
                // Dopo una prima pagina riletta e unita (MergeFirstPage) le pagine dopo si richiedono da capo: una già tutta
                // nell'elenco non aggiunge righe, e allora si chiede subito la successiva.
                int before = rows.Rows.Count;
                AppendKeepingPosition(() => rows.Append(page.Messages, current.Account));
                nothingNew = rows.Rows.Count == before && page.Messages.Count > 0;
            }
            next = page.Next;
            SetListStatus(rows.Rows.Count == 0 ? "Non ci sono messaggi qui." : null);
            if (first) SaveFirstPage(account, current.Folder.Id, focused, firstPage);
            _ = FillPreviewsAsync(current, version, first ? firstPage : page.Messages, prefetch: first);
        }
        catch (Exception ex)
        {
            Log.Error("elenco messaggi", ex);
            if (version == listVersion && !fromCache) SetListStatus(Network.Explain(ex) ?? "Non riesco a caricare i messaggi: " + ex.Message, () => _ = LoadMoreAsync(first));
        }
        finally
        {
            if (version == listVersion) loading = false; // un caricamento superato non sblocca quello nuovo
        }
        if (nothingNew && version == listVersion && next != null) await LoadMoreAsync();
    }

    /// <summary>
    /// La prima pagina arrivata dal server. Se sono gli stessi messaggi della copia locale si aggiornano solo letto e
    /// contrassegno; altrimenti l'elenco si ricostruisce, tenendo le anteprime già note e la selezione.
    /// </summary>
    void ShowFirstPage(List<MessageSummary> fresh, AccountItem account, bool fromCache)
    {
        var known = firstPage.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        // I destinatari mancano nelle copie locali salvate prima che servissero (Posta inviata e Bozze): allora si ricostruisce.
        static string People(MessageSummary m) => string.Join(";", m.To.Select(a => a.Email));
        if (fromCache && fresh.Select(m => m.Id).SequenceEqual(firstPage.Select(m => m.Id)) &&
            fresh.Select(People).SequenceEqual(firstPage.Select(People)))
        {
            var rowsById = rows.Rows.GroupBy(r => r.Summary.Id).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var m in fresh)
            {
                var old = known[m.Id];
                if (old.IsRead == m.IsRead && old.IsFlagged == m.IsFlagged) continue;
                old.IsRead = m.IsRead;
                old.IsFlagged = m.IsFlagged;
                foreach (var row in rowsById.GetValueOrDefault(m.Id) ?? new())
                {
                    row.IsRead = m.IsRead;
                    row.IsFlagged = m.IsFlagged;
                }
            }
            return;
        }

        foreach (var m in fresh)
            if (m.Preview.Length == 0 && known.TryGetValue(m.Id, out var old)) m.Preview = old.Preview;
        firstPage = fresh;
        if (!fromCache)
        {
            rows.Append(fresh, account);
            return;
        }
        var selectedIds = Selected.Select(i => i.Summary.Id).ToHashSet();
        regrouping = true;
        rows.Clear();
        rows.Append(fresh, account);
        foreach (var item in rows.Rows.Where(r => selectedIds.Contains(r.Summary.Id))) MessageList.SelectedItems.Add(item);
        regrouping = false;
        UpdateCommands();
        MarkArrived(rows.Rows.Where(r => !known.ContainsKey(r.Summary.Id))); // i messaggi che la copia locale non aveva
    }

    /// <summary>
    /// La prima pagina riletta (un avviso dell'IDLE, la coda ripartita) quando l'elenco ha già le pagine dopo, caricate
    /// scorrendo: restano, e dei messaggi più recenti dell'ultimo della pagina riletta escono solo quelli che il server non ha
    /// più. Prima l'elenco si rifaceva con la sola prima pagina e le pagine caricate sparivano. Solo per data, dal più recente
    /// (per mittente o oggetto la pagina non dice dove stanno gli altri); falso se non c'è nulla da unire.
    /// </summary>
    bool MergeFirstPage(List<MessageSummary> fresh, AccountItem account)
    {
        if (listOptions.Sort != SortBy.Date || listOptions.Ascending || fresh.Count == 0) return false;
        var loaded = rows.Rows.SelectMany(MessageRows.MessagesOf).DistinctBy(m => (m.FolderId, m.Id)).ToList();
        var freshIds = fresh.Select(m => m.Id).ToHashSet();
        var oldest = fresh.Min(m => m.Date);
        // Più vecchi dell'ultimo della pagina: le pagine caricate scorrendo (anche i messaggi scesi dalla prima alla seconda).
        var older = loaded.Where(m => !freshIds.Contains(m.Id) && m.Date <= oldest).ToList();
        if (older.Count == 0) return false;
        if (fresh.Select(m => m.Id).SequenceEqual(firstPage.Select(m => m.Id)))
        {
            ShowFirstPage(fresh, account, fromCache: true); // la stessa prima pagina: solo letto e contrassegno
            return true;
        }
        var known = loaded.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var m in fresh)
            if (m.Preview.Length == 0 && known.TryGetValue(m.Id, out var old)) m.Preview = old.Preview;
        firstPage = fresh;
        var selectedIds = Selected.Select(i => i.Summary.Id).ToHashSet();
        regrouping = true;
        rows.Clear();
        rows.Append(fresh.Concat(older.OrderByDescending(m => m.Date)), account);
        foreach (var item in rows.Rows.Where(r => selectedIds.Contains(r.Summary.Id))) MessageList.SelectedItems.Add(item);
        regrouping = false;
        UpdateCommands();
        MarkArrived(rows.Rows.Where(r => !known.ContainsKey(r.Summary.Id)));
        Log.Write($"elenco riletto: prima pagina unita a {older.Count} messaggi già caricati scorrendo");
        return true;
    }

    /// <summary>Anteprime che mancano (Gmail): arrivano a gruppi, in sottofondo, su una connessione separata.</summary>
    /// <remarks>Ognuno nella sua cartella: i risultati della ricerca in tutte le cartelle non stanno in quella aperta.</remarks>
    /// <param name="prefetch">Prima pagina dell'elenco o della ricerca: dopo le anteprime, i primi corpi in anticipo.</param>
    async Task FillPreviewsAsync(FolderItem current, int version, List<MessageSummary> messages, bool prefetch = false)
    {
        var missing = messages.Where(m => m.Preview.Length == 0).ToList();
        for (int i = 0; i < missing.Count && version == listVersion; i += 10)
        {
            var batch = missing.Skip(i).Take(10).ToList();
            try
            {
                foreach (var inFolder in batch.GroupBy(m => m.FolderId))
                {
                    var where = inFolder.Key == current.Folder.Id ? current.Folder : new MailFolder { Id = inFolder.Key, Name = "" };
                    var previews = await current.Account.Provider.GetPreviewsAsync(where, inFolder.ToList());
                    if (previews.Count == 0) return; // il provider le dà già con l'elenco
                    foreach (var m in inFolder) if (previews.TryGetValue(m.Id, out var p)) m.Preview = p;
                }
            }
            catch (Exception ex)
            {
                Log.Error("anteprime", ex);
                return;
            }
            if (version != listVersion) return;
            var ids = batch.Select(m => m.Id).ToHashSet();
            foreach (var row in rows.Rows.Where(r => ids.Contains(r.Summary.Id))) row.RefreshPreview();
        }
        if (version == listVersion && messages == firstPage)
            SaveFirstPage(current.Account.Provider.Account, current.Folder.Id, focused, firstPage);
        if (version == listVersion && prefetch) await PrefetchListAsync(current.Account.Provider, messages);
    }

    // ---------------------------------------------------------------- corpi in anticipo

    /// <summary>Quanti messaggi in cima all'elenco si scaricano in anticipo (Gmail).</summary>
    const int PrefetchCount = 10;
    /// <summary>
    /// Annullati cambiando cartella o ricerca (listPrefetches, uno per account: nella vista di tutti gli account ognuno ha i
    /// suoi) e aprendo un altro messaggio (neighborPrefetch).
    /// </summary>
    readonly Dictionary<IMailProvider, CancellationTokenSource> listPrefetches = new();
    CancellationTokenSource neighborPrefetch = new();

    void CancelListPrefetch()
    {
        foreach (var running in listPrefetches.Values) running.Cancel();
        listPrefetches.Clear();
    }

    /// <summary>
    /// Come il nuovo Outlook, che precarica i messaggi: finito l'elenco, anteprime comprese, i corpi dei primi dieci, prima i
    /// non letti. Solo dove il provider lo fa (Gmail), mai dallo Spam.
    /// </summary>
    async Task PrefetchListAsync(IMailProvider provider, List<MessageSummary> messages)
    {
        if (!provider.PrefetchesMany) return;
        if (listPrefetches.TryGetValue(provider, out var old)) old.Cancel();
        var mine = listPrefetches[provider] = new CancellationTokenSource();
        var junk = JunkOf(provider);
        var first = messages.Take(PrefetchCount).Where(m => m.FolderId != junk && m.LabelKind != FolderKind.Junk).OrderBy(m => m.IsRead).ToList();
        try { await provider.PrefetchAsync(first, mine.Token); }
        catch (Exception ex) { Log.Error("corpi in anticipo", ex); }
        // Anche su disco, cifrati (BodyCache): i messaggi più recenti si leggono senza rete, anche mai aperti. Non le bozze,
        // nemmeno quelle senza il segno \Draft.
        var drafts = accounts.FirstOrDefault(a => a.Provider == provider) is { } owner ? Special(owner, FolderKind.Drafts)?.Id : null;
        int saved = 0;
        foreach (var m in first)
            if (m.FolderId != drafts && provider.KeptBody(m) is { } body)
            {
                SenderLogos.NoteBody(provider.Account, m, body); // le Authentication-Results arrivano con il corpo: il dominio autenticato può avere il logo
                if (BodyCache.Save(provider.Account, m, body)) saved++;
            }
        if (saved > 0) Log.Write($"copia su disco di {saved} messaggi scaricati in anticipo ({provider.Account.Email})");
    }

    /// <summary>
    /// Il messaggio nel riquadro e, in anticipo, i suoi vicini nell'elenco: frecce e Ctrl+. li trovano già scaricati. Gmail
    /// chiede il successivo e il precedente appena il corpo si vede (prima gli toglierebbero banda), sulla connessione in
    /// sottofondo; Graph solo il successivo, anche dopo la conversazione, per non superare le quattro richieste insieme che
    /// Outlook accetta per casella.
    /// </summary>
    async Task ShowInReaderAsync(MessageItem item)
    {
        neighborPrefetch.Cancel();
        neighborPrefetch = new CancellationTokenSource();
        var cancel = neighborPrefetch.Token;
        bool many = item.Provider.PrefetchesMany;
        var shown = Reader.ShowAsync(item.Provider, item.Summary, S.ReadingWholeConversation, JunkOf(item.Provider),
            many ? () => _ = PrefetchNeighborsAsync(item, cancel) : null);
        if (many) return;
        await shown;
        await PrefetchNeighborsAsync(item, cancel);
    }

    async Task PrefetchNeighborsAsync(MessageItem item, CancellationToken cancel)
    {
        if (cancel.IsCancellationRequested) return;
        int k = MessageList.Items.IndexOf(item);
        var junk = JunkOf(item.Provider);
        // Il vicino dello stesso account: nella vista di tutti gli account quello subito sotto può essere di un altro, e il
        // corpo si chiede a chi ce l'ha (fino a cinque righe più in là).
        MessageItem? Near(int step)
        {
            for (int i = k + step, n = 0; k >= 0 && i >= 0 && i < MessageList.Items.Count && n < 5; i += step, n++)
                if (MessageList.Items[i] is MessageItem m && m.Provider == item.Provider) return m;
            return null;
        }
        var around = (item.Provider.PrefetchesMany ? new[] { Near(1), Near(-1) } : [Near(1)])
            .OfType<MessageItem>()
            .Where(m => m.Summary.FolderId != junk && m.Summary.LabelKind != FolderKind.Junk)
            .Select(m => m.Summary).ToList();
        try { await item.Provider.PrefetchAsync(around, cancel); }
        catch (Exception ex) { Log.Error("corpi in anticipo", ex); }
    }

    /// <param name="retry">
    /// Per un elenco o una ricerca non riusciti: accanto al testo un link "Riprova" che rifà la stessa richiesta. Prima c'era
    /// solo la riga con l'errore, e per riprovare bisognava cambiare cartella e tornare (audit, F-44 punto 6).
    /// </param>
    void SetListStatus(string? text, Action? retry = null)
    {
        ListStatus.Text = text ?? ""; // toglie anche il link di prima
        if (retry != null && !string.IsNullOrEmpty(text))
        {
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Riprova"));
            link.Click += (_, _) => retry();
            ListStatus.Inlines.Add(text.EndsWith('.') ? " " : ". ");
            ListStatus.Inlines.Add(link);
        }
        ListStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    ScrollViewer? listScroller;

    /// <summary>
    /// Lo ScrollViewer dell'elenco dei messaggi, quello che l'elenco ha adesso: al cambio di tema WPF rimette il modello
    /// dell'elenco, e quello tenuto da parte restava fuori dall'albero (audit del 7 ottobre 2026, B-05).
    /// </summary>
    ScrollViewer? ListScroller
    {
        get
        {
            if (listScroller != null && !listScroller.IsDescendantOf(MessageList)) listScroller = null;
            return listScroller ??= FindScroller(MessageList);
        }
    }

    /// <summary>
    /// La barra stabile (StableScrollBar) e la guardia del hit test (ScrollHitGuard) sullo ScrollViewer che l'elenco ha
    /// adesso: all'avvio, e di nuovo quando WPF rimette il modello dell'elenco (cambio di tema, anche quello automatico di
    /// Windows). Prima si attaccavano una volta sola: dopo un cambio di tema, fino al riavvio, il cursore della barra tornava
    /// a saltare, il mouse fermo tornava a partecipare allo scorrimento e la posizione tenuta alle pagine successive
    /// riguardava lo ScrollViewer di prima (visto con --theme-flip e "scorrimento stato"). Una volta per ScrollViewer.
    /// </summary>
    void AttachListScrolling()
    {
        var viewer = ListScroller;
        StableScrollBar.Attach(viewer);
        ScrollHitGuard.Attach(MessageList, viewer);
    }

    /// <summary>
    /// Aggiunge righe in coda (una pagina successiva) tenendo ferma la posizione dell'elenco. Con l'elenco in fondo, e anche a
    /// qualche schermata, WPF al layout dopo l'aggiunta porta la vista in fondo alle righe nuove (misurato il 29 settembre
    /// 2026: 40 righe di 74 px, posizione +2968 px, e 25-70 ms di layout per le righe del posto sbagliato): si saltavano tutte
    /// le righe appena caricate, a ogni pagina. Rimettere la stessa posizione subito dopo l'aggiunta, prima del layout, la
    /// tiene, senza un secondo layout.
    /// </summary>
    void AppendKeepingPosition(Action append)
    {
        var scroller = ListScroller;
        double kept = scroller?.VerticalOffset ?? 0;
        append();
        if (scroller != null && kept > 0) scroller.ScrollToVerticalOffset(kept);
    }

    static ScrollViewer? FindScroller(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScroller(child) is { } deeper) return deeper;
        }
        return null;
    }

    void OnListScrolled(object sender, ScrollChangedEventArgs e)
    {
        // Uno ScrollViewer che non è quello tenuto da parte: WPF ha rimesso il modello dell'elenco (AttachListScrolling).
        if (e.OriginalSource is ScrollViewer current && !ReferenceEquals(current, listScroller) && ReferenceEquals(current.TemplatedParent, MessageList))
        {
            listScroller = current;
            AttachListScrolling();
        }
        if (e.VerticalOffset > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 300) _ = searching ? SearchPageAsync(first: false) : LoadMoreAsync();
    }

    /// <summary>Nuovi messaggi dal controllo periodico: salgono in cima se la cartella è aperta.</summary>
    /// <param name="unread">I non letti della Posta in arrivo contati dal server (null se non sono arrivati).</param>
    /// <summary>
    /// Le cartelle dell'account già arrivate nella barra laterale (per le regole: Sposta in, Copia in, il Cestino), altrimenti
    /// chieste al provider.
    /// </summary>
    public async Task<List<MailFolder>> FoldersOfAsync(IMailProvider provider) =>
        accounts.FirstOrDefault(a => a.Provider.Account.Id == provider.Account.Id) is { Folders.Count: > 0 } item ? item.Snapshot() : await provider.GetFoldersAsync();

    /// <param name="ticket">
    /// Con unread: il biglietto preso prima di chiedere il conteggio (UnreadSync.Ticket). Un numero chiesto prima di una
    /// lettura fatta qui, o con una scrittura di Missiva in viaggio, non si scrive: si riconta (App.RecountSoon).
    /// </param>
    public void AddIncoming(IMailProvider provider, List<MessageSummary> incoming, int? unread = null, long? ticket = null, string who = "contati dal server")
    {
        // Anche nelle finestre di una sola cartella aperte sulla Posta in arrivo: solo le righe (ShowIncoming).
        if (incoming.Count > 0) foreach (var window in FolderWindows) window.ShowIncoming(provider, incoming);
        var inbox = accounts.FirstOrDefault(a => a.Provider == provider)?.Folders.FirstOrDefault(f => f.Folder.Kind == FolderKind.Inbox);
        if (inbox == null) return;
        if (unread == null) inbox.SetUnread(inbox.Unread + incoming.Count(m => !m.IsRead), $"{incoming.Count} arrivati");
        else if (!inbox.ApplyServerUnread(unread.Value, ticket, who)) App.Current.RecountSoon(provider);
        if (incoming.Count > 0) KeepInSavedPage(inbox, incoming);
        if (AddIncomingUnified(inbox.Account, incoming))
        {
            LogIncoming(inbox.Account, incoming.Count, null);
            return;
        }
        if (incoming.Count == 0) return;
        if (folder != inbox || searching || focused == false)
        {
            LogIncoming(inbox.Account, incoming.Count, searching ? "ricerca aperta" : folder != inbox ? "aperta un'altra cartella" : "aperta la scheda Altra");
            return;
        }
        MarkArrived(PrependRows(incoming, inbox.Account));
        LogIncoming(inbox.Account, incoming.Count, null);
    }

    /// <summary>
    /// Le finestre di una sola cartella aperte da questa ("Apri in un'altra finestra"). Il controllo dei nuovi messaggi,
    /// l'IDLE, la query delta e le regole parlano solo alla finestra principale (App), che li passa anche a loro: prima una
    /// seconda finestra sulla Posta in arrivo non vedeva i messaggi nuovi, quelli spostati dalle regole e i segni cambiati
    /// altrove finché non si rileggeva (audit dell'11 ottobre, C-60).
    /// </summary>
    IEnumerable<MainWindow> FolderWindows => only == null ? folderWindows.OfType<MainWindow>().ToList() : [];

    /// <summary>
    /// In una finestra di una sola cartella aperta sulla Posta in arrivo dell'account: i messaggi nuovi in cima, come nella
    /// principale. Solo le righe (copie sue dei messaggi): conteggi, copia locale e notifiche li tiene la finestra principale.
    /// </summary>
    void ShowIncoming(IMailProvider provider, List<MessageSummary> incoming)
    {
        var inbox = accounts.FirstOrDefault(a => a.Provider == provider)?.Folders.FirstOrDefault(f => f.Folder.Kind == FolderKind.Inbox);
        if (inbox == null || folder != inbox || searching || focused == false) return;
        var known = rows.Rows.SelectMany(MessageRows.MessagesOf).Where(m => m.FolderId == inbox.Folder.Id).Select(m => m.Id).ToHashSet();
        var fresh = incoming.Where(m => !known.Contains(m.Id)).Select(m => m.WithId(m.Id)).ToList();
        if (fresh.Count > 0) MarkArrived(PrependRows(fresh, inbox.Account));
    }

    /// <summary>
    /// I messaggi appena arrivati anche nella prima pagina salvata della loro Posta in arrivo: quella in memoria se è la
    /// cartella aperta, altrimenti la copia locale (MailCache.Prepend). La Posta in arrivo aperta dopo l'arrivo (un altro
    /// account, la vista di tutti gli account, un riavvio) li mostra subito in cima, senza aspettare il server: prima la copia
    /// era quella dell'ultima apertura. Non per gli account con Evidenziata e Altra: il messaggio nuovo non dice in quale
    /// scheda sta, e il loro elenco arriva sempre fresco dal server.
    /// </summary>
    void KeepInSavedPage(FolderItem inbox, List<MessageSummary> incoming)
    {
        var account = inbox.Account.Provider.Account;
        if (S.IsFocused(account)) return;
        if (folder != inbox || searching || !listOptions.IsDefault)
        {
            MailCache.Prepend(account, inbox.Folder.Id, null, incoming);
            return;
        }
        var known = firstPage.Select(m => m.Id).ToHashSet();
        firstPage.InsertRange(0, incoming.Where(m => known.Add(m.Id)).OrderByDescending(m => m.Date));
        SaveFirstPage(account, inbox.Folder.Id, focused, firstPage);
    }

    /// <summary>
    /// Nel registro, anche nella versione installata, quando le righe dei messaggi nuovi entrano nell'elenco, o perché no:
    /// accanto a "notifica mostrata" dice se l'elenco li ha avuti nello stesso momento (segnalazione dell'autore, 7 ottobre 2026).
    /// </summary>
    void LogIncoming(AccountItem account, int count, string? notShown) =>
        Log.Write(notShown == null
            ? $"elenco: {count} nuovi di {account.LogName} in cima ({rows.Rows.Count} righe{(IsVisible ? "" : ", finestra nell'area di notifica")})"
            : $"elenco: {count} nuovi di {account.LogName} non aggiunti ({notShown}{(IsVisible ? "" : ", finestra nell'area di notifica")})");

    /// <summary>
    /// Righe nuove in cima all'elenco. I gruppi si rifanno (RegroupKeepingSelection) solo se una riga apre un gruppo che
    /// l'elenco non ha ancora (Oggi dopo mezzanotte, Fissati), che WPF metterebbe in fondo; in un gruppo che c'è già WPF la
    /// mette da solo al suo posto. Prima si rifacevano a ogni messaggio nuovo: tutte le righe visibili ricreate e, con
    /// l'elenco scorso in giù, la vista portata altrove (audit, v2 V-12 e v1 V-17).
    /// </summary>
    List<MessageItem> PrependRows(List<MessageSummary> incoming, AccountItem account, bool always = false)
    {
        var shown = S.ShowDateHeaders && view.Groups is { } groups ? groups.OfType<CollectionViewGroup>().Select(g => g.Name as string).ToHashSet() : null;
        var added = rows.Prepend(incoming, account);
        if (always || shown == null || added.Any(a => !shown.Contains(a.Group))) RegroupKeepingSelection();
        return added;
    }

#if DEBUG
    /// <summary>
    /// Prove ("arrivo di prova" in MISSIVA_KEYS): una riga finta in cima alla cartella aperta, come un messaggio appena
    /// arrivato, per vedere l'ingresso (lampi.ps1 -List). Solo nell'elenco: la casella non sa nulla.
    /// </summary>
    /// <summary>
    /// Prove ("aggiungi 40 0" in MISSIVA_KEYS): porta l'elenco a X pixel dal fondo e ci aggiunge N righe finte più vecchie di
    /// tutte, come una pagina successiva, scrivendo dove sta l'elenco prima, dopo l'aggiunta e dopo il layout.
    /// </summary>
    internal void TestAppend(int count, double fromEnd, int fix = 0)
    {
        if (folder == null || ListScroller is not { } scroller || rows.Rows.Count == 0) return;
        scroller.ScrollToVerticalOffset(Math.Max(0, scroller.ScrollableHeight - fromEnd));
        MessageList.UpdateLayout();
        double before = scroller.VerticalOffset, extent = scroller.ExtentHeight, maxBefore = scroller.ScrollableHeight;
        var oldest = rows.Rows.Min(r => r.Summary.Date);
        var fake = Enumerable.Range(1, count).Select(k => new MessageSummary
        {
            Id = $"finta-{Environment.TickCount}-{k}", FolderId = folder.Folder.Id, Subject = $"Riga finta {k}", Date = oldest.AddMinutes(-k),
            From = new Address { Name = "Prova", Email = $"prova{k}@example.com" }, Preview = "Anteprima finta per provare la pagina successiva.",
        }).ToList();
        var clock = Stopwatch.StartNew();
        if (fix == 3) AppendKeepingPosition(() => rows.Append(fake, folder.Account));
        else rows.Append(fake, folder.Account);
        double appended = clock.Elapsed.TotalMilliseconds, afterAppend = scroller.VerticalOffset;
        if (fix == 1) scroller.ScrollToVerticalOffset(before);
        MessageList.UpdateLayout();
        double layoutMs = clock.Elapsed.TotalMilliseconds - appended, afterLayout = scroller.VerticalOffset, fixMs = 0;
        if (fix == 2 && Math.Abs(afterLayout - before) > 1)
        {
            var second = Stopwatch.StartNew();
            scroller.ScrollToVerticalOffset(before);
            MessageList.UpdateLayout();
            fixMs = second.Elapsed.TotalMilliseconds;
        }
        Log.Write($"prova aggiungi {count} a {fromEnd:0} px dal fondo (rimedio {fix}): aggiunte in {appended:0} ms, layout in {layoutMs:0} ms, secondo layout {fixMs:0} ms, " +
                  $"posizione {before:0} (massimo {maxBefore:0}) -> {afterAppend:0} -> {afterLayout:0} -> {scroller.VerticalOffset:0}, altezza {extent:0} -> {scroller.ExtentHeight:0}");
    }

    /// <summary>
    /// Prova ("vetrina" in MISSIVA_KEYS): in cima all'elenco righe finte di ogni tipo (non letta, letta, oggetto e anteprima
    /// lunghi, allegati con una, tre e una pericolosa, contrassegni con scadenza, importante, categorie, bozza, inviata,
    /// conversazione, fissata, dove, ieri e più vecchie), sempre uguali: le foto della finestra con il modello di prima
    /// (MISSIVA_RIGA_VECCHIA=1) e con MessageRowElement si confrontano pixel per pixel.
    /// </summary>
    internal void TestShowcase()
    {
        if (folder == null) return;
        var day = DateTime.Today;
        int n = 0;
        MessageSummary M(string from, string subject, string preview, double hours, bool read = true, string[]? files = null, string[]? cats = null,
                         bool flagged = false, int? due = null, bool important = false, string conv = "", FolderKind? kind = null, string where = "", int daysAgo = 0)
        {
            n++;
            return new MessageSummary
            {
                Id = "vetrina-" + n, FolderId = folder.Folder.Id, StableId = "vetrina-" + n, Subject = subject, Preview = preview, IsRead = read,
                Date = day.AddDays(-daysAgo).AddHours(hours), From = new Address { Name = from, Email = "vetrina" + n + "@example.com" },
                HasAttachments = files != null, AttachmentNames = files?.ToList() ?? new(), Categories = cats?.ToList() ?? new(),
                IsFlagged = flagged, FlagDue = due == null ? null : day.AddDays(due.Value), IsImportant = important, ConversationId = conv,
                LabelKind = kind, FolderName = where, To = [new Address { Name = "Anna Bianchi", Email = "anna@example.com" }],
            };
        }
        string longSubject = "Aggiornamento del contratto di fornitura per il secondo semestre con le nuove condizioni di consegna e pagamento";
        string longPreview = "Buongiorno, in allegato trova la bozza aggiornata del contratto con le modifiche concordate nell'ultima riunione: consegne settimanali, pagamento a 60 giorni e una clausola sui ritardi. Resto a disposizione per qualsiasi chiarimento.";
        var list = new List<MessageSummary>
        {
            M("Mario Rossi", "Riunione di domani alle 10", "Ciao, ti ricordo la riunione di domani in sala grande.", 9.5, read: false),
            M("Amazon.it", "Il tuo ordine è stato spedito", "Il tuo pacco arriverà entro giovedì.", 9.2),
            M("Ufficio Acquisti Fornitori Riuniti S.p.A.", longSubject, longPreview, 9.0, read: false, files: ["Preventivo-2026-09.pdf"]),
            M("Élodie Zanetti", "Foto delle vacanze", "Guarda che belle!", 8.8, files: ["fattura.xlsx", "installa.exe", "Relazione-annuale-2025-versione-definitiva-molto-lunga.docx"]),
            M("Banca Aurora", "Scadenza del pagamento", "Ricorda di completare il pagamento entro ieri.", 8.6, flagged: true, due: -1),
            M("Luca Verdi", "Da fare domani", "Contrassegnato per domani.", 8.4, read: false, flagged: true, due: 1),
            M("Sara Neri", "Urgente: firma il documento", "Serve la tua firma entro oggi.", 8.2, important: true, cats: ["Lavoro", "Da pagare"]),
            M("Anna Bianchi", "Bozza di una risposta", "Testo della bozza.", 8.0, kind: FolderKind.Drafts),
            M("Io", "Messaggio inviato", "Testo inviato.", 7.8, kind: FolderKind.Sent),
            M("Paolo Gialli", "Conversazione sul progetto", "Ultimo messaggio della conversazione.", 7.6, read: false, conv: "vetrina-c1"),
            M("Paolo Gialli", "Re: Conversazione sul progetto", "Secondo messaggio.", 7.4, conv: "vetrina-c1"),
            M("Paolo Gialli", "Re: Conversazione sul progetto", "Primo messaggio.", 7.2, conv: "vetrina-c1"),
            M("Ada Lovelace", "Messaggio fissato in alto", "Questo resta in cima.", 7.0),
            M("Tutto Insieme", longSubject, longPreview, 6.8, read: false, files: ["a.pdf", "b.docx", "c.png"], cats: ["Lavoro"], flagged: true, due: 0, important: true, where: "Archivio"),
            M("Senza Anteprima", "Anteprima ancora vuota", "", 6.5),
            M("Ieri Sera", "Messaggio di ieri", "Anteprima di ieri.", 22.0, daysAgo: 1),
            M("Tre Giorni Fa", "Messaggio di tre giorni fa", "Anteprima.", 10.0, read: false, daysAgo: 3),
            M("Anno Scorso", "Messaggio di venti giorni fa", "Anteprima più vecchia.", 10.0, daysAgo: 20),
        };
        S.SetPinned(folder.Account.Provider.Account, "vetrina-13", true);
        rows.Prepend(list, folder.Account);
        RegroupKeepingSelection();
        ListScroller?.ScrollToTop();
        Log.Write("prova: vetrina di righe finte in cima all'elenco");
    }
    internal void TestHeights()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Math.Min(40, MessageList.Items.Count); i++)
        {
            MessageList.ScrollIntoView(MessageList.Items[i]);
            MessageList.UpdateLayout();
            if (MessageList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem c) sb.Append($"{i}:{c.ActualHeight:0.###} ");
        }
        Log.Write("prova altezze: " + sb);
    }

    internal void TestActions(int index)
    {
        if (index >= 0 && index < rows.Rows.Count) ShowKeyboardActions(rows.Rows[index]);
    }

    internal void TestSelectMode() => SelectModeButton.IsChecked = true;

    /// <summary>
    /// Prove ("scorrimento stato" in MISSIVA_KEYS; audit del 7 ottobre, B-05): lo ScrollViewer che l'elenco ha adesso è quello
    /// che la finestra tiene da parte, ha la barra stabile (StableScrollBar) e la guardia del hit test (ScrollHitGuard)?
    /// </summary>
    internal void TestScrollerState()
    {
        var now = FindScroller(MessageList);
        var bar = now?.Template?.FindName("PART_VerticalScrollBar", now) as System.Windows.Controls.Primitives.ScrollBar;
        Log.Write($"prova scorrimento stato: ScrollViewer dell'elenco {(now == null ? "non trovato" : ReferenceEquals(now, listScroller) ? "lo stesso tenuto dalla finestra" : listScroller == null ? "mai tenuto" : "DIVERSO da quello tenuto dalla finestra")}, " +
                  $"barra stabile {(now != null && StableScrollBar.Attached(now) ? "sì" : "NO")} (scala della barra 0..{bar?.Maximum:0.###}), guardia del hit test {(now != null && ScrollHitGuard.Attached(now) ? "sì" : "NO")}, " +
                  $"elenco a pixel {VirtualizingPanel.GetScrollUnit(MessageList) == ScrollUnit.Pixel}, tema {(ReadingView.Dark ? "scuro" : "chiaro")}");
    }

    /// <summary>
    /// Prove ("costo gruppi" in MISSIVA_KEYS; audit del 7 ottobre, B-09): quanto costa rifare i gruppi dell'elenco com'è adesso
    /// (view.Refresh), togliere e rimettere il raggruppamento come fa ApplySettings (due rifacimenti), e lo stesso dentro
    /// DeferRefresh (uno solo). Le righe visibili si ricreano al layout dopo, misurato a parte.
    /// </summary>
    internal void TestGroupCost()
    {
        double Time(Action action)
        {
            var clock = Stopwatch.StartNew();
            action();
            return clock.Elapsed.TotalMilliseconds;
        }
        void Twice()
        {
            view.GroupDescriptions.Clear();
            if (S.ShowDateHeaders) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MessageItem.Group)));
        }
        var refresh = new List<double>();
        var twice = new List<double>();
        var once = new List<double>();
        var layout = new List<double>();
        for (int i = 0; i < 4; i++)
        {
            refresh.Add(Time(view.Refresh));
            layout.Add(Time(MessageList.UpdateLayout));
            twice.Add(Time(Twice));
            layout.Add(Time(MessageList.UpdateLayout));
            once.Add(Time(() => { using (view.DeferRefresh()) Twice(); }));
            layout.Add(Time(MessageList.UpdateLayout));
        }
        static string Show(List<double> v) => string.Join(" ", v.Select(x => x.ToString("0.0")));
        Log.Write($"prova costo gruppi, {rows.Rows.Count} righe: view.Refresh {Show(refresh)} ms; togli e rimetti come ApplySettings {Show(twice)} ms; lo stesso in DeferRefresh {Show(once)} ms; " +
                  $"il layout dopo ognuno {Show(layout)} ms");
    }

    internal void TestScrollTo(double pixels) => ListScroller?.ScrollToVerticalOffset(pixels);

    /// <param name="regroup">"arrivo di prova vecchio": i gruppi rifatti sempre, com'era prima dell'audit (per il confronto).</param>
    internal void TestArrival(bool regroup = false)
    {
        if (folder == null) return;
        var fake = new MessageSummary
        {
            Id = "prova-" + Environment.TickCount, FolderId = folder.Folder.Id, Subject = "Messaggio di prova appena arrivato", Date = MessageItem.Today.Add(DateTime.Now.TimeOfDay),
            From = new Address { Name = "Arrivo di prova", Email = "arrivo@prova.example" }, Preview = "Una riga finta per provare l'ingresso in cima.",
        };
        // La prima riga che si vede (il suo posto nell'elenco e i pixel dal bordo della lista), prima e dopo.
        (MessageItem? Row, double Y) Top()
        {
            foreach (var row in rows.Rows)
                if (MessageList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem { IsVisible: true } c && c.TranslatePoint(new Point(0, 0), MessageList).Y is var y && y + c.ActualHeight > 1)
                    return (row, y);
            return (null, 0);
        }
        var top = Top();
        int groupsBefore = view.Groups?.Count ?? 0;
        var clock = Stopwatch.StartNew();
        double before = ListScroller?.VerticalOffset ?? 0;
        var added = PrependRows([fake], folder.Account, always: regroup);
        MarkArrived(added);
        double placed = clock.Elapsed.TotalMilliseconds;
        MessageList.UpdateLayout();
        var after = Top();
        Log.Write($"prova: riga finta arrivata in cima a {rows.Rows.Count} righe{(regroup ? " (gruppi rifatti, com'era)" : "")} in {placed:0.0} ms, layout {clock.Elapsed.TotalMilliseconds - placed:0.0} ms, " +
                  $"posizione {before:0} -> {ListScroller?.VerticalOffset ?? 0:0}, prima riga visibile {(top.Row == null ? -1 : rows.Rows.IndexOf(top.Row) - 1)} a {top.Y:0} px -> " +
                  $"{(after.Row == null ? -1 : rows.Rows.IndexOf(after.Row) - 1)} a {after.Y:0} px{(after.Row == top.Row ? " (la stessa)" : "")}, " +
                  $"gruppi {groupsBefore} -> {view.Groups?.Count ?? 0}, prima riga dell'elenco {(rows.Rows[0] == added[0] ? "quella nuova" : "un'altra")}, " +
                  $"primo gruppo {(view.Groups?.FirstOrDefault() as CollectionViewGroup)?.Name} con in cima {((view.Groups?.FirstOrDefault() as CollectionViewGroup)?.Items.FirstOrDefault() == added[0] ? "quella nuova" : "un'altra")}, scelte {MessageList.SelectedItems.Count}");
    }

    /// <summary>
    /// Prove ("stato elenco" in MISSIVA_KEYS): che cosa mostra l'elenco adesso (le prime righe con gruppo e letto, i gruppi
    /// nell'ordine di WPF con la loro prima riga, quante righe sono disegnate) e i non letti di ogni Posta in arrivo, per
    /// vedere dal registro se un messaggio arrivato è in cima, anche dopo la riapertura dall'area di notifica.
    /// </summary>
    internal void TestListState()
    {
        MessageList.UpdateLayout();
        string Row(MessageItem r) => $"{r.Summary.Id}{(r.IsRead ? "" : "*")}{(r.Count > 1 ? $"({r.Count})" : "")}[{r.Group}]";
        var groups = view.Groups?.OfType<CollectionViewGroup>().Select(g => $"{g.Name}:{g.ItemCount}:{(g.Items.FirstOrDefault() is MessageItem first ? first.Summary.Id : "-")}") ?? [];
        var drawn = rows.Rows.Where(r => MessageList.ItemContainerGenerator.ContainerFromItem(r) is ListBoxItem { IsVisible: true }).Select(r => r.Summary.Id).ToList();
        Log.Write($"prova stato elenco: {(unified != null ? "tutti gli account, " + unified.Name : folder == null ? "nessuna cartella" : folder.Name + " di " + folder.Account.LogName)}" +
                  $"{(searching ? ", ricerca" : "")}{(IsVisible ? "" : ", finestra nascosta")}, {rows.Rows.Count} righe: {string.Join(' ', rows.Rows.Take(8).Select(Row))}; " +
                  $"gruppi {string.Join(' ', groups)}; disegnate {drawn.Count} (prima {drawn.FirstOrDefault() ?? "-"}); " +
                  $"non letti {string.Join(' ', accounts.Select(a => $"{a.LogName}={a.InboxUnread}"))}, icona {badge}");
    }

    /// <summary>
    /// Prove ("apri posta 2" e "apri tutti" in MISSIVA_KEYS): la Posta in arrivo dell'account N, o quella di tutti gli
    /// account, come il clic nella barra laterale; il registro dice quante righe si vedono subito (dalla copia locale).
    /// </summary>
    internal void TestOpenInbox(int account)
    {
        if (account == 0) SelectUnified(unifiedItems.First(u => u.View == UnifiedView.Inbox));
        else if (accounts.ElementAtOrDefault(account - 1)?.Folders.FirstOrDefault(f => f.Folder.Kind == FolderKind.Inbox) is { } inbox) SelectFolder(inbox);
        else return;
        Log.Write($"prova: aperta {(account == 0 ? "la Posta in arrivo di tutti gli account" : "la Posta in arrivo dell'account " + account)}, subito {rows.Rows.Count} righe" +
                  $"{(rows.Rows.Count > 0 ? ", la prima " + rows.Rows[0].Summary.Id : "")}");
    }

    /// <summary>Prove ("scegli 2 3 4" in MISSIVA_KEYS): quelle righe selezionate insieme, come con Ctrl+clic.</summary>
    internal void TestChoose(List<int> indexes)
    {
        MessageList.SelectedItems.Clear();
        foreach (var i in indexes.Where(i => i >= 0 && i < rows.Rows.Count)) MessageList.SelectedItems.Add(rows.Rows[i]);
        Log.Write($"prova: scelte {MessageList.SelectedItems.Count} righe");
    }

    /// <summary>Prove ("anteprima" in MISSIVA_KEYS): la prima riga con un allegato che ha l'anteprima (PDF, immagine, testo), come il clic sulla sua pastiglia.</summary>
    internal void TestPreview()
    {
        static bool Shown(string name) => new[] { ".pdf", ".png", ".jpg", ".jpeg", ".txt" }.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        if (rows.Rows.FirstOrDefault(r => r.Summary.AttachmentNames.Any(Shown)) is not { } row)
        {
            Log.Write("prova: nessuna riga con un allegato da mostrare in anteprima");
            return;
        }
        Log.Write($"prova: anteprima dell'allegato della riga {rows.Rows.IndexOf(row)}");
        MessageList.SelectedItems.Clear();
        MessageList.SelectedItem = row;
        Reader.PreviewWhenReady(row.Summary.AttachmentNames.First(Shown));
    }
#endif

    /// <summary>
    /// Le righe appena arrivate in cima entrano crescendo (MessageItem.Arrived, l'animazione in MainWindow.xaml) invece di
    /// far saltare di colpo tutto l'elenco; poco dopo il segno si spegne, così una riga riciclata scorrendo non rientra.
    /// </summary>
    async void MarkArrived(IEnumerable<MessageItem> items)
    {
        var arrived = items.ToList();
        if (arrived.Count == 0) return;
        foreach (var item in arrived) item.Arrived = true;
        await Task.Delay(400);
        foreach (var item in arrived) item.Arrived = false;
    }

    /// <summary>
    /// La pastiglia di un allegato nella riga, come nel nuovo Outlook: il messaggio si apre e l'allegato va in anteprima
    /// appena arriva il corpo (o si apre, se non ha anteprima).
    /// </summary>
    void OnChipClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AttachmentChip chip } element || S.ReadingPane == ReadingPanePosition.Off) return;
        if (ItemsControl.ContainerFromElement(MessageList, element) is not ListBoxItem { DataContext: MessageItem item }) return;
        e.Handled = true;
        RowChipClicked(item, chip);
    }

    /// <summary>Il clic su una pastiglia disegnata dalla riga (MessageRowElement): come OnChipClick.</summary>
    internal void RowChipClicked(MessageItem item, AttachmentChip chip)
    {
        if (S.ReadingPane == ReadingPanePosition.Off) return;
        MessageList.SelectedItems.Clear();
        MessageList.SelectedItem = item;
        Reader.PreviewWhenReady(chip.Name);
    }

    internal void OnConversationToggle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MessageItem item }) rows.Toggle(item);
    }

    void OnCollapseConversations(object sender, RoutedEventArgs e) => rows.CollapseAll();

    // ---------------------------------------------------------------- lettura

    List<MessageItem> Selected => MessageList.SelectedItems.Cast<MessageItem>().ToList();

    /// <summary>Messaggi su cui agire: una conversazione chiusa porta con sé tutti i suoi messaggi, come in Outlook.</summary>
    static List<MessageSummary> Targets(IEnumerable<MessageItem> items) =>
        items.SelectMany(MessageRows.MessagesOf).DistinctBy(m => m.Id).ToList();

    /// <summary>Il giorno a cui sono fermi i gruppi e le date dell'elenco.</summary>
    DateTime shownDay = MessageItem.Today;

    /// <summary>
    /// Il giorno è cambiato da quando l'elenco è stato raggruppato (mezzanotte con Missiva aperta, anche nell'area di
    /// notifica, o il PC ripreso il giorno dopo): gruppi e date si ricalcolano, una volta al giorno. Chiamata ogni minuto dal
    /// timer di Posponi, alla riapertura dall'area di notifica e quando la finestra torna attiva: di solito un confronto fra
    /// due date. Con l'elenco scorso in giù la prima riga che si vede resta dov'è: rifare i gruppi ricrea le righe, e WPF
    /// rimetterebbe la vista a un'altezza stimata (provato: da 900 px la prima riga visibile cambiava di sette righe).
    /// </summary>
    internal void CheckDay()
    {
        var today = MessageItem.Today;
        if (today == shownDay) return;
        shownDay = today;
        if (!rows.NewDay()) return;
        var scroller = ListScroller;
        MessageItem? top = null;
        double y = 0;
        if (IsVisible && S.ShowDateHeaders && scroller is { VerticalOffset: > 0 })
            foreach (var row in rows.Rows)
                if (MessageList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem { IsVisible: true } shown
                    && shown.TranslatePoint(new Point(0, 0), MessageList).Y is var at && at + shown.ActualHeight > 1)
                {
                    (top, y) = (row, at);
                    break;
                }
        RegroupKeepingSelection();
        if (top != null && scroller != null)
        {
            MessageList.ScrollIntoView(top);
            MessageList.UpdateLayout();
            if (MessageList.ItemContainerGenerator.ContainerFromItem(top) is ListBoxItem again)
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset + again.TranslatePoint(new Point(0, 0), MessageList).Y - y);
        }
        Log.Write($"elenco: cambio di giorno, gruppi e date rifatti ({rows.Rows.Count} righe{(IsVisible ? "" : ", finestra nell'area di notifica")})");
    }

    /// <summary>
    /// WPF mette i gruppi nuovi (Fissati, oppure Oggi dopo mezzanotte) in fondo: si riordina tutto mantenendo la selezione.
    /// </summary>
    void RegroupKeepingSelection()
    {
        if (!S.ShowDateHeaders) return;
        var selected = Selected;
        regrouping = true;
        view.Refresh();
        foreach (var item in selected.Where(rows.Rows.Contains)) MessageList.SelectedItems.Add(item);
        regrouping = false;
        UpdateCommands();
    }

    void OnMessageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (regrouping) return;
        selectionChanges++;
        UpdateCommands();
        ShowKeyboardActions(selectingByKeyboard && MessageList.SelectedItems.Count == 1 ? MessageList.SelectedItem as MessageItem : null);
        selectingByKeyboard = false;
        if (S.ReadingPane == ReadingPanePosition.Off) return;
        if (MessageList.SelectedItems.Count == 1 && MessageList.SelectedItem is MessageItem item)
        {
            // Per identificativo: lo stesso messaggio può arrivare prima dalla copia locale e poi dal server.
            if (Reader.Selected is not { } shown || shown.Id != item.Summary.Id || shown.FolderId != item.Summary.FolderId)
            {
                if (item.IsDraft) item.Summary.IsDraft = true; // nelle Bozze il riquadro offre Modifica invece di Rispondi
                readerOpens++;
                _ = ShowInReaderAsync(item);
            }
        }
        else if (MessageList.SelectedItems.Count == 0)
        {
            readerClears++;
            Reader.Clear();
        }
    }

    /// <summary>Quante volte la selezione è cambiata, e quante il riquadro ha aperto un messaggio o è tornato vuoto (per le righe "tempi:").</summary>
    int selectionChanges, readerOpens, readerClears;

    /// <summary>
    /// Con il riquadro nascosto (e sempre, con il doppio clic) il messaggio si apre in una finestra; una bozza, come nel nuovo
    /// Outlook, nella finestra di scrittura.
    /// </summary>
    void OnMessageDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(MessageList, d) is ListBoxItem { DataContext: MessageItem item })
            OpenInWindow(item);
    }

    Window? OpenInWindow(MessageItem item)
    {
        if (item.IsDraft)
        {
            OpenDraft(item.Provider, item.Summary, Reader.Selected == item.Summary ? Reader.SelectedBody : null);
            return null;
        }
        var reader = new ReadingView();
        reader.ReplyRequested += OpenCompose;
        reader.MessageRead += OnReaderRead;
        reader.MailtoRequested += OnMailto;
        reader.EditRequested += OpenDraft;
        reader.MoreRequested += (provider, summary) => ShowMessageMenu(provider, summary, reader, reader);
        reader.PersonRequested += (provider, person) => ShowPerson(provider, person, Window.GetWindow(reader) ?? this);
        reader.InviteRequested += OnInvite;
        reader.EmlRequested += OpenEml;
        var content = new DockPanel();
        var window = new Window
        {
            Title = item.Subject,
            Content = content,
            Width = 900,
            Height = 780,
            // Con la finestra principale nascosta nell'area di notifica (il clic su una notifica) la finestra del messaggio sta
            // da sola, al centro dello schermo: un Owner nascosto la terrebbe fuori dalla barra delle applicazioni.
            Owner = IsVisible ? this : null,
            WindowStartupLocation = IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Icon = Icon,
        };
        var bar = MessageWindowBar(item, reader, window);
        DockPanel.SetDock(bar, Dock.Top);
        content.Children.Add(bar);
        content.Children.Add(reader);
        window.Show();
        _ = reader.ShowAsync(item.Provider, item.Summary, S.ReadingWholeConversation, JunkOf(item.Provider));
        return window;
    }

    /// <summary>
    /// La barra della finestra del messaggio, come nel nuovo Outlook: Elimina, Archivia, Sposta in, Già letto/Da leggere,
    /// Contrassegna, Stampa e Altre azioni (il menu del messaggio). Agiscono sul messaggio della finestra come sulla sua
    /// riga dell'elenco (Annulla compreso, nella finestra principale); dopo Elimina, Archivia e Sposta la finestra si chiude.
    /// </summary>
    StackPanel MessageWindowBar(MessageItem item, ReadingView reader, Window window)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 0) };
        Button Command(string glyph, string? text, string name, string? tip = null, string glyphStyle = "Glyph")
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource(glyphStyle), Margin = new Thickness(0, 0, text != null ? 6 : 0, 0) });
            if (text != null) content.Children.Add(new TextBlock { Text = text });
            var button = new Button { Style = (Style)FindResource("Command"), Content = content, ToolTip = tip ?? name };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            bar.Children.Add(button);
            return button;
        }
        // Il messaggio con l'account di adesso: dopo un ricarico degli account quello della finestra ha il provider chiuso (CurrentItem).
        MessageItem Now() => item = CurrentItem(item);
        var delete = Command("", "Elimina", "Elimina");
        delete.Click += async (_, _) => await DeleteFromWindowAsync(Now(), window);
        var archive = Command("", "Archivia", "Archivia");
        archive.IsEnabled = Special(item.Account, FolderKind.Archive) != null;
        archive.Click += async (_, _) => { if (await ArchiveAsync([Now()])) window.Close(); };
        var move = Command("", "Sposta in", "Sposta in");
        move.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = move, Placement = PlacementMode.Bottom };
            foreach (var f in Now().Account.Folders.Where(f => f.Folder.Id != item.Summary.FolderId && !f.IsHidden))
            {
                var target = f.Folder;
                var entry = new MenuItem { Header = new TextBlock { Text = f.Name, Margin = new Thickness(f.Depth * 16, 0, 0, 0) } };
                entry.Click += async (_, _) => { if (await MoveAsync([Now()], _ => target, "spostamento")) window.Close(); };
                menu.Items.Add(entry);
            }
            menu.IsOpen = true;
        };
        bar.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 6, 6, 6) });
        ((Border)bar.Children[^1]).SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
        var read = Command("", "Già letto/Da leggere", "Già letto/Da leggere", "Segna come già letto o da leggere");
        read.Click += async (_, _) => await SetReadAsync([Now()], !item.IsRead);
        var flag = Command("", null, "Contrassegna", glyphStyle: "FlagGlyph");
        flag.Click += async (_, _) => await ToggleFlagAsync([Now()]);
        var print = Command("", "Stampa", "Stampa", "Stampa (anteprima di Edge)");
        print.Click += (_, _) => reader.Print();
        var more = Command("", null, "Altre azioni");
        more.Click += (_, _) => OpenMessageMenu(Now(), reader, more, null);
        // Dal pop-up (anche quello di una notifica) alla finestra principale: la cartella del messaggio, la sua riga scelta e
        // la mail nel riquadro di lettura. Il pop-up si chiude: il messaggio è ora nel riquadro (richiesta dell'autore, 29 settembre 2026).
        bar.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 6, 6, 6) });
        ((Border)bar.Children[^1]).SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
        var openInMain = Command("\uE8A7", "Apri nel riquadro", "Apri nel riquadro di lettura", "Apri questo messaggio nel riquadro di lettura della finestra principale");
        openInMain.Click += (_, _) =>
        {
            ShowInMainWindow(Now());
            window.Close();
        };
        return bar;
    }

    /// <summary>
    /// La riga di un messaggio aperto in una finestra sua, con l'account di adesso. Aggiungendo o rimuovendo un account, o
    /// aggiungendo una casella condivisa, gli account si ricaricano (ListAccountsAsync) e la finestra restava con l'account di
    /// prima, il cui provider è chiuso: Elimina, Archivia, Sposta, Già letto, Contrassegna e il menu davano l'errore che dice
    /// di chiudere e riaprire. Quale provider vale adesso lo decide ComposeWindow.Current, come per la scrittura: se è un
    /// altro, la riga è quella dell'elenco di adesso con lo stesso messaggio, o una nuova sullo stesso messaggio. Se
    /// l'account è stato rimosso resta la riga di prima, e l'azione dà l'errore del provider chiuso (scelte del 12 ottobre,
    /// X-1).
    /// </summary>
    MessageItem CurrentItem(MessageItem item) => CurrentItem(item, accounts, rows.Rows, ComposeWindow.Current);

    internal static MessageItem CurrentItem(MessageItem item, IEnumerable<AccountItem> accounts, IEnumerable<MessageItem> rows, Func<IMailProvider, IMailProvider> current)
    {
        var now = current(item.Provider);
        if (now == item.Provider || accounts.FirstOrDefault(a => a.Provider == now) is not { } account) return item;
        return rows.FirstOrDefault(r => r.Account == account && r.Summary.Id == item.Summary.Id && r.Summary.FolderId == item.Summary.FolderId && r.IsChild == item.IsChild && r.IsHead == item.IsHead)
               ?? new MessageItem(item.Summary, account);
    }

    /// <summary>
    /// Il messaggio di una finestra sua nella finestra principale (richiesta dell'autore, 29 settembre 2026): in primo piano (anche
    /// nell'area di notifica o ridotta a icona), la cartella del messaggio, la sua riga scelta e la mail nel riquadro.
    /// </summary>
    internal void ShowInMainWindow(MessageItem item)
    {
        ShowFromTray();
        Log.Write($"messaggio {item.Summary.Id} nel riquadro di lettura della finestra principale (dalla sua finestra)");
        _ = OpenFromNotificationAsync(item.Account.Provider.Account.Id, item.Summary.Id, item.Summary, item.Summary.FolderId);
    }

    /// <summary>Elimina dalla finestra del messaggio: se il messaggio se n'è andato, la finestra si chiude.</summary>
    /// <param name="simulate">Solo nel registro (--window-delete, per le prove in Debug).</param>
    async Task DeleteFromWindowAsync(MessageItem item, Window window, bool simulate = false)
    {
        if (await DeleteAsync([item], simulate)) window.Close();
    }

    /// <summary>
    /// Altre azioni ("...") nell'intestazione di un messaggio aperto, come nel nuovo Outlook: il menu del messaggio
    /// (BuildMessageMenu) per quel messaggio, anche dentro una conversazione.
    /// </summary>
    /// <param name="reader">Il riquadro della finestra del messaggio; null per quello principale.</param>
    void ShowMessageMenu(IMailProvider provider, MessageSummary summary, ReadingView source, ReadingView? reader)
    {
        // La sua riga, se è proprio quel messaggio: una conversazione chiusa porterebbe con sé tutti i suoi messaggi.
        var item = rows.Rows.FirstOrDefault(r => r.Provider == provider && r.Summary.Id == summary.Id && r.Summary.FolderId == summary.FolderId && !r.IsHead)
                   ?? (accounts.FirstOrDefault(a => a.Provider == provider) is { } account ? new MessageItem(summary, account) : null);
        if (item == null) return;
        // Vicino al puntatore: il clic arriva da WebView2, che WPF non vede. Dalla tastiera, in alto a destra del riquadro.
        var at = GetCursorPos(out var p) ? source.PointFromScreen(new Point(p.X, p.Y)) : new Point(-1, -1);
        if (at.X < 0 || at.Y < 0 || at.X > source.ActualWidth || at.Y > source.ActualHeight) at = new Point(Math.Max(0, source.ActualWidth - 60), 60);
        OpenMessageMenu(item, reader, source, at);
    }

    /// <param name="at">Dove, rispetto a target; null sotto target (il pulsante "..." della barra).</param>
    void OpenMessageMenu(MessageItem item, ReadingView? reader, UIElement target, Point? at)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = at != null ? PlacementMode.RelativePoint : PlacementMode.Bottom };
        if (at is { } point)
        {
            menu.HorizontalOffset = point.X;
            menu.VerticalOffset = point.Y;
        }
        if (!BuildMessageMenu(menu, [item], reader)) return;
        menu.IsOpen = true;
        Log.Write($"altre azioni: menu del messaggio {item.Summary.Id} con {menu.Items.OfType<MenuItem>().Count()} voci");
    }

    /// <summary>La Posta indesiderata dell'account: il riquadro non ne mette i destinatari nella rubrica dei suggerimenti.</summary>
    string? JunkOf(IMailProvider provider) =>
        accounts.FirstOrDefault(a => a.Provider == provider) is { } account ? Special(account, FolderKind.Junk)?.Id : null;

    /// <summary>
    /// Il riquadro ha segnato letto un messaggio: si aggiornano l'elenco e i non letti della sua cartella, che con "quando
    /// la selezione cambia" può non essere più quella aperta.
    /// </summary>
    void OnReaderRead(IMailProvider provider, MessageSummary summary)
    {
        foreach (var row in rows.Rows.Where(r => r.Provider == provider && r.Summary.Id == summary.Id)) row.IsRead = true;
        if (accounts.FirstOrDefault(a => a.Provider == provider)?.Folders.FirstOrDefault(f => f.Folder.Id == summary.FolderId) is { } where)
            where.SetUnread(where.Unread - 1, "letto nel riquadro");
        UpdateCommands();
    }

    // ---------------------------------------------------------------- azioni

    void UpdateCommands()
    {
        var selected = Selected;
        bool any = selected.Count > 0, one = selected.Count == 1;
        DeleteButton.IsEnabled = ArchiveButton.IsEnabled = MoveButton.IsEnabled = ReadButton.IsEnabled = FlagButton.IsEnabled = any;
        PinButton.IsEnabled = one && !selected[0].IsChild;
        ReplyButton.IsEnabled = ReplyAllButton.IsEnabled = ForwardButton.IsEnabled = one;
    }

    MailFolder? Special(AccountItem account, FolderKind kind) =>
        account.Folders.FirstOrDefault(f => f.Folder.Kind == kind)?.Folder;

    /// <summary>
    /// Il Cestino dell'account. Se manca (IMAP: su molti server nasce solo quando un programma lo crea, o ha un nome che
    /// Missiva non conosce) il provider lo crea, come Thunderbird, e le cartelle si ricaricano. null se non c'è e non si è
    /// potuto creare, dopo l'avviso: prima Elimina non faceva nulla senza dirlo.
    /// </summary>
    async Task<MailFolder?> TrashOfAsync(AccountItem account)
    {
        if (Special(account, FolderKind.Deleted) is { } trash) return trash;
        if (account.Folders.Count == 0) return null; // cartelle non ancora arrivate
        try
        {
            if (await account.Provider.EnsureTrashAsync() is { } created)
            {
                await LoadFoldersAsync(account);
                return Special(account, FolderKind.Deleted) ?? created;
            }
            Dialog.Show(this, "Nessun Cestino", $"L'account {account.Header} non ha un Cestino: i messaggi non si possono spostare lì.");
        }
        catch (Exception ex)
        {
            Log.Error("creazione del Cestino", ex);
            Dialog.Show(this, "Cestino non creato", $"L'account {account.Header} non ha un Cestino e Missiva non è riuscita a crearlo: {ex.Message}");
        }
        return null;
    }

    /// <returns>Falso se non è riuscita (dopo l'avviso).</returns>
    async Task<bool> RunAsync(string what, Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(what, ex);
            Dialog.Show(this, "Operazione non riuscita", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Toglie le righe e seleziona quella successiva, come Outlook. Restituisce dov'erano e con quali messaggi (una
    /// conversazione chiusa li porta tutti), per Annulla. La riga successiva si sceglie prima di togliere le altre: un solo
    /// cambio di selezione, e il riquadro passa dal messaggio a quello dopo. Prima le righe scelte uscivano una alla volta:
    /// con una il riquadro passava dal segnaposto (visto per 0,2 s), con più d'una, quando ne restava scelta una sola (che
    /// stava per uscire), il suo messaggio si apriva e si scaricava per niente (audit, v1 V-04 e v2 V-13).
    /// </summary>
    List<(int Index, List<MessageSummary> Messages)> RemoveRows(List<MessageItem> items)
    {
        var clock = Stopwatch.StartNew();
        var (changes, opens, clears) = (selectionChanges, readerOpens, readerClears);
        var removed = items.Select(i => (Index: rows.Rows.IndexOf(i), Messages: MessageRows.MessagesOf(i))).Where(r => r.Index >= 0).ToList();
        if (removed.Count > 0)
        {
            if (rows.After(items) is { } next) MessageList.SelectedItem = next;
            else MessageList.UnselectAll(); // non resta nessuna riga: il riquadro torna vuoto, una volta
        }
        foreach (var i in items) rows.Remove(i);
        // Anche dalla prima pagina tenuta per la copia locale: la rilettura dopo la trova uguale e non rifà l'elenco.
        var gone = removed.SelectMany(r => r.Messages).Select(m => (m.FolderId, m.Id)).ToHashSet();
        firstPage.RemoveAll(m => gone.Contains((m.FolderId, m.Id)));
        Log.Time($"{items.Count} righe tolte dall'elenco ({selectionChanges - changes} cambi di selezione, il riquadro apre {readerOpens - opens} messaggi e torna vuoto {readerClears - clears} volte)", clock);
        return removed;
    }

    /// <summary>
    /// Nella versione di prova (MISSIVA_NO_READ, la casella è quella vera) spostare, eliminare e copiare vanno solo nel
    /// registro, e l'elenco cambia come se fossero fatti: così si provano, Annulla compreso. Tutte le altre scritture le
    /// ferma TestingProvider, che avvolge i provider veri (NewProvider).
    /// </summary>
    internal static bool Testing =>
#if DEBUG || PROVA
        Environment.GetEnvironmentVariable("MISSIVA_NO_READ") == "1";
#else
        false;
#endif

    /// <param name="simulate">Solo nel registro, come nella versione di prova (per le prove in Debug, --delete e --drop).</param>
    /// <returns>Falso se non si è spostato nulla (nessuna cartella di arrivo).</returns>
    async Task<bool> MoveAsync(List<MessageItem> items, Func<AccountItem, MailFolder?> destination, string what, bool simulate = false)
    {
        if (items.Count == 0) return false;
        // Per account, quello dei messaggi: dalla finestra di un messaggio può non essere quello aperto, e nella vista di
        // tutti gli account la scelta può mescolarne più d'uno (Archivia manda ognuno nel suo Archivio).
        var groups = items.GroupBy(i => i.Account).Select(g => (Account: g.Key, Items: g.ToList(), Target: destination(g.Key))).ToList();
        if (groups.Any(g => g.Target == null)) return false;
        // L'elenco da cui si tolgono le righe: se durante lo spostamento si cambia cartella, Annulla non le rimette nell'altra.
        var version = listVersion;
        var removed = RemoveRows(items);
        bool dry = simulate || Testing, ok = true;
        var parts = new List<MovePart>();
        var failed = new Dictionary<MessageSummary, AccountItem>();
        foreach (var (account, group, target) in groups)
        {
            var messages = Targets(group);
            var leaving = messages.Where(m => m.FolderId != target!.Id).ToList();
            List<MovedMessage>? moved = null;
            bool queued = false;
            // Nella versione di prova solo il registro; senza rete (anche nella prova, --senza-rete) in coda, e al ritorno
            // della rete la coda passa dal provider, che nella prova è TestingProvider.
            if (simulate || dry && !Network.KnownOffline(account.Provider.Account))
            {
                Log.Write($"prova: sposterei {messages.Count} messaggi di {account.Header} in {target!.Name}");
                moved = leaving.Select(m => new MovedMessage(m, m.Id)).ToList();
            }
            else switch (await RunOrQueueAsync(account, what, async () => moved = await account.Provider.MoveAsync(messages, target!),
                                               () => Enqueue(account, leaving, QueuedKind.Move, target!.Id)))
            {
                case null:
                    // In coda: le righe sono già uscite, Annulla le toglie dalla coda.
                    moved = leaving.Select(m => new MovedMessage(m, m.Id)).ToList();
                    queued = true;
                    break;
                case false:
                    ok = false;
                    foreach (var m in messages) failed[m] = account; // rifiutato dal server: le righe tornano (PutBackFailed)
                    break;
                default:
                    BodyCache.Forget(account.Provider.Account, messages); // la copia su disco non resta alla cartella di prima
                    FollowFlags(account, moved, target!.Id);
                    break;
            }
            if (moved is { Count: > 0 }) parts.Add(new MovePart(account, target!, moved, queued));
        }
        if (parts.Count > 0) OfferUndo(new LastMove(parts, removed, version, dry));
        PutBackFailed(removed, failed, version);
        return ok;
    }

    /// <summary>
    /// Sposta, Archivia o Elimina che il server ha rifiutato (dopo l'avviso): le righe tolte prima della richiesta tornano al
    /// loro posto, se l'elenco è ancora quello da cui sono uscite; i messaggi sono rimasti nella loro cartella. Prima
    /// sparivano fino alla rilettura, e Annulla non le riportava: per loro non c'è nulla da annullare (audit dell'11
    /// ottobre, C-50).
    /// </summary>
    void PutBackFailed(List<(int Index, List<MessageSummary> Messages)> removed, Dictionary<MessageSummary, AccountItem> failed, int version)
    {
        if (failed.Count == 0) return;
        if (version != listVersion)
        {
            Log.Write($"{failed.Count} messaggi non spostati: l'elenco intanto è un altro, le righe non tornano qui");
            return;
        }
        var restored = rows.RestoreFailed(removed, failed);
        Log.Write($"{failed.Count} messaggi non spostati (rifiutati dal server): {restored.Count} righe tornate nell'elenco");
    }

    /// <summary>Le date e i promemoria dei contrassegni seguono i messaggi spostati (Flags.Follow): id e cartella nuovi.</summary>
    static void FollowFlags(AccountItem account, IEnumerable<MovedMessage>? moved, string folderId)
    {
        bool changed = false;
        foreach (var x in moved ?? [])
            changed |= Flags.Follow(account.Provider.Account, x.Original.Id, x.Original.FolderId, x.NewId, folderId);
        if (changed) Store.Save();
    }

    async void OnDelete(object sender, RoutedEventArgs e) => await DeleteAsync(Selected);

    /// <summary>
    /// Si decide messaggio per messaggio, non dalla cartella aperta: nella ricerca in tutte le cartelle i risultati
    /// vengono da cartelle diverse. Quelli già nel Cestino si eliminano per sempre, dopo la conferma; gli altri ci vanno.
    /// </summary>
    /// <returns>Falso se non si è eliminato nulla (niente Cestino, o l'eliminazione definitiva annullata).</returns>
    async Task<bool> DeleteAsync(List<MessageItem> items, bool simulate = false)
    {
        if (items.Count == 0) return false;
        // Per account, ognuno con il suo Cestino (nella vista di tutti gli account la scelta può mescolarli).
        var groups = new List<(AccountItem Account, MailFolder Trash, List<MessageSummary> ToTrash, List<MessageSummary> Permanent)>();
        foreach (var g in items.GroupBy(i => i.Account))
        {
            if (await TrashOfAsync(g.Key) is not { } trash) return false;
            var messages = Targets(g);
            groups.Add((g.Key, trash, messages.Where(m => m.FolderId != trash.Id).ToList(), messages.Where(m => m.FolderId == trash.Id).ToList()));
        }
        int permanent = groups.Sum(g => g.Permanent.Count), toTrash = groups.Sum(g => g.ToTrash.Count);
        if (permanent > 0)
        {
            var text = toTrash == 0
                ? permanent == 1 ? "Il messaggio non si potrà recuperare." : $"I {permanent} messaggi non si potranno recuperare."
                : (permanent == 1 ? "Uno dei messaggi è già nel Cestino: non si potrà recuperare." : $"{permanent} messaggi sono già nel Cestino: non si potranno recuperare.") +
                  " Gli altri vanno nel Cestino.";
            if (Dialog.Ask(this, "Eliminare definitivamente?", text, "Elimina") != Choice.Primary) return false;
        }
        // L'eliminazione definitiva non va in coda: non si annulla, e senza rete non si vede se il messaggio c'è ancora.
        if (!simulate && groups.Where(g => g.Permanent.Count > 0 && Network.KnownOffline(g.Account.Provider.Account)).Select(g => g.Account).FirstOrDefault() is { } away)
        {
            Dialog.Show(this, "Non in linea", $"{away.Header} non è in linea: i messaggi già nel Cestino si eliminano per sempre solo con la rete. Riprova quando torna.");
            return false;
        }
        var version = listVersion; // come in MoveAsync: l'elenco da cui si tolgono le righe
        var removed = RemoveRows(items);
        bool dry = simulate || Testing, ok = true;
        var parts = new List<MovePart>();
        var failed = new Dictionary<MessageSummary, AccountItem>();
        foreach (var (account, trash, inTrash, forever) in groups)
        {
            List<MovedMessage>? moved = null;
            bool queued = false;
            if (simulate || dry && !Network.KnownOffline(account.Provider.Account))
            {
                if (inTrash.Count > 0) Log.Write($"prova: sposterei {inTrash.Count} messaggi di {account.Header} in {trash.Name}");
                if (forever.Count > 0) Log.Write($"prova: eliminerei per sempre {forever.Count} messaggi di {account.Header}");
                moved = inTrash.Select(m => new MovedMessage(m, m.Id)).ToList();
            }
            else
            {
                // Nel Cestino anche senza rete (in coda, come Sposta); per sempre solo con la rete.
                var trashed = inTrash.Count == 0 ? true
                    : await RunOrQueueAsync(account, "eliminazione", async () => moved = await account.Provider.MoveAsync(inTrash, trash),
                                            () => Enqueue(account, inTrash, QueuedKind.Move, trash.Id));
                if (trashed == null)
                {
                    moved = inTrash.Select(m => new MovedMessage(m, m.Id)).ToList();
                    queued = true;
                }
                else if (trashed == false)
                {
                    ok = false;
                    // Rifiutato: nel Cestino non è andato nulla, e quelli da eliminare per sempre non partono (sotto): tornano tutti.
                    foreach (var m in inTrash.Concat(forever)) failed[m] = account;
                }
                else
                {
                    BodyCache.Forget(account.Provider.Account, inTrash); // eliminati: nemmeno la copia su disco resta
                    FollowFlags(account, moved, trash.Id); // nel Cestino il contrassegno resta, come in Outlook
                }
                if (forever.Count > 0 && trashed != false && await RunAsync("eliminazione", () => account.Provider.DeletePermanentlyAsync(forever)))
                {
                    BodyCache.Forget(account.Provider.Account, forever);
                    foreach (var m in forever) Flags.Forget(account.Provider.Account, m); // eliminati per sempre: nemmeno le date
                    Store.Save();
                }
                else if (forever.Count > 0)
                {
                    ok = false;
                    foreach (var m in forever) failed[m] = account;
                }
            }
            // Quelli eliminati per sempre non tornano: Annulla vale per quelli andati nel Cestino.
            if (moved is { Count: > 0 }) parts.Add(new MovePart(account, trash, moved, queued));
        }
        if (parts.Count > 0) OfferUndo(new LastMove(parts, removed, version, dry));
        PutBackFailed(removed, failed, version);
        return ok;
    }

    // ---------------------------------------------------------------- Annulla

    /// <summary>I messaggi di un account spostati in una sua cartella; Queued, senza rete: lo spostamento è in coda (ActionQueue).</summary>
    sealed record MovePart(AccountItem Account, MailFolder Destination, List<MovedMessage> Moved, bool Queued = false);

    /// <summary>
    /// L'ultimo spostamento: cosa e dove, per account (nella vista di tutti gli account Elimina e Archivia ne toccano più
    /// d'uno), e le righe tolte (con la loro posizione) per rimetterle. Simulated: fatto solo nel registro (versione di
    /// prova), e così anche il suo Annulla, qualunque cosa succeda dopo.
    /// </summary>
    sealed record LastMove(List<MovePart> Parts, List<(int Index, List<MessageSummary> Messages)> Rows, int ListVersion, bool Simulated)
    {
        public int Count => Parts.Sum(p => p.Moved.Count);
        /// <summary>Per il testo della barra: Cestino e Archivio sono dello stesso tipo in ogni account, Sposta in ha un account solo.</summary>
        public MailFolder Destination => Parts[0].Destination;
    }

    LastMove? lastMove;
    DispatcherTimer? undoTimer;

    /// <summary>
    /// Come nel nuovo Outlook: dopo Elimina, Archivia o Sposta, in fondo all'elenco "Elemento eliminato" (archiviato,
    /// spostato in...) con Annulla, per 8 secondi; Ctrl+Z fa lo stesso finché la barra si vede.
    /// </summary>
    void OfferUndo(LastMove move)
    {
        lastMove = move;
        int n = move.Count;
        var done = move.Destination.Kind switch
        {
            FolderKind.Deleted => n == 1 ? "eliminato" : "eliminati",
            FolderKind.Archive => n == 1 ? "archiviato" : "archiviati",
            _ => (n == 1 ? "spostato in " : "spostati in ") + move.Destination.Name,
        };
        UndoText.Text = n == 1 ? $"Elemento {done}" : $"{n} elementi {done}";
        UndoBar.Visibility = Visibility.Visible;
        if (undoTimer == null)
        {
            undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            undoTimer.Tick += (_, _) => HideUndo();
        }
        undoTimer.Stop();
        undoTimer.Start();
    }

    void HideUndo()
    {
        undoTimer?.Stop();
        UndoBar.Visibility = Visibility.Collapsed;
        lastMove = null;
    }

    async void OnUndo(object sender, RoutedEventArgs e) => await UndoAsync();

    /// <summary>Le scritture che aspettano di partire (Annulla invio): la barra "Invio in corso…" annulla l'ultima.</summary>
    readonly List<ComposeWindow> pendingSends = new();

    public void OfferUndoSend(ComposeWindow window)
    {
        pendingSends.Remove(window);
        pendingSends.Add(window);
        ShowSendBar();
    }

    public void EndUndoSend(ComposeWindow window)
    {
        pendingSends.Remove(window);
        ShowSendBar();
    }

    /// <summary>L'avanzamento di un invio è cambiato (ComposeWindow.SendProgress): la barra lo dice.</summary>
    public void RefreshSendBar() => ShowSendBar();

    void ShowSendBar()
    {
        SendText.Text = SendBarText(pendingSends.Select(w => (w.PendingSubject, w.SendProgress)).ToList());
        SendText.ToolTip = pendingSends.Count == 1 && pendingSends[0].PendingSubject is { Length: > 0 } subject ? subject : null;
        // Annulla solo quando serve: nell'attesa prima dell'invio, e mentre gli allegati grandi si caricano.
        bool uploading = pendingSends.LastOrDefault() is { SendPending: false, CanUndoSend: true };
        UndoSendButton.Visibility = pendingSends.LastOrDefault()?.CanUndoSend == true ? Visibility.Visible : Visibility.Collapsed;
        UndoSendButton.ToolTip = uploading ? "Ferma il caricamento degli allegati e riapri il messaggio" : "Non inviare il messaggio e riaprilo";
        SendBar.Visibility = pendingSends.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Il testo della barra degli invii: "Invio in corso: oggetto", e durante il caricamento degli allegati grandi (account
    /// Microsoft) i MB caricati sul totale e il nome del file, con i numeri per primi: la barra è stretta, e in fondo
    /// finirebbero tagliati. L'avanzamento è quello di GraphProvider ("Caricamento di nome: 3,5 di 12 MB…").
    /// </summary>
    internal static string SendBarText(IReadOnlyList<(string Subject, string? Progress)> sends)
    {
        if (sends.Count == 0) return "";
        string many = $"Invio di {sends.Count} messaggi in corso";
        if (sends.LastOrDefault(s => s.Progress != null).Progress is { } progress)
            return $"{(sends.Count == 1 ? "Invio in corso" : many)}: {UploadShort(progress)}";
        return sends.Count > 1 ? many + "…" : sends[0].Subject is { Length: > 0 } subject ? $"Invio in corso: {subject}" : "Invio in corso…";
    }

    /// <summary>
    /// L'avanzamento di un caricamento con i numeri per primi ("3,5 di 12 MB caricati (nome)"), per le righe strette: la barra
    /// degli invii e la riga di stato della scrittura, dove un nome lungo spingeva i MB fuori dalla vista. Un testo fatto in
    /// un altro modo resta com'è.
    /// </summary>
    internal static string UploadShort(string progress) => UploadProgress().Match(progress) is { Success: true } m
        ? $"{m.Groups["done"].Value} di {m.Groups["total"].Value} MB caricati ({m.Groups["name"].Value})"
        : progress;

    [System.Text.RegularExpressions.GeneratedRegex(@"^Caricamento di (?<name>.+): (?<done>[\d,.]+) di (?<total>[\d,.]+) MB…$")]
    private static partial System.Text.RegularExpressions.Regex UploadProgress();

    void OnUndoSend(object sender, RoutedEventArgs e)
    {
        if (pendingSends.LastOrDefault() is { } last) last.UndoSend();
    }

    /// <summary>
    /// I messaggi tornano nelle loro cartelle e le righe al loro posto, selezionate. Se intanto l'elenco è cambiato (altra
    /// cartella) i messaggi tornano lo stesso e l'elenco si rilegge.
    /// </summary>
    async Task UndoAsync()
    {
        if (lastMove is not { } last) return;
        HideUndo();
        // Per account: i messaggi tornati, con l'account a cui rimettere le loro righe.
        var back = new List<(MovedMessage Moved, AccountItem Account)>();
        bool stale = false;
        foreach (var part in last.Parts)
        {
            if (part.Queued)
            {
                // Fatto senza rete: lo spostamento esce dalla coda prima di partire. Se intanto la rete è tornata ed è già
                // partito, i messaggi restano dove sono andati e l'elenco si rilegge.
                var originals = part.Moved.Select(x => x.Original).ToList();
                var taken = ActionQueue.Take(part.Account.Provider.Account, originals, part.Destination.Id).ToHashSet();
                UpdateQueued(part.Account);
                // Quelli usciti dalla coda non sono partiti: tornano nell'elenco, anche se gli altri sono già partiti (prima, con
                // una parte sola uscita, restavano fuori dall'elenco e l'avviso li dava tutti per spostati).
                back.AddRange(part.Moved.Where(x => taken.Contains(x.Original)).Select(x => (x with { NewId = x.Original.Id }, part.Account)));
                if (taken.Count < originals.Count)
                {
                    Log.Write($"annullamento: {originals.Count - taken.Count} spostamenti di {part.Account.Header} erano già partiti al ritorno della rete");
                    Dialog.Show(this, "Già fatto", UndoStartedText(originals.Count - taken.Count, originals.Count, part.Destination.Name));
                    stale = true;
                }
                continue;
            }
            if (last.Simulated)
            {
                Log.Write($"prova: annullerei, {part.Moved.Count} messaggi di {part.Account.Header} tornerebbero da {part.Destination.Name}");
                back.AddRange(part.Moved.Select(x => (x with { NewId = x.Original.Id }, part.Account)));
            }
            else await RunAsync("annullamento", async () =>
            {
                var returned = await part.Account.Provider.UndoMoveAsync(part.Moved, part.Destination);
                back.AddRange(returned.Select(x => (x, part.Account)));
                // Le date dei contrassegni tornano con i messaggi: la voce era al messaggio nella cartella di arrivo.
                bool changed = false;
                foreach (var x in returned)
                    if (part.Moved.FirstOrDefault(p => p.Original == x.Original) is { } there)
                        changed |= Flags.Follow(part.Account.Provider.Account, there.NewId, part.Destination.Id, x.NewId, x.Original.FolderId);
                if (changed) Store.Save();
            });
        }
        if (back.Count == 0)
        {
            if (stale) await RefreshListAsync();
            return;
        }
        if (last.ListVersion != listVersion)
        {
            await RefreshListAsync();
            return;
        }
        var now = back.ToDictionary(x => x.Moved.Original, x => x.Moved.NewId == x.Moved.Original.Id ? x.Moved.Original : x.Moved.Original.WithId(x.Moved.NewId));
        var accountOf = back.ToDictionary(x => now[x.Moved.Original], x => x.Account);
        var restored = rows.Restore(last.Rows, now, m => accountOf[m]);
        if (restored.Count == 0)
        {
            if (stale) await RefreshListAsync();
            return;
        }
        // Una riga tornata: scelta, e il riquadro la apre (un solo cambio di selezione). Più righe: tornano scelte tutte e
        // il riquadro resta vuoto. Prima si sceglievano una alla volta: alla prima il riquadro apriva il suo messaggio e,
        // con "segna come letto subito", lo segnava letto senza che nessuno l'avesse aperto (audit, v2 V-13).
        if (restored.Count == 1) MessageList.SelectedItem = restored[0];
        else
        {
            regrouping = true;
            try
            {
                MessageList.SelectedItems.Clear();
                foreach (var item in restored) MessageList.SelectedItems.Add(item);
            }
            finally
            {
                regrouping = false;
            }
            UpdateCommands();
            ShowKeyboardActions(null);
            if (S.ReadingPane != ReadingPanePosition.Off)
            {
                readerClears++;
                Reader.Clear();
            }
            Log.Write($"annullamento: {restored.Count} righe tornate e scelte, riquadro vuoto");
        }
        MessageList.ScrollIntoView(restored[0]);
        if (stale) await RefreshListAsync(); // una parte era già partita: l'elenco si rilegge, con le righe tornate al loro posto
    }

    /// <summary>L'avviso di Annulla quando la rete è tornata e lo spostamento fatto senza rete è già partito, tutto o in parte.</summary>
    internal static string UndoStartedText(int started, int all, string destination) => started >= all
        ? "La rete è tornata e lo spostamento è già stato fatto: i messaggi sono in " + destination + "."
        : (started == 1 ? "La rete è tornata e uno spostamento era già partito: quel messaggio è in " : $"La rete è tornata e {started} spostamenti erano già partiti: quei messaggi sono in ")
          + destination + ". Gli altri sono tornati al loro posto.";

    async void OnArchive(object sender, RoutedEventArgs e) => await ArchiveAsync(Selected);

    async Task<bool> ArchiveAsync(List<MessageItem> items)
    {
        if (items.Count == 0) return false;
        if (items.Select(i => i.Account).Distinct().FirstOrDefault(a => Special(a, FolderKind.Archive) == null) is { } missing)
        {
            Dialog.Show(this, "Nessun archivio", $"L'account {missing.Header} non ha una cartella Archivio.");
            return false;
        }
        return await MoveAsync(items, a => Special(a, FolderKind.Archive), "archiviazione");
    }

    void OnMoveMenu(object sender, RoutedEventArgs e)
    {
        if (!OneAccount(Selected))
        {
            Dialog.Show(this, "Messaggi di più account", "Sposta in lavora sui messaggi di un account alla volta: ogni account ha le sue cartelle.");
            return;
        }
        if (BuildMoveMenu() is not { } menu) return;
        menu.PlacementTarget = Anchor(MoveButton);
        menu.IsOpen = true;
    }

    /// <summary>La cartella dei messaggi selezionati, se stanno tutti nella stessa (nei risultati della ricerca non è detto).</summary>
    string? SelectedFolderId()
    {
        var ids = Targets(Selected).Select(m => m.FolderId).Distinct().ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>I messaggi sono tutti dello stesso account (nella vista di tutti gli account la scelta può mescolarli).</summary>
    static bool OneAccount(List<MessageItem> items) => items.Count > 0 && items.All(i => i.Account == items[0].Account);

    ContextMenu? BuildMoveMenu()
    {
        var selected = Selected;
        if (!OneAccount(selected)) return null;
        var menu = new ContextMenu();
        var here = SelectedFolderId();
        foreach (var f in selected[0].Account.Folders.Where(f => f.Folder.Id != here))
        {
            var target = f.Folder;
            var entry = new MenuItem { Header = new TextBlock { Text = f.Name, Margin = new Thickness(f.Depth * 16, 0, 0, 0) } };
            entry.Click += async (_, _) => await MoveAsync(Selected, _ => target, "spostamento");
            menu.Items.Add(entry);
        }
        return menu;
    }

    async void OnToggleRead(object sender, RoutedEventArgs e)
    {
        var items = Selected;
        if (items.Count > 0) await SetReadAsync(items, !items.All(m => m.IsRead));
    }

    async Task SetReadAsync(List<MessageItem> items, bool read)
    {
        var targets = Targets(items);
        // Deciso a mano: "dopo N secondi" e "quando la selezione cambia" non lo rifanno letto più tardi.
        ReadingView.ForgetPendingRead(targets);
        var messages = targets.Where(m => m.IsRead != read).ToList();
#if DEBUG || PROVA
        if (Environment.GetEnvironmentVariable("MISSIVA_NO_READ") == "1" && !items.Any(i => Network.KnownOffline(i.Account.Provider.Account)))
        {
            // Prove sulla casella vera: Ctrl+Q, Ctrl+U e la busta si provano senza cambiarla, come le letture automatiche.
            // Senza rete (--senza-rete) invece in coda come fuori dalla prova: al ritorno la coda passa da TestingProvider.
            Log.Write($"prova: segnerei {(read ? "letti" : "da leggere")} {messages.Count} messaggi su {targets.Count}");
            return;
        }
#endif
        // Per account: nella vista di tutti gli account la scelta può mescolarli, e gli identificativi valgono per account.
        var clock = Stopwatch.StartNew();
        foreach (var group in items.GroupBy(i => i.Account))
        {
            var mine = Targets(group).Where(m => m.IsRead != read).ToList();
            // Le righe si cercano una volta sola, e i non letti di ogni cartella cambiano una volta sola (prima una scansione
            // dell'elenco e un cambio del conteggio per messaggio: 50 messaggi 91-127 ms di interfaccia ferma).
            foreach (var m in mine) m.IsRead = read;
            var ids = mine.Select(m => m.Id).ToHashSet();
            foreach (var row in rows.Rows.Where(r => r.Account == group.Key && ids.Contains(r.Summary.Id))) row.IsRead = read;
            group.Key.CountRead(mine, read);
            foreach (var item in group) item.IsRead = item.Summary.IsRead; // aggiorna l'aspetto anche delle teste
            Log.Time($"{mine.Count} messaggi segnati {(read ? "letti" : "da leggere")} nell'elenco", clock);
            if (mine.Count > 0)
                await RunOrQueueAsync(group.Key, "stato di lettura", () => group.Key.Provider.SetReadAsync(mine, read),
                                      () => Enqueue(group.Key, mine, read ? QueuedKind.Read : QueuedKind.Unread));
        }
        UpdateCommands();
    }

    async void OnToggleFlag(object sender, RoutedEventArgs e) => await ToggleFlagAsync(Selected);

    async Task ToggleFlagAsync(List<MessageItem> items)
    {
        if (items.Count == 0) return;
        bool flag = !items.All(m => m.IsFlagged);
        if (!flag)
        {
            await ClearFlagsAsync(items); // anche date e promemoria
            return;
        }
        foreach (var m in items) m.IsFlagged = flag;
        foreach (var group in items.GroupBy(i => i.Account))
        {
            var mine = group.Select(i => i.Summary).ToList();
            await RunOrQueueAsync(group.Key, "contrassegno", () => group.Key.Provider.SetFlagAsync(mine, flag), () => Enqueue(group.Key, mine, QueuedKind.Flag));
        }
    }

    async Task FlagAsync(MessageItem item, bool flag)
    {
        if (!flag)
        {
            await ClearFlagsAsync([item]);
            return;
        }
        item.IsFlagged = flag;
        await RunOrQueueAsync(item.Account, "contrassegno", () => item.Provider.SetFlagAsync(item.Summary, flag), () => Enqueue(item.Account, [item.Summary], QueuedKind.Flag));
    }

    void OnTogglePin(object sender, RoutedEventArgs e)
    {
        if (Selected is [{ IsChild: false } item]) TogglePin(item);
    }

    void TogglePin(MessageItem item)
    {
        S.SetPinned(item.Provider.Account, item.Summary.Id, !item.IsPinned);
        Store.SaveSoon();
        rows.Repin(item);
        RegroupKeepingSelection();
    }

    // Azioni rapide al passaggio del mouse (e sulla riga scelta con le frecce)
    internal async void OnRowRead(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MessageItem item }) await SetReadAsync(new List<MessageItem> { item }, !item.IsRead);
    }

    /// <summary>La selezione sta per cambiare con la tastiera (frecce, pagine, inizio, fine): la riga nuova mostrerà le azioni rapide.</summary>
    bool selectingByKeyboard;
    MessageItem? keyboardRow;

    void OnMessageListKey(object sender, KeyEventArgs e) =>
        selectingByKeyboard = e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;

    /// <summary>Con il mouse le azioni rapide seguono il passaggio del mouse, non la tastiera.</summary>
    void OnMessageListMouse(object sender, MouseButtonEventArgs e)
    {
        selectingByKeyboard = false;
        ShowKeyboardActions(null);
    }

    void ShowKeyboardActions(MessageItem? item)
    {
        if (keyboardRow != null) keyboardRow.KeyboardActions = false;
        keyboardRow = item;
        if (item != null) item.KeyboardActions = true;
    }

    internal async void OnRowFlag(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MessageItem item }) await FlagAsync(item, !item.IsFlagged);
    }

    internal void OnRowPin(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MessageItem { IsChild: false } item }) TogglePin(item);
    }

    internal async void OnRowDelete(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MessageItem item }) await DeleteAsync(new List<MessageItem> { item });
    }

    void OnMessageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            _ = DeleteAsync(Selected);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Selected is [var item])
        {
            OpenInWindow(item);
            e.Handled = true;
        }
    }

    void OnMessageMenu(object sender, ContextMenuEventArgs e)
    {
        if (!BuildMessageMenu(MessageList.ContextMenu!, Selected)) e.Handled = true;
    }

    /// <summary>
    /// Il menu del tasto destro sui messaggi: le voci scelte in "Personalizza il menu…", nell'ordine scelto (di base
    /// quelle del nuovo Outlook). Le voci che non si applicano alla selezione (Rispondi con più messaggi) non compaiono.
    /// </summary>
    /// <remarks>
    /// Lo stesso menu, per un messaggio solo, è "Altre azioni" ("...") nell'intestazione dei messaggi aperti, nel riquadro e
    /// nella finestra del messaggio (ShowMessageMenu): le voci agiscono sui messaggi dati, non sulla selezione dell'elenco.
    /// Posponi, categorie e Fissa in alto solo per le righe dell'elenco aperto.
    /// </remarks>
    /// <param name="reader">Il riquadro della finestra del messaggio (per Stampa e PDF); null per quello principale.</param>
    bool BuildMessageMenu(ContextMenu menu, List<MessageItem> selected, ReadingView? reader = null)
    {
        if (selected.Count == 0) return false;
        menu.Items.Clear();
        var single = selected.Count == 1 ? selected[0] : null;
        var account = selected[0].Account;
        // Nella vista di tutti gli account la scelta può mescolare più account: Sposta, Copia, Categorizza e Posponi
        // lavorano con le cartelle e le categorie di un account solo.
        bool one = OneAccount(selected);
        var involved = selected.Select(i => i.Account).Distinct().ToList();
        Brush? Tone(ContextMenus.Tone tone) => ContextMenus.Brush(tone);
        // Nella finestra del messaggio, come con la sua barra (MessageWindowBar): se il messaggio se n'è andato la finestra
        // si chiude. Resterebbe con l'id di prima, che in Outlook dopo lo spostamento non vale più.
        async Task Leave(Task<bool> moved)
        {
            if (await moved && reader != null) Window.GetWindow(reader)?.Close();
        }

        MenuItem? Build(string key)
        {
            switch (key)
            {
                case "reply" when single != null:
                    return MenuEntry("Rispondi", "", Tone(ContextMenus.Tone.Purple), (_, _) => ReplyTo(single!, ComposeMode.Reply, reader));
                case "replyAll" when single != null:
                    return MenuEntry("Rispondi a tutti", ContextMenus.ReplyAllGlyph, Tone(ContextMenus.Tone.Purple), (_, _) => ReplyTo(single!, ComposeMode.ReplyAll, reader));
                case "forward" when single != null:
                    return MenuEntry("Inoltra", ContextMenus.ForwardGlyph, Tone(ContextMenus.Tone.Blue), (_, _) => ReplyTo(single!, ComposeMode.Forward, reader));
                case "replyTemplate" when single != null && Templates.For(single.Account.Provider.Account).Count > 0:
                    // Rispondi con modello, come le Parti rapide di Outlook: la risposta con il modello già al cursore.
                    var withTemplate = MenuEntry("Rispondi con modello", "\ue8a5", Tone(ContextMenus.Tone.Purple));
                    foreach (var t in Templates.For(single.Account.Provider.Account))
                        withTemplate.Items.Add(MenuEntry(t.Name, "", null, (_, _) => ReplyWithTemplate(single!, t, reader)));
                    return withTemplate;
                case "meeting" when single != null && single.Account.Provider.HasCalendar:
                    return MenuEntry("Rispondi con riunione", "\ue787", Tone(ContextMenus.Tone.Purple), async (_, _) => await ReplyWithMeetingAsync(single!, reader));
                case "delete":
                    return MenuEntry("Elimina", "", null, async (_, _) => await Leave(DeleteAsync(selected)));
                case "archive":
                    return MenuEntry("Archivia", "", Tone(ContextMenus.Tone.Green), async (_, _) => await Leave(ArchiveAsync(selected)),
                                     involved.All(a => Special(a, FolderKind.Archive) != null));
                case "move" when one:
                    return FolderMenu("Sposta", "", Tone(ContextMenus.Tone.Blue), target => Leave(MoveAsync(selected, _ => target, "spostamento")), account, selected);
                case "copy" when one:
                    return FolderMenu("Copia", "", Tone(ContextMenus.Tone.Blue), target => CopyAsync(selected, target), account, selected);
                case "read":
                    bool allRead = selected.All(m => m.IsRead);
                    return MenuEntry(allRead ? "Segna come da leggere" : "Segna come già letto", allRead ? "" : "", null, async (_, _) => await SetReadAsync(selected, !allRead));
                case "pin" when single is { IsChild: false } && rows.Rows.Contains(single):
                    return MenuEntry(single.IsPinned ? "Rimuovi dall'alto" : "Fissa in alto", single.IsPinned ? "" : "", Tone(ContextMenus.Tone.Blue), (_, _) => TogglePin(single));
                case "categorize" when one && selected.All(rows.Rows.Contains):
                    return CategoryMenu(selected, account);
                case "snooze" when one && selected.All(rows.Rows.Contains):
                    return SnoozeMenu(selected);
                case "flag":
                    return FlagMenu(selected);
                case "rule" when single != null && HasRules(single.Account.Provider):
                    return MenuEntry("Crea regola", "", null, (_, _) => CreateRuleFrom(single!));
                case "report":
                    // In Gmail spostare in Spam è la segnalazione, e insegna al filtro. Come per Elimina conta la cartella
                    // dei messaggi, non quella aperta (ricerca in tutte le cartelle).
                    var report = MenuEntry("Segnala", "", Tone(ContextMenus.Tone.Red));
                    if (one && Special(account, FolderKind.Junk) is { } junk && Targets(selected).All(m => m.FolderId == junk.Id))
                        report.Items.Add(MenuEntry("Non è posta indesiderata", "", null,
                            async (_, _) => await Leave(MoveAsync(selected, a => Special(a, FolderKind.Inbox), "segnalazione"))));
                    else
                        report.Items.Add(MenuEntry("Segnala come posta indesiderata", "", Tone(ContextMenus.Tone.Red),
                            async (_, _) => await Leave(MoveAsync(selected, a => Special(a, FolderKind.Junk), "segnalazione")),
                            involved.All(a => Special(a, FolderKind.Junk) != null)));
                    return report;
                case "save" when single != null:
                    var save = MenuEntry("Salva con nome", "", null);
                    save.Items.Add(MenuEntry("File .eml (si apre con qualsiasi programma di posta)", "", null, async (_, _) => await SaveEmlAsync(single)));
                    save.Items.Add(MenuEntry("PDF", "", null, async (_, _) => await SavePdfAsync(single, reader), CanPrint(single, reader)));
                    return save;
                case "print" when single != null:
                    return MenuEntry("Stampa", "", null, (_, _) => (reader ?? Reader).Print(), CanPrint(single, reader));
                case "related" when single != null:
                    var related = MenuEntry("Trova elementi correlati", "", null);
                    var from = single.Summary.From.Email;
                    related.Items.Add(MenuEntry($"Messaggi da {single.Summary.From.Display}", "", null,
                        async (_, _) => { SearchBox.Text = "from:" + from; await SearchAsync("from:" + from, null, single.Account); }, from.Length > 0));
                    return related;
                case "view" when single != null:
                    var view = MenuEntry("Visualizza", "", null);
                    if (reader == null) view.Items.Add(MenuEntry("Apri in una nuova finestra", "", null, (_, _) => OpenInWindow(single)));
                    view.Items.Add(MenuEntry("Origine del messaggio", "", null, async (_, _) => await ShowSourceAsync(single)));
                    return view;
                case "window" when single != null && reader == null:
                    return MenuEntry("Apri in una nuova finestra", "", null, (_, _) => OpenInWindow(single));
                case "source" when single != null:
                    return MenuEntry("Origine del messaggio", "", null, async (_, _) => await ShowSourceAsync(single));
                default:
                    return null;
            }
        }

        FillCustomMenu(menu, ContextMenus.Messages, Build, this);
        return true;
    }

    // ---------------------------------------------------------------- Rispondi con riunione

    /// <summary>
    /// Rispondi con riunione, come nel nuovo Outlook: la finestra Nuovo evento con l'oggetto del messaggio, il mittente e gli
    /// altri destinatari come partecipanti (senza sé stessi) e il testo del messaggio nella descrizione; dalla prossima
    /// mezz'ora, nel calendario predefinito dell'account del messaggio. Serve il consenso al calendario: senza, lo si dice
    /// (nessuna finestra di Microsoft). Invia crea la riunione (nella prova solo il registro).
    /// </summary>
    async Task ReplyWithMeetingAsync(MessageItem item, ReadingView? reader)
    {
        var provider = item.Account.Provider;
        var owner = reader != null ? Window.GetWindow(reader) ?? this : this;
        if (!provider.HasCalendar)
        {
            Dialog.Show(owner, "Rispondi con riunione", $"Il calendario di {provider.Account.Email} non si usa ancora da Missiva: oggi c'è per gli account Microsoft e Google.");
            return;
        }
        CalendarData? data = null;
        try { data = await CalendarSource(provider, DateTime.Today, DateTime.Today.AddDays(1), CancellationToken.None); }
        catch (Exception ex) { Log.Error("calendari per Rispondi con riunione", ex); }
        var choices = (data?.Calendars ?? new()).Where(c => c.CanEdit).Select(c => new EventWindow.Choice(provider, c)).ToList();
        if (choices.Count == 0)
        {
            Dialog.Show(owner, "Rispondi con riunione", data == null
                ? $"Per creare la riunione Missiva deve poter usare il calendario di {provider.Account.Email}: apri il Calendario e premi \"Consenti l'accesso al calendario\"."
                : $"Nel calendario di {provider.Account.Email} non c'è un calendario in cui scrivere.");
            return;
        }
        var summary = item.Summary;
        MessageBody? body = (reader ?? Reader).Selected?.Id == summary.Id ? (reader ?? Reader).SelectedBody : null;
        body ??= provider.KeptBody(summary) ?? provider.SavedBody(summary);
        if (body == null)
        {
            try { body = await provider.GetBodyAsync(summary); }
            catch (Exception ex) { Log.Error("corpo per Rispondi con riunione", ex); }
        }
        var me = provider.Account.Identities.Select(i => i.Email).Append(provider.Account.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var people = new[] { summary.From }.Concat(body?.To ?? summary.To).Concat(body?.Cc ?? [])
            .Where(a => a.Email.Length > 0 && !me.Contains(a.Email))
            .GroupBy(a => a.Email.ToLowerInvariant()).Select(g => g.First()).ToList();
        var now = DateTime.Now;
        var start = now.Date.AddMinutes(30 * Math.Ceiling(now.TimeOfDay.TotalMinutes / 30));
        var window = new EventWindow(choices, choices.FirstOrDefault(c => c.Calendar.IsDefault), start, start.AddMinutes(30), false) { Owner = owner };
        var text = body is { Html: var html } ? await Task.Run(() => MailHtml.ReadableText(html)) : summary.Preview; // in sottofondo, come nella scrittura
        window.FillReply(summary.Subject, people, text);
        Log.Write($"rispondi con riunione: messaggio {summary.Id} di {provider.Account.Email}, {people.Count} partecipanti");
        if (window.ShowDialog() == true && Testing) Log.Write("rispondi con riunione: finestra chiusa con Invia");
    }

    // ---------------------------------------------------------------- Posponi

    /// <summary>
    /// Posponi, come in Outlook: il messaggio va nella cartella Posposti e all'ora scelta torna in Posta in arrivo, da
    /// leggere e in cima. Gmail non espone il suo Posponi ai programmi di posta: il ritorno lo fa Missiva, ogni minuto
    /// mentre è aperta, o appena si riapre.
    /// </summary>
    const string SnoozeFolderName = "Posposti";

    readonly DispatcherTimer snoozeTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    bool checkingSnoozed;
#if DEBUG || PROVA
    bool snoozeSkipped;
#endif

    static bool IsSnoozeFolder(FolderItem? f) => f is { Parent: null } && f.Folder.Name.Equals(SnoozeFolderName, StringComparison.OrdinalIgnoreCase);

    SnoozedMessage? SnoozeOf(AccountItem account, MessageSummary m) =>
        S.Snoozed.FirstOrDefault(s => s.AccountId == account.Provider.Account.Id && (s.Key == m.StableId || s.Key == m.Id));

    MenuItem SnoozeMenu(List<MessageItem> items)
    {
        var menu = MenuEntry("Posponi", "", null);
        if (IsSnoozeFolder(folder))
        {
            menu.Items.Add(MenuEntry("Riporta subito in Posta in arrivo", "", null, async (_, _) => await WakeNowAsync(items)));
            menu.Items.Add(new Separator());
        }
        void Option(string text, DateTime until)
        {
            var header = new DockPanel();
            var when = new TextBlock { Text = SnoozeWindow.Describe(until), Margin = new Thickness(24, 0, 0, 0) };
            when.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            DockPanel.SetDock(when, Dock.Right);
            header.Children.Add(when);
            header.Children.Add(new TextBlock { Text = text });
            var item = new MenuItem { Header = header };
            System.Windows.Automation.AutomationProperties.SetName(item, $"{text}, {SnoozeWindow.Describe(until)}"); // con un pannello per intestazione il nome sarebbe vuoto
            item.Click += async (_, _) => await SnoozeAsync(items, until);
            menu.Items.Add(item);
        }
        var now = DateTime.Now;
        // Di notte "più tardi oggi" vorrebbe dire le 4 del mattino: al suo posto, stamattina alle 8.
        if (now.Hour < 6) Option("Stamattina", now.Date.AddHours(8));
        else if (now.Hour < 18) Option("Più tardi oggi", now.Date.AddHours(now.Hour + 4)); // tra tre ore e qualcosa, a ora piena
        Option("Domani", now.Date.AddDays(1).AddHours(8));
        if (now.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Thursday)
            Option("Questo fine settimana", now.Date.AddDays(DayOfWeek.Saturday - now.DayOfWeek).AddHours(8));
        int toMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        // Di domenica lunedì mattina è già "Domani": la stessa ora due volte non serve.
        if (toMonday != 1) Option("La prossima settimana", now.Date.AddDays(toMonday == 0 ? 7 : toMonday).AddHours(8));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("Scegli una data e un'ora…", "", null, async (_, _) =>
        {
            var dialog = new SnoozeWindow { Owner = this };
            if (dialog.ShowDialog() == true) await SnoozeAsync(items, dialog.Until);
        }));
        return menu;
    }

    async Task SnoozeAsync(List<MessageItem> items, DateTime until)
    {
        if (items.Count == 0) return;
        var account = items[0].Account; // un account solo: il menu lo offre solo così
        var provider = account.Provider;
        var messages = Targets(items);

        // Già nei Posposti: cambia solo l'ora del ritorno.
        if (IsSnoozeFolder(folder))
        {
            foreach (var m in messages)
            {
                if (SnoozeOf(account, m) is { } s) s.Until = until;
                else S.Snoozed.Add(new SnoozedMessage { AccountId = provider.Account.Id, Key = m.StableId.Length > 0 ? m.StableId : m.Id,
                                                         FolderId = m.FolderId, Until = until, Subject = m.Subject, From = m.From.Display });
            }
            Store.Save();
            foreach (var row in items) row.RefreshDate();
            return;
        }

        bool creating = !account.Folders.Any(IsSnoozeFolder);
        RemoveRows(items);
        await RunAsync("posponi", async () =>
        {
            var target = await provider.EnsureFolderAsync(SnoozeFolderName);
            // Un salvataggio subito solo alla fine, anche se un messaggio non riesce: prima uno per messaggio, 12-26 ms l'uno sul
            // thread dell'interfaccia. Fra un messaggio e l'altro SaveSoon: accorpato e in sottofondo, al più 2 s dopo, così
            // di un Posponi lungo interrotto a metà si perde al più l'ora di ritorno degli ultimi secondi.
            try
            {
                foreach (var m in messages)
                {
                    var key = await provider.SnoozeAsync(m, target);
                    S.Snoozed.Add(new SnoozedMessage { AccountId = provider.Account.Id, Key = key, FolderId = target.Id, Until = until, Subject = m.Subject, From = m.From.Display });
                    Store.SaveSoon();
                    BodyCache.Forget(provider.Account, [m]); // uscito dalla cartella: la copia su disco non serve più
                }
            }
            finally
            {
                Store.Save();
            }
        });
        if (creating) await LoadFoldersAsync(account);
    }

    async Task WakeNowAsync(List<MessageItem> items)
    {
        if (items.Count == 0) return;
        var account = items[0].Account;
        var messages = Targets(items);
        var loose = new List<MessageItem>();
        foreach (var item in items)
        {
            if (SnoozeOf(account, item.Summary) is { } s) s.Until = DateTime.Now;
            else loose.Add(item);
        }
        Store.Save();
        RemoveRows(items.Except(loose).ToList());
        // Messaggi messi nei Posposti a mano: tornano in Posta in arrivo e basta.
        if (loose.Count > 0) await MoveAsync(loose, a => Special(a, FolderKind.Inbox), "ritorno in Posta in arrivo");
        await CheckSnoozedAsync();
    }

    /// <summary>Gli invii pianificati scaduti (ScheduledSending); una bozza sparita nel frattempo si dice con un avviso.</summary>
    async Task SendScheduledAsync()
    {
        if (S.ScheduledSends.Count == 0) return;
        await ScheduledSending.RunDueAsync(DateTime.Now, id => accounts.FirstOrDefault(a => a.Provider.Account.Id == id)?.Provider,
            (_, why) => Tell("Invio pianificato non riuscito", why));
    }

    /// <summary>
    /// Un avviso che arriva da solo (invio pianificato, azione di una notifica): a finestra aperta una finestra di dialogo, con
    /// Missiva nell'area di notifica una notifica di Windows, invece di una finestra che compare dal nulla.
    /// </summary>
    public void Tell(string heading, string text)
    {
        if (IsVisible)
        {
            Dialog.Show(this, heading, text);
            return;
        }
        Log.Write($"avviso come notifica (finestra nell'area di notifica): {heading}");
        try { App.Show(new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder().AddText(heading).AddText(text)); }
        catch (Exception ex) { Log.Error("notifica di un avviso", ex); }
    }

    /// <summary>Per account, il motivo per cui i ritorni da Posponi aspettano, già scritto nel registro.</summary>
    readonly Dictionary<string, string> snoozeWaitTold = new();
    const string OfflineWait = "non in linea";

    /// <summary>
    /// Perché un ritorno da Posponi non riuscito aspetta senza essere un errore: la rete che manca, o un accesso da rifare
    /// (la barra sotto l'account lo dice già). null per gli errori veri, che vanno nel registro per intero.
    /// </summary>
    internal static string? SnoozeWait(Exception ex) =>
        Network.IsOffline(ex) ? OfflineWait : MailException.FixOf(ex) != AccountFix.None ? ex.Message : null;

    /// <summary>Riporta in Posta in arrivo i messaggi posposti la cui ora è arrivata, con una notifica.</summary>
    async Task CheckSnoozedAsync()
    {
#if DEBUG || PROVA
        // Le prove (MISSIVA_NO_READ) non toccano la casella: la copia delle impostazioni di prova.ps1 ha gli stessi posposti
        // della Missiva installata, e la prova li riporterebbe da sé in Posta in arrivo, da leggere. Li riporta quella.
        if (Environment.GetEnvironmentVariable("MISSIVA_NO_READ") == "1")
        {
            int due = S.Snoozed.Count(s => s.Until <= DateTime.Now);
            if (due > 0 && !snoozeSkipped) Log.Write($"prova: {due} ritorni da Posponi lasciati alla Missiva installata");
            snoozeSkipped = due > 0;
            return;
        }
#endif
        if (checkingSnoozed) return;
        checkingSnoozed = true;
        var touched = new HashSet<AccountItem>();
        try
        {
            // Come in SnoozeAsync: più messaggi che tornano alla stessa ora (posposti insieme) fanno un salvataggio subito solo,
            // alla fine; fra l'uno e l'altro SaveSoon.
            try
            {
                foreach (var s in S.Snoozed.Where(s => s.Until <= DateTime.Now).ToList())
                {
                    var account = accounts.FirstOrDefault(a => a.Provider.Account.Id == s.AccountId);
                    if (account == null)
                    {
                        if (!S.Accounts.Any(a => a.Id == s.AccountId)) S.Snoozed.Remove(s); // account rimosso
                        continue;
                    }
                    if (Special(account, FolderKind.Inbox) is not { } inbox) continue; // cartelle non ancora caricate
                    // Si riprova al prossimo minuto. Senza rete o con un accesso da rifare una riga sola per account e motivo:
                    // prima l'eccezione intera con lo stack, ogni minuto e per ogni messaggio scaduto (una notte senza rete
                    // riempiva il registro, audit del 7 ottobre, A-17).
                    void Waits(string why)
                    {
                        if (snoozeWaitTold.GetValueOrDefault(s.AccountId) == why) return;
                        snoozeWaitTold[s.AccountId] = why;
                        Log.Write($"ritorno da Posponi su {account.Header} rimandato: {why} (si riprova ogni minuto; non lo riscrivo finché non cambia)");
                    }
                    if (Network.KnownOffline(account.Provider.Account))
                    {
                        Waits(OfflineWait); // nessuna richiesta che fallirebbe
                        continue;
                    }
                    try
                    {
                        var key = await account.Provider.WakeAsync(s.Key, new MailFolder { Id = s.FolderId, Name = SnoozeFolderName }, inbox);
                        snoozeWaitTold.Remove(s.AccountId);
                        S.Snoozed.Remove(s);
                        if (key != null)
                        {
                            S.Woken[s.AccountId + "|" + key] = DateTime.Now;
                            NotifyReturned(account, s);
                        }
                        Store.SaveSoon();
                        touched.Add(account);
                    }
                    catch (Exception ex)
                    {
                        if (SnoozeWait(ex) is { } why) Waits(why);
                        else Log.Error("ritorno da Posponi", ex);
                    }
                }
            }
            finally
            {
                if (touched.Count > 0) Store.Save();
            }
            foreach (var account in touched)
            {
                await LoadFoldersAsync(account);
                if (folder?.Account == account && (folder.Folder.Kind == FolderKind.Inbox || IsSnoozeFolder(folder))) await RefreshListAsync();
            }
        }
        finally
        {
            checkingSnoozed = false;
        }
    }

    static void NotifyReturned(AccountItem account, SnoozedMessage s)
    {
        try
        {
            App.Show(new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                .AddText("Di nuovo in Posta in arrivo")
                .AddText(s.From)
                .AddText(s.Subject.Length > 0 ? s.Subject : "(nessun oggetto)")
                .AddAttributionText(account.Provider.Account.Email));
        }
        catch (Exception ex)
        {
            Log.Error("notifica di Posponi", ex);
        }
    }

    /// <summary>
    /// I messaggi tornati da Posponi valgono con l'ora del ritorno, così stanno in cima fra quelli di oggi. Con l'elenco per
    /// data (order) la pagina si riordina, stabile (a parità l'ordine del provider): Gmail e IMAP la ordinano per
    /// INTERNALDATE e Graph per receivedDateTime, che lo spostamento non cambia, e il messaggio tornato mostrava l'ora del
    /// ritorno ma restava fra quelli vecchi.
    /// </summary>
    internal static void ApplyWoken(List<MessageSummary> messages, AccountItem account, ListOptions? order = null) =>
        ApplyWoken(messages, account.Provider.Account.Id, order);

    internal static void ApplyWoken(List<MessageSummary> messages, string accountId, ListOptions? order)
    {
        if (S.Woken.Count == 0) return;
        bool changed = false;
        foreach (var m in messages)
            if (S.Woken.TryGetValue(accountId + "|" + (m.StableId.Length > 0 ? m.StableId : m.Id), out var at) && at > m.Date)
            {
                m.Date = at;
                changed = true;
            }
        if (!changed || order is not { Sort: SortBy.Date }) return;
        var sorted = order.Ascending ? messages.OrderBy(m => m.Date).ToList() : messages.OrderByDescending(m => m.Date).ToList();
        messages.Clear();
        messages.AddRange(sorted);
    }

    /// <summary>Aggiorna la prima pagina della cartella aperta senza svuotare l'elenco né il riquadro di lettura.</summary>
    async Task RefreshListAsync()
    {
        if (folder == null && unified != null && !searching && !loading)
        {
            await ReloadAsync(); // la vista di tutti gli account si rilegge da capo
            return;
        }
        if (folder == null || searching || loading) return;
        var current = folder;
        var version = listVersion;
        try
        {
            var page = await current.Account.Provider.GetMessagesAsync(current.Folder, focused, null, listOptions);
            if (version != listVersion) return;
            ApplyWoken(page.Messages, current.Account, listOptions);
            ActionQueue.Apply(current.Account.Provider.Account.Id, page.Messages);
            if (!MergeFirstPage(page.Messages, current.Account)) ShowFirstPage(page.Messages, current.Account, fromCache: true);
            next = page.Next;
            SetListStatus(rows.Rows.Count == 0 ? "Non ci sono messaggi qui." : null);
            SaveFirstPage(current.Account.Provider.Account, current.Folder.Id, focused, firstPage);
            _ = FillPreviewsAsync(current, version, firstPage, prefetch: true);
        }
        catch (Exception ex)
        {
            Log.Error("aggiornamento dell'elenco", ex);
        }
    }

    // ---------------------------------------------------------------- categorie

    /// <summary>
    /// Categorizza: le categorie dell'account (le etichette nascoste dall'elenco no), con il segno di spunta su quelle
    /// che hanno già tutti i messaggi selezionati; un clic la mette o la toglie.
    /// </summary>
    MenuItem CategoryMenu(List<MessageItem> selected, AccountItem account)
    {
        var menu = MenuEntry("Categorizza", "", null);
        var id = account.Provider.Account.Id;
        var messages = Targets(selected);
        var categories = account.Categories.Where(c => !S.HiddenFolders.Contains(id + "|" + c.Name))
                                           .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var c in categories)
        {
            bool all = messages.All(m => m.Categories.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
            var header = new DockPanel { LastChildFill = true };
            if (all)
            {
                var check = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(check, Dock.Right);
                header.Children.Add(check);
            }
            header.Children.Add(new TextBlock { Text = c.Name });
            var entry = new MenuItem { Header = header, Icon = Swatch(c.Preset) };
            var name = c.Name;
            entry.Click += async (_, _) => await SetCategoryAsync(selected, name, !all);
            menu.Items.Add(entry);
        }
        if (categories.Count == 0) menu.Items.Add(new MenuItem { Header = "Nessuna categoria", IsEnabled = false });
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("Nuova categoria…", "", null, async (_, _) => await NewCategoryAsync(selected)));
        menu.Items.Add(MenuEntry("Togli tutte le categorie", "", null, async (_, _) => await ClearCategoriesAsync(selected),
            messages.Any(m => m.Categories.Any(c => !c.Equals(m.FolderId, StringComparison.OrdinalIgnoreCase)))));
        return menu;
    }

    static Border Swatch(int preset) => new()
    {
        Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(CategoryPalette.Hex(preset))),
    };

    async Task LoadCategoriesAsync(AccountItem account)
    {
        try
        {
            account.Categories = await account.Provider.GetCategoriesAsync();
            CategoryPalette.Remember(account.Provider.Account.Id, account.Categories);
            foreach (var row in rows.Rows.Where(r => r.Account == account)) row.RefreshCategories();
        }
        catch (Exception ex)
        {
            Log.Error($"categorie di {account.Header}", ex);
        }
    }

    async Task SetCategoryAsync(List<MessageItem> items, string name, bool on)
    {
        if (items.Count == 0) return;
        var messages = Targets(items);
        var provider = items[0].Provider;
        await RunAsync("categorie", () => provider.SetCategoryAsync(messages, name, on));
        // In Gmail, togliere l'etichetta della cartella aperta fa uscire il messaggio dalla cartella.
        if (!on && folder != null && folder.Folder.Id.Equals(name, StringComparison.OrdinalIgnoreCase)) RemoveRows(items);
        RefreshCategories(messages);
    }

    async Task ClearCategoriesAsync(List<MessageItem> items)
    {
        if (items.Count == 0) return;
        var messages = Targets(items);
        var provider = items[0].Provider;
        var current = folder?.Folder.Id ?? "";
        // Una richiesta per categoria, con tutti i messaggi che ce l'hanno.
        var categories = messages.SelectMany(m => m.Categories).Where(c => !c.Equals(current, StringComparison.OrdinalIgnoreCase))
                                 .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await RunAsync("categorie", async () =>
        {
            foreach (var c in categories)
                await provider.SetCategoryAsync(messages.Where(m => m.Categories.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList(), c, false);
        });
        RefreshCategories(messages);
    }

    async Task NewCategoryAsync(List<MessageItem> items)
    {
        if ((items.FirstOrDefault()?.Account ?? folder?.Account) is not { } account) return;
        var dialog = new CategoryWindow(account.Categories, account.Provider.Account.Kind == AccountKind.Gmail) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Category created;
        try
        {
            created = await account.Provider.CreateCategoryAsync(dialog.CategoryName, dialog.Preset);
        }
        catch (Exception ex)
        {
            Log.Error("nuova categoria", ex);
            Dialog.Show(this, "Categoria non creata", ex.Message);
            return;
        }
        account.Categories.Add(created);
        CategoryPalette.Remember(account.Provider.Account.Id, account.Categories);
        await SetCategoryAsync(items, created.Name, true);
        // In Gmail la categoria è un'etichetta nuova: compare anche fra le cartelle.
        if (account.Provider.Account.Kind == AccountKind.Gmail) await LoadFoldersAsync(account);
    }

    void RefreshCategories(List<MessageSummary> messages)
    {
        var set = messages.ToHashSet();
        foreach (var row in rows.Rows.Where(r => set.Contains(r.Summary))) row.RefreshCategories();
        if (Reader.Selected is { } shown && set.Any(m => m.Id == shown.Id)) Reader.Refresh();
        if (folder != null) SaveFirstPage(folder.Account.Provider.Account, folder.Folder.Id, focused, firstPage);
    }

    static MenuItem MenuEntry(string header, string glyph, Brush? color, RoutedEventHandler? click = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, Icon = ContextMenus.Icon(glyph, color), IsEnabled = enabled };
        if (click != null) item.Click += click;
        // Le voci con sottomenu tengono spazio per la freccia a destra.
        item.Loaded += (_, _) => { if (item.HasItems && item.Header is string text) item.Header = new TextBlock { Text = text, Margin = new Thickness(0, 0, 20, 0) }; };
        return item;
    }

    /// <summary>Sottomenu con le cartelle dell'account (tranne quella dei messaggi), rientrate come nell'albero.</summary>
    MenuItem FolderMenu(string header, string glyph, Brush? color, Func<MailFolder, Task> action, AccountItem account, List<MessageItem> items)
    {
        var menu = MenuEntry(header, glyph, color);
        var ids = Targets(items).Select(m => m.FolderId).Distinct().ToList();
        var here = ids.Count == 1 ? ids[0] : null;
        foreach (var f in account.Folders.Where(f => f.Folder.Id != here && !f.IsHidden))
        {
            var target = f.Folder;
            var entry = new MenuItem { Header = new TextBlock { Text = f.Name, Margin = new Thickness(f.Depth * 16, 0, 0, 0) } };
            entry.Click += async (_, _) => await action(target);
            menu.Items.Add(entry);
        }
        return menu;
    }

    async Task CopyAsync(List<MessageItem> items, MailFolder target, bool simulate = false)
    {
        if (items.Count == 0) return;
        var messages = Targets(items);
        var provider = items[0].Provider;
        if (simulate || Testing) Log.Write($"prova: copierei {messages.Count} messaggi in {target.Name}");
        else await RunAsync("copia", () => provider.CopyAsync(messages, target));
    }

    async Task SaveEmlAsync(MessageItem item)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Safety.SafeFileName(item.Subject) + ".eml",
            Filter = "Messaggio di posta (*.eml)|*.eml",
        };
        if (dialog.ShowDialog(this) != true) return;
        // Con il marchio web come gli allegati salvati (audit, S-10): chi lo riapre da un'altra parte è avvisato da Windows. Dal
        // gestore degli scaricamenti (ritocchi del 16 ottobre 2026): l'avanzamento, Annulla, e un errore detto dall'elemento.
        var provider = item.Provider;
        await DownloadList.Session.SaveAsync(dialog.FileName, null, (progress, cancel) => provider.GetRawAsync(item.Summary, int.MaxValue, cancel, progress),
                                             provider.Account.Id, ReadingView.InJunk(item.Summary, item.Account.Folders.FirstOrDefault(f => f.Folder.Kind == FolderKind.Junk)?.Folder.Id));
    }

    async Task SavePdfAsync(MessageItem item, ReadingView? reader = null)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Safety.SafeFileName(item.Subject) + ".pdf",
            Filter = "Documento PDF (*.pdf)|*.pdf",
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!await (reader ?? Reader).SavePdfAsync(dialog.FileName)) Dialog.Show(this, "PDF non salvato", "Il riquadro di lettura non è riuscito a creare il PDF.");
    }

    /// <summary>L'Origine mostra al massimo l'inizio del messaggio: gli allegati grandi non si scaricano.</summary>
    const int SourceLimit = 2_000_000;

    /// <summary>Il messaggio com'è arrivato, intestazioni comprese: serve per controllare da dove viene davvero.</summary>
    async Task ShowSourceAsync(MessageItem item)
    {
        byte[] raw;
        try { raw = await item.Provider.GetRawAsync(item.Summary, SourceLimit); }
        catch (Exception ex)
        {
            Log.Error("origine del messaggio", ex);
            Dialog.Show(this, "Origine non disponibile", ex.Message);
            return;
        }
        var text = System.Text.Encoding.UTF8.GetString(raw);
        if (raw.Length >= SourceLimit) text += "\n\n[Il messaggio continua: qui ci sono i primi 2 MB. Intero con Salva con nome, File .eml.]";
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0), Padding = new Thickness(12),
        };
        new Window { Title = "Origine: " + item.Subject, Content = box, Width = 900, Height = 700, Owner = this, Icon = Icon,
                     WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }

    // ---------------------------------------------------------------- scrittura

    void OnNew(object sender, RoutedEventArgs e)
    {
        if (accounts.Count == 0)
        {
            OnAddAccount(sender, e);
            return;
        }
        // Dalla vista di tutti gli account: l'account del messaggio scelto, o il primo.
        var from = folder?.Account.Provider ?? Selected.FirstOrDefault()?.Provider ?? accounts[0].Provider;
        ShowCompose(new ComposeWindow(Providers.ToList(), from, ComposeMode.New, null, null) { Owner = this });
    }

    /// <summary>La finestra di scrittura: salvando una bozza (o inviandone una) le Bozze aperte si aggiornano.</summary>
    void ShowCompose(ComposeWindow window)
    {
        window.DraftsChanged += OnDraftsChanged;
        window.Show();
    }

    void OnDraftsChanged(IMailProvider provider)
    {
        if (folder is { Folder.Kind: FolderKind.Drafts } drafts && drafts.Account.Provider == provider && !searching) _ = ReloadAsync();
        else if (folder == null && unified is { View: UnifiedView.Drafts } && !searching) _ = ReloadAsync();
    }

    /// <summary>
    /// Modifica, nelle Bozze (riquadro, doppio clic, Invio): la bozza si continua nella finestra di scrittura. Mai da una
    /// copia su disco (senza rete): salvando sostituirebbe sul server una versione più nuova; la finestra la rilegge, e
    /// senza rete lo dice.
    /// </summary>
    void OpenDraft(IMailProvider provider, MessageSummary draft, MessageBody? body) =>
        ShowCompose(new ComposeWindow(Providers.ToList(), provider, ComposeMode.New, null, null, draft, body?.OfflineCopy != null ? null : body) { Owner = this });

    void OnReply(object sender, RoutedEventArgs e) => ReplySelected(ComposeMode.Reply);
    void OnReplyAll(object sender, RoutedEventArgs e) => ReplySelected(ComposeMode.ReplyAll);
    void OnForward(object sender, RoutedEventArgs e) => ReplySelected(ComposeMode.Forward);

    void ReplySelected(ComposeMode mode)
    {
        if (Selected is [var item]) ReplyTo(item, mode);
    }

    /// <param name="reader">Il riquadro che mostra il messaggio, se è quello di una finestra del messaggio: il corpo è già lì.</param>
    void ReplyTo(MessageItem item, ComposeMode mode, ReadingView? reader = null)
    {
        var shown = reader ?? Reader;
        OpenCompose(item.Provider, item.Summary, mode, shown.Selected == item.Summary ? shown.SelectedBody : null);
    }

    /// <summary>Rispondi con modello: la risposta, con il modello inserito quando i destinatari sono al loro posto ({nome}).</summary>
    void ReplyWithTemplate(MessageItem item, MailTemplate template, ReadingView? reader)
    {
        var shown = reader ?? Reader;
        var providers = Providers.ToList();
        if (!providers.Contains(item.Provider)) providers.Add(item.Provider);
        var window = new ComposeWindow(providers, item.Provider, ComposeMode.Reply, item.Summary, shown.Selected == item.Summary ? shown.SelectedBody : null) { Owner = this };
        ShowCompose(window);
        window.UseTemplateWhenReady(template);
    }

    void OpenCompose(IMailProvider provider, MessageSummary summary, ComposeMode mode, MessageBody? body)
    {
        // Un .eml aperto in Missiva ha il suo provider (EmlProvider), che fa partire la risposta dall'account che lo conteneva.
        var providers = Providers.ToList();
        if (!providers.Contains(provider)) providers.Add(provider);
        ShowCompose(new ComposeWindow(providers, provider, mode, summary, body) { Owner = this });
    }

    // ---------------------------------------------------------------- inviti e .eml

    /// <summary>
    /// Accetta, Provvisorio, Rifiuta o Proponi sulla scheda di un invito: la nota e l'invio della risposta (InviteWindow), poi
    /// il provider risponde (iMIP, o sull'evento di Outlook per gli inviti di Exchange).
    /// </summary>
    async void OnInvite(IMailProvider provider, MessageSummary summary, Invitation invite, InviteAnswer answer)
    {
        if (!invite.Answerable) return; // risposte, proposte ed eventi pubblicati non hanno pulsanti
        if (invite.NeedsConsent)
        {
            Dialog.Show(this, "Serve l'accesso al calendario",
                "Per rispondere agli inviti di Outlook, Missiva deve poter usare il tuo calendario: premi \"Consenti l'accesso al calendario\" nella scheda dell'invito.");
            return;
        }
        var window = new InviteWindow(invite, answer) { Owner = this };
        if (window.ShowDialog() != true || window.Response is not { } response) return;
        try
        {
            await provider.RespondAsync(summary, invite, response);
            Log.Write($"invito: risposta {response.Answer} a {Log.Private(invite.Summary)}{(response.Send ? ", mandata all'organizzatore" : "")}");
        }
        catch (Exception ex)
        {
            Log.Error("risposta all'invito", ex);
            Dialog.Show(this, "Risposta non inviata", ex is MailException ? ex.Message : "La risposta all'invito non è partita: " + ex.Message);
        }
    }

    /// <summary>
    /// Un messaggio .eml (un allegato) in una finestra di Missiva, letto con MimeKit e mostrato nel riquadro confinato. Letto
    /// fuori dal thread dell'interfaccia, invito compreso: un file grande non ferma la finestra.
    /// </summary>
    /// <param name="fromJunk">Il messaggio che lo contiene sta nella Posta indesiderata: l'allegato si apre come se ci stesse anche lui.</param>
    async void OpenEml(IMailProvider provider, byte[] bytes, string name, bool fromJunk)
    {
        EmlProvider eml;
        try
        {
            eml = await Task.Run(() => new EmlProvider(MimeKit.MimeMessage.Load(new MemoryStream(bytes)), provider));
        }
        catch (Exception ex)
        {
            Log.Error("messaggio .eml", ex);
            Dialog.Show(this, "Messaggio non leggibile", $"\"{name}\" non è un messaggio di posta che Missiva sa leggere.");
            return;
        }
        OpenEmlWindow(eml, fromJunk);
    }

    void OpenEmlWindow(EmlProvider eml, bool fromJunk)
    {
        var reader = new ReadingView();
        reader.ReplyRequested += OpenCompose;
        reader.MailtoRequested += OnMailto;
        reader.InviteRequested += OnInvite;
        reader.EmlRequested += OpenEml;
        var window = new Window
        {
            Title = eml.Summary.Subject.Length > 0 ? eml.Summary.Subject : "Messaggio",
            Content = reader,
            Width = 900,
            Height = 780,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = Icon,
        };
        window.Show();
        _ = reader.ShowAsync(eml, eml.Summary, false, ReadingView.EmlJunk(eml, fromJunk));
    }

    // ---------------------------------------------------------------- ricerca

    void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 && SearchPills.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = SearchBox.Text.Length > 0 || searchFilters.HasFilters ? Visibility.Visible : Visibility.Collapsed;
        UpdateSuggestions();
    }

    async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            OnClearSearch(sender, e);
            return;
        }
        if (e.Key != Key.Enter || !HasList) return;
        if (calendarShown) ShowCalendar(false); // la ricerca è nella posta
        if (peopleShown) ShowPeople(false);
        var query = SearchBox.Text.Trim();
        if (query.Length == 0 && !searchFilters.HasFilters)
        {
            OnClearSearch(sender, e);
            return;
        }
        // I filtri scritti a mano (da:rossi) e quelli delle pastiglie li unisce TakeFilters, in SearchAsync e SearchAccountsAsync.
        if (SearchesAccounts) await SearchAccountsAsync(query, _ => query);
        else await SearchAsync(query, searchAll ? null : folder);
    }

    /// <summary>Dove si cerca: in tutte le cartelle dell'account aperto (come di base nel nuovo Outlook) o in quella aperta.</summary>
    bool searchAll = true;
    string searchQuery = "";
    /// <summary>La cartella della ricerca in corso; null se in tutte quelle di searchAccount.</summary>
    FolderItem? searchWhere;
    AccountItem? searchAccount;
    string? searchNext;

    /// <summary>
    /// Ricerca in tutte le cartelle dell'account della cartella aperta (where null) o in una sola. I risultati prendono il
    /// posto dell'elenco, con la cartella di ognuno; scorrendo in fondo arrivano i successivi, 50 alla volta.
    /// </summary>
    /// <param name="account">L'account in cui cercare quando non c'è una cartella (Messaggi da..., dalla vista di tutti gli account).</param>
    async Task SearchAsync(string query, FolderItem? where, AccountItem? account = null)
    {
        if ((where?.Account ?? account ?? folder?.Account) is not { } current) return;
        query = TakeFilters(query); // i filtri della ricerca: pastiglie e testo con le parole chiave (SearchTerms)
        CloseSuggestions(keep: [SuggestSearches.Key(current.Provider.Account.Id, where?.Folder.Id, query)]); // la ricerca dei suggerimenti con lo stesso testo si riusa
        Store.Settings.RememberSearch(query);
        Store.SaveSoon();
        searching = true;
        // Come in Outlook, durante la ricerca nessuna cartella resta selezionata: l'elenco sono i risultati. Chiudendo la
        // ricerca torna la cartella di prima (OnClearSearch).
        MarkSelectedFolder(null);
        listVersion++;
        CancelListPrefetch();
        CancelOfflineSearch(); // la ricerca di prima nella copia sul PC, se ancora in corso, si ferma
        searchQuery = query;
        searchWhere = where;
        searchAccount = current;
        searchAcross = null;
        searchNext = null;
        rows.Clear();
        rows.ShowAccounts = Reader.ShowAccount = false;
        rows.ByDue = false;
        Reader.Clear();
        ListTitle.Text = $"Risultati in {where?.Name ?? "Tutte le cartelle"}";
        ListTitle.ToolTip = ListTitle.Text;
        // Preferiti ed Evidenziata/Altra valgono per la cartella, non per i risultati.
        StarButton.Visibility = FocusedTabs.Visibility = Visibility.Collapsed;
        SetListStatus("Ricerca in corso…");
        await SearchPageAsync(first: true);
    }

    async Task SearchPageAsync(bool first)
    {
        if (searchAcross != null)
        {
            await SearchAccountsPageAsync(first);
            return;
        }
        if (!searching || searchAccount is not { } account || !first && (loading || searchNext == null)) return;
        loading = true;
        var version = listVersion;
        try
        {
            var clock = Stopwatch.StartNew();
            var page = await SearchOrOfflineAsync(account, searchWhere, searchQuery, first ? null : searchNext);
            Log.Time($"ricerca in {searchWhere?.Name ?? "tutte le cartelle"} di {account.Header} ({page.Messages.Count} risultati{(first ? "" : ", pagina successiva")})", clock);
            if (version != listVersion) return;
            if (first && page.Next == OfflineResults)
            {
                page = new MessagePage { Messages = page.Messages }; // la copia sul PC non ha altre pagine
                ListTitle.Text = searchWhere == null ? "Risultati sul PC (non in linea)" : $"Risultati sul PC in {searchWhere.Name}";
                ListTitle.ToolTip = "Non in linea: i risultati vengono dalla copia sul PC (messaggi aperti e Posta in arrivo degli ultimi 30 giorni).";
            }
            // Graph dice solo l'identificativo della cartella: il nome lo sa l'elenco delle cartelle.
            if (searchWhere == null)
                foreach (var m in page.Messages.Where(m => m.FolderName.Length == 0))
                    m.FolderName = account.Folders.FirstOrDefault(f => f.Folder.Id == m.FolderId)?.Name ?? "";
            AppendKeepingPosition(() => rows.Append(page.Messages, account));
            searchNext = page.Next;
            SetListStatus(rows.Rows.Count == 0 ? $"Nessun risultato per \"{searchQuery}\"." : null);
            _ = FillPreviewsAsync(searchWhere ?? (folder?.Account == account ? folder : null) ?? account.Folders[0], version, page.Messages, prefetch: first);
        }
        catch (Exception ex)
        {
            Log.Error("ricerca", ex);
            if (version == listVersion) SetListStatus("Ricerca non riuscita: " + ex.Message, () => _ = SearchPageAsync(first));
        }
        finally
        {
            if (version == listVersion) loading = false;
        }
    }

    void OnSearchToolsSize(object sender, SizeChangedEventArgs e) => FitSearchPadding();

    /// <summary>
    /// Dove cercare: tutte le cartelle dell'account aperto, la cartella aperta, o tutti gli account (come le cartelle comuni
    /// di Thunderbird). Dalla vista di tutti gli account si cerca sempre in tutti gli account.
    /// </summary>
    void OnSearchScope(object sender, RoutedEventArgs e)
    {
        bool across = SearchesAccounts;
        if (folder == null && unified != null)
            ShowMenu(SearchScopeButton, ("Tutti gli account", true, () => SetSearchScope(true, true)));
        else
            ShowMenu(SearchScopeButton,
                ("Tutte le cartelle", searchAll && !across, () => SetSearchScope(true, false)),
                ("Cartella corrente", !searchAll && !across, () => SetSearchScope(false, false)),
                ("Tutti gli account", across, () => SetSearchScope(true, true)));
    }

    /// <summary>Cambiando dove cercare, la ricerca in corso si ripete.</summary>
    void SetSearchScope(bool all, bool accountsToo = false)
    {
        searchAll = all;
        searchAccounts = accountsToo;
        ShowSearchScope();
        if (!searching || searchQuery.Length == 0) return;
        if (SearchesAccounts) _ = SearchAccountsAsync(searchQuery, _ => searchQuery);
        else if (folder != null) _ = SearchAsync(searchQuery, all ? null : folder);
    }

    void ShowSearchScope() =>
        SearchScopeText.Text = SearchesAccounts ? "Tutti gli account" : searchAll ? "Tutte le cartelle" : "Cartella corrente";

    void OnClearSearch(object sender, RoutedEventArgs e)
    {
        ClearFilters();
        SearchBox.Text = "";
        if (!searching || !HasList) return;
        searching = false;
        searchAcross = null;
        CancelOfflineSearch();
        if (folder != null) MarkSelectedFolder(folder);
        else MarkUnified(unified);
        FocusedTabs.Visibility = focused != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateListHeader();
        _ = ReloadAsync(fromTop: true);
    }

    // ---------------------------------------------------------------- scheda Visualizza

    void OnTab(object sender, RoutedEventArgs e)
    {
        if (HomeBar == null || ViewBar == null) return;
        HomeBar.Visibility = HomeTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewBar.Visibility = ViewTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(FitBars, DispatcherPriority.Loaded);
    }

    void OnViewSettings(object sender, RoutedEventArgs e) => OpenSettings();

    void OpenSettings(string? category = null, string? section = null)
    {
        var settings = new SettingsWindow(() => ApplySettings(), RemoveAccountAsync, () => OnAddAccount(this, new RoutedEventArgs()), ManageAccountAsync, EditAutoRepliesAsync, RuleHostFor)
            { Owner = this };
        if (category != null && section != null) settings.Open(category, section);
#if DEBUG
        DevTools.HideForPhotos(settings); // le foto (--foto): non si vede e non prende il fuoco
#endif
        settings.ShowDialog();
    }

#if DEBUG
    /// <summary>
    /// Prove e foto ("impostazioni Generali Notifiche" in MISSIVA_KEYS): le Impostazioni su quella pagina, aperte dopo il
    /// comando, così la finestra modale non ferma i comandi che seguono; "impostazioni chiudi" le chiude.
    /// </summary>
    internal void TestSettings(string what)
    {
        if (what.Equals("chiudi", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var open in OwnedWindows.OfType<SettingsWindow>().ToList()) open.Close();
            return;
        }
        var parts = what.Split(' ', 2, StringSplitOptions.TrimEntries);
        Dispatcher.BeginInvoke(() => OpenSettings(parts[0], parts.Length > 1 ? parts[1] : "Informazioni"));
    }
#endif

    // ---------------------------------------------------------------- regole

    /// <summary>Per le regole di un account: il suo provider e le cartelle già arrivate; null se l'account non è ancora caricato.</summary>
    RuleHost? RuleHostFor(MailAccount account) =>
        accounts.FirstOrDefault(a => a.Provider.Account.Id == account.Id) is { } item
            ? new RuleHost(item.Provider, item.Snapshot(), () => _ = RefreshListAsync())
            : null;

    /// <summary>
    /// Crea regola dal menu del messaggio, come nel nuovo Outlook: la finestra della regola con il mittente già scritto (e la
    /// condizione accesa) e l'oggetto scritto ma spento. Le caselle condivise non hanno regole in Missiva.
    /// </summary>
    void CreateRuleFrom(MessageItem item)
    {
        if (RuleHostFor(item.Account.Provider.Account) is not { } host) return;
        var summary = item.Summary;
        var prefill = new MailRule
        {
            AccountId = host.Provider.Account.Id,
            Name = "Da " + (summary.From.Name.Length > 0 ? summary.From.Name : summary.From.Email),
            FromAddresses = summary.From.Email.Length > 0 ? [summary.From.Email] : [],
            SubjectContains = summary.Subject.Trim().Length > 0 ? [summary.Subject.Trim()] : [],
        };
        var window = new RuleWindow(host.Provider, host.Folders, null, prefill) { Owner = this };
        if (window.ShowDialog() == true && window.Saved is { } saved)
            Log.Write($"regola {Log.Private(saved.Name)} creata da un messaggio di {host.Provider.Account.Email}");
    }

    /// <summary>Se l'account ha regole in Missiva: quelle di Outlook (non le caselle condivise) o quelle di Missiva per Gmail e IMAP.</summary>
    static bool HasRules(IMailProvider provider) => provider.HasServerRules || provider.Account.Kind != AccountKind.Microsoft;

    void ShowMenu(FrameworkElement target, params (string Header, bool Checked, Action Action)[] items)
    {
        var menu = new ContextMenu { PlacementTarget = Anchor(target), Placement = PlacementMode.Bottom };
        foreach (var (header, isChecked, action) in items)
        {
            var entry = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
    }

    void OnMessagesMenu(object sender, RoutedEventArgs e) => ShowMenu(MessagesMenuButton,
        ("Mostra i messaggi come conversazioni", S.GroupByConversation, () => { S.GroupByConversation = !S.GroupByConversation; SaveAndApply(); }),
        ("Nel riquadro di lettura mostra tutta la conversazione", S.ReadingWholeConversation, () => { S.ReadingWholeConversation = !S.ReadingWholeConversation; SaveAndApply(); }),
        ("Intestazioni per data", S.ShowDateHeaders, () => { S.ShowDateHeaders = !S.ShowDateHeaders; SaveAndApply(reload: false); }));

    void OnZoomMenu(object sender, RoutedEventArgs e) => ShowMenu(ZoomButton,
        new[] { 80, 90, 100, 110, 125, 150 }.Select(z => ($"{z}%", S.Zoom == z, (Action)(() => { S.Zoom = z; SaveAndApply(reload: false); }))).ToArray());

    void OnLightToggle(object sender, RoutedEventArgs e)
    {
        S.LightReadingPane = LightToggle.IsChecked == true;
        SaveAndApply(reload: false);
    }

    void OnPaneMenu(object sender, RoutedEventArgs e) => ShowMenu(PaneMenuButton,
        ("A destra", S.ReadingPane == ReadingPanePosition.Right, () => { S.ReadingPane = ReadingPanePosition.Right; SaveAndApply(reload: false); }),
        ("In basso", S.ReadingPane == ReadingPanePosition.Bottom, () => { S.ReadingPane = ReadingPanePosition.Bottom; SaveAndApply(reload: false); }),
        ("Nascosto (apri i messaggi con doppio clic)", S.ReadingPane == ReadingPanePosition.Off, () => { S.ReadingPane = ReadingPanePosition.Off; SaveAndApply(reload: false); }));

    void OnSizeMenu(object sender, RoutedEventArgs e) => ShowMenu(SizeMenuButton,
        ("Piccola", S.TextSize == TextSize.Small, () => { S.TextSize = TextSize.Small; SaveAndApply(); }),
        ("Media", S.TextSize == TextSize.Medium, () => { S.TextSize = TextSize.Medium; SaveAndApply(); }),
        ("Di grandi dimensioni", S.TextSize == TextSize.Large, () => { S.TextSize = TextSize.Large; SaveAndApply(); }));

    // ---------------------------------------------------------------- altro

    /// <summary>
    /// Ridotta a icona da qualche secondo, Missiva restituisce a Windows la memoria che non usa: una raccolta completa e
    /// compattata, anche dei blocchi grandi (corpi delle email, pagine del riquadro), che altrimenti restano occupati.
    /// WebView2 fa lo stesso per conto suo (ReadingView.OnHostState).
    /// </summary>
    async void OnStateChanged(object? sender, EventArgs e)
    {
        FitMaximized();
        if (WindowState != WindowState.Minimized || only != null) return;
        await Task.Delay(3000);
        if (WindowState != WindowState.Minimized) return;
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    void OnToggleFolders(object sender, RoutedEventArgs e)
    {
        if (compact) foldersOver = !foldersOver;
        else foldersClosed = !foldersClosed;
        ShowFolders();
    }

    async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    async Task RefreshAllAsync()
    {
        var selectedId = folder?.Folder.Id;
        var selectedAccount = folder?.Account;
        var view = folder == null ? unified : null;
        await Task.WhenAll(accounts.Select(LoadFoldersAsync));
        var again = selectedAccount?.Folders.FirstOrDefault(f => f.Folder.Id == selectedId);
        if (again != null) SelectFolder(again);
        else if (view != null && folder == null) SelectUnified(view);
    }

    /// <summary>
    /// Scorciatoie di Missiva, quelle del nuovo Outlook. Arrivano anche dal riquadro di lettura: segnarle come gestite evita
    /// che le usi il browser (Ctrl+U vi aprirebbe l'origine della pagina, Ctrl+P la sua stampa).
    /// </summary>
    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var mods = Keyboard.Modifiers;
#if DEBUG
        mods |= DevTools.ForcedModifiers;
#endif
        const ModifierKeys ctrl = ModifierKeys.Control, ctrlShift = ModifierKeys.Control | ModifierKeys.Shift;
        if (e.Key == Key.Escape && foldersOver) { e.Handled = true; CloseFoldersOver(); }
        else if (e.Key == Key.F5) { e.Handled = true; await RefreshAllAsync(); }
        // Ctrl+N secondo la vista, come nel nuovo Outlook: nel calendario un evento, altrove un messaggio (prima sempre un
        // messaggio: audit, F-42). Da ovunque: Ctrl+Maiusc+A un evento, Ctrl+Maiusc+M un messaggio.
        else if (e.Key == Key.N && mods == ctrl) { e.Handled = true; if (calendarShown && calendar != null) calendar.NewEventWhenReady(); else OnNew(this, e); }
        else if (e.Key == Key.A && mods == ctrlShift) { e.Handled = true; NewEventShortcut(); }
        else if (e.Key == Key.M && mods == ctrlShift) { e.Handled = true; OnNew(this, e); }
        else if (e.Key == Key.R && mods == ctrl) { e.Handled = true; OnReply(this, e); }
        else if (e.Key == Key.R && mods == ctrlShift) { e.Handled = true; OnReplyAll(this, e); }
        else if (e.Key == Key.F && mods is ctrl or ctrlShift) { e.Handled = true; OnForward(this, e); }
        else if (e.Key == Key.V && mods == ctrlShift) { e.Handled = true; OnMoveMenu(this, e); }
        else if (e.Key == Key.E && mods == ctrl) { e.Handled = true; SearchBox.Focus(); }
        else if (e.Key == Key.OemPeriod && mods == ctrl) { e.Handled = true; SelectNext(1); }
        else if (e.Key == Key.OemComma && mods == ctrl) { e.Handled = true; SelectNext(-1); }
        else if (e.Key is Key.Q or Key.U && mods == ctrl)
        {
            e.Handled = true;
            if (Selected is { Count: > 0 } items) await SetReadAsync(items, e.Key == Key.Q);
        }
        else if (e.Key == Key.Insert && mods == ModifierKeys.None) { e.Handled = true; OnToggleFlag(this, e); }
        else if (e.Key == Key.P && mods == ctrl) { e.Handled = true; Print(); }
        else if (e.Key == Key.Z && mods == ctrl && lastMove != null) { e.Handled = true; await UndoAsync(); }
    }

    /// <summary>Ctrl+. e Ctrl+, come nel nuovo Outlook: il messaggio dopo o prima nell'elenco, che si apre nel riquadro.</summary>
    void SelectNext(int step)
    {
        if (MessageList.Items.Count == 0) return;
        int at = MessageList.SelectedIndex < 0 ? (step > 0 ? 0 : MessageList.Items.Count - 1)
            : Math.Clamp(MessageList.SelectedIndex + step, 0, MessageList.Items.Count - 1);
        MessageList.SelectedIndex = at;
        MessageList.ScrollIntoView(MessageList.SelectedItem);
    }

    /// <summary>Si stampa (o si salva in PDF) quello che il riquadro mostra: il messaggio, se è lui che si vede.</summary>
    bool CanPrint(MessageItem item, ReadingView? reader = null) =>
        reader != null ? reader.Selected?.Id == item.Summary.Id : S.ReadingPane != ReadingPanePosition.Off && Reader.Selected?.Id == item.Summary.Id;

    /// <summary>Stampa (Ctrl+P, menu del tasto destro) con l'anteprima di stampa di Edge, come il nuovo Outlook.</summary>
    void Print()
    {
        if (Selected is [var item] && CanPrint(item)) Reader.Print();
    }

    void OnMailto(IMailProvider from, Uri mailto, SendIdentity? identity)
    {
        var window = new ComposeWindow(Providers.ToList(), from, ComposeMode.New, null, null, sendAs: identity) { Owner = this };
        window.FillFromMailto(mailto.OriginalString);
        ShowCompose(window);
    }

    /// <summary>
    /// Un link mailto: da Windows, come nel nuovo Outlook quando è l'app di posta: all'avvio (Missiva.exe "mailto:...") o
    /// passato dalla seconda copia a questa (App.ListenForMailto). Nuovo messaggio dall'account aperto, in primo piano.
    /// </summary>
    public async Task OpenMailtoAsync(string link)
    {
        await accountsListed.Task;
        ShowFromTray(); // anche ridotta a icona o nell'area di notifica: altrimenti nasconderebbe la scrittura
        if (accounts.Count == 0)
        {
            Dialog.Show(this, "Nessun account", "Aggiungi un account di posta per scrivere il messaggio.");
            return;
        }
        var window = new ComposeWindow(Providers.ToList(), folder?.Account.Provider ?? accounts[0].Provider, ComposeMode.New, null, null) { Owner = this };
        window.FillFromMailto(link);
        ShowCompose(window);
        window.Activate();
    }

    async void OnAddAccount(object sender, RoutedEventArgs e)
    {
        var window = new AccountWindow { Owner = this };
        var added = window.ShowDialog() == true;
        if (added) await LoadAccountsAsync();
        // "Scrivi all'amministratore" (consenso di Microsoft 365): Nuovo messaggio con il link, che parte solo con Invia.
        else if (window.AdminMailto is { } mail) await OpenMailtoAsync(mail);
    }

    void OnAccountMenu(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccountItem account, ContextMenu: { } menu }) return;
        menu.Items.Clear();
        var mail = account.Provider.Account;
        // Una casella condivisa non ha accesso né nome suoi: li ha l'account di lavoro che la apre.
        if (!account.IsShared)
        {
            var manage = new MenuItem { Header = "Gestisci account…" };
            manage.Click += async (_, _) => await ManageAccountAsync(mail, this);
            menu.Items.Add(manage);
        }
        if (account.Provider.HasAutoReplies)
        {
            var replies = new MenuItem { Header = "Risposte automatiche…" };
            replies.Click += async (_, _) => await EditAutoRepliesAsync(mail, this);
            menu.Items.Add(replies);
        }
        // Caselle condivise solo per gli account di lavoro o della scuola, come il nuovo Outlook.
        if (mail.Kind == AccountKind.Microsoft && mail.Tenant != null && mail.SharedOf == null && only == null)
        {
            var addShared = new MenuItem { Header = "Aggiungi casella condivisa…" };
            addShared.Click += async (_, _) => await AddSharedMailboxAsync(mail);
            menu.Items.Add(addShared);
        }
        var remove = new MenuItem { Header = account.IsShared ? "Rimuovi casella condivisa" : "Rimuovi account" };
        remove.Click += async (_, _) => await RemoveAccountAsync(account.Provider.Account);
        menu.Items.Add(remove);
        if (only != null || accounts.Count < 2) return;
        int index = accounts.IndexOf(account);
        menu.Items.Add(new Separator());
        var up = new MenuItem { Header = "Sposta su", IsEnabled = index > 0 };
        up.Click += (_, _) => MoveAccount(account, index - 1);
        menu.Items.Add(up);
        var down = new MenuItem { Header = "Sposta giù", IsEnabled = index < accounts.Count - 1 };
        down.Click += (_, _) => MoveAccount(account, index + 1);
        menu.Items.Add(down);
    }

    // ---------------------------------------------------------------- ordine degli account

    /// <summary>
    /// L'account va al posto index nella barra laterale. L'ordine è quello di Settings.Accounts, salvato: vale anche per
    /// le viste di tutti gli account e per la Posta in arrivo aperta all'avvio (quella del primo).
    /// </summary>
    void MoveAccount(AccountItem account, int index)
    {
        int from = accounts.IndexOf(account);
        index = Math.Clamp(index, 0, accounts.Count - 1);
        if (from < 0 || from == index) return;
        accounts.Move(from, index);
        var order = accounts.Select(a => a.Provider.Account.Id).ToList();
        int Position(MailAccount a) => order.IndexOf(a.Id) is var i and >= 0 ? i : int.MaxValue;
        var sorted = S.Accounts.OrderBy(Position).ToList(); // OrderBy è stabile, List.Sort no
        S.Accounts.Clear();
        S.Accounts.AddRange(sorted);
        Store.Save();
        App.Current.SetProviders(Providers.ToList());
        UpdateUnified();
        Log.Write($"account riordinati: {string.Join(", ", S.Accounts.Select(a => a.Email))}");
    }

    AccountItem? accountDragged;
    Point? accountDragStart;

    void OnAccountMouseDown(object sender, MouseButtonEventArgs e)
    {
        accountDragged = (sender as FrameworkElement)?.Tag as AccountItem;
        accountDragStart = e.GetPosition(this);
    }

    /// <summary>Oltre la soglia di trascinamento di Windows l'intestazione dell'account parte, come le cartelle.</summary>
    void OnAccountMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || accountDragged is not { } dragged || accountDragStart is not { } start || accounts.Count < 2) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        accountDragged = null;
        accountDragStart = null;
        var header = (Button)sender;
        header.ReleaseMouseCapture(); // il pulsante non resta premuto: al rilascio niente clic che chiude l'account
        DragDrop.DoDragDrop(header, new DataObject(typeof(AccountItem), dragged), DragDropEffects.Move);
        foreach (var a in accounts) a.DropSide = 0;
    }

    void OnAccountDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(AccountItem)) is not AccountItem dragged || sender is not FrameworkElement { Tag: AccountItem target } header) return;
        foreach (var a in accounts.Where(a => a != target)) a.DropSide = 0;
        if (target == dragged)
        {
            target.DropSide = 0;
            return;
        }
        target.DropSide = e.GetPosition(header).Y > header.ActualHeight / 2 ? 1 : -1;
        e.Effects = DragDropEffects.Move;
    }

    void OnAccountDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem target }) target.DropSide = 0;
    }

    void OnAccountDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(AccountItem)) is not AccountItem dragged || sender is not FrameworkElement { Tag: AccountItem target }) return;
        int side = target.DropSide;
        foreach (var a in accounts) a.DropSide = 0;
        if (target == dragged || side == 0) return;
        int to = accounts.IndexOf(target) + (side > 0 ? 1 : 0);
        if (accounts.IndexOf(dragged) < to) to--;
        MoveAccount(dragged, to);
    }

    public async Task RemoveAccountAsync(MailAccount account)
    {
        var shared = S.Accounts.Where(a => a.SharedOf == account.Id).ToList();
        var text = account.SharedOf != null ? "La casella condivisa esce da Missiva. La posta resta dov'è: sul server non si cancella nulla."
            : shared.Count > 0 ? $"L'account esce da Missiva con le sue caselle condivise ({string.Join(", ", shared.Select(s => s.Email))}). La posta resta dov'è: sul server non si cancella nulla."
            : "L'account esce da Missiva. La posta resta dov'è: sul server non si cancella nulla.";
        // Quello che Missiva faceva per l'account e non farà più: detto prima, perché non resti in attesa senza saperlo.
        var gone = shared.Select(s => s.Id).Append(account.Id).ToHashSet();
        int scheduled = S.ScheduledSends.Count(s => gone.Contains(s.AccountId)), snoozed = S.Snoozed.Count(s => gone.Contains(s.AccountId));
        if (scheduled > 0) text += scheduled == 1 ? " Un invio pianificato non partirà: il messaggio resta nelle Bozze." : $" {scheduled} invii pianificati non partiranno: i messaggi restano nelle Bozze.";
        if (snoozed > 0) text += snoozed == 1 ? $" Un messaggio posposto non tornerà: resta nella cartella {SnoozeFolderName}." : $" {snoozed} messaggi posposti non torneranno: restano nella cartella {SnoozeFolderName}.";
        text += OutboxRemovalText(gone.Sum(Outbox.Count));
        if (Dialog.Ask(this, $"Rimuovere {account.Email}?", text, "Rimuovi") != Choice.Primary) return;
        // I messaggi inviati senza rete da questi account non partiranno più: le loro finestre si riaprono, con il testo da
        // salvare altrove, e la barra della Posta in uscita non li promette più (audit dell'11 ottobre, C-54).
        foreach (var waiting in OutboxWindows().Where(w => w.OutboxAccountId is { } id && gone.Contains(id)))
            waiting.LeaveOutbox("Non inviato: l'account è stato rimosso da Missiva. Copia il testo, se ti serve.");
        // Una casella condivisa non ha un accesso suo: quello dell'account di lavoro resta.
        var revocation = GoogleRevocation.NotNeeded;
        if (account.Kind == AccountKind.Microsoft && account.SharedOf == null) await MicrosoftAuth.SignOutAsync(account);
        else if (account.Kind == AccountKind.Gmail) revocation = await GoogleAuth.SignOutAsync(account);
        // IMAP: la password cifrata esce con l'account, dalle impostazioni; con lui contrassegni, modelli, posposti, invii
        // pianificati, firme e il resto (Settings.ForgetAccount).
        foreach (var removed in shared.Prepend(account))
        {
            MailCache.Delete(removed);
            Calendars.Delete(removed);
            ContactBook.ForgetRemote(removed.Id);
            ActionQueue.Forget(removed.Id); // le azioni fatte senza rete non partono più
            Outbox.Forget(removed.Id); // né i messaggi della Posta in uscita (le finestre sono già riaperte, sopra)
            S.Accounts.RemoveAll(a => a.Id == removed.Id);
            S.ForgetAccount(removed.Id);
        }
        Store.Save();
        rows.Clear();
        Reader.Clear();
        ListTitle.Text = "";
        await LoadAccountsAsync();
        // A rimozione fatta: se Google non ha confermato la revoca lo si dice, con dove toglierla a mano. Prima era solo
        // una riga del registro (scelte del 12 ottobre, C-32).
        if (GoogleAuth.RevocationNotice(account, revocation) is var (heading, body)) Dialog.Show(this, heading, body);
    }

    /// <summary>Nella domanda di Rimuovi account: i messaggi della Posta in uscita che non partiranno (vuoto se non ce ne sono).</summary>
    internal static string OutboxRemovalText(int waiting) => waiting switch
    {
        <= 0 => "",
        1 => " Un messaggio nella Posta in uscita non partirà: la sua finestra si riapre.",
        _ => $" {waiting} messaggi nella Posta in uscita non partiranno: le loro finestre si riaprono.",
    };

    /// <summary>
    /// La rotellina in fondo alla colonna delle app apre subito le Impostazioni, come in Outlook (ritocchi del 16 ottobre,
    /// richiesta dell'autore: "non deve comparire aggiungi account, è ridondante"). Prima apriva un menu con Impostazioni,
    /// Aggiungi account e Apri la cartella dei registri: Aggiungi account resta in fondo alle cartelle e in Impostazioni,
    /// Account; la cartella dei registri è un pulsante di Impostazioni, Generali, Informazioni.
    /// </summary>
    void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();
}
