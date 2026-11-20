using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>La scheda "Tutti": le viste comuni di tutti gli account e i Preferiti. Porta la somma dei non letti delle Posta in arrivo.</summary>
public sealed class AllAccountsTab : Observable
{
    int unread;
    public int Unread
    {
        get => unread;
        set
        {
            if (unread == value) return;
            unread = value;
            Changed();
            Changed(nameof(TabUnreadText));
            Changed(nameof(TabUnreadVisibility));
            Changed(nameof(TabAutomationName));
        }
    }
    public string TabUnreadText => unread > 0 ? unread.ToString() : "";
    public Visibility TabUnreadVisibility => unread > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string TabTip => T("Tutti gli account");
    public string TabAutomationName => unread > 0 ? T("Tutti gli account") + ", " + AccountItem.UnreadWords(unread) : T("Tutti gli account");
}

/// <summary>Il "+" in fondo alle schede: Aggiungi account. Non si sceglie: apre la finestra e la scheda di prima resta.</summary>
public sealed class AddAccountTab
{
    public string TabTip => T("Aggiungi account");
    public string TabAutomationName => T("Aggiungi account");
}

/// <summary>
/// Le schede in fila, come in un browser: ognuna larga quanto le serve fino a MaxTab; se insieme non ci stanno si
/// stringono le più larghe per prime, tutte fino alla stessa misura, mai sotto MinTab (il nome si accorcia con i puntini).
/// </summary>
public sealed class TabStripPanel : Panel
{
    internal const double MaxTab = 230, MinTab = 56, Gap = 4;

    /// <summary>
    /// Le larghezze delle schede: quelle volute, o, se la somma supera lo spazio, tagliate tutte a una stessa misura (la
    /// più grande che fa stare la somma nello spazio), mai sotto MinTab.
    /// </summary>
    internal static double[] Widths(IReadOnlyList<double> wanted, double available)
    {
        var widths = wanted.Select(w => Math.Min(w, MaxTab)).ToArray();
        double room = available - Gap * Math.Max(0, widths.Length - 1);
        if (double.IsInfinity(available) || widths.Sum() <= room) return widths;
        // La misura del taglio: si cerca fra le larghezze ordinate, dalla più stretta.
        var sorted = widths.OrderBy(w => w).ToArray();
        double used = 0, cap = MinTab;
        for (int i = 0; i < sorted.Length; i++)
        {
            double share = (room - used) / (sorted.Length - i);
            if (sorted[i] > share)
            {
                cap = Math.Max(MinTab, share);
                break;
            }
            used += sorted[i];
            cap = sorted[i];
        }
        return widths.Select(w => Math.Min(w, cap)).ToArray();
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(MaxTab, available.Height));
        var widths = Widths(InternalChildren.Cast<UIElement>().Select(c => c.DesiredSize.Width).ToList(), available.Width);
        double height = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new Size(widths[i], available.Height));
            height = Math.Max(height, InternalChildren[i].DesiredSize.Height);
        }
        return new Size(widths.Sum() + Gap * Math.Max(0, widths.Length - 1), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var widths = Widths(InternalChildren.Cast<UIElement>().Select(c => c.DesiredSize.Width).ToList(), final.Width);
        double x = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Arrange(new Rect(x, 0, widths[i], final.Height));
            x += widths[i] + Gap;
        }
        return final;
    }
}

/// <summary>
/// Gli account come schede (20 novembre 2026, richiesta dell'autore: "vorrei che gli account non fossero più nella barra
/// verticale, ma in una lista orizzontale, tipo tab di un browser", sotto Home e Visualizza e sopra la barra dei comandi).
/// Le schede: Tutti (con due o più account, se Tutti gli account non è spento nelle Impostazioni), una per account
/// nell'ordine di Settings.Accounts, e "+" per aggiungerne uno. A sinistra restano le cartelle della scheda scelta: con
/// Tutti le viste comuni e i Preferiti, con un account le sue cartelle (con lo stato, il rimedio dell'accesso e le
/// risposte automatiche in cima, come prima sotto l'intestazione). Scegliendo una scheda si apre la cartella che lì era
/// aperta per ultima, o la Posta in arrivo; una cartella aperta da altrove (Preferiti, ricerca, notifica, avvio) porta con
/// sé la sua scheda. La finestra di una sola cartella non ha schede.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// L'altezza della barra dei comandi (CommandBar nel XAML) e delle barre di Calendario e Persone: quanto è larga la
    /// colonna di Posta, Calendario e Persone (la prima colonna di MainArea), come chiesto dall'autore il 20 novembre 2026.
    /// </summary>
    internal const double BarHeight = 56;

    readonly ObservableCollection<object> tabs = new();
    readonly AllAccountsTab allTab = new();
    readonly AddAccountTab addTab = new();
    /// <summary>La scheda scelta: allTab o un AccountItem; null finché non ci sono account.</summary>
    object? tab;
    bool selectingTab;
    /// <summary>La cartella aperta per ultima in ogni scheda di account (identificativo dell'account, chiave della cartella).</summary>
    readonly Dictionary<string, string> lastFolderInTab = new();
    UnifiedItem? lastUnified;

    /// <summary>La scheda Tutti c'è con due o più account, se Tutti gli account non è spento (Settings.ShowUnified).</summary>
    bool HasAllTab => only == null && accounts.Count >= 2 && S.ShowUnified;

    /// <summary>Rifà le schede dopo un cambio degli account (elenco, ordine, Tutti gli account acceso o spento).</summary>
    void UpdateTabs()
    {
        if (only != null)
        {
            foreach (var a in accounts) a.InTab = true; // niente schede: il suo account è sempre mostrato
            return;
        }
        AccountTabs.ItemsSource ??= tabs;
        var wanted = new List<object>();
        if (HasAllTab) wanted.Add(allTab);
        wanted.AddRange(accounts);
        wanted.Add(addTab);
        if (!tabs.SequenceEqual(wanted))
        {
            selectingTab = true;
            tabs.Clear();
            foreach (var t in wanted) tabs.Add(t);
            selectingTab = false;
        }
        AccountTabs.Visibility = accounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (tab == null || !tabs.Contains(tab)) tab = unified != null && HasAllTab ? allTab : folder?.Account ?? (object?)accounts.FirstOrDefault();
        ShowTab();
    }

    /// <summary>A sinistra le cartelle della scheda scelta, e la scheda segnata fra le schede.</summary>
    void ShowTab()
    {
        if (only != null) return;
        foreach (var a in accounts)
        {
            a.InTab = a == tab;
            if (a.InTab && !a.IsExpanded) a.IsExpanded = true; // l'intestazione con la freccia non c'è più: le cartelle si vedono sempre
        }
        ShowTabPanels();
        selectingTab = true;
        AccountTabs.SelectedItem = tab;
        selectingTab = false;
    }

    /// <summary>Le viste comuni solo nella scheda Tutti; i Preferiti lì, o in ogni scheda se Tutti non c'è.</summary>
    void ShowTabPanels()
    {
        if (only != null) return;
        bool all = tab == allTab;
        UnifiedPanel.Visibility = all && HasAllTab ? Visibility.Visible : Visibility.Collapsed;
        FavoritesPanel.Visibility = favorites.Count > 0 && (all || !HasAllTab) ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnAccountTabSelected(object sender, SelectionChangedEventArgs e)
    {
        if (selectingTab) return;
        var chosen = AccountTabs.SelectedItem;
        if (chosen == addTab)
        {
            // Il "+" non è una scheda: la scelta torna dov'era e si apre Aggiungi account.
            selectingTab = true;
            AccountTabs.SelectedItem = tab;
            selectingTab = false;
            OnAddAccount(this, new RoutedEventArgs());
            return;
        }
        if (chosen == null || chosen == tab) return;
        OpenTab(chosen);
    }

    /// <summary>La scheda scelta dall'utente: le sue cartelle a sinistra e, nell'elenco, la cartella che aveva aperta lì (o la Posta in arrivo).</summary>
    void OpenTab(object chosen)
    {
        tab = chosen;
        ShowTab();
        if (chosen == allTab) SelectUnified(lastUnified ?? unifiedItems.First(u => u.View == UnifiedView.Inbox));
        else if (chosen is AccountItem account)
        {
            var target = (lastFolderInTab.TryGetValue(account.Provider.Account.Id, out var key) ? account.Folders.FirstOrDefault(f => f.Key == key && !f.IsHidden) : null)
                         ?? account.Folders.FirstOrDefault(f => f.Folder.Kind == FolderKind.Inbox) ?? account.Folders.FirstOrDefault();
            if (target != null) SelectFolder(target);
        }
        Log.Write($"scheda scelta: {(chosen == allTab ? "Tutti" : (chosen as AccountItem)?.LogName)}");
    }

    /// <summary>
    /// Una cartella o una vista aperta (da una scheda, dai Preferiti, dalla ricerca, da una notifica, all'avvio): la scheda la
    /// segue. Un Preferito aperto dalla scheda Tutti resta lì; le viste comuni stanno in Tutti.
    /// </summary>
    void SyncTab(FolderItem? item, bool unifiedView)
    {
        if (only != null) return;
        object? wanted = unifiedView ? (HasAllTab ? allTab : tab)
            : item == null ? tab
            : tab == allTab && favorites.Contains(item) ? allTab
            : item.Account;
        if (item != null && !unifiedView) lastFolderInTab[item.Account.Provider.Account.Id] = item.Key;
        if (wanted == null || wanted == tab) return;
        tab = wanted;
        ShowTab();
    }

    /// <summary>Il menu del tasto destro su una scheda: quello dell'account (Gestisci account, Rimuovi, Sposta); niente su Tutti e sul "+".</summary>
    void OnAccountTabMenu(object sender, ContextMenuEventArgs e)
    {
        var item = e.OriginalSource is DependencyObject d ? ItemsControl.ContainerFromElement(AccountTabs, d) as ListBoxItem : null;
        if (item?.DataContext is not AccountItem account || AccountTabs.ContextMenu is not { } menu)
        {
            e.Handled = true;
            return;
        }
        FillAccountMenu(menu, account);
    }

    /// <summary>
    /// La cornice di vetro in un pezzo solo (Chrome, GlassShape): la barra dei comandi e, sotto a sinistra, la colonna con
    /// Posta, Calendario e Persone e le cartelle affiancate. Dopo ogni disposizione della finestra si ricalcolano i due
    /// rettangoli, sui pixel dello schermo, e si danno alla forma solo se sono cambiati (due confronti: il gestore gira a
    /// ogni disposizione, anche mentre l'elenco scorre). Con le cartelle chiuse o aperte sopra l'elenco la colonna è solo
    /// quella delle icone; in Calendario e Persone la barra dei comandi non c'è e resta la colonna.
    /// </summary>
    void FitChrome()
    {
        if (!Chrome.IsVisible || Chrome.ActualWidth <= 0) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double Snap(double v) => Math.Round(v * scale) / scale;
        Rect bar = Rect.Empty, side = Rect.Empty;
        double rail = double.NaN; // dove finisce la striscia delle icone, se accanto ci sono le cartelle
        if (CommandBar.IsVisible && CommandBar.ActualWidth > 0)
        {
            var at = CommandBar.TranslatePoint(new Point(), Chrome);
            bar = new Rect(new Point(Snap(at.X), Snap(at.Y)), new Point(Snap(at.X + CommandBar.ActualWidth), Snap(at.Y + CommandBar.ActualHeight)));
        }
        if (Rail.IsVisible && Rail.ActualWidth > 0 && MainArea.ActualHeight > 0)
        {
            var area = MainArea.TranslatePoint(new Point(), Chrome);
            // In Calendario e Persone la barra è la loro (Nuovo evento, Nuovo contatto), alta uguale, in cima alla vista.
            if (bar.IsEmpty && (calendarShown || peopleShown))
                bar = new Rect(new Point(Snap(area.X), Snap(area.Y)), new Point(Snap(area.X + MainArea.ActualWidth), Snap(area.Y + BarHeight)));
            double railRight = Rail.TranslatePoint(new Point(Rail.ActualWidth + Rail.Margin.Right, 0), Chrome).X;
            // Tutto sta in un foglio solo, fino al bordo destro: a destra della striscia delle icone, nella posta, le cartelle,
            // l'elenco e il riquadro di lettura (separati da un bordino, FolderDivider, e dallo stacco del riquadro); in
            // Calendario e Persone, dove la barra dei comandi non c'è, la loro vista, e il foglio comincia con lei.
            double top = bar.IsEmpty ? area.Y : bar.Top;
            side = new Rect(new Point(Snap(area.X), Snap(top)), new Point(Snap(area.X + MainArea.ActualWidth), Snap(area.Y + MainArea.ActualHeight)));
            rail = Snap(railRight);
        }
        if (Chrome.Bar != bar) Chrome.Bar = bar;
        if (Chrome.Side != side) Chrome.Side = side;
        if (!Chrome.Rail.Equals(rail)) Chrome.Rail = rail; // Equals: NaN è uguale a NaN
    }

    /// <summary>
    /// Il foro della velatura del fondo attorno ai pulsanti di sistema (GlassBackdrop.Hole): dove li mette Windows
    /// (DWMWA_CAPTION_BUTTON_BOUNDS, relativo alla finestra), nelle coordinate del fondo, fino al suo bordo destro.
    /// </summary>
    void FitBackdrop()
    {
        if (PresentationSource.FromVisual(Backdrop) == null || Backdrop.ActualWidth <= 0) return;
        var handle = new WindowInteropHelper(this).Handle;
        var hole = new Rect(Math.Max(0, Backdrop.ActualWidth - 138), 0, Math.Min(138, Backdrop.ActualWidth), 32); // se Windows non lo dice
        if (handle != IntPtr.Zero && DwmGetWindowAttribute(handle, 5 /* DWMWA_CAPTION_BUTTON_BOUNDS */, out var r, System.Runtime.InteropServices.Marshal.SizeOf<Rect32>()) == 0
            && r.Right > r.Left && GetWindowRect(handle, out var window))
        {
            var topLeft = Backdrop.PointFromScreen(new Point(window.Left + r.Left, window.Top + r.Top));
            var bottomRight = Backdrop.PointFromScreen(new Point(window.Left + r.Right, window.Top + r.Bottom));
            if (bottomRight.X > topLeft.X && bottomRight.Y > 0)
                hole = new Rect(new Point(Math.Max(0, topLeft.X - 4), 0), new Point(Backdrop.ActualWidth, bottomRight.Y + 2));
        }
        Backdrop.Hole = hole;
    }
}
