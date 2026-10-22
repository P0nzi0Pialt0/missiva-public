using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>Chiede un nome (nuova cartella, nuovo nome di una cartella), con una nota facoltativa.</summary>
public sealed class NameWindow : Window
{
    readonly TextBox box = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock error = Dialog.ErrorText();

    public string Value => box.Text.Trim();

    /// <param name="label">Che cosa si scrive: "Nome" di base (null), "Indirizzo" per una casella condivisa (già nella lingua di Missiva).</param>
    /// <param name="check">Un controllo del testo scritto: il motivo per cui non va, o null se va.</param>
    /// <param name="optional">Il testo si può lasciare vuoto (una nota facoltativa).</param>
    public NameWindow(string heading, string initial, string confirm, string? note = null, string? label = null, Func<string, string?>? check = null, bool optional = false)
    {
        // Il nome di base nella lingua di Missiva: un valore predefinito non può passare da T (la lingua si sceglie all'avvio).
        bool isName = label is null or "Nome";
        label = isName ? T("Nome") : label!;
        Title = heading;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
#if DEBUG
        DevTools.HideForPhotos(this); // le foto (--foto): nasce fuori dallo schermo e non prende il fuoco
#endif

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        if (note != null)
        {
            var hint = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            panel.Children.Add(hint);
        }
        panel.Children.Add(new TextBlock { Text = label });
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        box.Text = initial;
        panel.Children.Add(box);
        panel.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        var ok = new Button { Content = confirm, MinWidth = 100, IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) =>
        {
            var problem = Value.Length == 0 ? optional ? null : isName ? T("Scrivi un nome.") : T("Scrivi l'indirizzo.") : check?.Invoke(Value);
            if (problem != null)
            {
                error.Text = problem;
                error.Visibility = Visibility.Visible;
                return;
            }
            DialogResult = true;
        };
        var cancel = new Button { Content = T("Annulla"), MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(ok, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
    }
}
