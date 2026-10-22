using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Nuovo gruppo e Modifica gruppo, come i gruppi di contatti del nuovo Outlook: un nome e le persone, una per riga
/// ("Nome &lt;indirizzo&gt;" o solo l'indirizzo). Il gruppo sta fra i contatti di Missiva (contatti.dat, cifrato); nel campo A
/// della scrittura, scelto dai suggerimenti, diventa gli indirizzi delle sue persone.
/// </summary>
public sealed class GroupWindow : Window
{
    readonly TextBox name = new();
    readonly TextBox members = new() { AcceptsReturn = true, MinHeight = 160, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock error = Dialog.ErrorText();
    readonly Contact? original;

    /// <summary>Il gruppo come è stato salvato (con lo stesso Id se era una modifica).</summary>
    public Contact? Result { get; private set; }

    public GroupWindow(Contact? group)
    {
        original = group;
        Title = group == null ? T("Nuovo gruppo") : T("Modifica gruppo");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = T("Nome del gruppo"), Margin = new Thickness(0, 0, 0, 4) });
        AutomationProperties.SetName(name, T("Nome del gruppo"));
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = T("Persone, una per riga (Nome <indirizzo> o solo l'indirizzo)"), Margin = new Thickness(0, 14, 0, 4), TextWrapping = TextWrapping.Wrap });
        AutomationProperties.SetName(members, T("Persone del gruppo"));
        panel.Children.Add(members);
        var note = new TextBlock { Text = T("Il gruppo resta su questo PC, cifrato. Scrivendo il suo nome in A, Cc o Ccn lo trovi fra i suggerimenti: scelto, diventa gli indirizzi delle sue persone."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        panel.Children.Add(note);
        panel.Children.Add(error);
        if (group != null)
        {
            name.Text = group.DisplayName;
            members.Text = string.Join(Environment.NewLine, group.Members);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = T("Annulla"), MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = T("Salva"), MinWidth = 100, IsDefault = true };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => name.Focus();
#if DEBUG
        DevTools.HideForPhotos(this); // le foto (--foto, anche in inglese): fuori dallo schermo, senza il fuoco
#endif
    }

    void Save()
    {
        void Fail(string text)
        {
            error.Text = text;
            error.Visibility = Visibility.Visible;
        }
        var lines = members.Text.Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (lines.FirstOrDefault(l => Contact.ParseMember(l) == null) is { } bad) { Fail(T("Controlla \"{0}\": serve un indirizzo di posta.", bad)); return; }
        var people = lines.Select(l => Contact.ParseMember(l)!).GroupBy(a => a.Email, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (name.Text.Trim().Length == 0) { Fail(T("Scrivi il nome del gruppo.")); return; }
        if (people.Count == 0) { Fail(T("Aggiungi almeno una persona.")); return; }
        var result = original?.Clone() ?? new Contact();
        result.IsGroup = true;
        result.DisplayName = name.Text.Trim();
        result.Members = people.Select(a => a.ToString()).ToList();
        Result = result;
        DialogResult = true;
    }
}
