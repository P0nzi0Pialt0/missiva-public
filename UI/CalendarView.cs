using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Missiva.UI;

public enum CalendarMode { Day, WorkWeek, Week, Month }

/// <summary>
/// Il calendario, come nel nuovo Outlook: Giorno, Settimana lavorativa, Settimana e Mese; a sinistra i calendari di ogni
/// account con i loro colori (si mostrano e si nascondono), a destra i dettagli dell'evento scelto con i partecipanti e
/// "Partecipa" per le riunioni online. Legge Graph (calendarView, IMailProvider.GetCalendarAsync), dopo il consenso al
/// calendario chiesto solo dal pulsante; subito la copia locale della settimana di oggi. Nessun contenuto degli eventi
/// passa da un motore HTML: si mostra l'anteprima in testo.
/// </summary>
public sealed class CalendarView : UserControl
{
    static readonly CultureInfo Italian = new("it-IT");
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    const double HourHeight = 48;

    readonly Func<IReadOnlyList<IMailProvider>> providers;
    /// <summary>Da dove arrivano gli eventi: il provider (Graph); nelle prove in Debug gli eventi finti di --calendario-finto.</summary>
    public Func<IMailProvider, DateTime, DateTime, CancellationToken, Task<CalendarData?>> Source { get; set; } =
        (p, from, to, cancel) => p.GetCalendarAsync(from, to, cancel);

    /// <summary>Per account: i calendari e gli eventi arrivati (null: manca il consenso), o l'errore.</summary>
    readonly Dictionary<IMailProvider, CalendarData?> data = new();
    readonly Dictionary<IMailProvider, string> errors = new();
    DateTime anchor = DateTime.Today;
    CalendarMode mode = Enum.TryParse<CalendarMode>(Store.Settings.CalendarMode, out var saved) ? saved : CalendarMode.WorkWeek;
    (IMailProvider Provider, CalendarEvent Event)? selected;
    CancellationTokenSource? loading;
    int version;

    readonly TextBlock title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    readonly TextBlock status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly StackPanel accountsPanel = new() { Margin = new Thickness(4, 8, 8, 8) };
    readonly ContentControl body = new();
    readonly Border details = new() { Width = 330, Margin = new Thickness(12, 0, 0, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
    readonly Dictionary<CalendarMode, RadioButton> modeButtons = new();

    /// <summary>Gli eventi arrivati (per i promemoria), per account.</summary>
    public event Action<IMailProvider, CalendarData>? EventsArrived;
    /// <summary>"Consenti l'accesso al calendario": lo chiede MainWindow (finestra di Microsoft, solo al clic).</summary>
    public Func<IMailProvider, Task<bool>>? AllowRequested { get; set; }

    public CalendarView(Func<IReadOnlyList<IMailProvider>> providers)
    {
        this.providers = providers;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Barra: Oggi, frecce, periodo, e la vista a destra.
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        Grid.SetColumnSpan(bar, 3);
        var views = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (m, name) in new[] { (CalendarMode.Day, "Giorno"), (CalendarMode.WorkWeek, "Settimana lavorativa"), (CalendarMode.Week, "Settimana"), (CalendarMode.Month, "Mese") })
        {
            var button = new RadioButton { Content = name, GroupName = "calendario", Margin = new Thickness(0, 0, 4, 0), IsChecked = m == mode };
            button.SetResourceReference(StyleProperty, "Tab");
            AutomationProperties.SetName(button, name);
            button.Checked += (_, _) => SetMode(m);
            modeButtons[m] = button;
            views.Children.Add(button);
        }
        DockPanel.SetDock(views, Dock.Right);
        bar.Children.Add(views);
        var create = new Button { Content = "Nuovo evento", Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(14, 5, 14, 6) };
        create.SetResourceReference(StyleProperty, "AccentButtonStyle");
        AutomationProperties.SetName(create, "Nuovo evento");
        create.ToolTip = "Nuovo evento (Ctrl+N nel calendario, Ctrl+Maiusc+A da ovunque)";
        create.Click += (_, _) => NewEvent();
        DockPanel.SetDock(create, Dock.Left);
        bar.Children.Add(create);
        var today = Command("", "Oggi", "Oggi");
        today.Click += (_, _) => { anchor = DateTime.Today; hoursMoved = false; Show(); }; // "Oggi" riporta anche le ore al mattino
        var back = Command("", null, "Periodo precedente");
        back.Click += (_, _) => Step(-1);
        var forward = Command("", null, "Periodo successivo");
        forward.Click += (_, _) => Step(1);
        foreach (var b in new[] { today, back, forward }) { DockPanel.SetDock(b, Dock.Left); bar.Children.Add(b); }
        DockPanel.SetDock(title, Dock.Left);
        bar.Children.Add(title);
        status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        bar.Children.Add(status);
        root.Children.Add(bar);

        var left = new ScrollViewer { Content = accountsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(left, 1);
        root.Children.Add(left);

        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = body, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        Grid.SetRow(card, 1);
        Grid.SetColumn(card, 1);
        root.Children.Add(card);

        details.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        details.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        Grid.SetRow(details, 1);
        Grid.SetColumn(details, 2);
        root.Children.Add(details);
        Content = root;
    }

    Button Command(string glyph, string? text, string name)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, text != null ? 6 : 0, 0) });
        if (text != null) content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, ToolTip = name, Margin = new Thickness(0, 0, 4, 0) };
        button.SetResourceReference(StyleProperty, "Command");
        AutomationProperties.SetName(button, name);
        return button;
    }

    // ---------------------------------------------------------------- periodo

    /// <summary>Le date mostrate: un giorno, lunedì-venerdì, lunedì-domenica, o le sei settimane del mese.</summary>
    (DateTime From, DateTime To) Range()
    {
        var monday = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));
        switch (mode)
        {
            case CalendarMode.Day: return (anchor, anchor.AddDays(1));
            // Di sabato e di domenica, come in Outlook, la settimana lavorativa è quella che viene.
            case CalendarMode.WorkWeek: return anchor.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? (monday.AddDays(7), monday.AddDays(12)) : (monday, monday.AddDays(5));
            case CalendarMode.Week: return (monday, monday.AddDays(7));
            default:
                var first = new DateTime(anchor.Year, anchor.Month, 1);
                var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
                return (start, start.AddDays(42));
        }
    }

    void Step(int direction)
    {
        anchor = mode switch
        {
            CalendarMode.Day => anchor.AddDays(direction),
            CalendarMode.Month => new DateTime(anchor.Year, anchor.Month, 1).AddMonths(direction),
            _ => anchor.AddDays(7 * direction),
        };
        Show();
    }

    void SetMode(CalendarMode m)
    {
        if (mode == m && body.Content != null) return;
        mode = m;
        Store.Settings.CalendarMode = m.ToString();
        Store.SaveSoon();
        Show();
    }

    /// <summary>Apre (o riapre) il calendario su una data e una vista, per le prove e per "Oggi".</summary>
    public void Open(DateTime? day = null, CalendarMode? view = null)
    {
        if (day is { } d) anchor = d.Date;
        if (view is { } v)
        {
            mode = v;
            modeButtons[v].IsChecked = true;
        }
        // Subito la copia locale della settimana di oggi, se c'è; poi il server.
        foreach (var p in providers().Where(p => p.HasCalendar && !data.ContainsKey(p)))
            if (Calendars.Load(p.Account) is { } cached) data[p] = cached;
        Show();
    }

    /// <summary>Disegna il periodo con quello che c'è, poi chiede gli eventi agli account.</summary>
    void Show()
    {
        Render();
        lastLoad = LoadAsync();
    }

    /// <summary>L'ultima lettura dei calendari chiesta agli account.</summary>
    Task lastLoad = Task.CompletedTask;

    /// <summary>
    /// Nuovo evento da una scorciatoia (Ctrl+N nel calendario, Ctrl+Maiusc+A da ovunque, come nel nuovo Outlook): con il
    /// calendario appena aperto i calendari in cui scrivere possono non essere ancora arrivati, e allora si aspettano.
    /// </summary>
    public async void NewEventWhenReady()
    {
        if (Writable().Count == 0)
            try { await lastLoad; } catch (Exception ex) { Log.Error("calendari per il nuovo evento", ex); }
        NewEvent();
    }

    async Task LoadAsync()
    {
        loading?.Cancel();
        var cancel = (loading = new CancellationTokenSource()).Token;
        var v = ++version;
        var (from, to) = Range();
        var list = providers().Where(p => p.HasCalendar).ToList();
        if (list.Count == 0) return;
        status.Text = "Aggiornamento…";
        var clock = Stopwatch.StartNew();
        await Task.WhenAll(list.Select(async p =>
        {
            try
            {
                var got = await Source(p, from, to, cancel);
                if (v != version) return;
                errors.Remove(p);
                data[p] = got;
                if (got != null)
                {
                    Log.Time($"calendario di {p.Account.Email} dal {from:dd/MM} al {to:dd/MM} ({got.Events.Count} eventi, {got.Calendars.Count} calendari)", clock);
                    // La copia locale tiene la settimana di oggi, quella che si vede aprendo il calendario.
                    if (from <= DateTime.Today && to > DateTime.Today && mode != CalendarMode.Day) Calendars.Save(p.Account, got);
                    EventsArrived?.Invoke(p, got);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (v != version) return;
                Log.Error($"calendario di {p.Account.Email}", ex);
                errors[p] = ex.Message;
            }
            if (v == version) Render();
        }));
        if (v == version) status.Text = "";
    }

    // ---------------------------------------------------------------- disegno

    /// <summary>Gli eventi del periodo nei calendari mostrati, con il loro account e il colore del loro calendario.</summary>
    List<(IMailProvider Provider, CalendarEvent Event, Color Color)> Visible(DateTime from, DateTime to)
    {
        var result = new List<(IMailProvider, CalendarEvent, Color)>();
        foreach (var (p, d) in data)
        {
            if (d == null) continue;
            var colors = d.Calendars.ToDictionary(c => c.Id, c => (Color)ColorConverter.ConvertFromString(c.Color));
            foreach (var e in d.Events)
            {
                if (Store.Settings.HiddenCalendars.Contains(p.Account.Id + "|" + e.CalendarId)) continue;
                if (e.End <= from && !(e.End == e.Start && e.Start >= from) || e.Start >= to) continue;
                result.Add((p, e, colors.GetValueOrDefault(e.CalendarId, Color.FromRgb(0x0F, 0x6C, 0xBD))));
            }
        }
        return result;
    }

    void Render()
    {
#if DEBUG
        var renderClock = Stopwatch.StartNew();
        try { RenderNow(); }
        finally { testRenders++; testRenderMs += renderClock.Elapsed.TotalMilliseconds; }
    }

    int testRenders;
    double testRenderMs;

    /// <summary>Prove ("calendario stato" in MISSIVA_KEYS): quante volte la vista si è ricostruita, in quanto, e dove sono le ore.</summary>
    internal string TestState()
    {
        var hours = FindHours(body.Content as DependencyObject);
        string text = $"vista {mode} dal {Range().From:dd/MM}, ricostruita {testRenders} volte in {testRenderMs:0} ms, account con dati {data.Count(d => d.Value != null)}, " +
                      (hours == null ? "senza ore" : $"ore a {hours.VerticalOffset:0} px ({hours.VerticalOffset / HourHeight:0.0} h) su {hours.ScrollableHeight:0}");
        (testRenders, testRenderMs) = (0, 0);
        return text;
    }

    /// <summary>Prove ("calendario scorri N"): le ore a N pixel, come con la rotella.</summary>
    internal void TestScroll(double pixels) => FindHours(body.Content as DependencyObject)?.ScrollToVerticalOffset(pixels);

    static ScrollViewer? FindHours(DependencyObject? d)
    {
        if (d == null) return null;
        if (d is ScrollViewer { Content: Grid } found) return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindHours(VisualTreeHelper.GetChild(d, i)) is { } inner) return inner;
        return null;
    }

    void RenderNow()
    {
#endif
        var (from, to) = Range();
        title.Text = mode switch
        {
            CalendarMode.Day => anchor.ToString("dddd d MMMM yyyy", Italian),
            CalendarMode.Month => Capital(anchor.ToString("MMMM yyyy", Italian)),
            _ => Period(from, to.AddDays(-1)),
        };
        FillAccounts();
        var events = Visible(from, to);
        body.Content = mode == CalendarMode.Month ? MonthGrid(from, events) : WeekGrid(from, (int)(to - from).TotalDays, events);
        if (selected is { } s && !events.Any(x => x.Provider == s.Provider && x.Event.Id == s.Event.Id)) ShowDetails(null);
    }

    static string Capital(string text) => text.Length > 0 ? char.ToUpper(text[0], Italian) + text[1..] : text;

    static string Period(DateTime first, DateTime last) =>
        first.Month == last.Month ? $"{first.Day} - {last.ToString("d MMMM yyyy", Italian)}"
        : first.Year == last.Year ? $"{first.ToString("d MMMM", Italian)} - {last.ToString("d MMMM yyyy", Italian)}"
        : $"{first.ToString("d MMMM yyyy", Italian)} - {last.ToString("d MMMM yyyy", Italian)}";

    /// <summary>A sinistra, per account: i calendari con il loro colore (mostra e nascondi), o il pulsante del consenso.</summary>
    void FillAccounts()
    {
        accountsPanel.Children.Clear();
        foreach (var p in providers())
        {
            accountsPanel.Children.Add(new TextBlock { Text = p.Account.Email, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 10, 0, 4), ToolTip = p.Account.Email });
            if (!p.HasCalendar)
            {
                accountsPanel.Children.Add(Note("Per gli altri account il calendario (CalDAV) arriverà più avanti."));
                continue;
            }
            if (errors.TryGetValue(p, out var error))
            {
                var t = Note("Calendario non disponibile: " + error);
                Dialog.Critical(t);
                accountsPanel.Children.Add(t);
                continue;
            }
            if (!data.TryGetValue(p, out var d))
            {
                accountsPanel.Children.Add(Note("Caricamento…"));
                continue;
            }
            if (d == null)
            {
                accountsPanel.Children.Add(Note("Missiva non ha ancora il permesso di leggere il calendario di questo account."));
                var allow = new Button { Content = new TextBlock { Text = "Consenti l'accesso al calendario", TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(4, 6, 0, 4),
                                         HorizontalAlignment = HorizontalAlignment.Left };
                AutomationProperties.SetName(allow, "Consenti l'accesso al calendario di " + p.Account.Email);
                allow.Click += async (_, _) =>
                {
                    if (AllowRequested != null && await AllowRequested(p)) _ = LoadAsync();
                };
                accountsPanel.Children.Add(allow);
                continue;
            }
            foreach (var c in d.Calendars)
            {
                var key = p.Account.Id + "|" + c.Id;
                var color = (Color)ColorConverter.ConvertFromString(c.Color);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = c.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 });
                var check = new CheckBox { Content = row, IsChecked = !Store.Settings.HiddenCalendars.Contains(key), Margin = new Thickness(4, 1, 0, 1) };
                AutomationProperties.SetName(check, c.Name);
                void Changed(object sender, RoutedEventArgs e)
                {
                    Store.Settings.HiddenCalendars.Remove(key);
                    if (check.IsChecked != true) Store.Settings.HiddenCalendars.Add(key);
                    Store.SaveSoon();
                    Render();
                }
                check.Checked += Changed;
                check.Unchecked += Changed;
                accountsPanel.Children.Add(check);
            }
        }
    }

    /// <summary>Quanti eventi di un giorno intero si mostrano prima di "+N altri": tutti nella vista Giorno, tre nelle altre.</summary>
    internal static int MoreAfter(int count, bool dayView) => dayView ? count : Math.Min(count, 3);

    /// <summary>
    /// "+N altri": un pulsante (anche per la tastiera e UI Automation) che apre quel giorno nella vista Giorno, dove gli
    /// eventi ci sono tutti, come il numero del giorno nel mese.
    /// </summary>
    Button More(int count, DateTime day)
    {
        var more = new Button
        {
            Content = $"+{count} altri", FontSize = 12, Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(0, 1, 0, 1), MinWidth = 0, MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Mostra tutti gli eventi di " + day.ToString("dddd d MMMM", Italian),
        };
        more.SetResourceReference(ForegroundProperty, "SecondaryTextBrush");
        AutomationProperties.SetName(more, $"Altri {count} eventi di {day.ToString("dddd d MMMM", Italian)}: apri il giorno");
        more.Click += (_, _) => { anchor = day; modeButtons[CalendarMode.Day].IsChecked = true; };
        return more;
    }

    static TextBlock Note(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(4, 2, 0, 2) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    /// <summary>
    /// Giorno e settimane: le intestazioni dei giorni, la riga degli eventi di un giorno intero, poi le 24 ore con gli
    /// eventi al loro posto; si apre sulle 7:30, e nel giorno di oggi una linea segna l'ora.
    /// </summary>
    UIElement WeekGrid(DateTime from, int days, List<(IMailProvider Provider, CalendarEvent Event, Color Color)> events)
    {
        const double gutter = 52;
        Grid Columns()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gutter) });
            for (int i = 0; i < days; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }
        var outer = new DockPanel();

        var header = Columns();
        header.Margin = new Thickness(0, 8, 0, 4);
        var allDay = Columns();
        allDay.Margin = new Thickness(0, 0, 0, 4);
        for (int i = 0; i < days; i++)
        {
            var day = from.AddDays(i);
            bool isToday = day == DateTime.Today;
            var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 0) };
            var number = new TextBlock { Text = day.Day.ToString(), FontSize = 20, FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal };
            if (isToday) number.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            head.Children.Add(number);
            var name = new TextBlock { Text = Capital(day.ToString("dddd", Italian)), FontSize = 12 };
            name.SetResourceReference(TextBlock.ForegroundProperty, isToday ? "AccentTextFillColorPrimaryBrush" : "SecondaryTextBrush");
            head.Children.Add(name);
            Grid.SetColumn(head, i + 1);
            header.Children.Add(head);

            var chips = new StackPanel { Margin = new Thickness(2, 0, 2, 0), Background = Brushes.Transparent, MinHeight = 22 };
            // Doppio clic nella riga dei giorni interi: un evento di tutto il giorno.
            chips.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { e.Handled = true; NewEvent(day, wholeDay: true); } };
            // Nella vista Giorno tutti gli eventi di un giorno intero; nelle settimane i primi tre e "+N altri", che porta alla
            // vista Giorno. Prima "+N altri" era solo testo e anche la vista Giorno si fermava a tre: il quarto evento di un
            // giorno intero non si apriva in nessuna vista (audit, F-42).
            var whole = events.Where(x => Calendars.AllDayOn([x.Event], day).Any()).ToList();
            foreach (var (p, e, color) in whole.Take(MoreAfter(whole.Count, days == 1)))
                chips.Children.Add(Chip(p, e, color, compact: true));
            if (whole.Count > MoreAfter(whole.Count, days == 1)) chips.Children.Add(More(whole.Count - 3, day));
            // Nella vista Giorno con molti eventi di un giorno intero la riga scorre, e le ore sotto restano visibili.
            FrameworkElement row = days == 1 && whole.Count > 6 ? new ScrollViewer { Content = chips, MaxHeight = 196, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } : chips;
            Grid.SetColumn(row, i + 1);
            allDay.Children.Add(row);
        }
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(allDay, Dock.Top);
        outer.Children.Add(header);
        outer.Children.Add(allDay);

        var hours = Columns();
        hours.Height = 24 * HourHeight;
        for (int h = 0; h < 24; h++)
        {
            var label = new TextBlock { Text = $"{h:00}:00", FontSize = 11, Margin = new Thickness(8, h == 0 ? 2 : -7, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            var cell = new Grid { Height = HourHeight, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, h * HourHeight, 0, 0) };
            cell.Children.Add(label);
            hours.Children.Add(cell);
        }
        for (int i = 0; i < days; i++)
        {
            var day = from.AddDays(i);
            var column = new Grid { Background = Brushes.Transparent, AllowDrop = true };
            // Un evento lasciato qui si sposta a quell'ora, di quarto d'ora in quarto d'ora, con la sua durata.
            column.DragOver += (_, ev) =>
            {
                ev.Effects = ev.Data.GetData(typeof(DraggedEvent)) is DraggedEvent ? DragDropEffects.Move : DragDropEffects.None;
                ev.Handled = true;
            };
            column.Drop += async (_, ev) =>
            {
                ev.Handled = true;
                if (ev.Data.GetData(typeof(DraggedEvent)) is not DraggedEvent dragged) return;
                var quarters = Math.Clamp((int)Math.Round((ev.GetPosition(column).Y - dragged.GrabY) / (HourHeight / 4)), 0, 95);
                await MoveEventAsync(dragged.Provider, dragged.Event, day.AddMinutes(15 * quarters));
            };
            // Doppio clic su una fascia oraria, come in Outlook: un evento nuovo da quella mezz'ora.
            column.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                var halfHours = Math.Clamp((int)(e.GetPosition(column).Y / (HourHeight / 2)), 0, 47);
                NewEvent(day.AddMinutes(30 * halfHours));
            };
            // Le righe delle ore, e un filo fra i giorni.
            for (int h = 0; h < 24; h++)
            {
                var line = new Border { Height = HourHeight, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, h * HourHeight, 0, 0), BorderThickness = new Thickness(1, 1, 0, 0) };
                line.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                column.Children.Add(line);
            }
            var panel = new EventPanel();
            foreach (var placed in Calendars.Day(events.Select(x => x.Event), day))
            {
                var (p, e, color) = events.First(x => ReferenceEquals(x.Event, placed.Event));
                var chip = Chip(p, e, color, compact: false);
                EventPanel.SetPlace(chip, placed);
                panel.Children.Add(chip);
            }
            column.Children.Add(panel);
            if (day == DateTime.Today)
            {
                var now = new Border { Height = 2, Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)), VerticalAlignment = VerticalAlignment.Top,
                                       Margin = new Thickness(0, DateTime.Now.TimeOfDay.TotalHours * HourHeight - 1, 0, 0), IsHitTestVisible = false };
                column.Children.Add(now);
            }
            Grid.SetColumn(column, i + 1);
            hours.Children.Add(column);
        }
        var scroller = new ScrollViewer { Content = hours, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        // Dalle 7:30; nella vista del giorno da prima, se un evento comincia prima (il backup di notte).
        var earliest = mode != CalendarMode.Day ? 7.5 : events.Where(x => !x.Event.AllDay && x.Event.Start >= from && x.Event.Start < from.AddDays(days))
                                                                .Select(x => x.Event.Start.TimeOfDay.TotalHours).DefaultIfEmpty(7.5).Min();
        // La vista si ricostruisce a ogni arrivo dei dati di un account, e a ogni ricostruzione le ore tornavano alle 7:30
        // anche se nel frattempo si era scorso al pomeriggio (audit del 7 ottobre 2026, B-10; visto con --calendario-lento:
        // da 510 px a 336 a ogni account). Ora, finché periodo e vista sono gli stessi, le ore scorse a mano restano dove sono;
        // se nessuno le ha toccate vanno dove dicono i dati di adesso (nella vista del giorno, il primo evento).
        var period = (from, days, mode);
        if (period != hoursPeriod)
        {
            hoursPeriod = period;
            hoursMoved = false;
        }
        double start = hoursMoved ? hoursOffset : Math.Max(0, Math.Min(7.5, earliest - 0.5)) * HourHeight;
        bool settled = false;
        hoursScroller = scroller;
        scroller.Loaded += (_, _) => scroller.ScrollToVerticalOffset(start);
        scroller.ScrollChanged += (_, e) =>
        {
            if (e.VerticalChange == 0 || !ReferenceEquals(e.OriginalSource, scroller) || !ReferenceEquals(hoursScroller, scroller)) return;
            // Il primo spostamento è quello di Loaded, qui sopra; gli altri sono di chi guarda.
            if (!settled && Math.Abs(scroller.VerticalOffset - start) < 1)
            {
                settled = true;
                return;
            }
            settled = true;
            hoursMoved = true;
            hoursOffset = scroller.VerticalOffset;
        };
        outer.Children.Add(scroller);
        return outer;
    }

    /// <summary>Le ore mostrate (viste Giorno e settimane): di che periodo sono, se sono state scorse a mano e dove.</summary>
    (DateTime From, int Days, CalendarMode Mode) hoursPeriod;
    bool hoursMoved;
    double hoursOffset;
    ScrollViewer? hoursScroller;

    /// <summary>Il mese: sei settimane, per giorno fino a tre eventi e "+N altri"; il numero del giorno apre quel giorno.</summary>
    UIElement MonthGrid(DateTime from, List<(IMailProvider Provider, CalendarEvent Event, Color Color)> events)
    {
        var outer = new DockPanel();
        var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 8, 0, 4) };
        for (int i = 0; i < 7; i++)
        {
            var n = new TextBlock { Text = Capital(from.AddDays(i).ToString("dddd", Italian)), FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
            n.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            names.Children.Add(n);
        }
        DockPanel.SetDock(names, Dock.Top);
        outer.Children.Add(names);
        var grid = new UniformGrid { Rows = 6, Columns = 7 };
        for (int i = 0; i < 42; i++)
        {
            var day = from.AddDays(i);
            var cell = new Border { BorderThickness = new Thickness(1, 1, 0, 0), Padding = new Thickness(4, 2, 4, 2), Background = Brushes.Transparent };
            cell.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            cell.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { e.Handled = true; NewEvent(day, wholeDay: true); } };
            var stack = new StackPanel();
            var number = new Button { Content = day.Day == 1 ? day.ToString("d MMM", Italian) : day.Day.ToString(), Padding = new Thickness(4, 0, 4, 0), MinWidth = 0,
                                      HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                                      FontWeight = day == DateTime.Today ? FontWeights.SemiBold : FontWeights.Normal };
            AutomationProperties.SetName(number, day.ToString("dddd d MMMM", Italian));
            if (day == DateTime.Today) number.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            else if (day.Month != anchor.Month) number.SetResourceReference(ForegroundProperty, "SecondaryTextBrush");
            number.Click += (_, _) => { anchor = day; modeButtons[CalendarMode.Day].IsChecked = true; };
            stack.Children.Add(number);
            var today = events.Where(x => x.Event.AllDay ? Calendars.AllDayOn([x.Event], day).Any() : x.Event.Start.Date <= day && (x.Event.End > day || x.Event.Start.Date == day))
                              .OrderBy(x => !x.Event.AllDay).ThenBy(x => x.Event.Start).ToList();
            foreach (var (p, e, color) in today.Take(3)) stack.Children.Add(Chip(p, e, color, compact: true));
            if (today.Count > 3) stack.Children.Add(More(today.Count - 3, day));
            cell.Child = stack;
            grid.Children.Add(cell);
        }
        outer.Children.Add(grid);
        return outer;
    }

    /// <summary>
    /// Un evento: fondo del colore del calendario, attenuato, con la barretta a sinistra; provvisorio più chiaro, rifiutato
    /// o annullato barrato. Un pulsante: il clic (anche con UI Automation) mostra i dettagli.
    /// </summary>
    Button Chip(IMailProvider p, CalendarEvent e, Color color, bool compact)
    {
        bool dark = ReadingView.Dark;
        var tint = Color.FromArgb((byte)(e.Response == "tentativelyAccepted" || e.ShowAs == "free" ? (dark ? 0x30 : 0x22) : (dark ? 0x60 : 0x40)), color.R, color.G, color.B);
        var content = new StackPanel();
        var subject = new TextBlock { Text = (e.IsCancelled ? "Annullato: " : "") + (e.Subject.Length > 0 ? e.Subject : "(nessun titolo)"), FontSize = 12,
                                      FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        if (e.IsCancelled || e.Response == "declined") subject.TextDecorations = TextDecorations.Strikethrough;
        if (compact && !e.AllDay) subject.Text = $"{e.Start:HH:mm} {subject.Text}";
        content.Children.Add(subject);
        // Sotto i tre quarti d'ora c'è posto per una riga sola: l'ora va accanto al titolo.
        bool shortEvent = !compact && e.End - e.Start < TimeSpan.FromMinutes(45);
        if (shortEvent) subject.Text += $", {e.Start:HH:mm}";
        if (!compact && !shortEvent)
        {
            var line = new TextBlock { Text = $"{e.Start:HH:mm} - {e.End:HH:mm}" + (e.Location.Length > 0 ? " · " + e.Location : ""), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            line.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            content.Children.Add(line);
        }
        var button = new Button
        {
            Content = content, Background = new SolidColorBrush(tint), BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(6, compact ? 1 : 3, 4, 2), Margin = new Thickness(1, compact ? 1 : 0, 2, compact ? 1 : 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Template = ChipTemplate,
            // Larghi quanto la colonna (o la loro parte): il Button di Fluent si allinea a sinistra, largo quanto il testo.
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            ToolTip = $"{e.Subject}\n{Calendars.When(e)}" + (e.Location.Length > 0 ? "\n" + e.Location : ""), Cursor = System.Windows.Input.Cursors.Hand,
        };
        AutomationProperties.SetName(button, $"{(e.Subject.Length > 0 ? e.Subject : "(nessun titolo)")}, {Calendars.When(e)}");
        button.Click += (_, _) => ShowDetails((p, e));
        // Doppio clic su un evento proprio: la finestra in modifica, come nel nuovo Outlook.
        button.MouseDoubleClick += (_, ev) =>
        {
            if (!CanEdit(p, e)) return;
            ev.Handled = true;
            EditEvent(p, e);
        };
        // Trascinato nella giornata, un evento proprio con un orario si sposta (la durata resta).
        if (!compact && !e.AllDay && CanEdit(p, e))
        {
            Point? pressed = null;
            button.PreviewMouseLeftButtonDown += (_, ev) => pressed = ev.GetPosition(button);
            button.PreviewMouseMove += (_, ev) =>
            {
                if (ev.LeftButton != System.Windows.Input.MouseButtonState.Pressed || pressed is not { } at) return;
                var delta = ev.GetPosition(button) - at;
                if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                pressed = null;
                button.ReleaseMouseCapture(); // niente clic al rilascio
                DragDrop.DoDragDrop(button, new DataObject(typeof(DraggedEvent), new DraggedEvent(p, e, at.Y)), DragDropEffects.Move);
            };
            button.PreviewMouseLeftButtonUp += (_, _) => pressed = null;
        }
        return button;
    }

    /// <summary>Un evento che si trascina, con il punto in cui lo si è preso (pixel dall'alto dell'evento).</summary>
    sealed record DraggedEvent(IMailProvider Provider, CalendarEvent Event, double GrabY);

    /// <summary>Un evento proprio in un calendario modificabile: si modifica, si sposta, si elimina o si annulla.</summary>
    bool CanEdit(IMailProvider p, CalendarEvent e) =>
        e.IsOrganizer && !e.IsCancelled && p.HasCalendar && data.GetValueOrDefault(p)?.Calendars.FirstOrDefault(c => c.Id == e.CalendarId)?.CanEdit != false;

    /// <summary>Il pulsante degli eventi è un bordo con il contenuto: quello di Fluent metterebbe i suoi colori e angoli.</summary>
    static readonly ControlTemplate ChipTemplate = BuildChipTemplate();

    static ControlTemplate BuildChipTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(BorderBrush)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(BorderThickness)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(nameof(Padding)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(ClipToBoundsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    // ---------------------------------------------------------------- nuovo evento

    /// <summary>I calendari in cui si può scrivere: degli account con il consenso al calendario, non quelli di sola lettura.</summary>
    List<EventWindow.Choice> Writable() =>
        data.Where(d => d.Value != null && d.Key.HasCalendar)
            .SelectMany(d => d.Value!.Calendars.Where(c => c.CanEdit).Select(c => new EventWindow.Choice(d.Key, c))).ToList();

    /// <summary>L'ultimo calendario usato per un evento nuovo: il prossimo parte da lì.</summary>
    EventWindow.Choice? lastChoice;

    /// <summary>
    /// Nuovo evento, dalla barra (dalla prossima mezz'ora, per mezz'ora) o con doppio clic su una fascia oraria (da lì) o su
    /// un giorno (tutto il giorno). Poi il calendario si rilegge.
    /// </summary>
    public void NewEvent(DateTime? at = null, bool wholeDay = false)
    {
        var choices = Writable();
        var owner = Window.GetWindow(this);
        if (choices.Count == 0)
        {
            Dialog.Show(owner, "Nessun calendario in cui scrivere",
                "Per creare un evento serve un account Microsoft o Google con l'accesso al calendario: premi \"Consenti l'accesso al calendario\" a sinistra.");
            return;
        }
        var now = DateTime.Now;
        var start = at ?? now.Date.AddMinutes(30 * Math.Ceiling(now.TimeOfDay.TotalMinutes / 30));
        var end = wholeDay ? start.Date.AddDays(1) : start.AddMinutes(30);
        var preferred = choices.FirstOrDefault(c => c.Provider == lastChoice?.Provider && c.Calendar.Id == lastChoice.Calendar.Id);
        var window = new EventWindow(choices, preferred, wholeDay ? start.Date : start, end, wholeDay) { Owner = owner };
#if DEBUG
        if (Environment.GetCommandLineArgs().Contains("--nuovo-evento")) window.Loaded += (_, _) => window.FillSample();
#endif
        Log.Write($"calendario: nuovo evento dal {start:dd/MM/yyyy HH:mm}{(wholeDay ? ", tutto il giorno" : "")}");
        if (window.ShowDialog() != true) return;
        lastChoice = choices.FirstOrDefault(c => c.Provider == window.CreatedIn && c.Calendar.Id == window.Created?.CalendarId);
        status.Text = window.Created?.IsMeeting == true ? "Riunione inviata" : "Evento salvato";
        _ = LoadAsync();
    }

    // ---------------------------------------------------------------- modifica, spostamento, eliminazione

    /// <summary>
    /// Per un evento ricorrente, come Outlook: solo questo evento (false) o tutta la serie (true); null se si annulla.
    /// Gli eventi singoli non chiedono nulla.
    /// </summary>
    bool? AskSeries(CalendarEvent e, string verb)
    {
        if (!e.IsRecurring || e.SeriesMasterId.Length == 0) return false;
        return Dialog.Ask(Window.GetWindow(this), $"{verb} l'evento ricorrente?", $"\"{e.Subject}\" si ripete. {verb} solo questo evento o tutta la serie?",
                          "Solo questo evento", "Tutta la serie") switch
        {
            Choice.Primary => false,
            Choice.Secondary => true,
            _ => null,
        };
    }

    /// <summary>Modifica (doppio clic o dai dettagli): la finestra dell'evento con i suoi campi; poi il calendario si rilegge.</summary>
    public void EditEvent(IMailProvider p, CalendarEvent e)
    {
        if (!CanEdit(p, e) || AskSeries(e, "Modificare") is not { } series) return;
        var calendar = data.GetValueOrDefault(p)?.Calendars.FirstOrDefault(c => c.Id == e.CalendarId) ?? new CalendarInfo(e.CalendarId, "Calendario", "#0F6CBD", false);
        var window = EventWindow.ForEdit(new EventWindow.Choice(p, calendar), e, series);
        window.Owner = Window.GetWindow(this);
#if DEBUG
        if (Environment.GetCommandLineArgs().Contains("--modifica-evento")) window.Loaded += (_, _) => window.ChangeSample();
#endif
        Log.Write($"calendario: modifica di {e.Id}{(series ? ", tutta la serie" : "")}");
        if (window.ShowDialog() != true) return;
        status.Text = e.Attendees.Count > 0 || window.Created?.IsMeeting == true ? "Aggiornamento inviato" : "Evento salvato";
        _ = LoadAsync();
    }

    /// <summary>
    /// Trascinato a un'altra ora: solo inizio e fine cambiano (PATCH dei soli orari). Di un evento ricorrente si sposta solo
    /// quell'occorrenza, come in Outlook; per una riunione si chiede prima, perché i partecipanti ricevono l'aggiornamento.
    /// </summary>
    async Task MoveEventAsync(IMailProvider p, CalendarEvent e, DateTime start)
    {
        if (start == e.Start || !CanEdit(p, e)) return;
        var owner = Window.GetWindow(this);
        var moved = Calendars.Moved(e, start, start + (e.End - e.Start));
        if (e.Attendees.Count > 0 && Dialog.Ask(owner, "Spostare la riunione?",
                $"\"{e.Subject}\" va {moved.Start.ToString("dddd d MMMM, HH:mm", Italian)} - {moved.End:HH:mm}. I partecipanti ricevono l'aggiornamento.", "Sposta e invia") != Choice.Primary)
            return;
        try
        {
            await p.UpdateEventAsync(e, moved, series: false);
            status.Text = "Evento spostato";
            _ = LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Error("evento spostato", ex);
            Dialog.Show(owner, "Evento non spostato", ex.Message);
        }
    }

    /// <summary>
    /// Elimina (evento senza partecipanti) o Annulla riunione (con partecipanti: la nota arriva a tutti, la riunione esce dai
    /// loro calendari), di un'occorrenza o di tutta la serie.
    /// </summary>
    /// <summary>
    /// Che cosa chiede Annulla riunione. Per Microsoft la nota per i partecipanti (Graph: cancel con Comment). Google Calendar
    /// non ha una nota nell'annullamento (GmailProvider.CancelEventAsync: DELETE con sendUpdates=all): prima la finestra la
    /// chiedeva lo stesso e prometteva che i partecipanti l'avrebbero ricevuta, poi la nota finiva solo nel registro (audit,
    /// 1-06). Ora per Google una conferma senza casella, che lo dice.
    /// </summary>
    internal static (bool AsksNote, string Text) CancelPrompt(AccountKind kind, string subject, int attendees)
    {
        var leaves = $"\"{subject}\" esce dai calendari dei {attendees} partecipanti";
        return kind == AccountKind.Gmail
            ? (false, $"{leaves}, che ricevono da Google l'avviso che è annullata. Google Calendar non permette di aggiungere una nota all'annullamento: se serve, scrivi loro un messaggio.")
            : (true, $"{leaves}, che ricevono l'annullamento con questa nota.");
    }

    async Task RemoveEventAsync(IMailProvider p, CalendarEvent e)
    {
        bool meeting = e.Attendees.Count > 0;
        if (!CanEdit(p, e) || AskSeries(e, meeting ? "Annullare" : "Eliminare") is not { } series) return;
        var owner = Window.GetWindow(this);
        string comment = "";
        if (meeting)
        {
            var (asksNote, text) = CancelPrompt(p.Account.Kind, e.Subject, e.Attendees.Count);
            if (asksNote)
            {
                var note = new NameWindow("Annulla riunione", "", "Invia l'annullamento", text, "Nota per i partecipanti (facoltativa)", optional: true) { Owner = owner };
                if (note.ShowDialog() != true) return;
                comment = note.Value;
            }
            else if (Dialog.Ask(owner, "Annullare la riunione?", text, "Invia l'annullamento") != Choice.Primary) return;
        }
        else if (Dialog.Ask(owner, "Eliminare l'evento?", $"\"{e.Subject}\", {Calendars.When(e)}" + (series ? ", con tutta la serie." : "."), "Elimina") != Choice.Primary)
            return;
        try
        {
            if (meeting) await p.CancelEventAsync(e, comment, series);
            else await p.DeleteEventAsync(e, series);
            status.Text = meeting ? "Riunione annullata" : "Evento eliminato";
            ShowDetails(null);
            _ = LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Error(meeting ? "annullamento della riunione" : "eliminazione dell'evento", ex);
            Dialog.Show(owner, meeting ? "Riunione non annullata" : "Evento non eliminato", ex.Message);
        }
    }

#if DEBUG
    /// <summary>Prove (--sposta-evento): il primo evento proprio senza partecipanti della settimana spostato un'ora dopo, come trascinato.</summary>
    internal async Task TestMoveAsync()
    {
        var (from, to) = Range();
        if (Visible(from, to).FirstOrDefault(x => !x.Event.AllDay && !x.Event.IsRecurring && x.Event.Attendees.Count == 0 && CanEdit(x.Provider, x.Event)) is { Provider: { } p, Event: { } e })
            await MoveEventAsync(p, e, e.Start.AddHours(1));
    }

    /// <summary>Prove (--modifica-evento, --annulla-evento): la riunione organizzata da sé, in modifica o da annullare.</summary>
    internal void TestOpen(bool cancel)
    {
        var (from, to) = Range();
        if (Visible(from, to).FirstOrDefault(x => x.Event.Attendees.Count > 0 && CanEdit(x.Provider, x.Event)) is not { Provider: { } p, Event: { } e }) return;
        ShowDetails((p, e));
        if (cancel) _ = RemoveEventAsync(p, e);
        else EditEvent(p, e);
    }
#endif

    /// <summary>Accetta, Provvisorio o Rifiuta dai dettagli di un evento a cui si è invitati, con la risposta all'organizzatore.</summary>
    async Task RespondAsync(IMailProvider p, CalendarEvent e, InviteAnswer answer)
    {
        try
        {
            await p.RespondToEventAsync(e, answer, "", send: true);
            status.Text = answer switch { InviteAnswer.Accept => "Accettato", InviteAnswer.Decline => "Rifiutato", _ => "Risposta provvisoria inviata" };
            _ = LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Error("risposta dal calendario", ex);
            Dialog.Show(Window.GetWindow(this), "Risposta non inviata", ex.Message);
        }
    }

    // ---------------------------------------------------------------- dettagli

    void ShowDetails((IMailProvider Provider, CalendarEvent Event)? chosen)
    {
        selected = chosen;
        if (chosen is not { } pick)
        {
            details.Visibility = Visibility.Collapsed;
            return;
        }
        var (p, e) = pick;
        var d = data.GetValueOrDefault(p);
        var calendar = d?.Calendars.FirstOrDefault(c => c.Id == e.CalendarId);
        var panel = new StackPanel { Margin = new Thickness(18, 12, 18, 18) };
        var close = new Button { Content = new TextBlock { Text = "", FontFamily = Icons, FontSize = 12 }, HorizontalAlignment = HorizontalAlignment.Right,
                                 Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 6, 8, 6) };
        AutomationProperties.SetName(close, "Chiudi i dettagli");
        close.Click += (_, _) => ShowDetails(null);
        panel.Children.Add(close);
        panel.Children.Add(new TextBlock { Text = (e.IsCancelled ? "Annullato: " : "") + (e.Subject.Length > 0 ? e.Subject : "(nessun titolo)"), FontSize = 20,
                                           FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var where = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        if (calendar != null)
            where.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                                            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(calendar.Color)) });
        where.Children.Add(Note($"{calendar?.Name ?? "Calendario"} · {p.Account.Email}"));
        panel.Children.Add(where);
        panel.Children.Add(Line("", Calendars.When(e) + (e.IsRecurring ? "\nEvento ricorrente" : "")));
        if (e.Location.Length > 0) panel.Children.Add(Line("", e.Location));
        if (e.ReminderOn) panel.Children.Add(Line("", e.ReminderMinutes == 0 ? "Promemoria all'inizio" : $"Promemoria {e.ReminderMinutes} minuti prima"));
        if (e.Response is { Length: > 0 } and not ("organizer" or "none") && Calendars.ResponseText(e.Response) is { Length: > 0 } answer)
            panel.Children.Add(Line("", "La tua risposta: " + answer));
        // Invitati: Accetta, Provvisorio e Rifiuta anche da qui, come nel nuovo Outlook (con la risposta all'organizzatore).
        if (!e.IsCancelled && e.Organizer != null && e.Response is "notResponded" or "tentativelyAccepted" or "accepted" or "declined")
        {
            var answers = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
            foreach (var (value, text) in new[] { (InviteAnswer.Accept, "Accetta"), (InviteAnswer.Tentative, "Provvisorio"), (InviteAnswer.Decline, "Rifiuta") })
            {
                var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6), MinWidth = 88 };
                AutomationProperties.SetName(button, text);
                button.Click += async (_, _) => await RespondAsync(p, e, value);
                answers.Children.Add(button);
            }
            panel.Children.Add(answers);
        }
        // Eventi propri: Modifica, ed Elimina o, per le riunioni, Annulla riunione, come nel nuovo Outlook.
        if (CanEdit(p, e))
        {
            var own = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
            var edit = new Button { Content = "Modifica", Margin = new Thickness(0, 0, 6, 6), MinWidth = 88 };
            AutomationProperties.SetName(edit, "Modifica l'evento");
            edit.Click += (_, _) => EditEvent(p, e);
            own.Children.Add(edit);
            var remove = new Button { Content = e.Attendees.Count > 0 ? "Annulla riunione" : "Elimina", Margin = new Thickness(0, 0, 6, 6), MinWidth = 88 };
            AutomationProperties.SetName(remove, e.Attendees.Count > 0 ? "Annulla la riunione" : "Elimina l'evento");
            remove.Click += async (_, _) => await RemoveEventAsync(p, e);
            own.Children.Add(remove);
            panel.Children.Add(own);
        }
        if (e.JoinUrl != null)
        {
            var join = new Button { Content = "Partecipa", Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 120, ToolTip = e.JoinUrl };
            join.SetResourceReference(StyleProperty, "AccentButtonStyle");
            AutomationProperties.SetName(join, "Partecipa alla riunione");
            join.Click += (_, _) => Join(e.JoinUrl);
            panel.Children.Add(join);
        }
        if (e.Organizer is { } organizer && e.Response != "organizer")
            panel.Children.Add(Line("", "Organizzatore: " + organizer));
        if (e.Attendees.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = $"Partecipanti ({e.Attendees.Count})", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var a in e.Attendees.Take(30))
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var state = Note(Calendars.ResponseText(a.Response) is { Length: > 0 } r ? r : "In attesa");
                DockPanel.SetDock(state, Dock.Right);
                row.Children.Add(state);
                row.Children.Add(new TextBlock { Text = a.Person.Display + (a.Optional ? " (facoltativo)" : ""), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = a.Person.Email });
                panel.Children.Add(row);
            }
            if (e.Attendees.Count > 30) panel.Children.Add(Note($"e altri {e.Attendees.Count - 30}"));
        }
        if (e.Preview.Length > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Note", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            panel.Children.Add(new TextBlock { Text = e.Preview.Length > 600 ? e.Preview[..600] + "…" : e.Preview, TextWrapping = TextWrapping.Wrap });
        }
        details.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        details.Visibility = Visibility.Visible;
        Log.Write($"calendario: dettagli di {Log.Private(e.Subject)} ({p.Account.Email})");
    }

    static UIElement Line(string glyph, string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var icon = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 14, Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return row;
    }

    /// <summary>I siti delle riunioni online: il loro link si apre senza domande.</summary>
    static readonly string[] MeetingHosts = ["teams.microsoft.com", "teams.live.com", "zoom.us", "meet.google.com", "webex.com", "gotomeeting.com", "whereby.com"];

    /// <summary>
    /// "Partecipa": solo https, e senza domande solo verso i siti delle riunioni online (Teams, Zoom, Meet, Webex); gli altri
    /// e gli indirizzi con caratteri che imitano un sito noto chiedono conferma con l'indirizzo in vista, come i link delle email.
    /// </summary>
    void Join(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        var host = uri.IdnHost.ToLowerInvariant();
        bool known = MeetingHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
        var warning = Safety.LinkWarning("", uri);
        if ((!known || warning != null) &&
            Dialog.Ask(Window.GetWindow(this), "Aprire il link della riunione?", (warning ?? "Il link non porta a Teams, Zoom, Meet o Webex.") + "\n\nIndirizzo:\n" + uri.AbsoluteUri,
                       "Apri") != Choice.Primary)
            return;
        Log.Write($"calendario: apro la riunione su {uri.Host}");
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("apertura della riunione", ex); }
    }
}

/// <summary>
/// La colonna di un giorno: ogni evento dall'ora di inizio a quella di fine (frazioni dell'altezza), nella sua colonna fra
/// quelli che si sovrappongono (Calendars.Day). Alto almeno 20 pixel, perché il titolo si legga.
/// </summary>
public sealed class EventPanel : Panel
{
    static readonly DependencyProperty PlaceProperty = DependencyProperty.RegisterAttached("Place", typeof(Calendars.Placed), typeof(EventPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static void SetPlace(UIElement element, Calendars.Placed place) => element.SetValue(PlaceProperty, place);

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(double.IsInfinity(available.Width) ? 200 : available.Width, double.IsInfinity(available.Height) ? 1000 : available.Height));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (UIElement child in InternalChildren)
        {
            if (child.GetValue(PlaceProperty) is not Calendars.Placed p) continue;
            double width = final.Width / p.Columns;
            double top = p.Top * final.Height, height = Math.Max(20, (p.Bottom - p.Top) * final.Height);
            child.Arrange(new Rect(p.Column * width, top, Math.Max(0, width - 1), height));
        }
        return final;
    }
}
