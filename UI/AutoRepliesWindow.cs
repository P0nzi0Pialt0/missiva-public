using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Risposte automatiche, come Impostazioni, Account, Risposte automatiche nel nuovo Outlook: spente, attive, o attive in un
/// periodo (dal giorno e ora al giorno e ora); il messaggio per chi è nell'organizzazione e quello per gli esterni (a
/// nessuno, solo ai contatti, a tutti). Per gli account personali un messaggio solo, come Outlook.com, con "solo ai
/// contatti". Si legge all'apertura; senza il consenso alle impostazioni della casella la finestra lo spiega e offre il
/// pulsante che apre la finestra di Microsoft, solo al clic (nella prova solo il registro). I messaggi si scrivono con
/// l'editor di Missiva e partono in HTML (FlowToHtml); le immagini no, perché Outlook non le tiene.
/// </summary>
public sealed class AutoRepliesWindow : Window
{
    readonly IMailProvider provider;
    readonly bool personal;
    readonly RadioButton off = new() { Content = T("Spente"), GroupName = "stato" };
    readonly RadioButton on = new() { Content = T("Attive"), GroupName = "stato", Margin = new Thickness(0, 6, 0, 0) };
    readonly RadioButton scheduled = new() { Content = T("Attive solo in questo periodo"), GroupName = "stato", Margin = new Thickness(0, 6, 0, 0) };
    readonly DatePicker startDate = new() { Width = 150 }, endDate = new() { Width = 150 };
    readonly ComboBox startTime = Times(), endTime = Times();
    readonly RichEditor inside = new() { Height = 120 }, outside = new() { Height = 120 };
    readonly CheckBox external = new() { Margin = new Thickness(0, 16, 0, 0) };
    readonly ComboBox audience = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel period = new() { Margin = new Thickness(28, 8, 0, 0) };
    readonly StackPanel externalPanel = new();
    readonly StackPanel form = new() { Visibility = Visibility.Collapsed };
    readonly StackPanel consent = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock error = Dialog.ErrorText(14);
    readonly TextBlock status = Secondary(T("Caricamento…"));
    readonly Button save = new() { Content = T("Salva"), MinWidth = 100, IsDefault = true, IsEnabled = false };

    /// <summary>Le risposte lette dal server all'apertura (null senza consenso), per la barra sotto l'account.</summary>
    public AutoReplies? Found { get; private set; }
    /// <summary>Quelle salvate con Salva.</summary>
    public AutoReplies? Saved { get; private set; }
    /// <summary>Il consenso è arrivato in questa finestra: si possono rileggere anche le categorie di Outlook.</summary>
    public bool ConsentGiven { get; private set; }

    public AutoRepliesWindow(IMailProvider provider)
    {
        this.provider = provider;
        personal = provider.Account.Tenant == null;
        Title = T("Risposte automatiche");
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 24) };
        panel.Children.Add(new TextBlock { Text = T("Risposte automatiche"), FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(Secondary(provider.Account.Email));
        status.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(status);
        consent.Margin = new Thickness(0, 14, 0, 0);
        BuildConsent();
        panel.Children.Add(consent);
        BuildForm();
        panel.Children.Add(form);
        panel.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.Click += async (_, _) => await SaveAsync();
        var cancel = new Button { Content = T("Annulla"), MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += async (_, _) => await LoadAsync();
#if DEBUG
        DevTools.HideForPhotos(this); // le foto (--foto, anche in inglese): fuori dallo schermo, senza il fuoco
#endif
    }

#if DEBUG
    /// <summary>
    /// Prove (--risposte-finte): le risposte da mostrare al posto della lettura, che nella prova senza consenso non c'è.
    /// Salva e Disattiva passano comunque dal provider: nella prova solo nel registro.
    /// </summary>
    internal static AutoReplies? TestSample;

    internal static AutoReplies Sample() => new()
    {
        State = AutoReplyState.Scheduled,
        Start = DateTime.Today.AddHours(8),
        End = DateTime.Today.AddDays(3).AddHours(18),
        InternalHtml = "<p>Buongiorno,</p><p>sono fuori sede fino a <b>giovedì</b> e leggo la posta solo la sera.</p><p>Per le urgenze: 06 1234 5678.</p>",
        ExternalHtml = "<p>Grazie del messaggio: rispondo al rientro.</p>",
        External = AutoReplyAudience.Contacts,
    };
#endif

    // ---------------------------------------------------------------- pezzi

    static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    static TextBlock Label(string text, double top = 14) => new() { Text = text, Margin = new Thickness(0, top, 0, 4) };

    /// <summary>Le ore ogni mezz'ora; si può anche scrivere un'ora diversa (9:15).</summary>
    internal static ComboBox Times()
    {
        var box = new ComboBox { Width = 90, IsEditable = true, Margin = new Thickness(8, 0, 0, 0) };
        for (int m = 0; m < 24 * 60; m += 30) box.Items.Add($"{m / 60}:{m % 60:00}");
        return box;
    }

    internal static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParseExact(text.Trim().Replace('.', ':'), new[] { @"h\:mm", @"hh\:mm", "%h", "hh" }, CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1)
            ? t : null;

    void BuildConsent()
    {
        consent.Children.Add(Secondary(T("Per leggere e cambiare le risposte automatiche Missiva deve poter accedere alle impostazioni della casella (il permesso MailboxSettings, che l'accesso di base non chiede). Si chiede una volta sola: vale anche per le categorie di Outlook con i loro colori.")));
        var allow = new Button { Content = T("Consenti l'accesso alle impostazioni della casella"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        allow.SetResourceReference(StyleProperty, "AccentButtonStyle");
        AutomationProperties.SetName(allow, T("Consenti l'accesso alle impostazioni della casella"));
        allow.Click += async (_, _) => await AllowAsync();
        consent.Children.Add(allow);
    }

    void BuildForm()
    {
        form.Children.Add(Label(T("Stato"), 16));
        form.Children.Add(off);
        form.Children.Add(on);
        form.Children.Add(scheduled);
        var from = new StackPanel { Orientation = Orientation.Horizontal };
        from.Children.Add(new TextBlock { Text = T("Dal"), Width = Lingua.English ? 44 : 34, VerticalAlignment = VerticalAlignment.Center });
        from.Children.Add(startDate);
        from.Children.Add(startTime);
        var to = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        to.Children.Add(new TextBlock { Text = T("Al"), Width = Lingua.English ? 44 : 34, VerticalAlignment = VerticalAlignment.Center });
        to.Children.Add(endDate);
        to.Children.Add(endTime);
        AutomationProperties.SetName(startDate, T("Data di inizio"));
        AutomationProperties.SetName(startTime, T("Ora di inizio"));
        AutomationProperties.SetName(endDate, T("Data di fine"));
        AutomationProperties.SetName(endTime, T("Ora di fine"));
        period.Children.Add(from);
        period.Children.Add(to);
        form.Children.Add(period);
        foreach (var radio in new[] { off, on, scheduled })
        {
            radio.Checked += (_, _) => UpdateEnabled();
            AutomationProperties.SetName(radio, radio.Content.ToString());
        }

        form.Children.Add(Label(personal ? T("Messaggio") : T("Messaggio per chi è nella tua organizzazione"), 18));
        AutomationProperties.SetName(inside, personal ? T("Messaggio") : T("Messaggio per l'organizzazione"));
        form.Children.Add(Framed(inside));
        if (personal)
        {
            external.Content = T("Rispondi solo ai miei contatti");
            AutomationProperties.SetName(external, T("Rispondi solo ai miei contatti"));
            form.Children.Add(external);
        }
        else
        {
            external.Content = T("Rispondi anche a chi è fuori dalla tua organizzazione");
            AutomationProperties.SetName(external, T("Rispondi anche a chi è fuori dalla tua organizzazione"));
            external.Checked += (_, _) => UpdateEnabled();
            external.Unchecked += (_, _) => UpdateEnabled();
            form.Children.Add(external);
            audience.Items.Add(new ComboBoxItem { Content = T("Solo ai miei contatti") });
            audience.Items.Add(new ComboBoxItem { Content = T("A tutti") });
            AutomationProperties.SetName(audience, T("A chi rispondere fuori dall'organizzazione"));
            externalPanel.Margin = new Thickness(28, 0, 0, 0);
            externalPanel.Children.Add(audience);
            externalPanel.Children.Add(Label(T("Messaggio per chi è fuori dalla tua organizzazione"), 10));
            AutomationProperties.SetName(outside, T("Messaggio per gli esterni"));
            externalPanel.Children.Add(Framed(outside));
            form.Children.Add(externalPanel);
        }
    }

    static Border Framed(RichEditor editor)
    {
        // Il carattere con cui il messaggio parte (FlowToHtml: Segoe UI 11 punti), non quello di base dei documenti WPF.
        editor.Document.FontFamily = new FontFamily("Segoe UI");
        editor.Document.FontSize = 11 * 96 / 72.0;
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Child = editor };
        border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        return border;
    }

    void UpdateEnabled()
    {
        period.IsEnabled = scheduled.IsChecked == true;
        if (!personal) externalPanel.IsEnabled = external.IsChecked == true;
    }

    void ShowError(string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
    }

    // ---------------------------------------------------------------- lettura e consenso

    async Task LoadAsync()
    {
        error.Visibility = Visibility.Collapsed;
        status.Text = T("Caricamento…");
        status.Visibility = Visibility.Visible;
        try
        {
#if DEBUG
            Found = TestSample ?? await provider.GetAutoRepliesAsync();
#else
            Found = await provider.GetAutoRepliesAsync();
#endif
        }
        catch (Exception ex)
        {
            Log.Error("risposte automatiche", ex);
            status.Visibility = Visibility.Collapsed;
            ShowError(T("Le risposte automatiche non si leggono: {0}", ex.Message));
            return;
        }
        status.Visibility = Visibility.Collapsed;
        if (Found is not { } replies)
        {
            consent.Visibility = Visibility.Visible;
            form.Visibility = Visibility.Collapsed;
            save.IsEnabled = false;
            return;
        }
        consent.Visibility = Visibility.Collapsed;
        form.Visibility = Visibility.Visible;
        save.IsEnabled = true;
        Fill(replies);
    }

    /// <summary>La finestra di Microsoft per il consenso, solo da questo pulsante; nella prova nessuna finestra.</summary>
    async Task AllowAsync()
    {
        error.Visibility = Visibility.Collapsed;
        if (MainWindow.Testing)
        {
            await provider.AllowMailboxSettingsAsync(); // TestingProvider: solo "prova: chiederei il consenso ..." nel registro
            status.Text = T("Nella versione di prova la finestra di Microsoft non si apre.");
            status.Visibility = Visibility.Visible;
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
            Log.Error("consenso alle impostazioni della casella", ex);
            ReadingView.ShowConsentError(this, T("Accesso alle impostazioni non consentito"), ex, provider.Account, T("leggere e cambiare le risposte automatiche"));
        }
        finally
        {
            consent.IsEnabled = true;
        }
    }

    void Fill(AutoReplies replies)
    {
        (replies.State switch { AutoReplyState.On => on, AutoReplyState.Scheduled => scheduled, _ => off }).IsChecked = true;
        // Il periodo di prima se è ancora da venire, altrimenti da adesso (l'ora dopo) a domani alla stessa ora.
        var now = DateTime.Now;
        var start = replies.Start;
        var end = replies.End;
        if (start == DateTime.MinValue || end <= now)
        {
            start = now.Date.AddHours(now.Hour + 1);
            end = start.AddDays(1);
        }
        startDate.SelectedDate = start.Date;
        startTime.Text = start.ToString("H:mm", CultureInfo.InvariantCulture);
        endDate.SelectedDate = end.Date;
        endTime.Text = end.ToString("H:mm", CultureInfo.InvariantCulture);
        SetHtml(inside, replies.InternalHtml);
        if (personal) external.IsChecked = replies.External == AutoReplyAudience.Contacts;
        else
        {
            external.IsChecked = replies.External != AutoReplyAudience.None;
            audience.SelectedIndex = replies.External == AutoReplyAudience.Contacts ? 0 : 1;
            SetHtml(outside, replies.ExternalHtml);
        }
        // Un pubblico esterno che Missiva non conosce: detto, e lasciato com'è finché non se ne sceglie un altro (AutoReplies.UnknownAudience).
        if (replies.UnknownAudience is { Length: > 0 } unknown)
            ShowError(T("Per chi scrive da fuori Outlook ha un'impostazione che Missiva non conosce (\"{0}\"): qui la vedi come \"tutti\", ma Salva la lascia com'è finché non ne scegli un'altra.", unknown));
        UpdateEnabled();
    }

    /// <summary>L'HTML delle risposte (il proprio, scritto qui o sul sito di Outlook) nell'editor: paragrafi e stili semplici.</summary>
    static void SetHtml(RichEditor editor, string html)
    {
        editor.Document.Blocks.Clear();
        editor.Document.Blocks.AddRange(HtmlToFlow.Blocks(html));
    }

    /// <summary>Il testo dell'editor in HTML; vuoto se non c'è niente scritto.</summary>
    static string Html(RichEditor editor) => editor.PlainText.Trim().Length == 0 ? "" : FlowToHtml.Convert(editor.Document);

    /// <summary>Immagini nel messaggio: non si possono mandare (FlowToHtml le scriverebbe come cid: senza la parte).</summary>
    static bool HasImages(RichEditor editor) => EditorImages.All(editor.Document).Any();

    // ---------------------------------------------------------------- salvataggio

    async Task SaveAsync()
    {
        error.Visibility = Visibility.Collapsed;
        var state = on.IsChecked == true ? AutoReplyState.On : scheduled.IsChecked == true ? AutoReplyState.Scheduled : AutoReplyState.Off;
        DateTime start = default, end = default;
        if (state == AutoReplyState.Scheduled)
        {
            if (startDate.SelectedDate is not { } d1 || ParseTime(startTime.Text) is not { } t1 || endDate.SelectedDate is not { } d2 || ParseTime(endTime.Text) is not { } t2)
            {
                ShowError(T("Scegli il giorno e l'ora di inizio e di fine (per esempio 9:00)."));
                return;
            }
            start = d1.Date + t1;
            end = d2.Date + t2;
            if (end <= start)
            {
                ShowError(T("La fine deve venire dopo l'inizio."));
                return;
            }
            if (end <= DateTime.Now)
            {
                ShowError(T("La fine è già passata: scegli un periodo che finisce più avanti."));
                return;
            }
        }
        var internalHtml = Html(inside);
        var externalHtml = personal ? internalHtml : Html(outside);
        if (HasImages(inside) || !personal && HasImages(outside))
        {
            ShowError(T("Le risposte automatiche non possono contenere immagini: Outlook non le tiene. Toglile dal messaggio."));
            return;
        }
        var reach = personal
            ? external.IsChecked == true ? AutoReplyAudience.Contacts : AutoReplyAudience.All
            : external.IsChecked != true ? AutoReplyAudience.None : audience.SelectedIndex == 0 ? AutoReplyAudience.Contacts : AutoReplyAudience.All;
        if (state != AutoReplyState.Off && internalHtml.Length == 0)
        {
            ShowError(T("Scrivi il messaggio da mandare in risposta."));
            inside.Focus();
            return;
        }
        if (state != AutoReplyState.Off && reach != AutoReplyAudience.None && externalHtml.Length == 0)
        {
            ShowError(T("Scrivi il messaggio per chi è fuori dalla tua organizzazione, o togli la risposta agli esterni."));
            outside.Focus();
            return;
        }
        var replies = (Found ?? new AutoReplies()) with
        {
            State = state,
            Start = state == AutoReplyState.Scheduled ? start : Found?.Start ?? default,
            End = state == AutoReplyState.Scheduled ? end : Found?.End ?? default,
            InternalHtml = internalHtml,
            ExternalHtml = externalHtml,
            External = reach,
        };
        form.IsEnabled = save.IsEnabled = false;
        try
        {
            await provider.SetAutoRepliesAsync(replies);
            Saved = replies;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Error("risposte automatiche non salvate", ex);
            ShowError(T("Le risposte automatiche non sono state salvate: {0}", ex.Message));
            form.IsEnabled = save.IsEnabled = true;
        }
    }
}
