using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>Pianifica invio, "Scegli data e ora…": il giorno e l'ora in cui il messaggio deve partire.</summary>
public sealed class ScheduleWindow : Window
{
    readonly DatePicker day = new() { Margin = new Thickness(0, 4, 0, 10), SelectedDate = DateTime.Today.AddDays(1) };
    readonly TextBox time = new() { Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Text = "08:00", Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock error = Dialog.ErrorText();

    public DateTime At { get; private set; }

    public ScheduleWindow()
    {
        Title = "Pianifica invio";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = "Pianifica invio", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Giorno" });
        System.Windows.Automation.AutomationProperties.SetName(day, "Giorno");
        day.DisplayDateStart = DateTime.Today;
        panel.Children.Add(day);
        panel.Children.Add(new TextBlock { Text = "Ora" });
        System.Windows.Automation.AutomationProperties.SetName(time, "Ora");
        panel.Children.Add(time);
        panel.Children.Add(error);
        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        var ok = new Button { Content = "Pianifica", MinWidth = 100, IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) =>
        {
            if (day.SelectedDate is { } date && TimeSpan.TryParseExact(time.Text.Trim().Replace('.', ':'), ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var t)
                && t < TimeSpan.FromDays(1) && date.Date + t > DateTime.Now.AddMinutes(1))
            {
                At = date.Date + t;
                DialogResult = true;
                return;
            }
            error.Text = "Scegli un giorno e un'ora futuri, con l'ora come 8:30.";
            error.Visibility = Visibility.Visible;
        };
        var cancel = new Button { Content = "Annulla", MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(ok, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
    }
}
