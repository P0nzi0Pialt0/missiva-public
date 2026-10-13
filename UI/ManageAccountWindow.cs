using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Gestisci account, come Impostazioni, Account nel nuovo Outlook e Impostazioni server in Thunderbird: il nome mostrato
/// nel campo Da; per gli account IMAP la password (Aruba PEC la fa cambiare ogni tre mesi) e i server, provati con
/// ImapProvider.TestAsync prima di salvarli, con gli stessi controlli di Aggiungi account; per Microsoft e Google "Accedi
/// di nuovo", solo al clic. L'account resta lo stesso: firme, categorie, Preferiti e ordine delle cartelle sono legati al
/// suo Id. La password si cifra con DPAPI e non va mai nel registro.
/// </summary>
public sealed class ManageAccountWindow : Window
{
    readonly MailAccount account;
    readonly TextBox nameBox = new();
    readonly PasswordBox passwordBox = new();
    readonly TextBox imapHost = new(), imapPort = new(), smtpHost = new(), smtpPort = new(), userBox = new();
    readonly ComboBox imapSecurity = Security(), smtpSecurity = Security();
    readonly Expander serverExpander = new() { Header = "Server", Margin = new Thickness(0, 12, 0, 0) };
    readonly TextBlock error = Dialog.ErrorText(14);
    readonly TextBlock status = Secondary("");
    readonly Button save = new() { Content = "Salva", MinWidth = 100, IsDefault = true };
    readonly Button signIn = new() { Content = "Accedi di nuovo", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel form = new();
    CancellationTokenSource? googleWait;
    /// <summary>La prova dei server nuovi in corso (fino a 45 s): Annulla e la X la interrompono, e nulla si salva.</summary>
    CancellationTokenSource? trying;
    /// <summary>Annulla o la X durante la prova: la finestra si chiude appena la prova si è fermata.</summary>
    bool closeWhenStopped;

    /// <summary>Server, password o accesso nuovi: MainWindow rifà le connessioni dell'account e ricarica le cartelle.</summary>
    public bool Reconnect { get; private set; }

    public ManageAccountWindow(MailAccount account, bool focusPassword)
    {
        this.account = account;
        status.Margin = new Thickness(0, 10, 0, 0);
        Title = "Gestisci account";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 24) };
        panel.Children.Add(new TextBlock { Text = "Gestisci account", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = account.Email, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(Secondary(AccountTypes.Describe(account)));

        form.Children.Add(Label("Nome mostrato", 16));
        nameBox.Text = account.DisplayName;
        AutomationProperties.SetName(nameBox, "Nome mostrato");
        form.Children.Add(nameBox);
        form.Children.Add(Note(account.Kind == AccountKind.Microsoft
            ? "Nei messaggi inviati il nome lo mette Microsoft, dal tuo account (lo cambi nel profilo Microsoft); Missiva usa questo nelle risposte agli inviti e nelle conferme di lettura."
            : "Il nome che i destinatari vedono accanto al tuo indirizzo nel campo Da."));

        if (account.Kind == AccountKind.Imap && account.Imap is { } server) AddImap(server);
        else AddSignIn();
        AddIdentities();

        form.Children.Add(error);
        form.Children.Add(status);
        panel.Children.Add(form);

        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.Click += async (_, _) => await SaveAsync();
        var cancel = new Button { Content = "Annulla", MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = SystemParameters.WorkArea.Height - 80 };

        Closed += (_, _) => googleWait?.Cancel(); // chiusa mentre aspetta il browser
        Closing += (_, e) =>
        {
            // Durante la prova del server la finestra non si chiude sotto a SaveAsync (che poi salverebbe lo stesso password
            // e server): la prova si interrompe, e la finestra si chiude quando si è fermata, senza salvare nulla.
            if (trying == null) return;
            e.Cancel = true;
            closeWhenStopped = true;
            status.Text = "Interrompo la prova del collegamento…";
            trying.Cancel();
        };
        Loaded += (_, _) =>
        {
            if (focusPassword && account.Kind == AccountKind.Imap) passwordBox.Focus();
            else nameBox.Focus();
        };
    }

    // ---------------------------------------------------------------- pezzi

    static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    static TextBlock Note(string text)
    {
        var t = Secondary(text);
        t.Margin = new Thickness(0, 6, 0, 0);
        return t;
    }

    static TextBlock Label(string text, double top = 10) => new() { Text = text, Margin = new Thickness(0, top, 0, 4) };

    static TextBlock Section(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 4) };

    static ComboBox Security()
    {
        var box = new ComboBox();
        box.Items.Add(new ComboBoxItem { Content = "SSL/TLS" });
        box.Items.Add(new ComboBoxItem { Content = "STARTTLS" });
        return box;
    }

    /// <summary>Password e server degli account IMAP, con i campi di Configurazione manuale di Aggiungi account.</summary>
    void AddImap(ImapServer server)
    {
        form.Children.Add(Section("Password"));
        form.Children.Add(Secondary("Se hai cambiato la password sul sito del provider, scrivi qui quella nuova: Missiva la prova con il server prima di " +
                                    "salvarla. Resta cifrata su questo PC, solo per il tuo utente di Windows. Lasciala vuota per tenere quella di adesso."));
        form.Children.Add(Label("Nuova password"));
        AutomationProperties.SetName(passwordBox, "Nuova password");
        form.Children.Add(passwordBox);

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var width in new[] { new GridLength(1, GridUnitType.Star), new GridLength(8), new GridLength(80), new GridLength(8), new GridLength(120) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(UIElement element, int row, int column, string? name = null, int span = 1)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            if (name != null) AutomationProperties.SetName(element, name);
            grid.Children.Add(element);
        }
        Put(new TextBlock { Text = "Server IMAP (posta in arrivo)", Margin = new Thickness(0, 0, 0, 4) }, 0, 0);
        Put(new TextBlock { Text = "Porta", Margin = new Thickness(0, 0, 0, 4) }, 0, 2);
        Put(new TextBlock { Text = "Sicurezza", Margin = new Thickness(0, 0, 0, 4) }, 0, 4);
        Put(imapHost, 1, 0, "Server IMAP");
        Put(imapPort, 1, 2, "Porta IMAP");
        Put(imapSecurity, 1, 4, "Sicurezza IMAP");
        Put(new TextBlock { Text = "Server SMTP (posta in uscita)", Margin = new Thickness(0, 10, 0, 4) }, 2, 0);
        Put(new TextBlock { Text = "Porta", Margin = new Thickness(0, 10, 0, 4) }, 2, 2);
        Put(new TextBlock { Text = "Sicurezza", Margin = new Thickness(0, 10, 0, 4) }, 2, 4);
        Put(smtpHost, 3, 0, "Server SMTP");
        Put(smtpPort, 3, 2, "Porta SMTP");
        Put(smtpSecurity, 3, 4, "Sicurezza SMTP");
        Put(new TextBlock { Text = "Nome utente", Margin = new Thickness(0, 10, 0, 4) }, 4, 0);
        Put(userBox, 5, 0, "Nome utente", span: 5);

        imapHost.Text = server.ImapHost;
        imapPort.Text = server.ImapPort.ToString();
        imapSecurity.SelectedIndex = server.ImapSecurity == MailSecurity.Ssl ? 0 : 1;
        smtpHost.Text = server.SmtpHost;
        smtpPort.Text = server.SmtpPort.ToString();
        smtpSecurity.SelectedIndex = server.SmtpSecurity == MailSecurity.Ssl ? 0 : 1;
        userBox.Text = server.UserName;
        var inside = new StackPanel();
        inside.Children.Add(Secondary("Sempre cifrati, con SSL/TLS o STARTTLS: la password non viaggia mai in chiaro. Cambiali solo se il provider ti ha dato server nuovi."));
        inside.Children.Add(grid);
        serverExpander.Content = inside;
        form.Children.Add(serverExpander);
    }

    /// <summary>Microsoft e Google: il nuovo accesso, solo quando si preme il pulsante.</summary>
    void AddSignIn()
    {
        form.Children.Add(Section("Accesso"));
        var google = account.Kind == AccountKind.Gmail;
        form.Children.Add(Secondary((google ? "Si apre il browser" : "Si apre la finestra account di Windows") +
                                    " per rifare l'accesso con questo account: serve quando Missiva dice che l'account chiede un nuovo accesso, o dopo " +
                                    "aver cambiato la password dell'account o revocato i permessi. Missiva non vede mai la tua password."));
        if (account.Kind == AccountKind.Microsoft && account.Tenant != null)
        {
            var (clientId, tenant) = MicrosoftAuth.AppOf(account);
            var directory = tenant == MicrosoftAuth.Organizations ? "per qualsiasi organizzazione" : $"directory {tenant}";
            form.Children.Add(Note(clientId == MicrosoftAuth.DefaultClientId
                ? $"Con l'app di Missiva, {directory}."
                : $"Con l'app della tua organizzazione ({clientId}), {directory}."));
        }
        AutomationProperties.SetName(signIn, "Accedi di nuovo");
        signIn.Click += async (_, _) => await SignInAgainAsync();
        form.Children.Add(signIn);
    }

    // ---------------------------------------------------------------- altri indirizzi

    /// <summary>Gli altri indirizzi come sono nella finestra: si salvano con Salva.</summary>
    readonly List<SendIdentity> identities = new();
    readonly StackPanel identityList = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBox identityName = new(), identityEmail = new(), identityReplyTo = new();

    /// <summary>
    /// Altri indirizzi da cui scrivere con questo account (alias, "Invia come"), come gli indirizzi del Da di Outlook e le
    /// identità di Thunderbird: nome, indirizzo e Rispondi a. Nel campo Da compaiono tutti; Rispondi sceglie quello a cui era
    /// scritto il messaggio. Il server deve accettarli, e lo si dice per ogni tipo di account.
    /// </summary>
    void AddIdentities()
    {
        identities.AddRange(account.Identities.Select(i => new SendIdentity { Id = i.Id, Name = i.Name, Email = i.Email, ReplyTo = i.ReplyTo }));
        form.Children.Add(Section("Altri indirizzi (Invia come)"));
        form.Children.Add(Secondary(account.Kind switch
        {
            AccountKind.Gmail => "Per scrivere da un alias o da un altro indirizzo: aggiungilo anche in Gmail sul web (Impostazioni, Account e importazione, " +
                                 "Invia messaggio come), altrimenti Gmail mette al suo posto l'indirizzo principale.",
            AccountKind.Microsoft when account.Tenant == null => "Con Outlook.com l'indirizzo deve essere un alias del tuo account Microsoft (account.microsoft.com, " +
                                                                "Le tue informazioni, Gestisci la modalità di accesso).",
            AccountKind.Microsoft => "Con Microsoft 365 serve il permesso Invia come su quell'indirizzo (una casella condivisa come info@ o segreteria@): lo dà " +
                                     "l'amministratore. Senza, Microsoft rifiuta il messaggio e Missiva lo spiega.",
            _ => "Il server di posta in uscita deve accettare questo mittente: di solito gli alias della stessa casella sì.",
        }));
        form.Children.Add(identityList);
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var width in new[] { new GridLength(1, GridUnitType.Star), new GridLength(8), new GridLength(1, GridUnitType.Star) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(UIElement element, int row, int column, string? name = null)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            if (name != null) AutomationProperties.SetName(element, name);
            grid.Children.Add(element);
        }
        Put(new TextBlock { Text = "Nome", Margin = new Thickness(0, 0, 0, 4) }, 0, 0);
        Put(new TextBlock { Text = "Indirizzo", Margin = new Thickness(0, 0, 0, 4) }, 0, 2);
        Put(identityName, 1, 0, "Nome dell'altro indirizzo");
        Put(identityEmail, 1, 2, "Altro indirizzo");
        Put(new TextBlock { Text = "Rispondi a (facoltativo)", Margin = new Thickness(0, 10, 0, 4) }, 2, 0);
        Put(identityReplyTo, 3, 0, "Rispondi a");
        var add = new Button { Content = "Aggiungi", VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(add, "Aggiungi l'indirizzo");
        add.Click += (_, _) => AddIdentity();
        Put(add, 3, 2);
        form.Children.Add(grid);
        identityName.Text = account.DisplayName;
        FillIdentities();
    }

    void FillIdentities()
    {
        identityList.Children.Clear();
        if (identities.Count == 0) identityList.Children.Add(Secondary("Nessun altro indirizzo."));
        foreach (var identity in identities)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var remove = new Button { Content = "Togli", Padding = new Thickness(10, 2, 10, 3) };
            AutomationProperties.SetName(remove, "Togli " + identity.Email);
            remove.Click += (_, _) => { identities.Remove(identity); FillIdentities(); };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            var text = $"{(identity.Name.Length > 0 ? identity.Name + " <" + identity.Email + ">" : identity.Email)}" +
                       (identity.ReplyTo.Length > 0 ? $" · risposte a {identity.ReplyTo}" : "");
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text });
            identityList.Children.Add(row);
        }
    }

    void AddIdentity()
    {
        error.Visibility = Visibility.Collapsed;
        var email = identityEmail.Text.Trim();
        var replyTo = identityReplyTo.Text.Trim();
        static bool Valid(string address) => address.Contains('@') && !address.Contains(' ') && address.IndexOf('@') is > 0 and var at && at < address.Length - 3;
        if (!Valid(email)) { ShowError("Scrivi l'altro indirizzo, per esempio info@studiorossi.it."); return; }
        if (email.Equals(account.Email, StringComparison.OrdinalIgnoreCase) || identities.Any(i => i.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError($"{email} c'è già.");
            return;
        }
        if (replyTo.Length > 0 && !Valid(replyTo)) { ShowError("L'indirizzo di Rispondi a non è valido."); return; }
        identities.Add(new SendIdentity { Name = identityName.Text.Trim(), Email = email, ReplyTo = replyTo });
        identityEmail.Text = identityReplyTo.Text = "";
        FillIdentities();
    }

    void ShowError(string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
    }

    void SetBusy(bool busy) => form.IsEnabled = save.IsEnabled = !busy;

    // ---------------------------------------------------------------- salvataggio

    async Task SaveAsync()
    {
        error.Visibility = Visibility.Collapsed;
        var name = nameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowError("Scrivi il nome da mostrare, per esempio Mario Rossi o Studio Rossi.");
            nameBox.Focus();
            return;
        }
        if (account.Kind == AccountKind.Imap && account.Imap is { } old)
        {
            var candidate = AccountWindow.ParseServer(imapHost.Text, imapPort.Text, imapSecurity.SelectedIndex == 1, smtpHost.Text, smtpPort.Text,
                                                      smtpSecurity.SelectedIndex == 1, userBox.Text);
            if (candidate == null)
            {
                serverExpander.IsExpanded = true;
                ShowError(AccountWindow.ServerFieldsError);
                return;
            }
            var password = passwordBox.Password.Length > 0 ? passwordBox.Password : null;
            if (password != null || !SameServer(candidate, old))
            {
                status.Text = $"Provo il collegamento con {candidate.ImapHost} e {candidate.SmtpHost}…";
                SetBusy(true);
                trying = new CancellationTokenSource(TimeSpan.FromSeconds(MailLimits.ServerTrySeconds));
                bool stopped = false;
                try
                {
                    await ImapProvider.ChangeServerAsync(account, candidate, password, trying.Token);
                    Reconnect = true;
                }
                catch (Exception ex) when (closeWhenStopped)
                {
                    // Annulla o la X durante la prova: l'account resta con i server e la password di prima.
                    Log.Write($"account IMAP {account.Email} non cambiato: prova interrotta ({ex.GetType().Name})");
                    stopped = true;
                    return;
                }
                catch (MailException ex)
                {
                    // Nulla si salva: l'account resta con i server e la password di prima.
                    Log.Write($"account IMAP {account.Email} non cambiato ({candidate.ImapHost}, {candidate.SmtpHost}): {ex.Message}");
                    status.Text = "";
                    ShowError(ex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error("account IMAP non cambiato", ex);
                    status.Text = "";
                    ShowError("Collegamento non riuscito: " + ex.Message);
                    return;
                }
                finally
                {
                    trying.Dispose();
                    trying = null;
                    SetBusy(false);
                    if (stopped) Close();
                }
            }
        }
        account.DisplayName = name;
        if (!identities.Select(i => (i.Id, i.Name, i.Email, i.ReplyTo)).SequenceEqual(account.Identities.Select(i => (i.Id, i.Name, i.Email, i.ReplyTo))))
        {
            account.Identities = identities.ToList();
            Log.Write($"altri indirizzi di {account.Email}: {identities.Count}");
        }
        Store.Save();
        DialogResult = true;
    }

    static bool SameServer(ImapServer a, ImapServer b) =>
        a.ImapHost == b.ImapHost && a.ImapPort == b.ImapPort && a.ImapSecurity == b.ImapSecurity && a.SmtpHost == b.SmtpHost
        && a.SmtpPort == b.SmtpPort && a.SmtpSecurity == b.SmtpSecurity && a.UserName == b.UserName;

    // ---------------------------------------------------------------- nuovo accesso

    async Task SignInAgainAsync()
    {
        error.Visibility = Visibility.Collapsed;
        var google = account.Kind == AccountKind.Gmail;
        if (MainWindow.Testing)
        {
            // Nella versione di prova nessuno può rifare l'accesso: niente finestre, solo il registro.
            Log.Write($"prova: aprirei {(google ? "il browser per l'accesso a Google" : "la finestra di accesso di Microsoft")} per {account.Email}");
            status.Text = "Nella versione di prova la finestra di accesso non si apre.";
            return;
        }
        SetBusy(true);
        try
        {
            if (google) await SignInGoogleAsync();
            else
            {
                MicrosoftAuth.OpenOver(this);
                await MicrosoftAuth.SignInAgainAsync(account);
            }
            Reconnect = true;
            status.Text = "Accesso rifatto.";
        }
        catch (Exception ex) when (MicrosoftAuth.Canceled(ex))
        {
            // annullato nella finestra di Microsoft
        }
        catch (OperationCanceledException) when (!IsVisible)
        {
            // finestra chiusa durante l'attesa del browser
        }
        catch (OperationCanceledException)
        {
            ShowError("L'accesso nel browser non è stato completato in tempo. Riprova.");
        }
        catch (Microsoft.Identity.Client.MsalException ex)
        {
            Log.Error("nuovo accesso Microsoft", ex);
            ReadingView.ShowConsentError(this, "Accesso non riuscito", ex, account, "leggere e inviare la posta");
        }
        catch (Exception ex)
        {
            Log.Error("nuovo accesso", ex);
            ShowError(ex is MailException ? ex.Message : "Accesso non riuscito: " + ex.Message);
        }
        finally
        {
            if (IsVisible) SetBusy(false);
        }
    }

    /// <summary>Google nel browser, con l'indirizzo dell'account già scritto: il token nuovo sostituisce quello di prima.</summary>
    async Task SignInGoogleAsync()
    {
        status.Text = "Continua nel browser. Quando hai consentito l'accesso, Missiva lo prende da sola.";
        googleWait = new CancellationTokenSource(TimeSpan.FromMinutes(MailLimits.BrowserSignInMinutes));
        try
        {
            var result = await GoogleAuth.SignInAsync(googleWait.Token, account.Email, GoogleAuth.ExtraScopesOf(account)); // calendario e contatti, se c'erano, restano
            Activate(); // dal browser si torna a Missiva
            if (!result.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase))
                throw new MailException($"Nel browser hai scelto {result.Email}, non {account.Email}: per un altro account usa Aggiungi account.");
            await GmailProvider.TestAsync(result.Email, result.AccessToken);
            googleWait.Token.ThrowIfCancellationRequested();
            GoogleAuth.Apply(account, result); // una casella tolta nella pagina di Google: lo dice
            Store.Save();
            Log.Write($"nuovo accesso di {account.Email}");
        }
        finally
        {
            googleWait.Dispose();
            googleWait = null;
        }
    }
}
