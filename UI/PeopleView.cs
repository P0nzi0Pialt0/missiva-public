using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Persone, come nel nuovo Outlook ed eM Client: i contatti di Missiva (per tutti gli account, cifrati su questo PC) e
/// quelli di Outlook degli account Microsoft (me/contacts, dopo il consenso ai contatti chiesto solo dal pulsante), in un
/// elenco con la ricerca e la scheda della persona (indirizzi, telefoni, azienda, ruolo, indirizzo, note), con Nuovo,
/// Modifica, Elimina, Importa (vCard e CSV di Outlook e Gmail) ed Esporta (vCard). Nella prova le scritture sui contatti
/// di Outlook vanno solo nel registro (TestingProvider).
/// </summary>
public sealed partial class PeopleView : UserControl
{
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    readonly Func<IReadOnlyList<IMailProvider>> providers;
    /// <summary>Da dove arrivano i contatti di un account: il provider; nelle prove in Debug quelli finti di --contatti-finti.</summary>
    public Func<IMailProvider, Task<List<Contact>?>> Source { get; set; } = p => p.GetContactsAsync();
    /// <summary>"Consenti l'accesso ai contatti": lo chiede MainWindow (finestra di Microsoft, solo al clic).</summary>
    public Func<IMailProvider, Task<bool>>? AllowRequested { get; set; }
    /// <summary>"Consenti la modifica dei contatti" (account Google senza l'ambito di scrittura): lo chiede MainWindow, la finestra di Google solo al clic.</summary>
    public Func<IMailProvider, Task<bool>>? AllowWriteRequested { get; set; }
    /// <summary>Scrivi a una persona: Nuovo messaggio con l'indirizzo.</summary>
    public Action<string>? WriteRequested { get; set; }

    readonly Dictionary<IMailProvider, List<Contact>?> remote = new();
    readonly Dictionary<IMailProvider, string> errors = new();
    readonly TextBox search = new();
    /// <summary>Il testo guida della ricerca, sopra la casella e sparito appena si scrive (come "Cerca" in alto nella finestra).</summary>
    readonly TextBlock searchHint = new() { Text = T("Cerca nei contatti"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    readonly Border card = new() { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(12, 0, 0, 0) };
    readonly TextBlock status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

    public PeopleView(Func<IReadOnlyList<IMailProvider>> providers)
    {
        this.providers = providers;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetColumnSpan(bar, 2);
        var create = new Button { Content = T("Nuovo contatto"), Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 12, 0) };
        create.SetResourceReference(StyleProperty, "AccentButtonStyle");
        AutomationProperties.SetName(create, T("Nuovo contatto"));
        create.Click += (_, _) => Edit(null);
        bar.Children.Add(create);
        // Nuovo gruppo, come i gruppi di contatti del nuovo Outlook: nel campo A diventa gli indirizzi delle sue persone.
        var group = new Button { Content = T("Nuovo gruppo"), Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 12, 0) };
        AutomationProperties.SetName(group, T("Nuovo gruppo"));
        group.Click += (_, _) => EditGroup(null);
        bar.Children.Add(group);
        foreach (var (glyph, text, tip, action) in new (string, string, string, Action)[] { ("", T("Importa"), T("Importa una vCard (.vcf) o un CSV di Outlook o Gmail"), Import), ("", T("Esporta"), T("Esporta i contatti mostrati in una vCard (.vcf)"), Export) })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Margin = new Thickness(0, 0, 4, 0), ToolTip = tip };
            button.SetResourceReference(StyleProperty, "Command");
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) => action();
            bar.Children.Add(button);
        }
        status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        bar.Children.Add(status);
        root.Children.Add(bar);

        var left = new DockPanel();
        AutomationProperties.SetName(search, T("Cerca fra le persone"));
        search.ToolTip = T("Nome, indirizzo, azienda, telefono o note");
        searchHint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush"); // lo stesso grigio di "Cerca" in alto
        search.TextChanged += (_, _) =>
        {
            searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Fill();
        };
        var searchBox = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchBox.Children.Add(search);
        searchBox.Children.Add(searchHint);
        DockPanel.SetDock(searchBox, Dock.Top);
        left.Children.Add(searchBox);
        SetUpSections(left); // il filtro per account sotto la ricerca, le righe di intestazione, nota e persona
        // Righe create solo per quelle in vista, come l'elenco dei messaggi: lo stile Fluent del ListBox mette
        // CanContentScroll a falso, e con migliaia di contatti importati WPF creava tutte le righe a ogni lettera cercata.
        SetUpList(list);
        AutomationProperties.SetName(list, T("Persone"));
        list.SelectionChanged += (_, _) => OnSelectionChanged();
        list.MouseDoubleClick += (_, e) => OnListDoubleClick(e);
        left.Children.Add(list);
        Grid.SetRow(left, 1);
        root.Children.Add(left);

        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        Grid.SetRow(card, 1);
        Grid.SetColumn(card, 1);
        root.Children.Add(card);
        Content = root;
    }

    /// <summary>
    /// L'elenco come quello dei messaggi: righe create solo per quelle in vista (lo stile Fluent del ListBox mette CanContentScroll a
    /// falso, e con migliaia di contatti importati WPF creava tutte le righe a ogni lettera cercata) e scorrimento a pixel, con la
    /// stessa rotella e lo stesso touchpad di WheelScrolling (già attivo su ogni ScrollViewer). Per elementi, l'unità di prima (il
    /// gestore Loaded di WheelScrolling.Enable non la cambiava per questo elenco), la posizione contava righe e ogni tacca da 60
    /// "pixel" saltava di 60 contatti. Messo prima del primo disegno, con la cache a zero come la MessageList.
    /// </summary>
    internal static void SetUpList(ListBox list)
    {
        ScrollViewer.SetCanContentScroll(list, true);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        VirtualizingPanel.SetCacheLength(list, new VirtualizationCacheLength(0));
        VirtualizingPanel.SetCacheLengthUnit(list, VirtualizationCacheLengthUnit.Pixel);
    }
    /// <summary>Una persona nell'elenco, con da dove viene; il nome per UI Automation è quello mostrato.</summary>
    internal sealed record Row(Contact Contact, IMailProvider? Provider)
    {
        /// <summary>La seconda riga: il primo indirizzo, o per un gruppo quante persone ha.</summary>
        public string Detail => Contact.IsGroup ? T("Gruppo, {0}", PeopleCount(Contact.Members.Count)) : Contact.Emails.FirstOrDefault() ?? "";

        /// <summary>
        /// Il nome per UI Automation: nome, indirizzo o persone del gruppo, e di chi è. Per un gruppo il numero è sempre al plurale in
        /// italiano ("1 persone"), com'era; in inglese con il singolare giusto.
        /// </summary>
        public override string ToString()
        {
            var where = Provider != null ? OriginOf(Provider) : T("in Missiva");
            if (Contact.IsGroup)
                return T("Gruppo {0}", Contact.Name) + ", " + (Lingua.English ? PeopleCount(Contact.Members.Count) : T("{0} persone", Contact.Members.Count)) + ", " + where;
            return Contact.Name + (Detail.Length > 0 ? ", " + Detail : "") + ", " + where;
        }
    }

    /// <summary>Quante persone: "1 persona", "3 persone" (in inglese "1 person", "3 people").</summary>
    internal static string PeopleCount(int n) => n == 1 ? T("1 persona") : T("{0} persone", n);

    /// <summary>Di chi sono i contatti di un account: Google per Gmail e Workspace, Outlook per gli altri.</summary>
    internal static string Origin(IMailProvider p) => p.Account.Kind == AccountKind.Gmail ? "Google" : "Outlook";

    /// <summary>L'origine con l'account, "Google di ada@prova.example" (in inglese "Google (ada@prova.example)").</summary>
    internal static string OriginOf(IMailProvider p) =>
        p.Account.Kind == AccountKind.Gmail ? T("Google di {0}", p.Account.Email) : T("Outlook di {0}", p.Account.Email);

    /// <summary>Aperta: subito i contatti di Missiva, poi quelli degli account (solo con il consenso già dato).</summary>
    public void Open()
    {
        Fill();
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        foreach (var p in providers().Where(p => p.HasContacts))
        {
            try
            {
                remote[p] = await Source(p);
                errors.Remove(p);
                if (remote[p] is { } got) ContactBook.SetRemote(p.Account.Id, got); // la copia cifrata per i suggerimenti dei destinatari
            }
            catch (Exception ex)
            {
                Log.Error($"contatti di {p.Account.Email}", ex);
                errors[p] = ex.Message;
            }
            Fill();
        }
    }

    void Fill()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var selected = (list.SelectedItem as Row)?.Contact.Id;
        var words = search.Text.Trim();
        var origins = Origins(providers());
        RefreshFilter(origins);
        var entries = BuildEntries(origins, ContactBook.Local, remote, errors, words, filterKey);
        list.ItemsSource = entries;
        list.SelectedItem = selected == null ? null : entries.OfType<Row>().FirstOrDefault(r => r.Contact.Id == selected);
        if (list.SelectedItem == null) ShowCard(null);
        int shown = entries.Count(e => e is Row);
        status.Text = PeopleCount(shown);
        Log.Time($"Persone, elenco riempito ({shown} persone{(words.Length > 0 ? $", ricerca di {words.Length} caratteri" : "")})", clock);
    }

    static DataTemplate RowTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(MarginProperty, new Thickness(4, 4, 4, 4));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Contact.Name"));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Row.Detail)));
        detail.SetValue(TextBlock.FontSizeProperty, 12.0);
        detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        detail.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        panel.AppendChild(name);
        panel.AppendChild(detail);
        return new DataTemplate { VisualTree = panel };
    }

    static TextBlock Note(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(4, 2, 0, 2) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    // ---------------------------------------------------------------- scheda

    /// <summary>
    /// Aprire un indirizzo fuori da Missiva (chiamare con tel:, Google Contatti, il sito di un contatto): solo quelli che
    /// <see cref="ContactLinks.IsAllowed"/> accetta. Sostituibile dai test; nella versione di prova scrive solo "prova: chiamerei ..."
    /// nel registro e non apre niente.
    /// </summary>
    public static Action<string> OpenExternal { get; set; } = OpenForReal;

    /// <summary>Copiare un testo negli appunti. Sostituibile dai test; nella versione di prova non tocca gli appunti (solo il registro).</summary>
    public static Action<string> CopyText { get; set; } = CopyForReal;

    static void OpenForReal(string url)
    {
        if (!ContactLinks.IsAllowed(url))
        {
            Log.Write("persone: indirizzo non aperto (non consentito)");
            return;
        }
        if (MainWindow.Testing)
        {
            Log.Write("prova: " + ContactLinks.Describe(url));
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("persone: apertura di un collegamento", ex);
        }
    }

    static void CopyForReal(string text)
    {
        if (MainWindow.Testing)
        {
            Log.Write($"prova: copierei negli appunti {text.Length} caratteri");
            return;
        }
        try
        {
            Clipboard.SetDataObject(text, true);
        }
        catch (Exception ex)
        {
            Log.Error("persone: copia negli appunti", ex);
        }
    }

    /// <summary>I telefoni di un contatto in ordine (cellulare, ufficio, casa, con gli altri di Outlook), senza i vuoti: Chiama prende il primo che è un numero valido.</summary>
    internal static List<(string Label, string Text)> PhonesOf(Contact c)
    {
        var phones = new List<(string Label, string Text)> { (T("Cellulare"), c.Mobile), (T("Telefono ufficio"), c.BusinessPhone) };
        phones.AddRange(c.MoreBusinessPhones.Select(p => (T("Telefono ufficio"), p)));
        phones.Add((T("Telefono casa"), c.HomePhone));
        phones.AddRange(c.MoreHomePhones.Select(p => (T("Telefono casa"), p)));
        return phones.Where(p => p.Text.Trim().Length > 0).ToList();
    }

    /// <summary>
    /// Dove porta Modifica per un contatto di Google (che Missiva legge soltanto): la sua scheda in Google Contatti, un solo indirizzo https
    /// costruito da <see cref="ContactLinks.GoogleContactsUrl"/>. Null per gli altri contatti, che si modificano in Missiva.
    /// </summary>
    internal static string? GoogleEditUrl(Contact contact, IMailProvider? provider) =>
        provider is { ContactsReadOnly: true, Account.Kind: AccountKind.Gmail } ? ContactLinks.GoogleContactsUrl(contact.ServerId, provider.Account.Email) : null;
    /// <summary>
    /// Modifica: nei contatti di Missiva e di Outlook la finestra del contatto (il gruppo la sua); in quelli di Google, che Missiva
    /// legge soltanto, la scheda di quella persona in Google Contatti, nel browser.
    /// </summary>
    void Modify(Row row)
    {
        if (GoogleEditUrl(row.Contact, row.Provider) is { } url) OpenExternal(url);
        else if (row.Provider is not { ContactsReadOnly: true }) Edit(row);
    }

    static Button CardButton(string glyph, string text, string name, bool accent = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 5, 14, 6), MinWidth = 96 };
        if (accent) button.SetResourceReference(StyleProperty, "AccentButtonStyle");
        AutomationProperties.SetName(button, name);
        return button;
    }

    static TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    /// <summary>Il piccolo pulsante Copia accanto a un valore: dopo il clic per un secondo "Copiato" con la spunta.</summary>
    static Button CopyButton(string text, string name)
    {
        var glyph = new TextBlock { Text = "", FontFamily = Icons, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var done = new TextBlock { Text = T("Copiato"), FontSize = 12, Visibility = Visibility.Collapsed, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(glyph);
        content.Children.Add(done);
        var button = new Button { Content = content, Padding = new Thickness(7, 2, 7, 2), MinHeight = 26, MinWidth = 30, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = T("Copia") };
        button.SetResourceReference(StyleProperty, "Command");
        AutomationProperties.SetName(button, name);
        System.Windows.Threading.DispatcherTimer? timer = null;
        button.Click += (_, _) =>
        {
            CopyText(text);
            glyph.Text = "";
            done.Visibility = Visibility.Visible;
            timer?.Stop();
            timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                glyph.Text = "";
                done.Visibility = Visibility.Collapsed;
            };
            timer.Start();
        };
        return button;
    }

    /// <summary>
    /// Il valore di una scheda: testo di sola lettura che si seleziona col mouse e, se ha un'azione, un collegamento (nel colore
    /// d'accento, con la tastiera Tab e Invio, per UI Automation un Hyperlink con il suo nome).
    /// </summary>
    static RichTextBox ValueBox(string text, string label, string linkName, Action? open)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        if (open != null)
        {
            var link = new Hyperlink(new Run(text)) { ToolTip = linkName };
            link.SetResourceReference(Hyperlink.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            AutomationProperties.SetName(link, linkName);
            link.Click += (_, _) => open();
            paragraph.Inlines.Add(link);
        }
        else
        {
            var lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run(lines[i]));
            }
        }
        var document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
        var box = new RichTextBox(document)
        {
            IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsUndoEnabled = false, Margin = new Thickness(0, 3, 0, 0), VerticalAlignment = VerticalAlignment.Top,
        };
        // Il FlowDocument parte con il suo carattere (Times) e il suo colore: prende quelli della casella, cioè dell'app e del tema.
        document.SetBinding(FlowDocument.FontFamilyProperty, new System.Windows.Data.Binding(nameof(Control.FontFamily)) { Source = box });
        document.SetBinding(FlowDocument.FontSizeProperty, new System.Windows.Data.Binding(nameof(Control.FontSize)) { Source = box });
        document.SetBinding(FlowDocument.ForegroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source = box });
        AutomationProperties.SetName(box, label);
        return box;
    }

    /// <summary>Una riga della scheda: il valore (selezionabile, con il suo collegamento se ce l'ha), il tipo a destra e il pulsante Copia.</summary>
    static Grid ValueRow(string text, string label, string? caption, Action? open, string linkName, string copyName)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(ValueBox(text, label, linkName, open));
        if (caption != null)
        {
            var type = Note(caption);
            type.Margin = new Thickness(12, 5, 0, 0);
            type.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(type, 1);
            grid.Children.Add(type);
        }
        var copy = CopyButton(text, copyName);
        Grid.SetColumn(copy, 2);
        grid.Children.Add(copy);
        return grid;
    }

    /// <summary>L'avatar tondo della scheda: iniziali sul colore del nome, il logo del marchio se già trovato (come nell'elenco dei messaggi).</summary>
    static Border Avatar(Address who)
    {
        var avatar = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), Margin = new Thickness(0, 0, 18, 0), Background = Avatars.Brush(who), VerticalAlignment = VerticalAlignment.Top };
        avatar.Child = new TextBlock { Text = Avatars.Initials(who), Foreground = Brushes.White, FontSize = 26, FontWeight = FontWeights.SemiBold,
                                       HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (Logos.DomainFor(who) is { } domain && Logos.Get(domain) is { } logo)
        {
            var mark = new System.Windows.Shapes.Ellipse { Fill = new ImageBrush(logo) { Stretch = Stretch.UniformToFill }, Stroke = new SolidColorBrush(Color.FromArgb(0x1F, 0, 0, 0)) };
            RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
            avatar.Background = Brushes.White;
            avatar.Child = mark;
        }
        return avatar;
    }

    void ShowCard(Row? row)
    {
        if (row == null)
        {
            var empty = Note(T("Scegli una persona per vedere la sua scheda."));
            empty.Margin = new Thickness(24);
            card.Child = empty;
            return;
        }
        var c = row.Contact;
        if (c.IsGroup)
        {
            ShowGroup(row);
            return;
        }
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var avatar = Avatar(new Address { Name = c.Name, Email = c.Emails.FirstOrDefault() ?? "" });
        DockPanel.SetDock(avatar, Dock.Left);
        head.Children.Add(avatar);
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = c.Name, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var role = string.Join(", ", new[] { c.JobTitle, c.Company }.Where(t => t.Length > 0));
        if (role.Length > 0)
        {
            var roleLine = Note(role);
            roleLine.FontSize = 14;
            roleLine.Margin = new Thickness(0, 2, 0, 0);
            title.Children.Add(roleLine);
        }
        var origin = Note(row.Provider == null ? T("In Missiva, su questo PC") : row.Provider.ContactsReadOnly ? T("Contatti di {0} di {1}, in sola lettura", Origin(row.Provider), row.Provider.Account.Email) : T("Contatti di {0} di {1}", Origin(row.Provider), row.Provider.Account.Email));
        origin.Margin = new Thickness(0, 4, 0, 0);
        title.Children.Add(origin);
        head.Children.Add(title);
        panel.Children.Add(head);

        // I numeri: cellulare, ufficio e casa (con gli altri di Outlook); quelli che non sono un numero valido restano testo.
        var phones = PhonesOf(c);


        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        if (c.Emails.FirstOrDefault() is { } first && WriteRequested != null)
        {
            var write = CardButton("", T("Scrivi"), T("Scrivi a {0}", first), accent: true);
            write.ToolTip = T("Nuovo messaggio a {0}", first);
            write.Click += (_, _) => WriteRequested(first);
            actions.Children.Add(write);
        }
        if (phones.FirstOrDefault(p => ContactLinks.TelUri(p.Text) != null) is { Text: { Length: > 0 } callText } callable)
        {
            var call = CardButton("", T("Chiama"), T("Chiama {0}", callText));
            call.ToolTip = T("Chiama {0}: {1}", callable.Label.ToLowerInvariant(), callText);
            call.Click += (_, _) => OpenExternal(ContactLinks.TelUri(callText)!);
            actions.Children.Add(call);
        }
        // Google senza l'ambito di scrittura (ContactsReadOnly): Modifica apre Google Contatti, Elimina non c'è e c'è "Consenti la modifica"; con l'ambito come Outlook.
        bool readOnly = row.Provider is { ContactsReadOnly: true };
        bool google = row.Provider is { ContactsReadOnly: true, Account.Kind: AccountKind.Gmail };
        if (!readOnly || google)
        {
            var edit = CardButton("", T("Modifica"), google ? T("Modifica in Google Contatti") : T("Modifica il contatto"));
            if (google) edit.ToolTip = T("Apre questa persona in Google Contatti, nel browser");
            edit.Click += (_, _) => Modify(row);
            actions.Children.Add(edit);
        }
        if (!readOnly)
        {
            var remove = CardButton("", T("Elimina"), T("Elimina il contatto"));
            remove.Click += async (_, _) => await RemoveAsync(row);
            actions.Children.Add(remove);
        }
        if (google && AllowWriteRequested != null)
        {
            var allow = CardButton("\uE785", T("Consenti la modifica"), T("Consenti la modifica dei contatti di {0}", row.Provider!.Account.Email));
            allow.ToolTip = T("Missiva potrà creare, cambiare ed eliminare i tuoi contatti di Google");
            allow.Click += (_, _) => AllowFor(row.Provider!, write: true);
            actions.Children.Add(allow);
        }
        panel.Children.Add(actions);
        if (google)
        {
            var why = Note(T("Questo contatto è di Google: Modifica lo apre in Google Contatti. Per cambiarlo qui, consenti la modifica."));
            why.Margin = new Thickness(0, 0, 0, 2);
            panel.Children.Add(why);
        }

        if (c.Emails.Count > 0)
        {
            panel.Children.Add(Heading(T("Posta elettronica")));
            foreach (var email in c.Emails)
                panel.Children.Add(ValueRow(email, T("Posta elettronica"), null, WriteRequested == null ? null : () => WriteRequested(email), T("Scrivi a {0}", email), T("Copia l'indirizzo {0}", email)));
        }
        if (phones.Count > 0)
        {
            panel.Children.Add(Heading(T("Telefono")));
            foreach (var (label, text) in phones)
            {
                var uri = ContactLinks.TelUri(text);
                panel.Children.Add(ValueRow(text, label, label, uri == null ? null : () => OpenExternal(uri), T("Chiama {0}", text), T("Copia il numero {0}", text)));
            }
        }
        if (c.Address.Trim().Length > 0)
        {
            panel.Children.Add(Heading(T("Indirizzo")));
            panel.Children.Add(ValueRow(c.Address, T("Indirizzo"), null, null, "", T("Copia l'indirizzo postale")));
        }
        if (c.Website.Trim().Length > 0)
        {
            panel.Children.Add(Heading(T("Sito web")));
            var url = ContactLinks.WebUrl(c.Website);
            panel.Children.Add(ValueRow(c.Website.Trim(), T("Sito web"), null, url == null ? null : () => OpenExternal(url), T("Apri il sito {0}", c.Website.Trim()), T("Copia l'indirizzo del sito")));
        }
        if (c.Notes.Trim().Length > 0)
        {
            panel.Children.Add(Heading(T("Note")));
            panel.Children.Add(ValueRow(c.Notes.Trim(), T("Note"), null, null, "", T("Copia le note")));
        }
        card.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>La scheda di un gruppo: le sue persone (ognuna un collegamento Scrivi, con Copia), Scrivi al gruppo, Modifica, Elimina.</summary>
    void ShowGroup(Row row)
    {
        var g = row.Contact;
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var avatar = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Top };
        avatar.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        avatar.Child = new TextBlock { Text = "", FontFamily = Icons, Foreground = Brushes.White, FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(avatar, Dock.Left);
        head.Children.Add(avatar);
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = g.Name, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var kind = Note(T("Gruppo di contatti di Missiva, {0}", PeopleCount(g.Members.Count)));
        kind.Margin = new Thickness(0, 4, 0, 0);
        title.Children.Add(kind);
        head.Children.Add(title);
        panel.Children.Add(head);
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var people = g.MemberAddresses;
        if (people.Count > 0 && WriteRequested != null)
        {
            var write = CardButton("", T("Scrivi al gruppo"), T("Scrivi al gruppo {0}", g.Name), accent: true);
            write.Click += (_, _) => WriteRequested(string.Join("; ", people.Select(a => a.FieldText())));
            actions.Children.Add(write);
        }
        var edit = CardButton("", T("Modifica"), T("Modifica il gruppo"));
        edit.Click += (_, _) => EditGroup(row);
        actions.Children.Add(edit);
        var remove = CardButton("", T("Elimina"), T("Elimina il gruppo"));
        remove.Click += async (_, _) => await RemoveAsync(row);
        actions.Children.Add(remove);
        panel.Children.Add(actions);
        panel.Children.Add(Heading(T("Persone")));
        foreach (var member in g.Members)
        {
            var address = Contact.ParseMember(member);
            var email = address?.Email;
            panel.Children.Add(ValueRow(member, T("Persona del gruppo"), null, email == null || WriteRequested == null ? null : () => WriteRequested(email),
                                        T("Scrivi a {0}", email), T("Copia l'indirizzo {0}", email ?? member)));
        }
        card.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Nuovo gruppo o Modifica gruppo: fra i contatti di Missiva, cifrati.</summary>
    void EditGroup(Row? row)
    {
        var window = new GroupWindow(row?.Contact) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true || window.Result is not { } saved) return;
        ContactBook.Put(saved);
        Log.Write(row == null ? $"persone: gruppo creato in Missiva ({saved.Members.Count} persone)" : $"persone: gruppo di Missiva modificato ({saved.Members.Count} persone)");
        status.Text = row == null ? T("Gruppo creato") : T("Gruppo salvato");
        Fill();
        list.SelectedItem = list.Items.OfType<Row>().FirstOrDefault(r => r.Contact.Id == saved.Id);
    }

    // ---------------------------------------------------------------- scritture

    /// <summary>Gli account con i contatti già letti e scrivibili (Outlook con il consenso, Google con l'ambito di scrittura): lì si può anche creare.</summary>
    List<IMailProvider> Writable() => remote.Where(r => r.Value != null && !r.Key.ContactsReadOnly).Select(r => r.Key).ToList();

    void Edit(Row? row)
    {
        if (row?.Contact.IsGroup == true)
        {
            EditGroup(row);
            return;
        }
        var owner = Window.GetWindow(this);
        var window = new ContactWindow(row?.Contact, row == null ? Writable() : [], row?.Provider) { Owner = owner };
#if DEBUG
        if (Environment.GetCommandLineArgs().Contains("--contatti-finti") && row == null && Environment.GetCommandLineArgs().Contains("--nuovo-contatto")) window.Loaded += (_, _) => window.FillSample();
        if (Environment.GetCommandLineArgs().Contains("--scelta-dove") && row == null) window.Loaded += (_, _) => window.OpenWhere(1);
#endif
        if (window.ShowDialog() != true || window.Result is not { } saved) return;
        _ = SaveAsync(row, saved, window.Target);
    }

    async Task SaveAsync(Row? before, Contact after, IMailProvider? target)
    {
        try
        {
            if (target == null)
            {
                ContactBook.Put(after);
                Log.Write(before == null ? "persone: contatto creato in Missiva" : "persone: contatto di Missiva modificato");
            }
            else
            {
                after.ServerId = await target.SaveContactAsync(before?.Contact, after); // Google: after prende anche i campi come li ha salvati
                after.Source = target.Account.Id;
                if (remote.TryGetValue(target, out var saved) && saved != null)
                {
                    PutRemote(saved, after);
                    ContactBook.SetRemote(target.Account.Id, saved);
                }
            }
            if (filterKey.Length > 0 && filterKey != (target?.Account.Id ?? "missiva")) filterKey = ""; // il contatto nuovo si vede nella sua sezione
            status.Text = before == null ? T("Contatto creato") : T("Contatto salvato");
            Fill();
            list.SelectedItem = list.Items.OfType<Row>().FirstOrDefault(r => r.Contact.Id == after.Id);
        }
        catch (ContactChangedException changed)
        {
            // Google: cambiato altrove anche dopo la rilettura. La persona com'è ora prende il posto di quella dell'elenco e la finestra si riapre con i dati nuovi.
            Log.Write("persone: contatto di Google cambiato altrove, riaperto con i dati nuovi");
            var fresh = changed.Fresh;
            if (before != null) fresh.Id = before.Contact.Id;
            if (target != null && remote.TryGetValue(target, out var current) && current != null)
            {
                PutRemote(current, fresh);
                ContactBook.SetRemote(target.Account.Id, current);
            }
            Fill();
            list.SelectedItem = list.Items.OfType<Row>().FirstOrDefault(r => r.Contact.Id == fresh.Id);
            Dialog.Show(Window.GetWindow(this), T("Contatto cambiato altrove"), changed.Message);
            Edit(new Row(fresh, target));
        }
        catch (MailException gone) when (gone.Status == 404 && before != null && target != null)
        {
            // Eliminato altrove: esce anche dall'elenco.
            Log.Write("persone: il contatto è stato eliminato altrove, tolto dall'elenco");
            if (remote.TryGetValue(target, out var current) && current != null)
            {
                current.RemoveAll(c => c.Id == before.Contact.Id);
                ContactBook.SetRemote(target.Account.Id, current);
            }
            Fill();
            Dialog.Show(Window.GetWindow(this), T("Contatto non salvato"), gone.Message);
        }
        catch (Exception ex)
        {
            Log.Error("contatto non salvato", ex);
            Dialog.Show(Window.GetWindow(this), T("Contatto non salvato"), ex.Message);
        }
    }

    /// <summary>Un contatto salvato (nuovo o cambiato, stesso Id) nell'elenco dei contatti di un account: una voce sola, nella sua sezione.</summary>
    internal static void PutRemote(List<Contact> contacts, Contact saved)
    {
        contacts.RemoveAll(c => c.Id == saved.Id);
        contacts.Add(saved);
    }

    /// <summary>Cosa dice la conferma di Elimina per un contatto di un account: la cancellazione arriva a tutti i dispositivi di quell'account.</summary>
    internal static string DeleteWarning(IMailProvider provider) => provider.Account.Kind == AccountKind.Gmail
        ? T("Elimina da Google Contatti: sparisce anche dal telefono e da tutti i tuoi dispositivi.")
        : T("Elimina da Outlook: sparisce anche dalle altre app e dai dispositivi che usano {0}.", provider.Account.Email);

    async Task RemoveAsync(Row row)
    {
        var owner = Window.GetWindow(this);
        var heading = T("Eliminare {0}?", row.Contact.Name);
        var answer = row.Provider != null ? Dialog.AskCancelFirst(owner, heading, DeleteWarning(row.Provider), T("Elimina")) // predefinito Annulla: non si disfa
            : Dialog.Ask(owner, heading, row.Contact.IsGroup ? T("Il gruppo esce da Missiva; le sue persone restano.") : T("Il contatto esce da Missiva."), T("Elimina"));
        if (answer != Choice.Primary) return;
        try
        {
            if (row.Provider == null) ContactBook.Remove(row.Contact);
            else
            {
                await row.Provider.DeleteContactAsync(row.Contact);
                if (remote.TryGetValue(row.Provider, out var list) && list != null)
                {
                    list.RemoveAll(c => c.Id == row.Contact.Id);
                    ContactBook.SetRemote(row.Provider.Account.Id, list);
                }
            }
            status.Text = T("Contatto eliminato");
            Fill();
        }
        catch (Exception ex)
        {
            Log.Error("contatto non eliminato", ex);
            Dialog.Show(owner, T("Contatto non eliminato"), ex.Message);
        }
    }

    /// <summary>Un'importazione in corso: un secondo clic su Importa aspetta la fine.</summary>
    bool importing;

    /// <summary>
    /// Importa: una vCard (anche di più contatti) o un CSV di Outlook o Gmail, nei contatti di Missiva. Lettura, analisi e
    /// salvataggio (cifrato) fuori dal thread dell'interfaccia, un salvataggio solo per tutto il file (ContactBook.Add).
    /// </summary>
    async void Import()
    {
        if (importing) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = T("Rubriche (*.vcf;*.csv)|*.vcf;*.csv|vCard (*.vcf)|*.vcf|CSV di Outlook o Gmail (*.csv)|*.csv"), Title = T("Importa contatti") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var path = dialog.FileName;
        importing = true;
        status.Text = T("Importazione in corso…");
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var found = await Task.Run(() => ContactFiles.Read(path));
            ContactBook.Add(found); // l'elenco cambia qui, dove Fill lo legge
            await Task.Run(ContactBook.Save);
            Log.Time($"persone: importazione di {found.Count} contatti", clock);
            Log.Write($"persone: {found.Count} contatti importati da un file {Path.GetExtension(path)}");
            Fill();
            status.Text = T("{0} contatti importati in Missiva", found.Count);
        }
        catch (Exception ex)
        {
            Log.Error("importazione dei contatti", ex);
            status.Text = "";
            Dialog.Show(Window.GetWindow(this), T("Contatti non importati"), ex is MailException ? ex.Message : T("Il file non si legge: {0}", ex.Message));
        }
        finally
        {
            importing = false;
        }
    }

    /// <summary>Esporta i contatti mostrati (con la ricerca, solo quelli trovati) in una vCard 3.0, senza foto.</summary>
    void Export()
    {
        var shown = list.Items.OfType<Row>().Select(r => r.Contact).Where(c => !c.IsGroup).ToList(); // i gruppi non sono vCard
        if (shown.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "vCard (*.vcf)|*.vcf", FileName = T("Contatti.vcf"), Title = T("Esporta contatti") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, ContactFiles.WriteVCard(shown), new System.Text.UTF8Encoding(false));
            status.Text = T("{0} contatti esportati", shown.Count);
        }
        catch (Exception ex)
        {
            Dialog.Show(Window.GetWindow(this), T("Contatti non esportati"), ex.Message);
        }
    }

#if DEBUG
    /// <summary>Prove (--persona N): la scheda della persona N (da 0, senza contare le intestazioni), con la riga portata in vista.</summary>
    internal void TestSelect(int index)
    {
        var i = IndexOfContact(index);
        if (i < 0) return;
        list.SelectedIndex = i;
        list.ScrollIntoView(list.SelectedItem);
        list.UpdateLayout();
        (list.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem)?.Focus(); // i tasti di MISSIVA_KEYS arrivano all'elemento con il fuoco
    }

    /// <summary>Prove: l'elenco (rotella e stato dell'elenco in DevTools).</summary>
    internal ListBox TestList => list;

    /// <summary>Prove (--modifica-contatto): Modifica della persona scelta, come dal pulsante (per Google con l'ambito la finestra del contatto).</summary>
    internal void TestEdit()
    {
        if (list.SelectedItem is Row row) Modify(row);
    }

    /// <summary>Prove (--nuovo-contatto): la finestra del contatto nuovo, come dal pulsante.</summary>
    internal void TestNewContact() => Edit(null);

    /// <summary>Prove (--cerca-persone testo): scrive il testo nella casella di ricerca.</summary>
    internal void TestSearch(string text) => search.Text = text;

    /// <summary>Prove (--filtro-persone parte): sceglie nel filtro per account la voce il cui nome contiene la parte (missiva, netflix, outlook...).</summary>
    internal void TestFilter(string part) => filter.SelectedItem = filterOptions.FirstOrDefault(o => o.Title.Contains(part, StringComparison.OrdinalIgnoreCase)) ?? filter.SelectedItem;
#endif
}
