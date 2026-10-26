using System.Windows;

namespace Missiva.UI;

/// <summary>
/// Posta inviata dopo un invio, e due finestre sulla stessa cartella (audit di affidabilità del 25 ottobre 2026, elenco, E-13).
/// Dopo Invia si avvisavano solo le Bozze: la Posta inviata aperta restava senza il messaggio appena partito. E le finestre di
/// una sola cartella ("Apri in un'altra finestra") ricevevano dalla principale arrivi, segni e riletture, ma non Elimina, Sposta
/// e letto fatti in un'altra finestra: la stessa cartella in due finestre aveva righe diverse fino alla rilettura.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Quanto dopo un invio la Posta inviata aperta si rilegge: Gmail e IMAP (che Missiva copia lì) ce l'hanno subito, Exchange
    /// la salva qualche secondo dopo la risposta all'invio.
    /// </summary>
    static readonly TimeSpan SentSettle = TimeSpan.FromSeconds(3);

    /// <summary>Un messaggio è partito dall'account del provider: in ogni finestra la Posta inviata aperta, sua o di tutti gli account, si rilegge poco dopo.</summary>
    void OnSent(IMailProvider provider)
    {
        foreach (var window in Application.Current.Windows.OfType<MainWindow>()) window.RefreshSentLater(provider);
    }

    /// <summary>Dopo SentSettle la Posta inviata aperta in questa finestra, dell'account o di tutti gli account, si rilegge.</summary>
    async void RefreshSentLater(IMailProvider provider)
    {
        try
        {
            await Task.Delay(SentSettle);
            if (searching) return;
            if (folder is { Folder.Kind: FolderKind.Sent } sent && sent.Account.Provider.Account.Id == provider.Account.Id) await RefreshListAsync("messaggio inviato");
            else if (folder == null && unified is { View: UnifiedView.Sent } && accounts.Any(a => a.Provider.Account.Id == provider.Account.Id)) await RefreshListAsync("messaggio inviato");
        }
        catch (Exception ex)
        {
            Log.Error("Posta inviata dopo un invio", ex);
        }
    }

    /// <summary>Le altre finestre di Missiva con un elenco (la principale e quelle di una sola cartella).</summary>
    IEnumerable<MainWindow> OtherWindows => Application.Current?.Windows.OfType<MainWindow>().Where(w => w != this).ToList() ?? [];

    /// <summary>
    /// Messaggi spostati o eliminati da questa finestra (riuscito o in coda): escono anche dagli elenchi delle altre finestre,
    /// se ci sono. Prima solo da quella in cui si era fatto.
    /// </summary>
    void ShareRemoved(AccountItem account, IReadOnlyList<MessageSummary> messages)
    {
        if (messages.Count == 0) return;
        foreach (var window in OtherWindows) window.RemoveShared(account.Provider.Account.Id, messages);
    }

    /// <summary>I messaggi tolti in un'altra finestra: le loro righe escono da questa (MessageRows.RemoveMessage).</summary>
    internal void RemoveShared(string accountId, IReadOnlyList<MessageSummary> messages)
    {
        if (accounts.FirstOrDefault(a => a.Provider.Account.Id == accountId) is not { } account) return;
        journal.Removed(accountId, messages);
        foreach (var m in messages)
            if (rows.RemoveMessage(account, m.FolderId, m.Id, ByDateNewest) && Reader.Selected is { } shown && shown.Id == m.Id && shown.FolderId == m.FolderId) Reader.Clear();
    }

    /// <summary>
    /// Messaggi segnati letti o da leggere in questa finestra: le righe delle altre finestre cambiano anche loro (solo le righe:
    /// i numeri li rimette il riconteggio dal server, UnreadSync, come per le finestre di una cartella).
    /// </summary>
    void ShareRead(AccountItem account, IReadOnlyList<MessageSummary> messages, bool read)
    {
        if (messages.Count == 0) return;
        foreach (var window in OtherWindows) window.ReadShared(account.Provider.Account.Id, account.Provider.Account.Kind, messages, read);
    }

    /// <summary>I messaggi segnati in un'altra finestra: le loro righe qui (per messaggio, ReadMarks.Matches).</summary>
    internal void ReadShared(string accountId, AccountKind kind, IReadOnlyList<MessageSummary> messages, bool read)
    {
        var same = ReadMarks.Matches(kind, messages);
        foreach (var row in rows.Rows.Where(r => r.Account.Provider.Account.Id == accountId && same(r.Summary)))
            if (row.IsRead != read) row.IsRead = read;
    }
}
