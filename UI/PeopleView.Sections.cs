using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Persone divise per account (1 ottobre 2026, agente contatti-II, richiesta dell'autore: "ora i contatti sono un'unica lista, non divisa
/// per account"). L'elenco resta UN SOLO ListBox piatto, virtualizzato e a pixel come prima (<see cref="SetUpList"/>), con righe di tre
/// tipi: l'intestazione di una sezione (<see cref="Header"/>: icona o pallino dell'account, nome, tipo, quante persone), una nota
/// sotto l'intestazione (<see cref="SectionNote"/>: nessun contatto, caricamento, errore, o il pulsante di consenso) e la persona
/// (<see cref="Row"/>). Il raggruppamento di WPF (CollectionViewSource con IsVirtualizingWhenGrouping) è più fragile: la MessageList ha
/// dovuto rinunciarci per i salti di posizione. Le intestazioni e le note non si selezionano (Focusable falso, e le frecce le saltano
/// con <see cref="ContactNear"/>); lo stesso contatto in due account sta in tutte e due le sezioni, senza unire i doppioni.
/// </summary>
public sealed partial class PeopleView
{
    readonly ComboBox filter = new() { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
    /// <summary>Quale origine si vede ("" = Tutti i contatti): resta per la sessione, finché la vista è la stessa.</summary>
    string filterKey = "";
    List<FilterOption> filterOptions = [];
    bool refreshingFilter;
    Row? lastSelected;

    /// <summary>Da dove vengono i contatti di una sezione: Missiva (Provider null) o un account che ha i contatti.</summary>
    internal sealed record ContactOrigin(string Key, string Title, string Kind, string Extra, IMailProvider? Provider);

    /// <summary>L'intestazione di una sezione. Il nome per UI Automation è <see cref="ToString"/>.</summary>
    internal sealed record Header(string Title, string Kind, string Count, string Tip, bool First, Brush? Dot, bool IsMissiva)
    {
        /// <summary>La seconda riga: il tipo, e per Google la sola lettura.</summary>
        public string Detail { get; init; } = Kind;
        public Thickness Spacing => new(4, First ? 2 : 16, 4, 2);
        public Visibility DotVisibility => Dot != null ? Visibility.Visible : Visibility.Collapsed;
        public string Glyph => IsMissiva ? "" : "";
        public override string ToString() => Count.Length > 0 ? $"{Title}, {Detail}, {Count}" : $"{Title}, {Detail}";
    }

    /// <summary>Sotto l'intestazione, per una sezione senza contatti o che non si legge; con Allow il pulsante "Consenti l'accesso ai contatti".</summary>
    internal sealed record SectionNote(string Text, IMailProvider? Allow, bool Write = false)
    {
        /// <summary>Il pulsante: l'accesso ai contatti (lettura, o Outlook) oppure, per Google già letto, la modifica.</summary>
        public string ButtonText => Write ? T("Consenti la modifica dei contatti") : T("Consenti l'accesso ai contatti");
        public string ButtonName => Allow == null ? "" : Write ? T("Consenti la modifica dei contatti di {0}", Allow.Account.Email) : T("Consenti l'accesso ai contatti di {0}", Allow.Account.Email);
        public override string ToString() => Text;
    }

    /// <summary>Una voce del filtro per account: "Tutti i contatti" (Key vuota), Missiva e gli account.</summary>
    internal sealed record FilterOption(string Key, string Title, string Detail, Brush? Dot, bool IsMissiva)
    {
        public Visibility DotVisibility => Dot != null ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>La persona per Missiva, le due persone per Tutti i contatti, niente per gli account (hanno il pallino).</summary>
        public string Glyph => IsMissiva ? "" : Key.Length == 0 ? "" : "";
        public override string ToString() => Detail.Length > 0 ? $"{Title}, {Detail}" : Title;
    }

    /// <summary>Le origini in ordine: prima Missiva, poi gli account che hanno i contatti come in Settings.Accounts (la barra laterale).</summary>
    internal static List<ContactOrigin> Origins(IEnumerable<IMailProvider> providers)
    {
        static int Position(IMailProvider p)
        {
            int i = Store.Settings.Accounts.FindIndex(a => a.Id == p.Account.Id);
            return i < 0 ? int.MaxValue : i;
        }
        var list = new List<ContactOrigin> { new("missiva", T("Contatti di Missiva"), "Missiva", T("su questo PC"), null) };
        list.AddRange(providers.Where(p => p.HasContacts).OrderBy(Position)
            .Select(p => new ContactOrigin(p.Account.Id, p.Account.Email, Origin(p), p.ContactsReadOnly ? T("sola lettura") : "", p)));
        return list;
    }

    static Brush AccountDot(IMailProvider p)
    {
        var brush = new SolidColorBrush(Avatars.AccountColor(p.Account));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Le righe dell'elenco: per ogni origine l'intestazione, la nota se serve e le sue persone in ordine di nome. Con la ricerca solo le
    /// sezioni che hanno risultati (e il numero è "2 su 4"); con il filtro solo quella origine. <paramref name="remote"/> senza la chiave:
    /// ancora in caricamento; con valore null: manca il consenso.
    /// </summary>
    internal static List<object> BuildEntries(IReadOnlyList<ContactOrigin> origins, IReadOnlyList<Contact> local, Dictionary<IMailProvider, List<Contact>?> remote,
                                              Dictionary<IMailProvider, string> errors, string words, string filterKey)
    {
        var entries = new List<object>();
        bool searching = words.Length > 0;
        foreach (var origin in origins)
        {
            if (filterKey.Length > 0 && filterKey != origin.Key) continue;
            var provider = origin.Provider;
            IReadOnlyList<Contact>? all = provider == null ? local : remote.TryGetValue(provider, out var got) ? got : null;
            var found = all?.Where(c => c.Matches(words)).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (searching && (found == null || found.Count == 0)) continue;
            string count = all == null ? "" : searching && found!.Count != all.Count ? T("{0} su {1}", found.Count, all.Count) : found!.Count.ToString();
            var detail = origin.Extra.Length > 0 ? origin.Kind + " · " + origin.Extra : origin.Kind;
            entries.Add(new Header(origin.Title, origin.Kind, count, origin.Title + ", " + detail, entries.Count == 0, provider == null ? null : AccountDot(provider), provider == null) { Detail = detail });
            if (!searching)
            {
                if (provider != null && errors.TryGetValue(provider, out var error)) entries.Add(new SectionNote(T("Non disponibili ({0})", error), null));
                else if (provider != null && !remote.ContainsKey(provider)) entries.Add(new SectionNote(T("Caricamento…"), null));
                else if (all == null) entries.Add(new SectionNote(provider?.Account.Kind == AccountKind.Gmail ? T("Missiva non ha ancora il permesso di leggere i contatti di Google.")
                                                                  : T("Consenti l'accesso ai contatti di Outlook: potrai vederli e modificarli in Missiva."), provider));
                else
                {
                    // Google letto ma non scrivibile: la riga con il pulsante che chiede di poter creare, cambiare ed eliminare.
                    if (provider is { ContactsReadOnly: true, Account.Kind: AccountKind.Gmail })
                        entries.Add(new SectionNote(T("Missiva potrà creare, cambiare ed eliminare i tuoi contatti di Google."), provider, true));
                    if (all.Count == 0) entries.Add(new SectionNote(provider == null ? T("Nessun contatto: Nuovo contatto o Importa ne aggiungono.") : T("Nessun contatto."), null));
                }
            }
            if (found != null) entries.AddRange(found.Select(c => new Row(c, provider)));
        }
        return entries;
    }

    /// <summary>Dall'indice dato nella direzione data (1 o -1) la prima persona, saltando intestazioni e note; -1 se non ce n'è.</summary>
    internal static int ContactNear(System.Collections.IList items, int from, int direction)
    {
        for (int i = from + direction; i >= 0 && i < items.Count; i += direction)
            if (items[i] is Row) return i;
        return -1;
    }

    // ---------------------------------------------------------------- filtro

    List<FilterOption> FilterOptionsOf(IReadOnlyList<ContactOrigin> origins)
    {
        var options = new List<FilterOption> { new("", T("Tutti i contatti"), "", null, false) };
        options.AddRange(origins.Select(o => new FilterOption(o.Key, o.Title, o.Kind, o.Provider == null ? null : AccountDot(o.Provider), o.Provider == null)));
        return options;
    }

    /// <summary>Rimette le voci del filtro solo se gli account sono cambiati, e la scelta com'era (un account sparito torna a Tutti i contatti).</summary>
    void RefreshFilter(IReadOnlyList<ContactOrigin> origins)
    {
        var options = FilterOptionsOf(origins);
        refreshingFilter = true;
        try
        {
            if (!options.Select(o => o.Key).SequenceEqual(filterOptions.Select(o => o.Key)))
            {
                filterOptions = options;
                filter.ItemsSource = filterOptions;
            }
            if (!filterOptions.Any(o => o.Key == filterKey)) filterKey = "";
            filter.SelectedItem = filterOptions.First(o => o.Key == filterKey);
        }
        finally
        {
            refreshingFilter = false;
        }
    }

    // ---------------------------------------------------------------- modelli e stili

    /// <summary>
    /// Modelli e stili delle tre righe, nel ListBox: una persona ha lo stile <paramref name="personStyle"/> (NavItem nell'app), intestazioni e note
    /// uno proprio senza selezione, senza fuoco e senza evidenziazione al passaggio del mouse. Anche per le prove (ProvaBozze.Persone3.cs).
    /// </summary>
    internal static void SetUpEntries(ListBox list, Style personStyle)
    {
        list.Resources[new DataTemplateKey(typeof(Row))] = RowTemplate();
        var header = HeaderTemplate();
        list.Resources[header.DataTemplateKey!] = header;
        var note = NoteTemplate();
        list.Resources[note.DataTemplateKey!] = note;
        list.ItemContainerStyleSelector = new EntryStyles(personStyle, PlainStyle());
    }

    sealed class EntryStyles(Style person, Style plain) : StyleSelector
    {
        public override Style SelectStyle(object item, DependencyObject container) => item is Row ? person : plain;
    }

    static Style PlainStyle() => (Style)System.Windows.Markup.XamlReader.Parse(
        "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ListBoxItem\">" +
        "<Setter Property=\"Focusable\" Value=\"False\"/><Setter Property=\"IsTabStop\" Value=\"False\"/><Setter Property=\"Padding\" Value=\"0\"/>" +
        "<Setter Property=\"HorizontalContentAlignment\" Value=\"Stretch\"/><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"ListBoxItem\">" +
        "<ContentPresenter/></ControlTemplate></Setter.Value></Setter></Style>");

    static FrameworkElementFactory Text(string binding, double size, FontWeight weight, bool secondary)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(binding));
        text.SetValue(TextBlock.FontSizeProperty, size);
        text.SetValue(TextBlock.FontWeightProperty, weight);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        if (secondary) text.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return text;
    }

    /// <summary>Il segno dell'origine: il pallino del colore dell'account (come nelle viste di tutti gli account) o la persona di Missiva.</summary>
    static FrameworkElementFactory Mark()
    {
        var slot = new FrameworkElementFactory(typeof(Grid));
        slot.SetValue(WidthProperty, 22.0);
        slot.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var dot = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
        dot.SetValue(WidthProperty, 10.0);
        dot.SetValue(HeightProperty, 10.0);
        dot.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        dot.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(Header.Dot)));
        dot.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(Header.DotVisibility)));
        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Header.Glyph)));
        glyph.SetValue(TextBlock.FontFamilyProperty, Icons);
        glyph.SetValue(TextBlock.FontSizeProperty, 14.0);
        glyph.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        slot.AppendChild(dot);
        slot.AppendChild(glyph);
        return slot;
    }

    static DataTemplate HeaderTemplate()
    {
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetBinding(MarginProperty, new System.Windows.Data.Binding(nameof(Header.Spacing)));
        line.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(Header.Tip)));
        line.SetValue(Border.BackgroundProperty, Brushes.Transparent); // il suggerimento compare anche fra una scritta e l'altra
        line.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        line.SetValue(Border.PaddingProperty, new Thickness(0, 0, 0, 6));
        line.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var dock = new FrameworkElementFactory(typeof(DockPanel));
        var mark = Mark();
        mark.SetValue(DockPanel.DockProperty, Dock.Left);
        var count = Text(nameof(Header.Count), 13, FontWeights.SemiBold, false);
        count.SetValue(DockPanel.DockProperty, Dock.Right);
        count.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        count.SetValue(MarginProperty, new Thickness(10, 0, 18, 0)); // fuori dalla barra di scorrimento, che si posa sopra l'elenco
        var lines = new FrameworkElementFactory(typeof(StackPanel));
        lines.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        lines.AppendChild(Text(nameof(Header.Title), 13, FontWeights.SemiBold, false));
        lines.AppendChild(Text(nameof(Header.Detail), 12, FontWeights.Normal, true));
        dock.AppendChild(mark);
        dock.AppendChild(count);
        dock.AppendChild(lines);
        line.AppendChild(dock);
        return new DataTemplate(typeof(Header)) { VisualTree = line };
    }

    static DataTemplate NoteTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(MarginProperty, new Thickness(26, 2, 4, 6));
        var text = Text(nameof(SectionNote.Text), 12, FontWeights.Normal, true);
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.None);
        panel.AppendChild(text);
        var allow = new FrameworkElementFactory(typeof(Button));
        allow.SetValue(MarginProperty, new Thickness(0, 6, 0, 0));
        allow.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        allow.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding(nameof(SectionNote.ButtonText)));
        allow.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(SectionNote.Allow)) { Converter = new NullToCollapsed() });
        allow.SetBinding(AutomationProperties.NameProperty, new System.Windows.Data.Binding(nameof(SectionNote.ButtonName)));
        allow.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is FrameworkElement { DataContext: SectionNote { Allow: { } p } note } element && FindOwner(element) is { } view) view.AllowFor(p, note.Write);
        }));
        panel.AppendChild(allow);
        return new DataTemplate(typeof(SectionNote)) { VisualTree = panel };
    }

    static PeopleView? FindOwner(DependencyObject d)
    {
        while (d != null && d is not PeopleView) d = VisualTreeHelper.GetParent(d) is { } parent ? parent : LogicalTreeHelper.GetParent(d);
        return d as PeopleView;
    }

    /// <summary>
    /// "Consenti l'accesso ai contatti" (Outlook, Google in lettura) o "Consenti la modifica dei contatti" (Google, write) sotto l'intestazione o
    /// nella scheda: la finestra di Microsoft o di Google solo al clic (AllowRequested, AllowWriteRequested), poi i contatti si rileggono.
    /// </summary>
    async void AllowFor(IMailProvider p, bool write = false)
    {
        var request = write ? AllowWriteRequested : AllowRequested;
        if (request != null && await request(p)) _ = LoadAsync();
    }

    sealed class NullToCollapsed : System.Windows.Data.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value == null ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }


    /// <summary>Il filtro per account (una tendina sotto la ricerca) e le righe dell'elenco; chiamato dal costruttore con il pannello sinistro.</summary>
    void SetUpSections(DockPanel left)
    {
        AutomationProperties.SetName(filter, T("Mostra i contatti di"));
        filter.ToolTip = T("Tutti i contatti, o solo quelli di Missiva o di un account");
        var item = new FrameworkElementFactory(typeof(DockPanel));
        var mark = Mark();
        mark.SetValue(DockPanel.DockProperty, Dock.Left);
        var kind = Text(nameof(FilterOption.Detail), 12, FontWeights.Normal, true);
        kind.SetValue(DockPanel.DockProperty, Dock.Right);
        kind.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        kind.SetValue(MarginProperty, new Thickness(10, 0, 0, 0));
        var title = Text(nameof(FilterOption.Title), 14, FontWeights.Normal, false);
        title.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        item.AppendChild(mark);
        item.AppendChild(kind);
        item.AppendChild(title);
        filter.ItemTemplate = new DataTemplate(typeof(FilterOption)) { VisualTree = item };
        filter.SelectionChanged += (_, _) =>
        {
            if (refreshingFilter || filter.SelectedItem is not FilterOption chosen || chosen.Key == filterKey) return;
            filterKey = chosen.Key;
            Fill();
        };
        DockPanel.SetDock(filter, Dock.Top);
        left.Children.Add(filter);
        SetUpEntries(list, (Style)FindResource("NavItem"));
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); // un nome lungo finisce con i puntini invece di allargare l'elenco
        list.PreviewKeyDown += OnListKeyDown;
    }

    /// <summary>
    /// Frecce, Pagina su e giù, Inizio e Fine sulle sole persone: le intestazioni e le note non si scelgono mai (WPF, con le righe non create,
    /// non saprebbe saltarle). La riga scelta si porta in vista e prende il fuoco, come fa l'elenco da solo.
    /// </summary>
    void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || list.Items.Count == 0) return;
        int current = list.SelectedIndex, last = list.Items.Count - 1;
        int PageTarget(int direction)
        {
            int to = Math.Clamp((current < 0 ? 0 : current) + 8 * direction, 0, last);
            if (list.Items[to] is Row) return to;
            int near = ContactNear(list.Items, to, direction);
            return near >= 0 ? near : ContactNear(list.Items, to, -direction);
        }
        int target;
        switch (e.Key)
        {
            case Key.Down: target = ContactNear(list.Items, current, 1); break;
            case Key.Up: target = current < 0 ? ContactNear(list.Items, -1, 1) : ContactNear(list.Items, current, -1); break;
            case Key.Home: target = ContactNear(list.Items, -1, 1); break;
            case Key.End: target = ContactNear(list.Items, last + 1, -1); break;
            case Key.PageDown: target = PageTarget(1); break;
            case Key.PageUp: target = PageTarget(-1); break;
            default: return;
        }
        e.Handled = true;
        if (target < 0 || target == current) return;
        list.SelectedIndex = target;
        list.ScrollIntoView(list.Items[target]);
        list.UpdateLayout();
        (list.ItemContainerGenerator.ContainerFromIndex(target) as ListBoxItem)?.Focus();
    }

    /// <summary>
    /// La scelta cambia: una persona mostra la scheda; un'intestazione o una nota (per esempio con Select di UI Automation) non si scelgono, resta la persona di prima.
    /// </summary>
    void OnSelectionChanged()
    {
        if (list.SelectedItem != null && list.SelectedItem is not Row)
        {
            list.SelectedItem = lastSelected;
            return;
        }
        lastSelected = list.SelectedItem as Row;
        ShowCard(lastSelected);
    }

    /// <summary>Doppio clic: solo se è su una persona, non su un'intestazione con una persona scelta altrove.</summary>
    void OnListDoubleClick(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(list, source) is ListBoxItem { DataContext: Row row } && row.Provider is not { ContactsReadOnly: true })
            Edit(row); // i contatti di Google si cambiano solo da Modifica
    }

    /// <summary>La persona numero N (da 0) fra le sole persone, senza contare le intestazioni (prove: --persona N).</summary>
    int IndexOfContact(int n)
    {
        int seen = -1;
        for (int i = 0; i < list.Items.Count; i++)
            if (list.Items[i] is Row && ++seen == n) return i;
        return -1;
    }
}
