# Audit del codice: pulizia, semplificazione, robustezza

- Commit di main letto: `2084922f87e64d08421bc949788b285ee08582b2` (8 ottobre 2026).
- Sessione online del 11 ottobre 2026, su Linux, in sola lettura. Missiva non è stata eseguita e la suite `--prova-bozze` non è girata.
  Nessun punto di questo rapporto è "provato": ogni punto dice se è MISURATO (con che cosa, qui su Linux), CONFERMATO (letto nel
  codice, con file e riga) o SOSPETTO (e che cosa manca per confermarlo).
- Le righe citate sono quelle del commit qui sopra. Dopo una divisione dei file (C-19 e seguenti) non valgono più.
- Gli strumenti usati (script Python per metodi, doppioni e membri mai usati, la copia per gli analizzatori) sono rimasti fuori dal
  repository.

## Indice

- [Misure](#misure)
- [1. Codice morto e residui](#1-codice-morto-e-residui) (C-01 ... C-07)
- [2. Duplicazioni](#2-duplicazioni) (C-08 ... C-18)
- [3. File e metodi troppo grandi](#3-file-e-metodi-troppo-grandi) (C-19 ... C-23)
- [4. Robustezza](#4-robustezza) (C-24 ... C-36)
- [5. Avvisi del compilatore](#5-avvisi-del-compilatore) (C-37)
- [6. Numeri e soglie](#6-numeri-e-soglie) (C-38, C-39)
- [7. Le prove](#7-le-prove) (C-40 ... C-49)
- [8. Difetti di comportamento](#8-difetti-di-comportamento) (C-50 ... C-63)
- [9. Semplificazioni](#9-semplificazioni) (C-64 ... C-66)
- [10. Documentazione](#10-documentazione) (C-67 ... C-71)
- [I venti interventi con il miglior rapporto fra beneficio e rischio](#i-venti-interventi-con-il-miglior-rapporto-fra-beneficio-e-rischio)
- [Guardato e trovato a posto](#guardato-e-trovato-a-posto)
- [Non guardato](#non-guardato)

---

## Misure

### Compilazione su Linux

.NET SDK 10.0.112 dai pacchetti di Ubuntu 24.04 (`apt-get install dotnet-sdk-10.0`), su una copia del commit fuori dal repository.

- `dotnet build -c Debug`: riesce, 0 errori, 11 avvisi diversi (24 righe con i doppioni del riepilogo). MISURATO.
- `dotnet build -c Release`: riesce, 0 errori, 9 avvisi diversi, fra cui uno che in Debug non c'è: `UI/RulesView.cs(101)` CS0649,
  `RulesView.StartAccount` mai assegnato (lo assegnano solo le righe `#if DEBUG` di `MainWindow.xaml.cs:266` e `273`). MISURATO.
- Che la compilazione riesca su Linux non dice nulla del comportamento (CLAUDE.md, "Sessioni online").

Avvisi della compilazione normale (Debug), uno per riga; la classificazione è in [C-37](#c-37-gli-avvisi-del-compilatore-uno-per-uno).

| Avviso | Dove |
|---|---|
| CS8602 | Core/GmailProvider.cs:300, 325, 1338; Core/ImapProvider.cs:1549; Core/ProvaBozze.cs:4654, 4717 |
| CS8604 | Core/GmailProvider.cs:332; UI/MainWindow.xaml.cs:1086, 2128 |
| CS0252 | Core/ProvaBozze.ContattiGoogle.cs:431 |
| CA2200 | UI/ReadingView.xaml.cs:693 |
| CS0649 (solo Release) | UI/RulesView.cs:101 |

### Analizzatori di .NET su una copia

Sulla stessa copia, con un `.editorconfig` aggiunto solo lì e `-p:EnforceCodeStyleInBuild=true -p:AnalysisLevel=latest`, regole
accese come avvisi: IDE0051, IDE0052 (membri privati mai usati o mai letti), IDE0059 (assegnazioni inutili), IDE0060 (parametri
inutili), IDE0035 e CS0162 (codice irraggiungibile), CA2000, CA2213, CA1001 (IDisposable non rilasciati), CA2016 (token non
passati), CA1849 (chiamate bloccanti in metodi async), CA2008, CA1806, CA1068, CA2200. MISURATO.

311 avvisi diversi: 207 nel codice di prova (`Core/ProvaBozze*.cs`), 104 nel resto.

| Regola | Totale | Fuori dalle prove | File con più avvisi (fuori dalle prove) |
|---|---|---|---|
| CA2000, oggetto IDisposable non rilasciato | 140 | 18 | GoogleAuth 3, SenderLogos 2, Sandbox 2, PdfWorker 2, GmailProvider 2, ImapProvider 2 |
| CA1849, chiamata bloccante in un metodo async | 102 | 29 | ImapProvider 6, GmailProvider 5, SenderLogos 4, ReadingView 3 |
| CA2016, CancellationToken non passato | 14 | 14 | GmailProvider 4, PdfWorker 3, ImapProvider 3, ReadingView 2 |
| CA2213, campo IDisposable non rilasciato | 11 | 6 | PdfWorker 3, ImapProvider 2, GmailProvider 1 |
| CA1001, tipo con campi IDisposable non IDisposable | 9 | 8 | MainWindow, ComposeWindow, ReadingView, CalendarView, App, AccountWindow, ManageAccountWindow, MailboxGate |
| IDE0060, parametro inutile | 6 | 6 | vedi C-07 |
| IDE0059, assegnazione inutile | 4 | 4 | vedi C-07 |
| CA2008, Task senza TaskScheduler | 5 | 5 | vedi C-24 |
| CA1806, risultato ignorato | 3 | 3 | PdfWorker:768, Sandbox:242, TrayIcon:39 |
| CA1068, CancellationToken non ultimo | 3 | 3 | GraphProvider:177, Rules:452, ReadingView:794 |
| IDE0051, IDE0052, CS0162 | 0 | 0 | |

Lettura: IDE0051 e IDE0052 non trovano membri privati mai usati; i membri mai usati sono `internal` o `public` e li ho cercati a parte
(C-05). Quasi tutti i CA2000 e CA1849 sono innocui (MemoryStream, `CancellationTokenSource.Cancel()`, `File.ReadAllBytes` su un thread
del pool): non vanno corretti in blocco. Quelli che contano sono nei punti C-24 ... C-36.

### Dimensioni

Codice C#: 3,82 MB in 125 file. Di questi, 1,24 MB si compilano solo in Debug (prove, strumenti, sonde), 0,03 MB in Debug e nella
versione di prova (`PROVA`), 2,45 MB sempre. La suite di prove (`Core/ProvaBozze*.cs`, 13 file) pesa 1,07 MB, il 28% del codice.
MISURATO (script sui blocchi `#if`).

| File | KB | Righe | Note |
|---|---|---|---|
| Core/ProvaBozze.Audit.cs | 484 | 6539 | solo Debug |
| Core/ProvaBozze.cs | 386 | 4969 | solo Debug |
| UI/MainWindow.xaml.cs | 275 | 5177 | 36 KB solo Debug (562 righe) |
| Core/GraphProvider.cs | 110 | 1919 | |
| UI/ComposeWindow.xaml.cs | 104 | 2089 | |
| Core/ImapProvider.cs | 95 | 1765 | |
| UI/ReadingView.xaml.cs | 95 | 1831 | |
| UI/MainWindow.xaml | 94 | 1031 | 170 righe del modello vecchio della riga (C-01) |
| Core/GmailProvider.cs | 84 | 1513 | |
| UI/App.cs | 78 | 1401 | |
| UI/ViewModels.cs | 66 | 1499 | |
| Core/DevTools.cs | 66 | 1051 | solo Debug |

Metodi: 1541 trovati dallo script, 66 fra 80 e 150 righe, 19 fra 150 e 300, 5 oltre 300 (questi ultimi sono in realtà dichiarazioni
di classe con costruttore primario contate come un metodo). I metodi di produzione più lunghi sono in [C-23](#c-23-i-metodi-più-lunghi).
MISURATO, con il limite che lo script conta le graffe senza un vero analizzatore C#.

### Doppioni

Script su finestre di 8 righe normalizzate (stringhe e numeri sostituiti, righe di sole graffe tolte). Fuori dalle prove: 68 blocchi
ripetuti. Le coppie di file con più righe in comune:

| Righe ripetute | File |
|---|---|
| 284 | Core/GmailProvider.cs e Core/ImapProvider.cs (circa 425 righe di codice uguali contando anche i blocchi più corti, confronto riga per riga) |
| 141 | UI/ContextMenus.cs con sé stesso (l'elenco del catalogo: righe simili, non un doppione vero) |
| 66 | Core/Calendar.cs con sé stesso (mappe di nomi e colori: non un doppione vero) |
| 24 | UI/AutoRepliesWindow.cs e UI/RuleWindow.cs (consenso, C-13) |
| 17 | UI/AutoRepliesWindow.cs e UI/ManageAccountWindow.cs (scheletro delle finestre, C-16) |
| 10 | UI/LinkWindow.cs e UI/NameWindow.cs (C-16) |

Con finestre di 6 righe salgono le finestre di dialogo (ScheduleWindow, InviteWindow, BackupWindows, EventWindow: 12-14 righe a coppia).
MISURATO. Il doppione più grande fra produzione e prove è `MainWindow.AddTestTemplate` (MainWindow.xaml.cs:994) e `TemplatesTest`
(ProvaBozze.cs:2019): due documenti finti costruiti allo stesso modo, solo Debug, innocuo.

---

## 1. Codice morto e residui

### C-01 Il modello vecchio della riga dell'elenco, ancora acceso da una variabile anche nella versione installata

- Dove: UI/MainWindow.xaml:795-967 (`ListBox.ItemTemplate` della MessageList), UI/MainWindow.xaml.cs:42-43 (costruttore),
  UI/MainWindow.xaml.cs:2669 (commento). Proprietà di `MessageItem` usate solo da quel modello: UI/ViewModels.cs:693
  (`ChevronVisibility` della riga), 696 (`RowMargin`), 716 (`PinVisibility`), 826 (`WhereVisibility`), 872 (`AttachmentVisibility`),
  873 (`ImportantVisibility`), 899 (`ChipsVisibility`), 973 (`FlagVisibility`), più le notifiche `Changed(nameof(...))` di queste a
  703, 721, 949.
- Che cosa: la riga è disegnata da `MessageRowElement` dal giro F1. Il modello di prima (173 righe di XAML) resta nel file e
  `MISSIVA_RIGA_VECCHIA=1` lo rimette. La lettura della variabile non è dentro `#if DEBUG`: vale anche per la Missiva installata.
  CLAUDE.md dice "da togliere quando A ha finito" (giro del 29-30 settembre). Le otto proprietà elencate non le legge nessun codice C#
  (cercate con grep in tutti i .cs e .xaml): esistono solo per i binding del modello vecchio. Le tre `Changed(...)` fanno partire un
  `PropertyChanged` in più a ogni cambio di contrassegno, fissato o conversazione, e `MessageRowElement.OnItemChanged`
  (MessageRowElement.cs:110-131) per un nome che non conosce fa `Sync()` e `Invalidate(true)`, cioè misura e disegno della riga.
- Perché conta: 173 righe di XAML e otto proprietà da tenere allineate a ogni cambio della riga; il confronto pixel per pixel che le
  giustificava è finito. Il lavoro in più su ogni `Changed` è piccolo ma è nel percorso della riga.
- Stato: CONFERMATO (codice e grep). Che il confronto di "A" sia finito è SOSPETTO: lo deduco dalle date di CLAUDE.md.
- Proposta: togliere il modello dal XAML, la riga 43 (assegnare sempre `MessageRowNew`, o metterlo direttamente come `ItemTemplate`
  nel XAML), le otto proprietà e le loro `Changed`. Insieme vanno via i comandi di confronto "vetrina", "vscroll", "altezze" se non
  servono più (C-04). 1 ora, rischio basso (se resta un binding nascosto, WPF lo scrive nell'Output di Debug).
- Verifica su Windows: `dotnet build`; "vetrina" e foto della lista in chiaro e scuro prima e dopo; "costo righe" e "scorri 120"
  (atteso pari o meglio); `--prova-bozze` (AccessibilityTests usa i nomi delle righe).

### C-02 Interruttori della sonda del cursore rimasti dopo la decisione

- Dove: Core/CursorProbe.cs:36-57 (interruttori), UI/MessageRowElement.cs:69, 153-154, 275-276, 291-292, 736 (dove si leggono);
  variabili MISSIVA_SENZA_AZIONI, MISSIVA_SENZA_HOVER, MISSIVA_SENZA_TOOLTIP, MISSIVA_SENZA_MOUSEMOVE, MISSIVA_LISTA_INERTE,
  MISSIVA_RIMEDIO_HOVER (lette da una tabella in CursorProbe.cs:150, 174-175); comandi "sonda", "cursore stato", "sospensione prova".
- Che cosa: l'esperimento del cursore sopra l'elenco si è chiuso il 1 ottobre con `ScrollHitGuard` acceso di base. Gli interruttori
  servivano a quella misura; nella versione installata valgono `false` costanti (ramo `#else`), quindi il compilatore toglie i rami,
  ma il codice di `MessageRowElement` li porta ancora (il "rimedio di prova, spento" `DeferHover` a 276 e 292). I contatori (`Hit`,
  `Time`) sono `[Conditional("DEBUG")]` o vuoti: non costano.
- Perché conta: sette condizioni in più nel file della riga, che è il punto più delicato per lo scorrimento; chi lo legge deve capire
  che sono sempre false.
- Stato: CONFERMATO.
- Proposta: togliere gli interruttori e il rimedio `DeferHover` (con le variabili e il comando "sonda"); lasciare i contatori, se l'autore
  vuole ancora la riga "cursore:" di "scorri N". 1 ora, rischio basso.
- Verifica su Windows: `dotnet build` in Debug e Release; "scorri 120" con il cursore sopra le righe, riga "cursore:" del registro.

### C-03 Copie "di prima" tenute per il confronto

- Dove: Core/AddressBook.cs:339-401 e 463-478 (`SuggestOld`), Core/Contacts.cs:73-75 e 85-91 (`TestOldMatch`, `MatchesByCulture`),
  Core/HtmlToFlow.cs:177-179, 263-265, 389-406 (`TestOld`), Core/DevTools.Persone.cs:39 ("persone confronto vecchio"), comando
  "arrivo di prova vecchio".
- Che cosa: tutte dentro `#if DEBUG`. Le usano le prove dell'audit (ProvaBozze.Audit.cs:1762, 1779, 1872, 6340, 6389) per dire "stesso
  risultato di prima". Sono il controllo di non regressione di quei punti, quindi non sono morte; ma fissano per sempre due versioni
  dello stesso algoritmo.
- Perché conta: ogni correzione futura di `Suggest`, `Matches` o `HtmlToFlow` va fatta due volte, o il confronto cade.
- Stato: CONFERMATO.
- Proposta: quando si tocca uno di questi algoritmi, sostituire il confronto con un elenco di risultati attesi scritto nelle prove
  (un "golden" di 30-50 casi preso dall'uscita di oggi), poi togliere la copia vecchia. Non prima: oggi non costano nulla nella versione
  installata. 2-3 ore per i tre, rischio basso.
- Verifica su Windows: `--prova-audit risultato.txt velocita2` e `dati`, stesso numero di controlli.

### C-04 Comandi di MISSIVA_KEYS e variabili senza un uso noto

- Dove: Core/DevTools.cs:189-506 (`Press`, 318 righe), Core/DevTools.Persone.cs; variabili in Core/Store.cs:1097
  (`MISSIVA_REGISTRO_SUBITO`), Core/DevTools.cs:47-60 (`--mostra-notifica`).
- Che cosa: i comandi riconosciuti da `Press` sono una cinquantina. Gli script del repository (`strumenti/*.ps1`) ne usano pochi:
  "clic", "apri posta", "punto elenco", "replay", "rotella", "scorri" (ingresso.ps1), più le parole "notifica" e "seleziona" in altri
  script. Gli altri li usano gli strumenti in `loop\...` sul PC dell'autore, che qui non ci sono, o nessuno. Senza uso documentato o legati a
  esperimenti chiusi: "tau" (alias della molla di prima), "scorri semplice" (controllo con una ListBox banale), "sonda", "cursore stato",
  "sospensione prova" (C-02), "vetrina", "vscroll", "altezze" (confronto con il modello vecchio, C-01), "disegni dopo arrivo" (non citato
  in CLAUDE.md), "arrivo di prova vecchio" (C-03), e la prova alla cieca del touchpad (`MISSIVA_VARIANTI`, `TouchModels` 0-2: il modello
  scelto è il 3, UI/WheelScrolling.cs:86). `MISSIVA_REGISTRO_SUBITO` e `--mostra-notifica` sono lette ma non citate da nessuna parte.
- Perché conta: `Press` è il metodo più lungo dopo i costruttori (318 righe) e ogni comando nuovo si aggiunge in fondo. Comandi morti
  confondono chi deve scegliere quale usare.
- Stato: CONFERMATO che gli script del repository non li usano; SOSPETTO che non li usi nessuno degli strumenti in `loop\` (da
  controllare sul PC con `Select-String` in `C:\Progetti\Missiva\loop`).
- Proposta: sul PC, un `Select-String` dei nomi dei comandi in `loop\` e `strumenti\`; togliere quelli senza uso; dividere `Press` in una
  tabella `nome -> azione` (come per la suite, C-40), un file per area (elenco, scorrimento, notifiche, persone). 2-3 ore, rischio basso
  (solo Debug).
- Verifica su Windows: `dotnet build` Debug e Release con gli strumenti (`-p:DefineConstants=DEBUG%3BTRACE`); gli script di `loop\` che
  restano girano come prima.

### C-05 Membri mai usati

- Dove (cercati con uno script: nome dichiarato che compare una volta sola in tutti i .cs, .xaml e .ps1):
  - Core/Offline.cs:610 `OfflineCopy.TestFull`;
  - Core/Pec.cs:34 `PecInfo.IsReceipt`;
  - UI/EventWindow.cs:583 `TestSaveAsync`;
  - UI/PeopleView.cs:762 `TestSelectFirst`;
  - UI/MainWindow.Notifications.cs:183 `MainWindow.QuickReply`: usato solo dalle prove (ProvaBozze.cs:247, 268, 290). Le risposte dalla
    casella delle notifiche non partono più (audit S-21, MainWindow.Notifications.cs:43-51 apre sempre una scrittura), quindi la funzione
    prova un percorso che l'app non ha più;
  - UI/RulesView.cs:101 `StartAccount`: usato solo in Debug, avviso CS0649 in Release.
  - I campi delle strutture di Windows in UI/TrayIcon.cs:143-150 e Core/PdfWorker.cs:641, 652 sono "mai usati" ma servono alla forma
    della struttura: vanno lasciati.
- Che cosa: residui di prove o di funzioni cambiate.
- Perché conta: poco codice, ma `QuickReply` con le sue prove fa credere che le risposte rapide esistano ancora (e CLAUDE.md, righe
  129-134, lo dice: C-68).
- Stato: CONFERMATO (lo script ha il limite dei nomi uguali in classi diverse: questi sei li ho controllati a mano).
- Proposta: togliere i quattro `Test...` e `IsReceipt`; mettere `StartAccount` dentro `#if DEBUG` o assegnarlo `= null` esplicito;
  togliere `QuickReply` e le sue tre prove, oppure, se l'autore vuole tenere la porta aperta, scrivere nel commento che oggi non la usa
  nessuno. 30 minuti, rischio nullo.
- Verifica su Windows: build Debug, Release e `MissivaProva=true` senza avvisi nuovi; `--prova-bozze` con 3 controlli in meno se si
  toglie `QuickReply`.

### C-06 Codice di migrazione che gira a ogni avvio

- Dove: Core/MailCache.cs:80-101 (`EncryptOld`, chiamata da UI/App.cs:100); i lettori dei formati in chiaro: Core/AddressBook.cs:12,
  Core/Store.cs:103, 122, 293, 395; Core/SenderLogos.cs:199 (`AuthenticateFromCopies`, già protetta da un file segno).
- Che cosa: `EncryptOld` 20 s dopo ogni avvio enumera ricorsivamente tutta la cartella `cache` (comprese le sottocartelle `corpi` con
  migliaia di `.bin`, scartati dal filtro `*.json`) e legge per intero ogni `.json` con `File.ReadAllBytes` solo per guardarne il primo
  byte. Dopo la prima volta non trova più nulla da cifrare.
- Perché conta: lavoro di disco inutile a ogni avvio (piccolo: le copie degli elenchi sono qualche KB l'una) e codice di migrazione che
  nessuno toglierà se non lo si scrive da qualche parte.
- Stato: CONFERMATO il lavoro; il costo è SOSPETTO (non misurato qui).
- Proposta: controllare nel registro dell'autore che la riga "copia locale: N file scritti in chiaro ... ora cifrati" non compaia più da
  settimane; poi togliere `EncryptOld`, oppure segnarla fatta con un file come `AuthenticateFromCopies`. I lettori dei formati in chiaro
  (pochi `if` ciascuno) possono restare finché c'è una copia di sicurezza vecchia da reimportare. 20 minuti, rischio nullo.
- Verifica su Windows: avvio con la copia dei dati, nessuna riga di errore; il registro non deve più dire "copia locale: ... cifrati".

### C-07 Assegnazioni e parametri inutili

- Dove e che cosa:
  - UI/MessageRowElement.cs:430: `icons`, una `List` creata a ogni `Compute` e mai usata. È il percorso caldo dello scorrimento (una
    allocazione per riga calcolata).
  - UI/MessageRowElement.cs:374: `accent`, una ricerca di risorsa a ogni `Compute`; la barra la rilegge in `OnRender` (riga 659).
  - UI/MessageRowElement.cs:642: `var l = Ensure(...)`; `Ensure` va chiamato, la variabile no.
  - Core/Store.cs:747: `error = null` sovrascritto subito dopo.
  - UI/App.cs:1253: parametro `now` di `AddActions`, residuo di quando le ore di Posponi si calcolavano alla notifica. Nello stesso
    punto due `<summary>` di fila (App.cs:1236-1250): il primo è di `AddActions` ma sta sopra `FlagButton`. Lo stesso a
    UI/MainWindow.xaml.cs:4106-4107 (il riassunto di `CheckSnoozedAsync` sopra `SendScheduledAsync`).
  - Core/SenderCheck.cs:482: parametro `found` che serve solo a scegliere un sovraccarico (ImapProvider.cs:1062, GmailProvider.cs:777).
  - Core/MailHtml.cs:354, 362: `range`, `position` imposti dalle firme dei ganci di AngleSharp: vanno lasciati (scarto `_`).
  - UI/ComposeWindow.Format.cs:333: parametro `name` di `ReplaceSignature`: qui il residuo nasconde un difetto (C-52).
- Perché conta: piccolo, ma `icons` e `accent` sono lavoro per riga durante lo scorrimento.
- Stato: CONFERMATO (IDE0059 e IDE0060 sulla copia, poi letti a mano).
- Proposta: un commit di pulizia; `SenderCheck.FetchAsync(..., found)` diventa un metodo con un nome suo (`FetchIfThereAsync`).
  30 minuti, rischio nullo.
- Verifica su Windows: "costo righe" e "scorri 120" prima e dopo (atteso pari o meglio); `--prova-bozze`.

---

## 2. Duplicazioni

### C-08 Gmail e IMAP: corpi, anticipi, allegati e azioni per cartella scritti due volte

- Dove: Core/GmailProvider.cs:735-999 (`GetBodyAsync`, `KeptBody`, `SavedBody`, `OnDisk` 781, `GetListHeadersAsync`, `DownloadAsync`
  812, `PrefetchAsync` 844, `ReadBodyAsync` 883, `ContentOf` 957, `AttachmentAsync` 970, `EachFolderAsync` 991) e
  Core/ImapProvider.cs:1023-1279 (gli stessi; `ReadBodyAsync` 1158, `AttachmentAsync` 1241, `EachFolderAsync` 1271).
- Che cosa: `GmailProvider` e `ImapProvider` sono tutte e due `sealed`, implementano `IMailProvider` e non ereditano l'una dall'altra
  (GmailProvider.cs:17, ImapProvider.cs:22). Gmail prende da IMAP solo pezzi statici (`FreshInboxAsync`, `OpenFreshAsync`,
  `ArrivedAsync`, `Prefetchable`, `ArrivalDates`, `SummaryCache`). In questa parte 182 righe di codice su circa 200 sono uguali; il
  blocco più lungo uguale è GmailProvider.cs:883-941 contro ImapProvider.cs:1158-1214 (46 righe). Le differenze vere: la connessione
  (`ConnectionFor(message)` in Gmail, `main` in IMAP), la riga "tempi", la lettura degli indirizzi (`People()`). Una divergenza è già
  nata: in IMAP `AttachmentAsync` apre la cartella con EXAMINE sulle connessioni secondarie (`ExamineOrOpenAsync`, ImapProvider.cs:1234,
  1249), in Gmail sempre con `OpenAsync`, cioè SELECT in lettura e scrittura (GmailProvider.cs:976), anche sulla connessione delle bozze
  quando Inoltra scarica gli allegati dell'originale (GmailProvider.cs:1467).
- Perché conta: ogni correzione dell'audit su questi metodi è stata fatta due volte, e una non è arrivata in Gmail.
- Stato: CONFERMATO (lettura e confronto riga per riga sul codice senza commenti). L'effetto del SELECT in più è SOSPETTO (un SELECT
  invece di EXAMINE può segnare `\Recent` e costa 0,3 s secondo CLAUDE.md, ma per Gmail `\Recent` non conta).
- Proposta: una classe base astratta comune (per esempio `ImapCore`) con tre ganci: la connessione per un messaggio, la chiave stabile
  (`StableIdOf`: X-GM-MSGID o Message-Id), la riga del registro. Da fare dopo C-10 (il passo più facile) e insieme a C-11. 1-2 giorni,
  rischio medio (la scelta della connessione di Gmail per "Tutti i messaggi" va tenuta identica).
- Verifica su Windows: `--prova-bozze` intera, in particolare corpi in anticipo con EXAMINE e BODY.PEEK[], il clic durante un EXAMINE
  lento (`ExamineDelay`), `BODY.PEEK[1.MIME]`, `OfflineCopyTestsAsync`, `SpeedAuditTests`; poi `elenchi.ps1 -Messages` sulla copia di
  prova, tempi di apertura prima e dopo.

### C-09 La connessione IMAP e `RunAsync` copiate, con un timeout diverso

- Dove: classe `Connection` in Core/GmailProvider.cs:27-59 e Core/ImapProvider.cs:29-58; `RunAsync` in GmailProvider.cs:164-224 e
  ImapProvider.cs:258-290; `ConnectEarlyAsync` in GmailProvider.cs:78-96.
- Che cosa: 23 righe su 24 uguali, ma il client nasce in modo diverso: IMAP `new() { Timeout = 60_000 }` (ImapProvider.cs:32), Gmail
  `new()` (GmailProvider.cs:30 e 81), cioè il valore di MailKit, 120 s. Anche il client SMTP di Gmail (`DsnSmtpClient`,
  GmailProvider.cs:137, 1253) non imposta il timeout, quello di IMAP sì (ImapProvider.cs:173). CLAUDE.md ("Non letti ed elenco dopo un
  arrivo", ultima frase) parla del "timeout di MailKit di 120 s" come di un valore unico: vale solo per Gmail. Solo IMAP ha `retrying`
  (ImapProvider.cs:256, 278); solo Gmail ha `Adopt`.
- Perché conta: dopo una sospensione una connessione morta di Gmail tiene ferma la principale fino a due minuti, quella IMAP uno.
  `StuckCheck` (UI/App.cs:921) vale 2 minuti: per Gmail scade insieme al timeout, non dopo. Nessuno ha scelto questa differenza.
- Stato: CONFERMATO nel codice. Che il caso del 6 ottobre nel registro ("cartelle") fosse Gmail è SOSPETTO: manca la riga con l'account.
- Proposta: passo minimo, una costante comune (per esempio `ImapProvider.CommandTimeout`) usata dalle due `new ImapClient()` di Gmail
  e dai client SMTP; da decidere con l'autore se 60 o 120 s. Poi una sola classe di connessione con `Timeout`, `Adopt` e il LOGOUT in
  sottofondo, e un `RunAsync` comune con il collegamento come delegato. Passo minimo 15 minuti, rischio basso; il resto mezza giornata.
- Verifica su Windows: `--prova-bozze` (connessione caduta dopo APPEND e dopo COPY, `IdleTestsAsync`); a mano, sospendere il PC con
  Missiva aperta e leggere nel registro i tempi della prima richiesta Gmail dopo la ripresa.

### C-10 `BuildAsync` identico in Gmail e IMAP

- Dove: Core/GmailProvider.cs:1420-1500 (con `TypeOf` 1494) e Core/ImapProvider.cs:1611-1676 (con `TypeOf` 1674).
- Che cosa: 59 righe di codice uguali su 60: destinatari, citazione, cid, allegati dell'inoltro, immagini del testo. L'unica differenza è
  la connessione della FETCH di Message-Id e References (GmailProvider.cs:1444-1445). Anche i quattro `Attachments.Add` sincroni
  (CA1849: GmailProvider.cs:1463-1487, ImapProvider.cs:1650-1667) sono gli stessi.
- Perché conta: la parte del messaggio in uscita è quella dove un errore si vede da chi lo riceve.
- Stato: CONFERMATO.
- Proposta: una funzione statica condivisa (per esempio in `MimeOptions`) con tre delegati: corpo dell'originale, intestazioni,
  scaricamento di un allegato. Non tocca le connessioni. 2-3 ore, rischio basso.
- Verifica su Windows: `--prova-bozze` (`FormatRoundTrip`, immagini del testo di Gmail con multipart/related, Inoltra con allegati,
  `IdentitiesAsync`, invio pianificato); `MISSIVA_DUMP` per confrontare l'HTML in uscita prima e dopo.

### C-11 Bozze di Gmail e IMAP, e una differenza nella ricerca della bozza

- Dove: Core/GmailProvider.cs:1329-1418 (`SaveDraftAsync`, `KeepAttachment` 1382, `DiscardDraftAsync` 1385, `FindAsync` 1391,
  `DraftsAsync` 1396, `DeleteDraftAsync` 1404) e Core/ImapProvider.cs:1540-1610, `FindAsync` 1435.
- Che cosa: 45 righe uguali su circa 50. Cambiano la cartella Bozze, la chiave (X-GM-MSGID o Message-Id) e l'EXPUNGE (solo con UIDPLUS
  in IMAP). In più `FindAsync` di IMAP esclude i messaggi `\Deleted` (ImapProvider.cs:1437), quello di Gmail no.
- Perché conta: la bozza di prima ritrovata dopo una connessione caduta è il punto più delicato delle bozze; la differenza su `\Deleted`
  non è spiegata da nessun commento.
- Stato: CONFERMATO la differenza; che con Gmail conti è SOSPETTO (Gmail cancella subito con UID EXPUNGE, quindi un `\Deleted` rimasto
  è raro).
- Proposta: i tre ganci nella base di C-08; per `FindAsync` di Gmail, o lo stesso filtro, o un commento che spiega perché no.
  Mezza giornata insieme a C-08, rischio medio.
- Verifica su Windows: `--prova-bozze` ("connessione caduta dopo APPEND", Scarta in Gmail e IMAP, allegati sotto 5 MB tenuti,
  `MoveDraftTo`).

### C-12 Graph: cinque copie di "token del consenso più Consenti"

- Dove: Core/GraphProvider.cs:137-157 (caselle condivise), 1075-1091 (calendario), 1094-1112 (impostazioni della casella), 1171-1189
  (contatti), 1233-1251 (colleghi).
- Che cosa: ogni coppia ripete lo stesso schema: un `TestXConsent` sotto `#if DEBUG`, `XTokenAsync(bool interactive)` con il ramo
  `testing ? Task.FromResult(TestX || interactive ? "prova-x" : null)`, `MicrosoftAuth.ExtraTokenAsync(account, scopes, interactive)`, e
  `AllowXAsync` che lancia "Il consenso a ... non è arrivato.". Circa 95 righe.
- Perché conta: un consenso nuovo (e ne mancano ancora, per esempio per le funzioni delle caselle condivise) è una sesta copia.
- Stato: CONFERMATO.
- Proposta: un `enum` o una piccola tabella (permessi, account che accede, segnaposto di prova, frase) e due metodi soli; i metodi
  pubblici restano come chiamate di una riga, quindi `IMailProvider` e le prove non cambiano. 2-3 ore, rischio basso.
- Verifica su Windows: `--prova-bozze` (`CalendarAsync`, `AutoRepliesAsync`, `ContactsAsync`, `PeopleSuggestAsync`,
  `SharedMailboxAsync`, `RulesAsync`: tutte contano "nessuna richiesta senza consenso").

### C-13 Consenso nell'interfaccia: otto copie, e il proprietario della finestra di Microsoft che resta su finestre chiuse

- Dove: UI/MainWindow.Calendar.cs:105-122, UI/MainWindow.People.cs:114-133, UI/MainWindow.xaml.cs:1214-1233,
  UI/ReadingView.xaml.cs:1509-1525, UI/AutoRepliesWindow.cs:250-280, UI/RuleWindow.cs:282-308, UI/RulesView.cs:300-326,
  UI/ComposeWindow.xaml.cs:1031-1052. `MicrosoftAuth.Owner` in Core/MicrosoftAuth.cs:37.
- Che cosa: ogni copia ripete try, `catch when MicrosoftAuth.Canceled`, `Log.Error` e `ReadingView.ShowConsentError`; tre
  (AutoRepliesWindow, RuleWindow, RulesView) sono quasi identiche, compresa la frase "Nella versione di prova la finestra di Microsoft
  non si apre." scritta quattro volte. `MicrosoftAuth.Owner` è una `Func` statica: la impostano AutoRepliesWindow.cs:263,
  RuleWindow.cs:294, RulesView.cs:312, ComposeWindow.xaml.cs:1039, ManageAccountWindow.cs:397, AccountWindow.xaml.cs:230 e 295; la
  rimette sulla finestra principale MainWindow solo dopo Risposte automatiche (MainWindow.AutoReplies.cs:59), Gestisci
  (MainWindow.xaml.cs:1275) e Aggiungi (5006). Dopo RuleWindow, RulesView e la scrittura nessuno la rimette: la `Func` tiene viva la
  finestra chiusa (per la scrittura: documento e immagini) e la prossima finestra di Microsoft riceve l'HWND di una finestra distrutta.
- Perché conta: perdita di memoria piccola ma certa; effetto sulla finestra di accesso sconosciuto.
- Stato: CONFERMATO che `Owner` non si rimette. SOSPETTO l'effetto su WAM (finestra dietro, errore di MSAL, o nulla): serve un account
  con il token scaduto.
- Proposta: `Owner` risolto al momento della chiamata, sul thread dell'interfaccia: la finestra attiva di Missiva se c'è, altrimenti la
  principale; così nessuno deve più impostarlo né rimetterlo. Poi un solo aiuto `ConsentUi.RunAsync(owner, allow, ...)` che fa ramo di
  prova, Canceled, registro e `ShowConsentError`. 30 minuti per `Owner`, mezza giornata per l'aiuto; rischio basso.
- Verifica su Windows: nella versione di prova ogni "Consenti ..." scrive "prova: chiederei il consenso ..."; `memoria.ps1` dopo dieci
  scritture aperte e chiuse con "Cerca anche fra i colleghi"; la finestra di Microsoft vera solo a mano.

### C-14 Google: la chiusura dell'accesso copiata tre volte

- Dove: Core/GmailProvider.Calendar.cs:27-50 (`AllowAsync`), UI/ManageAccountWindow.cs:427-451 (`SignInGoogleAsync`),
  UI/AccountWindow.xaml.cs:565-595.
- Che cosa: tutte fanno `GoogleAuth.SignInAsync` con `ExtraScopesOf`, controllano che l'indirizzo scelto nel browser sia quello
  dell'account (due copie del messaggio "Nel browser hai scelto ..."), salvano il token cifrato, `GoogleAuth.SetScopes`,
  `GoogleAuth.Forget`, `Store.Save()`. Anche l'attesa di 10 minuti è scritta tre volte (GmailProvider.Calendar.cs:29,
  ManageAccountWindow.cs:435, AccountWindow.xaml.cs:558).
- Perché conta: la regola "un permesso tolto si dice" (`PermissionsLost`) dipende dal fatto che tutti e tre passino da `SetScopes`; un
  quarto punto di accesso la dimenticherebbe.
- Stato: CONFERMATO.
- Proposta: `GoogleAuth.Apply(account, result)` e `GoogleAuth.RequireSame(account, result)`, più una costante per l'attesa. 1-2 ore,
  rischio basso.
- Verifica su Windows: `--prova-bozze` (`GoogleCalendarAsync`, `GoogleContactsFlowAsync`, `DefectAuditTests`); il browser vero a mano.

### C-15 Menu del tasto destro: ciclo copiato e nomi scritti due volte

- Dove: il riempimento in UI/MainWindow.xaml.cs:1600-1616 (cartelle) e 3912-3928 (messaggi); le voci in `Build` di 1557-1598 e
  3833-3909 contro il catalogo di UI/ContextMenus.cs:69-133.
- Che cosa: le 17 righe che mettono le voci, i separatori solo fra voci presenti e "Personalizza il menu…" sono identiche nei due menu.
  Nome, icona e tono di ogni voce stanno sia nel catalogo (usato da "Personalizza") sia nella chiamata `MenuEntry(...)`; oggi coincidono
  (controllato con uno script), ma niente lo garantisce.
- Perché conta: un nome cambiato in un posto solo fa vedere in "Personalizza" una voce diversa da quella del menu.
- Stato: CONFERMATO.
- Proposta: `FillCustomMenu(menu, definizione, build)` per il ciclo e un `MenuEntry(entry, click)` che prende nome, icona e tono dal
  catalogo (le voci con il nome che cambia, Letto e Da leggere, Fissa e Rimuovi, passano il testo a parte). 2-3 ore, rischio basso.
- Verifica su Windows: `--menu`, `--menu Posponi`, `--folder-menu`, `--folder-menu "Tutti i messaggi"`, `--menu-edit`, con
  `foto.ps1 -Popup` prima e dopo.

### C-16 Finestre di dialogo con lo stesso scheletro, e un rosso fisso nel tema scuro

- Dove: UI/NameWindow.cs:19-66, UI/ScheduleWindow.cs:17-57, UI/LinkWindow.cs:20-66, SnoozeWindow, CategoryWindow, GroupWindow,
  InviteWindow, ContactWindow, BackupWindows (due), AutoRepliesWindow, ManageAccountWindow, RuleWindow, e `Dialog.Build`
  (UI/Dialog.cs:44-80).
- Che cosa: lo stesso blocco (`Width`, `SizeToContent.Height`, `ResizeMode.NoResize`, `CenterOwner`, `ShowInTaskbar=false`) è in 15
  file; il pannello OK/Annulla con `IsDefault`, `IsCancel`, `MinWidth 100` e `AccentButtonStyle` è copiato 11 volte. Il testo d'errore
  rosso è `Color.FromRgb(0xC4, 0x2B, 0x1C)` scritto in 16 punti, uguale per i due temi, mentre l'app ha `CriticalBrush` per tema
  (UI/App.cs:684, #FF99A4 nello scuro). Contrasto calcolato di #C42B1C: 2,9 a 1 su #202020, contro 5,7 a 1 sul bianco. Solo `Dialog.Build`
  gestisce il proprietario nascosto nell'area di notifica (Dialog.cs:53-56); per SnoozeWindow lo rifà a mano MainWindow.Notifications.cs:155-157.
- Perché conta: il rosso fisso nel tema scuro si legge male (lo stesso difetto corretto per le righe dell'elenco al giro D1); ogni
  finestra nuova copia lo scheletro.
- Stato: CONFERMATO il codice; il contrasto è calcolato su fondi di riferimento, quello vero sopra Mica è SOSPETTO (va misurato su una
  foto).
- Proposta: un aiuto `DialogFrame` (cornice, pulsanti, testo d'errore con `SetResourceReference(Foreground, "CriticalBrush")`,
  proprietario o schermo), poi le finestre una alla volta. Il cambio del rosso nel tema scuro si vede: va chiesto all'autore. 1 giorno,
  rischio basso-medio (pochi pixel di disposizione).
- Verifica su Windows: `foto.ps1` di ogni finestra in chiaro e scuro, prima e dopo; nomi di UI Automation con lo script di
  accessibilità del giro P4.

### C-17 Scritture di file fuori da `SafeFile` e DPAPI chiamata direttamente in nove file

- Dove:
  - file temporaneo e rinomina senza `Flush(true)`: Core/GoogleAuth.cs:183-186 (token di Gmail), Core/MailCache.cs:92-94 e 137-140,
    Core/Offline.cs:257-260 e 303-306 (corpi su disco);
  - scrittura diretta sul file vero: Core/Calendar.cs:446 (copia del calendario), Core/SenderLogos.cs:217, 373, 382;
  - `ProtectedData.Protect/Unprotect(..., null, CurrentUser)` diretto: ActionQueue.cs:72, 98; AddressBook.cs:108, 563; Calendar.cs:446,
    462; Contacts.cs:172, 191, 256, 276; GoogleAuth.cs:155, 182; MailCache.cs:93, 123, 139; Offline.cs:253, 295, 300, 342, 373, 415.
    Esiste già `Store.ProtectBytes` e `Store.UnprotectBytes` (Store.cs:860-862), con l'entropia di prova `TestEntropy` che fa da "altro PC".
- Che cosa: lo schema "cifra, file temporaneo, rinomina" è scritto otto volte. Il nome del temporaneo è sempre `path + ".tmp"`: in
  `MailCache.Write` non c'è un lucchetto, e `EncryptOld` (C-06) gira in sottofondo sugli stessi file. Con una caduta di corrente il file
  scritto senza `Flush(true)` può restare vuoto o di zeri; la copia del calendario scritta direttamente può restare troncata.
- Perché conta: sono tutte copie che si rifanno (token, elenchi, corpi, loghi), quindi il danno è "si riscarica", non una perdita. Ma
  `Backup` legge contatti.dat e rubrica.json con `Store.UnprotectBytes` (Backup.cs:316-326) mentre chi li scrive usa l'entropia nulla:
  in Debug, con `TestEntropy` impostata, il caricamento vero di quei file non passa dalla simulazione "altro PC".
- Stato: CONFERMATO il codice; l'effetto sulle prove della copia di sicurezza è SOSPETTO (da guardare se `BackupAuditTests` scrive da sé
  quei file).
- Proposta: `SafeFile.WriteProtected(path, bytes, durable)` e `SafeFile.ReadProtected(path)` sopra `Store.ProtectBytes`; `durable: false`
  per i corpi della copia dei 30 giorni, dove `Flush(true)` su centinaia di file va prima misurato; nome temporaneo unico
  (`.tmp-` più un Guid). Nella versione installata i byte restano gli stessi. 2-3 ore, rischio basso.
- Verifica su Windows: `--prova-audit risultato.txt copia` e `dati`, `OfflineTests`, `OfflineCopyTestsAsync`, `CalendarAsync`; la
  misura della copia dei 30 giorni ("Gmail N corpi in X s") prima e dopo; una Missiva che rilegge i file della versione di prima.

### C-18 Doppioni minori nell'interfaccia

- Dove e che cosa:
  - UI/MainWindow.Calendar.cs:72-93 e UI/MainWindow.People.cs:39-60: nascondere e rimettere la posta, 22 righe identiche (cambiano solo
    la vista e la lista delle parti nascoste).
  - UI/MainWindow.xaml.cs:3283-3310 (`MoveAsync`) e 3364-3392 (`DeleteAsync`): il gruppo per account con prova, coda, `BodyCache.Forget`
    e `FollowFlags` ha la stessa forma, con piccole differenze (C-50 riguarda tutti e due).
  - `static readonly CultureInfo Italian = new("it-IT")` in UI/MainWindow.xaml.cs:1619 e UI/ComposeWindow.xaml.cs:783.
  - L'azzeramento di `listOptions` in UI/MainWindow.xaml.cs:1920-1924 e UI/MainWindow.Unified.cs:99-103.
- Perché conta: poco, ma il primo è una trappola: una parte nuova della finestra va aggiunta in due elenchi.
- Stato: CONFERMATO.
- Proposta: `HideMail(view, saved)` e `RestoreMail(view, saved)`; `MoveGroupAsync` comune dopo C-50; una costante di cultura in un
  posto solo. 2 ore, rischio basso (quello di Sposta ed Elimina medio).
- Verifica su Windows: `--calendario`, `--persone` e ritorno alla posta con UI Automation; `--delete 3`, `--drop "2>Archivio"`;
  `--prova-bozze` (`UnifiedRows`, coda).

---

## 3. File e metodi troppo grandi

Regole comuni ai punti C-19 ... C-22:

- ogni passo è uno spostamento puro di membri in un file `partial` nuovo, un commit per passo, con lo stesso blocco `using`;
- i campi con inizializzatore restano nel file principale (fra parti di una `partial` l'ordine di inizializzazione non è specificato:
  oggi nessuno dipende da un altro, ma è meglio non provarci);
- i blocchi `#if` si portano con i membri (MainWindow ne ha 13, GraphProvider 13, ReadingView 7, ComposeWindow 4; uno è
  `#if DEBUG || PROVA ... #else`, MainWindow.xaml.cs:3263-3267);
- da fare senza rami aperti: ogni spostamento di MainWindow.xaml.cs fa conflitto con qualunque altro lavoro;
- dopo la divisione i 123 rimandi `file:riga` di docs/audit non valgono più: scrivere in ESITO.md il commit di prima della divisione.

Verifica su Windows per ogni passo: `dotnet build` in Debug, Release e `-p:MissivaProva=true`; `--prova-bozze` con lo stesso numero di
controlli (870); il diff deve contenere solo righe spostate (`git diff HEAD~1 --color-moved=dimmed-zebra
--color-moved-ws=allow-indentation-change`); lo stesso numero di membri della classe, contato con la riflessione sulla dll di Debug.

### C-19 UI/MainWindow.xaml.cs (5177 righe, 298 membri)

- Dove: tutto il file; parti `partial` già esistenti: AutoReplies 64 righe, People 133, Flags 220, Calendar 251, SearchFilters 298,
  Notifications 323, Offline 340, Unified 354, Suggestions 415.
- Che cosa: 18 metodi oltre 40 righe, 8 oltre 60; 562 righe sono solo Debug. Divisione proposta per responsabilità (intervalli di righe
  di oggi):

| File nuovo | Contenuto | Intervalli | Righe |
|---|---|---|---|
| MainWindow.List.cs | caricamento, pagine, anticipi, scorrimento, arrivi, righe, selezione | 2183-2625, 2874-3010, 4265-4298 | 619 |
| MainWindow.Actions.cs | Sposta, Elimina, Archivia, segni, azioni della riga, Copia, Salva .eml e PDF, Origine | 3161-3408, 3609-3814, 4434-4494 | 501 |
| MainWindow.Accounts.cs | caricamento degli account, cartelle dal server, stato, caselle condivise, menu e rimozione | 930-993, 1031-1063, 1091-1300, 5002-5076, 5129-5161 | 411 |
| MainWindow.Folders.cs | menu, colori, Preferiti, crea, rinomina, sposta, svuota, elimina, selezione | 1534-1933 | 400 |
| MainWindow.MessageMenu.cs | `BuildMessageMenu`, Rispondi con riunione, categorie | 3815-3986, 4299-4433 | 317 |
| MainWindow.Debug.cs (tutto `#if DEBUG`) | prove e comandi; poi il blocco di `StartAsync` (C-23) | 994-1030, 2626-2873 | 287, poi 505 |
| MainWindow.Snooze.cs | Posponi, ritorni, invii pianificati | 3987-4264 | 283 |
| MainWindow.Layout.cs | colonne, divisori, finestra ingrandita, barre | 663-921 | 254 |
| MainWindow.ListHeader.cs | titolo, filtro, ordina, Vai a, Seleziona, segni delle cartelle | 1934-2182 | 242 |
| MainWindow.DragDrop.cs | trascinamento di cartelle, messaggi, account | 1364-1533, 5077-5128 | 222 |
| MainWindow.Undo.cs | Annulla e barra dell'invio | 3409-3608 | 203 |
| MainWindow.Commands.cs | schede, Impostazioni, regole, tastiera, Stampa, aggiorna | 4798-4975, 5162-5177 | 196 |
| MainWindow.Tray.cs | area di notifica, numero sull'icona | 458-580, 1301-1363 | 184 |
| MainWindow.Compose.cs | Nuovo, Rispondi, Inoltra, bozze, inviti, .eml, mailto | 4495-4634, 4976-5001 | 166 |
| MainWindow.Search.cs | ricerca | 4635-4797 | 163 |
| MainWindow.MessageWindow.cs | finestra del messaggio e sua barra | 3011-3160 | 146 |
| MainWindow.Notifications.cs (esistente) | `OpenFromNotificationAsync` | 1064-1090 | +31 |

  Restano nel principale (circa 550 righe, 330 dopo C-23): campi, costruttore, `StartAsync`, `OnClosing`, `ApplySettings`, tema.
  Campi da portare prima in cima al file, perché dichiarati dentro un intervallo ma usati da più gruppi: `listOptions` e `reloading`
  (1928-1932), `firstPage` (2202), `readerClears`, `selectionChanges`, `readerOpens` (3002), `selectingByKeyboard` (3751), `lastMove`
  (3423), `snoozeTimer` (3990), `accountsReady` e `accountsListed` (1055-1057), `badge` (1301), `Italian` (1619), `compact`,
  `foldersOver`, `foldersClosed` (708-712).
- Perché conta: è il file più toccato del progetto, e il più lento da leggere per chi arriva.
- Stato: CONFERMATO (membri e intervalli trovati con uno script che toglie stringhe e commenti, controllati a campione).
- Proposta: passo 1, i campi in cima (20 minuti, rischio basso); passo 2, MainWindow.Debug.cs (15 minuti, rischio molto basso); poi un
  file per passo, dal più grande, circa 20 minuti l'uno: 5-6 ore in tutto, rischio basso.
- Verifica su Windows: quella comune sopra.

### C-20 UI/ComposeWindow.xaml.cs (2089 righe, 98 membri)

- Dove e che cosa:

| File nuovo | Contenuto | Intervalli | Righe |
|---|---|---|---|
| ComposeWindow.Send.cs | Parse, Message, ReadyAsync, allegato dimenticato, Invia, errori, Pianifica | 1414-1786, 1897-1991 | 468 |
| ComposeWindow.Drafts.cs | chiusura, bozze, salvataggio automatico, fine sessione | 195-558 | 361 |
| ComposeWindow.Recipients.cs | mailto, destinatari, suggerimenti, colleghi | 559-592, 727-765, 906-1140 | 305 |
| ComposeWindow.Attachments.cs | allegati, incolla, immagini, trascinamento | 1226-1413 | 187 |
| ComposeWindow.Templates.cs | modelli | 593-713 | 121 |
| ComposeWindow.Outbox.cs | Posta in uscita e Annulla invio | 1787-1896 | 113 |
| ComposeWindow.Original.cs | testo dell'originale | 714-726, 766-859 | 112 |
| ComposeWindow.Signatures.cs | firme, più `OnSignatureMenu` e `ReplaceSignature` da Format.cs | 860-905 | circa 80 |
| ComposeWindow.Format.cs (esistente) | grassetto, elenchi, link | 1141-1225 | +85 |

  Restano nel principale (circa 290 righe) costruttore, opzioni, Scarta, tastiera, e i campi condivisi (`signature`,
  `signatureLine`, `signatureText`, `quiet`, `autoSave`, `sendDone`, `outboxEntry`, `stopSend`, `leaving`, `initialTo`,
  `recipientsReady`, `asking`, `importance`, `readReceipt`, `deliveryReceipt`).
- Perché conta, Stato, Verifica: come C-19; CONFERMATO.
- Proposta: otto passi da 15 minuti, 2 ore; rischio basso.

### C-21 UI/ReadingView.xaml.cs (1831 righe, 83 membri)

- Dove e che cosa:

| File nuovo | Contenuto | Intervalli | Righe |
|---|---|---|---|
| ReadingView.Web.cs | WebView2: creazione, rilascio, errori, ambiente, guasti, preparazione, script, richieste e navigazione | 66-99, 826-922, 1020-1349 | 458 |
| ReadingView.Actions.cs | `Do`, consenso, Annulla iscrizione | 1350-1576 | 223 |
| ReadingView.Attachments.cs | anteprima, allegati, Salva tutti, link | 1618-1831 | 218 |
| ReadingView.Body.cs | corpo e Segna come letto | 644-825 | 185 |
| ReadingView.Render.cs | disegno, immagini, loghi | 923-1019, 1577-1617 | 138 |
| ReadingView.Conversation.cs | conversazioni ricordate | 360-455 | 91 |

  Restano nel principale (circa 515 righe) `ShowAsync`, `Prepare`, `Clear`, `ShowBlank`, tema, zoom e stampa, e i campi condivisi
  (`entries`, `provider`, `version`, `opening`, `junk`, `imagesOnce`, `actionsPage`, `shownPage`, `servedPage`, `previewBytes`,
  `pendingPreview`, `opened`, `clock`, `restoreScroll`, `pageLoaded`, `pageNavigation`, i dizionari statici delle conversazioni).
- Stato: CONFERMATO. Proposta: sei passi da 15 minuti, 1,5 ore; rischio basso.

### C-22 Core/GraphProvider.cs (1919 righe, 95 membri)

- Dove e che cosa: il file ha già i separatori `// ----` per area (righe 173, 288, 403, 625, 900, 978, 1073, 1092, 1133, 1169, 1231,
  1270, 1893) ed è già `partial`. Come per GmailProvider (`GmailProvider.Calendar.cs`, `GmailProvider.Contacts.cs`):

| File nuovo | Intervalli | Righe |
|---|---|---|
| GraphProvider.Drafts.cs (invio, bozze, allegati a pezzi) | 1018-1072, 1501-1892 | 452 |
| GraphProvider.Calendar.cs (calendario e inviti) | 1073-1091, 1270-1500 | 245 |
| GraphProvider.Messages.cs (elenchi, ricerca, nuovi, delta) | 403-624 | 222 |
| GraphProvider.Body.cs (corpo, conversazione, allegati) | 680-899 | 220 |
| GraphProvider.Actions.cs | 900-1017 | 118 |
| GraphProvider.Folders.cs | 288-402 | 115 |
| GraphProvider.Contacts.cs (contatti e colleghi) | 1169-1269 | 101 |
| GraphProvider.MailboxSettings.cs (risposte automatiche e regole) | 1092-1168 | 77 |
| GraphProvider.Categories.cs | 625-679 | 55 |
| GraphProvider.Shared.cs (caselle condivise) | 127-157, 255-273 | circa 50 |

  Restano nel nucleo (circa 265 righe) `me`, `gate` e `gates`, `testing`, `opening`, `expandAttachments`, `AttachmentsExpand`,
  `IsShared`, `MailboxGate`, `SendAsync` HTTP, `Result`, i campi statici.
- Stato: CONFERMATO. Proposta: dieci passi da 15 minuti, dal più piccolo; 2,5 ore; rischio molto basso (le prove di Graph finto coprono
  tutte le aree). È il punto da cui cominciare la divisione.

### C-23 I metodi più lunghi

Fuori dalle prove, in ordine di lunghezza; le proposte sono estrazioni, non spostamenti, quindi vanno in commit a parte, dopo C-19 ...
C-22. Stato: CONFERMATO per tutti.

| Metodo | Righe | Proposta | Lavoro, rischio |
|---|---|---|---|
| Core/DevTools.cs:189 `Press` | 318 | tabella dei comandi (C-04) | 2 ore, basso (solo Debug) |
| UI/MainWindow.xaml.cs:108 `StartAsync` | 288 | le righe 177-394 sono un solo blocco `#if DEBUG` che legge gli argomenti di prova: `RunTestArgumentsAsync()` in MainWindow.Debug.cs, chiamato dopo `await serverFolders`; attenzione alla variabile locale `only` (riga dentro il blocco) che oggi nasconde il campo `only` | 20 minuti, molto basso |
| Core/GraphProvider.cs:1547 `WriteDraftAsync` | 179 | `ReadFilesAsync` (1553-1564), `CreateDraftAsync` (1568-1595), `DraftPatch` funzione pura (1601-1644), `AddFilesAsync` e `SyncImagesAsync` (1648-1686, con un `AddAttachmentAsync` comune ai due blocchi quasi uguali 1665-1683 e 1688-1709), `CleanupFailedSaveAsync` (1688-1719); `known` va passato in un piccolo oggetto di stato | 1,5 ore, medio |
| UI/App.cs:75 `OnStartup` | 163 | registrazione di timer, notifiche e servizi in tre metodi | 45 minuti, basso |
| UI/ReadingPage.cs:555 `Document` | 163 | il foglio di stile in una costante o in un file di risorse | 30 minuti, basso |
| Core/MailHtml.cs:76 `Clean` | 148 | lasciare: è la pulizia dell'HTML, coperta dai 434 testi e dalle email vere del blocco 1 | |
| Core/GoogleContacts.Write.cs:171 `Merge` | 149 | un metodo per campo (nomi, posta, telefoni, indirizzo) | 1 ora, medio |
| UI/ComposeWindow.xaml.cs:64 costruttore | 129 | il gestore di `Closing` (145-191) e quello di `FromBox.SelectionChanged` (91-103) come metodi | 45 minuti, basso |
| Core/ActionQueue.cs:250 `ReplayAsync` | 123 | lasciare finché non c'è una prova di B-13 più ampia | |
| Core/GraphProvider.cs:736 `ReadBodyAsync` | 120 | `FetchMessageNodeAsync` (738-753), `DownloadCidImagesAsync` (772-789), `ToAttachments` (790-815), `InviteOfAsync` (818-833) | 1 ora, basso-medio |
| UI/MainWindow.xaml.cs:3815 `BuildMessageMenu` | 116 | la funzione locale `Build` diventa un metodo con un record di contesto; il ciclo va in `FillCustomMenu` (C-15) | 1 ora, medio |
| UI/ReadingView.xaml.cs:1074 `InitWebAsync` | 107 | le tre lambda (DOMContentLoaded 1121-1137, NavigationCompleted 1138-1165, SizeChanged 1166-1172) come metodi con il controllo passato per argomento (devono restare legate a `control`) | 1 ora, medio |
| UI/ReadingView.xaml.cs:650 `FetchBodyAsync` | 103 | `ObtainBodyAsync` (660-697), `StartPec` (704-711), `CleanHtmlInBackgroundAsync` (714-728) | 45 minuti, basso |
| Core/GraphProvider.cs:1759 `UploadLargeAsync` | 96 | `PutChunkAsync` (con la ripetizione per 429 e 503), `AbortUploadAsync` | 45 minuti, basso |
| UI/ReadingView.xaml.cs:274 `ShowAsync` | 85 | `BeginOpen` (279-293), `ShowConversationWhenReadyAsync` (334-356); il controllo `v != version` deve restare a ogni ripresa | 45 minuti, medio |
| UI/MainWindow.xaml.cs:3525 `UndoAsync` | 83 | `UndoPartAsync` (3532-3566), `ReselectRestored` (3584-3605); insieme a C-56 | 40 minuti, basso |
| UI/MainWindow.xaml.cs:4142 `CheckSnoozedAsync` | 80 | `WakeOneAsync` (4166-4205) | 30 minuti, basso |
| UI/ComposeWindow.xaml.cs:1642 `SendMessageAsync` | 80 | `SendCoreAsync` (1672-1687); i cinque `catch ... when` restano dove sono, l'ordine conta | 30 minuti, basso |

Verifica su Windows: `--prova-bozze`; per WriteDraftAsync e UploadLargeAsync `UploadGraph` e `FailAfterPosts`; per InitWebAsync e
ShowAsync le misure "clic, pagina mostrata" di `lampi.ps1` prima e dopo.

---

## 4. Robustezza

Conteggi (CONFERMATI con grep, codice di produzione): 34 metodi `async void` con nome, di cui 15 non sono gestori di eventi, più circa
93 lambda `async` agganciate a eventi o timer; 84 righe `_ = ...Async(`, 12 `Task.Run` scartati, 7 `ContinueWith`; 98 `catch` che non
registrano né rilanciano (42 vuoti su una riga): PdfWorker 9, Store 9, Backup 9, ReadingView 8, ImapProvider 7, GraphProvider 4,
GmailProvider 4, Calendar 3, GoogleAuth 3, gli altri file 2 o meno. Quasi tutti sono voluti (Disconnect, temporanei, ripieghi spiegati
nei commenti); quelli che contano sono sotto.

### C-24 Nessun gestore per le eccezioni fuori dal Dispatcher

- Dove: UI/App.cs:111 (`DispatcherUnhandledException`, l'unico gestore); callback di `System.Threading.Timer` in Core/AddressBook.cs:516
  e Core/PdfWorker.cs:439; le 84 righe `_ = ...Async(`, per esempio UI/App.cs:169 (`_ = main.StartAsync()`), UI/MainWindow.xaml.cs:2009,
  2173, 4516, 4517, 4793 (`_ = ReloadAsync()`), 558 e 2991 (`ShowInReaderAsync`), UI/App.cs:1320, 1327, 1337, 1342 (azioni delle
  notifiche).
- Che cosa: `AppDomain.CurrentDomain.UnhandledException` e `TaskScheduler.UnobservedTaskException` non compaiono nel repository. Un Task
  scartato che fallisce senza un `catch` interno sparisce senza una riga nel registro; un'eccezione nella callback di un Timer chiude il
  processo senza riga nel registro.
- Perché conta: la diagnosi dell'autore passa tutta dal registro; un elenco che non si ricarica o un'azione di una notifica che non parte non
  lasciano traccia.
- Stato: CONFERMATO che i gestori mancano; SOSPETTO quali chiamate falliscano davvero in silenzio (molte hanno un `catch` interno,
  controllate a campione).
- Proposta: in `Program.Main` i due gestori con `Log.Error` (il registro ha un suo lucchetto, Store.cs:1167) e `SetObserved()`; non
  avvisano in tempo reale, ma lasciano la riga. 20 minuti, rischio basso.
- Verifica su Windows: un comando di prova che scarta un Task che fallisce, poi `GC.Collect()` e `GC.WaitForPendingFinalizers()`: la
  riga deve comparire nel registro.

### C-25 Avvio: se `LoadAccountsAsync` fallisce, chi aspetta gli account resta appeso

- Dove: UI/MainWindow.xaml.cs:930-990 (`accountsListed` a 953, `accountsReady` a 989, senza try/finally); UI/App.cs:169; chi aspetta:
  `OpenFromNotificationAsync` (MainWindow.xaml.cs:1066), `FlagReminderActionAsync` (MainWindow.Flags.cs:197),
  `NotificationActionAsync` (MainWindow.Notifications.cs:22).
- Che cosa: un'eccezione fra 939 e 989 (`SetProviders`, `UpdateQueued`, `MailCache.LoadFolders`, `SetFolders`, `UpdateFavorites`,
  `SelectStart`) lascia le due `TaskCompletionSource` aperte per sempre. Con l'avvio nascosto l'eccezione non arriva a nessuno (C-24).
- Perché conta: Missiva avviata da Windows per un'azione di una notifica non fa l'azione e non esce più.
- Stato: CONFERMATO che manca la protezione; SOSPETTO che un'eccezione lì capiti davvero.
- Proposta: try/catch con `Log.Error` in `StartAsync`; in `LoadAccountsAsync` un `finally` che chiude le due `TaskCompletionSource`
  (con `TrySetResult`, o `TrySetException` e chi aspetta lo gestisce). 30 minuti, rischio basso.
- Verifica su Windows: un'eccezione forzata in Debug in `SelectStart`, con `--nascosta` e "toast ... action=flag": il processo deve uscire
  e il registro deve dire perché.

### C-26 Azioni delle notifiche: l'uscita si salta se l'azione fallisce

- Dove: UI/App.cs:1359-1378 (`CountActionAsync`).
- Che cosa: il controllo "finite tutte, esci" sta dopo il `finally`, non dentro: se `action()` lancia, il contatore scende ma l'uscita
  non si controlla, e l'eccezione del Task scartato (App.cs:1320, 1327) si perde.
- Perché conta: Missiva avviata nascosta per un Elimina o un Segna, con "resta nell'area di notifica" spento, resta aperta e invisibile.
- Stato: CONFERMATO; raro, perché i metodi chiamati (MainWindow.Notifications.cs:165, 217, 241) hanno un `catch` interno.
- Proposta: `catch (Exception ex) { Log.Error(...); }` e il controllo dell'uscita dentro il `finally`. 10 minuti, rischio basso.
- Verifica su Windows: `--nascosta` con un errore finto in `FlagFromNotificationAsync` (solo Debug), "toast ... action=flag": il
  processo deve uscire.

### C-27 RuleWindow chiama il provider da un thread del pool

- Dove: UI/RuleWindow.cs:252-261 (`_ = Task.Run(async () => await provider.GetCategoriesAsync() ...)`); Core/Categories.cs:23
  (`CategoryPalette.Known`, un `Dictionary` statico senza lucchetto).
- Che cosa: i provider sono pensati per riprendere sul thread dell'interfaccia. Da un thread del pool Graph legge e scrive
  `CategoryPalette.Known` e `Store.Settings.LocalCategories`, Gmail può scrivere `userLabels` (GmailProvider.cs:286), e se serve un nuovo
  accesso `MicrosoftAuth.Owner()` legge `WindowInteropHelper.Handle` da un thread che non è quello della finestra (WPF lancia
  InvalidOperationException). Tutto finisce nel `catch` "categorie non lette".
- Perché conta: lo stato condiviso viene toccato da due thread senza lucchetto; il `Task.Run` non serve, la chiamata è già asincrona.
- Stato: CONFERMATO che la chiamata parte dal pool; SOSPETTI gli effetti.
- Proposta: togliere `Task.Run` (resta `_ = LoadCategoriesAsync()` sul thread dell'interfaccia). 5 minuti, rischio basso.
- Verifica su Windows: Crea regola con un account Gmail e uno Microsoft: le categorie compaiono, nessun errore nel registro.

### C-28 Un solo lucchetto dei token Microsoft, tenuto con la finestra di accesso aperta

- Dove: Core/MicrosoftAuth.cs:82 (`TokenGate`), 164-175 (`TokenAsync`), 196-212, 241-246 (`AcquireTokenInteractive` dentro
  `FreshTokenAsync`, chiamato sotto il lucchetto), 302-332, 343-362.
- Che cosa: finché la finestra account di Windows resta aperta per un account, ogni richiesta di token degli altri account Microsoft con
  il token scaduto aspetta; la query delta (App.cs:872) e il controllo dei nuovi messaggi aspettano finché `StuckCheck` li lascia andare
  dopo 2 minuti.
- Perché conta: con due account Microsoft (lavoro e personale) un accesso lasciato a metà ferma le notifiche dell'altro.
- Stato: CONFERMATO l'ambito del lucchetto; SOSPETTO l'impatto (serve un secondo account con il token scaduto).
- Proposta: un lucchetto per account (dizionario di `SemaphoreSlim`); `AppGate` resta unico e l'ordine resta TokenGate poi AppGate. Il
  lucchetto unico è stato messo apposta per non aprire due finestre insieme: va tenuto un lucchetto unico solo per la parte interattiva.
  1-2 ore, rischio medio.
- Verifica su Windows: due account Microsoft, finestra di accesso di uno lasciata aperta, nel registro le richieste dell'altro devono
  partire.

### C-29 Processo dei PDF: dopo i 15 s, `Stop` e la richiesta in corso si incrociano

- Dove: Core/PdfWorker.cs:376-397 (e il commento a 372-374, "Start e Stop non si incrociano").
- Che cosa: scaduto il tempo, `await Task.Run(Stop)` gira mentre `work` (`Start()` e la richiesta) è ancora vivo su un altro thread; poi
  il gate si rilascia, e la richiesta dopo può fare `Start()` mentre il vecchio `work` tocca ancora `process`, `job`, `writer`, `reader`.
- Perché conta: due avvii del processo nello stesso momento, o un lettore chiuso sotto una lettura.
- Stato: SOSPETTO: serve un `Start` o una lettura ferma oltre 15 s.
- Proposta: dopo `Stop` aspettare la fine di `work` con un tetto (`await work.WaitAsync(TimeSpan.FromSeconds(5))` in un try) prima di
  rilasciare il gate. 20 minuti, rischio basso.
- Verifica su Windows: in `PdfTestsAsync` un processo finto che non risponde, poi una seconda anteprima subito.

### C-30 Contatti: lucchetto tenuto durante DPAPI e la scrittura del file

- Dove: Core/Contacts.cs:186-198 (`Save`), 246-262 (`SetRemote`); chi aspetta dal thread dell'interfaccia: Contacts.cs:163 (`Local`),
  269 (`Remote`), 323 (`ForSuggestions`, chiamata da `AddressBook.Suggest`, AddressBook.cs:221, a ogni lettera nel campo A).
- Che cosa: è lo stesso difetto già corretto per la rubrica (audit del 7 ottobre, B-14). Inoltre `ForSuggestions` copia `Local` fuori
  dal lucchetto, anche da `AddressBook.Preload`, che gira in `Task.Run`.
- Perché conta: ESITO.md misura contatti.dat in 10-46 ms; una lettera nel campo A può aspettare tanto durante un salvataggio.
- Stato: CONFERMATO il codice; SOSPETTO il tempo d'attesa reale.
- Proposta: come `AddressBook.Save`: sotto il lucchetto solo il JSON e la copia della lista, fuori cifratura e file con copie numerate.
  1 ora, rischio basso.
- Verifica su Windows: Importa 5000 contatti e intanto scrivere nel campo A: nessuna riga "interfaccia lenta" di StallLog.

### C-31 CancellationToken non passati: quali sono voluti e quali no

- Dove (CA2016): Core/GmailProvider.cs:188, 190, 196, 216; Core/ImapProvider.cs:165, 274, 282; Core/PdfWorker.cs:381, 385, 395;
  Core/GoogleAuth.cs:444; Core/EmlProvider.cs:97; UI/ReadingView.xaml.cs:719, 1451.
- Che cosa:
  - voluti: `DisconnectAsync` dentro un `catch` (GmailProvider.cs:216, ImapProvider.cs:165, 282), `Stop` del processo dei PDF che deve
    girare comunque (PdfWorker.cs:381-395), il ciclo di GoogleAuth.cs:444 che usa `stop.Token` dentro, EmlProvider.cs:97 sincrono;
  - da cambiare: GmailProvider.cs:188, 190, 196 e ImapProvider.cs:274 non passano il token a `ConnectAsync` e `AuthenticateAsync`. La
    scelta scritta a GmailProvider.cs:162-165 (un comando MailKit interrotto a metà chiude la connessione) non vale per un collegamento
    che non è ancora in piedi; oggi una richiesta già superata tiene il Gate della connessione fino al timeout (120 s in Gmail, C-09);
  - UI/ReadingView.xaml.cs:719: la pulizia dell'HTML in `Task.Run` gira anche per un messaggio già superato (fino a 75-84 ms per una
    newsletter da 1 MB secondo ESITO.md).
- Perché conta: con la rete lenta, cliccare più messaggi di fila accumula collegamenti che nessuno aspetta più.
- Stato: CONFERMATO.
- Proposta: il token solo a `ConnectAsync` e `AuthenticateAsync`; `Task.Run(..., cancel)` a ReadingView.xaml.cs:719; un commento
  `// token non passato: ...` sugli altri, così l'analizzatore non li ripropone. 30 minuti, rischio basso.
- Verifica su Windows: con il server finto lento o `--errore-rete`, dieci clic di fila, poi le righe "attesa della connessione".

### C-32 `catch` vuoti che nascondono qualcosa

- Dove: Core/MailCache.cs:107-110, Core/Calendar.cs:471-474, Core/Contacts.cs:316 (cancellazione delle copie di un account rimosso);
  Core/GoogleAuth.cs:400-410 (revoca del token di Google); Core/MicrosoftAuth.cs:383 (`RemoveAsync` di MSAL).
- Che cosa: le cancellazioni delle copie non lasciano traccia se non riescono (invece `BodyCache.Delete`, Offline.cs:575-580, lo
  registra). Nella revoca di Google la risposta di `Http.PostAsync` non si chiude né si controlla, e il `catch` è vuoto: se la revoca
  non riesce, il token di aggiornamento resta valido presso Google e nessuno lo sa. LEGGIMI.md promette la revoca alla rimozione
  dell'account.
- Perché conta: dati di un account rimosso che restano sul disco, un accesso che resta valido.
- Stato: CONFERMATO.
- Proposta: `Log.Error` nei tre `catch` delle cancellazioni e in quello di MSAL; per la revoca `using var response`, controllo di
  `IsSuccessStatusCode`, una riga nel registro (senza il token). 20 minuti, rischio nullo.
- Verifica su Windows: rimuovere un account con un file della copia tenuto aperto, poi leggere il registro e la cartella `cache`.

### C-33 `async void` senza protezione

- Dove: i 15 che non sono gestori di eventi: UI/ReadingView.xaml.cs `Prepare` 456, `SetRead` 801, `Render` 923, `Do` 1350;
  UI/MainWindow.xaml.cs `MarkArrived` 2879, `OnInvite` 4569, `OpenEml` 4596; UI/PeopleView.Sections.cs:277 `AllowFor`;
  UI/PeopleView.cs:711 `Import`; UI/CalendarView.cs:196 `NewEventWhenReady`; UI/App.cs `OnNetworkBack` 297, `ListenForMailto` 551,
  `OnInboxEvent` 833, `RecountSoon` 1118; UI/ComposeWindow.xaml.cs:686 `UseTemplateWhenReady`.
- Che cosa: senza try: `Render` (Navigate su WebView2 dopo un await), `Do`, `OnInboxEvent`, `NewEventWhenReady` (`NewEvent` fuori dal
  try), `UseTemplateWhenReady`. Un'eccezione dà l'avviso generico (una volta per sessione) senza dire quale azione è fallita. Per
  `UseTemplateWhenReady`: se `FillRecipientsAsync` lancia, `recipientsReady` (ComposeWindow.xaml.cs:122-123) non si chiude mai e il
  modello non arriva, in silenzio.
- Perché conta: nessuna chiusura dell'app (tutti girano sul thread dell'interfaccia, e il gestore del Dispatcher li prende), ma avvisi
  senza contesto e uno stato fermo.
- Stato: CONFERMATO.
- Proposta: try/catch con un messaggio specifico in `Do`, `Render`, `OnInboxEvent`, `NewEventWhenReady`; `recipientsReady.TrySetResult`
  in un `finally` di `FillRecipientsAsync`. 30 minuti, rischio basso.
- Verifica su Windows: `MISSIVA_ERRORE_WEBVIEW` e i comandi del riquadro ("sole", "immagini"); Rispondi con modello con un destinatario
  non valido.

### C-34 domini.dat non salvato all'uscita

- Dove: Core/SenderLogos.cs:244-250 (`SaveSoonAsync`, 5 s di attesa); UI/App.cs:320-327 (`OnExit`, che chiama `Store.Flush()` ma non
  salva i domini).
- Che cosa: i domini visti e i segni "autenticato" degli ultimi 5 s prima di Esci si perdono.
- Perché conta: poco (si rifanno al prossimo messaggio), ma l'audit del blocco 2 ha messo i loghi sotto la condizione "dominio noto da
  14 giorni in due giorni diversi": un giorno perso conta.
- Stato: CONFERMATO.
- Proposta: un `SenderLogos.Flush()` in `OnExit`. 10 minuti, rischio nullo.
- Verifica su Windows: aprire un messaggio di un dominio nuovo e uscire subito; al riavvio domini.dat lo contiene.

### C-35 `throw ex` in ReadingView

- Dove: UI/ReadingView.xaml.cs:693 (`?? throw ex`, avviso CA2200).
- Che cosa: rilancia l'errore di rete perdendo la pila originale; nel registro di `LoadBodyAsync` resta solo la riga 693.
- Perché conta: solo diagnosi.
- Stato: CONFERMATO.
- Proposta: `ExceptionDispatchInfo.Capture(ex).Throw()` quando il corpo non c'è. 5 minuti, rischio nullo.
- Verifica su Windows: `--errore-rete 10054` su un messaggio non in copia: la pila nel registro arriva al provider.

### C-36 WebView2 delle finestre dei messaggi chiuse

- Dove: UI/ReadingView.xaml.cs:112-117 e `Leave` 125-130.
- Che cosa: chiudendo una finestra del messaggio non si chiama `ReleaseWeb` né `web.Dispose`; il rilascio dipende da quello che fa il
  controllo WPF di WebView2 alla distruzione del suo HWND. Inoltre `App.ThemeChanged` si stacca solo se `Loaded` è scattato almeno una
  volta.
- Perché conta: processi WebView2 che restano (decine di MB l'uno) dopo aver aperto e chiuso molte finestre.
- Stato: SOSPETTO.
- Proposta: misurare; se i processi restano, `ReleaseWeb()` in `host.Closed`. 10 minuti più la misura, rischio basso.
- Verifica su Windows: aprire e chiudere dieci finestre di messaggi, poi `memoria.ps1 -Detail`: il numero di processi WebView2 deve
  tornare quello di prima.

---

## 5. Avvisi del compilatore

### C-37 Gli avvisi del compilatore, uno per uno

| Avviso | Dove | Difetto vero? | Proposta |
|---|---|---|---|
| CS8602 | Core/GmailProvider.cs:300 (`CreateCategoryAsync`), 325 (`CreateFolderAsync`) | Sì, raro: `CreateAsync` di MailKit restituisce null se la LIST dopo CREATE non trova la cartella; IMAP lo gestisce già (ImapProvider.cs:437, `?? throw new MailException`), Gmail no. Ne esce "Object reference not set..." e, per la categoria, un colore salvato con chiave nulla | copiare la correzione di IMAP; 15 minuti |
| CS8604 | Core/GmailProvider.cs:332 (`RenameFolderAsync`) | Sì, raro: `f.ParentFolder` è null se il genitore non è in cache; IMAP usa `f.ParentFolder ?? client.GetFolder(PersonalNamespaces[0])` (ImapProvider.cs:446) | come IMAP; 5 minuti |
| CS8602 | Core/GmailProvider.cs:1338, Core/ImapProvider.cs:1549 (`content.Stream.Length`) | No: gli allegati vengono da `BuildAsync`, che crea sempre il contenuto | `?.` o `!`; spariscono con C-11 |
| CS8604 | UI/MainWindow.xaml.cs:1086 (`ShowAsync(..., summary, ...)`) | No: `summary` è stato controllato da `(summary ??= FindNotified(...)) != null` nella stessa condizione; il compilatore non segue `??=` dentro `&&` | `is { } found` |
| CS8604 | UI/MainWindow.xaml.cs:2128 (`favorites.Contains(item)`) | No: `Contains(null)` restituisce false | `item != null &&` |
| CA2200 | UI/ReadingView.xaml.cs:693 | Solo diagnosi | C-35 |
| CS0649 (Release) | UI/RulesView.cs:101 | No | C-05 |
| CS0252 | Core/ProvaBozze.ContattiGoogle.cs:431 | No: il riferimento confrontato è proprio quello voluto (`writeNote` viene da `OfType<SectionNote>().SingleOrDefault()` sulla stessa lista) | `ReferenceEquals`, come alle righe 430 e 434; 1 minuto |
| CS8602 | Core/ProvaBozze.cs:4654, 4717 (`FakeImap.Fetch`) | Sì, nel server finto: vedi C-45 | |

- Stato: CONFERMATO per tutti (letti nel codice).
- Proposta: un commit che porta le build Debug e Release a zero avvisi, poi `<TreatWarningsAsErrors>` solo per gli avvisi di nullità
  (`<WarningsAsErrors>nullable</WarningsAsErrors>`), così un avviso nuovo non passa inosservato. 1 ora in tutto, rischio basso (i due di
  Gmail cambiano solo il messaggio d'errore in un caso raro).
- Verifica su Windows: `dotnet build` Debug, Release e `MissivaProva=true` con 0 avvisi; `--prova-bozze`.

---

## 6. Numeri e soglie

### C-38 Soglie scritte in più posti con lo stesso significato

- Dove e che cosa (stesso valore e stesso significato in più punti, non legati nel codice):

| Valore | Dove | Significato |
|---|---|---|
| 300.000 byte | Core/GmailProvider.cs:729, Core/ImapProvider.cs:66 (`SmallMessage`) | messaggio scaricato intero |
| 5 MiB | Core/GmailProvider.cs:1382, Core/ImapProvider.cs:1590 (`KeepAttachment`); UI/ComposeWindow.xaml.cs:521 (`Heavy`, che CLAUDE.md lega ai 5 MB) | allegati della bozza tenuti in memoria; salvataggio automatico rado |
| 5.000.000 byte | UI/ReadingView.xaml.cs:1649 | "5 MB" dell'anteprima, ma in decimale |
| 20 | Core/GmailProvider.cs:114, Core/ImapProvider.cs:65 (`BodyCacheSize`) | corpi in memoria |
| 50 | Core/GmailProvider.cs:19, Core/ImapProvider.cs:24, Core/GraphProvider.cs:32 (`PageSize`) | messaggi per pagina |
| 5 s | Core/GmailProvider.cs:53, Core/ImapProvider.cs:52 | LOGOUT in sottofondo |
| 5 s | Core/GraphProvider.cs:212, 1811 | tetto di Retry-After |
| 30 s | Core/GoogleApi.cs:21, 25; Core/GoogleAuth.cs:109 | timeout HTTP di Google |
| 10 minuti | Core/GmailProvider.Calendar.cs:29, UI/ManageAccountWindow.cs:435, UI/AccountWindow.xaml.cs:558 | attesa dell'accesso nel browser |
| 45 s | UI/ManageAccountWindow.cs:326, UI/AccountWindow.xaml.cs:512 | prova del server IMAP |
| 15 s | Core/Unsubscribe.cs:129, 143 | richiesta di Annulla iscrizione |
| 2.000.000 | UI/MainWindow.xaml.cs:4468 (`SourceLimit`), UI/AttachmentPreview.cs:435 | "2 MB di testo" |
| 2000 | Core/Rules.cs:615 | `RulesSeen`, scritto due volte nella stessa riga |

- Perché conta: la prossima correzione di uno solo dei due valori crea una differenza che nessuno ha scelto (è già successo con il
  timeout, C-09).
- Stato: CONFERMATO.
- Proposta: le costanti di Gmail e IMAP in un posto solo (nella base di C-08, o in una classe `MailLimits`); per gli altri una costante
  per file con un nome. Da NON unire, perché il valore coincide ma il significato no: i 14 giorni (`Woken` in MainWindow.xaml.cs:154 e
  `KnownAfter`, `Retry` in SenderLogos.cs:41, 44), i 2000 di `SummaryCache`, `notified` e `RulesSeen`, i 300.000 e 2.000.000 delle
  immagini di Graph (GraphProvider.cs:782). 1-2 ore, rischio nullo.
- Verifica su Windows: `dotnet build`; `--prova-bozze` invariata.

### C-39 Soglie che devono restare coerenti fra loro, ma sono scritte a mano

- Dove e che cosa:
  - controllo periodico: `poll.Interval` 2 minuti (UI/App.cs:220), `SafetyNet` 100 s (937), che deve stare sotto il primo (lo spiega il
    commento a 932-936), `StuckCheck` 2 minuti (921), che oggi coincide con il timeout di Gmail (C-09), `DeltaFresh` 2 minuti e
    `deltaTimer` 30 s (937, 816);
  - copia sul disco: `BodyCache.AccountLimit` 200 MiB (Core/Offline.cs:144) e `OfflineCopy.Full` 180 MiB (606), che deve stare sotto (lo
    spiega il commento a 598-602, altrimenti `Trim` toglie e la copia rimette a ogni giro); `MaxAge` e `OfflineCopy.Days`, 30 giorni (146,
    605), trattati dal commento come la stessa finestra;
  - Graph: `UploadChunk` 1,25 MiB (GraphProvider.cs:1733) deve essere multiplo di 320 KiB e stare nei 60 s di `Http.Timeout` (39) e
    `DownloadIdle` (861);
  - ricerca: `ReuseSearch` 60 s (ImapProvider.cs:589) e `Fresh` 30 s (MainWindow.Suggestions.cs:23), due finestre di riuso della stessa
    ricerca.
- Perché conta: chi cambia un numero non sa che un altro dipende da lui.
- Stato: CONFERMATO.
- Proposta: legarli nel codice: `SafetyNet = PollInterval - TimeSpan.FromSeconds(20)`, `StuckCheck = ImapTimeout + 30 s`,
  `OfflineCopy.Full = BodyCache.AccountLimit * 9 / 10`, `OfflineCopy.Days = (int)BodyCache.MaxAge.TotalDays`, un commento con il conto
  per `UploadChunk`. 30 minuti, rischio nullo (i valori di oggi non cambiano, tranne `StuckCheck` se si sceglie un altro timeout).
- Verifica su Windows: `--prova-bozze`; nel registro "controllo ... fermo da" dopo una sospensione.

---

## 7. Le prove

Numeri (CONFERMATI, letti nel codice): 801 chiamate a `Check(` nel sorgente (circa 870 righe all'esecuzione, perché alcune sono nei
cicli): ProvaBozze.cs 339, ProvaBozze.Audit.cs 279, ContattiGoogle 34, BarraAccount 28, SenzaRete 26, Regole 18, Persone2 14, Persone3
14, SchedaMittente 14, Idle 13, Persone 11, Formato 6, Ricerca 5. Attese fisse (costanti di `Task.Delay` e `Thread.Sleep` sommate con le
ripetizioni): circa 22,7 s per giro (Audit 20,0 s, ProvaBozze.cs 2,25 s, Idle 0,45 s), più circa 3 s che fanno parte di una prova.
Le soglie di tempo: 47 attese e 36 Stopwatch in Audit, 9 e 7 in ProvaBozze.cs.

### C-40 Un solo try per tutta la suite, e i nomi sconosciuti di `--prova-audit`

- Dove: Core/ProvaBozze.cs:28-92 (`Run`: 60 gruppi in fila dentro un solo try/catch), 1338-1342 (`Check`); Core/ProvaBozze.Audit.cs:24-60
  (`RunAudit`, una catena di `if` sul nome).
- Che cosa: un'eccezione in un gruppo interrompe tutti i gruppi dopo; il rapporto dice "1 controlli non passati" e centinaia non sono
  girati. Un nome di `--prova-audit` sconosciuto (per esempio "velocità" con l'accento) finisce nel ramo `else` e fa girare altri gruppi,
  con "tutto bene" alla fine. I gruppi di ProvaBozze.cs (Graph, Gmail, IMAP, PEC, calendario) non si avviano da soli. Il rapporto non dice
  quanto dura ogni gruppo.
- Perché conta: un errore in un gruppo nasconde lo stato degli altri; un nome sbagliato dà un falso "tutto bene".
- Stato: CONFERMATO.
- Proposta: una tabella `nome -> gruppo` usata da `Run` e `RunAudit`; ogni gruppo nel suo try con un aiuto `Group(nome, azione)` che
  scrive anche la durata; un nome sconosciuto esce con errore. Mezza giornata, rischio basso.
- Verifica su Windows: un gruppo che lancia un'eccezione apposta: i gruppi dopo girano; `--prova-audit r.txt sconosciuto` esce con 1.

### C-41 "Ricerca che nessuno aspetta più": 6 SEARCH invece di 5

- Dove: Core/ProvaBozze.cs:3062-3073 (dentro `ImapAsync`); produzione in Core/ImapProvider.cs:696-716, 747-760.
- Che cosa: la prova avvia una ricerca, la annulla, aspetta 300 ms, ne fa una uguale, e chiede che il totale dei SEARCH sia
  `<= Folders.Count - 2`. `Waiting` scende a 0 solo nel `finally` della continuazione di `WaitAsync(cancel)`, su un thread del pool;
  intanto `FindEverywhereAsync` ha già passato `StopIfUnwanted()`, fa LIST in meno di 1 ms sul server finto e può entrare nella prima
  cartella (EXAMINE e un SEARCH). Il codice fa ciò che promette ("si ferma fra una cartella e l'altra"): è la prova che pretende zero
  SEARCH dalla ricerca abbandonata.
- Perché conta: è la caduta "una volta su tre" scritta in CLAUDE.md.
- Stato: CONFERMATO.
- Proposta: dopo `await abandoned` aspettare la fine vera della ricerca interna (un gancio di Debug sul Task di `everywhere`), contare a
  parte (al più 1 SEARCH dalla ricerca abbandonata, poi `Commands.Clear()`, poi esattamente `Folders.Count - 2`). In alternativa un
  `TaskCompletionSource` nel server finto che trattiene la risposta a LIST finché la prova non ha visto l'annullamento. 1-2 ore, rischio
  basso.
- Verifica su Windows: `--prova-bozze` venti volte di fila, zero cadute.

### C-42 S-01 e gli altri controlli con soglie di tempo

- Dove: Core/ProvaBozze.Audit.cs:3551-3559 (S-01: `waited is > 900 and < 1900`, `burned < 200` con `Process.TotalProcessorTime` di tutto
  il processo in 400 ms); altre soglie: Audit.cs:113 (< 500), 130 (< 1000), 1497 (< 15 ms), 1980 (mediana < 10 ms), 2786 (< 1500), 3336
  (< 300), 3444 (< 1500), 3547 (< 4000), 3595 (< 5000), 5689, 5899 (< 100); ProvaBozze.cs:1441 (PDF da 500 pagine < 500 ms), 2329, 3873,
  3923. Gare: V-21 (Audit.cs:1601-1621, `Thread.Sleep(30)` poi annullamento), `UnreadSync` (Audit.cs:797-826, entro 150 ms), le
  finestre "nessuna connessione" di 300 e 200 ms (Audit.cs:3434, 2987).
- Che cosa: il tempo di CPU di S-01 conta anche il GC, i task rimasti dei server finti e il campionatore di S-02; le soglie di 15 ms e
  della mediana sotto 10 ms dipendono dalla macchina e dal carico. Le finestre "nessuna connessione" non vedrebbero una connessione
  tardiva: possono passare per il motivo sbagliato.
- Perché conta: falsi "NO" sotto carico (dopo una compilazione, a batteria: CLAUDE.md dice fino al 40% in più) e falsi "ok".
- Stato: CONFERMATO che sono a tempo; SOSPETTO che cadano (ESITO.md non registra cadute oltre a C-41).
- Proposta: S-01 con il tempo di CPU del solo thread STA (un contatore di giri del ciclo che deve smettere di crescere) e una scadenza
  larga; le soglie che provano "non quadratico" o "non aspetta 60 s" moltiplicate per 5-10 rispetto alla misura; quelle di 15 ms e 10 ms
  sostituite da conteggi (il chiamante non tiene il lucchetto, il Task non è ancora completato); per le gare un orologio iniettabile in
  `UnreadSync` e `InboxIdle.OwnEcho`, e per "nessuna connessione" aspettare la fine del lavoro (`SenderLogos.IsBusy` falso) prima di
  contare. Mezza giornata, rischio: perdere sensibilità se si allarga troppo.
- Verifica su Windows: la suite dieci volte, anche subito dopo una compilazione.

### C-43 Controlli che possono passare per il motivo sbagliato

- Dove e che cosa:
  - Core/ProvaBozze.cs:3167-3170: `log.Contains("... cambiato, con una password nuova")` legge tutto `missiva.log` di MISSIVA_DATA, che
    resta da un giro all'altro e ruota solo a 1 MB (Store.cs:1174): una riga di un giro di prima fa passare il controllo anche se oggi
    non si scrive più. Lo stesso schema, più debole, nelle 11 letture `TakeLast(N)` del registro: ProvaBozze.cs:188, 368, 568, 725,
    1674, 1806, 1892; Persone.cs:86; Persone2.cs:94; Regole.cs:135; ContattiGoogle.cs:325.
  - ProvaBozze.cs:3398-3403: LookAlike con nomi fissi nella rubrica di MISSIVA_DATA, che resta fra i giri (il gemello Audit.cs:2656 usa un
    segno casuale apposta).
  - ProvaBozze.cs:3154: `catch (Exception) { interrupted = true; }`: anche una NullReferenceException fa passare "Annulla durante la prova
    del server".
  - ProvaBozze.cs:1447-1449: `truncated != null` è sempre vero (il try e il catch assegnano tutti e due un testo).
  - ProvaBozze.cs:4612-4614: il server IMAP finto risponde `OK` a ogni comando che non conosce (DELETE, RENAME, SUBSCRIBE, CLOSE,
    UNSELECT, STORE senza UID), mentre il commento della classe (4213-4214) dice "un comando che il server non ha riceve BAD".
- Perché conta: sono "ok" che non dimostrano nulla, e il server accomodante renderebbe a vuoto le prove future sulle cartelle (C-47).
- Stato: CONFERMATO.
- Proposta: un pozzo del registro in memoria per le prove (`Log.TestSink`, solo Debug) con un segno d'inizio al posto dei file; un segno
  casuale per LookAlike; `catch (OperationCanceledException)` o la `MailException` attesa; il controllo vero sul testo di `truncated`;
  `default: BAD` con i comandi usati elencati. Mezza giornata, rischio basso.
- Verifica su Windows: togliere temporaneamente la riga del registro di ManageAsync: il controllo deve cadere anche con un registro vecchio.

### C-44 Ganci statici di prova mai rimessi a posto

- Dove: Core/GraphProvider.cs:98 (`UseTestHandler`: sostituisce un HttpClient statico e mette `testing = true`, chiamato 27 volte, mai
  rimesso); lo stesso per `GoogleApi`, `MailAutoconfig`, `ImapProvider.TestPlain` (24 volte vero, 25 falso), `TestCalendarConsent`,
  `TestSettingsConsent`, `Network.TestOffline`, i ganci di `BodyCache`.
- Che cosa: una prova che dimentica di installare il suo gestore riceve quello del gruppo prima; un "nessuna richiesta" contato sulla
  lista nuova passa a vuoto.
- Perché conta: falsi "ok" che dipendono dall'ordine dei gruppi, e impedisce di far girare i gruppi in parallelo.
- Stato: CONFERMATO lo schema; SOSPETTO che oggi una prova ne approfitti.
- Proposta: ogni gancio restituisce un `IDisposable` che rimette il valore di prima (`using var _ = GraphProvider.UseTestHandler(...)`);
  `Group()` di C-40 controlla alla fine che siano tutti a riposo. 2-3 ore, rischio basso.
- Verifica su Windows: la suite con lo stesso numero di controlli; i gruppi in ordine inverso danno lo stesso esito.

### C-45 Server IMAP finto: eccezioni perse e una connessione muta per 60 s

- Dove: Core/ProvaBozze.cs:4654 e 4717 (`FakeImap.Fetch`: `a.Content.Stream.Length`, `part.Content.WriteTo(data)`, avvisi CS8602), 4711
  (cast `(MimePart)` su un allegato che può essere `MessagePart`), 4616 (`ServeAsync` cattura solo `IOException`), 4359
  (`_ = ServeAsync(...)`).
- Che cosa: con un allegato senza contenuto o un .eml allegato il server finto lancia NullReferenceException o InvalidCastException;
  l'eccezione si perde nel Task scartato, la connessione resta muta e il client aspetta il suo timeout di 60 s (ImapProvider.cs:32).
- Perché conta: non un falso "ok", ma un falso "NO" dopo un minuto, senza la causa.
- Stato: CONFERMATO.
- Proposta: gestire `Content` null e `MessagePart`; in `ServeAsync` un `catch (Exception ex)` che risponde `* BYE` e registra l'errore in
  una lista `ServerErrors` controllata a fine gruppo. 30 minuti, rischio nullo (solo prove).
- Verifica su Windows: un messaggio finto con un .eml allegato in `ImapAsync`: niente attesa di 60 s.

### C-46 Aiuti ripetuti e controlli doppi

- Dove e che cosa:
  - `Until(condition, limit)` scritto due volte (Audit.cs:5566, Idle.cs:21) e altre 7 attese a polling scritte a mano (Audit.cs 2863,
    3125, 5053, 6450, 6500, 6513; Persone.cs:98; ProvaBozze.cs:2570);
  - account verso il server IMAP finto in quattro forme (`AccountOf` locale a ProvaBozze.cs:2964, `SpeedAccount` Audit.cs:2447,
    `FakeProvider` Audit.cs:1031, e 15 costruzioni a mano in 6 file);
  - gestori HTTP finti: 6 classi (FakeGraph ProvaBozze.cs:2364, FakeHttp 4186, SlowGraph Audit.cs:1210, UploadGraph 4234, SilentNetwork
    5702, PeopleApi ContattiGoogle.cs:19) e 31 lambda `new FakeHttp(request => ...)`, ognuna con la sua lista di richieste;
  - cartelle temporanee: 7 volte `Path.Combine(Path.GetTempPath(), "missiva-prova-...-" + Guid)` e 12 `try { Directory.Delete } catch { }`;
    `TempFile` (ProvaBozze.cs:1344) usa invece una cartella fissa, condivisa e mai pulita;
  - controlli doppi: LookAlike in ProvaBozze.cs:3398-3403 è un sottoinsieme di S-24 (Audit.cs:2654-2710); "password assente dal registro"
    tre volte (ProvaBozze.cs:3098, 3170; Audit.cs:4204).
- Perché conta: ogni prova nuova ricomincia da capo, e i doppioni divergono (vedi il LookAlike con e senza segno casuale).
- Stato: CONFERMATO.
- Proposta: un file `ProvaBozze.Aiuti.cs` con `Until`, `TempDir()` (IDisposable), `ImapAccount(fake, ...)`, un `GraphFake` con
  instradamento e richieste registrate, `LogSince()` (C-43); togliere il LookAlike di ProvaBozze.cs. 1 giorno, rischio medio (solo
  prove).
- Verifica su Windows: stesso numero di controlli, stessi esiti.

### C-47 Parti senza copertura

- Dove: nessun controllo per `CreateFolderAsync`, `RenameFolderAsync`, `MoveFolderAsync` (ImapProvider.cs:435-452 e gli equivalenti di
  Gmail e Graph), `MainWindow.MigrateFolderKeys` (MainWindow.xaml.cs:1811), `MarkAllReadAsync` (Gmail, IMAP, Graph), `Calendars.Save` e
  `Calendars.Load`, `CategoryPalette`. Coperti solo di passaggio: `Mailto.Parse` (solo dentro Annulla iscrizione, Audit.cs:4578-4581, dove
  Cc e Ccn si tolgono prima), `SafeFile` (3 riferimenti, misure e non controlli).
- Che cosa: proprio i due difetti di nullità di Gmail di C-37 stanno in metodi senza prove.
- Perché conta: rinominare una cartella in Gmail cambia l'identificativo e deve portare con sé Preferiti, colori e ordine
  (`MigrateFolderKeys`): oggi nulla lo controlla.
- Stato: CONFERMATO (riferimenti cercati nei file di prova; per contrasto ActionQueue ha 76 riferimenti, BodyCache 118, Store 245).
- Proposta: dopo `default: BAD` (C-43): prove delle cartelle sul server finto, `Mailto.Parse` diretta (RFC 6068 con Cc, Ccn, "+" e
  testo), andata e ritorno di `Calendars.Save` e `Load`, `MarkAllRead` con il conteggio dei non letti. 1-2 giorni, rischio nullo.
- Verifica su Windows: le prove nuove passano; tolta a mano una riga di `MigrateFolderKeys`, la prova cade.

### C-48 Tempo della suite

- Dove: Core/Store.cs:894 (`SettingsSaver.Delay`, 400 ms, `static readonly`: 9 attese da 800 ms nelle prove, Audit.cs:2098-2175, circa
  7 s); Audit.cs:1462-1464 (2 x 1100 ms chiamati due volte, 4,4 s: si aspetta perché le date di IMAP sono al secondo); Audit.cs:5213,
  5227-5228 (canale con nome, il client finto dorme 1500 ms, 3 s); ProvaBozze.cs:1487 (PDF inattivo, 1500 ms fissi); Audit.cs:731,
  ProvaBozze.cs:3070 (300 ms fissi).
- Che cosa: circa 22,7 s di attese fisse per giro. Le sei funzioni "...Measure" (ProviderWorkMeasure, SummaryCacheMeasureAsync,
  SuggestIndexMeasure, CleanMeasure, DataWritesMeasure, BigDraftMeasure) non hanno controlli e girano a ogni suite.
- Perché conta: un giro più corto si ripete più volentieri (C-41 chiede venti giri).
- Stato: CONFERMATO le attese; SOSPETTO il guadagno complessivo (la durata totale della suite non è scritta da nessuna parte: C-40 la
  darebbe).
- Proposta: `SettingsSaver.Delay` regolabile in Debug (40 ms nelle prove); una INTERNALDATE esplicita nel server finto al posto delle
  attese di 1100 ms; il client del canale annullato dopo la lettura; `Process.WaitForExitAsync` con scadenza per il PDF; le "...Measure"
  dietro un'opzione `misure`. Circa 15 s in meno per giro. Mezza giornata, rischio basso. Il parallelismo dei gruppi solo dopo C-44;
  già oggi si possono affiancare su un altro thread le prove di funzioni pure (HtmlAuditTests, SenderAuditTests, DangerousTypes, la
  lettura di SearchTerms, Unsubscribe.Links) e PdfTestsAsync.
- Verifica su Windows: la durata per gruppo (C-40) prima e dopo, stesso numero di controlli.

### C-49 Stato che resta da un giro all'altro, e divisione dei due file di prova

- Dove: MISSIVA_DATA usata da tutta la suite (rubrica, `missiva.log`, contatti); Core/ProvaBozze.cs (4969 righe) e
  Core/ProvaBozze.Audit.cs (6539 righe).
- Che cosa: un controllo può dipendere dai giri prima (C-43). I due file più grandi del progetto sono di prove.
- Perché conta: due giri di fila possono dare esiti diversi; i file sono difficili da percorrere.
- Stato: CONFERMATO.
- Proposta: `Run` crea e poi cancella una sottocartella nuova di MISSIVA_DATA per ogni giro (1 ora, rischio basso). Divisione per
  spostamento puro (2-3 ore per file, rischio basso):
  - ProvaBozze.cs in `ProvaBozze.FakeImap.cs` (4199-4754), `.FakeSmtp.cs` (4755-4969), `.FakeHttp.cs` (FakeGraph 2364-2497, FakeHttp,
    SenderStub), `.Graph.cs`, `.Gmail.cs` (2500-2731), `.Imap.cs` (2869-3308), `.Account.cs`, `.Calendario.cs` (circa 1000 righe),
    `.Pec.cs` (3522-3892), `.Pdf.cs` (1394-1508), `.Mittente.cs`, `.Contatti.cs`, `.Interfaccia.cs`, `.Avvio.cs`; resta `Run`, `Check`,
    `TempFile`;
  - ProvaBozze.Audit.cs un file per blocco, con il nome del gruppo di `--prova-audit`: Html (3567-3842), Loghi (2794-3566), Mittente
    (2528-2793), Velocita (644-656, 2191-2527), Difetti (75-643), Funzioni (4219-4893), Copia (3843-4218), Velocita2 (1488-2190),
    Velocita3 (945-1487), Sicurezza (4894-5467), Ottobre (700-944, 5468-6539).
- Verifica su Windows: due giri di fila con esito identico e lo stesso numero di righe; dopo la divisione 870 controlli come prima.

---

## 8. Difetti di comportamento

### C-50 Sposta, Archivia ed Elimina non riusciti: le righe spariscono e non tornano

- Dove: UI/MainWindow.xaml.cs:3271-3315 (`MoveAsync`: `RemoveRows` a 3280, `case false` a 3304-3306), 3334-3404 (`DeleteAsync`, anche
  l'eliminazione definitiva); `RunOrQueueAsync` in UI/MainWindow.Offline.cs:50-55.
- Che cosa: le righe escono dall'elenco prima della richiesta. Se la richiesta fallisce per un motivo che non è la rete (NO di IMAP, 400 o
  403 di Graph, cartella sparita), `RunOrQueueAsync` mostra "Operazione non riuscita" e restituisce false; il ramo `false` mette solo
  `ok = false`. Le righe tolte non si rimettono e per quel gruppo non c'è un `MovePart`, quindi nemmeno Annulla le riporta.
- Perché conta: il messaggio è ancora nella cartella sul server ma l'utente non lo vede fino alla prossima rilettura; dopo l'avviso
  sembra comunque spostato o eliminato.
- Stato: CONFERMATO (nessun ramo `false` chiama `rows.Restore`; i chiamanti a 3327, 3609, 3654, 3783 non rileggono).
- Proposta: nel `case false` rimettere le righe del gruppo (`rows.Restore` con quelle di `removed` di quell'account), se `listVersion` è
  ancora `version`; altrimenti nulla (l'elenco è già un altro). 1-2 ore, rischio basso.
- Verifica su Windows: `--imap-finto` con `RefuseOnce` sulla MOVE, Canc su un messaggio: la riga torna; una prova in `--prova-bozze` con
  il server finto che rifiuta.

### C-51 Regole di Missiva: un errore a metà fa saltare per sempre anche i messaggi mai toccati

- Dove: Core/Rules.cs:611-617 (`ApplyToNewAsync`); il segno `marks[id]` avanza a 606, prima di agire.
- Che cosa: tutti i messaggi nuovi vanno in `RulesSeen` e `Store.SaveSoon()` prima di `RunAsync`, per scelta (commento a 612: "se
  un'azione non riesce a metà, al controllo dopo non si ripete su quelli già toccati"). `RunAsync` raggruppa: prima letto, contrassegno e
  categorie, poi copie e spostamenti. Un errore di rete o un NO nel primo gruppo interrompe tutto (l'eccezione la registra
  `CheckAccountAsync`, App.cs:1035): al controllo dopo tutti i messaggi sono "visti", anche quelli che nessuna azione ha toccato, e lo
  spostamento non si rifà più.
- Perché conta: dopo la ripresa dalla sospensione, con la rete che va e viene, una regola "sposta in Fatture" può non agire senza che
  nessuno lo dica.
- Stato: CONFERMATO.
- Proposta: se `RunAsync` fallisce con un errore che passa (`Network.IsOffline` o lo stesso criterio di `ActionQueue.Passing`), togliere
  da `RulesSeen` i messaggi dei gruppi non ancora partiti; oppure mettere le azioni non fatte in `ActionQueue`, che ha già la ripetizione.
  Da discutere con l'autore il rischio opposto (un'azione ripetuta: segnare letto due volte è innocuo, spostare due volte no, ma
  `StillThereAsync` lo sa già). Mezza giornata, rischio medio.
- Verifica su Windows: in `--prova-bozze` una regola "segna letto e sposta" su tre messaggi con il server finto che chiude la
  connessione alla prima STORE; al controllo dopo i messaggi devono essere spostati, una volta sola.

### C-52 "Nessuna firma" scelta a mano viene annullata cambiando "Da"

- Dove: UI/ComposeWindow.Format.cs:333-344 (`ReplaceSignature`, parametro `name` non usato), UI/ComposeWindow.xaml.cs:891-904
  (`ChangeSignature`).
- Che cosa: dopo Firma, Nessuna firma, `signature` è null. Cambiando "Da", `ChangeSignature` salta il controllo `SignatureUntouched` (vale
  solo se `signature != null`) e chiama `InsertSignature`, che mette la firma predefinita del nuovo indirizzo. Lo stesso dopo aver scelto
  un'altra firma: `ReplaceSignature` mette `signature = null` (riga 338) e `PutSignature` la rimette; dipende da `PutSignature` se la
  firma scelta torna a essere "di Missiva" e quindi sostituibile. Il commento di `ReplaceSignature` dice "la scelta è dell'utente".
- Perché conta: chi toglie la firma per scrivere a un collega e poi cambia l'indirizzo se la ritrova.
- Stato: CONFERMATO per "Nessuna firma"; SOSPETTO per una firma scelta (va letto `PutSignature`).
- Proposta: un campo `signatureChosen`, acceso da `ReplaceSignature`; con quel campo acceso `ChangeSignature` non tocca la firma. Il
  parametro `name` si toglie. 30 minuti, rischio basso.
- Verifica su Windows: Nuovo messaggio, Firma, Nessuna firma, poi "Da" su un altro indirizzo con firma: il testo resta senza firma.

### C-53 Account rimosso o cambiato: i provider si chiudono sotto le richieste in corso

- Dove: UI/MainWindow.xaml.cs:937 (`LoadAccountsAsync` fa `Dispose` di ogni provider; la chiamano `RemoveAccountAsync`, Gestisci
  account, Aggiungi account); Core/GmailProvider.cs:41-59 e Core/ImapProvider.cs:40 (`Connection.Dispose` fa il LOGOUT su un thread del
  pool senza prendere `Gate`).
- Che cosa: nessuno aspetta quello che è in corso: copia dei 30 giorni (`OfflineCopy.RunAsync`), `ActionQueue.ReplayAsync`,
  `CheckAccountAsync`, `RecountSoon`, regole, invii pianificati. Continuano sul provider vecchio; `ImapClient` non è thread-safe, quindi un
  LOGOUT in parallelo a un comando dà "client busy", ObjectDisposedException o una connessione in stato sbagliato. Righe e conteggi si
  scartano bene (il provider si confronta per riferimento), restano errori nel registro e, per le azioni dell'utente, un "Operazione non
  riuscita".
- Perché conta: Gestisci account durante la copia dei 30 giorni (che parte quando la finestra è ferma) è un caso normale.
- Stato: SOSPETTO: manca la prova.
- Proposta: `Connection.Dispose` prende `Gate` con un tetto (per esempio 5 s) prima del LOGOUT; oppure un `CancellationTokenSource` per
  provider, annullato in Dispose e controllato da `RunAsync` prima di ogni comando. Mezza giornata, rischio medio.
- Verifica su Windows: `prova.ps1 -Keep` con la copia dei 30 giorni in corso, poi Gestisci account, Salva; nel registro niente "busy" né
  ObjectDisposedException.

### C-54 Rimuovi account non svuota la Posta in uscita di quell'account

- Dove: UI/MainWindow.xaml.cs:5129-5160 (`RemoveAccountAsync`: chiama `ActionQueue.Forget`, niente per `Outbox`), UI/MainWindow.Offline.cs:124
  (`OutboxWindows` conta tutte le scritture in uscita, di qualunque account).
- Che cosa: un messaggio inviato senza rete da un account poi rimosso resta in `Outbox` con la sua finestra nascosta, che tiene il
  provider chiuso. La barra continua a dire "partirà quando torna la rete": non partirà mai, perché `ReplayQueueAsync` gira solo sugli
  account rimasti.
- Perché conta: un messaggio che l'utente crede in partenza.
- Stato: CONFERMATO che manca la pulizia; SOSPETTO l'effetto di "Annulla" sulla finestra riaperta.
- Proposta: in `RemoveAccountAsync` riaprire le scritture in uscita di quell'account con un avviso ("account rimosso: il messaggio non
  partirà"), come fa `CloseForOwnerAsync(keepOutbox: false)`; oppure dirlo nella domanda prima di rimuovere, come per gli invii
  pianificati. 1 ora, rischio basso.
- Verifica su Windows: `--senza-rete`, Invia (va in uscita), Rimuovi account: la barra sparisce e la scrittura si riapre.

### C-55 Annulla di uno spostamento in coda partito solo in parte

- Dove: UI/MainWindow.xaml.cs:3536-3550 (`UndoAsync`, ramo `part.Queued`).
- Che cosa: se `ActionQueue.Remove` toglie solo alcune azioni (le altre sono già `running`, B-13), il codice tratta il gruppo come "già
  fatto": non mette in `back` quelle tolte davvero (che restano nella cartella d'origine sul server), dice "lo spostamento è già stato
  fatto" per tutte, e rilegge l'elenco solo se `back.Count == 0`. Nella vista di tutti gli account, con un'altra parte riuscita, le righe
  tolte dalla coda non tornano.
- Perché conta: righe che spariscono dall'elenco mentre il messaggio è rimasto dov'era.
- Stato: CONFERMATO dalla lettura; raro (Annulla nel secondo in cui torna la rete).
- Proposta: `ActionQueue.Remove` restituisce le chiavi tolte; quelle vanno in `back`, e `stale = true` con la rilettura comunque.
  1 ora, rischio basso.
- Verifica su Windows: in `--prova-bozze` con `QueueStub` lento, Annulla durante la ripetizione di tre spostamenti.

### C-56 Promemoria del calendario persi quando la lettura non riesce, e nell'ora del 25 ottobre

- Dove: UI/MainWindow.Calendar.cs:129, 214-219 (`CheckRemindersAsync`), 142-147 (`Upcoming`).
- Che cosa: `remindedUpTo` avanza ogni 30 s anche quando `upcoming` non ha gli eventi (lettura fallita, rete assente all'avvio). Alla
  lettura riuscita `Upcoming` prende solo i promemoria con `at > now - 1 minuto`: quelli scaduti durante il buco non suonano mai. La
  rilettura dopo 1 o 5 minuti (audit del 7 ottobre, A-17) accorcia il buco ma non lo chiude. Inoltre `remindedUpTo` è un'ora locale: al
  ritorno all'ora solare (domenica 25 ottobre 2026, 03:00 che torna 02:00) sta un'ora avanti a `DateTime.Now`, e i promemoria fra le
  02:00 e le 02:59 della seconda passata non suonano.
- Perché conta: un promemoria di una riunione perso perché il PC si è svegliato senza rete.
- Stato: CONFERMATO; gravità media per il primo caso, bassa per il secondo (di notte).
- Proposta: `remindedUpTo` per account, che avanza solo dopo una lettura riuscita di quell'account; in `Upcoming` tenere anche i
  promemoria scaduti da al più 30 minuti se oltre `remindedUpTo` (con la notifica che dice "era alle ..."); confronti in UTC. 2-3 ore,
  rischio basso.
- Verifica su Windows: `--calendario-finto` con il promemoria fra un minuto, `--senza-rete` per due minuti, poi "rete tornata": il
  promemoria suona in ritardo.

### C-57 Durate misurate con `DateTime.Now`

- Dove: Core/Offline.cs:51 (`RecentlyOffline`); Core/ImapProvider.cs:582, 702 (`Everywhere.Started`, `ReuseSearch`);
  UI/MainWindow.Offline.cs:281, 300, 306 (`lastActivity`, `offlineDone`); UI/MainWindow.SearchFilters.cs:43, 93; UI/App.cs:844;
  Core/Contacts.cs:296.
- Che cosa: il 25 ottobre `DateTime.Now - x` è negativo per un'ora: la ricerca IMAP in tutte le cartelle riusa risultati fino a circa 61
  minuti invece di 60 s; la copia dei 30 giorni e la raccolta a riposo non partono per un'ora; la chiusura del pannello dei filtri dopo
  600 ms non scatta. A fine marzo il contrario: tutto scade un'ora prima.
- Perché conta: poco (di notte, una volta l'anno), ma la correzione è meccanica.
- Stato: CONFERMATO.
- Proposta: `Environment.TickCount64` o `Stopwatch` per le durate, `DateTime.UtcNow` dove serve un istante. 1 ora, rischio nullo.
- Verifica su Windows: `--prova-bozze`; a mano in una macchina virtuale con l'ora cambiata, la riga "ricerca: riusata".

### C-58 Promemoria del contrassegno cancellato prima di controllare l'account

- Dove: UI/MainWindow.Flags.cs:160-162 (`CheckFlagReminders`).
- Che cosa: `f.Reminder = null` viene prima di `if (account == null) continue;`, poi `Store.Save()`: per una voce di un account che non
  è (o non è ancora) fra gli account, il promemoria sparisce senza notifica.
- Perché conta: raro (`ForgetAccount` toglie le voci; serve una voce orfana, per esempio da una copia di sicurezza importata).
- Stato: CONFERMATO l'ordine; SOSPETTO il caso reale.
- Proposta: spostare l'azzeramento dopo il controllo. 5 minuti, rischio nullo.
- Verifica su Windows: in `--prova-bozze` una `FlagDate` di un account inesistente con un promemoria passato: resta.

### C-59 Orari in un fuso estero a un'ora che non esiste

- Dove: Core/Invitation.cs:259 (`value.TrimEnd('Z')[..15]`), 262 (`Time`); Core/Calendar.cs:117 (`Calendars.LocalTime`);
  Core/GraphProvider.cs:1423 (`When`, copia di `LocalTime`).
- Che cosa: `TimeZoneInfo.ConvertTime(time, zone, Local)` lancia ArgumentException se l'ora cade nel salto dell'ora legale del fuso
  d'origine (un invito da New York alle 02:30 del secondo marzo). In `Invitation.Parse` il `catch` generale restituisce null: la scheda
  dell'invito sparisce. Un valore più corto di 15 caratteri fa lo stesso. In `Calendars.LocalTime` non c'è `catch`: la lettura
  dell'evento fallisce.
- Perché conta: un invito dall'estero senza scheda né pulsanti.
- Stato: CONFERMATO dal codice e dal comportamento documentato di .NET; SOSPETTO che Graph mandi orari in un altro fuso (Missiva chiede
  il fuso di Windows con Prefer).
- Proposta: una sola funzione condivisa per le tre copie che, se `zone.IsInvalidTime(t)`, sposta in avanti dello scarto della regola (o
  di un'ora) prima di convertire; controllo della lunghezza prima di `[..15]`. 1 ora, rischio basso.
- Verifica su Windows: `--scrivi-inviti` con un .ics costruito con quell'ora, aperto con `--eml`.

### C-60 La finestra "una sola cartella" non riceve i messaggi nuovi

- Dove: UI/App.cs:868 e seguenti, 1033, 1048: tutte le chiamate passano da `(MainWindow as MainWindow)?.AddIncoming`,
  `RemoveRuledRows`, `RefreshInboxOf`, cioè solo la finestra principale.
- Che cosa: una seconda finestra aperta sulla Posta in arrivo ("Apri in un'altra finestra") non mostra le righe nuove, non toglie
  quelle spostate dalle regole, non vede i segni cambiati altrove finché non si rilegge.
- Perché conta: due finestre aperte sono un caso previsto (CLAUDE.md, menu delle cartelle).
- Stato: SOSPETTO: va controllato che la finestra secondaria non abbia un suo aggiornamento che non ho trovato.
- Proposta: le quattro chiamate su `Application.Current.Windows.OfType<MainWindow>()`. 1 ora, rischio basso.
- Verifica su Windows: `--folder-window`, poi "arrivo Oggetto" con `prova-finta.ps1`: la riga compare in tutte e due.

### C-61 Invii pianificati: un errore dopo il DATA può far ripartire il messaggio

- Dove: Core/ScheduledSending.cs:55-60, 79-86 (`Transient`: IOException, ProtocolException, OperationCanceledException).
- Che cosa: se la connessione SMTP cade dopo il DATA e prima della risposta 250, l'errore è "di rete": l'invio resta in elenco e riparte
  dopo un minuto, ma il server può aver già accettato il messaggio. Il commento "mai due invii" vale se `SendDraftAsync` assorbe tutto
  dopo il 250; il caso "fra DATA e 250" non lo copre nessuno.
- Perché conta: un doppio invio a un cliente.
- Stato: SOSPETTO: serve leggere `SendDraftAsync` di Gmail e IMAP riga per riga e provare con `FakeSmtp` che chiude dopo il DATA.
- Proposta: un'eccezione propria per "dopo il DATA" che toglie l'invio con un avviso ("forse è partito: controlla la Posta inviata").
  2 ore, rischio basso.
- Verifica su Windows: prova con `FakeSmtp` che chiude dopo il DATA: un solo invio.

### C-62 Filtri della ricerca: virgolette, parentesi e date senza anno

- Dove: Core/SearchTerms.cs:39-57 (`Tokens`), 59-61 (`Quote`), 69 (`ParseDate`, forma "d/M"), 149-151 (`ToGmail`).
- Che cosa: una virgoletta non chiusa (`da:"Mario fattura`) mette in `From` tutto il resto del testo; un valore che contiene `"` non fa il
  giro `ToString` e `Parse`; `from:(...)` di Gmail riceve parentesi non bilanciate se il nome le contiene; "dal:25/12" scritto a gennaio è
  il 25 dicembre di quest'anno, nel futuro, quindi nessun risultato.
- Perché conta: ricerche vuote senza spiegazione.
- Stato: CONFERMATO; gravità bassa.
- Proposta: in `Quote` togliere le virgolette interne; in `ToGmail` togliere le parentesi dai valori; per "d/M" senza anno, se la data è
  nel futuro, l'anno prima. 1 ora, rischio basso.
- Verifica su Windows: casi nuovi in `SearchFiltersAsync`.

### C-63 Il colore del testo secondario non è lo stesso dappertutto

- Dove: UI/MainWindow.Suggestions.cs:177 (`Secondary` del pannello dei suggerimenti), UI/MainWindow.xaml.cs:4013,
  UI/MainWindow.Flags.cs:25, UI/NameWindow.cs:32, UI/CategoryWindow.cs:40, UI/Dialog.cs:70, UI/MessageRowElement.cs:434 (graffetta della
  riga), e 18 punti nei .xaml (MainWindow.xaml:40, 67, 151, 168, 174, 176, 226, 233, 898; ComposeWindow.xaml:14, 89, 134;
  MenuCustomizeWindow.xaml:9, 73; ReadingView.xaml:9).
- Che cosa: CLAUDE.md (giro 6, "Testo secondario") dice che il `TextFillColorSecondaryBrush` di Fluent è trasparente e dava #A8A8A5 nel
  chiaro e #E9E9E9 nello scuro, e che Missiva usa `SecondaryTextBrush` (#616161 e #ADADAD). 24 punti usano ancora quello di Fluent, 44
  quello di Missiva. Alcuni sono voluti (l'elenco del giro 6 non è completo), altri no: per esempio la graffetta della riga disegnata
  (MessageRowElement.cs:434) usa Fluent mentre anteprima e data della stessa riga usano Missiva; nel modello vecchio era lo stesso
  (MainWindow.xaml:898).
- Perché conta: due grigi diversi nella stessa riga; ma cambiarli cambia ciò che si vede.
- Stato: CONFERMATO l'uso misto; SOSPETTO quali siano voluti.
- Proposta: un elenco dei 24 punti da far guardare all'autore con una foto per tema, poi un solo commit. 1 ora più la scelta, rischio basso
  (visivo).
- Verifica su Windows: `foto.ps1` in chiaro e scuro prima e dopo; i pixel con `pix.ps1`.

---

## 9. Semplificazioni

### C-64 Semplificazioni senza rischio che riducono codice o lavoro

- Dove e che cosa (ognuna è già descritta nel suo punto; qui l'elenco con la stima):
  - il blocco Debug di `StartAsync` in un metodo suo (C-23): 218 righe fuori dal metodo di avvio, 20 minuti;
  - `FillCustomMenu` (C-15) e `HideMail`/`RestoreMail` (C-18): circa 40 righe in meno, 40 minuti;
  - consenso di Graph (C-12): circa 40 righe in meno, 2-3 ore;
  - `MicrosoftAuth.Owner` risolto al momento (C-13): undici assegnazioni in meno e la perdita di memoria chiusa, 30 minuti;
  - le allocazioni inutili della riga (C-07), 10 minuti;
  - il `Task.Run` di RuleWindow (C-27), 5 minuti;
  - `EncryptOld` (C-06), 10 minuti.
- Stato: CONFERMATO.
- Verifica su Windows: quella di ciascun punto.

### C-65 `IMailProvider` e TestingProvider: ogni metodo nuovo si scrive in quattro posti

- Dove: Core/TestingProvider.cs:10 (circa 283 righe, un metodo per ogni metodo di `IMailProvider`), GraphProvider, GmailProvider,
  ImapProvider, EmlProvider.
- Che cosa: CLAUDE.md lo ricorda ("un metodo nuovo che scrive va aggiunto lì come scrittura"). Con la base comune di C-08 i posti
  diventano tre; i metodi di sola lettura di TestingProvider sono inoltri di una riga.
- Perché conta: un metodo di scrittura dimenticato in TestingProvider scriverebbe sulla casella vera dell'autore durante le prove.
- Stato: CONFERMATO che il pericolo è solo un'abitudine; SOSPETTO che oggi manchi qualcosa (non controllato metodo per metodo).
- Proposta: una prova in `--prova-bozze` che con la riflessione elenca i metodi di `IMailProvider` e controlla che ogni metodo che non è
  in una lista di letture conosciute sia sovrascritto in TestingProvider in modo da non chiamare il provider vero (per esempio con un
  provider finto che lancia a ogni chiamata). 1-2 ore, rischio nullo.
- Verifica su Windows: aggiungere a mano un metodo di scrittura finto a `IMailProvider` senza TestingProvider: la prova cade.

### C-66 Codice di prova nell'eseguibile

- Dove: 1,24 MB di sorgente solo Debug (Misure), compreso nella versione di prova con gli strumenti (`-p:DefineConstants=DEBUG%3BTRACE`).
- Che cosa: la suite e gli strumenti stanno nell'assembly di Missiva perché usano membri `internal` e il thread STA di WPF. Nella versione
  installata non ci sono (tutto dietro `#if DEBUG`), quindi non costano a chi usa Missiva.
- Perché conta: compilazione di Debug più lenta e un progetto solo per due cose diverse; ma spostare la suite in un progetto a parte
  (`InternalsVisibleTo`) è un lavoro grande che non cambia nulla per l'utente.
- Stato: CONFERMATO.
- Proposta: non ora; da rivedere dopo C-40 e C-49 se i tempi di compilazione in Debug danno fastidio.

---

## 10. Documentazione

### C-67 CLAUDE.md: struttura proposta

- Dove: CLAUDE.md, 279.642 byte, 2.378 righe (circa 75-85 mila token, stima a 3,5 caratteri per token). "Stato e architettura" pesa 247
  KB, "Prove" 23,5 KB. Le voci più grandi: "Fluidità dello scorrimento, Loop 4" 27,3 KB (riga 570), "Solo in Debug" con la storia dei
  conteggi 18,7 KB (2180, di cui 15 KB di numeri vecchi "870 ... prima 858 ... prima 258"), Persone 13,7 (1418), Calendario 13,4 (918),
  Scatti dello scorrimento 8,8 (495), Lavoro senza rete 8,3 (1090), Suggerimenti dei destinatari 7,7 (1356), Messaggio aperto 7,7 (273).
  MISURATO (byte per voce).
- Che cosa: CLAUDE.md è insieme manuale di lavoro, architettura, diario delle misure e storia degli esperimenti. Ripetizioni: notifiche in
  quattro voci (righe 114, 1749, 1763, 1855, più S-21 a 2144); senza rete in quattro (1064, 1090, 1730, 1740); scorrimento raccontato tre
  volte (479, 495, 570: per lavorare serve solo lo stato attuale, 4-6 KB su 38); contatti di Google due volte (963-992 dentro Calendario e
  1494-1520 in Persone); i blocchi 6-10 dell'audit (1996-2176, 20,7 KB) ripetono le funzioni e rimandano 16 volte a ESITO.md; argomenti di
  Debug in due posti più 24 frasi "In Debug `--x`" sparse (e `--calendario` dentro "Lavoro senza rete", 1159-1162).
- Perché conta: ogni sessione paga 280 KB di contesto prima di cominciare, e le frasi superate (C-68) restano accanto a quelle giuste.
- Stato: CONFERMATO.
- Proposta:
  - CLAUDE.md breve, 11-13 KB: "Come lavorare con l'autore" e "Sessioni online" come oggi; l'architettura in 25-30 righe, una per area con le
    classi principali; l'indice dei file in docs con una riga "quando leggerlo"; le prove essenziali (build, `prova.ps1` e `-Release`,
    MISSIVA_DATA e MISSIVA_NO_READ, MISSIVA_KEYS in breve, la casella vera da non modificare, SendInput di notte, TestingProvider per ogni
    metodo nuovo che scrive).
  - File per area (KB di oggi, poi dopo aver tolto le ripetizioni):

| File | Voci di oggi | KB oggi | KB dopo |
|---|---|---|---|
| docs/architettura/avvio-e-processi.md | Installazione, Avvio, Area di notifica, Una sola Missiva, WebView2 senza GPU, Registro e Store, Copia di sicurezza | 20 | 14 |
| docs/architettura/account.md | Microsoft, lavoro, Gestisci, Aggiungi, Workspace, IMAP, Gmail, altri indirizzi, caselle condivise, token, ordine (Sito e Pubblicazione con segnaposto, C-69) | 18,5 | 14 |
| docs/architettura/lettura.md | contenuto, messaggio aperto, pulizia HTML, loghi, avatar, chi scrive davvero, Reply-To, inviti, PEC, anteprima, .eml, apertura, anticipi, Origine, protezioni, Annulla iscrizione | 37 | 28 |
| docs/architettura/elenco-e-finestra.md | finestra, righe, margini, pillola, barra delle cartelle, contatore, elenco, menu, scorciatoie, tema, Annulla, trascinamento, velocità, accessibilità; scorrimento solo lo stato attuale | 63 | 20 |
| docs/storia/scorrimento.md | Scatti, Fluidità, prova alla cieca, DirectManipulation tolto, cursore sopra le righe, con le misure | 36 | 25 |
| docs/architettura/scrittura.md | firme, formato, Rispondi, bozze, salvataggio automatico, immagini, suggerimenti, Annulla invio, Pianifica, opzioni, modelli, allegato dimenticato, allegati grandi | 25 | 20 |
| docs/architettura/calendario-persone.md | calendario con Google, contatti di Google, Persone, contrassegni, risposte automatiche, regole | 36 | 28 |
| docs/architettura/sincronizzazione-e-notifiche.md | senza rete, lavoro senza rete, IDLE e delta, controllo, non letti, notifiche (le quattro voci unite), letto, tutti gli account, ricerca, filtri, suggerimenti | 36 | 28 |
| docs/prove.md | Prove senza la storia dei conteggi; tabella degli argomenti e delle variabili; catalogo dei comandi di MISSIVA_KEYS (C-04); strumenti | 26 | 12 |

  - Totale: da 280 KB a circa 200 KB in docs più 12 KB in CLAUDE.md, circa 265 KB in meno per sessione. La storia dei conteggi della
    suite diventa una tabella "gruppo di `--prova-audit` -> che cosa prova" (circa 2 KB).
  - 3-5 ore per spostare, 2-3 ore per condensare scorrimento e conteggi; rischio medio-basso: l'indice deve nominare le classi chiave di
    ogni area, perché chi lavora sappia quale file aprire. Le parti tolte restano nella storia di git.
- Verifica su Windows: non serve; una sessione di prova che deve trovare un'informazione partendo solo dall'indice.

### C-68 CLAUDE.md: frasi superate o in contraddizione con il codice

- Dove e che cosa (righe di CLAUDE.md):
  1. 129-134: "quel testo parte ancora, dopo il controllo QuickReplyRisk ... poi 'Risposta inviata'". Il codice apre sempre una scrittura
     (UI/MainWindow.Notifications.cs:43-51, audit S-21) e la prova controlla che `ReplyFromNotificationAsync` non esista più
     (ProvaBozze.Audit.cs:5269-5270). Contraddice anche la riga 2144.
  2. 1764-1766: "senza nessuna finestra aperta apre l'app con la mail nel riquadro". `App.OnToast` (UI/App.cs:1330-1337) apre sempre la
     finestra del messaggio, come dice la voce prima (1749).
  3. 665: "Il valore fisso è il 2 (`Touch = TouchModels[2]`)". Il codice usa `TouchModels[3]` (UI/WheelScrolling.cs:86), come dice 678.
  4. 645: "Se non basta: DirectManipulation ..., da provare come esperimento". 713-721 dicono che è stato tolto e non va riproposto.
  5. 274: "fino a 300 ms (`ShowDelay`)". Vale 1000 ms (UI/ReadingView.xaml.cs:253); la riga 312 lo corregge solo dopo.
  6. 1026-1027: "Non ancora: ... Gmail (Calendar API ...)". Il calendario di Google c'è (940-962, Core/GmailProvider.Calendar.cs).
  7. 1421-1422: contatti di Missiva per Gmail "finché Google Cloud non ha la People API". La People API c'è, in lettura e scrittura.
  8. 1897: "catalogo e icone in `UI/MessageMenu.cs`". Il file non esiste; il catalogo è in UI/ContextMenus.cs (come dice 1916). È l'unico
     percorso citato che non esiste.
  9. 2294: la prova "limite di 3 MB". Il limite è stato tolto (F-15), ma la prova (ProvaBozze.cs:2300-2314) porta ancora quel nome e oggi
     controlla solo che non ci sia errore.
  10. 487, 519, 641: `SpringTau` e `FineSpread`: `FineSpread` non esiste più nel codice (0 occorrenze).
  11. La voce "Non letti ed elenco dopo un arrivo" parla del "timeout di MailKit di 120 s": vale solo per Gmail (C-09).
  12. 615-616: MISSIVA_RIGA_VECCHIA "da togliere quando A ha finito": è ancora lì (C-01).
  13. Nel codice, il commento di UI/ContactWindow.cs:27 ("gli account Microsoft in cui si può creare") non dice che oggi vale anche per
      Google con l'ambito di scrittura.
- Controllo dei nomi: tutti i 1.407 nomi fra backtick di CLAUDE.md sono stati cercati nei .cs e .xaml; esistono tutti tranne
  `UI/MessageMenu.cs`, `FineSpread` e `PredictAdapt` (quest'ultimo è nel simulatore fuori dal repository, ed è scritto così). Il
  controllo dice che il nome c'è, non che il significato descritto sia ancora quello. MISURATO (script).
- Perché conta: chi legge (anche una sessione online) prende per vero un comportamento che non c'è più.
- Stato: CONFERMATO.
- Proposta: correggere le dodici frasi (30 minuti, rischio nullo), anche prima della riorganizzazione di C-67.

### C-69 Dati reali dentro CLAUDE.md

- Dove: CLAUDE.md, voci "Account Microsoft", "Sito" e "Pubblicazione per tutti" (circa righe 137-138 e 234-258) e la riga dell'indirizzo
  di contatto (circa 255).
- Che cosa: ci sono un indirizzo di posta reale, gli identificativi della directory e dell'app registrata, il dominio personale e il valore
  di un record TXT di verifica. La regola della riga 14 dello stesso file dice che in quel file non vanno indirizzi veri, e il file entra in
  ogni sessione, anche online. Qui non li ripeto.
- Perché conta: la regola è dell'autore; l'identificativo dell'app è nel codice per necessità (`MicrosoftAuth.DefaultClientId`), quello della
  directory no (0 occorrenze nel codice).
- Stato: CONFERMATO.
- Proposta: spostare "Sito" e "Pubblicazione per tutti" in una nota fuori dal repository e lasciare segnaposto; l'identificativo dell'app
  può restare citato come `MicrosoftAuth.DefaultClientId` senza il valore. 15 minuti, rischio nullo. La storia di git li conserva comunque
  (repository privato, come già scritto per i dati tolti il 3 ottobre).

### C-70 LEGGIMI.md: che cosa non corrisponde più

- Dove e che cosa (righe di LEGGIMI.md):
  1. 331: la copia dei 30 giorni "per Gmail e IMAP solo quelli sotto 300 KB". `ImapProvider.Prefetchable` (Core/ImapProvider.cs:1115-1119)
     copia anche i messaggi grandi con allegati: testo e immagini incorporate se stanno sotto 2 MB (`ReadableLimit`).
  2. 15 e 520: requisiti "solo .NET 10 SDK". `installa.ps1` si lancia con `pwsh` (PowerShell 7), che su un Windows normale non c'è. Che
     con PowerShell 5.1 l'installazione fallisca è SOSPETTO (CLAUDE.md dice che senza BOM 5.1 storpia le accentate; installa.ps1 ha
     lettere accentate).
  3. 3: "per Windows 11"; Missiva.csproj:6 dichiara `SupportedOSPlatformVersion` 10.0.17763 (Windows 10 1809). Su Windows 10 non è mai
     stato provato e Mica è solo di Windows 11: va scelto quale dei due vale.
  4. 170: le sezioni delle Impostazioni elencano "Generali, Aspetto e Notifiche; Account e Firme"; UI/SettingsWindow.cs:94-95 ha anche
     "Avvio e chiusura" e "Copia di sicurezza", e la sezione si chiama "Account di posta".
  5. 412: i temporanei degli allegati si cancellano "alla chiusura (quelli ancora in uso, all'avvio dopo)"; il codice cancella anche ogni
     minuto le cartelle ferme da un'ora (UI/MainWindow.xaml.cs:165), all'avvio solo quelle di più di un giorno (UI/App.cs:99), alla chiusura
     tutte (App.cs:325).
  6. 496: "da Impostazioni, Apri la cartella dei registri"; la voce è nel menu dell'ingranaggio (UI/MainWindow.xaml.cs:5162-5174), non
     nella finestra Impostazioni.
  7. I loghi dei mittenti non sono citati: sono accesi di base (Core/Store.cs:56) e, per un dominio noto e autenticato, Missiva chiede DNS
     e https al sito del mittente quando la riga compare (UI/ViewModels.cs:1396-1409, `SenderLogos.Request`), con il rinnovo dopo 30
     giorni. La sezione Sicurezza (404-405) promette che "il mittente non sa se e quando hai letto": vero per il riquadro, ma l'azienda può
     vedere l'indirizzo IP e il momento in cui l'elenco la mostra (una volta ogni 30 giorni per dominio).
  8. 334: "Annulla funziona come sempre" per le azioni senza rete; mentre la ripetizione è in corso le azioni di quell'account non si
     tolgono (Core/ActionQueue.cs:152, `running`), e C-55.
  9. 422-423: "Le risposte automatiche di Gmail e degli altri account." sta nel punto Persone di "Cosa manca ancora".
  10. 32-45: i passi per un'app Microsoft propria fanno dichiarare solo Mail.ReadWrite e Mail.Send; calendario, contatti, impostazioni e
      colleghi chiedono altri permessi con il consenso incrementale (Core/MicrosoftAuth.cs:274-277). SOSPETTO che funzioni senza
      dichiararli (consenso dinamico con un account personale: mai provato).
- Corrispondono al codice (controllati): le scorciatoie della finestra principale (UI/MainWindow.xaml.cs:4931-4953, Canc e Invio a
  3788-3793) e della scrittura (UI/ComposeWindow.xaml.cs:2055-2066, Esc a 2082-2088), Rispondi della notifica, "+N altri" oltre tre
  eventi, la revoca del token di Google (ma vedi C-32), Posponi che torna da leggere, i filtri della ricerca, la versione 0.3.
- Stato: CONFERMATO salvo dove scritto.
- Proposta: correggere 1, 4, 5, 6, 8, 9 (20 minuti); aggiungere PowerShell 7 fra i requisiti; una riga sui loghi in "Cosa c'è" e in
  Sicurezza con la casella "Mostra il logo dell'azienda del mittente"; per 3 e 10 decide l'autore. Rischio nullo.

### C-71 Strumenti e argomenti non citati

- Dove: strumenti/lampi.ps1:37 e strumenti/scrivi.ps1:9 (cercano solo `bin\Debug`); strumenti/scrivi.ps1 (mai citato); `--mostra-notifica`
  (Core/DevTools.cs:47-60) e `MISSIVA_REGISTRO_SUBITO` (Core/Store.cs:1097), letti ma mai citati; l'elenco degli strumenti in CLAUDE.md
  (riga 2351) senza `avvio.ps1`, `ingresso.ps1`, `scrivi.ps1`, e con "gli altri strumenti la riconoscono" (la Release di prova), che per
  lampi.ps1 e scrivi.ps1 non è vero.
- Che cosa: i 83 argomenti `--...` letti dal codice e citati in CLAUDE.md, LEGGIMI.md e negli script esistono tutti (quelli citati e non
  letti sono di altri programmi: Edge, dotnet-trace, dotnet). Le 26 variabili MISSIVA_ lette esistono tutte.
- Stato: CONFERMATO.
- Proposta: citarli in docs/prove.md (C-67); allineare la ricerca del processo di lampi.ps1 e scrivi.ps1 a quella di elenchi.ps1.
  20 minuti sul PC, rischio basso.
- Verifica su Windows: `lampi.ps1` con `prova.ps1 -Release` avviata.

---

## I venti interventi con il miglior rapporto fra beneficio e rischio

In ordine. Il tempo è una stima del lavoro sul PC dell'autore, prove comprese.

| # | Punto | Che cosa | Tempo | Rischio |
|---|---|---|---|---|
| 1 | C-50 | rimettere le righe quando Sposta, Archivia o Elimina falliscono | 1-2 ore | basso |
| 2 | C-09 | un solo timeout di MailKit per Gmail e IMAP (oggi 120 e 60 s senza una scelta) | 15 minuti | basso |
| 3 | C-24 | gestori `UnhandledException` e `UnobservedTaskException` con la riga nel registro | 20 minuti | basso |
| 4 | C-13 | `MicrosoftAuth.Owner` risolto al momento: niente finestre chiuse tenute in memoria né HWND distrutti | 30 minuti | basso |
| 5 | C-52 | "Nessuna firma" che resta cambiando "Da" | 30 minuti | basso |
| 6 | C-68 | correggere le dodici frasi di CLAUDE.md che contraddicono il codice | 30 minuti | nullo |
| 7 | C-37 | zero avvisi del compilatore (le due correzioni di nullità di Gmail comprese) | 1 ora | basso |
| 8 | C-41 | rendere deterministica la prova della ricerca abbandonata (la caduta "una volta su tre") | 1-2 ore | basso |
| 9 | C-40 | un try per gruppo nella suite, durata per gruppo, nome sconosciuto come errore | mezza giornata | basso |
| 10 | C-25, C-26 | chiudere le attese degli account e l'uscita dopo le azioni delle notifiche anche con un errore | 40 minuti | basso |
| 11 | C-32, C-34, C-35 | revoca di Google controllata, cancellazioni registrate, domini.dat salvato all'uscita, pila dell'errore | 35 minuti | nullo |
| 12 | C-27 | togliere il `Task.Run` di RuleWindow | 5 minuti | basso |
| 13 | C-54 | Rimuovi account con la Posta in uscita di quell'account | 1 ora | basso |
| 14 | C-01, C-02, C-05, C-07 | togliere il modello vecchio della riga, gli interruttori della sonda, i membri mai usati e le allocazioni inutili della riga | 2-3 ore | basso |
| 15 | C-22 | dividere GraphProvider in file per area (spostamenti puri, il primo passo della divisione) | 2,5 ore | molto basso |
| 16 | C-56 | promemoria del calendario che non si perdono quando la lettura non riesce | 2-3 ore | basso |
| 17 | C-43, C-45 | controlli che passano per il motivo sbagliato e server finto che tace per 60 s | mezza giornata | basso |
| 18 | C-10 | `BuildAsync` comune a Gmail e IMAP (primo passo della base comune di C-08) | 2-3 ore | basso |
| 19 | C-39, C-38 | legare le soglie che devono restare coerenti e unire quelle doppie | 2 ore | nullo |
| 20 | C-67, C-69 | CLAUDE.md breve con i file per area, e i dati reali fuori | 1 giorno | medio-basso |

Subito dopo, per beneficio ma con più rischio o più lavoro: C-51 (regole dopo un errore a metà), C-53 (provider chiusi sotto le
richieste), C-08 e C-11 (base comune IMAP), C-19 (divisione di MainWindow), C-28 (lucchetto dei token per account).

## Guardato e trovato a posto

- Eventi statici: `ActionQueue.Changed`, `UnreadSync.Written`, `SenderLogos.Arrived`, `Store.SaveFailed`, `PermissionsLost`,
  `ReadingView.ZoomChanged`, `App.ThemeChanged` sono staccati alla chiusura (UI/MainWindow.xaml.cs:59-68 e 145-151, anche per le finestre
  "una sola cartella"; MessageRowElement.cs:79, 85; MenuCustomizeWindow.cs:60-61); SystemEvents e NetworkChange in `App.OnExit`; i
  gestori che arrivano da altri thread passano da `BeginInvoke` o `CheckAccess`.
- Lucchetti: nessun annidamento in ordine inverso (MicrosoftAuth sempre TokenGate poi AppGate; GoogleAuth Gate poi FileGate; fra i Gate
  delle connessioni IMAP nessuna chiamata annidata). ActionQueue, BodyCache, AddressBook, Pec, `Rules.marks`, `Store.unreadable`,
  `UnreadSync` sono sotto lucchetto. Unica eccezione trovata: `CategoryPalette.Known` (C-27).
- `Dispatcher.Invoke` sincrono: solo in DevTools.
- I Task scartati di GmailProvider.cs:694, GraphProvider.cs:716, PdfWorker.cs:383, MainWindow.Suggestions.cs:218 e 242 (avvisi CA2008):
  le eccezioni sono osservate o registrate.
- `CancellationTokenSource` (CA2000): nessuna perdita vera; quelle per ricerca e apertura non hanno timer né collegamenti, quella di
  SenderLogos.cs:862 si chiude con il `ContinueWith` a 887.
- `.Result` di UI/ReadingView.xaml.cs:319-320: guardato da `IsCompleted`, e `ConversationAsync` non finisce mai in errore: nessun blocco.
- I timer della finestra principale (`snoozeTimer`, `offlineTimer`, `reminderTimer`) esistono solo nella principale, che chiudendosi
  chiude l'app.
- Le sonde `CursorProbe`, `InputProbe`, `WheelRecorder`: nella versione installata non costano (metodi `[Conditional("DEBUG")]`, rami
  `#else` vuoti o costanti). Solo i loro interruttori sono residui (C-02).
- `ScrollHitGuard` e `StallLog` leggono una variabile anche nella versione installata (`MISSIVA_SENZA_SOSPENSIONE`, `MISSIVA_SENZA_STALL`):
  voluto, per i confronti, e innocuo.
- Le apparenti ripetizioni di UI/ContextMenus.cs con sé stesso e di Core/Calendar.cs con Core/GoogleCalendar.cs (`ChangedText`, colori
  dei calendari): stessa forma, contenuti diversi (nomi dei campi di Graph e di Google). Non sono doppioni.
- Impostazioni di `Settings`: tutte le proprietà trovate sono lette e scritte da qualche parte (le quattro dei menu solo da
  UI/ContextMenus.cs:106-137, ma è voluto).
- Argomenti da riga di comando e variabili d'ambiente: tutti quelli citati esistono (C-71).
- I due parametri "inutili" di MailHtml.cs:354, 362 sono imposti dai ganci di AngleSharp.

## Non guardato

- Nulla è stato eseguito su Windows: tempi, memoria, aspetto delle finestre e comportamento a schermo restano da verificare con i passi
  indicati in ogni punto.
- I file XAML oltre al modello della riga (stili, risorse, binding con nomi sbagliati: WPF li segnala solo all'esecuzione).
- La sicurezza: c'è già docs/audit/sicurezza.md con il suo esito; qui solo dove toccava la robustezza (C-32).
- Le prestazioni misurate: non si confrontano numeri presi su Linux con quelli del PC dell'autore, e ESITO.md ha già misurato e lasciato molte
  ottimizzazioni.
- Le funzioni mancanti (docs/audit/funzionalita.md).
- Gli script PowerShell (`installa.ps1`, `disinstalla.ps1`, `strumenti/*.ps1`) solo per i riferimenti; `installa.ps1` con PowerShell 5.1
  resta da provare (C-70).
- Il simulatore dello scorrimento (`strumenti/simulatore-scorrimento.txt`).
- La concordanza di ogni metodo di `IMailProvider` con TestingProvider, metodo per metodo (C-65 propone una prova automatica).
- `SendDraftAsync` riga per riga per il caso di C-61.
- docs/audit/ESITO.md non è stato riletto per intero: lo si è consultato per parole (controlli instabili, misure lasciate) mentre si
  scrivevano i punti; se un punto qui ripete qualcosa che ESITO.md dà per misurato e trascurabile, vale ESITO.md. I rimandi `file:riga`
  dei rapporti di audit non sono stati ricontrollati uno per uno.
