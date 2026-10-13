using System.Windows;
using System.Windows.Interop;

namespace Missiva.UI;

/// <summary>
/// Le risposte automatiche degli account Microsoft, come il nuovo Outlook: la finestra (menu dell'account, Impostazioni)
/// e la barra sotto l'account quando sono attive o programmate, con Disattiva. Si leggono solo se il consenso alle
/// impostazioni della casella c'è già: senza, nessuna richiesta e nessuna finestra, finché non si preme il pulsante
/// "Consenti l'accesso alle impostazioni della casella" nella finestra.
/// </summary>
public partial class MainWindow
{
    /// <summary>Lo stato delle risposte automatiche per la barra, dopo le cartelle dell'account (senza consenso non chiede nulla).</summary>
    async Task LoadAutoRepliesAsync(AccountItem account)
    {
        if (!account.Provider.HasAutoReplies) return;
#if DEBUG
        if (AutoRepliesWindow.TestSample != null) return; // --risposte-finte: restano quelle finte
#endif
        try
        {
            account.AutoReplies = await account.Provider.GetAutoRepliesAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"risposte automatiche di {account.Header}", ex);
        }
    }

    void OnAutoRepliesEdit(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountItem item }) _ = EditAutoRepliesAsync(item.Provider.Account, this);
    }

    /// <summary>Disattiva della barra: spente, con i messaggi tenuti per la prossima volta (nella prova solo nel registro).</summary>
    async void OnAutoRepliesOff(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AccountItem item } || item.AutoReplies is not { } current) return;
        try
        {
            var off = current with { State = AutoReplyState.Off };
            await item.Provider.SetAutoRepliesAsync(off);
            item.AutoReplies = off;
        }
        catch (Exception ex)
        {
            Log.Error("risposte automatiche non spente", ex);
            Dialog.Show(this, "Risposte automatiche non spente", ex.Message);
        }
    }

    /// <summary>La finestra delle risposte automatiche di un account Microsoft (menu dell'account, barra, Impostazioni).</summary>
    public async Task EditAutoRepliesAsync(MailAccount account, Window owner)
    {
        if (accounts.FirstOrDefault(a => a.Provider.Account.Id == account.Id) is not { } item || !item.Provider.HasAutoReplies) return;
        var window = new AutoRepliesWindow(item.Provider) { Owner = owner };
        var saved = window.ShowDialog() == true;
        if (saved) item.AutoReplies = window.Saved;
        else if (window.Found is { } found) item.AutoReplies = found; // lette nella finestra (anche dopo il consenso appena dato)
        if (window.ConsentGiven) await LoadCategoriesAsync(item); // con il consenso arrivano anche le categorie di Outlook
    }
}
