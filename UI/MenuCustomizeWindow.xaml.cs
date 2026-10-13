using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Personalizza un menu del tasto destro (messaggi o cartelle): quali voci mostrare e in che ordine, con i separatori
/// fra i gruppi. Ogni modifica si salva subito e vale dal prossimo menu che si apre.
/// </summary>
public partial class MenuCustomizeWindow : Window
{
    public sealed class Row : Observable
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public FrameworkElement? Icon { get; init; }
        public bool IsSeparator => Key == ContextMenus.Separator;
        public Visibility EntryVisibility => IsSeparator ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SeparatorVisibility => IsSeparator ? Visibility.Visible : Visibility.Collapsed;

        bool shown = true;
        public bool Shown
        {
            get => shown;
            set { shown = value; Changed(); }
        }

        /// <summary>Linea che indica dove finirà la voce trascinata: sopra (-1), sotto (1) o nessuna (0).</summary>
        public int DropSide
        {
            get => dropSide;
            set
            {
                if (dropSide == value) return;
                dropSide = value;
                Changed(nameof(DropLine));
            }
        }
        int dropSide;
        public Thickness DropLine => dropSide switch { < 0 => new(0, 2, 0, 0), > 0 => new(0, 0, 0, 2), _ => new(0) };
    }

    readonly ContextMenus.Definition menu;
    readonly ObservableCollection<Row> rows = new();
    Point? dragStart;
    Row? dragRow;

    public MenuCustomizeWindow(ContextMenus.Definition menu)
    {
        this.menu = menu;
        InitializeComponent();
        Heading.Text = menu.Title;
        Explanation.Text = menu.Description;
        Entries.ItemsSource = rows;
        Load();
        // Le icone colorate hanno i colori del tema: al cambio di tema di Windows si rifanno.
        App.ThemeChanged += Load;
        Closed += (_, _) => App.ThemeChanged -= Load;
    }

    void Load()
    {
        rows.Clear();
        var hidden = menu.Hidden();
        foreach (var key in menu.Order())
        {
            if (key == ContextMenus.Separator)
            {
                rows.Add(new Row { Key = key });
                continue;
            }
            var entry = menu.Find(key)!;
            rows.Add(new Row { Key = key, Name = entry.Name, Icon = ContextMenus.Icon(entry.Glyph, ContextMenus.Brush(entry.Tone)), Shown = !hidden.Contains(key) });
        }
        UpdateButtons();
    }

    void Save() => menu.Save(rows.Select(r => r.Key), rows.Where(r => !r.IsSeparator && !r.Shown).Select(r => r.Key));

    Row? Current => Entries.SelectedItem as Row;

    void Move(Row row, int to)
    {
        int from = rows.IndexOf(row);
        to = Math.Clamp(to, 0, rows.Count - 1);
        if (from < 0 || from == to) return;
        rows.Move(from, to);
        Entries.SelectedItem = row;
        Entries.ScrollIntoView(row);
        Save();
        UpdateButtons();
    }

    void OnShownClick(object sender, RoutedEventArgs e) => Save();

    void OnUp(object sender, RoutedEventArgs e)
    {
        if (Current is { } r) Move(r, rows.IndexOf(r) - 1);
    }

    void OnDown(object sender, RoutedEventArgs e)
    {
        if (Current is { } r) Move(r, rows.IndexOf(r) + 1);
    }

    void OnAddSeparator(object sender, RoutedEventArgs e)
    {
        var row = new Row { Key = ContextMenus.Separator };
        rows.Insert(Current is { } r ? rows.IndexOf(r) + 1 : rows.Count, row);
        Entries.SelectedItem = row;
        Save();
        UpdateButtons();
    }

    void OnRemoveSeparator(object sender, RoutedEventArgs e)
    {
        if (Current is not { IsSeparator: true } r) return;
        int i = rows.IndexOf(r);
        rows.Remove(r);
        if (rows.Count > 0) Entries.SelectedIndex = Math.Min(i, rows.Count - 1);
        Save();
        UpdateButtons();
    }

    void OnReset(object sender, RoutedEventArgs e)
    {
        menu.Reset();
        Load();
    }

    void OnSelected(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    void UpdateButtons()
    {
        int i = Current is { } r ? rows.IndexOf(r) : -1;
        UpButton.IsEnabled = i > 0;
        DownButton.IsEnabled = i >= 0 && i < rows.Count - 1;
        RemoveButton.IsEnabled = Current?.IsSeparator == true;
    }

    /// <summary>Alt+Su e Alt+Giù spostano la voce selezionata, Spazio la accende o la spegne, Canc toglie un separatore.</summary>
    void OnKey(object sender, KeyEventArgs e)
    {
        if (Current is not { } r) return;
        if (e.Key == Key.System && e.SystemKey is Key.Up or Key.Down)
        {
            Move(r, rows.IndexOf(r) + (e.SystemKey == Key.Up ? -1 : 1));
            e.Handled = true;
        }
        else if (e.Key == Key.Space && !r.IsSeparator)
        {
            r.Shown = !r.Shown;
            Save();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && r.IsSeparator)
        {
            OnRemoveSeparator(sender, e);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- trascinamento

    Row? RowAt(object source) =>
        source is DependencyObject d && ItemsControl.ContainerFromElement(Entries, d) is ListBoxItem { DataContext: Row r } ? r : null;

    static bool InsideCheckBox(object source)
    {
        for (var d = source as DependencyObject; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is CheckBox) return true;
        return false;
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Dalla casella non si trascina: accende e spegne la voce.
        dragRow = InsideCheckBox(e.OriginalSource) ? null : RowAt(e.OriginalSource);
        dragStart = e.GetPosition(this);
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragRow is not { } row || dragStart is not { } start) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragRow = null;
        dragStart = null;
        DragDrop.DoDragDrop(Entries, new DataObject(typeof(Row), row), DragDropEffects.Move);
        foreach (var r in rows) r.DropSide = 0;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(Row)) is not Row dragged) return;
        var target = RowAt(e.OriginalSource);
        foreach (var r in rows.Where(r => r != target)) r.DropSide = 0;
        if (target == null || target == dragged || Entries.ItemContainerGenerator.ContainerFromItem(target) is not ListBoxItem container) return;
        target.DropSide = e.GetPosition(container).Y > container.ActualHeight / 2 ? 1 : -1;
        e.Effects = DragDropEffects.Move;
    }

    void OnDragLeave(object sender, DragEventArgs e)
    {
        foreach (var r in rows) r.DropSide = 0;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(Row)) is not Row dragged || rows.FirstOrDefault(r => r.DropSide != 0) is not { } target) return;
        bool after = target.DropSide > 0;
        foreach (var r in rows) r.DropSide = 0;
        int from = rows.IndexOf(dragged), to = rows.IndexOf(target) + (after ? 1 : 0);
        if (from < to) to--;
        Move(dragged, to);
    }
}
