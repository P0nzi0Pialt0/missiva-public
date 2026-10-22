using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Missiva.UI;

public partial class ComposeWindow : Window
{
    readonly ComposeMode mode;
    readonly MessageSummary? original;
    readonly List<string> attachments = new();
    MessageBody? originalBody;
    bool sent;

    /// <summary>La bozza sul server che questa finestra continua (ripresa con Modifica, o salvata qui); null se non c'è.</summary>
    SavedDraft? savedDraft;
    /// <summary>L'account nelle cui Bozze sta savedDraft.</summary>
    IMailProvider? draftProvider;
    /// <summary>
    /// La bozza è stata ripresa con Modifica. Se no, quella salvata è nata in questa finestra (da sola o con Ctrl+S): Scarta
    /// la elimina, e cambiando "Da" passa all'altro account.
    /// </summary>
    readonly bool resumed;
    /// <summary>Chiusa con Scarta o Esc: la bozza nata qui esce dalle Bozze (DiscardOwnDraftAsync). La X la lascia.</summary>
    bool discarding;
    /// <summary>L'eliminazione della bozza scartata, se è partita: chiudendo tutta Missiva la si aspetta.</summary>
    Task? discardingDraft;
    /// <summary>Gli allegati della bozza che restano, come sono sul server.</summary>
    readonly List<Attachment> keptAttachments = new();
    /// <summary>Il messaggio com'era all'ultimo salvataggio o alla ripresa della bozza (State): chiudendo così, nessuna domanda.</summary>
    string? savedState;
    bool saving, closingSaved, closed;
    /// <summary>Chiusa con "Salva bozza": vero quando la finestra si è chiusa dopo il salvataggio, falso se è rimasta aperta.</summary>
    Task<bool>? savingToClose;

    /// <summary>Una bozza salvata o un messaggio inviato che continuava una bozza: l'elenco delle Bozze è cambiato.</summary>
    public event Action<IMailProvider>? DraftsChanged;

    /// <summary>
    /// Una voce del campo Da: un account con il suo indirizzo, o uno dei suoi altri indirizzi (SendIdentity: alias, "Invia
    /// come"), come gli indirizzi del Da di Outlook e le identità di Thunderbird.
    /// </summary>
    public sealed record Sender(IMailProvider Provider, SendIdentity? Identity)
    {
        public string Display => Identity == null ? Provider.Account.Email : T("{0} (da {1})", Identity.Email, Provider.Account.Email);
    }

    /// <summary>L'account scelto nel Da, con il suo provider di adesso (Current).</summary>
    IMailProvider? FromProvider => (FromBox.SelectedItem as Sender)?.Provider is { } chosen ? Current(chosen) : null;

    /// <summary>
    /// Il provider da usare adesso al posto di quello dato. Aggiungendo o rimuovendo un account Missiva chiude tutti i
    /// provider e ne crea di nuovi (MainWindow.LoadAccountsAsync): una finestra di scrittura aperta prima teneva quelli
    /// chiusi, e da lì la bozza non si salvava più e un messaggio IMAP partiva senza copia in Posta inviata (audit dell'11
    /// ottobre, X-1). Solo se quello dato è chiuso si passa a quello nuovo dello stesso account; se l'account non c'è più
    /// resta lui, che rifiuta ogni richiesta dicendolo (MailException.Closed). Per la stessa strada passano il riquadro di
    /// lettura e la barra di un messaggio aperto in una finestra sua (ReadingView.provider, MainWindow.CurrentItem).
    /// </summary>
    internal static IMailProvider Current(IMailProvider known, Func<string, IMailProvider?> now) =>
        known.IsClosed && now(known.Account.Id) is { IsClosed: false } replaced ? replaced : known;

    internal static IMailProvider Current(IMailProvider known) => Application.Current is App app ? Current(known, app.ProviderFor) : known;
    /// <summary>L'altro indirizzo scelto nel Da; null per quello dell'account.</summary>
    SendIdentity? FromIdentity => (FromBox.SelectedItem as Sender)?.Identity;
    /// <summary>La voce del Da di prima: con la bozza legata al suo account si torna lì se si sceglie un altro account.</summary>
    Sender? lastSender;

    /// <summary>
    /// Il Da si cambia fra gli account quando la bozza può passare all'altro (CanChangeFrom); fra gli indirizzi dello stesso
    /// account anche nelle risposte e nelle bozze riprese, tranne durante un salvataggio.
    /// </summary>
    bool FromChangeable => CanChangeFrom || !saving && FromProvider is { } p && p.Account.Identities.Count > 0;

    /// <param name="draft">Modifica, nelle Bozze: la bozza da continuare, con il suo corpo se è già stato letto.</param>
    /// <param name="sendAs">
    /// Un messaggio nuovo che parte da un altro indirizzo dell'account (Annulla iscrizione di una lista arrivata a un alias:
    /// audit dell'11 ottobre, T-11); null dall'indirizzo principale.
    /// </param>
    public ComposeWindow(List<IMailProvider> providers, IMailProvider from, ComposeMode mode, MessageSummary? original, MessageBody? originalBody,
                         MessageSummary? draft = null, MessageBody? draftBody = null, SendIdentity? sendAs = null)
    {
        InitializeComponent();
#if DEBUG
        // Le foto (--foto): ogni scrittura nasce fuori dallo schermo, anche una risposta (--rispondi, le foto in inglese), non
        // solo quella di --formato-finto; senza --foto non fa nulla.
        DevTools.HideForPhotos(this);
#endif
        // Il correttore ortografico si accende a finestra disegnata, non nel costruttore e non dal XAML. Dal XAML
        // (SpellCheck.IsEnabled prima del FlowDocument) WPF lo creava sul documento di partenza e lo buttava subito cambiando
        // Document: la prima volta in una sessione il rilascio dell'oggetto COM bloccava l'interfaccia per 200 ms. Creato nel
        // costruttore costa ancora 60 ms a freddo (WinRT, dizionario): dopo il primo disegno la finestra compare prima.
        ContentRendered += (_, _) => SpellCheck.SetIsEnabled(Editor, true);
        InitFormat(); // carattere, dimensione e colore dei messaggi, dalle impostazioni
        this.mode = mode;
        this.original = original;
        this.originalBody = originalBody;

        resumed = draft != null;
        // Nei messaggi nuovi tutti gli account con i loro indirizzi; nelle risposte e nelle bozze riprese solo quelli
        // dell'account del messaggio. Si risponde dall'indirizzo a cui era scritto il messaggio (un alias, se è il caso);
        // una bozza ripresa dal suo mittente.
        var senders = (mode == ComposeMode.New && draft == null ? providers : [from])
            .SelectMany(p => p.Account.Identities.Select(i => new Sender(p, i)).Prepend(new Sender(p, null))).ToList();
        var identity = draft != null ? from.Account.Identities.FirstOrDefault(i => i.Email.Equals(draft.From.Email, StringComparison.OrdinalIgnoreCase))
                     : original != null && mode != ComposeMode.Forward ? SendIdentity.For(from.Account, original, originalBody)
                     : sendAs != null && from.Account.Identities.Contains(sendAs) ? sendAs : null;
        FromBox.ItemsSource = senders;
        FromBox.SelectedItem = lastSender = senders.First(s => s.Provider == from && s.Identity == identity);
        if (identity != null) Log.Write("scrittura dall'altro indirizzo dell'account a cui era scritto il messaggio");
        // Risposte e inoltri partono dall'account del messaggio, le bozze da quello nelle cui Bozze stanno.
        FromBox.IsEnabled = FromChangeable;
        FromBox.SelectionChanged += (_, _) =>
        {
            if (FromBox.SelectedItem is not Sender chosen) return;
            if (lastSender != null && chosen.Provider != lastSender.Provider && !CanChangeFrom)
            {
                FromBox.SelectedItem = lastSender; // la bozza non può passare all'altro account adesso
                return;
            }
            lastSender = chosen;
            ChangeSignature(chosen.Provider, chosen.Identity);
            MoveDraftTo(chosen.Provider);
            UpdateExternal();
        };
        if (draft == null) InsertSignature(from, identity); // la bozza ha già la sua

        if (original != null)
        {
            SubjectBox.Text = SubjectFor(mode, original.Subject);
            initialSubject = SubjectBox.Text;
        }
        Loaded += async (_, _) =>
        {
            if (mode == ComposeMode.New || mode == ComposeMode.Forward) ToBox.Focus();
            else Editor.Focus();
            try
            {
                if (draft != null) await LoadDraftAsync(from, draft, draftBody);
                await FillRecipientsAsync(from);
            }
            finally
            {
                // Anche se un passo fallisce: Rispondi con modello aspetta questo (UseTemplateWhenReady), e prima restava ad
                // aspettare per sempre, senza inserire il modello e senza dirlo (audit dell'11 ottobre, C-33).
                recipientsReady.TrySetResult();
            }
            await ShowForwardedAsync(from);
            await ShowOriginalAsync(from);
        };
        DataObject.AddPastingHandler(Editor, OnEditorPasting);
        // Salvataggio automatico, come nel nuovo Outlook: ogni modifica fa ripartire il conto (OnAutoSaveTick). Anche il
        // testo messo da Missiva passa di qui, ma senza niente di nuovo (State, IsEmpty) non si salva nulla.
        autoSave.Tick += OnAutoSaveTick;
        Editor.TextChanged += (_, _) => Touched();
        foreach (var box in new[] { ToBox, CcBox, BccBox, SubjectBox }) box.TextChanged += (_, _) => Touched();
        FromBox.SelectionChanged += (_, _) => Touched();
        // La lista dei suggerimenti è una finestra a parte: non segue la finestra che si sposta o perde il primo piano.
        LocationChanged += (_, _) => CloseSuggestions();
        Deactivated += (_, _) => CloseSuggestions();
        Closed += (_, _) =>
        {
            closed = true;
            leaving.Cancel(); // un caricamento di allegati grandi ancora in corso (salvataggio da solo, poi Scarta) si ferma
            autoSave.Stop();
            // Chiusa da WPF mentre aspettava nella Posta in uscita (Missiva che esce): il messaggio non parte più da solo.
            if (outboxEntry is { } waiting && Outbox.Remove(waiting)) Log.Write("Posta in uscita: finestra chiusa, il messaggio non partirà");
        };
        Closing += (_, e) =>
        {
#if DEBUG
            DevTools.DumpOutgoing(FlowToHtml.Convert(Editor.Document)); // le prove vedono l'HTML che partirebbe, senza inviare
#endif
            if (sent || closingSaved) return;
            // La bozza nata qui, salvata da sola o con Ctrl+S (anche quella che si sta salvando ora): con Scarta o Esc esce
            // dalle Bozze, come nel nuovo Outlook, quindi si chiede come se non fosse mai stata salvata. La X la lascia lì.
            bool own = !resumed && (savedDraft != null || saving);
            if (discarding && own ? IsEmpty() : savedState != null ? State() == savedState : IsEmpty())
            {
                if (discarding && own) discardingDraft = DiscardOwnDraftAsync();
                return;
            }
            // Come nel nuovo Outlook quello che hai scritto non si perde chiudendo: di base va nelle Bozze. Mentre la domanda
            // è aperta il salvataggio automatico aspetta (il timer gira anche sotto una finestra modale): una bozza salvata
            // adesso resterebbe nelle Bozze anche con Scarta, che ha già deciso che la bozza non c'era.
            asking = true;
            Choice answer;
            try
            {
                answer = Dialog.Ask(this, T("Salvare la bozza?"),
                    own ? T("Il messaggio non è stato inviato. Salvalo nelle Bozze per continuarlo più tardi; Scarta elimina anche la bozza già salvata.")
                        : T("Il messaggio non è stato inviato. Salvalo nelle Bozze per continuarlo più tardi."),
                    T("Salva bozza"), T("Scarta"));
            }
            finally
            {
                asking = false;
            }
            switch (answer)
            {
                case Choice.Secondary:
                    // Scarta: le modifiche si perdono; la bozza nata qui esce dalle Bozze, quella ripresa con Modifica resta
                    // com'era sul server.
                    if (own) discardingDraft = DiscardOwnDraftAsync();
                    return;
                case Choice.Primary:
                    // Dopo: dentro Closing WPF non accetta un'altra Close (il salvataggio della prova non aspetta nulla).
                    e.Cancel = true;
                    savingToClose = Dispatcher.InvokeAsync(SaveAndCloseAsync).Task.Unwrap();
                    return;
                default:
                    e.Cancel = true;
                    return;
            }
        };
    }

    /// <summary>
    /// L'oggetto di una risposta o di un inoltro, che parte con il messaggio e quindi segue la lingua di Missiva: in
    /// italiano "R: " e "I: " come il nuovo Outlook in italiano (lo stesso testo di sempre), in inglese "RE: " e "FW: ". Un
    /// prefisso già presente non si ripete, in tutte e due le lingue e qualunque sia quella di adesso: una risposta a
    /// "RE: Offerta" resta "RE: Offerta" anche in italiano, un inoltro di "I: Offerta" resta com'è anche in inglese (così
    /// una conversazione fra le due lingue non diventa "RE: R: I: ..."). Maiuscole e minuscole non contano.
    /// </summary>
    internal static string SubjectFor(ComposeMode mode, string subject) => mode == ComposeMode.Forward
        ? StartsWithAny(subject, ForwardPrefixes) ? subject : T("I: {0}", subject)
        : StartsWithAny(subject, ReplyPrefixes) ? subject : T("R: {0}", subject);

    /// <summary>I prefissi già presenti che si riconoscono: quelli di Missiva nelle due lingue.</summary>
    static readonly string[] ReplyPrefixes = ["R:", "RE:"], ForwardPrefixes = ["I:", "FW:"];

    static bool StartsWithAny(string subject, string[] prefixes) => prefixes.Any(p => subject.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Salva bozza, poi chiude; false se la bozza non si è salvata e la finestra resta aperta.</summary>
    async Task<bool> SaveAndCloseAsync()
    {
        // Un Ctrl+S ancora in corso: finito quello, si salva solo se nel frattempo è cambiato qualcosa.
        while (saving) await Task.Delay(50);
        if (State() != savedState && !await SaveDraftAsync()) return false;
        closingSaved = true;
        Close();
        return true;
    }

    /// <summary>
    /// Si chiude la finestra di Missiva che possiede questa: WPF chiuderebbe anche questa, ma senza Closing, quindi senza
    /// "Salvare la bozza?", e il testo si perderebbe. Si chiude prima, come con la X: true se è chiusa (anche dopo aver
    /// salvato la bozza), false se resta aperta (Annulla, o bozza non salvata).
    /// </summary>
    /// <param name="keepOutbox">
    /// La finestra principale va nell'area di notifica: un messaggio nella Posta in uscita resta lì (true, e la finestra
    /// resta nascosta). Missiva esce davvero: il messaggio esce dalla Posta in uscita e la finestra si riapre con la domanda.
    /// </param>
    public async Task<bool> CloseForOwnerAsync(bool keepOutbox = false)
    {
        if (closed) return true;
        if (SendPending && sendDone is { } pendingSend)
        {
            // Annulla invio in attesa: chi chiude Missiva ha premuto Invia, quindi il messaggio parte adesso.
            SendNow();
            await pendingSend.Task;
            if (!InOutbox) return closed;
        }
        if (InOutbox)
        {
            if (keepOutbox) return true;
            LeaveOutbox(T("Non inviato: manca la rete. Salvalo nelle Bozze, o riprova quando la rete torna."));
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); // la domanda è sua: si vede di quale messaggio si parla
        savingToClose = null;
        Close();
        if (closed)
        {
            if (discardingDraft != null) await discardingDraft; // con Scarta: Missiva si chiude dopo aver tolto la bozza
            return true;
        }
        return savingToClose is { } pending && await pending;
    }

    /// <summary>
    /// Scarta, come nel nuovo Outlook: la bozza nata in questa finestra (salvata da sola o con Ctrl+S) esce dalle Bozze. Un
    /// salvataggio in corso finisce prima, e la bozza che crea esce anche lei.
    /// </summary>
    async Task DiscardOwnDraftAsync()
    {
        while (saving) await Task.Delay(50);
        if (resumed || savedDraft is not { } draft || draftProvider is not { } provider) return;
        savedDraft = null;
        await DeleteDraftAsync(provider, draft, "bozza scartata");
    }

    /// <summary>
    /// Un altro "Da" dopo che la bozza nata qui si è salvata: sta nelle Bozze dell'account di prima, quindi da lì si elimina,
    /// e il salvataggio dopo (Touched) la crea in quelle di questo.
    /// </summary>
    void MoveDraftTo(IMailProvider provider)
    {
        // Per account, non per oggetto: dopo un ricarico degli account lo stesso account ha un provider nuovo (Current).
        if (resumed || savedDraft is not { } draft || draftProvider is not { } old || old.Account.Id == provider.Account.Id) return;
        savedDraft = null;
        savedState = null;
        draftProvider = null;
        Status.Text = "";
        _ = DeleteDraftAsync(old, draft, "bozza passata a un altro account");
    }

    /// <summary>La bozza nata qui esce dalle Bozze di quell'account; se non si riesce, solo il registro lo dice.</summary>
    async Task DeleteDraftAsync(IMailProvider provider, SavedDraft draft, string why)
    {
        provider = Current(provider);
        try
        {
            await provider.DiscardDraftAsync(draft);
            Log.Write($"{why}: eliminata dalle Bozze di {provider.Account.Email}");
            DraftsChanged?.Invoke(provider);
        }
        catch (Exception ex)
        {
            Log.Error(why, ex);
        }
    }

    /// <summary>
    /// "Da" si cambia nei messaggi nuovi anche dopo che la bozza si è salvata da sola: MoveDraftTo la passa all'altro
    /// account. Non durante un salvataggio (la bozza non è ancora arrivata), non per una bozza ripresa con Modifica (resta
    /// nelle Bozze del suo account), non con allegati già sul server, che da un account all'altro non passano.
    /// </summary>
    bool CanChangeFrom => mode == ComposeMode.New && !resumed && !saving && keptAttachments.Count == 0;

    /// <summary>
    /// Modifica, nelle Bozze, come nel nuovo Outlook: la bozza riempie A, Cc, Ccn, Oggetto, testo e allegati. Il testo passa
    /// dall'HTML all'editor (HtmlToFlow); gli allegati restano sul server finché non si salva o si invia.
    /// </summary>
    async Task LoadDraftAsync(IMailProvider provider, MessageSummary draft, MessageBody? body)
    {
        Status.Text = T("Apertura della bozza…");
        try
        {
            body ??= await provider.GetBodyAsync(draft);
        }
        catch (Exception ex)
        {
            Log.Error("apertura della bozza", ex);
            Status.Text = "";
            Dialog.Show(this, T("Bozza non disponibile"), Network.Explain(ex) is { } offline
                ? T("{0} La bozza si riprende dal server, perché le modifiche fatte altrove non vadano perse: riprova quando torna la rete.", offline)
                : ex.Message);
            Close();
            return;
        }
        // Un destinatario non risolto di Outlook ha solo il nome ("elisa"): resta così, da correggere prima di salvare o inviare.
        static string Field(List<Address> list) => string.Join("; ", list.Select(a => a.Email.Length > 0 ? a.FieldText() : a.Name));
        quiet = true; // testo messo da Missiva: niente suggerimenti
        ToBox.Text = Field(body.To);
        CcBox.Text = Field(body.Cc);
        BccBox.Text = Field(body.Bcc);
        SubjectBox.Text = draft.Subject;
        quiet = false;
        if (body.Cc.Count + body.Bcc.Count > 0) OnShowCcBcc(this, new RoutedEventArgs());
        Editor.Document.Blocks.Clear();
        // Le immagini incorporate tornano immagini del testo, con il loro cid: salvando di nuovo non si ricaricano.
        var images = body.Attachments.Where(a => a.IsInline && a.ContentId != null).ToList();
        Editor.Document.Blocks.AddRange(HtmlToFlow.Blocks(body.Html, cid => images.FirstOrDefault(a => a.ContentId!.Equals(cid, StringComparison.OrdinalIgnoreCase))));
        Editor.CaretPosition = Editor.Document.ContentStart;
        draftProvider = provider;
        ShowDraft(new SavedDraft { Message = draft, Attachments = body.Attachments.Where(a => !a.IsInline).ToList(), Images = images });
        Status.Text = "";
        Log.Write($"bozza ripresa: {body.To.Count} destinatari, {keptAttachments.Count} allegati, {EditorImages.All(Editor.Document).Count()} immagini nel testo");
    }

    /// <summary>
    /// La bozza sul server è questa: i suoi allegati prendono il posto di quelli nella finestra salvati con lei (uploaded;
    /// tutti se null). Un file aggiunto mentre il salvataggio automatico era in corso resta un file nuovo, da salvare dopo.
    /// </summary>
    /// <param name="content">Testo e campi com'erano nel messaggio salvato (ContentState), se nel frattempo sono cambiati.</param>
    void ShowDraft(SavedDraft draft, IReadOnlyCollection<string>? uploaded = null, string? content = null)
    {
        savedDraft = draft;
        var added = uploaded == null ? new List<string>() : attachments.Where(f => !uploaded.Contains(f)).ToList();
        attachments.Clear();
        keptAttachments.Clear();
        AttachmentsPanel.Children.Clear();
        foreach (var a in draft.Attachments)
        {
            keptAttachments.Add(a);
            var chip = Chip(a.Size > 0 ? $"{a.Name} ({SizeText(a.Size)})" : a.Name, "", T("Togli l'allegato"));
            AutomationProperties.SetName(chip, a.Name);
            chip.Click += (_, _) =>
            {
                keptAttachments.Remove(a);
                AttachmentsPanel.Children.Remove(chip);
                FromBox.IsEnabled = FromChangeable;
                Touched();
            };
            AttachmentsPanel.Children.Add(chip);
        }
        // La bozza sta nelle Bozze di quell'account: "Da" cambia solo se la bozza può passare all'altro (CanChangeFrom).
        FromBox.IsEnabled = FromChangeable;
        savedState = (content ?? ContentState()) + "\u0001" + AttachmentState();
        foreach (var file in added)
        {
            attachments.Add(file);
            AddChip(file);
        }
    }

    /// <summary>La dimensione di un allegato della bozza, con i decimali della lingua di Missiva (Lingua.Culture).</summary>
    static string SizeText(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(Lingua.Culture) + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0", Lingua.Culture) + " KB",
        _ => (bytes / 1048576.0).ToString("0.#", Lingua.Culture) + " MB",
    };

    /// <summary>Com'è ora il messaggio: se è quello dell'ultimo salvataggio, chiudendo non c'è niente da perdere.</summary>
    string State() => ContentState() + "\u0001" + AttachmentState();

    /// <summary>Da, destinatari, oggetto e testo (con le immagini, per cid).</summary>
    string ContentState() => string.Join("\u0001", FromBox.SelectedIndex, ToBox.Text.Trim(), CcBox.Text.Trim(), BccBox.Text.Trim(), SubjectBox.Text.Trim(),
        FlowToHtml.Convert(Editor.Document));

    /// <summary>I file nuovi e gli allegati della bozza che restano.</summary>
    string AttachmentState() => string.Join("|", attachments) + "\u0001" + string.Join("|", keptAttachments.Select(a => a.Id));

    /// <summary>
    /// Salva la bozza nelle Bozze (Ctrl+S, chiudendo, o da sola), al posto di quella di prima; true se è andata. Gli
    /// indirizzi si controllano come per Invia, ma possono mancare. Nella versione di prova (MISSIVA_NO_READ) non va sul
    /// server.
    /// </summary>
    /// <param name="auto">
    /// Salvataggio automatico (OnAutoSaveTick, fine della sessione di Windows): nessuna finestra di errore, e un indirizzo
    /// non ancora finito o sbagliato resta fuori dalla bozza invece di fermare il salvataggio.
    /// </param>
    async Task<bool> SaveDraftAsync(bool auto = false)
    {
        if (saving || FromProvider is not { } provider || Message(provider, quiet: auto) is not { } message) return false;
        saving = true;
        FromBox.IsEnabled = false; // la bozza va nelle Bozze di questo account: "Da" non cambia finché non è arrivata
        // Com'era il messaggio salvato: quello che si scrive mentre la bozza va sul server resta da salvare.
        var content = ContentState();
        Status.Text = T("Salvataggio della bozza…");
        try
        {
#if DEBUG || PROVA
            if (Environment.GetEnvironmentVariable("MISSIVA_NO_READ") == "1")
            {
                // Prove sulla casella vera: la bozza va solo nel registro (e con MISSIVA_DUMP nel file alla chiusura).
                Log.Write($"prova: salverei la bozza \"{message.Subject}\" di {provider.Account.Email} per {message.To.Count + message.Cc.Count + message.Bcc.Count} destinatari, " +
                          $"{message.KeptAttachments.Count} allegati della bozza e {message.AttachmentPaths.Count} nuovi, {message.Images.Count} immagini incorporate, " +
                          $"{message.Html.Length} caratteri di HTML" +
                          (message.Draft != null ? ", al posto della bozza di prima" : "") + (auto ? ", salvataggio automatico" : ""));
                savedState = content + "\u0001" + AttachmentState();
                // Come dopo un salvataggio vero (ShowDraft), con una bozza finta: Scarta e un altro "Da" la eliminano solo
                // nel registro (TestingProvider, che nella prova avvolge ogni provider).
                savedDraft ??= new SavedDraft { Message = new MessageSummary { Id = "prova", FolderId = "prova", Subject = message.Subject, IsDraft = true } };
                draftProvider = provider;
                Status.Text = T("Bozza salvata alle {0} (versione di prova: solo nel registro)", Pec.Time(DateTime.Now));
                Saved();
                return true;
            }
#endif
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var saved = await provider.SaveDraftAsync(message);
            Log.Time(auto ? "bozza salvata da sola" : "bozza salvata", clock);
            draftProvider = provider;
            ShowDraft(saved, message.AttachmentPaths, content); // i file salvati ora sono allegati della bozza sul server
            Status.Text = T("Bozza salvata alle {0}", Pec.Time(DateTime.Now));
            Saved();
            DraftsChanged?.Invoke(provider);
            return true;
        }
        catch (OperationCanceledException) when (closed)
        {
            Log.Write("salvataggio della bozza interrotto: la finestra di scrittura è stata chiusa mentre gli allegati si caricavano");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(auto ? "salvataggio automatico della bozza" : "salvataggio della bozza", ex);
            if (auto)
            {
                Status.Text = T("Bozza non salvata: nuovo tentativo fra poco");
                autoFailed = true;
                return false;
            }
            Status.Text = "";
            Dialog.Show(this, T("Bozza non salvata"), ex.Message);
            return false;
        }
        finally
        {
            saving = false;
            FromBox.IsEnabled = FromChangeable;
        }
    }

    // ---------------------------------------------------------------- salvataggio automatico

    /// <summary>
    /// Come nel nuovo Outlook la bozza si salva da sola, così un blocco di Missiva o un riavvio di Windows non fanno perdere
    /// il testo: 10 s dopo l'ultima modifica, o dopo un minuto se si continua a scrivere. In Gmail e negli account IMAP,
    /// con più di 5 MB di allegati (che ogni salvataggio riscarica e riscrive, SaveDraftAsync), al massimo ogni 5 minuti;
    /// dopo un salvataggio non riuscito si riprova dopo un minuto.
    /// </summary>
    static readonly TimeSpan AutoSaveIdle = TimeSpan.FromSeconds(10), AutoSaveBusy = TimeSpan.FromMinutes(1),
                             AutoSaveHeavy = TimeSpan.FromMinutes(5), AutoSaveRetry = TimeSpan.FromMinutes(1);
    readonly System.Windows.Threading.DispatcherTimer autoSave = new() { Interval = TimeSpan.FromSeconds(1) };
    /// <summary>L'ultima modifica non ancora salvata e la prima (per il minuto di chi continua a scrivere); null se non ce ne sono.</summary>
    DateTime? changedAt, pendingSince;
    DateTime lastAutoSave = DateTime.MinValue;
    bool autoFailed;
    /// <summary>La domanda "Salvare la bozza?" della chiusura è aperta.</summary>
    bool asking;

    /// <summary>Qualcosa è cambiato: il conto del salvataggio automatico riparte.</summary>
    void Touched()
    {
        if (closed || sent) return;
        changedAt = DateTime.UtcNow;
        pendingSince ??= changedAt;
        if (!autoSave.IsEnabled) autoSave.Start();
    }

    /// <summary>La finestra torna dalla Posta in uscita: se c'era qualcosa da salvare, il conto del salvataggio automatico riprende.</summary>
    void ResumeAutoSave()
    {
        if (changedAt != null && !closed && !sent && !autoSave.IsEnabled) autoSave.Start();
    }

    /// <summary>Salvata (a mano o da sola): se nel frattempo non si è scritto altro, non c'è più niente in attesa.</summary>
    void Saved()
    {
        autoFailed = false;
        if (State() != savedState) return;
        changedAt = pendingSince = null;
        autoSave.Stop();
    }

    async void OnAutoSaveTick(object? sender, EventArgs e)
    {
        // Non mentre si salva o si invia (la finestra è spenta durante l'invio), né con la domanda della chiusura aperta.
        if (saving || asking || closed || sent || closingSaved || !IsEnabled || changedAt is not { } changed || FromProvider is not { } provider) return;
        var now = DateTime.UtcNow;
        if (now - changed < AutoSaveIdle && now - pendingSince < AutoSaveBusy) return;
        if (now - lastAutoSave < (autoFailed ? AutoSaveRetry : Heavy(provider) ? AutoSaveHeavy : TimeSpan.Zero)) return;
        changedAt = pendingSince = null;
        autoSave.Stop();
        if (savedState != null ? State() == savedState : IsEmpty()) return; // niente di nuovo da salvare
        lastAutoSave = now;
        if (!await SaveDraftAsync(auto: true) && !closed)
        {
            // Non riuscito (rete, indirizzo non valido): si riprova, al più presto dopo AutoSaveRetry.
            changedAt ??= now;
            pendingSince ??= now;
            autoSave.Start();
        }
    }

    /// <summary>
    /// Gmail e gli account IMAP riscrivono la bozza intera a ogni salvataggio (APPEND): con allegati grandi, o foto grandi
    /// nel testo (partono con i byte originali, EditorImages.FromFile), si salva più di rado. Per IMAP dall'audit (v2 V-06).
    /// </summary>
    bool Heavy(IMailProvider provider) => provider.Account.Kind is AccountKind.Gmail or AccountKind.Imap && AttachedBytes() > HeavyBytes;

    /// <summary>
    /// Oltre questo peso di allegati e foto, tutti insieme, la bozza è "pesante". Vale 5 MB come MailLimits.KeepAttachment,
    /// ma non è lo stesso numero: quello è la misura di un allegato solo, sotto la quale resta in memoria.
    /// </summary>
    const long HeavyBytes = 5 * 1024 * 1024;

    /// <summary>Quanto pesano gli allegati della bozza, i file nuovi e le immagini del testo.</summary>
    long AttachedBytes()
    {
        long size = keptAttachments.Sum(a => a.Size);
        foreach (var file in attachments)
        {
            try { size += new FileInfo(file).Length; }
            catch (IOException) { }
        }
        foreach (var image in EditorImages.All(Editor.Document)) size += EditorImages.Of(image)?.Content.Length ?? 0;
        return size;
    }

    /// <summary>
    /// Windows sta per chiudere la sessione (riavvio per gli aggiornamenti, spegnimento): la bozza si salva adesso, senza
    /// domande, se c'è qualcosa di nuovo. True se non c'era niente da salvare o se è salvata.
    /// </summary>
    public async Task<bool> SaveForSessionEndAsync()
    {
        if (SendPending && sendDone is { } pendingSend)
        {
            SendNow(); // Annulla invio in attesa: il messaggio parte prima che Windows chiuda
            await pendingSend.Task;
            if (!InOutbox) return true;
        }
        // Nella Posta in uscita (senza rete): la bozza si prova a salvare, come per le altre scritture aperte.
        if (InOutbox) LeaveOutbox("", show: false);
        while (saving) await Task.Delay(50);
        if (closed || sent || (savedState != null ? State() == savedState : IsEmpty())) return true;
        return await SaveDraftAsync(auto: true);
    }

    /// <summary>
    /// Da un link mailto: (mailto:indirizzo?cc=...&amp;subject=...&amp;body=...), in un'email o da Windows quando Missiva è
    /// l'app di posta: A, Cc, Ccn, oggetto e testo (Mailto, RFC 6068), sopra la firma.
    /// </summary>
    public void FillFromMailto(string link)
    {
        var mailto = Mailto.Parse(link);
        quiet = true; // testo messo da Missiva: niente suggerimenti
        ToBox.Text = initialTo = string.Join("; ", mailto.To);
        CcBox.Text = initialCc = string.Join("; ", mailto.Cc);
        BccBox.Text = initialBcc = string.Join("; ", mailto.Bcc);
        SubjectBox.Text = initialSubject = mailto.Subject;
        quiet = false;
        if (mailto.Cc.Count + mailto.Bcc.Count > 0) OnShowCcBcc(this, new RoutedEventArgs());
        if (mailto.Body.Length > 0)
        {
            initialText = mailto.Body.Trim(); // messo dal link, come i destinatari: chiudendo senza toccarlo non si chiede nulla
            var first = Editor.Document.Blocks.FirstBlock;
            foreach (var line in mailto.Body.Split('\n'))
            {
                var paragraph = new Paragraph(new Run(line));
                if (first != null) Editor.Document.Blocks.InsertBefore(first, paragraph);
                else Editor.Document.Blocks.Add(paragraph);
            }
        }
        Log.Write($"scrittura da un link mailto: {mailto.To.Count} in A, {mailto.Cc.Count} in Cc, {mailto.Bcc.Count} in Ccn, " +
                  $"oggetto di {mailto.Subject.Length} caratteri, testo di {mailto.Body.Length}");
    }

    /// <summary>
    /// Il testo scritto altrove (la casella di Rispondi di una notifica, quando la risposta non è partita), sopra la firma,
    /// con il perché accanto ai pulsanti. È testo di chi scrive, non di Missiva: chiudendo si chiede se salvare la bozza.
    /// </summary>
    // ---------------------------------------------------------------- modelli

    /// <summary>Destinatari e bozza al loro posto (Rispondi con modello aspetta questo per {nome}).</summary>
    readonly TaskCompletionSource recipientsReady = new();

    void OnTemplates(object sender, RoutedEventArgs e) => ShowTemplates();

    /// <summary>Il pannello dei modelli sotto il pulsante: ricerca per nome, oggetto e testo, poi clic o Invio per inserire.</summary>
    void ShowTemplates()
    {
        TemplateSearch.Text = "";
        FillTemplates();
        TemplatesPopup.IsOpen = true;
        TemplateSearch.Focus();
        Log.Write($"scrittura: pannello dei modelli ({Templates.For(FromProvider?.Account).Count} modelli)");
    }

    void FillTemplates()
    {
        var words = TemplateSearch.Text.Trim();
        var list = Templates.For(FromProvider?.Account)
            .Where(t => words.Length == 0 || t.Name.Contains(words, StringComparison.CurrentCultureIgnoreCase) || t.Subject.Contains(words, StringComparison.CurrentCultureIgnoreCase)
                        || Templates.PlainText(t).Contains(words, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        TemplateList.ItemsSource = list;
        // I modelli che su questo PC non si decifrano non sono in elenco (Templates.For): la nota lo dice, e dove sono.
        TemplatesNote.Text = Templates.PanelNote(Store.Settings.Templates.Count, list.Count, Templates.Hidden(FromProvider?.Account), words.Length > 0);
        TemplatesNote.Visibility = TemplatesNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnTemplateSearch(object sender, TextChangedEventArgs e) => FillTemplates();

    void OnTemplateSearchKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && TemplateList.Items.Count > 0)
        {
            e.Handled = true;
            UseTemplate((MailTemplate)(TemplateList.SelectedItem ?? TemplateList.Items[0]));
        }
        else if (e.Key == System.Windows.Input.Key.Down && TemplateList.Items.Count > 0)
        {
            e.Handled = true;
            TemplateList.SelectedIndex = 0;
            (TemplateList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            TemplatesPopup.IsOpen = false;
        }
    }

    void OnTemplateClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: MailTemplate t }) UseTemplate(t);
    }

    void OnTemplateKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is ListBoxItem { DataContext: MailTemplate t })
        {
            e.Handled = true;
            UseTemplate(t);
        }
    }

    /// <summary>
    /// Il modello al cursore, come Outlook: il suo testo (con {nome} del primo destinatario e {data}) nel paragrafo del
    /// cursore, o al suo posto se è vuoto; l'oggetto solo se è ancora vuoto. Conta come scritto: chiudendo si chiede.
    /// </summary>
    public void UseTemplate(MailTemplate template)
    {
        TemplatesPopup.IsOpen = false;
        var first = (Parse(ToBox.Text, out _, skipInvalid: true) ?? new()).FirstOrDefault();
        var name = Templates.FirstName(first);
        // Un modello che su questo PC non si decifra più (impostazioni di un altro utente di Windows): non inserisce nulla, e
        // lo si dice. Prima il testo "non leggibile" diventava l'oggetto e il testo del messaggio (audit dell'11 ottobre, T-21).
        if (Templates.Unreadable(template))
            Dialog.Show(this, T("Questo modello su questo PC non si legge"),
                T("Il modello è cifrato per un altro utente di Windows, e qui non si riesce a decifrarlo: le parti che non si leggono non vengono inserite. Con l'utente di Windows con cui è stato scritto si legge ancora; altrimenti eliminalo e riscrivilo in Impostazioni, Posta, Modelli."));
        var blocks = Templates.Blocks(template, name, DateTime.Today);
        var subject = Templates.Subject(template, name, DateTime.Today);
        if (SubjectBox.Text.Trim().Length == 0 && subject.Length > 0) SubjectBox.Text = subject;
        // Il blocco del primo livello che contiene il cursore (un paragrafo, o l'elenco in cui sta): lì va il modello.
        Block? anchor = Editor.CaretPosition.Paragraph;
        while (anchor != null && anchor.Parent is not FlowDocument) anchor = anchor.Parent as Block;
        if (anchor == null || anchor == signature) anchor = Editor.Document.Blocks.FirstBlock;
        bool replace = anchor is Paragraph p && new TextRange(p.ContentStart, p.ContentEnd).Text.Length == 0;
        Block? last = null;
        foreach (var block in blocks)
        {
            if (anchor == null) Editor.Document.Blocks.Add(block);
            else if (replace || anchor == signature) Editor.Document.Blocks.InsertBefore(anchor, block);
            else Editor.Document.Blocks.InsertAfter(last ?? anchor, block);
            last = block;
        }
        if (replace && anchor != null && blocks.Count > 0) Editor.Document.Blocks.Remove(anchor);
        if (last != null) Editor.CaretPosition = last.ContentEnd;
        Editor.Focus();
        Log.Write($"scrittura: modello inserito ({blocks.Count} paragrafi{(name == null && template.Rtf.Contains("{nome}") ? ", {nome} senza il nome del destinatario" : "")})");
    }

    /// <summary>Rispondi con modello (menu del messaggio): quando i destinatari sono al loro posto, il modello al cursore.</summary>
    public async void UseTemplateWhenReady(MailTemplate template)
    {
        await recipientsReady.Task;
        UseTemplate(template);
    }

    /// <summary>Salva come modello: il testo scritto (senza la firma) e l'oggetto, con un nome, per l'account del Da.</summary>
    void OnSaveTemplate(object sender, RoutedEventArgs e)
    {
        TemplatesPopup.IsOpen = false;
        var range = new TextRange(Editor.Document.ContentStart, signatureLine != null && Editor.Document.Blocks.Contains(signatureLine) ? signatureLine.ContentStart : Editor.Document.ContentEnd);
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Rtf);
        var rtf = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        if (range.Text.Trim().Length == 0)
        {
            Dialog.Show(this, T("Salva come modello"), T("Scrivi prima il testo del modello."));
            return;
        }
        var ask = new NameWindow(T("Salva come modello"), SubjectBox.Text.Trim(), T("Salva"),
            T("Il testo scritto finora (senza la firma) e l'oggetto diventano un modello. {nome} diventa il nome del primo destinatario, {data} la data di oggi.")) { Owner = this };
        if (ask.ShowDialog() != true) return;
        Store.Settings.Templates.Add(new MailTemplate { Name = ask.Value, Subject = SubjectBox.Text.Trim(), Rtf = rtf, AccountId = null });
        Store.Save();
        Status.Text = T("Modello salvato");
        Log.Write("scrittura: modello salvato");
    }

    public void PutText(string text, string note)
    {
        var first = Editor.Document.Blocks.FirstBlock;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var paragraph = new Paragraph(new Run(line));
            if (first != null) Editor.Document.Blocks.InsertBefore(first, paragraph);
            else Editor.Document.Blocks.Add(paragraph);
        }
        Status.Text = note;
        Log.Write($"scrittura con il testo di una notifica ({text.Length} caratteri)");
    }

    async Task FillRecipientsAsync(IMailProvider provider)
    {
        if (original == null || mode == ComposeMode.Forward) return;
        var me = provider.Account.Email;
        // Come Outlook e Thunderbird, le risposte vanno a Reply-To se c'è (moduli dei siti, ticket, marketplace), non al mittente.
        var (replyTo, _) = Replies.Recipients(original, null, me, mode);
        quiet = true; // testo messo da Missiva: niente suggerimenti
        ToBox.Text = initialTo = string.Join("; ", replyTo.Select(a => a.FieldText()));
        quiet = false;
        Log.Write($"risposta: A {(original.ReplyTo.Count > 0 ? $"Reply-To ({replyTo.Count} indirizzi)" : "il mittente")}");
        if (mode != ComposeMode.ReplyAll) return;
        try
        {
            originalBody ??= await provider.GetBodyAsync(original);
            // Senza gli altri indirizzi dell'account (alias): non si scrive a sé stessi.
            var others = Replies.Recipients(original, originalBody, me, mode).Cc
                .Where(a => !provider.Account.Identities.Any(i => i.Email.Equals(a.Email, StringComparison.OrdinalIgnoreCase))).Select(a => a.FieldText()).ToList();
            if (others.Count > 0)
            {
                quiet = true;
                CcBox.Text = initialCc = string.Join("; ", others);
                quiet = false;
                OnShowCcBcc(this, new RoutedEventArgs());
            }
        }
        catch (Exception ex)
        {
            Log.Error("destinatari di rispondi a tutti", ex);
        }
    }

    /// <summary>Destinatari, oggetto e testo come li ha messi Missiva (risposte, inoltri, mailto:): toccarli è scrivere qualcosa.</summary>
    string initialTo = "", initialCc = "", initialBcc = "", initialSubject = "", initialText = "";

    /// <summary>
    /// Niente da perdere chiudendo, come nel nuovo Outlook: destinatari e oggetto come li ha messi Missiva, nessun allegato
    /// e nessun testo scritto (la firma messa da Missiva non conta, nemmeno il suo logo). Una risposta non toccata si
    /// chiude senza domande, e non si salva da sola.
    /// </summary>
    bool IsEmpty()
    {
        var doc = Editor.Document;
        bool untouched = SignatureUntouched;
        var text = untouched
            ? new TextRange(doc.ContentStart, signature!.ElementStart).Text + new TextRange(signature.ElementEnd, doc.ContentEnd).Text
            : Editor.PlainText;
        // Le immagini della firma (RichEditor la tiene in RTF, immagini comprese) non sono scritte da chi scrive.
        var images = EditorImages.All(doc);
        if (untouched) images = images.Except(EditorImages.All(signature!.Blocks));
        return ToBox.Text.Trim() == initialTo.Trim() && CcBox.Text.Trim() == initialCc.Trim() && BccBox.Text.Trim() == initialBcc.Trim() &&
               SubjectBox.Text.Trim() == initialSubject.Trim() && attachments.Count == 0 && keptAttachments.Count == 0 &&
               text.Replace("\r\n", "\n").Trim() == initialText.Trim() && !images.Any();
    }

    // ---------------------------------------------------------------- messaggio originale

    /// <summary>
    /// Sotto il corpo, come nel nuovo Outlook: Da, Inviato, A, Cc, Oggetto e il testo del messaggio a cui si risponde o
    /// che si inoltra, cioè quello che il provider citerà. Testo semplice dall'HTML già letto (MailHtml.ToText): niente
    /// secondo WebView2 nella finestra di scrittura.
    /// </summary>
    async Task ShowOriginalAsync(IMailProvider provider)
    {
        if (original == null || mode == ComposeMode.New) return;
        try
        {
            originalBody ??= await provider.GetBodyAsync(original);
        }
        catch (Exception ex)
        {
            Log.Error("messaggio originale nella scrittura", ex);
            return;
        }
        void Line(string label, string value)
        {
            if (value.Length > 0) OriginalHeader.Children.Add(new TextBlock { Text = $"{label}: {value}", TextWrapping = TextWrapping.Wrap });
        }
        Line(T("Da"), original.From.ToString());
        Line(T("Inviato"), MimeOptions.QuoteDate(original.Date));
        Line(T("A"), string.Join("; ", originalBody.To));
        Line("Cc", string.Join("; ", originalBody.Cc));
        Line(T("Oggetto"), original.Subject);
        // Il testo dell'originale (un'analisi intera dell'HTML: 26 ms per una newsletter da 211 KB) in sottofondo, non sul
        // thread dell'interfaccia mentre la finestra si apre (audit, v2 V-09).
        var reading = System.Diagnostics.Stopwatch.StartNew();
        var html = originalBody.Html;
        int ui = Environment.CurrentManagedThreadId;
        var (text, worker) = await Task.Run(() => (MailHtml.ReadableText(html), Environment.CurrentManagedThreadId));
        Log.Time($"scrittura, testo dell'originale ({html.Length / 1024} KB) {(worker == ui ? "sul thread dell'interfaccia" : "in sottofondo")}", reading);
        OriginalText.Text = text;
        OriginalPanel.Visibility = Visibility.Visible;
        FitOriginal();
        SizeChanged += (_, _) => FitOriginal();
    }

    /// <summary>L'originale non prende più di un terzo abbondante della finestra (poco meno di metà, aperto tutto): il resto scorre.</summary>
    void FitOriginal() => OriginalScroll.MaxHeight = Math.Max(120, ActualHeight * (double.IsInfinity(OriginalClip.MaxHeight) ? 0.45 : 0.35));

    /// <summary>
    /// "Mostra tutto" solo se il testo non sta nelle dieci righe. L'altezza si misura a parte: WPF taglia DesiredSize
    /// allo spazio che il riquadro gli dà.
    /// </summary>
    void OnOriginalSize(object sender, SizeChangedEventArgs e)
    {
        if (double.IsInfinity(OriginalClip.MaxHeight) || OriginalClip.ActualWidth <= 0) return;
        var t = OriginalText;
        var formatted = new System.Windows.Media.FormattedText(t.Text, Lingua.Culture, FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize,
            System.Windows.Media.Brushes.Black, System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = OriginalClip.ActualWidth,
            LineHeight = t.LineHeight,
        };
        ShowAllButton.Visibility = formatted.Height > OriginalClip.MaxHeight + 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Tutto l'originale, che scorre per conto suo.</summary>
    void OnShowAll(object sender, RoutedEventArgs e)
    {
        OriginalClip.MaxHeight = double.PositiveInfinity;
        ShowAllButton.Visibility = Visibility.Collapsed;
        FitOriginal();
    }

    // ---------------------------------------------------------------- firma

    /// <summary>La firma messa da Missiva e la riga vuota in più aggiunta con lei, finché restano nel documento.</summary>
    Section? signature;
    Paragraph? signatureLine;
    string signatureText = "";
    /// <summary>
    /// "Nessuna firma" scelta a mano dal pulsante Firma: cambiando il Da il messaggio resta senza firma. Prima la firma
    /// predefinita dell'altro indirizzo tornava da sola (audit dell'11 ottobre, C-52).
    /// </summary>
    bool noSignature;

    bool SignatureUntouched =>
        signature != null && Editor.Document.Blocks.Contains(signature) &&
        new TextRange(signature.ContentStart, signature.ContentEnd).Text == signatureText;

    /// <summary>
    /// Come nel nuovo Outlook: la firma dell'account dopo due righe vuote, dove si comincia a scrivere; nelle risposte e
    /// negli inoltri resta sopra la citazione, che il provider aggiunge sotto al testo quando invia.
    /// </summary>
    void InsertSignature(IMailProvider provider, SendIdentity? identity)
    {
        // La firma predefinita dell'indirizzo per i nuovi messaggi o per le risposte e gli inoltri (Impostazioni, Firme).
        bool reply = mode != ComposeMode.New;
        if (Store.Settings.SignatureFor(provider.Account, reply, identity) is not { } s) return;
        PutSignature(s.RtfFor(reply));
        Editor.CaretPosition = Editor.Document.ContentStart;
    }

    /// <summary>La firma in fondo al testo, dopo una riga vuota, ricordata per cambiarla con il Da o con il pulsante Firma.</summary>
    void PutSignature(string rtf)
    {
        var blocks = RichEditor.BlocksFromRtf(rtf);
        if (blocks.Count == 0) return;
        signature = new Section();
        signature.Blocks.AddRange(blocks);
        signatureLine = new Paragraph();
        Editor.Document.Blocks.Add(signatureLine);
        Editor.Document.Blocks.Add(signature);
        signatureText = new TextRange(signature.ContentStart, signature.ContentEnd).Text;
    }

    /// <summary>
    /// Se cambiando "Da" la firma diventa quella dell'altro account o indirizzo: non dopo "Nessuna firma" scelta a mano, e
    /// non se la firma messa da Missiva è stata toccata o tolta.
    /// </summary>
    internal static bool FollowsFrom(bool noneChosen, bool hasSignature, bool untouched) => !noneChosen && (!hasSignature || untouched);

    /// <summary>Cambiando "Da", la firma diventa quella dell'altro account o indirizzo, se non è stata toccata (FollowsFrom).</summary>
    void ChangeSignature(IMailProvider provider, SendIdentity? identity)
    {
        if (!FollowsFrom(noSignature, signature != null, SignatureUntouched)) return;
        if (signature != null)
        {
            Editor.Document.Blocks.Remove(signature);
            if (signatureLine != null && Editor.Document.Blocks.Contains(signatureLine) &&
                new TextRange(signatureLine.ContentStart, signatureLine.ContentEnd).Text.Length == 0)
                Editor.Document.Blocks.Remove(signatureLine);
            signature = null;
            signatureLine = null;
        }
        InsertSignature(provider, identity);
    }

    void OnSubjectChanged(object sender, TextChangedEventArgs e) =>
        Title = SubjectBox.Text.Trim().Length > 0 ? SubjectBox.Text.Trim() : T("Nuovo messaggio");

    void OnShowCcBcc(object sender, RoutedEventArgs e)
    {
        CcLabel.Visibility = CcBox.Visibility = BccLabel.Visibility = BccBox.Visibility = Visibility.Visible;
        CcBccButton.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- suggerimenti dei destinatari

    /// <summary>Il campo (A, Cc o Ccn) di cui la lista mostra i suggerimenti.</summary>
    TextBox? suggestFor;
    /// <summary>Testo messo da Missiva (risposte, mailto:, suggerimento scelto) o selezione spostata con le frecce: nessun suggerimento.</summary>
    bool quiet;

    /// <summary>
    /// La parte del campo che si sta scrivendo: dall'ultimo separatore al cursore; alla fine se il campo non ha il cursore
    /// o l'ha all'inizio (il testo messo da fuori, come UI Automation, lascia il cursore all'inizio).
    /// </summary>
    static (int Start, int End) TokenOf(TextBox box)
    {
        int end = box.IsKeyboardFocused && box.CaretIndex > 0 ? box.CaretIndex : box.Text.Length;
        int start = end == 0 ? 0 : box.Text.LastIndexOfAny(new[] { ';', ',' }, end - 1) + 1;
        return (start, end);
    }

    /// <summary>
    /// Come Outlook negli account di lavoro: "Stai scrivendo a persone esterne" quando fra A, Cc e Ccn c'è qualcuno fuori
    /// dal dominio dell'account (i sottodomini contano come l'organizzazione).
    /// </summary>
    void UpdateExternal()
    {
        if (FromProvider is not { } provider || !AccountTypes.IsWork(provider.Account))
        {
            ExternalBar.Visibility = Visibility.Collapsed;
            return;
        }
        var own = AccountTypes.DomainOf(provider.Account.Email);
        var outside = new[] { ToBox.Text, CcBox.Text, BccBox.Text }
            .SelectMany(text => Parse(text, out _, skipInvalid: true) ?? new())
            .Where(a => AccountTypes.DomainOf(a.Email) is { Length: > 0 } domain && !SenderCheck.SameOrganization(domain, own))
            .Select(a => a.Email).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ExternalBar.Visibility = outside.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (outside.Count > 0)
            ExternalText.Text = T("Stai scrivendo a persone esterne alla tua organizzazione: {0}.",
                                  outside.Count > 3 ? T("{0} e altri {1}", string.Join(", ", outside.Take(3)), outside.Count - 3) : string.Join(", ", outside));
    }

    void OnRecipientChanged(object sender, TextChangedEventArgs e)
    {
        UpdateExternal(); // anche per il testo messo da Missiva (risposte, mailto:)
        if (quiet) return;
        ShowSuggestions((TextBox)sender);
    }

    /// <summary>
    /// Come nel nuovo Outlook: da due lettere, fino a otto persone della rubrica (AddressBook) per nome o indirizzo, le
    /// più usate prima; si sceglie con frecce e Invio o Tab, o con un clic.
    /// </summary>
    void ShowSuggestions(TextBox box)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (start, end) = TokenOf(box);
        var token = box.Text[start..end].Trim();
        if (token.Length < 2 || token.Contains('<'))
        {
            CloseSuggestions();
            return;
        }
        // Chi è già nel campo non si propone di nuovo (la parte che si sta scrivendo non è ancora un indirizzo).
        var present = box.Text.Split(';', ',').Select(part => Parse(part, out _)?.FirstOrDefault()?.Email).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = AddressBook.Suggest(token, 8, present);
        // Account di lavoro di Microsoft 365: i colleghi della directory. Con il consenso già dato arrivano da soli dopo una
        // breve pausa; senza, in fondo "Cerca anche fra i colleghi", che lo chiede solo al clic.
        var colleagues = FromProvider is { HasPeople: true } people ? people : null;
        if (colleagues != null && peopleConsent.GetValueOrDefault(colleagues.Account.Id) != true) found.Add(ColleaguesEntry);
        if (colleagues != null && peopleConsent.GetValueOrDefault(colleagues.Account.Id) == true) _ = SearchColleaguesAsync(box, token, ask: false, pause: true);
        if (found.Count == 0)
        {
            CloseSuggestions();
            return;
        }
        // Nessuna scelta evidenziata: Invio prende la prima, e scegliere la prima con UI Automation (Select) la inserisce.
        quiet = true;
        SuggestList.ItemsSource = found;
        quiet = false;
        suggestFor = box;
        SuggestPopup.PlacementTarget = box;
        SuggestPopup.Width = Math.Min(Math.Max(box.ActualWidth, 320), 520);
        SuggestPopup.IsOpen = true;
        Dispatcher.BeginInvoke(() => Log.Time($"suggerimenti per \"{token}\" ({found.Count}) sullo schermo", clock),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    void CloseSuggestions()
    {
        SuggestPopup.IsOpen = false;
        suggestFor = null;
    }

    /// <summary>La voce in fondo ai suggerimenti degli account di lavoro: cerca fra i colleghi (me/people).</summary>
    /// <remarks>Creata alla prima richiesta, nella lingua di Missiva (Lingua.Start è già passato): una sola, confrontata per riferimento.</remarks>
    static AddressBook.Entry ColleaguesEntry => colleaguesEntry ??= new() { Name = T("Cerca anche fra i colleghi"), Email = T("nella directory di Microsoft 365 della tua organizzazione") };
    static AddressBook.Entry? colleaguesEntry;

    /// <summary>Per account: se il consenso alle persone c'è (true), manca (false) o non si sa ancora (assente).</summary>
    static readonly Dictionary<string, bool> peopleConsent = new();
    int colleaguesVersion;

    /// <summary>
    /// I colleghi di Microsoft 365 per quello che si sta scrivendo (me/people?$search), uniti ai suggerimenti senza doppioni.
    /// Con ask (il clic su "Cerca anche fra i colleghi") se il consenso manca si apre la finestra di Microsoft; un 65001
    /// spiega che serve l'amministratore (AdminConsentWindow). Nella prova nessuna finestra, solo il registro.
    /// </summary>
    async Task SearchColleaguesAsync(TextBox box, string typed, bool ask, bool pause = false)
    {
        if (FromProvider is not { HasPeople: true } provider) return;
        int mine = ++colleaguesVersion;
        if (pause) await Task.Delay(300);
        if (mine != colleaguesVersion) return; // si è scritto ancora
        List<Contact>? found;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            found = await provider.SearchPeopleAsync(typed);
            if (found == null && ask)
            {
                if (MainWindow.Testing)
                {
                    await provider.AllowPeopleAsync(); // TestingProvider: solo "prova: chiederei il consenso alle persone ..." nel registro
                    Status.Text = T("Nella versione di prova la finestra di Microsoft non si apre.");
                    return;
                }
                MicrosoftAuth.OpenOver(this);
                await provider.AllowPeopleAsync();
                found = await provider.SearchPeopleAsync(typed);
            }
            Log.Time($"suggerimenti: colleghi per \"{Log.Private(typed)}\" ({found?.Count.ToString() ?? "senza consenso"})", clock);
        }
        catch (Exception ex) when (MicrosoftAuth.Canceled(ex))
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error("colleghi di Microsoft 365", ex);
            if (ask) ReadingView.ShowConsentError(this, T("Ricerca fra i colleghi non riuscita"), ex, provider.Account, T("cercare fra i colleghi della tua organizzazione"));
            return;
        }
        peopleConsent[provider.Account.Id] = found != null;
        if (found == null || mine != colleaguesVersion || suggestFor != box) return;
        var (start, end) = TokenOf(box);
        if (!box.Text[start..end].Trim().Equals(typed, StringComparison.Ordinal)) return;
        var present = box.Text.Split(';', ',').Select(part => Parse(part, out _)?.FirstOrDefault()?.Email).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shown = AddressBook.WithColleagues(SuggestList.ItemsSource is IEnumerable<AddressBook.Entry> entries ? entries.Where(e => e != ColleaguesEntry) : [], found, present);
        quiet = true;
        SuggestList.ItemsSource = shown;
        quiet = false;
        SuggestPopup.IsOpen = shown.Count > 0;
    }

    void OnRecipientKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!SuggestPopup.IsOpen || sender != suggestFor) return;
        int count = SuggestList.Items.Count;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Down or System.Windows.Input.Key.Up:
                quiet = true;
                SuggestList.SelectedIndex = SuggestList.SelectedIndex < 0 ? (e.Key == System.Windows.Input.Key.Down ? 0 : count - 1)
                    : (SuggestList.SelectedIndex + (e.Key == System.Windows.Input.Key.Down ? 1 : count - 1)) % count;
                SuggestList.ScrollIntoView(SuggestList.SelectedItem);
                quiet = false;
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Enter or System.Windows.Input.Key.Tab when (SuggestList.SelectedItem ?? SuggestList.Items[0]) is AddressBook.Entry chosen:
                Choose(chosen);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Escape:
                CloseSuggestions();
                e.Handled = true;
                break;
        }
    }

    void OnRecipientLostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender == suggestFor && !(e.NewFocus is DependencyObject d && SuggestList.IsAncestorOf(d))) CloseSuggestions();
    }

    /// <summary>La scelta fatta con UI Automation (Select); le frecce spostano la selezione senza scegliere.</summary>
    void OnSuggestionChosen(object sender, SelectionChangedEventArgs e)
    {
        if (!quiet && SuggestPopup.IsOpen && SuggestList.SelectedItem is AddressBook.Entry chosen) Choose(chosen);
    }

    /// <summary>
    /// Il clic su una riga. Non passa dalla selezione: le righe non prendono il fuoco, che resta nel campo, e WPF seleziona
    /// al clic solo le righe che lo prendono (ListBoxItem.HandleMouseButtonDown); la riga già evidenziata con le frecce,
    /// poi, non cambierebbe selezione.
    /// </summary>
    void OnSuggestionClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SuggestPopup.IsOpen && ((FrameworkElement)sender).DataContext is AddressBook.Entry chosen) Choose(chosen);
    }

    /// <summary>
    /// Al posto di quello che si stava scrivendo va "Nome &lt;indirizzo&gt;; " (un gruppo: tutte le sue persone), con il
    /// cursore dopo. "Cerca anche fra i colleghi" invece cerca.
    /// </summary>
    void Choose(AddressBook.Entry chosen)
    {
        if (suggestFor is not { } box) return;
        var (start, end) = TokenOf(box);
        if (chosen == ColleaguesEntry)
        {
            _ = SearchColleaguesAsync(box, box.Text[start..end].Trim(), ask: true);
            return;
        }
        var text = box.Text;
        int next = text.IndexOfAny(new[] { ';', ',' }, end);
        var before = text[..start].TrimEnd();
        var after = next >= 0 ? text[(next + 1)..].TrimStart() : "";
        var inserted = (before.Length > 0 ? before + " " : "") + chosen + "; ";
        quiet = true;
        box.Text = inserted + after;
        box.CaretIndex = inserted.Length;
        quiet = false;
        CloseSuggestions();
        box.Focus();
    }

    // ---------------------------------------------------------------- formattazione

    void OnBold(object sender, RoutedEventArgs e) => Toggle(EditingCommands.ToggleBold);
    void OnItalic(object sender, RoutedEventArgs e) => Toggle(EditingCommands.ToggleItalic);
    void OnUnderline(object sender, RoutedEventArgs e) => Toggle(EditingCommands.ToggleUnderline);
    void OnBullets(object sender, RoutedEventArgs e) => Toggle(EditingCommands.ToggleBullets);
    void OnNumbers(object sender, RoutedEventArgs e) => Toggle(EditingCommands.ToggleNumbering);

    void OnLink(object sender, RoutedEventArgs e) => InsertLink();

    /// <summary>
    /// Inserisci collegamento (Ctrl+K), come nel nuovo Outlook: sul testo selezionato, o con un testo nuovo al cursore.
    /// FlowToHtml lo spedisce come &lt;a href&gt;.
    /// </summary>
    void InsertLink()
    {
        // Dentro un link già inserito (per correggerne l'indirizzo) si cambia quello, come nel nuovo Outlook: WPF non
        // accetta un link dentro un altro, e prima il testo selezionato spariva senza che il link nuovo ci fosse.
        var existing = LinkAt(Editor.Selection.Start) is { } a && a == LinkAt(Editor.Selection.End) ? a : null;
        var shown = existing != null ? new TextRange(existing.ContentStart, existing.ContentEnd).Text : Editor.Selection.Text.Trim();
        var dialog = new LinkWindow(shown, existing?.NavigateUri?.OriginalString) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Address is not { } address) return;
        if (existing != null)
        {
            existing.NavigateUri = address;
            existing.ToolTip = address.OriginalString;
            if (dialog.Text != shown)
            {
                existing.Inlines.Clear();
                existing.Inlines.Add(new Run(dialog.Text));
            }
            Editor.CaretPosition = existing.ElementEnd;
            Editor.Focus();
            return;
        }
        // Un solo passo da annullare: se WPF rifiuta il link (la selezione cominciava dentro un altro), il testo tolto torna.
        bool failed = false, deleted = false;
        Editor.BeginChange();
        try
        {
            var range = Editor.Selection;
            deleted = !range.IsEmpty;
            range.Text = "";
            var link = new Hyperlink(new Run(dialog.Text), range.Start) { NavigateUri = address, ToolTip = address.OriginalString };
            // Nell'editor il documento non è "attivo" (i link non si aprono al clic): lo stile Fluent li disegnerebbe grigi.
            link.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            Editor.CaretPosition = link.ElementEnd;
        }
        catch (Exception ex)
        {
            Log.Error("inserimento del link", ex);
            failed = true;
        }
        finally
        {
            Editor.EndChange();
        }
        if (failed && deleted) Editor.Undo();
        Editor.Focus();
    }

    /// <summary>Il link che contiene quella posizione del testo, se c'è.</summary>
    static Hyperlink? LinkAt(TextPointer position)
    {
        for (var e = position.Parent; e is TextElement element; e = element.Parent)
            if (element is Hyperlink link) return link;
        return null;
    }

    void Toggle(System.Windows.Input.RoutedUICommand command)
    {
        command.Execute(null, Editor);
        Editor.Focus();
        OnEditorSelection(this, new RoutedEventArgs());
    }

    void OnEditorSelection(object sender, RoutedEventArgs e)
    {
        var s = Editor.Selection;
        BoldButton.IsChecked = s.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w >= FontWeights.SemiBold;
        ItalicButton.IsChecked = s.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle st && st == FontStyles.Italic;
        UnderlineButton.IsChecked = s.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection d && d.Count > 0;
        SyncFormat();
    }

    // ---------------------------------------------------------------- allegati

    void OnAttach(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = T("Allega file") };
        if (dialog.ShowDialog(this) != true) return;
        AddFiles(dialog.FileNames);
    }

    /// <summary>
    /// File allegati con Allega, trascinati sulla finestra o incollati (Ctrl+V di file copiati in Esplora file), come nel
    /// nuovo Outlook. Le cartelle no; per i tipi che possono eseguire codice (Safety.IsDangerous) si chiede prima: Gmail e
    /// Outlook li bloccano, e chi li riceve spesso non pu\u00F2 aprirli.
    /// </summary>
    void AddFiles(IEnumerable<string> files)
    {
        var skipped = new List<string>();
        foreach (var file in files.Where(f => !attachments.Contains(f)))
        {
            if (!File.Exists(file))
            {
                skipped.Add(Path.GetFileName(file.TrimEnd('\\')));
                continue;
            }
            if (Safety.IsDangerous(file) &&
                Dialog.Ask(this, T("Allegare questo file?"), T("{0} è un tipo di file che può eseguire programmi: Gmail e Outlook di solito lo bloccano, e chi lo riceve potrebbe non riuscire ad aprirlo.", Path.GetFileName(file)),
                           T("Allega comunque")) != Choice.Primary)
                continue;
            // Account Microsoft: il limite di Outlook per gli allegati di un messaggio si dice subito, non dopo Invia (audit, F-15).
            if (FromProvider?.Account.Kind == AccountKind.Microsoft && AttachedBytes() + new FileInfo(file).Length > GraphProvider.MaxAttachments)
            {
                Dialog.Show(this, T("Allegati troppo grandi"), T("Con {0} gli allegati supererebbero i {1} MB che Outlook accetta per un messaggio: il file non è stato allegato. Puoi condividerlo con un link.",
                                                                 Path.GetFileName(file), GraphProvider.MaxAttachments / 1048576));
                continue;
            }
            attachments.Add(file);
            AddChip(file);
            Touched();
        }
        if (skipped.Count > 0)
            Dialog.Show(this, T("Non allegato"), T("{0}: le cartelle non si possono allegare. Comprimile prima in un file .zip.", string.Join(", ", skipped)));
        Log.Write($"allegati nella scrittura: {attachments.Count}");
    }

    /// <summary>La scheda di un file nuovo, che con la X esce dagli allegati.</summary>
    void AddChip(string file)
    {
        var chip = Chip(Path.GetFileName(file), "\uE711", T("Togli l'allegato"));
        AutomationProperties.SetName(chip, Path.GetFileName(file));
        chip.Click += (_, _) =>
        {
            attachments.Remove(file);
            AttachmentsPanel.Children.Remove(chip);
            Touched();
        };
        AttachmentsPanel.Children.Add(chip);
    }

    /// <summary>
    /// Incolla nel corpo, come nel nuovo Outlook: un'immagine (uno screenshot) diventa un'immagine del testo, che parte
    /// incorporata (FlowToHtml); dei file copiati si fanno allegati. Il resto lo incolla il RichTextBox come sempre.
    /// Prima, da solo, il RichTextBox metteva l'immagine in un blocco che non partiva, e i file non li incollava.
    /// </summary>
    void OnEditorPasting(object sender, DataObjectPastingEventArgs e)
    {
        var data = e.DataObject;
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            e.CancelCommand();
            AddFiles(files);
            return;
        }
        // Con del testo (una pagina web, Word) incolla il RichTextBox: le immagini che porta con s\u00E9 partono lo stesso.
        if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Rtf) || data.GetDataPresent(DataFormats.Xaml)) return;
        InlineImage? image = null;
        try
        {
            // Il PNG, se c'\u00E8 (Strumento di cattura), tiene la trasparenza e non si ricodifica; altrimenti la bitmap.
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png) image = new InlineImage(EditorImages.NewContentId(), T("immagine.png"), "image/png", png.ToArray());
            else if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource bitmap) image = EditorImages.FromBitmap(bitmap);
        }
        catch (Exception ex)
        {
            Log.Error("immagine dagli appunti", ex);
        }
        if (image == null) return;
        e.CancelCommand();
        InsertImage(image, null, screen: true);
    }

    /// <summary>
    /// L'immagine nel testo, al cursore (al posto della selezione) o dove \u00E8 stata lasciata. Uno screenshot si vede grande
    /// come sullo schermo (i pixel divisi per la scala di Windows), al massimo EditorImages.MaxWidth.
    /// </summary>
    void InsertImage(InlineImage data, TextPointer? at, bool screen = false)
    {
        double? width = null;
        // Se non è un'immagine lo dice EditorImages.Create.
        if (screen && EditorImages.PixelWidth(data.Content) is int pixels) width = pixels / System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (EditorImages.Create(data, width) is not { } image)
        {
            Dialog.Show(this, T("Immagine non inserita"), T("{0} non si riesce a leggere come immagine.", data.Name));
            return;
        }
        Editor.BeginChange();
        try
        {
            if (at == null && !Editor.Selection.IsEmpty) Editor.Selection.Text = "";
            var position = (at ?? Editor.Selection.Start).GetInsertionPosition(LogicalDirection.Forward);
            var container = new InlineUIContainer(image, position);
            Editor.CaretPosition = container.ElementEnd;
        }
        finally
        {
            Editor.EndChange();
        }
        Editor.Focus();
        Log.Write($"immagine nel testo: {Log.Private(data.Name)}, {data.Content.Length / 1024} KB, larga {Math.Round(image.Width)}");
    }

    /// <summary>File trascinati sulla finestra: si possono lasciare (le altre cose le gestiscono i campi e l'editor).</summary>
    void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>
    /// File lasciati sulla finestra, come nel nuovo Outlook: le immagini lasciate sul testo entrano nel testo, dove sono
    /// state lasciate; tutto il resto diventa allegato.
    /// </summary>
    void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        e.Handled = true;
        var onText = e.OriginalSource is TextElement || e.OriginalSource is System.Windows.Media.Visual v && (v == Editor || Editor.IsAncestorOf(v));
        var at = onText ? Editor.GetPositionFromPoint(e.GetPosition(Editor), true) : null;
        var others = new List<string>();
        foreach (var file in files)
        {
            if (at != null && EditorImages.IsImageFile(file) && EditorImages.FromFile(file) is { } image)
            {
                InsertImage(image, at);
                at = Editor.CaretPosition; // la prossima dopo questa
            }
            else others.Add(file);
        }
        Activate();
        // Dopo: durante il rilascio Esplora file aspetta, e le domande di AddFiles lo terrebbero fermo.
        if (others.Count > 0) Dispatcher.BeginInvoke(() => AddFiles(others));
    }

    /// <summary>Come in Outlook, Inoltra porta con s\u00E9 gli allegati dell'originale: si vedono accanto a quelli aggiunti.</summary>
    async Task ShowForwardedAsync(IMailProvider provider)
    {
        if (mode != ComposeMode.Forward || original == null) return;
        try
        {
            originalBody ??= await provider.GetBodyAsync(original);
        }
        catch (Exception ex)
        {
            Log.Error("allegati da inoltrare", ex);
            return;
        }
        foreach (var a in originalBody.Attachments.Where(a => !a.IsInline))
            AttachmentsPanel.Children.Insert(0, Chip(a.Name, "\uE723", T("Allegato del messaggio originale: viene inoltrato")));
    }

    static Button Chip(string name, string glyph, string tip)
    {
        var chip = new Button { Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(10, 4, 8, 4), ToolTip = tip };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = name, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 10,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        chip.Content = content;
        return chip;
    }

    // ---------------------------------------------------------------- invio

    /// <param name="skipInvalid">Gli indirizzi non validi si saltano (invalid dice il primo) invece di restituire null.</param>
    internal static List<Address>? Parse(string text, out string? invalid, bool skipInvalid = false)
    {
        invalid = null;
        var result = new List<Address>();
        foreach (var raw in text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = "", email = raw;
            var lt = raw.LastIndexOf('<');
            if (lt >= 0 && raw.EndsWith('>'))
            {
                name = raw[..lt].Trim().Trim('"');
                email = raw[(lt + 1)..^1].Trim();
            }
            if (!email.Contains('@') || email.Contains(' '))
            {
                invalid ??= raw;
                if (skipInvalid) continue;
                return null;
            }
            result.Add(new Address { Name = name, Email = email });
        }
        return result;
    }

    /// <summary>Il messaggio com'è nella finestra, con la bozza che continua; null (dopo l'avviso) se un indirizzo non è valido.</summary>
    /// <param name="quiet">Salvataggio automatico: gli indirizzi non validi (magari ancora da finire) restano fuori, senza avviso.</param>
    /// <param name="sendAt">Pianifica invio con Graph: l'ora che tiene Exchange.</param>
    OutgoingMessage? Message(IMailProvider provider, bool quiet = false, DateTime? sendAt = null)
    {
        var to = Parse(ToBox.Text, out var bad1, quiet);
        var cc = Parse(CcBox.Text, out var bad2, quiet);
        var bcc = Parse(BccBox.Text, out var bad3, quiet);
        if (to == null || cc == null || bcc == null)
        {
            Dialog.Show(this, T("Indirizzo non valido"), T("Controlla \"{0}\".", bad1 ?? bad2 ?? bad3));
            return null;
        }
        if (bad1 != null || bad2 != null || bad3 != null) Log.Write("salvataggio automatico: un indirizzo non valido resta fuori dalla bozza");
        var images = new List<InlineImage>();
        var html = FlowToHtml.Convert(Editor.Document, images);
        return new OutgoingMessage
        {
            Mode = mode,
            Original = original,
            OriginalBody = originalBody,
            To = to,
            Cc = cc,
            Bcc = bcc,
            Subject = SubjectBox.Text.Trim(),
            Html = html,
            Text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text.TrimEnd(),
            AttachmentPaths = attachments.ToList(),
            Images = images,
            Draft = savedDraft,
            KeptAttachments = keptAttachments.ToList(),
            Importance = importance,
            ReadReceipt = readReceipt,
            DeliveryReceipt = deliveryReceipt,
            SendAt = sendAt != null && provider.SchedulesOnServer ? sendAt : null,
            From = FromIdentity,
            Cancel = (stopSend ?? leaving).Token,
            Progress = uploadProgress ??= new Progress<string>(text =>
            {
                // Un avviso che arriva dopo la fine o dopo Annulla (gli avvisi passano dalla coda dell'interfaccia, anche dopo
                // la ripresa di chi ha annullato) non rimette "Caricamento…" nella riga di stato.
                if (closed || (stopSend == null && !saving) || stopSend is { IsCancellationRequested: true }) return;
                Status.Text = MainWindow.UploadShort(text); // i MB per primi: la riga è stretta, e un nome lungo li nasconderebbe
                // Con Annulla invio la finestra è nascosta e la riga di stato non la vede nessuno: l'avanzamento va anche nella
                // barra "Invio in corso" della finestra principale, che resta finché l'invio non è finito.
                if (sendBar is { } bar)
                {
                    SendProgress = text;
                    bar.RefreshSendBar();
                }
            }),
        };
    }

    /// <summary>La finestra si è chiusa: annulla i caricamenti lunghi ancora in corso (OutgoingMessage.Cancel).</summary>
    readonly CancellationTokenSource leaving = new();
    /// <summary>
    /// Il caricamento degli allegati grandi (account Microsoft) detto nella riga di stato, pezzo per pezzo, e durante un
    /// invio a finestra nascosta nella barra della finestra principale (sendBar).
    /// </summary>
    Progress<string>? uploadProgress;
    /// <summary>
    /// Durante Invia: annullato da Annulla sulla barra della finestra principale mentre gli allegati grandi si caricano (o
    /// dalla chiusura della finestra, come leaving): il caricamento si ferma fra un pezzo e l'altro e il messaggio non parte.
    /// </summary>
    CancellationTokenSource? stopSend;
    /// <summary>La finestra principale che mostra questo invio nella sua barra (la finestra di scrittura è nascosta).</summary>
    MainWindow? sendBar;
    /// <summary>L'ultimo avanzamento del caricamento durante l'invio a finestra nascosta ("Caricamento di ...: 3,5 di 12 MB…"), o null.</summary>
    public string? SendProgress { get; private set; }

    /// <summary>Il messaggio pronto per partire (Invia o Pianifica invio), dopo le domande; null se manca qualcosa.</summary>
    async Task<(IMailProvider Provider, OutgoingMessage Message)?> ReadyAsync(DateTime? sendAt = null)
    {
        if (!IsEnabled) return null;
        if (saving)
        {
            // La bozza si sta salvando (da sola o con Ctrl+S): si invia appena è sul server, con quella bozza. Prima Invia
            // non faceva nulla, e chi poi chiudeva la finestra lasciava nelle Bozze un messaggio che credeva partito.
            IsEnabled = false;
            Status.Text = T("Invio dopo il salvataggio della bozza…");
            while (saving) await Task.Delay(50);
            IsEnabled = true;
            if (closed) return null;
        }
        if (FromProvider is not { } provider || Message(provider, sendAt: sendAt) is not { } message) return null;
        if (message.To.Count + message.Cc.Count + message.Bcc.Count == 0)
        {
            Dialog.Show(this, T("Manca il destinatario"), T("Scrivi almeno un indirizzo nel campo A."));
            return null;
        }
        if (message.Subject.Length == 0 &&
            Dialog.Ask(this, T("Inviare senza oggetto?"), T("Il messaggio non ha un oggetto."), T("Invia comunque")) != Choice.Primary)
            return null;
        if (ForgottenAttachment(provider) is { } word)
        {
            Log.Write("invio: il testo parla di un allegato e non ce n'è nessuno, domanda");
            if (Dialog.Ask(this, T("Hai dimenticato l'allegato?"), T("Nel messaggio hai scritto \"{0}\", ma non c'è nessun allegato.", word), T("Invia comunque"), null, T("Torna al messaggio")) != Choice.Primary)
                return null;
            attachmentAsked = true; // detto una volta: Riprova e un nuovo Invia dopo Annulla invio non lo richiedono
        }
        return (provider, message);
    }

    // ---------------------------------------------------------------- allegato dimenticato

    /// <summary>"Invia comunque" già scelto in questa finestra: la domanda sull'allegato non torna.</summary>
    bool attachmentAsked;

    /// <summary>
    /// Avviso di allegato dimenticato, come nel nuovo Outlook (audit, F-14): la parola che parla di un allegato nel testo
    /// scritto qui, se non c'è nessun allegato; null se non c'è niente da chiedere. Contano come allegati i file aggiunti,
    /// quelli della bozza, quelli dell'originale di un inoltro (partono con lui finché la bozza non è salvata, poi sono fra
    /// quelli della bozza) e le immagini messe nel testo, tolte quelle della firma. L'originale citato sta fuori dal documento.
    /// </summary>
    string? ForgottenAttachment(IMailProvider provider)
    {
        if (!Store.Settings.AttachmentReminder || attachmentAsked || attachments.Count + keptAttachments.Count > 0) return null;
        if (mode == ComposeMode.Forward && savedDraft == null && (originalBody?.Attachments.Any(a => !a.IsInline) ?? original?.HasAttachments ?? false)) return null;
        var text = Editor.PlainText;
        if (AttachmentMention(text, [signatureText]) is not { } word) return null;
        var images = EditorImages.All(Editor.Document);
        if (signature != null && Editor.Document.Blocks.Contains(signature)) images = images.Except(EditorImages.All(signature.Blocks));
        if (images.Any()) return null;
        // Una bozza ripresa ha la firma nel testo, senza più il segno di dov'è: si tolgono le righe delle firme di chi scrive.
        if (!resumed || Store.Settings.SignaturesOf(provider.Account, FromIdentity) is not { } signatures) return word;
        return AttachmentMention(text, signatures.All().Select(s =>
        {
            var section = new Section();
            section.Blocks.AddRange(RichEditor.BlocksFromRtf(s.Rtf));
            return new TextRange(section.ContentStart, section.ContentEnd).Text;
        }));
    }

    /// <summary>
    /// Le parole che dicono "c'è un allegato", intere: allego, alleghiamo, allegato e le sue forme ("in allegato"), accluso,
    /// attached, attachment, attaching, enclosed. Non "allegare" né "attach" (di solito è una richiesta all'altro), e non dopo
    /// una negazione ("senza allegati", "nessun allegato", "non allego", "no attachment").
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex AttachmentWords = new(
        @"(?<![\p{L}\p{N}_])(?<!\b(?:senza|nessun|non|no|without)\s{1,3}(?:alcun\s{1,3}|any\s{1,3})?)" +
        @"(?:alleg(?:o|hiamo|ato|ata|ati|ate|ando|herò|heremo)|acclus[oaie]|attach(?:ed|ments?|ing)|enclosed)(?![\p{L}\p{N}_])",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Dove comincia un messaggio citato: l'intestazione di Missiva e di Gmail, quella di Outlook, le righe di trattini.</summary>
    static readonly System.Text.RegularExpressions.Regex QuoteStart = new(
        @"^(?:Il .{1,200} ha scritto:|On .{1,200} wrote:|-{2,} ?(?:Messaggio originale|Original Message|Messaggio inoltrato|Forwarded message) ?-{2,})$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// La prima parola di <paramref name="text"/> che parla di un allegato, o null. Non si guardano le righe delle firme
    /// (<paramref name="signatures"/>, testo semplice: "questo messaggio e i suoi allegati sono riservati"), le righe citate
    /// (">") e tutto quello che segue l'intestazione di un messaggio citato (una bozza ripresa di una risposta ha la
    /// citazione nel testo; in una risposta appena aperta sta fuori dal documento).
    /// </summary>
    internal static string? AttachmentMention(string text, IEnumerable<string> signatures)
    {
        static string Normal(string line) => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        static string[] Lines(string s) => s.Split('\n', '\r');
        var skip = signatures.SelectMany(Lines).Select(Normal).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = Lines(text).Select(Normal).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (QuoteStart.IsMatch(line)) break;
            // Outlook: "Da: ..." e subito sotto "Inviato: ..." (anche in inglese).
            if ((line.StartsWith("Da:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("From:", StringComparison.OrdinalIgnoreCase)) &&
                lines.Skip(i + 1).Take(3).Any(l => l.StartsWith("Inviato:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Sent:", StringComparison.OrdinalIgnoreCase) ||
                                                   l.StartsWith("Data:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Date:", StringComparison.OrdinalIgnoreCase)))
                break;
            if (line.StartsWith('>') || skip.Contains(line)) continue;
            if (AttachmentWords.Match(line) is { Success: true } m) return m.Value;
        }
        return null;
    }

    async void OnSend(object sender, RoutedEventArgs e)
    {
        // Prima di preparare il messaggio: il suo Cancel è questo (Message), così Annulla sulla barra ferma il caricamento.
        var stop = stopSend = CancellationTokenSource.CreateLinkedTokenSource(leaving.Token);
        try
        {
            await SendMessageAsync(stop);
        }
        finally
        {
            // Solo il proprio: Riprova, dalla finestra dell'errore, ha già cominciato un altro invio.
            if (stopSend == stop)
            {
                stopSend = null;
                EndSendBar();
            }
        }
    }

    /// <summary>L'invio a finestra nascosta è finito (partito, non riuscito o annullato): la barra della finestra principale lo toglie.</summary>
    void EndSendBar()
    {
        SendProgress = null;
        if (sendBar is not { } bar) return;
        sendBar = null;
        bar.EndUndoSend(this);
    }

    async Task SendMessageAsync(CancellationTokenSource stop)
    {
        if (await ReadyAsync() is not var (provider, message)) return;

        // Annulla invio, come nel nuovo Outlook: la finestra sparisce subito e il messaggio aspetta qualche secondo, con
        // "Invio in corso… Annulla" in fondo all'elenco; Annulla la riapre com'era.
        if (Store.Settings.UndoSendSeconds > 0 && Owner is MainWindow { IsVisible: true } main)
        {
            if (!await WaitToSendAsync(main, Store.Settings.UndoSendSeconds)) return;
        }

        // Senza rete, come l'Outlook classico: nella Posta in uscita, che parte quando la rete torna.
        if (Network.KnownOffline(provider.Account))
        {
            PutInOutbox(provider, message);
            sendDone?.TrySetResult();
            return;
        }
        IsEnabled = false;
        Status.Text = T("Invio in corso…");
        // La finestra è nascosta (Annulla invio): la barra "Invio in corso" della finestra principale resta finché l'invio non
        // è finito, e dice il caricamento degli allegati grandi. Prima spariva alla fine dell'attesa, e un invio di minuti
        // non si vedeva da nessuna parte (segnalato dall'autore il 4 ottobre 2026).
        if (!IsVisible && Owner is MainWindow shown)
        {
            sendBar = shown;
            shown.OfferUndoSend(this);
        }
        Exception? failed = null;
        try
        {
#if DEBUG
            // Prove: MISSIVA_ERRORE_INVIO=testo fa fallire ogni invio con quel testo, per vedere la finestra dell'errore.
            if (Environment.GetEnvironmentVariable("MISSIVA_ERRORE_INVIO") is { Length: > 0 } test) throw new MailException(test);
            // Prove: MISSIVA_CARICAMENTO_FINTO=MB fa precedere l'invio da un caricamento finto di tanti MB, detto pezzo per
            // pezzo come quello vero degli account Microsoft, per vedere la barra della finestra principale e il suo Annulla.
            if (int.TryParse(Environment.GetEnvironmentVariable("MISSIVA_CARICAMENTO_FINTO"), out var megabytes) && megabytes > 0)
                for (double done = 0; done < megabytes; done += 1.25)
                {
                    message.Progress?.Report(T("Caricamento di {0}: {1} di {2} MB…", "relazione-prova.zip", done.ToString("0.#", Lingua.Culture), megabytes)); // il testo di GraphProvider, anche in inglese
                    await Task.Delay(700, message.Cancel);
                }
#endif
            await provider.SendAsync(message);
            AfterSent(provider, message);
        }
        catch (OperationCanceledException) when (closed)
        {
            Log.Write("invio interrotto: la finestra di scrittura è stata chiusa mentre gli allegati si caricavano");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Annulla sulla barra durante il caricamento: il provider ha già tolto dalla bozza quello che aveva caricato.
            Log.Write("invio annullato mentre gli allegati si caricavano: la scrittura si riapre com'era");
            IsEnabled = true;
            if (!IsVisible) Show();
            Activate();
            Status.Text = T("Invio annullato");
        }
        catch (Exception ex) when (Network.IsOffline(ex))
        {
            Log.Write($"invio: {provider.Account.Email} non in linea ({ex.Message}), il messaggio va nella Posta in uscita");
            Network.Note(provider.Account, ex);
            PutInOutbox(provider, message);
        }
        catch (Exception ex)
        {
            Log.Error("invio", ex);
            Status.Text = "";
            IsEnabled = true;
            if (!IsVisible) Show(); // durante Annulla invio era nascosta
            failed = ex;
        }
        finally
        {
            sendDone?.TrySetResult();
            EndSendBar(); // prima della finestra dell'errore: l'invio non è più in corso
        }
        if (failed != null) await SendFailedAsync(provider, failed);
    }

    /// <summary>
    /// Quello che la finestra dell'errore d'invio propone: il testo, e il pulsante principale secondo il rimedio dell'errore
    /// (Aggiorna password, Accedi di nuovo) o Riprova.
    /// </summary>
    internal static (string Body, string Primary, AccountFix Fix) SendFailure(Exception ex, string prefix = "")
    {
        var fix = MailException.FixOf(ex) is var f and (AccountFix.Password or AccountFix.SignIn) ? f : AccountFix.None;
        // Una frase intera per rimedio (Lingua): in italiano lo stesso testo di prima.
        var body = fix switch
        {
            AccountFix.Password => T("{0}{1}\n\nIl messaggio è ancora qui, con destinatari, testo e allegati. Aggiorna la password dell'account e poi premi di nuovo Invia, salvarlo nelle Bozze o tornare a scriverlo.", prefix, ex.Message),
            AccountFix.SignIn => T("{0}{1}\n\nIl messaggio è ancora qui, con destinatari, testo e allegati. Rifai l'accesso all'account e poi premi di nuovo Invia, salvarlo nelle Bozze o tornare a scriverlo.", prefix, ex.Message),
            _ => T("{0}{1}\n\nIl messaggio è ancora qui, con destinatari, testo e allegati. Puoi riprovare, salvarlo nelle Bozze o tornare a scriverlo.", prefix, ex.Message),
        };
        return (body, fix switch { AccountFix.Password => T("Aggiorna password"), AccountFix.SignIn => T("Accedi di nuovo"), _ => T("Riprova") }, fix);
    }

    /// <summary>
    /// L'invio non è riuscito (subito o dalla Posta in uscita) per un motivo che non è la rete: prima la finestra aveva il
    /// solo OK, e chi non sapeva che il messaggio era ancora lì sotto lo credeva perso (audit, F-44 punto 2). Ora tre scelte:
    /// Riprova (o il rimedio dell'account: Aggiorna password, Accedi di nuovo, che apre Gestisci account), Salva nelle Bozze,
    /// Torna al messaggio. In ogni caso la scrittura resta aperta com'era.
    /// </summary>
    async Task SendFailedAsync(IMailProvider provider, Exception ex, string prefix = "")
    {
        var (body, primary, fix) = SendFailure(ex, prefix);
        // Interrotto quando il server aveva già tutto il messaggio (MailException.MaybeSent): "non inviato" non è detto.
        var choice = Dialog.Ask(this, MailException.MaybeSentOf(ex) ? T("Invio interrotto") : T("Messaggio non inviato"), body, primary, T("Salva nelle Bozze"), T("Torna al messaggio"));
        if (closed) return;
        if (choice == Choice.Secondary)
        {
            if (await SaveDraftAsync() && !closed) Status.Text = T("Non inviato: salvato nelle Bozze alle {0}", Pec.Time(DateTime.Now));
        }
        else if (choice == Choice.Primary && fix != AccountFix.None)
        {
            if (Application.Current.MainWindow is MainWindow main) await main.ManageAccountAsync(provider.Account, this, focusPassword: fix == AccountFix.Password);
            if (!closed) Status.Text = T("Non inviato: premi Invia per riprovare");
        }
        else if (choice == Choice.Primary) OnSend(this, new RoutedEventArgs());
        else Status.Text = T("Non inviato");
    }

    /// <summary>Partito (subito o dalla Posta in uscita): la rubrica, le Bozze se c'era una bozza, e la finestra si chiude.</summary>
    void AfterSent(IMailProvider provider, OutgoingMessage message)
    {
        sent = true;
        AddressBook.Sent(message.To.Concat(message.Cc).Concat(message.Bcc), DateTime.Now);
        if (savedDraft != null) DraftsChanged?.Invoke(provider); // la bozza è uscita dalle Bozze
        Close();
    }

    // ---------------------------------------------------------------- Posta in uscita

    /// <summary>Il messaggio aspetta nella Posta in uscita di Missiva (Outbox), con questa finestra nascosta e spenta.</summary>
    Outbox.Entry? outboxEntry;

    /// <summary>Inviato senza rete: parte quando la rete torna, anche con Missiva nell'area di notifica.</summary>
    public bool InOutbox => outboxEntry != null;
    /// <summary>L'account da cui il messaggio nella Posta in uscita partirà; null se non ci sta.</summary>
    public string? OutboxAccountId => outboxEntry?.AccountId;

    /// <summary>
    /// Invia senza rete: la finestra resta nascosta e spenta (niente salvataggi automatici), il messaggio va nella Posta in
    /// uscita. Partito, la finestra si chiude come dopo Invia; rifiutato dal server per un altro motivo, si riapre con il
    /// perché. La finestra passa alla finestra principale: quella di una sola cartella, chiudendosi, la chiuderebbe senza
    /// domande.
    /// </summary>
    void PutInOutbox(IMailProvider provider, OutgoingMessage message)
    {
        IsEnabled = false;
        // Nella Posta in uscita la finestra può restare ore o giorni: il timer del salvataggio automatico, che spenta non
        // salva, non resta a scattare ogni secondo (audit del 7 ottobre, B-30). Riaperta, riparte (ResumeAutoSave).
        autoSave.Stop();
        Status.Text = "";
        if (IsVisible) Hide();
        if (Application.Current.MainWindow is { } main && Owner != main && main != this) Owner = main;
        var entry = new Outbox.Entry
        {
            AccountId = provider.Account.Id,
            Message = message,
            Sent = sender =>
            {
                outboxEntry = null;
                AfterSent(sender, message);
            },
            Failed = ex =>
            {
                outboxEntry = null;
                IsEnabled = true;
                ResumeAutoSave();
                Show();
                Activate();
                _ = SendFailedAsync(provider, ex, T("Dalla Posta in uscita: "));
            },
        };
        outboxEntry = entry;
        Outbox.Add(entry, provider.Account.Email);
    }

    /// <summary>
    /// Annulla sulla barra della Posta in uscita, o Missiva che esce davvero: il messaggio esce dalla Posta in uscita e la
    /// finestra torna com'era, con il perché accanto ai pulsanti. Falso se intanto era già partito.
    /// </summary>
    public bool LeaveOutbox(string why, bool show = true)
    {
        if (outboxEntry is not { } entry || !Outbox.Remove(entry)) return false;
        outboxEntry = null;
        Log.Write("Posta in uscita: messaggio tolto, la scrittura torna aperta");
        IsEnabled = true;
        ResumeAutoSave();
        Status.Text = why;
        if (show)
        {
            Show();
            Activate();
        }
        return true;
    }

    /// <summary>Annulla invio: annullato dal pulsante della finestra principale (UndoSend) o mandato subito (SendNow).</summary>
    CancellationTokenSource? undoSend;
    TaskCompletionSource? sendNow;
    /// <summary>L'invio in attesa è finito (partito, non riuscito o annullato): chi chiude Missiva lo aspetta.</summary>
    TaskCompletionSource? sendDone;

    public bool SendPending => undoSend != null;
    public string PendingSubject => SubjectBox.Text.Trim();
    /// <summary>
    /// Annulla sulla barra "Invio in corso" serve: durante l'attesa, e dopo mentre gli allegati grandi si caricano (il
    /// provider si ferma fra un pezzo e l'altro). Non nel resto dell'invio, che non si può fermare a metà.
    /// </summary>
    public bool CanUndoSend => undoSend != null || (stopSend != null && SendProgress != null);
    /// <summary>Annulla sulla barra "Invio in corso": il messaggio non parte, la finestra torna com'era.</summary>
    public void UndoSend()
    {
        if (undoSend != null) undoSend.Cancel();
        else if (SendProgress != null) stopSend?.Cancel();
    }
    /// <summary>Missiva si chiude (o la sessione finisce) durante l'attesa: il messaggio parte subito.</summary>
    public void SendNow() => sendNow?.TrySetResult();

    /// <summary>
    /// La finestra si nasconde e il messaggio aspetta i secondi scelti (Impostazioni, Posta, Scrittura e risposta). true se
    /// deve partire, false se è stato annullato (e la finestra è di nuovo aperta, com'era).
    /// </summary>
    async Task<bool> WaitToSendAsync(MainWindow main, int seconds)
    {
        undoSend = new CancellationTokenSource();
        sendNow = new TaskCompletionSource();
        sendDone = new TaskCompletionSource();
        IsEnabled = false; // niente salvataggi automatici mentre aspetta
        Hide();
        main.OfferUndoSend(this);
        Log.Write($"invio fra {seconds} s, annullabile");
        try
        {
            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(seconds), undoSend.Token), sendNow.Task);
            if (!undoSend.IsCancellationRequested) return true;
            Log.Write("invio annullato: la scrittura si riapre com'era");
            IsEnabled = true;
            Show();
            Activate();
            Status.Text = T("Invio annullato");
            sendDone.TrySetResult();
            return false;
        }
        finally
        {
            undoSend = null;
            sendNow = null;
            main.EndUndoSend(this);
        }
    }

    // ---------------------------------------------------------------- Pianifica invio e opzioni

    /// <summary>La freccia accanto a Invia: domani alle 8, lunedì alle 8, o data e ora scelte.</summary>
    void OnSendMenu(object sender, RoutedEventArgs e)
    {
        if (FromProvider is not { } provider) return;
        var menu = new ContextMenu { PlacementTarget = SendMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = T("Pianifica invio"), IsEnabled = false, FontWeight = FontWeights.SemiBold });
        var now = DateTime.Now;
        int toMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        void Add(string text, DateTime at)
        {
            var item = new MenuItem { Header = $"{text} ({at.ToString("ddd d MMM", Lingua.Culture)})" };
            item.Click += async (_, _) => await ScheduleAsync(at);
            menu.Items.Add(item);
        }
        Add(T("Domani alle 8:00"), now.Date.AddDays(1).AddHours(8));
        Add(T("Lunedì alle 8:00"), now.Date.AddDays(toMonday == 0 ? 7 : toMonday).AddHours(8));
        var custom = new MenuItem { Header = T("Scegli data e ora…") };
        custom.Click += async (_, _) =>
        {
            var window = new ScheduleWindow { Owner = this };
            if (window.ShowDialog() == true) await ScheduleAsync(window.At);
        };
        menu.Items.Add(custom);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            IsEnabled = false,
            Header = new TextBlock
            {
                MaxWidth = 300, TextWrapping = TextWrapping.Wrap,
                Text = provider.SchedulesOnServer
                    ? T("Outlook tiene il messaggio nella Posta in uscita e lo invia all'ora scelta, anche con Missiva chiusa.")
                    : T("Il messaggio resta nelle Bozze e lo invia Missiva all'ora scelta, se è aperta; altrimenti appena la riapri."),
            },
        });
        menu.IsOpen = true;
    }

    /// <summary>
    /// Pianifica invio. Graph: il messaggio parte ora con PidTagDeferredSendTime (SendAt) e lo tiene Exchange. Gmail e IMAP:
    /// si salva la bozza e l'invio lo fa Missiva all'ora (ScheduledSending).
    /// </summary>
    async Task ScheduleAsync(DateTime at)
    {
        if (at <= DateTime.Now.AddMinutes(1))
        {
            Dialog.Show(this, T("Ora già passata"), T("Scegli un giorno e un'ora futuri."));
            return;
        }
        if (await ReadyAsync(at) is not var (provider, message)) return;
        IsEnabled = false;
        Status.Text = T("Pianificazione…");
        try
        {
            if (provider.SchedulesOnServer)
            {
                await provider.SendAsync(message);
                Log.Write($"invio pianificato per {at:dd/MM/yyyy HH:mm} (lo tiene Exchange)");
            }
            else
            {
                IsEnabled = true;
                if (!await SaveDraftAsync()) return;
                IsEnabled = false;
                if (savedDraft is not { } draft) return;
                ScheduledSending.Add(provider.Account, draft.Message.StableId, at, message.Subject);
            }
            sent = true;
            AddressBook.Sent(message.To.Concat(message.Cc).Concat(message.Bcc), DateTime.Now);
            if (savedDraft != null) DraftsChanged?.Invoke(provider);
            Close();
        }
        catch (OperationCanceledException) when (closed)
        {
            Log.Write("invio pianificato interrotto: la finestra di scrittura è stata chiusa mentre gli allegati si caricavano");
        }
        catch (Exception ex)
        {
            Log.Error("invio pianificato", ex);
            Dialog.Show(this, T("Invio non pianificato"), ex.Message);
        }
        finally
        {
            if (!closed)
            {
                IsEnabled = true;
                if (Status.Text == T("Pianificazione…")) Status.Text = "";
            }
        }
    }

    /// <summary>Opzioni del messaggio, come nel nuovo Outlook: importanza e conferme di lettura e di recapito.</summary>
    Importance importance;
    bool readReceipt, deliveryReceipt;

    void OnOptions(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = OptionsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        MenuItem Item(string header, bool on, Action<bool> set)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = on };
            item.Click += (_, _) =>
            {
                set(item.IsChecked);
                ShowOptions();
                Touched();
            };
            return item;
        }
        menu.Items.Add(Item(T("Importanza alta"), importance == Importance.High, on => importance = on ? Importance.High : Importance.Normal));
        menu.Items.Add(Item(T("Importanza bassa"), importance == Importance.Low, on => importance = on ? Importance.Low : Importance.Normal));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(T("Richiedi conferma di lettura"), readReceipt, on => readReceipt = on));
        menu.Items.Add(Item(T("Richiedi conferma di recapito"), deliveryReceipt, on => deliveryReceipt = on));
        menu.IsOpen = true;
    }

    /// <summary>Il pulsante Opzioni dice le opzioni scelte, come il nuovo Outlook sotto l'oggetto.</summary>
    void ShowOptions()
    {
        var parts = new List<string>();
        if (importance == Importance.High) parts.Add(T("Importanza alta"));
        if (importance == Importance.Low) parts.Add(T("Importanza bassa"));
        if (readReceipt) parts.Add(T("Lettura"));
        if (deliveryReceipt) parts.Add(T("Recapito"));
        OptionsText.Text = parts.Count == 0 ? T("Opzioni") : string.Join(", ", parts);
        OptionsGlyph.Text = importance switch { Importance.High => "", Importance.Low => "", _ => "" };
        if (importance == Importance.High) OptionsGlyph.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC5, 0x0F, 0x1F));
        else OptionsGlyph.ClearValue(TextBlock.ForegroundProperty); // il colore del tema, anche quando cambia
    }

    /// <summary>Scarta (anche Esc): come chiudere, ma la bozza nata qui esce dalle Bozze (Closing).</summary>
    void OnDiscard(object sender, RoutedEventArgs e)
    {
        discarding = true;
        try
        {
            Close();
        }
        finally
        {
            discarding = false; // chiusura annullata, o rimandata al salvataggio (Salva bozza)
        }
    }

    // ---------------------------------------------------------------- scorciatoie

    /// <summary>
    /// Come nel nuovo Outlook: Ctrl+Invio invia (con gli stessi controlli del pulsante), Ctrl+K inserisce un link, Ctrl+V
    /// di file copiati li allega. Prima dell'editor, che con Invio andrebbe a capo.
    /// </summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var mods = System.Windows.Input.Keyboard.Modifiers;
#if DEBUG
        mods |= DevTools.ForcedModifiers;
#endif
        if (e.Key == System.Windows.Input.Key.Enter && mods == System.Windows.Input.ModifierKeys.Control)
        {
            e.Handled = true;
            CloseSuggestions();
            OnSend(SendButton, e);
        }
        else if (e.Key == System.Windows.Input.Key.K && mods == System.Windows.Input.ModifierKeys.Control)
        {
            e.Handled = true;
            InsertLink();
        }
        else if (e.Key == System.Windows.Input.Key.S && mods == System.Windows.Input.ModifierKeys.Control)
        {
            e.Handled = true; // Ctrl+S salva la bozza, come nel nuovo Outlook
            CloseSuggestions();
            _ = SaveDraftAsync();
        }
        else if (e.Key == System.Windows.Input.Key.V && mods == System.Windows.Input.ModifierKeys.Control && Clipboard.ContainsFileDropList() && !Clipboard.ContainsText())
        {
            // File copiati in Esplora file: allegati, in qualunque punto della finestra. Il RichTextBox non li incolla, e il
            // suo Incolla resta spento (quindi nemmeno OnEditorPasting li riceve).
            e.Handled = true;
            AddFiles(Clipboard.GetFileDropList().Cast<string>().ToList());
        }
    }

    /// <summary>Esc chiude come Scarta (con la domanda, se c'è qualcosa da perdere); prima chiude i suggerimenti e le tendine.</summary>
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != System.Windows.Input.Key.Escape || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None) return;
        e.Handled = true;
        OnDiscard(this, e);
    }
}
