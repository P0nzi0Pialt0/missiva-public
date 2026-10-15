#if DEBUG
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Strumenti di sviluppo, solo nella compilazione Debug.
/// Missiva.exe --page messaggio.html pagina.html [--dark] [--images]
///   costruisce la pagina del riquadro di lettura per un'email salvata, da aprire in Edge per le prove.
/// Missiva.exe --testo messaggio.html testo.txt [--pool]
///   scrive il testo di un'email salvata (MailHtml.ReadableText), sul thread principale o su uno del pool.
/// Missiva.exe --prova-bozze risultato.txt [inverso]
///   tutta la suite (ProvaBozze): tutti i gruppi di ProvaBozze.Gruppi.cs, con Graph, IMAP e SMTP finti; con MISSIVA_DATA,
///   dentro la quale ogni giro lavora in una sottocartella nuova. Con inverso i gruppi girano dall'ultimo al primo.
/// Missiva.exe --prova-audit risultato.txt [nome]
///   solo i gruppi degli audit, o il solo gruppo con quel nome (i nomi sono in ProvaBozze.Gruppi.cs e in fondo a ogni
///   risultato); un nome che non c'è è un errore. Anche questa con MISSIVA_DATA.
/// Missiva.exe --scrivi-pec cartella
///   scrive buste PEC e fatture .p7m di prova (firmate con certificati fatti al volo) da aprire con --eml; con
///   --pec-radice cartella\radice-di-prova.cer la radice di prova vale come attendibile nella versione di prova.
/// Missiva.exe --imap-finto porta [senza-cestino] [password=...]
///   tiene aperto il server IMAP finto con qualche messaggio, per un account IMAP nella versione di prova
///   (MISSIVA_IMAP_FINTO=1: senza TLS verso 127.0.0.1), e un server SMTP finto sulla porta dopo; con senza-cestino il
///   server non ha il Cestino, con password=... IMAP e SMTP accettano solo quella.
/// Variabile MISSIVA_DUMP=cartella: salva l'HTML di ogni messaggio aperto e di ogni messaggio scritto (alla chiusura).
/// Variabile MISSIVA_KEYS=file: ogni riga scritta nel file ("ctrl+shift+r", "esc") arriva come tasto (WatchKeys); anche
/// "clic N", "testo ...", "fine sessione", "chiudi finestra", "apri finestra", "menu area di notifica", "deseleziona",
/// "notifica N", "toast argomenti | casella=valore".
/// </summary>
static partial class DevTools
{
    public static bool TryRun(string[] args)
    {
        if (args is ["--prova-bozze", var output, ..])
        {
            ProvaBozze.Run(output, args.Length > 2 ? args[2] : null);
            return true;
        }
        if (args is ["--prova-audit", var auditOutput, ..])
        {
            ProvaBozze.RunAudit(auditOutput, args.Length > 2 ? args[2] : null);
            return true;
        }
        // Un pacchetto di privato\rilascio.ps1 (zip, manifesto, firma) controllato dallo stesso codice di Missiva, su un GitHub finto.
        if (args is ["--verifica-pacchetto", var package, var publicKey, var packageOutput])
        {
            ProvaBozze.VerifyPackage(package, publicKey, packageOutput);
            return true;
        }
        if (args is ["--scrivi-inviti", var folder, ..])
        {
            ProvaBozze.WriteInvites(folder);
            return true;
        }
        if (args is ["--scrivi-pec", var pecFolder, ..])
        {
            ProvaBozze.WritePec(pecFolder);
            return true;
        }
        if (args is ["--imap-finto", var port, ..] && int.TryParse(port, out var number))
        {
            ProvaBozze.ServeImap(number, args.Contains("senza-cestino"), args.FirstOrDefault(a => a.StartsWith("password="))?["password=".Length..], args.Contains("vecchi"));
            return true;
        }
        if (args is ["--page-thread", var threadEmail, var threadOut, ..])
        {
            // Una conversazione finta per le intestazioni (messaggio chiuso con nome lungo, aperto, con logo, proprio, senza indirizzo):
            // --logo file.png (messo nella pagina come data:), --dark, --stretto non cambia nulla qui (la larghezza la dà la finestra).
            string Html = File.ReadAllText(threadEmail);
            MessageSummary Make(string id, string name, string email, string preview, params string[] cats) => new()
            {
                Id = id, FolderId = "prova", Subject = "Conversazione di prova", Date = DateTime.Now.AddMinutes(-int.Parse(id) * 37),
                From = new Address { Name = name, Email = email }, Preview = preview, Categories = cats.ToList(),
            };
            var logoFile = args.SkipWhile(a => a != "--logo").Skip(1).FirstOrDefault();
            var logoBytes = logoFile == null ? null : File.ReadAllBytes(logoFile);
            var to = new List<Address> { new() { Name = "Ada", Email = "ada@example.com" }, new() { Name = "Maria Rossi", Email = "maria.rossi@example.com" } };
            var thread = new[]
            {
                new PageEntry(Make("1", "Giovanni Battista Alessandro Montefiore-Castelnuovo di Sant'Agata", "gb@example.com", "Ti scrivo per la riunione di giovedì: porto io i documenti e il resto del materiale, così non perdiamo tempo."), null, false, false, null, false),
                new PageEntry(Make("2", "Mario Rossi", "mario.rossi@example.com", "", "Clienti"), new MessageBody { Html = Html, To = to, Cc = to.Take(1).ToList() }, true, false, null, false),
                new PageEntry(Make("3", "Amazon.it", "no-reply@amazon.it", "Il tuo ordine è stato spedito, arriverà domani entro le 20."), null, false, false, null, false, null, logoBytes),
                new PageEntry(Make("4", "Ada", "ada.prova@gmail.com", "Grazie, confermo l'orario e passo a prendere i documenti."), null, false, false, null, false),
                new PageEntry(Make("5", "Nessun indirizzo", "", "Un messaggio senza indirizzo: né foto né nome aprono la scheda."), null, false, false, null, false),
            };
            var built = ReadingPage.Build(thread, !args.Contains("--dark"));
            var threadHtml = built.Html;
            if (logoBytes != null) threadHtml = threadHtml.Replace("src=\"/logo/2.png\"", "src=\"data:image/png;base64," + Convert.ToBase64String(logoBytes) + "\"");
            File.WriteAllText(threadOut, threadHtml);
            return true;
        }
        // Il testo di un'email salvata (MailHtml.ReadableText: quello di Rispondi, Inoltra e delle regole), sul thread
        // principale o, con --pool, su un thread del pool: per provare in un processo a parte gli HTML che lo chiuderebbero.
        if (args is ["--testo", var textEmail, var textOut, ..])
        {
            var source = File.ReadAllText(textEmail);
            File.WriteAllText(textOut, args.Contains("--pool") ? Task.Run(() => MailHtml.ReadableText(source)).GetAwaiter().GetResult() : MailHtml.ReadableText(source));
            return true;
        }
        if (args.Length < 3 || args[0] != "--page") return false;
        var summary = new MessageSummary
        {
            Id = "0", FolderId = "prova", Subject = "Prova", Date = DateTime.Now,
            From = new Address { Name = "Mittente di prova", Email = "prova@example.com" },
        };
        var body = new MessageBody { Html = File.ReadAllText(args[1]), To = new() { new Address { Name = "Ada", Email = "ada@example.com" } } };
        SenderNotes? notes = null;
        string? unsubscribe = null;
        // --headers file: intestazioni finte (From, Reply-To, Authentication-Results) per le avvertenze sul mittente, lette
        // come le leggerebbe un account Gmail (authserv-id mx.google.com).
        if (args.SkipWhile(a => a != "--headers").Skip(1).FirstOrDefault() is { } headersFile)
        {
            using var stream = File.OpenRead(headersFile);
            var headers = MimeKit.HeaderList.Load(stream);
            var from = MimeKit.InternetAddressList.TryParse(headers[MimeKit.HeaderId.From] ?? "", out var f) ? f.Mailboxes.FirstOrDefault() : null;
            var replyTo = MimeKit.InternetAddressList.TryParse(headers[MimeKit.HeaderId.ReplyTo] ?? "", out var r) ? r.Mailboxes.ToList() : [];
            var sender = from == null ? summary.From : new Address { Name = from.Name ?? "", Email = from.Address };
            summary = new MessageSummary
            {
                Id = "0", FolderId = "prova", Subject = headers[MimeKit.HeaderId.Subject] ?? "Prova", Date = DateTime.Now, From = sender,
                ReplyTo = Address.ReplyToOf(replyTo.Select(a => new Address { Name = a.Name ?? "", Email = a.Address }), sender),
            };
            body = new MessageBody
            {
                Html = body.Html, To = body.To, AuthenticationResults = SenderCheck.FromHeaders(headers),
                ListUnsubscribe = headers["List-Unsubscribe"], ListUnsubscribePost = headers["List-Unsubscribe-Post"],
            };
            var account = new MailAccount { Kind = AccountKind.Gmail, Email = "ada.prova@gmail.com" };
            notes = SenderCheck.Of(account, summary, body);
            // La riga "Annulla iscrizione" (--annullata: com'è dopo la richiesta riuscita).
            if (Unsubscribe.For(account, summary, body, junk: false) is { } action)
                unsubscribe = args.Contains("--annullata") ? $"Richiesta di annullare l'iscrizione inviata a {action.Where}." : "";
        }
        var page = ReadingPage.Build(new[] { new PageEntry(summary, body, true, false, null, args.Contains("--images"), notes, Unsubscribe: unsubscribe) }, !args.Contains("--dark"));
        File.WriteAllText(args[2], page.Html);
        return true;
    }

    /// <summary>I tasti modificatori di un tasto mandato da WatchKeys, che la tastiera vera non ha premuti.</summary>
    public static ModifierKeys ForcedModifiers { get; private set; }

    /// <summary>
    /// Con MISSIVA_KEYS=file, le righe scritte nel file arrivano come tasti all'elemento con il fuoco della finestra attiva
    /// di Missiva, con PreviewKeyDown e KeyDown come la tastiera: per provare le scorciatoie quando SendInput non arriva
    /// (di notte la sessione non riceve input, e i messaggi WM_KEYDOWN non portano Ctrl e Maiusc). Il file si svuota.
    /// "clic N" è invece un clic sulla riga N dell'elenco (Click), "fine sessione" fa come la fine della sessione di Windows.
    /// </summary>
    public static void WatchKeys()
    {
        if (Environment.GetEnvironmentVariable("MISSIVA_RECORD") == "1")
        {
            UI.WheelScrolling.WheelSeen = WheelRecorder.Seen;
            if (Environment.GetEnvironmentVariable("MISSIVA_VARIANTI") == "1") WheelRecorder.EnableVariants(); // il test alla cieca della molla
        }
        if (Environment.GetEnvironmentVariable("MISSIVA_KEYS") is not { Length: > 0 } path) return;
        // Come i tasti e i clic veri: WPF li consegna dentro Dispatcher.Invoke a priorità Send, e dopo ogni await il lavoro
        // riprende alla priorità di chi l'ha avviato, quindi sopra il disegno. Prima i comandi giravano a priorità Input (e
        // prima ancora a Background): mentre WPF disegna a ogni fotogramma (scorrimento, cursore della barra) le loro riprese
        // aspettavano fino a mezzo secondo, e sembrava un ritardo dei tasti. Misurato con tasti veri l'8 ottobre 2026 (audit
        // del 7 ottobre, B-04; strumenti\ingresso.ps1): freccia giù vera durante lo scorrimento, HTML ripreso 0-6 ms dopo il
        // lavoro e testo pronto in 51-105 ms; la stessa freccia da qui a priorità Input, 41-612 ms e 195-892 ms.
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            string[] lines;
            try
            {
                if (!File.Exists(path)) return;
                lines = File.ReadAllLines(path);
                File.Delete(path);
            }
            catch (IOException) { return; } // lo script lo sta ancora scrivendo
            foreach (var line in lines.Where(l => l.Trim().Length > 0)) timer.Dispatcher.Invoke(() => Press(line.Trim()), DispatcherPriority.Send);
        };
        timer.Start();
    }

    static void Press(string combo)
    {
        if (combo.StartsWith("clic ", StringComparison.OrdinalIgnoreCase) && int.TryParse(combo[5..], out var row))
        {
            Click(row);
            return;
        }
        // "testo parole" scrive nella casella con il fuoco, senza UI Automation (che dopo un SetValue può tenere ferma
        // l'interfaccia per secondi, e falsare i tempi dei suggerimenti della ricerca).
        if (combo.StartsWith("testo ", StringComparison.OrdinalIgnoreCase) && Keyboard.FocusedElement is System.Windows.Controls.TextBox box)
        {
            box.Text = combo[6..];
            box.CaretIndex = box.Text.Length;
            Log.Write($"testo di prova \"{box.Text}\" in {box.Name}");
            return;
        }
        if (combo.Equals("fine sessione", StringComparison.OrdinalIgnoreCase))
        {
            // Come quando Windows chiude la sessione (App.SessionEnding), senza chiuderla: le scritture aperte salvano la bozza.
            App.Current.SaveDraftsBeforeExit("fine della sessione di prova");
            return;
        }
        // Area di notifica: "chiudi finestra" come la X, "apri finestra" come il clic sull'icona, "menu area di notifica" apre
        // il menu dell'icona (da fotografare con foto.ps1 -Popup), "notifica 2" mostra la notifica del messaggio 2 dell'elenco
        // anche a finestra chiusa (come --notify).
        if (Application.Current.MainWindow is MainWindow main)
        {
            if (CursorProbe.Command(main, combo)) return; // sonda NOME on|off, cursore stato
            if (PeopleCommand(main, combo)) return; // persone stato, persone rotella N (DevTools.Persone.cs)
            switch (combo.ToLowerInvariant())
            {
                case "chiudi finestra":
                    Log.Write("prova: chiudo la finestra come con la X");
                    main.Close();
                    return;
                case "esci": // come Esci dal menu dell'area di notifica: Missiva si chiude davvero (per provare che cosa resta salvato)
                    App.Current.ExitAll();
                    return;
                case "apri finestra":
                    main.ShowFromTray();
                    return;
                // La rete nelle prove (audit del 7 ottobre, A-13..A-19), dopo un avvio con --senza-rete: "rete tornata" la rimette
                // e fa come Windows che dice che è cambiata (App.OnNetworkBack, senza i suoi 5 s); "rete tornata in silenzio" la
                // rimette senza dirlo (un firewall che comincia a far passare); "rete assente" la toglie; "stato account" scrive
                // nel registro lo stato sotto ogni account.
                case "rete tornata" or "rete tornata in silenzio" or "rete assente":
                    Network.TestOffline = combo.EndsWith("assente", StringComparison.OrdinalIgnoreCase);
                    Log.Write($"prova: {combo}");
                    if (combo.Equals("rete tornata", StringComparison.OrdinalIgnoreCase)) App.Current.OnNetworkBack();
                    return;
                case "stato account":
                    foreach (var account in main.TestAccounts())
                        Log.Write($"prova: stato di {account.Header}: \"{account.StatusLine}\", cartelle {account.Folders.Count}, cartelle non lette {account.FoldersFailed}, non in linea {Network.KnownOffline(account.Provider.Account)}, IDLE {InboxIdle.Watching(account.Provider.Account.Email)}");
                    return;
                case "menu area di notifica":
                    App.Current.ShowTrayMenu();
                    return;
                case "misure elenco": // le posizioni (in DIP, dal bordo del pannello dell'elenco) di schede, intestazioni e barra di scorrimento
                    MeasureList(main);
                    return;
                case "misure pillole": // pillola e testo di ogni intestazione di gruppo (Oggi, Ieri...) in pixel dalla finestra, con la maiuscola calcolata dai caratteri
                    MeasurePills(main);
                    return;
                case "account chiudi" or "account apri": // chiude o apre tutti gli account nella barra laterale, come la freccia (per le foto della barra)
                    foreach (var account in main.TestAccounts()) account.IsExpanded = combo.EndsWith("apri", StringComparison.OrdinalIgnoreCase);
                    return;
                case "seleziona": // la modalità Seleziona (caselle su tutte le righe), come il pulsante dell'intestazione
                    main.TestSelectMode();
                    return;
                case "scorrimento stato": // lo ScrollViewer dell'elenco ha ancora barra stabile e guardia (dopo un cambio di tema, B-05)?
                    main.TestScrollerState();
                    return;
                case "altezze": // l'altezza di ogni riga dell'elenco (le prime 40), per confrontare i modelli della riga
                    main.TestHeights();
                    return;
                case "vetrina": // righe finte di ogni tipo in cima all'elenco, per confrontare il disegno delle righe (foto.ps1)
                    main.TestShowcase();
                    return;
                case "arrivo di prova": // una riga finta in cima, come un messaggio appena arrivato (solo nell'elenco)
                    main.TestArrival();
                    return;
                case "arrivo misurato": // lo stesso con i disegni dei 600 ms dopo (MISSIVA_INGRESSO=1, riga "disegni dopo arrivo"; B-07)
                    InputProbe.Watch("arrivo", 600);
                    main.TestArrival();
                    return;
                case "costo gruppi": // quanto costa rifare i gruppi dell'elenco, una volta e due come ApplySettings (B-09)
                    main.TestGroupCost();
                    return;
                case "costo webview": // quanto costa a ogni layout il lavoro di WebView2 (HwndHost.LayoutUpdated), con e senza (B-06)
                    MeasureWebViewLayout(main);
                    return;
                case "arrivo di prova vecchio": // lo stesso, rifacendo sempre i gruppi come prima dell'audit (per il confronto)
                    main.TestArrival(regroup: true);
                    return;
                case "stato elenco": // le prime righe, i gruppi e i non letti nel registro (anche a finestra nascosta)
                    main.TestListState();
                    return;
                case "deseleziona": // nessun messaggio scelto: il riquadro torna vuoto (Clear), come dopo un cambio di cartella
                    if (main.FindName("MessageList") is System.Windows.Controls.ListBox messages) messages.UnselectAll();
                    return;
                case "punto elenco": // per il mouse vero (strumenti\ingresso.ps1): un punto dell'elenco in pixel dello schermo, lontano da azioni rapide e caselle
                    if (main.FindName("MessageList") is System.Windows.Controls.ListBox pointList)
                    {
                        var at = pointList.PointToScreen(new Point(pointList.ActualWidth * 0.35, pointList.ActualHeight * 0.45));
                        Log.Write($"punto elenco: {at.X:0} {at.Y:0}");
                    }
                    return;
                case "fotogrammi": // quanti fotogrammi disegna WPF senza fare nulla (CompositionTarget.Rendering per 2 s)
                    MeasureFrames(null, 0);
                    return;
            }
            // "giorno 1": per l'elenco è domani (MessageItem.TestToday), per provare il cambio di giorno con Missiva aperta (audit
            // del 7 ottobre, B-08): l'elenco se ne accorge al controllo di ogni minuto, alla riapertura dall'area di notifica,
            // quando la finestra torna attiva, o subito con "giorno 1 subito"; "giorno 0" torna a oggi.
            if (combo.StartsWith("giorno ", StringComparison.OrdinalIgnoreCase) && int.TryParse(combo.Split(' ')[1], out var days))
            {
                UI.MessageItem.TestToday = days == 0 ? null : DateTime.Today.AddDays(days);
                Log.Write($"prova: per l'elenco oggi è il {UI.MessageItem.Today:d MMMM}");
                if (combo.EndsWith(" subito", StringComparison.OrdinalIgnoreCase)) main.CheckDay();
                return;
            }
            // "calendario stato" e "calendario scorri N" (audit del 7 ottobre, B-10): quante volte la vista del calendario si è
            // ricostruita e dove sono le ore; le ore a N pixel, come con la rotella.
            if (combo.Equals("calendario stato", StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("prova calendario: " + (main.TestCalendar?.TestState() ?? "non aperto"));
                return;
            }
            if (combo.StartsWith("calendario scorri ", StringComparison.OrdinalIgnoreCase) && double.TryParse(combo[18..], out var hoursAt))
            {
                main.TestCalendar?.TestScroll(hoursAt);
                return;
            }
            // "scorri semplice 30": la stessa prova su una finestra a parte con una ListBox banale (solo testo), per sapere se i
            // fotogrammi persi vengono da Missiva o dal resto (scheda video, Windows, altri programmi).
            if (combo.StartsWith("scorri semplice ", StringComparison.OrdinalIgnoreCase) && double.TryParse(combo[16..], out var simpleStep))
            {
                var simpleBox = new System.Windows.Controls.ListBox { ItemsSource = Enumerable.Range(0, 3000).Select(i => $"Riga {i}: un testo abbastanza lungo per riempire lo spazio della riga"), BorderThickness = new Thickness(0) };
                var simpleWindow = new Window { Left = main.Left, Top = main.Top, Width = main.ActualWidth, Height = main.ActualHeight, Content = simpleBox, Title = "prova semplice" };
                simpleWindow.Show();
                simpleWindow.UpdateLayout();
                if (FindChild<System.Windows.Controls.ScrollViewer>(simpleBox) is { } plain) MeasureFrames(plain, simpleStep);
                return;
            }
            // "scorri 8": l'elenco dei messaggi scende di 8 pixel a ogni fotogramma per 3 s, poi il registro dice ogni quanto
            // arriva un fotogramma (medio, peggiore) e quanti superano i 16,7 ms: la fluidità dello scorrimento, senza mani.
            if (combo.StartsWith("scorri ", StringComparison.OrdinalIgnoreCase) && double.TryParse(combo[7..], out var step)
                && main.FindName("MessageList") is System.Windows.Controls.ListBox list
                && FindChild<System.Windows.Controls.ScrollViewer>(list) is { } viewer)
            {
                MeasureFrames(viewer, step);
                return;
            }
            // "rotella 6" (o "rotella 6 fine"): sei tacche di rotellina verso il basso, una ogni 100 ms (fine: da touchpad, 30
            // alla volta ogni 16 ms), come arrivano a WheelScrolling; il registro dice gli spostamenti di ogni fotogramma.
            if (combo.StartsWith("barra ", StringComparison.OrdinalIgnoreCase) && main.FindName("MessageList") is System.Windows.Controls.ListBox barList
                && FindChild<System.Windows.Controls.ScrollViewer>(barList) is { } barViewer
                && barViewer.Template?.FindName("PART_VerticalScrollBar", barViewer) is System.Windows.Controls.Primitives.ScrollBar realBar)
            {
                double before = barViewer.VerticalOffset;
                var kind = combo[6..].Trim().Equals("pagina", StringComparison.OrdinalIgnoreCase) ? System.Windows.Controls.Primitives.ScrollEventType.LargeIncrement : System.Windows.Controls.Primitives.ScrollEventType.ThumbTrack;
                double value = kind == System.Windows.Controls.Primitives.ScrollEventType.ThumbTrack && double.TryParse(combo[6..].Trim(), out var typed) ? typed : 0;
                realBar.RaiseEvent(new System.Windows.Controls.Primitives.ScrollEventArgs(kind, value) { RoutedEvent = System.Windows.Controls.Primitives.ScrollBar.ScrollEvent, Source = realBar });
                barViewer.UpdateLayout();
                Log.Write($"prova barra {combo[6..].Trim()}: posizione {before:0} -> {barViewer.VerticalOffset:0} su {barViewer.ScrollableHeight:0} ({barViewer.VerticalOffset / Math.Max(1, barViewer.ScrollableHeight):0.000} della pista), cursore mostrato {realBar.Value:0.000}");
                return;
            }
            if (combo.Equals("costo righe", StringComparison.OrdinalIgnoreCase) && main.FindName("MessageList") is System.Windows.Controls.ListBox costList
                && FindChild<System.Windows.Controls.ScrollViewer>(costList) is { } costViewer)
            {
                MeasureRowCost(costViewer, "elenco di Missiva");
                var plainBox = new System.Windows.Controls.ListBox { ItemsSource = Enumerable.Range(0, 3000).Select(i => $"Riga {i}: un testo abbastanza lungo per riempire lo spazio della riga"), BorderThickness = new Thickness(0) };
                var plainWindow = new Window { Left = main.Left, Top = main.Top, Width = main.ActualWidth, Height = main.ActualHeight, Content = plainBox, Title = "prova semplice" };
                plainWindow.Show();
                plainWindow.UpdateLayout();
                if (FindChild<System.Windows.Controls.ScrollViewer>(plainBox) is { } plainViewer) MeasureRowCost(plainViewer, "ListBox banale");
                plainWindow.Close();
                return;
            }
            // "modello N": il modello dei movimenti fini di WheelScrolling.TouchModels; "tau N": la molla senza previsione (compatibile con
            // gli strumenti di A); "cima": l'elenco all'inizio. Per rifare i gesti veri con "replay" (touchpad-C.ps1).
            if (combo.StartsWith("modello ", StringComparison.OrdinalIgnoreCase) && int.TryParse(combo[8..], out var modelIndex) && modelIndex >= 0 && modelIndex < UI.WheelScrolling.TouchModels.Length)
            {
                UI.WheelScrolling.Touch = UI.WheelScrolling.TouchModels[modelIndex];
                Log.Write($"prova: modello dei movimenti fini {modelIndex}, {UI.WheelScrolling.Touch.Name}");
                return;
            }
            if (combo.StartsWith("tau ", StringComparison.OrdinalIgnoreCase) && double.TryParse(combo[4..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var springTau))
            {
                UI.WheelScrolling.SpringTau = springTau;
                return;
            }
            if (combo.Equals("cima", StringComparison.OrdinalIgnoreCase))
            {
                main.TestScrollTo(0);
                return;
            }
            if (combo.StartsWith("azioni ", StringComparison.OrdinalIgnoreCase) && int.TryParse(combo[7..], out var actionsRow)) // le azioni rapide sulla riga N, come con le frecce
            {
                main.TestActions(actionsRow);
                return;
            }
            if (combo.StartsWith("vscroll ", StringComparison.OrdinalIgnoreCase) && double.TryParse(combo[8..], out var vscroll)) // l'elenco a N pixel dall'alto (per le foto della vetrina)
            {
                main.TestScrollTo(vscroll);
                return;
            }
            if (combo.StartsWith("aggiungi ", StringComparison.OrdinalIgnoreCase) && combo.Split(' ') is [_, var addCount, var addFromEnd, ..var addRest]
                && int.TryParse(addCount, out var addN) && double.TryParse(addFromEnd, out var addX))
            {
                main.TestAppend(addN, addX, addRest.Length > 0 && int.TryParse(addRest[0], out var addFix) ? addFix : 0);
                return;
            }
            if (combo.StartsWith("replay ", StringComparison.OrdinalIgnoreCase) && main.FindName("MessageList") is System.Windows.Controls.ListBox replayList)
            {
                ReplayWheel(replayList, combo[7..].Trim());
                return;
            }
            if (combo.StartsWith("rotella ", StringComparison.OrdinalIgnoreCase) && int.TryParse(combo.Split(' ')[1], out var notches)
                && main.FindName("MessageList") is System.Windows.Controls.ListBox wheelList
                && FindChild<System.Windows.Controls.ScrollViewer>(wheelList) is { } wheelViewer)
            {
                MeasureWheel(wheelViewer, wheelList, notches, combo.EndsWith(" fine", StringComparison.OrdinalIgnoreCase));
                return;
            }
            if (combo.StartsWith("zoom riquadro ", StringComparison.OrdinalIgnoreCase) && int.TryParse(combo[14..], out var percent)
                && main.FindName("Reader") is UI.ReadingView reader)
            {
                reader.TestZoom(percent / 100.0);
                return;
            }
            // "apri posta 2": la Posta in arrivo dell'account 2; "apri tutti": quella di tutti gli account.
            if (combo.Equals("apri tutti", StringComparison.OrdinalIgnoreCase))
            {
                main.TestOpenInbox(0);
                return;
            }
            if (combo.StartsWith("apri posta ", StringComparison.OrdinalIgnoreCase) && int.TryParse(combo[11..], out var inboxOf))
            {
                main.TestOpenInbox(inboxOf);
                return;
            }
            if (combo.StartsWith("notifica ", StringComparison.OrdinalIgnoreCase) && int.TryParse(combo[9..], out var shown))
            {
                main.TestNotify(shown);
                return;
            }
            // Audit, blocco 4: "scegli 2 3 4" seleziona quelle righe insieme (come Ctrl+clic), "sole" e "immagini" premono il
            // sole e "Mostra immagini" del messaggio aperto, "anteprima" apre l'anteprima del primo allegato che ne ha una.
            if (combo.StartsWith("scegli ", StringComparison.OrdinalIgnoreCase))
            {
                main.TestChoose(combo[7..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => int.TryParse(t, out var n) ? n : -1).ToList());
                return;
            }
            if (combo.ToLowerInvariant() is "sole" or "immagini" && main.FindName("Reader") is UI.ReadingView page)
            {
                page.TestAction(combo.Equals("sole", StringComparison.OrdinalIgnoreCase) ? "sun" : "images");
                return;
            }
            if (combo.Equals("anteprima", StringComparison.OrdinalIgnoreCase))
            {
                main.TestPreview();
                return;
            }
            // "toast account=...;message=...[;action=reply] [| reply=testo] [| snooze=...]": il gestore del clic o di un
            // pulsante della notifica, come --toast, con i valori delle caselle dopo "|".
            if (combo.StartsWith("toast ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = combo[6..].Split('|', StringSplitOptions.TrimEntries);
                var input = parts.Skip(1).Select(p => p.Split('=', 2)).Where(kv => kv.Length == 2).ToDictionary(kv => kv[0], kv => (object)kv[1]);
                App.Current.OnToast(parts[0], input);
                return;
            }
        }
        var mods = ModifierKeys.None;
        Key key = Key.None;
        foreach (var part in combo.ToLowerInvariant().Split('+'))
        {
            switch (part)
            {
                case "ctrl": mods |= ModifierKeys.Control; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "esc": key = Key.Escape; break;
                case "invio" or "enter": key = Key.Enter; break;
                case "ins": key = Key.Insert; break;
                case ".": key = Key.OemPeriod; break;
                case ",": key = Key.OemComma; break;
                default: if (!Enum.TryParse(part, true, out key) || !Enum.IsDefined(key)) key = Key.None; break; // un numero non è un tasto
            }
        }
        var window = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        if (key == Key.None || window == null || PresentationSource.FromVisual(window) is not { } source) return;
        var target = Keyboard.FocusedElement is DependencyObject d && Window.GetWindow(d) == window ? (IInputElement)d : window;
        Log.Write($"tasto di prova {combo} a {target.GetType().Name} in \"{window.Title}\"");
        ForcedModifiers = mods;
        try
        {
            var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(down);
            if (down.Handled) return;
            var up = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent };
            target.RaiseEvent(up);
            // Le scorciatoie dei comandi di WPF (Ctrl+V nell'editor) guardano i modificatori della tastiera vera, che qui non
            // sono premuti: il comando si esegue a mano, come farebbe WPF.
            var command = new[] { ApplicationCommands.Paste, ApplicationCommands.Copy, ApplicationCommands.Cut, ApplicationCommands.SelectAll, ApplicationCommands.Undo }
                .FirstOrDefault(c => c.InputGestures.OfType<KeyGesture>().Any(g => g.Key == key && g.Modifiers == mods));
            if (!up.Handled && command != null && command.CanExecute(null, target))
            {
                command.Execute(null, target);
                Log.Write($"tasto di prova {combo}: comando {command.Name}");
            }
        }
        finally
        {
            ForcedModifiers = ModifierKeys.None;
        }
    }

    /// <summary>
    /// "costo webview" (audit del 7 ottobre, B-06): WebView2 è un HwndHost, e a ogni layout della finestra che cambia qualcosa
    /// il suo gestore di LayoutUpdated ricalcola la posizione della finestra figlia. Qui 400 passi di scorrimento dell'elenco
    /// con UpdateLayout (come un fotogramma di WheelScrolling), a blocchi alternati con il gestore com'è e con il gestore
    /// tolto (campo privato di HwndHost, solo per questa misura, poi rimesso): la differenza è il suo costo per fotogramma.
    /// </summary>
    static void MeasureWebViewLayout(MainWindow main)
    {
        if (main.FindName("MessageList") is not System.Windows.Controls.ListBox list || FindChild<System.Windows.Controls.ScrollViewer>(list) is not { } viewer) return;
        var web = FindChild<Microsoft.Web.WebView2.Wpf.WebView2>(main);
        var field = typeof(System.Windows.Interop.HwndHost).GetField("_handlerLayoutUpdated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (web == null || field?.GetValue(web) is not EventHandler handler)
        {
            Log.Write($"costo webview: {(web == null ? "nessun WebView2 nella finestra" : "il gestore di LayoutUpdated di HwndHost non si trova")}");
            return;
        }
        double Pass()
        {
            viewer.ScrollToVerticalOffset(0);
            viewer.UpdateLayout();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + (i < 50 ? 12 : -12));
                viewer.UpdateLayout();
            }
            return clock.Elapsed.TotalMilliseconds / 100;
        }
        Pass();
        var with = new List<double>();
        var without = new List<double>();
        for (int round = 0; round < 6; round++)
        {
            with.Add(Pass());
            web.LayoutUpdated -= handler;
            try { without.Add(Pass()); }
            finally { web.LayoutUpdated += handler; }
        }
        Log.Write($"costo webview (visibile {web.IsVisible}): un passo di scorrimento con il layout, 6 blocchi alternati da 100 passi: con il gestore di WebView2 {string.Join(" ", with.Select(v => v.ToString("0.000")))} ms " +
                  $"(mediana {with.OrderBy(v => v).ElementAt(3):0.000}), senza {string.Join(" ", without.Select(v => v.ToString("0.000")))} ms (mediana {without.OrderBy(v => v).ElementAt(3):0.000})");
    }

    static void MeasureList(Window main)
    {
        if (main.FindName("ListCard") is not FrameworkElement card || main.FindName("MessageList") is not System.Windows.Controls.ListBox list) return;
        string Box(FrameworkElement e)
        {
            var at = e.TransformToAncestor(card).Transform(new Point(0, 0));
            return $"x {at.X:0.#} a {at.X + e.ActualWidth:0.#} (larga {e.ActualWidth:0.#})";
        }
        var text = new System.Text.StringBuilder($"misure elenco: pannello larga {card.ActualWidth:0.#}; lista {Box(list)}");
        if (FindChild<System.Windows.Controls.ScrollViewer>(list) is { } viewer)
            text.Append($"; ScrollViewer {Box(viewer)}, area visibile {viewer.ViewportWidth:0.#}, contenuto {viewer.ExtentWidth:0.#}, barra {viewer.ComputedVerticalScrollBarVisibility}");
        if (FindChild<System.Windows.Controls.Primitives.ScrollBar>(list) is { Orientation: System.Windows.Controls.Orientation.Vertical } bar)
            text.Append($"; barra verticale {Box(bar)}");
        var item = FindChild<System.Windows.Controls.ListBoxItem>(list);
        if (item != null) text.Append($"; prima scheda {Box(item)}, margini {item.Margin}");
        if (FindChild<System.Windows.Controls.GroupItem>(list) is { } group) text.Append($"; gruppo {Box(group)}");
        Log.Write(text.ToString());
    }

    /// <summary>
    /// Le intestazioni dei gruppi dell'elenco (la pillola e il testo, in pixel dalla finestra) e dove cade la maiuscola del testo
    /// secondo i caratteri (alto della maiuscola dalla linea di base) rispetto al centro della pillola: la differenza in pixel,
    /// positiva se la maiuscola e' piu' in basso. La misura vera, dai pixel, e' in loop\strumenti\pillola-I.ps1.
    /// </summary>
    static void MeasurePills(Window main)
    {
        if (main.FindName("MessageList") is not System.Windows.Controls.ListBox list) return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(main);
        var text = new System.Text.StringBuilder($"misure pillole (scala {dpi.DpiScaleX:0.##}):");
        void Walk(DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is System.Windows.Controls.TextBlock { Parent: System.Windows.Controls.Grid { Name: "Header" } grid } tb &&
                    grid.Children.OfType<System.Windows.Controls.Border>().FirstOrDefault() is { } pill && tb.IsVisible)
                {
                    double s = dpi.DpiScaleX;
                    var pillAt = pill.TransformToAncestor(main).Transform(new Point(0, 0));
                    var textAt = tb.TransformToAncestor(main).Transform(new Point(0, 0));
                    var face = new System.Windows.Media.Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
                    var formatted = new System.Windows.Media.FormattedText("O", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, tb.FontSize,
                        System.Windows.Media.Brushes.Black, s);
                    double caps = face.TryGetGlyphTypeface(out var glyphs) ? glyphs.CapsHeight * tb.FontSize : double.NaN;
                    double baseline = textAt.Y + formatted.Baseline;
                    double capCenter = baseline - caps / 2;
                    double pillCenter = pillAt.Y + pill.ActualHeight / 2;
                    text.Append($" [{tb.Text}: pillola x {pillAt.X * s:0.##} y {pillAt.Y * s:0.##} larga {pill.ActualWidth * s:0.##} alta {pill.ActualHeight * s:0.##};" +
                                $" testo y {textAt.Y * s:0.##} alto {tb.ActualHeight * s:0.##}, linea di base {baseline * s:0.##}, maiuscola {caps * s:0.##}; maiuscola meno pillola {(capCenter - pillCenter) * s:0.##}]");
                }
                Walk(child);
            }
        }
        Walk(list);
        Log.Write(text.ToString());
    }

    static T? FindChild<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>Righe dell'elenco preparate (MessageItem.Initials, letto una volta per riga) e gruppi per data caricati, per i fotogrammi lenti.</summary>
    public static int RowsPrepared, GroupsLoaded;
    static bool countingGroups;

    /// <summary>
    /// Un fotogramma con più di 8 ms di lavoro, per capire di chi è il picco: quando (ms dall'inizio), quanto, le raccolte
    /// di .NET (generazioni 0, 1 e 2) e le righe e i gruppi per data preparati in quel fotogramma.
    /// </summary>
    sealed class SlowFrames
    {
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        readonly List<string> slow = new();
        int gc0, gc1, gc2, rows, groups;

        public SlowFrames()
        {
            if (!countingGroups)
            {
                countingGroups = true;
                EventManager.RegisterClassHandler(typeof(System.Windows.Controls.GroupItem), FrameworkElement.LoadedEvent, new RoutedEventHandler((_, _) => GroupsLoaded++));
            }
            Mark();
        }

        /// <summary>Da qui comincia il lavoro del fotogramma.</summary>
        public void Mark() => (gc0, gc1, gc2, rows, groups) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), RowsPrepared, GroupsLoaded);

        /// <summary>Il lavoro del fotogramma è finito: se è durato più di 8 ms si annota.</summary>
        public void Done(double ms)
        {
            if (ms > 8)
                slow.Add($"{clock.ElapsedMilliseconds} ms: {ms:0.0} ms, raccolte {GC.CollectionCount(0) - gc0}/{GC.CollectionCount(1) - gc1}/{GC.CollectionCount(2) - gc2}, " +
                         $"righe {RowsPrepared - rows}, gruppi {GroupsLoaded - groups}");
            Mark();
        }

        public override string ToString() => slow.Count == 0 ? "nessun fotogramma oltre 8 ms di lavoro" : $"oltre 8 ms di lavoro: {string.Join("; ", slow)}";
    }

    /// <summary>
    /// Chi alloca mentre si scorre: gli eventi GCAllocationTick del runtime (uno ogni 100 KB circa, con il tipo dell'oggetto
    /// che ha passato la soglia), sommati per tipo mentre On è vero. Per capire da dove vengono le raccolte dello scorrimento.
    /// </summary>
    sealed class AllocationSampler : System.Diagnostics.Tracing.EventListener
    {
        public readonly Dictionary<string, long> Types = new();
        public volatile bool On;

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (!On || e.EventName?.StartsWith("GCAllocationTick") != true || e.PayloadNames == null || e.Payload == null) return;
            int type = e.PayloadNames.IndexOf("TypeName"), amount = e.PayloadNames.IndexOf("AllocationAmount64");
            if (type < 0 || amount < 0) return;
            var name = e.Payload[type] as string ?? "?";
            lock (Types) Types[name] = Types.GetValueOrDefault(name) + Convert.ToInt64(e.Payload[amount]);
        }

        public string Top(int count)
        {
            lock (Types)
            {
                var total = Types.Values.Sum();
                var top = string.Join(", ", Types.OrderByDescending(t => t.Value).Take(count).Select(t => $"{t.Key} {t.Value * 100 / Math.Max(1, total)}%"));
                Types.Clear();
                return top;
            }
        }
    }

    /// <summary>
    /// Dove va il tempo di ogni fotogramma di WPF (prove, "scorri N"): il tempo dentro l'operazione Render del Dispatcher
    /// prima e dopo l'evento CompositionTarget.Rendering, le altre operazioni del Dispatcher fra un fotogramma e l'altro e
    /// il resto (attesa dello schermo). Per i fotogrammi oltre 12 ms dice da cosa sono fatti.
    /// </summary>
    internal sealed class FrameTrace
    {
        readonly Dictionary<DispatcherOperation, (long At, string Name)> started = new();
        static readonly System.Reflection.FieldInfo? MethodField = typeof(DispatcherOperation).GetField("_method", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        readonly List<string> between = new();
        readonly List<(int Index, double Gap, double PostBefore, double Pre, double Mine, string Ops, int Rows)> hitches = new();
        long renderStarted, tickStarted, tickEnded, lastTick;
        double postBefore, pre, mine;
        int index, rowsBefore;
        readonly DispatcherHooks hooks;

        public FrameTrace(Dispatcher dispatcher)
        {
            hooks = dispatcher.Hooks;
            hooks.OperationStarted += OnStarted;
            hooks.OperationCompleted += OnCompleted;
        }

        static double Ms(long from, long to) => (to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        void OnStarted(object? sender, DispatcherHookEventArgs e)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            var method = MethodField?.GetValue(e.Operation) as Delegate;
            started[e.Operation] = (now, method == null ? "?" : $"{method.Method.DeclaringType?.DeclaringType?.FullName ?? method.Method.DeclaringType?.FullName}.{method.Method.Name}");
            if (e.Operation.Priority == DispatcherPriority.Render) renderStarted = now;
        }

        void OnCompleted(object? sender, DispatcherHookEventArgs e)
        {
            if (!started.Remove(e.Operation, out var begun)) return;
            long at = begun.At, now = System.Diagnostics.Stopwatch.GetTimestamp();
            double ms = Ms(at, now);
            if (e.Operation.Priority == DispatcherPriority.Render && tickEnded > at)
            {
                postBefore = Ms(tickEnded, now); // il disegno dopo l'evento: passa a "prima" del fotogramma dopo
                tickEnded = 0;
            }
            else if (ms > 1.5) between.Add($"{e.Operation.Priority} {begun.Name} {ms:0.0}");
        }

        /// <summary>Comincia l'evento Rendering (dentro l'operazione Render).</summary>
        public void TickStart()
        {
            tickStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            pre = renderStarted != 0 ? Ms(renderStarted, tickStarted) : 0;
        }

        /// <summary>Finito il lavoro dell'evento Rendering.</summary>
        public void TickEnd()
        {
            tickEnded = System.Diagnostics.Stopwatch.GetTimestamp();
            mine = Ms(tickStarted, tickEnded);
            if (lastTick != 0)
            {
                double gap = Ms(lastTick, tickStarted);
                if (gap > 12)
                    hitches.Add((index, gap, postBefore, pre, mine, between.Count == 0 ? "-" : string.Join(", ", between), RowsPrepared - rowsBefore));
            }
            between.Clear();
            postBefore = 0;
            rowsBefore = RowsPrepared;
            lastTick = tickStarted;
            index++;
        }

        public void Stop()
        {
            hooks.OperationStarted -= OnStarted;
            hooks.OperationCompleted -= OnCompleted;
        }

        public override string ToString() =>
            hitches.Count == 0 ? "nessun fotogramma oltre 12 ms" :
            "oltre 12 ms (gap = disegno del fotogramma prima + altre operazioni + attesa + inizio del disegno; lavoro mio): " +
            string.Join(" | ", hitches.Select(h =>
                $"#{h.Index} {h.Gap:0.0} ms = dopo {h.PostBefore:0.0} + altre [{h.Ops}] + inizio {h.Pre:0.0}, mio {h.Mine:0.0}, righe {h.Rows}"));
    }

    /// <summary>
    /// Gli scatti del cursore della barra di scorrimento: il cursore sta a offset/altezza della pista, e con l'altezza stimata da
    /// WPF (righe di altezze diverse) che cambia a ogni riga realizzata salta anche se l'offset va liscio. Per ogni fotogramma lo
    /// scarto fra il passo vero del cursore (in px di pista, la pista alta quanto la vista) e quello che avrebbe fatto con l'altezza
    /// di prima: quanti fotogrammi oltre 2 px, il massimo, e quanto varia l'altezza in tutto.
    /// </summary>
    static string ThumbSummary(List<(double Offset, double Height, double Viewport, double Shown)> samples)
    {
        if (samples.Count < 2) return "nessun campione";
        var anomalies = new List<double>();
        for (int i = 1; i < samples.Count; i++)
        {
            var (o0, h0, _, _) = samples[i - 1];
            var (o1, h1, view, _) = samples[i];
            if (h0 <= 0 || h1 <= 0) continue;
            double actual = view * (o1 / h1 - o0 / h0), expected = view * (o1 - o0) / h0;
            anomalies.Add(Math.Abs(actual - expected));
        }
        // Il cursore mostrato: quanto salta oltre a quello che farebbe con l'altezza vera (quella dell'ultimo campione).
        double finalHeight = samples[^1].Height;
        var shown = new List<double>();
        for (int i = 1; i < samples.Count; i++)
            if (!double.IsNaN(samples[i].Shown) && !double.IsNaN(samples[i - 1].Shown) && finalHeight > 0)
                shown.Add(Math.Max(0, Math.Abs(samples[i].Viewport * (samples[i].Shown - samples[i - 1].Shown)) - Math.Abs(samples[i].Viewport * (samples[i].Offset - samples[i - 1].Offset) / finalHeight)));
        string shownText = shown.Count == 0 ? "cursore mostrato non letto" : $"cursore mostrato: {shown.Count(a => a > 2)} fotogrammi su {shown.Count} oltre 2 px in più del vero, massimo {shown.Max():0.0} px";
        return $"{shownText}; grezzo: {anomalies.Count(a => a > 2)} fotogrammi su {anomalies.Count} con il cursore fuori di oltre 2 px (oltre 6: {anomalies.Count(a => a > 6)}), scarto massimo {anomalies.Max():0.0} px, " +
               $"altezza scorribile da {samples.Min(s => s.Height):0} a {samples.Max(s => s.Height):0} ({samples.Zip(samples.Skip(1), (a, b) => Math.Abs(a.Height - b.Height)).Count(d => d > 1)} cambi oltre 1 px)";
    }

    static AllocationSampler? sampler;

    /// <summary>I fotogrammi di WPF per qualche secondo (RenderingTime di CompositionTarget.Rendering), scorrendo di step pixel ognuno.</summary>
    static void MeasureFrames(System.Windows.Controls.ScrollViewer? viewer, double step)
    {
        var times = new List<TimeSpan>();
        var limit = TimeSpan.FromSeconds(viewer == null ? 2 : 3);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var work = new List<double>();
        var slow = new SlowFrames();
        var trace = viewer != null ? new FrameTrace(viewer.Dispatcher) : null;
        if (viewer != null) CursorProbe.Begin(viewer.Dispatcher);
        var thumb = new List<(double Offset, double Height, double Viewport, double Shown)>();
        var shownBar = viewer?.Template?.FindName("PART_VerticalScrollBar", viewer) as System.Windows.Controls.Primitives.ScrollBar;
        long allocated = GC.GetAllocatedBytesForCurrentThread(), collected = GC.CollectionCount(0);
        if (viewer != null)
        {
            sampler ??= new AllocationSampler();
            sampler.Top(0);
            sampler.On = true;
        }
        double direction = 1; // su e giù dentro l'elenco caricato: in fondo non si caricano pagine, in cima non si salta
        EventHandler? tick = null;
        tick = (_, e) =>
        {
            var at = ((System.Windows.Media.RenderingEventArgs)e).RenderingTime;
            if (times.Count > 0 && times[^1] == at) return; // lo stesso fotogramma annunciato due volte
            times.Add(at);
            trace?.TickStart();
            if (viewer != null)
            {
                slow.Mark();
                var busy = System.Diagnostics.Stopwatch.StartNew();
                if (viewer.VerticalOffset + step * direction >= viewer.ScrollableHeight - 400 || viewer.VerticalOffset + step * direction <= 0) direction = -direction;
                ScrollHitGuard.Moving(viewer, step); // come un gesto: il mouse fermo non partecipa finché la lista scorre
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + step * direction);
                viewer.UpdateLayout();
                thumb.Add((viewer.VerticalOffset, viewer.ScrollableHeight, viewer.ViewportHeight, shownBar == null || shownBar.Maximum <= 0 ? double.NaN : shownBar.Value / shownBar.Maximum));
                work.Add(busy.Elapsed.TotalMilliseconds);
                slow.Done(work[^1]);
                trace?.TickEnd();
            }
            if (clock.Elapsed < limit) return;
            System.Windows.Media.CompositionTarget.Rendering -= tick;
            trace?.Stop();
            if (sampler != null) sampler.On = false;
            var gaps = times.Zip(times.Skip(1), (a, b) => (b - a).TotalMilliseconds).ToList();
            Log.Write($"fotogrammi{(viewer != null ? $" scorrendo di {step} px" : "")}: {gaps.Count + 1} in {clock.Elapsed.TotalSeconds:0.0} s, " +
                      $"ogni {gaps.Average():0.0} ms (peggiore {gaps.Max():0.0}), oltre 16,7 ms: {gaps.Count(g => g > 16.7)}, oltre 9 ms: {gaps.Count(g => g > 9)}" +
                      (work.Count > 0 ? $"; lavoro per fotogramma {work.Average():0.0} ms (peggiore {work.Max():0.0}); " +
                                        $"allocati {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1048576.0:0.0} MB, raccolte {GC.CollectionCount(0) - collected}; {slow}; " +
                                        $"disegno: tier {System.Windows.Media.RenderCapability.Tier >> 16}, {System.Windows.Media.RenderOptions.ProcessRenderMode}; barra: {ThumbSummary(thumb)}; traccia: {trace}; " +
                                        $"allocazioni per tipo: {sampler?.Top(12)}" : ""));
            if (viewer != null) Log.Write(CursorProbe.End($"scorri {step}"));
        };
        System.Windows.Media.CompositionTarget.Rendering += tick;
    }

    /// <summary>
    /// "replay rotella-vera-HHMMSS.csv": rifà gli eventi della rotella registrati da WheelRecorder (MISSIVA_RECORD=1) agli stessi
    /// tempi, dentro i fotogrammi, per rifare a tavolino uno scorrimento vero. Con MISSIVA_RECORD=1 il registratore ne scrive un altro.
    /// </summary>
    static void ReplayWheel(System.Windows.Controls.ListBox list, string name)
    {
        var events = File.ReadAllLines(Path.Combine(Store.Dir, name)).Skip(1).Select(l => l.Split(';')).Where(c => c[0] == "E")
                         .Select(c => (At: double.Parse(c[1]), Delta: int.Parse(c[2]))).ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int sent = 0;
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            while (sent < events.Count && clock.Elapsed.TotalMilliseconds >= events[sent].At)
            {
                FindChild<System.Windows.Controls.ListBoxItem>(list)?.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, events[sent].Delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                sent++;
            }
            if (sent >= events.Count) System.Windows.Media.CompositionTarget.Rendering -= tick;
        };
        System.Windows.Media.CompositionTarget.Rendering += tick;
    }

    /// <summary>
    /// "costo righe": quanto costa realizzare una riga nuova (ScrollToVerticalOffset e UpdateLayout, senza fotogrammi in mezzo),
    /// sull'elenco di Missiva e su una ListBox banale con solo testo, tre passate da 20 px: dice se il costo è dell'elenco
    /// di Missiva o di WPF. I passi che costano più di 1 ms sono quelli che hanno realizzato una riga.
    /// </summary>
    static void MeasureRowCost(System.Windows.Controls.ScrollViewer viewer, string name)
    {
        var last = new List<double>();
        for (int pass = 0; pass < 9; pass++)
        {
            viewer.ScrollToVerticalOffset(0);
            viewer.UpdateLayout();
            var watch = new System.Diagnostics.Stopwatch();
            var costs = new List<double>();
            while (viewer.VerticalOffset + 20 < viewer.ScrollableHeight - 300 && costs.Count < 400)
            {
                watch.Restart();
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + 20);
                viewer.UpdateLayout();
                costs.Add(watch.Elapsed.TotalMilliseconds);
            }
            if (pass >= 4) last.AddRange(costs); // le prime passate scaldano (JIT, modelli, cache)
        }
        var rows = last.Where(c => c > 1).ToList();
        var sorted = last.OrderBy(c => c).ToList();
        Log.Write($"costo righe, {name}: {last.Count} passi da 20 px, mediana {sorted[sorted.Count / 2]:0.00} ms, massimo {sorted[^1]:0.0} ms; " +
                  $"con una riga nuova (oltre 1 ms): {rows.Count} passi, media {(rows.Count == 0 ? 0 : rows.Average()):0.00} ms, mediana {(rows.Count == 0 ? 0 : rows.OrderBy(c => c).ElementAt(rows.Count / 2)):0.00} ms");
    }

    static void MeasureWheel(System.Windows.Controls.ScrollViewer viewer, System.Windows.Controls.ListBox list, int count, bool fine)
    {
        var offsets = new List<(TimeSpan At, double Offset)>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        CursorProbe.Begin(viewer.Dispatcher);
        int sent = 0, events = fine ? count * 4 : count;
        double every = fine ? 16 : 100;
        // Il lavoro di WheelScrolling.OnFrame: i primi cinque fotogrammi del gesto, e quelli oltre 8 ms.
        var first = new List<double>();
        var slow = new SlowFrames();
        var collections = new List<int>(); // raccolte di .NET (generazione 0) a ogni fotogramma
        var sentAt = new List<int>(); // il fotogramma di ogni evento
        UI.WheelScrolling.FrameWork = ms =>
        {
            CursorProbe.AddWork(ms);
            if (first.Count < 5) first.Add(ms);
            slow.Done(ms);
        };
        UI.WheelScrolling.FrameStarting = slow.Mark;
        EventHandler? tick = null;
        tick = (_, e) =>
        {
            var at = ((System.Windows.Media.RenderingEventArgs)e).RenderingTime;
            if (offsets.Count > 0 && offsets[^1].At == at) return;
            offsets.Add((at, viewer.VerticalOffset));
            collections.Add(GC.CollectionCount(0));
            // Gli eventi partono da qui, a tempo: un DispatcherTimer (WM_TIMER) non scatta mentre si disegna di continuo.
            // Su una riga che c'è adesso: quella di prima, uscita di vista, l'elenco l'ha già riciclata.
            UI.WheelScrolling.FrameTime = at; // gli eventi arrivano dentro questo fotogramma
            while (sent < events && clock.Elapsed.TotalMilliseconds >= sent * every)
            {
                FindChild<System.Windows.Controls.ListBoxItem>(list)?.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, fine ? -30 : -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                sent++;
                sentAt.Add(offsets.Count - 1);
            }
            UI.WheelScrolling.FrameTime = null;
            if (clock.Elapsed < TimeSpan.FromMilliseconds(events * every + 600)) return;
            System.Windows.Media.CompositionTarget.Rendering -= tick;
            UI.WheelScrolling.FrameWork = null;
            UI.WheelScrolling.FrameStarting = null;
            var moving = offsets.Zip(offsets.Skip(1), (a, b) => (Gap: (b.At - a.At).TotalMilliseconds, Step: b.Offset - a.Offset))
                                .SkipWhile(s => s.Step == 0).Reverse().SkipWhile(s => s.Step == 0).Reverse().ToList();
            // Gli intervalli oltre 12 ms (un fotogramma perso) e quanti con una raccolta di .NET in mezzo.
            var steps = Enumerable.Range(1, offsets.Count - 1).Where(k => offsets[k].Offset != offsets[k - 1].Offset).ToList();
            var gaps = steps.Count == 0 ? new List<int>() : Enumerable.Range(steps[0], steps[^1] - steps[0] + 1)
                .Where(k => (offsets[k].At - offsets[k - 1].At).TotalMilliseconds > 12).ToList();
            Log.Write($"rotella {(fine ? "fine" : "a tacche")}: {events} eventi, {offsets[^1].Offset - offsets[0].Offset:0} px in {moving.Count} fotogrammi " +
                      $"(ogni {(moving.Count > 0 ? moving.Average(s => s.Gap) : 0):0.0} ms), fermi a metà: {moving.Count(s => s.Step == 0)}, " +
                      $"passi: {string.Join(" ", moving.Take(40).Select(s => s.Step.ToString("0")))}; " +
                      $"intervalli: {string.Join(" ", moving.Take(20).Select(s => s.Gap.ToString("0.0")))}; " +
                      $"lavoro dei primi fotogrammi: {string.Join(" ", first.Select(ms => ms.ToString("0.0")))} ms; {slow}; " +
                      $"intervalli oltre 12 ms: {gaps.Count}, con una raccolta: {gaps.Count(k => collections[k] != collections[k - 1])}; " +
                      Regular(offsets, sentAt, fine));
            Log.Write(CursorProbe.End($"rotella {count}{(fine ? " fine" : "")}"));
        };
        System.Windows.Media.CompositionTarget.Rendering += tick;
    }

    /// <summary>
    /// I passi della rotellina senza gli intoppi del disegno: solo quelli fra due fotogrammi regolari (a meno di 10 ms), dove
    /// un passo più lungo non è un fotogramma perso. A tacche il passo più piccolo fra la seconda e la quinta tacca (la velocità
    /// fra una tacca e l'altra); fini il più piccolo e il più grande fra il terzo e l'ultimo evento (passi uguali).
    /// </summary>
    static string Regular(List<(TimeSpan At, double Offset)> offsets, List<int> sentAt, bool fine)
    {
        // Il passo del fotogramma k è quello fatto nel fotogramma k-1, dopo gli eventi mandati lì.
        int from = fine ? sentAt.ElementAtOrDefault(2) : sentAt.ElementAtOrDefault(1), to = fine ? sentAt.LastOrDefault() : sentAt.ElementAtOrDefault(4);
        var steps = Enumerable.Range(from + 2, Math.Max(0, to - from))
            .Where(k => k < offsets.Count && (offsets[k].At - offsets[k - 1].At).TotalMilliseconds < 10 && (offsets[k - 1].At - offsets[k - 2].At).TotalMilliseconds < 10)
            .Select(k => offsets[k].Offset - offsets[k - 1].Offset).ToList();
        return steps.Count == 0 ? "passi regolari: nessuno" :
            $"passi a fotogrammi regolari {(fine ? "dal terzo all'ultimo evento" : "fra la seconda e la quinta tacca")}: {steps.Count}, da {steps.Min():0.0} a {steps.Max():0.0} px";
    }

    /// <summary>
    /// "clic 2" in MISSIVA_KEYS: un clic sinistro sul primo testo della riga 2 dell'elenco (la data, lontana da caselle e
    /// azioni rapide), come la tastiera finta: PreviewMouseDown, MouseDown se nessuno l'ha preso, poi lo stesso per il
    /// rilascio. Proprio quelli, perché gli eventi del tasto sinistro (MouseLeftButtonDown) sono diretti: WPF li rilancia
    /// elemento per elemento da MouseDown. Di notte i clic veri (SendInput, PostMessage) non arrivano; così si provano la
    /// selezione e il clic che aspetta il trascinamento (MainWindow.OnMessageDragStart).
    /// </summary>
    static void Click(int index)
    {
        if (Application.Current.MainWindow?.FindName("MessageList") is not System.Windows.Controls.ListBox list || index < 0 || index >= list.Items.Count) return;
        if (list.ItemContainerGenerator.ContainerFromItem(list.Items[index]) is not DependencyObject row) return;
        static System.Windows.Controls.TextBlock? FirstText(DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is System.Windows.Controls.TextBlock { Text.Length: > 2 } text && text.IsVisible) return text;
                if (FirstText(child) is { } found) return found;
            }
            return null;
        }
        // Con la riga disegnata (MessageRowElement, da d475e5b) non ci sono TextBlock: il clic va all'elemento della riga.
        static UI.MessageRowElement? FindRowElement(DependencyObject d)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (child is UI.MessageRowElement element) return element;
                if (FindRowElement(child) is { } found) return found;
            }
            return null;
        }
        UIElement? target = FirstText(row) ?? (UIElement?)FindRowElement(row);
        if (target == null) return;        int before = list.SelectedItems.Count;
        bool taken = false; // la pressione presa prima della riga (OnMessageDragStart)
        foreach (var (preview, bubble) in new[] { (Mouse.PreviewMouseDownEvent, Mouse.MouseDownEvent), (Mouse.PreviewMouseUpEvent, Mouse.MouseUpEvent) })
        {
            var first = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = preview };
            target.RaiseEvent(first);
            if (preview == Mouse.PreviewMouseDownEvent) taken = first.Handled;
            if (!first.Handled) target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = bubble });
        }
        Log.Write($"clic di prova sulla riga {index} (\"{(target is System.Windows.Controls.TextBlock text ? text.Text : "riga disegnata")}\", {list.SelectionMode}): pressione {(taken ? "presa prima della riga" : "alla riga")}, " +
                  $"righe scelte {before}, poi {list.SelectedItems.Count}; questa {(((System.Windows.Controls.ListBoxItem)row).IsSelected ? "scelta" : "non scelta")}");
    }

    public static void Dump(MessageSummary message, string html)
    {
        if (Environment.GetEnvironmentVariable("MISSIVA_DUMP") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Safety.SafeFileName($"{message.Date:yyyyMMdd-HHmm} {message.From.Display}") + ".html"), html);
    }

    /// <summary>Con MISSIVA_DUMP, l'HTML che partirebbe dalla finestra di scrittura, salvato quando si chiude (anche con Scarta).</summary>
    public static void DumpOutgoing(string html)
    {
        if (Environment.GetEnvironmentVariable("MISSIVA_DUMP") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"in uscita {DateTime.Now:yyyyMMdd-HHmmss}.html"), html);
    }
}
#endif
