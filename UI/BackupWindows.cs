using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Missiva.UI;

/// <summary>
/// Copia di sicurezza (Impostazioni, Generali): le finestre di Esporta e Importa attorno a Backup. La password si legge
/// dalle caselle come vettore di caratteri, passa alla derivazione della chiave e viene azzerata: non diventa mai un testo
/// di .NET, non va nel registro né su disco.
/// </summary>
public static class BackupUi
{
    public const int MinPassword = 8;

    /// <summary>Perché la password scelta non va, o null se va: corta, o scritta in due modi diversi.</summary>
    internal static string? PasswordProblem(ReadOnlySpan<char> first, ReadOnlySpan<char> second) =>
        first.Length < MinPassword ? $"La password deve avere almeno {MinPassword} caratteri."
        : !first.SequenceEqual(second) ? "Le due password non sono uguali: riscrivile."
        : null;

    /// <summary>I caratteri scritti in una casella della password, senza passare da un testo di .NET (che non si può azzerare).</summary>
    internal static char[] Chars(PasswordBox box)
    {
        var secure = box.SecurePassword;
        var chars = new char[secure.Length];
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try { Marshal.Copy(pointer, chars, 0, chars.Length); }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
        return chars;
    }

    /// <summary>Prove a schermo (solo Debug): con MISSIVA_COPIA_FILE il file è quello, senza le finestre di Windows per scegliere dove.</summary>
    static string? TestFile =>
#if DEBUG
        Environment.GetEnvironmentVariable("MISSIVA_COPIA_FILE") is { Length: > 0 } path ? path :
#endif
        null;

    const string Filter = "Copia di sicurezza di Missiva (*" + Backup.Extension + ")|*" + Backup.Extension;

    // ---------------------------------------------------------------- esporta

    public static void Export(Window owner) => new BackupExportWindow { Owner = owner }.ShowDialog();

    internal static string? AskWhereToSave(Window owner)
    {
        if (TestFile != null) return TestFile;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Esporta una copia di sicurezza", Filter = Filter, DefaultExt = Backup.Extension, AddExtension = true,
            FileName = $"Missiva {DateTime.Now:yyyy-MM-dd}{Backup.Extension}",
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    /// <summary>Che cosa dire dopo un'esportazione riuscita: dov'è, che cosa contiene, che cosa no, e quello che non è potuto entrare.</summary>
    internal static string ExportedText(string path, BackupSummary summary, IReadOnlyList<string> notes)
    {
        var text = $"{path}\n\nContiene: {summary.Contents}.\n\n" +
                   "Non contiene la posta, che si riscarica dai server, né gli accessi agli account: dove la importi ogni account chiede di nuovo l'accesso" +
                   (summary.Passwords > 0 ? $", tranne {(summary.Passwords == 1 ? "l'account IMAP di cui hai incluso la password" : $"i {summary.Passwords} account IMAP di cui hai incluso la password")}." : ".") +
                   "\n\nSenza la password la copia non si apre, e Missiva non può recuperarla: conservala dove la ritrovi.";
        if (notes.Count > 0) text += "\n\nAttenzione: " + string.Join(" ", notes);
        return text;
    }

    // ---------------------------------------------------------------- importa

    /// <summary>
    /// Sceglie il file, chiede la password, dice che cosa c'è dentro e chiede se sostituire tutto; poi lascia l'importazione
    /// in attesa del riavvio (Backup.SavePending) e propone di riavviare. Finché Missiva non riparte non cambia nulla.
    /// </summary>
    public static void Import(Window owner)
    {
        var path = TestFile;
        if (path == null)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Importa una copia di sicurezza", Filter = Filter + "|Tutti i file|*.*" };
            if (dialog.ShowDialog(owner) != true) return;
            path = dialog.FileName;
        }
        byte[] file;
        try
        {
            if (new FileInfo(path).Length > int.MaxValue / 2) throw new BackupException(BackupProblem.NotABackup, "Questo file è troppo grande per essere una copia di sicurezza di Missiva.");
            file = File.ReadAllBytes(path);
            BackupFile.Check(file); // prima della password: un file sbagliato, incompleto o di una versione più nuova si dice subito
        }
        catch (Exception ex) when (ex is BackupException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"copia di sicurezza: file non importabile ({(ex as BackupException)?.Problem.ToString() ?? ex.GetType().Name})");
            Dialog.Show(owner, "Questo file non si può importare", ex is BackupException ? ex.Message : $"Il file non si riesce a leggere: {ex.Message}");
            return;
        }
        var window = new BackupImportWindow(Path.GetFileName(path), file) { Owner = owner };
        if (window.ShowDialog() != true || window.Data is not { } data) return;

        BackupSummary summary;
        try
        {
            summary = Backup.Describe(data, Store.Settings);
        }
        catch (Exception ex)
        {
            // Backup.Read ha già controllato il contenuto: qui non dovrebbe arrivare nulla. Se arriva, un avviso e non
            // un'eccezione sul thread dell'interfaccia (audit dell'11 ottobre, T-16).
            Log.Error("copia di sicurezza: contenuto non descritto", ex);
            Dialog.Show(owner, "Questo file non si può importare", "Il contenuto della copia non si legge: Missiva non la importa, e non è cambiato nulla.");
            return;
        }
        var s = Store.Settings;
        bool hasData = s.Accounts.Count > 0 || s.Templates.Count > 0 || s.Rules.Count > 0 || s.Signatures.Count > 0 || ContactBook.Local.Count > 0;
        if (Confirm(owner, summary, data.App, hasData) != Choice.Primary) return;
        try
        {
            Backup.SavePending(Store.Dir, data);
        }
        catch (Exception ex)
        {
            Log.Error("copia di sicurezza: importazione non preparata", ex);
            Dialog.Show(owner, "Importazione non preparata", $"Missiva non è riuscita a scrivere nella cartella dei dati: non è cambiato nulla.\n\n{ex.Message}");
            return;
        }
        Log.Write($"copia di sicurezza: importazione in attesa del riavvio ({summary.Accounts.Count} account, copia del {summary.Created:yyyy-MM-dd})");
        OfferRestart(owner);
    }

    internal static (string Heading, string Body, string Primary) ConfirmText(BackupSummary summary, string app, bool hasData)
    {
        var what = $"Copia del {summary.When}{(app.Length > 0 ? $", fatta con Missiva {app}" : "")}.\nContiene: {summary.Contents}.";
        // Non solo indirizzi e numeri (audit dell'11 ottobre, T-17): con che server entra ogni account, che cosa fanno le regole,
        // per chi si caricano da sole le immagini e, per prima cosa, quello che conviene guardare prima di dire sì.
        static string List(IEnumerable<string> lines) => string.Join("\n", lines.Select(l => "- " + l));
        if (summary.Warnings.Count > 0) what += "\n\nDa guardare prima di importare:\n" + List(summary.Warnings);
        if (summary.AccountLines.Count > 0) what += "\n\nAccount:\n" + List(summary.AccountLines);
        if (summary.RuleLines.Count > 0) what += "\n\nRegole di Missiva (quelle accese valgono per i messaggi che arrivano, appena Missiva riparte):\n" + List(summary.RuleLines);
        if (summary.ImageSenders.Count > 0)
            what += $"\n\nImmagini esterne mostrate sempre per {(summary.ImageSenders.Count == 1 ? "un mittente" : $"{summary.ImageSenders.Count} mittenti")}: " +
                    string.Join(", ", summary.ImageSenders.Take(12)) + (summary.ImageSenders.Count > 12 ? $" e altri {summary.ImageSenders.Count - 12}" : "") + ".";
        var access = "Gli accessi non sono nella copia: gli account che su questo PC non lo hanno già chiederanno di nuovo l'accesso, con la barra sotto l'account (Accedi di nuovo o Aggiorna password)" +
                     (summary.Passwords > 0 ? $"; {(summary.Passwords == 1 ? "di un account IMAP c'è la password" : $"di {summary.Passwords} account IMAP c'è la password")}, e non la chiederà." : ".");
        const string restart = "Per finire Missiva si chiude e si riapre: la posta si riscarica dai server.";
        return hasData
            ? ("Sostituire tutto con questa copia?",
               $"{what}\n\nImportandola, quello che Missiva ha ora su questo PC (account, impostazioni, firme, modelli, regole, contatti di Missiva, rubrica dei destinatari) viene " +
               "sostituito da quello della copia: le due cose non si uniscono. I dati di adesso non si cancellano: restano nella cartella dei dati di Missiva, messi da parte con la data " +
               $"nel nome.\n\n{access}\n\n{restart}", "Sostituisci tutto")
            : ("Importare questa copia?", $"{what}\n\n{access}\n\n{restart}", "Importa");
    }

    static Choice Confirm(Window owner, BackupSummary summary, string app, bool hasData)
    {
        var (heading, body, primary) = ConfirmText(summary, app, hasData);
        return hasData ? Dialog.AskCancelFirst(owner, heading, body, primary) : Dialog.Ask(owner, heading, body, primary);
    }

    // ---------------------------------------------------------------- i dati di prima di un'importazione

    static readonly System.Globalization.CultureInfo Italian = System.Globalization.CultureInfo.GetCultureInfo("it-IT");

    /// <summary>"3 file", "un file", e di quando: "messi da parte il 12 ottobre 2026", o fra due date se le importazioni sono state più d'una.</summary>
    static string AsideWhat(IReadOnlyList<string> files)
    {
        var days = files.Select(Backup.AsideDate).OfType<DateTime>().Select(d => d.Date).Distinct().Order().ToList();
        string when = days.Count == 0 ? "" : days.Count == 1 ? $" il {days[0].ToString("d MMMM yyyy", Italian)}" : $" fra il {days[0].ToString("d MMMM yyyy", Italian)} e il {days[^1].ToString("d MMMM yyyy", Italian)}";
        return files.Count == 1 ? $"un file, messo da parte{when}" : $"{files.Count} file, messi da parte{when}";
    }

    /// <summary>
    /// La riga della pagina Copia di sicurezza quando nella cartella dei dati ci sono file messi da parte da
    /// un'importazione (Backup.AsideFiles): che cosa sono e che cosa tengono ancora (scelte del 12 ottobre, T-20).
    /// </summary>
    internal static string AsideLine(IReadOnlyList<string> files) =>
        $"Nella cartella dei dati di Missiva ci sono i dati che c'erano prima di un'importazione ({AsideWhat(files)}): le impostazioni di allora, con i contatti di Missiva e la rubrica dei destinatari. " +
        "Servono solo per tornare a com'era Missiva prima. Nelle impostazioni di allora ci sono ancora gli accessi degli account di allora (la password degli account IMAP, l'accesso a Google), " +
        "cifrati per il tuo utente di Windows: finché i file restano, restano anche loro.";

    /// <summary>La domanda prima di eliminarli: quanti sono, che cosa si perde, e che non passano dal Cestino.</summary>
    internal static (string Heading, string Body, string Primary) AsideQuestion(IReadOnlyList<string> files) =>
        ("Eliminare i dati di prima dell'importazione?",
         $"Sono {AsideWhat(files)}, nella cartella dei dati di Missiva: le impostazioni, i contatti di Missiva e la rubrica dei destinatari com'erano prima dell'importazione, " +
         "con gli account di allora e i loro accessi.\n\nEliminandoli non potrai più tornare a com'era Missiva prima dell'importazione: firme, modelli, regole, contatti e account che avevi solo lì sono persi. " +
         "I file non vanno nel Cestino.\n\nQuello che Missiva ha adesso non cambia.",
         "Elimina");

    /// <summary>L'importazione è in attesa: riavviare adesso (la finestra delle Impostazioni si chiude) o al prossimo avvio.</summary>
    internal static void OfferRestart(Window owner)
    {
        var choice = Dialog.Ask(owner, "Riavvia Missiva per finire l'importazione",
            "La copia è pronta. Missiva la mette al posto dei dati di adesso al prossimo avvio: fino ad allora non cambia nulla, e quello che cambi adesso verrà sostituito.",
            "Riavvia ora", null, "Più tardi");
        if (choice != Choice.Primary) return;
        owner.Close();
        App.Current.Dispatcher.BeginInvoke(App.Current.Restart, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    internal static TextBlock Secondary(string text, double top = 0)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return t;
    }

    internal static TextBlock ErrorLine() => Dialog.ErrorText();
}

/// <summary>
/// Esporta: la password scelta, scritta due volte, e la casella delle password IMAP, spenta di base. Poi dove salvare, e
/// il file. La chiave si deriva in sottofondo (qualche decimo di secondo), con la finestra che lo dice.
/// </summary>
public sealed class BackupExportWindow : Window
{
    readonly PasswordBox first = new(), second = new();
    readonly CheckBox passwords = new() { Content = "Includi le password degli account IMAP", Margin = new Thickness(0, 16, 0, 0) };
    readonly TextBlock error = BackupUi.ErrorLine();
    readonly Button export = new() { Content = "Esporta…", MinWidth = 100, IsDefault = true };
    readonly Button cancel = new() { Content = "Annulla", MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
    bool working;

    public BackupExportWindow()
    {
        Title = "Esporta una copia di sicurezza";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = "Esporta una copia di sicurezza", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(BackupUi.Secondary("Scegli una password per il file: la copia è cifrata, e senza la password non si apre. Missiva non la conserva e non può recuperarla: " +
                                              "scrivila dove la ritrovi."));
        panel.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 14, 0, 4) });
        AutomationProperties.SetName(first, "Password");
        panel.Children.Add(first);
        panel.Children.Add(new TextBlock { Text = "Ripeti la password", Margin = new Thickness(0, 10, 0, 4) });
        AutomationProperties.SetName(second, "Ripeti la password");
        panel.Children.Add(second);
        panel.Children.Add(BackupUi.Secondary($"Almeno {BackupUi.MinPassword} caratteri. Una frase di più parole è più sicura di una parola sola.", 6));

        // Solo se c'è un account IMAP: per gli altri non c'è nessuna password da includere.
        if (Store.Settings.Accounts.Any(a => a.Kind == AccountKind.Imap))
        {
            panel.Children.Add(passwords);
            panel.Children.Add(BackupUi.Secondary("Spenta, la scelta più prudente: dove importi la copia riscrivi la password di ogni account IMAP con Aggiorna password. Accesa, chi ha il file " +
                                                  "e indovina o conosce la sua password entra nelle tue caselle IMAP. Gli accessi a Microsoft e a Google non entrano mai nella copia.", 4));
        }
        panel.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        export.SetResourceReference(StyleProperty, "AccentButtonStyle");
        export.Click += async (_, _) => await ExportAsync();
        DockPanel.SetDock(export, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(export);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => first.Focus();
        Closing += (_, e) => e.Cancel = working; // durante la scrittura del file la finestra aspetta
    }

    void Say(string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
    }

    async Task ExportAsync()
    {
        char[] chosen = BackupUi.Chars(first), again = BackupUi.Chars(second);
        try
        {
            if (BackupUi.PasswordProblem(chosen, again) is { } problem)
            {
                Say(problem);
                (chosen.Length < BackupUi.MinPassword ? first : second).Focus();
                return;
            }
            error.Visibility = Visibility.Collapsed;
            if (BackupUi.AskWhereToSave(this) is not { } path) return;

            working = true;
            export.IsEnabled = cancel.IsEnabled = first.IsEnabled = second.IsEnabled = passwords.IsEnabled = false;
            export.Content = "Scrivo la copia…";
            AddressBook.Save(); // la rubrica si salva qualche secondo dopo l'ultima modifica: adesso, perché la copia la legge dal file
            bool withPasswords = passwords.IsChecked == true;
            var data = Backup.Collect(Store.Settings, Store.Dir, withPasswords, out var notes);
            var summary = Backup.Describe(data);
            await Task.Run(() =>
            {
                var bytes = Backup.Write(data, chosen);
                SafeFile.Write(path, bytes);
            });
            Log.Write($"copia di sicurezza esportata: {summary.Accounts.Count} account, {(withPasswords ? $"con {summary.Passwords} password IMAP" : "senza password")}, {new FileInfo(path).Length / 1024} KB" +
                      (notes.Count > 0 ? $"; {notes.Count} cose non leggibili lasciate fuori" : ""));
            working = false;
            var owner = Owner;
            Close();
            if (Dialog.Show(owner, "Copia di sicurezza salvata", BackupUi.ExportedText(path, summary, notes), "Apri la cartella") == Choice.Secondary)
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
                catch (Exception ex) { Log.Error("apertura della cartella della copia di sicurezza", ex); }
        }
        catch (Exception ex)
        {
            Log.Error("copia di sicurezza non esportata", ex);
            working = false;
            export.IsEnabled = cancel.IsEnabled = first.IsEnabled = second.IsEnabled = passwords.IsEnabled = true;
            export.Content = "Esporta…";
            Say($"La copia non è stata scritta: {ex.Message}");
        }
        finally
        {
            Array.Clear(chosen);
            Array.Clear(again);
        }
    }
}

/// <summary>
/// Importa: la password della copia. Sbagliata, lo dice e resta aperta per riprovare; un file rovinato o modificato lo dice
/// e non c'è altro da fare che Annulla. Data è il contenuto aperto, solo in memoria.
/// </summary>
public sealed class BackupImportWindow : Window
{
    readonly byte[] file;
    readonly PasswordBox box = new();
    readonly TextBlock error = BackupUi.ErrorLine();
    readonly Button open = new() { Content = "Apri", MinWidth = 100, IsDefault = true };
    readonly Button cancel = new() { Content = "Annulla", MinWidth = 100, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
    bool closed;

    public BackupData? Data { get; private set; }

    public BackupImportWindow(string name, byte[] file)
    {
        this.file = file;
        Title = "Importa una copia di sicurezza";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock { Text = "Importa una copia di sicurezza", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name });
        panel.Children.Add(BackupUi.Secondary("Scrivi la password scelta quando la copia è stata esportata. Prima di sostituire qualcosa Missiva ti dice che cosa c'è dentro e te lo chiede.", 6));
        panel.Children.Add(new TextBlock { Text = "Password della copia", Margin = new Thickness(0, 14, 0, 4) });
        AutomationProperties.SetName(box, "Password della copia");
        panel.Children.Add(box);
        panel.Children.Add(error);

        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        open.SetResourceReference(StyleProperty, "AccentButtonStyle");
        open.Click += async (_, _) => await OpenAsync();
        DockPanel.SetDock(open, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(open);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => box.Focus();
        Closed += (_, _) => closed = true;
    }

    async Task OpenAsync()
    {
        var password = BackupUi.Chars(box);
        try
        {
            if (password.Length == 0)
            {
                error.Text = "Scrivi la password della copia.";
                error.Visibility = Visibility.Visible;
                return;
            }
            error.Visibility = Visibility.Collapsed;
            open.IsEnabled = box.IsEnabled = false;
            open.Content = "Apro la copia…";
            var data = await Task.Run(() => Backup.Read(file, password));
            // Annulla o la X durante l'attesa della chiave: la finestra non c'è più, e DialogResult su una finestra chiusa
            // è un'eccezione (audit dell'11 ottobre, T-19). La copia aperta si lascia andare.
            if (closed) return;
            Data = data;
            DialogResult = true;
        }
        catch (BackupException ex)
        {
            Log.Write($"copia di sicurezza non aperta: {ex.Problem}");
            if (closed) return;
            error.Text = ex.Message;
            error.Visibility = Visibility.Visible;
            open.Content = "Apri";
            // Con la password sbagliata si riprova; un file rovinato resta rovinato.
            open.IsEnabled = box.IsEnabled = ex.Problem == BackupProblem.WrongPassword;
            if (ex.Problem == BackupProblem.WrongPassword)
            {
                box.Clear();
                box.Focus();
            }
            else cancel.Content = "Chiudi";
        }
        catch (Exception ex)
        {
            // Altro (la memoria che non basta per un file enorme): detto qui, non come errore imprevisto dell'interfaccia.
            Log.Error("copia di sicurezza non aperta", ex);
            error.Text = $"Il file non si è potuto aprire: {ex.Message}";
            error.Visibility = Visibility.Visible;
            open.Content = "Apri";
            cancel.Content = "Chiudi";
        }
        finally
        {
            Array.Clear(password);
        }
    }
}
