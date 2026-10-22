using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Nuovo contatto e Modifica, come la scheda di Persone del nuovo Outlook: nome, cognome, nome mostrato, indirizzi di posta
/// (uno per riga), cellulare, telefono dell'ufficio e di casa, azienda, ruolo, indirizzo e note. Un contatto nuovo va in
/// Missiva o nei contatti di un account (Outlook con il consenso ai contatti, Google con quello alla modifica).
/// </summary>
public sealed class ContactWindow : Window
{
    readonly TextBox given = new(), surname = new(), display = new(), emails = new() { AcceptsReturn = true, MinHeight = 56, TextWrapping = TextWrapping.Wrap },
                     mobile = new(), business = new(), home = new(), company = new(), job = new(), address = new(),
                     notes = new() { AcceptsReturn = true, MinHeight = 80, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly ComboBox where = new() { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock error = Dialog.ErrorText();
    readonly Contact? original;
    readonly List<IMailProvider> targets;

    /// <summary>Il contatto come è stato salvato nella finestra (con lo stesso Id), e dove va (null: in Missiva).</summary>
    public Contact? Result { get; private set; }
    public IMailProvider? Target { get; private set; }

    /// <param name="targets">Per un contatto nuovo: gli account in cui si può creare (Microsoft, e Google con l'ambito di scrittura), oltre a Missiva.</param>
    /// <param name="owner">Per un contatto che c'è già: l'account dei suoi contatti di Outlook o di Google (null: in Missiva).</param>
    /// <param name="prefill">Per un contatto nuovo, i campi già scritti (Aggiungi ai contatti dalla scheda del mittente).</param>
    public ContactWindow(Contact? contact, List<IMailProvider> targets, IMailProvider? owner, Contact? prefill = null)
    {
        original = contact;
        this.targets = targets;
        Target = owner;
        Title = contact == null ? T("Nuovo contatto") : T("Modifica contatto");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, owner == null ? 12 : 2) });
        if (owner != null)
        {
            // Un contatto di un account (Google, Outlook): di chi è, e che Salva lo scrive lì.
            var from = new TextBlock { Text = T("Contatti di {0} di {1}: Salva li cambia anche lì", PeopleView.Origin(owner), owner.Account.Email), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
            from.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            panel.Children.Add(from);
        }
        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Field(string label, FrameworkElement control)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, Margin = new Thickness(0, 12, 8, 0), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(text, form.RowDefinitions.Count - 1);
            form.Children.Add(text);
            control.Margin = new Thickness(0, 6, 0, 0);
            AutomationProperties.SetName(control, label);
            Grid.SetRow(control, form.RowDefinitions.Count - 1);
            Grid.SetColumn(control, 1);
            form.Children.Add(control);
        }
        if (contact == null)
        {
            where.Items.Add(T("In Missiva (su questo PC)"));
            foreach (var p in targets) where.Items.Add(T("Contatti di {0} di {1}", PeopleView.Origin(p), p.Account.Email));
            where.SelectedIndex = 0;
            Field(T("Dove"), where);
        }
        // "Nome" da solo è il nome di qualcosa (in inglese "Name", la stessa chiave nelle altre finestre); qui è il nome di
        // battesimo accanto a Cognome, e in inglese si scrive a parte.
        Field(Lingua.English ? "First name" : "Nome", given);
        Field(T("Cognome"), surname);
        Field(T("Nome mostrato"), display);
        Field(T("Posta elettronica (una per riga)"), emails);
        Field(T("Cellulare"), mobile);
        Field(T("Telefono ufficio"), business);
        Field(T("Telefono casa"), home);
        Field(T("Azienda"), company);
        Field(T("Ruolo"), job);
        Field(T("Indirizzo"), address);
        Field(T("Note (PEC, partita IVA...)"), notes);
        panel.Children.Add(form);
        panel.Children.Add(error);
        if ((contact ?? prefill) is { } shown)
        {
            contact = shown;
            given.Text = contact.GivenName;
            surname.Text = contact.Surname;
            display.Text = contact.DisplayName;
            emails.Text = string.Join(Environment.NewLine, contact.Emails);
            mobile.Text = contact.Mobile;
            business.Text = contact.BusinessPhone;
            home.Text = contact.HomePhone;
            company.Text = contact.Company;
            job.Text = contact.JobTitle;
            address.Text = contact.Address;
            notes.Text = contact.Notes;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = T("Annulla"), MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = T("Salva"), MinWidth = 100, IsDefault = true };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = SystemParameters.WorkArea.Height - 80 };
        Loaded += (_, _) => given.Focus();
#if DEBUG
        DevTools.HideForPhotos(this); // le foto (--foto, anche in inglese): fuori dallo schermo, senza il fuoco
#endif
    }

    void Save()
    {
        var list = emails.Text.Split(['\n', '\r', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.FirstOrDefault(e => !AccountTypes.LooksValid(e)) is { } bad)
        {
            error.Text = T("Controlla l'indirizzo \"{0}\".", bad);
            error.Visibility = Visibility.Visible;
            return;
        }
        var result = original?.Clone() ?? new Contact();
        result.GivenName = given.Text.Trim();
        result.Surname = surname.Text.Trim();
        result.DisplayName = display.Text.Trim();
        result.Emails = list;
        result.Mobile = mobile.Text.Trim();
        result.BusinessPhone = business.Text.Trim();
        result.HomePhone = home.Text.Trim();
        result.Company = company.Text.Trim();
        result.JobTitle = job.Text.Trim();
        result.Address = address.Text.Trim();
        result.Notes = notes.Text.Trim();
        if (result.Name == Contact.NoName)
        {
            error.Text = T("Scrivi almeno un nome, un indirizzo o l'azienda.");
            error.Visibility = Visibility.Visible;
            return;
        }
        if (original == null) Target = where.SelectedIndex > 0 ? targets[where.SelectedIndex - 1] : null;
        Result = result;
        DialogResult = true;
    }

#if DEBUG
    /// <summary>Prove (--contatti-finti --nuovo-contatto): la finestra riempita come da un utente; Salva resta da premere.</summary>
    internal void FillSample()
    {
        given.Text = "Giulia";
        surname.Text = "Ferri";
        emails.Text = "giulia.ferri@cliente-prova.example";
        mobile.Text = "+39 333 000 1111";
        company.Text = "Ferri Impianti srl";
        job.Text = "Amministrazione";
        notes.Text = "PEC ferriimpianti@pec-prova.example, P. IVA 00000000000";
    }

    /// <summary>Prove (--scelta-dove): sceglie la voce N di "Dove" e apre la tendina, per fotografare la scelta dell'account.</summary>
    internal void OpenWhere(int index)
    {
        if (where.Items.Count == 0) return;
        where.SelectedIndex = Math.Min(index, where.Items.Count - 1);
        where.IsDropDownOpen = true;
    }
#endif
}
