namespace Missiva.UI;

/// <summary>
/// La cartella aperta quando la sua riga non si vede perché il gruppo che la contiene è chiuso (richiesta dell'autore, 1 ottobre 2026:
/// con gli account tutti chiusi nella barra laterale nessuna riga era evidenziata e non si capiva dove si era). L'intestazione del
/// gruppo chiuso prende l'aspetto delle cartelle scelte (stile NavItem: fondo grigio e barretta del colore principale); con il gruppo
/// aperto resta evidenziata solo la cartella, mai un doppio segno. Durante la ricerca nessuna cartella è scelta (come in Outlook), quindi
/// nessuna intestazione si evidenzia. Una cartella dei Preferiti si vede anche nel suo account: se tutti e due i gruppi sono chiusi si
/// evidenziano tutti e due, come con tutti e due aperti lo sono le due righe.
/// </summary>
public static class SidebarMarks
{
    /// <summary>Segna su ogni account se la cartella aperta è sua (null: ricerca o vista di tutti gli account, nessuna).</summary>
    public static void Mark(IEnumerable<AccountItem> accounts, FolderItem? selected)
    {
        foreach (var account in accounts) account.HasSelection = selected != null && selected.Account == account;
    }

    /// <summary>L'intestazione dei Preferiti: gruppo chiuso con la cartella aperta fra i suoi.</summary>
    public static bool FavoritesHeader(bool collapsed, FolderItem? selected, IEnumerable<FolderItem> favorites) =>
        collapsed && selected != null && favorites.Contains(selected);

    /// <summary>L'intestazione di Tutti gli account: gruppo chiuso con una sua vista aperta.</summary>
    public static bool UnifiedHeader(bool collapsed, UnifiedItem? selected) => collapsed && selected != null;
}
