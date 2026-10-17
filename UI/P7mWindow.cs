using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Un file firmato .p7m aperto in Missiva, come fanno i programmi di firma: in alto chi l'ha firmato, quando e com'è la
/// firma (verde, gialla, rossa come per la posta certificata), sotto il contenuto. Le fatture elettroniche (XML) e i
/// testi si leggono qui, come testo; gli altri documenti (PDF...) si salvano o si aprono con il loro programma, con il
/// marchio "scaricato da Internet" e la conferma per i tipi che possono avviare programmi, come gli allegati.
/// </summary>
public sealed class P7mWindow : Window
{
    readonly P7m.Result result;

    /// <param name="text">Il contenuto da mostrare come testo (fatture XML, testi), già preparato in sottofondo; null per gli altri.</param>
    P7mWindow(P7m.Result result, string? text)
    {
        this.result = result;
        Title = result.Name;
        Width = 860;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = result.Name, FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var (bandLine, tone) = P7m.Band(result);
        var band = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 10, 0, 10) };
        bool dark = ReadingView.Dark;
        (band.Background, var ink) = tone switch
        {
            "ok" => (Brush(dark ? "#1F3B20" : "#DFF6DD"), Brush(dark ? "#9FD89F" : "#0E5C0E")),
            "danger" => (Brush(dark ? "#442726" : "#FDE7E9"), Brush(dark ? "#FF99A4" : "#A4262C")),
            _ => (Brush(dark ? "#433519" : "#FFF4CE"), Brush(dark ? "#FCE100" : "#5C4400")),
        };
        var bandText = new TextBlock { Text = bandLine, TextWrapping = TextWrapping.Wrap, Foreground = ink };
        AutomationProperties.SetName(bandText, bandLine);
        band.Child = bandText;
        head.Children.Add(band);
        DockPanel.SetDock(head, Dock.Top);
        panel.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = "Salva il documento", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => Save();
        buttons.Children.Add(save);
        if (text == null)
        {
            var open = new Button { Content = "Apri il documento", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
            open.SetResourceReference(StyleProperty, "AccentButtonStyle");
            open.Click += (_, _) => Open();
            buttons.Children.Add(open);
        }
        var close = new Button { Content = "Chiudi", MinWidth = 100, IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        if (text != null)
        {
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true,
            };
            AutomationProperties.SetName(box, "Contenuto del documento firmato");
            panel.Children.Add(box);
        }
        else
        {
            var note = new TextBlock { Text = $"Il documento dentro la firma ({result.Content.Length / 1024} KB) si apre con il suo programma, o si salva.", TextWrapping = TextWrapping.Wrap };
            note.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            panel.Children.Add(note);
        }
        Content = panel;
    }

    static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>
    /// Apre il file firmato in Missiva; se non si legge, l'errore spiegato. Busta, firma, catena e testo si preparano in
    /// sottofondo: un PDF firmato da 20 MB fermava la finestra per mezzo secondo.
    /// </summary>
    public static async Task ShowAsync(Window? owner, byte[] file, string name)
    {
        P7m.Result result;
        string? text;
        try
        {
            (result, text) = await Task.Run(() =>
            {
                var opened = P7m.Open(file, name);
                return (opened, P7m.IsText(opened) ? P7m.Text(opened) : null);
            });
        }
        catch (Exception ex)
        {
            Log.Error("file firmato", ex);
            Dialog.Show(owner, "File firmato non leggibile", ex is MailException ? ex.Message : "Il file firmato non si legge: " + ex.Message);
            return;
        }
        Log.Write($"file firmato {Log.Private(result.Name)}: firma {result.Signature}, {result.Layers} buste, {result.Content.Length / 1024} KB");
        var window = new P7mWindow(result, text);
        if (owner is { IsVisible: true }) window.Owner = owner;
#if DEBUG
        DevTools.HideForPhotos(window); // le foto della pagina GitHub (--foto): non si vede e non prende il fuoco
#endif
        window.Show();
    }

    /// <summary>
    /// Salva il documento dove si sceglie, con il marchio web, dal gestore degli scaricamenti (ritocchi del 16 ottobre 2026): è
    /// già in memoria, quindi l'elemento finisce subito, ma c'è, con Apri e Mostra nella cartella; un errore lo dice lui.
    /// </summary>
    void Save()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = Safety.SafeFileName(result.Name) };
        if (dialog.ShowDialog(this) != true) return;
        var content = result.Content;
        _ = DownloadList.Session.SaveAsync(dialog.FileName, content.Length, (progress, _) => Task.FromResult(content));
    }

    /// <summary>Come gli allegati: nella cartella temporanea, con il marchio web, e la conferma per i tipi pericolosi e per le pagine web.</summary>
    void Open()
    {
        var name = Safety.SafeFileName(result.Name);
        if (Safety.IsDangerous(name) &&
            Dialog.Ask(this, "Aprire un file che può avviare programmi?",
                $"\"{name}\" è un tipo di file che può eseguire codice sul PC. Aprilo solo se lo aspettavi e ti fidi di chi lo ha firmato.", "Apri comunque") != Choice.Primary)
            return;
        if (!Dialog.ConfirmOpenWebPage(this, name)) return;
        try
        {
            var temp = Safety.WriteTemp(name, result.Content);
            if (!temp.Marked && !Dialog.ConfirmOpenUnmarked(this, name))
            {
                temp.Discard();
                return;
            }
            Process.Start(new ProcessStartInfo(temp.Release()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("documento firmato", ex);
            Dialog.Show(this, "Documento non aperto", ex.Message);
        }
    }
}
