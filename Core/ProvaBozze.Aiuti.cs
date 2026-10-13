#if DEBUG
namespace Missiva;

/// <summary>
/// Gli aiuti comuni della suite (audit dell'11 ottobre, C-44, C-45, C-46): i ganci di prova di Missiva con il loro valore
/// di riposo, rimessi fra un gruppo e l'altro; il gestore HTTP "nessuno" che sta al posto di Graph, di Google e della
/// configurazione automatica finché un gruppo non installa il suo; gli errori dei server finti; l'attesa di una condizione.
/// </summary>
static partial class ProvaBozze
{
    /// <summary>Un gancio di prova di Missiva: il nome, se è a riposo, e come ci torna.</summary>
    sealed record Hook(string Name, Func<bool> Resting, Action Rest);

    /// <summary>Il riposo di un gancio è il valore che ha quando nessuna prova l'ha ancora toccato: letto qui, una volta.</summary>
    static Hook Keep<T>(string name, Func<T> get, Action<T> set)
    {
        var start = get();
        return new(name, () => EqualityComparer<T>.Default.Equals(get(), start), () => set(start));
    }

    static Hook[]? hooks;

    /// <summary>
    /// I ganci statici che le prove accendono, e le attese di Missiva che le prove accorciano. Un gancio nuovo va aggiunto
    /// qui: così un gruppo che lo lascia acceso viene detto, e il gruppo dopo non lo trova. Il 12 ottobre 2026 nessun
    /// gruppo ne lasciava (ognuno li rimette in un finally), ma nulla lo controllava. L'elenco nasce al primo gruppo, dopo
    /// FreshData: leggere Store prima fisserebbe la cartella dei dati.
    /// </summary>
    static Hook[] Hooks => hooks ??=
    [
        Keep("ImapProvider.TestPlain", () => ImapProvider.TestPlain, v => ImapProvider.TestPlain = v),
        Keep("ImapProvider.TestCertificate", () => ImapProvider.TestCertificate, v => ImapProvider.TestCertificate = v),
        Keep("GmailProvider.TestPort", () => GmailProvider.TestPort, v => GmailProvider.TestPort = v),
        Keep("GmailProvider.TestSmtpPort", () => GmailProvider.TestSmtpPort, v => GmailProvider.TestSmtpPort = v),
        Keep("GmailProvider.TestSmtpCertificate", () => GmailProvider.TestSmtpCertificate, v => GmailProvider.TestSmtpCertificate = v),
        Keep("GmailProvider.TestContactsWritable", () => GmailProvider.TestContactsWritable, v => GmailProvider.TestContactsWritable = v),
        Keep("GoogleAuth.TestToken", () => GoogleAuth.TestToken, v => GoogleAuth.TestToken = v),
        Keep("GoogleAuth.TestHttp", () => GoogleAuth.TestHttp, v => GoogleAuth.TestHttp = v),
        Keep("GoogleAuth.FailureMemory", () => GoogleAuth.FailureMemory, v => GoogleAuth.FailureMemory = v),
        Keep("GraphProvider.TestSharedConsent", () => GraphProvider.TestSharedConsent, v => GraphProvider.TestSharedConsent = v),
        Keep("GraphProvider.TestCalendarConsent", () => GraphProvider.TestCalendarConsent, v => GraphProvider.TestCalendarConsent = v),
        Keep("GraphProvider.TestSettingsConsent", () => GraphProvider.TestSettingsConsent, v => GraphProvider.TestSettingsConsent = v),
        Keep("GraphProvider.TestContactsConsent", () => GraphProvider.TestContactsConsent, v => GraphProvider.TestContactsConsent = v),
        Keep("GraphProvider.TestPeopleConsent", () => GraphProvider.TestPeopleConsent, v => GraphProvider.TestPeopleConsent = v),
        Keep("GraphProvider.DownloadIdle", () => GraphProvider.DownloadIdle, v => GraphProvider.DownloadIdle = v),
        Keep("HtmlToFlow.TestOld", () => HtmlToFlow.TestOld, v => HtmlToFlow.TestOld = v),
        Keep("MailAutoconfig.TestProbe", () => MailAutoconfig.TestProbe, v => MailAutoconfig.TestProbe = v),
        Keep("MailDns.TestAnswers", () => MailDns.TestAnswers, v => MailDns.TestAnswers = v),
        Keep("MailDns.TestTxt", () => MailDns.TestTxt, v => MailDns.TestTxt = v),
        Keep("Network.TestOffline", () => Network.TestOffline, v => Network.TestOffline = v),
        Keep("Network.TestOfflineCode", () => Network.TestOfflineCode, v => Network.TestOfflineCode = v),
        Keep("BodyCache.TestRoot", () => BodyCache.TestRoot, v => BodyCache.TestRoot = v),
        Keep("BodyCache.TestLimit", () => BodyCache.TestLimit, v => BodyCache.TestLimit = v),
        Keep("OfflineCopy.TestFull", () => OfflineCopy.TestFull, v => OfflineCopy.TestFull = v),
        Keep("PdfWorker.TestFailJob", () => PdfWorker.TestFailJob, v => PdfWorker.TestFailJob = v),
        Keep("Pec.TestRoots", () => Pec.TestRoots, v => Pec.TestRoots = v),
        Keep("SenderLogos.TestMarkRoot", () => SenderLogos.TestMarkRoot, v => SenderLogos.TestMarkRoot = v),
        Keep("SenderLogos.TestConnect", () => SenderLogos.TestConnect, v => SenderLogos.TestConnect = v),
        Keep("Store.TestDataRoot", () => Store.TestDataRoot, v => Store.TestDataRoot = v),
        Keep("Store.TestEntropy", () => Store.TestEntropy, v => Store.TestEntropy = v),
        Keep("Log.TestDir", () => Log.TestDir, v => Log.TestDir = v),
        Keep("AddressBook.TestWriting", () => AddressBook.TestWriting, v => AddressBook.TestWriting = v),
        Keep("AddressBook.Contacts", () => AddressBook.Contacts, v => AddressBook.Contacts = v),
        Keep("Contact.TestOldMatch", () => Contact.TestOldMatch, v => Contact.TestOldMatch = v),
        Keep("InboxIdle.Renew", () => InboxIdle.Renew, v => InboxIdle.Renew = v),
        Keep("InboxIdle.FirstRetry", () => InboxIdle.FirstRetry, v => InboxIdle.FirstRetry = v),
        Keep("InboxIdle.OwnWindow", () => InboxIdle.OwnWindow, v => InboxIdle.OwnWindow = v),
        Keep("UnreadSync.Settle", () => UnreadSync.Settle, v => UnreadSync.Settle = v),
        Keep("MessageItem.TestToday", () => UI.MessageItem.TestToday, v => UI.MessageItem.TestToday = v),
        Keep("ReadingView.TestDark", () => UI.ReadingView.TestDark, v => UI.ReadingView.TestDark = v),
    ];

    /// <summary>
    /// Il gestore HTTP che sta al posto di Graph, di Google e della configurazione automatica fra un gruppo e l'altro:
    /// nessuna richiesta parte, e chi ne fa una senza aver installato il suo gestore finto riceve un errore e resta scritto
    /// in Strays. Prima restava installato il gestore dell'ultimo gruppo che ne aveva messo uno, e una prova che
    /// dimenticava il suo riceveva le risposte di quello.
    /// </summary>
    sealed class NoHandler(string service) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var what = $"{service}: {request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath}";
            lock (Strays) Strays.Add(what);
            throw new InvalidOperationException($"prova senza gestore finto ({what})");
        }
    }

    /// <summary>Le richieste arrivate a NoHandler: fatte senza un gestore finto installato.</summary>
    static readonly List<string> Strays = new();

    /// <summary>Gli errori che i server finti (IMAP e SMTP) hanno sollevato servendo una connessione, che il client vede solo come una connessione caduta.</summary>
    static readonly List<string> ServerErrors = new();

    /// <summary>Tutti i ganci a riposo, e i gestori HTTP su "nessuno" o su quello vero: lo stato da cui parte ogni gruppo.</summary>
    static void RestHooks()
    {
        foreach (var hook in Hooks) hook.Rest();
        GraphProvider.UseTestHandler(new NoHandler("Graph"));
        GoogleApi.UseTestHandler(new NoHandler("Google"));
        MailAutoconfig.UseTestHandler(new NoHandler("configurazione automatica"));
        SenderLogos.UseTestHandler(null);
        Unsubscribe.UseTestHandler(null);
        AddressBook.TestBook(null);
    }

    /// <summary>I ganci che ora non sono a riposo.</summary>
    static List<string> HooksLeft() => Hooks.Where(h => !h.Resting()).Select(h => h.Name).ToList();

    /// <summary>
    /// Dopo un gruppo: i ganci rimasti accesi, le richieste fatte senza un gestore finto e gli errori dei server finti
    /// sono ognuno un controllo non passato, con il nome del gruppo. Se tutto è a posto non scrive nulla.
    /// </summary>
    static void LeftBehind(string group)
    {
        var left = HooksLeft();
        if (left.Count > 0) Check(false, $"il gruppo \"{group}\" lascia accesi dei ganci di prova, che il gruppo dopo troverebbe: {string.Join(", ", left)}");
        List<string> strays, errors;
        lock (Strays) { strays = Strays.ToList(); Strays.Clear(); }
        lock (ServerErrors) { errors = ServerErrors.ToList(); ServerErrors.Clear(); }
        if (strays.Count > 0)
            Check(false, $"durante il gruppo \"{group}\" {strays.Count} richieste sono partite senza un gestore finto installato: {string.Join("; ", strays.Distinct().Take(6))}");
        if (errors.Count > 0)
            Check(false, $"durante il gruppo \"{group}\" un server finto ha sollevato {errors.Count} errori servendo una connessione: {string.Join("; ", errors.Distinct().Take(4))}");
    }

    /// <summary>Aspetta che la condizione diventi vera, guardandola ogni 20 ms, al più per limit; dice se lo è diventata.</summary>
    static async Task<bool> Until(Func<bool> condition, TimeSpan limit)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < limit) await Task.Delay(20);
        return condition();
    }
}
#endif
