using System.Windows;
using System.Windows.Controls;

namespace Missiva.UI;

public enum Choice { Cancel, Primary, Secondary }

/// <summary>Finestre di conferma nello stile dell'app (le MessageBox di Windows non seguono il tema).</summary>
public static class Dialog
{
    public static void Show(Window? owner, string heading, string body) => Build(owner, heading, body, "OK", false);

    /// <summary>
    /// Il testo d'errore di una finestra, nascosto finché non serve, nel rosso del tema (Critical). Prima ogni finestra
    /// lo creava per conto suo con un rosso fisso, quello di Fluent per il tema chiaro, anche nel tema scuro, dove si
    /// legge male (audit dell'11 ottobre, C-16).
    /// </summary>
    /// <param name="top">Lo spazio sopra, in DIP: ogni finestra tiene il suo.</param>
    public static TextBlock ErrorText(double top = 10)
    {
        var text = new TextBlock { Visibility = Visibility.Collapsed, Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap };
        Critical(text);
        return text;
    }

    /// <summary>
    /// Il rosso di Missiva per quello che non va, che segue il tema anche a finestra aperta: CriticalBrush
    /// (App.SetTextColors), #C50F1F nel chiaro e #FF99A4 nello scuro, lo stesso di bandierine, importanza alta e scadenze
    /// passate.
    /// </summary>
    public static void Critical(TextBlock text) => text.SetResourceReference(TextBlock.ForegroundProperty, CriticalBrush);

    /// <summary>Lo stesso rosso per il bordo di un riquadro.</summary>
    public static void Critical(Border border) => border.SetResourceReference(Border.BorderBrushProperty, CriticalBrush);

    internal const string CriticalBrush = "CriticalBrush";

    /// <summary>Un avviso con OK e, accanto, una seconda scelta ("Apri la cartella"): Secondary se è stata scelta quella.</summary>
    public static Choice Show(Window? owner, string heading, string body, string secondary) => Build(owner, heading, body, "OK", false, secondary);

    public static Choice Ask(Window? owner, string heading, string body, string primary) => Build(owner, heading, body, primary, true);

    /// <summary>
    /// Il marchio web non si è potuto scrivere (Safety.MarkFromInternet falso): un documento di Office o un tipo pericoloso
    /// si apre solo dopo una conferma, perché senza marchio Office non lo apre in Visualizzazione protetta e SmartScreen non
    /// lo guarda. Gli altri tipi (PDF, immagini, testi) si aprono come prima. Vero se si può aprire.
    /// </summary>
    public static bool ConfirmOpenUnmarked(Window? owner, string fileName) =>
        !Safety.NeedsWebMark(fileName) ||
        Ask(owner, "Aprire un file senza il controllo di Windows?",
            $"\"{fileName}\" non si è potuto contrassegnare come scaricato da Internet (la cartella temporanea di Windows non lo permette), " +
            "quindi Office e SmartScreen non lo controllerebbero come fanno di solito. Aprilo solo se ti fidi di chi lo ha mandato.",
            "Apri comunque") == Choice.Primary;

    /// <summary>
    /// Una pagina web allegata (Safety.IsWebPage: .html, .svg, .mht...) si apre nel browser solo dopo una conferma con il
    /// suo testo: è una pagina arrivata per posta, non ci si scrivono password (scelte del 12 ottobre, T-10). Senza il
    /// rosso dei tipi che avviano programmi. Gli altri tipi non chiedono nulla. Vero se si può aprire.
    /// </summary>
    public static bool ConfirmOpenWebPage(Window? owner, string fileName)
    {
        if (!Safety.IsWebPage(fileName)) return true;
        var (heading, body, primary) = Safety.WebPageQuestion(fileName);
        return Ask(owner, heading, body, primary) == Choice.Primary;
    }

    /// <summary>
    /// Per le cose che non si disfano (eliminare un contatto di Google) e per una regola che manda la posta fuori:
    /// predefinito Annulla, con il fuoco su Annulla; Invio ed Esc annullano, per confermare si sceglie il pulsante.
    /// </summary>
    public static Choice AskCancelFirst(Window? owner, string heading, string body, string primary) => Build(owner, heading, body, primary, true, cancelFirst: true);

    /// <summary>Con una seconda scelta fra Annulla e quella principale (Salva bozza, Scarta, Annulla).</summary>
    public static Choice Ask(Window? owner, string heading, string body, string primary, string secondary) =>
        Build(owner, heading, body, primary, true, secondary);

    /// <summary>
    /// Tre scelte con un nome proprio anche per quella che non fa nulla (Riprova, Salva nelle Bozze, Torna al messaggio); con
    /// secondary null due sole (Invia comunque, Torna al messaggio).
    /// </summary>
    public static Choice Ask(Window? owner, string heading, string body, string primary, string? secondary, string cancel) =>
        Build(owner, heading, body, primary, true, secondary, cancelText: cancel);

    static Choice Build(Window? owner, string heading, string body, string primary, bool cancel, string? secondary = null, bool cancelFirst = false, string cancelText = "Annulla")
    {
        var result = Choice.Cancel;
        var window = new Window
        {
            Title = "Missiva",
            // Tre pulsanti con nomi lunghi (Torna al messaggio, Salva nelle Bozze, Aggiorna password) non stanno in 440.
            Width = primary.Length + (secondary?.Length ?? 0) + (cancel ? cancelText.Length : 0) > 34 ? 540 : 440,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        if (owner is { IsVisible: true }) window.Owner = owner;
#if DEBUG
        DevTools.HideForPhotos(window); // le foto della pagina GitHub (--foto): nessuna domanda sullo schermo di chi usa il PC
#endif

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        // Il testo si seleziona e si copia (un errore da incollare a chi aiuta): una casella di sola lettura senza bordo né
        // fondo, come gli indirizzi della scheda di una persona. Prima era un TextBlock (audit, F-44 punto 7). Ctrl+C senza
        // selezione copia titolo e testo, come le finestre di messaggio di Windows.
        var text = new TextBox
        {
            Text = body, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, IsTabStop = false, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(0), MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        System.Windows.Automation.AutomationProperties.SetName(text, body);
        text.SetResourceReference(Control.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(text);
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.C || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control || text.SelectionLength > 0) return;
            try { Clipboard.SetText(heading + Environment.NewLine + body); } catch (Exception ex) { Log.Error("copia del testo di un avviso", ex); }
            e.Handled = true;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        if (cancel)
        {
            var no = new Button { Content = cancelText, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsCancel = true, IsDefault = cancelFirst };
            if (cancelFirst) window.Loaded += (_, _) => no.Focus();
            no.Click += (_, _) => window.Close();
            buttons.Children.Add(no);
        }
        if (secondary != null)
        {
            var other = new Button { Content = secondary, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
            other.Click += (_, _) =>
            {
                result = Choice.Secondary;
                window.Close();
            };
            buttons.Children.Add(other);
        }
        var yes = new Button { Content = primary, MinWidth = 100, IsDefault = !cancelFirst };
        yes.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        yes.Click += (_, _) =>
        {
            result = Choice.Primary;
            window.Close();
        };
        buttons.Children.Add(yes);
        panel.Children.Add(buttons);
        window.Content = panel;
        window.ShowDialog();
        return result;
    }
}
