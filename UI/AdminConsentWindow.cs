using System.Windows;
using System.Windows.Controls;

namespace Missiva.UI;

/// <summary>
/// Chiedi l'approvazione all'amministratore, per un permesso in più (il calendario) che la finestra di Microsoft ha
/// rifiutato a un account di lavoro o della scuola (AADSTS65001, 90094): la spiegazione di MicrosoftAuth.Explain, il link
/// del consenso dell'amministratore da copiare e "Scrivi all'amministratore", che prepara un Nuovo messaggio (Mailto) e
/// non invia nulla. Lo stesso pannello di Aggiungi account.
/// </summary>
public sealed class AdminConsentWindow : Window
{
    /// <summary>"Scrivi all'amministratore": il link mailto: da aprire in Nuovo messaggio dopo la chiusura.</summary>
    public string? Mailto { get; private set; }

    public AdminConsentWindow(string heading, string explanation, MailAccount account)
    {
        Title = heading;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
#if DEBUG
        DevTools.HideForPhotos(this); // le foto (--foto): nasce fuori dallo schermo e non prende il fuoco
#endif

        var (clientId, tenant) = MicrosoftAuth.AppOf(account);
        var link = MicrosoftAuth.AdminConsentUrl(clientId, tenant);
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(Secondary(explanation));
        panel.Children.Add(new TextBlock { Text = T("Chiedi l'approvazione all'amministratore"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        panel.Children.Add(Secondary(T("Manda questo link a un amministratore di Microsoft 365 della tua organizzazione: aprendolo può approvare Missiva per tutti gli utenti. {0} Quando l'ha fatto, riprova.",
                                       MicrosoftAuth.AdminConsentScope(clientId))));
        var box = new TextBox { Text = link, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(box, T("Link per l'amministratore"));
        panel.Children.Add(box);

        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        var copy = new Button { Content = T("Copia"), MinWidth = 100 };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(link);
                copy.Content = T("Copiato");
            }
            catch (Exception ex)
            {
                Log.Write("link per l'amministratore non copiato: " + ex.Message); // appunti occupati da un altro programma
                box.Focus();
                box.SelectAll();
            }
        };
        var mail = new Button { Content = T("Scrivi all'amministratore"), Margin = new Thickness(8, 0, 0, 0) };
        mail.Click += (_, _) =>
        {
            Mailto = MailtoFor(account.Email, link);
            Close();
        };
        var close = new Button { Content = T("Chiudi"), MinWidth = 100, IsCancel = true, IsDefault = true };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        buttons.Children.Add(copy);
        buttons.Children.Add(mail);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        Content = panel;
    }

    static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    /// <summary>
    /// Il messaggio per l'amministratore, come link mailto: senza destinatario (MainWindow lo apre in Nuovo messaggio; parte
    /// solo con Invia). Nella lingua di Missiva: in inglese la mail è in inglese.
    /// </summary>
    public static string MailtoFor(string? email, string link)
    {
        var body = email != null
            ? T("Ciao,\n\nper usare Missiva (programma di posta per Windows) con il mio account di lavoro {0} serve l'approvazione di un amministratore di Microsoft 365. Puoi darla aprendo questo link con il tuo account di amministratore:\n\n{1}\n\nGrazie", email, link)
            : T("Ciao,\n\nper usare Missiva (programma di posta per Windows) serve l'approvazione di un amministratore di Microsoft 365. Puoi darla aprendo questo link con il tuo account di amministratore:\n\n{0}\n\nGrazie", link);
        return "mailto:?subject=" + Uri.EscapeDataString(T("Approvazione di Missiva per Microsoft 365")) + "&body=" + Uri.EscapeDataString(body);
    }
}
