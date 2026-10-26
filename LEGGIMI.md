# Missiva 0.3

[English](MANUAL.md)

Client di posta per Windows 11, sul modello del nuovo Outlook ma nativo: interfaccia WPF con lo stile Fluent di
Windows, scrittura con editor nativo e controllo ortografico nella lingua di Missiva, italiano o inglese (sotto,
"Lingua"). Solo il contenuto delle email è impaginato da
WebView2, chiuso: niente JavaScript, niente Internet, come un visualizzatore e non come un browser. Account supportati:
Outlook.com, Hotmail e Live (account Microsoft personali), Microsoft 365 di lavoro o della scuola (con l'app di Missiva o
con quella registrata dall'organizzazione), Gmail e Google Workspace, e gli altri account IMAP e SMTP (caselle su
dominio proprio: Aruba, Register.it, OVH, Zoho, cPanel, Exchange con IMAP).

Aggiungi account parte dall'indirizzo email, come Outlook e Thunderbird: Missiva riconosce da sola il tipo di account
(dal dominio, e per i domini propri dal server di posta) e lo propone; si può sempre sceglierlo a mano.

## Installare

Da un rilascio pubblicato su GitHub (la pagina Releases): si scarica `Missiva-0.3.N.zip`, si estrae in una cartella e
da lì si lancia `powershell -ExecutionPolicy Bypass -File installa.ps1`. Non serve altro: lo script vede che accanto
c'è `Missiva.exe` e non i sorgenti, prova l'eseguibile e lo installa, anche con il Windows PowerShell 5.1 di serie.
Missiva finisce in `%LOCALAPPDATA%\Programs\Missiva`, compare nel menu Start e in Impostazioni, App installate (da dove
si disinstalla), e da lì si aggiorna da sola (sotto, "Aggiornamenti"). Senza un certificato di firma del codice, la
prima apertura di un file scaricato può far dire a Windows che "ha protetto il PC" (Ulteriori informazioni, Esegui
comunque).

Dai sorgenti servono .NET 10 SDK e PowerShell 7 (il comando `pwsh`: su Windows non c'è di serie, si installa dal
Microsoft Store o con `winget install Microsoft.PowerShell`). Dalla cartella dei sorgenti: `pwsh -ExecutionPolicy
Bypass -File installa.ps1`; il risultato è lo stesso, ma una Missiva compilata così non si aggiorna da sola: per
aggiornarla si rilancia lo stesso comando.

Aggiornamenti: due minuti dopo l'avvio e poi una volta al giorno una Missiva installata da un rilascio chiede a GitHub
se c'è una versione nuova. Se c'è, nella barra del titolo, a destra, compare il pulsante **Aggiornamento**: un clic apre
"Missiva 0.3.N del gg/mm/aaaa" con le novità, Aggiorna e Più tardi (per quella versione torna il giorno dopo). Aggiorna
scarica la versione nuova (l'avanzamento è nel pulsante, con la percentuale, e nel pannellino, con Annulla), la
controlla, chiede conferma e poi chiude Missiva come Esci (i messaggi che stai scrivendo chiedono se salvare la bozza) e
la riapre aggiornata. Se qualcosa non va, il pulsante dice "Aggiornamento non riuscito" e il pannellino il motivo. In Impostazioni,
Generali, Informazioni ci sono la versione, la casella **Controlla da solo gli aggiornamenti** (spenta, nessuna
richiesta a GitHub), **Controlla ora** con l'esito dell'ultimo controllo; in una Missiva compilata dai sorgenti, al
loro posto, che si aggiorna con `installa.ps1`. Che cosa controlla prima di accettare un aggiornamento e che cosa vede
GitHub: `docs/aggiornamenti.md`.

La versione cambia a ogni modifica: i primi due numeri si scrivono a mano nel progetto, il terzo è il numero delle
modifiche (i commit di git) e cresce da solo, e accanto ci sono la data e il codice dell'ultima modifica compresa, per
esempio "0.3.526 del 14/10/2026 (1a2b3c4)". La dicono `installa.ps1` (anche quella che c'era prima), Impostazioni,
Generali, Informazioni, App installate e la prima riga del registro a ogni avvio. Compilata da una copia senza git (uno
ZIP) resta "0.3.0".

Per aprire con Missiva i link di posta (mailto:) delle pagine web e dei PDF, dopo l'installazione sceglila in
Impostazioni, App, App predefinite, Missiva, MAILTO: Windows non lascia farlo ai programmi.

## Lingua

Missiva è in italiano e in inglese. La lingua si sceglie in Impostazioni, Generali, Aspetto, "Lingua (Language)": Come
Windows (di base: italiano se Windows è in italiano, inglese altrimenti), Italiano o English. Vale dal prossimo avvio:
Riavvia ora la riapre subito nella lingua nuova. Con la lingua cambiano:

- tutta l'interfaccia: finestre, menu, avvisi e domande, notifiche, la pagina del riquadro di lettura;
- le date, i mesi e i giorni: in inglese con i formati di Windows se Windows usa quelli inglesi (americani,
  britannici...), altrimenti con quelli britannici;
- il controllo ortografico della scrittura;
- quello che parte con i messaggi: il prefisso dell'oggetto delle risposte e degli inoltri (R: e I: in italiano, RE: e
  FW: in inglese; un prefisso già presente, nell'una o nell'altra lingua, non si ripete) e, con gli account Gmail e IMAP,
  la riga sopra il messaggio citato ("Il mercoledì 21 ottobre 2026 14:30, Mario Rossi <...> ha scritto:" oppure "On
  Wednesday, 21 October 2026 14:30, Mario Rossi <...> wrote:"; con gli account Microsoft la scrive il server di Outlook);
- le parole chiave della ricerca: in inglese valgono anche from:, to:, subject:, after:, before:, has:attachment,
  is:unread e is:flagged, e le date si scrivono come in inglese; quelle italiane (da:, a:, oggetto:, dal:, al:...)
  valgono sempre.

Nei modelli {nome} e {data} si scrivono anche {name} e {date}, in tutte e due le lingue.

## Account Microsoft: la prima volta

Aggiungi account, scrivi l'indirizzo (outlook.it, hotmail.com...), Accedi con Microsoft: si apre la finestra account di
Windows, scegli l'account e consenti a Missiva di leggere e inviare la posta.

Missiva usa l'interfaccia ufficiale Microsoft Graph, che vuole un "ID applicazione" registrato su Microsoft Entra.
Missiva ne ha già uno. Solo se preferisci usare un'app registrata da te (gratis, una volta sola, vale per tutti gli
account Microsoft personali):

1. Attiva un account Azure gratuito con il tuo account Microsoft personale: https://azure.microsoft.com/it-it/free
   (Microsoft chiede la verifica con la carta, senza addebiti; da quando non si possono più registrare app fuori da
   una directory è l'unico modo per un account personale). Poi accedi a https://entra.microsoft.com.
2. Entra ID, Registrazioni app (App registrations), Nuova registrazione (New registration).
3. Nome: Missiva. Tipi di account supportati: "Solo account Microsoft personali" (Personal Microsoft accounts only).
   URI di reindirizzamento: lascia vuoto. Premi Registra.
4. Nella pagina Panoramica (Overview) copia l'ID applicazione (client) (Application (client) ID).
5. Autenticazione (Authentication), Aggiungi una piattaforma (Add a platform), Applicazioni per dispositivi mobili e
   desktop (Mobile and desktop applications). Negli URI personalizzati scrivi, sostituendo il tuo ID:
   `ms-appx-web://microsoft.aad.brokerplugin/IL-TUO-ID`
   Premi Configura. Aggiungi poi, nella stessa sezione, anche `http://localhost` e salva.
6. Autorizzazioni API (API permissions), Aggiungi un'autorizzazione (Add a permission), Microsoft Graph,
   Autorizzazioni delegate (Delegated permissions): spunta Mail.ReadWrite e Mail.Send, poi Aggiungi. Bastano per la
   posta. Le altre funzioni chiedono ognuna un permesso in più, solo quando premi il loro "Consenti l'accesso":
   Calendars.ReadWrite (calendario e inviti), Contacts.ReadWrite (Persone), MailboxSettings.ReadWrite (risposte
   automatiche, regole e categorie di Outlook), People.Read (colleghi), Mail.ReadWrite.Shared e Mail.Send.Shared
   (caselle condivise). L'app di Missiva li ha tutti dichiarati qui; con un'app tua, se li vuoi usare, aggiungili allo
   stesso modo (senza dichiararli non è mai stato provato).
7. In Missiva: Aggiungi account, scrivi l'indirizzo (o scegli Microsoft), incolla l'ID in App Microsoft di Missiva,
   Accedi. Si apre la finestra account di Windows.

## Account di lavoro o della scuola (Microsoft 365)

Scrivi l'indirizzo in Aggiungi account: se il dominio usa Microsoft 365, Missiva lo riconosce. L'app di Missiva è
registrata per gli account personali e per quelli di lavoro o della scuola: Accedi con Microsoft apre la finestra
account di Windows. Se il servizio informatico ha registrato un'app per Missiva, puoi usare quella: "Usa l'app della mia
organizzazione", con l'ID dell'app e l'ID o il dominio della directory (tenant). Vale solo per quell'account: quelli
personali continuano con l'app di Missiva. L'app dell'organizzazione deve avere la piattaforma App per dispositivi
mobili e desktop con gli indirizzi di ritorno `ms-appx-web://microsoft.aad.brokerplugin/ID-DELL-APP` e
`http://localhost`, e i permessi delegati User.Read, Mail.ReadWrite e Mail.Send.

Se Microsoft rifiuta l'accesso, Missiva spiega il motivo. Quando serve l'approvazione di un amministratore
(la tua organizzazione non lascia agli utenti il consenso alle app, o solo a quelle di editori verificati), "Chiedi
l'approvazione all'amministratore" dà il link da mandargli: lo copi, o lo metti in un nuovo messaggio da un altro tuo
account. Con l'app di Missiva un solo consenso vale per tutti i suoi permessi: la posta, e anche calendario,
impostazioni della casella e caselle condivise, che Missiva chiede solo quando apri quelle funzioni. Quando l'ha
approvata, aggiungi di nuovo l'account. Lo stesso vale se l'organizzazione rifiuta un permesso chiesto dopo (per
esempio "Consenti l'accesso al calendario").

Caselle condivise (info@, segreteria@, amministrazione@): tasto destro sull'account di lavoro, "Aggiungi casella
condivisa…", e scrivi l'indirizzo (Microsoft non dice ai programmi a quali caselle hai accesso). La casella compare sotto
l'account con le sue cartelle, la ricerca e la copia per leggere senza rete; la prima volta sotto di lei "Consenti
l'accesso alle caselle condivise" apre la finestra di Microsoft (solo quando lo premi). Le risposte ai suoi messaggi
partono dalla casella condivisa, con la copia nella sua Posta inviata. Serve che l'amministratore ti abbia dato
Accesso completo sulla casella, e Invia come (o Invia per conto di) per scrivere da lì: se manca, Missiva lo dice.
Rimuovendo l'account escono anche le sue caselle condivise.

## Gestisci account

In Impostazioni, Account, ogni account ha Gestisci (anche dal tasto destro sull'account): il nome mostrato nel campo Da;
per gli altri account (IMAP) la password e i server, che Missiva prova con il server prima di salvarli (una password
sbagliata non cambia nulla); per Microsoft e Google "Accedi di nuovo". Firme, categorie, Preferiti e ordine delle
cartelle restano. Quando il server rifiuta la password (per esempio la password di Aruba PEC per i programmi di posta,
che scade ogni tre mesi) o Microsoft e Google chiedono un nuovo accesso, sotto l'account compare una barra con "Aggiorna
password" o "Accedi di nuovo": niente si apre da solo, nemmeno con la finestra di Missiva aperta (il controllo della
posta, i cambiamenti ogni 30 secondi e la copia degli ultimi 30 giorni non aprono la finestra di Microsoft). Se nella
finestra di Microsoft premi Annulla, non torna da sola: la riapre Accedi di nuovo.

## Altri account (IMAP e SMTP)

Per le caselle su dominio proprio. Scrivi indirizzo, nome e password: Missiva cerca da sola i server (configurazione
del dominio, database dei provider di Thunderbird, record DNS) e li mostra prima dell'accesso; in Configurazione
manuale li puoi scrivere o correggere (server, porta, SSL/TLS o STARTTLS, nome utente). Se i server trovati nel DNS
sono di un altro dominio, Missiva lo dice e prima di mandare la password chiede conferma. Per la posta certificata di Aruba PEC e Legalmail (anche sui domini propri) i server sono già noti. Il collegamento è sempre
cifrato, e un server con un certificato non valido viene rifiutato con la spiegazione. Se la casella non ha ancora un
Cestino, Missiva lo crea la prima volta che elimini qualcosa, come Thunderbird. La password resta sul PC,
cifrata con la protezione dati di Windows e leggibile solo dal tuo utente, e si cambia in Gestisci account. Con la
verifica in due passaggi del provider serve una password per le app.

## Gmail e Google Workspace: la prima volta

L'accesso è come nel nuovo Outlook: si apre il browser, scegli l'account Google e consenti a Missiva di usare la posta.
Google però lo permette solo alle app registrate su Google Cloud: ognuno registra la propria, una volta sola, gratis, e
vale per tutti i propri account Gmail.

La guida passo per passo è in [docs/gmail.md](docs/gmail.md), "Gmail: registrare la tua app su Google Cloud": i sette
passi, la pagina di Google che avvisa dell'app non verificata, il calendario e i contatti (facoltativi), Google
Workspace, che cosa fare se qualcosa non va, che cosa resta sul PC e come togliere l'accesso. Ci rimanda anche la
pagina di ingresso (README.md): se i passi cambiano, si correggono solo in quel file e nella sua versione inglese,
[docs/gmail.en.md](docs/gmail.en.md).

## Cosa c'è

- Una sola barra in alto, come nel nuovo Outlook: nome, ricerca e i pulsanti di Windows (con Snap Layouts); sotto, schede
  Home e Visualizza, colonna con Posta, Calendario e Persone, cartelle di tutti gli account (ogni account si
  comprime con la freccia), elenco dei messaggi, riquadro di lettura.
- La finestra si adatta fino a metà schermo: sotto una certa larghezza le cartelle si chiudono e il pulsante con le tre
  righe le apre sopra l'elenco, elenco e riquadro di lettura restano sempre dentro la finestra (nella finestra stretta si
  stringe prima l'elenco: il riquadro di lettura resta la colonna più larga), i comandi che non entrano nella barra
  finiscono nel menu "...".
- Cartelle ad albero, con le sottocartelle che si aprono e chiudono. Si riordinano trascinandole (una linea mostra dove
  finiranno) o con il tasto destro, Sposta su e Sposta giù; l'ordine resta salvato per ogni account.
- Elenco come nel nuovo Outlook: messaggi raggruppati per conversazione (freccia per aprirle), intestazioni per data
  (Oggi, Ieri, Questa settimana...), messaggi fissati in cima, azioni rapide al passaggio del mouse e sulla riga scelta
  con le frecce (segna come letto o da leggere, contrassegna, fissa, elimina), casella di selezione al posto
  dell'immagine del mittente, anteprima degli allegati. In Posta inviata e Bozze la riga mostra i destinatari invece del
  mittente, e le bozze hanno "[Bozza]" in rosso prima dell'oggetto. Tutte le righe hanno la stessa altezza, anche senza
  anteprima o mentre arriva: l'elenco non si sposta sotto il mouse. L'elenco di Gmail e degli altri account è in ordine
  di ora, come la mostra. All'avvio la cartella aperta si vede evidenziata, e con più account accanto al nome della
  cartella c'è l'account.
- Loghi dei mittenti al posto delle iniziali, nell'elenco, nel messaggio, nella scheda della persona e nelle notifiche:
  il logo certificato del marchio (quello che mostra anche Gmail) o l'icona del sito, solo per le aziende da cui ricevi
  posta da almeno due settimane e i cui messaggi il server della tua casella dà per autentici (hanno superato il
  controllo DMARC: si sa con Gmail e con Outlook, non con un account IMAP; un'azienda che non pubblica DMARC resta con
  le iniziali). Mai per la posta personale (gmail.com e simili), certificata o indesiderata.
  Missiva scarica il logo dal sito del mittente quando la riga compare nell'elenco, senza dirgli quale messaggio hai
  ricevuto, lo tiene sul PC e lo richiede dopo 30 giorni. Si spegne in Impostazioni, Posta, Elenco messaggi, "Mostra il
  logo dell'azienda del mittente".
- Assistente vocale e UI Automation leggono le righe come Outlook ("Non letto, Amazon.it, Spedito: ..., oggi 09:54, con
  allegato, contrassegnato") e le cartelle ("Posta in arrivo, 3 non letti"); tutti i pulsanti con un'icona hanno un nome.
- Posta in arrivo Evidenziata e Altra per gli account Microsoft, attivabile per singolo account.
- Riquadro di lettura con tutta la conversazione (il messaggio più recente in alto, gli altri si aprono con un clic),
  a destra, in basso o nascosto (i messaggi si aprono con doppio clic o Invio in una finestra). Come nel nuovo Outlook
  cambia in un colpo solo: oggetto, avviso delle immagini e testo del messaggio arrivano insieme, senza sfarfallio.
  Come il nuovo Outlook, Missiva scarica in anticipo i messaggi che probabilmente aprirai (per Gmail i primi dieci
  dell'elenco e quelli accanto al messaggio aperto, per Outlook.com il successivo), senza segnarli come letti: si aprono
  in qualche centesimo di secondo.
- Email impaginate come nell'originale, newsletter comprese; quelle più larghe del riquadro si rimpiccioliscono fino a
  starci, e se non basta (a metà schermo) scorrono di lato, con la barra in fondo al riquadro.
- Tema scuro: le email che hanno una versione scura la usano, le altre vengono adattate ai colori scuri come in
  Outlook; il pulsante con il sole le mostra su fondo chiaro, e in Visualizza c'è "Riquadro di lettura chiaro" per
  averle sempre così. Zoom del testo dei messaggi.
- Impostazioni come in Outlook (Generali: Aspetto, Notifiche, Avvio e chiusura, Copia di sicurezza, Informazioni; Account: Account di
  posta e Firme; Posta: Elenco messaggi, Riquadro di lettura, Gestione messaggi, Regole, Scrittura e risposta e Modelli), aperte
  dall'ingranaggio in fondo alla colonna a sinistra: tema, lingua, notifiche per account e suono, numero sull'icona, dimensioni del testo, conversazioni, anteprima di
  0, 1 o 2 righe, mittente o oggetto all'inizio, immagini dei mittenti, anteprima degli allegati, intestazioni per
  data, quando segnare letti i messaggi aperti. Si applicano subito (la lingua al prossimo avvio).
- Messaggi segnati come letti subito, dopo alcuni secondi aperti nel riquadro, quando la selezione cambia o mai
  (Impostazioni, Posta, Gestione messaggi).
- Lettura con allegati fra l'intestazione e il testo, come nel nuovo Outlook: icona del tipo di file e dimensione, clic
  per aprire, pulsante accanto per salvare, "Salva tutti" in una cartella (tranne i tipi di file che possono avviare
  programmi, segnati in rosso: quelli si salvano uno per uno). Gli allegati aperti con un programma o salvati, i messaggi
  salvati come .eml e i documenti dei file firmati passano dagli scaricamenti nella barra del titolo (sotto). Una pagina web allegata (.html, .htm, .svg, .mht) si
  apre nel browser solo dopo una domanda: è una pagina arrivata per posta, non scriverci password. Immagini esterne
  bloccate finché non le chiedi, al loro posto un riquadro grigio con l'icona e il testo alternativo (al massimo 160
  pixel di altezza, così il testo si vede subito): dopo Mostra immagini, per quel mittente si vedono sempre, in tutti
  gli account (tranne nella Posta indesiderata, nei messaggi con una fascia rossa sul mittente o sulla firma e nei
  messaggi allegati, i file .eml, dove non si caricano mai da sole, nemmeno per i mittenti consentiti: lì Mostra
  immagini vale solo per quella volta). Per le aziende vale tutto il
  dominio (google.com comprende accounts.google.com), per la posta personale come gmail.com solo quell'indirizzo.
  Per gli enti che stanno sotto gov.it, edu.it o un nome geografico (il comune di un paese sotto pd.it, un ente sotto
  veneto.it o padova.it) vale quell'ente, non tutti quelli sotto lo stesso suffisso. Una voce che vale per tutto un
  servizio di posta (gmail.com) o per tutto un suffisso (co.uk, pd.it), che Missiva non scrive, all'avvio esce, e il
  registro dice quale: per quei mittenti premi di nuovo Mostra immagini.
  L'elenco è in Impostazioni, Riquadro di lettura.
- I link mailto: nelle email aprono un nuovo messaggio in Missiva, e anche quelli delle pagine web e dei PDF se scegli
  Missiva come app di posta (vedi Installare).
- Annulla iscrizione, come nel nuovo Outlook e in Gmail: nelle newsletter che lo prevedono, sotto il mittente c'è
  "Ricevi troppi messaggi da questo mittente? Annulla iscrizione". Missiva dice prima che cosa farà e verso quale sito, e
  non fa nulla senza la tua conferma: manda la richiesta al sito del mittente (solo se il messaggio viene davvero da
  lui, cioè ha superato il controllo DMARC, e porta la sua firma: si sa con Gmail e con Outlook, non con un account
  IMAP), oppure prepara un messaggio
  che invii tu (dall'indirizzo a cui la newsletter ti scrive, se è uno dei tuoi altri indirizzi), oppure
  apre la pagina del mittente nel browser. I messaggi già arrivati
  restano dove sono. Il comando non c'è nella Posta indesiderata (annullare l'iscrizione lì conferma il tuo indirizzo a
  chi manda spam) né nei messaggi con la fascia rossa.
- Chi scrive davvero: quando le risposte vanno a un altro indirizzo (Reply-To, per esempio i moduli dei siti) il
  messaggio lo dice ("Le risposte vanno a"), e Rispondi va lì come in Outlook. Una fascia rossa avvisa quando il server
  della tua casella non ha potuto verificare il mittente (DMARC fallito, o SPF e DKIM entrambi falliti), una gialla
  quando il nome è di una persona a cui hai scritto ma l'indirizzo è un altro, o quando il mittente si presenta con
  l'indirizzo o il sito di un altro al posto del nome (allora elenco e notifiche mostrano l'indirizzo vero). Negli account di lavoro i mittenti fuori
  dall'organizzazione hanno "Esterno", e scrivendo a persone esterne Missiva lo ricorda.
- Inviti alle riunioni: una scheda nel messaggio con quando (nell'ora del tuo PC), dove, organizzatore, partecipanti,
  ricorrenza e link della riunione online (Teams, Zoom, Meet), e Accetta, Provvisorio, Rifiuta (con una nota, con o senza
  risposta all'organizzatore) e Proponi un nuovo orario. Per Gmail e gli altri account la risposta parte come messaggio
  all'organizzatore, che il suo calendario capisce; per gli inviti di Outlook serve una volta "Consenti l'accesso al
  calendario", e la risposta aggiorna anche il calendario di Outlook. Le risposte agli inviti che hai mandato tu
  ("Anna ha accettato"), le proposte di un nuovo orario e gli eventi da un biglietto o un webinar hanno la scheda senza
  pulsanti.
- I messaggi allegati (.eml) si aprono in una finestra di Missiva, con lo stesso riquadro chiuso. Lì le immagini
  esterne sono sempre bloccate finché non premi Mostra immagini: chi abbia scritto davvero un messaggio allegato non lo
  verifica nessuno.
- Scrivi, rispondi, rispondi a tutti, inoltra, con allegati, link e formattazione come Formato testo del nuovo Outlook:
  carattere e dimensione, grassetto, corsivo, sottolineato, colore del testo ed evidenziatore (colori fissi, che restano
  anche nel tema scuro), elenchi, rientro, allineamento, tabelle (Inserisci tabella), immagini da un file e Cancella
  formattazione. Il carattere, la dimensione e il colore con cui cominciano i messaggi si scelgono in Impostazioni, Posta,
  Scrittura e risposta; le bozze riprese e i modelli tengono il formato. Uno screenshot incollato (Ctrl+V) o un'immagine trascinata nel testo parte dentro il messaggio, come nel
  nuovo Outlook, e così le tabelle incollate da Word o Excel; i file trascinati sulla finestra o incollati diventano allegati. Sotto il testo si vede il messaggio a cui rispondi o che inoltri (Da, Inviato, A, Oggetto e il testo, con
  Mostra tutto); una risposta o un inoltro non toccati si chiudono senza domande.
- Bozze come nel nuovo Outlook: si salvano da sole mentre scrivi (10 secondi dopo l'ultima modifica, o ogni minuto se
  continui; accanto ai pulsanti "Bozza salvata alle..."), anche quando Windows si riavvia per gli aggiornamenti.
  Chiudendo un messaggio con qualcosa di non salvato Missiva propone Salva bozza (o Scarta, o Annulla), anche quando
  chiudi tutta Missiva; Ctrl+S salva quando vuoi. Scarta (o Esc) toglie dalle Bozze anche la bozza salvata da sola, e
  cambiando il mittente la bozza passa all'altro account. Nelle Bozze, Modifica (o il doppio clic) riapre la bozza con
  destinatari, oggetto, testo e allegati; inviata, sparisce dalle Bozze.
- Annulla invio come nel nuovo Outlook: dopo Invia il messaggio aspetta 5 secondi (0 o 10 in Impostazioni, Posta,
  Scrittura e risposta) con "Invio in corso… Annulla" in fondo all'elenco; Annulla lo riapre com'era.
- Allegato dimenticato, come nel nuovo Outlook: se nel testo scrivi di un allegato ("in allegato", "allego", "attached")
  e non ce n'è nessuno, a Invia Missiva chiede "Hai dimenticato l'allegato?", con Invia comunque o Torna al messaggio.
  La firma e il messaggio a cui rispondi non contano; inoltrando, gli allegati dell'originale sì. Si spegne in
  Impostazioni, Posta, Scrittura e risposta.
- Pianifica invio, dalla freccia accanto a Invia: domani alle 8, lunedì alle 8 o all'ora che scegli. Con gli account
  Microsoft il messaggio aspetta nella Posta in uscita di Outlook e parte anche a Missiva chiusa; con Gmail e gli altri
  resta nelle Bozze e lo invia Missiva all'ora scelta, o appena la riapri. Se non riesce a partire, Missiva lo dice con
  il motivo: senza rete riprova ogni minuto, negli altri casi il messaggio resta nelle Bozze da inviare di nuovo. Se
  la connessione cade proprio mentre il server lo sta ricevendo non riprova, perché potrebbe arrivare due volte: dice
  che forse è partito, e lo controlli tu.
- Opzioni del messaggio: importanza alta o bassa, conferma di lettura e di recapito. I messaggi ricevuti con importanza
  alta hanno il punto esclamativo rosso nell'elenco.
- Firme per ogni account e indirizzo (Impostazioni, Account, Firme), come nel nuovo Outlook: più firme con un nome (per
  esempio una breve), una per i nuovi messaggi e una per le risposte e gli inoltri (o nessuna); nella scrittura il
  pulsante Firma la cambia. Cambiando il mittente cambia anche la firma, se non l'hai modificata; se dal pulsante Firma
  hai scelto Nessuna firma, il messaggio resta senza.
- Modelli, come I miei modelli del nuovo Outlook (Impostazioni, Posta, Modelli): i testi che si ripetono (preventivi,
  solleciti, coordinate bancarie), con un nome, l'oggetto se vuoi e il testo formattato, per tutti gli account o per uno.
  Nella scrittura il pulsante Modelli li cerca e li inserisce dove sei (e mette l'oggetto se manca); "Salva come
  modello" prende il testo scritto. Dal menu del messaggio c'è anche "Rispondi con modello". {nome} diventa il nome del
  primo destinatario, {data} la data di oggi. Restano sul PC, cifrati. Un modello che su questo PC non si decifra più
  (impostazioni portate da un altro utente di Windows senza la copia di sicurezza) non compare fra quelli da inserire:
  resta in Impostazioni, Posta, Modelli, dove si elimina.
- Regole, come Impostazioni, Posta, Regole del nuovo Outlook: l'elenco nell'ordine in cui si applicano, ognuna accesa o
  spenta, con Modifica, Elimina, Su, Giù ed "Esegui ora" su una cartella (gli ultimi 500 messaggi, dopo una conferma con
  quanti ne cambiano o, se la regola elimina, con quanti ne vanno nel Cestino). Condizioni: mittente (indirizzi o parole), inviato a me, in Cc, oggetto, testo, allegati,
  importanza; azioni: sposta, copia, elimina (nel
  Cestino), segna come letto, contrassegna, categoria, non applicare altre regole. "Crea regola" nel menu del tasto
  destro di un messaggio parte con il suo mittente. Con gli account Microsoft sono le regole di Outlook, le stesse di
  Outlook sul web: girano sul server anche a Missiva chiusa (la prima volta "Consenti l'accesso alle impostazioni della
  casella", solo quando lo premi). Una regola che inoltra la posta fuori dalla tua organizzazione (il trucco di chi entra
  di nascosto in una casella) è in rosso con Spegni, e Missiva la crea, e la riaccende se era spenta, solo dopo una
  conferma con tutti gli indirizzi in vista, anche quelli che la finestra della regola non mostra; lì Invio annulla, e
  per confermare si sceglie il pulsante. Salvando una regola senza condizioni, la domanda ricorda gli indirizzi a cui
  manda la posta.
  Le regole di Outlook con condizioni che Missiva non mostra (oggetto o testo, destinatari, eccezioni) non hanno Esegui
  ora: le applica Outlook ai messaggi che arrivano.
  Con Gmail e gli altri account sono regole di Missiva, cifrate sul PC: agiscono sui nuovi messaggi della Posta in
  arrivo solo mentre Missiva è aperta (anche nell'area di notifica), su tutti quelli arrivati dall'ultima volta (anche già
  letti sul telefono, anche dopo una notte con il portatile chiuso), una volta sola per messaggio; quello che spostano o
  eliminano non arriva come notifica. Se il server rifiuta un'azione le altre vanno avanti lo stesso, e Missiva riprova
  quel messaggio ai controlli dopo (ogni minuto), fino a tre volte in tutto, poi lo lascia nella Posta in arrivo;
  una copia già fatta non si rifà. Esegui ora dice che cosa è riuscito e che cosa no.
- Suggerimenti dei destinatari in A, Cc e Ccn (e fra i partecipanti di una riunione) mentre scrivi, dalle persone a cui
  scrivi (Posta inviata di ogni account), da cui ricevi posta e dai tuoi contatti di Persone (anche quelli a cui non hai
  mai scritto, con l'azienda e tutti i loro indirizzi; per gli account Microsoft anche i contatti di Outlook, se hai già
  dato l'accesso): frecce e Invio o Tab, oppure un clic. Con un account di lavoro di Microsoft 365, in fondo "Cerca anche
  fra i colleghi" trova i colleghi della tua organizzazione a cui non hai mai scritto (la prima volta chiede il permesso,
  solo quando lo premi; se serve l'amministratore, Missiva spiega come chiederglielo).
- Gruppi di contatti (Persone, Nuovo gruppo): un nome e le persone; scrivendo il nome in A lo trovi fra i suggerimenti, e
  scelto diventa gli indirizzi delle sue persone. Restano sul PC, cifrati.
- Scheda della persona: un clic sulla foto o sul nome del mittente nel riquadro (anche in ogni messaggio di una
  conversazione) mostra il contatto con azienda, telefoni e note, oppure "Aggiungi ai contatti", con gli ultimi messaggi con
  quella persona e i loro allegati, Scrivi e "Tutti i messaggi".
- Contrassegna con scadenza, come Outlook: dal menu del messaggio Oggi, Domani, Questa settimana, Settimana prossima,
  Nessuna data, una data scelta, e il promemoria all'ora che vuoi (una notifica, anche con Missiva nell'area di notifica,
  con Posponi promemoria e Segna come completato). Sulla riga "Scadenza: domani", in rosso quando è passata; in Tutti gli
  account, Contrassegnati li ordina per scadenza (In ritardo, Oggi, Domani, Questa settimana, Più avanti, Senza data).
  Con gli account Microsoft le date stanno in Outlook; con Gmail e gli altri il contrassegno sta sul server e le date in
  Missiva.
- Elimina, archivia, sposta, copia, segna come letto o da leggere, contrassegna, fissa. Su una conversazione chiusa le
  azioni valgono per tutti i suoi messaggi nella cartella, come in Outlook. Dopo Elimina, Archivia o Sposta, in fondo
  all'elenco compare Annulla per qualche secondo (anche Ctrl+Z): i messaggi tornano dov'erano, in Gmail con le etichette
  che avevano. Con Seleziona un clic aggiunge o toglie un messaggio dalla scelta.
- Messaggi trascinati su una cartella o su un Preferito dello stesso account: si spostano (con Ctrl si copiano), e la
  cartella sotto il puntatore si evidenzia.
- Menu del tasto destro come nel nuovo Outlook, con le icone colorate; con "Personalizza il menu…", in fondo, scegli
  quali voci mostrare, il loro ordine e i separatori.
- Categorie, come in Outlook: pastiglie colorate nell'elenco e nel messaggio. In Gmail sono le etichette (il colore lo
  tiene Missiva), negli account Microsoft le categorie di Outlook (con i loro colori dopo il consenso alle impostazioni
  della casella, dalla finestra delle risposte automatiche; prima quelle create in Missiva).
- Risposte automatiche per gli account Microsoft, come nel nuovo Outlook (menu dell'account o Impostazioni, Account,
  Risposte automatiche): spente, attive o attive in un periodo, con il messaggio per la tua organizzazione e quello per
  gli esterni (a nessuno, ai contatti, a tutti; per Outlook.com un messaggio solo). Quando sono attive o programmate,
  sotto l'account una barra lo dice, con Disattiva e Modifica. La prima volta "Consenti l'accesso alle impostazioni
  della casella" apre la finestra di Microsoft (solo quando lo premi).
- Posponi: il messaggio va nella cartella Posposti e all'ora scelta torna in Posta in arrivo, da leggere e in cima, con
  una notifica. Il ritorno lo fa Missiva: se a quell'ora è chiusa, torna appena la riapri.
- "..." (Altre azioni) nell'intestazione di ogni messaggio aperto apre il suo menu, come il tasto destro. La finestra
  del messaggio ha la sua barra: Elimina, Archivia, Sposta in, Già letto/Da leggere, Contrassegna, Stampa e Altre azioni.
- Salva con nome (file .eml o PDF), Stampa, origine del messaggio con tutte le intestazioni (dei messaggi con allegati
  molto grandi i primi 2 MB, che bastano per le intestazioni), messaggi dello stesso mittente.
- Menu del tasto destro sulle cartelle come in Outlook. Svuota sposta nel Cestino i messaggi della cartella (dal Cestino
  e dallo Spam li elimina per sempre, dopo una conferma); come nel sito di Gmail non c'è per Tutti i messaggi e Speciali,
  che contengono anche i messaggi delle altre cartelle.
- Calendario per gli account Microsoft (Outlook.com e Microsoft 365) e Google (Gmail e Google Workspace), dal pulsante
  nella colonna a sinistra: Giorno,
  Settimana lavorativa, Settimana e Mese, i calendari di ogni account con i loro colori (si mostrano e si nascondono),
  i dettagli dell'evento con partecipanti e risposte, "Partecipa" per le riunioni online. Con più di tre eventi in un
  giorno, "+N altri" apre quel giorno con tutti. La prima volta "Consenti
  l'accesso al calendario" apre la finestra di Microsoft o, per Google, il browser (solo quando lo premi). I promemoria arrivano come notifiche di
  Windows mentre Missiva è aperta (anche nell'area di notifica); quelli scaduti mentre il PC era sospeso o la rete
  mancava arrivano in ritardo appena Missiva rilegge il calendario, ma solo per le riunioni non ancora finite, e la
  notifica lo dice ("Promemoria in ritardo: era alle 08:45"); quelli delle riunioni già finite non arrivano. Nuovo evento (Ctrl+N nel calendario, Ctrl+Maiusc+A da
  ovunque), o doppio clic su un'ora o su un giorno:
  titolo, partecipanti (suggeriti dalla rubrica: con dei partecipanti diventa una riunione e partono gli inviti),
  calendario, inizio e fine o giornata intera, ripetizione (ogni giorno, ogni settimana nei giorni scelti, ogni mese),
  luogo, promemoria, mostra come, descrizione; riunione di Teams per gli account di lavoro. Agli eventi a cui sei invitato
  rispondi anche dai dettagli: Accetta, Provvisorio, Rifiuta. I tuoi eventi si modificano (doppio clic, o Modifica nei
  dettagli; con partecipanti "Invia aggiornamento"), si spostano trascinandoli a un'altra ora, si eliminano o, se sono
  riunioni, si annullano con una nota per i partecipanti (per Google senza nota: Google Calendar non la manda, e Missiva
  lo dice); per quelli che si ripetono scegli solo questo o tutta la serie.
  Dal menu del messaggio "Rispondi con riunione" prepara la riunione con il mittente e gli altri destinatari. Con gli
  account di lavoro "Controlla la disponibilità" mostra quando i partecipanti sono liberi.
- Persone, come nel nuovo Outlook: i tuoi contatti con la ricerca e la scheda (indirizzi, telefoni, azienda, ruolo,
  indirizzo, note, dove tenere anche PEC e partita IVA), Scrivi, Nuovo contatto, Modifica, Elimina. Con gli account
  Microsoft sono i contatti di Outlook e con gli account Google quelli di Google Contatti (la prima volta "Consenti
  l'accesso ai contatti", solo quando lo premi; per Google si leggono, e dopo "Consenti la modifica dei contatti" si
  creano, si cambiano e si eliminano anche da Missiva); per gli altri account i contatti stanno in Missiva, sul PC,
  cifrati. Importa una vCard o un CSV di Outlook o di Gmail
  (senza le foto; anche migliaia di contatti in meno di un secondo), Esporta in vCard.
- Altri indirizzi da cui scrivere (alias, "Invia come"): in Gestisci account aggiungi nome, indirizzo e, se serve,
  Rispondi a. Compaiono nel campo Da; rispondendo a un messaggio scritto all'alias parte dall'alias. Ognuno può avere la
  sua firma (Impostazioni, Account, Firme). Per Gmail l'indirizzo va aggiunto anche in Gmail (Impostazioni, Account e
  importazione, Invia messaggio come); per Microsoft 365 serve il permesso Invia come dato dall'amministratore.
- Senza rete i messaggi già aperti e i più recenti della Posta in arrivo scaricati in anticipo si leggono lo stesso,
  dalla copia su disco (cifrata, solo per il tuo utente di Windows), con la riga "Non in linea: copia del ...". Sotto
  l'account "Non in linea" dice il perché: la connessione, o un firewall (come Portmaster) che blocca Missiva. Le bozze
  non si copiano (si riprendono sempre dal server); la copia di un messaggio eliminato, spostato o svuotato si toglie, e
  dopo 30 giorni esce comunque. La posta certificata letta dalla copia dice che la firma si controlla solo in linea.
  Quando non usi la finestra da mezzo minuto (o Missiva è nell'area di notifica), Missiva tiene sul PC anche i messaggi
  della Posta in arrivo degli ultimi 30 giorni, come l'Outlook classico: li scarica in sottofondo, uno alla volta, senza
  segnarli letti (per Gmail e IMAP interi quelli sotto 300 KB; di quelli più grandi, con allegati, solo il testo e le
  immagini nel testo, se insieme stanno sotto 2 MB), fino a circa 180 MB per account. Senza rete la ricerca
  cerca in questa copia ("Risultati sul PC").
- Lavorare senza rete: Elimina, Archivia, Sposta, Già letto/Da leggere e Contrassegna fatti senza rete si vedono subito
  e vanno in coda (sotto l'account "N azioni in attesa"; Annulla funziona come sempre finché la rete manca, e se intanto
  è tornata e lo spostamento è già partito Missiva lo dice e rimette al loro posto solo i messaggi non ancora partiti); quando la rete torna partono una
  volta sola, e se intanto un messaggio non c'è più (eliminato dal telefono, per esempio) Missiva lo dice; se prima serve
  un nuovo accesso, o il server è occupato, aspettano e Missiva dice perché. Invia senza
  rete mette il messaggio nella Posta in uscita di Missiva: una barra in fondo all'elenco lo dice, con Riprova e Annulla,
  e il messaggio parte da solo quando la rete torna, anche con Missiva nell'area di notifica. Se invece la connessione
  cade proprio mentre il server sta ricevendo il messaggio, non va nella Posta in uscita (ripartendo potrebbe arrivare
  due volte): una finestra "Invio interrotto" dice che forse è partito, e lo controlli tu. Un messaggio già nel
  Cestino si elimina per sempre solo con la rete.
- Missiva resta nell'area di notifica, vicino all'orologio, quando chiudi la finestra (come Outlook): continua a
  controllare la posta, a mostrarti le notifiche, a mandare i messaggi pianificati e a riportare i posposti, usando poca
  memoria (la parte che disegna i messaggi si chiude). Un clic sull'icona la riapre subito; tasto destro, Esci per
  chiuderla del tutto. In Impostazioni, Generali, Avvio e chiusura si sceglie se chiuderla del tutto e se avviarla
  all'accesso a Windows, nascosta.
- Posta certificata (PEC), come le webmail dei gestori: la busta del gestore si apre da sola e il riquadro mostra il
  messaggio vero con i suoi allegati, sotto una fascia che dice chi l'ha mandato, quando e con quale gestore, e se la
  firma del gestore è verificata (verde: il certificato è quello del gestore, per la posta, riconosciuto da Windows),
  integra ma con un certificato che Windows non conosce (gialla, il caso di tutti i giorni) o alterata (rossa). Una
  busta senza la firma di un gestore, o firmata con un altro certificato, è rossa: "Sembra posta certificata, ma...".
  Le ricevute di accettazione e di consegna si leggono in una riga; le mancate consegne sono in rosso con il motivo;
  l'anomalia (un messaggio non certificato arrivato nella casella PEC) è gialla, con l'originale allegato. Rispondi e
  "Mostra immagini" valgono per il mittente vero, non per il gestore. Anche i messaggi firmati (S/MIME) dicono chi li ha
  firmati, e sono rossi se il certificato non è del mittente.
- File firmati .p7m (fatture elettroniche, documenti firmati digitalmente): un clic li apre in Missiva con il
  firmatario, la data e lo stato della firma; se le firme sono più di una si leggono tutti i firmatari, e basta una
  firma che non va (un certificato scaduto, o non adatto a firmare documenti) perché la fascia lo dica, con il nome di
  chi l'ha messa; le fatture (XML) si leggono come testo, gli altri documenti si salvano o si aprono con il loro
  programma.
- Anteprima degli allegati nel riquadro, come nel nuovo Outlook: un clic su un PDF, un'immagine (PNG, JPEG, GIF, BMP) o
  un testo (.txt, .csv, .xml, .json, .log), sulla scheda nel riquadro, sulla pastiglia nell'elenco o su un file dei
  suggerimenti della ricerca, lo mostra al posto del messaggio, con zoom, frecce per gli altri allegati, Scarica, Apri e
  Torna al messaggio (anche Esc). I PDF e le immagini li disegna Windows in un processo a parte e protetto, come nei
  browser: senza rete e senza accesso ai tuoi file (impostazioni, accessi, posta), non può avviare programmi e si chiude
  da solo dopo un minuto; niente JavaScript, link o moduli del PDF. Nessun file temporaneo. I documenti Office si aprono
  ancora con il loro programma.
- Tutti gli account, come in Thunderbird: con due o più account, in cima alle cartelle Posta in arrivo, Non letti,
  Contrassegnati, Bozze e Posta inviata di tutti gli account insieme, in ordine di data. Ogni riga ha il colore e il nome
  del suo account (e il riquadro di lettura lo ripete sotto l'oggetto); Rispondi parte da quell'account, Elimina e
  Archivia usano il Cestino e l'Archivio di ognuno, Annulla compreso.
- Ricerca in tutte le cartelle dell'account aperto, solo nella cartella aperta o in tutti gli account (menu accanto alla
  casella; dalle viste di tutti gli account si cerca sempre in tutti); ogni risultato mostra la sua cartella (e il suo
  account) e scorrendo arrivano i successivi. Per Gmail con la stessa sintassi della ricerca di
  Gmail, che come Gmail non guarda in Spam e Cestino. Scrivendo si apre, come nel nuovo Outlook, il pannello con le
  schede Tutto, Mail, Persone e File: ricerche recenti, persone con cui scrivi, allegati per nome e messaggi. Frecce e
  Invio per scegliere: una persona mostra i messaggi da e per lei, un file o un messaggio si apre fra i risultati.
  Filtri, come nel nuovo Outlook (l'imbuto accanto a "Dove cercare"): Da, A, Oggetto, Contiene, Dal, Al, Con allegati,
  Non letti e Contrassegnati, anche in tutti gli account. I filtri scelti diventano pastiglie nella casella, ognuna con la
  x per toglierla; si possono anche scrivere: da:rossi, a:"Mario Rossi", oggetto:preventivo, dal:1/3/2026, al:31/3/2026,
  ha:allegati, è:nonletto, è:contrassegnato. Restano nelle ricerche recenti. Le date valgono nell'ora del tuo PC, anche il
  giorno del cambio d'ora.
- Nuovi messaggi subito, come Thunderbird: con Gmail e con gli altri account IMAP il server avvisa Missiva appena arriva
  un messaggio (IDLE), e così per i messaggi letti o spostati dal telefono; con gli account Microsoft Missiva chiede i
  cambiamenti della Posta in arrivo ogni 30 secondi (letti ed eliminati altrove si aggiornano nell'elenco). Anche con
  Missiva nell'area di notifica. Il controllo di riserva resta per tutti: ogni minuto con Gmail e gli altri account IMAP,
  ogni due minuti con gli account Microsoft. Alla ripresa del PC dalla sospensione e quando la rete cambia Missiva rifà
  subito le connessioni e controlla tutti gli account.
- Notifiche di Windows per i nuovi messaggi, anche mentre Missiva è nell'area di
  notifica: il clic sulla notifica di un messaggio lo apre in una finestra sua (da lì un pulsante lo porta nel riquadro di
  lettura della finestra principale); quello su "N nuovi messaggi" apre la Posta in arrivo di quell'account. La notifica
  di un messaggio ha tre pulsanti: Rispondi (apre la risposta in una finestra di scrittura, dove si vede a chi va), Segna
  (lo contrassegna) ed Elimina (nel Cestino); nel menu "..." Posponi di un'ora e "Posponi...", con il giorno e l'ora da
  scegliere. Segna, Elimina e Posponi di un'ora lavorano senza aprire Missiva. Si spengono per account, e il suono per tutte, in
  Impostazioni, Generali, Notifiche. Sull'icona nella barra delle applicazioni un numero, a scelta in Impostazioni,
  Generali, Notifiche, "Numero sull'icona di Missiva": i messaggi da leggere nelle Posta in arrivo (di base), i messaggi
  nuovi arrivati da quando hai guardato Missiva (quelli delle notifiche, anche con le notifiche spente; tornano a zero
  quando apri la finestra) o nessun numero, e per quali account ("Conta questi account"). Lo stesso numero lo dice il
  suggerimento dell'icona vicino all'orologio.
- Scaricamenti, come in un browser: aprire un allegato con un programma, salvarlo (Salva con nome, Salva tutti: uno per
  file), salvare un messaggio come .eml o il documento di un file firmato mette un elemento negli scaricamenti, con
  l'icona nella barra del titolo accanto all'aggiornamento: mentre scarica un anello dice quanto manca (con più file,
  quanti sono), quando finisce compare per qualche secondo un segno di fatto, e un errore resta segnato finché non lo
  guardi. Il clic apre l'elenco di questa sessione (gli ultimi 20), con lo stato di ognuno (in corso con la
  percentuale, finito, non riuscito con il motivo, annullato), Annulla per quelli in corso (non resta nessun file a
  metà), Apri e Mostra nella cartella per quelli finiti (Apri fa le stesse domande del riquadro), e "Cancella
  l'elenco". L'anteprima nel riquadro non ci passa: lì si vede già che carica.
- Scorciatoie come nel nuovo Outlook: Ctrl+N nuovo (un messaggio nella posta, un evento nel calendario; da ovunque
  Ctrl+Maiusc+M un messaggio e Ctrl+Maiusc+A un evento), Ctrl+R rispondi, Ctrl+Maiusc+R rispondi a tutti, Ctrl+F (o
  Ctrl+Maiusc+F) inoltra, Ctrl+Maiusc+V sposta in una cartella, Ctrl+Z annulla, Ctrl+. e Ctrl+, messaggio successivo e
  precedente, Ctrl+Q e Ctrl+U già letto e da leggere, Ins contrassegna, Ctrl+P stampa, Ctrl+E cerca, Canc elimina,
  Invio apre in una finestra, F5 sincronizza. Nella scrittura Ctrl+Invio
  invia, Esc chiude come Scarta, Ctrl+K inserisce un link, Ctrl+S salva la bozza.
- Tema chiaro o scuro come Windows, anche quando cambia con Missiva aperta (per esempio il cambio automatico al
  tramonto): il messaggio aperto si ridisegna restando dov'era. Oppure sempre chiaro o sempre scuro (Impostazioni,
  Generali, Aspetto).

## Sicurezza

- Nessun codice delle email viene eseguito: JavaScript è spento nel riquadro di lettura, e script, frame e moduli
  vengono tolti dall'email prima ancora di mostrarla. Anche quando rispondi o inoltri da un account Gmail o IMAP (con
  gli account Microsoft la citazione la prepara il server di Outlook): il messaggio citato parte senza script, moduli
  e link che eseguono qualcosa, e con le sue immagini com'erano.
- Il riquadro non si collega a Internet da solo: ogni richiesta passa da Missiva, che lascia passare solo le immagini
  dei mittenti per cui hai premuto Mostra immagini. Così il mittente non sa se e quando hai letto.
- Fuori dal riquadro, l'unica richiesta che Missiva fa da sola verso un mittente è quella del suo logo (vedi "Loghi dei
  mittenti" in Cosa c'è; una ricerca nel DNS, poi il logo dal suo sito): parte quando l'elenco mostra un suo messaggio,
  di norma una volta ogni 30 giorni per azienda, e non dice quale messaggio né se l'hai aperto; il sito vede però
  l'indirizzo IP e il momento. Spenta la casella dei loghi non parte più.
- Una Missiva installata da un rilascio chiede a GitHub se c'è una versione nuova due minuti dopo l'avvio e poi una
  volta al giorno (GitHub vede l'indirizzo IP, l'ora e il nome "Missiva"; si spegne in Impostazioni, Generali,
  Informazioni), e accetta un aggiornamento solo se è firmato con la chiave di chi pubblica Missiva, più nuovo, con
  l'impronta giusta e i soli file attesi (`docs/aggiornamenti.md`).
- I link si aprono nel tuo browser, mai dentro Missiva.
- Allegati salvati o aperti con il marchio "scaricato da Internet": Office li apre in Visualizzazione protetta e
  SmartScreen controlla gli eseguibili. I tipi di file che possono avviare programmi o collegarsi da soli a un server
  (gli stessi che blocca Outlook, come .exe, .js, .bat, .iso, .library-ms, .search-ms, .rdp) chiedono conferma prima di
  aprirsi, e "Salva tutti" li salta.
- Gli allegati aperti si cancellano dalla cartella temporanea alla chiusura di Missiva e, mentre è aperta, un'ora dopo
  l'apertura o l'ultimo salvataggio (un file ancora aperto in un altro programma resta, e si toglie a un avvio dopo,
  quando ha più di un giorno).
- Link sospetti: se il testo mostra un sito e il link porta altrove, o se l'indirizzo usa caratteri che imitano
  quelli di un sito conosciuto, compare un avviso con l'indirizzo reale prima di aprirlo.

## Cosa manca ancora

- Calendario: l'agenda di oggi accanto alla posta; quello degli altri account (CalDAV) e quello delle caselle condivise.
  Spostando tutta una serie si cambia solo l'ora: i giorni della ripetizione si cambiano in Outlook o in Google Calendar
  sul web. Il calendario di Google non ha le riunioni di Meet né la nota dell'annullamento.
- Persone: i contatti "Altri" di Google (quelli che Gmail raccoglie da solo); i gruppi di contatti di Outlook (quelli
  di Missiva stanno sul PC); la scheda della persona dei destinatari (c'è per il mittente).
- Risposte automatiche: quelle di Gmail e degli altri account (ci sono per gli account Microsoft).
- Regole: quelle di Gmail e degli altri account girano solo con Missiva aperta (i filtri del sito di Gmail vorrebbero un
  permesso in più sul progetto Google Cloud); le regole di Outlook non
  hanno Contrassegna fra le azioni, e le loro condizioni più rare (intestazioni, dimensione, eccezioni) si vedono solo
  come "altre condizioni di Outlook", e restano com'erano quando modifichi la regola. Le regole delle caselle condivise
  si cambiano in Outlook sul web.
- Caselle condivise: calendario, categorie di Outlook e risposte automatiche della casella condivisa; gli inviti di
  Outlook arrivati nella casella condivisa non hanno la scheda con Accetta e Rifiuta.
- Senza rete: gli allegati non si tengono sul PC (solo i nomi e le immagini nel testo); della Posta in arrivo si tengono
  gli ultimi 30 giorni, delle altre cartelle i messaggi aperti. La Posta in uscita sta in memoria: se esci da Missiva
  (Esci, non la X) prima che la rete torni, i messaggi non partiti si riaprono per salvarli come bozza o scartarli, e
  senza rete anche la bozza non si salva. Copia, categorie, Posponi e le date dei contrassegni senza rete non vanno in
  coda.
- Tutti gli account: filtro, "Vai a" e ordinamento valgono per le cartelle dei singoli account; Sposta in, Copia,
  Categorizza e Posponi lavorano sui messaggi di un account alla volta.
- Account IMAP: le conversazioni non si raggruppano (il server non le conosce); sui server senza IDLE i nuovi messaggi
  arrivano con il controllo di ogni minuto.
- Bozze riprese con Modifica: il testo torna nell'editor di Missiva con il formato che Missiva sa scrivere (carattere,
  colori, allineamento, rientro, elenchi, tabelle, link, immagini messe in Missiva); gli stili più rari, le immagini da
  Internet e quelle del messaggio citato restano fuori o diventano testo semplice. Una risposta ripresa dalle Bozze
  parte come messaggio nuovo (fuori dalla conversazione).
- Messaggi fissati: restano solo in Missiva. Microsoft Graph non rende disponibile il "fissato" di Outlook, quindi
  quelli fissati in Outlook non risultano fissati qui, e viceversa.
- Riepilogo di Copilot: è un servizio di Microsoft, non replicabile.
- Account Microsoft: gli allegati possono pesare in tutto fino a 150 MB per messaggio, il limite di Outlook (Missiva lo
  dice quando aggiungi il file). Quelli da 3 MB in su si caricano a pezzi: la riga in fondo alla scrittura dice a che
  punto è il caricamento, e una volta nella bozza non si ricaricano. Se la linea cade a metà, il file non viene aggiunto
  e Missiva lo dice; chiudendo la scrittura il caricamento si ferma.

## Copia di sicurezza: cambiare PC o rimettere tutto com'era

Quello che in Missiva è tuo sta in un file solo, da rimettere su questo PC o su un altro. Copiare la cartella dei dati non
basta: è cifrata per il tuo utente di Windows, e altrove non si legge.

- **Fare una copia**: Impostazioni, Generali, Copia di sicurezza, **Esporta…**. Scegli una password (almeno 8 caratteri,
  scritta due volte) e dove salvare il file, che finisce in `.missiva-copia`. La copia è cifrata con quella password:
  senza non si apre, e Missiva non la conserva e non può recuperarla. Scrivila dove la ritrovi.
- **Che cosa contiene**: le impostazioni, gli account (indirizzi, server, altri indirizzi del Da), le firme, i modelli, le
  regole di Missiva, le categorie e i loro colori, i Preferiti e l'ordine delle cartelle, i contatti e i gruppi di
  Missiva, la rubrica dei destinatari suggeriti, i contrassegni con le date, i messaggi posposti, i mittenti di cui
  mostri le immagini. Al massimo 64 MB di dati.
- **Che cosa non contiene**: la posta (sta sui server, e si riscarica), i loghi dei mittenti (sul PC nuovo tornano man
  mano che Missiva rilegge la posta), gli invii pianificati di
  Gmail e degli altri account (le bozze restano nelle Bozze: ripianificale), e gli accessi agli account. Gli accessi a
  Microsoft e a Google non entrano mai. Le password degli account IMAP solo se accendi **Includi le password degli
  account IMAP**: è spenta di base, perché con quella casella chi ha il file e la sua password entra nelle tue caselle.
- **Rimetterla**: sul PC nuovo installa Missiva e aprila; nella finestra Aggiungi un account c'è "Hai una copia di
  sicurezza di Missiva fatta su un altro PC? **Importala**" (oppure Impostazioni, Generali, Copia di sicurezza,
  **Importa…**). Scegli il file e scrivi la password: Missiva ti dice di quando è la copia e che cosa contiene: gli
  account con i loro server, le regole in parole, i mittenti di cui mostrare sempre le immagini. Se c'è qualcosa da
  guardare prima di dire sì lo mette in cima: una regola che vale per tutti i messaggi, un account che su quel PC c'è già
  e nella copia ha un altro server. Se su quel
  PC Missiva ha già dei dati chiede "Sostituire tutto con questa copia?": importare sostituisce tutto, non unisce. I
  dati di prima non si cancellano: restano nella cartella dei dati con `prima-dell-importazione` e la data nel nome.
  Dentro ci sono anche gli accessi degli account di prima: quando non ti servono più, nella stessa pagina delle
  Impostazioni compare "Elimina i dati di prima…", che li toglie per sempre dopo una domanda. La rubrica dei
  destinatari arriva con nomi e indirizzi ma senza i conteggi: i suggerimenti si rimettono in ordine quando Missiva
  ha riletto la Posta inviata.
  Poi Missiva si riavvia (Riavvia ora; con Più tardi l'importazione aspetta il prossimo avvio, e dalla stessa pagina si
  può annullare).
- **Che cosa va rifatto sul PC nuovo**: l'accesso a ogni account. Dopo il riavvio gli account ci sono tutti, e sotto
  ognuno c'è una barra: **Accedi di nuovo** per Microsoft e Google, **Aggiorna password** per gli account IMAP di cui non
  hai incluso la password. La posta si riscarica da sé. Rimettendo la copia sullo stesso PC gli account che c'erano già
  tengono il loro accesso, se nella copia hanno gli stessi server (per Microsoft e Google la stessa app). Le regole di
  Missiva arrivano accese: l'avviso dopo il riavvio lo ricorda.
- Password sbagliata, file incompleto, file rovinato o modificato, copia fatta con una Missiva più nuova, copia con dentro
  qualcosa che Missiva non scrive (preparata o cambiata da altri): Missiva dice quale dei casi è, e non cambia nulla.
  Importa solo copie fatte da te: chi prepara una copia sceglie account, server e regole che ci sono dentro.

## File

- Impostazioni, account, token e password degli account IMAP (cifrati): `%LOCALAPPDATA%\Missiva`; la copia della posta
  per l'avvio e per leggere senza rete, cifrata: `%LOCALAPPDATA%\Missiva\cache`. Cifrati anche la rubrica dei
  suggerimenti, i contatti di Missiva (`contatti.dat`), la coda delle azioni fatte senza rete (`coda.dat`), i modelli, le regole di Missiva (nomi, indirizzi, parole e
  cartelle), le ricerche recenti, e oggetto e mittente dei messaggi posposti, pianificati e contrassegnati. Rimuovendo un account escono dal PC anche i suoi contrassegni, modelli, posposti, invii
  pianificati e firme (la posta sul server resta).
- Accanto a `impostazioni.json` c'è `impostazioni.bak`, il salvataggio precedente. Se il file delle impostazioni si rovina
  (un'interruzione di corrente, il disco), all'avvio Missiva lo mette da parte con la data nel nome
  (`impostazioni-illeggibile-...json`, mai cancellato né sovrascritto), riparte dalla copia di riserva e lo dice, con dove
  sono i file. Lo stesso per `contatti.dat` e `coda.dat` che non si leggono. I dati sono cifrati per il tuo utente di
  Windows: copiati su un altro PC o in un altro profilo, password e accessi vanno rifatti (lo dice la barra sotto
  l'account) e i testi cifrati non si leggono; nel file restano com'erano. Per cambiare PC c'è la copia di sicurezza
  (sopra).
- Registro degli errori: `%LOCALAPPDATA%\Missiva\missiva.log` (anche da Impostazioni, Generali, Informazioni, Apri la
  cartella dei registri; le Impostazioni si aprono con l'ingranaggio in fondo alla colonna a sinistra),
  senza oggetti dei messaggi né titoli degli eventi: si può mandare a chi ti aiuta.

## Compilare

Serve .NET 10 SDK: `dotnet build` compila la versione di prova, che si avvia con `strumenti\prova.ps1` su una copia dei
dati (avviata da sola, senza `MISSIVA_DATA`, si chiude subito: lavorerebbe sui dati veri); per installare
`installa.ps1`, con PowerShell 7 (vedi sopra). Compilata così Missiva non controlla gli aggiornamenti: solo un rilascio
(`-p:MissivaAggiornamenti=proprietario/repository` e una chiave pubblica in `Core/UpdateKey.cs`) lo fa.
