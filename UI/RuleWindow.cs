using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Nuova regola e Modifica regola, come la finestra delle regole del nuovo Outlook: nome, condizioni (devono valere tutte;
/// dentro una casella le parole separate da punto e virgola, ne basta una) e azioni. Per gli account Microsoft la regola si
/// salva in Outlook (Graph messageRules, con il consenso alle impostazioni della casella, chiesto solo dal pulsante) e gira
/// sul server; per Gmail e IMAP è una regola di Missiva, cifrata nelle impostazioni, che gira con Missiva attiva. Una regola
/// che inoltra fuori dall'organizzazione si salva solo dopo una conferma con gli indirizzi in vista.
/// </summary>
public sealed class RuleWindow : Window
{
    readonly IMailProvider provider;
    readonly List<MailFolder> folders;
    readonly MailRule? editing;
    readonly bool server;
    List<MailRule>? known;

    readonly TextBox name = new();
    readonly CheckBox fromCheck = Box("Da"), toMe = Box("Inviato a me (sono fra i destinatari A)"), ccMe = Box("Sono in Cc"),
        subjectCheck = Box("L'oggetto contiene"), bodyCheck = Box("Il testo contiene"), attachments = Box("Ha allegati"), importanceCheck = Box("Importanza");
    readonly TextBox from = Field("Indirizzi o parole del mittente"), subject = Field("Parole dell'oggetto"), body = Field("Parole del testo");
    readonly ComboBox importance = new() { Width = 140, Items = { "Alta", "Bassa" }, SelectedIndex = 0 };
    readonly CheckBox moveCheck = Box("Sposta in"), copyCheck = Box("Copia in"), delete = Box("Elimina (nel Cestino)"), markRead = Box("Segna come letto"),
        flag = Box("Contrassegna"), categoryCheck = Box("Categoria"), forwardCheck = Box("Inoltra a"), stop = Box("Non applicare altre regole");
    readonly ComboBox moveTo = new() { Width = 260 }, copyTo = new() { Width = 260 };
    readonly ComboBox category = new() { Width = 260, IsEditable = true };
    readonly TextBox forward = Field("Indirizzi a cui inoltrare");
    readonly StackPanel form = new();
    readonly StackPanel consent = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) };
    readonly TextBlock status = Secondary("");
    readonly TextBlock error = Dialog.ErrorText(12);
    readonly Button save = new() { Content = "Salva", MinWidth = 100, IsDefault = true };

    /// <summary>La regola salvata (con l'id del server per Outlook).</summary>
    public MailRule? Saved { get; private set; }
    /// <summary>Il consenso alle impostazioni della casella è arrivato in questa finestra.</summary>
    public bool ConsentGiven { get; private set; }

    /// <param name="editing">La regola da modificare; null per una nuova.</param>
    /// <param name="prefill">Per una nuova, i campi già messi (Crea regola da un messaggio: il mittente e l'oggetto).</param>
    /// <param name="known">Le regole del server già lette (l'elenco delle Impostazioni); null: la finestra le legge.</param>
    public RuleWindow(IMailProvider provider, List<MailFolder> folders, MailRule? editing, MailRule? prefill = null, List<MailRule>? known = null)
    {
        this.provider = provider;
        this.folders = folders;
        this.editing = editing;
        this.known = known;
        server = provider.HasServerRules;
        Title = editing == null ? "Nuova regola" : "Modifica regola";
        Width = 640;
        Height = 760;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 12) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(Secondary(provider.Account.Email + (server
            ? ": la regola si salva in Outlook e gira sul server, anche a Missiva chiusa."
            : ": è una regola di Missiva. Gira solo quando Missiva è aperta, anche nell'area di notifica, sui nuovi messaggi della Posta in arrivo.")));
        status.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(status);
        BuildConsent();
        panel.Children.Add(consent);
        BuildForm();
        panel.Children.Add(form);
        panel.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(28, 8, 28, 20), LastChildFill = false };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.Click += async (_, _) => await SaveAsync();
        var cancel = new Button { Content = "Annulla", MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Fill(editing ?? prefill ?? new MailRule { AccountId = provider.Account.Id });
        Loaded += async (_, _) => await LoadAsync();
    }

    // ---------------------------------------------------------------- pezzi

    static CheckBox Box(string text)
    {
        var box = new CheckBox { Content = text, Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
        AutomationProperties.SetName(box, text);
        return box;
    }

    static TextBox Field(string automationName)
    {
        var box = new TextBox { MinWidth = 260 };
        AutomationProperties.SetName(box, automationName);
        return box;
    }

    static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    static TextBlock Section(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 4) };

    /// <summary>Una riga: la casella e, accanto, il campo che la accompagna (scrivendo nel campo la casella si spunta).</summary>
    static DockPanel Row(CheckBox check, FrameworkElement? field = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        DockPanel.SetDock(check, Dock.Left);
        row.Children.Add(check);
        if (field != null)
        {
            field.VerticalAlignment = VerticalAlignment.Center;
            if (field is ComboBox) field.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(field);
            if (field is TextBox text) text.TextChanged += (_, _) => check.IsChecked = text.Text.Trim().Length > 0;
            if (field is ComboBox { IsEditable: true } editable)
                editable.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => check.IsChecked = editable.Text.Trim().Length > 0));
            else if (field is ComboBox box) box.SelectionChanged += (_, _) => check.IsChecked = box.SelectedItem != null;
        }
        return row;
    }

    /// <summary>Una cartella nell'elenco delle cartelle: con il percorso, perché due "Fatture" in due clienti si distinguano.</summary>
    sealed record FolderChoice(MailFolder Folder, string Path)
    {
        public override string ToString() => Path;
    }

    List<FolderChoice> FolderChoices()
    {
        var byId = folders.ToDictionary(f => f.Id);
        string PathOf(MailFolder f)
        {
            var parts = new List<string> { f.Name };
            for (var p = f.ParentId; p != null && byId.TryGetValue(p, out var parent) && parts.Count < 8; p = parent.ParentId) parts.Insert(0, parent.Name);
            return string.Join(" / ", parts);
        }
        // Le Bozze e la Posta in uscita non sono una destinazione: Outlook non le propone.
        return folders.Where(f => f.Kind != FolderKind.Drafts).Select(f => new FolderChoice(f, PathOf(f)))
            .OrderBy(c => c.Folder.Kind == FolderKind.Other ? 1 : 0).ThenBy(c => c.Folder.Kind == FolderKind.Other ? c.Path : "", StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    void BuildConsent()
    {
        consent.Children.Add(Secondary("Le regole di Outlook stanno nelle impostazioni della casella: per leggerle e cambiarle Missiva deve poterci " +
                                       "accedere (il permesso MailboxSettings, lo stesso delle risposte automatiche, che l'accesso di base non chiede)."));
        var allow = new Button { Content = "Consenti l'accesso alle impostazioni della casella", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        allow.SetResourceReference(StyleProperty, "AccentButtonStyle");
        AutomationProperties.SetName(allow, "Consenti l'accesso alle impostazioni della casella");
        allow.Click += async (_, _) => await AllowAsync();
        consent.Children.Add(allow);
    }

    void BuildForm()
    {
        form.Children.Add(Section("Nome"));
        AutomationProperties.SetName(name, "Nome della regola");
        form.Children.Add(name);

        form.Children.Add(Section("Quando arriva un messaggio che"));
        form.Children.Add(Secondary("Devono valere tutte le condizioni scelte. Nelle caselle più parole o indirizzi si separano con il punto e virgola: ne basta uno."));
        form.Children.Add(Row(fromCheck, from));
        form.Children.Add(Row(toMe));
        form.Children.Add(Row(ccMe));
        form.Children.Add(Row(subjectCheck, subject));
        form.Children.Add(Row(bodyCheck, body));
        form.Children.Add(Row(attachments));
        AutomationProperties.SetName(importance, "Importanza del messaggio");
        form.Children.Add(Row(importanceCheck, importance));
        if (!server) importance.Items.RemoveAt(1); // l'elenco di Gmail e IMAP sa solo dell'importanza alta

        form.Children.Add(Section("Fai questo"));
        var choices = FolderChoices();
        moveTo.ItemsSource = choices;
        copyTo.ItemsSource = choices;
        AutomationProperties.SetName(moveTo, "Cartella in cui spostare");
        AutomationProperties.SetName(copyTo, "Cartella in cui copiare");
        form.Children.Add(Row(moveCheck, moveTo));
        form.Children.Add(Row(copyCheck, copyTo));
        form.Children.Add(Row(delete));
        form.Children.Add(Row(markRead));
        if (!server) form.Children.Add(Row(flag)); // le regole di Outlook non contrassegnano
        AutomationProperties.SetName(category, "Categoria da mettere");
        form.Children.Add(Row(categoryCheck, category));
        if (server) form.Children.Add(Row(forwardCheck, forward));
        form.Children.Add(Row(stop));
    }

    void Fill(MailRule r)
    {
        name.Text = r.Name;
        from.Text = string.Join("; ", r.FromAddresses.Concat(r.SenderContains));
        fromCheck.IsChecked = from.Text.Length > 0;
        toMe.IsChecked = r.SentToMe;
        ccMe.IsChecked = r.SentCcMe;
        subject.Text = string.Join("; ", r.SubjectContains);
        // Crea regola da un messaggio: l'oggetto è scritto ma la condizione resta spenta, come in Outlook.
        subjectCheck.IsChecked = r.SubjectContains.Count > 0 && editing != null;
        body.Text = string.Join("; ", r.BodyContains);
        attachments.IsChecked = r.HasAttachments;
        importanceCheck.IsChecked = r.WithImportance != null;
        importance.SelectedIndex = r.WithImportance == Importance.Low && server ? 1 : 0;
        var choices = (List<FolderChoice>)moveTo.ItemsSource;
        moveTo.SelectedItem = choices.FirstOrDefault(c => c.Folder.Id == r.MoveTo);
        moveCheck.IsChecked = r.MoveTo.Length > 0;
        copyTo.SelectedItem = choices.FirstOrDefault(c => c.Folder.Id == r.CopyTo);
        copyCheck.IsChecked = r.CopyTo.Length > 0;
        delete.IsChecked = r.Delete;
        markRead.IsChecked = r.MarkRead;
        flag.IsChecked = r.Flag;
        category.Text = string.Join("; ", r.Categories);
        categoryCheck.IsChecked = r.Categories.Count > 0;
        forward.Text = string.Join("; ", r.ForwardTo.Select(a => a.Email));
        forwardCheck.IsChecked = r.ForwardTo.Count > 0;
        stop.IsChecked = r.StopProcessing;
        if (r.RedirectTo.Count + r.ForwardAsAttachmentTo.Count > 0 || r.Hidden.Count > 0)
        {
            var more = r.Hidden.ToList();
            if (r.RedirectTo.Count > 0) more.Add("reindirizza a " + string.Join(", ", r.RedirectTo.Select(a => a.Email)));
            if (r.ForwardAsAttachmentTo.Count > 0) more.Add("inoltra come allegato a " + string.Join(", ", r.ForwardAsAttachmentTo.Select(a => a.Email)));
            form.Children.Insert(2, Secondary("Questa regola di Outlook fa anche cose che Missiva non mostra (" + string.Join(", ", more) + "): restano come sono."));
        }
    }

    void ShowError(string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
    }

    // ---------------------------------------------------------------- lettura e consenso

    /// <summary>
    /// Le categorie per la casella della categoria (Gmail: le etichette; Microsoft: quelle di Outlook o di Missiva). Sul
    /// thread dell'interfaccia, come ogni richiesta ai provider: prima partiva da un thread del pool (Task.Run), e da lì
    /// il provider toccava il suo stato e le impostazioni insieme all'interfaccia, e un nuovo accesso a Microsoft
    /// avrebbe chiesto la finestra dal thread sbagliato (audit dell'11 ottobre, C-27). Non si aspetta: le regole si leggono intanto.
    /// </summary>
    async Task LoadCategoriesAsync()
    {
        try
        {
            var list = await provider.GetCategoriesAsync();
            category.ItemsSource = list.Select(c => c.Name).ToList();
        }
        catch (Exception ex) { Log.Write("regole: categorie non lette: " + ex.Message); }
    }

    async Task LoadAsync()
    {
        _ = LoadCategoriesAsync();
        if (!server || known != null) return;
        status.Text = "Caricamento delle regole di Outlook…";
        form.IsEnabled = save.IsEnabled = false;
        try
        {
            known = await provider.GetRulesAsync();
        }
        catch (Exception ex)
        {
            Log.Error("regole di Outlook", ex);
            status.Text = "";
            ShowError("Le regole di Outlook non si leggono: " + ex.Message);
            return;
        }
        status.Text = "";
        consent.Visibility = known == null ? Visibility.Visible : Visibility.Collapsed;
        form.IsEnabled = save.IsEnabled = known != null;
    }

    /// <summary>La finestra di Microsoft per il consenso, solo da questo pulsante; nella prova nessuna finestra.</summary>
    async Task AllowAsync()
    {
        error.Visibility = Visibility.Collapsed;
        if (MainWindow.Testing)
        {
            await provider.AllowMailboxSettingsAsync(); // TestingProvider: solo "prova: chiederei il consenso ..." nel registro
            status.Text = "Nella versione di prova la finestra di Microsoft non si apre.";
            return;
        }
        consent.IsEnabled = false;
        try
        {
            MicrosoftAuth.OpenOver(this);
            await provider.AllowMailboxSettingsAsync();
            ConsentGiven = true;
            await LoadAsync();
        }
        catch (Exception ex) when (MicrosoftAuth.Canceled(ex))
        {
            // annullato nella finestra di Microsoft
        }
        catch (Exception ex)
        {
            Log.Error("consenso alle impostazioni della casella (regole)", ex);
            ReadingView.ShowConsentError(this, "Accesso alle impostazioni non consentito", ex, provider.Account, "leggere e cambiare le regole di Outlook");
        }
        finally
        {
            consent.IsEnabled = true;
        }
    }

    // ---------------------------------------------------------------- salvataggio

    /// <summary>La regola scritta nella finestra, o null con l'errore mostrato.</summary>
    MailRule? Read()
    {
        var r = editing?.Copy() ?? new MailRule { AccountId = provider.Account.Id };
        var sender = fromCheck.IsChecked == true ? Rules.Words(from.Text) : [];
        r.FromAddresses = sender.Where(AccountTypes.LooksValid).ToList();
        r.SenderContains = sender.Where(w => !AccountTypes.LooksValid(w)).ToList();
        r.SentToMe = toMe.IsChecked == true;
        r.SentCcMe = ccMe.IsChecked == true;
        r.SubjectContains = subjectCheck.IsChecked == true ? Rules.Words(subject.Text) : [];
        r.BodyContains = bodyCheck.IsChecked == true ? Rules.Words(body.Text) : [];
        r.HasAttachments = attachments.IsChecked == true;
        r.WithImportance = importanceCheck.IsChecked == true ? importance.SelectedIndex == 1 ? Importance.Low : Importance.High : null;
        var moveFolder = moveCheck.IsChecked == true ? (moveTo.SelectedItem as FolderChoice)?.Folder : null;
        var copyFolder = copyCheck.IsChecked == true ? (copyTo.SelectedItem as FolderChoice)?.Folder : null;
        if (moveCheck.IsChecked == true && moveFolder == null) { ShowError("Scegli la cartella in cui spostare i messaggi."); return null; }
        if (copyCheck.IsChecked == true && copyFolder == null) { ShowError("Scegli la cartella in cui copiare i messaggi."); return null; }
        (r.MoveTo, r.MoveToName) = (moveFolder?.Id ?? "", moveFolder?.Name ?? "");
        (r.CopyTo, r.CopyToName) = (copyFolder?.Id ?? "", copyFolder?.Name ?? "");
        r.Delete = delete.IsChecked == true;
        r.MarkRead = markRead.IsChecked == true;
        r.Flag = !server && flag.IsChecked == true;
        r.Categories = categoryCheck.IsChecked == true ? Rules.Words(category.Text) : [];
        r.StopProcessing = stop.IsChecked == true;
        if (server)
        {
            var addresses = forwardCheck.IsChecked == true ? Rules.Words(forward.Text) : [];
            if (addresses.FirstOrDefault(a => !AccountTypes.LooksValid(a)) is { } wrong) { ShowError($"\"{wrong}\" non è un indirizzo valido."); return null; }
            r.ForwardTo = addresses.Select(a => r.ForwardTo.FirstOrDefault(x => x.Email.Equals(a, StringComparison.OrdinalIgnoreCase)) ?? new Address { Email = a }).ToList();
        }
        if (r.MoveTo.Length > 0 && r.Delete) { ShowError("Scegli Sposta in oppure Elimina, non tutte e due."); return null; }
        if (!r.HasActions) { ShowError("Scegli almeno un'azione: che cosa deve fare la regola."); return null; }
        if (fromCheck.IsChecked == true && sender.Count == 0 || subjectCheck.IsChecked == true && r.SubjectContains.Count == 0 || bodyCheck.IsChecked == true && r.BodyContains.Count == 0)
        {
            ShowError("Scrivi le parole della condizione scelta, o togli la spunta.");
            return null;
        }
        r.Name = name.Text.Trim();
        if (r.Name.Length == 0)
            r.Name = r.FromAddresses.Concat(r.SenderContains).FirstOrDefault() is { } who ? "Da " + who
                   : r.SubjectContains.FirstOrDefault() is { } word ? "Oggetto: " + word : "Nuova regola";
        return r;
    }

    async Task SaveAsync()
    {
        error.Visibility = Visibility.Collapsed;
        if (Read() is not { } rule) return;
        // Il trucco di chi entra in una casella: una regola che inoltra tutto altrove, di nascosto. Si crea solo sapendolo:
        // quando chiedere, e con quali indirizzi in vista (tutti, anche quelli che questa finestra non mostra), lo dice
        // Rules.ForwardQuestion. La finestra non accende e non spegne: quello lo fa la casella dell'elenco, che chiede lì.
        // Il pulsante predefinito è Annulla (scelte del 14 ottobre 2026, U-02): prima Invio due volte salvava e confermava.
        var question = Rules.ForwardQuestion(editing, rule, provider.Account);
        if (question is { } forward && Dialog.AskCancelFirst(this, forward.Heading, forward.Body, forward.Confirm) != Choice.Primary) return;
        // Senza condizioni (anche quelle di Outlook che Missiva non mostra) la regola agisce su ogni messaggio. Una domanda
        // sola: se c'era quella sugli inoltri lo diceva già; altrimenti questa, che ricorda gli inoltri verso fuori che ci
        // sono (scelte del 14 ottobre 2026, U-02: prima le due domande venivano di fila, e questa non nominava gli inoltri).
        if (question == null && Rules.AllMessagesQuestion(rule, provider.Account) is { } all && Dialog.Ask(this, all.Heading, all.Body, all.Confirm) != Choice.Primary) return;
        form.IsEnabled = save.IsEnabled = false;
        try
        {
            if (server)
            {
                if (editing == null) rule.Sequence = (known?.Count > 0 ? known.Max(k => k.Sequence) : 0) + 1;
                rule.ServerId = await provider.SaveRuleAsync(editing, rule);
            }
            else
            {
                Rules.Put(rule);
                if (MainWindow.Testing)
                    Log.Write($"prova: {(editing == null ? "creerei" : "modificherei")} la regola \"{rule.Name}\" di Missiva per {provider.Account.Email}: {Rules.Describe(rule)} " +
                              "(salvata solo nella copia delle impostazioni della prova)");
            }
            Saved = rule;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Error("regola non salvata", ex);
            ShowError("La regola non è stata salvata: " + ex.Message);
            form.IsEnabled = save.IsEnabled = true;
        }
    }
}
