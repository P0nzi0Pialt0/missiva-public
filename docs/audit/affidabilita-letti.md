# Audit di affidabilità: letti e non letti che si perdono

**Versione guardata: 0.3.628, main `96a4bbe`** (rilascio del 22 ottobre 2026, quella installata dall'autore). Audit in locale
del 25 ottobre 2026, ramo `affidabilita-letti`. Il codice di Missiva non è stato cambiato: c'è solo questo rapporto e, in
un commit a parte, le sonde che riproducono i punti (`Core/ProvaBozze.SondeLetti.cs`, gruppo `sonde-letti`).

La richiesta dell'autore: "i messaggi letti o non letti ogni tanto si perdano per strada". Qui: ogni strada per cui un
messaggio letto in Missiva risulta di nuovo da leggere (in Missiva o sul server), uno segnato da leggere torna letto, uno
letto sul telefono resta da leggere in Missiva (o il contrario), il numero dei non letti non corrisponde al vero.

## Come leggere il rapporto

- **CONFERMATO**: riprodotto con una sonda (si dice quale e con che esito) oppure letto nel codice senza dubbi, con file e
  riga (righe del commit `96a4bbe`).
- **SOSPETTO**: dipende da qualcosa che qui non si è potuto provare (un server vero, la finestra di Missiva, i tempi della
  rete); si dice che cosa manca.
- Le sonde girano senza finestra, come la suite: server IMAP finto (`FakeImap`), gestore finto di Graph, e il riquadro di
  lettura (`ReadingView`) costruito senza mostrarlo, con i suoi metodi privati chiamati per riflessione come li chiama
  `ShowAsync`. Ogni controllo delle sonde passa quando il difetto **si vede** ("riprodotto: ..."): dopo una correzione va
  rovesciato.
- Non si ripropongono come nuovi i punti già scritti in CLAUDE.md o in `docs/audit/ESITO.md` come scelta o limite noto
  (la conversazione con due messaggi da leggere, Evidenziata e Altra nel numero di Outlook, il timeout di MailKit lasciato
  a 60 e 120 s, "dopo N secondi" che si ferma chiudendo la finestra): quando oggi pesano di più, lo si dice.

## Riassunto

In ordine di quanto spiegano quello che vede l'autore.

| Sigla | Che cosa vede l'autore | Stato | Quanto dura |
|---|---|---|---|
| L-01 | Un messaggio letto nel riquadro torna da leggere; il numero risale a 1 con tutte le righe lette | CONFERMATO (sonda) | per sempre, finché non lo si rilegge |
| L-02 | Un messaggio segnato da leggere a mano torna letto riaprendo Missiva dall'area di notifica | CONFERMATO (sonda e codice) | per sempre |
| L-03 | Dopo un periodo senza rete un letto o un da leggere fatto dopo (in Missiva o sul telefono) torna com'era prima | CONFERMATO (sonda) | per sempre |
| L-04 | Un messaggio letto sul telefono resta in grassetto in Missiva | CONFERMATO (sonda e codice) | fino alla rilettura dell'elenco |
| L-05 | Appena aperto, il messaggio resta (o torna) in grassetto nell'elenco, con il numero giusto | CONFERMATO (codice) | secondi con Gmail, fino alla rilettura con IMAP |
| L-06 | Con Outlook.com un "da leggere" subito dopo l'apertura non tiene: sul server resta letto | CONFERMATO (sonda) | per sempre |
| L-07 | Una riga cambia stato senza essere stata toccata, o quella giusta non cambia | CONFERMATO (codice) | fino alla rilettura |
| L-08 | Un letto fatto un attimo prima di uscire (o di un aggiornamento) non arriva al server | CONFERMATO (codice), frequenza SOSPETTA | per sempre |
| L-09 | "Contrassegna tutti come letti" senza rete o con un errore: le righe dicono letti, il server no | CONFERMATO (codice) | fino alla rilettura |
| L-10 | In "Tutti gli account", o per i messaggi più vecchi, un letto sul telefono non si vede nell'elenco | CONFERMATO (codice) | fino alla rilettura |

Le tre cose da correggere per prime:

1. **L-01: una scrittura del letto che non riesce non deve sparire.** Oggi il letto del riquadro va in coda solo con gli
   errori "non in linea" di Windows (nove codici di socket); un tempo scaduto (la connessione morta dopo la sospensione del
   PC: 120 s per Gmail, 60 per IMAP), una connessione caduta due volte, un NO, un 503 o tre 429 di Graph lasciano il
   messaggio letto in Missiva e da leggere sul server, senza riprovare. È una spiegazione diretta, e compatibile con il
   registro, del "numero fermo a 1 con tutto letto" del 5-6 ottobre, con il PC sospeso in mezzo, che ESITO.md lasciava
   senza causa ("una lettura non arrivata al server" era una delle tre ipotesi).
2. **L-02: la riapertura dall'area di notifica non deve segnare letto.** `ShowFromTray` rimette nel riquadro la riga scelta
   con un'apertura nuova, e il "da leggere" a mano, che stava sull'apertura di prima, si perde: con "Subito" il messaggio
   torna letto ogni volta che si riapre la finestra.
3. **L-03: un segno in coda non deve vincere su un segno più nuovo.** La coda delle azioni senza rete ripete i suoi segni
   anche se intanto lo stesso messaggio è stato segnato in linea (in Missiva, dopo il ritorno della rete o dopo un riavvio)
   o sul telefono.

## I punti

### L-01. Il letto del riquadro si perde con ogni errore che non è "non in linea"

**Che cosa vede l'autore.** "Ho letto il messaggio e me lo ritrovo da leggere"; "il numero dei non letti resta a 1 anche se
sono tutte lette" (segnalazione del 7 ottobre, ESITO.md, "Dopo l'installazione"). Sul telefono il messaggio è ancora da
leggere.

**Quando succede.**
- `ReadingView.SetRead` (`UI/ReadingView.xaml.cs` 839-865) segna subito il messaggio letto in Missiva
  (`entry.Summary.IsRead = true`, riga 850, poi `MessageRead`, che toglie il grassetto alla riga e abbassa il numero:
  `MainWindow.OnReaderRead`, `UI/MainWindow.xaml.cs` 3392-3398) e poi scrive sul server (riga 858). Se la scrittura fallisce
  va in coda solo con `Network.IsOffline` (riga 859), cioè solo con un `SocketException` fra 10013, 10050, 10051, 10060,
  10064, 10065, 11001, 11002, 11004 (`Core/Offline.cs` 17 e 32). Con ogni altro errore (riga 864) scrive "stato di
  lettura" nel registro e basta: nessuna ripresa, niente coda, il segno locale resta.
- Gli errori che arrivano lì davvero, per Gmail e IMAP: `RunAsync` riprova una volta solo `IOException`,
  `ServiceNotConnectedException` e `ImapProtocolException` (`Core/ImapProvider.cs` 287, `Core/GmailProvider.cs` 224). Un
  tempo scaduto di MailKit è un `System.TimeoutException` (visto dalla sonda: "Operation timed out after 1500
  milliseconds"), che non si riprova e non è "non in linea". È proprio la riga del registro del 6 ottobre dopo la ripresa
  dalla sospensione ("Operation timed out after 120000 milliseconds" sulla connessione principale di Gmail). Lo stesso per
  una connessione che cade anche alla ripresa, per un NO del server (`ImapCommandException`).
- Per Graph: `SendAsync` ripete i 429 due volte e i 503 solo per le letture (`Core/GraphProvider.cs` 305-306): il PATCH del
  letto con 503, 500 o tre 429 di fila esce con errore; anche un accesso da rifare con la finestra nascosta
  (`MicrosoftAuth.Quiet`) o un `HttpRequestException` con 10054 (connessione chiusa dall'altra parte) non sono "non in linea".
- Finita la scrittura (riuscita o no) `UnreadSync.Written` fa ricontare (`UI/App.cs` 258-260, `RecountSoon` 1255-1274): il
  server conta ancora quel messaggio, e il numero risale con le righe tutte lette. Alla prossima rilettura dell'elenco
  (cartella riaperta, riavvio, avviso dell'IDLE) la riga torna in grassetto.
- Dopo la sospensione la connessione principale resta ferma finché l'utente non fa qualcosa (il controllo di ogni due minuti
  va su quella in sottofondo, l'IDLE ha la sua). Il primo comando su quella connessione morta è spesso proprio il segno di
  lettura: il corpo del messaggio arriva dalla copia sul PC (`BodyCache`, primi dieci e ultimi 30 giorni) senza passare dal
  server, e il riquadro segna letto subito. Quel comando aspetta 120 s (Gmail) o 60 (IMAP) e si perde; quelli dopo, su una
  connessione nuova, riescono.
- Lo stesso vale per i segni a mano (Ctrl+Q, Ctrl+U, busta, menu, finestra del messaggio): `RunOrQueueAsync`
  (`UI/MainWindow.Offline.cs` 27-56) mette in coda solo con `Network.IsOffline`; con gli altri errori mostra "Operazione non
  riuscita" (dopo fino a due minuti) ma la riga resta come l'utente l'ha messa (`MainWindow.SetReadAsync`, 3966-3970), senza
  tornare indietro. Almeno lì l'avviso c'è.
- La coda stessa sa già quali errori "passano" (`ActionQueue.Passing`, `Core/ActionQueue.cs` 220-223: accesso da rifare,
  429, 5xx, tempo scaduto, connessione caduta), ma la usa solo quando la ripete, non quando l'azione nasce.

**CONFERMATO**, sonda `sonde-letti` (IMAP, server finto; il codice di Gmail è lo stesso):
- NO al segno: "in Missiva letto True, sul server \Seen False, comandi STORE 1, azioni in coda 0";
- connessione caduta due volte: "letto True, \Seen False, comandi STORE 2, azioni in coda 0";
- connessione muta fino al tempo scaduto (accorciato a 1,5 s sul client della connessione principale): "letto True, \Seen
  False, comandi STORE 1"; l'errore è `System.TimeoutException`, `Network.IsOffline` falso; la richiesta dopo, sulla
  connessione rifatta, riesce: il segno si sarebbe potuto riprendere;
- dopo le tre scritture fallite `UnreadSync.Written` è partito e il server conta 4 da leggere (i tre aperti più uno della
  sonda): è il numero che `RecountSoon` scriverebbe;
- Microsoft: "PATCH arrivati 503:1 429:3 500:1; sul server tutti da leggere; azioni in coda 0", con i tre messaggi letti in
  Missiva.

**Quanto spesso, quanto è grave.** A ogni ripresa dalla sospensione in cui la prima cosa fatta su un account è aprire un
messaggio, quando la connessione è morta senza che Windows lo sappia (il caso del 6 ottobre); con Outlook.com a ogni 503 o
raffica di 429 (rari, ma Outlook li manda). Lo stato si perde per sempre: il messaggio resta da leggere sul server e sul
telefono, e in Missiva torna in grassetto alla prima rilettura. Il numero sbagliato (1 con tutto letto) dura fino a quella
rilettura.

**Rimedio.** In `SetRead` e in `RunOrQueueAsync` gli errori che passano (quelli di `ActionQueue.Passing`, reso interno)
vanno in coda come la rete che manca, e la coda riparte al controllo dopo; con un rifiuto vero (NO, 400, 403) il segno
locale torna indietro (riga e numero) e si dice. In `RunAsync` anche `TimeoutException` fra gli errori ripresi una volta (il
STORE si può ripetere). **Prova**: le sonde L-01 rovesciate (dopo il NO e dopo il tempo scaduto l'azione è in coda e la
ripetizione mette `\Seen`; dopo il 503 lo stesso con Graph).

### L-02. Riaperta dall'area di notifica, il messaggio segnato da leggere torna letto

**Che cosa vede l'autore.** "L'avevo segnato da leggere per ricordarmelo, e me lo ritrovo letto." Anche sul telefono.

**Quando succede.**
- Ctrl+U (o la busta, o il menu) sul messaggio aperto: `MainWindow.SetReadAsync` chiama `ReadingView.ForgetPendingRead`
  (`UI/ReadingView.xaml.cs` 146-154), che mette `ReadByHand` sull'**Entry** del riquadro (riga 152), cioè su quella
  apertura, e il messaggio diventa da leggere sul server.
- Chiudendo la finestra (X, con "Mantieni nell'area di notifica", di base) `HideToTray` chiama `Reader.ReleaseWeb`
  (`UI/MainWindow.xaml.cs` 523), che svuota il riquadro (`Clear`, `UI/ReadingView.xaml.cs` 520-538: le Entry vanno via).
  La riga resta scelta nell'elenco.
- Riaprendo (icona, "Apri" dal menu dell'icona, un link mailto:, "apri" da una seconda copia) `ShowFromTray` rimette la
  riga scelta nel riquadro (`UI/MainWindow.xaml.cs` 597-603: `ShowInReaderAsync(item)`), cioè `ShowAsync` con una Entry
  nuova (`UI/ReadingView.xaml.cs` 324-325), senza `ReadByHand`. Arrivato il corpo, `MarkRead` (360, 804-820) trova il
  messaggio da leggere e, con "Subito", lo segna letto; con "dopo N secondi" dopo N secondi; con "quando la selezione cambia"
  al primo cambio.
- In Outlook un messaggio segnato da leggere e lasciato nel riquadro resta da leggere finché non si sceglie un'altra cosa e
  si torna lì.

**CONFERMATO.** Sonda `sonde-letti`, "L-02 riquadro": "letto al clic True; da leggere a mano True; riaperto il riquadro con
la stessa riga: in Missiva letto True, sul server \Seen True". La sonda fa le due aperture come `ShowAsync` (Entry nuova sullo
stesso `MessageSummary` della riga) e in mezzo il segno come `MainWindow.SetReadAsync`; il passaggio da `ShowFromTray` a
`ShowInReaderAsync` è letto nel codice (righe 597-603), perché le prove a finestra non erano di questa parte.

**Quanto spesso, quanto è grave.** Ogni volta che si chiude la finestra lasciando scelto un messaggio segnato da leggere, e la
si riapre: per chi tiene Missiva nell'area di notifica (l'autore) è un gesto di tutti i giorni. Lo stato si perde per
sempre, sul server.

**Rimedio.** La riapertura del riquadro che rimette il messaggio di prima (`ShowFromTray`, e ogni ridisegno che non è una
scelta dell'utente) passa a `ShowAsync` un `restore: true` che salta `MarkRead`; oppure il "da leggere a mano" si tiene per
messaggio (account, cartella, id) fino al prossimo cambio di selezione, non sulla Entry. **Prova**: la sonda L-02 rovesciata
(dopo la riapertura il server non ha `\Seen`), più un controllo sul sorgente che `ShowFromTray` non chieda il letto.

### L-03. Un segno in coda vince su quello fatto dopo

**Che cosa vede l'autore.** "Senza rete (in treno) l'avevo segnato da leggere, poi a casa l'ho letto: è tornato da leggere."
Oppure: "l'avevo rimesso da leggere sul telefono, Missiva l'ha rifatto letto."

**Quando succede.**
- `ActionQueue.Add` (`Core/ActionQueue.cs` 113-129) toglie un segno contrario solo quando ne entra un altro **in coda**. Un
  segno fatto in linea (`ReadingView.SetRead` 858, `RunOrQueueAsync` 39) va dritto al server e non guarda la coda: il segno
  vecchio dello stesso messaggio resta lì.
- `ReplayAsync` (238-381) lo ripete quando torna la rete: `StillThereAsync` controlla solo che il messaggio ci sia ancora
  (IMAP, `Core/ImapProvider.cs` 1445-1459: UID, Message-Id, niente `\Deleted`), non il suo stato, e il segno parte comunque
  (337-340).
- Le finestre in cui succede: dopo un riavvio con `coda.dat` non vuota (`Network.KnownOffline` è in memoria e all'avvio è
  vuoto, `Core/Offline.cs` 43 e 60-66: tutto va dritto al server finché la coda non riparte, al primo controllo riuscito);
  dopo il ritorno della rete, fra la prima richiesta riuscita (che toglie "non in linea") e la ripetizione della coda;
  sempre, per i cambi fatti sul telefono mentre Missiva era senza rete.
- `ActionQueue.Apply` (184-201) intanto mostra nell'elenco lo stato della coda, cioè quello vecchio: anche l'elenco dice il
  falso finché la coda non è ripartita.

**CONFERMATO**, sonda `sonde-letti`:
- "letto in linea dopo il 'da leggere' in coda: sul server \Seen True, in coda ancora 1; ripetuta la coda (1 fatte), sul
  server \Seen False";
- "letto in coda, poi da leggere sul telefono: ripetuta la coda (1 fatte), sul server \Seen True".

**Quanto spesso, quanto è grave.** Solo dopo un periodo senza rete (o con un "non in linea" dato da un errore di rete
breve: basta un 11001 di un DNS lento al risveglio) e con lo stesso messaggio toccato due volte. Raro, ma lo stato si perde
per sempre e nessun avviso lo dice.

**Rimedio.** Un segno di lettura fatto in linea toglie dalla coda i segni di lettura dello stesso messaggio (lo stesso
`RemoveAll` di `Add`, in una `ActionQueue.Supersede` chiamata da `SetRead` e da `MainWindow.SetReadAsync` quando la
scrittura riesce). Per il telefono è una scelta: l'Outlook classico fa lo stesso (vince il client); si può dire nella frase
"Azioni..." o far scadere i segni di lettura in coda da più di qualche ora. **Prova**: la prima sonda L-03 rovesciata (dopo la
ripetizione il server ha `\Seen`, e la coda è vuota prima di ripartire).

### L-04. L'eco delle scritture di Missiva copre per 4 secondi tutto l'account

**Che cosa vede l'autore.** "Ho letto un messaggio sul telefono e in Missiva è ancora in grassetto" (mentre il numero, dopo
un po', è giusto).

**Quando succede.**
- Ogni scrittura di Gmail e IMAP passa da `InboxIdle.OwnAsync` (`Core/Idle.cs` 141-157); per 4 s dopo la fine
  (`OwnWindow`, 126) `OwnEcho` è vero per l'**account** (162-165), qualunque messaggio dica l'avviso.
- `App.OnInboxEvent` (`UI/App.cs` 968-974) con `OwnEcho` scarta l'avviso e esce: niente `ApplyIdleFlags`, niente rilettura,
  niente controllo. Un letto fatto sul telefono su un **altro** messaggio, in quei secondi, si perde per l'elenco: la riga resta
  com'era fino alla prossima rilettura (riaprire la cartella). Il numero lo rimette il riconteggio dopo la scrittura (se il
  telefono è arrivato prima) o il controllo di ogni due minuti.
- Le scritture di Missiva sono anche quelle che l'utente non vede: il letto automatico del riquadro, le regole, i ritorni da
  Posponi. Con Gmail, che manda gli avvisi dell'IDLE 20-30 s dopo (ESITO.md, 7 ottobre), l'avviso del
  telefono arriva quando vuole lui, e basta che cada nei 4 s dopo una qualunque scrittura; e l'eco vero di Missiva arriva quasi
  sempre fuori dalla finestra, cioè la finestra con Gmail protegge poco quello per cui c'è.

**CONFERMATO**, sonda `sonde-letti` (IMAP, server finto che manda a chi è in IDLE i segni cambiati): Missiva segna letta
l'offerta, un secondo dopo "il telefono" legge la richiesta del cliente; arrivano all'IDLE "UID 1 letto True, eco True; UID 2
letto True, eco True": l'avviso del telefono arriva con `OwnEcho` vero, e `OnInboxEvent` (letto nel codice) lo scarta.

**Quanto spesso, quanto è grave.** Serve che Missiva scriva qualcosa e il telefono cambi un segno nella stessa manciata di
secondi: raro, ma con Missiva aperta mentre si legge anche sul telefono succede. Dura fino alla rilettura dell'elenco.

**Rimedio.** L'eco per messaggio: `OwnAsync` annota gli UID scritti (account e cartella), e `OnInboxEvent` scarta solo le
FETCH di quegli UID; le altre si applicano. Per gli EXPUNGE, che non hanno l'UID, resta la regola di oggi. **Prova**: la
sonda L-04 rovesciata (l'avviso dell'UID 2 arriva con eco falso), più un controllo che una FETCH dell'UID scritto resti eco.

### L-05. La pagina riletta, o la query delta, rimette sulla riga lo stato di prima della scrittura

**Che cosa vede l'autore.** "Ho aperto il messaggio, è nel riquadro, ma nell'elenco è ancora in grassetto" (il numero è
giusto). Oppure, dopo Ctrl+U, la riga torna non in grassetto.

**Quando succede.**
- `UnreadSync` protegge solo i **numeri** (biglietti, `Core/Idle.cs` 87-104). Le **righe** prendono lo stato del server
  senza nessun controllo: `ShowFirstPage` con la stessa pagina della copia locale (`UI/MainWindow.xaml.cs` 2455-2470:
  `old.IsRead = m.IsRead; row.IsRead = m.IsRead`), `MergeFirstPage` (2507-2510), e per Outlook `ApplyInboxChanges`
  (`UI/MainWindow.Notifications.cs` 276).
- Il caso più comune: si apre la Posta in arrivo (la copia locale si vede subito, 2404-2408) e si clicca il primo messaggio da
  leggere prima che arrivi la pagina del server. Il corpo arriva dalla copia sul PC, il riquadro segna letto subito, e il
  STORE aspetta sulla connessione principale dietro la FETCH della pagina: la pagina torna con lo stato di prima e la riga
  ritorna da leggere. Lo stesso con una rilettura dopo un avviso (`RefreshListAsync`) o dopo la coda.
- Con Outlook: una query delta partita un attimo prima del PATCH che contiene quel messaggio (per esempio appena arrivato)
  lo riporta da leggere sulla riga.
- Chi rimette a posto: con Gmail l'avviso dell'IDLE con l'UID, 20-30 s dopo (fuori dai 4 s dell'eco), via `ApplyIdleFlags`;
  con Outlook la query delta dopo (30 s); con IMAP l'eco arriva subito ed è scartato (`OwnEcho`), quindi la riga resta sbagliata
  fino alla prossima rilettura.

**CONFERMATO** nel codice (le righe sopra). Non riprodotto: serve la finestra principale (`ShowFirstPage` e `MergeFirstPage`
sono di `MainWindow`), e le prove a finestra non erano di questa parte. Che la FETCH partita prima del STORE sulla stessa
connessione torni con lo stato di prima è come funziona IMAP (una connessione, un comando alla volta: `Connection.Gate`).

**Quanto spesso, quanto è grave.** Ogni volta che si clicca un messaggio mentre la prima pagina è in viaggio: all'avvio,
cambiando account o cartella, dopo un avviso che fa rileggere (la finestra è il tempo della richiesta della pagina: di solito
decimi di secondo, secondi quando la connessione va rifatta, come all'avvio o dopo una sospensione). Lo stato sul server è
giusto; la riga sbaglia per secondi (Gmail, Outlook) o fino alla rilettura (IMAP).

**Rimedio.** Un biglietto per messaggio, come per i numeri: ogni segno locale annota l'istante (account, cartella, id), e
chi applica lo stato del server (pagina, unione, delta) lo salta per i messaggi segnati dopo che la richiesta è partita.
**Prova**: la regola in una funzione statica (stato locale, stato del server, istante della richiesta) con un controllo per
pagina, unione e delta.

### L-06. Con Microsoft due scritture sullo stesso messaggio non hanno un ordine

**Che cosa vede l'autore.** "Ho aperto il messaggio e ho fatto subito Ctrl+U: in Missiva è da leggere, sul telefono è letto."

**Quando succede.**
- Gmail e IMAP scrivono su una connessione, un comando alla volta, nell'ordine in cui arrivano. Graph no: ogni
  `SetReadAsync` è un PATCH a sé (`Core/GraphProvider.cs` 1028-1029, `EachAsync` 1012-1021), fino a quattro insieme
  (`MailboxGate`), e un 429 si ripete dopo l'attesa che Outlook chiede (305-310, fino a 5 s). Il letto del riquadro che riceve
  un 429 parte di nuovo dopo il "da leggere" di Ctrl+U e vince.

**CONFERMATO**, sonda `sonde-letti`: "ordine sul server: letto: 429, da leggere: applicato, letto: applicato; stato finale
letto True (l'ultima richiesta dell'utente era 'da leggere')".

**Quanto spesso, quanto è grave.** Serve un 429 (o una risposta lenta) proprio sul letto del riquadro e un segno contrario
entro pochi secondi: raro. Lo stato si perde per sempre.

**Rimedio.** Le scritture di lettura di Graph in fila per messaggio (un `SemaphoreSlim` per id, o una scrittura che aspetta
quella prima dello stesso messaggio); in alternativa il segno nuovo annulla la ripetizione di quello vecchio ancora in
attesa. **Prova**: la sonda L-06 rovesciata (stato finale da leggere).

### L-07. Le righe si cercano solo per identificativo, senza la cartella

**Che cosa vede l'autore.** "Ho letto un messaggio della conversazione e nell'elenco è diventato non in grassetto un altro
messaggio"; "nei risultati della ricerca ne ho letto uno e se ne sono schiariti due".

**Quando succede.**
- `OnReaderRead` (`UI/MainWindow.xaml.cs` 3394) cambia le righe con `r.Summary.Id == summary.Id`, senza la cartella, e il
  numero della cartella `summary.FolderId` (3395). Con Gmail i messaggi della conversazione aperti nel riquadro vengono da
  "Tutti i messaggi" (`GetConversationAsync`, `Core/GmailProvider.cs` 741: `Summary(i, all)`): l'UID è di quella cartella.
  Aprendo nel riquadro un messaggio più vecchio della conversazione la riga della Posta in arrivo con quel numero di UID
  (un altro messaggio, se c'è) diventa letta solo in Missiva, la riga vera del messaggio no, e scende il numero di "Tutti i
  messaggi" invece di quello della Posta in arrivo (il riconteggio lo rimette).
- `MainWindow.SetReadAsync` (3967-3968) fa lo stesso con `ids.Contains(r.Summary.Id)`: nella ricerca in tutte le cartelle di
  un account IMAP due risultati di cartelle diverse con lo stesso UID cambiano insieme (solo in Missiva).
- Una finestra di una sola cartella ("Apri in un'altra finestra") ha il suo riquadro: il letto fatto lì cambia le sue righe,
  non quelle della finestra principale (il numero lo rimette il riconteggio).

**CONFERMATO** nel codice. Non riprodotto (serve la finestra principale).

**Quanto spesso, quanto è grave.** Gli UID di "Tutti i messaggi" e della Posta in arrivo di Gmail sono due numerazioni
diverse: che coincidano su una riga visibile dipende dalla casella. Il caso della riga vera che non cambia succede ogni volta
che si apre nel riquadro un messaggio più vecchio di una conversazione con Gmail; lo rimette l'avviso dell'IDLE dopo 20-30 s.
Solo in Missiva, fino alla rilettura.

**Rimedio.** Le righe per cartella e identificativo, e per i messaggi di "Tutti i messaggi" per identificativo stabile
(`StableId`, X-GM-MSGID); il numero sulla cartella della riga trovata. **Prova**: la ricerca delle righe in una funzione
statica, con una conversazione di Gmail e due risultati IMAP con lo stesso UID.

### L-08. Le scritture in viaggio quando Missiva esce

**Che cosa vede l'autore.** "L'ultimo messaggio che ho letto prima di chiudere (o prima dell'aggiornamento) è di nuovo da
leggere."

**Quando succede.**
- Esci, "esci" sul canale (l'aggiornamento con `installa.ps1`), l'aggiornamento da GitHub e la fine della sessione di
  Windows chiudono la finestra (`App.ExitAll`, `UI/App.cs` 474-481; `SessionEnding` 133-139) e `OnExit` (346-356) non aspetta
  le scritture in corso: i segni che aspettano sulla connessione (anche dietro un comando fermo, L-01) si perdono, e non sono
  in coda.
- Con "quando la selezione cambia" il messaggio aperto diventa letto alla chiusura della finestra (`Leave`,
  `UI/ReadingView.xaml.cs` 134-139): la scrittura parte mentre Missiva sta uscendo.

**CONFERMATO** nel codice (nessuna attesa delle scritture e nessuna coda all'uscita); **SOSPETTO** quanto spesso: dipende
da quanto dura la scrittura rispetto all'uscita (millisecondi di solito, fino a due minuti con una connessione ferma).

**Rimedio.** All'uscita aspettare al più qualche secondo le scritture in corso (`UnreadSync` sa quante sono) e mettere in coda
quelle non finite (la coda è su disco e riparte al riavvio). **Prova**: un controllo con una scrittura trattenuta dal server
finto e l'uscita simulata: dopo, l'azione è in `coda.dat`.

### L-09. "Contrassegna tutti come letti" segna le righe anche quando non è riuscito

**Che cosa vede l'autore.** Senza rete, o con un errore: "le ho segnate tutte lette, poi sono tornate da leggere."

**Quando succede.** `MainWindow.MarkAllReadAsync` (`UI/MainWindow.xaml.cs` 1910-1914): `FolderActionAsync` (1917-1930)
mostra l'errore e lo inghiotte, e la riga dopo segna lette tutte le righe comunque. Non passa dalla coda: senza rete non si
ripete. Le cartelle rilette rimettono il numero vero, la cartella riaperta le righe vere.

**CONFERMATO** nel codice. Lo si vede (c'è l'avviso "Operazione non riuscita"), e dura fino alla rilettura.

**Rimedio.** Le righe solo se l'azione è riuscita; senza rete dirlo, o metterla in coda come segno per ogni messaggio da
leggere della cartella. **Prova**: un controllo sul sorgente o con la funzione estratta.

### L-10. Che cosa Missiva non segue dopo il primo elenco

**Che cosa vede l'autore.** "In 'Tutti gli account' un messaggio letto sul telefono resta in grassetto"; "un messaggio di
qualche giorno fa letto sul telefono in Missiva è ancora da leggere"; il numero invece è giusto.

**Quando succede.**
- Con un avviso dell'IDLE senza UID (server che nelle FETCH non spontanee non mettono l'UID) o con un EXPUNGE,
  `RefreshInboxOf` rilegge solo se la cartella aperta è quella Posta in arrivo (`UI/MainWindow.Notifications.cs` 292): con
  "Tutti gli account" aperta (`folder` nullo) le righe restano com'erano.
- Outlook: la query delta parte con `receivedDateTime ge` due giorni prima della prima lettura della sessione
  (`Core/GraphProvider.cs` 652-653): i messaggi più vecchi letti sul telefono non arrivano mai come cambiati.
- L'IDLE guarda solo la Posta in arrivo: le altre cartelle si aggiornano solo riaprendole.
- I numeri (Posta in arrivo, intestazione dell'account chiuso, "Tutti gli account", icona) si ricontano a ogni controllo, quindi
  sono giusti: è l'elenco aperto che non lo è.

**CONFERMATO** nel codice. Il limite dei due giorni è scritto in CLAUDE.md come scelta (per la query delta leggera); oggi,
con l'autore che legge anche dal telefono, pesa di più: il numero e il grassetto dicono due cose diverse.

**Rimedio.** Con "Tutti gli account" aperta, un avviso senza UID o un EXPUNGE rilegge la vista (`RefreshListAsync` lo sa già
fare per la vista unita); per Outlook, quando il numero del server scende senza che la delta l'abbia detto, rileggere la prima
pagina. **Prova**: un controllo su `RefreshInboxOf` con la vista unita.

## Da sapere (non sono difetti)

- **Il numero di Gmail non è quello del telefono.** Missiva conta i non letti di tutta la Posta in arrivo di IMAP
  (`GetInboxUnreadAsync`, `Core/GmailProvider.cs` 642-648: SEARCH UNSEEN), cioè anche Promozioni, Social e Aggiornamenti;
  l'app e il sito di Gmail, con le schede attive, contano a parte quelli di Principale. Se l'autore confronta i due numeri, non tornano mai: non è una
  perdita, ma si può scambiare per una. SOSPETTO che sia parte di quello che l'autore vede (non so come ha l'app impostata).
- **Outlook.com con Evidenziata e Altra**: il numero conta le due schede (già in ESITO.md, "Non verificato").
- **"Dopo N secondi"** si ferma chiudendo la finestra, nascondendola o aprendo altro: è come in Outlook (CLAUDE.md).
- **Conversazione con due da leggere**: aprendola diventa letto solo il più recente (ESITO.md, scelta).

## Guardato e trovato a posto

- **Le letture non segnano letto.** Tutte le FETCH di Missiva passano dalle API di MailKit (`FetchAsync` con
  `MessageSummaryItems` e `HeaderFields`, `GetMessageAsync`, `GetBodyPartAsync`, `GetStreamAsync` in
  `Core/GmailProvider.cs` 917-923, 980, 1279-1280, 1331, `Core/ImapProvider.cs` 1198-1204, 1258, 1302-1303, 1522,
  `Core/Invitation.cs` 521), che chiedono il corpo con `BODY.PEEK`; nessun comando scritto a mano (`BODY[` compare solo in
  un commento, `Core/Transfer.cs` 19). Il controllo della scelta 33
  (C-65, gruppo `scelte3`), rifatto qui su `96a4bbe`: tutti i 75 metodi di `IMailProvider` su un account IMAP (86 comandi) e
  su uno Gmail (71) senza nessun `BODY[]` senza PEEK, segni e messaggi come prima. Il controllo cerca `BODY[` ma non `RFC822`
  né `BINARY[` (che pure segnerebbero letto): MailKit non li usa e il server finto non offre BINARY, ma il controllo
  potrebbe cercarli. Corpi in anticipo, copia dei 30 giorni, anteprime e IDLE sono su EXAMINE (sola lettura). Graph legge con
  GET, che non cambia `isRead`.
- **Posponi**: il ritorno toglie `\Seen` (IMAP `Core/ImapProvider.cs` 1476, Gmail `Core/GmailProvider.cs` 1255) o manda
  `isRead: false` (Graph `Core/GraphProvider.cs` 1105) prima di spostare; un errore lascia il posposto da riprovare.
- **Archivia, Sposta, Annulla**: lo stato segue il messaggio (MOVE e COPY di IMAP tengono i segni, Gmail lavora sulle
  etichette, Graph `/move`).
- **Elimina con "quando la selezione cambia"**: il letto del messaggio che si lascia parte prima dello spostamento sulla
  stessa connessione (la catena da `RemoveRows` a `SetRead` prende il posto sulla connessione senza aspettare), quindi il
  messaggio arriva letto nel Cestino o nell'Archivio, con Gmail e IMAP.
- **Regole che segnano letto**: un gruppo che non riesce si riprova al controllo dopo (`Rules.RunAsync`, `Core/Rules.cs`
  629-654), le righe già in elenco cambiano (`ApplyIdleFlags` da `CheckAccountAsync`, `UI/App.cs` 1165-1166).
- **I numeri**: `UnreadSync` (biglietti, scritture in viaggio, riconteggio dopo ogni raffica) fa quello che dice; il gruppo
  `arrivi` lo prova. Intestazione dell'account chiuso, "Tutti gli account" e icona vengono tutti da `FolderItem.Unread` delle
  Posta in arrivo (`AccountItem.InboxUnread`, `UI/ViewModels.cs` 66): non hanno una deriva loro. Il numero "nuovi da quando si
  è guardato" non scende leggendo altrove per scelta (CLAUDE.md).
- **La coda** tiene le azioni su disco finché non sono finite, toglie i doppioni e i contrari in coda, e applica i suoi segni
  agli elenchi riletti (`Core/ActionQueue.cs` 113-129, 184-201, 238-257).
- **Il letto dalla notifica** (clic sulla notifica, finestra del messaggio) passa dallo stesso `OnReaderRead` della finestra
  principale (`UI/MainWindow.xaml.cs` 3226).

## Che cosa non ho potuto provare

- Niente server veri: Gmail, Outlook.com e la casella IMAP dell'autore non sono stati toccati. Il ritardo di 20-30 s
  degli avvisi dell'IDLE di Gmail è quello di ESITO.md; se Gmail, con due cambi ravvicinati, mandi due FETCH o una sola non
  lo so (cambia quanto dura la riga sbagliata di L-04 e L-05).
- Quanto spesso, dopo la sospensione, la connessione muore "in silenzio" (tempo scaduto, L-01) invece di dare subito un
  errore (che `RunAsync` riprende): dipende dal router e dalla rete. Il registro della versione installata lo direbbe con le
  righe "stato di lettura"; non l'ho letto (regola dell'audit).
- Le parti della finestra principale (`ShowFromTray` di L-02, `ShowFirstPage` e `MergeFirstPage` di L-05, `OnReaderRead` di
  L-07, l'uscita di L-08, `MarkAllReadAsync` di L-09, `RefreshInboxOf` di L-10): lette nel codice, perché le prove a
  finestra erano dell'altra parte. Di L-02 la sonda riproduce il riquadro (due aperture e il segno a mano in mezzo).
- Che cosa fa `HttpClient` di .NET con una connessione morta dopo la sospensione per un PATCH (lo ripete da sé o no): con
  Outlook.com L-01 dipende anche da questo.
- Il tempo scaduto di MailKit è stato accorciato nella sonda (1,5 s sul client della connessione principale, per
  riflessione) invece di aspettare 60 s.

## Come si rifanno le prove

```
dotnet build
set MISSIVA_DATA=<una cartella nuova>
bin\Debug\net10.0-windows10.0.19041.0\win-x64\Missiva.exe --prova-audit risultato.txt sonde-letti
```

Dieci controlli, circa 14 secondi; ognuno passa quando il difetto si vede. La suite intera (`--prova-bozze`) con il gruppo
delle sonde: 1345 controlli in 218 s, tutti passati (i 1335 di sempre più i 10 delle sonde): le sonde non toccano i controlli
che c'erano. Il gruppo è in `Core/ProvaBozze.SondeLetti.cs`
e nella tabella di `Core/ProvaBozze.Gruppi.cs`, nel commit delle sonde (da non unire così com'è). Il controllo sulle letture
con PEEK: `--prova-audit risultato.txt scelte3` (39 controlli, tutti passati su `96a4bbe`).

## Esito delle correzioni (25 ottobre 2026)

Ramo `correzioni-letti`, da main `fd971be`. Le correzioni approvate dall'autore: "Un segno non riuscito resta in attesa e si
ripete invece di perdersi. La riapertura dall'area di notifica non segna più letto da sola. Un segno nuovo vince su uno
vecchio in attesa. I cambi fatti sul telefono non vengono più ignorati." Un commit per punto; in ognuno il controllo nuovo
cadeva con il codice di prima. I controlli sono nel gruppo `affidabilita-letti` (`Core/ProvaBozze.AffidabilitaLetti.cs`,
23 controlli, 15 s; da solo con `--prova-audit risultato.txt affidabilita-letti`), senza finestre: il riquadro di lettura è
quello vero (`ReadingView.ShowAsync`) sul thread dell'interfaccia con il suo Dispatcher, senza WebView2
(`ReadingView.TestWithoutWeb`, gancio di prova nuovo); server IMAP finto e gestore finto di Graph; le parti della finestra
principale, che nessuna prova costruisce, lette nel sorgente. La suite intera (`--prova-bozze`): 1358 controlli, tutto
bene; i controlli di prima passano tutti, nessuna attesa cambiata. Il gruppo `lingua` passa (nessun testo nuovo a schermo).

| Punto | Esito | Commit | Controllo |
|---|---|---|---|
| L-01 | fatto | `78257b7` | cinque controlli: IMAP con NO, connessione caduta due volte e muta fino al tempo scaduto; Microsoft con 503, tre 429, 500 e 400; il sorgente dei segni a mano |
| L-02 | fatto | `0caee5d` | il riquadro vero: clic, Ctrl+U, riapertura nei tre modi di Gestione messaggi; il sorgente di ShowFromTray |
| L-03 | fatto, tranne il telefono (scelta) | `415e6b2` | il "da leggere" in coda e il letto in linea, lo stesso dopo un riavvio, la coda che riparte intanto; la guardia al contrario |
| L-04, E-14 | fatto | `bc97d62`, `8404b1f` | l'IDLE vero sul server finto: il telefono su un altro messaggio, un cambio diverso dall'atteso, l'eco dopo 4,5 s, il contrassegno nello stesso avviso, gli EXPUNGE, la FETCH senza UID |
| L-05 | fatto | `decb455` | le letture del server chieste prima, durante e dopo la scrittura; il sorgente dei cinque posti |
| L-06 | fatto | `53cf703` | il letto con un 429 e il "da leggere" subito dopo, su Graph finto |
| L-07 | fatto | `e219eb9` | la regola su una conversazione di Gmail, due risultati IMAP e Microsoft; il sorgente del riquadro e dei segni a mano |
| L-08 | fatto | `ceb1c4c` | il letto trattenuto all'uscita (in coda nel file, poi arriva ed esce) e il processo che finisce prima (la coda al riavvio); il sorgente di OnExit e SessionEnding |
| L-09 | fatto | `ff1942e` | il sorgente di MarkAllReadAsync e TryFolderActionAsync |
| L-10 | lasciato al blocco 3 | | |
| Numero di Outlook.com in ritardo dopo un letto | fatto | `868966f` | 2 s dopo un letto di Graph il conteggio non vale ancora, per IMAP sì |
| Arrivo contato due volte | fatto | `217b97b` | cartelle rilette con gli arrivati di notte, uno arrivato adesso, la copia locale |
| Gmail fermo a 1 per 14 ore | non riprodotto | | |

**L-01.** Il letto del riquadro e i segni a mano passano da `ReadMarks.WriteAsync` (`Core/ReadMarks.cs`, nuovo): con la
rete che manca o con un errore che passa (`ActionQueue.Passing`, ora interno) il segno va in coda, e la coda si ripete 5 s
dopo (`ReadMarks.QueuedAfterError`, `MainWindow.ReplayLaterAsync`), non solo al controllo di ogni due minuti, perché
intanto il riconteggio del server fa risalire il numero. Con un rifiuto del server il segno torna com'era: dal riquadro il
messaggio torna da leggere (`ReadingView.ReadUndone`), a mano righe e numero tornano indietro dopo l'avviso di sempre. Il
secondo tentativo di `RunAsync` dopo un `TimeoutException` è del blocco 1: il controllo del tempo scaduto passa in tutti e
due i casi (il segno in coda, o ripreso subito).

**L-02.** `ShowFromTray` rimette nel riquadro la riga scelta con `restore` (`ShowInReaderAsync`, `ReadingView.ShowAsync`):
il messaggio si mostra com'è, in ogni modo di Gestione messaggi.

**L-03.** Un segno riuscito in linea toglie dalla coda e da coda.dat i segni di prima degli stessi messaggi
(`ActionQueue.Supersede`); una ripetizione già partita salta quelli tolti intanto. Vale il segno più nuovo: ogni segno ha
il suo numero (`ReadMarks`), e un segno superato da uno fatto dopo non va in coda, non toglie dalla coda quello dopo e non
si rimette indietro. **Scelta per l'autore**: un cambio fatto sul telefono mentre Missiva era senza rete resta sotto il
segno in coda (vince il client, come nell'Outlook classico). Saperlo vorrebbe lo stato del server al momento del segno, che
IMAP senza CONDSTORE non dice; le alternative sono far scadere i segni di lettura in coda da più di qualche ora, o dirlo
nella frase "Azioni...".

**L-04 ed E-14.** L'eco è per messaggio e per segno: dentro la scrittura il provider annota che cosa tornerà all'IDLE
della Posta in arrivo (`InboxIdle.Expect`: il segno scritto con il suo valore, un segno qualsiasi per categorie, etichette
e \Deleted, un EXPUNGE per ogni messaggio che esce, anche le etichette \Inbox tolte da "Tutti i messaggi" di Gmail), e
`InboxIdle.OwnEcho` con l'avviso vale una volta per eco atteso, per `OwnWindow`, ora 60 s invece di 4 (nel registro
dell'autore 33 avvisi su 39 dell'eco di Gmail arrivano 1-34 s dopo, 3 dopo 43-85 s). Un cambio fatto altrove su un altro
messaggio, o diverso da quello atteso, passa sempre; l'altro segno dello stesso avviso (il contrassegno messo dal telefono
nell'eco di un letto) arriva alla riga. Restano fuori, e passano come avvisi veri con un controllo in più, gli echi che
Missiva non sa prevedere: con Gmail un segno scritto su un messaggio di "Tutti i messaggi" (conversazioni, ricerca) torna
all'IDLE con l'UID della Posta in arrivo, che Missiva non conosce; dice lo stato giusto.

**L-05.** Chi applica lo stato del server alle righe prende il biglietto prima di chiedere (`ReadMarks.Asked`) e passa da
`ReadMarks.Apply` o `Holding`: la prima pagina e la sua rilettura, le pagine della vista di tutti gli account, la query
delta di Graph (`InboxChanges.Asked`, e lì anche la coda) e gli avvisi dell'IDLE. Per i messaggi segnati dopo, o con la
scrittura in viaggio o finita da meno di `ReadMarks.Settle` (1 s), vale il segno di qui; una lettura chiesta dopo vince.

**L-06.** `GraphProvider.SetReadAsync`: per messaggio la scrittura chiesta dopo parte quando quella prima è finita
(`InReadOrder`); messaggi diversi non si aspettano.

**L-07.** `ReadMarks.Same` e `Matches`: Microsoft per id, Gmail e IMAP per cartella e UID, Gmail anche per X-GM-MSGID. Le
usano il letto del riquadro e il suo rifiuto (`MainWindow.ReadReaderRows`) e i segni a mano; il numero cambia nella
cartella di ogni riga che cambia. Una finestra di una sola cartella resta com'era (il numero lo rimette il riconteggio).

**L-08.** I segni ancora in viaggio all'uscita vanno nella coda su disco (`ReadMarks.QueueUnfinished`, da `App.OnExit` e
`SessionEnding`); se la scrittura arriva lo stesso escono dalla coda (Supersede). Niente attesa all'uscita: `OnExit` non può
aspettare lavori che riprendono sul thread dell'interfaccia, e la coda basta.

**L-09.** Le righe si segnano lette solo se il server l'ha fatto (`TryFolderActionAsync` dice se l'azione è riuscita;
`FolderActionAsync` resta per le altre azioni sulle cartelle). Non in coda: "tutti" sono anche i messaggi che l'elenco
non ha caricato.

**L-10.** Lasciato al blocco 3 (elenco e conteggi), come chiesto: ogni sua parte è una rilettura dell'elenco (la vista di
tutti gli account dopo un avviso senza UID o un EXPUNGE, la query delta di Graph limitata agli ultimi due giorni, le
cartelle che l'IDLE non guarda). La parte che sta nei letti funziona già: un letto fatto sul telefono detto con l'UID, o
dalla query delta, cambia anche le righe di "Tutti gli account" (`ApplyIdleFlags` e `ApplyInboxChanges` cercano le righe
per account, non per cartella aperta; letto nel codice).

**Il numero di Outlook.com in ritardo dopo un letto** (registro, 22 e 24 ottobre). Exchange aggiorna `unreadItemCount`
qualche secondo dopo la risposta al PATCH, e il riconteggio aspettava `UnreadSync.Settle`, un secondo: dopo un letto di
Graph il conteggio del server non vale per `GraphProvider.ReadCountSettle`, 5 s (`UnreadSync.WriteAsync` con un'attesa in
più). Le altre scritture di Graph (sposta, elimina, tutti letti) come prima: il registro non ne mostra.

**L'arrivo contato due volte** (registro, quattro volte). Le cartelle rilette, o il riconteggio, avevano già contato gli
arrivati (tre volte su quattro durante la sospensione), e il numero stimato con la notifica li sommava di nuovo. Ora la
stima non somma i messaggi arrivati più di 30 s prima del numero del server che la Posta in arrivo ha adesso
(`FolderItem.CountedAt`, `UnreadSync.AskedAt`, `MainWindow.ArrivedUnread`). Il caso del 15 ottobre alle 12:51 (l'arrivo
nello stesso secondo del riconteggio) resta: un secondo, e il mezzo minuto serve perché Gmail mostra un arrivo alle altre
connessioni anche qualche secondo dopo.

**Gmail fermo a 1 per 14 ore e mezza** (17-18 ottobre). Non riprodotto, e il registro esclude la spiegazione che sembrava
la prima: prima della lettura delle 20:22:10 il numero di qui era 1, non 2 ("1 -> 0 letto nel riquadro", poi "0 -> 1
ricontati dal server"). Una conversazione con due messaggi da leggere (aprendola diventa letto solo il più recente, scelta
scritta in ESITO.md) avrebbe dato "2 -> 1" e nessun riconteggio in salita. Vuol dire che il messaggio segnato letto nel
riquadro non era quello che il server contava: era già letto sul server, oppure la sua scrittura non è arrivata senza
lasciare un errore. Nessun eco dell'IDLE segue la lettura (il server non ha cambiato nulla), e il numero resta 1 a ogni
controllo fino alle 10:56 del giorno dopo. Lo stesso segno, "1 -> 0" e subito "0 -> 1", c'è alle 20:08:37, due secondi
dopo una riapertura dall'area di notifica: è L-02 (la riapertura rimetteva nel riquadro la riga scelta e la segnava letta),
su una riga che in Missiva era da leggere e sul server no, come la lasciavano L-04 (un avviso vero scartato, o un eco
vecchio applicato) e L-05 (una pagina chiesta prima della scrittura); alle 20:08:44 la lettura del messaggio giusto lo
rimette a 0. Quale messaggio teneva il numero a 1 il registro non lo dice (le letture non hanno l'identificativo): il più
probabile è quello arrivato alle 20:11:18 a finestra chiusa. Le strade che danno questo segno (L-02, L-04, L-05, e L-01 per
la scrittura persa) sono corrette; il caso preciso no, perché non riprodotto.

**Scelte lasciate all'autore, e cose da sapere.**
- Il telefono con la coda (L-03, sopra).
- Il contrassegno non passa dalla strada nuova: con un errore che passa resta l'avviso "Operazione non riuscita", come
  prima, e va in coda solo senza rete. La stessa strada (`ReadMarks.WriteAsync`) si potrebbe usare anche per lui.
- Un NO di Gmail vale come rifiuto, e il segno torna indietro: anche quello del 25 ottobre alle 14:01:21 ("System Error
  (Failure)", due minuti dopo una ripresa), che con ogni probabilità era passeggero. Si possono trattare come errori che
  passano i NO con [UNAVAILABLE] o [SERVERBUG] (RFC 5530) e quel testo di Gmail.
- Un segno in coda per un errore che passa, se dopo cinque secondi aspetta ancora, fa dire alla ripetizione della coda
  "Un'azione fatta senza rete aspetta ancora...", una volta per motivo: il testo dice "senza rete" anche quando il motivo è
  un altro (lo dice subito dopo).

**Che cosa non ho potuto provare.** Niente server veri: i tempi dell'eco di Gmail e del conteggio di Exchange sono quelli
del registro; che Gmail mandi una sola FETCH per un segno, e l'EXPUNGE per ogni messaggio che esce dalla Posta in arrivo
anche da "Tutti i messaggi", non l'ho visto dal vero. Le parti della finestra principale (`ShowFromTray`, `SetReadAsync`,
`ReadReaderRows`, `ApplyInboxChanges`, `MarkAllReadAsync`) sono provate attraverso le funzioni che chiamano e lette nel
sorgente, senza la finestra (le prove a finestra erano del blocco 1); l'uscita vera di Missiva non è stata provata, solo
`ReadMarks.QueueUnfinished` e i due posti che la chiamano.
