using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>Nuova categoria: nome e colore, fra i 25 delle categorie di Outlook.</summary>
public sealed class CategoryWindow : Window
{
    readonly TextBox nameBox = new() { Margin = new Thickness(0, 4, 0, 16) };
    readonly TextBlock error = Dialog.ErrorText();
    readonly List<Border> swatches = new();

    public string CategoryName => nameBox.Text.Trim();
    public int Preset { get; private set; }

    public CategoryWindow(IEnumerable<Category> existing, bool gmail)
    {
        Title = "Nuova categoria";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        // Il primo colore vivace non ancora usato.
        var used = existing.Select(c => c.Preset).ToHashSet();
        Preset = Enumerable.Range(0, 10).FirstOrDefault(i => !used.Contains(i));

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = "Nuova categoria", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var note = new TextBlock
        {
            Text = gmail
                ? "In Gmail una categoria è un'etichetta: compare anche fra le cartelle, sul web e sul telefono. Il colore lo tiene Missiva."
                : "È una categoria di Outlook: la vedi anche nel nuovo Outlook e sul web.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14),
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(note);
        panel.Children.Add(new TextBlock { Text = "Nome" });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "Colore", Margin = new Thickness(0, 0, 0, 6) });

        var palette = new WrapPanel();
        for (int i = 0; i < CategoryPalette.Presets.Length; i++)
        {
            int preset = i;
            var swatch = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(CategoryPalette.Hex(i))),
                ToolTip = CategoryPalette.Presets[i].Name,
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "TextFillColorPrimaryBrush");
            swatch.MouseLeftButtonUp += (_, _) => Select(preset);
            swatches.Add(swatch);
            palette.Children.Add(swatch);
        }
        panel.Children.Add(palette);
        panel.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        var create = new Button { Content = "Crea", MinWidth = 100, IsDefault = true };
        create.SetResourceReference(StyleProperty, "AccentButtonStyle");
        create.Click += (_, _) =>
        {
            if (CategoryName.Length == 0)
            {
                error.Text = "Scrivi il nome della categoria.";
                error.Visibility = Visibility.Visible;
                return;
            }
            if (existing.Any(c => c.Name.Equals(CategoryName, StringComparison.OrdinalIgnoreCase)))
            {
                error.Text = $"La categoria \"{CategoryName}\" c'è già.";
                error.Visibility = Visibility.Visible;
                return;
            }
            DialogResult = true;
        };
        var cancel = new Button { Content = "Annulla", MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(create, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(create);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        Select(Preset);
        Loaded += (_, _) => nameBox.Focus();
    }

    void Select(int preset)
    {
        Preset = preset;
        for (int i = 0; i < swatches.Count; i++) swatches[i].BorderThickness = new Thickness(i == preset ? 3 : 0);
    }
}
