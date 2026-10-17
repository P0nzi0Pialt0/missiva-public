using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Il pannellino sotto un pulsante della barra del titolo (l'aggiornamento, gli scaricamenti; ritocchi del 16 ottobre 2026):
/// una piccola finestra senza bordi, come il pannello dei filtri della ricerca (MainWindow.SearchFilters.cs), perché un
/// Popup di WPF non tiene bene il fuoco e la tastiera; qui Tab passa da un pulsante all'altro. Allineata a destra con il
/// pulsante, sotto la barra del titolo. Si chiude con Esc, con un altro clic sul pulsante o nella finestra principale,
/// passando a un'altra app, spostando, riducendo o ingrandendo la finestra.
/// </summary>
sealed class TitleFlyout
{
    readonly Window owner;
    readonly FrameworkElement anchor;
    readonly string name;
    readonly double width;
    readonly Border frame;
    Window? window;
    DateTime closed;

    public bool IsOpen => window != null;

    /// <param name="name">Il nome del pannello per UI Automation e per il registro ("aggiornamento", "scaricamenti").</param>
    /// <param name="width">La larghezza fissa in DIP: si sa prima di mostrarla, e il bordo destro resta sotto quello del pulsante.</param>
    public TitleFlyout(Window owner, FrameworkElement anchor, string name, double width)
    {
        this.owner = owner;
        this.anchor = anchor;
        this.name = name;
        this.width = width;
        frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Width = width, Padding = new Thickness(16, 12, 16, 12) };
        frame.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorDefaultBrush");
        AutomationProperties.SetName(frame, name);
        frame.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close("Esc");
            anchor.Focus();
        };
        owner.LocationChanged += (_, _) => Close("finestra spostata");
        owner.StateChanged += (_, _) => Close("finestra ridotta o ingrandita");
        owner.PreviewMouseDown += (_, _) => { if (!anchor.IsMouseOver) Close("clic nella finestra"); };
    }

    /// <summary>
    /// Il clic sul pulsante: apre il pannello con quel contenuto, o lo chiude se è aperto. Il clic stesso ha già chiuso un
    /// pannello aperto (non è più la finestra attiva): appena chiuso, non lo si riapre.
    /// </summary>
    public void Toggle(Func<UIElement> content)
    {
        if (window != null) Close("pulsante");
        else if (DateTime.UtcNow - closed > TimeSpan.FromMilliseconds(400)) Show(content());
    }

    /// <summary>Il contenuto del pannello aperto cambia (lo stato dell'aggiornamento, un elemento in più): resta aperto dov'è.</summary>
    public void Update(UIElement content)
    {
        if (window != null) frame.Child = content;
    }

    public void Show(UIElement content)
    {
        frame.Child = content;
        if (window != null) return;
        // Il bordo destro sotto quello del pulsante, 4 DIP più giù (in DIP, anche a 200%).
        var corner = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight + 4));
        var toDip = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var at = toDip.Transform(corner);
        window = new Window
        {
            Owner = owner, Title = name, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            AllowsTransparency = true, Background = Brushes.Transparent, SizeToContent = SizeToContent.Height, Width = width,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = at.X - width, Top = at.Y, Content = frame,
        };
#if DEBUG
        // Le foto (--foto): non prende il fuoco; resta sotto il suo pulsante, quindi fuori dallo schermo con la finestra
        // principale (DevTools.HideForPhotos), e la foto la mette dov'è.
        if (DevTools.Photos) window.ShowActivated = false;
#endif
        // Passando a un'altra app si chiude; non subito dopo l'apertura, quando Windows può negare il fuoco a una finestra
        // che non è in primo piano (e WPF manda Deactivated appena la mostra), come per il pannello dei filtri.
        var shown = DateTime.UtcNow;
        var opened = window;
        opened.Deactivated += (_, _) =>
        {
            if (DateTime.UtcNow - shown > TimeSpan.FromMilliseconds(600)) Close("finestra non più attiva");
        };
        opened.Closed += (_, _) =>
        {
            opened.Content = null; // il riquadro torna libero per la prossima volta
            if (window == opened) window = null;
        };
        opened.Loaded += (_, _) => frame.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Log.Write($"barra del titolo: pannello {name} aperto");
        opened.Show();
    }

    public void Close(string why)
    {
        if (window is not { } open) return;
        Log.Write($"barra del titolo: pannello {name} chiuso ({why})");
        window = null;
        closed = DateTime.UtcNow;
        open.Content = null;
        open.Close();
    }
}
