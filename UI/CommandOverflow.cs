using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Barra dei comandi che si adatta alla finestra, come nel nuovo Outlook: i pulsanti che non entrano si nascondono,
/// dagli ultimi ai primi, e si ritrovano nel menu del pulsante "..." (Altre opzioni). Le voci del menu prendono il nome
/// da AutomationProperties.Name del pulsante e l'icona dal suo primo glifo, oppure dal Tag (un glifo del catalogo dei
/// menu, come Rispondi a tutti). I separatori sono i figli che non sono pulsanti.
/// </summary>
static class CommandOverflow
{
    /// <summary>Un pulsante che ora non serve (Riquadro di lettura chiaro nel tema chiaro): né nella barra né nel menu.</summary>
    public static readonly DependencyProperty AbsentProperty =
        DependencyProperty.RegisterAttached("Absent", typeof(bool), typeof(CommandOverflow), new PropertyMetadata(false));

    public static bool GetAbsent(DependencyObject d) => (bool)d.GetValue(AbsentProperty);
    public static void SetAbsent(DependencyObject d, bool value) => d.SetValue(AbsentProperty, value);

    /// <summary>Mostra i pulsanti che entrano in <paramref name="width"/>; il pulsante "..." compare se qualcuno resta fuori.</summary>
    public static void Fit(Panel commands, FrameworkElement more, double width)
    {
        foreach (UIElement e in commands.Children) e.Visibility = GetAbsent(e) ? Visibility.Collapsed : Visibility.Visible;
        var items = commands.Children.Cast<UIElement>().Where(e => !GetAbsent(e)).ToList();
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        commands.Measure(infinite);
        double total = items.Sum(e => e.DesiredSize.Width);
        if (total <= width)
        {
            more.Visibility = Visibility.Collapsed;
            return;
        }
        more.Visibility = Visibility.Visible;
        more.Measure(infinite);
        width -= more.DesiredSize.Width;
        for (int i = items.Count - 1; i >= 0 && total > width; i--)
        {
            total -= items[i].DesiredSize.Width;
            items[i].Visibility = Visibility.Collapsed;
            // Un separatore rimasto in fondo se ne va con il pulsante che lo seguiva.
            if (i > 0 && items[i - 1] is not ButtonBase)
            {
                i--;
                total -= items[i].DesiredSize.Width;
                items[i].Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>Il menu "...": i pulsanti nascosti, nello stesso ordine e con gli stessi gruppi della barra.</summary>
    public static void ShowMenu(Panel commands, FrameworkElement more)
    {
        var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
        foreach (var e in commands.Children.Cast<UIElement>().Where(e => e.Visibility != Visibility.Visible && !GetAbsent(e)))
        {
            if (e is not ButtonBase button)
            {
                if (menu.Items.Count > 0 && menu.Items[^1] is not Separator) menu.Items.Add(new Separator());
                continue;
            }
            var item = new MenuItem { Header = AutomationProperties.GetName(button), Icon = IconOf(button), IsEnabled = button.IsEnabled };
            if (button is ToggleButton toggle) item.IsChecked = toggle.IsChecked == true;
            item.Click += (_, _) =>
            {
                if (button is ToggleButton t) t.IsChecked = t.IsChecked != true;
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0 && menu.Items[^1] is Separator last) menu.Items.Remove(last);
        menu.IsOpen = true;
    }

    /// <summary>Dove aprire il menu di un pulsante della barra: il pulsante stesso, o "..." se è finito lì dentro.</summary>
    public static FrameworkElement Anchor(FrameworkElement button, FrameworkElement more) => button.IsVisible ? button : more;

    static FrameworkElement IconOf(ButtonBase button)
    {
        if (button.Tag is string glyph) return ContextMenus.Icon(glyph, null);
        var first = Glyphs(button).FirstOrDefault();
        return ContextMenus.Icon(first?.Text ?? "", first != null && OwnColor(first) ? first.Foreground : null);
    }

    /// <summary>Un glifo con un colore suo (la bandierina rossa, nell'elemento o nel suo stile), non ereditato dal pulsante.</summary>
    static bool OwnColor(TextBlock glyph) =>
        DependencyPropertyHelper.GetValueSource(glyph, TextBlock.ForegroundProperty).BaseValueSource is
            BaseValueSource.Local or BaseValueSource.Style or BaseValueSource.StyleTrigger;

    static IEnumerable<TextBlock> Glyphs(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is TextBlock { FontFamily.Source: var font } t && font.Contains("Fluent Icons")) yield return t;
            foreach (var inner in Glyphs(child)) yield return inner;
        }
    }
}
