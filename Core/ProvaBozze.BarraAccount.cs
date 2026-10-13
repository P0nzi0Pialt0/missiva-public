#if DEBUG
using System.ComponentModel;
using Missiva.UI;

namespace Missiva;

/// <summary>
/// Barra laterale (1 ottobre 2026, agente rifiniture-I, richiesta dell'autore: con gli account chiusi nessuna riga era evidenziata): l'intestazione
/// di un account chiuso con la cartella aperta si evidenzia (<see cref="AccountItem.HeaderSelected"/>), con l'account aperto no (resta la
/// cartella), si accende e si spegne subito aprendo e chiudendo, cambiando cartella o account, durante la ricerca nessuna, e lo stesso per
/// Tutti gli account e per i Preferiti (<see cref="SidebarMarks"/>). Il disegno vero (fondo grigio e barretta) si guarda nelle foto.
/// </summary>
static partial class ProvaBozze
{
    static void SidebarMarksTests()
    {
        Report.Add("--- Barra laterale: intestazione dell'account chiuso con la cartella aperta");
        var collapsed = Store.Settings.CollapsedAccounts;
        var before = collapsed.ToList();
        try
        {
            var a = new MailAccount { Kind = AccountKind.Gmail, Email = "ada.prova@gmail.com" };
            var b = new MailAccount { Kind = AccountKind.Microsoft, Email = "alex.prova@outlook.it" };
            var c = new MailAccount { Kind = AccountKind.Gmail, Email = "famiglia.prova.lunga@gmail.com" };
            foreach (var x in new[] { a, b, c }) collapsed.Remove(x.Id);
            using var ga = new GmailProvider(a);
            var gb = new GraphProvider(b);
            using var gc = new GmailProvider(c);
            var accounts = new List<AccountItem> { new(ga), new(gb), new(gc) };
            FolderItem Inbox(AccountItem account) => new(account, new MailFolder { Id = "INBOX", Name = "Posta in arrivo", Kind = FolderKind.Inbox });
            var inboxes = accounts.Select(Inbox).ToList();
            string Marks() => string.Join("", accounts.Select(x => x.HeaderSelected ? "X" : "-"));
            string Folders() => string.Join("", accounts.Select(x => x.HasSelection ? "X" : "-"));

            var raised = new List<string>();
            foreach (var x in accounts) x.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

            Check(Marks() == "---" && Folders() == "---", "all'inizio nessuna intestazione evidenziata");
            SidebarMarks.Mark(accounts, inboxes[0]);
            Check(Folders() == "X--" && Marks() == "---", "cartella del primo account aperta, account aperto: la segna solo il primo e l'intestazione NON si evidenzia (lo e' la cartella)");

            raised.Clear();
            accounts[0].IsExpanded = false;
            Check(Marks() == "X--" && raised.Contains(nameof(AccountItem.HeaderSelectedVisibility)) && accounts[0].HeaderSelectedVisibility == System.Windows.Visibility.Visible,
                  "chiudendo l'account con la cartella aperta l'intestazione si evidenzia subito (notifica HeaderSelectedVisibility)");
            raised.Clear();
            accounts[0].IsExpanded = true;
            Check(Marks() == "---" && raised.Contains(nameof(AccountItem.HeaderSelectedVisibility)) && accounts[0].HeaderSelectedVisibility == System.Windows.Visibility.Collapsed,
                  "riaprendolo si spegne subito, e resta la cartella (niente doppio segno)");

            // Cambiando cartella o account (anche da una notifica) l'evidenziazione segue la cartella aperta.
            foreach (var x in accounts) x.IsExpanded = false;
            SidebarMarks.Mark(accounts, inboxes[0]);
            Check(Marks() == "X--", "tre account chiusi, cartella del primo: solo la sua intestazione");
            raised.Clear();
            SidebarMarks.Mark(accounts, inboxes[2]);
            Check(Marks() == "--X" && Folders() == "--X" && raised.Count(r => r == nameof(AccountItem.HeaderSelectedVisibility)) == 2,
                  "cambiando account passa all'altra intestazione (due notifiche: una si spegne, una si accende)");
            accounts[2].IsExpanded = true;
            Check(Marks() == "---", "aprendo il terzo account l'intestazione si spegne anche se gli altri restano chiusi");
            accounts[2].IsExpanded = false;

            // Durante la ricerca nessuna cartella e' selezionata, come in Outlook: nessuna evidenziazione, anche con tutti chiusi.
            SidebarMarks.Mark(accounts, null);
            Check(Marks() == "---" && Folders() == "---", "durante la ricerca (nessuna cartella) nessuna intestazione e' evidenziata");
            SidebarMarks.Mark(accounts, inboxes[2]);
            Check(Marks() == "--X", "chiusa la ricerca torna quella della cartella di prima");

            // Una vista di Tutti gli account non e' di un account: nessuna intestazione di account.
            SidebarMarks.Mark(accounts, null);
            var unified = new UnifiedItem(UnifiedView.Inbox);
            Check(SidebarMarks.UnifiedHeader(true, unified), "Tutti gli account chiuso con una sua vista aperta: l'intestazione si evidenzia");
            Check(!SidebarMarks.UnifiedHeader(false, unified), "Tutti gli account aperto: si evidenzia la vista, non l'intestazione");
            Check(!SidebarMarks.UnifiedHeader(true, null), "Tutti gli account chiuso senza vista aperta (una cartella, o la ricerca): nessuna evidenziazione");
            Check(Marks() == "---", "con una vista di Tutti gli account aperta nessun account e' evidenziato");

            // Preferiti: solo con il gruppo chiuso e la cartella aperta fra i Preferiti.
            var favorites = new List<FolderItem> { inboxes[2] };
            Check(SidebarMarks.FavoritesHeader(true, inboxes[2], favorites), "Preferiti chiuso con la cartella aperta fra i suoi: intestazione evidenziata");
            Check(!SidebarMarks.FavoritesHeader(false, inboxes[2], favorites), "Preferiti aperto: si evidenzia la cartella, non l'intestazione");
            Check(!SidebarMarks.FavoritesHeader(true, inboxes[0], favorites), "Preferiti chiuso ma la cartella aperta non e' un Preferito: nessuna evidenziazione");
            Check(!SidebarMarks.FavoritesHeader(true, null, favorites), "Preferiti chiuso durante la ricerca o in una vista di tutti gli account: nessuna evidenziazione");
            SidebarMarks.Mark(accounts, inboxes[2]);
            Check(Marks() == "--X" && SidebarMarks.FavoritesHeader(true, inboxes[2], favorites),
                  "cartella fra i Preferiti con Preferiti e account chiusi: tutte e due le intestazioni, come con tutte e due aperte lo sono le due righe");

            // Contatore sull'intestazione (2 ottobre 2026, richiesta dell'autore: con gli account chiusi non si sapeva quale ha posta da leggere):
            // i non letti della Posta in arrivo dell'account, solo con l'account chiuso.
            Report.Add("--- Barra laterale: contatore dei non letti sull'intestazione dell'account");
            foreach (var x in accounts) x.IsExpanded = true;
            MailFolder Folder(string id, FolderKind kind, int unread) => new() { Id = id, Name = id, Kind = kind, Unread = unread };
            accounts[0].SetFolders(new[] { Folder("INBOX", FolderKind.Inbox, 3), Folder("Sent", FolderKind.Sent, 7), Folder("Spam", FolderKind.Junk, 9) });
            accounts[1].SetFolders(new[] { Folder("inbox", FolderKind.Inbox, 120) });
            accounts[2].SetFolders(new[] { Folder("INBOX", FolderKind.Inbox, 0), Folder("Clienti", FolderKind.Other, 4) });
            string Counts() => string.Join("|", accounts.Select(x => x.HeaderUnreadText));
            Check(Counts() == "||" && accounts.All(x => x.HeaderUnreadVisibility == System.Windows.Visibility.Collapsed),
                  "con gli account aperti nessun numero sull'intestazione (lo mostra la cartella)");
            Check(accounts[0].InboxUnread == 3 && accounts[1].InboxUnread == 120 && accounts[2].InboxUnread == 0,
                  "InboxUnread conta solo la Posta in arrivo: non Posta inviata, Posta indesiderata o altre cartelle");
            raised.Clear();
            foreach (var x in accounts) x.IsExpanded = false;
            Check(Counts() == "3|120|" && accounts[0].HeaderUnreadVisibility == System.Windows.Visibility.Visible && accounts[2].HeaderUnreadVisibility == System.Windows.Visibility.Collapsed
                  && raised.Contains(nameof(AccountItem.HeaderUnreadText)) && raised.Contains(nameof(AccountItem.HeaderUnreadVisibility)),
                  "chiudendo gli account il numero compare subito sull'intestazione, e non per chi non ha non letti");
            Check(accounts[0].HeaderAutomationName.EndsWith(", 3 non letti") && accounts[1].HeaderAutomationName.EndsWith(", 120 non letti") && accounts[2].HeaderAutomationName == accounts[2].HeaderTip,
                  "il nome per UI Automation dice i non letti solo con il numero in vista");
            raised.Clear();
            var firstInbox = accounts[0].Folders.First(f => f.Folder.Kind == FolderKind.Inbox);
            firstInbox.Unread = 1;
            Check(accounts[0].HeaderUnreadText == "1" && accounts[0].HeaderAutomationName.EndsWith(", 1 non letto") && raised.Contains(nameof(AccountItem.HeaderUnreadText)),
                  "leggendo un messaggio il numero scende subito (e al singolare: 1 non letto)");
            raised.Clear();
            accounts[0].Folders.First(f => f.Folder.Kind == FolderKind.Sent).Unread = 2;
            Check(raised.Count == 0 && accounts[0].HeaderUnreadText == "1", "cambiano i non letti di un'altra cartella: l'intestazione non cambia e non notifica");
            firstInbox.Unread = 0;
            Check(accounts[0].HeaderUnreadText == "" && accounts[0].HeaderUnreadVisibility == System.Windows.Visibility.Collapsed, "senza non letti il numero sparisce");
            accounts[0].SetFolders(new[] { Folder("INBOX", FolderKind.Inbox, 12), Folder("Sent", FolderKind.Sent, 7) });
            Check(accounts[0].HeaderUnreadText == "12", "le cartelle rilette dal server aggiornano il numero (stesso elemento della cartella)");
            accounts[2].SetFolders(new[] { Folder("INBOX", FolderKind.Inbox, 5), Folder("Clienti", FolderKind.Other, 4) });
            Check(accounts[2].HeaderUnreadText == "5", "una Posta in arrivo che prima non aveva non letti e adesso ne ha: il numero compare");
            foreach (var x in accounts) x.IsExpanded = true;
            Check(Counts() == "||", "riaprendo gli account il numero sparisce di nuovo");
        }
        finally
        {
            collapsed.Clear();
            collapsed.AddRange(before);
        }
    }
}
#endif
