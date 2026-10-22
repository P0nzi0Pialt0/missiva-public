using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Impostazioni sul modello del nuovo Outlook: categorie a sinistra, sezioni al centro, opzioni a destra.
/// Ogni modifica si salva e si applica subito, senza pulsante Salva.
/// </summary>
public sealed class SettingsWindow : Window
{
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static Settings S => Store.Settings;

    readonly Action changed;
    readonly Func<MailAccount, Task> removeAccount;
    readonly Action addAccount;
    readonly Func<MailAccount, Window, bool, Task> manageAccount;
    readonly Func<MailAccount, Window, Task>? autoReplies;
    readonly Func<MailAccount, RuleHost?>? ruleHost;
    readonly ListBox categories = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    readonly ListBox sections = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    readonly ContentControl page = new();

    public SettingsWindow(Action changed, Func<MailAccount, Task> removeAccount, Action addAccount, Func<MailAccount, Window, bool, Task> manageAccount,
                          Func<MailAccount, Window, Task>? autoReplies = null, Func<MailAccount, RuleHost?>? ruleHost = null)
    {
        this.changed = changed;
        this.removeAccount = removeAccount;
        this.addAccount = addAccount;
        this.manageAccount = manageAccount;
        this.autoReplies = autoReplies;
        this.ruleHost = ruleHost;
        Title = T("Impostazioni");
        Width = 980;
        Height = 700;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        categories.ItemContainerStyle = sections.ItemContainerStyle = (Style)FindResource("NavItem");
        // Il nome italiano resta nel Tag (Open, le prove e "impostazioni Categoria Sezione" lo usano); si mostra tradotto.
        categories.Items.Add(Nav("\uE713", N("Generali")));
        categories.Items.Add(Nav("\uE77B", N("Account")));
        categories.Items.Add(Nav("\uE715", N("Posta")));
        categories.SelectionChanged += (_, _) => FillSections();
        grid.Children.Add(categories);

        page.Margin = new Thickness(0, 0, 16, 0); // la barra di scorrimento Fluent sta sopra il contenuto: il testo non ci finisce sotto
        sections.SelectionChanged += (_, _) => ShowPage();
        sections.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(sections, 1);
        grid.Children.Add(sections);

        var content = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24, 16, 24, 16),
            Child = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        content.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        content.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        Grid.SetColumn(content, 2);
        grid.Children.Add(content);

        Content = grid;
        categories.SelectedIndex = 2; // Posta, Elenco messaggi: come nella tua registrazione
    }

    static ListBoxItem Nav(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = T(text) });
        var item = new ListBoxItem { Content = panel, Tag = text, MinHeight = 36, Padding = new Thickness(12, 0, 10, 0) };
        AutomationProperties.SetName(item, T(text)); // Assistente vocale leggeva "System.Windows.Controls.ListBoxItem"
        return item;
    }

    void FillSections()
    {
        sections.Items.Clear();
        var names = ((categories.SelectedItem as ListBoxItem)?.Tag as string) switch
        {
            "Generali" => new[] { N("Aspetto"), N("Notifiche"), N("Avvio e chiusura"), N("Copia di sicurezza"), N("Informazioni") },
            "Account" => new[] { N("Account di posta"), N("Firme") },
            _ => new[] { N("Elenco messaggi"), N("Riquadro di lettura"), N("Gestione messaggi"), N("Regole"), N("Scrittura e risposta"), N("Modelli") },
        };
        foreach (var n in names) sections.Items.Add(new ListBoxItem { Content = T(n), Tag = n, MinHeight = 36, Padding = new Thickness(12, 0, 10, 0) });
        sections.SelectedIndex = 0;
    }

    /// <summary>Apre una sezione (Posta, Regole): dal menu dell'account e nelle prove.</summary>
    /// <remarks>
    /// I nomi sono quelli italiani dei Tag ("Posta", "Regole": --impostazioni, il menu dell'account, MISSIVA_KEYS), in
    /// qualunque lingua; con Missiva in inglese valgono anche quelli mostrati ("Mail", "Rules").
    /// </remarks>
    public void Open(string category, string section)
    {
        static bool Named(ListBoxItem item, string name) => item.Tag is string tag && (tag == name || T(tag) == name);
        categories.SelectedItem = categories.Items.OfType<ListBoxItem>().FirstOrDefault(i => Named(i, category));
        sections.SelectedItem = sections.Items.OfType<ListBoxItem>().FirstOrDefault(i => Named(i, section));
    }

    void ShowPage()
    {
        page.Content = ((sections.SelectedItem as ListBoxItem)?.Tag as string) switch
        {
            "Elenco messaggi" => MessageListPage(),
            "Riquadro di lettura" => ReadingPanePage(),
            "Gestione messaggi" => MessageHandlingPage(),
            "Scrittura e risposta" => ComposingPage(),
            "Aspetto" => AppearancePage(),
            "Notifiche" => NotificationsPage(),
            "Avvio e chiusura" => StartupPage(),
            "Copia di sicurezza" => BackupPage(),
            "Informazioni" => AboutPage(),
            "Account di posta" => AccountsPage(),
            "Firme" => SignaturesPage(),
            "Modelli" => TemplatesPage(),
            "Regole" => RulesPage(),
            _ => null,
        };
    }

    // ---------------------------------------------------------------- Posta, Regole

    /// <summary>
    /// Come Impostazioni, Posta, Regole del nuovo Outlook: le regole di Outlook per gli account Microsoft, quelle di Missiva
    /// per Gmail e IMAP (RulesView).
    /// </summary>
    StackPanel RulesPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Regole")));
        p.Children.Add(Question(T("Le regole mettono in ordine la posta che arriva: spostano, segnano come letto, mettono categorie, eliminano, secondo mittente, destinatari, oggetto, testo, allegati o importanza. Si applicano nell'ordine dell'elenco. Crea regola, nel menu del tasto destro di un messaggio, ne comincia una con il suo mittente.")));
        if (ruleHost == null) return p;
        p.Children.Add(new RulesView(ruleHost));
        return p;
    }

    void Apply()
    {
        Store.Save();
        changed();
    }

    // ---------------------------------------------------------------- Generali, Informazioni

    /// <summary>
    /// Quale Missiva è questa: il numero di versione, con la data e il commit dell'ultima modifica compresa (AppVersion; 14
    /// ottobre 2026, richiesta dell'autore). Il testo si seleziona e si copia, per dirlo a chi aiuta.
    /// </summary>
    StackPanel AboutPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Informazioni")));
        var version = new TextBox
        {
            Text = "Missiva " + VersionText(AppVersion.Text), IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), FontSize = 15, TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(version, T("Versione di Missiva"));
        p.Children.Add(version);
        p.Children.Add(Question(T("Il terzo numero cresce a ogni modifica di Missiva. La data e il codice fra parentesi sono quelli dell'ultima modifica compresa in questa versione: per sapere se una correzione c'è, si confronta con la data in cui è stata fatta.")));
        p.Children.Add(Section(T("Aggiornamenti")));
        foreach (var part in UpdatesPart()) p.Children.Add(part);
        p.Children.Add(Rule());
        p.Children.Add(Section(T("Registri")));
        p.Children.Add(Question(T("Nella cartella dei dati di Missiva c'è il registro (missiva.log), che dice che cosa ha fatto e che cosa non è riuscito: serve a capire un problema.")));
        // Dal menu della rotellina, che ora apre subito le Impostazioni (ritocchi del 16 ottobre).
        var logs = new Button { Content = T("Apri la cartella dei registri"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(14, 5, 14, 5) };
        logs.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Store.Dir) { UseShellExecute = true });
        p.Children.Add(logs);
        return p;
    }

    /// <summary>
    /// La versione come la dice Informazioni. AppVersion.Text lo scrive la compilazione, in italiano: "0.3.525 del 14/10/2026
    /// (8c444de)", con ", con modifiche non ancora in un commit" se la cartella ne aveva. In italiano resta com'è; in inglese
    /// le parole sono in inglese e la data è quella breve di Lingua.Culture. Un testo d'altra forma (senza git, il solo
    /// numero) resta com'è.
    /// </summary>
    internal static string VersionText(string text)
    {
        if (!Lingua.English) return text;
        var m = System.Text.RegularExpressions.Regex.Match(text, @"^(?<number>\S+) del (?<day>\d\d)/(?<month>\d\d)/(?<year>\d{4}) \((?<commit>[0-9a-f]+)\)(?<changed>, con modifiche non ancora in un commit)?$");
        if (!m.Success || !DateTime.TryParseExact($"{m.Groups["year"].Value}-{m.Groups["month"].Value}-{m.Groups["day"].Value}", "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day))
            return text;
        string number = m.Groups["number"].Value, date = day.ToString("d", Lingua.Culture), commit = m.Groups["commit"].Value;
        return m.Groups["changed"].Success ? T("{0} del {1} ({2}), con modifiche non ancora in un commit", number, date, commit) : T("{0} del {1} ({2})", number, date, commit);
    }

    /// <summary>
    /// Gli aggiornamenti da GitHub (Updates; 15 ottobre 2026, richiesta dell'autore): in un rilascio la casella del controllo
    /// automatico e "Controlla ora", con l'esito; compilata dai sorgenti, o senza la chiave, che cosa fare per aggiornarla.
    /// </summary>
    IEnumerable<UIElement> UpdatesPart()
    {
        switch (Updates.Current)
        {
            case Updates.Mode.FromSources:
                yield return Question(T("Questa Missiva è stata compilata dai sorgenti: non controlla gli aggiornamenti. Si aggiorna rilanciando installa.ps1 dalla cartella dei sorgenti, oppure installando un rilascio pubblicato, che poi si aggiorna da solo."));
                yield break;
            case Updates.Mode.NoKey:
                yield return Question(T("Questa versione non ha la chiave con cui Missiva controlla la firma degli aggiornamenti: non li cerca. Si aggiorna scaricando un rilascio più nuovo e lanciando il suo installa.ps1."));
                yield break;
        }
        yield return Check(T("Controlla da solo gli aggiornamenti"), S.UpdatesAuto, v => S.UpdatesAuto = v, refresh: false);
        var status = Question(MainWindow.LastUpdateLine ?? T("Nessun controllo in questa sessione."));
        var now = new Button { Content = T("Controlla ora"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(14, 5, 14, 5) };
        now.Click += async (_, _) =>
        {
            if (Application.Current.MainWindow is not MainWindow main) return;
            now.IsEnabled = false;
            status.Text = T("Controllo in corso…");
            try { status.Text = await main.CheckUpdatesAsync(automatic: false); }
            finally { now.IsEnabled = true; }
        };
        yield return now;
        yield return status;
        yield return Question(T("Con la casella accesa Missiva chiede a GitHub qual è l'ultima versione pubblicata due minuti dopo l'avvio e poi una volta al giorno; se è più nuova lo dice con il pulsante \"Aggiornamento\" nella barra del titolo, e si aggiorna solo con Aggiorna. Accetta solo aggiornamenti firmati con la chiave di chi pubblica Missiva."));
    }

    // ---------------------------------------------------------------- pezzi comuni

    static TextBlock Heading(string text) => new() { Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };

    static TextBlock Section(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) };

    static TextBlock Question(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    /// <summary>
    /// Etichetta e casella sulla stessa riga; se non ci stanno, la casella va sotto. Prima la casella, larga almeno 260,
    /// usciva dal pannello e finiva sotto la barra di scorrimento, senza il bordo destro. Larga 220, alla misura normale
    /// della finestra sta ancora accanto a "Queste impostazioni si applicano a".
    /// </summary>
    static WrapPanel Labeled(string text, ComboBox box)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) });
        box.Width = 220;
        row.Children.Add(box);
        return row;
    }

    static Border Rule()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 0) };
        b.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
        return b;
    }

    StackPanel Radios<T>(string group, T current, Action<T> set, params (T Value, string Text)[] options) => Radios(group, current, set, true, options);

    /// <param name="refresh">
    /// Le scelte che cambiano l'elenco si applicano subito alla finestra principale (che ricarica la cartella); le altre
    /// (tema, notifiche, segna come letto) si salvano soltanto.
    /// </param>
    StackPanel Radios<T>(string group, T current, Action<T> set, bool refresh, params (T Value, string Text)[] options)
    {
        var panel = new StackPanel();
        foreach (var (value, text) in options)
        {
            var radio = new RadioButton { Content = text, GroupName = group, IsChecked = Equals(value, current), Margin = new Thickness(0, 2, 0, 2) };
            radio.Checked += (_, _) =>
            {
                set(value);
                if (refresh) Apply();
                else Store.Save();
            };
            panel.Children.Add(radio);
        }
        return panel;
    }

    CheckBox Check(string text, bool value, Action<bool> set, bool refresh = true)
    {
        var check = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 3, 0, 3) };
        // Checked e Unchecked, non Click: Assistente vocale e UI Automation (Toggle) cambiano la casella senza Click, e la
        // scelta non si salvava.
        void Changed(object sender, RoutedEventArgs e)
        {
            set(check.IsChecked == true);
            if (refresh) Apply();
            else Store.Save();
        }
        check.Checked += Changed;
        check.Unchecked += Changed;
        return check;
    }

    // ---------------------------------------------------------------- Posta, Elenco messaggi

    StackPanel MessageListPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Elenco messaggi")));

        // Posta in arrivo evidenziata, per account (solo Microsoft)
        var microsoft = S.Accounts.Where(a => a.Kind == AccountKind.Microsoft && a.SharedOf == null).ToList();
        p.Children.Add(Section(T("Posta in arrivo evidenziata")));
        if (microsoft.Count == 0)
        {
            p.Children.Add(Question(T("Disponibile per gli account Microsoft. Per Gmail la Posta in arrivo è unica.")));
        }
        else
        {
            var accountBox = new ComboBox { ItemsSource = microsoft, DisplayMemberPath = nameof(MailAccount.Email), SelectedIndex = 0 };
            p.Children.Add(Labeled(T("Queste impostazioni si applicano a"), accountBox));
            p.Children.Add(Question(T("Vuoi che Missiva ordini i tuoi messaggi di posta elettronica per poterti dedicare alle cose più importanti?")));
            var focusedCheck = new CheckBox { Content = T("Ordina i messaggi in Evidenziata e Altra"), IsChecked = S.IsFocused(microsoft[0]) };
            bool switching = false; // la casella mostra l'account scelto: non è una scelta da salvare
            accountBox.SelectionChanged += (_, _) =>
            {
                if (accountBox.SelectedItem is not MailAccount a) return;
                switching = true;
                focusedCheck.IsChecked = S.IsFocused(a);
                switching = false;
            };
            // Checked e Unchecked, non Click, come in Check: anche con Assistente vocale la scelta si salva.
            void FocusedChanged(object sender, RoutedEventArgs e)
            {
                if (switching || accountBox.SelectedItem is not MailAccount a) return;
                S.Focused[a.Id] = focusedCheck.IsChecked == true;
                Apply();
            }
            focusedCheck.Checked += FocusedChanged;
            focusedCheck.Unchecked += FocusedChanged;
            p.Children.Add(focusedCheck);
        }
        p.Children.Add(Rule());

        p.Children.Add(Section(T("Dimensioni e spaziatura del testo")));
        p.Children.Add(Question(T("Missiva adatterà l'elenco dei messaggi senza cambiare i messaggi ricevuti o inviati.")));
        p.Children.Add(Radios("size", S.TextSize, v => S.TextSize = v,
            (TextSize.Small, T("Piccola")), (TextSize.Medium, T("Media")), (TextSize.Large, T("Di grandi dimensioni"))));
        p.Children.Add(Rule());

        p.Children.Add(Section(T("Organizzazione messaggi")));
        p.Children.Add(Question(T("Come vuoi che sia organizzato l'Elenco dei messaggi?")));
        p.Children.Add(Radios("group", S.GroupByConversation, v => S.GroupByConversation = v,
            (true, T("Raggruppa i messaggi in base a conversazione")), (false, T("Non raggruppare i messaggi"))));
        p.Children.Add(Question(T("Come vuoi organizzare il riquadro di lettura?")));
        p.Children.Add(Radios("reading", S.ReadingWholeConversation, v => S.ReadingWholeConversation = v,
            (true, T("Mostra tutti i messaggi della conversazione selezionata")), (false, T("Mostra solo il messaggio selezionato"))));
        p.Children.Add(Rule());

        p.Children.Add(Section(T("Anteprima del testo")));
        p.Children.Add(Question(T("Vuoi visualizzare l'anteprima del testo?")));
        p.Children.Add(Radios("preview", S.PreviewLines, v => S.PreviewLines = v, (0, "Off"), (1, T("1 riga")), (2, T("2 righe"))));
        p.Children.Add(Question(T("Come vuoi che i messaggi vengano visualizzati?")));
        p.Children.Add(Radios("order", S.SenderFirst, v => S.SenderFirst = v, (true, T("Nome del mittente all'inizio")), (false, T("Oggetto all'inizio"))));
        p.Children.Add(Rule());

        p.Children.Add(Section(T("Personalizza il layout dell'elenco dei messaggi")));
        p.Children.Add(Check(T("Mostra le immagini dei mittenti"), S.ShowSenderImages, v => S.ShowSenderImages = v));
        p.Children.Add(Check(T("Mostra il logo dell'azienda del mittente"), S.ShowSenderLogos, v => S.ShowSenderLogos = v));
        p.Children.Add(Question(T("Al posto delle iniziali: il logo certificato del marchio (quello che mostra anche Gmail) e, per i siti da cui ricevi posta da qualche settimana, l'icona del sito. Mai per la posta indesiderata. Missiva scarica il logo dal sito del mittente, senza dirgli quale messaggio hai ricevuto.")));
        p.Children.Add(Check(T("Mostra l'anteprima degli allegati"), S.ShowAttachmentPreviews, v => S.ShowAttachmentPreviews = v));
        p.Children.Add(Check(T("Visualizza le intestazioni date"), S.ShowDateHeaders, v => S.ShowDateHeaders = v));
        return p;
    }

    // ---------------------------------------------------------------- Posta, Riquadro di lettura

    StackPanel ReadingPanePage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Riquadro di lettura")));

        p.Children.Add(Section(T("Posizione")));
        p.Children.Add(Question(T("Dove vuoi vedere il riquadro di lettura?")));
        p.Children.Add(Radios("pane", S.ReadingPane, v => S.ReadingPane = v,
            (ReadingPanePosition.Right, T("A destra")), (ReadingPanePosition.Bottom, T("In basso")),
            (ReadingPanePosition.Off, T("Nascosto (i messaggi si aprono con doppio clic)"))));
        p.Children.Add(Rule());

        p.Children.Add(Section(T("Aspetto dei messaggi")));
        p.Children.Add(Question(T("Con il tema scuro di Windows i messaggi vengono adattati ai colori scuri. Il pulsante con il sole in alto a destra di ogni messaggio lo mostra su fondo chiaro.")));
        p.Children.Add(Check(T("Mostra sempre i messaggi su fondo chiaro"), S.LightReadingPane, v => S.LightReadingPane = v));
        p.Children.Add(Question(T("Zoom del testo dei messaggi")));
        var zoom = new ComboBox { ItemsSource = new[] { 80, 90, 100, 110, 125, 150 }, SelectedItem = S.Zoom, Width = 120, HorizontalAlignment = HorizontalAlignment.Left, ItemStringFormat = "{0}%" };
        zoom.SelectionChanged += (_, _) =>
        {
            if (zoom.SelectedItem is int z)
            {
                S.Zoom = z;
                Apply();
            }
        };
        p.Children.Add(zoom);
        p.Children.Add(Rule());

        p.Children.Add(Section(T("Immagini esterne")));
        p.Children.Add(Question(T("Le immagini scaricate da Internet dicono al mittente se e quando hai letto il messaggio, per questo sono bloccate. Quando premi Mostra immagini su un messaggio, Missiva le mostra da quel momento in poi per quel mittente, in tutti gli account: per le aziende vale tutto il dominio (google.com comprende accounts.google.com), per la posta personale come gmail.com solo quell'indirizzo. Ecco per chi le mostra:")));
        var senders = new StackPanel();
        void Fill()
        {
            senders.Children.Clear();
            if (S.ImageSenders.Count == 0)
            {
                senders.Children.Add(Question(T("Nessuno.")));
                return;
            }
            foreach (var email in S.ImageSenders.OrderBy(e => e, StringComparer.CurrentCultureIgnoreCase).ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var remove = new Button { Content = T("Non mostrarle più"), Padding = new Thickness(10, 3, 10, 3) };
                remove.Click += (_, _) =>
                {
                    S.ImageSenders.Remove(email);
                    Apply();
                    Fill();
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(new TextBlock { Text = email, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                senders.Children.Add(row);
            }
        }
        Fill();
        p.Children.Add(senders);
        return p;
    }

    // ---------------------------------------------------------------- Generali

    /// <summary>
    /// Come Impostazioni, Generali, Aspetto nel nuovo Outlook: chiaro, scuro o come Windows, applicato subito
    /// (App.SetTheme, lo stesso gestore del cambio di tema di Windows) e ricordato.
    /// </summary>
    StackPanel AppearancePage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Aspetto")));
        foreach (var part in LanguagePart()) p.Children.Add(part);
        p.Children.Add(Rule());
        p.Children.Add(Section(T("Tema")));
        p.Children.Add(Question(T("Missiva può seguire il tema delle app di Windows, che può cambiare da solo all'alba e al tramonto, o restare sempre chiara o sempre scura.")));
        p.Children.Add(Radios("theme", S.Theme, v => App.Current.SetTheme(v), false,
            (AppTheme.System, T("Come Windows")), (AppTheme.Light, T("Chiaro")), (AppTheme.Dark, T("Scuro"))));
        p.Children.Add(Rule());
        p.Children.Add(Section(T("Cartelle")));
        p.Children.Add(Check(T("Mostra \"Tutti gli account\" in cima alle cartelle"), S.ShowUnified, on => S.ShowUnified = on));
        p.Children.Add(Question(T("Con due o più account: Posta in arrivo, Non letti, Contrassegnati, Bozze e Posta inviata di tutti gli account insieme.")));
        return p;
    }

    /// <summary>
    /// La lingua di Missiva (Lingua, 21 ottobre 2026, richiesta dell'autore): come Windows (di base), italiano o inglese. Il
    /// titolo è nelle due lingue, perché lo trovi anche chi non legge quella in cui Missiva è adesso; "Italiano" e "English"
    /// restano sempre scritti così. La scelta si salva subito e vale dal prossimo avvio: se cambia la lingua di Missiva, sotto
    /// lo dice, con Riavvia ora (App.RestartBecause, l'uscita di Esci con le domande delle scritture aperte).
    /// </summary>
    IEnumerable<UIElement> LanguagePart()
    {
        string[] values = ["", "it", "en"];
        var title = T("Lingua (Language)");
        var box = new ComboBox
        {
            ItemsSource = new[] { T("Come Windows"), "Italiano", "English" },
            SelectedIndex = Math.Max(0, Array.IndexOf(values, S.Language)), Width = 220, HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(box, title);
        var notice = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        notice.Children.Add(Question(T("La lingua cambia al prossimo avvio di Missiva.")));
        var restart = new Button { Content = T("Riavvia ora"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 5, 14, 5) };
        restart.SetResourceReference(StyleProperty, "AccentButtonStyle");
        restart.Click += (_, _) =>
        {
            Close();
            App.Current.Dispatcher.BeginInvoke(() => App.Current.RestartBecause("per la lingua nuova"), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        notice.Children.Add(restart);
        void Show() => notice.Visibility = Lingua.ChangesAtRestart(S.Language) ? Visibility.Visible : Visibility.Collapsed;
        Show();
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            S.Language = values[box.SelectedIndex];
            Store.Save();
            Log.Write($"lingua: impostazione \"{S.Language}\" (vale dal prossimo avvio)");
            Show();
        };
        yield return Section(title);
        yield return box;
        yield return notice;
    }

    /// <summary>
    /// Come Impostazioni, Generali, Notifiche nel nuovo Outlook: per ogni account le notifiche dei nuovi messaggi (Missiva
    /// controlla ogni due minuti), e il suono.
    /// </summary>
    StackPanel NotificationsPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Notifiche")));
        p.Children.Add(Section(T("Nuovi messaggi")));
        p.Children.Add(Question(T("Quando nella Posta in arrivo arriva un messaggio nuovo Missiva lo dice con una notifica di Windows: subito con Gmail e gli altri account IMAP (il server avvisa), entro 30 secondi con gli account Microsoft, e comunque con il controllo di ogni due minuti. Spegnile per gli account che non vuoi seguire: i messaggi arrivano lo stesso nell'elenco.")));
        if (S.Accounts.Count == 0) p.Children.Add(Question(T("Nessun account.")));
        foreach (var account in S.Accounts.ToList())
            p.Children.Add(Check(T("Notifiche per {0}", account.Email), !S.NotificationsOff.Contains(account.Id), on =>
            {
                S.NotificationsOff.Remove(account.Id);
                if (!on) S.NotificationsOff.Add(account.Id);
            }, refresh: false));
        p.Children.Add(Rule());
        p.Children.Add(Section(T("Suono")));
        p.Children.Add(Check(T("Riproduci un suono con le notifiche"), S.NotificationSound, on => S.NotificationSound = on, refresh: false));
        foreach (var part in IconNumberPart()) p.Children.Add(part);
        return p;
    }

    /// <summary>
    /// Il numero sull'icona di Missiva (ritocchi del 16 ottobre 2026, richiesta dell'autore): che cosa conta e per quali
    /// account, con la stessa regola sull'icona della barra delle applicazioni e nel suggerimento dell'area di notifica
    /// (IconCount). Ogni cambio si salva e il numero si rifà subito, senza ricaricare l'elenco.
    /// </summary>
    IEnumerable<UIElement> IconNumberPart()
    {
        static void Refresh()
        {
            if (Application.Current.MainWindow is MainWindow main) main.RefreshBadge();
        }
        var accounts = new List<CheckBox>();
        yield return Rule();
        yield return Section(T("Numero sull'icona di Missiva"));
        yield return Question(T("Il numero nel cerchio sull'icona di Missiva nella barra delle applicazioni; lo stesso dice il suggerimento dell'icona vicino all'orologio."));
        yield return Radios("icona", S.IconNumber, v =>
            {
                S.IconNumber = v;
                foreach (var box in accounts) box.IsEnabled = v != IconNumberKind.None;
                Refresh();
            }, false,
            (IconNumberKind.Unread, T("Messaggi da leggere nella Posta in arrivo")),
            (IconNumberKind.New, T("Messaggi nuovi arrivati da quando hai guardato Missiva")),
            (IconNumberKind.None, T("Nessun numero")));
        yield return new TextBlock { Text = T("Conta questi account"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) };
        if (S.Accounts.Count == 0) yield return Question(T("Nessun account."));
        foreach (var account in S.Accounts.ToList())
        {
            var box = Check(account.Email, !S.IconNumberOff.Contains(account.Id), on =>
            {
                S.IconNumberOff.Remove(account.Id);
                if (!on) S.IconNumberOff.Add(account.Id);
                Refresh();
            }, refresh: false);
            box.IsEnabled = S.IconNumber != IconNumberKind.None;
            accounts.Add(box);
            yield return box;
        }
    }

    /// <summary>
    /// Come Outlook classico e Thunderbird: chiudendo la finestra Missiva resta nell'area di notifica (di base), oppure si
    /// chiude del tutto; e può avviarsi all'accesso a Windows, nascosta (HKCU Run con --nascosta).
    /// </summary>
    StackPanel StartupPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Avvio e chiusura")));
        p.Children.Add(Section(T("Chiudendo la finestra")));
        p.Children.Add(Question(T("Nell'area di notifica, vicino all'orologio, Missiva continua a controllare la posta ogni due minuti, a mostrarti le notifiche, a mandare i messaggi pianificati e a riportare i messaggi posposti, usando poca memoria. Riaprila con un clic sull'icona; per chiuderla del tutto, tasto destro sull'icona, Esci.")));
        p.Children.Add(Radios("tray", S.KeepInTray, v =>
            {
                S.KeepInTray = v;
                App.Current.UpdateTray();
            }, false,
            (true, T("Resta nell'area di notifica")), (false, T("Chiudi Missiva del tutto"))));
        p.Children.Add(Rule());
        p.Children.Add(Section(T("Avvio")));
#if !(DEBUG || PROVA)
        // Lo stato vero, non solo l'impostazione di Missiva: spenta da App di avvio di Windows (o con il valore tolto da Run)
        // la casella è spenta. Nella versione di prova la chiave Run non si scrive mai, e resta l'impostazione.
        if (App.StartsWithWindows() != S.StartWithWindows)
        {
            S.StartWithWindows = !S.StartWithWindows;
            Store.Save();
            Log.Write($"avvio con Windows: per Windows è {(S.StartWithWindows ? "acceso" : "spento")}, l'impostazione di Missiva lo segue");
        }
#endif
        p.Children.Add(Check(T("Avvia Missiva all'accesso a Windows, nascosta nell'area di notifica"), S.StartWithWindows, on =>
        {
            S.StartWithWindows = on;
            App.ApplyStartWithWindows(byUser: true);
        }, refresh: false));
        p.Children.Add(Question(T("Così le notifiche arrivano da subito e la finestra si apre all'istante, già con la posta.")));
        return p;
    }

    /// <summary>
    /// Copia di sicurezza (audit, F-29; Backup e BackupUi): tutto ciò che in Missiva è dell'utente in un file cifrato con una
    /// password sua, da rimettere su questo PC o su un altro. Esporta non cambia nulla; Importa sostituisce tutto, dopo una
    /// domanda, e si completa al riavvio. Con un'importazione in attesa la pagina lo dice e la lascia annullare. Se nella
    /// cartella dei dati ci sono ancora i file messi da parte da un'importazione, in fondo c'è "Elimina i dati di prima".
    /// </summary>
    StackPanel BackupPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Copia di sicurezza")));
        p.Children.Add(Question(T("Un file solo con quello che in Missiva è tuo, da rimettere su questo PC o su un PC nuovo: impostazioni, account, firme, modelli, regole di Missiva, categorie e colori, Preferiti e ordine delle cartelle, contatti e gruppi di Missiva, rubrica dei destinatari, contrassegni e messaggi posposti. La posta non c'è: sta sui server e si riscarica.")));
        if (Backup.HasPending(Store.Dir))
        {
            var waiting = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 10, 0, 0) };
            waiting.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            waiting.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            var box = new StackPanel();
            var waits = Backup.Pending(Store.Dir) is { } copy ? T("Un'importazione aspetta il riavvio di Missiva (copia del {0})", Backup.Describe(copy).When)
                : T("Un'importazione aspetta il riavvio di Missiva");
            box.Children.Add(new TextBlock { Text = waits, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            box.Children.Add(Question(T("Al prossimo avvio i dati di adesso vengono messi da parte e al loro posto vanno quelli della copia. Fino ad allora non cambia nulla.")));
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var restart = new Button { Content = T("Riavvia ora"), Margin = new Thickness(0, 0, 8, 0) };
            restart.SetResourceReference(StyleProperty, "AccentButtonStyle");
            restart.Click += (_, _) =>
            {
                Close();
                App.Current.Dispatcher.BeginInvoke(App.Current.Restart, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            var forget = new Button { Content = T("Annulla l'importazione") };
            forget.Click += (_, _) =>
            {
                Backup.CancelPending(Store.Dir);
                Log.Write("copia di sicurezza: importazione in attesa annullata");
                ShowPage();
            };
            row.Children.Add(restart);
            row.Children.Add(forget);
            box.Children.Add(row);
            waiting.Child = box;
            p.Children.Add(waiting);
        }

        p.Children.Add(Section(T("Esporta")));
        p.Children.Add(Question(T("Il file è cifrato con una password che scegli tu: senza quella non si apre, e Missiva non può recuperarla. Gli accessi agli account non ci sono: dove importi la copia ogni account chiede di nuovo l'accesso (per gli account IMAP puoi scegliere di includere la password).")));
        var export = new Button { Content = T("Esporta…"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 120 };
        AutomationProperties.SetName(export, T("Esporta una copia di sicurezza"));
        export.Click += (_, _) => BackupUi.Export(this);
        p.Children.Add(export);
        p.Children.Add(Rule());

        p.Children.Add(Section(T("Importa")));
        p.Children.Add(Question(T("Importare sostituisce tutto quello che Missiva ha ora su questo PC con quello della copia, senza unire. Prima Missiva ti dice che cosa c'è nella copia e te lo chiede; i dati di adesso restano nella cartella dei dati, messi da parte con la data nel nome. Poi Missiva si riavvia.")));
        var import = new Button { Content = T("Importa…"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 120 };
        AutomationProperties.SetName(import, T("Importa una copia di sicurezza"));
        import.Click += (_, _) =>
        {
            BackupUi.Import(this);
            if (IsVisible) ShowPage(); // un'importazione rimandata a dopo: la pagina lo dice
        };
        p.Children.Add(import);

        // I dati di prima di un'importazione, se ce ne sono: tengono ancora gli accessi degli account di allora. Missiva non
        // li toglie da sola (servono a tornare indietro): li si elimina da qui, dopo una domanda (scelte del 12 ottobre, T-20).
        if (Backup.AsideFiles(Store.Dir) is { Count: > 0 } aside)
        {
            p.Children.Add(Rule());
            p.Children.Add(Section(T("Dati di prima di un'importazione")));
            p.Children.Add(Question(BackupUi.AsideLine(aside)));
            var delete = new Button { Content = T("Elimina i dati di prima…"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 120 };
            AutomationProperties.SetName(delete, T("Elimina i dati di prima dell'importazione"));
            delete.Click += (_, _) =>
            {
                var now = Backup.AsideFiles(Store.Dir); // quelli che ci sono adesso: la domanda dice di questi
                var (heading, body, primary) = BackupUi.AsideQuestion(now);
                if (now.Count > 0 && Dialog.AskCancelFirst(this, heading, body, primary) == Choice.Primary)
                {
                    var (_, failed) = Backup.DeleteAside(Store.Dir);
                    if (failed.Count > 0)
                        Dialog.Show(this, T("Non tutti i dati di prima sono stati eliminati"), failed.Count == 1
                            ? T("Un file non si è lasciato eliminare, forse perché in uso da un altro programma: {0}. Chiudi quel programma e riprova da qui.", failed[0])
                            : T("{0} file non si sono lasciati eliminare, forse perché in uso da un altro programma: {1}. Chiudi quel programma e riprova da qui.", failed.Count, string.Join(", ", failed)));
                }
                ShowPage(); // senza più file la parte sparisce
            };
            p.Children.Add(delete);
        }
        return p;
    }

    // ---------------------------------------------------------------- Posta, Gestione messaggi

    /// <summary>
    /// Come Impostazioni, Posta, Gestione messaggi nel nuovo Outlook: quando un messaggio aperto nel riquadro diventa letto.
    /// Scorrendo con le frecce, "subito" segna letti tutti i messaggi toccati.
    /// </summary>
    StackPanel MessageHandlingPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Gestione messaggi")));
        p.Children.Add(Section(T("Segna come letto")));
        p.Children.Add(Question(T("Quando vuoi che i messaggi aperti nel riquadro di lettura vengano segnati come letti?")));
        var seconds = new ComboBox
        {
            ItemsSource = new[] { 1, 2, 3, 5, 10, 15, 30 }, SelectedItem = S.MarkReadSeconds, ItemStringFormat = T("{0} secondi"),
            IsEnabled = S.MarkRead == MarkReadMode.AfterDelay,
        };
        AutomationProperties.SetName(seconds, T("Secondi di attesa"));
        seconds.SelectionChanged += (_, _) =>
        {
            if (seconds.SelectedItem is not int s) return;
            S.MarkReadSeconds = s;
            Store.Save();
        };
        var radios = Radios("markread", S.MarkRead, v =>
            {
                S.MarkRead = v;
                seconds.IsEnabled = v == MarkReadMode.AfterDelay;
            }, false,
            (MarkReadMode.Immediately, T("Subito, appena il messaggio si apre")),
            (MarkReadMode.AfterDelay, T("Quando il messaggio resta aperto nel riquadro per")),
            (MarkReadMode.OnSelectionChange, T("Quando la selezione cambia")),
            (MarkReadMode.Never, T("Non segnarli automaticamente")));
        var wait = Labeled(T("Attesa"), seconds);
        wait.Margin = new Thickness(28, 0, 0, 4);
        radios.Children.Insert(2, wait); // sotto la sua scelta
        p.Children.Add(radios);
        p.Children.Add(Question(T("Puoi sempre segnarli a mano: Ctrl+Q li segna come letti, Ctrl+U da leggere, come la busta fra le azioni rapide dei messaggi.")));
        return p;
    }

    // ---------------------------------------------------------------- Posta, Scrittura e risposta

    /// <summary>
    /// Come Impostazioni, Posta, Scrittura e risposta nel nuovo Outlook: Annulla invio, cioè quanti secondi il messaggio
    /// aspetta prima di partire, con "Invio in corso… Annulla" in fondo all'elenco.
    /// </summary>
    StackPanel ComposingPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Scrittura e risposta")));
        p.Children.Add(Section(T("Annulla invio")));
        p.Children.Add(Question(T("Dopo Invia la finestra si chiude subito, ma il messaggio aspetta qualche secondo prima di partire: in fondo all'elenco \"Invio in corso…\" con Annulla, che riapre il messaggio com'era.")));
        p.Children.Add(Radios("undosend", S.UndoSendSeconds, v => S.UndoSendSeconds = v, false,
            (0, T("Non aspettare: il messaggio parte subito")), (5, T("5 secondi")), (10, T("10 secondi"))));
        p.Children.Add(Rule());
        p.Children.Add(Section(T("Allegati dimenticati")));
        p.Children.Add(Question(T("Se nel testo scrivi di un allegato (\"in allegato\", \"allego\", \"attached\") e non c'è nessun allegato, a Invia Missiva chiede se vuoi inviare lo stesso. La firma e il messaggio a cui rispondi non contano.")));
        p.Children.Add(Check(T("Avvisami se manca l'allegato di cui ho scritto"), S.AttachmentReminder, v => S.AttachmentReminder = v, refresh: false));
        p.Children.Add(Rule());
        FormatSection(p);
        p.Children.Add(Rule());
        p.Children.Add(Section(T("Pianifica invio")));
        p.Children.Add(Question(T("Dalla freccia accanto a Invia. Con gli account Microsoft il messaggio aspetta nella Posta in uscita di Outlook e parte all'ora scelta anche con Missiva chiusa. Con Gmail e gli altri account resta nelle Bozze e lo manda Missiva all'ora scelta, anche con la finestra chiusa se Missiva resta nell'area di notifica (Generali, Avvio e chiusura); se Missiva è chiusa del tutto, appena la riapri.")));
        return p;
    }

    /// <summary>
    /// Formato del messaggio, come nel nuovo Outlook: il carattere, la dimensione e il colore con cui cominciano i nuovi
    /// messaggi, le risposte e gli inoltri (ComposeWindow.InitFormat, FlowToHtml.BaseStyle), con l'anteprima.
    /// </summary>
    void FormatSection(StackPanel p)
    {
        p.Children.Add(Section(T("Formato del messaggio")));
        p.Children.Add(Question(T("Il carattere con cui cominciano i nuovi messaggi, le risposte e gli inoltri. Nella scrittura la barra lo cambia per il testo scelto.")));
        var font = new ComboBox { ItemsSource = ComposeWindow.Fonts.Contains(S.ComposeFont) ? ComposeWindow.Fonts : ComposeWindow.Fonts.Prepend(S.ComposeFont).ToArray(), SelectedItem = S.ComposeFont };
        AutomationProperties.SetName(font, T("Carattere dei messaggi"));
        var sizes = ComposeWindow.Sizes.Select(v => v.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)).ToList();
        var size = new ComboBox { ItemsSource = sizes, SelectedItem = S.ComposeFontSize.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) };
        AutomationProperties.SetName(size, T("Dimensione del carattere dei messaggi"));
        var color = new ComboBox { ItemsSource = ComposeWindow.TextColors.Select(c => T(c.Name)).ToList() }; // i nomi della scrittura, tradotti se segnati con N
        color.SelectedIndex = Math.Max(0, Array.FindIndex(ComposeWindow.TextColors, c => c.Hex.Equals(S.ComposeColor, StringComparison.OrdinalIgnoreCase)));
        AutomationProperties.SetName(color, T("Colore del testo dei messaggi"));
        p.Children.Add(Labeled(T("Carattere"), font));
        p.Children.Add(Labeled(T("Dimensione"), size));
        p.Children.Add(Labeled(T("Colore"), color));
        var sample = new TextBlock { Text = T("Gentile cliente, in allegato il preventivo richiesto."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        void Preview()
        {
            sample.FontFamily = new FontFamily(S.ComposeFont);
            sample.FontSize = S.ComposeFontSize * 96 / 72;
            if (FlowToHtml.ParseColor(S.ComposeColor) is { } c) sample.Foreground = new SolidColorBrush(c);
            else sample.ClearValue(TextBlock.ForegroundProperty);
        }
        font.SelectionChanged += (_, _) =>
        {
            if (font.SelectedItem is not string f) return;
            S.ComposeFont = f;
            Store.Save();
            Preview();
        };
        size.SelectionChanged += (_, _) =>
        {
            if (size.SelectedItem is not string text || !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var points)) return;
            S.ComposeFontSize = points;
            Store.Save();
            Preview();
        };
        color.SelectionChanged += (_, _) =>
        {
            if (color.SelectedIndex < 0) return;
            S.ComposeColor = ComposeWindow.TextColors[color.SelectedIndex].Hex;
            Store.Save();
            Preview();
        };
        Preview();
        p.Children.Add(sample);
    }

    // ---------------------------------------------------------------- Posta, Modelli

    /// <summary>
    /// Come I miei modelli del nuovo Outlook: i testi che si ripetono (preventivi, solleciti, coordinate bancarie), con un
    /// nome, l'oggetto facoltativo e il testo scritto con l'editor delle firme, per tutti gli account o per uno. Nella
    /// scrittura il pulsante Modelli li inserisce. Cifrati con DPAPI; si salvano mentre scrivi.
    /// </summary>
    StackPanel TemplatesPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Modelli")));
        p.Children.Add(Question(T("Testi che si ripetono, da inserire con il pulsante Modelli della scrittura. {nome} diventa il nome del primo destinatario, {data} la data di oggi. Restano su questo PC, cifrati.")));
        var list = new ListBox { MinHeight = 90, MaxHeight = 180, DisplayMemberPath = nameof(MailTemplate.Name), Margin = new Thickness(0, 6, 0, 0) };
        list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "NavItem");
        var itemStyle = new Style(typeof(ListBoxItem), (Style)FindResource("NavItem"));
        itemStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new System.Windows.Data.Binding(nameof(MailTemplate.Name))));
        list.ItemContainerStyle = itemStyle; // per UI Automation il nome, non "Missiva.MailTemplate"
        AutomationProperties.SetName(list, T("Modelli"));
        p.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var add = new Button { Content = T("Nuovo modello") };
        add.SetResourceReference(StyleProperty, "AccentButtonStyle");
        var remove = new Button { Content = T("Elimina"), Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(remove, T("Elimina il modello"));
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        p.Children.Add(buttons);

        var form = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var name = new TextBox();
        AutomationProperties.SetName(name, T("Nome del modello"));
        var subject = new TextBox();
        AutomationProperties.SetName(subject, T("Oggetto del modello"));
        var owners = new List<(string? Id, string Text)> { (null, T("Tutti gli account")) };
        owners.AddRange(S.Accounts.Select(a => ((string?)a.Id, a.Email)));
        var owner = new ComboBox { ItemsSource = owners.Select(o => o.Text).ToList(), SelectedIndex = 0 };
        AutomationProperties.SetName(owner, T("Modello per"));
        form.Children.Add(Section(T("Nome")));
        form.Children.Add(name);
        form.Children.Add(Section(T("Oggetto (facoltativo: va nei messaggi senza oggetto)")));
        form.Children.Add(subject);
        form.Children.Add(Labeled(T("Per"), owner));
        form.Children.Add(Section(T("Testo")));
        var editor = new RichEditor
        {
            MinHeight = 150, MaxHeight = 320, Padding = new Thickness(8), AcceptsTab = true, Language = Lingua.Xml, // la lingua di Missiva per il correttore: it-IT in italiano, come prima
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14,
            Document = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 14 },
        };
        SpellCheck.SetIsEnabled(editor, true);
        AutomationProperties.SetName(editor, T("Testo del modello"));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (glyph, tip, command) in new[]
                 {
                     ("\uE8DD", T("Grassetto (Ctrl+B)"), EditingCommands.ToggleBold), ("\uE8DB", T("Corsivo (Ctrl+I)"), EditingCommands.ToggleItalic),
                     ("\uE8DC", T("Sottolineato (Ctrl+U)"), EditingCommands.ToggleUnderline), ("\uE8FD", T("Elenco puntato"), EditingCommands.ToggleBullets),
                     ("\uE9D5", T("Elenco numerato"), EditingCommands.ToggleNumbering),
                 })
        {
            var tool = new Button
            {
                Content = new TextBlock { Text = glyph, FontFamily = Icons }, ToolTip = tip, Width = 34, Height = 32, Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            };
            AutomationProperties.SetName(tool, tip);
            tool.Click += (_, _) =>
            {
                command.Execute(null, editor);
                editor.Focus();
            };
            tools.Children.Add(tool);
        }
        form.Children.Add(tools);
        form.Children.Add(editor);
        p.Children.Add(form);

        bool loading = false;
        void Refill(MailTemplate? select)
        {
            loading = true;
            list.ItemsSource = null;
            list.ItemsSource = S.Templates.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            list.SelectedItem = select;
            loading = false;
            Load();
        }
        void Load()
        {
            loading = true;
            var t = list.SelectedItem as MailTemplate;
            form.IsEnabled = remove.IsEnabled = t != null;
            name.Text = t?.Name ?? "";
            subject.Text = t?.Subject ?? "";
            owner.SelectedIndex = Math.Max(0, owners.FindIndex(o => o.Id == t?.AccountId));
            editor.Rtf = t?.Rtf ?? "";
            loading = false;
        }
        // Come la firma: si salva mezzo secondo dopo l'ultima lettera, cambiando modello e chiudendo la finestra.
        var pending = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        MailTemplate? edited = null;
        void Flush()
        {
            pending.Stop();
            if (edited is not { } t) return;
            edited = null;
            t.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : T("Senza nome");
            t.Subject = subject.Text.Trim();
            t.AccountId = owners[Math.Max(0, owner.SelectedIndex)].Id;
            t.Rtf = editor.Rtf;
            Store.SaveSoon();
        }
        void Edited()
        {
            if (loading || list.SelectedItem is not MailTemplate t) return;
            edited = t;
            pending.Stop();
            pending.Start();
        }
        pending.Tick += (_, _) => Flush();
        Closed += (_, _) => Flush();
        name.TextChanged += (_, _) => Edited();
        subject.TextChanged += (_, _) => Edited();
        editor.TextChanged += (_, _) => Edited();
        owner.SelectionChanged += (_, _) => Edited();
        name.LostFocus += (_, _) =>
        {
            if (list.SelectedItem is not MailTemplate t) return;
            Flush();
            Refill(t); // il nome nuovo nell'elenco, al suo posto in ordine
        };
        list.SelectionChanged += (_, _) =>
        {
            if (loading) return;
            Flush();
            Load();
        };
        add.Click += (_, _) =>
        {
            Flush();
            var t = new MailTemplate { Name = T("Nuovo modello") };
            S.Templates.Add(t);
            Store.Save();
            Refill(t);
            name.Focus();
            name.SelectAll();
        };
        remove.Click += (_, _) =>
        {
            if (list.SelectedItem is not MailTemplate t) return;
            if (Dialog.Ask(this, T("Eliminare il modello \"{0}\"?", t.Name), T("Il modello esce da Missiva. I messaggi scritti con lui restano."), T("Elimina")) != Choice.Primary) return;
            edited = null;
            pending.Stop();
            S.Templates.Remove(t);
            Store.Save();
            Refill(null);
        };
        Refill(S.Templates.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault());
        return p;
    }

    // ---------------------------------------------------------------- Account

    StackPanel AccountsPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Account di posta")));
        p.Children.Add(Question(T("Gli account collegati a Missiva. Gestisci cambia il nome mostrato, la password e i server (o rifà l'accesso); rimuovendo un account, la posta resta sul server.")));
        foreach (var account in S.Accounts.ToList())
        {
            var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0) };
            card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            // L'indirizzo intero sopra, i pulsanti sotto: accanto all'indirizzo lo tagliavano ("mario.rossi@outlool").
            var row = new StackPanel();
            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var remove = new Button { Content = T("Rimuovi"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(remove, T("Rimuovi {0}", account.Email));
            remove.Click += async (_, _) =>
            {
                await removeAccount(account);
                ShowPage();
            };

            // Come Impostazioni, Account nel nuovo Outlook e Impostazioni server in Thunderbird. Una casella condivisa non ha
            // accesso né nome suoi: li ha l'account di lavoro che la apre.
            if (account.SharedOf == null)
            {
                var manage = new Button { Content = T("Gestisci"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                AutomationProperties.SetName(manage, T("Gestisci {0}", account.Email));
                manage.Click += async (_, _) =>
                {
                    await manageAccount(account, this, false);
                    ShowPage();
                };
                actions.Children.Add(manage);
            }
            if (account.Kind == AccountKind.Microsoft && account.SharedOf == null && autoReplies != null)
            {
                // Come Impostazioni, Account, Risposte automatiche nel nuovo Outlook.
                var replies = new Button { Content = T("Risposte automatiche"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                AutomationProperties.SetName(replies, T("Risposte automatiche di {0}", account.Email));
                replies.Click += async (_, _) => await autoReplies(account, this);
                actions.Children.Add(replies);
            }
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = account.Email, FontWeight = FontWeights.SemiBold });
            if (account.DisplayName.Length > 0 && account.DisplayName != account.Email)
            {
                var name = new TextBlock { Text = account.DisplayName, TextWrapping = TextWrapping.Wrap };
                name.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
                info.Children.Add(name);
            }
            var kind = new TextBlock { Text = AccountTypes.Describe(account), TextWrapping = TextWrapping.Wrap };
            kind.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            info.Children.Add(kind);
            row.Children.Add(info);
            actions.Children.Add(remove);
            row.Children.Add(actions);
            card.Child = row;
            p.Children.Add(card);
        }
        var add = new Button { Content = T("Aggiungi account"), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        add.SetResourceReference(StyleProperty, "AccentButtonStyle");
        add.Click += (_, _) =>
        {
            addAccount();
            ShowPage();
        };
        p.Children.Add(add);

        if (!string.IsNullOrEmpty(S.MicrosoftClientId))
        {
            p.Children.Add(Section(T("App registrata su Microsoft Entra")));
            p.Children.Add(Question(T("ID applicazione (client) degli account personali: {0}. Gli account di lavoro o della scuola usano l'app scritta per ognuno, o quella di Missiva.",
                                      S.MicrosoftClientId)));
        }
        if (!string.IsNullOrEmpty(S.GoogleClientId))
        {
            p.Children.Add(Section(T("App registrata su Google Cloud")));
            p.Children.Add(Question(T("ID client: {0}", S.GoogleClientId)));
        }
        return p;
    }

    // ---------------------------------------------------------------- Account, Firme

    /// <summary>Di chi è una firma: un account (il suo Id) o un suo altro indirizzo ("account|identità"), con l'indirizzo da mostrare.</summary>
    public sealed record SignatureOwner(string Key, string Email);

    /// <summary>Una firma nella casella delle firme, con il suo nome (per UI Automation anche).</summary>
    sealed record SignatureChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// Come Impostazioni, Account, Firme nel nuovo Outlook: per ogni indirizzo più firme con un nome (la principale e altre,
    /// come una breve per le risposte), scritte con lo stesso editor dei messaggi, e quale va nei nuovi messaggi e quale
    /// nelle risposte e negli inoltri. Nella scrittura il pulsante Firma le propone tutte. Si salva mentre scrivi.
    /// </summary>
    StackPanel SignaturesPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Firme")));
        if (S.Accounts.Count == 0)
        {
            p.Children.Add(Question(T("Aggiungi un account per scrivere la sua firma.")));
            return p;
        }
        // Gli account e i loro altri indirizzi (Gestisci account): un altro indirizzo senza firma sua usa quella dell'account.
        var owners = S.Accounts.SelectMany(a => a.Identities.Select(i => new SignatureOwner(a.Id + "|" + i.Id, T("{0} (altro indirizzo di {1})", i.Email, a.Email)))
                                                  .Prepend(new SignatureOwner(a.Id, a.Email))).ToList();
        var accountBox = new ComboBox { ItemsSource = owners, DisplayMemberPath = nameof(SignatureOwner.Email), SelectedIndex = 0 };
        AutomationProperties.SetName(accountBox, T("Firma per"));
        var itemStyle = new Style(typeof(ComboBoxItem), (Style)FindResource(typeof(ComboBoxItem)));
        itemStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new System.Windows.Data.Binding(nameof(SignatureOwner.Email))));
        accountBox.ItemContainerStyle = itemStyle; // per UI Automation l'indirizzo, non "Missiva.MailAccount"
        p.Children.Add(Labeled(T("Firma per"), accountBox));

        // Le firme dell'indirizzo: la principale e le altre, con Nuova firma, il nome ed Elimina.
        var signatureBox = new ComboBox();
        AutomationProperties.SetName(signatureBox, T("Firma da modificare"));
        p.Children.Add(Labeled(T("Firma"), signatureBox));
        var manage = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        var add = new Button { Content = T("Nuova firma"), Margin = new Thickness(0, 0, 8, 4) };
        AutomationProperties.SetName(add, T("Nuova firma"));
        var remove = new Button { Content = T("Elimina la firma"), Margin = new Thickness(0, 0, 8, 4) };
        AutomationProperties.SetName(remove, T("Elimina la firma"));
        var nameBox = new TextBox { Width = 200, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(nameBox, T("Nome della firma"));
        manage.Children.Add(add);
        manage.Children.Add(new TextBlock { Text = T("Nome"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 4) });
        manage.Children.Add(nameBox);
        manage.Children.Add(remove);
        p.Children.Add(manage);

        p.Children.Add(Section(T("Modifica la firma")));
        var editor = new RichEditor
        {
            MinHeight = 150, MaxHeight = 320, Padding = new Thickness(8), AcceptsTab = true, Language = Lingua.Xml, // la lingua di Missiva per il correttore: it-IT in italiano, come prima
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14,
            Document = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 14 },
        };
        SpellCheck.SetIsEnabled(editor, true);
        AutomationProperties.SetName(editor, T("Firma"));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (glyph, tip, command) in new[]
                 {
                     ("", T("Grassetto (Ctrl+B)"), EditingCommands.ToggleBold), ("", T("Corsivo (Ctrl+I)"), EditingCommands.ToggleItalic),
                     ("", T("Sottolineato (Ctrl+U)"), EditingCommands.ToggleUnderline), ("", T("Allinea a sinistra"), EditingCommands.AlignLeft),
                     ("", T("Al centro"), EditingCommands.AlignCenter),
                 })
        {
            var tool = new Button
            {
                Content = new TextBlock { Text = glyph, FontFamily = Icons }, ToolTip = tip, Width = 34, Height = 32, Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            };
            AutomationProperties.SetName(tool, tip);
            tool.Click += (_, _) =>
            {
                command.Execute(null, editor);
                editor.Focus();
            };
            tools.Children.Add(tool);
        }
        p.Children.Add(tools);
        p.Children.Add(editor);

        p.Children.Add(Section(T("Firme predefinite")));
        p.Children.Add(Question(T("Missiva mette la firma da sola, dopo due righe vuote; nelle risposte sta sopra il messaggio citato. Nella scrittura il pulsante Firma la cambia.")));
        var newBox = new ComboBox();
        var replyBox = new ComboBox();
        AutomationProperties.SetName(newBox, T("Firma dei nuovi messaggi"));
        AutomationProperties.SetName(replyBox, T("Firma delle risposte e degli inoltri"));
        p.Children.Add(Labeled(T("Nei nuovi messaggi"), newBox));
        p.Children.Add(Labeled(T("Nelle risposte e negli inoltri"), replyBox));

        bool loading = false;
        const string None = "-";
        Signature Current(SignatureOwner a) => S.Signatures.TryGetValue(a.Key, out var s) ? s : S.Signatures[a.Key] = new Signature();
        string EditedId() => (signatureBox.SelectedItem as SignatureChoice)?.Id ?? "";
        void FillChoices(Signature s, string select)
        {
            var all = s.All().Select(x => new SignatureChoice(x.Id, x.Id.Length == 0 ? T("Firma principale") : x.Name.Length > 0 ? x.Name : T("Senza nome"))).ToList();
            signatureBox.ItemsSource = all;
            signatureBox.SelectedItem = all.FirstOrDefault(c => c.Id == select) ?? all[0];
            var defaults = all.Prepend(new SignatureChoice(None, T("Nessuna firma"))).ToList();
            newBox.ItemsSource = defaults;
            replyBox.ItemsSource = defaults;
            newBox.SelectedItem = defaults.FirstOrDefault(c => c.Id == (s.NewMessages ? s.NewId : None)) ?? defaults[1];
            replyBox.SelectedItem = defaults.FirstOrDefault(c => c.Id == (s.Replies ? s.ReplyId : None)) ?? defaults[1];
        }
        void Load(string select = "")
        {
            if (accountBox.SelectedItem is not SignatureOwner a) return;
            loading = true;
            var s = S.Signatures.GetValueOrDefault(a.Key) ?? new Signature();
            FillChoices(s, select);
            ShowSignature(s);
            loading = false;
        }
        void ShowSignature(Signature s)
        {
            var id = EditedId();
            var named = s.More.FirstOrDefault(m => m.Id == id);
            editor.Rtf = named?.Rtf ?? s.Rtf;
            nameBox.Text = named?.Name ?? T("Firma principale");
            nameBox.IsEnabled = remove.IsEnabled = named != null;
        }
        void Save(SignatureOwner a, string id)
        {
            var s = Current(a);
            if (id.Length == 0) s.Rtf = editor.Rtf;
            else if (s.More.FirstOrDefault(m => m.Id == id) is { } named) named.Rtf = editor.Rtf;
            Store.SaveSoon(); // la firma non cambia nulla nella finestra principale: basta salvare
        }
        // Scrivendo la firma si salva mezzo secondo dopo l'ultima lettera, non a ogni lettera: Store.Save scrive tutte le
        // impostazioni. Le modifiche in sospeso si salvano anche cambiando account, firma o chiudendo la finestra.
        var pending = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        (SignatureOwner Owner, string Id)? edited = null;
        void Flush()
        {
            pending.Stop();
            if (edited is not { } e) return;
            edited = null;
            Save(e.Owner, e.Id);
        }
        pending.Tick += (_, _) => Flush();
        Closed += (_, _) => Flush();
        accountBox.SelectionChanged += (_, _) =>
        {
            Flush(); // l'editor ha ancora la firma dell'account di prima
            Load();
        };
        signatureBox.SelectionChanged += (_, _) =>
        {
            if (loading || accountBox.SelectedItem is not SignatureOwner a) return;
            Flush();
            loading = true;
            ShowSignature(Current(a));
            loading = false;
        };
        editor.TextChanged += (_, _) =>
        {
            if (loading || accountBox.SelectedItem is not SignatureOwner a) return;
            edited = (a, EditedId());
            pending.Stop();
            pending.Start();
        };
        add.Click += (_, _) =>
        {
            if (accountBox.SelectedItem is not SignatureOwner a) return;
            Flush();
            var s = Current(a);
            var named = new NamedSignature { Name = s.More.Count == 0 ? T("Breve") : T("Firma {0}", s.More.Count + 2) };
            s.More.Add(named);
            Store.Save();
            Load(named.Id);
            nameBox.Focus();
            nameBox.SelectAll();
        };
        nameBox.LostFocus += (_, _) =>
        {
            if (loading || accountBox.SelectedItem is not SignatureOwner a || Current(a).More.FirstOrDefault(m => m.Id == EditedId()) is not { } named) return;
            var name = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : T("Senza nome");
            if (name == named.Name) return;
            Flush();
            named.Name = name;
            Store.Save();
            Load(named.Id);
        };
        remove.Click += (_, _) =>
        {
            if (accountBox.SelectedItem is not SignatureOwner a || Current(a).More.FirstOrDefault(m => m.Id == EditedId()) is not { } named) return;
            if (Dialog.Ask(this, T("Eliminare la firma \"{0}\"?", named.Name), T("La firma esce da Missiva; i messaggi già scritti con lei restano come sono."), T("Elimina")) != Choice.Primary) return;
            edited = null;
            pending.Stop();
            var s = Current(a);
            s.More.Remove(named);
            if (s.NewId == named.Id) s.NewId = "";
            if (s.ReplyId == named.Id) s.ReplyId = "";
            Store.Save();
            Load();
        };
        foreach (var (box, reply) in new[] { (newBox, false), (replyBox, true) })
            box.SelectionChanged += (_, _) =>
            {
                if (loading || accountBox.SelectedItem is not SignatureOwner a || box.SelectedItem is not SignatureChoice choice) return;
                var s = Current(a);
                if (reply) (s.Replies, s.ReplyId) = choice.Id == None ? (false, s.ReplyId) : (true, choice.Id);
                else (s.NewMessages, s.NewId) = choice.Id == None ? (false, s.NewId) : (true, choice.Id);
                Store.Save();
            };
        Load();
        return p;
    }
}
