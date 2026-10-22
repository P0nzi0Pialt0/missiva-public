using System.Windows.Documents;

namespace Missiva.UI;

/// <summary>
/// Posponi, Rispondi, Contrassegna ed Elimina dalle notifiche dei nuovi messaggi (richiesta dell'autore), come le notifiche di
/// Outlook. Rispondi apre la finestra della risposta; gli altri lavorano senza aprire la finestra, anche con Missiva
/// nell'area di notifica o chiusa (Windows la avvia nascosta). Il messaggio si ritrova
/// dall'account e dall'identificativo tenuti da Missiva (le notifiche recenti, l'elenco aperto, la copia locale della Posta
/// in arrivo), mai dal testo della notifica. Le scritture passano dal provider come tutte le altre: nella versione di prova
/// da TestingProvider, solo nel registro.
/// </summary>
public partial class MainWindow
{
    /// <param name="folderId">La cartella del messaggio (null nelle notifiche di prima: la Posta in arrivo, l'unica notificata).</param>
    /// <param name="action">reply, snooze, flag o delete.</param>
    /// <param name="text">Rispondi dalle notifiche di prima (con la casella): il testo scritto, che si apre in una scrittura e non parte da solo (S-21); senza testo si apre la risposta.</param>
    /// <param name="until">Posponi: l'ora scelta (App.SnoozeUntil).</param>
    /// <param name="known">Il messaggio della notifica, se Missiva lo ha ancora in memoria (App.Notify).</param>
    public async Task NotificationActionAsync(string accountId, string? folderId, string messageId, string action, string? text, DateTime until, MessageSummary? known)
    {
        await accountsListed.Task; // avviata da Windows per l'azione: gli account e le cartelle della copia locale
        if (accounts.FirstOrDefault(a => a.Provider.Account.Id == accountId) is not { } account)
        {
            Log.Write($"azione dalla notifica: account {accountId} non trovato");
            return;
        }
        var message = known ?? FindNotified(account, folderId, messageId);
        if (message == null)
        {
            // Non si agisce su un messaggio che potrebbe essere un altro: si apre la Posta in arrivo, e il testo di Rispondi
            // resta in una scrittura, da copiare nella risposta.
            Log.Write($"azione dalla notifica: messaggio {messageId} non trovato, si apre la Posta in arrivo");
            Tell(T("Messaggio non trovato"), action == "reply" && text?.Trim().Length > 0
                ? T("Missiva non ha più questo messaggio sotto mano: lo trovi nella Posta in arrivo, che si apre ora. Il testo della risposta è in una finestra di scrittura.")
                : T("Missiva non ha più questo messaggio sotto mano: lo trovi nella Posta in arrivo, che si apre ora."));
            ShowFromTray();
            await OpenFromNotificationAsync(accountId, messageId, null);
            if (action == "reply" && text?.Trim().Length > 0) OpenQuickReplyWindow(account, null, text, T("Risposta dalla notifica: il messaggio non si è trovato. Copia il testo nella risposta."));
            return;
        }
        switch (action)
        {
            // Mai un invio senza finestra (audit, blocco 10, S-21). Le notifiche mostrate dalle versioni di prima avevano una
            // casella di testo e la risposta partiva da sola; quelle di oggi no, ma un'azione "reply" con un testo può
            // arrivare da qualunque processo dello stesso utente (il server COM delle notifiche è in HKCU) verso un messaggio
            // dell'elenco: il testo va sempre in una scrittura, con A ben visibile, e parte solo con Invia.
            case "reply" when text?.Trim().Length > 0:
                OpenQuickReplyWindow(account, message, text,
                    SenderCheck.QuickReplyRisk(account.Provider.Account, message) is { } risk
                        ? T("Dalla notifica non è partita: {0}. Controlla a chi va prima di inviare.", risk)
                        : T("Il testo viene da una notifica: non è stato inviato. Controlla a chi va e invia da qui."));
                break;
            case "reply":
                OpenReplyFromNotification(account, message);
                break;
            case "snooze":
                await SnoozeFromNotificationAsync(account, message, until);
                break;
            case "snoozepick":
                await PickSnoozeFromNotificationAsync(account, message);
                break;
            case "flag":
                await FlagFromNotificationAsync(account, message);
                break;
            case "delete":
                await DeleteFromNotificationAsync(account, message);
                break;
        }
    }

    /// <summary>
    /// Il clic su una notifica di un messaggio solo (richiesta dell'autore): il messaggio si apre in una finestra sua, come il
    /// doppio clic sulla riga, e la finestra principale resta com'è (dov'era Persone, o il Calendario, non cambia nulla
    /// sotto e non si viene riportati alla posta); nascosta nell'area di notifica resta nascosta. Il messaggio si ritrova
    /// come per le azioni della notifica (FindNotified); se Missiva non lo ha più sotto mano si ripiega sulla Posta in
    /// arrivo dell'account, in primo piano, come prima.
    /// </summary>
    public async Task OpenMessageWindowFromNotificationAsync(string accountId, string? folderId, string messageId, MessageSummary? known)
    {
        await accountsListed.Task;
        if (accounts.FirstOrDefault(a => a.Provider.Account.Id == accountId) is not { } account)
        {
            Log.Write($"notifica aperta: account {accountId} non trovato");
            ShowFromTray();
            return;
        }
        var message = known ?? FindNotified(account, folderId, messageId);
        if (message == null)
        {
            Log.Write($"notifica aperta: messaggio {messageId} non trovato, si apre la Posta in arrivo");
            ShowFromTray();
            await OpenFromNotificationAsync(accountId, messageId, null);
            return;
        }
        // La sua riga, se è nell'elenco aperto (letto e contrassegno restano allineati), altrimenti una nuova.
        var item = rows.Rows.FirstOrDefault(r => r.Account == account && r.Summary.Id == message.Id && r.Summary.FolderId == message.FolderId && !r.IsHead)
                   ?? new MessageItem(message, account);
        var window = OpenInWindow(item);
        window?.Activate();
        Log.Write($"notifica aperta: il messaggio {message.Id} di {account.Provider.Account.Email} in una finestra ({(IsVisible ? "con la finestra principale aperta" : "finestra principale nell'area di notifica")})");
    }

    /// <summary>
    /// Il messaggio della notifica nell'elenco aperto o nella copia locale della sua cartella (la Posta in arrivo, anche
    /// Evidenziata e Altra), sempre con la cartella: in Gmail e IMAP l'identificativo è l'UID, che si ripete da una cartella
    /// all'altra, e con aperta Posta inviata o una ricerca l'azione finirebbe su un altro messaggio.
    /// </summary>
    MessageSummary? FindNotified(AccountItem account, string? folderId, string messageId)
    {
        folderId ??= account.Folders.FirstOrDefault(f => f.Folder.Kind == FolderKind.Inbox)?.Folder.Id;
        if (folderId == null) return null;
        if (rows.Rows.FirstOrDefault(r => r.Account == account && r.Summary.Id == messageId && r.Summary.FolderId == folderId) is { } row) return row.Summary;
        foreach (var focusedTab in new bool?[] { null, true, false })
            if (MailCache.LoadMessages(account.Provider.Account, folderId, focusedTab)?.FirstOrDefault(m => m.Id == messageId && m.FolderId == folderId) is { } cached)
                return cached;
        return null;
    }

    /// <summary>
    /// La risposta scritta nella notifica in una finestra di scrittura: Rispondi al messaggio con quel testo sopra la firma;
    /// senza messaggio (non trovato) un messaggio nuovo dell'account. Dall'audit, blocco 10 (S-21), è l'unica strada: la
    /// risposta con un testo non parte mai da sola, come faceva con le notifiche che avevano la casella. Il testo conta come
    /// scritto: chiudendo si chiede se salvare la bozza.
    /// </summary>
    void OpenQuickReplyWindow(AccountItem account, MessageSummary? original, string text, string note)
    {
        ShowFromTray();
        var window = new ComposeWindow(Providers.ToList(), account.Provider, original != null ? ComposeMode.Reply : ComposeMode.New, original, null) { Owner = this };
        window.PutText(text, note);
        ShowCompose(window);
        window.Activate();
    }

    /// <summary>
    /// Rispondi dalla notifica: subito la finestra della risposta, come Rispondi nel riquadro (destinatari, Da, firma e
    /// citazione li mette lei). Con la finestra principale nascosta nell'area di notifica si apre solo la risposta, al centro
    /// dello schermo: la finestra principale non si mostra.
    /// </summary>
    void OpenReplyFromNotification(AccountItem account, MessageSummary original)
    {
        var window = new ComposeWindow(Providers.ToList(), account.Provider, ComposeMode.Reply, original, null);
        if (IsVisible) window.Owner = this;
        else window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        ShowCompose(window);
        window.Activate();
        Log.Write($"risposta dalla notifica: aperta la scrittura per il messaggio {original.Id} di {account.Provider.Account.Email}");
    }

    /// <summary>
    /// "Posponi..." dal menu della notifica: la scelta di giorno e ora di Posponi (SnoozeWindow), come dal menu del messaggio.
    /// Con la finestra principale nascosta nell'area di notifica si apre solo la scelta, al centro dello schermo.
    /// </summary>
    async Task PickSnoozeFromNotificationAsync(AccountItem account, MessageSummary message)
    {
        var dialog = new SnoozeWindow();
        if (IsVisible) dialog.Owner = this;
        else dialog.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        dialog.Activate();
        if (dialog.ShowDialog() == true) await SnoozeFromNotificationAsync(account, message, dialog.Until);
    }

    /// <summary>Contrassegna dalla notifica, come la bandierina: il messaggio resta dov'è, contrassegnato.</summary>
    async Task FlagFromNotificationAsync(AccountItem account, MessageSummary message)
    {
        try
        {
            await account.Provider.SetFlagAsync(message, true);
            message.IsFlagged = true;
            foreach (var row in rows.Rows.Where(r => r.Account == account && r.Summary.Id == message.Id && r.Summary.FolderId == message.FolderId)) row.IsFlagged = true;
            Log.Write($"contrassegnato dalla notifica il messaggio {message.Id} di {account.Provider.Account.Email}");
        }
        catch (Exception ex)
        {
            Log.Error("contrassegna dalla notifica", ex);
            Tell(T("Messaggio non contrassegnato"), ex.Message);
        }
    }

    /// <summary>
    /// La risposta scritta nella notifica, pronta per il provider (sul thread dell'interfaccia: FlowDocument). Dal blocco 10
    /// (S-21) l'app non la invia più da una notifica: resta per le prove della risposta dei provider (destinatari, citazione).
    /// </summary>
    internal static OutgoingMessage QuickReply(MailAccount account, MessageSummary original, string text)
    {
        var (to, _) = Replies.Recipients(original, null, account.Email, ComposeMode.Reply);
        // L'oggetto come Rispondi: "R: " in italiano, "RE: " in inglese, e un prefisso delle due lingue già presente resta.
        var subject = ComposeWindow.SubjectFor(ComposeMode.Reply, original.Subject);
        // Come Rispondi: dall'indirizzo a cui era scritto (un alias, info@), con la firma di quell'indirizzo.
        var identity = SendIdentity.For(account, original);
        var (html, plain, images) = QuickReplyBody(text, Store.Settings.SignatureFor(account, reply: true, identity));
        return new OutgoingMessage { Mode = ComposeMode.Reply, Original = original, To = to, Subject = subject, Html = html, Text = plain, Images = images, From = identity };
    }

    /// <summary>
    /// Il testo della risposta veloce come lo spedirebbe la finestra di scrittura (FlowToHtml): i paragrafi, una riga vuota e
    /// la firma, con le sue immagini.
    /// </summary>
    static (string Html, string Text, List<InlineImage> Images) QuickReplyBody(string text, Signature? signature)
    {
        var document = new FlowDocument();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n')) document.Blocks.Add(new Paragraph(new Run(line)));
        if (signature != null && RichEditor.BlocksFromRtf(signature.RtfFor(reply: true)) is { Count: > 0 } blocks)
        {
            document.Blocks.Add(new Paragraph());
            document.Blocks.AddRange(blocks);
        }
        var images = new List<InlineImage>();
        var html = FlowToHtml.Convert(document, images);
        return (html, new TextRange(document.ContentStart, document.ContentEnd).Text.TrimEnd(), images);
    }

    /// <summary>Posponi dalla notifica, come dal menu: nella cartella Posposti, e all'ora scelta torna in Posta in arrivo.</summary>
    async Task SnoozeFromNotificationAsync(AccountItem account, MessageSummary message, DateTime until)
    {
        var provider = account.Provider;
        bool creating = !account.Folders.Any(IsSnoozeFolder);
        try
        {
            var target = await provider.EnsureFolderAsync(SnoozeFolderName(account));
            var key = await provider.SnoozeAsync(message, target);
            S.Snoozed.Add(new SnoozedMessage { AccountId = provider.Account.Id, Key = key, FolderId = target.Id, Until = until, Subject = message.Subject, From = message.From.Display });
            Store.Save();
            Log.Write($"posposto dalla notifica il messaggio {message.Id} di {provider.Account.Email} fino a {until:dd/MM/yyyy HH:mm}");
            BodyCache.Forget(provider.Account, [message]);
            RemoveNotifiedRow(account, message);
            if (creating) await LoadFoldersAsync(account);
        }
        catch (Exception ex)
        {
            Log.Error("posponi dalla notifica", ex);
            Tell(T("Messaggio non posposto"), ex.Message);
        }
    }

    /// <summary>
    /// Elimina dalla notifica: nel Cestino dell'account, come Elimina (mai un'eliminazione definitiva, nemmeno se il messaggio
    /// fosse già nel Cestino). Senza Cestino (IMAP) il provider lo crea, come per Elimina.
    /// </summary>
    async Task DeleteFromNotificationAsync(AccountItem account, MessageSummary message)
    {
        try
        {
            var trash = Special(account, FolderKind.Deleted) ?? await account.Provider.EnsureTrashAsync();
            if (trash == null)
            {
                Tell(T("Messaggio non eliminato"), T("L'account {0} non ha un Cestino.", account.Header));
                return;
            }
            if (message.FolderId == trash.Id) return;
            var moved = await account.Provider.MoveAsync([message], trash);
            Log.Write($"eliminato dalla notifica il messaggio {message.Id} di {account.Provider.Account.Email} (nel {trash.Name})");
            BodyCache.Forget(account.Provider.Account, [message]);
            FollowFlags(account, moved, trash.Id);
            RemoveNotifiedRow(account, message);
        }
        catch (Exception ex)
        {
            Log.Error("elimina dalla notifica", ex);
            Tell(T("Messaggio non eliminato"), ex.Message);
        }
    }

    /// <summary>
    /// I cambiamenti della Posta in arrivo di Graph fatti altrove (query delta, ogni 30 secondi): le righe di quei messaggi
    /// cambiano da lette a da leggere e viceversa, quelle dei messaggi eliminati o spostati altrove escono dall'elenco.
    /// </summary>
    public void ApplyInboxChanges(IMailProvider provider, InboxChanges changes)
    {
        foreach (var window in FolderWindows) window.ApplyInboxChanges(provider, changes);
        if (accounts.FirstOrDefault(a => a.Provider == provider) is not { } account) return;
        var read = changes.Seen.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Last().Read);
        var removed = changes.Removed.ToHashSet();
        foreach (var row in rows.Rows.Where(r => r.Account == account).ToList())
        {
            if (read.TryGetValue(row.Summary.Id, out var isRead) && row.IsRead != isRead) row.IsRead = isRead;
            if (row.Count == 1 && removed.Contains(row.Summary.Id))
            {
                if (Reader.Selected?.Id == row.Summary.Id) Reader.Clear();
                rows.Remove(row);
            }
        }
    }

    /// <summary>
    /// Dopo un avviso dell'IDLE sui segni o sui messaggi usciti (letti o spostati dal telefono), la Posta in arrivo
    /// dell'account, se è quella aperta, si rilegge dal server.
    /// </summary>
    public void RefreshInboxOf(IMailProvider provider)
    {
        foreach (var window in FolderWindows) window.RefreshInboxOf(provider);
        if (folder is { Folder.Kind: FolderKind.Inbox } open && open.Account.Provider == provider && !searching && !loading) _ = RefreshListAsync();
    }

    /// <summary>
    /// Un segno cambiato altrove che l'IDLE dice con l'UID (letto o contrassegnato dal telefono, dal sito): cambia solo la
    /// riga della Posta in arrivo, senza rileggere; i non letti li riconta il controllo che segue.
    /// </summary>
    public void ApplyIdleFlags(IMailProvider provider, string id, bool? read, bool? flagged)
    {
        foreach (var window in FolderWindows) window.ApplyIdleFlags(provider, id, read, flagged);
        if (accounts.FirstOrDefault(a => a.Provider == provider) is not { } account) return;
        var inbox = Special(account, FolderKind.Inbox)?.Id ?? "INBOX";
        foreach (var row in rows.Rows.Where(r => r.Account == account && r.Summary.Id == id && r.Summary.FolderId.Equals(inbox, StringComparison.OrdinalIgnoreCase)))
        {
            if (read is { } r && row.IsRead != r) row.IsRead = r;
            if (flagged is { } f && row.IsFlagged != f) row.IsFlagged = f;
        }
    }

    /// <summary>I messaggi che le regole di Missiva hanno spostato o eliminato: le loro righe escono dall'elenco, se ci sono.</summary>
    public void RemoveRuledRows(IMailProvider provider, IEnumerable<MessageSummary> messages)
    {
        messages = messages.ToList();
        foreach (var window in FolderWindows) window.RemoveRuledRows(provider, messages);
        if (accounts.FirstOrDefault(a => a.Provider == provider) is not { } account) return;
        foreach (var m in messages) RemoveNotifiedRow(account, m);
    }

    /// <summary>La riga del messaggio esce dall'elenco, se c'è da sola (una conversazione resta: la ricarica il controllo dopo).</summary>
    void RemoveNotifiedRow(AccountItem account, MessageSummary message)
    {
        foreach (var row in rows.Rows.Where(r => r.Account == account && r.Count == 1 && r.Summary.Id == message.Id && r.Summary.FolderId == message.FolderId).ToList())
        {
            if (Reader.Selected?.Id == message.Id) Reader.Clear();
            rows.Remove(row);
        }
    }
}
