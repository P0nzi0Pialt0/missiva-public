# Secondo audit di sicurezza di Missiva

Commit di main letto: `2084922f87e64d08421bc949788b285ee08582b2` (8 ottobre 2026).

## Metodo e limiti

- Sessione nel cloud, su Linux (Ubuntu 24.04, .NET SDK 10.0.112 dai pacchetti di Ubuntu). Missiva non si esegue,
  `--prova-bozze` non gira, WPF, WebView2, DPAPI e Windows non ci sono.
- Le parole sono quelle di CLAUDE.md:
  - **MISURATO**: eseguito qui, con lo strumento detto nel punto.
  - **CONFERMATO**: percorso del codice letto, con file e riga.
  - **SOSPETTO**: manca qualcosa per confermarlo, ed è detto che cosa.
- Prove eseguite, tutte fuori dal repository (in `/tmp`):
  - `Core/MailHtml.cs` copiato in un progetto console con AngleSharp 1.8.2 e un sostituto minimo di
    `System.Windows.Media.Color`. Le pagine generate imitano `ReadingPage.Document`: shadow root dichiarativa, la CSP
    della pagina o nessuna CSP, base `https://missiva.example/`. Sono state caricate in Chromium 1194 (Playwright) con
    JavaScript spento, un ascoltatore su 127.0.0.1 e tutte le richieste registrate.
  - Copie di funzioni pure in progetti console: `Safety`, `Unsubscribe.Links`, `ContactLinks`,
    `SenderCheck.SameOrganization`, il confronto degli host di `Invitation`, la decompressione di `Backup`, PBKDF2.
  - Un progetto MSBuild di prova con la stessa struttura delle proprietà di `Missiva.csproj`.
- Le misure di tempo sono di questo container. Non si confrontano con il PC dell'autore.
- Non riproposti, perché ESITO.md li dà per decisi: S-06, S-08, le chiavi delle mappe di S-26, il cid di S-15. Si
  ritorna su un punto già noto solo se c'è un fatto nuovo, e lo si dice.

## Parte A: le correzioni reggono?

### T-01 Immagini esterne scritte come le legge Chromium sfuggono alla pulizia, e partono in una conversazione con un mittente consentito

- **Dove**:
  - `Core/MailHtml.cs:783-787` (`IsRemote`): riconosce solo i valori che cominciano con `http://`, `https://` o `//`.
  - È usato per `src`, `background` e `poster` (`:117-149`) e per gli altri attributi (`:189-195`).
  - Lo strato della pagina vale per tutta la pagina, non per il singolo messaggio: `UI/ReadingPage.cs:78`
    (`RemoteAllowed = entries.Any(e => e.Expanded && e.AllowRemote)`) e `UI/ReadingView.xaml.cs:1277`.
- **Scenario**:
  - Un mittente non consentito manda `<img src="http:\\host.example/p.gif">`. Funzionano allo stesso modo `\\host`,
    `/\host`, `http:/host`, `http:host`, `ht&#9;tp://host` e `<td background="\\host/x">`.
  - `Prepare(..., allowRemote: false)` lascia l'attributo com'è, con `BlockedImages` a 0: non compare nemmeno la fascia
    "Mostra immagini". Chromium invece toglie tabulazioni e a capo, legge `\` come `/` e risolve `http:host` come
    `http://host`.
  - Da solo il messaggio è fermato dalla CSP (`img-src` della sola origine) e dall'intercettazione.
  - In una conversazione basta un altro messaggio espanso di un mittente consentito: la pagina ha
    `img-src ... https: http:` e `RemoteAllowed` vero, e le immagini del mittente non consentito partono. Il mittente
    sa che il messaggio è stato letto e da quale IP.
  - È proprio il caso che il commento di `:189-190` vuole escludere ("Il blocco non deve dipendere dalla pagina: con un
    altro messaggio consentito aperto accanto, il riquadro lascerebbe passare").
  - Un dettaglio vicino: `OnResourceRequested` giudica con `current` (la pagina appena costruita), non con `servedPage`.
    Le richieste in ritardo della pagina di prima si giudicano quindi con la regola della nuova.
- **Gravità**: Media. È un aggiramento della barriera che la correzione dichiarava indipendente dalla pagina. Serve una
  conversazione con un mittente consentito espanso, che è un caso normale (newsletter, colleghi).
- **Stato**: MISURATO.
  - File di prova con 10 forme. Con `allowRemote: false` la pulizia ne ha lasciate 9, con `blocked=1`: ha contato solo
    `&#10;//host`, che riconosce.
  - Caricata in Chromium headless senza CSP, la pagina ha fatto 9 richieste all'ascoltatore: 8 ai percorsi scelti, una
    malformata che resta su missiva.example. Le stesse 9 con la CSP di una pagina che consente le immagini.
  - Con la CSP di una pagina che non le consente, nessuna richiesta.
  - L'ha misurato in modo indipendente anche un secondo banco, con 5 forme e lo stesso esito.
  - `url()` nel CSS invece è a posto: un indirizzo che `IsRemote` non riconosce diventa `none` (`:858-868`).
- **Proposta**:
  - Per `src`, `background`, `poster` e i riferimenti non di link, una lista bianca al posto di `IsRemote`. Restano
    solo:
    - `data:`;
    - `cid:` collegato;
    - `https://missiva.example/...`;
    - con `allowRemote`, un indirizzo che `LinkTarget` (che legge già come Chromium) accetta come http o https.
  - Tutto il resto si toglie, e si conta in `blocked` se `Uri.TryCreate(new Uri("https://missiva.example/"), valore)`
    dà un host diverso da missiva.example.
  - Per il controllo generico di `:191-195` lo stesso giudizio: "risolto contro la pagina, l'host non è missiva.example".
  - In `OnResourceRequested` usare `servedPage ?? current`.
- **Verifica su Windows**:
  - In `HtmlAuditTests` le dieci forme con `allowRemote: false`: ognuna senza attributo remoto, e `BlockedImages` uguale
    al numero di immagini.
  - A mano: `--page-thread` con un messaggio consentito e uno no, tutti e due espansi, Edge headless con un ascoltatore
    su 127.0.0.1. Nessuna richiesta.

### T-02 Le immagini dei mittenti consentiti si caricano da sole con la fascia rossa, negli .eml e nelle PEC false

- **Dove**:
  - `UI/ReadingView.xaml.cs:1002-1003` (`LoadsImages`): guarda solo la Posta indesiderata e il mittente consentito, mai
    `SenderCheck.Failed`.
  - `Core/Pec.cs:184`: `ImagesFrom` è il From di postacert.eml, qualunque sia lo stato della firma.
  - `UI/MainWindow.xaml.cs:4630`: un .eml si apre con `junkFolderId` null.
  - `UI/ReadingView.xaml.cs:1565`: "Mostra immagini" ricorda per sempre il mittente della pagina.
- **Scenario**:
  - (a) Un messaggio con il From falsificato di un dominio consentito (per esempio banca.example), DMARC fallito,
    arriva nella Posta in arrivo. C'è la fascia rossa, ma le immagini partono subito: chi l'ha mandato sa che è stato
    aperto e da quale IP. Logo e Annulla iscrizione invece la fascia rossa la rispettano (`Unsubscribe.cs:94`).
  - (b) Una busta che "sembra posta certificata, ma" non è firmata da un gestore, con dentro un postacert.eml il cui From
    è un mittente consentito: le immagini partono.
  - (c) Uno spam in Posta indesiderata con un .eml allegato che ha il From di un mittente consentito: si apre l'allegato
    e le immagini partono. Logo e domini noti escludono già gli .eml perché "le intestazioni le ha scritte chi l'ha
    mandato"; le immagini no.
  - (d) "Mostra immagini" premuto in questi casi aggiunge per sempre a `ImageSenders` il dominio falsificato.
- **Gravità**: Media. È la conferma di lettura che F-12 voleva togliere, e in (a) e (b) senza nemmeno un clic.
- **Stato**: CONFERMATO (percorsi letti alle righe dette).
- **Proposta**: `LoadsImages` tratta come la Posta indesiderata (solo `imagesOnce`, nulla di ricordato) i messaggi con:
  - `SenderCheck.Own(...)` che dà `Failed` o `Malformed`;
  - `body.Pec` con `Pec.Band(...).Tone == "danger"`;
  - il provider `EmlProvider`.

  In `Pec.cs:184`, `ImagesFrom = ""` se la busta non è firmata da un gestore. `ShowImages` non chiama `AllowImagesFrom`
  in questi casi.
- **Verifica su Windows**: in `SenderAuditTests`, `LoadsImages` falso per un mittente consentito con `dmarc=fail`, per
  la busta finta senza firma di `PecEnvelope` e per un `EmlProvider`. Dopo `ShowImages`, `ImageSenders` invariato.

### T-03 Account IMAP: una Authentication-Results scritta dal mittente vale come quella del server, e dà il logo per sempre

- **Dove**:
  - `Core/SenderCheck.cs:259-264` (`Own`, caso IMAP): prende la prima intestazione (`headers[0]`) e la accetta se
    l'authserv-id, ridotto con `Settings.ImageKey`, è il dominio del server IMAP o dell'indirizzo.
  - Il segno resta: `Core/SenderLogos.cs:181-183` (`Verified` scritto, mai tolto).
- **Scenario**:
  - Molti server IMAP piccoli non scrivono Authentication-Results e non tolgono quelle che arrivano col messaggio. La
    RFC 8601, sezione 5, lo chiede, ma non è garantito.
  - Il mittente conosce il dominio del destinatario e mette in cima al messaggio
    `Authentication-Results: mail.<dominio del destinatario>; dmarc=pass header.from=banca.example`. Basta anche un
    sottodominio, perché `ImageKey` lo riduce.
  - Missiva la crede sua:
    - `Authenticated` è vero;
    - il dominio diventa `Verified` in domini.dat per sempre;
    - con un From falsificato compare il logo della banca;
    - nessuna fascia rossa;
    - con List-Unsubscribe-Post è possibile anche il clic singolo di Annulla iscrizione.
  - Dopo la prima volta anche i messaggi successivi mostrano il logo nell'elenco, prima che arrivi il corpo. ESITO,
    blocco 2, lo aveva già messo fra le scelte da rivedere; il fatto nuovo è che basta un solo messaggio costruito.
- **Gravità**: Media. Il logo è un segnale di fiducia e qui non servono trucchi di analisi. Dipende dal server.
- **Stato**: CONFERMATO il percorso. SOSPETTO quanti server lascino passare l'intestazione (ESITO, blocco 3: IMAP
  generico mai provato dal vivo).
- **Proposta**:
  - Per gli account IMAP, le Authentication-Results non contano per il logo né per il clic singolo: `Authenticated`
    falso con `AccountKind.Imap`. La fascia rossa può restare: nasce da un "fail", e chi attacca non ha interesse a
    scriverne uno.
  - In alternativa, accettarle solo se sopra di loro c'è un `Received` del server della casella. Va chiesto anche
    `Received` in `SenderCheck.FetchAsync`.
  - In ogni caso `Verified` scade (per esempio dopo 90 giorni) e cade al primo DMARC fallito per quel dominio.
- **Verifica su Windows**: sul server IMAP finto, un messaggio con una sola Authentication-Results aggiunta dal
  mittente, con l'authserv-id del dominio dell'account: `SenderLogos.IsAuthenticated` resta falso e `Unsubscribe.For`
  non dà `OneClick`.

### T-04 Gmail: si cerca l'authserv-id di Gmail in tutte le intestazioni, non solo in cima

- **Dove**: `Core/SenderCheck.cs:254` (`headers.Select(Parse).FirstOrDefault(r => r.AuthservId == <Gmail>)`).
- **Scenario**:
  - Di norma Gmail mette la sua in cima e vince quella.
  - Dove Gmail non la scrive, vince la prima con il suo authserv-id, anche se l'ha scritta il mittente: posta presa da
    altri account, importazioni, messaggi mandati a sé.
  - L'effetto è come in T-03.
- **Gravità**: Bassa.
- **Stato**: CONFERMATO il codice; SOSPETTO che esistano messaggi senza quella di Gmail.
- **Proposta**: solo `headers[0]`, e solo con l'authserv-id di Gmail.
- **Verifica su Windows**: un controllo con due intestazioni, quella in cima di un altro server: `Own` dà null.

### T-05 Microsoft: authserv-id confrontato con `EndsWith` senza il punto, e accettato anche se manca

- **Dove**: `Core/SenderCheck.cs:256-258`.
- **Scenario**:
  - Un authserv-id che finisce con il nome del servizio di Microsoft senza un punto davanti passa (per esempio
    `x` seguito dal nome).
  - Un authserv-id assente vale sempre.
  - Contano solo se l'intestazione è la prima in alto, quindi solo dove Exchange non ha messo la sua.
- **Gravità**: Bassa.
- **Stato**: CONFERMATO il codice; SOSPETTO il caso reale. Non è verificato che `internetMessageHeaders` di Graph tenga
  sempre l'ordine.
- **Proposta**: uguaglianza, o `EndsWith("." + nome)`. Per il logo, un authserv-id assente non basta.

### T-06 Il segno "autenticato" usa il dominio a due etichette, non la lista dei suffissi pubblici

- **Dove**: `Core/SenderCheck.cs:236` (`Same`, con `SenderLogos.DomainOf`) e `Core/SenderLogos.cs:79-91` (`TwoLevel`
  scritto a mano).
- **Scenario**:
  - Sulle piattaforme che danno un sottodominio a ogni cliente (`cliente1.piattaforma.example`), una firma DKIM
    superata di un cliente vale per il From di un altro cliente: si riducono tutti e due a `piattaforma.example`.
  - Lo stesso con i suffissi a due livelli che mancano da `TwoLevel`.
  - Il logo mostrato è quello della piattaforma, non di un marchio altrui: il danno è limitato.
- **Gravità**: Bassa.
- **Stato**: CONFERMATO.
- **Proposta**: senza DMARC, la firma DKIM o il mittente SPF devono essere il dominio del From o un suo genitore fino a
  `DomainOf`. Con DMARC superato, `header.from` deve essere uguale all'host del From.

### T-07 Il disegno dei loghi fermato dopo 5 secondi: se `ControlledExecution` non ferma il thread, nessuno lo sa

- **Dove**: `Core/SenderLogos.cs:857-888` (`OnStaAsync`).
- **Scenario**:
  - Dopo il tempo il risultato è null, ma un thread fermo in codice nativo (WPF, DirectWrite) continua a girare e a
    chiedere memoria.
  - Un'interruzione a metà di WPF può lasciare a metà anche lo stato condiviso.
  - Oggi `LogoSvg` ferma prima tutti i casi noti.
- **Gravità**: Bassa.
- **Stato**: SOSPETTO: serve un SVG che superi la lista bianca e tenga occupato SharpVectors, provato su Windows.
- **Proposta**: contare i thread non fermati. Da uno in su, niente più disegni di loghi nella sessione, con una riga nel
  registro.

### T-08 Posta indesiderata non riconosciuta: il blocco delle immagini cede

- **Dove**:
  - `UI/MainWindow.xaml.cs:3161-3162` (`JunkOf` null se manca la cartella);
  - `Core/GraphProvider.cs:329-338` (se la lettura di junkemail fallisce, la cartella diventa una qualunque);
  - `Core/ImapProvider.cs:339-347, 419-422` (una sola Posta indesiderata, e dai nomi solo in cima);
  - in più `UI/ReadingView.xaml.cs:696, 701, 733, 975` guardano solo la cartella, non `InJunk`.
- **Scenario**:
  - Su IMAP con una "Junk" segnata SPECIAL-USE e una "Spam" del filtro del server, i messaggi della seconda caricano
    le immagini dei mittenti consentiti.
  - Lo stesso su Graph dopo un 429 sulla lettura delle cartelle speciali.
- **Gravità**: Bassa.
- **Stato**: CONFERMATO il codice; SOSPETTO la frequenza.
- **Proposta**:
  - IMAP: segnare come indesiderate tutte le cartelle con `\Junk` o con un nome della lista, e passarne un insieme.
  - Graph: ripiego sulla copia locale delle cartelle speciali.
  - Ovunque `InJunk(entry.Summary, junk)`.

### T-09 `SafeFileName`: nomi riservati di Windows incompleti

- **Dove**: `Core/Safety.cs:40-44, 55, 87`.
- **Scenario**: restano com'erano `COM¹`, `COM²`, `COM³`, `LPT¹`, `LPT²`, `LPT³` (riservati secondo la documentazione
  di Windows), `COM0`, `LPT0`, `CONIN$`, `CONOUT$` e `CON .txt` (Win32 toglie gli spazi prima di riconoscere il
  dispositivo). Salva o Apri scrivono su un dispositivo o falliscono. Non esegue codice.
- **Gravità**: Bassa.
- **Stato**: MISURATO (funzione copiata). SOSPETTO l'effetto su Windows 11.
- **Proposta**: aggiungere quei nomi e confrontare `nome.Split('.')[0].TrimEnd(' ')`.
- **Verifica su Windows**: in `SecurityLowAuditTests`, quei nomi passati da `SafeFileName`, poi Salva in una cartella
  temporanea.

### T-10 `Safety.Dangerous`: le pagine HTML e SVG allegate si aprono senza domande

- **Dove**: `Core/Safety.cs:24-36`.
- **Scenario**:
  - L'elenco di Outlook c'è tutto (provato: .hta, .msc, .cpl, .lnk, .url, .iso, .img, .vhd, .vhdx, .chm, .jar,
    .ps1, .reg, .scf, .appref-ms, .msix, .application e gli altri).
  - Non ci sono `.htm`, `.html`, `.shtml`, `.xhtml`, `.svg`, `.mht` e `.mhtml`: la pagina di accesso falsa in
    allegato, il caso di phishing più comune. Apri la passa al browser con JavaScript.
  - Mancano anche `.appcontent-ms`, `.udl` e `.diagpkg`: SOSPETTO che siano nell'elenco di Outlook, da controllare.
- **Gravità**: Bassa.
- **Stato**: MISURATO (`IsDangerous` falso per questi).
- **Proposta**: per HTML, SVG e MHT nessun blocco, ma una conferma propria ("pagina web arrivata per posta: non scrivere
  password"), oppure l'anteprima come testo.

## Parte B: codice nuovo

### T-11 Annulla iscrizione con un messaggio: parte dall'indirizzo principale, non dall'alias che riceve la lista

- **Dove**:
  - `UI/ReadingView.xaml.cs:1479` porta a `UI/MainWindow.xaml.cs:4976-4981` (`OnMailto`, `ComposeMode.New` senza
    originale).
  - `SendIdentity.For` si usa solo con un originale (`UI/ComposeWindow.xaml.cs:85`).
  - La conferma nomina l'indirizzo principale (`Core/Unsubscribe.cs:112`).
- **Scenario**: una newsletter ricevuta su un alias ("Altri indirizzi"). Il messaggio di annullamento si prepara
  dall'indirizzo principale, e chi gestisce la lista lo scopre. Rispondi farebbe il contrario.
- **Gravità**: Bassa (privacy; il Da si vede prima di Invia).
- **Stato**: CONFERMATO.
- **Proposta**: passare il messaggio a `MailtoRequested` e aprire la scrittura con
  `SendIdentity.For(account, entry.Summary, entry.Body)`; lo stesso indirizzo nella conferma.
- **Verifica su Windows**: in `IdentitiesAsync`, un messaggio a un alias con List-Unsubscribe mailto: la scrittura
  parte dall'alias.

### T-12 Clic singolo (RFC 8058) senza controllare che la firma DKIM copra le due intestazioni

- **Dove**: `Core/Unsubscribe.cs:98-100`; `Core/SenderCheck.cs:234-241` (`Authenticated` accetta anche SPF o una firma
  DKIM qualsiasi del dominio).
- **Scenario**:
  - La RFC 8058, sezione 4, vuole una firma DKIM valida che copra List-Unsubscribe e List-Unsubscribe-Post.
  - Con DMARC superato via SPF (un servizio d'invio condiviso nell'include), o con intestazioni aggiunte da un
    intermediario, la POST va a un indirizzo che il dominio non ha firmato.
  - La destinazione resta limitata: https, porta 443, nome DNS, IP pubblici, nessun reindirizzamento.
- **Gravità**: Bassa.
- **Stato**: CONFERMATO che manca il controllo di `h=`; SOSPETTO l'abuso pratico.
- **Proposta**: clic singolo solo con `dkim=pass` e `header.d` allineato, e il `h=` della DKIM-Signature che contiene le
  due intestazioni (lette nella stessa richiesta di oggi). Altrimenti si ripiega su mailto o sulla pagina.

### T-13 Conferma di Annulla iscrizione: l'host in due forme, e non dice che l'indirizzo risulta attivo

- **Dove**: `Core/Unsubscribe.cs:106-119`.
- **Scenario**:
  - `shown` è `Uri.AbsoluteUri`, che tiene l'host in Unicode, mentre `Where` è in punycode. La finestra mostra lo stesso
    sito in due forme diverse, e l'indirizzo lungo può contenere un omoglifo.
  - Un U+202E nel percorso esce codificato, quindi niente inganno bidi.
  - Il testo non dice che POST, pagina e messaggio confermano al mittente che l'indirizzo è attivo, e che la POST ne
    rivela l'IP.
- **Gravità**: Bassa.
- **Stato**: MISURATO (le due forme con `Uri` in .NET 10: `https://bücher.example/u?id=1`); CONFERMATO il testo.
- **Proposta**: `shown` ricostruito con `IdnHost`; una frase "il mittente saprà che questo indirizzo è attivo".

### T-14 Copia di sicurezza: l'identificativo di un account diventa un percorso, fino a una cancellazione ricorsiva

- **Dove**:
  - `MailAccount.Id` (`Core/Models.cs:13`) è un Guid solo quando nasce in Missiva. Arriva dalla copia senza controlli:
    `Backup.Parse` (`Core/Backup.cs:386`), `ApplyPending` e `Merge` (`:542-674`).
  - Lo usano come pezzo di percorso:
    - `MailCache.AccountDir` (`Core/MailCache.cs:22`) e `MailCache.Delete` (`:105-110`, `Directory.Delete(..., true)`);
    - `BodyCache.Folder` e `Delete` (`Core/Offline.cs:159, 578`);
    - `Calendars` (`Core/Calendar.cs:433, 471-473`, `calendario-{Id}.json`);
    - `ContactBook.RemoteFile` (`Core/Contacts.cs:239, 256, 316`).
  - Chiamati da `MainWindow.RemoveAccountAsync` (`UI/MainWindow.xaml.cs:5148-5153`).
- **Scenario**:
  - Un file `.missiva-copia` preparato da un falso "supporto", con la password che l'utente conosce, contiene un account
    IMAP con `"Id": "..\\..\\..\\..\\Documents"`, oppure con un percorso assoluto, che `Path.Combine` mette al posto
    della base.
  - L'account non funziona e l'utente lo rimuove. `Directory.Delete(...\Missiva\cache\..\..\..\..\Documents, true)`
    cancella la cartella Documenti, senza Cestino.
  - Con `"Id": ".."` va via tutta la cartella dei dati di Missiva, compresi i file messi da parte.
  - Prima ancora della rimozione, con un account Graph o Google funzionante, `contatti-{Id}.dat` e
    `calendario-{Id}.json` scrivono file `.dat` o `.json` in posti scelti da chi ha preparato la copia.
- **Gravità**: Alta. La perdita di dati dell'utente è grave e irreversibile. Il modello di minaccia è quello chiesto:
  una copia ostile di cui si conosce la password. Le condizioni (importare, poi rimuovere l'account che non va) sono
  quelle normali di chi segue le istruzioni del "supporto".
- **Stato**: CONFERMATO il percorso del codice. MISURATO su Linux solo `Path.Combine`: `Combine(base, "cache",
  "../../../../Documents")` risale fuori dalla base, e un percorso assoluto prende il posto della base. Su Windows
  `Path.Combine` fa lo stesso con `\`, ma non è stato eseguito.
- **Proposta**:
  1. In `Backup.Parse`, `Damaged` per ogni account con `Id` non conforme a `^[0-9a-f]{32}$`, o duplicato. Lo stesso in
     `Store.Read`, per un impostazioni.json cambiato a mano.
  2. Difesa in più: una funzione unica `Store.AccountPath(id, ...)` che controlla che `Path.GetFullPath` del risultato
     cominci con `Store.Dir\cache\`, usata da MailCache, BodyCache, Calendars e ContactBook.
- **Verifica su Windows**:
  - In `BackupAuditTests`, copie con Id `..\x`, `C:\x` e vuoto: `Backup.Read` dà `Damaged`.
  - `MailCache.Delete` su un account con Id ostile, in una cartella temporanea con una cartella esca accanto: l'esca
    resta.

### T-15 Copia di sicurezza: la password IMAP di questo PC parte verso il server scritto nella copia

- **Dove**: `Core/Backup.cs:651` (`same` si trova solo con Id, tipo e indirizzo) e `:665` (la password di questo PC
  passa all'account in arrivo, con il suo `ImapHost` e `SmtpHost`); per Microsoft `:655`.
- **Scenario**:
  1. L'utente esporta senza password, la scelta prudente di base, e manda la copia e la sua password al "supporto".
  2. Gli torna una copia "sistemata": stessi Id e indirizzo, server IMAP e SMTP del supporto, con un certificato valido.
  3. Al riavvio Missiva rimette la password cifrata di questo PC e la manda subito via TLS a quel server. Non c'è nessuna
     domanda e nessuna barra.
  - Lo stesso con `HomeAccountId` e un `ClientId` o `Tenant` diversi: porta al consenso per l'app di chi ha preparato la
    copia. Che ottenga dei token è SOSPETTO, perché tornano a Missiva.
- **Gravità**: Media. È il furto della password della casella senza altro gesto dopo l'importazione.
- **Stato**: CONFERMATO.
- **Proposta**: riusare `ProtectedPassword` solo se host, porte, sicurezza e nome utente di IMAP e SMTP sono identici;
  altrimenti l'account va in `needAccess`. `HomeAccountId` solo se `Tenant` e `ClientId` sono gli stessi.
- **Verifica su Windows**: in `BackupAuditTests`, importazione sopra un account con password e una copia con un altro
  host: `ProtectedPassword` null e l'account fra quelli che chiedono l'accesso.

### T-16 Copia di sicurezza: un `importazione.dat` costruito apposta chiude Missiva a ogni avvio

- **Dove**:
  - `Core/Backup.cs:563`: `Merge` è fuori dal try di `ApplyPending`.
  - `:665`: `ImapPasswords.TryGetValue(account.Id, ...)` e `Store.Protect(password)`.
  - Le eccezioni risalgono dal costruttore di `App` (`UI/App.cs:40-50`).
  - Collegato: `Backup.Describe` sul thread dell'interfaccia, fuori dal try (`UI/BackupWindows.cs:101`).
- **Scenario**:
  - La copia ha `"ImapPasswords": {"<id di un account IMAP>": null}`, oppure un account IMAP con `"Id": null`.
  - `Read`, `Describe` e `SavePending` passano: di `ImapPasswords` contano solo quanti sono.
  - Al riavvio `Merge` solleva `ArgumentNullException` e Missiva si chiude prima della finestra.
  - `importazione.dat` resta dov'è, quindi ogni avvio dopo fa lo stesso. Dall'interfaccia non si esce: il file va
    cancellato a mano.
  - Con `"Accounts": [null]` è invece `Describe` a dare `NullReferenceException`.
- **Gravità**: Media. Blocco che resta e che l'utente non capisce.
- **Stato**: MISURATO in console: il null nel dizionario si legge, e `Encoding.UTF8.GetBytes(null)` e
  `TryGetValue(null)` sollevano `ArgumentNullException`. CONFERMATO il resto della catena; App non è stata avviata.
- **Proposta**:
  1. In `Backup.Parse` una validazione della struttura:
     - nessun elemento null in Accounts, Rules, Templates, Snoozed, Flags e Signatures;
     - Id e indirizzo presenti;
     - valori di `ImapPasswords` non null, con chiavi uguali all'Id di un account IMAP.
  2. In `ApplyPending`, `Merge` e la serializzazione dentro il try (catturando `Exception`): con un errore
     `SafeFile.SetAside(pending)` ed esito negativo.
- **Verifica su Windows**: `--prova-audit ... copia` con questi contenuti: `Read` dà `Damaged`, e un `importazione.dat`
  scritto a mano dà un esito negativo, con il file messo da parte e nessuna eccezione.

### T-17 Copia di sicurezza: la conferma mostra indirizzi e numeri, e passano regole, mittenti delle immagini e rubrica

- **Dove**: `BackupUi.ConfirmText` (`UI/BackupWindows.cs:117-129`) e `BackupSummary.Contents` (`Core/Backup.cs:198-216`);
  il resto delle impostazioni si applica com'è.
- **Scenario** (copia ostile con password nota):
  - (a) Regole di Missiva per Gmail e IMAP che spostano nel Cestino o segnano lette, per esempio su "password" o
    "accesso", oppure senza condizioni. `Rules.Matches` le fa valere per ogni messaggio nuovo (`Core/Rules.cs:385-404`,
    `ApplyToNewAsync` a `:595-617`); la domanda "vale per tutti" esiste solo in `RuleWindow`. Così si nascondono gli
    avvisi di sicurezza.
  - (b) `ImageSenders` con un dominio di primo livello, per esempio `"com"`: `ShowsImagesFrom` (`Core/Store.cs:125-131`)
    fa `host.EndsWith("." + s)`, quindi le immagini esterne di tutti i mittenti di quel dominio si caricano da sole.
    Dall'interfaccia non si può ottenere (`ImageKey` dà vuoto per un'etichetta sola), dall'importazione sì.
  - (c) `rubrica.json` con conteggi gonfiati: per "Mario Rossi" il primo suggerimento diventa un indirizzo di chi ha
    preparato la copia, e quel nome non accende più la fascia gialla dei sosia.
  - (d) `NotificationsOff` per un account, `AttachmentReminder` spento.
  - (e) Server, `ClientId` e `Tenant` scelti da chi ha preparato la copia (T-15).
- **Gravità**: Media per (a), (b) e (c), che nascondono posta, fanno partire immagini e dirottano destinatari; Bassa
  per il resto.
- **Stato**: CONFERMATO (percorsi letti).
- **Proposta**:
  - Nella conferma: tipo e server di ogni account; un avviso se `ClientId`, `Tenant` o `GoogleClientId` non sono quelli
    di Missiva o di questo PC; le regole in parole.
  - Importare le regole spente, con una riga nell'esito.
  - In `Parse`, scartare da `ImageSenders` le voci per cui `Settings.ImageKey(s) != s` o senza punto.
  - Decidere se la rubrica debba entrare.
- **Verifica su Windows**: in `BackupAuditTests`, una copia con `ImageSenders ["com"]`, una regola senza condizioni che
  elimina e un `ClientId` estraneo: dopo `ApplyPending` niente "com", regola spenta, testo della conferma con i server.

### T-18 Copia di sicurezza: `domini.dat` importato salta la regola dei 14 giorni dei loghi

- **Dove**: `Core/Backup.cs:319, 572` (`KnownDomains` letto e scritto, controllato solo come JSON a `:398-403`);
  `SenderLogos.IsKnown` e `IsAuthenticated` (`Core/SenderLogos.cs:134-147`) decidono da quel file.
- **Scenario**: la copia segna come noto da mesi e `Verified` un dominio sosia di chi l'ha preparata. Al primo messaggio
  da lì, autenticato (facile, il dominio è suo), compare subito l'icona del suo sito. È quello che il blocco 2 voleva
  impedire.
- **Gravità**: Bassa.
- **Stato**: CONFERMATO.
- **Proposta**: `domini.dat` fuori dalla copia (si ricostruisce da solo), o almeno `Verified` azzerato all'importazione.

### T-19 Copia di sicurezza: un file da 1 MB si espande a 1 GB; iterazioni PBKDF2 fino a 20 milioni

- **Dove**: `Backup.Decompress` (`Core/Backup.cs:459-484`: tetto di 1 GB, MemoryStream che raddoppia, poi `ToArray`);
  iterazioni `Core/Backup.cs:57, 114`.
- **Scenario**:
  - Una copia da 995 KB con dentro una stringa JSON da 1000 MB.
  - Un file con 20 milioni di iterazioni costa 13-17 s a ogni tentativo di password.
  - SOSPETTO: chiudendo la finestra durante l'attesa, `DialogResult = true` (`UI/BackupWindows.cs:334`) si imposta su
    una finestra già chiusa.
- **Gravità**: Bassa (serve la password, il lavoro non è sul thread dell'interfaccia).
- **Stato**: MISURATO in console Linux con la stessa logica: 995 KB diventano 1000 MB in 27,4 s, con 3,5 GB chiesti in
  tutto; 600.000 iterazioni in 508 ms, 2 milioni in 1330 ms.
- **Proposta**: tetto di 64 MB sul contenuto decompresso e un rapporto massimo di 50 a 1; iterazioni al più 5 milioni;
  `IsLoaded` controllato prima di `DialogResult`.

### T-20 File messi da parte con i token, mai tolti

- **Dove**: `Core/Backup.cs:557, 598, 618`; `SafeFile.SetAside` (`Core/Store.cs:595-616`).
- **Scenario**: dopo un'importazione che toglie un account, `impostazioni-prima-dell-importazione-*.json` e il suo
  `.bak` tengono ancora token di aggiornamento e password IMAP (cifrati DPAPI). Ogni importazione ne aggiunge altri, e
  Rimuovi account non li tocca.
- **Gravità**: Bassa (riservatezza; resta DPAPI dello stesso utente).
- **Stato**: CONFERMATO.
- **Proposta**: riscrivere il file messo da parte senza `ProtectedRefreshToken`, `HomeAccountId` e `ProtectedPassword`,
  oppure un pulsante "Elimina i dati di prima" nell'esito.

### T-21 Modelli con testi illeggibili: il segnaposto diventa l'oggetto del messaggio

- **Dove**: `MailTemplate.Name`, `Subject` e `Rtf` (`Core/Store.cs:359-361`) non filtrano il segnaposto, mentre
  `Signature.Rtf` sì (`:299`); `UI/ComposeWindow.xaml.cs:665` (`SubjectBox.Text = Templates.Fill(template.Subject, ...)`).
- **Scenario**: impostazioni da un altro utente di Windows. Il modello usato con l'oggetto vuoto mette "(testo cifrato
  non leggibile su questo PC, rif. ...)" come oggetto del messaggio inviato. Il corpo resta vuoto.
- **Gravità**: Bassa (il segnaposto non contiene il cifrato).
- **Stato**: CONFERMATO. Le regole con parti illeggibili invece non valgono mai: vedi "Guardato e trovato a posto".
- **Proposta**: i modelli con parti illeggibili spenti nel pannello Modelli, con la spiegazione; in `UseTemplate` il
  segnaposto vale vuoto.
- **Verifica su Windows**: con `Store.TestEntropy`, un modello cifrato con un'altra entropia: l'oggetto della scrittura
  non cambia.

### T-22 Copia di sicurezza: un valore "dpapi:..." nella copia diventa testo vero

- **Dove**: `ProtectedTextConverter.Read` in modo Portable (`Core/Store.cs:435`) non decifra; `ProtectedListConverter`
  sì (`:515`).
- **Scenario**: una copia con `"MoveTo": "dpapi:..."` porta una regola verso una cartella di nome letterale "dpapi:...".
  È un difetto di coerenza, non un aggiramento.
- **Gravità**: Bassa.
- **Stato**: CONFERMATO.
- **Proposta**: in Portable, `Damaged` per ogni valore che comincia con "dpapi:".

### T-23 Graph: un indirizzo assoluto della risposta riceve il token, e `uploadUrl` non ha controllo dell'host

- **Dove**:
  - `Core/GraphProvider.cs:183`: `url.StartsWith("https://") ? url : Base + url`, poi `Authorization: Bearer`.
  - Indirizzi presi dalle risposte: `@odata.nextLink` (`:432, 495, 531, 577, 1207, 1304, 1746`), `@odata.deltaLink`
    (`:572`).
  - `uploadUrl`: solo lo schema https (`:1773`).
  - Il gestore è `Network.ApiHandler` (`Core/Offline.cs:92-97`), con i reindirizzamenti attivi.
- **Scenario**:
  - Una risposta di Graph manomessa, o un difetto di Graph, manderebbe il token d'accesso a un host qualsiasi (nextLink,
    deltaLink) e i pezzi dell'allegato a un host https qualsiasi.
  - Le PUT non portano il token (`:1792-1795`): questa parte è a posto. Ma una 307 o 308 dell'host di caricamento
    rimanderebbe il pezzo altrove.
- **Gravità**: Bassa (bisogna prima manomettere le risposte TLS di Graph; è difesa in profondità).
- **Stato**: CONFERMATO.
- **Proposta**:
  - In `SendAsync`, un indirizzo assoluto solo con l'host di `Base` (`IdnHost`, https, porta di base).
  - `uploadUrl` solo verso gli host delle sessioni di caricamento di Outlook (uguaglianza, o fine dopo un punto).
  - PUT e DELETE dei pezzi con un `HttpClient` senza reindirizzamenti né cookie.
- **Verifica su Windows**: `UploadGraph` con un `uploadUrl` verso un altro host: errore e nessuna PUT. Un nextLink verso
  un host finto non riceve `Authorization`.

### T-24 Allegati grandi: misura letta prima di aprire il file

- **Dove**: `Core/GraphProvider.cs:1556` (`new FileInfo(path).Length`), apertura a `:1651`, lettura di esattamente
  `size` byte a `:1781-1785`; i file piccoli con `ReadAllBytesAsync` a `:1557`.
- **Scenario**:
  - Un file che cresce fra la misura e l'apertura (un PDF che un altro programma sta ancora scrivendo) parte troncato,
    con un `Content-Range` coerente: nessuno se ne accorge.
  - Un file sotto 3 MB che nel frattempo diventa enorme entra intero in memoria, fuori dal controllo dei 150 MB.
- **Gravità**: Bassa (integrità).
- **Stato**: CONFERMATO.
- **Proposta**: aprire subito con `FileShare.Read` e prendere la misura da `stream.Length`; dopo l'ultimo pezzo
  controllare `ReadByte() == -1`, altrimenti "il file è cambiato".

### T-25 Canale con nome: un altro processo può occuparne il nome e spegnere mailto, "apri" ed "esci"

- **Dove**:
  - `Program.cs:58`: il nome è `Missiva-mailto-<sessione>`, senza l'utente.
  - `UI/App.cs:551-588`: un'istanza sola, rifatta a ogni giro dopo il `Dispose`; `failures` non torna mai a zero e dopo
    5 errori il ciclo finisce.
- **Scenario**:
  - Un altro account dello stesso PC (o un processo dello stesso utente) crea il canale prima di Missiva, o fra un
    `Dispose` e la creazione dopo. Missiva fallisce 5 volte e smette di ascoltare per tutta la sessione.
  - Effetti: i link mailto: si perdono; "apri" non riporta su Missiva dall'area di notifica; "esci" di installa e
    disinstalla non arriva, e dopo 30 s il processo è chiuso a forza, con le scritture aperte perse senza la domanda.
  - I dati non escono: il client usa `CurrentUserOnly`.
  - Lo stesso arresto si ottiene con 5 errori qualsiasi in una sessione lunga.
- **Gravità**: Bassa (solo blocco del servizio).
- **Stato**: CONFERMATO il codice; SOSPETTO il comportamento esatto di `FILE_FLAG_FIRST_PIPE_INSTANCE` in .NET.
- **Proposta**:
  - Un'impronta del SID dell'utente nel nome.
  - Un'istanza sempre in attesa: la successiva creata prima di gestire quella collegata.
  - `failures` azzerato dopo un collegamento riuscito, e attese crescenti invece della resa.
- **Verifica su Windows**: un secondo account locale che crea il canale con quel nome prima dell'avvio: oggi il registro
  mostra 5 errori e i mailto: non arrivano.

### T-26 Azioni delle notifiche: un'attivazione con argomenti scelti agisce su qualunque messaggio della copia locale

- **Dove**: `UI/App.cs:1303-1335` (`OnToast`); `UI/MainWindow.Notifications.cs:20-69` e `:108-117` (`FindNotified` cerca
  anche fuori dalle ultime 50 notifiche, nella copia locale di qualunque cartella); `:239-251`.
- **Scenario**: un processo dello stesso utente attiva il gestore COM delle notifiche con
  `account=<Id>;folder=<qualunque>;message=<id>;action=delete`. Missiva sposta nel Cestino, contrassegna o pospone
  senza chiedere messaggi mai notificati. La risposta dalla notifica invece apre solo la scrittura: quella parte è a
  posto.
- **Gravità**: Bassa (è lo stesso utente; il Cestino si recupera). È il resto di S-21.
- **Stato**: CONFERMATO in Missiva; SOSPETTO che il Toolkit 7.1.3 non controlli chi attiva.
- **Proposta**: un'azione vale solo per una tripla che sta in `notifiedMessages`; altrimenti si apre come il clic.
  Meglio: un nonce casuale negli argomenti di ogni notifica.
- **Verifica su Windows**: in `NotificationActions`, `OnToast` con `action=delete` su un messaggio mai notificato non
  chiama `MoveAsync`.

### T-27 Il comando del link mailto: è `"exe" "%1"`: un link con le virgolette aggiunge opzioni

- **Dove**: `installa.ps1:81`; opzioni lette nella versione installata: `Program.cs:65, 75`, `UI/App.cs:149-150, 164`.
- **Scenario**: un programma che passa a ShellExecute un mailto: con `"` non codificate
  (`mailto:a@b.example" --nascosta "x`) avvia Missiva nascosta, o con `--riavvio` la fa aspettare 30 s. Non esegue
  codice: "il link non ha fatto nulla".
- **Gravità**: Bassa.
- **Stato**: SOSPETTO: da provare se Windows e i browser codificano `"` nei mailto:.
- **Proposta**: comando `"exe" --mailto "%1"`; con `--mailto` si ignora ogni altra opzione.

### T-28 La versione finale con gli strumenti di prova perde PROVA: avviata a mano lavora sui dati veri accanto a quella installata

- **Dove**: `strumenti/prova.ps1:27-28` (`-p:MissivaProva=true @extra`), `Missiva.csproj:44-46`
  (`$(DefineConstants);PROVA`), `Program.cs:28-40` (l'uscita "senza MISSIVA_DATA" con `#if PROVA`).
- **Scenario**:
  - CLAUDE.md dice di compilare la versione finale con gli strumenti con
    `-Publish '-p:DefineConstants=DEBUG%3BTRACE'`. Una proprietà data a MSBuild dalla riga di comando prevale sulle
    assegnazioni del progetto, quindi `PROVA` non si aggiunge.
  - L'eseguibile in `prova-release` è DEBUG senza PROVA. Con un doppio clic, senza MISSIVA_DATA:
    - parte sui dati veri;
    - ha il mutex della prova, quindi gira accanto alla Missiva installata (si sovrascrivono le impostazioni);
    - senza MISSIVA_NO_READ fa scritture vere sulla casella;
    - ha attivi i ganci DEBUG.
  - Esce solo se avviato da una notifica.
- **Gravità**: Media. Tocca la regola "mai azioni che modificano la casella vera" e i dati veri, con un gesto comune
  (doppio clic su un eseguibile in una cartella nota). Riguarda il PC di sviluppo, non chi installa Missiva.
- **Stato**: MISURATO con MSBuild su Linux, su un progetto con la stessa struttura: con `-p:DefineConstants=DEBUG%3BTRACE`
  le costanti finali sono solo `DEBUG;TRACE`. Missiva non è stata compilata.
- **Proposta**: in `prova.ps1`, se `-Publish` contiene `DefineConstants`, aggiungere `%3BPROVA`; oppure aggiungere
  `PROVA` in un `Target` prima di `CoreCompile`. Difesa in più in `Program.cs`: senza MISSIVA_DATA uscire anche con
  `#if DEBUG` quando l'eseguibile non è sotto `bin\Debug`.
- **Verifica su Windows**: compilare come dice CLAUDE.md e avviare `prova-release\Missiva.exe` senza MISSIVA_DATA:
  oggi parte, dopo deve chiudersi.

### T-29 Ganci rimasti nella versione installata

- **Dove**: `UI/MainWindow.xaml.cs:43` (`MISSIVA_RIGA_VECCHIA`), `UI/ScrollHitGuard.cs:43` (`MISSIVA_SENZA_SOSPENSIONE`),
  `UI/StallLog.cs:49` (`MISSIVA_SENZA_STALL`); runtime .NET: `DOTNET_STARTUP_HOOKS`, perché `Missiva.csproj` non ha
  `StartupHookSupport=false`.
- **Scenario**: le tre variabili cambiano solo aspetto e diagnostica. `DOTNET_STARTUP_HOOKS` in una variabile utente
  carica codice in Missiva, ma serve lo stesso utente, che ha già altri modi.
- **Gravità**: Bassa.
- **Stato**: CONFERMATO (tabella sotto).
- **Proposta**: togliere `MISSIVA_RIGA_VECCHIA` come previsto; `<StartupHookSupport>false</StartupHookSupport>`.

### T-30 `disinstalla.ps1` con Windows PowerShell 5.1: `Remove-Item -Recurse` e le giunzioni; `LOCALAPPDATA` vuota

- **Dove**: `disinstalla.ps1:53` (`Remove-Item $dir -Recurse -Force`), `:8` (percorso da `$env:LOCALAPPDATA`);
  `installa.ps1:34`.
- **Scenario**:
  - Con 5.1 una giunzione nella cartella dei programmi verso un'altra cartella dell'utente potrebbe essere seguita, e
    quella cartella svuotata.
  - Con `LOCALAPPDATA` vuota il percorso diventa relativo alla cartella corrente.
- **Gravità**: Bassa (stesso utente, nessun confine di privilegi).
- **Stato**: SOSPETTO: il comportamento di 5.1 sulle giunzioni va provato.
- **Proposta**: cancellare per nome i file noti, poi la cartella vuota senza ricorsione; fermarsi se `$dir` non è
  assoluto.

### Ganci di prova (punto 10)

Variabili d'ambiente:

| Nome | Dove | Condizione | Nell'installata | Effetto |
|---|---|---|---|---|
| MISSIVA_DATA | `Core/Store.cs:651`, `Program.cs:33` | DEBUG o PROVA | no | sposta la cartella dei dati |
| MISSIVA_NO_READ | `UI/MainWindow.xaml.cs:3264` e altre | DEBUG o PROVA | no | scritture solo nel registro |
| MISSIVA_SENZA_RETE | `UI/App.cs:57` | DEBUG o PROVA | no | finge l'assenza di rete |
| MISSIVA_ASSEMBLY | `Program.cs:51` | DEBUG o PROVA | no | assembly nel registro |
| MISSIVA_REGISTRO_SUBITO | `Core/Store.cs:1097` | DEBUG o PROVA | no | righe "tempi" subito |
| MISSIVA_KEYS, MISSIVA_RECORD, MISSIVA_VARIANTI | `Core/DevTools.cs:161-166` | DEBUG | no | comandi da file |
| MISSIVA_DUMP | `Core/DevTools.cs:1038, 1046` | DEBUG | no | salva l'HTML |
| MISSIVA_IMAP_FINTO | `Core/ImapProvider.cs:95` | DEBUG | no | IMAP in chiaro verso 127.0.0.1 |
| MISSIVA_INGRESSO | `Core/InputProbe.cs:116` | DEBUG | no | misure dell'ingresso |
| MISSIVA_SENZA_AZIONI, _HOVER, _TOOLTIP, _MOUSEMOVE, _LISTA_INERTE, _RIMEDIO_HOVER | `Core/CursorProbe.cs:150, 174-175` | DEBUG | no | sonde |
| MISSIVA_ERRORE_INVIO, MISSIVA_CARICAMENTO_FINTO | `UI/ComposeWindow.xaml.cs:1675, 1678` | DEBUG | no | errori finti |
| MISSIVA_ERRORE_WEBVIEW | `UI/ReadingView.xaml.cs:1082` | DEBUG | no | errore finto |
| MISSIVA_COPIA_FILE | `UI/BackupWindows.cs:38` | DEBUG | no | file della copia senza finestra |
| MISSIVA_MISURA_CORPI, MISSIVA_PROVA_TOKEN | `Core/ProvaBozze.Audit.cs:6062, 5016` | DEBUG | no | prove |
| MISSIVA_RIGA_VECCHIA | `UI/MainWindow.xaml.cs:43` | sempre | **sì** | modello di riga di prima (T-29) |
| MISSIVA_SENZA_SOSPENSIONE | `UI/ScrollHitGuard.cs:43` | sempre | **sì** | spegne la sospensione del hit test |
| MISSIVA_SENZA_STALL | `UI/StallLog.cs:49` | sempre | **sì** | spegne il registro dei blocchi |

Argomenti:

| Argomento | Dove | Condizione | Nell'installata | Effetto |
|---|---|---|---|---|
| `--pdf-worker` | `Program.cs:20`, `Core/PdfWorker.cs:414` | sempre | sì, solo come unico argomento | processo dei PDF |
| `--riavvio` | `Program.cs:65`, `UI/App.cs:331` | sempre | sì | aspetta il mutex fino a 30 s |
| `--nascosta` | `Program.cs:75`, `UI/App.cs:150, 164` | sempre | sì | avvio nell'area di notifica (T-27) |
| `-ToastActivated` | `UI/App.cs:149` (e `Program.cs:38`, DEBUG o PROVA) | sempre | sì | avvio nascosto per una notifica |
| `mailto:...` | `Program.cs:72`, `UI/App.cs:210` | sempre | sì | Nuovo messaggio, mai inviato |
| `--senza-rete`, `--errore-rete`, `--prova-anteprima` | `UI/App.cs:57, 59`, `Program.cs:43` | DEBUG o PROVA | no | prove |
| circa 80 altri (`--page`, `--imap-finto`, `--prova-bozze`, `--prova-audit`, `--toast`, `--delete`, `--pec-radice`, ...) | DevTools, MainWindow, App | DEBUG | no | prove |

Campi e metodi statici:
- Dentro `#if DEBUG`: `UseTestHandler` (GraphProvider, GoogleApi, GoogleCalendar, MailAutoconfig, SenderLogos,
  Unsubscribe), `TestPort`, `TestSmtpPort`, `TestPlain`, `TestCertificate`, `TestRoots`, `TestEntropy`, `TestToken`,
  `TestHttp`, `TestTxt`, `TestAnswers`, `TestMarkRoot`, `TestCalendarConsent`, `TestProbe`, `TestFailJob`, `TestRoot`,
  `TestBook` e gli altri.
- Compilati sempre ma senza effetto nell'installata:
  - `PdfWorker.TestProcess`, `TestInJob`, `TestInAppContainer`, `MainWindow.TestAccounts` (sola lettura);
  - `BackupWindows.TestFile` (null);
  - `Network.TestOffline` (`Core/Offline.cs:68`: lo assegna solo codice DEBUG o PROVA, e `Check()` è vuoto).
- Attenzione: con il caso di T-28 tutto ciò che è DEBUG c'è anche nella "versione finale di prova".

## Parte C: mai guardato

### T-31 `HtmlToFlow`: il `font-family` di una bozza ripresa arriva a `new FontFamily` senza filtro

- **Dove**: `Core/HtmlToFlow.cs:229-230` (`family = first`, il primo nome con le sole virgolette tolte) e `:191`
  (`run.FontFamily = new FontFamily(family)`).
- **Scenario**:
  - Una bozza ripresa con Modifica (la citazione dell'email originale, una bozza sincronizzata da un altro client), o
    una risposta automatica impostata da altri (`UI/AutoRepliesWindow.cs:317`), contiene
    `style="font-family:'file://host.example/share/#Nome'"` o un percorso UNC.
  - In WPF `FontFamily` accetta la forma "indirizzo#famiglia". Se WPF risolve quell'indirizzo quando disegna l'editor,
    parte una connessione SMB (con l'hash NTLM) o un caricamento remoto del carattere, alla sola apertura in Modifica.
  - In uscita `FlowToHtml.CssFamily` ripulisce, ma è troppo tardi.
- **Gravità**: Media se confermato (fuga dell'hash NTLM senza clic); Bassa altrimenti.
- **Stato**: CONFERMATO il percorso (nome non filtrato fino a `new FontFamily`). SOSPETTO l'effetto: va provato su
  Windows come WPF risolve un nome con `#` e uno schema.
- **Proposta**: filtrare `family` come `CssFamily` (solo lettere, cifre, spazio e trattino; niente `#`, `/`, `\`, `:`)
  prima di `new FontFamily`, con un tetto alla lunghezza.
- **Verifica su Windows**: in Debug, Modifica su una bozza con quel `font-family` verso una condivisione di prova, con
  Procmon o un ascoltatore SMB: nessuna connessione. In `--prova-bozze`, `HtmlToFlow.Blocks` su quell'HTML dà Run con
  `FontFamily.Source` senza `#`.

### T-32 Incolla nell'editor: RTF e XAML degli appunti lasciati a WPF

- **Dove**: `UI/ComposeWindow.xaml.cs:1287-1311` (`OnEditorPasting`): gestisce solo file e immagini; per testo, RTF e
  XAML lascia fare al RichTextBox.
- **Scenario**: appunti preparati da un'altra pagina o programma con RTF che contiene
  `{\field{\*\fldinst INCLUDEPICTURE "http://host.example/x.png"}}`, `\object`, oppure `XamlPackage` con un'immagine
  da un percorso remoto. Non si sa se WPF scarichi qualcosa.
- **Gravità**: Bassa (Media se un caricamento remoto si conferma).
- **Stato**: SOSPETTO: serve la prova su Windows.
- **Proposta**: se si conferma, togliere `Xaml` e `XamlPackage` dagli appunti in `OnEditorPasting`
  (`e.DataObject` rifatto con i soli formati accettati).
- **Verifica su Windows**: uno script STA che mette quei formati negli appunti, poi Ctrl+V nell'editor con un
  ascoltatore: nessuna richiesta.

### T-33 Inviti: il link della riunione riconosciuto con `EndsWith` senza il punto

- **Dove**: `Core/Invitation.cs:330, 333, 493` (`MeetingHosts.Any(h => HostOf(url).EndsWith(h))`); al contrario
  `UI/CalendarView.cs:985` confronta bene (`host == h || host.EndsWith("." + h)`).
- **Scenario**:
  - Un .ics, o il corpo di un eventMessage (`Core/GraphProvider.cs:1437`), con l'indirizzo della riunione su un host
    che finisce con il nome di un servizio noto ma è di un altro (`mal<servizio>`).
  - La scheda dell'invito lo mostra come "Partecipa alla riunione online (<host>)" (`UI/ReadingPage.cs:360-367`) e lo
    registra in `LinkTexts`, quindi il controllo dei link ingannevoli non dice nulla.
  - L'host vero è scritto nel testo: questo limita l'inganno.
- **Gravità**: Bassa.
- **Stato**: MISURATO (il confronto, copiato in console: un host con un prefisso attaccato al nome del servizio dà vero).
  CONFERMATO il percorso della scheda.
- **Proposta**: lo stesso confronto di `CalendarView.cs:985` nelle tre righe.
- **Verifica su Windows**: `--scrivi-inviti` con un .ics con quell'host e `--eml`: niente riga "Online".

### T-34 Regole di Outlook che inoltrano fuori: segnalate solo in Impostazioni

- **Dove**: `Core/Rules.cs:199-207` (`ExternalForwards`), `UI/RulesView.cs:252`.
- **Scenario**: chi ha preso la casella aggiunge da un altro client una regola di inoltro o reindirizzamento verso
  fuori. Missiva la mostra con il bordo rosso, ma solo se si apre Impostazioni, Posta, Regole. Non c'è nessun avviso
  quando una regola così compare.
- **Gravità**: Bassa (rilevazione, non prevenzione).
- **Stato**: CONFERMATO.
  - Il confronto dei domini è a posto: `SenderCheck.SameOrganization` (`Core/SenderCheck.cs:466-467`) usa il punto, e
    un dominio che contiene quello dell'account come prefisso risulta esterno (MISURATO).
  - Il controllo copre i tre tipi di inoltro (`Core/Rules.cs:96`).
- **Proposta**: dopo `GetRulesAsync` riuscito, una notifica la prima volta che compare una regola di `ExternalForwards`
  non vista (impronta delle regole in impostazioni, cifrata).

### T-35 Sito dei contatti verso la rete interna

- **Dove**: `Core/ContactLinks.cs:68` (`WebUrl`).
- **Scenario**: il campo sito di un contatto arrivato da Google, da Outlook o da una vCard importata, con
  `http://10.0.0.1/...` o `https://127.0.0.1/`, apre il browser lì al clic su "Apri il sito".
- **Gravità**: Bassa (serve il clic; nessuno schema eseguibile passa).
- **Stato**: MISURATO (funzione copiata: indirizzi privati e di loopback accettati; credenziali, schemi diversi e
  `[::1]` rifiutati).
- **Proposta**: in `WebUrl` rifiutare host che sono IP letterali non pubblici (lo stesso `IsPublic` dei loghi).

### T-36 Registro: a capo dei testi di terzi non tolti, e nomi delle cartelle in chiaro

- **Dove**: `Core/Store.cs:1161-1163` (`Log.Write` e `Append` scrivono com'è); `Log.Private` (`:1048`) aggiunge solo le
  virgolette; `Core/ImapProvider.cs:777` e `:1324` scrivono `f.FullName` senza `Log.Private`.
- **Scenario**:
  - Un server IMAP con una cartella il cui nome contiene CR e LF, o un messaggio d'errore del server che li contiene,
    aggiunge righe false a un registro che "si manda a chi aiuta".
  - I nomi delle cartelle restano in chiaro nella versione installata: ESITO ne segnala già due di Gmail; questi sono
    altri due.
- **Gravità**: Bassa.
- **Stato**: CONFERMATO il codice; SOSPETTO che un server vero ammetta CR e LF nei nomi.
- **Proposta**: in `Append`, CR, LF e gli altri caratteri di controllo diventano uno spazio; i nomi delle cartelle con
  `Log.Private`.
- **Verifica su Windows**: `--imap-finto` con una cartella con `%0d%0a` nel nome e una ricerca che fallisce: nessuna
  riga spezzata.

## Tabella riassuntiva (per gravità)

| Sigla | Gravità | Stato | In breve |
|---|---|---|---|
| T-14 | Alta | CONFERMATO | Id di un account dalla copia usato come percorso: cancellazione ricorsiva fuori dalla cartella dei dati |
| T-01 | Media | MISURATO | `\\host`, `http:host`, tabulazioni: immagini esterne non bloccate dalla pulizia, partono con un mittente consentito accanto |
| T-02 | Media | CONFERMATO | Immagini automatiche anche con fascia rossa, negli .eml e nelle PEC false |
| T-03 | Media | CONFERMATO | IMAP: Authentication-Results del mittente credute, logo per sempre |
| T-15 | Media | CONFERMATO | Copia: la password IMAP di questo PC va al server scritto nella copia |
| T-16 | Media | MISURATO (in parte) | Copia: `importazione.dat` costruito chiude Missiva a ogni avvio |
| T-17 | Media | CONFERMATO | Copia: regole senza condizioni, `ImageSenders ["com"]`, rubrica truccata passano senza essere viste |
| T-28 | Media | MISURATO (MSBuild) | Versione finale di prova senza PROVA: con un doppio clic lavora sui dati veri |
| T-31 | Media se confermato | SOSPETTO | `font-family` di una bozza a `new FontFamily` senza filtro (UNC) |
| T-04 | Bassa | CONFERMATO | Gmail: authserv-id cercato in tutte le intestazioni |
| T-05 | Bassa | CONFERMATO | Microsoft: `EndsWith` senza punto, authserv-id assente accettato |
| T-06 | Bassa | CONFERMATO | Allineamento a due etichette |
| T-07 | Bassa | SOSPETTO | Thread dei loghi non fermato |
| T-08 | Bassa | CONFERMATO | Posta indesiderata non riconosciuta |
| T-09 | Bassa | MISURATO | Nomi riservati incompleti |
| T-10 | Bassa | MISURATO | HTML e SVG allegati senza domanda |
| T-11 | Bassa | CONFERMATO | Annulla iscrizione dall'indirizzo principale |
| T-12 | Bassa | CONFERMATO | Clic singolo senza DKIM sulle due intestazioni |
| T-13 | Bassa | MISURATO | Conferma: host in due forme |
| T-18 | Bassa | CONFERMATO | Copia: `domini.dat` salta i 14 giorni |
| T-19 | Bassa | MISURATO | Copia: 1 MB che diventa 1 GB, 20 milioni di iterazioni |
| T-20 | Bassa | CONFERMATO | File messi da parte con i token |
| T-21 | Bassa | CONFERMATO | Segnaposto dei modelli come oggetto |
| T-22 | Bassa | CONFERMATO | "dpapi:" nella copia come testo |
| T-23 | Bassa | CONFERMATO | Graph: host di nextLink, deltaLink, uploadUrl non controllato |
| T-24 | Bassa | CONFERMATO | Allegati grandi: misura prima dell'apertura |
| T-25 | Bassa | CONFERMATO | Canale con nome occupabile, ascolto che si arrende |
| T-26 | Bassa | CONFERMATO | Azioni delle notifiche su messaggi mai notificati |
| T-27 | Bassa | SOSPETTO | `"%1"` del mailto: |
| T-29 | Bassa | CONFERMATO | Ganci rimasti, startup hook |
| T-30 | Bassa | SOSPETTO | disinstalla.ps1 con 5.1 e giunzioni |
| T-32 | Bassa | SOSPETTO | Incolla RTF e XAML |
| T-33 | Bassa | MISURATO | Inviti: `EndsWith` senza punto |
| T-34 | Bassa | CONFERMATO | Inoltri verso fuori non annunciati |
| T-35 | Bassa | MISURATO | Sito dei contatti verso IP interni |
| T-36 | Bassa | CONFERMATO | Registro: CR e LF, nomi delle cartelle |

## Guardato e trovato a posto

- **mXSS fra AngleSharp e Chromium** (MISURATO, 10 pagine in Chromium headless con JavaScript spento): nessuna uscita
  dalla shadow root, nessuna immagine o richiesta nuova. Casi provati:
  - `xmp`, `noembed`, `noframes` e `iframe` dentro `svg`;
  - `style` in `svg` e in `math` con `mglyph`;
  - `title` e `desc`, CDATA e commenti in `svg`;
  - `annotation-xml` e `foreignObject`;
  - i casi noti di DOMPurify con `</p>`, `textarea` e `style`;
  - commenti con `--!>` e `<!--`.

  Il serializzatore di AngleSharp 1.8.2 codifica il testo dei nodi stranieri, e i `style` vanno nel foglio raccolto con
  `</` riscritto (`Core/MailHtml.cs:219`).
- `url()` nel CSS: ciò che non è `data:`, `cid:` o della pagina diventa `none` anche se `IsRemote` non lo riconosce
  (`Core/MailHtml.cs:858-868`, MISURATO con `http:\\` e con un escape).
- Il messaggio come solo testo (`PlainOnly`, `RoughText`, `FromText`): testo codificato prima dei link; il testo del link
  è l'indirizzo; il risultato ripassa da `Clean` e `LinkTarget` (`Core/MailHtml.cs:55-74, 647-652, 810`).
- CSP e intercettazione: `UI/ReadingPage.cs:567`; `UI/ReadingView.xaml.cs:1257-1281` (fuori dalla pagina solo immagini
  http e https con `RemoteAllowed`), `:1287-1311` (navigazioni), `:1108` (frame).
- `LogoSvg`:
  - niente DTD né risolutore (`Core/LogoSvg.cs:99`);
  - gzip e UTF-16 rifiutati (`:75-77`);
  - `href` solo `#id` (`:139-146`);
  - `xml:base` tolto (`:147`);
  - elementi fuori elenco tolti con il contenuto (`:28, 124-129`);
  - `image`, `script` e `foreignObject` rifiutati (`:32`);
  - `url()` solo interni (`:285-297`);
  - stili e fogli riscritti senza `@` (`:303-387`);
  - a SharpVectors arriva il testo riscritto (`Core/SenderLogos.cs:686-688`).
- Logo BIMI:
  - gzip con tetto e stessa lista bianca (`Core/SenderLogos.cs:438, 484-511`);
  - PEM al più 64 KB (`:430`);
  - catena, uso e nome DNS (`:449-480`). La revoca manca, per scelta già scritta.
- Rete dei loghi e del clic singolo:
  - `IsPublic` (`Core/SenderLogos.cs:632-645`, con NAT64, 6to4, Teredo e gli spazi privati);
  - `ConnectCallback` sull'IP già controllato, niente DNS rebinding (`:599-608`);
  - niente proxy (`:597`);
  - reindirizzamenti limitati, e spenti per la POST (`Core/Unsubscribe.cs:136-141`).
- Nomi dei file dei loghi: solo lettere, cifre, `.` e `-` (`Core/SenderLogos.cs:47, 84-86`).
- `SenderCheck.Parse` e `Authenticated`: il peggiore fra due risultati, `Malformed` che blocca (`Core/SenderCheck.cs:52-87,
  234-241`).
- `Unsubscribe.Links` e il mailto: (MISURATO):
  - solo https sulla porta di base;
  - IP letterali e credenziali rifiutati;
  - un solo destinatario ASCII, niente Cc, Ccn e allegati;
  - CRLF tolti dall'oggetto;
  - parte solo con Invia.
- Annulla iscrizione nascosto nella Posta indesiderata, con la fascia rossa, nelle bozze e nelle PEC rosse
  (`Core/Unsubscribe.cs:93-96`). Le intestazioni chieste dopo usano EXAMINE e BODY.PEEK (`Core/ImapProvider.cs:1060-1064`).
  La riga è aggiunta con `JsonSerializer` sopra testo già codificato (`UI/ReadingPage.cs:261-273`).
- `CleanAttachmentName` (MISURATO): bidi, invisibili, NUL e tabulazione tolti; `:` dei flussi tolti; punti e spazi
  finali tolti; coda tenuta oltre 150 caratteri.
- Marchio web su tutti i percorsi di salvataggio e apertura:
  - Apri e Salva (`UI/ReadingView.xaml.cs:1742-1750`);
  - Salva tutti (`:1801-1802`);
  - anteprima (`:1629-1630`);
  - p7m (`UI/P7mWindow.cs:117-149`);
  - .eml (`UI/MainWindow.xaml.cs:4453`);
  - PDF (`UI/ReadingView.xaml.cs:592`).

  Nessun trascinamento di file fuori dalla finestra.
- Posta indesiderata nelle conversazioni: in Graph è decisa messaggio per messaggio con `parentFolderId` ed è espanso
  solo il messaggio scelto; le conversazioni di Gmail vengono da "Tutti i messaggi", senza Spam; IMAP non ha
  conversazioni.
- Formato della copia (`Core/Backup.cs:64-161`):
  - due nonce diversi;
  - sigillo sui byte dell'intestazione;
  - lunghezza controllata prima di allocare;
  - chiave e password azzerate.
- TLS di IMAP e SMTP sempre attivo anche con i valori di una copia (`Core/ImapProvider.cs:100-107`).
- Regole con parti illeggibili: `MailRule.Unreadable` (`Core/Rules.cs:89-91`) copre nome, cartelle e liste; `Matches` è
  l'unica strada, sia per i nuovi messaggi sia per Esegui ora (`:375-404, 481, 595-617`). Le regole di Outlook non
  hanno campi cifrati.
- Firme illeggibili vuote (`Core/Store.cs:299, 339`); `ImageSenders` illeggibili mai uguali a un dominio (`:125-131`).
- `SafeFile.Write`, `Store.Read` con `.bak` e `SetAside`, `SettingsSaver` con istantanee numerate (`Core/Store.cs:549-585,
  676-700, 892-1010`): nessuna corsa trovata fra `Save` e `SaveSoon`.
- `ApplyPending`: scrive accanto, poi sostituisce, le impostazioni per prime, il `.bak` messo da parte; ripetibile
  (`Core/Backup.cs:542-622`), salvo T-16.
- Caricamento a pezzi: PUT senza `Authorization` (`Core/GraphProvider.cs:1792-1795`), `Content-Range` giusto, 201
  obbligatorio, DELETE della sessione abbandonata, pulizia degli orfani (`:1702-1722`).
- Canale con nome dopo S-22: 64 KB in 3 s (`UI/App.cs:525-549`), `CurrentUserOnly` ai due capi, primo piano al solo PID
  all'altro capo (`Program.cs:99`), tre comandi soltanto.
- AppContainer dopo S-13: ambiente in lista bianca senza `DOTNET_STARTUP_HOOKS` (`Core/Sandbox.cs:115-135`), elenco dei
  soli due tubi, job obbligatorio con i limiti (`Core/PdfWorker.cs:421-432, 569-595`), risposte del processo controllate
  (`:257-265, 324-331, 351-358`).
- `HtmlToFlow`:
  - immagini solo da `cid:` di Missiva, con i decodificatori per tipo (`Core/HtmlToFlow.cs:374-387`);
  - link solo http, https e mailto (`:297`), non attivi nell'editor;
  - tetti su colspan, rowspan, rientro, dimensioni e bordi (`:231, 241, 332-333, 358`).
- `LinkWindow` solo http, https e mailto (`UI/LinkWindow.cs:69-76`).
- Calendario:
  - "Partecipa" con il confronto giusto e solo https (`UI/CalendarView.cs:975-993`, MISURATO);
  - testi degli eventi in TextBlock;
  - colori da esadecimali controllati (`Core/Calendar.cs:176`, `Core/GoogleCalendar.cs:127`).
- `ContactLinks` (MISURATO):
  - numeri solo cifre ASCII;
  - id di Google con `\z`;
  - `authuser` codificato;
  - `OpenExternal` apre solo ciò che `IsAllowed` accetta (`UI/PeopleView.cs:216-219`).
- `ReadingPage`: ogni testo di terzi passa da `E()` (`WebUtility.HtmlEncode`), nel testo e negli attributi, comprese
  fasce PEC, sosia, "si presenta come", account, inviti e allegati; negli script `JsonSerializer`; nei fogli di stile
  solo colori interni.

## Non guardato

- Tutto ciò che vuole Windows: WebView2 vero (differenze con Chromium 1194), `FontFamily` di WPF (T-31), l'incolla di
  RTF e XAML (T-32), i nomi riservati su Windows 11 (T-09), Windows PowerShell 5.1 (T-30), `ControlledExecution` (T-07),
  il gestore COM delle notifiche (T-26), i canali con nome fra due utenti (T-25).
- Server veri: quali server IMAP tolgono le Authentication-Results in arrivo (T-03), l'ordine di `internetMessageHeaders`
  in Graph, gli host delle sessioni di caricamento di Outlook (T-23).
- Codice di MimeKit, MailKit, SharpVectors e del Toolkit delle notifiche.
- `dotnet list package --vulnerable` (serve il ripristino dei pacchetti da qui).
- Missiva intera non compilata qui: le prove usano copie delle funzioni; `MailHtml.cs` è l'unico file compilato com'è,
  con un sostituto di `Color` che non conta per la pulizia (conta solo per il tema scuro, non provato).
- Le parti di Gmail e IMAP che non riguardano la sicurezza delle email ricevute (bozze, IDLE, coda senza rete) e le
  finestre delle impostazioni.
