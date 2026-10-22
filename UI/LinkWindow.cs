using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>Inserisci collegamento (Ctrl+K nella scrittura), come nel nuovo Outlook: il testo da mostrare e l'indirizzo.</summary>
public sealed class LinkWindow : Window
{
    readonly TextBox text = new() { Margin = new Thickness(0, 4, 0, 12) };
    readonly TextBox address = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock error = Dialog.ErrorText();

    /// <summary>Il testo del link: se manca, l'indirizzo stesso.</summary>
    public string Text => text.Text.Trim().Length > 0 ? text.Text.Trim() : address.Text.Trim();
    public Uri? Address { get; private set; }

    /// <param name="current">L'indirizzo del link che si sta correggendo, se c'è.</param>
    public LinkWindow(string selected, string? current = null)
    {
        Title = T("Inserisci collegamento");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new TextBlock { Text = T("Testo da visualizzare") });
        text.Text = selected;
        System.Windows.Automation.AutomationProperties.SetName(text, T("Testo da visualizzare"));
        panel.Children.Add(text);
        panel.Children.Add(new TextBlock { Text = T("Indirizzo") });
        address.Text = current ?? "";
        System.Windows.Automation.AutomationProperties.SetName(address, T("Indirizzo"));
        panel.Children.Add(address);
        panel.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        var ok = new Button { Content = T("Inserisci"), MinWidth = 100, IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) =>
        {
            Address = Parse(address.Text);
            if (Address == null)
            {
                error.Text = T("Scrivi un indirizzo web (https://...) o di posta.");
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
            if (selected.Length > 0) address.Focus();
            else text.Focus();
            address.SelectAll(); // correggendo un link, l'indirizzo di prima si sostituisce scrivendo
        };
    }

    /// <summary>Solo link web e di posta; "www.esempio.it" diventa https://www.esempio.it, un indirizzo con @ un mailto:.</summary>
    static Uri? Parse(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Any(char.IsWhiteSpace)) return null;
        if (!value.Contains(':')) value = value.Contains('@') && !value.Contains('/') ? "mailto:" + value : "https://" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" ? uri : null;
    }
}
