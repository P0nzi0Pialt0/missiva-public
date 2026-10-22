using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Navigation;
using Microsoft.Identity.Client;

namespace Missiva.UI;

/// <summary>
/// Aggiungi account, come in Outlook e Thunderbird: prima l'indirizzo, da cui Missiva riconosce il tipo di account
/// (AccountTypes.DetectAsync), poi l'accesso di quel tipo. Il tipo si può sempre cambiare a mano.
/// </summary>
public partial class AccountWindow : Window
{
    /// <summary>Il tipo di cui si mostra il pannello; null nel primo passo (indirizzo) e nella scelta a mano.</summary>
    AccountType? type;
    CancellationTokenSource? googleWait;
    /// <summary>La ricerca delle impostazioni del server (configurazione automatica), per l'indirizzo in configFor.</summary>
    CancellationTokenSource? configSearch;
    /// <summary>Server di un altro dominio trovati con i record SRV (DNS senza garanzie): all'accesso si chiede conferma.</summary>
    string[] foreignHosts = [];
    string? configFor;

    /// <summary>
    /// "Scrivi all'amministratore": il link mailto: del messaggio da preparare dopo la chiusura della finestra, con il link
    /// del consenso (MainWindow lo apre in Nuovo messaggio, da un altro account; mai inviato da solo).
    /// </summary>
    public string? AdminMailto { get; private set; }

    public AccountWindow()
    {
        InitializeComponent();
#if DEBUG
        DevTools.HideForPhotos(this); // le foto (--foto): nasce fuori dallo schermo e non prende il fuoco
        if (DevTools.Photos) PhotoNetwork();
#endif
        ClientIdBox.Text = Store.Settings.MicrosoftClientId ?? MicrosoftAuth.DefaultClientId;
        GoogleIdBox.Text = Store.Settings.GoogleClientId ?? "";
        // Un segreto che non si decifra più (impostazioni di un altro PC) resta vuoto, da riscrivere: prima l'eccezione di
        // Windows fermava il costruttore e la finestra non si apriva.
        bool secretRead = Store.TryUnprotect(Store.Settings.ProtectedGoogleClientSecret, out var googleSecret);
        GoogleSecretBox.Text = GoogleAuth.IsConfigured ? googleSecret : "";
        GoogleAppExpander.IsExpanded = !GoogleAuth.IsConfigured || !secretRead;
        MailAdminButton.Visibility = Store.Settings.Accounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ImportBackupLine.Visibility = Store.Settings.Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Closed += (_, _) =>
        {
            googleWait?.Cancel(); // chiusa mentre aspetta il browser
            configSearch?.Cancel();
        };
        Loaded += (_, _) => AddressBox.Focus();
        UpdateWork();
    }

    string Email => AddressBox.Text.Trim();

#if DEBUG
    /// <summary>
    /// Le foto delle Impostazioni in inglese (--foto): niente rete vera. Il DNS non risponde nulla (il dominio diventa un
    /// account IMAP), le pagine della configurazione automatica non ci sono, e i collegamenti di prova a imap. e smtp. del
    /// dominio riescono sulle porte con SSL/TLS: la finestra trova "da sola" i server, senza un pacchetto in rete.
    /// </summary>
    static void PhotoNetwork()
    {
        MailDns.TestAnswers = (_, _) => [];
        MailDns.TestTxt = _ => [];
        MailAutoconfig.UseTestHandler(new NotFoundHandler());
        MailAutoconfig.TestProbe = (_, port, _, _) => Task.FromResult(port is 993 or 465);
    }

    sealed class NotFoundHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancel) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
#endif

    /// <summary>
    /// Senza account (il primo avvio su un PC nuovo): la copia di sicurezza si importa da qui, senza cercare le Impostazioni.
    /// Con "Riavvia ora" questa finestra si chiude e Missiva riparte con i dati della copia (BackupUi.OfferRestart).
    /// </summary>
    void OnImportBackup(object sender, RoutedEventArgs e) =>
        // Dopo il clic, non dentro: un link premuto con UI Automation (Assistente vocale) aspetta la fine del gestore, e con
        // una finestra aperta lì dentro restava fermo fino alla sua chiusura.
        Dispatcher.BeginInvoke(() => BackupUi.Import(this));

    void OnChooseMicrosoft(object sender, RoutedEventArgs e) => Show(AccountType.MicrosoftPersonal);
    void OnChooseWork(object sender, RoutedEventArgs e) => Show(AccountType.Microsoft365);
    void OnChooseGmail(object sender, RoutedEventArgs e) =>
        Show(AccountTypes.LooksValid(Email) && !AccountTypes.IsGmailDomain(Email) ? AccountType.GoogleWorkspace : AccountType.Google);

    void OnChooseImap(object sender, RoutedEventArgs e)
    {
        if (AccountTypes.LooksValid(Email))
        {
            Show(AccountType.Imap);
            return;
        }
        ShowAddress(); // senza indirizzo non si cercano i server
        ShowError(T("Per un altro account serve l'indirizzo: scrivilo e premi Avanti."));
    }

    /// <summary>Il primo passo: l'indirizzo.</summary>
    void ShowAddress()
    {
        type = null;
        HidePanels();
        AddressPanel.Visibility = Visibility.Visible;
        ConfirmButton.Visibility = Visibility.Visible;
        ConfirmButton.Content = T("Avanti");
        Heading.Text = T("Aggiungi un account");
        AddressBox.Focus();
    }

    /// <summary>La scelta a mano del tipo, con una nota (per esempio il dominio che non si è riconosciuto).</summary>
    void ShowChoices(string? note = null)
    {
        type = null;
        HidePanels();
        ChoicePanel.Visibility = Visibility.Visible;
        ChoiceNote.Text = note ?? "";
        ChoiceNote.Visibility = note == null ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        Heading.Text = T("Scegli il tipo di account");
    }

    void Show(AccountType t)
    {
        type = t;
        HidePanels();
        KindLine.Visibility = Visibility.Visible;
        KindText.Text = AccountTypes.Name(t) + (AccountTypes.LooksValid(Email) ? " · " + Email : "");
        MicrosoftPanel.Visibility = t == AccountType.MicrosoftPersonal ? Visibility.Visible : Visibility.Collapsed;
        WorkPanel.Visibility = t == AccountType.Microsoft365 ? Visibility.Visible : Visibility.Collapsed;
        GmailPanel.Visibility = t is AccountType.Google or AccountType.GoogleWorkspace ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceNote.Visibility = t == AccountType.GoogleWorkspace ? Visibility.Visible : Visibility.Collapsed;
        ImapPanel.Visibility = t == AccountType.Imap ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = ConfirmButton.Visibility = Visibility.Visible;
        ConfirmButton.Content = t switch
        {
            AccountType.Google or AccountType.GoogleWorkspace => T("Accedi con Google"),
            AccountType.Imap => T("Accedi"),
            _ => T("Accedi con Microsoft"),
        };
        Heading.Text = t switch
        {
            AccountType.MicrosoftPersonal => T("Account Microsoft"),
            AccountType.Microsoft365 => T("Account Microsoft 365"),
            AccountType.GoogleWorkspace => T("Account Google Workspace"),
            AccountType.Imap => T("Altro account"),
            _ => T("Account Google"),
        };
        if (t == AccountType.Imap && configFor != Email) _ = FindConfigAsync();
    }

    void HidePanels()
    {
        foreach (var panel in new UIElement[] { AddressPanel, ChoicePanel, KindLine, MicrosoftPanel, WorkPanel, GmailPanel, ImapPanel, AdminPanel, Error, Detecting, BackButton })
            panel.Visibility = Visibility.Collapsed;
        ConfirmButton.Visibility = Visibility.Collapsed;
    }

    void OnShowChoices(object sender, RoutedEventArgs e) => ShowChoices();

    void OnBack(object sender, RoutedEventArgs e) => ShowAddress();

    /// <summary>
    /// I due link della finestra (Microsoft Entra, Google Cloud), scritti nel XAML: si aprono nel browser solo se sono
    /// https. Oggi lo sono tutti e due, e nessun altro indirizzo arriva qui: è una difesa in più, perché quello che si
    /// passa a Windows da aprire sia sempre una pagina web (revisione del 14 ottobre 2026, U-05).
    /// </summary>
    void OnLink(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.Uri is not { IsAbsoluteUri: true } uri || uri.Scheme != Uri.UriSchemeHttps) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    void OnOwnApp(object sender, RoutedEventArgs e) => UpdateWork();

    /// <summary>Con l'app di Missiva, che per ora accetta solo account personali, il pannello di Microsoft 365 lo dice subito.</summary>
    void UpdateWork()
    {
        bool own = OwnAppCheck.IsChecked == true;
        OwnAppPanel.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
        bool blocked = !own && MicrosoftAuth.Blocked(MicrosoftAuth.DefaultClientId, MicrosoftAuth.Organizations);
        WorkBlocked.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        WorkIntro.Visibility = blocked ? Visibility.Collapsed : Visibility.Visible;
    }

    void ShowError(string text)
    {
        Error.Text = text;
        Error.Visibility = Visibility.Visible;
    }

    async void OnConfirm(object sender, RoutedEventArgs e)
    {
        Error.Visibility = AdminPanel.Visibility = Visibility.Collapsed;
        if (type == null)
        {
            await DetectAsync();
            return;
        }
        // Annulla resta attivo: durante l'accesso Google serve per smettere di aspettare il browser.
        SetBusy(true);
        try
        {
            switch (type)
            {
                case AccountType.MicrosoftPersonal: await AddMicrosoftAsync(); break;
                case AccountType.Microsoft365: await AddWorkAsync(); break;
                case AccountType.Google or AccountType.GoogleWorkspace: await AddGmailAsync(type == AccountType.GoogleWorkspace); break;
                case AccountType.Imap: await AddImapAsync(); break;
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    void SetBusy(bool busy) =>
        MicrosoftPanel.IsEnabled = WorkPanel.IsEnabled = GmailPanel.IsEnabled = ImapPanel.IsEnabled = KindLine.IsEnabled = BackButton.IsEnabled = ConfirmButton.IsEnabled = !busy;

    /// <summary>Dall'indirizzo al tipo: i domini noti subito, gli altri dal loro server di posta (MX).</summary>
    async Task DetectAsync()
    {
        if (!AccountTypes.LooksValid(Email))
        {
            ShowError(T("Scrivi l'indirizzo email completo, per esempio mario.rossi@studio.it."));
            return;
        }
        Detecting.Visibility = Visibility.Visible;
        AddressBox.IsEnabled = ConfirmButton.IsEnabled = false;
        try
        {
            var detected = await AccountTypes.DetectAsync(Email);
            if (!IsVisible) return;
            Log.Write($"account: {AccountTypes.DomainOf(Email)} riconosciuto come {detected}");
            Show(detected);
        }
        finally
        {
            Detecting.Visibility = Visibility.Collapsed;
            AddressBox.IsEnabled = ConfirmButton.IsEnabled = true;
        }
    }

    async Task AddMicrosoftAsync()
    {
        var clientId = ClientIdBox.Text.Trim();
        if (!Guid.TryParse(clientId, out _))
        {
            MicrosoftAppExpander.IsExpanded = true;
            ShowError(T("Inserisci l'ID dell'app Microsoft. Ha questa forma: 12345678-abcd-1234-abcd-1234567890ab."));
            return;
        }
        Store.Settings.MicrosoftClientId = clientId;
        Store.Save();
        MicrosoftAuth.OpenOver(this);
        try
        {
            var result = await MicrosoftAuth.SignInAsync();
            var email = result.Account.Username;
            if (Store.Settings.Accounts.Any(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            {
                ShowError(T("{0} è già in Missiva.", email));
                return;
            }
            Store.Settings.Accounts.Add(new MailAccount
            {
                Kind = AccountKind.Microsoft,
                Email = email,
                DisplayName = result.ClaimsPrincipal?.FindFirst("name")?.Value ?? email,
                HomeAccountId = result.Account.HomeAccountId.Identifier,
            });
            Store.Save();
            DialogResult = true;
        }
        catch (Exception ex) when (MicrosoftAuth.Canceled(ex))
        {
            // annullato dall'utente
        }
        catch (Exception ex)
        {
            Log.Error("accesso Microsoft", ex);
            ShowError(MicrosoftAuth.Explain(ex, Email, clientId).Text);
        }
    }

    /// <summary>
    /// Microsoft 365, lavoro o scuola: con l'app di Missiva e qualsiasi organizzazione, oppure con l'app registrata
    /// dall'organizzazione e la sua directory, solo per questo account. Gli errori di consenso si spiegano, e se serve un
    /// amministratore c'è il link da mandargli.
    /// </summary>
    async Task AddWorkAsync()
    {
        bool own = OwnAppCheck.IsChecked == true;
        var clientId = MicrosoftAuth.DefaultClientId;
        var tenant = MicrosoftAuth.Organizations;
        if (own)
        {
            clientId = WorkClientIdBox.Text.Trim();
            tenant = TenantBox.Text.Trim().ToLowerInvariant();
            if (!Guid.TryParse(clientId, out _))
            {
                ShowError(T("Inserisci l'ID dell'app della tua organizzazione. Ha questa forma: 12345678-abcd-1234-abcd-1234567890ab."));
                WorkClientIdBox.Focus();
                return;
            }
            if (tenant.Length == 0) tenant = MicrosoftAuth.Organizations; // app registrata per più organizzazioni
            else if (!Guid.TryParse(tenant, out _) && !(tenant.Contains('.') && !tenant.Any(char.IsWhiteSpace) && !tenant.Contains('/')))
            {
                ShowError(T("La directory è l'ID (come l'ID dell'app) o il dominio dell'organizzazione, per esempio studio.onmicrosoft.com o studio.it."));
                TenantBox.Focus();
                return;
            }
        }
        if (MicrosoftAuth.Blocked(clientId, tenant))
        {
            ShowError(T("L'app di Missiva ora accetta solo account Microsoft personali: la finestra di accesso non si apre. Usa l'app registrata dalla tua organizzazione, se c'è, con \"Usa l'app della mia organizzazione\"."));
            return;
        }
        MicrosoftAuth.OpenOver(this);
        try
        {
            var result = await SignInWorkAsync(tenant, clientId);
            var email = result.Account.Username;
            // 9188040d-...: la directory degli account personali (un ospite dell'organizzazione con un account Outlook.com).
            if (result.TenantId == "9188040d-6c67-4c5b-b112-36a304b66dad")
            {
                ShowError(T("{0} è un account Microsoft personale: aggiungilo come Outlook.com, Hotmail e Live.", email));
                return;
            }
            if (Store.Settings.Accounts.Any(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            {
                ShowError(T("{0} è già in Missiva.", email));
                return;
            }
            Store.Settings.Accounts.Add(new MailAccount
            {
                Kind = AccountKind.Microsoft,
                Email = email,
                DisplayName = result.ClaimsPrincipal?.FindFirst("name")?.Value ?? email,
                HomeAccountId = result.Account.HomeAccountId.Identifier,
                Tenant = tenant,
                ClientId = own ? clientId : null,
            });
            Store.Save();
            DialogResult = true;
        }
        catch (Exception ex) when (MicrosoftAuth.Canceled(ex))
        {
            // annullato dall'utente
        }
        catch (Exception ex)
        {
            Log.Error("accesso Microsoft 365", ex);
            var error = MicrosoftAuth.Explain(ex, Email, clientId);
            ShowError(error.Text);
            if (error.AdminConsent)
            {
                AdminLink.Text = MicrosoftAuth.AdminConsentUrl(clientId, tenant);
                AdminText.Text = T("Manda questo link a un amministratore di Microsoft 365 della tua organizzazione: aprendolo può approvare Missiva per tutti gli utenti. {0} Quando l'ha fatto, aggiungi di nuovo l'account.",
                                   MicrosoftAuth.AdminConsentScope(clientId));
                CopyAdminLink.Content = T("Copia");
                AdminPanel.Visibility = Visibility.Visible;
            }
        }
    }

    Task<AuthenticationResult> SignInWorkAsync(string tenant, string clientId)
    {
#if DEBUG
        // Prove degli errori senza finestre di accesso: --errore-msal 65001 fa rispondere a Microsoft con quel codice.
        var cli = Environment.GetCommandLineArgs();
        if (cli.SkipWhile(a => a != "--errore-msal").Skip(1).FirstOrDefault() is { } code)
        {
            Log.Write($"prova: errore MSAL finto AADSTS{code} per {clientId} ({tenant})");
            throw new MsalServiceException("invalid_grant", $"AADSTS{code}: errore finto della prova. Trace ID: 0 Correlation ID: 0");
        }
#endif
        return MicrosoftAuth.SignInAsync(tenant, clientId, AccountTypes.LooksValid(Email) ? Email : null);
    }

    void OnCopyAdminLink(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(AdminLink.Text);
            CopyAdminLink.Content = T("Copiato");
        }
        catch (Exception ex)
        {
            Log.Write("link per l'amministratore non copiato: " + ex.Message); // appunti occupati da un altro programma
            AdminLink.Focus();
            AdminLink.SelectAll();
        }
    }

    /// <summary>Un messaggio per l'amministratore, da un altro account: si apre in Nuovo messaggio e parte solo con Invia.</summary>
    void OnMailAdmin(object sender, RoutedEventArgs e)
    {
        AdminMailto = AdminConsentWindow.MailtoFor(AccountTypes.LooksValid(Email) ? Email : null, AdminLink.Text);
        DialogResult = false;
    }

    // ---------------------------------------------------------------- altro account (IMAP e SMTP)

    /// <summary>
    /// Le impostazioni del server trovate da sole (MailAutoconfig, come Thunderbird) e mostrate prima dell'accesso, nei
    /// campi della configurazione manuale: chi aggiunge l'account vede a quali server va la password.
    /// </summary>
    async Task FindConfigAsync()
    {
        configSearch?.Cancel();
        var search = configSearch = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var email = configFor = Email;
        var domain = AccountTypes.DomainOf(email);
        UserBox.Text = email;
        ConfigStatus.Text = T("Cerco le impostazioni del server…");
        ConfirmButton.IsEnabled = false;
        try
        {
            var found = await MailAutoconfig.FindAsync(email, search.Token);
            if (search != configSearch) return; // intanto un altro indirizzo
            foreignHosts = found is { Foreign: true } ? [found.Server.ImapHost, found.Server.SmtpHost] : [];
            if (found != null)
            {
                Fill(found.Server);
                ConfigStatus.Text = T("Impostazioni trovate ({0}): posta in arrivo {1}, in uscita {2}.", found.Source,
                                      Describe(found.Server.ImapHost, found.Server.ImapPort, found.Server.ImapSecurity),
                                      Describe(found.Server.SmtpHost, found.Server.SmtpPort, found.Server.SmtpSecurity)) + " " +
                                    (found.Foreign ? T("Attenzione: non sono server di {0}; controlla che siano quelli del tuo provider.", domain) : T("Puoi cambiarle in Configurazione manuale."));
                Log.Write($"account IMAP {domain}: impostazioni da {found.Source}");
            }
            else
            {
                Fill(new ImapServer { ImapHost = "imap." + domain, SmtpHost = "smtp." + domain, UserName = email });
                ConfigStatus.Text = T("Non ho trovato le impostazioni del server: scrivile in Configurazione manuale (sono nelle istruzioni del tuo provider).");
                ManualExpander.IsExpanded = true;
            }
        }
        catch (OperationCanceledException)
        {
            if (search == configSearch && IsVisible)
            {
                ConfigStatus.Text = T("La ricerca delle impostazioni ha impiegato troppo: scrivile in Configurazione manuale.");
                ManualExpander.IsExpanded = true;
            }
        }
        finally
        {
            if (search == configSearch) ConfirmButton.IsEnabled = true;
        }
    }

    static string Describe(string host, int port, MailSecurity security) =>
        $"{host}:{port} ({(security == MailSecurity.Ssl ? "SSL/TLS" : "STARTTLS")})";

    void Fill(ImapServer server)
    {
        ImapHostBox.Text = server.ImapHost;
        ImapPortBox.Text = server.ImapPort.ToString();
        ImapSecurityBox.SelectedIndex = server.ImapSecurity == MailSecurity.Ssl ? 0 : 1;
        SmtpHostBox.Text = server.SmtpHost;
        SmtpPortBox.Text = server.SmtpPort.ToString();
        SmtpSecurityBox.SelectedIndex = server.SmtpSecurity == MailSecurity.Ssl ? 0 : 1;
        UserBox.Text = server.UserName.Length > 0 ? server.UserName : Email;
    }

    /// <summary>I server dei campi, controllati; null (con l'errore mostrato) se manca qualcosa.</summary>
    ImapServer? ServerFromFields()
    {
        var server = ParseServer(ImapHostBox.Text, ImapPortBox.Text, ImapSecurityBox.SelectedIndex == 1, SmtpHostBox.Text, SmtpPortBox.Text,
                                 SmtpSecurityBox.SelectedIndex == 1, UserBox.Text);
        if (server == null)
        {
            ManualExpander.IsExpanded = true;
            ShowError(ServerFieldsError);
        }
        return server;
    }

    /// <summary>L'errore dei campi dei server (anche di Gestisci account), nella lingua di Missiva.</summary>
    public static string ServerFieldsError =>
        T("Controlla la configurazione manuale: servono i nomi dei server (come imap.provider.it), le porte (da 1 a 65535) e il nome utente.");

    /// <summary>
    /// I server scritti a mano, controllati (anche da Gestisci account): nomi senza spazi, barre e porte, porte da 1 a
    /// 65535, nome utente. La sicurezza è sempre SSL/TLS o STARTTLS: una casella senza cifratura non c'è. null se manca qualcosa.
    /// </summary>
    public static ImapServer? ParseServer(string imapHost, string imapPort, bool imapStartTls, string smtpHost, string smtpPort, bool smtpStartTls, string user)
    {
        static bool Host(string h) => h.Length > 0 && !h.Any(char.IsWhiteSpace) && !h.Contains('/') && !h.Contains(':');
        imapHost = imapHost.Trim().ToLowerInvariant();
        smtpHost = smtpHost.Trim().ToLowerInvariant();
        if (!Host(imapHost) || !Host(smtpHost) || !int.TryParse(imapPort.Trim(), out var imap) || imap is < 1 or > 65535
            || !int.TryParse(smtpPort.Trim(), out var smtp) || smtp is < 1 or > 65535 || user.Trim().Length == 0)
            return null;
        return new ImapServer
        {
            ImapHost = imapHost, ImapPort = imap, ImapSecurity = imapStartTls ? MailSecurity.StartTls : MailSecurity.Ssl,
            SmtpHost = smtpHost, SmtpPort = smtp, SmtpSecurity = smtpStartTls ? MailSecurity.StartTls : MailSecurity.Ssl,
            UserName = user.Trim(),
            // Microsoft 365 e Gmail salvano da sé in Posta inviata quello che parte via SMTP: una copia in più sarebbe un doppione.
            ServerSavesSent = smtpHost is "smtp.office365.com" or "smtp-mail.outlook.com" or "smtp.gmail.com",
        };
    }

    /// <summary>Prova IMAP e SMTP con la password (niente viene inviato), poi aggiunge l'account con la password cifrata.</summary>
    async Task AddImapAsync()
    {
        if (!AccountTypes.LooksValid(Email))
        {
            ShowAddress();
            ShowError(T("Scrivi l'indirizzo email completo, per esempio mario.rossi@studio.it."));
            return;
        }
        if (Store.Settings.Accounts.Any(a => a.Email.Equals(Email, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError(T("{0} è già in Missiva.", Email));
            return;
        }
        if (PasswordBox.Password.Length == 0)
        {
            ShowError(T("Scrivi la password della casella."));
            PasswordBox.Focus();
            return;
        }
        if (ServerFromFields() is not { } server) return;
        // Server di un altro dominio arrivati dai record SRV, e non cambiati a mano: la password parte solo con una conferma.
        var domain = AccountTypes.DomainOf(Email);
        var outside = new[] { server.ImapHost, server.SmtpHost }
            .Where(h => foreignHosts.Contains(h, StringComparer.OrdinalIgnoreCase) && !MailAutoconfig.InDomain(h, domain)).Distinct().ToList();
        // Al più due nomi, il server IMAP e quello SMTP.
        if (outside.Count > 0 && Dialog.Ask(this, outside.Count == 1 ? T("{0} non è un server di {1}", outside[0], domain) : T("{0} e {1} non sono server di {2}", outside[0], outside[1], domain),
                T("Li ha indicati il DNS del dominio (i record SRV o il suo server di posta), che una rete non affidabile può falsificare. Se non sei sicuro che siano i server del tuo provider, controllali in Configurazione manuale. Mandare lì la password della casella?"),
                T("Continua")) != Choice.Primary)
            return;
        server.ProtectedPassword = Store.Protect(PasswordBox.Password);
        var status = ConfigStatus.Text;
        ConfigStatus.Text = T("Provo il collegamento con {0} e {1}…", server.ImapHost, server.SmtpHost);
        using var trying = new CancellationTokenSource(TimeSpan.FromSeconds(MailLimits.ServerTrySeconds));
        try
        {
            var extensions = await ImapProvider.TestAsync(server, trying.Token);
            if (!IsVisible) return;
            Log.Write($"account IMAP aggiunto: {server.ImapHost}:{server.ImapPort} {server.ImapSecurity}, {server.SmtpHost}:{server.SmtpPort} {server.SmtpSecurity} ({extensions})");
            var local = Email[..Email.LastIndexOf('@')];
            Store.Settings.Accounts.Add(new MailAccount
            {
                Kind = AccountKind.Imap,
                Email = Email,
                DisplayName = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : local,
                Imap = server,
            });
            Store.Save();
            DialogResult = true;
        }
        catch (MailException ex)
        {
            Log.Write($"account IMAP non aggiunto ({server.ImapHost}, {server.SmtpHost}): {ex.Message}");
            ConfigStatus.Text = status;
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("account IMAP", ex);
            ConfigStatus.Text = status;
            ShowError(T("Collegamento non riuscito: {0}", ex.Message));
        }
    }

    async Task AddGmailAsync(bool workspace)
    {
        var clientId = GoogleIdBox.Text.Trim();
        var secret = GoogleSecretBox.Text.Trim();
        if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase) || secret.Length == 0)
        {
            GoogleAppExpander.IsExpanded = true;
            ShowError(T("Inserisci ID client e client secret dell'app Google. L'ID client finisce con .apps.googleusercontent.com."));
            return;
        }
        Store.Settings.GoogleClientId = clientId;
        Store.Settings.ProtectedGoogleClientSecret = Store.Protect(secret);
        Store.Save();

        GoogleWaiting.Visibility = Visibility.Visible;
        googleWait = new CancellationTokenSource(TimeSpan.FromMinutes(MailLimits.BrowserSignInMinutes));
        try
        {
            // Un account che ha già i permessi del calendario o dei contatti li tiene anche con un nuovo accesso da qui.
            var kept = Store.Settings.Accounts.FirstOrDefault(a => a.Kind == AccountKind.Gmail && a.Email.Equals(Email.Trim(), StringComparison.OrdinalIgnoreCase)) is { } known
                ? GoogleAuth.ExtraScopesOf(known) : [];
            var result = await GoogleAuth.SignInAsync(googleWait.Token, workspace && AccountTypes.LooksValid(Email) ? Email : null, kept);
            Activate(); // dal browser si torna a Missiva
            await GmailProvider.TestAsync(result.Email, result.AccessToken);
            googleWait.Token.ThrowIfCancellationRequested(); // chiusa nel frattempo: non si aggiunge nulla

            // Account già presente: è un nuovo accesso (per esempio dopo una revoca), si aggiorna solo il token.
            var existing = Store.Settings.Accounts.FirstOrDefault(a => a.Email.Equals(result.Email, StringComparison.OrdinalIgnoreCase));
            if (existing is { Kind: not AccountKind.Gmail })
            {
                ShowError(T("{0} è già in Missiva.", result.Email));
                return;
            }
            if (existing != null) GoogleAuth.Apply(existing, result); // aggiunto di nuovo: calendario e contatti qui non si chiedono, e se c'erano lo dice
            else
            {
                Store.Settings.Accounts.Add(new MailAccount
                {
                    Kind = AccountKind.Gmail,
                    Email = result.Email,
                    DisplayName = result.Name,
                    ProtectedRefreshToken = Store.Protect(result.RefreshToken),
                    GoogleScopes = result.Scope,
                });
            }
            Store.Save();
            DialogResult = true;
        }
        catch (OperationCanceledException) when (!IsVisible)
        {
            // finestra chiusa durante l'attesa
        }
        catch (OperationCanceledException)
        {
            // Con Google Workspace, un'app bloccata dall'amministratore resta sulla pagina d'errore di Google, senza tornare.
            ShowError(workspace
                ? T("L'accesso nel browser non è stato completato in tempo. Se Google ha mostrato \"admin_policy_enforced\" o \"Accesso bloccato\": {0}", GoogleAuth.AdminBlocked(clientId))
                : T("L'accesso nel browser non è stato completato in tempo. Riprova."));
        }
        catch (MailException ex)
        {
            Log.Error("accesso Google", ex);
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("accesso Google", ex);
            ShowError(T("Accesso non riuscito: {0}", ex.Message));
        }
        finally
        {
            GoogleWaiting.Visibility = Visibility.Collapsed;
            googleWait.Dispose();
            googleWait = null;
        }
    }
}
