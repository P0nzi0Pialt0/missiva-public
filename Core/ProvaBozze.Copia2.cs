#if DEBUG
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Missiva;

/// <summary>
/// Audit dell'11 ottobre, sicurezza, primo gruppo (docs\audit\ESITO.md): la copia di sicurezza costruita apposta da chi ne
/// conosce la password (T-14..T-22). Come BackupAuditTests: cartelle temporanee sue, mai la cartella dei dati della suite né
/// quella vera, solo dati finti, e Store.TestEntropy per fare da altro PC. Le copie ostili si scrivono a mano: il JSON del
/// contenuto, compresso e sigillato con BackupFile.Seal (un file .missiva-copia) o cifrato con DPAPI (un importazione.dat).
/// Ogni cartella "vittima" sta sotto la stessa cartella temporanea della cartella dei dati di prova: gli identificativi
/// ostili sono scritti in modo da non poterne uscire. Con "Missiva.exe --prova-audit risultato.txt copia2" solo queste.
/// </summary>
static partial class ProvaBozze
{
    const string Copy2Password = "password-della-copia-di-prova";

    static byte[] Copy2Gzip(string json)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(Encoding.UTF8.GetBytes(json));
        return packed.ToArray();
    }

    /// <summary>Il contenuto di una copia con queste impostazioni, come JSON da cambiare a mano prima di sigillarlo.</summary>
    static JsonObject Copy2Content(Settings settings, Action<JsonObject>? change = null)
    {
        var node = JsonNode.Parse(Backup.PortableJson(settings))!.AsObject();
        change?.Invoke(node);
        return new JsonObject { ["Format"] = 1, ["Created"] = "2026-10-10T08:00:00Z", ["App"] = "1.0.0", ["Settings"] = node.ToJsonString(), ["ImapPasswords"] = new JsonObject() };
    }

    /// <summary>Come va Backup.Read sul file .missiva-copia con questo contenuto: "letta", il problema detto, o l'eccezione sfuggita.</summary>
    static string Copy2Read(JsonObject content, out BackupData? data, out string message)
    {
        (data, message) = (null, "");
        try
        {
            data = Backup.Read(BackupFile.Seal(Copy2Gzip(content.ToJsonString()), Copy2Password), Copy2Password);
            return "letta";
        }
        catch (BackupException ex)
        {
            message = ex.Message;
            return ex.Problem.ToString();
        }
        catch (Exception ex)
        {
            return "ECCEZIONE " + ex.GetType().Name;
        }
    }

    /// <summary>Un importazione.dat scritto a mano con questo contenuto, nella cartella.</summary>
    static void Copy2Pending(string dir, JsonObject content)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, Backup.PendingName), Store.ProtectBytes(Copy2Gzip(content.ToJsonString())));
    }

    /// <summary>Come va l'avvio con l'importazione in attesa: l'esito, o l'eccezione che chiuderebbe Missiva.</summary>
    static string Copy2Apply(string dir, out BackupOutcome? outcome)
    {
        outcome = null;
        try
        {
            outcome = Backup.ApplyPending(dir);
            return outcome == null ? "nulla in attesa" : outcome.Applied ? "applicata" : "non applicata";
        }
        catch (Exception ex)
        {
            return "ECCEZIONE " + ex.GetType().Name;
        }
    }

    static MailAccount Copy2Imap(string id, string host = "127.0.0.1", int port = 993, string email = "mario@studio-prova.example") => new()
    {
        Id = id, Kind = AccountKind.Imap, Email = email, DisplayName = "Mario Rossi",
        Imap = new ImapServer { ImapHost = host, ImapPort = port, SmtpHost = host, SmtpPort = 465, UserName = email },
    };

    static void Backup2AuditTests()
    {
        Report.Add("--- Audit dell'11 ottobre, sicurezza, primo gruppo: copia di sicurezza costruita apposta (T-14..T-22)");
        var root = Path.Combine(Path.GetTempPath(), "missiva-prova-copia2-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            Copy2HostileIds(root);
            Copy2Access(root);
            Copy2Structure(root);
            Copy2Confirm(root);
            Copy2Portable(root);
            Copy2Domains(root);
            Copy2Limits(root);
            Copy2Templates(root);
            Copy2Aside(root);
        }
        finally
        {
            Store.TestEntropy = null;
            ProtectedTextConverter.TestForget();
            Store.TestDataRoot = null;
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// T-14: un identificativo di account scritto in una copia (o in impostazioni.json) come pezzo di percorso. Prima
    /// MailCache.Delete cancellava la cartella vittima fuori dai dati, e calendario e contatti scrivevano file dove voleva chi
    /// aveva preparato la copia.
    /// </summary>
    static void Copy2HostileIds(string root)
    {
        // ---------------------------------------------------------------- la regola
        string[] good = [Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("D"), "imap-prova", "A_b-9", new string('a', 64)];
        string?[] bad = [null, "", new string('a', 65), "..", ".", "a.b", "a/b", @"a\b", @"..\x", @"..\..\..\..\Documents", "C:", @"C:\x", @"\\server-prova\dati", "a|b", "a b", " a", "a\n", "a:b", "a*", "a?",
                         "con", "NUL", "prn", "Aux", "com1", "LPT9", "caffè", "a\u202Eb", "a\0b", "%TEMP%"];
        Check(good.All(MailAccount.ValidId) && !bad.Any(MailAccount.ValidId),
              $"identificativo di un account: valgono lettere e cifre ASCII, trattino e trattino basso fino a 64 caratteri (i {good.Length} di prova, fra cui quelli che Missiva genera); " +
              $"non valgono vuoto, punti, barre, due punti, \"|\", spazi, caratteri non ASCII o di controllo, nomi di periferica ({bad.Length} casi" +
              $"{(bad.Where(MailAccount.ValidId).ToList() is { Count: > 0 } passed ? "; PASSATI: " + string.Join(" ", passed) : "")})");

        // ---------------------------------------------------------------- la copia di sicurezza
        string[] hostile = [@"..\x", @"C:\x", "", @"..\..\..\..\Documents", "..", "a/b", "nul"];
        var read = hostile.Take(3).Select(id => (id, how: Copy2Read(Copy2Content(new Settings { Accounts = [Copy2Imap(id)] }), out _, out var message), message)).ToList();
        var pendingRoot = Path.Combine(root, "t14-attesa");
        var applied = hostile.Select((id, i) =>
        {
            var dir = Path.Combine(pendingRoot, i.ToString());
            Copy2Pending(dir, Copy2Content(new Settings { Accounts = [Copy2Imap(id)] }));
            return (id, known: Backup.Pending(dir) != null, how: Copy2Apply(dir, out var outcome), outcome, written: File.Exists(Path.Combine(dir, Store.FileName)),
                    aside: Directory.GetFiles(dir, "importazione-illeggibile-*").Length, left: Backup.HasPending(dir));
        }).ToList();
        string twiceHow = Copy2Read(Copy2Content(new Settings { Accounts = [Copy2Imap("doppio-prova"), Copy2Imap("DOPPIO-prova", email: "maria@studio-prova.example")] }), out _, out var twiceMessage);
        string fineHow = Copy2Read(Copy2Content(new Settings { Accounts = [Copy2Imap(good[0]), Copy2Imap("imap-prova", email: "maria@studio-prova.example")] }), out var fine, out _);
        Check(read.All(r => r.how == "Damaged" && r.message.Contains("identificativo") && r.message.Contains("non lo importa"))
              && applied.All(a => !a.known && a.how == "non applicata" && !a.written && a.aside == 1 && !a.left && a.outcome!.Body.Contains("Non è cambiato nulla"))
              && twiceHow == "Damaged" && twiceMessage.Contains("lo stesso identificativo") && fineHow == "letta" && Backup.Describe(fine!).Accounts.Count == 2,
              $"copia di sicurezza con un identificativo di account che è un percorso (\"..\\x\", \"C:\\x\", vuoto): Backup.Read la rifiuta e dice perché ({string.Join(", ", read.Select(r => r.how))}: \"{read[0].message}\"); " +
              $"lo stesso con due account dallo stesso identificativo ({twiceHow}); un importazione.dat scritto a mano con {hostile.Length} identificativi ostili non si applica, va da parte e non cambia nulla " +
              $"({string.Join(", ", applied.Select(a => a.how).Distinct())}); una copia con identificativi normali si legge ({fineHow})");

        // ---------------------------------------------------------------- impostazioni.json cambiato a mano
        string Hand(string name, Settings broken, Settings? backup)
        {
            var dir = Path.Combine(root, "t14-" + name);
            if (backup != null)
            {
                Store.TryWrite(dir, backup, null, out _);
                Store.TryWrite(dir, backup, null, out _); // due salvataggi: c'è impostazioni.bak
            }
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, Store.FileName), Store.Snapshot(broken, out _)!);
            return dir;
        }
        var saved = new Settings { Accounts = [Copy2Imap("imap-prova")] };
        var withBackup = Hand("a-mano", new Settings { Accounts = [Copy2Imap(@"..\..\vittima")] }, saved);
        var fromBackup = Store.Read(withBackup, out var recovered);
        var alone = Hand("a-mano-solo", new Settings { Accounts = [Copy2Imap(@"C:\Users\Public\Documents")] }, null);
        var fromNothing = Store.Read(alone, out var lost);
        var doubled = Hand("a-mano-doppio", new Settings { Accounts = [Copy2Imap("doppio-prova"), Copy2Imap("doppio-prova")] }, null);
        Store.Read(doubled, out var doubledRecovery);
        var normal = Hand("a-mano-normale", saved, null);
        var asWritten = Store.Read(normal, out var noRecovery);
        Check(fromBackup.Accounts is [{ Id: "imap-prova" }] && recovered is { BackupDate: not null, Blocked: false, BrokenFile: not null } && recovered.Why.Contains("identificativo") && File.Exists(recovered.BrokenFile)
              && fromNothing.Accounts.Count == 0 && lost is { BackupDate: null, BrokenFile: not null } && File.ReadAllText(lost.BrokenFile).Contains("Public")
              && doubledRecovery != null && doubledRecovery.Why.Contains("lo stesso identificativo")
              && asWritten.Accounts is [{ Id: "imap-prova" }] && noRecovery == null,
              $"impostazioni.json cambiato a mano con un identificativo che è un percorso: non entra, come un file rovinato (\"{recovered?.Why}\"): va da parte con la data nel nome e si riparte dalla copia di riserva, " +
              "o senza account se non c'è; lo stesso con due account dallo stesso identificativo; un file normale si legge come prima");

        // ---------------------------------------------------------------- i percorsi della copia locale
        // La cartella dei dati di prova e le "vittime" accanto, tutte sotto root: ogni identificativo ostile, risolto come
        // faceva il codice di prima, cade lì dentro (controllato prima di usarlo).
        var data = Path.Combine(root, "t14-dati");
        var cache = Path.Combine(data, "cache");
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(data, "impostazioni-di-prova.txt"), "x");
        string Victim(string name)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(Path.Combine(dir, "corpi"));
            File.WriteAllText(Path.Combine(dir, "documento-prova.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "corpi", "relazione-prova.bin"), "x");
            File.SetLastWriteTimeUtc(Path.Combine(dir, "corpi", "relazione-prova.bin"), DateTime.UtcNow.AddDays(-40)); // la pulizia dei corpi toglie i file più vecchi di 30 giorni
            return dir;
        }
        string relative = Victim("vittima"), absolute = Victim("vittima-assoluta"), deep = Victim("vittima-in-fondo");
        // Come li risolveva il codice di prima: come cartella (cache\identificativo: elenchi e corpi) o come pezzo di un nome
        // di file (cache\calendario-identificativo.json, cache\contatti-identificativo.dat). Ognuno si usa solo nei modi in cui
        // resta sotto root: l'ultimo, come cartella, salirebbe più su, e come cartella non si prova.
        bool UnderRoot(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception) { return true; } // un percorso che Windows non risolve non porta da nessuna parte
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        string[] ids = [@"..\..\vittima", absolute, "..", ".", "", @"..\..\vittima\", @"..\..\..\..\vittima-in-fondo\x"];
        bool aimed = Path.GetFullPath(Path.Combine(cache, ids[0])) == relative && Path.GetFullPath(Path.Combine(cache, ids[1])) == absolute && Path.GetFullPath(Path.Combine(cache, ids[2])) == data
                     && Path.GetFullPath(Path.Combine(cache, $"calendario-{ids[6]}.json")) == Path.Combine(deep, "x.json");
        var trouble = new List<string>();
        int asFolders = 0, asFiles = 0, kept = 0, removed = 0;
        var bodyRoot = BodyCache.TestRoot;
        BodyCache.TestRoot = null;
        Store.TestDataRoot = data;
        try
        {
            foreach (var id in aimed ? ids : [])
            {
                bool asFolder = UnderRoot(Path.Combine(cache, id)) && UnderRoot(Path.Combine(cache, id, "corpi")),
                     asFile = UnderRoot(Path.Combine(cache, $"calendario-{id}.json")) && UnderRoot(Path.Combine(cache, $"contatti-{id}.dat"));
                var account = new MailAccount { Id = id, Kind = AccountKind.Imap, Email = "mario@studio-prova.example" };
                var message = new MessageSummary { Id = "1", FolderId = "INBOX", Subject = "Oggetto di prova" };
                try
                {
                    bool works = asFolder || asFile;
                    if (asFolder)
                    {
                        asFolders++;
                        MailCache.SaveFolders(account, [new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox }]);
                        MailCache.SaveMessages(account, "INBOX", null, [message]);
                        BodyCache.Save(account, message, new MessageBody { Html = "<p>testo di prova</p>" });
                        Task.WhenAll(BodyCache.TestWrites.ToArray()).GetAwaiter().GetResult();
                        BodyCache.Trim(account); // quella che l'avvio fa per ogni account: toglie i .bin più vecchi di 30 giorni
                        // Finché l'account c'è la sua copia funziona, in una cartella dal nome innocuo dentro cache.
                        works &= MailCache.LoadFolders(account) is [{ Id: "INBOX" }] && MailCache.LoadMessages(account, "INBOX", null) is [{ Id: "1" }] && BodyCache.Load(account, message) != null
                                 && Directory.GetDirectories(cache, "id-*").Length == 1;
                    }
                    if (asFile)
                    {
                        asFiles++;
                        Calendars.Save(account, new CalendarData());
                        ContactBook.SetRemote(id, [new Contact { DisplayName = "Maria Rossi" }]);
                        for (int i = 0; i < 100 && Directory.GetFiles(cache, "contatti-*.dat").Length == 0; i++) Thread.Sleep(20);
                        works &= Calendars.Load(account) != null && Directory.GetFiles(cache, "calendario-id-*.json").Length == 1 && Directory.GetFiles(cache, "contatti-id-*.dat").Length == 1;
                    }
                    if (works) kept++;
                    // Rimuovi account, come MainWindow.RemoveAccountAsync.
                    if (asFolder) MailCache.Delete(account);
                    if (asFile)
                    {
                        Calendars.Delete(account);
                        ContactBook.ForgetRemote(id);
                    }
                    if (Directory.GetFileSystemEntries(cache).Length == 0) removed++;
                    else trouble.Add($"\"{id}\": in cache restano {string.Join(", ", Directory.GetFileSystemEntries(cache).Select(Path.GetFileName))}");
                }
                catch (Exception ex)
                {
                    trouble.Add($"\"{id}\": {ex.GetType().Name}");
                }
            }
        }
        finally
        {
            Store.TestDataRoot = null;
            BodyCache.TestRoot = bodyRoot;
            ContactBook.Reset();
        }
        // Che cosa c'è sotto root fuori dalla cartella dei dati di prova: solo le vittime, com'erano, e le cartelle delle altre prove.
        bool Intact(string dir) => Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f)).Order(StringComparer.Ordinal).SequenceEqual(new[] { @"corpi\relazione-prova.bin", "documento-prova.txt" });
        var strangers = Directory.GetFileSystemEntries(root).Select(n => Path.GetFileName(n)!).Where(n => n is not ("vittima" or "vittima-assoluta" or "vittima-in-fondo") && !(n.Length > 4 && n[0] == 't' && n[3] == '-')).ToList();
        Check(aimed && trouble.Count == 0 && asFolders == 6 && asFiles == ids.Length && kept == ids.Length && removed == ids.Length && Intact(relative) && Intact(absolute) && Intact(deep)
              && strangers.Count == 0 && Directory.GetFileSystemEntries(data).Select(Path.GetFileName).Order().SequenceEqual(["cache", "impostazioni-di-prova.txt"]),
              $"account con un identificativo che è un percorso ({ids.Length} casi: verso una cartella accanto ai dati, un percorso intero, \"..\", \".\", vuoto): elenchi e corpi ({asFolders} casi), calendario e contatti " +
              $"({asFiles} casi) si scrivono e si rileggono dentro cache, sotto un nome fatto dall'impronta dell'identificativo ({kept} su {ids.Length}); Rimuovi account e la pulizia dei corpi toccano solo quelli " +
              $"({removed} su {ids.Length}); le tre cartelle vittima fuori dai dati restano con i loro file, e la cartella dei dati pure{(trouble.Count > 0 ? "; NON VA: " + string.Join("; ", trouble) : "")}" +
              $"{(strangers.Count == 0 ? "" : "; sotto la cartella di prova è comparso: " + string.Join(", ", strangers))}");

        // Un account normale: i nomi sul disco sono quelli di sempre (la copia locale di chi aggiorna Missiva resta dov'è).
        Store.TestDataRoot = data;
        bool names, gone;
        try
        {
            var account = Copy2Imap("0123456789abcdef0123456789abcdef");
            MailCache.SaveFolders(account, [new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox }]);
            Calendars.Save(account, new CalendarData());
            ContactBook.SetRemote(account.Id, [new Contact { DisplayName = "Maria Rossi" }]);
            for (int i = 0; i < 100 && !File.Exists(Path.Combine(cache, $"contatti-{account.Id}.dat")); i++) Thread.Sleep(20);
            BodyCache.Save(account, new MessageSummary { Id = "1", FolderId = "INBOX" }, new MessageBody { Html = "<p>testo di prova</p>" });
            Task.WhenAll(BodyCache.TestWrites.ToArray()).GetAwaiter().GetResult();
            names = File.Exists(Path.Combine(cache, account.Id, "cartelle.json")) && File.Exists(Path.Combine(cache, $"calendario-{account.Id}.json")) && File.Exists(Path.Combine(cache, $"contatti-{account.Id}.dat"))
                    && Directory.GetFiles(Path.Combine(cache, account.Id, "corpi"), "*.bin", SearchOption.AllDirectories).Length == 1;
            MailCache.Delete(account);
            Calendars.Delete(account);
            ContactBook.ForgetRemote(account.Id);
            gone = Directory.GetFileSystemEntries(cache).Length == 0;
        }
        finally
        {
            Store.TestDataRoot = null;
            ContactBook.Reset();
        }
        Check(names && gone, "account con un identificativo normale: cartella, calendario, contatti e corpi hanno sul disco i nomi di sempre (cache\\identificativo), e Rimuovi account li toglie");

        // ---------------------------------------------------------------- chi cancella una cartella intera
        var inside = Path.Combine(root, "t14-dati", "cache");
        Check(Store.IsInside(Path.Combine(inside, "abc"), inside) && Store.IsInside(Path.Combine(inside.ToUpperInvariant(), "abc", "corpi"), inside) && Store.IsInside(inside + "/abc", inside + @"\")
              && !Store.IsInside(inside, inside) && !Store.IsInside(inside + @"\", inside) && !Store.IsInside(Path.Combine(inside, ".."), inside) && !Store.IsInside(Path.Combine(inside, @"..\..\vittima"), inside)
              && !Store.IsInside(Path.Combine(inside, @"abc\..\..\cache-accanto"), inside) && !Store.IsInside(inside + "-accanto", inside) && !Store.IsInside(relative, inside)
              && !Store.IsInside("", inside) && !Store.IsInside(@"\\server-prova\dati\x", inside) && !Store.IsInside("abc", inside),
              "prima di cancellare una cartella intera (MailCache.Delete, BodyCache.Delete, Svuota) il percorso risolto deve stare dentro cache: dentro sì, anche con maiuscole diverse; " +
              "la cartella stessa, \"..\", una cartella dal nome che comincia uguale, un percorso di rete e un percorso vuoto no");
    }

    /// <summary>
    /// T-15: gli accessi che questo PC ha già. Prima bastavano lo stesso identificativo e lo stesso indirizzo: una copia senza
    /// password con il server cambiato riceveva la password IMAP di questo PC, e al primo collegamento la mandava lì.
    /// </summary>
    static void Copy2Access(string root)
    {
        using var mine = new FakeImap(new Flavor(Gmail: false));
        using var theirs = new FakeImap(new Flavor(Gmail: false));
        const string secret = "password-di-questo-pc", copied = "password-scritta-nella-copia";
        (BackupOutcome? Outcome, MailAccount Account) Import(string name, Action<ImapServer> change, bool withPassword = false, Action<ImapServer>? before = null)
        {
            var dir = Path.Combine(root, "t15-" + name);
            var here = Copy2Imap("imap-prova", port: mine.Port);
            before?.Invoke(here.Imap!);
            here.Imap!.ProtectedPassword = Store.Protect(secret);
            Store.TryWrite(dir, new Settings { Accounts = [here] }, null, out _);
            var incoming = Copy2Imap("imap-prova", port: mine.Port);
            before?.Invoke(incoming.Imap!);
            change(incoming.Imap!);
            var content = Copy2Content(new Settings { Accounts = [incoming] });
            if (withPassword) content["ImapPasswords"] = new JsonObject { ["imap-prova"] = copied };
            Copy2Pending(dir, content);
            Copy2Apply(dir, out var outcome);
            return (outcome, Store.Read(dir, out _).Accounts[0]);
        }
        static string? Password(MailAccount account) => Store.TryUnprotect(account.Imap!.ProtectedPassword, out var password) ? password : null;

        // Il caso del rapporto: stessa casella, un altro server. La password di questo PC non lo segue.
        var moved = Import("altro-server", s => s.ImapPort = theirs.Port);
        ImapProvider.TestPlain = true;
        Exception? refused = null;
        try
        {
            using var provider = new ImapProvider(moved.Account);
            try { provider.GetFoldersAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { refused = ex; }
        }
        finally { ImapProvider.TestPlain = false; }
        Check(moved.Outcome is { Applied: true } && moved.Account.Imap is { ProtectedPassword: null } server && server.ImapPort == theirs.Port
              && moved.Outcome.NeedAccess is [{ Id: "imap-prova" }] && moved.Outcome.Body.Contains("mario@studio-prova.example chiede di nuovo l'accesso")
              && MailException.FixOf(refused) == AccountFix.Password && theirs.Commands.Count == 0 && theirs.Password == null && mine.Commands.Count == 0,
              "copia senza password con lo stesso identificativo e indirizzo ma un altro server IMAP: l'account arriva senza password e chiede Aggiorna password; il server scritto nella copia " +
              $"non riceve nulla ({theirs.Commands.Count} comandi, password ricevuta: {theirs.Password ?? "nessuna"}). Prima riceveva la password di questo PC");

        // Ogni cosa che cambia dove va la password la toglie; gli stessi server (anche scritti con altre maiuscole) la tengono.
        var changes = new (string Name, Action<ImapServer> Change)[]
        {
            ("host IMAP", s => s.ImapHost = "imap.altro-prova.example"), ("host SMTP", s => s.SmtpHost = "smtp.altro-prova.example"), ("porta SMTP", s => s.SmtpPort = 587),
            ("sicurezza IMAP", s => s.ImapSecurity = MailSecurity.StartTls), ("sicurezza SMTP", s => s.SmtpSecurity = MailSecurity.StartTls), ("utente", s => s.UserName = "altro@studio-prova.example"),
        };
        var dropped = changes.Select((c, i) => (c.Name, Result: Import("cambio-" + i, c.Change))).ToList();
        var same = Import("uguale", _ => { });
        var sameHost = Import("uguale-maiuscole", s => (s.ImapHost, s.SmtpHost) = ("IMAP.Studio-Prova.Example.", "smtp.STUDIO-prova.example"),
                              before: s => (s.ImapHost, s.SmtpHost) = ("imap.studio-prova.example", "smtp.studio-prova.example"));
        Check(dropped.All(d => d.Result.Outcome is { Applied: true, NeedAccess.Count: 1 } && d.Result.Account.Imap!.ProtectedPassword == null)
              && same.Outcome is { Applied: true, NeedAccess.Count: 0 } && Password(same.Account) == secret && sameHost.Outcome is { NeedAccess.Count: 0 } && Password(sameHost.Account) == secret,
              $"la password IMAP di questo PC resta solo se la copia ha gli stessi server, porte, sicurezza e utente, per IMAP e per SMTP: cambiando {string.Join(", ", dropped.Select(d => d.Name))} l'account chiede la password " +
              $"({dropped.Count(d => d.Result.Account.Imap!.ProtectedPassword == null)} su {dropped.Count}); con tutto uguale, anche scritto con altre maiuscole, resta e non chiede nulla");

        // Con la password nella copia vale quella, con il server della copia: di questo PC non parte nulla.
        var carried = Import("con-password", s => s.ImapPort = theirs.Port, withPassword: true);
        theirs.Commands.Clear();
        ImapProvider.TestPlain = true;
        bool connected;
        try
        {
            using var provider = new ImapProvider(carried.Account);
            try { connected = provider.GetFoldersAsync().GetAwaiter().GetResult().Any(f => f.Kind == FolderKind.Inbox); }
            catch (Exception) { connected = false; }
        }
        finally { ImapProvider.TestPlain = false; }
        Check(carried.Outcome is { Applied: true, NeedAccess.Count: 0 } && Password(carried.Account) == copied && connected && theirs.Password == copied && mine.Commands.Count == 0,
              "copia con la password IMAP e un altro server: vale la password della copia, e quel server riceve quella, non la password di questo PC");

        // Microsoft e Google: l'accesso di questo PC resta solo con la stessa app (e directory) e lo stesso client.
        const string home = "casa-prova.directory-prova", personalHome = "casa-personale-prova", token = "token-di-questo-pc";
        Settings Here() => new()
        {
            MicrosoftClientId = "app-di-questo-pc", GoogleClientId = "google-di-questo-pc.apps.example",
            Accounts =
            [
                new MailAccount { Id = "lavoro-prova", Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = "organizations", HomeAccountId = home },
                new MailAccount { Id = "personale-prova", Kind = AccountKind.Microsoft, Email = "mario.rossi@outlook-prova.example", HomeAccountId = personalHome },
                new MailAccount { Id = "gmail-prova", Kind = AccountKind.Gmail, Email = "maria.rossi@gmail-prova.example", ProtectedRefreshToken = Store.Protect(token) },
            ],
        };
        (BackupOutcome? Outcome, Settings After) Apps(string name, Action<Settings> change)
        {
            var dir = Path.Combine(root, "t15-app-" + name);
            Store.TryWrite(dir, Here(), null, out _);
            var incoming = Backup.ParseSettings(Backup.PortableJson(Here()));
            foreach (var a in incoming.Accounts) a.HomeAccountId = a.ProtectedRefreshToken = null; // come Collect: la copia non porta accessi
            change(incoming);
            Copy2Pending(dir, Copy2Content(incoming));
            Copy2Apply(dir, out var outcome);
            return (outcome, Store.Read(dir, out _));
        }
        static string Kept(Settings s) => string.Join(" ", s.Accounts.Select(a => a.HomeAccountId != null || Store.TryUnprotect(a.ProtectedRefreshToken, out _) ? "sì" : "no"));
        var sameApps = Apps("uguali", _ => { });
        var otherApp = Apps("app", s => s.Accounts[0].ClientId = "app-della-copia");
        var otherTenant = Apps("directory", s => s.Accounts[0].Tenant = "directory-della-copia.example");
        var otherPersonal = Apps("personale", s => s.MicrosoftClientId = "app-della-copia");
        var otherGoogle = Apps("google", s => s.GoogleClientId = "google-della-copia.apps.example");
        Check(Kept(sameApps.After) == "sì sì sì" && sameApps.Outcome is { NeedAccess.Count: 0 } && sameApps.After.Accounts[0].HomeAccountId == home
              && Kept(otherApp.After) == "no sì sì" && otherApp.Outcome!.NeedAccess is [{ Id: "lavoro-prova" }] && Kept(otherTenant.After) == "no sì sì"
              && Kept(otherPersonal.After) == "sì no sì" && otherPersonal.Outcome!.NeedAccess is [{ Id: "personale-prova" }]
              && Kept(otherGoogle.After) == "sì sì no" && otherGoogle.Outcome!.NeedAccess is [{ Id: "gmail-prova" }],
              "Microsoft e Google: con la stessa app, la stessa directory e lo stesso client l'accesso di questo PC resta (lavoro, personale, Gmail: " + Kept(sameApps.After) + "); con un'altra app o un'altra directory " +
              $"dell'account di lavoro ({Kept(otherApp.After)}; {Kept(otherTenant.After)}), un'altra app Microsoft nelle impostazioni ({Kept(otherPersonal.After)}) o un altro client Google ({Kept(otherGoogle.After)}) " +
              "quell'account arriva senza accesso ed è fra quelli che lo chiedono");

        // Accessi scritti nella copia a mano: non entrano mai, nemmeno un cifrato di questo PC messo nel campo di un altro account.
        var planted = Store.Protect(secret);
        var dirPlanted = Path.Combine(root, "t15-scritti");
        Copy2Pending(dirPlanted, Copy2Content(new Settings
        {
            ProtectedGoogleClientSecret = planted,
            Accounts =
            [
                new MailAccount { Id = "ms-nuovo", Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", HomeAccountId = "casa-scritta-nella-copia", ProtectedRefreshToken = planted,
                                  Imap = new ImapServer { ImapHost = "imap.altro-prova.example", ProtectedPassword = planted } },
                new MailAccount { Id = "gm-nuovo", Kind = AccountKind.Gmail, Email = "maria.rossi@gmail-prova.example", ProtectedRefreshToken = planted, HomeAccountId = "casa-scritta-nella-copia" },
                new MailAccount { Id = "imap-nuovo", Kind = AccountKind.Imap, Email = "mario@studio-prova.example", HomeAccountId = "casa-scritta-nella-copia", ProtectedRefreshToken = planted,
                                  Imap = new ImapServer { ImapHost = "imap.altro-prova.example", ProtectedPassword = planted } },
            ],
        }));
        Copy2Apply(dirPlanted, out var plantedOutcome);
        var afterPlanted = Store.Read(dirPlanted, out _);
        Check(plantedOutcome is { Applied: true, NeedAccess.Count: 3 } && afterPlanted.ProtectedGoogleClientSecret == null
              && afterPlanted.Accounts.All(a => a.HomeAccountId == null && a.ProtectedRefreshToken == null && a.Imap?.ProtectedPassword == null)
              && !File.ReadAllText(Path.Combine(dirPlanted, Store.FileName)).Contains(planted),
              "accessi scritti a mano nella copia (identificativo Microsoft, token e password già cifrati, anche con un cifrato di questo PC copiato nel campo di un altro account): non entrano, " +
              "i tre account arrivano senza accesso e nel file delle impostazioni quel cifrato non c'è");
    }

    /// <summary>Impostazioni con un po' di tutto, per le copie da cambiare a mano: tre account, firme, modelli, regole, contrassegni, posposti.</summary>
    static Settings Copy2Rich() => new()
    {
        MicrosoftClientId = "id-app-prova", GoogleClientId = "google-prova.apps.example",
        Accounts =
        [
            new MailAccount { Id = "ms-prova", Kind = AccountKind.Microsoft, Email = "mario.rossi@outlook-prova.example", DisplayName = "Mario Rossi", Tenant = "organizations",
                              Identities = [new SendIdentity { Id = "alias-prova", Name = "Studio Rossi", Email = "info@studio-prova.example" }] },
            new MailAccount { Id = "gm-prova", Kind = AccountKind.Gmail, Email = "maria.rossi@gmail-prova.example", DisplayName = "Maria Rossi", GoogleScopes = "https://mail.google.com/" },
            Copy2Imap("imap-prova", "imap.studio-prova.example"),
        ],
        Theme = AppTheme.Dark, NotificationsOff = ["gm-prova"], Focused = { ["ms-prova"] = false },
        Signatures = { ["imap-prova"] = new Signature { Rtf = @"{\rtf1 Mario Rossi}", More = [new NamedSignature { Id = "breve", Name = "Breve", Rtf = @"{\rtf1 MR}" }], ReplyId = "breve" } },
        Templates = [new MailTemplate { Id = "modello", Name = "Preventivo", Subject = "Preventivo del {data}", Rtf = @"{\rtf1 Buongiorno {nome}}" }],
        Rules = [new MailRule { Id = "regola", AccountId = "imap-prova", Name = "Fatture", FromAddresses = ["fatture@fornitore-prova.example"], SubjectContains = ["fattura"], MoveTo = "INBOX/Fatture", MoveToName = "Fatture",
                                MarkRead = true, Categories = ["Clienti"] }],
        RulesSeen = ["impronta-prova"], RecentSearches = ["da:\"Maria Rossi\""], ImageSenders = ["fornitore-prova.example"],
        CategoryColors = { ["gm-prova|Clienti"] = 7 }, LocalCategories = { ["ms-prova"] = ["Urgente"] },
        Favorites = ["imap-prova|INBOX/Fatture"], FolderOrder = { ["imap-prova"] = ["INBOX", "INBOX/Fatture"] }, FolderColors = { ["imap-prova|INBOX/Fatture"] = 3 },
        HiddenFolders = ["gm-prova|Promozioni"], Pinned = { ["imap-prova"] = ["7"] },
        Flags = [new FlagDate { AccountId = "imap-prova", Key = "chiave", MessageId = "7", FolderId = "INBOX", Due = new DateTime(2026, 10, 20), Subject = "Contrassegno di prova", From = "Maria Rossi" }],
        Snoozed = [new SnoozedMessage { AccountId = "gm-prova", Key = "posposto", FolderId = "INBOX", Until = new DateTime(2026, 10, 25, 8, 0, 0), Subject = "Posposto di prova", From = "Mario Rossi" }],
        Woken = { ["gm-prova|sveglio"] = new DateTime(2026, 10, 3, 8, 0, 0) }, MessageMenuOrder = ["reply", "-", "delete"], HiddenCalendars = ["ms-prova|calendario-prova"],
    };

    static readonly Dictionary<Type, (System.Reflection.PropertyInfo Property, bool NotNull)[]> Copy2Members = new();

    /// <summary>I punti di un oggetto dove c'è null e il tipo dice di no, e le voci null degli elenchi: quello che dopo un'importazione non deve esserci.</summary>
    static List<string> Copy2Nulls(object root)
    {
        var found = new List<string>();
        // Le proprietà da guardare di ogni tipo, e se il tipo dice che non sono mai null: lette una volta (la riflessione costa, e le varianti sono centinaia).
        (System.Reflection.PropertyInfo Property, bool NotNull)[] Members(Type type)
        {
            if (Copy2Members.TryGetValue(type, out var known)) return known;
            var nullability = new System.Reflection.NullabilityInfoContext();
            return Copy2Members[type] = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanWrite && !p.IsDefined(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false))
                .Select(p => (p, !p.PropertyType.IsValueType && nullability.Create(p).ReadState == System.Reflection.NullabilityState.NotNull)).ToArray();
        }
        void Walk(object value, string path, int depth)
        {
            var type = value.GetType();
            if (depth > 8 || value is string || type.IsPrimitive || type.IsEnum || value is DateTime) return;
            if (value is System.Collections.IDictionary map)
            {
                foreach (System.Collections.DictionaryEntry entry in map)
                    if (entry.Value == null) found.Add($"{path}[{entry.Key}]");
                    else Walk(entry.Value, $"{path}[{entry.Key}]", depth + 1);
                return;
            }
            if (value is System.Collections.IEnumerable list)
            {
                int i = 0;
                foreach (var item in list)
                {
                    if (item == null) found.Add($"{path}[{i}]");
                    else Walk(item, $"{path}[{i}]", depth + 1);
                    i++;
                }
                return;
            }
            foreach (var (property, notNull) in Members(type))
            {
                var inner = property.GetValue(value);
                if (inner != null) Walk(inner, path + "." + property.Name, depth + 1);
                else if (notNull) found.Add(path + "." + property.Name);
            }
        }
        Walk(root, "", 0);
        return found;
    }

    /// <summary>
    /// T-16: una copia con un null dove Missiva non lo scrive. Prima ApplyPending sollevava un'eccezione all'avvio con il file
    /// in attesa ancora lì (Missiva non partiva più), oppure importava impostazioni che l'avvio dopo non riusciva a leggere.
    /// </summary>
    static void Copy2Structure(string root)
    {
        const string contactsJson = "[{\"Id\":\"contatto\",\"DisplayName\":\"Maria Rossi\",\"Emails\":[\"maria.rossi@clienti-prova.example\"],\"Mobile\":\"333 0000000\"}," +
                                    "{\"Id\":\"gruppo\",\"IsGroup\":true,\"DisplayName\":\"Gruppo di prova\",\"Members\":[\"maria.rossi@clienti-prova.example\"]}]";
        const string bookJson = "{\"Entries\":[{\"Name\":\"Mario Rossi\",\"Email\":\"destinatario-prova@clienti-prova.example\",\"Received\":2,\"Sent\":5,\"LastReceived\":\"2026-10-01T10:00:00\",\"LastSent\":\"2026-10-02T10:00:00\"}]," +
                                "\"SentScans\":{\"imap-prova\":\"2026-10-02T10:00:00Z\"}}";
        var old = new Settings { Accounts = [Copy2Imap("di-prima", email: "prima@studio-prova.example")], Theme = AppTheme.Light };
        var before = Store.Snapshot(old, out _)!;
        JsonObject Rich(Action<JsonObject>? settings = null, Action<JsonObject>? content = null, bool lean = false)
        {
            var made = Copy2Content(Copy2Rich(), settings);
            made["ImapPasswords"] = new JsonObject { ["imap-prova"] = "password-nella-copia" };
            if (!lean) // senza contatti e rubrica un'importazione scrive un file solo: per le centinaia di varianti delle sole impostazioni
            {
                made["Contacts"] = contactsJson;
                made["AddressBook"] = bookJson;
            }
            content?.Invoke(made);
            return made;
        }
        int folders = 0;
        // Un avvio con questo contenuto in attesa, sopra impostazioni che ci sono già. Che cosa non deve mai succedere:
        // un'eccezione, il file in attesa ancora lì, impostazioni che dopo non si leggono o con un null dove non va.
        (string How, string? Wrong) Start(JsonObject content, bool describe = false)
        {
            var dir = Path.Combine(root, "t16-" + ++folders);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, Store.FileName), before);
            Copy2Pending(dir, content);
            try
            {
                var known = Backup.Pending(dir);
                if (known != null && describe) UI.BackupUi.ConfirmText(Backup.Describe(known), known.App, hasData: true);
                var how = Copy2Apply(dir, out var outcome);
                if (how.StartsWith("ECCEZIONE")) return (how, how);
                if (Backup.HasPending(dir)) return (how, "il file in attesa è rimasto");
                if (Directory.GetFiles(dir, "*.importato", SearchOption.AllDirectories).Length > 0) return (how, "un file .importato è rimasto");
                if (known == null != (how == "non applicata")) return (how, "la pagina delle Impostazioni e l'avvio non dicono la stessa cosa");
                if (how == "non applicata")
                    return (how, !File.ReadAllBytes(Path.Combine(dir, Store.FileName)).SequenceEqual(before) ? "le impostazioni di prima sono cambiate"
                        : Directory.GetFiles(dir, "*-prima-dell-importazione-*").Length > 0 ? "qualcosa è andato da parte"
                        : Directory.GetFiles(dir, "importazione-illeggibile-*").Length != 1 ? "il file in attesa non è da parte" : null);
                // Applicata: quello che l'avvio fa subito dopo (Store.Load), e nessun null dove il tipo dice di no.
                var after = Store.Read(dir, out var recovery);
                after.UpgradeImageSenders();
                if (recovery != null) return (how, "le impostazioni importate non si leggono: " + recovery.Why);
                if (Copy2Nulls(after) is { Count: > 0 } nulls) return (how, "null in " + string.Join(", ", nulls.Take(3)));
                var contacts = Path.Combine(dir, "contatti.dat");
                if (File.Exists(contacts) && System.Text.Json.JsonSerializer.Deserialize(Store.UnprotectBytes(File.ReadAllBytes(contacts)), ContactsJson.Default.ListContact) is { } people
                    && Copy2Nulls(people) is { Count: > 0 } missing) return (how, "null nei contatti: " + string.Join(", ", missing.Take(3)));
                return (how, null);
            }
            catch (Exception ex)
            {
                return ("ECCEZIONE " + ex.GetType().Name, "ECCEZIONE " + ex.GetType().Name + " dopo l'importazione");
            }
        }

        // ---------------------------------------------------------------- i casi del rapporto, e quelli trovati provando
        var cases = new (string Name, string Expected, JsonObject Content)[]
        {
            ("copia com'è", "applicata", Rich()),
            ("password IMAP null", "non applicata", Rich(content: c => c["ImapPasswords"] = new JsonObject { ["imap-prova"] = null })),
            ("password di un account che non c'è", "non applicata", Rich(content: c => c["ImapPasswords"] = new JsonObject { ["altro-prova"] = "x" })),
            ("password di un account senza server IMAP", "non applicata", Rich(content: c => c["ImapPasswords"] = new JsonObject { ["gm-prova"] = "x" })),
            ("elenco delle password null", "applicata", Rich(content: c => c["ImapPasswords"] = null)),
            ("account con Id null", "non applicata", Rich(s => s["Accounts"]![2]!["Id"] = null)),
            ("account senza Id", "non applicata", Rich(s => s["Accounts"]![2]!.AsObject().Remove("Id"))),
            ("account senza indirizzo", "non applicata", Rich(s => s["Accounts"]![2]!["Email"] = "")),
            ("account null nell'elenco", "non applicata", Rich(s => s["Accounts"]!.AsArray().Add(null))),
            ("elenco degli account null", "applicata", Rich(s => s["Accounts"] = null, c => c["ImapPasswords"] = new JsonObject())),
            ("impostazioni null", "non applicata", Rich(content: c => c["Settings"] = null)),
            ("impostazioni che non sono un oggetto", "non applicata", Rich(content: c => c["Settings"] = "[]")),
            ("impostazioni che non sono JSON", "non applicata", Rich(content: c => c["Settings"] = "{ non è JSON")),
            ("versione di Missiva null", "applicata", Rich(content: c => c["App"] = null)),
            ("mittenti delle immagini null", "applicata", Rich(s => s["ImageSenders"] = null)),
            ("regola null nell'elenco", "non applicata", Rich(s => s["Rules"]!.AsArray().Add(null))),
            ("firma null", "applicata", Rich(s => s["Signatures"]!["imap-prova"] = null)),
            ("altre firme null", "applicata", Rich(s => s["Signatures"]!["imap-prova"]!["More"] = null)),
            ("parole di una regola null", "applicata", Rich(s => s["Rules"]![0]!["SubjectContains"] = null)),
            ("ordine delle cartelle con un elenco null", "applicata", Rich(s => s["FolderOrder"]!["imap-prova"] = null)),
            ("server IMAP null", "applicata", Rich(s => s["Accounts"]![2]!["Imap"] = null, c => c["ImapPasswords"] = new JsonObject())),
            ("contatto null nell'elenco", "non applicata", Rich(content: c => c["Contacts"] = "[null]")),
            ("contatti che non sono un elenco", "non applicata", Rich(content: c => c["Contacts"] = "{}")),
            ("contatto con gli indirizzi null", "applicata", Rich(content: c => c["Contacts"] = contactsJson.Replace("[\"maria.rossi@clienti-prova.example\"],\"Mobile\"", "null,\"Mobile\""))),
            ("rubrica che non è un oggetto", "non applicata", Rich(content: c => c["AddressBook"] = "[]")),
            ("la stessa proprietà due volte", "non applicata", Rich(content: c => c["Settings"] = "{\"Accounts\":[],\"Accounts\":[]}")),
        };
        var results = cases.Select(c => (c.Name, c.Expected, Result: Start(c.Content, describe: true))).ToList();
        var wrongCases = results.Where(r => r.Result.How != r.Expected || r.Result.Wrong != null).Select(r => $"{r.Name}: {r.Result.How}{(r.Result.Wrong != null ? ", " + r.Result.Wrong : "")}").ToList();
        // Gli stessi dal file .missiva-copia: Backup.Read li rifiuta come file che non si importa, senza altre eccezioni.
        var fromFile = cases.Where(c => c.Expected == "non applicata").Take(6).Select(c => (c.Name, How: Copy2Read(c.Content, out _, out _))).ToList();
        Check(wrongCases.Count == 0 && fromFile.All(f => f.How == "Damaged"),
              $"importazione in attesa con un null dove Missiva non lo scrive ({cases.Length} casi: la password di un account, un account, il suo identificativo, le impostazioni, un elenco, una voce di un elenco): " +
              $"mai un'eccezione all'avvio e mai il file in attesa lasciato lì; {results.Count(r => r.Result.How == "non applicata")} non si applicano, vanno da parte e le impostazioni di prima restano byte per byte, " +
              $"{results.Count(r => r.Result.How == "applicata")} si applicano con i valori di base al posto dei null, e l'avvio dopo le legge; dal file .missiva-copia Backup.Read li rifiuta ({fromFile.Count} su {fromFile.Count})" +
              $"{(wrongCases.Count > 0 ? "; NON VA: " + string.Join("; ", wrongCases) : "")}{(fromFile.Any(f => f.How != "Damaged") ? "; dal file: " + string.Join("; ", fromFile.Where(f => f.How != "Damaged").Select(f => f.Name + " " + f.How)) : "")}");

        // ---------------------------------------------------------------- ogni proprietà, una alla volta
        // Ogni valore del contenuto, delle impostazioni e dei contatti messo a null, cambiato di tipo e, se è un elenco, con
        // una voce null: nessuna variante deve dare un'eccezione, lasciare il file in attesa o impostazioni che non si leggono.
        static List<List<object>> Paths(JsonNode? node)
        {
            var all = new List<List<object>>();
            void Visit(JsonNode? at, List<object> path)
            {
                if (path.Count > 0) all.Add(path);
                if (at is JsonObject map)
                    foreach (var (key, value) in map) Visit(value, [.. path, key]);
                else if (at is JsonArray list)
                    for (int i = 0; i < list.Count; i++) Visit(list[i], [.. path, i]);
            }
            Visit(node, []);
            return all;
        }
        static JsonNode? At(JsonNode node, List<object> path, int upTo) => path.Take(upTo).Aggregate((JsonNode?)node, (at, step) => step is int i ? at![i] : at![(string)step]);
        static void Put(JsonNode node, List<object> path, JsonNode? value)
        {
            var parent = At(node, path, path.Count - 1)!;
            if (path[^1] is int i) parent[i] = value;
            else parent[(string)path[^1]] = value;
        }
        static JsonNode? WrongType(JsonNode? value) => value switch
        {
            JsonObject => new JsonArray(),
            JsonArray => new JsonObject(),
            JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.String => 7,
            _ => "x",
        };
        var variants = new List<(string Name, JsonObject Content)>();
        void Vary(string part, JsonNode tree, Func<JsonNode, JsonObject> rebuild)
        {
            foreach (var path in Paths(tree))
            {
                string name = part + "/" + string.Join("/", path);
                var original = At(tree, path, path.Count);
                foreach (var (kind, changed) in new (string, Func<JsonNode?>)[] { ("null", () => null), ("tipo", () => WrongType(original)) })
                {
                    var copy = tree.DeepClone();
                    Put(copy, path, changed());
                    variants.Add(($"{name} {kind}", rebuild(copy)));
                }
                if (original is JsonArray)
                {
                    var copy = tree.DeepClone();
                    At(copy, path, path.Count)!.AsArray().Add(null);
                    variants.Add(($"{name} voce null", rebuild(copy)));
                }
            }
        }
        var whole = Rich();
        Vary("copia", whole, tree => tree.AsObject());
        Vary("impostazioni", JsonNode.Parse(whole["Settings"]!.GetValue<string>())!, tree => Rich(content: c => c["Settings"] = tree.ToJsonString(), lean: true));
        Vary("contatti", JsonNode.Parse(contactsJson)!, tree => Rich(content: c => c["Contacts"] = tree.ToJsonString()));
        Vary("rubrica", JsonNode.Parse(bookJson)!, tree => Rich(content: c => c["AddressBook"] = tree.ToJsonString()));
        // Chi cerca i null li trova davvero: un elenco null, una voce null, un testo null dentro un account.
        var holed = new Settings { ImageSenders = null!, Rules = [null!], Accounts = [new MailAccount { Email = null! }], Signatures = { ["x"] = null! } };
        bool finds = Copy2Nulls(holed).Order(StringComparer.Ordinal).SequenceEqual([".Accounts[0].Email", ".ImageSenders", ".Rules[0]", ".Signatures[x]"]) && Copy2Nulls(Copy2Rich()).Count == 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var outcomes = variants.Select(v => (v.Name, Result: Start(v.Content))).ToList();
        var bad = outcomes.Where(o => o.Result.Wrong != null).Select(o => $"{o.Name}: {o.Result.Wrong}").ToList();
        Check(finds && bad.Count == 0 && outcomes.Count > 400 &&outcomes.Any(o => o.Result.How == "applicata") && outcomes.Any(o => o.Result.How == "non applicata"),
              $"ogni valore di una copia (contenuto, impostazioni, contatti, rubrica) messo a null, cambiato di tipo o, negli elenchi, con una voce null, uno alla volta: {outcomes.Count} importazioni in attesa in " +
              $"{clock.ElapsedMilliseconds} ms, nessuna eccezione, nessun file in attesa rimasto; {outcomes.Count(o => o.Result.How == "non applicata")} rifiutate senza cambiare nulla, " +
              $"{outcomes.Count(o => o.Result.How == "applicata")} applicate, e ognuna di queste l'avvio dopo la legge, senza null dove il tipo dice di no" +
              $"{(bad.Count > 0 ? $"; NON VA ({bad.Count}): " + string.Join("; ", bad.Take(12)) : "")}");

        // ---------------------------------------------------------------- un file nuovo che non si riesce a scrivere
        var blocked = Path.Combine(root, "t16-scrittura");
        Store.TryWrite(blocked, old, null, out _);
        var untouched = File.ReadAllBytes(Path.Combine(blocked, Store.FileName));
        Directory.CreateDirectory(Path.Combine(blocked, "contatti.dat.importato")); // una cartella dove deve nascere il file dei contatti: la scrittura fallisce
        Copy2Pending(blocked, Rich());
        string blockedHow = Copy2Apply(blocked, out var blockedOutcome);
        Check(blockedHow == "non applicata" && blockedOutcome!.Body.Contains("non si sono potuti scrivere") && !Backup.HasPending(blocked)
              && File.ReadAllBytes(Path.Combine(blocked, Store.FileName)).SequenceEqual(untouched) && Directory.GetFiles(blocked, "*.importato").Length == 0
              && Directory.GetFiles(blocked, "*-prima-dell-importazione-*").Length == 0,
              "un file nuovo che non si riesce a scrivere (i contatti): l'importazione si ferma prima di toccare quelli di adesso, lo dice, toglie i file già preparati e il file in attesa; le impostazioni restano com'erano");
    }

    /// <summary>
    /// T-22: un valore "dpapi:..." scritto a mano in una copia. Prima, negli elenchi (parole e categorie delle regole, ricerche
    /// recenti), si decifrava con DPAPI di questo PC: un cifrato di questo utente messo nella copia tornava in chiaro.
    /// </summary>
    static void Copy2Portable(string root)
    {
        ProtectedTextConverter.TestForget();
        const string secret = "segreto-di-questo-pc";
        var sealedSecret = "dpapi:" + Store.Protect(secret);
        var content = Copy2Content(new Settings
        {
            Accounts = [Copy2Imap("imap-prova")], Rules = [new MailRule { AccountId = "imap-prova", Name = "Regola", MarkRead = true }],
            Templates = [new MailTemplate { Name = "dpapi: appunti di prova", Rtf = @"{\rtf1 testo}" }], RecentSearches = ["dpapi:una ricerca di prova"],
        }, s =>
        {
            var rule = s["Rules"]![0]!;
            (rule["MoveTo"], rule["Name"], rule["Categories"], rule["SubjectContains"], rule["FromAddresses"]) = (sealedSecret, sealedSecret, sealedSecret, sealedSecret, sealedSecret);
            (s["HiddenCalendars"], s["ImageSenders"]) = (sealedSecret, sealedSecret);
        });
        var dir = Path.Combine(root, "t22");
        Copy2Pending(dir, content);
        string how = Copy2Apply(dir, out _);
        var after = Store.Read(dir, out _);
        var imported = after.Rules.FirstOrDefault();
        Check(how == "applicata" && imported is { Unreadable: false, MoveTo: var move, Name: var name, Categories: [var category], SubjectContains: [var word], FromAddresses: [var from] }
              && new[] { move, name, category, word, from }.All(v => v == sealedSecret) && !Backup.PortableJson(after).Contains(secret)
              && after.Templates is [{ Name: "dpapi: appunti di prova" }] && after.RecentSearches is ["dpapi:una ricerca di prova"] && ProtectedTextConverter.UnreadableCount == 0,
              "copia con \"dpapi:...\" scritto a mano al posto di testi ed elenchi (cartella, nome, categorie e parole di una regola, calendari nascosti), con dentro un cifrato di questo PC: resta il testo che è, " +
              "da nessuna parte compare il segreto decifrato; un nome di modello e una ricerca che cominciano davvero con \"dpapi:\" passano dalla copia uguali");
    }

    /// <summary>
    /// T-18: i domini noti dei loghi (domini.dat) scritti in una copia. Prima l'importazione li metteva al posto di quelli del
    /// PC: un dominio appena nato dato per noto da mesi e autenticato mostrava subito il logo del suo sito.
    /// </summary>
    static void Copy2Domains(string root)
    {
        const string theirs = "{\"sosia-prova.example\":{\"First\":\"2026-01-01T00:00:00Z\",\"Last\":\"2026-10-01T00:00:00Z\",\"Verified\":\"2026-10-01T00:00:00Z\"}}";
        const string mine = "{\"dominio-prova.example\":{\"First\":\"2026-09-01T00:00:00Z\",\"Last\":\"2026-10-01T00:00:00Z\",\"Verified\":\"2026-10-01T00:00:00Z\"}}";
        var settings = new Settings { Accounts = [Copy2Imap("imap-prova")] };
        var dir = Path.Combine(root, "t18-con-domini");
        Store.TryWrite(dir, settings, null, out _);
        Directory.CreateDirectory(Path.Combine(dir, "loghi"));
        var file = Path.Combine(dir, "loghi", "domini.dat");
        File.WriteAllText(file, Store.Protect(mine));
        var before = File.ReadAllBytes(file);
        var exported = Backup.Collect(Store.Read(dir, out _), dir, withPasswords: false, out var notes);

        var content = Copy2Content(settings);
        content["KnownDomains"] = theirs;
        string readHow = Copy2Read(content, out var read, out _);
        Copy2Pending(dir, content);
        string how = Copy2Apply(dir, out var outcome);
        var fresh = Path.Combine(root, "t18-pc-nuovo");
        Copy2Pending(fresh, content);
        string freshHow = Copy2Apply(fresh, out _);
        var garbled = Copy2Content(settings);
        garbled["KnownDomains"] = "[ non è nemmeno JSON";
        var odd = Path.Combine(root, "t18-rovinati");
        Copy2Pending(odd, garbled);
        string oddHow = Copy2Apply(odd, out _);
        Check(exported.KnownDomains == null && notes.Count == 0 && readHow == "letta" && read!.KnownDomains == null
              && how == "applicata" && File.ReadAllBytes(file).SequenceEqual(before) && Directory.GetFileSystemEntries(Path.Combine(dir, "loghi")).Length == 1 && !outcome!.SetAside.Any(n => n.Contains("domini"))
              && freshHow == "applicata" && !Directory.Exists(Path.Combine(fresh, "loghi")) && oddHow == "applicata" && !Directory.Exists(Path.Combine(odd, "loghi")),
              "domini noti dei loghi: una copia nuova non li contiene, e quelli scritti in una copia (fatta prima, o preparata da altri con un dominio dato per noto da gennaio e autenticato) non si importano: " +
              "il domini.dat di questo PC resta byte per byte, e su un PC nuovo non nasce");
    }

    /// <summary>
    /// T-19: un file piccolo che si espande, e le iterazioni scritte nell'intestazione. Prima il tetto era 1 GB (100 MB di
    /// contenuto: 0,45 s e 1,1 GB di memoria chiesti in tutto) e 20 milioni di iterazioni (2,6 s a ogni password provata).
    /// </summary>
    static void Copy2Limits(string root)
    {
        // Un contenuto di poco oltre il tetto, tutto di una lettera: compresso sta in qualche decina di KB.
        static byte[] Expanding(int bytes)
        {
            using var packed = new MemoryStream();
            using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write("{\"Settings\":\""u8);
                var chunk = new byte[1 << 20];
                Array.Fill(chunk, (byte)'a');
                for (int written = 0; written < bytes; written += chunk.Length) gzip.Write(chunk);
                gzip.Write("\"}"u8);
            }
            return packed.ToArray();
        }
        var bomb = BackupFile.Seal(Expanding(Backup.MaxPlain + (1 << 20)), Copy2Password);
        GC.Collect();
        long allocated = GC.GetTotalAllocatedBytes(true);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        BackupException? refused = null;
        try { Backup.Read(bomb, Copy2Password); }
        catch (BackupException ex) { refused = ex; }
        long took = clock.ElapsedMilliseconds;
        allocated = GC.GetTotalAllocatedBytes(true) - allocated;
        Check(refused is { Problem: BackupProblem.Damaged } && allocated < 8 << 20 && bomb.Length < 256 << 10,
              $"file di {bomb.Length / 1024} KB che si espande a {(Backup.MaxPlain >> 20) + 1} MB, oltre il tetto di {Backup.MaxPlain >> 20}: rifiutato in {took} ms contando soltanto, con {allocated / 1024} KB di memoria chiesti " +
              "(prima il tetto era 1 GB, e 100 MB ne chiedevano 1.100)");

        // Una copia grande ma vera passa: una firma da 4 MB (un'immagine incollata), andata e ritorno.
        var big = new Settings { Accounts = [Copy2Imap("imap-prova")], Signatures = { ["imap-prova"] = new Signature { Rtf = @"{\rtf1 " + new string('f', 4 << 20) + "}" } } };
        var data = Backup.Collect(big, Path.Combine(root, "t19-vuota"), withPasswords: false, out _);
        var file = Backup.Write(data, Copy2Password);
        var back = Backup.ParseSettings(Backup.Read(file, Copy2Password).Settings);
        // E una che supera il tetto non si scrive nemmeno: non resterebbe un file che poi non si importa.
        string tooBig = "";
        try { Backup.Write(new BackupData { Settings = new string('a', Backup.MaxPlain) }, Copy2Password); }
        catch (InvalidOperationException ex) { tooBig = ex.Message; }
        GC.Collect();
        Check(back.Signatures["imap-prova"].Rtf.Length == big.Signatures["imap-prova"].Rtf.Length && tooBig.Contains($"superano i {Backup.MaxPlain >> 20} MB"),
              $"una copia con una firma da 4 MB si scrive e si rilegge ({file.Length / 1024} KB); dati oltre il tetto non si esportano, e lo si dice (\"{tooBig}\")");

        // Le iterazioni: fino a 5 milioni l'intestazione vale, oltre il file si rifiuta prima di derivare qualunque chiave.
        var small = BackupFile.Seal("{}"u8, Copy2Password);
        string Header(uint iterations)
        {
            var changed = (byte[])small.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(12), iterations);
            try { BackupFile.Check(changed); return "vale"; }
            catch (BackupException ex) { return ex.Problem.ToString(); }
        }
        clock.Restart();
        string over = "";
        var beyond = (byte[])small.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(beyond.AsSpan(12), 20_000_000);
        try { Backup.Read(beyond, "una password qualsiasi"); }
        catch (BackupException ex) { over = ex.Problem.ToString(); }
        long overTook = clock.ElapsedMilliseconds;
        Check(Header(600_000) == "vale" && Header(5_000_000) == "vale" && Header(5_000_001) == "Damaged" && Header(599_999) == "Damaged" && over == "Damaged" && overTook < 200,
              $"iterazioni nell'intestazione: da 600.000 a 5 milioni il file vale, con 5.000.001 o meno di 600.000 è rovinato; con 20 milioni (prima accettati: 2,6 s a ogni password provata) rifiutato in {overTook} ms, senza derivare la chiave");
    }

    /// <summary>
    /// T-21: un modello cifrato per un altro utente di Windows (impostazioni copiate da un altro PC). Prima il testo messo al
    /// posto del cifrato, "(testo cifrato non leggibile su questo PC, rif. ...)", diventava l'oggetto e il testo del messaggio.
    /// </summary>
    static void Copy2Templates(string root)
    {
        var dir = Path.Combine(root, "t21");
        var today = new DateTime(2026, 10, 11);
        static string Text(List<System.Windows.Documents.Block> blocks)
        {
            var scratch = new System.Windows.Documents.FlowDocument();
            scratch.Blocks.AddRange(blocks);
            return new System.Windows.Documents.TextRange(scratch.ContentStart, scratch.ContentEnd).Text.Trim();
        }
        try
        {
            Store.TestEntropy = [9, 4, 9, 4]; // un altro utente di Windows
            ProtectedTextConverter.TestForget();
            Store.TryWrite(dir, new Settings { Templates = [new MailTemplate { Name = "Preventivo", Subject = "Preventivo del {data}", Rtf = @"{\rtf1 Buongiorno \{nome\}, ecco il preventivo.}" }] }, null, out _);
            var written = Store.Read(dir, out _).Templates[0];
            string subject = UI.Templates.Subject(written, "Mario", today), text = Text(UI.Templates.Blocks(written, "Mario", today));
            Store.TestEntropy = null; // questo PC
            ProtectedTextConverter.TestForget();
            var foreign = Store.Read(dir, out _).Templates[0];
            string foreignSubject = UI.Templates.Subject(foreign, "Mario", today), foreignText = Text(UI.Templates.Blocks(foreign, "Mario", today));
            Check(!UI.Templates.Unreadable(written) && subject == "Preventivo del 11 ottobre 2026" && text == "Buongiorno Mario, ecco il preventivo."
                  && UI.Templates.Unreadable(foreign) && foreign.Subject.StartsWith("(testo cifrato non leggibile") && foreignSubject.Length == 0 && foreignText.Length == 0 && UI.Templates.PlainText(foreign).Length == 0,
                  "modello cifrato per un altro utente di Windows: usandolo non dà né oggetto né testo (prima davano tutti e due \"(testo cifrato non leggibile su questo PC, rif. ...)\", che partiva con il messaggio); " +
                  $"lo stesso modello letto dal suo utente dà l'oggetto e il testo di sempre (\"{subject}\", \"{text}\")");
        }
        finally
        {
            Store.TestEntropy = null;
            ProtectedTextConverter.TestForget();
        }
    }

    /// <summary>
    /// T-17: quello che la conferma dice prima di importare. Prima mostrava indirizzi e numeri: un altro server per la stessa
    /// casella, una regola che elimina tutto quello che arriva e le immagini accese per interi servizi di posta passavano
    /// senza che nulla lo dicesse.
    /// </summary>
    static void Copy2Confirm(string root)
    {
        // Questo PC ha già la casella, con i suoi server.
        var mine = Copy2Imap("imap-prova", "imap.studio-prova.example");
        mine.Imap!.SmtpHost = "smtp.studio-prova.example";
        var here = new Settings { Accounts = [mine] };
        // La copia preparata da altri: la stessa casella verso un altro server, due regole che nascondono posta, immagini per tutti.
        var theirs = new Settings
        {
            Accounts = [Copy2Imap("imap-prova", "imap.altro-prova.example")],
            Rules =
            [
                new MailRule { AccountId = "imap-prova", Name = "Fatture", FromAddresses = ["fatture@fornitore-prova.example"], MoveTo = "INBOX/Fatture", MoveToName = "Fatture", Enabled = false },
                new MailRule { AccountId = "imap-prova", Name = "Avvisi", SubjectContains = ["password", "accesso"], Delete = true },
                new MailRule { AccountId = "imap-prova", Name = "Pulizia", Delete = true, MarkRead = true },
            ],
            ImageSenders = ["com", "co.uk", "gmail.example", "LIBERO.example", "fornitore-prova.example", "info@fornitore-prova.example", "mario.rossi@gmail.example"],
            NotificationsOff = ["imap-prova"],
        };
        var content = Copy2Content(theirs);
        string readHow = Copy2Read(content, out var data, out _);
        var summary = readHow == "letta" ? Backup.Describe(data!, here) : null;
        string body = summary == null ? "" : UI.BackupUi.ConfirmText(summary, data!.App, hasData: true).Body;
        int At(string text) => body.IndexOf(text, StringComparison.Ordinal);
        Check(summary != null
              && summary.Warnings is [var everything, var servers]
              && everything == "Una regola di mario@studio-prova.example vale per tutti i messaggi che arrivano: elimina (nel Cestino), segna come letto."
              && servers == "mario@studio-prova.example su questo PC c'è già, con altri server: qui imap.studio-prova.example:993 e smtp.studio-prova.example:465, utente mario@studio-prova.example; " +
                            "nella copia imap.altro-prova.example:993 e imap.altro-prova.example:465, utente mario@studio-prova.example. La password di questo PC non va ai server della copia."
              && summary.AccountLines is ["mario@studio-prova.example: IMAP, posta in arrivo da imap.altro-prova.example:993, in uscita da imap.altro-prova.example:465, notifiche spente"]
              && summary.RuleLines is ["mario@studio-prova.example: Per tutti i messaggi: elimina (nel Cestino), segna come letto",
                                       "mario@studio-prova.example: Se l'oggetto contiene \"password\" o \"accesso\": elimina (nel Cestino)",
                                       "mario@studio-prova.example: Se il mittente è fatture@fornitore-prova.example: sposta in Fatture (spenta)"]
              && summary.ImageSenders is ["fornitore-prova.example", "mario.rossi@gmail.example"]
              && At("Da guardare prima di importare:\n- Una regola di") > 0 && At("Da guardare") < At("\n\nAccount:\n- mario@") && At("Account:") < At("\n\nRegole di Missiva (") && At("Regole di Missiva (") < At("Immagini esterne mostrate sempre per 2 mittenti: fornitore-prova.example, mario.rossi@gmail.example.")
              && At("Immagini esterne") < At("Importandola, quello che Missiva ha ora") && body.Contains("le due cose non si uniscono"),
              "conferma di una copia preparata da altri, su un PC che ha già quella casella: dice per prima cosa che una regola vale per tutti i messaggi (elimina, segna come letto) e che la casella nella copia ha altri server " +
              "(quelli di qui e quelli della copia, e che la password di questo PC non ci va); poi ogni account con tipo e server, le regole in parole (prima quelle che eliminano, le spente dette), " +
              $"e per chi le immagini si mostrano da sole. Prima: indirizzi e numeri soltanto. Avvisi: {(summary == null ? readHow : string.Join(" | ", summary.Warnings))}");

        // Importata: le voci che valgono per interi servizi non entrano, e l'avviso dopo il riavvio dice delle regole accese.
        var dir = Path.Combine(root, "t17-ostile");
        Store.TryWrite(dir, here, null, out _);
        Copy2Pending(dir, content);
        string how = Copy2Apply(dir, out var outcome);
        var after = Store.Read(dir, out _);
        after.UpgradeImageSenders(); // come Store.Load
        Check(how == "applicata" && after.ImageSenders is ["fornitore-prova.example", "mario.rossi@gmail.example"]
              && !after.ShowsImagesFrom("sconosciuto@gmail.example") && !after.ShowsImagesFrom("sconosciuta@libero.example") && !after.ShowsImagesFrom("x@azienda-prova.co.uk") && !after.ShowsImagesFrom("chiunque@qualunque-prova.com")
              && after.ShowsImagesFrom("newsletter@posta.fornitore-prova.example") && after.ShowsImagesFrom("mario.rossi@gmail.example")
              && outcome!.Notes is [var droppedNote, var rulesNote] && droppedNote.StartsWith("4 voci dell'elenco dei mittenti di cui mostrare sempre le immagini non sono state importate")
              && rulesNote.StartsWith("La copia aveva 2 regole di Missiva accese") && outcome.Body.Contains(droppedNote) && outcome.Body.Contains(rulesNote),
              "mittenti delle immagini di una copia: non entrano un dominio di primo livello (\"com\": cadeva già alla lettura), un suffisso nazionale da solo (\"co.uk\") e un servizio di posta personale intero " +
              "(prima entravano, e le immagini di chiunque scrivesse da lì si caricavano da sole); restano il dominio di un'organizzazione e l'indirizzo di una persona. L'avviso dopo il riavvio dice quante voci " +
              $"non sono entrate e che la copia aveva regole accese (\"{outcome?.Notes.LastOrDefault()}\")");

        // Una copia fatta da Missiva, su un PC che ha gli stessi account con gli stessi server: nessun avviso, e nulla si perde.
        var rich = Copy2Rich();
        string richHow = Copy2Read(Copy2Content(rich), out var richData, out _);
        var same = richHow == "letta" ? Backup.Describe(richData!, Copy2Rich()) : null;
        var alone = richHow == "letta" ? Backup.Describe(richData!) : null;
        var first = same == null ? default : UI.BackupUi.ConfirmText(same, "1.0.0", hasData: false);
        var dirRich = Path.Combine(root, "t17-normale");
        Copy2Pending(dirRich, Copy2Content(rich));
        Copy2Apply(dirRich, out var richOutcome);
        Check(same is { Warnings.Count: 0, RuleLines: ["mario@studio-prova.example: Se il mittente è fatture@fornitore-prova.example e l'oggetto contiene \"fattura\": sposta in Fatture, segna come letto, categoria \"Clienti\""],
                        ImageSenders: ["fornitore-prova.example"] }
              && same.AccountLines is ["mario.rossi@outlook-prova.example: Microsoft 365", "maria.rossi@gmail-prova.example: Google Workspace, notifiche spente",
                                       "mario@studio-prova.example: IMAP, posta in arrivo da imap.studio-prova.example:993, in uscita da imap.studio-prova.example:465"]
              && alone is { Warnings.Count: 0 } && first.Heading == "Importare questa copia?" && !first.Body.Contains("Da guardare") && !first.Body.Contains("sostituito")
              && first.Body.Contains("Immagini esterne mostrate sempre per un mittente: fornitore-prova.example.")
              && richOutcome is { Applied: true, Notes: [var oneRule] } && oneRule.StartsWith("La copia aveva una regola di Missiva accesa") && Store.Read(dirRich, out _).ImageSenders is ["fornitore-prova.example"],
              "conferma di una copia normale: nessuna riga \"Da guardare\", gli account con il loro tipo (Microsoft 365, Google Workspace, IMAP con i server), la regola in parole e il mittente delle immagini; " +
              "importata, quel mittente resta e l'avviso ricorda la regola accesa");

        // Altre cose che la copia può cambiare a un account che c'è già, e testi fatti per confondere la conferma.
        var hereApps = new Settings
        {
            MicrosoftClientId = "app-di-questo-pc", GoogleClientId = "google-di-questo-pc.apps.example",
            Accounts = [new MailAccount { Id = "lavoro-prova", Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = "organizations", HomeAccountId = "casa-prova" },
                        new MailAccount { Id = "gmail-prova", Kind = AccountKind.Gmail, Email = "maria.rossi@gmail-prova.example" }],
        };
        var otherApps = new Settings
        {
            MicrosoftClientId = "app-di-questo-pc", GoogleClientId = "google-della-copia.apps.example",
            Accounts = [new MailAccount { Id = "lavoro-prova", Kind = AccountKind.Microsoft, Email = "mario.rossi@studio-prova.example", Tenant = "directory-della-copia.example", ClientId = "app-della-copia" },
                        new MailAccount { Id = "gmail-prova", Kind = AccountKind.Gmail, Email = "maria.rossi@gmail-prova.example" },
                        Copy2Imap("imap-prova", "imap.studio-prova.example\n- Regola finta: ‮elbisivni")],
            Rules = Enumerable.Range(0, 20).Select(i => new MailRule { AccountId = "imap-prova", Name = "Regola " + i, SubjectContains = [i == 0 ? new string('x', 5000) + "\nseconda riga‮" : "parola " + i], MarkRead = true }).ToList(),
        };
        string appsHow = Copy2Read(Copy2Content(otherApps), out var appsData, out _);
        var apps = appsHow == "letta" ? Backup.Describe(appsData!, hereApps) : null;
        var lines = apps == null ? [] : apps.Warnings.Concat(apps.AccountLines).Concat(apps.RuleLines).Concat(apps.ImageSenders).ToList();
        Check(apps is { Warnings: [var app, var google] } && app.StartsWith("mario.rossi@studio-prova.example su questo PC c'è già, ma nella copia entra in Microsoft con un'altra app o un'altra directory")
              && google.StartsWith("Gli account Google che su questo PC ci sono già nella copia usano un altro client di Google")
              && apps.AccountLines[0] == "mario.rossi@studio-prova.example: Microsoft 365, directory directory-della-copia.example, app app-della-copia"
              && apps.RuleLines.Count == 13 && apps.RuleLines[^1].StartsWith("e altre 8") && lines.All(l => l.Length <= 601 && !l.Contains('\n') && !l.Contains('‮')) && lines.Any(l => l.EndsWith('…')),
              "conferma: un account Microsoft che c'è già e nella copia ha un'altra app o un'altra directory, e un altro client di Google, sono detti; venti regole diventano dodici righe e \"e altre 8\"; " +
              "un testo della copia con a capo, cambi di direzione o 5.000 caratteri resta su una riga sola, accorciata, senza quei caratteri (non può fingere altre righe della conferma)");

        // (c) La rubrica dei destinatari: confermato e lasciato l'11 ottobre. Dalle scelte del 12 ottobre l'importazione azzera i
        // conteggi (AddressBook.Imported, ProvaBozze.Scelte1.cs). Una riga di sola misura, non un controllo: che cosa fanno
        // dei conteggi scritti da altri, se arrivassero.
        AddressBook.TestBook([new AddressBook.Entry { Name = "Mario Rossi", Email = "mario.rossi@altro-prova.example", Sent = 900, LastSent = DateTime.Now },
                              new AddressBook.Entry { Name = "Mario Rossi", Email = "mario.rossi@studio-prova.example", Sent = 12, LastSent = DateTime.Now }]);
        try
        {
            Report.Add($"      (T-17, rubrica dei destinatari: con conteggi scritti da altri il primo suggerimento per \"mario\" sarebbe {AddressBook.Suggest("mario", 3).FirstOrDefault()?.Email}, " +
                       $"e per un messaggio di \"Mario Rossi\" da quell'indirizzo la fascia dei sosia: {AddressBook.LookAlike(new Address { Name = "Mario Rossi", Email = "mario.rossi@altro-prova.example" }) ?? "nessuna"}; " +
                       "dal 12 ottobre l'importazione li azzera)");
        }
        finally
        {
            AddressBook.TestBook(null);
        }
    }

    /// <summary>
    /// T-20: i file messi da parte da un'importazione tengono gli accessi degli account di prima (cifrati con DPAPI per questo
    /// utente). Confermato e lasciato l'11 ottobre: tenerli è una scelta del blocco 7 (ESITO.md). Dalle scelte del 12 ottobre
    /// restano finché non li si elimina dalla pagina Copia di sicurezza (Backup.DeleteAside, ProvaBozze.Scelte1.cs). Una
    /// riga di sola misura, non un controllo.
    /// </summary>
    static void Copy2Aside(string root)
    {
        var dir = Path.Combine(root, "t20");
        var imap = Copy2Imap("di-prima-imap", email: "prima@studio-prova.example");
        imap.Imap!.ProtectedPassword = Store.Protect("password-di-prima");
        var before = new Settings { Accounts = [imap, new MailAccount { Id = "di-prima-gmail", Kind = AccountKind.Gmail, Email = "maria.rossi@gmail-prova.example", ProtectedRefreshToken = Store.Protect("token-di-prima") }] };
        Store.TryWrite(dir, before, null, out _);
        Store.TryWrite(dir, before, null, out _); // due salvataggi: c'è anche impostazioni.bak
        Copy2Pending(dir, Copy2Content(new Settings { Accounts = [Copy2Imap("nuovo-prova")] }));
        string how = Copy2Apply(dir, out _);
        var aside = Directory.GetFiles(dir, "impostazioni-prima-dell-importazione-*");
        int secrets = aside.Sum(path => Store.TryParse(path, out var kept, out _)
            ? kept.Accounts.Count(a => Store.TryUnprotect(a.Imap?.ProtectedPassword, out _) || Store.TryUnprotect(a.ProtectedRefreshToken, out _)) : 0);
        Report.Add($"      (T-20, file messi da parte: importazione {how} sopra due account che la copia non ha; da parte {aside.Length} file delle impostazioni di prima, " +
                   $"e lì dentro si decifrano ancora {secrets} accessi di quegli account, la password IMAP e il token di Google in ognuno; dal 12 ottobre si eliminano da Impostazioni, Copia di sicurezza)");
    }
}
#endif
