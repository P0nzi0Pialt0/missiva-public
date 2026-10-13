using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// La riga dell'elenco dei messaggi disegnata da un solo elemento (Loop 4, agente C, consiglio di A): al posto del modello
/// con un centinaio di elementi (contenitori, TextBlock, Run, Button, CheckBox, Ellipse, ItemsControl) una riga fa una
/// misura e un OnRender, e il disegno resta in cache finché la riga non cambia. La scheda, la tinta, la selezione, l'ingresso
/// animato e il riciclo restano del ListBoxItem (stile in MainWindow.xaml). Il disegno è quello del modello di prima, voce
/// per voce (le misure sono le sue: Margin, Padding, MaxWidth, dimensioni dei caratteri).
/// Quello che ha bisogno di elementi veri lo ha solo quando serve: la freccia della conversazione (righe di conversazione),
/// le quattro azioni rapide e la casella di selezione (solo con il mouse sopra, con la riga scelta con le frecce o in
/// modalità Seleziona), tolte dall'albero quando non servono. Le pastiglie degli allegati e delle categorie sono disegnate
/// e ricevono il clic e il suggerimento con il test del punto.
/// </summary>
public sealed class MessageRowElement : FrameworkElement
{
    /// <summary>La finestra della riga: i gestori delle azioni sono i suoi (ci possono essere più finestre principali).</summary>
    MainWindow? Owner => Window.GetWindow(this) as MainWindow;

    /// <summary>Modalità Seleziona (Tag dell'elenco): la casella al posto dell'immagine su tutte le righe.</summary>
    bool SelectMode => FindContainer() is { } container && ItemsControl.ItemsControlFromItemContainer(container)?.Tag as string == "select";

    /// <summary>Il tema o l'accento sono cambiati: le righe si ridisegnano con i colori nuovi.</summary>
    public static void RefreshAll() => Refreshed?.Invoke();
    static event Action? Refreshed;

    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static readonly CultureInfo TextCulture = CultureInfo.GetCultureInfo("en-US");
    static readonly Pen LogoRing = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x1F, 0, 0, 0))), 1));
    static readonly Dictionary<(FontFamily, int), Typeface> Typefaces = new();

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    static Typeface TypefaceFor(FontFamily family, FontWeight weight)
    {
        var key = (family, weight.ToOpenTypeWeight());
        if (!Typefaces.TryGetValue(key, out var typeface))
            Typefaces[key] = typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
        return typeface;
    }

    // ---------------------------------------------------------------- stato

    MessageItem? item;
    bool attached, hover, showChevron, showActions, showCheck;
    Layout? layout;
    double layoutWidth = -1;
    List<CategoryChip>? categories;
    readonly List<UIElement> children = new();
    Button? chevron, readButton, flagButton, pinButton, deleteButton;
    StackPanel? actions;
    CheckBox? check;
    double dpi = 1;

    public MessageRowElement()
    {
        ToolTip = new ToolTip(); // il testo lo sceglie OnToolTipOpening dal punto del mouse
        DataContextChanged += (_, _) => Bind(DataContext as MessageItem);
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (VisualParent != null && !attached)
        {
            attached = true;
            Refreshed += OnRefreshed;
            Bind(DataContext as MessageItem);
        }
        else if (VisualParent == null && attached)
        {
            attached = false;
            Refreshed -= OnRefreshed;
            if (item != null) item.PropertyChanged -= OnItemChanged;
            item = null;
            hover = false;
            Sync();
        }
    }

    void Bind(MessageItem? next)
    {
        if (ReferenceEquals(item, next) && attached) return;
        if (item != null) item.PropertyChanged -= OnItemChanged;
        item = attached ? next : null;
        if (item != null) item.PropertyChanged += OnItemChanged;
        categories = null;
        Sync();
        Invalidate(true);
    }

    void OnRefreshed()
    {
        Sync();
        Invalidate(true);
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MessageItem.CategoryChips):
            case nameof(MessageItem.CategoryVisibility):
                categories = null;
                break;
            case nameof(MessageItem.Preview):
            case nameof(MessageItem.Line1Brush):
            case nameof(MessageItem.Line2Brush):
            case nameof(MessageItem.DraftBrush):
            case nameof(MessageItem.DueBrush):
            case nameof(MessageItem.Logo):
            case nameof(MessageItem.LogoVisibility):
            case nameof(MessageItem.Arrived):
                Invalidate(false);
                return;
        }
        Sync();
        Invalidate(true);
    }

    /// <summary>Il disegno da rifare; con measure anche la misura (una riga cambia di larghezza dei pezzi, e di altezza con le categorie).</summary>
    void Invalidate(bool measure)
    {
        layout = null;
        if (measure)
        {
            CursorProbe.Hit(CursorProbe.C.InvalidateMeasure);
            InvalidateMeasure();
        }
        CursorProbe.Hit(CursorProbe.C.InvalidateVisual);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- elementi veri, solo quando servono

    void Sync()
    {
        using var probeTime = CursorProbe.Time(CursorProbe.T.Sync);
        CursorProbe.Hit(CursorProbe.C.SyncCalls);
        bool wantChevron = item is { IsHead: true };
        bool wantActions = item != null && (hover || item.KeyboardActions);
        bool wantCheck = item != null && (hover || SelectMode);
        if (wantChevron != showChevron || wantActions != showActions || wantCheck != showCheck)
        {
            CursorProbe.Hit(CursorProbe.C.SyncChanges);
            Change(ref showChevron, wantChevron, () => chevron ??= MakeChevron());
            Change(ref showActions, wantActions, () => actions ??= MakeActions());
            Change(ref showCheck, wantCheck, () => check ??= MakeCheck());
            CursorProbe.Hit(CursorProbe.C.InvalidateMeasure);
            InvalidateMeasure();
        }
        if (item == null) return;
        if (showChevron && chevron != null)
        {
            chevron.Tag = item;
            ((TextBlock)chevron.Content).Text = item.ChevronGlyph;
            System.Windows.Automation.AutomationProperties.SetName(chevron, item.ChevronTip);
        }
        if (showActions)
        {
            Tune(readButton!, item, item.ReadTip, item.ReadGlyph);
            Tune(flagButton!, item, item.FlagTip, null);
            Tune(pinButton!, item, item.PinTip, null);
            Tune(deleteButton!, item, "Elimina", null);
        }
        if (showCheck && check != null)
        {
            // La casella segue la selezione della riga (il ListBoxItem, a due vie), come il modello di prima.
            var container = FindContainer();
            if (container != null && !ReferenceEquals(check.Tag, container))
            {
                check.Tag = container;
                check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(ListBoxItem.IsSelected)) { Source = container, Mode = System.Windows.Data.BindingMode.TwoWay });
            }
        }
    }

    static void Tune(Button button, MessageItem item, string tip, string? glyph)
    {
        button.Tag = item;
        button.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        if (glyph != null) ((TextBlock)button.Content).Text = glyph;
    }

    ListBoxItem? FindContainer()
    {
        for (DependencyObject? d = this; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is ListBoxItem lbi) return lbi;
        return null;
    }

    void Change(ref bool state, bool want, Func<UIElement> make)
    {
        if (state == want) return;
        state = want;
        var element = make();
        if (want)
        {
            children.Add(element);
            CursorProbe.Hit(CursorProbe.C.AddChild);
            AddVisualChild(element);
            AddLogicalChild(element);
        }
        else
        {
            children.Remove(element);
            CursorProbe.Hit(CursorProbe.C.RemoveChild);
            RemoveVisualChild(element);
            RemoveLogicalChild(element);
        }
    }

    Button MakeChevron()
    {
        var button = new Button
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(2, 0, 6, 0), MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new TextBlock { FontFamily = IconFont, FontSize = 10 },
        };
        button.Click += (s, e) => Owner?.OnConversationToggle(s, e);
        return button;
    }

    Button MakeButton(string glyph, Action<object, RoutedEventArgs> click)
    {
        var button = new Button
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(5, 3, 5, 3), MinWidth = 0,
            Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 13 },
        };
        button.Click += (s, e) => click(s, e);
        return button;
    }

    StackPanel MakeActions()
    {
        readButton = MakeButton("", (s, e) => Owner?.OnRowRead(s, e));
        flagButton = MakeButton("", (s, e) => Owner?.OnRowFlag(s, e));
        pinButton = MakeButton("", (s, e) => Owner?.OnRowPin(s, e));
        deleteButton = MakeButton("", (s, e) => Owner?.OnRowDelete(s, e));
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(readButton);
        panel.Children.Add(flagButton);
        panel.Children.Add(pinButton);
        panel.Children.Add(deleteButton);
        return panel;
    }

    CheckBox MakeCheck() => new() { MinWidth = 0, VerticalAlignment = VerticalAlignment.Top };

    protected override int VisualChildrenCount => children.Count;

    protected override Visual GetVisualChild(int index) => children[index];

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        using var probeTime = CursorProbe.Time(CursorProbe.T.EnterLeave);
        CursorProbe.Hit(CursorProbe.C.RowEnter);
        base.OnMouseEnter(e);
        hover = true;
        Sync();
        Invalidate(true);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        using var probeTime = CursorProbe.Time(CursorProbe.T.EnterLeave);
        CursorProbe.Hit(CursorProbe.C.RowLeave);
        base.OnMouseLeave(e);
        hover = false;
        Sync();
        Invalidate(true);
    }

#if DEBUG
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        CursorProbe.Hit(CursorProbe.C.HitTest);
        return base.HitTestCore(hitTestParameters);
    }
#endif

    // ---------------------------------------------------------------- disposizione

    sealed class Piece
    {
        public FormattedText Text = null!;
        public Point At;
    }

    sealed class ChipBox
    {
        public Rect Rect;
        public Brush? Fill;
        public Pen? Border;
        public List<Piece> Pieces = new();
        public Rect Clip;
        public bool HasClip;
        public string Tip = "";
        public AttachmentChip? Attachment;
    }

    sealed class Layout
    {
        public double Width, Height;
        public Rect Bar;
        public bool DrawBar;
        public bool DrawAvatar;
        public Rect Avatar;
        public Brush? AvatarBrush;
        public ImageSource? Logo;
        public Point? DotAt;
        public Brush? DotBrush;
        public List<Piece> Texts = new();
        public List<ChipBox> Chips = new();
        public Rect WhereRect, ImportantRect;
        public string WhereTip = "";
    }

    Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    FormattedText Make(string text, double size, Brush brush, FontWeight weight, bool icons = false) =>
        new(text, TextCulture, FlowDirection.LeftToRight, TypefaceFor(icons ? IconFont : TextElement.GetFontFamily(this), weight), size, brush, dpi);

    static FormattedText Fit(FormattedText text, double width)
    {
        text.MaxTextWidth = Math.Max(1, width);
        text.MaxLineCount = 1;
        text.Trimming = TextTrimming.CharacterEllipsis;
        return text;
    }

    static Size Infinite => new(double.PositiveInfinity, double.PositiveInfinity);

    Layout Compute(double width)
    {
        var it = item!;
        dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var result = new Layout { Width = width };
        var pad = it.Padding;
        double size = it.FontSize, small = it.SmallFontSize;
        var secondary = Res("SecondaryTextBrush", Brushes.Gray);
        var critical = Res("CriticalBrush", Brushes.Red);
        bool avatarVisible = it.AvatarVisibility == Visibility.Visible;

        // Colonna dell'immagine: la casella (con il mouse sopra o in Seleziona) occupa il suo posto anche senza immagine.
        double checkSlot = 0;
        if (showCheck && check != null)
        {
            check.Measure(Infinite);
            checkSlot = 14 + check.DesiredSize.Width + 6;
        }
        double rowLeft = it.IsChild ? 44 : 0;
        // Con la casella (mouse sopra o Seleziona) il modello di prima rendeva l'immagine Hidden, non Collapsed: il suo posto c'è sempre.
        double col1 = rowLeft + Math.Max(avatarVisible || showCheck ? 8 + 34 + 12 : 0, checkSlot);
        double left = pad.Left + 4 + col1;
        double colW = Math.Max(0, width - pad.Right - left);
        double top = pad.Top;

        // Riga 1: freccia, mittente (o oggetto), data o azioni rapide
        string tag1 = it.Line1Tag, line1 = it.Line1, suffix = it.Line1Suffix;
        var ft1 = Make(tag1 + line1 + " " + suffix, size, it.Line1Brush, it.Line1Weight);
        if (tag1.Length > 0 && it.DraftBrush is { } draft) ft1.SetForegroundBrush(draft, 0, tag1.Length);
        if (suffix.Length > 0)
        {
            int at = tag1.Length + line1.Length + 1;
            ft1.SetFontWeight(FontWeights.Normal, at, suffix.Length);
            ft1.SetForegroundBrush(secondary, at, suffix.Length);
        }
        var date = Make(it.DateText, small, secondary, it.Weight);
        double dateW = date.WidthIncludingTrailingWhitespace;
        double chevronW = 0, chevronH = 0, actionsW = 0, actionsH = 0;
        if (showChevron && chevron != null)
        {
            chevron.Measure(Infinite);
            chevronW = chevron.DesiredSize.Width;
            chevronH = chevron.DesiredSize.Height;
        }
        if (showActions && actions != null)
        {
            actions.Measure(Infinite);
            actionsW = actions.DesiredSize.Width;
            actionsH = actions.DesiredSize.Height - 8; // margini -4 sopra e sotto
        }
        double col2 = Math.Max(dateW + 8, showActions ? actionsW + 4 : 0);
        double h1 = Math.Max(Math.Max(ft1.Height, date.Height), Math.Max(chevronH, actionsH));
        double text1W = Math.Max(0, colW - chevronW - col2);
        result.Texts.Add(new Piece { Text = Fit(ft1, text1W), At = new Point(left + chevronW, top) });
        double col2X = left + chevronW + text1W;
        if (!showActions) result.Texts.Add(new Piece { Text = date, At = new Point(col2X + 8, top + (h1 - date.Height) / 2) });
        chevronRect = showChevron ? new Rect(left, top + (h1 - chevronH) / 2, chevronW, chevronH) : Rect.Empty;
        actionsRect = showActions ? new Rect(col2X + 4, top - 4, Math.Max(0, col2 - 4), h1 + 8) : Rect.Empty;

        // Riga 2: oggetto (o mittente), allegato, fissato, contrassegno, dove, importanza
        double y2 = top + h1;
        string tag2 = it.Line2Tag;
        var ft2 = Make(tag2 + it.Line2, size, it.Line2Brush, it.Line2Weight);
        if (tag2.Length > 0 && it.DraftBrush is { } draft2) ft2.SetForegroundBrush(draft2, 0, tag2.Length);
        FormattedText? attach = null, pin = null, flag = null, due = null, important = null, where = null;
        double whereW = 0;
        bool dot = it.AccountDotVisibility == Visibility.Visible;
        if (it.Summary.HasAttachments) attach = Make("", 12, Res("TextFillColorSecondaryBrush", Brushes.Gray), FontWeights.Normal, true);
        if (it.IsPinned) pin = Make("", 12, Res("AccentTextFillColorPrimaryBrush", Brushes.DodgerBlue), FontWeights.Normal, true);
        if (it.IsFlagged)
        {
            flag = Make("", 12, critical, FontWeights.Normal, true);
            if (it.DueVisibility == Visibility.Visible) due = Make(it.DueText, small, it.DueBrush, FontWeights.Normal);
        }
        if (it.Summary.IsImportant) important = Make("", 12, critical, FontWeights.Normal, true);
        string whereText = it.WhereText;
        if (whereText.Length > 0)
        {
            where = Make(whereText, small, secondary, FontWeights.Normal);
            whereW = Math.Min(where.WidthIncludingTrailingWhitespace, 170);
            Fit(where, whereW);
        }
        double flagW = flag == null ? 0 : (due == null ? 0 : 8 + due.WidthIncludingTrailingWhitespace) + 6 + flag.WidthIncludingTrailingWhitespace;
        double whereStackW = where == null ? 0 : 8 + (dot ? 7 + 5 : 0) + whereW;
        double auto = (attach == null ? 0 : 6 + attach.WidthIncludingTrailingWhitespace) + (pin == null ? 0 : 6 + pin.WidthIncludingTrailingWhitespace)
                      + flagW + whereStackW + (important == null ? 0 : 6 + important.WidthIncludingTrailingWhitespace);
        double h2 = ft2.Height;
        foreach (var f in new[] { attach, pin, flag, due, important, where }) if (f != null) h2 = Math.Max(h2, f.Height);
        if (where != null && dot) h2 = Math.Max(h2, 8);
        double text2W = Math.Max(0, colW - auto);
        result.Texts.Add(new Piece { Text = Fit(ft2, text2W), At = new Point(left, y2) });
        double x = left + text2W;
        void Icon(FormattedText? f)
        {
            if (f == null) return;
            x += 6;
            result.Texts.Add(new Piece { Text = f, At = new Point(x, y2 + (h2 - f.Height) / 2) });
            x += f.WidthIncludingTrailingWhitespace;
        }
        Icon(attach);
        Icon(pin);
        if (flag != null)
        {
            if (due != null)
            {
                x += 8;
                result.Texts.Add(new Piece { Text = due, At = new Point(x, y2 + (h2 - due.Height) / 2) });
                x += due.WidthIncludingTrailingWhitespace;
            }
            Icon(flag);
        }
        result.WhereRect = Rect.Empty;
        if (where != null)
        {
            double stackH = Math.Max(where.Height, dot ? 8 : 0);
            double stackY = y2 + (h2 - stackH) / 2;
            x += 8;
            result.WhereRect = new Rect(x, stackY, whereStackW - 8, stackH);
            if (dot)
            {
                var dotRect = new Rect(x, stackY + (stackH - 8) / 2 + 1, 7, 7);
                result.DotAt = new Point(dotRect.X + dotRect.Width / 2, dotRect.Y + dotRect.Height / 2);
                result.DotBrush = it.AccountBrush;
                x += 7 + 5;
            }
            result.Texts.Add(new Piece { Text = where, At = new Point(x, stackY + (stackH - where.Height) / 2) });
            x += whereW;
            result.WhereTip = it.WhereTip;
        }
        result.ImportantRect = Rect.Empty;
        if (important != null)
        {
            x += 6;
            result.ImportantRect = new Rect(x, y2 + (h2 - important.Height) / 2, important.WidthIncludingTrailingWhitespace, important.Height);
            result.Texts.Add(new Piece { Text = important, At = result.ImportantRect.TopLeft });
        }
        double y = y2 + h2;

        // Anteprima
        if (it.PreviewVisibility == Visibility.Visible)
        {
            var preview = Make(it.Preview, small, secondary, FontWeights.Normal);
            double previewH;
            if (it.PreviewWrapping == TextWrapping.Wrap)
            {
                preview.MaxTextWidth = Math.Max(1, colW);
                preview.MaxLineCount = 2; // l'altezza fissa taglia alla seconda riga, con i puntini
                preview.Trimming = TextTrimming.CharacterEllipsis;
                previewH = it.PreviewHeight;
            }
            else
            {
                Fit(preview, colW);
                previewH = it.Preview.Length == 0 ? Make("A", small, secondary, FontWeights.Normal).Height : preview.Height; // anche vuota la riga ha la sua altezza
            }
            result.Texts.Add(new Piece { Text = preview, At = new Point(left, y) });
            y += previewH;
        }

        // Categorie e pastiglie degli allegati (WrapPanel, Margin 0,4,0,0)
        categories ??= it.CategoryChips;
        if (categories.Count > 0) y = Chips(result, y, left, colW, categories, null);
        var attachments = it.AttachmentChips;
        if (attachments.Count > 0) y = Chips(result, y, left, colW, null, attachments);

        // Altezza: la colonna di testo, o l'immagine (34) o la casella, se più alte
        double avatarH = avatarVisible || showCheck ? 34 : 0;
        if (showCheck && check != null) avatarH = Math.Max(avatarH, 6 + check.DesiredSize.Height);
        double content = Math.Max(y - top, avatarH);
        result.Height = pad.Top + content + pad.Bottom;
        result.DrawBar = it.UnreadVisibility == Visibility.Visible;
        result.Bar = new Rect(pad.Left + 0.5, top + 2, 3, Math.Max(0, content - 4)); // 3 DIP al centro dei 4 della colonna
        if (avatarVisible)
        {
            result.Avatar = new Rect(pad.Left + 4 + rowLeft + 8, top, 34, 34);
            result.DrawAvatar = !showCheck; // con la casella l'immagine è nascosta (Hidden), ma il suo posto resta
            if (result.DrawAvatar)
            {
                result.AvatarBrush = it.AvatarBrush;
                result.Logo = it.Logo;
                if (result.Logo == null)
                {
                    var initials = Make(it.Initials, 13, Brushes.White, FontWeights.SemiBold);
                    var box = result.Avatar;
                    result.Texts.Add(new Piece { Text = initials, At = new Point(box.X + (34 - initials.WidthIncludingTrailingWhitespace) / 2, box.Y + (34 - initials.Height) / 2) });
                }
            }
        }
        checkRect = showCheck && check != null ? new Rect(pad.Left + 4 + rowLeft + 14, top + 6, check.DesiredSize.Width, check.DesiredSize.Height) : Rect.Empty;
        return result;
    }

    Rect chevronRect = Rect.Empty, actionsRect = Rect.Empty, checkRect = Rect.Empty;

    /// <summary>Una fila di pastiglie che vanno a capo come un WrapPanel; restituisce la y dopo.</summary>
    double Chips(Layout result, double y, double left, double colW, List<CategoryChip>? cats, List<AttachmentChip>? files)
    {
        y += 4; // margine sopra dell'ItemsControl
        double x = 0, rowH = 0, rowsTop = 0;
        var border = Res("CardStrokeColorDefaultBrush", Brushes.LightGray);
        var textBrush = Res("TextFillColorPrimaryBrush", Brushes.Black);
        int count = cats?.Count ?? files!.Count;
        for (int i = 0; i < count; i++)
        {
            var chip = new ChipBox();
            double w, h, mr;
            if (cats != null)
            {
                var c = cats[i];
                var text = Make(c.Text, 11, c.Foreground, FontWeights.Normal);
                double tw = Math.Min(text.WidthIncludingTrailingWhitespace, 140);
                Fit(text, tw);
                w = 14 + tw;
                h = 3 + text.Height;
                mr = 5;
                chip.Fill = c.Background;
                chip.Tip = c.Name;
                chip.Pieces.Add(new Piece { Text = text, At = new Point(7, 1) });
            }
            else
            {
                var f = files![i];
                var glyph = Make(f.Glyph, 12, f.Brush, FontWeights.Normal, true);
                var name = Make(f.Name, 11, textBrush, FontWeights.Normal);
                double inner = glyph.WidthIncludingTrailingWhitespace + 5 + name.WidthIncludingTrailingWhitespace;
                double innerH = Math.Max(glyph.Height, name.Height);
                w = Math.Min(170, 2 + 12 + inner);
                h = 2 + 4 + innerH;
                mr = 6;
                chip.Border = new Pen(border, 1);
                chip.Attachment = f;
                chip.Tip = f.Name;
                chip.Pieces.Add(new Piece { Text = glyph, At = new Point(1 + 6, 1 + 2 + (innerH - glyph.Height) / 2) });
                chip.Pieces.Add(new Piece { Text = name, At = new Point(1 + 6 + glyph.WidthIncludingTrailingWhitespace + 5, 1 + 2 + (innerH - name.Height) / 2) });
                chip.HasClip = true;
                chip.Clip = new Rect(7, 3, Math.Max(0, w - 14), innerH); // il pannello interno taglia il nome troppo lungo
            }
            double outerW = w + mr, outerH = h + 2;
            if (x > 0 && x + outerW > colW + 0.001)
            {
                rowsTop += rowH;
                x = 0;
                rowH = 0;
            }
            chip.Rect = new Rect(left + x, y + rowsTop, w, h);
            result.Chips.Add(chip);
            x += outerW;
            rowH = Math.Max(rowH, outerH);
        }
        return y + rowsTop + rowH;
    }

    Layout Ensure(double width)
    {
        if (layout == null || Math.Abs(layoutWidth - width) > 0.01)
        {
            layout = Compute(width);
            layoutWidth = width;
        }
        return layout;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        CursorProbe.Hit(CursorProbe.C.RowMeasure);
        if (item == null) return new Size(0, 0);
        double width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        layout = null;
        var l = Ensure(width);
        return new Size(width, l.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (item == null) return finalSize;
        Ensure(finalSize.Width); // la disposizione, se manca: mette chevronRect, actionsRect e checkRect
        if (showChevron && chevron != null) chevron.Arrange(chevronRect);
        if (showActions && actions != null) actions.Arrange(actionsRect);
        if (showCheck && check != null) check.Arrange(checkRect);
        return finalSize;
    }

    // ---------------------------------------------------------------- disegno

    protected override void OnRender(DrawingContext dc)
    {
        if (item == null) return;
        var l = Ensure(ActualWidth > 0 ? ActualWidth : RenderSize.Width);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Math.Max(0, RenderSize.Width), Math.Max(0, RenderSize.Height))); // per il mouse
        if (l.DrawBar)
        {
            Guide(dc, l.Bar);
            dc.DrawRoundedRectangle(Res("AccentFillColorDefaultBrush", Brushes.DodgerBlue), null, l.Bar, 1.5, 1.5);
            dc.Pop();
        }
        if (l.DrawAvatar && l.AvatarBrush != null)
        {
            var center = new Point(l.Avatar.X + l.Avatar.Width / 2, l.Avatar.Y + l.Avatar.Height / 2);
            Guide(dc, l.Avatar);
            dc.DrawRoundedRectangle(l.AvatarBrush, null, l.Avatar, 17, 17);
            if (l.Logo != null)
            {
                dc.DrawEllipse(Brushes.White, null, center, 17, 17);
                RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
                dc.DrawEllipse(new ImageBrush(l.Logo) { Stretch = Stretch.UniformToFill }, null, center, 17, 17);
                dc.DrawEllipse(null, LogoRing, center, 16.5, 16.5);
            }
            dc.Pop();
        }
        if (l.DotAt is { } dot)
        {
            Guide(dc, new Rect(dot.X - 3.5, dot.Y - 3.5, 7, 7));
            dc.DrawEllipse(l.DotBrush, null, dot, 3.5, 3.5);
            dc.Pop();
        }
        foreach (var chip in l.Chips)
        {
            Guide(dc, chip.Rect);
            dc.DrawRoundedRectangle(chip.Fill ?? Brushes.Transparent, null, chip.Rect, 4, 4);
            if (chip.Border != null)
                dc.DrawRoundedRectangle(null, chip.Border, new Rect(chip.Rect.X + 0.5, chip.Rect.Y + 0.5, Math.Max(0, chip.Rect.Width - 1), Math.Max(0, chip.Rect.Height - 1)), 3.5, 3.5);
            dc.Pop();
            if (chip.HasClip) dc.PushClip(new RectangleGeometry(new Rect(chip.Rect.X + chip.Clip.X, chip.Rect.Y + chip.Clip.Y, chip.Clip.Width, chip.Clip.Height)));
            foreach (var p in chip.Pieces) dc.DrawText(p.Text, new Point(chip.Rect.X + p.At.X, chip.Rect.Y + p.At.Y));
            if (chip.HasClip) dc.Pop();
        }
        foreach (var piece in l.Texts) dc.DrawText(piece.Text, piece.At);
    }

    /// <summary>I bordi di una forma sui pixel del dispositivo, come i Border di WPF (guide di allineamento; poi dc.Pop).</summary>
    static void Guide(DrawingContext dc, Rect r) =>
        dc.PushGuidelineSet(new GuidelineSet(new[] { r.Left, r.Right }, new[] { r.Top, r.Bottom }));

    // ---------------------------------------------------------------- suggerimenti, clic sulle pastiglie, accessibilità

    ChipBox? ChipAt(Point p)
    {
        if (layout == null) return null;
        foreach (var chip in layout.Chips) if (chip.Rect.Contains(p)) return chip;
        return null;
    }

    string? TipAt(Point p)
    {
        if (layout == null) return null;
        if (ChipAt(p) is { } chip) return chip.Tip;
        if (!layout.WhereRect.IsEmpty && layout.WhereRect.Contains(p) && layout.WhereTip.Length > 0) return layout.WhereTip;
        if (!layout.ImportantRect.IsEmpty && layout.ImportantRect.Contains(p)) return "Importanza alta";
        return null;
    }

    protected override void OnToolTipOpening(ToolTipEventArgs e)
    {
        CursorProbe.Hit(CursorProbe.C.ToolTipOpening);
        var tip = TipAt(Mouse.GetPosition(this));
        if (tip == null)
        {
            e.Handled = true;
            return;
        }
        if (ToolTip is ToolTip tooltip) tooltip.Content = tip;
        base.OnToolTipOpening(e);
    }


    protected override void OnQueryCursor(QueryCursorEventArgs e)
    {
        CursorProbe.Hit(CursorProbe.C.QueryCursor);
        base.OnQueryCursor(e);
        if (ChipAt(e.GetPosition(this)) is { Attachment: not null })
        {
            e.Cursor = Cursors.Hand;
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (item != null && ChipAt(e.GetPosition(this)) is { Attachment: { } file })
        {
            e.Handled = true;
            Owner?.RowChipClicked(item, file);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

    sealed class RowPeer(MessageRowElement owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(MessageRowElement);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string GetNameCore() => owner.item?.AutomationName ?? "";
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
    }
}
