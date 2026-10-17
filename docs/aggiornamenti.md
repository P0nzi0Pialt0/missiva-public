# Aggiornamenti

Chi installa Missiva da un rilascio pubblicato su GitHub (la pagina Releases del repository) la tiene aggiornata da
Missiva stessa: quando esce una versione nuova lo dice, e si aggiorna solo quando glielo chiedi.

## Come funziona

- **Solo i rilasci.** Lo script con cui l'autore pubblica un rilascio scrive nell'eseguibile il repository da cui
  arrivano gli aggiornamenti. Una Missiva compilata dai sorgenti (`installa.ps1` dalla cartella dei sorgenti) non ce
  l'ha: non chiede nulla a nessuno, e si aggiorna rilanciando `installa.ps1`. Così chi compila da sé non riceve gli
  eseguibili di un altro.
- **Il controllo.** Due minuti dopo l'avvio, e poi una volta al giorno finché resta aperta, Missiva chiede a GitHub
  qual è l'ultimo rilascio. Se è più nuovo scarica anche il manifesto (`aggiornamento.json`) e la sua firma, due file
  piccoli. In Impostazioni, Generali, Informazioni c'è la casella **Controlla da solo gli aggiornamenti** (accesa di
  base: spenta, Missiva non chiede più nulla) e il pulsante **Controlla ora**.
- **Il pulsante.** Se c'è una versione nuova, nella barra del titolo, a destra prima dei pulsanti della finestra,
  compare il pulsante **Aggiornamento**, nel colore d'accento. Un clic apre un pannellino con "Missiva 0.3.N del
  gg/mm/aaaa", le novità, **Aggiorna** e **Più tardi**: con Più tardi, per quella versione il pulsante torna il giorno
  dopo. Nessuna finestra si apre da sola, e niente si scarica da solo.
- **Aggiorna.** Missiva scarica lo zip del rilascio nella sua cartella dei dati (l'avanzamento è nel pulsante, con una
  barra sottile e la percentuale, e nel pannellino, con Annulla), lo controlla, prova il nuovo eseguibile e chiede
  conferma. Poi si chiude come con Esci (i messaggi che stai scrivendo chiedono se salvare la bozza; se annulli, Missiva
  resta com'è), e il nuovo eseguibile prende il posto di quello che girava e riapre Missiva. Se qualcosa va storto
  prima della sostituzione resta la versione di prima, e il pulsante dice "Aggiornamento non riuscito" (anche
  all'apertura successiva, se l'intoppo è stato lì): il pannellino ne dice il motivo.

## Che cosa controlla Missiva prima di accettare un aggiornamento

- La firma del manifesto, fatta con la chiave di rilascio dell'autore (ECDSA P-256 su SHA-256): Missiva ha nel codice
  la metà pubblica della chiave (`Core/UpdateKey.cs`) e accetta solo un manifesto firmato con la metà privata.
- Che il manifesto sia di Missiva e dica una versione più alta di quella installata: mai una uguale o più vecchia.
- Che lo zip scaricato abbia la dimensione e l'impronta SHA-256 scritte nel manifesto.
- Che nello zip ci siano solo `Missiva.exe`, `installa.ps1`, `disinstalla.ps1`, `LICENSE` e `TERZE-PARTI.md`, senza
  cartelle e senza nient'altro.
- Che l'eseguibile estratto dica, nella sua versione di file, la versione del manifesto (letta dal file, senza
  avviarlo). Poi Missiva lo avvia una volta in prova, senza finestre, come fa `installa.ps1`: se non parte, non si
  chiude.
- La rete: solo https, e solo verso GitHub (`api.github.com`, `github.com` e gli host dove GitHub tiene i file dei
  rilasci, `objects.githubusercontent.com` e `release-assets.githubusercontent.com`), anche quando GitHub rimanda a un
  altro indirizzo; ogni risposta ha un tetto alla dimensione.

Chi entrasse nell'account GitHub dell'autore, senza la chiave di rilascio, potrebbe pubblicare un rilascio ma non farlo
accettare a nessuna Missiva.

## Che cosa vede GitHub

Una richiesta due minuti dopo l'avvio e poi una al giorno: l'indirizzo IP, l'ora e il nome del programma ("Missiva").
Nessun account, nessun dato della posta, nessun identificativo del PC. Con Aggiorna, anche lo scaricamento dello zip.
Con la casella spenta, nessuna richiesta finché non premi Controlla ora.

## I limiti

- Chi ha la chiave di rilascio e la sua password può pubblicare un aggiornamento che Missiva accetta. La chiave sta sul
  PC dell'autore, cifrata con una password; se si perdesse, le Missiva installate non accetterebbero più aggiornamenti
  e andrebbero reinstallate a mano da un rilascio nuovo.
- Missiva non ha un certificato di firma del codice di Windows. Alla prima installazione da un file scaricato Windows
  avvisa ("Windows ha protetto il PC": Ulteriori informazioni, Esegui comunque). Gli aggiornamenti li scarica Missiva,
  e Windows non li segna come scaricati da Internet.
- Con Smart App Control acceso, Windows può bloccare un eseguibile senza firma. Missiva prova il nuovo eseguibile
  prima di chiudersi: se Windows non lo lascia partire, resta la versione di prima e la barra lo dice.
- Il formato delle risposte di GitHub e gli host dei file sono quelli della sua documentazione: le prove automatiche
  usano un GitHub finto. Se GitHub cambiasse host, il controllo si fermerebbe con "indirizzo non ammesso" nel registro
  (`missiva.log`), senza installare nulla.

## Per chi pubblica rilasci da un fork

Serve una chiave propria: la metà pubblica va in `Core/UpdateKey.cs`, e l'eseguibile va compilato con
`-p:MissivaAggiornamenti=proprietario/repository` (il repository dei rilasci). Senza quella proprietà, o con la chiave
vuota, gli aggiornamenti restano spenti.
