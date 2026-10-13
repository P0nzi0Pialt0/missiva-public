namespace Missiva;

/// <summary>
/// I numeri che più file devono avere uguali, scritti una volta (audit dell'11 ottobre, C-38). Prima ogni provider ne
/// aveva la sua copia: correggerne una sola avrebbe creato una differenza che nessuno aveva scelto (è già successo con il
/// tempo scaduto delle connessioni, C-09). Qui solo quelli che hanno lo stesso significato dappertutto; due numeri
/// uguali per caso restano dove sono. Sono costanti: con quell'audit nessun valore è cambiato; dopo, solo dove è detto.
/// </summary>
static class MailLimits
{
    /// <summary>Messaggi per pagina dell'elenco e della ricerca (Gmail, IMAP, Graph).</summary>
    public const int PageSize = 50;

    /// <summary>Corpi dei messaggi aperti tenuti in memoria da un provider (Gmail, IMAP): pochi, perché con le immagini incorporate pesano.</summary>
    public const int BodyCacheSize = 20;

    /// <summary>Sotto questa dimensione, in byte, un messaggio si scarica intero: una richiesta sola, invece di una per parte (Gmail, IMAP).</summary>
    public const uint SmallMessage = 300_000;

    /// <summary>Gli allegati di una bozza fino a qui restano in memoria fra un salvataggio e l'altro; quelli più grandi si riscaricano (Gmail, IMAP).</summary>
    public const long KeepAttachment = 5 * 1024 * 1024;

    /// <summary>
    /// Il tempo scaduto di MailKit, in millisecondi, sulle connessioni degli account IMAP e sul loro SMTP: quanto può
    /// restare muto il server durante un comando prima che la connessione si dia per morta. Scritto così dal primo giorno
    /// degli account IMAP (26 settembre 2026).
    /// </summary>
    public const int ImapSilenceMs = 60_000;

    /// <summary>
    /// Lo stesso per Gmail (le sue connessioni IMAP, la prova dell'accesso e il suo SMTP): è il valore di base di MailKit,
    /// che fino all'audit dell'11 ottobre (C-09) valeva perché nessuno ne scriveva un altro. I due numeri sono diversi
    /// senza che nessuno l'abbia scelto: qui hanno un nome, uno accanto all'altro, e valgono come prima. Dopo una
    /// sospensione una connessione morta tiene ferma la principale di un account Gmail fino a due minuti, di un account
    /// IMAP uno; App.StuckCheck (due minuti) per Gmail scade insieme a questo, non dopo. Quale debba valere per tutti e
    /// due è una scelta dell'autore (ESITO.md).
    /// </summary>
    public const int GmailSilenceMs = 120_000;

    /// <summary>
    /// Per quanti secondi dal suo inizio una ricerca già fatta vale ancora per chi la richiede con le stesse parole: Invio
    /// dopo i suggerimenti (SuggestSearches.Fresh, tutti gli account) e la ricerca in tutte le cartelle di un account IMAP
    /// (ImapProvider.ReuseSearch). Dopo si rifà: intanto può essere arrivata posta. Fino alle scelte del 12 ottobre (C-39)
    /// erano due numeri, 30 e 60: fra 30 e 60 secondi Invio "rifaceva" la ricerca e su IMAP riceveva quella di prima.
    /// </summary>
    public const int SearchReuseSeconds = 30;

    /// <summary>Alla chiusura di una connessione, i secondi che si aspetta la richiesta in corso prima del LOGOUT (Gmail, IMAP; C-53).</summary>
    public const int CloseGraceSeconds = 5;

    /// <summary>I secondi che il LOGOUT in sottofondo può durare (Gmail, IMAP).</summary>
    public const int LogoutSeconds = 5;

    /// <summary>Il tempo scaduto, in secondi, delle richieste HTTP a Google: le API (calendario, contatti) e il token.</summary>
    public const int GoogleHttpSeconds = 30;

    /// <summary>I minuti che Missiva aspetta un accesso di Google lasciato a metà nel browser (aggiunta, Gestisci account, un permesso in più).</summary>
    public const int BrowserSignInMinutes = 10;

    /// <summary>I secondi della prova del collegamento con un server IMAP e SMTP (aggiunta e Gestisci account).</summary>
    public const int ServerTrySeconds = 45;
}
