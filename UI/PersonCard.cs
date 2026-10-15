using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Missiva.UI;

/// <summary>
/// La scheda della persona, dal clic sul mittente nel riquadro, come la scheda del nuovo Outlook: il contatto (di Missiva o
/// di Outlook) con azienda, ruolo, telefoni e note, oppure "Aggiungi ai contatti"; sotto gli ultimi messaggi con la
/// persona e i loro allegati, cercati con la ricerca che c'è già (in tutte le cartelle dell'account). Solo letture: Scrivi
/// apre Nuovo messaggio, "Tutti i messaggi" fa la ricerca nella finestra principale, un messaggio o un file si apre nella
/// finestra del messaggio.
/// </summary>
public sealed class PersonCard : Window
{
    static readonly CultureInfo Italian = new("it-IT");
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    readonly IMailProvider provider;
    readonly Address person;
    readonly string query;
    readonly Action<string> write;
    readonly Action<string> searchAll;
    readonly Action<MessageSummary> open;
    readonly StackPanel details = new();
    readonly StackPanel messages = new();
    readonly StackPanel files = new();
    readonly TextBlock status = Secondary("Ricerca dei messaggi…");

    /// <param name="query">La ricerca dei messaggi da e per la persona, nella sintassi dell'account.</param>
    /// <param name="open">Apre un messaggio trovato nella finestra del messaggio.</param>
    public PersonCard(IMailProvider provider, Address person, string query, Action<string> write, Action<string> searchAll, Action<MessageSummary> open)
    {
        this.provider = provider;
        this.person = person;
        this.query = query;
        this.write = write;
        this.searchAll = searchAll;
        this.open = open;
        Title = person.Display;
        Width = 520;
        Height = 640;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var avatar = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Margin = new Thickness(0, 0, 16, 0), Background = Avatars.Brush(person) };
        avatar.Child = new TextBlock { Text = Avatars.Initials(person), Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold,
                                       HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // Il logo del marchio (Logos), se c'è, come nelle righe dell'elenco
        if (Logos.DomainFor(person) is { } domain && Logos.Get(domain) is { } logo)
        {
            var mark = new Ellipse { Fill = new ImageBrush(logo) { Stretch = Stretch.UniformToFill }, Stroke = new SolidColorBrush(Color.FromArgb(0x1F, 0, 0, 0)) };
            RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
            avatar.Background = Brushes.White;
            avatar.Child = mark;
        }
        DockPanel.SetDock(avatar, Dock.Left);
        head.Children.Add(avatar);
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = person.Display, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var address = new TextBox { Text = person.Email, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0) };
        AutomationProperties.SetName(address, "Indirizzo");
        title.Children.Add(address);
        head.Children.Add(title);
        panel.Children.Add(head);
        panel.Children.Add(details);
        panel.Children.Add(Heading("Ultimi messaggi"));
        panel.Children.Add(status);
        panel.Children.Add(messages);
        panel.Children.Add(Heading("File"));
        panel.Children.Add(files);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ShowDetails();
        Loaded += async (_, _) => await LoadAsync();
    }

    static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    static TextBlock Heading(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) };

    /// <summary>Una riga (messaggio o file) che apre il suo messaggio: pulsante piatto, con il clic, Invio o la barra spaziatrice.</summary>
    Button Opener(UIElement row, MessageSummary message, string name)
    {
        var button = new Button
        {
            Content = row, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(-6, 0, -6, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Apri il messaggio",
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => open(message);
        return button;
    }

    /// <summary>Il contatto con questo indirizzo fra quelli di Missiva e le copie dei contatti di Outlook.</summary>
    Contact? Known() => ContactBook.ForSuggestions().FirstOrDefault(c => !c.IsGroup && c.Emails.Contains(person.Email, StringComparer.OrdinalIgnoreCase));

    void ShowDetails()
    {
        details.Children.Clear();
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var writeButton = new Button { Content = "Scrivi", MinWidth = 88, Margin = new Thickness(0, 0, 6, 6) };
        writeButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        AutomationProperties.SetName(writeButton, "Scrivi a " + person.Email);
        writeButton.Click += (_, _) => write(person.FieldText());
        actions.Children.Add(writeButton);
        var all = new Button { Content = "Tutti i messaggi", MinWidth = 88, Margin = new Thickness(0, 0, 6, 6) };
        AutomationProperties.SetName(all, "Tutti i messaggi con " + person.Display);
        all.Click += (_, _) =>
        {
            searchAll(query);
            Close();
        };
        actions.Children.Add(all);
        var contact = Known();
        if (contact == null)
        {
            var add = new Button { Content = "Aggiungi ai contatti", MinWidth = 88, Margin = new Thickness(0, 0, 6, 6) };
            AutomationProperties.SetName(add, "Aggiungi ai contatti");
            add.Click += (_, _) => AddContact();
            actions.Children.Add(add);
        }
        details.Children.Add(actions);
        if (contact == null)
        {
            details.Children.Add(Secondary("Non è fra i tuoi contatti."));
            return;
        }
        var from = contact.Source == Contact.Local ? "In Missiva, su questo PC"
            : Store.Settings.Accounts.FirstOrDefault(a => a.Id == contact.Source) is { } owner ? $"Contatti di {(owner.Kind == AccountKind.Gmail ? "Google" : "Outlook")} di {owner.Email}" : "Contatti di Outlook";
        details.Children.Add(Secondary(from));
        void Line(string label, string value)
        {
            if (value.Trim().Length == 0) return;
            var line = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            line.Children.Add(Secondary(label));
            var box = new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0) };
            AutomationProperties.SetName(box, label);
            line.Children.Add(box);
            details.Children.Add(line);
        }
        Line("Azienda e ruolo", string.Join(", ", new[] { contact.JobTitle, contact.Company }.Where(t => t.Length > 0)));
        foreach (var other in contact.Emails.Where(e => !e.Equals(person.Email, StringComparison.OrdinalIgnoreCase))) Line("Altro indirizzo", other);
        Line("Cellulare", contact.Mobile);
        Line("Telefono ufficio", contact.BusinessPhone);
        Line("Telefono casa", contact.HomePhone);
        Line("Indirizzo", contact.Address);
        Line("Note", contact.Notes);
    }

    /// <summary>Aggiungi ai contatti: la finestra del contatto con nome e indirizzo, salvato fra i contatti di Missiva.</summary>
    void AddContact()
    {
        var words = person.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var draft = new Contact
        {
            GivenName = words.Length > 0 && !person.Name.Contains('@') ? words[0] : "",
            Surname = words.Length > 1 && !person.Name.Contains('@') ? words[1] : "",
            Emails = [person.Email],
        };
        var window = new ContactWindow(null, [], null, draft) { Owner = this };
        if (window.ShowDialog() != true || window.Result is not { } saved) return;
        ContactBook.Put(saved);
        Log.Write("persone: contatto aggiunto in Missiva dalla scheda del mittente");
        ShowDetails();
    }

    /// <summary>Gli ultimi messaggi con la persona (la ricerca in tutte le cartelle dell'account) e i loro allegati.</summary>
    async Task LoadAsync()
    {
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var page = await provider.SearchAsync(null, query);
            Log.Time($"scheda della persona: {page.Messages.Count} messaggi", clock);
            status.Visibility = Visibility.Collapsed;
            var found = page.Messages.OrderByDescending(m => m.Date).ToList();
            if (found.Count == 0) messages.Children.Add(Secondary("Nessun messaggio trovato."));
            foreach (var m in found.Take(8))
            {
                var row = new DockPanel();
                var when = Secondary(m.Date.Date == DateTime.Today ? m.Date.ToString("HH:mm", Italian) : m.Date.ToString("d MMM yyyy", Italian));
                when.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(when, Dock.Right);
                row.Children.Add(when);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = m.Subject.Length > 0 ? m.Subject : "(senza oggetto)", TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = m.IsRead ? FontWeights.Normal : FontWeights.SemiBold });
                if (m.FolderName.Length > 0) text.Children.Add(Secondary(m.FolderName));
                row.Children.Add(text);
                messages.Children.Add(Opener(row, m, $"{m.Subject}, {m.Date:d MMM yyyy}"));
            }
            var names = found.SelectMany(m => Pec.ShownAttachments(m.AttachmentNames).Select(n => (Name: n, Message: m))).Take(8).ToList();
            if (names.Count == 0) files.Children.Add(Secondary("Nessun allegato nei messaggi trovati."));
            foreach (var (name, m) in names)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = "", FontFamily = Icons, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var when = Secondary("  " + m.Date.ToString("d MMM", Italian));
                when.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(when);
                files.Children.Add(Opener(row, m, $"{name}, nel messaggio {m.Subject}"));
            }
        }
        catch (Exception ex)
        {
            Log.Error("scheda della persona", ex);
            status.Text = "I messaggi non si trovano: " + ex.Message;
        }
    }
}
