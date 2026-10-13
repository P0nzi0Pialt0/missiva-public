using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// I filtri della ricerca, come i Filtri del nuovo Outlook: il pulsante accanto a "Dove cercare" apre un pannello con Da,
/// A, Oggetto, Contiene, Dal, Al, Con allegati, Non letti e Contrassegnati. I filtri scelti (o scritti a mano, "da:rossi")
/// diventano pastiglie nella casella, ognuna con la x per toglierla; la casella tiene le parole libere. La ricerca parte
/// come testo con le parole chiave (SearchTerms), che ogni provider traduce e che resta così nelle ricerche recenti; vale
/// anche in tutti gli account.
/// </summary>
public partial class MainWindow
{
    /// <summary>I filtri della ricerca (senza le parole libere, che sono nella casella).</summary>
    SearchTerms searchFilters = new();
    readonly TextBox filterFrom = FilterBox("Da"), filterTo = FilterBox("A"), filterSubject = FilterBox("Oggetto"), filterWords = FilterBox("Contiene");
    readonly DatePicker filterSince = new() { Width = 190 }, filterUntil = new() { Width = 190 };
    readonly CheckBox filterAttachments = FilterCheck("Con allegati"), filterUnread = FilterCheck("Non letti"), filterFlagged = FilterCheck("Contrassegnati");

    static TextBox FilterBox(string name)
    {
        var box = new TextBox { MinWidth = 260 };
        AutomationProperties.SetName(box, name);
        return box;
    }

    static CheckBox FilterCheck(string text)
    {
        var box = new CheckBox { Content = text, Margin = new Thickness(0, 0, 16, 0) };
        AutomationProperties.SetName(box, text);
        return box;
    }

    void OnSearchFilters(object sender, RoutedEventArgs e)
    {
        // Il clic sul pulsante chiude già il pannello (non è più la finestra attiva): non lo si riapre subito.
        if (filtersWindow != null) CloseFilters("pulsante Filtri");
        else if (DateTime.UtcNow - filtersClosed > TimeSpan.FromMilliseconds(400)) ShowFilters();
    }

    /// <summary>
    /// Il pannello dei filtri, una piccola finestra senza bordi sotto la casella (come un menu a comparsa, ma con caselle in
    /// cui scrivere: un Popup di WPF con caselle perdeva il fuoco): si chiude con Esc, con Cerca, facendo clic altrove o
    /// spostando la finestra principale.
    /// </summary>
    void ShowFilters()
    {
        CloseSuggestions();
        if (filtersWindow != null)
        {
            filtersWindow.Activate();
            return;
        }
        filtersPanel ??= BuildFilters();
        if (filtersHooked == false)
        {
            filtersHooked = true;
            LocationChanged += (_, _) => CloseFilters("finestra spostata");
            PreviewMouseDown += (_, _) => { if (!SearchFiltersButton.IsMouseOver) CloseFilters("clic nella finestra"); };
            StateChanged += (_, _) => CloseFilters("finestra ridotta o ingrandita");
        }
        var typed = SearchTerms.Parse(SearchBox.Text);
        var f = Merge(searchFilters, typed);
        filterFrom.Text = f.From;
        filterTo.Text = f.To;
        filterSubject.Text = f.Subject;
        filterWords.Text = typed.Text;
        filterSince.SelectedDate = f.Since;
        filterUntil.SelectedDate = f.Until;
        filterAttachments.IsChecked = f.HasAttachments;
        filterUnread.IsChecked = f.Unread;
        filterFlagged.IsChecked = f.Flagged;
        // Sotto la casella della ricerca, allineata al suo bordo sinistro (in DIP, anche a 200%).
        var corner = SearchArea.PointToScreen(new Point(0, SearchArea.ActualHeight + 4));
        var toDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var at = toDip.Transform(corner);
        filtersWindow = new Window
        {
            Owner = this, Title = "Filtri", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            AllowsTransparency = true, Background = Brushes.Transparent, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = at.X, Top = at.Y, Content = filtersPanel,
        };
        // Passando a un'altra app si chiude; non subito dopo l'apertura, quando Windows può negare il fuoco a una finestra
        // che non è in primo piano (e WPF manda Deactivated appena la mostra).
        var shown = DateTime.UtcNow;
        filtersWindow.Deactivated += (_, _) =>
        {
            if (DateTime.UtcNow - shown > TimeSpan.FromMilliseconds(600)) CloseFilters("finestra non più attiva");
        };
        filtersWindow.Closed += (_, _) =>
        {
            if (filtersWindow != null) filtersWindow.Content = null; // il pannello torna libero per la prossima volta
            filtersWindow = null;
        };
        filtersWindow.Loaded += (_, _) => filterFrom.Focus();
        filtersWindow.Show();
    }

    Window? filtersWindow;
    Border? filtersPanel;
    bool filtersHooked;
    DateTime filtersClosed;

    void CloseFilters(string why)
    {
        if (filtersWindow is not { } window) return;
        Log.Write("ricerca: pannello dei filtri chiuso (" + why + ")");
        filtersWindow = null;
        filtersClosed = DateTime.UtcNow;
        window.Content = null;
        window.Close();
    }

    Border BuildFilters()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Row(string label, FrameworkElement field)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 0) };
            Grid.SetRow(text, grid.RowDefinitions.Count - 1);
            grid.Children.Add(text);
            field.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(field, grid.RowDefinitions.Count - 1);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
        }
        Row("Da", filterFrom);
        Row("A", filterTo);
        Row("Oggetto", filterSubject);
        Row("Contiene", filterWords);
        var dates = new StackPanel { Orientation = Orientation.Horizontal };
        AutomationProperties.SetName(filterSince, "Dal giorno");
        AutomationProperties.SetName(filterUntil, "Al giorno");
        dates.Children.Add(filterSince);
        dates.Children.Add(new TextBlock { Text = "al", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) });
        dates.Children.Add(filterUntil);
        Row("Dal", dates);
        var checks = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        checks.Children.Add(filterAttachments);
        checks.Children.Add(filterUnread);
        checks.Children.Add(filterFlagged);
        Row("", checks);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var clear = new Button { Content = "Cancella filtri", Margin = new Thickness(0, 0, 8, 0), MinWidth = 100 };
        AutomationProperties.SetName(clear, "Cancella filtri");
        clear.Click += (_, _) =>
        {
            foreach (var box in new[] { filterFrom, filterTo, filterSubject }) box.Text = "";
            filterSince.SelectedDate = filterUntil.SelectedDate = null;
            filterAttachments.IsChecked = filterUnread.IsChecked = filterFlagged.IsChecked = false;
        };
        var search = new Button { Content = "Cerca", MinWidth = 100 };
        search.SetResourceReference(StyleProperty, "AccentButtonStyle");
        AutomationProperties.SetName(search, "Cerca con i filtri");
        search.Click += async (_, _) => await ApplyFiltersAsync();
        buttons.Children.Add(clear);
        buttons.Children.Add(search);

        var panel = new StackPanel { Margin = new Thickness(18, 12, 18, 14) };
        panel.Children.Add(new TextBlock { Text = "Filtri", FontSize = 16, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(grid);
        panel.Children.Add(buttons);
        panel.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && e.OriginalSource is not Button) { e.Handled = true; await ApplyFiltersAsync(); }
            else if (e.Key == Key.Escape) { e.Handled = true; CloseFilters("Esc"); SearchBox.Focus(); }
        };
        var border = new Border { Child = panel, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Width = Math.Max(SearchArea.ActualWidth, 540) };
        border.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorDefaultBrush");
        AutomationProperties.SetName(border, "Filtri della ricerca");
        return border;
    }

    /// <summary>Cerca dal pannello: i filtri diventano pastiglie, Contiene le parole della casella.</summary>
    async Task ApplyFiltersAsync()
    {
        var since = filterSince.SelectedDate;
        var until = filterUntil.SelectedDate;
        if (since != null && until != null && until < since) (since, until) = (until, since);
        searchFilters = new SearchTerms
        {
            From = filterFrom.Text.Trim(), To = filterTo.Text.Trim(), Subject = filterSubject.Text.Trim(), Since = since, Until = until,
            HasAttachments = filterAttachments.IsChecked == true, Unread = filterUnread.IsChecked == true, Flagged = filterFlagged.IsChecked == true,
        };
        CloseFilters("Cerca");
        SetSearchText(filterWords.Text.Trim());
        ShowPills();
        Log.Write($"ricerca: filtri {string.Join(", ", searchFilters.Parts().Select(p => p.Key))}");
        await RunSearchAsync();
    }

    /// <summary>La ricerca con le parole della casella e i filtri, dove si cerca adesso (come Invio).</summary>
    async Task RunSearchAsync()
    {
        if (!HasList) return;
        if (calendarShown) ShowCalendar(false);
        if (peopleShown) ShowPeople(false);
        var query = searchFilters.WithText(SearchBox.Text).ToString();
        if (query.Length == 0)
        {
            OnClearSearch(this, new RoutedEventArgs());
            return;
        }
        if (SearchesAccounts) await SearchAccountsAsync(query, _ => query);
        else await SearchAsync(query, searchAll ? null : folder);
    }

    /// <summary>
    /// I filtri scritti a mano nella ricerca (da:rossi, dal:1/3/2026) passano nelle pastiglie, e la casella tiene le parole
    /// libere; restituisce il testo della ricerca con i filtri, quello che vanno ai provider e alle ricerche recenti.
    /// </summary>
    string TakeFilters(string query)
    {
        var parsed = SearchTerms.Parse(query);
        if (parsed.HasFilters)
        {
            searchFilters = Merge(searchFilters, parsed).Filters;
            if (SearchBox.Text.Trim() != parsed.Text) SetSearchText(parsed.Text);
        }
        ShowPills();
        return searchFilters.WithText(parsed.Text).ToString();
    }

    /// <summary>I filtri di prima con quelli nuovi al posto dei loro.</summary>
    static SearchTerms Merge(SearchTerms current, SearchTerms added) => current with
    {
        From = added.From.Length > 0 ? added.From : current.From,
        To = added.To.Length > 0 ? added.To : current.To,
        Subject = added.Subject.Length > 0 ? added.Subject : current.Subject,
        Since = added.Since ?? current.Since,
        Until = added.Until ?? current.Until,
        HasAttachments = current.HasAttachments || added.HasAttachments,
        Unread = current.Unread || added.Unread,
        Flagged = current.Flagged || added.Flagged,
        Text = "",
    };

    /// <summary>Le pastiglie dei filtri nella casella, ognuna con la x che la toglie e rifà la ricerca.</summary>
    void ShowPills()
    {
        SearchPills.Children.Clear();
        foreach (var (key, label, _) in searchFilters.Parts())
        {
            var pill = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 2, 1), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            pill.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis });
            var remove = new Button
            {
                Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 8 },
                Width = 18, Height = 18, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, Margin = new Thickness(4, 0, 0, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "Togli il filtro",
            };
            AutomationProperties.SetName(remove, "Togli il filtro " + label);
            var filterKey = key;
            remove.Click += async (_, _) =>
            {
                searchFilters = searchFilters.Without(filterKey);
                ShowPills();
                if (searching) await RunSearchAsync();
            };
            content.Children.Add(remove);
            pill.Child = content;
            AutomationProperties.SetName(pill, label);
            SearchPills.Children.Add(pill);
        }
        SearchFiltersGlyph.SetResourceReference(TextBlock.ForegroundProperty, searchFilters.HasFilters ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        FitSearchPadding();
    }

    void OnSearchPillsSize(object sender, SizeChangedEventArgs e) => FitSearchPadding();

    /// <summary>Il testo della casella comincia dopo le pastiglie e finisce prima dei pulsanti.</summary>
    void FitSearchPadding()
    {
        double left = SearchPills.Children.Count > 0 ? 32 + SearchPills.ActualWidth + 4 : 34;
        SearchBox.Padding = new Thickness(left, 5, SearchTools.ActualWidth + 6, 5);
        SearchHint.Visibility = SearchBox.Text.Length == 0 && SearchPills.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Chiudendo la ricerca i filtri si tolgono.</summary>
    void ClearFilters()
    {
        if (!searchFilters.HasFilters) return;
        searchFilters = new SearchTerms();
        ShowPills();
    }
}
