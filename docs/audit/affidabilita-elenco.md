# Audit di affidabilità: l'elenco che non si aggiorna in tempo (E-01..E-15)

25 ottobre 2026. Versione guardata: 0.3.628 (main `96a4bbe`, rilascio del 22 ottobre 2026), quella installata dall'autore.
Ramo `affidabilita-elenco`. È un audit: il codice di Missiva non è stato cambiato. Le sonde sono in un commit a parte
(`Core/ProvaBozze.SondeElenco.cs`, gruppo `sonde-elenco`), da non unire così com'è.

La richiesta dell'autore: "ogni tanto sembra che le mail arrivano tardi, che non si aggiorni in tempo, che i messaggi letti
o non letti ogni tanto si perdano per strada". Questa parte guarda quello che succede quando un cambiamento sul server
c'è (o è già stato visto) e l'elenco, le cartelle, i conteggi e le viste devono cambiare: messaggi arrivati che non
compaiono, eliminati o spostati altrove che restano, righe in un ordine sbagliato, numeri delle cartelle fermi. La catena
dell'arrivo (IDLE, controllo ogni due minuti, query delta) e lo stato letto/non letto in sé sono delle altre due parti.

Come ho lavorato: letto il codice delle voci di CLAUDE.md "Non letti ed elenco dopo un arrivo", "Nuovi messaggi subito",
"Tutti gli account", "Avvio veloce", "Elenco come nel nuovo Outlook", "Annulla", "Lavoro senza rete", "Menu delle
cartelle" e di `docs/audit/ESITO.md` per l'elenco; riprodotto sui server finti della suite (FakeImap, gestore finto di
Graph) quello che si poteva riprodurre senza la finestra; il resto è letto nel codice, con file e riga. Nessuna prova a
finestra, nessuna rete vera, nessun dato vero.

## Riassunto

In ordine di quanto spiegano quello che l'autore vede.

| Sigla | Che cosa vede l'autore | Stato | Quanto dura |
|---|---|---|---|
| E-01 | Aperta Missiva (o dopo la sospensione) l'elenco è quello dell'ultima volta, senza avvisi, e non si aggiorna più da solo | CONFERMATO (codice) | fino a un cambio di cartella o Sincronizza (F5): ore |
| E-02 | Una mail arrivata non compare nell'elenco aperto: letta sul telefono appena arrivata, rimessa in Posta in arrivo, oltre la ventesima | CONFERMATO (sonda) | fino alla rilettura |
| E-03 | Al risveglio del portatile le mail eliminate o archiviate dal telefono intanto sono ancora lì (Gmail, IMAP) | CONFERMATO (sonda) | fino alla rilettura |
| E-06 | I numeri delle cartelle (non Posta in arrivo) sono fermi; in una cartella o etichetta aperta i nuovi non compaiono | CONFERMATO (codice) | fino a F5 o al riavvio: giorni |
| E-04 | Una discussione eliminata dal telefono, o una risposta eliminata dalla notifica, resta come riga "(2)" | CONFERMATO (sonda) | fino alla rilettura |
| E-05 | Tutti gli account si aggiorna tardi: l'account più lento ferma gli altri; arrivi che compaiono e spariscono; eliminati che restano | CONFERMATO (codice) | secondi o minuti; alcuni fino alla rilettura |
| E-09 | Outlook.com: una newsletter di Altra compare in Evidenziata e poi sparisce | CONFERMATO (sonda) | fino alla rilettura |
| E-07 | Una mail eliminata ricompare un attimo dopo; una mail appena notificata compare e sparisce | CONFERMATO (sonde) | fino alla rilettura |
| E-08 | Come E-03 senza sospensione: l'avviso di un messaggio uscito si perde mentre un'altra pagina carica | CONFERMATO (codice) | fino alla rilettura |
| E-11 | Outlook.com: un messaggio vecchio eliminato o letto altrove resta com'era | SOSPETTO | fino alla rilettura |
| E-10 | Con un filtro, un ordine o "Vai a" i nuovi entrano in cima anche se non c'entrano | CONFERMATO (codice) | fino alla rilettura |
| E-12 | Eliminati altrove più in basso nell'elenco che restano; gruppi di date fuori ordine dopo un messaggio rimesso | CONFERMATO (codice) | fino alla rilettura |
| E-13 | Posta inviata aperta senza il messaggio appena inviato; due finestre sulla stessa cartella che non si somigliano | CONFERMATO (codice) | fino alla rilettura |
| E-14 | Per qualche secondo dopo ogni azione di Missiva gli avvisi veri del server si buttano | CONFERMATO (codice) | fino alla rilettura |
| E-15 | Minori: categorie e scadenze cambiate altrove; un elenco che resta su "Ricerca in corso…" | CONFERMATO (codice) | fino alla rilettura |

Il filo comune: l'elenco aperto si rilegge dal server solo quando si cambia cartella, con Sincronizza (F5), o per pochi
avvisi precisi (l'IDLE della sola Posta in arrivo che dice di un messaggio uscito o di un segno senza UID, la coda senza
rete ripartita, un ritorno da Posponi, Annulla). Tutto il resto aggiorna le righe a pezzi, e quello che i pezzi non
coprono resta com'era. Per gli arrivi c'è una rete di sicurezza (il controllo di ogni due minuti); per l'elenco no.
Un clic sulla cartella già aperta non la rilegge (la rilettura parte solo da SelectionChanged,
`UI/MainWindow.xaml.cs:2078-2083`, e la selezione non cambia; letto nel codice, non provato a finestra), quindi il gesto
naturale dell'autore non serve.

Le tre cose da correggere per prime:

1. **Una rete di sicurezza per l'elenco aperto** (E-01, E-03, E-08, e in parte E-02, E-06, E-11): rileggere la prima
   pagina della cartella aperta con `RefreshListAsync` (che unisce senza togliere selezione e pagine caricate) quando la
   prima pagina non è arrivata e l'account torna a rispondere, quando l'IDLE riprende dopo una caduta, alla riapertura
   della finestra dall'area di notifica se l'elenco è più vecchio di qualche minuto, e una volta ogni pochi minuti per la
   cartella aperta. Con la prima pagina fallita, una riga discreta "copia del ... non aggiornata, Riprova".
2. **Gli arrivi che il controllo non spiega fanno rileggere** (E-02): un avviso Arrived dell'IDLE, o un messaggio nuovo
   della query delta che l'elenco non ha, dopo il controllo fa `RefreshInboxOf` se il controllo non ha trovato tutti i
   messaggi in più. Copre i messaggi letti sul telefono nei venti secondi di ritardo dell'IDLE di Gmail, quelli rimessi in
   Posta in arrivo e quelli oltre il ventesimo.
3. **Conteggi delle cartelle e cartelle diverse dalla Posta in arrivo** (E-06): nel controllo periodico rileggere i
   conteggi (LIST-STATUS per Gmail e IMAP, già usato all'avvio; `mailFolders` per Graph) con `SetServerFolders`, e la
   cartella aperta se non è la Posta in arrivo.

Subito dopo, perché costano poco: E-04 (togliere il messaggio dalla conversazione invece di lasciare la riga) ed E-09
(la scheda Evidenziata/Altra nei messaggi nuovi di Graph).

## Come si rifanno le prove

`dotnet build` nell'albero, poi, con `MISSIVA_DATA` verso una cartella nuova:

    bin\Debug\net10.0-windows10.0.19041.0\win-x64\Missiva.exe --prova-audit risultato.txt sonde-elenco

Eseguito il 25 ottobre 2026: 9 controlli, tutti "ok" (in queste sonde "ok" vuol dire che il difetto si vede), 2,3 s.
Dopo una correzione la sonda corrispondente deve dare NO, e va rovesciata in un controllo vero. Nello stesso giro, sullo
stesso albero, i gruppi `arrivi` (4 controlli), `idle` (13) e `prove` (11, che controlla anche i nomi dei gruppi) sono
verdi: nessun controllo vecchio cade.

| Sonda | Punto | Che cosa fa |
|---|---|---|
| `ProbeNotNewAsync` (IMAP e Gmail) | E-02 | arrivo già letto, messaggio rimesso con data di un mese fa, 25 nuovi insieme; `GetNewAsync` contro la prima pagina |
| `ProbeIdleGapAsync` (IMAP) | E-03 | sessione IDLE, connessione tagliata, sul server un eliminato e un letto, sessione ripresa: quali avvisi |
| `ProbePageBeforeMoveAsync` (IMAP e Gmail) | E-07 | prima pagina trattenuta sul server, intanto `MoveAsync` nel Cestino; poi le chiamate di RemoveRows e ShowFirstPage |
| `ProbePageBeforeArrivalAsync` (IMAP e Gmail) | E-07 | prima pagina trattenuta, intanto un arrivo trovato da `GetNewAsync`; poi le chiamate di AddIncoming e RefreshListAsync |
| `ProbeConversationRows` | E-04 | la regola `Count == 1` di ApplyInboxChanges e RemoveNotifiedRow su una conversazione di due messaggi |
| `ProbeGraphFocusedAsync` | E-09 | gestore finto di Graph: la richiesta dei nuovi messaggi e quella dell'elenco di Evidenziata |

La suite non costruisce `MainWindow`: dove il difetto sta nella finestra (E-04, E-07) la sonda rifà le stesse chiamate a
`MessageRows` che fa il codice della finestra, con file e riga nel testo del controllo. I punti senza sonda sono letti
nel codice.

## Punti

### E-01 Prima pagina non arrivata con la copia locale già visibile: l'elenco resta la copia, in silenzio, per sempre

- **Che cosa vede l'autore.** "L'elenco non si aggiorna": accesa Missiva con Windows, o tornato al PC dopo la
  sospensione, la Posta in arrivo mostra i messaggi dell'ultima volta. Mancano quelli arrivati mentre il PC era spento o
  in sospensione; scorrendo in fondo non arrivano altri messaggi; nessun avviso. Il numero sulla cartella e sull'icona
  invece è giusto (lo conta il controllo): "il numero dice 3 da leggere, ma nell'elenco sono tutti letti". Si sistema
  cambiando cartella e tornando, o con Sincronizza (F5); un clic sulla cartella già scelta non fa nulla.
- **Quando succede.** `LoadMoreAsync(first: true)` mostra subito la copia locale (`UI/MainWindow.xaml.cs:2401-2410`).
  Se `GetMessagesAsync` non riesce (rete non ancora pronta all'accesso a Windows, Wi-Fi che si ricollega dopo la
  sospensione, connessione principale morta che scade dopo 120 s con Gmail e 60 s con IMAP, accesso che non si rinnova,
  un errore del server) il `catch` scrive nel registro e, con la copia visibile, non mostra nulla né Riprova
  (`:2434-2438`, la condizione `!fromCache`). `next` resta null (azzerato da ReloadAsync a `:2376`): le pagine dopo non
  partono (`:2395`). Poi nessuna strada rilegge l'elenco aperto quando l'account torna a rispondere: `NoteAccountState`
  rilegge le cartelle e riparte la coda (`:1259-1287`), `AccountReached` lo stesso (`:1244-1250`), `App.OnNetworkBack`
  fa il controllo (`UI/App.cs:324-339`), la coda rilegge l'elenco solo se ha ripetuto qualcosa
  (`UI/MainWindow.Offline.cs:116`), e la lettura riuscita delle cartelle aggiorna gli stessi `FolderItem` senza
  rileggere (`UI/ViewModels.cs:276-304`; `ListAccountsAsync` richiama `SelectStart` solo se nessuna cartella è aperta,
  `UI/MainWindow.xaml.cs:1073-1074`). Il controllo dei nuovi messaggi aggiunge in cima solo i nuovi da leggere arrivati
  dopo l'avvio (`lastCheck` parte dall'avvio, `UI/App.cs:892`, `:1137`): quelli arrivati prima non entrano mai.
  `LoadMoreAsync` non chiama `Network.Note`, quindi l'errore dell'elenco non segna nemmeno l'account come non in linea.
  `RefreshListAsync` parte solo dagli avvisi dell'IDLE di messaggi usciti o di segni senza UID, e solo per la Posta in
  arrivo aperta (`UI/MainWindow.Notifications.cs:292`); Gmail manda i segni con l'UID (cambia solo la riga), quindi in
  pratica solo quando qualcosa viene eliminato o spostato altrove. Non c'è un gestore del risveglio dalla sospensione
  (nessun `PowerModeChanged` in `UI/`), e la riapertura dalla finestra dall'area di notifica non rilegge (`Activated`
  fa solo `CheckDay` e il numero dei nuovi, `UI/MainWindow.xaml.cs:193-195`).
- **Stato.** CONFERMATO, letto nel codice (righe sopra). Non riprodotto con una sonda: il percorso è tutto dentro
  `MainWindow`.
- **Quanto spesso, quanto è grave.** Ogni volta che la prima pagina non arriva e c'è una copia locale: all'avvio con
  Windows con la rete in ritardo (è il caso di A-15 nell'audit del 7 ottobre, parte A, provato dal vivo allora: stato
  dell'account e cartelle si rimettono a posto da soli quando la rete torna, l'elenco no, e lì non era guardato), dopo la
  sospensione con la connessione principale morta (registro del 6 ottobre: "Operation timed out after 120000
  milliseconds"). Lo stato vecchio resta fino a un gesto dell'autore: con Missiva nell'area di notifica possono essere
  ore. Nel registro della versione installata si riconosce: una riga "ERRORE (elenco messaggi): ..." e dopo, per quella
  cartella, nessun cambio.
- **Rimedio.** Segnare l'elenco "solo copia" quando la prima pagina fallisce e rileggerlo (RefreshListAsync, che rimette
  anche `next`) quando l'account torna a rispondere (NoteAccountState senza errore, AccountReached, IDLE Connected) o al
  minuto dopo; nella riga di stato dell'elenco "Copia del ...: non aggiornata. Riprova".
- **Prova che lo terrebbe fermo.** La decisione ("l'elenco aperto va riletto?") in una funzione pura dello stato
  dell'elenco e dell'evento dell'account, con i suoi controlli; un controllo sui sorgenti che NoteAccountState e
  AccountReached la chiamano; sui server finti `FailOn` sulla prima FETCH e la pagina arrivata alla seconda.

### E-02 Gli arrivi che non sono "nuovi da leggere" non entrano mai nell'elenco aperto

- **Che cosa vede l'autore.** "La mail è arrivata (la vedo sul telefono) ma su Missiva non c'è" finché non cambia
  cartella: un messaggio aperto sul telefono appena arrivato; un messaggio rimesso in Posta in arrivo dal telefono o dal
  sito (Sposta in Posta in arrivo, Non è spam, annulla archiviazione); dopo una notte, i nuovi oltre il ventesimo.
- **Quando succede.** Un arrivo entra nell'elenco aperto solo da `App.CheckAccountAsync` e `MainWindow.AddIncoming`
  (`UI/App.cs:1140`, `:1182-1183`; `UI/MainWindow.xaml.cs:2740-2762`), con i soli messaggi di `GetNewAsync`: da leggere,
  con la data di arrivo dopo l'ultimo controllo, al massimo i 20 più recenti (`Core/ImapProvider.cs:899-908`,
  `Core/GmailProvider.cs:573-583`, `Core/GraphProvider.cs:592-599`). L'avviso Arrived dell'IDLE fa il controllo, e lo
  ripete dopo 3 e 10 s, ma non rilegge mai la Posta in arrivo: la rilettura è solo per segni e usciti
  (`UI/App.cs:975-977`, `:988-994`; il commento a `:986-987` dice già che "un messaggio già letto ... non si troverà
  mai"). Con Gmail l'IDLE avvisa circa venti secondi dopo l'arrivo (misura del 27 settembre in CLAUDE.md, "Controllo dei
  nuovi messaggi"): un messaggio aperto sul telefono in quel tempo è già letto quando Missiva lo cerca, e la FETCH dei
  segni che segue non trova righe (`ApplyIdleFlags`). Graph: la query delta vede il messaggio (Seen, letto) ma
  `ApplyInboxChanges` cambia solo le righe che ci sono (`UI/MainWindow.Notifications.cs:268-283`). Lo stesso nella vista
  Posta in arrivo di Tutti gli account (`AddIncomingUnified`) e nella copia locale (`KeepInSavedPage`).
- **Stato.** CONFERMATO, riprodotto con `ProbeNotNewAsync` (IMAP e Gmail): `GetNewAsync` dà 0 per l'arrivo già letto,
  0 per il messaggio rimesso con data di un mese fa, 20 di 25 nuovi; la prima pagina del server li ha tutti (32 righe).
  Per Graph letto nel codice.
- **Quanto spesso, quanto è grave.** Ogni volta che l'autore apre sul telefono una mail appena arrivata prima del
  controllo di Missiva (con Gmail i venti secondi bastano), o rimette un messaggio in Posta in arrivo; i più di 20 dopo
  una sospensione lunga. Il messaggio manca dall'elenco aperto fino alla rilettura. Quelli oltre il ventesimo mancano anche
  dalle notifiche (parte "arrivo").
- **Rimedio.** Dopo un avviso Arrived (o una delta con un Seen che l'elenco non ha) rileggere la Posta in arrivo aperta
  (`RefreshInboxOf`) se il controllo non ha trovato tanti nuovi quanti sono i messaggi in più, o sempre: costa un NOOP e
  una FETCH.
- **Prova.** `ProbeNotNewAsync` rovesciata su una funzione "l'arrivo è spiegato dal controllo?", più un controllo sui
  sorgenti che OnInboxEvent la usa anche per Arrived.

### E-03 Dopo la sospensione o una caduta della rete l'IDLE riparte da zero: quello che è cambiato intanto non arriva

- **Che cosa vede l'autore.** Al risveglio del portatile: "ho eliminato (o archiviato) la mail dal telefono e su Missiva
  c'è ancora"; "letta sul telefono, qui è ancora da leggere" (lo stato letto è della parte "letti").
- **Quando succede.** La sessione IDLE nuova apre la Posta in arrivo e prende il conteggio dopo l'apertura
  (`Core/Idle.cs:253-256`): quello che è successo mentre la connessione era caduta non dà avvisi, né EXPUNGE né FETCH.
  L'avviso Connected fa solo `AccountReached` (`UI/App.cs:962-966`), che rilegge solo un account segnato non in linea.
  Il controllo dei nuovi messaggi aggiunge i nuovi, non rilegge l'elenco. Non c'è CONDSTORE né QRESYNC (CLAUDE.md:
  "Non ancora: CONDSTORE"), né un gestore del risveglio. Graph non ha il problema: la query delta riparte dal deltaLink e
  vede gli usciti (con i limiti di E-04 ed E-11).
- **Stato.** CONFERMATO, riprodotto con `ProbeIdleGapAsync`: sessione IDLE in corso, connessione tagliata, sul server un
  messaggio eliminato e uno letto, sessione ripresa: l'unico avviso è "Connected"; righe prima 4, sul server 3.
  Gmail usa la stessa `InboxIdle.SessionAsync` (`Core/GmailProvider.cs:1488-1489`): stesso comportamento, non eseguito.
- **Quanto spesso, quanto è grave.** A ogni risveglio da una sospensione abbastanza lunga da far chiudere la connessione
  dell'IDLE, se intanto qualcosa è stato eliminato, spostato o letto altrove (il portatile dell'autore va in
  sospensione; al mattino si legge sul telefono). La riga resta fino alla rilettura; aprendola, il messaggio non si trova.
- **Rimedio.** Quando una sessione IDLE riparte dopo un errore (non al rinnovo dei 9 minuti: InboxIdle.RunAsync sa la
  differenza) rileggere la Posta in arrivo aperta e ricontare; più avanti CONDSTORE/QRESYNC per chiedere solo il cambiato.
- **Prova.** `ProbeIdleGapAsync` rovesciata: dopo la ripresa un avviso che fa rileggere, e nessuno al rinnovo normale.

### E-04 Le conversazioni restano anche quando i loro messaggi sono usciti

- **Che cosa vede l'autore.** "Ho eliminato la discussione dal telefono e la riga è ancora lì", con il numero "(2)" e
  l'oggetto e il mittente del messaggio eliminato; lo stesso dopo Elimina o Posponi dalla notifica di una risposta.
- **Quando succede.** `ApplyInboxChanges` toglie solo le righe con `Count == 1`
  (`UI/MainWindow.Notifications.cs:277`); `RemoveNotifiedRow` lo stesso (`:323`), con il commento "la ricarica il
  controllo dopo", che non è vero: il controllo non rilegge l'elenco. RemoveNotifiedRow la usano Elimina dalla notifica
  (`:240-262`), Posponi dalla notifica (`:214-233`) e le regole (`:312-318`). Le conversazioni sono accese di base
  (`Core/Store.cs:71`) per Gmail (X-GM-THRID, `Core/GmailProvider.cs:686`) e Graph (conversationId,
  `Core/GraphProvider.cs:707`); IMAP non ne ha. `Count` è il numero dei messaggi della conversazione anche per i figli
  di una conversazione aperta (`UI/ViewModels.cs:700`), quindi non esce nemmeno la riga figlia del messaggio uscito. Con
  Gmail l'EXPUNGE che segue Elimina dalla notifica è un eco e si ignora (`UI/App.cs:968-974`); con Graph la query delta
  lo dice, ma la riga ha Count 2.
- **Stato.** CONFERMATO, riprodotto con `ProbeConversationRows`: una conversazione di due messaggi e un messaggio da
  solo, tutti e tre usciti: resta la riga "r2(2)".
- **Quanto spesso, quanto è grave.** Con Outlook.com ogni messaggio eliminato o spostato altrove che sta in una
  conversazione con un altro della Posta in arrivo; con Gmail e Outlook.com Elimina o Posponi dalla notifica di una
  risposta. Fino alla rilettura.
- **Rimedio.** Un metodo di MessageRows che toglie un messaggio dalla sua conversazione (testa nuova, o riga tolta se
  resta vuota) al posto del `Count == 1`, in ApplyInboxChanges e RemoveNotifiedRow.
- **Prova.** `ProbeConversationRows` rovesciata sul metodo nuovo: nessuna riga resta, e con un messaggio solo uscito la
  testa diventa l'altro.

### E-05 Tutti gli account: l'account più lento ferma gli altri, gli arrivi intanto si perdono, gli usciti restano

- **Che cosa vede l'autore.** "Tutti gli account si aggiorna tardi": Non letti, Contrassegnati, Bozze e Posta inviata
  fermi su "Caricamento…", la Posta in arrivo ferma sulla copia locale; una mail notificata in quel momento compare e
  sparisce; mail eliminate dal telefono che nella vista restano.
- **Quando succede.** (a) `NextMergedAsync` aspetta tutte le fonti con `Task.WhenAll`
  (`UI/MainWindow.Unified.cs:208`) e mostra le righe solo dopo: finché l'ultimo account non risponde o non va in errore
  (fino a 120 s Gmail e 60 s IMAP con una connessione morta) non si vede nessun account. (b) Con la copia locale
  visibile, all'arrivo delle pagine `rows.Clear()` e le pagine (`:164-176`): i nuovi messi in cima intanto da
  `AddIncomingUnified` (`:261-266`) non stanno nelle pagine chieste prima e spariscono (come E-07). (c) Gli avvisi
  dell'IDLE di messaggi usciti non rileggono le viste: `RefreshInboxOf` guarda solo una cartella Posta in arrivo aperta
  (`UI/MainWindow.Notifications.cs:292`); per Graph `ApplyInboxChanges` toglie le righe, con il limite di E-04. (d) Bozze,
  Posta inviata e Contrassegnati di tutti gli account si rileggono solo per le azioni di Missiva (`OnDraftsChanged`,
  `UI/MainWindow.xaml.cs:4805-4809`; `UI/MainWindow.Flags.cs:150`), mai per quello fatto altrove né dopo un invio.
- **Stato.** CONFERMATO, letto nel codice. Non riprodotto: le viste sono in `MainWindow`.
- **Quanto spesso, quanto è grave.** (a) a ogni apertura della vista con un account lento: di solito secondi, dopo una
  sospensione fino a due minuti; (b) raro, fino alla rilettura; (c) come E-03, anche senza sospensione, fino alla
  rilettura.
- **Rimedio.** Mostrare le pagine di ogni account appena arrivano, fino a dove `Cutoff` permette (Release dopo ogni
  FetchAsync, non dopo WhenAll), con un tempo massimo oltre il quale l'account lento "arriva dopo"; rifare le righe senza
  perdere quelle arrivate dopo la richiesta (E-07); `RefreshInboxOf` anche per le viste Posta in arrivo e Non letti.
- **Prova.** Il rilascio in una funzione pura (fonti con pagine e tempi): un account fermo non trattiene gli altri e un
  messaggio più vecchio non passa davanti a uno che deve arrivare (la regola di Cutoff di oggi).

### E-06 Le cartelle diverse dalla Posta in arrivo e i loro numeri non si aggiornano mai da soli

- **Che cosa vede l'autore.** "I numeri delle cartelle sono fermi"; in una cartella o un'etichetta di Gmail aperta (dove
  i filtri del server mettono la posta) i nuovi non compaiono; cartelle create o eliminate dal telefono o dal sito che
  non compaiono o restano.
- **Quando succede.** IDLE, controllo e query delta guardano solo la Posta in arrivo. L'elenco di un'altra cartella
  aperta si rilegge solo cambiando cartella o con F5: `RefreshListAsync` la chiamano solo i ritorni da Posponi
  (`UI/MainWindow.xaml.cs:4503`, solo per la Posta in arrivo e Posposti), Annulla (`:3836-3878`), la coda ripartita
  (`UI/MainWindow.Offline.cs:116`), le regole eseguite a mano (`UI/MainWindow.xaml.cs:5133`) e RefreshInboxOf (solo Posta
  in arrivo). I numeri delle cartelle (`UnreadText`, `UI/ViewModels.cs:666`: tutte tranne Posta inviata, Cestino e
  Bozze, anche nei Preferiti) vengono solo da `LoadFoldersAsync` (`UI/MainWindow.xaml.cs:1199-1228`), chiamata all'avvio
  (`:1073`), dopo una lettura non riuscita (`:1271`), con F5 (`:5226`), dopo le azioni sulle cartelle (`:1928`, `:2051`,
  `:3427`), i ritorni da Posponi (`:4502`), un'etichetta nuova di Gmail (`:4687`), Gestisci account e il consenso
  (`:1318`, `:1363`), Posponi con una cartella nuova (`:4374`, `UI/MainWindow.Notifications.cs:227`); mai dal controllo
  periodico. Spostando un messaggio da leggere in una cartella il suo numero non sale (`CountRead` conta solo le letture,
  `UI/ViewModels.cs:97-102`). Lo stesso per le finestre di una sola cartella aperte su una cartella che non è la Posta in
  arrivo (ricevono solo gli avvisi della Posta in arrivo).
- **Stato.** CONFERMATO, letto nel codice.
- **Quanto spesso, quanto è grave.** Di continuo per chi usa i filtri del server (Gmail "salta la Posta in arrivo",
  regole di Outlook.com, filtri IMAP); con Missiva aperta per giorni i numeri sono di giorni prima. Si rimette con F5 o un
  riavvio.
- **Rimedio.** Nel controllo periodico (ogni giro o ogni pochi giri) rileggere i conteggi con `SetServerFolders`
  (LIST-STATUS, già usato all'avvio; `mailFolders` per Graph) e l'elenco aperto se non è la Posta in arrivo.
- **Prova.** Sui server finti un messaggio aggiunto in una cartella dell'utente: dopo il giro il suo conteggio cambia, con
  il giro richiamabile senza finestra.

### E-07 Una pagina chiesta prima rifà l'elenco dopo: righe eliminate che tornano, arrivi che spariscono

- **Che cosa vede l'autore.** "Ho eliminato una mail e dopo un attimo è ricomparsa" (aprendola: messaggio non trovato);
  "la notifica è arrivata, la riga è comparsa in cima e poi è sparita".
- **Quando succede.** La versione dell'elenco (`listVersion`) cambia solo con un elenco nuovo
  (`UI/MainWindow.xaml.cs:2365`, `:4979`, `UI/MainWindow.Unified.cs:298`), non con Elimina o Sposta (`:3503-3504`, che
  la usano solo per Annulla) né con un arrivo (`:2740-2762`): una prima pagina chiesta prima e arrivata dopo passa il
  controllo (`:2417`, `:4567`), e ShowFirstPage o MergeFirstPage rifanno le righe con lei (`:2482-2489`, `:2515-2523`).
  Con Gmail e IMAP lo spostamento sta in fila dietro la pagina sulla stessa connessione principale
  (`Core/ImapProvider.cs:264-298`), quindi la pagina ha sempre il messaggio; l'EXPUNGE che torna all'IDLE è un eco e si
  ignora (`UI/App.cs:968-974`): la riga resta fino alla rilettura. Per un arrivo: AddIncoming lo mette in cima e in
  `firstPage` (`:2760`, `:2801-2803`), la pagina chiesta prima lo toglie, SaveFirstPage salva senza, e il controllo dopo
  non lo ripropone (è fra i notificati, `UI/App.cs:1140`, `:1176`). È il punto lasciato in `ESITO.md` ("Dopo
  l'installazione", Non verificato): "un arrivo proprio mentre la prima pagina è in viaggio".
- **Stato.** CONFERMATO, riprodotto con `ProbePageBeforeMoveAsync` e `ProbePageBeforeArrivalAsync` (IMAP e Gmail): con la
  FETCH della pagina trattenuta sul server finto, lo spostamento aspetta la pagina, la pagina arriva con 6 messaggi (sul
  server 5), `OwnEcho` è vero, e rifatte le chiamate della finestra la riga eliminata torna; l'arrivo trovato intanto dal
  controllo non è nella pagina e sparisce dalle righe e dalla prima pagina salvata.
- **Quanto spesso, quanto è grave.** Serve una prima pagina in viaggio (apertura di una cartella, rilettura dopo un
  avviso, F5) mentre si elimina o arriva qualcosa: con la rete buona, a stima, mezzo secondo o un secondo (non misurato
  qui), molto di più con la rete lenta o subito dopo la sospensione. Poco frequente; lo stato sbagliato resta fino alla rilettura.
- **Rimedio.** Annotare le azioni e gli arrivi successivi alla richiesta della pagina (un contatore come quello di
  UnreadSync) e applicarli alla pagina arrivata: togliere gli spostati da allora (come fa `ActionQueue.Apply` per la
  coda), rimettere gli arrivati da allora.
- **Prova.** Le due sonde rovesciate, sulla funzione che unisce pagina e cambiamenti.

### E-08 La rilettura dopo un avviso dell'IDLE si perde se un'altra pagina sta caricando

- **Che cosa vede l'autore.** Come E-03 (eliminato dal telefono che resta), senza sospensione.
- **Quando succede.** `RefreshInboxOf` e `RefreshListAsync` non fanno nulla con `loading` vero
  (`UI/MainWindow.Notifications.cs:292`, `UI/MainWindow.xaml.cs:4561`): mentre si scorre (pagina dopo in viaggio) o
  mentre la prima pagina carica, l'avviso di un messaggio uscito si butta e non si ripete.
- **Stato.** CONFERMATO, letto nel codice.
- **Quanto spesso, quanto è grave.** Basso da solo; con E-01 di più, perché una prima pagina appesa fino a 120 s fa
  perdere tutti gli avvisi di quel tempo.
- **Rimedio.** Segnare la rilettura come da fare e farla alla fine del caricamento in corso.
- **Prova.** Controllo sui sorgenti, o la funzione di E-01 con lo stato "caricamento in corso".

### E-09 Outlook.com con Evidenziata e Altra: i messaggi di Altra entrano in Evidenziata e poi spariscono

- **Che cosa vede l'autore.** "Una newsletter compare in cima a Evidenziata e poi sparisce" (alla rilettura).
- **Quando succede.** `GetNewAsync` di Graph chiede tutta la Posta in arrivo, senza `inferenceClassification`, e i campi
  letti non la comprendono (`Core/GraphProvider.cs:59`, `:592-599`; `MessageSummary` non ha la scheda). `AddIncoming`
  esclude solo la scheda Altra aperta (`UI/MainWindow.xaml.cs:2755`): con Evidenziata aperta mette in cima anche i
  messaggi di Altra. La prima rilettura (l'elenco filtra, `Core/GraphProvider.cs:537`) li toglie.
- **Stato.** CONFERMATO, riprodotto con `ProbeGraphFocusedAsync` (gestore finto di Graph): la richiesta dei nuovi è
  "receivedDateTime gt ... and isRead eq false", quella di Evidenziata ha "inferenceClassification eq 'focused'".
- **Quanto spesso, quanto è grave.** A ogni arrivo in Altra con Evidenziata aperta (l'account Outlook.com dell'autore ha
  le due schede, ESITO.md). Una riga di troppo fino alla rilettura. Non tocca KeepInSavedPage, che per questi account già
  non scrive (scelta scritta in CLAUDE.md).
- **Rimedio.** `inferenceClassification` in SummaryFields e nel messaggio; in AddIncoming, con una scheda aperta, solo i
  messaggi di quella scheda.
- **Prova.** `ProbeGraphFocusedAsync` rovesciata, più una funzione pura "entra in questa vista" (anche per E-10).

### E-10 Con un filtro, un ordine o "Vai a" i nuovi messaggi entrano lo stesso in cima

- **Che cosa vede l'autore.** "Ho filtrato i contrassegnati e in cima c'è una mail che non lo è"; con "dal più vecchio"
  o "Vai a" una mail di oggi in cima.
- **Quando succede.** AddIncoming guarda cartella, ricerca e scheda, non `listOptions` (`UI/MainWindow.xaml.cs:2755-2760`);
  KeepInSavedPage invece sì (`:2796`).
- **Stato.** CONFERMATO, letto nel codice.
- **Quanto spesso, quanto è grave.** Basso (filtri poco usati); innocuo con "Da leggere". Fino alla rilettura.
- **Rimedio.** Con `listOptions` non di base mettere in cima solo i messaggi che la vista mostrerebbe (filtro, data entro
  "Vai a", ordine per data dal più recente); gli altri arrivano alla rilettura.
- **Prova.** La funzione pura di E-09.

### E-11 Graph: la query delta segue solo i messaggi arrivati dai due giorni prima dell'avvio (SOSPETTO)

- **Che cosa vede l'autore.** Nella Posta in arrivo di Outlook.com un messaggio vecchio eliminato o letto altrove resta
  com'era.
- **Quando succede.** La prima lettura fissa `$filter=receivedDateTime ge` l'avvio meno due giorni, e il deltaLink lo
  porta con sé per tutta la sessione (`Core/GraphProvider.cs:652-653`). Se la query delta tiene il filtro anche per i
  cambiamenti, i messaggi più vecchi non danno né `@removed` né `isRead`, e nessun'altra strada aggiorna le righe aperte
  di Graph (niente IDLE).
- **Stato.** SOSPETTO: il codice è certo, il comportamento del server no; serve una prova su una casella vera, che qui
  non si fa. Il gestore finto risponde come lo si scrive.
- **Quanto spesso, quanto è grave.** Ogni eliminazione o lettura altrove di un messaggio più vecchio di due giorni con la
  Posta in arrivo di Outlook.com aperta. Fino alla rilettura.
- **Rimedio.** La rete di sicurezza dell'elenco (prima cosa da correggere) lo copre; in più, alla delta con
  cambiamenti, rileggere la prima pagina aperta.
- **Prova.** Solo sul server vero: eliminare dal sito un messaggio di una settimana prima e guardare la delta.

### E-12 La prima pagina riletta tiene tutto il caricato scorrendo, e un messaggio rimesso con una data vecchia rompe l'ordine

- **Che cosa vede l'autore.** Mail eliminate dal telefono più in basso nell'elenco che restano; dopo Annulla di
  un'archiviazione o un messaggio rimesso in Posta in arrivo, scorrendo, gruppi di date fuori ordine ("Settimana scorsa"
  sotto un mese prima).
- **Quando succede.** MergeFirstPage tiene tutti i messaggi caricati più vecchi dell'ultimo della pagina
  (`UI/MainWindow.xaml.cs:2505`): escono solo quelli che stavano nei primi 50 (lo dice il suo commento, `:2492-2497`).
  Gmail e IMAP prendono le pagine per numero d'ordine, cioè di arrivo, e le ordinano per data dentro la pagina
  (`Core/ImapProvider.cs:627-640`, `Core/GmailProvider.cs:426-442`, scelta del giro P4 in CLAUDE.md): un messaggio con una
  data vecchia arrivato ora sta in fondo alla prima pagina; le pagine dopo hanno messaggi più recenti di lui, e i loro
  gruppi di date nuovi finiscono dopo il suo (Append non rifà i gruppi; che WPF metta in fondo un gruppo nuovo lo dice il
  commento di PrependRows, `:2815-2820`). Con quel messaggio nella pagina `oldest` diventa vecchissima, e alla rilettura
  le pagine caricate scorrendo escono (tornano scorrendo).
- **Stato.** CONFERMATO, letto nel codice; l'ordine dei gruppi non visto a finestra.
- **Quanto spesso, quanto è grave.** Basso.
- **Rimedio.** Le eliminazioni lontane le copre la rilettura periodica (non tutte: solo la prima pagina) o QRESYNC; per
  l'ordine, rifare i gruppi dopo una pagina che apre un gruppo più recente dell'ultimo.
- **Prova.** Sul modello delle righe: una pagina con un messaggio di un mese prima in fondo e una pagina dopo più recente.

### E-13 Posta inviata dopo un invio, e due finestre sulla stessa cartella

- **Che cosa vede l'autore.** Posta inviata aperta (anche quella di Tutti gli account) senza il messaggio appena
  inviato; la stessa cartella aperta in due finestre ("Apri in un'altra finestra") con righe diverse.
- **Quando succede.** Dopo Invia, `AfterSent` avvisa solo le Bozze (`UI/ComposeWindow.xaml.cs:1832-1838`,
  `UI/MainWindow.xaml.cs:4805-4809`). Le finestre di una sola cartella ricevono arrivi, usciti dalle regole, segni e
  riletture (`FolderWindows`, `UI/MainWindow.xaml.cs:2770`; `UI/MainWindow.Notifications.cs:268-318`), ma non Elimina,
  Sposta e letto fatti nell'altra finestra (MoveAsync, DeleteAsync, SetReadAsync cambiano solo le righe proprie), e l'eco
  all'IDLE si ignora.
- **Stato.** CONFERMATO, letto nel codice.
- **Quanto spesso, quanto è grave.** Basso. Fino alla rilettura.
- **Rimedio.** Dopo un invio rileggere Posta inviata se è aperta; passare alle finestre di una cartella anche le righe
  tolte da Missiva (come RemoveRuledRows).
- **Prova.** Controllo sui sorgenti, come quello di C-60 nel gruppo comportamento.

### E-14 Per quattro secondi dopo ogni scrittura di Missiva gli avvisi veri del server si buttano

- **Che cosa vede l'autore.** Raramente, una mail eliminata dal telefono che resta mentre si stava leggendo in Missiva.
- **Quando succede.** `OwnEcho` vale per tutto l'account (`Core/Idle.cs:126`, `:162-165`; `UI/App.cs:968-974`): durante
  e fino a 4 s dopo qualunque scrittura, anche il letto automatico del riquadro, ogni EXPUNGE e ogni FETCH dei segni si
  ignora, anche se viene dal telefono. Leggendo un messaggio dopo l'altro la finestra resta aperta di continuo. (Gmail
  manda l'eco vero 20-30 s dopo, ESITO.md: quello invece passa e fa rileggere, senza danno.)
- **Stato.** CONFERMATO, letto nel codice.
- **Quanto spesso, quanto è grave.** Basso. Fino alla rilettura.
- **Rimedio.** Riconoscere l'eco dagli UID scritti invece che dal tempo, o, se un avviso è stato ignorato, una rilettura
  alla fine della finestra.
- **Prova.** Funzione pura sull'eco: UID scritti e UID dell'avviso.

### E-15 Minori

- ShowFirstPage con gli stessi messaggi della copia aggiorna solo letto e contrassegno (`UI/MainWindow.xaml.cs:2455-2471`):
  categorie e scadenze dei contrassegni cambiate altrove restano vecchie finché la prima pagina non cambia.
- Svuota con la cartella aperta e una ricerca partita intanto: `ReloadAsync` (`:1907`) alza la versione con `searching`
  vero e non carica nulla (`:2395`); la ricerca in corso si scarta e `loading` resta vero (`:5037`): l'elenco resta vuoto
  su "Ricerca in corso…" finché non si apre un'altra cartella. Molto raro.

## Guardato e trovato a posto

- La versione dell'elenco scarta bene i risultati di un elenco superato: prima pagina e pagine dopo
  (`UI/MainWindow.xaml.cs:2417`, `:2441`), anteprime (`:2534`, `:2552-2557`), Tutti gli account (`UI/MainWindow.Unified.cs:209`),
  righe rifiutate rimesse (`:3554`), Annulla (`:3839`). Non ho trovato casi in cui scarti il risultato giusto senza che un
  caricamento nuovo lo sostituisca, tranne quello di E-15.
- `ImapProvider.OpenFreshAsync` (NOOP prima della prima pagina riletta, `Core/ImapProvider.cs:324-331`): il gruppo
  `arrivi` è verde; la prima pagina riletta ha l'ultimo arrivato.
- `KeepInSavedPage` e `MailCache.Prepend` (`UI/MainWindow.xaml.cs:2792-2804`, `Core/MailCache.cs:74-81`): i nuovi entrano
  nella copia della Posta in arrivo non aperta, senza doppioni (gruppo `arrivi`). `MailCache.ForgetFolder` toglie la copia
  di una cartella eliminata.
- `PrependRows` (`UI/MainWindow.xaml.cs:2821-2827`) e `MessageRows.Prepend` (`UI/ViewModels.cs:1220-1240`): una risposta
  nuova in una conversazione già nell'elenco ne diventa la testa e sale in cima (Gmail e Graph), con la conversazione chiusa.
- Cambio di giorno (`CheckDay`, `MessageRows.NewDay`, `UI/MainWindow.xaml.cs:3137-3163`, `UI/ViewModels.cs:1170-1190`): ogni
  minuto e alla riattivazione.
- `AppendKeepingPosition`, `MessageRows.Restore` e `RestoreFailed` (Annulla e rifiuti del server): righe al loro posto,
  solo se l'elenco è ancora quello.
- `ApplyIdleFlags` con l'UID (Gmail, Dovecot) cambia la riga della Posta in arrivo anche nella vista di tutti gli account.
- `Cutoff` e `Release` (`UI/MainWindow.Unified.cs:238-248`) tengono l'ordine fra gli account: un messaggio più vecchio non
  passa davanti a uno che deve ancora arrivare; un account in errore esce dal limite e l'elenco lo dice (`ShowMergedStatus`).
  Il problema di E-05 è l'attesa, non l'ordine.
- `ActionQueue.Apply` sulle pagine e sulle copie locali: le azioni in coda senza rete restano fatte nell'elenco.
- I non letti della Posta in arrivo (dal lato elenco): li riconta ogni controllo, ogni delta con cambiamenti e
  `RecountSoon` dopo ogni scrittura (`UnreadSync`, gruppo `arrivi` verde); il numero sull'icona e Tutti gli account li
  sommano. Il ritardo dei conteggi sta nelle altre cartelle (E-06).
- I risultati della ricerca non si aggiornano con gli arrivi e gli usciti (AddIncoming e RefreshInboxOf li saltano), tranne
  le righe tolte dalla delta di Graph: è come la ricerca del nuovo Outlook, una fotografia. Non lo conto come difetto.
- Sincronizza (F5, `RefreshAllAsync`, `UI/MainWindow.xaml.cs:5221-5230`) rilegge cartelle, conteggi ed elenco: è il
  rimedio a mano di quasi tutti i punti. Non è scritto in CLAUDE.md.

## Che cosa non ho potuto provare

- Nessuna prova a finestra (per regola le fa solo la parte "arrivo"): tutto quello che sta in `MainWindow` (E-01, E-05,
  E-08, E-10, E-12, E-13, E-14, E-15 e la parte di finestra di E-04 ed E-07) è letto nel codice o rifatto con le stesse
  chiamate a `MessageRows`, non visto. L'ordine dei gruppi di E-12 viene dal comportamento di WPF descritto nei commenti.
- Nessun server vero: i ritardi di Gmail (i venti secondi dell'IDLE di E-02 sono la misura del 27 settembre in CLAUDE.md)
  e la semantica della query delta con il filtro (E-11) non sono verificati qui.
- E-03 eseguito solo con IMAP; Gmail usa la stessa sessione IDLE ma la sonda non gira con lui.
- Le frequenze sono stime dal codice e dalle misure già scritte in CLAUDE.md ed ESITO.md: nessun registro della versione
  installata è stato letto (per regola). Un registro dell'autore direbbe se E-01 succede: righe "ERRORE (elenco
  messaggi)" seguite da nessuna rilettura di quella cartella.
- Non ho misurato quanto dura davvero una prima pagina dopo la sospensione sul PC dell'autore.

## Esito delle correzioni (26 ottobre 2026)

Ramo `correzioni-elenco`, da main `5d53818` (blocchi 1 e 2 già uniti, suite a 1393 controlli). Blocco 3 delle correzioni
dell'audit di affidabilità: elenco e conteggi. Quello che l'autore ha approvato: "L'elenco si rilegge dopo un errore o un
risveglio, e se resta vecchio te lo dice. I numeri delle altre cartelle si aggiornano con il controllo periodico. Più due
correzioni piccole su Outlook.com." I controlli nuovi sono nel gruppo `affidabilita-elenco`
(`Core/ProvaBozze.AffidabilitaElenco.cs`, nel giro degli audit; da solo con `Missiva.exe --prova-audit risultato.txt
affidabilita-elenco`, 30 controlli in circa 2,5 s). La finestra principale nella suite non si costruisce: le decisioni
stanno in funzioni senza finestra (`UI/ListRefresh.cs`, `UI/ListJournal.cs`, `MessageRows.RemoveMessage`), provate lì, con
il server IMAP finto e l'IDLE vero e il gestore finto di Graph; i posti di MainWindow, App e ComposeWindow si leggono nei
sorgenti senza commenti. Ogni controllo è stato visto cadere con il codice di prima: per i posti nei sorgenti rimettendo i
file del commit di prima e facendo girare il gruppo (NO); per i pezzi nuovi con la regola di prima rifatta nella prova,
dove serve; detto in ogni commit. Il codice di Missiva in italiano non cambia nei testi, tranne le tre righe nuove sopra
l'elenco (tradotte in inglese, gruppo `lingua` verde).

### Punto per punto

| Punto | Esito | Commit | Controlli (gruppo affidabilita-elenco) |
|---|---|---|---|
| E-01 prima pagina non arrivata con la copia a schermo | FATTO: riga "Copia sul PC del ...: non aggiornata." con Riprova; rilettura da sola al controllo riuscito, all'IDLE collegato, alla query delta, alla rete cambiata (al più ogni 30 s) e alla riapertura dall'area di notifica (vecchio o letto più di 2 minuti prima); la vista di tutti gli account si rilegge senza svuotarsi | `967d627` | le regole (ListRefresh, MailCache.SavedAt, il testo in italiano e in inglese); i posti |
| E-02 arrivi che il controllo non spiega | FATTO: un Arrived con meno messaggi trovati che annunciati rilegge la Posta in arrivo aperta, subito e dopo i tentativi; la query delta con messaggi che l'elenco non ha anche (RefreshIfMissing) | `d8f11c9` | IDLE vero su IMAP e Gmail finti (già letto, rimesso con la data di un mese fa, normale); la delta di Graph finta; i posti |
| E-03 IDLE ricollegato | FATTO: dopo il controllo dell'IDLE ricollegato la Posta in arrivo aperta si rilegge | `d8f11c9` | la sessione IDLE tagliata con un messaggio uscito e uno letto intanto; i posti |
| E-06 numeri delle altre cartelle, cartella aperta | FATTO: al controllo di riserva i numeri di tutte le cartelle ogni 5 minuti per account; la cartella aperta che nessuno guarda si rilegge se i suoi numeri cambiano, o ogni 5 minuti | `523733a` | le regole; i numeri su IMAP, Gmail e Graph finti con il costo; i posti |
| E-04 conversazioni uscite | FATTO: il messaggio esce dalla sua conversazione (MessageRows.RemoveMessage), in ApplyInboxChanges e RemoveNotifiedRow | `23f6380` | la sonda rovesciata sul metodo nuovo; i posti |
| E-09 Evidenziata e Altra | FATTO: inferenceClassification nei campi delle righe (non per le caselle condivise), MessageSummary.Focused; con Evidenziata aperta un nuovo di Altra non entra | `387acaf`, `0437ad7` | la sonda rovesciata sul gestore finto di Graph; i posti |
| E-08 rilettura scartata durante un caricamento | FATTO: resta da fare e parte alla fine del caricamento | `bec6da6` | i posti |
| E-07 pagina chiesta prima e arrivata dopo | FATTO: ListJournal, le righe tolte e arrivate dopo la richiesta si applicano alla pagina | `25b752b` | le due sonde rovesciate (IMAP e Gmail); le regole; i posti |
| E-05 Tutti gli account | FATTO: (a) dopo 3 s l'account lento arriva dopo, con la riga "... non ha ancora risposto", senza rompere l'ordine; (b) con E-07; (c) gli avvisi dell'IDLE rileggono anche le viste Posta in arrivo e Non letti; (d) Contrassegnati, Bozze e Posta inviata con il controllo di riserva (E-06), Posta inviata dopo un invio (E-13) | `e6afbf4` | i posti; la prova a finestra E05 |
| E-13 Posta inviata dopo un invio, due finestre | FATTO: Posta inviata aperta riletta 3 s dopo l'invio, in ogni finestra; spostati, eliminati e letti passano alle altre finestre (non Annulla) | `f5ea68f` | i posti |
| E-12 messaggio rimesso con una data vecchia | FATTO: la prima pagina riletta tiene i caricati per UID con Gmail e IMAP; una pagina dopo fuori ordine rifà l'elenco in ordine; una pagina senza seguito è tutta la cartella. NON FATTO: le eliminazioni lontane (sotto) | `e644cd0`, `e089cf7` | le regole; i gruppi con la vista raggruppata di WPF; i posti |
| E-10 filtri, ordine, "Vai a" | FATTO: ListRefresh.Enters in AddIncoming e nelle finestre di una cartella | `5d10b1a` | la regola; i posti |
| E-15 minori | FATTO tutti e due: categorie e scadenze dalla prima pagina uguale alla copia; Svuota con una ricerca aperta | `50e969a` | i posti |
| E-14 | già fatto dal blocco 2 con L-04 | | |
| E-11 query delta di Graph sui due giorni | LASCIATA com'è (sotto); coperta in parte da L-10 | `b363e65` | |
| L-10, la parte dell'elenco | FATTO: il numero del server più basso dei da leggere dell'elenco fa rileggere; le viste di tutti gli account con E-05; le altre cartelle con E-06 | `b363e65` | la regola; i posti |
| L'ora legale (nota del rapporto sull'arrivo) | VERO, e FATTO: le righe si ordinano per istante (MessageSummary.Instant), con l'ora locale | `b723a49` | due messaggi a dieci minuti dal cambio del fuso del PC sul server IMAP finto |

Gli aiuti di prova (comandi nuovi del server finto, `strumenti\arrivo\elenco.ps1`, `finestra.ps1 -Restart` e
`-ServerArgs`, "apri cartella" e "stato elenco" più ricco in MISSIVA_KEYS) sono in `f8d9dc7`, `113a4e5`, `4ede222`,
`de3f2de`.

### Le prove a finestra, prima e dopo

Debug fuori dallo schermo (`--foto`), due account IMAP verso due `--imap-finto` su 127.0.0.1, notifiche solo nel registro,
con `strumenti\arrivo\elenco.ps1 -Prova ...` (sopra `finestra.ps1`); lo stato dell'elenco chiesto ogni secondo ("stato
elenco"). "Prima" è il codice di `f8d9dc7` (main con i soli aiuti di prova) con la correzione degli aiuti di `113a4e5` e
`4ede222`, costruito in un albero a parte; "dopo" il codice finale.

| Prova | Che cosa | Prima | Dopo |
|---|---|---|---|
| ripresa | quattro arrivi con le connessioni morte, finestra nell'area di notifica, poi "ripresa": quando sono nell'elenco | 1,3 s (dal blocco 1) | 1,6 s |
| E01 | Missiva chiusa, quattro arrivi, riaperta con il server che non si raggiunge (copia a schermo), poi il server torna: quando sono nell'elenco | non in 150 s, riga di stato vuota (il numero diceva 5, l'elenco 1) | riga "Copia sul PC del 26/10/2026 12:24: non aggiornata. Riprova"; 4,5 s (riletto al controllo riuscito) |
| E01rete | lo stesso, con "rete tornata" al ritorno del server | 1,5 s (dal blocco 1: la rilettura del risveglio) | 1,7 s |
| E02 | un arrivo già letto altrove: quando è nell'elenco | non in 60 s | 1,4 s |
| E03 | IDLE morto, un messaggio tolto dal server intanto, l'IDLE si ricollega: quando la riga esce | non in 60 s | 1,3 s (con `e644cd0` non in 60 s: corretto in `e089cf7`) |
| E05 | Tutti gli account con il secondo account che non risponde: quando si vedono le pagine del server del primo | 120,6 s | 3,3 s e 3,6 s (due giri), con la riga "... non ha ancora risposto" |
| E06 | un arrivo in Clienti: quando il suo numero cambia | non in 400 s | 46,3 s (il primo controllo di riserva) |
| E06aperta | lo stesso con Clienti aperta: quando la sua riga c'è | non in 400 s | 43,8 s |
| E10 | con il filtro Contrassegnati un arrivo non contrassegnato | in cima | non entra |
| E12 | 120 messaggi, uno rimesso con la data di un mese fa, F5, la pagina dopo: i gruppi | Oggi, Ieri, Settimana scorsa, Mese scorso, Questo mese | Oggi, Ieri, Settimana scorsa, Questo mese, Mese scorso |
| costo | Clienti aperta per 6 minuti: comandi IMAP ai due server, processore della prova | 55 comandi (24 e 31), 4,90 ms/s | 72 comandi (33 e 39: 14 STATUS e 2 LIST dei numeri, un NOOP della rilettura), 5,12 ms/s |

### Misure

- **I numeri delle cartelle** (gruppo affidabilita-elenco, server finti nello stesso processo): un giro su IMAP sono 1
  LIST e 7 STATUS in 4 ms (il server finto non ha LIST-STATUS: MailKit chiede una STATUS per cartella; Gmail e i server che
  l'hanno fanno una richiesta sola), su Gmail 1 LIST e 6 STATUS in 2 ms; su Graph, dopo l'avvio, 3 richieste (mailFolders,
  le sottocartelle della cartella che ne ha, l'Archivio). Al più un giro ogni 5 minuti per account: 12 all'ora.
- **La cartella aperta che nessuno guarda**: al più una prima pagina ogni 5 minuti (per Gmail e IMAP un NOOP e una FETCH di
  50 righe, per Graph una richiesta), e subito quando i suoi numeri cambiano. A finestra (prova "costo": due account IMAP,
  la cartella Clienti aperta e ferma, 6 minuti dopo un minuto di assestamento): i comandi IMAP ai due server sono 72 invece
  di 55 (in più 14 STATUS e 2 LIST dei numeri, un NOOP della rilettura: la cartella era vuota), il processore della prova
  5,12 ms al secondo invece di 4,90, cioè nel rumore (il resto della finestra pesa di più).
- **Tutti gli account**: 3 s (SlowSource) di attesa al più per l'account lento alla prima pagina; prima fino al suo errore.

### Non fatti, e perché

- **E-11, far scorrere la query delta di Graph**: non serve, anzi segue meno messaggi. Il filtro della prima lettura
  (`receivedDateTime ge` l'avvio meno due giorni) tiene già tutti i messaggi arrivati da allora, per tutta la sessione; una
  query delta nuova "degli ultimi due giorni" ogni tanto ne seguirebbe di meno, e i messaggi arrivati più di due giorni
  prima dell'avvio non li segue né l'una né l'altra. Allargare il filtro (per esempio a due settimane) farebbe la prima
  lettura molto più grande e non è provabile senza un server vero (la semantica del filtro nella delta resta SOSPETTA).
  Coperto in parte da L-10: se un messaggio vecchio è letto altrove il numero del server scende sotto i da leggere
  dell'elenco, e l'elenco si rilegge.
- **E-12, le eliminazioni fatte altrove più in basso della prima pagina**: la rilettura (prima pagina) non le vede, e i
  caricati scorrendo restano finché non si cambia cartella. Servirebbe CONDSTORE/QRESYNC (Gmail le ha), un lavoro a parte.
- **E-13, Annulla**: rimette le righe solo nella finestra in cui si è fatto; le altre finestre le riprendono alla rilettura.
- **E-05**: un account che va in errore (non solo lento) resta fuori come prima, e le sue righe della copia locale escono.

### Scelte fatte qui, da rivedere

- I tempi: la riapertura dall'area di notifica rilegge un elenco letto più di 2 minuti prima (`ListRefresh.TrayAge`); i
  numeri delle cartelle ogni 5 minuti (`CountsEvery`); la cartella aperta che nessuno guarda ogni 5 minuti (`OtherEvery`);
  l'account lento di Tutti gli account dopo 3 s (`SlowSource`); la Posta inviata 3 s dopo l'invio (`SentSettle`); le
  riletture dell'elenco rimasto vecchio al più ogni 30 s (`StaleRetry`). Si cambiano in un punto solo.
- La riga "Copia sul PC del ...: non aggiornata." compare anche quando non riesce una rilettura dopo che l'elenco era stato
  letto (con l'ora di quella lettura), non solo con la copia locale.
- Con la scheda Altra aperta i messaggi nuovi non entrano in cima, come prima (ora si potrebbe, sapendo la scheda).
- Ogni rilettura scrive una riga nel registro ("elenco riletto (motivo): ..."), anche nella versione installata.

### Comportamenti che l'autore vedrà cambiati

- Se la prima pagina non arriva, sopra l'elenco "Copia sul PC del 26/10/2026 09:12: non aggiornata. Riprova", e l'elenco si
  aggiorna da solo appena l'account risponde; riaprendo dall'area di notifica dopo qualche minuto l'elenco si rilegge.
- Le mail già lette sul telefono, quelle rimesse nella Posta in arrivo e quelle eliminate altrove mentre l'IDLE era giù
  entrano o escono dall'elenco aperto da sole, in pochi secondi.
- I numeri delle cartelle e delle etichette di Gmail si aggiornano da soli (entro 5 minuti), e la cartella aperta mostra i
  messaggi nuovi.
- In Tutti gli account un account che non risponde non fa aspettare gli altri: "... non ha ancora risposto: i suoi messaggi
  arrivano appena risponde".
- Con Outlook.com e Evidenziata aperta le newsletter di Altra non compaiono più in cima per poi sparire; le conversazioni
  eliminate dal telefono escono.
- Nella notte del cambio dell'ora i messaggi dell'ora ripetuta sono in ordine di arrivo.

### Che cosa non ho potuto provare

- Server veri: Gmail, Outlook.com e la casella IMAP dell'autore non sono stati toccati. In particolare il $select con
  `inferenceClassification` nel controllo dei nuovi messaggi e negli elenchi di Graph (stesso campo già usato nei filtri
  dell'elenco), e quanto spesso Gmail rende visibile un messaggio dopo il suo avviso (per E-02 la rilettura dopo i
  tentativi).
- A finestra non ho provato E-07 (serve una pagina in viaggio nel momento giusto), E-08, E-13 (un invio vero dalla
  scrittura e due finestre sulla stessa cartella), E-04 ed E-09 (servono Gmail o Graph con le conversazioni e le schede, che
  il server finto di --imap-finto non ha), E-15: per loro le funzioni senza finestra e i sorgenti.
- Una sospensione vera del PC: la ripresa è "ripresa" in MISSIVA_KEYS con le connessioni mute del server finto.
- L'ora legale è provata con il fuso di Windows di questo PC (W. Europe Standard Time) e il cambio del 26 ottobre 2025.
