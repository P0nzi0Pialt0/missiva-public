namespace Missiva;

/// <summary>
/// Gli invii pianificati di Gmail e IMAP, che fa Missiva (con Graph li tiene Exchange): la bozza nelle Bozze e l'ora in
/// Settings.ScheduledSends. Ogni minuto, e all'avvio, quelli scaduti partono (IMailProvider.SendDraftAsync) ed escono
/// dall'elenco; se la bozza non c'è più o l'invio non riesce, lo si dice.
/// </summary>
public static class ScheduledSending
{
    static bool running;
    /// <summary>Gli invii fermi per la rete già detti all'utente: l'avviso una volta sola, poi si riprova ogni minuto in silenzio.</summary>
    static readonly HashSet<ScheduledSend> warned = new();

    public static void Add(MailAccount account, string draftKey, DateTime at, string subject)
    {
        Store.Settings.ScheduledSends.Add(new ScheduledSend { AccountId = account.Id, DraftKey = draftKey, At = at, Subject = subject });
        Store.Save();
        Log.Write($"invio pianificato di {Log.Private(subject)} per {at:dd/MM/yyyy HH:mm}: lo farà Missiva ({account.Email})");
    }

    /// <summary>
    /// Gli invii scaduti a quell'ora (le prove la simulano). Restituisce quanti ne sono partiti; problem riceve quelli la cui
    /// bozza non c'è più e quelli non riusciti, con il motivo. SendDraftAsync restituisce appena l'SMTP è riuscito, e l'invio
    /// esce subito dall'elenco: non riparte mai due volte. Un errore di rete lascia l'invio in elenco per il minuto dopo
    /// (avviso al primo); gli altri (accesso da rifare, destinatario rifiutato, server che non accetta) lo tolgono, con
    /// l'avviso: la bozza resta nelle Bozze, da inviare o pianificare di nuovo. Lo toglie anche la connessione caduta
    /// quando il server aveva già tutto il messaggio (MailException.MaybeSent): forse è partito, e non si riprova.
    /// </summary>
    public static async Task<int> RunDueAsync(DateTime now, Func<string, IMailProvider?> providerOf, Action<ScheduledSend, string>? problem = null)
    {
        if (running) return 0;
        running = true;
        int sent = 0;
        try
        {
            foreach (var due in Store.Settings.ScheduledSends.Where(s => s.At <= now).OrderBy(s => s.At).ToList())
            {
                if (providerOf(due.AccountId) is not { } provider) continue; // account non ancora pronto, o rimosso
                try
                {
                    bool done = await provider.SendDraftAsync(due.DraftKey);
                    Store.Settings.ScheduledSends.Remove(due);
                    Store.Save();
                    warned.Remove(due);
                    if (done)
                    {
                        sent++;
                        Log.Write($"invio pianificato di {Log.Private(due.Subject)} partito ({provider.Account.Email})");
                    }
                    else problem?.Invoke(due, $"La bozza di \"{due.Subject}\" non c'è più nelle Bozze (eliminata, o modificata dopo la pianificazione): non è partita.");
                }
                catch (Exception ex)
                {
                    Log.Error($"invio pianificato di {Log.Private(due.Subject)}", ex);
                    var reason = ex is MailException ? ex.Message : FirstLine(ex.Message);
                    if (MailException.MaybeSentOf(ex))
                    {
                        // Il server aveva già tutto il messaggio e non ha risposto: può averlo accettato. Riprovare lo manderebbe
                        // due volte (o una volta al minuto, con un server lento a rispondere): l'invio esce dall'elenco e si dice.
                        Store.Settings.ScheduledSends.Remove(due);
                        Store.Save();
                        warned.Remove(due);
                        problem?.Invoke(due, $"\"{due.Subject}\" doveva partire il {due.At:dd/MM} alle {due.At:HH:mm}: {reason}\n\nMissiva non riprova da sola, per non mandarlo due volte. " +
                                             "Il messaggio è ancora nelle Bozze: controlla se è arrivato (nella Posta inviata, o chiedendo al destinatario) e, se non è partito, invialo da lì.");
                    }
                    else if (Transient(ex))
                    {
                        // Rete assente o server che non risponde: si riprova al prossimo controllo, e lo si dice una volta.
                        if (warned.Add(due))
                            problem?.Invoke(due, $"\"{due.Subject}\" doveva partire il {due.At:dd/MM} alle {due.At:HH:mm} ma non è ancora partito: {reason}\n\nMissiva riprova ogni minuto finché è aperta.");
                    }
                    else
                    {
                        Store.Settings.ScheduledSends.Remove(due);
                        Store.Save();
                        warned.Remove(due);
                        problem?.Invoke(due, $"\"{due.Subject}\" non è partito: {reason}\n\nIl messaggio è ancora nelle Bozze: aprilo da lì per inviarlo o pianificarlo di nuovo.");
                    }
                }
            }
        }
        finally
        {
            running = false;
        }
        return sent;
    }

    /// <summary>Errori di rete (anche dentro una MailException di Missiva): passano da soli, si riprova.</summary>
    internal static bool Transient(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is IOException or System.Net.Sockets.SocketException or TimeoutException or OperationCanceledException or HttpRequestException
                  or MailKit.ServiceNotConnectedException or MailKit.ProtocolException)
                return true;
        return false;
    }

    static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}
