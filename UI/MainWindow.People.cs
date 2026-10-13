using System.Windows;

namespace Missiva.UI;

/// <summary>
/// Persone nella finestra principale, come il nuovo Outlook: il pulsante Persone della colonna delle app sostituisce la
/// posta (o il calendario) con PeopleView; Posta torna com'era. Il consenso ai contatti di Outlook si chiede solo dal
/// pulsante della vista (nella prova solo il registro).
/// </summary>
public partial class MainWindow
{
    PeopleView? people;
    bool peopleShown;
    /// <summary>Le parti della posta nascoste mentre si vede Persone (WebView2 non si lascia coprire da WPF).</summary>
    readonly List<(UIElement Element, Visibility Was)> peopleHidden = new();

    void OnPeopleRail(object sender, RoutedEventArgs e) => ShowPeople(true);

    void ShowPeople(bool show)
    {
        if (only != null) return;
        if (show && calendarShown) ShowCalendar(false);
        if (show && people == null)
        {
            people = new PeopleView(() => Providers.ToList()) { Margin = new Thickness(0) };
#if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--contatti-finti")) { ContactSamples.AddLocal(); people.Source = p => Task.FromResult(ContactSamples.Load(p.Account, Environment.GetCommandLineArgs())); }
            // --google-scrittura e --google-sola-lettura: i contatti di Google si dicono modificabili o no, senza guardare gli ambiti veri (per fotografare)
            if (Environment.GetCommandLineArgs().Contains("--google-scrittura")) GmailProvider.TestContactsWritable = true;
            else if (Environment.GetCommandLineArgs().Contains("--google-sola-lettura")) GmailProvider.TestContactsWritable = false;
#endif
            people.AllowRequested = p => AllowContactsAsync(p);
            people.AllowWriteRequested = p => AllowContactsAsync(p, write: true);
            people.WriteRequested = email => _ = OpenMailtoAsync("mailto:" + Uri.EscapeDataString(email).Replace("%40", "@"));
            System.Windows.Controls.Grid.SetColumn(people, 1);
            System.Windows.Controls.Grid.SetColumnSpan(people, 5);
            MainArea.Children.Add(people);
        }
        if (show != peopleShown)
        {
            peopleShown = show;
            if (show)
            {
                peopleHidden.Clear();
                foreach (UIElement part in MainArea.Children)
                    if (part != people && System.Windows.Controls.Grid.GetColumn(part) >= 1)
                    {
                        peopleHidden.Add((part, part.Visibility));
                        part.Visibility = Visibility.Collapsed;
                    }
                CommandArea.Visibility = Visibility.Collapsed;
                people!.Visibility = Visibility.Visible;
            }
            else
            {
                foreach (var (part, was) in peopleHidden) part.Visibility = was;
                peopleHidden.Clear();
                CommandArea.Visibility = Visibility.Visible;
                if (people != null) people.Visibility = Visibility.Collapsed;
                ShowFolders();
            }
            PaintRail();
            Log.Write(show ? "persone aperte" : "persone chiuse");
        }
        if (show) people!.Open();
    }

    /// <summary>Il pulsante dell'app aperta ha il fondo e il glifo colorato, come nel nuovo Outlook.</summary>
    void PaintRail()
    {
        foreach (var (button, glyph, on) in new[]
                 {
                     (MailRailButton, MailRailGlyph, !calendarShown && !peopleShown), (CalendarRailButton, CalendarRailGlyph, calendarShown),
                     (PeopleRailButton, PeopleRailGlyph, peopleShown),
                 })
        {
            if (on) button.SetResourceReference(BackgroundProperty, "SubtleFillColorSecondaryBrush");
            else button.Background = System.Windows.Media.Brushes.Transparent;
            glyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, on ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        }
    }

    /// <summary>
    /// La scheda della persona dal clic sul mittente nel riquadro (PersonCard): contatto o "Aggiungi ai contatti", ultimi
    /// messaggi e file con la ricerca dell'account; "Tutti i messaggi" cerca nella finestra principale.
    /// </summary>
    void ShowPerson(IMailProvider provider, Address person, Window owner)
    {
        if (person.Email.Length == 0) return;
        var account = accounts.FirstOrDefault(a => a.Provider == provider);
        Log.Write($"scheda della persona di un messaggio di {provider.Account.Email}");
        var card = new PersonCard(provider, person, PersonQuery(provider, person.Email),
            write: to => _ = OpenMailtoAsync("mailto:" + Uri.EscapeDataString(to).Replace("%40", "@")),
            searchAll: q =>
            {
                SearchBox.Text = q;
                _ = SearchAsync(q, null, account);
            },
            // La sua riga, se è nell'elenco aperto (stato di lettura e contrassegno restano allineati), altrimenti una nuova.
            open: message =>
            {
                if (account == null) return;
                Log.Write("scheda della persona: messaggio aperto in una finestra");
                OpenInWindow(rows.Rows.FirstOrDefault(r => r.Provider == provider && r.Summary.Id == message.Id && r.Summary.FolderId == message.FolderId && !r.IsHead)
                             ?? new MessageItem(message, account));
            }) { Owner = owner };
        card.Show();
    }

    /// <summary>
    /// "Consenti l'accesso ai contatti" (la finestra di Microsoft) o, con write, "Consenti la modifica dei contatti" (la finestra di Google):
    /// solo al clic (nella prova solo il registro).
    /// </summary>
    async Task<bool> AllowContactsAsync(IMailProvider provider, bool write = false)
    {
        try
        {
            if (write) await provider.AllowContactsWriteAsync();
            else await provider.AllowContactsAsync();
            return true;
        }
        catch (Exception ex) when (MicrosoftAuth.Canceled(ex))
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("consenso ai contatti", ex);
            ReadingView.ShowConsentError(this, write ? "Modifica dei contatti non consentita" : "Accesso ai contatti non consentito", ex, provider.Account, write ? "modificare i contatti" : "usare i contatti");
            return false;
        }
    }
}
