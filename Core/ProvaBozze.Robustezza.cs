#if DEBUG
using System.Text.RegularExpressions;

namespace Missiva;

/// <summary>
/// Audit dell'11 ottobre, codice, secondo gruppo (docs\audit\ESITO.md): la robustezza, C-24..C-36, più due punti trovati
/// dai gruppi di prima (X-1 e X-2). Quasi tutti si provano facendo fallire un passo (un lavoro che solleva, un file che
/// non si scrive in una cartella temporanea, un server finto) e guardando che cosa resta appeso o perso. Solo dati finti e
/// cartelle temporanee; nessuna finestra di Missiva: delle parti che stanno nelle finestre si legge il sorgente.
/// Con "Missiva.exe --prova-audit risultato.txt robustezza" solo queste.
/// </summary>
static partial class ProvaBozze
{
    static void RobustnessAuditTests()
    {
        Report.Add("--- Audit dell'11 ottobre, codice, secondo gruppo: robustezza (C-24..C-36, X-1, X-2)");
        RobLogFailure();
        RobUnobserved();
        RobGuard();
        RobCategoryColors();
        Task.Run(RobPdfTimeoutAsync).GetAwaiter().GetResult(); // fuori dal thread STA, come PdfTestsAsync
        RobContactsCopy();
        RobRemovalTraces();
        RobGoogleRevokeAsync().GetAwaiter().GetResult();
        RobAsyncVoid();
        RobSupersededConnectAsync().GetAwaiter().GetResult();
        RobClosedProviderAsync().GetAwaiter().GetResult();
        RobLogoDomainsAtExit();
        RobRethrow();
        RobWebViewWindows();
        RobGmailCreateAsync().GetAwaiter().GetResult();
        RobUnobservedMeasure(); // in fondo: conta quelli di tutto il processo fino a qui
    }

    /// <summary>Una cartella temporanea di queste prove, vuota.</summary>
    static string RobTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "missiva-prova-robustezza-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void RobDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch (Exception) { }
    }

    static bool RobSameDir(string? a, string b) =>
        a != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// X-2: quando il registro non si riesce a scrivere, il motivo va in missiva-errori.log. Prima sempre in
    /// %LOCALAPPDATA%\Missiva, la cartella della Missiva installata, anche da una versione di prova che lavora altrove.
    /// Prima si guarda solo dove andrebbe (un percorso, nessuna scrittura); la scrittura si provoca soltanto se quel
    /// percorso sta nella cartella temporanea di questa prova, mai altrimenti.
    /// </summary>
    static void RobLogFailure()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Missiva");
        var dir = RobTemp();
        try
        {
            bool follows = RobSameDir(Path.GetDirectoryName(Log.FailurePath), Store.Dir);
            if (RobSameDir(Store.Dir, installed))
                Report.Add("      (X-2: questa prova gira senza MISSIVA_DATA, sulla cartella vera: dove va missiva-errori.log non si distingue)");
            else
                Check(follows && !RobSameDir(Path.GetDirectoryName(Log.FailurePath), installed),
                      "X-2, il file che dice che il registro non si scrive (missiva-errori.log) sta accanto al registro, nella cartella dei dati di questa versione: " +
                      "una versione di prova non scrive più nella cartella della Missiva installata");
            Log.TestDir = dir;
            bool inside = RobSameDir(Path.GetDirectoryName(Log.FailurePath), dir);
            string noted = "", after = "";
            if (inside) // solo così: la scrittura non si provoca mai fuori dalla cartella temporanea
            {
                Log.Write("riga di prova prima del blocco");
                var log = Path.Combine(dir, "missiva.log");
                using (new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Log.Write("riga che non si riesce a scrivere");
                Log.Write("riga di prova dopo il blocco");
                var failure = Path.Combine(dir, "missiva-errori.log");
                noted = File.Exists(failure) ? File.ReadAllText(failure) : "";
                after = File.ReadAllText(log);
            }
            Check(inside && noted.Contains("registro non scritto") && noted.Contains(nameof(IOException)) && !after.Contains("riga che non si riesce a scrivere")
                  && after.Contains("riga di prova prima del blocco") && after.Contains("riga di prova dopo il blocco") && Directory.GetFiles(dir).Length == 2,
                  "X-2, con il registro tenuto aperto da un altro (in una cartella temporanea) la riga persa lascia il motivo in missiva-errori.log in quella stessa cartella, e il registro riprende appena si libera" +
                  (inside ? "" : " (non provato: il file andrebbe fuori dalla cartella temporanea, e la scrittura non si provoca)"));
        }
        finally
        {
            Log.TestDir = null;
            RobDelete(dir);
        }
    }

    /// <summary>Un lavoro lanciato senza aspettarlo, che fallisce: da un metodo a parte, perché dopo nessuno tenga più il Task.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void RobDiscardFailing(ManualResetEventSlim ran)
    {
        _ = Task.Run(() =>
        {
            ran.Set();
            throw new InvalidOperationException("errore di prova C-24");
        });
    }

    /// <summary>
    /// C-24: un lavoro lanciato con "_ =" che fallisce. Prima spariva senza una riga; ora, quando .NET raccoglie il Task, la
    /// riga è nel registro con l'errore e il punto in cui è nato. Il registro di questa prova è in una cartella temporanea.
    /// </summary>
    static void RobUnobserved()
    {
        var dir = RobTemp();
        try
        {
            Log.WatchUnhandled(); // come Program.Main; nella suite è già stato fatto all'inizio, e vale una volta sola
            Log.TestDir = dir;
            var path = Path.Combine(dir, "missiva.log");
            using var ran = new ManualResetEventSlim();
            RobDiscardFailing(ran);
            ran.Wait(5000);
            string log = "";
            int rounds = 0;
            for (; rounds < 150 && !log.Contains("errore di prova C-24"); rounds++)
            {
                Thread.Sleep(20);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                log = File.Exists(path) ? File.ReadAllText(path) : "";
            }
            Check(log.Contains("ERRORE (lavoro in sottofondo che nessuno aspettava): System.InvalidOperationException: errore di prova C-24") && log.Contains(nameof(RobDiscardFailing)),
                  "C-24, un lavoro lanciato senza aspettarlo che fallisce lascia nel registro l'errore e il metodo in cui è nato, quando .NET raccoglie il Task " +
                  $"(qui dopo {rounds} raccolte forzate); prima nessuna riga");
        }
        finally
        {
            Log.TestDir = null;
            RobDelete(dir);
        }
    }

    /// <summary>Il testo di un metodo in un sorgente, da dove comincia a dove comincia quello dopo; null se uno dei due non si trova.</summary>
    static string? RobPiece(string? source, string from, string to)
    {
        if (source == null) return null;
        int start = source.IndexOf(from, StringComparison.Ordinal);
        int end = start < 0 ? -1 : source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        return end < 0 ? null : source[start..end];
    }

    /// <summary>
    /// C-25 e C-26: le attese che restavano appese se un passo falliva. Log.GuardAsync, con un lavoro che solleva prima e
    /// dopo il primo await: l'errore va nel registro con il contesto e non esce, così quello che viene dopo si fa. Che le
    /// finestre lo usino (l'uscita dopo le azioni di una notifica, l'avvio nascosto) e che le due attese degli account si
    /// chiudano in un finally si legge dal sorgente: nessuna prova crea la finestra principale.
    /// </summary>
    static void RobGuard()
    {
        var dir = RobTemp();
        try
        {
            Log.TestDir = dir;
            bool fine = Log.GuardAsync("prova, lavoro riuscito", () => Task.CompletedTask).GetAwaiter().GetResult();
            bool sync = true, later = true, after = false;
            string? thrown = null;
            try
            {
                sync = Log.GuardAsync("prova C-26, subito", () => throw new InvalidOperationException("errore prima del primo await")).GetAwaiter().GetResult();
                later = Log.GuardAsync("prova C-26, più tardi", async () =>
                {
                    await Task.Delay(20);
                    throw new MailException("errore dopo un await");
                }).GetAwaiter().GetResult();
                after = true; // quello che viene dopo l'azione fallita: in App, il conto delle azioni e l'uscita
            }
            catch (Exception ex) { thrown = ex.GetType().Name; }
            var path = Path.Combine(dir, "missiva.log");
            var log = File.Exists(path) ? File.ReadAllText(path) : "";
            Check(fine && !sync && !later && after && thrown == null && !log.Contains("lavoro riuscito")
                  && log.Contains("ERRORE (prova C-26, subito): System.InvalidOperationException: errore prima del primo await")
                  && log.Contains("ERRORE (prova C-26, più tardi): Missiva.MailException: errore dopo un await"),
                  "C-26, un lavoro che fallisce dentro Log.GuardAsync (prima o dopo il primo await) lascia nel registro l'errore con il suo contesto e non esce: quello che viene dopo si fa" +
                  (thrown == null ? "" : $" (uscito: {thrown})"));
        }
        finally
        {
            Log.TestDir = null;
            RobDelete(dir);
        }
        // I sorgenti ci sono solo accanto alla cartella dei sorgenti; se ci sono, un metodo che non si trova è un controllo caduto.
        var app = BehSource("UI", "App.cs");
        if (app != null)
            Check(RobPiece(app, "async Task CountActionAsync(Func<Task> action)", "bool startedForNotification;") is { } count
                  && count.Contains("await Log.GuardAsync(\"azione dalla notifica\", action);") && !count.Contains("await action()")
                  && count.IndexOf("notificationActions--;") > count.IndexOf("Log.GuardAsync(") && count.IndexOf("ExitAll();") > count.IndexOf("notificationActions--;"),
                  "C-26, l'azione di una notifica gira dentro Log.GuardAsync: se fallisce il conto delle azioni scende lo stesso e il controllo \"finite tutte, esci\" si fa " +
                  "(prima stava dopo un finally: con un errore si saltava, e Missiva avviata solo per l'azione restava accesa e nascosta)");
        if (app != null)
            Check(app.Contains("_ = Log.GuardAsync(\"avvio nascosto nell'area di notifica\", main.StartAsync);") && !app.Contains("_ = main.StartAsync();"),
                  "C-25, l'avvio nascosto nell'area di notifica, che nessuno aspetta, gira dentro Log.GuardAsync: un suo errore è nel registro subito");
        if (BehSource("UI", "MainWindow.xaml.cs") is { } main)
            Check(RobPiece(main, "public async Task LoadAccountsAsync()", "async Task ListAccountsAsync()") is { } load
                  && Regex.IsMatch(load, @"try\s*\{\s*await ListAccountsAsync\(\);\s*\}\s*finally\s*\{\s*accountsListed\.TrySetResult\(\);\s*accountsReady\.TrySetResult\(\);\s*\}"),
                  "C-25, le due attese degli account (accountsListed e accountsReady) si chiudono in un finally: se un passo del caricamento fallisce, l'azione di una notifica, " +
                  "il link mailto: e il clic su una notifica ripartono invece di aspettare per sempre");
    }

    /// <summary>
    /// C-27: i colori noti delle categorie (CategoryPalette.Known), scritti dall'interfaccia e letti dai provider. Un thread
    /// aggiunge account, come Remember dopo ogni lettura delle categorie, e un altro chiede di continuo il colore di una
    /// categoria che c'è da prima: senza lucchetto, mentre il dizionario si allarga, la risposta è quella di chi non la
    /// conosce (o un'eccezione). E la finestra della regola non chiama più il provider da un thread del pool (sorgente).
    /// </summary>
    static void RobCategoryColors()
    {
        const string known = "c27-prova-fisso";
        const int accounts = 120_000;
        CategoryPalette.Remember(known, [new Category("Rossa di prova", 17)]); // 17: il colore ricavato dal nome è sempre fra 0 e 9
        int asked = 0, wrong = 0, errors = 0;
        bool writing = true;
        string? writerError = null;
        var reader = new Thread(() =>
        {
            while (Volatile.Read(ref writing))
            {
                try
                {
                    if (CategoryPalette.PresetOf(known, "Rossa di prova") != 17) wrong++;
                }
                catch (Exception) { errors++; }
                asked++;
            }
        });
        var writer = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < accounts; i++) CategoryPalette.Remember("c27-prova-" + i, [new Category("Altra", 2)]);
            }
            catch (Exception ex) { writerError = ex.GetType().Name; }
            finally { Volatile.Write(ref writing, false); }
        });
        reader.Start();
        writer.Start();
        writer.Join();
        reader.Join();
        CategoryPalette.TestForget(id => id.StartsWith("c27-prova-", StringComparison.Ordinal));
        Check(wrong == 0 && errors == 0 && writerError == null && asked > 1000,
              $"C-27, i colori noti delle categorie letti da un thread mentre un altro ne aggiunge {accounts:N0}: {asked:N0} domande, {wrong} risposte sbagliate, {errors + (writerError == null ? 0 : 1)} errori " +
              "(senza lucchetto il dizionario, mentre si allarga, risponde come se la categoria non ci fosse)");
        if (BehSource("UI", "RuleWindow.cs") is { } rule)
            Check(RobPiece(rule, "async Task LoadCategoriesAsync()", "async Task AllowAsync()") is { } load && !load.Contains("Task.Run") && !load.Contains("Dispatcher.Invoke")
                  && load.Contains("var list = await provider.GetCategoriesAsync();") && load.Contains("_ = LoadCategoriesAsync();"),
                  "C-27, la finestra della regola chiede le categorie al provider dal thread dell'interfaccia, come ogni altra richiesta (prima da un thread del pool, con Task.Run)");
    }

    /// <summary>
    /// C-29: il processo dei PDF che non risponde entro il tempo di una richiesta (qui accorciato). Missiva lo chiude, e la
    /// richiesta di prima finisce un momento dopo, sul suo thread: la richiesta dopo non deve partire finché quella non ha
    /// finito, perché tutte e due toccano il processo e i suoi tubi. La richiesta ferma è una lettura dal processo vero,
    /// nel suo AppContainer, che non manda nulla finché non gli si chiede qualcosa; poi una seconda anteprima, subito.
    /// </summary>
    static async Task RobPdfTimeoutAsync()
    {
        using var worker = new PdfWorker { RequestTimeout = TimeSpan.FromMilliseconds(800) };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long firstEnded = -1, secondStarted = -1;
        string first, readEnd = "ancora ferma";
        try
        {
            await worker.TestRequestAsync((_, reader) =>
            {
                try { return reader.ReadByte(); }
                catch (Exception ex) { readEnd = ex.GetType().Name; throw; }
                finally
                {
                    Thread.Sleep(500); // la richiesta di prima ci mette ancora un momento a uscire
                    Volatile.Write(ref firstEnded, clock.ElapsedMilliseconds);
                }
            });
            first = "nessun errore";
        }
        catch (Exception ex) { first = ex.GetType().Name; }
        long gaveUp = clock.ElapsedMilliseconds;
        int pages = 0;
        string second = "";
        try
        {
            await worker.TestRequestAsync((_, _) =>
            {
                Volatile.Write(ref secondStarted, clock.ElapsedMilliseconds);
                return 0;
            });
            (pages, _, _) = await worker.LoadAsync(Pdf(1));
        }
        catch (Exception ex) { second = $", errore {ex.GetType().Name}"; }
        long loaded = clock.ElapsedMilliseconds;
        for (int i = 0; i < 100 && Volatile.Read(ref firstEnded) < 0; i++) await Task.Delay(50);
        long ended = Volatile.Read(ref firstEnded), started = Volatile.Read(ref secondStarted);
        Check(first == nameof(TimeoutException) && ended >= 0 && started >= ended && pages == 1,
              $"C-29, processo dei PDF fermo oltre il tempo di una richiesta ({worker.RequestTimeout.TotalMilliseconds:0} ms nella prova): la richiesta esce con {first} a {gaveUp} ms; " +
              $"quella di prima finisce a {ended} ms (lettura: {readEnd}) e quella dopo parte a {started} ms, non prima; la seconda anteprima si carica ({pages} pagina a {loaded} ms{second})");
    }

    /// <summary>
    /// C-30: i contatti per i suggerimenti (ContactBook.ForSuggestions) chiesti da un thread mentre un altro ne aggiunge,
    /// come AddressBook.Preload all'avvio (in Task.Run) accanto all'interfaccia. Prima l'elenco si copiava fuori dal
    /// lucchetto. I contatti di prova non si salvano: alla fine si rilegge il file.
    /// </summary>
    static void RobContactsCopy()
    {
        const int added = 6000;
        ContactBook.Reset();
        int asked = 0, errors = 0;
        string? kind = null, writerError = null;
        bool adding = true;
        var reader = new Thread(() =>
        {
            while (Volatile.Read(ref adding))
            {
                try { ContactBook.ForSuggestions(); }
                catch (Exception ex)
                {
                    errors++;
                    kind ??= ex.GetType().Name;
                }
                asked++;
            }
        });
        var writer = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < added; i++)
                    ContactBook.Add([new Contact { DisplayName = "Contatto di prova " + i, Emails = [$"c30.{i}@contatti-prova.example"] }]);
            }
            catch (Exception ex) { writerError = ex.GetType().Name; }
            finally { Volatile.Write(ref adding, false); }
        });
        try
        {
            reader.Start();
            writer.Start();
            writer.Join();
            reader.Join();
        }
        finally
        {
            ContactBook.Reset();
        }
        Check(errors == 0 && writerError == null && asked > 20,
              $"C-30, i contatti per i suggerimenti chiesti {asked:N0} volte da un thread mentre un altro ne aggiunge {added:N0}: {errors} errori" +
              (kind == null ? "" : $" ({kind})") + (writerError == null ? "" : $", chi aggiunge: {writerError}") + "; prima l'elenco si copiava fuori dal lucchetto");
    }

    /// <summary>
    /// C-32: le copie di un account rimosso che non si riescono a cancellare (un file tenuto aperto da un altro). In una
    /// cartella dei dati temporanea (Store.TestDataRoot) tre file della copia tenuti aperti: la cartella dell'elenco, il
    /// calendario, i contatti di Outlook. Prima le tre cancellazioni fallivano in silenzio; ora ognuna lascia la sua riga.
    /// Liberati i file le copie si tolgono, e chiedere di togliere quello che non c'è non scrive nulla.
    /// </summary>
    static void RobRemovalTraces()
    {
        var dir = RobTemp();
        var logs = Path.Combine(dir, "registro");
        Directory.CreateDirectory(logs);
        try
        {
            Store.TestDataRoot = dir;
            Log.TestDir = logs;
            ContactBook.Reset();
            var account = new MailAccount { Kind = AccountKind.Imap, Email = "mario@studio-prova.example", Id = "c32prova" };
            var name = Store.CacheName(account.Id);
            string lists = Store.CachePath(name), calendar = Store.CachePath($"calendario-{name}.json"), contacts = Store.CachePath($"contatti-{name}.dat");
            Directory.CreateDirectory(lists);
            foreach (var file in new[] { Path.Combine(lists, "INBOX.json"), Path.Combine(lists, "altra.json"), calendar, contacts }) File.WriteAllText(file, "prova");
            string Lines() => File.Exists(Path.Combine(logs, "missiva.log")) ? File.ReadAllText(Path.Combine(logs, "missiva.log")) : "";
            void Remove()
            {
                MailCache.Delete(account);
                Calendars.Delete(account);
                ContactBook.ForgetRemote(account.Id);
            }
            using (new FileStream(Path.Combine(lists, "INBOX.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            using (new FileStream(calendar, FileMode.Open, FileAccess.Read, FileShare.None))
            using (new FileStream(contacts, FileMode.Open, FileAccess.Read, FileShare.None))
                Remove();
            var held = Lines();
            bool stillThere = Directory.Exists(lists) && File.Exists(calendar) && File.Exists(contacts);
            Check(stillThere && held.Contains("ERRORE (copia locale dell'account rimosso non cancellata)") && held.Contains("ERRORE (copia del calendario dell'account rimosso non cancellata)")
                  && held.Contains("ERRORE (copia dei contatti dell'account rimosso non cancellata)"),
                  "C-32, account rimosso con tre file della sua copia tenuti aperti da un altro (elenchi, calendario, contatti di Outlook): le copie restano sul disco e ognuna lascia la sua riga nel registro; prima nessuna riga");
            Remove();
            bool gone = !Directory.Exists(lists) && !File.Exists(calendar) && !File.Exists(contacts);
            var afterFree = Lines();
            Remove(); // di nuovo, con niente da togliere
            Directory.Delete(Store.CachePath(""), true);
            Remove(); // e senza nemmeno la cartella della copia
            Check(gone && afterFree == held && Lines() == held,
                  "C-32, liberati i file le copie dell'account si tolgono; togliere quello che non c'è più (nemmeno la cartella della copia) non scrive righe nel registro");
        }
        finally
        {
            Store.TestDataRoot = null;
            Log.TestDir = null;
            ContactBook.Reset();
            RobDelete(dir);
        }
    }

    /// <summary>
    /// C-32: la revoca dell'accesso a Google alla rimozione dell'account. Prima la risposta non si guardava e un errore si
    /// perdeva. Con un Google finto (GoogleAuth.TestHttp): risposta 200, risposta 400, rete che cade. Il registro, in una
    /// cartella temporanea, dice com'è andata, e il token non c'è mai.
    /// </summary>
    static async Task RobGoogleRevokeAsync()
    {
        var dir = RobTemp();
        var requests = new List<(string Url, string Form)>();
        var answer = System.Net.HttpStatusCode.OK;
        bool down = false;
        const string token = "aggiornamento-finto-c32";
        try
        {
            Log.TestDir = dir;
            GoogleAuth.TestHttp = new HttpClient(new FakeHttp(request =>
            {
                requests.Add((request.RequestUri!.ToString(), request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));
                if (down) throw new HttpRequestException("rete di prova assente");
                return new HttpResponseMessage(answer) { Content = new StringContent(answer == System.Net.HttpStatusCode.OK ? "{}" : "{\"error\":\"invalid_token\"}") };
            }));
            var account = new MailAccount { Kind = AccountKind.Gmail, Email = "mario.prova@gmail.example", ProtectedRefreshToken = Store.Protect(token) };
            // Dalle scelte del 12 ottobre l'esito ha tre valori (GoogleRevocation, per l'avviso a schermo: ProvaBozze.Scelte1.cs); qui conta "revocato o no".
            bool revoked = await GoogleAuth.SignOutAsync(account) == GoogleRevocation.Revoked;
            answer = System.Net.HttpStatusCode.BadRequest;
            bool refused = await GoogleAuth.SignOutAsync(account) == GoogleRevocation.Revoked;
            down = true;
            string? thrown = null;
            bool offline = true;
            try { offline = await GoogleAuth.SignOutAsync(account) == GoogleRevocation.Revoked; }
            catch (Exception ex) { thrown = ex.GetType().Name; }
            bool none = await GoogleAuth.SignOutAsync(new MailAccount { Kind = AccountKind.Gmail, Email = "senza.accesso@gmail.example" }) == GoogleRevocation.Revoked;
            var log = File.ReadAllText(Path.Combine(dir, "missiva.log"));
            Check(revoked && !refused && !offline && !none && thrown == null && requests.Count == 3 && requests.All(r => r.Url == "https://oauth2.googleapis.com/revoke" && r.Form == "token=" + token)
                  && log.Contains("accesso a Google di mario.prova@gmail.example revocato") && log.Contains("non revocato: Google ha risposto 400")
                  && log.Contains("non revocato (HttpRequestException: rete di prova assente)") && !log.Contains(token) && !log.Contains("senza.accesso"),
                  "C-32, rimozione di un account Gmail: la revoca dell'accesso presso Google si guarda. Con 200 il registro dice revocato; con 400 e con la rete che cade dice che non lo è (con la rete caduta, che presso " +
                  "Google può restare valido; con il 400 \"invalid_token\" di questa prova, dalle scelte del 12 ottobre, che non valeva già più) (prima niente); mai il token nel registro, e senza un accesso salvato nessuna richiesta");
        }
        finally
        {
            GoogleAuth.TestHttp = null;
            Log.TestDir = null;
            RobDelete(dir);
        }
    }

    static async void RobFailingAsyncVoid()
    {
        await Task.Delay(30);
        throw new InvalidOperationException("errore di prova C-33");
    }

    /// <summary>
    /// C-33: gli async void senza protezione. Che cosa succede a un loro errore sul thread dell'interfaccia: arriva al
    /// gestore degli errori del Dispatcher (in Missiva App.DispatcherUnhandledException, che lo scrive nel registro e lo
    /// dice una volta per sessione) con la traccia che dice il metodo. Qui lo stesso gestore sul Dispatcher della suite.
    /// E l'attesa di Rispondi con modello (recipientsReady) si chiude in un finally: dal sorgente, senza finestra.
    /// </summary>
    static void RobAsyncVoid()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Exception? caught = null;
        System.Windows.Threading.DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
        {
            caught = e.Exception;
            e.Handled = true;
        };
        var before = SynchronizationContext.Current;
        dispatcher.UnhandledException += handler;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
        try
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            RobFailingAsyncVoid();
            var poll = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(20), System.Windows.Threading.DispatcherPriority.Normal,
                (_, _) => { if (caught != null || clock.ElapsedMilliseconds > 3000) frame.Continue = false; }, dispatcher);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            poll.Stop();
        }
        finally
        {
            dispatcher.UnhandledException -= handler;
            SynchronizationContext.SetSynchronizationContext(before);
        }
        Check(caught is InvalidOperationException { Message: "errore di prova C-33" } && caught.ToString().Contains(nameof(RobFailingAsyncVoid)),
              "C-33, un async void che fallisce dopo un await sul thread dell'interfaccia non si perde: arriva al gestore degli errori del Dispatcher, con la traccia che dice il metodo " +
              "(in Missiva quel gestore lo scrive nel registro e lo dice una volta per sessione)");
        if (BehSource("UI", "ComposeWindow.xaml.cs") is { } compose)
            Check(Regex.IsMatch(compose, @"try\s*\{\s*if \(draft != null\) await LoadDraftAsync\(from, draft, draftBody\);\s*await FillRecipientsAsync\(from\);\s*\}\s*finally\s*\{[^}]*recipientsReady\.TrySetResult\(\);\s*\}"),
                  "C-33, Rispondi con modello: l'attesa dei destinatari (recipientsReady) si chiude in un finally, anche se la bozza o i destinatari falliscono; prima il modello non arrivava più, in silenzio");
    }

    /// <summary>
    /// C-34: i domini dei mittenti (domini.dat) si salvano cinque secondi dopo l'ultima novità; uscendo prima, la novità si
    /// perdeva. SenderLogos.Flush, che App chiama all'uscita e a fine sessione, la scrive subito; senza niente in attesa
    /// non riscrive il file (il cifrato cambia a ogni scrittura: stessi byte, nessuna scrittura).
    /// </summary>
    static void RobLogoDomainsAtExit()
    {
        var domain = $"c34-{Guid.NewGuid().ToString("N")[..8]}.example";
        var path = Path.Combine(Store.Dir, "loghi", "domini.dat");
        string Disk() => File.Exists(path) ? Store.Unprotect(File.ReadAllText(path)) : "";
        try
        {
            SenderLogos.TestReload(); // niente in attesa: quello che c'era è sul disco
            var bytes = File.ReadAllBytes(path);
            SenderLogos.Flush();
            bool idle = File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
            SenderLogos.Note(new MessageSummary { Id = "1", FolderId = "INBOX", Subject = "Prova", Date = DateTime.Now, From = new Address { Name = "Negozio di prova", Email = "info@" + domain } });
            bool early = Disk().Contains(domain);
            SenderLogos.Flush();
            bool flushed = Disk().Contains(domain);
            Check(idle && !early && flushed,
                  "C-34, un dominio visto un attimo prima di uscire: il suo salvataggio aspetta cinque secondi, e all'uscita SenderLogos.Flush lo scrive subito in domini.dat " +
                  "(prima si perdeva); senza novità in attesa il file non si riscrive");
        }
        finally
        {
            SenderLogos.TestForget([domain]);
        }
        if (BehSource("UI", "App.cs") is { } app)
            Check(RobPiece(app, "protected override void OnExit(ExitEventArgs e)", "bool restartAfterExit;") is { } exit && exit.Contains("SenderLogos.Flush();")
                  && RobPiece(app, "SessionEnding += (_, ev) =>", "MicrosoftAuth.Quiet") is { } ending && ending.Contains("SenderLogos.Flush();"),
                  "C-34, App chiama SenderLogos.Flush all'uscita (OnExit) e alla fine della sessione di Windows");
    }

    /// <summary>
    /// C-35: "throw ex" dentro un catch rilancia l'errore tagliando la traccia al punto del rilancio. Nei sorgenti non ne
    /// resta nessuno (quello del riquadro di lettura, senza rete e senza copia su disco, ora è "throw;"); e che differenza
    /// fa per il registro, con un errore nato tre chiamate più sotto.
    /// </summary>
    static void RobRethrow()
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void RobProviderFails() => throw new IOException("rete di prova assente");
        string Trace(bool keep)
        {
            try
            {
                try { RobProviderFails(); }
                catch (Exception ex)
                {
                    if (keep) throw;
#pragma warning disable CA2200 // qui apposta: è il confronto con com'era
                    throw ex;
#pragma warning restore CA2200
                }
            }
            catch (Exception ex) { return ex.StackTrace ?? ""; }
            return "";
        }
        bool before = Trace(keep: false).Contains(nameof(RobProviderFails)), after = Trace(keep: true).Contains(nameof(RobProviderFails));
        var found = new List<string>();
        if (SourceRoot() is { } root)
            foreach (var dir in new[] { "Core", "UI" })
                foreach (var file in Directory.GetFiles(Path.Combine(root, dir), "*.cs"))
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith("ProvaBozze")) continue;
                    int n = 0;
                    foreach (var line in File.ReadLines(file))
                    {
                        n++;
                        if (Regex.IsMatch(line, @"\bthrow\s+(ex|e|error|exception)\s*;")) found.Add($"{dir}\\{name}:{n}");
                    }
                }
        Check(!before && after && found.Count == 0,
              "C-35, un errore rilanciato con \"throw;\" tiene nella traccia il metodo in cui è nato, con \"throw ex\" no; nei sorgenti non resta nessun \"throw ex\"" +
              (found.Count == 0 ? "" : $" (restano: {string.Join(", ", found)})"));
    }

    /// <summary>
    /// C-36: che cosa resta di WebView2 quando la finestra che lo ospita si chiude senza che nessuno chiuda il controllo,
    /// come fa la finestra di un messaggio. Finestre vere di WPF, fuori dallo schermo e mai in primo piano, ognuna con il
    /// controllo di WebView2 che usa il riquadro di lettura dentro una griglia, in un ambiente suo (una cartella nella
    /// cartella dei dati di prova, nessun nome di rete risolto). Si contano i processi di disegno dell'ambiente: con tre
    /// finestre in più, e due secondi dopo averle chiuse e basta, senza raccolte forzate di .NET.
    /// </summary>
    static void RobWebViewWindows()
    {
        (int Base, int Open, int Closed) n;
        try { n = Sec2Pump(RobWebViewWindowsAsync, 90); }
        catch (Exception ex)
        {
            Report.Add($"      (C-36, non misurato: WebView2 in una finestra di prova non parte: {ex.GetType().Name})");
            return;
        }
        Check(n.Open >= n.Base + 3 && n.Closed <= n.Base,
              $"C-36, tre finestre con WebView2 chiuse senza chiudere il controllo: i loro processi di disegno escono da soli entro due secondi (processi di disegno: {n.Base} con una finestra, " +
              $"{n.Open} con tre in più, {n.Closed} dopo averle chiuse). Il controllo di WPF si chiude con la sua finestra: il sospetto del rapporto non è confermato");
    }

    static async Task<(int Base, int Open, int Closed)> RobWebViewWindowsAsync()
    {
        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(EngineRoot, "WebView2-prove-finestre"),
            new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions
            {
                Language = "it-IT",
                AdditionalBrowserArguments = UI.ReadingView.BrowserArguments + " --host-resolver-rules=\"MAP * ~NOTFOUND\"",
            });
        var windows = new List<(System.Windows.Window Window, Microsoft.Web.WebView2.Wpf.WebView2 Web)>();
        async Task OpenAsync()
        {
            var web = new Microsoft.Web.WebView2.Wpf.WebView2();
            var slot = new System.Windows.Controls.Grid();
            slot.Children.Add(web);
            var window = new System.Windows.Window
            {
                Content = slot, Width = 420, Height = 320, Left = -30000, Top = -30000, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                ShowInTaskbar = false, ShowActivated = false, WindowStyle = System.Windows.WindowStyle.ToolWindow, Title = "Missiva, prova C-36",
            };
            window.Show();
            await web.EnsureCoreWebView2Async(env);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            web.CoreWebView2.NavigationCompleted += (_, _) => loaded.TrySetResult();
            web.CoreWebView2.NavigateToString("<!doctype html><p>Pagina di prova</p>");
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
            windows.Add((window, web));
        }
        int Renderers() => env.GetProcessInfos().Count(p => p.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ProcessKind.Renderer);
        await OpenAsync(); // resta aperta fino alla fine: tiene vivo il processo del browser dell'ambiente
        var kept = windows[0];
        windows.Clear();
        try
        {
            await Task.Delay(500);
            int atStart = Renderers();
            for (int i = 0; i < 3; i++) await OpenAsync();
            await Task.Delay(500);
            int open = Renderers();
            foreach (var (window, _) in windows) window.Close();
            await Task.Delay(2000);
            return (atStart, open, Renderers()); // i controlli sono ancora nell'elenco: nessuna raccolta di .NET li ha toccati
        }
        finally
        {
            foreach (var (window, web) in windows.Append(kept))
            {
                try { web.Dispose(); } catch (Exception) { }
                try { window.Close(); } catch (Exception) { }
            }
        }
    }

    /// <summary>Davanti al server IMAP finto: accetta il collegamento, aspetta, poi passa i byte nei due versi. Un collegamento lento.</summary>
    sealed class RobSlowFront : IDisposable
    {
        readonly System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        readonly List<System.Net.Sockets.TcpClient> open = new();
        int accepted;

        public int Port => ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        public int Accepted => Volatile.Read(ref accepted);

        public RobSlowFront(int target, TimeSpan delay)
        {
            listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var client = await listener.AcceptTcpClientAsync();
                        Interlocked.Increment(ref accepted);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(delay);
                                var server = new System.Net.Sockets.TcpClient();
                                await server.ConnectAsync(System.Net.IPAddress.Loopback, target);
                                lock (open) { open.Add(client); open.Add(server); }
                                static async Task Relay(System.Net.Sockets.NetworkStream from, System.Net.Sockets.NetworkStream to)
                                {
                                    try { await from.CopyToAsync(to); }
                                    catch (Exception) { } // chiuso dall'altro capo
                                }
                                await Task.WhenAny(Relay(client.GetStream(), server.GetStream()), Relay(server.GetStream(), client.GetStream()));
                            }
                            catch (Exception) { }
                            finally { client.Dispose(); }
                        });
                    }
                }
                catch (Exception) { } // chiuso
            });
        }

        public void Dispose()
        {
            listener.Stop();
            lock (open) foreach (var c in open) c.Dispose();
        }
    }

    /// <summary>
    /// C-31: il token di annullamento non passa al collegamento e all'accesso IMAP. Che cosa succede con un collegamento
    /// lento (0,6 s nella prova) e cinque messaggi cliccati di fila, ognuno che supera quello di prima, poi un sesto che
    /// resta: quanti collegamenti partono verso il server, quanti corpi si chiedono, e quando arriva il sesto.
    /// </summary>
    static async Task RobSupersededConnectAsync()
    {
        const int delay = 600;
        using var fake = new FakeImap(new Flavor(Gmail: false));
        for (int i = 1; i <= 6; i++) fake.Add("INBOX", SpeedMail("Messaggio di prova " + i, "Buongiorno"));
        using var front = new RobSlowFront(fake.Port, TimeSpan.FromMilliseconds(delay));
        ImapProvider.TestPlain = true;
        try
        {
            var account = SpeedAccount(fake, "mario@studio-prova.example");
            account.Imap!.ImapPort = front.Port;
            using var provider = new ImapProvider(account);
            var inbox = (await provider.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            var listed = (await provider.GetMessagesAsync(inbox, null, null)).Messages;
            await provider.ResetConnectionsAsync(); // tutte le connessioni chiuse: il prossimo clic si collega da capo
            await Task.Delay(200);
            int before = front.Accepted;
            lock (fake.Commands) fake.Commands.Clear();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var superseded = new List<Task<string>>();
            for (int i = 0; i < 5; i++)
            {
                var cancel = new CancellationTokenSource();
                var asked = provider.GetBodyAsync(listed[i], cancel.Token);
                superseded.Add(asked.ContinueWith(t => t.IsCompletedSuccessfully ? "letto" : t.IsCanceled || t.Exception?.InnerException is OperationCanceledException ? "annullato" : "errore", TaskScheduler.Default));
                await Task.Delay(60);
                cancel.Cancel();
            }
            var last = await provider.GetBodyAsync(listed[5]);
            long ms = clock.ElapsedMilliseconds;
            var ends = await Task.WhenAll(superseded);
            int connections = front.Accepted - before, bodies;
            lock (fake.Commands) bodies = fake.Commands.Count(c => c.Contains("FETCH", StringComparison.OrdinalIgnoreCase) && c.Contains("BODY", StringComparison.OrdinalIgnoreCase));
            Check(connections == 1 && last.Html.Contains("Buongiorno") && ends[0] == "letto" && ends.Skip(1).All(e => e == "annullato") && ms < delay * 2,
                  $"C-31, collegamento IMAP lento ({delay} ms nella prova) e sei messaggi cliccati di fila: un solo collegamento verso il server ({connections}), non uno per clic; il primo clic, già partito, finisce " +
                  $"({ends[0]}), gli altri quattro escono dalla coda ({string.Join(", ", ends.Skip(1).Distinct())}) e il sesto arriva a {ms} ms sul collegamento aperto dal primo ({bodies} richieste di corpi al server)");
        }
        finally
        {
            ImapProvider.TestPlain = false;
        }
    }

    /// <summary>
    /// X-1: una finestra di scrittura aperta mentre gli account si ricaricano (Aggiungi account, Aggiungi casella condivisa,
    /// Rimuovi account) tiene i provider di prima, chiusi. Prima: elenco e bozza davano l'errore di .NET per un oggetto già
    /// rilasciato, e un messaggio nuovo partiva via SMTP senza copia in Posta inviata e senza errori. Ora un provider chiuso
    /// rifiuta ogni richiesta, anche l'invio, dicendo perché, e la finestra di scrittura passa a quello nuovo dello stesso
    /// account (ComposeWindow.Current, qui chiamata come fa la finestra): l'invio parte e lascia la sua copia.
    /// </summary>
    static async Task RobClosedProviderAsync()
    {
        static async Task<string> Try(Func<Task> request)
        {
            try
            {
                await request();
                return "nessun errore";
            }
            catch (MailException ex) when (ex.Message.Contains("è stato chiuso")) { return "rifiutato"; }
            catch (Exception ex) { return ex.GetType().Name; }
        }
        static OutgoingMessage Mail(string subject) => new() { To = To, Subject = subject, Html = "<p>Ciao</p>", Text = "Ciao" };

        using var smtp = new FakeSmtp();
        string imapList, imapDraft, imapSend, gmailList, gmailDraft, gmailSend;
        int imapArrived, imapCopies, afterArrived, afterCopies, gmailArrived, gmailAfter;
        bool moved, stays, untouched, gmailMoved;
        ImapProvider.TestPlain = true;
        ImapProvider.TestCertificate = smtp.Certificate.GetCertHashString();
        try
        {
            using var fake = new FakeImap(new Flavor(Gmail: false));
            fake.Add("INBOX", SpeedMail("Messaggio di prova", "Buongiorno"));
            var account = SpeedAccount(fake, "mario@studio-prova.example");
            account.Imap!.SmtpPort = smtp.Port;
            account.Imap.SmtpSecurity = MailSecurity.StartTls;
            var old = new ImapProvider(account);
            var inbox = (await old.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            untouched = !old.IsClosed && UI.ComposeWindow.Current(old, _ => new ImapProvider(account)) == old; // finché è aperto resta lui
            old.Dispose(); // come MainWindow.LoadAccountsAsync
            await Task.Delay(300);
            imapList = await Try(() => old.GetMessagesAsync(inbox, null, null));
            imapDraft = await Try(() => old.SaveDraftAsync(Mail("Bozza dal provider chiuso")));
            imapSend = await Try(() => old.SendAsync(Mail("Inviato dal provider chiuso")));
            (imapArrived, imapCopies) = (smtp.Received.Count, fake.Folders[fake.SentFolder].Count);
            using var fresh = new ImapProvider(account); // quello nuovo dello stesso account
            var now = UI.ComposeWindow.Current(old, id => id == account.Id ? fresh : null);
            moved = now == fresh;
            stays = old.IsClosed && UI.ComposeWindow.Current(old, _ => null) == old; // account rimosso: resta quello chiuso, che rifiuta
            await now.SendAsync(Mail("Inviato dal provider nuovo"));
            (afterArrived, afterCopies) = (smtp.Received.Count, fake.Folders[fake.SentFolder].Count);
        }
        finally
        {
            ImapProvider.TestPlain = false;
            ImapProvider.TestCertificate = null;
        }
        try
        {
            using var fake = new FakeImap();
            GmailProvider.TestPort = fake.Port;
            GmailProvider.TestSmtpPort = smtp.Port;
            GmailProvider.TestSmtpCertificate = smtp.Certificate.GetCertHashString();
            var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", Id = "gmail-x1" };
            var old = new GmailProvider(account);
            var inbox = (await old.GetFoldersAsync()).First(f => f.Kind == FolderKind.Inbox);
            old.Dispose();
            await Task.Delay(300);
            int before = smtp.Received.Count;
            gmailList = await Try(() => old.GetMessagesAsync(inbox, null, null));
            gmailDraft = await Try(() => old.SaveDraftAsync(Mail("Bozza dal provider chiuso")));
            gmailSend = await Try(() => old.SendAsync(Mail("Inviato dal provider chiuso")));
            gmailArrived = smtp.Received.Count - before;
            using var fresh = new GmailProvider(account);
            var now = UI.ComposeWindow.Current(new TestingProvider(old), id => id == account.Id ? fresh : null); // anche dentro TestingProvider, come nella versione di prova
            gmailMoved = now == fresh;
            await now.SendAsync(Mail("Inviato dal provider nuovo"));
            gmailAfter = smtp.Received.Count - before;
        }
        finally
        {
            GmailProvider.TestPort = null;
            GmailProvider.TestSmtpPort = null;
            GmailProvider.TestSmtpCertificate = null;
        }
        Check(imapList == "rifiutato" && imapDraft == "rifiutato" && imapSend == "rifiutato" && imapArrived == 0 && imapCopies == 0
              && gmailList == "rifiutato" && gmailDraft == "rifiutato" && gmailSend == "rifiutato" && gmailArrived == 0,
              $"X-1, un provider chiuso rifiuta ogni richiesta dicendo perché (IMAP: elenco {imapList}, bozza {imapDraft}, invio {imapSend}, {imapArrived} messaggi arrivati al server SMTP e {imapCopies} copie in Posta inviata; " +
              $"Gmail: elenco {gmailList}, bozza {gmailDraft}, invio {gmailSend}, {gmailArrived} arrivati); prima l'invio da un provider IMAP chiuso partiva senza errori e senza copia");
        Check(untouched && moved && stays && gmailMoved && afterArrived == 1 && afterCopies == 1 && gmailAfter == 1,
              $"X-1, la finestra di scrittura passa al provider nuovo dello stesso account solo quando il suo è chiuso (ComposeWindow.Current): da lì l'invio parte ({afterArrived} arrivato al server SMTP) con la copia in Posta inviata ({afterCopies}); " +
              "se l'account non c'è più resta quello chiuso, che rifiuta; un provider aperto non si cambia mai");
        if (BehSource("UI", "ComposeWindow.xaml.cs") is { } compose)
            Check(compose.Contains("IMailProvider? FromProvider => (FromBox.SelectedItem as Sender)?.Provider is { } chosen ? Current(chosen) : null;")
                  && RobPiece(compose, "async Task DeleteDraftAsync(IMailProvider provider, SavedDraft draft, string why)", "bool CanChangeFrom") is { } delete && delete.Contains("provider = Current(provider);")
                  && RobPiece(compose, "void MoveDraftTo(IMailProvider provider)", "async Task DeleteDraftAsync(") is { } move && move.Contains("old.Account.Id == provider.Account.Id"),
                  "X-1, nella finestra di scrittura il provider del Da e quello della bozza passano da Current, e la bozza si confronta per account, non per oggetto (letto dal sorgente)");
    }

    /// <summary>
    /// C-37, i due avvisi di nullità di Gmail che erano difetti veri: MailKit restituisce null quando, creata una cartella,
    /// il server non la elenca. Con il server finto che risponde OK a CREATE senza crearla: una nuova etichetta (categoria)
    /// e una nuova cartella escono con un errore che lo dice, e nessun colore resta salvato per un'etichetta che non c'è.
    /// Prima: l'errore di .NET per un riferimento nullo.
    /// </summary>
    static async Task RobGmailCreateAsync()
    {
        using var fake = new FakeImap { ForgetCreated = true };
        GmailProvider.TestPort = fake.Port;
        var account = new MailAccount { Kind = AccountKind.Gmail, Email = "prova@example.com", Id = "gmail-c37" };
        string category = "nessun errore", folder = "nessun errore";
        bool colored;
        try
        {
            using var gmail = new GmailProvider(account);
            try { await gmail.CreateCategoryAsync("Etichetta di prova", 3); }
            catch (Exception ex) { category = $"{ex.GetType().Name}: {ex.Message}"; }
            try { await gmail.CreateFolderAsync("Cartella di prova", null); }
            catch (Exception ex) { folder = $"{ex.GetType().Name}: {ex.Message}"; }
            colored = Store.Settings.CategoryColors.Keys.Any(k => k.StartsWith(account.Id + "|", StringComparison.Ordinal));
        }
        finally
        {
            GmailProvider.TestPort = null;
            foreach (var key in Store.Settings.CategoryColors.Keys.Where(k => k.StartsWith(account.Id + "|", StringComparison.Ordinal)).ToList()) Store.Settings.CategoryColors.Remove(key);
        }
        int created;
        lock (fake.Commands) created = fake.Commands.Count(c => c.StartsWith("CREATE"));
        Check(category == "MailException: Gmail non ha creato l'etichetta Etichetta di prova." && folder == "MailException: Gmail non ha creato la cartella Cartella di prova." && !colored && created == 2,
              $"C-37, Gmail: un'etichetta e una cartella che il server dice di aver creato ma poi non elenca ({created} CREATE) escono con un errore che lo dice, senza colore salvato " +
              $"(categoria: {category}; cartella: {folder}); prima un errore di .NET per un riferimento nullo");
    }

    /// <summary>
    /// C-24, misura: i Task falliti che nessuno aspettava in tutto questo processo (nella suite intera, tutte le prove di
    /// prima), raccolti qui a forza. Dice quali lavori in sottofondo falliscono davvero in silenzio negli scenari delle prove.
    /// </summary>
    static void RobUnobservedMeasure()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        List<string> seen;
        lock (Log.UnobservedSeen) seen = Log.UnobservedSeen.Where(s => !s.Contains(nameof(RobDiscardFailing))).ToList();
        var groups = seen.GroupBy(s => s).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}").ToList();
        Report.Add($"      (C-24, misura: {seen.Count} Task falliti che nessuno aspettava in questo processo fino a qui, oltre a quello di prova" +
                   (groups.Count == 0 ? "" : ": " + string.Join("; ", groups.Take(12)) + (groups.Count > 12 ? $"; e altri {groups.Count - 12} tipi" : "")) + ")");
    }

    /// <summary>
    /// C-24, solo a mano ("--prova-audit risultato.txt caduta", mai dalla suite): un errore su un altro thread, che chiude
    /// il processo. Il file del risultato non si scrive: si guarda il registro nella cartella di MISSIVA_DATA, che deve
    /// avere la riga "errore non gestito fuori dall'interfaccia, Missiva si chiude".
    /// </summary>
    static void RobCrash()
    {
        var thread = new Thread(() => throw new InvalidOperationException("errore di prova C-24 su un altro thread"));
        thread.Start();
        thread.Join();
    }
}
#endif
