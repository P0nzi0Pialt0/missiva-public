using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Missiva.UI;

/// <summary>
/// Riquadro di lettura: la conversazione intera, con il messaggio più recente in alto e gli altri compressi
/// (un clic sull'intestazione li apre), come nel nuovo Outlook. Il contenuto lo impagina WebView2, ma confinato:
/// JavaScript spento, nessuna richiesta verso Internet tranne le immagini dei mittenti consentiti, nessun download,
/// link aperti nel browser dopo il controllo antiphishing. Oggetto e avviso delle immagini sono nella pagina, perché il
/// riquadro cambi una volta sola quando si apre un messaggio (ShowAsync).
/// </summary>
public partial class ReadingView : UserControl
{
    sealed class Entry(MessageSummary summary)
    {
        public MessageSummary Summary { get; } = summary;
        public MessageBody? Body { get; set; }
        public bool Expanded { get; set; }
        public bool Loading { get; set; }
        public string? Error { get; set; }
        /// <summary>Segnato letto o da leggere a mano (Ctrl+Q, Ctrl+U, busta): le letture rimandate non lo toccano più.</summary>
        public bool ReadByHand { get; set; }
    }

    /// <summary>I riquadri aperti (finestra principale, finestre dei messaggi e di una sola cartella), per ForgetPendingRead.</summary>
    static readonly List<ReadingView> open = new();

    static Task<CoreWebView2Environment>? environment;

    /// <summary>Il provider con cui il messaggio è stato aperto (ShowAsync).</summary>
    IMailProvider? opener;
    /// <summary>
    /// Il provider del messaggio aperto, quello di adesso. Aggiungendo o rimuovendo un account, o aggiungendo una casella
    /// condivisa, Missiva chiude tutti i provider e ne crea di nuovi: un messaggio rimasto aperto in una finestra sua teneva
    /// quello chiuso, e ogni sua azione dava l'errore che dice di chiudere e riaprire. Ora passa a quello nuovo dello stesso
    /// account, per la stessa strada della scrittura (ComposeWindow.Current); se l'account è stato rimosso resta quello
    /// chiuso, che rifiuta dicendo perché (scelte del 12 ottobre, X-1).
    /// </summary>
    IMailProvider? provider => opener is { } known ? ComposeWindow.Current(known) : null;
    /// <summary>La Posta indesiderata dell'account del messaggio aperto (ShowAsync).</summary>
    string? junk;
    /// <summary>
    /// I messaggi della Posta indesiderata per cui si è premuto "Mostra immagini" (OnceKey): vale finché resta aperta questa
    /// conversazione, e non si ricorda il mittente (audit, F-12).
    /// </summary>
    readonly HashSet<string> imagesOnce = new();
    /// <summary>
    /// "Annulla iscrizione" con un clic già chiesto in questa sessione (OnceKey): il testo che prende il posto del comando,
    /// in corso o inviata, perché la richiesta non parta due volte. Di tutti i riquadri: lo stesso messaggio in un'altra finestra.
    /// </summary>
    static readonly Dictionary<string, string> unsubscribed = new();
    List<Entry> entries = new();
    int version, pageNumber;
    bool? lightOverride;
    /// <summary>Messaggio da tenere in vista quando la pagina si ridisegna (quello appena aperto o chiuso).</summary>
    int? anchor;
    ReadingPage page = ReadingPage.Build([], true);
    Task? webReady;

    /// <summary>
    /// WebView2, creato alla prima necessità (Prepare, primo messaggio) invece che con la finestra: le sue librerie non
    /// rallentano l'avvio, e con Missiva nell'area di notifica si chiude davvero con i suoi processi (ReleaseWeb) e si
    /// ricrea alla riapertura.
    /// </summary>
    Microsoft.Web.WebView2.Wpf.WebView2? web;

    Microsoft.Web.WebView2.Wpf.WebView2 Web => web ?? CreateWeb();

    Microsoft.Web.WebView2.Wpf.WebView2 CreateWeb()
    {
        web = new Microsoft.Web.WebView2.Wpf.WebView2
        {
            Visibility = PreviewHost.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible, // sotto un'anteprima aperta
        };
        web.PreviewKeyDown += OnWebKey;
        WebSlot.Children.Insert(0, web);
        return web;
    }

    /// <summary>
    /// Missiva va nell'area di notifica: WebView2 si chiude con la pagina e i suoi processi, e con l'ultimo riquadro chiuso
    /// anche il processo del browser; l'ambiente condiviso non vale più. Alla riapertura Prepare lo ricrea.
    /// </summary>
    public void ReleaseWeb()
    {
        Clear();
        // Senza WebView2 il riquadro vuoto è di nuovo quello di WPF, finché Prepare non rifà la pagina del segnaposto.
        ContentPanel.Visibility = Visibility.Hidden;
        EmptyPanel.Visibility = Visibility.Visible;
        if (web == null) return;
        WebSlot.Children.Remove(web);
        web.Dispose();
        web = null;
        webReady = null;
        servedPage = shownPage = actionsPage = null;
        pageNumber = 0;
        blankNext = false;
        if (open.All(v => v.web == null)) environment = null;
    }

    public ReadingView()
    {
        InitializeComponent();
        App.ThemeChanged += OnThemeChanged;
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is not { } window || window == host) return;
            if (host != null) host.StateChanged -= OnHostState;
            host = window;
            host.StateChanged += OnHostState;
            if (!open.Contains(this)) open.Add(this);
            // Non con Unloaded: al cambio di tema WPF rifà il modello della finestra e il riquadro esce e rientra.
            host.Closed += (_, _) =>
            {
                App.ThemeChanged -= OnThemeChanged; // la finestra di un messaggio, chiusa, non resta in memoria
                open.Remove(this);
                Leave();
            };
        };
    }

    /// <summary>
    /// La finestra si chiude: il messaggio si lascia come cambiando selezione. Con "quando la selezione cambia" diventa
    /// letto (la finestra di un messaggio non cambia mai selezione), il conto alla rovescia di "dopo N secondi" si ferma.
    /// </summary>
    void Leave()
    {
        FlushReadOnLeave();
        version++;
        opening.Cancel();
    }

    /// <summary>
    /// Letto o da leggere segnato a mano (Ctrl+Q, Ctrl+U, la busta della riga): vince sulle letture rimandate di Gestione
    /// messaggi in tutti i riquadri aperti, anche se il messaggio era già così (Ctrl+U su un messaggio ancora da leggere
    /// lo lascia da leggere anche quando si cambia selezione o scade il tempo).
    /// </summary>
    public static void ForgetPendingRead(IEnumerable<MessageSummary> messages)
    {
        var keys = messages.Select(m => (m.FolderId, m.Id)).ToHashSet();
        foreach (var view in open)
        {
            view.readOnLeave.RemoveAll(r => keys.Contains((r.Entry.Summary.FolderId, r.Entry.Summary.Id)));
            foreach (var entry in view.entries.Where(e => keys.Contains((e.Summary.FolderId, e.Summary.Id)))) entry.ReadByHand = true;
        }
    }

    /// <summary>
    /// Il tema di Windows è cambiato con il riquadro aperto (anche da solo, con il cambio automatico all'alba e al
    /// tramonto): come nel nuovo Outlook il messaggio si ridisegna subito nel tema nuovo, restando dove si era. Il sole
    /// della pagina valeva per il tema di prima.
    /// </summary>
    void OnThemeChanged()
    {
        opened = Stopwatch.StartNew();
        clock = "tema";
        lightOverride = null;
        if (entries.Count > 0) _ = RenderWarmAsync();
        else ShowBlank();
    }

    Window? host;

    /// <summary>Con la finestra ridotta a icona WebView2 libera tutta la memoria che può; tornando, riprende il normale.</summary>
    void OnHostState(object? sender, EventArgs e)
    {
        if (web?.CoreWebView2 is { } core && host != null)
            core.MemoryUsageTargetLevel = host.WindowState == WindowState.Minimized
                ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
    }

    /// <summary>Qualcuno chiede di rispondere o inoltrare un messaggio della conversazione.</summary>
    public event Action<IMailProvider, MessageSummary, ComposeMode, MessageBody?>? ReplyRequested;
    /// <summary>Un messaggio della conversazione è stato letto aprendolo (o lasciandolo, secondo Gestione messaggi).</summary>
    public event Action<IMailProvider, MessageSummary>? MessageRead;
    /// <summary>
    /// Link mailto: nel messaggio: si scrive con Missiva, non con il programma di posta predefinito. Il terzo argomento è
    /// l'altro indirizzo dell'account da cui scrivere (Annulla iscrizione di una lista arrivata a un alias), o null.
    /// </summary>
    public event Action<IMailProvider, Uri, SendIdentity?>? MailtoRequested;
    /// <summary>Modifica su una bozza: si continua nella finestra di scrittura.</summary>
    public event Action<IMailProvider, MessageSummary, MessageBody?>? EditRequested;
    /// <summary>Altre azioni ("...") nell'intestazione di un messaggio: il menu del messaggio, vicino al puntatore.</summary>
    public event Action<IMailProvider, MessageSummary>? MoreRequested;
    /// <summary>Clic sul mittente nell'intestazione di un messaggio: la scheda della persona.</summary>
    public event Action<IMailProvider, Address>? PersonRequested;
    /// <summary>Accetta, Provvisorio, Rifiuta o Proponi un nuovo orario sulla scheda di un invito.</summary>
    public event Action<IMailProvider, MessageSummary, Invitation, InviteAnswer>? InviteRequested;
    /// <summary>
    /// Un allegato .eml da aprire in una finestra di Missiva: il provider del messaggio che lo contiene, i byte, il nome, e
    /// se quel messaggio sta nella Posta indesiderata (allora l'allegato si apre come se ci stesse anche lui: EmlJunk).
    /// </summary>
    public event Action<IMailProvider, byte[], string, bool>? EmlRequested;

    /// <summary>
    /// La "Posta indesiderata" con cui si apre un .eml (ShowAsync): la sua stessa cartella finta se era allegato a un
    /// messaggio della Posta indesiderata, così per lui vale tutto quello che vale lì (le immagini mai da sole, niente
    /// "Annulla iscrizione", niente rubrica); altrimenti nessuna. Prima un .eml si apriva sempre come posta normale, e le
    /// immagini di un mittente consentito scritto nel suo From partivano (audit dell'11 ottobre, T-02). Le immagini non
    /// si caricano da sole nemmeno in un .eml allegato a un messaggio normale (IsEml), che per il resto è posta normale.
    /// </summary>
    internal static string? EmlJunk(EmlProvider eml, bool fromJunk) => fromJunk ? eml.Summary.FolderId : null;

    /// <summary>
    /// Il messaggio aperto è un .eml (un allegato): il suo From e le sue intestazioni li ha scritti chi l'ha mandato, e
    /// nessun server li ha verificati. Per le immagini vale come dove Missiva non si fida di chi scrive (ImagesOnce).
    /// </summary>
    bool IsEml => provider is EmlProvider;

    public MessageSummary? Selected { get; private set; }

    /// <summary>L'elenco mescola più account (Tutti gli account, ricerca in tutti gli account): la pagina dice di quale è il messaggio.</summary>
    public bool ShowAccount { get; set; }
    public MessageBody? SelectedBody => entries.FirstOrDefault(e => e.Summary == Selected)?.Body;

#if DEBUG
    /// <summary>Prova del cambio di tema senza toccare Windows (--theme-flip): il tema che Missiva crede di Windows.</summary>
    public static bool? TestDark;
#endif

    /// <summary>
    /// Il tema di Missiva è scuro: quello scelto in Impostazioni, Generali, Aspetto, o con "Come Windows" il tema delle app di
    /// Windows (Impostazioni, Personalizzazione, Colori).
    /// </summary>
    public static bool Dark
    {
        get
        {
            int known = darkKnown;
            if (known == 0) darkKnown = known = ReadDark() ? 2 : 1;
            return known == 2;
        }
    }

    /// <summary>
    /// Il tema letto una volta e tenuto (0 da leggere, 1 chiaro, 2 scuro): ogni riga dell'elenco lo chiede per il colore di
    /// "[Bozza]" e il registro costava il 7% dello scorrimento. Si rilegge solo in `App.ApplyTheme` (avvio, scelta in
    /// Impostazioni, cambio del tema di Windows con Missiva aperta, prova --theme-flip).
    /// </summary>
    static volatile int darkKnown;

    public static void ResetDark() => darkKnown = 0;

    static bool ReadDark()
    {
#if DEBUG
        if (TestDark is { } test) return test;
#endif
        if (Store.Settings.Theme != AppTheme.System) return Store.Settings.Theme == AppTheme.Dark;
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    bool Light => lightOverride ?? (Store.Settings.LightReadingPane || !Dark);

    // ---------------------------------------------------------------- apertura

    /// <summary>
    /// Quanto il riquadro tiene il messaggio di prima aspettando il corpo del nuovo: se arriva entro questo tempo (quasi
    /// sempre: Outlook.com 0,1 s, Gmail 0,2-0,3 s) il riquadro cambia una volta sola, come nel nuovo Outlook, invece di
    /// mostrare prima l'intestazione con un segnaposto e poi il corpo. Oggetto e avviso delle immagini sono nella pagina
    /// (ReadingPage), così cambiano insieme al corpo: nativi, WPF li disegnava 30-50 ms prima che WebView2 avesse la pagina.
    /// Era 300 ms: con i corpi non ancora sul PC (Gmail 0,4-0,8 s) il segnaposto compariva per 130-400 ms e poi la mail, un
    /// lampo che l'autore vedeva "ogni tanto" (misurato con lampi.ps1, 28 settembre 2026). Ora un secondo, e intanto dopo
    /// FadeDelay il messaggio di prima si sbiadisce (classe "leaving"): si vede subito che il clic è arrivato, senza
    /// pagine di mezzo.
    /// </summary>
    static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(1000);
    static readonly TimeSpan FadeDelay = TimeSpan.FromMilliseconds(250);
    /// <summary>Un messaggio aperto dentro la conversazione mostrata: il segnaposto dopo 300 ms, come prima.</summary>
    static readonly TimeSpan ExpandDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Quanto si aspetta a chiedere corpo e conversazione quando il messaggio di prima non è ancora arrivato (frecce o
    /// Ctrl+. tenuti premuti): si chiedono solo per il messaggio su cui ci si ferma, non per ognuno di passaggio.
    /// </summary>
    static readonly TimeSpan HurryDelay = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// Annullato dall'apertura successiva: le richieste del messaggio di prima non ancora partite non partono (Gmail le
    /// metteva in coda sulla connessione, dieci Ctrl+. erano 2-3 s di attesa sull'ultimo) e quelle di Graph si interrompono.
    /// </summary>
    CancellationTokenSource opening = new();
    /// <summary>L'ultima apertura arrivata al corpo: se è più vecchia di quella in corso, si scorre più veloce dei messaggi.</summary>
    int arrived;

    /// <param name="junkFolderId">La Posta indesiderata dell'account: i destinatari dei suoi messaggi non vanno nella rubrica.</param>
    /// <param name="bodyShown">Il corpo si vede (non se nel frattempo si è aperto altro): per scaricare in anticipo i vicini.</param>
    public async Task ShowAsync(IMailProvider p, MessageSummary selected, bool wholeConversation, string? junkFolderId = null, Action? bodyShown = null)
    {
        bool hurried = entries.Count > 0 && arrived != version;
        FlushReadOnLeave();
        ClosePreview();
        var v = ++version;
        opening.Cancel();
        opening = new CancellationTokenSource();
        var cancel = opening.Token;
        opened = System.Diagnostics.Stopwatch.StartNew();
        clock = "clic";
        opener = p;
        junk = junkFolderId;
        Selected = selected;
        if (!PendingFor(p, selected)) pendingPreview = null; // l'anteprima chiesta per un altro messaggio non vale per questo
        previewBytes.Clear();
        imagesOnce.Clear();
        lightOverride = null;
        anchor = null;
        actionsPage = null; // i pulsanti della pagina di prima non valgono più per i nuovi messaggi
        var current = new Entry(selected) { Expanded = true };
        entries = new List<Entry> { current };
        if (hurried)
        {
            await Task.Delay(HurryDelay);
            if (v != version) return;
            Log.Time("clic, attesa dello scorrimento veloce", opened);
        }
        var body = FetchBodyAsync(current, cancel);
        bool whole = wholeConversation && selected.ConversationId.Length > 0;
        var conversation = whole ? ConversationAsync(p, selected, cancel) : Task.FromResult<List<MessageSummary>?>(null);
        // Una conversazione già vista nella sessione si mostra subito intera, com'era: quella del server, se è la stessa,
        // non rifà la pagina.
        var seen = whole ? SeenConversation(p, selected) : null;
        // Solo il corpo: la conversazione in Gmail arriva spesso dopo (0,2-0,5 s) e aspettarla ritarderebbe il messaggio.
        await Task.WhenAny(body, Task.Delay(FadeDelay));
        if (v != version) return;
        if (!body.IsCompleted)
        {
            await Script("document.documentElement.classList.add('leaving')"); // il messaggio di prima si sbiadisce
            await Task.WhenAny(body, Task.Delay(ShowDelay - FadeDelay));
            if (v != version) return;
        }

        bool conversationDone = conversation.IsCompleted;
        var shown = conversationDone ? conversation.Result : seen;
        if (conversationDone) Remember(p, selected, conversation.Result);
        if (!Merge(shown, selected, current)) shown = null;
        Render(keepScroll: false); // con il corpo, o con intestazione e segnaposto se tarda
        if (!body.IsCompleted)
        {
            await body;
            if (v != version) return;
            Render(keepScroll: false); // uno script per lo scorrimento aspetterebbe la pagina del segnaposto
        }
        arrived = v;
        MarkRead(current);
        bodyShown?.Invoke();
        _ = TryPendingPreviewAsync();

        if (!conversationDone)
        {
            var list = await conversation;
            if (v != version) return;
            Remember(p, selected, list);
            if (shown != null && SameMessages(list, shown))
            {
                Log.Time("clic, conversazione arrivata, la stessa già mostrata", opened);
            }
            else if (Merge(list, selected, current))
            {
                // Il messaggio aperto resta dove si vedeva, anche se sopra arrivano quelli più recenti.
                var k = entries.IndexOf(current);
                anchor = k > 0 ? k : null;
                Render();
            }
            else if (shown != null)
            {
                entries = new List<Entry> { current }; // quella vista prima non c'è più: il messaggio solo
                Render();
            }
            else return;
        }
        await FillPreviewsAsync(p, v);
    }

    /// <summary>Le conversazioni già arrivate nella sessione (account e conversazione), le ultime ConversationsKept.</summary>
    static readonly Dictionary<string, List<MessageSummary>> conversations = new();
    static readonly Queue<string> conversationOrder = new();
    const int ConversationsKept = 50;

    static string ConversationKey(IMailProvider p, MessageSummary m) => p.Account.Id + "|" + m.ConversationId;

    /// <summary>La conversazione del messaggio già arrivata prima, se c'è ancora il messaggio (e non è rimasto solo).</summary>
    static List<MessageSummary>? SeenConversation(IMailProvider p, MessageSummary selected) =>
        conversations.TryGetValue(ConversationKey(p, selected), out var list) && list.Count > 1
        && list.Any(m => m.Id == selected.Id || (m.Date == selected.Date && m.From.Email == selected.From.Email)) ? list : null;

    static void Remember(IMailProvider p, MessageSummary selected, List<MessageSummary>? list)
    {
        if (list == null) return;
        var key = ConversationKey(p, selected);
        if (!conversations.ContainsKey(key)) conversationOrder.Enqueue(key);
        conversations[key] = list;
        while (conversationOrder.Count > ConversationsKept) conversations.Remove(conversationOrder.Dequeue());
    }

    /// <summary>Gli stessi messaggi (cartella e identificativo), cioè la stessa pagina: niente da ridisegnare.</summary>
    static bool SameMessages(List<MessageSummary>? a, List<MessageSummary> b) =>
        a != null && a.Count == b.Count && a.Select(m => (m.FolderId, m.Id)).ToHashSet().SetEquals(b.Select(m => (m.FolderId, m.Id)));

    async Task<List<MessageSummary>?> ConversationAsync(IMailProvider p, MessageSummary selected, CancellationToken cancel)
    {
        try
        {
            var conversation = await p.GetConversationAsync(selected, cancel);
            Log.Time($"clic, conversazione arrivata ({conversation.Count})", opened);
            return conversation;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return null; // si è aperto un altro messaggio
        }
        catch (Exception ex)
        {
            Log.Error("conversazione", ex); // resta il messaggio singolo
            return null;
        }
    }

    /// <summary>La conversazione, dal più recente, al posto del messaggio solo; false se c'è solo quello.</summary>
    bool Merge(List<MessageSummary>? conversation, MessageSummary selected, Entry current)
    {
        if (conversation == null || conversation.Count <= 1) return false;
        entries = conversation
            .OrderByDescending(m => m.Date)
            .Select(m => m.Id == selected.Id || (m.Date == selected.Date && m.From.Email == selected.From.Email) ? current : new Entry(m))
            .ToList();
        if (!entries.Contains(current)) entries.Insert(0, current);
        return true;
    }

    /// <summary>
    /// Anteprime dei messaggi compressi della conversazione: Gmail non le manda con la conversazione (costano), quindi
    /// arrivano dopo, sulla connessione in sottofondo. Le scrive nella pagina mostrata uno script di Missiva, senza
    /// ridisegnarla (prima era una navigazione in più, a 0,7-0,9 s dal clic, che si vedeva).
    /// </summary>
    async Task FillPreviewsAsync(IMailProvider p, int v)
    {
        var missing = entries.Where(e => !e.Expanded && e.Summary.Preview.Length == 0).Select(e => e.Summary).ToList();
        if (missing.Count == 0) return;
        try
        {
            foreach (var inFolder in missing.GroupBy(m => m.FolderId))
            {
                var previews = await p.GetPreviewsAsync(new MailFolder { Id = inFolder.Key, Name = "" }, inFolder.ToList());
                foreach (var m in inFolder)
                    if (previews.TryGetValue(m.Id, out var text)) m.Preview = text;
            }
            if (v != version) return;
            if (shownPage != page)
            {
                Render(); // una pagina nuova sta arrivando, forse senza le anteprime: meglio ridisegnare
                return;
            }
            var script = new StringBuilder();
            for (int k = 0; k < entries.Count; k++)
                if (!entries[k].Expanded && missing.Contains(entries[k].Summary) && entries[k].Summary.Preview.Length > 0)
                    script.Append($"document.querySelectorAll('#m{k} .preview').forEach(e => e.textContent = {JsonSerializer.Serialize(entries[k].Summary.Preview)});");
            if (script.Length > 0) await Script(script.ToString());
        }
        catch (Exception ex)
        {
            Log.Error("anteprime della conversazione", ex);
        }
    }

    /// <summary>
    /// Prepara in anticipo quello che il primo messaggio altrimenti pagava tutto al clic (0,6-1,2 s invece di 0,2-0,6):
    /// WebView2 con i suoi processi, il codice che pulisce l'HTML (compilato la prima volta che serve, in sottofondo) e
    /// il motore della pagina, con un messaggio finto che resta nascosto.
    /// </summary>
    public async void Prepare()
    {
        if (webReady != null) return;
        var clock = Stopwatch.StartNew();
        Log.Startup("WebView2: preparazione avviata");
        var ready = webReady = InitWebAsync();
        try
        {
            const string sample = "<div style=\"color:#202020;background:#ffffff\"><p>Missiva</p><table><tr><td>" +
                                  "<a href=\"https://example.com\">link</a><img src=\"cid:logo\"></td></tr></table></div>";
            await Task.Run(() => MailHtml.Prepare(MailHtml.WarmUpSample, true, false, _ => null));
            await ready;
            Log.Time("WebView2 pronto in anticipo", clock);
            Log.Startup("WebView2 pronto");
            if (webReady != ready) return; // intanto chiuso (Missiva nell'area di notifica)
            if (entries.Count > 0 || pageNumber > 0) return; // intanto è già arrivato un messaggio
            bool light = Light;
            var summary = new MessageSummary { Id = "", FolderId = "", From = new Address { Name = "Missiva", Email = "missiva@missiva.example" } };
            page = ReadingPage.Build([new PageEntry(summary, new MessageBody { Html = sample }, true, false, null, false)], light);
            var paper = light ? Colors.White : MailHtml.DarkPaper;
            Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(paper.R, paper.G, paper.B);
            blankNext = true; // poi la pagina vuota, che si vedrà per un attimo al primo messaggio
            Web.CoreWebView2.Navigate($"{ReadingPage.Origin}/?v={++pageNumber}");
        }
        catch (Exception) when (webReady != ready)
        {
            Log.Write("WebView2 chiuso mentre si preparava in anticipo (finestra nell'area di notifica)");
        }
        catch (Exception ex)
        {
            Log.Error("WebView2 in anticipo", ex); // al primo messaggio Render mostra l'avviso
        }
    }

    public void Clear()
    {
        FlushReadOnLeave();
        ClosePreview();
        version++;
        opening.Cancel();
        entries.Clear();
        Selected = null;
        pendingPreview = null;
        previewBytes.Clear();
        imagesOnce.Clear();
        actionsPage = null;
        opened.Reset(); // il segnaposto non è l'apertura di un messaggio: niente "testo pronto" nel registro
        // Con WebView2 pronto il riquadro vuoto è la pagina del segnaposto (ShowBlank) e WebView2 resta visibile: Chromium
        // tiene il messaggio finché non ha disegnato il segnaposto, e il messaggio dopo finché non l'ha disegnato. Prima
        // WebView2 si nascondeva e, tornando visibile, mostrava per 55-60 ms il foglio bianco della pagina vuota.
        if (ContentPanel.Visibility != Visibility.Visible) EmptyPanel.Visibility = Visibility.Visible;
        ShowBlank();
    }

    bool blankNext;
    /// <summary>La navigazione della pagina del segnaposto di ShowBlank (numero della pagina e poi identificativo di WebView2).</summary>
    int blankNumber;
    ulong? blankNavigation;

    /// <summary>
    /// Il riquadro vuoto nella pagina (ReadingPage.Empty): la busta e "Seleziona un elemento da leggere" come il segnaposto
    /// di WPF (EmptyPanel), che resta solo finché WebView2 non c'è. Quello di WPF prende carattere e colori dal tema: si
    /// leggono da lui, e i colori semitrasparenti di Fluent si posano sul fondo della pagina.
    /// </summary>
    void ShowBlank()
    {
        if (web?.CoreWebView2 == null) return;
        // Il riquadro vuoto segue il tema dell'app, non "Riquadro di lettura chiaro": quello vale per i messaggi. Con il
        // riquadro chiaro nel tema scuro il segnaposto nel foglio bianco aveva i colori del tema scuro, quasi bianchi, e non
        // si vedeva niente: solo un rettangolo bianco all'apertura (l'autore, 28 settembre 2026).
        bool light = !Dark;
        var paper = light ? Colors.White : MailHtml.DarkPaper;
        ApplyZoom();
        // WPF posa il testo semitrasparente in luce lineare, non come Chromium: con lo stesso conto la busta e la scritta hanno
        // i colori che si vedevano (misurati: #C9C7C5 e #A9A7A6 nel chiaro, #D1D1D1 e #EAEAEA nello scuro; mescolati come
        // fa Chromium erano #8D8D8D e #616161, molto più scuri).
        static double Linear(byte v) { var c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        static byte Encoded(double l) => (byte)Math.Round(Math.Clamp(l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055, 0, 1) * 255);
        string On(string key)
        {
            var brush = Application.Current.TryFindResource(key) as SolidColorBrush;
            var c = brush?.Color ?? Colors.Gray;
            double a = c.A / 255.0 * (brush?.Opacity ?? 1);
            byte Mix(byte fore, byte back) => Encoded(a * Linear(fore) + (1 - a) * Linear(back));
            return $"#{Mix(c.R, paper.R):X2}{Mix(c.G, paper.G):X2}{Mix(c.B, paper.B):X2}";
        }
        var label = EmptyText;
        var fonts = string.Join(",", label.FontFamily.Source.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(f => $"'{f}'"));
        // Il centro del riquadro sta sotto quello della pagina di metà della differenza fra i margini sopra e sotto (Paper).
        var margin = FullPaper ? Paper.Padding : Paper.Margin;
        page = ReadingPage.Empty(light, new EmptyLook(fonts + ",'Segoe UI',sans-serif", label.FontSize,
            On("TextFillColorTertiaryBrush"), On("TextFillColorSecondaryBrush"), web.ZoomFactor, (margin.Bottom - margin.Top) / 2));
        // Il fondo intorno a WebView2 cambia quando il segnaposto è disegnato (NavigationCompleted), non subito: finché
        // Chromium tiene il messaggio (bianco con il riquadro chiaro) una cornice scura intorno si vedrebbe.
        blankPaper = new SolidColorBrush(paper);
        if (ContentPanel.Visibility != Visibility.Visible) Paper.Background = blankPaper;
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(paper.R, paper.G, paper.B);
        blankNumber = ++pageNumber;
        blankNavigation = null;
        Web.CoreWebView2.Navigate($"{ReadingPage.Origin}/?v={blankNumber}");
    }

    /// <summary>Il fondo del riquadro vuoto dell'ultimo ShowBlank, da posare quando la sua pagina è disegnata.</summary>
    Brush? blankPaper;

    /// <summary>
    /// Il riquadro della finestra principale: il fondo della pagina riempie tutta la scheda (i margini attorno a WebView2
    /// diventano spazio interno), così il segnaposto nella pagina è sullo stesso fondo del resto del riquadro e passare dal
    /// messaggio al riquadro vuoto non cambia nulla fuori da WebView2. Nelle finestre dei messaggi resta il foglio con i margini.
    /// </summary>
    public bool FullPaper
    {
        get => Paper.Padding != default;
        set
        {
            if (!value) return;
            Paper.Padding = Paper.Margin;
            Paper.Margin = default;
            Paper.CornerRadius = new CornerRadius(7); // dentro la scheda del riquadro (ReadingCard: 8, bordo di 1)
        }
    }

    /// <summary>Stampa quello che il riquadro mostra, con l'anteprima di stampa di Edge (Ctrl+P, menu del tasto destro).</summary>
    public void Print()
    {
        if (web?.CoreWebView2 != null && entries.Count > 0) web.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
    }

    /// <summary>Salva come PDF quello che il riquadro mostra (la conversazione, con le intestazioni).</summary>
    public async Task<bool> SavePdfAsync(string path)
    {
        if (web?.CoreWebView2 == null || entries.Count == 0) return false;
        var settings = web.CoreWebView2.Environment.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        if (!await web.CoreWebView2.PrintToPdfAsync(path, settings)) return false;
        Safety.MarkFromInternet(path); // come gli allegati salvati (audit, S-10)
        return true;
    }

    public void ApplyZoom()
    {
        if (web?.CoreWebView2 != null) web.ZoomFactor = Math.Clamp(Store.Settings.Zoom, 50, 200) / 100.0;
    }

    /// <summary>Lo zoom salvato è cambiato da un riquadro (Ctrl+rotellina, Ctrl + e Ctrl -): il pulsante Zoom lo mostra.</summary>
    public static event Action? ZoomChanged;

#if DEBUG
    /// <summary>Prove ("zoom riquadro 125" in MISSIVA_KEYS): lo zoom di WebView2 cambiato come da Ctrl+rotellina.</summary>
    internal void TestZoom(double factor)
    {
        if (web?.CoreWebView2 != null) web.ZoomFactor = factor;
    }

    /// <summary>Prove ("sole" e "immagini" in MISSIVA_KEYS): il sole o "Mostra immagini" del primo messaggio della pagina, come il clic.</summary>
    internal void TestAction(string action)
    {
        Log.Write($"prova: {action} sul messaggio aperto");
        Do([action, "0"]);
    }
#endif

    /// <summary>
    /// Ctrl+rotellina, Ctrl +, Ctrl - e Ctrl 0 nel riquadro (IsZoomControlEnabled, richiesta dell'autore): lo zoom di WebView2
    /// diventa quello di Missiva (Impostazioni, Zoom), per tutti i riquadri aperti e i messaggi dopo, fra 50 e 200%. Il
    /// pizzico del touchpad e dello schermo (IsPinchZoomEnabled) ingrandisce invece la pagina come in Edge, fluido e senza
    /// cambiare lo zoom salvato: stringendo le dita si torna com'era, e il messaggio dopo si apre normale.
    /// </summary>
    void OnZoomFactorChanged(object? sender, EventArgs e)
    {
        if (web == null) return;
        int zoom = (int)Math.Round(Math.Clamp(web.ZoomFactor, 0.5, 2.0) * 100);
        if (Math.Abs(web.ZoomFactor * 100 - zoom) > 0.5) web.ZoomFactor = zoom / 100.0; // oltre i limiti: riportato dentro
        if (zoom == Math.Clamp(Store.Settings.Zoom, 50, 200)) return; // il proprio ApplyZoom, o nessun cambio
        Store.Settings.Zoom = zoom;
        Store.SaveSoon();
        Log.Write($"zoom del riquadro da tastiera o rotellina: {zoom}%");
        foreach (var other in open.Where(v => v != this)) other.ApplyZoom();
        ZoomChanged?.Invoke();
        var request = ++fitRequest; // la larghezza in pixel della pagina è cambiata: l'email larga si riadatta
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(150);
            if (request == fitRequest) await FitAsync();
        });
    }

    public void Refresh()
    {
        if (entries.Count > 0) Render();
    }

    /// <summary>Scarica il corpo (o annota l'errore); false se c'era già, si stava già scaricando o si è aperto altro.</summary>
    async Task<bool> FetchBodyAsync(Entry entry, CancellationToken cancel)
    {
        if (provider == null || entry.Body != null || entry.Loading) return false;
        entry.Loading = true;
        entry.Error = null;
        var account = provider.Account;
        try
        {
            // Senza rete da poco (Network.RecentlyOffline): subito la copia su disco, senza aspettare il collegamento.
            var body = Network.RecentlyOffline(account) ? BodyCache.Load(account, entry.Summary) : null;
            if (body == null)
            {
                try
                {
                    // Una busta PEC aperta da poco si riprende com'era: senza chiedere di nuovo il corpo né il messaggio intero.
                    // Poi quello in memoria e, per Gmail e IMAP, la copia su disco anche in linea (il corpo non cambia):
                    // un clic fuori dai corpi in memoria costava 290-440 ms invece di 40-70.
                    body = Pec.Recall(provider, entry.Summary);
                    if (body == null && provider.KeptBody(entry.Summary) == null)
                    {
                        // Fuori dal thread dell'interfaccia: file, DPAPI e JSON costano 5-30 ms.
                        var reading = Stopwatch.StartNew();
                        var saving = provider;
                        long read = 0;
                        body = await Task.Run(() =>
                        {
                            var saved = saving.SavedBody(entry.Summary);
                            read = reading.ElapsedMilliseconds;
                            return saved;
                        });
                        // Il tempo della lettura e quello in cui l'interfaccia la riprende: la differenza è interfaccia occupata.
                        if (body != null) Log.Time($"corpo dalla copia su disco ({body.Html.Length / 1024} KB, letto in {read} ms)", reading);
                    }
                    if (body == null)
                    {
                        body = await provider.GetBodyAsync(entry.Summary, cancel);
                        Network.Note(account, null);
                    }
                }
                catch (Exception ex) when (Network.IsOffline(ex))
                {
                    // Non in linea: la copia su disco, se il messaggio è stato aperto (o scaricato in anticipo) prima.
                    Network.Note(account, ex);
                    body = BodyCache.Load(account, entry.Summary);
                    if (body == null) throw; // senza copia: l'errore com'è, con la traccia fino al provider ("throw ex" la tagliava qui)
                }
                // Una copia su disco, cifrata, per leggerlo anche senza rete; non dalla Posta indesiderata.
                if (body.OfflineCopy == null && !InJunk(entry.Summary, junk) && provider is not EmlProvider) BodyCache.Save(account, entry.Summary, body);
            }
            // Le Authentication-Results arrivano con il corpo: da qui si sa se il messaggio viene davvero dal dominio del From
            // (il dominio autenticato può avere il logo, questo messaggio no se non lo supera). Mai dalla Posta indesiderata,
            // né per un .eml aperto, dove le intestazioni le ha scritte chi l'ha mandato.
            if (!InJunk(entry.Summary, junk) && provider is not EmlProvider) SenderLogos.NoteBody(account, entry.Summary, body);
            if (body.OfflineCopy != null) Log.Write("messaggio aperto dalla copia su disco (non in linea)");
            // Posta certificata e messaggi firmati: la busta com'è sul server, aperta e verificata in sottofondo (Pec).
            if (body.OfflineCopy == null && Pec.Wants(body))
            {
                var unwrapping = Stopwatch.StartNew();
                try { body = await Pec.OpenAsync(provider, entry.Summary, body, cancel); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
                catch (Exception ex) { Log.Error("posta certificata", ex); }
                Log.Time($"busta aperta ({(body.Pec is { } pec ? pec.IsPec ? pec.Kind : "firma S/MIME" : "niente")}, firma {body.Pec?.Signature})", unwrapping);
            }
            // La pulizia dell'HTML in sottofondo: il disegno la trova pronta invece di fermare l'interfaccia (se non riesce,
            // la rifà il disegno, che mostra l'avviso).
            bool light = Light, remote = LoadsImages(entry.Summary, body, junk, imagesOnce.Contains(OnceKey(entry.Summary)), account, IsEml);
            var cleaning = Stopwatch.StartNew();
            long began = 0, cleaned = 0;
            try
            {
                await Task.Run(() =>
                {
                    began = cleaning.ElapsedMilliseconds;
                    ReadingPage.Warm(body, light, remote);
                    cleaned = cleaning.ElapsedMilliseconds;
                });
            }
            catch { }
            // Quando è partita e quando ha finito, e quando l'interfaccia la riprende: la differenza è attesa, non lavoro.
            Log.Time($"HTML pulito in sottofondo ({body.Html.Length / 1024} KB, da {began} a {cleaned} ms)", cleaning);
            entry.Body = body;
            // Una copia su disco fatta prima che Missiva leggesse List-Unsubscribe: le intestazioni si chiedono ora, in sottofondo.
            if (body.ListHeadersUnread && body.OfflineCopy == null) _ = FillListHeadersAsync(provider, entry, body, cancel);
            // Gli altri destinatari, per i suggerimenti; non dallo Spam, come per i mittenti dell'elenco.
            if (!InJunk(entry.Summary, junk)) AddressBook.Received(entry.Body.To.Concat(entry.Body.Cc), entry.Summary.Date);
#if DEBUG
            DevTools.Dump(entry.Summary, entry.Body.Html);
#endif
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            entry.Loading = false; // superato da un altro messaggio: nessuno lo mostra
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("lettura messaggio", ex);
            // Senza rete e senza copia su disco: lo si dice, senza il testo di Windows.
            entry.Error = Network.Explain(ex) is { } offline ? offline + " Questo messaggio non è ancora stato scaricato."
                                                              : "Non riesco ad aprire il messaggio: " + ex.Message;
        }
        entry.Loading = false;
        return true;
    }

    async Task LoadBodyAsync(Entry entry, int v)
    {
        if (!await FetchBodyAsync(entry, opening.Token) || v != version) return;
        Render();
        MarkRead(entry);
    }

    /// <summary>
    /// Il corpo si vede: quando il messaggio diventa letto lo decide Impostazioni, Posta, Gestione messaggi, come in
    /// Outlook. Subito (di base); dopo N secondi, se nel frattempo non si è aperto altro; quando la selezione cambia
    /// (FlushReadOnLeave); mai.
    /// </summary>
    void MarkRead(Entry entry)
    {
        if (provider is not { } p || entry.Body == null || entry.Summary.IsRead || entry.ReadByHand) return;
        var shown = Stopwatch.StartNew();
        switch (Store.Settings.MarkRead)
        {
            case MarkReadMode.Immediately:
                SetRead(p, entry, shown);
                break;
            case MarkReadMode.AfterDelay:
                _ = MarkLaterAsync(p, entry, version, opening.Token, shown);
                break;
            case MarkReadMode.OnSelectionChange:
                if (!readOnLeave.Any(r => r.Entry == entry)) readOnLeave.Add((p, entry, shown));
                break;
        }
    }

    /// <summary>I messaggi da segnare letti quando la selezione cambia (un altro messaggio, un'altra cartella, nessuno).</summary>
    readonly List<(IMailProvider Provider, Entry Entry, Stopwatch Shown)> readOnLeave = new();

    void FlushReadOnLeave()
    {
        foreach (var (p, entry, shown) in readOnLeave) SetRead(p, entry, shown);
        readOnLeave.Clear();
    }

    /// <summary>Il conto alla rovescia si ferma se si apre un altro messaggio (o nessuno) prima della fine.</summary>
    async Task MarkLaterAsync(IMailProvider p, Entry entry, int v, CancellationToken cancel, Stopwatch shown)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(Store.Settings.MarkReadSeconds, 1, 120)), cancel); }
        catch (OperationCanceledException) { return; }
        if (v == version && !entry.ReadByHand) SetRead(p, entry, shown);
    }

    async void SetRead(IMailProvider p, Entry entry, Stopwatch shown)
    {
        if (entry.Summary.IsRead || entry.ReadByHand) return;
#if DEBUG || PROVA
        if (Environment.GetEnvironmentVariable("MISSIVA_NO_READ") == "1")
        {
            // Prove sulla casella vera: il momento giusto si vede nel registro, la casella non cambia.
            Log.Write($"prova: segnerei letto {entry.Summary.Id} dopo {shown.ElapsedMilliseconds} ms");
            return;
        }
#endif
        entry.Summary.IsRead = true;
        MessageRead?.Invoke(p, entry.Summary);
        // Letto senza rete (in treno, dalla copia sul PC): il segno va nella coda delle azioni e parte quando la rete torna.
        if (Network.KnownOffline(p.Account) && p is not EmlProvider)
        {
            ActionQueue.Add(p.Account, [entry.Summary], QueuedKind.Read);
            return;
        }
        try { await p.SetReadAsync(entry.Summary, true); }
        catch (Exception ex) when (Network.IsOffline(ex) && p is not EmlProvider)
        {
            Network.Note(p.Account, ex);
            ActionQueue.Add(p.Account, [entry.Summary], QueuedKind.Read);
        }
        catch (Exception ex) { Log.Error("stato di lettura", ex); }
    }

    // ---------------------------------------------------------------- disegno

    /// <summary>Il programma di installazione di WebView2 Runtime (Evergreen Bootstrapper), l'indirizzo che dà Microsoft.</summary>
    internal const string WebRuntimeLink = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>WebView2 Runtime non è installato: lo dice l'errore, o Windows quando gli si chiede la versione.</summary>
    internal static bool WebRuntimeMissing(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e is WebView2RuntimeNotFoundException) return true;
        try { return string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
        catch (WebView2RuntimeNotFoundException) { return true; }
        catch (Exception) { return false; }
    }

    /// <summary>Che cosa dire quando WebView2 non parte: se manca il Runtime (con il link per installarlo) o l'errore vero.</summary>
    internal static (string Text, bool Missing) WebErrorText(Exception ex, bool missing) => missing
        ? ("Per mostrare i messaggi serve Microsoft Edge WebView2 Runtime, di solito già presente in Windows 11: su questo PC non c'è. ", true)
        : ($"Il riquadro di lettura non è riuscito ad avviarsi: {ex.Message.Trim().TrimEnd('.')}. Può dipendere dalla cartella dei dati di Missiva (bloccata, o disco pieno). ", false);

    /// <summary>
    /// WebView2 non è partito. Prima per qualunque errore (anche una cartella bloccata o il disco pieno) c'era la stessa riga
    /// grigia sul Runtime, con l'indirizzo scritto come testo e "riapri Missiva" (audit, F-44 punto 3). Ora: se il Runtime
    /// manca davvero, il link di Microsoft per installarlo; altrimenti l'errore vero e la cartella dei registri; in tutti e
    /// due i casi Riprova, che ricrea il riquadro senza riavviare.
    /// </summary>
    void ShowWebError(Exception ex)
    {
        var (text, missing) = WebErrorText(ex, WebRuntimeMissing(ex));
        System.Windows.Documents.Hyperlink Link(string label, Action click)
        {
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(label));
            link.Click += (_, _) => click();
            return link;
        }
        StatusText.Inlines.Clear();
        StatusText.Inlines.Add(text);
        if (missing)
        {
            StatusText.Inlines.Add(Link("Scaricalo dal sito di Microsoft", () =>
            {
                try { Process.Start(new ProcessStartInfo(WebRuntimeLink) { UseShellExecute = true }); }
                catch (Exception e) { Log.Error("apertura del link di WebView2 Runtime", e); }
            }));
            StatusText.Inlines.Add(", installalo e poi premi ");
            StatusText.Inlines.Add(Link("Riprova", RetryWeb));
            StatusText.Inlines.Add(".");
        }
        else
        {
            StatusText.Inlines.Add(Link("Riprova", RetryWeb));
            StatusText.Inlines.Add(" oppure ");
            StatusText.Inlines.Add(Link("apri la cartella dei registri", App.OpenDataFolder));
            StatusText.Inlines.Add(" (missiva.log dice l'errore per esteso).");
        }
        StatusText.Visibility = Visibility.Visible;
    }

#if DEBUG
    /// <summary>Prove (MISSIVA_ERRORE_WEBVIEW): l'avvio finto non riuscito c'è già stato, Riprova riesce.</summary>
    static bool webTestFailed;
#endif

    /// <summary>Riprova dopo un WebView2 non partito: controllo e ambiente nuovi (quelli falliti non si riusano), poi il messaggio aperto.</summary>
    void RetryWeb()
    {
        Log.Write("WebView2: Riprova dopo un avvio non riuscito");
        if (web is { } failed)
        {
            web = null;
            WebSlot.Children.Remove(failed);
            try { failed.Dispose(); } catch (Exception ex) { Log.Error("WebView2 non partito", ex); }
        }
        webReady = null;
        if (environment is { IsCompletedSuccessfully: false } || open.All(v => v.web == null)) environment = null; // quello buono di un'altra finestra resta
        servedPage = shownPage = actionsPage = null;
        pageNumber = 0;
        blankNext = false;
        StatusText.Visibility = Visibility.Collapsed;
        StatusText.Inlines.Clear();
#if DEBUG
        webTestFailed = true;
#endif
        if (entries.Count == 0 || provider == null)
        {
            ContentPanel.Visibility = Visibility.Hidden;
            EmptyPanel.Visibility = Visibility.Visible;
            Prepare();
            return;
        }
        Render(keepScroll: false);
    }

    /// <param name="keepScroll">Ridisegno della stessa conversazione (corpo arrivato, immagini, tema): si resta dove si era.</param>
    async void Render(bool keepScroll = true)
    {
        bool light = Light;
        var building = Stopwatch.StartNew();
        var built = ReadingPage.Build(entries.Select(e => new PageEntry(e.Summary, e.Body, e.Expanded, e.Loading, e.Error,
            LoadsImages(e.Summary, e.Body, junk, imagesOnce.Contains(OnceKey(e.Summary)), provider?.Account, IsEml),
            e.Expanded && e.Body != null && provider != null ? SenderCheck.Of(provider.Account, e.Summary, e.Body) : null,
            LogoOf(e.Summary), InJunk(e.Summary, junk), UnsubscribeLine(e), ImagesOnce(provider?.Account, e.Summary, e.Body, junk, IsEml))).ToList(), light, provider?.Account.Id ?? "",
            new PageHeader(Selected is { Subject.Length: > 0 } s ? s.Subject : "(nessun oggetto)", Dark,
                ShowAccount && provider != null ? (provider.Account.Email, Avatars.AccountColor(provider.Account)) : null));
        Log.Time($"pagina costruita ({entries.Count(e => e.Body != null)} corpi)", building);
        page = actionsPage = built;
        var paper = light ? Colors.White : MailHtml.DarkPaper;
        Paper.Background = new SolidColorBrush(paper);

        foreach (var entry in entries.Where(e => e.Expanded && e.Body == null && !e.Loading && e.Error == null))
            _ = LoadBodyAsync(entry, version);

        var ready = webReady ??= InitWebAsync();
        try
        {
            await ready;
        }
        catch (Exception) when (webReady != ready)
        {
            return; // WebView2 chiuso mentre si preparava (Missiva nell'area di notifica): alla riapertura si ricrea
        }
        catch (Exception ex)
        {
            Log.Error("WebView2", ex);
            Web.Visibility = Visibility.Collapsed;
            ShowWebError(ex);
            EmptyPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Visible;
            return;
        }
        var scroll = keepScroll ? await ScrollAsync() : null;
        if (page != built || web == null) return; // nel frattempo è arrivata una versione più nuova, o il riquadro si è chiuso
        restoreScroll = scroll is > 0 ? scroll : null;
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(paper.R, paper.G, paper.B);
        // Le email che hanno una versione scura la usano solo se il riquadro è scuro.
        Web.CoreWebView2.Profile.PreferredColorScheme = light ? CoreWebView2PreferredColorScheme.Light : CoreWebView2PreferredColorScheme.Dark;
        ApplyZoom();
        var fragment = restoreScroll == null && anchor is { } a ? $"#m{a}" : "";
        Web.CoreWebView2.Navigate($"{ReadingPage.Origin}/?v={++pageNumber}{fragment}");
    }

    /// <summary>
    /// Il logo del marchio del mittente (Logos), se c'è già; mai nella posta indesiderata, per un .eml aperto, né per un
    /// messaggio arrivato senza la prova che venga dal dominio del From (con la fascia rossa lo toglie anche la pagina).
    /// </summary>
    byte[]? LogoOf(MessageSummary summary) =>
        !InJunk(summary, junk) && provider is { } p and not EmlProvider && !SenderLogos.IsDoubtful(p.Account, summary)
        && Logos.DomainFor(summary.From) is { } domain ? Logos.Bytes(domain) : null;

    /// <summary>
    /// Dal riquadro vuoto al messaggio quando la sua pagina è pronta, non prima: il riquadro vuoto non passa per la
    /// pagina di un altro messaggio (quella che WebView2 aveva disegnato prima di essere nascosto).
    /// </summary>
    void ShowContent()
    {
        if (servedPage == null || servedPage != actionsPage || ContentPanel.Visibility == Visibility.Visible) return;
        EmptyPanel.Visibility = Visibility.Collapsed;
        ContentPanel.Visibility = Visibility.Visible;
    }

    /// <summary>La pagina che valgono i pulsanti (quella di entries): null mentre si aspetta un messaggio nuovo.</summary>
    ReadingPage? actionsPage;
    /// <summary>L'ultima pagina servita a WebView2 e quella che si vede (testo pronto).</summary>
    ReadingPage? servedPage, shownPage;

    /// <summary>I messaggi con immagini bloccate nella pagina dei pulsanti, per "Mostra immagini".</summary>
    List<Entry> BlockedEntries() => actionsPage is not { } shown ? new() : entries.Where((e, k) => k < shown.Blocked.Length && shown.Blocked[k] > 0).ToList();

    /// <summary>
    /// Le immagini esterne di un messaggio si caricano da sole? Per i mittenti consentiti con "Mostra immagini", ma mai
    /// nella Posta indesiderata (audit, F-12): un messaggio finito lì con il From di un mittente consentito, vero o
    /// falsificato, direbbe a chi lo manda che è stato letto. Lì solo dopo "Mostra immagini", per quella volta (once). Lo
    /// stesso negli altri casi di <see cref="ImagesOnce"/>.
    /// </summary>
    internal static bool LoadsImages(MessageSummary summary, MessageBody? body, string? junkFolderId, bool once = false, MailAccount? account = null, bool eml = false) =>
        ImagesOnce(account, summary, body, junkFolderId, eml) != null ? once : Store.Settings.ShowsImagesFrom(ImageSender(summary, body));

    /// <summary>
    /// Perché per questo messaggio "Mostra immagini" vale solo per quella volta e il mittente non si ricorda, o null se si
    /// ricorda. Il testo va nel suggerimento della fascia delle immagini bloccate (ReadingPage.Header). Oltre alla Posta
    /// indesiderata, dall'audit dell'11 ottobre (T-02) i casi in cui Missiva stessa dice di non fidarsi di chi scrive, e
    /// già toglie il logo e "Annulla iscrizione": la fascia rossa del mittente non verificato (SenderCheck.Failed sulle
    /// intestazioni del server della casella) e quella rossa della posta certificata o della firma (busta senza la firma
    /// di un gestore, messaggio alterato dopo la firma). Prima il From di un mittente consentito, anche falsificato,
    /// bastava a far partire le immagini, e "Mostra immagini" lo ricordava per sempre. Dalle scelte del 12 ottobre 2026
    /// (T-02) anche ogni .eml aperto da un allegato, da qualunque cartella: il suo From non lo verifica nessuno, e bastava
    /// allegare a un messaggio normale un .eml con il From di un mittente consentito.
    /// </summary>
    /// <param name="account">L'account del messaggio, per sapere quale Authentication-Results è del suo server; null non lo guarda.</param>
    /// <param name="eml">Il messaggio è un .eml aperto da un allegato (EmlProvider).</param>
    internal static string? ImagesOnce(MailAccount? account, MessageSummary summary, MessageBody? body, string? junkFolderId, bool eml = false)
    {
        if (InJunk(summary, junkFolderId)) return "Questo messaggio è nella Posta indesiderata";
        if (eml) return "Questo messaggio è stato aperto da un allegato, e nessuno ha verificato chi lo ha scritto";
        if (body == null) return null;
        if (account != null && SenderCheck.Own(account, body.AuthenticationResults) is { } own && SenderCheck.Failed(own)) return "Il mittente di questo messaggio non è verificato";
        if (body.Pec is { } pec && Pec.Band(pec).Tone == "danger") return "La posta certificata o la firma di questo messaggio ha un avviso rosso";
        return null;
    }

    /// <summary>
    /// "Mostra immagini" su un messaggio: il suo mittente si ricorda da ora in poi (vero), oppure vale solo per questa volta
    /// (falso: <see cref="ImagesOnce"/>) e nelle impostazioni non cambia nulla.
    /// </summary>
    internal static bool RememberImages(MailAccount? account, MessageSummary summary, MessageBody? body, string? junkFolderId, bool eml = false)
    {
        if (ImagesOnce(account, summary, body, junkFolderId, eml) != null) return false;
        if (ImageSender(summary, body) is { Length: > 0 } email) Store.Settings.AllowImagesFrom(email);
        return true;
    }

    /// <summary>Nella Posta indesiderata: la cartella dell'account del messaggio aperto, o l'etichetta nei risultati di una ricerca.</summary>
    internal static bool InJunk(MessageSummary summary, string? junkFolderId) =>
        summary.LabelKind == FolderKind.Junk || junkFolderId != null && summary.FolderId == junkFolderId;

    static string OnceKey(MessageSummary summary) => summary.FolderId + "|" + summary.Id;

    /// <summary>
    /// Chi decide le immagini esterne di un messaggio: il mittente della riga, o per la posta certificata il mittente vero
    /// (MessageBody.ImagesFrom), non il gestore della busta, lo stesso per tutti i suoi clienti.
    /// </summary>
    public static string ImageSender(MessageSummary summary, MessageBody? body) => body?.ImagesFrom ?? summary.From.Email;

    // ---------------------------------------------------------------- WebView2 confinato

    /// <summary>
    /// Senza GPU: le email sono pagine ferme, senza JavaScript, e Chromium le disegna bene anche così. Il processo GPU di
    /// WebView2 passa da 100-165 MB a circa 23 (misurato il 26 settembre 2026, 47 messaggi aperti), a parità di tempi.
    /// Dal giro D1 anche quel che resta della GPU dentro il processo del browser (--in-process-gpu: la GPU è spenta, resta
    /// il disegno in software), e niente richieste in sottofondo né aggiornamenti dei componenti (circa 19 MB scaricati che
    /// Missiva non usa: filtro delle pubblicità, riconoscimento vocale, sillabazione): 5 processi invece di 6. Il servizio
    /// di rete resta nel suo processo: D1 l'aveva messo nel browser (NetworkServiceInProcess2, altri 15 MB circa), ma
    /// scarica le immagini dei mittenti, e un suo difetto colpirebbe il processo del browser invece di uno separato (l'autore
    /// ha scelto l'isolamento). Un guasto del browser porta via tutto WebView2: OnProcessFailed ricrea il riquadro.
    /// </summary>
    static Task<CoreWebView2Environment> SharedEnvironment() =>
        environment ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(Store.Dir, "WebView2"),
            new CoreWebView2EnvironmentOptions
            {
                Language = "it-IT",
                AdditionalBrowserArguments = BrowserArguments,
            });

    internal const string BrowserArguments = "--disable-gpu --in-process-gpu --disable-background-networking --disable-component-update";

    /// <summary>
    /// Il confino del riquadro, uguale per ogni WebView2 di Missiva: niente JavaScript della pagina, niente messaggi, oggetti
    /// dell'app, strumenti di sviluppo, compilazione automatica; ogni richiesta passa da WebResourceRequested; nessuna
    /// navigazione nei frame, nessuno scaricamento, nessun permesso, nessun programma esterno. In un punto solo perché le
    /// prove lo applichino com'è a un WebView2 vero senza finestra (ProvaBozze.Sicurezza2.cs).
    /// </summary>
    internal static void Confine(CoreWebView2 core)
    {
        var s = core.Settings;
        s.IsScriptEnabled = false;
        s.IsWebMessageEnabled = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.AreDevToolsEnabled = false;
        s.IsZoomControlEnabled = true; // Ctrl+rotellina e Ctrl +/-: lo zoom di Missiva (OnZoomFactorChanged)
        s.IsPinchZoomEnabled = true; // il pizzico ingrandisce la pagina, come in Edge
        s.IsSwipeNavigationEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsBuiltInErrorPageEnabled = false;
        s.IsStatusBarEnabled = true; // passando su un link si vede l'indirizzo vero
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.FrameNavigationStarting += (_, e) => e.Cancel = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
    }

    /// <summary>
    /// Il processo del browser di WebView2 (con la rete e la GPU dentro) o quello della pagina è caduto: il riquadro si
    /// ricrea da capo, con un WebView2 nuovo, e il messaggio aperto torna com'era. Senza, restava un riquadro vuoto fino al
    /// riavvio di Missiva.
    /// </summary>
    void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (e.ProcessFailedKind is not (CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)) return;
        var failed = web;
        bool browser = e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited;
        Log.Write($"WebView2: processo caduto ({e.ProcessFailedKind}, {e.Reason}, codice {e.ExitCode}): riquadro ricreato");
        Dispatcher.BeginInvoke(() =>
        {
            if (failed == null || web != failed) return; // già ricreato
            // Il controllo caduto non si tocca più (CoreWebView2 lancia un'eccezione): si toglie senza passare dalla pagina
            // del segnaposto, come ReleaseWeb, e il browser caduto porta via l'ambiente condiviso.
            web = null;
            webReady = null;
            WebSlot.Children.Remove(failed);
            try { failed.Dispose(); } catch (Exception ex) { Log.Error("WebView2 caduto", ex); }
            servedPage = shownPage = actionsPage = null;
            pageNumber = 0;
            blankNext = false;
            if (browser) environment = null;
            ContentPanel.Visibility = Visibility.Hidden;
            EmptyPanel.Visibility = Visibility.Visible;
            if (entries.Count == 0 || provider == null)
            {
                Prepare();
                return;
            }
            opened = Stopwatch.StartNew();
            clock = "processo ricreato";
            Render(keepScroll: false);
        });
    }

    async Task InitWebAsync()
    {
        // Il controllo di questa preparazione: se intanto ReleaseWeb lo chiude (finestra nell'area di notifica entro il mezzo
        // secondo della preparazione), ci si ferma, senza toccare quello nuovo che il getter Web creerebbe.
        var control = Web;
#if DEBUG
        // Prove: MISSIVA_ERRORE_WEBVIEW=manca (Runtime assente) o un altro testo (un errore qualunque) fa fallire il primo
        // avvio di WebView2, per vedere l'avviso; Riprova poi riesce.
        if (!webTestFailed && Environment.GetEnvironmentVariable("MISSIVA_ERRORE_WEBVIEW") is { Length: > 0 } test)
        {
            await Task.Yield();
            throw test == "manca" ? new WebView2RuntimeNotFoundException("prova: WebView2 Runtime assente") : new IOException(test);
        }
#endif
        await control.EnsureCoreWebView2Async(await SharedEnvironment());
        if (web != control) throw new OperationCanceledException("WebView2 chiuso mentre si preparava");
        var core = control.CoreWebView2;
        Confine(core);
        control.ZoomFactorChanged += OnZoomFactorChanged;
        core.WebResourceRequested += OnResourceRequested;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindow;
        core.ContextMenuRequested += OnContextMenu;
        core.ProcessFailed += OnProcessFailed;
        // Il testo è pronto prima che arrivino le immagini esterne dei mittenti consentiti, che possono metterci un secondo
        // (l'email di Azure "lenta" vista da A: testo a 130 ms, immagini di microsoft.com a 0,8-2,2 s). L'email larga si
        // adatta già qui, senza aspettarle, e poi di nuovo alla fine.
        // La pagina nasce nascosta (ReadingPage, "fitting"): qui si adatta, si aspettano un poco le immagini che mancano
        // (al più SettleLimit: quelle esterne possono metterci secondi) e solo allora si mostra, già com'è. Intanto
        // Chromium tiene la pagina di prima, quindi si passa da una mail all'altra senza stati di mezzo.
        core.DOMContentLoaded += async (_, _) =>
        {
            if (opened.IsRunning) Log.Time($"{clock}, testo pronto", opened);
            var served = servedPage;
            var loaded = pageLoaded;
            shownPage = served;
            if (await Script(FitAndReveal(restoreScroll)) == "true")
            {
                await Task.WhenAny(loaded.Task, Task.Delay(SettleLimit));
                if (servedPage != served) return; // intanto un'altra pagina
                await FitAsync();
                if (restoreScroll is { } y) await Script(ScrollTo(y));
                await RevealAsync();
            }
            else ShowContent(); // la pagina si è già mostrata nello stesso giro
            if (opened.IsRunning) Log.Time($"{clock}, pagina mostrata", opened);
        };
        core.NavigationCompleted += async (_, e) =>
        {
            if (e.NavigationId == pageNavigation) pageLoaded.TrySetResult();
            if (e.NavigationId == blankNavigation && entries.Count == 0 && blankPaper != null) Paper.Background = blankPaper;
            // Il primo segnaposto nella pagina (dopo Prepare): da qui il riquadro vuoto è quello di Chromium, e WebView2 non si
            // nasconde più (Clear).
            if (e.NavigationId == blankNavigation && entries.Count == 0 && ContentPanel.Visibility != Visibility.Visible && PreviewHost.Visibility != Visibility.Visible)
            {
                ContentPanel.Visibility = Visibility.Visible;
                EmptyPanel.Visibility = Visibility.Collapsed;
                return;
            }
            if (blankNext)
            {
                blankNext = false;
                if (entries.Count == 0) ShowBlank();
            }
            if (!e.IsSuccess)
            {
                ShowContent(); // anche se la pagina non si è caricata
                return;
            }
            if (opened.IsRunning) Log.Time($"{clock}, pagina disegnata", opened); // non la pagina vuota di Prepare
            await FitAsync();
            if (restoreScroll is { } y) await Script(ScrollTo(y));
            restoreScroll = null;
            await RevealAsync(); // se DOMContentLoaded non l'ha già fatto
        };
        control.SizeChanged += async (_, _) =>
        {
            // Chromium ridimensiona la pagina un po' dopo WPF: misurando subito si leggerebbe la larghezza vecchia.
            var request = ++fitRequest;
            await Task.Delay(150);
            if (request == fitRequest) await FitAsync();
        };
        // Pronto: se una preparazione di prima non era riuscita, il riquadro torna com'era, senza l'avviso del Runtime.
        if (StatusText.Visibility == Visibility.Visible)
        {
            StatusText.Visibility = Visibility.Collapsed;
            StatusText.Text = "";
        }
        if (control.Visibility == Visibility.Collapsed) control.Visibility = PreviewHost.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
    }

    int fitRequest;

    /// <summary>
    /// Quanto si aspettano le immagini che mancano prima di mostrare la pagina (poi arrivano in vista). Erano 300 ms per
    /// tutte: una mail pronta a 55 ms si vedeva a 387 (l'autore: "l'apertura di alcune mail è molto lenta"). Ora solo quelle
    /// della mail servite da Missiva nella parte visibile, che arrivano in pochi ms; quelle da Internet non si aspettano.
    /// </summary>
    static readonly TimeSpan SettleLimit = TimeSpan.FromMilliseconds(100);
    /// <summary>NavigationCompleted della pagina in corso: tutte le immagini arrivate (o fallite).</summary>
    TaskCompletionSource pageLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    ulong pageNavigation;

    /// <summary>Immagini servite da Missiva (incorporate, loghi) nella parte visibile, anche nelle shadow root, non ancora arrivate.</summary>
    const string ImagesPending = """
        (() => { const all = [...document.images];
          for (const m of document.querySelectorAll('.mail')) if (m.shadowRoot) all.push(...m.shadowRoot.querySelectorAll('img'));
          return all.some(i => !i.complete && i.src.startsWith(location.origin) && i.getBoundingClientRect().top < innerHeight); })()
        """;

    static string ScrollTo(double y) => $"window.scrollTo(0,{y.ToString(System.Globalization.CultureInfo.InvariantCulture)})";

    /// <summary>La pagina, pronta, si vede (via "fitting"); poi il riquadro, se era vuoto.</summary>
    async Task RevealAsync()
    {
        await Script("document.documentElement.classList.remove('fitting')");
        ShowContent();
    }

    double? restoreScroll;
    System.Diagnostics.Stopwatch opened = new(); // per i tempi della versione di prova
    /// <summary>Che cosa misura <see cref="opened"/>: l'apertura di un messaggio ("clic") o il cambio di tema.</summary>
    string clock = "clic";

    /// <summary>
    /// Script di Missiva, non dell'email: WebView2 li esegue anche con JavaScript spento per la pagina, che invece non
    /// può eseguire nulla (IsScriptEnabled falso e CSP script-src 'none').
    /// </summary>
    async Task<string?> Script(string code)
    {
        try { return web?.CoreWebView2 == null ? null : await web.CoreWebView2.ExecuteScriptAsync(code); }
        catch (Exception ex) { Log.Error("script del riquadro", ex); return null; }
    }

    /// <summary>
    /// Come nel nuovo Outlook: un'email più larga del riquadro (le newsletter da 600-700 pixel) si rimpicciolisce
    /// fino a starci, invece di obbligare a scorrere di lato. Non sotto metà, dove il testo non si legge più: oltre,
    /// scorre di lato la pagina, con la barra sottile in fondo al riquadro (.content in ReadingPage).
    /// </summary>
    Task FitAsync() => Script(Fit);

    const string Fit = """
        for (const m of document.querySelectorAll('.mail')) {
          m.style.zoom = '';
          const body = m.shadowRoot && m.shadowRoot.querySelector('.missiva-body');
          const need = body ? body.scrollWidth : m.scrollWidth, have = m.clientWidth;
          if (need > have + 2) m.style.zoom = Math.max(0.5, have / need).toFixed(3);
        }
        """;

    /// <summary>
    /// Un giro solo verso la pagina al posto di tre o quattro (audit, v2 V-33): adatta le email larghe, guarda se mancano
    /// immagini servite da Missiva e, se non ne mancano, rimette lo scorrimento e mostra la pagina. Dà "true" se ne mancano:
    /// allora la pagina resta nascosta e si aspetta, come prima.
    /// </summary>
    static string FitAndReveal(double? scroll) =>
        "(() => {" + Fit + "const pending = " + ImagesPending.Trim() + "; if (!pending) { " + (scroll is { } y ? ScrollTo(y) + "; " : "") +
        "document.documentElement.classList.remove('fitting'); } return pending; })()";

    async Task<double?> ScrollAsync() =>
        double.TryParse(await Script("window.scrollY"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ? y : null;

    /// <summary>
    /// Ogni richiesta passa di qui. La pagina e le immagini incorporate le fornisce Missiva; da Internet arrivano solo
    /// immagini, e solo se sulla pagina c'è un mittente consentito (le altre sono già state tolte dall'HTML).
    /// </summary>
    void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = Web.CoreWebView2.Environment;
        var current = page;
        switch (FateOf(current, e.Request.Uri, e.ResourceContext, out var resource))
        {
            case RequestFate.Page:
                servedPage = current;
                e.Response = Serve(env, Encoding.UTF8.GetBytes(current.Html), "text/html; charset=utf-8");
                break;
            case RequestFate.Resource:
                e.Response = Serve(env, resource.Data, resource.Type);
                break;
            case RequestFate.Network:
                break; // nessuna risposta di Missiva: la richiesta va in rete
            default:
                e.Response = Refuse(env);
                break;
        }
    }

    /// <summary>Che cosa fa il riquadro di una richiesta di WebView2: la pagina, una sua risorsa (immagine incorporata, logo), la rete, niente.</summary>
    internal enum RequestFate { Page, Resource, Network, Refused }

    /// <summary>
    /// La regola di <see cref="OnResourceRequested"/> per la pagina mostrata, senza toccare WebView2: così le prove la usano
    /// com'è con un WebView2 vero senza finestra (ProvaBozze.Sicurezza2.cs). La pagina si dà solo a chi la chiede come
    /// documento: un'immagine dell'email con l'indirizzo "#a" o "?a" è la pagina stessa, e prima la riceveva per intero ogni
    /// volta che il motore la chiedeva (audit dell'11 ottobre, T-01).
    /// </summary>
    internal static RequestFate FateOf(ReadingPage page, string address, CoreWebView2WebResourceContext context, out (byte[] Data, string Type) resource)
    {
        resource = default;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return RequestFate.Refused;
        if (uri.Host == ReadingPage.Host)
        {
            if (uri.AbsolutePath == "/") return context == CoreWebView2WebResourceContext.Document ? RequestFate.Page : RequestFate.Refused;
            return page.Resources.TryGetValue(uri.AbsolutePath, out resource) ? RequestFate.Resource : RequestFate.Refused;
        }
        return page.RemoteAllowed && context == CoreWebView2WebResourceContext.Image && uri.Scheme is "http" or "https" ? RequestFate.Network : RequestFate.Refused;
    }

    internal static CoreWebView2WebResourceResponse Serve(CoreWebView2Environment env, byte[] data, string type) =>
        env.CreateWebResourceResponse(new MemoryStream(data), 200, "OK", $"Content-Type: {type}\r\nCache-Control: no-store");

    internal static CoreWebView2WebResourceResponse Refuse(CoreWebView2Environment env) =>
        env.CreateWebResourceResponse(null, 403, "Bloccato", "");

    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Host == ReadingPage.Host && uri.AbsolutePath == "/")
        {
            pageLoaded.TrySetResult(); // quella di prima non si aspetta più
            pageLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pageNavigation = e.NavigationId;
            if (uri.Query == $"?v={blankNumber}") blankNavigation = e.NavigationId;
            return;
        }
        e.Cancel = true;
        if (uri == null || !e.IsUserInitiated) return; // nessuna navigazione automatica (refresh, reindirizzamenti)
        if (uri.Host == ReadingPage.Host)
        {
            // Solo i pulsanti della pagina mostrata, con il suo gettone: un link dell'email verso /do/... non fa nulla.
            // Mentre si aspetta il messaggio nuovo (fino a 300 ms) quelli della pagina di prima non fanno nulla.
            var parts = uri.AbsolutePath.StartsWith("/do/") ? uri.AbsolutePath["/do/".Length..].Split('/') : [];
            if (parts.Length > 1 && parts[0] == actionsPage?.Token) Dispatcher.BeginInvoke(() => Do(parts[1..]));
            else Log.Write("azione del riquadro rifiutata: " + uri.AbsolutePath);
        }
        else
        {
            Dispatcher.BeginInvoke(() => OpenLink(uri));
        }
    }

    void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.IsUserInitiated && Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) Dispatcher.BeginInvoke(() => OpenLink(uri));
    }

    /// <summary>Menu del tasto destro: solo copia, seleziona tutto, copia link, copia immagine e stampa; nessuno sul segnaposto.</summary>
    void OnContextMenu(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (entries.Count == 0)
        {
            e.Handled = true;
            return;
        }
        var keep = new HashSet<string> { "copy", "selectAll", "copyLinkLocation", "copyImage", "print" };
        var items = e.MenuItems;
        for (int k = items.Count - 1; k >= 0; k--)
            if (items[k].Kind != CoreWebView2ContextMenuItemKind.Separator && !keep.Contains(items[k].Name)) items.RemoveAt(k);
        for (int k = items.Count - 1; k >= 0; k--)
            if (items[k].Kind == CoreWebView2ContextMenuItemKind.Separator &&
                (k == 0 || k == items.Count - 1 || items[k - 1].Kind == CoreWebView2ContextMenuItemKind.Separator))
                items.RemoveAt(k);
        if (items.Count == 0) e.Handled = true;
    }

    /// <summary>
    /// Scorciatoie del browser che non hanno senso qui (salva pagina, apri file). Ctrl+U (sorgente) passa: nella finestra
    /// principale è "Segna come da leggere", che la segna come gestita; altrove la pagina dell'origine non si apre comunque.
    /// </summary>
    static void OnWebKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.S or Key.O or Key.G or Key.H or Key.J) e.Handled = true;
        if (e.Key is Key.F7 or Key.F12) e.Handled = true;
    }

    // ---------------------------------------------------------------- azioni dalla pagina

    async void Do(string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var i) || i < 0 || i >= entries.Count) return;
        var entry = entries[i];
        switch (parts[0])
        {
            case "sun":
                lightOverride = !Light;
                await RenderWarmAsync();
                break;
            case "images":
                ShowImages();
                break;
            case "toggle":
                if (entries.Count == 1 && entry.Expanded) return;
                entry.Expanded = !entry.Expanded;
                anchor = i;
                if (entry.Expanded && entry.Body == null && entry.Error == null)
                {
                    // Come all'apertura: si ridisegna una volta sola, con il corpo, se arriva entro 300 ms (qui la pagina resta
                    // la stessa conversazione: aspettare di più lascerebbe il clic senza risposta).
                    var v = version;
                    var body = LoadBodyAsync(entry, v);
                    if (await Task.WhenAny(body, Task.Delay(ExpandDelay)) == body || v != version || !entry.Loading) return;
                }
                Render(keepScroll: false);
                break;
            case "reply":
                Reply(entry, ComposeMode.Reply);
                break;
            case "replyall":
                Reply(entry, ComposeMode.ReplyAll);
                break;
            case "forward":
                Reply(entry, ComposeMode.Forward);
                break;
            case "more" when provider != null:
                MoreRequested?.Invoke(provider, entry.Summary);
                break;
            case "person" when provider != null:
                PersonRequested?.Invoke(provider, entry.Summary.From);
                break;
            case "edit" when provider != null:
                EditRequested?.Invoke(provider, entry.Summary, entry.Body);
                break;
            case "open" or "save" when parts.Length > 2 && int.TryParse(parts[2], out var j) && entry.Body is { } body && j >= 0 && j < body.Attachments.Count:
                await OpenAttachmentAsync(entry, body.Attachments[j], save: parts[0] == "save");
                break;
            case "saveall" when entry.Body is { } withFiles:
                await SaveAllAsync(entry, withFiles.Attachments.Where(a => !a.IsInline).ToList());
                break;
            case "accept" or "tentative" or "decline" or "propose" when provider != null && entry.Body?.Invite is { Answerable: true } invite:
                InviteRequested?.Invoke(provider, entry.Summary, invite, parts[0] switch
                {
                    "accept" => InviteAnswer.Accept,
                    "tentative" => InviteAnswer.Tentative,
                    "decline" => InviteAnswer.Decline,
                    _ => InviteAnswer.Propose,
                });
                break;
            case "consent" when provider != null && entry.Body?.Invite is { Answerable: true } pending:
                await AllowCalendarAsync(provider, pending);
                break;
            case "unsubscribe" when provider != null:
                await UnsubscribeAsync(provider, entry);
                break;
        }
    }

    // ---------------------------------------------------------------- annulla iscrizione

    /// <summary>La riga "Annulla iscrizione" di un messaggio aperto (PageEntry.Unsubscribe): null se il comando non c'è.</summary>
    string? UnsubscribeLine(Entry e)
    {
        if (!e.Expanded || provider == null || Unsubscribe.For(provider.Account, e.Summary, e.Body, InJunk(e.Summary, junk)) == null) return null;
        lock (unsubscribed) return unsubscribed.GetValueOrDefault(provider.Account.Id + "|" + OnceKey(e.Summary), "");
    }

    /// <summary>
    /// Gmail e IMAP aprono i messaggi dalla copia su disco, e le copie fatte prima che Missiva leggesse List-Unsubscribe non
    /// l'hanno: "Annulla iscrizione" non compariva quasi mai (segnalato dall'autore il 4 ottobre 2026). Per quelle copie le due
    /// intestazioni si chiedono al server in sottofondo, senza il corpo (IMailProvider.GetListHeadersAsync), si scrivono nel
    /// corpo e nella copia (una volta sola per messaggio), e la riga entra nella pagina già mostrata con uno script, senza
    /// ridisegnarla; se la pagina sta ancora arrivando, si ridisegna quella.
    /// </summary>
    async Task FillListHeadersAsync(IMailProvider p, Entry entry, MessageBody body, CancellationToken cancel)
    {
        try
        {
            var asked = Stopwatch.StartNew();
            if (await p.GetListHeadersAsync(entry.Summary, cancel) is not { } headers) return;
            body.ListUnsubscribe = headers.Unsubscribe;
            body.ListUnsubscribePost = headers.Post;
            body.ListHeadersUnread = false;
            _ = BodyCache.SetListHeaders(p.Account, entry.Summary, headers);
            Log.Time($"intestazioni di Annulla iscrizione lette dopo il corpo ({(headers.Unsubscribe != null ? "ci sono" : "il messaggio non le ha")})", asked);
            // Solo in una pagina disegnata con questo corpo: una non ancora costruita avrà la riga da sé. Se la pagina sta ancora
            // arrivando (una newsletter grande: le intestazioni fanno prima) si aspetta che sia mostrata, senza rifarla: una
            // seconda pagina si vedrebbe. Lo script non fa nulla se la riga c'è già.
            bool Mine() => provider == p && entry.Body == body && entries.IndexOf(entry) is >= 0 and var at && page is { } built && at < built.Bodies.Count
                           && ReferenceEquals(built.Bodies[at], body);
            for (int wait = 0; wait < 100 && Mine() && shownPage != page; wait++) await Task.Delay(50);
            if (!Mine() || shownPage != page || UnsubscribeLine(entry) is not { } line) return;
            await Script(shownPage!.UnsubscribeScript(entries.IndexOf(entry), line));
        }
        catch (OperationCanceledException) { } // si è aperto un altro messaggio: alla prossima apertura
        catch (Exception ex)
        {
            Log.Error("intestazioni di Annulla iscrizione", ex);
        }
    }

    /// <summary>
    /// Annulla iscrizione (audit, F-11), sempre dopo una conferma che dice che cosa succede e verso dove (Unsubscribe.Confirmation).
    /// Un clic (RFC 8058): la POST di Unsubscribe.OneClickAsync, poi un avviso breve; se non riesce, la pagina del mittente
    /// nel browser. mailto: una scrittura già compilata, che parte solo con Invia. Solo https: la pagina nel browser, con i
    /// controlli dei link (OpenLink). Nessuna azione sulla casella. Nella versione di prova la POST va solo nel registro.
    /// </summary>
    async Task UnsubscribeAsync(IMailProvider from, Entry entry)
    {
        if (Unsubscribe.For(from.Account, entry.Summary, entry.Body, InJunk(entry.Summary, junk)) is not { } action) return;
        var owner = Window.GetWindow(this);
        var sender = entry.Summary.From.Display;
        bool Confirm(string heading, string body, string primary) => Dialog.Ask(owner, heading, body, primary, null, "Resta iscritto") == Choice.Primary;
        // L'indirizzo iscritto alla lista è quello a cui il messaggio era scritto: un alias dell'account, se è il caso (T-11).
        var (identity, subscribed) = Unsubscribe.Recipient(from.Account, entry.Summary, entry.Body);
        if (action.Kind != UnsubscribeKind.OneClick)
        {
            var (heading, body, primary) = Unsubscribe.Confirmation(action, sender, subscribed);
            if (!Confirm(heading, body, primary)) return;
            Log.Write($"annulla iscrizione: {(action.Kind == UnsubscribeKind.Mailto ? "scrittura già compilata" : "pagina del mittente nel browser")}");
            if (action.Kind == UnsubscribeKind.Mailto) MailtoRequested?.Invoke(from, new Uri(action.Target), identity);
            else OpenLink(new Uri(action.Target));
            return;
        }
        var key = from.Account.Id + "|" + OnceKey(entry.Summary);
        var outcome = await Unsubscribe.OneClickAsync(action, sender, subscribed, (heading, body, primary) =>
        {
            if (!Confirm(heading, body, primary)) return false;
            lock (unsubscribed) unsubscribed[key] = "Annullamento dell'iscrizione in corso…";
            if (entries.Contains(entry)) Render();
            return true;
        }, dryRun: MainWindow.Testing);
        if (!outcome.Confirmed) return;
        lock (unsubscribed)
        {
            if (outcome.Error == null) unsubscribed[key] = $"Richiesta di annullare l'iscrizione inviata a {action.Where}.";
            else unsubscribed.Remove(key);
        }
        if (entries.Contains(entry)) Render();
        if (outcome.Error == null)
            Dialog.Show(owner, "Iscrizione annullata", $"{action.Where} ha accettato la richiesta per {sender}. Possono passare alcuni giorni prima che i messaggi smettano di arrivare.");
        else if (Dialog.Ask(owner, "Iscrizione non annullata", outcome.Error + "\n\nPuoi provare dalla pagina del mittente, nel browser:\n" + action.Shown,
                            "Apri nel browser", null, "Chiudi") == Choice.Primary)
            OpenLink(new Uri(action.Target));
    }

    /// <summary>
    /// "Consenti l'accesso al calendario" sulla scheda di un invito di Exchange: solo qui, al clic, si apre la finestra di
    /// Microsoft per il permesso del calendario. Poi la scheda offre le risposte.
    /// </summary>
    async Task AllowCalendarAsync(IMailProvider p, Invitation invite)
    {
        try
        {
            await p.AllowCalendarAsync();
            invite.NeedsConsent = false;
            Render();
        }
        catch (Exception ex) when (MicrosoftAuth.Canceled(ex))
        {
            // annullato nella finestra di Microsoft
        }
        catch (Exception ex)
        {
            Log.Error("consenso al calendario", ex);
            ShowConsentError(Window.GetWindow(this), "Accesso al calendario non consentito", ex, p.Account, "usare il calendario");
        }
    }

    /// <summary>
    /// Un permesso in più rifiutato, spiegato. Se serve un amministratore (un account di lavoro con 65001 o 90094), la
    /// finestra offre il link del consenso e "Scrivi all'amministratore", che apre Nuovo messaggio senza inviare nulla.
    /// </summary>
    public static void ShowConsentError(Window? owner, string heading, Exception ex, MailAccount account, string purpose)
    {
        if (ex is not Microsoft.Identity.Client.MsalException)
        {
            Dialog.Show(owner, heading, ex.Message);
            return;
        }
        var error = MicrosoftAuth.Explain(ex, account.Email, MicrosoftAuth.AppOf(account).ClientId, purpose);
        if (!error.AdminConsent || account.Tenant == null)
        {
            Dialog.Show(owner, heading, error.Text);
            return;
        }
        var window = new AdminConsentWindow(heading, error.Text, account);
        if (owner is { IsVisible: true }) window.Owner = owner;
        window.ShowDialog();
        if (window.Mailto is { } mailto && Application.Current.MainWindow is MainWindow main) _ = main.OpenMailtoAsync(mailto);
    }

    void Reply(Entry entry, ComposeMode mode)
    {
        if (provider != null) ReplyRequested?.Invoke(provider, entry.Summary, mode, entry.Body);
    }

    /// <summary>
    /// Le immagini si mostrano, e da ora in poi sempre, per i mittenti dei messaggi che le avevano bloccate; per i messaggi
    /// della Posta indesiderata e negli altri casi di ImagesOnce (un .eml aperto, la fascia rossa) solo questa volta, senza
    /// ricordare il mittente.
    /// </summary>
    void ShowImages()
    {
        foreach (var entry in BlockedEntries())
            if (!RememberImages(provider?.Account, entry.Summary, entry.Body, junk, IsEml)) imagesOnce.Add(OnceKey(entry.Summary));
        Store.SaveSoon(); // una preferenza: il file (12-26 ms) non sta fra il clic e le immagini (audit del 7 ottobre, B-19)
        _ = RenderWarmAsync();
    }

    /// <summary>
    /// Ridisegno con un tema o con immagini diversi da quelli dell'apertura (il sole, "Mostra immagini", il tema di
    /// Windows): la pulizia dell'HTML dei corpi aperti si rifà prima, in sottofondo, come all'apertura (FetchBodyAsync), e
    /// il disegno la trova pronta. Prima la rifaceva il disegno sul thread dell'interfaccia: 26-45 ms a ogni sole su una
    /// newsletter da 211 KB (audit, v1 V-14 e v2 V-19).
    /// </summary>
    async Task RenderWarmAsync()
    {
        var v = version;
        bool light = Light;
        var bodies = entries.Where(e => e.Expanded && e.Body != null)
            .Select(e => (Body: e.Body!, Remote: LoadsImages(e.Summary, e.Body, junk, imagesOnce.Contains(OnceKey(e.Summary)), provider?.Account, IsEml))).ToList();
        var cleaning = Stopwatch.StartNew();
        try
        {
            await Task.Run(() =>
            {
                foreach (var (body, remote) in bodies) ReadingPage.Warm(body, light, remote);
            });
        }
        catch { } // la rifà il disegno, che mostra l'avviso
        Log.Time($"HTML pulito in sottofondo prima del ridisegno ({bodies.Sum(b => b.Body.Html.Length) / 1024} KB)", cleaning);
        if (v != version || entries.Count == 0) return; // intanto si è aperto altro, o nessuno
        Render();
    }

    // ---------------------------------------------------------------- anteprima degli allegati

    AttachmentPreview? preview;
    /// <summary>
    /// Un allegato da mostrare in anteprima appena arriva il corpo del messaggio (dalla pastiglia della riga, dal pannello
    /// File): il nome, e il messaggio a cui vale. Aprendo un altro messaggio si dimentica: prima valeva per il primo messaggio
    /// aperto dopo con un allegato dello stesso nome, che si apriva da solo.
    /// </summary>
    (IMailProvider? Provider, MessageSummary Message, string Name)? pendingPreview;
    /// <summary>
    /// I byte degli allegati visti in anteprima (sotto 5 MB, al massimo PreviewBytesMax in tutto), solo per il messaggio
    /// aperto: tornando all'anteprima, con le frecce, con Scarica o Apri non si riscaricano. Prima restavano in
    /// Attachment.Content, lo stesso oggetto dei corpi tenuti dai provider (20 per account), anche chiusa l'anteprima.
    /// </summary>
    readonly List<(Attachment Attachment, byte[] Bytes)> previewBytes = new();
    const long PreviewBytesMax = 20_000_000;
    /// <summary>Un allegato visto in anteprima si tiene solo sotto questa misura ("5 MB", in decimale).</summary>
    const int PreviewKeepBelow = 5_000_000;

    /// <summary>
    /// L'anteprima nel riquadro, come nel nuovo Outlook, con le frecce fra gli allegati che si possono vedere; WebView2 si
    /// nasconde (non si lascia coprire) e torna con "Torna al messaggio".
    /// </summary>
    async Task PreviewAsync(Entry entry, Attachment attachment)
    {
        if (provider is not { } p || entry.Body is not { } body) return;
        var list = body.Attachments.Where(a => !a.IsInline && AttachmentPreview.CanPreview(a)).ToList();
        if (preview == null)
        {
            preview = new AttachmentPreview();
            preview.BackRequested += ClosePreview;
            // Un messaggio chiesto e non ancora mostrato: la raccolta dopo la chiusura dell'anteprima aspetta.
            preview.Busy = () => entries.Count > 0 && (actionsPage == null || shownPage != actionsPage);
        }
        preview.SaveRequested = a => OpenAttachmentAsync(entry, a, save: true);
        preview.OpenRequested = a => OpenAttachmentAsync(entry, a, save: false, external: true);
        PreviewHost.Content = preview;
        PreviewHost.Visibility = Visibility.Visible;
        if (web != null) web.Visibility = Visibility.Hidden;
        Log.Write($"anteprima di {Log.Private(attachment.Name)}"); // il nome di un allegato dice di che cosa si parla (audit, S-27)
        await preview.ShowAsync(list.Select(a => new AttachmentPreview.Item(a, () => LoadForPreviewAsync(p, entry.Summary, a))).ToList(), list.IndexOf(attachment));
    }

    /// <summary>
    /// L'allegato per l'anteprima; sotto i 5 MB resta finché si guarda lo stesso messaggio (previewBytes), così tornando
    /// all'anteprima o scorrendo con le frecce non si riscarica. Quelli già in Attachment.Content (.eml, posta certificata)
    /// si usano come sono.
    /// </summary>
    async Task<byte[]> LoadForPreviewAsync(IMailProvider p, MessageSummary message, Attachment attachment)
    {
        if (attachment.Content != null) return attachment.Content;
        if (previewBytes.FirstOrDefault(b => b.Attachment == attachment).Bytes is { } kept) return kept;
        var v = version;
        var bytes = await p.GetAttachmentAsync(message, attachment);
        if (bytes.Length < PreviewKeepBelow && v == version) // non se intanto si è aperto un altro messaggio
        {
            previewBytes.Add((attachment, bytes));
            while (previewBytes.Count > 1 && previewBytes.Sum(b => (long)b.Bytes.Length) > PreviewBytesMax) previewBytes.RemoveAt(0);
        }
        return bytes;
    }

    /// <summary>I byte di un allegato: quelli già scaricati per l'anteprima, o dal provider.</summary>
    Task<byte[]> AttachmentBytesAsync(IMailProvider p, Entry entry, Attachment attachment) =>
        previewBytes.FirstOrDefault(b => b.Attachment == attachment).Bytes is { } kept ? Task.FromResult(kept) : p.GetAttachmentAsync(entry.Summary, attachment);

    public void ClosePreview()
    {
        if (PreviewHost.Visibility != Visibility.Visible) return;
        PreviewHost.Visibility = Visibility.Collapsed;
        if (web != null) web.Visibility = Visibility.Visible;
        preview?.Clear();
    }

    /// <summary>Appena il corpo del messaggio aperto arriva, l'anteprima di quell'allegato (per nome), solo per questo messaggio.</summary>
    public void PreviewWhenReady(string name)
    {
        if (Selected is not { } message) return;
        pendingPreview = (provider, message, name);
        _ = TryPendingPreviewAsync();
    }

    /// <summary>L'anteprima in attesa vale per questo messaggio (stesso account, cartella e identificativo).</summary>
    bool PendingFor(IMailProvider? p, MessageSummary? message) =>
        pendingPreview is { } pending && message != null && pending.Provider == p && pending.Message.Id == message.Id && pending.Message.FolderId == message.FolderId;

    async Task TryPendingPreviewAsync()
    {
        if (pendingPreview is not { } pending || !PendingFor(provider, Selected)
            || entries.FirstOrDefault(e => e.Summary == Selected) is not { Body: { } body } entry) return;
        pendingPreview = null;
        var name = pending.Name;
        if (body.Attachments.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } attachment)
        {
            if (AttachmentPreview.CanPreview(attachment)) await PreviewAsync(entry, attachment);
            else await OpenAttachmentAsync(entry, attachment, save: false);
        }
    }

    // ---------------------------------------------------------------- allegati e link

    /// <param name="external">Apri, dall'anteprima: con il programma di Windows anche i tipi che Missiva sa mostrare.</param>
    async Task OpenAttachmentAsync(Entry entry, Attachment attachment, bool save, bool external = false)
    {
        if (provider == null) return;
        var owner = Window.GetWindow(this);
        var name = Safety.SafeFileName(attachment.Name);
        // PDF, immagini e testi si vedono nel riquadro, senza file temporanei né altri programmi.
        if (!save && !external && AttachmentPreview.CanPreview(attachment) && entry.Body != null)
        {
            await PreviewAsync(entry, attachment);
            return;
        }
        if (!save && Safety.IsDangerous(name) &&
            Dialog.Ask(owner, "Aprire un file che può avviare programmi?",
                $"\"{name}\" è un tipo di file che può eseguire codice sul PC: è il modo più comune con cui le email infettano un computer. " +
                "Aprilo solo se lo aspettavi e ti fidi di chi lo ha mandato. Windows lo controllerà comunque prima di avviarlo.",
                "Apri comunque") != Choice.Primary)
            return;
        // Una pagina web allegata va al browser: prima una conferma sua, senza il rosso (scelte del 12 ottobre, T-10).
        if (!save && !Dialog.ConfirmOpenWebPage(owner, name)) return;
        try
        {
            // Un file firmato (.p7m: fatture elettroniche, documenti firmati) si apre in Missiva: firma verificata e contenuto.
            if (!save && P7m.IsP7m(name))
            {
                await P7mWindow.ShowAsync(owner, await AttachmentBytesAsync(provider, entry, attachment), name);
                return;
            }
            // Un messaggio allegato (.eml) si apre in Missiva, nel riquadro confinato come gli altri, non con un altro programma.
            if (!save && EmlRequested != null && (attachment.ContentType.Equals("message/rfc822", StringComparison.OrdinalIgnoreCase)
                                                  || name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)))
            {
                EmlRequested(provider, await AttachmentBytesAsync(provider, entry, attachment), name, InJunk(entry.Summary, junk));
                return;
            }
            string path;
            if (save)
            {
                var dialog = new SaveFileDialog { FileName = name };
                if (dialog.ShowDialog(owner) != true) return;
                path = dialog.FileName;
            }
            else
            {
                var dir = Path.Combine(Safety.TempDir, Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, name);
            }
            await File.WriteAllBytesAsync(path, await AttachmentBytesAsync(provider, entry, attachment));
            // Visualizzazione protetta di Office e controllo di SmartScreen. Se il marchio non si scrive (cartella temporanea
            // senza flussi alternativi) un documento di Office o un tipo pericoloso non si apre in silenzio (audit, S-10).
            if (!Safety.MarkFromInternet(path) && !save && !Dialog.ConfirmOpenUnmarked(owner, name))
            {
                try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
                return;
            }
            if (!save) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("allegato", ex);
            Dialog.Show(owner, "Allegato non disponibile", ex.Message);
        }
    }

    bool savingAll;

    /// <summary>
    /// Salva tutti, come nel nuovo Outlook: si sceglie una cartella e gli allegati ci vanno con il loro nome (con " (2)"
    /// se un file con quel nome c'è già), scaricati uno alla volta. Non i tipi che il riquadro segna in rosso (.lnk, .url,
    /// .scf, .exe...): Esplora file li legge appena apre la cartella, per esempio per l'icona di un .url su un percorso di
    /// rete, e qui nessuno ne vede il nome. Si salvano uno per uno con Salva con nome.
    /// </summary>
    async Task SaveAllAsync(Entry entry, List<Attachment> attachments)
    {
        // Il pulsante della pagina resta attivo mentre la scelta della cartella è aperta: un secondo clic ne apriva un'altra.
        if (provider == null || attachments.Count == 0 || savingAll) return;
        var owner = Window.GetWindow(this);
        var skipped = attachments.Where(a => Safety.IsDangerous(Safety.SafeFileName(a.Name))).ToList();
        var why = skipped.Count == 1
            ? $"\"{skipped[0].Name}\" è un tipo di file che può avviare programmi: se ti fidi di chi l'ha mandato, salvalo da solo con Salva con nome."
            : $"{string.Join(", ", skipped.Select(a => $"\"{a.Name}\""))} sono tipi di file che possono avviare programmi: se ti fidi di chi li ha mandati, " +
              "salvali uno per uno con Salva con nome.";
        if (skipped.Count == attachments.Count)
        {
            Dialog.Show(owner, "Allegati non salvati", why);
            return;
        }
        attachments = attachments.Except(skipped).ToList();
        var dialog = new OpenFolderDialog { Title = "Salva tutti gli allegati nella cartella" };
        savingAll = true;
        try
        {
            if (dialog.ShowDialog(owner) != true) return;
        }
        finally
        {
            savingAll = false;
        }
        try
        {
            foreach (var attachment in attachments)
            {
                var name = Safety.SafeFileName(attachment.Name);
                var path = Path.Combine(dialog.FolderName, name);
                for (int n = 2; File.Exists(path); n++)
                    path = Path.Combine(dialog.FolderName, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
                await File.WriteAllBytesAsync(path, await AttachmentBytesAsync(provider, entry, attachment));
                Safety.MarkFromInternet(path);
            }
        }
        catch (Exception ex)
        {
            Log.Error("salvataggio degli allegati", ex);
            Dialog.Show(owner, "Allegati non salvati", ex.Message);
            return;
        }
        if (skipped.Count > 0) Dialog.Show(owner, skipped.Count == 1 ? "Un allegato non salvato" : $"{skipped.Count} allegati non salvati", why);
    }

    void OpenLink(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeMailto)
        {
            if (provider != null) MailtoRequested?.Invoke(provider, uri, null);
            return;
        }
        if (uri.Scheme is not ("http" or "https")) return; // file:, ms-settings: e simili non si aprono mai da un'email
        // Se Chromium ha scritto l'indirizzo diversamente da .NET, i testi di tutti i link verso lo stesso sito.
        var texts = page.LinkTexts.GetValueOrDefault(uri.AbsoluteUri) ?? page.HostTexts.GetValueOrDefault(ReadingPage.HostKey(uri)) ?? new List<string> { "" };
        var warning = texts.Select(t => Safety.LinkWarning(t, uri)).FirstOrDefault(w => w != null);
        if (warning != null &&
            Dialog.Ask(Window.GetWindow(this), "Controlla questo link", warning + "\n\nIndirizzo reale:\n" + uri.AbsoluteUri, "Apri comunque") != Choice.Primary)
            return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("apertura link", ex); }
    }
}
