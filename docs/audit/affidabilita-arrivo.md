# Audit di affidabilità: l'arrivo dei messaggi nuovi

25 ottobre 2026. Richiesta dell'autore: "ogni tanto sembra che le mail arrivano tardi". Questa è la parte "arrivo": perché
una mail può comparire (elenco, notifica, numero sull'icona) secondi o minuti dopo che il server l'ha ricevuta, per Gmail,
IMAP e Microsoft (Graph). Versione guardata: 0.3.628 (main `96a4bbe`), quella installata dall'autore. Ramo
`affidabilita-arrivo`; le sonde stanno nei commit `92f5200` e `f1eb1a5` ("sonde dell'audit", da non unire così com'è). Il codice di
Missiva non è stato toccato.

Tre fonti:

- **Sonde della suite** (`Missiva.exe --prova-audit risultato.txt arrivo-sonde`, 12 sonde in circa 85 s, e `arrivo-done`,
  circa 5 minuti, con MISSIVA_DATA verso una cartella di prova): MailKit e i provider veri contro il server IMAP finto, che
  ora sa rendere "mute" delle connessioni (leggono e non rispondono, senza chiudersi: una connessione morta senza che
  nessuno se ne accorga). Dove serviva, il tempo scaduto di MailKit e il rinnovo dell'IDLE sono accorciati nella sonda
  (1,5-4 s e 6 s invece di 60-120 s e 9 minuti); i conti con i valori veri sono scritti accanto.
- **Prove a finestra**: la versione di prova (Debug di questo ramo) fuori dallo schermo (`--foto`), con due account IMAP
  verso due `--imap-finto` su 127.0.0.1 e le notifiche solo nel registro (`MISSIVA_NOTIFICHE_SOLO_REGISTRO=1`):
  `strumenti/sonde-arrivo/finestra.ps1` e `ripresa.ps1`. I tempi sono dall'istante in cui lo script scrive il comando al
  server finto (che lo legge entro 200 ms) alla riga "notifica mostrata" del registro della prova. Sono le prove F1..F8 qui
  sotto.
- **Il registro della Missiva installata** che l'autore ha copiato (dal 15 al 25 ottobre alle 14:02; la 0.3.628 dal 22
  ottobre alle 10:44), letto e basta. Qui solo orari, conteggi e tipi di riga. Gli account si chiamano come nel registro
  ("Gmail 1", "Gmail 2", "Outlook 3", "Gmail 4").

## Riassunto

Nel registro dell'autore, dalla 0.3.628 in poi, gli arrivi sugli account con l'IDLE (i tre Gmail) sono 22: **11 li ha
annunciati l'IDLE, 11 il controllo di ogni due minuti**. Cioè metà della posta è arrivata con fino a due minuti di ritardo
(e dopo la ripresa del PC anche di più). Dei 11 arrivati dal controllo: 7 subito dopo una ripresa del PC o un cambio di rete,
2 erano il secondo messaggio di una coppia arrivata insieme, 2 dopo un silenzio del registro di 24 e 41 minuti senza nessun
avviso dell'IDLE. I punti, in ordine di quanto spiegano quello che l'autore vede:

| Sigla | In breve | Stato | Ritardo |
|---|---|---|---|
| A-01 | L'IDLE muore senza che nessuno se ne accorga: niente keepalive, niente tempo scaduto durante l'IDLE, rinnovo ogni 9 minuti, e al rinnovo il DONE senza risposta lo ferma per sempre | CONFERMATO | fino a 2 minuti per ogni arrivo, anche per tutta la sessione |
| A-02 | Dopo la ripresa del PC anche il controllo di ogni due minuti trova le connessioni morte: aspetta 60 s (IMAP) o 120 s (Gmail), non riprova, e gli account si controllano in fila | CONFERMATO | 2-4 minuti dopo la ripresa |
| A-03 | Quando l'IDLE si ricollega, i messaggi arrivati mentre era giù non li annuncia nessuno | CONFERMATO | fino a 2 minuti |
| A-04 | Nessun gestore della sospensione e della ripresa; i timer di .NET non contano il tempo sospeso | CONFERMATO | la posta arrivata col PC sospeso compare circa 2 minuti dopo la ripresa |
| A-05 | Due messaggi vicini (o uno che Gmail rende visibile tardi): i tentativi a 3 e 10 s valgono per "un messaggio qualsiasi", non per quello annunciato | CONFERMATO (meccanismo); SOSPETTO quanto a Gmail | da 10 s a 2 minuti |
| A-06 | I nuovi si scelgono per data di arrivo e orologio del PC, non per UID: uno con la data di arrivo più vecchia non si annuncia e non entra nell'elenco | CONFERMATO | perso fino a una rilettura dell'elenco |
| A-07 | Con le regole di Missiva che guardano il testo, la notifica aspetta la connessione principale | CONFERMATO | secondi (uno scaricamento), fino a 120 s dopo la ripresa |
| A-08 | Microsoft: fino a 30 s per costruzione; un deltaLink da rifare perde un giro; token con un semaforo unico e senza tempo limite | CONFERMATO in parte, SOSPETTO per il token | 30 s, fino a 2 minuti |
| A-09 | La connessione in sottofondo è condivisa con anteprime, copia dei 30 giorni e rubrica | CONFERMATO dal codice | secondi |
| A-10 | Il registro della versione installata non dice quando l'IDLE si ricollega né quando è fermo | CONFERMATO | (nessuno: rende i ritardi invisibili) |

**Le tre cose da correggere per prime**

1. **A-01 e A-04 insieme: un cane da guardia per l'IDLE e un gestore della ripresa.** Al rinnovo, se l'IDLE non finisce
   entro 20-30 s dal DONE, cancellare la sessione (la sonda mostra che la cancellazione la ferma in 8 ms); alla ripresa del
   PC (`SystemEvents.PowerModeChanged`, Resume) e a `NetworkAddressChanged` chiudere le sessioni IDLE in corso, non solo
   svegliare quelle in attesa. Con A-03 (un controllo dopo ogni ricollegamento) è un cambiamento solo, in `Core/Idle.cs` e
   `App.OnNetworkBack`.
2. **A-02: le connessioni morte dopo la ripresa.** Alla ripresa e al cambio di rete chiudere senza saluto le connessioni
   ferme (la proposta V-37 di `velocita-2.md`, mai fatta); un `TimeoutException` sulle letture si riprova una volta su una
   connessione nuova; gli account si controllano insieme, non in fila.
3. **A-06: i nuovi per UID** (come già fanno le regole con `ArrivedAsync` e il mark), per Gmail e IMAP; per Graph gli
   identificativi nuovi della query delta.

## Punti

### A-01 L'IDLE muore senza che nessuno se ne accorga, e al rinnovo può fermarsi per sempre

- **Che cosa vede l'autore**: "la mail arriva tardi": sul telefono c'è già, in Missiva compare fino a due minuti dopo, con
  il controllo di ogni due minuti. Può durare tutta la sessione, finché Missiva non si riavvia.
- **Quando succede.** La connessione dell'IDLE resta aperta fra un rinnovo e l'altro senza che passi nulla:
  - il socket di MailKit non ha il keepalive di TCP (sonda: `SO_KEEPALIVE` 0): se la connessione muore (PC sospeso, Wi-Fi
    cambiato, il NAT del router che dimentica una connessione ferma, il server che chiude mentre il PC dorme) il client non
    lo sa finché non scrive qualcosa;
  - durante l'IDLE il tempo scaduto di MailKit non vale (sonda: IDLE con `Timeout` di 1,5 s e un server muto, finisce solo
    al DONE, dopo 5,0 s, senza errori);
  - il client scrive solo al rinnovo, ogni 9 minuti (`InboxIdle.Renew`, `Core/Idle.cs:118`, `CancelAfter` a `:273`), e i 9
    minuti sono di PC sveglio (A-04);
  - al rinnovo MailKit manda DONE, e **se la risposta non arriva aspetta senza limite**: sonda `arrivo-done`, connessione muta,
    DONE a 3 s: dopo 150 s l'IDLE aspetta ancora, con `Timeout` di 1,5 s e di 4 s, `IsConnected` vero. Se invece TCP dà un
    errore (il server ha chiuso e risponde con un reset, oppure le ritrasmissioni falliscono), `RunAsync` lo riprende da sé
    (`Core/GmailProvider.cs:224-228`, `Core/ImapProvider.cs:287-291`);
  - nessuno interrompe una sessione ferma: `InboxIdle.RetryNow` sveglia solo chi aspetta di riprovare dopo un errore
    (`Core/Idle.cs:202-212`, `:231-232`); `App.OnNetworkBack` chiama solo quello (`UI/App.cs:324-339`); non c'è un gestore
    della ripresa (A-04); `InboxIdle.Watching` resta vero (`Core/Idle.cs:185`, `:241-244`), quindi per il controllo di ogni
    due minuti l'IDLE è vivo e lui fa da rete di sicurezza (`UI/App.cs:1069-1071`); il registro non dice nulla (A-10).
- **CONFERMATO.**
  - Sonde (gruppo `arrivo-sonde`): A-01 IDLE senza tempo scaduto; IDLE muto con DONE a 3 s ancora fermo a 20 s; keepalive
    spento; IDLE di un provider (IMAP e Gmail) con la connessione morta, rinnovo a 6 s e `Timeout` di 2 s: a 22 s ancora
    fermo sul DONE, `Watching` vero, `RetryNow` senza effetto.
  - Prova a finestra F4: connessione dell'IDLE muta, quella in sottofondo viva, un arrivo: notifica dopo 108 s, dal
    controllo di ogni due minuti. Al rinnovo (9 minuti dopo l'avvio della sessione) il server finto riceve il DONE e non
    risponde: nel registro della prova nessuna riga; "stato account" dice ancora "IDLE True" per tutti e due gli account.
    Prova F6: un arrivo dopo quel rinnovo arriva dopo 69 s, ancora dal controllo di ogni due minuti.
  - Registro dell'autore: 11 arrivi su 22 dal controllo di ogni due minuti (sopra). Due di loro (23 ottobre alle 11:07 e alle
    16:14, "Gmail 1") senza nessun avviso dell'IDLE prima e dopo un silenzio del registro di 41 e 24 minuti: compatibile con
    un IDLE morto in silenzio (o con una sospensione: il registro non lo distingue, A-10).
- **Quanto spesso, quanto è grave**: a ogni sospensione e ripresa (su questo PC 36 ore di sospensione negli ultimi due giorni,
  A-04) e a ogni cambio di rete l'IDLE resta giù fino al rinnovo; se al rinnovo TCP non dà errore (un programma in mezzo
  che tiene la connessione: antivirus che controlla la posta, VPN; o il server stesso) resta giù per sempre. Intanto ogni
  arrivo ha da 0 a 2 minuti di ritardo, in media uno.
- **Rimedio**: un cane da guardia sul DONE (se `IdleAsync` non torna entro 20-30 s dal rinnovo, cancellare il secondo token
  che `InboxIdle.SessionAsync` passa a `IdleAsync`: la sonda `arrivo-done` mostra che lo ferma in 8 ms, con `IsConnected`
  falso, e la sessione dopo si ricollega); alla ripresa e al cambio di rete cancellare le sessioni (A-04). In più, il
  keepalive di TCP (MailKit accetta un `Socket` già collegato) o un rinnovo più corto (2-4 minuti), che accorcia il tempo
  di una connessione morta prima che qualcuno la scopra.
- **Prova che lo tiene fermo**: la sonda A-01/A-03 del gruppo `arrivo-sonde` deve vedere l'IDLE di nuovo in sessione entro
  rinnovo più cane da guardia; con "rete tornata" la sessione ferma deve chiudersi subito.

### A-02 Dopo la ripresa, anche il controllo di ogni due minuti trova le connessioni morte

- **Che cosa vede l'autore**: riaperto il portatile, la posta arrivata intanto compare dopo due, tre o quattro minuti; e
  aprendo un messaggio di Gmail il riquadro resta vuoto fino a due minuti (questa seconda parte è V-37 di `velocita-2.md`).
- **Quando succede.**
  - Il controllo usa la connessione in sottofondo (`GetNewAsync`, `Core/ImapProvider.cs:899-908`,
    `Core/GmailProvider.cs:573-583`), che dopo la ripresa può essere morta. Il NOOP aspetta tutto il tempo scaduto di
    MailKit, 60 s per IMAP e 120 s per Gmail (`Core/MailLimits.cs:28`, `:38`), e l'errore è `TimeoutException`.
  - `RunAsync` riprova da capo solo `IOException`, `ServiceNotConnectedException` e `ImapProtocolException`
    (`Core/ImapProvider.cs:287`, `Core/GmailProvider.cs:224`): il `TimeoutException` esce, il controllo di quell'account è
    perso (`UI/App.cs:1221-1231`). Non è nemmeno "non in linea" (`Network.IsOffline` falso: `Core/Offline.cs:17`, `:32`),
    quindi nessuno stato sotto l'account e `OnNetworkBack` non lo considera.
  - Gli account si controllano uno dopo l'altro (`UI/App.cs:1090-1096`), e ognuno può tenere fermi gli altri fino a
    `StuckCheck`, 2 minuti, che per Gmail è proprio il suo tempo scaduto (C-39, lasciato); il timer salta il giro se il
    controllo di prima è ancora in corso (`UI/App.cs:1079`).
  - Un avviso dell'IDLE che arriva intanto per lo stesso account aspetta il controllo in corso (`UI/App.cs:1121-1129`).
- **CONFERMATO.**
  - Sonde: NOOP su una connessione muta con `Timeout` di 1,5 s, errore dopo 1,5 s, `TimeoutException`, poi `IsConnected`
    falso. `GetNewAsync` di IMAP e di Gmail con la connessione in sottofondo muta e `Timeout` di 2 s: un solo tentativo,
    errore dopo 2,0 s, non riprovato; il `GetNewAsync` dopo apre una connessione nuova e trova il messaggio.
  - Prova a finestra F5 (`ripresa.ps1`): subito dopo un controllo di ogni due minuti tutte le connessioni dell'account 1
    muoiono (le nuove funzionano), arriva un messaggio sull'account 1 e uno sull'account 2 (che ha l'IDLE morto da F4), poi
    "rete tornata". "rete tornata" non fa nulla. Al giro dopo l'account 1 aspetta 60 s e finisce con "Operation timed out
    after 60000 milliseconds"; l'account 2 viene controllato solo dopo: **notifica dell'account 2 dopo 180 s,
    dell'account 1 dopo 240 s**.
  - Registro dell'autore, 24 ottobre: ripresa alle 10:41:03; il controllo di "Gmail 4" resta fermo, "fermo da più di 2
    minuti: passo agli altri account" alle 10:42:59, poi `IOException` ("The read operation failed") alle 10:43:00; i due
    messaggi di "Gmail 1", arrivati col PC sospeso, sono annunciati alle 10:43:06, **2 minuti e 3 secondi dopo la ripresa**.
    23 ottobre: ripresa alle 23:57:50, "cartelle" di due account Gmail con `IOException` alle 23:59:47, tre messaggi di
    "Gmail 1" annunciati alle 00:00:08 (2 minuti e 18 secondi dopo), con un controllo durato 21,5 s. Il 6 ottobre (ESITO.md,
    "Dopo l'installazione") la connessione principale di un account Gmail era finita con il tempo scaduto di 120 s dopo una
    ripresa.
- **Quanto spesso, quanto è grave**: a ogni ripresa dopo una sospensione lunga, cioè più volte al giorno su questo PC.
  Ritardo di 2-4 minuti sulla posta arrivata durante la sospensione e subito dopo; con quattro account in fila anche di più.
- **Rimedio**: alla ripresa e a `NetworkAddressChanged` chiudere senza saluto le connessioni non IDLE (il primo uso si
  ricollega in 1-2 s invece di aspettare 60-120 s); `TimeoutException` fra gli errori che `RunAsync` riprova una volta per
  le letture; i controlli degli account in parallelo (ognuno ha già il suo `checkingAccounts`). La scelta C-09 (60 s anche
  per Gmail), rimasta all'autore, oggi pesa di più.
- **Prova che lo tiene fermo**: la sonda A-02 deve trovare il messaggio al primo `GetNewAsync` dopo la connessione morta;
  F5 deve dare le due notifiche entro pochi secondi dal controllo.

### A-03 Quando l'IDLE si ricollega, i messaggi arrivati intanto non li annuncia nessuno

- **Che cosa vede l'autore**: dopo un cambio di rete o una ripresa la posta arrivata nel frattempo compare tardi, anche se
  l'IDLE è di nuovo collegato.
- **Quando succede**: una sessione nuova conta i messaggi da lì (`int count = inbox.Count` dopo EXAMINE,
  `Core/Idle.cs:254-256`) e avvisa solo di quelli dopo. Il ricollegamento dice solo `Connected`, che rimette a posto lo
  stato dell'account senza controllare nulla (`UI/App.cs:962-967`, `UI/MainWindow.xaml.cs:1244-1250`). Il messaggio arrivato
  mentre l'IDLE era giù resta al controllo di ogni due minuti.
- **CONFERMATO.** Sonda A-01/A-03 (IMAP): dopo `ResetConnectionsAsync` la sessione nuova parte, e l'avviso per il messaggio
  arrivato intanto è zero. Prova a finestra F8: IDLE muto, un arrivo, poi la connessione chiusa dal server: l'IDLE si
  ricollega in 0,3 s (senza nessuna riga nel registro della versione installata, A-10) e la notifica arriva 91 s dopo, dal
  controllo di ogni due minuti. Registro dell'autore: il 25 ottobre alle 14:01:43 quattro messaggi di "Gmail 1", arrivati col
  PC sospeso, sono annunciati dal controllo 2 minuti dopo la ripresa, mentre l'IDLE di quell'account era vivo (alle 14:01:22
  riceve l'eco di una lettura) e senza nessuna riga "IDLE interrotto": compatibile con un IDLE ricollegato da solo dopo la
  ripresa, che `RunAsync` fa senza scriverlo (A-10). Quando invece la rete cade in modo che Windows lo veda (23 ottobre alle
  14:49, "Host sconosciuto" per 10 s), l'account è "non in linea" e il controllo al ritorno della rete (A-15 dell'audit del 7
  ottobre) trova il messaggio 6 s dopo: lì il problema non c'è.
- **Quanto spesso, quanto è grave**: a ogni ricollegamento dell'IDLE (i 18 "IDLE interrotto" del registro dal 22 ottobre, più
  quelli che `RunAsync` riprende senza scriverli). Fino a 2 minuti.
- **Rimedio**: a ogni `Connected` che segue un errore o un ricollegamento, `CheckAccountAsync(provider, "IDLE ricollegato")`;
  oppure la sessione nuova parte dal numero dell'ultima vista (UIDNEXT) invece che da quello di adesso.
- **Prova che lo tiene fermo**: la sonda A-01/A-03 deve vedere il controllo (o l'avviso) dopo il ricollegamento.

### A-04 Nessun gestore della sospensione e della ripresa; i timer di .NET non contano il tempo sospeso

- **Che cosa vede l'autore**: riaperto il portatile, la posta arrivata mentre dormiva compare circa due minuti dopo, anche
  con Missiva aperta davanti.
- **Quando succede**: nel codice non c'è `SystemEvents.PowerModeChanged` né `WM_POWERBROADCAST` (`SystemEvents` serve solo
  per il tema, `UI/App.cs:156`; il solo segnale è `NetworkAddressChanged`, `UI/App.cs:264`, che fa A-01 e A-02 a metà). Alla
  ripresa nessuno chiude le connessioni né fa un controllo: si aspetta il giro del timer (e il primo giro trova le
  connessioni morte, A-02). I timer di .NET (`Task.Delay`, `CancelAfter` del rinnovo dell'IDLE, le attese dopo un errore,
  `StuckCheck` nel giro) contano solo il tempo da sveglio: dopo la ripresa il rinnovo dell'IDLE arriva fino a 9 minuti
  dopo, non subito.
- **CONFERMATO.** Ricerca nei sorgenti: nessun gestore. Sonda A-04: l'orologio dei timer di .NET (`TimerQueue.TickCount64`) è
  a 1 ms da `QueryUnbiasedInterruptTime`, l'orologio di Windows che non conta la sospensione; `GetTickCount64`, che la
  conta, è avanti di 129.453 s: **36 ore di sospensione dall'avvio di Windows del 23 ottobre**. Registro dell'autore: alle
  tre riprese del 23, 24 e 25 ottobre la posta arrivata col PC sospeso è annunciata 2 minuti e 18 secondi, 2 minuti e 3
  secondi e 2 minuti e 2 secondi dopo la prima riga scritta alla ripresa (A-02 e A-03); il 25 ottobre l'autore stava già
  leggendo un messaggio nel riquadro 27 s prima che arrivassero i quattro nuovi. Il 23 ottobre un controllo di "Outlook 3"
  partito prima della sospensione è "fermo da 14106 s" alla ripresa: finisce 27 s dopo con il tempo scaduto di HttpClient,
  che contava solo il tempo da sveglio.
- **Quanto spesso, quanto è grave**: a ogni ripresa; circa 2 minuti di ritardo sulla posta arrivata col PC sospeso, la più
  probabile da trovare appena si apre il portatile.
- **Rimedio**: un gestore di `PowerModeChanged` (Resume, anche `PBT_APMRESUMEAUTOMATIC`): chiudere sessioni IDLE e connessioni
  ferme (A-01, A-02), e dopo qualche secondo (rete pronta, come `OnNetworkChanged`) un controllo di tutti gli account
  e la query delta.
- **Prova che lo tiene fermo**: un comando di MISSIVA_KEYS "ripresa" che chiama lo stesso gestore: con le connessioni mute
  (F5) le due notifiche devono arrivare entro pochi secondi.

### A-05 Due messaggi vicini, o uno che Gmail rende visibile tardi

- **Che cosa vede l'autore**: arrivano due mail quasi insieme; la prima subito, la seconda un minuto dopo.
- **Quando succede.** `App.OnInboxEvent` riunisce gli avvisi dei 700 ms (`UI/App.cs:978-980`) e fa un controllo; Gmail rende
  il messaggio visibile alle altre connessioni anche qualche secondo dopo l'avviso (CLAUDE.md, "Controllo dei nuovi
  messaggi"), e per questo si riprova dopo 3 e 10 s (`UI/App.cs:988-993`). Ma si riprova solo se il controllo ha trovato
  zero messaggi (`if (found != 0) break`, `:990`), e `found` conta qualunque messaggio nuovo (`:1219`), non quello annunciato:
  se il primo è già visibile e il secondo no, niente tentativi. Lo stesso se l'avviso arriva durante un altro controllo
  dell'account (`-1`, rifatto una volta sola alla fine, `:1239`, senza i tentativi). Un messaggio che diventa visibile più
  di 13 s dopo l'avviso resta al controllo di ogni due minuti.
- **CONFERMATO il meccanismo, SOSPETTO quanto a Gmail.** Prove a finestra con il comando "arrivo-lento N" del server finto
  (EXISTS subito all'IDLE, messaggio visibile alle altre connessioni N secondi dopo): F7a visibile dopo 2 s, notifica a 4,2 s
  (tentativo dei 3 s); F7b dopo 5 s, notifica a 14,0 s (tentativo dei 10 s); F7c dopo 15 s, notifica a 107 s (controllo di
  ogni due minuti); F7d due arrivi nello stesso istante, il secondo visibile dopo 4 s: **il primo a 0,5 s, il secondo a
  78 s**, con una sola riga "IDLE: nuovo messaggio". Registro dell'autore: 22 ottobre alle 16:38:46 e 23 ottobre alle
  15:17:16 lo stesso disegno (compatibile, non dimostrato: il registro non dice quanti messaggi annunciava l'EXISTS), una
  riga dell'IDLE, il primo messaggio subito, il secondo dal controllo di ogni due minuti 48 e 16 s dopo, con l'IDLE vivo
  intanto (alle 16:39:22 avvisa di un segno cambiato). Quanto ritardi davvero Gmail non è misurato: nel registro dal 22 ottobre nessun tentativo a 3 o 10 s ha
  trovato qualcosa.
- **Quanto spesso, quanto è grave**: ogni volta che arrivano due messaggi vicini (conversazioni, notifiche di servizi,
  liste); da 10 s a 2 minuti.
- **Rimedio**: ripetere finché la connessione in sottofondo non vede il numero che l'IDLE ha annunciato (l'EXISTS dice
  quanti sono), per esempio a 3, 10, 30 e 60 s; contare come trovato solo un messaggio con UID oltre l'ultimo visto.
- **Prova che lo tiene fermo**: F7d deve dare la seconda notifica entro 5 s; in suite, un controllo con due EXISTS e un
  messaggio visibile in ritardo.

### A-06 I nuovi si scelgono per data di arrivo e orologio del PC, non per UID

- **Che cosa vede l'autore**: "il numero dei non letti è salito, ma la mail non c'è e la notifica non è arrivata"; compare
  solo quando l'elenco si rilegge.
- **Quando succede**: "dopo l'ultimo controllo" è `lastCheck`, l'inizio del controllo di prima meno un minuto, con
  l'orologio del PC (`UI/App.cs:1137`, `:1141`); `GetNewAsync` tiene solo i non letti con la data di arrivo del server dopo
  quell'istante (`ArrivalDates.AfterAsync`, `Core/ImapProvider.cs:892`; Gmail `Core/GmailProvider.cs:579`; Graph
  `receivedDateTime gt`, `Core/GraphProvider.cs:595-596`). Un messaggio con la data di arrivo più vecchia si perde: messo
  nella Posta in arrivo con la data che aveva altrove (tolto dalla posta indesiderata o spostato dal telefono, un filtro o
  un'importazione sul server), o diventato visibile più di un minuto dopo la sua data con un controllo nel mezzo, o con
  l'orologio del PC avanti (il margine di un minuto si accorcia di altrettanto). Le regole invece vanno per UID (il mark di
  `ArrivedAsync`, `Core/ImapProvider.cs:928-942`) e lo vedono.
- **CONFERMATO.** Sonda A-06 (IMAP e Gmail): due non letti arrivati dopo il controllo di prima, uno con la data di arrivo di
  150 s fa: `GetNewAsync` ne dà uno, i non letti dal server due, `GetArrivedAsync` per UID due. Prova a finestra F3
  ("arrivo-datato 300"): "IDLE: nuovo messaggio", tre controlli con 0 nuovi, nessuna notifica, l'elenco senza la riga
  ("stato elenco": 5 righe, l'UID nuovo assente), non letti da 2 a 3.
- **Quanto spesso, quanto è grave**: raro (non visto nel registro dell'autore, dove non si potrebbe distinguere), ma la mail
  non è in ritardo: è persa per le notifiche e per l'elenco fino alla rilettura. Con l'orologio di Windows sincronizzato
  il margine regge.
- **Rimedio**: per Gmail e IMAP i nuovi dopo l'ultimo UID visto con la stessa UIDVALIDITY (lo schema c'è già in
  `ArrivedAsync`), la data solo la prima volta; per Graph gli identificativi nuovi e da leggere della query delta.
- **Prova che lo tiene fermo**: la sonda A-06 deve dare 2 nuovi; F3 deve dare la notifica.

### A-07 Con le regole di Missiva la notifica aspetta la connessione principale

- **Che cosa vede l'autore**: la notifica arriva solo quando finisce di scaricarsi un allegato grande, o un minuto o due dopo
  la ripresa.
- **Quando succede**: le regole girano prima delle notifiche (`UI/App.cs:1143-1172`, poi `:1182`). Una regola che guarda il
  testo (`Rules.NeedsBody`, `Core/Rules.cs:438`: "il testo contiene", "in Cc") vuole il corpo di ogni messaggio nuovo, letto
  con `GetBodyAsync` (`Core/Rules.cs:571-575`) sulla connessione principale (Gmail `Core/GmailProvider.cs:775`, IMAP quella
  di base); le azioni (letto, contrassegno, sposta) usano anche loro la principale. Sulla principale passano l'elenco, il
  corpo dei messaggi aperti e gli allegati (`Core/ImapProvider.cs:1266-1267`); dopo la ripresa può essere morta (A-02).
- **CONFERMATO.** Sonda A-07: regola "il testo contiene fattura"; senza nulla sulla principale le regole costano 0,33-0,60 s;
  con un allegato di 1,2 MB in arrivo a pezzi sulla principale, messaggio nuovo trovato in 2 ms dalla connessione in
  sottofondo e regole finite a 6,45 s, nello stesso istante dello scaricamento.
- **Quanto spesso, quanto è grave**: solo per gli account con regole di Missiva attive che guardano il testo o che agiscono;
  quali regole abbia l'autore non lo so (il registro non ne mostra nessuna al lavoro dal 22 ottobre). Secondi, fino a 120 s
  con la principale morta.
- **Rimedio**: leggere il corpo per le regole sulla connessione in sottofondo (EXAMINE e BODY.PEEK, come gli anticipi), non
  sulla principale; le azioni restano dove sono.
- **Prova che lo tiene fermo**: la sonda A-07 deve finire le regole in meno di un secondo con lo scaricamento in corso.

### A-08 Microsoft (Graph): 30 secondi per costruzione, e due punti deboli

- **Che cosa vede l'autore**: con Outlook.com la mail arriva fino a mezzo minuto dopo il telefono; ogni tanto qualche
  minuto.
- **Quando succede.**
  - La query delta ogni 30 s (`UI/App.cs:942-945`, `DeltaEvery`): da 0 a 30 s per costruzione (scritto in CLAUDE.md, "Nuovi
    messaggi subito"; non è nuovo).
  - Un deltaLink che Graph non accetta più (400, 404, 410) torna a null e la lettura dopo è di nuovo "il punto di partenza",
    che non avvisa (`Core/GraphProvider.cs:651`, `:673-679`): quello che arriva in quel giro lo trova solo il controllo di
    ogni due minuti.
  - Le query delta degli account Graph sono in fila e aspettano anche il controllo che fanno partire (`UI/App.cs:1007-1021`);
    una richiesta su una connessione morta tiene fermo il giro fino ai 60 s di HttpClient (`Core/GraphProvider.cs:76`).
  - I token: un semaforo solo per tutti gli account Microsoft (`MicrosoftAuth.TokenGate`, `Core/MicrosoftAuth.cs:149`,
    `:231`) e `AcquireTokenSilent` senza un tempo limite (`:295`): se WAM tarda, aspettano tutte le richieste a Graph.
- **CONFERMATO dal codice** il deltaLink e la fila; **SOSPETTO** il token (WAM non si prova senza un account vero). Registro
  dell'autore dalla 0.3.628: 16 righe "delta ... non letta" (4 "WAM Error", alla ripresa o con la rete appena caduta), 2
  arrivi annunciati dalla query delta; il 23 ottobre un controllo di "Outlook 3" partito prima della sospensione è stato trovato fermo alla ripresa
  (A-04).
- **Quanto spesso, quanto è grave**: i 30 s sempre; il resto raro. Fino a 2 minuti.
- **Rimedio**: alla ripresa la query delta subito (A-04); dopo un deltaLink rifatto, un controllo dell'account; un tempo
  limite su `AcquireTokenSilent` (per esempio 30 s con un `CancellationToken`).
- **Prova che lo tiene fermo**: in suite, con il gestore HTTP finto, un 410 sulla delta seguito da un messaggio nuovo deve
  far partire un controllo.

### A-09 La connessione in sottofondo è condivisa

- **Che cosa vede l'autore**: la notifica qualche secondo dopo, di solito mentre si scorre un elenco o a finestra ferma.
- **Quando succede**: `GetNewAsync` e il conteggio usano la connessione in sottofondo, che serve anche le anteprime
  dell'elenco di Gmail (`Core/GmailProvider.cs:497-505`: per 50 messaggi fino a 8 s, CLAUDE.md, "Avvio veloce"), la copia
  dei 30 giorni (`GetRecentKeysAsync` 0,7-5,3 s secondo il commento di `Core/GmailProvider.cs:617-619`, poi i corpi uno alla
  volta), la Posta inviata per la rubrica (circa 2 s, una volta all'ora) e `StillThereAsync` della coda: il controllo aspetta
  il suo turno (`Gate`, riga "attesa della connessione in sottofondo" nella versione di prova).
- **CONFERMATO dal codice**; i secondi sono quelli scritti nei commenti e in CLAUDE.md, non rimisurati qui. Nel registro
  dell'autore, 24 ottobre alle 16:05:39, un avviso dell'IDLE di "Gmail 1" è diventato notifica 11 s dopo, con un controllo
  di 15,7 s, senza una ripresa vicina: SOSPETTO che sia questo (o una connessione in sottofondo ricollegata in silenzio), il
  registro non lo dice (A-10).
- **Quanto è grave**: secondi.
- **Rimedio**: il controllo dei nuovi messaggi su una connessione sua (o sulla connessione dell'IDLE, fra un rinnovo e
  l'altro: DONE, NOOP e SEARCH, poi di nuovo IDLE).

### A-10 Il registro della versione installata non dice quando l'IDLE si ricollega né quando è fermo

- **Che cosa vede l'autore**: nulla; chi legge il registro non può dire se un ritardo era un IDLE morto, una sospensione o un
  giro saltato.
- **Quando succede**: `RunAsync` riprende una connessione caduta senza scriverlo (`Core/GmailProvider.cs:224-228`); le righe
  "tempi:" (collegamenti, durata del giro di ogni due minuti) ci sono solo nelle versioni di prova (`Log.Time`,
  `Core/Store.cs:1300-1302`); il controllo di ogni due minuti senza novità non scrive nulla (`UI/App.cs:1216`); un DONE
  fermo non scrive nulla (A-01); il controllo partito al ritorno della rete (`OnNetworkBack`, `CheckNewAsync` senza motivo)
  si scrive come "controllo di ogni due minuti" (`UI/App.cs:1093`, `:1118`). Registro dell'autore: buchi fino a 19 ore senza
  una riga, che possono essere sospensioni o silenzio.
- **CONFERMATO** (prova F8: ricollegamento dell'IDLE con la sola riga "tempi:", che la versione installata non scrive).
- **Rimedio**: una riga per ogni ricollegamento dell'IDLE e per ogni sospensione e ripresa (con A-04), e una riga quando un
  arrivo viene annunciato dal controllo di ogni due minuti invece che dall'IDLE (oggi si capisce dal motivo, ma non perché).

## Le prove a finestra, in breve

Con `strumenti/sonde-arrivo/finestra.ps1 -Start` (due account IMAP finti, prova fuori dallo schermo, notifiche nel
registro), poi:

| Prova | Comandi | Esito |
|---|---|---|
| F1 | `-Measure -Server 1 -Do "arrivo ..."` | notifica dopo 1,2 s (700 ms di attesa degli avvisi più 150 ms di controllo) |
| F3 | `"arrivo-datato 300 ..."` | nessuna notifica, nessuna riga, non letti da 2 a 3 (A-06) |
| F4 | `"muto idle"` sul server 2, poi `"arrivo ..."` | notifica dopo 108 s, dal controllo di ogni due minuti (A-01) |
| F5 | `ripresa.ps1` | "rete tornata" senza effetto; notifiche dopo 180 e 240 s; "Operation timed out after 60000 milliseconds" (A-02) |
| F6 | dopo il rinnovo (DONE senza risposta) `"arrivo ..."` | notifica dopo 69 s; "stato account": IDLE True (A-01) |
| F7a-d | `"arrivo-lento 2|5|15 ..."`, due arrivi insieme | 4,2 s, 14,0 s, 107 s; il secondo di due a 78 s (A-05) |
| F8 | `"muto idle"`, `"arrivo ..."`, `"taglia"` | IDLE ricollegato in 0,3 s, notifica dopo 91 s (A-03) |

## Guardato e trovato a posto

- **L'ora e il cambio dell'ora legale** (stanotte): `lastCheck`, `StuckCheck`, `SafetyNet`, `checkingAccounts`,
  `UnreadSync`, `InboxIdle.OwnEcho` e l'età di un controllo usano `DateTime.UtcNow` (`UI/App.cs:1123`, `:1133`, `:1137`;
  `Core/Idle.cs:59`, `:92`, `:164`); la SEARCH per giorni prende un giorno in più (`DeliveredAfter(...Date.AddDays(-1))`), e
  la data di arrivo si confronta in UTC. Il cambio dell'ora non sposta nulla nell'arrivo. Il registro scrive l'ora locale
  (`Core/Store.cs:1406`): fra le 2 e le 3 di stanotte le righe hanno le stesse ore due volte. Fuori dalla mia parte: le righe
  dell'elenco hanno la data locale (`MessageSummary.Date`) e si ordinano con quella (per esempio
  `UI/MainWindow.xaml.cs:2802`): un messaggio delle 2:10 dopo il cambio può finire sotto uno delle 2:50 di prima. SOSPETTO,
  non provato, per la parte dell'elenco.
- **Un errore che ferma l'IDLE per sempre**: no. `InboxIdle.RunAsync` (`Core/Idle.cs:171-220`) prende ogni eccezione e
  riprova con attese fino a 5 minuti; esce solo se il server non ha IDLE o con la cancellazione. Il "per sempre" di A-01 è
  un'attesa senza errore, non un errore.
- **Gli avvisi dell'IDLE che sono l'eco di Missiva**: `OwnEcho` non scarta mai un EXISTS (`UI/App.cs:968`): un arrivo
  durante una scrittura si annuncia.
- **La notifica dopo il controllo**: righe e notifica partono prima del conteggio dei non letti (`ShowThenCountAsync`,
  `UI/App.cs:1284-1293`); "notifica a N ms" nel registro dell'autore dal 22 ottobre sta fra 126 e 4043 ms in 20 controlli
  su 24; gli altri sono 11 e 19 s (A-09 e A-02) e un controllo a cavallo di una sospensione.
  Le notifiche di più di tre messaggi diventano "N nuovi messaggi" (`UI/App.cs:1329`), senza ritardi. L'immagine del
  mittente si disegna la prima volta in un thread a parte e si aspetta (`UI/NotificationAvatar.cs:30-37`): decine di ms.
- **Finestra nell'area di notifica**: il timer di ogni due minuti, l'IDLE e la query delta girano anche a finestra nascosta e
  con WebView2 chiuso; le notifiche non passano da WebView2. Nel registro dell'autore molti arrivi sono "finestra nell'area
  di notifica" e arrivano come gli altri.
- **`notified`** (`UI/App.cs:17`, gli ultimi 2000): serve solo al minuto di margine. Un UID che si ripete dopo una
  UIDVALIDITY nuova potrebbe essere scartato: per Gmail non succede, per IMAP raro; non provato.
- **Graph, 429 e Retry-After**: due tentativi con al più 5 s (`Core/GraphProvider.cs:305-311`) e `MailboxGate` con quattro
  posti per casella (`:110-147`): per i controlli dei nuovi messaggi, decimi di secondo di fila al più (ESITO.md, "Scelte
  fatte qui, da rivedere" del blocco 9). Nel registro dell'autore nessun 429 dal 22 ottobre.
- **I promemoria, Posponi e gli invii pianificati** dopo la ripresa: fuori dalla mia parte; si vedono partire alla prima riga
  dopo la ripresa (righe "promemoria del calendario" con errore di rete subito dopo).

## Che cosa non ho potuto provare

- **Una sospensione vera del PC**: non si sospende il PC dell'autore. La ripresa è simulata con le connessioni mute del
  server finto (che lasciano TCP vivo: il caso peggiore, quello in cui solo un tempo scaduto di Missiva può accorgersene) e
  letta nel registro dell'autore. Quanto ci mette TCP a dare errore su una connessione morta dopo una ripresa vera (il
  registro dell'autore mostra `IOException` circa 2 minuti dopo la ripresa, il 23 e il 24 ottobre) non è stato misurato.
- **Il timer di ogni due minuti dopo la ripresa**: è un `DispatcherTimer` di WPF; se conti o no il tempo sospeso non l'ho
  provato. Il registro dell'autore è compatibile con un primo giro subito dopo la ripresa (24 ottobre) e anche con uno due
  minuti dopo (25 ottobre).
- **Gmail vero**: quanto tardi rende visibile un messaggio alle altre connessioni (A-05), se chiude da sé le connessioni in
  IDLE e dopo quanto, se manda qualcosa durante l'IDLE. Niente rete vera.
- **Graph vero e WAM**: la query delta, i deltaLink scaduti e i token dopo una ripresa (A-08) sono letti nel codice e nel
  registro, non provati con un gestore finto.
- **Programmi in mezzo** (antivirus che controllano la posta, VPN): quali tengano viva una connessione TCP il cui server è
  andato, cioè quanto spesso A-01 diventa "per sempre" sul PC dell'autore.
- **L'orologio del PC dell'autore**: lo stato della sincronizzazione dell'ora non si legge (il servizio Ora di Windows era
  fermo al momento della richiesta, come succede a quelli che partono solo quando servono); quanto sia avanti o indietro
  rispetto ai server (A-06) non lo so.
- **Le regole dell'autore** (A-07): il registro non le mostra al lavoro; non so se ne abbia che guardano il testo.

## Esito delle correzioni (25 ottobre 2026)

Ramo `correzioni-arrivo` (da main `fd971be`), blocco 1 delle correzioni dell'audit di affidabilità: risveglio e
connessioni. Quello che l'autore ha approvato: "Missiva si accorge del risveglio e dei cambi di rete: rifà subito le
connessioni, controlla tutti gli account insieme e rilegge elenco e cartelle. In più: un limite di tempo sulla
connessione che resta ferma, un secondo tentativo dopo un tempo scaduto o un errore di rete, e per Gmail il controllo di
riserva ogni minuto invece che ogni due." I controlli nuovi sono nel gruppo `affidabilita-arrivo`
(`Core/ProvaBozze.AffidabilitaArrivo.cs`, da solo con `Missiva.exe --prova-audit risultato.txt affidabilita-arrivo`,
35 controlli in circa 50 s); ognuno è stato visto cadere con il codice di prima (dove serviva un nome nuovo, con il nome già presente e il
comportamento di prima), tranne quelli che provano pezzi nuovi (Arrival.RunAllAsync, RetryAsync, SleepClock,
NetworkPrint), detti nel commit.

### Punto per punto

| Punto | Esito | Commit | Controlli (gruppo affidabilita-arrivo) |
|---|---|---|---|
| A-01 IDLE morto in silenzio | FATTO: rinnovo ogni 2 minuti invece che ogni 9, cane da guardia sul DONE (20 s), Watching falso quando la sessione cade | `4b005e6` | i tempi dell'IDLE; IDLE muto e DONE senza risposta (IMAP, Gmail); server muto per tutti, Watching; misura del rinnovo |
| A-02 connessioni morte, account in fila | FATTO: le letture riprovano una volta dopo un tempo scaduto (mai le scritture), gli account si controllano insieme, il giro del timer non si salta più | `0f7e4c2` | GetNewAsync con la connessione in sottofondo muta (IMAP, Gmail); una scrittura non si ripete; RunAllAsync; sorgenti di CheckNewAsync |
| A-03 IDLE ricollegato senza controllo | FATTO: il Connected di una sessione su una connessione nuova dice Reconnected, e App controlla l'account | `6631722` | IDLE tagliato dal server con un arrivo (IMAP, Gmail); sorgenti di OnInboxEvent |
| A-04 nessun gestore della ripresa | FATTO: App.WakeAsync dalla ripresa (PowerModeChanged e l'orologio della sospensione) e dai cambi di rete veri; DropConnections; tentativi brevi se la rete non è pronta; cartelle ed elenco riletti; query delta rifatta; "ripresa" in MISSIVA_KEYS | `c107d55` | DropConnections con un controllo fermo e l'IDLE in sessione, due risvegli di fila (IMAP, Gmail); RetryAsync; SleepClock; NetworkPrint; sorgenti |
| Controllo di riserva ogni minuto | FATTO: Gmail e IMAP ogni minuto, Graph ogni due | `ed1c27f` | PollEvery e PollDue; misura del costo (IMAP, Gmail) |
| A-05 tentativi dopo l'avviso | FATTO: l'avviso dice quanti messaggi, e il controllo si ripete a 3, 10 e 30 s finché non li ha trovati | `e099f01` | un EXISTS con due messaggi (IMAP, Gmail); sorgenti |
| A-06 nuovi per data | FATTO per Gmail e IMAP: per UID, con il mark; i messaggi messi da Missiva nella Posta in arrivo non si annunciano; primo controllo 10 s dopo l'avvio. Graph NON FATTO (sotto) | `abe8f2a` | due arrivi, uno con la data di 150 s fa, uno già letto, uno vecchio spostato (IMAP, Gmail); Sposta e Annulla (IMAP, Gmail); sorgenti |
| A-07 regole e connessione principale | FATTO per la lettura dei corpi (connessione in sottofondo); le azioni restano sulla principale | `659c179` | regola sul testo con uno scaricamento in corso |
| A-08 Graph | FATTO il deltaLink rifatto (fa controllare l'account); NON FATTO il tempo limite sul token di WAM (sotto) | `abb99f3` | 410 sulla delta |
| A-09 connessione in sottofondo condivisa | NON FATTO (sotto) | | |
| A-10 registro | FATTO: sospensione e ripresa, IDLE fermo, IDLE ricollegato, motivo del controllo dopo un cambio di rete, com'era l'IDLE quando il controllo di riserva trova posta | `c107d55`, `6631722`, `abe8f2a`, `004a40a` | sorgenti delle righe |

Gli aiuti di prova (le connessioni mute del server finto e le prove a finestra) sono in `0dd2e72`.

### Le prove a finestra, prima e dopo

Debug di questo ramo fuori dallo schermo (`--foto`), due account IMAP verso due `--imap-finto` su 127.0.0.1, notifiche
solo nel registro, con `strumenti\arrivo\finestra.ps1`, `ripresa.ps1` e `rete-lenta.ps1` (nuova). "Prima" è il codice di
`fd971be` con i soli aiuti di prova, misurato alle 15:44-15:58; "dopo" il codice finale (F7d e F3 dopo i loro punti).

| Prova | Prima | Dopo |
|---|---|---|
| F1 arrivo con l'IDLE vivo | 1,2 s | 1,1 s |
| F3 "arrivo-datato 300" | nessuna notifica | 1,2 s |
| F4 IDLE muto, poi un arrivo | 94,5 s | 40,4 s (al più un giro di riserva, 60 s); al rinnovo dopo l'IDLE si ricollega da solo |
| F5 connessioni morte, un arrivo per account, poi il segnale | 180 e 240 s con "rete tornata" (che non faceva nulla) | 0,2-0,3 s dopo "ripresa" e dopo "rete tornata" |
| F6 un arrivo dopo il rinnovo con il DONE senza risposta | 69 s, e l'IDLE fermo per sempre | 1,0 s: il rinnovo ha chiuso la sessione dopo 20 s e l'IDLE è di nuovo in sessione |
| F7d due arrivi insieme, il secondo visibile dopo 4 s | il secondo a 61 s | il secondo a 14,1 s |
| F8 IDLE muto, un arrivo, il server taglia la connessione | 112,6 s | 0,4-0,6 s |
| F9 ripresa con la rete che torna 4 s dopo (nuova) | (non c'era il gestore) | 6,4 s dalla ripresa, 2,4 s dal ritorno della rete |

### Misure

- **Rinnovo dell'IDLE** (gruppo affidabilita-arrivo, server finto nello stesso processo, quindi per eccesso): da 1,3 a 9 ms
  di processore per rinnovo secondo il giro (37 rinnovi in 4 s, contro un IDLE senza rinnovi); ogni 2 minuti sono 30
  rinnovi all'ora per account invece di 7, cioè da 40 a 270 ms all'ora, e poche centinaia di byte ciascuno.
- **Controllo di riserva** (gruppo affidabilita-arrivo): un controllo senza novità sono 3 comandi IMAP (NOOP, UID SEARCH
  dei nuovi dopo il mark, UID SEARCH del conteggio) e 0,3-0,9 ms di processore, client e server finto insieme; ogni minuto
  invece che ogni due sono 30 controlli in più all'ora per account, 90 comandi e circa 10-30 ms all'ora.
  A finestra (Debug fuori dallo schermo, due account IMAP verso due server finti, ferma, 360 s dopo un minuto di
  assestamento): con il giro di prima (`--controllo 120`) Missiva 4,04 ms di processore al secondo e 37 comandi IMAP ai
  due server, con quello nuovo di un minuto 3,86 ms/s e 55 comandi. Il processore non cambia in modo misurabile (il resto
  della finestra pesa di più); i comandi sono 18 in più in 6 minuti per due account, cioè 90 all'ora per account, come
  nella suite.
- **Buttare una connessione con un comando in attesa** (server finto, connessione muta): chiudere il client di MailKit
  fermava un NOOP in attesa a volte sì e a volte no, un IDLE mai (il server vedeva la connessione chiusa, IdleAsync restava
  fermo); chiudere il socket li fermava 36 volte su 36 in 0-6 ms; DropConnections con la sola chiusura 5 volte su 6 (una
  corsa dentro MailKit lasciava il NOOP fermo fino al tempo scaduto), con RunAsync che smette di aspettare (Dropped) 12 su
  12 in 3-16 ms. Per questo le connessioni IMAP si aprono con un socket di Missiva (ImapSockets). Chiuderle in sottofondo
  (Task.Run) con il pool dei thread occupato dalle chiusure dei provider, che aspettano il LOGOUT fino a cinque secondi,
  arrivava tardi: Drop chiude sul thread di chi chiama.

### Non fatti, e perché

- **C-09, il tempo scaduto di Gmail a 60 s come IMAP**: i tre tempi scaduti di 120 s del registro dell'autore sono tutti
  nei due minuti dopo una ripresa, che ora butta le connessioni da sé; per una connessione morta senza che Windows lo dica
  ora c'è il nuovo tentativo delle letture; il rischio della scelta (una ricerca lunga di Gmail interrotta e rifatta) si
  guarda solo sulla casella vera (prova 33 di ESITO.md). Resta una scelta dell'autore, che pesa meno di prima.
- **Il keepalive di TCP sull'IDLE**: non vede una connessione che TCP tiene viva ma su cui il server non risponde più (il
  caso della sonda arrivo-done); il rinnovo di 2 minuti con il limite sul DONE vede tutte e due, e la ripresa e i cambi di
  rete li prende WakeAsync.
- **A-06 per Graph**: la query delta parte dagli ultimi due giorni e non vede i messaggi più vecchi spostati nella Posta in
  arrivo, cioè proprio quelli di A-06; i suoi identificativi non aggiungono nulla con una correzione semplice. Graph resta
  per data, con la query delta ogni 30 s.
- **A-08, il tempo limite sul token di WAM**: SOSPETTO nel rapporto, non si prova senza un account vero, e non è detto che
  un AcquireTokenSilent annullato fermi WAM.
- **A-09, una connessione propria per i controlli**: sarebbe una sesta connessione per account Gmail (il limite di Gmail è
  15, contando il telefono), un collegamento in più a ogni avvio e a ogni risveglio, e diversi controlli vecchi della suite
  guardano quale connessione usa il controllo; per un SOSPETTO con un caso solo nel registro (11 s) non è un rimedio
  semplice e sicuro.

### Comportamenti che l'autore vedrà cambiati

- Dopo la ripresa del PC e dopo un cambio di rete le notifiche della posta arrivata intanto arrivano in pochi secondi, e
  cartelle ed elenco si rileggono. Nel registro due righe per ogni risveglio, e una per ogni sospensione.
- Il controllo di riserva è ogni minuto per Gmail e IMAP; i testi delle Impostazioni (Notifiche; Avvio e chiusura) e
  LEGGIMI.md e MANUAL.md lo dicono.
- **Da guardare**: un messaggio da leggere messo nella Posta in arrivo da un altro programma con una data di arrivo di non
  più di un giorno prima (tolto dalla posta indesiderata sul telefono, spostato dal web) ora si annuncia come nuovo, come
  voleva A-06; prima non si annunciava. Quelli più vecchi e quelli che sposta Missiva stessa no. Se non piace, la guardia
  di un giorno (ImapProvider.NewAge) si accorcia.
- Un primo controllo dei nuovi messaggi 10 s dopo l'avvio, con una riga nel registro per account.

### Che cosa non ho potuto provare

- Una sospensione vera del PC, e la sospensione moderna (se Windows dà PowerModeChanged ai programmi desktop): la ripresa
  è simulata con "ripresa" in MISSIVA_KEYS e le connessioni mute del server finto; l'orologio della sospensione
  (Arrival.SleepClock) è provato con un orologio finto.
- Un cambio di Wi-Fi vero (Arrival.NetworkPrint è provato solo come "uguale due volte di fila").
- Gmail e Graph veri, e in particolare il collegamento con il socket di Missiva (ImapSockets) verso imap.gmail.com con
  TLS, IPv6 e IPv4: è lo stesso schema di MailKit, provato solo verso 127.0.0.1. Dopo l'installazione le righe da guardare
  nel registro sono quelle degli errori di collegamento ("non risponde sulla porta") e "ripresa dalla sospensione".
- Che il secondo tentativo dopo un tempo scaduto non rifaccia una ricerca lunga su una casella grande (si ripete al più una
  volta, e solo per le letture).
