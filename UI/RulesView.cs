using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>Quello che le regole di un account chiedono alla finestra principale: il provider, le cartelle, e l'elenco da rileggere dopo "Esegui ora".</summary>
public sealed record RuleHost(IMailProvider Provider, List<MailFolder> Folders, Action Refresh);

/// <summary>
/// Impostazioni, Posta, Regole, come nel nuovo Outlook: per l'account scelto l'elenco delle regole nell'ordine in cui si
/// applicano, ognuna accesa o spenta, con Modifica, Elimina, Su, Giù ed "Esegui ora" su una cartella; Nuova regola. Per gli
/// account Microsoft sono le regole di Outlook (le stesse di Outlook sul web: senza il consenso alle impostazioni della
/// casella nessuna richiesta e il pulsante che lo chiede, solo al clic); per Gmail e IMAP le regole di Missiva. Una regola
/// che inoltra la posta fuori dall'organizzazione è in rosso, con Spegni.
/// </summary>
public sealed class RulesView : StackPanel
{
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    readonly Func<MailAccount, RuleHost?> hostOf;
    readonly ComboBox accountBox = new() { Width = 260, DisplayMemberPath = nameof(MailAccount.Email) };
    readonly TextBlock info = Secondary("");
    readonly TextBlock status = Secondary("");
    readonly TextBlock result = Secondary("");
    readonly StackPanel consent = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    readonly StackPanel list = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly Button add = new() { Content = "Nuova regola", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    RuleHost? host;
    List<MailRule>? rules;
    int version;

    public RulesView(Func<MailAccount, RuleHost?> hostOf)
    {
        this.hostOf = hostOf;
        var accounts = Store.Settings.Accounts.ToList();
        accountBox.ItemsSource = accounts;
        AutomationProperties.SetName(accountBox, "Regole di");
        var itemStyle = new Style(typeof(ComboBoxItem), (Style)Application.Current.FindResource(typeof(ComboBoxItem)));
        itemStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new System.Windows.Data.Binding(nameof(MailAccount.Email))));
        accountBox.ItemContainerStyle = itemStyle;
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock { Text = "Regole di", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) });
        row.Children.Add(accountBox);
        Children.Add(row);
        info.Margin = new Thickness(0, 6, 0, 0);
        Children.Add(info);
        status.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(status);
        BuildConsent();
        Children.Add(consent);
        Children.Add(list);
        add.SetResourceReference(StyleProperty, "AccentButtonStyle");
        add.Click += async (_, _) => await EditAsync(null);
        Children.Add(add);
        result.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(result);
        accountBox.SelectionChanged += async (_, _) => await LoadAsync();
        if (accounts.Count == 0)
        {
            info.Text = "Aggiungi un account per creare le sue regole.";
            add.Visibility = Visibility.Collapsed;
            return;
        }
#if DEBUG
        accountBox.SelectedIndex = Math.Max(0, accounts.FindIndex(a => a.Email.Equals(StartAccount, StringComparison.OrdinalIgnoreCase)));
#else
        accountBox.SelectedIndex = 0;
#endif
    }

#if DEBUG
    /// <summary>
    /// Prove (--regole-finte): le regole di Outlook da mostrare al posto della lettura, che nella prova senza consenso non c'è;
    /// fra queste una che inoltra fuori. Spegni, Modifica ed Elimina passano comunque dal provider: nella prova solo nel registro.
    /// </summary>
    internal static List<MailRule>? TestSample;

    /// <summary>
    /// Quattro regole di Outlook come le dà Graph: fatture in una cartella, newsletter lette, un inoltro a un indirizzo esterno
    /// e una con una condizione che Missiva non sa controllare (oggetto o testo), senza Esegui ora.
    /// </summary>
    internal static List<MailRule> Sample(MailAccount account, List<MailFolder> folders)
    {
        var target = folders.FirstOrDefault(f => f.Kind == FolderKind.Archive) ?? folders.FirstOrDefault(f => f.Kind == FolderKind.Other) ?? folders.First();
        JsonNode Rule(string id, string name, int sequence, JsonObject conditions, JsonObject actions) =>
            new JsonObject { ["id"] = id, ["displayName"] = name, ["sequence"] = sequence, ["isEnabled"] = true, ["hasError"] = false, ["isReadOnly"] = false, ["conditions"] = conditions, ["actions"] = actions };
        return new[]
        {
            Rule("r1", "Fatture dei fornitori", 1, new JsonObject { ["subjectContains"] = new JsonArray("fattura", "avviso di pagamento"), ["hasAttachments"] = true },
                 new JsonObject { ["moveToFolder"] = target.Id, ["stopProcessingRules"] = true }),
            Rule("r2", "Newsletter", 2, new JsonObject { ["senderContains"] = new JsonArray("newsletter", "noreply") }, new JsonObject { ["markAsRead"] = true, ["assignCategories"] = new JsonArray("Da leggere") }),
            Rule("r3", "Copia di sicurezza", 3, new JsonObject(),
                 new JsonObject { ["forwardTo"] = new JsonArray(new JsonObject { ["emailAddress"] = new JsonObject { ["name"] = "Archivio", ["address"] = "archivio.posta.7731@gmail.com" } }) }),
            Rule("r4", "Offerte", 4, new JsonObject { ["bodyOrSubjectContains"] = new JsonArray("offerta") }, new JsonObject { ["moveToFolder"] = target.Id }),
        }.Select(r => Rules.FromGraph(r, account.Id)).ToList();
    }
#endif

#if DEBUG
    /// <summary>
    /// L'account da mostrare all'apertura (per le prove, --regole indirizzo). Lo assegnano solo gli argomenti di prova della
    /// Debug: fuori dalla Debug non c'è, e la pagina si apre sul primo account come prima.
    /// </summary>
    internal static string? StartAccount;
#endif

    static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    void BuildConsent()
    {
        consent.Children.Add(Secondary("Le regole di Outlook stanno nelle impostazioni della casella: per leggerle e cambiarle Missiva deve poterci accedere " +
                                       "(il permesso MailboxSettings, lo stesso delle risposte automatiche e delle categorie, che l'accesso di base non chiede)."));
        var allow = new Button { Content = "Consenti l'accesso alle impostazioni della casella", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        allow.SetResourceReference(StyleProperty, "AccentButtonStyle");
        AutomationProperties.SetName(allow, "Consenti l'accesso alle impostazioni della casella");
        allow.Click += async (_, _) => await AllowAsync();
        consent.Children.Add(allow);
    }

    MailAccount? Account => accountBox.SelectedItem as MailAccount;

    // ---------------------------------------------------------------- lettura

    async Task LoadAsync()
    {
        int mine = ++version;
        list.Children.Clear();
        result.Text = "";
        consent.Visibility = Visibility.Collapsed;
        rules = null;
        if (Account is not { } account) return;
        host = hostOf(account);
        add.Visibility = Visibility.Visible;
        add.IsEnabled = false;
        if (account.SharedOf != null)
        {
            info.Text = "Le regole di una casella condivisa le imposta l'amministratore di Microsoft 365 (o chi ha la casella), in Outlook sul web.";
            add.Visibility = Visibility.Collapsed;
            status.Text = "";
            return;
        }
        if (host == null)
        {
            info.Text = "L'account non è ancora pronto: riprova fra poco.";
            status.Text = "";
            return;
        }
        var provider = host.Provider;
        info.Text = provider.HasServerRules
            ? "Sono le regole di Outlook: le stesse di Outlook sul web e del telefono. Girano sul server, anche con Missiva chiusa, nell'ordine dell'elenco."
            : "Sono regole di Missiva: girano solo quando Missiva è aperta, anche nascosta nell'area di notifica, sui nuovi messaggi della Posta in arrivo, " +
              "nell'ordine dell'elenco. Restano su questo PC, cifrate." + (account.Kind == AccountKind.Gmail ? " I filtri fatti sul sito di Gmail continuano a valere." : "");
        if (!provider.HasServerRules)
        {
            status.Text = "";
            rules = Rules.For(account);
            Show();
            return;
        }
        status.Text = "Caricamento delle regole di Outlook…";
        List<MailRule>? found;
        try
        {
#if DEBUG
            found = TestSample != null && account.Kind == AccountKind.Microsoft ? TestSample : await provider.GetRulesAsync();
#else
            found = await provider.GetRulesAsync();
#endif
        }
        catch (Exception ex)
        {
            Log.Error("regole di Outlook", ex);
            if (mine == version) status.Text = "Le regole di Outlook non si leggono: " + ex.Message;
            return;
        }
        if (mine != version) return; // intanto si è scelto un altro account
        status.Text = "";
        if (found == null)
        {
            consent.Visibility = Visibility.Visible;
            return;
        }
        rules = found;
        Show();
    }

    string? FolderName(string id) => host?.Folders.FirstOrDefault(f => f.Id == id)?.Name;

    void Show()
    {
        list.Children.Clear();
        add.IsEnabled = true;
        if (rules == null || Account is not { } account) return;
        if (rules.Count == 0) list.Children.Add(Secondary("Nessuna regola."));
        for (int i = 0; i < rules.Count; i++) list.Children.Add(Card(rules[i], i, account));
    }

    Button Small(string glyph, string name, Func<Task> action, bool enabled = true)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = Icons }, ToolTip = name, Width = 34, Height = 30, Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0), IsEnabled = enabled, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(button, name);
        button.Click += async (_, _) => await action();
        return button;
    }

    /// <summary>Una regola nell'elenco: accesa o spenta, nome, che cosa fa, i pulsanti; in rosso se manda la posta fuori.</summary>
    Border Card(MailRule rule, int index, MailAccount account)
    {
        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 8, 10), Margin = new Thickness(0, 6, 0, 0) };
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var panel = new StackPanel();
        var top = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        bool editable = !rule.IsReadOnly;
        buttons.Children.Add(Small("", $"Modifica la regola {rule.Name}", () => EditAsync(rule), editable));
        buttons.Children.Add(Small("", $"Elimina la regola {rule.Name}", () => DeleteAsync(rule), editable));
        buttons.Children.Add(Small("", $"Sposta su la regola {rule.Name}", () => MoveAsync(index, -1), editable && index > 0));
        buttons.Children.Add(Small("", $"Sposta giù la regola {rule.Name}", () => MoveAsync(index, +1), editable && index < rules!.Count - 1));
        // Con condizioni o eccezioni che solo Outlook controlla, Esegui ora non c'è: Missiva le tratterebbe come assenti.
        bool runnable = rule.HiddenConditions.Count == 0;
        var run = new Button { Content = "Esegui ora", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), IsEnabled = runnable };
        AutomationProperties.SetName(run, $"Esegui ora la regola {rule.Name}");
        if (!runnable)
        {
            run.ToolTip = "La regola ha condizioni che solo Outlook controlla: la applica Outlook ai messaggi che arrivano.";
            ToolTipService.SetShowOnDisabled(run, true);
        }
        run.Click += (_, _) => ShowRunMenu(run, rule);
        buttons.Children.Add(run);
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        var enabled = new CheckBox { Content = rule.Name, IsChecked = rule.Enabled, FontWeight = FontWeights.SemiBold, IsEnabled = editable, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(enabled, $"Regola {rule.Name} accesa");
        // Checked e Unchecked, non Click: UI Automation (Toggle) e Assistente vocale non generano Click.
        async void Toggled(object sender, RoutedEventArgs e)
        {
            if (enabled.IsChecked == rule.Enabled) return;
            await SetEnabledAsync(rule, enabled.IsChecked == true);
        }
        enabled.Checked += Toggled;
        enabled.Unchecked += Toggled;
        top.Children.Add(enabled);
        panel.Children.Add(top);
        var description = Secondary(Rules.Describe(rule, FolderName));
        description.Margin = new Thickness(26, 2, 0, 0);
        panel.Children.Add(description);
        var outside = Rules.ExternalForwards(rule, account);
        if (outside.Count > 0)
        {
            var warning = new DockPanel { Margin = new Thickness(26, 6, 0, 0) };
            if (rule.Enabled)
            {
                var off = new Button { Content = "Spegni", Padding = new Thickness(10, 3, 10, 3), IsEnabled = editable };
                AutomationProperties.SetName(off, $"Spegni la regola {rule.Name}");
                off.Click += async (_, _) => await SetEnabledAsync(rule, false);
                DockPanel.SetDock(off, Dock.Right);
                warning.Children.Add(off);
            }
            var danger = new TextBlock
            {
                Text = rule.Enabled
                    ? $"Inoltra la tua posta a {string.Join(", ", outside)}, fuori dalla tua casella. Se non l'hai creata tu, spegnila e cambia la password."
                    : $"Inoltrerebbe la tua posta a {string.Join(", ", outside)}, fuori dalla tua casella (ora è spenta). Se non l'hai creata tu, eliminala e cambia la password.",
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            };
            Dialog.Critical(danger); // il rosso del tema (C-16): quello fisso di prima nello scuro si leggeva male
            warning.Children.Add(danger);
            panel.Children.Add(warning);
            Dialog.Critical(card);
        }
        if (rule.HasError)
        {
            var problem = new TextBlock { Text = "Outlook segnala un errore in questa regola (per esempio una cartella che non c'è più): aprila con Modifica e controllala.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 4, 0, 0) };
            Dialog.Critical(problem);
            panel.Children.Add(problem);
        }
        if (rule.IsReadOnly)
        {
            var locked = Secondary("Questa regola si cambia solo in Outlook classico.");
            locked.Margin = new Thickness(26, 4, 0, 0);
            panel.Children.Add(locked);
        }
        if (!runnable)
        {
            var only = Secondary("Esegui ora non c'è per questa regola: ha condizioni che solo Outlook sa controllare. Outlook la applica da sé ai messaggi che arrivano.");
            only.Margin = new Thickness(26, 4, 0, 0);
            panel.Children.Add(only);
        }
        card.Child = panel;
        return card;
    }

    // ---------------------------------------------------------------- azioni

    Window? Owner => Window.GetWindow(this);

    /// <summary>La finestra di Microsoft per il consenso, solo da questo pulsante; nella prova nessuna finestra.</summary>
    async Task AllowAsync()
    {
        if (host == null) return;
        if (MainWindow.Testing)
        {
            await host.Provider.AllowMailboxSettingsAsync(); // TestingProvider: solo "prova: chiederei il consenso ..." nel registro
            status.Text = "Nella versione di prova la finestra di Microsoft non si apre.";
            return;
        }
        consent.IsEnabled = false;
        try
        {
            if (Owner is { } owner) MicrosoftAuth.OpenOver(owner);
            await host.Provider.AllowMailboxSettingsAsync();
            await LoadAsync();
        }
        catch (Exception ex) when (MicrosoftAuth.Canceled(ex))
        {
            // annullato nella finestra di Microsoft
        }
        catch (Exception ex)
        {
            Log.Error("consenso alle impostazioni della casella (regole)", ex);
            ReadingView.ShowConsentError(Owner, "Accesso alle impostazioni non consentito", ex, host.Provider.Account, "leggere e cambiare le regole di Outlook");
        }
        finally
        {
            consent.IsEnabled = true;
        }
    }

    async Task EditAsync(MailRule? rule)
    {
        if (host == null) return;
        var window = new RuleWindow(host.Provider, host.Folders, rule, known: rules) { Owner = Owner };
        if (window.ShowDialog() != true) return;
        if (host.Provider.HasServerRules && MainWindow.Testing && window.Saved is { } saved)
        {
            // Nella prova la regola non arriva al server: l'elenco la mostra come se ci fosse.
            if (rule == null) rules?.Add(saved);
            else if (rules != null && rules.IndexOf(rule) is var at and >= 0) rules[at] = saved;
            Show();
            return;
        }
        await LoadAsync();
    }

    async Task DeleteAsync(MailRule rule)
    {
        if (host == null) return;
        if (Dialog.Ask(Owner, $"Eliminare la regola \"{rule.Name}\"?", host.Provider.HasServerRules
                ? "La regola esce da Outlook: non agirà più sui messaggi che arrivano, nemmeno su Outlook sul web e sul telefono."
                : "La regola esce da Missiva. I messaggi già spostati restano dove sono.", "Elimina") != Choice.Primary) return;
        try
        {
            if (host.Provider.HasServerRules) await host.Provider.DeleteRuleAsync(rule);
            else Rules.Remove(rule);
            rules?.Remove(rule);
            Show();
        }
        catch (Exception ex)
        {
            Log.Error("regola non eliminata", ex);
            Dialog.Show(Owner, "Regola non eliminata", ex.Message);
        }
    }

    async Task SetEnabledAsync(MailRule rule, bool on)
    {
        if (host == null || rules == null) return;
        var after = rule.Copy();
        after.Enabled = on;
        try
        {
            if (host.Provider.HasServerRules) await host.Provider.SaveRuleAsync(rule, after);
            else Rules.Put(after);
            rules[rules.IndexOf(rule)] = after;
        }
        catch (Exception ex)
        {
            Log.Error("regola non cambiata", ex);
            Dialog.Show(Owner, on ? "Regola non accesa" : "Regola non spenta", ex.Message);
        }
        Show();
    }

    /// <summary>
    /// Su e giù: per le regole di Missiva l'ordine delle impostazioni; per quelle di Outlook le regole si rinumerano da 1 e
    /// si cambia (PATCH di sequence) solo quella che cambia numero.
    /// </summary>
    async Task MoveAsync(int index, int delta)
    {
        if (host == null || rules == null) return;
        int to = index + delta;
        if (to < 0 || to >= rules.Count) return;
        try
        {
            if (!host.Provider.HasServerRules)
            {
                Rules.Move(rules[index], delta);
                (rules[index], rules[to]) = (rules[to], rules[index]);
                Show();
                return;
            }
            var (shown, problem) = await ReorderAsync(host.Provider, rules, index, to);
            rules = shown;
            if (problem != null) Dialog.Show(Owner, problem.Value.Heading, problem.Value.Body);
        }
        catch (Exception ex)
        {
            Log.Error("regole non riordinate", ex);
            Dialog.Show(Owner, "Ordine delle regole non cambiato", ex.Message);
        }
        Show();
    }

    /// <summary>
    /// Sposta una regola di Outlook: le regole si rinumerano da 1, con un PATCH di sequence per ogni regola che cambia numero.
    /// Se una richiesta non riesce dopo che altre sono riuscite, su Outlook l'ordine è già cambiato in parte: prima Missiva
    /// diceva "Ordine delle regole non cambiato" e mostrava l'ordine di prima (audit, P-02). Ora dopo un errore le regole si
    /// rileggono dal server, si mostra l'ordine vero e si dice che cosa è successo; se nemmeno la rilettura riesce, si mostra
    /// quello che risulta dalle richieste riuscite e lo si dice.
    /// </summary>
    internal static async Task<(List<MailRule> Rules, (string Heading, string Body)? Problem)> ReorderAsync(IMailProvider provider, List<MailRule> rules, int index, int to)
    {
        var ordered = rules.ToList();
        (ordered[index], ordered[to]) = (ordered[to], ordered[index]);
        int total = ordered.Where((r, i) => r.Sequence != i + 1).Count(), done = 0;
        try
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Sequence == i + 1) continue;
                var after = ordered[i].Copy();
                after.Sequence = i + 1;
                await provider.SaveRuleAsync(ordered[i], after);
                ordered[i] = after;
                done++;
            }
            return (ordered, null);
        }
        catch (Exception ex)
        {
            Log.Error($"regole non riordinate ({done} di {total} richieste riuscite)", ex);
            var partly = done > 0 ? $"L'ordine è cambiato solo in parte: {done} di {total} regole hanno preso il numero nuovo, poi Outlook ha risposto con un errore. " : "";
            try
            {
                if (await provider.GetRulesAsync() is { } fresh)
                    return (fresh, done > 0
                        ? ("Ordine delle regole cambiato solo in parte", partly + "Qui sotto c'è l'ordine che Outlook ha adesso: controllalo e sposta di nuovo la regola.\n\n" + ex.Message)
                        : ("Ordine delle regole non cambiato", "Outlook non ha cambiato nulla: l'ordine è quello di prima.\n\n" + ex.Message));
            }
            catch (Exception again)
            {
                Log.Error("regole non rilette dopo il riordino non riuscito", again);
            }
            // Senza rilettura: l'ordine che risulta dalle richieste riuscite (le altre regole hanno ancora il numero di prima).
            return (done > 0 ? ordered.OrderBy(r => r.Sequence).ToList() : rules,
                    (done > 0 ? "Ordine delle regole cambiato solo in parte" : "Ordine delle regole non cambiato",
                     partly + "Missiva non è riuscita a rileggere le regole da Outlook: l'ordine qui sotto può non essere quello vero. Chiudi e riapri la pagina Regole.\n\n" + ex.Message));
        }
    }

    /// <summary>Esegui ora: le cartelle dell'account, la Posta in arrivo per prima.</summary>
    void ShowRunMenu(Button anchor, MailRule rule)
    {
        if (host == null) return;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var folder in host.Folders.Where(f => f.Kind is not (FolderKind.Drafts or FolderKind.Sent)).OrderBy(f => f.Kind == FolderKind.Inbox ? 0 : 1))
        {
            var item = new MenuItem { Header = folder.Name };
            AutomationProperties.SetName(item, folder.Name);
            item.Click += async (_, _) => await RunNowAsync(rule, folder);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>
    /// Esegui ora, come nel nuovo Outlook: la regola sui messaggi già nella cartella (gli ultimi 500), con le stesse azioni
    /// di IMailProvider (nella prova solo nel registro), dopo una conferma con quanti messaggi cambiano. L'inoltro no: lo fa
    /// solo Exchange, sui messaggi che arrivano. Mai con condizioni che solo Outlook controlla (HiddenConditions).
    /// </summary>
    async Task RunNowAsync(MailRule rule, MailFolder folder)
    {
        if (host == null) return;
        var provider = host.Provider;
        if (rule.HiddenConditions.Count > 0)
        {
            result.Text = $"Esegui ora \"{rule.Name}\": la regola ha condizioni che solo Outlook sa controllare, quindi Missiva non la applica.";
            return;
        }
        result.Text = $"Esegui ora \"{rule.Name}\" su {folder.Name}…";
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var messages = new List<MessageSummary>();
            string? next = null;
            for (int page = 0; page < 10; page++)
            {
                var got = await provider.GetMessagesAsync(folder, null, next);
                messages.AddRange(got.Messages);
                if ((next = got.Next) == null) break;
            }
            var active = rule.Copy();
            active.Enabled = true;
            // Prima di agire la conferma, con quanti messaggi cambiano: una regola senza condizioni vale per tutti i 500.
            Task<bool> ConfirmAsync(int touched) => Dispatcher.InvokeAsync(() =>
            {
                var noConditions = !active.HasConditions ? " La regola non ha condizioni: vale per ogni messaggio." : "";
                return Dialog.Ask(Owner, $"Eseguire ora \"{rule.Name}\"?",
                    $"La regola cambia {touched} dei {messages.Count} messaggi di {folder.Name}.{noConditions}\n\n{Rules.Describe(active, FolderName)}", "Esegui") == Choice.Primary;
            }).Task;
            var outcome = await Rules.RunAsync(provider, messages, [active], () => Task.FromResult(host.Folders), confirm: ConfirmAsync);
            Log.Time($"regole: Esegui ora su {messages.Count} messaggi, {outcome.Matched} corrispondono, {outcome.Touched} da cambiare{(outcome.Canceled ? ", annullato" : "")}", clock);
            if (outcome.Canceled)
            {
                result.Text = $"Esegui ora \"{rule.Name}\" annullato: nessun messaggio cambiato.";
                return;
            }
            if (outcome.Errors.Count > 0) Log.Write($"regole: Esegui ora, non riuscito ({outcome.Errors.Count}): {Log.Private(string.Join("; ", outcome.Errors))}");
            result.Text = RunResult(rule.Name, folder.Name, messages.Count, outcome);
            if (outcome.Removed.Count + outcome.Read.Count + outcome.Flagged.Count > 0) host.Refresh();
        }
        catch (Exception ex)
        {
            Log.Error("Esegui ora", ex);
            result.Text = $"Esegui ora \"{rule.Name}\" non riuscito: {ex.Message}";
        }
    }

    /// <summary>
    /// L'esito di "Esegui ora" in parole: quanti messaggi corrispondono, che cosa è stato fatto e, se un'azione non è
    /// riuscita, quale e perché. Un'azione non riuscita non ferma più le altre (Rules.RunAsync): prima si vedeva solo il
    /// primo errore, senza sapere che cosa era già stato fatto.
    /// </summary>
    internal static string RunResult(string rule, string folder, int messages, Rules.Outcome outcome)
    {
        if (outcome.Matched == 0) return $"Esegui ora \"{rule}\" su {folder}: nessuno dei {messages} messaggi corrisponde.";
        return $"Esegui ora \"{rule}\" su {folder}: {outcome.Matched} dei {messages} messaggi corrispondono" + (outcome.Done.Count > 0 ? "; " + string.Join(", ", outcome.Done) : "") + "." +
               (outcome.Errors.Count > 0 ? " Non riuscito: " + string.Join("; ", outcome.Errors) + "." : "") +
               (outcome.SkippedForwards ? " L'inoltro lo fa solo Outlook, sui messaggi che arrivano." : "");
    }
}
