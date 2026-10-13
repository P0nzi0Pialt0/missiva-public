using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// La risposta a un invito, come in Outlook: una nota facoltativa per l'organizzatore e se mandargli la risposta; per
/// Proponi un nuovo orario il giorno, l'inizio e la fine (la proposta si manda sempre).
/// </summary>
public sealed class InviteWindow : Window
{
    readonly TextBox note = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly CheckBox send = new() { IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
    readonly DatePicker day = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBox from = new() { Width = 80 }, to = new() { Width = 80 };
    readonly TextBlock error = Dialog.ErrorText();
    readonly InviteAnswer answer;

    public InviteResponse? Response { get; private set; }

    public InviteWindow(Invitation invite, InviteAnswer answer)
    {
        this.answer = answer;
        var heading = answer switch
        {
            InviteAnswer.Accept => "Accetta l'invito",
            InviteAnswer.Tentative => "Rispondi in modo provvisorio",
            InviteAnswer.Decline => "Rifiuta l'invito",
            _ => "Proponi un nuovo orario",
        };
        Title = heading;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var what = new TextBlock { Text = $"{invite.Summary}\n{invite.When}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        what.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        panel.Children.Add(what);

        if (answer == InviteAnswer.Propose)
        {
            panel.Children.Add(new TextBlock { Text = "Giorno" });
            day.SelectedDate = invite.Start.Date;
            System.Windows.Automation.AutomationProperties.SetName(day, "Giorno");
            panel.Children.Add(day);
            var times = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 12) };
            from.Text = invite.Start.ToString("HH:mm");
            to.Text = invite.End.ToString("HH:mm");
            System.Windows.Automation.AutomationProperties.SetName(from, "Inizio");
            System.Windows.Automation.AutomationProperties.SetName(to, "Fine");
            times.Children.Add(new TextBlock { Text = "Dalle", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            times.Children.Add(from);
            times.Children.Add(new TextBlock { Text = "alle", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) });
            times.Children.Add(to);
            panel.Children.Add(times);
        }

        panel.Children.Add(new TextBlock { Text = "Nota per l'organizzatore (facoltativa)" });
        System.Windows.Automation.AutomationProperties.SetName(note, "Nota per l'organizzatore");
        note.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(note);
        send.Content = invite.Organizer is { } organizer ? $"Invia la risposta a {organizer.Display}" : "Invia la risposta all'organizzatore";
        send.IsEnabled = invite.Organizer != null && answer != InviteAnswer.Propose;
        if (invite.Organizer == null) send.IsChecked = false;
        if (answer != InviteAnswer.Propose) panel.Children.Add(send);
        panel.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        var ok = new Button { Content = answer == InviteAnswer.Propose ? "Proponi" : "Invia", MinWidth = 100, IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) => Confirm();
        var cancel = new Button { Content = "Annulla", MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(ok, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => note.Focus();
    }

    void Confirm()
    {
        if (answer != InviteAnswer.Propose)
        {
            Response = new InviteResponse(answer, note.Text, send.IsChecked == true);
            DialogResult = true;
            return;
        }
        static TimeSpan? Time(string text) =>
            TimeSpan.TryParseExact(text.Trim().Replace('.', ':'), ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1) ? t : null;
        if (day.SelectedDate is not { } date || Time(from.Text) is not { } start || Time(to.Text) is not { } end || end <= start)
        {
            error.Text = "Scegli il giorno e scrivi inizio e fine come 9:30 e 10:30, con la fine dopo l'inizio.";
            error.Visibility = Visibility.Visible;
            return;
        }
        Response = new InviteResponse(answer, note.Text, true, date.Date + start, date.Date + end);
        DialogResult = true;
    }
}
