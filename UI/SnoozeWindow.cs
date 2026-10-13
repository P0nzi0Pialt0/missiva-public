using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Posponi, Scegli una data e un'ora: il giorno dal calendario e l'ora a mezz'ore. La stessa finestra sceglie la
/// scadenza di un contrassegno (solo il giorno, anche oggi) e l'ora del suo promemoria.
/// </summary>
public sealed class SnoozeWindow : Window
{
    static readonly CultureInfo Italian = new("it-IT");

    readonly DatePicker date = new() { SelectedDate = DateTime.Today.AddDays(1), DisplayDateStart = DateTime.Today, Margin = new Thickness(0, 4, 0, 14) };
    readonly ComboBox time = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock error = Dialog.ErrorText();

    public DateTime Until { get; private set; }

    public SnoozeWindow(string title = "Posponi", string heading = "Posponi fino a", string confirm = "Posponi", bool withTime = true)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        for (var t = TimeSpan.Zero; t < TimeSpan.FromDays(1); t += TimeSpan.FromMinutes(30)) time.Items.Add(t.ToString(@"hh\:mm"));
        time.SelectedItem = "08:00";

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Giorno" });
        System.Windows.Automation.AutomationProperties.SetName(date, "Giorno");
        System.Windows.Automation.AutomationProperties.SetName(time, "Ora");
        panel.Children.Add(date);
        if (withTime)
        {
            panel.Children.Add(new TextBlock { Text = "Ora" });
            panel.Children.Add(time);
        }
        else date.SelectedDate = DateTime.Today;
        panel.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        var ok = new Button { Content = confirm, MinWidth = 100, IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) =>
        {
            var until = (date.SelectedDate ?? DateTime.Today) + (withTime ? TimeSpan.Parse((string)time.SelectedItem, CultureInfo.InvariantCulture) : TimeSpan.Zero);
            if (withTime ? until <= DateTime.Now : until < DateTime.Today)
            {
                error.Text = "Scegli un momento nel futuro.";
                error.Visibility = Visibility.Visible;
                return;
            }
            Until = until;
            DialogResult = true;
        };
        var cancel = new Button { Content = "Annulla", MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(ok, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
    }

    /// <summary>Come Outlook scrive l'ora del ritorno: "18:00" oggi, "sab 08:00" nei prossimi giorni.</summary>
    public static string Describe(DateTime until) =>
        until.Date == DateTime.Today ? until.ToString("HH:mm", Italian) :
        until.Date < DateTime.Today.AddDays(7) ? until.ToString("ddd HH:mm", Italian) : until.ToString("d MMM HH:mm", Italian);
}
