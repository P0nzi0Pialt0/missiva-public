using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Nuovo evento, come nel nuovo Outlook: titolo, calendario (fra quelli che si possono modificare), inizio e fine nell'ora di
/// Windows o giornata intera, ripetizione semplice (ogni giorno, ogni settimana nei giorni scelti, ogni mese), luogo,
/// promemoria, "mostra come", descrizione con l'editor di Missiva. Con dei partecipanti (suggeriti dalla rubrica) diventa
/// una riunione e il pulsante è Invia: gli inviti li manda Exchange. Riunione di Teams solo per gli account di lavoro o della
/// scuola. Salva scrive con IMailProvider.CreateEventAsync (Graph, con il consenso al calendario); nella prova solo il
/// registro (TestingProvider). La stessa finestra modifica un evento proprio (ForEdit, doppio clic o Modifica nei
/// dettagli): Salva, o "Invia aggiornamento" con partecipanti, manda solo i campi cambiati (UpdateEventAsync); la
/// ripetizione e la descrizione restano com'erano.
/// </summary>
public sealed class EventWindow : Window
{
    /// <summary>Un calendario in cui si può scrivere, con il suo account.</summary>
    public sealed record Choice(IMailProvider Provider, CalendarInfo Calendar)
    {
        public override string ToString() => $"{Calendar.Name} ({Provider.Account.Email})";
    }

    readonly TextBox subject = new() { FontSize = 16 };
    readonly ComboBox calendar = new() { Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
    readonly CheckBox allDay = new() { Content = T("Giornata intera"), Margin = new Thickness(0, 12, 0, 0) };
    readonly DatePicker startDate = new() { Width = 150 }, endDate = new() { Width = 150 };
    readonly ComboBox startTime = AutoRepliesWindow.Times(), endTime = AutoRepliesWindow.Times();
    readonly ComboBox repeat = new() { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    readonly WrapPanel repeatDays = new() { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly DatePicker repeatUntil = new() { Width = 150, Margin = new Thickness(8, 0, 0, 0) };
    readonly StackPanel repeatEnd = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBox location = new();
    readonly ComboBox reminder = new() { Width = 170 };
    readonly ComboBox showAs = new() { Width = 170, Margin = new Thickness(12, 0, 0, 0) };
    readonly TextBox required = new(), optional = new();
    readonly CheckBox teams = new() { Content = T("Riunione di Teams"), Margin = new Thickness(0, 10, 0, 0) };
    readonly RichEditor description = new() { Height = 110 };
    readonly TextBlock error = Dialog.ErrorText(12);
    readonly Button save = new() { Content = T("Salva"), MinWidth = 100, IsDefault = true };
    readonly TextBlock heading = new() { Text = T("Nuovo evento"), FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
    readonly TextBlock editNote = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -6, 0, 6), Visibility = Visibility.Collapsed };
    /// <summary>Le righe che la modifica non mostra: la ripetizione, la descrizione e Teams restano com'erano.</summary>
    readonly List<UIElement> newOnly = new();
    readonly StackPanel availability = new();
    readonly TextBlock availabilityNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>Da dove arriva la disponibilità: il provider (getSchedule); nelle prove in Debug quella finta di --calendario-finto.</summary>
    public static Func<IMailProvider, IReadOnlyList<string>, DateTime, DateTime, Task<List<Availability>?>> ScheduleSource { get; set; } =
        (p, emails, from, to) => p.GetScheduleAsync(emails, from, to);

    /// <summary>L'evento che la finestra modifica (null: evento nuovo), e se tutta la serie.</summary>
    CalendarEvent? editing;
    bool editSeries;
    readonly Popup suggestions = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    readonly ListBox suggestionList = new() { MaxHeight = 240, MinWidth = 320 };
    TextBox? suggestFor;
    /// <summary>La riga della riunione di Teams, nascosta per gli account personali.</summary>
    TextBlock? teamsLabel;
    bool saving;

    /// <summary>L'evento scritto con Salva o Invia, e l'account in cui è andato.</summary>
    public NewEvent? Created { get; private set; }
    public IMailProvider? CreatedIn { get; private set; }

    /// <summary>I giorni della ripetizione settimanale, dal lunedì: il nome si traduce dove si mostra (T).</summary>
    static readonly (DayOfWeek Day, string Name)[] Week =
    [
        (DayOfWeek.Monday, N("Lun")), (DayOfWeek.Tuesday, N("Mar")), (DayOfWeek.Wednesday, N("Mer")), (DayOfWeek.Thursday, N("Gio")),
        (DayOfWeek.Friday, N("Ven")), (DayOfWeek.Saturday, N("Sab")), (DayOfWeek.Sunday, N("Dom")),
    ];

    public EventWindow(IReadOnlyList<Choice> choices, Choice? preferred, DateTime start, DateTime end, bool wholeDay)
    {
        Title = T("Nuovo evento");
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 24) };
        panel.Children.Add(heading);
        editNote.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        panel.Children.Add(editNote);
        // Un modulo a due colonne, come la finestra dell'evento di Outlook: etichetta a sinistra, campo a destra.
        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TextBlock Field(string label, UIElement control)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 8, 0), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(text, form.RowDefinitions.Count - 1);
            form.Children.Add(text);
            if (control is FrameworkElement element) element.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(control, form.RowDefinitions.Count - 1);
            Grid.SetColumn(control, 1);
            form.Children.Add(control);
            return text;
        }
        panel.Children.Add(form);
        AutomationProperties.SetName(subject, T("Titolo"));
        Field(T("Titolo"), subject);

        AutomationProperties.SetName(required, T("Partecipanti"));
        AutomationProperties.SetName(optional, T("Partecipanti facoltativi"));
        required.ToolTip = T("Con dei partecipanti l'evento diventa una riunione e partono gli inviti");
        Field(T("Partecipanti"), required);
        Field(T("Facoltativi"), optional);

        foreach (var c in choices) calendar.Items.Add(c);
        calendar.SelectedItem = preferred ?? choices.FirstOrDefault(c => c.Calendar.IsDefault) ?? choices.FirstOrDefault();
        AutomationProperties.SetName(calendar, T("Calendario"));
        Field(T("Calendario"), calendar);

        startDate.SelectedDate = start.Date;
        endDate.SelectedDate = (wholeDay ? end.AddDays(-1) : end).Date;
        startTime.Text = $"{start.Hour}:{start.Minute:00}";
        endTime.Text = $"{end.Hour}:{end.Minute:00}";
        allDay.IsChecked = wholeDay;
        AutomationProperties.SetName(startDate, T("Data di inizio"));
        AutomationProperties.SetName(startTime, T("Ora di inizio"));
        AutomationProperties.SetName(endDate, T("Data di fine"));
        AutomationProperties.SetName(endTime, T("Ora di fine"));
        var startRow = Row(startDate, startTime);
        allDay.Margin = new Thickness(16, 0, 0, 0);
        allDay.VerticalAlignment = VerticalAlignment.Center;
        startRow.Children.Add(allDay);
        Field(T("Inizio"), startRow);
        Field(T("Fine"), Row(endDate, endTime));

        // Disponibilità dei partecipanti, come l'Assistente Pianificazione di Outlook: solo al clic, dalle 8 alle 20 del giorno.
        var check = new Button { Content = T("Controlla la disponibilità"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 4) };
        AutomationProperties.SetName(check, T("Controlla la disponibilità dei partecipanti"));
        check.Click += async (_, _) => await ShowAvailabilityAsync();
        availabilityNote.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        var availabilityRow = new StackPanel();
        availabilityRow.Children.Add(check);
        availabilityRow.Children.Add(availabilityNote);
        availabilityRow.Children.Add(availability);
        Field(T("Disponibilità"), availabilityRow);
        allDay.Checked += (_, _) => startTime.IsEnabled = endTime.IsEnabled = false;
        allDay.Unchecked += (_, _) => startTime.IsEnabled = endTime.IsEnabled = true;
        startTime.IsEnabled = endTime.IsEnabled = !wholeDay;
        // Spostando l'inizio la fine lo segue, con la stessa durata, come in Outlook.
        var duration = end - start;
        startDate.SelectedDateChanged += (_, _) =>
        {
            if (startDate.SelectedDate is { } d && endDate.SelectedDate is { } e && e < d) endDate.SelectedDate = d.AddDays(Math.Max(0, (int)duration.TotalDays));
            FitRepeatDays();
        };

        foreach (var (value, text) in new[] { (EventRepeat.None, T("Non si ripete")), (EventRepeat.Daily, T("Ogni giorno")), (EventRepeat.Weekly, T("Ogni settimana")), (EventRepeat.Monthly, T("Ogni mese")) })
            repeat.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        repeat.SelectedIndex = 0;
        AutomationProperties.SetName(repeat, T("Ripeti"));
        foreach (var (day, name) in Week)
        {
            var toggle = new ToggleButton { Content = T(name), Tag = day, MinWidth = 48, Margin = new Thickness(0, 0, 4, 0), IsChecked = day == start.DayOfWeek };
            AutomationProperties.SetName(toggle, Lingua.Culture.DateTimeFormat.GetDayName(day));
            repeatDays.Children.Add(toggle);
        }
        repeatEnd.Margin = new Thickness(16, 0, 0, 0);
        repeatEnd.Children.Add(new TextBlock { Text = T("fino al"), VerticalAlignment = VerticalAlignment.Center });
        repeatUntil.Width = 180;
        repeatUntil.ToolTip = T("Vuoto: senza fine");
        AutomationProperties.SetName(repeatUntil, T("Si ripete fino al"));
        repeatEnd.Children.Add(repeatUntil);
        var repeatRow = new StackPanel();
        var repeatLine = new StackPanel { Orientation = Orientation.Horizontal };
        repeatLine.Children.Add(repeat);
        repeatLine.Children.Add(repeatEnd);
        repeatRow.Children.Add(repeatLine);
        repeatRow.Children.Add(repeatDays);
        newOnly.Add(Field(T("Ripeti"), repeatRow));
        newOnly.Add(repeatRow);
        repeat.SelectionChanged += (_, _) =>
        {
            var chosen = RepeatChoice;
            repeatDays.Visibility = chosen == EventRepeat.Weekly ? Visibility.Visible : Visibility.Collapsed;
            repeatEnd.Visibility = chosen == EventRepeat.None ? Visibility.Collapsed : Visibility.Visible;
        };

        AutomationProperties.SetName(location, T("Luogo"));
        Field(T("Luogo"), location);

        foreach (var (minutes, text) in new (int?, string)[] { (null, T("Nessun promemoria")), (0, T("All'inizio")), (5, T("5 minuti prima")), (15, T("15 minuti prima")), (30, T("30 minuti prima")),
                                                                  (60, T("1 ora prima")), (24 * 60, T("1 giorno prima")) })
            reminder.Items.Add(new ComboBoxItem { Content = text, Tag = minutes });
        reminder.SelectedIndex = wholeDay ? 0 : 3;
        foreach (var (value, text) in new[] { ("free", T("Libero")), ("tentative", T("Provvisorio")), ("busy", T("Occupato")), ("oof", T("Fuori sede")), ("workingElsewhere", T("Lavoro altrove")) })
            showAs.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        showAs.SelectedIndex = wholeDay ? 0 : 2;
        AutomationProperties.SetName(reminder, T("Promemoria"));
        AutomationProperties.SetName(showAs, T("Mostra come"));
        var options = new StackPanel { Orientation = Orientation.Horizontal };
        options.Children.Add(reminder);
        options.Children.Add(new TextBlock { Text = T("Mostra come"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        options.Children.Add(showAs);
        Field(T("Promemoria"), options);
        foreach (var box in new[] { required, optional })
        {
            box.TextChanged += (_, _) => { Suggest(box); FitSaveButton(); };
            box.PreviewKeyDown += OnPeopleKey;
        }
        suggestionList.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(suggestionList, (DependencyObject)e.OriginalSource) is ListBoxItem { Tag: AddressBook.Entry entry }) Pick(entry);
        };
        var border = new Border { Child = suggestionList, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(2) };
        border.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorDefaultBrush");
        suggestions.Child = border;

        teamsLabel = Field("", teams);
        newOnly.Add(teamsLabel);
        newOnly.Add(teams);
        calendar.SelectionChanged += (_, _) => FitTeams();
        FitTeams();

        AutomationProperties.SetName(description, T("Descrizione"));
        description.Document.FontFamily = SystemFonts.MessageFontFamily; // come il resto della finestra, non il carattere dei documenti
        description.Document.FontSize = 14;
        newOnly.Add(Field(T("Descrizione"), description));
        newOnly.Add(description);
        panel.Children.Add(error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = T("Annulla"), MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.Click += async (_, _) => await SaveAsync();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = SystemParameters.WorkArea.Height - 80 };
        Loaded += (_, _) => subject.Focus();
        Closing += (_, e) => e.Cancel = saving; // mentre si scrive sul server
#if DEBUG
        DevTools.HideForPhotos(this); // le foto (--foto, anche in inglese): fuori dallo schermo, senza il fuoco
#endif
    }

    EventRepeat RepeatChoice => repeat.SelectedItem is ComboBoxItem { Tag: EventRepeat r } ? r : EventRepeat.None;

    static StackPanel Row(DatePicker date, ComboBox time)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(date);
        row.Children.Add(time);
        return row;
    }

    /// <summary>Ogni settimana: se nessun giorno è scelto, quello dell'inizio.</summary>
    void FitRepeatDays()
    {
        if (startDate.SelectedDate is not { } d || repeatDays.Children.OfType<ToggleButton>().Any(t => t.IsChecked == true)) return;
        foreach (var t in repeatDays.Children.OfType<ToggleButton>()) t.IsChecked = (DayOfWeek)t.Tag == d.DayOfWeek;
    }

    /// <summary>Teams solo per gli account di lavoro o della scuola (Graph onlineMeetingProvider teamsForBusiness).</summary>
    void FitTeams()
    {
        bool work = calendar.SelectedItem is Choice { Provider.Account.Tenant: not null };
        teams.Visibility = work ? Visibility.Visible : Visibility.Collapsed;
        if (teamsLabel != null) teamsLabel.Visibility = teams.Visibility;
        if (!work) teams.IsChecked = false;
    }

    /// <summary>
    /// Con dei partecipanti il pulsante è Invia, come nel nuovo Outlook: partono gli inviti; nella modifica "Invia
    /// aggiornamento", perché Exchange lo manda a tutti.
    /// </summary>
    void FitSaveButton() => save.Content = required.Text.Trim().Length + optional.Text.Trim().Length > 0 ? (editing != null ? T("Invia aggiornamento") : T("Invia")) : T("Salva");

    /// <summary>
    /// Modifica di un evento proprio, come nel nuovo Outlook: la finestra con i campi dell'evento, nel suo calendario.
    /// Ripetizione, descrizione e Teams non si mostrano e restano com'erano (la descrizione vera è HTML, e qui c'è solo la
    /// sua anteprima in testo). series: tutta la serie invece della sola occorrenza.
    /// </summary>
    public static EventWindow ForEdit(Choice choice, CalendarEvent e, bool series)
    {
        var window = new EventWindow([choice], choice, e.Start, e.End, e.AllDay);
        window.Edit(e, series);
        return window;
    }

    void Edit(CalendarEvent e, bool series)
    {
        editing = e;
        editSeries = series && e.SeriesMasterId.Length > 0;
        Title = heading.Text = T("Modifica evento");
        subject.Text = e.Subject;
        location.Text = e.Location;
        required.Text = string.Concat(e.Attendees.Where(a => !a.Optional).Select(a => a.Person.FieldText() + "; "));
        optional.Text = string.Concat(e.Attendees.Where(a => a.Optional).Select(a => a.Person.FieldText() + "; "));
        suggestions.IsOpen = false;
        int? minutes = e.ReminderOn ? e.ReminderMinutes : null;
        var match = reminder.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int?)i.Tag == minutes);
        if (match == null)
        {
            match = new ComboBoxItem { Content = T("{0} minuti prima", minutes), Tag = minutes };
            reminder.Items.Add(match);
        }
        reminder.SelectedItem = match;
        showAs.SelectedItem = showAs.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == e.ShowAs) ?? showAs.SelectedItem;
        calendar.IsEnabled = false;
        foreach (var part in newOnly) part.Visibility = Visibility.Collapsed;
        var note = new List<string>();
        if (e.IsRecurring) note.Add(editSeries ? T("Modifichi tutta la serie: i giorni della ripetizione restano quelli.") : T("Modifichi solo questo evento della serie."));
        note.Add(T("Ripetizione e descrizione restano com'erano."));
        if (e.Attendees.Count > 0) note.Add(T("I partecipanti ricevono l'aggiornamento."));
        editNote.Text = string.Join(" ", note);
        editNote.Visibility = Visibility.Visible;
        FitSaveButton();
    }

    // ---------------------------------------------------------------- disponibilità

    /// <summary>
    /// La disponibilità dei partecipanti nel giorno dell'inizio, dalle 8 alle 20 a mezz'ore: una riga per persona, occupato
    /// pieno, provvisorio e lavoro altrove tenui, fuori sede viola; le mezz'ore dell'evento segnate sopra. Con gli account
    /// personali e senza consenso al calendario lo si dice, senza richieste.
    /// </summary>
    async Task ShowAvailabilityAsync()
    {
        availability.Children.Clear();
        if (calendar.SelectedItem is not Choice choice || startDate.SelectedDate is not { } day) return;
        var must = ComposeWindow.Parse(required.Text, out _, skipInvalid: true) ?? new();
        var may = ComposeWindow.Parse(optional.Text, out _, skipInvalid: true) ?? new();
        var people = must.Concat(may).GroupBy(a => a.Email.ToLowerInvariant()).Select(g => g.First()).ToList();
        if (people.Count == 0)
        {
            availabilityNote.Text = T("Scrivi prima i partecipanti.");
            return;
        }
        var from = day.Date.AddHours(8);
        var to = day.Date.AddHours(20);
        availabilityNote.Text = T("Controllo…");
        List<Availability>? found;
        try { found = await ScheduleSource(choice.Provider, people.Select(p => p.Email).ToList(), from, to); }
        catch (Exception ex)
        {
            availabilityNote.Text = ex.Message;
            return;
        }
        if (found == null)
        {
            availabilityNote.Text = T("Per vedere la disponibilità Missiva deve poter usare il calendario: premi \"Consenti l'accesso al calendario\" nel calendario.");
            return;
        }
        availabilityNote.Text = T("{0}, dalle 8 alle 20: pieno occupato, tenue provvisorio, viola fuori sede. In alto l'evento.", day.ToString("dddd d MMMM", Lingua.Culture));
        DateTime? start = AutoRepliesWindow.ParseTime(startTime.Text) is { } st && allDay.IsChecked != true ? day.Date + st : null;
        DateTime? end = AutoRepliesWindow.ParseTime(endTime.Text) is { } et && allDay.IsChecked != true ? (endDate.SelectedDate ?? day).Date + et : null;
        UIElement Strip(Func<int, Brush?> fill, string name, string tip)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0), ToolTip = tip };
            var label = new TextBlock { Text = name, Width = 150, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            var cells = new UniformGrid { Rows = 1, Columns = 24, Height = 14 };
            for (int i = 0; i < 24; i++)
            {
                var cell = new Border { Margin = new Thickness(0, 0, 1, 0), BorderThickness = new Thickness(i % 2 == 0 ? 1 : 0, 0, 0, 0), Background = fill(i) };
                cell.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                cells.Children.Add(cell);
            }
            row.Children.Add(cells);
            return row;
        }
        var accent = (Brush)FindResource("AccentFillColorDefaultBrush");
        Brush Soft(Brush b) => b is SolidColorBrush s ? new SolidColorBrush(Color.FromArgb(0x70, s.Color.R, s.Color.G, s.Color.B)) : b;
        var away = new SolidColorBrush(Color.FromRgb(0x87, 0x64, 0xB8));
        availability.Children.Add(Strip(i => start is { } s && end is { } e && from.AddMinutes(30 * i) < e && from.AddMinutes(30 * (i + 1)) > s ? Soft(accent) : null, T("Evento"), T("L'orario dell'evento")));
        foreach (var person in people)
        {
            var a = found.FirstOrDefault(f => f.Email.Equals(person.Email, StringComparison.OrdinalIgnoreCase));
            var view = a?.View ?? "";
            availability.Children.Add(Strip(i => i < view.Length ? view[i] switch { '2' => accent, '1' or '4' => Soft(accent), '3' => away, _ => null } : null,
                person.Display, a?.Error is { } err ? $"{person.Email}: {err}" : person.Email));
        }
        Log.Write($"disponibilità di {people.Count} partecipanti il {day:dd/MM/yyyy} ({found.Count(f => f.Error == null)} con il calendario)");
    }

    // ---------------------------------------------------------------- suggerimenti dei partecipanti

    /// <summary>L'indirizzo che si sta scrivendo (dopo l'ultimo ";" o ","): da due lettere, le persone della rubrica.</summary>
    void Suggest(TextBox box)
    {
        var text = box.Text;
        var cut = text.LastIndexOfAny([';', ',']);
        var typed = text[(cut + 1)..].Trim();
        if (typed.Length < 2)
        {
            suggestions.IsOpen = false;
            return;
        }
        var already = (ComposeWindow.Parse(text[..Math.Max(0, cut)], out _, skipInvalid: true) ?? new()).Select(a => a.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = AddressBook.Suggest(typed, 8, already);
        suggestionList.Items.Clear();
        foreach (var entry in found)
            suggestionList.Items.Add(new ListBoxItem
            {
                Content = entry.Group != null ? $"{entry.Name} ({entry.Detail})" : entry.Name.Length > 0 ? $"{entry.Name} <{entry.Email}>" : entry.Email, Tag = entry,
                ToolTip = entry.Group != null ? entry.ToString() : entry.Detail,
            });
        if (found.Count == 0)
        {
            suggestions.IsOpen = false;
            return;
        }
        suggestFor = box;
        suggestions.PlacementTarget = box;
        suggestions.IsOpen = true;
    }

    void OnPeopleKey(object sender, KeyEventArgs e)
    {
        if (!suggestions.IsOpen || sender != suggestFor) return;
        switch (e.Key)
        {
            case Key.Down:
                suggestionList.SelectedIndex = Math.Min(suggestionList.Items.Count - 1, suggestionList.SelectedIndex + 1);
                e.Handled = true;
                break;
            case Key.Up:
                suggestionList.SelectedIndex = Math.Max(0, suggestionList.SelectedIndex - 1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Tab when suggestionList.SelectedItem is ListBoxItem { Tag: AddressBook.Entry entry }:
                Pick(entry);
                e.Handled = true;
                break;
            case Key.Escape:
                suggestions.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    void Pick(AddressBook.Entry entry)
    {
        if (suggestFor is not { } box) return;
        var cut = box.Text.LastIndexOfAny([';', ',']);
        // Un gruppo di contatti entra con tutte le sue persone (Entry.ToString), una persona come "Nome <indirizzo>".
        box.Text = (cut >= 0 ? box.Text[..(cut + 1)] + " " : "") + entry + "; ";
        box.CaretIndex = box.Text.Length;
        suggestions.IsOpen = false;
        box.Focus();
    }

    // ---------------------------------------------------------------- salvataggio

    /// <summary>L'evento come è nella finestra; null (dopo il messaggio sotto) se manca qualcosa.</summary>
    NewEvent? Read()
    {
        void Fail(string text)
        {
            error.Text = text;
            error.Visibility = Visibility.Visible;
        }
        error.Visibility = Visibility.Collapsed;
        if (calendar.SelectedItem is not Choice choice) { Fail(T("Scegli il calendario.")); return null; }
        if (startDate.SelectedDate is not { } sd || endDate.SelectedDate is not { } ed) { Fail(T("Scegli il giorno di inizio e quello di fine.")); return null; }
        bool whole = allDay.IsChecked == true;
        DateTime start, end;
        if (whole)
        {
            start = sd.Date;
            end = ed.Date.AddDays(1);
        }
        else
        {
            if (AutoRepliesWindow.ParseTime(startTime.Text) is not { } st || AutoRepliesWindow.ParseTime(endTime.Text) is not { } et) { Fail(T("Scrivi le ore come 9:30.")); return null; }
            start = sd.Date + st;
            end = ed.Date + et;
        }
        if (end <= start) { Fail(T("La fine deve venire dopo l'inizio.")); return null; }
        var must = ComposeWindow.Parse(required.Text, out var bad1);
        var may = ComposeWindow.Parse(optional.Text, out var bad2);
        if (must == null || may == null) { Fail(T("Controlla l'indirizzo \"{0}\".", bad1 ?? bad2)); return null; }
        var repeatKind = RepeatChoice;
        var days = repeatDays.Children.OfType<ToggleButton>().Where(t => t.IsChecked == true).Select(t => (DayOfWeek)t.Tag).ToList();
        if (repeatKind == EventRepeat.Weekly && days.Count == 0) days.Add(start.DayOfWeek);
        if (repeatKind != EventRepeat.None && repeatUntil.SelectedDate is { } until && until.Date < start.Date) { Fail(T("La ripetizione deve finire dopo il primo giorno.")); return null; }
        return new NewEvent
        {
            CalendarId = choice.Calendar.Id,
            Subject = subject.Text.Trim(),
            Start = start,
            End = end,
            AllDay = whole,
            Location = location.Text.Trim(),
            ReminderMinutes = reminder.SelectedItem is ComboBoxItem { Tag: int minutes } ? minutes : null,
            ShowAs = showAs.SelectedItem is ComboBoxItem { Tag: string s } ? s : "busy",
            Repeat = repeatKind,
            RepeatDays = days,
            RepeatUntil = repeatKind == EventRepeat.None ? null : repeatUntil.SelectedDate?.Date,
            Html = description.PlainText.Trim().Length > 0 ? FlowToHtml.Convert(description.Document) : "",
            Required = must,
            Optional = may.Where(a => !must.Any(m => m.Email.Equals(a.Email, StringComparison.OrdinalIgnoreCase))).ToList(),
            OnlineMeeting = teams.IsChecked == true,
        };
    }

    async Task SaveAsync()
    {
        if (saving || Read() is not { } e || calendar.SelectedItem is not Choice choice) return;
        saving = true;
        save.IsEnabled = false;
        try
        {
            if (editing != null)
            {
                await choice.Provider.UpdateEventAsync(editing, e, editSeries);
                Log.Write($"evento {editing.Id} modificato in {choice.Provider.Account.Email}{(editSeries ? ", tutta la serie" : "")}");
            }
            else
            {
                await choice.Provider.CreateEventAsync(e);
                Log.Write($"evento creato in {choice.Provider.Account.Email}{(e.IsMeeting ? $", riunione con {e.Required.Count + e.Optional.Count} partecipanti" : "")}");
            }
            if (e.IsMeeting) AddressBook.Sent(e.Required.Concat(e.Optional), DateTime.Now);
            Created = e;
            CreatedIn = choice.Provider;
            saving = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Error("nuovo evento", ex);
            error.Text = editing != null ? T("Modifica non salvata: {0}", ex.Message) : e.IsMeeting ? T("Riunione non inviata: {0}", ex.Message) : T("Evento non salvato: {0}", ex.Message);
            error.Visibility = Visibility.Visible;
            save.IsEnabled = true;
        }
        finally
        {
            saving = false;
        }
    }

#if DEBUG
    /// <summary>Prove (--nuovo-evento): la finestra riempita come da un utente, da fotografare; Salva resta da premere.</summary>
    internal void FillSample()
    {
        subject.Text = "Riunione con il cliente Rossi";
        location.Text = "Studio, sala Verdi";
        required.Text = "Anna Bianchi <anna.bianchi@studio-prova.example>; ";
        optional.Text = "luca.verdi@cliente-prova.example; ";
        repeat.SelectedIndex = 2;
        foreach (var t in repeatDays.Children.OfType<ToggleButton>()) t.IsChecked = (DayOfWeek)t.Tag is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday;
        description.SetPlainText("Ordine del giorno: preventivo e tempi.");
        suggestions.IsOpen = false;
    }

#endif

    /// <summary>
    /// Rispondi con riunione: oggetto, partecipanti e il testo del messaggio (in testo semplice, al massimo 4000 caratteri)
    /// come nel nuovo Outlook. Resta tutto da controllare: parte solo con Invia.
    /// </summary>
    public void FillReply(string messageSubject, IEnumerable<Address> people, string text)
    {
        subject.Text = messageSubject;
        required.Text = string.Concat(people.Select(a => a.FieldText() + "; "));
        suggestions.IsOpen = false;
        var trimmed = text.Trim();
        if (trimmed.Length > 4000) trimmed = trimmed[..4000] + "…";
        if (trimmed.Length > 0) description.SetPlainText(trimmed);
        FitSaveButton();
    }

#if DEBUG
    /// <summary>Prove (--modifica-evento): nella finestra di modifica cambiano il luogo e l'ora di fine, come da un utente.</summary>
    internal void ChangeSample()
    {
        location.Text = "Studio, sala Rossa";
        if (AutoRepliesWindow.ParseTime(endTime.Text) is { } end) endTime.Text = $"{(end + TimeSpan.FromMinutes(30)).Hours}:{(end + TimeSpan.FromMinutes(30)).Minutes:00}";
    }
#endif
}
