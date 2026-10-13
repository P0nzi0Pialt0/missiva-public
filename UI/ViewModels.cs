using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Missiva.UI;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AccountItem : Observable
{
    public AccountItem(IMailProvider provider)
    {
        Provider = provider;
        expanded = !Store.Settings.CollapsedAccounts.Contains(provider.Account.Id);
    }

    public IMailProvider Provider { get; }

    bool expanded;
    public bool IsExpanded
    {
        get => expanded;
        set
        {
            expanded = value;
            Store.Settings.CollapsedAccounts.Remove(Provider.Account.Id);
            if (!value) Store.Settings.CollapsedAccounts.Add(Provider.Account.Id);
            Store.SaveSoon();
            Changed();
            Changed(nameof(FoldersVisibility));
            Changed(nameof(HiddenToggleVisibility));
            Changed(nameof(Chevron));
            Changed(nameof(HeaderSelectedVisibility));
            NotifyHeaderUnread();
        }
    }

    bool hasSelection;
    /// <summary>La cartella aperta e' di questo account (la segna SidebarMarks; falso durante la ricerca e nelle viste di tutti gli account).</summary>
    public bool HasSelection
    {
        get => hasSelection;
        set
        {
            if (hasSelection == value) return;
            hasSelection = value;
            Changed(nameof(HeaderSelectedVisibility));
        }
    }

    /// <summary>
    /// L'intestazione si evidenzia come una cartella scelta solo con l'account chiuso (le cartelle non si vedono): aperto lo e' la
    /// cartella, niente doppio segno.
    /// </summary>
    public bool HeaderSelected => hasSelection && !expanded;
    public Visibility HeaderSelectedVisibility => HeaderSelected ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>I non letti della Posta in arrivo dell'account: gli stessi del numero sull'icona e di "Tutti gli account".</summary>
    public int InboxUnread => Folders.Where(f => f.Folder.Kind == FolderKind.Inbox).Sum(f => f.Unread);

    /// <summary>
    /// Con l'account chiuso le cartelle non si vedono, e con loro il numero della Posta in arrivo: l'intestazione lo porta
    /// a destra, come il gruppo chiuso di Outlook. Aperto lo mostra la cartella, niente doppio numero.
    /// </summary>
    public string HeaderUnreadText => !expanded && InboxUnread > 0 ? InboxUnread.ToString() : "";
    public Visibility HeaderUnreadVisibility => HeaderUnreadText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Il nome per UI Automation e l'Assistente vocale: "indirizzo, 3 non letti" solo con il numero in vista.</summary>
    public string HeaderAutomationName => HeaderUnreadText.Length == 0 ? HeaderTip : $"{HeaderTip}, {InboxUnread} {(InboxUnread == 1 ? "non letto" : "non letti")}";

    int shownInboxUnread = -1;

    /// <summary>Chiamata dalle cartelle quando cambiano i non letti di una Posta in arrivo (FolderItem.Unread e Update).</summary>
    internal void InboxUnreadChanged()
    {
        int now = InboxUnread;
        if (now == shownInboxUnread) return;
        shownInboxUnread = now;
        NotifyHeaderUnread();
    }

    /// <summary>
    /// Messaggi diventati letti (o da leggere): i non letti di ogni loro cartella cambiano una volta sola, con il totale.
    /// Prima uno per messaggio: 50 messaggi erano 50 cambi, e per la Posta in arrivo 50 volte icona, area di notifica e
    /// registro (audit, v2 V-05).
    /// </summary>
    public void CountRead(IEnumerable<MessageSummary> changed, bool read)
    {
        foreach (var inFolder in changed.GroupBy(m => m.FolderId))
            if (Folders.FirstOrDefault(f => f.Folder.Id == inFolder.Key) is { } where)
                where.SetUnread(where.Unread + (read ? -inFolder.Count() : inFolder.Count()), read ? "segnati letti" : "segnati da leggere");
    }

    /// <summary>
    /// L'account nel registro senza l'indirizzo: il tipo e il suo posto fra gli account ("Gmail 2", "Outlook 3", "IMAP 4"),
    /// per le righe che dicono chi ha cambiato i non letti e dove sono entrate le righe nuove.
    /// </summary>
    public string LogName
    {
        get
        {
            var account = Provider.Account;
            string kind = account.Kind switch { AccountKind.Microsoft => "Outlook", AccountKind.Gmail => "Gmail", _ => "IMAP" };
            return $"{kind} {Store.Settings.Accounts.FindIndex(a => a.Id == account.Id) + 1}";
        }
    }

    void NotifyHeaderUnread()
    {
        Changed(nameof(InboxUnread));
        Changed(nameof(HeaderUnreadText));
        Changed(nameof(HeaderUnreadVisibility));
        Changed(nameof(HeaderAutomationName));
    }

    public Visibility FoldersVisibility => expanded ? Visibility.Visible : Visibility.Collapsed;
    public string Chevron => expanded ? "\uE70D" : "\uE76C";
    public string Header => Provider.Account.Email;

    /// <summary>Linea che indica dove finir\u00E0 l'account trascinato: sopra (-1), sotto (1) o nessuna (0), come per le cartelle.</summary>
    public int DropSide
    {
        get => dropSide;
        set
        {
            if (dropSide == value) return;
            dropSide = value;
            Changed();
            Changed(nameof(DropLine));
        }
    }
    int dropSide;
    public Thickness DropLine => dropSide switch { < 0 => new(0, 2, 0, 0), > 0 => new(0, 0, 0, 2), _ => new(0) };

    /// <summary>
    /// Il nome breve dell'account sulle righe delle viste di tutti gli account: la parte prima della @, se nessun altro
    /// account ce l'ha uguale; altrimenti l'indirizzo intero.
    /// </summary>
    public string ShortName
    {
        get
        {
            var email = Provider.Account.Email;
            int at = email.IndexOf('@');
            if (at <= 0) return email;
            var local = email[..at];
            return Store.Settings.Accounts.Count(a => a.Email.StartsWith(local + "@", StringComparison.OrdinalIgnoreCase)) > 1 ? email : local;
        }
    }

    /// <summary>Il colore dell'account nelle viste di tutti gli account (righe e riquadro), dal suo posto fra gli account.</summary>
    public Brush Brush => brush ??= Frozen(Avatars.AccountColor(Provider.Account));
    Brush? brush;

    static Brush Frozen(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
    /// <summary>Le cartelle nell'ordine mostrato: ogni cartella seguita dalle sue sottocartelle.</summary>
    public ObservableCollection<FolderItem> Folders { get; } = new();
    /// <summary>Categorie dell'account (etichette di Gmail o categorie di Outlook), per il menu Categorizza.</summary>
    public List<Category> Categories { get; set; } = new();

    string status = "Caricamento…";
    public string Status { get => status; set { status = value; Changed(); Changed(nameof(StatusVisibility)); Changed(nameof(StatusLine)); } }
    public Visibility StatusVisibility => (status.Length > 0 || queued > 0) && fix == AccountFix.None ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// L'ultima lettura delle cartelle dal server non è riuscita (MainWindow.LoadFoldersAsync), per la rete o per altro
    /// ("Non raggiungibile: ..."): al primo segno che il server risponde si rileggono (MainWindow.NoteAccountState).
    /// </summary>
    public bool FoldersFailed { get; set; }

    int queued;
    /// <summary>Le azioni fatte senza rete che aspettano di ripartire (ActionQueue) e i messaggi nella Posta in uscita di Missiva.</summary>
    public int Queued { get => queued; set { queued = value; Changed(nameof(StatusLine)); Changed(nameof(StatusVisibility)); } }
    /// <summary>La riga sotto l'account: lo stato (Non in linea...) e, se ce ne sono, le azioni in attesa.</summary>
    public string StatusLine => string.Join(" · ", new[] { status, queued == 0 ? "" : queued == 1 ? "1 azione in attesa" : $"{queued} azioni in attesa" }.Where(t => t.Length > 0));

    AccountFix fix;
    /// <summary>
    /// Il server ha rifiutato la password, o Microsoft o Google chiedono un nuovo accesso: sotto l'account una barra con
    /// "Aggiorna password" o "Accedi di nuovo", che apre Gestisci account. Nessuna finestra si apre da sola.
    /// </summary>
    public AccountFix Fix
    {
        get => fix;
        set
        {
            fix = value;
            Changed();
            Changed(nameof(FixVisibility));
            Changed(nameof(FixText));
            Changed(nameof(FixMessage));
            Changed(nameof(StatusVisibility));
        }
    }
    public Visibility FixVisibility => fix != AccountFix.None ? Visibility.Visible : Visibility.Collapsed;
    public string FixText => fix switch
    {
        AccountFix.Password => "Aggiorna password",
        AccountFix.Consent => "Consenti l'accesso alle caselle condivise",
        _ => "Accedi di nuovo",
    };
    public string FixMessage => fix switch
    {
        // "non si legge più": la password o l'accesso salvati che non si decifrano (ImapProvider.CredentialsOf, GoogleAuth.TokenAsync).
        // "manca": un account arrivato con una copia di sicurezza, che non porta gli accessi (Backup).
        AccountFix.Password => status.Contains("non si legge più") ? "La password salvata non si legge più su questo PC."
            : status.Contains("manca la password") ? "Su questo PC manca la password dell'account." : "Password non accettata dal server.",
        AccountFix.Consent => "Missiva non ha ancora il permesso di aprire le caselle condivise. La finestra di Microsoft si apre solo premendo il pulsante.",
        _ => status.Contains("non si legge più") ? "L'accesso salvato non si legge più su questo PC."
            : status.Contains("manca l'accesso") ? "Su questo PC manca l'accesso all'account." : "L'account chiede un nuovo accesso.",
    };

    /// <summary>Una casella condivisa di Microsoft 365 (info@, segreteria@), aperta con un account di lavoro.</summary>
    public bool IsShared => Provider.Account.SharedOf != null;
    public Visibility SharedVisibility => IsShared ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>Il suggerimento dell'intestazione: l'indirizzo e, per una casella condivisa, con quale account si apre.</summary>
    public string HeaderTip => IsShared && Store.Settings.Accounts.FirstOrDefault(a => a.Id == Provider.Account.SharedOf) is { } owner
        ? $"{Header}: casella condivisa, aperta con {owner.Email}"
        : Header;

    AutoReplies? autoReplies;
    /// <summary>
    /// Le risposte automatiche di Outlook, lette solo se il consenso alle impostazioni della casella c'è già: attive o
    /// programmate, sotto l'account una barra con Disattiva e Modifica, come l'avviso del nuovo Outlook.
    /// </summary>
    public AutoReplies? AutoReplies
    {
        get => autoReplies;
        set
        {
            autoReplies = value;
            RefreshAutoReplies();
        }
    }
    /// <summary>La barra segue l'ora: il periodo comincia o finisce anche senza leggere di nuovo.</summary>
    public void RefreshAutoReplies()
    {
        Changed(nameof(AutoReplies));
        Changed(nameof(AutoReplyVisibility));
        Changed(nameof(AutoReplyText));
    }
    public Visibility AutoReplyVisibility =>
        autoReplies is { } r && (r.ActiveAt(DateTime.Now) || r.PlannedAfter(DateTime.Now)) ? Visibility.Visible : Visibility.Collapsed;
    public string AutoReplyText => autoReplies?.Describe(DateTime.Now) ?? "";

    string Id => Provider.Account.Id;

    /// <summary>
    /// Cartelle dalla copia locale o dal server: si costruisce l'albero e si ordina secondo le scelte dell'utente.
    /// Le cartelle già presenti restano gli stessi elementi (si aggiornano nome e non letti), così la selezione resta.
    /// </summary>
    public void SetFolders(IEnumerable<MailFolder> folders)
    {
        var existing = Folders.GroupBy(f => f.Folder.Id).ToDictionary(g => g.Key, g => g.First());
        var items = folders.Select((f, i) =>
        {
            var item = existing.TryGetValue(f.Id, out var old) ? old : new FolderItem(this, f);
            item.Update(f, i);
            return item;
        }).ToList();
        var byId = items.GroupBy(f => f.Folder.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var f in items)
            f.Parent = f.Folder.ParentId != null && byId.TryGetValue(f.Folder.ParentId, out var p) && p != f ? p : null;
        foreach (var f in items)
        {
            f.HasChildren = items.Any(c => c.Parent == f);
            f.expanded = !Store.Settings.CollapsedFolders.Contains(Id + "|" + f.Folder.Id);
        }
        var flat = Arrange(items);
        for (int i = Folders.Count - 1; i >= 0; i--)
            if (!flat.Contains(Folders[i])) Folders.RemoveAt(i);
        for (int i = 0; i < flat.Count; i++)
        {
            int current = Folders.IndexOf(flat[i]);
            if (current < 0) Folders.Insert(i, flat[i]);
            else if (current != i) Folders.Move(current, i);
        }
        UpdateVisibility();
        InboxUnreadChanged(); // le cartelle nuove entrano dopo il loro Update, e quelle tolte non contano piu'
    }

    /// <summary>
    /// Le cartelle rilette dal server, chieste con quel biglietto (UnreadSync.Ticket): arrivano con i loro non letti, e se
    /// intanto qui sono cambiati (una lettura, una scrittura in viaggio) quelli delle Posta in arrivo restano i nostri.
    /// Falso in quel caso: chi chiama li fa ricontare.
    /// </summary>
    public bool SetServerFolders(List<MailFolder> folders, long? ticket)
    {
        bool superseded = Folders.Count > 0 && !UnreadSync.Valid(Id, ticket);
        if (superseded)
            foreach (var f in folders.Where(f => f.Kind == FolderKind.Inbox))
                if (Folders.FirstOrDefault(x => x.Folder.Id == f.Id) is { } shown) f.Unread = shown.Unread;
        SetFolders(folders);
        return !superseded;
    }

    /// <summary>Le cartelle come sono ora, con i non letti aggiornati, per la copia locale.</summary>
    public List<MailFolder> Snapshot() => Folders.OrderBy(f => f.ServerIndex).Select(f => f.Folder).ToList();

    /// <summary>
    /// Ordine: per ogni gruppo di cartelle sorelle vale la posizione salvata trascinandole; quelle nuove seguono
    /// nell'ordine del server (prima le cartelle di sistema, poi le altre in ordine alfabetico).
    /// </summary>
    List<FolderItem> Arrange(IEnumerable<FolderItem> items)
    {
        var all = items.OrderBy(f => f.ServerIndex).ToList();
        var saved = Store.Settings.FolderOrder.GetValueOrDefault(Id) ?? new List<string>();
        var position = new Dictionary<string, int>();
        for (int i = 0; i < saved.Count; i++) position.TryAdd(saved[i], i);
        IEnumerable<FolderItem> Sorted(IEnumerable<FolderItem> siblings) =>
            siblings.OrderBy(f => position.GetValueOrDefault(f.Folder.Id, int.MaxValue)).ThenBy(f => f.ServerIndex);
        var flat = new List<FolderItem>();
        void Add(FolderItem f, int depth)
        {
            f.Depth = depth;
            flat.Add(f);
            foreach (var c in Sorted(all.Where(c => c.Parent == f))) Add(c, depth + 1);
        }
        foreach (var root in Sorted(all.Where(f => f.Parent == null))) Add(root, 0);
        return flat;
    }

    /// <summary>Riordina l'elenco sul posto: gli elementi restano gli stessi, quindi la selezione non si perde.</summary>
    void Rearrange()
    {
        var flat = Arrange(Folders);
        for (int i = 0; i < flat.Count; i++)
        {
            int current = Folders.IndexOf(flat[i]);
            if (current != i) Folders.Move(current, i);
        }
        UpdateVisibility();
    }

    /// <summary>Sposta una cartella prima o dopo una sorella (stessa cartella madre).</summary>
    public bool Move(FolderItem item, FolderItem target, bool after)
    {
        if (item == target || item.Parent != target.Parent) return false;
        var order = Folders.Select(f => f.Folder.Id).ToList();
        order.Remove(item.Folder.Id);
        int t = order.IndexOf(target.Folder.Id);
        order.Insert(after ? t + 1 : t, item.Folder.Id);
        Store.Settings.FolderOrder[Id] = order;
        Store.SaveSoon();
        Rearrange();
        return true;
    }

    /// <summary>Su o giù di un posto fra le sorelle (dal menu, senza trascinare).</summary>
    public void Step(FolderItem item, int direction)
    {
        var siblings = Folders.Where(f => f.Parent == item.Parent).ToList();
        int i = siblings.IndexOf(item) + direction;
        if (i >= 0 && i < siblings.Count) Move(item, siblings[i], after: direction > 0);
    }

    public bool HasCustomOrder => Store.Settings.FolderOrder.ContainsKey(Id);

    public void ResetOrder()
    {
        Store.Settings.FolderOrder.Remove(Id);
        Store.SaveSoon();
        Rearrange();
    }

    public void Toggle(FolderItem item)
    {
        item.expanded = !item.expanded;
        var key = Id + "|" + item.Folder.Id;
        Store.Settings.CollapsedFolders.Remove(key);
        if (!item.expanded) Store.Settings.CollapsedFolders.Add(key);
        Store.SaveSoon();
        UpdateVisibility();
    }

    // ---------------------------------------------------------------- cartelle nascoste

    bool showHidden;

    bool IsHidden(FolderItem f) => Store.Settings.HiddenFolders.Contains(Id + "|" + f.Folder.Id);

    public void SetHidden(FolderItem item, bool hidden)
    {
        var key = Id + "|" + item.Folder.Id;
        Store.Settings.HiddenFolders.Remove(key);
        if (hidden) Store.Settings.HiddenFolders.Add(key);
        Store.SaveSoon();
        UpdateVisibility();
    }

    public void ToggleShowHidden()
    {
        showHidden = !showHidden;
        UpdateVisibility();
    }

    public int HiddenCount => Folders.Count(IsHidden);
    public Visibility HiddenToggleVisibility => expanded && HiddenCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string HiddenToggleText => showHidden
        ? "Nascondi di nuovo le cartelle nascoste"
        : HiddenCount == 1 ? "Mostra 1 cartella nascosta" : $"Mostra {HiddenCount} cartelle nascoste";

    /// <summary>Una cartella si vede se le madri sono aperte e né lei né una madre è nascosta (salvo "Mostra").</summary>
    void UpdateVisibility()
    {
        foreach (var f in Folders)
        {
            bool hidden = false, visible = true;
            for (var p = f; p != null; p = p.Parent)
            {
                hidden |= IsHidden(p);
                if (p != f) visible &= p.expanded;
            }
            f.Refresh(visible && (showHidden || !hidden), hidden);
        }
        Changed(nameof(HiddenToggleVisibility));
        Changed(nameof(HiddenToggleText));
    }
}

/// <summary>Le viste di tutti gli account (con due o più account), in cima alle cartelle come in Thunderbird e Apple Mail.</summary>
public enum UnifiedView { Inbox, Unread, Flagged, Drafts, Sent }

/// <summary>Una riga di "Tutti gli account": le Posta in arrivo di tutti, i loro non letti o contrassegnati, Bozze e Posta inviata.</summary>
public sealed class UnifiedItem(UnifiedView view) : Observable
{
    public UnifiedView View { get; } = view;

    public string Name => View switch
    {
        UnifiedView.Inbox => "Posta in arrivo",
        UnifiedView.Unread => "Non letti",
        UnifiedView.Flagged => "Contrassegnati",
        UnifiedView.Drafts => "Bozze",
        _ => "Posta inviata",
    };

    public string Glyph => View switch
    {
        UnifiedView.Inbox => "",
        UnifiedView.Unread => "",
        UnifiedView.Flagged => "",
        UnifiedView.Drafts => "",
        _ => "",
    };

    public string Tip => View switch
    {
        UnifiedView.Inbox => "La Posta in arrivo di tutti gli account, insieme",
        UnifiedView.Unread => "I messaggi da leggere nella Posta in arrivo di tutti gli account",
        UnifiedView.Flagged => "I messaggi contrassegnati nella Posta in arrivo di tutti gli account",
        UnifiedView.Drafts => "Le Bozze di tutti gli account",
        _ => "La Posta inviata di tutti gli account",
    };

    public string AutomationName => Name + " di tutti gli account";
    public override string ToString() => AutomationName;

    /// <summary>La cartella di ogni account da cui vengono i messaggi.</summary>
    public FolderKind Kind => View switch { UnifiedView.Drafts => FolderKind.Drafts, UnifiedView.Sent => FolderKind.Sent, _ => FolderKind.Inbox };

    /// <summary>Il filtro chiesto a ogni account (Non letti e Contrassegnati filtrano la Posta in arrivo).</summary>
    public ListOptions Options => View switch
    {
        UnifiedView.Unread => ListOptions.Default with { Filter = MessageFilter.Unread },
        UnifiedView.Flagged => ListOptions.Default with { Filter = MessageFilter.Flagged },
        _ => ListOptions.Default,
    };

    int unread;
    /// <summary>Per Posta in arrivo e Non letti la somma dei non letti delle Posta in arrivo, come il numero sull'icona.</summary>
    public int Unread
    {
        get => unread;
        set
        {
            if (unread == value) return;
            unread = value;
            Changed();
            Changed(nameof(UnreadText));
            Changed(nameof(Weight));
        }
    }
    public string UnreadText => unread > 0 ? unread.ToString() : "";
    public FontWeight Weight => unread > 0 ? FontWeights.SemiBold : FontWeights.Normal;
}

public sealed class FolderItem : Observable
{
    public FolderItem(AccountItem account, MailFolder folder)
    {
        Account = account;
        Folder = folder;
    }

    public AccountItem Account { get; }
    public MailFolder Folder { get; private set; }
    public FolderItem? Parent { get; set; }
    public bool HasChildren { get; set; }
    public int Depth { get; set; }
    /// <summary>Posizione nell'ordine del server, per le cartelle nuove e per tornare all'ordine predefinito.</summary>
    public int ServerIndex { get; private set; }

    /// <summary>Dati nuovi per la stessa cartella (dal server dopo la copia locale).</summary>
    internal void Update(MailFolder folder, int serverIndex)
    {
        int before = Folder.Unread;
        bool same = ReferenceEquals(Folder, folder);
        Folder = folder;
        ServerIndex = serverIndex;
        if (!same) LogUnread(before, "cartelle rilette");
        Changed(nameof(Name));
        Changed(nameof(Glyph));
        Changed(nameof(Unread));
        Changed(nameof(UnreadText));
        Changed(nameof(Weight));
        if (Folder.Kind == FolderKind.Inbox) Account.InboxUnreadChanged();
    }
    internal bool expanded = true;
    bool visible = true;

    public string Name => Folder.Name;
    public string Key => Account.Provider.Account.Id + "|" + Folder.Id;

    /// <summary>Colore dell'icona scelto con Cambia colore, o quello del testo.</summary>
    public Brush GlyphBrush
    {
        get
        {
            if (!Store.Settings.FolderColors.TryGetValue(Key, out var preset))
                return (Brush)(Application.Current.TryFindResource("TextFillColorPrimaryBrush") ?? Brushes.Black);
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(CategoryPalette.Hex(preset)));
            brush.Freeze();
            return brush;
        }
    }

    public void RefreshColor() => Changed(nameof(GlyphBrush));

    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    public Visibility RowVisibility => visible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ChevronVisibility => HasChildren ? Visibility.Visible : Visibility.Hidden;
    public string ChevronGlyph => expanded ? "" : "";

    /// <summary>Linea che indica dove finirà la cartella trascinata: sopra (-1), sotto (1) o nessuna (0).</summary>
    public int DropSide
    {
        get => dropSide;
        set
        {
            if (dropSide == value) return;
            dropSide = value;
            Changed();
            Changed(nameof(DropLine));
        }
    }
    int dropSide;
    public Thickness DropLine => dropSide switch { < 0 => new(0, 2, 0, 0), > 0 => new(0, 0, 0, 2), _ => new(0) };

    /// <summary>Messaggi trascinati sopra la cartella: si evidenzia, come in Outlook, dove finiranno.</summary>
    public bool IsDropTarget
    {
        get => dropTarget;
        set
        {
            if (dropTarget == value) return;
            dropTarget = value;
            Changed();
        }
    }
    bool dropTarget;

    /// <summary>Nascosta dall'elenco, o dentro una cartella nascosta.</summary>
    public bool IsHidden { get; private set; }
    /// <summary>Le cartelle nascoste, quando si mostrano, sono attenuate.</summary>
    public double RowOpacity => IsHidden ? 0.55 : 1;

    internal void Refresh(bool isVisible, bool isHidden)
    {
        visible = isVisible;
        IsHidden = isHidden;
        Changed(nameof(RowOpacity));
        Changed(nameof(RowVisibility));
        Changed(nameof(Indent));
        Changed(nameof(ChevronVisibility));
        Changed(nameof(ChevronGlyph));
    }
    public string Glyph => Folder.Kind switch
    {
        FolderKind.Inbox => "\uE715",
        FolderKind.Drafts => "\uE70F",
        FolderKind.Sent => "\uE724",
        FolderKind.Deleted => "\uE74D",
        FolderKind.Archive => "\uE7B8",
        FolderKind.Junk => "\uE733",
        _ => "\uE8B7",
    };

    public int Unread
    {
        get => Folder.Unread;
        set { Folder.Unread = Math.Max(0, value); Changed(); Changed(nameof(UnreadText)); Changed(nameof(Weight)); if (Folder.Kind == FolderKind.Inbox) Account.InboxUnreadChanged(); }
    }

    /// <summary>
    /// I non letti cambiati, con chi li ha cambiati: per la Posta in arrivo una riga nel registro, anche nella versione
    /// installata ("non letti di Gmail 2: 2 -> 1 (letto nel riquadro)"), senza indirizzi. Il 7 ottobre 2026 il numero
    /// sull'icona era risalito da solo dopo una lettura e il registro non diceva chi l'aveva scritto.
    /// </summary>
    internal void SetUnread(int value, string who)
    {
        int before = Folder.Unread;
        // Cambiati qui: un conteggio della Posta in arrivo chiesto al server prima di adesso non vale più (UnreadSync).
        if (Folder.Kind == FolderKind.Inbox) UnreadSync.Edited(Account.Provider.Account.Id);
        Unread = value;
        LogUnread(before, who);
    }

    /// <summary>
    /// I non letti contati dal server, chiesti con quel biglietto (UnreadSync.Ticket): si scrivono solo se da allora Missiva
    /// non li ha cambiati né ha scritture in viaggio. Falso se il numero è superato: resta quello di qui, e chi chiama riconta.
    /// </summary>
    internal bool ApplyServerUnread(int unread, long? ticket, string who)
    {
        if (unread == Folder.Unread) return true; // lo stesso numero di qui: niente da scrivere né da ricontare
        if (!UnreadSync.Valid(Account.Provider.Account.Id, ticket))
        {
            Log.Write($"non letti di {Account.LogName}: {unread} dal server scartati, cambiati qui nel frattempo (restano {Folder.Unread}): si riconta");
            return false;
        }
        int before = Folder.Unread;
        Unread = unread;
        LogUnread(before, who);
        return true;
    }

    void LogUnread(int before, string who)
    {
        if (Folder.Kind == FolderKind.Inbox && Folder.Unread != before) Log.Write($"non letti di {Account.LogName}: {before} -> {Folder.Unread} ({who})");
    }

    public string UnreadText => Folder.Unread > 0 && Folder.Kind is not (FolderKind.Sent or FolderKind.Deleted or FolderKind.Drafts) ? Folder.Unread.ToString() : "";
    public FontWeight Weight => UnreadText.Length > 0 ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>
    /// Il nome per UI Automation e l'Assistente vocale, come Outlook: "Posta in arrivo, 3 non letti" (prima era
    /// "Missiva.UI.FolderItem"). Le voci degli elenchi lo prendono da ToString.
    /// </summary>
    public string AutomationName => Name + (UnreadText.Length > 0 ? $", {Folder.Unread} {(Folder.Unread == 1 ? "non letto" : "non letti")}" : "")
                                    + (IsHidden ? ", nascosta" : "");
    public override string ToString() => AutomationName;
}

/// <summary>
/// Una riga dell'elenco: un messaggio singolo, la testa di una conversazione (il messaggio più recente, con il numero
/// dei messaggi) oppure, a conversazione aperta, uno dei messaggi precedenti rientrato sotto la testa.
/// </summary>
public sealed class MessageItem : Observable
{
    static readonly CultureInfo Italian = new("it-IT");

    public MessageItem(MessageSummary summary, AccountItem account)
    {
        Summary = summary;
        Account = account;
        // Posta ricevuta davvero: il suo dominio diventa noto col tempo (SenderLogos.IsKnown, per l'icona del sito).
        if (Kind is not (FolderKind.Junk or FolderKind.Sent or FolderKind.Drafts)) SenderLogos.Note(summary);
    }

    public MessageSummary Summary { get; }
    public AccountItem Account { get; }
    public IMailProvider Provider => Account.Provider;

    // ---------------------------------------------------------------- conversazione

    public ConversationGroup? Conversation { get; set; }
    public bool IsChild { get; init; }
    public int Count => Conversation?.Messages.Count ?? 1;
    public bool IsHead => !IsChild && Count > 1;
    public Visibility ChevronVisibility => IsHead ? Visibility.Visible : Visibility.Collapsed;
    public string ChevronGlyph => Conversation?.Expanded == true ? "\uE70D" : "\uE76C";
    public string ChevronTip => Conversation?.Expanded == true ? "Comprimi la conversazione" : "Espandi la conversazione";
    public Thickness RowMargin => IsChild ? new Thickness(44, 0, 0, 0) : new Thickness(0);
    public string CountText => IsHead ? $"({Count})" : "";
    public void RefreshConversation()
    {
        Changed(nameof(Line1Suffix));
        Changed(nameof(Count));
        Changed(nameof(IsHead));
        Changed(nameof(ChevronVisibility));
        Changed(nameof(ChevronGlyph));
        Changed(nameof(ChevronTip));
        Changed(nameof(CountText));
    }

    // ---------------------------------------------------------------- gruppi per data e fissati

    string group = "";
    /// <summary>Intestazione del gruppo (Oggi, Ieri...); vuota per i messaggi fissati, che stanno in cima senza titolo.</summary>
    public string Group { get => group; set { group = value; Changed(); } }

    public bool IsPinned => Store.Settings.IsPinned(Account.Provider.Account, Summary.Id);
    public Visibility PinVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;
    public string PinTip => IsPinned ? "Rimuovi dai fissati" : "Fissa in alto";
    public void RefreshPin()
    {
        Changed(nameof(IsPinned));
        Changed(nameof(PinVisibility));
        Changed(nameof(PinTip));
    }

    /// <summary>
    /// Oggi, per i gruppi dell'elenco (Oggi, Ieri...), le date delle righe e le scadenze. Nelle prove il giorno si può cambiare
    /// (TestToday): il cambio di data a mezzanotte con Missiva aperta (MessageRows.NewDay, MainWindow.CheckDay).
    /// </summary>
#if DEBUG
    public static DateTime Today => TestToday ?? DateTime.Today;
    internal static DateTime? TestToday;
#else
    public static DateTime Today => DateTime.Today;
#endif

    public static string GroupFor(DateTime date)
    {
        var today = Today;
        var d = date.Date;
        int weekday = ((int)today.DayOfWeek + 6) % 7; // lunedì = 0
        var weekStart = today.AddDays(-weekday);
        if (d == today) return "Oggi";
        if (d == today.AddDays(-1)) return "Ieri";
        if (d >= weekStart) return "Questa settimana";
        if (d >= weekStart.AddDays(-7)) return "Settimana scorsa";
        if (d.Year == today.Year && d.Month == today.Month) return "Questo mese";
        var previousMonth = today.AddMonths(-1);
        if (d.Year == previousMonth.Year && d.Month == previousMonth.Month) return "Mese scorso";
        var name = d.ToString(d.Year == today.Year ? "MMMM" : "MMMM yyyy", Italian);
        return char.ToUpper(name[0]) + name[1..];
    }

    // ---------------------------------------------------------------- contenuto

    /// <summary>Il tipo della cartella del messaggio (nei risultati della ricerca può non essere quella aperta).</summary>
    FolderKind Kind => kind ??= Summary.LabelKind
        ?? Account.Folders.FirstOrDefault(f => f.Folder.Id == Summary.FolderId)?.Folder.Kind ?? FolderKind.Other;
    FolderKind? kind;

    /// <summary>
    /// Come in Outlook, in Posta inviata e Bozze (anche fra i risultati della ricerca) la riga dice a chi è scritto il
    /// messaggio: il mittente lì sei sempre tu, con lo stesso nome e la stessa immagine su ogni riga.
    /// </summary>
    bool ShowsRecipients => Kind is FolderKind.Sent or FolderKind.Drafts;
    public bool IsDraft => Kind == FolderKind.Drafts;

    /// <summary>La persona dell'immagine: il mittente, o il primo destinatario in Posta inviata e Bozze.</summary>
    Address Person => ShowsRecipients && Summary.To.Count > 0 ? Summary.To[0] : Summary.From;

    /// <summary>Calcolato una volta per riga (Address.Display dice l'indirizzo vero se il nome è l'indirizzo di un altro): il disegno lo legge e basta.</summary>
    public string Sender => sender ??= ShowsRecipients
        ? Summary.To.Count > 0 ? string.Join("; ", Summary.To.Select(a => a.Display)) : "(nessun destinatario)"
        : Summary.From.Display.Length > 0 ? Summary.From.Display : "(nessun mittente)";
    string? sender;
    public string Subject => Summary.Subject.Length > 0 ? Summary.Subject : "(nessun oggetto)";
    /// <summary>"[Bozza]" in rosso prima dell'oggetto, come in Outlook.</summary>
    string DraftTag => IsDraft ? "[Bozza] " : "";
    public string Preview => Summary.Preview;

    /// <summary>L'anteprima è arrivata dopo l'elenco (Gmail).</summary>
    public void RefreshPreview()
    {
        Changed(nameof(Preview));
    }

    /// <summary>Categorie come pastiglie colorate; nella cartella di un'etichetta Gmail quella stessa non si ripete.</summary>
    public List<CategoryChip> CategoryChips => Summary.Categories
        .Where(c => !c.Equals(Summary.FolderId, StringComparison.OrdinalIgnoreCase))
        .Select(c => new CategoryChip(c, CategoryPalette.PresetOf(Account.Provider.Account.Id, c))).ToList();
    public Visibility CategoryVisibility => CategoryChips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public void RefreshCategories()
    {
        Changed(nameof(CategoryChips));
        Changed(nameof(CategoryVisibility));
    }
    /// <summary>Nei Posposti, al posto della data, quando il messaggio tornerà (come in Outlook).</summary>
    public string DateText => SnoozedUntil is { } until ? "Torna " + SnoozeWindow.Describe(until) : Format(Summary.Date);

    DateTime? SnoozedUntil => Store.Settings.Snoozed.FirstOrDefault(s =>
        s.AccountId == Account.Provider.Account.Id && (s.Key == Summary.StableId || s.Key == Summary.Id))?.Until;

    public void RefreshDate() => Changed(nameof(DateText));

    /// <summary>È cambiato il giorno: la data ("21:54" diventa "mer 21:54"), la scadenza ("domani" diventa "oggi") e il nome letto.</summary>
    public void RefreshDay()
    {
        Changed(nameof(DateText));
        if (Due != null)
        {
            Changed(nameof(DueText));
            Changed(nameof(DueBrush));
        }
        Changed(nameof(AutomationName));
    }
    /// <summary>Nei risultati della ricerca in tutte le cartelle, la cartella del messaggio.</summary>
    public string FolderName => Summary.FolderName;

    /// <summary>
    /// Come in Thunderbird ed eM Client, nelle viste di tutti gli account ogni riga porta il colore e l'indirizzo del suo
    /// account (e nella ricerca la cartella dopo).
    /// </summary>
    public bool ShowAccount { get; init; }
    public string WhereText => ShowAccount ? FolderName.Length > 0 ? $"{Account.ShortName} · {FolderName}" : Account.ShortName : FolderName;
    public string WhereTip => ShowAccount ? FolderName.Length > 0 ? $"{Account.Header}, {FolderName}" : Account.Header : FolderName;
    public Visibility WhereVisibility => WhereText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AccountDotVisibility => ShowAccount ? Visibility.Visible : Visibility.Collapsed;
    public Brush AccountBrush => Account.Brush;
    public string Initials
    {
        get
        {
#if DEBUG
            DevTools.RowsPrepared++; // letto una volta per riga preparata (o riciclata): per i fotogrammi lenti di "scorri N"
#endif
            return Avatars.Initials(Person);
        }
    }
    public Brush AvatarBrush => Avatars.Brush(Person);
    /// <summary>
    /// Appena arrivata in cima (un messaggio nuovo, o la prima pagina dal server con messaggi che la copia locale non
    /// aveva): la riga entra crescendo (MainWindow.xaml). MainWindow.MarkArrived la accende e la spegne poco dopo, così una
    /// riga riciclata più tardi non rifà l'ingresso.
    /// </summary>
    public bool Arrived
    {
        get => arrived;
        set
        {
            if (arrived == value) return;
            arrived = value;
            Changed();
        }
    }
    bool arrived;

    /// <summary>
    /// Il dominio del logo della persona (Logos); null senza logo possibile: posta indesiderata, posta personale, dominio
    /// non autenticato, o questo messaggio arrivato senza la prova che venga da lì (SenderLogos.IsDoubtful).
    /// </summary>
    public string? LogoDomain => Kind == FolderKind.Junk || SenderLogos.IsDoubtful(Account.Provider?.Account, Summary) ? null : Logos.DomainFor(Person);
    /// <summary>Il logo del marchio al posto delle iniziali, quando c'è (arriva dopo: RefreshLogo).</summary>
    public ImageSource? Logo => LogoDomain is { } domain ? Logos.Get(domain) : null;
    /// <summary>La riga è di una persona di questo dominio: quando il suo logo cambia (Logos.Arrived) lo rilegge, che lo mostri o no.</summary>
    public bool IsOfDomain(string domain) => string.Equals(SenderLogos.DomainOf(Person.Email), domain, StringComparison.OrdinalIgnoreCase);
    public Visibility LogoVisibility => Logo != null ? Visibility.Visible : Visibility.Collapsed;
    public void RefreshLogo()
    {
        Changed(nameof(Logo));
        Changed(nameof(LogoVisibility));
    }
    public Visibility AttachmentVisibility => Summary.HasAttachments ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImportantVisibility => Summary.IsImportant ? Visibility.Visible : Visibility.Collapsed;

    // Opzioni di layout (Impostazioni, Elenco messaggi)
    static Settings S => Store.Settings;
    public double FontSize => S.TextSize switch { TextSize.Small => 13, TextSize.Large => 15.5, _ => 14 };
    public double SmallFontSize => FontSize - 2;
    public Thickness Padding => S.TextSize switch { TextSize.Small => new Thickness(0, 3, 4, 3), TextSize.Large => new Thickness(0, 9, 4, 9), _ => new Thickness(0, 6, 4, 6) };
    public string Line1 => S.SenderFirst ? Sender : Subject;
    public string Line2 => S.SenderFirst ? Subject : Sender;
    /// <summary>Prima della riga: "[Bozza]" se è la riga dell'oggetto di una bozza.</summary>
    public string Line1Tag => S.SenderFirst ? "" : DraftTag;
    public string Line2Tag => S.SenderFirst ? DraftTag : "";
    public Brush? DraftBrush => ContextMenus.Brush(ContextMenus.Tone.Red);
    public string Line1Suffix => CountText;
    /// <summary>
    /// Come in Outlook la riga dell'anteprima resta anche vuota, o mentre l'anteprima arriva (Gmail): tutte le righe hanno
    /// la stessa altezza e l'elenco non scivola sotto il mouse quando arrivano le anteprime.
    /// </summary>
    public Visibility PreviewVisibility => S.PreviewLines > 0 ? Visibility.Visible : Visibility.Collapsed;
    public TextWrapping PreviewWrapping => S.PreviewLines >= 2 ? TextWrapping.Wrap : TextWrapping.NoWrap;
    /// <summary>Con due righe l'altezza è fissa, perché un'anteprima corta non faccia la riga più bassa; con una è quella del testo.</summary>
    public double PreviewHeight => S.PreviewLines >= 2 ? SmallFontSize * 2.8 : double.NaN;
    public Visibility AvatarVisibility => S.ShowSenderImages && !IsChild ? Visibility.Visible : Visibility.Collapsed;
    List<AttachmentChip>? attachmentChips;
    /// <summary>Le pastiglie degli allegati, fatte una volta per riga: i nomi non cambiano e ogni lettura ne faceva di nuove (la riga le legge due volte).</summary>
    public List<AttachmentChip> AttachmentChips => attachmentChips ??= S.ShowAttachmentPreviews ? Summary.AttachmentNames.Take(3).Select(n => new AttachmentChip(n)).ToList() : new();
    public Visibility ChipsVisibility => AttachmentChips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>La riga dell'oggetto è blu quando il messaggio non è letto, come in Outlook.</summary>
    public Brush Line1Brush => Brush(!S.SenderFirst);
    public Brush Line2Brush => Brush(S.SenderFirst);
    Brush Brush(bool isSubjectLine) =>
        (Brush)(Application.Current.TryFindResource(isSubjectLine && !IsRead ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush")
                ?? Brushes.Black);

    /// <summary>Il tema di Windows è cambiato: i colori presi dalle risorse sono ancora quelli del tema di prima.</summary>
    public void RefreshTheme()
    {
        Changed(nameof(Line1Brush));
        Changed(nameof(Line2Brush));
        Changed(nameof(DraftBrush));
        Changed(nameof(DueBrush));
    }

    public bool IsRead
    {
        get => Summary.IsRead;
        set
        {
            Summary.IsRead = value;
            Changed();
            Changed(nameof(Weight));
            Changed(nameof(Line1Weight));
            Changed(nameof(Line2Weight));
            Changed(nameof(UnreadVisibility));
            Changed(nameof(Line1Brush));
            Changed(nameof(Line2Brush));
            Changed(nameof(ReadTip));
            Changed(nameof(ReadGlyph));
        }
    }

    /// <summary>La prima delle azioni rapide, come in Outlook: busta aperta per segnare letto, chiusa per segnare da leggere.</summary>
    public string ReadTip => IsRead ? "Segna come da leggere (Ctrl+U)" : "Segna come letto (Ctrl+Q)";
    public string ReadGlyph => IsRead ? "" : "";

    /// <summary>
    /// Azioni rapide anche senza il mouse, sulla riga scelta con le frecce: da tastiera si vedono, e UI Automation le trova
    /// (nascoste, per lei non ci sono).
    /// </summary>
    public bool KeyboardActions { get => keyboardActions; set { keyboardActions = value; Changed(); } }
    bool keyboardActions;

    public bool IsFlagged
    {
        get => Summary.IsFlagged;
        set { Summary.IsFlagged = value; Changed(); Changed(nameof(FlagVisibility)); Changed(nameof(FlagTip)); RefreshFlag(); }
    }

    /// <summary>La scadenza del contrassegno (Graph, o tenuta da Missiva per Gmail e IMAP); null senza data.</summary>
    public DateTime? Due => Flags.DueOf(Account.Provider.Account, Summary);
    /// <summary>"Scadenza: domani" sulla riga, come Outlook; in rosso se è passata.</summary>
    public string DueText => Due is { } d ? Flags.DueText(d, Today) : "";
    public Visibility DueVisibility => Due != null ? Visibility.Visible : Visibility.Collapsed;
    public Brush DueBrush => (Brush)(Application.Current.TryFindResource(Due is { } d && Flags.Overdue(d, Today) ? "CriticalBrush" : "SecondaryTextBrush")
                                     ?? Brushes.Gray);

    /// <summary>Scadenza cambiata (Contrassegna, Segna come completato).</summary>
    public void RefreshFlag()
    {
        Changed(nameof(DueText));
        Changed(nameof(DueVisibility));
        Changed(nameof(DueBrush));
    }

    public FontWeight Weight => IsRead ? FontWeights.Normal : FontWeights.SemiBold;
    /// <summary>Il nome del mittente è sempre in grassetto, per distinguerlo dall'oggetto; l'oggetto lo è solo da leggere.</summary>
    public FontWeight Line1Weight => S.SenderFirst ? FontWeights.SemiBold : Weight;
    public FontWeight Line2Weight => S.SenderFirst ? Weight : FontWeights.SemiBold;
    public Visibility UnreadVisibility => IsRead ? Visibility.Hidden : Visibility.Visible;
    public Visibility FlagVisibility => IsFlagged ? Visibility.Visible : Visibility.Collapsed;
    public string FlagTip => IsFlagged ? "Togli il contrassegno" : "Contrassegna";

    /// <summary>
    /// Il nome della riga per UI Automation e l'Assistente vocale, come Outlook: "Non letto, Amazon.it, Spedito: ...,
    /// sabato 21:54, con allegato, contrassegnato" (prima era "Missiva.UI.MessageItem"). Le voci dell'elenco lo prendono
    /// da ToString, a ogni richiesta: dice lo stato di adesso.
    /// </summary>
    public string AutomationName
    {
        get
        {
            var parts = new List<string>();
            if (!IsRead) parts.Add("Non letto");
            if (IsDraft) parts.Add("Bozza");
            parts.Add(ShowsRecipients ? "A " + Sender : Sender);
            parts.Add(Subject);
            if (IsHead) parts.Add($"conversazione di {Count} messaggi");
            parts.Add(SnoozedUntil is { } until ? "torna " + SnoozeWindow.Describe(until) : Spoken(Summary.Date));
            if (Summary.HasAttachments) parts.Add("con allegato");
            if (Summary.IsImportant) parts.Add("importanza alta");
            if (IsFlagged) parts.Add(DueText.Length > 0 ? "contrassegnato, " + DueText.ToLowerInvariant() : "contrassegnato");
            if (IsPinned) parts.Add("fissato in alto");
            if (ShowAccount) parts.Add(Account.Header);
            return string.Join(", ", parts);
        }
    }

    public override string ToString() => AutomationName;

    /// <summary>La data da leggere: "oggi 21:54", "sabato 21:54" nell'ultima settimana, poi "12 marzo 2026".</summary>
    static string Spoken(DateTime date)
    {
        var today = Today;
        if (date.Date == today) return "oggi " + date.ToString("HH:mm", Italian);
        if (date.Date == today.AddDays(-1)) return "ieri " + date.ToString("HH:mm", Italian);
        if (date.Date > today.AddDays(-7)) return date.ToString("dddd HH:mm", Italian);
        return date.ToString("d MMMM yyyy", Italian);
    }

    public static string Format(DateTime date)
    {
        var today = Today;
        if (date.Date == today) return date.ToString("HH:mm", Italian);
        if (date.Date > today.AddDays(-7)) return date.ToString("ddd HH:mm", Italian);
        if (date.Year == today.Year) return date.ToString("d MMM", Italian);
        return date.ToString("dd/MM/yyyy", Italian);
    }
}

/// <summary>
/// Un allegato nella riga dell'elenco, come nel nuovo Outlook: nome e icona del tipo di file, con le tinte delle schede
/// del riquadro di lettura (FileKinds); i tipi che possono avviare programmi con l'avviso rosso, come nel riquadro.
/// </summary>
public sealed class AttachmentChip(string name)
{
    public string Name { get; } = name;
    public string Glyph { get; } = Safety.IsDangerous(name) ? "" : FileKinds.Glyph(FileKinds.Of(name));
    public Brush Brush { get; } = BrushOf(Safety.IsDangerous(name) ? "#D13438" : FileKinds.GlyphColor(FileKinds.Of(name)));

    static readonly Dictionary<string, Brush> brushes = new();

    static Brush BrushOf(string hex)
    {
        if (brushes.TryGetValue(hex, out var known)) return known;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brushes[hex] = brush;
    }
}

/// <summary>Una categoria mostrata come pastiglia: nome (l'ultima parte, per le etichette annidate) e colori.</summary>
public sealed class CategoryChip(string name, int preset)
{
    public string Name { get; } = name;
    public string Text => Name.Contains('/') ? Name[(Name.LastIndexOf('/') + 1)..] : Name;
    public Brush Background { get; } = Frozen(CategoryPalette.Hex(preset));
    public Brush Foreground { get; } = Frozen(CategoryPalette.TextHex(preset));

    static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>I messaggi di una conversazione presenti nella cartella, dal più recente.</summary>
public sealed class ConversationGroup
{
    public required string Key { get; init; }
    public List<MessageSummary> Messages { get; } = new();
    public bool Expanded { get; set; }
    public MessageItem? Head { get; set; }
    public List<MessageItem> Children { get; } = new();
}

/// <summary>
/// Tiene allineate le righe dell'elenco: raggruppa per conversazione (se attivo), mette i fissati in cima,
/// assegna le intestazioni di data, apre e chiude le conversazioni senza ricostruire tutto l'elenco.
/// </summary>
public sealed class MessageRows
{
    readonly Dictionary<string, ConversationGroup> groups = new();
    public ObservableCollection<MessageItem> Rows { get; } = new();

    /// <summary>
    /// La chiave del gruppo, con l'account: nella Posta in arrivo di tutti gli account (e nella ricerca in tutti gli
    /// account) due account possono avere lo stesso UID o la stessa conversazione, che resta su due righe. Con la cartella:
    /// gli UID IMAP valgono per cartella, e la ricerca in tutte le cartelle ne mescola più d'una.
    /// </summary>
    static string KeyOf(MessageSummary m, AccountItem account) =>
        Store.Settings.GroupByConversation && m.ConversationId.Length > 0
            ? "c:" + account.Provider.Account.Id + "|" + m.ConversationId
            : "m:" + account.Provider.Account.Id + "|" + m.FolderId + "|" + m.Id;

    /// <summary>Ordinamento dell'elenco: per data le intestazioni sono Oggi, Ieri...; per mittente o oggetto le iniziali.</summary>
    public SortBy Sort { get; set; } = SortBy.Date;

    /// <summary>L'elenco mescola più account (Tutti gli account, ricerca in tutti gli account): ogni riga dice il suo.</summary>
    public bool ShowAccounts { get; set; }

    /// <summary>
    /// Contrassegnati di tutti gli account, come l'elenco delle attività di Outlook: le righe per scadenza (quelle senza
    /// data in fondo, a parità di scadenza la più recente prima) nei gruppi In ritardo, Oggi, Domani, Questa settimana, Più
    /// avanti, Senza data.
    /// </summary>
    public bool ByDue { get; set; }

    static (DateTime Due, long Newer) DueKey(MessageSummary m, AccountItem account) =>
        (Flags.DueOf(account.Provider.Account, m) ?? DateTime.MaxValue, -m.Date.Ticks);

    /// <summary>Dove va una riga nuova: in fondo, o per scadenza (ByDue), fra le teste e dopo i figli delle conversazioni aperte.</summary>
    int PlaceOf(MessageSummary m, AccountItem account)
    {
        if (!ByDue) return Rows.Count;
        var key = DueKey(m, account);
        int i = PinnedEnd();
        while (i < Rows.Count && (Rows[i].IsChild || DueKey(Rows[i].Summary, Rows[i].Account).CompareTo(key) <= 0)) i++;
        return i;
    }

    string GroupOf(MessageSummary m, AccountItem account)
    {
        if (ByDue) return Flags.Group(Flags.DueOf(account.Provider.Account, m), MessageItem.Today);
        static string Initial(string text)
        {
            var c = text.TrimStart().FirstOrDefault(char.IsLetterOrDigit);
            return c == default ? "#" : char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : "#";
        }
        return Sort switch
        {
            SortBy.From => Initial(m.From.Display),
            SortBy.Subject => Initial(m.Subject),
            _ => MessageItem.GroupFor(m.Date),
        };
    }

    public void Clear()
    {
        groups.Clear();
        Rows.Clear();
    }

    /// <summary>
    /// È cambiato il giorno con l'elenco aperto (Missiva resta aperta per giorni, anche nell'area di notifica): il gruppo di
    /// ogni riga si ricalcola (Oggi diventa Ieri; nei Contrassegnati per scadenza Domani diventa Oggi) e le date mostrate si
    /// aggiornano. Prima il gruppo restava quello di quando la riga era stata inserita: dopo mezzanotte i messaggi di ieri
    /// restavano sotto "Oggi", insieme a quelli arrivati dopo (audit del 7 ottobre 2026, B-08). Vero se un gruppo è cambiato:
    /// allora l'elenco va raggruppato di nuovo (MainWindow.CheckDay).
    /// </summary>
    public bool NewDay()
    {
        bool changed = false;
        foreach (var row in Rows)
        {
            if (!row.IsChild)
            {
                string group = row.IsPinned ? "" : GroupOf(row.Summary, row.Account);
                if (group != row.Group)
                {
                    row.Group = group;
                    changed = true;
                }
            }
            row.RefreshDay();
        }
        // Le righe di una conversazione aperta stanno nel gruppo della prima.
        foreach (var row in Rows)
            if (row.IsChild && row.Conversation?.Head is { } head && row.Group != head.Group) row.Group = head.Group;
        return changed;
    }

    /// <summary>Messaggi più vecchi (pagina successiva): nuove conversazioni in fondo, le altre si allungano.</summary>
    public void Append(IEnumerable<MessageSummary> messages, AccountItem account) => Append(messages.Select(m => (m, account)));

    /// <summary>Come sopra, con l'account di ogni messaggio (Posta in arrivo di tutti gli account, ricerca in tutti gli account).</summary>
    public void Append(IEnumerable<(MessageSummary Message, AccountItem Account)> messages)
    {
        foreach (var (m, account) in messages)
        {
            var key = KeyOf(m, account);
            if (groups.TryGetValue(key, out var g))
            {
                if (g.Messages.Any(x => x.Id == m.Id)) continue;
                g.Messages.Add(m);
                g.Head!.RefreshConversation();
                if (g.Expanded) InsertChild(g, m, account);
                continue;
            }
            g = new ConversationGroup { Key = key };
            g.Messages.Add(m);
            groups[key] = g;
            var head = new MessageItem(m, account) { Conversation = g, ShowAccount = ShowAccounts };
            g.Head = head;
            head.Group = head.IsPinned ? "" : GroupOf(m, account);
            Rows.Insert(head.IsPinned ? PinnedEnd() : PlaceOf(m, account), head);
        }
    }

    /// <summary>Messaggi nuovi: diventano la testa della loro conversazione e salgono in cima. Restituisce le righe nuove.</summary>
    public List<MessageItem> Prepend(IEnumerable<MessageSummary> messages, AccountItem account)
    {
        var added = new List<MessageItem>();
        foreach (var m in messages.OrderBy(m => m.Date))
        {
            var key = KeyOf(m, account);
            if (groups.TryGetValue(key, out var old))
            {
                if (old.Messages.Any(x => x.Id == m.Id)) continue;
                Remove(old);
                old.Messages.Insert(0, m);
                added.Add(Reinsert(old, account));
                continue;
            }
            var g = new ConversationGroup { Key = key };
            g.Messages.Add(m);
            groups[key] = g;
            added.Add(Reinsert(g, account));
        }
        return added;
    }

    MessageItem Reinsert(ConversationGroup g, AccountItem account)
    {
        var head = new MessageItem(g.Messages[0], account) { Conversation = g, ShowAccount = ShowAccounts };
        g.Head = head;
        g.Children.Clear();
        g.Expanded = false;
        head.Group = head.IsPinned ? "" : GroupOf(head.Summary, account);
        Rows.Insert(ByDue && !head.IsPinned ? PlaceOf(head.Summary, account) : PinnedEnd(), head);
        return head;
    }

    int PinnedEnd()
    {
        int i = 0;
        while (i < Rows.Count && Rows[i].Group.Length == 0) i++;
        return i;
    }

    public void Toggle(MessageItem head)
    {
        var g = head.Conversation;
        if (g == null || g.Messages.Count < 2) return;
        g.Expanded = !g.Expanded;
        if (g.Expanded)
        {
            foreach (var m in g.Messages.Skip(1)) InsertChild(g, m, head.Account);
        }
        else
        {
            foreach (var c in g.Children) Rows.Remove(c);
            g.Children.Clear();
        }
        head.RefreshConversation();
    }

    public void CollapseAll()
    {
        foreach (var g in groups.Values.Where(g => g.Expanded).ToList()) Toggle(g.Head!);
    }

    void InsertChild(ConversationGroup g, MessageSummary m, AccountItem account)
    {
        var child = new MessageItem(m, account) { Conversation = g, IsChild = true, Group = g.Head!.Group, ShowAccount = ShowAccounts };
        var anchor = g.Children.Count > 0 ? g.Children[^1] : g.Head;
        Rows.Insert(Rows.IndexOf(anchor) + 1, child);
        g.Children.Add(child);
    }

    /// <summary>
    /// La riga da scegliere quando quelle righe escono (una testa porta via i suoi figli aperti): la prima che resta dopo la
    /// prima tolta, o l'ultima rimasta se dopo non c'è nulla; null se non resta nessuna riga, o se nessuna di quelle è
    /// nell'elenco.
    /// </summary>
    public MessageItem? After(IEnumerable<MessageItem> items)
    {
        var leaving = new HashSet<MessageItem>(items);
        foreach (var g in leaving.Where(i => !i.IsChild && i.Conversation != null).Select(i => i.Conversation!).ToList())
        {
            if (g.Head != null) leaving.Add(g.Head); // Remove toglie la testa di adesso, anche se la riga data è una di prima
            leaving.UnionWith(g.Children);
        }
        int first = 0;
        while (first < Rows.Count && !leaving.Contains(Rows[first])) first++;
        if (first == Rows.Count) return null;
        for (int k = first + 1; k < Rows.Count; k++)
            if (!leaving.Contains(Rows[k])) return Rows[k];
        return first > 0 ? Rows[first - 1] : null;
    }

    /// <summary>Toglie una riga: se è la testa, sparisce tutta la conversazione dalla cartella.</summary>
    public void Remove(MessageItem item)
    {
        if (item.Conversation is not { } g) { Rows.Remove(item); return; }
        if (item.IsChild)
        {
            g.Messages.Remove(item.Summary);
            g.Children.Remove(item);
            Rows.Remove(item);
            g.Head?.RefreshConversation();
            return;
        }
        Remove(g);
        groups.Remove(g.Key);
    }

    void Remove(ConversationGroup g)
    {
        foreach (var c in g.Children) Rows.Remove(c);
        if (g.Head != null) Rows.Remove(g.Head);
    }

    /// <summary>
    /// Annulla: le righe tolte tornano dov'erano, con i messaggi come sono ora (back: dall'originale a quello tornato, con
    /// l'identificativo nuovo). Una conversazione torna chiusa; un suo messaggio tolto da solo rientra nella sua, se
    /// nell'elenco c'è ancora. Restituisce le righe tornate, da selezionare.
    /// </summary>
    public List<MessageItem> Restore(IEnumerable<(int Index, List<MessageSummary> Messages)> removed,
                                     IReadOnlyDictionary<MessageSummary, MessageSummary> back, Func<MessageSummary, AccountItem> accountOf)
    {
        var restored = new List<MessageItem>();
        foreach (var (index, messages) in removed.OrderBy(r => r.Index))
        {
            var now = messages.Where(back.ContainsKey).Select(m => back[m]).OrderByDescending(m => m.Date).ToList();
            if (now.Count == 0) continue;
            var account = accountOf(now[0]);
            var key = KeyOf(now[0], account);
            if (groups.TryGetValue(key, out var g) && g.Head != null)
            {
                g.Messages.AddRange(now.Where(m => !g.Messages.Any(x => x.Id == m.Id)));
                g.Messages.Sort((a, b) => b.Date.CompareTo(a.Date));
                if (g.Expanded)
                {
                    Toggle(g.Head);
                    Toggle(g.Head);
                }
                g.Head.RefreshConversation();
                restored.Add(g.Head);
                continue;
            }
            g = new ConversationGroup { Key = key };
            g.Messages.AddRange(now);
            groups[key] = g;
            var head = new MessageItem(now[0], account) { Conversation = g, ShowAccount = ShowAccounts };
            g.Head = head;
            head.Group = head.IsPinned ? "" : GroupOf(now[0], account);
            Rows.Insert(Math.Min(index, Rows.Count), head);
            restored.Add(head);
        }
        return restored;
    }

    /// <summary>
    /// Uno spostamento che il server ha rifiutato: le righe di quei messaggi tornano dov'erano, senza contare il posto di
    /// quelle tolte insieme a loro che non tornano (i messaggi di un altro account, spostati davvero). Prima restavano fuori
    /// dall'elenco fino alla rilettura, con il messaggio ancora nella sua cartella (audit dell'11 ottobre, C-50).
    /// </summary>
    public List<MessageItem> RestoreFailed(IEnumerable<(int Index, List<MessageSummary> Messages)> removed, IReadOnlyDictionary<MessageSummary, AccountItem> failed)
    {
        var back = new List<(int Index, List<MessageSummary> Messages)>();
        int gone = 0;
        foreach (var row in removed.OrderBy(r => r.Index))
        {
            if (row.Messages.Any(failed.ContainsKey)) back.Add((row.Index - gone, row.Messages));
            else gone++;
        }
        return Restore(back, failed.Keys.ToDictionary(m => m, m => m), m => failed[m]);
    }

    /// <summary>Fissato o non più fissato: la conversazione cambia posto.</summary>
    public void Repin(MessageItem head)
    {
        if (head.Conversation is not { } g || head.IsChild) return;
        bool wasExpanded = g.Expanded;
        if (wasExpanded) Toggle(head);
        Rows.Remove(head);
        head.Group = head.IsPinned ? "" : GroupOf(head.Summary, head.Account);
        if (head.IsPinned) Rows.Insert(0, head);
        else
        {
            int i = PinnedEnd();
            while (i < Rows.Count && Rows[i].Summary.Date > head.Summary.Date) i++;
            Rows.Insert(i, head);
        }
        head.RefreshPin();
        if (wasExpanded) Toggle(head);
    }

    /// <summary>Tutti i messaggi rappresentati da una riga (la testa porta con sé la conversazione chiusa).</summary>
    public static List<MessageSummary> MessagesOf(MessageItem item) =>
        item.IsChild || item.Conversation == null || item.Conversation.Expanded ? new() { item.Summary } : item.Conversation.Messages.ToList();
}

/// <summary>
/// I loghi dei mittenti per l'interfaccia (SenderLogos): un'immagine congelata per dominio, letta dal disco una volta; la
/// prima richiesta di un dominio senza logo lo fa cercare (una volta per sessione, quando è noto e autenticato). Solo le
/// righe che si vedono lo chiedono: il binding legge Logo quando la riga compare. Un dominio non autenticato
/// (SenderLogos.IsAuthenticated: nessun messaggio ricevuto da lì ha superato DMARC) non ha logo, nemmeno se il file c'è.
/// </summary>
public static class Logos
{
    static readonly Dictionary<string, ImageSource?> images = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> asked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Il logo di un dominio è cambiato (arrivato, o il dominio è appena risultato autenticato), sul thread della finestra.</summary>
    public static event Action<string>? Arrived;

    static Logos() => SenderLogos.Arrived += domain => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        images.Remove(domain);
        Arrived?.Invoke(domain);
    });

    /// <summary>
    /// Il dominio del logo di una persona, con l'impostazione accesa e il dominio autenticato; null altrimenti (restano le
    /// iniziali). Vale per elenco, riquadro, scheda della persona e notifiche.
    /// </summary>
    public static string? DomainFor(Address person) =>
        Store.Settings.ShowSenderLogos && SenderLogos.DomainOf(person.Email) is { } domain && SenderLogos.IsAuthenticated(domain) ? domain : null;

    public static ImageSource? Get(string domain)
    {
        if (!SenderLogos.IsAuthenticated(domain)) return null;
        if (!images.TryGetValue(domain, out var image))
        {
            var path = SenderLogos.PathOf(domain);
            image = images[domain] = File.Exists(path) ? Load(path) : null;
            // Un logo salvato da più di 30 giorni si riverifica in sottofondo, una volta per sessione; intanto resta quello.
            if (image != null && SenderLogos.IsStale(domain)) SenderLogos.Request(domain);
        }
        // Chiesto una volta per sessione, da quando la ricerca può partire (dominio noto: Request dice di no finché non lo è).
        if (image == null && !asked.Contains(domain) && SenderLogos.Request(domain)) asked.Add(domain);
        return image;
    }

    /// <summary>I byte del PNG, per il riquadro di lettura; null senza logo.</summary>
    public static byte[]? Bytes(string domain)
    {
        if (Get(domain) == null) return null;
        try { return File.ReadAllBytes(SenderLogos.PathOf(domain)); }
        catch (IOException) { return null; }
    }

    static ImageSource? Load(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // il file resta libero
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Write($"logo non letto: {ex.Message}");
            return null;
        }
    }
}

public static class Avatars
{
    static readonly Color[] Palette =
    {
        Color.FromRgb(0xC2, 0x39, 0xB3), Color.FromRgb(0x00, 0x78, 0xD4), Color.FromRgb(0x10, 0x7C, 0x10),
        Color.FromRgb(0xCA, 0x50, 0x10), Color.FromRgb(0x88, 0x17, 0x98), Color.FromRgb(0x00, 0x83, 0x7A),
        Color.FromRgb(0xA4, 0x26, 0x2C), Color.FromRgb(0x5C, 0x2E, 0x91), Color.FromRgb(0x49, 0x82, 0x05),
        Color.FromRgb(0x03, 0x83, 0x87), Color.FromRgb(0x8E, 0x56, 0x2E), Color.FromRgb(0x4F, 0x6B, 0xED),
    };

    /// <summary>
    /// Come nel nuovo Outlook: le parole si separano solo sugli spazi ("Amazon.it" è "A", non "AI") e di un indirizzo
    /// conta la parte prima della @ ("assistenza@paypal.it" è "A").
    /// </summary>
    public static string Initials(Address a)
    {
        var source = a.Name.Length > 0 ? a.Name : a.Email;
        if (source.Contains('@') && !source.Contains(' ')) source = source.Split('@')[0];
        var words = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                          .Where(w => char.IsLetterOrDigit(w[0])).ToArray();
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..1].ToUpperInvariant(),
            _ => (words[0][..1] + words[^1][..1]).ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Il colore viene dal nome mostrato, come nel nuovo Outlook, e dall'indirizzo solo se il nome manca: Amazon.it ha
    /// lo stesso colore anche se scrive da indirizzi diversi per ordini e spedizioni.
    /// </summary>
    public static Color ColorOf(Address a) => ColorOf(a.Name.Length > 0 ? a.Name : a.Email);

    public static Color ColorOf(string key)
    {
        int hash = 0;
        foreach (var c in key.ToLowerInvariant()) hash = hash * 31 + c;
        return Palette[Math.Abs(hash % Palette.Length)];
    }

    /// <summary>
    /// Il colore di un account nelle viste di tutti gli account: dall'ordine degli account, così due account non hanno mai
    /// lo stesso colore (dall'indirizzo, i due account dell'autore venivano tutti e due arancioni).
    /// </summary>
    public static Color AccountColor(MailAccount account)
    {
        int i = Store.Settings.Accounts.FindIndex(a => a.Id == account.Id);
        int[] order = [1, 3, 2, 0, 5, 7, 4, 6, 9, 11, 8, 10];
        return i < 0 ? ColorOf(account.Email) : Palette[order[i % order.Length]];
    }

    public static Brush Brush(Address a)
    {
        var brush = new SolidColorBrush(ColorOf(a));
        brush.Freeze();
        return brush;
    }
}
