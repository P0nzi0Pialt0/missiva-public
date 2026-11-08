#if DEBUG
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Ritocchi dell'8 novembre 2026, chiesti dall'autore: le icone sulla riga dell'elenco come nel nuovo Outlook (con un video),
/// la barra delle applicazioni nascosta automaticamente che non usciva con Missiva ingrandita, e Mostra nella cartella
/// anche per gli allegati aperti con un programma (questo nel gruppo ritocchi, RitDownloadsSources). La riga si misura
/// davvero, senza finestra; la finestra della barra si crea senza mai mostrarla. Nessuna rete. Con "Missiva.exe
/// --prova-audit risultato.txt ritocchi-novembre" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void NovemberTouchUps()
    {
        Report.Add("--- Ritocchi dell'8 novembre: le icone della riga come nel nuovo Outlook, la barra delle applicazioni nascosta");
        RnIcons();
        OnUiThread(RnRowAsync);
        RnTaskbarClient();
        RnTaskbarHook();
    }

    /// <summary>I dieci disegni delle Fluent UI System Icons: dentro il quadrato di 20, e la versione piena copre più della vuota.</summary>
    static void RnIcons()
    {
        var pairs = new (string Name, Geometry Regular, Geometry Filled)[]
        {
            ("cestino", FluentIcons.Delete, FluentIcons.DeleteFilled), ("bandierina", FluentIcons.Flag, FluentIcons.FlagFilled),
            ("puntina", FluentIcons.Pin, FluentIcons.PinFilled), ("busta", FluentIcons.Mail, FluentIcons.MailFilled),
            ("busta aperta", FluentIcons.MailRead, FluentIcons.MailReadFilled),
        };
        var bad = new List<string>();
        foreach (var (name, regular, filled) in pairs)
        {
            foreach (var g in new[] { regular, filled })
            {
                var b = g.Bounds;
                if (b.IsEmpty || b.Left < 0 || b.Top < 0 || b.Right > FluentIcons.Grid || b.Bottom > FluentIcons.Grid || !g.IsFrozen) bad.Add($"{name}: {b}");
            }
            double r = regular.GetArea(), f = filled.GetArea();
            if (!(f > r * 1.3)) bad.Add($"{name}: piena {f:0.0} contro vuota {r:0.0}");
        }
        Check(bad.Count == 0, $"icone della riga: dieci disegni dentro il quadrato di 20, la versione piena copre più della vuota ({(bad.Count == 0 ? "tutte" : string.Join("; ", bad))})");
    }

    /// <summary>
    /// La riga vera (MessageRowElement), misurata senza finestra, ferma e con il mouse sopra: la data sulla seconda riga, a
    /// destra; le icone di stato in alto; con il mouse sopra le azioni in alto, al posto delle icone di stato, e il cestino dopo
    /// la data, nella sua colonna, con le azioni che finiscono dove finisce la data, come nel video di Outlook; l'altezza
    /// della riga uguale. Le icone: vuote da ferme, piene sotto il mouse, rosse per Contrassegna ed Elimina.
    /// </summary>
    static async Task RnRowAsync()
    {
        await Task.Yield();
        var account = new AccountItem(new GraphProvider(new MailAccount { Kind = AccountKind.Microsoft, Email = "ada.righe@studio-prova.example" }));
        var summary = new MessageSummary
        {
            Id = "AQMkRigheNovembre", FolderId = "inbox", From = new Address { Name = "Mario Rossi", Email = "mario.rossi@studio-prova.example" },
            Subject = "Preventivo per la cucina", Preview = "Ciao Ada, come promesso ti mando il preventivo", Date = DateTime.Now.AddMinutes(-5), HasAttachments = true,
        };
        var item = new MessageItem(summary, account);
        var row = new MessageRowElement();
        var host = new Border { Width = 420, DataContext = item, Child = row };
        void Lay()
        {
            host.Measure(new Size(420, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
        }
        Lay();
        var still = row.TestLayout();
        row.TestHover(true);
        Lay();
        var over = row.TestLayout();

        const double right = 420 - 4; // la larghezza meno il margine destro della riga (Padding.Right)
        var date = item.DateText;
        bool Has(List<(string Text, Point At, double Width)> texts, string text, out (string Text, Point At, double Width) piece)
        {
            piece = texts.FirstOrDefault(t => t.Text == text);
            return piece.Text != null;
        }
        bool stillOk = Has(still.Texts, "Mario Rossi ", out var sender) && Has(still.Texts, item.Line2, out var subject) && Has(still.Texts, date, out var stillDate)
                       && Has(still.Texts, "", out var clip)
                       && stillDate.At.Y > sender.At.Y && Math.Abs(stillDate.At.X + stillDate.Width - right) < 0.5 && clip.At.Y < subject.At.Y
                       && still.Actions.IsEmpty && still.Delete.IsEmpty;
        bool overOk = Has(over.Texts, date, out var overDate) && !Has(over.Texts, "", out _)
                      && !over.Actions.IsEmpty && !over.Delete.IsEmpty
                      && over.Actions.Top + over.Actions.Height / 2 < overDate.At.Y // le azioni sulla riga 1
                      && over.Delete.Top + over.Delete.Height / 2 > overDate.At.Y // il cestino sulla riga 2
                      && over.Delete.Left >= overDate.At.X + overDate.Width + 3 && over.Delete.Right <= right + 0.5
                      && Math.Abs(over.Actions.Right - (overDate.At.X + overDate.Width)) < 0.5
                      && Math.Abs(over.Height - still.Height) < 0.01;
        var names = over.Buttons.Select(b => b.Name).ToList();
        bool icons = over.Buttons.Count == 4 && names.SequenceEqual(new[] { item.ReadTip, item.FlagTip, item.PinTip, T("Elimina") })
                     && over.Buttons[0].Idle == FluentIcons.MailRead && over.Buttons[0].Over == FluentIcons.MailReadFilled
                     && over.Buttons[1].Idle == FluentIcons.Flag && over.Buttons[1].Over == FluentIcons.FlagFilled && over.Buttons[1].OverBrush == "CriticalBrush"
                     && over.Buttons[2].Idle == FluentIcons.Pin && over.Buttons[2].Over == FluentIcons.PinFilled
                     && over.Buttons[3].Idle == FluentIcons.Delete && over.Buttons[3].Over == FluentIcons.DeleteFilled && over.Buttons[3].OverBrush == "CriticalBrush";
        // Contrassegnato: la bandierina piena anche ferma, e sulla riga ferma la bandierina rossa piena in alto.
        item.IsFlagged = true;
        Lay();
        bool flaggedButton = row.TestLayout().Buttons is [_, var flag, _, _] && flag.Idle == FluentIcons.FlagFilled;
        row.TestHover(false);
        Lay();
        var flagged = row.TestLayout();
        bool flaggedStill = flagged.Shapes.Any(s => s.Shape == FluentIcons.FlagFilled && s.Box.Top + s.Box.Height / 2 < subject.At.Y) && flagged.Buttons.Count == 0;
        Check(stillOk && overOk && icons && flaggedButton && flaggedStill,
              $"riga come nel nuovo Outlook: ferma, la data sulla seconda riga a destra e la graffetta in alto ({stillOk}); con il mouse sopra le azioni in alto al posto della graffetta, " +
              $"il cestino dopo la data nella sua colonna, le azioni che finiscono dove finisce la data, la stessa altezza ({overOk}: {still.Height:0.0} e {over.Height:0.0}); " +
              $"busta, bandierina, puntina e cestino vuoti, pieni sotto il mouse, rossi bandierina e cestino ({icons}); contrassegnato, la bandierina piena anche ferma, " +
              $"sul pulsante ({flaggedButton}) e in alto sulla riga ({flaggedStill})");
    }

    /// <summary>
    /// La barra delle applicazioni nascosta automaticamente: con Missiva ingrandita l'area client lascia libero un pixel sul
    /// bordo dello schermo dove sta la barra, e solo lì; senza barre nascoste nulla cambia.
    /// </summary>
    static void RnTaskbarClient()
    {
        var monitor = new AutoHideTaskbar.Rect32 { Left = 0, Top = 0, Right = 1440, Bottom = 900 };
        var proposed = new AutoHideTaskbar.Rect32 { Left = -8, Top = -8, Right = 1448, Bottom = 908 }; // ingrandita: sborda del bordo invisibile
        var bottom = AutoHideTaskbar.Client(proposed, monitor, AutoHideTaskbar.Edges.Bottom);
        var left = AutoHideTaskbar.Client(proposed, monitor, AutoHideTaskbar.Edges.Left);
        var none = AutoHideTaskbar.Client(proposed, monitor, AutoHideTaskbar.Edges.None);
        var two = AutoHideTaskbar.Client(proposed, monitor, AutoHideTaskbar.Edges.Top | AutoHideTaskbar.Edges.Right);
        bool ok = bottom is { Left: -8, Top: -8, Right: 1448, Bottom: 899 } && left is { Left: 1, Top: -8, Right: 1448, Bottom: 908 }
                  && none.Equals(proposed) && two is { Left: -8, Top: 1, Right: 1439, Bottom: 908 };
        Check(ok, $"barra nascosta: l'area client di Missiva ingrandita lascia un pixel libero solo sul bordo della barra (in basso {bottom.Bottom}, a sinistra {left.Left}, " +
                  $"in alto e a destra {two.Top} e {two.Right}); senza barre nascoste è quella di prima");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RnCalcSize { public AutoHideTaskbar.Rect32 Window, Before, Client; public IntPtr Position; }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, ref RnCalcSize lParam);

    /// <summary>
    /// Il gancio sulla finestra vera, con la barra del titolo di Missiva (WindowChrome come in MainWindow.xaml), creata e mai
    /// mostrata: WM_NCCALCSIZE arriva prima al gancio di Missiva, che lascia il pixel libero e risponde come la WindowChrome;
    /// senza barre nascoste risponde la WindowChrome, che lascia la finestra intera. Se il gancio arrivasse dopo quello
    /// della WindowChrome, il pixel non ci sarebbe mai.
    /// </summary>
    static void RnTaskbarHook()
    {
        var window = new Window { ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, Width = 300, Height = 200 };
        System.Windows.Shell.WindowChrome.SetWindowChrome(window, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 44, GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = true, ResizeBorderThickness = new Thickness(6),
        });
        var handle = new WindowInteropHelper(window).EnsureHandle();
        AutoHideTaskbar.Attach(window);
        try
        {
            AutoHideTaskbar.TestZoomed = true;
            var screen = new AutoHideTaskbar.Rect32();
            AutoHideTaskbar.Rect32 Ask(AutoHideTaskbar.Edges edges, AutoHideTaskbar.Rect32 proposed, out long answer)
            {
                AutoHideTaskbar.TestEdges = monitor =>
                {
                    screen = monitor;
                    return edges;
                };
                var calc = new RnCalcSize { Window = proposed };
                answer = (long)SendMessage(handle, 0x0083 /* WM_NCCALCSIZE */, new IntPtr(1), ref calc);
                return calc.Window;
            }
            // Prima lo schermo vero (quello più vicino), poi la finestra ingrandita su di lui: sborda di 8 pixel per lato.
            Ask(AutoHideTaskbar.Edges.None, new AutoHideTaskbar.Rect32 { Left = 0, Top = 0, Right = 100, Bottom = 100 }, out _);
            var maximized = new AutoHideTaskbar.Rect32 { Left = screen.Left - 8, Top = screen.Top - 8, Right = screen.Right + 8, Bottom = screen.Bottom + 8 };
            var hidden = Ask(AutoHideTaskbar.Edges.Bottom, maximized, out long hiddenAnswer);
            var plain = Ask(AutoHideTaskbar.Edges.None, maximized, out long plainAnswer);
            bool ok = hidden.Bottom == screen.Bottom - 1 && hidden.Top == maximized.Top && hidden.Right == maximized.Right && plain.Equals(maximized) && hiddenAnswer == plainAnswer;
            Check(ok, $"barra nascosta, sulla finestra vera: il gancio di Missiva arriva prima della WindowChrome e lascia libero il pixel in basso ({hidden.Bottom} con lo schermo che finisce a " +
                      $"{screen.Bottom}, risposta 0x{hiddenAnswer:X}); senza barre nascoste risponde la WindowChrome e la finestra resta intera ({plain.Bottom}, 0x{plainAnswer:X})");
        }
        finally
        {
            AutoHideTaskbar.TestZoomed = false;
            AutoHideTaskbar.TestEdges = null;
            window.Close();
        }
    }
}
#endif
